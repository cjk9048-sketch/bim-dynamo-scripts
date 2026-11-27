using DH.Pipe.Core.Geom;
using DH.Pipe.Core.Model;

namespace DH.Pipe.Core.Profile;

/// <summary>종단 검사 결과 한 줄.</summary>
public sealed class ProfileIssue
{
    /// <summary>토피 · 이격 · 경사 · 알림</summary>
    public string Kind { get; set; } = "";
    public double StaFrom { get; set; }
    public double StaTo { get; set; }
    public string Text { get; set; } = "";
    public bool Bad { get; set; } = true;
}

/// <summary>지장물 하나의 이격 검사.</summary>
public sealed class ObstacleCheck
{
    public Obstacle Obstacle { get; set; } = new();
    public double Top { get; set; }
    public double Bottom { get; set; }
    /// <summary>관이 지장물 위로 지나는지.</summary>
    public bool PipeAbove { get; set; }
    /// <summary>바깥면 사이 거리(m). 음수 = 겹침.</summary>
    public double Gap { get; set; }
    public double Need { get; set; }
    public bool Ok => Gap >= Need - 1e-6;
}

/// <summary>종단 검사: 토피 · 지장물 이격 · 급경사.</summary>
public static class ProfileCheck
{
    public static List<ObstacleCheck> Obstacles(ProfileLine line, Pl ground, RouteData route)
    {
        var res = new List<ObstacleCheck>();
        if (line.IsEmpty) return res;
        foreach (var o in route.Obstacles.OrderBy(o => o.Sta))
        {
            if (o.Sta < line.S0 - 1e-6 || o.Sta > line.S1 + 1e-6) continue;
            var pipe = route.PipeAt(o.Sta);
            double g = ground.At(o.Sta);
            double top = o.TopElev ?? g - o.Depth, bot = top - o.Height;
            // 지장물 폭의 양 끝과 가운데에서 재어 가장 좁은 곳을 이격으로(관이 기울어 지나면 끝에서 더 가깝다 — 검토 보통)
            double gap = double.MaxValue;
            bool above = true;
            foreach (double s in new[] { o.Sta - o.Width * 0.5, o.Sta, o.Sta + o.Width * 0.5 })
            {
                double sc = Math.Clamp(s, line.S0, line.S1);
                double inv = line.At(sc);
                double pTop = PipeGeom.OuterTop(pipe, inv), pBot = PipeGeom.OuterBottom(pipe, inv);
                var m = route.MethodAt(sc);
                if (m.Kind == MethodKind.Jacking)
                {
                    double cas = Math.Max(pipe.Od, m.CasingOd is > 0 ? m.CasingOd.Value : pipe.Od + route.Std.CasingExtra);
                    double cz = pBot + pipe.Od * 0.5;
                    pTop = cz + cas * 0.5; pBot = cz - cas * 0.5;
                }
                bool ab = 0.5 * (pTop + pBot) >= 0.5 * (top + bot);
                double gp = ab ? pBot - top : bot - pTop;
                if (gp < gap) { gap = gp; above = ab; }
            }
            res.Add(new ObstacleCheck { Obstacle = o, Top = top, Bottom = bot, PipeAbove = above, Gap = gap, Need = route.Std.ClearanceToOthers });
        }
        return res;
    }

