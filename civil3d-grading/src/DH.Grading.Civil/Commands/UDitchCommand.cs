using System.Globalization;
using System.IO;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using DH.Grading.Core;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace DH.Grading.Civil.Commands;

/// <summary>★★[배수지 U형 측구 1단계 MVP · 명세서 «배수지_U형측구_애드인_모듈별개발명세서.md» §7] DHUDITCH(창) · -DHUDITCH(명령줄 — 키트 · FILEDIA 0 · 화면 없는 Civil).
/// <para>흐름: 부지 대표점(M01) → 강우(M02 수동 · CSV) → 측구 선(M04 고르기 · 그리기) → 규격 · 바닥고 · 집수(M05) → [측구 생성](모든 측구 다시 · ★[JACK 1124] 도면 = 원 선에 선종류 U만 · 솔리드 없음 · 지표면 안 바꿈 · 3D 보기 = 실제 모양) → 보고서(M08 txt · json).
/// 기록 = 도면 NOD «DH_GRADING/UDITCH»(다시 열면 그대로 · 고침). 셈 = Core <see cref="UDitch"/>.</para></summary>
public sealed class UDitchCommand
{
    /// <summary>도면마다(세션) — 고른 측구 · 불러온 CSV · 지난 일 알림(도킹창이 한 번 보이고 비움).</summary>
    internal sealed class Memo
    {
        public int Sel;
        public List<UDitch.IdfRow> Csv = new();
        public string CsvSource = "", CsvFile = "";
        public string Note = "", Error = "";
    }
    static readonly Dictionary<string, Memo> Memos = new(System.StringComparer.OrdinalIgnoreCase);
    internal static Memo MemoOf(Document doc)
    {
        if (!Memos.TryGetValue(doc.Name ?? "", out var m)) Memos[doc.Name ?? ""] = m = new Memo();
        return m;
    }
    static readonly CultureInfo Ic = CultureInfo.InvariantCulture;

    internal sealed class Ctx
    {
        public Document Doc = null!; public Database Db = null!; public Editor Ed = null!;
        public UDitch.Record Rec = new(); public UDitch.Settings S = new();
        public string SetFrom = "", SetWhy = "";
        public System.Text.StringBuilder Log = new();
    }

    [CommandMethod("DHUDITCH")]
    public void Run() => Start(false);

    [CommandMethod("-DHUDITCH")]
    public void RunLine() => Start(true);

    static void Start(bool lineMode)
    {
        var doc = AcadApp.DocumentManager.MdiActiveDocument; if (doc == null) return;
        var c = new Ctx { Doc = doc, Db = doc.Database, Ed = doc.Editor, Log = new System.Text.StringBuilder("\n■ DHUDITCH(U형 측구)\n") };
        try
        {
            GradingSettings.SyncToDocument(doc);
            c.S = UDitchStore.LoadSettings(out c.SetFrom, out c.SetWhy);
            c.Log.AppendLine($"  설정: {c.SetFrom}{(c.SetWhy.Length > 0 ? " · ⚠" + c.SetWhy : "")}");
            using (var tr = c.Db.TransactionManager.StartTransaction())
            {
                bool ok = UDitchStore.TryLoad(c.Db, tr, out c.Rec, out string why);
                tr.Commit();
                if (!ok) { Done(c, "⚠원인: " + why + " — 기록을 덮지 않으려고 멈춤"); return; }
            }
            UDitch.Link(c.Rec, c.S);   // ★[JACK 1123] 합류 · 교차 · 출구 집수정
            c.Rec.Site = UDitchStore.SiteOf(c.Db, GradingSettings.ExportEpsg, c.Rec.Ditches);
            int fd = 1;
            try { fd = System.Convert.ToInt16(AcadApp.GetSystemVariable("FILEDIA")); } catch { }
            if (lineMode || SelfCheckLog.Headless || fd == 0) RunCmdLine(c);
            else { Done(c, "도킹창 열기"); UDitchPalette.Show(doc); }   // ★[JACK 1122 «측구 기능도 도킹팝업으로»] 창 = 우측 도킹창(명령줄 · 키트는 그대로)
        }
        catch (System.Exception ex) { Done(c, "⚠원인: " + ex.Message + "\n" + ex); }
    }

    static void Done(Ctx c, string msg)
    {
        c.Ed.WriteMessage("\n[U형 측구] " + msg.Split('\n')[0]);
        try { DiagLog.Append(c.Log.ToString() + "  → " + msg + "\n"); } catch { }
    }

    static void SaveRec(Ctx c)
    {
        using var tr = c.Db.TransactionManager.StartTransaction();
        UDitchStore.Save(c.Db, tr, c.Rec);
        tr.Commit();
    }

    // ══════════════════════════════ 도킹창 단추 → 명령 ══════════════════════════════
    // ★[JACK 1122 «측구 기능도 도킹팝업으로 바꾸고 기능을 업데이트»] 창 단추는 아래 명령을 보낸다(도면 일 = 명령 안 · 되돌리기 한 번).
    //   값 고침(강우 · 규격 · 바닥고 · 집수 · 덮개 · 방향)은 창이 기록에 바로 적는다(UDitchPalette). 일이 끝나면 알림은 Memo에 · 창은 다시 읽음.

