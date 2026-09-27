using G7Bridge.Core;
using System.Diagnostics;
using System.Text;

namespace G7Bridge.Windows;

internal sealed class MainForm : Form
{
    private readonly BridgeSession session=new();
    private readonly ComboBox method=Combo(),devices=Combo(),buttonTarget=Combo(),candidate=Combo();
    private readonly Label status=new() { AutoSize=false,Width=790,Height=180,Padding=new(12),BackColor=Color.FromArgb(242,245,248) };
    private readonly Label hint=new() { AutoSize=true,MaximumSize=new(810,0),ForeColor=Color.FromArgb(65,75,87) };
    private readonly Label backgroundAccess=new() { AutoSize=true,MaximumSize=new(800,0),Margin=new(3,6,3,6) };
    private readonly TextBox log=new() { Multiline=true,ReadOnly=true,Dock=DockStyle.Fill,ScrollBars=ScrollBars.Both,WordWrap=false,Font=new("Consolas",9) };
    private readonly ListBox mappings=new() { Width=790,Height=230 };
    private readonly NumericUpDown gyro=Number(16m,0.001m,32768m,3),accel=Number(8192,1,65536,1);
    private readonly ComboBox[] axes=[Combo(),Combo(),Combo()];
    private readonly CheckBox[] signs=[new(){Text="X omkeren",AutoSize=true},new(){Text="Y omkeren",AutoSize=true},new(){Text="Z omkeren",AutoSize=true}];
    private readonly System.Windows.Forms.Timer displayTimer=new(){Interval=100};
    private byte[][]? released,held;
    private bool closing,busy;
    private long previousPackets,previousTelemetry;
    private DateTime rateTime=DateTime.UtcNow;
    private int packetsPerSecond,motionPerSecond;
    private readonly Queue<string> logLines=new();
    private readonly NotifyIcon tray=new(){Text="G7 Bridge",Icon=SystemIcons.Application};

    public MainForm()
    {
        Text="G7 Bridge — versie 0.4.0 · native invoer";
        Width=930;Height=790;MinimumSize=new(900,720);StartPosition=FormStartPosition.CenterScreen;
        Font=new("Segoe UI",10);BackColor=Color.White;
        var root=new TableLayoutPanel { Dock=DockStyle.Fill,RowCount=3,ColumnCount=1,Padding=new(18) };
        root.RowStyles.Add(new(SizeType.Absolute,76));root.RowStyles.Add(new(SizeType.Percent,100));root.RowStyles.Add(new(SizeType.Absolute,34));
        var header=new Panel { Dock=DockStyle.Fill };
        header.Controls.Add(new Label { Text="GameSir G7 Pro → Steam Controller",Font=new("Segoe UI",18,FontStyle.Bold),AutoSize=true,Location=new(0,0) });
        header.Controls.Add(new Label { Text="2,4GHz-dongle · gyro-uitlezing op de achtergrond aangetoond · Steam-doorgifte in ontwikkeling",AutoSize=true,Location=new(1,40),ForeColor=Color.DimGray });
        root.Controls.Add(header,0,0);
        var tabs=new TabControl { Dock=DockStyle.Fill };
        tabs.TabPages.Add(ConnectionPage());tabs.TabPages.Add(ButtonsPage());tabs.TabPages.Add(MotionPage());tabs.TabPages.Add(LogPage());
        root.Controls.Add(tabs,0,1);
        root.Controls.Add(new Label { Text="Profielen blijven op de controller intact. Trillingen worden in deze versie nog niet doorgestuurd.",AutoSize=true,Padding=new(0,8,0,0),ForeColor=Color.DimGray },0,2);
        Controls.Add(root);
        session.Log+=AppendLog;
        try { session.Settings=SettingsStore.Load(); } catch(Exception ex) { AppendLog("Instellingen niet geladen: "+ex.Message+" Het bestaande bestand blijft bewaard."); }
        PopulateSettings();RefreshMappings();
        displayTimer.Tick+=(_,_)=>UpdateStatus();displayTimer.Start();
        FormClosing+=OnClosing;
        var menu=new ContextMenuStrip();
        menu.Items.Add("Open G7 Bridge",null,(_,_)=>RestoreWindow());
        menu.Items.Add("Afsluiten",null,(_,_)=>Close());
        tray.ContextMenuStrip=menu;tray.DoubleClick+=(_,_)=>RestoreWindow();tray.Visible=true;
        Resize+=(_,_)=>{if(WindowState==FormWindowState.Minimized)Hide();};
        Activated+=(_,_)=>AppendLog($"Venster op voorgrond; gyropakketten: {session.Status.TelemetryPackets}.");
        Deactivate+=(_,_)=>AppendLog($"Venster verliest focus; uitleeswerker blijft actief. Gyropakketten: {session.Status.TelemetryPackets}.");
        AppendLog("Gestart. Er wordt pas met de controller gecommuniceerd nadat je ‘Uitlezen’ kiest.");
        AppendLog($"Achtergrondrechten: beheerder={Program.IsAdmin}; Windows-ontwikkelaarsmodus={Program.DeveloperModeEnabled}.");
        UpdateBackgroundAccess();
    }

