// Public GameInput v3 adapter. No Nexus binaries, USB takeover or GIP auth replay.
#define NOMINMAX
#include <windows.h>
#include <cfgmgr32.h>
#include <initguid.h>
#include <wrl/client.h>
#include <GameInput.h>
#include "GameInputOpen.h"
#include "VendorGip.h"
#include "GipDevices.h"
#if GAMEINPUT_API_VERSION != 3
#error G7Bridge requires the vendored GameInput v3 header.
#endif
#include <array>
#include <deque>
#include <mutex>
#include <string>
#include <vector>
#include <algorithm>
#include <memory>

using namespace GameInput::v3;
using Microsoft::WRL::ComPtr;
#define API extern "C" __declspec(dllexport)
static_assert(sizeof(APP_LOCAL_DEVICE_ID)==32);
static constexpr wchar_t HexDigits[]=L"0123456789ABCDEF";

struct G7Info {
    uint16_t vendor, product;
    uint32_t family, kinds;
    wchar_t key[512], id[65], name[128], reports[512];
};
static_assert(sizeof(G7Info)==2448);
struct G7Pad { uint32_t buttons; float lt, rt, lx, ly, rx, ry; };
struct Frame { uint32_t id, size; uint64_t time; std::array<uint8_t,64> data{}; };
struct Context {
    ComPtr<IGameInput> api;
    ComPtr<IGameInputDevice> device;
    GameInputCallbackToken token{};
    std::mutex mutex;
    std::deque<Frame> frames;
    uint64_t dropped{};
    uint16_t product{};
    std::shared_ptr<G7Native::VendorChannel> vendor;
    HRESULT vendorResult=S_OK;
    bool vendorPrepared=false;
    bool direct=false;
    std::wstring directInstance;
    uint64_t nextDirectPad{};
    ~Context() { if(token) { api->StopCallback(token); api->UnregisterCallback(token); } }
};

