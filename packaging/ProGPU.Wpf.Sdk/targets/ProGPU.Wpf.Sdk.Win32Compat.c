#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <time.h>

#if defined(__APPLE__)
#include <pthread.h>
#include <unistd.h>
#elif defined(__linux__)
#include <sys/syscall.h>
#include <unistd.h>
#endif

#if defined(__GNUC__) || defined(__clang__)
#define PROGPU_EXPORT __attribute__((visibility("default")))
#else
#define PROGPU_EXPORT
#endif

typedef intptr_t progpu_intptr;

static progpu_intptr progpu_next_fake_handle = 2;

#define PROGPU_GWL_WNDPROC (-4)
#define PROGPU_GWL_HINSTANCE (-6)
#define PROGPU_GWL_HWNDPARENT (-8)
#define PROGPU_GWL_ID (-12)
#define PROGPU_GWL_STYLE (-16)
#define PROGPU_GWL_EXSTYLE (-20)
#define PROGPU_GWL_USERDATA (-21)
#define PROGPU_DEFAULT_STYLE ((progpu_intptr)0x10CF0000)
#define PROGPU_DEFAULT_EX_STYLE ((progpu_intptr)0x00000100)
#define PROGPU_DEF_WINDOW_PROC_A ((progpu_intptr)0x10001)
#define PROGPU_DEF_WINDOW_PROC_W ((progpu_intptr)0x10002)
#define PROGPU_FAKE_WINDOW_STATE_COUNT 256

typedef struct progpu_window_state
{
    progpu_intptr window;
    progpu_intptr style;
    progpu_intptr ex_style;
    progpu_intptr wnd_proc;
    progpu_intptr instance;
    progpu_intptr parent;
    progpu_intptr id;
    progpu_intptr user_data;
    int32_t x;
    int32_t y;
    int32_t width;
    int32_t height;
    int32_t visible;
} progpu_window_state;

static progpu_window_state progpu_fake_window_states[PROGPU_FAKE_WINDOW_STATE_COUNT];
static progpu_intptr progpu_active_window = 0;
static progpu_intptr progpu_capture_window = 0;
static progpu_intptr progpu_current_cursor = 0;
static int32_t progpu_cursor_show_count = 0;

static progpu_intptr progpu_allocate_fake_handle(void)
{
    return progpu_next_fake_handle++;
}

static void progpu_initialize_window_state(progpu_window_state* state, progpu_intptr window)
{
    state->window = window;
    state->style = PROGPU_DEFAULT_STYLE;
    state->ex_style = PROGPU_DEFAULT_EX_STYLE;
    state->wnd_proc = 1;
    state->instance = 1;
    state->parent = 0;
    state->id = 1;
    state->user_data = 1;
    state->x = 0;
    state->y = 0;
    state->width = 0;
    state->height = 0;
    state->visible = 0;
}

static progpu_window_state* progpu_get_window_state(progpu_intptr window, int create)
{
    if (window == 0)
    {
        return 0;
    }

    progpu_window_state* empty = 0;
    for (int i = 0; i < PROGPU_FAKE_WINDOW_STATE_COUNT; ++i)
    {
        progpu_window_state* state = &progpu_fake_window_states[i];
        if (state->window == window)
        {
            return state;
        }

        if (empty == 0 && state->window == 0)
        {
            empty = state;
        }
    }

    if (!create || empty == 0)
    {
        return 0;
    }

    progpu_initialize_window_state(empty, window);
    return empty;
}

static progpu_intptr progpu_get_default_window_long(int32_t index)
{
    switch (index)
    {
        case PROGPU_GWL_STYLE: return PROGPU_DEFAULT_STYLE;
        case PROGPU_GWL_EXSTYLE: return PROGPU_DEFAULT_EX_STYLE;
        case PROGPU_GWL_WNDPROC: return 1;
        case PROGPU_GWL_HINSTANCE: return 1;
        case PROGPU_GWL_ID: return 1;
        case PROGPU_GWL_USERDATA: return 1;
        default: return 1;
    }
}

static progpu_intptr progpu_get_window_long_value(progpu_intptr window, int32_t index)
{
    progpu_window_state* state = progpu_get_window_state(window, 0);
    if (state == 0)
    {
        return progpu_get_default_window_long(index);
    }

    switch (index)
    {
        case PROGPU_GWL_STYLE: return state->style;
        case PROGPU_GWL_EXSTYLE: return state->ex_style;
        case PROGPU_GWL_WNDPROC: return state->wnd_proc;
        case PROGPU_GWL_HINSTANCE: return state->instance;
        case PROGPU_GWL_HWNDPARENT: return state->parent;
        case PROGPU_GWL_ID: return state->id;
        case PROGPU_GWL_USERDATA: return state->user_data;
        default: return progpu_get_default_window_long(index);
    }
}

static progpu_intptr progpu_set_window_long_value(progpu_intptr window, int32_t index, progpu_intptr value)
{
    progpu_window_state* state = progpu_get_window_state(window, 1);
    progpu_intptr previous = progpu_get_window_long_value(window, index);
    if (state == 0)
    {
        return previous;
    }

    switch (index)
    {
        case PROGPU_GWL_STYLE:
            state->style = value == 0 ? PROGPU_DEFAULT_STYLE : value;
            break;
        case PROGPU_GWL_EXSTYLE:
            state->ex_style = value == 0 ? PROGPU_DEFAULT_EX_STYLE : value;
            break;
        case PROGPU_GWL_WNDPROC:
            state->wnd_proc = value == 0 ? 1 : value;
            break;
        case PROGPU_GWL_HINSTANCE:
            state->instance = value == 0 ? 1 : value;
            break;
        case PROGPU_GWL_HWNDPARENT:
            state->parent = value;
            break;
        case PROGPU_GWL_ID:
            state->id = value == 0 ? 1 : value;
            break;
        case PROGPU_GWL_USERDATA:
            state->user_data = value == 0 ? 1 : value;
            break;
    }

    return previous;
}

