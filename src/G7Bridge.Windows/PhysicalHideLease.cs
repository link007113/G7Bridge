using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using G7Bridge.Core;
using Nefarius.Drivers.HidHide;

namespace G7Bridge.Windows;

internal sealed class PhysicalHideLease : IDisposable
{
    private sealed class Journal
    {
        public string AppPath {get;set;}="";
        public string Instance {get;set;}="";
        public string[] Instances {get;set;}=[];
        public string[] AddedInstances {get;set;}=[];
        public bool AddedInstance {get;set;}
        public bool AddedApplication {get;set;}
        public bool Activated {get;set;}
        public string[] InitialBlocked {get;set;}=[];
    }
    private readonly HidHideControlService api=new();
    private readonly Journal ownership;
    private bool disposed;
    private static string JournalPath=>Path.Combine(ServiceFiles.DirectoryPath,"hiding.json");
    private static string PairingPath=>Path.Combine(ServiceFiles.DirectoryPath,"paired-input.json");
    private sealed class Pairing {public string Root {get;set;}="";public string Location {get;set;}="";public string[] Instances {get;set;}=[];}
    internal string Root=>ownership.Instance;
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] private static extern uint QueryDosDevice(string name,StringBuilder target,int length);
    [DllImport("cfgmgr32.dll",CharSet=CharSet.Unicode)] private static extern int CM_Locate_DevNodeW(out uint node,string instance,uint flags);
    [DllImport("cfgmgr32.dll")] private static extern int CM_Get_Child(out uint child,uint parent,uint flags);
    [DllImport("cfgmgr32.dll")] private static extern int CM_Get_Sibling(out uint sibling,uint node,uint flags);
    [DllImport("cfgmgr32.dll",CharSet=CharSet.Unicode)] private static extern int CM_Get_Device_IDW(uint node,StringBuilder id,int length,uint flags);
    private static string[] InputDescendants(string root)
    {
        if(CM_Locate_DevNodeW(out uint node,root,0)!=0)throw new IOException("The selected GameSir is no longer present.");
        // Keep the GIP root accessible to Windows' controller-provider broker;
        // only its synthesized legacy input children belong in HidHide.
        var found=new List<string>();
        void Visit(uint parent,int depth)
        {
            if(depth>6 || found.Count>16)throw new IOException("Unexpectedly large controller device tree.");
            if(CM_Get_Child(out uint child,parent,0)!=0)return;
            do {
                var id=new StringBuilder(1024);
                if(CM_Get_Device_IDW(child,id,id.Capacity,0)==0) {
                    string value=id.ToString();
                    if(value.StartsWith(@"HID\VID_045E&PID_02FF&IG_",StringComparison.OrdinalIgnoreCase) || value.StartsWith(@"USB\VID_045E&PID_02FF&IG_",StringComparison.OrdinalIgnoreCase))found.Add(value);
                }
                Visit(child,depth+1);
            } while(CM_Get_Sibling(out child,child,0)==0);
        }
        Visit(node,0);if(found.Count==0)throw new IOException("The GameSir has no verifiable legacy input instances yet.");return found.ToArray();
    }
    private static string DriverPath(string path)
    {
        var volume=new StringBuilder(1024);
        if(path.Length<3 || QueryDosDevice(path[..2],volume,volume.Capacity)==0)throw new IOException("Cannot resolve the application path for HidHide.");
        return volume+path[2..];
    }
    internal static void CheckAvailable()
    {
        var client=new HidHideControlService();
        if(!client.IsInstalled || !client.IsOperational)throw new IOException("The HidHide driver is not available yet. Complete G7 Bridge setup.");
        if(client.IsAppListInverted)throw new IOException("HidHide uses an inverted application list; the existing setting will not be changed.");
    }
    internal PhysicalHideLease(string instance,string location):this(instance,InputDescendants(instance))
    {
        File.WriteAllText(PairingPath,JsonSerializer.Serialize(new Pairing{Root=instance,Location=location,Instances=ownership.Instances}));
    }
    internal static PhysicalHideLease? BeforeConnection(string location)
    {
        if(!File.Exists(PairingPath))return null;
        var pair=JsonSerializer.Deserialize<Pairing>(File.ReadAllText(PairingPath));
        if(pair is null || pair.Location!=location || !PhysicalHidePolicy.IsSelectedModel(pair.Root))return null;
        var inputs=pair.Instances.Where(id=>!string.Equals(id,pair.Root,StringComparison.OrdinalIgnoreCase)).ToArray();
        if(inputs.Length is <1 or >16)throw new IOException("Invalid stored controller pairing.");
        return new(pair.Root,inputs);
    }
    private PhysicalHideLease(string instance,string[] instances)
    {
        if(!PhysicalHidePolicy.IsSelectedModel(instance))throw new IOException("No unique GameSir 106B device instance to hide: "+instance);
        if(File.Exists(JournalPath))throw new IOException("A previous hiding rule must be recovered first.");
        var blocked=api.BlockedInstanceIds.ToArray();bool active=api.IsActive;
        if(!PhysicalHidePolicy.CanActivate(active,api.IsAppListInverted,blocked,instance,instances))throw new IOException("HidHide has inactive rules for other devices; those rules will not be activated.");
        string app=Environment.ProcessPath!;
        ownership=new(){AppPath=app,Instance=instance,Instances=instances,AddedInstances=instances.Except(blocked,StringComparer.OrdinalIgnoreCase).ToArray(),InitialBlocked=blocked,
            AddedInstance=false,
            AddedApplication=!api.ApplicationPaths.Contains(DriverPath(app),StringComparer.OrdinalIgnoreCase),Activated=!active};
        // Persist ownership before any write, so a later service start can recover a crash.
        File.WriteAllText(JournalPath,JsonSerializer.Serialize(ownership));
        try {
            if(ownership.AddedApplication)api.AddApplicationPath(app,true);
            foreach(var id in ownership.AddedInstances)api.AddBlockedInstanceId(id);
            if(ownership.Activated)api.IsActive=true;
            if(!api.IsActive || ownership.Instances.Except(api.BlockedInstanceIds,StringComparer.OrdinalIgnoreCase).Any())throw new IOException("Physical GameSir hiding was not confirmed.");
            ServiceFiles.Log($"Hid {instances.Length} legacy input instances of the selected GameSir; service retains access: "+instance);
        } catch {Dispose();throw;}
    }
    private PhysicalHideLease(Journal journal)=>ownership=journal;
    internal static void Recover(string? installedExecutable=null)
    {
        if(!File.Exists(JournalPath))return;
        var journal=JsonSerializer.Deserialize<Journal>(File.ReadAllText(JournalPath))??throw new IOException("Recovery data is missing.");
        if(!PhysicalHidePolicy.IsSelectedModel(journal.Instance) || !string.Equals(journal.AppPath,installedExecutable??Environment.ProcessPath,StringComparison.OrdinalIgnoreCase))
            throw new IOException("The stored hiding rule cannot be attributed to this installation.");
        using var lease=new PhysicalHideLease(journal);
    }
    public void Dispose()
    {
        if(disposed)return;
        // Also recover the single-instance journal written by the first service build.
        var added=ownership.AddedInstances.Length>0?ownership.AddedInstances:ownership.Instances.Length==0 && ownership.AddedInstance?[ownership.Instance]:[];
        foreach(var id in added)api.RemoveBlockedInstanceId(id);
        var remaining=api.BlockedInstanceIds;
        if(ownership.Activated && remaining.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(ownership.InitialBlocked))api.IsActive=false;
        if(ownership.AddedApplication)api.RemoveApplicationPath(ownership.AppPath);
        File.Delete(JournalPath);disposed=true;
        ServiceFiles.Log("Owned hiding rule removed; previous HidHide settings retained.");
    }
}