    private TabPage ConnectionPage()
    {
        var page=Page("Verbinding",out var panel);
        AddText(panel,"Sluit Nexus en eerdere G7 Bridge-versies. Gyro via de bestaande Xbox-driver werkt ook bij wegklikken als Windows-ontwikkelaarsmodus aanstaat en deze app als beheerder draait.");
        panel.Controls.Add(backgroundAccess);
        panel.Controls.Add(Row(Button("Windows-ontwikkelaarsmodus",()=>{Process.Start(new ProcessStartInfo("ms-settings:developers"){UseShellExecute=true});return Task.CompletedTask;}),Button("Herstart als beheerder",RelaunchAdmin)));
        AddText(panel,"De knop opent Windows-instellingen; je bepaalt daar zelf of je ontwikkelaarsmodus inschakelt. Die Windows-instelling blijft aan totdat je hem zelf weer uitzet. G7 Bridge verandert hem niet automatisch.");
        method.Items.AddRange(["Windows GameInput + GameSir GIP","USB / UsbDk — eerdere experimentele route","Vendor-HID — alleen onderzoek"]);method.SelectedIndex=0;method.Width=440;
        panel.Controls.Add(Row(method,Button("Zoek controller",async()=>await Discover())));
        devices.Width=790;panel.Controls.Add(devices);
        var steamButton=Button("Start Steam-uitvoer",StartSteam);steamButton.Enabled=false;
        method.SelectedIndexChanged+=(_,_)=>steamButton.Enabled=method.SelectedIndex==1;
        panel.Controls.Add(Row(Button("Uitlezen",Connect),Button("Stop",async()=>await session.StopAsync()),steamButton,Button("Stop Steam-uitvoer",()=>{session.StopSteam();return Task.CompletedTask;})));
        panel.Controls.Add(Row(Button("Gyro activeren",()=>{session.RequestGyro();return Task.CompletedTask;}),
            new Label{Text="Eenmalige GameSir-start; de dongle kan kort opnieuw verschijnen.",AutoSize=true,Padding=new(6,7,0,0)}));
        panel.Controls.Add(status);
        panel.Controls.Add(hint);
        AddText(panel,"GameInput 3.5 zit bij de app. Alleen als Windows een runtimefout meldt: GameInput installeren en de bridge opnieuw openen. Installatie gebeurt uitsluitend met de knop hieronder.");
        panel.Controls.Add(Row(Button("GameInput installeren",InstallGameInput),Button("UsbDk installeren",InstallUsbDk),Button("Steam-driver installeren",InstallOutput)));
        AddText(panel,"Kies Uitlezen en daarna eenmaal Gyro activeren als de controller nog in XInput staat. Native Steam-uitvoer wacht op een oplossing voor dubbele controllerinvoer. Profielen en firmware worden niet gewijzigd.");
        return page;
    }

