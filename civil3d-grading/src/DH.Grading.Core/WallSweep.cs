namespace DH.Grading.Core;

/// <summary>★★★[v106 · 계획_v106.md 2.1] <b>옹벽 3D 스윕 계획</b> — 단면 줄(<see cref="WallExcav.SecLine"/>)을 따라 단면을 세울 정거장 · 덩어리 · 모서리 메움.
/// <para>v105 <c>WallSolidDwg.Populate</c>(Civil)의 규칙을 그대로 Core로 옮겼다(하네스가 지킨다): 변마다 흙 쪽 법선 · 꺾인 점(15° 넘거나 단면 폭 × 각 ≥ 이웃 변 반)은 끊고 모서리 메움 ·
/// 완만한 곡선은 이어 붙이되 그 점 법선 = 두 변의 이등분 · 폭 1/cos(반각)(≤ 1.1) · 표 줄이 바뀌면 변 가운데에서 끊음 · 닫힌 고리 이음매도 메움.</para>
/// <para>무늬(<see cref="WallFacePattern"/>)가 <b>몸통과 같은 정거장 · 법선 · 폭 늘임</b>을 써야 면에서 뜨거나 묻히지 않는다 — 그래서 한 곳에서 짓는다.</para>
/// <para>★[v106 · 계획 2판 2.3 · 검토 높음 2] <b>앞면 맞춤 전단</b> — 단면 앞면이 정지면 벽(1:SlopeW)보다 누우면 그 차 b = max(0, FaceN − SlopeW)만큼 높이에 비례해 앞으로 세운다:
/// x' = x − <see cref="FrontOut"/> − b·z(벽 밑 자리 · 넓이 그대로 · 앞면은 정지면 벽과 나란히 2 cm 앞). 평행 이동은 석축 · 보강토에서 벽 밑이 수십 cm ~ 수 m 앞 허공으로 나가 버렸다.</para></summary>
public static class WallSweep
{
    /// <summary>앞면을 공기 쪽으로 내미는 거리(m) — 지형의 옹벽 면과 겹쳐 깜빡이지 않게(3D 모양만 · 수량과 무관).</summary>
    public const double FrontOut = 0.02;
    /// <summary>한 덩어리로 잇는 꺾임 한계(도).</summary>
    public const double StraightDeg = 1.0;

    /// <summary>꼭대기 꼭짓점을 올리는 높이(m) — 지형 뒷채움 평면과 같은 높이라 InfraWorks에서 깜빡이지 않게(3D 모양만).</summary>
    public const double TopLift = 0.01;

    /// <summary>정거장 — 자리(x, y) · 앞 땅 · 흙 쪽 법선 · 단면 · 폭 늘임 · 앞면 맞춤 전단 b · 바닥 줄 점 번호(가운데 정거장은 앞 점).</summary>
    public readonly record struct Sta(double X, double Y, double Toe, double Nx, double Ny, WallSection.Shape Shape, double Scale, double Lean, int Pt);

    /// <summary>모서리 메움 — 꺾인 점 · 앞 · 뒤 변 법선 · 각(n1 → n2 · 부호) · 그 점 전단 b.</summary>
    public readonly record struct Corner(WallExcav.SecPoint P, double N1x, double N1y, double N2x, double N2y, double Ang, double Lean);

    public sealed class Plan
    {
        /// <summary>덩어리 — 정거장 둘 이상 · 같은 표 줄 · 같은 링 꼴.</summary>
        public List<List<Sta>> Runs = new();
        public List<Corner> Corners = new();
    }

    /// <summary>조각 링 — 구조물 · 온 몫만(재료 · 링).</summary>
    public static List<WallSection.Part> Solids(WallSection.Shape sh) => sh.Parts.Where(p => p.Structure && p.Fraction >= 1 - 1e-12 && p.Ring.Count >= 3).ToList();

    /// <summary>두 단면을 한 덩어리로 이을 수 있나 — 같은 표 줄 · 조각 수 · 재료 · 꼭짓점 수가 같다.</summary>
    public static bool Same(WallExcav.SecPoint a, WallExcav.SecPoint b)
    {
        if (a.Row != b.Row) return false;
        var pa = Solids(a.Shape); var pb = Solids(b.Shape);
        if (pa.Count != pb.Count) return false;
        for (int i = 0; i < pa.Count; i++) if (pa[i].Material != pb[i].Material || pa[i].Ring.Count != pb[i].Ring.Count) return false;
        return true;
    }

