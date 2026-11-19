using System.Globalization;
using NetTopologySuite.Algorithm.Locate;
using NetTopologySuite.Geometries;

namespace DH.Grading.Core;

/// <summary>[옹벽 추가] 단 표가 읽는 쪽 · 측점(고리 면 <see cref="RoadRing.RingBench"/> · 선 먼저 면 <see cref="RoadLines.LinesBench"/>).</summary>
public interface IRoadSideBench
{
    /// <summary>그 점을 이긴 원천의 쪽(−1 좌 · +1 우 · 0 마구리 · 없음).</summary>
    int SideAt(double x, double y);
    /// <summary>그 점을 이긴 원천의 측점 — 못 읽으면 NaN.</summary>
    double StationAt(double x, double y);
}

public static partial class RoadLines
{
    /// <summary>★★★[JACK 1119 «선 먼저» 2단계 · 계획 §3 LinesBench] 선 먼저 면 위 [옹벽 추가]의 옛 소단 높이 — 면을 만든 판에서 그대로 읽음
    /// (옛 고리 평가기 <c>b.Ev</c>는 굽이에서 새 면과 높이 규칙이 달라 단 높이가 어긋났다).
    /// 단 k 높이 = 그 점을 이긴 판 세모의 원천 높이 ± R_k(절토 = 원천 + R_k · 성토 = 원천 − R_k) — 원천 높이 = 판 높이 ∓ F_p(d)(그 띠 단면 · 원천 거리 d).</summary>
    public sealed class LinesBench : WallByLine.BenchLevels, IRoadSideBench
    {
        readonly Ctx _c; readonly Sheet _sh; readonly bool _up; readonly double _benchW, _mu;
        readonly double[] _rise, _h, _dIn, _dOut;
        readonly IPointOnGeometryLocator _deck; readonly IPointOnGeometryLocator? _cutReg;
        string _why = "";
        double _worst; string _worstTxt = "";
        /// <summary>셈 — 단 높이 부름 · 못 읽음 · 이어짐 쌍 · 다른 다리 · 못 읽음 · 골 · 1 cm 넘음.</summary>
        public int LevelCalls, LevelNaN, JumpPairs, JumpLegDiff, JumpNoLv, JumpValley, JumpOverN;

        public LinesBench(RoadRing.Built b, RoadGrade.SlopeRule rule, bool up)
        {
            // ★[2b · 검토 L3] 짓다 멈춘 꾸러미(판 · 자리 없음)면 까닭 있는 예외(빈 참조 대신)
            if (b.Lf?.C?.Cut == null || b.Lf.C.Fill == null || b.Lf.Region == null) throw new InvalidOperationException("선 먼저 면이 다 안 지어짐 — 단 높이를 못 읽음");
            _c = b.Lf.C; _up = up; _sh = up ? _c.Cut : _c.Fill; _benchW = Math.Max(0, rule.BenchW);
            _mu = b.Deck.MaxGrade + 0.01;
            var p = _sh.P;
            var rise = new List<double>(); var dIn = new List<double>(); var dOut = new List<double>();
            for (int i = 1; i < p.E.Count; i++)
            {
                if (!(p.E[i].R > p.E[i - 1].R + 1e-9)) continue;   // 사면 끝 = 단 k
                double din = p.E[i].D, dout = din;
                if (i + 1 < p.E.Count && p.K[i + 1] == p.K[i] && Math.Abs(p.E[i + 1].R - p.E[i].R) < 1e-9) dout = p.E[i + 1].D;
                rise.Add(p.E[i].R); dIn.Add(din); dOut.Add(dout);
            }
            _rise = rise.ToArray(); _dIn = dIn.ToArray(); _dOut = dOut.ToArray();
            _h = new double[_rise.Length];
            for (int k = 0; k < _rise.Length; k++) _h[k] = k == 0 ? _rise[0] : _rise[k] - _rise[k - 1];
            _deck = new IndexedPointInAreaLocator(b.Deck.Poly);
            _cutReg = b.Lf.RegionCut != null && !b.Lf.RegionCut.IsEmpty ? new IndexedPointInAreaLocator(b.Lf.RegionCut) : null;
        }

