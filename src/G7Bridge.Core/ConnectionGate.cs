namespace G7Bridge.Core;

public enum GateAction { Wait, Start, Release }

// Decides when the service opens a session. Hiding stays in place while waiting,
// so a controller that turns on is never briefly visible as a physical XInput pad.
public sealed class ConnectionGate
{
    public const int MaximumFailures=3;
    public const long ReportGraceMilliseconds=10_000;
    private int failures;
    private long? graceStart;
    public bool Released {get;private set;}
    public bool HoldHiding=>!Released;
    public GateAction Next(bool controllerPresent,long now=0)
    {
        if(!controllerPresent){Released=false;failures=0;graceStart=null;return GateAction.Wait;}
        graceStart??=now;
        return Released?GateAction.Wait:GateAction.Start;
    }
    // Plug and Play shows the controller, but Windows' input API does not report it.
    // After a grace period this counts as a failed session, so hiding cannot stay
    // in place indefinitely without a working bridge.
    public GateAction NotReported(long now,bool hidingApplies)
    {
        graceStart??=now;
        if(now-graceStart<ReportGraceMilliseconds)return GateAction.Wait;
        graceStart=now;
        return SessionEnded(false,hidingApplies);
    }
    // Repeated failures with the controller present give the physical controller
    // back until it disconnects. Hiding has no effect on the 100A identity, so
    // failures there only lead to another attempt.
    public GateAction SessionEnded(bool receivedTelemetry,bool hidingApplies=true)
    {
        if(receivedTelemetry){failures=0;return GateAction.Wait;}
        if(!hidingApplies)return GateAction.Wait;
        if(++failures<MaximumFailures)return GateAction.Wait;
        Released=true;return GateAction.Release;
    }
}
