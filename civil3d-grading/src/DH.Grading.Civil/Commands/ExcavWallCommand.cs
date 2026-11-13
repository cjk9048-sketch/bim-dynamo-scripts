using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using DH.Grading.Core;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;
using AcDoc = Autodesk.AutoCAD.ApplicationServices.Document;

namespace DH.Grading.Civil.Commands;

/// <summary>★★★[계획 7단계 · JACK 0909 확정 <i>"(가) 제대로 한다"</i>]
/// <b>터파기 둘레의 한 구간만 가시설(수직)로 — 그리고 되돌리기.</b>
///
/// <para><b>왜 정지의 것을 그대로 못 쓰나.</b> <see cref="ZoneEditCommon"/>은 671줄이고
/// 정지 번들·단높이·소단폭·옹벽형태에 깊이 묶여 있다. 터파기는 <b>제원이 구배 하나뿐</b>이라
/// (JACK 0824: <i>"단높이 설정은 필요 없어, 어차피 구배로만 치는 거야"</i>)
/// 그 기계를 끌고 오면 <b>쓰지 않는 물음이 대부분</b>이 된다.
/// 대신 <b>사용자가 겪는 흐름</b>은 똑같이 맞춘다 — ★[v117.16 · JACK 1112] 시작점 → 둘레 따라 끝점 → 날개(옛 구간지정과 같은 그리기) → 다시 만든다.</para>
///
/// <para>★<b>옹벽이 아니라 가시설이다.</b> 이 저장소는 둘을 이미 갈라 놓았다 —
/// 종단 막대가 옹벽은 <b>계획면↔원지반</b>, 가시설은 <b>계획면↔터파기 바닥</b>이고
/// 밴드도 가시설은 건너뛴다(JACK 0827). 그래서 여기서 만든 구간은
/// <b>터파기 기록</b>에만 들어가고 정지 번들에는 닿지 않는다 — 태그가 섞이면 종단이 틀리게 그려진다.</para>
///
/// <para>★<b>옹벽 형태(보강토·역T·앵커판넬)는 여기 없다.</b> 그것들은 블록·기초·3m 앵커를 만드는
/// 물건이라 <b>흙막이 부재가 아니다</b>. 가시설 종류(H파일·시트파일 등)가 필요해지면
/// 그때 <b>별도 목록</b>을 만들어야 한다.</para></summary>
public sealed class ExcavWallCommand
{
    /// <summary>한 번에 지정할 수 있는 구간 길이의 하한(m) — 이보다 짧으면 링을 못 자른다 · 한 바퀴에서 이만큼 모자라도 둘레 전체로 본다.</summary>
    private const double MinRun = 0.5;

    [CommandMethod("DHEXCAVWALL", CommandFlags.Modal)]
    public void ToWall() => PickWall();   // ★[v117.16 · JACK 1112] 시작점 → 둘레 따라 끝점 → 날개(종전: 둘레 근처 클릭 + 길이 입력)

    [CommandMethod("DHEXCAVSLOPE", CommandFlags.Modal)]
    public void ToSlope() => PickSlope();   // ★[v117.16 · JACK 1112] 가시설 구간에 커서 → 빨강 → 클릭

    /// <summary>이 도면의 <b>모든</b> 가시설 구간을 지우고 순수 사면으로 되돌린다.</summary>
    [CommandMethod("DHEXCAVWALLCLEAR", CommandFlags.Modal)]
    public void ClearAll()
    {
        var doc = AcadApp.DocumentManager.MdiActiveDocument;
        if (doc == null) return;
        var ed = doc.Editor;
        GradingSettings.SyncToDocument(doc);
        try
        {
            var recs = Load(doc, out string why);
            if (recs == null) { ed.WriteMessage("\n[가시설] " + why); return; }
            int n = 0;
            foreach (var r in recs) if (r.WallZones != null && r.WallZones.Count > 0) { r.WallZones = null; n++; }
            if (n == 0) { ed.WriteMessage("\n[가시설] 지정된 구간이 없습니다."); return; }
            Save(doc, recs);
            ed.WriteMessage($"\n[가시설] 구조물 {n}개의 구간을 전부 지웠습니다 — 다시 만듭니다.");
            Rebuild(doc, recs, ed);
        }
        catch (System.Exception ex)
        {
            ed.WriteMessage("\n[가시설] 실패 — " + ex.Message);
            try { DiagLog.Append($"\n■ 가시설 전체해제 예외 — {ex.GetType().Name}: {ex.Message}\n"); } catch { }
        }
    }

