using System.Collections.Generic;

namespace DH.Grading.Core;

/// <summary>★★★[2차 검토 0909 · 높음3] <b>링을 가시설/열린굴착 토막으로 자른다.</b>
///
/// <para><b>왜 Core에 있나.</b> 이 셈은 처음에 <c>StationMarks</c>(AutoCAD 계층)에 있었다.
/// 그래서 <b>오프라인 검사기가 한 줄도 못 쟀다</b> — 그 상태에서 "868개 통과"를 근거로 내밀었는데,
/// 정작 가장 위험했던 결함(<b>짝짓기 키</b>)은 그 868개 중 <b>0개</b>가 볼 수 있었다.
/// 증거가 아닌 것을 증거라고 말한 셈이라, <b>재려면 여기로 내려와야</b> 했다.</para>
///
/// <para><b>무엇을 하나.</b> 링의 점마다 <see cref="SlopeZone.IsWallAtPoint"/>로 <i>수직인가</i>를 묻고,
/// 답이 바뀌는 자리에서 끊는다. 수직 토막에는 <b>어느 구간이 이겼는지</b>(<c>Zi</c>)를 붙인다 —
/// 이것이 종단에서 <b>상단 링과 바닥 링을 짝짓는 키</b>다.</para>
///
/// <para>★<b>판정은 기하가 쓰는 그 자로 한다.</b> "구간 안이면 수직"이 아니다 —
/// 원래 구배가 0인 구조물은 <b>둘레 전체가 이미 수직</b>이라, 구간 밖도 수직이어야 맞다.
/// <see cref="SlopeZone.ResolveAt"/>에 그대로 물어보므로 <b>형상과 어긋날 수가 없다</b>.</para></summary>
public static class WallSplit
{
    /// <summary>한 토막 — 수직인가, 어느 구간이 이겼나(수직이 아니면 −1), 그리고 점들.</summary>
    public readonly record struct Piece(bool Wall, int Zi, List<Point3> Pts);

