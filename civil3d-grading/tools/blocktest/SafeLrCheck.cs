using DH.Grading.Core;
using NetTopologySuite.Geometries;

/// <summary>★★[v102.0 · 계획 4판 §4 — 음성 기준을 구현보다 먼저 박는다] S144 다듬기 안전 — 여유 띠 쌍 · 마지막 수단.
/// <para>ⓓ 여유 띠: 네모 102µm 쌍(합치면 1:0.01 면에서 테두리 약 10mm)은 <b>안 합친다</b>(Civil은 네모 &lt; 1e-4만 합친다 — 실측) ·
/// 네모 99µm 쌍은 <b>반드시</b> 합친다.</para>
/// <para>ⓒ 구역 모드(합성): 위험한 바늘을 고치다가 삼각망 <b>바깥 테두리 점</b>(고정 아님)을 옮기거나 없애지 않는다 — 옮기면 정지면 테두리 모양이 바뀐다.</para>
/// <para>켬/끔 대조(ⓐ·ⓑ)와 결정성(ⓔ)은 판을 가진 곳(S142·S143 · S139 ⑯ · S140)에서 <see cref="OnOff"/>로 잰다.</para></summary>
static class SafeLrCheck
{
    const double OX = 210300, OY = 509800;

    /// <summary>새 길(켬 — 여유 띠 · 마지막 수단) / 옛 길(끔 — v101) — 하네스 전용 스위치 한 곳.</summary>
    public static void SetLr(bool on) => CivilSafeMesh.LegacyOnly = !on;
    public static bool LrOn => !CivilSafeMesh.LegacyOnly;

    /// <summary>BLOCKTEST_SAFE_DUMP=폴더면 이 판의 LandXML을 «lx_이름.xml»로 남긴다 — 설치 전 화면 없는 Civil에 그대로 넣는다(계획 4판 §5).</summary>
    public static void Dump(List<WallDaylight.Tri> safe, string name)
    {
        string? dumpDir = Environment.GetEnvironmentVariable("BLOCKTEST_SAFE_DUMP");
        if (string.IsNullOrEmpty(dumpDir) || !Directory.Exists(dumpDir)) return;
        string xf = Path.Combine(dumpDir, "lx_" + string.Concat(name.Select(ch => char.IsLetterOrDigit(ch) ? ch : '_')) + ".xml");
        try { WallDaylight.WriteLandXmlTin(xf, "PUREWALL", safe, 1e-7, out _, out _, out _); } catch { }
    }

    public static void Run(Action<string, bool, string> check)
    {
        Console.WriteLine("\n== S144 다듬기 안전 — 여유 띠 쌍 · 구역 모드 바깥 테두리 ==");
        Margin(check);
        ZoneHull(check);
    }

    // ───────────── ⓓ 여유 띠 ─────────────
    /// <summary>1:0.01 앞면(x로 0.05m에 5m) 위 ㄴ자 띠. 모서리 R1(0.02, 0.5)과 R2(0.02+d, 0.5+d) — 네모 거리 d.
    /// 안쪽 점 S가 R1R2 맞은편에 있어 그 면의 가장 낮은 높이는 ~144µm(납작하지 않다) — 여유 띠 규칙만 겨눈다.</summary>
    /// <param name="sx">안쪽 점 S — 기본(0.034, 0.486)이면 R1R2 면 높이 ~144µm · (0.0396, 0.49724)이면 ~115µm(0929에 Civil이 뒤집은 «가는 바늘» 꼴 — 검토 r4 중간 3).</param>
    static (List<Point3> Ring, List<WallDaylight.Tri> Tris, List<WallDaylight.Tri> Wall) MarginBoard(double d, double sx = 0.034, double sy = 0.486)
    {
        static double Zp(double x) => 100 - 100 * x;
        static Point3 P(double x, double y) => new(OX + x, OY + y, Zp(x));
        var V0 = P(0, 0); var V1 = P(0.05, 0); var V2 = P(0.05, 1); var V3 = P(0.02 + d, 1);
        var R2 = P(0.02 + d, 0.5 + d); var R1 = P(0.02, 0.5); var V6 = P(0, 0.5); var S = P(sx, sy);
        var ring = new List<Point3> { V0, V1, V2, V3, R2, R1, V6 };
        var tris = new List<WallDaylight.Tri> { new(V0, V1, S), new(V1, V2, S), new(V2, V3, S), new(V3, R2, S), new(R2, R1, S), new(R1, V6, S), new(V6, V0, S) };
        var wall = new List<WallDaylight.Tri> { new(P(-0.01, -0.1), P(0.06, -0.1), P(0.06, 1.1)), new(P(-0.01, -0.1), P(0.06, 1.1), P(-0.01, 1.1)) };
        return (ring, tris, wall);
    }

