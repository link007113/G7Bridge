using System.Diagnostics;
using G7Bridge.Core;

namespace G7Bridge.Windows;

internal sealed record SessionStatus(long Packets,long TelemetryPackets,Telemetry? Telemetry,InputSnapshot Input,bool Connected,bool SteamActive,long Haptics,string Phase,bool GyroRequested);

internal sealed class BridgeSession : IAsyncDisposable
{
    private readonly object sync=new();
    private readonly InputState input=new();
    private readonly Stopwatch clock=Stopwatch.StartNew();
    private readonly Queue<byte[]> frames=new();
    private readonly Queue<byte[]> rawFrames=new();
    private CancellationTokenSource? cancellation;
    private Task? running;
    private SteamOutput? steam;
    private BridgeSettings settings=new();
    private Telemetry? latest;
    private long packets,telemetryPackets;
    private long rumbleUpdates;
    private bool connected,exclusive;
    private string transportKind="";
    private bool gyroRequested,gyroInitialized;
    private string phase="No active input session";
    private bool startingSteam;
    private string nativeInstanceId="";
    private Task steamCreation=Task.CompletedTask;
    public event Action<string>? Log;

    public BridgeSettings Settings { get { lock(sync) return SettingsStore.Clone(settings); } set { ButtonLearning.Validate(value);lock(sync) settings=SettingsStore.Clone(value); } }
    public SessionStatus Status { get { lock(sync) return new(packets,telemetryPackets,latest,input.Snapshot(clock.ElapsedMilliseconds,settings),connected,steam is not null,steam?.HapticPackets??0,phase,gyroRequested); } }
    public byte[][] RecentFrames { get { lock(sync) return frames.Select(p=>p.ToArray()).ToArray(); } }
    public byte[][] RecentRawFrames { get { lock(sync) return rawFrames.Select(p=>p.ToArray()).ToArray(); } }
    public bool IsRunning=>running is { IsCompleted:false };
    public string NativeInstanceId {get{lock(sync)return nativeInstanceId;}}
    public long OutputReports {get{lock(sync)return steam?.Reports??0;}}
    public long RumbleUpdates=>Interlocked.Read(ref rumbleUpdates);
    public string LastOutputReport {get{lock(sync)return steam?.LastReport??"";}}

    public void Start(ControllerDevice device)
    {
        if(IsRunning) throw new InvalidOperationException("An input session is already running.");
        cancellation?.Dispose();cancellation=new();
        lock(sync) {
            input.Reset();frames.Clear();rawFrames.Clear();latest=null;packets=telemetryPackets=0;
            gyroRequested=gyroInitialized=false;connected=false;transportKind=device.Transport;
            nativeInstanceId="";
            phase=device.Transport=="GameInput"?"Opening the Windows controller channel":"Opening receiver; USB takeover may briefly reconnect it";
        }
        Log?.Invoke($"Selected channel: {device.Name}.");
        running=Task.Run(()=>Run(device,cancellation.Token));
    }

