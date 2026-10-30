namespace DH.Grading.Core;

/// <summary>★★★[v111 · 계획_v111.md 2.2 · 2.3 · JACK 1029 «옹벽변환시 소단선이 매끄럽지 못하고 같은 직선인데 끊어져» · «삼각망이 너무 촘촘» · «원지반과 닫는 부분이 꼭지점에서 억지로 끊었달까»]
/// <b>단면 광선</b> — 측점마다 직각 횡단(가장자리 높이 ± 사면 단면)을 꺾임 목록으로 정확히 놓고, 데이라잇은 광선을 따라 바탕면과 만나는 자리를 이분으로 찾는다.
/// <para>엔진(칸 원뿔 감싸기)의 뜻이 «점 P = 가장 가까운 중심선 측점의 직각 횡단»이라 곧은 곳 · 곡선 모두 광선 하나가 그 자리 단면이다.
/// 예외(두 다리 · 곡선 안쪽이 반경을 넘음 · 충돌)는 엔진 점 평가기(<see cref="Result.Eval"/>)에 «이 점의 주인»(종류 · 측점 · 쪽)을 물어 광선을 거기서 자른다(주름).
/// 높이는 묻지 않는다 — 엔진은 0.005 m 표(꺾임 곁 흐림) · 꺾은선 중심선(곡선 안쪽 꼭짓점마다 측점이 뛰어 수 cm 계단)이라, 광선(꺾임 목록 · 매끈한 법선)이 더 정확하다.</para></summary>
public static partial class RoadGrade
{
    internal enum RayEnd : byte { None, Daylight, Crease, Reach, NoGround, EdgeWall, Meet }

    /// <summary>광선 위 점 — 가장자리에서 거리 D · 자리 · 설계 높이 · 꺾임 번호 K(0 = 가장자리 · 단면 꺾임 목록 번호 · −1 = 끝점(데이라잇 · 주름 · 범위 끝)).</summary>
    internal readonly record struct RayPt(double D, double X, double Y, double Z, int K);

    /// <summary>단면 광선 하나 — 쪽(−1 좌 · +1 우 · 0 끝 마구리) · Core 측점 · 가장자리 점 · 바깥 방향 · 단면 · 절(+1)/성(−1)/없음(0) · 끝 종류.</summary>
    internal sealed class Ray
    {
        public int Side; public double S;
        public double Ex, Ey, Ez, Nx, Ny;
        public int Prof; public bool EdgeWall; public sbyte Regime;
        public RayEnd End;
        public List<RayPt> Pts = new();
        /// <summary>끝 마구리 광선의 끝 선 위 옆 자리(좌 − · 우 +).</summary>
        public double Lat;
        /// <summary>끝 마구리 · 모서리 부채 광선이 기대는 끝(0 = 시점 · 1 = 종점) · 옆 광선은 −1.</summary>
        public int EndCap = -1;
        /// <summary>꼴(점 수 · 꺾임 번호 · 끝 종류 · 단면 · 절성)이 같은가 — 이웃 광선끼리 네모로 이을 수 있나.</summary>
        public bool SameShape(Ray o)
        {
            if (o.Prof != Prof || o.EdgeWall != EdgeWall || o.Regime != Regime || o.End != End || o.Pts.Count != Pts.Count) return false;
            for (int i = 0; i < Pts.Count; i++) if (Pts[i].K != o.Pts[i].K) return false;
            return true;
        }
    }

