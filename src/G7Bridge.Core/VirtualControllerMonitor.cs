using System.Buffers.Binary;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace G7Bridge.Core;

public static class VirtualControllerIdentity
{
    public const string Key = "grimm-g7bridge-triton-v1";
    // HIDMaestro 1.9 DeviceIdentity: the low 48 bits of the first big-endian
    // SHA-256 word. Keep the existing key so Steam profiles keep their identity.
    public static string UsbSerial { get; } = "HM" + Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes("HIDMaestro.DeviceIdentity.v1\n" + Key)).AsSpan(2,6));
    public static bool Matches(ushort vendor, ushort product, ushort usagePage, int inputBytes, string? serial) =>
        vendor==0x28DE && product==0x1302 && usagePage==0xFF00 && inputBytes==54 &&
        string.Equals(serial,UsbSerial,StringComparison.OrdinalIgnoreCase);
}

public readonly record struct VirtualPadState(PadButtons Buttons, short LX, short LY, short RX, short RY, ushort LT, ushort RT)
{
    public static float Axis(short value) => value < 0 ? value / 32768f : value / 32767f;
    public static float Trigger(ushort value) => Math.Clamp(value / 32767f, 0, 1);
}

public readonly record struct VirtualInputSnapshot(bool Fresh, VirtualPadState Pad, long Reports, PadButtons LastPressed, MotionState Motion=default);

public sealed class VirtualInputMonitor
{
    private VirtualPadState pad;
    private MotionState motion;
    private PadButtons lastPressed;
    private long receivedAt=-1, reports;
    public bool Accept(ReadOnlySpan<byte> report, long now)
    {
        if(report.Length!=54 || report[0]!=0x42)return false;
        var buttons=(PadButtons)(BinaryPrimitives.ReadUInt32LittleEndian(report[2..]) & 0x000FFFFF);
        var pressed=buttons & ~pad.Buttons;
        if(pressed!=PadButtons.None)lastPressed=pressed;
        pad=new(buttons,
            BinaryPrimitives.ReadInt16LittleEndian(report[10..]),BinaryPrimitives.ReadInt16LittleEndian(report[12..]),
            BinaryPrimitives.ReadInt16LittleEndian(report[14..]),BinaryPrimitives.ReadInt16LittleEndian(report[16..]),
            BinaryPrimitives.ReadUInt16LittleEndian(report[6..]),BinaryPrimitives.ReadUInt16LittleEndian(report[8..]));
        motion=new(BinaryPrimitives.ReadInt16LittleEndian(report[40..]),BinaryPrimitives.ReadInt16LittleEndian(report[42..]),BinaryPrimitives.ReadInt16LittleEndian(report[44..]),
            BinaryPrimitives.ReadInt16LittleEndian(report[34..]),BinaryPrimitives.ReadInt16LittleEndian(report[36..]),BinaryPrimitives.ReadInt16LittleEndian(report[38..]));
        receivedAt=now;reports++;return true;
    }
    public VirtualInputSnapshot Snapshot(long now) => receivedAt>=0 && now>=receivedAt && now-receivedAt<500
        ? new(true,pad,reports,lastPressed,motion) : new(false,default,reports,PadButtons.None);
    public void Clear() {pad=default;motion=default;lastPressed=PadButtons.None;receivedAt=-1;reports=0;}
}

public static class RumbleTest
{
    public const ushort Strength = 0x4000;
    public const int DurationMilliseconds = 400;
    public const int ResendMilliseconds = 40;
    public static byte[] Report(ushort strength, int reportLength)
    {
        if(reportLength is <10 or >64)throw new ArgumentOutOfRangeException(nameof(reportLength));
        var report=new byte[reportLength];report[0]=0x80;
        BinaryPrimitives.WriteUInt16LittleEndian(report.AsSpan(4),strength);
        BinaryPrimitives.WriteUInt16LittleEndian(report.AsSpan(7),strength);
        return report;
    }
    public static async Task RunAsync(Func<ushort, CancellationToken, ValueTask> send, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        bool completed=false;
        try {
            var clock=Stopwatch.StartNew();
            while(clock.ElapsedMilliseconds<DurationMilliseconds) {
                cancellation.ThrowIfCancellationRequested();
                await send(Strength,cancellation).ConfigureAwait(false);
                await Task.Delay(ResendMilliseconds,cancellation).ConfigureAwait(false);
            }
            completed=true;
        } finally {
            // Cancellation/disconnection must not skip the neutral command.
            using var stop=new CancellationTokenSource(200);
            try {await send(0,stop.Token).ConfigureAwait(false);}
            catch when(!completed) { /* Preserve the initial failure after attempting stop. */ }
        }
    }
}
