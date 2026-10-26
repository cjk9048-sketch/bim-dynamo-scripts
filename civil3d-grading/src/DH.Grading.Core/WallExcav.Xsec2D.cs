using System.Globalization;
using NetTopologySuite.Algorithm.Locate;
using NetTopologySuite.Geometries;
using Tri = DH.Grading.Core.WallDaylight.Tri;

namespace DH.Grading.Core;

/// <summary>★★★[v105.3 · 계획_v105_3.md · JACK 1026 «가로지르는 벽만 (노란 선)»] 횡단 옹벽 터파기선 · 배면사면 · 구조물 공제를 <b>그 횡단에 그린 벽만으로</b>(2D 실무 방식) 짓는다.
/// <para>벽 = <see cref="CutSections(SecRecord, double, double, double, double, double, out int)"/>(그림과 같은 함수 · 같은 sinθ 하한 · 같은 늘임) — 기초 밑은 그린 기초 바닥(버림 밑)에 붙고,
/// 바닥 앞 · 뒤 끝(단면 끝 + 여유)에서 1:m(+소단)으로 땅까지. 단면과 거의 나란한 벽의 터파기 · 구조물은 이 횡단에 없다(그 벽은 그 벽을 가로지르는 노선으로 센다).</para>
/// <para>그림(DHXVIEW)과 수량(QtyAt)이 같은 함수 · 같은 입력으로 짓는다. 3D 터파기면(옹벽터파기_DH)은 종단 · 3D용(<see cref="ConeLower"/>로 벽 끝 절벽만 고침).</para></summary>
public static partial class WallExcav
{
    /// <summary>터파기 법면 제원 — 경사 1:N · 소단 켬이면 높이 BenchH마다 폭 BenchW 평평(3D 조각 <c>Params</c>와 같은 꼴 · 오름은 그 자리 바닥에서 잰다).</summary>
    public readonly record struct SlopeSpec(double N, bool BenchOn, double BenchH, double BenchW)
    {
        double Nn => Math.Max(1e-6, N);
        bool Benched => BenchOn && BenchH > 1e-9 && BenchW > 1e-9;
        double Cycle => BenchH * Nn + BenchW;

        /// <summary>바닥 끝에서 벽에 직각인 참 거리 <paramref name="d"/>만큼 바깥의 오름.</summary>
        public double Rise(double d)
        {
            if (!(d > 0)) return 0;
            if (!Benched) return d / Nn;
            double k = Math.Floor(d / Cycle), r = d - k * Cycle;
            return k * BenchH + Math.Min(r / Nn, BenchH);
        }

        /// <summary>오름의 아래 한계 기울기 — 소단 끝을 잇는 선(h(d) ≥ d × 이것).</summary>
        public double MinGrade => Benched ? BenchH / Cycle : 1 / Nn;

        /// <summary>0 &lt; d ≤ <paramref name="dMax"/> 안 꺾임 거리(소단 시작 · 끝).</summary>
        public IEnumerable<double> Kinks(double dMax)
        {
            if (!Benched) yield break;
            for (int k = 0; k < 100000; k++)
            {
                double a = k * Cycle + BenchH * Nn, b = (k + 1) * Cycle;
                if (a > dMax) yield break;
                yield return a;
                if (b > dMax) yield break;
                yield return b;
            }
        }
    }

    /// <summary>벽 하나의 2D 터파기 단면(횡단 축 위) — 바닥 [FLo, FHi] 높이 Zf · 법면 · 데이라잇 [DayLo, DayHi] 밖은 없음.</summary>
    public sealed class Prof2D
    {
        public CutSec C = null!;
        /// <summary>앞면 선(단면 x = 0)의 축 자리.</summary>
        public double A0;
        public double Zf, FLo, FHi, DayLo, DayHi;
        /// <summary>그 쪽으로 법면이 있다(도랑 뒤 = 없음 — 벽 앞면이 깎기면).</summary>
        public bool OpenLo, OpenHi;
        /// <summary>단면 앞면 선 줄의 앞면 구배(정지면 · 쐐기 공제).</summary>
        public double SlopeW;
        internal SlopeSpec S;

        /// <summary>데이라잇을 안 보고 법면을 끝없이 늘인 높이(닫힌 쪽 바깥은 NaN).</summary>
        public double Raw(double a)
        {
            if (a >= FLo && a <= FHi) return Zf;
            if (a < FLo) return OpenLo ? Zf + S.Rise((FLo - a) / C.Stretch) : double.NaN;
            return OpenHi ? Zf + S.Rise((a - FHi) / C.Stretch) : double.NaN;
        }

