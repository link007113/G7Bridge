using System.Runtime.InteropServices;
using G7Bridge.Core;

namespace G7Bridge.Windows;

internal sealed class UsbTransport : IControllerTransport
{
    private IntPtr context,handle;
    private bool claimed;
    public bool Exclusive=>true;

    private static IntPtr CreateContext()
    {
        Check(Native.libusb_init(out var ctx),"USB-bibliotheek starten");
        int result=Native.libusb_set_option(ctx,1); // LIBUSB_OPTION_USE_USBDK, before enumeration.
        if(result<0) {
            string nativeError=Marshal.PtrToStringAnsi(Native.libusb_error_name(result))??"onbekend";
            Native.libusb_exit(ctx);
            throw new IOException($"UsbDk ontbreekt of kon niet worden geladen ({nativeError}, {result}). Installeer UsbDk via de knop in G7 Bridge. De Xbox-driver wordt niet vervangen.");
        }
        return ctx;
    }
    public static IReadOnlyList<ControllerDevice> Enumerate(bool includeUnknownGameSir=false)
    {
        var ctx=CreateContext();
        try {
            var result=new List<ControllerDevice>();
            Visit(ctx,(dev,d)=>{
                if(DeviceScope.Allowed(d.Vendor,d.Product) || (includeUnknownGameSir && d.Vendor==0x3537)) {
                    var key=Key(dev);
                    result.Add(new(key,$"GameSir {d.Product:X4} — USB {key}",d.Product,"UsbDk"));
                }
            });
            return result;
        } finally { Native.libusb_exit(ctx); }
    }

    public UsbTransport(ControllerDevice target,Action<string>? log=null)
    {
        try {
            context=CreateContext();
            Visit(context,(dev,d)=>{
                if(handle!=IntPtr.Zero || d.Product!=target.ProductId || !DeviceScope.Allowed(d.Vendor,d.Product) || Key(dev)!=target.Key) return;
                ValidateInterface(dev,log);
                Check(Native.libusb_open(dev,out handle),"GameSir-ontvanger openen");
            });
            if(handle==IntPtr.Zero) throw new IOException("De gekozen GameSir-ontvanger is niet meer aanwezig.");
            Check(Native.libusb_claim_interface(handle,0),"GameSir-interface tijdelijk overnemen");
            claimed=true;
        } catch { Dispose();throw; }
    }

    private static void ValidateInterface(IntPtr dev,Action<string>? log)
    {
        Check(Native.libusb_get_active_config_descriptor(dev,out var cfg),"USB-interface lezen");
        try {
            // libusb's public, pointer-aligned descriptor ABI (x64 package only).
            int count=Marshal.ReadByte(cfg,4);var interfaces=Marshal.ReadIntPtr(cfg,16);
            bool supported=false;
            var descriptions=new List<string>();
            for(int i=0;i<count;i++) {
                var iface=IntPtr.Add(interfaces,i*16); var alt=Marshal.ReadIntPtr(iface);int nAlt=Marshal.ReadInt32(iface,8);
                for(int j=0;j<nAlt;j++) {
                    var desc=IntPtr.Add(alt,j*40);
                    int endpoints=Marshal.ReadByte(desc,4);var ep=Marshal.ReadIntPtr(desc,16);
                    var endpointInfo=new List<UsbEndpointInfo>();
                    for(int k=0;k<endpoints;k++) {
                        var p=IntPtr.Add(ep,k*32);int address=Marshal.ReadByte(p,2),kind=Marshal.ReadByte(p,3)&3;
                        endpointInfo.Add(new((byte)address,(byte)kind,(ushort)(Marshal.ReadInt16(p,4)&0x7FF),Marshal.ReadByte(p,6)));
                    }
                    var info=new UsbInterfaceInfo(Marshal.ReadByte(desc,2),Marshal.ReadByte(desc,3),Marshal.ReadByte(desc,5),Marshal.ReadByte(desc,6),Marshal.ReadByte(desc,7),endpointInfo);
                    string description=UsbInterfacePolicy.Describe(info);
                    descriptions.Add(description);log?.Invoke("USB-beschrijving: "+description);
                    supported|=UsbInterfacePolicy.SupportsG7(info);
                }
            }
            if(supported) {
                log?.Invoke("GameSir-interface aanvaard. USB-pakketgrootte en 64-byte protocolbuffer worden afzonderlijk behandeld.");
                return;
            }
            throw new IOException("Geen passende GameSir-interface 0 met interrupt-endpoints 02/82 (USB-pakketten van 32 of 64 bytes). Gevonden: "+string.Join(" | ",descriptions));
        } finally { Native.libusb_free_config_descriptor(cfg); }
    }