    /// <summary>중심선(꺾은선) — 측점 → 자리 · 높이 · 매끈한 왼쪽 법선(꼭짓점 = 두 선분 법선의 이등분 · 선분 안은 측점 따라 보간).</summary>
    internal sealed class CenterLine
    {
        public readonly double[] X, Y, Z, S;
        readonly double[] _vnx, _vny;   // 꼭짓점 법선(왼쪽)
        public readonly int N;
        public double Length => S[N - 1];
        public CenterLine(double[] x, double[] y, double[] z)
        {
            N = Math.Min(x.Length, Math.Min(y.Length, z.Length));
            X = x; Y = y; Z = z; S = new double[N];
            for (int i = 1; i < N; i++) S[i] = S[i - 1] + Math.Sqrt(Sq(X[i] - X[i - 1]) + Sq(Y[i] - Y[i - 1]));
            _vnx = new double[N]; _vny = new double[N];
            for (int i = 0; i < N; i++)
            {
                double ax = 0, ay = 0;
                if (i > 0) { var (nx, ny) = SegN(i - 1); ax += nx; ay += ny; }
                if (i + 1 < N) { var (nx, ny) = SegN(i); ax += nx; ay += ny; }
                double l = Math.Sqrt(ax * ax + ay * ay);
                if (l < 1e-12) { var (nx, ny) = SegN(Math.Min(i, N - 2)); ax = nx; ay = ny; l = 1; }
                _vnx[i] = ax / l; _vny[i] = ay / l;
            }
        }
        (double, double) SegN(int i)
        {
            double dx = X[i + 1] - X[i], dy = Y[i + 1] - Y[i], l = Math.Sqrt(dx * dx + dy * dy);
            return l < 1e-12 ? (0, 1) : (-dy / l, dx / l);
        }
        /// <summary>선분 번호 · 선분 안 비율.</summary>
        public (int I, double T) Loc(double s)
        {
            if (s <= 0) return (0, 0);
            if (s >= Length) return (N - 2, 1);
            int i = Array.BinarySearch(S, s); if (i < 0) i = ~i - 1;
            i = Math.Clamp(i, 0, N - 2);
            double L = S[i + 1] - S[i];
            return (i, L > 1e-12 ? (s - S[i]) / L : 0);
        }
        /// <summary>측점의 자리 · 계획고 · 매끈한 왼쪽 법선.</summary>
        public (double X, double Y, double Z, double Nx, double Ny) At(double s)
        {
            var (i, t) = Loc(s);
            double x = X[i] + (X[i + 1] - X[i]) * t, y = Y[i] + (Y[i + 1] - Y[i]) * t, z = Z[i] + (Z[i + 1] - Z[i]) * t;
            double nx = _vnx[i] * (1 - t) + _vnx[i + 1] * t, ny = _vny[i] * (1 - t) + _vny[i + 1] * t, l = Math.Sqrt(nx * nx + ny * ny);
            if (l < 1e-12) { (nx, ny) = SegN(i); l = 1; }
            return (x, y, z, nx / l, ny / l);
        }
        /// <summary>진행 방향(단위) — 선분 방향.</summary>
        public (double Tx, double Ty) Dir(int i)
        {
            double dx = X[i + 1] - X[i], dy = Y[i + 1] - Y[i], l = Math.Sqrt(dx * dx + dy * dy);
            return l < 1e-12 ? (1, 0) : (dx / l, dy / l);
        }
        /// <summary>꼭짓점 꺾임 중 가장 큰 것(도).</summary>
        public double MaxTurnDeg
        {
            get
            {
                // ★[v111.2 · 전체 검토 L] 길이 0 선분(겹친 표본)은 건너뜀 — 앞 · 뒤 진짜 선분끼리 꺾임(엔진도 그 선분을 건너뜀)
                double m = 0; int prev = -1;
                for (int i = 0; i + 1 < N; i++)
                {
                    if (S[i + 1] - S[i] < 1e-9) continue;
                    if (prev >= 0)
                    {
                        var (ax, ay) = Dir(prev); var (bx, by) = Dir(i);
                        m = Math.Max(m, Math.Abs(Math.Atan2(ax * by - ay * bx, ax * bx + ay * by)));
                    }
                    prev = i;
                }
                return m * 180 / Math.PI;
            }
        }
        /// <summary>그 측점 곁 선분 길이(주인 판정 측점 한도).</summary>
        public double SegLen(double s) { var (i, _) = Loc(s); return S[i + 1] - S[i]; }
    }

    /// <summary>광선 만들기 — 입력 · 엔진 결과(단면 · 기록 · 점 평가기) · 중심선을 붙잡음.</summary>
    internal sealed class RayCtx
    {
        public readonly Input Inp; public readonly Result R; public readonly CenterLine CL; public readonly Section Sec;
        public readonly double DMax;
        /// <summary>주인 판정 · 데이라잇 이분 횟수 — 잴 때 씀.</summary>
        public long Evals, Walks;
        public RayCtx(Input inp, Result r)
        {
            Inp = inp; R = r; Sec = inp.Sec; CL = new CenterLine(inp.X, inp.Y, inp.Z);
            DMax = Math.Max(inp.Reach, r.ReachUsed) + 1;
            // ★[계획 검토 M5 · L12 · 내 판단] 매끈한 법선은 곡선을 잘게 나눈 꺾은선(1 m · 꼭짓점 꺾임 몇 도)용 — 곡선 없는 IP(10° 넘게 꺾임)는 옛 칸 길로 물러섬(알림)
            if (CL.MaxTurnDeg >= 10) throw new NotSupportedException($"중심선 꼭짓점 꺾임 {CL.MaxTurnDeg:F0}° (곡선 없는 IP) — 선 면은 꺾임 10° 미만 곡선용");
        }

