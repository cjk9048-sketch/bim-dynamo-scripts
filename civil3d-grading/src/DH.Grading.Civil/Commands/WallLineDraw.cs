using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.GraphicsInterface;
using DH.Grading.Core;
using AcadPoly = Autodesk.AutoCAD.DatabaseServices.Polyline;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace DH.Grading.Civil.Commands;

/// <summary>★★★[v113.0 · JACK 1104 «옹벽 폴리선 — 경계 2점 자르기 + 안쪽 다단»] 옹벽 폴리선 <b>그리기 · 고르기</b> — 부지 [옹벽 추가](DHWALLADD) · 진입도로 [옹벽 추가](DHROADWALLADD)가 함께 쓴다(계획 4-1 ③ · 4-2 · 5-4).
/// <para>★★[JACK 1104] 점 찍기 = 첫 점 경계 위(붙임) → 가운데 점 경계 안 → 경계 위를 누르면 끝(점마다 Core <see cref="BoundaryDraw"/> · 거절이면 까닭 한 줄 · 그 점 다시) · U = 되돌리기 ·
/// 기존 폴리선 고르기(S — LWPOLYLINE · POLYLINE · 3D 폴리선 · XY만 · 호는 0.5 m로 촘촘히).
/// 찍는 동안은 <see cref="CursorLabel.GetPoint"/>의 지그가 경계(시안) · 찍은 토막 · 고무줄 · 경계와 만나는 자리(빨간 점)를 화면마다 다시 그린다(확대 · 축소에도 남는다 — v112.4).</para>
/// <para>다 그리면 <paramref name="classify"/>(Core <see cref="BoundaryCut.Classify"/>)로 판정 — 거절이면 까닭 한 줄 + 문제 자리 빨간 X · [다시 그리기(R)] · Esc = 그만.</para></summary>
internal static class WallLineDraw
{
    static readonly List<object> _stranded = new();
    /// <summary>소단 미리보기 걸린 시간을 로그에 적은 수(AutoCAD가 도는 동안 처음 몇 번만 — 현장 시간 재기).</summary>
    static int _benchLogged;

    internal sealed class Got
    {
        public List<Point3> Pts = new();
        public string SrcHandle = "";
        public BoundaryCut.Result Cut = new();
    }

    /// <summary>곡선 → 점열(꼭짓점 + 굽은 토막은 0.5 m) · XY만.</summary>
    internal static List<Point3> PointsOf(Curve c)
    {
        var r = new List<Point3>();
        double p0 = c.StartParam, p1 = c.EndParam;
        int nSeg = System.Math.Max(1, (int)System.Math.Round(p1 - p0));
        bool integral = System.Math.Abs((p1 - p0) - nSeg) < 1e-9 && !(c is Spline) && !(c is Arc) && !(c is Circle) && !(c is Ellipse);
        void Add(Point3d q) { if (r.Count == 0 || System.Math.Abs(r[^1].X - q.X) > 1e-9 || System.Math.Abs(r[^1].Y - q.Y) > 1e-9) r.Add(new Point3(q.X, q.Y, 0)); }
        if (integral)
        {
            for (int i = 0; i < nSeg; i++)
            {
                var a = c.GetPointAtParameter(p0 + i); var b = c.GetPointAtParameter(p0 + i + 1); var m = c.GetPointAtParameter(p0 + i + 0.5);
                Add(a);
                double cx = (a.X + b.X) / 2, cy = (a.Y + b.Y) / 2;
                if (System.Math.Sqrt((m.X - cx) * (m.X - cx) + (m.Y - cy) * (m.Y - cy)) > 0.01)
                {
                    double d0 = c.GetDistanceAtParameter(p0 + i), d1 = c.GetDistanceAtParameter(p0 + i + 1);
                    int k = System.Math.Max(2, (int)System.Math.Ceiling((d1 - d0) / 0.5));
                    for (int q = 1; q < k; q++) Add(c.GetPointAtDist(d0 + (d1 - d0) * q / k));
                }
            }
            Add(c.GetPointAtParameter(p1));
        }
        else
        {
            double L = c.GetDistanceAtParameter(p1);
            int k = System.Math.Max(2, (int)System.Math.Ceiling(L / 0.5));
            for (int q = 0; q <= k; q++) Add(c.GetPointAtDist(L * q / k));
        }
        return r;
    }

