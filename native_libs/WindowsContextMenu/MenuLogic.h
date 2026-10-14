#pragma once
#include <windows.h>
#include <string>
#include <cwctype>
#include <algorithm>

// Keep aligned with ExplorerRequest and the sparse package manifest.
inline constexpr GUID MenuClsid = {0x7b93c594, 0x4f32, 0x4f04, {0xa7, 0x0c, 0xc2, 0x93, 0xa7, 0x2e, 0x61, 0xe4}};
enum class Action { Root, Open, Extract, Folder, Here, Add, Zip };
inline std::wstring Lower(std::wstring value) {
    std::transform(value.begin(), value.end(), value.begin(), [](wchar_t c) { return static_cast<wchar_t>(std::towlower(c)); });
    return value;
}
inline bool EndsWith(const std::wstring& text, const std::wstring& suffix) {
    return text.size() >= suffix.size() && text.compare(text.size() - suffix.size(), suffix.size(), suffix) == 0;
}
inline bool IsArchive(const std::wstring& path) {
    auto lower = Lower(path);
    for (auto ext : {L".larc", L".rar", L".zip", L".7z", L".tar", L".gz", L".tgz", L".bz2", L".xz"})
        if (EndsWith(lower, ext)) return true;
    return false;
}
inline std::wstring FolderName(const std::wstring& path) {
    auto name = path.substr(path.find_last_of(L"\\/") + 1);
    auto lower = Lower(name);
    for (auto ext : {L".tar.gz", L".tar.bz2", L".tar.xz"}) {
        if (EndsWith(lower, ext)) return name.substr(0, name.size() - std::wstring(ext).size());
    }
    auto dot = name.find_last_of(L'.');
    return dot == std::wstring::npos || dot == 0 ? name : name.substr(0, dot);
}
// Windows CommandLineToArgvW quoting, including trailing backslashes.
inline std::wstring QuoteArgument(const std::wstring& value) {
    std::wstring result = L"\"";
    size_t slashes = 0;
    for (auto c : value) {
        if (c == L'\\') { ++slashes; continue; }
        result.append(c == L'\"' ? slashes * 2 + 1 : slashes, L'\\');
        result += c; slashes = 0;
    }
    result.append(slashes * 2, L'\\');
    return result + L"\"";
}
inline const wchar_t* SwitchFor(Action action) {
    switch (action) {
    case Action::Open: return L"--shell-open";
    case Action::Extract: return L"--shell-extract";
    case Action::Folder: return L"--shell-extract-folder";
    case Action::Here: return L"--shell-extract-here";
    case Action::Add: return L"--shell-add";
    case Action::Zip: return L"--shell-add-zip";
    default: return L"";
    }
}
