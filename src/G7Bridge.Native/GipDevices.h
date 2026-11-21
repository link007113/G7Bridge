#pragma once
#include <windows.h>
#include <cfgmgr32.h>
#include <initguid.h>
#include <devpkey.h>
#include <cstdint>
#include <string>
#include <vector>

namespace G7Native {
inline constexpr uint16_t OriginalGipProduct=0x106B;
inline constexpr uint16_t XboxModeProduct=0x1004;

inline bool HasGipCompatibleId(const wchar_t* ids) {
    for(auto id=ids;*id;id+=wcslen(id)+1) {
        if(_wcsicmp(id,L"USB\\MS_COMP_XGIP10")==0)return true;
    }
    return false;
}

struct GipUsbDevice {std::wstring instance,location;uint16_t product;};
inline std::vector<GipUsbDevice> ListGipDevices() {
    ULONG count{};constexpr ULONG flags=CM_GETIDLIST_FILTER_ENUMERATOR|CM_GETIDLIST_FILTER_PRESENT;
    if(CM_Get_Device_ID_List_SizeW(&count,L"USB",flags)!=CR_SUCCESS || count<2 || count>1'000'000)return {};
    std::vector<wchar_t> ids(count);
    if(CM_Get_Device_ID_ListW(L"USB",ids.data(),count,flags)!=CR_SUCCESS)return {};
    std::vector<GipUsbDevice> result;
    constexpr wchar_t original[]=L"USB\\VID_3537&PID_106B\\";
    constexpr wchar_t xboxModePrefix[]=L"USB\\VID_3537&PID_1004\\";
    for(const wchar_t* id=ids.data();*id;id+=wcslen(id)+1) {
        const bool xboxMode=_wcsnicmp(id,xboxModePrefix,wcslen(xboxModePrefix))==0;
        if(!xboxMode && _wcsnicmp(id,original,wcslen(original))!=0)continue;
        DEVINST node{};if(CM_Locate_DevNodeW(&node,const_cast<wchar_t*>(id),CM_LOCATE_DEVNODE_NORMAL)!=CR_SUCCESS)continue;
        ULONG state{},problem{};
        if(CM_Get_DevNode_Status(&state,&problem,node,0)!=CR_SUCCESS || !(state&DN_STARTED) || problem)continue;
        if(xboxMode) {
            // The T4 Kaleid shares the PID and has an Xbox 360 USB interface.
            wchar_t compatibleIds[2048]{};ULONG bytes=sizeof(compatibleIds);DEVPROPTYPE type{};
            if(CM_Get_DevNode_PropertyW(node,&DEVPKEY_Device_CompatibleIds,&type,reinterpret_cast<PBYTE>(compatibleIds),&bytes,0)!=CR_SUCCESS ||
                type!=DEVPROP_TYPE_STRING_LIST || !HasGipCompatibleId(compatibleIds))continue;
        }
        wchar_t location[2048]{};ULONG bytes=sizeof(location);DEVPROPTYPE type{};
        if(CM_Get_DevNode_PropertyW(node,&DEVPKEY_Device_LocationPaths,&type,reinterpret_cast<PBYTE>(location),&bytes,0)!=CR_SUCCESS || type!=DEVPROP_TYPE_STRING_LIST || !location[0])continue;
        result.push_back({id,location,xboxMode?XboxModeProduct:OriginalGipProduct});
    }
    return result;
}
}
