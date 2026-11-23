using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace DH.Grading.Civil.Commands;

/// <summary>★★★[JACK 1121 «측량도 보정하기»] DHSURVEYFIX — 측량도 DWG · DXF를 골라 미리보기 창에서 가져올 것을 고르고, 그 범위만 백판(원지반)에서 측량 자료로 바꿔 원지반을 새로 짓는다.
/// <para>원지반이 있어야 한다([원지반 가져오기] 창의 네 번째 단추 — 원지반이 없으면 꺼져 있음). 본체는 <see cref="SurveyFix"/>.</para>
/// <para>화면 없는 시험: <see cref="FileForTest"/>(한 번 쓰고 비움) · 창 없이 꺼진 레이어 빼고 전부 · 여유 <see cref="MarginForTest"/> · 틈 메움 <see cref="GapForTest"/>.</para></summary>
public sealed class SurveyFixCommand
{
    internal static string? FileForTest;
    internal static double MarginForTest = 2.0, GapForTest = 40.0;
    /// <summary>★[JACK 1122] 시험 고리 — 참이면 다음 한 번(화면 없음)은 «측량도만으로 원지반»(원지반과 다른 곳 측량도). 쓰고 나면 거짓.</summary>
    internal static bool SurveyOnlyForTest;
    /// <summary>창 기본값 — 한 세션 동안 마지막에 쓴 값.</summary>
    static double _margin = 2.0, _gap = 40.0;

