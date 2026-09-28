using G7Bridge.Core;
using L = G7Bridge.Core.UiLanguage;
using System.ServiceProcess;
using System.Text.Json;

namespace G7Bridge.Windows;

internal sealed class ServiceForm : Form
{
    internal const string ShowSignal="Local\\Grimm.G7Bridge.Show";
    private readonly EventWaitHandle showSignal=new(false,EventResetMode.AutoReset,ShowSignal);
    private RegisteredWaitHandle? showWait;
    private readonly Label phase=new(){AutoSize=true,Font=new("Segoe UI",14,FontStyle.Bold),Margin=new(0,5,0,7)};
    private readonly Label details=new(){AutoSize=true,MaximumSize=new(730,0),Margin=new(0,0,0,8)};
    private readonly Label readback=new(){AutoSize=true,Font=new("Segoe UI",10,FontStyle.Bold),Margin=new(0,0,0,8)};
    private readonly Label input=new(){AutoSize=true,Margin=new(0,7,0,4)};
    private readonly Label rumbleResult=new(){AutoSize=true,MaximumSize=new(730,0),ForeColor=Color.DimGray,Margin=new(0,3,0,4)};
    private readonly ControllerDiagram diagram=new();
    private readonly Button power=new(){Text=L.Text("Turn on","Aanzetten"),AutoSize=true,Padding=new(10,4,10,4)};
    private readonly Button rumble=new(){Text=L.Text("Test rumble (0.4 s)","Triltest (0,4 s)"),AutoSize=true,Padding=new(10,4,10,4),Enabled=false};
    private readonly NotifyIcon tray=new(){Text="G7 Bridge",Icon=SystemIcons.Application,Visible=true};
    private readonly System.Windows.Forms.Timer statusTimer=new(){Interval=350};
    private readonly System.Windows.Forms.Timer inputTimer=new(){Interval=33};
    private readonly bool previewMode;
    private readonly VirtualControllerClient? controller;
    private bool busy,closing,rumbleBusy,autoHide=true,serviceRunning;
    private string? lastProblem;
    private ServiceSnapshot? last;

