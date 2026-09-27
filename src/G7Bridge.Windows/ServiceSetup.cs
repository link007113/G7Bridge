using System.Diagnostics;
using System.Security.AccessControl;
using System.Security.Principal;
using System.ServiceProcess;
using System.Text.Json;
using G7Bridge.Core;
using Microsoft.Win32;

namespace G7Bridge.Windows;

internal static class ServiceSetup
{
    internal static string InstallDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Grimm", "G7Bridge");
    private static string Executable => Path.Combine(InstallDirectory, "G7Bridge.exe");
    private static string ExpectedCommand => $"\"{Executable}\" --service";

    // Inno owns files and shortcuts. Local users get only query/start/stop rights,
    // including when another administrator supplies the installation credentials.
    internal static Task<int> ConfigureMachine() => Install("S-1-5-32-545", copyPackage:false, createShortcut:false);

    internal static async Task<int> Install(string ownerSid, bool copyPackage=true, bool createShortcut=true)
    {
        RequireAdmin();
        var owner = new SecurityIdentifier(ownerSid);
        bool existed = OwnServiceExists();
        Prepare();
        string source = Path.GetFullPath(AppContext.BaseDirectory).TrimEnd(Path.DirectorySeparatorChar);
        Directory.CreateDirectory(InstallDirectory);
        Directory.CreateDirectory(ServiceFiles.DirectoryPath);
        SecureDirectory(InstallDirectory, new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null));
        SecureDirectory(ServiceFiles.DirectoryPath, owner);
        if (copyPackage && !string.Equals(source, InstallDirectory, StringComparison.OrdinalIgnoreCase))
        {
            string manifest = Path.Combine(source, "package-files.json");
            var files = JsonSerializer.Deserialize<string[]>(File.ReadAllText(manifest)) ?? throw new IOException("The package file manifest is missing.");
            foreach (string relative in files)
            {
                string from = Path.GetFullPath(Path.Combine(source, relative));
                string to = Path.GetFullPath(Path.Combine(InstallDirectory, relative));
                if (!from.StartsWith(source + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                    !to.StartsWith(InstallDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    throw new IOException("Invalid package path.");
                Directory.CreateDirectory(Path.GetDirectoryName(to)!);
                for (int attempt=0;;attempt++)
                {
                    try { File.Copy(from, to, true); break; }
                    catch (IOException) when (attempt<20) { await Task.Delay(100); }
                }
            }
            File.Copy(manifest, Path.Combine(InstallDirectory, "package-files.json"), true);
        }

        bool restartRequired = false;
        var hide = new Nefarius.Drivers.HidHide.HidHideControlService();
        if (!hide.IsInstalled)
        {
            string driver = Path.Combine(InstallDirectory, "drivers", "HidHide_1.5.230_x64.exe");
            using (var file = File.OpenRead(driver))
                if (Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(file)) != "F4BBBCB82E6258641B887C74BC81C4C5F66E4AA811808DFC304347687B7605F6")
                    throw new IOException("HidHide does not match the pinned signed release.");
            using var setup = Process.Start(new ProcessStartInfo(driver) { Arguments="/exenoui /qn /norestart", UseShellExecute=false, CreateNoWindow=true })
                ?? throw new IOException("Could not start the HidHide installer.");
            await setup.WaitForExitAsync();
            if (setup.ExitCode is not 0 and not 3010) throw new IOException("HidHide installer exit code: " + setup.ExitCode);
            restartRequired = setup.ExitCode == 3010;
        }
        // The signed USB/IP backend, never HIDMaestro's UMDF/certificate installer.
        HIDMaestro.HMContext.InstallUsbipBackend();
        restartRequired |= !new Nefarius.Drivers.HidHide.HidHideControlService().IsOperational;

        var config = File.Exists(ServiceFiles.ConfigurationPath) ? ServiceFiles.Configuration() : new ServiceConfiguration();
        config.OwnerSid = owner.Value;
        config.InputOnly = false;
        File.WriteAllText(ServiceFiles.ConfigurationPath, JsonSerializer.Serialize(config));
        if (!existed)
            await Sc("create", ServiceFiles.Name, "binPath=", ExpectedCommand, "start=", "demand", "obj=", "LocalSystem", "DisplayName=", "G7 Bridge Controller Service");
        await Sc("sdset", ServiceFiles.Name, $"D:(A;;GA;;;SY)(A;;GA;;;BA)(A;;CCLCSWRPWP;;;{owner.Value})");
        await Sc("description", ServiceFiles.Name, "GameSir G7 Pro to virtual Steam Controller. Start and stop through G7 Bridge.");
        if (createShortcut) CreateShortcut();
        return restartRequired ? 3010 : 0;
    }

    internal static void Prepare()
    {
        RequireAdmin();
        if (OwnServiceExists())
        {
            using var svc = new ServiceController(ServiceFiles.Name);
            if (svc.Status != ServiceControllerStatus.Stopped)
            {
                if (svc.Status != ServiceControllerStatus.StopPending) svc.Stop(stopDependentServices:false);
                svc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(25));
            }
        }
        PhysicalHideLease.Recover(Executable);
    }

    internal static async Task Uninstall()
    {
        RequireAdmin();
        bool existed = OwnServiceExists();
        Prepare();
        if (existed) await Sc("delete", ServiceFiles.Name);
        // Shared drivers and user diagnostics stay; Inno removes its own installed files.
        if (File.Exists(ServiceFiles.StatusPath)) File.Delete(ServiceFiles.StatusPath);
    }

    private static bool OwnServiceExists()
    {
        var image = Registry.GetValue(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\" + ServiceFiles.Name, "ImagePath", null) as string;
        if (image is null) return false;
        if (!InstallationPolicy.OwnsService(image, Executable))
            throw new IOException("Another service uses the G7 Bridge service name. It will not be modified.");
        return true;
    }

    private static void RequireAdmin()
    {
        if (!Program.IsAdmin) throw new UnauthorizedAccessException("Administrator rights are required for setup.");
    }

    private static void CreateShortcut()
    {
        var shellType = Type.GetTypeFromProgID("WScript.Shell") ?? throw new IOException("Windows shortcuts are unavailable.");
        dynamic shell = Activator.CreateInstance(shellType)!;
        dynamic link = shell.CreateShortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "G7 Bridge.lnk"));
        try
        {
            link.TargetPath=Executable; link.WorkingDirectory=InstallDirectory;
            link.Description="GameSir G7 Pro to Steam Controller"; link.Save();
        }
        finally
        {
            System.Runtime.InteropServices.Marshal.FinalReleaseComObject(link);
            System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell);
        }
    }

    private static void SecureDirectory(string directory, SecurityIdentifier reader)
    {
        var acl = new DirectorySecurity(); acl.SetAccessRuleProtection(true, false);
        var inherit = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
        foreach (var sid in new[] { new SecurityIdentifier(WellKnownSidType.LocalSystemSid,null), new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid,null) })
            acl.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));
        acl.AddAccessRule(new FileSystemAccessRule(reader, FileSystemRights.ReadAndExecute, inherit, PropagationFlags.None, AccessControlType.Allow));
        new DirectoryInfo(directory).SetAccessControl(acl);
    }

    private static async Task Sc(params string[] arguments)
    {
        var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "sc.exe"))
        { UseShellExecute=false, CreateNoWindow=true, RedirectStandardOutput=true, RedirectStandardError=true };
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new IOException("Could not start Windows Service Control.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        if (process.ExitCode!=0) throw new IOException("Windows Service Control: " + await output + await error);
    }
}