    /// <param name="tag">명령 이름(«옹벽 변환» · «진입도로 옹벽 변환»).</param>
    /// <param name="bd">생성된 지표면 경계(시안으로 보인다).</param>
    /// <param name="floor">바닥(계획선 · 도로 띠) — 회색으로.</param>
    internal static Got? Run(Document doc, string tag, IReadOnlyList<Point3> bd, IReadOnlyList<Point3>? floor, System.Func<List<Point3>, BoundaryCut.Result> classify,
        System.Func<WallBenchPreview.Input, WallBenchPreview.Result?>? benchPreview = null)
    {
        // ★★[v116.11 · JACK 1110 «옹벽 추가할 때 스냅 좀 잡혔으면 — 스냅이 안 잡혀»] 객체 스냅(OSMODE)을 끄지 않는다.
        //   v113.0은 찍는 동안 OSMODE를 0으로 껐다 — 기운 3D 화면에서 높이 있는 객체(지표면 등)에 붙은 점을 «시선 따라 Z = 0 평면으로 내림»(Flat)이
        //   높이 × 기울기만큼 옮겨 평면 자리가 수백 m 어긋났기 때문(1104 화면 없는 시험 338~346 m). 원인은 스냅이 아니라 내림이었다 —
        //   객체에 붙은 점은 그 X · Y가 곧 평면 자리이므로 내리지 않는다(<see cref="RunCore"/>의 Plan). 스냅 안 된 커서 점만 지금처럼 내린다.
        return RunCore(doc, tag, bd, floor, classify, benchPreview);
    }

    /// <summary>화면 시선 방향으로 경계 평면(Z = 0)에 내린 점 — 기운 3D 화면에서 커서가 가리키는 평면 자리(위에서 본 화면이면 X · Y 그대로).</summary>
    internal static Point3d Flat(Point3d p, Vector3d viewDir)
    {
        var f = BoundaryDraw.Flat(p.X, p.Y, p.Z, viewDir.X, viewDir.Y, viewDir.Z);
        return new Point3d(f.X, f.Y, 0);
    }

    static Got? RunCore(Document doc, string tag, IReadOnlyList<Point3> bd, IReadOnlyList<Point3>? floor, System.Func<List<Point3>, BoundaryCut.Result> classify,
        System.Func<WallBenchPreview.Input, WallBenchPreview.Result?>? benchPreview)
    {
        // ★[v116.11] 객체 스냅이 잡힌 점인가 — AutoCAD가 알려 주는 «객체 스냅됨»(PointMonitor) · 지그는 지금 커서 점마다 이 값을 새로 받는다
        var edS = doc.Editor;
        bool snapNow = false; Point3d snapAt = Point3d.Origin;
        PointMonitorEventHandler onPm = (_, e) =>
        {
            try { snapNow = (e.Context.History & PointHistoryBits.ObjectSnapped) != 0; snapAt = e.Context.ObjectSnappedPoint; }
            catch { snapNow = false; }
        };
        edS.PointMonitor += onPm;
        try { return RunCore2(doc, tag, bd, floor, classify, () => (snapNow, snapAt), benchPreview); }
        finally { try { edS.PointMonitor -= onPm; } catch { } }
    }

