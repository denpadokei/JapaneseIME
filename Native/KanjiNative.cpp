// JapaneseIME.KanjiNative converts a kana reading into kanji with the Microsoft
// IME conversion engine.
//
// The managed plugin cannot do this in process: Unity runs Mono, and Mono does
// not implement COM activation (Type.GetTypeFromProgID throws
// NotImplementedException "Unmanaged activation is not supported"). Native code
// has full COM support, so the engine is created here on a dedicated STA thread
// and the plugin calls into it with a plain C function.

#include <windows.h>
#include <chrono>
#include <condition_variable>
#include <cstring>
#include <cwchar>
#include <mutex>
#include <string>
#include <vector>

#pragma comment(lib, "ole32.lib")

namespace
{
    const CLSID kMsimeClsid = {0x6A91029E, 0xAA49, 0x471B, {0xAE, 0xE7, 0x7D, 0x33, 0x27, 0x85, 0x66, 0x0D}};
    const IID kIfeLanguageIid = {0x019F7152, 0xE6DB, 0x11D0, {0x83, 0xC3, 0x00, 0xC0, 0x4F, 0xDD, 0xB8, 0x2E}};

    const DWORD kRequestConversion = 0x00010000;      // FELANG_REQ_CONV
    const DWORD kCmodeNoInvisibleChar = 0x40000000;   // FELANG_CMODE_NOINVISIBLECHAR

    // The engine returns one best sentence per request, so a reading is converted
    // inside several short sentences. The surrounding kana change which homophone
    // wins: "これはかんじ" gives 漢字, "このかんじ" gives 感じ and "かんじです"
    // gives 幹事. Either side of the reading is a kana-only template, which lets
    // the result be trimmed back to the reading's own conversion.
    struct Template
    {
        const wchar_t *prefix;
        const wchar_t *suffix;
    };

    const Template kTemplates[] = {
        {L"これは", L""},
        {L"この", L""},
        {L"", L"です"},
        {L"", L"か"},
        {L"", L"をする"},
    };

    const int kStartTimeoutMilliseconds = 15000;
    const int kConvertTimeoutMilliseconds = 1500;

    // Only the head of MORRSLT is used; the SDK packs it to one byte.
#pragma pack(push, 1)
    struct MorphResult
    {
        DWORD size;
        LPWSTR output;
        USHORT outputLength;
    };
#pragma pack(pop)

    // The vtable order of MSIME's IFELanguage.
    struct IFELanguageVtbl
    {
        HRESULT(STDMETHODCALLTYPE *QueryInterface)(void *self, REFIID iid, void **result);
        ULONG(STDMETHODCALLTYPE *AddRef)(void *self);
        ULONG(STDMETHODCALLTYPE *Release)(void *self);
        HRESULT(STDMETHODCALLTYPE *Open)(void *self);
        HRESULT(STDMETHODCALLTYPE *Close)(void *self);
        HRESULT(STDMETHODCALLTYPE *GetJMorphResult)(void *self, DWORD request, DWORD mode, int length,
                                                   const WCHAR *input, DWORD *clauses, MorphResult **result);
        HRESULT(STDMETHODCALLTYPE *GetConversionModeCaps)(void *self, DWORD *caps);
        HRESULT(STDMETHODCALLTYPE *GetPhonetic)(void *self, BSTR text, int start, int length, BSTR *result);
        HRESULT(STDMETHODCALLTYPE *GetConversion)(void *self, BSTR text, int start, int length, BSTR *result);
    };

    struct IFELanguage
    {
        IFELanguageVtbl *vtbl;
    };

    struct State
    {
        std::mutex mutex;
        std::condition_variable requestReady;
        std::condition_variable replyReady;
        std::wstring request;
        std::wstring reply;
        unsigned sequence = 0;
        unsigned answered = 0;
        int status = 0; // 0 not started, 1 starting, 2 ready, negative failed
        bool stop = false;
        // A raw thread handle, not std::thread: the DLL is unloaded (or the
        // process exits) while the worker is still alive, and a joinable
        // std::thread in a static would call std::terminate there.
        HANDLE workerHandle = nullptr;
    };

