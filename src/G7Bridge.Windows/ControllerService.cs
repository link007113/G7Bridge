using System.Diagnostics;
using System.Security.Principal;
using G7Bridge.Core;
using System.ServiceProcess;
using System.Text.Json;

namespace G7Bridge.Windows;

internal sealed class ServiceConfiguration
{
    public string OwnerSid {get;set;}="";
    public string PreferredLocation {get;set;}="";
    public bool InputOnly {get;set;}=true;
}

internal sealed class ServiceSnapshot
{
    public DateTimeOffset Updated {get;set;}
    public string Phase {get;set;}="Stopped";
    public string Attention {get;set;}="";
    public bool Connected {get;set;}
    public bool TelemetryFresh {get;set;}
    public long TelemetryPackets {get;set;}
    public bool VirtualController {get;set;}
    public long OutputReports {get;set;}
    public string OutputReport {get;set;}="";
    public string SourceReport {get;set;}="";
    public string HiddenInstance {get;set;}="";
    public byte? Battery {get;set;}
    public bool Charging {get;set;}
    public bool FullResolutionAxes {get;set;}
    public long FeedbackPackets {get;set;}
    public long RumbleUpdates {get;set;}
    public string Buttons {get;set;}="None";
    public string Axes {get;set;}="";
    public string Motion {get;set;}="";
    public string Location {get;set;}="";
    public bool SystemAccount {get;set;}
    public int SessionId {get;set;}
    public bool DeveloperMode {get;set;}
    public string[] Log {get;set;}=[];
}

internal static class ServiceFiles
{
    internal const string Name="GrimmG7Bridge";
    internal static string DirectoryPath=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),"G7Bridge");
    internal static string StatusPath=>Path.Combine(DirectoryPath,"status.json");
    internal static string ConfigurationPath=>Path.Combine(DirectoryPath,"service.json");
    private static readonly object sync=new();
    private static readonly Queue<string> lines=new();
    private static long lastWrite;
    private static string lastPhase="";
    internal static ServiceConfiguration Configuration()=>JsonSerializer.Deserialize<ServiceConfiguration>(File.ReadAllText(ConfigurationPath))??throw new IOException("Service configuration is missing.");
    internal static void Log(string message)
    {
        lock(sync) {
            string line=$"{DateTimeOffset.Now:O} {message}";lines.Enqueue(line);while(lines.Count>40)lines.Dequeue();
            try {
                string path=Path.Combine(DirectoryPath,"service.log");
                if(File.Exists(path) && new FileInfo(path).Length>2_000_000)File.Move(path,path+".previous",true);
                File.AppendAllText(path,line+Environment.NewLine);
            } catch(IOException) { } catch(UnauthorizedAccessException) { }
        }
    }
    internal static void Publish(ServiceSnapshot state)
    {
        long now=Environment.TickCount64;
        if(state.Phase==lastPhase && now-lastWrite<500)return;
        lastWrite=now;lastPhase=state.Phase;
        state.Updated=DateTimeOffset.Now;
        lock(sync)state.Log=lines.TakeLast(8).ToArray();
        try {File.WriteAllText(StatusPath+".new",JsonSerializer.Serialize(state));File.Move(StatusPath+".new",StatusPath,true);}
        catch(IOException) { /* Readers can use the last complete status until the next update. */ }
        catch(UnauthorizedAccessException) { /* A reader can briefly prevent replacing the status file. */ }
    }
}

internal sealed class ControllerService : ServiceBase
{
    private readonly CancellationTokenSource shutdown=new();
    private Task? worker;
    internal ControllerService() {ServiceName=ServiceFiles.Name;CanStop=true;CanShutdown=true;AutoLog=false;}
    protected override void OnStart(string[] args)
    {
        if(!WindowsIdentity.GetCurrent().IsSystem)throw new InvalidOperationException("The controller service requires the LocalSystem account.");
        worker=Task.Run(()=>Run(shutdown.Token));
    }
    protected override void OnStop()
    {
        shutdown.Cancel();RequestAdditionalTime(15_000);
        if(worker is not null && !worker.Wait(12_000))ServiceFiles.Log("Shutdown exceeded the timeout.");
    }
    protected override void OnShutdown()=>OnStop();