typedef struct progpu_rect
{
    int32_t left;
    int32_t top;
    int32_t right;
    int32_t bottom;
} progpu_rect;

typedef struct progpu_point
{
    int32_t x;
    int32_t y;
} progpu_point;

typedef struct progpu_open_file_name_a
{
    int32_t struct_size;
    progpu_intptr owner;
    progpu_intptr instance;
    char* filter;
    char* custom_filter;
    int32_t max_cust_filter;
    int32_t filter_index;
    char* file;
    int32_t max_file;
    char* file_title;
    int32_t max_file_title;
    char* initial_dir;
    char* title;
    int32_t flags;
    int16_t file_offset;
    int16_t file_extension;
    char* def_ext;
    progpu_intptr cust_data;
    progpu_intptr hook;
    char* template_name;
} progpu_open_file_name_a;

typedef struct progpu_open_file_name_w
{
    int32_t struct_size;
    progpu_intptr owner;
    progpu_intptr instance;
    uint16_t* filter;
    uint16_t* custom_filter;
    int32_t max_cust_filter;
    int32_t filter_index;
    uint16_t* file;
    int32_t max_file;
    uint16_t* file_title;
    int32_t max_file_title;
    uint16_t* initial_dir;
    uint16_t* title;
    int32_t flags;
    int16_t file_offset;
    int16_t file_extension;
    uint16_t* def_ext;
    progpu_intptr cust_data;
    progpu_intptr hook;
    uint16_t* template_name;
} progpu_open_file_name_w;

static void progpu_copy_dialog_title(char* destination, size_t destination_count, const char* title, const char* fallback)
{
    if (destination == 0 || destination_count == 0)
    {
        return;
    }

    const char* source = title != 0 && title[0] != 0 ? title : fallback;
    size_t position = 0;
    for (; source[position] != 0 && position + 2 < destination_count; ++position)
    {
        char value = source[position];
        if (value == '"' || value == '\\')
        {
            destination[position++] = '\\';
            if (position + 1 >= destination_count)
            {
                break;
            }
        }

        destination[position] = value;
    }

    destination[position] = 0;
}

static void progpu_trim_process_output(char* value)
{
    if (value == 0)
    {
        return;
    }

    size_t length = strlen(value);
    while (length > 0)
    {
        char c = value[length - 1];
        if (c != '\r' && c != '\n' && c != ' ' && c != '\t')
        {
            break;
        }

        value[--length] = 0;
    }
}

static int32_t progpu_run_dialog_process(int save, const char* title, char* result, size_t result_count)
{
    if (result == 0 || result_count == 0)
    {
        return 0;
    }

    result[0] = 0;

    const char* forced_result = getenv(save ? "PROGPU_WPF_WIN32_SAVE_FILE_NAME" : "PROGPU_WPF_WIN32_OPEN_FILE_NAME");
    if (forced_result == 0 || forced_result[0] == 0)
    {
        forced_result = getenv("PROGPU_WPF_WIN32_FILE_DIALOG_RESULT");
    }

    if (forced_result != 0 && forced_result[0] != 0)
    {
        strncpy(result, forced_result, result_count - 1);
        result[result_count - 1] = 0;
        return 1;
    }

#if defined(__APPLE__)
    char escaped_title[512];
    progpu_copy_dialog_title(escaped_title, sizeof(escaped_title), title, save ? "Save File" : "Open File");

    char command[1024];
    if (save)
    {
        snprintf(
            command,
            sizeof(command),
            "osascript -e 'POSIX path of (choose file name default name \"untitled\" with prompt \"%s\")' 2>/dev/null",
            escaped_title);
    }
    else
    {
        snprintf(
            command,
            sizeof(command),
            "osascript -e 'POSIX path of (choose file with prompt \"%s\")' 2>/dev/null",
            escaped_title);
    }

    FILE* pipe = popen(command, "r");
    if (pipe == 0)
    {
        return 0;
    }

    char* read_result = fgets(result, (int)result_count, pipe);
    int status = pclose(pipe);
    if (read_result == 0 || status != 0)
    {
        result[0] = 0;
        return 0;
    }

    progpu_trim_process_output(result);
    return result[0] != 0;
#else
    (void)save;
    (void)title;
    return 0;
#endif
}

static void progpu_set_file_offsets_a(progpu_open_file_name_a* ofn, const char* path)
{
    const char* file_name = strrchr(path, '/');
    file_name = file_name == 0 ? path : file_name + 1;
    ofn->file_offset = (int16_t)(file_name - path);

    const char* extension = strrchr(file_name, '.');
    ofn->file_extension = extension == 0 ? 0 : (int16_t)(extension - path + 1);
}

static int32_t progpu_write_open_file_name_a(progpu_open_file_name_a* ofn, const char* path)
{
    if (ofn == 0 || ofn->file == 0 || ofn->max_file <= 0 || path == 0 || path[0] == 0)
    {
        return 0;
    }

    strncpy(ofn->file, path, (size_t)ofn->max_file - 1);
    ofn->file[ofn->max_file - 1] = 0;
    progpu_set_file_offsets_a(ofn, ofn->file);
    return 1;
}