    /// <summary>창 단추 일 하나 — 기록 읽기 → 일 → 알림(Memo) → 창 다시 읽기.</summary>
    static void PalRun(string what, System.Func<Ctx, Memo, (string Note, string Error)> act, bool echo = true)
    {
        var doc = AcadApp.DocumentManager.MdiActiveDocument; if (doc == null) return;
        var c = new Ctx { Doc = doc, Db = doc.Database, Ed = doc.Editor, Log = new System.Text.StringBuilder($"\n■ DHUDITCH {what}\n") };
        var memo = MemoOf(doc);
        memo.Note = ""; memo.Error = "";   // ★[검토 1122 MEDIUM] 알림은 다음 일이 시작될 때만 비움(창 다시 읽기는 지우지 않음)
        if (what != "도면에서 보기") ClearMark();
        try
        {
            if (!Load(c, out string why)) { memo.Note = ""; memo.Error = why; Done(c, "⚠" + why); return; }
            var (n, e) = act(c, memo);
            memo.Note = n; memo.Error = e;
            if (echo && (n.Length > 0 || e.Length > 0)) Done(c, e.Length > 0 ? "⚠" + e : n);
        }
        catch (System.Exception ex) { memo.Note = ""; memo.Error = ex.Message; Done(c, "⚠원인: " + ex.Message + "\n" + ex); }
        finally { UDitchPalette.Refresh(); }
    }

    /// <summary>설정 · 기록 · 부지 읽기(창 · 단추 명령 공통). 거짓 = 기록이 깨짐(덮지 않으려고 멈춤).</summary>
    internal static bool Load(Ctx c, out string why, bool sync = true)
    {
        // ★[검토 1122 MEDIUM] 창 다시 읽기(도면 탭 바꿈 등)는 sync = 거짓 — SyncToDocument가 정지 세션 기억(마지막 계획 · 원지반 · 옹벽 고른 것)을 지우므로 명령일 때만
        if (sync) GradingSettings.SyncToDocument(c.Doc);
        c.S = UDitchStore.LoadSettings(out c.SetFrom, out c.SetWhy);
        using (var tr = c.Db.TransactionManager.StartTransaction())
        {
            bool ok = UDitchStore.TryLoad(c.Db, tr, out c.Rec, out why);
            tr.Commit();
            if (!ok) { why = why + " — 기록을 덮지 않으려고 멈춤"; return false; }
        }
        UDitch.Link(c.Rec, c.S);   // ★[JACK 1123] 합류 · 교차 · 출구 집수정 · 닫힌 선 갈래(창 목록 · 결과 줄이 보게)
        // ★[재검토 1122 LOW] 창 다시 읽기(sync 거짓)는 좌표계 동기화가 없어 EPSG가 앞 도면 것일 수 있음 — 적힌 부지 그대로(없을 때만 셈)
        if (sync || c.Rec.Site.Source.Contains("없음") || !double.IsFinite(c.Rec.Site.X))
            c.Rec.Site = UDitchStore.SiteOf(c.Db, GradingSettings.ExportEpsg, c.Rec.Ditches);
        return true;
    }

    static bool SelOk(Ctx c, Memo m) => m.Sel >= 0 && m.Sel < c.Rec.Ditches.Count;

    static (string, string) Picked(Memo m, int i, string msg) { m.Sel = i; return (msg, ""); }

    [CommandMethod("DHUDITCHPICK")]
    public void PalPick() => PalRun("선 고르기", (c, m) =>
        PickEntity(c, keywords: false, out var id, out _) && AddDitch(c, id, out int i, out string msg) ? Picked(m, i, msg) : ("", ""));

    [CommandMethod("DHUDITCHDRAW")]
    public void PalDraw() => PalRun("선 그리기", (c, m) =>
        DrawLine(c, out var id) && AddDitch(c, id, out int i, out string msg) ? Picked(m, i, msg) : ("", ""));

    /// <summary>★[JACK 1124 «경계를 선택하고 시점 · 종점을 선택하면 그 부분만 마우스 거리만큼 오프셋»] [경계 따라] — 새 폴리선(<see cref="UDitchEdge"/>) → [선 그리기]와 같은 측구.</summary>
    [CommandMethod("DHUDITCHEDGE")]
    public void PalEdge() => PalRun("경계 따라", (c, m) =>
        UDitchEdge.Draw(c.Doc, out var id, out string note) && AddDitch(c, id, out int i, out string msg) ? Picked(m, i, msg + " · " + note) : ("", ""));

