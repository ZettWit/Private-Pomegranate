// Pomegranate's Windows 11 right-click menu entry (spec §12.3).
//
// An IExplorerCommand registered by the MSIX package. It doesn't do any work itself: it collects
// the selected paths and starts Pomegranate.exe (next to this DLL) with "--wheel <paths…>". The
// running app receives that over its single-instance pipe and opens the wheel at the cursor.
// Very long selections are passed through a temporary list file instead ("--wheel-list <file>").

#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <shlobj.h>
#include <shobjidl_core.h>
#include <shlwapi.h>
#include <new>
#include <string>
#include <vector>

#pragma comment(lib, "shlwapi.lib")
#pragma comment(lib, "ole32.lib")
#pragma comment(lib, "shell32.lib")
#pragma comment(lib, "user32.lib")

// {21A5268D-A685-4143-A2E1-3F0C7C29FF02}
static const CLSID CLSID_OpenInPomegranate = {
    0x21a5268d, 0xa685, 0x4143, {0xa2, 0xe1, 0x3f, 0x0c, 0x7c, 0x29, 0xff, 0x02}};

static HMODULE g_module = nullptr;
static LONG g_objects = 0;

static std::wstring ModuleFolder()
{
    std::wstring path(MAX_PATH, L'\0');
    for (;;)
    {
        DWORD length = GetModuleFileNameW(g_module, path.data(), static_cast<DWORD>(path.size()));
        if (length == 0)
        {
            return L"";
        }

        if (length < path.size())
        {
            path.resize(length);
            break;
        }

        path.resize(path.size() * 2);
    }

    auto slash = path.find_last_of(L'\\');
    return slash == std::wstring::npos ? L"" : path.substr(0, slash);
}

static std::wstring Quote(const std::wstring& value)
{
    // Quote for CommandLineToArgvW: double any backslashes before a quote or the closing quote.
    std::wstring result = L"\"";
    size_t backslashes = 0;
    for (wchar_t c : value)
    {
        if (c == L'\\')
        {
            ++backslashes;
            continue;
        }

        if (c == L'"')
        {
            result.append(backslashes * 2 + 1, L'\\');
        }
        else
        {
            result.append(backslashes, L'\\');
        }

        backslashes = 0;
        result.push_back(c);
    }

    result.append(backslashes * 2, L'\\');
    result.push_back(L'"');
    return result;
}

static std::vector<std::wstring> SelectedPaths(IShellItemArray* items)
{
    std::vector<std::wstring> paths;
    DWORD count = 0;
    if (items == nullptr || FAILED(items->GetCount(&count)))
    {
        return paths;
    }

    for (DWORD i = 0; i < count; ++i)
    {
        IShellItem* item = nullptr;
        if (FAILED(items->GetItemAt(i, &item)))
        {
            continue;
        }

        PWSTR path = nullptr;
        if (SUCCEEDED(item->GetDisplayName(SIGDN_FILESYSPATH, &path)))
        {
            paths.emplace_back(path);
            CoTaskMemFree(path);
        }

        item->Release();
    }

    return paths;
}

static bool WriteListFile(const std::vector<std::wstring>& paths, std::wstring& file)
{
    wchar_t folder[MAX_PATH];
    wchar_t name[MAX_PATH];
    if (GetTempPathW(MAX_PATH, folder) == 0 || GetTempFileNameW(folder, L"pom", 0, name) == 0)
    {
        return false;
    }

    HANDLE handle = CreateFileW(name, GENERIC_WRITE, 0, nullptr, CREATE_ALWAYS, FILE_ATTRIBUTE_TEMPORARY, nullptr);
    if (handle == INVALID_HANDLE_VALUE)
    {
        return false;
    }

    // UTF-8, one path per line.
    bool ok = true;
    for (const auto& path : paths)
    {
        std::wstring line = path + L"\n";
        int bytes = WideCharToMultiByte(CP_UTF8, 0, line.c_str(), static_cast<int>(line.size()), nullptr, 0, nullptr, nullptr);
        std::string utf8(bytes, '\0');
        WideCharToMultiByte(CP_UTF8, 0, line.c_str(), static_cast<int>(line.size()), utf8.data(), bytes, nullptr, nullptr);
        DWORD written = 0;
        ok = ok && WriteFile(handle, utf8.data(), static_cast<DWORD>(utf8.size()), &written, nullptr);
    }

    CloseHandle(handle);
    file = name;
    return ok;
}

class OpenInPomegranateCommand final : public IExplorerCommand
{
public:
    OpenInPomegranateCommand() { InterlockedIncrement(&g_objects); }
    ~OpenInPomegranateCommand() { InterlockedDecrement(&g_objects); }

    // IUnknown
    IFACEMETHODIMP QueryInterface(REFIID riid, void** ppv) override
    {
        static const QITAB table[] = {QITABENT(OpenInPomegranateCommand, IExplorerCommand), {nullptr, 0}};
        return QISearch(this, table, riid, ppv);
    }

