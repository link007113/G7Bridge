using System.Buffers.Binary;

namespace G7Bridge.Core;

public static class GameInputPackets
{
    // GameInput supplies the GIP payload and message ID separately. The wire
    // header below is synthetic; sequence zero is not a captured USB sequence.
    public static byte[]? Normalize(uint id,ReadOnlySpan<byte> payload)
    {
        if(!((id==0x10 && payload.Length==60 && payload[0]==0xE0) ||
            (id==0x20 && payload.Length is 14 or 32))) return null;
        var result=new byte[payload.Length+4];result[0]=(byte)id;result[3]=(byte)payload.Length;
        payload.CopyTo(result.AsSpan(4));return result;
    }

    // A current GameInput gamepad snapshot, encoded in the decoder's GIP shape.
    // This preserves held axes even when the physical GIP stream is event-driven.
    public static byte[] Gamepad(uint buttons,float lt,float rt,float lx,float ly,float rx,float ry)
    {
        var p=new byte[18];p[0]=0x20;p[3]=14;
        ushort mapped=0;
        (uint From,ushort To)[] map=[(1,4),(2,8),(4,0x10),(8,0x20),(16,0x40),(32,0x80),
            (64,0x100),(128,0x200),(256,0x400),(512,0x800),(1024,0x1000),(2048,0x2000),(4096,0x4000),(8192,0x8000)];
        foreach(var m in map)if((buttons&m.From)!=0)mapped|=m.To;
        BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(4),mapped);
        BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(6),Trigger(lt));
        BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(8),Trigger(rt));
        var axes=new[]{lx,ly,rx,ry};
        for(int i=0;i<4;i++)BinaryPrimitives.WriteInt16LittleEndian(p.AsSpan(10+2*i),Axis(axes[i]));
        return p;
    }
    private static ushort Trigger(float value)=>float.IsFinite(value)?(ushort)Math.Round(Math.Clamp(value,0,1)*1023): (ushort)0;
    private static short Axis(float value)=>float.IsFinite(value)?(short)Math.Round(Math.Clamp(value,-1,1)*(value<0?32768:32767)):(short)0;
}
