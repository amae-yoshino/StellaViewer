#include <windows.h>
#include <shlobj.h>
#include <shobjidl.h>
#include <shlguid.h>
#include <wrl/client.h>
#include <string>
using Microsoft::WRL::ComPtr;

struct Apartment {
    HRESULT hr = CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED);
    ~Apartment() { if (SUCCEEDED(hr)) CoUninitialize(); }
};

// RecycleItem has no permanent-deletion fallback. Never call RemoveItem/DeleteFile here.
extern "C" __declspec(dllexport) HRESULT __stdcall RecyclePath(PCWSTR path, PIDLIST_ABSOLUTE* identifier, UINT* bytes) {
    *identifier = nullptr; *bytes = 0;
    Apartment apartment; if (FAILED(apartment.hr)) return apartment.hr;
    ComPtr<IShellItem> source, parent, recycle, result;
    ComPtr<ITransferSource> transfer;
    HRESULT hr = SHCreateItemFromParsingName(path, nullptr, IID_PPV_ARGS(&source));
    if (SUCCEEDED(hr)) hr = source->GetParent(&parent);
    if (SUCCEEDED(hr)) hr = parent->BindToHandler(nullptr, BHID_Transfer, IID_PPV_ARGS(&transfer));
    if (SUCCEEDED(hr)) hr = SHGetKnownFolderItem(FOLDERID_RecycleBinFolder, KF_FLAG_DEFAULT, nullptr, IID_PPV_ARGS(&recycle));
    if (SUCCEEDED(hr)) hr = transfer->RecycleItem(source.Get(), recycle.Get(), TSF_NORMAL, &result);
    if (SUCCEEDED(hr) && !result) hr = E_FAIL;
    if (SUCCEEDED(hr)) hr = SHGetIDListFromObject(result.Get(), identifier);
    if (SUCCEEDED(hr)) *bytes = ILGetSize(*identifier);
    return hr;
}

// TSF_FAIL_EXIST is zero: a destination collision fails rather than merging or overwriting.
extern "C" __declspec(dllexport) HRESULT __stdcall RestoreItem(PCIDLIST_ABSOLUTE identifier, PCWSTR folder, PCWSTR name) {
    Apartment apartment; if (FAILED(apartment.hr)) return apartment.hr;
    ComPtr<IShellItem> source, parent, destination, result;
    ComPtr<ITransferSource> transfer;
    HRESULT hr = SHCreateItemFromIDList(identifier, IID_PPV_ARGS(&source));
    if (SUCCEEDED(hr)) hr = source->GetParent(&parent);
    if (SUCCEEDED(hr)) hr = parent->BindToHandler(nullptr, BHID_Transfer, IID_PPV_ARGS(&transfer));
    if (SUCCEEDED(hr)) hr = SHCreateItemFromParsingName(folder, nullptr, IID_PPV_ARGS(&destination));
    if (SUCCEEDED(hr)) hr = transfer->MoveItem(source.Get(), destination.Get(), name, TSF_FAIL_EXIST, &result);
    if (SUCCEEDED(hr) && !result) hr = E_FAIL;
    return hr;
}