    // ══ ★[v117.16 · JACK 1112 «가시설 변환도 찍고 숫자로 입력해서 하려니 너무 불편하고 직관적이지가 않아 · 예전에 옹벽 구간 설정했던 것처럼 직접 노선상에서 선택 ·
    //   찍고 노선상에서 마지막 선 찍고 날개 찍었던 그 방식»] 옛 [옹벽 변환] 구간지정(ZoneEditCommon.PickOnRing · WallPolyDraw)과 같은 흐름 ══
    //   ① 시작점 — 커서가 터파기 둘레(바닥)에 붙고 가로지르는 빨간 표식 ② 끝점 — 시작점부터 커서까지 둘레를 따라 굵은 빨간 띠 + 화살촉(커서가 간 쪽으로 · 한 바퀴 돌면 둘레 전체)
    //   ③ 날개 — 끝점 쪽 · 시작점 쪽 차례로 방향 클릭(빨간 선이 터파기 윗선까지 · Esc = 날개 자동). 이미 있는 가시설 구간은 파랗게 보인다.

    /// <summary>터파기 둘레 하나 — 기록 번호 · 바닥 둘레 · 누적 길이.</summary>
    private sealed class Rim
    {
        public int Idx;
        public System.Collections.Generic.List<Point3> Pts = new();
        public double[] Cum = System.Array.Empty<double>();
        public double Total;
    }

    private static System.Collections.Generic.List<Rim> RimsOf(System.Collections.Generic.List<ExcavBundle> recs)
    {
        var l = new System.Collections.Generic.List<Rim>();
        for (int i = 0; i < recs.Count; i++)
        {
            var b = recs[i].Bottom;
            if (b == null || b.Count < 3) continue;
            var cum = GradingGeometry.CumLen2D(b);
            if (!(cum[^1] > 1e-6)) continue;
            l.Add(new Rim { Idx = i, Pts = b, Cum = cum, Total = cum[^1] });
        }
        return l;
    }

    /// <summary>(x,y)에 가장 가까운 둘레 · 그 위 자리(호길이).</summary>
    private static (Rim R, double T)? NearestRim(System.Collections.Generic.List<Rim> rims, double x, double y)
    {
        Rim? best = null; double bd = double.MaxValue, bt = 0;
        foreach (var r in rims)
        {
            double t = GradingGeometry.ParamAt(r.Pts, r.Cum, x, y);
            var q = GradingGeometry.PointAtParam(r.Pts, r.Cum, t);
            double d = (q.X - x) * (q.X - x) + (q.Y - y) * (q.Y - y);
            if (d < bd) { bd = d; best = r; bt = t; }
        }
        return best == null ? null : (best, bt);
    }

    private static double W(double v, double tot) => tot <= 0 ? 0 : ((v % tot) + tot) % tot;

    /// <summary>시작점(<paramref name="ta"/>)에서 커서(<paramref name="tt"/>)까지 — 커서가 지나온 쪽(<paramref name="acc"/> 부호)으로.
    /// 한 바퀴 가까이 돌았으면 둘레 전체. 돌려주는 (S0, S1)은 T가 느는 차례.</summary>
    private static (double S0, double S1, double Span, bool Fwd, bool Whole) Stretch(double ta, double tt, double acc, double tot)
    {
        if (System.Math.Abs(acc) >= tot - MinRun) return (0, tot, tot, acc >= 0, true);
        double fwd = W(tt - ta, tot);
        bool goFwd = acc >= 0;
        return goFwd ? (ta, tt, fwd, true, false) : (tt, ta, W(ta - tt, tot), false, false);
    }