        /// <summary>축 a의 높이 — 데이라잇 밖이면 NaN. ★[V48] 1 µm 여유 — 꺾임점 합치기(1e-7)가 데이라잇 바로 밖 점을 구간 끝으로 남겨, 그 끝에서 «데이라잇인가»가 NaN으로 빠져 배면이 하나 사라졌다
        /// (데이라잇 밖은 법면 ≥ 땅이라 E는 어차피 NaN).</summary>
        public double Z(double a) => a < DayLo - 1e-6 || a > DayHi + 1e-6 ? double.NaN : Raw(a);
    }

    /// <summary>배면 하나 — 데이라잇 Day(축)에서 벽 앞면 선 Wall 쪽으로 1:n(축 기울기 K) · 벽 꼭대기 T · 닿은 자리 Contact(없으면 NaN).</summary>
    public sealed class Back2D
    {
        public double Wall, Day, LDay, T, K, Stretch;
        public double Contact = double.NaN;
        public double Lo => Math.Min(Wall, Day);
        public double Hi => Math.Max(Wall, Day);
    }

    /// <summary>한 횡단의 2D 터파기 · 배면.</summary>
    public sealed class Xs2D
    {
        public List<Prof2D> Profs = new();
        /// <summary>꺾임 · 구간 끝(정렬 · 1e-7 안 합침) — 수량 표본 축과 그림 꼭짓점에 넣는다.</summary>
        public List<double> Breaks = new();
        /// <summary>E가 있는 구간(축).</summary>
        public List<(double Lo, double Hi)> Spans = new();
        public List<Back2D> Backs = new();
        /// <summary>단면과 거의 나란해 뺀 벽 교점 수.</summary>
        public int Skipped;
        public double AMin, AMax;
        internal Func<double, double> Gf = null!, Pf = null!, Sf = null!;

        /// <summary>땅 = Lower(원지반, 계획면)(없으면 있는 쪽 · 둘 다 없으면 NaN).</summary>
        public double Lz(double a)
        {
            double g = Gf(a), p = Pf(a);
            return double.IsNaN(g) ? p : double.IsNaN(p) ? g : Math.Min(g, p);
        }

        /// <summary>그린 벽 프로파일 최솟값(땅을 안 본다) · 없으면 NaN.</summary>
        public double EMin(double a, out int arg)
        {
            double best = double.NaN; arg = -1;
            for (int i = 0; i < Profs.Count; i++)
            {
                double z = Profs[i].Z(a);
                if (!double.IsNaN(z) && (double.IsNaN(best) || z < best)) { best = z; arg = i; }
            }
            return best;
        }

        /// <summary>축 a의 터파기 높이 — 땅보다 낮을 때만(아니면 NaN · 터파기 아님).</summary>
        public double E(double a)
        {
            double z = EMin(a, out _);
            if (double.IsNaN(z)) return double.NaN;
            double l = Lz(a);
            return double.IsNaN(l) || z >= l - 1e-9 ? double.NaN : z;
        }

        /// <summary>배면 높이(Ω 밖이면 NaN) · <paramref name="flat"/> = 벽 곁 0.5 m 안인데 꼭대기보다 1 cm 넘게 높음(평탄 없이 벽에 닿음 ⚠ — 3D BackAt과 같은 뜻).</summary>
        public double B(double a, out bool inside, out bool flat) => B(a, out inside, out flat, out _);

        /// <param name="code">이긴 항 — 0 밖 · 1 터파기선 · 2 닿은 뒤 평탄 · 3 평탄 · 4 직선(꺾임 찾기용).</param>
        internal double B(double a, out bool inside, out bool flat, out int code)
        {
            inside = false; flat = false; code = 0;
            foreach (var bk in Backs)
            {
                if (a < bk.Lo - 1e-9 || a > bk.Hi + 1e-9) continue;
                double e = E(a);
                if (double.IsNaN(e)) return double.NaN;
                double g = Gf(a), sv = Sf(a);
                // 벽 뒤 설계 사면(정지면_DH ≠ 원지반) — 그 면이 되메우기 윗면(3D 가면 Natural과 같은 면 · 같은 1 cm — 계획 검토 높음 2: 정지순수옹벽_DH는 벽 뒤 패드 높이로 깎인 자리가 있어 V39 ④가 돌아온다)
                if (!double.IsNaN(g) && !double.IsNaN(sv) && Math.Abs(sv - g) > 0.01) return double.NaN;
                double l = Lz(a);
                double s = Math.Abs(a - bk.Day), d = bk.LDay - s * bk.K;
                bool after = !double.IsNaN(bk.Contact) && s > Math.Abs(bk.Contact - bk.Day) + 1e-12;
                double top = after ? bk.T : Math.Max(bk.T, d);
                double b = Math.Max(e, Math.Min(l, top));
                if (!(b < l - 1e-9)) return double.NaN;
                inside = true;
                code = b <= e + 1e-12 ? 1 : after ? 2 : d <= bk.T ? 3 : 4;
                flat = Math.Abs(a - bk.Wall) < 0.5 * bk.Stretch && b > bk.T + 0.01;
                return b;
            }
            return double.NaN;
        }

