using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using G7Bridge.Core;
using HIDMaestro;
using Microsoft.Win32.SafeHandles;

// Explicit opt-in hardware checks. The SDK test has an isolated USB identity,
// so its synthetic buttons cannot be mapped by Steam onto the user's desktop.
// --observe only reads the production bridge; --rumble is a bounded real pulse.
try {
    if(args is ["--sdk",var directory])await SdkTest(Path.GetFullPath(directory));
    else if(args is ["--observe",var seconds,var result])await Observe(int.Parse(seconds),result);
    else if(args is ["--rumble"])await PhysicalRumble();
    else if(args is ["--rumble-motors"])await MotorProbe();
    else if(args is ["--watch-button",var button,var destination])await WatchButton(button,destination);
    else throw new ArgumentException("Use --sdk <scratch-directory>, --observe <seconds> <json>, --watch-button <name> <json>, --rumble or --rumble-motors.");
    return 0;
} catch(Exception ex) {Console.Error.WriteLine(ex);return 1;}

static async Task SdkTest(string scratch)
{
    if(!HMContext.IsUsbipBackendAvailable)throw new IOException("Install the supported USB/IP backend first.");
    Directory.CreateDirectory(scratch);
    using var resource=Assembly.GetExecutingAssembly().GetManifestResourceStream("TritonProfile.json")!;
    using var reader=new StreamReader(resource);
    using(var file=new StreamWriter(new FileStream(Path.Combine(scratch,"steam-controller-2.json"),FileMode.CreateNew,FileAccess.Write)))
        file.Write(TritonIdentity.UseBridgeOwnedStream(TritonIdentity.WithCompatibilityVersion(reader.ReadToEnd(),0x6ACFF323)));
    using var context=new HMContext();
    context.LoadProfilesFromDirectory(scratch);
    var source=context.GetProfile("steam-controller-2")??throw new IOException("Prepared profile did not load.");
    // Builder updates the device descriptor IDs as well. No physical transport,
    // Steam VID/PID, production identity key, or physical rumble is used here.
    const string key="grimm-g7bridge-sdk-validation-v1";
    var profile=new HMProfileBuilder().FromProfile(source).Id("g7bridge-sdk-validation").Vid(0x1209).Pid(0xF7B2).Build();
    using var controller=context.CreateController(profile,key);
    using var device=await HidDevice.Find(0x1209,0xF7B2,Serial(key));
    var outputs=new ConcurrentQueue<uint>();
    controller.OutputReceived+=(_,packet)=>{
        if(packet.Source==HMOutputSource.HidOutput && TritonFeedback.TryRumble(packet.ReportId,packet.Data.Span,out uint value))outputs.Enqueue(value);
    };
    byte sequence=0;
    async Task Check(byte[] frame)
    {
        controller.SubmitRawExtendedReport(frame);
        await device.WaitFor(frame);
    }
    foreach(var button in Enum.GetValues<PadButtons>().Where(b=>b!=PadButtons.None)) {
        await Check(TritonProtocol.Encode(new(0,0,0,0,0,0,button,false),default,sequence++,1));
        await Check(TritonProtocol.Encode(default,default,sequence++,2));
    }
    Console.WriteLine("PASS all 20 independent button presses and releases through Windows HID");
    foreach(int trigger in new[]{0,1,127,128,254,255}) {
        await Check(TritonProtocol.Encode(new(short.MinValue,short.MaxValue,-12345,12345,(ushort)(trigger*257),(ushort)((255-trigger)*257),0,true),default,sequence++,3));
    }
    Console.WriteLine("PASS signed sticks and independent partial/full analog triggers");
    var held=TritonProtocol.Encode(default,new(321,-654,987,123,-456,16384),sequence++,123456);
    await Check(held);
    var clock=Stopwatch.StartNew();int repeats=0;
    while(clock.ElapsedMilliseconds<120) {
        byte[] read=await device.Read();
        if(read[0]!=0x42)continue;
        Require(read.AsSpan(0,54).SequenceEqual(held),"Idle repeat injected or changed a raw motion frame.");repeats++;
    }
    Require(repeats>=5,"The 4 ms idle repeat did not produce enough frames.");
    Console.WriteLine($"PASS raw signed gyro/accel/quaternion and idle hold ({repeats} repeated frames, no neutral injection)");
    await Check(TritonFeedback.Battery(73,false));
    await Check(TritonProtocol.Encode(default,default,sequence++,123460));
    Console.WriteLine("PASS separate battery and input reports both delivered");
    foreach(byte index in new byte[]{0,1}) {
        var answer=device.Feature(0xAE,index);
        string serial=Encoding.ASCII.GetString(answer,4,13);
        Require(serial==(index==0?"MXA9960200001":"FXA9960200001"),"The Steam command-channel serial changed.");
    }
    var attributes=device.Feature(0x83,0);
    Require(BinaryPrimitives.ReadUInt32LittleEndian(attributes.AsSpan(19,4))>=0x6ACFF323,"Firmware compatibility attribute was lost.");
    Console.WriteLine("PASS existing Steam board/unit serials and firmware compatibility response");
    foreach(uint strength in new uint[]{0x12344000,0}) {
        var command=new byte[device.OutputBytes];command[0]=0x80;
        BinaryPrimitives.WriteUInt16LittleEndian(command.AsSpan(4),(ushort)strength);
        BinaryPrimitives.WriteUInt16LittleEndian(command.AsSpan(7),(ushort)(strength>>16));
        await device.Write(command);
        var timeout=Stopwatch.StartNew();bool received=false;
        while(timeout.ElapsedMilliseconds<1500 && !received) {
            while(outputs.TryDequeue(out uint value))if(value==strength)received=true;
            if(!received)await Task.Delay(10);
        }
        Require(received,"Rumble output did not reach the SDK callback with both motor values intact.");
    }
    Console.WriteLine("PASS Windows HID rumble output -> SDK callback, separate motors and explicit stop");
}

