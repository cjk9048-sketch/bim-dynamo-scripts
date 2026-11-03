namespace DH.Grading.Core;

/// <summary>★★[도로 마무리 ② · 계획_도로마무리.md 6.2 · 계획_측구.md · JACK 1102 «대표단면 하나 + 치수별로 조정»] 측구 단면 꼴 — 형식 · 치수(m) → 콘크리트 다각형 · 경계석 · 윗폭 · 넓이.
/// <para>좌표 = (옵셋 O, 높이 Z) · 길어깨 끝 윗면 = (0, 0) · 바깥(사면 쪽) = +O. 측구 윗면은 길어깨 끝 높이(지표면은 파지 않음 — 계획 1장).</para>
/// <para>대표값 = JACK 도면(참고자료\측구표준도\측구_치수표.md): U형 «24_U형측구 상세도» 300×300 · 벽 · 바닥 150 · 기초 100(양쪽 100 내밈) · 뚜껑 자리 50×50 /
/// L형 «보차도경계석 및 L형측구상세도»(2019) 배수면 500 · 6 % → 경계석 200×250(보임 200) → 뒤 50(턱 50) · 경계석 밑 140 · 바깥 끝 두께 220 · 콘크리트 0.140 ㎡/m.</para></summary>
public static class DitchSection
{
    public enum Kind { None = 0, U = 1, L = 2 }

    /// <summary>U형(현장타설) — 안폭 B · 안높이 H(벽 윗면 ~ 안바닥) · 벽 Tw · 바닥 Tb · 기초 두께 Tf · 기초 내민 폭 Pf · 뚜껑 자리 폭 Sw · 깊이 Sd · 뚜껑(그레이팅) 있음.</summary>
    public sealed record UDims(double B = 0.30, double H = 0.30, double Tw = 0.15, double Tb = 0.15, double Tf = 0.10, double Pf = 0.10, double Sw = 0.05, double Sd = 0.05, bool Lid = true);

    /// <summary>L형(경계석 + 측구) — 배수면 폭 Wg · 경사 Sg(경계석 쪽으로 낮아짐) · 경계석 폭 Wc · 높이 Hc · 보이는 높이 Reveal · 경계석 밑 Tb · 뒤 폭 Wb · 뒤 턱 높이 Hb · 바깥 끝 두께 Te(확인용).</summary>
    public sealed record LDims(double Wg = 0.50, double Sg = 0.06, double Wc = 0.20, double Hc = 0.25, double Reveal = 0.20, double Tb = 0.14, double Wb = 0.05, double Hb = 0.05, double Te = 0.22);

    /// <summary>꼴 — 콘크리트 다각형들(기초 따로) · 경계석(L형) · 물길/그레이팅 자리(U형 안폭 · O 범위) · 윗폭 · 콘크리트 넓이 · 이름 글자 · 경고.</summary>
    public sealed record Shape(Kind Kind, List<List<(double O, double Z)>> Concrete, List<(double O, double Z)>? Curb, (double O0, double O1)? Channel, double TopWidth, double Area, double Depth, string Label, string Warn);

    static double Area(IReadOnlyList<(double O, double Z)> p)
    {
        double a = 0;
        for (int i = 0; i < p.Count; i++) { var q = p[i]; var r = p[(i + 1) % p.Count]; a += q.O * r.Z - r.O * q.Z; }
        return Math.Abs(a) / 2;
    }

