using DH.Pipe.Core.Geom;
using DH.Pipe.Core.Model;
using DH.Pipe.Core.Profile;
using DH.Pipe.Core.Section;

namespace DH.Pipe.Core.Quantity;

/// <summary>수직구 하나.</summary>
public sealed class ShaftQty
{
    public string Name { get; set; } = "";
    public double Sta { get; set; }
    public ShaftShape Shape { get; set; }
    public ShaftWall Wall { get; set; }
    /// <summary>사각: 폭 × 길이(노선 방향) · 원형: 지름(W).</summary>
    public double W { get; set; }
    public double L { get; set; }
    public double GroundZ { get; set; }
    public double BottomZ { get; set; }
    public double Depth => Math.Max(0, GroundZ - BottomZ);
    public double ExcVol { get; set; }
    public int HPiles { get; set; }
    public double HPileLen { get; set; }
    public double LagArea { get; set; }
    public int Sheets { get; set; }
    public double SheetLen { get; set; }
    public double LinerLen { get; set; }
    public string SizeText => Shape == ShaftShape.Rect ? $"{W:0.0}×{L:0.0}" : $"Ø{W:0.0}";
}

/// <summary>공법 구간 하나의 수량.</summary>
public sealed class SpanQty
{
    public MethodSpan Span { get; set; } = new();
    public double StaFrom { get; set; }
    public double StaTo { get; set; }
    public double Length => StaTo - StaFrom;
    public double AvgDepth { get; set; }
    public double MaxDepth { get; set; }

    // 가시설
    public double AvgWallH { get; set; }
    public double SpecH { get; set; }
    public double SpecB { get; set; }
    /// <summary>조립식 판넬 형(300 · 400 · 500 · 600).</summary>
    public int PanelType { get; set; }
    public double WallArea { get; set; }
    public int HPiles { get; set; }
    public double HPileLen { get; set; }
    public int Sheets { get; set; }
    public double SheetLen { get; set; }

    // 하천 횡단
    public double DamLength { get; set; }
    public int Bags { get; set; }
    public int DamSheets { get; set; }
    public double DamSheetLen { get; set; }

    // 추진
    public double JackLength { get; set; }
    public double CasingOd { get; set; }
    public List<ShaftQty> Shafts { get; } = new();

    public string SpecText => Span.IsVertical ? $"H={SpecH:0.00}m,B={SpecB:0.00}m" : "";
}

public sealed class PipeQty
{
    public PipeSpec Pipe { get; set; } = new();
    public double StaFrom { get; set; }
    public double StaTo { get; set; }
    public double PlanLen { get; set; }
    public double SlopeLen { get; set; }
    public int Count { get; set; }
}

public sealed class BendQty
{
    public double Sta { get; set; }
    public bool Vertical { get; set; }
    public double Deg { get; set; }
    /// <summary>평면 곡선(전체 교각)인지.</summary>
    public bool Curve { get; set; }
    /// <summary>곡관 조합(예 «45° + 11¼°») · 작으면 «이음 조정».</summary>
    public string Fitting { get; set; } = "";
}

/// <summary>구간 · 관 · 부속 수량(토적표 밖의 것).</summary>
public sealed class Summary
{
    public List<SpanQty> Spans { get; } = new();
    public List<PipeQty> Pipes { get; } = new();
    public List<BendQty> Bends { get; } = new();
    public int JointPits { get; set; }
    public double JointPitVol { get; set; }
    public int Protections { get; set; }
    public double TapeLen { get; set; }
    public List<string> Notes { get; } = new();

    /// <summary>이 각보다 작으면 곡관 없이 이음에서 조정하는 것으로 본다(도). 관종별 허용각은 확인 못 해 낮게 잡음.</summary>
    public const double JointAdjustDeg = 2.5;

    public static string FittingOf(double deg)
    {
        double a = Math.Abs(deg);
        if (a < JointAdjustDeg) return "이음 조정";
        int units = (int)Math.Round(a / 11.25, MidpointRounding.AwayFromZero);
        if (units < 1) units = 1;
        var parts = new List<string>();
        foreach (var (u, name) in new[] { (8, "90°"), (4, "45°"), (2, "22½°"), (1, "11¼°") })
        {
            while (units >= u) { parts.Add(name); units -= u; }
        }
        return string.Join(" + ", parts);
    }