static async Task Observe(int seconds,string path)
{
    Require(seconds is >=5 and <=600,"Observation must be between 5 and 600 seconds.");
    using var device=await HidDevice.Find(0x28DE,0x1302,VirtualControllerIdentity.UsbSerial);
    var minimum=new int[]{32767,32767,32767};var maximum=new int[]{-32768,-32768,-32768};
    var timestamps=new HashSet<uint>();PadButtons buttons=0;int count=0,invalidQuaternion=0;
    int minLt=32767,maxLt=0,minRt=32767,maxRt=0;bool partialLt=false,partialRt=false;
    string board=Encoding.ASCII.GetString(device.Feature(0xAE,0),4,13),unit=Encoding.ASCII.GetString(device.Feature(0xAE,1),4,13);
    var clock=Stopwatch.StartNew();
    while(clock.Elapsed.TotalSeconds<seconds && !File.Exists(path+".stop")) {
        byte[] frame=await device.Read();if(frame[0]!=0x42)continue;
        count++;buttons|=(PadButtons)(BinaryPrimitives.ReadUInt32LittleEndian(frame.AsSpan(2))&0xFFFFF);
        timestamps.Add(BinaryPrimitives.ReadUInt32LittleEndian(frame.AsSpan(30)));
        int lt=BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(6)),rt=BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(8));
        minLt=Math.Min(minLt,lt);maxLt=Math.Max(maxLt,lt);minRt=Math.Min(minRt,rt);maxRt=Math.Max(maxRt,rt);
        partialLt|=lt is >1000 and <30000;partialRt|=rt is >1000 and <30000;
        double norm=0;
        for(int i=0;i<3;i++){int g=BinaryPrimitives.ReadInt16LittleEndian(frame.AsSpan(40+i*2));minimum[i]=Math.Min(minimum[i],g);maximum[i]=Math.Max(maximum[i],g);}
        for(int i=46;i<54;i+=2){double q=BinaryPrimitives.ReadInt16LittleEndian(frame.AsSpan(i))/32767.0;norm+=q*q;}
        if(norm is <0.9 or >1.1)invalidQuaternion++;
    }
    var missing=Enum.GetValues<PadButtons>().Where(b=>b!=0 && (buttons&b)==0).ToArray();
    var result=new {Seconds=clock.Elapsed.TotalSeconds,Reports=count,DistinctTimestamps=timestamps.Count,BoardSerial=board,UnitSerial=unit,ButtonsSeen=buttons.ToString(),MissingButtons=missing.Select(b=>b.ToString()).ToArray(),LeftTrigger=new{Min=minLt,Max=maxLt,Partial=partialLt},RightTrigger=new{Min=minRt,Max=maxRt,Partial=partialRt},GyroMin=minimum,GyroMax=maximum,InvalidQuaternion=invalidQuaternion};
    File.WriteAllText(path,JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine(JsonSerializer.Serialize(result));
    Require(count>100 && timestamps.Count>100 && invalidQuaternion==0,"Input stream or orientation failed.");
    Require(board=="MXA9960200001" && unit=="FXA9960200001","Steam layout identity changed.");
    Require(missing.Length==0 && partialLt && partialRt,"Physical button/analog trigger sequence is incomplete; see JSON.");
    Require(Enumerable.Range(0,3).All(i=>maximum[i]-minimum[i]>200),"Movement on all three gyro axes is incomplete.");
}