    public static List<ProfileIssue> Run(ProfileLine line, Pl ground, RouteData route)
    {
        var res = new List<ProfileIssue>();
        if (line.IsEmpty) return res;
        // 토피 부족 구간: 지반 꺾인 점 · PVI 에서 재고, 이웃한 부족 점을 한 구간으로 묶는다
        var xs = new SortedSet<double> { Math.Max(line.S0, ground.X0), Math.Min(line.S1, ground.X1) };
        foreach (var p in ground.Points()) if (p.X > line.S0 && p.X < line.S1) xs.Add(p.X);
        foreach (var p in line.Pvi) if (p.S >= ground.X0 && p.S <= ground.X1) xs.Add(p.S);
        double from = double.NaN, worst = double.MaxValue, need = 0, prev = double.NaN;
        void Flush()
        {
            if (double.IsNaN(from)) return;
            res.Add(new ProfileIssue { Kind = "토피", StaFrom = from, StaTo = prev, Text = $"토피 부족 {worst:0.00} < {need:0.00}" });
            from = double.NaN; worst = double.MaxValue;
        }
        foreach (double s in xs)
        {
            var m = route.MethodAt(s);
            var pipe = route.PipeAt(s);
            double c = PipeGeom.Cover(pipe, line.At(s), ground.At(s));
            double min = route.Std.MinCover(pipe);
            bool bad = m.Kind != MethodKind.Jacking && c < min - 0.005;
            if (bad)
            {
                if (double.IsNaN(from)) from = s;
                if (c < worst) { worst = c; need = min; }
            }
            else Flush();
            prev = s;
        }
        Flush();

        foreach (var oc in Obstacles(line, ground, route))
        {
            if (oc.Ok) continue;
            string how = oc.Gap < 0 ? "겹침" : $"이격 {oc.Gap:0.00} < {oc.Need:0.00}";
            res.Add(new ProfileIssue { Kind = "이격", StaFrom = oc.Obstacle.Sta, StaTo = oc.Obstacle.Sta, Text = $"{oc.Obstacle.Kind} {how}" });
        }

        // 역사이펀(하천 횡단) 앞뒤 경사 45° 이하 — KDS 57 50 00 4.1.20 (2)
        for (int i = 0; i + 1 < line.Pvi.Count; i++)
        {
            double g = Math.Abs(line.GradePct(i));
            double sa = line.Pvi[i].S, sb = line.Pvi[i + 1].S;
            // 추진 수직구 안의 수직 관은 급경사가 아니라 설계 그대로
            bool riser = route.Methods.Any(m => m.Kind == MethodKind.Jacking &&
                ((Math.Abs(sb - m.StaFrom) < 0.2 && sb - sa < 0.2) || (Math.Abs(sa - m.StaTo) < 0.2 && sb - sa < 0.2)));
            if (riser) continue;
            if (g > 100.0 + 1e-6)
                res.Add(new ProfileIssue { Kind = "경사", StaFrom = line.Pvi[i].S, StaTo = line.Pvi[i + 1].S, Text = $"경사 {g:0}% > 45°" });
        }
        return res;
    }
}

/// <summary>
/// «최적 종단 가이드안» — 공기밸브 · 이토밸브 · 제수밸브 · 소화전 자리 제안.
/// 근거: KDS 57 50 00 4.1.8(제수밸브) · 4.1.9(공기밸브) · 4.1.10(배수설비) · KDS 57 65 00 4.4.4(소화전) — 원문 확인(설계기준표).
/// </summary>
public static class Guide
{
    private static readonly int[] DrainSizes = { 80, 100, 150, 200, 250, 300, 350, 400, 450, 500, 600 };