    private TabPage ButtonsPage()
    {
        var page=Page("Knoppen",out var panel);
        AddText(panel,"L4, R4, L5, R5, Share en de gewone knoppen worden automatisch uit de fysieke GameSir-velden gelezen. Deze indeling komt overeen met jouw opname en bestaand protocolonderzoek. Alleen bij een verkeerde knop gebruik je hieronder een lokale afwijking.");
        buttonTarget.Items.AddRange(Enum.GetValues<PadButtons>().Where(v=>v!=PadButtons.None).Cast<object>().ToArray());buttonTarget.SelectedItem=PadButtons.L4;buttonTarget.Width=130;
        panel.Controls.Add(Row(new Label{Text="Doelknop",AutoSize=true,Padding=new(0,6,8,0)},buttonTarget));
        AddText(panel,"Laat eerst alle knoppen los en neem de ruststand op. Houd daarna alleen de gekozen fysieke knop ingedrukt terwijl je ‘Ingedrukt opnemen’ kiest. Laat sticks en triggers verder met rust.");
        panel.Controls.Add(Row(Button("Ruststand opnemen",async()=>{released=await CaptureFrames();held=null;candidate.Items.Clear();AppendLog($"Ruststand: {released.Length} verse pakketten.");}),
            Button("Ingedrukt opnemen",async()=>{
                if(released is null) throw new InvalidOperationException("Neem eerst de ruststand op.");
                held=await CaptureFrames();var candidates=ButtonLearning.Candidates(released,held);candidate.Items.Clear();
                candidate.Items.AddRange(candidates.Cast<object>().ToArray());if(candidates.Count==1)candidate.SelectedIndex=0;
                AppendLog($"Knoponderzoek: {candidates.Count} stabiel veranderde bits.");
                if(candidates.Count==0) throw new InvalidOperationException("Geen afzonderlijke stabiele knopbit gevonden. Neem opnieuw op of exporteer de diagnose.");
                if(candidates.Count>1) MessageBox.Show(this,"Meerdere bits veranderen. Er wordt niets automatisch gekoppeld. Controleer de kandidaat tegenover andere knoppen of exporteer de diagnose voor verdere analyse.","Meerdere signalen");
            })));
        candidate.Width=440;
        panel.Controls.Add(Row(candidate,Button("Koppel gekozen signaal",()=>{
            if(buttonTarget.SelectedItem is not PadButtons target || candidate.SelectedItem is not BitBinding binding) throw new InvalidOperationException("Kies een doelknop en een signaal.");
            var settings=session.Settings;settings.Bindings[target]=binding;Save(settings);RefreshMappings();return Task.CompletedTask;
        }),Button("Verwijder koppeling",()=>{
            if(buttonTarget.SelectedItem is PadButtons target) {var settings=session.Settings;settings.Bindings.Remove(target);Save(settings);RefreshMappings();}
            return Task.CompletedTask;
        })));
        panel.Controls.Add(mappings);
        AddText(panel,"Een koppeling is pas bruikbaar als het signaal uitsluitend bij die fysieke knop hoort. De app weigert dubbele bits, maar jouw controller bepaalt of de extra knop werkelijk zelfstandig wordt gerapporteerd.");
        return page;
    }

