namespace DH.Grading.Core;

/// <summary>★★★[v113.3 · 계획 2026-11-05_도로평가기_연속 · 검토 H1 · H2 · M3 · M4 · M5 · M6 · L7 · L9 · L10] <b>주름 원천 감싸기</b> — 점 평가기가 끊기는 자리(굽은 안쪽에서 가장 가까운 측점이 곡선 중심을 건너 뜀)에서
/// 두 쪽 극한 중 높은(성토 lo) · 낮은(절토 hi) 값을 원천으로 기울기 K = √(k² + g²) 원뿔을 세워 lo' = max(lo, C⁺) · hi' = min(hi, C⁻).
/// 원천 B에서 C⁺(B) ≥ max(lo_A, lo_B)라 두 쪽 모두 C⁺(B)로 같아져 끊김이 없어지고, C⁺는 원뿔의 최댓값이라 이어진다(원천 마디 사이는 마디 원뿔 · 현 오차 1 mm까지 쪼갬).
/// <para>꼭짓점 이등분선 계단 · 끝 모서리 끊김은 원인 자리에서 고쳤다(매끈한 측점 · 마구리 모서리 표본 — RoadGrade.cs) — 감싸기는 남은 큰 주름만.</para>
/// <para>자격 밖(벽 면 단면 · 옛 가장자리 옹벽 뒤 · 폴리선 옹벽 칸)은 원천도 대상도 아님 — 셈만(EnvWallSkip · EnvClip).</para></summary>
public static partial class RoadGrade
{
    /// <summary>하네스 전용 — 감싸기를 끈 옛 길(원천 안 만듦 · 흘러내림 켬). ★[검토 L10] internal static이라 출하 DLL에서 되돌리려면 다시 빌드해야 한다.</summary>
    internal static bool EnvOff;

    /// <summary>하네스 비교용 — 이긴 표본 꼴 바뀜 신호(발 표본 · 다른 다리 · 반평면). 원천 자리에서 원뿔 식을 이어 붙인 뒤(A안 ③ ④)로는 끊김을 안 만들어 꺼 둠(속도).</summary>
    internal static bool UseSwitch;

    /// <summary>★[v113.3] F(안쪽 값)를 위해 감싸기를 보는 띠 — 감싸기가 땅 · 반대쪽 원뿔보다 이만큼 넘게 아래(성토) · 위(절토)면 안 봄. 높이(Z)는 늘 그대로(그 자리 감싸기는 Z를 못 바꿈) — F 크기만 그 밖에서 옛 값.</summary>
    const double EnvF = 2.0;

    /// <summary>원천 마디 묶음 — 성토(lo · 높은 극한) · 절토(hi · 낮은 극한) 따로 · 4 m 칸 색인(칸마다 가장 높은(성토) · 낮은(절토) 원천 높이 · 가장 무른 K).</summary>
    internal sealed class EnvSet
    {
        public readonly List<(double X0, double Y0, double Z0, double X1, double Y1, double Z1, double K)> Lo = new(), Hi = new();
        Grid? _gl, _gh;
        public int Count => Lo.Count + Hi.Count;

