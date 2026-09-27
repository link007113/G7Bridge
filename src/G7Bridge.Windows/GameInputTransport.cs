using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using G7Bridge.Core;

namespace G7Bridge.Windows;

internal sealed class GameInputTransport : IControllerTransport
{
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]
    private struct NativeInfo
    {
        public ushort Vendor,Product;
        public uint Family,Kinds;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=512)] public string Key;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=65)] public string Id;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=128)] public string Name;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=512)] public string Reports;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct NativePad { public uint Buttons; public float LT,RT,LX,LY,RX,RY; }
    private const string Library="G7Bridge.Native.dll";
    [DllImport(Library,CallingConvention=CallingConvention.Cdecl)] private static extern int g7_rumble(IntPtr handle,ushort left,ushort right);
    [DllImport(Library,CallingConvention=CallingConvention.Cdecl,CharSet=CharSet.Unicode,ExactSpelling=true)] private static extern int g7_device_instance(IntPtr handle,StringBuilder instance,uint capacity);
    [DllImport(Library,CallingConvention=CallingConvention.Cdecl)] private static extern int g7_enumerate([Out] NativeInfo[] items,uint capacity,out uint count);
    [DllImport(Library,CallingConvention=CallingConvention.Cdecl,CharSet=CharSet.Unicode,ExactSpelling=true)] private static extern int g7_open_v2(string id,out IntPtr handle,out NativeInfo info,out uint stage);
    [DllImport(Library,CallingConvention=CallingConvention.Cdecl)] private static extern void g7_close(IntPtr handle);
    [DllImport(Library,CallingConvention=CallingConvention.Cdecl)] private static extern int g7_read(IntPtr handle,[Out] byte[] data,uint capacity,out uint id,out uint size,out ulong time,out ulong dropped);
    [DllImport(Library,CallingConvention=CallingConvention.Cdecl)] private static extern int g7_pad(IntPtr handle,out NativePad pad);
    [DllImport(Library,CallingConvention=CallingConvention.Cdecl)] private static extern int g7_send_session(IntPtr handle,byte[] data,uint size,out uint stage,out uint capacity);
    [DllImport(Library,CallingConvention=CallingConvention.Cdecl)] private static extern int g7_mode_piece(IntPtr handle,byte left,byte right);
    [DllImport(Library,CallingConvention=CallingConvention.Cdecl)] private static extern int g7_prepare_vendor(IntPtr handle);
    [DllImport(Library,CallingConvention=CallingConvention.Cdecl,CharSet=CharSet.Unicode,ExactSpelling=true)] private static extern void g7_vendor_status(IntPtr handle,StringBuilder text,uint capacity);

    private IntPtr handle;
    private readonly Action<string> log;
    private readonly byte[] raw=new byte[64];
    private readonly HashSet<(uint,uint)> seenReports=[];
    private readonly Stopwatch clock=Stopwatch.StartNew();
    private long nextPad;
    private ulong lastDropped;
    private long nextVendorStatus;
    private string? lastVendorStatus;
    public bool Exclusive=>false;
    public string PhysicalKey { get; }
    public string InstanceId { get; }

    public static IReadOnlyList<ControllerDevice> Enumerate()
    {
        var items=new NativeInfo[16];Check(g7_enumerate(items,16,out uint count),"GameInput-apparaten zoeken",runtimeOperation:true);
        return items.Take((int)count).Where(d=>G7DeviceCatalog.IsSupported(d.Vendor,d.Product)).Select(d=>
            new ControllerDevice(d.Id,$"GameSir {d.Vendor:X4}:{d.Product:X4} — {(d.Id.StartsWith("GIP:",StringComparison.Ordinal)?"direct GIP input":"Windows GameInput")}",d.Product,"GameInput",d.Key)).ToArray();
    }
    public GameInputTransport(ControllerDevice device,Action<string> logger)
    {
        log=logger;
        int result=g7_open_v2(device.Key,out handle,out var info,out uint stage);
        string step=stage switch {1=>"starting runtime",2=>"waiting for device list and selected device",3=>"reading device information",4=>"registering raw input",_=>"opening input"};
        Check(result,"Opening GameInput channel / "+step,runtimeOperation:stage==1);
        PhysicalKey=info.Key;
        var instance=new StringBuilder(1024);
        InstanceId=g7_device_instance(handle,instance,1024)>=0?instance.ToString():"";
        if(info.Product!=device.ProductId || !G7DeviceCatalog.IsSupported(info.Vendor,info.Product) ||
            (!string.IsNullOrWhiteSpace(device.PhysicalKey) && info.Key!=device.PhysicalKey)) {
            Dispose();throw new IOException("The selected GameInput device has changed. Reconnect the controller.");
        }
        log($"Windows GameInput opened: {info.Vendor:X4}:{info.Product:X4}, family={info.Family}, kinds=0x{info.Kinds:X}.");
        log("Background input for controller, Guide and Share requested. The Microsoft Xbox driver remains active.");
        log("GameInput reports: "+info.Reports);
        log("Physical USB location: "+(string.IsNullOrWhiteSpace(PhysicalKey)?"unknown — automatic mode switch refused":PhysicalKey));
        log(device.Key.StartsWith("GIP:",StringComparison.Ordinal)?
            "Windows GIP: axes from GamepadReading and original pad reports; physical buttons and gyro from device telemetry.":
            "Diagnostics: GIP headers and ordinary pad frames are reconstructed from GameInput; only raw payload is unmodified device input.");
        if(G7DeviceCatalog.IsSessionIdentity(info.Vendor,info.Product)) {
            int prepared=g7_prepare_vendor(handle);
            log($"Preparing additional GameSir channel: 0x{prepared:X8}."+(prepared<0?" Ordinary input remains available.":""));
            LogVendorStatus();
        }
    }
    public int Read(byte[] data,CancellationToken token)
    {
        for(int attempt=0;attempt<8;attempt++) {
            token.ThrowIfCancellationRequested();
            if(clock.ElapsedMilliseconds>=nextVendorStatus){nextVendorStatus=clock.ElapsedMilliseconds+500;LogVendorStatus();}
            if(clock.ElapsedMilliseconds>=nextPad) {
                nextPad=clock.ElapsedMilliseconds+8;
                int padResult=g7_pad(handle,out var p);Check(padResult,"GameInput controller state");
                if(padResult==0) {
                    var packet=GameInputPackets.Gamepad(p.Buttons,p.LT,p.RT,p.LX,p.LY,p.RX,p.RY);
                    packet.CopyTo(data,0);return packet.Length;
                }
            }
            int result=g7_read(handle,raw,(uint)raw.Length,out var id,out var size,out _,out var dropped);
            Check(result,"Reading GameInput report");
            if(dropped!=lastDropped) {lastDropped=dropped;log($"GameInput: {dropped} stale/overflow queue packets discarded.");}
            if(result==0) {
                if(seenReports.Add((id,size)))log($"GameInput raw id=0x{id:X2}, {size} bytes: {Convert.ToHexString(raw.AsSpan(0,(int)size))}");
                var packet=GameInputPackets.Normalize(id,raw.AsSpan(0,(int)size));
                if(packet is not null){packet.CopyTo(data,0);return packet.Length;}
            } else {
                if(token.WaitHandle.WaitOne(4)) token.ThrowIfCancellationRequested();
            }
        }
        return 0;
    }
    public void Write(byte[] data)
    {
        if(handle==IntPtr.Zero)throw new ObjectDisposedException(nameof(GameInputTransport));
        if(data.Length==8 && data[0]==0 && data[1]==8 && data[2]==0 && data.AsSpan(5).IndexOfAnyExcept((byte)0)<0) {
            Check(g7_mode_piece(handle,data[3],data[4]),"Requesting GameSir startup part through Windows");return;
        }
        if(data.Length is 5 or 6 && data[0]==0x0F && data[1]==0 && data[3]==data.Length-4) {
            var payload=data[4..];int result=g7_send_session(handle,payload,(uint)payload.Length,out uint stage,out uint capacity);
            Check(result,$"GameSir session message through Windows (stage {stage}: 1=create report, 2=fill payload, 3=send, 10=prepare GIP provider, 11=direct GIP send; buffer={capacity}, payload={payload.Length})");return;
        }
        throw new IOException("Unknown outgoing GameSir message refused.");
    }
    public static bool IsDisconnected(IOException ex)=>GameInputErrors.IsDisconnected(ex.HResult);
    public void SetRumble(uint motors)=>Check(g7_rumble(handle,(ushort)motors,(ushort)(motors>>16)),"Controller rumble");
    private void LogVendorStatus()
    {
        var text=new StringBuilder(768);g7_vendor_status(handle,text,768);
        string value=text.ToString();int message=value.IndexOf("; last message",StringComparison.Ordinal);
        string state=message<0?value:value[..message];if(state==lastVendorStatus)return;
        lastVendorStatus=state;log("GameSir channel: "+value);
    }
    private static void Check(int result,string action,bool runtimeOperation=false)
    {
        if(result>=0)return;
        string message=$"{action}: 0x{result:X8}. "+GameInputErrors.RecoveryHint(result,runtimeOperation);
        throw new IOException(message,result);
    }
    public void Dispose(){if(handle!=IntPtr.Zero){g7_close(handle);handle=IntPtr.Zero;}}
}
