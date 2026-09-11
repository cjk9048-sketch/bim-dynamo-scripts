using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;   // ★[JACK 0910] 커서 자리(Point3d)를 받으려고 — PickOnRing
using DH.Grading.Core;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace DH.Grading.Civil.Commands;

/// <summary>
/// [변환 공통 0804 — JACK] 옹벽 변환(DHWALL)·사면 변환(DHSLOPE)의 공통 흐름.
/// 규칙(JACK 확정):
///  · **1회 실행 = 1개만 바뀐다.** 선을 연달아 눌러도 마지막에 누른 것만 선택된다.
///  · 제원은 <b>프롬프트 키워드</b>로 바꾸고 Enter를 치면 그대로 적용된다(JACK 0820) —
///      옹벽: 단높이(H) · 소단길이(T)              (구배는 수직 = 구배 하한 고정)
///      사면: 단높이(H) · 사면구배(R) · 소단길이(T)
///    기본값 = **최초 정지옵션에서 준 값**(클릭한 방향의 값을 따라간다 — JACK 0820)
///    ※ 단높이는 <b>구간이 아니라 방향(절토/성토) 전체</b>에 적용된다 —
///      링은 같은 표고의 등고선이라 둘레의 일부만 단높이를 바꿀 수 없다(v16.9).
///      층 전체를 바꾸는 것은 링마다 표고가 여전히 하나라 안전하다(JACK 0820).
///  · 입력값은 **클릭한 단부터 바깥 끝까지** 적용된다. 여러 번 실행하면 규칙이 쌓여
///    '아래는 급하게 · 위는 완만하게'가 된다.
/// 두 명령은 묻는 항목만 다르고 나머지(선 작도·선택·구간 병합·재생성)는 전부 같다.
/// </summary>
internal static class ZoneEditCommon
{
    private const short SelAci = 2;   // 선택 = 노랑(대상선 시안과 구분)

    /// <summary>부분 구간의 <b>최소 길이(m)</b> — <see cref="GradingGeometry.MinPartSpan"/>가 정한다.
    /// <para>★[검토 0910 · 보통4] 종전엔 여기 <c>3.0</c> 리터럴이 있었다. 그런데 그 값이 성립하는
    /// 근거(조밀화 간격 <c>dens</c> · <c>CollectRuns</c>의 <c>cur.Count &gt;= 2</c>)는 전부 Core에 있어서,
    /// 누가 그 둘을 손대면 <b>이 상수가 조용히 틀린 값</b>이 됐다. 이제 <b>같은 함수</b>를
    /// 명령도 검사기도 부른다.</para></summary>
    private static double MinPartOf() => GradingGeometry.MinPartSpan(GradingSettings.ToParams());

    /// <summary>빨간 띠의 <b>반폭(m)</b> — 고리 둘레 하나로 정한다.
    /// <para>★[검토 0910 · 보통3] 커서를 따라가는 띠와 확정된 띠가 <b>서로 다른 식</b>을 쓰고 있었다
    /// (하나는 고리 둘레, 하나는 그 조각의 바운딩박스). 그래서 조각이 짧으면 확정 순간
    /// 띠가 <b>홀쭉해졌다</b>. 식을 하나로 모은다 — 부지가 커도 선을 안 덮고, 조각이 짧아도 보인다.</para></summary>
    private static double BandHalf(double ringLen)
        => System.Math.Max(0.35, System.Math.Min(ringLen * 0.004, 2.0));

    /// <summary>커서를 따라 그리는 표식의 색 — 고른 선(노랑)·대상선(시안)과 <b>구분되는</b> 빨강.</summary>
    private const short GlyphAci = 1;

    /// <summary>커서 표식이 <b>한 번이라도</b> 자빠졌나 — 그 사실을 로그에 한 번만 남기려는 걸쇠.
    /// <para>★[검토 0910 · 보통5] 이 자리는 마우스가 움직이는 내내 도는 곳이라
    /// 실패를 매번 적으면 로그가 못 쓰게 된다. 그렇다고 <c>catch { }</c>로 삼키면
    /// "안 보인다"의 원인을 영영 못 찾는다. 그래서 <b>처음 한 번만</b> 적는다.</para></summary>
    private static bool _glyphLogged;

    /// <summary>★[JACK 0820 '정지옵션과 변환은 연동되긴 해야 해'] 변환 기본값 = <b>지금 정지옵션에 있는 값</b>.
    /// <para>고정 숫자도, 번들 저장값도 아니다 — 사용자가 정지옵션에서 방금 바꾼 값이 그대로 기본값이 된다.
    /// 절토·성토는 단높이·소단폭·구배가 따로이므로(v16.6) <b>클릭한 방향</b>의 값을 따라간다.</para>
    /// 단높이는 <b>그 단에 실제로 적용 중인 값</b>을 준다(규칙이 쌓여 있으면 그 값) —
    /// "지금 몇 m인가"가 기본값이어야 바꿀지 말지를 판단할 수 있다.</summary>
    private static (double H, double N, double W) DefaultsFor(bool up, int bench)
    {
        var p = GradingSettings.ToParams();
        return (p.BenchHeightAt(up, bench), BaseSlopeOf(p, up), p.BenchWidthOf(up));
    }

    // ══ ★★★[JACK 0910] 형상선 위에서 찍기 — <b>Civil 3D 정지와 같은 방식</b> ══════════
    //
    //   <para>JACK 스샷(0910 13:35)이 보여 준 것: <c>CREATEGRADING 시작점 선택:</c> 에서
    //   커서가 형상선에 붙고 <b>ㄴ자 표식 + 방향 화살표</b>가 따라오며 툴팁에
    //   <i>「측점:0+123.05m, 표고:93.000m」</i>이 뜬다. <b>자리와 방향을 한 번에</b> 보여 주는 것이다.</para>
    //
    //   <para>AutoCAD에는 <b>커서 움직임을 듣는 문</b>(<c>Editor.PointMonitor</c>)이 있고,
    //   그 자리에서 툴팁에 글자를 붙일 수 있다(<c>AppendToolTipText</c>).
    //   표시는 이 저장소가 이미 쓰는 <b>임시 그래픽</b>으로 그린다 — 도면에는 아무것도 안 남는다.</para>

    /// <summary>고리 위 <paramref name="t"/> 자리의 <b>바깥쪽</b> 방향(길이 <paramref name="len"/>)의 점.
    /// <para>바깥 = 고리 <b>무게중심의 반대쪽</b>. 사면은 언제나 바깥으로 나므로 그쪽을 가리킨다.</para></summary>
    //   ★[검토 0910] 셈은 <see cref="GradingGeometry.OutwardAt"/>에 있다 — 무게중심 방식이
    //     오목한 부지에서 <b>안쪽을 바깥이라고</b> 가리켜서 폴리곤이 도는 방향으로 고쳤고,
    //     오프라인 검사기가 닿게 Core로 내렸다.

    // ══ ★★★[JACK 0910 두 번째 지시] <b>커서 표시를 다시 만든다</b> ═══════════════════
    //
    //   <para>JACK: <i>"커서가 L자 모양으로 나오고 거기서 떨어져서 그으면 점선이 마우스를 클릭한
    //   지점에서 그려져서 엄청 헷갈려. L자 모양도 직관적이지 않고. … 처음 찍고 그 좌표를 기준으로
    //   상하좌우를 인식하고 노선방향쪽으로 화살표가 실시간으로 보이게 해. 그리고 그 방향으로
    //   두꺼운 빨간선이 쭉 생겼다가 마지막 위치 찍으면 진행되게."</i></para>
    //
    //   <para><b>무엇이 헷갈렸나.</b> ①ㄴ자는 "무엇을 뜻하는 표시인지" 안 보였다.
    //   ②두 번째 점을 <c>UseBasePoint</c>로 받아서 AutoCAD가 <b>찍은 자리</b>에서 고무줄 점선을 그렸다 —
    //   그런데 실제로 잡히는 자리는 <b>계단선 위에 투영된 점</b>이라, 점선과 결과가 서로 딴 데를 가리켰다.</para>
    //
    //   <para>→ ①고무줄 점선을 없애고 ②시작점은 <b>계단선을 가로지르는 짧고 굵은 표시</b>로,
    //   ③끝점을 찍는 동안에는 <b>시작점부터 커서까지 계단선을 따라 굵은 빨간 띠</b>를 그리고
    //   그 끝에 <b>진행 방향 화살표</b>를 얹는다. 보이는 것이 곧 잡히는 것이 된다.</para>

    /// <summary>화면에 얹을 <b>굵은 띠</b> — 선을 따라 사각형을 이어 칠한다.
    /// <para>임시 그래픽이 아니라 <c>ViewportDraw</c>라 <b>이 프레임에만</b> 그려지고 저절로 사라진다.</para></summary>
    private static void DrawBand(Autodesk.AutoCAD.GraphicsInterface.ViewportDraw dc,
                                 System.Collections.Generic.IReadOnlyList<Point3> path, double half)
    {
        if (dc == null || path == null || path.Count < 2) return;
        // 점이 아주 많으면 몇 개 건너뛰어 그린다 — 굵은 띠라 눈에는 똑같고, 한 프레임 안에 끝나야 한다.
        int step = path.Count > 400 ? path.Count / 400 + 1 : 1;
        // ★★★[검토 0910 · 높음2] <b>담는 그릇은 하나만 만들어 다시 쓴다.</b>
        //   <c>Point3dCollection</c>은 네이티브 배열을 쥔 <c>DisposableWrapper</c>다.
        //   여기는 <c>PointMonitor</c> 안이라 <b>마우스가 움직이는 내내</b> 돈다 —
        //   띠 한 줄에 최대 400개씩, 초당 30~60프레임이면 <b>초당 1~2만 개</b>가
        //   소멸자 대기줄에 쌓인다. 하나를 <c>Clear()</c>해 가며 쓰면 프레임당 <b>하나</b>다.
        using var quad = new Point3dCollection();
        for (int i = 0; i + step < path.Count; i += step)
        {
            var a = path[i]; var b = path[System.Math.Min(i + step, path.Count - 1)];
            double dx = b.X - a.X, dy = b.Y - a.Y;
            double L = System.Math.Sqrt(dx * dx + dy * dy);
            if (L < 1e-9) continue;
            double nx = -dy / L * half, ny = dx / L * half;
            quad.Clear();
            quad.Add(new Point3d(a.X - nx, a.Y - ny, a.Z));
            quad.Add(new Point3d(a.X + nx, a.Y + ny, a.Z));
            quad.Add(new Point3d(b.X + nx, b.Y + ny, b.Z));
            quad.Add(new Point3d(b.X - nx, b.Y - ny, b.Z));
            try { dc.Geometry.Polygon(quad); } catch { }
        }
    }

    /// <summary>진행 방향 <b>화살촉</b> — 띠 끝에 얹어 "이쪽으로 간다"를 보여 준다.</summary>
    private static void DrawArrow(Autodesk.AutoCAD.GraphicsInterface.ViewportDraw dc,
                                  Point3 tip, double dirX, double dirY, double size)
    {
        if (dc == null) return;
        double L = System.Math.Sqrt(dirX * dirX + dirY * dirY);
        if (L < 1e-9) return;
        double ux = dirX / L, uy = dirY / L;         // 진행 방향
        double px = -uy, py = ux;                     // 그 직각
        // ★[검토 0910 · 높음2] 여기도 <c>PointMonitor</c> 안이다 — 쥔 것을 <b>반드시 버린다</b>.
        using var tri = new Point3dCollection
        {
            new Point3d(tip.X + ux * size, tip.Y + uy * size, tip.Z),
            new Point3d(tip.X - ux * size * 0.4 + px * size * 0.55,
                        tip.Y - uy * size * 0.4 + py * size * 0.55, tip.Z),
            new Point3d(tip.X - ux * size * 0.4 - px * size * 0.55,
                        tip.Y - uy * size * 0.4 - py * size * 0.55, tip.Z),
        };
        try { dc.Geometry.Polygon(tri); } catch { }
    }

