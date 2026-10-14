#include <windows.h>
#include <shobjidl.h>
#include <shlwapi.h>
#include <shellapi.h>
#include <atomic>
#include <new>
#include "MenuLogic.h"

static HMODULE moduleHandle;
static std::atomic<long> objects{0}, locks{0};

static std::wstring ModuleDirectory() {
    wchar_t path[32768]{};
    DWORD length = GetModuleFileNameW(moduleHandle, path, ARRAYSIZE(path));
    if (!length || length >= ARRAYSIZE(path)) return {};
    std::wstring value(path, length);
    return value.substr(0, value.find_last_of(L'\\'));
}
static HRESULT SelectedPath(IShellItemArray* items, std::wstring& path, bool& folder) {
    if (!items) return E_INVALIDARG;
    DWORD count = 0;
    HRESULT hr = items->GetCount(&count);
    if (FAILED(hr) || count != 1) return E_INVALIDARG;
    IShellItem* item = nullptr;
    hr = items->GetItemAt(0, &item);
    if (FAILED(hr)) return hr;
    SFGAOF attributes = 0;
    hr = item->GetAttributes(SFGAO_FILESYSTEM | SFGAO_FOLDER, &attributes);
    PWSTR name = nullptr;
    if (SUCCEEDED(hr) && (attributes & SFGAO_FILESYSTEM)) hr = item->GetDisplayName(SIGDN_FILESYSPATH, &name);
    else hr = E_INVALIDARG;
    item->Release();
    if (FAILED(hr)) return hr;
    if (!name) return E_FAIL;
    try {
        path = name;
        // ZIP namespace items can report SFGAO_FOLDER while being physical files.
        DWORD fileAttributes = GetFileAttributesW(name);
        folder = fileAttributes != INVALID_FILE_ATTRIBUTES
            ? (fileAttributes & FILE_ATTRIBUTE_DIRECTORY) != 0 : (attributes & SFGAO_FOLDER) != 0;
    }
    catch (...) { CoTaskMemFree(name); return E_OUTOFMEMORY; }
    CoTaskMemFree(name);
    return S_OK;
}

class Command final : public IExplorerCommand {
    std::atomic<ULONG> references{1};
    Action action;
public:
    explicit Command(Action a) : action(a) { ++objects; }
    ~Command() { --objects; }
    HRESULT STDMETHODCALLTYPE QueryInterface(REFIID iid, void** result) override {
        if (!result) return E_POINTER;
        *result = nullptr;
        if (iid == IID_IUnknown || iid == IID_IExplorerCommand) { *result = static_cast<IExplorerCommand*>(this); AddRef(); return S_OK; }
        return E_NOINTERFACE;
    }
    ULONG STDMETHODCALLTYPE AddRef() override { return ++references; }
    ULONG STDMETHODCALLTYPE Release() override { ULONG value = --references; if (!value) delete this; return value; }
    HRESULT STDMETHODCALLTYPE GetTitle(IShellItemArray* items, PWSTR* title) override {
        if (!title) return E_POINTER;
        *title = nullptr;
        try {
            std::wstring text;
            switch (action) {
            case Action::Root: text = L"LunaArc"; break;
            case Action::Open: text = L"Open with LunaArc"; break;
            case Action::Extract: text = L"Extract files..."; break;
            case Action::Here: text = L"Extract Here"; break;
            case Action::Add: text = L"Add to archive..."; break;
            case Action::Zip: text = L"Add to ZIP archive..."; break;
            case Action::Folder: {
                std::wstring path; bool folder = false;
                text = SUCCEEDED(SelectedPath(items, path, folder))
                    ? L"Extract to \"" + FolderName(path).substr(0, 100) + L"\\\"" : L"Extract to folder";
                break;
            }
            }
            return SHStrDupW(text.c_str(), title);
        } catch (...) { return E_OUTOFMEMORY; }
    }
    HRESULT STDMETHODCALLTYPE GetIcon(IShellItemArray*, PWSTR* icon) override {
        if (!icon) return E_POINTER;
        *icon = nullptr;
        try { return SHStrDupW((L"\"" + ModuleDirectory() + L"\\LunaArc.Menu.ico\",0").c_str(), icon); }
        catch (...) { return E_OUTOFMEMORY; }
    }
    HRESULT STDMETHODCALLTYPE GetToolTip(IShellItemArray*, PWSTR* text) override {
        if (!text) return E_POINTER;
        *text = nullptr; return E_NOTIMPL;
    }
    HRESULT STDMETHODCALLTYPE GetCanonicalName(GUID* name) override {
        if (!name) return E_POINTER;
        *name = MenuClsid; name->Data1 += static_cast<ULONG>(action); return S_OK;
    }
    HRESULT STDMETHODCALLTYPE GetState(IShellItemArray* items, BOOL, EXPCMDSTATE* state) override {
        if (!state) return E_POINTER;
        *state = ECS_HIDDEN;
        try {
            std::wstring path; bool folder = false;
            if (FAILED(SelectedPath(items, path, folder))) return S_OK;
            bool archive = !folder && IsArchive(path);
            if (action == Action::Root || (action <= Action::Here && archive) || (action >= Action::Add && !archive))
                *state = ECS_ENABLED;
            return S_OK;
        } catch (...) { return E_OUTOFMEMORY; }
    }
    HRESULT STDMETHODCALLTYPE Invoke(IShellItemArray* items, IBindCtx*) override {
        if (action == Action::Root) return E_NOTIMPL;
        try {
            std::wstring path; bool folder = false;
            HRESULT hr = SelectedPath(items, path, folder);
            if (FAILED(hr)) return hr;
            EXPCMDSTATE state;
            hr = GetState(items, TRUE, &state);
            if (FAILED(hr) || state != ECS_ENABLED) return E_INVALIDARG;
            std::wstring executable = ModuleDirectory() + L"\\LunaArc.exe";
            std::wstring arguments = std::wstring(SwitchFor(action)) + L" " + QuoteArgument(path);
            SHELLEXECUTEINFOW info{sizeof(info)};
            info.fMask = SEE_MASK_FLAG_NO_UI;
            info.lpFile = executable.c_str(); info.lpParameters = arguments.c_str();
            info.nShow = SW_SHOWNORMAL;
            return ShellExecuteExW(&info) ? S_OK : HRESULT_FROM_WIN32(GetLastError());
        } catch (...) { return E_OUTOFMEMORY; }
    }
    HRESULT STDMETHODCALLTYPE GetFlags(EXPCMDFLAGS* flags) override {
        if (!flags) return E_POINTER;
        *flags = action == Action::Root ? ECF_HASSUBCOMMANDS : ECF_DEFAULT; return S_OK;
    }
    HRESULT STDMETHODCALLTYPE EnumSubCommands(IEnumExplorerCommand** result) override;
};

