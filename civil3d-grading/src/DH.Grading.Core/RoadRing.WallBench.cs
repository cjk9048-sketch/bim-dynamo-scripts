using System.Collections.Concurrent;
using System.Globalization;

namespace DH.Grading.Core;

/// <summary>★★★[v116.6 · JACK 1110 «옹벽 추가가 안 됨 · 어디든 폴리선을 그으면 작성이 되어야» · 계획 `2026-11-10_진입도로_옹벽2단계_고리면.md` §1-1]
/// 고리 면 위 선 옹벽의 옛 소단 높이 — 면을 만든 높이 규칙(E2) 그 자체에서 읽는다. 옛 광선 단 표(<see cref="RoadGrade.RoadBench"/>)는 옛 엔진 면의 광선이라
/// 고리 면과 굽은 곳에서 2 cm 어긋났다(키트 rwring 시제품).</summary>
public static partial class RoadRing
{
    /// <summary>단 k 높이 = 그 점을 이긴 원천(변 · 꼭짓점 · 이어 받는 원뿔)의 후보 값에서 남은 단면 높이만큼(<see cref="Level"/>) —
    /// 소단 띠 안이면 면 값 그대로 · 사면 위면 그 단면의 소단 높이 · 단 간격 = 단높이 그대로. 변 원천은 소단이 평평(발 높이 + rise_k).</summary>
    public sealed class RingBench : WallByLine.BenchLevels
    {
        readonly Evaluator _ev; readonly bool _up; readonly int _m;
        readonly double[] _rise, _h, _dIn, _dOut;
        readonly double _benchW;
        readonly double[] _turn;   // 둘레 꼭짓점까지 누적 꺾임(rad)
        readonly ConcurrentDictionary<(long, long), (Win W, byte Kind)> _win = new();
        string _why = "";
        /// <summary>이어짐 셈 — 쌍 · 다른 다리 · 높이 못 읽음 · 1 cm 넘음 · 단 높이 못 읽은 점 · 이어 받는 원뿔이 정한 점.</summary>
        public int JumpPairs, JumpLegDiff, JumpNoLv, JumpOverN, JumpValley, LevelNaN, LevelRelay, LevelCalls;

        public RingBench(Built b, RoadGrade.SlopeRule rule, bool up)
        {
            _ev = b.Ev; _up = up; _m = b.Ev.D.B.Count; _benchW = Math.Max(0, rule.BenchW);
            var p = up ? _ev.Pc : _ev.Pf;
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
            // 둘레 누적 꺾임 — 같은 쪽 두 원천이 한 다리인가(옛 SameLeg: 꺾임 ≤ 100°)
            var B = _ev.D.B; _turn = new double[_m + 1];
            for (int i = 1; i <= _m; i++)
            {
                int a = (i - 1 + _m) % _m, c = i % _m, n = (i + 1) % _m;
                double h0 = Math.Atan2(B[c].Y - B[a].Y, B[c].X - B[a].X), h1 = Math.Atan2(B[n].Y - B[c].Y, B[n].X - B[c].X), da = h1 - h0;
                while (da > Math.PI) da -= 2 * Math.PI; while (da < -Math.PI) da += 2 * Math.PI;
                _turn[i] = _turn[i - 1] + Math.Abs(da);
            }
        }

        public override bool Up => _up;
        /// <summary>μ = 최대 종단 + 0.01(셈의 굽음 벌점과 같은 값).</summary>
        public override double MaxGrad => _ev.Mu;
        public override int K => _rise.Length;
        public override double TerraceW => _benchW;
        public override double BenchW => _benchW;
        public override int TierCount => _rise.Length;
        public override (double Rise, double H, bool Terrace) Tier(int k) { int i = Math.Clamp(k, 1, Math.Max(1, _rise.Length)) - 1; return _rise.Length == 0 ? (0, 0, false) : (_rise[i], _h[i], false); }
        public override string Name => "고리 면";
        public override bool MidBench => true;
        public override string JumpWhy => _why;

        /// <summary>그 점을 이긴 원천(절토 = 위 · 성토 = 아래) · Kind — 도로 면 위 · 원천 없음이면 Src −1.</summary>
        public (Win W, byte Kind) WinAt(double x, double y)
        {
            var key = (BitConverter.DoubleToInt64Bits(x), BitConverter.DoubleToInt64Bits(y));
            if (_win.TryGetValue(key, out var o)) return o;
            var q = _ev.Eval(x, y);
            // ★[v116.7 · wall2 검토 M2] 부딪힘(Kind 4 · lo > hi)이면 면 = hi(절토 원천) — 성토 단 표가 lo 원천을 읽으면 틀린 높이 → 원천 없음(성토 벽은 그 자리를 못 지남 · 까닭 «부딪히는 자리»)
            var w = q.Kind == 1 || (q.Kind == 4 && !_up) ? new Win { Src = -1, V = double.NaN } : _up ? q.WHi : q.WLo;
            if (w.Src < 0 || !double.IsFinite(w.V)) w = new Win { Src = -1, V = double.NaN };
            var v = (w, q.Kind);
            _win[key] = v;
            return v;
        }