    public static List<Appurtenance> Make(ProfileLine line, RouteData route)
    {
        var res = new List<Appurtenance>();
        if (line.IsEmpty) return res;
        var std = route.Std;
        double s0 = line.S0, s1 = line.S1, len = s1 - s0;

        // ---- 볼록 꼭대기 · 오목 바닥: 경사 부호가 바뀌는 곳(평평한 토막은 건너뛰고 본다)
        double tol = Math.Max(0.01, std.CrestMinGradeChange * 0.1);    // % — 이보다 작으면 평평
        int n = line.Pvi.Count - 1;
        var sign = new int[n];
        for (int i = 0; i < n; i++)
        {
            double g = line.GradePct(i);
            sign[i] = g > tol ? 1 : g < -tol ? -1 : 0;
        }
        int lastSign = 0, lastIdx = -1;
        int crests = 0;
        for (int i = 0; i < n; i++)
        {
            if (sign[i] == 0) continue;
            if (lastSign != 0 && sign[i] != lastSign)
            {
                double change = Math.Abs(line.GradePct(i) - line.GradePct(lastIdx));
                if (change >= std.CrestMinGradeChange)
                {
                    // 꼭대기 · 바닥 = 앞 토막의 끝 ~ 뒤 토막의 시작(사이에 평평한 토막이 있으면 그 가운데)
                    double sa = line.Pvi[lastIdx + 1].S, sb = line.Pvi[i].S;
                    double s = 0.5 * (sa + sb);
                    var pipe = route.PipeAt(s);
                    if (lastSign > 0)
                    {
                        crests++;
                        res.Add(new Appurtenance
                        {
                            Sta = s, Kind = ApptKind.AirValve, Reason = "종단 볼록 꼭대기",
                            Spec = pipe.Dn >= std.AirValveRapidDn ? "급속공기밸브" : "급속 또는 소형 공기밸브",
                        });
                    }
                    else
                    {
                        bool river = route.Methods.Any(m => m.Kind == MethodKind.RiverOpen && s >= m.StaFrom - 30 && s <= m.StaTo + 30);
                        res.Add(new Appurtenance
                        {
                            Sta = s, Kind = ApptKind.Drain, Spec = $"이토관 D{DrainSize(pipe.Dn, std.DrainRatio)}",
                            Reason = river ? "종단 오목 바닥 · 하천 가까움" : "종단 오목 바닥(방류할 수로 확인)",
                        });
                    }
                }
            }
            lastSign = sign[i];
            lastIdx = i;
        }

        // ---- 제수밸브: 시점 · 종점 · 하천 횡단(역사이펀) · 추진(철도 · 간선도로 횡단) 앞뒤 · 긴 구간 간격
        var gates = new List<(double S, string Why)> { (s0, "시점"), (s1, "종점") };
        foreach (var m in route.Methods)
        {
            if (!m.IsLevel) continue;
            string nm = m.Kind == MethodKind.RiverOpen ? "하천 횡단" : "추진 횡단";
            double a = Math.Max(s0, m.StaFrom - 5), b = Math.Min(s1, m.StaTo + 5);
            if (a > s0 + 1) gates.Add((a, nm + " 앞"));
            if (b < s1 - 1) gates.Add((b, nm + " 뒤"));
        }
        gates = gates.OrderBy(g => g.S).ToList();
        if (std.GateValveSpacing > 0)
        {
            var extra = new List<(double, string)>();
            for (int i = 0; i + 1 < gates.Count; i++)
            {
                double gap = gates[i + 1].S - gates[i].S;
                int k = (int)Math.Ceiling(gap / std.GateValveSpacing) - 1;
                for (int j = 1; j <= k; j++) extra.Add((gates[i].S + gap * j / (k + 1), $"간격 {std.GateValveSpacing / 1000:0.#} km 이내"));
            }
            gates.AddRange(extra);
        }
        foreach (var g in gates.OrderBy(g => g.S))
            res.Add(new Appurtenance { Sta = g.S, Kind = ApptKind.GateValve, Reason = g.Why, Spec = $"제수밸브 D{route.PipeAt(g.S).Dn}" });

        // ---- 볼록이 하나도 없으면: 높은 쪽 제수밸브 바로 앞(KDS 4.1.9 (1))
        if (crests == 0 && len > 1)
        {
            bool endHigh = line.At(s1) >= line.At(s0);
            double s = endHigh ? Math.Max(s0, s1 - 2) : Math.Min(s1, s0 + 2);
            var pipe = route.PipeAt(s);
            res.Add(new Appurtenance
            {
                Sta = s, Kind = ApptKind.AirValve, Reason = "볼록 없음 → 높은 쪽 제수밸브 앞",
                Spec = pipe.Dn >= std.AirValveRapidDn ? "급속공기밸브" : "급속 또는 소형 공기밸브",
            });
        }

        // ---- 소화전: 배수관일 때만 · 관경 150 이상
        if (std.Use == PipeUse.Distribution && std.HydrantSpacing > 0)
        {
            for (double s = s0 + std.HydrantSpacing * 0.5; s < s1; s += std.HydrantSpacing)
            {
                if (route.MethodAt(s).IsLevel) continue;
                var pipe = route.PipeAt(s);
                if (pipe.Dn < 150) continue;
                res.Add(new Appurtenance
                {
                    Sta = s, Kind = ApptKind.Hydrant, Reason = $"간격 {std.HydrantSpacing:0} m",
                    Spec = pipe.Dn >= 300 ? "쌍구 소화전" : "단구 소화전",
                });
            }
        }
        return res.OrderBy(a => a.Sta).ThenBy(a => a.Kind).ToList();
    }

    /// <summary>이토관 관경: 본관 × 비(1/2 ~ 1/4)에 가장 가까운 규격(작지 않은 쪽).</summary>
    public static int DrainSize(int dn, double ratio)
    {
        double want = dn * Math.Clamp(ratio, 0.25, 0.5);
        foreach (int d in DrainSizes) if (d >= want - 1e-9) return d;
        return DrainSizes[^1];
    }
}
