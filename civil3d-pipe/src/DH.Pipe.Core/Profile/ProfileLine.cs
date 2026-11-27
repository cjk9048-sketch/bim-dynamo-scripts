using DH.Pipe.Core.Geom;
using DH.Pipe.Core.Model;

namespace DH.Pipe.Core.Profile;

/// <summary>종단선(관저고). 꺾인 점 = PVI · 사이는 직선(관은 곡관으로 꺾는다).</summary>
public sealed class ProfileLine
{
    public List<(double S, double Z)> Pvi { get; } = new();

    public ProfileLine() { }

    public ProfileLine(IEnumerable<(double S, double Z)> pvi)
    {
        Pvi.AddRange(pvi.OrderBy(p => p.S));
    }

    public bool IsEmpty => Pvi.Count < 2;
    public double S0 => Pvi[0].S;
    public double S1 => Pvi[^1].S;

    /// <summary>측점의 관저고. 범위 밖은 끝 값.</summary>
    public double At(double s)
    {
        if (Pvi.Count == 0) return double.NaN;
        if (s <= Pvi[0].S) return Pvi[0].Z;
        if (s >= Pvi[^1].S) return Pvi[^1].Z;
        for (int i = 0; i + 1 < Pvi.Count; i++)
        {
            if (s > Pvi[i + 1].S) continue;
            double d = Pvi[i + 1].S - Pvi[i].S;
            if (d < 1e-12) return Pvi[i + 1].Z;
            return Pvi[i].Z + (Pvi[i + 1].Z - Pvi[i].Z) * ((s - Pvi[i].S) / d);
        }
        return Pvi[^1].Z;
    }

    /// <summary>i 번째 직선의 경사(%) — 오르면 +.</summary>
    public double GradePct(int i)
    {
        double d = Pvi[i + 1].S - Pvi[i].S;
        return d < 1e-9 ? 0 : (Pvi[i + 1].Z - Pvi[i].Z) / d * 100.0;
    }

    /// <summary>관을 따라 잰 길이(경사 길이) — [a, b] 구간.</summary>
    public double SlopeLength(double a, double b)
    {
        if (b <= a || Pvi.Count < 2) return Math.Max(0, b - a);
        var cuts = new List<double> { a };
        foreach (var p in Pvi) if (p.S > a + 1e-9 && p.S < b - 1e-9) cuts.Add(p.S);
        cuts.Add(b);
        double sum = 0;
        for (int i = 0; i + 1 < cuts.Count; i++)
        {
            double ds = cuts[i + 1] - cuts[i], dz = At(cuts[i + 1]) - At(cuts[i]);
            sum += Math.Sqrt(ds * ds + dz * dz);
        }
        return sum;
    }

    public Pl ToPl() => Pl.From(Pvi.Select(p => (p.S, p.Z)));
}

/// <summary>관저고 ↔ 관 바깥 윗면 · 밑면 · 토피 셈(한 곳에 모아 둔다).</summary>
public static class PipeGeom
{
    public static double OuterBottom(PipeSpec p, double invert) => invert - p.Wall;
    public static double OuterTop(PipeSpec p, double invert) => invert - p.Wall + p.Od;
    public static double Cover(PipeSpec p, double invert, double ground) => ground - OuterTop(p, invert);
    /// <summary>토피 c 를 주는 관저고.</summary>
    public static double InvertForCover(PipeSpec p, double ground, double cover) => ground - cover - p.Od + p.Wall;

    /// <summary>
    /// 하천 횡단 · 추진 구간의 수평 관저고: 구간에서 가장 낮은 땅 아래 토피.
    /// 추진은 케이싱 윗면 기준(에어로사이트 1126 검토 결정).
    /// </summary>
    public static double LevelInvert(MethodSpan m, PipeSpec p, Standards std, double groundMin)
    {
        if (m.Kind == MethodKind.Jacking)
        {
            double cover = m.Cover ?? std.JackCover;
            double cas = Math.Max(p.Od, m.CasingOd is > 0 ? m.CasingOd.Value : p.Od + std.CasingExtra);
            double center = groundMin - cover - cas * 0.5;
            return center - p.Od * 0.5 + p.Wall;
        }
        return InvertForCover(p, groundMin, m.Cover ?? std.RiverCover);
    }
}
