// Isolated debugger controls, never linked into a product/application payload.
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <cstdint>
#include <intrin.h>

int wmain(int argc, wchar_t** argv) {
    wchar_t mode[32]{};
    if (GetEnvironmentVariableW(L"SHOWCASE_DEBUGGER_FIXTURE", mode, 32) == 0) return 2;
    if (lstrcmpW(mode, L"exit") == 0) return 17;
    if (lstrcmpW(mode, L"fail-fast") == 0) __fastfail(FAST_FAIL_FATAL_APP_EXIT);
    if (lstrcmpW(mode, L"modal-arguments") == 0) {
        wchar_t expected[MAX_PATH]{};
        if (GetEnvironmentVariableW(L"SHOWCASE_DEBUGGER_EXPECTED_DIRECTORY", expected, MAX_PATH) == 0) return 2;
        return argc == 5 && lstrcmpW(argv[1], L"--evidence-directory") == 0 &&
            lstrcmpW(argv[2], expected) == 0 && lstrcmpW(argv[3], L"--run-id") == 0 &&
            lstrcmpW(argv[4], L"0123456789abcdef0123456789abcdef") == 0 ? 17 : 2;
    }
    if (lstrcmpW(mode, L"live-stack") == 0) {
        // An owned live thread remains available for the external snapshot;
        // ordinary exit must remain ordinary, without a fabricated exception.
        Sleep(1500);
        return 17;
    }
    if (lstrcmpW(mode, L"handled") == 0) {
        __try { RaiseException(0xE0424242, 0, 0, nullptr); }
        __except (EXCEPTION_EXECUTE_HANDLER) { return 0; }
    }
    if (lstrcmpW(mode, L"access-violation") == 0) {
        *reinterpret_cast<volatile unsigned char*>(static_cast<std::uintptr_t>(1)) = 0;
        return 3;
    }
    if (lstrcmpW(mode, L"unhandled-nonnull-av") == 0) {
        void* page = VirtualAlloc(nullptr, 4096, MEM_COMMIT | MEM_RESERVE, PAGE_NOACCESS);
        if (!page) return 2;
        *static_cast<volatile unsigned char*>(page) = 0;
        VirtualFree(page, 0, MEM_RELEASE);
        return 3;
    }
    if (lstrcmpW(mode, L"handled-nonnull-av") == 0) {
        void* page = VirtualAlloc(nullptr, 4096, MEM_COMMIT | MEM_RESERVE, PAGE_NOACCESS);
        if (!page) return 2;
        __try { *static_cast<volatile unsigned char*>(page) = 0; }
        __except (EXCEPTION_EXECUTE_HANDLER) {
            return VirtualFree(page, 0, MEM_RELEASE) ? 0 : 2;
        }
        VirtualFree(page, 0, MEM_RELEASE);
        return 3;
    }
    return 2;
}