    private async Task Run(ControllerDevice device,CancellationToken stop)
    {
        using var resolution=TimerResolution.Request(1);
        IControllerTransport? transport=null;
        try {
            transport=Open(device);
            bool heartbeatEnabled=device.Transport=="HID" || G7DeviceCatalog.IsSessionIdentity(G7DeviceCatalog.Vendor,device.ProductId);
            lock(sync) { connected=true;exclusive=transport.Exclusive;nativeInstanceId=(transport as GameInputTransport)?.InstanceId??"";gyroInitialized=heartbeatEnabled;phase="Receiver connected — reading input"; }
            Log?.Invoke(heartbeatEnabled ? "Connected. Documented session heartbeats active; controller profiles are unchanged." :
                "Connected. Reading ordinary input. Gyro activation is requested by the controller service.");
            var data=new byte[64];long started=clock.ElapsedMilliseconds,nextHeartbeat=0,packetsAtStart=0,gyroAtStart=0;
            byte seq=0;
            bool beginPending=heartbeatEnabled;
            var monitor=new GyroWaitMonitor();
            uint lastRumble=0;long nextRumble=0;bool rumbleAvailable=true;
            while(!stop.IsCancellationRequested) {
                bool initialize;lock(sync) initialize=gyroRequested && !gyroInitialized;
                if(initialize && clock.ElapsedMilliseconds-started>=G7SessionSetup.MinimumStableMilliseconds) {
                    lock(sync) { gyroInitialized=true;connected=false;input.Reset();phase="Starting GameSir session: brief reconnection expected"; }
                    (transport,device)=await InitializeGyro(device,transport,stop);
                    started=clock.ElapsedMilliseconds;nextHeartbeat=0;seq=0;monitor=new();heartbeatEnabled=true;beginPending=true;
                    lock(sync) {
                        input.Reset();latest=null;frames.Clear();packetsAtStart=packets;gyroAtStart=telemetryPackets;
                        connected=true;exclusive=transport.Exclusive;phase="GameSir session open — waiting for motion data";
                        nativeInstanceId=(transport as GameInputTransport)?.InstanceId??"";
                    }
                }
                stop.ThrowIfCancellationRequested();
                long now=clock.ElapsedMilliseconds;
                if(heartbeatEnabled && now>=nextHeartbeat) {
                    try {
                        if(beginPending) {transport.Write(G7Protocol.BeginSession(++seq));beginPending=false;}
                        transport.Write(G7Protocol.Heartbeat(++seq));
                    } catch(IOException ex) when(device.Transport=="GameInput" && !GameInputTransport.IsDisconnected(ex)) {
                        heartbeatEnabled=false;
                        lock(sync)phase="Additional GameSir channel denied — ordinary input remains active";
                        Log?.Invoke("Windows rejected the GameSir session message: "+ex.Message+" Input remains read-only; no USB takeover or automatic retry.");
                    }
                    nextHeartbeat=clock.ElapsedMilliseconds+250;
                }
                int count=transport.Read(data,stop);
                now=clock.ElapsedMilliseconds;
                uint desiredRumble;
                lock(sync) {
                    if(count>0) {
                        RecordPacket(data.AsSpan(0,count),now);
                        if(input.Snapshot(now,settings).HasTelemetry) phase="Receiver connected — receiving motion data";
                    }
                    // A missing optional gyro stream never closes an otherwise live USB session.
                    var notice=monitor.Observe(now-started,packets-packetsAtStart,telemetryPackets-gyroAtStart);
                    if(notice is not null) Log?.Invoke(notice==MissingTelemetryNotice.NoInput ?
                        "No input received yet. The input session remains open until stopped." :
                        gyroInitialized && !heartbeatEnabled ? "Ordinary input remains active; the additional GameSir channel was denied. Save diagnostics. No further mode switch is requested in this session.":
                        "Ordinary input received, no gyro yet. The service handles gyro activation.");
                    var current=input.Snapshot(now,settings);
                    steam?.Submit(current,unchecked((uint)(clock.ElapsedTicks*1_000_000L/Stopwatch.Frequency)),latest);
                    desiredRumble=current.HasTelemetry?(steam?.CurrentRumble??0):0;
                }
                if(rumbleAvailable && transport is GameInputTransport motor && (desiredRumble!=lastRumble || (desiredRumble!=0 && now>=nextRumble))) {
                    try{motor.SetRumble(desiredRumble);Interlocked.Increment(ref rumbleUpdates);lastRumble=desiredRumble;nextRumble=now+40;}
                    catch(IOException ex){rumbleAvailable=false;Log?.Invoke("Rumble unavailable: "+ex.Message);}
                }
            }
        } catch(OperationCanceledException) when(stop.IsCancellationRequested) { }
        catch(Exception ex) { Log?.Invoke("Input stopped: "+ex.Message); }
        finally {
            SteamOutput? old;
            lock(sync) {
                connected=false;input.Reset();old=steam;steam=null;phase="No active input session";
            }
            try { old?.Dispose(); } catch(Exception ex) { Log?.Invoke("Closing virtual controller: "+ex.Message); }
            if(transport is GameInputTransport motor){try{motor.SetRumble(0);}catch(IOException){ }}
            try { transport?.Dispose(); } catch(Exception ex) { Log?.Invoke("Releasing USB receiver: "+ex.Message); }
            Log?.Invoke(device.Transport=="GameInput"?"Windows input session closed; Xbox driver remains attached.":
                "Session closed; physical USB receiver released. Windows may briefly reconnect it.");
        }
    }