        public override bool Up => _up;
        public override double MaxGrad => _mu;
        public override int K => _rise.Length;
        public override double TerraceW => _benchW;
        public override double BenchW => _benchW;
        public override int TierCount => _rise.Length;
        public override (double Rise, double H, bool Terrace) Tier(int k) { int i = Math.Clamp(k, 1, Math.Max(1, _rise.Length)) - 1; return _rise.Length == 0 ? (0, 0, false) : (_rise[i], _h[i], false); }
        public override string Name => "선 먼저 면";
        public override bool MidBench => true;
        public override string JumpWhy => _why;

        /// <summary>그 점을 이긴 판 세모의 띠(못 읽으면 −1) · 원천 거리 · 원천 높이 · 측점 · 쪽 · 단면 번호.</summary>
        public (int F, double D, double Src, double S, int Side, int Pr) WinAt(double x, double y)
        {
            var q = new Coordinate(x, y);
            if (_deck.Locate(q) == Location.Interior) return (-1, double.NaN, double.NaN, double.NaN, 0, 0);
            // 성토 단 표인데 그 자리가 절토 자리(부딪힘 · 절토 면)면 못 읽음(옛 고리 단 표와 같은 뜻)
            if (!_up && _cutReg != null && _cutReg.Locate(q) == Location.Interior && _c.KindAt(x, y) == 4) return (-1, double.NaN, double.NaN, double.NaN, 0, 0);
            // 땅과 못 만나 뺀 세모까지(선 옹벽 선은 데이라잇 밖 1 m까지 그어짐 — 뺀 세모 자리에서 남은 세모를 읽으면 12 cm 어긋났다 · 키트 rwall)
            var (z, f) = _c.TopAll(_sh, x, y, 1e-3);
            if (f == null || !double.IsFinite(z)) return (-1, double.NaN, double.NaN, double.NaN, 0, 0);
            var (u, d) = _c.Locate(f.Strip, x, y);
            // 원천 높이 = 그 띠 두 광선 원천 높이를 띠 비율로(판 높이에서 F(d)를 빼면 오목 굽이 덮개 · 다리 늘림 띠에서 거리 뜻이 꺾여 2 m 어긋났다 — 덤프 B 굽이 1 안쪽)
            var (ia, ib, _, _) = _c.Strips[f.Strip]; var ra = _c.Rays[ia]; var rb = _c.Rays[ib];
            double uu = Math.Clamp(u, 0, 1), src = ra.Z + (rb.Z - ra.Z) * uu;
            var (s, side) = _c.StripStation(f.Strip, x, y);
            return (f.Strip, d, src, s, side, f.Pr);
        }

        public double StationAt(double x, double y) => WinAt(x, y).S;
        public int SideAt(double x, double y) => WinAt(x, y).Side;

        public override double Level(int k, double x, double y)
        {
            Interlocked.Increment(ref LevelCalls);
            if (k < 1 || k > _rise.Length) return double.NaN;
            var w = WinAt(x, y);
            if (w.F < 0 || !double.IsFinite(w.Src)) { Interlocked.Increment(ref LevelNaN); return double.NaN; }
            return w.Src + (_up ? 1 : -1) * _rise[k - 1];
        }

        public override bool InBand(int k, double x, double y)
        {
            if (k < 1 || k > _rise.Length || _dOut[k - 1] - _dIn[k - 1] <= 1e-6) return false;
            var w = WinAt(x, y);
            if (w.F < 0 || !double.IsFinite(w.D)) return false;
            if (w.Pr != 0) return false;   // [옹벽 변환] 단면 띠는 규칙 검사 안 함
            return w.D >= _dIn[k - 1] - 1e-9 && w.D <= _dOut[k - 1] + 1e-9;
        }

        double Heading(double s)
        {
            var d = _c.D; int i = Array.BinarySearch(d.S, s); if (i < 0) i = ~i; i = Math.Clamp(i, 1, d.S.Length - 1);
            return Math.Atan2(d.Y[i] - d.Y[i - 1], d.X[i] - d.X[i - 1]);
        }

