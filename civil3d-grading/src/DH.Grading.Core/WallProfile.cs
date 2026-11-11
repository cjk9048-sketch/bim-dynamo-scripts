using System.Globalization;

namespace DH.Grading.Core;

/// <summary>★★[로드맵 `2026-11-11_옹벽추가_자문기반_로드맵.md` 2-2 · 2-3 — <b>보고만</b>] 선 옹벽(수직 · 다단 바깥 벽) 측점 높이표 · 절토/성토/0/못 읽음 분류 ·
/// 검토 높이 넘음(D4 ① — ⚠만) · 형식 단면 기초가 그은 선 밖으로 몇 m(D1 재료 · 재기만). 기하 · 합성 · 기록 · 자가검증 줄을 안 바꾼다(순수 셈).
/// <para>측점 = 짓기가 이미 만든 L 위 점 전부(꺾은점 · 원지반 · 정지면 삼각형 변 건넘 · 가름점 C · 끝 A · B) — 두 면이 측점 사이에서 1차라 H = EG − FG가 측점 사이 직선(간격 설정 없음).
/// H &gt; 0 절토 · &lt; 0 성토(자문 4.2 부호) · |H| ≤ 1 cm = 0(끝 높이 계약값 — 새 여유 아님 · 보고에만 씀).</para></summary>
public static class WallProfile
{
    public enum Cls { Cut, Fill, Zero, NoData }
    /// <summary>0 판정 높이(m) — 이미 있는 끝 높이 계약값(수직 옹벽 VEndMax)과 같은 1 cm.</summary>
    public const double ZeroZ = 0.01;

    /// <summary>측점 — 호 길이 S · 자리 · 원지반 EG · 정지면 FG(벽 없을 때) · H = EG − FG · 벽 높이 Wh(가로대 · 없으면 NaN) · 띠 폭 W(L ~ L′) · 종류(수직 짓기의 L 점 종류).</summary>
    public sealed class Station
    {
        public double S, X, Y, EG, FG, H, Wh = double.NaN, W = double.NaN;
        public Cls Cls; public int Kind; public bool NoData;
    }
    /// <summary>같은 분류가 이어진 몫 · 끝(선 끝에 닿은 0 조각) · 몫 안 최고 벽 높이.</summary>
    public sealed class Run { public Cls Cls; public double S0, S1, WhMax; public bool End; public double Len => S1 - S0; }
    /// <summary>기초 재기(한 쪽) — 형식 · 잰 가로대 수 · 선 밖 최대(구조물 · 보강토체 포함) · 자리 · 그 벽 높이 · 표 줄 H · 표 범위 밖(위 · 아래) 수.</summary>
    public sealed class Foot
    {
        public bool Up; public WallSection.Kind Kind; public int N, OverN, BelowN;
        public double OutMax = double.NegativeInfinity, AllMax = double.NegativeInfinity, HAt, RowH, HAll;
        public (double X, double Y) At, AtAll;
    }
    public sealed class Result
    {
        public List<Station> Stations = new();
        public List<Run> Runs = new();
        /// <summary>길이(m) — 절 · 성 · 안쪽 0 · 끝 0 · 못 읽음 · 안쪽 0 조각 수 · 가장 짧은 절/성 조각.</summary>
        public double LenC, LenF, LenZ, LenZEnd, LenNo, ShortMin = double.NaN;
        public int ZeroN;
        /// <summary>검토 높이(절 · 성) · 넘은 길이 · 넘은 최고 · 자리 · 그 자리 검토 높이.</summary>
        public double ReviewC = double.NaN, ReviewF = double.NaN, OverLen, OverMax, OverHw = double.NaN;
        public (double X, double Y) OverAt;
        public List<Foot> Feet = new();
        /// <summary>로그 줄(측점표 · 기초) · 미리보기 한 줄 · 탐침 한 줄 · 검토 넘음 ⚠(없으면 빈 값).</summary>
        public string Text = "", FootText = "", Preview = "", Probe = "", OverWarn = "";
    }

    static readonly CultureInfo Ci = CultureInfo.InvariantCulture;

