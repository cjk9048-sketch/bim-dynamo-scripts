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
/// <para>흐름: 부지 대표점(M01) → 강우(M02 수동 · CSV) → 측구 선(M04 고르기 · 그리기) → 규격 · 바닥고 · 집수(M05) → [모델 생성](M06 솔리드 · 모든 측구 다시) → 보고서(M08 txt · json).
/// 기록 = 도면 NOD «DH_GRADING/UDITCH»(다시 열면 그대로 · 고침). 셈 = Core <see cref="UDitch"/>.</para></summary>
public sealed class UDitchCommand
{
    /// <summary>도면마다(세션) — 고른 측구 · 불러온 CSV · 창 자리.</summary>
    sealed class Memo
    {
        public int Sel;
        public List<UDitch.IdfRow> Csv = new();
        public string CsvSource = "", CsvFile = "";
    }
    static readonly Dictionary<string, Memo> Memos = new(System.StringComparer.OrdinalIgnoreCase);
    static double? WinLeft, WinTop;
    static readonly CultureInfo Ic = CultureInfo.InvariantCulture;

    sealed class Ctx
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
            c.Rec.Site = UDitchStore.SiteOf(c.Db, GradingSettings.ExportEpsg, c.Rec.Ditches);
            int fd = 1;
            try { fd = System.Convert.ToInt16(AcadApp.GetSystemVariable("FILEDIA")); } catch { }
            if (!Memos.TryGetValue(doc.Name ?? "", out var memo)) Memos[doc.Name ?? ""] = memo = new Memo();
            if (lineMode || SelfCheckLog.Headless || fd == 0) RunCmdLine(c);
            else RunPopup(c, memo);
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

    // ══════════════════════════════ 창 ══════════════════════════════

    static void RunPopup(Ctx c, Memo memo)
    {
        string note = "", error = "";
        for (int guard = 0; guard < 200; guard++)
        {
            var st = c.Rec.Site;
            var v = new UDitchDialog.View
            {
                Rec = c.Rec, S = c.S, Sel = System.Math.Min(memo.Sel, c.Rec.Ditches.Count - 1), Csv = memo.Csv, CsvSource = memo.CsvSource, CsvFile = memo.CsvFile,
                Note = note, Error = error,
                SiteText = (double.IsFinite(st.X) ? string.Format(Ic, "X {0:0.000} · Y {1:0.000}", st.X, st.Y) : "대표점 없음") + $" · EPSG {(st.Epsg > 0 ? st.Epsg.ToString(Ic) : "?")}"
                           + (double.IsFinite(st.Lat) ? string.Format(Ic, " · {0:0.00000}°N {1:0.00000}°E", st.Lat, st.Lon) : ""),
                SiteWarn = SiteWarn(c),
                Foot = string.Format(Ic, "설정 {0} · n {1} · 허용 수심 {2:0}% · 최소 경사 {3:0.###} % · 유속 {4:0.0#}~{5:0.0#} m/s", Path.GetFileName(c.SetFrom), c.S.ManningN, c.S.DepthRatio * 100,
                                     c.S.MinSlopePct, c.S.MinVelocity, c.S.MaxVelocity) + (c.SetWhy.Length > 0 ? " · ⚠" + c.SetWhy : ""),
            };
            if (v.Sel < 0 && c.Rec.Ditches.Count > 0) v.Sel = 0;
            var dlg = new UDitchDialog(v);
            if (WinLeft is double wl && WinTop is double wt) { dlg.WindowStartupLocation = System.Windows.WindowStartupLocation.Manual; dlg.Left = wl; dlg.Top = wt; }
            AcadApp.ShowModalWindow(dlg);
            try { WinLeft = dlg.Left; WinTop = dlg.Top; } catch { }
            memo.Sel = dlg.Sel; memo.Csv = v.Csv; memo.CsvSource = v.CsvSource; memo.CsvFile = v.CsvFile;
            if (dlg.Dirty) SaveRec(c);
            note = ""; error = "";
            if (dlg.DialogResult != true || dlg.Action == UDitchDialog.Act.None) { c.Ed.WriteMessage("\n[U형 측구] 닫음"); return; }
            switch (dlg.Action)
            {
                case UDitchDialog.Act.Pick:
                    if (PickEntity(c, keywords: false, out var pid, out _) && AddDitch(c, pid, out int pi, out string pmsg)) { memo.Sel = pi; note = pmsg; }
                    continue;
                case UDitchDialog.Act.Draw:
                    if (DrawLine(c, out var did) && AddDitch(c, did, out int di, out string dmsg)) { memo.Sel = di; note = dmsg; }
                    continue;
                case UDitchDialog.Act.Delete:
                    if (memo.Sel >= 0 && memo.Sel < c.Rec.Ditches.Count)
                    {
                        string id = c.Rec.Ditches[memo.Sel].Id;
                        int n;
                        using (var tr = c.Db.TransactionManager.StartTransaction())
                        {
                            n = UDitchStore.Erase(c.Db, tr, id);
                            c.Rec.Ditches.RemoveAt(memo.Sel);
                            UDitchStore.Save(c.Db, tr, c.Rec);
                            tr.Commit();
                        }
                        note = $"{id} 지움(모델 {n}개)";
                        memo.Sel = System.Math.Min(memo.Sel, c.Rec.Ditches.Count - 1);
                    }
                    continue;
                case UDitchDialog.Act.Build:
                    if (Build(c, out string bmsg)) note = bmsg; else error = bmsg;
                    continue;
            }
        }
    }