    /// <summary>링을 토막 낸다. 구간이 없으면 <b>빈 목록</b>을 낸다(부르는 쪽이 옛 규칙으로 간다).</summary>
    /// <param name="ring">자를 링. <b>닫힌 링</b>(첫 점이 끝에 복제)이어도 된다.</param>
    /// <param name="refB">구간의 자가 되는 경계 — 보통 터파기 바닥.</param>
    /// <param name="refCum">그 경계의 누적 호길이(<see cref="GradingGeometry.CumLen2D"/>).</param>
    /// <param name="baseSlope">구간이 안 이기는 자리의 구배 — <b>이 기록의</b> 값이다.</param>
    /// <param name="dens">자르기 전에 링을 이 간격으로 <b>촘촘하게</b> 만든다(m).</param>
    public static List<Piece> Split(IReadOnlyList<Point3> ring, IReadOnlyList<Point3> refB, double[] refCum,
                                    IReadOnlyList<SlopeZone> zones, double baseSlope, double gate,
                                    double dens = 1.0)
    {
        var outp = new List<Piece>();
        if (ring == null || ring.Count < 2 || zones == null || zones.Count == 0) return outp;

        // ★★★[3차 검토 0909 · 치명1] <b>자를 링을 먼저 촘촘하게 만든다.</b>
        //
        //   <b>왜.</b> 두 링의 해상도가 <b>천지 차이</b>다:
        //     · 바닥(<c>e.Bottom</c>) = 설계자가 그린 폴리선 <b>원본</b> — 사각 배수지면 <b>4점</b>
        //     · 상단(<c>e.FinalRing</c>) = 기하가 만든 것 — 실측 <b>715점</b>
        //   구간은 <b>호길이</b>로 자르는데 바닥에는 <b>잘릴 점이 없다</b>. 그래서 상단만 잘리고
        //   바닥은 통째로 남아 <b>짝이 한 조도 안 맺혔다</b> —
        //   종단면도에 <b>마젠타 막대가 아예 안 서고</b> 횡단 (전)(후)도 사라진다.
        //   ★평면·3D는 멀쩡하다(<c>GradingGeometry.Build</c>가 제 안에서 조밀화한다) — <b>종단만</b>
        //     틀리므로 눈치채기 가장 어려운 종류다.
        //
        //   ★<b>자(<c>refB</c>·<c>refCum</c>)는 안 건드린다</b> — 구간을 지정할 때 쓴 그 자라야 한다.
        //     촘촘하게 만드는 것은 <b>잘릴 링</b>뿐이다.
        ring = Densify(ring, dens);

        // 이 점이 수직인가, 그리고 <b>어느 구간이 이겼나</b>.
        //   ResolveAt과 같은 규칙 — 나중 구간이 앞 구간을 덮으므로 <b>마지막으로 품은 것</b>이 이긴다.
        (bool Wall, int Zi) At(Point3 p)
        {
            bool w = SlopeZone.IsWallAtPoint(zones, p.X, p.Y, 0, baseSlope, gate, refB, refCum);
            int zi = -1;
            if (w)
                for (int z = 0; z < zones.Count; z++)
                {
                    var zz = zones[z];
                    if (zz != null && zz.Rules.Count > 0 && zz.ContainsAt(p.X, p.Y, refB, refCum)) zi = z;
                }
            return (w, zi);
        }

        // ★★★[S101이 잡았다] <b>수직인지 아닌지가 바뀔 때만 끊는다.</b>
        //
        //   종전엔 <c>Zi</c>가 바뀌어도 끊었다. 그래서 <b>바탕이 이미 수직인 구조물</b>에
        //   구간을 하나 얹으면 <b>이어져 있는 한 장의 벽</b>이 구간 경계에서 두 토막으로 갈렸다 —
        //   같은 벽인데 <b>이음매에 측점이 하나 더</b> 서고 막대가 둘로 쪼개진다.
        //   물리적으로 하나인 것을 자료에서 둘로 만들면 안 된다.
        //
        //   ★<c>Zi</c>는 <b>그 줄기에서 이긴 구간 중 가장 앞 번호</b>로 정한다.
        //     상단 링과 바닥 링이 <b>같은 자로</b> 판정되므로 같은 줄기는 같은 번호를 받는다 —
        //     이것이 종단에서 상단↔바닥을 짝짓는 키다(치명2).
        var segs = new List<Piece>();
        var cur = new List<Point3> { ring[0] };
        var st = At(ring[0]);
        int runZi = st.Zi;
        for (int k = 1; k < ring.Count; k++)
        {
            var here = At(ring[k]);
            if (here.Wall == st.Wall)
            {
                cur.Add(ring[k]);
                if (here.Zi >= 0 && (runZi < 0 || here.Zi < runZi)) runZi = here.Zi;
                continue;
            }
            cur.Add(ring[k]);                                   // 경계점은 <b>양쪽에</b>(틈이 안 생기게)
            segs.Add(new Piece(st.Wall, runZi, cur));
            cur = new List<Point3> { ring[k] };
            st = here;
            runZi = here.Zi;
        }
        segs.Add(new Piece(st.Wall, runZi, cur));

        // ★0번 이음매 — 마지막 토막과 첫 토막이 같은 부류면 <b>한 줄기</b>다.
        //   링은 첫 점이 끝에 복제된 닫힌 선이라, 구간이 시작점을 걸치면 한 줄기가 두 토막으로 나온다.
        //   붙이지 않으면 <b>같은 자리에 벽이 둘</b> 선다.
        if (segs.Count > 1)
        {
            var a = segs[segs.Count - 1];
            var b = segs[0];
            // ★<c>Zi</c>가 달라도 <b>둘 다 벽이면 한 줄기</b>다 — 위와 같은 규칙이라야 앞뒤가 맞는다.
            if (a.Wall == b.Wall)
            {
                var joined = new List<Point3>(a.Pts);
                for (int k = 1; k < b.Pts.Count; k++) joined.Add(b.Pts[k]);   // 겹치는 점 하나를 뺀다
                int zi = a.Zi < 0 ? b.Zi : (b.Zi < 0 ? a.Zi : System.Math.Min(a.Zi, b.Zi));
                segs.RemoveAt(segs.Count - 1);
                segs[0] = new Piece(a.Wall, zi, joined);
            }
        }

        foreach (var g in segs)
            if (g.Pts.Count >= 2) outp.Add(g);                  // 2점 미만은 측점을 세울 선이 못 된다
        return outp;
    }

    /// <summary>선을 <paramref name="maxSeg"/> 이하 간격으로 쪼갠다 — 모양은 그대로다(직선 위에만 점을 더한다).</summary>
    private static List<Point3> Densify(IReadOnlyList<Point3> loop, double maxSeg)
    {
        if (loop.Count < 2 || maxSeg <= 1e-6) return new List<Point3>(loop);
        var outp = new List<Point3>(loop.Count * 2);
        for (int i = 0; i < loop.Count - 1; i++)
        {
            var a = loop[i]; var b = loop[i + 1];
            outp.Add(a);
            double dx = b.X - a.X, dy = b.Y - a.Y;
            double len = System.Math.Sqrt(dx * dx + dy * dy);
            int sub = (int)System.Math.Floor(len / maxSeg);
            for (int t = 1; t <= sub; t++)
            {
                double u = (double)t / (sub + 1);
                outp.Add(new Point3(a.X + dx * u, a.Y + dy * u, a.Z + (b.Z - a.Z) * u));
            }
        }
        outp.Add(loop[loop.Count - 1]);
        return outp;
    }
}
