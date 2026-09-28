using System.Diagnostics;
using System.Globalization;
using System.Security.Principal;
using G7Bridge.Core;

namespace G7Bridge.Windows;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        UiLanguage.Initialize(CultureInfo.CurrentUICulture);
        if (args is ["--service"])
        {
            UiLanguage.Initialize(CultureInfo.InvariantCulture);
            System.ServiceProcess.ServiceBase.Run(new ControllerService());
            return 0;
        }
        if (args is ["--prepare-setup"] or ["--configure-service"] or ["--uninstall-service"] || args is ["--install-service", _])
        {
            try
            {
                if (args is ["--install-service", var ownerSid]) return ServiceSetup.Install(ownerSid).GetAwaiter().GetResult();
                if (args[0]=="--configure-service") return ServiceSetup.ConfigureMachine().GetAwaiter().GetResult();
                if (args[0]=="--uninstall-service") ServiceSetup.Uninstall().GetAwaiter().GetResult();
                else ServiceSetup.Prepare();
                return 0;
            }
            catch (Exception ex)
            {
                try
                {
                    string path=IsAdmin?ServiceFiles.DirectoryPath:SettingsStore.DirectoryPath;
                    Directory.CreateDirectory(path);
                    File.WriteAllText(Path.Combine(path,"setup-error.txt"),ex.ToString());
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
                return 1;
            }
        }
        // Only the non-interactive layout helper accepts a language override.
        bool trayOnly=args is [AutostartEntry.TrayArgument];
        if (args is ["--render-ui", _, var language] && language is "en" or "nl")
            UiLanguage.Initialize(CultureInfo.GetCultureInfo(language));
        else if (args.Length!=0 && !trayOnly && args is not ["--render-ui", _]) return 2;

        ApplicationConfiguration.Initialize();
        if (args.Length>=2 && args[0]=="--render-ui")
        {
            using var preview=new ServiceForm(startAutomatically:false);
            preview.RenderPreview(args[1]);
            TrayIcons.RenderSheet(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(args[1]))!,Path.GetFileNameWithoutExtension(args[1])+"-tray.png"));
            return 0;
        }
        using var mutex=new Mutex(true,"Local\\Grimm.G7Bridge",out bool owner);
        if (!owner)
        {
            // A sign-in start never brings an already running window forward.
            if (trayOnly) return 0;
            try { using var show=EventWaitHandle.OpenExisting(ServiceForm.ShowSignal); show.Set(); return 0; }
            catch (WaitHandleCannotBeOpenedException)
            {
                MessageBox.Show(UiLanguage.Text("G7 Bridge is already in the system tray.","G7 Bridge staat al in het systeemvak."),"G7 Bridge");
                return 1;
            }
        }
        Application.Run(new ServiceForm(trayOnly:trayOnly));
        return 0;
    }

    internal static bool IsAdmin=>new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
    internal static bool DeveloperModeEnabled
    {
        get
        {
            try { return Microsoft.Win32.Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock", "AllowDevelopmentWithoutDevLicense",null) is int value && value==1; }
            catch (System.Security.SecurityException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }
    }
    internal static Process? StartElevated(string arguments,ProcessWindowStyle windowStyle=ProcessWindowStyle.Hidden)=>Process.Start(new ProcessStartInfo
    { FileName=Environment.ProcessPath!,Arguments=arguments,UseShellExecute=true,Verb="runas",WindowStyle=windowStyle });
}
