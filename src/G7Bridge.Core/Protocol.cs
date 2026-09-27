using System.Buffers.Binary;

namespace G7Bridge.Core;

public static class G7Protocol
{
    // Actual Nexus wire lengths: byte 3 is the GIP payload length.
    public static byte[] BeginSession(byte sequence)=>[0x0F,0,sequence,1,0xF2];
    public static byte[] Heartbeat(byte sequence)=>[0x0F,0,sequence,2,0xF2,0];
    public static bool TryTelemetry(ReadOnlySpan<byte> report, out Telemetry? value)
    {
        value=null;
        if(report.Length != 64 || report[0]!=0x10 || report[3]!=60 || report[4]!=0xE0) return false;
        var pad=new PadState(Axis(report[5]),Invert(Axis(report[6])),Axis(report[7]),Invert(Axis(report[8])),
            (ushort)(report[12]*257),(ushort)(report[13]*257),PhysicalButtons(report),false);
        value=new Telemetry(S16(report,17),S16(report,19),S16(report,21),S16(report,23),S16(report,25),S16(report,27),
            report[33]<=100 ? report[33] : null,report[32]==1,pad,report.ToArray());
        return true;
    }
    public static bool TryGamepad(ReadOnlySpan<byte> report, out PadState value)
    {
        value=default;
        // MS-XUSBI's 20-byte Xbox 360 packet. Some backends pad it to 64 bytes.
        if(report.Length>=20 && report[0]==0 && report[1]==20)
        {
            var b=BinaryPrimitives.ReadUInt16LittleEndian(report[2..]);
            PadButtons buttons=0;
            (int Bit,PadButtons Button)[] mapping=[(1,PadButtons.Up),(2,PadButtons.Down),(4,PadButtons.Left),(8,PadButtons.Right),
                (0x10,PadButtons.Menu),(0x20,PadButtons.View),(0x40,PadButtons.L3),(0x80,PadButtons.R3),
                (0x100,PadButtons.LB),(0x200,PadButtons.RB),(0x400,PadButtons.Guide),
                (0x1000,PadButtons.A),(0x2000,PadButtons.B),(0x4000,PadButtons.X),(0x8000,PadButtons.Y)];
            foreach(var m in mapping) if((b&m.Bit)!=0) buttons|=m.Button;
            value=new(S16(report,6),S16(report,8),S16(report,10),S16(report,12),
                (ushort)(report[4]*257),(ushort)(report[5]*257),buttons,true);
            return true;
        }
        // Both the standard 14-byte and captured 32-byte payload share this prefix.
        // Chunked GIP messages need reassembly and must not enter this decoder.
        if(report.Length>=18 && report[0]==0x20 && (report[1]&0xC0)==0 &&
           report[3] is 14 or 32 && report.Length>=4+report[3])
        {
            var b=BinaryPrimitives.ReadUInt16LittleEndian(report[4..]);
            PadButtons buttons=0;
            (int Bit,PadButtons Button)[] mapping=[(4,PadButtons.Menu),(8,PadButtons.View),(16,PadButtons.A),(32,PadButtons.B),
                (64,PadButtons.X),(128,PadButtons.Y),(0x100,PadButtons.Up),(0x200,PadButtons.Down),
                (0x400,PadButtons.Left),(0x800,PadButtons.Right),(0x1000,PadButtons.LB),(0x2000,PadButtons.RB),
                (0x4000,PadButtons.L3),(0x8000,PadButtons.R3)];
            foreach(var m in mapping) if((b&m.Bit)!=0) buttons|=m.Button;
            var lt=BinaryPrimitives.ReadUInt16LittleEndian(report[6..]);
            var rt=BinaryPrimitives.ReadUInt16LittleEndian(report[8..]);
            if(lt>1023 || rt>1023) return false;
            value=new(S16(report,10),S16(report,12),S16(report,14),S16(report,16),
                (ushort)(lt*65535/1023),(ushort)(rt*65535/1023),buttons,true);
            return true;
        }
        return false;
    }
    private static short S16(ReadOnlySpan<byte> p,int o)=>BinaryPrimitives.ReadInt16LittleEndian(p[o..]);
    private static PadButtons PhysicalButtons(ReadOnlySpan<byte> p)
    {
        PadButtons buttons=(p[55]&15) switch {
            0=>PadButtons.Up,1=>PadButtons.Up|PadButtons.Right,2=>PadButtons.Right,
            3=>PadButtons.Down|PadButtons.Right,4=>PadButtons.Down,5=>PadButtons.Down|PadButtons.Left,
            6=>PadButtons.Left,7=>PadButtons.Up|PadButtons.Left,_=>PadButtons.None
        };
        (int Offset,int Mask,PadButtons Button)[] map=[
            (55,0x10,PadButtons.X),(55,0x20,PadButtons.A),(55,0x40,PadButtons.B),(55,0x80,PadButtons.Y),
            (56,1,PadButtons.LB),(56,2,PadButtons.RB),(56,0x10,PadButtons.View),(56,0x20,PadButtons.Menu),
            (56,0x40,PadButtons.L3),(56,0x80,PadButtons.R3),(57,1,PadButtons.Guide),(57,2,PadButtons.Share),
            (57,8,PadButtons.L4),(57,0x10,PadButtons.R4),(58,1,PadButtons.L5),(58,2,PadButtons.R5)];
        foreach(var m in map) if((p[m.Offset]&m.Mask)!=0) buttons|=m.Button;
        return buttons;
    }
    private static short Axis(byte b)=>(short)(b<128 ? (b-128)*256 : (b-128)*32767/127);
    private static short Invert(short v)=>v==short.MinValue ? short.MaxValue : (short)-v;
}

