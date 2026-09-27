#pragma once
#include <guiddef.h>

namespace G7Native {
// Microsoft.Xbox.Input.IGamepad, present in the captured G7 Pro descriptor.
inline constexpr GUID GamepadGipInterface{0x082E402C,0x07DF,0x45E1,{0xA5,0xAB,0xA3,0x12,0x7A,0xF1,0x97,0xB5}};

template<class Manager,class Factory>
void RegisterG7Factory(Factory const& factory) {
    Manager::RegisterCustomFactoryForGipInterface(factory,GamepadGipInterface);
}
}