    static void Margin(Action<string, bool, string> check)
    {
        {
            var (ring, tris, wall) = MarginBoard(102e-6);
            var safe = CivilSafeMesh.Make(tris, ring, new List<List<Point3>>(), wall, out var rep);
            var m = safe == null ? null : SafeCheck.Measure(safe, ring, new List<List<Point3>>(), wall);
            if (safe != null) Dump(safe, "S144_margin102");
            check("S144 ⓓ 여유 띠 — 네모 102µm 쌍(합치면 테두리 약 10mm)은 안 합친다 · 면 7 · 점 8 그대로 · 판정 1 · 남은 점 네모 ≥ 101µm · Civil 합치기 흉내 0",
                  safe != null && rep.Tier == 1 && rep.PtsOut == 8 && rep.FacesOut == 7 && m != null && m.MinBox >= CivilSafeMesh.MustMerge && m.CivilMerges == 0 && SafeCheck.Tier1(m),
                  rep.Summary + (m != null ? " · " + m.Text : ""));
        }
        {
            var (ring, tris, wall) = MarginBoard(99e-6);
            var safe = CivilSafeMesh.Make(tris, ring, new List<List<Point3>>(), wall, out var rep);
            var m = safe == null ? null : SafeCheck.Measure(safe, ring, new List<List<Point3>>(), wall);
            if (safe != null) Dump(safe, "S144_margin99");
            check("S144 ⓓ 여유 띠 — 네모 99µm 쌍은 반드시 합친다(Civil이 합친다) · 점 7 · 지어진다(판정 ≤ 2) · Civil 합치기 흉내 0",
                  rep.PtsOut == 7 && safe != null && m != null && m.CivilMerges == 0 && m.MinBox >= CivilSafeMesh.MustMerge,
                  rep.Summary + (m != null ? " · " + m.Text : ""));
        }
        {
            // ★[검토 r4 중간 3] 여유 띠 쌍을 두면 그 곁에 0929 꼴 «가는 바늘»(높이 ~115µm · 짧은 변 144µm)이 남을 수 있다 —
            //   판정엔 안 넣지만 보고가 <b>세어야</b> 한다(설치 전 화면 없는 Civil 시험이 이 꼴을 가른다)
            var (ring, tris, wall) = MarginBoard(102e-6, 0.0396, 0.49724);
            var safe = CivilSafeMesh.Make(tris, ring, new List<List<Point3>>(), wall, out var rep);
            if (safe != null) Dump(safe, "S144_margin102_thin");
            check("S144 ⓓ 여유 띠 — 둔 쌍 곁 가는 바늘(높이 ~115µm)을 보고가 센다(가는 바늘 ≥ 1 · 짧은 변 ≥ 1) · 쌍은 그대로",
                  safe != null && rep.PtsOut == 8 && rep.MarginKept == 1 && rep.ThinLeft >= 1 && rep.ShortLeft >= 1,
                  rep.Summary);
        }
    }

    // ───────────── ⓒ 구역 모드 바깥 테두리 ─────────────
    /// <summary>1m 네모(고정 네 모서리) · 바닥 변 가운데 테두리 점 H(고정 아님) · H 위 80µm에 N1·N2 — 바늘 (N1, H, N2)는 긴 변이 안쪽이고 들로네가 아니다(Civil이 고칠 면).
    /// H를 1mm 낮춰 먼저 뒤집기(0.2mm)가 못 하게 한다 → 마지막 수단이 고친다. H를 옮기면 바닥 변이 휘어 넓이가 준다.</summary>
    static void ZoneHull(Action<string, bool, string> check)
    {
        static Point3 P(double x, double y, double z) => new(OX + x, OY + y, z);
        var C0 = P(0, 0, 50); var C1 = P(1, 0, 50); var C2 = P(1, 1, 50); var C3 = P(0, 1, 50);
        var H = P(0.5, 0, 49.999); var N1 = P(0.3, 8e-5, 50); var N2 = P(0.7, 8e-5, 50); var X = P(0.5, 0.5, 50);
        var tris = new List<WallDaylight.Tri>
        {
            new(C0, H, N1), new(H, C1, N2), new(N1, H, N2), new(N1, N2, X), new(C0, N1, C3), new(N1, X, C3), new(X, C2, C3), new(N2, C2, X), new(N2, C1, C2),
        };
        var fixedPts = new[] { C0, C1, C2, C3 };
        foreach (bool on in new[] { false, true })
        {
            bool was = LrOn; SetLr(on);
            List<WallDaylight.Tri>? z;
            CivilSafeMesh.Report rep;
            try { z = CivilSafeMesh.MakeZone(tris, fixedPts, out rep); }
            finally { SetLr(was); }
            double area = z == null ? 0 : z.Sum(t => Math.Abs((t.B.X - t.A.X) * (t.C.Y - t.A.Y) - (t.B.Y - t.A.Y) * (t.C.X - t.A.X)) / 2);
            bool hKept = z != null && z.Any(t => new[] { t.A, t.B, t.C }.Any(q => q.X == H.X && q.Y == H.Y && q.Z == H.Z));
            if (!on)
                Console.WriteLine($"      S144 ⓒ(마지막 수단 끔) 판정 {rep.Tier} · Civil이 고칠 면 {rep.CivilRisk} · H 남음 {hKept} · 넓이 {area:F12}");
            else
                check("S144 ⓒ 구역 모드 — 위험한 바늘을 고치되 바깥 테두리 점 H는 그대로(자리·높이 비트) · 넓이 1㎡ 그대로 · Civil이 고칠 면 0 · 판정 ≤ 2",
                      z != null && hKept && Math.Abs(area - 1.0) <= 1e-9 && rep.CivilRisk == 0 && rep.Tier <= 2,
                      $"판정 {rep.Tier} · H 남음 {hKept} · 넓이 {area:F12} · {rep.Summary}");
        }
    }

