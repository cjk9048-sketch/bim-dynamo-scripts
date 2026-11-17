using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using DH.Grading.Core;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;
using CivilDb = Autodesk.Civil.DatabaseServices;

namespace DH.Grading.Civil.Commands;

/// <summary>★★[JACK 1117 «종단 직접 그리기» · 인터뷰] 진입도로 계획 종단을 종단도 위에 처음부터 새로 찍는다.
/// <para>시점 = 지금 종단 첫 PVI(찍기 허용 <see cref="PviPickTol"/> 안 → 딱 붙임 · 아니면 거절) → 점 클릭 또는 [경사입력](경사 %,길이 m) → 종점 = 지금 종단 끝 PVI를 찍어야 끝(Enter로는 안 끝남) · Esc = 그대로.</para>
/// <para>종단도가 여러 장이면 장 끝 측점(경계)을 찍고(측점은 경계에 붙임 · 높이는 찍은 자리) 다음 장에서 같은 점을 다시 찍어야 이어 감.</para>
/// <para>끝나면 같은 계획 종단 객체의 가운데 PVI를 바꾸고(처음 · 끝 PVI 그대로) «PVI 간격»마다 같은 직선 위 PVI를 더한 뒤 [측점 넣기] Enter와 같은 다시 만들기 — 한 명령 = U 한 번.
/// 종단곡선은 안 넣음(<see cref="PviAddAt"/> · [종단 자동으로 다시]와 같음).</para></summary>
public partial class RoadCommand
{
    /// <summary>종단뷰 한 장 — 측점 · 표고 ↔ 도면 XY(직선 비례 · Civil이 잰 두 점으로).</summary>
    sealed class DrawSheet
    {
        internal double S0, S1, Z0, Z1, Xa, Ya, Kx, Ky;
        internal Extents3d Ext;
        internal Point3d XY(double s, double z) => new(Xa + (s - S0) * Kx, Ya + (z - Z0) * Ky, 0);
        internal (double S, double Z) SZ(Point3d p) => (S0 + (p.X - Xa) / Kx, Z0 + (p.Y - Ya) / Ky);
        internal bool Holds(Point3d p) { var (s, z) = SZ(p); return s >= S0 - 1e-6 && s <= S1 + 1e-6 && z >= Z0 - 1e-6 && z <= Z1 + 1e-6; }
    }

    /// <summary>이 도로 종단뷰를 측점 차례로(같은 시작 측점의 겹친 뷰는 앞의 것만).</summary>
    static List<DrawSheet> DrawSheets(Transaction tr, ObjectId aid)
    {
        var al = (CivilDb.Alignment)tr.GetObject(aid, OpenMode.ForRead);
        var o = new List<DrawSheet>();
        foreach (ObjectId vid in al.GetProfileViewIds())
        {
            try
            {
                if (vid.IsErased || tr.GetObject(vid, OpenMode.ForRead) is not CivilDb.ProfileView pv) continue;
                double s0 = pv.StationStart, s1 = pv.StationEnd, z0 = pv.ElevationMin, z1 = pv.ElevationMax, xa = 0, ya = 0, xb = 0, yb = 0;
                if (!(s1 - s0 > 1e-6) || !(z1 - z0 > 1e-6)) continue;
                if (!pv.FindXYAtStationAndElevation(s0, z0, ref xa, ref ya) || !pv.FindXYAtStationAndElevation(s1, z1, ref xb, ref yb)) continue;
                if (Math.Abs(xb - xa) < 1e-9 || Math.Abs(yb - ya) < 1e-9) continue;
                o.Add(new DrawSheet { S0 = s0, S1 = s1, Z0 = z0, Z1 = z1, Xa = xa, Ya = ya, Kx = (xb - xa) / (s1 - s0), Ky = (yb - ya) / (z1 - z0), Ext = pv.GeometricExtents });
            }
            catch { }
        }
        o.Sort((a, b) => a.S0.CompareTo(b.S0));
        for (int i = o.Count - 1; i >= 1; i--) if (o[i].S0 - o[i - 1].S0 < 1.0) o.RemoveAt(i);
        return o;
    }