    /// <summary>이미 있는 가시설 구간 — 파랗게(지금 찍는 빨강과 갈리게).</summary>
    private static void DrawZones(Autodesk.AutoCAD.GraphicsInterface.ViewportDraw dc,
                                  System.Collections.Generic.List<ExcavBundle> recs, double half, SlopeZone? skip = null)
    {
        if (dc == null) return;
        try { dc.SubEntityTraits.Color = ZoneAci; } catch { }
        foreach (var rec in recs)
        {
            if (rec.WallZones == null) continue;
            foreach (var z in rec.WallZones)
            {
                if (z == null || ReferenceEquals(z, skip)) continue;
                var ruler = z.Ref ?? rec.Bottom;
                if (ruler == null || ruler.Count < 3) continue;
                var cum = GradingGeometry.CumLen2D(ruler);
                ZoneEditCommon.DrawBand(dc, GradingGeometry.SubPath(ruler, cum, z.T0, z.T1), half * 0.6);
            }
        }
        try { dc.SubEntityTraits.Color = ZoneEditCommon.GlyphAci; } catch { }
    }

    /// <summary>기존 가시설 구간 색 — 파랑.</summary>
    private const short ZoneAci = 5;

    /// <summary>커서 표식이 한 번이라도 자빠졌나 — 로그에 한 번만 남기는 걸쇠(ZoneEditCommon과 같은 까닭).</summary>
    private static bool _glyphLogged;
    private static void GlyphFail(System.Exception ex)
    {
        if (_glyphLogged) return;
        _glyphLogged = true;
        try { DiagLog.Append($"\n   ⚠ 가시설 커서 표식 실패 — {ex.GetType().Name}: {ex.Message}\n"); } catch { }
    }

    private static double ArrowOf(double tot) => System.Math.Max(1.5, System.Math.Min(tot * 0.02, 8.0));