        /// <summary>뜻이 바뀌는 자리(분류 값이 바뀌는 곳)를 <paramref name="step"/> 걸음 + 이분법(1e-7 m)으로 찾아 넣는다.</summary>
        internal static void Flips(double lo, double hi, double step, Func<double, int> cls, List<double> outv)
        {
            if (!(hi > lo)) return;
            int m = Math.Max(1, (int)Math.Ceiling((hi - lo) / step));
            double prevA = lo; int prev = cls(lo);
            for (int k = 1; k <= m; k++)
            {
                double a = k == m ? hi : lo + (hi - lo) * k / m;
                int c = cls(a);
                if (c != prev)
                {
                    double x0 = prevA, x1 = a;
                    for (int it = 0; it < 40 && x1 - x0 > 1e-7; it++) { double mid = (x0 + x1) / 2; if (cls(mid) == prev) x0 = mid; else x1 = mid; }
                    // ★[설치 전 검토 높음 1] 오른쪽 점은 2 µm 안으로 — x0 · x1(1e-7 안)을 넣으면 꺾임점 합치기(1e-7 · 수량 1e-6)가 작은 쪽(왼쪽 = 경계 밖)만 남겨
                    //   왼쪽 데이라잇 · Ω 왼쪽 끝에서 그림 첫 꼭짓점이 다음 꺾임까지 밀렸다(배면 1:n 직선 6 m 빠짐 · 수량 첫 칸 빠짐)
                    outv.Add(x0); outv.Add(Math.Min(hi, x1 + 2e-6));
                }
                prevA = a; prev = c;
            }
        }

        // ── 구조물 공제(그린 벽 단면만 — ColumnAt과 같은 조각 법) ──────────────────────

        /// <summary>축 a의 세로 기둥 — 그린 벽 단면마다 x = (a − A0)·Sign/Stretch 세로선이 지나는 조각의 z 구간(합집합 · 두 번 안 뺀다).</summary>
        public void Column(double a, List<(double Lo, double Hi)> st, List<(double Lo, double Hi)> air, List<(double Lo, double Hi)> rf,
                           List<(double Lo, double Hi, double F)>? frac = null, List<(double Lo, double Hi)>? key = null)
        {
            st.Clear(); air.Clear(); rf.Clear(); frac?.Clear(); key?.Clear();
            foreach (var p in Profs)
            {
                var c = p.C; var sh = c.Shape;
                double x = (a - p.A0) * c.Sign / c.Stretch;
                foreach (var part in sh.Parts)
                {
                    if (part.Structure && part.Fraction < 1 - 1e-12)
                    {
                        if (frac == null) continue;
                        var tmp = new List<(double, double)>(2);
                        CrossZ(part.Ring, x, c.Toe, tmp);
                        foreach (var (lo, hi) in tmp) frac.Add((lo, hi, part.Fraction));
                        continue;
                    }
                    CrossZ(part.Ring, x, c.Toe, part.Structure ? st : rf);
                    if (key != null && part.Material == "활동방지벽") CrossZ(part.Ring, x, c.Toe, key);
                }
                double fb = sh.FaceBatter, sw = Math.Max(0, p.SlopeW), zTop = sh.HTotal - sh.Df;
                if (!sh.Trench && x > 0 && fb > sw + 1e-9)
                {
                    double lo = x / fb, hi = sw > 1e-9 ? x / sw : double.PositiveInfinity;
                    lo = Math.Max(0, lo); hi = Math.Min(zTop, hi);
                    if (hi > lo + 1e-9) air.Add((c.Toe + lo, c.Toe + hi));
                }
            }
            Merge(st);
            if (key != null) Merge(key);
            Merge(air); Subtract(air, st);
            Merge(rf); Subtract(rf, st); Subtract(rf, air);
            if (frac != null && frac.Count > 1)
            {
                frac.Sort((u, v) => u.Lo.CompareTo(v.Lo));
                var mm = new List<(double Lo, double Hi, double F)> { frac[0] };
                for (int i = 1; i < frac.Count; i++)
                {
                    var (lo, hi, f) = mm[^1];
                    if (frac[i].Lo <= hi) mm[^1] = (lo, Math.Max(hi, frac[i].Hi), Math.Max(f, frac[i].F)); else mm.Add(frac[i]);
                }
                frac.Clear(); frac.AddRange(mm);
            }
        }

