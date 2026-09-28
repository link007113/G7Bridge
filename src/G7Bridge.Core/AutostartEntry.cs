namespace G7Bridge.Core;

public static class AutostartEntry
{
    public const string ValueName="G7 Bridge";
    public const string TrayArgument="--tray";
    public static string Command(string executable)=>$"\"{executable}\" {TrayArgument}";
    // Windows' Startup apps page sets bit 0 of the first StartupApproved byte when disabled.
    public static bool IsEnabled(string? runValue,string executable,byte[]? approved)=>
        string.Equals(runValue,Command(executable),StringComparison.OrdinalIgnoreCase) && (approved is not {Length:>0} || (approved[0]&1)==0);
}