static async Task PhysicalRumble()
{
    using var device=await HidDevice.Find(0x28DE,0x1302,VirtualControllerIdentity.UsbSerial);
    await RumbleTest.RunAsync(async(strength,token)=>await device.Write(RumbleTest.Report(strength,device.OutputBytes),token),CancellationToken.None);
    Console.WriteLine("A bounded 400 ms pulse and an explicit neutral command were sent. Physical sensation requires user confirmation.");
}

static async Task MotorProbe()
{
    using var device=await HidDevice.Find(0x28DE,0x1302,VirtualControllerIdentity.UsbSerial);
    foreach(bool left in new[]{true,false}) {
        try {
            var clock=Stopwatch.StartNew();
            while(clock.ElapsedMilliseconds<1000) {
                var report=RumbleTest.Report(0,device.OutputBytes);
                BinaryPrimitives.WriteUInt16LittleEndian(report.AsSpan(left?4:7),0xC000);
                using var timeout=new CancellationTokenSource(200);
                await device.Write(report,timeout.Token);await Task.Delay(40);
            }
        } finally {
            using var timeout=new CancellationTokenSource(200);
            await device.Write(RumbleTest.Report(0,device.OutputBytes),timeout.Token);
        }
        await Task.Delay(500);
    }
    Console.WriteLine("Sent left then right: one second each at 75%, with explicit stops. Physical sensation requires user confirmation.");
}

static async Task WatchButton(string name,string path)
{
    var button=Enum.Parse<PadButtons>(name,true);uint mask=(uint)button;
    Require(mask!=0 && (mask&(mask-1))==0 && (mask&0xFFFFF)==mask,"Choose one named controller button.");
    var clock=Stopwatch.StartNew();
    File.WriteAllText(path,JsonSerializer.Serialize(new{Button=button.ToString(),Completed=false,State="Waiting for the owned controller and button press"}));
    while(clock.Elapsed.TotalMinutes<30) {
        try {
            using var device=await HidDevice.Find(0x28DE,0x1302,VirtualControllerIdentity.UsbSerial);
            byte[]? pressed=null;
            while(clock.Elapsed.TotalMinutes<30) {
                byte[] frame=await device.Read();if(frame[0]!=0x42)continue;
                bool down=(BinaryPrimitives.ReadUInt32LittleEndian(frame.AsSpan(2))&mask)!=0;
                if(down && pressed is null) {
                    pressed=frame;
                    File.WriteAllText(path,JsonSerializer.Serialize(new{Button=button.ToString(),Completed=false,State="Press observed; waiting for release",Press=Convert.ToHexString(pressed)}));
                }
                if(!down && pressed is not null) {
                    var result=new{Button=button.ToString(),Completed=true,Seconds=clock.Elapsed.TotalSeconds,Press=Convert.ToHexString(pressed),Release=Convert.ToHexString(frame)};
                    File.WriteAllText(path,JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true}));
                    Console.WriteLine(JsonSerializer.Serialize(result));return;
                }
            }
        } catch(Exception ex) when(ex is IOException or OperationCanceledException) {
            // A press and release must belong to the same live connection.
            File.WriteAllText(path,JsonSerializer.Serialize(new{Button=button.ToString(),Completed=false,State="Waiting for the owned controller to reconnect"}));
            await Task.Delay(500);
        }
    }
    throw new IOException("Button press/release not observed within 30 minutes.");
}

static string Serial(string key)=>"HM"+Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes("HIDMaestro.DeviceIdentity.v1\n"+key)).AsSpan(2,6));
static void Require(bool condition,string message){if(!condition)throw new IOException(message);}

