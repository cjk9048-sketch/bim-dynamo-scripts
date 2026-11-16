using System.Globalization;

namespace DH.Grading.Core;

/// <summary>★[수량산출서] 측점 한 줄 — 횡단 수량(㎡). 못 잰 값 = NaN(빈칸 · 0 아님).</summary>
public sealed class QtyStation
{
    public string Name = "";
    public double Sta;
    public double Fill = double.NaN, Cut = double.NaN, CutWeathered = double.NaN;
    /// <summary>터파기 — 토사 육상 ≤H · 토사 육상 &gt;H · 토사 용수(전 깊이) · 풍화암 육상 · 풍화암 용수.</summary>
    public double ExcSoilLe = double.NaN, ExcSoilGt = double.NaN, ExcSoilWater = double.NaN, ExcWeathered = double.NaN, ExcWeatheredWater = double.NaN;
    public double Backfill = double.NaN;
    /// <summary>성토면 · 절토면 가로 폭(m) — 면고르기 길이.</summary>
    public double FillW = double.NaN, CutW = double.NaN;
}

/// <summary>U형 측구 규격 하나의 묶음(같은 규격 전부 합).</summary>
public sealed class QtyDitch
{
    public string Label = "";
    public double B, H, Tw, Tb, Tf, Pf;
    public double Length;
    public int Count;
}

/// <summary>임목폐기물 입력 — 기본값 = 템플릿(밀도 18주/100㎡ · 흉고 0.22 · 근원 0.25 · K 1.3 · f 0.5 · W0 0.8 · P 0.5 · 할증 1.3).</summary>
public sealed class QtyForest
{
    public bool Known;
    public string Note = "";
    /// <summary>임상별 ㎡ — 1~4(침엽 · 활엽 · 혼효 · 죽림).</summary>
    public double[] ByFrtp = new double[5];
    public double Density = 18, Dbh = 0.22, Root = 0.25;
    /// <summary>수고(m) — NaN이면 «임상도 값 없음» → <see cref="DefaultHeight"/>.</summary>
    public double Height = double.NaN;
    public bool HeightAuto;
    public const double DefaultHeight = 7.2;
    public double KRoot = 1.3, F = 0.5, W0 = 0.8, P = 0.5, KForm = 0.5, Surcharge = 1.3;
    public double HeightUsed => double.IsFinite(Height) && Height > 0 ? Height : DefaultHeight;
}

public sealed class QtyReportModel
{
    public string Facility = "부지";
    /// <summary>터파기 깊이 구분(m).</summary>
    public double DeepLimit = 4.5;
    public List<QtyStation> Site = new();
    public bool SiteKnown;
    public List<QtyStation> Road = new();
    public bool RoadKnown;
    /// <summary>벌개제근 면적(㎡) — NaN = 못 잼.</summary>
    public double Clearing = double.NaN;
    public List<QtyDitch> Ditches = new();
    public double DitchCover = 0;
    public double DitchSlope = 0.3, DitchMargin = 0.2;
    public QtyForest Forest = new();
    /// <summary>토공집계표 밑에 적을 줄 — 못 잰 까닭 · 가정.</summary>
    public List<string> Notes = new();
}

/// <summary>★[수량산출서] 순수 계산 — 엑셀 식과 같은 식을 C#에서도 낸다(시험이 견준다).</summary>
public static class QtyReportCalc
{
    static readonly CultureInfo Ic = CultureInfo.InvariantCulture;
    public const double Pi = 3.14;   // 템플릿이 3.14를 쓴다

    /// <summary>측점(m) → (NO, 더하기) — 20 m 간격 · 소수 둘째 자리.</summary>
    public static (int No, double Plus) Split(double sta)
    {
        double t = Math.Round(sta, 2);
        int no = (int)Math.Floor(t / 20.0 + 1e-9);
        return (no, Math.Round(t - no * 20.0, 2));
    }

    /// <summary>평균단면법 합 — 값 NaN = 빈칸(0). 엑셀 식 ROUND((앞+이)/2×거리,2)의 합과 같다.</summary>
    public static double AvgEndSum(IReadOnlyList<QtyStation> st, Func<QtyStation, double> sel)
    {
        double sum = 0;
        for (int i = 1; i < st.Count; i++)
        {
            double a = V(sel(st[i - 1])), b = V(sel(st[i]));
            var p = Split(st[i - 1].Sta); var q = Split(st[i].Sta);
            double d = (q.No * 20 + q.Plus) - (p.No * 20 + p.Plus);
            sum += Math.Round((a + b) / 2 * d, 2, MidpointRounding.AwayFromZero);
        }
        return sum;
    }

    public static bool Has(IReadOnlyList<QtyStation> st, Func<QtyStation, double> sel) => st.Any(s => double.IsFinite(sel(s)));
    static double V(double v) => double.IsFinite(v) ? Math.Round(v, 2, MidpointRounding.AwayFromZero) : 0;
    internal static double R2(double v) => Math.Round(v, 2, MidpointRounding.AwayFromZero);

    // ── U형 측구 ──
    public static double BodyW(QtyDitch d) => d.B + 2 * d.Tw;
    public static double FootW(QtyDitch d) => d.Tf > 0 ? BodyW(d) + 2 * d.Pf : BodyW(d);
    public static double Depth(QtyDitch d, double cover) => d.Tf + d.Tb + d.H + cover;
    public static double Bottom(QtyDitch d, double margin) => FootW(d) + 2 * margin;
    public static double Top(QtyDitch d, double cover, double slope, double margin) => Bottom(d, margin) + 2 * slope * Depth(d, cover);
    /// <summary>터파기 = (상부폭+하부폭)/2 × 깊이 × L.</summary>
    public static double Excavation(QtyDitch d, double cover, double slope, double margin)
        => R2((Top(d, cover, slope, margin) + Bottom(d, margin)) / 2 * Depth(d, cover) * d.Length);
    /// <summary>잔토처리 = 구조물이 땅속에서 차지하는 부피 = (구체폭×(바닥+안높이) + 기초폭×기초) × L.</summary>
    public static double Occupied(QtyDitch d)
        => R2((BodyW(d) * (d.Tb + d.H) + FootW(d) * d.Tf) * d.Length);

    // ── 임목폐기물 ──
    public static double RootVol(QtyForest f) { double r = f.Root * 4 / 2; return Pi * r * r * r + Pi * r * r * r / 6; }
    public static double TopVol(QtyForest f) => Pi * f.Dbh * f.Dbh / 4 * f.HeightUsed * f.Surcharge;
    /// <summary>지상부 중량(ton/주) = K·π·(d/2)²·H·W0·(1+P).</summary>
    public static double TopWeight(QtyForest f) => f.KForm * Pi * (f.Dbh / 2) * (f.Dbh / 2) * f.HeightUsed * f.W0 * (1 + f.P);
    /// <summary>뿌리부 중량(ton/주) = K·f·V.</summary>
    public static double RootWeight(QtyForest f) => f.KRoot * f.F * RootVol(f);
    public static double Trees(double area, double density) => Math.Truncate(area * density / 100);
    /// <summary>계(TON) = Σ 임상 [TRUNC(뿌리 단위중량×수량) + TRUNC(지상 단위중량×수량)].</summary>
    public static double TotalTon(QtyForest f)
    {
        double t = 0;
        for (int k = 1; k <= 4; k++)
        {
            double n = Trees(f.ByFrtp[k], f.Density);
            t += Math.Truncate(RootWeight(f) * n) + Math.Truncate(TopWeight(f) * n);
        }
        return t;
    }

    public static string Num(double v) => v.ToString("0.###", Ic);
}