static bool Supported(const GameInputDeviceInfo* d) {
    return d && d->vendorId==0x3537 &&
        (d->productId==0x100A || d->productId==0x106B || d->productId==0x109B || d->productId==0x109C) &&
        (d->deviceFamily==GameInputFamilyXboxOne || d->deviceFamily==GameInputFamilyXbox360);
}
static std::wstring Wide(const char* s) {
    if(!s || !*s) return {};
    int n=MultiByteToWideChar(CP_UTF8,0,s,-1,nullptr,0);
    if(n<=1) return {};
    std::wstring result(static_cast<size_t>(n),L'\0');
    MultiByteToWideChar(CP_UTF8,0,s,-1,result.data(),n);
    result.pop_back();return result;
}
static std::wstring IdString(const APP_LOCAL_DEVICE_ID& id) {
    std::wstring result;result.reserve(sizeof(id)*2);
    auto bytes=reinterpret_cast<const uint8_t*>(&id);
    for(size_t i=0;i<sizeof(id);++i) { result+=L"0123456789ABCDEF"[bytes[i]>>4];result+=L"0123456789ABCDEF"[bytes[i]&15]; }
    return result;
}
static std::wstring PhysicalKey(const char* path) {
    auto p=Wide(path);if(p.empty()) return {};
    // Locate the USB ancestor and use its topology, which survives a PID change.
    wchar_t instance[1024]{};ULONG size=sizeof(instance);DEVPROPTYPE type{};
    if(CM_Get_Device_Interface_PropertyW(p.c_str(),&DEVPKEY_Device_InstanceId,&type,
        reinterpret_cast<PBYTE>(instance),&size,0)!=CR_SUCCESS) {
        // Some providers expose the instance path itself rather than its interface.
        if(p.size()>=1024)return {};wcscpy_s(instance,p.c_str());
    }
    DEVINST node{};
    if(CM_Locate_DevNodeW(&node,instance,CM_LOCATE_DEVNODE_NORMAL)!=CR_SUCCESS) return {};
    for(int i=0;i<12;++i) {
        wchar_t id[1024]{};
        if(CM_Get_Device_IDW(node,id,1024,0)==CR_SUCCESS &&
            _wcsnicmp(id,L"USB\\VID_3537",12)==0 && !wcsstr(id,L"&MI_")) {
            wchar_t locations[2048]{};size=sizeof(locations);
            if(CM_Get_DevNode_PropertyW(node,&DEVPKEY_Device_LocationPaths,&type,
                reinterpret_cast<PBYTE>(locations),&size,0)==CR_SUCCESS &&
                type==DEVPROP_TYPE_STRING_LIST && wcslen(locations)<512) return locations;
        }
        DEVINST parent{};if(CM_Get_Parent(&parent,node,0)!=CR_SUCCESS)break;node=parent;
    }
    return {};
}
static void Info(const GameInputDeviceInfo* d,G7Info& out) {
    out={};out.vendor=d->vendorId;out.product=d->productId;
    out.family=static_cast<uint32_t>(d->deviceFamily);out.kinds=static_cast<uint32_t>(d->supportedInput);
    auto key=PhysicalKey(d->pnpPath),id=IdString(d->deviceId),name=Wide(d->displayName);
    wcscpy_s(out.key,key.c_str());wcscpy_s(out.id,id.c_str());wcsncpy_s(out.name,name.c_str(),_TRUNCATE);
    std::wstring reports;
    for(uint32_t i=0;i<d->inputReportCount;++i) {
        wchar_t item[48];swprintf_s(item,L"IN %02X:%u ",d->inputReportInfo[i].id,d->inputReportInfo[i].size);reports+=item;
    }
    for(uint32_t i=0;i<d->outputReportCount;++i) {
        wchar_t item[48];swprintf_s(item,L"OUT %02X:%u ",d->outputReportInfo[i].id,d->outputReportInfo[i].size);reports+=item;
    }
    wcsncpy_s(out.reports,reports.c_str(),_TRUNCATE);
}
static HRESULT Create(std::unique_ptr<Context>& out) {
    out=std::make_unique<Context>();
    HRESULT hr=GameInputCreate(&out->api);if(FAILED(hr))return hr;
    out->api->SetFocusPolicy(GameInputEnableBackgroundInput|GameInputEnableBackgroundGuideButton|GameInputEnableBackgroundShareButton);
    return S_OK;
}
struct Inventory { std::mutex mutex;std::vector<G7Info> items; };
static void CALLBACK OnDevice(GameInputCallbackToken,void* opaque,IGameInputDevice* device,
    uint64_t,GameInputDeviceStatus status,GameInputDeviceStatus) noexcept {
    try {
        if(!(status&GameInputDeviceConnected))return;
        const GameInputDeviceInfo* d{};if(FAILED(device->GetDeviceInfo(&d)) || !Supported(d))return;
        G7Info info{};Info(d,info);auto& list=*static_cast<Inventory*>(opaque);std::lock_guard lock(list.mutex);
        if(list.items.size()<16 && std::none_of(list.items.begin(),list.items.end(),[&](auto& x){return wcscmp(x.id,info.id)==0;}))list.items.push_back(info);
    } catch(...) { /* Enumeration remains bounded; no exception crosses COM. */ }
}
API HRESULT __cdecl g7_enumerate(G7Info* items,uint32_t capacity,uint32_t* count) noexcept {
    if(!items || !count || capacity>16)return E_INVALIDARG;*count=0;
    try {
        std::unique_ptr<Context> context;HRESULT hr=Create(context);if(FAILED(hr))return hr;
        Inventory inventory;GameInputCallbackToken token{};
        hr=context->api->RegisterDeviceCallback(nullptr,GameInputKindGamepad,GameInputDeviceConnected,
            GameInputBlockingEnumeration,&inventory,OnDevice,&token);
        if(FAILED(hr))return hr;
        context->api->StopCallback(token);context->api->UnregisterCallback(token);
        std::lock_guard lock(inventory.mutex);
        // GIP discovery must not depend on Microsoft's synthesized XInput HID
        // child, because HidHide intentionally hides that child from games.
        auto direct=G7Native::ListGipDevices();
        if(!direct.empty()) {
            std::erase_if(inventory.items,[](auto const& x){return x.product==0x106B;});
            for(auto const& root:direct) {
                if(root.instance.size()+4>=65 || root.location.size()>=512)continue;
                G7Info item{};item.vendor=0x3537;item.product=0x106B;item.family=1;item.kinds=GameInputKindRawDeviceReport|GameInputKindGamepad;
                wcscpy_s(item.id,(L"GIP:"+root.instance).c_str());wcscpy_s(item.key,root.location.c_str());
                wcscpy_s(item.name,L"GameSir G7 Pro directe GIP");wcscpy_s(item.reports,L"GIP IN 10:60 IN 20:32 OUT 0F:1/2");inventory.items.push_back(item);
            }
        }
        *count=static_cast<uint32_t>(std::min<size_t>(capacity,inventory.items.size()));
        std::copy_n(inventory.items.data(),*count,items);return S_OK;
    } catch(...) {return E_OUTOFMEMORY;}
}
static void CALLBACK OnReading(GameInputCallbackToken,void* opaque,IGameInputReading* reading) noexcept {
    try {
        ComPtr<IGameInputRawDeviceReport> report;if(!reading->GetRawReport(&report))return;
        GameInputRawDeviceReportInfo info{};report->GetReportInfo(&info);
        const auto size=report->GetRawDataSize();if(size==0 || size>64)return;
        Frame frame{};frame.id=info.id;frame.size=static_cast<uint32_t>(size);frame.time=reading->GetTimestamp();
        if(report->GetRawData(frame.size,frame.data.data())!=size)return;
        auto& context=*static_cast<Context*>(opaque);std::lock_guard lock(context.mutex);
        if(context.frames.size()>=256){context.frames.pop_front();++context.dropped;}
        context.frames.push_back(frame);
    } catch(...) { }
}
API HRESULT __cdecl g7_open_v2(const wchar_t* idText,void** handle,G7Info* info,uint32_t* stage) noexcept {
    if(!stage)return E_INVALIDARG;*stage=0;
    if(!idText || !handle || !info)return E_INVALIDARG;
    *handle=nullptr;
    try {
        if(wcsncmp(idText,L"GIP:",4)==0) {
            const auto roots=G7Native::ListGipDevices();
            if(roots.size()!=1 || roots.front().instance!=idText+4 || roots.front().location.size()>=512)return HRESULT_FROM_WIN32(ERROR_DUP_NAME);
            auto context=std::make_unique<Context>();context->direct=true;context->product=0x106B;context->directInstance=roots.front().instance;
            *stage=10;HRESULT hr=G7Native::OpenDirectVendorChannel(context->directInstance.c_str(),context->vendor);if(FAILED(hr))return hr;
            context->vendorPrepared=true;*info={};info->vendor=0x3537;info->product=0x106B;info->family=1;info->kinds=GameInputKindRawDeviceReport|GameInputKindGamepad;
            wcscpy_s(info->key,roots.front().location.c_str());wcscpy_s(info->id,idText);wcscpy_s(info->name,L"GameSir G7 Pro directe GIP");wcscpy_s(info->reports,L"GIP IN 10:60 IN 20:32 OUT 0F:1/2");
            *stage=0;*handle=context.release();return S_OK;
        }
        if(wcslen(idText)!=sizeof(APP_LOCAL_DEVICE_ID)*2)return E_INVALIDARG;
        APP_LOCAL_DEVICE_ID id{};auto bytes=reinterpret_cast<uint8_t*>(&id);
        for(size_t i=0;i<sizeof(id)*2;++i){const wchar_t* digit=wcschr(HexDigits,idText[i]);if(!digit)return E_INVALIDARG;
            bytes[i/2]=static_cast<uint8_t>((bytes[i/2]<<4)|(digit-HexDigits));}
        *stage=1;std::unique_ptr<Context> context;HRESULT hr=Create(context);if(FAILED(hr))return hr;
        *stage=2;hr=G7Native::FindEnumeratedDevice(context->api.Get(),id,&context->device);if(FAILED(hr))return hr;
        *stage=3;const GameInputDeviceInfo* d{};hr=context->device->GetDeviceInfo(&d);if(FAILED(hr))return hr;
        if(!Supported(d))return E_ACCESSDENIED;context->product=d->productId;Info(d,*info);
        if(d->supportedInput&GameInputKindRawDeviceReport) {
            *stage=4;
            hr=context->api->RegisterReadingCallback(context->device.Get(),GameInputKindRawDeviceReport,context.get(),OnReading,&context->token);
            if(FAILED(hr))return hr;
        }
        *stage=0;*handle=context.release();return S_OK;
    } catch(...) {return E_OUTOFMEMORY;}
}
API void __cdecl g7_close(void* handle) noexcept {delete static_cast<Context*>(handle);}
API HRESULT __cdecl g7_rumble(void* handle,uint16_t left,uint16_t right) noexcept {
    if(!handle)return E_INVALIDARG;auto& c=*static_cast<Context*>(handle);
    if(c.product!=0x106B || !c.vendor)return HRESULT_FROM_WIN32(ERROR_NOT_SUPPORTED);
    return G7Native::SetVendorRumble(c.vendor,left/65535.0,right/65535.0);
}
API HRESULT __cdecl g7_device_instance(void* handle,wchar_t* instance,uint32_t capacity) noexcept {
    if(!handle || !instance || capacity<2)return E_INVALIDARG;instance[0]=0;
    try {
        auto& c=*static_cast<Context*>(handle);
        if(c.direct){if(c.directInstance.size()>=capacity)return E_INVALIDARG;wcscpy_s(instance,capacity,c.directInstance.c_str());return S_OK;}
        const GameInputDeviceInfo* info{};
        HRESULT hr=c.device->GetDeviceInfo(&info);if(FAILED(hr))return hr;
        auto path=Wide(info->pnpPath);if(path.empty())return HRESULT_FROM_WIN32(ERROR_NOT_FOUND);
        wchar_t id[1024]{};ULONG bytes=sizeof(id);DEVPROPTYPE type{};
        if(CM_Get_Device_Interface_PropertyW(path.c_str(),&DEVPKEY_Device_InstanceId,&type,reinterpret_cast<PBYTE>(id),&bytes,0)!=CR_SUCCESS) {
            DEVINST node{};if(CM_Locate_DevNodeW(&node,path.data(),CM_LOCATE_DEVNODE_NORMAL)!=CR_SUCCESS)return HRESULT_FROM_WIN32(ERROR_NOT_FOUND);
            if(CM_Get_Device_IDW(node,id,1024,0)!=CR_SUCCESS)return HRESULT_FROM_WIN32(ERROR_NOT_FOUND);
        }
        // Xbox creates a Microsoft 045E:02FF HID child. HidHide must target
        // the verified GameSir XboxComposite ancestor, not a generic model match.
        DEVINST node{};if(CM_Locate_DevNodeW(&node,id,CM_LOCATE_DEVNODE_NORMAL)!=CR_SUCCESS)return HRESULT_FROM_WIN32(ERROR_NOT_FOUND);
        wchar_t prefix[40]{};swprintf_s(prefix,L"USB\\VID_%04X&PID_%04X",info->vendorId,info->productId);
        for(int depth=0;depth<12;++depth) {
            if(CM_Get_Device_IDW(node,id,1024,0)==CR_SUCCESS && _wcsnicmp(id,prefix,wcslen(prefix))==0 &&
                (id[wcslen(prefix)]==L'\\' || id[wcslen(prefix)]==L'&')) {
                if(wcslen(id)>=capacity)return HRESULT_FROM_WIN32(ERROR_INSUFFICIENT_BUFFER);
                wcscpy_s(instance,capacity,id);return S_OK;
            }
            DEVINST parent{};if(CM_Get_Parent(&parent,node,0)!=CR_SUCCESS)break;node=parent;
        }
        return HRESULT_FROM_WIN32(ERROR_NOT_FOUND);
    } catch(...) {return E_OUTOFMEMORY;}
}
API HRESULT __cdecl g7_prepare_vendor(void* handle) noexcept {
    if(!handle)return E_INVALIDARG;auto& c=*static_cast<Context*>(handle);
    if(c.vendorPrepared)return c.vendorResult;
    c.vendorPrepared=true;
    const GameInputDeviceInfo* info{};HRESULT hr=c.device->GetDeviceInfo(&info);
    if(FAILED(hr))return c.vendorResult=hr;
    for(uint32_t i=0;i<info->outputReportCount;++i)if(info->outputReportInfo[i].id==0x0F)return S_FALSE;
    if(info->vendorId!=0x3537 || info->productId!=0x106B || info->deviceFamily!=GameInputFamilyXboxOne)
        return c.vendorResult=HRESULT_FROM_WIN32(ERROR_NOT_SUPPORTED);
    return c.vendorResult=G7Native::OpenVendorChannel(c.api.Get(),c.device.Get(),c.vendor);
}
API void __cdecl g7_vendor_status(void* handle,wchar_t* text,uint32_t capacity) noexcept {
    if(!handle || !text || !capacity)return;
    G7Native::DescribeVendor(static_cast<Context*>(handle)->vendor,text,capacity);
}
API HRESULT __cdecl g7_read(void* handle,uint8_t* data,uint32_t capacity,uint32_t* id,uint32_t* size,uint64_t* time,uint64_t* dropped) noexcept {
    if(!handle || !data || !id || !size || !time || !dropped)return E_INVALIDARG;
    auto& c=*static_cast<Context*>(handle);*size=0;
    if(c.direct) {
        if(!G7Native::IsVendorConnected(c.vendor))return HRESULT_FROM_WIN32(ERROR_DEVICE_NOT_CONNECTED);
        auto now=GetTickCount64();*time=now*1000;*dropped=0;
        if(now>=c.nextDirectPad && G7Native::ReadVendorPad(c.vendor,data,capacity)) {
            c.nextDirectPad=now+8;*id=0x20;*size=32;return S_OK;
        }
        if(G7Native::ReadVendorTelemetry(c.vendor,data,capacity,*dropped)){*id=0x10;*size=60;return S_OK;}
        return S_FALSE;
    }
    if(!(c.device->GetDeviceStatus()&GameInputDeviceConnected))return HRESULT_FROM_WIN32(ERROR_DEVICE_NOT_CONNECTED);
    uint64_t vendorDropped=0;
    const bool vendorReceived=G7Native::ReadVendorTelemetry(c.vendor,data,capacity,vendorDropped);
    std::lock_guard lock(c.mutex);
    if(vendorReceived) {
        *id=0x10;*size=60;*time=c.api->GetCurrentTimestamp();*dropped=c.dropped+vendorDropped;return S_OK;
    }
    const auto now=c.api->GetCurrentTimestamp();
    while(!c.frames.empty() && (c.frames.front().time>now || now-c.frames.front().time>150000)) {c.frames.pop_front();++c.dropped;}
    *dropped=c.dropped+vendorDropped;
    if(c.frames.empty())return S_FALSE;
    const auto& f=c.frames.front();if(capacity<f.size)return E_INVALIDARG;
    *id=f.id;*size=f.size;*time=f.time;std::copy_n(f.data.data(),f.size,data);c.frames.pop_front();return S_OK;
}
API HRESULT __cdecl g7_pad(void* handle,G7Pad* pad) noexcept {
    if(!handle || !pad)return E_INVALIDARG;auto& c=*static_cast<Context*>(handle);
    if(c.direct) {
        uint32_t buttons{};float values[6]{};
        if(!G7Native::ReadVendorGamepad(c.vendor,buttons,values))return S_FALSE;
        *pad={buttons,values[0],values[1],values[2],values[3],values[4],values[5]};return S_OK;
    }
    if(!(c.device->GetDeviceStatus()&GameInputDeviceConnected))return HRESULT_FROM_WIN32(ERROR_DEVICE_NOT_CONNECTED);
    ComPtr<IGameInputReading> reading;HRESULT hr=c.api->GetCurrentReading(GameInputKindGamepad,c.device.Get(),&reading);
    if(FAILED(hr))return S_FALSE;
    GameInputGamepadState state{};if(!reading->GetGamepadState(&state))return S_FALSE;
    *pad={static_cast<uint32_t>(state.buttons),state.leftTrigger,state.rightTrigger,state.leftThumbstickX,state.leftThumbstickY,state.rightThumbstickX,state.rightThumbstickY};return S_OK;
}
API HRESULT __cdecl g7_send_session(void* handle,const uint8_t* data,uint32_t length,uint32_t* stage,uint32_t* capacity) noexcept {
    if(!stage || !capacity)return E_INVALIDARG;*stage=0;*capacity=0;
    if(!handle || !data || (length!=1 && length!=2) || data[0]!=0xF2 || (length==2 && data[1]!=0))return E_INVALIDARG;
    auto& c=*static_cast<Context*>(handle);if(c.product==0x100A)return E_ACCESSDENIED;
    if(c.vendorPrepared && FAILED(c.vendorResult)){*stage=10;return c.vendorResult;}
    if(c.vendor){*stage=11;auto result=G7Native::SendVendorSession(c.vendor,data,length);if(SUCCEEDED(result))*stage=0;return result;}
    ComPtr<IGameInputRawDeviceReport> report;
    *stage=1;HRESULT hr=c.device->CreateRawDeviceReport(0x0F,GameInputRawOutputReport,&report);if(FAILED(hr))return hr;
    *capacity=static_cast<uint32_t>(report->GetRawDataSize());*stage=2;
    if(!report->SetRawData(length,data))return HRESULT_FROM_WIN32(ERROR_INVALID_DATA);
    *stage=3;hr=c.device->SendRawDeviceOutput(report.Get());if(SUCCEEDED(hr))*stage=0;return hr;
}
API HRESULT __cdecl g7_mode_piece(void* handle,uint8_t left,uint8_t right) noexcept {
    if(!handle)return E_INVALIDARG;auto& c=*static_cast<Context*>(handle);if(c.product!=0x100A)return E_ACCESSDENIED;
    // Only the captured gamesirapp pairs and their neutral flush can be requested.
    if(!((left==0 && right==0)||(left=='g' && right=='a')||(left=='m' && right=='e')||
        (left=='s' && right=='i')||(left=='r' && right=='a')||(left=='p' && right=='p')))return E_INVALIDARG;
    GameInputRumbleParams rumble{};rumble.lowFrequency=left/255.0f;rumble.highFrequency=right/255.0f;
    c.device->SetRumbleState(&rumble);return S_OK;
}
