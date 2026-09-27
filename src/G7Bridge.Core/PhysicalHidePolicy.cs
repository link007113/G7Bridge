namespace G7Bridge.Core;

public static class PhysicalHidePolicy
{
    public static bool IsSelectedModel(string instanceId)
    {
        if(string.IsNullOrWhiteSpace(instanceId) || instanceId.Length>1023 || instanceId.Any(char.IsControl))return false;
        var id=instanceId.ToUpperInvariant();
        return new[]{@"USB\VID_3537&PID_106B",@"HID\VID_3537&PID_106B"}.Any(prefix=>
            id.StartsWith(prefix,StringComparison.Ordinal) && id.Length>prefix.Length && id[prefix.Length] is '\\' or '&');
    }
    public static bool CanActivate(bool active,bool inverted,IEnumerable<string> blocked,string selected)
        =>CanActivate(active,inverted,blocked,selected,[selected]);
    public static bool CanActivate(bool active,bool inverted,IEnumerable<string> blocked,string selected,IReadOnlyList<string> verifiedInstances)
        =>IsSelectedModel(selected) && verifiedInstances.Count is >0 and <=16 && !inverted && (active || !blocked.Except(verifiedInstances,StringComparer.OrdinalIgnoreCase).Any());
}