    private TabPage MotionPage()
    {
        var page=Page("Gyro",out var panel);
        AddText(panel,"Beginwaarden uit protocolonderzoek: 16 ruwe gyro-eenheden per graad/seconde en 8192 accelerometereenheden per g. De schaal en asrichting op jouw firmware zijn nog niet vastgesteld. Steam bepaalt later de gevoeligheid per spel.");
        panel.Controls.Add(Row(new Label{Text="Gyro-eenheden per °/s",Width=220,AutoSize=false,Height=30},gyro));
        panel.Controls.Add(Row(new Label{Text="Accelerometereenheden per g",Width=220,AutoSize=false,Height=30},accel));
        string[] names=["X","Y","Z"];
        for(int i=0;i<3;i++) {
            axes[i].Items.AddRange(names);axes[i].Width=90;
            panel.Controls.Add(Row(new Label{Text=$"Valve-as {names[i]} gebruikt G7-as",Width=220,Height=30},axes[i],signs[i]));
        }
        panel.Controls.Add(Row(Button("Instellingen opslaan",()=>{
            var s=session.Settings;s.GyroCountsPerDps=(double)gyro.Value;s.AccelCountsPerG=(double)accel.Value;
            s.AxisOrder=axes.Select(a=>a.SelectedIndex).ToArray();s.AxisSign=signs.Select(c=>c.Checked?-1:1).ToArray();Save(s);return Task.CompletedTask;
        }),Button("Rustoffset opnemen",async()=>{
            var samples=(await CaptureFrames()).Select(p=>G7Protocol.TryTelemetry(p,out var t)?t:null).Where(t=>t is not null).Cast<Telemetry>().ToArray();
            if(samples.Length<8) throw new InvalidOperationException("Te weinig verse gyrogegevens.");
            var values=new[]{samples.Select(t=>(double)t.GX).ToArray(),samples.Select(t=>(double)t.GY).ToArray(),samples.Select(t=>(double)t.GZ).ToArray()};
            if(values.Any(v=>v.Max()-v.Min()>128)) throw new InvalidOperationException("De controller bewoog te veel. Leg hem stil neer en probeer opnieuw.");
            var s=session.Settings;s.GyroBias=values.Select(v=>v.Average()).ToArray();Save(s);AppendLog("Rustoffset lokaal opgeslagen: "+string.Join(", ",s.GyroBias.Select(v=>v.ToString("F2"))));
        }),Button("Rustoffset wissen",()=>{var s=session.Settings;s.GyroBias=[0,0,0];Save(s);return Task.CompletedTask;})));
        AddText(panel,"Rustoffset: leg de controller op een stil oppervlak voordat je op opnemen klikt. Dit schrijft alleen naar het lokale instellingenbestand. De firmwarekalibratie blijft intact.");
        AddText(panel,"Alle drie de bronassen moeten precies eenmaal worden gebruikt. Indien de bewegingsrichting in Steam verkeerd is, verander de asvolgorde of het teken hier. De virtuele controller ontvangt echte hoeksnelheid, versnelling en een uit gyro berekende relatieve oriëntatie.");
        return page;
    }

    private TabPage LogPage()
    {
        var page=new TabPage("Diagnose"){Padding=new(10)};
        var export=Button("Exporteer diagnose",Export);export.Dock=DockStyle.Bottom;
        page.Controls.Add(log);page.Controls.Add(export);return page;
    }