    /// <summary>★★★[JACK 0910] <b>형상선 위에서 한 점을 찍는다</b> — 커서를 따라 자리·표고·방향을 보여 준다.
    ///
    /// <para>찍은 점을 그냥 받는 것과 다르다: 커서가 움직이는 <b>동안</b> 그 자리를 고리에 투영해
    /// ①표식을 그리고 ②툴팁에 둘레 위치와 표고를 적는다. 그래서 <b>클릭하기 전에</b>
    /// 어디가 잡히는지 보인다 — 스샷의 Civil 3D가 하는 그것이다.</para>
    ///
    /// <para>★<b>구간이 정해져 있으면 그 안으로 가둔 자리</b>를 보여 준다 —
    /// 밖을 가리키면 표식이 구간 끝에 붙어 <b>더는 못 간다</b>는 것이 눈에 보인다.</para>
    ///
    /// <para>★★<paramref name="fromT"/>가 있으면 <b>그 자리부터 커서까지</b> 계단선을 따라
    /// <b>굵은 빨간 띠</b>를 그리고 그 끝에 <b>진행 방향 화살촉</b>을 얹는다 —
    /// 어느 쪽으로 얼마나 잡힐지가 <b>클릭하기 전에</b> 보인다(JACK 0910
    /// <i>"그 좌표를 기준으로 상하좌우를 인식하고 노선방향쪽으로 화살표가 실시간으로"</i>).
    /// 없으면(시작점) 계단선을 <b>가로지르는 짧고 굵은 표시</b> 하나만 그린다.</para>
    ///
    /// <para>★그리는 자리는 <c>InputPointContext.DrawContext</c> — <b>이 프레임에만</b> 얹히고
    /// 저절로 사라져 걷을 것도, 화면을 갱신할 일도 없다. 그래서 <b>떨림도 없다</b>.</para></summary>
    private static PromptPointResult PickOnRing(
        Editor ed, string msg, double? fromT,
        System.Collections.Generic.IReadOnlyList<Point3> ring, double[] cum,
        double f0, double f1, bool whole, out double t)
    {
        double total = cum[cum.Length - 1];
        // 띠 굵기·화살촉 크기 — 부지 크기에 따라 눈에 보이게(너무 얇으면 안 보이고 너무 굵으면 선을 덮는다).
        double half = BandHalf(total);
        double arrow = System.Math.Max(1.5, System.Math.Min(total * 0.02, 8.0));
        double minPart = MinPartOf();

        void OnMove(object? s, PointMonitorEventArgs e)
        {
            try
            {
                // ★[검토 0910] <b>지금 그 점이 뜻이 있는가</b>부터 묻는다 — 좌표를 타이핑하는 중에도
                //   이 이벤트가 돈다(<c>PointHistoryBits.CoordinatePending</c>).
                if (!e.Context.PointComputed) return;
                var p = e.Context.ComputedPoint;
                double tt = GradingGeometry.ParamAt(ring, cum, p.X, p.Y);
                if (!whole) tt = GradingGeometry.ClampInto(f0, f1, tt, total, out _);
                var at = GradingGeometry.PointAtParam(ring, cum, tt);

                // ★★★[검토 0910 · 높음1] <b>AutoCAD가 이 자리에 그리라고 준 문으로 그린다.</b>
                //   <c>InputPointContext.DrawContext</c>는 <b>지금 그리는 중인 화면</b>에 바로 얹는 자리다 —
                //   프레임이 끝나면 저절로 사라져 걷을 것이 없고, 화면을 따로 갱신할 필요도 없다.
                //   (첫 판은 임시 그래픽을 넣었다 뺐다 하며 화면을 강제 갱신했는데, 그 갱신이
                //    이 이벤트를 다시 부르면 손잡이를 덮어써 <b>죽은 포인터</b>가 된다.)
                var dc = e.Context.DrawContext;
                if (dc != null) { try { dc.SubEntityTraits.Color = GlyphAci; } catch { } }

                if (fromT == null)
                {
                    // ── 시작점 — <b>계단선을 가로지르는 짧고 굵은 표시</b>. "여기"만 뜻한다.
                    e.AppendToolTipText($"시작점 · 둘레 {tt:0.00}m · 표고 {at.Z:0.000}m");
                    if (dc == null) return;
                    var o = GradingGeometry.OutwardAt(ring, cum, tt, arrow * 0.5);
                    var i2 = GradingGeometry.OutwardAt(ring, cum, tt, -arrow * 0.5);
                    DrawBand(dc, new System.Collections.Generic.List<Point3> { i2, o }, half);
                    return;
                }

                // ── 끝점 — <b>시작점부터 커서까지 계단선을 따라 굵은 빨간 띠</b> + 진행 방향 화살촉.
                // ★[검토 0910 · 보통1] 시작점은 <b>날것</b>으로 넘어온다(그래야 나중에 "밖을 찍었다"를 말할 수 있다).
                //   화면에 그릴 때는 커서와 <b>같은 자로</b> 가둔다 — 안 그러면 띠가 구간 밖에서 시작한다.
                double a0 = fromT.Value;
                if (!whole) a0 = GradingGeometry.ClampInto(f0, f1, a0, total, out _);
                double fwd = tt >= a0 ? tt - a0 : total - a0 + tt;      // 앞으로 가는 길이
                double bwd = total - fwd;                                // 뒤로 가는 길이
                bool goFwd;
                if (whole)
                {
                    // 한 바퀴면 두 점 사이 호가 <b>둘</b> — <b>짧은 쪽</b>이 기본이다.
                    goFwd = fwd <= bwd;
                    // ★적용하는 손(<c>GradingGeometry.PartInterval</c>)과 <b>같은 규칙</b>을 써야
                    //   보이는 것과 잡히는 것이 갈라지지 않는다 —
                    //   짧은 쪽이 최소 길이에 못 미치면 그쪽은 애초에 못 쓰므로 <b>반대쪽</b>으로 넘어간다.
                    double ch = goFwd ? fwd : bwd, ot = goFwd ? bwd : fwd;
                    if (ch < minPart && ot >= minPart) goFwd = !goFwd;
                }
                else
                {
                    // 조각 구간이면 <b>구간이 흐르는 방향</b>으로 잰다 — 답이 하나뿐인 그 방향.
                    double Rel(double x) { double r2 = x - f0; return r2 >= 0 ? r2 : total + r2; }
                    goFwd = Rel(a0) <= Rel(tt);
                }
                double s0 = goFwd ? a0 : tt, s1 = goFwd ? tt : a0;
                double span = goFwd ? fwd : bwd;
                e.AppendToolTipText($"구간 {span:0.00}m · 둘레 {tt:0.00}m · 표고 {at.Z:0.000}m");
                if (dc == null || span < 1e-6) return;

                var path = GradingGeometry.SubPath(ring, cum, s0, s1);
                if (path.Count < 2) return;
                DrawBand(dc, path, half);

                // 화살촉은 <b>커서 쪽 끝</b>에 — 지금 어디로 늘고 있는지가 그것이다.
                var tip = at;
                var near = GradingGeometry.PointAtParam(ring, cum, tt + (goFwd ? -1.0 : 1.0));
                DrawArrow(dc, tip, tip.X - near.X, tip.Y - near.Y, arrow);
            }
            // ★★[검토 0910 · 보통5] <b>전건 침묵이었다.</b> <c>PointMonitor</c>·<c>AppendToolTipText</c>·
            //   <c>ViewportDraw</c>는 이 저장소에서 <b>여기서만</b> 쓴다 — 처음 쓰는 문이다.
            //   그런데 예외가 나면 아무 자국 없이 <b>띠만 안 그려진다</b>. JACK이 "띠가 안 보여"라고 하면
            //   원인을 알 길이 없다("기하 버그는 계측부터"를 못 지키는 자리).
            //   ★프레임마다 적을 수는 없으니 <b>한 번만 적는 걸쇠</b>를 둔다.
            catch (System.Exception gex)
            {
                if (!_glyphLogged) { _glyphLogged = true; Log("   ⚠ 커서 표식 실패 — " + gex.Message); }
            }
        }

        // ★[JACK 0910] <b>고무줄 점선을 안 쓴다</b>(<c>UseBasePoint</c>를 안 건다) —
        //   그 점선은 <b>찍은 자리</b>에서 나오는데 실제로 잡히는 것은 <b>계단선에 투영된 점</b>이라
        //   둘이 서로 딴 데를 가리켜 헷갈린다(JACK 0910). 보여 줄 것은 위의 띠 하나면 된다.
        var opt = new PromptPointOptions(msg) { AllowNone = false };
        bool hooked = false;
        try { ed.PointMonitor += OnMove; hooked = true; } catch { }
        PromptPointResult r;
        // ★어느 길로 빠져나가든 <b>듣기를 끊는다</b> — 안 그러면 커서가 다른 명령에서도 우리 것을 끌고 다닌다.
        try { r = ed.GetPoint(opt); }
        finally { if (hooked) { try { ed.PointMonitor -= OnMove; } catch { } } }

        // ★★[검토 0910 · 높음2] <b>찍힌 그 점을 다시 잰다</b> — 커서 값을 쓰지 않는다.
        //   스냅으로 잡거나 좌표를 쳐 넣으면 찍힌 자리와 커서가 있던 자리가 다르다.
        //
        // ★★★[검토 0910 · 보통1] <b>여기서 가두지 않는다 — 날것으로 돌려준다.</b>
        //   <para>종전엔 여기서 <c>ClampInto</c>를 걸어 돌려줬다. 그러면 <c>PartInterval</c>이 받는 값은
        //   <b>이미 구간 안</b>이라 가둘 것이 없고, <c>moved</c>가 <b>언제나 0</b>이 된다
        //   (독립 계측 2만 건: <c>moved&gt;0</c> 0건). 그래서 구간 밖을 찍은 사람에게
        //   <i>"구간 밖을 찍어 끝으로 붙였습니다"</i> 대신 <i>"두 점이 너무 가깝습니다"</i>가 떴다 —
        //   <b>원인을 잘못 짚어 주는</b> 안내이고, §78이 값을 치른 바로 그 실수다.</para>
        //   <para>★화면은 <b>그대로 가둔 자리</b>에 그린다(위 <c>OnMove</c>) — 표식이 구간 끝에 붙어
        //   "더는 못 간다"가 눈에 보이는 것이 맞다. 가두는 셈은 순수 함수라
        //   <c>PartInterval</c>이 다시 걸어도 <b>같은 자리</b>가 나온다. 보이는 것과 잡히는 것은 같고,
        //   <b>왜 그리 됐는지만</b> 이제 말할 수 있게 된다.</para>
        t = 0;
        if (r.Status == PromptStatus.OK) t = GradingGeometry.ParamAt(ring, cum, r.Value.X, r.Value.Y);
        return r;
    }