    /// <summary>단면의 흙 쪽 · 앞쪽 가장 먼 폭(앞 내밀기 반영) — 꺾인 점에서 단면이 서로 넘어가는지 가른다.</summary>
    static double Reach(WallSection.Shape sh) => Solids(sh).SelectMany(p => p.Ring).Select(q => Math.Abs(q.X - FrontOut)).DefaultIfEmpty(0).Max();

    /// <summary>그 단면의 앞면 맞춤 전단 b = max(0, 보이는 앞면 기울기 − 정지면 벽 기울기). 정지면 벽 기울기를 모르면(음수) 0.</summary>
    public static double LeanOf(WallSection.Shape sh, double slopeW) => slopeW < 0 ? 0 : Math.Max(0, sh.FaceN - slopeW);

    /// <summary>스윕 계획. <paramref name="lean"/> 거짓이면 전단 없음(v105와 같은 결과 — 회귀 견줌).</summary>
    public static Plan Make(WallExcav.SecLine ln, bool lean = true)
    {
        var plan = new Plan();
        var P = ln.Pts;
        int n = P.Count;
        if (n < 2) return plan;
        double Ln(WallSection.Shape sh) => lean ? LeanOf(sh, ln.SlopeW) : 0;
        // 변마다 흙 쪽 법선(왼 법선 × Soil — ColumnAt의 부호 있는 거리와 같은 정의)
        var nx = new double[n - 1]; var ny = new double[n - 1]; var len = new double[n - 1];
        for (int k = 0; k + 1 < n; k++)
        {
            double ex = P[k + 1].X - P[k].X, ey = P[k + 1].Y - P[k].Y, L = Math.Sqrt(ex * ex + ey * ey);
            len[k] = L;
            if (L < 1e-9) { nx[k] = double.NaN; continue; }
            nx[k] = -ey / L * ln.Soil; ny[k] = ex / L * ln.Soil;
        }
        var run = new List<Sta>();
        void Flush()
        {
            if (run.Count >= 2) plan.Runs.Add(run);
            run = new List<Sta>();
        }
        // ★[v105 설치 전 검토 낮음 · 중간] 꺾인 점: 15° 넘거나 단면 폭 × 각이 이웃 변 길이의 반을 넘으면(이어 붙이면 안쪽 점이 서로 넘어가 면이 꼬인다) 끊고 모서리 메움 ·
        //   그 밖(완만한 곡선)은 이어 붙이되 그 점의 법선을 두 변의 이등분으로 · 폭은 1/cos(반각)(평행 오프셋 — 곡선마다 덩어리가 수천 개로 쪼개지지 않게)
        bool IsCorner(int k0, int k1, WallSection.Shape sh, out double ang)
        {
            double cr = nx[k0] * ny[k1] - ny[k0] * nx[k1], dt = nx[k0] * nx[k1] + ny[k0] * ny[k1];
            ang = Math.Atan2(cr, dt);
            double a = Math.Abs(ang);
            if (a * 180 / Math.PI < StraightDeg * 0.25) return false;
            return a * 180 / Math.PI >= 15.0 || Reach(sh) * a >= 0.5 * Math.Min(len[k0], len[k1]);
        }
        int lastK = -1, firstK = -1;
        for (int k = 0; k + 1 < n; k++)
        {
            if (double.IsNaN(nx[k])) continue;
            if (firstK < 0) firstK = k;
            var a = P[k]; var b = P[k + 1];
            if (lastK >= 0)
            {
                if (IsCorner(lastK, k, a.Shape, out double ang))
                {
                    Flush();
                    plan.Corners.Add(new Corner(a, nx[lastK], ny[lastK], nx[k], ny[k], ang, Ln(a.Shape)));
                }
                else if (run.Count > 0 && Math.Abs(run[^1].X - a.X) < 1e-9 && Math.Abs(run[^1].Y - a.Y) < 1e-9)
                {
                    // 이어 붙임 — 그 점 정거장의 법선을 이등분으로 · 폭 늘임
                    double bx = nx[lastK] + nx[k], by = ny[lastK] + ny[k], bl = Math.Sqrt(bx * bx + by * by);
                    if (bl > 1e-9)
                    {
                        double half = Math.Abs(ang) / 2;
                        run[^1] = run[^1] with { Nx = bx / bl, Ny = by / bl, Scale = Math.Min(1.1, 1 / Math.Cos(half)) };
                    }
                }
            }
            if (Same(a, b))
            {
                if (run.Count == 0) run.Add(new Sta(a.X, a.Y, a.Toe, nx[k], ny[k], a.Shape, 1.0, Ln(a.Shape), k));
                run.Add(new Sta(b.X, b.Y, b.Toe, nx[k], ny[k], b.Shape, 1.0, Ln(b.Shape), k + 1));
            }
            else
            {
                // 표 줄이 바뀐다 — 가운데에서 끊는다: 앞 단면으로 가운데까지 · 뒤 단면으로 가운데부터
                double mx = (a.X + b.X) / 2, my = (a.Y + b.Y) / 2, mt = (a.Toe + b.Toe) / 2;
                if (run.Count == 0) run.Add(new Sta(a.X, a.Y, a.Toe, nx[k], ny[k], a.Shape, 1.0, Ln(a.Shape), k));
                run.Add(new Sta(mx, my, mt, nx[k], ny[k], a.Shape, 1.0, Ln(a.Shape), k));
                Flush();
                run.Add(new Sta(mx, my, mt, nx[k], ny[k], b.Shape, 1.0, Ln(b.Shape), k));
                run.Add(new Sta(b.X, b.Y, b.Toe, nx[k], ny[k], b.Shape, 1.0, Ln(b.Shape), k + 1));
            }
            lastK = k;
        }
        Flush();
        // ★[v105 설치 전 검토 낮음] 닫힌 고리(한 바퀴 옹벽 — 첫 점 = 끝 점) 이음매도 꺾인 점이면 메운다
        if (firstK >= 0 && lastK > firstK && Math.Abs(P[0].X - P[n - 1].X) < 1e-6 && Math.Abs(P[0].Y - P[n - 1].Y) < 1e-6)
        {
            double cr = nx[lastK] * ny[firstK] - ny[lastK] * nx[firstK], dt = nx[lastK] * nx[firstK] + ny[lastK] * ny[firstK];
            double ang = Math.Atan2(cr, dt);
            if (Math.Abs(ang) * 180 / Math.PI >= StraightDeg * 0.25) plan.Corners.Add(new Corner(P[0], nx[lastK], ny[lastK], nx[firstK], ny[firstK], ang, Ln(P[0].Shape)));
        }
        return plan;
    }