    /// <summary>[가시설 변환] — 시작점 → 둘레 따라 끝점 → 날개 둘 → 그 구간 수직(흙막이).</summary>
    private static void PickWall()
    {
        var doc = AcadApp.DocumentManager.MdiActiveDocument;
        if (doc == null) return;
        var ed = doc.Editor;
        const string label = "가시설 변환";
        GradingSettings.SyncToDocument(doc);
        PickSession.Hook();
        if (!PickSession.Begin(ed, label)) return;
        try
        {
            var recs = Load(doc, out string why);
            if (recs == null) { ed.WriteMessage($"\n[{label}] " + why); return; }
            var rims = RimsOf(recs);
            if (rims.Count == 0) { ed.WriteMessage($"\n[{label}] 터파기 둘레 못 읽음 → [구조물 터파기] 다시"); return; }

            // ── ① 시작점 — 둘레에 붙는 가로 표식 ──
            void OnStart(object? s, PointMonitorEventArgs e)
            {
                try
                {
                    if (!e.Context.PointComputed) return;
                    var c = e.Context.ComputedPoint;
                    var hit = NearestRim(rims, c.X, c.Y);
                    if (hit == null) return;
                    var (r, t) = hit.Value;
                    e.AppendToolTipText($"시작점 · 구조물 {r.Idx + 1} · 둘레 {t:0.00}m");
                    var dc = e.Context.DrawContext;
                    if (dc == null) return;
                    double half = ZoneEditCommon.BandHalf(r.Total), arrow = ArrowOf(r.Total);
                    DrawZones(dc, recs, half);
                    var o = GradingGeometry.OutwardAt(r.Pts, r.Cum, t, arrow * 0.5);
                    var i2 = GradingGeometry.OutwardAt(r.Pts, r.Cum, t, -arrow * 0.5);
                    ZoneEditCommon.DrawBand(dc, new System.Collections.Generic.List<Point3> { i2, o }, half);
                }
                catch (System.Exception gex) { GlyphFail(gex); }
            }
            var p1 = GetPointWatched(ed, "\n[가시설 변환] 시작점 클릭(터파기 둘레): ", OnStart);
            if (p1.Status != PromptStatus.OK) { ed.WriteMessage($"\n[{label}] 취소."); return; }
            var h1 = NearestRim(rims, p1.Value.X, p1.Value.Y);
            if (h1 == null) { ed.WriteMessage($"\n[{label}] 둘레 못 찾음"); return; }
            var rim = h1.Value.R; double ta = h1.Value.T, tot = rim.Total;
            var rec = recs[rim.Idx];

            // ★★[2차 검토 0909 · 보통] 하한이 수직 문턱보다 크면 세워도 형상이 안 바뀐다 — 찍기 전에 막는다(종전과 같은 판정)
            if (rec.MinSlope > GradingSettings.WallGateSlope + 1e-9)
            {
                ed.WriteMessage($"\n[{label}] 최소구배 1:{rec.MinSlope:0.###} > 수직 문턱 1:{GradingSettings.WallGateSlope:0.###} → 수직 불가"
                              + "\n  → [구조물 터파기] 굴착 구배 0(수직)으로 다시 생성 뒤 지정");
                return;
            }

            // ── ② 끝점 — 시작점부터 커서까지 둘레 따라 굵은 빨간 띠 + 화살촉(커서가 지나온 쪽) ──
            double acc = 0, lastT = ta; bool moved = false;
            void OnEnd(object? s, PointMonitorEventArgs e)
            {
                try
                {
                    if (!e.Context.PointComputed) return;
                    var c = e.Context.ComputedPoint;
                    double tt = GradingGeometry.ParamAt(rim.Pts, rim.Cum, c.X, c.Y);
                    double d = W(tt - lastT, tot); if (d > tot * 0.5) d -= tot;
                    acc = System.Math.Max(-tot, System.Math.Min(tot, acc + d)); lastT = tt; moved = true;
                    var st = Stretch(ta, tt, acc, tot);
                    var at = GradingGeometry.PointAtParam(rim.Pts, rim.Cum, tt);
                    e.AppendToolTipText(st.Whole ? $"둘레 전체 {tot:0.00}m" : $"구간 {st.Span:0.00}m");
                    var dc = e.Context.DrawContext;
                    if (dc == null) return;
                    double half = ZoneEditCommon.BandHalf(tot), arrow = ArrowOf(tot);
                    DrawZones(dc, recs, half);
                    if (st.Span < 1e-6) return;
                    var path = GradingGeometry.SubPath(rim.Pts, rim.Cum, st.S0, st.S1);
                    if (path.Count < 2) return;
                    ZoneEditCommon.DrawBand(dc, path, half);
                    if (st.Whole) return;
                    var near = GradingGeometry.PointAtParam(rim.Pts, rim.Cum, tt + (st.Fwd ? -1.0 : 1.0));
                    ZoneEditCommon.DrawArrow(dc, at, at.X - near.X, at.Y - near.Y, arrow);
                }
                catch (System.Exception gex) { GlyphFail(gex); }
            }
            var p2 = GetPointWatched(ed, "\n[가시설 변환] 끝점 클릭(둘레 따라 · 한 바퀴 = 전체): ", OnEnd);
            if (p2.Status != PromptStatus.OK) { ed.WriteMessage($"\n[{label}] 취소."); return; }
            double tb = GradingGeometry.ParamAt(rim.Pts, rim.Cum, p2.Value.X, p2.Value.Y);
            // ★찍힌 그 점으로 마지막 걸음을 더한다(스냅 · 좌표 입력이면 커서 자리와 다르다) · 커서가 한 번도 안 움직였으면(명령줄 입력) 짧은 쪽
            if (moved) { double d = W(tb - lastT, tot); if (d > tot * 0.5) d -= tot; acc = System.Math.Max(-tot, System.Math.Min(tot, acc + d)); }
            else { double f = W(tb - ta, tot); acc = f <= tot - f ? f : -(tot - f); }
            var pick = Stretch(ta, tb, acc, tot);
            if (pick.Span < MinRun) { ed.WriteMessage($"\n[{label}] 구간 {pick.Span:0.##}m — 짧음({MinRun:0.#}m 이상)"); return; }
            double t0 = pick.S0, t1 = pick.S1;

            // ── ③ 날개 — 끝점 쪽 → 시작점 쪽(옛 손 폴리곤과 같은 그리기 · 빨간 선은 터파기 윗선까지) ──
            (double X, double Y)? wT0 = null, wT1 = null;
            if (pick.Whole) ed.WriteMessage($"\n[{label}] 둘레 전체 — 날개 없음");
            else if (rec.FinalRing == null || rec.FinalRing.Count < 3)
                ed.WriteMessage($"\n[{label}] 터파기 윗선 없음 → 날개 자동");
            else
            {
                var seg = WallInPoly.SegmentPoints(rim.Pts, rim.Cum, t0, t1);
                var n0 = WallInPoly.FenceNormal(rim.Pts, rim.Cum, t0);
                var n1 = WallInPoly.FenceNormal(rim.Pts, rim.Cum, t1);
                int ow = GradingGeometry.OutwardSideOfTravel(rim.Pts);
                // WallPolyDraw는 구간 끝(end1)부터 묻는다 — 끝점(마지막에 찍은 자리)이 end1이 되게 · 거꾸로면 구간을 뒤집고 바깥쪽 부호도 뒤집는다
                bool endIsT1 = pick.Fwd;
                var segO = new System.Collections.Generic.List<Point3>(seg);
                if (!endIsT1) segO.Reverse();
                var got = new WallPolyDraw.Picked();
                string[] nm = { "끝점", "시작점" };
                var poly = WallPolyDraw.Run(doc, rec.FinalRing, segO, endIsT1 ? n0 : n1, endIsT1 ? n1 : n0, endIsT1 ? ow : -ow,
                                            out _, out _, out string plog,
                                            ask: step => $"\n[가시설 날개] {nm[step]} 쪽 날개 방향 클릭(Esc = 날개 자동): ",
                                            outDirs: got);
                try { DiagLog.Append($"\n■ 가시설 날개 찍기 — 구조물 {rim.Idx + 1} · [{t0:F2}~{t1:F2}m]\n{plog}"); } catch { }
                if (got.N < 2) ed.WriteMessage($"\n[{label}] 날개 안 찍음 → 날개 자동");
                else if (poly == null) { ed.WriteMessage($"\n[{label}] 날개 폴리곤 꼬임 → 다시(날개 방향 바꿔서)"); return; }
                else
                {
                    var a = endIsT1 ? got.DirA : got.DirB;   // T1 끝
                    var b = endIsT1 ? got.DirB : got.DirA;   // T0 끝
                    var chk = ExcavWing.Region(rim.Pts, t0, t1, b, a, rec.FinalRing, GradingSettings.WallPolygonMargin, out string wwhy);
                    if (chk == null) { ed.WriteMessage($"\n[{label}] 날개 못 씀 — {wwhy} → 다시"); return; }
                    wT0 = b; wT1 = a;
                }
            }

            rec.WallZones ??= new System.Collections.Generic.List<SlopeZone>();
            var z = new SlopeZone { T0 = t0, T1 = t1, WingT0 = wT0, WingT1 = wT1 };
            // ★단높이·소단은 안 쓴다 — 터파기 제원은 구배 하나다(JACK 0824). 소단폭 −1 = "전역값 따름".
            // ★★[검토 0909 · 높음3] 전역이 아니라 <b>그 기록의 하한</b>(막대 두께와 실제 벽면 폭이 같게).
            z.Rules.Add((0, rec.MinSlope, -1));
            z.Ref = new System.Collections.Generic.List<Point3>(rec.Bottom);
            // ★[2차 검토 0909 · 보통] 겹치는 옛 구간은 걷어내고 넣는다.
            int dup = rec.WallZones.RemoveAll(q => Overlaps(q.T0, q.T1, t0, t1, tot));
            rec.WallZones.Add(z);
            ed.WriteMessage($"\n[{label}] 구조물 {rim.Idx + 1} · {pick.Span:0.#}m 수직" + (z.HasWings ? " · 날개 2" : pick.Whole ? "" : " · 날개 자동")
                          + (dup > 0 ? $" · 겹친 옛 구간 {dup}개 합침" : ""));
            // ★[검토 0909 · 보통] 저장을 먼저 해야 DoExcav가 구간을 읽는다 — 다시 만들기가 실패하면 그 사실을 말한다(Rebuild).
            Save(doc, recs);
            Rebuild(doc, recs, ed, rim.Idx);
        }
        catch (System.Exception ex)
        {
            ed.WriteMessage($"\n[{label}] 실패 — " + ex.Message);
            try { DiagLog.Append($"\n■ 가시설 변환 예외 — {ex.GetType().Name}: {ex.Message}\n"); } catch { }
        }
        finally { PickSession.End(); }
    }

