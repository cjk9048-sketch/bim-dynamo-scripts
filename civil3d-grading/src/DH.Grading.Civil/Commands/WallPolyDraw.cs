using System.Collections.Generic;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.GraphicsInterface;
using DH.Grading.Core;
using AcadPoly = Autodesk.AutoCAD.DatabaseServices.Polyline;

namespace DH.Grading.Civil.Commands;

/// <summary>★★★[JACK 0917] <b>옹벽 폴리곤을 시점·종점 두 번으로 그린다.</b>
///
/// <para>JACK: <i>"구간 누르고 나면 <b>첫 시점</b> 먼저 선택할 수 있게 나오고 그다음 <b>종점</b> 위치에서
/// 선택하게 나와야 해. 그리고 구간 선정 시 <b>두꺼운 빨간 선</b>이 보여야 해. 그 상태에서
/// 첫 시점 가이드선도 같은 두꺼운 빨간 선으로 하고, 클릭하면 <b>선이 보이는 상태에서</b> 종점 선택할 수 있게
/// (이때도 가이드가 빨간 두꺼운 선) 나오고, 이것도 완료하면 <b>닫힌 폴리곤</b>이 생성되어야 해."</i></para>
///
/// <para><b>첫 판이 틀렸다.</b> 클릭을 여러 번 받아 데이라잇 위를 걸어 다니게 만들었는데,
/// JACK이 원한 것은 <b>딱 두 점</b>이었다 — 구간의 두 끝에서 각각 바깥으로 한 번씩.
/// 그래서 한 번 찍고 Enter를 치면 «점이 모자라다»로 끝나고 계산한 띠로 물러났다
/// (0917 로그 실측: <i>"⚠점이 모자라 폴리곤을 못 만든다(찍은 점 1개 · 최소 2개)"</i>).</para>
///
/// <code>
///      시점●─────────────●종점      ← 손으로 찍는 <b>바깥 변</b>
///         │               │
///   구간 T0●━━━━━━━━━━━━━━●T1      ← 고른 구간(안쪽 변) · 두꺼운 빨강
/// </code>
///
/// <para>★<b>도면은 그리는 동안 한 번도 안 건드린다.</b> 전부 임시 그래픽이라 Esc로 나가도 자국이 없다.</para></summary>
internal static class WallPolyDraw
{
    /// <summary>고른 구간·가이드선 색 — <b>빨강</b>(JACK 지시).</summary>
    private const short GuideAci = 1;

    /// <summary>두께 — 도형 대각선의 몇 %인가.
    /// <para><see cref="PickMark"/>가 같은 까닭으로 같은 방식을 쓴다 — 화면 배율을 알 수 없으므로
    /// 고정 두께는 작은 부지에서 도형을 덮고 큰 부지에서 실오라기가 된다.</para></summary>
    private const double WidthFrac = 0.004;
    private const double WidthMin = 0.10;

    /// <summary>기준점이 경계 위에 앉아 있을 때 <b>제자리(길이 0)</b>를 답이라 하지 않게 하는 문턱(m).</summary>
    private const double MinRun = 0.05;

    /// <summary>★[검토 0917 · 치명] 못 걷은 임시 그래픽을 <b>놓아 주지 않는 자리</b>.
    /// <para>놓아 주면 소멸자가 네이티브를 지워 관리자가 죽은 포인터를 쥔다
    /// (<see cref="PickMark"/>의 「검토 0909 · 치명」과 같은 까닭).</para></summary>
    private static readonly List<object> _stranded = new();