    /// <summary>측점(S 오름차순)으로 분류 · 몫 · 검토 높이 넘음을 센다. <paramref name="len"/> = 선 길이(끝 0 조각 가리기).</summary>
    public static Result Analyze(List<Station> st, double len, double reviewC, double reviewF)
    {
        var R = new Result { Stations = st, ReviewC = reviewC, ReviewF = reviewF };
        foreach (var p in st) p.Cls = p.NoData ? Cls.NoData : p.H > ZeroZ ? Cls.Cut : p.H < -ZeroZ ? Cls.Fill : Cls.Zero;
        // 몫 — 측점 사이 H 1차 → ±z 건넘에서 정확히 나눔 · 못 읽은 측점이 낀 칸은 통째로 못 읽음
        void Add(Cls c, double a, double b, double wa, double wb)
        {
            if (b - a <= 0) return;
            double wm = Math.Max(double.IsNaN(wa) ? 0 : wa, double.IsNaN(wb) ? 0 : wb);
            if (R.Runs.Count > 0 && R.Runs[^1].Cls == c && Math.Abs(R.Runs[^1].S1 - a) <= 1e-9) { R.Runs[^1].S1 = b; R.Runs[^1].WhMax = Math.Max(R.Runs[^1].WhMax, wm); return; }
            R.Runs.Add(new Run { Cls = c, S0 = a, S1 = b, WhMax = wm });
        }
        static double Lerp(double a, double b, double t) => a + (b - a) * t;
        for (int i = 0; i + 1 < st.Count; i++)
        {
            var p = st[i]; var q = st[i + 1];
            if (q.S - p.S <= 0) continue;
            if (p.NoData || q.NoData) { Add(Cls.NoData, p.S, q.S, p.Wh, q.Wh); continue; }
            var ts = new List<double> { 0, 1 };
            foreach (double z in new[] { ZeroZ, -ZeroZ })
                if ((p.H - z) * (q.H - z) < 0) ts.Add((z - p.H) / (q.H - p.H));
            ts.Sort();
            for (int k = 0; k + 1 < ts.Count; k++)
            {
                double ta = ts[k], tb = ts[k + 1]; if (tb - ta <= 0) continue;
                double hm = Lerp(p.H, q.H, (ta + tb) / 2);
                var c = hm > ZeroZ ? Cls.Cut : hm < -ZeroZ ? Cls.Fill : Cls.Zero;
                Add(c, Lerp(p.S, q.S, ta), Lerp(p.S, q.S, tb), Lerp(p.Wh, q.Wh, ta), Lerp(p.Wh, q.Wh, tb));
            }
        }
        for (int k = 0; k < R.Runs.Count; k++)
        {
            var r = R.Runs[k];
            r.End = r.Cls == Cls.Zero && (r.S0 <= 1e-9 || r.S1 >= len - 1e-9);
            switch (r.Cls)
            {
                case Cls.Cut: R.LenC += r.Len; break;
                case Cls.Fill: R.LenF += r.Len; break;
                case Cls.NoData: R.LenNo += r.Len; break;
                default: if (r.End) R.LenZEnd += r.Len; else { R.LenZ += r.Len; R.ZeroN++; } break;
            }
            if (r.Cls is Cls.Cut or Cls.Fill && !(r.Len >= R.ShortMin)) R.ShortMin = r.Len;
        }
        // 검토 높이 — 칸마다 벽 높이(가로대 사이 1차)가 그 쪽(절토 H > 0 · 성토 H < 0) 검토 높이를 넘는 길이 · 최고
        for (int i = 0; i + 1 < st.Count; i++)
        {
            var p = st[i]; var q = st[i + 1];
            if (q.S - p.S <= 0 || p.NoData || q.NoData || double.IsNaN(p.Wh) || double.IsNaN(q.Wh)) continue;
            double hw = (p.H + q.H) / 2 >= 0 ? reviewC : reviewF;
            if (!(hw > 0)) continue;
            double a = p.Wh - hw, b = q.Wh - hw, frac = a > 0 && b > 0 ? 1 : a <= 0 && b <= 0 ? 0 : a > 0 ? a / (a - b) : b / (b - a);
            if (frac <= 0) continue;
            R.OverLen += frac * (q.S - p.S);
            foreach (var s in new[] { p, q })
                if (s.Wh > hw && s.Wh > R.OverMax) { R.OverMax = s.Wh; R.OverAt = (s.X, s.Y); R.OverHw = hw; }
        }
        if (R.OverLen > 0)
            R.OverWarn = string.Format(Ci, "검토 높이 넘음 — 벽 최고 {0:F2} m > 검토 {1:F2} m · 넘는 길이 {2:F2} m ({3:F1}, {4:F1}) — 경고만(구조 안전 판정 아님)", R.OverMax, R.OverHw, R.OverLen, R.OverAt.X, R.OverAt.Y);
        return R;
    }

