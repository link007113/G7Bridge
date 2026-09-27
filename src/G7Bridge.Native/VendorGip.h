#pragma once
#include <GameInput.h>
#include <memory>

namespace G7Native {
struct VendorChannel;
HRESULT OpenDirectVendorChannel(const wchar_t* instance,std::shared_ptr<VendorChannel>& channel) noexcept;
bool IsVendorConnected(const std::shared_ptr<VendorChannel>& channel) noexcept;
bool ReadVendorPad(const std::shared_ptr<VendorChannel>& channel,uint8_t* data,uint32_t capacity) noexcept;
bool ReadVendorGamepad(const std::shared_ptr<VendorChannel>& channel,uint32_t& buttons,float* values) noexcept;
HRESULT SetVendorRumble(const std::shared_ptr<VendorChannel>& channel,double left,double right) noexcept;
HRESULT OpenVendorChannel(GameInput::v3::IGameInput* api,GameInput::v3::IGameInputDevice* device,
    std::shared_ptr<VendorChannel>& channel) noexcept;
HRESULT SendVendorSession(const std::shared_ptr<VendorChannel>& channel,const uint8_t* data,uint32_t length) noexcept;
bool ReadVendorTelemetry(const std::shared_ptr<VendorChannel>& channel,uint8_t* data,uint32_t capacity,uint64_t& dropped) noexcept;
void DescribeVendor(const std::shared_ptr<VendorChannel>& channel,wchar_t* text,uint32_t capacity) noexcept;
}
