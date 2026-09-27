#pragma once
#include <cstdint>
#include <span>

namespace G7Native {
inline bool IsSessionPayload(std::span<const uint8_t> data) {
    return (data.size()==1 || data.size()==2) && data[0]==0xF2 && (data.size()==1 || data[1]==0);
}
inline bool IsVendorTelemetry(uint32_t messageClass,uint8_t id,std::span<const uint8_t> data) {
    // Observed on Windows: wire message 0x10 arrives as Command/class 0, ID 0x10.
    return messageClass==0 && id==0x10 && data.size()==60 && data[0]==0xE0;
}
inline bool CanBindVendor(uint32_t physicalRoots,bool selectionPresent,uint32_t providers) {
    return physicalRoots==1 && selectionPresent && providers==1;
}
}