    IFACEMETHODIMP_(ULONG) AddRef() override { return InterlockedIncrement(&m_refs); }

    IFACEMETHODIMP_(ULONG) Release() override
    {
        ULONG refs = InterlockedDecrement(&m_refs);
        if (refs == 0)
        {
            delete this;
        }

        return refs;
    }

    // IExplorerCommand
    IFACEMETHODIMP GetTitle(IShellItemArray*, PWSTR* name) override
    {
        return SHStrDupW(L"Open in Pomegranate", name);
    }

    IFACEMETHODIMP GetIcon(IShellItemArray*, PWSTR* icon) override
    {
        std::wstring path = ModuleFolder() + L"\\Pomegranate.exe,0";
        return SHStrDupW(path.c_str(), icon);
    }

    IFACEMETHODIMP GetToolTip(IShellItemArray*, PWSTR* tip) override
    {
        *tip = nullptr;
        return E_NOTIMPL;
    }

    IFACEMETHODIMP GetCanonicalName(GUID* guid) override
    {
        *guid = CLSID_OpenInPomegranate;
        return S_OK;
    }

    IFACEMETHODIMP GetState(IShellItemArray*, BOOL, EXPCMDSTATE* state) override
    {
        *state = ECS_ENABLED;
        return S_OK;
    }

    IFACEMETHODIMP GetFlags(EXPCMDFLAGS* flags) override
    {
        *flags = ECF_DEFAULT;
        return S_OK;
    }

    IFACEMETHODIMP EnumSubCommands(IEnumExplorerCommand** commands) override
    {
        *commands = nullptr;
        return E_NOTIMPL;
    }

    IFACEMETHODIMP Invoke(IShellItemArray* items, IBindCtx*) override
    {
        auto paths = SelectedPaths(items);
        if (paths.empty())
        {
            return S_OK;
        }

        std::wstring exe = ModuleFolder() + L"\\Pomegranate.exe";
        std::wstring commandLine = Quote(exe) + L" --wheel";
        for (const auto& path : paths)
        {
            commandLine += L" " + Quote(path);
        }

        // CreateProcess allows 32,767 characters; leave room to spare.
        if (commandLine.size() > 30000)
        {
            std::wstring list;
            if (!WriteListFile(paths, list))
            {
                return E_FAIL;
            }

            commandLine = Quote(exe) + L" --wheel-list " + Quote(list);
        }

        STARTUPINFOW startup{};
        startup.cb = sizeof(startup);
        PROCESS_INFORMATION process{};
        if (!CreateProcessW(exe.c_str(), commandLine.data(), nullptr, nullptr, FALSE, 0, nullptr, nullptr, &startup, &process))
        {
            return HRESULT_FROM_WIN32(GetLastError());
        }

        // Let the running Pomegranate take the foreground so its wheel can appear on top.
        AllowSetForegroundWindow(process.dwProcessId);
        CloseHandle(process.hThread);
        CloseHandle(process.hProcess);
        return S_OK;
    }

private:
    LONG m_refs = 1;
};

class ClassFactory final : public IClassFactory
{
public:
    IFACEMETHODIMP QueryInterface(REFIID riid, void** ppv) override
    {
        static const QITAB table[] = {QITABENT(ClassFactory, IClassFactory), {nullptr, 0}};
        return QISearch(this, table, riid, ppv);
    }

    IFACEMETHODIMP_(ULONG) AddRef() override { return 2; }
    IFACEMETHODIMP_(ULONG) Release() override { return 1; }

    IFACEMETHODIMP CreateInstance(IUnknown* outer, REFIID riid, void** ppv) override
    {
        *ppv = nullptr;
        if (outer != nullptr)
        {
            return CLASS_E_NOAGGREGATION;
        }

        auto* command = new (std::nothrow) OpenInPomegranateCommand();
        if (command == nullptr)
        {
            return E_OUTOFMEMORY;
        }

        HRESULT hr = command->QueryInterface(riid, ppv);
        command->Release();
        return hr;
    }

    IFACEMETHODIMP LockServer(BOOL lock) override
    {
        if (lock)
        {
            InterlockedIncrement(&g_objects);
        }
        else
        {
            InterlockedDecrement(&g_objects);
        }

        return S_OK;
    }
};

static ClassFactory g_factory;

BOOL APIENTRY DllMain(HMODULE module, DWORD reason, LPVOID)
{
    if (reason == DLL_PROCESS_ATTACH)
    {
        g_module = module;
        DisableThreadLibraryCalls(module);
    }

    return TRUE;
}

STDAPI DllGetClassObject(REFCLSID clsid, REFIID riid, LPVOID* ppv)
{
    if (clsid != CLSID_OpenInPomegranate)
    {
        *ppv = nullptr;
        return CLASS_E_CLASSNOTAVAILABLE;
    }

    return g_factory.QueryInterface(riid, ppv);
}

STDAPI DllCanUnloadNow()
{
    return g_objects == 0 ? S_OK : S_FALSE;
}