    /// <summary>★시점·종점을 받아 <b>닫힌 폴리곤</b>을 돌려준다(취소·실패면 <c>null</c>).</summary>
    /// <param name="dayRing">데이라잇 경계 — 두 점 다 이 안이어야 한다.</param>
    /// <param name="seg">고른 구간의 점들(안쪽 변) — 첫 점이 T0, 끝 점이 T1.</param>
    /// <param name="nStart">구간 <b>첫 끝</b>에서의 바깥쪽 법선 — 이 반대쪽으로는 못 그린다.</param>
    /// <param name="nEnd">구간 <b>끝 끝</b>에서의 바깥쪽 법선.</param>
    /// <param name="outwardSide">★[v102.2] 바깥쪽이 구간 진행의 왼쪽 +1 · 오른쪽 −1 · 모름 0(<see cref="GradingGeometry.OutwardSideOfTravel"/>) — 폴리곤을 어느 쪽으로 두르나.</param>
    internal static List<Point3>? Run(Document doc, IReadOnlyList<Point3> dayRing,
                                      IReadOnlyList<Point3> seg,
                                      (double X, double Y) nStart, (double X, double Y) nEnd, int outwardSide,
                                      out List<Point3>? wallChain, out List<bool>? isWall, out string log)
    {
        log = ""; wallChain = null; isWall = null;
        Editor ed = doc.Editor;
        if (dayRing == null || dayRing.Count < 3)
        { log = "  ⚠데이라잇 경계가 없다 — 손으로 그리기를 건너뛴다(정지면 생성을 먼저).\n"; return null; }
        if (seg == null || seg.Count < 2)
        { log = "  ⚠고른 구간의 점이 모자라다 — 손으로 그리기를 건너뛴다.\n"; return null; }

        double z = seg[0].Z;
        var end0 = new Point3d(seg[0].X, seg[0].Y, z);
        var end1 = new Point3d(seg[seg.Count - 1].X, seg[seg.Count - 1].Y, z);

        // 두께 — 데이라잇을 품는 상자의 대각선에서 뽑는다
        double w;
        {
            double mnx = double.MaxValue, mny = double.MaxValue, mxx = double.MinValue, mxy = double.MinValue;
            foreach (var q in dayRing)
            {
                mnx = System.Math.Min(mnx, q.X); mny = System.Math.Min(mny, q.Y);
                mxx = System.Math.Max(mxx, q.X); mxy = System.Math.Max(mxy, q.Y);
            }
            double diag = System.Math.Sqrt((mxx - mnx) * (mxx - mnx) + (mxy - mny) * (mxy - mny));
            w = System.Math.Max(WidthMin, diag * WidthFrac);
        }

        var tm = TransientManager.CurrentTransientManager;
        var noIds = new IntegerCollection();
        var live = new List<AcadPoly>();          // 명령이 도는 동안 살아 있는 것들
        int leak = 0;

        // ★[검토 0917 · 치명] 걷는 일은 <b>명령당 한 번</b>.
        //   못 걷었으면 <b>소멸자를 끄고 붙잡는다</b> — 놓아 주면 소멸자가 네이티브를 지운다.
        void DropAll()
        {
            foreach (var g in live)
            {
                bool erased = false;
                try { erased = tm.EraseTransient(g, noIds); } catch { erased = false; }
                if (erased) { try { g.Dispose(); } catch { } }
                else
                {
                    leak++;
                    try { System.GC.SuppressFinalize(g); } catch { }
                    lock (_stranded) _stranded.Add(g);
                }
            }
            live.Clear();
        }
        AcadPoly? Make(IReadOnlyList<Point3d> pts)
        {
            AcadPoly? g = null;
            try
            {
                g = new AcadPoly();
                for (int i = 0; i < pts.Count; i++)
                    g.AddVertexAt(i, new Point2d(pts[i].X, pts[i].Y), 0, w, w);
                g.Elevation = z; g.ColorIndex = GuideAci; g.ConstantWidth = w;
                bool added = false;
                try { added = tm.AddTransient(g, TransientDrawingMode.DirectShortTerm, 128, noIds); }
                catch { added = false; }
                if (!added) { try { g.Dispose(); } catch { } return null; }
                live.Add(g);
                return g;
            }
            catch { try { g?.Dispose(); } catch { } return null; }
        }

        var sb = new System.Text.StringBuilder();
        Point3? pA = null, pB = null;
        Point3? dayA = null, dayB = null;      // 데이라잇에 닿은 자리(여유를 안 더한 것)
        (double X, double Y) dirA = (0, 0), dirB = (0, 0);   // ★[v102.2] 찍은 방향(단위) — 측선이 이 방향 그대로 여유 테두리까지 간다
        try
        {
            // ── ①고른 구간을 <b>두꺼운 빨강</b>으로 — 명령이 끝날 때까지 남는다 ──
            {
                var segPts = new List<Point3d>();
                foreach (var q in seg) segPts.Add(new Point3d(q.X, q.Y, z));
                if (Make(segPts) == null)
                    sb.Append("\n    ⚠고른 구간을 <b>빨갛게 못 그렸다</b>(임시 그래픽 실패) — 그리기는 계속한다");
            }

            // ── ②시점 · ③종점 — 각각 구간의 <b>제 끝</b>에서 바깥으로 ──
            for (int step = 0; step < 2; step++)
            {
                // ★★★[JACK 0917] <b>시점 → 종점</b> 차례다.
                //   <para>JACK: <i>"순서가 좀 잘못된 게 구간 그리고 <b>종점 시점</b> 순으로 선을 그리게 돼 있는데
                //   구간 그리고 <b>시점 종점</b> 순으로 바꿔줘."</i></para>
                //   <para>화면에서 먼저 나오던 끝이 JACK 눈에는 <b>종점</b>이었다 —
                //   그래서 <b>반대 끝에서 시작</b>한다(<c>end1</c> → <c>end0</c>).</para>
                var from = step == 0 ? end1 : end0;
                var nrm = step == 0 ? nEnd : nStart;      // ★그 끝의 바깥쪽 법선
                string nm = step == 0 ? "시점" : "종점";
                AcadPoly? guide = null;
                PointMonitorEventHandler? mon = null;
                try
                {
                    mon = (s, e) =>
                    {
                        try
                        {
                            var c = e.Context.ComputedPoint;
                            // ★★★[JACK 0917] <b>커서가 안에 있어도 언제나 데이라잇까지</b> 뻗는다.
                            //   <para>JACK: <i>"데이라잇 구간 안에 커서가 있더라도 그냥 커서 방향으로
                            //   <b>데이라잇에 항상 닿게</b> 해줘. 옹벽 변환 구간인데 괜히 데이라잇보다
                            //   짧게 구간이 잡혀 버리면 나중에 저 폴리곤으로 옹벽 칠 건데 이상하게 되잖아."</i></para>
                            //   <para>종전엔 커서가 안이면 <b>커서 자리에서 멈췄다</b> — 커서는 방향만 주면 된다.</para>
                            // ★노선 <b>안쪽</b>을 향하면 보여 줄 선이 없다(JACK: ㄷ자 안뜰로 못 들어오게)
                            if (!GradingGeometry.RayRingHitOutward(dayRing, from.X, from.Y,
                                    c.X - from.X, c.Y - from.Y, nrm.X, nrm.Y,
                                    out double gx, out double gy, out _, MinRun))
                            { gx = from.X; gy = from.Y; }      // 길이 0으로 접는다
                            if (guide == null)
                                guide = Make(new List<Point3d> { from, new Point3d(gx, gy, z) });
                            else
                            {
                                guide.SetPointAt(0, new Point2d(from.X, from.Y));
                                guide.SetPointAt(1, new Point2d(gx, gy));
                                try { tm.UpdateTransient(guide, noIds); } catch { }
                            }
                        }
                        catch { }
                    };
                    ed.PointMonitor += mon;

                    var opt = new PromptPointOptions(
                        $"\n[옹벽 폴리곤] {nm}을 찍으세요 — 데이라잇 안이어야 합니다 (Esc=취소): ")
                    { AllowNone = false, UseBasePoint = true, BasePoint = from };
                    var r = ed.GetPoint(opt);
                    if (r.Status != PromptStatus.OK)
                    { log = $"  <b>취소</b>했다({nm}에서 Esc).\n"; return null; }

                    var p = r.Value;
                    // ★클릭도 <b>방향만</b> 준다 — 끝점은 언제나 데이라잇 위다.
                    if (!GradingGeometry.RayRingHitOutward(dayRing, from.X, from.Y,
                            p.X - from.X, p.Y - from.Y, nrm.X, nrm.Y,
                            out double fx, out double fy, out double run, MinRun))
                    {
                        // 안쪽을 향했는지 · 데이라잇에 안 닿는지를 갈라 알린다
                        double dd = (p.X - from.X) * nrm.X + (p.Y - from.Y) * nrm.Y;
                        ed.WriteMessage(dd <= 0
                            ? "\n  구간 노선 안쪽으로는 못 그립니다 — 노선 바깥(사면 쪽)을 찍으세요."
                            : "\n  그 방향으로는 데이라잇에 닿지 않습니다 — 부지 쪽을 찍으세요.");
                        step--;                                // 같은 점을 다시 받는다
                        continue;
                    }
                    // ★★★[JACK 0917] <b>빨간 선은 데이라잇까지, 폴리곤은 10m 더.</b>
                    //   <para>JACK: <i>"빨간선으로 나올 땐 데이라잇까지만이지만 실제 영역 폴리곤이
                    //   만들어질 때는 <b>10m씩 여유있게 데이라잇 바깥으로 연장</b>해서 닫아줘."</i></para>
                    //   <para>여유 값은 <see cref="GradingSettings.WallPolygonMargin"/>과 <b>같은 것</b>을 쓴다 —
                    //   계산한 띠와 손으로 그린 것이 <b>서로 다른 자</b>를 쓰면 안 된다(§65).</para>
                    double ux = (fx - from.X) / (run < 1e-9 ? 1 : run), uy = (fy - from.Y) / (run < 1e-9 ? 1 : run);
                    double mg = System.Math.Max(0, GradingSettings.WallPolygonMargin);
                    var got = new Point3(fx + ux * mg, fy + uy * mg, z);
                    if (step == 0) { pA = got; dayA = new Point3(fx, fy, z); dirA = (ux, uy); }
                    else { pB = got; dayB = new Point3(fx, fy, z); dirB = (ux, uy); }
                    sb.Append($"\n      {nm} — 데이라잇 ({fx:F1},{fy:F1}) · 구간 끝에서 {run:F1}m · 찍은 방향 ({ux:F3},{uy:F3})"
                            + " → 측선은 이 방향 그대로 «데이라잇 + 여유» 테두리까지(모서리는 아래 조립 줄)");

                    // ★찍은 선은 <b>그대로 둔다</b> — JACK: "클릭하면 선이 보이는 상태에서 종점 선택".
                    //   <c>live</c>에 남아 있으므로 명령이 끝날 때 한꺼번에 걷힌다.
                    if (guide != null)
                    {
                        // ★남기는 선은 <b>데이라잇까지</b>다 — 여유 10m는 폴리곤에만 들어간다.
                        try { guide.SetPointAt(1, new Point2d(fx, fy)); tm.UpdateTransient(guide, noIds); }
                        catch { }
                    }
                    guide = null;                              // 다음 걸음은 새 가이드를 만든다
                }
                finally { if (mon != null) try { ed.PointMonitor -= mon; } catch { } }
            }
        }
        finally { DropAll(); }

        if (pA == null || pB == null) { log = "  ⚠두 점을 다 못 받았다.\n"; return null; }

        // ── ④닫는다 — 구간(안쪽 변) → 시점 측선 → 바깥 변 → 종점 측선 ──
        // ══ ★★★[JACK 0917 스샷 <i>"이런 경우 폴리곤이 데이라잇 안에 들어와 버려"</i>] ═══════
        //
        //   <para>시점·종점을 데이라잇 밖에 잡아도 <b>그 둘을 이은 직선</b>은,
        //   데이라잇이 가운데서 부풀어 있으면 <b>안으로 파고든다</b>.
        //   그러면 폴리곤이 데이라잇 띠를 다 못 품고 그 자리에 옹벽이 모자란다.</para>
        //
        //   <para>→ 바깥 변을 <b>직선이 아니라 «데이라잇 + 여유» 테두리를 따라</b> 두른다(S134 · 이음매 없이 한 줄).</para>
        double mgF = System.Math.Max(0, GradingSettings.WallPolygonMargin);

        // ★★★[JACK 0918] <b>측선 점을 링에도 넣는다.</b> 종전엔 링이 «선택구간 + 폐합면»뿐이라
        //   측선이 <b>변 하나</b>였다 — 그러면 줄이 그 변을 따라갈 수가 없다.
        //   같이 <b>점마다 옹벽인지</b> 표를 만든다: 선택구간·측선 = 옹벽 · 폐합면 = 수직.
        double spR = 1.0;
        {
            double Ls = 0;
            for (int i = 0; i + 1 < seg.Count; i++)
                Ls += System.Math.Sqrt((seg[i + 1].X - seg[i].X) * (seg[i + 1].X - seg[i].X)
                                     + (seg[i + 1].Y - seg[i].Y) * (seg[i + 1].Y - seg[i].Y));
            if (seg.Count >= 2) spR = System.Math.Max(0.5, Ls / System.Math.Max(1, seg.Count - 1));
        }
        // ★★[v102.2 · JACK 0930 «끝선을 꺾지 않고 연장선처럼 뺐는데 강제로 꺾여서 옹벽이 생성됐다»] 조립은 Core(WallInPoly.AssembleManual · 하네스 S146이 같은 함수를 잰다) —
        //   측선 = 구간 끝 → <b>찍은 방향 그대로</b> «데이라잇 + 여유» 테두리를 처음 빠져나가는 점 · 두 점 사이는 그 테두리를 따라(폐합면 · 수직).
        //   종전(0917 S134)은 두른 선의 끝 점(데이라잇에 닿은 점에서 테두리의 가장 가까운 꼭짓점)을 모서리로 써 측선이 찍은 방향에서 틀어졌다(0930 16:58 — 7.0° · 7.8°).
        //   두르는 쪽은 폴리곤이 구간의 <b>바깥쪽</b>(자 고리가 도는 방향 — outwardSide)에 서는 갈래(코드 검토 중간 1 — 좁은 부지에서 한 바퀴 돌던 자리).
        //   안 되면(테두리 못 만듦 · 광선 안 닿음 · 제 몸 지름 · 넓이) 종전 모서리로 물러나고 까닭과 틀어진 각을 적는다 · 그것도 안 되면 null(계산한 띠로).
        var ring = WallInPoly.AssembleManual(seg, dirA, dirB, outwardSide, dayA ?? pA.Value, dayB ?? pB.Value, dayRing, mgF, spR, z,
                                             out var flag, out bool usedRay, out string asmNote)
                   ?? new List<Point3>();
        if (ring.Count == 0) flag = new List<bool>();               // ★[코드 검토 낮음 10] 물러난 링도 못 쓰면 표도 비운다(«수직 −N» 안 찍게)
        sb.Append("\n      " + asmNote);
        if (!usedRay || asmNote.Contains("⚠"))
            try { ed.WriteMessage("\n[옹벽 폴리곤] " + System.Text.RegularExpressions.Regex.Replace(asmNote, "<[^>]+>", "")); } catch { }
        bool simple = ring.Count >= 3 && GradingGeometry.RingIsSimple(ring);
        double area = ring.Count >= 3 ? GradingGeometry.RingAreaNts(ring) : 0;
        sb.Insert(0, $"  ★<b>손으로 그린 폴리곤</b> — 구간 {seg.Count}점 + 시점·종점 2점"
            + $" → 꼭짓점 {ring.Count}개 · 넓이 <b>{area:F1}㎡</b>"
            + $"(데이라잇 바깥으로 <b>{System.Math.Max(0, GradingSettings.WallPolygonMargin):0.#}m</b> 여유)"
            + (leak > 0 ? $" · <b>⚠미리보기를 못 걷은 횟수 {leak}</b>(해제하지 않고 붙잡아 두었다)" : "")
            + $" · 제 몸을 지르지 않는가 <b>{(simple ? "예" : "아니오")}</b>");
        {
            int nW = 0; foreach (var bb in flag) if (bb) nW++;
            wallChain = new List<Point3>(ring); isWall = new List<bool>(flag);
            sb.Append($"\n      변 나누기 — 점 {ring.Count}개 중 <b>옹벽 {nW}</b>"
                    + $"(선택구간 {seg.Count} + 측선 {System.Math.Max(0, nW - seg.Count)})"
                    + $" · <b>수직 {ring.Count - nW}</b>(폐합면) — 폐합면은 제자리에서 <b>수직</b>으로 오른다");
        }
        sb.Append("\n");
        log = sb.ToString();
        // ★제 몸을 지르면 <b>안 내보낸다</b> — 넓이만 보면 못 가린다(하네스 S128: 나비 신발끈 0.0 / NTS 450.0㎡).
        if (!simple || area < 1.0) return null;
        return ring;
    }
}
