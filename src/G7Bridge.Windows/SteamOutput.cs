using G7Bridge.Core;
using HIDMaestro;

namespace G7Bridge.Windows;

internal sealed class SteamOutput : IDisposable
{
    private readonly HMContext context;
    private HMController? controller;
    private byte sequence;
    public long HapticPackets=>Interlocked.Read(ref hapticPackets);
    private long hapticPackets;
    private long reports;
    public long Reports=>Interlocked.Read(ref reports);
    public string LastReport {get;private set;}="";
    private readonly object feedbackSync=new();
    private uint rumble;
    private long rumbleAt,nextBattery;
    public uint CurrentRumble {get{lock(feedbackSync)return Environment.TickCount64-rumbleAt<=250?rumble:0;}}

    public SteamOutput()
    {
        // Preflight prevents CreateController from silently installing a system driver.
        if(!HMContext.IsUsbipBackendAvailable)
            throw new InvalidOperationException("The virtual USB driver is missing. Run the G7 Bridge installer.");
        context=new HMContext();
        try {
            context.LoadProfilesFromDirectory(SteamIdentity.PrepareProfile());
            var profile=context.GetProfile("steam-controller-2")??throw new InvalidOperationException("HIDMaestro does not contain the Triton profile.");
            if(!profile.RequiresUsbipBackend || profile.VendorId!=0x28DE || profile.ProductId!=0x1302 || profile.InputReportSize!=54)
                throw new InvalidOperationException("The Steam controller profile does not match the expected USB/Triton identity.");
            controller=context.CreateController(profile,"grimm-g7bridge-triton-v1");
            controller.SubmitRawExtendedReport(TritonProtocol.Encode(default,default,sequence++,0));
            controller.OutputReceived+=OnOutput;
        } catch { context.Dispose();throw; }
    }
    private void OnOutput(HMController _,HMOutputPacket packet)
    {
        if(packet.ReportId>=0x80 && packet.Source==HMOutputSource.HidOutput) {
            Interlocked.Increment(ref hapticPackets);
            if(TritonFeedback.TryRumble(packet.ReportId,packet.Data.Span,out uint motors))
                lock(feedbackSync){rumble=motors;rumbleAt=Environment.TickCount64;}
        }
    }
    public void Submit(InputSnapshot state,uint timestamp,Telemetry? telemetry=null)
    {
        var report=TritonProtocol.Encode(state.Pad,state.Motion,sequence++,timestamp,state.Orientation);
        if(telemetry?.Battery is byte battery && Environment.TickCount64>=nextBattery) {
            nextBattery=Environment.TickCount64+1000;
            report=TritonFeedback.Battery(battery,telemetry.Charging);
        }
        controller?.SubmitRawExtendedReport(report);
        LastReport=Convert.ToHexString(report);Interlocked.Increment(ref reports);
    }
    public void Dispose()
    {
        var pad=controller;controller=null;
        try {
            if(pad is not null) {
                try { pad.SubmitRawExtendedReport(TritonProtocol.Encode(default,default,sequence++,0)); }
                finally { pad.OutputReceived-=OnOutput;pad.Dispose(); }
            }
        } finally { context.Dispose(); }
    }
}