static int32_t progpu_write_open_file_name_w(progpu_open_file_name_w* ofn, const char* path)
{
    if (ofn == 0 || ofn->file == 0 || ofn->max_file <= 0 || path == 0 || path[0] == 0)
    {
        return 0;
    }

    int32_t i = 0;
    for (; i + 1 < ofn->max_file && path[i] != 0; ++i)
    {
        ofn->file[i] = (uint16_t)(unsigned char)path[i];
    }

    ofn->file[i] = 0;

    const char* file_name = strrchr(path, '/');
    file_name = file_name == 0 ? path : file_name + 1;
    ofn->file_offset = (int16_t)(file_name - path);

    const char* extension = strrchr(file_name, '.');
    ofn->file_extension = extension == 0 ? 0 : (int16_t)(extension - path + 1);
    return 1;
}

static int32_t progpu_get_open_file_name_a(progpu_open_file_name_a* ofn, int save)
{
    char path[4096];
    if (!progpu_run_dialog_process(save, ofn != 0 ? ofn->title : 0, path, sizeof(path)))
    {
        return 0;
    }

    return progpu_write_open_file_name_a(ofn, path);
}

static int32_t progpu_get_open_file_name_w(progpu_open_file_name_w* ofn, int save)
{
    char path[4096];
    if (!progpu_run_dialog_process(save, 0, path, sizeof(path)))
    {
        return 0;
    }

    return progpu_write_open_file_name_w(ofn, path);
}

PROGPU_EXPORT uint32_t GetCurrentThreadId(void)
{
#if defined(__APPLE__)
    uint64_t thread_id = 0;
    if (pthread_threadid_np(0, &thread_id) == 0)
    {
        return (uint32_t)thread_id;
    }

    return (uint32_t)(uintptr_t)pthread_self();
#elif defined(__linux__)
    return (uint32_t)syscall(SYS_gettid);
#else
    return 1;
#endif
}

PROGPU_EXPORT uint32_t GetCurrentProcessId(void)
{
#if defined(__APPLE__) || defined(__linux__)
    return (uint32_t)getpid();
#else
    return 1;
#endif
}

PROGPU_EXPORT void Sleep(uint32_t milliseconds)
{
    struct timespec requested;
    requested.tv_sec = (time_t)(milliseconds / 1000u);
    requested.tv_nsec = (long)((milliseconds % 1000u) * 1000000u);
    nanosleep(&requested, 0);
}

PROGPU_EXPORT int32_t GetOpenFileNameA(progpu_open_file_name_a* ofn)
{
    return progpu_get_open_file_name_a(ofn, 0);
}

PROGPU_EXPORT int32_t GetOpenFileNameW(progpu_open_file_name_w* ofn)
{
    return progpu_get_open_file_name_w(ofn, 0);
}

PROGPU_EXPORT int32_t GetOpenFileName(progpu_open_file_name_a* ofn)
{
    return GetOpenFileNameA(ofn);
}

PROGPU_EXPORT int32_t GetSaveFileNameA(progpu_open_file_name_a* ofn)
{
    return progpu_get_open_file_name_a(ofn, 1);
}

PROGPU_EXPORT int32_t GetSaveFileNameW(progpu_open_file_name_w* ofn)
{
    return progpu_get_open_file_name_w(ofn, 1);
}

PROGPU_EXPORT int32_t GetSaveFileName(progpu_open_file_name_a* ofn)
{
    return GetSaveFileNameA(ofn);
}

PROGPU_EXPORT progpu_intptr LocalFree(progpu_intptr memory)
{
    if (memory != 0)
    {
        free((void*)memory);
    }

    return 0;
}

PROGPU_EXPORT progpu_intptr GetModuleHandleW(const void* module_name)
{
    (void)module_name;
    return 1;
}

PROGPU_EXPORT progpu_intptr GetModuleHandleA(const void* module_name)
{
    return GetModuleHandleW(module_name);
}

PROGPU_EXPORT progpu_intptr GetModuleHandle(const void* module_name)
{
    return GetModuleHandleW(module_name);
}

PROGPU_EXPORT int32_t GetModuleHandleExW(uint32_t flags, const void* module_name, progpu_intptr* module)
{
    (void)flags;
    (void)module_name;
    if (module != 0)
    {
        *module = 1;
    }

    return 1;
}

PROGPU_EXPORT int32_t GetModuleHandleExA(uint32_t flags, const void* module_name, progpu_intptr* module)
{
    return GetModuleHandleExW(flags, module_name, module);
}

PROGPU_EXPORT int32_t GetModuleHandleEx(uint32_t flags, const void* module_name, progpu_intptr* module)
{
    return GetModuleHandleExW(flags, module_name, module);
}

PROGPU_EXPORT progpu_intptr GetProcAddress(progpu_intptr module, const char* procedure_name)
{
    (void)module;
    if (procedure_name == 0)
    {
        return 0;
    }

    if (strcmp(procedure_name, "DefWindowProcW") == 0 ||
        strcmp(procedure_name, "DefWindowProc") == 0)
    {
        return PROGPU_DEF_WINDOW_PROC_W;
    }

    if (strcmp(procedure_name, "DefWindowProcA") == 0)
    {
        return PROGPU_DEF_WINDOW_PROC_A;
    }

    return 0;
}

