using System.IO;
using System.Text.Json;
using System.Windows;

namespace DH.Takeoff.Revit;

/// <summary>
/// 「수량산출」 창의 크기 · 자리 기억(R62 — JACK 2026-10-26 «지금처럼 크게 + 기억»): 바꾼 크기 · 자리 · 최대화를 PC 설정 파일에 두고 다음에 그대로.
/// 모델 자료가 아니다(창 크기는 사람 · 모니터의 것) — %APPDATA%\DH.Takeoff\window.json. 그 자리가 지금 화면 밖이면(모니터를 뺐음) 쓰지 않는다.
/// </summary>
public static class WindowMemory
{
    private sealed class Placement
    {
        public double Left { get; set; }
        public double Top { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public bool Maximized { get; set; }
    }

    private static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DH.Takeoff", "window.json");

    /// <summary>기억한 크기 · 자리를 창에 입힌다(없거나 화면 밖이면 그대로).</summary>
    public static void Apply(Window window, Action<string>? log = null)
    {
        try
        {
            if (!File.Exists(FilePath)) return;
            var p = JsonSerializer.Deserialize<Placement>(File.ReadAllText(FilePath));
            if (p == null || p.Width < 200 || p.Height < 200) return;
            // 창의 가운데가 지금 화면(모든 모니터) 안이어야 한다.
            double cx = p.Left + p.Width / 2, cy = p.Top + p.Height / 2;
            if (cx < SystemParameters.VirtualScreenLeft || cy < SystemParameters.VirtualScreenTop ||
                cx > SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth || cy > SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight)
            {
                log?.Invoke("창 자리\t기억한 자리가 화면 밖 — 처음 자리");
                return;
            }
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Left = p.Left;
            window.Top = p.Top;
            window.Width = Math.Max(window.MinWidth, p.Width);
            window.Height = Math.Max(window.MinHeight, p.Height);
            if (p.Maximized) window.WindowState = WindowState.Maximized;
        }
        catch (Exception ex) { log?.Invoke("창 자리\t읽기 오류\t" + ex.Message); }
    }

    /// <summary>지금 크기 · 자리를 적는다(최대화면 최대화 전의 크기).</summary>
    public static void Save(Window window, Action<string>? log = null)
    {
        try
        {
            var bounds = window.WindowState == WindowState.Normal ? new Rect(window.Left, window.Top, window.Width, window.Height) : window.RestoreBounds;
            if (bounds.IsEmpty || bounds.Width < 200) return;
            var p = new Placement { Left = bounds.Left, Top = bounds.Top, Width = bounds.Width, Height = bounds.Height, Maximized = window.WindowState == WindowState.Maximized };
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(p));
        }
        catch (Exception ex) { log?.Invoke("창 자리\t적기 오류\t" + ex.Message); }
    }
}