        /// <summary>[lo, hi](터파기 바닥 ~ 그 칸 지표) 안 공제 넓이 — <see cref="Integrate(SecRecord, double, double, double, double, double, double, double[], double[], double[], double)"/>와 같은 칸 법(그린 벽만).</summary>
        public ColumnSum Integrate(double[] xs, double[] lo, double[] hi, double step = 0.01)
        {
            var sum = new ColumnSum { Lines = Profs.Count };
            if (Profs.Count == 0 || xs.Length < 2) return sum;
            var st = new List<(double, double)>(); var air = new List<(double, double)>(); var rf = new List<(double, double)>();
            var frac = new List<(double Lo, double Hi, double F)>();
            for (int k = 0; k + 1 < xs.Length; k++)
            {
                if (double.IsNaN(lo[k]) || double.IsNaN(lo[k + 1]) || double.IsNaN(hi[k]) || double.IsNaN(hi[k + 1])) continue;
                double w = xs[k + 1] - xs[k];
                if (w <= 0) continue;
                int m = Math.Max(1, (int)Math.Ceiling(w / step - 1e-9));
                double dx = w / m;
                for (int j = 0; j < m; j++)
                {
                    double x = xs[k] + (j + 0.5) * dx, f = (x - xs[k]) / w;
                    double zl = lo[k] + (lo[k + 1] - lo[k]) * f, zh = hi[k] + (hi[k + 1] - hi[k]) * f;
                    if (zh <= zl) continue;
                    Column(x, st, air, rf, frac);
                    if (st.Count == 0 && air.Count == 0 && rf.Count == 0 && frac.Count == 0) continue;
                    sum.Struct += Within(st, zl, zh) * dx;
                    foreach (var (fa, fb, ff) in frac)
                    {
                        double x0 = Math.Max(fa, zl), x1 = Math.Min(fb, zh);
                        if (x1 <= x0) continue;
                        sum.Struct += ff * (x1 - x0 - Within(st, x0, x1)) * dx;
                    }
                    sum.Air += Within(air, zl, zh) * dx;
                    sum.Reinf += Within(rf, zl, zh) * dx;
                }
            }
            return sum;
        }

        /// <summary>활동방지벽 자리(축 구간 · 겹치면 합침) · <paramref name="corners"/> = 키 링 꼭짓점 축 자리(기운 밑면 따라가기).</summary>
        public List<(double Lo, double Hi)> KeySpans(List<double>? corners = null)
        {
            var res = new List<(double Lo, double Hi)>();
            foreach (var p in Profs)
            {
                var c = p.C;
                var k = c.Shape.Parts.FirstOrDefault(q => q.Material == "활동방지벽");
                if (k == null) continue;
                double a0 = p.A0 + c.Sign * k.Ring.Min(q => q.X) * c.Stretch, a1 = p.A0 + c.Sign * k.Ring.Max(q => q.X) * c.Stretch;
                res.Add((Math.Min(a0, a1), Math.Max(a0, a1)));
                if (corners != null) foreach (var q in k.Ring) corners.Add(p.A0 + c.Sign * q.X * c.Stretch);
            }
            Merge(res);
            return res;
        }

        /// <summary>축 a 기둥의 활동방지벽 밑(가장 낮은 z) — 없으면 NaN.</summary>
        public double KeyBottomAt(double a)
        {
            var st = new List<(double Lo, double Hi)>(); var air = new List<(double Lo, double Hi)>(); var rf = new List<(double Lo, double Hi)>(); var key = new List<(double Lo, double Hi)>();
            Column(a, st, air, rf, null, key);
            return key.Count == 0 ? double.NaN : key.Min(q => q.Lo);
        }

        /// <summary>터파기선의 참 기울기(축 기울기 × 그 자리 벽의 늘임 = 벽에 직각인 기울기) — 기울기 라벨(설정 1:m과 견줌)용 · 없으면 NaN.</summary>
        public double EGrad(double a, double h = 0.05)
        {
            double z0 = E(a - h), z1 = E(a + h);
            if (double.IsNaN(z0) || double.IsNaN(z1)) return double.NaN;
            EMin(a, out int arg);
            return arg < 0 ? double.NaN : Math.Abs(z1 - z0) / (2 * h) * Profs[arg].C.Stretch;
        }

        /// <summary>배면선의 참 기울기(축 기울기 × 그 배면 벽의 늘임) · 없으면 NaN.</summary>
        public double BGrad(double a, double h = 0.05)
        {
            double z0 = B(a - h, out bool i0, out _), z1 = B(a + h, out bool i1, out _);
            if (!i0 || !i1) return double.NaN;
            var bk = Backs.FirstOrDefault(q => a >= q.Lo - 1e-9 && a <= q.Hi + 1e-9);
            return bk == null ? double.NaN : Math.Abs(z1 - z0) / (2 * h) * bk.Stretch;
        }

        public string Text => string.Format(CultureInfo.InvariantCulture, "2D 벽 {0}(나란해 뺀 {1}) · 구간 {2} · 배면 {3}{4}",
            Profs.Count, Skipped, Spans.Count, Backs.Count, Backs.Any(b => !double.IsNaN(b.Contact)) ? $"(닿음 {Backs.Count(b => !double.IsNaN(b.Contact))})" : "");
    }