    enum DrawKind { Start, Mid, SheetEnd, SheetStart, End }

    [CommandMethod("DHROADPVIDRAW", CommandFlags.Modal | CommandFlags.Redraw)]
    public void PviDraw()
    {
        const string tag = "[종단 직접 그리기]";
        var doc = AcadApp.DocumentManager.MdiActiveDocument; var ed = doc.Editor; var db = doc.Database;
        var s = Need(ed); if (s == null) return;
        try
        {
            List<DrawSheet> sh;
            double a, za, b, zb;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                sh = DrawSheets(tr, s.AlignId);
                var prof = (CivilDb.Profile)tr.GetObject(s.PlanId, OpenMode.ForRead);
                int n0 = prof.PVIs.Count;
                if (n0 < 2) { ed.WriteMessage($"\n{tag} 계획 종단 비어 있음 → [종단 자동으로 다시]"); return; }
                a = prof.PVIs[0].RawStation; za = prof.PVIs[0].Elevation; b = prof.PVIs[n0 - 1].RawStation; zb = prof.PVIs[n0 - 1].Elevation;
                tr.Commit();
            }
            if (sh.Count == 0) { ed.WriteMessage($"\n{tag} 종단도 없음 → [종단도 생성]부터"); return; }
            // 장 끝 측점 = 다음 장 시작 + 들임(SplitPlan이 다음 장 시작을 경계보다 1 cm 당김) · 마지막 장은 없음
            var bound = new double[sh.Count];
            for (int k = 0; k < sh.Count; k++) bound[k] = k + 1 < sh.Count ? Math.Round(sh[k + 1].S0 + SheetCommand.SheetLeadM, 3) : double.PositiveInfinity;
            int SheetOf(double st) { for (int k = 0; k < sh.Count; k++) if (st >= sh[k].S0 - 1e-6 && st <= Math.Min(sh[k].S1, bound[k]) + 1e-6) return k; return -1; }
            int ka = SheetOf(a), kb = SheetOf(b);
            if (ka < 0 || kb < 0 || kb < ka) { ed.WriteMessage($"\n{tag} 종단도가 시점 · 종점을 안 덮음 → [종단도 생성] 다시"); return; }
            var dsn = s.Fresh();
            double maxG = dsn.MaxGrade, minLen = dsn.MinPviLen;

            var pts = new List<(double S, double Z, int Sheet, DrawKind Kind)>();
            List<(Point3d P, bool End)> marks = new();
            try { using var trm = db.TransactionManager.StartTransaction(); marks = PviMarks(trm, s); trm.Commit(); } catch { }
            var planId = s.PlanId;
            ZoomTo(ed, sh[ka].Ext);
            Hilite(db, planId, true);
            int shown = ka;
            void Show(int k) { if (k != shown && k >= 0 && k < sh.Count) { ZoomTo(ed, sh[k].Ext); shown = k; } }
            bool done = false;
            string live = "";
            try
            {
                while (true)
                {
                    // 지금 할 일 — 시점 · 다음 장 시점 · 다음 점
                    var last = pts.Count > 0 ? pts[^1] : default;
                    bool needStart = pts.Count == 0, needSheet = !needStart && last.Kind == DrawKind.SheetEnd;
                    int k = needStart ? ka : needSheet ? last.Sheet + 1 : last.Sheet;
                    var v = sh[k];
                    string label = needStart ? "시점 클릭" : needSheet ? "다음 장 시점 클릭" : "점 클릭 · 경사 입력";
                    var ppo = new PromptPointOptions(needStart ? $"\n{tag} 시점(첫 꺾인 점) 클릭"
                        : needSheet ? $"\n{tag} 다음 장({k + 1}/{sh.Count}) 시점 = 앞 장 끝 점 클릭"
                        : $"\n{tag} 다음 점 클릭 · 종점 클릭 = 끝") { AllowNone = true };
                    if (!needStart && !needSheet) ppo.Keywords.Add("G", "G", "경사입력(G)");
                    if (!needStart) ppo.Keywords.Add("U", "U", "되돌리기(U)");
                    live = label;
                    var pr = CursorLabel.GetPoint(ed, ppo, label,
                        extra: (wd, cur, has) =>
                        {
                            DrawPviMarks(wd, marks, cur, has, false);
                            live = DrawPreview(wd, sh, pts, cur, has && !needStart && !needSheet, k, maxG, label);
                            if (has && (needStart || needSheet))
                            {
                                var t = needStart ? v.XY(a, za) : v.XY(last.S, last.Z);
                                if (new Point2d(t.X, t.Y).GetDistanceTo(new Point2d(cur.X, cur.Y)) <= PviPickTol()) live = needStart ? "시점 → 클릭" : "다음 장 시점 → 클릭";
                            }
                            else if (has && k == kb && new Point2d(v.XY(b, zb).X, v.XY(b, zb).Y).GetDistanceTo(new Point2d(cur.X, cur.Y)) <= PviPickTol()) live = "종점 → 클릭하면 끝";
                        },
                        live: () => live);
                    if (pr.Status == PromptStatus.None) { ed.WriteMessage($"\n{tag} 종점 클릭해야 끝 · Esc = 취소"); continue; }
                    if (pr.Status == PromptStatus.Keyword)
                    {
                        string kw = (pr.StringResult ?? "").Trim().ToUpperInvariant();
                        if (kw == "U")
                        {
                            if (pts.Count > 0) pts.RemoveAt(pts.Count - 1);
                            ed.WriteMessage($"\n{tag} 점 하나 되돌림 · 남은 점 {pts.Count}개");
                            Show(pts.Count == 0 ? ka : pts[^1].Kind == DrawKind.SheetEnd ? pts[^1].Sheet + 1 : pts[^1].Sheet);
                            continue;
                        }
                        if (kw == "G")
                        {
                            var so = new PromptStringOptions($"\n{tag} 경사 %,길이 m (예: -3.5,20): ") { AllowSpaces = true };
                            var sr = ed.GetString(so);
                            if (sr.Status != PromptStatus.OK) continue;
                            var parts = (sr.StringResult ?? "").Split(new[] { ',', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries);
                            var ic = System.Globalization.CultureInfo.InvariantCulture;
                            if (parts.Length != 2 || !double.TryParse(parts[0], System.Globalization.NumberStyles.Float, ic, out double gp) || !double.TryParse(parts[1], System.Globalization.NumberStyles.Float, ic, out double ln)
                                || !double.IsFinite(gp) || !double.IsFinite(ln) || ln <= 0)
                            { ed.WriteMessage($"\n{tag} ⚠경사 %,길이 m 두 수(길이 > 0) → 예: -3.5,20"); continue; }
                            // ★[검토 MED] 클릭과 같은 최소 간격 — 너무 짧은 길이는 끝에서 PVI 넣기가 거절돼 그린 것을 통째로 버렸다
                            if (ln < PviGap) { ed.WriteMessage($"\n{tag} ⚠길이 {ln:0.##} m — {PviGap:0.#} m 이상"); continue; }
                            double st = Math.Round(last.S + ln, 2), z = Math.Round(last.Z + gp / 100 * (Math.Round(last.S + ln, 2) - last.S), 3);
                            if (k == kb && st >= b - PviGap) { ed.WriteMessage($"\n{tag} ⚠종점({b:F2} m)까지 닿음 → 종점은 클릭"); continue; }
                            if (k != kb && st >= bound[k] - PviGap)
                            {
                                st = bound[k]; z = last.Z + gp / 100 * (st - last.S);
                                if (st <= last.S + PviGap) { ed.WriteMessage($"\n{tag} ⚠장 끝과 너무 가까움 → 되돌리기(U)"); continue; }
                                pts.Add((st, z, k, DrawKind.SheetEnd));
                                ed.WriteMessage($"\n{tag} 장 끝에서 멈춤 · 측점 {st:F2} m · 표고 {z:F3} m · 경사 {gp:0.##} %{Over(gp / 100, maxG)} → 다음 장 시점 클릭");
                                Show(k + 1);
                                continue;
                            }
                            pts.Add((st, z, k, DrawKind.Mid));
                            ed.WriteMessage($"\n{tag} 점 {pts.Count} · 측점 {st:F2} m · 표고 {z:F3} m · 경사 {gp:0.##} %{Over(gp / 100, maxG)}");
                        }
                        continue;
                    }
                    if (pr.Status != PromptStatus.OK) { ed.WriteMessage($"\n{tag} 취소 — 종단 그대로"); return; }
                    var p = pr.Value.TransformBy(ed.CurrentUserCoordinateSystem);
                    double tol = PviPickTol();
                    static double D(Point3d u, Point3d w) => new Point2d(u.X, u.Y).GetDistanceTo(new Point2d(w.X, w.Y));
                    if (needStart)
                    {
                        if (D(p, v.XY(a, za)) > tol) { ed.WriteMessage($"\n{tag} ⚠시점 아님 → 첫 꺾인 점(측점 {a:F2} m) 바로 위 클릭"); continue; }
                        pts.Add((a, za, k, DrawKind.Start));
                        ed.WriteMessage($"\n{tag} 시점 · 측점 {a:F2} m · 표고 {za:F3} m → 다음 점");
                        continue;
                    }
                    if (needSheet)
                    {
                        // ★[검토 MED] 다음 장 표고 범위가 달라 앞 장 끝 표고가 화면 밖일 수 있음 — 측점(가로 자리)만 맞으면 받고 표고는 앞 장 끝 그대로(이음 점 하나)
                        var pj = v.XY(last.S, last.Z);
                        bool inRange = v.Holds(pj);
                        if (inRange ? D(p, pj) > tol : Math.Abs(p.X - pj.X) > tol) { ed.WriteMessage($"\n{tag} ⚠다음 장 시점 아님 → 앞 장 끝 점(측점 {last.S:F2} m · 표고 {last.Z:F3} m) 클릭"); continue; }
                        pts.Add((last.S, last.Z, k, DrawKind.SheetStart));
                        ed.WriteMessage($"\n{tag} 다음 장({k + 1}/{sh.Count}) 시점 → 다음 점");
                        continue;
                    }
                    if (!v.Holds(p))
                    {
                        bool other = sh.Any(q => q != v && q.Holds(p));
                        ed.WriteMessage(other ? $"\n{tag} ⚠다른 장 → 지금 장({k + 1}/{sh.Count}) 안 클릭" : $"\n{tag} ⚠종단도 밖 → 종단도 안 클릭");
                        continue;
                    }
                    if (k == kb && D(p, v.XY(b, zb)) <= tol)
                    {
                        pts.Add((b, zb, k, DrawKind.End));
                        double gE = (zb - last.Z) / Math.Max(1e-9, b - last.S);
                        if (b <= last.S + PviGap) { pts.RemoveAt(pts.Count - 1); ed.WriteMessage($"\n{tag} ⚠앞 점이 종점과 너무 가까움 → 되돌리기(U)"); continue; }
                        ed.WriteMessage($"\n{tag} 종점 · 경사 {gE * 100:0.##} %{Over(gE, maxG)}");
                        done = true;
                        break;
                    }
                    var (cs, cz) = v.SZ(p);
                    cs = Math.Round(cs, 2); cz = Math.Round(cz, 3);
                    var kind = DrawKind.Mid;
                    if (k == kb)
                    {
                        if (cs >= b - PviGap) { ed.WriteMessage($"\n{tag} ⚠종점 근처 → 종점(끝 꺾인 점) 바로 위 클릭"); continue; }
                    }
                    else if (cs >= bound[k] - PviGap || Math.Abs(p.X - v.XY(bound[k], cz).X) <= tol)
                    {
                        if (Math.Abs(p.X - v.XY(bound[k], cz).X) > tol && cs > bound[k]) { ed.WriteMessage($"\n{tag} ⚠장 끝(측점 {bound[k]:F2} m) 넘음 → 장 끝 클릭"); continue; }
                        cs = bound[k]; kind = DrawKind.SheetEnd;
                    }
                    if (cs <= last.S + PviGap) { ed.WriteMessage($"\n{tag} ⚠측점 {cs:F2} m — 앞 점({last.S:F2} m) 뒤만"); continue; }
                    double g = (cz - last.Z) / (cs - last.S);
                    pts.Add((cs, cz, k, kind));
                    if (kind == DrawKind.SheetEnd)
                    {
                        ed.WriteMessage($"\n{tag} 장 끝 · 측점 {cs:F2} m · 표고 {cz:F3} m · 경사 {g * 100:0.##} %{Over(g, maxG)} → 다음 장 시점 클릭");
                        Show(k + 1);
                    }
                    else ed.WriteMessage($"\n{tag} 점 {pts.Count} · 측점 {cs:F2} m · 표고 {cz:F3} m · 경사 {g * 100:0.##} %{Over(g, maxG)}");
                }
            }
            finally { Hilite(db, planId, false); }
            if (!done) return;

            // 그린 꺾인 점(다음 장 시점은 앞 장 끝과 같은 점이라 뺌) → PVI 간격마다 같은 직선 위 PVI
            var drawn = pts.Where(q => q.Kind != DrawKind.SheetStart).Select(q => (q.S, q.Z)).ToList();
            var ranges = s.Ranges;
            var (dense, added) = RoadProfileDraft.Densify(drawn, minLen, double.NaN, ranges.Count > 0 ? (Func<double, bool>)(st => RoadRanges.Inside(ranges, st)) : null);
            int over = 0;
            for (int i = 1; i < drawn.Count; i++) if (Math.Abs(drawn[i].Z - drawn[i - 1].Z) / Math.Max(1e-9, drawn[i].S - drawn[i - 1].S) > maxG + 1e-9) over++;
            bool was = s.Suppress; s.Suppress = true;   // 우리가 쓰는 동안 바뀜 표시 막음 — 끝에서 한 번 다시 계산
            try
            {
                using var tr = db.TransactionManager.StartTransaction();
                var prof = (CivilDb.Profile)tr.GetObject(planId, OpenMode.ForWrite);
                // 처음 · 끝 PVI는 Civil이 못 지움 → 그대로 두고(시점 · 종점 = 그 점) 가운데만 바꿈 — 같은 종단 객체(도로 연결 그대로) · 수가 안 맞으면 커밋 안 함
                var f0 = prof.PVIs[0]; var f1 = prof.PVIs[prof.PVIs.Count - 1];
                if (Math.Abs(f0.RawStation - a) > 1e-6 || Math.Abs(f0.Elevation - za) > 1e-6 || Math.Abs(f1.RawStation - b) > 1e-6 || Math.Abs(f1.Elevation - zb) > 1e-6)
                    throw new InvalidOperationException("그리는 동안 시점 · 종점이 바뀜 → 다시");
                for (int i = prof.PVIs.Count - 2; i >= 1; i--) prof.PVIs.RemoveAt(i);
                for (int i = 1; i + 1 < dense.Count; i++) prof.PVIs.AddPVI(dense[i].S, dense[i].Z);
                if (prof.PVIs.Count != dense.Count) throw new InvalidOperationException($"PVI 수가 다름({prof.PVIs.Count}/{dense.Count}) → 안 바꿈");
                tr.Commit();
            }
            catch (System.Exception exW)
            {
                s.Suppress = was;
                ed.WriteMessage($"\n{tag} 못 함 — {exW.Message} · 종단 그대로");
                try { DiagLog.Append($"\n{tag} 쓰기 실패 — " + exW); } catch { }
                return;
            }
            s.Suppress = was;
            try { s.Recompute(); } catch { }
            ed.WriteMessage($"\n{tag} 꺾인 점 {drawn.Count}개 + PVI 간격 {minLen:0.##} m마다 {added}개 = PVI {dense.Count}개"
                + (over > 0 ? $" · 최대 경사 넘은 구간 {over}개" : "") + " → 지표면 · 종단도 · 횡단도 다시 만듦 (U = 한꺼번에 되돌림)");
            RedrawStale(doc, s, ask: true);
            try { if (!planId.IsNull && !planId.IsErased) ed.SetImpliedSelection(new[] { planId }); } catch { }
        }
        catch (System.Exception ex)
        {
            ed.WriteMessage($"\n{tag} 실패 — " + ex.Message);
            try { DiagLog.Append($"\n{tag} 실패 — " + ex); } catch { }
        }
    }