        /// <summary>이긴 원천의 측점(벽 선 · 노리선 기록용) — 못 읽으면 NaN.</summary>
        public double StationAt(double x, double y) { var (w, _) = WinAt(x, y); return w.Src < 0 ? double.NaN : SigToStation(_ev.D, w.Sigma).S; }

        /// <summary>원천의 둘레 쪽(−1 좌 · +1 우 · 0 마구리 · 없음).</summary>
        public int SideAt(double x, double y) { var (w, _) = WinAt(x, y); return w.Src < 0 ? 0 : _ev.SourceSide(w.Src); }

        public override double Level(int k, double x, double y)
        {
            Interlocked.Increment(ref LevelCalls);
            if (k < 1 || k > _rise.Length) return double.NaN;
            var (w, _) = WinAt(x, y);
            if (w.Src < 0 || !double.IsFinite(w.Dist)) { Interlocked.Increment(ref LevelNaN); return double.NaN; }
            if (w.Src >= 2 * _m) Interlocked.Increment(ref LevelRelay);
            // 그 점 면 값(이긴 원천 후보 값 V)에서 남은 단면 높이만큼 — 절토 = V − F(d) + R_k · 성토 = V + F(d) − R_k.
            //   소단 띠 안이면 F(d) = R_k라 V 그대로(규칙 검사 정확) · 굽음 벌점(꼭짓점 μτ · 이어 받는 원뿔 굽음)은 그 점 자리 값이라 단 높이 기울기 ≤ μ
            //   (벌점을 소단 자리 반지름에서 세면 꼭짓점 곁 벽에서 반지름 비만큼 커져 벽 소단이 33 % 기울었다 — 키트 rwall 1110)
            double F = (_up ? _ev.Pc : _ev.Pf).F(w.Dist), sg = _up ? 1 : -1;
            return w.V + sg * (_rise[k - 1] - F);
        }

        /// <summary>규칙 검사 범위 — 변 · 꼭짓점 원천 거리가 소단 k 구간 안(이어 받는 원뿔은 거짓 — 거리 뜻이 꺾여 규칙 검사 안 함 · 옛 꼬리 광선과 같은 대우).</summary>
        public override bool InBand(int k, double x, double y)
        {
            if (k < 1 || k > _rise.Length || _dOut[k - 1] - _dIn[k - 1] <= 1e-6) return false;
            var (w, _) = WinAt(x, y);
            if (w.Src < 0 || w.Src >= 2 * _m) return false;
            return w.Dist >= _dIn[k - 1] - 1e-9 && w.Dist <= _dOut[k - 1] + 1e-9;
        }

        /// <summary>단 k 소단 구간(원천 거리).</summary>
        public (double In, double Out) BandD(int k) => k >= 1 && k <= _rise.Length ? (_dIn[k - 1], _dOut[k - 1]) : (double.NaN, double.NaN);

        double SigDiff(double a, double b) { double P = _ev.D.Perimeter, d = Math.Abs(a - b); return P > 0 ? Math.Min(d, P - d) : d; }
        double TurnAtSig(double s)
        {
            var c = _ev.D.Cum; if (c.Length < 2) return 0;
            double P = c[^1]; if (P > 0) { s %= P; if (s < 0) s += P; }
            int lo = 0, hi = c.Length - 1;
            while (lo + 1 < hi) { int md = (lo + hi) >> 1; if (c[md] <= s) lo = md; else hi = md; }
            return _turn[Math.Clamp(lo, 0, _m)];
        }

