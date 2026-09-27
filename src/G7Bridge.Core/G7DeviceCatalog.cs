namespace G7Bridge.Core;

public static class G7DeviceCatalog
{
    public const ushort Vendor=0x3537;
    public const ushort XInputProduct=0x100A;
    // 106B was observed after the documented handshake on Anthony's original
    // dongle, on the same physical port; no guess based on adjacent product IDs.
    public static bool IsSessionIdentity(ushort vendor,ushort product)
        =>vendor==Vendor && product is 0x106B or 0x109B or 0x109C;
    public static bool IsSupported(ushort vendor,ushort product)
        =>vendor==Vendor && (product==XInputProduct || IsSessionIdentity(vendor,product));
}
