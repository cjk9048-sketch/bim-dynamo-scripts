// ── S290 굽이 평탄화(RoadBendFlat) — 합성 굽이 + JACK 덤프(BLOCKTEST_RRJACK) 구간 수치
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using DH.Grading.Core;

static class BendFlatCheck
{
    // 직선 60 m → 호(반경 R · 각 defl°) → 직선 60 m, 1 m 표본
    static (double[] X, double[] Y, double[] S) Make(double R, double deg)
    {
        var X = new List<double>(); var Y = new List<double>(); var S = new List<double>();
        double x = 0, y = 0, th = 0, s = 0, ds = 1.0;
        void Step(double k) { X.Add(x); Y.Add(y); S.Add(s); th += k * ds / 2; x += Math.Cos(th) * ds; y += Math.Sin(th) * ds; th += k * ds / 2; s += ds; }
        for (int i = 0; i < 60; i++) Step(0);
        double arc = R * deg * Math.PI / 180; int na = (int)Math.Round(arc);
        for (int i = 0; i < na; i++) Step(1.0 / R);
        for (int i = 0; i < 60; i++) Step(0);
        X.Add(x); Y.Add(y); S.Add(s);
        return (X.ToArray(), Y.ToArray(), S.ToArray());
    }

    public static void Run(Action<string, bool, string> check)
    {
        var (x, y, s) = Make(7, 90);
        var z7 = RoadBendFlat.Detect(x, y, s, 10, 10);
        check("S290 BF R7 · 90° · 단 10 m = 급한 굽이 1곳", z7.Count == 1, string.Join(" | ", z7));
        bool both = z7.Count == 1 && z7[0].A < 60 && z7[0].B >= 60 + 11 + 3;
        check("S290 BF 구간이 곡선 앞 · 뒤 두 다리 발을 덮음", both, z7.Count > 0 ? $"{z7[0].A:F1}~{z7[0].B:F1}" : "");
        var (x2, y2, s2) = Make(60, 90);
        check("S290 BF R60 = 안 급함(0곳)", RoadBendFlat.Detect(x2, y2, s2, 10, 10).Count == 0, "");
        // 종단 — 시점 100 → 종점 70(30 m 낮음) 직선 경사 내리막
        double L = s[^1];
        var pvi = new List<(double, double)> { (0, 100), (L, 100 - 0.10 * L) };
        var r = RoadBendFlat.Apply(pvi, z7, 0.15);
        var z = r.Zones.Count > 0 ? r.Zones[0] : null;
        bool flat = z != null && Math.Abs(RoadProfileDraft.At(r.Pvi, z.A) - RoadProfileDraft.At(r.Pvi, z.B)) < 1e-9 && Math.Abs(RoadProfileDraft.At(r.Pvi, (z.A + z.B) / 2) - z.Z) < 1e-9;
        check("S290 BF 구간 0 %(양 끝 · 가운데 같은 높이)", flat, z != null ? $"{z.A:F1}~{z.B:F1} 높이 {z.Z:F2}" : "구간 없음");
        check("S290 BF 시점 · 종점 높이 그대로", r.Pvi[0].Z == 100 && Math.Abs(r.Pvi[^1].Z - (100 - 0.10 * L)) < 1e-9 && r.Pvi[0].S == 0 && Math.Abs(r.Pvi[^1].S - L) < 1e-9, "");
        check("S290 BF 경사 한도 안 = 경고 없음", r.Warnings.Count == 0, string.Join(" / ", r.Warnings));
        var rw = RoadBendFlat.Apply(pvi, RoadBendFlat.Detect(x, y, s, 10, 10), 0.101);
        check("S290 BF 직선 경사 한도 넘음 = 그래도 적용 + ⚠ 경고", rw.Warnings.Count == 1 && rw.Warnings[0].StartsWith("⚠ 굽이 1 평탄") && rw.Zones.Count == 1, string.Join(" / ", rw.Warnings));
        // 끄기 = 펴기 전 복원(Pack/Unpack · Same)
        var back = RoadBendFlat.Unpack(RoadBendFlat.Pack(pvi.Select(p => (p.Item1, p.Item2)).ToList()));
        check("S290 BF 복원 기록이 펴기 전과 같음 · 편 것과는 다름", RoadBendFlat.Same(back, pvi.Select(p => (p.Item1, p.Item2)).ToList()) && !RoadBendFlat.Same(back, r.Pvi), "");
        Jack();
    }