PROGPU_EXPORT progpu_intptr LoadLibraryW(const void* file_name)
{
    (void)file_name;
    return 1;
}

PROGPU_EXPORT progpu_intptr LoadLibraryA(const void* file_name)
{
    return LoadLibraryW(file_name);
}

PROGPU_EXPORT progpu_intptr LoadLibrary(const void* file_name)
{
    return LoadLibraryW(file_name);
}

PROGPU_EXPORT progpu_intptr LoadLibraryExW(const void* file_name, progpu_intptr file, uint32_t flags)
{
    (void)file;
    (void)flags;
    return LoadLibraryW(file_name);
}

PROGPU_EXPORT progpu_intptr LoadLibraryExA(const void* file_name, progpu_intptr file, uint32_t flags)
{
    return LoadLibraryExW(file_name, file, flags);
}

PROGPU_EXPORT progpu_intptr LoadLibraryEx(const void* file_name, progpu_intptr file, uint32_t flags)
{
    return LoadLibraryExW(file_name, file, flags);
}

PROGPU_EXPORT int32_t FreeLibrary(progpu_intptr module)
{
    (void)module;
    return 1;
}

PROGPU_EXPORT uint32_t SetErrorMode(uint32_t mode)
{
    (void)mode;
    return 0;
}

PROGPU_EXPORT int32_t SetProcessWorkingSetSize(progpu_intptr process, progpu_intptr minimum_size, progpu_intptr maximum_size)
{
    (void)process;
    (void)minimum_size;
    (void)maximum_size;
    return 1;
}

PROGPU_EXPORT progpu_intptr SetWindowsHookEx(int32_t code, progpu_intptr callback, progpu_intptr instance, int32_t thread_id)
{
    (void)code;
    (void)callback;
    (void)instance;
    (void)thread_id;
    return 1;
}

PROGPU_EXPORT progpu_intptr SetWindowsHookExA(int32_t code, progpu_intptr callback, progpu_intptr instance, int32_t thread_id)
{
    return SetWindowsHookEx(code, callback, instance, thread_id);
}

PROGPU_EXPORT progpu_intptr SetWindowsHookExW(int32_t code, progpu_intptr callback, progpu_intptr instance, int32_t thread_id)
{
    return SetWindowsHookEx(code, callback, instance, thread_id);
}

PROGPU_EXPORT int32_t UnhookWindowsHookEx(progpu_intptr hook)
{
    (void)hook;
    return 1;
}

PROGPU_EXPORT int32_t CallNextHookEx(progpu_intptr hook, int32_t code, progpu_intptr w_param, progpu_intptr l_param)
{
    (void)hook;
    (void)code;
    (void)w_param;
    (void)l_param;
    return 0;
}

PROGPU_EXPORT progpu_intptr SetActiveWindow(progpu_intptr window)
{
    progpu_intptr previous = progpu_active_window;
    progpu_active_window = window;
    return previous;
}

PROGPU_EXPORT progpu_intptr GetActiveWindow(void)
{
    return progpu_active_window;
}

PROGPU_EXPORT progpu_intptr GetFocus(void)
{
    return 0;
}

PROGPU_EXPORT progpu_intptr SetFocus(progpu_intptr window)
{
    (void)window;
    return 0;
}

PROGPU_EXPORT progpu_intptr GetCapture(void)
{
    return progpu_capture_window;
}

PROGPU_EXPORT progpu_intptr SetCapture(progpu_intptr window)
{
    progpu_intptr previous = progpu_capture_window;
    progpu_capture_window = window;
    return previous;
}

PROGPU_EXPORT int32_t ReleaseCapture(void)
{
    progpu_capture_window = 0;
    return 1;
}

PROGPU_EXPORT int32_t IsChild(progpu_intptr parent, progpu_intptr child)
{
    (void)parent;
    (void)child;
    return 0;
}

PROGPU_EXPORT int32_t IsWindow(progpu_intptr window)
{
    return window != 0;
}

PROGPU_EXPORT int32_t IsWindowVisible(progpu_intptr window)
{
    progpu_window_state* state = progpu_get_window_state(window, 0);
    return state != 0 ? state->visible : window != 0;
}

PROGPU_EXPORT int32_t IsWindowEnabled(progpu_intptr window)
{
    return window != 0;
}

PROGPU_EXPORT int32_t BringWindowToTop(progpu_intptr window)
{
    (void)window;
    return 1;
}

PROGPU_EXPORT int32_t SetWindowPos(
    progpu_intptr window,
    progpu_intptr insert_after,
    int32_t x,
    int32_t y,
    int32_t width,
    int32_t height,
    uint32_t flags)
{
    (void)insert_after;
    progpu_window_state* state = progpu_get_window_state(window, 1);
    if (state != 0)
    {
        if ((flags & 0x0002u) == 0)
        {
            state->x = x;
            state->y = y;
        }

        if ((flags & 0x0001u) == 0)
        {
            state->width = width;
            state->height = height;
        }
    }

    return 1;
}

PROGPU_EXPORT progpu_intptr SetParent(progpu_intptr child, progpu_intptr new_parent)
{
    progpu_window_state* state = progpu_get_window_state(child, 1);
    if (state == 0)
    {
        return 0;
    }

    progpu_intptr previous = state->parent;
    state->parent = new_parent;
    return previous;
}

PROGPU_EXPORT progpu_intptr GetParent(progpu_intptr window)
{
    progpu_window_state* state = progpu_get_window_state(window, 0);
    return state != 0 ? state->parent : 0;
}