    public int Read(byte[] data,CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        int result=Native.libusb_interrupt_transfer(handle,0x82,data,data.Length,out var count,100);
        if(result==-7) return count; // timeout can contain a partial transfer; decoder checks length.
        Check(result,"GameSir-invoer lezen");return count;
    }
    public void Write(byte[] data)
    {
        Check(Native.libusb_interrupt_transfer(handle,0x02,data,data.Length,out var count,1000),"GameSir-sessiepakket versturen");
        if(count!=data.Length) throw new IOException($"GameSir ontving een onvolledig sessiepakket ({count}/{data.Length} bytes).");
    }
    public void Dispose()
    {
        if(handle!=IntPtr.Zero) { if(claimed) Native.libusb_release_interface(handle,0);Native.libusb_close(handle);handle=IntPtr.Zero;claimed=false; }
        if(context!=IntPtr.Zero) { Native.libusb_exit(context);context=IntPtr.Zero; }
    }
    private static string Key(IntPtr dev)
    {
        var ports=new byte[8];int count=Native.libusb_get_port_numbers(dev,ports,ports.Length);
        if(count<=0) return $"{Native.libusb_get_bus_number(dev)}:address-{Native.libusb_get_device_address(dev)}";
        return $"{Native.libusb_get_bus_number(dev)}:{string.Join('.',ports[..count])}";
    }
    private static void Visit(IntPtr ctx,Action<IntPtr,Native.Descriptor> action)
    {
        long count=Native.libusb_get_device_list(ctx,out var list).ToInt64();
        if(count<0) { Check((int)count,"USB-apparaten opsommen");return; }
        try { for(int i=0;i<count;i++) { var dev=Marshal.ReadIntPtr(list,i*IntPtr.Size);if(Native.libusb_get_device_descriptor(dev,out var d)==0) action(dev,d); } }
        finally { Native.libusb_free_device_list(list,1); }
    }
    private static void Check(int result,string action)
    { if(result<0) throw new UsbTransferException(action,result,Marshal.PtrToStringAnsi(Native.libusb_error_name(result))??"onbekend"); }

    private static class Native
    {
        private const string Dll="libusb-1.0.dll";
        [StructLayout(LayoutKind.Sequential,Pack=1)] internal struct Descriptor {
            public byte Length,Type;public ushort Usb;public byte Class,SubClass,Protocol,PacketSize;
            public ushort Vendor,Product,Device;public byte Manufacturer,ProductString,Serial,Configurations;
        }
        [DllImport(Dll)] internal static extern int libusb_init(out IntPtr ctx);
        [DllImport(Dll)] internal static extern int libusb_set_option(IntPtr ctx,int option);
        [DllImport(Dll)] internal static extern void libusb_exit(IntPtr ctx);
        [DllImport(Dll)] internal static extern IntPtr libusb_get_device_list(IntPtr ctx,out IntPtr list);
        [DllImport(Dll)] internal static extern void libusb_free_device_list(IntPtr list,int unref);
        [DllImport(Dll)] internal static extern int libusb_get_device_descriptor(IntPtr dev,out Descriptor descriptor);
        [DllImport(Dll)] internal static extern int libusb_get_active_config_descriptor(IntPtr dev,out IntPtr config);
        [DllImport(Dll)] internal static extern void libusb_free_config_descriptor(IntPtr config);
        [DllImport(Dll)] internal static extern int libusb_get_port_numbers(IntPtr dev,byte[] ports,int max);
        [DllImport(Dll)] internal static extern byte libusb_get_bus_number(IntPtr dev);
        [DllImport(Dll)] internal static extern byte libusb_get_device_address(IntPtr dev);
        [DllImport(Dll)] internal static extern int libusb_open(IntPtr dev,out IntPtr handle);
        [DllImport(Dll)] internal static extern void libusb_close(IntPtr handle);
        [DllImport(Dll)] internal static extern int libusb_claim_interface(IntPtr handle,int iface);
        [DllImport(Dll)] internal static extern int libusb_release_interface(IntPtr handle,int iface);
        [DllImport(Dll)] internal static extern int libusb_interrupt_transfer(IntPtr handle,byte endpoint,byte[] data,int length,out int transferred,uint timeout);
        [DllImport(Dll)] internal static extern IntPtr libusb_error_name(int error);
    }
}