    /// <summary>[사면 변환] — 가시설 구간에 커서 → 그 구간 빨강 → 클릭 → 그 구간만 다시 사면.</summary>
    private static void PickSlope()
    {
        var doc = AcadApp.DocumentManager.MdiActiveDocument;
        if (doc == null) return;
        var ed = doc.Editor;
        const string label = "사면 변환";
        GradingSettings.SyncToDocument(doc);
        PickSession.Hook();
        if (!PickSession.Begin(ed, label)) return;
        try
        {
            var recs = Load(doc, out string why);
            if (recs == null) { ed.WriteMessage($"\n[{label}] " + why); return; }
            int nz = 0;
            foreach (var r in recs) nz += r.WallZones?.Count ?? 0;
            if (nz == 0) { ed.WriteMessage($"\n[{label}] 가시설 구간 없음"); return; }
            var rims = RimsOf(recs);

            // 커서 밑 구간 — 가장 가까운 둘레의 그 자리를 품은 구간(구간은 제 자로 잰다)
            (int Rec, SlopeZone Z, double Span)? Under(double x, double y)
            {
                var hit = NearestRim(rims, x, y);
                if (hit == null) return null;
                var rec = recs[hit.Value.R.Idx];
                if (rec.WallZones == null) return null;
                foreach (var z in rec.WallZones)
                {
                    if (z == null) continue;
                    var ruler = z.Ref ?? rec.Bottom;
                    if (ruler == null || ruler.Count < 3) continue;
                    var cum = GradingGeometry.CumLen2D(ruler);
                    double t = GradingGeometry.ParamAt(ruler, cum, x, y);
                    if (In(t, z.T0, z.T1)) return (hit.Value.R.Idx, z, GradingGeometry.SpanOf(z.T0, z.T1, cum[^1]));
                }
                return null;
            }

            void OnMove(object? s, PointMonitorEventArgs e)
            {
                try
                {
                    if (!e.Context.PointComputed) return;
                    var c = e.Context.ComputedPoint;
                    var u = Under(c.X, c.Y);
                    var dc = e.Context.DrawContext;
                    var hit = NearestRim(rims, c.X, c.Y);
                    double half = ZoneEditCommon.BandHalf(hit?.R.Total ?? 40);
                    if (u == null)
                    {
                        e.AppendToolTipText("가시설 아님");
                        if (dc != null) DrawZones(dc, recs, half);
                        return;
                    }
                    e.AppendToolTipText($"가시설 {u.Value.Span:0.00}m → 클릭 = 사면");
                    if (dc == null) return;
                    DrawZones(dc, recs, half, u.Value.Z);
                    var ruler = u.Value.Z.Ref ?? recs[u.Value.Rec].Bottom;
                    var cum = GradingGeometry.CumLen2D(ruler);
                    ZoneEditCommon.DrawBand(dc, GradingGeometry.SubPath(ruler, cum, u.Value.Z.T0, u.Value.Z.T1), half);
                }
                catch (System.Exception gex) { GlyphFail(gex); }
            }
            var pr = GetPointWatched(ed, "\n[사면 변환] 가시설 구간 클릭: ", OnMove);
            if (pr.Status != PromptStatus.OK) { ed.WriteMessage($"\n[{label}] 취소."); return; }
            var got = Under(pr.Value.X, pr.Value.Y);
            if (got == null) { ed.WriteMessage($"\n[{label}] 가시설 구간 아님 — 파란 띠 위 클릭"); return; }
            var rec0 = recs[got.Value.Rec];
            rec0.WallZones!.Remove(got.Value.Z);
            if (rec0.WallZones.Count == 0) rec0.WallZones = null;
            ed.WriteMessage($"\n[{label}] 구조물 {got.Value.Rec + 1} · 가시설 {got.Value.Span:0.#}m → 사면");
            Save(doc, recs);
            Rebuild(doc, recs, ed, got.Value.Rec);
        }
        catch (System.Exception ex)
        {
            ed.WriteMessage($"\n[{label}] 실패 — " + ex.Message);
            try { DiagLog.Append($"\n■ 사면 변환(가시설) 예외 — {ex.GetType().Name}: {ex.Message}\n"); } catch { }
        }
        finally { PickSession.End(); }
    }