    static Got? RunCore2(Document doc, string tag, IReadOnlyList<Point3> bd, IReadOnlyList<Point3>? floor, System.Func<List<Point3>, BoundaryCut.Result> classify,
        System.Func<(bool On, Point3d At)> snapState, System.Func<WallBenchPreview.Input, WallBenchPreview.Result?>? benchPreview)
    {
        var ed = doc.Editor; var db = doc.Database;
        var ucs = ed.CurrentUserCoordinateSystem;
        Vector3d vdir = Vector3d.ZAxis;
        try { vdir = ed.GetCurrentView().ViewDirection; } catch { }
        double elev = 0; try { elev = System.Convert.ToDouble(AcadApp.GetSystemVariable("ELEVATION")); } catch { }
        // ★[v116.11] 찍은 점(WCS) → 평면 자리. 객체에 붙은 점(«객체 스냅됨»이 이 점 · 또는 작도 평면 밖 — 높이 있는 객체에 붙었거나 X,Y,Z를 친 점)은 X · Y 그대로,
        //   커서가 작도 평면에 찍은 점만 시선 따라 Z = 0으로 내린다(기운 3D 화면에서 커서가 가리키는 평면 자리 — 종전 그대로).
        Point3d Plan(Point3d wcs, out bool snapped)
        {
            var (on, at) = snapState();
            bool bySnap = on && at.DistanceTo(wcs) <= 1e-6;
            bool offPlane = false;
            try { offPlane = System.Math.Abs(wcs.TransformBy(ucs.Inverse()).Z - elev) > 1e-6; } catch { }
            snapped = bySnap || offPlane;
            return snapped ? new Point3d(wcs.X, wcs.Y, 0) : Flat(wcs, vdir);
        }
        // ★[설치 전 검토 4] 경계 · 바닥 다각형은 한 번만 짓는다(지그가 화면마다 다시 짓지 않게) · 붙이기는 그 고친 고리에(Classify와 같은 고리)
        var geo = BoundaryDraw.Prepare(bd, floor);
        if (geo == null) { ed.WriteMessage($"\n[{tag}] 경계 다각형 만들기 실패 — 안 바뀜"); return null; }
        var bdD = bd.Select(q => new Point3d(q.X, q.Y, 0)).ToList();
        var flD = floor?.Select(q => new Point3d(q.X, q.Y, 0)).ToList();
        double diag;
        {
            double mnx = bd.Min(q => q.X), mxx = bd.Max(q => q.X), mny = bd.Min(q => q.Y), mxy = bd.Max(q => q.Y);
            diag = System.Math.Sqrt((mxx - mnx) * (mxx - mnx) + (mxy - mny) * (mxy - mny));
        }
        double mk = System.Math.Max(0.3, diag * 0.006);
        void Cross(WorldDraw wd, double x, double y, short aci)
        {
            wd.SubEntityTraits.Color = aci;
            wd.Geometry.WorldLine(new Point3d(x - mk, y - mk, 0), new Point3d(x + mk, y + mk, 0));
            wd.Geometry.WorldLine(new Point3d(x - mk, y + mk, 0), new Point3d(x + mk, y - mk, 0));
        }
        void Ring(WorldDraw wd, List<Point3d> r, short aci)
        {
            wd.SubEntityTraits.Color = aci;
            for (int i = 0; i < r.Count; i++) wd.Geometry.WorldLine(r[i], r[(i + 1) % r.Count]);
        }
        void Box(WorldDraw wd, double x, double y, short aci)
        {
            wd.SubEntityTraits.Color = aci;
            var c = new[] { new Point3d(x - mk, y - mk, 0), new Point3d(x + mk, y - mk, 0), new Point3d(x + mk, y + mk, 0), new Point3d(x - mk, y + mk, 0) };
            for (int i = 0; i < 4; i++) wd.Geometry.WorldLine(c[i], c[(i + 1) % 4]);
        }
        // 경계 위로 붙는 거리 = 화면 높이의 1.5 %(0.2 ~ 15 m) — 확대하면 촘촘히 · 축소하면 넉넉히
        double SnapTol()
        {
            try { var vh = System.Convert.ToDouble(Autodesk.AutoCAD.ApplicationServices.Core.Application.GetSystemVariable("VIEWSIZE")); return System.Math.Clamp(vh * 0.015, 0.2, 15); }
            catch { return 1.0; }
        }
        // ★[v117.14 · JACK 1112 «경계 위에 마우스를 올리면 실시간으로 소단 · 침범이 보이게»] 끝 자리(경계 위)에 커서가 오면 어림 소단 미리보기를 그린다.
        //   같은 자리(점 수 · 끝 점 1 cm · 마지막 찍은 점 1 cm)는 다시 안 셈 · 한 번이라도 50 ms 넘으면 이 명령 동안 끔(DiagLog).
        // ★[v117.14.1 · JACK 1112 «미리보기는 부지 근처에 있을 때만 생기는 것 같은데 어디든 생기게(단수 포함)»] 원인 둘 —
        //   ① 커서가 끝 자리(경계 위 · 밖)일 때만 셈(가운데 점 자리는 안 셈) ② 어림 단 수 n = 1(벽 ≤ 단높이 — 부지에서 먼 선)이면 그릴 것이 없었다(벽은 부지 쪽에서만 높다).
        //   이제 커서가 어디 있든 셈 — 끝 자리 = 다 그린 선 판정 · 가운데 자리 = 커서 뒤를 경계까지 임시로 이어(① 마지막 토막 방향 그대로 ② 가장 가까운 경계 점) 판정 · 소단은 커서까지만 ·
        //   판정 못 한 자리(부지 바닥에 닿아 거절 등) = 앞서 판정한 안쪽 부호(없으면 바닥 쪽)로 · 경계로 자름. n = 1도 «1단» 글.
        bool benchOff = false; int benchSlow = 0;
        (int N, int K, long X, long Y, long LX, long LY)? bKey = null; WallBenchPreview.Result? bRes = null;
        int sideKnown = 0; (double X, double Y) sideFrom = default;
        WallBenchPreview.Result? BenchAt(List<(double X, double Y)> drawn, BoundaryDraw.Kind kind, double x, double y)
        {
            if (benchPreview == null || drawn.Count == 0) return null;
            var key = (drawn.Count, (int)kind, (long)System.Math.Round(x * 100), (long)System.Math.Round(y * 100), (long)System.Math.Round(drawn[^1].X * 100), (long)System.Math.Round(drawn[^1].Y * 100));
            if (bKey is { } k0 && k0 == key) return bRes;   // ★[설치 전 검토 MED] 같은 자리는 끈 뒤에도 방금 셈을 그대로 보임(한 번 번쩍이고 사라지지 않게)
            if (benchOff) return null;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            WallBenchPreview.Result? r = null; string how = "";
            try
            {
                var path = new List<(double X, double Y)>(drawn) { (x, y) };
                var tries = new List<List<(double X, double Y)>>();
                if (kind == BoundaryDraw.Kind.End) tries.Add(path);
                else if (kind == BoundaryDraw.Kind.Inside && !(sideKnown != 0 && sideFrom == drawn[0]))   // ★[설치 전 검토 · 큰 부지 속도] 안쪽 부호를 이미 알면 판정 생략 → 아래 싼 길
                {
                    var prev = drawn[^1];
                    if (BoundaryDraw.RayExit(geo, x, y, x - prev.X, y - prev.Y) is { } ex) tries.Add(path.Append(ex).ToList());
                    var nb = BoundaryDraw.Nearest(geo, x, y); tries.Add(path.Append((nb.X, nb.Y)).ToList());
                }
                double showLen = 0;
                for (int i = 0; i + 1 < path.Count; i++) showLen += System.Math.Sqrt((path[i + 1].X - path[i].X) * (path[i + 1].X - path[i].X) + (path[i + 1].Y - path[i].Y) * (path[i + 1].Y - path[i].Y));
                foreach (var tl in tries)
                {
                    var ct = classify(BoundaryDraw.ExtendSafe(geo, tl, 1.0, out _, out _));
                    if (!ct.Ok) continue;
                    r = benchPreview(new WallBenchPreview.Input { Line = ct.Line, NewRing = ct.NewRing, CutRing = ct.CutRing, ShowLen = kind == BoundaryDraw.Kind.End ? double.NaN : showLen });
                    if (r != null) { sideKnown = r.Side; sideFrom = drawn[0]; how = kind == BoundaryDraw.Kind.End ? "판정" : "임시 판정"; break; }
                }
                if (r == null)
                {
                    int side = sideKnown != 0 && sideFrom == drawn[0] ? sideKnown : 0;
                    r = benchPreview(new WallBenchPreview.Input { Line = path.Select(q => new Point3(q.X, q.Y, 0)).ToList(), NewRing = bd, Side = side });
                    how = side != 0 ? "앞 판정 부호" : "바닥 쪽";
                }
            }
            catch { r = null; }
            bKey = key; bRes = r;
            // ★[설치 전 검토 MED] 첫 셈(도면 연 뒤 처음 — 코드 적재)은 느릴 수 있다 → 3번 잇달아 50 ms 넘을 때만 끔 · 처음 몇 번 걸린 시간은 로그(현장 시간 재기)
            long ms = sw.ElapsedMilliseconds;
            benchSlow = ms > 50 ? benchSlow + 1 : 0;
            if (benchSlow >= 3)
            {
                benchOff = true;
                try { DiagLog.AppendCarry($"\n  [{tag}] 소단 미리보기 {ms} ms(3번 잇달아 50 ms 넘음) — 이 명령 동안 끔"); } catch { }
            }
            else if (_benchLogged < 5) { _benchLogged++; try { DiagLog.AppendCarry($"\n  [{tag}] 소단 미리보기 {ms} ms · {kind} · {(r == null ? "없음" : $"{how} · {r.N}단 · 침범 {r.Hit.Count}")}"); } catch { } }
            return r;
        }
        void DrawBench(WorldDraw wd, WallBenchPreview.Result r, Point3d cur)
        {
            // ★★[v117.15 · JACK 1112 «옹벽 추가 시 미리보기가 잘 안 보여 — 프레임이 끊기고 보였다 안 보였다 · 해치 말고 다른 방법»]
            //   현장 로그(16:36 · 셈 0~18 ms — 셈은 느리지 않음): 빗금(화면 7 px마다 선 — 수백 줄)과 반투명 칠을 커서 움직일 때마다 다시 그림 → 끊김.
            //   → 빗금 · 칠 없앰 · 소단 = 초록 굵은 테(화면 2 px) · 침범 = 빨간 굵은 테(3 px) + «부지경계 침범» · 단 수 글은 늘(1단도 — 사라진 듯 안 보이게).
            double px = CursorLabel.PixelUnit();
            var tr = wd.SubEntityTraits;
            foreach (var o in r.Ok) { WideRing(wd, o.Outer, 2.2 * px, OkGreen); foreach (var hl in o.Holes) WideRing(wd, hl, 2.2 * px, OkGreen); }
            tr.TrueColor = new Autodesk.AutoCAD.Colors.EntityColor(110, 190, 130);
            foreach (var ln in r.Lines) for (int i = 0; i + 1 < ln.Count; i++) wd.Geometry.WorldLine(new Point3d(ln[i].X, ln[i].Y, 0), new Point3d(ln[i + 1].X, ln[i + 1].Y, 0));
            foreach (var h in r.Hit) { WideRing(wd, h.Outer, 3.2 * px, HitRed); foreach (var hl in h.Holes) WideRing(wd, hl, 3.2 * px, HitRed); }
            tr.TrueColor = new Autodesk.AutoCAD.Colors.EntityColor(70, 150, 90);
            foreach (var (x, y, k) in r.BenchLabels) Label(wd, x, y, "소단 " + k, px, 11);
            tr.Color = 1;
            foreach (var (x, y) in r.HitLabels) Label(wd, x, y, HitText, px, 13);
            // ★[v117.14.1 · JACK 1112 «단수 포함»] 단 수 — 커서 오른쪽 위(이름표 카드는 오른쪽 아래) · 침범이면 빨강
            tr.Color = (short)(r.Hit.Count > 0 ? 1 : GuideAci);
            Label(wd, cur.X + 34 * px, cur.Y + 16 * px, TierText(r.N), px, 14);
        }
        while (true)
        {
            var pts = new List<Point3d>();
            string src = "";
            bool drawnMode = true;   // 찍어 그림(첫 · 끝 = 경계 위 → 판정 전에 바깥으로 늘임) · 거짓 = 기존 폴리선 고르기(S)
            // ── 점 찍기(★★[v113.0 · JACK 1104 «[옹벽 추가]» — 첫 점 = 경계 위 · 가운데 = 경계 안 · 경계 위를 누르면 끝] · 점마다 BoundaryDraw) · 고르기(S) ──
            while (true)
            {
                double snap = SnapTol();
                bool first = pts.Count == 0;
                var ppo = new PromptPointOptions(first ? $"\n[{tag}] 시작점 — 가장 가까운 경계 점에 붙음 [고르기(S)]: " : $"\n[{tag}] 다음 점 — 사면 띠 안(계획선 밖) · 경계 위나 밖을 누르면 끝 [되돌리기(U)]: ");
                if (first) ppo.Keywords.Add("S", "S", "고르기(S)");
                else
                {
                    if (SelfCheckLog.Headless) { ppo.UseBasePoint = true; ppo.BasePoint = pts[^1].TransformBy(ucs.Inverse()); ppo.UseDashedLine = false; }
                    ppo.Keywords.Add("U", "U", "되돌리기(U)");
                }
                ppo.AllowNone = false;
                ppo.AppendKeywordsToMessage = false;
                var drawn = pts.Select(q => (q.X, q.Y)).ToList();
                // ★★[v113.3 · JACK 1105 «처음 시작부터 안내선이 경계에 닿게»] 시작점은 커서가 어디 있든 가장 가까운 경계 점(붙임 거리 제한 없음) — 안내선 + 네모로 미리 보임
                BoundaryDraw.Step Try(double x, double y) => first ? BoundaryDraw.First(geo, x, y, double.MaxValue) : BoundaryDraw.Next(geo, drawn, x, y, snap);
                string? why = null;   // 지금 커서 자리의 거절 까닭(카드 글 — ★[v113.3 · JACK 1105 «다음 점이 클릭이 안 돼»] 누르기 전에 보이게)
                void Path(WorldDraw wd, Point3d cur, bool has)
                {
                    why = null;
                    Ring(wd, bdD, 4);
                    if (flD != null) Ring(wd, flD, 8);
                    BoundaryDraw.Step? st = null;
                    var cf = cur;
                    if (has)
                    {
                        cf = Plan(cur, out _);   // ★[v116.11] 스냅 잡힌 점은 그 자리로(미리보기 안내선도)
                        try { st = Try(cf.X, cf.Y); } catch { st = null; }
                    }
                    // ★[v117.14.1 · JACK 1112 «어디든 생기게(단수 포함)»] 소단 미리보기 — 받는 자리(가운데 · 끝)와 부지 바닥에 닿아 거절된 자리 모두 · 칠이 선 밑에 오게 먼저 그림
                    WallBenchPreview.Result? br = null;
                    if (st != null && drawn.Count > 0 && (st.Ok || st.Floor))
                    {
                        br = st.Ok ? BenchAt(drawn, st.Kind, st.X, st.Y) : BenchAt(drawn, BoundaryDraw.Kind.No, cf.X, cf.Y);
                        if (br != null) DrawBench(wd, br, cf);
                    }
                    wd.SubEntityTraits.Color = GuideAci;   // ★[v117.14.1 · JACK 1112 «노란 가이드선이 노리선과 헷갈림 → 초록»]
                    for (int i = 0; i + 1 < drawn.Count; i++) wd.Geometry.WorldLine(new Point3d(drawn[i].X, drawn[i].Y, 0), new Point3d(drawn[i + 1].X, drawn[i + 1].Y, 0));
                    if (st == null) return;
                    if (!st.Ok) why = st.Why;
                    if (drawn.Count == 0)
                    {
                        // 시작점 안내선: 커서 → 붙을 경계 점(받으면 초록 · 바닥에 붙은 자리면 빨강)
                        var nb = st.Ok ? (st.X, st.Y) : st.At is (double ax, double ay) ? (ax, ay) : (cf.X, cf.Y);
                        wd.SubEntityTraits.Color = (short)(st.Ok ? 3 : 1);
                        wd.Geometry.WorldLine(cf, new Point3d(nb.Item1, nb.Item2, 0));
                        if (st.Ok) Box(wd, st.X, st.Y, 3); else Cross(wd, nb.Item1, nb.Item2, 1);
                        return;
                    }
                    var to = st.Ok ? new Point3d(st.X, st.Y, 0) : cf;
                    // ★[v117.14.1 · JACK 1112 «부지에 겹치면 … 지금은 그냥 빨간선만 나오고 X»] 부지 바닥에 닿아 거절된 자리는 빨간 칠(겹친 몫)이 알려 준다 — 고무줄은 안내색 · X 없음
                    //   (칠이 없을 때 — 0.1 m 안으로 붙기만 함 등 — 와 다른 거절은 종전대로 빨간 선 + X)
                    bool red = !st.Ok && !(st.Floor && br != null && br.Hit.Count > 0);
                    wd.SubEntityTraits.Color = red ? (short)1 : GuideAci;
                    wd.Geometry.WorldLine(new Point3d(drawn[^1].X, drawn[^1].Y, 0), to);
                    if (st.Kind == BoundaryDraw.Kind.End) Box(wd, st.X, st.Y, 3);   // 경계 위에 붙는 끝 자리
                    else if (red && st.At != null) Cross(wd, st.At.Value.X, st.At.Value.Y, 1);
                }
                string label = first ? "시작점 · 가까운 경계에 붙음" : "다음 점 · 경계 위·밖 = 끝";
                var lr = CursorLabel.GetPoint(ed, ppo, label, "", Path, () => why);
                if (lr.Status == PromptStatus.Keyword)
                {
                    string kw = (lr.StringResult ?? "").Trim().ToUpperInvariant();
                    if (kw == "U") { if (pts.Count > 0) pts.RemoveAt(pts.Count - 1); continue; }
                    if (kw == "S" && pts.Count == 0)
                    {
                        var peo = new PromptEntityOptions($"\n[{tag}] 폴리선 고르기(폴리선 · 3D 폴리선): ");
                        peo.SetRejectMessage("\n폴리선만 가능");
                        peo.AddAllowedClass(typeof(AcadPoly), true); peo.AddAllowedClass(typeof(Polyline2d), true); peo.AddAllowedClass(typeof(Polyline3d), true);
                        var per = ed.GetEntity(peo);
                        if (per.Status != PromptStatus.OK) continue;
                        using var tr = db.TransactionManager.StartTransaction();
                        if (tr.GetObject(per.ObjectId, OpenMode.ForRead) is Curve cv)
                        {
                            var got = PointsOf(cv);
                            pts = got.Select(q => new Point3d(q.X, q.Y, 0)).ToList();
                            src = per.ObjectId.Handle.ToString();
                        }
                        tr.Commit();
                        if (pts.Count >= 2) { drawnMode = false; break; }
                        ed.WriteMessage("\n  폴리선 점 읽기 실패");
                        pts.Clear();
                        continue;
                    }
                    continue;
                }
                if (lr.Status != PromptStatus.OK) { ed.WriteMessage($"\n[{tag}] 안 바뀜"); return null; }
                var w = Plan(lr.Value.TransformBy(ucs), out bool snapped);   // UCS → WCS → 스냅 잡힌 점은 X · Y 그대로 · 아니면 시선 따라 경계 평면(Z = 0)
                try { DiagLog.AppendCarry($"\n  [{tag}] 점 {pts.Count + 1} ({w.X:F4}, {w.Y:F4}){(snapped ? " · 객체 스냅" : "")}"); } catch { }
                var stp = Try(w.X, w.Y);
                if (!stp.Ok)
                {
                    ed.WriteMessage($"\n  {stp.Why}");
                    try { var nb = BoundaryDraw.Nearest(geo, w.X, w.Y); DiagLog.AppendCarry($"\n  [{tag}] 점 거절 — ({w.X:F3}, {w.Y:F3}) · 경계까지 {nb.D:F3} m · 붙임 {snap:F2} m · 점 {pts.Count}개 · {stp.Why}"); } catch { }
                    continue;
                }
                pts.Add(new Point3d(stp.X, stp.Y, 0));
                if (stp.Kind == BoundaryDraw.Kind.End) break;
            }
            if (pts.Count < 2) { ed.WriteMessage($"\n[{tag}] 점 부족 — 안 바뀜"); return null; }
            // 찍어 그린 선은 첫 · 끝 토막을 경계 밖으로 1 m 늘여 판정(Classify는 양 끝이 경계 밖이어야 — 경계 위 두 점이 그대로 교점)
            // ★[v117.2 · JACK 1111 13:5x «끝 점이 경계 안»] 늘인 점이 경계 안(뾰족한 V 자리)이거나 늘인 토막이 경계에 또 닿으면 그 끝은 안 늘임 — 경계 위 클릭 점 그대로(Classify가 1 mm 안 끝을 받음)
            bool extA = true, extB = true;
            var list = drawnMode ? BoundaryDraw.ExtendSafe(geo, pts.Select(q => (q.X, q.Y)).ToList(), 1.0, out extA, out extB) : pts.Select(q => new Point3(q.X, q.Y, 0)).ToList();
            var cut = classify(list);
            try { DiagLog.AppendCarry($"\n  [{tag}] 폴리선 판정 — 점 {list.Count}개{(src.Length > 0 ? $"(기존 폴리선 {src})" : "")}{(extA && extB ? "" : $" · 안 늘인 끝 {(extA ? "" : "A")}{(extB ? "" : "B")}(늘이면 경계에 다시 닿음 — 클릭 점 그대로)")} · {BoundaryCut.CodeName(cut.Code)} · 교차 {cut.Crossings}" + (cut.Ok ? $" · A({cut.A.X:F2},{cut.A.Y:F2}) · B({cut.B.X:F2},{cut.B.Y:F2}) · 교차각 {cut.AngleA:F0}°/{cut.AngleB:F0}° · 옛 {cut.AreaOld:F1} = 새 {cut.AreaNew:F1} + 잘라낼 {cut.AreaCut:F1}㎡" : " · " + cut.Why)); } catch { }
            SelfCheckLog.Line(tag + " — 폴리선 판정", cut.Ok ? '–' : '⚠', $"{BoundaryCut.CodeName(cut.Code)} · 교차 {cut.Crossings}" + (cut.Ok ? "" : " · " + cut.Why), doc.Name);
            if (cut.Ok)
            {
                foreach (var wn in cut.Warn) ed.WriteMessage($"\n[{tag}] ⚠{wn}");
                return new Got { Pts = list, SrcHandle = src, Cut = cut };
            }
            // ── 거절 — 까닭 + 빨간 X(질문 동안 · 명령당 한 번 걷기) ──
            ed.WriteMessage($"\n[{tag}] {WallEasy.Cut(cut.Code, cut.Why)} — 안 바뀜");
            var live = Show(cut.Marks, pts.Select(q => new Point3(q.X, q.Y, 0)).ToList(), mk);   // ★[v117.9.2 · JACK 1112 «경계 밖으로 노란 선 길어짐»] 찍은 선 그대로 보임(1 m 늘인 판정용 선은 안 보임)
            try
            {
                var pk = new PromptKeywordOptions($"\n[{tag}] [다시 그리기(R)]: ");
                pk.Keywords.Add("R", "R", "다시 그리기(R)");
                pk.AllowNone = false; pk.AppendKeywordsToMessage = false;
                var kr = ed.GetKeywords(pk);
                if (kr.Status != PromptStatus.OK && kr.Status != PromptStatus.Keyword) return null;
            }
            finally { Drop(live); }
        }
    }