    static string SiteWarn(Ctx c)
    {
        var w = new List<string>();
        if (!double.IsFinite(c.Rec.Site.Lat)) w.Add("경위도 변환 못 함 — 좌표계 확인([기타 설정])");
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
        if (!UDitchStore.ReadLine(tr, id, c.S.ArcStep, out var pts, out bool z3, out string why)) { tr.Commit(); msg = why; c.Ed.WriteMessage("\n[U형 측구] ⚠" + why); return false; }
        string h = id.Handle.ToString();
        int k = c.Rec.Ditches.FindIndex(d => d.LineHandle == h);
        var d = k >= 0 ? c.Rec.Ditches[k] : new UDitch.Ditch { Id = UDitch.NextId(c.Rec.Ditches), LineHandle = h, Size = c.S.Sizes[0].Copy(), C = c.S.DefaultC };
        UDitchStore.FillGround(c.Db, tr, d, pts, z3);
        UDitch.ApplyAuto(d, c.S);
        if (k < 0) { c.Rec.Ditches.Add(d); k = c.Rec.Ditches.Count - 1; }
        if (c.Rec.Site.Source.Contains("없음")) c.Rec.Site = UDitchStore.SiteOf(c.Db, GradingSettings.ExportEpsg, c.Rec.Ditches);
        UDitchStore.Save(c.Db, tr, c.Rec);
        tr.Commit();
        index = k;
        var q = UDitch.Qty(d);
        msg = string.Format(Ic, "{0} — 길이 {1:0.00} m · 지표면 {2}{3}", d.Id, q.Length, d.Surface, why.Length > 0 ? " · ⚠" + why : "");
        c.Log.AppendLine("  " + msg);
        return true;
    }

    // ══════════════════════════════ 모델 · 보고서 ══════════════════════════════

    static bool Build(Ctx c, out string msg)
    {
        c.Log.AppendLine("  모델 생성:");
        UDitchStore.BuildStats st;
        try { st = UDitchStore.Build(c.Db, c.Rec, c.S, c.Log); }
        catch (System.Exception ex) { msg = "모델을 못 지음 — " + ex.Message; Done(c, "⚠" + msg + "\n" + ex); return false; }
        string rep = UDitchStore.WriteReport(c.Db, c.Doc.Name ?? "", c.Rec, c.S, out string rwhy);
        int shortN = c.Rec.Ditches.Count(d => UDitch.Check(d, c.Rec.Rain, c.S).Verdict == UDitch.Verdict.Short);
        msg = string.Format(Ic, "모델 {0}개(솔리드 {1}{2}) · 길이 {3:0.00} m · 콘크리트 {4:0.00} ㎥", c.Rec.Ditches.Count - st.Skipped, st.Solids, st.Meshes > 0 ? $" · 메시 {st.Meshes}" : "", st.Length, st.BodyVol + st.FootVol)
              + (!c.Rec.Rain.Has ? " · 강우자료 없음" : shortN > 0 ? $" · ⚠수리 부족 {shortN}" : "")
              + (st.Skipped > 0 ? $" · ⚠못 지음 {st.Skipped}({st.FirstFail})" : st.FirstFail.Length > 0 ? " · ⚠" + st.FirstFail : "")
              + (rep.Length > 0 ? " · 보고서 " + Path.GetFileName(rep) : " · ⚠" + rwhy);
        c.Log.AppendLine($"  → {msg} · 지운 옛 모델 {st.Erased}" + (rep.Length > 0 ? " · " + rep : ""));
        Done(c, msg);
        return st.Skipped < c.Rec.Ditches.Count;
    }
}
