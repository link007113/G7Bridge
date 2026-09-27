using System.Buffers.Binary;
using System.Text.Json.Nodes;

namespace G7Bridge.Core;

public static class TritonIdentity
{
    public static string UseBridgeOwnedStream(string profile)
    {
        var root=JsonNode.Parse(profile)??throw new InvalidDataException("Leeg controllerprofiel.");
        var extended=root["extendedReport"]?.AsObject()??throw new InvalidDataException("Het Triton-rapport ontbreekt.");
        // SubmitRawExtendedReport does not refresh HIDMaestro's SubmitState idle
        // timer. Its default idle generator would interleave empty frames.
        extended["idleFrameIntervalMs"]=0;return root.ToJsonString();
    }
    public static string WithCompatibilityVersion(string profile,uint targetVersion)
    {
        var root=JsonNode.Parse(profile)??throw new InvalidDataException("Leeg controllerprofiel.");
        var reports=root["featureStubs"]?["reports"]?.AsArray()??throw new InvalidDataException("Controllerattributen ontbreken.");
        int changed=0;
        foreach(var report in reports) {
            if(report?["id"]?.GetValue<string>()!="0x83")continue;
            byte[] bytes=Convert.FromHexString(report["data"]!.GetValue<string>());
            if(bytes.Length<3 || bytes[0]!=1 || bytes[1]!=0x83 || bytes.Length!=3+bytes[2] || bytes[2]%5!=0)throw new InvalidDataException("Onverwachte attribuutindeling.");
            for(int i=3;i<bytes.Length;i+=5)if(bytes[i]==4) {
                uint existing=BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(i+1,4));
                BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(i+1,4),Math.Max(existing,targetVersion));changed++;
            }
            report["data"]=Convert.ToHexString(bytes);
        }
        if(changed!=1)throw new InvalidDataException("Het compatibiliteitsversieveld is niet eenduidig.");
        return root.ToJsonString();
    }
}
