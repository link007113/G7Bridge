namespace G7Bridge.Core;

public enum MissingTelemetryNotice { NoInput, OrdinaryInputOnly }

public sealed class GyroWaitMonitor
{
    private bool warned;
    public MissingTelemetryNotice? Observe(long elapsedMilliseconds,long packets,long gyroPackets)
    {
        if(warned || elapsedMilliseconds<=10_000 || gyroPackets!=0) return null;
        warned=true;
        return packets==0 ? MissingTelemetryNotice.NoInput : MissingTelemetryNotice.OrdinaryInputOnly;
    }
}

public readonly record struct UsbDeviceIdentity(string PhysicalKey,ushort Vendor,ushort Product);

public static class G7SessionSetup
{
    public const int MinimumStableMilliseconds=5_000;
    public const int HandshakeSpacingMilliseconds=20;
    public const int ReenumerationTimeoutMilliseconds=15_000;
    public static byte[][] HandshakePackets()
    {
        const string signature="gamesirapp";
        var packets=new List<byte[]>();
        for(int i=0;i<signature.Length;i+=2) {
            packets.Add([0,8,0,(byte)signature[i],(byte)signature[i+1],0,0,0]);
            if(i+2<signature.Length) packets.Add([0,8,0,0,0,0,0,0]);
        }
        return packets.ToArray();
    }
    public static UsbDeviceIdentity? FindTransitionTarget(string originalKey,IReadOnlyList<UsbDeviceIdentity> devices)
    {
        if(string.IsNullOrWhiteSpace(originalKey) || originalKey.Contains(":address-",StringComparison.Ordinal))
            throw new ArgumentException("Een stabiel fysiek USB-poortpad is nodig voor een modeswitch.");
        var matches=devices.Where(d=>d.PhysicalKey==originalKey && G7DeviceCatalog.IsSessionIdentity(d.Vendor,d.Product)).ToArray();
        return matches.Length==1 ? matches[0] : null;
    }
}
