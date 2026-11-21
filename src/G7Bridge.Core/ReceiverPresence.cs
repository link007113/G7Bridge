namespace G7Bridge.Core;

// A GameSir receiver is a USB root. A connected controller adds synthesized
// XInput/GIP children (IG_); interface children (MI_) exist without a controller.
public static class ReceiverPresence
{
    private const string SessionRoot=@"USB\VID_3537&PID_106B\";
    private const string XboxModeRoot=@"USB\VID_3537&PID_1004\";
    private static readonly string[] Roots=[@"USB\VID_3537&PID_100A\",SessionRoot];
    public static bool IsReceiverRoot(string instanceId)=>Roots.Any(root=>instanceId.StartsWith(root,StringComparison.OrdinalIgnoreCase));
    public static bool IsSessionRoot(string instanceId,bool verifiedGip=false)
        =>instanceId.StartsWith(SessionRoot,StringComparison.OrdinalIgnoreCase) || verifiedGip && IsXboxModeRoot(instanceId);
    public static bool IsXboxModeRoot(string instanceId)=>instanceId.StartsWith(XboxModeRoot,StringComparison.OrdinalIgnoreCase);
    public static bool HasGipCompatibleId(IEnumerable<string> compatibleIds)
        =>compatibleIds.Contains(@"USB\MS_COMP_XGIP10",StringComparer.OrdinalIgnoreCase);
    public static bool HasController(IEnumerable<string> presentDescendants)=>presentDescendants.Any(id=>id.Contains("&IG_",StringComparison.OrdinalIgnoreCase));
}