    /// <summary>★[로드맵 2-3 · 재기만] 가로대마다 형식 단면(<see cref="WallSection.Build"/>)을 세워 구조물이 그은 선 L 밖으로 나가는 거리.
    /// 단면 원점 = 앞면 · 앞 땅 · x = 흙 쪽 +. <b>절토</b> 앞 땅 = L′(흙 쪽 = 선 밖) → 밖 거리 = max x − W · <b>성토</b> 앞 땅 = L(앞 = 선 밖) → 밖 거리 = −min x.
    /// «구조물» = 구조물 조각만(콘크리트 · 버림 · 활동방지벽 · 블록 · 기초패드) · «보강토체 포함» = 모든 조각(배수층 · 보강토체 더함).</summary>
    public static void Footing(Result R, WallSection.Table? cut, WallSection.Table? fill, WallSection.Common? common)
    {
        if (common == null || (cut == null && fill == null)) { R.FootText = "  ⑨-b 기초 재기 — 단면 표 없음(안 잼)\n"; return; }
        var feet = new Dictionary<bool, Foot>();
        foreach (var s in R.Stations)
        {
            if (s.NoData || s.Cls is not (Cls.Cut or Cls.Fill) || double.IsNaN(s.Wh)) continue;
            bool up = s.Cls == Cls.Cut;
            var t = up ? cut : fill; if (t == null || t.Rows.Count == 0) continue;
            var sh = WallSection.Build(t, s.Wh, common, up);
            double mx = double.NegativeInfinity, mn = double.PositiveInfinity, mxA = double.NegativeInfinity, mnA = double.PositiveInfinity;
            foreach (var pt in sh.Parts)
                foreach (var (x, _) in pt.Ring)
                {
                    if (pt.Structure) { mx = Math.Max(mx, x); mn = Math.Min(mn, x); }
                    mxA = Math.Max(mxA, x); mnA = Math.Min(mnA, x);
                }
            if (double.IsInfinity(mx)) continue;
            double w = double.IsNaN(s.W) ? 0 : s.W;
            double o = up ? mx - w : -mn, oa = up ? mxA - w : -mnA;
            if (!feet.TryGetValue(up, out var f)) feet[up] = f = new Foot { Up = up, Kind = t.Kind };
            f.N++; if (sh.Over) f.OverN++; if (sh.Below) f.BelowN++;
            if (o > f.OutMax) { f.OutMax = o; f.At = (s.X, s.Y); f.HAt = s.Wh; f.RowH = WallSection.Lookup(t, s.Wh, out _, out _)[0]; }
            if (oa > f.AllMax) { f.AllMax = oa; f.AtAll = (s.X, s.Y); f.HAll = s.Wh; }
        }
        R.Feet = feet.Values.OrderBy(f => f.Up ? 0 : 1).ToList();
        var sb = new System.Text.StringBuilder("  ⑨-b 기초 재기(형식 단면 · 재기만 · v118 3D 몸통과 다를 수 있음) — ");
        if (R.Feet.Count == 0) sb.Append("잴 가로대 없음");
        sb.Append(string.Join(" / ", R.Feet.Select(f => string.Format(Ci, "{0} {1}: 구조물 선 밖 {2:F2} m @({3:F1}, {4:F1})(벽 {5:F2} m · 표 H {6:F2}) · 보강토체 포함 {7:F2} m · 가로대 {8} · 표 위 넘음 {9} · 아래 {10}",
            f.Up ? "절토" : "성토", f.Kind, f.OutMax, f.At.X, f.At.Y, f.HAt, f.RowH, f.AllMax, f.N, f.OverN, f.BelowN))));
        R.FootText = sb.Append('\n').ToString();
    }