    /// <summary>★[v105.3] 한 횡단(축 −wl ~ wr · 왼끝 (lx, ly) → 오른끝 (rx, ry))의 2D 터파기 · 배면을 짓는다.</summary>
    /// <param name="G">축 → 원지반(없으면 NaN).</param>
    /// <param name="P">축 → 계획면(정지순수옹벽_DH — 그린 흰 선 · 수량 상한 · 없으면 NaN).</param>
    /// <param name="S">축 → 정지면_DH(자연 땅 판정 = abs(S − G) ≤ 1 cm · 없으면 NaN = 판정 안 함).</param>
    /// <param name="backN">배면 경사 1:n(배면 끔 · 기록 없음이면 null).</param>
    /// <param name="minSin">벽과 자르는 선 사잇각 sin 하한 — 그림(<c>WallSectionDraw.MinSin</c>)과 같은 값.</param>
    public static Xs2D Excav2D(SecRecord rec, double lx, double ly, double rx, double ry, double wl, double wr,
                               Func<double, double> G, Func<double, double> P, Func<double, double> S, SlopeSpec sp, double? backN, double minSin)
    {
        var X = new Xs2D { Gf = G, Pf = P, Sf = S, AMin = -wl, AMax = wr };
        var cuts = CutSections(rec, lx, ly, rx, ry, minSin, out int sk);
        X.Skipped = sk;
        double margin = Math.Max(0, rec.Margin);
        var br = new List<double> { -wl, wr };
        foreach (var c in cuts)
        {
            var sh = c.Shape;
            double a0 = -wl + c.Dist;
            double xF = -Math.Max(0, -sh.XFront + margin), xB = sh.Trench ? 0 : Math.Max(0, sh.XBack + margin);
            double aF = a0 + c.Sign * xF * c.Stretch, aB = a0 + c.Sign * xB * c.Stretch;
            var p = new Prof2D
            {
                C = c, A0 = a0, Zf = c.Toe + sh.ZBottom, FLo = Math.Min(aF, aB), FHi = Math.Max(aF, aB), S = sp,
                SlopeW = c.Line >= 0 && c.Line < rec.Lines.Count ? rec.Lines[c.Line].SlopeW : 0,
            };
            // 도랑(패널식) — 뒤(흙 쪽)는 법면 없음(벽 앞면 = 깎기면 · 수직)
            p.OpenLo = !(sh.Trench && c.Sign < 0);
            p.OpenHi = !(sh.Trench && c.Sign > 0);
            p.DayLo = p.OpenLo ? Daylight(p, X, -1) : p.FLo;
            p.DayHi = p.OpenHi ? Daylight(p, X, +1) : p.FHi;
            X.Profs.Add(p);
            br.Add(p.FLo); br.Add(p.FHi); br.Add(p.DayLo); br.Add(p.DayHi);
            foreach (var (open, end, dir, day) in new[] { (p.OpenLo, p.FLo, -1, p.DayLo), (p.OpenHi, p.FHi, +1, p.DayHi) })
            {
                if (open) { foreach (double d in sp.Kinks(Math.Abs(day - end) / c.Stretch)) br.Add(end + dir * d * c.Stretch); }
                else { br.Add(end - dir * 1e-4); br.Add(end + dir * 1e-4); }   // 도랑 뒤 수직 — 양쪽 0.1 mm(수량 칸이 수직을 따라가게)
            }
        }
        if (X.Profs.Count > 0)
        {
            double lo = Math.Max(-wl, X.Profs.Min(q => q.DayLo)), hi = Math.Min(wr, X.Profs.Max(q => q.DayHi));
            // 두 벽 프로파일이 바뀌는 자리 · 땅과 만나는 자리(터파기 있음 · 없음)
            Xs2D.Flips(lo, hi, 0.02, a => { double z = X.EMin(a, out int arg); if (double.IsNaN(z)) return -1; double l = X.Lz(a); return double.IsNaN(l) || z >= l - 1e-9 ? -2 : arg; }, br);
        }
        X.Breaks = Clean(br, -wl, wr);
        // 구간
        for (int i = 0; i + 1 < X.Breaks.Count; i++)
        {
            double a = X.Breaks[i], b = X.Breaks[i + 1];
            if (double.IsNaN(X.E((a + b) / 2))) continue;
            if (X.Spans.Count > 0 && Math.Abs(X.Spans[^1].Hi - a) < 1e-9) X.Spans[^1] = (X.Spans[^1].Lo, b); else X.Spans.Add((a, b));
        }
        // 배면 — 구간 끝(데이라잇)마다 안쪽으로 처음 만나는 벽이 흙 쪽을 이 끝으로 둔 절토 · 도랑 아닌 벽이면
        if (backN is double n && n > 0)
        {
            foreach (var (lo, hi) in X.Spans)
                foreach (double e in new[] { lo, hi })
                {
                    double zMin = X.EMin(e, out _), le = X.Lz(e);
                    if (double.IsNaN(zMin) || double.IsNaN(le) || Math.Abs(zMin - le) > 1e-3) continue;   // 데이라잇 아님(도랑 수직 · 뷰 끝)
                    Prof2D? first = null;
                    foreach (var p in X.Profs)
                        if (p.A0 >= lo - 1e-9 && p.A0 <= hi + 1e-9 && (first == null || Math.Abs(p.A0 - e) < Math.Abs(first.A0 - e))) first = p;
                    if (first == null || !first.C.Up || first.C.Shape.Trench || first.C.Sign * (e - first.A0) <= 0) continue;
                    var sh = first.C.Shape;
                    var bk = new Back2D { Wall = first.A0, Day = e, LDay = le, T = first.C.Toe + sh.HTotal - sh.Df, K = 1 / (n * first.C.Stretch), Stretch = first.C.Stretch };
                    // 닿음 — e에서 벽 쪽으로 1 cm 걸음: 처음 D &lt; E − 0.01(직선이 터파기선 1 cm 넘게 아래 · v105.2와 같은 문턱) · D가 꼭대기 아래로 가면 그만(그 뒤는 어느 쪽이든 평탄)
                    double dir = Math.Sign(first.A0 - e), len = Math.Abs(first.A0 - e);
                    double Gap(double q) { double ev = X.E(e + dir * q); return double.IsNaN(ev) ? double.NaN : (le - q * bk.K) - (ev - 0.01); }
                    double sPrev = 0;
                    for (double s = 0.01; s <= len + 1e-9; s += 0.01)
                    {
                        if (le - s * bk.K < bk.T - 0.01) break;
                        double gp = Gap(s);
                        if (!double.IsNaN(gp) && gp < 0)
                        {
                            double s0 = sPrev, s1 = s;
                            for (int it = 0; it < 40 && s1 - s0 > 1e-7; it++) { double m = (s0 + s1) / 2; double gm = Gap(m); if (!double.IsNaN(gm) && gm < 0) s1 = m; else s0 = m; }
                            bk.Contact = e + dir * s1;
                            break;
                        }
                        sPrev = s;
                    }
                    X.Backs.Add(bk);
                }
            if (X.Backs.Count > 0)
            {
                var bb = new List<double>(X.Breaks);
                foreach (var bk in X.Backs)
                {
                    bb.Add(bk.Wall); bb.Add(bk.Day);
                    if (!double.IsNaN(bk.Contact)) bb.Add(bk.Contact);
                    double sT = (bk.LDay - bk.T) / bk.K;
                    if (sT > 0 && sT < Math.Abs(bk.Wall - bk.Day)) bb.Add(bk.Day + Math.Sign(bk.Wall - bk.Day) * sT);
                    Xs2D.Flips(bk.Lo, bk.Hi, 0.02, a => { X.B(a, out _, out _, out int code); return code; }, bb);
                }
                X.Breaks = Clean(bb, -wl, wr);
            }
        }
        return X;
    }

