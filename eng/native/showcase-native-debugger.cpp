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

DWORD write_minidump(HANDLE process, DWORD pid, const std::filesystem::path& path,
                     MINIDUMP_EXCEPTION_INFORMATION* exception) {
    handle file(CreateFileW(path.c_str(), GENERIC_WRITE | GENERIC_READ, 0, nullptr,
                            CREATE_NEW, FILE_ATTRIBUTE_NORMAL, nullptr));
    if (!file.valid()) return GetLastError();
    dump_budget budget{file.value, GetTickCount64() + 5000};
    MINIDUMP_CALLBACK_INFORMATION callback{check_dump_budget, &budget};
    if (!MiniDumpWriteDump(process, pid, file.value, MiniDumpNormal,
                           exception, nullptr, &callback)) return GetLastError();
    LARGE_INTEGER size{};
    if (!GetFileSizeEx(file.value, &size)) return GetLastError();
    return size.QuadPart >= 32 && size.QuadPart <= maximum_dump_bytes ? ERROR_SUCCESS : ERROR_FILE_TOO_LARGE;
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
    return write_minidump(process, event.dwProcessId, path, &exception);
}

bool owned_name(const std::filesystem::path& app, bool modal) {
    const auto name = app.filename().wstring();
    if (!name.starts_with(modal ? L"ModalStartup-" : L"ShowcaseIdle-") || !name.ends_with(L".exe") || name.size() != 49) return false;
    for (std::size_t i = 13; i < 45; ++i)
        if (!((name[i] >= L'0' && name[i] <= L'9') || (name[i] >= L'a' && name[i] <= L'f'))) return false;
    return true;
}