    private async Task Discover()
    {
        if(session.IsRunning) throw new InvalidOperationException("Stop eerst de huidige uitleessessie.");
        int route=method.SelectedIndex;
        var found=await Task.Run(()=>route switch {0=>GameInputTransport.Enumerate(),1=>UsbTransport.Enumerate(),_=>HidTransport.Enumerate()});
        devices.Items.Clear();devices.Items.AddRange(found.Cast<object>().ToArray());if(found.Count==1)devices.SelectedIndex=0;
        AppendLog($"{found.Count} passende GameSir-kanalen gevonden.");
        if(found.Count==0) throw new InvalidOperationException("Geen passend GameSir-kanaal gevonden. Zet de controller aan en controleer de gekozen verbindingsroute.");
    }
    private Task Connect()
    {
        if(devices.SelectedItem is not ControllerDevice d) throw new InvalidOperationException("Zoek en kies eerst de GameSir-ontvanger.");
        if(d.Transport=="UsbDk" && !Program.IsAdmin) throw new InvalidOperationException("Herstart als beheerder voor de tijdelijke USB-overname.");
        session.Start(d);return Task.CompletedTask;
    }
    private async Task StartSteam()
    {
        if(!Program.IsAdmin) throw new InvalidOperationException("Herstart als beheerder om een virtuele controller te maken.");
        await Task.Run(()=>session.StartSteam());
    }
    private async Task<byte[][]> CaptureFrames()
    {
        if(!session.Status.Input.HasTelemetry) throw new InvalidOperationException("Start eerst uitlezen en wacht op echte gyrogegevens.");
        var before=session.Status.TelemetryPackets;await Task.Delay(1100);
        var after=session.Status;
        if(!after.Input.HasTelemetry || after.TelemetryPackets-before<8) throw new InvalidOperationException("Onvoldoende verse controllergegevens ontvangen.");
        return session.RecentFrames.TakeLast((int)Math.Min(64,after.TelemetryPackets-before)).ToArray();
    }
    private void Save(BridgeSettings settings) { ButtonLearning.Validate(settings);SettingsStore.Save(settings);session.Settings=settings;AppendLog("Lokale instellingen opgeslagen."); }
    private void PopulateSettings()
    {
        var s=session.Settings;gyro.Value=(decimal)s.GyroCountsPerDps;accel.Value=(decimal)s.AccelCountsPerG;
        for(int i=0;i<3;i++){axes[i].SelectedIndex=s.AxisOrder[i];signs[i].Checked=s.AxisSign[i]<0;}
    }
    private void RefreshMappings()
    {
        mappings.Items.Clear();var s=session.Settings;
        foreach(var key in Enum.GetValues<PadButtons>().Where(v=>v!=PadButtons.None))
            mappings.Items.Add($"{key,-6} {(s.Bindings.TryGetValue(key,out var binding)?binding.ToString():"— automatisch uit fysieke GameSir-velden")}");
    }

    private void UpdateStatus()
    {
        if(closing)return;
        var s=session.Status;double elapsed=(DateTime.UtcNow-rateTime).TotalSeconds;
        if(elapsed>=1) {packetsPerSecond=(int)Math.Max(0,(s.Packets-previousPackets)/elapsed);motionPerSecond=(int)Math.Max(0,(s.TelemetryPackets-previousTelemetry)/elapsed);previousPackets=s.Packets;previousTelemetry=s.TelemetryPackets;rateTime=DateTime.UtcNow;UpdateBackgroundAccess();}
        var p=s.Input.Pad;var m=s.Telemetry;
        status.Text=$"{s.Phase}    |    Steam-uitvoer: {(s.SteamActive?"aan":"uit")}\n"+
            $"Invoerpakketten: {s.Packets} ({packetsPerSecond}/s)    |    Gyropakketten: {s.TelemetryPackets} ({motionPerSecond}/s)\n"+
            $"Batterij: {(m?.Battery is byte b?$"{b}%":"onbekend")}    |    Stickbron: {(s.Input.HasGamepad?"Xbox / GameInput, 16-bit velden":s.Input.HasTelemetry?"GameSir-telemetrie, 8-bit":"geen actuele gegevens")}\n"+
            $"Sticks: L {p.LX}, {p.LY}    R {p.RX}, {p.RY}    Triggers: {p.LT}, {p.RT}\n"+
            $"Gyro (ruw): {(m is null?"—":$"{m.GX}, {m.GY}, {m.GZ}")}    |    Knoppen: {p.Buttons}\n"+
            $"Steam-haptiek ontvangen: {s.Haptics} (doorgifte nog niet ondersteund)";
        hint.Text="Extra knoppen + Share: automatische decoder. "+(!s.Input.HasTelemetry?
            (s.GyroRequested?"GameSir-start is aangevraagd; gyro wacht nog op een actuele gegevensstroom.":"Gyro wacht op een actuele gegevensstroom. Gebruik eenmalig ‘Gyro activeren’."):
            !s.Input.HasGamepad?"De bron levert nu alleen 8-bit sticks.":"Xbox / GameInput en GameSir-gyro zijn beschikbaar.");
    }

