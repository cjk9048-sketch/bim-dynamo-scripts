using System.Globalization;

namespace DH.Grading.Core;

/// <summary>★★[v113.1 · 계획_종단방향_축척.md 2 · JACK 1104 «종단 축척 H · V 따로 · [자동]»] 종단도 가로(H) · 세로(V) 축척 고르기 — 화면 없는 시험(하니스)이 본다.
/// <para>H = 가로(측점) 축척 · 도면 주석 축척 · 장 나누기 · V = 세로(표고) 축척. Civil 종단뷰는 수직과장 VE = H ÷ V로 그린다(뷰 스타일).</para>
/// <para>칸 값: H — 0 = 자동 · &gt; 0 = 고정. V — <see cref="SameAsH"/>(−1) = H와 같게(수직과장 없음) · 0 = 자동 · &gt; 0 = 고정.</para></summary>
public static class ProfileScaleFit
{
    /// <summary>V 칸 «H와 같게(수직과장 없음)».</summary>
    public const double SameAsH = -1;
    /// <summary>★[JACK 1104 Q3 «여러 장으로 나눔»] 자동 H는 1:1000보다 작게(숫자가 크게) 고르지 않는다 — 한 장에 안 들어가면 장을 나눈다.</summary>
    public const double AutoHMax = 1000;
    /// <summary>★[JACK 1104 Q2] 자동 V의 수직과장 범위 1 ~ 10배(세로를 줄여 그리지 않음).</summary>
    public const double VeMax = 10;
    const double Tol = 1e-9;

    /// <summary>고른 결과 — 장 수 · 넘침(가로 · 세로) · 채움(그래프 자리 중 그림이 차지하는 넓이 비) · 한 줄.</summary>
    public readonly record struct Result(double H, double V, int Sheets, bool OverW, bool OverV, double Fill, string Note)
    {
        public double Exaggeration => V > 0 ? H / V : 1;
    }

    /// <summary>고르기.
    /// <para><paramref name="needW"/> = 노선 전체가 한 장에 들어가는 가장 큰 축척(= 측점 범위 × 1000 ÷ 쓸 수 있는 폭 mm) ·
    /// <paramref name="needV"/> = 표고 격자 범위가 그래프 자리에 들어가는 가장 큰 세로 축척(= 범위 × 1000 ÷ 자리 높이 mm).</para>
    /// <para>후보 = 사다리 H × 사다리 V(고정된 쪽은 그 값 하나 · «H와 같게» = V = H). 자동 V는 1 ≤ H/V ≤ <paramref name="veMax"/>.</para>
    /// <list type="number">
    /// <item>H 고정: 장 나누기(<paramref name="sheetsFor"/> — 없으면 한 장 · 그 폭 검사). V만 고름.</item>
    /// <item>H 자동: 한 장에 들어가는 H ≤ <paramref name="hMax"/>가 있으면 그중 · 없으면 H ≤ <paramref name="hMax"/>로 장 나누기(<paramref name="sheetsFor"/>가 있을 때).</item>
    /// <item>고르기 = 장 수 가장 적은 것 → 채움 F = (가로 채움 = needW ÷ (장 수 × H)) × (세로 채움 = needV ÷ V) 가장 큰 것 → H 숫자 작은 것(크게) → 과장이 1에 가까운 것.
    /// 나눌 때도 장 수가 먼저다(작은 축척 여러 장보다 1:1000 두 장) — 같은 장 수 안에서 가장 크게 그리는 H.</item>
    /// <item>들어가는 짝이 없으면 가장 덜 넘치는 짝(넘침 비 최댓값이 가장 작은 것)을 넘침 표시와 함께.</item>
    /// </list>
    /// <para>옛 규칙(«H와 같게» · <paramref name="hMax"/> = ∞ · 장 나누기 없음) = <c>첫 사다리 ≥ max(needW, needV)</c> — 지금 식과 같은 답.</para></summary>
    public static Result Pick(double needW, double needV, IReadOnlyList<double> ladder, double pinH, double pinV,
                              double hMax = double.PositiveInfinity, Func<double, int>? sheetsFor = null, double veMax = VeMax)
    {
        var lad = (ladder ?? Array.Empty<double>()).Where(v => double.IsFinite(v) && v > 0).Distinct().OrderBy(v => v).ToList();
        if (lad.Count == 0) lad.Add(1000);
        double nW = double.IsFinite(needW) && needW > 0 ? needW : 0;
        double nV = double.IsFinite(needV) && needV > 0 ? needV : double.IsPositiveInfinity(needV) ? double.PositiveInfinity : 0;
        bool fixH = double.IsFinite(pinH) && pinH > 0, fixV = double.IsFinite(pinV) && pinV > 0, same = !fixV && pinV < 0;
        double hm = double.IsNaN(hMax) || hMax <= 0 ? double.PositiveInfinity : hMax;
        var hs = fixH ? new List<double> { pinH } : lad;

        var cands = new List<(double H, double V, int N, double WRatio, double VRatio, bool Split)>();
        foreach (double h in hs)
        {
            IEnumerable<double> vs = fixV ? new[] { pinV } : same ? new[] { h } : lad.Where(v => h / v >= 1 - Tol && h / v <= veMax + Tol);
            int n; bool split = false;
            if (fixH) n = Math.Max(1, sheetsFor?.Invoke(h) ?? 1);
            else if (h >= nW - Tol * Math.Max(1, nW)) n = 1;
            else if (sheetsFor != null && h <= hm + Tol) { n = Math.Max(1, sheetsFor(h)); split = true; }
            else n = 1;
            double wr = nW / (n * h);                 // 가로 채움(1 넘으면 넘침)
            foreach (double v in vs) cands.Add((h, v, n, wr, nV / v, split));
        }
        bool Fits((double H, double V, int N, double WRatio, double VRatio, bool Split) c) => c.WRatio <= 1 + Tol && c.VRatio <= 1 + Tol;
        // H 자동: 한 장 · H ≤ 한도 → 나눔(한도 안) 차례
        var pool = cands.Where(c => Fits(c) && (fixH || (c.N == 1 && c.H <= hm + Tol))).ToList();
        if (pool.Count == 0 && !fixH) pool = cands.Where(c => Fits(c) && c.Split).ToList();
        if (pool.Count == 0 && !fixH && double.IsPositiveInfinity(hm)) pool = cands.Where(Fits).ToList();
        bool over = pool.Count == 0;
        IEnumerable<(double H, double V, int N, double WRatio, double VRatio, bool Split)> ordered;
        if (!over)
            ordered = pool.OrderBy(c => c.N).ThenByDescending(c => Math.Round(c.WRatio * c.VRatio, 12)).ThenBy(c => c.H).ThenBy(c => Math.Abs(Math.Log(c.H / c.V)));
        else
        {
            // 가장 덜 넘침 — H 자동이면 한도 안 후보부터(나눔 포함)
            var src = cands.Where(c => fixH || c.H <= hm + Tol).ToList();
            if (src.Count == 0) src = cands;
            ordered = src.OrderBy(c => Math.Round(Math.Max(c.WRatio, c.VRatio), 12)).ThenByDescending(c => Math.Round(c.WRatio * c.VRatio, 12)).ThenBy(c => c.N).ThenBy(c => c.H);
        }
        var b = ordered.First();
        bool ow = b.WRatio > 1 + Tol, ov = b.VRatio > 1 + Tol;
        string note = $"H=1:{N(b.H)} · V=1:{N(b.V)}" + (Math.Abs(b.H - b.V) > 1e-9 ? $"(과장 {b.H / b.V:0.##}배)" : "(과장 없음)")
                    + (b.N > 1 ? $" · {b.N}장" : "") + $" · 채움 {b.WRatio * b.VRatio * 100:0}%"
                    + (fixH ? " · H 고정" : "") + (fixV ? " · V 고정" : same ? " · V = H" : "")
                    + (ow ? " ⚠가로 넘침" : "") + (ov ? " ⚠세로 넘침" : "");
        return new Result(b.H, b.V, b.N, ow, ov, b.WRatio * b.VRatio, note);
    }

