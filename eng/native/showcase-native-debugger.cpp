// Original LibreWPF diagnostic: one owned child, no attach/global crash policy.
// SDK structures supply the actual x64/ARM64 CONTEXT and DEBUG_EVENT ABI.
#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#define _WIN32_WINNT 0x0A00
#include <windows.h>
#include <dbghelp.h>
#include <cstdint>
#include <filesystem>
#include <initializer_list>
#include <string>
#include <type_traits>

namespace {
constexpr ULONGLONG child_deadline_ms = 110000;
constexpr LONGLONG maximum_dump_bytes = 32 * 1024 * 1024;
#if defined(_M_ARM64)
constexpr USHORT machine = IMAGE_FILE_MACHINE_ARM64;
#elif defined(_M_X64)
constexpr USHORT machine = IMAGE_FILE_MACHINE_AMD64;
#else
#error This diagnostic supports native Windows ARM64 and x64 only.
#endif

struct handle {
    HANDLE value = nullptr;
    handle() = default;
    explicit handle(HANDLE input) noexcept : value(input) {}
    handle(const handle&) = delete;
    handle& operator=(const handle&) = delete;
    ~handle() { if (value && value != INVALID_HANDLE_VALUE) CloseHandle(value); }
    bool valid() const noexcept { return value && value != INVALID_HANDLE_VALUE; }
};

struct dump_budget { HANDLE file; ULONGLONG deadline; };
BOOL CALLBACK check_dump_budget(void* parameter, PMINIDUMP_CALLBACK_INPUT input,
                               MINIDUMP_CALLBACK_OUTPUT* output) {
    switch (input->CallbackType) {
        case ModuleCallback:
        case ThreadCallback:
        case ThreadExCallback:
        case IncludeThreadCallback:
        case IncludeModuleCallback:
            return TRUE; // Preserve the SDK's default write flags and inclusion.
        case CancelCallback: {
            const auto& budget = *static_cast<const dump_budget*>(parameter);
            LARGE_INTEGER size{};
            output->CheckCancel = TRUE;
            output->Cancel = GetTickCount64() >= budget.deadline ||
                !GetFileSizeEx(budget.file, &size) || size.QuadPart > maximum_dump_bytes;
            return TRUE;
        }
        case VmStartCallback:
            output->Status = S_OK; // No alternate virtual-memory reader.
            return TRUE;
        default:
            // Do not opt into alternate I/O, snapshot handles, extra memory or
            // kernel dumps, and never acknowledge an ignored memory-read error.
            return FALSE;
    }
}
static_assert(std::is_same_v<decltype(&check_dump_budget), MINIDUMP_CALLBACK_ROUTINE>);

bool callback_controls() {
    MINIDUMP_CALLBACK_INPUT input{};
    MINIDUMP_CALLBACK_OUTPUT output{};
    constexpr ULONG sentinel = 0x12345678;
    for (const auto kind : {ModuleCallback, ThreadCallback, ThreadExCallback,
                           IncludeThreadCallback, IncludeModuleCallback}) {
        input.CallbackType = kind;
        output.ModuleWriteFlags = sentinel;
        if (!check_dump_budget(nullptr, &input, &output) || output.ModuleWriteFlags != sentinel) return false;
    }
    for (const auto kind : {MemoryCallback, WriteKernelMinidumpCallback, RemoveMemoryCallback,
                           IoStartCallback, IoWriteAllCallback, IoFinishCallback,
                           ReadMemoryFailureCallback, IsProcessSnapshotCallback, SecondaryFlagsCallback}) {
        input.CallbackType = kind;
        output.ModuleWriteFlags = sentinel;
        if (check_dump_budget(nullptr, &input, &output) || output.ModuleWriteFlags != sentinel) return false;
    }
    input.CallbackType = MAXDWORD;
    if (check_dump_budget(nullptr, &input, &output) || output.ModuleWriteFlags != sentinel) return false;
    input.CallbackType = VmStartCallback;
    output.Status = E_NOTIMPL;
    if (!check_dump_budget(nullptr, &input, &output) || output.Status != S_OK) return false;
    input.CallbackType = CancelCallback;
    dump_budget expired{INVALID_HANDLE_VALUE, 0};
    return check_dump_budget(&expired, &input, &output) && output.CheckCancel && output.Cancel;
}

DWORD write_dump(HANDLE process, const DEBUG_EVENT& event, const std::filesystem::path& path) {
    handle thread(OpenThread(THREAD_GET_CONTEXT | THREAD_QUERY_INFORMATION, FALSE, event.dwThreadId));
    if (!thread.valid()) return GetLastError();
    if (GetProcessIdOfThread(thread.value) != event.dwProcessId) return ERROR_INVALID_DATA;
    CONTEXT context{};
    context.ContextFlags = CONTEXT_FULL;
    // The uncontinued debug event suspends the owned process's threads.
    if (!GetThreadContext(thread.value, &context)) return GetLastError();
    EXCEPTION_RECORD record = event.u.Exception.ExceptionRecord;
    // ClientPointers=FALSE requires debugger-owned pointers, not a foreign
    // chained record. Do not dereference or mislabel a remote exception chain.
    if (record.ExceptionRecord != nullptr) return ERROR_NOT_SUPPORTED;
    EXCEPTION_POINTERS pointers{&record, &context};
    MINIDUMP_EXCEPTION_INFORMATION exception{event.dwThreadId, &pointers, FALSE};
    handle file(CreateFileW(path.c_str(), GENERIC_WRITE | GENERIC_READ, 0, nullptr,
                            CREATE_NEW, FILE_ATTRIBUTE_NORMAL, nullptr));
    if (!file.valid()) return GetLastError();
    dump_budget budget{file.value, GetTickCount64() + 5000};
    MINIDUMP_CALLBACK_INFORMATION callback{check_dump_budget, &budget};
    if (!MiniDumpWriteDump(process, event.dwProcessId, file.value, MiniDumpNormal,
                           &exception, nullptr, &callback)) return GetLastError();
    LARGE_INTEGER size{};
    if (!GetFileSizeEx(file.value, &size)) return GetLastError();
    return size.QuadPart >= 32 && size.QuadPart <= maximum_dump_bytes ? ERROR_SUCCESS : ERROR_FILE_TOO_LARGE;
}

bool owned_name(const std::filesystem::path& app) {
    const auto name = app.filename().wstring();
    if (!name.starts_with(L"ShowcaseIdle-") || !name.ends_with(L".exe") || name.size() != 49) return false;
    for (std::size_t i = 13; i < 45; ++i)
        if (!((name[i] >= L'0' && name[i] <= L'9') || (name[i] >= L'a' && name[i] <= L'f'))) return false;
    return true;
}

int run(const std::filesystem::path& app, const std::filesystem::path& raw,
        const std::filesystem::path& receipt) {
    if (!app.is_absolute() || !raw.is_absolute() || !receipt.is_absolute() ||
        !owned_name(app) || !std::filesystem::is_regular_file(app) ||
        !std::filesystem::is_directory(raw) || !std::filesystem::is_empty(raw)) return 2;
    handle report(CreateFileW(receipt.c_str(), GENERIC_WRITE, 0, nullptr, CREATE_NEW,
                              FILE_ATTRIBUTE_NORMAL, nullptr));
    if (!report.valid()) return 2;
    handle job(CreateJobObjectW(nullptr, nullptr));
    JOBOBJECT_EXTENDED_LIMIT_INFORMATION limits{};
    limits.BasicLimitInformation.LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;
    if (!job.valid() || !SetInformationJobObject(job.value, JobObjectExtendedLimitInformation,
                                                &limits, sizeof(limits))) return 2;
    STARTUPINFOW startup{};
    startup.cb = sizeof(startup);
    startup.dwFlags = STARTF_USESTDHANDLES;
    startup.hStdInput = GetStdHandle(STD_INPUT_HANDLE);
    startup.hStdOutput = GetStdHandle(STD_OUTPUT_HANDLE);
    startup.hStdError = GetStdHandle(STD_ERROR_HANDLE);
    PROCESS_INFORMATION created{};
    std::wstring command = L"\"" + app.wstring() + L"\"";
    if (!CreateProcessW(app.c_str(), command.data(), nullptr, nullptr, TRUE,
        DEBUG_ONLY_THIS_PROCESS | CREATE_SUSPENDED, nullptr, app.parent_path().c_str(),
        &startup, &created)) return 2;
    handle process(created.hProcess), initial_thread(created.hThread);
    if (!AssignProcessToJobObject(job.value, process.value)) {
        TerminateProcess(process.value, 2); // Only the child just created, still suspended.
        return 2;
    }
    USHORT emulated{}, native{};
    if (!IsWow64Process2(process.value, &emulated, &native) || emulated != IMAGE_FILE_MACHINE_UNKNOWN ||
        native != machine || ResumeThread(initial_thread.value) == static_cast<DWORD>(-1)) return 2;
    DWORD exit_code = 124, exception_code = 0, thread_id = 0, dump_error = 0, loop_error = 0;
    std::uintptr_t exception_address = 0;
    bool captured = false, exited = false, exit_event_seen = false, loader_breakpoint = false;
    const ULONGLONG deadline = GetTickCount64() + child_deadline_ms;
    while (!exit_event_seen && GetTickCount64() < deadline) {
        DEBUG_EVENT event{};
        if (!WaitForDebugEventEx(&event, 100)) {
            const DWORD error = GetLastError();
            if (error == ERROR_SEM_TIMEOUT) continue;
            loop_error = error;
            break;
        }
        DWORD continuation = DBG_CONTINUE;
        if (event.dwProcessId != created.dwProcessId) { loop_error = ERROR_INVALID_DATA; break; }
        switch (event.dwDebugEventCode) {
            case CREATE_PROCESS_DEBUG_EVENT:
                if (event.u.CreateProcessInfo.hFile && event.u.CreateProcessInfo.hFile != INVALID_HANDLE_VALUE)
                    CloseHandle(event.u.CreateProcessInfo.hFile);
                break;
            case LOAD_DLL_DEBUG_EVENT:
                if (event.u.LoadDll.hFile && event.u.LoadDll.hFile != INVALID_HANDLE_VALUE)
                    CloseHandle(event.u.LoadDll.hFile);
                break;
            case EXCEPTION_DEBUG_EVENT:
                continuation = DBG_EXCEPTION_NOT_HANDLED;
                if (!loader_breakpoint && event.u.Exception.dwFirstChance &&
                    event.u.Exception.ExceptionRecord.ExceptionCode == EXCEPTION_BREAKPOINT) {
                    loader_breakpoint = true;
                    continuation = DBG_CONTINUE;
                } else if (!event.u.Exception.dwFirstChance && exception_code == 0) {
                    exception_code = event.u.Exception.ExceptionRecord.ExceptionCode;
                    exception_address = reinterpret_cast<std::uintptr_t>(event.u.Exception.ExceptionRecord.ExceptionAddress);
                    thread_id = event.dwThreadId;
                    const auto name = app.filename().wstring() + L"." + std::to_wstring(created.dwProcessId) + L".dmp";
                    dump_error = write_dump(process.value, event, raw / name);
                    captured = dump_error == ERROR_SUCCESS;
                }
                break;
            case EXIT_PROCESS_DEBUG_EVENT:
                exit_code = event.u.ExitProcess.dwExitCode;
                exit_event_seen = true;
                break;
            default: break;
        }
        if (!ContinueDebugEvent(event.dwProcessId, event.dwThreadId, continuation)) {
            loop_error = GetLastError();
            break;
        }
    }
    // Debug-owned process/thread event handles are closed by Windows on exit
    // continuation. The separate CreateProcess handles above remain caller-owned.
    // An exit event is not the process-object signal. Observe actual termination
    // before the caller removes the unique apphost, using only the remainder of
    // the original child deadline (not a fresh cleanup timeout).
    if (exit_event_seen && loop_error == 0) {
        const ULONGLONG now = GetTickCount64();
        const DWORD remaining = now < deadline ? static_cast<DWORD>(deadline - now) : 0;
        const DWORD wait = WaitForSingleObject(process.value, remaining);
        if (wait == WAIT_OBJECT_0) {
            DWORD actual_exit_code = 0;
            if (!GetExitCodeProcess(process.value, &actual_exit_code)) loop_error = GetLastError();
            else if (actual_exit_code != exit_code) loop_error = ERROR_INVALID_DATA;
            else exited = true;
        } else {
            loop_error = wait == WAIT_TIMEOUT ? ERROR_TIMEOUT :
                (wait == WAIT_FAILED ? GetLastError() : ERROR_INVALID_DATA);
        }
    }
    // Closing our job also retires the child if the debugger deadline/API failed.
    const std::string json = "{\"schemaVersion\":1,\"diagnosticOnly\":true,\"processId\":" +
        std::to_string(created.dwProcessId) + ",\"machine\":" + std::to_string(machine) +
        ",\"exitCode\":" + std::to_string(exit_code) + ",\"exited\":" + (exited ? "true" : "false") +
        ",\"exceptionCode\":" + std::to_string(exception_code) + ",\"exceptionThreadId\":" +
        std::to_string(thread_id) + ",\"exceptionAddress\":" + std::to_string(exception_address) +
        ",\"captured\":" + (captured ? "true" : "false") + ",\"dumpError\":" +
        std::to_string(dump_error) + ",\"loopError\":" + std::to_string(loop_error) + "}\n";
    DWORD written = 0;
    if (json.size() > 4096) return 2;
    if (!WriteFile(report.value, json.data(), static_cast<DWORD>(json.size()), &written, nullptr) ||
        written != json.size() || !FlushFileBuffers(report.value)) return 2;
    return static_cast<int>(loop_error ? 2 : exit_code);
}
}

int wmain(int argc, wchar_t** argv) {
    if (argc == 2 && std::wstring(argv[1]) == L"--test-callbacks") return callback_controls() ? 0 : 1;
    if (argc != 4) return 2;
    try { return run(argv[1], argv[2], argv[3]); }
    catch (...) { return 2; } // Job/handle scope still releases the owned child.
}
