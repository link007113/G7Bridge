// Exercise the production Factory/Sink/Inbox with an in-memory WinRT provider.
// No GameInputCreate, OpenVendorChannel, GetFactory or device registration is called.
#include "../../src/G7Bridge.Native/VendorGip.cpp"
#include "../../src/G7Bridge.Native/GipAggregation.h"
#include <iostream>

namespace {
namespace C=winrt::Windows::Gaming::Input::Custom;
struct FakeProvider : winrt::implements<FakeProvider,C::IGameControllerProvider,C::IGipGameControllerProvider> {
    bool connected=true;
    uint16_t vendor=0x3537,product=0x106B;
    C::GameControllerVersionInfo FirmwareVersionInfo() const {return {};}
    C::GameControllerVersionInfo HardwareVersionInfo() const {return {};}
    uint16_t HardwareVendorId() const {return vendor;}
    uint16_t HardwareProductId() const {return product;}
    bool IsConnected() const {return connected;}
    void SendMessage(C::GipMessageClass const&,uint8_t,winrt::array_view<uint8_t const>) {throw winrt::hresult_not_implemented();}
    void SendReceiveMessage(C::GipMessageClass const&,uint8_t,winrt::array_view<uint8_t const>,winrt::array_view<uint8_t>) {throw winrt::hresult_not_implemented();}
    winrt::Windows::Foundation::IAsyncOperationWithProgress<C::GipFirmwareUpdateResult,C::GipFirmwareUpdateProgress>
        UpdateFirmwareAsync(winrt::Windows::Storage::Streams::IInputStream const&) {throw winrt::hresult_not_implemented();}
};
std::array<uint8_t,60> CapturedPayload() {
    // Actual L4 payload from capture 2, frame 8241 (GIP header excluded).
    constexpr char hex[]="e0808080800f0000000000000004ff520079ff7bfc520c52dd00000000470000ffff00ffff00ffff00ffff00000000838185850f0008000000000000";
    std::array<uint8_t,60> result{};
    auto digit=[](char c)->uint8_t{return static_cast<uint8_t>(c<='9'?c-'0':c-'a'+10);};
    static_assert(sizeof(hex)==121);
    for(size_t i=0;i<result.size();++i)result[i]=static_cast<uint8_t>((digit(hex[2*i])<<4)|digit(hex[2*i+1]));
    return result;
}
struct FakeOuter : winrt::implements<FakeOuter,G7Native::Wgi::IGameController,winrt::composing> {
    using HeadsetHandler=winrt::Windows::Foundation::TypedEventHandler<G7Native::Wgi::IGameController,G7Native::Wgi::Headset>;
    using UserHandler=winrt::Windows::Foundation::TypedEventHandler<G7Native::Wgi::IGameController,winrt::Windows::System::UserChangedEventArgs>;
    winrt::event<HeadsetHandler> connected,disconnected;
    winrt::event<UserHandler> user;
    void Inner(winrt::Windows::Foundation::IInspectable value){m_inner=std::move(value);}
    G7Native::Wgi::Headset Headset()const{return nullptr;}
    bool IsWireless()const{return true;}
    winrt::Windows::System::User User()const{return nullptr;}
    winrt::event_token HeadsetConnected(HeadsetHandler const& h){return connected.add(h);}
    void HeadsetConnected(winrt::event_token const& t)noexcept{connected.remove(t);}
    winrt::event_token HeadsetDisconnected(HeadsetHandler const& h){return disconnected.add(h);}
    void HeadsetDisconnected(winrt::event_token const& t)noexcept{disconnected.remove(t);}
    winrt::event_token UserChanged(UserHandler const& h){return user.add(h);}
    void UserChanged(winrt::event_token const& t)noexcept{user.remove(t);}
};
winrt::Windows::Foundation::IInspectable Aggregate(winrt::Windows::Foundation::IInspectable inner) {
    auto aggregate=inner.as<IGipAggregation>();
    auto outer=winrt::make_self<FakeOuter>();outer->Inner(std::move(inner));
    auto result=outer.as<winrt::Windows::Foundation::IInspectable>();
    auto abi=reinterpret_cast<::IInspectable*>(winrt::get_abi(result));
    winrt::check_hresult(aggregate->SetOuter(abi));
    if(aggregate->SetOuter(abi)!=E_ILLEGAL_METHOD_CALL)throw std::runtime_error("An inner must not be reparented");
    return result;
}
}

