using System.Runtime.InteropServices;
using System.Text;
using G7Bridge.Core;

namespace G7Bridge.Windows;

internal readonly record struct Receiver(string InstanceId,string Location,bool ControllerPresent);

// Read-only Plug and Play scan of present devices. It never opens a device or
// touches hiding rules, so the service can run it cheaply while waiting.
internal static class ReceiverScanner
{
    private const uint FilterEnumerator=0x1,FilterPresent=0x100;
    [StructLayout(LayoutKind.Sequential)] private struct DevPropKey {public Guid Category;public uint Id;}
    // DEVPKEY_Device_LocationPaths; its first entry equals the GameInput physical key.
    private static readonly DevPropKey LocationPaths=new(){Category=new("a45c254e-df1c-4efd-8020-67d146a850e0"),Id=37};
    [DllImport("cfgmgr32.dll",CharSet=CharSet.Unicode)] private static extern int CM_Get_Device_ID_List_SizeW(out uint length,string filter,uint flags);
    [DllImport("cfgmgr32.dll",CharSet=CharSet.Unicode)] private static extern int CM_Get_Device_ID_ListW(string filter,[Out] char[] buffer,uint length,uint flags);
    [DllImport("cfgmgr32.dll",CharSet=CharSet.Unicode)] private static extern int CM_Locate_DevNodeW(out uint node,string instance,uint flags);
    [DllImport("cfgmgr32.dll")] private static extern int CM_Get_Child(out uint child,uint parent,uint flags);
    [DllImport("cfgmgr32.dll")] private static extern int CM_Get_Sibling(out uint sibling,uint node,uint flags);
    [DllImport("cfgmgr32.dll",CharSet=CharSet.Unicode)] private static extern int CM_Get_Device_IDW(uint node,StringBuilder id,int length,uint flags);
    [DllImport("cfgmgr32.dll",CharSet=CharSet.Unicode)] private static extern int CM_Get_DevNode_PropertyW(uint node,in DevPropKey key,out uint type,[Out] byte[]? buffer,ref uint size,uint flags);

    // Null when Windows could not return the device list; callers retry instead of treating it as empty.
    internal static Receiver[]? Scan()
    {
        // A device arriving between both calls makes the list larger; the next scan retries.
        if(CM_Get_Device_ID_List_SizeW(out uint length,"USB",FilterEnumerator|FilterPresent)!=0)return null;
        var buffer=new char[length];
        if(CM_Get_Device_ID_ListW("USB",buffer,length,FilterEnumerator|FilterPresent)!=0)return null;
        var found=new List<Receiver>();
        foreach(var id in new string(buffer).Split('\0',StringSplitOptions.RemoveEmptyEntries).Where(ReceiverPresence.IsReceiverRoot)) {
            if(CM_Locate_DevNodeW(out uint node,id,0)!=0)continue;
            found.Add(new(id,Location(node),ReceiverPresence.HasController(Descendants(node))));
        }
        return found.ToArray();
    }
    private static string Location(uint node)
    {
        uint size=0;
        CM_Get_DevNode_PropertyW(node,LocationPaths,out _,null,ref size,0);
        if(size==0 || size>8192)return "";
        var data=new byte[size];
        if(CM_Get_DevNode_PropertyW(node,LocationPaths,out _,data,ref size,0)!=0)return "";
        return Encoding.Unicode.GetString(data,0,(int)size).Split('\0',StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()??"";
    }
    private static List<string> Descendants(uint root)
    {
        var found=new List<string>();
        void Visit(uint parent,int depth)
        {
            if(depth>6 || found.Count>32 || CM_Get_Child(out uint child,parent,0)!=0)return;
            do {
                var id=new StringBuilder(1024);
                if(CM_Get_Device_IDW(child,id,id.Capacity,0)==0)found.Add(id.ToString());
                Visit(child,depth+1);
            } while(found.Count<=32 && CM_Get_Sibling(out child,child,0)==0);
        }
        Visit(root,0);
        return found;
    }
}
