// ── S213 ★★★[v113.1 · JACK 1104 «궁극적으로 고칠 수 없어?» → «한다»] 부지 둘레 땅으로 잰 수직 예산(VerticalBudget) ──
//   만든 꼴(현장 값 없음 — 공개 저장소) · 좌표는 현장 크기(X 2e5 · Y 5.5e5) · 원지반 = 10 m 격자 세모(출하 CachedGroundSurface와 같은 꼴의 입력)
//   ①먼 산(1 km 밖 400 m)은 예산을 안 올린다 ②닿는 거리 안의 언덕은 올린다 ③그 예산으로 지은 마지막 링이 모든 점에서 땅보다 높다(모든 방향 데이라잇)
//   ④성토 = 절토의 거울(구덩이) ⑤대소단(산지) — 여유 규칙 · 데이라잇 ⑥순수(같은 입력 같은 답) · 되풀이 끝남 ⑦종전 예산을 안 넘음
//   ⑧먼 산 탓에 늘던 사면 구간 전이 띠가 부지 둘레 예산이면 늘 일이 없다(띠 하나 10 m · 묶음 아님)
using DH.Grading.Core;

static class VerticalBudgetCheck
{
    const double OX = 200000, OY = 550000;

    sealed class GridGround : IGroundSurface
    {
        readonly double _x0, _y0, _c; readonly int _nx, _ny; readonly double[,] _z;
        public readonly List<WallDaylight.Tri> Tris = new();
        public GridGround(double x0, double y0, double x1, double y1, double cell, Func<double, double, double> f)
        {
            _x0 = x0; _y0 = y0; _c = cell;
            _nx = (int)Math.Ceiling((x1 - x0) / cell); _ny = (int)Math.Ceiling((y1 - y0) / cell);
            _z = new double[_nx + 1, _ny + 1];
            for (int i = 0; i <= _nx; i++) for (int j = 0; j <= _ny; j++) _z[i, j] = f(x0 + i * cell, y0 + j * cell);
            for (int i = 0; i < _nx; i++)
                for (int j = 0; j < _ny; j++)
                {
                    Point3 P(int a, int b) => new(_x0 + a * _c, _y0 + b * _c, _z[a, b]);
                    Tris.Add(new WallDaylight.Tri(P(i, j), P(i + 1, j), P(i + 1, j + 1)));
                    Tris.Add(new WallDaylight.Tri(P(i, j), P(i + 1, j + 1), P(i, j + 1)));
                }
        }
        public bool TryGetElevation(double x, double y, out double z)
        {
            z = 0;
            double u = (x - _x0) / _c, v = (y - _y0) / _c;
            int i = (int)Math.Floor(u), j = (int)Math.Floor(v);
            if (i < 0 || j < 0 || i >= _nx || j >= _ny) return false;
            double fu = u - i, fv = v - j;
            // 세모 (i,j)-(i+1,j)-(i+1,j+1) [fu ≥ fv] · (i,j)-(i+1,j+1)-(i,j+1)
            z = fu >= fv ? _z[i, j] + fu * (_z[i + 1, j] - _z[i, j]) + fv * (_z[i + 1, j + 1] - _z[i + 1, j])
                         : _z[i, j] + fu * (_z[i + 1, j + 1] - _z[i, j + 1]) + fv * (_z[i, j + 1] - _z[i, j]);
            return true;
        }
    }

