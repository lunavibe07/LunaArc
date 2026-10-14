#include <windows.h>
#include <shobjidl.h>
#include <shellapi.h>
#include <wrl/client.h>
#include <iostream>
#include <filesystem>
#include <fstream>
#include <stdexcept>
#include "MenuLogic.h"
using Microsoft::WRL::ComPtr;
static int checks = 0;
static void Check(bool success, const char* message) {
    if (!success) throw std::runtime_error(message);
    ++checks;
}
static ComPtr<IShellItemArray> Selection(const std::filesystem::path& path) {
    ComPtr<IShellItem> item;
    Check(SUCCEEDED(SHCreateItemFromParsingName(path.c_str(), nullptr, IID_PPV_ARGS(&item))), "Create a shell item");
    ComPtr<IShellItemArray> items;
    Check(SUCCEEDED(SHCreateShellItemArrayFromShellItem(item.Get(), IID_PPV_ARGS(&items))), "Create a shell selection");
    return items;
}
int wmain(int argc, wchar_t** argv) {
    if (argc != 2) return 2;
    HRESULT init = CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED);
    if (FAILED(init)) return 2;
    wchar_t temp[MAX_PATH]{};
    GetTempPathW(ARRAYSIZE(temp), temp);
    auto directory = std::filesystem::path(temp) / (L"LunaArc-Menu-" + std::to_wstring(GetCurrentProcessId()));
    HMODULE library = nullptr;
    bool created = false;
    try {
        if (std::filesystem::exists(directory)) throw std::runtime_error("Test directory already exists; refusing reuse");
        created = std::filesystem::create_directory(directory);
        if (!created) throw std::runtime_error("Could not create test directory");
        std::ofstream(directory / L"Sample archive.zip") << "Synthetic shell selection; not an archive.";
        std::ofstream(directory / L"notes.txt") << "Synthetic file.";
        library = LoadLibraryW(argv[1]);
        Check(library != nullptr, "Load the native extension without registration");
        auto getFactory = reinterpret_cast<HRESULT(__stdcall*)(REFCLSID, REFIID, void**)>(GetProcAddress(library, "DllGetClassObject"));
        auto canUnload = reinterpret_cast<HRESULT(__stdcall*)()>(GetProcAddress(library, "DllCanUnloadNow"));
        Check(getFactory && canUnload, "COM exports exist");
        Check(canUnload() == S_OK, "DLL starts unloadable");
        {
            ComPtr<IClassFactory> factory;
            Check(SUCCEEDED(getFactory(MenuClsid, IID_PPV_ARGS(&factory))), "Acquire class factory");
            ComPtr<IExplorerCommand> root;
            Check(SUCCEEDED(factory->CreateInstance(nullptr, IID_PPV_ARGS(&root))), "Create IExplorerCommand");
            Check(canUnload() == S_FALSE, "Live objects prevent DLL unloading");
            EXPCMDFLAGS flags{};
            Check(SUCCEEDED(root->GetFlags(&flags)) && flags == ECF_HASSUBCOMMANDS, "Root is a cascading menu");
            PWSTR title = nullptr;
            Check(SUCCEEDED(root->GetTitle(nullptr, &title)) && std::wstring(title) == L"LunaArc", "Root title");
            CoTaskMemFree(title);
            auto archive = Selection(directory / L"Sample archive.zip");
            auto file = Selection(directory / L"notes.txt");
            auto folder = Selection(directory);
            EXPCMDSTATE state{};
            Check(SUCCEEDED(root->GetState(archive.Get(), FALSE, &state)) && state == ECS_ENABLED, "Archive root enabled");
            Check(SUCCEEDED(root->GetState(nullptr, FALSE, &state)) && state == ECS_HIDDEN, "Missing selection hidden");
            ComPtr<IEnumExplorerCommand> children;
            Check(SUCCEEDED(root->EnumSubCommands(&children)), "Enumerate commands");
            int archiveCommands = 0, fileCommands = 0;
            for (int index = 1; index <= 6; ++index) {
                ComPtr<IExplorerCommand> child;
                Check(children->Next(1, child.GetAddressOf(), nullptr) == S_OK, "Next child command");
                Check(SUCCEEDED(child->GetTitle(archive.Get(), &title)), "Child title");
                if (index == 3) Check(std::wstring(title) == L"Extract to \"Sample archive\\\"", "Dynamic extraction-folder title");
                CoTaskMemFree(title);
                Check(SUCCEEDED(child->GetState(archive.Get(), FALSE, &state)), "Archive command state");
                if (state == ECS_ENABLED) ++archiveCommands;
                Check(SUCCEEDED(child->GetState(file.Get(), FALSE, &state)), "File command state");
                if (state == ECS_ENABLED) ++fileCommands;
                Check(SUCCEEDED(child->GetState(folder.Get(), FALSE, &state)) &&
                    state == (index >= 5 ? ECS_ENABLED : ECS_HIDDEN), "Folder command state");
            }
            Check(archiveCommands == 4 && fileCommands == 2, "Archives have four actions; other files have two compression actions");
            ComPtr<IExplorerCommand> end;
            Check(children->Next(1, end.GetAddressOf(), nullptr) == S_FALSE, "Enumeration reaches its end");
            Check(children->Reset() == S_OK && children->Skip(2) == S_OK, "Reset and skip");
            ComPtr<IEnumExplorerCommand> clone;
            Check(children->Clone(&clone) == S_OK, "Clone enumeration");
            Check(clone->Next(1, end.GetAddressOf(), nullptr) == S_OK, "Clone retains position");
            Check(SUCCEEDED(end->GetTitle(archive.Get(), &title)) && std::wstring(title).find(L"Extract to") == 0, "Clone returns the expected child");
            CoTaskMemFree(title);
        }
        Check(canUnload() == S_OK, "All COM references released");
        for (auto path : {L"C:\\folder with spaces\\file.zip", L"C:\\trailing folder\\", L"C:\\Unicode-\u65e5\u672c\\100%.zip"}) {
            auto command = L"program " + QuoteArgument(path);
            int count = 0;
            auto values = CommandLineToArgvW(command.c_str(), &count);
            Check(values && count == 2 && std::wstring(values[1]) == path, "Arguments round-trip safely");
            LocalFree(values);
        }
        Check(FolderName(L"C:\\release.TAR.GZ") == L"release", "Compound extension handled");
        Check(IsArchive(L"C:\\archive.RAR") && !IsArchive(L"C:\\notes.txt"), "Archive extension filtering");
        FreeLibrary(library); library = nullptr;
        Check(std::filesystem::weakly_canonical(directory).parent_path() == std::filesystem::weakly_canonical(temp), "Cleanup stays inside the OS temp directory");
        std::filesystem::remove_all(directory);
        created = false;
        CoUninitialize();
        std::cout << "PASS: " << checks << " native Explorer command checks (no Explorer registration or app launches)." << std::endl;
        return 0;
    } catch (const std::exception& error) {
        std::cerr << "FAIL: " << error.what() << std::endl;
        if (library) FreeLibrary(library);
        // The temporary directory is constructed from the OS temp directory and current process ID.
        if (created && std::filesystem::weakly_canonical(directory).parent_path() == std::filesystem::weakly_canonical(temp))
            std::filesystem::remove_all(directory);
        CoUninitialize();
        return 1;
    }
}
