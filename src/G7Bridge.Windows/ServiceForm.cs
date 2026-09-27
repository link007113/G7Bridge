using L = G7Bridge.Core.UiLanguage;
using System.ServiceProcess;
using System.Text.Json;

namespace G7Bridge.Windows;

internal sealed class ServiceForm : Form
{
    internal const string ShowSignal="Local\\Grimm.G7Bridge.Show";
    private readonly EventWaitHandle showSignal=new(false,EventResetMode.AutoReset,ShowSignal);
    private RegisteredWaitHandle? showWait;
    private readonly Label phase=new(){AutoSize=true,MaximumSize=new(565,0),Font=new("Segoe UI",14,FontStyle.Bold),Margin=new(0,14,0,12)};
    private readonly Label details=new(){AutoSize=true,MaximumSize=new(565,0),Margin=new(0,0,0,16)};
    private readonly Label input=new(){AutoSize=true,MaximumSize=new(565,0),Margin=new(0,0,0,12)};
    private readonly Button power=new(){Text=L.Text("Turn on", "Aanzetten"),AutoSize=true,Padding=new(12,5,12,5)};
    private readonly NotifyIcon tray=new(){Text="G7 Bridge",Icon=SystemIcons.Application,Visible=true};
    private readonly System.Windows.Forms.Timer timer=new(){Interval=350};
    private bool busy,closing,autoHide=true;
    private readonly bool previewMode;
    private string? lastProblem;
    private ServiceSnapshot? last;
    internal ServiceForm(bool startAutomatically=true)
    {
        previewMode=!startAutomatically;
        Text="G7 Bridge";ClientSize=new(610,360);MinimumSize=new(620,390);StartPosition=FormStartPosition.CenterScreen;
        Font=new("Segoe UI",10);BackColor=Color.White;
        var panel=new FlowLayoutPanel{Dock=DockStyle.Fill,Padding=new(22),FlowDirection=FlowDirection.TopDown,WrapContents=false,AutoScroll=true};
        panel.Controls.Add(new Label{Text="GameSir G7 Pro",Font=new("Segoe UI",20,FontStyle.Bold),AutoSize=true});
        panel.Controls.Add(phase);panel.Controls.Add(details);panel.Controls.Add(input);
        var row=new FlowLayoutPanel{AutoSize=true,WrapContents=false};row.Controls.Add(power);
        var show=new Button{Text=L.Text("Save diagnostics", "Diagnose opslaan"),AutoSize=true,Padding=new(8,5,8,5)};show.Click+=(_,_)=>Export();row.Controls.Add(show);panel.Controls.Add(row);
        panel.Controls.Add(new Label{Text=L.Text("Minimizing keeps the bridge running. Exiting stops it and restores the physical controller.", "Minimaliseren laat de bridge aan. Afsluiten stopt de bridge en geeft de fysieke controller vrij."),AutoSize=true,MaximumSize=new(565,0),ForeColor=Color.DimGray,Margin=new(0,12,0,0)});
        Controls.Add(panel);
        var menu=new ContextMenuStrip();menu.Items.Add("Open G7 Bridge",null,(_,_)=>ShowWindow());
        menu.Items.Add(L.Text("Turn off", "Uitschakelen"),null,async(_,_)=>await Toggle(false));menu.Items.Add(L.Text("Exit", "Afsluiten"),null,(_,_)=>Close());
        tray.ContextMenuStrip=menu;tray.DoubleClick+=(_,_)=>ShowWindow();
        power.Click+=async(_,_)=>await Toggle(GetState()!=ServiceControllerStatus.Running);
        Resize+=(_,_)=>{if(WindowState==FormWindowState.Minimized)Hide();};
        FormClosing+=OnClosing;
        timer.Tick+=(_,_)=>RefreshStatus();if(!previewMode)timer.Start();
        if(previewMode)tray.Visible=false;
        if(startAutomatically)Shown+=async(_,_)=>await Toggle(true);
        if(!previewMode)showWait=ThreadPool.RegisterWaitForSingleObject(showSignal,(_,_)=>{
            try{if(!IsDisposed && IsHandleCreated)BeginInvoke(ShowWindow);}catch(InvalidOperationException){ }
        },null,Timeout.Infinite,false);
    }
    private static ServiceControllerStatus? GetState()
    {
        try{using var svc=new ServiceController(ServiceFiles.Name);return svc.Status;}
        catch(InvalidOperationException){return null;}
    }
    private async Task Toggle(bool start)
    {
        if(busy || closing)return;busy=true;power.Enabled=false;lastProblem=null;
        try {
            if(GetState() is null) {
                phase.Text=L.Text("One-time setup", "Eenmalige installatie");details.Text=L.Text("Windows will ask for permission to install the controller service and required drivers.", "Windows vraagt eenmalig toestemming voor de controllerdienst en de benodigde drivers.");
                string sid=System.Security.Principal.WindowsIdentity.GetCurrent().User!.Value;
                using var setup=Program.StartElevated("--install-service "+sid)??throw new IOException(L.Text("Setup could not start.", "Installatie kon niet starten."));
                await setup.WaitForExitAsync();
                if(setup.ExitCode==3010)throw new IOException(L.Text("Restart Windows to finish driver installation, then open G7 Bridge.","Herstart Windows om de driverinstallatie te voltooien en open daarna G7 Bridge."));
                if(setup.ExitCode!=0)throw new IOException(L.Text("Setup did not finish. See setup-error.txt in %ProgramData%\\G7Bridge.", "Installatie niet voltooid. Zie setup-error.txt in %ProgramData%\\G7Bridge."));
            }
            await Task.Run(()=>{
                using var svc=new ServiceController(ServiceFiles.Name);
                if(start && svc.Status==ServiceControllerStatus.Stopped){svc.Start();svc.WaitForStatus(ServiceControllerStatus.Running,TimeSpan.FromSeconds(20));}
                if(!start && svc.Status!=ServiceControllerStatus.Stopped){svc.Stop(stopDependentServices:false);svc.WaitForStatus(ServiceControllerStatus.Stopped,TimeSpan.FromSeconds(20));}
            });
            if(!start)autoHide=false;
        } catch(Exception ex){autoHide=false;lastProblem=ex.Message;phase.Text=L.Text("Attention needed", "Aandacht nodig");details.Text=ex.Message;}
        finally{busy=false;power.Enabled=true;RefreshStatus();}
    }
    private void RefreshStatus()
    {
        if(closing)return;
        try {
            var state=GetState();power.Text=state==ServiceControllerStatus.Running?L.Text("Turn off", "Uitzetten"):state is null?L.Text("Install", "Installeren"):L.Text("Turn on", "Aanzetten");
            if(lastProblem is not null){phase.Text=L.Text("Attention needed", "Aandacht nodig");details.Text=lastProblem;return;}
            if(state is null){if(!busy){phase.Text=L.Text("Not installed yet", "Nog niet geïnstalleerd");details.Text=L.Text("Install the bridge once. After that, just open it.", "Installeer de bridge één keer. Daarna hoef je hem alleen te openen.");}return;}
            if(state==ServiceControllerStatus.Stopped){phase.Text=L.Text("Turned off", "Uitgeschakeld");details.Text=L.Text("The physical controller is available for normal use.", "De fysieke controller is beschikbaar voor normaal gebruik.");input.Text="";tray.Text=L.Text("G7 Bridge — off", "G7 Bridge — uit");return;}
            using var file=File.Open(ServiceFiles.StatusPath,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);
            last=JsonSerializer.Deserialize<ServiceSnapshot>(file);
            if(last is null)return;
            bool current=(DateTimeOffset.Now-last.Updated).TotalSeconds<3;
            bool ready=current && last.VirtualController && last.TelemetryFresh && last.OutputReports>3;
            phase.Text=ready?L.Text("Ready to play", "Klaar om te spelen"):!current?L.Text("Waiting for the controller service", "De controllerdienst reageert nog niet"):last.Connected?L.Text("Preparing controller…", "Controller voorbereiden…"):L.Text("Waiting for the G7 Pro", "Wachten op de G7 Pro");
            phase.ForeColor=last.VirtualController && last.TelemetryFresh && current?Color.FromArgb(25,113,65):Color.FromArgb(100,75,20);
            details.Text=ready?L.Text("Steam sees a Steam Controller. Extra buttons and gyro are available.", "Steam ziet een Steam Controller. Extra knoppen en gyro zijn beschikbaar."):last.Connected?L.Text("Connection and gyro activation are handled automatically.", "Verbinden en gyrostart worden automatisch afgehandeld."):L.Text("Turn on the controller and keep its 2.4 GHz receiver connected.", "Zet de controller aan en laat de 2,4GHz-dongle aangesloten.");
            if(!ready && last.Attention.Length!=0) {
                phase.Text=L.Text("Attention needed","Aandacht nodig");
                details.Text=last.Attention switch {
                    "close-nexus"=>L.Text("Close GameSir Nexus so the bridge can connect.","Sluit GameSir Nexus zodat de bridge verbinding kan maken."),
                    "multiple-controllers"=>L.Text("More than one GameSir receiver was found. Connect only the G7 Pro receiver you want to use.","Er is meer dan één GameSir-ontvanger gevonden. Sluit alleen de gewenste G7 Pro-ontvanger aan."),
                    "driver"=>L.Text("The controller drivers are not ready. Restart Windows after installation. Save diagnostics if this persists.","De controllerdrivers zijn nog niet beschikbaar. Herstart Windows na installatie. Sla de diagnose op als dit blijft gebeuren."),
                    _=>L.Text("The virtual controller could not start. Save diagnostics for the error details.","De virtuele controller kon niet starten. Sla de diagnose op voor de foutdetails.")
                };
            }
            input.Text=ready?$"{L.Text("Pressed", "Ingedrukt")}: {(last.Buttons=="None"?L.Text("none", "geen"):last.Buttons)}\n{L.Text("Battery", "Accu")}: {(last.Battery is byte battery?$"{battery}%":L.Text("unknown", "onbekend"))}":"";
            tray.Text=last.VirtualController && current?L.Text("G7 Bridge — active", "G7 Bridge — actief"):L.Text("G7 Bridge — connecting", "G7 Bridge — verbinden");
            if(autoHide && ready){autoHide=false;Hide();}
        } catch(IOException) { } catch(UnauthorizedAccessException) { } catch(JsonException) { }
    }
    private void ShowWindow(){autoHide=false;Show();WindowState=FormWindowState.Normal;Activate();}
    internal void RenderPreview(string path)
    {
        autoHide=false;timer.Stop();ShowInTaskbar=false;StartPosition=FormStartPosition.Manual;
        phase.Text=L.Text("Ready to play","Klaar om te spelen");
        details.Text=L.Text("Steam sees a Steam Controller. Extra buttons and gyro are available.","Steam ziet een Steam Controller. Extra knoppen en gyro zijn beschikbaar.");
        input.Text=L.Text("Pressed: none\nBattery: 75%","Ingedrukt: geen\nAccu: 75%");
        power.Text=L.Text("Turn off","Uitzetten");
        Location=new Point(SystemInformation.VirtualScreen.Right+1000,SystemInformation.VirtualScreen.Bottom+1000);
        Show();Application.DoEvents();PerformLayout();
        using var bitmap=new Bitmap(Width,Height);DrawToBitmap(bitmap,new Rectangle(0,0,Width,Height));bitmap.Save(path,System.Drawing.Imaging.ImageFormat.Png);
    }
    protected override bool ShowWithoutActivation=>true;
    protected override void Dispose(bool disposing)
    {
        if(disposing){showWait?.Unregister(null);showSignal.Dispose();timer.Dispose();tray.Dispose();}
        base.Dispose(disposing);
    }
    private void Export()
    {
        using var save=new SaveFileDialog{FileName=L.Text("G7Bridge-diagnostics.json", "G7Bridge-diagnose.json"),Filter=L.Text("Diagnostics|*.json", "Diagnose|*.json")};
        if(save.ShowDialog(this)==DialogResult.OK)File.WriteAllText(save.FileName,JsonSerializer.Serialize(last,new JsonSerializerOptions{WriteIndented=true}));
    }
    private async void OnClosing(object? sender,FormClosingEventArgs e)
    {
        if(previewMode)return;
        if(closing)return;e.Cancel=true;closing=true;timer.Stop();Enabled=false;
        try {
            await Task.Run(()=>{if(GetState() is not { } state || state==ServiceControllerStatus.Stopped)return;using var svc=new ServiceController(ServiceFiles.Name);svc.Stop(stopDependentServices:false);svc.WaitForStatus(ServiceControllerStatus.Stopped,TimeSpan.FromSeconds(20));});
        } catch(Exception ex){MessageBox.Show(this,L.Text("The service could not be stopped cleanly: ", "De dienst kon niet netjes worden gestopt: ")+ex.Message,"G7 Bridge");}
        finally{tray.Visible=false;tray.Dispose();Close();}
    }
}