    /// <summary>단면 높이 z(앞 땅 기준)에서 구조물 가장 앞 x(공기 쪽 끝) — 그 높이를 가로지르는 링 변들 중 최소 x. 없으면 NaN.</summary>
    public static double FrontAt(WallSection.Shape sh, double z)
    {
        double best = double.NaN;
        foreach (var p in Solids(sh))
        {
            var r = p.Ring;
            for (int i = 0; i < r.Count; i++)
            {
                var a = r[i]; var b = r[(i + 1) % r.Count];
                if ((a.Z - z) * (b.Z - z) > 0 || Math.Abs(b.Z - a.Z) < 1e-12) continue;
                double x = a.X + (b.X - a.X) * (z - a.Z) / (b.Z - a.Z);
                if (double.IsNaN(best) || x < best) best = x;
            }
        }
        return best;
    }

    /// <summary>덮기 자가점검 셈 — 표본 점 · 잰 표본 · 지형 벽면이 3D 앞면보다 1 cm 넘게 앞인 표본 · 가장 많이 앞선 거리와 자리 · 못 잰 표본.</summary>
    public sealed class CoverNote
    {
        public int Points, Measured, Ahead, Unknown;
        public double Max;
        public string MaxAt = "";
    }

    /// <summary>★[v106 · 계획 2판 2.3 · 검토 높음 2] <b>지형 벽면 덮기 자가점검</b>(고치지 않고 잰다) — 점마다 보이는 높이의 50 · 90 · 98 % 자리에서 지형(정지면_DH)이 처음 그 높이에 닿는 자리(앞면 법선 · 흙 쪽 +)와
    /// 3D 앞면(구조물 가장 앞 − <see cref="FrontOut"/> − 전단 b·z)을 견준다. 지형이 1 cm 넘게 앞이면 InfraWorks에서 지형 면이 무늬를 가린다.
    /// <para>100 %는 벽 꼭대기 모서리라 뒷채움이 평평하면 교점이 없거나 엉뚱한 자리를 잡는다(검토) — 98 %까지.</para></summary>
    public static void CoverCheck(WallExcav.SecLine ln, Func<double, double, double?> zAt, CoverNote note, bool lean = true)
    {
        var P = ln.Pts;
        int n = P.Count;
        for (int i = 0; i < n; i++)
        {
            var p = P[i];
            if (!(p.H > 0.05)) continue;
            double nx = 0, ny = 0;
            for (int k = Math.Max(0, i - 1); k <= Math.Min(n - 2, i); k++)
            {
                double ex = P[k + 1].X - P[k].X, ey = P[k + 1].Y - P[k].Y, L = Math.Sqrt(ex * ex + ey * ey);
                if (L < 1e-9) continue;
                nx += -ey / L * ln.Soil; ny += ex / L * ln.Soil;
            }
            double nl = Math.Sqrt(nx * nx + ny * ny);
            if (nl < 1e-9) continue;
            nx /= nl; ny /= nl;
            double b = lean ? LeanOf(p.Shape, ln.SlopeW) : 0;
            foreach (double f in new[] { 0.5, 0.9, 0.98 })
            {
                note.Points++;
                double z = Math.Max(0.01, f * p.H), Z = p.Toe + z;
                double xs = FrontAt(p.Shape, z);
                double? xt = double.IsNaN(xs) ? null : FaceX(p.X, p.Y, nx, ny, Z, zAt);
                if (xt == null) { note.Unknown++; continue; }
                note.Measured++;
                double ahead = (xs - FrontOut - b * z) - xt.Value;   // + = 지형 벽면이 3D 앞면보다 앞(공기 쪽)
                if (ahead > 0.01) note.Ahead++;
                if (ahead > note.Max) { note.Max = ahead; note.MaxAt = string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0} ({1:F2},{2:F2}) h {3:F2} @{4:P0}", ln.Src, p.X, p.Y, p.H, f); }
            }
        }
    }