int run(const std::filesystem::path& app, const std::filesystem::path& raw,
        const std::filesystem::path& receipt, DWORD snapshot_after_ms,
        const std::filesystem::path& snapshot, const std::filesystem::path& first_av,
        const std::filesystem::path& modal_directory, const std::wstring& modal_run, bool first_av_low) {
    const bool modal = !modal_directory.empty() || !modal_run.empty();
    if (first_av_low && (!modal || first_av.empty())) return 2;
    if (modal && (!modal_directory.is_absolute() || std::filesystem::exists(modal_directory) ||
        !std::filesystem::is_directory(modal_directory.parent_path()) ||
        modal_directory.wstring().find_first_of(L"\"\r\n") != std::wstring::npos ||
        modal_run.size() != 32 || modal_run.find_first_not_of(L"0123456789abcdef") != std::wstring::npos)) return 2;
    const ULONGLONG budget_ms = modal ? 60000 : child_deadline_ms;
    if (!app.is_absolute() || !raw.is_absolute() || !receipt.is_absolute() ||
        !owned_name(app, modal) || !std::filesystem::is_regular_file(app) ||
        !std::filesystem::is_directory(raw) || !std::filesystem::is_empty(raw)) return 2;
    if (snapshot_after_ms != 0 &&
        (!snapshot.is_absolute() || snapshot.filename() != L"native-stack.dmp" ||
         !std::filesystem::is_directory(snapshot.parent_path()) ||
         !std::filesystem::is_empty(snapshot.parent_path()) ||
         std::filesystem::equivalent(snapshot.parent_path(), raw))) return 2;
    if (!first_av.empty() &&
        (!first_av.is_absolute() || first_av.filename() != L"first-chance-av.dmp" ||
         !std::filesystem::is_directory(first_av.parent_path()) ||
         !std::filesystem::is_empty(first_av.parent_path()) ||
         std::filesystem::equivalent(first_av.parent_path(), raw) ||
         (snapshot_after_ms != 0 &&
          std::filesystem::equivalent(first_av.parent_path(), snapshot.parent_path())))) return 2;
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
    if (modal) command += L" --evidence-directory \"" + modal_directory.wstring() + L"\" --run-id " + modal_run;
    if (!CreateProcessW(app.c_str(), command.data(), nullptr, nullptr, TRUE,
        DEBUG_ONLY_THIS_PROCESS | CREATE_SUSPENDED, nullptr, modal ? nullptr : app.parent_path().c_str(),
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
    bool snapshot_attempted = false;
    DWORD snapshot_error = 0;
    ULONGLONG snapshot_elapsed_ms = 0;
    bool first_av_attempted = false;
    DWORD first_av_error = 0, first_av_thread = 0;
    std::uintptr_t first_av_instruction = 0, first_av_address = 0, first_av_operation = 0;
    ULONGLONG first_av_elapsed_ms = 0;
    const ULONGLONG deadline = GetTickCount64() + budget_ms;
    while (!exit_event_seen && GetTickCount64() < deadline) {
        const auto elapsed = GetTickCount64() - (deadline - budget_ms);
        if (snapshot_after_ms != 0 && !snapshot_attempted && elapsed >= snapshot_after_ms) {
            snapshot_attempted = true;
            snapshot_elapsed_ms = elapsed;
            // External, live-process snapshot, not an exception. Never inject a
            // breakpoint, change a register or call into the renderer. DbgHelp
            // runs only here on the debugger thread, outside a pending event.
            snapshot_error = write_minidump(process.value, created.dwProcessId, snapshot, nullptr);
        }
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
                // Capture at most one real AV before the CLR can
                // handle it and terminate without a second-chance event.
                // Original replay excludes low addresses; the explicit modal
                // diagnostic can retain them without changing exception handling.
                // This is diagnostic evidence, including if the app recovers;
                // it never changes normal exception handling or crash status.
                if (!first_av.empty() && !first_av_attempted && GetTickCount64() < deadline &&
                    event.u.Exception.dwFirstChance &&
                    event.u.Exception.ExceptionRecord.ExceptionCode == EXCEPTION_ACCESS_VIOLATION &&
                    event.u.Exception.ExceptionRecord.NumberParameters == 2 &&
                    (first_av_low || event.u.Exception.ExceptionRecord.ExceptionInformation[1] > 0x10000) &&
                    (event.u.Exception.ExceptionRecord.ExceptionInformation[0] == 0 ||
                     event.u.Exception.ExceptionRecord.ExceptionInformation[0] == 1 ||
                     event.u.Exception.ExceptionRecord.ExceptionInformation[0] == 8)) {
                    first_av_attempted = true;
                    first_av_thread = event.dwThreadId;
                    first_av_instruction = reinterpret_cast<std::uintptr_t>(event.u.Exception.ExceptionRecord.ExceptionAddress);
                    first_av_address = event.u.Exception.ExceptionRecord.ExceptionInformation[1];
                    first_av_operation = event.u.Exception.ExceptionRecord.ExceptionInformation[0];
                    first_av_elapsed_ms = GetTickCount64() - (deadline - budget_ms);
                    first_av_error = write_dump(process.value, event, first_av);
                }
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
    const std::string json = "{\"schemaVersion\":3,\"diagnosticOnly\":true,\"processId\":" +
        std::to_string(created.dwProcessId) + ",\"machine\":" + std::to_string(machine) +
        ",\"exitCode\":" + std::to_string(exit_code) + ",\"exited\":" + (exited ? "true" : "false") +
        ",\"exceptionCode\":" + std::to_string(exception_code) + ",\"exceptionThreadId\":" +
        std::to_string(thread_id) + ",\"exceptionAddress\":" + std::to_string(exception_address) +
        ",\"captured\":" + (captured ? "true" : "false") + ",\"dumpError\":" +
        std::to_string(dump_error) + ",\"loopError\":" + std::to_string(loop_error) +
        ",\"snapshotAfterMs\":" + std::to_string(snapshot_after_ms) +
        ",\"snapshotAttempted\":" + (snapshot_attempted ? "true" : "false") +
        ",\"snapshotElapsedMs\":" + std::to_string(snapshot_elapsed_ms) +
        ",\"snapshotError\":" + std::to_string(snapshot_error) +
        ",\"firstChanceAvEnabled\":" + (first_av.empty() ? "false" : "true") +
        ",\"firstChanceAvIncludesLowAddresses\":" + (first_av_low ? "true" : "false") +
        ",\"firstChanceAvAttempted\":" + (first_av_attempted ? "true" : "false") +
        ",\"firstChanceAvThreadId\":" + std::to_string(first_av_thread) +
        ",\"firstChanceAvInstruction\":" + std::to_string(first_av_instruction) +
        ",\"firstChanceAvAddress\":" + std::to_string(first_av_address) +
        ",\"firstChanceAvOperation\":" + std::to_string(first_av_operation) +
        ",\"firstChanceAvElapsedMs\":" + std::to_string(first_av_elapsed_ms) +
        ",\"firstChanceAvDumpError\":" + std::to_string(first_av_error) + "}\n";
    DWORD written = 0;
    if (json.size() > 4096) return 2;
    if (!WriteFile(report.value, json.data(), static_cast<DWORD>(json.size()), &written, nullptr) ||
        written != json.size() || !FlushFileBuffers(report.value)) return 2;
    return static_cast<int>(loop_error ? 2 : exit_code);
}
}

int wmain(int argc, wchar_t** argv) {
    if (argc == 2 && std::wstring(argv[1]) == L"--test-callbacks") return callback_controls() ? 0 : 1;
    if (argc < 4 || argc > 12 || argc % 2 != 0) return 2;
    try {
        DWORD delay = 0;
        std::filesystem::path snapshot, first_av, modal_directory;
        std::wstring modal_run;
        bool first_av_low = false;
        for (int i = 4; i < argc; i += 2) {
            const std::wstring argument(argv[i]);
            if (argument == L"--modal-evidence-directory") {
                if (!modal_directory.empty()) return 2;
                modal_directory = argv[i + 1];
                if (modal_directory.empty()) return 2;
                continue;
            }
            if (argument == L"--modal-run-id") {
                if (!modal_run.empty()) return 2;
                modal_run = argv[i + 1];
                if (modal_run.empty()) return 2;
                continue;
            }
            if (argument == L"--first-chance-av" || argument == L"--first-chance-av-including-low") {
                if (!first_av.empty()) return 2;
                first_av_low = argument == L"--first-chance-av-including-low";
                first_av = argv[i + 1];
                if (first_av.empty()) return 2;
                continue;
            }
            const std::wstring prefix = L"--snapshot-after-ms=";
            if (!argument.starts_with(prefix) || delay != 0) return 2;
            const auto value = argument.substr(prefix.size());
            if (value.empty() || value.size() > 5 || value.find_first_not_of(L"0123456789") != std::wstring::npos) return 2;
            delay = std::stoul(value);
            if (delay < 100 || delay > 60000) return 2;
            snapshot = argv[i + 1];
        }
        return run(argv[1], argv[2], argv[3], delay, snapshot, first_av, modal_directory, modal_run, first_av_low);
    }
    catch (...) { return 2; } // Job/handle scope still releases the owned child.
}
