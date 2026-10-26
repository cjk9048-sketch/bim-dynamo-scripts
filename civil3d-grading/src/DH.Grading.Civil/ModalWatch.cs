using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace DH.Grading.Civil;

/// <summary>★[v105.1.1 · JACK 1023 «지표면 생성 · 변환에도 로딩 팝업»] AutoCAD가 사람에게 묻는 창을 띄웠나 — 로딩 창(<see cref="BusyPopup"/>)이 그동안 숨는다.
/// <para>정지면 생성 흐름에는 알림 · 예/아니오 창이 스무 곳 가까이 있다(움직일 옹벽 · 합성 관문 · 완료 · 오류 …). 자리마다 숨기기를 넣으면 새 창이 생길 때 또 빠진다 —
/// 그래서 창 상태로 가린다: ① AutoCAD 창이 꺼져 있다(주인이 있는 모달 창 — 알림 · 예/아니오 · WPF 모달) ② AutoCAD 스레드에 대화 상자(#32770)가 새로 떠 있다(주인 없이 뜬 예/아니오 —
/// AutoCAD가 앞 창이 아닐 때 WPF MessageBox는 주인 없이 뜬다). 처음부터 떠 있던 대화 상자는 뺀다.</para>
/// <para>창 상태만 읽으므로 다른 스레드(로딩 창 스레드)에서 불러도 된다 — AutoCAD 스레드가 계산으로 묶여 있어도 답한다.</para></summary>
internal sealed class ModalWatch
{
    readonly IntPtr _main;
    readonly uint _tid;
    readonly HashSet<IntPtr> _base;

    /// <param name="main">AutoCAD 창 핸들.</param>
    internal ModalWatch(IntPtr main)
    {
        _main = main;
        _tid = main == IntPtr.Zero ? 0 : GetWindowThreadProcessId(main, out _);
        _base = Dialogs();
    }

    /// <summary>지금 묻는 창이 떠 있나.</summary>
    internal bool Asking()
    {
        if (_main == IntPtr.Zero) return false;
        if (IsWindow(_main) && !IsWindowEnabled(_main)) return true;
        foreach (var h in Dialogs())
            if (!_base.Contains(h)) return true;
        return false;
    }

    /// <summary>AutoCAD 스레드의 보이는 대화 상자(#32770).</summary>
    HashSet<IntPtr> Dialogs()
    {
        var set = new HashSet<IntPtr>();
        if (_tid == 0) return set;
        var cls = new StringBuilder(64);
        EnumThreadWindows(_tid, (h, _) =>
        {
            if (IsWindowVisible(h))
            {
                cls.Clear();
                if (GetClassName(h, cls, cls.Capacity) > 0 && cls.ToString() == "#32770") set.Add(h);
            }
            return true;
        }, IntPtr.Zero);
        return set;
    }

    delegate bool EnumProc(IntPtr hWnd, IntPtr lParam);
    [DllImport("user32.dll")] static extern bool EnumThreadWindows(uint dwThreadId, EnumProc lpfn, IntPtr lParam);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
    [DllImport("user32.dll")] static extern bool IsWindow(IntPtr hWnd);
    [DllImport("user32.dll")] static extern bool IsWindowEnabled(IntPtr hWnd);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr hWnd, StringBuilder name, int max);
}
