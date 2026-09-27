#pragma once
#include <windows.h>
#include <cfgmgr32.h>
#include <initguid.h>
#include <devpkey.h>
#include <string>
#include <vector>

namespace G7Native {
struct GipUsbDevice {std::wstring instance,location;};
inline std::vector<GipUsbDevice> ListGipDevices() {
    ULONG count{};constexpr ULONG flags=CM_GETIDLIST_FILTER_ENUMERATOR|CM_GETIDLIST_FILTER_PRESENT;
    if(CM_Get_Device_ID_List_SizeW(&count,L"USB",flags)!=CR_SUCCESS || count<2 || count>1'000'000)return {};
    std::vector<wchar_t> ids(count);
    if(CM_Get_Device_ID_ListW(L"USB",ids.data(),count,flags)!=CR_SUCCESS)return {};
    std::vector<GipUsbDevice> result;
    for(const wchar_t* id=ids.data();*id;id+=wcslen(id)+1) {
        constexpr wchar_t prefix[]=L"USB\\VID_3537&PID_106B\\";
        if(_wcsnicmp(id,prefix,wcslen(prefix))!=0)continue;
        DEVINST node{};if(CM_Locate_DevNodeW(&node,const_cast<wchar_t*>(id),CM_LOCATE_DEVNODE_NORMAL)!=CR_SUCCESS)continue;
        ULONG state{},problem{};
        if(CM_Get_DevNode_Status(&state,&problem,node,0)!=CR_SUCCESS || !(state&DN_STARTED) || problem)continue;
        wchar_t location[2048]{};ULONG bytes=sizeof(location);DEVPROPTYPE type{};
        if(CM_Get_DevNode_PropertyW(node,&DEVPKEY_Device_LocationPaths,&type,reinterpret_cast<PBYTE>(location),&bytes,0)!=CR_SUCCESS || type!=DEVPROP_TYPE_STRING_LIST || !location[0])continue;
        result.push_back({id,location});
    }
    return result;
}
}
