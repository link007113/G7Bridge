// Independent WinRT controller component for the captured G7 Pro GIP channel.
// Windows owns the driver/authentication. Only F2 and F2/00 may be transmitted.
#define NOMINMAX
#include <windows.h>
#include <roapi.h>
#include <wrl/client.h>
#include <winrt/Windows.Foundation.h>
#include <winrt/Windows.Gaming.Input.h>
#include <winrt/Windows.Gaming.Input.Custom.h>
#include <winrt/Windows.System.h>
#include "VendorGip.h"
#include "VendorProtocol.h"
#include "VendorRegistration.h"
#include "GameInputOpen.h"
#include "GipAggregation.h"
#include "GipDevices.h"
#include <array>
#include <deque>
#include <mutex>
#include <set>
#include <vector>
#include <string>
#include <algorithm>
#include <atomic>

namespace G7Native {
using namespace GameInput::v3;
namespace Wgi=winrt::Windows::Gaming::Input;
namespace Custom=winrt::Windows::Gaming::Input::Custom;
using Microsoft::WRL::ComPtr;

struct Apartment {
    HRESULT result=RoInitialize(RO_INIT_MULTITHREADED);
    Apartment(){if(FAILED(result))winrt::throw_hresult(result);}
    ~Apartment(){RoUninitialize();}
};
struct MtaLifetime {
    CO_MTA_USAGE_COOKIE cookie{};
    MtaLifetime(){winrt::check_hresult(CoIncrementMTAUsage(&cookie));}
    ~MtaLifetime(){if(cookie)CoDecrementMTAUsage(cookie);}
    MtaLifetime(MtaLifetime const&)=delete;
    MtaLifetime& operator=(MtaLifetime const&)=delete;
};
struct Sample {uint64_t received;std::array<uint8_t,60> payload;};
struct Inbox {
    std::mutex mutex;
    std::deque<Sample> samples;
    std::array<uint8_t,32> pad{};
    bool hasPad=false;
    bool enabled=true,suspended=false,activityKnown=false,seenTelemetry=false;
    uint32_t resumes=0,suspends=0,lastClass=0,lastId=0,lastLength=0;
    uint64_t dropped=0;
    std::wstring note=L"GIP provider not attached yet";
};

struct Sink : winrt::implements<Sink,Custom::IGameControllerInputSink,Custom::IGipGameControllerInputSink,winrt::composable> {
    Custom::IGameControllerProvider identity;
    Custom::GipGameControllerProvider provider;
    winrt::com_ptr<::IUnknown> providerIdentity;
    std::atomic<bool> removed=false;
    mutable std::mutex queryMutex;
    mutable std::vector<winrt::guid> missingInterfaces;
    std::mutex mutex;
    std::shared_ptr<Inbox> inbox;
    bool suspended=false,activityKnown=false;
    Sink(Custom::IGameControllerProvider const& source,Custom::GipGameControllerProvider const& gip)
        :identity(source),provider(gip),providerIdentity(source.as<::IUnknown>()){}
    winrt::hstring GetRuntimeClassName() const {return L"G7Bridge.Native.G7Controller";}
    int32_t query_interface_tearoff(winrt::guid const& id,void** object) const noexcept override {
        *object=nullptr;
        try {std::lock_guard lock(queryMutex);if(missingInterfaces.size()<6 && std::find(missingInterfaces.begin(),missingInterfaces.end(),id)==missingInterfaces.end())missingInterfaces.push_back(id);}catch(...){}
        return E_NOINTERFACE;
    }
    std::wstring MissingInterfaces() const {
        std::lock_guard lock(queryMutex);std::wstring text;
        for(auto const& id:missingInterfaces){text+=winrt::to_hstring(id);text+=L" ";}return text;
    }
    // IGameController is supplied by the controlling Windows object. The SDK's
    // composable support delegates identity/reference counting to that object.
    bool Connected() const {return !removed.load() && identity.IsConnected();}
    void Invalidate() {
        removed.store(true);
        std::shared_ptr<Inbox> target;
        {std::lock_guard lock(mutex);target=std::move(inbox);}
        if(target){std::lock_guard lock(target->mutex);target->enabled=false;target->samples.clear();target->hasPad=false;target->note=L"GIP provider detached";}
    }
    void Attach(std::shared_ptr<Inbox> value) {
        std::lock_guard lock(mutex);inbox=std::move(value);
        if(inbox){std::lock_guard state(inbox->mutex);inbox->suspended=suspended;inbox->activityKnown=activityKnown;}
    }
    void OnInputResumed(uint64_t) {
        std::lock_guard lock(mutex);suspended=false;activityKnown=true;
        if(inbox){std::lock_guard state(inbox->mutex);inbox->activityKnown=true;inbox->suspended=false;++inbox->resumes;}
    }
    void OnInputSuspended(uint64_t) {
        std::lock_guard lock(mutex);suspended=true;activityKnown=true;
        if(inbox){std::lock_guard state(inbox->mutex);inbox->activityKnown=true;inbox->suspended=true;++inbox->suspends;inbox->samples.clear();inbox->hasPad=false;}
    }
    void OnKeyReceived(uint64_t,uint8_t,bool) { }
    void OnMessageReceived(uint64_t,Custom::GipMessageClass const& messageClass,uint8_t id,uint8_t,
        winrt::array_view<uint8_t const> data) {
        std::shared_ptr<Inbox> target;{std::lock_guard lock(mutex);target=inbox;}
        if(!target)return;
        std::lock_guard lock(target->mutex);if(!target->enabled)return;
        target->lastClass=static_cast<uint32_t>(messageClass);target->lastId=id;target->lastLength=data.size();
        if(!target->suspended && target->lastClass==1 && id==0 && data.size()==32) {
            std::copy(data.begin(),data.end(),target->pad.begin());target->hasPad=true;return;
        }
        if(target->suspended || !IsVendorTelemetry(target->lastClass,id,{data.data(),data.size()}))return;
        Sample sample{};sample.received=GetTickCount64();std::copy(data.begin(),data.end(),sample.payload.begin());
        if(target->samples.size()>=128){target->samples.pop_front();++target->dropped;}
        target->activityKnown=true;target->seenTelemetry=true;target->samples.push_back(sample);
    }
};

struct Factory : winrt::implements<Factory,Custom::ICustomGameControllerFactory> {
    std::mutex mutex;
    // The factory owns each connected controller. Windows may release the
    // object returned by CreateGameController immediately after that callback.
    std::vector<winrt::com_ptr<Sink>> sinks;
    HRESULT lastError=S_OK;
    uint32_t calls=0,ignored=0,created=0,connected=0,aggregated=0;
    uint16_t lastVendor=0,lastProduct=0;
    bool registered=false;
    winrt::Windows::Foundation::IInspectable CreateGameController(Custom::IGameControllerProvider const& provider) {
        try {
            {std::lock_guard lock(mutex);++calls;}
            const auto vendor=provider.HardwareVendorId(),product=provider.HardwareProductId();
            {std::lock_guard lock(mutex);lastVendor=vendor;lastProduct=product;}
            // This supplementary route is enabled only for Anthony's observed PID.
            if(vendor!=0x3537 || product!=0x106B){std::lock_guard lock(mutex);++ignored;return nullptr;}
            auto gip=provider.try_as<Custom::GipGameControllerProvider>();
            if(!gip){std::lock_guard lock(mutex);lastError=E_NOINTERFACE;return nullptr;}
            auto weak=get_weak();
            auto shell=new AggregationShell([weak,provider,gip](auto const& outer) {
                winrt::Windows::Foundation::IInspectable inner;
                auto receiver=winrt::impl::composable_factory<Sink>::CreateInstance<Custom::IGameControllerInputSink>(outer,inner,provider,gip);
                auto sink=winrt::get_self<Sink>(receiver);
                if(auto factory=weak.get())factory->Retain(sink->get_strong());
                else winrt::throw_hresult(RO_E_CLOSED);
                return inner;
            });
            {std::lock_guard lock(mutex);++created;lastError=S_OK;}
            return {static_cast<IGipAggregation*>(shell),winrt::take_ownership_from_abi};
        } catch(...) {std::lock_guard lock(mutex);lastError=winrt::to_hresult();return nullptr;}
    }
    void OnGameControllerAdded(Wgi::IGameController const&) { }
    void Retain(winrt::com_ptr<Sink> sink) {
        (void)Connected();
        winrt::com_ptr<Sink> replaced;
        {std::lock_guard lock(mutex);
            auto found=std::find_if(sinks.begin(),sinks.end(),[&](auto& entry){return entry->providerIdentity.get()==sink->providerIdentity.get();});
            if(found!=sinks.end()){replaced=*found;*found=std::move(sink);}
            else {
                if(sinks.size()>=16)winrt::throw_hresult(HRESULT_FROM_WIN32(ERROR_TOO_MANY_NAMES));
                sinks.push_back(std::move(sink));
            }
            ++aggregated;
        }
        if(replaced)replaced->Invalidate();
    }
    void OnGameControllerRemoved(Wgi::IGameController const& value) {
        if(!value)return;
        auto identity=value.as<::IUnknown>();winrt::com_ptr<Sink> removedSink;
        {std::lock_guard lock(mutex);
            auto found=std::find_if(sinks.begin(),sinks.end(),[&](auto& sink){return sink.template as<::IUnknown>().get()==identity.get();});
            if(found!=sinks.end()){removedSink=*found;sinks.erase(found);connected=std::min(connected,static_cast<uint32_t>(sinks.size()));}
        }
        if(removedSink)removedSink->Invalidate();
    }
    HRESULT LastError(){std::lock_guard lock(mutex);return lastError;}
    void Registered(){std::lock_guard lock(mutex);registered=true;}
    std::wstring Describe() {
        std::lock_guard lock(mutex);const auto alive=static_cast<uint32_t>(sinks.size());
        wchar_t text[384]{};
        swprintf_s(text,L"GIP registration=%s; factory calls=%u; outside selection=%u; created=%u; aggregations=%u; alive=%u; connected=%u; last model=%04X:%04X; factory error=%08X",
            registered?L"ready":L"not ready",calls,ignored,created,aggregated,alive,connected,lastVendor,lastProduct,static_cast<uint32_t>(lastError));
        std::wstring result=text;
        for(auto& sink:sinks)result+=L"; unknown requested interfaces="+sink->MissingInterfaces();
        return result;
    }
    std::vector<winrt::com_ptr<Sink>> Connected() {
        std::vector<winrt::com_ptr<Sink>> result;
        std::vector<winrt::com_ptr<Sink>> snapshot;
        std::vector<winrt::com_ptr<Sink>> disconnected;
        {std::lock_guard lock(mutex);snapshot=sinks;}
        for(auto& sink:snapshot) {
            if(sink->Connected())result.push_back(sink);
            else {sink->Invalidate();disconnected.push_back(sink);}
        }
        {std::lock_guard lock(mutex);
            std::erase_if(sinks,[&](auto& entry){return std::any_of(disconnected.begin(),disconnected.end(),[&](auto& old){return old.get()==entry.get();});});
            connected=static_cast<uint32_t>(result.size());
        }
        return result;
    }
};

static winrt::com_ptr<Factory> GetFactory() {
    // Windows has no unregister-factory API. Registration is process-local and
    // lasts until exit; sinks collect no data unless explicitly attached below.
    static std::mutex mutex;
    static winrt::com_ptr<Factory> factory;
    std::lock_guard lock(mutex);
    if(!factory) {
        auto candidate=winrt::make_self<Factory>();
        RegisterG7Factory<Custom::GameControllerFactoryManager>(candidate.as<Custom::ICustomGameControllerFactory>());
        candidate->Registered();
        factory=std::move(candidate);
    }
    return factory;
}

struct ScopeSnapshot {
    std::mutex mutex;
    std::set<std::array<uint8_t,32>> roots;
    APP_LOCAL_DEVICE_ID selected{};
    bool present=false;
    uint32_t callbacks=0,matching=0;
    HRESULT error=S_OK;
};
static void CALLBACK ScopedDevice(GameInputCallbackToken,void* opaque,IGameInputDevice* device,
    uint64_t,GameInputDeviceStatus status,GameInputDeviceStatus) noexcept {
    auto& scope=*static_cast<ScopeSnapshot*>(opaque);
    try {
        {std::lock_guard lock(scope.mutex);++scope.callbacks;}
        if(!(status&GameInputDeviceConnected))return;
        const GameInputDeviceInfo* info{};if(FAILED(device->GetDeviceInfo(&info)) || !info)return;
        if(info->vendorId!=0x3537 || info->productId!=0x106B)return;
        {std::lock_guard lock(scope.mutex);++scope.matching;}
        std::array<uint8_t,32> root{};memcpy(root.data(),&info->deviceRootId,root.size());
        std::lock_guard lock(scope.mutex);
        if(std::all_of(root.begin(),root.end(),[](auto b){return b==0;})){scope.error=E_UNEXPECTED;return;}
        scope.roots.insert(root);
        scope.present|=memcmp(&scope.selected,&info->deviceId,sizeof(info->deviceId))==0;
    } catch(...) {std::lock_guard lock(scope.mutex);scope.error=E_OUTOFMEMORY;}
}
static HRESULT CheckScope(IGameInput* api,IGameInputDevice* device,uint32_t providers,std::wstring& details) {
    if(!(device->GetDeviceStatus()&GameInputDeviceConnected))return GAMEINPUT_E_DEVICE_DISCONNECTED;
    const GameInputDeviceInfo* info{};HRESULT hr=device->GetDeviceInfo(&info);if(FAILED(hr))return hr;
    if(info->vendorId!=0x3537 || info->productId!=0x106B || info->deviceFamily!=GameInputFamilyXboxOne)return E_ACCESSDENIED;
    ScopeSnapshot scope;scope.selected=info->deviceId;GameInputCallbackToken token{};
    hr=RegisterControllerInventory(api,&scope,ScopedDevice,&token);
    if(FAILED(hr))return hr;
    api->StopCallback(token);api->UnregisterCallback(token);
    std::lock_guard lock(scope.mutex);if(FAILED(scope.error))return scope.error;
    wchar_t text[256]{};
    swprintf_s(text,L"scope: callbacks=%u; matching=%u; physical roots=%zu; selected ID=%s; GIP providers=%u",
        scope.callbacks,scope.matching,scope.roots.size(),scope.present?L"found":L"missing",providers);
    details=text;
    return CanBindVendor(static_cast<uint32_t>(scope.roots.size()),scope.present,providers)?S_OK:HRESULT_FROM_WIN32(ERROR_DUP_NAME);
}

struct VendorChannel {
    // Keep callback/RPC support alive between short native calls on pool threads.
    // Declared first so the interfaces below are released before this lease.
    MtaLifetime lifetime;
    ComPtr<IGameInput> api;
    ComPtr<IGameInputDevice> device;
    winrt::com_ptr<Factory> factory;
    winrt::com_ptr<Sink> sink;
    std::shared_ptr<Inbox> inbox=std::make_shared<Inbox>();
    ~VendorChannel() {
        if(sink)sink->Attach(nullptr);
        std::lock_guard lock(inbox->mutex);inbox->enabled=false;inbox->samples.clear();
    }
};

HRESULT OpenDirectVendorChannel(const wchar_t* instance,std::shared_ptr<VendorChannel>& output) noexcept {
    try {
        auto roots=ListGipDevices();
        if(!instance || roots.size()!=1 || _wcsicmp(roots.front().instance.c_str(),instance)!=0)return HRESULT_FROM_WIN32(ERROR_DUP_NAME);
        output=std::make_shared<VendorChannel>();auto& channel=*output;
        Apartment apartment;channel.factory=GetFactory();
        const auto deadline=GetTickCount64()+3000;
        std::vector<winrt::com_ptr<Sink>> providers;
        do {providers=channel.factory->Connected();if(!providers.empty())break;Sleep(20);}while(GetTickCount64()<deadline);
        if(providers.size()!=1)return HRESULT_FROM_WIN32(ERROR_NOT_FOUND);
        channel.sink=providers.front();channel.sink->Attach(channel.inbox);
        {std::lock_guard lock(channel.inbox->mutex);channel.inbox->note=L"Direct GIP input: one verified GameSir USB root and one provider; independent buttons, sticks and gyro";}
        return S_OK;
    } catch(...) {return winrt::to_hresult();}
}
bool IsVendorConnected(const std::shared_ptr<VendorChannel>& channel) noexcept {
    try {Apartment apartment;return channel && channel->sink && channel->sink->Connected();}catch(...){return false;}
}
HRESULT SetVendorRumble(const std::shared_ptr<VendorChannel>& channel,double left,double right) noexcept {
    if(!(left>=0 && left<=1 && right>=0 && right<=1) || !channel || !channel->sink)return E_INVALIDARG;
    try {
        Apartment apartment;
        if(!channel->sink->Connected())return GAMEINPUT_E_DEVICE_DISCONNECTED;
        auto pad=Wgi::Gamepad::FromGameController(channel->sink.as<Wgi::IGameController>());
        if(!pad)return HRESULT_FROM_WIN32(ERROR_NOT_SUPPORTED);
        pad.Vibration(Wgi::GamepadVibration{left,right,0,0});return S_OK;
    }catch(...){return winrt::to_hresult();}
}
bool ReadVendorGamepad(const std::shared_ptr<VendorChannel>& channel,uint32_t& buttons,float* values) noexcept {
    if(!channel || !channel->sink || !values)return false;
    try {
        Apartment apartment;
        if(!channel->sink->Connected())return false;
        auto pad=Wgi::Gamepad::FromGameController(channel->sink.as<Wgi::IGameController>());
        if(!pad)return false;
        auto reading=pad.GetCurrentReading();buttons=static_cast<uint32_t>(reading.Buttons);
        values[0]=static_cast<float>(reading.LeftTrigger);values[1]=static_cast<float>(reading.RightTrigger);
        values[2]=static_cast<float>(reading.LeftThumbstickX);values[3]=static_cast<float>(reading.LeftThumbstickY);
        values[4]=static_cast<float>(reading.RightThumbstickX);values[5]=static_cast<float>(reading.RightThumbstickY);return true;
    }catch(...){return false;}
}
bool ReadVendorPad(const std::shared_ptr<VendorChannel>& channel,uint8_t* data,uint32_t capacity) noexcept {
    if(!channel || !data || capacity<32)return false;
    std::lock_guard lock(channel->inbox->mutex);auto& box=*channel->inbox;
    if(!box.enabled || box.suspended || !box.hasPad)return false;
    std::copy(box.pad.begin(),box.pad.end(),data);return true;
}

HRESULT OpenVendorChannel(IGameInput* api,IGameInputDevice* device,std::shared_ptr<VendorChannel>& output) noexcept {
    try {
        output=std::make_shared<VendorChannel>();auto& channel=*output;channel.api=api;channel.device=device;
        Apartment apartment;
        channel.factory=GetFactory();
        const uint64_t deadline=GetTickCount64()+3000;
        std::vector<winrt::com_ptr<Sink>> providers;
        do {
            providers=channel.factory->Connected();if(!providers.empty())break;
            HRESULT error=channel.factory->LastError();
            if(FAILED(error)){std::lock_guard lock(channel.inbox->mutex);channel.inbox->note=L"Windows provider could not create a GIP input sink";return error;}
            Sleep(20);
        } while(GetTickCount64()<deadline);
        if(providers.empty()) {
            std::lock_guard lock(channel.inbox->mutex);channel.inbox->note=L"No connected provider available after GIP registration";
            return HRESULT_FROM_WIN32(ERROR_NOT_FOUND);
        }
        std::wstring scopeDetails;
        HRESULT hr=CheckScope(api,device,static_cast<uint32_t>(providers.size()),scopeDetails);
        if(FAILED(hr)) {
            std::lock_guard lock(channel.inbox->mutex);channel.inbox->note=L"GIP provider could not be uniquely matched; "+scopeDetails;return hr;
        }
        channel.sink=providers.front();channel.sink->Attach(channel.inbox);
        {std::lock_guard lock(channel.inbox->mutex);channel.inbox->note=L"Custom WinRT GIP component attached; "+scopeDetails;}
        return S_OK;
    } catch(...) {
        if(output){std::lock_guard lock(output->inbox->mutex);output->inbox->note=L"Custom WinRT GIP component could not be opened";}
        return winrt::to_hresult();
    }
}
HRESULT SendVendorSession(const std::shared_ptr<VendorChannel>& channel,const uint8_t* data,uint32_t length) noexcept {
    if(!channel || !channel->sink || !data || !IsSessionPayload({data,length}))return E_INVALIDARG;
    try {
        Apartment apartment;
        if(channel->device && !(channel->device->GetDeviceStatus()&GameInputDeviceConnected))return GAMEINPUT_E_DEVICE_DISCONNECTED;
        auto providers=channel->factory->Connected();
        if(providers.size()!=1 || providers.front().get()!=channel->sink.get())return HRESULT_FROM_WIN32(ERROR_DUP_NAME);
        // Message class A/Command, vendor command 0F. The driver supplies the GIP header.
        channel->sink->provider.SendMessage(Custom::GipMessageClass::Command,0x0F,{data,data+length});
        return S_OK;
    } catch(...) {return winrt::to_hresult();}
}
bool ReadVendorTelemetry(const std::shared_ptr<VendorChannel>& channel,uint8_t* data,uint32_t capacity,uint64_t& dropped) noexcept {
    if(!channel || !data || capacity<60)return false;
    std::lock_guard lock(channel->inbox->mutex);auto& box=*channel->inbox;
    const auto now=GetTickCount64();
    while(!box.samples.empty() && now-box.samples.front().received>150){box.samples.pop_front();++box.dropped;}
    dropped=box.dropped;
    if(!box.enabled || box.suspended || box.samples.empty())return false;
    std::copy(box.samples.front().payload.begin(),box.samples.front().payload.end(),data);box.samples.pop_front();return true;
}
void DescribeVendor(const std::shared_ptr<VendorChannel>& channel,wchar_t* text,uint32_t capacity) noexcept {
    if(!text || !capacity)return;
    if(!channel){wcsncpy_s(text,capacity,L"GameInput raw report channel",_TRUNCATE);return;}
    const auto factoryInfo=channel->factory?channel->factory->Describe():L"Factory not created";
    std::lock_guard lock(channel->inbox->mutex);const auto& box=*channel->inbox;
    _snwprintf_s(text,capacity,_TRUNCATE,L"%s; %s; input=%s; resumed=%u; suspended=%u; telemetry=%s; last message class=%u id=%02X length=%u",
        box.note.c_str(),factoryInfo.c_str(),!box.activityKnown?L"unknown":box.suspended?L"suspended":L"active",box.resumes,box.suspends,
        box.seenTelemetry?L"seen":L"not yet",box.lastClass,box.lastId,box.lastLength);
}
}