PROGPU_EXPORT progpu_intptr GetTopWindow(progpu_intptr window)
{
    (void)window;
    return 0;
}

PROGPU_EXPORT progpu_intptr GetWindow(progpu_intptr window, uint32_t command)
{
    (void)window;
    (void)command;
    return 0;
}

PROGPU_EXPORT int32_t GetCursorPos(progpu_point* point)
{
    if (point != 0)
    {
        point->x = 0;
        point->y = 0;
    }

    return 1;
}

PROGPU_EXPORT progpu_intptr GetCursor(void)
{
    return progpu_current_cursor;
}

PROGPU_EXPORT progpu_intptr SetCursor(progpu_intptr cursor)
{
    progpu_intptr previous = progpu_current_cursor;
    progpu_current_cursor = cursor;
    return previous;
}

PROGPU_EXPORT int32_t ShowCursor(int32_t show)
{
    progpu_cursor_show_count += show ? 1 : -1;
    return progpu_cursor_show_count;
}

PROGPU_EXPORT progpu_intptr LoadCursor(progpu_intptr instance, progpu_intptr cursor_name)
{
    (void)instance;
    return cursor_name != 0 ? cursor_name : progpu_allocate_fake_handle();
}

PROGPU_EXPORT progpu_intptr LoadCursorA(progpu_intptr instance, progpu_intptr cursor_name)
{
    return LoadCursor(instance, cursor_name);
}

PROGPU_EXPORT progpu_intptr LoadCursorW(progpu_intptr instance, progpu_intptr cursor_name)
{
    return LoadCursor(instance, cursor_name);
}

PROGPU_EXPORT int32_t DestroyCursor(progpu_intptr cursor)
{
    if (progpu_current_cursor == cursor)
    {
        progpu_current_cursor = 0;
    }

    return 1;
}

PROGPU_EXPORT progpu_intptr WindowFromPoint(progpu_point point)
{
    if (progpu_capture_window != 0)
    {
        return progpu_capture_window;
    }

    for (int i = PROGPU_FAKE_WINDOW_STATE_COUNT - 1; i >= 0; i--)
    {
        progpu_window_state* state = &progpu_fake_window_states[i];
        if (state->window == 0 || !state->visible)
        {
            continue;
        }

        if (point.x >= state->x &&
            point.y >= state->y &&
            point.x < state->x + state->width &&
            point.y < state->y + state->height)
        {
            return state->window;
        }
    }

    return 0;
}

PROGPU_EXPORT int32_t ScreenToClient(progpu_intptr window, progpu_point* point)
{
    progpu_window_state* state = progpu_get_window_state(window, 0);
    if (point != 0 && state != 0)
    {
        point->x -= state->x;
        point->y -= state->y;
    }

    return 1;
}

PROGPU_EXPORT int32_t ClientToScreen(progpu_intptr window, progpu_point* point)
{
    progpu_window_state* state = progpu_get_window_state(window, 0);
    if (point != 0 && state != 0)
    {
        point->x += state->x;
        point->y += state->y;
    }

    return 1;
}

PROGPU_EXPORT int32_t GetClientRect(progpu_intptr window, progpu_rect* rect)
{
    if (rect != 0)
    {
        progpu_window_state* state = progpu_get_window_state(window, 0);
        rect->left = 0;
        rect->top = 0;
        rect->right = state != 0 ? state->width : 0;
        rect->bottom = state != 0 ? state->height : 0;
    }

    return 1;
}

PROGPU_EXPORT int32_t GetWindowRect(progpu_intptr window, progpu_rect* rect)
{
    if (rect != 0)
    {
        progpu_window_state* state = progpu_get_window_state(window, 0);
        int32_t x = state != 0 ? state->x : 0;
        int32_t y = state != 0 ? state->y : 0;
        int32_t width = state != 0 ? state->width : 0;
        int32_t height = state != 0 ? state->height : 0;
        rect->left = x;
        rect->top = y;
        rect->right = x + width;
        rect->bottom = y + height;
    }

    return 1;
}

PROGPU_EXPORT progpu_intptr MonitorFromRect(progpu_rect* rect, uint32_t flags)
{
    (void)rect;
    (void)flags;
    return 1;
}

PROGPU_EXPORT progpu_intptr MonitorFromWindow(progpu_intptr window, uint32_t flags)
{
    (void)window;
    (void)flags;
    return 1;
}

PROGPU_EXPORT int32_t GetMonitorInfo(progpu_intptr monitor, void* info)
{
    (void)monitor;
    (void)info;
    return 0;
}

PROGPU_EXPORT int32_t SendMessage(progpu_intptr window, int32_t message, progpu_intptr w_param, progpu_intptr l_param)
{
    (void)window;
    (void)message;
    (void)w_param;
    (void)l_param;
    return 0;
}

PROGPU_EXPORT int32_t PostMessage(progpu_intptr window, int32_t message, progpu_intptr w_param, progpu_intptr l_param)
{
    (void)window;
    (void)message;
    (void)w_param;
    (void)l_param;
    return 1;
}

PROGPU_EXPORT void NotifyWinEvent(int32_t event_id, progpu_intptr window, int32_t object_id, int32_t child_id)
{
    (void)event_id;
    (void)window;
    (void)object_id;
    (void)child_id;
}

PROGPU_EXPORT progpu_intptr DefWindowProcW(progpu_intptr window, int32_t message, progpu_intptr w_param, progpu_intptr l_param)
{
    (void)window;
    (void)message;
    (void)w_param;
    (void)l_param;
    return 0;
}

