using G7Bridge.Core;

namespace G7Bridge.Windows;

internal sealed record ControllerDevice(string Key,string Name,ushort ProductId,string Transport,string PhysicalKey="")
{
    public override string ToString()=>Name;
}

internal interface IControllerTransport : IDisposable
{
    bool Exclusive { get; }
    int Read(byte[] data,CancellationToken token);
    void Write(byte[] data);
}

internal sealed class UsbTransferException(string action,int errorCode,string nativeError)
    : IOException($"{action}: {nativeError} ({errorCode}).")
{
    public int ErrorCode { get; }=errorCode;
}

internal static class DeviceScope
{
    public static bool Allowed(ushort vid,ushort pid)=>G7DeviceCatalog.IsSupported(vid,pid);
}
