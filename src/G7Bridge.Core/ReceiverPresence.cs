namespace G7Bridge.Core;

// A GameSir receiver is a USB root. A connected controller adds synthesized
// XInput/GIP children (IG_); interface children (MI_) exist without a controller.
public static class ReceiverPresence
{
    private const string SessionRoot=@"USB\VID_3537&PID_106B\";
    private static readonly string[] Roots=[@"USB\VID_3537&PID_100A\",SessionRoot];
    public static bool IsReceiverRoot(string instanceId)=>Roots.Any(root=>instanceId.StartsWith(root,StringComparison.OrdinalIgnoreCase));
    // The paired hiding rule lists the 106B session identity's children; it does not cover 100A.
    public static bool IsSessionRoot(string instanceId)=>instanceId.StartsWith(SessionRoot,StringComparison.OrdinalIgnoreCase);
    public static bool HasController(IEnumerable<string> presentDescendants)=>presentDescendants.Any(id=>id.Contains("&IG_",StringComparison.OrdinalIgnoreCase));
}
