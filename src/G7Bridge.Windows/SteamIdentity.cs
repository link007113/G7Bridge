using System.Globalization;
using System.Reflection;
using G7Bridge.Core;
using Microsoft.Win32;

namespace G7Bridge.Windows;

internal static class SteamIdentity
{
    internal static string PrepareProfile()
    {
        // Compatibility identity for an emulated device. No physical firmware,
        // Steam preferences or updater files are modified or executed.
        uint version=0x6A628345; // Current Steam target observed on 2026-09-27.
        var roots=new List<string?> {
            Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam","InstallPath",null) as string,
            Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Valve\Steam","InstallPath",null) as string,
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),"Steam")
        };
        try {
            string sid=ServiceFiles.Configuration().OwnerSid;
            roots.Add(Registry.GetValue(@"HKEY_USERS\"+sid+@"\Software\Valve\Steam","SteamPath",null) as string);
        }catch(IOException) { }
        foreach(var root in roots.Where(x=>!string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase)) {
            string config=Path.Combine(root!,"bin","hardwareupdater","hardwareupdater.cfg");
            if(!File.Exists(config))continue;
            foreach(var line in File.ReadLines(config)) {
                const string prefix="TRITON_FW_TS:";
                if(line.StartsWith(prefix,StringComparison.Ordinal) && uint.TryParse(line[prefix.Length..].Trim(),NumberStyles.HexNumber,CultureInfo.InvariantCulture,out uint current))version=Math.Max(version,current);
            }
        }
        using var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("G7Bridge.SteamProfile.json")??throw new IOException("The virtual controller profile is missing.");
        using var reader=new StreamReader(stream);
        string profile=TritonIdentity.UseBridgeOwnedStream(TritonIdentity.WithCompatibilityVersion(reader.ReadToEnd(),version));
        string directory=Path.Combine(ServiceFiles.DirectoryPath,"profiles");Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory,"steam-controller-2.json"),profile);
        ServiceFiles.Log($"Virtual Steam compatibility version: 0x{version:X8}; no physical firmware changes.");
        return directory;
    }
}