        /// <summary>★[v113.3 · 속도] 원천 칸 색인 — 4 m 칸(배열) + 16 m 묶음(칸 최고 · 가장 무른 K) · 성토는 그대로 · 절토는 높이 부호를 뒤집어 같은 꼴(최댓값).</summary>
        sealed class Grid
        {
            const double C = 4.0; const int B = 4;   // 묶음 = B × B 칸
            readonly double _x0, _y0; readonly int _nx, _ny, _mx, _my;
            readonly List<int>?[] _cell; readonly double[] _cz, _ck, _bz, _bk;
            readonly double[] X0, Y0, Z0, X1, Y1, Z1, K;
            public readonly double ZMax, KMin, Bx0, By0, Bx1, By1;
            public Grid(List<(double X0, double Y0, double Z0, double X1, double Y1, double Z1, double K)> l, bool lo)
            {
                int n = l.Count; X0 = new double[n]; Y0 = new double[n]; Z0 = new double[n]; X1 = new double[n]; Y1 = new double[n]; Z1 = new double[n]; K = new double[n];
                double bx0 = double.MaxValue, by0 = double.MaxValue, bx1 = double.MinValue, by1 = double.MinValue, zm = double.NegativeInfinity, km = double.PositiveInfinity;
                for (int i = 0; i < n; i++)
                {
                    var s = l[i];
                    X0[i] = s.X0; Y0[i] = s.Y0; X1[i] = s.X1; Y1[i] = s.Y1; K[i] = s.K;
                    Z0[i] = lo ? s.Z0 : -s.Z0; Z1[i] = lo ? s.Z1 : -s.Z1;
                    bx0 = Math.Min(bx0, Math.Min(s.X0, s.X1)); bx1 = Math.Max(bx1, Math.Max(s.X0, s.X1)); by0 = Math.Min(by0, Math.Min(s.Y0, s.Y1)); by1 = Math.Max(by1, Math.Max(s.Y0, s.Y1));
                    zm = Math.Max(zm, Math.Max(Z0[i], Z1[i])); km = Math.Min(km, s.K);
                }
                Bx0 = bx0; By0 = by0; Bx1 = bx1; By1 = by1; ZMax = zm; KMin = km;
                _x0 = bx0; _y0 = by0;
                _nx = Math.Max(1, (int)Math.Floor((bx1 - bx0) / C) + 1); _ny = Math.Max(1, (int)Math.Floor((by1 - by0) / C) + 1);
                _mx = (_nx + B - 1) / B; _my = (_ny + B - 1) / B;
                _cell = new List<int>?[_nx * _ny]; _cz = new double[_nx * _ny]; _ck = new double[_nx * _ny]; _bz = new double[_mx * _my]; _bk = new double[_mx * _my];
                Array.Fill(_cz, double.NegativeInfinity); Array.Fill(_ck, double.PositiveInfinity); Array.Fill(_bz, double.NegativeInfinity); Array.Fill(_bk, double.PositiveInfinity);
                for (int i = 0; i < n; i++)
                {
                    double z = Math.Max(Z0[i], Z1[i]);
                    int a0 = Ix(Math.Min(X0[i], X1[i])), a1 = Ix(Math.Max(X0[i], X1[i])), b0 = Iy(Math.Min(Y0[i], Y1[i])), b1 = Iy(Math.Max(Y0[i], Y1[i]));
                    for (int a = a0; a <= a1; a++)
                        for (int b = b0; b <= b1; b++)
                        {
                            int c = b * _nx + a;
                            (_cell[c] ??= new()).Add(i);
                            _cz[c] = Math.Max(_cz[c], z); _ck[c] = Math.Min(_ck[c], K[i]);
                            int q = (b / B) * _mx + a / B;
                            _bz[q] = Math.Max(_bz[q], z); _bk[q] = Math.Min(_bk[q], K[i]);
                        }
                }
            }
            int Ix(double x) => Math.Clamp((int)Math.Floor((x - _x0) / C), 0, _nx - 1);
            int Iy(double y) => Math.Clamp((int)Math.Floor((y - _y0) / C), 0, _ny - 1);
            static double Gap(double v, double lo, double hi) => v < lo ? lo - v : v > hi ? v - hi : 0;
            /// <summary>max SegCone over sources whose cone can exceed thr — thr를 넘는 값만(못 넘으면 −∞).</summary>
            public double At(double x, double y, double thr)
            {
                if (!(ZMax > thr)) return double.NegativeInfinity;
                double R = (ZMax - thr) / KMin;
                if (x < Bx0 - R || x > Bx1 + R || y < By0 - R || y > By1 + R) return double.NegativeInfinity;
                int a0 = Ix(x - R), a1 = Ix(x + R), b0 = Iy(y - R), b1 = Iy(y + R);
                double best = thr;
                for (int qb = b0 / B; qb <= b1 / B; qb++)
                    for (int qa = a0 / B; qa <= a1 / B; qa++)
                    {
                        int q = qb * _mx + qa;
                        if (!(_bz[q] > best)) continue;
                        double gx = Gap(x, _x0 + qa * B * C, _x0 + (qa + 1) * B * C), gy = Gap(y, _y0 + qb * B * C, _y0 + (qb + 1) * B * C);
                        if (_bz[q] - _bk[q] * Math.Sqrt(gx * gx + gy * gy) <= best) continue;
                        for (int b = Math.Max(b0, qb * B); b <= Math.Min(b1, qb * B + B - 1); b++)
                            for (int a = Math.Max(a0, qa * B); a <= Math.Min(a1, qa * B + B - 1); a++)
                            {
                                int c = b * _nx + a; var li = _cell[c];
                                if (li == null) continue;
                                double cx = Gap(x, _x0 + a * C, _x0 + (a + 1) * C), cy = Gap(y, _y0 + b * C, _y0 + (b + 1) * C);
                                if (_cz[c] - _ck[c] * Math.Sqrt(cx * cx + cy * cy) <= best) continue;
                                foreach (int i in li) { double v = SegCone(x, y, X0[i], Y0[i], Z0[i], X1[i], Y1[i], Z1[i], K[i]); if (v > best) best = v; }
                            }
                    }
                return best > thr ? best : double.NegativeInfinity;
            }
        }

        public void Build() { _gl = Lo.Count > 0 ? new Grid(Lo, true) : null; _gh = Hi.Count > 0 ? new Grid(Hi, false) : null; }

        /// <summary>성토 감싸기 C⁺(x, y) — <paramref name="thr"/>를 넘는 값만(못 넘으면 −∞). 칸 가지치기: 칸 최고 높이 − 가장 무른 K × 칸까지 거리 ≤ 지금 값이면 건너뜀(같은 답).</summary>
        public double LoAt(double x, double y, double thr) => _gl == null ? double.NegativeInfinity : _gl.At(x, y, thr);

        /// <summary>절토 감싸기 C⁻(x, y) — <paramref name="thr"/>보다 낮은 값만(못 내려가면 +∞).</summary>
        public double HiAt(double x, double y, double thr) => _gh == null ? double.PositiveInfinity : -_gh.At(x, y, -thr);
    }

    /// <summary>원천 점 — 자리 · 성토(높은 극한 · K) · 절토(낮은 극한 · K) · 없으면 NaN.</summary>
    struct CreasePt { public double X, Y, ZLo, KLo, ZHi, KHi, JLo, JHi; public bool Fs; }

    /// <summary>★[v113.3] 원천 찾기에 쓰는 칸(마디) 값 — 끝 식 전 lo · hi · 땅 · 측점 · 쪽(끝 너머 0) · 같은 다리 판정 바탕 · 이긴 표본 · 자격 · 꼴 표지.</summary>
    internal struct ENode
    {
        public double Lo, Hi, Zb; public float Fs, Nx, Ny, Th; public sbyte Sd, Fsd; public byte Fl; public int KLo, KHi; public bool Ok;
        /// <summary>표지 — 1 성토 자격 · 2 절토 자격 · 4 성토 발 표본 · 8 절토 발 표본 · 16 성토 이긴 표본 다른 다리 · 32 절토 이긴 표본 다른 다리.</summary>
        public static ENode Of(in PointEval p) => !p.Raw ? default : new ENode
        {
            Lo = p.Lo, Hi = p.Hi, Zb = p.Zb, Fs = p.FS, Nx = p.Fnx, Ny = p.Fny, Th = p.Fth, Sd = p.FSide, Fsd = p.Fsd, KLo = p.KLo, KHi = p.KHi, Ok = p.HasBase,
            Fl = (byte)((p.EligLo ? 1 : 0) | (p.EligHi ? 2 : 0) | (p.KLo == -2 ? 4 : 0) | (p.KHi == -2 ? 8 : 0) | (p.OLLo ? 16 : 0) | (p.OLHi ? 32 : 0)),
        };
    }