sealed class HidDevice : IDisposable
{
    private readonly SafeFileHandle metadata;
    private readonly FileStream input,output;
    public int OutputBytes {get;}
    private HidDevice(string path,SafeFileHandle meta,int outputBytes)
    {
        metadata=meta;OutputBytes=outputBytes;
        input=new FileStream(CreateFileW(path,0x80000000,3,0,3,0x40000000,0),FileAccess.Read,128,true);
        output=new FileStream(CreateFileW(path,0x40000000,3,0,3,0x40000000,0),FileAccess.Write,128,true);
    }
    public static async Task<HidDevice> Find(ushort vid,ushort pid,string serial)
    {
        for(int attempt=0;attempt<80;attempt++) {
            Guid guid=new("4D1E55B2-F16F-11CF-88CB-001111000030");
            if(CM_Get_Device_Interface_List_SizeW(out uint size,ref guid,null,0)==0 && size is >1 and <1_000_000) {
                var list=new char[size];CM_Get_Device_Interface_ListW(ref guid,null,list,size,0);
                foreach(string path in new string(list).Split('\0',StringSplitOptions.RemoveEmptyEntries)) {
                    if(!path.Contains($"vid_{vid:x4}&pid_{pid:x4}",StringComparison.OrdinalIgnoreCase))continue;
                    var meta=CreateFileW(path,0,3,0,3,0,0);
                    if(meta.IsInvalid){meta.Dispose();continue;}
                    var text=new byte[256];var caps=new byte[64];
                    if(!HidD_GetSerialNumberString(meta,text,text.Length) || Encoding.Unicode.GetString(text).TrimEnd('\0')!=serial || !HidD_GetPreparsedData(meta,out nint pp)){meta.Dispose();continue;}
                    int status;try{status=HidP_GetCaps(pp,caps);}finally{HidD_FreePreparsedData(pp);}
                    if(status!=0x110000 || BitConverter.ToUInt16(caps,2)!=0xFF00 || BitConverter.ToUInt16(caps,4)!=54){meta.Dispose();continue;}
                    return new HidDevice(path,meta,BitConverter.ToUInt16(caps,6));
                }
            }
            await Task.Delay(100);
        }
        throw new IOException("Expected owned virtual HID collection was not found.");
    }
    public async Task<byte[]> Read()
    {
        using var timeout=new CancellationTokenSource(2000);var bytes=new byte[54];
        int read=await input.ReadAsync(bytes,timeout.Token);if(read<1)throw new IOException("HID stream ended.");return bytes;
    }
    public async Task WaitFor(byte[] expected)
    {
        var clock=Stopwatch.StartNew();
        while(clock.ElapsedMilliseconds<2000){var bytes=await Read();if(bytes.AsSpan(0,expected.Length).SequenceEqual(expected))return;}
        throw new IOException("Expected raw frame was not delivered: "+Convert.ToHexString(expected));
    }
    public ValueTask Write(byte[] report,CancellationToken stop=default)=>output.WriteAsync(report,stop);
    public byte[] Feature(byte command,byte index)
    {
        var bytes=new byte[64];bytes[0]=1;bytes[1]=command;bytes[2]=command==0xAE?(byte)1:(byte)0;bytes[3]=index;
        if(!HidD_SetFeature(metadata,bytes,bytes.Length))throw new IOException("HID feature write failed: "+Marshal.GetLastWin32Error());
        bytes=new byte[64];bytes[0]=1;
        if(!HidD_GetFeature(metadata,bytes,bytes.Length) || bytes[1]!=command)throw new IOException("HID feature reply failed: "+Marshal.GetLastWin32Error());
        return bytes;
    }
    public void Dispose(){input.Dispose();output.Dispose();metadata.Dispose();}
    [DllImport("cfgmgr32.dll",CharSet=CharSet.Unicode)]static extern int CM_Get_Device_Interface_List_SizeW(out uint size,ref Guid guid,string? id,uint flags);
    [DllImport("cfgmgr32.dll",CharSet=CharSet.Unicode)]static extern int CM_Get_Device_Interface_ListW(ref Guid guid,string? id,[Out]char[] list,uint size,uint flags);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern SafeFileHandle CreateFileW(string path,uint access,uint share,nint security,uint disposition,uint flags,nint template);
    [DllImport("hid.dll")][return:MarshalAs(UnmanagedType.U1)]static extern bool HidD_GetSerialNumberString(SafeFileHandle handle,[Out]byte[] text,int length);
    [DllImport("hid.dll")][return:MarshalAs(UnmanagedType.U1)]static extern bool HidD_GetPreparsedData(SafeFileHandle handle,out nint data);
    [DllImport("hid.dll")][return:MarshalAs(UnmanagedType.U1)]static extern bool HidD_FreePreparsedData(nint data);
    [DllImport("hid.dll")]static extern int HidP_GetCaps(nint data,[Out]byte[] caps);
    [DllImport("hid.dll",SetLastError=true)][return:MarshalAs(UnmanagedType.U1)]static extern bool HidD_SetFeature(SafeFileHandle handle,byte[] report,int length);
    [DllImport("hid.dll",SetLastError=true)][return:MarshalAs(UnmanagedType.U1)]static extern bool HidD_GetFeature(SafeFileHandle handle,[Out]byte[] report,int length);
}
