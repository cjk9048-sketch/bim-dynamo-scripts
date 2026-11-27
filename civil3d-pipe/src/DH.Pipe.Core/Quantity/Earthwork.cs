using DH.Pipe.Core.Model;
using DH.Pipe.Core.Section;

namespace DH.Pipe.Core.Quantity;

/// <summary>측점 이름 · 측점 목록.</summary>
public static class StationPlan
{
    /// <summary>«1+2.97» 꼴(간격 20 m 를 No. 로).</summary>
    public static string Name(double sta, double interval = 20)
    {
        if (interval <= 0) interval = 20;
        double s = Math.Round(sta, 2);
        int no = (int)Math.Floor(s / interval + 1e-9);
        double rest = s - no * interval;
        if (rest < 0) rest = 0;
        return $"{no}+{rest:0.00}";
    }

    /// <summary>
    /// 측점 목록: 일정 간격 + 꼭 있어야 하는 곳(must — 시점 · 종점 · 공법 경계 · 꺾인 점 · PVI …).
    /// 꼭 있어야 하는 곳에서 tol 안에 든 간격 측점은 뺀다.
    /// </summary>
    public static List<double> Build(double s0, double s1, double interval, IEnumerable<double> must, double tol = 0.5)
    {
        var keep = new List<double> { s0, s1 };
        foreach (double m in must) if (m > s0 + 1e-6 && m < s1 - 1e-6) keep.Add(m);
        keep.Sort();
        var uniq = new List<double>();
        foreach (double k in keep) if (uniq.Count == 0 || k - uniq[^1] > 1e-4) uniq.Add(k);
        var all = new List<double>(uniq);
        if (interval > 0)
        {
            for (double s = Math.Ceiling((s0 + 1e-9) / interval) * interval; s < s1 - 1e-9; s += interval)
            {
                bool near = false;
                foreach (double k in uniq) if (Math.Abs(k - s) < tol) { near = true; break; }
                if (!near) all.Add(s);
            }
        }
        all.Sort();
        return all;
    }
}

/// <summary>한 측점의 단면(공법 경계는 전 · 후 두 장).</summary>
public sealed class StationRow
{
    public double Sta { get; set; }
    public string Name { get; set; } = "";
    /// <summary>앞 구간 쪽 단면(전).</summary>
    public SectionResult Before { get; set; } = new();
    /// <summary>뒤 구간 쪽 단면(후). 경계가 아니면 Before 와 같은 것.</summary>
    public SectionResult After { get; set; } = new();
    public bool IsBreak => !ReferenceEquals(Before, After);
}

/// <summary>토적표 한 줄(앞 측점 ~ 이 측점).</summary>
public sealed class EarthRow
{
    public double Sta { get; set; }
    public string Name { get; set; } = "";
    public double Dist { get; set; }
    /// <summary>이 측점의 단면 값(전 단면) · 다음 구간 계산에 쓰는 값(후 단면).</summary>
    public Dictionary<string, double> Here { get; } = new();
    public Dictionary<string, double> Next { get; } = new();
    /// <summary>앞 측점 ~ 이 측점의 양(평균 × 거리).</summary>
    public Dictionary<string, double> Qty { get; } = new();
    public bool Missing { get; set; }
    public string Note { get; set; } = "";
}

/// <summary>토적표(평균단면법).</summary>
public sealed class EarthTable
{
    /// <summary>열(넓이 → 부피).</summary>
    public List<string> AreaKeys { get; } = new();
    /// <summary>열(길이 · 개수 → 넓이 · 길이).</summary>
    public List<string> LenKeys { get; } = new();
    public List<EarthRow> Rows { get; } = new();
    public Dictionary<string, double> Total { get; } = new();
    public int MissingIntervals { get; set; }
    public double DepthLimit { get; set; } = 5.0;

    public double T(string key) => Total.TryGetValue(key, out double v) ? v : 0;
    public double ExcTotal => Total.Where(kv => kv.Key.StartsWith("EXC.")).Sum(kv => kv.Value);
    public double BackfillTotal => T(Q.Bed) + T(Q.Zone) + T(Q.Upper);
    /// <summary>잔토 = 터파기 − 되메우기(관상단 · 유용).</summary>
    public double Surplus => ExcTotal - T(Q.Upper);

    /// <summary>표에 쓰는 열 순서(샘플 집계표 순서: 육상 → 용수 · 5m 이하 → 초과 · 토사 → 풍화암 → 연암 → 포장 → 되메우기).</summary>
    public static readonly string[] AreaOrder = BuildAreaOrder();

    private static string[] BuildAreaOrder()
    {
        var l = new List<string>();
        for (int wet = 0; wet < 2; wet++)
            for (int deep = 0; deep < 2; deep++)
                for (int rock = 0; rock < 3; rock++)
                    l.Add(Q.Exc(wet == 1, deep == 1, rock));
        l.AddRange(new[] { Q.PavAsp, Q.PavCon, Q.PavBlk, Q.Subbase, Q.Bed, Q.Zone, Q.Upper, Q.Encase, Q.Bore });
        return l.ToArray();
    }

    /// <summary>길이 열: 평균 × 거리로 양이 되는 것만.</summary>
    public static readonly string[] LenOrder = { Q.CutAsp, Q.CutCon, Q.CutBlk, Q.CutLines, Q.WallL, Q.WallR, Q.Curb };

    public static EarthTable Build(IReadOnlyList<StationRow> rows, double depthLimit = 5.0)
    {
        var t = new EarthTable { DepthLimit = depthLimit };
        foreach (string k in AreaOrder)
            if (rows.Any(r => r.Before.A(k) > 1e-9 || r.After.A(k) > 1e-9)) t.AreaKeys.Add(k);
        foreach (string k in LenOrder)
            if (rows.Any(r => r.Before.L(k) > 1e-9 || r.After.L(k) > 1e-9)) t.LenKeys.Add(k);

        for (int i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var e = new EarthRow { Sta = row.Sta, Name = row.Name };
            foreach (string k in t.AreaKeys) { e.Here[k] = row.Before.A(k); e.Next[k] = row.After.A(k); }
            foreach (string k in t.LenKeys) { e.Here[k] = row.Before.L(k); e.Next[k] = row.After.L(k); }
            if (i > 0)
            {
                var prev = rows[i - 1];
                e.Dist = row.Sta - prev.Sta;
                if (!prev.After.Ok || !row.Before.Ok)
                {
                    e.Missing = true;                           // 단면을 못 만든 구간 — 0 으로 덮지 않고 표시
                    e.Note = "단면 없음";
                    t.MissingIntervals++;
                }
                else
                {
                    foreach (string k in t.AreaKeys) Put(t, e, k, (prev.After.A(k) + row.Before.A(k)) * 0.5 * e.Dist);
                    foreach (string k in t.LenKeys) Put(t, e, k, (prev.After.L(k) + row.Before.L(k)) * 0.5 * e.Dist);
                }
            }
            t.Rows.Add(e);
        }
        return t;
    }

    private static void Put(EarthTable t, EarthRow e, string k, double v)
    {
        v = Math.Round(v, 2, MidpointRounding.AwayFromZero);      // 줄마다 소수 둘째 자리(샘플 산출서)
        e.Qty[k] = v;
        t.Total[k] = t.T(k) + v;
    }
}