    static string N(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);

    /// <summary>도면 글 — 같으면 «1:200» · 다르면 «H=1:500, V=1:100».</summary>
    public static string ScaleText(double h, double v)
        => !(v > 0) || Math.Abs(v - h) <= 1e-6 ? $"1:{N(h)}" : $"H=1:{N(h)}, V=1:{N(v)}";

    /// <summary>칸 값 확인 — H: 0 · 사다리 값 · V: −1 · 0 · 사다리 값. 아니면 <paramref name="keep"/>.</summary>
    public static double CleanH(double v, IReadOnlyList<double>? ladder, double keep)
        => double.IsFinite(v) && (v == 0 || (ladder?.Any(q => Math.Abs(q - v) < 1e-9) ?? v > 0)) ? v : keep;
    public static double CleanV(double v, IReadOnlyList<double>? ladder, double keep)
        => double.IsFinite(v) && (v == SameAsH || v == 0 || (ladder?.Any(q => Math.Abs(q - v) < 1e-9) ?? v > 0)) ? v : keep;

    /// <summary>칸 열쇠(종단도를 그린 때 설정 — 지금과 다르면 «축척이 바뀌었습니다»).</summary>
    public static string Key(double h, double v) => $"H{N(h)}|V{N(v)}";

    // ══════════════ 복사본 뷰 스타일(수직과장 · 주석 비율) ══════════════

    /// <summary>복사본 이름 꼬리 — «(DH H1:500 V1:100)» · 주석 축척이 H와 다르면 «@anno»까지.</summary>
    public static string CloneSuffix(double h, double v, double anno, string? tag = null)
        => $"(DH H1:{N(h)} V1:{N(v)}" + (anno > 0 && Math.Abs(anno - h) > 1e-6 ? $"@{N(anno)}" : "") + (string.IsNullOrEmpty(tag) ? "" : " #" + tag) + ")";

    /// <summary>진입도로 한 도로의 복사본인가(이름 꼬리 « #핸들)») — 다시 그릴 때 지우고 새로.</summary>
    public static bool IsRoadClone(string? name, string handle)
        => IsClone(name) && !string.IsNullOrEmpty(handle) && name!.EndsWith(" #" + handle + ")", StringComparison.OrdinalIgnoreCase);

    /// <summary>우리 축척 복사본인가(회사 스타일 고르기 · 템플릿 옮기기에서 뺌).</summary>
    public static bool IsClone(string? name) => name != null && name.Contains("(DH H1:", StringComparison.Ordinal);

    /// <summary>복사본 이름에서 원본 이름(꼬리 뗌).</summary>
    public static string BaseName(string? name)
    {
        string n = name ?? "";
        int i = n.IndexOf("(DH H1:", StringComparison.Ordinal);
        return i >= 0 ? n.Substring(0, i) : n;
    }

    /// <summary>종이 크기(mm)를 스타일 값(종이 m · 주석 축척이 곱해짐)으로 — 그릴 축척 <paramref name="scale"/>이 주석 축척 <paramref name="anno"/>와 다르면 비율.</summary>
    public static double PaperToStyle(double paperMm, double scale, double anno)
        => anno > 1e-9 && scale > 0 ? paperMm / 1000.0 * scale / anno : paperMm / 1000.0;
}