    State g_state;

    bool HasKanji(const std::wstring &value)
    {
        for (size_t i = 0; i < value.size(); i++)
        {
            wchar_t c = value[i];
            if ((c >= 0x4E00 && c <= 0x9FFF) || (c >= 0x3400 && c <= 0x4DBF) || c == 0x3005)
            {
                return true;
            }
        }

        return false;
    }

    void AddCandidate(std::vector<std::wstring> &candidates, const std::wstring &value, const std::wstring &reading)
    {
        if (value.empty() || value == reading || !HasKanji(value))
        {
            return;
        }

        for (size_t i = 0; i < candidates.size(); i++)
        {
            if (candidates[i] == value)
            {
                return;
            }
        }

        candidates.push_back(value);
    }

    bool ConvertTemplate(IFELanguage *language, const std::wstring &reading, const Template &pattern,
                         std::wstring &output)
    {
        std::wstring input = std::wstring(pattern.prefix) + reading + pattern.suffix;
        std::vector<DWORD> clauses(input.size(), 0);

        MorphResult *result = nullptr;
        HRESULT hr = language->vtbl->GetJMorphResult(language, kRequestConversion, kCmodeNoInvisibleChar,
                                                     static_cast<int>(input.size()), input.c_str(),
                                                     clauses.data(), &result);
        if (FAILED(hr) || result == nullptr)
        {
            return false;
        }

        bool converted = false;
        if (result->output != nullptr && result->outputLength > 0 &&
            result->outputLength <= input.size() * 4)
        {
            std::wstring text(result->output, result->outputLength);
            size_t prefixLength = wcslen(pattern.prefix);
            size_t suffixLength = wcslen(pattern.suffix);

            if (text.size() > prefixLength + suffixLength &&
                text.compare(0, prefixLength, pattern.prefix) == 0 &&
                text.compare(text.size() - suffixLength, suffixLength, pattern.suffix) == 0)
            {
                output = text.substr(prefixLength, text.size() - prefixLength - suffixLength);
                converted = !output.empty();
            }
        }

        CoTaskMemFree(result);
        return converted;
    }

    std::wstring BuildReply(IFELanguage *language, const std::wstring &reading)
    {
        std::vector<std::wstring> candidates;

        for (size_t i = 0; i < sizeof(kTemplates) / sizeof(kTemplates[0]); i++)
        {
            std::wstring value;
            if (ConvertTemplate(language, reading, kTemplates[i], value))
            {
                AddCandidate(candidates, value, reading);
            }
        }

        std::wstring reply;
        for (size_t i = 0; i < candidates.size(); i++)
        {
            if (i > 0)
            {
                reply += L'\t';
            }

            reply += candidates[i];
        }

        return reply;
    }

    void SetStatus(int status)
    {
        std::lock_guard<std::mutex> lock(g_state.mutex);
        g_state.status = status;
    }

    void Worker()
    {
        CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED);

        IFELanguage *language = nullptr;
        void *instance = nullptr;
        HRESULT hr = CoCreateInstance(kMsimeClsid, nullptr, CLSCTX_INPROC_SERVER | CLSCTX_LOCAL_SERVER,
                                      kIfeLanguageIid, &instance);

        if (SUCCEEDED(hr) && instance != nullptr)
        {
            language = static_cast<IFELanguage *>(instance);
            if (FAILED(language->vtbl->Open(language)))
            {
                language->vtbl->Release(language);
                language = nullptr;
            }
        }

        if (language == nullptr)
        {
            SetStatus(-3);
            return;
        }

        SetStatus(2);