    /// <summary>앞면 법선을 따라 앞(−0.3 m)에서 흙 쪽(+3 m)으로 2 cm씩 걸어 지형이 처음 Z에 닿는 x(이분 1 mm). 앞에서 이미 닿았거나 끝까지 안 닿으면 null.</summary>
    static double? FaceX(double px, double py, double nx, double ny, double Z, Func<double, double, double?> zAt)
    {
        const double X0 = -0.3, X1 = 3.0, Step = 0.02;
        var z0 = zAt(px + nx * X0, py + ny * X0);
        if (z0 == null || z0.Value >= Z) return null;
        double prev = X0;
        for (double x = X0 + Step; x <= X1 + 1e-9; x += Step)
        {
            var z = zAt(px + nx * x, py + ny * x);
            if (z == null) return null;
            if (z.Value >= Z)
            {
                double lo = prev, hi = x;
                for (int it = 0; it < 6; it++)
                {
                    double m = (lo + hi) / 2;
                    var zm = zAt(px + nx * m, py + ny * m);
                    if (zm != null && zm.Value >= Z) hi = m; else lo = m;
                }
                return hi;
            }
            prev = x;
        }
        return null;
    }

    /// <summary>정거장 · 단면 링 점(x, z) → 월드 점(덩어리 · 무늬 · 모서리 메움이 모두 이 식): (X, Y) + N·((x − FrontOut − Lean·z)·Scale), Toe + z
    /// (<paramref name="lift"/>면 꼭대기 꼭짓점을 <see cref="TopLift"/>만큼 올림 — 몸통만).</summary>
    public static Point3 World(in Sta s, double x, double z, bool lift = false)
    {
        double xx = (x - FrontOut - s.Lean * z) * s.Scale;
        double zz = lift && z >= s.Shape.ZTop - 1e-9 ? z + TopLift : z;
        return new Point3(s.X + s.Nx * xx, s.Y + s.Ny * xx, s.Toe + zz);
    }

    /// <summary>전단 ⚠ 문턱(m) — 단면 앞면을 정지면 벽에 맞춰 세운 몫(b·h)이 이보다 크면 팝업 ⚠(보강토 1:0.05 vs 정지면 1:0.01은 5 m에서 0.2 m — 흔해서 로그 글만 · V52).</summary>
    public const double LeanWarn = 0.30;

    /// <summary>전단 셈 — 정거장 b·h 최대(m)와 그 자리 · <see cref="LeanWarn"/> 넘는 정거장 수(팝업 ⚠).</summary>
    public static (double Max, string At, int Over) LeanReport(Plan plan, string src)
    {
        double mx = 0; string at = ""; int over = 0;
        foreach (var run in plan.Runs)
            foreach (var s in run)
            {
                double d = s.Lean * Math.Max(0, s.Shape.ZTop);
                if (d > LeanWarn) over++;
                if (d > mx) { mx = d; at = string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0} ({1:F2},{2:F2}) 단면 1:{3:0.###}", src, s.X, s.Y, s.Shape.FaceN); }
            }
        return (mx, at, over);
    }
}
