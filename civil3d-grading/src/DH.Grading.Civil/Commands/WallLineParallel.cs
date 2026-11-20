using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.GraphicsInterface;
using DH.Grading.Core;

namespace DH.Grading.Civil.Commands;

/// <summary>★★[JACK 1119 «고르기S에 추가해서 도로선평행(P) — 누르면 마우스가 선택 네모로 바뀌고 도로의 끝선을 선택 → 사면 방향으로 원하는 평행길이만큼 오프셋 →
/// 그 오프셋선에 맞춰 옹벽 설치 · 3D폴리선은 오프셋이 안 되니 폴리선으로 변환해서»] 진입도로 [옹벽 추가]의 시작 질문 [도로선평행(P)].
/// <para>도로 끝선 클릭(네모 커서 · 선이면 그 선 · 도로 지표면을 누르면 누른 자리에 가까운 설계 도로 끝) → 평행 거리(이 세션 마지막 값) →
/// 평면 선으로 펴서(높이 버림 · 호는 0.5 m) 사면 쪽으로 옮김(Core <see cref="RoadEdgeOffset"/> — 쪽은 묻지 않음) → 구간(Enter = 전체) →
/// 경계를 두 번 지나는 선을 돌려준다 — 부르는 쪽(<see cref="WallLineDraw"/>)이 고르기(S)와 똑같이 판정 · 미리보기 · 적용.</para>
/// <para>도면에 아무것도 안 넣는다(옮긴 선은 메모리 · 화면은 지그와 임시 그림).</para></summary>
internal static class WallLineParallel
{
    /// <summary>이 세션 마지막 평행 거리(첫 값 2 m).</summary>
    static double _dist = 2.0;