    /// <summary>커서를 따라 그리며 점 하나를 받는다 — 어느 길로 빠져나가든 듣기를 끊는다(ZoneEditCommon.PickOnRing과 같은 문).
    /// ★고무줄 점선을 안 쓴다(JACK 0910 — 찍은 자리와 잡히는 자리가 달라 헷갈림).</summary>
    private static PromptPointResult GetPointWatched(Editor ed, string msg, PointMonitorEventHandler mon)
    {
        var opt = new PromptPointOptions(msg) { AllowNone = false };
        bool hooked = false;
        try { ed.PointMonitor += mon; hooked = true; } catch { }
        try { return ed.GetPoint(opt); }
        finally { if (hooked) { try { ed.PointMonitor -= mon; } catch { } } }
    }

    private static bool In(double t, double s, double e) => s <= e ? (t >= s - 1e-9 && t <= e + 1e-9) : (t >= s - 1e-9 || t <= e + 1e-9);

    // ── 자잘한 것 ─────────────────────────────────────────────────────────
    private static System.Collections.Generic.List<ExcavBundle> Load(AcDoc doc, out string why)
    {
        why = "";
        using var tr = doc.Database.TransactionManager.StartTransaction();
        var recs = ExcavBundleStore.TryLoadAll(doc.Database, tr, out string w, out bool tooNew);
        tr.Commit();
        if (tooNew) { why = w + " — 애드인을 최신으로 올린 뒤 다시 하세요."; return null; }
        if (recs == null || recs.Count == 0) { why = "터파기 기록이 없습니다 — [구조물 터파기]를 먼저 하세요."; return null; }
        return recs;
    }

