namespace G7Bridge.Core;

// A GameSir receiver is a USB root. A connected controller adds synthesized
// XInput/GIP children (IG_); interface children (MI_) exist without a controller.
public static class ReceiverPresence
{
    private static readonly string[] Roots=[@"USB\VID_3537&PID_100A\",@"USB\VID_3537&PID_106B\"];
    public static bool IsReceiverRoot(string instanceId)=>Roots.Any(root=>instanceId.StartsWith(root,StringComparison.OrdinalIgnoreCase));
    public static bool HasController(IEnumerable<string> presentDescendants)=>presentDescendants.Any(id=>id.Contains("&IG_",StringComparison.OrdinalIgnoreCase));
}