PROGPU_EXPORT progpu_intptr DefWindowProcA(progpu_intptr window, int32_t message, progpu_intptr w_param, progpu_intptr l_param)
{
    return DefWindowProcW(window, message, w_param, l_param);
}

PROGPU_EXPORT progpu_intptr DefWindowProc(progpu_intptr window, int32_t message, progpu_intptr w_param, progpu_intptr l_param)
{
    return DefWindowProcW(window, message, w_param, l_param);
}

PROGPU_EXPORT progpu_intptr CallWindowProcW(
    progpu_intptr procedure,
    progpu_intptr window,
    int32_t message,
    progpu_intptr w_param,
    progpu_intptr l_param)
{
    if (procedure == PROGPU_DEF_WINDOW_PROC_A || procedure == PROGPU_DEF_WINDOW_PROC_W || procedure == 1)
    {
        return DefWindowProcW(window, message, w_param, l_param);
    }

    return 0;
}

PROGPU_EXPORT progpu_intptr CallWindowProcA(
    progpu_intptr procedure,
    progpu_intptr window,
    int32_t message,
    progpu_intptr w_param,
    progpu_intptr l_param)
{
    return CallWindowProcW(procedure, window, message, w_param, l_param);
}

PROGPU_EXPORT progpu_intptr CallWindowProc(
    progpu_intptr procedure,
    progpu_intptr window,
    int32_t message,
    progpu_intptr w_param,
    progpu_intptr l_param)
{
    return CallWindowProcW(procedure, window, message, w_param, l_param);
}

PROGPU_EXPORT int16_t RegisterClassExW(void* window_class)
{
    (void)window_class;
    return 1;
}

PROGPU_EXPORT int16_t RegisterClassExA(void* window_class)
{
    return RegisterClassExW(window_class);
}

PROGPU_EXPORT int16_t RegisterClassEx(void* window_class)
{
    return RegisterClassExW(window_class);
}

PROGPU_EXPORT int32_t UnregisterClassW(const void* class_name, progpu_intptr instance)
{
    (void)class_name;
    (void)instance;
    return 1;
}

PROGPU_EXPORT int32_t UnregisterClassA(const void* class_name, progpu_intptr instance)
{
    return UnregisterClassW(class_name, instance);
}

PROGPU_EXPORT int32_t UnregisterClass(const void* class_name, progpu_intptr instance)
{
    return UnregisterClassW(class_name, instance);
}

PROGPU_EXPORT uint32_t RegisterWindowMessageW(const void* message)
{
    (void)message;
    return 0xC000u;
}

PROGPU_EXPORT uint32_t RegisterWindowMessageA(const void* message)
{
    return RegisterWindowMessageW(message);
}

PROGPU_EXPORT uint32_t RegisterWindowMessage(const void* message)
{
    return RegisterWindowMessageW(message);
}

PROGPU_EXPORT progpu_intptr CreateWindowEx(
    int32_t extended_style,
    const void* class_name,
    const void* window_name,
    int32_t style,
    int32_t x,
    int32_t y,
    int32_t width,
    int32_t height,
    progpu_intptr parent,
    progpu_intptr menu,
    progpu_intptr instance,
    void* parameter)
{
    (void)extended_style;
    (void)class_name;
    (void)window_name;
    (void)style;
    (void)x;
    (void)y;
    (void)width;
    (void)height;
    (void)parent;
    (void)menu;
    (void)instance;
    (void)parameter;
    progpu_intptr handle = progpu_allocate_fake_handle();
    progpu_window_state* state = progpu_get_window_state(handle, 1);
    if (state != 0)
    {
        state->style = style == 0 ? PROGPU_DEFAULT_STYLE : (progpu_intptr)style;
        state->ex_style = extended_style == 0 ? PROGPU_DEFAULT_EX_STYLE : (progpu_intptr)extended_style;
        state->parent = parent;
        state->id = menu == 0 ? 1 : menu;
        state->instance = instance == 0 ? 1 : instance;
        state->x = x;
        state->y = y;
        state->width = width;
        state->height = height;
        state->visible = (style & 0x10000000) != 0;
    }

    return handle;
}

PROGPU_EXPORT progpu_intptr CreateWindowExW(
    int32_t extended_style,
    const void* class_name,
    const void* window_name,
    int32_t style,
    int32_t x,
    int32_t y,
    int32_t width,
    int32_t height,
    progpu_intptr parent,
    progpu_intptr menu,
    progpu_intptr instance,
    void* parameter)
{
    return CreateWindowEx(extended_style, class_name, window_name, style, x, y, width, height, parent, menu, instance, parameter);
}

PROGPU_EXPORT progpu_intptr CreateWindowExA(
    int32_t extended_style,
    const void* class_name,
    const void* window_name,
    int32_t style,
    int32_t x,
    int32_t y,
    int32_t width,
    int32_t height,
    progpu_intptr parent,
    progpu_intptr menu,
    progpu_intptr instance,
    void* parameter)
{
    return CreateWindowEx(extended_style, class_name, window_name, style, x, y, width, height, parent, menu, instance, parameter);
}

PROGPU_EXPORT int32_t DestroyWindow(progpu_intptr window)
{
    progpu_window_state* state = progpu_get_window_state(window, 0);
    if (state != 0)
    {
        state->window = 0;
    }

    if (progpu_capture_window == window)
    {
        progpu_capture_window = 0;
    }

    if (progpu_active_window == window)
    {
        progpu_active_window = 0;
    }

    return 1;
}

