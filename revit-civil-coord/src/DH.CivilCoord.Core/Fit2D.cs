using System.Globalization;

namespace DH.CivilCoord.Core;

/// <summary>평면 점(m).</summary>
public readonly record struct P2(double X, double Y)
{
    public static P2 Mid(P2 a, P2 b) => new((a.X + b.X) / 2, (a.Y + b.Y) / 2);
    public static double Dist(P2 a, P2 b) => Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));
}

/// <summary>
/// 두 점 맞춤(JACK 1110): Revit 평면 좌표(내부 좌표 · m) → Civil 도면 좌표(m). 옮김 + 돌림만(축척 없음 — 둘 다 m).
/// <para>돌림 = Revit 두 점 방향과 Civil 두 점 방향의 차. 옮김 = 두 점의 가운데(중점)끼리 맞춤 — 두 거리가 조금 다르면 어긋남을 두 점에 반씩 나눈다
/// (점 1에만 맞추면 점 2에 차가 몰림). 거리 차는 <see cref="DistanceError"/>로 보여 준다.</para>
/// <para>세계 = Cm + R(θ)·(p − Rm) = R(θ)·p + T.</para>
/// </summary>
public sealed class Fit2D
{
    /// <summary>두 점이 이보다 가까우면 방향이 흔들려 받지 않는다(m).</summary>
    public const double MinDistance = 1.0;

    public P2 R1 { get; }
    public P2 R2 { get; }
    public P2 C1 { get; }
    public P2 C2 { get; }

    public Fit2D(P2 r1, P2 r2, P2 c1, P2 c2) { R1 = r1; R2 = r2; C1 = c1; C2 = c2; }

    public double RevitDistance => P2.Dist(R1, R2);
    public double CivilDistance => P2.Dist(C1, C2);
    /// <summary>Civil 거리 − Revit 거리(m). 같은 두 점이면 0에 가깝다.</summary>
    public double DistanceError => CivilDistance - RevitDistance;

    /// <summary>못 쓰는 까닭(쓸 수 있으면 null).</summary>
    public string? Problem
    {
        get
        {
            foreach (var p in new[] { R1, R2, C1, C2 })
                if (!double.IsFinite(p.X) || !double.IsFinite(p.Y)) return "숫자가 아닌 값이 있습니다";
            if (RevitDistance < MinDistance) return $"Revit 두 점이 {MinDistance:0} m보다 가깝습니다";
            if (CivilDistance < MinDistance) return $"Civil 두 점이 {MinDistance:0} m보다 가깝습니다";
            return null;
        }
    }

    /// <summary>돌림(라디안 · 반시계 +) −π..π.</summary>
    public double Angle => Normalize(Math.Atan2(C2.Y - C1.Y, C2.X - C1.X) - Math.Atan2(R2.Y - R1.Y, R2.X - R1.X));

    public double AngleDegrees => Angle * 180.0 / Math.PI;

    public P2 Apply(P2 p)
    {
        double c = Math.Cos(Angle), s = Math.Sin(Angle);
        var rm = P2.Mid(R1, R2); var cm = P2.Mid(C1, C2);
        double dx = p.X - rm.X, dy = p.Y - rm.Y;
        return new P2(cm.X + c * dx - s * dy, cm.Y + s * dx + c * dy);
    }

    /// <summary>세계 = R·p + T 의 T(m).</summary>
    public P2 Translation
    {
        get
        {
            double c = Math.Cos(Angle), s = Math.Sin(Angle);
            var rm = P2.Mid(R1, R2); var cm = P2.Mid(C1, C2);
            return new P2(cm.X - (c * rm.X - s * rm.Y), cm.Y - (s * rm.X + c * rm.Y));
        }
    }

    /// <summary>−π..π 로.</summary>
    public static double Normalize(double a)
    {
        if (!double.IsFinite(a)) return 0;
        a = Math.IEEERemainder(a, 2 * Math.PI);
        if (a <= -Math.PI) a += 2 * Math.PI;
        if (a > Math.PI) a -= 2 * Math.PI;
        return a;
    }
}