    internal const string HitText = "부지경계 침범";
    /// <summary>★[v117.14.1 · JACK 1112 «옹벽추가 선 그릴 때 노란색 가이드선이 노리선하고 헷갈리니깐 초록색으로»] 그린 선 · 고무줄 · 판정 화면의 그은 선 색(ACI 3 초록 — 옛 2 노랑).</summary>
    internal const short GuideAci = 3;
    /// <summary>★[v117.14.1 · JACK 1112 «단수 포함»] 단 수 글(«3단»).</summary>
    internal static string TierText(int n) => n + "단";

    static Autodesk.AutoCAD.GraphicsInterface.TextStyle? _bts;
    /// <summary>지그 안 글(화면 px 크기 — 가운데 맞춤) · 글꼴은 이름표와 같은 맑은 고딕(따로 만든 글꼴 — 이름표 것과 크기를 섞지 않게).</summary>
    static readonly Autodesk.AutoCAD.Colors.Color OkGreen = Autodesk.AutoCAD.Colors.Color.FromRgb(60, 170, 90), HitRed = Autodesk.AutoCAD.Colors.Color.FromRgb(225, 40, 40);
    /// <summary>★[v117.15 · JACK 1112 «해치 말고»] 닫힌 테를 굵게(폭 = 세계 단위 · 화면 px × n) — 선 굵기 표시(LWDISPLAY)와 상관없이 굵게 보인다.</summary>
    static void WideRing(WorldDraw wd, IReadOnlyList<Point3> ring, double w, Autodesk.AutoCAD.Colors.Color c)
    {
        if (ring.Count < 3) return;
        try
        {
            using var pl = new AcadPoly();
            for (int i = 0; i < ring.Count; i++) pl.AddVertexAt(i, new Point2d(ring[i].X, ring[i].Y), 0, w, w);
            pl.Closed = true; pl.Color = c;
            wd.Geometry.Draw(pl);
        }
        catch { }
    }