    static void Jack()
    {
        var dir = Environment.GetEnvironmentVariable("BLOCKTEST_RRJACK"); if (string.IsNullOrEmpty(dir)) return;
        var jo = new JsonSerializerOptions { IncludeFields = true, NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals };
        var inp = JsonSerializer.Deserialize<RoadGrade.Input>(File.ReadAllText(Path.Combine(dir, "rd_1_1.json")), jo)!;
        int n = inp.X.Length; var s = new double[n];
        for (int i = 1; i < n; i++) s[i] = s[i - 1] + Math.Sqrt(Math.Pow(inp.X[i] - inp.X[i - 1], 2) + Math.Pow(inp.Y[i] - inp.Y[i - 1], 2));
        double half = inp.Sec.HalfL + inp.Sec.ShoulderL;
        double D = half + Math.Max(inp.Cut.Slope * inp.Cut.BenchH + inp.Cut.BenchW / 2, inp.Fill.Slope * inp.Fill.BenchH + inp.Fill.BenchW / 2);
        var zones = RoadBendFlat.Detect(inp.X, inp.Y, s, D, D);
        Console.WriteLine($"[BF-JACK] 점 {n} · 길이 {s[^1]:F1} m · 단 거리 D {D:F2} · 굽이 {zones.Count}");
        foreach (var z in zones) Console.WriteLine("   " + z);
        // 종단 = Z 꺾은선(0.02 m 허용 줄임)
        var pv = new List<(double S, double Z)>(); 
        void Dp(int a, int b, List<int> keep) { if (b <= a + 1) return; double best = -1; int bi = -1; for (int i = a + 1; i < b; i++) { double zl = inp.Z[a] + (inp.Z[b] - inp.Z[a]) * (s[i] - s[a]) / (s[b] - s[a]); double d = Math.Abs(inp.Z[i] - zl); if (d > best) { best = d; bi = i; } } if (best > 0.02) { keep.Add(bi); Dp(a, bi, keep); Dp(bi, b, keep); } }
        var kp = new List<int> { 0, n - 1 }; Dp(0, n - 1, kp); kp.Sort(); foreach (var i in kp) pv.Add((s[i], inp.Z[i]));
        Console.WriteLine($"[BF-JACK] 원 종단 PVI {pv.Count}개: " + string.Join(" ", pv.Select(p => $"({p.S:F0},{p.Z:F2})")));
        var r = RoadBendFlat.Apply(pv, zones, 0.15);
        foreach (var z in r.Zones) Console.WriteLine($"   편 구간 {z.A:F1}~{z.B:F1} 높이 {z.Z:F2} · 앞 경사 {z.GradeIn * 100:F1} % · 뒤 경사 {z.GradeOut * 100:F1} %");
        foreach (var w in r.Warnings) Console.WriteLine("   " + w);
        Console.WriteLine($"[BF-JACK] 새 PVI {r.Pvi.Count}개: " + string.Join(" ", r.Pvi.Select(p => $"({p.S:F1},{p.Z:F2})")) + $" · 최대 경사 {r.MaxGrade * 100:F1} %");
        if (Environment.GetEnvironmentVariable("BLOCKTEST_RRBENDZ") == "1")
        {
            for (int i = 0; i < n; i++) inp.Z[i] = RoadProfileDraft.At(r.Pvi, s[i]);
            File.WriteAllText(Path.Combine(dir, "rd_1_1_flat.json"), JsonSerializer.Serialize(inp, jo));
            Console.WriteLine("[BF-JACK] 편 종단 입력 저장 rd_1_1_flat.json");
        }
    }
}