PROGPU_EXPORT int32_t DestroyIcon(progpu_intptr icon)
{
    (void)icon;
    return 1;
}

PROGPU_EXPORT int32_t ShowWindow(progpu_intptr window, int32_t command)
{
    progpu_window_state* state = progpu_get_window_state(window, 1);
    if (state != 0)
    {
        state->visible = command != 0;
    }

    return 1;
}

PROGPU_EXPORT int32_t ShowWindowAsync(progpu_intptr window, int32_t command)
{
    return ShowWindow(window, command);
}

PROGPU_EXPORT int32_t GetSystemMetrics(int32_t metric)
{
    switch (metric)
    {
        case 4:  return 23; /* SM_CYCAPTION */
        case 30: return 30; /* SM_CXSIZE */
        case 31: return 18; /* SM_CYSIZE */
        case 32: return 4;  /* SM_CXSIZEFRAME */
        case 33: return 4;  /* SM_CYSIZEFRAME */
        case 45: return 2;  /* SM_CXEDGE */
        case 46: return 2;  /* SM_CYEDGE */
        case 49: return 16; /* SM_CXSMICON */
        case 50: return 16; /* SM_CYSMICON */
        default: return 0;
    }
}

PROGPU_EXPORT int32_t SystemParametersInfoW(uint32_t action, uint32_t parameter, void* value, uint32_t flags)
{
    (void)action;
    (void)parameter;
    (void)value;
    (void)flags;
    return 1;
}

PROGPU_EXPORT int32_t SystemParametersInfoA(uint32_t action, uint32_t parameter, void* value, uint32_t flags)
{
    return SystemParametersInfoW(action, parameter, value, flags);
}

PROGPU_EXPORT int32_t SystemParametersInfo(uint32_t action, uint32_t parameter, void* value, uint32_t flags)
{
    return SystemParametersInfoW(action, parameter, value, flags);
}

PROGPU_EXPORT int32_t AdjustWindowRectEx(progpu_rect* rect, uint32_t style, int32_t has_menu, uint32_t extended_style)
{
    (void)rect;
    (void)style;
    (void)has_menu;
    (void)extended_style;
    return 1;
}

PROGPU_EXPORT int32_t ChangeWindowMessageFilter(uint32_t message, uint32_t flag)
{
    (void)message;
    (void)flag;
    return 1;
}

PROGPU_EXPORT int32_t ChangeWindowMessageFilterEx(progpu_intptr window, uint32_t message, uint32_t action, void* filter)
{
    (void)window;
    (void)message;
    (void)action;
    (void)filter;
    return 1;
}

PROGPU_EXPORT progpu_intptr GetSystemMenu(progpu_intptr window, int32_t revert)
{
    (void)window;
    (void)revert;
    return 0;
}

PROGPU_EXPORT int32_t EnableMenuItem(progpu_intptr menu, uint32_t item, uint32_t enable)
{
    (void)menu;
    (void)item;
    (void)enable;
    return 0;
}

PROGPU_EXPORT int32_t RemoveMenu(progpu_intptr menu, uint32_t position, uint32_t flags)
{
    (void)menu;
    (void)position;
    (void)flags;
    return 1;
}

PROGPU_EXPORT int32_t DrawMenuBar(progpu_intptr window)
{
    (void)window;
    return 1;
}

PROGPU_EXPORT int32_t GetWindowLong(progpu_intptr window, int32_t index)
{
    return (int32_t)progpu_get_window_long_value(window, index);
}

PROGPU_EXPORT progpu_intptr GetWindowLongPtr(progpu_intptr window, int32_t index)
{
    return progpu_get_window_long_value(window, index);
}

PROGPU_EXPORT int32_t SetWindowLong(progpu_intptr window, int32_t index, int32_t value)
{
    return (int32_t)progpu_set_window_long_value(window, index, (progpu_intptr)value);
}

PROGPU_EXPORT progpu_intptr SetWindowLongPtr(progpu_intptr window, int32_t index, progpu_intptr value)
{
    return progpu_set_window_long_value(window, index, value);
}

PROGPU_EXPORT int32_t SetClassLong(progpu_intptr window, int32_t index, int32_t value)
{
    (void)window;
    (void)index;
    return value;
}

PROGPU_EXPORT progpu_intptr SetClassLongPtr(progpu_intptr window, int32_t index, progpu_intptr value)
{
    (void)window;
    (void)index;
    return value;
}

PROGPU_EXPORT int32_t SetWindowRgn(progpu_intptr window, progpu_intptr region, int32_t redraw)
{
    (void)window;
    (void)region;
    (void)redraw;
    return 1;
}

PROGPU_EXPORT int32_t GetWindowPlacement(progpu_intptr window, void* placement)
{
    (void)window;
    (void)placement;
    return 1;
}

PROGPU_EXPORT uint32_t TrackPopupMenuEx(progpu_intptr menu, uint32_t flags, int32_t x, int32_t y, progpu_intptr window, void* parameters)
{
    (void)menu;
    (void)flags;
    (void)x;
    (void)y;
    (void)window;
    (void)parameters;
    return 0;
}

PROGPU_EXPORT int32_t SendInput(int32_t input_count, void* inputs, int32_t input_size)
{
    (void)inputs;
    (void)input_size;
    return input_count;
}

PROGPU_EXPORT progpu_intptr GetStockObject(int32_t object)
{
    (void)object;
    return 1;
}