        public override double JumpOver(int k, double x0, double y0, double za, double x1, double y1, double zb, double dist)
        {
            Interlocked.Increment(ref JumpPairs);
            var w0 = WinAt(x0, y0); var w1 = WinAt(x1, y1);
            if (!double.IsFinite(za) || !double.IsFinite(zb) || w0.F < 0 || w1.F < 0)
            {
                Interlocked.Increment(ref JumpNoLv);
                _why = "선 먼저 면 단 높이를 못 읽음(도로 면 위 · 부딪히는 자리 · 셈 범위 밖)";
                return double.NaN;
            }
            double ds = Math.Abs(w0.S - w1.S);
            if (ds > dist + 1.0)
            {
                double t = Math.Abs(Heading(w0.S) - Heading(w1.S)); while (t > Math.PI) t = Math.Abs(t - 2 * Math.PI);
                bool same = w0.Side != 0 && w0.Side == w1.Side && t <= 1.75;
                if (!same)
                {
                    Interlocked.Increment(ref JumpLegDiff);
                    _why = string.Format(CultureInfo.InvariantCulture, "두 점의 주인 다리가 다름(측점 {0:F1} · {1:F1})", w0.S, w1.S);
                    return double.NaN;
                }
            }
            double over = Math.Abs(zb - za) - _mu * Math.Max(dist, ds);
            if (over > 0.01 && w0.F != w1.F && ds > dist + 0.5)
            {
                // 두 다리 판이 만나는 골(굽이 안쪽 단차) — 양쪽 옛 소단 높이가 달라 이어진 벽 소단을 못 만듦
                Interlocked.Increment(ref JumpValley);
                _why = string.Format(CultureInfo.InvariantCulture, "두 사면이 만나는 골을 지납니다(양쪽 옛 소단 높이가 {0:F2} m 다름) — 선을 골 한쪽 사면에만 그리세요", Math.Abs(zb - za));
                return double.NaN;
            }
            if (over > 0.01)
            {
                Interlocked.Increment(ref JumpOverN);
                lock (_rise) if (over > _worst) { _worst = over; _worstTxt = string.Format(CultureInfo.InvariantCulture, "단 {0} ({1:F2},{2:F2}) s {3:F1} z {4:F3} ↔ ({5:F2},{6:F2}) s {7:F1} z {8:F3} · 거리 {9:F2}", k, x0 % 1000, y0 % 1000, w0.S, za, x1 % 1000, y1 % 1000, w1.S, zb, dist); }
            }
            return over;
        }

        public override string Text => (_worstTxt.Length > 0 ? "[최악 " + _worstTxt + "] " : "") + string.Format(CultureInfo.InvariantCulture,
            "{0} 선 먼저 면 · 단 {1}(R {2}) · μ {3:F3} · 단 높이 {4}점(못 읽음 {5}) · 이어짐 쌍 {6}(다리 다름 {7} · 못 읽음 {8} · 골 {9} · 1 cm 넘음 {10})",
            _up ? "절토" : "성토", _rise.Length, string.Join("/", _rise.Take(4).Select(v => v.ToString("0.##", CultureInfo.InvariantCulture))), _mu,
            LevelCalls, LevelNaN, JumpPairs, JumpLegDiff, JumpNoLv, JumpValley, JumpOverN);
    }

    internal sealed partial class Ctx
    {
        /// <summary>띠 s 안 점 (x, y)의 띠 비율 u · 원천 거리 d — P = (1−u)(O_a + d U_a) + u(O_b + d U_b)를 뉴턴으로(부채 = 같은 원점).</summary>
        public (double U, double D) Locate(int s, double x, double y)
        {
            var (ia, ib, _, _) = Strips[s]; var a = Rays[ia]; var b = Rays[ib];
            double ex = b.OX - a.OX, ey = b.OY - a.OY, l2 = ex * ex + ey * ey;
            double u = l2 < 1e-18 ? 0.5 : Math.Clamp(((x - a.OX) * ex + (y - a.OY) * ey) / l2, 0, 1);
            double d = Math.Sqrt((x - a.OX - ex * u) * (x - a.OX - ex * u) + (y - a.OY - ey * u) * (y - a.OY - ey * u));
            for (int it = 0; it < 20; it++)
            {
                double ux = a.UX + (b.UX - a.UX) * u, uy = a.UY + (b.UY - a.UY) * u;
                double fx = a.OX + ex * u + d * ux - x, fy = a.OY + ey * u + d * uy - y;
                if (fx * fx + fy * fy < 1e-14) break;
                double j11 = ex + d * (b.UX - a.UX), j12 = ux, j21 = ey + d * (b.UY - a.UY), j22 = uy;
                double det = j11 * j22 - j12 * j21; if (Math.Abs(det) < 1e-14) break;
                double du = (fx * j22 - fy * j12) / det, dd = (j11 * fy - j21 * fx) / det;
                u -= du; d -= dd;
                if (!double.IsFinite(u) || !double.IsFinite(d)) { u = 0.5; d = 0; break; }
            }
            return (Math.Clamp(u, -0.05, 1.05), Math.Max(0, d));
        }
    }
}