    /// <summary>로그 · 미리보기 · 탐침 글을 채운다(<see cref="Analyze"/> · <see cref="Footing"/> 뒤).</summary>
    public static void Texts(Result R)
    {
        string Hw(double v) => double.IsNaN(v) ? "—" : v.ToString("0.##", Ci);
        string F2(double v) => double.IsFinite(v) ? v.ToString("F2", Ci) : "—";
        R.Text = string.Format(Ci, "  ⑨ 측점표 — 측점 {0} · 절 {1:F2} · 성 {2:F2} · 0 {3:F2} m({4}곳) · 끝 0 {5:F2} m · 못 읽음 {6:F2} m · 가장 짧은 절/성 조각 {7} m · 검토 높이 절 {8} · 성 {9} m 넘음 {10:F2} m{11}\n",
            R.Stations.Count, R.LenC, R.LenF, R.LenZ, R.ZeroN, R.LenZEnd, R.LenNo, F2(R.ShortMin), Hw(R.ReviewC), Hw(R.ReviewF), R.OverLen,
            R.OverLen > 0 ? string.Format(Ci, "(최고 {0:F2} m @({1:F1}, {2:F1}))", R.OverMax, R.OverAt.X, R.OverAt.Y) : "");
        string foot = string.Join(" · ", R.Feet.Select(f => string.Format(Ci, "{0} {1:F2}(보강토체 {2:F2})", f.Up ? "절" : "성", f.OutMax, f.AllMax)));
        var pv = new List<string> { string.Format(Ci, "측점 {0}", R.Stations.Count) };
        if (R.ZeroN > 0) pv.Add(string.Format(Ci, "높이 0 구간 {0}곳 {1:F1} m", R.ZeroN, R.LenZ));
        if (R.LenNo > 0) pv.Add(string.Format(Ci, "못 읽음 {0:F1} m", R.LenNo));
        if (R.OverLen > 0) pv.Add(string.Format(Ci, "검토 높이 {0} m 넘음 {1:F1} m(최고 {2:F2} m)", Hw(R.OverHw), R.OverLen, R.OverMax));
        if (foot.Length > 0) pv.Add("기초 선 밖 " + foot + " m");
        R.Preview = "〈" + string.Join(" · ", pv) + "〉";
        R.Probe = string.Format(Ci, "[옹벽 추가 측점] 측점 {0} · 절 {1:F2} · 성 {2:F2} · 0 {3:F2} m({4}곳) · 끝 0 {5:F2} · 못 읽음 {6:F2} · 검토 {7}/{8} 넘음 {9:F2} m · 기초 밖 {10}",
            R.Stations.Count, R.LenC, R.LenF, R.LenZ, R.ZeroN, R.LenZEnd, R.LenNo, Hw(R.ReviewC), Hw(R.ReviewF), R.OverLen, foot.Length > 0 ? foot + " m" : "—");
    }

    /// <summary>★[M0Dump] 기록(목록)의 바깥 벽 면(Tier 1 · 앞면)에서 측점표를 다시 센다 — 기록에는 측점표가 없으므로(판 9 · 10 바이트 그대로)
    /// 가로대 두 끝을 씀: EG = L 쪽 끝 높이 · FG ≈ L′ 쪽 끝 높이(짓기 때 FG는 L 위 정지면 — 띠 폭만큼 다름) → «가로대 기준»으로 적는다.</summary>
    public static Result FromFaces(IEnumerable<WallByLine.FaceSeg> faces)
    {
        var st = new List<Station>();
        double s = 0; (double X, double Y)? prev = null;
        foreach (var f in faces.Where(f => f.Kind == 0 && f.Tier <= 1).OrderBy(f => f.Part))
            for (int i = 0; i < f.Top.Count && i < f.Foot.Count; i++)
            {
                var l = f.Up ? f.Top[i] : f.Foot[i]; var o = f.Up ? f.Foot[i] : f.Top[i];
                if (prev is { } pv)
                {
                    double d = Math.Sqrt((l.X - pv.X) * (l.X - pv.X) + (l.Y - pv.Y) * (l.Y - pv.Y));
                    if (d <= 1e-9) continue;   // 가름점 가로대(두 부분이 같이 가짐)
                    s += d;
                }
                prev = (l.X, l.Y);
                st.Add(new Station { S = s, X = l.X, Y = l.Y, EG = l.Z, FG = o.Z, H = l.Z - o.Z, Wh = Math.Abs(f.Top[i].Z - f.Foot[i].Z) });
            }
        var r = Analyze(st, s, double.NaN, double.NaN);
        Texts(r);
        return r;
    }
}