        /// <summary>그 쪽 · 측점의 단면 — 나중 기록이 이김(엔진 RecAt과 같은 규칙) · 0단 · 소단 0 기록 = 가장자리 옹벽.</summary>
        public (int Prof, bool EdgeWall) ProfileAt(int side, double s)
        {
            if (side == 0) return (0, false);
            foreach (var w in Inp.Walls) if (w.Side == side && s >= Math.Min(w.S0, w.S1) - 1e-9 && s <= Math.Max(w.S0, w.S1) + 1e-9) return (0, true);
            for (int i = R.Recs.Count - 1; i >= 0; i--)
            {
                var t = R.Recs[i];
                if (t.Side != side || s < t.S0 - 1e-9 || s > t.S1 + 1e-9) continue;
                if (t.Tier <= 0 && t.BenchL <= 0) return (0, true);
                return (Math.Max(0, R.ProfFor(i, side, s)), false);   // ★[v111.3] 끝면 띠면 그 띠 단면
            }
            return (0, false);
        }

        /// <summary>옆 광선 — 측점 s · 쪽(−1 좌 · +1 우) · 매끈한 법선.</summary>
        public Ray SideRay(int side, double s)
        {
            var (cx, cy, cz, nx, ny) = CL.At(s);
            double off = side < 0 ? Sec.OuterL : Sec.OuterR;
            var (p, ew) = ProfileAt(side, s);
            var ray = new Ray
            {
                Side = side, S = s, Ex = cx - side * nx * off, Ey = cy - side * ny * off, Ez = cz + Sec.Drop(side * off),
                Nx = -side * nx, Ny = -side * ny, Prof = p, EdgeWall = ew,
                // 끝 측점의 옆 광선 = 모서리 부채의 첫 광선 — 엔진은 그 자리를 끝 너머(쪽 0)로도 본다(투영 비율이 0을 살짝 넘나듦)
                EndCap = s <= 1e-9 ? 0 : s >= CL.Length - 1e-9 ? 1 : -1,
            };
            Walk(ray);
            return ray;
        }

        /// <summary>끝 마구리 광선 — 끝(0 시점 · 1 종점) · 끝 선 위 옆 자리 lat(좌 − · 우 +) · 진행 방향 바깥.</summary>
        public Ray EndRay(int end, double lat)
        {
            int i = end == 0 ? 0 : CL.N - 2;
            var (tx, ty) = CL.Dir(i);
            double nx = -ty, ny = tx;   // 왼쪽 법선(선분)
            double cx = end == 0 ? CL.X[0] : CL.X[CL.N - 1], cy = end == 0 ? CL.Y[0] : CL.Y[CL.N - 1], cz = end == 0 ? CL.Z[0] : CL.Z[CL.N - 1];
            var ray = new Ray
            {
                Side = 0, S = end == 0 ? 0 : CL.Length, Ex = cx - nx * lat, Ey = cy - ny * lat, Ez = cz + Sec.Drop(lat),
                Nx = end == 0 ? -tx : tx, Ny = end == 0 ? -ty : ty, Prof = 0, EndCap = end, Lat = lat,
            };
            Walk(ray);
            return ray;
        }

        /// <summary>모서리 부채 광선 — 끝 · 쪽의 가장자리 모서리 점에서 옆 법선(a = 0)과 진행 바깥(a = 1) 사이 방향.</summary>
        public Ray CornerRay(int end, int side, double a)
        {
            int i = end == 0 ? 0 : CL.N - 2;
            var (tx, ty) = CL.Dir(i);
            double nx = -ty, ny = tx;
            double off = side < 0 ? Sec.OuterL : Sec.OuterR;
            double cx = end == 0 ? CL.X[0] : CL.X[CL.N - 1], cy = end == 0 ? CL.Y[0] : CL.Y[CL.N - 1], cz = end == 0 ? CL.Z[0] : CL.Z[CL.N - 1];
            double ox = -side * nx, oy = -side * ny, lx = end == 0 ? -tx : tx, ly = end == 0 ? -ty : ty;
            double ang = a * Math.PI / 2, dx = ox * Math.Cos(ang) + lx * Math.Sin(ang), dy = oy * Math.Cos(ang) + ly * Math.Sin(ang);
            var ray = new Ray
            {
                Side = side, S = end == 0 ? 0 : CL.Length, Ex = cx - side * nx * off, Ey = cy - side * ny * off, Ez = cz + Sec.Drop(side * off),
                Nx = dx, Ny = dy, Prof = 0, EndCap = end,
            };
            Walk(ray);
            return ray;
        }