/// <summary>
/// 창에 넣은 값 그대로(rvt 안에 JSON 으로 둔다 — 다음에 열면 그대로). Revit 점 = 내부 좌표(m) · Civil 값 = 사람이 적은 글 그대로(쉼표 허용).
/// </summary>
public sealed class FitState
{
    public double[]? R1 { get; set; }
    public double[]? R2 { get; set; }
    public string C1X { get; set; } = "";
    public string C1Y { get; set; } = "";
    public string C2X { get; set; } = "";
    public string C2Y { get; set; } = "";
    /// <summary>위아래 옮김(m) — 레벨 높이에 더함.</summary>
    public string Dz { get; set; } = "0";

    // ── Civil 기준점 파일에서 불러온 것(JACK 1110 «타이핑 말고 Civil에서 받아오기»)
    /// <summary>불러온 파일 경로(빈 글 = 손으로 넣음).</summary>
    public string CivilFile { get; set; } = "";
    /// <summary>«이름» · 도면 · EPSG.</summary>
    public string CivilSummary { get; set; } = "";
    /// <summary>점 1 · 2 의 Civil 지반고 한 줄(없으면 빈 글).</summary>
    public string CivilH1 { get; set; } = "";
    public string CivilH2 { get; set; } = "";
    /// <summary>불러올 때 칸에 넣은 글 넷(«|»로 이음) — 지금 칸과 다르면 손으로 고친 것.</summary>
    public string CivilLoaded { get; set; } = "";

    /// <summary>지금 칸이 불러온 값 그대로인가(불러온 적 없으면 false).</summary>
    public bool CivilUntouched => CivilLoaded.Length > 0 && CivilLoaded == string.Join("|", C1X, C1Y, C2X, C2Y);

    /// <summary>Civil 기준점 파일 값을 칸에 넣음(µm 까지 · 천 단위 쉼표).</summary>
    public void ApplyCivil(CivilPoints cp, string path)
    {
        static string T(double v) => v.ToString("#,0.0#####", CultureInfo.InvariantCulture);
        C1X = T(cp.Points[0].X); C1Y = T(cp.Points[0].Y);
        C2X = T(cp.Points[1].X); C2Y = T(cp.Points[1].Y);
        CivilFile = path;
        CivilSummary = cp.Summary;
        CivilH1 = CivilPoints.Heights(cp.Points[0]);
        CivilH2 = CivilPoints.Heights(cp.Points[1]);
        CivilLoaded = string.Join("|", C1X, C1Y, C2X, C2Y);
    }

    /// <summary>"200,123.450" · " 200123.45 " → 200123.45. 못 읽으면 NaN.</summary>
    public static double Num(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return double.NaN;
        string t = s.Replace(",", "").Replace(" ", "").Replace(" ", "").Trim();
        return double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) && double.IsFinite(v) ? v : double.NaN;
    }

    public double DzValue { get { double v = Num(Dz); return double.IsNaN(v) ? 0 : v; } }
    public bool DzValid => string.IsNullOrWhiteSpace(Dz) || !double.IsNaN(Num(Dz));

    /// <summary>점 둘 · 좌표 넷이 다 있으면 맞춤(값이 이상해도 만든다 — 까닭은 <see cref="Fit2D.Problem"/>). 빠진 게 있으면 null.</summary>
    public Fit2D? Fit()
    {
        if (R1 is not { Length: >= 2 } || R2 is not { Length: >= 2 }) return null;
        double a = Num(C1X), b = Num(C1Y), c = Num(C2X), d = Num(C2Y);
        if (double.IsNaN(a) || double.IsNaN(b) || double.IsNaN(c) || double.IsNaN(d)) return null;
        return new Fit2D(new P2(R1[0], R1[1]), new P2(R2[0], R2[1]), new P2(a, b), new P2(c, d));
    }

    public FitState Clone() => new()
    {
        R1 = R1?.ToArray(), R2 = R2?.ToArray(), C1X = C1X, C1Y = C1Y, C2X = C2X, C2Y = C2Y, Dz = Dz,
        CivilFile = CivilFile, CivilSummary = CivilSummary, CivilH1 = CivilH1, CivilH2 = CivilH2, CivilLoaded = CivilLoaded,
    };

    public string ToJson() => System.Text.Json.JsonSerializer.Serialize(this);

    public static FitState? FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return System.Text.Json.JsonSerializer.Deserialize<FitState>(json); }
        catch { return null; }
    }
}
