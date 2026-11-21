namespace G7Bridge.Core;

public static class G7DeviceCatalog
{
    public const ushort Vendor=0x3537;
    public const ushort XInputProduct=0x100A;
    public const ushort XboxModeProduct=0x1004;

    // 106B was observed after the documented handshake on Anthony's original dongle.
    // 1004 is shared with the T4 Kaleid, so it requires a verified GIP USB root.
    public static bool IsSessionIdentity(ushort vendor,ushort product,bool verifiedGip=false)
        =>vendor==Vendor && (product is 0x106B or 0x109B or 0x109C || product==XboxModeProduct && verifiedGip);
    public static bool IsSupported(ushort vendor,ushort product,bool verifiedGip=false)
        =>vendor==Vendor && (product==XInputProduct || IsSessionIdentity(vendor,product,verifiedGip));
}