        /// <summary>점 (x, y)가 이 광선의 것인가 — 엔진 점 평가기의 주인(종류 · 쪽 · 측점). 충돌(4) · 바탕 없음 · 남의 도로 면 = 아님.
        /// <para>★[계획 검토 M11] 먼저 격자 칸 값(점을 둘러싼 칸 가운데 넷)을 본다 — 넷 다 넉넉히(측점 한도 − 칸) 내 것이면 점 평가기를 안 부른다.</para></summary>
        public bool Owns(Ray ray, double x, double y, double d)
        {
            // ★[설치 전 검토 L12] 엔진 버킷은 Reach까지만 — 그 너머는 판정 못 함(내 것으로)
            if (d > Math.Max(Inp.Reach, R.ReachUsed) - 0.5) return true;
            double cap = ray.EndCap == 0 ? 0 : CL.Length, sRef = ray.EndCap >= 0 ? cap : ray.S;
            double lim = Math.Max(1.0, 1.5 * CL.SegLen(sRef));
            bool Mine(byte kind, int fside, double fs, double slack)
            {
                if (kind == 255 || kind == 4) return false;
                if (kind == 1) return d < 0.02;
                if (ray.EndCap >= 0)
                {
                    if (fside == 0) return Math.Abs(fs - cap) < 1e-3;
                    return ray.Side != 0 && fside == ray.Side && Math.Abs(fs - cap) <= lim - slack;
                }
                return fside == ray.Side && Math.Abs(fs - ray.S) <= lim - slack;
            }
            {
                double gi = (x - R.X0) / R.Cell - 0.5, gj = (y - R.Y0) / R.Cell - 0.5;
                int i = (int)Math.Floor(gi), j = (int)Math.Floor(gj);
                if (i >= 0 && j >= 0 && i + 1 < R.W && j + 1 < R.H && d > 2 * R.Cell)
                {
                    bool all = true;
                    for (int q = 0; q < 4 && all; q++)
                    {
                        int k = (j + (q >> 1)) * R.W + i + (q & 1);
                        all = Mine(R.Kind[k], R.FSide[k], R.FS[k], 2 * R.Cell) && R.Kind[k] != 0;
                    }
                    if (all) return true;
                }
            }
            System.Threading.Interlocked.Increment(ref Evals);
            var pe = R.Eval!(x, y);
            return Mine(pe.Kind, pe.FSide, pe.FS, 0);
        }