    /// <summary>바닥 끝에서 바깥으로(<paramref name="dir"/>) 0.05 m 걸음 — 처음 법면 ≥ 땅인 자리(이분법 · 1e-7 m) · 땅이 없으면 마지막으로 잰 자리 · 뷰 끝이면 뷰 끝.
    /// 바닥 끝이 이미 땅 위면 바닥 끝(그 쪽 법면 없음).</summary>
    static double Daylight(Prof2D p, Xs2D X, int dir)
    {
        double end = dir < 0 ? p.FLo : p.FHi, lim = dir < 0 ? X.AMin : X.AMax;
        if (dir * (end - lim) >= 0) return end;   // 바닥 끝이 이미 뷰 밖
        double l0 = X.Lz(end);
        if (double.IsNaN(l0) || p.Zf >= l0) return end;
        double prev = end;
        for (int k = 1; k < 2000000; k++)
        {
            double a = end + dir * 0.05 * k;
            if (dir * (a - lim) >= 0) { a = lim; }
            double l = X.Lz(a), z = p.Raw(a);
            if (double.IsNaN(l)) return prev;
            if (z >= l)
            {
                double x0 = prev, x1 = a;   // x0 = 땅 아래 · x1 = 땅 위(또는 같음)
                for (int it = 0; it < 40 && Math.Abs(x1 - x0) > 1e-7; it++)
                {
                    double m = (x0 + x1) / 2, lm = X.Lz(m);
                    if (double.IsNaN(lm) || p.Raw(m) >= lm) x1 = m; else x0 = m;
                }
                return x1;
            }
            if (a == lim) return lim;
            prev = a;
        }
        return prev;
    }