    public static void Run(Action<string, bool, string> check)
    {
        Console.WriteLine("\n== S213 부지 둘레 땅으로 잰 수직 예산 — 먼 산 · 닿는 언덕 · 모든 방향 데이라잇 · 성토 거울 · 대소단 · 순수 · 띠 ==");
        var pad = new List<Point3> { new(OX + 0, OY + 0, 100), new(OX + 40, OY + 0, 100), new(OX + 40, OY + 30, 100), new(OX + 0, OY + 30, 100) };
        GradingParams P(double riseCut, double riseFill, bool terrace = false, double blend = 10) => new()
        {
            CutBenchHeight = 5, FillBenchHeight = 5, CutBenchWidth = 1, FillBenchWidth = 1, CutSlope = 1.5, FillSlope = 1.5,
            CellSize = 0.5, MaxBenches = 400, MaxRise = Math.Max(riseCut, riseFill), MaxRiseCut = riseCut, MaxRiseFill = riseFill,
            VertexSpacing = 2.0, MinSlope = 0.01, WallGateSlope = 0.05, MinFaceRun = 0.005, MiterConvex = true, MiterLimit = 2.0,
            MountainTerrace = terrace, TerraceInterval = 15, TerraceWidth = 15, ZoneBlendLength = blend,
        };
        // BuildParams와 같은 셈(여유 2단 · 대소단이면 간격마다 더)
        double SpareOf(double diff, bool terrace) => (2 + (terrace ? (int)Math.Floor(diff / 15) + 2 : 0)) * 5.0;
        VerticalBudget.Result Budget(GridGround g, bool terrace, double slopeMax = 1.5)
        {
            double gMax = g.Tris.Max(t => Math.Max(t.A.Z, Math.Max(t.B.Z, t.C.Z))), gMin = g.Tris.Min(t => Math.Min(t.A.Z, Math.Min(t.B.Z, t.C.Z)));
            return VerticalBudget.Local(pad, g.Tris,
                (up, rise) => VerticalBudget.ReachOf(rise, slopeMax, 1, 5, 0.005, terrace, 15, 15),
                d => SpareOf(d, terrace), Math.Max(gMax - 100, 0), Math.Max(100 - gMin, 0));
        }
        // 마지막 링이 모든 점에서 땅보다 높다(절토) / 낮다(성토) — 땅이 있는 점만
        (int Bad, int Seen) DaylightAll(GridGround g, GradingParams p, bool up)
        {
            var v = GradingGeometry.Build(pad, g, p, up, null);
            var last = v.Rings[^1];
            int bad = 0, seen = 0;
            foreach (var q in last)
            {
                if (!g.TryGetElevation(q.X, q.Y, out double gz)) continue;
                seen++;
                if (up ? q.Z < gz - 1e-9 : q.Z > gz + 1e-9) bad++;
            }
            return (bad, seen);
        }
        double Hill(double x, double y, double cx, double cy, double h, double r)
        {
            double d2 = ((x - cx) * (x - cx) + (y - cy) * (y - cy)) / (r * r);
            return h * Math.Exp(-d2);
        }

        // ① 먼 산 — 부지 둘레 105 m 평지 · 1 km 밖 꼭대기 400 m
        var gFar = new GridGround(OX - 300, OY - 300, OX + 1400, OY + 400, 10, (x, y) => 105 + Hill(x, y, OX + 1100, OY + 15, 300, 120));
        var bFar = Budget(gFar, false);
        check("S213 ①먼 산(1 km 밖 400 m)은 예산을 안 올린다(절토 필요 = 부지 곁 5 m)", !bFar.Fallback && Math.Abs(bFar.NeedCut - 5) < 1e-6 && bFar.NeedCut < 290,
              $"{bFar.Note} · 되풀이 {bFar.IterCut}");
        var dFar = DaylightAll(gFar, P(bFar.NeedCut + SpareOf(Math.Max(bFar.NeedCut, bFar.NeedFill), false), 5), true);
        check("S213 ①' 그 예산으로 지은 마지막 링이 땅보다 높다(모든 방향 데이라잇)", dFar.Bad == 0 && dFar.Seen > 0, $"낮은 점 {dFar.Bad}/{dFar.Seen}");

        // ② 닿는 언덕 — 부지 동쪽 60 m에 꼭대기 160 m(부지 곁에서 솟는다)
        var gNear = new GridGround(OX - 400, OY - 400, OX + 600, OY + 450, 10, (x, y) => 102 + Hill(x, y, OX + 100, OY + 15, 58, 45));
        var bNear = Budget(gNear, false);
        double gMaxN = gNear.Tris.Max(t => Math.Max(t.A.Z, Math.Max(t.B.Z, t.C.Z)));
        check("S213 ②닿는 거리 안의 언덕은 예산을 올린다(부지 곁 2 m보다 크고 · 종전 이하)", bNear.NeedCut > 20 && bNear.NeedCut <= gMaxN - 100 + 1e-9,
              $"{bNear.Note} · 되풀이 {bNear.IterCut}");
        var dNear = DaylightAll(gNear, P(bNear.NeedCut + SpareOf(Math.Max(bNear.NeedCut, bNear.NeedFill), false), 5), true);
        check("S213 ②' 언덕 쪽까지 마지막 링이 땅보다 높다(모든 방향 데이라잇)", dNear.Bad == 0 && dNear.Seen > 0, $"낮은 점 {dNear.Bad}/{dNear.Seen}");

        // ③ 성토 거울 — 부지 둘레 96 m · 서쪽 60 m 구덩이 바닥 40 m · 1 km 밖 깊은 골 −300 m
        var gPit = new GridGround(OX - 1400, OY - 400, OX + 400, OY + 450, 10,
            (x, y) => 96 - Hill(x, y, OX - 60, OY + 15, 56, 45) - Hill(x, y, OX - 1100, OY + 15, 396, 120));
        var bPit = Budget(gPit, false);
        double gMinP = gPit.Tris.Min(t => Math.Min(t.A.Z, Math.Min(t.B.Z, t.C.Z)));
        check("S213 ③성토 — 닿는 구덩이만(먼 골은 안 봄 · 종전보다 작음)", bPit.NeedFill > 20 && bPit.NeedFill < 100 - gMinP - 100, $"{bPit.Note}");
        var dPit = DaylightAll(gPit, P(5, bPit.NeedFill + SpareOf(Math.Max(bPit.NeedCut, bPit.NeedFill), false)), false);
        check("S213 ③' 성토 마지막 링이 땅보다 낮다(모든 방향 데이라잇)", dPit.Bad == 0 && dPit.Seen > 0, $"높은 점 {dPit.Bad}/{dPit.Seen}");

        // ④ 대소단(산지) — 같은 언덕 · 여유 규칙과 대소단 폭까지 넣은 거리
        var bTer = Budget(gNear, true);
        var dTer = DaylightAll(gNear, P(bTer.NeedCut + SpareOf(Math.Max(bTer.NeedCut, bTer.NeedFill), true), 5, terrace: true), true);
        check("S213 ④대소단 — 예산 · 모든 방향 데이라잇", !bTer.Fallback && bTer.NeedCut >= bNear.NeedCut - 1e-9 && dTer.Bad == 0 && dTer.Seen > 0,
              $"{bTer.Note} · 낮은 점 {dTer.Bad}/{dTer.Seen}");

        // ⑤ 순수 · 되풀이 끝남 · 종전 넘지 않음
        var bNear2 = Budget(gNear, false);
        check("S213 ⑤같은 입력 같은 답 · 되풀이 64번 안 · 종전 예산 이하", bNear2.NeedCut == bNear.NeedCut && bNear2.NeedFill == bNear.NeedFill
              && bNear.IterCut < 64 && bFar.NeedCut <= 300 && bPit.NeedFill <= 100 - gMinP, $"절토 {bNear.NeedCut:0.###}/{bNear2.NeedCut:0.###} · 되풀이 {bNear.IterCut}");
        var bEmpty = VerticalBudget.Local(pad, new List<WallDaylight.Tri>(), (u, r) => r, d => 10, 77, 33);
        check("S213 ⑤' 세모 없으면 종전 예산으로 물러남", bEmpty.Fallback && bEmpty.NeedCut == 77 && bEmpty.NeedFill == 33, bEmpty.Note);

        // ⑥ 먼 산 탓에 늘던 전이 띠 — 종전 예산(TIN 전체 300 m · 62단)이면 가파름 지킴이 띠를 늘리고, 부지 둘레 예산이면 10 m 띠 하나
        {
            var v0 = GradingGeometry.Build(pad, gFar, P(300, 5), true, null);
            var ruler = v0.Rings[0];
            double tot = GradingGeometry.CumLen2D(ruler)[^1];
            var zone = new SlopeZone { T0 = 40.9, T1 = (40.9 + 100) % tot, Ref = ruler };
            zone.Rules.Add((0, 0.8, 1.0)); zone.Normalize();
            var zs = new List<SlopeZone> { zone };
            ZoneBlend BlendFor(double rise)
            {
                var pp = P(rise, 5);
                return new ZoneBlend(zs, pad, GradingGeometry.CumLen2D(pad), pp, 1.5, 5, 1, true, StepProfile.Build(pp, 1.5, 5, 1, true));
            }
            var eOld = BlendFor(300 + 10).Ends.Where(x => x.Any).ToList();
            double localRise = bFar.NeedCut + SpareOf(Math.Max(bFar.NeedCut, bFar.NeedFill), false);
            var eNew = BlendFor(localRise).Ends.Where(x => x.Any).ToList();
            bool oldStretched = eOld.Count == 2 && eOld.All(x => x.Staged || x.L > 10 + 1e-9);
            bool newPlain = eNew.Count == 2 && eNew.All(x => !x.Staged && Math.Abs(x.L - 10) < 1e-12 && !x.Warn);
            check("S213 ⑥먼 산 탓에 늘던 사면 구간 전이 띠 — 부지 둘레 예산이면 10 m 띠 하나(묶음 · 늘림 · ⚠ 없음)", oldStretched && newPlain,
                  $"종전(310 m) [{string.Join(" ", eOld.Select(x => $"{(x.AtT0 ? "T0" : "T1")} 묶음 {x.Staged} 띠 {x.L:0.##}m"))}] · 둘레({localRise:0.#} m) [{string.Join(" ", eNew.Select(x => $"{(x.AtT0 ? "T0" : "T1")} 묶음 {x.Staged} 띠 {x.L:0.##}m ⚠{x.Warn}"))}]");
        }
    }
}
