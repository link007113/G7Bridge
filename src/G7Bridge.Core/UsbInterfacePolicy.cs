namespace G7Bridge.Core;

public readonly record struct UsbEndpointInfo(byte Address,byte TransferType,ushort MaxPacketSize,byte Interval);
public sealed record UsbInterfaceInfo(byte Number,byte Alternate,byte Class,byte SubClass,byte Protocol,IReadOnlyList<UsbEndpointInfo> Endpoints);

public static class UsbInterfacePolicy
{
    public static bool SupportsG7(UsbInterfaceInfo descriptor)
    {
        if(descriptor.Number!=0 || descriptor.Alternate!=0 || descriptor.Class!=0xFF) return false;
        bool input=false,output=false;
        foreach(var endpoint in descriptor.Endpoints) {
            // wMaxPacketSize describes a USB transaction, not the length of a
            // logical GameSir report. The observed 100A interface uses 32-byte
            // transactions; the 64-byte transfer buffers remain valid multiples.
            if(endpoint.TransferType!=3 || endpoint.MaxPacketSize is not (32 or 64))continue;
            input|=endpoint.Address==0x82;output|=endpoint.Address==0x02;
        }
        return input&&output;
    }

    public static string Describe(UsbInterfaceInfo descriptor)
        =>$"IF {descriptor.Number}, alt {descriptor.Alternate}, klasse {descriptor.Class:X2}/{descriptor.SubClass:X2}/{descriptor.Protocol:X2}: "+
          string.Join("; ",descriptor.Endpoints.Select(e=>$"EP {e.Address:X2}, type {e.TransferType}, maxPacket {e.MaxPacketSize}, interval {e.Interval}"));
}