class Commands final : public IEnumExplorerCommand {
    std::atomic<ULONG> references{1};
    ULONG position = 1;
public:
    explicit Commands(ULONG start = 1) : position(start) { ++objects; }
    ~Commands() { --objects; }
    HRESULT STDMETHODCALLTYPE QueryInterface(REFIID iid, void** result) override {
        if (!result) return E_POINTER;
        *result = nullptr;
        if (iid == IID_IUnknown || iid == IID_IEnumExplorerCommand) { *result = static_cast<IEnumExplorerCommand*>(this); AddRef(); return S_OK; }
        return E_NOINTERFACE;
    }
    ULONG STDMETHODCALLTYPE AddRef() override { return ++references; }
    ULONG STDMETHODCALLTYPE Release() override { ULONG value = --references; if (!value) delete this; return value; }
    HRESULT STDMETHODCALLTYPE Next(ULONG count, IExplorerCommand** commands, ULONG* fetched) override {
        if (!commands || (!fetched && count != 1)) return E_POINTER;
        ULONG completed = 0;
        for (; completed < count && position <= 6; ++completed, ++position) {
            commands[completed] = new(std::nothrow) Command(static_cast<Action>(position));
            if (!commands[completed]) {
                for (ULONG i = 0; i < completed; ++i) commands[i]->Release();
                if (fetched) *fetched = 0;
                return E_OUTOFMEMORY;
            }
        }
        if (fetched) *fetched = completed;
        return completed == count ? S_OK : S_FALSE;
    }
    HRESULT STDMETHODCALLTYPE Skip(ULONG count) override {
        ULONG remaining = 7 - position;
        position += (count < remaining ? count : remaining);
        return count <= remaining ? S_OK : S_FALSE;
    }
    HRESULT STDMETHODCALLTYPE Reset() override { position = 1; return S_OK; }
    HRESULT STDMETHODCALLTYPE Clone(IEnumExplorerCommand** result) override {
        if (!result) return E_POINTER;
        *result = new(std::nothrow) Commands(position); return *result ? S_OK : E_OUTOFMEMORY;
    }
};
HRESULT Command::EnumSubCommands(IEnumExplorerCommand** result) {
    if (!result) return E_POINTER;
    *result = nullptr;
    if (action != Action::Root) return E_NOTIMPL;
    *result = new(std::nothrow) Commands(); return *result ? S_OK : E_OUTOFMEMORY;
}

class Factory final : public IClassFactory {
    std::atomic<ULONG> references{1};
public:
    Factory() { ++objects; }
    ~Factory() { --objects; }
    HRESULT STDMETHODCALLTYPE QueryInterface(REFIID iid, void** result) override {
        if (!result) return E_POINTER;
        *result = nullptr;
        if (iid == IID_IUnknown || iid == IID_IClassFactory) { *result = static_cast<IClassFactory*>(this); AddRef(); return S_OK; }
        return E_NOINTERFACE;
    }
    ULONG STDMETHODCALLTYPE AddRef() override { return ++references; }
    ULONG STDMETHODCALLTYPE Release() override { ULONG value = --references; if (!value) delete this; return value; }
    HRESULT STDMETHODCALLTYPE CreateInstance(IUnknown* outer, REFIID iid, void** result) override {
        if (!result) return E_POINTER;
        *result = nullptr;
        if (outer) return CLASS_E_NOAGGREGATION;
        auto command = new(std::nothrow) Command(Action::Root);
        if (!command) return E_OUTOFMEMORY;
        HRESULT hr = command->QueryInterface(iid, result);
        command->Release(); return hr;
    }
    HRESULT STDMETHODCALLTYPE LockServer(BOOL lock) override { if (lock) ++locks; else --locks; return S_OK; }
};
extern "C" HRESULT __stdcall DllGetClassObject(REFCLSID clsid, REFIID iid, void** result) {
    if (!result) return E_POINTER;
    *result = nullptr;
    if (clsid != MenuClsid) return CLASS_E_CLASSNOTAVAILABLE;
    auto factory = new(std::nothrow) Factory();
    if (!factory) return E_OUTOFMEMORY;
    HRESULT hr = factory->QueryInterface(iid, result);
    factory->Release(); return hr;
}
extern "C" HRESULT __stdcall DllCanUnloadNow() { return objects == 0 && locks == 0 ? S_OK : S_FALSE; }
BOOL WINAPI DllMain(HINSTANCE instance, DWORD reason, LPVOID) {
    if (reason == DLL_PROCESS_ATTACH) moduleHandle = instance;
    return TRUE;
}