    private void RecordPacket(ReadOnlySpan<byte> packet,long now)
    {
        packets++;rawFrames.Enqueue(packet.ToArray());while(rawFrames.Count>64)rawFrames.Dequeue();
        input.Accept(packet,now);
        if(G7Protocol.TryTelemetry(packet,out var t)) {
            latest=t;telemetryPackets++;frames.Enqueue(t!.Report);while(frames.Count>64)frames.Dequeue();
        }
        if(packets<=5)Log?.Invoke($"Received: {packet.Length} bytes, report {packet[0]:X2}.");
    }

    public void RequestGyro()
    {
        lock(sync) {
            if(!connected)throw new InvalidOperationException("Wait until the receiver is connected before requesting gyro activation.");
            if(!exclusive && transportKind!="GameInput")throw new InvalidOperationException("GameSir session start requires Windows GameInput or USB / UsbDk.");
            if(gyroRequested || gyroInitialized || input.Snapshot(clock.ElapsedMilliseconds,settings).HasTelemetry) {
                Log?.Invoke("GameSir session start was already requested or gyro is active. No second handshake will be sent.");return;
            }
            gyroRequested=true;phase="Gyro activation requested — waiting for USB to settle";
        }
        Log?.Invoke("GameSir session start requested. A single mode switch may briefly reconnect the receiver.");
    }