    private void UpdateBackgroundAccess()
    {
        bool admin=Program.IsAdmin,developer=Program.DeveloperModeEnabled;
        backgroundAccess.Text=admin && developer
            ? "Achtergrondgyro: beheerdersrechten en ontwikkelaarsmodus zijn aanwezig."
            : $"Achtergrondgyro vereist nog: {string.Join(" en ",new[]{developer?null:"Windows-ontwikkelaarsmodus",admin?null:"herstart als beheerder"}.Where(s=>s is not null))}. Zonder deze rechten valt de gyrosessie bij wegklikken weg.";
        backgroundAccess.ForeColor=admin && developer?Color.DarkGreen:Color.DarkRed;
    }

    private async Task InstallOutput()
    {
        if(session.IsRunning) throw new InvalidOperationException("Stop eerst het uitlezen.");
        using var p=Program.StartElevated("--install-output-driver")??throw new IOException("Installer kon niet starten.");
        await p.WaitForExitAsync();
        if(p.ExitCode!=0) {
            var file=Path.Combine(SettingsStore.DirectoryPath,"driver-install-error.txt");
            throw new IOException(File.Exists(file)?File.ReadAllText(file):"Installatie van virtuele USB-driver is niet voltooid.");
        }
        AppendLog("Installatieactie voor virtuele USB-driver voltooid.");
    }
    private async Task InstallUsbDk()
    {
        if(session.IsRunning) throw new InvalidOperationException("Stop eerst het uitlezen.");
        var file=Path.Combine(AppContext.BaseDirectory,"drivers","UsbDk_1.0.22_x64.msi");
        if(!File.Exists(file)) { Process.Start(new ProcessStartInfo("https://github.com/daynix/UsbDk/releases/tag/v1.00-22"){UseShellExecute=true});return; }
        using var p=Process.Start(new ProcessStartInfo("msiexec.exe") { Arguments=$"/i \"{file}\"",UseShellExecute=true,Verb="runas" });
        if(p is not null) {await p.WaitForExitAsync();if(p.ExitCode is not 0 and not 3010)throw new IOException($"UsbDk-installatie beëindigd met code {p.ExitCode}.");}
        AppendLog("UsbDk-installer afgesloten. Als Windows opnieuw starten vraagt, doe dat voor je de bridge gebruikt.");
    }
    private async Task InstallGameInput()
    {
        if(session.IsRunning)throw new InvalidOperationException("Stop eerst het uitlezen.");
        var file=Path.Combine(AppContext.BaseDirectory,"drivers","GameInputRedist-3.5.278.msi");
        if(!File.Exists(file))throw new FileNotFoundException("De meegeleverde GameInput-installer ontbreekt.",file);
        using var p=Process.Start(new ProcessStartInfo("msiexec.exe") { Arguments=$"/i \"{file}\"",UseShellExecute=true,Verb="runas" });
        if(p is not null){await p.WaitForExitAsync();if(p.ExitCode is not 0 and not 3010)throw new IOException($"GameInput-installatie beëindigd met code {p.ExitCode}.");}
        AppendLog("GameInput-installer afgesloten. Heropen G7 Bridge om de runtime opnieuw te laden.");
    }
    private async Task RelaunchAdmin()
    {
        if(Program.IsAdmin){AppendLog("G7 Bridge draait al als beheerder.");return;}
        await session.StopAsync();
        // The elevated instance waits for this exact process before taking the mutex.
        // Arguments are a fixed option and a numeric PID; no shell command is assembled.
        using var child=Program.StartElevated($"--restart-admin {Environment.ProcessId}",ProcessWindowStyle.Normal)??throw new IOException("Herstart kon niet beginnen.");
        Close();
    }

