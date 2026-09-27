namespace G7Bridge.Core;

[Flags]
public enum PadButtons : uint
{
    None = 0, A = 1, B = 2, X = 4, Y = 8, Share = 0x10,
    // Triton's VIEW wire bit is SDL Start; its MENU bit is SDL Back.
    R3 = 0x20, Menu = 0x40, R4 = 0x80, R5 = 0x100, RB = 0x200,
    Down = 0x400, Right = 0x800, Left = 0x1000, Up = 0x2000,
    View = 0x4000, L3 = 0x8000, Guide = 0x10000,
    L4 = 0x20000, L5 = 0x40000, LB = 0x80000
}

public readonly record struct PadState(short LX, short LY, short RX, short RY,
    ushort LT, ushort RT, PadButtons Buttons, bool HighResolution);

public sealed record Telemetry(short GX, short GY, short GZ, short AX, short AY,
    short AZ, byte? Battery, bool Charging, PadState LowResolutionPad, byte[] Report);

public readonly record struct MotionState(short GX, short GY, short GZ,
    short AX, short AY, short AZ);

public sealed record BitBinding(int Offset, byte Mask, bool ActiveHigh)
{
    public override string ToString() => $"byte {Offset}, bit 0x{Mask:X2}, {(ActiveHigh ? "aan" : "uit")}";
}

public sealed class BridgeSettings
{
    public Dictionary<PadButtons, BitBinding> Bindings { get; set; } = [];
    public double GyroCountsPerDps { get; set; } = 16.0;
    public double AccelCountsPerG { get; set; } = 8192.0;
    public double[] GyroBias { get; set; } = [0, 0, 0];
    public int[] AxisOrder { get; set; } = [0, 1, 2];
    public int[] AxisSign { get; set; } = [1, 1, 1];
}