    [CommandMethod("DHUDITCHDEL")]
    public void PalDelete() => PalRun("지우기", (c, m) =>
    {
        if (!SelOk(c, m)) return ("", "지울 측구를 고르세요");
        var d0 = c.Rec.Ditches[m.Sel];
        // ★[JACK 1123] 닫힌 선 갈래는 두 갈래를 같이(선 하나)
        var gone = d0.Loop > 0 ? c.Rec.Ditches.Where(x => x.Loop > 0 && x.LineHandle == d0.LineHandle).ToList() : new List<UDitch.Ditch> { d0 };
        int n = 0;
        using (var tr = c.Db.TransactionManager.StartTransaction())
        {
            foreach (var x in gone) { n += UDitchStore.Erase(c.Db, tr, x.Id); c.Rec.Ditches.Remove(x); }
            if (!c.Rec.Ditches.Any(x => x.LineHandle == d0.LineHandle)) UDitchStore.UnmarkLine(c.Db, tr, d0.LineHandle);   // ★[JACK 1124] 원 선의 선종류 U 되돌림
            UDitchStore.Save(c.Db, tr, c.Rec);
            tr.Commit();
        }
        m.Sel = System.Math.Min(m.Sel, c.Rec.Ditches.Count - 1);
        return ($"{string.Join(" · ", gone.Select(x => x.Id))} 지움(원 선은 그대로{(n > 0 ? $" · 옛 모델 {n}개" : "")})" + (gone.Count > 1 ? " · " + UDitch.LoopText : ""), "");
    });

    [CommandMethod("DHUDITCHBUILD")]
    public void PalBuild() => PalRun("모델 생성", (c, m) => Build(c, out string msg) ? (msg, "") : ("", msg), echo: false);   // Build가 알림 줄을 이미 씀

    /// <summary>[지표면 맞춤] — 원 선 · 지표를 다시 읽고 바닥이 지표를 따라감(단차 → 집수정 + 관로 · 급하면 낙차).</summary>
    [CommandMethod("DHUDITCHFIT")]
    public void PalFit() => PalRun("지표면 맞춤", (c, m) =>
    {
        if (!SelOk(c, m)) return ("", "측구를 고르세요");
        var d = c.Rec.Ditches[m.Sel];
        string h = d.LineHandle, note;
        // ★[JACK 1123] 같은 원 선의 측구(닫힌 선 두 갈래)는 같이 — 다시 읽으면 옛 «한 변 빠진» 측구도 두 갈래로
        foreach (var x in c.Rec.Ditches.Where(x => ReferenceEquals(x, d) || (h.Length > 0 && x.LineHandle == h))) { x.InvertAuto = true; x.Follow = true; }
        using (var tr = c.Db.TransactionManager.StartTransaction())
        {
            note = h.Length > 0 ? UDitchStore.RefreshLine(c.Db, tr, c.Rec, h, c.S) : "";
            foreach (var x in c.Rec.Ditches.Where(x => ReferenceEquals(x, d) || (h.Length > 0 && x.LineHandle == h))) UDitch.ApplyAuto(x, c.S);
            UDitch.Link(c.Rec, c.S);
            UDitchStore.Save(c.Db, tr, c.Rec);
            tr.Commit();
        }
        if (note.Length > 0) c.Log.AppendLine("  " + note);
        UDitchStore.LogJunctions(c.Rec, c.Log);
        int k = c.Rec.Ditches.IndexOf(d);
        if (k >= 0) m.Sel = k;
        var ly = UDitch.LayoutOf(d, c.S);
        foreach (var ln in ly.Log) c.Log.AppendLine("    " + ln);
        return (string.Format(Ic, "{0} — 지표 다시 읽음 · 구간 {1}{2}{3}{4}{5}", d.Id, ly.Segs.Count, ly.Drops.Count > 0 ? $" · 낙차 {ly.Drops.Count}" : "",
                              ly.Basins.Count > 0 ? string.Format(Ic, " · 집수정 {0} · 관 {1:0.0} m", ly.Basins.Count, ly.Pipes.Sum(p => p.Length)) : "",
                              ly.Cuts.Count > 0 ? " · 집수정 " + string.Join("·", ly.Cuts.Select(q => q.Name + " " + q.Kind)) : "",
                              d.Loop > 0 ? " · " + UDitch.LoopText : ""), "");
    });

    /// <summary>★[JACK 1122 기능 업데이트] [면적 고르기] — 닫힌 폴리선 · 원 · 영역 · 해치를 골라 넓이 합 → 고른 측구의 집수면적(ha).</summary>
    [CommandMethod("DHUDITCHAREA")]
    public void PalArea() => PalRun("면적 고르기", (c, m) =>
    {
        if (!SelOk(c, m)) return ("", "측구를 먼저 고르세요");
        var d = c.Rec.Ditches[m.Sel];
        var o = new PromptSelectionOptions { MessageForAdding = $"\n[U형 측구] {d.Id} 집수 구역 — 닫힌 폴리선 · 원 · 영역 · 해치 고르기(여럿이면 합)" };
        var f = new SelectionFilter(new[] { new TypedValue((int)DxfCode.Start, "LWPOLYLINE,POLYLINE,CIRCLE,ELLIPSE,SPLINE,REGION,HATCH") });
        var r = c.Ed.GetSelection(o, f);
        if (r.Status != PromptStatus.OK) return ("", "");
        var (sum, used, open) = AreaOf(c.Db, r.Value.GetObjectIds());
        if (used == 0) return ("", open > 0 ? $"닫힌 것이 없음(열린 선 {open}개) — 닫힌 폴리선 · 원 · 영역 · 해치를 고르세요" : "넓이를 셀 것이 없음");
        d.AreaHa = sum / 10000.0;
        UDitch.SyncLoop(c.Rec, d, c.S);   // ★[JACK 1123] 닫힌 선 갈래 — 짝도 같은 면적(선 전체 · 안전 쪽)
        using (var tr = c.Db.TransactionManager.StartTransaction()) { UDitchStore.Save(c.Db, tr, c.Rec); tr.Commit(); }
        string unit = "";
        try { if (c.Db.Insunits != UnitsValue.Meters && c.Db.Insunits != UnitsValue.Undefined) unit = $" · ⚠도면 단위 {c.Db.Insunits} — m로 가정"; } catch { }
        return (string.Format(Ic, "{0} 집수면적 {1:0.####} ha ({2:N0} ㎡ · {3}개{4}){5}", d.Id, d.AreaHa, sum, used, open > 0 ? $" · 열린 선 {open}개 뺌" : "", unit), "");
    });

