namespace G7Bridge.Core;

public enum TrayKind { Off, Waiting, Active, Attention }

public readonly record struct TrayView(TrayKind Kind,byte? Battery,bool Charging)
{
    public string Tooltip=>Kind switch {
        TrayKind.Off=>UiLanguage.Text("G7 Bridge — off","G7 Bridge — uit"),
        TrayKind.Attention=>UiLanguage.Text("G7 Bridge — attention needed","G7 Bridge — aandacht nodig"),
        TrayKind.Active=>UiLanguage.Text("G7 Bridge — active","G7 Bridge — actief")+BatteryText,
        _=>UiLanguage.Text("G7 Bridge — waiting for the controller","G7 Bridge — wachten op de controller")
    };
    private string BatteryText=>Battery is byte level?" · "+(Charging?UiLanguage.Text("charging","opladen"):UiLanguage.Text("battery","accu"))+$" {level}%":"";
    public static TrayView From(bool running,bool current,bool ready,string attention,byte? battery,bool charging)
    {
        if(!running)return new(TrayKind.Off,null,false);
        if(ready)return new(TrayKind.Active,battery,charging);
        return new(current && attention.Length!=0?TrayKind.Attention:TrayKind.Waiting,battery,charging);
    }
}
