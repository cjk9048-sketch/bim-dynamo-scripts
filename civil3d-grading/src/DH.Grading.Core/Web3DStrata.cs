namespace DH.Grading.Core;

/// <summary>★★[JACK 1103 «3D 뷰에서 지층을 모델링했다면 자르기 뷰에서 지층도 보이게»] <b>[3D 보기] 지층 격자</b> — 지층면(<c>DH_지층_n_이름</c> = 그 층의 <b>상단</b>) · 지하수위를
/// 자르기 범위(측점 가로선 둘레) 격자 마디마다 읽어 장면에 싣는다. 뷰어(<c>web3d/src/strata.js</c>)가 자르는 면 위에서 층마다 «상단 ~ 다음 층 상단»을 칠하고 최종 지표면 아래로 자른다.
/// <para>격자 = <see cref="Pick"/>(<see cref="Cell0"/> m부터 · 마디가 <see cref="MaxNodes"/>를 넘으면 1.25배씩 키움) · 마디 차례 <c>j·(w+1) + i</c>(시공 격자와 같음) · 못 읽은 마디 = NaN.
/// 지층면 자체가 5 m 안팎 격자(<c>StrataDraw.BuildSurfaces</c> — 칸 5 m 이하)라 2 m면 모양이 다 담긴다 · 1 m는 장면만 네 배(1103 시험 도면 실측: 1 m = gzip +0.99 MB).
/// 최종 지표면과의 자름은 뷰어가 지형 세모 선으로 한다(이 격자 굵기와 무관).</para>
/// <para>층 차례는 위에서 아래 — 아래 층 상단이 위 층 상단보다 높으면 위 층 값으로 누른다(지층 모델이 이미 누른 값이라 보통 0 · 다른 세모 나누기에서 생기는 mm 차만).</para>
/// <para>지층이 없으면 부르는 쪽이 머리에 열쇠를 안 넣는다(옛 장면 바이트 그대로).</para></summary>
public static class Web3DStrata
{
    /// <summary>처음 칸(m).</summary>
    public const double Cell0 = 2.0;

    /// <summary>층 하나 마디 한도(float 240 KB).</summary>
    public const int MaxNodes = 60_000;

    /// <summary>격자 — 원점 뺀 왼쪽 아래 · 칸 · 칸 수.</summary>
    public readonly record struct GridSpec(double X0, double Y0, double Cell, int W, int H)
    {
        public int Nodes => (W + 1) * (H + 1);
    }

    /// <summary>층 하나 — 이름(화면) · 차례(이름의 번호) · 색(2D 횡단 · 종단과 같은 색) · 높이(도면 좌표 · 없으면 null).</summary>
    public sealed record Layer(string Name, int Ord, byte R, byte G, byte B, Func<double, double, double?> Z);

    /// <summary>범위(도면 좌표) → 격자(<see cref="Cell0"/> m부터 · 마디 ≤ <paramref name="maxNodes"/>) · 빈 범위면 null.</summary>
    public static GridSpec? Pick(double x0, double y0, double x1, double y1, int maxNodes = MaxNodes)
    {
        if (!(double.IsFinite(x0) && double.IsFinite(y0) && double.IsFinite(x1) && double.IsFinite(y1)) || x1 <= x0 || y1 <= y0) return null;
        double c = Cell0;
        int W() => (int)Math.Ceiling((x1 - x0) / c);
        int H() => (int)Math.Ceiling((y1 - y0) / c);
        while ((long)(W() + 1) * (H() + 1) > maxNodes) c *= 1.25;
        return new GridSpec(x0, y0, c, W(), H());
    }

    /// <summary>마디마다 높이(float · 없으면 NaN).</summary>
    public static float[] Sample(GridSpec g, Func<double, double, double?> z, out int ok)
    {
        var a = new float[g.Nodes];
        ok = 0;
        for (int j = 0; j <= g.H; j++)
            for (int i = 0; i <= g.W; i++)
            {
                var v = z(g.X0 + i * g.Cell, g.Y0 + j * g.Cell);
                bool f = v.HasValue && double.IsFinite(v.Value);
                a[j * (g.W + 1) + i] = f ? (float)v!.Value : float.NaN;
                if (f) ok++;
            }
        return a;
    }

    /// <summary>머리 «strata» — 층(차례대로 · 높이 하나도 없는 층은 뺌) · 지하수위(있으면). 남는 층이 없으면 null(열쇠를 안 넣는다).
    /// <para>꼴: <c>{x0, y0, cell, w, h, layers: [{name, ord, color: [r,g,b], z: f32}], water?: {name, z: f32}}</c> — x0 · y0는 원점 뺌.</para></summary>
    public static Dictionary<string, object>? Head(Web3DScene sc, GridSpec g, IReadOnlyList<Layer> layers, Func<double, double, double?>? water, double ox, double oy, out string text)
    {
        var keep = new List<(Layer L, float[] Z, int Ok)>();
        int drop = 0;
        foreach (var l in layers.OrderBy(l => l.Ord))
        {
            var z = Sample(g, l.Z, out int ok);
            if (ok == 0) { drop++; continue; }
            keep.Add((l, z, ok));
        }
        if (keep.Count == 0) { text = $"지층 {layers.Count}층 — 범위 안 높이 없음(안 실음)"; return null; }
        // 위에서 아래 — 아래 층 상단이 위 층보다 높으면 누른다
        int pressed = 0; double worst = 0;
        for (int k = 1; k < keep.Count; k++)
        {
            var up = keep[k - 1].Z; var me = keep[k].Z;
            for (int i = 0; i < me.Length; i++)
                if (float.IsFinite(up[i]) && float.IsFinite(me[i]) && me[i] > up[i]) { worst = Math.Max(worst, me[i] - up[i]); me[i] = up[i]; pressed++; }
        }
        var h = new Dictionary<string, object>
        {
            ["x0"] = Web3DScene.Num(g.X0 - ox, 3), ["y0"] = Web3DScene.Num(g.Y0 - oy, 3), ["cell"] = Web3DScene.Num(g.Cell, 4), ["w"] = g.W, ["h"] = g.H,
            ["layers"] = keep.Select(k => (object)new Dictionary<string, object>
            {
                ["name"] = k.L.Name, ["ord"] = k.L.Ord, ["color"] = new[] { (int)k.L.R, k.L.G, k.L.B }, ["z"] = sc.F32(k.Z),
            }).ToList(),
        };
        int wOk = 0;
        if (water != null)
        {
            var wz = Sample(g, water, out wOk);
            if (wOk > 0) h["water"] = new Dictionary<string, object> { ["name"] = "지하수위", ["z"] = sc.F32(wz) };
        }
        text = $"격자 {g.W}×{g.H} · 칸 {g.Cell:0.##} m · 층 {keep.Count}({string.Join(" · ", keep.Select(k => $"{k.L.Name} {k.Ok:N0}"))})"
             + (drop > 0 ? $" · 높이 없어 뺀 층 {drop}" : "") + (water != null ? $" · 지하수위 {wOk:N0}" : "")
             + (pressed > 0 ? $" · 누른 마디 {pressed:N0}(최대 {worst * 1000:F1} mm)" : "");
        return h;
    }
}