    private Task Export()
    {
        using var dialog=new SaveFileDialog { FileName="G7Bridge-diagnose.txt",Filter="Tekstbestand|*.txt" };
        if(dialog.ShowDialog(this)!=DialogResult.OK)return Task.CompletedTask;
        var sb=new StringBuilder();sb.AppendLine("G7 Bridge 0.4.0 — door gebruiker geëxporteerde diagnose");sb.AppendLine(DateTimeOffset.Now.ToString("O"));
        sb.AppendLine(status.Text);sb.AppendLine(hint.Text);sb.AppendLine();
        sb.AppendLine("Lokale knopkoppelingen (lege lijst betekent dat nog niets is aangeleerd):");
        foreach(var binding in session.Settings.Bindings)sb.AppendLine($"{binding.Key}: {binding.Value}");
        sb.AppendLine();
        foreach(string line in logLines)sb.AppendLine(line);
        sb.AppendLine("\nLaatste maximaal 64 ruwe controllerpakketten (alle reporttypes):");
        foreach(var p in session.RecentRawFrames)sb.AppendLine(Convert.ToHexString(p));
        sb.AppendLine("\nLaatste maximaal 64 GameSir-telemetriepakketten:");
        foreach(var p in session.RecentFrames)sb.AppendLine(Convert.ToHexString(p));
        if(released is not null) {sb.AppendLine("\nOpgenomen ruststand:");foreach(var p in released)sb.AppendLine(Convert.ToHexString(p));}
        if(held is not null) {sb.AppendLine($"\nOpgenomen ingedrukte knop (huidig doel: {buttonTarget.SelectedItem}):");foreach(var p in held)sb.AppendLine(Convert.ToHexString(p));}
        File.WriteAllText(dialog.FileName,sb.ToString());AppendLog("Diagnose geëxporteerd.");return Task.CompletedTask;
    }

    private void AppendLog(string message)
    {
        if(IsDisposed||closing)return;
        if(InvokeRequired) { try { if(IsHandleCreated)BeginInvoke(()=>AppendLog(message)); }catch(InvalidOperationException) { } return; }
        logLines.Enqueue($"{DateTime.Now:HH:mm:ss} {message}");while(logLines.Count>400)logLines.Dequeue();
        log.Text=string.Join(Environment.NewLine,logLines);log.SelectionStart=log.TextLength;log.ScrollToCaret();
    }
    private async void OnClosing(object? sender,FormClosingEventArgs e)
    {
        if(closing)return;e.Cancel=true;closing=true;displayTimer.Stop();Enabled=false;
        try {await session.DisposeAsync();}catch(Exception ex){MessageBox.Show(this,ex.Message,"Afsluiten G7 Bridge");}
        finally{tray.Visible=false;tray.Dispose();Close();}
    }
    private void RestoreWindow(){Show();WindowState=FormWindowState.Normal;Activate();}
    private Button Button(string text,Func<Task> action)
    {
        var b=new Button{Text=text,AutoSize=true,Height=34,Padding=new(7,3,7,3)};
        b.Click+=async(_,_)=>{if(busy)return;busy=true;b.Enabled=false;try{await action();}catch(Exception ex){AppendLog(ex.Message);MessageBox.Show(this,ex.Message,"G7 Bridge",MessageBoxButtons.OK,MessageBoxIcon.Information);}finally{busy=false;if(!b.IsDisposed)b.Enabled=true;}};
        return b;
    }
    private static TabPage Page(string title,out FlowLayoutPanel panel)
    {
        var p=new TabPage(title){Padding=new(10)};panel=new(){Dock=DockStyle.Fill,FlowDirection=FlowDirection.TopDown,WrapContents=false,AutoScroll=true};p.Controls.Add(panel);return p;
    }
    private static void AddText(Control parent,string text)=>parent.Controls.Add(new Label{Text=text,AutoSize=true,MaximumSize=new(800,0),Margin=new(3,8,3,10)});
    private static FlowLayoutPanel Row(params Control[] controls) {var r=new FlowLayoutPanel{AutoSize=true,WrapContents=false,Margin=new(0,4,0,6)};r.Controls.AddRange(controls);return r;}
    private static ComboBox Combo()=>new(){DropDownStyle=ComboBoxStyle.DropDownList};
    private static NumericUpDown Number(decimal value,decimal min,decimal max,int digits)=>new(){Minimum=min,Maximum=max,DecimalPlaces=digits,Value=value,Width=130};
}
