// Isolated debugger controls, never linked into a product/application payload.
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <cstdint>

int main() {
    wchar_t mode[32]{};
    if (GetEnvironmentVariableW(L"SHOWCASE_DEBUGGER_FIXTURE", mode, 32) == 0) return 2;
    if (lstrcmpW(mode, L"exit") == 0) return 17;
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