    private async Task<(IControllerTransport Transport,ControllerDevice Device)> InitializeGyro(ControllerDevice original,IControllerTransport current,CancellationToken stop)
    {
        if(current is GameInputTransport native)return await InitializeNativeGyro(original,native,stop);
        // Validate stable identity before transmitting any mode-switch bytes.
        _=G7SessionSetup.FindTransitionTarget(original.Key,[]);
        if(original.Transport!="UsbDk")throw new InvalidOperationException("This gyro startup route requires UsbDk.");
        if(original.ProductId!=0x100A)return(current,original);
        Log?.Invoke("Documented gamesirapp startup: five parts, with empty flush packets between them.");
        var handshake=G7SessionSetup.HandshakePackets();
        bool sequenceCompleted=false;
        try {
            for(int i=0;i<handshake.Length;i++) {
                stop.ThrowIfCancellationRequested();
                try {current.Write(handshake[i]);}
                catch(UsbTransferException ex) when(i==handshake.Length-1 && ex.ErrorCode is -4 or -1) {
                    Log?.Invoke("Receiver disappeared at the last startup packet; waiting for its new USB identity.");break;
                }
                await Task.Delay(G7SessionSetup.HandshakeSpacingMilliseconds,stop);
            }
            sequenceCompleted=true;
        } finally {
            // These messages share the Xbox rumble envelope. End a cancelled
            // partial sequence with the already documented neutral flush.
            if(!sequenceCompleted)TryNeutralFlush(current);
        }
        // Do not immediately hand the 100A device back to Windows while its
        // firmware is still scheduling reenumeration (~1.5 seconds upstream).
        var waiting=Stopwatch.StartNew();var buffer=new byte[64];bool disappeared=false;
        while(waiting.ElapsedMilliseconds<5_000) {
            stop.ThrowIfCancellationRequested();
            try {
                int n=current.Read(buffer,stop);
                if(n>0)lock(sync)RecordPacket(buffer.AsSpan(0,n),clock.ElapsedMilliseconds);
            } catch(UsbTransferException ex) when(ex.ErrorCode is -4 or -1) {disappeared=true;break;}
            await Task.Delay(20,stop);
        }
        if(!disappeared)TryNeutralFlush(current);
        current.Dispose();
        lock(sync)phase="Waiting for the same receiver after the GameSir mode switch";
        var deadline=Stopwatch.StartNew();string? lastSeen=null,stableKey=null;ushort stablePid=0;int stableCount=0;
        while(deadline.ElapsedMilliseconds<G7SessionSetup.ReenumerationTimeoutMilliseconds) {
            await Task.Delay(350,stop);
            var devices=UsbTransport.Enumerate(includeUnknownGameSir:true);
            var samePort=devices.Where(d=>d.Key==original.Key).ToArray();
            string seen=samePort.Length==0 ? "temporarily absent" : string.Join(", ",samePort.Select(d=>$"3537:{d.ProductId:X4}"));
            if(seen!=lastSeen){
                Log?.Invoke($"USB port {original.Key}: {seen}.");
                if(samePort.Length==0 && devices.Count>0)
                    Log?.Invoke("Other visible GameSir identities (not opened): "+string.Join(", ",devices.Select(d=>$"{d.ProductId:X4} op {d.Key}")));
                lastSeen=seen;
            }
            var target=G7SessionSetup.FindTransitionTarget(original.Key,devices.Select(d=>new UsbDeviceIdentity(d.Key,0x3537,d.ProductId)).ToArray());
            if(target is null){stableCount=0;continue;}
            if(target.Value.PhysicalKey==stableKey && target.Value.Product==stablePid)stableCount++;else{stableKey=target.Value.PhysicalKey;stablePid=target.Value.Product;stableCount=1;}
            if(stableCount<2)continue;
            var device=samePort.Single(d=>d.ProductId==target.Value.Product);
            Log?.Invoke($"GameSir mode switch observed: {original.ProductId:X4} → {device.ProductId:X4}. Reopening the same USB port.");
            IControllerTransport? opened=null;
            try {
                opened=new UsbTransport(device,message=>Log?.Invoke(message));stop.ThrowIfCancellationRequested();
                return(opened,device);
            } catch {opened?.Dispose();throw;}
        }
        throw new IOException("No supported 106B/109B/109C identity appeared on the same USB port within 15 seconds. Last seen: "+lastSeen+". No automatic retry. Save diagnostics.");
    }

    private IControllerTransport Open(ControllerDevice device)=>device.Transport switch {
        "GameInput"=>new GameInputTransport(device,message=>Log?.Invoke(message)),
        "HID"=>new HidTransport(device),
        _=>new UsbTransport(device,message=>Log?.Invoke(message))
    };