    static void Label(WorldDraw wd, double x, double y, string text, double px, double sizePx)
    {
        if (_bts == null) { var t = new Autodesk.AutoCAD.GraphicsInterface.TextStyle(); t.Font = new Autodesk.AutoCAD.GraphicsInterface.FontDescriptor("Malgun Gothic", true, false, 129, 34); _bts = t; }
        var ts = _bts; ts.TextSize = sizePx * px;
        double w = text.Length * ts.TextSize * 0.9;
        try { var e = ts.ExtentsBox(text, true, true, null); if (e.MaxPoint.X > e.MinPoint.X) w = e.MaxPoint.X - e.MinPoint.X; } catch { }
        wd.Geometry.Text(new Point3d(x - w / 2, y - ts.TextSize / 2, 0), Vector3d.ZAxis, Vector3d.XAxis, text, true, ts);
    }

    /// <summary>★[v117.14] «자리» 거절 — 소단 띠 해치(연초록) · 침범 몫 빨간 해치 + 글 «부지경계 침범»(임시 그림 · <see cref="Drop"/>으로 걷음). 헤드리스는 빈 목록.
    /// ★[v117.14.1 · JACK 1112 «겹치는 부분이 해치가 빨간색으로»] 침범 = 빨간 반투명 칠(세모 Solid) + 빨간 테(옛: 빨간 빗금선) · 단 수 글(«3단»).</summary>
    internal static List<Entity> ShowBench(WallBenchPreview.Result r)
    {
        var live = new List<Entity>();
        if (SelfCheckLog.Headless) return live;
        var tm = TransientManager.CurrentTransientManager;
        var noIds = new IntegerCollection();
        void Add(Entity e) { bool ok = false; try { ok = tm.AddTransient(e, TransientDrawingMode.DirectShortTerm, 128, noIds); } catch { } if (ok) live.Add(e); else e.Dispose(); }
        double px = CursorLabel.PixelUnit();
        // ★[v117.15 · JACK 1112 «해치 말고»] 소단 = 초록 굵은 테 · 침범 = 빨간 굵은 테(빗금 · 반투명 칠 없앰 — 지그 미리보기와 같은 꼴)
        void Wide(IReadOnlyList<Point3> l, double w, short aci)
        {
            if (l.Count < 3) return;
            var pl = new AcadPoly();
            for (int i = 0; i < l.Count; i++) pl.AddVertexAt(i, new Point2d(l[i].X, l[i].Y), 0, w, w);
            pl.Closed = true; pl.ColorIndex = aci;
            Add(pl);
        }
        foreach (var o in r.Ok) { Wide(o.Outer, 2.2 * px, 3); foreach (var hl in o.Holes) Wide(hl, 2.2 * px, 3); }
        foreach (var h in r.Hit) { Wide(h.Outer, 3.2 * px, 1); foreach (var hl in h.Holes) Wide(hl, 3.2 * px, 1); }
        void Txt(double x, double y, string text, short aci, double sizePx)
        {
            var mt = new MText();
            try { mt.SetDatabaseDefaults(HostApplicationServices.WorkingDatabase); } catch { }   // ★[설치 전 검토 MED] 도면 기본(글꼴 스타일 등) — 도면 밖 글이 안 그려지는 일 없게
            mt.Contents = "{\\fMalgun Gothic|b1|c129;" + text + "}"; mt.Location = new Point3d(x, y, 0); mt.TextHeight = sizePx * px; mt.Attachment = AttachmentPoint.MiddleCenter; mt.ColorIndex = aci;
            Add(mt);
        }
        foreach (var (x, y) in r.HitLabels) Txt(x, y, HitText, 1, 13);
        // 단 수 — 보인 선 가운데에서 안쪽으로 화면 24 px
        Txt(r.MidX + r.MidNx * 24 * px, r.MidY + r.MidNy * 24 * px, TierText(r.N), (short)(r.Hit.Count > 0 ? 1 : GuideAci), 14);
        return live;
    }