    private static async Task Run(CancellationToken stop)
    {
        var state=new ServiceSnapshot{SystemAccount=WindowsIdentity.GetCurrent().IsSystem,SessionId=Process.GetCurrentProcess().SessionId,DeveloperMode=Program.DeveloperModeEnabled};
        ServiceFiles.Log($"Service started: SYSTEM={state.SystemAccount}; session={state.SessionId}; developerMode={state.DeveloperMode}.");
        // The owned hiding rule stays while the bridge is on, including while it waits,
        // so a controller that turns on is already hidden from other applications.
        PhysicalHideLease? hiding=null;
        var gate=new ConnectionGate();
        string lastError="";
        // Nexus, a driver problem or several receivers pause the bridge, so the physical
        // controller is released then. A failed release is retried on the next pass.
        void ReleaseHiding()
        {
            if(hiding is null)return;
            try {hiding.Dispose();hiding=null;}
            catch(Exception ex) {if(ex.Message!=lastError){lastError=ex.Message;ServiceFiles.Log("Releasing the hiding rule failed; retrying: "+ex.Message);}}
        }
        void Ended(GateAction result)
        {
            if(result!=GateAction.Release)return;
            ServiceFiles.Log($"Connection failed {ConnectionGate.MaximumFailures} times with the controller present; the physical controller is released until it reconnects.");
            ReleaseHiding();
        }
        try {
            var config=ServiceFiles.Configuration();
            _=new SecurityIdentifier(config.OwnerSid);
            PhysicalHideLease.Recover();
            while(!stop.IsCancellationRequested) {
                state.Connected=state.TelemetryFresh=state.VirtualController=false;state.Battery=null;state.Charging=false;state.Attention="";
                try {
                    if(!config.InputOnly) {
                        try {
                            PhysicalHideLease.CheckAvailable();
                            if(!HIDMaestro.HMContext.IsUsbipBackendAvailable)throw new IOException("The virtual USB driver is missing. Complete G7 Bridge setup.");
                        } catch(Exception ex) {ReleaseHiding();state.Phase=ex.Message;state.Attention="driver";ServiceFiles.Publish(state);await Task.Delay(2000,stop);continue;}
                    }
                    if(Process.GetProcessesByName("HJC.GameSir.Nexus2_0").Length!=0) {
                        ReleaseHiding();state.Phase="Close GameSir Nexus to use the bridge";state.Attention="close-nexus";ServiceFiles.Publish(state);await Task.Delay(2000,stop);continue;
                    }
                    // Read-only Plug and Play check: the controller is not opened while it is off.
                    // An unreadable device list is skipped, not mistaken for an absent controller.
                    if(ReceiverScanner.Scan() is not { } scanned){await Task.Delay(500,stop);continue;}
                    var receivers=scanned.Where(r=>config.PreferredLocation.Length==0 || r.Location==config.PreferredLocation).ToArray();
                    if(receivers.Length>1) {
                        ReleaseHiding();state.Attention="multiple-controllers";state.Phase="More than one GameSir found; no device selected automatically";
                        ServiceFiles.Publish(state);await Task.Delay(500,stop);continue;
                    }
                    bool present=receivers.Length==1 && receivers[0].ControllerPresent;
                    var action=gate.Next(present,Environment.TickCount64);
                    if(!present)lastError="";
                    if(receivers.Length==1)state.Location=receivers[0].Location;
                    if(!gate.HoldHiding)ReleaseHiding();
                    else if(receivers.Length==1 && !config.InputOnly)hiding??=PhysicalHideLease.BeforeConnection(receivers[0].Location);
                    if(action!=GateAction.Start) {
                        state.Attention=gate.Released?"connection":"";
                        state.Phase=gate.Released?"Connection failed three times; the physical controller works normally until it reconnects":"Waiting for the GameSir G7 Pro";
                        ServiceFiles.Publish(state);await Task.Delay(500,stop);continue;
                    }
                    bool hidingApplies=ReceiverPresence.IsSessionRoot(receivers[0].InstanceId);
                    ControllerDevice[] devices;
                    try {devices=GameInputTransport.Enumerate().Where(d=>d.ProductId is 0x100A or 0x106B && d.PhysicalKey==receivers[0].Location).ToArray();}
                    catch(Exception) {Ended(gate.SessionEnded(false,hidingApplies));throw;}
                    if(devices.Length!=1) {
                        Ended(gate.NotReported(Environment.TickCount64,hidingApplies));
                        state.Phase="Waiting for Windows to report the GameSir G7 Pro";
                        ServiceFiles.Publish(state);await Task.Delay(500,stop);continue;
                    }
                    var device=devices[0];
                    bool received=false;
                    try {
                        await using var session=new BridgeSession();
                        session.Log+=ServiceFiles.Log;
                        try {
                            session.Start(device);bool requested=false;var started=Stopwatch.StartNew();
                            long nextOutputAttempt=0;string outputError="";
                            while(session.IsRunning && !stop.IsCancellationRequested) {
                                var s=session.Status;
                                if(s.TelemetryPackets>0)received=true;
                                if(s.Connected && !requested && device.ProductId==0x100A) {session.RequestGyro();requested=true;}
                                state.Phase=s.Phase;state.Connected=s.Connected;state.TelemetryFresh=s.Input.HasTelemetry;
                                state.TelemetryPackets=s.TelemetryPackets;state.VirtualController=s.SteamActive;
                                state.OutputReports=session.OutputReports;state.OutputReport=session.LastOutputReport;
                                state.Battery=s.Telemetry?.Battery;state.Charging=s.Telemetry?.Charging??false;state.FullResolutionAxes=s.Input.HasGamepad;
                                state.FeedbackPackets=s.Haptics;state.RumbleUpdates=session.RumbleUpdates;
                                state.SourceReport=s.Telemetry is null?"":Convert.ToHexString(s.Telemetry.Report);
                                state.Buttons=s.Input.Pad.Buttons.ToString();
                                var p=s.Input.Pad;state.Axes=$"L {p.LX},{p.LY} R {p.RX},{p.RY} T {p.LT},{p.RT}";
                                var m=s.Telemetry;state.Motion=m is null?"":$"G {m.GX},{m.GY},{m.GZ} A {m.AX},{m.AY},{m.AZ}";
                                if(s.Input.HasTelemetry) {
                                    if(config.InputOnly)state.Phase="Background input active (service probe)";
                                    else if(s.SteamActive)state.Phase="Active — GameSir to Steam Controller";
                                    else if(started.ElapsedMilliseconds>=nextOutputAttempt) {
                                        nextOutputAttempt=started.ElapsedMilliseconds+15000;
                                        state.Phase="Connecting virtual Steam Controller";ServiceFiles.Publish(state);
                                        try {
                                            if(hiding is null) {
                                                hiding=new PhysicalHideLease(session.NativeInstanceId,device.PhysicalKey);
                                                // First pairing: close and reopen once with the complete hide list
                                                // already active, so applications cannot keep an earlier XInput handle.
                                                state.Phase="First pairing; closing previous physical input connection";
                                                ServiceFiles.Log(state.Phase);ServiceFiles.Publish(state);break;
                                            }
                                            if(!string.Equals(hiding.Root,session.NativeInstanceId,StringComparison.OrdinalIgnoreCase))throw new IOException("The paired controller instance changed.");
                                            state.HiddenInstance=session.NativeInstanceId;
                                            await Task.Run(()=>session.StartSteam(physicalInputHidden:true),stop);
                                            outputError="";state.Attention="";
                                        } catch(Exception ex) {
                                            outputError="Virtual output: "+ex.Message;state.Attention="output";ServiceFiles.Log(outputError);
                                            hiding?.Dispose();hiding=null;state.HiddenInstance="";
                                        }
                                    } else if(outputError.Length!=0)state.Phase=outputError;
                                }
                                ServiceFiles.Publish(state);
                                if(started.Elapsed>TimeSpan.FromSeconds(45) && s.TelemetryPackets==0)throw new IOException("No GameSir telemetry within 45 seconds.");
                                await Task.Delay(100,stop);
                            }
                        } finally {await session.StopAsync();state.HiddenInstance="";}
                    } finally {
                        if(!stop.IsCancellationRequested)Ended(gate.SessionEnded(received,hidingApplies));
                    }
                    if(received)lastError="";
                    await Task.Delay(2000,stop);
                } catch(OperationCanceledException) when(stop.IsCancellationRequested) {break;}
                catch(Exception ex) {
                    state.Phase=ex.Message;
                    // A repeated identical failure is logged once, not on every attempt.
                    if(ex.Message!=lastError){lastError=ex.Message;ServiceFiles.Log("Connection: "+ex.Message);}
                    ServiceFiles.Publish(state);await Task.Delay(2000,stop);
                }
            }
        } catch(OperationCanceledException) when(stop.IsCancellationRequested) { }
        catch(Exception ex) {ServiceFiles.Log("Service error: "+ex);}
        finally {
            hiding?.Dispose();
            state.Phase="Stopped";state.Connected=state.TelemetryFresh=state.VirtualController=false;
            state.Attention="";
            ServiceFiles.Log("Service stopped; owned controller session closed.");ServiceFiles.Publish(state);
        }
    }
}