    // ───────────── ⓐ·ⓑ·ⓔ 켬/끔 대조 · 결정성 ─────────────
    /// <summary>한 판을 마지막 수단 <b>끔 · 켬 · 켬(두 번째)</b>으로 돌려 견준다.
    /// ⓐ 켬 판정 ≤ 끔 판정 ⓑ 끔이 판정 1이면 켬은 마지막 수단 0이고 결과가 비트 그대로 ⓔ 켬 두 번이 비트 그대로.</summary>
    /// <param name="run">판정 · 결과 삼각형 · 마지막 수단 수 · 되돌림(새 길이 옛 길보다 나빠 옛 길로 지음 + 마지막 수단 되돌림) — ★[코드 검토 낮음 2]
    /// Run()이 두 길 중 나은 쪽을 고르므로 «판정 ≤ 옛 길»만으로는 늘 참이다 — 새 길이 <b>스스로</b> 옛 길 이상인지(되돌림 0)를 따로 본다.</param>
    public static void OnOff(Action<string, bool, string> check, string tag, Func<(int Tier, List<WallDaylight.Tri>? Tris, int Lr, int Fallback)> run)
    {
        bool was = LrOn;
        (int Tier, List<WallDaylight.Tri>? Tris, int Lr, int Fallback) off, on1, on2;
        try { SetLr(false); off = run(); SetLr(true); on1 = run(); on2 = run(); }
        finally { SetLr(was); }
        static bool Same(List<WallDaylight.Tri>? a, List<WallDaylight.Tri>? b)
        {
            if (a == null || b == null) return a == null && b == null;
            if (a.Count != b.Count) return false;
            static bool P(Point3 p, Point3 q) => BitConverter.DoubleToInt64Bits(p.X) == BitConverter.DoubleToInt64Bits(q.X)
                && BitConverter.DoubleToInt64Bits(p.Y) == BitConverter.DoubleToInt64Bits(q.Y) && BitConverter.DoubleToInt64Bits(p.Z) == BitConverter.DoubleToInt64Bits(q.Z);
            for (int i = 0; i < a.Count; i++) if (!P(a[i].A, b[i].A) || !P(a[i].B, b[i].B) || !P(a[i].C, b[i].C)) return false;
            return true;
        }
        check($"{tag} 새 길(여유 띠 · 마지막 수단) — ⓐ판정 ≤ 옛 길 · 되돌림 0(새 길이 스스로) · ⓑ옛 길이 판정 1이면 새 길은 마지막 수단 0 · 옛 길과 비트 그대로 · ⓔ두 번 비트 그대로",
              on1.Tier <= off.Tier && on1.Fallback == 0 && (off.Tier != 1 || (on1.Lr == 0 && Same(off.Tris, on1.Tris))) && Same(on1.Tris, on2.Tris) && on1.Tier == on2.Tier,
              $"옛 길 판정 {off.Tier} · 새 길 판정 {on1.Tier}(마지막 수단 {on1.Lr} · 되돌림 {on1.Fallback}) · 새 길 두 번 같음 {Same(on1.Tris, on2.Tris)} · 옛 길=새 길 {Same(off.Tris, on1.Tris)}");
    }
}