int main() {
    std::cout.setf(std::ios::unitbuf);
    int total=0,failed=0;
    auto check=[&](bool ok,const char* name){++total;std::cout<<(ok?"PASS ":"FAIL ")<<name<<'\n';if(!ok)++failed;};
    auto factory=winrt::make_self<G7Native::Factory>();
    auto provider=winrt::make_self<FakeProvider>();
    auto controller=factory->CreateGameController(provider.as<C::IGameControllerProvider>());
    check(static_cast<bool>(controller),"Production factory constructs a sink for the observed G7 provider");
    check(static_cast<bool>(controller.try_as<IGipAggregation>()),"Factory result accepts the Windows controller aggregation handshake");
    controller=Aggregate(std::move(controller));
    controller=nullptr; // Exactly the hand-off where 0.3.1 lost its only strong reference.
    auto connected=factory->Connected();
    check(connected.size()==1,"Factory owns the sink after the framework releases the returned object");
    if(connected.size()==1) {
        auto sink=connected.front();auto weak=winrt::make_weak(sink.as<C::IGameControllerInputSink>());connected.clear();
        auto duplicate=Aggregate(factory->CreateGameController(provider.as<C::IGameControllerProvider>()));duplicate=nullptr;
        check(factory->Connected().size()==1,"Repeated creation for the same COM provider preserves one controller");
        sink=factory->Connected().front();weak=winrt::make_weak(sink.as<C::IGameControllerInputSink>());
        auto channel=std::make_shared<G7Native::VendorChannel>();
        channel->factory=factory;channel->sink=sink;sink->Attach(channel->inbox);
        auto callback=sink.as<C::IGipGameControllerInputSink>();
        auto payload=CapturedPayload();
        callback.OnMessageReceived(0,C::GipMessageClass::Command,0x10,0x67,payload);
        std::array<uint8_t,64> received{};uint64_t dropped=0;
        check(G7Native::ReadVendorTelemetry(channel,received.data(),64,dropped) &&
            std::equal(payload.begin(),payload.end(),received.begin()),"Captured telemetry survives the real WinRT callback and input queue");
        std::array<uint8_t,32> pad{};pad[0]=0x10;pad[2]=0xFF;pad[3]=0x03;pad[7]=0x80;pad[12]=0x34;pad[13]=0x12;
        callback.OnMessageReceived(0,C::GipMessageClass::LowLatency,0,0x68,pad);
        check(G7Native::ReadVendorPad(channel,received.data(),64) && std::equal(pad.begin(),pad.end(),received.begin()),"Direct GIP preserves the complete 32-byte high-resolution pad report");
        check(G7Native::ReadVendorPad(channel,received.data(),64),"A held pad state remains available between change-only GIP messages");
        callback.OnMessageReceived(0,C::GipMessageClass::Command,0x10,0x68,payload);
        sink->OnInputSuspended(0);
        check(!G7Native::ReadVendorTelemetry(channel,received.data(),64,dropped),"Input suspension flushes queued controls");
        check(!G7Native::ReadVendorPad(channel,received.data(),64),"Input suspension clears cached pad controls");
        callback.OnMessageReceived(0,C::GipMessageClass::Command,0x10,0x69,payload);
        check(!G7Native::ReadVendorTelemetry(channel,received.data(),64,dropped),"Suspended input cannot replay new callbacks as active controls");
        sink->OnInputResumed(0);
        callback.OnMessageReceived(0,C::GipMessageClass::Command,0x10,0x70,payload);
        check(G7Native::ReadVendorTelemetry(channel,received.data(),64,dropped),"Resuming accepts a fresh controller message");
        auto inbox=channel->inbox;
        channel.reset();
        callback.OnMessageReceived(0,C::GipMessageClass::Command,0x10,0x71,payload);
        check(!inbox->enabled && inbox->samples.empty(),"A callback already in flight cannot refill a stopped channel");
        check(factory->Connected().size()==1,"Stopping a session leaves its connected provider available for a later session");
        auto second=std::make_shared<G7Native::VendorChannel>();second->factory=factory;second->sink=sink;sink->Attach(second->inbox);
        callback.OnMessageReceived(0,C::GipMessageClass::Command,0x10,0x72,payload);
        check(G7Native::ReadVendorTelemetry(second,received.data(),64,dropped),"A subsequent session receives fresh input from the retained provider");
        second.reset();callback=nullptr;sink=nullptr;
        provider->connected=false;
        check(factory->Connected().empty(),"A disconnected provider disappears from the selectable controllers");
        check(!weak.get(),"Pruning the disconnected provider releases the owned sink");
    }
    auto other=winrt::make_self<FakeProvider>();other->vendor=0x045E;
    check(!factory->CreateGameController(other.as<C::IGameControllerProvider>()),"Other manufacturers never receive a G7 sink");
    auto removedFactory=winrt::make_self<G7Native::Factory>();
    auto firstProvider=winrt::make_self<FakeProvider>(),secondProvider=winrt::make_self<FakeProvider>();
    auto first=Aggregate(removedFactory->CreateGameController(firstProvider.as<C::IGameControllerProvider>()));
    auto second=Aggregate(removedFactory->CreateGameController(secondProvider.as<C::IGameControllerProvider>()));
    check(removedFactory->Connected().size()==2,"Two distinct providers remain distinct for the ambiguity guard");
    auto firstController=first.as<G7Native::Wgi::IGameController>();
    auto inputInterface=first.as<C::IGameControllerInputSink>();
    auto firstSink=winrt::get_self<G7Native::Sink>(inputInterface);
    auto firstWeak=winrt::make_weak(inputInterface);
    auto active=std::make_shared<G7Native::VendorChannel>();active->factory=removedFactory;
    active->sink=firstSink->get_strong();firstSink->Attach(active->inbox);
    auto oldInbox=active->inbox;
    removedFactory->OnGameControllerRemoved(firstController);
    check(removedFactory->Connected().size()==1 && !oldInbox->enabled,
        "A removal callback disables the removed sink and preserves the other controller");
    active.reset();first=nullptr;firstController=nullptr;inputInterface=nullptr;
    check(!firstWeak.get(),"Removal releases the sink after its last active session closes");
    auto replacement=Aggregate(removedFactory->CreateGameController(firstProvider.as<C::IGameControllerProvider>()));
    check(static_cast<bool>(replacement) && removedFactory->Connected().size()==2,
        "A later provider creation is accepted after the previous sink was removed");
    std::cout<<total-failed<<'/'<<total<<" production-component offline tests passed (in-memory provider only).\n";
    return failed?1:0;
}
