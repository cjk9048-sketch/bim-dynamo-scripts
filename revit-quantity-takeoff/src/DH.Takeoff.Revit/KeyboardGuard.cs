using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace DH.Takeoff.Revit;

/// <summary>
/// 띄운 채 쓰는 창(R62)의 글쇠를 Revit 보다 먼저 받는다(계획 검토 H2): Revit 의 메시지 고리는 창의 글쇠 메시지를 자기 단축키 · 가속기로 먼저 볼 수 있어,
/// 창에서 누른 Del · Ctrl+Z 가 Revit 의 «선택한 부재 지우기» · «실행 취소»가 될 수 있다. 이 스레드의 메시지 훅(WH_GETMESSAGE)에서 우리 창(과 그 펼친 목록)의
/// 글쇠 메시지만 WPF 에 먼저 넘기고(ComponentDispatcher — Tab · 방향키 · 단추 단축키) 안 받았으면 창에 보낸 뒤(TranslateMessage · DispatchMessage)
/// 메시지를 비운다(WM_NULL) — 그래서 Revit 에는 가지 않는다. 다른 창의 글쇠는 건드리지 않는다.
/// </summary>
public sealed class KeyboardGuard : IDisposable
{
    private const int WH_GETMESSAGE = 3, PM_REMOVE = 1, WM_KEYFIRST = 0x0100, WM_KEYLAST = 0x0109, WM_NULL = 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMsg
    {
        public nint hwnd;
        public int message;
        public nint wParam, lParam;
        public int time, x, y;
    }

    private delegate nint HookProc(int code, nint wParam, nint lParam);

    [DllImport("user32.dll", SetLastError = true)] private static extern nint SetWindowsHookEx(int idHook, HookProc proc, nint module, uint threadId);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(nint hook);
    [DllImport("user32.dll")] private static extern nint CallNextHookEx(nint hook, int code, nint wParam, nint lParam);
    [DllImport("user32.dll")] private static extern bool TranslateMessage(ref NativeMsg msg);
    [DllImport("user32.dll")] private static extern nint DispatchMessage(ref NativeMsg msg);
    [DllImport("user32.dll")] private static extern nint GetParent(nint hwnd);
    [DllImport("user32.dll")] private static extern bool IsWindowEnabled(nint hwnd);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();

    private readonly nint _window;
    private readonly HookProc _proc;   // 대리자를 붙들어 둔다(가비지가 치우면 Revit 이 죽는다)
    private nint _hook;

    /// <summary>시험용: 훅이 받아 창에 보낸 글쇠 메시지 수.</summary>
    public int Handled { get; private set; }

    public KeyboardGuard(Window window)
    {
        _window = new WindowInteropHelper(window).EnsureHandle();
        _proc = Proc;
        _hook = SetWindowsHookEx(WH_GETMESSAGE, _proc, 0, GetCurrentThreadId());
    }

    /// <summary>훅이 걸렸는가(걸지 못하면 전처럼 — 일지에 남긴다).</summary>
    public bool Active => _hook != 0;

    private nint Proc(int code, nint wParam, nint lParam)
    {
        if (code >= 0 && wParam == PM_REMOVE)
        {
            try
            {
                var msg = Marshal.PtrToStructure<NativeMsg>(lParam);
                if (msg.message >= WM_KEYFIRST && msg.message <= WM_KEYLAST && Ours(msg.hwnd))
                {
                    var wpf = new MSG { hwnd = msg.hwnd, message = msg.message, wParam = msg.wParam, lParam = msg.lParam, time = msg.time, pt_x = msg.x, pt_y = msg.y };
                    if (!ComponentDispatcher.RaiseThreadMessage(ref wpf))
                    {
                        TranslateMessage(ref msg);
                        DispatchMessage(ref msg);
                    }
                    msg.message = WM_NULL;
                    Marshal.StructureToPtr(msg, lParam, false);
                    Handled++;
                }
            }
            catch { /* 훅 안에서 새는 오류는 Revit 을 죽인다 — 그 메시지는 전처럼 흘려보낸다 */ }
        }
        return CallNextHookEx(_hook, code, wParam, lParam);
    }

    // 우리 창이거나 우리 창이 주인인 창(펼친 목록 · 풍선 — WS_POPUP 은 GetParent 가 주인을 돌려준다).
    // 우리 창이 꺼져 있으면(우리 창이 주인인 묻는 창 · 저장 창이 떠 있음) 받지 않는다 — 그 창의 Enter · Esc · Tab 은 그 창이 스스로(IsDialogMessage) 본다(R62 설치 전 검토 중간).
    private bool Ours(nint hwnd)
    {
        if (!IsWindowEnabled(_window)) return false;
        for (int i = 0; hwnd != 0 && i < 16; i++, hwnd = GetParent(hwnd))
            if (hwnd == _window) return true;
        return false;
    }

    public void Dispose()
    {
        if (_hook == 0) return;
        UnhookWindowsHookEx(_hook);
        _hook = 0;
    }
}
