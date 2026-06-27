// WinMain.cpp
// 纯 Win32 API 界面：进度条已移除，开始/停止合并为一个按钮，回车为快捷键，URL 文本框支持 Ctrl+A 全选

#include <windows.h>
#include <commctrl.h>
#include <string>
#include <vector>

// Link with Comctl32.lib for InitCommonControlsEx
#pragma comment(lib, "Comctl32.lib")

// 控件 ID
#define IDC_LABEL_URL   106
#define IDC_URL_EDIT    107
#define IDC_BTN_START   101
#define IDC_STATUS      104
#define IDC_LOG_EDIT    105

#define WM_APP_PROCESS_OUTPUT (WM_APP + 1)
#define WM_APP_PROCESS_EXIT   (WM_APP + 2)

struct AppState {
    HWND hWnd;
    HWND hLabelUrl;
    HWND hUrlEdit;
    WNDPROC prevUrlProc;
    HWND hBtnStart;
    HWND hStatus;
    HWND hLog;
    HANDLE hChildProcess;
    HANDLE hChildThread;
    HANDLE hOutputRead;
    bool running;
    bool sawError;
    AppState() : hWnd(nullptr), hLabelUrl(nullptr), hUrlEdit(nullptr), prevUrlProc(nullptr), hBtnStart(nullptr), hStatus(nullptr), hLog(nullptr), hChildProcess(nullptr), hChildThread(nullptr), hOutputRead(nullptr), running(false), sawError(false) {}
};

static AppState g_state;

// 将文本追加到只读多行编辑控件
void AppendLog(HWND hLog, const std::wstring& text) {
    int len = GetWindowTextLengthW(hLog);
    SendMessageW(hLog, EM_SETSEL, (WPARAM)len, (LPARAM)len);
    SendMessageW(hLog, EM_REPLACESEL, FALSE, (LPARAM)text.c_str());
}

// URL 编辑框子类，用于处理回车和 Ctrl+A
LRESULT CALLBACK UrlEditProc(HWND hwnd, UINT uMsg, WPARAM wParam, LPARAM lParam) {
    AppState* s = &g_state;
    if (uMsg == WM_KEYDOWN) {
        if (wParam == VK_RETURN) {
            // 触发按钮命令
            PostMessageW(s->hWnd, WM_COMMAND, (WPARAM)MAKEWPARAM(IDC_BTN_START, BN_CLICKED), 0);
            return 0;
        }
        if ((wParam == 'A' || wParam == 'a') && (GetKeyState(VK_CONTROL) & 0x8000)) {
            // Ctrl+A 全选
            SendMessageW(hwnd, EM_SETSEL, 0, -1);
            return 0;
        }
    }
    return CallWindowProcW(s->prevUrlProc, hwnd, uMsg, wParam, lParam);
}

DWORD WINAPI OutputReaderThread(LPVOID lpParam) {
    AppState* s = (AppState*)lpParam;
    DWORD read = 0;
    HANDLE hRead = s->hOutputRead;

    while (true) {
        // Read bytes from pipe
        char localBuf[2048];
        BOOL ok = ReadFile(hRead, localBuf, (DWORD)sizeof(localBuf), &read, NULL);
        if (!ok || read == 0) break;

        // Convert to wide and post message
        // Use OEMCP because many console apps output using OEM code page, which avoids 中文乱码
        int needed = MultiByteToWideChar(CP_OEMCP, 0, localBuf, (int)read, NULL, 0);
        if (needed > 0) {
            std::wstring out;
            out.resize(needed);
            MultiByteToWideChar(CP_OEMCP, 0, localBuf, (int)read, &out[0], needed);

            // detect "error" 字样（按英文关键字判断）
            std::wstring lower = out;
            for (auto &c : lower) c = towlower(c);
            if (lower.find(L"error") != std::wstring::npos || lower.find(L"failed") != std::wstring::npos) s->sawError = true;

            // allocate copy and post
            wchar_t* msg = new wchar_t[out.size()+1];
            wcscpy_s(msg, out.size()+1, out.c_str());
            PostMessageW(s->hWnd, WM_APP_PROCESS_OUTPUT, 0, (LPARAM)msg);
        }
    }

    // Wait for process termination and post exit message
    DWORD exitCode = 0;
    if (s->hChildProcess) {
        WaitForSingleObject(s->hChildProcess, INFINITE);
        GetExitCodeProcess(s->hChildProcess, &exitCode);
    }

    PostMessageW(s->hWnd, WM_APP_PROCESS_EXIT, (WPARAM)exitCode, 0);
    return 0;
}

