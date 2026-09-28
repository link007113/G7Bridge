namespace G7Bridge.Core;

// One notification per threshold and discharge cycle. Charging, or recovering
// five points above a threshold, re-arms it; unknown readings change nothing.
public sealed class BatteryAlerts
{
    private static readonly byte[] Thresholds=[20,10];
    private const int Rearm=5;
    private readonly HashSet<byte> fired=[];
    public byte? Observe(byte? battery,bool charging)
    {
        if(battery is not byte level)return null;
        if(charging){fired.Clear();return null;}
        fired.RemoveWhere(threshold=>level>=threshold+Rearm);
        var due=Thresholds.Where(threshold=>level<=threshold && !fired.Contains(threshold)).ToArray();
        if(due.Length==0)return null;
        foreach(var threshold in Thresholds.Where(threshold=>level<=threshold))fired.Add(threshold);
        return due.Min();
    }
}