    /// <summary>★[JACK 1122 «업체마다 레이어명이 다름 — 사용자가 보고 고름»] 지난번 [선택 레이어 사용] 때 쓴 레이어 이름 — 같은 업체 측량도면 그대로 체크해 연다.
    /// %APPDATA%\DH.Grading\survey_layers.txt(한 줄에 하나). 못 읽으면 빈 것(아무것도 체크 안 함).</summary>
    static string LayerFile => System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DH.Grading", "survey_layers.txt");
    static List<string> LoadLayers()
    {
        try { return System.IO.File.Exists(LayerFile) ? System.IO.File.ReadAllLines(LayerFile).Where(l => l.Length > 0).ToList() : new(); } catch { return new(); }
    }
    static void SaveLayers(IEnumerable<string> layers)
    {
        try
        {
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(LayerFile)!);
            System.IO.File.WriteAllLines(LayerFile, layers);
        }
        catch { }
    }
    static string _lastDir = "";

    [CommandMethod("DHSURVEYFIX")]
    public void Run()
    {
        Document doc = AcadApp.DocumentManager.MdiActiveDocument;
        if (doc == null) return;
        GradingSettings.SyncToDocument(doc);
        Editor ed = doc.Editor;
        Database db = doc.Database;
        try
        {
            var grounds = GroundSet.List(db);
            if (grounds.Count == 0)
            {
                Refuse(ed, "원지반이 없습니다.\n\n먼저 [원지반 가져오기] 창의 [수치지도 DXF 가져오기] · [서버에서 가져오기] · [등고선으로 작성하기] 중 하나로 원지반(백판)을 만드세요.");
                return;
            }
            string? path = AskFile();
            if (string.IsNullOrEmpty(path)) { ed.WriteMessage("\n[측량도 보정] 취소"); return; }
            string name = System.IO.Path.GetFileName(path);
            ed.WriteMessage($"\n[측량도 보정] {name} 읽는 중…");
            SurveyFix.Data data;
            var swRead = System.Diagnostics.Stopwatch.StartNew();
            try { data = SurveyFix.Read(path); }
            catch (System.Exception rex)
            {
                Refuse(ed, $"측량도를 읽지 못했습니다 — {name}\n\n{rex.Message}\n\n다른 프로그램이 쓰고 있지 않은지, DWG · DXF가 맞는지 확인하세요.");
                return;
            }
            double readSec = swRead.Elapsed.TotalSeconds;
            string skipped = data.Skipped();
            if (data.Items.Count == 0)
            {
                Refuse(ed, $"측량도에 쓸 등고선 · 표고점이 없습니다 — {name}\n\n읽은 객체 {data.Read}개" + (skipped.Length > 0 ? " · " + skipped : "") +
                           "\n\n표고(Z · 고도)가 든 선 · 점 · 블록이어야 합니다. 블록 안의 선은 분해(EXPLODE)한 뒤 저장해 주세요.");
                return;
            }
            ed.WriteMessage($"\n  읽은 객체 {data.Read}개 → 등고선 {data.Items.Count(i => i.Kind == SurveyKind.Contour)} · 3D 선 {data.Items.Count(i => i.Kind == SurveyKind.Break)} · 표고점 {data.Items.Count(i => i.Kind == SurveyKind.Spot)}"
                          + (skipped.Length > 0 ? " · " + skipped : ""));

            int gi = grounds.Count - 1;   // 기본 = 가장 나중에 만든 원지반
            // ★[JACK 1122 «원지반 만든 곳과 다른 곳의 측량도»] 원지반이 둘 이상이면 측량도와 겹치는 것을 먼저 고름(가장 많이 겹치는 것)
            if (grounds.Count > 1)
                try
                {
                    var sb = SurveyFix.BoxOf(data.Items.Where(i => !data.LayersOff.Contains(i.Layer)), _margin);   // 창 · 합성과 같은 상자(꺼진 레이어 빼고 · 여유)
                    double best = 0;
                    for (int k = 0; k < grounds.Count; k++)
                    {
                        var b = SurveyFix.BaseForView(db, grounds[k].Id);
                        if (!b.Any) continue;
                        double ox = Math.Min(sb.X1, b.X1) - Math.Max(sb.X0, b.X0), oy = Math.Min(sb.Y1, b.Y1) - Math.Max(sb.Y0, b.Y0);
                        if (ox > 0 && oy > 0 && ox * oy > best) { best = ox * oy; gi = k; }
                    }
                }
                catch { }
            List<SurveyItem> use;
            double margin, gap;
            bool surveyOnly = false;
            bool test = FileForTestUsed;
            FileForTestUsed = false;
            if (test || SelfCheckLog.Headless)
            {
                use = data.Items.Where(i => !data.LayersOff.Contains(i.Layer)).ToList();
                margin = MarginForTest; gap = GapForTest;
                surveyOnly = SurveyOnlyForTest; SurveyOnlyForTest = false;
            }
            else
            {
                var swOpen = System.Diagnostics.Stopwatch.StartNew();
                var w = new SurveyFixWindow(name, data.Items, data.LayersOff, grounds.Select(g => g.Name).ToList(), gi,
                                            k => SurveyFix.BaseForView(db, grounds[Math.Clamp(k, 0, grounds.Count - 1)].Id), _margin, _gap,
                                            skipped.Length > 0 ? $"읽은 객체 {data.Read}개 · {skipped}" : $"읽은 객체 {data.Read}개", LoadLayers(), data.LayerArgb, data.Decor);
                // ★[JACK 1123 «창이 느리고 끊김»] 창 닫을 때 한 줄 — 읽기 · 객체 · 점 · 창 뜸 · 그림 · 찾기 · 범위 셈
                double openSec = -1;
                w.ContentRendered += (_, __) => { if (openSec < 0) openSec = swOpen.Elapsed.TotalSeconds; };
                bool? shown = AcadApp.ShowModalWindow(w);
                try
                {
                    long pts = data.Items.Sum(i => (long)i.Pts.Count), dpts = data.Decor.Sum(x => (long)x.Pts.Count);
                    DiagLog.Append($"\n[측량도 창] 읽기 {readSec:0.00}초 · 객체 {data.Items.Count} · 점 {pts} · 블록 모양 점 {dpts} · 창 뜸 {(openSec < 0 ? "-" : openSec.ToString("0.00") + "초")} · {w.PerfSummary}");
                }
                catch { }
                if (shown != true) { ed.WriteMessage("\n[측량도 보정] 취소"); return; }
                use = w.Chosen; gi = Math.Clamp(w.GroundIndex, 0, grounds.Count - 1);
                // ★[검토 1122 LOW] 업체마다 따로 기억 — 이 도면에서 쓴 것은 더하고 안 쓴 것만 뺌(다른 업체 레이어명은 남김)
                var mem = new List<string>(LoadLayers());
                var drop = new HashSet<string>(w.UncheckedLayers, StringComparer.OrdinalIgnoreCase);
                mem.RemoveAll(drop.Contains);
                foreach (var l in w.CheckedLayers) if (!mem.Contains(l, StringComparer.OrdinalIgnoreCase)) mem.Add(l);
                SaveLayers(mem);
                margin = w.Band; gap = w.Gap;
                surveyOnly = w.SurveyOnly;
                _margin = margin; _gap = gap;
            }
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var r = SurveyFix.Merge(doc, use, grounds[gi].Id, margin, gap, surveyOnly);
            try { DiagLog.Append($"\n[측량도 보정] {name} — {(surveyOnly ? "측량도만으로 " : "")}{(r.Ok ? "끝" : "못 함")} · {sw.Elapsed.TotalSeconds:0.0}초 · 쓴 것 {use.Count}개"); } catch { }
            if (!r.Ok) { Refuse(ed, r.Message); return; }
            ed.WriteMessage($"\n[측량도 보정] {name} → 새 '{r.NewName}' — " + r.Message);
            if (!SelfCheckLog.Headless)
                AcadApp.ShowAlertDialog((surveyOnly ? $"측량도만으로 원지반 — 새 원지반 '{r.NewName}'\n\n" : $"측량도 보정 끝 — 새 원지반 '{r.NewName}'\n\n") + r.Message.Replace(" · ", "\n· ").Replace("\n  ", "\n"));
        }
        catch (System.Exception ex)
        {
            ed.WriteMessage("\n[측량도 보정 오류] " + ex.Message);
            try { DiagLog.Append($"\n■ DHSURVEYFIX 오류 — {ex}\n"); } catch { }
            if (!SelfCheckLog.Headless) AcadApp.ShowAlertDialog("측량도 보정 중 오류:\n" + ex.Message);
        }
    }

    static bool FileForTestUsed;

    static string? AskFile()
    {
        if (FileForTest != null) { var t = FileForTest; FileForTest = null; FileForTestUsed = true; return t; }
        if (SelfCheckLog.Headless) return null;
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "측량도 선택 (DWG · DXF)",
            Filter = "측량도 (*.dwg;*.dxf)|*.dwg;*.dxf|모든 파일 (*.*)|*.*",
            CheckFileExists = true,
        };
        try { if (_lastDir.Length > 0 && System.IO.Directory.Exists(_lastDir)) dlg.InitialDirectory = _lastDir; } catch { }
        bool? ok;
        try { ok = dlg.ShowDialog(); } catch { ok = false; }
        if (ok != true) return null;
        try { _lastDir = System.IO.Path.GetDirectoryName(dlg.FileName) ?? ""; } catch { }
        return dlg.FileName;
    }

    static void Refuse(Editor ed, string msg)
    {
        ed.WriteMessage("\n[측량도 보정] " + msg.Replace("\n\n", " — ").Replace("\n", " "));
        try { DiagLog.Append("\n■ DHSURVEYFIX — 거절: " + msg.Replace("\n", " ") + "\n"); } catch { }
        if (!SelfCheckLog.Headless) AcadApp.ShowAlertDialog("측량도 보정 — 하지 못했습니다\n\n" + msg);
    }
}
