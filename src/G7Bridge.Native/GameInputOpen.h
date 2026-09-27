#pragma once
#include <GameInput.h>

namespace G7Native {

template<class Api>
HRESULT RegisterControllerInventory(Api* api,void* context,
    GameInput::v3::GameInputDeviceCallback callback,GameInput::v3::GameInputCallbackToken* token)
{
    using namespace GameInput::v3;
    return api->RegisterDeviceCallback(nullptr,GameInputKindGamepad|GameInputKindController|GameInputKindRawDeviceReport,GameInputDeviceConnected,
        GameInputBlockingEnumeration,context,callback,token);
}

inline void CALLBACK EnumerationReady(GameInput::v3::GameInputCallbackToken,void*,
    GameInput::v3::IGameInputDevice*,uint64_t,GameInput::v3::GameInputDeviceStatus,
    GameInput::v3::GameInputDeviceStatus) noexcept { }

// Kept independent of runtime creation so the fresh-session lifecycle can be
// exercised with an in-memory API in the native unit test.
template<class Api>
HRESULT FindEnumeratedDevice(Api* api,const APP_LOCAL_DEVICE_ID& id,
    GameInput::v3::IGameInputDevice** device)
{
    using namespace GameInput::v3;
    *device=nullptr;
    GameInputCallbackToken token{};
    const HRESULT hr=api->RegisterDeviceCallback(nullptr,GameInputKindGamepad,
        GameInputDeviceConnected,GameInputBlockingEnumeration,nullptr,EnumerationReady,&token);
    if(FAILED(hr))return hr;
    struct Registration {
        Api* api;GameInputCallbackToken token;
        ~Registration(){api->StopCallback(token);api->UnregisterCallback(token);}
    } registration{api,token};
    // FindDeviceFromId only consults existing local objects. A new API instance
    // must finish initial device enumeration before this lookup can succeed.
    return api->FindDeviceFromId(&id,device);
}

}