        /// <summary>광선 걷기 — 데이라잇(바탕면과 만나는 첫 자리 · 이분) · 꺾임점 · 주인이 바뀌면 그 자리(이분 1 mm)에서 자름.</summary>
        void Walk(Ray ray)
        {
            System.Threading.Interlocked.Increment(ref Walks);
            ray.Pts.Clear();
            ray.Pts.Add(new RayPt(0, ray.Ex, ray.Ey, ray.Ez, 0));
            var gnd = Inp.Base;
            if (!gnd.TryGetElevation(ray.Ex, ray.Ey, out double g0)) { ray.End = RayEnd.NoGround; return; }
            double dz0 = g0 - ray.Ez;
            if (Math.Abs(dz0) < 1e-4) { ray.Regime = 0; ray.End = RayEnd.Daylight; return; }
            bool cut = dz0 > 0;
            ray.Regime = (sbyte)(cut ? 1 : -1);
            if (ray.EdgeWall)
            {
                // 가장자리 옹벽 — 가장자리에서 바로 땅까지(평면폭 = 높이 × MinSlope · 부지 옹벽과 같은 꼴)
                double w = Math.Max(0.001, Math.Abs(dz0) * Math.Clamp(Inp.MinSlope, 0.001, 0.2));
                double wx = ray.Ex + ray.Nx * w, wy = ray.Ey + ray.Ny * w;
                if (!gnd.TryGetElevation(wx, wy, out double gw)) gw = g0;
                ray.Pts.Add(new RayPt(w, wx, wy, gw, -1));
                ray.End = RayEnd.EdgeWall;
                return;
            }
            var prof = (cut ? R.CutProfs : R.FillProfs)[Math.Clamp(ray.Prof, 0, R.CutProfs.Count - 1)];
            double sgn = cut ? 1 : -1;
            double Cone(double d) => ray.Ez + sgn * prof.FAt(d);
            bool G(double d, out double g) => gnd.TryGetElevation(ray.Ex + ray.Nx * d, ray.Ey + ray.Ny * d, out g);
            // f > 0 = 아직 깎거나 쌓을 자리(바탕이 원뿔 밖)
            double F(double d, out bool ok) { ok = G(d, out double g); return ok ? sgn * (g - Cone(d)) : double.NaN; }
            // 걸음 자리 — 꺾임점 + 0.1 m(바탕 세모 변을 놓치지 않게)
            var br = prof.Breaks;
            var marks = new List<(double D, int K)>();
            for (int k = 1; k < br.Count && br[k].D < DMax; k++) marks.Add((br[k].D, k));
            double dDay = double.NaN, dLast = 0; bool noGround = false;
            {
                var steps = new List<double>(marks.Count + (int)(DMax / 0.1) + 2);
                for (int q = 1; q * 0.1 < DMax; q++) steps.Add(q * 0.1);
                foreach (var (d, _) in marks) steps.Add(d);
                steps.Add(DMax);
                steps.Sort();
                double a = 0;
                foreach (double b in steps)
                {
                    if (b <= a + 1e-9) continue;
                    double fb = F(b, out bool okb);
                    if (!okb) { noGround = true; dLast = a; break; }
                    if (fb <= 0)
                    {
                        double lo = a, hi = b;
                        for (int it = 0; it < 48 && hi - lo > 1e-7; it++)
                        {
                            double m = (lo + hi) / 2, fm = F(m, out bool okm);
                            if (!okm || fm <= 0) hi = m; else lo = m;
                        }
                        dDay = hi;
                        break;
                    }
                    a = b;
                }
                if (double.IsNaN(dDay) && !noGround) dLast = DMax;
            }
            double dEnd = double.IsNaN(dDay) ? dLast : dDay;
            ray.End = !double.IsNaN(dDay) ? RayEnd.Daylight : noGround ? RayEnd.NoGround : RayEnd.Reach;
            // 주인 확인 — 꺾임 사이(꺾임에서 6 mm 떨어진 자리 · 0.5 m마다) · 끝 곁. 처음 남의 것이 된 자리와 앞 자리 사이를 이분(1 mm).
            double firstBad = double.NaN, lastGood = 0;
            {
                var test = new List<double>();
                double prev = 0;
                foreach (var (d, _) in marks)
                {
                    if (d >= dEnd) break;
                    AddTests(test, prev, d);
                    prev = d;
                }
                AddTests(test, prev, dEnd);
                foreach (double d in test)
                {
                    if (Owns(ray, ray.Ex + ray.Nx * d, ray.Ey + ray.Ny * d, d)) { lastGood = d; continue; }
                    firstBad = d; break;
                }
            }
            if (!double.IsNaN(firstBad))
            {
                double lo = lastGood, hi = firstBad;
                while (hi - lo > 1e-3)
                {
                    double m = (lo + hi) / 2;
                    if (Owns(ray, ray.Ex + ray.Nx * m, ray.Ey + ray.Ny * m, m)) lo = m; else hi = m;
                }
                // ★[계획 검토 M7] 주름 끝은 2 mm 앞에서 — 맞은편 사슬과 겹치지 않게(틈은 속 구멍 메움이 닫는다)
                dEnd = Math.Max(0, lo - 0.002); ray.End = RayEnd.Crease;
            }
            // ★[계획 검토 H1] 끝이 꺾임점에서 1 mm 안이면 그 꺾임점으로 — Civil이 0.1 mm 안 두 점을 합쳐 점 수가 어긋나지 않게
            foreach (var (d, _) in marks)
            {
                if (d > dEnd + 1e-3) break;
                if (Math.Abs(d - dEnd) <= 1e-3) { dEnd = d; break; }
            }
            foreach (var (d, k) in marks)
            {
                if (d >= dEnd - 1e-6) break;
                ray.Pts.Add(new RayPt(d, ray.Ex + ray.Nx * d, ray.Ey + ray.Ny * d, Cone(d), k));
            }
            if (dEnd > 1e-6)
            {
                double ze = ray.End == RayEnd.Daylight && G(dEnd, out double ge) ? ge : Cone(dEnd);
                ray.Pts.Add(new RayPt(dEnd, ray.Ex + ray.Nx * dEnd, ray.Ey + ray.Ny * dEnd, ze, -1));
            }
        }

        static void AddTests(List<double> test, double a, double b)
        {
            if (b - a <= 0.012) { test.Add((a + b) / 2); return; }
            double lo = a + 0.006, hi = b - 0.006;
            test.Add(lo);
            int m = (int)Math.Ceiling((hi - lo) / 0.5);
            for (int i = 1; i < m; i++) test.Add(lo + (hi - lo) * i / m);
            test.Add(hi);
        }
    }
}