    /// <summary>닫힌 것들의 넓이 합(㎡) · 쓴 수 · 열린 선 수 — 화면 없는 시험도 이것.</summary>
    internal static (double Sum, int Used, int Open) AreaOf(Database db, IEnumerable<ObjectId> ids)
    {
        double sum = 0; int used = 0, open = 0;
        using var tr = db.TransactionManager.StartTransaction();
        // ★[검토 1122 MEDIUM] 해치와 그 경계선을 같이 고르면 두 번 셈 → 고른 해치의 연관 경계선은 뺌
        var idList = ids.ToList();
        var skip = new HashSet<ObjectId>();
        foreach (var id in idList)
            try { if (tr.GetObject(id, OpenMode.ForRead) is Hatch hh && hh.Associative) foreach (ObjectId b in hh.GetAssociatedObjectIds()) skip.Add(b); } catch { }
        foreach (var id in idList)
            try
            {
                if (skip.Contains(id)) continue;
                var e = tr.GetObject(id, OpenMode.ForRead);
                double a = double.NaN;
                if (e is Region rg) a = rg.Area;
                else if (e is Hatch h) a = h.Area;
                else if (e is Curve cv)
                {
                    bool closed = cv.Closed;
                    try { if (!closed && cv.StartPoint.DistanceTo(cv.EndPoint) < 1e-6) closed = true; } catch { }
                    if (closed) a = cv.Area; else open++;
                }
                if (double.IsFinite(a) && a > 0) { sum += a; used++; }
            }
            catch { }
        tr.Commit();
        return (sum, used, open);
    }

    /// <summary>★[JACK 1122 기능 업데이트] [도면에서 보기] — 고른 측구 선으로 화면 이동 + 빨간 선으로 표시(다른 단추 · 다른 측구를 고르면 지움).</summary>
    [CommandMethod("DHUDITCHZOOM")]
    public void PalZoom() => PalRun("도면에서 보기", (c, m) =>
    {
        if (!SelOk(c, m)) return ("", "측구를 고르세요");
        var d = c.Rec.Ditches[m.Sel];
        var pts = UDitch.CleanPts(d.Pts);
        if (pts.Count < 2) return ("", $"{d.Id} 선 점이 없음 — [선 고르기]로 다시 읽으세요");
        double x0 = pts.Min(p => p[0]), y0 = pts.Min(p => p[1]), x1 = pts.Max(p => p[0]), y1 = pts.Max(p => p[1]);
        GroundOffCommand.ZoomToExt(c.Ed, new Extents3d(new Point3d(x0, y0, 0), new Point3d(x1, y1, 0)));
        ClearMark();
        try { _mark = PickMark.PaintPath(c.Doc, pts.Select(p => new Point3(p[0], p[1], p.Length > 2 && double.IsFinite(p[2]) ? p[2] : 0)).ToList(), false); } catch { }
        return ($"{d.Id} — 도면에 빨간 선으로 표시(다른 측구를 고르면 지움)", "");
    });

    static PickMark? _mark;
    /// <summary>[도면에서 보기] 빨간 선 지우기 — 창에서 다른 측구를 고를 때 · 다른 단추 일 전에.</summary>
    internal static void ClearMark() { try { _mark?.Dispose(); } catch { } _mark = null; }

    static string SiteWarn(Ctx c)
    {
        var w = new List<string>();
        if (!double.IsFinite(c.Rec.Site.Lat)) w.Add("경위도 변환 못 함 — 좌표계 확인([공통설정])");
        try { if (c.Db.Insunits != UnitsValue.Meters && c.Db.Insunits != UnitsValue.Undefined) w.Add($"도면 단위 {c.Db.Insunits} — m로 가정"); } catch { }
        return string.Join(" · ", w);
    }

    // ══════════════════════════════ 명령줄(키트 · FILEDIA 0 · 화면 없는 Civil) ══════════════════════════════