    public static Summary Build(RouteContext c, IReadOnlyList<StationRow> rows, Pl ground)
    {
        var sum = new Summary();
        var route = c.Route; var std = route.Std;
        if (rows.Count < 2) return sum;
        double s0 = rows[0].Sta, s1 = rows[^1].Sta;

        // ---------------------------------------------------------------- 공법 구간(없는 곳은 자연 터파기 기본 구간)
        var spans = new List<(double A, double B, MethodSpan M)>();
        var cuts = new SortedSet<double> { s0, s1 };
        foreach (var m in route.Methods)
        {
            if (m.StaFrom > s0 && m.StaFrom < s1) cuts.Add(m.StaFrom);
            if (m.StaTo > s0 && m.StaTo < s1) cuts.Add(m.StaTo);
        }
        var cl = cuts.ToList();
        for (int i = 0; i + 1 < cl.Count; i++)
        {
            var m = route.MethodAt(0.5 * (cl[i] + cl[i + 1]));
            if (spans.Count > 0 && ReferenceEquals(spans[^1].M, m)) spans[^1] = (spans[^1].A, cl[i + 1], m);
            else spans.Add((cl[i], cl[i + 1], m));
        }

        foreach (var (a, b, m) in spans)
        {
            var q = new SpanQty { Span = m, StaFrom = a, StaTo = b };
            // 구간 안 측점 토막마다: 앞 측점의 «후» 단면 ~ 뒷 측점의 «전» 단면(토적표와 같은 짝 — 구간 안에서 관이 바뀌는 측점도 맞게)
            var inSpan = rows.Where(row => row.Sta >= a - 1e-6 && row.Sta <= b + 1e-6).ToList();
            var secs = new List<(double S, SectionResult R)>();
            for (int i = 0; i < inSpan.Count; i++)
            {
                if (i > 0) secs.Add((inSpan[i].Sta, inSpan[i].Before));
                if (i + 1 < inSpan.Count) secs.Add((inSpan[i].Sta, inSpan[i].After));
            }
            double lenOk = 0, sumDepth = 0, sumWall = 0, wallArea = 0;
            for (int i = 0; i + 1 < inSpan.Count; i++)
            {
                double sa = inSpan[i].Sta, sb = inSpan[i + 1].Sta;
                var ra = inSpan[i].After; var rb = inSpan[i + 1].Before;
                if (!ra.Ok || !rb.Ok) continue;
                double l = sb - sa;
                lenOk += l;
                sumDepth += 0.5 * (ra.L(Q.Depth) + rb.L(Q.Depth)) * l;
                double ha = 0.5 * (ra.L(Q.WallL) + ra.L(Q.WallR)), hb = 0.5 * (rb.L(Q.WallL) + rb.L(Q.WallR));
                sumWall += 0.5 * (ha + hb) * l;
                wallArea += 0.5 * (ra.L(Q.WallL) + ra.L(Q.WallR) + rb.L(Q.WallL) + rb.L(Q.WallR)) * l;
            }
            q.AvgDepth = lenOk > 0 ? sumDepth / lenOk : 0;
            q.MaxDepth = secs.Where(x => x.R.Ok).Select(x => x.R.L(Q.Depth)).DefaultIfEmpty(0).Max();

            if (m.IsVertical)
            {
                q.AvgWallH = lenOk > 0 ? Math.Round(sumWall / lenOk, 2) : 0;
                q.SpecH = Math.Ceiling(q.AvgWallH * 10 - 1e-9) / 10.0;             // 0.1 올림(샘플 가시설 산출서)
                q.SpecB = secs.Where(x => x.R.Ok).Select(x => x.R.L(Q.BotWidth)).DefaultIfEmpty(0).Max();
                q.WallArea = Math.Round(wallArea, 2);
                if (m.Kind == MethodKind.ShoreSk)
                    q.PanelType = q.AvgWallH < 3.01 ? 300 : q.AvgWallH < 4.01 ? 400 : q.AvgWallH < 5.01 ? 500 : 600;
                if (m.Kind == MethodKind.HPile)
                {
                    // 본수 = 올림(길이 / 간격) × 2열 — 현장 샘플 A 가시설 산출서 식 그대로(끝 말뚝 +1 은 넣지 않음)
                    q.HPiles = (int)Math.Ceiling(q.Length / std.HPilePitch - 1e-9) * 2;
                    q.HPileLen = q.AvgWallH + std.HPileEmbed;
                }
                if (m.Kind == MethodKind.SheetPile)
                {
                    q.Sheets = (int)Math.Ceiling(q.Length / std.SheetWidth - 1e-9) * 2;
                    q.SheetLen = q.AvgWallH + std.SheetEmbed;
                }
            }
            else if (m.Kind == MethodKind.RiverOpen)
            {
                // 가물막이(초안 규칙): 반폭씩 2단계 · 단계마다 노선과 나란한 둑 2줄(각 구간의 절반) + 가로 둑 1줄(관에서 양쪽 DamOffset)
                q.DamLength = Math.Round(2 * q.Length + 4 * std.DamOffset, 2);
                if (m.Dam == DamKind.Bag)
                {
                    int layers = Math.Max(1, (int)Math.Ceiling(std.DamHeight / std.BagH - 1e-9));
                    q.Bags = (int)Math.Ceiling(q.DamLength / std.BagW - 1e-9) * layers * (layers + 1) / 2;
                }
                else
                {
                    q.DamSheets = (int)Math.Ceiling(q.DamLength / std.SheetWidth - 1e-9);
                    q.DamSheetLen = std.DamHeight + std.SheetEmbed;
                }
            }
            else if (m.Kind == MethodKind.Jacking)
            {
                var pipe = route.PipeAt(0.5 * (a + b));
                q.CasingOd = Math.Max(pipe.Od, m.CasingOd is > 0 ? m.CasingOd.Value : pipe.Od + std.CasingExtra);
                double inv = c.Profile.At(0.5 * (a + b));
                double casBottom = PipeGeom.OuterBottom(pipe, inv) + pipe.Od * 0.5 - q.CasingOd * 0.5;
                double bottom = casBottom - std.JackBase;
                bool launchAtEnd = m.Reverse;
                foreach (bool atEnd in new[] { false, true })
                {
                    bool launch = atEnd == launchAtEnd;
                    double sta = atEnd ? b : a;
                    var sh = new ShaftQty
                    {
                        Name = launch ? "발진 수직구" : "도달 수직구", Sta = sta, Shape = m.Shaft, Wall = m.Wall,
                        GroundZ = ground.At(sta), BottomZ = bottom,
                    };
                    if (m.Shaft == ShaftShape.Rect)
                    {
                        sh.W = launch ? std.LaunchW : std.ArriveW;
                        sh.L = launch ? std.LaunchL : std.ArriveL;
                        double per = 2 * (sh.W + sh.L);
                        sh.ExcVol = Math.Round(sh.W * sh.L * sh.Depth, 2);
                        if (m.Wall == ShaftWall.HPile)
                        {
                            sh.HPiles = (int)Math.Ceiling(per / std.HPilePitch - 1e-9);
                            sh.HPileLen = sh.Depth + std.ShaftHpEmbed;
                            sh.LagArea = Math.Round(per * sh.Depth, 2);
                        }
                        else
                        {
                            sh.Sheets = (int)Math.Ceiling(per / std.SheetWidth - 1e-9);
                            sh.SheetLen = sh.Depth + std.SheetEmbed;
                        }
                    }
                    else
                    {
                        sh.W = launch ? std.LaunchDia : std.ArriveDia;
                        sh.L = sh.W;
                        sh.ExcVol = Math.Round(Math.PI * sh.W * sh.W * 0.25 * sh.Depth, 2);
                        sh.LinerLen = Math.Round(sh.Depth, 2);
                    }
                    q.Shafts.Add(sh);
                }
                q.JackLength = Math.Round(Math.Max(0, q.Length - 0.5 * (q.Shafts[0].L + q.Shafts[1].L)), 2);
            }
            sum.Spans.Add(q);
        }

        // ---------------------------------------------------------------- 관
        var runCuts = new SortedSet<double> { s0, s1 };
        foreach (var r in route.Runs)
        {
            if (r.StaFrom > s0 && r.StaFrom < s1) runCuts.Add(r.StaFrom);
            if (r.StaTo > s0 && r.StaTo < s1) runCuts.Add(r.StaTo);
        }
        var rc = runCuts.ToList();
        for (int i = 0; i + 1 < rc.Count; i++)
        {
            var pipe = route.PipeAt(0.5 * (rc[i] + rc[i + 1]));
            double sl = c.Profile.SlopeLength(rc[i], rc[i + 1]);
            var last = sum.Pipes.Count > 0 ? sum.Pipes[^1] : null;
            if (last != null && last.Pipe.Dn == pipe.Dn && last.Pipe.Material == pipe.Material)
            {
                last.StaTo = rc[i + 1];
                last.PlanLen += rc[i + 1] - rc[i];
                last.SlopeLen += sl;
            }
            else sum.Pipes.Add(new PipeQty { Pipe = pipe, StaFrom = rc[i], StaTo = rc[i + 1], PlanLen = rc[i + 1] - rc[i], SlopeLen = sl });
        }
        foreach (var p in sum.Pipes)
        {
            p.SlopeLen = Math.Round(p.SlopeLen, 2);
            p.Count = (int)Math.Ceiling(p.SlopeLen / Math.Max(p.Pipe.UnitLength, 0.1) - 1e-9);
        }

        // ---------------------------------------------------------------- 곡관(평면 꺾임 · 종단 꺾임)
        foreach (var (s, deg, curve) in c.Plan.Bends())
        {
            if (s < s0 - 1e-6 || s > s1 + 1e-6 || Math.Abs(deg) < 0.05) continue;
            // 곡선은 통째로 한 줄(교각) — 곡관으로 나눌지 이음에서 돌릴지는 설계자가 정한다
            sum.Bends.Add(new BendQty { Sta = s, Deg = deg, Curve = curve, Fitting = curve ? "곡선(교각) — 이음 조정 또는 곡관 나눔" : FittingOf(deg) });
        }
        for (int i = 1; i + 1 < c.Profile.Pvi.Count; i++)
        {
            double g1 = c.Profile.GradePct(i - 1) / 100.0, g2 = c.Profile.GradePct(i) / 100.0;
            double deg = (Math.Atan(g2) - Math.Atan(g1)) * 180.0 / Math.PI;
            if (Math.Abs(deg) < 0.05) continue;
            sum.Bends.Add(new BendQty { Sta = c.Profile.Pvi[i].S, Vertical = true, Deg = deg, Fitting = FittingOf(deg) });
        }
        sum.Bends.Sort((x, y) => x.Sta.CompareTo(y.Sta));

        // ---------------------------------------------------------------- 접합부 터파기(강관 용접부) — 개략 식
        if (std.JointPit)
        {
            foreach (var sq in sum.Spans)
            {
                if (sq.Span.Kind == MethodKind.Jacking) continue;
                var pipe = route.PipeAt(0.5 * (sq.StaFrom + sq.StaTo));
                if (pipe.Material != "SP") continue;
                int n = (int)Math.Floor(c.Profile.SlopeLength(sq.StaFrom, sq.StaTo) / Math.Max(pipe.UnitLength, 0.1));
                if (n <= 0) continue;
                double bp = pipe.Od + 2 * std.JointPitSide;
                double extra = pipe.Od <= std.JointPitLargeOd ? std.JointPitDepthSmall : std.JointPitDepthLarge;
                double bNormal = sq.Span.IsVertical ? Math.Max(sq.SpecB, 0) : pipe.Od + 2 * (sq.Span.Clearance ?? std.Clearance);
                double each = std.JointPitLength * (Math.Max(0, bp - bNormal) * sq.AvgDepth + bp * extra);
                sum.JointPits += n;
                sum.JointPitVol += n * each;
            }
            sum.JointPitVol = Math.Round(sum.JointPitVol, 2);
            if (sum.JointPits > 0) sum.Notes.Add("접합부 터파기 = 길이 × ((접합부 폭 − 바닥폭) × 평균 깊이 + 접합부 폭 × 더 파는 깊이) — 개략");
        }

        // ---------------------------------------------------------------- 지장물 보호공 · 경고 테이프
        sum.Protections = route.Obstacles.Count(o => o.Sta >= s0 && o.Sta <= s1 && !route.MethodAt(o.Sta).IsLevel);
        foreach (var sq in sum.Spans)
            if (sq.Span.Kind != MethodKind.Jacking) sum.TapeLen += c.Profile.SlopeLength(sq.StaFrom, sq.StaTo);
        sum.TapeLen = Math.Round(sum.TapeLen, 2);
        if (sum.Spans.Any(s => s.Span.Kind == MethodKind.RiverOpen))
            sum.Notes.Add("가물막이 길이 = 2 × 구간 길이 + 4 × 관에서 둑까지 거리(반폭씩 2단계) — 초안 규칙");
        return sum;
    }
}