    /// <summary>★[v105.3 · 계획 검토 중간 7] 횡단 띠 안(<paramref name="inSwath"/> — 변 가운데)의 앞면 선 변 중 <b>가장 가까운 횡단과 거의 나란한</b>(sinθ &lt; <paramref name="minSin"/>) 것의 길이(m) —
    /// 어느 횡단이 지나가도 빠지는 벽(이 노선 횡단 · 수량에 없음 — 그 벽을 가로지르는 노선으로 따로 셈 · 2D 실무)을 알린다. 측점 사이에 있어 안 잘린 변은 세지 않는다(평균단면법이 잇는다).</summary>
    public static double UncutLength(SecRecord rec, IReadOnlyList<(double Ax, double Ay, double Bx, double By)> cuts, double minSin, Func<double, double, bool> inSwath, out int segs)
    {
        segs = 0;
        double len = 0;
        if (cuts.Count == 0) return 0;
        foreach (var ln in rec.Lines)
            for (int i = 0; i + 1 < ln.Pts.Count; i++)
            {
                var p = ln.Pts[i]; var q = ln.Pts[i + 1];
                double ex = q.X - p.X, ey = q.Y - p.Y, eL = Math.Sqrt(ex * ex + ey * ey);
                double mx = (p.X + q.X) / 2, my = (p.Y + q.Y) / 2;
                if (eL < 1e-9 || !inSwath(mx, my)) continue;
                // 가장 가까운 횡단(가운데에서 그 선분까지 거리)
                double best = double.MaxValue, sinB = 1;
                foreach (var (ax, ay, bx, by) in cuts)
                {
                    double ux = bx - ax, uy = by - ay, uL2 = ux * ux + uy * uy;
                    if (uL2 < 1e-18) continue;
                    double t = Math.Clamp(((mx - ax) * ux + (my - ay) * uy) / uL2, 0, 1);
                    double dx = ax + ux * t - mx, dy = ay + uy * t - my, d2 = dx * dx + dy * dy;
                    if (d2 < best) { best = d2; sinB = Math.Abs(ux * ey - uy * ex) / (Math.Sqrt(uL2) * eL); }
                }
                if (sinB < minSin) { len += eL; segs++; }
            }
        return len;
    }

    static List<double> Clean(List<double> v, double lo, double hi)
    {
        var s = v.Where(x => !double.IsNaN(x) && x >= lo - 1e-12 && x <= hi + 1e-12).Select(x => Math.Min(hi, Math.Max(lo, x))).ToList();
        s.Sort();
        var r = new List<double>(s.Count);
        foreach (double x in s) if (r.Count == 0 || x - r[^1] > 1e-7) r.Add(x);
        return r;
    }

    // ══ 3D 조각 벽 끝 절벽(★[v105.3 · 계획 6]) ══════════════════════════════════════