    /// <summary>취소 시 일반(무태그) 옹벽선 복원용 좌표 — 진입 때 레이어를 비우므로 종료 때 되돌린다.</summary>
    /// <summary>노리선이 이미 그려져 있나 — <b>그 레이어에 객체가 하나라도 있으면</b> 그렇다.
    /// <para>번들이나 설정이 아니라 <b>도면에 실제로 있는 것</b>으로 판단한다 —
    /// 사용자가 손으로 지웠으면 다시 그리지 않는 편이 맞다.</para></summary>
    private static bool HasNoriLines(Database db)
    {
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);
            foreach (ObjectId id in ms)
            {
                try
                {
                    if (tr.GetObject(id, OpenMode.ForRead) is Entity e &&
                        string.Equals(e.Layer, "DH-노리선", System.StringComparison.OrdinalIgnoreCase))
                    { tr.Commit(); return true; }
                }
                catch { }
            }
            tr.Commit();
        }
        catch { }
        return false;
    }

    private static System.Collections.Generic.List<System.Collections.Generic.List<Point3>>? _restoreLines;

    public static void Run(Document doc, bool wallMode)
    {
        string cmdLabel = wallMode ? "옹벽 변환" : "사면 변환";
        Editor ed = doc.Editor;
        Database db = doc.Database;
        string app = GradingSettings.WallPickAppName;

        GradingBundle? region = null;
        string activePlan = GradingSettings.LastPlanHandle;
        var madeIds = new System.Collections.Generic.List<ObjectId>();
        var info = new System.Collections.Generic.Dictionary<ObjectId, (bool up, int bench, int gid)>();
        var groups = new System.Collections.Generic.Dictionary<(bool up, int gid, int bench),
            System.Collections.Generic.List<ObjectId>>();
        var lineArc = new System.Collections.Generic.Dictionary<(bool up, int gid, int bench), (double T0, double T1)>();
        // ★[JACK 0820] 클릭한 선의 **실제 2D 길이**도 같이 들고 있는다 — 선은 긴데 구간이 0이면
        //   '경계 투영이 무너졌다'는 뜻이라, 이 둘을 나란히 봐야 원인이 갈린다.
        var lineLen = new System.Collections.Generic.Dictionary<(bool up, int gid, int bench), (double Len, int N)>();
        var wholeLoop = new System.Collections.Generic.HashSet<(bool up, int gid, int bench)>();
        // ★★★[JACK 0910 <i>"그 구간을 선택한 후에 그 구간내에서 시점 종점을 별도로 클릭해서 그부분만 변환"</i>]
        //   <b>고른 선이 덮는 구간 안의 더 작은 조각</b>. 비어 있으면 <c>lineArc</c>(구간 전체)를 쓴다.
        //   ★<b>조각 구간</b>이면 두 점을 그 안으로 가두므로 답이 하나다.
        //     <b>한 바퀴 고리</b>(닫힌 계단선)면 가두는 것이 아무 제약이 아니라 호가 <b>둘</b>이다 —
        //     그때만 짧은 쪽을 보여 주고 <b>반대쪽(R)</b>으로 뒤집게 한다(검토 0910 치명2).
        var partArc = new System.Collections.Generic.Dictionary<(bool up, int gid, int bench), (double T0, double T1)>();
        // ★<c>ShowPart</c>가 잡아 쓰려면 <b>그 앞에</b> 있어야 한다 — 계획 경계를 읽은 뒤 채운다.
        System.Collections.Generic.List<Point3>? boundaryRef = null;
        PickMark? partMark = null;   // 부분 구간 미리보기(빨간 띠) — 도면에는 아무것도 안 남긴다
        void DropPart()
        {
            try { partMark?.Dispose(); } catch { }
            partMark = null;
        }
        // ★★★[JACK 0824 "단마다 해당 단의 가상 계획폴리곤을 기억하고 그걸로 시작한다"]
        //   그 단의 링(닫힌 폴리곤) = 이 단 구간을 재는 **자**. 계획 폴리곤은 너무 작아
        //   바깥 단 조각이 코너 한 점으로 뭉개진다(0820 실측: 선 3m → 구간 0.000000m).
        var benchRing = new System.Collections.Generic.Dictionary<(bool up, int bench),
            System.Collections.Generic.List<Point3>>();
        var lineRef = new System.Collections.Generic.Dictionary<(bool up, int gid, int bench),
            System.Collections.Generic.List<Point3>>();
        // ★[JACK 0824] 클릭한 선의 한가운데 — '이 자리 지금 값이 뭐냐'를 되묻는 데 쓴다.
        var lineMid = new System.Collections.Generic.Dictionary<(bool up, int gid, int bench), Point3>();
        // ★[검토 0910 · 높음3] <b>지정과 그림을 늘 같이 움직인다.</b>
        //   종전엔 다른 선을 골랐다 돌아오면 프롬프트엔 〈부분 20m〉가 뜨는데 <b>띠는 없었다</b> —
        //   화면과 적용될 값이 갈리는 자리다. 고를 때마다 그 선의 지정을 다시 그린다.
        void ShowPart((bool up, int gid, int bench) k)
        {
            DropPart();
            if (!partArc.TryGetValue(k, out var pa)) return;
            try
            {
                var rr = lineRef.TryGetValue(k, out var rp3) ? rp3 : boundaryRef;
                if (rr == null || rr.Count < 3) return;
                var rc = GradingGeometry.CumLen2D(rr);
                var path = GradingGeometry.SubPath(rr, rc, pa.T0, pa.T1);
                // ★[검토 0910 · 보통3] 굵기는 <b>자의 둘레</b>로 뽑는다 — 조각 길이로 뽑으면
                //   3m 조각에서 반폭 7.5mm가 되어 "두꺼운 빨간선"이 아니게 된다.
                //   커서를 따라가던 띠와 <b>같은 식</b>이라 확정 전후로 굵기가 안 바뀐다.
                if (path.Count >= 2)
                    partMark = PickMark.PaintPath(doc, path, closed: false, BandHalf(rc[rc.Length - 1]));
            }
            catch { }
        }

        (bool up, int gid, int bench)? pick = null;   // [1회 1개] 선택은 항상 최대 하나
        bool finishedByEnter = false;
        bool clearAll = false;
        int gidSeq = 0;

        try
        {
            System.Collections.Generic.List<Point3>? boundary = null;
            double[]? cumB = null;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var regs = GradingBundleStore.TryLoadAll(db, tr, out string reason);
                if (regs == null || regs.Count == 0)
                {
                    Refuse(ed, cmdLabel, $"{cmdLabel}을(를) 실행할 수 없습니다.\n{reason}\n\n[정지면 생성](DHGRADE)을 먼저 실행하세요.");
                    tr.Commit();
                    return;
                }
                region = regs[^1];
                if (string.IsNullOrEmpty(activePlan)) activePlan = region.PlanHandle;
                if (!region.CutHasSlope && !region.FillHasSlope)
                {
                    Refuse(ed, cmdLabel, "이 구역에 절토·성토 사면이 없습니다.\n[정지면 생성](DHGRADE)을 먼저 실행하세요.");
                    tr.Commit();
                    return;
                }
                boundary = region.Boundary;
                boundaryRef = boundary;      // ★로컬 함수(ShowPart)가 쓸 사본 손잡이
                cumB = GradingGeometry.CumLen2D(boundary);
                // ★[검토 0824 M-4] 화면의 클릭 대상선은 **번들 제원**(지금 그려진 모양)으로 만들고,
                //   재생성은 **정지옵션 제원**으로 돈다. 둘이 다르면 자로 박아 넣은 링이 재생성 결과에
                //   없는 링이 된다 — 자리가 어긋나므로 그 자리에서 알린다(막지는 않는다: 사용자가
                //   일부러 바꿨을 수 있고, 0820 결론은 '정지옵션과 변환은 연동'이다).
                {
                    var sNow = GradingSettings.ToParams();
                    var sBun = region.Params;
                    bool same = System.Math.Abs(BaseSlopeOf(sNow, true) - BaseSlopeOf(sBun, true)) < 1e-9
                             && System.Math.Abs(BaseSlopeOf(sNow, false) - BaseSlopeOf(sBun, false)) < 1e-9
                             && System.Math.Abs(sNow.CutBenchHeight - sBun.CutBenchHeight) < 1e-9
                             && System.Math.Abs(sNow.FillBenchHeight - sBun.FillBenchHeight) < 1e-9
                             && System.Math.Abs(sNow.CutBenchWidth - sBun.CutBenchWidth) < 1e-9
                             && System.Math.Abs(sNow.FillBenchWidth - sBun.FillBenchWidth) < 1e-9;
                    if (!same)
                    {
                        ed.WriteMessage($"\n[{cmdLabel}] ⚠ 정지옵션이 화면의 정지면과 다릅니다 — " +
                                        "재생성하면 모양이 크게 바뀔 수 있습니다. 먼저 [정지면 생성]을 한 번 돌리는 편이 안전합니다.");
                        Log($"■ {cmdLabel} ⚠ 제원 불일치 — 정지옵션 절토{sNow.CutBenchHeight:0.##}m/1:{BaseSlopeOf(sNow, true):0.##} " +
                            $"성토{sNow.FillBenchHeight:0.##}m/1:{BaseSlopeOf(sNow, false):0.##} vs " +
                            $"번들 절토{sBun.CutBenchHeight:0.##}m/1:{BaseSlopeOf(sBun, true):0.##} " +
                            $"성토{sBun.FillBenchHeight:0.##}m/1:{BaseSlopeOf(sBun, false):0.##}");
                    }
                }

                // 클릭 대상 = 각 단의 '시작선'(절토=소단선·성토=사면선). 옹벽 구간이든 사면 구간이든 전부 대상.
                //   (옹벽도 사면도 같은 규칙 하나로 표현되므로 두 명령이 같은 선을 쓴다.)
                var ng = new NullGround();
                var cutEdges = new System.Collections.Generic.List<(bool, int, int, System.Collections.Generic.List<Point3>)>();
                var fillEdges = new System.Collections.Generic.List<(bool, int, int, System.Collections.Generic.List<Point3>)>();
                static System.Collections.Generic.List<System.Collections.Generic.List<Point3>>? RingsOf(
                    System.Collections.Generic.List<System.Collections.Generic.List<Point3>>? many,
                    System.Collections.Generic.List<Point3>? one)
                    => many ?? (one != null ? new() { one } : null);
                // ★★★[검토 0910 · 치명] <b>"있다고 한 방향인데 아무것도 못 만들었나"</b> —
                //   되돌리기를 걸지 말지는 <b>오직 이것</b>으로 정한다.
                //   <para>첫 판은 옛 줄의 <b>방향 꼬리표</b>로 갈랐는데, 재 보니 <b>출하 경로에서 한 번도 안 걸린다</b> —
                //   명령이 끝날 때 <c>RestoreAndCleanup</c>이 꼬리표 달린 줄을 <b>지우고</b> 맨 것으로 다시 그리고
                //   (<c>GradingBuilder.DrawWallLines</c>는 XData를 안 붙인다), 이 레이어에 그리는 다른 두 자리
                //   (<c>CreateGradingCommand</c>·<c>NoriCommand</c>)도 전부 맨 것이다.
                //   그래서 <c>TryReadPick</c>이 늘 거짓 → 꼬리표는 늘 <c>null</c> → <b>"둘 다 비었을 때만"</b>이라는
                //   고치기 전 조건으로 되돌아갔다.</para>
                //   <para>★물어야 할 것은 "이 옛 줄이 어느 방향인가"가 아니라 <b>관문이 어디서 걸렸나</b>다.
                //   ①번들이 <b>애초에 없다</b>고 한 방향은 잃을 것이 없다(전부 절토인 부지의 성토가 여기 걸린다 —
                //   그래서 한 방향뿐인 부지에서 헛되이 되돌리지 않는다).
                //   ②·③은 <b>있다고 해 놓고 못 만든</b> 것이라 그 방향 옹벽선을 잃는다.</para>
                bool lost = false;
                foreach (var (up, hasSlope, ringList, zones, target) in new[]
                {
                    (true, region.CutHasSlope, RingsOf(region.CutFinalRings, region.CutFinalRing),
                     region.CutWallZones, cutEdges),
                    (false, region.FillHasSlope, RingsOf(region.FillFinalRings, region.FillFinalRing),
                     region.FillWallZones, fillEdges),
                })
                {
                    // ★★★[JACK 0910 <i>"옹벽으로 바꾸면 사면변환 눌렀을 때 가이드선이 안 뜨네?"</i>]
                    //   <b>이 관문이 완전히 침묵이었다.</b> 여기서 건너뛰면 그 방향은 클릭할 선이 <b>하나도</b>
                    //   안 생기는데(아래 <c>DrawWallLines</c>가 레이어를 통째로 지우고 새것만 그린다),
                    //   왜 없는지 로그에 아무 자국이 없었다 — "기하 버그는 계측부터"를 어긴 자리다.
                    //   ★<b>지나가든 걸리든</b> 적는다. 걸린 것만 적으면 "정상일 때 무엇이었는지"를 못 견준다.
                    Log($"   대상선 재료[{(up ? "절토" : "성토")}] — 사면있음={hasSlope}"
                      + $" · 링 {(ringList == null ? "없음(null)" : ringList.Count + "개")}"
                      + $" · 구간 {(zones == null ? 0 : zones.Count)}개");
                    if (!hasSlope || ringList == null)
                    {
                        Log($"     → 건너뜀 — 이 방향은 클릭할 대상선이 <b>하나도</b> 안 생긴다"
                          + $"({(ringList == null ? "데이라잇 링이 없다" : "사면이 없다")})");
                        continue;
                    }
                    double bs = BaseSlopeOf(region.Params, up), ms = region.Params.WallGateSlope;
                    var vs = GradingGeometry.Build(region.Boundary, ng, region.Params, up, zones);
                    if (!vs.HasSlope)
                    {
                        // ★관문 ② — 번들엔 있다는데 지금 구간으로 다시 지으니 사면이 없다.
                        //   <b>이 방향 옹벽선을 잃는다</b>: JACK이 겪은 그 길이다
                        //   (절토를 전부 옹벽으로 바꾸면 절토 쪽이 여기 걸린다).
                        lost = true;
                        Log($"     → 건너뜀 — 다시 지은 기하에 사면이 없다(링 {vs.Rings.Count}개)"
                          + " · <b>이 방향 옹벽선을 잃는다</b> — 되돌리기를 건다");
                        continue;
                    }
                    int madeBefore = target.Count;
                    // ★[JACK 0824] 단마다 **클릭 대상 선이 놓인 링**을 자로 삼는다.
                    //   GenerateEdgeLinesTagged와 같은 짝짓기(2k, 2k+1)·같은 고르기(절토=아랫선/성토=윗선)여야
                    //   클릭한 선과 자가 어긋나지 않는다.
                    static double AvgZOf(System.Collections.Generic.List<Point3> r)
                    {
                        double t = 0; foreach (var q in r) t += q.Z; return r.Count > 0 ? t / r.Count : 0;
                    }
                    for (int k = 0; 2 * k + 1 < vs.Rings.Count; k++)
                    {
                        var rA = vs.Rings[2 * k]; var rB = vs.Rings[2 * k + 1];
                        if (rA.Count < 3 || rB.Count < 3) continue;
                        bool aHigher = AvgZOf(rA) >= AvgZOf(rB);
                        var crest = aHigher ? rA : rB;
                        var toe = aHigher ? rB : rA;
                        benchRing[(up, k)] = up ? toe : crest;
                    }
                    foreach (var fr in ringList)
                    {
                        if (fr == null || fr.Count < 3) continue;
                        var plain = SlopeHatchGenerator.GenerateEdgeLinesTagged(vs.Rings, ng, up, fr,
                            region.Boundary, zones, region.Boundary, null, target, bs, ms);
                        foreach (var e in plain)
                            if (up != e.IsSlope) target.Add((e.IsSlope, e.Bench, e.Seg, e.Pts));
                    }
                    // ★관문 ③ — 사면은 있다는데 클릭할 선이 <b>하나도</b> 안 나왔다. 이것도 잃은 것이다.
                    if (target.Count == madeBefore)
                    {
                        lost = true;
                        Log($"     → ⚠ 사면은 있는데 대상선이 <b>하나도</b> 안 나왔다(링 {vs.Rings.Count}개)"
                          + " — 되돌리기를 건다");
                    }
                }
                // ★★★[JACK 0910] <b>지우기 전에 지금 있는 것을 떠 둔다.</b>
                //   <para>바로 아래 <c>DrawWallLines(빈 배열)</c>은 "DH-옹벽선" 레이어를 <b>조건 없이 통째로</b>
                //   지운다(<c>GradingBuilder.EraseOnLayer</c>). 그리고 새로 만든 것만 다시 그린다 —
                //   그래서 한 방향이 비면 <b>그 방향 옹벽선이 화면에서 사라지고 Esc로도 안 돌아왔다</b>.</para>
                //   <para>★<b>못 그릴 것을 지우지 않는다</b> — 되돌릴 밑천을 먼저 잡아 둔다.</para>
                var priorWall = ReadWallLines(db, tr);
                GradingBuilder.DrawWallLines(db, tr, System.Array.Empty<System.Collections.Generic.List<Point3>>());
                madeIds = GradingBuilder.DrawWallLinesTagged(db, tr, cutEdges, fillEdges, activePlan);
                _restoreLines = new System.Collections.Generic.List<System.Collections.Generic.List<Point3>>();
                foreach (var (_, _, _, pts) in cutEdges) _restoreLines.Add(pts);
                foreach (var (_, _, _, pts) in fillEdges) _restoreLines.Add(pts);

                // ★★★[검토 0910 · 치명] <b>한 방향이라도 잃었으면 <u>있던 그대로</u> 되돌린다.</b>
                //   <para>종전 조건은 <c>_restoreLines.Count == 0</c> — <b>둘 다</b> 비었을 때만 걸렸다.
                //   그런데 실제 길은 <b>한 방향만</b> 빈다: 절토를 전부 옹벽으로 바꾸면 절토 쪽에
                //   사면이 없어 대상선이 안 나오는데, 성토는 남아 있으니 방어가 안 걸린다 →
                //   위 <c>DrawWallLines(빈 배열)</c>이 레이어를 통째로 지운 뒤라
                //   <b>절토 쪽 선은 Esc로도 안 돌아온다</b>(JACK 0910 <i>"가이드선이 안 뜨네?"</i>).</para>
                //   <para>★<b>반쪽만 섞지 않는다.</b> <c>priorWall</c>은 지우기 직전 레이어 <b>전체</b> 사본이라
                //   성공한 방향까지 들어 있다. 새로 만든 것과 섞으면 살아남은 방향이 <b>두 벌</b> 그려진다.
                //   잃었으면 <b>명령 시작 전 그대로</b>가 맞다.</para>
                if (lost && priorWall.Count > 0)
                {
                    _restoreLines = priorWall;
                    Log($"   ⚠ 한 방향을 잃었다 — 지웠던 옹벽선 {priorWall.Count}줄을 있던 그대로 되돌린다"
                      + $" (새로 만든 것: 절토 {cutEdges.Count} · 성토 {fillEdges.Count})");
                }

                double total = cumB[cumB.Length - 1];
                foreach (var id in madeIds)
                {
                    if (!TryReadPick(tr, id, app, out var pki)) continue;
                    var pts = new System.Collections.Generic.List<Point3>();
                    if (tr.GetObject(id, OpenMode.ForRead) is Polyline3d p3)
                        foreach (ObjectId vId in p3)
                            if (tr.GetObject(vId, OpenMode.ForRead) is PolylineVertex3d pv)
                                pts.Add(new Point3(pv.Position.X, pv.Position.Y, pv.Position.Z));
                    int gid = gidSeq++;
                    info[id] = (pki.up, pki.bench, gid);
                    var key = (pki.up, gid, pki.bench);
                    if (!groups.TryGetValue(key, out var g)) groups[key] = g = new();
                    g.Add(id);

                    // [리뷰 0803] 부지를 한 바퀴 도는 '닫힌 고리'는 최대간극 방식이 둘레 전체 비슷한 값을 준다 →
                    //   둘레 전체로 명시하고 안내한다(그 간극이 남아 엉뚱한 조각이 생기는 것도 막는다).
                    double len2d = 0;
                    for (int q = 1; q < pts.Count; q++)
                    {
                        double dx = pts[q].X - pts[q - 1].X, dy = pts[q].Y - pts[q - 1].Y;
                        len2d += System.Math.Sqrt(dx * dx + dy * dy);
                    }
                    lineLen[key] = (len2d, pts.Count);
                    if (pts.Count > 0) lineMid[key] = pts[pts.Count / 2];

                    bool closed = pts.Count >= 3
                        && System.Math.Abs(pts[0].X - pts[pts.Count - 1].X) < 0.05
                        && System.Math.Abs(pts[0].Y - pts[pts.Count - 1].Y) < 0.05;

                    // ★★★[JACK 0824] 이 선이 놓인 **그 단의 링**을 자로 쓴다. 링이 없으면 옛 방식(계획 폴리곤).
                    //   자를 바꾸면 34m 조각은 그 링 위에서 34m다 — 0으로 무너질 수가 없다.
                    var ruler = benchRing.TryGetValue((pki.up, pki.bench), out var br) && br.Count >= 3 ? br : null;
                    var rulerCum = ruler != null ? GradingGeometry.CumLen2D(ruler) : cumB;
                    var rulerPoly = ruler ?? boundary;
                    if (ruler != null) lineRef[key] = ruler;
                    double rulerTot = rulerCum[rulerCum.Length - 1];

                    if (closed) { lineArc[key] = (0.0, rulerTot); wholeLoop.Add(key); }
                    else
                    {
                        var iv = GradingGeometry.PickInterval(pts, rulerPoly, rulerCum);
                        if (iv != null) lineArc[key] = (iv.Value.T0, iv.Value.T1);
                    }
                }

                GradingBuilder.SetLayersColor(db, tr, new[] { "DH-옹벽선" }, GradingBuilder.EdgePickAci); // 시안 강조
                tr.Commit();
            }
            if (madeIds.Count == 0)
            {
                RestoreAndCleanup(db, madeIds);
                Refuse(ed, cmdLabel, "선택할 계단선을 만들지 못했습니다 — [정지면 생성]을 다시 실행한 뒤 시도하세요.");
                return;
            }
            Log($"■ {cmdLabel} 시작 {System.DateTime.Now:HH:mm:ss} — 대상선 {madeIds.Count}개");
            // [JACK 0804] 멘트 간결화 — 안내는 한 줄로.
            // ★[JACK 0910] 차례가 셋으로 갈렸으므로 <b>그 차례를 그대로</b> 적는다.
            ed.WriteMessage($"\n[{cmdLabel}] ① 계단선을 클릭 → ② 전체구간/구간지정 → ③ "
                          + (wallMode ? "단높이·소단길이" : "단높이·사면구배·소단길이")
                          + " 정하고 Enter. (전체해제=C · Esc=취소)");

            PickGuard.Enter(doc, "DH-옹벽선");

            // ★★[JACK 0820 'fillet처럼 옵션(O)를 하나 만들고'] 제원은 옵션에서 정하고, Enter는 적용만 한다.
            //   종전엔 Enter 뒤에 제원을 <b>순서대로 물었다</b> — 무엇을 고르고 있는지 보이지 않는 상태에서
            //   숫자를 세 번 받아야 했다. 옵션으로 빼면 <b>현재 값이 프롬프트에 늘 보이고</b>
            //   바꿀 것만 바꾸면 된다(AutoCAD 명령들의 방식).
            // ★[JACK 0910] <b>고른 것을 놓는다</b> — 색을 되돌리고 구간지정·빨간 띠까지 같이 걷는다.
            //   차례가 셋으로 갈리면서 <b>뒤로 물러나는 자리</b>가 생겼다(2·3단계에서 Esc·다시선택).
            void Deselect((bool up, int gid, int bench) k)
            {
                try
                {
                    using var trD = db.TransactionManager.StartTransaction();
                    if (groups.TryGetValue(k, out var gD))
                        foreach (var gid in gD) SetColorByLayer(trD, gid);
                    trD.Commit();
                }
                catch { }
                pick = null;
                partArc.Remove(k);
                DropPart();
            }

            // ══ ★★★[JACK 0911] <b>날개벽 선을 그려 본다 — 먼저 보고 정하려고.</b> ═══════════
            //   JACK: <i>"선택부분 끝점에서 <b>직각</b>부분, <b>코너</b>라면 연장(평면 기준으로
            //   연장선이 데이라잇과 만나는 지점까지의 평면거리만큼)으로 가상선을 만들어서
            //   그 <b>3개 선 모두</b> 만들어지는 걸로 먼저 만들어 봐. 그거 보고 결정하자."</i>
            //
            //   <para>★<b>형상을 안 바꾼다.</b> 지표면도 옹벽선도 안 건드리고 <b>선만</b> 그린다.
            //   0910~0911에 마감면을 아홉 번 짓고 아홉 번 되돌렸는데, 그 공통 원인이
            //   <b>모양을 눈으로 확인하기 전에 지은 것</b>이었다. 이번엔 보고 정한다.</para>
            //
            //   <para>선은 <c>DH-날개벽선</c> 레이어에 그린다 — <c>DHRESET</c>이 걷어 간다.</para>
            void ShowWings((bool up, int gid, int bench) k,
                           System.Collections.Generic.IReadOnlyList<Point3> ruler,
                           double[] rcum, double t0, double t1)
            {
                try
                {
                    var many = k.up ? region!.CutFinalRings : region!.FillFinalRings;
                    var one = k.up ? region.CutFinalRing : region.FillFinalRing;
                    // ★<b>가장 바깥 고리</b>가 데이라잇이다 — 여러 조각이면 제일 큰 것.
                    System.Collections.Generic.List<Point3>? day = one;
                    if (many != null && many.Count > 0)
                    {
                        double best = -1;
                        foreach (var r in many)
                        {
                            if (r == null || r.Count < 3) continue;
                            double a2 = 0;
                            for (int i = 0; i < r.Count; i++)
                            { var u = r[i]; var w2 = r[(i + 1) % r.Count]; a2 += u.X * w2.Y - w2.X * u.Y; }
                            a2 = System.Math.Abs(a2) * 0.5;
                            if (a2 > best) { best = a2; day = r; }
                        }
                    }
                    if (day == null || day.Count < 3)
                    { Log("   날개벽선 — 데이라잇 선이 없어 못 그린다"); return; }

                    // ★끝이 <b>계획 꼭짓점</b>에 놓였나 = 코너다. 볼록 코너의 바깥 점은
                    //   전부 그 꼭짓점 하나로 투영되므로 판정이 또렷하다(0910 실측).
                    bool IsCorner(double t)
                    {
                        if (boundary == null || cumB == null) return false;
                        var q = GradingGeometry.PointAtParam(ruler, rcum, t);
                        double tp = GradingGeometry.ParamAt(boundary, cumB, q.X, q.Y);
                        double tot2 = cumB[cumB.Length - 1];
                        for (int i = 0; i + 1 < cumB.Length; i++)
                        {
                            double d = System.Math.Abs(tp - cumB[i]);
                            d = System.Math.Min(d, tot2 - d);
                            if (d < 1.5) return true;
                        }
                        return false;
                    }

                    var segs = new System.Collections.Generic.List<System.Collections.Generic.List<Point3>>();
                    // ① 고른 구간 그 자체
                    var midPath = GradingGeometry.SubPath(ruler, rcum, t0, t1);
                    if (midPath.Count >= 2) segs.Add(midPath);
                    double zLine = midPath.Count > 0 ? midPath[0].Z : 0;   // 노선 표고 — 셋 다 이 표고다
                    // ②③ 양 끝의 날개선
                    string txt = "";
                    System.Collections.Generic.List<Point3>? wing0 = null, wing1 = null;
                    double zStop = double.NaN;
                    foreach (var (t, towardT1, nm) in new[] { (t0, true, "시점"), (t1, false, "종점") })
                    {
                        bool corner = IsCorner(t);
                        var w = GradingGeometry.WingLine(ruler, rcum, t, corner, towardT1, day, 500.0, out double gz);
                        // ★[JACK 0911 <i>"로그를 촘촘히 넣어"</i>] <b>못 만든 것도 왜 못 만들었는지 적는다.</b>
                        var atP = GradingGeometry.PointAtParam(ruler, rcum, t);
                        if (w == null)
                        {
                            txt += $" · {nm} {(corner ? "연장" : "직각")}=데이라잇에 <b>못 닿음</b>";
                            Log($"     {nm} 날개선 실패 — {(corner ? "연장(코너)" : "직각")}"
                              + $" · 끝점({atP.X:F1},{atP.Y:F1},{atP.Z:F2}) 호길이 {t:F1}m"
                              + $" · 데이라잇 {day.Count}점 — <b>반직선이 데이라잇에 안 닿는다</b>"
                              + "(데이라잇이 그 방향에 없거나 500m를 넘는다)");
                            continue;
                        }
                        segs.Add(w);
                        if (towardT1) wing0 = w; else wing1 = w;
                        // ★어디까지 쌓을지 — 두 끝 중 <b>더 멀리 가는 쪽</b>에 맞춘다(모자라면 끊겨 보인다).
                        if (double.IsNaN(zStop)) zStop = gz;
                        else zStop = k.up ? System.Math.Max(zStop, gz) : System.Math.Min(zStop, gz);
                        double len = System.Math.Sqrt((w[1].X - w[0].X) * (w[1].X - w[0].X)
                                                    + (w[1].Y - w[0].Y) * (w[1].Y - w[0].Y));
                        txt += $" · {nm} {(corner ? "연장(코너)" : "직각")} {len:0.#}m";
                        Log($"     {nm} 날개선 — {(corner ? "<b>연장(코너)</b>" : "직각")}"
                          + $" · 끝점({w[0].X:F1},{w[0].Y:F1},{w[0].Z:F2}) 호길이 {t:F1}m"
                          + $" → 닿은 곳({w[1].X:F1},{w[1].Y:F1}) 길이 {len:F1}m"
                          + $" · 그 자리 원지반 {gz:F2}m · 방향({(w[1].X - w[0].X) / len:F3},{(w[1].Y - w[0].Y) / len:F3})");
                    }
                    DrawWingLines(db, segs, "DH-날개벽선", 4);

                    // ★★★[JACK 0911] <b>선에서 그치지 않고 지표면까지 쌓아 면을 만든다.</b>
                    //   <i>"그 상태에서 지표면까지 그레이딩하고 사면하고 합성하면 어떨까"</i>
                    //   <para>세 선을 <b>한 줄</b>로 잇고, 그 선에서 한 단씩 밀어 올려
                    //   원지반 표고에 닿을 때까지 쌓는다. 고리(링)를 <b>안 고치므로</b>
                    //   0910~0911에 아홉 번 되돌린 그 턱이 <b>생길 자리가 없다</b>.</para>
                    Log($"   ★날개벽 — 고른 구간 [{t0:F1}..{t1:F1}] 길이 {GradingGeometry.SpanOf(t0, t1, rcum[rcum.Length - 1]):F1}m"
                      + $" · 노선 표고 {zLine:F2}m · 자 {ruler.Count}점(둘레 {rcum[rcum.Length - 1]:F1}m)"
                      + $" · 데이라잇 {day.Count}점");
                    var chain = new System.Collections.Generic.List<Point3>();
                    void Push(System.Collections.Generic.IReadOnlyList<Point3> l, bool rev)
                    {
                        for (int i = 0; i < l.Count; i++)
                        {
                            var q = l[rev ? l.Count - 1 - i : i];
                            if (chain.Count > 0)
                            {
                                var last = chain[chain.Count - 1];
                                if (System.Math.Abs(q.X - last.X) < 1e-6 && System.Math.Abs(q.Y - last.Y) < 1e-6) continue;
                            }
                            chain.Add(new Point3(q.X, q.Y, zLine));   // ★표고는 노선 그대로 — 수평
                        }
                    }
                    if (wing0 != null) Push(wing0, rev: true);     // 시점 날개(바깥 → 안쪽)
                    Push(midPath, rev: false);                     // 고른 구간
                    if (wing1 != null) Push(wing1, rev: false);    // 종점 날개(안쪽 → 바깥)

                    // ★★★[JACK 0911 <i>"날개벽쪽이 생성되어야하는데 안생성되었어"</i>]
                    //   <b>멈출 표고를 <u>자리마다</u> 묻는다 — 한 숫자로 끊으면 모자란다.</b>
                    //   <para>첫 판은 날개선이 데이라잇에 닿은 <b>한 점</b>의 표고로 전부 끊었다.
                    //   실측(0911 로그): <c>표고 105 → 120</c>에서 멈춰 <b>4단</b>밖에 안 쌓였다 —
                    //   그 부지 원지반은 27~150m라 한참 모자라다. 원지반은 자리마다 다르므로
                    //   <b>표면에 직접 물어야</b> 한다.</para>
                    IGroundSurface? realGround = null;
                    try
                    {
                        var gId = NoriCommand.FindByHandle(db, GradingSettings.LastGroundHandle);
                        if (gId.IsNull) gId = NoriCommand.FindByHandle(db, region!.GroundHandle);
                        if (!gId.IsNull)
                        {
                            using var trg = db.TransactionManager.StartTransaction();
                            if (trg.GetObject(gId, OpenMode.ForRead) is Autodesk.Civil.DatabaseServices.TinSurface gt)
                                realGround = new CachedGroundSurface(gt);
                            trg.Commit();
                        }
                    }
                    catch { }

                    int faceN = 0;
                    if (chain.Count >= 3 && (realGround != null || !double.IsNaN(zStop)))
                    {
                        var pw = GradingSettings.ToParams();
                        // ★미는 쪽 = <b>부지 바깥</b>. 선이 가는 방향의 어느 쪽이 바깥인지로 부호를 정한다.
                        double tmid = (t0 + t1) * 0.5;
                        var mid2 = GradingGeometry.PointAtParam(ruler, rcum, tmid);
                        var out2 = GradingGeometry.OutwardAt(ruler, rcum, tmid, 1.0);
                        int im = System.Math.Max(0, System.Math.Min(chain.Count - 2, chain.Count / 2));
                        double tx = chain[im + 1].X - chain[im].X, ty = chain[im + 1].Y - chain[im].Y;
                        double ox = out2.X - mid2.X, oy = out2.Y - mid2.Y;
                        int sgn = (-ty * ox + tx * oy) >= 0 ? +1 : -1;
                        var faces = realGround != null
                            ? GradingGeometry.WallFromLine(chain, realGround, pw, k.up,
                                  slope: pw.MinSlope, benchW: pw.BenchWidthOf(k.up), outSign: sgn)
                            : GradingGeometry.WallFromLine(chain, zStop, pw, k.up,
                                  slope: pw.MinSlope, benchW: pw.BenchWidthOf(k.up), outSign: sgn);
                        faceN = faces.Count;
                        if (faceN > 0) DrawWingLines(db, faces, "DH-날개벽면", 2);
                        // ★★[JACK 0911] <b>쌓은 것이 제대로인지 한 줄로 판단할 수 있게</b> —
                        //   단 수 · 표고 범위 · 한 단 폭 · 점 수 · 날개가 살아 있나.
                        if (faceN >= 2)
                        {
                            double dx0 = faces[1][0].X - faces[0][0].X, dy0 = faces[1][0].Y - faces[0][0].Y;
                            double stepRun = System.Math.Sqrt(dx0 * dx0 + dy0 * dy0);
                            double wantRun = System.Math.Max(pw.BenchHeightOf(k.up) * pw.MinSlope, pw.MinFaceRun)
                                           + pw.BenchWidthOf(k.up);
                            int wingKept = 0;
                            foreach (var f in faces)
                            {
                                bool a5 = wing0 == null, b5 = wing1 == null;
                                foreach (var q in f)
                                {
                                    if (wing0 != null && System.Math.Abs(q.X - wing0[1].X) < 15
                                                      && System.Math.Abs(q.Y - wing0[1].Y) < 15) a5 = true;
                                    if (wing1 != null && System.Math.Abs(q.X - wing1[1].X) < 15
                                                      && System.Math.Abs(q.Y - wing1[1].Y) < 15) b5 = true;
                                }
                                if (a5 && b5) wingKept++;
                            }
                            Log($"     쌓기 — 단 {faceN}개 · 표고 {faces[0][0].Z:F2} → {faces[faceN - 1][0].Z:F2}"
                              + $" · 한 단 폭 실측 {stepRun:F3}m / 기대 {wantRun:F3}m"
                              + (System.Math.Abs(stepRun - wantRun) > 0.05 ? " <b>⚠어긋남</b>" : " ✓")
                              + $" · 점 {faces[0].Count}→{faces[faceN - 1].Count}개"
                              + $" · <b>날개 양쪽이 살아 있는 단 {wingKept}/{faceN}</b>"
                              + (wingKept == faceN ? " ✓" : " <b>⚠날개가 빠진 단이 있다</b>")
                              + $" · 미는 쪽 {(sgn > 0 ? "왼쪽(+1)" : "오른쪽(-1)")}"
                              + $" · 원지반 {(realGround != null ? "표면에 직접" : $"한 표고 {zStop:F1}")}");
                        }
                        else Log($"     쌓기 — 단 {faceN}개뿐 <b>⚠</b>"
                               + $" (선 {chain.Count}점 · 원지반 {(realGround != null ? "표면" : $"{zStop:F1}")})");
                    }
                    txt += $" · 쌓은 단 {faceN}개(표고 {zLine:0.#}부터"
                         + (realGround != null ? " · 원지반 표면에 직접 물음)" : $" · 한 표고 {zStop:0.#}로 끊음)");
                    ed.WriteMessage($"\n   날개벽선 {segs.Count}개(DH-날개벽선) · 면(DH-날개벽면){txt}");
                    Log($"   날개벽선 {segs.Count}개{txt}");

                    // ══ ★★★[JACK 0911] <b>가상 계획폴리곤</b> — 구간에서 <u>계산으로</u> 만든다 ══════
                    //
                    //   <para>JACK: <i>"난 복잡하게 TIN을 억지로 날들고 날개벽을 만들어서 끼워넣고
                    //   막 그런거보다도 그냥 구간에서 선을 설정하면 그선이 포함된 <b>계획폴리곤(가상)</b>이
                    //   생성되고 그게 기존에있던 <b>이어서하기</b>를 어떻게 응용해서 만들어서
                    //   합성하는식으로하면 깔끔히 될것같아"</i></para>
                    //
                    //   <para>★<b>베끼지 않고 계산한다.</b> JACK: <i>"내가 그린건 그냥 사면선에 대충 대고
                    //   그은거야 그거대로 보고가면안되 우린 <b>계산해서</b> 그려야되"</i>.
                    //   네 변이 전부 셈에서 나온다 —
                    //   ①안쪽 = 찍은 선에서 <b>한 단 내밀기</b>(단높이×구배+소단폭)만큼 안쪽,
                    //   ②③날개 = 직각(변 가운데) 또는 <b>연장</b>(코너)으로 <b>마감 링에 닿을 때까지</b>,
                    //   ④바깥 = <b>옛 데이라잇 ∪ 새 데이라잇</b> 링을 그대로 따라.</para>
                    //
                    //   <para>★<b>왜 거리 하나로 안 줄이나.</b> 첫 판은 "먼 쪽 거리"를 하나 구해 사방으로
                    //   그만큼 밀었다. 그런데 데이라잇 링의 <b>마이터 코너</b>는 경계에서 84.85m인데
                    //   같은 링의 <b>변</b>은 60m다. 코너 값이 뽑히자 폴리곤이 사방으로 부풀어
                    //   진짜 마감선을 <b>25m 넘어섰다</b>(오프라인 실측 S111).</para>
                    //
                    //   <para>★<b>지금은 그려서 보여 주기만 한다 — 지표면을 안 건드린다.</b>
                    //   0910~0911에 마감면을 <b>열두 번</b> 짓고 열두 번 되돌렸고, 그 공통 원인이
                    //   <b>모양을 눈으로 확인하기 전에 지은 것</b>이었다. 폴리곤은 <c>DH-가상계획선</c>에
                    //   그려지고 <c>DHRESET</c>이 걷어 간다. 형상 합성은 이 모양을 보고 정한다.</para>
                    try
                    {
                        if (boundary == null || cumB == null)
                            Log("   가상 계획폴리곤 — 계획 경계가 없어 못 만든다");
                        else if (realGround == null)
                            Log("   가상 계획폴리곤 — 원지반 표면을 못 찾아 못 만든다"
                              + $"(설정 핸들 '{GradingSettings.LastGroundHandle}' · 구역 핸들 '{region!.GroundHandle}')");
                        else
                        {
                            var pv = GradingSettings.ToParams();
                            // ★★★[JACK 0911] <b>날개벽을 어떤 구배로 마감하나</b> — JACK이 정한 규칙:
                            //   <i>"옹벽-옹벽, 옹벽-사면, 사면-옹벽에서의 날개벽은 <b>무조건 옹벽</b>으로해야하고
                            //   사면-사면은 <b>급한경사의 사면</b>으로 날개벽을 마감해야해"</i>
                            //
                            //   <para>★<b>옹벽 변환</b>은 정해진다 — 바꾼 쪽이 옹벽이고 나머지가 사면이니
                            //   "사면-옹벽"이라 날개벽은 <b>옹벽</b>이다. 그래서 <c>MinSlope</c>를 쓴다.</para>
                            //
                            //   <para>★<b>사면 변환의 "사면-사면"은 아직 정해지지 않았다.</b>
                            //   이 저장소에 <i>급한 경사</i>에 해당하는 값이 <b>없다</b> —
                            //   <c>WallGateSlope</c>(0.05)는 "옹벽으로 볼 문턱"이라 사실상 수직이고,
                            //   <c>SlopeFallback</c>(1.5)은 보통 사면이다. 없는 값을 <b>지어내지 않는다</b>.
                            //   지금은 옹벽 구배를 쓰고 <b>무엇을 썼는지 로그에 적는다</b> — JACK이 값을 정하면 여기만 바꾼다.
                            //   (또 하나: 이 자리는 제원을 묻기 <b>전</b>이라 목표 구배를 아직 모른다.
                            //    제원을 받은 뒤 다시 그리려면 부르는 차례도 같이 옮겨야 한다.)</para>
                            double wingSlope = pv.MinSlope;
                            Log($"     날개벽 구배 — {(wallMode ? "<b>옹벽 변환</b>이라 사면-옹벽 → 옹벽(1:{0})".Replace("{0}", $"{wingSlope:0.###}") : $"<b>사면 변환</b>인데 사면-사면의 '급한 경사'가 <b>아직 안 정해졌다</b> — 우선 옹벽 구배(1:{wingSlope:0.###})로 그린다")}"
                              + $" · 한 단 내밀기 {System.Math.Max(pv.BenchHeightOf(k.up) * System.Math.Max(wingSlope, pv.MinSlope), pv.MinFaceRun) + pv.BenchWidthOf(k.up):F3}m");
                            var vpoly = GradingGeometry.BuildWallPolygon(
                                ruler, rcum, t0, t1, boundary, cumB, day, realGround,
                                pv, k.up, wingSlope, cornerTol: 1.5, out string vwhy);
                            if (vpoly == null || vpoly.Count < 4)
                            {
                                Log($"   가상 계획폴리곤 <b>못 만들었다</b> — {vwhy}");
                                ed.WriteMessage("\n   가상 계획폴리곤 — 못 만들었습니다: " + vwhy);
                            }
                            else
                            {
                                // ★닫아서 그린다 — 계획폴리곤은 닫힌 선이어야 한다.
                                var closed = new System.Collections.Generic.List<Point3>(vpoly) { vpoly[0] };
                                DrawWingLines(db,
                                    new System.Collections.Generic.List<System.Collections.Generic.List<Point3>> { closed },
                                    "DH-가상계획선", 6);
                                double zLo = double.MaxValue, zHi = double.MinValue;
                                foreach (var q in vpoly)
                                { zLo = System.Math.Min(zLo, q.Z); zHi = System.Math.Max(zHi, q.Z); }
                                var pp = GradingGeometry.LastWallPolyParts;
                                Log($"   ★가상 계획폴리곤 {vpoly.Count}점 · 표고 {zLo:F2}~{zHi:F2}m"
                                  + $" · 네 변(안쪽 {pp.Inner} · 날개끝 {pp.Wing1} · 바깥 {pp.Far} · 날개시작 {pp.Wing0})");
                                Log($"     {GradingGeometry.LastWallPolyLog}");
                                Log($"     첫 점({vpoly[0].X:F1},{vpoly[0].Y:F1},{vpoly[0].Z:F2})"
                                  + $" · 안쪽 끝({vpoly[System.Math.Max(0, pp.Inner - 1)].X:F1},"
                                  + $"{vpoly[System.Math.Max(0, pp.Inner - 1)].Y:F1})"
                                  + $" · 바깥 첫({vpoly[System.Math.Min(vpoly.Count - 1, pp.Inner + pp.Wing1)].X:F1},"
                                  + $"{vpoly[System.Math.Min(vpoly.Count - 1, pp.Inner + pp.Wing1)].Y:F1})");
                                ed.WriteMessage($"\n   ★가상 계획폴리곤 {vpoly.Count}점(DH-가상계획선)"
                                  + $" · 표고 {zLo:0.#}~{zHi:0.#}m — <b>그리기만</b> 했습니다(지표면 안 바뀜).");
                            }
                        }
                    }
                    catch (System.Exception vex) { Log("   ⚠ 가상 계획폴리곤 실패 — " + vex.Message); }
                }
                catch (System.Exception wex) { Log("   ⚠ 날개벽선 실패 — " + wex.Message); }
            }

            // ★★★[JACK 0910] <b>구간지정</b> — 고른 구간 안에서 시점·종점을 찍는다.
            //   <para>고른 선이 정해진 <b>뒤에</b> 부르는 것이라 함수로 뗐다(차례가 셋으로 갈렸다).
            //   돌려주는 값: <c>true</c>=정했거나 전체로 두기로 했다 · <c>false</c>=취소(선을 다시 고른다).</para>
            bool AskPart((bool up, int gid, int bench) keyP)
            {
                var ruler = lineRef.TryGetValue(keyP, out var rp2) ? rp2 : boundary;
                if (ruler == null || ruler.Count < 3) { ed.WriteMessage("\n → 이 선의 자를 못 찾았습니다."); return false; }
                var rcum = GradingGeometry.CumLen2D(ruler);
                double tot = rcum[rcum.Length - 1];
                var full = lineArc[keyP];
                double fullSpan = GradingGeometry.SpanOf(full.T0, full.T1, tot);
                bool wholeRing = GradingGeometry.WholeRing(full.T0, full.T1, tot);

                // ★커서를 따라 <b>자리·표고·방향</b>이 보인다(<see cref="PickOnRing"/>) — Civil 3D 정지와 같은 방식.
                var p1 = PickOnRing(ed, "\n시작점 선택: ", null,
                                    ruler, rcum, full.T0, full.T1, wholeRing, out double ta);
                if (p1.Status != PromptStatus.OK) { ed.WriteMessage("\n → 구간지정 취소."); return false; }
                var p2 = PickOnRing(ed, "\n끝점 선택: ", ta,
                                    ruler, rcum, full.T0, full.T1, wholeRing, out double tb);
                if (p2.Status != PromptStatus.OK) { ed.WriteMessage("\n → 구간지정 취소."); return false; }

                bool flip = false;
                while (true)
                {
                    // ★★★[검토 0910] <b>판정은 Core가 한다</b>(<c>GradingGeometry.PartInterval</c>) —
                    //   가두기 · 방향 · 너무 짧은지 · 구간 전체인지가 한 함수에 있다.
                    //   여기 두면 오프라인 검사기가 못 닿아 <b>먹이는 값이 출하되는 값이 아닌</b> 검사가 된다(§78 ③·④).
                    var got = GradingGeometry.PartInterval(full.T0, full.T1, ta, tb, tot,
                                                           MinPartOf(), flip, out double moved, out string why,
                                                           out var fail, out bool onlySide);
                    if (got == null)
                    {
                        ed.WriteMessage("\n → " + why);
                        if (fail == GradingGeometry.PartFail.WholeInterval)
                        { partArc.Remove(keyP); DropPart(); return true; }
                        return false;   // 너무 짧다 — 선을 다시 고르거나 다시 찍게 한다
                    }
                    var (pt0, pt1, span2, wasWhole) = got.Value;
                    partArc[keyP] = (pt0, pt1);
                    ShowPart(keyP);
                    ShowWings(keyP, ruler, rcum, pt0, pt1);
                    bool banded = partMark != null && partMark.HasMarks;
                    ed.WriteMessage($"\n → 구간지정 {span2:0.#}m (전체 {fullSpan:0.#}m 중)"
                                  + (moved > 0.01 ? $" · 구간 밖을 찍어 끝으로 붙였습니다(최대 {moved:0.#}m)" : "")
                                  + (string.IsNullOrEmpty(why) ? "" : " · " + why)
                                  + (banded ? " — 빨간 띠가 바뀔 자리입니다." : ""));

                    // ★★[검토 0910 · 치명2] <b>한 바퀴 고리는 방향이 안 정해진다</b> — 그때만 뒤집을 기회를 준다.
                    if (!wasWhole) return true;
                    // ★★[검토 0910 · 보통2] <b>먹지도 않을 R을 걸지 않는다.</b>
                    //   한쪽이 최소 길이에 못 미치면 뒤집어도 <b>같은 답</b>이 나오는데,
                    //   종전엔 R을 걸어 놓고 눌러도 아무 일이 없었다 — 게다가 그때는
                    //   <c>tookOther</c>가 거짓이라 <b>안내조차 안 나갔다</b>.
                    //   쓸 수 있는 답이 하나뿐이면 R을 안 걸고, 위 <c>why</c>가 그 사실을 말한다.
                    if (onlySide) return true;
                    var pk2 = new PromptKeywordOptions($"\n이 조각으로 할까요? 〈{span2:0.#}m〉  Enter=예");
                    pk2.Keywords.Add("R", "R", "반대쪽(R)");
                    pk2.AllowNone = true;
                    var rr2 = ed.GetKeywords(pk2);
                    if (rr2.Status == PromptStatus.Cancel)
                    {
                        partArc.Remove(keyP); DropPart();
                        ed.WriteMessage("\n → 구간지정 취소.");
                        return false;
                    }
                    // ★★★[검토 0910 · 치명1] <c>GetKeywords</c>는 키워드를 <c>OK</c>로 돌려준다 —
                    //   <c>Keyword</c>만 보면 <b>R이 통째로 무시</b>된다(이 저장소의 다른 세 곳이 이미 둘 다 받는다).
                    if ((rr2.Status == PromptStatus.Keyword || rr2.Status == PromptStatus.OK)
                        && (rr2.StringResult ?? "").Trim().ToUpperInvariant() == "R")
                    { flip = !flip; continue; }
                    return true;
                }
            }

            var d0 = DefaultsFor(true, 0);
            double optH = d0.H, optW = d0.W;
            // ★[JACK 0910] 사면 변환이면 수직 기본값을 쓰지 않는다 — 아래 SlopeDefaultFor 참고.
            double optN = SlopeDefaultFor(wallMode, d0.N);
            bool setH = false, setN = false, setW = false;   // 사용자가 손댄 항목만 지킨다

            while (true)
            {
                // ★★[JACK 0820 실측 '*유효하지 않은 선택*'] **프롬프트 문구에 대괄호를 쓰면 안 된다.**
                //   AutoCAD는 문구 안의 <c>[...]</c>를 <b>자기 키워드 목록으로 읽는다</b> —
                //   상태 표시에 대괄호를 쓰면 진짜 키워드 목록을 덮어써서 H를 쳐도 안 먹는다
                //   (실측: "점을 예상하거나 또는 최종(L)/선택: 성토 1단]…"로 파싱이 깨졌다).
                //   → 상태는 〈 〉로 감싼다. 대괄호는 AutoCAD가 키워드를 붙일 자리로 비워 둔다.
                string cur = pick == null ? ""
                    : $" 〈선택 {(pick.Value.up ? "절토" : "성토")} {pick.Value.bench + 1}단〉";
                // ★[JACK 0910] 부분 지정이 걸려 있으면 <b>그 사실과 길이</b>를 프롬프트에 늘 보여 준다 —
                //   안 보이면 "전체를 바꾸는 줄 알았는데 일부만 바뀌었다"가 된다.
                string part = "";
                if (pick != null && partArc.TryGetValue(pick.Value, out var pv))
                {
                    var rr = lineRef.TryGetValue(pick.Value, out var rp) ? rp : boundary;
                    var rc = GradingGeometry.CumLen2D(rr);
                    // ★[검토 0910 · 낮음] 길이는 <b>한 함수</b>로 잰다 — 인라인 식이 남으면 규약이 갈린다.
                    double sp = GradingGeometry.SpanOf(pv.T0, pv.T1, rc[rc.Length - 1]);
                    part = $" 〈부분 {sp:0.#}m〉";
                }
                string spec = wallMode
                    ? $"〈단높이 {optH:0.##}m · 소단 {optW:0.##}m · 수직〉"
                    : $"〈단높이 {optH:0.##}m · 구배 1:{optN:0.##} · 소단 {optW:0.##}m〉";
                // ★★★[JACK 0910 <i>"먼저 … 선택옵션이 없는 그냥 일단 구간 선택하게 뜨고,
                //   선택하면 … 명령창옵션에서 전체구간·구간지정이 뜨고, 그후에 … 단높이나 사면구배같이
                //   옵션들 뜨고나서 다시 누르면 바뀌는거"</i>] <b>차례를 셋으로 가른다.</b>
                //
                //   <para><b>왜.</b> 종전엔 제원(H·R·T)·구간지정(P)·전체해제(C)가 <b>한 프롬프트에 다</b> 걸려 있었다.
                //   대괄호 안에 다섯이 늘어서니 <b>구간지정이 눈에 안 들어왔다</b>
                //   (JACK 0910: <i>"기능이 어디에 있는거야? 그냥 기존하고 똑같은데?"</i>).</para>
                //
                //   <para>이제 <b>①선만 고르고 → ②전체/구간을 정하고 → ③제원을 정한 뒤 Enter</b>다.
                //   한 화면에 물어보는 것이 하나씩이라 <b>다음에 뭘 해야 하는지가 프롬프트에 그대로</b> 있다.</para>
                //
                //   <para>★<b>제원 키워드는 이 단계에 없다</b> — 선을 고르기 전에 값을 바꾸는 것은
                //   무엇에 걸리는지 모르는 채로 값을 만지는 일이었다.</para>
                var peo = new PromptEntityOptions($"\n계단선을 클릭하세요{cur}{part} (Esc=취소)");
                peo.AllowNone = false;
                //   ※★[0820 실측] AutoCAD는 입력을 <b>localName의 앞글자</b>와 맞춘다.
                //     "단높이(H)"처럼 H가 <b>맨 뒤</b>면 앞글자가 아니라 'H'를 쳐도 "유효하지 않은 선택"이 된다.
                //     → <b>매칭용 이름(localName)은 글자 하나</b>로 두고, 한글은 <b>표시용(displayName)</b>에만 쓴다.
                //     세 인자가 각각 다른 일을 한다: global=코드가 받는 값 · local=사용자가 치는 값 · display=화면.
                peo.Keywords.Add("C", "C", "전체해제(C)");   // ★선을 안 골라도 쓰는 것이라 이 단계에 남긴다
                var per = ed.GetEntity(peo);
                if (per.Status == PromptStatus.Cancel) break;
                // ★[검토 0910 · 치명1] 키워드는 <c>Keyword</c>로도 <c>OK</c>로도 온다 — 둘 다 받는다.
                if (per.Status == PromptStatus.Keyword
                    || (per.Status == PromptStatus.OK && per.ObjectId.IsNull))
                {
                    // ★[JACK 0820 '대문자로 표기되었지만 대문자나 소문자 다 먹어야 돼'] 대소문자를 안 가린다.
                    if ((per.StringResult ?? "").Trim().ToUpperInvariant() != "C") continue;
                    clearAll = true; finishedByEnter = true;
                    ed.WriteMessage("\n → 전체 해제 — 순수 사면으로 재생성합니다.");
                    break;
                }
                if (per.Status != PromptStatus.OK) continue;

                using var tr = db.TransactionManager.StartTransaction();
                if (!info.TryGetValue(per.ObjectId, out var pk))
                {
                    var alt = PickGuard.SnapToLayerLine(ed, tr, per.PickedPoint, "DH-옹벽선");
                    if (alt.IsNull || !info.TryGetValue(alt, out pk))
                    {
                        ed.WriteMessage("\n → 근처에 대상 선이 없습니다 — 시안색 선 근처를 클릭하세요.");
                        tr.Commit();
                        continue;
                    }
                }
                var key = (pk.up, pk.gid, pk.bench);
                if (!lineArc.ContainsKey(key))
                {
                    ed.WriteMessage("\n → 이 선은 선택할 수 없습니다 — 다른 선을 클릭하세요.");
                    tr.Commit();
                    continue;
                }
                void ColorGroup((bool up, int gid, int bench) k2, bool on)
                {
                    if (!groups.TryGetValue(k2, out var g2)) return;
                    foreach (var gid in g2) { if (on) SetColor(tr, gid, SelAci); else SetColorByLayer(tr, gid); }
                }
                // [1회 1개 — JACK] 연달아 누르면 이전 선택은 해제하고 마지막 것만 남긴다.
                if (pick != null && !pick.Value.Equals(key)) ColorGroup(pick.Value, false);
                pick = key; ColorGroup(key, true);
                ShowPart(key);   // ★그 선에 걸린 구간지정이 있으면 <b>다시 그린다</b>(없으면 걷는다)
                // ★[JACK 0820] 안 손댄 항목은 **그 방향·그 단의 현재 값**으로 갱신한다 —
                //   절토·성토는 제원이 따로라(v16.6), 절토 값을 보여 주다 성토 선을 고르면 엉뚱한 값이 기본이 된다.
                var dk = DefaultsFor(pk.up, pk.bench);
                if (!setH) optH = dk.H;
                if (!setN) optN = SlopeDefaultFor(wallMode, dk.N);
                if (!setW) optW = dk.W;
                double pickSpan = GradingGeometry.SpanOf(lineArc[key].T0, lineArc[key].T1,
                    GradingGeometry.CumLen2D(lineRef.TryGetValue(key, out var rSel) ? rSel : boundary)[^1]);
                // ★★★[JACK 0910 <i>"코너찍고 한쪽만 변환시키면 옹벽으로 마감되지않아"</i>]
                //   <b>고른 선이 <u>지금</u> 무엇인지 그 자리에서 말한다.</b>
                //   <para>실측 로그(0910 16:40)가 보여 준 것: JACK이 고른 성토 1단 선은
                //   <b>이미 수직이던 구간 그 자체</b>였다(호길이 [44.5..173.6] = 옹벽 구간 [44.5..173.6]).
                //   대상선은 구간 경계에서 갈리므로, <b>이미 옹벽인 선</b>과 사면인 선이 생김새로는 똑같다.
                //   그래서 이미 벽인 선을 골라 벽으로 바꾸고 <i>"안 바뀐다"</i>가 됐다.</para>
                //   ★값이 같은지는 <b>맨 끝(Enter)에서도</b> 잡아 주지만(아래), 그때는 이미
                //   제원을 다 정한 뒤다. <b>고르는 순간</b> 보여 주는 편이 되돌아갈 길이 짧다.
                string nowTxt = "";
                if (lineMid.TryGetValue(key, out var pmidSel) && boundary != null)
                {
                    var zNow = pk.up ? region!.CutWallZones : region!.FillWallZones;
                    var (nS, nW) = SlopeZone.ResolveAt(zNow, pmidSel.X, pmidSel.Y, pk.bench,
                        BaseSlopeOf(region.Params, pk.up), region.Params.BenchWidthOf(pk.up), boundary, cumB!);
                    nowTxt = $" · 지금 {(nS <= GradingSettings.WallGateSlope ? "<수직=옹벽>" : $"1:{nS:0.##}")}"
                           + $"·소단 {nW:0.##}m";
                }
                ed.WriteMessage($"\n → {(pk.up ? "절토" : "성토")} {pk.bench + 1}단 선택 · 이 구간 {pickSpan:0.#}m"
                              + nowTxt
                              + (wholeLoop.Contains(key) ? " (한 바퀴 고리)" : ""));
                // ★★[JACK 0910] 사면 변환인데 <b>지금 그 자리가 수직</b>이면, 기본값을 사면으로 올렸다는 것을
                //   그 자리에서 말한다 — 안 말하면 "왜 1.5가 됐지"가 되고, 안 올리면 "왜 안 바뀌지"가 된다.
                if (!wallMode && !setN && dk.N <= GradingSettings.WallGateSlope)
                    ed.WriteMessage($"\n   이 자리는 지금 수직(옹벽)입니다 — 사면 기본값을 1:{optN:0.##}로 둡니다"
                                  + "(사면구배 R로 바꿀 수 있습니다).");
                tr.Commit();

                // ══ ★★★[JACK 0910] <b>2단계 — 전체구간이냐 구간지정이냐</b> ══════════════
                //   <para>선을 고른 <b>바로 그 자리</b>에서 묻는다. 종전엔 제원과 한데 섞여 있어
                //   <i>"기능이 어디 있는지"</i> 보이지 않았다.</para>
                var scope = new PromptKeywordOptions(
                    $"\n무엇을 바꿀까요? 〈{(pk.up ? "절토" : "성토")} {pk.bench + 1}단 · {pickSpan:0.#}m〉"
                  + " [전체구간(A)/구간지정(P)] <전체구간(A)>");
                scope.Keywords.Add("A", "A", "전체구간(A)");
                scope.Keywords.Add("P", "P", "구간지정(P)");
                scope.Keywords.Default = "A";
                scope.AllowNone = true;
                var scr = ed.GetKeywords(scope);
                if (scr.Status == PromptStatus.Cancel) { Deselect(key); continue; }
                string scopeKw = (scr.Status == PromptStatus.Keyword || scr.Status == PromptStatus.OK)
                               ? (scr.StringResult ?? "A").Trim().ToUpperInvariant() : "A";
                if (scopeKw == "P")
                {
                    if (!AskPart(key)) { Deselect(key); continue; }   // 취소 — 선부터 다시 고른다
                }
                else
                {
                    // ★전체구간을 골랐으면 <b>걸려 있던 구간지정을 푼다</b> — 안 풀면 화면과 결과가 갈린다.
                    partArc.Remove(key); DropPart();
                    ed.WriteMessage($"\n → 전체구간({pickSpan:0.#}m)을 바꿉니다.");
                }

                // ══ ★★★[JACK 0910] <b>3단계 — 제원을 정하고 Enter로 적용</b> ═══════════════
                bool goBack = false;
                while (true)
                {
                    // ★[JACK 0910] <b>1:0.01이 "수직"이라는 것을 글자로 적는다</b> — 숫자만으로는
                    //   그것이 옹벽이라는 것을 모른다(그래서 사면 변환이 옹벽을 만드는 일이 생겼다).
                    string spec3 = wallMode
                        ? $"〈단높이 {optH:0.##}m · 소단 {optW:0.##}m · 수직〉"
                        : $"〈단높이 {optH:0.##}m · 구배 1:{optN:0.##}"
                          + (optN <= GradingSettings.WallGateSlope ? "(수직=옹벽)" : "")
                          + $" · 소단 {optW:0.##}m〉";
                    string part3 = partArc.TryGetValue(key, out var pv3)
                        ? $" 〈구간 {GradingGeometry.SpanOf(pv3.T0, pv3.T1, GradingGeometry.CumLen2D(lineRef.TryGetValue(key, out var r3) ? r3 : boundary)[^1]):0.#}m〉"
                        : $" 〈전체 {pickSpan:0.#}m〉";
                    var spo = new PromptKeywordOptions($"\n제원 {spec3}{part3} (Enter=적용)");
                    spo.Keywords.Add("H", "H", "단높이(H)");
                    if (!wallMode) spo.Keywords.Add("R", "R", "사면구배(R)");
                    spo.Keywords.Add("T", "T", "소단길이(T)");
                    spo.Keywords.Add("S", "S", "다시선택(S)");
                    spo.AllowNone = true;
                    var spr = ed.GetKeywords(spo);
                    if (spr.Status == PromptStatus.None) { finishedByEnter = true; break; }
                    if (spr.Status == PromptStatus.Cancel) { goBack = true; break; }
                    string kw = (spr.StringResult ?? "").Trim().ToUpperInvariant();
                    if (kw == "H")
                    {
                        var h2 = AskPositive(ed, "단높이 (m)", optH, 0.2, 15.0);
                        if (h2 != null) { optH = h2.Value; setH = true; ed.WriteMessage($"\n → 단높이 {optH:0.##}m"); }
                        continue;
                    }
                    if (kw == "R")
                    {
                        var n2 = AskPositive(ed, "사면 구배 1:n", optN, GradingSettings.MinSlope, 30.0);
                        if (n2 != null) { optN = n2.Value; setN = true; ed.WriteMessage($"\n → 사면 구배 1:{optN:0.##}"); }
                        continue;
                    }
                    if (kw == "T")
                    {
                        var w2 = AskPositive(ed, "소단 길이 (m)", optW, 0.0, 60.0);
                        if (w2 != null) { optW = w2.Value; setW = true; ed.WriteMessage($"\n → 소단 길이 {optW:0.##}m"); }
                        continue;
                    }
                    if (kw == "S") { goBack = true; break; }
                }
                if (goBack) { Deselect(key); continue; }   // 선부터 다시
                break;                                      // Enter — 아래 적용으로
            }

            // ── 제원 = 옵션(O)에서 정해 둔 값. Enter는 적용만 한다(JACK 0820). ──
            //   옹벽은 구배를 묻지 않는다 — 수직(최소구배) 고정이다.
            double? askW = null, askN = null, askH = null;
            if (finishedByEnter && !clearAll && pick != null)
            {
                askN = wallMode ? GradingSettings.MinSlope : optN;
                askW = optW;
                askH = optH;
            }

            DropPart();   // ★[JACK 0910] 빨간 띠는 명령이 끝나면 반드시 걷는다(도면엔 아무것도 안 남는다)
            RestoreAndCleanup(db, madeIds);
            Log($"■ {cmdLabel} 종료({(finishedByEnter ? "Enter" : "Esc")}) — 선택 {(pick == null ? "없음" : "1건")}");

            if (!finishedByEnter || (pick == null && !clearAll))
            {
                ed.WriteMessage(finishedByEnter ? $"\n[{cmdLabel}] 선택 없음 — 변경 없이 종료." : $"\n[{cmdLabel}] 취소.");
                return;
            }

            // ★[JACK 0824] 단높이는 아래 루프가 정지옵션을 고치므로 **고치기 전에** 지금 값을 떠 둔다 —
            //   고친 뒤에 읽으면 언제나 '같다'가 나와 '안 바뀐다' 경고가 늘 뜬다.
            double beforeH = pick == null ? 0
                : GradingSettings.ToParams().BenchHeightAt(pick.Value.up, pick.Value.bench);

            // ── 적용: 기존 구간 + 이번 규칙 하나 ──
            var newCut = new System.Collections.Generic.List<SlopeZone>();
            var newFill = new System.Collections.Generic.List<SlopeZone>();
            if (!clearAll)
            {
                foreach (var (up, src, target) in new[]
                {
                    (true, region!.CutWallZones, newCut),
                    (false, region.FillWallZones, newFill),
                })
                {
                    if (src != null)
                        foreach (var z in src)
                            target.Add(new SlopeZone { T0 = z.T0, T1 = z.T1, Rules = new(z.Rules), Ref = z.Ref });
                    if (pick!.Value.up != up) continue;
                    // ★★★[JACK 0910] <b>부분 지정이 있으면 그것</b>, 없으면 구간 전체.
                    //   자료 구조는 처음부터 <c>[T0,T1]</c>이었다 — 새로 만든 것은 <b>입력 방식</b>뿐이다.
                    bool isPart = partArc.TryGetValue(pick.Value, out var pArc);
                    var a = isPart ? pArc : lineArc[pick.Value];
                    // ★[JACK 0910] 부분인지 전체인지 <b>로그에 남긴다</b> — 나중에 "왜 여기만 바뀌었지"를
                    //   되짚을 자리가 이 한 줄이다(이 저장소가 "단계마다 로그"로 정한 그 규칙).
                    Log($"■ 적용 구간 = {(isPart ? "부분 지정" : "선이 덮는 구간 전체")} [{a.T0:0.##} ~ {a.T1:0.##}]");
                    // ★[JACK 0824] 이 구간이 어느 자로 잰 값인지 함께 들려 보낸다 — 안 붙이면 재생성이
                    //   계획 폴리곤으로 되읽어 엉뚱한 자리가 된다.
                    var nz = new SlopeZone
                    {
                        T0 = a.T0, T1 = a.T1,
                        Ref = lineRef.TryGetValue(pick.Value, out var pr) ? pr : null,
                    };
                    nz.Rules.Add((pick.Value.bench, askN!.Value, askW!.Value));
                    nz.Normalize();   // ★[검토 0824 사소-14] FirstBench 전제를 지킨다(규칙이 늘어도 안전)
                    target.Add(nz);
                    // ★★★[JACK 0820] **단높이는 구간이 아니라 방향 전체에 쌓는다.**
                    //   구배·소단폭은 클릭한 선의 호길이 범위 안에만 적용되지만(위 Flatten),
                    //   단높이는 그러면 안 된다 — 둘레의 일부만 단높이가 다르면 같은 링에 표고가 둘이 되어
                    //   링을 이어 붙일 수 없다(v16.9가 '구간별 불가'라고 한 그 이유).
                    //   층 전체를 바꾸면 링마다 표고는 여전히 하나라 안전하다.
                    // ★★[JACK 0820 '정지옵션과 변환은 연동되긴 해야 해'] 규칙은 <b>정지옵션</b>에 쌓는다 —
                    //   재생성이 정지옵션을 읽으므로 여기 넣어야 먹고, 다음 변환의 기본값도 이 값이 된다.
                    var steps = up ? GradingSettings.CutBenchSteps : GradingSettings.FillBenchSteps;
                    steps.Add((pick.Value.bench, askH!.Value));
                    var norm = GradingSettings.ToParams(); norm.NormalizeBenchSteps();
                    GradingSettings.CutBenchSteps = new System.Collections.Generic.List<(int, double)>(norm.CutBenchSteps);
                    GradingSettings.FillBenchSteps = new System.Collections.Generic.List<(int, double)>(norm.FillBenchSteps);
                    // [스샷 버그 0804] 겹침은 합치지 않고 조각으로 가른다 — 새 규칙은 클릭한 선의 범위 '안'에만 남는다.
                    SlopeZone.Flatten(target, cumB![cumB.Length - 1]);
                    // ★[JACK 0824] 뒤 규칙에 덮여 아무 일도 안 하는 구간은 뺀다 —
                    //   안 빼면 변환할 때마다 쌓여 번들이 커지고 로그를 읽을 수 없다(실측: 4개 중 3개가 죽어 있었다).
                    SlopeZone.Compact(target, boundary, cumB);
                }
            }

            var planId = NoriCommand.FindByHandle(db, activePlan);
            var groundId = NoriCommand.FindByHandle(db, GradingSettings.LastGroundHandle);
            if (groundId.IsNull) groundId = NoriCommand.FindByHandle(db, region!.GroundHandle);
            if (planId.IsNull || groundId.IsNull)
            {
                AcadApp.ShowAlertDialog("정지면을 재생성하려면 [정지면 생성](DHGRADE)을 먼저 한 번 실행해야 합니다.");
                return;
            }

            // [리뷰 0803 — 치명] 재생성은 세션 설정을 읽는다. 재시작 후엔 기본값이라 '구간만 바꿔 다시 만들기'가
            //   전혀 다른 파라미터로 새로 만들기가 된다 → 이 구역의 저장값을 기준선으로 복원한 뒤 재생성.
            // ★★★[JACK 0820 '정지옵션에서 바꾸고 변환에서 바꿔도 정지옵션이 무조건 처음 설정값 5로 됐다']
            //   **세션 설정을 되돌리지 않는다.** 종전엔 여기서 <c>RestoreFrom(구역.Params)</c>로 덮었다 —
            //   0803이 막으려던 것은 "Civil3D를 껐다 켠 뒤 기본값으로 새로 만들어지는 것"인데,
            //   그건 이미 <c>SyncToDocument</c>가 <b>도면이 바뀔 때</b> 번들 값으로 복원해 막고 있다.
            //   여기서 또 덮으면 <b>사용자가 방금 정지옵션에서 바꾼 값이 매번 지워진다</b>(JACK 실측: 5로 되돌아감).
            //   → 정지옵션과 변환은 <b>연동</b>이다: 변환은 정지옵션 값을 기본값으로 쓰고, 바꾼 값을 거기에 쌓는다.
            // ★[검토 0824 M-2] **전체 해제는 단높이 규칙도 지운다.**
            //   종전엔 구간만 비우고 단높이 규칙은 남겨, "순수 사면으로 재생성합니다"라고 해 놓고
            //   15단부터 1m 같은 규칙이 그대로 살아 돌아왔다. 번들에도 저장돼 재시작해도 안 없어졌다
            //   (지우는 길이 DHRESET뿐이었다).
            if (clearAll)
            {
                GradingSettings.CutBenchSteps.Clear();
                GradingSettings.FillBenchSteps.Clear();
                Log("■ 전체 해제 — 구간과 단높이 규칙을 모두 비웠다(전역 단높이로 되돌아간다)");
            }
            GradingSettings.ZoneOverride = (newCut, newFill);
            // ★★[JACK 0824 '마지막 단을 선택하고 사면변환을 했지만 변하지 않았어'] **안 바뀌면 안 바뀐다고 말한다.**
            //   변환 기본값은 '그 단에 지금 적용 중인 값'이라, Enter만 치면 넣은 값이 지금 값과 같아
            //   아무 일도 안 일어난다 — 그런데 화면엔 '적용' 이라고만 떠서 고장으로 보인다(0824 실측:
            //   6단에 1:1.5를 넣었는데 이미 1단부터 1:1.5였다). 셋 다 같으면 그 자리에서 알린다.
            if (!clearAll && pick != null && lineMid.TryGetValue(pick.Value, out var pmid) && boundary != null)
            {
                bool pu = pick.Value.up;
                var oldZones = pu ? region!.CutWallZones : region!.FillWallZones;
                var pOld = region.Params;
                // ★★★[JACK 0910 로그] <b>구간지정을 했으면 <u>그 조각의 한가운데</u>에서 물어야 한다.</b>
                //   <para>여기는 <c>lineMid</c> — <b>클릭한 선 전체</b>의 한가운데를 쓰고 있었다.
                //   그런데 구간지정은 그 선의 <b>일부</b>다. 129m 선 안의 7m 조각을 골랐는데
                //   129m 한가운데 값을 가져다 견주면 그 조각의 현재 값과 다를 수 있다 —
                //   "같다"고 해 놓고 실제로는 바뀌거나, 그 반대가 된다.</para>
                var askPt = pmid;
                if (partArc.TryGetValue(pick.Value, out var paNow))
                {
                    var rrN = lineRef.TryGetValue(pick.Value, out var rpN) ? rpN : boundary;
                    if (rrN != null && rrN.Count >= 3)
                    {
                        var rcN = GradingGeometry.CumLen2D(rrN);
                        double totN = rcN[rcN.Length - 1];
                        double midT = paNow.T0 + GradingGeometry.SpanOf(paNow.T0, paNow.T1, totN) * 0.5;
                        askPt = GradingGeometry.PointAtParam(rrN, rcN, midT);
                    }
                }
                var (curS, curW) = SlopeZone.ResolveAt(oldZones, askPt.X, askPt.Y, pick.Value.bench,
                    BaseSlopeOf(pOld, pu), pOld.BenchWidthOf(pu), boundary, cumB!);
                double curH = beforeH;
                bool sameS = System.Math.Abs(curS - askN!.Value) < 1e-9;
                bool sameW = System.Math.Abs(curW - askW!.Value) < 1e-9;
                bool sameH = System.Math.Abs(curH - askH!.Value) < 1e-9;
                if (sameS && sameW && sameH)
                {
                    string msg = $"이 자리는 이미 {(wallMode ? "수직" : $"1:{curS:0.###}")} · 소단 {curW:0.##}m · 단높이 {curH:0.##}m 입니다 " +
                                 "— 넣은 값이 지금 값과 같아 **모양이 안 바뀝니다.**";
                    ed.WriteMessage($"\n[{cmdLabel}] ⚠ {msg}");
                    ed.WriteMessage($"\n   바꾸려면 {(wallMode ? "단높이(H)·소단길이(T)" : "단높이(H)·사면구배(R)·소단길이(T)")}로 값을 먼저 바꾸세요.");
                    Log($"■ {cmdLabel} ⚠ 값이 지금과 같다 — {(pu ? "절토" : "성토")} {pick.Value.bench + 1}단 " +
                        (partArc.ContainsKey(pick.Value) ? "(구간지정 한가운데에서 잼) " : "(선 한가운데에서 잼) ") +
                        $"현재 1:{curS:0.###}·소단{curW:0.##}m·단높이{curH:0.##}m / 넣은 값 1:{askN:0.###}·소단{askW:0.##}m·단높이{askH:0.##}m");

                    // ★★★[JACK 0910 <i>"코너찍고 한쪽만 변환시키면 옹벽으로 마감되지않아"</i>]
                    //   <b>안 바뀔 것을 13초 걸려 다시 만들지 않는다.</b>
                    //   <para>종전엔 위 두 줄을 적어 놓고 <b>그대로 재생성으로 들어갔다</b>.
                    //   13.6초가 지나고 화면은 그대로다 — 사용자는 <b>고장으로 읽는다</b>.
                    //   실측 로그(0910 16:40)가 정확히 그 판이다: 성토 1단이 이미 [44.5..173.6] 수직인데
                    //   그 안의 [83.0..90.1]에 <b>다시 수직</b>을 넣었다. 애드인은 옳게 알렸고,
                    //   그 다음에 <b>아무 일도 안 일어날 재생성</b>을 13.6초 돌렸다.</para>
                    //   ★<b>묻고 멈춘다.</b> 기본은 <b>아니오</b> — 값을 바꿔 다시 하는 편이 거의 언제나 맞다.
                    //     (그래도 다시 만들고 싶을 때가 있다: 다른 이유로 면이 낡았을 때.)
                    var pkSame = new PromptKeywordOptions("\n그래도 다시 만들까요?  Enter=아니오");
                    pkSame.Keywords.Add("Y", "Y", "예(Y)");
                    pkSame.Keywords.Add("N", "N", "아니오(N)");
                    pkSame.AllowNone = true;
                    var rSame = ed.GetKeywords(pkSame);
                    // ★키워드는 <c>Keyword</c>로도 <c>OK</c>로도 온다 — 둘 다 받는다(0910 치명1과 같은 자리).
                    bool goOn = (rSame.Status == PromptStatus.Keyword || rSame.Status == PromptStatus.OK)
                                && (rSame.StringResult ?? "").Trim().ToUpperInvariant() == "Y";
                    if (!goOn)
                    {
                        ed.WriteMessage("\n → 그대로 두었습니다 — 값을 바꿔 다시 해 보세요.");
                        Log($"■ {cmdLabel} — 값이 같아 재생성을 <b>안 했다</b>(사용자가 아니오)");
                        return;
                    }
                }
            }
            string what = clearAll ? "전체 해제"
                : $"{(pick!.Value.up ? "절토" : "성토")} {pick.Value.bench + 1}단부터 " +
                  (wallMode ? $"수직 옹벽 · 소단 {askW:0.##}m"
                            : $"경사 1:{askN:0.###} · 소단 {askW:0.##}m")
                  // ★[JACK 0820 '단높이가 바꿔도 안 바껴'] **적용한 단높이를 눈에 보이게 적는다.**
                  //   종전 메시지엔 단높이가 없어, 값이 안 들어간 건지 들어갔는데 안 먹는 건지 못 갈랐다.
                  + $" · 단높이 {askH:0.##}m";
            ed.WriteMessage($"\n[{cmdLabel}] {what} 적용 — 정지면 재생성 중…");
            // ★[JACK 0820] 단높이 규칙이 실제로 쌓혔는지 · 재생성이 그 값을 받는지 숫자로 남긴다.
            static string StepsTxt(System.Collections.Generic.IReadOnlyList<(int FromBench, double H)> l)
                => l.Count == 0 ? "없음" : string.Join(" ", l.Select(r => $"{r.FromBench + 1}단~{r.H:0.##}m"));
            Log($"■ {cmdLabel} 적용 — {what} · 절토구간 {newCut.Count} · 성토구간 {newFill.Count}");
                        Log($"   단높이 규칙 — 정지옵션(재생성이 읽는 값): 절토[{StepsTxt(GradingSettings.CutBenchSteps)}] 성토[{StepsTxt(GradingSettings.FillBenchSteps)}]" +
                $" · 전역 단높이 절토 {GradingSettings.CutBenchHeight:0.##}m 성토 {GradingSettings.FillBenchHeight:0.##}m");
            // ★★[JACK 0820 '중간에서 하면 잘 변환되는데 사면 맨 아랫단은 안 바뀌네'] **클릭한 선이 어느 구간으로 잡혔는가.**
            //   기하 엔진은 양방향 맨 아랫단이 모두 정상임을 하니스로 확인했다(S43·S45 — 링도 좁아지고
            //   옹벽선도 그 단에 선다). 그러면 남는 자리는 '클릭한 선 → 호길이 구간' 변환뿐이다:
            //   바깥 단일수록 링이 경계에서 멀어(성토 맨 아랫단 실측 43m) 그 점들을 경계에 투영하면
            //   코너에 뭉쳐 **구간이 실제 선보다 훨씬 좁게 잡힐 수 있다**. 그러면 옹벽은 그 좁은 자리에만
            //   서고 눈에는 '안 바뀐다'로 보인다. 추측 대신 숫자로 가른다.
            if (!clearAll && pick != null && cumB != null)
            {
                // ★[0824] 둘레는 **그 구간의 자** 기준이다 — 계획 폴리곤 둘레로 적으면 %가 엉뚱해진다.
                var pRef = lineRef.TryGetValue(pick.Value, out var pr2) ? pr2 : null;
                double tot = pRef != null
                    ? GradingGeometry.CumLen2D(pRef)[^1]
                    : cumB[cumB.Length - 1];
                var pa = lineArc[pick.Value];
                double segLen = pa.T1 >= pa.T0 ? pa.T1 - pa.T0 : pa.T1 + tot - pa.T0;
                Log($"   클릭한 선 — {(pick.Value.up ? "절토" : "성토")} {pick.Value.bench + 1}단 · " +
                    $"호길이 [{pa.T0:F1}..{pa.T1:F1}] = {segLen:F1}m / 둘레 {tot:F1}m " +
                    $"({segLen / System.Math.Max(tot, 1e-9) * 100:F0}%)" +
                    (pRef != null ? $" · 자=그 단의 링({pRef.Count}점)" : " · 자=계획 폴리곤(옛 방식)") +
                    (wholeLoop.Contains(pick.Value) ? " · 닫힌 고리(둘레 전체)" : "") +
                    (lineLen.TryGetValue(pick.Value, out var ll)
                        ? $" · 선 길이 {ll.Len:F1}m({ll.N}점)" +
                          (segLen < 0.5 && ll.Len > 2.0 ? "  ⚠자가 무너졌다 — 이런 줄이 보이면 알려 주세요" : "")
                        : ""));
                var zs = pick.Value.up ? newCut : newFill;
                for (int zi = 0; zi < zs.Count; zi++)
                {
                    var z = zs[zi];
                    string rt = z.Rules.Count == 0 ? "없음" : string.Join(" ", z.Rules.Select(r =>
                        $"{r.FromBench + 1}단~1:{r.Slope:0.###}" +
                        (r.Slope <= GradingSettings.WallGateSlope + 1e-9 ? "(수직)" : "")));
                    // ★[0824] 길이는 **그 구간의 자**로 잰다 — 클릭한 선의 자로 재면 자가 다른 구간이 엉뚱하게 찍힌다.
                    double zTot = z.RefCum != null ? z.RefCum[^1] : cumB[cumB.Length - 1];
                    double zl = z.T1 >= z.T0 ? z.T1 - z.T0 : z.T1 + zTot - z.T0;
                    Log($"   구간#{zi + 1} [{z.T0:F1}..{z.T1:F1}] {zl:F1}m/{zTot:F0}m — {rt}" +
                        (z.Ref != null ? $" · 자=링({z.Ref.Count}점)" : " · 자=계획"));
                }
            }
            CreateGradingCommand.DoGrade(doc, planId, groundId, GradeMode.RerunLast);

            // ★★★[JACK 0902 "노리선 기능 후 옹벽이나 사면변환을 하면 꼭 노리선 기능을 눌러야
            //   업데이트되는데, 노리선 기능을 사용했으면 그 후 변화되는 지형에 맞춰서 계속 자동 업뎃"]
            //
            //   정지면을 다시 만들면 사면·소단·옹벽이 <b>다 바뀌는데</b> 노리선은 옛 자리에 그대로 남는다.
            //   사람이 [노리선]을 다시 눌러야 맞춰지고, 안 누르면 <b>도면이 조용히 틀린 채</b>로 있다.
            //
            //   ★<b>이미 그려 둔 경우에만</b> 다시 그린다 — 한 번도 안 그린 사람에게 갑자기
            //   노리선이 생기면 그것도 놀랄 일이다. "쓰던 사람은 계속 맞고, 안 쓰던 사람은 그대로".
            //   ★명령으로 태운다 — 여기는 이미 명령 문맥이지만, 노리선은 제 트랜잭션·잠금을 쓰므로
            //   같은 흐름 안에서 직접 부르면 정지면 재생성이 쥔 것과 부딪힐 수 있다.
            if (HasNoriLines(db))
            {
                ed.WriteMessage($"\n[{cmdLabel}] 노리선을 새 형상에 맞춰 다시 그립니다…");
                Log($"■ {cmdLabel} — 노리선이 있어 자동 갱신(DHNORI)");
                try { doc.SendStringToExecute("DHNORI ", true, false, true); } catch { }
            }
        }
        catch (System.Exception ex)
        {
            RestoreAndCleanup(db, madeIds);
            ed.WriteMessage($"\n[{cmdLabel} 오류] " + ex.Message);
            Log($"■ {cmdLabel} 예외 — " + ex.GetType().Name + ": " + ex.Message + "\n" + ex.StackTrace);
        }
        finally
        {
            // ★★[검토 0910 · 높음1] <b>어느 길로 빠져나가든 띠를 걷는다.</b>
            //   종전엔 정상 경로에만 있었다 — 예외로 빠지면 <c>partMark</c>가 지역 변수째 버려지는데
            //   <c>TransientManager</c>는 그 물건을 계속 쥔다. 관리 쪽 참조가 사라지면
            //   <b>소멸자가 네이티브를 지우는</b> 바로 그 죽은 포인터 경로다
            //   (<see cref="PickMark"/>가 <i>"막겠다던 그 죽은 포인터를 내가 만들었다"</i>고 적어 둔 것).
            DropPart();
            PickGuard.Exit();
        }
    }

    /// <summary>그 방향의 전역(원래) 구배 — 최소구배 하한 적용.</summary>
    private static double BaseSlopeOf(GradingParams p, bool up)
        => System.Math.Max(up ? p.CutSlope : p.FillSlope, p.MinSlope);

    /// <summary>★★★[JACK 0910 <i>"옹벽으로 바꾸고나서 사면변환을 다시 수행했더니 안바꿔"</i>]
    /// <b>사면 변환의 기본 구배는 수직이면 안 된다.</b>
    ///
    /// <para><b>무엇이 있었나.</b> 기본값은 <see cref="BaseSlopeOf"/> — <b>전역 정지 구배</b>다.
    /// 그런데 JACK 도면은 전역이 <c>1:0.01</c>(수직)이라, [사면 변환]을 눌러도 기본값이 <b>수직</b>이었다.
    /// 그대로 Enter를 치면 <b>옹벽을 옹벽으로</b> 바꾼다 — 로그가 그 자리에서
    /// <i>"값이 지금과 같다"</i>고 적어 두었지만, 사용자는 "사면 변환이니 사면이 되겠지" 하고 누른다.</para>
    ///
    /// <para>→ 사면 변환일 때 기본값이 <b>수직 문턱 이하</b>면 <see cref="SlopeFallback"/>으로 올린다.
    /// 옹벽 변환은 그대로 둔다 — 그쪽은 수직이 <b>맞는 값</b>이다.</para></summary>
    private static double SlopeDefaultFor(bool wallMode, double n)
        => wallMode || n > GradingSettings.WallGateSlope ? n : SlopeFallback;

    /// <summary>전역이 수직일 때 사면 변환이 쓸 기본 구배 — 이 저장소의 처음 기본값과 같은 1:1.5.
    /// <para>★임의로 고른 수가 아니라 <c>GradingSettings.CutSlope</c>·<c>FillSlope</c>의 초기값이다.</para></summary>
    private const double SlopeFallback = 1.5;

    /// <summary>숫자 하나 입력 — 기본값은 현재 값(프롬프트의 &lt;&gt;는 AutoCAD가 자동 표시).
    /// 범위 밖이면 다시 묻는다. Esc/취소면 null.</summary>
    private static double? AskPositive(Editor ed, string label, double dflt, double min, double max)
    {
        while (true)
        {
            var pdo = new PromptDoubleOptions($"\n{label}")
            {
                DefaultValue = dflt,
                UseDefaultValue = true,
                AllowNegative = false,
                AllowZero = min <= 0,
                AllowNone = false,
            };
            var r = ed.GetDouble(pdo);
            if (r.Status != PromptStatus.OK) return null;
            if (r.Value < min - 1e-9 || r.Value > max + 1e-9)
            {
                ed.WriteMessage($"\n → {min:0.##}~{max:0.##} 사이 값을 넣어주세요.");
                continue;
            }
            return r.Value;
        }
    }

    /// <summary>★[JACK 0910] 지금 도면에 있는 <b>옹벽선</b>을 읽어 둔다 — 지우기 전에 떠 두는 밑천.</summary>
    /// <summary>★[JACK 0911] <b>날개벽선을 도면에 그린다</b> — 보고 판단하려는 것이라 <b>선만</b> 그린다.
    /// <para>레이어 <c>DH-날개벽선</c>. 부를 때마다 <b>먼저 비우고</b> 새로 그린다 —
    /// 구간을 다시 찍으면 옛 선이 남아 겹치면 무엇이 지금 것인지 알 수 없다.</para></summary>
    private static void DrawWingLines(Database db,
        System.Collections.Generic.IReadOnlyList<System.Collections.Generic.List<Point3>> segs,
        string layer, short aci)
    {
        using var tr = db.TransactionManager.StartTransaction();
        try
        {
            GradingBuilder.EnsureLayer(db, tr, layer, aci);
            GradingBuilder.EraseOnLayer(db, tr, layer);
            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
            var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            var layerId = lt.Has(layer) ? lt[layer] : ObjectId.Null;
            foreach (var seg in segs)
            {
                if (seg == null || seg.Count < 2) continue;
                var pl = new Polyline3d();
                if (!layerId.IsNull) pl.LayerId = layerId;
                ms.AppendEntity(pl);
                tr.AddNewlyCreatedDBObject(pl, true);
                foreach (var q in seg)
                {
                    var v = new PolylineVertex3d(new Point3d(q.X, q.Y, q.Z));
                    pl.AppendVertex(v);
                    tr.AddNewlyCreatedDBObject(v, true);
                }
            }
            tr.Commit();
        }
        catch { }
    }

    /// <summary>이 레이어에 지금 있는 줄을 <b>있는 그대로</b> 떠 온다 — 지우기 직전의 사본.
    /// <para>★[검토 0910] 한때 <b>방향 꼬리표</b>도 같이 읽게 했는데 <b>쓸모가 없었다</b> —
    /// 이 레이어에 줄을 그리는 세 자리가 전부 <c>DrawWallLines</c>(맨 것)라 XData가 없다.
    /// 꼬리표는 <c>DrawWallLinesTagged</c>가 이 명령 <b>안에서</b> 그리는 동안만 붙어 있고,
    /// 끝날 때 <c>RestoreAndCleanup</c>이 지우고 맨 것으로 다시 그린다.
    /// → 방향은 꼬리표가 아니라 <b>관문이 어디서 걸렸나</b>로 안다(<c>lost</c>).</para></summary>
    private static System.Collections.Generic.List<System.Collections.Generic.List<Point3>> ReadWallLines(
        Database db, Transaction tr)
    {
        var outp = new System.Collections.Generic.List<System.Collections.Generic.List<Point3>>();
        try
        {
            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);
            foreach (ObjectId id in ms)
            {
                try
                {
                    if (tr.GetObject(id, OpenMode.ForRead) is not Entity e) continue;
                    if (!string.Equals(e.Layer, "DH-옹벽선", System.StringComparison.OrdinalIgnoreCase)) continue;
                    if (e is not Polyline3d p3) continue;
                    var pts = new System.Collections.Generic.List<Point3>();
                    foreach (ObjectId vId in p3)
                        if (tr.GetObject(vId, OpenMode.ForRead) is PolylineVertex3d pv)
                            pts.Add(new Point3(pv.Position.X, pv.Position.Y, pv.Position.Z));
                    if (pts.Count >= 2) outp.Add(pts);
                }
                catch { }
            }
        }
        catch { }
        return outp;
    }

    private static void RestoreAndCleanup(Database db, System.Collections.Generic.List<ObjectId> madeIds)
    {
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            foreach (var id in madeIds)
            {
                try { if (!id.IsErased && tr.GetObject(id, OpenMode.ForWrite) is Entity e) e.Erase(); } catch { }
            }
            if (_restoreLines != null && _restoreLines.Count > 0)
                GradingBuilder.DrawWallLines(db, tr, _restoreLines);
            GradingBuilder.SetLayersColor(db, tr, new[] { "DH-옹벽선" }, 1);
            tr.Commit();
            _restoreLines = null;
        }
        catch { }
    }

    private static void Refuse(Editor ed, string label, string msg)
    {
        ed.WriteMessage($"\n[{label}] " + msg.Replace("\n", " "));
        AcadApp.ShowAlertDialog(msg);
    }

    private static void Log(string line)
    {
        // ★[JACK 0820] Carry로 남긴다 — 이 줄들을 쓴 직후 DoGrade가 로그를 새로 쓰기 때문에
        //   그냥 Append하면 **재생성 머리말과 함께 지워진다**(0820 실측).
        try { DiagLog.AppendCarry("\n" + line); } catch { }
    }

    private static bool TryReadPick(Transaction tr, ObjectId id, string app,
        out (bool up, bool isSlope, int bench, int seg, string plan) pk)
    {
        pk = default;
        if (id.IsErased) return false;
        if (tr.GetObject(id, OpenMode.ForRead) is not Entity ent) return false;
        var rb = ent.GetXDataForApplication(app);
        if (rb == null) return false;
        var v = rb.AsArray();
        if (v.Length < 5) return false;
        string plan = "";
        if (v.Length >= 6) { try { plan = v[5].Value as string ?? ""; } catch { } }
        pk = (System.Convert.ToInt32(v[1].Value) != 0, System.Convert.ToInt32(v[2].Value) != 0,
              System.Convert.ToInt32(v[3].Value), System.Convert.ToInt32(v[4].Value), plan);
        return true;
    }

    private static void SetColor(Transaction tr, ObjectId id, short aci)
    {
        var ent = (Entity)tr.GetObject(id, OpenMode.ForWrite);
        ent.Color = Color.FromColorIndex(ColorMethod.ByAci, aci);
    }

    private static void SetColorByLayer(Transaction tr, ObjectId id)
    {
        var ent = (Entity)tr.GetObject(id, OpenMode.ForWrite);
        ent.Color = Color.FromColorIndex(ColorMethod.ByLayer, 256);
    }
}