PROGPU_EXPORT int32_t DeleteObject(progpu_intptr object)
{
    (void)object;
    return 1;
}

PROGPU_EXPORT progpu_intptr CreateSolidBrush(int32_t color)
{
    (void)color;
    return progpu_allocate_fake_handle();
}

PROGPU_EXPORT progpu_intptr CreateRectRgn(int32_t left, int32_t top, int32_t right, int32_t bottom)
{
    (void)left;
    (void)top;
    (void)right;
    (void)bottom;
    return progpu_allocate_fake_handle();
}

PROGPU_EXPORT progpu_intptr CreateRoundRectRgn(
    int32_t left,
    int32_t top,
    int32_t right,
    int32_t bottom,
    int32_t ellipse_width,
    int32_t ellipse_height)
{
    (void)left;
    (void)top;
    (void)right;
    (void)bottom;
    (void)ellipse_width;
    (void)ellipse_height;
    return progpu_allocate_fake_handle();
}

PROGPU_EXPORT progpu_intptr CreateRectRgnIndirect(const progpu_rect* rect)
{
    (void)rect;
    return progpu_allocate_fake_handle();
}

PROGPU_EXPORT int32_t CombineRgn(progpu_intptr destination, progpu_intptr source1, progpu_intptr source2, int32_t mode)
{
    (void)destination;
    (void)source1;
    (void)source2;
    (void)mode;
    return 1;
}

PROGPU_EXPORT progpu_intptr SelectObject(progpu_intptr dc, progpu_intptr object)
{
    (void)dc;
    return object;
}

PROGPU_EXPORT progpu_intptr GetDC(progpu_intptr window)
{
    (void)window;
    return 1;
}

PROGPU_EXPORT int32_t ReleaseDC(progpu_intptr window, progpu_intptr dc)
{
    (void)window;
    (void)dc;
    return 1;
}

PROGPU_EXPORT int32_t GetDeviceCaps(progpu_intptr dc, int32_t index)
{
    (void)dc;
    switch (index)
    {
        case 88: /* LOGPIXELSX */
        case 90: /* LOGPIXELSY */
            return 96;
        default:
            return 0;
    }
}

PROGPU_EXPORT int32_t DwmIsCompositionEnabled(void)
{
    return 0;
}

PROGPU_EXPORT int32_t DwmGetColorizationColor(uint32_t* colorization, int32_t* opaque_blend)
{
    if (colorization != 0)
    {
        *colorization = 0xFF000000u;
    }

    if (opaque_blend != 0)
    {
        *opaque_blend = 1;
    }

    return 0;
}

PROGPU_EXPORT int32_t DwmExtendFrameIntoClientArea(progpu_intptr window, void* margins)
{
    (void)window;
    (void)margins;
    return 0;
}

PROGPU_EXPORT int32_t DwmDefWindowProc(
    progpu_intptr window,
    int32_t message,
    progpu_intptr w_param,
    progpu_intptr l_param,
    progpu_intptr* result)
{
    (void)window;
    (void)message;
    (void)w_param;
    (void)l_param;
    if (result != 0)
    {
        *result = 0;
    }

    return 0;
}

PROGPU_EXPORT void DwmSetWindowAttribute(progpu_intptr window, int32_t attribute, void* value, int32_t value_size)
{
    (void)window;
    (void)attribute;
    (void)value;
    (void)value_size;
}

PROGPU_EXPORT int32_t IsThemeActive(void)
{
    return 0;
}

PROGPU_EXPORT int32_t GetCurrentThemeName(void* theme_name, int32_t theme_name_count, void* color, int32_t color_count, void* size, int32_t size_count)
{
    (void)theme_name;
    (void)theme_name_count;
    (void)color;
    (void)color_count;
    (void)size;
    (void)size_count;
    return 0;
}

PROGPU_EXPORT void SetWindowThemeAttribute(progpu_intptr window, int32_t attribute, void* options, uint32_t options_size)
{
    (void)window;
    (void)attribute;
    (void)options;
    (void)options_size;
}

PROGPU_EXPORT int32_t GdiplusStartup(progpu_intptr* token, void* input, void* output)
{
    (void)input;
    (void)output;
    if (token != 0)
    {
        *token = 1;
    }

    return 0;
}

PROGPU_EXPORT void GdiplusShutdown(progpu_intptr token)
{
    (void)token;
}

PROGPU_EXPORT int32_t GdipCreateBitmapFromStream(void* stream, progpu_intptr* bitmap)
{
    (void)stream;
    if (bitmap != 0)
    {
        *bitmap = progpu_allocate_fake_handle();
    }

    return 0;
}

PROGPU_EXPORT int32_t GdipCreateHBITMAPFromBitmap(progpu_intptr bitmap, progpu_intptr* hbitmap, int32_t background)
{
    (void)bitmap;
    (void)background;
    if (hbitmap != 0)
    {
        *hbitmap = progpu_allocate_fake_handle();
    }

    return 0;
}

PROGPU_EXPORT int32_t GdipCreateHICONFromBitmap(progpu_intptr bitmap, progpu_intptr* hicon)
{
    (void)bitmap;
    if (hicon != 0)
    {
        *hicon = progpu_allocate_fake_handle();
    }

    return 0;
}

PROGPU_EXPORT int32_t GdipDisposeImage(progpu_intptr image)
{
    (void)image;
    return 0;
}

PROGPU_EXPORT int32_t GdipImageForceValidation(progpu_intptr image)
{
    (void)image;
    return 0;
}