    private async Task<(IControllerTransport Transport,ControllerDevice Device)> InitializeNativeGyro(ControllerDevice original,GameInputTransport current,CancellationToken stop)
    {
        if(original.ProductId!=0x100A)return(current,original);
        string key=current.PhysicalKey;
        _=G7SessionSetup.FindTransitionTarget(key,[]);
        Log?.Invoke("Single GameSir session start through Windows rumble. The same USB location must reappear as GIP.");
        bool completed=false;
        try {
            foreach(var packet in G7SessionSetup.HandshakePackets()) {
                stop.ThrowIfCancellationRequested();current.Write(packet);
                await Task.Delay(G7SessionSetup.HandshakeSpacingMilliseconds,stop);
            }
            completed=true;
        } finally {if(!completed)TryNeutralFlush(current);}
        var waiting=Stopwatch.StartNew();var buffer=new byte[64];bool disappeared=false;
        try {
            while(waiting.ElapsedMilliseconds<5_000) {
                stop.ThrowIfCancellationRequested();
                try {int n=current.Read(buffer,stop);if(n>0)lock(sync)RecordPacket(buffer.AsSpan(0,n),clock.ElapsedMilliseconds);}
                catch(IOException ex) when(GameInputTransport.IsDisconnected(ex)){disappeared=true;break;}
                await Task.Delay(10,stop);
            }
        } finally {if(!disappeared)TryNeutralFlush(current);}
        current.Dispose();
        var deadline=Stopwatch.StartNew();string? lastId=null,lastSeen=null;int stable=0;
        while(deadline.ElapsedMilliseconds<G7SessionSetup.ReenumerationTimeoutMilliseconds) {
            await Task.Delay(350,stop);
            var devices=GameInputTransport.Enumerate().Where(d=>d.PhysicalKey==key).ToArray();
            var candidates=devices.Where(d=>G7DeviceCatalog.IsSessionIdentity(G7DeviceCatalog.Vendor,d.ProductId)).ToArray();
            var seen=devices.Length==0?"temporarily absent":string.Join(", ",devices.Select(d=>$"3537:{d.ProductId:X4}"));
            if(seen!=lastSeen){lastSeen=seen;Log?.Invoke("Windows devices at the same USB location: "+seen);}
            if(candidates.Length!=1){stable=0;continue;}
            var target=candidates[0];stable=target.Key==lastId?stable+1:1;lastId=target.Key;
            if(stable<2)continue;
            IControllerTransport? opened=null;
            try {opened=Open(target);stop.ThrowIfCancellationRequested();return(opened,target);}
            catch {opened?.Dispose();throw;}
        }
        throw new IOException("The Windows startup request did not produce a stable GIP identity at the same USB location. Last seen: "+lastSeen+". Save diagnostics; this startup sequence will not be repeated.");
    }

    private void TryNeutralFlush(IControllerTransport transport)
    {
        try {transport.Write([0,8,0,0,0,0,0,0]);}
        catch(IOException ex){Log?.Invoke("Interrupted startup sequence could not receive a neutral flush: "+ex.Message);}
    }

    public void StartSteam(bool physicalInputHidden=false)
    {
        TaskCompletionSource finished;
        lock(sync) {
            if(steam is not null || startingSteam) return;
            if(!connected || !input.Snapshot(clock.ElapsedMilliseconds,settings).HasTelemetry)
                throw new InvalidOperationException("Wait for the input session and actual motion data before starting virtual output.");
            if(!exclusive && (!physicalInputHidden || !System.Security.Principal.WindowsIdentity.GetCurrent().IsSystem))
                throw new InvalidOperationException("Virtual output requires the service and a hidden physical legacy input interface to avoid duplicate input.");
            startingSteam=true;
            finished=new(TaskCreationOptions.RunContinuationsAsynchronously);
            steamCreation=finished.Task;
        }
        SteamOutput? created=null;
        try {
            // Creation may take seconds; do not hold the input/status lock.
            created=new SteamOutput();
            lock(sync) {
                if(!connected || cancellation?.IsCancellationRequested!=false)
                    throw new IOException("The input session has stopped. Closing the virtual controller.");
                steam=created;created=null;
            }
        } finally {
            try {created?.Dispose();}
            finally {lock(sync) startingSteam=false;finished.TrySetResult();}
        }
        Log?.Invoke("Virtual Steam Controller 2026 connected; forwarding input.");
    }
    public void StopSteam()
    {
        SteamOutput? old;lock(sync) {old=steam;steam=null;}
        old?.Dispose();Log?.Invoke("Steam output stopped.");
    }
    public async Task StopAsync()
    {
        cancellation?.Cancel();
        if(running is not null) await running;
        Task pending;lock(sync) pending=steamCreation;
        await pending;
    }
    public async ValueTask DisposeAsync()
    {
        await StopAsync();StopSteam();cancellation?.Dispose();
    }
}
