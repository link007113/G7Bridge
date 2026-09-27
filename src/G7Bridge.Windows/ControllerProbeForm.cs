using System.Diagnostics;
using System.Text;
using G7Bridge.Core;

namespace G7Bridge.Windows;

// Explicit opt-in diagnostic entry point. Normal application startup never uses this.
internal sealed class ControllerProbeForm : Form
{
    private readonly BridgeSession session=new();
    private readonly TextBox display=new(){Multiline=true,ReadOnly=true,Dock=DockStyle.Fill,ScrollBars=ScrollBars.Vertical};
    private readonly StringBuilder report=new();
    private readonly CancellationTokenSource cancel=new();
    private readonly string output;
    private bool finished;
    internal int ExitCode {get;private set;}=1;

    public ControllerProbeForm(string outputPath)
    {
        output=Path.GetFullPath(outputPath);
        Text="G7 Bridge — gerichte controllerdiagnose";Width=900;Height=560;
        Controls.Add(display);
        Activated+=(_,_)=>Write("Diagnosevenster krijgt focus.");
        Deactivate+=(_,_)=>Write("Diagnosevenster verliest focus.");
        session.Log+=message=>{if(!IsDisposed && IsHandleCreated)BeginInvoke(()=>Write(message));};
        Shown+=async(_,_)=>await RunProbe();
        FormClosing+=(_,e)=>{if(!finished){e.Cancel=true;cancel.Cancel();}};
    }

    private void Write(string text)
    {
        var line=$"{DateTimeOffset.Now:O} {text}";report.AppendLine(line);
        display.AppendText(line+Environment.NewLine);
    }
    private async Task WaitUntil(Func<bool> ready,int milliseconds,string error)
    {
        var elapsed=Stopwatch.StartNew();
        while(!ready()) {
            cancel.Token.ThrowIfCancellationRequested();
            if(session.Status.Phase.StartsWith("Extra GameSir-kanaal geweigerd",StringComparison.Ordinal))
                throw new IOException("Het aanvullende kanaal is expliciet geweigerd; verdere wachttijd heeft geen nut.");
            if(elapsed.ElapsedMilliseconds>=milliseconds || !session.IsRunning)throw new IOException(error+" Status: "+session.Status.Phase);
            await Task.Delay(100,cancel.Token);
        }
    }
    private async Task Observe(string phase,int milliseconds)
    {
        long before=session.Status.TelemetryPackets;
        Write($"Meetfase {phase}; vensterfocus={ContainsFocus}; beginteller={before}.");
        var elapsed=Stopwatch.StartNew();bool allFresh=true;
        while(elapsed.ElapsedMilliseconds<milliseconds) {
            await Task.Delay(250,cancel.Token);
            var state=session.Status;
            allFresh&=state.Connected && state.Input.HasTelemetry;
            if(!session.IsRunning)throw new IOException("Uitleessessie viel weg tijdens "+phase+".");
        }
        var after=session.Status;
        Write($"Resultaat {phase}: nieuwe gyropakketten={after.TelemetryPackets-before}; voortdurend vers={allFresh}; verbonden={after.Connected}; knoppen={after.Input.Pad.Buttons}.");
        if(after.TelemetryPackets-before<8 || !allFresh)throw new IOException("Geen ononderbroken telemetrie tijdens "+phase+".");
    }
    private async Task RunProbe()
    {
        try {
            if(File.Exists(output))throw new IOException("Het diagnosebestand bestaat al en wordt niet overschreven.");
            if(Process.GetProcessesByName("HJC.GameSir.Nexus2_0").Length!=0)throw new IOException("Nexus is nog actief. De probe begint niet.");
            Write("G7 Bridge 0.4.0: expliciete invoerdiagnose. Geen Steam-uitvoer, driverinstallatie, profiel- of firmwarewijzigingen.");
            Write($"Procesrechten: beheerder={Program.IsAdmin}; Windows-ontwikkelaarsmodus={Program.DeveloperModeEnabled}. De probe wijzigt zelf geen Windows-instellingen.");
            var devices=await Task.Run(GameInputTransport.Enumerate);
            if(devices.Count!=1)throw new IOException($"Precies één geschikte GameSir vereist; gevonden: {devices.Count}.");
            var device=devices[0];Write($"Geselecteerd: {device.Name}; locatie={device.PhysicalKey}.");
            session.Start(device);
            await WaitUntil(()=>session.Status.Connected,12_000,"Controller openen is niet voltooid.");
            if(device.ProductId==0x100A)session.RequestGyro();
            await WaitUntil(()=>session.Status.Input.HasTelemetry,35_000,"Binnen de begrensde wachttijd kwamen geen echte telemetriepakketten binnen.");
            await Observe("venster geopend",5_000);
            WindowState=FormWindowState.Minimized;
            await Observe("venster geminimaliseerd",15_000);
            Write("GESLAAGD: echte telemetrie bleef ontvangen tijdens beide meetfasen. Fysieke beweging en afzonderlijke knoppen vereisen bediening door Anthony.");
            ExitCode=0;
        } catch(OperationCanceledException){ExitCode=2;Write("AFGEBROKEN door gebruiker.");}
        catch(Exception ex){Write("MISLUKT: "+ex.Message);}
        finally {
            Write("Laatste ontvangen telemetriepayloads:");
            foreach(var frame in session.RecentFrames.TakeLast(8))report.AppendLine(Convert.ToHexString(frame));
            try {await session.DisposeAsync();}catch(Exception ex){Write("Afsluiten: "+ex.Message);}
            // Allow queued session-log messages to reach the UI before writing the report.
            await Task.Yield();
            try {
                Directory.CreateDirectory(Path.GetDirectoryName(output)!);
                using var file=new FileStream(output,FileMode.CreateNew,FileAccess.Write,FileShare.Read);
                using var writer=new StreamWriter(file);writer.Write(report.ToString());
            } catch(Exception ex){ExitCode=3;MessageBox.Show(this,"Diagnose kon niet worden opgeslagen: "+ex.Message,"G7 Bridge");}
            finished=true;cancel.Dispose();Close();
        }
    }
}
