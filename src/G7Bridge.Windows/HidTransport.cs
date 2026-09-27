using Microsoft.Win32.SafeHandles;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace G7Bridge.Windows;

internal sealed class HidTransport : IControllerTransport
{
    private readonly FileStream stream;
    public bool Exclusive=>false;
    private static Guid hidGuid=new("4D1E55B2-F16F-11CF-88CB-001111000030");

    public static IReadOnlyList<ControllerDevice> Enumerate()
    {
        var devices=new List<ControllerDevice>();
        if(CM_Get_Device_Interface_List_SizeW(out var length,ref hidGuid,null,0)!=0 || length<2) return devices;
        var chars=new char[length];
        if(CM_Get_Device_Interface_ListW(ref hidGuid,null,chars,length,0)!=0) return devices;
        foreach(var path in new string(chars).Split('\0',StringSplitOptions.RemoveEmptyEntries))
        {
            if(!path.Contains("vid_3537",StringComparison.OrdinalIgnoreCase)) continue;
            using var handle=CreateFileW(path,0,3,IntPtr.Zero,3,0,IntPtr.Zero);
            if(handle.IsInvalid) continue;
            var attr=new Attributes { Size=Marshal.SizeOf<Attributes>() };
            if(!HidD_GetAttributes(handle,ref attr) || !DeviceScope.Allowed(attr.Vendor,attr.Product)) continue;
            if(!HidD_GetPreparsedData(handle,out var pp)) continue;
            var caps=new byte[64];
            try { if(HidP_GetCaps(pp,caps)!=0x110000) continue; }
            finally { HidD_FreePreparsedData(pp); }
            int usagePage=BitConverter.ToUInt16(caps,2),input=BitConverter.ToUInt16(caps,4),output=BitConverter.ToUInt16(caps,6);
            if(usagePage<0xFF00 || input!=64 || output!=64) continue;
            devices.Add(new(path,$"GameSir {attr.Product:X4} — vendor-HID (uitlezen)",attr.Product,"HID"));
        }
        return devices;
    }

    public HidTransport(ControllerDevice device)
    {
        // Require an exact enumerated vendor collection; never open keyboard/mouse collections.
        if(!Enumerate().Any(d=>d.Key==device.Key)) throw new IOException("Het gekozen GameSir-kanaal is niet meer aanwezig.");
        var handle=CreateFileW(device.Key,0xC0000000,3,IntPtr.Zero,3,0x40000000,IntPtr.Zero);
        if(handle.IsInvalid) { int e=Marshal.GetLastWin32Error();handle.Dispose();throw new Win32Exception(e); }
        try { stream=new FileStream(handle,FileAccess.ReadWrite,64,true); }
        catch { handle.Dispose();throw; }
    }

    public int Read(byte[] data,CancellationToken token)
    {
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(100);
        try { return stream.ReadAsync(data.AsMemory(),timeout.Token).AsTask().GetAwaiter().GetResult(); }
        catch(OperationCanceledException) when(!token.IsCancellationRequested) { return 0; }
    }
    public void Write(byte[] data)
    {
        using var timeout=new CancellationTokenSource(1000);
        stream.WriteAsync(data.AsMemory(),timeout.Token).AsTask().GetAwaiter().GetResult();
    }
    public void Dispose()=>stream.Dispose();

    [StructLayout(LayoutKind.Sequential)] private struct Attributes { public int Size;public ushort Vendor,Product,Version; }
    [DllImport("cfgmgr32.dll",CharSet=CharSet.Unicode)] private static extern int CM_Get_Device_Interface_List_SizeW(out uint len,ref Guid guid,string? device,uint flags);
    [DllImport("cfgmgr32.dll",CharSet=CharSet.Unicode)] private static extern int CM_Get_Device_Interface_ListW(ref Guid guid,string? device,char[] buffer,uint length,uint flags);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] private static extern SafeFileHandle CreateFileW(string name,uint access,uint share,IntPtr sa,uint creation,uint flags,IntPtr template);
    [DllImport("hid.dll")] private static extern bool HidD_GetAttributes(SafeFileHandle h,ref Attributes a);
    [DllImport("hid.dll")] private static extern bool HidD_GetPreparsedData(SafeFileHandle h,out IntPtr pp);
    [DllImport("hid.dll")] private static extern bool HidD_FreePreparsedData(IntPtr pp);
    [DllImport("hid.dll")] private static extern int HidP_GetCaps(IntPtr pp,byte[] caps);
}