    static void RunCmdLine(Ctx c)
    {
        var ed = c.Ed;
        // ① 강우
        var ko = new PromptKeywordOptions($"\n[U형 측구] 강우 — 지금: {c.Rec.Rain.Text}") { AllowNone = true };
        ko.Keywords.Add("Keep", "K", "그대로(K)");
        ko.Keywords.Add("Manual", "M", "수동(M)");
        ko.Keywords.Add("Csv", "C", "CSV(C)");
        ko.Keywords.Add("None", "N", "없음(N)");
        ko.Keywords.Default = "Keep";
        var kr = ed.GetKeywords(ko);
        if (kr.Status != PromptStatus.OK && kr.Status != PromptStatus.None) { ed.WriteMessage("\n[U형 측구] 취소"); return; }
        string kw = kr.Status == PromptStatus.None ? "Keep" : kr.StringResult;
        var r = c.Rec.Rain;
        if (kw == "Manual")
        {
            if (Dbl(ed, "재현기간(년)", r.ReturnYears > 0 ? r.ReturnYears : 30, false) is not double y) return;
            if (Dbl(ed, "지속기간(분)", r.DurationMin > 0 ? r.DurationMin : 10, false) is not double m) return;
            if (Dbl(ed, "강우강도(mm/h · 0 = 자료 미확보)", r.Has ? r.IntensityMmh : 0, false) is not double i) return;
            if (Str(ed, "출처", r.Source) is not string src) return;
            if (Str(ed, "기준연도 · 버전", r.SourceVersion) is not string ver) return;
            c.Rec.Rain = new UDitch.Rain { ReturnYears = y, DurationMin = m, IntensityMmh = i, Source = src, SourceVersion = ver, Method = "수동", Entered = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm", Ic) };
        }
        else if (kw == "Csv")
        {
            if (Str(ed, "CSV 파일", "") is not string path || path.Length == 0) return;
            path = path.Trim('"');
            List<UDitch.IdfRow> rows; string src; List<string> errs;
            try { rows = UDitch.ParseIdfCsv(File.ReadAllText(path), out src, out errs); }
            catch (System.Exception ex) { ed.WriteMessage("\n[U형 측구] ⚠CSV를 못 읽음 — " + ex.Message); return; }
            ed.WriteMessage($"\n[U형 측구] CSV {rows.Count}줄" + (errs.Count > 0 ? $" · 뺀 줄 {errs.Count}(첫: {errs[0]})" : ""));
            if (Dbl(ed, "재현기간(년)", 30, false) is not double y) return;
            if (Dbl(ed, "지속기간(분)", 10, false) is not double m) return;
            var row = UDitch.Find(rows, y, m);
            if (row == null) ed.WriteMessage(string.Format(Ic, "\n[U형 측구] ⚠표에 {0:0.#}년 · {1:0.#}분 줄이 없음(보간 안 함) — 강우 그대로", y, m));
            else c.Rec.Rain = new UDitch.Rain { ReturnYears = y, DurationMin = m, IntensityMmh = row.IntensityMmh, Source = src.Length > 0 ? src : Path.GetFileName(path), Method = "CSV", File = path, Entered = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm", Ic) };
        }
        else if (kw == "None") c.Rec.Rain = new UDitch.Rain();
        ed.WriteMessage("\n[U형 측구] 강우: " + c.Rec.Rain.Text);
        SaveRec(c);

        // ② 측구 선(Enter = 새 선 없이 있는 측구만 다시 지음)
        if (!PickEntity(c, keywords: true, out var id, out bool none)) { ed.WriteMessage("\n[U형 측구] 취소"); return; }
        if (!none)
        {
            if (!AddDitch(c, id, out int k, out string msg)) { ed.WriteMessage("\n[U형 측구] ⚠" + msg); return; }
            ed.WriteMessage("\n[U형 측구] " + msg);
            var d = c.Rec.Ditches[k];
            // ③ 규격 · 바닥고 · 집수
            while (true)
            {
                if (Str(ed, $"규격({string.Join(" ", c.S.Sizes.Select(z => z.Label))})", d.Size.Label) is not string sz) return;
                var hit = MatchSize(c.S, sz);
                if (hit != null) { d.Size = hit.Copy(); UDitch.ApplyAuto(d, c.S); break; }
                ed.WriteMessage("\n[U형 측구] 표에 없는 규격 — 예: 300 · 400×400 · 500x500");
            }
            if (Dbl(ed, "시점 바닥고(m)", d.StartInvert, true) is not double a) return;
            if (Dbl(ed, "종점 바닥고(m)", d.EndInvert, true) is not double b) return;
            if (System.Math.Abs(a - d.StartInvert) > 1e-9 || System.Math.Abs(b - d.EndInvert) > 1e-9 || !double.IsFinite(d.StartInvert)) { d.StartInvert = a; d.EndInvert = b; d.InvertAuto = false; }
            if (Dbl(ed, "집수면적(ha)", d.AreaHa, false) is not double ar) return;
            if (Dbl(ed, "유출계수 C", d.C, false) is not double cc) return;
            d.AreaHa = System.Math.Max(0, ar); d.C = System.Math.Clamp(cc, 0, 1);
            UDitch.SyncLoop(c.Rec, d, c.S);   // ★[JACK 1123] 닫힌 선 갈래 — 규격 · 덮개 · 집수 같게
            SaveRec(c);
        }
        if (c.Rec.Ditches.Count == 0) { ed.WriteMessage("\n[U형 측구] 측구가 없음 — 끝"); return; }
        // ④ 모델 · 보고서(한 줄 알림은 Build가 씀)
        Build(c, out _);
    }

    static UDitch.Size? MatchSize(UDitch.Settings s, string text)
    {
        string t = text.Trim().Replace('x', '×').Replace('X', '×').Replace("*", "×").Replace("U형", "").Trim();
        var hit = s.Sizes.FirstOrDefault(z => z.Label == t);
        if (hit != null) return hit;
        if (double.TryParse(t, NumberStyles.Float, Ic, out double mm)) hit = s.Sizes.FirstOrDefault(z => System.Math.Abs(z.B * 1000 - mm) < 0.5);
        return hit;
    }

    static double? Dbl(Editor ed, string what, double def, bool neg)
    {
        var o = new PromptDoubleOptions($"\n[U형 측구] {what}") { AllowNone = true, AllowNegative = neg, AllowZero = true };
        if (double.IsFinite(def)) { o.DefaultValue = def; o.UseDefaultValue = true; }
        var r = ed.GetDouble(o);
        if (r.Status == PromptStatus.OK) return r.Value;
        if (r.Status == PromptStatus.None && double.IsFinite(def)) return def;
        ed.WriteMessage("\n[U형 측구] 취소");
        return null;
    }

    static string? Str(Editor ed, string what, string def)
    {
        var o = new PromptStringOptions($"\n[U형 측구] {what}") { AllowSpaces = true };
        if (def.Length > 0) { o.DefaultValue = def; o.UseDefaultValue = true; }
        var r = ed.GetString(o);
        if (r.Status == PromptStatus.OK) return (r.StringResult ?? "").Trim();
        ed.WriteMessage("\n[U형 측구] 취소");
        return null;
    }

    // ══════════════════════════════ 선 ══════════════════════════════

    /// <summary>측구 선 고르기 — <paramref name="keywords"/>면 «마지막(L)» = 모형 공간의 마지막 선 · «그리기(D)» · Enter = 없음(<paramref name="none"/>). Esc = 거짓.</summary>
    static bool PickEntity(Ctx c, bool keywords, out ObjectId id, out bool none)
    {
        id = ObjectId.Null; none = false;
        var ed = c.Ed;
        while (true)
        {
            var o = new PromptEntityOptions(keywords ? "\n[U형 측구] 측구 선(시점 = 상류) 또는 <새 선 없이 다시 짓기>" : "\n[U형 측구] 측구 선(그린 방향 = 흐름 · 시점 = 상류)") { AllowNone = keywords };
            o.SetRejectMessage("\n폴리선 · 3D 폴리선 · 선만 됩니다");
            o.AddAllowedClass(typeof(Polyline), true);
            o.AddAllowedClass(typeof(Polyline3d), true);
            o.AddAllowedClass(typeof(Line), true);
            if (keywords) { o.Keywords.Add("Last", "L", "마지막(L)"); o.Keywords.Add("Draw", "D", "그리기(D)"); }
            var r = ed.GetEntity(o);
            if (r.Status == PromptStatus.OK) { id = r.ObjectId; return true; }
            if (r.Status == PromptStatus.None) { none = true; return true; }
            if (r.Status == PromptStatus.Keyword && r.StringResult == "Draw") return DrawLine(c, out id);
            if (r.Status == PromptStatus.Keyword && r.StringResult == "Last")
            {
                id = LastCurve(c.Db);
                if (!id.IsNull) return true;
                ed.WriteMessage("\n[U형 측구] 모형 공간에 선이 없음");
                continue;
            }
            return false;
        }
    }

    static ObjectId LastCurve(Database db)
    {
        var last = ObjectId.Null;
        using var tr = db.TransactionManager.StartTransaction();
        var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
        foreach (ObjectId e in ms)
            if (!e.IsErased && (e.ObjectClass.IsDerivedFrom(RXObject.GetClass(typeof(Polyline))) || e.ObjectClass.IsDerivedFrom(RXObject.GetClass(typeof(Polyline3d))) || e.ObjectClass.IsDerivedFrom(RXObject.GetClass(typeof(Line)))))
                last = e;
        tr.Commit();
        return last;
    }

    /// <summary>평면에서 점을 차례로(U = 마지막 점 빼기 · Enter = 끝) → 폴리선(레이어 DH-U형측구-선). Esc = 거짓.</summary>
    static bool DrawLine(Ctx c, out ObjectId id)
    {
        id = ObjectId.Null;
        var ed = c.Ed;
        var ucs = ed.CurrentUserCoordinateSystem;
        var pts = new List<Point3d>();
        PickMark? mark = null;
        try
        {
            while (true)
            {
                var o = new PromptPointOptions(pts.Count == 0 ? "\n[U형 측구] 시점(상류)" : $"\n[U형 측구] 다음 점({pts.Count}개) · Enter = 끝") { AllowNone = pts.Count >= 2 };
                if (pts.Count > 0) { o.UseBasePoint = true; o.BasePoint = pts[^1].TransformBy(ucs.Inverse()); o.UseDashedLine = true; o.Keywords.Add("Undo", "U", "빼기(U)"); }
                var r = ed.GetPoint(o);
                if (r.Status == PromptStatus.Keyword) { if (pts.Count > 0) pts.RemoveAt(pts.Count - 1); }
                else if (r.Status == PromptStatus.None) break;
                else if (r.Status != PromptStatus.OK) return false;
                else
                {
                    var w = r.Value.TransformBy(ucs);
                    if (pts.Count > 0 && System.Math.Abs(w.X - pts[^1].X) < 1e-3 && System.Math.Abs(w.Y - pts[^1].Y) < 1e-3) continue;
                    pts.Add(w);
                }
                try { mark?.Dispose(); } catch { }
                mark = pts.Count >= 2 ? PickMark.PaintPath(c.Doc, pts.Select(p => new Point3(p.X, p.Y, p.Z)).ToList(), false) : null;
            }
        }
        finally { try { mark?.Dispose(); } catch { } }
        if (pts.Count < 2) return false;
        using var tr = c.Db.TransactionManager.StartTransaction();
        var pl = new Polyline();
        for (int i = 0; i < pts.Count; i++) pl.AddVertexAt(i, new Point2d(pts[i].X, pts[i].Y), 0, 0, 0);
        pl.LayerId = GradingBuilder.EnsureLayer(c.Db, tr, UDitchStore.LineLayer, 4);
        var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(c.Db), OpenMode.ForWrite);
        id = ms.AppendEntity(pl); tr.AddNewlyCreatedDBObject(pl, true);
        tr.Commit();
        return true;
    }

    /// <summary>선 → 측구(같은 선을 또 고르면 그 측구를 다시 읽음) · 지표면 · 기본 바닥고 · 기록 저장. 돌려줌 = 그 측구 차례.</summary>
    static bool AddDitch(Ctx c, ObjectId id, out int index, out string msg)
    {
        index = -1; msg = "";
        using var tr = c.Db.TransactionManager.StartTransaction();
        if (!UDitchStore.ReadLine(tr, id, c.S.ArcStep, out var pts, out bool z3, out bool closed, out string why)) { tr.Commit(); msg = why; c.Ed.WriteMessage("\n[U형 측구] ⚠" + why); return false; }
        string h = id.Handle.ToString();
        int k = c.Rec.Ditches.FindIndex(d => d.LineHandle == h);
        // ★[JACK 1123 «닫힌 폴리선 → 출구에서 두 갈래»] 닫힌 선 = 측구 둘(시작 = 가장 높은 곳 → 양쪽 → 출구 = 가장 낮은 곳 · 출구에 집수정)
        if (closed || (k >= 0 && c.Rec.Ditches[k].Loop > 0))
        {
            if (k < 0) c.Rec.Ditches.Add(new UDitch.Ditch { Id = UDitch.NextId(c.Rec.Ditches), LineHandle = h, Size = c.S.Sizes[0].Copy(), C = c.S.DefaultC, Follow = true });
            string note = UDitchStore.RefreshLine(c.Db, tr, c.Rec, h, c.S);
            UDitch.Link(c.Rec, c.S);
            if (c.Rec.Site.Source.Contains("없음")) c.Rec.Site = UDitchStore.SiteOf(c.Db, GradingSettings.ExportEpsg, c.Rec.Ditches);
            UDitchStore.Save(c.Db, tr, c.Rec);
            UDitchStore.MarkLine(c.Db, tr, h, UDitchStore.EnsureLinetype(c.Db, tr));   // ★[JACK 1124] 측구 선 = 선종류 U
            tr.Commit();
            index = c.Rec.Ditches.FindIndex(d => d.LineHandle == h);
            var grp = c.Rec.Ditches.Where(d => d.LineHandle == h).ToList();
            var jn = c.Rec.Junctions.FirstOrDefault(j => j.M.Count(x => x.D.LineHandle == h && x.Role == "end") == 2);
            msg = (grp.Count == 2 ? $"{grp[0].Id} · {grp[1].Id} — {UDitch.LoopText} · 길이 {UDitch.LengthText(grp[0], c.S, "0.00")} + {UDitch.LengthText(grp[1], c.S, "0.00")}"
                                  : $"{grp[0].Id} — 길이 {UDitch.LengthText(grp[0], c.S, "0.00")}")
                  + (jn != null ? $" · 출구 {jn.Name}" : "") + (grp.Count > 0 && grp[0].LoopNote.Length > 0 ? " · " + grp[0].LoopNote : "") + (note.Length > 0 && !note.Contains("→ D-") ? " · " + note : "");
            c.Log.AppendLine("  " + msg);
            UDitchStore.LogJunctions(c.Rec, c.Log);
            return index >= 0;
        }
        // ★[v117.16 · JACK 1112] 새 측구 = 지표 따라감(단차 → 집수정 + 관로 · 급하면 낙차) · 옛 측구는 그대로(한 경사 — [지표면 맞춤]으로 바꿈)
        var d = k >= 0 ? c.Rec.Ditches[k] : new UDitch.Ditch { Id = UDitch.NextId(c.Rec.Ditches), LineHandle = h, Size = c.S.Sizes[0].Copy(), C = c.S.DefaultC, Follow = true };
        UDitchStore.FillGround(c.Db, tr, d, pts, z3, c.S);
        UDitch.ApplyAuto(d, c.S);
        // ★[JACK 1122] 새 측구는 흐름 방향을 지표로 정함(높은 쪽 → 낮은 쪽) — 그린 차례가 오르막이면 측구가 통째로 묻힘. 다시 고른 옛 측구는 [방향 바꾸기] 그대로.
        string dirNote = k < 0 ? UDitch.AutoDirection(d, c.S) : "";
        if (k < 0) { c.Rec.Ditches.Add(d); k = c.Rec.Ditches.Count - 1; }
        UDitch.Link(c.Rec, c.S);   // ★[JACK 1123] 합류 · 교차 집수정
        if (c.Rec.Site.Source.Contains("없음")) c.Rec.Site = UDitchStore.SiteOf(c.Db, GradingSettings.ExportEpsg, c.Rec.Ditches);
        UDitchStore.Save(c.Db, tr, c.Rec);
        UDitchStore.MarkLine(c.Db, tr, h, UDitchStore.EnsureLinetype(c.Db, tr));   // ★[JACK 1124] 측구 선 = 선종류 U
        tr.Commit();
        index = k;
        var lay = UDitch.LayoutOf(d, c.S);
        msg = string.Format(Ic, "{0} — 길이 {1} · 지표면 {2}{3}{4}", d.Id, UDitch.LengthText(d, c.S, "0.00"), d.Surface,
                            lay.Basins.Count > 0 ? $" · 단차 {lay.Basins.Count} → 집수정 + 관로" : "", why.Length > 0 ? " · ⚠" + why : "")
              + (dirNote.Length > 0 ? " · " + dirNote : "")
              + (d.Cuts.Count > 0 ? " · 집수정 " + string.Join("·", d.Cuts.Select(q => q.Name + " " + q.Kind)) : "");
        c.Log.AppendLine("  " + msg);
        UDitchStore.LogJunctions(c.Rec, c.Log);
        foreach (var ln in lay.Log) c.Log.AppendLine("    " + ln);
        return true;
    }

    // ══════════════════════════════ 모델 · 보고서 ══════════════════════════════

    static bool Build(Ctx c, out string msg)
    {
        c.Log.AppendLine("  측구 생성(선종류 U · 솔리드 없음 · 지표면 안 바꿈):");
        UDitchStore.BuildStats st;
        try { st = UDitchStore.Build(c.Db, c.Rec, c.S, c.Log); }
        catch (System.Exception ex) { msg = "모델을 못 지음 — " + ex.Message; Done(c, "⚠" + msg + "\n" + ex); return false; }
        string rep = UDitchStore.WriteReport(c.Db, c.Doc.Name ?? "", c.Rec, c.S, out string rwhy);
        int shortN = c.Rec.Ditches.Count(d => UDitch.Check(d, c.Rec.Rain, c.S).Verdict == UDitch.Verdict.Short);
        // ★[JACK 1124] 도면 = 선종류 U 선만(솔리드 없음) · 옛 솔리드는 지움
        msg = string.Format(Ic, "측구 {0}개(U 선 {1}{2}) · 길이 {3:0.00} m · 콘크리트 {4:0.00} ㎥", c.Rec.Ditches.Count - st.Skipped, st.Lines, st.Erased > 0 ? $" · 옛 솔리드 {st.Erased} 지움" : "", st.Length, st.BodyVol + st.FootVol + st.ExtraVol)
              + (st.Drops > 0 ? $" · 낙차 {st.Drops}" : "") + (st.Basins - st.Junctions > 0 ? string.Format(Ic, " · 집수정 {0} · 관 {1:0.0} m", st.Basins - st.Junctions, st.PipeLen) : "")   // ★[v117.16]
              + (st.Junctions > 0 ? $" · 합류 · 교차 · 출구 집수정 {st.Junctions}" + (st.JunctionWarn > 0 ? $"(⚠{st.JunctionWarn} — 보고서 확인)" : "") : "")   // ★[JACK 1123]
              + (st.GrateLen > 0 ? string.Format(Ic, " · 그레이팅 {0:0.0} m", st.GrateLen) : "")
              + (!c.Rec.Rain.Has ? " · 강우자료 없음" : shortN > 0 ? $" · ⚠수리 부족 {shortN}" : "")
              + (st.Skipped > 0 ? $" · ⚠못 지음 {st.Skipped}({st.FirstFail})" : st.FirstFail.Length > 0 ? " · ⚠" + st.FirstFail : "")
              + (rep.Length > 0 ? " · 보고서 " + Path.GetFileName(rep) : " · ⚠" + rwhy);
        c.Log.AppendLine($"  → {msg} · 지운 옛 모델 {st.Erased} · 대신 그린 선 {st.StandIn}" + (rep.Length > 0 ? " · " + rep : ""));
        Done(c, msg);
        return st.Skipped < c.Rec.Ditches.Count;
    }
}
