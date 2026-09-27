using System.Buffers.Binary;

namespace G7Bridge.Core;

public static class TritonFeedback
{
    // SDL's MsgHapticRumble, without the report ID: type, intensity,
    // left speed/gain, right speed/gain. Only ordinary rumble is translated.
    public static bool TryRumble(byte reportId,ReadOnlySpan<byte> payload,out uint motors)
    {
        motors=0;
        if(reportId!=0x80 || payload.Length!=9 || payload[0]!=0)return false;
        ushort left=BinaryPrimitives.ReadUInt16LittleEndian(payload[3..]),right=BinaryPrimitives.ReadUInt16LittleEndian(payload[6..]);
        motors=(uint)left|((uint)right<<16);return true;
    }
    public static byte[] Battery(byte percentage,bool charging)
    {
        if(percentage>100)throw new ArgumentOutOfRangeException(nameof(percentage));
        // Voltages, current and temperature are not known from our G7 input.
        var report=new byte[15];report[0]=0x43;report[1]=charging?(byte)(percentage==100?4:2):(byte)1;report[2]=percentage;return report;
    }
}
