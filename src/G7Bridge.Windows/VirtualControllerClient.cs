using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using G7Bridge.Core;
using Microsoft.Win32.SafeHandles;
using L = G7Bridge.Core.UiLanguage;

namespace G7Bridge.Windows;

internal readonly record struct VirtualControllerObservation(VirtualInputSnapshot Input, bool CanRumble, string? Error);

// A normal-user HID client of our virtual device. It never opens the physical G7
// or sends setup/configuration commands. Rumble is written only after a GUI click.
internal sealed class VirtualControllerClient
{
    private readonly object sync=new();
    private readonly VirtualInputMonitor input=new();
    private readonly CancellationTokenSource shutdown=new();
    private readonly SemaphoreSlim rumbleGate=new(1,1);
    private readonly Task worker;
    private CancellationTokenSource? connectionStop;
    private Device? current;
    private bool enabled;
    private string? error;
    private Task? stopping;

    internal VirtualControllerClient() => worker=Task.Run(ReadLoop);

    internal VirtualControllerObservation Observation
    {
        get {lock(sync) {
            var state=enabled?input.Snapshot(Environment.TickCount64):default;
            return new(state,state.Fresh && current?.OutputBytes is >=10 and <=64,error);
        }}
    }

    internal void SetEnabled(bool value)
    {
        CancellationTokenSource? cancel=null;
        lock(sync) {
            if(enabled==value)return;
            enabled=value;
            if(!value){input.Clear();error=null;cancel=connectionStop;}
        }
        Cancel(cancel);
    }

    internal async Task TestRumbleAsync()
    {
        if(!await rumbleGate.WaitAsync(0).ConfigureAwait(false))
            throw new InvalidOperationException(L.Text("A rumble test is already running.","Er loopt al een triltest."));
        try {
            Device target;CancellationToken connection;
            lock(sync) {
                if(!enabled || current is null || connectionStop is null || !input.Snapshot(Environment.TickCount64).Fresh)
                    throw new IOException(L.Text("Wait for live input from the virtual controller.","Wacht op live invoer van de virtuele controller."));
                target=current;connection=connectionStop.Token;
            }
            using var cancel=CancellationTokenSource.CreateLinkedTokenSource(shutdown.Token,connection);
            cancel.CancelAfter(1500);
            cancel.Token.ThrowIfCancellationRequested();
            using var output=target.Open(write:true);
            await RumbleTest.RunAsync(async (strength,token)=>{
                using var writeTimeout=CancellationTokenSource.CreateLinkedTokenSource(token);
                writeTimeout.CancelAfter(200);
                await output.WriteAsync(RumbleTest.Report(strength,target.OutputBytes),writeTimeout.Token).ConfigureAwait(false);
            },cancel.Token).ConfigureAwait(false);
        } finally {rumbleGate.Release();}
    }

    private async Task ReadLoop()
    {
        var stop=shutdown.Token;
        while(!stop.IsCancellationRequested) {
            CancellationTokenSource? connection=null;
            try {
                bool active;lock(sync)active=enabled;
                if(!active){await Task.Delay(200,stop).ConfigureAwait(false);continue;}
                var device=Device.Find();
                if(device is null){await Task.Delay(500,stop).ConfigureAwait(false);continue;}
                using var stream=device.Open(write:false);
                connection=CancellationTokenSource.CreateLinkedTokenSource(stop);
                lock(sync) {
                    if(!enabled)continue;
                    input.Clear();current=device;connectionStop=connection;error=null;
                }
                var report=new byte[54];
                while(!connection.IsCancellationRequested) {
                    using var readTimeout=CancellationTokenSource.CreateLinkedTokenSource(connection.Token);
                    readTimeout.CancelAfter(500);
                    int count;
                    try {count=await stream.ReadAsync(report,readTimeout.Token).ConfigureAwait(false);}
                    catch(OperationCanceledException) when(!connection.IsCancellationRequested){continue;}
                    if(count==0)throw new IOException(L.Text("The virtual controller disconnected.","De virtuele controller is losgekoppeld."));
                    lock(sync)input.Accept(report.AsSpan(0,count),Environment.TickCount64);
                }
            } catch(OperationCanceledException) { }
            catch(Exception ex) when(ex is IOException or Win32Exception or UnauthorizedAccessException or InvalidOperationException) {
                lock(sync)error=ex.Message;
            } finally {
                lock(sync){current=null;connectionStop=null;input.Clear();}
                Cancel(connection);connection?.Dispose();
            }
            try {await Task.Delay(500,stop).ConfigureAwait(false);}catch(OperationCanceledException){break;}
        }
    }

    internal Task StopAsync()
    {
        lock(sync)return stopping??=Task.Run(async()=>{
            SetEnabled(false);shutdown.Cancel();
            await worker.ConfigureAwait(false);
            await rumbleGate.WaitAsync().ConfigureAwait(false);rumbleGate.Release();
        });
    }
    private static void Cancel(CancellationTokenSource? source)
    {
        try {source?.Cancel();}catch(ObjectDisposedException){ }
    }

