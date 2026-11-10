using NetTopologySuite.Geometries;

namespace DH.Grading.Core;

/// <summary>★★[v116.5 · JACK 1110 «소단 표시가 정확하지 않아 — 소단 표시 누락»(3D 보기)] 소단 자리 — 면을 만든 높이 규칙(E2)에서 그 점을 이긴 원천의 거리 d가
/// 단면의 평평한 토막(소단)에 드는 자리. 3D 보기 소단 띠가 도면 계단선 짝(<see cref="RoadBenches.Strips"/>)에서 오면, 두 다리 사면이 만나는 안쪽에서
/// 계단선이 «가장 가까운 둘레» 거리 고리를 따라 꺾여(이긴 원천 = 다른 다리 모서리 원뿔) 실제 소단 턱(면은 E2에 맞춰 다듬어짐)에 선이 없어 잔디로 칠해졌다
/// (JACK 도로 재현: 셈 소단 270 ㎡ 중 32 ㎡ 빠짐).</summary>
public static partial class RoadRing
{
    /// <summary>소단 자리 셈 결과 — 다각형(0.25 m 칸을 합침) · 칸 수 · 넓이 · 이어 받는 원뿔이 이긴 칸(기울기로 가름) 수.</summary>
    public sealed class BenchAreaResult
    {
        public Geometry? Area; public int Cells, RelayCells, Probes; public double M2;
        public string Text => $"소단 자리(면 규칙) {M2:F1} ㎡ · 칸 {Cells:N0}(이어 받는 원뿔 {RelayCells}) · 잰 점 {Probes:N0}";
    }

    /// <summary>소단 자리 — <paramref name="b"/>의 격자(미리보기 1 m · 짓기 0.25 m)에서 소단 근처 마디만 골라 그 칸을 <paramref name="cell"/>마다 다시 잼.
    /// 변 · 꼭짓점 원천 = 거리 d가 평평한 토막 안 · 이어 받는 원뿔(거리 모름) = 원천 방향으로 0.2 m 사이 높이 차 기울기 0.3 아래.
    /// <paramref name="onSurface"/> = 지은 도로 면이 있는 자리(없으면 다 봄). 소단 없는 단면(소단폭 0)은 빈 결과.</summary>
    public static BenchAreaResult BenchArea(Built b, Func<double, double, bool>? onSurface = null, double cell = 0.25)
    {
        var R = new BenchAreaResult();
        if (b?.Ev == null || b.Grid == null || b.Grid.P.Length == 0) return R;
        var ev = b.Ev; var g = b.Grid; int m = ev.D.B.Count;
        // 단면마다 평평한 토막(d 구간)
        static List<(double A, double B)> Flats(Prof p)
        {
            var o = new List<(double, double)>();
            for (int i = 1; i < p.E.Count; i++) if (Math.Abs(p.E[i].R - p.E[i - 1].R) < 1e-9 && p.E[i].D > p.E[i - 1].D + 1e-9) o.Add((p.E[i - 1].D, p.E[i].D));
            return o;
        }
        var fc = Flats(ev.Pc); var ff = Flats(ev.Pf);
        if (fc.Count == 0 && ff.Count == 0) return R;
        static bool In(List<(double A, double B)> fl, double d, double pad) { foreach (var (a, c) in fl) if (d > a - pad && d < c + pad) return true; return false; }
        // 한 점이 소단인가(0 아님 · 1 소단 · 2 이어 받는 원뿔 소단)
        int Is(double x, double y)
        {
            var q = ev.Eval(x, y);
            if (q.Kind is not (2 or 3 or 4)) return 0;
            bool cut = q.Kind != 3; var w = cut ? q.WHi : q.WLo; var fl = cut ? fc : ff;
            if (w.Src < 0) return 0;
            if (w.Src < 2 * m) return In(fl, w.Dist, 0) ? 1 : 0;
            var gd = ev.SourceGrad(w.Src, x, y); if (!double.IsFinite(gd.X)) return 0;
            var q2 = ev.Eval(x + gd.X * 0.2, y + gd.Y * 0.2); if (q2.Kind != q.Kind) return 0;
            double v1 = cut ? q.Hi : q.Lo, v2 = cut ? q2.Hi : q2.Lo;
            return Math.Abs(v2 - v1) / 0.2 < 0.3 ? 2 : 0;
        }
        // 1) 후보 마디 — 바뀐 자리 · 원천 거리가 소단 ± (격자 칸 + 0.5 m) 안 · 이어 받는 원뿔은 다 후보
        double pad = g.Cell + 0.5;
        var cand = new List<int>();
        for (int k = 0; k < g.P.Length; k++)
        {
            if (!g.Keep[k]) continue;
            var q = g.P[k]; if (q.Kind is not (2 or 3 or 4)) continue;
            bool cut = q.Kind != 3; var w = cut ? q.WHi : q.WLo;
            if (w.Src < 0) continue;
            if (w.Src >= 2 * m || In(cut ? fc : ff, w.Dist, pad)) cand.Add(k);
        }
        // 2) 후보 마디의 칸(마디 둘레 ± 반 칸)을 cell마다 — 칸 열쇠(전역 cell 격자)로 겹침 없이
        double c = Math.Max(0.05, cell);
        var hit = new System.Collections.Concurrent.ConcurrentDictionary<(long, long), byte>();
        int probes = 0;
        Parallel.ForEach(cand, k =>
        {
            int i = k % g.W, j = k / g.W; double cx = g.X(i), cy = g.Y(j), h = g.Cell / 2;
            long i0 = (long)Math.Floor((cx - h) / c), i1 = (long)Math.Floor((cx + h - 1e-9) / c), j0 = (long)Math.Floor((cy - h) / c), j1 = (long)Math.Floor((cy + h - 1e-9) / c);
            for (long a = i0; a <= i1; a++)
                for (long bb = j0; bb <= j1; bb++)
                {
                    if (hit.ContainsKey((a, bb))) continue;
                    double x = (a + 0.5) * c, y = (bb + 0.5) * c;
                    Interlocked.Increment(ref probes);
                    if (onSurface != null && !onSurface(x, y)) { hit.TryAdd((a, bb), 0); continue; }
                    int v = Is(x, y);
                    hit.TryAdd((a, bb), (byte)v);
                }
        });
        R.Probes = probes;
        // 3) 줄마다 이어진 칸 → 네모 → 합침
        var rows = hit.Where(kv => kv.Value != 0).GroupBy(kv => kv.Key.Item2);
        var gf = NtsSupport.Factory();
        var rects = new List<Geometry>();
        foreach (var row in rows)
        {
            var xs = row.Select(kv => kv.Key.Item1).OrderBy(v => v).ToList();
            R.Cells += xs.Count; R.RelayCells += row.Count(kv => kv.Value == 2);
            int s = 0;
            while (s < xs.Count)
            {
                int e = s; while (e + 1 < xs.Count && xs[e + 1] == xs[e] + 1) e++;
                double x0 = xs[s] * c, x1 = (xs[e] + 1) * c, y0 = row.Key * c, y1 = (row.Key + 1) * c;
                rects.Add(gf.CreatePolygon(new[] { new Coordinate(x0, y0), new Coordinate(x1, y0), new Coordinate(x1, y1), new Coordinate(x0, y1), new Coordinate(x0, y0) }));
                s = e + 1;
            }
        }
        if (rects.Count > 0)
        {
            try { R.Area = NetTopologySuite.Operation.Union.CascadedPolygonUnion.Union(rects); }
            catch { R.Area = gf.BuildGeometry(rects).Buffer(0); }
            R.M2 = R.Area?.Area ?? 0;
        }
        return R;
    }
}
