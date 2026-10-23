using System.Buffers.Binary;
using System.Text.Json.Nodes;

namespace G7Bridge.Core;

public static class TritonIdentity
{
    public static string UseBridgeOwnedStream(string profile)
    {
        var root=JsonNode.Parse(profile)??throw new InvalidDataException("Leeg controllerprofiel.");
        var extended=root["extendedReport"]?.AsObject()??throw new InvalidDataException("Het Triton-rapport ontbreekt.");
        // 2.0.2's default clock retains only the latest raw report. The bridge
        // owns its IMU timestamps and also sends battery (0x43) reports, so
        // deliver each submit. Since 1.10 the idle repeat retains our raw frame.
        extended["send"]="submit";
        extended["idleFrameIntervalMs"]=4;
        // 2.0.2 varies these command-channel serials for keyed controllers.
        // Keep the pre-upgrade serials that name existing Steam layouts. The
        // stable keyed USB identity is retained independently by the SDK.
        if(root["featureStubs"]?["reports"] is JsonArray reports)
            foreach(var report in reports) {
                if(report?["data"]?.GetValue<string>() is not string data)continue;
                byte[] bytes=Convert.FromHexString(data);
                if(bytes.Length<4 || bytes[0]!=1 || bytes[1]!=0xAE || bytes[3]>1)continue;
                string expected=bytes[3]==0?"01AE14004D584139393630323030303031":"01AE140146584139393630323030303031";
                if(!data.Equals(expected,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Unexpected Steam controller serial; existing layouts must retain their identity.");
                if(report["patch"] is JsonArray patches)
                    for(int i=patches.Count-1;i>=0;i--)
                        if(patches[i]?["source"]?.GetValue<string>()=="instanceSerial")patches.RemoveAt(i);
            }
        return root.ToJsonString();
    }
    public static string WithCompatibilityVersion(string profile,uint targetVersion)
    {
        var root=JsonNode.Parse(profile)??throw new InvalidDataException("Leeg controllerprofiel.");
        var reports=root["featureStubs"]?["reports"]?.AsArray()??throw new InvalidDataException("Controllerattributen ontbreken.");
        int changed=0;
        foreach(var report in reports) {
            if(report is null)continue;
            bool legacy=report["id"]?.GetValue<string>()=="0x83";
            bool command=report["id"]?.GetValue<string>()=="0x01" &&
                report["message"]?["at"]?.GetValue<int>()==1 &&
                string.Equals(report["message"]?["value"]?.GetValue<string>(),"83",StringComparison.OrdinalIgnoreCase);
            if(!legacy && !command)continue;
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