public static class TritonProtocol
{
    public static byte[] Encode(PadState pad, MotionState motion, byte sequence, uint timestamp, System.Numerics.Quaternion? orientation=null)
    {
        var p=new byte[54]; p[0]=0x42;p[1]=sequence;
        BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(2),(uint)pad.Buttons);
        W(p,6,(short)(pad.LT*32767L/65535)); W(p,8,(short)(pad.RT*32767L/65535));
        W(p,10,pad.LX);W(p,12,pad.LY);W(p,14,pad.RX);W(p,16,pad.RY);
        // Trackpads and touch flags stay absent, because the G7 has no such sensors.
        BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(30),timestamp);
        W(p,34,motion.AX);W(p,36,motion.AY);W(p,38,motion.AZ);
        W(p,40,motion.GX);W(p,42,motion.GY);W(p,44,motion.GZ);
        var q=orientation??System.Numerics.Quaternion.Identity;
        W(p,46,(short)(Math.Clamp(q.W,-1,1)*32767));W(p,48,(short)(Math.Clamp(q.X,-1,1)*32767));
        W(p,50,(short)(Math.Clamp(q.Y,-1,1)*32767));W(p,52,(short)(Math.Clamp(q.Z,-1,1)*32767));
        return p;
    }
    private static void W(byte[] p,int o,short value)=>BinaryPrimitives.WriteInt16LittleEndian(p.AsSpan(o),value);
}

public static class ButtonLearning
{
    public static IReadOnlyList<BitBinding> Candidates(IReadOnlyList<byte[]> released, IReadOnlyList<byte[]> held)
    {
        if(released.Count<8 || held.Count<8 || released.Concat(held).Any(p=>p.Length!=64 || p[0]!=0x10 || p[4]!=0xE0)) return [];
        var result=new List<BitBinding>();
        foreach(int offset in Offsets)
            for(int bit=1;bit<=128;bit<<=1)
            {
                if(!DigitalBit(offset,bit))continue;
                bool a=(released[0][offset]&bit)!=0,b=(held[0][offset]&bit)!=0;
                if(a!=b && released.All(p=>((p[offset]&bit)!=0)==a) && held.All(p=>((p[offset]&bit)!=0)==b))
                    result.Add(new(offset,(byte)bit,b));
            }
        return result;
    }
    public static PadButtons Apply(ReadOnlySpan<byte> report, IReadOnlyDictionary<PadButtons, BitBinding> bindings)
    {
        if(report.Length!=64 || report[0]!=0x10 || report[4]!=0xE0) return PadButtons.None;
        PadButtons result=0;
        foreach(var (button,b) in bindings)
            if(DigitalBit(b.Offset,b.Mask) && (((report[b.Offset]&b.Mask)!=0)==b.ActiveHigh)) result|=button;
        return result;
    }
    // Physical digital controls only. 59/60 are analog; 9..11 are post-remap.
    public static readonly int[] Offsets=[55,56,57,58];
    private static bool DigitalBit(int offset,int mask)=>mask!=0 && (mask&(mask-1))==0 &&
        ((offset switch {55=>0xF0,56=>0xF3,57=>0x1B,58=>0x03,_=>0})&mask)!=0;
    public static void Validate(BridgeSettings settings)
    {
        if(settings.Bindings is null || settings.GyroBias is not { Length:3 } || settings.AxisOrder is not { Length:3 } || settings.AxisSign is not { Length:3 })
            throw new ArgumentException("Onvolledige instellingen.");
        if(!double.IsFinite(settings.GyroCountsPerDps) || settings.GyroCountsPerDps<=0 || settings.GyroCountsPerDps>32768 ||
           !double.IsFinite(settings.AccelCountsPerG) || settings.AccelCountsPerG<=0 || settings.AccelCountsPerG>65536 ||
           settings.GyroBias.Any(v=>!double.IsFinite(v) || Math.Abs(v)>32768) ||
           !settings.AxisOrder.Order().SequenceEqual(new[]{0,1,2}) || settings.AxisSign.Any(v=>v!=1 && v!=-1))
            throw new ArgumentException("Ongeldige gyro-schaal, offset of asrichting.");
        var used=new HashSet<(int,byte)>();
        foreach(var (button,b) in settings.Bindings)
        {
            if(b is null || !Enum.IsDefined(button) || button==PadButtons.None || !DigitalBit(b.Offset,b.Mask))
                throw new ArgumentException("Ongeldige knopkoppeling.");
            if(!used.Add((b.Offset,b.Mask))) throw new ArgumentException("Dezelfde fysieke bit is aan meerdere knoppen gekoppeld.");
        }
    }
}

public static class MotionConversion
{
    public static MotionState Convert(Telemetry sample, BridgeSettings settings)
    {
        double[] g=[sample.GX,sample.GY,sample.GZ],a=[sample.AX,sample.AY,sample.AZ];
        short[] go=new short[3],ao=new short[3];
        for(int i=0;i<3;i++)
        {
            int j=settings.AxisOrder[i];
            go[i]=Clamp((g[j]-settings.GyroBias[j])*settings.AxisSign[i]*16.384/settings.GyroCountsPerDps);
            ao[i]=Clamp(a[j]*settings.AxisSign[i]*16384/settings.AccelCountsPerG);
        }
        return new(go[0],go[1],go[2],ao[0],ao[1],ao[2]);
    }
    private static short Clamp(double v)=>(short)Math.Clamp(Math.Round(v),short.MinValue,short.MaxValue);
}