// 启动子进程并开始读取输出。函数内部会读取 URL 编辑框的文本并作为参数传递
bool StartChildProcess(AppState* s) {
    // Get URL from edit control
    wchar_t urlBuf[2048] = {0};
    if (s->hUrlEdit) {
        GetWindowTextW(s->hUrlEdit, urlBuf, (int)_countof(urlBuf));
    }

    // Create pipe for capturing stdout and stderr
    SECURITY_ATTRIBUTES sa;
    sa.nLength = sizeof(sa);
    sa.lpSecurityDescriptor = NULL;
    sa.bInheritHandle = TRUE;

    HANDLE hRead = NULL;
    HANDLE hWrite = NULL;
    if (!CreatePipe(&hRead, &hWrite, &sa, 0)) return false;
    // Ensure the read handle is not inheritable
    SetHandleInformation(hRead, HANDLE_FLAG_INHERIT, 0);

    // Prepare STARTUPINFO
    STARTUPINFOW si;
    PROCESS_INFORMATION pi;
    ZeroMemory(&si, sizeof(si));
    si.cb = sizeof(si);
    si.dwFlags = STARTF_USESTDHANDLES | STARTF_USESHOWWINDOW;
    si.hStdOutput = hWrite;
    si.hStdError = hWrite;
    si.hStdInput = GetStdHandle(STD_INPUT_HANDLE);

    // Build command line: include quoted exe name and quoted URL argument (if present)
    wchar_t cmdLine[4096];
    if (wcslen(urlBuf) > 0) {
        // Quote the URL argument
        swprintf_s(cmdLine, L"\"N_m3u8DL-RE.exe\" \"%s\"", urlBuf);
    } else {
        swprintf_s(cmdLine, L"\"N_m3u8DL-RE.exe\"");
    }

    BOOL ok = CreateProcessW(NULL, cmdLine, NULL, NULL, TRUE, CREATE_NO_WINDOW, NULL, NULL, &si, &pi);
    // Close the write end in parent - child has a handle
    CloseHandle(hWrite);
    if (!ok) {
        CloseHandle(hRead);
        return false;
    }

    s->hOutputRead = hRead;
    s->hChildProcess = pi.hProcess;
    CloseHandle(pi.hThread);

    // Start reader thread
    s->hChildThread = CreateThread(NULL, 0, OutputReaderThread, s, 0, NULL);
    if (!s->hChildThread) {
        TerminateProcess(s->hChildProcess, 1);
        CloseHandle(s->hChildProcess);
        s->hChildProcess = NULL;
        CloseHandle(hRead);
        return false;
    }

    return true;
}

void StopChildProcess(AppState* s) {
    if (s->hChildProcess) {
        TerminateProcess(s->hChildProcess, 1);
        WaitForSingleObject(s->hChildProcess, 2000);
        CloseHandle(s->hChildProcess);
        s->hChildProcess = NULL;
    }
    if (s->hChildThread) {
        WaitForSingleObject(s->hChildThread, 2000);
        CloseHandle(s->hChildThread);
        s->hChildThread = NULL;
    }
    if (s->hOutputRead) {
        CloseHandle(s->hOutputRead);
        s->hOutputRead = NULL;
    }
}