    /// <summary>★[v105.3 · 계획_v105_3.md 6 · 1023 V47 덤프 — 벽 끝 둥근 자리에서 조각 면이 참 원뿔보다 최대 4.9 m 높아 2 m에 4.4 m 떨어지는 절벽]
    /// 조각 삼각형 꼭짓점 중 바닥 띠 밖(띠 경계에서 1 mm 넘게)인 것의 z를 <b>참 원뿔</b> min_f(z_f + h(|p − f|))로 낮춘다(f = 띠 둘레 0.05 m 표본 · h = 법면 오름).
    /// <para>GradingGeometry.Build는 바닥 높이가 길이 따라 바뀌는 띠를 수직 오프셋해 «가장 가까운 바닥 + h»를 쓰는데, 더 먼 낮은 바닥에서 오르는 1:m 법면이 더 낮을 수 있다 —
    /// 그만큼 덜 판 것(실제로는 1:m보다 가파른 깎기면). 오프셋 거리 = 띠까지 거리라 Build z ≥ 참 원뿔 → 낮추기만 한다. 평면 자리는 그대로(삼각망 꼴 · 이웃 공유 꼭짓점 그대로).</para>
    /// <para>남는 오차 = 삼각형 안 보간(원뿔이 꺾이는 줄과 삼각형 변이 안 맞음 · JACK 사본 95% 0.3 m · 최대 0.8 m · 벽 끝 둥근 자리) — 3D 종단 그림 몫(횡단 · 수량은 2D).</para></summary>
    public static List<Tri> ConeLower(IReadOnlyList<Tri> tris, IReadOnlyList<Point3> ring, SlopeSpec sp, out int lowered, out double maxDrop)
    {
        lowered = 0; maxDrop = 0;
        var res = new List<Tri>(tris.Count);
        if (ring.Count < 3) { res.AddRange(tris); return res; }
        // 둘레 표본(0.05 m · z 선형)
        var fx = new List<double>(); var fy = new List<double>(); var fz = new List<double>();
        for (int i = 0; i < ring.Count; i++)
        {
            var a = ring[i]; var b = ring[(i + 1) % ring.Count];
            double L = Math.Sqrt(Sq(b.X - a.X) + Sq(b.Y - a.Y));
            int m = Math.Max(1, (int)Math.Ceiling(L / 0.05));
            for (int k = 0; k < m; k++) { double t = (double)k / m; fx.Add(a.X + (b.X - a.X) * t); fy.Add(a.Y + (b.Y - a.Y) * t); fz.Add(a.Z + (b.Z - a.Z) * t); }
        }
        int nf = fx.Count;
        double zMin = fz.Min(), g = sp.MinGrade;
        // 표본 격자(2 m) — 가까운 칸부터 넓혀 가며 «남은 칸의 아래 한계 ≥ 지금 최소»면 멈춘다
        const double Cell = 2.0;
        double ox = fx.Min(), oy = fy.Min();
        var grid = new Dictionary<(int, int), List<int>>();
        for (int i = 0; i < nf; i++)
        {
            var key = ((int)Math.Floor((fx[i] - ox) / Cell), (int)Math.Floor((fy[i] - oy) / Cell));
            if (!grid.TryGetValue(key, out var l)) grid[key] = l = new List<int>();
            l.Add(i);
        }
        int gx0 = grid.Keys.Min(k => k.Item1), gx1 = grid.Keys.Max(k => k.Item1), gy0 = grid.Keys.Min(k => k.Item2), gy1 = grid.Keys.Max(k => k.Item2);
        var gf = new GeometryFactory();
        var coords = ring.Select(q => new Coordinate(q.X, q.Y)).ToList(); coords.Add(coords[0]);
        var loc = new IndexedPointInAreaLocator(gf.CreatePolygon(coords.ToArray()));
        var memo = new Dictionary<(double, double), double>();
        double Cone(double x, double y, double zNow)
        {
            if (memo.TryGetValue((x, y), out double v)) return v;
            if (loc.Locate(new Coordinate(x, y)) != Location.Exterior) { memo[(x, y)] = zNow; return zNow; }
            int cx = (int)Math.Floor((x - ox) / Cell), cy = (int)Math.Floor((y - oy) / Cell);
            double best = double.PositiveInfinity, dNear = double.PositiveInfinity, zNear = zNow;
            int rMax = Math.Max(Math.Max(Math.Abs(cx - gx0), Math.Abs(cx - gx1)), Math.Max(Math.Abs(cy - gy0), Math.Abs(cy - gy1)));
            for (int r = 0; r <= rMax; r++)
            {
                // 고리 r 안 점까지 거리 ≥ (r − 1)·Cell → 그 값의 아래 한계 · 가장 가까운 띠 점(하한 · 경계 판정)도 다 찾은 뒤에 멈춘다
                if (r > 1 && zMin + (r - 1) * Cell * g >= best && (r - 1) * Cell >= dNear) break;
                for (int i = cx - r; i <= cx + r; i++)
                    for (int j = cy - r; j <= cy + r; j++)
                    {
                        if (Math.Max(Math.Abs(i - cx), Math.Abs(j - cy)) != r) continue;
                        if (!grid.TryGetValue((i, j), out var l)) continue;
                        foreach (int k in l)
                        {
                            double d = Math.Sqrt(Sq(fx[k] - x) + Sq(fy[k] - y));
                            if (d < dNear) { dNear = d; zNear = fz[k]; }
                            double c = fz[k] + sp.Rise(d);
                            if (c < best) best = c;
                        }
                    }
            }
            // 띠 경계 위(1 mm 안) — 바닥 그대로 · ★[계획 검토 중간 9] 하한 = 가장 가까운 띠 점의 바닥(계단 기초 — 바닥이 1/m보다 가파르게 바뀌는 자리에서 띠 곁이 그 바닥보다 내려가 세로 면이 생기지 않게)
            v = dNear < 1e-3 ? zNow : Math.Min(zNow, Math.Max(best, zNear));
            memo[(x, y)] = v;
            return v;
        }
        double drop = 0;
        Point3 Low(Point3 q)
        {
            double z = Cone(q.X, q.Y, q.Z);
            if (z < q.Z - 1e-9) { drop = Math.Max(drop, q.Z - z); return new Point3(q.X, q.Y, z); }
            return q;
        }
        var seen = new HashSet<(double, double)>();
        foreach (var t in tris)
        {
            var a = Low(t.A); var b = Low(t.B); var c = Low(t.C);
            foreach (var (o, nw) in new[] { (t.A, a), (t.B, b), (t.C, c) }) if (nw.Z < o.Z - 1e-9 && seen.Add((o.X, o.Y))) lowered++;
            res.Add(new Tri(a, b, c));
        }
        maxDrop = drop;
        return res;
    }
}
