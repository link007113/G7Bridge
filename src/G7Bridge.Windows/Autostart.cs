using System.Text.Json;
using G7Bridge.Core;
using Microsoft.Win32;

namespace G7Bridge.Windows;

// Per-user sign-in start without elevation. Only the installed executable is
// registered, so a development build never replaces the installed entry.
internal static class Autostart
{
    private const string RunKey=@"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovedKey=@"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    private sealed class Preferences {public bool AutostartInitialized {get;set;}}
    private static string PreferencesPath=>Path.Combine(SettingsStore.DirectoryPath,"ui.json");
    private static string Executable=>Path.Combine(ServiceSetup.InstallDirectory,"G7Bridge.exe");
    internal static bool Available=>string.Equals(Environment.ProcessPath,Executable,StringComparison.OrdinalIgnoreCase);
    internal static bool Enabled
    {
        get
        {
            using var run=Registry.CurrentUser.OpenSubKey(RunKey);
            using var approved=Registry.CurrentUser.OpenSubKey(ApprovedKey);
            return AutostartEntry.IsEnabled(run?.GetValue(AutostartEntry.ValueName) as string,Executable,approved?.GetValue(AutostartEntry.ValueName) as byte[]);
        }
    }
    internal static void Set(bool enabled)
    {
        if(!Available)throw new InvalidOperationException(L("Start with Windows is only available for the installed app.","Starten met Windows kan alleen voor de geïnstalleerde app."));
        using var run=Registry.CurrentUser.CreateSubKey(RunKey);
        if(enabled) {
            run.SetValue(AutostartEntry.ValueName,AutostartEntry.Command(Executable));
            // Enabling here also lifts an earlier "off" from Windows' Startup apps page.
            using var approved=Registry.CurrentUser.OpenSubKey(ApprovedKey,true);
            approved?.DeleteValue(AutostartEntry.ValueName,false);
        } else run.DeleteValue(AutostartEntry.ValueName,false);
    }
    // On by default: enabled once when this version first runs; afterwards only the user's choice counts.
    internal static void InitializeOnce()
    {
        if(!Available)return;
        try {
            var preferences=File.Exists(PreferencesPath)?JsonSerializer.Deserialize<Preferences>(File.ReadAllText(PreferencesPath))??new():new();
            if(preferences.AutostartInitialized)return;
            Set(true);
            preferences.AutostartInitialized=true;
            Directory.CreateDirectory(SettingsStore.DirectoryPath);
            File.WriteAllText(PreferencesPath,JsonSerializer.Serialize(preferences));
        } catch(IOException) { } catch(UnauthorizedAccessException) { } catch(JsonException) { } catch(System.Security.SecurityException) { }
    }
    private static string L(string english,string dutch)=>UiLanguage.Text(english,dutch);
}