    /// <param name="plan">찍은 점(WCS) → 평면 자리(<see cref="WallLineDraw"/>의 스냅 · 시선 내림 규칙 그대로).</param>
    /// <param name="edgeAt">도로 지표면을 눌렀을 때 — 누른 자리에 가까운 설계 도로 끝(평면 점열) · 없으면 null.</param>
    /// <returns>판정에 넘길 선(z = 0) · null = 취소(시작 질문으로).</returns>
    internal static List<Point3>? Pick(Document doc, string tag, IReadOnlyList<Point3> bd, IReadOnlyList<Point3> band,
        System.Func<Point3d, Point3d> plan, System.Func<double, double, List<Point3>?>? edgeAt, out string src)
    {
        src = "";
        var ed = doc.Editor; var db = doc.Database;
        var ic = System.Globalization.CultureInfo.InvariantCulture;
        var bdD = bd.Select(q => new Point3d(q.X, q.Y, 0)).ToList();
        var flD = band.Select(q => new Point3d(q.X, q.Y, 0)).ToList();
        while (true)
        {
            // ① 도로 끝선 — 네모 커서(GetEntity)
            var peo = new PromptEntityOptions($"\n[{tag}] 도로 끝선 클릭 (Esc=취소): ") { AllowNone = false };
            var per = ed.GetEntity(peo);
            if (per.Status != PromptStatus.OK) return null;
            List<Point3>? edge = null; string what = "";
            var pk = plan(per.PickedPoint.TransformBy(ed.CurrentUserCoordinateSystem));
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var ent = tr.GetObject(per.ObjectId, OpenMode.ForRead) as Entity;
                if (ent is Curve cv && ent is not Autodesk.Civil.DatabaseServices.Alignment)
                {
                    try { edge = WallLineDraw.PointsOf(cv); } catch { edge = null; }
                    what = $"{ent.GetType().Name} {per.ObjectId.Handle} «{ent.Layer}»";
                }
                else what = $"{ent?.GetType().Name ?? "?"} {per.ObjectId.Handle} «{ent?.Layer}»";
                tr.Commit();
            }
            // 거리를 묻기 전에 «도로 끝선인가 · 쪽»부터 — 작은 거리(≤ 0.5 m)로 한 번(지난 거리가 크면 여기서 꼬여 거리를 못 묻는다 · 꼬임은 ② 뒤에 판정)
            double chkD = System.Math.Min(_dist, 0.5);
            //   Side ≠ 0 = 끝선 · 쪽 판정은 통과(평행선 못 만듦 · 꼬임은 거리 탓 — 여기서 거절 안 함)
            var chk = edge != null && edge.Count >= 2 ? RoadEdgeOffset.Offset(edge, chkD, band) : null;
            if (chk == null || chk.Side == 0)
            {
                // ★[1119 화면 없는 시험] 절토 쪽 도로 끝에는 그린 선이 없고 노리선 머리가 닿아 있다 — 고른 것이 도로 끝선이 아니어도(지표면 · 노리선 등)
                //   누른 자리가 설계 도로 끝에서 1.5 m 안이면 그 설계 도로 끝을 쓴다
                var de = edgeAt?.Invoke(pk.X, pk.Y);
                var dc = de != null && de.Count >= 2 && RoadEdgeOffset.Project(de, pk.X, pk.Y).D <= 1.5 ? RoadEdgeOffset.Offset(de, chkD, band) : null;
                if (dc != null && dc.Side != 0)
                {
                    try { DiagLog.AppendCarry(string.Format(ic, "\n  [{0}] 도로선평행 — 고른 것 {1}{2} → 누른 자리({3:F2}, {4:F2}) 가까운 설계 도로 끝", tag, what, chk == null ? "(선 아님)" : " 거절 — " + chk.Log, pk.X, pk.Y)); } catch { }
                    edge = de; chk = dc; what += " → 설계 도로 끝";
                }
                else
                {
                    ed.WriteMessage($"\n  {(chk == null ? "도로 끝선이 아님 — 선을 클릭" : chk.Why)}");
                    try { DiagLog.AppendCarry($"\n  [{tag}] 도로선평행 — 고른 것 {what} · 점 {edge?.Count ?? 0} · 거절 — {(chk == null ? "선 아님" : chk.Log)}"); } catch { }
                    continue;
                }
            }
            if (edge == null) continue;
            // ② 평행 거리 — ★[JACK 1119 «입력하지 않고 마우스로 띄운 만큼 눌러도 생성»] 숫자 입력 또는 위치 클릭(OFFSET «통과점»처럼 고른 선까지 직각 거리) · Enter = 마지막 값.
            //   커서를 따라 평행선이 미리 보인다(쪽은 늘 사면 쪽 — 도로 쪽을 눌러도 같은 거리로 사면 쪽).
            //   거리가 정해진 뒤 평행선을 못 만들거나 꼬이면 거리부터 다시(끝선 클릭으로 안 돌아감).
            double dist = _dist; string how = "Enter";
            RoadEdgeOffset.Off off;
            while (true)
            {
                dist = _dist; how = "Enter";
                var bdD0 = bd.Select(q => new Point3d(q.X, q.Y, 0)).ToList();
                var edD = edge.Select(q => new Point3d(q.X, q.Y, 0)).ToList();
                // 미리보기 다시 셈 = 거리가 5 cm 이상 바뀔 때만 · 지난 셈이 100 ms 넘게 걸렸으면 50 cm 이상 바뀔 때만
                double pvD = double.NaN, pvMs = 0; List<Point3> pv = new();
                double curD = double.NaN;
                void DistPath(WorldDraw wd, Point3d cur, bool has)
                {
                    wd.SubEntityTraits.Color = 4;
                    for (int i = 0; i < bdD0.Count; i++) wd.Geometry.WorldLine(bdD0[i], bdD0[(i + 1) % bdD0.Count]);
                    wd.SubEntityTraits.Color = 6;
                    for (int i = 0; i + 1 < edD.Count; i++) wd.Geometry.WorldLine(edD[i], edD[i + 1]);
                    if (!has) { curD = double.NaN; return; }
                    var cf = plan(cur);
                    var th = RoadEdgeOffset.Through(edge, cf.X, cf.Y);
                    curD = th.D;
                    if (!(th.D >= 0.01)) return;
                    if (double.IsNaN(pvD) || System.Math.Abs(th.D - pvD) >= (pvMs > 100 ? 0.5 : 0.05))
                    {
                        pvD = th.D;
                        var sw = System.Diagnostics.Stopwatch.StartNew();
                        try { pv = RoadEdgeOffset.Preview(edge, th.D, chk.Side); } catch { pv = new(); }
                        pvMs = sw.Elapsed.TotalMilliseconds;
                    }
                    wd.SubEntityTraits.Color = WallLineDraw.GuideAci;
                    for (int i = 0; i + 1 < pv.Count; i++) wd.Geometry.WorldLine(new Point3d(pv[i].X, pv[i].Y, 0), new Point3d(pv[i + 1].X, pv[i + 1].Y, 0));
                }
                var pdo = new PromptPointOptions($"\n[{tag}] 평행 거리(m) 입력 또는 위치 클릭 <{_dist.ToString("0.###", ic)}>: ")
                { AllowNone = true, AllowArbitraryInput = true, AppendKeywordsToMessage = false };
                var dr = CursorLabel.GetPoint(ed, pdo, "평행 거리 · Enter = " + _dist.ToString("0.##", ic) + " m", "", DistPath,
                    () => double.IsFinite(curD) ? "평행 " + curD.ToString("0.00", ic) + " m" : null);
                if (dr.Status == PromptStatus.Cancel || dr.Status == PromptStatus.Error) return null;
                if (dr.Status == PromptStatus.OK)
                {
                    var w0 = plan(dr.Value.TransformBy(ed.CurrentUserCoordinateSystem));
                    var th = RoadEdgeOffset.Through(edge, w0.X, w0.Y);
                    dist = System.Math.Round(th.D, 3);
                    how = th.Side == chk.Side ? "클릭" : "클릭(도로 쪽 → 사면 쪽 같은 거리)";
                }
                else if (dr.Status == PromptStatus.Keyword)
                {
                    if (!double.TryParse((dr.StringResult ?? "").Trim(), System.Globalization.NumberStyles.Float, ic, out double v)) { ed.WriteMessage("\n  숫자 또는 위치 클릭"); continue; }
                    dist = v; how = "입력";
                }
                if (!(dist > 0.01) || dist > 100) { ed.WriteMessage("\n  범위 밖(0.01~100 m)"); continue; }
                ed.WriteMessage($"\n  평행 거리 {dist.ToString("0.###", ic)} m");
                off = RoadEdgeOffset.Offset(edge, dist, band);
                if (!off.Ok)
                {
                    ed.WriteMessage($"\n  {off.Why}");
                    try { DiagLog.AppendCarry($"\n  [{tag}] 도로선평행 — 고른 것 {what} · 거리 {dist:0.###} · 거절 — {off.Log}"); } catch { }
                    continue;   // 거리 다시
                }
                break;
            }
            _dist = dist;
            // ③ 구간(Enter · A = 전체) — 평행선이 보이는 채로 · 누른 자리는 평행선 위 가장 가까운 점으로
            double mk = System.Math.Max(0.3, System.Math.Sqrt(System.Math.Pow(bd.Max(q => q.X) - bd.Min(q => q.X), 2) + System.Math.Pow(bd.Max(q => q.Y) - bd.Min(q => q.Y), 2)) * 0.006);
            var offD = off.Line.Select(q => new Point3d(q.X, q.Y, 0)).ToList();
            while (true)
            {
                double? sA = null;
                void Path(WorldDraw wd, Point3d cur, bool has)
                {
                    wd.SubEntityTraits.Color = 4;
                    for (int i = 0; i < bdD.Count; i++) wd.Geometry.WorldLine(bdD[i], bdD[(i + 1) % bdD.Count]);
                    wd.SubEntityTraits.Color = 8;
                    for (int i = 0; i < flD.Count; i++) wd.Geometry.WorldLine(flD[i], flD[(i + 1) % flD.Count]);
                    wd.SubEntityTraits.Color = WallLineDraw.GuideAci;
                    for (int i = 0; i + 1 < offD.Count; i++) wd.Geometry.WorldLine(offD[i], offD[i + 1]);
                    if (!has) return;
                    var cf = plan(cur);
                    var pj = RoadEdgeOffset.Project(off.Line, cf.X, cf.Y);
                    wd.SubEntityTraits.Color = 6;
                    if (sA is double a)
                    {
                        var part = RoadEdgeOffset.Part(off.Line, a, pj.S);
                        for (int i = 0; i + 1 < part.Count; i++) wd.Geometry.WorldLine(new Point3d(part[i].X, part[i].Y, 0), new Point3d(part[i + 1].X, part[i + 1].Y, 0));
                    }
                    var c = new[] { new Point3d(pj.X - mk, pj.Y - mk, 0), new Point3d(pj.X + mk, pj.Y - mk, 0), new Point3d(pj.X + mk, pj.Y + mk, 0), new Point3d(pj.X - mk, pj.Y + mk, 0) };
                    for (int i = 0; i < 4; i++) wd.Geometry.WorldLine(c[i], c[(i + 1) % 4]);
                    wd.Geometry.WorldLine(cf, new Point3d(pj.X, pj.Y, 0));
                }
                var p1 = new PromptPointOptions($"\n[{tag}] 구간 — 시작점 클릭 [전체(A)] <전체>: ") { AllowNone = true, AppendKeywordsToMessage = false };
                p1.Keywords.Add("A", "A", "전체(A)");
                var r1 = CursorLabel.GetPoint(ed, p1, "구간 시작 · Enter = 전체", "", Path);
                if (r1.Status == PromptStatus.Cancel || r1.Status == PromptStatus.Error) return null;
                double s0 = double.NaN, s1 = double.NaN;
                if (r1.Status == PromptStatus.OK)
                {
                    var w1 = plan(r1.Value.TransformBy(ed.CurrentUserCoordinateSystem));
                    s0 = RoadEdgeOffset.Project(off.Line, w1.X, w1.Y).S;
                    sA = s0;
                    var p2 = new PromptPointOptions($"\n[{tag}] 구간 — 끝점 클릭: ") { AllowNone = false };
                    var r2 = CursorLabel.GetPoint(ed, p2, "구간 끝", "", Path);
                    if (r2.Status != PromptStatus.OK) return null;
                    var w2 = plan(r2.Value.TransformBy(ed.CurrentUserCoordinateSystem));
                    s1 = RoadEdgeOffset.Project(off.Line, w2.X, w2.Y).S;
                    if (System.Math.Abs(s1 - s0) < 1.0) { ed.WriteMessage("\n  구간이 너무 짧음(1 m 미만) — 다시"); continue; }
                }
                var wall = RoadEdgeOffset.Build(off, bd, band, s0, s1);
                try { DiagLog.AppendCarry($"\n  [{tag}] 도로선평행 — 고른 것 {what} · 점 {edge.Count} · 거리 {dist.ToString("0.###", ic)}({how}) · {off.Log} · {(wall.Ok ? "" : "거절 — ")}{wall.Log}"); } catch { }
                SelfCheckLog.Line(tag + " — 도로선평행", wall.Ok ? '–' : '⚠', string.Format(ic, "거리 {0:0.###}({4}) · 쪽 {1} · 단순 {2} · {3}", dist, off.Side > 0 ? "왼쪽" : "오른쪽", off.Simple, wall.Ok ? wall.Log : wall.Why + " · " + wall.Log, how), doc.Name);
                if (!wall.Ok)
                {
                    ed.WriteMessage($"\n  {wall.Why}");
                    continue;   // 구간 다시(Esc = 그만)
                }
                if (wall.Cut && double.IsNaN(s0)) ed.WriteMessage("\n  평행선이 사면 띠 밖으로 나가는 자리 있음 — 가장 긴 구간만(다른 곳은 구간 지정)");
                src = per.ObjectId.Handle.ToString();   // 기록에는 고르기(S)처럼 고른 객체 핸들만
                return wall.Line;
            }
        }
    }
}