        /// <summary>줄 안 이웃 두 점 — 다른 다리(원천 둘레 자리 차가 거리 + 1 m 넘고 · 같은 쪽 100° 안 꺾임도 아님)면 NaN ·
        /// 아니면 |Δz| − μ·max(거리, 원천 둘레 자리 차)(옛 도로 단 표: 종단 경사 × 측점 차와 같은 뜻 — 굽은 안쪽은 두 점 원천이 멀다)
        /// </summary>
        public override double JumpOver(int k, double x0, double y0, double za, double x1, double y1, double zb, double dist)
        {
            Interlocked.Increment(ref JumpPairs);
            var (w0, k0) = WinAt(x0, y0); var (w1, k1) = WinAt(x1, y1);
            if (!double.IsFinite(za) || !double.IsFinite(zb) || w0.Src < 0 || w1.Src < 0)
            {
                Interlocked.Increment(ref JumpNoLv);
                _why = !_up && (k0 == 4 || k1 == 4) ? "절토 · 성토 사면이 부딪히는 자리를 지납니다(그 자리 면은 절토 사면)"
                     : k0 == 1 || k1 == 1 ? "고리 면 단 높이를 못 읽음(도로 면 위)" : "고리 면 단 높이를 못 읽음(원천 없음 — 셈 범위 밖)";
                return double.NaN;
            }
            double ds = SigDiff(w0.Sigma, w1.Sigma);
            if (ds > dist + 1.0)
            {
                int s0 = _ev.SourceSide(w0.Src), s1 = _ev.SourceSide(w1.Src);
                bool same = s0 != 0 && s0 == s1 && Math.Abs(TurnAtSig(w0.Sigma) - TurnAtSig(w1.Sigma)) <= 1.75;
                if (!same)
                {
                    Interlocked.Increment(ref JumpLegDiff);
                    _why = string.Format(CultureInfo.InvariantCulture, "두 점의 주인 다리가 다름(고리 둘레 자리 {0:F1} · {1:F1} · 차 {2:F1} m)", w0.Sigma, w1.Sigma, ds);
                    return double.NaN;
                }
            }
            double over = Math.Abs(zb - za) - _ev.Mu * Math.Max(dist, ds);
            if (over > 0.01 && w0.Src != w1.Src)
            {
                // ★[v116.7 · 키트 rwsweep] 두 원천이 갈리는 자리에서 단 높이가 뜀 = 두 사면이 만나는 골(굽은 안쪽 · 높이가 다른 두 도로 자리) — 두 쪽 옛 소단이 이 높이만큼 어긋나
                //   이어진 벽 소단을 못 만든다(지으면 소단 가운데 절벽). 까닭을 알기 쉽게(옛 문구 «링 이등분선 · 접힘 곁»)
                Interlocked.Increment(ref JumpValley);
                _why = string.Format(CultureInfo.InvariantCulture, "두 사면이 만나는 골을 지납니다(양쪽 옛 소단 높이가 {0:F2} m 다름) — 선을 골 한쪽 사면에만 그리세요", Math.Abs(zb - za));
                lock (_win) if (over > _worst) { _worst = over; _worstTxt = string.Format(CultureInfo.InvariantCulture, "골 단 {13} ({0:F2},{1:F2}) {2} d {3:F2} σ {4:F1} z {5:F3} ↔ ({6:F2},{7:F2}) {8} d {9:F2} σ {10:F1} z {11:F3} · 거리 {12:F2}",
                    x0, y0, SrcName(w0.Src), w0.Dist, w0.Sigma, za, x1, y1, SrcName(w1.Src), w1.Dist, w1.Sigma, zb, dist, k); }
                return double.NaN;
            }
            if (over > 0.01)
            {
                Interlocked.Increment(ref JumpOverN);
                lock (_win) if (over > _worst) { _worst = over; _worstTxt = string.Format(CultureInfo.InvariantCulture, "단 {13} ({0:F2},{1:F2}) {2} d {3:F2} σ {4:F1} z {5:F3} ↔ ({6:F2},{7:F2}) {8} d {9:F2} σ {10:F1} z {11:F3} · 거리 {12:F2}",
                    x0, y0, SrcName(w0.Src), w0.Dist, w0.Sigma, za, x1, y1, SrcName(w1.Src), w1.Dist, w1.Sigma, zb, dist, k); }
            }
            return over;
        }

        double _worst; string _worstTxt = "";
        string SrcName(int s) => s < 0 ? "없음" : s < _m ? "변" + s : s < 2 * _m ? "꼭" + (s - _m) : "이" + (s - 2 * _m);
        public override string Text => (_worstTxt.Length > 0 ? "[최악 " + _worstTxt + "] " : "") + string.Format(CultureInfo.InvariantCulture,
            "{0} 고리 면 · 단 {1}(R {2}) · μ {3:F3} · 단 높이 {4}점(못 읽음 {5} · 이어 받는 원뿔 {6}) · 이어짐 쌍 {7}(다리 다름 {8} · 못 읽음 {9} · 골 {11} · 1 cm 넘음 {10})",
            _up ? "절토" : "성토", _rise.Length, string.Join("/", _rise.Take(4).Select(v => v.ToString("0.##", CultureInfo.InvariantCulture))), _ev.Mu,
            LevelCalls, LevelNaN, LevelRelay, JumpPairs, JumpLegDiff, JumpNoLv, JumpOverN, JumpValley);
    }
}