        while (true)
        {
            std::wstring reading;
            unsigned sequence;

            {
                std::unique_lock<std::mutex> lock(g_state.mutex);
                g_state.requestReady.wait(lock, [] { return g_state.sequence != g_state.answered || g_state.stop; });
                if (g_state.stop)
                {
                    break;
                }

                reading = g_state.request;
                sequence = g_state.sequence;
            }

            std::wstring reply = BuildReply(language, reading);

            {
                std::lock_guard<std::mutex> lock(g_state.mutex);
                g_state.reply = reply;
                g_state.answered = sequence;
            }

            g_state.replyReady.notify_all();
        }

        language->vtbl->Close(language);
        language->vtbl->Release(language);
        CoUninitialize();
    }

    DWORD WINAPI WorkerThread(void *)
    {
        Worker();
        return 0;
    }

    void StopWorker(bool wait)
    {
        HANDLE handle;

        {
            std::lock_guard<std::mutex> lock(g_state.mutex);
            g_state.stop = true;
            handle = g_state.workerHandle;
        }

        g_state.requestReady.notify_all();

        if (wait && handle != nullptr)
        {
            WaitForSingleObject(handle, 2000);
        }
    }

    void EnsureStarted()
    {
        std::lock_guard<std::mutex> lock(g_state.mutex);
        if (g_state.status == 0)
        {
            g_state.status = 1;
            g_state.workerHandle = CreateThread(nullptr, 0, WorkerThread, nullptr, 0, nullptr);
        }
    }
}

BOOL WINAPI DllMain(HINSTANCE, DWORD reason, LPVOID reserved)
{
    if (reason == DLL_PROCESS_DETACH)
    {
        // reserved is not null while the process is going away; never block the
        // loader in that case.
        StopWorker(reserved == nullptr);
    }

    return TRUE;
}

extern "C" __declspec(dllexport) int JimeGetStatus()
{
    std::lock_guard<std::mutex> lock(g_state.mutex);
    return g_state.status;
}

// Starts the STA thread (and therefore the IME engine) without converting
// anything. The plugin calls this from a background thread at startup so the
// first Space press does not have to wait for COM initialisation.
extern "C" __declspec(dllexport) void JimeWarmUp()
{
    EnsureStarted();
}

extern "C" __declspec(dllexport) int JimeConvert(const wchar_t *reading, wchar_t *buffer, int bufferLength)
{
    if (reading == nullptr || buffer == nullptr || bufferLength <= 1)
    {
        return -1;
    }

    buffer[0] = L'\0';
    EnsureStarted();

    unsigned sequence;
    {
        std::unique_lock<std::mutex> lock(g_state.mutex);
        bool started = g_state.requestReady.wait_for(
            lock, std::chrono::milliseconds(kStartTimeoutMilliseconds),
            [] { return g_state.status >= 2 || g_state.status < 0; });

        if (!started || g_state.status < 0)
        {
            return -1;
        }

        g_state.request = reading;
        sequence = ++g_state.sequence;
    }

    g_state.requestReady.notify_all();

    std::unique_lock<std::mutex> lock(g_state.mutex);
    bool answered = g_state.replyReady.wait_for(
        lock, std::chrono::milliseconds(kConvertTimeoutMilliseconds),
        [sequence] { return g_state.answered == sequence; });

    if (!answered)
    {
        // The engine stopped answering; remember it so the plugin can fall back
        // to the helper process instead of blocking every key press.
        g_state.status = -4;
        return -1;
    }

    size_t length = g_state.reply.size();
    if (length > static_cast<size_t>(bufferLength) - 1)
    {
        length = static_cast<size_t>(bufferLength) - 1;
    }

    if (length > 0)
    {
        std::memcpy(buffer, g_state.reply.data(), length * sizeof(wchar_t));
    }

    buffer[length] = L'\0';
    return static_cast<int>(length);
}

extern "C" __declspec(dllexport) void JimeShutdown()
{
    StopWorker(true);
}