    /// <summary>질문 동안 보일 임시 그림(빨간 X · 그린 폴리선) — 걷기는 <see cref="Drop"/>(못 걷으면 붙잡는다 — WallPolyDraw와 같은 _stranded 규칙).</summary>
    internal static List<Entity> Show(IEnumerable<(double X, double Y)> marks, IReadOnlyList<Point3>? line, double mk, short lineAci = GuideAci, IEnumerable<IReadOnlyList<Point3>>? more = null, short moreAci = 3)
    {
        var live = new List<Entity>();
        if (SelfCheckLog.Headless) return live;
        var tm = TransientManager.CurrentTransientManager;
        var noIds = new IntegerCollection();
        void Add(Entity e) { bool ok = false; try { ok = tm.AddTransient(e, TransientDrawingMode.DirectShortTerm, 128, noIds); } catch { } if (ok) live.Add(e); else e.Dispose(); }
        foreach (var (x, y) in marks)
        {
            Add(new Line(new Point3d(x - mk, y - mk, 0), new Point3d(x + mk, y + mk, 0)) { ColorIndex = 1 });
            Add(new Line(new Point3d(x - mk, y + mk, 0), new Point3d(x + mk, y - mk, 0)) { ColorIndex = 1 });
        }
        void Pl(IReadOnlyList<Point3> l, short aci, bool closed)
        {
            if (l == null || l.Count < 2) return;
            var pl = new AcadPoly();
            for (int i = 0; i < l.Count; i++) pl.AddVertexAt(i, new Point2d(l[i].X, l[i].Y), 0, 0, 0);
            pl.Closed = closed; pl.ColorIndex = aci;
            Add(pl);
        }
        if (line != null) Pl(line, lineAci, false);
        if (more != null) foreach (var m in more) Pl(m, moreAci, false);
        return live;
    }

    internal static void Drop(List<Entity> live)
    {
        if (live == null) return;
        var tm = TransientManager.CurrentTransientManager;
        var noIds = new IntegerCollection();
        foreach (var g in live)
        {
            bool erased = false;
            try { erased = tm.EraseTransient(g, noIds); } catch { }
            if (erased) { try { g.Dispose(); } catch { } }
            else { try { System.GC.SuppressFinalize(g); } catch { } lock (_stranded) _stranded.Add(g); }
        }
        live.Clear();
    }
}
