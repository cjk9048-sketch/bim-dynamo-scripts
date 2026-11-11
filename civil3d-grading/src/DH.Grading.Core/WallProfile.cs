using System.Globalization;
using NetTopologySuite.Geometries;

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
        /// <summary>★[로드맵 v117.3 · D1 ③] 형식 단면이 L에서 바깥(R_out 쪽 +)으로 — 바깥 끝 · 안쪽 끝(구조물) · 보강토체 포함(A). <see cref="Footing"/>이 채움 · NaN = 단면 없음.</summary>
        public double Out = double.NaN, In = double.NaN, OutA = double.NaN, InA = double.NaN;
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

        // ══ ★[로드맵 v117.3 · JACK D1 ③ — 경고만] 구조물 외곽(기초 포함) · 용지 경계(없으면 그은 선) — <c>LandCheck</c> ══
        /// <summary>구조물 외곽(닫힌 링 · 세계 · Z 0 · 끝 점 안 겹침) — 단면이 이어진 측점 몫마다 하나.</summary>
        public List<List<Point3>> Outline = new();
        /// <summary>쓴 용지 경계(미리보기) · 경계 밖 몫마다 가장 먼 자리(빨간 X).</summary>
        public List<IReadOnlyList<Point3>> LandRings = new();
        public List<(double X, double Y)> LandMarks = new();
        /// <summary>용지 경계 없음 → 그은 선 = 경계(밖 거리 = L에서 바깥으로 수직 거리 — ⑨-b와 같은 셈).</summary>
        public bool LandByLine;
        /// <summary>경계 밖 최대(m · 구조물 · 보강토체 포함) — 밖이 없으면 0 이하(가장 적은 여유의 음수) · NaN = 안 잼.</summary>
        public double LandMax = double.NaN, LandMaxAll = double.NaN;
        public (double X, double Y) LandAt;
        /// <summary>구조물이 경계 밖인 측점 구간(S · 선 시작 A부터 m).</summary>
        public List<(double S0, double S1)> LandRuns = new();
        public string LandText = "", LandWarn = "", LandPreview = "", LandProbe = "";
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
            s.Out = o; s.OutA = oa; s.In = up ? mn - w : -mx; s.InA = up ? mnA - w : -mxA;   // ★[v117.3 D1 ③] 측점마다 남김(외곽선 · 경계 검사)
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

    /// <summary>★[로드맵 v117.3 · JACK D1 ③ «그은 선 = 벽 앞면 + 용지 경계 선을 따로 고름 — 안 고르면 그은 선을 경계로 봄 · 기초 외곽이 경계 밖이면 경고»] <b>경고만</b>(기하 · 기록 · 자가검증 줄 그대로).
    /// <see cref="Footing"/>이 측점마다 남긴 끝(Out · In · 보강토체 포함 OutA · InA)으로 L에서 바깥 법선 방향 외곽을 세운다 — 꺾은점은 앞 · 뒤 토막 평행선의 만남(1 + n₁·n₂ &lt; 0.5 · 120° 넘게 꺾이면 두 점).
    /// 용지 경계(닫힌 링들의 합)가 있으면 외곽 변을 0.5 m 간격으로 재어 경계 밖 거리(밖 +) · 밖인 측점 구간(밖 거리 0 자리를 이웃 잰 점 사이 1차로) ·
    /// 없으면 그은 선 L = 경계(밖 거리 = L에서 수직 거리 Out — ⑨-b와 같은 셈).</summary>
    /// <param name="cutRing">잘라낼 자리 R_out — 선의 바깥 쪽을 가린다(짓기와 같은 판정).</param>
    /// <param name="land">용지 경계 링(세계 · 닫힘 무관) — null · 빈 것 · 못 읽은 링뿐이면 그은 선.</param>
    public static void LandCheck(Result R, IReadOnlyList<Point3> cutRing, IReadOnlyList<IReadOnlyList<Point3>>? land)
    {
        const double Eps = 1e-3;   // 밖 판정(m) — 셈 잡음 바닥 1 mm(보고에만 씀 · 기하 여유 아님)
        const double Step = 0.5;   // 외곽 변 재는 간격(m) — 오목한 경계에서는 변 가운데가 더 멀 수 있다
        R.Outline.Clear(); R.LandRings.Clear(); R.LandMarks.Clear(); R.LandRuns.Clear();
        R.LandMax = R.LandMaxAll = double.NaN; R.LandAt = default; R.LandText = R.LandWarn = R.LandPreview = R.LandProbe = "";
        var gf = new GeometryFactory();
        Geometry? landG = null; int landN = 0, landBad = 0;
        if (land != null)
            foreach (var r in land)
            {
                var pg = r == null ? null : NtsSupport.ToCleanGeometry(r, gf);
                if (pg == null || pg.IsEmpty || pg.Area <= 1e-6) { landBad++; continue; }
                landG = landG == null ? pg : landG.Union(pg);
                landN++; R.LandRings.Add(r!);
            }
        R.LandByLine = landG == null;
        string basis = R.LandByLine ? "그은 선(용지 경계 없음)" : string.Format(Ci, "용지 경계 {0}개", landN);
        if (landBad > 0) basis += string.Format(Ci, "(못 읽은 링 {0} 뺌)", landBad);
        var st = R.Stations; int m = st.Count;
        string Skip(string why) { R.LandText = "  ⑨-c 기초 외곽 — " + why + "(안 잼) · 기준 " + basis + "\n"; R.LandProbe = "[옹벽 추가 외곽] 안 잼 — " + why; return why; }
        if (m < 2) { Skip("측점 모자람"); return; }
        // 바깥 쪽(R_out 쪽) — 가장 긴 측점 토막 가운데에서 왼쪽 · 오른쪽 0.1 mm를 R_out에 묻는다(BuildVertical과 같은 판정)
        var cutP = cutRing == null ? null : NtsSupport.ToCleanGeometry(cutRing, gf);
        int best = -1; double bl = 0;
        for (int i = 0; i + 1 < m; i++) { double l = Math.Sqrt((st[i + 1].X - st[i].X) * (st[i + 1].X - st[i].X) + (st[i + 1].Y - st[i].Y) * (st[i + 1].Y - st[i].Y)); if (l > bl) { bl = l; best = i; } }
        if (cutP == null || best < 0 || bl <= 1e-9) { Skip("잘라낼 자리 · 선을 못 읽음"); return; }
        double side;
        {
            var a = st[best]; var b = st[best + 1];
            double mx = (a.X + b.X) / 2, my = (a.Y + b.Y) / 2, tx = (b.X - a.X) / bl, ty = (b.Y - a.Y) / bl, e = Math.Min(1e-4, bl * 0.01);
            bool left = cutP.Contains(gf.CreatePoint(new Coordinate(mx - ty * e, my + tx * e)));
            bool right = cutP.Contains(gf.CreatePoint(new Coordinate(mx + ty * e, my - tx * e)));
            if (left == right) { Skip("선의 바깥 쪽을 못 가림"); return; }
            side = left ? 1 : -1;
        }
        // 토막마다 바깥 단위 법선(길이 0 토막은 가까운 이웃 것)
        var nx = new double[m - 1]; var ny = new double[m - 1]; var okN = new bool[m - 1];
        for (int k = 0; k + 1 < m; k++)
        {
            double dx = st[k + 1].X - st[k].X, dy = st[k + 1].Y - st[k].Y, l = Math.Sqrt(dx * dx + dy * dy);
            if (l <= 1e-9) continue;
            nx[k] = -dy / l * side; ny[k] = dx / l * side; okN[k] = true;
        }
        for (int k = 0; k + 1 < m; k++)
            if (!okN[k])
                for (int d = 1; d < m; d++)
                {
                    if (k - d >= 0 && okN[k - d]) { nx[k] = nx[k - d]; ny[k] = ny[k - d]; break; }
                    if (k + d < m - 1 && okN[k + d]) { nx[k] = nx[k + d]; ny[k] = ny[k + d]; break; }
                }
        void EdgePt(int i, double off, List<(double X, double Y, double S, double Off)> to)
        {
            int kp = i > 0 ? i - 1 : i, kn = i + 1 < m ? i : i - 1;
            double ax = nx[kp], ay = ny[kp], bx = nx[kn], by = ny[kn], d = 1 + ax * bx + ay * by;
            var s = st[i];
            if (d >= 0.5) to.Add((s.X + off * (ax + bx) / d, s.Y + off * (ay + by) / d, s.S, off));
            else { to.Add((s.X + off * ax, s.Y + off * ay, s.S, off)); to.Add((s.X + off * bx, s.Y + off * by, s.S, off)); }
        }
        var prep = landG == null ? null : NetTopologySuite.Geometries.Prepared.PreparedGeometryFactory.Prepare(landG);
        var bnd = landG?.Boundary;
        // 밖 거리(밖 +) — 경계가 있으면 그 합까지 평면 거리(안이면 경계선까지 −) · 없으면 L에서 수직 거리 그대로
        double Sd(double x, double y, double off)
        {
            if (landG == null) return off;
            var p = gf.CreatePoint(new Coordinate(x, y));
            return prep!.Covers(p) ? -bnd!.Distance(p) : landG.Distance(p);
        }
        var runs = new List<(double S0, double S1)>();
        // 한 변(점열)을 Step 간격으로 잰다 — 구조물 변은 최대 · 밖 몫 · 몫마다 가장 먼 자리 · 보강토체 변(all)은 최대만
        void Walk(List<(double X, double Y, double S, double Off)> e, bool all)
        {
            double pSd = double.NaN, pS = 0, s0 = 0, rMax = double.NegativeInfinity; (double X, double Y) rAt = default;
            void Sample(double x, double y, double s, double off)
            {
                double sd = Sd(x, y, off);
                if (all) { if (double.IsNaN(R.LandMaxAll) || sd > R.LandMaxAll) R.LandMaxAll = sd; return; }
                if (double.IsNaN(R.LandMax) || sd > R.LandMax) { R.LandMax = sd; R.LandAt = (x, y); }
                bool o = sd > Eps, po = pSd > Eps;
                // 밖 거리 0 자리 = 이웃 잰 점 사이 1차(곧은 경계 · 곧은 외곽이면 정확)
                double Zero() => sd == pSd ? s : Math.Clamp(pS + (s - pS) * (0 - pSd) / (sd - pSd), Math.Min(pS, s), Math.Max(pS, s));
                if (o && !po) { s0 = double.IsNaN(pSd) ? s : Zero(); rMax = sd; rAt = (x, y); }
                else if (o) { if (sd > rMax) { rMax = sd; rAt = (x, y); } }
                else if (po) { runs.Add((s0, Zero())); R.LandMarks.Add(rAt); }
                pSd = sd; pS = s;
            }
            for (int j = 0; j < e.Count; j++)
            {
                if (j == 0) { Sample(e[0].X, e[0].Y, e[0].S, e[0].Off); continue; }
                var p = e[j - 1]; var q = e[j];
                int n = Math.Max(1, (int)Math.Ceiling(Math.Sqrt((q.X - p.X) * (q.X - p.X) + (q.Y - p.Y) * (q.Y - p.Y)) / Step));
                for (int u = 1; u <= n; u++)
                {
                    double t = (double)u / n;
                    Sample(p.X + (q.X - p.X) * t, p.Y + (q.Y - p.Y) * t, p.S + (q.S - p.S) * t, p.Off + (q.Off - p.Off) * t);
                }
            }
            if (!all && pSd > Eps) { runs.Add((s0, pS)); R.LandMarks.Add(rAt); }
        }
        // 단면이 이어진 측점 몫마다 — 외곽 링(바깥 변 + 안쪽 변 거꾸로) · 네 변 재기
        for (int a = 0; a < m;)
        {
            if (double.IsNaN(st[a].Out) || double.IsNaN(st[a].In)) { a++; continue; }
            int b = a; while (b + 1 < m && !double.IsNaN(st[b + 1].Out) && !double.IsNaN(st[b + 1].In)) b++;
            var o = new List<(double X, double Y, double S, double Off)>(); var n = new List<(double X, double Y, double S, double Off)>();
            var oA = new List<(double X, double Y, double S, double Off)>(); var nA = new List<(double X, double Y, double S, double Off)>();
            // 몫 끝이 높이 0 측점과 맞닿으면 벽이 시작하는 자리(|H| = 1 cm 건넘 — Analyze 몫 경계)까지 옆 측점 단면 그대로 늘인다(표 맨 아래 줄 — ⑨-b와 같은 단면)
            void Ext(int z, int i, bool atEnd)
            {
                var p = st[z]; var q = st[i];
                if (p.NoData || p.Cls != Cls.Zero || q.H == p.H) return;
                double t = (Math.Sign(q.H) * ZeroZ - p.H) / (q.H - p.H);
                if (!(t > 0 && t < 1)) return;
                int k = Math.Min(z, i);
                double x = p.X + (q.X - p.X) * t, y = p.Y + (q.Y - p.Y) * t, s = p.S + (q.S - p.S) * t;
                foreach (var (lst, off) in new[] { (o, q.Out), (n, q.In), (oA, double.IsNaN(q.OutA) ? q.Out : q.OutA), (nA, double.IsNaN(q.InA) ? q.In : q.InA) })
                {
                    var v = (x + off * nx[k], y + off * ny[k], s, off);
                    if (atEnd) lst.Add(v); else lst.Insert(0, v);
                }
            }
            for (int i = a; i <= b; i++) { EdgePt(i, st[i].Out, o); EdgePt(i, st[i].In, n); EdgePt(i, double.IsNaN(st[i].OutA) ? st[i].Out : st[i].OutA, oA); EdgePt(i, double.IsNaN(st[i].InA) ? st[i].In : st[i].InA, nA); }
            if (a > 0) Ext(a - 1, a, false);
            if (b + 1 < m) Ext(b + 1, b, true);
            if (o.Count >= 2) R.Outline.Add(o.Select(q => new Point3(q.X, q.Y, 0)).Concat(Enumerable.Reverse(n).Select(q => new Point3(q.X, q.Y, 0))).ToList());
            Walk(o, false); Walk(n, false); Walk(oA, true); Walk(nA, true);
            a = b + 1;
        }
        if (double.IsNaN(R.LandMax)) { Skip("단면을 세운 측점 없음"); return; }
        // 밖 몫 합치기(바깥 · 안쪽 변 · 몫끼리 겹치면 하나로)
        foreach (var r in runs.OrderBy(r => Math.Min(r.S0, r.S1)))
        {
            double r0 = Math.Min(r.S0, r.S1), r1 = Math.Max(r.S0, r.S1);
            if (R.LandRuns.Count > 0 && r0 <= R.LandRuns[^1].S1 + 1e-6) R.LandRuns[^1] = (R.LandRuns[^1].S0, Math.Max(R.LandRuns[^1].S1, r1));
            else R.LandRuns.Add((r0, r1));
        }
        string F2(double v) => double.IsFinite(v) ? v.ToString("F2", Ci) : "—";
        string sta = R.LandRuns.Count == 0 ? "—" : string.Join(", ", R.LandRuns.Take(3).Select(r => string.Format(Ci, "{0:F1}~{1:F1}", r.S0, r.S1))) + (R.LandRuns.Count > 3 ? string.Format(Ci, " 외 {0}곳", R.LandRuns.Count - 3) : "");
        bool outS = R.LandMax > Eps, outA = R.LandMaxAll > Eps;
        string where = R.LandByLine ? "그은 선(용지 경계 없음)" : "용지 경계";
        R.LandText = string.Format(Ci, "  ⑨-c 기초 외곽(형식 단면 · 경고만) — 기준 {0} · 구조물 {1} · 보강토체 포함 {2} · 외곽 {3}개\n", basis,
            outS ? string.Format(Ci, "밖 최대 {0:F2} m @({1:F1}, {2:F1}) · STA {3} m(선 시작 A부터)", R.LandMax, R.LandAt.X, R.LandAt.Y, sta) : string.Format(Ci, "밖 없음(가장 적은 여유 {0:F2} m)", -R.LandMax),
            outA ? "밖 최대 " + F2(R.LandMaxAll) + " m" : "밖 없음", R.Outline.Count);
        if (outS)
        {
            R.LandWarn = string.Format(Ci, "기초 외곽이 {0} 밖 — 최대 {1:F2} m · STA {2} m{3} — 경고만", where, R.LandMax, sta, outA && R.LandMaxAll > R.LandMax + Eps ? $"(보강토체 포함 {F2(R.LandMaxAll)} m)" : "");
            R.LandPreview = string.Format(Ci, "기초 외곽 {0} 밖 최대 {1:F2} m(STA {2})", R.LandByLine ? "그은 선" : "용지 경계", R.LandMax, sta);
        }
        else if (outA)
        {
            R.LandWarn = string.Format(Ci, "보강토체가 {0} 밖 — 최대 {1} m(구조물은 안 · 여유 {2:F2} m) — 경고만", where, F2(R.LandMaxAll), -R.LandMax);
            R.LandPreview = string.Format(Ci, "보강토체 {0} 밖 최대 {1} m", R.LandByLine ? "그은 선" : "용지 경계", F2(R.LandMaxAll));
        }
        else if (!R.LandByLine) R.LandPreview = string.Format(Ci, "기초 외곽 용지 경계 안(여유 {0:F2} m)", -R.LandMax);
        R.LandProbe = string.Format(Ci, "[옹벽 추가 외곽] 기준 {0} · 구조물 밖 최대 {1:F2} m · STA {2} · 보강토체 포함 {3} m · 외곽 {4}",
            R.LandByLine ? "그은 선" : "용지 경계 " + landN.ToString(Ci), R.LandMax, sta, F2(R.LandMaxAll), R.Outline.Count);
    }

    /// <summary>★[v117.3 D1 ③] 지은 선 옹벽(수직 · 다단)에 <c>LandCheck</c> — 로그 ⑨-c 줄 · ⚠(경고만) · 미리보기 다시 씀.
    /// 못 세면 로그 한 줄만(짓기 그대로). <paramref name="note"/> = 경계 읽기 글(원 폴리선 지워짐 등 · ⑨-c 줄 끝).</summary>
    public static void LandCheck(WallByLine.Built b, IReadOnlyList<Point3> cutRing, IReadOnlyList<IReadOnlyList<Point3>>? land, string note = "")
    {
        if (b == null || !b.Ok || b.Profile == null) return;
        var R = b.Profile;
        try { LandCheck(R, cutRing, land); Texts(R); }
        catch (Exception ex)
        {
            R.Outline.Clear(); R.LandMarks.Clear(); R.LandWarn = R.LandPreview = R.LandProbe = "";
            R.LandText = $"  ⑨-c 기초 외곽 못 셈 — {ex.GetType().Name}: {ex.Message}\n";
        }
        if (note.Length > 0) R.LandText = R.LandText.TrimEnd('\n') + " · " + note + "\n";
        b.Log += R.LandText;
        if (R.LandWarn.Length > 0) b.Warn.Add(R.LandWarn);
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
        if (R.LandPreview.Length > 0) pv.Add(R.LandPreview);   // ★[v117.3 D1 ③] LandCheck 뒤에만(그 전에는 빈 값 — 옛 글자 그대로)
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
