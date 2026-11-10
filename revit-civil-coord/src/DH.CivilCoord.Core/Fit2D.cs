using System.Globalization;

namespace DH.CivilCoord.Core;

/// <summary>평면 점(m).</summary>
public readonly record struct P2(double X, double Y)
{
    public static P2 Mid(P2 a, P2 b) => new((a.X + b.X) / 2, (a.Y + b.Y) / 2);
    public static double Dist(P2 a, P2 b) => Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));
}

/// <summary>
/// 두 점 맞춤(JACK 1110): Revit 평면 좌표(내부 좌표 · m) → Civil 도면 좌표(m).
/// <para>IFC(<see cref="Apply"/>) = 옮김 + 돌림 + <b>평면 비율</b>(JACK 1110 «Civil 선분 길이와 Revit에서 찍은 길이가 다르면 그 비율만큼 모든 객체를 확대 · 축소» ·
/// 인터뷰: 평면 X · Y 만 · 높이는 레벨 높이 그대로 · 묻지 않고 늘 비율대로) — 두 점이 Civil 두 점에 꼭 맞는다.
/// 세계 XY = Cm + k·R(θ)·(p − Rm) = k·R(θ)·p + T (k = Civil 거리 ÷ Revit 거리).</para>
/// <para>공유 좌표(<see cref="ApplyRigid"/>) = 옮김 + 돌림만(Revit 공유 좌표는 비율을 못 담음) — 두 점 가운데(중점)끼리 맞추고 끝점은 거리 차의 반씩 어긋남.</para>
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

    /// <summary>평면 비율 k = Civil 거리 ÷ Revit 거리(같으면 1).</summary>
    public double Scale => RevitDistance > 0 ? CivilDistance / RevitDistance : 1;

    /// <summary>IFC 쪽: 옮김 + 돌림 + 평면 비율 — R1 → C1 · R2 → C2 꼭 맞음.</summary>
    public P2 Apply(P2 p) => Map(p, Scale);

    /// <summary>공유 좌표 쪽: 옮김 + 돌림만(비율 1) — 중점끼리 맞고 끝점은 거리 차의 반씩.</summary>
    public P2 ApplyRigid(P2 p) => Map(p, 1);

    /// <summary>IFC 세계 XY = k·R·p + T 의 T(m).</summary>
    public P2 Translation => Offset(Scale);

    /// <summary>공유 좌표 세계 XY = R·p + T 의 T(m).</summary>
    public P2 RigidTranslation => Offset(1);

    private P2 Map(P2 p, double k)
    {
        double c = Math.Cos(Angle), s = Math.Sin(Angle);
        var rm = P2.Mid(R1, R2); var cm = P2.Mid(C1, C2);
        double dx = k * (p.X - rm.X), dy = k * (p.Y - rm.Y);
        return new P2(cm.X + c * dx - s * dy, cm.Y + s * dx + c * dy);
    }

    private P2 Offset(double k)
    {
        double c = Math.Cos(Angle), s = Math.Sin(Angle);
        var rm = P2.Mid(R1, R2); var cm = P2.Mid(C1, C2);
        return new P2(cm.X - k * (c * rm.X - s * rm.Y), cm.Y - k * (s * rm.X + c * rm.Y));
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

    // ── Civil 좌표를 얻는 법 두 가지(JACK 1110 «오해 없게 탭으로 분리»): "file" = 기준점 파일 · "manual" = 직접 입력.
    //    두 값은 따로 둔다 — 파일 값은 CivilLoaded · 직접 넣은 값은 C1X … C2Y. 계산은 고른 쪽 값만 쓴다.
    /// <summary>"file" · "manual" · ""(옛 판 기록 — <see cref="ResolveMode"/>가 정함).</summary>
    public string Mode { get; set; } = "";

    public bool FileMode => Mode != "manual";

    /// <summary>
    /// 모드가 비었으면(처음 · v1.0 기록) 정함: 직접 넣은 값만 있으면 직접 입력 · 파일을 불러온 뒤 칸을 손으로 고친 기록도 직접 입력
    /// (v1.0 은 파일 값을 칸에 넣고 고친 칸 값으로 내보냈음 — 파일 탭으로 열면 그 값이 말없이 바뀜 · 검토 중간) · 아니면 파일.
    /// </summary>
    public void ResolveMode()
    {
        if (Mode is "file" or "manual") return;
        bool typed = (C1X + C1Y + C2X + C2Y).Trim().Length > 0;
        bool edited = CivilLoaded.Length > 0 && typed && CivilLoaded != string.Join("|", C1X, C1Y, C2X, C2Y);
        Mode = (CivilFile.Length == 0 && typed) || edited ? "manual" : "file";
    }

    // ── Civil 기준점 파일에서 불러온 것(JACK 1110 «타이핑 말고 Civil에서 받아오기»)
    /// <summary>불러온 파일 경로(빈 글 = 안 불러옴).</summary>
    public string CivilFile { get; set; } = "";
    /// <summary>«이름» · 도면 · EPSG.</summary>
    public string CivilSummary { get; set; } = "";
    /// <summary>점 1 · 2 의 Civil 지반고 한 줄(없으면 빈 글).</summary>
    public string CivilH1 { get; set; } = "";
    public string CivilH2 { get; set; } = "";
    /// <summary>파일에서 읽은 Civil 좌표 넷(«|»로 이음 · µm 까지 · 천 단위 쉼표) — 파일 모드의 계산 값.</summary>
    public string CivilLoaded { get; set; } = "";

    /// <summary>파일 값 넷(없으면 빈 글 넷).</summary>
    public (string C1X, string C1Y, string C2X, string C2Y) FileValues
    {
        get
        {
            var p = CivilLoaded.Split('|');
            return p.Length == 4 ? (p[0], p[1], p[2], p[3]) : ("", "", "", "");
        }
    }

    /// <summary>Civil 기준점 파일 값을 파일 모드 값으로(직접 입력 칸은 그대로 둠) · 파일 모드로.</summary>
    public void ApplyCivil(CivilPoints cp, string path)
    {
        CivilFile = path;
        CivilSummary = cp.Summary;
        CivilH1 = CivilPoints.Heights(cp.Points[0]);
        CivilH2 = CivilPoints.Heights(cp.Points[1]);
        CivilLoaded = Loaded(cp);
        Mode = "file";
    }

    /// <summary>파일 좌표가 지금 기록(<see cref="CivilLoaded"/>)과 다른가 — Civil 에서 다시 내보낸 파일.</summary>
    public bool FileDiffers(CivilPoints cp) => CivilLoaded.Length > 0 && Loaded(cp) != CivilLoaded;

    private static string Loaded(CivilPoints cp)
    {
        static string T(double v) => v.ToString("#,0.0#####", CultureInfo.InvariantCulture);
        return string.Join("|", T(cp.Points[0].X), T(cp.Points[0].Y), T(cp.Points[1].X), T(cp.Points[1].Y));
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

    /// <summary>점 둘 · 좌표 넷(고른 모드 쪽)이 다 있으면 맞춤(값이 이상해도 만든다 — 까닭은 <see cref="Fit2D.Problem"/>). 빠진 게 있으면 null.</summary>
    public Fit2D? Fit()
    {
        if (R1 is not { Length: >= 2 } || R2 is not { Length: >= 2 }) return null;
        var (s1x, s1y, s2x, s2y) = FileMode ? FileValues : (C1X, C1Y, C2X, C2Y);
        double a = Num(s1x), b = Num(s1y), c = Num(s2x), d = Num(s2y);
        if (double.IsNaN(a) || double.IsNaN(b) || double.IsNaN(c) || double.IsNaN(d)) return null;
        return new Fit2D(new P2(R1[0], R1[1]), new P2(R2[0], R2[1]), new P2(a, b), new P2(c, d));
    }

    public FitState Clone() => new()
    {
        R1 = R1?.ToArray(), R2 = R2?.ToArray(), C1X = C1X, C1Y = C1Y, C2X = C2X, C2Y = C2Y, Dz = Dz, Mode = Mode,
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
