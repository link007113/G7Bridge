using System.Numerics;

namespace G7Bridge.Core;

public readonly record struct InputSnapshot(PadState Pad,MotionState Motion,bool HasTelemetry,bool HasGamepad,Quaternion Orientation);

public sealed class InputState
{
    private Telemetry? telemetry;
    private PadState? gamepad;
    private long telemetryTime,gamepadTime;
    private readonly GyroOrientation orientation=new();
    public void Accept(ReadOnlySpan<byte> packet,long milliseconds)
    {
        if(G7Protocol.TryTelemetry(packet,out var t)) { telemetry=t;telemetryTime=milliseconds; }
        if(G7Protocol.TryGamepad(packet,out var p)) { gamepad=p;gamepadTime=milliseconds; }
    }
    public InputSnapshot Snapshot(long milliseconds,BridgeSettings settings)
    {
        bool motionFresh=telemetry is not null && milliseconds>=telemetryTime && milliseconds-telemetryTime<=150;
        bool padFresh=gamepad is not null && milliseconds>=gamepadTime && milliseconds-gamepadTime<=150;
        var pad=padFresh ? gamepad!.Value : motionFresh ? telemetry!.LowResolutionPad : default;
        MotionState motion=default;
        if(motionFresh) {
            var buttons=telemetry!.LowResolutionPad.Buttons;
            foreach(var target in settings.Bindings.Keys) buttons&=~target;
            pad=pad with { Buttons=buttons|ButtonLearning.Apply(telemetry.Report,settings.Bindings) };
            motion=MotionConversion.Convert(telemetry,settings);
            orientation.Update(motion,telemetryTime);
        }
        return new(pad,motion,motionFresh,padFresh,orientation.Value);
    }
    public void Reset() { telemetry=null;gamepad=null;orientation.Reset(); }
}

public sealed class GyroOrientation
{
    public Quaternion Value { get; private set; } = Quaternion.Identity;
    private long? previous;
    public void Update(MotionState motion,long milliseconds)
    {
        var old=previous;previous=milliseconds;
        if(old is null || milliseconds<=old || milliseconds-old>150) return;
        double dt=(milliseconds-old.Value)/1000.0;
        var velocity=new Vector3(motion.GX,motion.GY,motion.GZ)*(float)(Math.PI/(180*16.384));
        float speed=velocity.Length();
        if(speed<0.000001f) return;
        var delta=Quaternion.CreateFromAxisAngle(velocity/speed,speed*(float)dt);
        Value=Quaternion.Normalize(Value*delta);
    }
    public void Reset() { previous=null;Value=Quaternion.Identity; }
}
