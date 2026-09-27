#pragma once
#include <inspectable.h>
#include <winstring.h>
#include <winrt/Windows.Foundation.h>
#include <atomic>
#include <functional>
#include <mutex>

// Interoperability ABI used by Windows.Gaming.Input and the installed OEM
// component. The host supplies its controlling IGameController through this
// interface before subscribing to the custom input sink.
MIDL_INTERFACE("06E58977-7684-4DC5-BAD1-CDA52A4AA06D") IGipAggregation : public IInspectable {
    virtual HRESULT STDMETHODCALLTYPE SetOuter(IInspectable* outer)=0;
};

namespace G7Native {
// The returned object is the non-delegating inner. The Windows host attaches
// its controlling IGameController before asking for the input-sink interfaces.
class AggregationShell final : public IGipAggregation {
    using Inspectable=winrt::Windows::Foundation::IInspectable;
    std::atomic<ULONG> references{1};
    std::mutex mutex;
    Inspectable inner{nullptr};
    std::function<Inspectable(Inspectable const&)> compose;
    bool bound=false;
public:
    explicit AggregationShell(std::function<Inspectable(Inspectable const&)> callback):compose(std::move(callback)){}
    HRESULT STDMETHODCALLTYPE QueryInterface(REFIID iid,void** result) noexcept override {
        if(!result)return E_POINTER;*result=nullptr;
        if(iid==IID_IUnknown || iid==__uuidof(IInspectable) || iid==__uuidof(IGipAggregation) || iid==__uuidof(IAgileObject)) {
            *result=static_cast<IGipAggregation*>(this);AddRef();return S_OK;
        }
        std::lock_guard lock(mutex);
        if(!inner)return E_NOINTERFACE;
        return reinterpret_cast<IInspectable*>(winrt::get_abi(inner))->QueryInterface(iid,result);
    }
    ULONG STDMETHODCALLTYPE AddRef() noexcept override {return ++references;}
    ULONG STDMETHODCALLTYPE Release() noexcept override {
        ULONG remaining=--references;if(!remaining)delete this;return remaining;
    }
    HRESULT STDMETHODCALLTYPE GetIids(ULONG* count,IID** values) noexcept override {
        if(!count || !values)return E_POINTER;*count=0;*values=nullptr;
        auto allocation=static_cast<IID*>(CoTaskMemAlloc(sizeof(IID)));if(!allocation)return E_OUTOFMEMORY;
        *allocation=__uuidof(IGipAggregation);*count=1;*values=allocation;return S_OK;
    }
    HRESULT STDMETHODCALLTYPE GetRuntimeClassName(HSTRING* name) noexcept override {
        if(!name)return E_POINTER;
        constexpr wchar_t value[]=L"G7Bridge.Native.AggregationShell";
        return WindowsCreateString(value,static_cast<UINT32>(std::size(value)-1),name);
    }
    HRESULT STDMETHODCALLTYPE GetTrustLevel(TrustLevel* level) noexcept override {
        if(!level)return E_POINTER;*level=BaseTrust;return S_OK;
    }
    HRESULT STDMETHODCALLTYPE SetOuter(IInspectable* outer) noexcept override {
        if(!outer || outer==static_cast<IGipAggregation*>(this))return E_INVALIDARG;
        try {
            std::lock_guard lock(mutex);if(bound)return E_ILLEGAL_METHOD_CALL;
            Inspectable controller{nullptr};winrt::copy_from_abi(controller,outer);
            inner=compose(controller);
            if(!inner)return E_UNEXPECTED;
            bound=true;compose={};return S_OK;
        } catch(...) {return winrt::to_hresult();}
    }
};
}