    // 한글 글꼴(커서 이름표와 같은 맑은 고딕) — 한 번 만들어 계속 씀(CursorLabel.SharedStyle과 같은 까닭으로 안 지움)
    static Autodesk.AutoCAD.GraphicsInterface.TextStyle? _overStyle;
    static Autodesk.AutoCAD.GraphicsInterface.TextStyle OverStyle(double h)
    {
        _overStyle ??= new Autodesk.AutoCAD.GraphicsInterface.TextStyle { Font = new Autodesk.AutoCAD.GraphicsInterface.FontDescriptor("Malgun Gothic", true, false, 129, 34) };
        _overStyle.TextSize = h;
        return _overStyle;
    }

    static string Over(double g, double maxG) => Math.Abs(g) > maxG + 1e-9 ? $" · 최대 경사 넘음({maxG * 100:0.##} %)" : "";

    /// <summary>그린 직선(장마다 제 종단뷰 위) + 마지막 점 → 커서 고무줄 · 최대 경사 넘은 직선은 빨강 + «최대 경사 넘음». 돌려줌 = 커서 이름표 글(경사).</summary>
    static string DrawPreview(Autodesk.AutoCAD.GraphicsInterface.WorldDraw wd, List<DrawSheet> sh, List<(double S, double Z, int Sheet, DrawKind Kind)> pts,
                              Point3d cur, bool rubber, int k, double maxG, string label)
    {
        var t = wd.SubEntityTraits;
        var ok = new Autodesk.AutoCAD.Colors.EntityColor(255, 170, 0);
        var red = new Autodesk.AutoCAD.Colors.EntityColor(235, 60, 50);
        double u = CursorLabel.PixelUnit();
        void Seg(DrawSheet v, double s0, double z0, double s1, double z1)
        {
            double g = (z1 - z0) / Math.Max(1e-9, s1 - s0);
            bool bad = Math.Abs(g) > maxG + 1e-9;
            t.TrueColor = bad ? red : ok;
            Point3d p0 = v.XY(s0, z0), p1 = v.XY(s1, z1);
            var off = new Vector3d(0, u, 0);
            wd.Geometry.WorldLine(p0, p1);
            wd.Geometry.WorldLine(p0 + off, p1 + off);   // 두 겹 — 계획선 위에서도 보이게
            if (bad)
            {
                var m = new Point3d((p0.X + p1.X) / 2, (p0.Y + p1.Y) / 2 + 6 * u, 0);
                wd.Geometry.Text(m, Vector3d.ZAxis, Vector3d.XAxis, "최대 경사 넘음", true, OverStyle(12 * u));
            }
        }
        for (int i = 1; i < pts.Count; i++)
        {
            if (pts[i].Kind == DrawKind.SheetStart) continue;
            Seg(sh[pts[i].Sheet], pts[i - 1].S, pts[i - 1].Z, pts[i].S, pts[i].Z);
        }
        if (!rubber || pts.Count == 0) return label;
        var last = pts[^1]; var v0 = sh[k];
        var (cs, cz) = v0.SZ(cur);
        if (!(cs > last.S + 1e-6)) return "앞 점 뒤 클릭";
        Seg(v0, last.S, last.Z, cs, cz);
        double gC = (cz - last.Z) / (cs - last.S);
        return $"경사 {gC * 100:0.00} %" + (Math.Abs(gC) > maxG + 1e-9 ? " · 최대 경사 넘음" : "");
    }
}
