using G7Bridge.Core;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace G7Bridge.Windows;

internal static class SettingsStore
{
    public static string DirectoryPath=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"G7Bridge");
    public static string FilePath=>Path.Combine(DirectoryPath,"settings.json");
    private static readonly JsonSerializerOptions json=new() { WriteIndented=true,Converters={new JsonStringEnumConverter()} };
    public static BridgeSettings Load()
    {
        if(!File.Exists(FilePath)) return new();
        var result=JsonSerializer.Deserialize<BridgeSettings>(File.ReadAllText(FilePath),json)??throw new InvalidDataException("Leeg instellingenbestand.");
        ButtonLearning.Validate(result);return result;
    }
    public static void Save(BridgeSettings settings)
    {
        ButtonLearning.Validate(settings);Directory.CreateDirectory(DirectoryPath);
        string temp=FilePath+".new";
        File.WriteAllText(temp,JsonSerializer.Serialize(settings,json));File.Move(temp,FilePath,true);
    }
    public static BridgeSettings Clone(BridgeSettings settings)=>JsonSerializer.Deserialize<BridgeSettings>(JsonSerializer.Serialize(settings,json),json)!;
}
