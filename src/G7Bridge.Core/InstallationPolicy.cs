namespace G7Bridge.Core;

public static class InstallationPolicy
{
    public static bool OwnsService(string command, string executable) =>
        string.Equals(command, $"\"{executable}\" --service", StringComparison.OrdinalIgnoreCase);
}
