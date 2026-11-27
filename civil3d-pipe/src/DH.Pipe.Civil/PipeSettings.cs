using Microsoft.Win32;

namespace DH.Pipe.Civil;

/// <summary>애드인 판 · 사용자 기본값(레지스트리). 부지정지와 키를 따로 쓴다.</summary>
public static class PipeSettings
{
    /// <summary>판 — 도구가 이 줄을 읽는다(형식 유지).</summary>
    public const string Version = "v0.2.0 (2026-11-27)";

    public const string ProductName = "DH 관로";
    internal const string PrefsRegKey = @"Software\DHPipe";

    internal static int LoadInt(string name, int fallback)
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(PrefsRegKey);
            return k?.GetValue(name) is int v ? v : fallback;
        }
        catch { return fallback; }
    }

    internal static void SaveInt(string name, int value)
    {
        try
        {
            using var k = Registry.CurrentUser.CreateSubKey(PrefsRegKey);
            k?.SetValue(name, value, RegistryValueKind.DWord);
        }
        catch { }
    }

    internal static string LoadText(string name, string fallback)
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(PrefsRegKey);
            return k?.GetValue(name) as string ?? fallback;
        }
        catch { return fallback; }
    }

    internal static void SaveText(string name, string value)
    {
        try
        {
            using var k = Registry.CurrentUser.CreateSubKey(PrefsRegKey);
            k?.SetValue(name, value, RegistryValueKind.String);
        }
        catch { }
    }
}

/// <summary>진단 기록. 위치 = 환경변수 DH_PIPE_LOG_DIR(시험) 아니면 %LOCALAPPDATA%\DHPipe.</summary>
public static class PipeLog
{
    private static readonly object Gate = new();
    private static string? _path;

    public static bool Headless { get; } = DetectHeadless();

    private static bool DetectHeadless()
    {
        try { return System.Diagnostics.Process.GetCurrentProcess().ProcessName.Contains("accoreconsole", StringComparison.OrdinalIgnoreCase); }
        catch { return false; }
    }

    public static string FilePath
    {
        get
        {
            if (_path != null) return _path;
            string? dir = Environment.GetEnvironmentVariable("DH_PIPE_LOG_DIR");
            if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
                dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DHPipe");
            try { Directory.CreateDirectory(dir); } catch { }
            return _path = Path.Combine(dir, "DHPIPE_진단.log");
        }
    }

    public static void Append(string text)
    {
        try
        {
            lock (Gate)
            {
                var fi = new FileInfo(FilePath);
                if (fi.Exists && fi.Length > 5_000_000)
                {
                    string old = FilePath + ".old";
                    try { File.Delete(old); File.Move(FilePath, old); } catch { }
                }
                File.AppendAllText(FilePath, text.StartsWith('\n') ? text : "\n" + text);
            }
        }
        catch { }
    }

    public static void Line(string text) => Append($"\n[{DateTime.Now:MM-dd HH:mm:ss}] {text}");
}