LRESULT CALLBACK WndProc(HWND hWnd, UINT message, WPARAM wParam, LPARAM lParam) {
    AppState* s = &g_state;
    switch (message) {
    case WM_CREATE: {
        // Create child controls (status and log 整体上移)
        s->hLabelUrl = CreateWindowW(L"STATIC", L"m3u8 地址:", WS_CHILD | WS_VISIBLE, 10, 10, 80, 20, hWnd, (HMENU)IDC_LABEL_URL, NULL, NULL);
        s->hUrlEdit = CreateWindowExW(WS_EX_CLIENTEDGE, L"EDIT", L"", WS_CHILD | WS_VISIBLE | ES_LEFT | ES_AUTOHSCROLL, 100, 8, 360, 24, hWnd, (HMENU)IDC_URL_EDIT, NULL, NULL);
        // subclass URL edit to capture Enter and Ctrl+A
        s->prevUrlProc = (WNDPROC)SetWindowLongPtrW(s->hUrlEdit, GWLP_WNDPROC, (LONG_PTR)UrlEditProc);

        s->hBtnStart = CreateWindowW(L"BUTTON", L"Start", WS_CHILD | WS_VISIBLE | BS_PUSHBUTTON | BS_DEFPUSHBUTTON, 470, 8, 80, 28, hWnd, (HMENU)IDC_BTN_START, NULL, NULL);

        s->hStatus = CreateWindowW(L"STATIC", L"Idle", WS_CHILD | WS_VISIBLE, 10, 48, 540, 20, hWnd, (HMENU)IDC_STATUS, NULL, NULL);

        s->hLog = CreateWindowExW(WS_EX_CLIENTEDGE, L"EDIT", L"", WS_CHILD | WS_VISIBLE | ES_LEFT | ES_MULTILINE | ES_AUTOVSCROLL | ES_READONLY | WS_VSCROLL, 10, 72, 540, 320, hWnd, (HMENU)IDC_LOG_EDIT, NULL, NULL);

        break; }

    case WM_COMMAND: {
        int id = LOWORD(wParam);
        if (id == IDC_BTN_START) {
            // toggle start/stop
            if (!s->running) {
                // start
                s->sawError = false;
                SetWindowTextW(s->hLog, L"");
                SetWindowTextW(s->hStatus, L"Starting...");
                EnableWindow(s->hUrlEdit, FALSE);
                SetWindowTextW(s->hBtnStart, L"Stop");
                s->running = true;

                if (!StartChildProcess(s)) {
                    SetWindowTextW(s->hStatus, L"Failed to start process");
                    s->running = false;
                    EnableWindow(s->hUrlEdit, TRUE);
                    SetWindowTextW(s->hBtnStart, L"Start");
                    MessageBoxW(hWnd, L"无法启动子进程 (请确保 N_m3u8DL-RE.exe 在工作目录中)", L"错误", MB_ICONERROR);
                } else {
                    SetWindowTextW(s->hStatus, L"Running...");
                }
            } else {
                // stop
                SetWindowTextW(s->hStatus, L"Stopping...");
                StopChildProcess(s);
                s->running = false;
                SetWindowTextW(s->hStatus, L"Cancelled");
                EnableWindow(s->hUrlEdit, TRUE);
                SetWindowTextW(s->hBtnStart, L"Start");
            }
        }
        break; }

    case WM_APP_PROCESS_OUTPUT: {
        wchar_t* msg = (wchar_t*)lParam;
        if (msg) {
            AppendLog(s->hLog, msg);
            delete[] msg;
        }
        break; }

    case WM_APP_PROCESS_EXIT: {
        DWORD exitCode = (DWORD)wParam;
        s->running = false;
        StopChildProcess(s);
        if (exitCode == 0 && !s->sawError) {
            SetWindowTextW(s->hStatus, L"Completed");
            MessageBoxW(hWnd, L"任务完成。", L"完成", MB_ICONINFORMATION);
        } else {
            SetWindowTextW(s->hStatus, L"Error");
            MessageBoxW(hWnd, L"任务遇到错误，请查看日志。", L"错误", MB_ICONERROR);
        }
        EnableWindow(s->hUrlEdit, TRUE);
        SetWindowTextW(s->hBtnStart, L"Start");
        break; }

    case WM_SIZE: {
        RECT rc;
        GetClientRect(hWnd, &rc);
        int w = rc.right - rc.left;
        // reposition controls
        MoveWindow(s->hLabelUrl, 10, 10, 80, 20, TRUE);
        MoveWindow(s->hUrlEdit, 100, 8, w - 220, 24, TRUE);
        MoveWindow(s->hBtnStart, w - 100, 8, 80, 28, TRUE);
        MoveWindow(s->hStatus, 10, 48, w - 20, 20, TRUE);
        MoveWindow(s->hLog, 10, 72, w - 20, rc.bottom - 82, TRUE);
        break; }

    case WM_DESTROY:
        StopChildProcess(s);
        PostQuitMessage(0);
        break;

    default:
        return DefWindowProcW(hWnd, message, wParam, lParam);
    }
    return 0;
}

int APIENTRY wWinMain(HINSTANCE hInstance, HINSTANCE hPrevInstance, LPWSTR lpCmdLine, int nCmdShow) {
    INITCOMMONCONTROLSEX icex;
    icex.dwSize = sizeof(icex);
    icex.dwICC = ICC_STANDARD_CLASSES;
    InitCommonControlsEx(&icex);

    WNDCLASSW wc = {};
    wc.lpfnWndProc = WndProc;
    wc.hInstance = hInstance;
    wc.lpszClassName = L"N_m3u8DL_UI_Class";
    wc.hCursor = LoadCursor(NULL, IDC_ARROW);
    RegisterClassW(&wc);

    HWND hWnd = CreateWindowExW(0, wc.lpszClassName, L"N_m3u8-RE GUI", WS_OVERLAPPEDWINDOW & ~WS_THICKFRAME, CW_USEDEFAULT, CW_USEDEFAULT, 680, 520, NULL, NULL, hInstance, NULL);
    if (!hWnd) return 0;

    g_state.hWnd = hWnd;

    ShowWindow(hWnd, nCmdShow);
    UpdateWindow(hWnd);

    MSG msg;
    while (GetMessageW(&msg, NULL, 0, 0)) {
        TranslateMessage(&msg);
        DispatchMessageW(&msg);
    }

    return (int)msg.wParam;
}
