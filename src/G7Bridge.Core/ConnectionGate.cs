namespace G7Bridge.Core;

public enum GateAction { Wait, Start, Release }

// Decides when the service opens a session. Hiding stays in place while waiting,
// so a controller that turns on is never briefly visible as a physical XInput pad.
public sealed class ConnectionGate
{
    public const int MaximumFailures=3;
    private int failures;
    public bool Released {get;private set;}
    public bool HoldHiding=>!Released;
    public GateAction Next(bool controllerPresent)
    {
        if(!controllerPresent){Released=false;failures=0;return GateAction.Wait;}
        return Released?GateAction.Wait:GateAction.Start;
    }
    // Repeated failures with the controller present give the physical controller
    // back until it disconnects, instead of leaving the user without input.
    public GateAction SessionEnded(bool receivedTelemetry)
    {
        if(receivedTelemetry){failures=0;return GateAction.Wait;}
        if(++failures<MaximumFailures)return GateAction.Wait;
        Released=true;return GateAction.Release;
    }
}