    /// <summary>치수 검사 — 0 · 음수 · 맞지 않는 값(오류 글 · 빈 문자열 = 맞음).</summary>
    public static string Check(UDims d)
    {
        if (!(d.B > 0 && d.H > 0 && d.Tw > 0 && d.Tb > 0)) return "안폭 · 안높이 · 벽 · 바닥 두께는 0보다 커야 합니다";
        if (d.Tf < 0 || d.Pf < 0 || d.Sw < 0 || d.Sd < 0) return "기초 · 뚜껑 자리 값은 0 이상이어야 합니다";
        if (d.Sw >= d.Tw || d.Sd >= d.H) return "뚜껑 자리가 벽 두께 · 안높이보다 큽니다";
        return "";
    }
    public static string Check(LDims d)
    {
        if (!(d.Wg > 0 && d.Wc > 0 && d.Hc > 0 && d.Tb > 0)) return "배수면 폭 · 경계석 폭 · 높이 · 밑 콘크리트는 0보다 커야 합니다";
        if (d.Wb < 0 || d.Hb < 0 || d.Sg < 0 || d.Sg > 0.3 || d.Reveal <= 0 || d.Reveal >= d.Hc) return "뒤 폭 · 턱 · 경사(0~30 %) · 보이는 높이(경계석 높이보다 작게)를 확인하세요";
        return "";
    }

    public static Shape U(UDims d)
    {
        double W = d.B + 2 * d.Tw, bot = -(d.H + d.Tb);
        var body = new List<(double, double)> { (0, 0), (0, bot), (W, bot), (W, 0) };
        // 오른 벽 위 뚜껑 자리 → 안 홈 → 왼 벽 위 뚜껑 자리
        if (d.Sw > 0 && d.Sd > 0) { body.Add((d.Tw + d.B + d.Sw, 0)); body.Add((d.Tw + d.B + d.Sw, -d.Sd)); body.Add((d.Tw + d.B, -d.Sd)); }
        else body.Add((d.Tw + d.B, 0));
        body.Add((d.Tw + d.B, -d.H)); body.Add((d.Tw, -d.H));
        if (d.Sw > 0 && d.Sd > 0) { body.Add((d.Tw, -d.Sd)); body.Add((d.Tw - d.Sw, -d.Sd)); body.Add((d.Tw - d.Sw, 0)); }
        else body.Add((d.Tw, 0));
        var polys = new List<List<(double O, double Z)>> { body };
        if (d.Tf > 0) polys.Add(new() { (-d.Pf, bot), (-d.Pf, bot - d.Tf), (W + d.Pf, bot - d.Tf), (W + d.Pf, bot) });
        double area = polys.Sum(Area);
        return new Shape(Kind.U, polys, null, (d.Tw, d.Tw + d.B), W, area, d.H + d.Tb + d.Tf,
                         $"U형 측구 B{d.B * 1000:0}×H{d.H * 1000:0}" + (d.Lid ? " (그레이팅)" : ""), Check(d));
    }

    public static Shape L(LDims d)
    {
        double gEnd = -d.Sg * d.Wg;                       // 경계석 앞 배수면 높이
        double cTop = gEnd + d.Reveal, cBot = cTop - d.Hc, bot = cBot - d.Tb;
        double o1 = d.Wg, o2 = d.Wg + d.Wc, o3 = o2 + d.Wb;
        var conc = new List<(double O, double Z)> { (0, 0), (o1, gEnd), (o1, cBot), (o2, cBot) };
        if (d.Wb > 0) { conc.Add((o2, cBot + d.Hb)); conc.Add((o3, cBot + d.Hb)); conc.Add((o3, bot)); }
        else conc.Add((o2, bot));
        conc.Add((0, bot));
        var curb = new List<(double O, double Z)> { (o1, cBot), (o2, cBot), (o2, cTop), (o1, cTop) };
        double te = -bot;
        string warn = Check(d);
        if (warn.Length == 0 && Math.Abs(te - d.Te) > 1e-3) warn = $"바깥 끝 두께 {d.Te * 1000:0} mm ≠ 셈 {te * 1000:0} mm(밑 + 턱 + 경사 × 폭 · 경계석 높이 − 보이는 높이)";
        return new Shape(Kind.L, new() { conc }, curb, null, o3, Area(conc), te, $"L형 측구 W{o3 * 1000:0}", warn);
    }

    /// <summary>형식 → 꼴(없음 = null).</summary>
    public static Shape? Of(Kind k, UDims? u, LDims? l) => k switch { Kind.U => U(u ?? new()), Kind.L => L(l ?? new()), _ => null };
}