    /// <summary>★★[v113.3 · 계획 §2 · 검토 M5 · M6] 주름 원천 만들기.
    /// <para>① 1판 격자 이웃 쌍 신호 — 측점 뜀(칸 + 1 m) · 끝 너머 바뀜 · 이긴 표본 꼴(발 표본 · 다른 다리) 바뀜 · <b>이긴 표본이 이웃 칸에서 다른 꼴</b>(반평면 밖 · 같은 다리 판정이 뒤집힘 — 그 원뿔 식이 바뀌는 자리) ·
    /// 립시츠 한도(k·칸 + g·max(칸, 측점 차) + 2 mm)를 넘는 lo · hi 차 → 1e-5 m까지 이분(끊김 = 1 mm 넘게 남음).</para>
    /// <para>② 원천 값 = 두 극한 중 높은(성토) · 낮은(절토) 값 · 그 측점 직각 단면 높이(광선과 같은 값) · 땅보다 높은(성토) · 낮은(절토) 쪽만(검토 M4) · 두 극한이 다 자격일 때만.</para>
    /// <para>③ 격자 네모마다 이음 — 변 위 끊김 점이 둘이면 마디 · 셋 넘으면(갈래 · 곡선 중심 — 검토 M5) 그 네모를 8 × 8로 다시 나눠 같은 일을(깊이 3 · 1 mm 네모) · 가장 깊은 갈래는 둘레 16점 극한으로 점 원천.</para>
    /// <para>④ 마디 가운데에서 직각 이분으로 진짜 주름 자리를 다시 찾아 (값 보간 오차 + K × 처짐) &gt; 1 mm면 쪼갬(깊이 ≤ 10).</para></summary>
    internal static EnvSet BuildCreases(Input inp, Result r, Func<double, double, PointEval> raw, ENode[] gN, List<Edge> edges,
        Func<double, int, double, double, bool, double> secZ, Func<double, double> grade, out (int Cand, int Pts, int WallSkip, long Evals, int PerpFail) stat)
    {
        int W = r.W, H = r.H; double cell = r.Cell;
        double kF = 1.0 / Math.Max(1e-6, inp.Fill.Slope), kC = 1.0 / Math.Max(1e-6, inp.Cut.Slope);
        double gMax = 0;
        for (int i = 0; i + 1 < inp.Z.Length; i++) { double l = Math.Sqrt(Sq(inp.X[i + 1] - inp.X[i]) + Sq(inp.Y[i + 1] - inp.Y[i])); if (l > 1e-9) gMax = Math.Max(gMax, Math.Abs(inp.Z[i + 1] - inp.Z[i]) / l); }
        int wallSkip = 0, cand = 0, nPts = 0, perpFail0 = 0;   // perpFail0 = 첫 쪼개기에서 가운데 주름을 못 찾아 두 점 원천으로 바꾼 마디(검토 질문 6 — 셈)
        // ★[v113.3 · 속도] 평가 수는 실마다 따로 세어 끝에 더함(모든 실이 한 칸을 Interlocked로 치면 캐시 줄 다툼으로 병렬이 거의 안 됐다)
        using var evTl = new System.Threading.ThreadLocal<long[]>(() => new long[1], true);
        long Evals() { long t = 0; foreach (var v in evTl.Values) t += v[0]; return t; }
        static PointEval FromNode(in ENode n) => new() { Raw = n.Ok, HasBase = n.Ok, Lo = n.Lo, Hi = n.Hi, Zb = n.Zb, FS = n.Fs, FSide = n.Sd, Fsd = n.Fsd, Fnx = n.Nx, Fny = n.Ny, Fth = n.Th, KLo = n.KLo, KHi = n.KHi, EligLo = (n.Fl & 1) != 0, EligHi = (n.Fl & 2) != 0, OLLo = (n.Fl & 16) != 0, OLHi = (n.Fl & 32) != 0 };
        PointEval Raw(double x, double y) { evTl.Value![0]++; return raw(x, y); }
        // 이긴 표본 k(마디 a에서 다른 다리 = olA)가 마디 b 자리에서 꼴이 바뀌나 — 반평면 밖 · 같은 다리 판정이 뒤집힘
        bool Switch(int k, bool olA, in ENode b, double x, double y)
        {
            if (k < 0 || k >= edges.Count) return false;
            var e = edges[k];
            double rx = x - e.X, ry = y - e.Y;
            if (rx * e.Nx + ry * e.Ny < -1e-9) return true;
            double dist = Math.Sqrt(rx * rx + ry * ry), ds = b.Fs - e.S;
            bool same = Math.Abs(ds) <= dist + 1.0 || (e.Side != 0 && e.Side == b.Fsd && b.Nx * e.Nx + b.Ny * e.Ny > 0.17 && Math.Abs(b.Th - e.Th) <= 1.75);
            return same == olA;
        }
        (bool Lo, bool Hi) Signal(in ENode a, in ENode b, double ax, double ay, double bx, double by, double h)
        {
            if (!a.Ok || !b.Ok) return (false, false);
            double dfs = Math.Abs(a.Fs - b.Fs);
            bool s1 = !(dfs <= h + 1.0) || a.Sd != b.Sd;
            double gpad = gMax * Math.Max(h, double.IsFinite(dfs) ? dfs : h) + 0.002;
            // 땅 위(성토) · 아래(절토) 쪽만(검토 M4 — 원천 값에서 다시 잼) — 칸 가운데 값이 땅 곁이면 쌍 사이 주름 자리에서는 땅을 넘을 수 있어 칸 기울기만큼 넉넉히
            double zbm = Math.Min(a.Zb, b.Zb), zbM = Math.Max(a.Zb, b.Zb);
            bool lo = double.IsFinite(a.Lo) && double.IsFinite(b.Lo) && Math.Max(a.Lo, b.Lo) > zbm - kF * h - gpad && (a.Fl & b.Fl & 1) != 0
                && (s1 || Math.Abs(a.Lo - b.Lo) > kF * h + gpad || (UseSwitch && (((a.Fl ^ b.Fl) & (4 | 16)) != 0 || Switch(a.KLo, (a.Fl & 16) != 0, b, bx, by) || Switch(b.KLo, (b.Fl & 16) != 0, a, ax, ay))));
            bool hi = double.IsFinite(a.Hi) && double.IsFinite(b.Hi) && Math.Min(a.Hi, b.Hi) < zbM + kC * h + gpad && (a.Fl & b.Fl & 2) != 0
                && (s1 || Math.Abs(a.Hi - b.Hi) > kC * h + gpad || (UseSwitch && (((a.Fl ^ b.Fl) & (8 | 32)) != 0 || Switch(a.KHi, (a.Fl & 32) != 0, b, bx, by) || Switch(b.KHi, (b.Fl & 32) != 0, a, ax, ay))));
            // 자격 밖 쌍의 큰 차는 셈만(옹벽 · 계단선 · 폴리선 옹벽 — 덮지 않음)
            if ((a.Fl & b.Fl & 3) != 3 && (s1 || Math.Abs(a.Lo - b.Lo) > kF * h + gpad || Math.Abs(a.Hi - b.Hi) > kC * h + gpad)) System.Threading.Interlocked.Increment(ref wallSkip);
            return (lo, hi);
        }
        double E(in PointEval p, in PointEval q, bool lo, bool hi)
        {
            double e = 0;
            if (lo && double.IsFinite(p.Lo) && double.IsFinite(q.Lo)) e = Math.Max(e, Math.Abs(p.Lo - q.Lo));
            if (hi && double.IsFinite(p.Hi) && double.IsFinite(q.Hi)) e = Math.Max(e, Math.Abs(p.Hi - q.Hi));
            return e;
        }
        // 끊김 자리 이분 — 두 점 사이에서 lo(성토) · hi(절토) 차가 큰 쪽 반을 1e-5 m까지(곡선 중심 곁 가파른 연속 변화를 끊김으로 안 보게) · 끝 차 > 1 mm면 끊김
        // 측점 뜀 자리 이분(측점 차가 큰 쪽 반 · 1e-5 m) — 다리가 바뀌는 주름은 높이 차가 0 곁인 토막도 따라가게(높이 이분은 거기서 놓침)
        bool LocateFs(double ax, double ay, PointEval pa, double bx, double by, PointEval pb, out double mx, out double my, out PointEval la, out PointEval lb)
        {
            mx = my = 0; la = pa; lb = pb;
            int it = 0;
            while (Sq(ax - bx) + Sq(ay - by) > 1e-8 && it++ < 48)   // 0.1 mm — 자리 오차 × K ≪ 1 mm(측점 뜀은 높이 이분과 달리 가파른 연속 변화를 끊김으로 오인하지 않음)
            {
                double cx = (ax + bx) / 2, cy = (ay + by) / 2;
                var pc = Raw(cx, cy);
                if (!pc.Raw) return false;
                if (Math.Abs(pa.FS - pc.FS) >= Math.Abs(pc.FS - pb.FS)) { bx = cx; by = cy; pb = pc; } else { ax = cx; ay = cy; pa = pc; }
            }
            mx = (ax + bx) / 2; my = (ay + by) / 2; la = pa; lb = pb;
            return Math.Abs(pa.FS - pb.FS) > 0.5;
        }
        bool Locate(double ax, double ay, PointEval pa, double bx, double by, PointEval pb, bool lo, bool hi, out double mx, out double my, out PointEval la, out PointEval lb)
            => Locate2(ax, ay, pa, bx, by, pb, lo, hi, out mx, out my, out la, out lb, out _);
        // hit = 사이 점이 사면 칸이 아님(도로 띠 · 바탕 없음) — 그 자리(mx, my)를 돌려 부르는 쪽이 두 토막으로 다시
        bool Locate2(double ax, double ay, PointEval pa, double bx, double by, PointEval pb, bool lo, bool hi, out double mx, out double my, out PointEval la, out PointEval lb, out bool hit, double stop2 = 1e-10)
        {
            mx = my = 0; la = pa; lb = pb; hit = false;
            int it = 0;
            while (Sq(ax - bx) + Sq(ay - by) > stop2 && it++ < 48)
            {
                double cx = (ax + bx) / 2, cy = (ay + by) / 2;
                var pc = Raw(cx, cy);
                if (!pc.Raw) { mx = cx; my = cy; hit = true; return false; }
                if (E(pa, pc, lo, hi) >= E(pc, pb, lo, hi)) { bx = cx; by = cy; pb = pc; } else { ax = cx; ay = cy; pa = pc; }
            }
            mx = (ax + bx) / 2; my = (ay + by) / 2; la = pa; lb = pb;
            return E(pa, pb, lo, hi) > 1e-3;
        }
        // 원천 값 — 극한들 중 높은(성토) · 낮은(절토) 값 · 그 측점 직각 단면 높이 · 자격 · 땅 위(성토) · 아래(절토)만
        CreasePt Value(double x, double y, List<PointEval> ps, bool lo, bool hi)
        {
            var c = new CreasePt { X = x, Y = y, ZLo = double.NaN, ZHi = double.NaN, KLo = double.NaN, KHi = double.NaN };
            if (!inp.Base.TryGetElevation(x, y, out double zb)) return c;
            bool allLo = true, allHi = true; foreach (var p in ps) { allLo &= p.EligLo; allHi &= p.EligHi; }
            if (lo && allLo)
            {
                double z = double.NegativeInfinity; PointEval w = ps[0];
                foreach (var p in ps) if (double.IsFinite(p.Lo) && p.Lo > z) { z = p.Lo; w = p; }
                foreach (var p in ps) { double sz = secZ(p.FS, p.FSide, x, y, false); if (double.IsFinite(sz) && sz > z) z = sz; }
                if (z > zb) { c.ZLo = z; c.KLo = Math.Sqrt(kF * kF + Sq(grade(w.FS))); double mn = z; foreach (var p in ps) if (double.IsFinite(p.Lo) && p.Lo < mn) mn = p.Lo; c.JLo = z - mn; }
            }
            if (hi && allHi)
            {
                double z = double.PositiveInfinity; PointEval w = ps[0];
                foreach (var p in ps) if (double.IsFinite(p.Hi) && p.Hi < z) { z = p.Hi; w = p; }
                foreach (var p in ps) { double sz = secZ(p.FS, p.FSide, x, y, true); if (double.IsFinite(sz) && sz < z) z = sz; }
                if (z < zb) { c.ZHi = z; c.KHi = Math.Sqrt(kC * kC + Sq(grade(w.FS))); double mx = z; foreach (var p in ps) if (double.IsFinite(p.Hi) && p.Hi > mx) mx = p.Hi; c.JHi = mx - z; }
            }
            return c;
        }
        var segL = new System.Collections.Concurrent.ConcurrentBag<(CreasePt A, CreasePt B)>(); var segH = new System.Collections.Concurrent.ConcurrentBag<(CreasePt A, CreasePt B)>();
        var lone = new System.Collections.Concurrent.ConcurrentBag<CreasePt>();
        const int Sub = 8, MaxDepth = 3; const long HB = 1L << 62, SB = 1L << 52, KM = SB - 1;
        // 마디 격자 한 판 — nx × ny 마디 · 자리 P(i, j) · 값 N(i, j) · 간격 h · 깊이
        void Stage(int nx, int ny, Func<int, int, (double X, double Y)> P, Func<int, int, ENode> N, double h, int depth)
        {
            var pts = new System.Collections.Concurrent.ConcurrentDictionary<long, CreasePt>();
            // ★[v113.3 · 속도] 신호는 먼저 한 번에 훑어 모으고(가벼움) · 신호 선 쌍만 고르게 나눠 병렬로 이분(주름 줄에 몰린 쌍이 한 실에 쌓이지 않게) · 쌍 끝 값은 1판 칸 값을 그대로(다시 안 셈)
            void Pair(int i, int j, int ii, int jj, long key, bool lo, bool hi)
            {
                var na = N(i, j); var nb = N(ii, jj);
                var (pa0, pb0) = (P(i, j), P(ii, jj));
                System.Threading.Interlocked.Increment(ref cand);
                var pa = FromNode(na); var pb = FromNode(nb);
                // 성토(lo) · 절토(hi) 따로 이분 — 한쪽의 가파른 연속 변화가 다른 쪽 끊김을 가리지 않게 · 절토 점은 다른 열쇠(HB) ·
                // 한 쌍 안에 끊김이 여럿이면(두 주름이 한 칸 안) 찾은 점 양쪽 남은 토막도 신호가 서면 다시(점마다 번호 SB)
                // 측점 뜀(다리 바뀜) — 높이 차와 상관없이 그 주름 자리에 점(높이 차가 0 곁인 토막에서도 사슬이 이어지게 · 쪼개기가 사이 높이 차를 잼)
                if (!(Math.Abs(na.Fs - nb.Fs) <= h + 1.0) && LocateFs(pa0.X, pa0.Y, pa, pb0.X, pb0.Y, pb, out double fx, out double fy, out var fa, out var fb))
                {
                    foreach (bool wl in new[] { true, false })
                    {
                        if (wl ? !lo : !hi) continue;
                        var c0 = Value(fx, fy, new List<PointEval> { fa, fb }, wl, !wl);
                        c0.Fs = true;
                        if (double.IsFinite(wl ? c0.ZLo : c0.ZHi)) { pts[wl ? key : key | HB] = c0; System.Threading.Interlocked.Increment(ref nPts); }
                    }
                    return;
                }
                foreach (bool wl in new[] { true, false })
                {
                    if (wl ? !lo : !hi) continue;
                    int idx = 0;
                    void Find(double ax, double ay, PointEval qa, double bx, double by, PointEval qb, int d)
                    {
                        double mx = 0, my = 0; PointEval la = qa, lb = qb; bool hit = false;
                        bool okL = idx < 64 && Locate2(ax, ay, qa, bx, by, qb, wl, !wl, out mx, out my, out la, out lb, out hit);
                        if (hit && d < 6)
                        {
                            // 사이에 도로 띠 — 양쪽 사면 칸 끝(이분)까지 줄여 두 토막으로
                            (double X, double Y, PointEval P)? Edge(double x0, double y0, PointEval p0, double x1, double y1)
                            {
                                for (int q = 0; q < 30 && Sq(x1 - x0) + Sq(y1 - y0) > 1e-10; q++) { double cx = (x0 + x1) / 2, cy = (y0 + y1) / 2; var pc = Raw(cx, cy); if (pc.Raw) { x0 = cx; y0 = cy; p0 = pc; } else { x1 = cx; y1 = cy; } }
                                return (x0, y0, p0);
                            }
                            var ea = Edge(ax, ay, qa, mx, my); var eb = Edge(bx, by, qb, mx, my);
                            if (ea is { } u && Sq(u.X - ax) + Sq(u.Y - ay) > 1e-8) Find(ax, ay, qa, u.X, u.Y, u.P, d + 1);
                            if (eb is { } v && Sq(v.X - bx) + Sq(v.Y - by) > 1e-8) Find(v.X, v.Y, v.P, bx, by, qb, d + 1);
                            return;
                        }
                        if (!okL) return;
                        var c1 = Value(mx, my, new List<PointEval> { la, lb }, wl, !wl);
                        if (double.IsFinite(wl ? c1.ZLo : c1.ZHi)) { pts[(wl ? key : key | HB) + idx * SB] = c1; idx++; System.Threading.Interlocked.Increment(ref nPts); }
                    }
                    Find(pa0.X, pa0.Y, pa, pb0.X, pb0.Y, pb, 0);
                }
            }
            var flagged = new System.Collections.Concurrent.ConcurrentBag<(int I, int J, int II, int JJ, long K, bool Lo, bool Hi)>();
            void Row(int j)
            {
                for (int i = 0; i < nx; i++)
                    for (int dir = 0; dir < 2; dir++)
                    {
                        int ii = i + (dir == 0 ? 1 : 0), jj = j + (dir == 1 ? 1 : 0);
                        if (ii >= nx || jj >= ny) continue;
                        var na = N(i, j); var nb = N(ii, jj);
                        if (!na.Ok || !nb.Ok) continue;
                        var (pa0, pb0) = (P(i, j), P(ii, jj));
                        var (lo, hi) = Signal(na, nb, pa0.X, pa0.Y, pb0.X, pb0.Y, h);
                        if (lo || hi) flagged.Add((i, j, ii, jj, 2L * (j * (long)nx + i) + dir, lo, hi));
                    }
            }
            if (depth == 0) System.Threading.Tasks.Parallel.For(0, ny, Row); else for (int j = 0; j < ny; j++) Row(j);
            System.Threading.Tasks.Parallel.ForEach(flagged, f => Pair(f.I, f.J, f.II, f.JJ, f.K, f.Lo, f.Hi));
            if (pts.IsEmpty) return;
            var used = new HashSet<long>();
            var deeper = new List<(int I, int J)>();
            // 점이 걸린 네모만(점 하나마다 곁 네모 둘)
            var sq = new HashSet<(int, int)>();
            foreach (long k0 in pts.Keys)
            {
                long k = k0 & KM, c = k >> 1; int i = (int)(c % nx), j = (int)(c / nx);
                if ((k & 1) == 0) { sq.Add((i, j)); sq.Add((i, j - 1)); } else { sq.Add((i, j)); sq.Add((i - 1, j)); }
            }
            foreach (var (i, j) in sq)
            {
                if (i < 0 || j < 0 || i + 1 >= nx || j + 1 >= ny) continue;
                var ks = new[] { 2L * (j * (long)nx + i), 2L * ((j + 1) * (long)nx + i), 2L * (j * (long)nx + i) + 1, 2L * (j * (long)nx + i + 1) + 1 };
                var got = new List<(long K, CreasePt P)>();
                foreach (long k in ks)
                    foreach (long t in new[] { k, k | HB })
                        for (long q = 0; pts.TryGetValue(t + q * SB, out var p); q++) got.Add((t + q * SB, p));
                if (got.Count < 2) continue;
                bool split = false;
                foreach (bool lo in new[] { true, false })
                {
                    var g = got.Where(q => double.IsFinite(lo ? q.P.ZLo : q.P.ZHi)).ToList();
                    if (g.Count == 2) { (lo ? segL : segH).Add((g[0].P, g[1].P)); used.Add(g[0].K); used.Add(g[1].K); }
                    else if (g.Count > 2) split = true;
                }
                if (split) { deeper.Add((i, j)); foreach (var q in got) used.Add(q.K); }
            }
            foreach (var kv in pts) if (!used.Contains(kv.Key)) lone.Add(kv.Value);
            // 갈래 네모 — 8 × 8로 다시(가장 깊으면 둘레 16점 극한으로 점 원천)
            foreach (var (i, j) in deeper)
            {
                var a = P(i, j); var b = P(i + 1, j + 1);
                double x0 = a.X, y0 = a.Y, hx = (b.X - a.X) / Sub, hy = (b.Y - a.Y) / Sub;
                if (depth + 1 > MaxDepth)
                {
                    double cx = (a.X + b.X) / 2, cy = (a.Y + b.Y) / 2, rr = 0.5 * Math.Sqrt(Sq(b.X - a.X) + Sq(b.Y - a.Y));
                    var ps = new List<PointEval>();
                    for (int q = 0; q < 16; q++) { var p = Raw(cx + rr * Math.Cos(q * Math.PI / 8), cy + rr * Math.Sin(q * Math.PI / 8)); if (p.Raw) ps.Add(p); }
                    if (ps.Count == 0) continue;
                    var c = Value(cx, cy, ps, true, true);
                    if (double.IsFinite(c.ZLo) || double.IsFinite(c.ZHi)) lone.Add(c);
                    continue;
                }
                var fn = new ENode[(Sub + 1) * (Sub + 1)];
                System.Threading.Tasks.Parallel.For(0, fn.Length, q => fn[q] = ENode.Of(Raw(x0 + (q % (Sub + 1)) * hx, y0 + (q / (Sub + 1)) * hy)));
                Stage(Sub + 1, Sub + 1, (ii, jj) => (x0 + ii * hx, y0 + jj * hy), (ii, jj) => fn[jj * (Sub + 1) + ii], Math.Max(Math.Abs(hx), Math.Abs(hy)), depth + 1);
            }
        }
        Stage(W, H, (i, j) => (r.X0 + (i + 0.5) * cell, r.Y0 + (j + 0.5) * cell), (i, j) => gN[j * W + i], cell, 0);
        // ③′ 곡선 중심(이웃 꼭짓점 법선이 만나는 자리) — 모든 측점 법선이 모여 측점이 곡선 끝에서 끝까지 도는 특이점(곁 기울기 = 경사 × R / 거리 → 칸 신호 · 이분이 못 가름).
        //   중심 둘레 2 m 네모를 16 × 16(12.5 cm)으로 나눠 같은 일(갈래면 더 깊이 — 가장 깊은 네모 2 mm)
        {
            var cl = new CenterLine(inp.X, inp.Y, inp.Z);
            var foc = new List<(double X, double Y)>();
            double lim = inp.Reach + inp.MaxOuter;
            for (int i = 0; i + 1 < cl.N; i++)
            {
                var (ax, ay) = cl.VN(i); var (bx, by) = cl.VN(i + 1);
                double den = ax * by - ay * bx;
                if (Math.Abs(den) < 1e-9) continue;
                double qx = inp.X[i + 1] - inp.X[i], qy = inp.Y[i + 1] - inp.Y[i], t = (qx * by - qy * bx) / den;
                if (Math.Abs(t) > lim) continue;
                double fx = inp.X[i] + ax * t, fy = inp.Y[i] + ay * t;
                if (foc.Any(f => Sq(f.X - fx) + Sq(f.Y - fy) < 0.0625)) continue;
                foc.Add((fx, fy));
            }
            const int FS = 16; const double FB = 1.0;
            System.Threading.Tasks.Parallel.ForEach(foc, f =>
            {
                var (fx, fy) = f;
                int ci = (int)Math.Floor((fx - r.X0) / cell), cj = (int)Math.Floor((fy - r.Y0) / cell);
                if (ci < 0 || cj < 0 || ci >= W || cj >= H || !gN[cj * W + ci].Ok) return;
                double x0 = fx - FB, y0 = fy - FB, hf = 2 * FB / FS;
                var fn = new ENode[(FS + 1) * (FS + 1)];
                System.Threading.Tasks.Parallel.For(0, fn.Length, q => fn[q] = ENode.Of(Raw(x0 + (q % (FS + 1)) * hf, y0 + (q / (FS + 1)) * hf)));
                Stage(FS + 1, FS + 1, (ii, jj) => (x0 + ii * hf, y0 + jj * hf), (ii, jj) => fn[jj * (FS + 1) + ii], hf, 1);
            });
        }
        // ③″ 주름 따라가기 — 마디 사슬 끝(이음 하나)에서 그 방향으로 반 칸씩 나가 직각 이분 · 끊김이 1 mm 아래가 될 때까지(칸 신호가 주름 끝자락의 작은 높이 차를 놓쳐도)
        foreach (bool lo in new[] { true, false })
        {
            var bag = lo ? segL : segH;
            var list = bag.ToList();
            var deg = new Dictionary<(double, double), (int N, CreasePt O)>();
            foreach (var (a, b) in list)
            {
                foreach (var (p, o) in new[] { (a, b), (b, a) })
                { var k = (p.X, p.Y); deg[k] = deg.TryGetValue(k, out var v) ? (v.N + 1, v.O) : (1, o); }
            }
            var ends = deg.Where(kv => kv.Value.N == 1).Select(kv => (P: list.SelectMany(q => new[] { q.A, q.B }).First(q => q.X == kv.Key.Item1 && q.Y == kv.Key.Item2), O: kv.Value.O)).ToList();
            System.Threading.Tasks.Parallel.ForEach(ends, e =>
            {
                var cur = e.P; double dx = e.P.X - e.O.X, dy = e.P.Y - e.O.Y, dl = Math.Sqrt(dx * dx + dy * dy);
                if (dl < 1e-9) return;
                dx /= dl; dy /= dl;
                double st = cell / 2;
                for (int it = 0; it < 400; it++)
                {
                    double mx = cur.X + dx * st, my = cur.Y + dy * st, nx = -dy, ny = dx;
                    var p0 = Raw(mx - nx * st, my - ny * st); var p1 = Raw(mx + nx * st, my + ny * st);
                    if (!p0.Raw || !p1.Raw) break;
                    if (!Locate(mx - nx * st, my - ny * st, p0, mx + nx * st, my + ny * st, p1, lo, !lo, out double cx, out double cy, out var la, out var lb)) break;
                    var c = Value(cx, cy, new List<PointEval> { la, lb }, lo, !lo);
                    if (!double.IsFinite(lo ? c.ZLo : c.ZHi)) break;
                    bag.Add((cur, c));
                    double ex = c.X - cur.X, ey = c.Y - cur.Y, el = Math.Sqrt(ex * ex + ey * ey);
                    if (el < 1e-6) break;
                    dx = ex / el; dy = ey / el; cur = c;
                }
            });
        }
        // ④ 마디 쪼개기 — 가운데에서 직각으로 진짜 주름 자리를 다시 찾아 (값 보간 오차 + K × 처짐) > 1 mm면 둘로(깊이 ≤ 10)
        // 쪼개기 직각 찾기(±L/2 · 측점 뜀 사슬은 측점 이분 · 아니면 높이 이분)
        bool Perp(double mx, double my, double nx, double ny, double L, bool fsMode, bool lo, out double cx, out double cy, out PointEval la, out PointEval lb)
        {
            cx = cy = 0; la = lb = default;
            foreach (double w in new[] { Math.Max(0.5 * L, 0.01) })
            {
                var p0 = Raw(mx - nx * w, my - ny * w); var p1 = Raw(mx + nx * w, my + ny * w);
                if (!p0.Raw || !p1.Raw) continue;
                bool ok = fsMode ? LocateFs(mx - nx * w, my - ny * w, p0, mx + nx * w, my + ny * w, p1, out cx, out cy, out la, out lb)
                                 : Locate2(mx - nx * w, my - ny * w, p0, mx + nx * w, my + ny * w, p1, lo, !lo, out cx, out cy, out la, out lb, out _);
                if (ok) return true;
                if (w >= Math.Max(0.5 * L, 0.01)) break;
            }
            return false;
        }
        void Refine(CreasePt A, CreasePt B, bool lo, int depth, List<(double, double, double, double, double, double, double)> o)
        {
            double za = lo ? A.ZLo : A.ZHi, zb2 = lo ? B.ZLo : B.ZHi, ka = lo ? A.KLo : A.KHi, kb = lo ? B.KLo : B.KHi;
            double L = Math.Sqrt(Sq(B.X - A.X) + Sq(B.Y - A.Y)), k = Math.Min(ka, kb);   // ★[검토 L9] 두 마디 K가 다르면 무른 쪽
            bool fail0 = false;   // ★[v113.3 · 설치 전 검토 질문 6] 첫 쪼개기(깊이 0)에서 가운데 주름을 못 찾음
            if (A.Fs && B.Fs && depth < 12 && L > 2e-3)
            {
                // 측점 뜀 사슬 — 가운데에서 직각으로 측점 뜀 자리를 다시(늘 찾음 · 높이 차가 0 곁이어도) · (값 보간 오차 + K × 처짐) > 1 mm면 쪼갬(사이 높이 차 혹도 여기서 잡힘)
                double mx = (A.X + B.X) / 2, my = (A.Y + B.Y) / 2, nx = -(B.Y - A.Y) / L, ny = (B.X - A.X) / L;
                bool okP = Perp(mx, my, nx, ny, L, true, lo, out double cx, out double cy, out var la, out var lb);
                if (!okP && depth == 0) fail0 = true;
                if (okP)
                {
                    var c = Value(cx, cy, new List<PointEval> { la, lb }, lo, !lo);
                    c.Fs = true;
                    double zc = lo ? c.ZLo : c.ZHi;
                    if (double.IsFinite(zc))
                    {
                        double err = Math.Abs(zc - (za + zb2) / 2) + k * Math.Sqrt(Sq(cx - mx) + Sq(cy - my));
                        if (err > 1e-3) { Refine(A, c, lo, depth + 1, o); Refine(c, B, lo, depth + 1, o); return; }
                    }
                }
            }
            else if (depth < 10 && L > 2e-3)
            {
                double mx = (A.X + B.X) / 2, my = (A.Y + B.Y) / 2, nx = -(B.Y - A.Y) / L, ny = (B.X - A.X) / L;
                bool okP = Perp(mx, my, nx, ny, L, false, lo, out double cx, out double cy, out var la, out var lb);
                if (!okP && depth == 0) fail0 = true;
                if (okP)
                {
                    var c = Value(cx, cy, new List<PointEval> { la, lb }, lo, !lo);
                    double zc = lo ? c.ZLo : c.ZHi;
                    if (double.IsFinite(zc))
                    {
                        double err = Math.Abs(zc - (za + zb2) / 2) + k * Math.Sqrt(Sq(cx - mx) + Sq(cy - my));
                        if (err > 1e-3) { Refine(A, c, lo, depth + 1, o); Refine(c, B, lo, depth + 1, o); return; }
                    }
                }
            }
            // 측점 뜀 사슬 중 두 끝 다 높이 차 1 mm 아래인 토막은 원천이 필요 없음(이미 이어짐 — 쪼개기가 사이 혹을 이미 봄)
            if (A.Fs && B.Fs && Math.Max(lo ? A.JLo : A.JHi, lo ? B.JLo : B.JHi) <= 1e-3) return;
            // ★[v113.3 · 설치 전 검토 질문 6] 첫 쪼개기에서 두 끝 사이 가운데 주름을 못 찾은 마디(한 네모의 두 끝 점이 다른 주름 · 주름 끝자락)는 이어 붙이지 않고 두 점 원천으로 —
            //   이은 마디는 사이 주름이 없는 자리에도 선형 높이 원뿔을 세워 감싸기가 땅 · 사면을 괜히 밀 수 있다(셈 = EnvPerpFail · S214 · S212에서 남).
            if (fail0)
            {
                System.Threading.Interlocked.Increment(ref perpFail0);
                o.Add((A.X, A.Y, za, A.X, A.Y, za, ka));
                o.Add((B.X, B.Y, zb2, B.X, B.Y, zb2, kb));
                return;
            }
            o.Add((A.X, A.Y, za, B.X, B.Y, zb2, k));
        }
        var env = new EnvSet();
        var outL = new System.Collections.Concurrent.ConcurrentBag<List<(double, double, double, double, double, double, double)>>();
        var outH = new System.Collections.Concurrent.ConcurrentBag<List<(double, double, double, double, double, double, double)>>();
        System.Threading.Tasks.Parallel.ForEach(segL, s => { var o = new List<(double, double, double, double, double, double, double)>(); Refine(s.A, s.B, true, 0, o); outL.Add(o); });
        System.Threading.Tasks.Parallel.ForEach(segH, s => { var o = new List<(double, double, double, double, double, double, double)>(); Refine(s.A, s.B, false, 0, o); outH.Add(o); });
        foreach (var o in outL) env.Lo.AddRange(o);
        foreach (var o in outH) env.Hi.AddRange(o);
        foreach (var p in lone)
        {
            if (p.Fs && Math.Max(double.IsFinite(p.ZLo) ? p.JLo : 0, double.IsFinite(p.ZHi) ? p.JHi : 0) <= 1e-3) continue;
            if (double.IsFinite(p.ZLo)) env.Lo.Add((p.X, p.Y, p.ZLo, p.X, p.Y, p.ZLo, p.KLo));
            if (double.IsFinite(p.ZHi)) env.Hi.Add((p.X, p.Y, p.ZHi, p.X, p.Y, p.ZHi, p.KHi));
        }
        // 차례 고정(병렬 모음 차례가 판마다 달라도 같은 색인 · 같은 값)
        env.Lo.Sort(); env.Hi.Sort();
        env.Build();
        stat = (cand, nPts, wallSkip, Evals(), perpFail0);
        return env;
    }
}
