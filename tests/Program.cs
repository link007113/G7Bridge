using G7Bridge.Core;
using System.Buffers.Binary;
using System.Globalization;

UiLanguage.Initialize(CultureInfo.InvariantCulture);
var tests = new (string Name, Action Body)[] {
    ("Only the USB root of a supported GameSir receiver is selectable",()=>{
        True(ReceiverPresence.IsReceiverRoot(@"USB\VID_3537&PID_106B\0000DF3F3CCA6347"));
        True(ReceiverPresence.IsReceiverRoot(@"usb\vid_3537&pid_100a\00CC848C41"));
        True(!ReceiverPresence.IsReceiverRoot(@"USB\VID_3537&PID_100A&MI_00\8&31B07931&0&0000"));
        True(!ReceiverPresence.IsReceiverRoot(@"HID\VID_3537&PID_106B\0000"));
        True(!ReceiverPresence.IsReceiverRoot(@"USB\VID_3537&PID_0575\7&1441131D&0&4"));
        True(!ReceiverPresence.IsReceiverRoot(@"USB\VID_045E&PID_02FF&IG_00\00&00&0000DF3F3CCA6347"));
    }),
    ("A receiver without controller children reports no controller; captured 106B children do",()=>{
        True(!ReceiverPresence.HasController([]));
        True(ReceiverPresence.HasController([@"USB\VID_045E&PID_02FF&IG_00\00&00&0000DF3F3CCA6347",@"HID\VID_045E&PID_02FF&IG_00\9&1C728799&0&0000"]));
    }),
    ("Always-present 100A interfaces do not count as a connected controller",()=>{
        True(!ReceiverPresence.HasController([@"USB\VID_3537&PID_100A&MI_00\8&31B07931&0&0000",@"USB\VID_3537&PID_100A&MI_01\8&31B07931&0&0001",
            @"HID\VID_3537&PID_100A&MI_01&COL01\9&1DB5C6EA&0&0000",@"HID\VID_3537&PID_100A&MI_01&COL04\9&1DB5C6EA&0&0003"]));
        // Recorded by Windows on the development PC in the 100A window of 2026-09-28 18:32:53-18:33:02, controller on.
        True(ReceiverPresence.HasController([@"USB\VID_3537&PID_100A&MI_00\8&31B07931&0&0000",@"USB\VID_3537&PID_100A&IG_02\9&5F23AA5&0&02",@"HID\VID_3537&PID_100A&IG_02\A&2EE197D2&0&0000"]));
    }),
    ("Only the 106B session receiver has hideable paired controller children",()=>{
        True(ReceiverPresence.IsSessionRoot(@"USB\VID_3537&PID_106B\0000DF3F3CCA6347"));
        True(!ReceiverPresence.IsSessionRoot(@"USB\VID_3537&PID_100A\00CC848C41"));
    }),
    ("A present controller that Windows does not report within ten seconds counts as a failed session",()=>{
        var gate=new ConnectionGate();
        Equal(GateAction.Start,gate.Next(true,0));
        Equal(GateAction.Wait,gate.NotReported(9_999,hidingApplies:true));Equal(GateAction.Wait,gate.NotReported(10_000,true));
        Equal(GateAction.Wait,gate.NotReported(19_999,true));Equal(GateAction.Wait,gate.NotReported(20_000,true));True(gate.HoldHiding);
        Equal(GateAction.Wait,gate.NotReported(29_999,true));Equal(GateAction.Release,gate.NotReported(30_000,true));True(!gate.HoldHiding);
    }),
    ("A controller turned on again gets a fresh grace period",()=>{
        var gate=new ConnectionGate();
        Equal(GateAction.Start,gate.Next(true,0));Equal(GateAction.Wait,gate.NotReported(10_000,true));
        Equal(GateAction.Wait,gate.Next(false,11_000));Equal(GateAction.Start,gate.Next(true,50_000));
        Equal(GateAction.Wait,gate.NotReported(59_999,true));
        // Only failures after the re-arm count: the in-grace report above must not be one of them.
        Equal(GateAction.Start,gate.Next(true,60_000));Equal(GateAction.Wait,gate.SessionEnded(false));
        Equal(GateAction.Start,gate.Next(true,61_000));Equal(GateAction.Wait,gate.SessionEnded(false));
        Equal(GateAction.Start,gate.Next(true,62_000));Equal(GateAction.Release,gate.SessionEnded(false));
    }),
    ("Failures on the 100A identity never release hiding because it does not apply there",()=>{
        var gate=new ConnectionGate();
        for(int i=0;i<6;i++){Equal(GateAction.Start,gate.Next(true,i*60_000L));Equal(GateAction.Wait,gate.SessionEnded(false,hidingApplies:false));}
        for(long t=400_000;t<500_000;t+=10_000)Equal(GateAction.Wait,gate.NotReported(t,false));
        True(gate.HoldHiding);True(!gate.Released);Equal(GateAction.Start,gate.Next(true,500_000));
    }),
    ("Waiting for an absent controller keeps hiding and never starts a session",()=>{
        var gate=new ConnectionGate();
        for(int i=0;i<20;i++){Equal(GateAction.Wait,gate.Next(controllerPresent:false));True(gate.HoldHiding);}
        Equal(GateAction.Start,gate.Next(controllerPresent:true));
    }),
    ("Three failed sessions with the controller present release hiding until it disconnects",()=>{
        var gate=new ConnectionGate();
        for(int i=1;i<=2;i++){Equal(GateAction.Start,gate.Next(true));Equal(GateAction.Wait,gate.SessionEnded(receivedTelemetry:false));True(gate.HoldHiding);}
        Equal(GateAction.Start,gate.Next(true));Equal(GateAction.Release,gate.SessionEnded(receivedTelemetry:false));
        True(!gate.HoldHiding);True(gate.Released);
        for(int i=0;i<5;i++)Equal(GateAction.Wait,gate.Next(true));
        Equal(GateAction.Wait,gate.Next(false));True(gate.HoldHiding);True(!gate.Released);
        Equal(GateAction.Start,gate.Next(true));Equal(GateAction.Wait,gate.SessionEnded(false));True(gate.HoldHiding);
    }),
    ("A session that received telemetry resets the failure count",()=>{
        var gate=new ConnectionGate();
        foreach(bool worked in new[]{false,false,true,false,false}){Equal(GateAction.Start,gate.Next(true));Equal(GateAction.Wait,gate.SessionEnded(worked));}
        True(gate.HoldHiding);True(!gate.Released);
    }),
    ("Battery alerts fire once at 20% and 10% while discharging",()=>{
        var alerts=new BatteryAlerts();
        byte?[] expected=new byte?[101];expected[20]=20;expected[10]=10;
        for(int level=100;level>=0;level--)Equal(expected[level],alerts.Observe((byte)level,charging:false));
    }),
    ("A controller first seen below 10% gets one alert, not both",()=>{
        var alerts=new BatteryAlerts();
        Equal((byte?)10,alerts.Observe(8,false));
        for(int level=7;level>=0;level--)Equal((byte?)null,alerts.Observe((byte)level,false));
    }),
    ("Battery jitter does not repeat an alert; charging or +5% re-arms it",()=>{
        var alerts=new BatteryAlerts();
        Equal((byte?)20,alerts.Observe(20,false));
        foreach(byte level in new byte[]{21,20,22,19,24,20})Equal((byte?)null,alerts.Observe(level,false));
        Equal((byte?)null,alerts.Observe(25,false));Equal((byte?)20,alerts.Observe(20,false));
        Equal((byte?)null,alerts.Observe(18,true));Equal((byte?)20,alerts.Observe(18,false));
    }),
    ("Unknown battery readings neither alert nor re-arm",()=>{
        var alerts=new BatteryAlerts();
        Equal((byte?)20,alerts.Observe(20,false));Equal((byte?)null,alerts.Observe(null,false));
        Equal((byte?)null,alerts.Observe(19,false));
    }),
    ("Autostart command quotes the installed path and starts in the tray",()=>{
        Equal("\"C:\\Program Files\\Grimm\\G7Bridge\\G7Bridge.exe\" --tray",AutostartEntry.Command(@"C:\Program Files\Grimm\G7Bridge\G7Bridge.exe"));
    }),
    ("Autostart is enabled only for this executable and when Windows has not disabled it",()=>{
        string exe=@"C:\Program Files\Grimm\G7Bridge\G7Bridge.exe",command=AutostartEntry.Command(exe);
        True(AutostartEntry.IsEnabled(command,exe,null));
        True(AutostartEntry.IsEnabled(command.ToLowerInvariant(),exe,[2,0,0,0,0,0,0,0,0,0,0,0]));
        True(!AutostartEntry.IsEnabled(null,exe,null));
        True(!AutostartEntry.IsEnabled(AutostartEntry.Command(@"D:\Old\G7Bridge.exe"),exe,null));
        True(!AutostartEntry.IsEnabled(command,exe,[3,0,0,0,0,0,0,0,0,0,0,0]));
        True(!AutostartEntry.IsEnabled("\""+exe+"\"",exe,null));
    }),
    ("Tray view reflects service, readiness, attention and battery",()=>{
        Equal(TrayKind.Off,TrayView.From(running:false,current:false,ready:false,attention:"",battery:50,charging:false).Kind);
        Equal((byte?)null,TrayView.From(false,false,false,"",50,false).Battery);
        Equal(TrayKind.Waiting,TrayView.From(true,false,false,"",null,false).Kind);
        Equal(TrayKind.Waiting,TrayView.From(true,true,false,"",null,false).Kind);
        Equal(TrayKind.Attention,TrayView.From(true,true,false,"output",null,false).Kind);
        var active=TrayView.From(true,true,true,"",64,false);
        Equal(TrayKind.Active,active.Kind);Equal((byte?)64,active.Battery);
        Equal("G7 Bridge — active · battery 64%",active.Tooltip);
        Equal("G7 Bridge — active · charging 64%",TrayView.From(true,true,true,"",64,true).Tooltip);
        Equal("G7 Bridge — waiting for the controller",TrayView.From(true,true,false,"",null,false).Tooltip);
        Equal("G7 Bridge — off",TrayView.From(false,false,false,"",null,false).Tooltip);
        Equal("G7 Bridge — attention needed",TrayView.From(true,true,false,"driver",null,false).Tooltip);
        True(TrayView.From(true,true,true,"",100,true).Tooltip.Length<=127);
    }),
    ("Captured partial LT uses its physical position instead of the saturated processed field",()=>{
        var report=Convert.FromHexString("1000003CE0808080800F0400FF000000001500FCFFF3FF5FEF0D0DECE1000000006100007F7F007F7F007F7F007F7F00000000858084850F0400006B00000000");
        Equal((byte)255,report[12]);Equal((byte)107,report[59]);
        True(G7Protocol.TryTelemetry(report,out var telemetry));
        Equal((ushort)(107*257),telemetry!.LowResolutionPad.LT);Equal((ushort)0,telemetry.LowResolutionPad.RT);
    }),
    ("Fresh physical triggers override later clipped Windows samples while preserving high-resolution sticks",()=>{
        var state=new InputState();var report=TelemetryFrame();report[12]=report[13]=255;report[59]=107;report[60]=64;
        state.Accept(report,1000);
        state.Accept(GameInputPackets.Gamepad(0,1,1,0.75f,-0.25f,-0.5f,0.5f),1008);
        var snapshot=state.Snapshot(1010,new());
        Equal((ushort)(107*257),snapshot.Pad.LT);Equal((ushort)(64*257),snapshot.Pad.RT);
        Equal((short)24575,snapshot.Pad.LX);Equal((short)-8192,snapshot.Pad.LY);True(snapshot.HasGamepad);
        var output=TritonProtocol.Encode(snapshot.Pad,snapshot.Motion,0,0);
        var monitor=new VirtualInputMonitor();True(monitor.Accept(output,1010));
        var received=monitor.Snapshot(1011).Pad;True(Math.Abs(VirtualPadState.Trigger(received.LT)-107/255f)<0.0001);
    }),
    ("All 256 physical trigger positions reach virtual output monotonically and independently",()=>{
        int previous=-1;
        for(int position=0;position<=255;position++) {
            var state=new InputState();var raw=TelemetryFrame();raw[12]=raw[13]=255;raw[59]=(byte)position;raw[60]=(byte)(255-position);
            state.Accept(GameInputPackets.Gamepad(0,1,1,0,0,0,0),0);state.Accept(raw,1);
            var input=state.Snapshot(2,new());var report=TritonProtocol.Encode(input.Pad,input.Motion,0,0);
            int left=BinaryPrimitives.ReadUInt16LittleEndian(report.AsSpan(6)),right=BinaryPrimitives.ReadUInt16LittleEndian(report.AsSpan(8));
            Equal(position*32767/255,left);Equal((255-position)*32767/255,right);True(left>previous);previous=left;
        }
    }),
    ("Stale physical trigger telemetry releases triggers rather than restoring clipped Windows values",()=>{
        var state=new InputState();var raw=TelemetryFrame();raw[59]=128;raw[60]=255;
        state.Accept(raw,1000);state.Accept(GameInputPackets.Gamepad(0,1,1,0.5f,0,0,0),1151);
        var lost=state.Snapshot(1151,new());True(lost.HasGamepad);True(!lost.HasTelemetry);
        Equal((ushort)0,lost.Pad.LT);Equal((ushort)0,lost.Pad.RT);Equal((short)16384,lost.Pad.LX);
        raw[59]=32;raw[60]=0;state.Accept(raw,1160);
        Equal((ushort)(32*257),state.Snapshot(1160,new()).Pad.LT);
    }),
    ("Ordinary gamepad triggers still work before any vendor telemetry has arrived",()=>{
        var state=new InputState();state.Accept(GameInputPackets.Gamepad(0,0.5f,0.25f,0,0,0,0),0);
        var pad=state.Snapshot(1,new()).Pad;True(pad.LT>32000 && pad.LT<33000);True(pad.RT>16000 && pad.RT<17000);
    }),
    ("Monitor targets only the bridge serial, never a physical or unrelated Steam Controller",()=>{
        Equal("HM5AB6FC945E5A",VirtualControllerIdentity.UsbSerial);
        True(VirtualControllerIdentity.Matches(0x28DE,0x1302,0xFF00,54,"HM5AB6FC945E5A"));
        True(!VirtualControllerIdentity.Matches(0x28DE,0x1302,0xFF00,54,"FXA996020001"));
        True(!VirtualControllerIdentity.Matches(0x3537,0x106B,0xFF00,54,"HM5AB6FC945E5A"));
        True(!VirtualControllerIdentity.Matches(0x28DE,0x1302,1,54,"HM5AB6FC945E5A"));
        True(!VirtualControllerIdentity.Matches(0x28DE,0x1302,0xFF00,64,"HM5AB6FC945E5A"));
        True(!VirtualControllerIdentity.Matches(0x28DE,0x1302,0xFF00,54,null));
    }),
    ("Readback decodes independent extra buttons and signed axes from a Windows Triton report",()=>{
        var monitor=new VirtualInputMonitor();var report=new byte[54];
        byte[] header=[0x42,0xA7,0x91,0x00,0x06,0x00,0xFF,0x7F,0x00,0x40,0x00,0x80,0xFF,0x7F,0x34,0x12,0xCC,0xED];
        header.CopyTo(report,0);
        True(monitor.Accept(report,1000));var state=monitor.Snapshot(1001);
        True(state.Fresh);Equal(1L,state.Reports);
        Equal(PadButtons.A|PadButtons.Share|PadButtons.R4|PadButtons.L4|PadButtons.L5,state.Pad.Buttons);
        Equal(short.MinValue,state.Pad.LX);Equal(short.MaxValue,state.Pad.LY);
        Equal((short)0x1234,state.Pad.RX);Equal((short)-0x1234,state.Pad.RY);
        Equal((ushort)32767,state.Pad.LT);Equal((ushort)16384,state.Pad.RT);
        Equal(-1f,VirtualPadState.Axis(state.Pad.LX));Equal(1f,VirtualPadState.Trigger(state.Pad.LT));
    }),
    ("Battery and malformed reports cannot overwrite readback buttons; stale input clears",()=>{
        var monitor=new VirtualInputMonitor();var down=new byte[54];down[0]=0x42;down[2]=1;
        True(monitor.Accept(down,1000));True(!monitor.Accept(new byte[53],1010));
        var battery=new byte[54];battery[0]=0x43;battery[2]=99;True(!monitor.Accept(battery,1200));
        Equal(PadButtons.A,monitor.Snapshot(1250).Pad.Buttons);
        var stale=monitor.Snapshot(1600);True(!stale.Fresh);Equal(PadButtons.None,stale.Pad.Buttons);
        monitor.Clear();True(!monitor.Snapshot(1100).Fresh);
    }),
    ("Readback preserves a short press for the last-button label after release",()=>{
        var monitor=new VirtualInputMonitor();var down=new byte[54];down[0]=0x42;down[4]=2;
        True(monitor.Accept(down,1000));down[4]=0;True(monitor.Accept(down,1008));
        var state=monitor.Snapshot(1033);Equal(PadButtons.None,state.Pad.Buttons);Equal(PadButtons.L4,state.LastPressed);
    }),
    ("Gyro readback preserves signed sensor values and clears motion when input goes stale",()=>{
        var monitor=new VirtualInputMonitor();var report=new byte[54];report[0]=0x42;
        byte[] sensors=[0x00,0x40,0x00,0xC0,0x01,0x00,0x00,0x40,0x00,0xE0,0x00,0x80];
        sensors.CopyTo(report,34);True(monitor.Accept(report,100));
        var motion=monitor.Snapshot(101).Motion;
        Equal((short)16384,motion.GX);Equal((short)-8192,motion.GY);Equal(short.MinValue,motion.GZ);
        Equal((short)16384,motion.AX);Equal((short)-16384,motion.AY);Equal((short)1,motion.AZ);
        Equal(default(MotionState),monitor.Snapshot(700).Motion);
    }),
    ("Rumble test writes the known report and pads only to the HID output length",()=>{
        var report=RumbleTest.Report(0x4000,64);Equal(64,report.Length);
        Equal("80000000004000004000",Convert.ToHexString(report.AsSpan(0,10)));
        True(report.AsSpan(10).IndexOfAnyExcept((byte)0)<0);
        True(TritonFeedback.TryRumble(report[0],report.AsSpan(1,9),out var motors));Equal(0x40004000u,motors);
        True(RumbleTest.Report(0,64).AsSpan(1).IndexOfAnyExcept((byte)0)<0);
        bool rejected=false;try{RumbleTest.Report(1,9);}catch(ArgumentOutOfRangeException){rejected=true;}True(rejected);
    }),
    ("Cancelling a rumble test still sends neutral through a separate stop token",()=>{
        using var cancel=new CancellationTokenSource();var sent=new List<ushort>();
        try {
            RumbleTest.RunAsync((value,token)=>{sent.Add(value);if(value!=0)cancel.Cancel();else True(!token.IsCancellationRequested);return ValueTask.CompletedTask;},cancel.Token).GetAwaiter().GetResult();
        }catch(OperationCanceledException){ }
        True(sent.Count==2);Equal(RumbleTest.Strength,sent[0]);Equal((ushort)0,sent[^1]);
    }),
    ("A failed rumble write still attempts neutral; pre-cancelled tests send nothing",()=>{
        var sent=new List<ushort>();bool failed=false;
        try {RumbleTest.RunAsync((value,_)=>{sent.Add(value);if(value!=0)throw new IOException("disconnected");return ValueTask.CompletedTask;},CancellationToken.None).GetAwaiter().GetResult();}
        catch(IOException){failed=true;}
        True(failed);Equal((ushort)0,sent[^1]);
        sent.Clear();using var cancel=new CancellationTokenSource();cancel.Cancel();
        try {RumbleTest.RunAsync((value,_)=>{sent.Add(value);return ValueTask.CompletedTask;},cancel.Token).GetAwaiter().GetResult();}catch(OperationCanceledException){ }
        Equal(0,sent.Count);
    }),
    ("A completed rumble test is bounded and ends with an explicit neutral command",()=>{
        var sent=new List<ushort>();
        RumbleTest.RunAsync((value,_)=>{sent.Add(value);return ValueTask.CompletedTask;},CancellationToken.None).GetAwaiter().GetResult();
        True(sent.Count>=2);True(sent.Take(sent.Count-1).All(value=>value==0x4000));Equal((ushort)0,sent[^1]);
        Equal(400,RumbleTest.DurationMilliseconds);Equal(40,RumbleTest.ResendMilliseconds);
    }),
    ("UI follows the Windows display language, with English as the fallback",()=>{
        foreach(string culture in new[]{"en-US","en-GB","de-DE","fr-FR","ja-JP",""})
            Equal("en",UiLanguage.Select(System.Globalization.CultureInfo.GetCultureInfo(culture)));
        foreach(string culture in new[]{"nl-NL","nl-BE","nl"})
            Equal("nl",UiLanguage.Select(System.Globalization.CultureInfo.GetCultureInfo(culture)));
        Equal("en",UiLanguage.Select(null));
    }),
    ("Service ownership compares the complete fixed executable command",()=>{
        const string exe=@"C:\Program Files\Grimm\G7Bridge\G7Bridge.exe";
        True(InstallationPolicy.OwnsService($"\"{exe}\" --service",exe));
        True(InstallationPolicy.OwnsService($"\"{exe.ToUpperInvariant()}\" --service",exe));
        foreach(var foreign in new[]{"other.exe --service",$"\"{exe}\" --service --extra",$"\"{exe}.other\" --service",exe})
            True(!InstallationPolicy.OwnsService(foreign,exe));
    }),
    ("Regional number formats cannot override the display language",()=>{
        var oldFormat=System.Globalization.CultureInfo.CurrentCulture;
        var oldUi=System.Globalization.CultureInfo.CurrentUICulture;
        try {
            System.Globalization.CultureInfo.CurrentCulture=System.Globalization.CultureInfo.GetCultureInfo("nl-NL");
            System.Globalization.CultureInfo.CurrentUICulture=System.Globalization.CultureInfo.GetCultureInfo("en-US");
            Equal("English",UiLanguage.Text("English","Nederlands"));
            System.Globalization.CultureInfo.CurrentCulture=System.Globalization.CultureInfo.GetCultureInfo("en-US");
            System.Globalization.CultureInfo.CurrentUICulture=System.Globalization.CultureInfo.GetCultureInfo("nl-BE");
            Equal("Nederlands",UiLanguage.Text("English","Nederlands"));
            System.Globalization.CultureInfo.CurrentUICulture=System.Globalization.CultureInfo.GetCultureInfo("de-DE");
            Equal("English",UiLanguage.Text("English","Nederlands"));
        } finally {
            System.Globalization.CultureInfo.CurrentCulture=oldFormat;
            System.Globalization.CultureInfo.CurrentUICulture=oldUi;
        }
    }),
    ("Raw bridge streaming disables the SDK generator that would inject empty input frames",()=>{
        const string json="{\"extendedReport\":{\"id\":66,\"size\":54,\"idleFrameIntervalMs\":4,\"alwaysArmed\":true}}";
        using var doc=System.Text.Json.JsonDocument.Parse(TritonIdentity.UseBridgeOwnedStream(json));
        var report=doc.RootElement.GetProperty("extendedReport");Equal(0,report.GetProperty("idleFrameIntervalMs").GetInt32());
        Equal(54,report.GetProperty("size").GetInt32());True(report.GetProperty("alwaysArmed").GetBoolean());
    }),
    ("Xbox Menu and View keep Start and Back semantics in the stock SDL Triton decoder",()=>{
        True(G7Protocol.TryGamepad(GameInputPackets.Gamepad(1,0,0,0,0,0,0),out var menu));
        True(G7Protocol.TryGamepad(GameInputPackets.Gamepad(2,0,0,0,0,0,0),out var view));
        Equal(0x40u,BinaryPrimitives.ReadUInt32LittleEndian(TritonProtocol.Encode(menu,default,0,0).AsSpan(2)));
        Equal(0x4000u,BinaryPrimitives.ReadUInt32LittleEndian(TritonProtocol.Encode(view,default,0,0).AsSpan(2)));
    }),
    ("Virtual Steam identity updates only the compatibility timestamp and preserves other attributes",()=>{
        const string json="{\"featureStubs\":{\"reports\":[{\"id\":\"0x83\",\"data\":\"018319010213000002000000000a2ef9d2680457d0186a0948000000\"}]}}";
        string updated=TritonIdentity.WithCompatibilityVersion(json,0x6A628345);
        using var document=System.Text.Json.JsonDocument.Parse(updated);
        string data=document.RootElement.GetProperty("featureStubs").GetProperty("reports")[0].GetProperty("data").GetString()!;
        Equal("018319010213000002000000000A2EF9D268044583626A0948000000",data);
        Equal(updated,TritonIdentity.WithCompatibilityVersion(updated,1));
    }),
    ("Triton rumble keeps separate motor strengths and rejects other output formats",()=>{
        byte[] payload=[0,0,0,0x34,0x12,0,0xCD,0xAB,0];
        True(TritonFeedback.TryRumble(0x80,payload,out var motors));Equal(0xABCD1234u,motors);
        True(!TritonFeedback.TryRumble(0x81,payload,out _));True(!TritonFeedback.TryRumble(0x80,payload.AsSpan(0,8),out _));
        payload[0]=1;True(!TritonFeedback.TryRumble(0x80,payload,out _));
    }),
    ("Battery reports expose measured percentage and charging without fabricated voltages",()=>{
        var report=TritonFeedback.Battery(39,false);Equal(15,report.Length);Equal((byte)0x43,report[0]);Equal((byte)1,report[1]);Equal((byte)39,report[2]);
        True(report.Skip(3).All(b=>b==0));Equal((byte)2,TritonFeedback.Battery(40,true)[1]);Equal((byte)4,TritonFeedback.Battery(100,true)[1]);
    }),
    ("Only the selected GameSir GIP model can receive a hide rule",()=>{
        True(PhysicalHidePolicy.IsSelectedModel(@"USB\VID_3537&PID_106B&MI_00\example"));
        True(PhysicalHidePolicy.IsSelectedModel(@"hid\vid_3537&pid_106b&ig_00\example"));
        foreach(var id in new[]{@"USB\VID_054C&PID_0CE6\example",@"USB\VID_28DE&PID_1302\example",@"USB\VID_3537&PID_100A\example",@"USB\VID_3537&PID_106B9\example","USB\\VID_3537&PID_106B\\bad\n"})True(!PhysicalHidePolicy.IsSelectedModel(id));
    }),
    ("Activating HidHide cannot activate disabled rules for unrelated devices",()=>{
        const string selected=@"USB\VID_3537&PID_106B\example";
        True(PhysicalHidePolicy.CanActivate(false,false,[],selected));
        True(!PhysicalHidePolicy.CanActivate(false,false,[@"HID\VID_054C&PID_0CE6\other"],selected));
        True(PhysicalHidePolicy.CanActivate(true,false,[@"HID\VID_054C&PID_0CE6\other"],selected));
        True(!PhysicalHidePolicy.CanActivate(true,true,[],selected));
    }),
    ("Missing GIP channel and provider interface do not advise reinstalling working GameInput", () => {
        foreach(uint code in new uint[]{0x80070490,0x80004002,0x80070034})
            True(!GameInputErrors.RecoveryHint(unchecked((int)code)).Contains("install",StringComparison.OrdinalIgnoreCase));
    }),
    ("Missing selected GameInput device does not recommend reinstalling the runtime", () => {
        var hint=GameInputErrors.RecoveryHint(unchecked((int)0x838A0002));
        True(hint.Contains("Turn it on",StringComparison.Ordinal));
        True(!hint.Contains("install",StringComparison.OrdinalIgnoreCase));
    }),
    ("Actual missing or incompatible GameInput runtime gets an installation hint", () => {
        foreach(uint code in new uint[]{0x80004002,0x8007007E,0x8007007F})
            True(GameInputErrors.RecoveryHint(unchecked((int)code),runtimeOperation:true).Contains("Reinstall G7 Bridge",StringComparison.Ordinal));
    }),
    ("Native and Win32 disconnect errors are recognized without treating a lookup miss as unplugging", () => {
        True(GameInputErrors.IsDisconnected(unchecked((int)0x838A0001)));
        True(GameInputErrors.IsDisconnected(unchecked((int)0x8007048F)));
        True(!GameInputErrors.IsDisconnected(unchecked((int)0x838A0002)));
    }),
    ("Native GIP payload normalization preserves captured telemetry and rejects wrong framing", () => {
        var source=Convert.FromHexString(G7Bridge.Tests.NexusFixtures.Buttons[1].Hex);
        var result=GameInputPackets.Normalize(0x10,source.AsSpan(4));
        True(result is not null);Equal(Convert.ToHexString(source.AsSpan(4)),Convert.ToHexString(result!.AsSpan(4)));
        True(G7Protocol.TryTelemetry(result,out var t));Equal(PadButtons.L4,t!.LowResolutionPad.Buttons);
        True(GameInputPackets.Normalize(0x10,source.AsSpan(4,59)) is null);
        True(GameInputPackets.Normalize(0x11,source.AsSpan(4)) is null);
    }),
    ("Native gamepad state retains axis extremes and button identities", () => {
        var report=GameInputPackets.Gamepad(0x2045,1,0,-1,1,0.5f,-0.5f);
        True(G7Protocol.TryGamepad(report,out var p));Equal(short.MinValue,p.LX);Equal(short.MaxValue,p.LY);
        Equal(ushort.MaxValue,p.LT);Equal(PadButtons.Menu|PadButtons.A|PadButtons.Up|PadButtons.R3,p.Buttons);
    }),
    ("Session intro matches the captured five-byte command",()=>Equal("0F000101F2",Convert.ToHexString(G7Protocol.BeginSession(1)))),
    ("Nexus capture exposes four independent extras and Share including combinations", () => {
        foreach(var sample in G7Bridge.Tests.NexusFixtures.Buttons) {
            True(G7Protocol.TryTelemetry(Convert.FromHexString(sample.Hex),out var t));
            Equal(sample.Buttons,t!.LowResolutionPad.Buttons);
        }
    }),
    ("Physical telemetry buttons replace post-remap Xbox buttons without losing fine axes", () => {
        var state=new InputState();
        state.Accept(Convert.FromHexString("20000220100000000000c5c18c1d00000000000000000000000000000000000000000000"),1000);
        state.Accept(Convert.FromHexString(G7Bridge.Tests.NexusFixtures.Buttons[1].Hex),1000);
        var p=state.Snapshot(1010,new()).Pad;
        Equal(PadButtons.L4,p.Buttons);Equal((short)-15931,p.LX);True(p.HighResolution);
    }),
    ("Nexus 36-byte GIP input retains full axes and rejects a truncated declared payload", () => {
        var p=Convert.FromHexString("20000220000000000000c5c18c1d00000000000000000000000000000000000000000000");
        True(G7Protocol.TryGamepad(p,out var pad));Equal((short)-15931,pad.LX);Equal((short)7564,pad.LY);
        True(!G7Protocol.TryGamepad(p[..18],out _));p[1]=0x80;True(!G7Protocol.TryGamepad(p,out _));
    }),
    ("Analog trigger fields cannot become learned digital buttons", () => {
        var a=Enumerable.Range(0,8).Select(_=>TelemetryFrame()).ToArray();
        var b=Enumerable.Range(0,8).Select(_=>{var p=TelemetryFrame();p[59]=255;p[60]=255;return p;}).ToArray();
        Equal(0,ButtonLearning.Candidates(a,b).Count);
        var s=new BridgeSettings();s.Bindings[PadButtons.L4]=new(60,8,true);Throws(()=>ButtonLearning.Validate(s));
    }),
    ("Observed 100A to 106B transition is accepted only on the original USB port", () => {
        var target=new UsbDeviceIdentity("7:4",0x3537,0x106B);
        Equal<UsbDeviceIdentity?>(target,G7SessionSetup.FindTransitionTarget("7:4",[target,new("7:5",0x3537,0x109C)]));
        True(G7SessionSetup.FindTransitionTarget("7:5",[target]) is null);
    }),
    ("Observed 106B identity can be enumerated and starts session heartbeats", () => {
        True(G7DeviceCatalog.IsSupported(0x3537,0x106B));True(G7DeviceCatalog.IsSessionIdentity(0x3537,0x106B));
        True(!G7DeviceCatalog.IsSessionIdentity(0x3537,0x100A));
        True(!G7DeviceCatalog.IsSupported(0x1234,0x106B));True(!G7DeviceCatalog.IsSupported(0x3537,0x1004));
    }),
    ("Observed 106B controller interface is accepted while its audio interface is rejected", () => {
        True(UsbInterfacePolicy.SupportsG7(new(0,0,0xFF,0x47,0xD0,[new(0x02,3,64,4),new(0x82,3,64,2)])));
        True(!UsbInterfacePolicy.SupportsG7(new(1,1,0xFF,0x47,0xD0,[new(0x03,1,228,1),new(0x83,1,228,1)])));
    }),
    ("Ordinary controller data without gyro keeps the session open after ten seconds", () => {
        var monitor=new GyroWaitMonitor();
        Equal<MissingTelemetryNotice?>(MissingTelemetryNotice.OrdinaryInputOnly,monitor.Observe(10_001,463,0));
        True(monitor.Observe(30_000,800,0) is null);
    }),
    ("No data produces one warning rather than repeated USB disconnects", () => {
        var monitor=new GyroWaitMonitor();True(monitor.Observe(9999,0,0) is null);
        Equal<MissingTelemetryNotice?>(MissingTelemetryNotice.NoInput,monitor.Observe(10_001,0,0));
        True(monitor.Observe(60_000,0,0) is null);
    }),
    ("A received gyro stream does not trigger the missing-gyro warning", () => {
        True(new GyroWaitMonitor().Observe(60_000,100,20) is null);
    }),
    ("Documented GameSir initialization uses five chunks and four flush packets", () => {
        var p=G7SessionSetup.HandshakePackets();Equal(9,p.Length);
        string[] expected=["0008006761000000","0008000000000000","0008006D65000000","0008000000000000",
            "0008007369000000","0008000000000000","0008007261000000","0008000000000000","0008007070000000"];
        for(int i=0;i<p.Length;i++)Equal(expected[i],Convert.ToHexString(p[i]));
    }),
    ("Reenumeration selection stays on the same physical port and expected G7 identities", () => {
        UsbDeviceIdentity[] devices=[new("7:5",0x3537,0x109C),new("7:4",0x3537,0x100A),new("7:4",0x1234,0x109C)];
        True(G7SessionSetup.FindTransitionTarget("7:4",devices) is null);
        var candidate=new UsbDeviceIdentity("7:4",0x3537,0x109C);
        Equal<UsbDeviceIdentity?>(candidate,G7SessionSetup.FindTransitionTarget("7:4",[..devices,candidate]));
        True(G7SessionSetup.FindTransitionTarget("7:4",[new("7:4",0x3537,0x1004)]) is null);
        Throws(()=>G7SessionSetup.FindTransitionTarget("7:address-4",devices));
        True(G7SessionSetup.FindTransitionTarget("7:4",[candidate,new("7:4",0x3537,0x109B)]) is null);
    }),
    ("Actual captured controller frames decode sticks, triggers and Y without inventing gyro", () => {
        var stick=Convert.FromHexString("0014000000001D93B3BC00000000000000000000");
        True(G7Protocol.TryGamepad(stick,out var p));Equal((short)-27875,p.LX);Equal((short)-17229,p.LY);
        var buttons=Convert.FromHexString("00140080FF000000000000000000000000000000");
        True(G7Protocol.TryGamepad(buttons,out p));Equal(PadButtons.Y,p.Buttons);Equal(ushort.MaxValue,p.LT);
        True(!G7Protocol.TryTelemetry(stick,out _));
    }),
    ("Observed G7 100A USB description accepts 32-byte interrupt packets", () => {
        var d=new UsbInterfaceInfo(0,0,0xFF,0x5D,1,[new(0x82,3,32,1),new(0x02,3,32,8)]);
        True(UsbInterfacePolicy.SupportsG7(d));
    }),
    ("G7 validation retains the 64-byte baseline", () => {
        True(UsbInterfacePolicy.SupportsG7(new(0,0,0xFF,0x5D,1,[new(0x82,3,64,1),new(0x02,3,64,8)])));
    }),
    ("G7 validation rejects HID, wrong endpoints and non-interrupt pipes", () => {
        True(!UsbInterfacePolicy.SupportsG7(new(1,0,3,0,0,[new(0x84,3,64,1),new(0x04,3,64,5)])));
        True(!UsbInterfacePolicy.SupportsG7(new(0,0,0xFF,0x5D,1,[new(0x81,3,32,1),new(0x01,3,32,8)])));
        True(!UsbInterfacePolicy.SupportsG7(new(0,0,0xFF,0x5D,1,[new(0x82,2,64,1),new(0x02,2,64,8)])));
        True(!UsbInterfacePolicy.SupportsG7(new(0,1,0xFF,0x5D,1,[new(0x82,3,64,1),new(0x02,3,64,8)])));
    }),
    ("Signal timeout releases buttons, sticks and gyro", () => {
        var state=new InputState();var p=TelemetryFrame();p[57]=8;p[5]=255;p[17]=32;
        var settings=new BridgeSettings();settings.Bindings[PadButtons.L4]=new(57,8,true);
        state.Accept(p,1000);var live=state.Snapshot(1050,settings);
        True(live.HasTelemetry);Equal(PadButtons.L4,live.Pad.Buttons);
        var stale=state.Snapshot(1500,settings);True(!stale.HasTelemetry);
        Equal(default(PadState),stale.Pad);Equal(default(MotionState),stale.Motion);
    }),
    ("Disconnect reset cannot replay old controls after reconnect", () => {
        var state=new InputState();state.Accept(TelemetryFrame(),1000);state.Reset();
        True(!state.Snapshot(1001,new()).HasTelemetry);
    }),
    ("Gyro orientation integrates time and rejects a stale interval", () => {
        var o=new GyroOrientation();var m=new MotionState(0,0,1475,0,0,16384);
        o.Update(m,0);for(int i=1;i<=100;i++)o.Update(m,i*10);
        True(Math.Abs(o.Value.Z-0.7071)<0.005);True(Math.Abs(o.Value.W-0.7071)<0.005);
        var q=o.Value;o.Update(m,3000);Equal(q,o.Value);
    }),
    ("Heartbeat contains only the documented session command", () => {
        var p = G7Protocol.Heartbeat(255);
        Equal("0F00FF02F200", Convert.ToHexString(p));
    }),
    ("Reject truncated and unrelated telemetry", () => {
        True(!G7Protocol.TryTelemetry(new byte[28], out _));
        var p = TelemetryFrame(); p[4] = 5;
        True(!G7Protocol.TryTelemetry(p, out _));
    }),
    ("Decode signed motion without inventing battery or stick precision", () => {
        var p = TelemetryFrame(); p[17]=0x00; p[18]=0x80;
        p[19]=0xFF; p[20]=0x7F; p[23]=0x00; p[24]=0x20;
        p[33]=255; p[5]=0; p[6]=255;
        True(G7Protocol.TryTelemetry(p, out var t));
        Equal((short)-32768,t!.GX); Equal((short)32767,t.GY); Equal((short)8192,t.AX);
        True(t.Battery is null); True(!t.LowResolutionPad.HighResolution);
        Equal((short)-32768,t.LowResolutionPad.LX); Equal((short)-32767,t.LowResolutionPad.LY);
    }),
    ("Xbox 360 raw report keeps full stick values and button identity", () => {
        byte[] p = new byte[20]; p[1]=20; p[2]=0x81; p[3]=0x11;
        p[4]=255; p[6]=0x39; p[7]=0x30; p[8]=0; p[9]=0x80;
        True(G7Protocol.TryGamepad(p,out var v));
        Equal((short)12345,v.LX); Equal(short.MinValue,v.LY); Equal(ushort.MaxValue,v.LT);
        Equal(PadButtons.Up|PadButtons.R3|PadButtons.LB|PadButtons.A,v.Buttons);
        True(v.HighResolution);
        p[1]=19; True(!G7Protocol.TryGamepad(p,out _));
    }),
    ("Do not mistake telemetry or partial Xbox packets for ordinary input", () => {
        True(!G7Protocol.TryGamepad(TelemetryFrame(),out _));
        True(!G7Protocol.TryGamepad(new byte[]{0x20,0,1,14,0x10},out _));
    }),
    ("Triton frame preserves extremes, four grips and motion offsets", () => {
        var p = TritonProtocol.Encode(new(short.MinValue,12345,short.MaxValue,-7,65535,0,
            PadButtons.L4|PadButtons.R4|PadButtons.L5|PadButtons.R5,true),new(-100,200,-300,0,0,8192),7,0x12345678);
        Equal(54,p.Length); Equal((byte)0x42,p[0]); Equal((byte)7,p[1]);
        Equal(0x00060180u,BinaryPrimitives.ReadUInt32LittleEndian(p.AsSpan(2)));
        Equal((short)32767,BinaryPrimitives.ReadInt16LittleEndian(p.AsSpan(6)));
        Equal(short.MinValue,BinaryPrimitives.ReadInt16LittleEndian(p.AsSpan(10)));
        Equal((short)12345,BinaryPrimitives.ReadInt16LittleEndian(p.AsSpan(12)));
        Equal(0x12345678u,BinaryPrimitives.ReadUInt32LittleEndian(p.AsSpan(30)));
        Equal((short)-100,BinaryPrimitives.ReadInt16LittleEndian(p.AsSpan(40)));
        Equal((short)8192,BinaryPrimitives.ReadInt16LittleEndian(p.AsSpan(38)));
    }),
    ("Learning ignores moving gyro bytes and requires stable button changes", () => {
        var a = Enumerable.Range(0,10).Select(_=>TelemetryFrame()).ToArray();
        var b = Enumerable.Range(0,10).Select(i=>{var p=TelemetryFrame();p[57]=0x08;p[17]=(byte)i;return p;}).ToArray();
        var c = ButtonLearning.Candidates(a,b);
        Equal(1,c.Count); Equal(new BitBinding(57,8,true),c[0]);
        b[3][57]=0; Equal(0,ButtonLearning.Candidates(a,b).Count);
    }),
    ("Ambiguous learning stays ambiguous and short captures cannot map", () => {
        var a=Enumerable.Range(0,8).Select(_=>TelemetryFrame()).ToArray();
        var b=Enumerable.Range(0,8).Select(_=>{var p=TelemetryFrame();p[57]=0x18;return p;}).ToArray();
        Equal(2,ButtonLearning.Candidates(a,b).Count);
        Equal(0,ButtonLearning.Candidates(a[..2],b[..2]).Count);
    }),
    ("Bindings reject duplicate bits, sensor offsets and zero sensitivity", () => {
        var s=new BridgeSettings(); s.Bindings[PadButtons.L4]=new(57,8,true);
        ButtonLearning.Validate(s);
        s.Bindings[PadButtons.R4]=new(57,8,false); Throws(()=>ButtonLearning.Validate(s));
        s.Bindings.Clear(); s.Bindings[PadButtons.L4]=new(17,1,true); Throws(()=>ButtonLearning.Validate(s));
        s.Bindings.Clear(); s.GyroCountsPerDps=0; Throws(()=>ButtonLearning.Validate(s));
    }),
    ("Mapped buttons are released by a neutral raw report", () => {
        var p=TelemetryFrame(); p[57]=8;
        var bindings=new Dictionary<PadButtons,BitBinding>{{PadButtons.L4,new(57,8,true)}};
        Equal(PadButtons.L4,ButtonLearning.Apply(p,bindings)); p[57]=0;
        Equal(PadButtons.None,ButtonLearning.Apply(p,bindings));
    }),
    ("Gyro conversion removes bias, uses physical scales and clamps", () => {
        var s=new BridgeSettings { GyroCountsPerDps=16, GyroBias=[16,0,0] };
        var t=new Telemetry(32,0,0,0,0,8192,50,false,default,TelemetryFrame());
        var m=MotionConversion.Convert(t,s);
        Equal((short)16,m.GX); Equal((short)16384,m.AZ);
        s.GyroCountsPerDps=0.001;
        Equal(short.MaxValue,MotionConversion.Convert(t,s).GX);
    })
};
int failed=0;
foreach(var test in tests) {
    try { test.Body(); Console.WriteLine("PASS " + test.Name); }
    catch(Exception ex) { failed++; Console.WriteLine("FAIL " + test.Name + ": " + ex.Message); }
}
Console.WriteLine($"{tests.Length-failed}/{tests.Length} offline unit tests passed (no device access).");
return failed == 0 ? 0 : 1;

static byte[] TelemetryFrame() { var p=new byte[64]; p[0]=0x10;p[3]=60;p[4]=0xE0;p[55]=15; p[5]=p[6]=p[7]=p[8]=128; return p; }
static void True(bool value) { if(!value) throw new Exception("Expected true"); }
static void Equal<T>(T expected,T actual) { if(!EqualityComparer<T>.Default.Equals(expected,actual)) throw new Exception($"Expected {expected}, got {actual}"); }
static void Throws(Action action) { try { action(); } catch(ArgumentException) { return; } throw new Exception("Expected ArgumentException"); }