    private static void Save(AcDoc doc, System.Collections.Generic.List<ExcavBundle> recs)
    {
        using var tr = doc.Database.TransactionManager.StartTransaction();
        ExcavBundleStore.SaveAll(doc.Database, tr, recs);
        tr.Commit();
    }

    /// <summary>구간을 바꿨으면 <b>다시 만들어야</b> 보인다 — 기록만 고치면 도면은 그대로다.</summary>
    private static void Rebuild(AcDoc doc, System.Collections.Generic.List<ExcavBundle> recs, Editor ed, int idx = 0)
    {
        ObjectId groundId = ObjectId.Null;
        try
        {
            using var tr = doc.Database.TransactionManager.StartTransaction();
            groundId = NoriCommand.FindByHandle(doc.Database, recs[idx].GroundHandle);
            if (groundId.IsNull) groundId = NoriCommand.FindByHandle(doc.Database, GradingSettings.LastGroundHandle);
            tr.Commit();
        }
        catch { }
        if (groundId.IsNull)
        {
            ed.WriteMessage("\n[가시설] 원지반을 못 찾아 다시 만들지 못했습니다 —"
                          + " [구조물 터파기]를 한 번 돌리면 반영됩니다.");
            return;
        }
        // ★★[2차 검토 0909] <b>고친 그 구조물</b>의 폴리선을 준다.
        //   <c>DoExcav</c>는 기록된 전부를 다시 굽지만, <b>넘긴 폴리선의 기록만</b>
        //   지금 도면 모양으로 새로 읽어 갈아끼운다(<c>cur</c>). 늘 <c>recs[0]</c>을 주면
        //   3번 구조물의 구간을 고쳐도 <b>1번이 다시 읽히고</b>, 제원 물려주기도 1번에만 걸린다.
        if (idx < 0 || idx >= recs.Count) idx = 0;
        ObjectId anyPoly = NoriCommand.FindByHandle(doc.Database, recs[idx].PolyHandle);
        if (anyPoly.IsNull)
        {
            ed.WriteMessage("\n[가시설] 구조물 폴리선을 못 찾아 다시 만들지 못했습니다.");
            return;
        }
        try
        {
            bool ok;
            using (ViewSurfaceCommand.Focus(doc.Database, null))
                // ★★[검토 0909 · 치명3] <c>keepSpec: true</c> — 구배·하한·기준면을 <b>그 기록의 것으로</b>.
                //   구간만 고치러 온 것이지 제원을 바꾸러 온 게 아니다.
                ok = ExcavCommand.DoExcav(doc, anyPoly, groundId, keepSpec: true);
            ViewSurfaceCommand.ShowAll();

            // ★★★[2차 검토 0909 · 높음1] <b>지표면이 "있다"를 성공으로 보면 안 됐다.</b>
            //   <c>DoExcav</c>는 조용히 돌아설 때 <b>아무것도 지우지 않으므로</b>
            //   직전 실행이 만든 <c>터파기면_DH</c>가 그대로 남아 <b>언제나 참</b>이었다 —
            //   막겠다던 바로 그 상태를 못 잡는 처방이었다.
            //   → 이제 <c>DoExcav</c>가 <b>자기가 구웠는지</b>를 직접 답한다.
            if (ok) ExcavPalette.Say("가시설 구간을 바꿔 터파기를 다시 만들었습니다.");
            else
            {
                ed.WriteMessage("\n[가시설] 구간은 저장했지만 지표면을 다시 만들지 못했습니다 —"
                              + " 창의 [터파기 지표면 생성]을 눌러 주세요(누르기 전 종단은 옛 형상입니다).");
                ExcavPalette.Say("구간은 저장했습니다 — [터파기 지표면 생성]을 눌러야 도면에 반영됩니다.");
            }
        }
        catch (System.Exception ex)
        {
            ed.WriteMessage("\n[가시설] 다시 만들기 실패 — " + ex.Message);
            try { ViewSurfaceCommand.ShowAll(); } catch { }
        }
    }

    /// <summary>두 구간이 겹치는가 — 랩(0을 지나 이어짐)까지 본다.</summary>
    private static bool Overlaps(double a0, double a1, double b0, double b1, double len)
    {
        bool In(double t, double s, double e) => s <= e ? (t >= s && t <= e) : (t >= s || t <= e);
        return In(a0, b0, b1) || In(a1, b0, b1) || In(b0, a0, a1) || In(b1, a0, a1);
    }
}
