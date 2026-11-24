using System;
using System.Collections.Generic;
using System.IO;
using Chapter = DH.Grading.Civil.Tutorial.TutorialFlow.Chapter;

namespace DH.Grading.Civil.Tutorial;

/// <summary>★[튜토리얼 · JACK 1117 «단원 · 한 지표면에서 이어서 · 중간 단원은 저장된 자료로»] 튜토리얼 자료 자리.
/// <para>기본 자료(설치 번들 <c>Contents\Tutorial\</c> — 공개 저장소에는 없음 · .gitignore): <c>tut_base.dwg</c>(원지반 + 계획부지선 + 터파기선) · <c>tut_ch3.dwg</c>(정지 끝낸 꼴 — 도면화 시작) · <c>tut_ch5.dwg</c>(진입도로 시작 — 정지 끝 + 노선 선) · <c>tut_ch1.dxf</c>(수치지도).
/// 같은 이름이 <c>%LOCALAPPDATA%\DHGrading\tutorial\data\</c>에 있으면 그쪽이 먼저(설치 전 시험용).</para>
/// <para>끝낸 단원 표시 <c>%LOCALAPPDATA%\DHGrading\tutorial\progress4.txt</c>(✓). ★[JACK 1124 «내 것으로 이어서 없애»] 단원 결과 도면은 안 남김.
/// 단원은 늘 기본 자료의 <b>사본</b>(<c>work\</c>)을 연다 — 기본 자료는 그대로.</para>
/// <para>Autodesk 형을 안 쓴다(화면 없이 시험 가능).</para></summary>
internal static class TutorialData
{
    internal static string Home => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DHGrading", "tutorial");

    static string BundleDir => Path.Combine(
        Path.GetDirectoryName(typeof(TutorialData).Assembly.Location) ?? "", "Tutorial");

    /// <summary>기본 자료 파일 — 없으면 null. LOCALAPPDATA\…\data가 먼저 · 다음 번들.</summary>
    internal static string? DataFile(string name)
    {
        foreach (var d in new[] { Path.Combine(Home, "data"), BundleDir })
        {
            try { string p = Path.Combine(d, name); if (File.Exists(p)) return p; } catch { }
        }
        return null;
    }

    /// <summary>★[JACK 1124 단원 4개] 단원 시작 기본 자료 — 2 계획부지 = <c>tut_base.dwg</c>(원지반 + 계획부지선 + 터파기선) ·
    /// 3 진입도로 = <c>tut_ch5.dwg</c>(정지 끝낸 꼴 + 노선 따라 그릴 선 «TUT-노선») · 4 도면화 = <c>tut_ch4.dwg</c>(있으면) 아니면 <c>tut_ch3.dwg</c>(정지 끝낸 꼴).</summary>
    internal static string? StartDwg(Chapter c) => c switch
    {
        Chapter.Ground => null,
        Chapter.Site => DataFile("tut_base.dwg"),
        Chapter.Road => DataFile("tut_ch5.dwg"),
        _ => DataFile("tut_ch4.dwg") ?? DataFile("tut_ch3.dwg"),
    };
    internal static string? DxfFile => DataFile("tut_ch1.dxf");

    // ── 끝낸 단원 — ★[JACK 1124] 단원이 4개로 바뀜 → 새 파일(옛 번호 3 · 5와 섞이지 않게) ──
    static string ProgressFile => Path.Combine(Home, "progress4.txt");

    internal static HashSet<int> DoneSet()
    {
        var s = new HashSet<int>();
        try
        {
            if (File.Exists(ProgressFile))
                foreach (var ln in File.ReadAllLines(ProgressFile))
                    if (int.TryParse(ln.Trim(), out int n) && n >= 1 && n <= TutorialFlow.ChapterCount) s.Add(n);
        }
        catch { }
        return s;
    }

    internal static void MarkDone(Chapter c)
    {
        try
        {
            var s = DoneSet();
            if (!s.Add((int)c)) return;
            Directory.CreateDirectory(Home);
            var l = new List<int>(s); l.Sort();
            File.WriteAllLines(ProgressFile, l.ConvertAll(n => n.ToString()));
        }
        catch { }
    }

    /// <summary>열 사본 — work\chN_시각.dwg로 복사해 그 경로를 돌려줌(원본은 안 건드림). 실패하면 null + 까닭.</summary>
    internal static string? CopyToWork(string src, Chapter c, out string why)
    {
        why = "";
        try
        {
            string dir = Path.Combine(Home, "work");
            Directory.CreateDirectory(dir);
            TidyWork(dir);
            string dst = Path.Combine(dir, $"ch{(int)c}_{DateTime.Now:yyyyMMdd_HHmmss}.dwg");
            File.Copy(src, dst, overwrite: false);
            File.SetAttributes(dst, FileAttributes.Normal);   // 기본 자료가 읽기 전용이어도 사본은 쓰기
            return dst;
        }
        catch (Exception ex) { why = ex.Message; return null; }
    }

    /// <summary>work 폴더 정리 — 7일 넘은 사본(열려 있으면 못 지움 → 그대로).</summary>
    static void TidyWork(string dir)
    {
        try
        {
            foreach (var f in Directory.GetFiles(dir))
                try { if (DateTime.Now - File.GetLastWriteTime(f) > TimeSpan.FromDays(7)) File.Delete(f); } catch { }
        }
        catch { }
    }
}