    internal ServiceForm(bool startAutomatically=true)
    {
        previewMode=!startAutomatically;
        if(!previewMode)controller=new VirtualControllerClient();
        Text="G7 Bridge";ClientSize=new(800,760);MinimumSize=new(690,580);StartPosition=FormStartPosition.CenterScreen;
        Font=new("Segoe UI",10);BackColor=Color.White;
        var panel=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new(20),ColumnCount=1,RowCount=9,AutoScroll=true};
        panel.ColumnStyles.Add(new(SizeType.Percent,100));
        for(int i=0;i<9;i++)panel.RowStyles.Add(new(i==4?SizeType.Percent:SizeType.AutoSize,i==4?100:0));
        panel.Controls.Add(new Label{Text="GameSir G7 Pro",Font=new("Segoe UI",20,FontStyle.Bold),AutoSize=true,Margin=new(0,0,0,4)},0,0);
        panel.Controls.Add(phase,0,1);panel.Controls.Add(details,0,2);panel.Controls.Add(readback,0,3);panel.Controls.Add(diagram,0,4);panel.Controls.Add(input,0,5);
        var row=new FlowLayoutPanel{AutoSize=true,Dock=DockStyle.Fill,WrapContents=true,Margin=new(0)};
        row.Controls.Add(power);row.Controls.Add(rumble);
        var save=new Button{Text=L.Text("Save diagnostics","Diagnose opslaan"),AutoSize=true,Padding=new(8,4,8,4)};
        save.Click+=(_,_)=>Export();row.Controls.Add(save);panel.Controls.Add(row,0,6);panel.Controls.Add(rumbleResult,0,7);
        panel.Controls.Add(new Label{Text=L.Text("Minimize to keep playing. Exit to stop the bridge. Configure game actions in Steam Input.","Minimaliseren laat de bridge aan. Afsluiten stopt de bridge. Spelacties stel je in via Steam Input."),AutoSize=true,MaximumSize=new(730,0),ForeColor=Color.DimGray,Margin=new(0,6,0,0)},0,8);
        Controls.Add(panel);
        var menu=new ContextMenuStrip();menu.Items.Add("Open G7 Bridge",null,(_,_)=>ShowWindow());
        menu.Items.Add(L.Text("Turn off","Uitschakelen"),null,async(_,_)=>await Toggle(false));menu.Items.Add(L.Text("Exit","Afsluiten"),null,(_,_)=>Close());
        tray.ContextMenuStrip=menu;tray.DoubleClick+=(_,_)=>ShowWindow();
        power.Click+=async(_,_)=>await Toggle(GetState()!=ServiceControllerStatus.Running);
        rumble.Click+=async(_,_)=>await TestRumble();
        Resize+=(_,_)=>{if(WindowState==FormWindowState.Minimized)Hide();};
        VisibleChanged+=(_,_)=>{if(previewMode)return;inputTimer.Enabled=Visible;UpdateReadback();};
        FormClosing+=OnClosing;
        statusTimer.Tick+=(_,_)=>RefreshStatus();inputTimer.Tick+=(_,_)=>UpdateReadback();
        if(!previewMode)statusTimer.Start();else tray.Visible=false;
        if(startAutomatically)Shown+=async(_,_)=>{diagram.Focus();await Toggle(true);};
        if(!previewMode)showWait=ThreadPool.RegisterWaitForSingleObject(showSignal,(_,_)=>{
            try{if(!IsDisposed && IsHandleCreated)BeginInvoke(ShowWindow);}catch(InvalidOperationException){ }
        },null,Timeout.Infinite,false);
        rumbleResult.Text=L.Text("The test briefly vibrates both main motors.","De test laat beide hoofdmotoren kort trillen.");
        readback.Text=L.Text("Waiting for virtual controller input","Wachten op invoer van de virtuele controller");
    }

    private static ServiceControllerStatus? GetState()
    {
        try{using var svc=new ServiceController(ServiceFiles.Name);return svc.Status;}
        catch(InvalidOperationException){return null;}
    }
    private bool Current=>last is not null && (DateTimeOffset.Now-last.Updated).TotalSeconds is >=0 and <3;
    private bool Ready=>serviceRunning && Current && last!.VirtualController && last.TelemetryFresh && last.OutputReports>3;

    private async Task Toggle(bool start)
    {
        if(previewMode || busy || closing)return;
        diagram.Focus();busy=true;power.Enabled=false;lastProblem=null;
        if(!start){controller?.SetEnabled(false);rumble.Enabled=false;}
        try {
            if(GetState() is null) {
                phase.Text=L.Text("One-time setup","Eenmalige installatie");details.Text=L.Text("Windows will ask for permission to install the controller service and required drivers.","Windows vraagt eenmalig toestemming voor de controllerdienst en de benodigde drivers.");
                string sid=System.Security.Principal.WindowsIdentity.GetCurrent().User!.Value;
                using var setup=Program.StartElevated("--install-service "+sid)??throw new IOException(L.Text("Setup could not start.","Installatie kon niet starten."));
                await setup.WaitForExitAsync();
                if(setup.ExitCode==3010)throw new IOException(L.Text("Restart Windows to finish driver installation, then open G7 Bridge.","Herstart Windows om de driverinstallatie te voltooien en open daarna G7 Bridge."));
                if(setup.ExitCode!=0)throw new IOException(L.Text("Setup did not finish. See setup-error.txt in %ProgramData%\\G7Bridge.","Installatie niet voltooid. Zie setup-error.txt in %ProgramData%\\G7Bridge."));
            }
            await Task.Run(()=>{
                using var svc=new ServiceController(ServiceFiles.Name);
                if(start && svc.Status==ServiceControllerStatus.Stopped){svc.Start();svc.WaitForStatus(ServiceControllerStatus.Running,TimeSpan.FromSeconds(20));}
                if(!start && svc.Status!=ServiceControllerStatus.Stopped){svc.Stop(stopDependentServices:false);svc.WaitForStatus(ServiceControllerStatus.Stopped,TimeSpan.FromSeconds(20));}
            });
            if(!start)autoHide=false;
        } catch(Exception ex){autoHide=false;lastProblem=ex.Message;phase.Text=L.Text("Attention needed","Aandacht nodig");details.Text=ex.Message;}
        finally{busy=false;power.Enabled=true;RefreshStatus();}
    }

    private void RefreshStatus()
    {
        if(closing || previewMode)return;
        try {
            var state=GetState();serviceRunning=state==ServiceControllerStatus.Running;
            power.Text=serviceRunning?L.Text("Turn off","Uitzetten"):state is null?L.Text("Install","Installeren"):L.Text("Turn on","Aanzetten");
            if(lastProblem is not null){phase.Text=L.Text("Attention needed","Aandacht nodig");details.Text=lastProblem;return;}
            if(state is null){last=null;if(!busy){phase.Text=L.Text("Not installed yet","Nog niet geïnstalleerd");details.Text=L.Text("Install the bridge once. After that, just open it.","Installeer de bridge één keer. Daarna hoef je hem alleen te openen.");}return;}
            if(state==ServiceControllerStatus.Stopped){last=null;phase.Text=L.Text("Turned off","Uitgeschakeld");details.Text=L.Text("The physical controller is available for normal use.","De fysieke controller is beschikbaar voor normaal gebruik.");tray.Text=L.Text("G7 Bridge — off","G7 Bridge — uit");return;}
            using var file=File.Open(ServiceFiles.StatusPath,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);
            last=JsonSerializer.Deserialize<ServiceSnapshot>(file);
            if(last is null)return;
            phase.Text=Ready?L.Text("Ready to play","Klaar om te spelen"):!Current?L.Text("Waiting for the controller service","De controllerdienst reageert nog niet"):last.Connected?L.Text("Preparing controller…","Controller voorbereiden…"):L.Text("Waiting for the G7 Pro","Wachten op de G7 Pro");
            phase.ForeColor=Ready?Color.FromArgb(25,113,65):Color.FromArgb(100,75,20);
            details.Text=Ready?L.Text("Extra buttons and gyro can be assigned in Steam Input.","Extra knoppen en gyro kun je in Steam Input toewijzen."):last.Connected?L.Text("Connection and gyro activation are handled automatically.","Verbinden en gyrostart worden automatisch afgehandeld."):L.Text("Turn on the controller and keep its 2.4 GHz receiver connected.","Zet de controller aan en laat de 2,4GHz-dongle aangesloten.");
            if(!Ready && last.Attention.Length!=0) {
                phase.Text=L.Text("Attention needed","Aandacht nodig");
                details.Text=last.Attention switch {
                    "close-nexus"=>L.Text("Close GameSir Nexus so the bridge can connect.","Sluit GameSir Nexus zodat de bridge verbinding kan maken."),
                    "multiple-controllers"=>L.Text("More than one GameSir receiver was found. Connect only the receiver you want to use.","Er is meer dan één GameSir-ontvanger gevonden. Sluit alleen de gewenste ontvanger aan."),
                    "driver"=>L.Text("The controller drivers are not ready. Restart Windows after installation. Save diagnostics if this persists.","De controllerdrivers zijn nog niet beschikbaar. Herstart Windows na installatie. Sla de diagnose op als dit blijft gebeuren."),
                    _=>L.Text("The virtual controller could not start. Save diagnostics for the error details.","De virtuele controller kon niet starten. Sla de diagnose op voor de foutdetails.")
                };
            }
            tray.Text=last.VirtualController && Current?L.Text("G7 Bridge — active","G7 Bridge — actief"):L.Text("G7 Bridge — connecting","G7 Bridge — verbinden");
            if(autoHide && Ready){autoHide=false;Hide();}
        } catch(IOException) { } catch(UnauthorizedAccessException) { } catch(JsonException) { }
        finally {UpdateReadback();}
    }

    private void UpdateReadback()
    {
        if(previewMode || closing || controller is null)return;
        controller.SetEnabled(Visible && serviceRunning && Current && last!.VirtualController && !busy);
        var observed=controller.Observation;diagram.Frame=observed.Input;
        rumble.Enabled=!busy && !rumbleBusy && Ready && observed.CanRumble;
        readback.Text=observed.Input.Fresh?L.Text("Virtual Steam Controller · Windows input","Virtuele Steam Controller · Windows-invoer"):
            observed.Error is null?L.Text("Waiting for virtual controller input","Wachten op invoer van de virtuele controller"):
            L.Text("Virtual input unavailable — save diagnostics for details","Virtuele invoer niet beschikbaar — sla de diagnose op voor details");
        readback.ForeColor=observed.Input.Fresh?Color.FromArgb(21,128,101):Color.DimGray;
        string buttons=observed.Input.Fresh && observed.Input.LastPressed!=PadButtons.None?observed.Input.LastPressed.ToString():"—";
        string battery=Current && last?.Battery is byte percent?$"{percent}%":"—";
        input.Text=$"{L.Text("Last press","Laatste knop")}: {buttons}     ·     {L.Text("Battery","Accu")}: {battery}";
    }

    private async Task TestRumble()
    {
        if(previewMode || rumbleBusy || controller is null || !Ready || !controller.Observation.CanRumble)return;
        diagram.Focus();rumbleBusy=true;rumble.Enabled=false;
        rumbleResult.Text=L.Text("Testing both main motors…","Beide hoofdmotoren testen…");
        try {
            await controller.TestRumbleAsync();
            if(!closing)rumbleResult.Text=L.Text("Rumble test sent. The pulse has finished.","Triltest verzonden. De puls is afgelopen.");
        } catch(OperationCanceledException){if(!closing)rumbleResult.Text=L.Text("Rumble test stopped.","Triltest gestopt.");}
        catch(Exception ex){if(!closing)rumbleResult.Text=L.Text("Rumble test failed: ","Triltest mislukt: ")+ex.Message;}
        finally{rumbleBusy=false;if(!closing)UpdateReadback();}
    }

    private void ShowWindow(){autoHide=false;Show();WindowState=FormWindowState.Normal;Activate();diagram.Focus();}
    internal void RenderPreview(string path)
    {
        autoHide=false;statusTimer.Stop();inputTimer.Stop();ShowInTaskbar=false;StartPosition=FormStartPosition.Manual;
        phase.Text=L.Text("Interface preview","Voorbeeld van de interface");
        details.Text=L.Text("Sample input for layout review. No controller is accessed.","Voorbeeldinvoer voor de opmaak. De controller wordt niet uitgelezen.");
        readback.Text=L.Text("Virtual Steam Controller · sample input","Virtuele Steam Controller · voorbeeldinvoer");
        diagram.Frame=new(true,new(PadButtons.A|PadButtons.L4|PadButtons.R5|PadButtons.RB|PadButtons.R3,-16384,8192,12000,-14000,24000,8000),120,PadButtons.L4,new(1311,-819,410,0,16384,0));
        input.Text=L.Text("Last press: L4     ·     Battery: 75%","Laatste knop: L4     ·     Accu: 75%");
        power.Text=L.Text("Turn off","Uitzetten");rumble.Enabled=true;
        Location=new Point(SystemInformation.VirtualScreen.Right+1000,SystemInformation.VirtualScreen.Bottom+1000);
        Show();Application.DoEvents();PerformLayout();
        using var bitmap=new Bitmap(Width,Height);DrawToBitmap(bitmap,new Rectangle(0,0,Width,Height));bitmap.Save(path,System.Drawing.Imaging.ImageFormat.Png);
    }
    protected override bool ShowWithoutActivation=>true;
    protected override void Dispose(bool disposing)
    {
        if(disposing){if(controller is not null)_=controller.StopAsync();showWait?.Unregister(null);showSignal.Dispose();statusTimer.Dispose();inputTimer.Dispose();tray.Dispose();}
        base.Dispose(disposing);
    }
    private void Export()
    {
        using var save=new SaveFileDialog{FileName=L.Text("G7Bridge-diagnostics.json","G7Bridge-diagnose.json"),Filter=L.Text("Diagnostics|*.json","Diagnose|*.json")};
        if(save.ShowDialog(this)==DialogResult.OK)File.WriteAllText(save.FileName,JsonSerializer.Serialize(new{Service=last,VirtualController=controller?.Observation},new JsonSerializerOptions{WriteIndented=true}));
        diagram.Focus();
    }
    private async void OnClosing(object? sender,FormClosingEventArgs e)
    {
        if(previewMode || closing)return;e.Cancel=true;closing=true;statusTimer.Stop();inputTimer.Stop();Enabled=false;
        try {
            if(controller is not null)await controller.StopAsync();
            await Task.Run(()=>{if(GetState() is not { } state || state==ServiceControllerStatus.Stopped)return;using var svc=new ServiceController(ServiceFiles.Name);svc.Stop(stopDependentServices:false);svc.WaitForStatus(ServiceControllerStatus.Stopped,TimeSpan.FromSeconds(20));});
        } catch(Exception ex){MessageBox.Show(this,L.Text("The service could not be stopped cleanly: ","De dienst kon niet netjes worden gestopt: ")+ex.Message,"G7 Bridge");}
        finally{tray.Visible=false;tray.Dispose();Close();}
    }
}