    private sealed record Device(string Path, int OutputBytes)
    {
        internal static Device? Find()
        {
            Guid guid=new("4D1E55B2-F16F-11CF-88CB-001111000030");
            if(CM_Get_Device_Interface_List_SizeW(out uint size,ref guid,null,0)!=0 || size is <2 or >1_000_000)return null;
            var paths=new char[size];
            if(CM_Get_Device_Interface_ListW(ref guid,null,paths,size,0)!=0)return null;
            Device? selected=null;
            foreach(var path in new string(paths).Split('\0',StringSplitOptions.RemoveEmptyEntries)) {
                if(!path.Contains("vid_28de&pid_1302",StringComparison.OrdinalIgnoreCase))continue;
                using var meta=CreateFileW(path,0,3,IntPtr.Zero,3,0,IntPtr.Zero);
                if(meta.IsInvalid || !Inspect(meta,out int output))continue;
                if(selected is not null)throw new IOException(L.Text("More than one G7 Bridge virtual controller was found.","Er is meer dan één virtuele G7 Bridge-controller gevonden."));
                selected=new(path,output);
            }
            return selected;
        }

        internal FileStream Open(bool write)
        {
            if(write && (OutputBytes is <10 or >64))throw new IOException(L.Text("The virtual controller has no supported rumble output.","De virtuele controller heeft geen ondersteunde triluitvoer."));
            var handle=CreateFileW(Path,write?0x40000000u:0x80000000u,3,IntPtr.Zero,3,0x40000000,IntPtr.Zero);
            if(handle.IsInvalid){int code=Marshal.GetLastWin32Error();handle.Dispose();throw new Win32Exception(code);}
            try {
                // Recheck the opened handle, not just a previously enumerated path.
                if(!Inspect(handle,out int output) || (write && output!=OutputBytes))
                    throw new IOException(L.Text("The virtual controller identity changed.","De identiteit van de virtuele controller is veranderd."));
                return new FileStream(handle,write?FileAccess.Write:FileAccess.Read,1,true);
            } catch {handle.Dispose();throw;}
        }

        private static bool Inspect(SafeFileHandle handle,out int output)
        {
            output=0;
            var attributes=new Attributes{Size=Marshal.SizeOf<Attributes>()};
            if(!HidD_GetAttributes(handle,ref attributes) || attributes.Vendor!=0x28DE || attributes.Product!=0x1302)return false;
            var serial=new byte[512];
            if(!HidD_GetSerialNumberString(handle,serial,(uint)serial.Length))return false;
            if(!HidD_GetPreparsedData(handle,out var pp))return false;
            var caps=new byte[64];
            try {if(HidP_GetCaps(pp,caps)!=0x110000)return false;}
            finally {HidD_FreePreparsedData(pp);}
            if(!VirtualControllerIdentity.Matches(attributes.Vendor,attributes.Product,BitConverter.ToUInt16(caps,2),BitConverter.ToUInt16(caps,4),Encoding.Unicode.GetString(serial).TrimEnd('\0')))return false;
            output=BitConverter.ToUInt16(caps,6);return true;
        }
    }

    [StructLayout(LayoutKind.Sequential)] private struct Attributes {public int Size;public ushort Vendor,Product,Version;}
    [DllImport("cfgmgr32.dll",CharSet=CharSet.Unicode)] private static extern int CM_Get_Device_Interface_List_SizeW(out uint length,ref Guid guid,string? device,uint flags);
    [DllImport("cfgmgr32.dll",CharSet=CharSet.Unicode)] private static extern int CM_Get_Device_Interface_ListW(ref Guid guid,string? device,[Out]char[] paths,uint length,uint flags);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] private static extern SafeFileHandle CreateFileW(string path,uint access,uint share,IntPtr security,uint creation,uint flags,IntPtr template);
    [DllImport("hid.dll")][return:MarshalAs(UnmanagedType.U1)] private static extern bool HidD_GetAttributes(SafeFileHandle handle,ref Attributes attributes);
    [DllImport("hid.dll")][return:MarshalAs(UnmanagedType.U1)] private static extern bool HidD_GetSerialNumberString(SafeFileHandle handle,[Out]byte[] buffer,uint length);
    [DllImport("hid.dll")][return:MarshalAs(UnmanagedType.U1)] private static extern bool HidD_GetPreparsedData(SafeFileHandle handle,out IntPtr data);
    [DllImport("hid.dll")][return:MarshalAs(UnmanagedType.U1)] private static extern bool HidD_FreePreparsedData(IntPtr data);
    [DllImport("hid.dll")] private static extern int HidP_GetCaps(IntPtr data,[Out]byte[] caps);
}
