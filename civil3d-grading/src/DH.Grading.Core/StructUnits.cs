using System.Text.RegularExpressions;

namespace DH.Grading.Core;

/// <summary>★★[v115 · JACK 1105 Revit 구조물 · 계획 D4 · D6 · D7 · D8 · D9 · 검토 M2] 구조물 불러오기의 판정들 — 순수 함수(하네스 S233).</summary>
public static class StructUnits
{
    /// <summary>단위 계수 후보(D7) — m · mm · cm · ft.</summary>
    public static readonly double[] Candidates = { 1, 0.001, 0.01, 0.3048 };

    /// <summary>INSUNITS → m 계수(모르면 1).</summary>
    public static double FromInsunits(int u) => u switch
    {
        1 => 0.0254, 2 => 0.3048, 4 => 0.001, 5 => 0.01, 6 => 1.0, 14 => 0.1, _ => 1.0,
    };

    public readonly record struct Box2(double X0, double Y0, double X1, double Y1)
    {
        public double Area => Math.Max(0, X1 - X0) * Math.Max(0, Y1 - Y0);
        public Box2 Scale(double f) => new(X0 * f, Y0 * f, X1 * f, Y1 * f);
        public bool IsEmpty => !(X1 >= X0 && Y1 >= Y0);
    }

    static double Inter(Box2 a, Box2 b) => Math.Max(0, Math.Min(a.X1, b.X1) - Math.Max(a.X0, b.X0)) * Math.Max(0, Math.Min(a.Y1, b.Y1) - Math.Max(a.Y0, b.Y0));

    /// <summary>XY 상자 겹침 비율(교집합 ÷ 합집합) — D9 «같은 구조물?».</summary>
    public static double IoU(Box2 a, Box2 b)
    {
        double i = Inter(a, b), u = a.Area + b.Area - i;
        return u > 0 ? i / u : 0;
    }

    /// <summary>원지반과 겹치는가 — 구조물 상자의 절반 이상이 원지반 상자 안(납작한 상자는 가운데 점).</summary>
    public static bool Overlaps(Box2 s, Box2 g)
    {
        if (s.Area < 1e-6) { double cx = (s.X0 + s.X1) / 2, cy = (s.Y0 + s.Y1) / 2; return cx >= g.X0 && cx <= g.X1 && cy >= g.Y0 && cy <= g.Y1; }
        return Inter(s, g) >= 0.5 * s.Area;
    }

    /// <summary>★[D7] 단위 계수 — INSUNITS 값부터 · 안 맞으면 후보를 차례로 원지반 겹침으로 검산. 다 안 맞으면 NaN(거절) · 원지반이 없으면 INSUNITS 값 그대로 + ⚠.
    /// <paramref name="raw"/> = 원본 좌표 그대로의 XY 상자(원점 삽입 · 축척 1).</summary>
    public static double PickFactor(int insunits, Box2 raw, Box2? ground, out string note)
    {
        double f0 = FromInsunits(insunits);
        if (ground == null || ground.Value.IsEmpty) { note = $"⚠원지반이 없어 단위를 검산 못 함(INSUNITS {insunits} → ×{f0:0.####})"; return f0; }
        var order = new List<double> { f0 };
        foreach (double c in Candidates) if (!order.Any(o => Math.Abs(o - c) < 1e-12)) order.Add(c);
        foreach (double f in order)
            if (Overlaps(raw.Scale(f), ground.Value))
            {
                note = Math.Abs(f - f0) < 1e-12 ? "" : $"단위: INSUNITS {insunits}(×{f0:0.####})가 원지반과 안 겹쳐 ×{f:0.####}로 읽음";
                return f;
            }
        double cxr = (raw.X0 + raw.X1) / 2, cyr = (raw.Y0 + raw.Y1) / 2;
        bool nearOrigin = Math.Abs(cxr * f0) < 5000 && Math.Abs(cyr * f0) < 5000;   // INSUNITS로 읽어 원점 5 km 안
        note = nearOrigin ? "구조물이 원점 근처에 있습니다 — Revit에서 «공유» 좌표로 내보내세요"
                          : "구조물이 원지반과 겹치지 않습니다(단위 m · mm · cm · ft 모두) — 좌표계를 확인하세요";
        return double.NaN;
    }

    /// <summary>★[D8] 구조물 윗면과 외곽 아래 원지반 차가 10 m를 넘으면 ⚠(막지 않음).</summary>
    public const double HeightWarnM = 10.0;
    public static bool HeightOff(double top, double ground) => double.IsFinite(top) && double.IsFinite(ground) && Math.Abs(top - ground) > HeightWarnM;

    static readonly Regex Tail = new(@"^(.*?)\s*-\s*3D\s*(뷰|View)\b.*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>★[D9] 파일 이름 → 구조물 이름(Revit 꼬리 «-3D 뷰-…» · «- 3D View - …» 떼기).</summary>
    public static string NameFromFile(string path)
    {
        string n = Path.GetFileNameWithoutExtension(path ?? "") ?? "";
        var m = Tail.Match(n);
        if (m.Success && m.Groups[1].Value.Trim().Length > 0) n = m.Groups[1].Value;
        n = n.Trim().Trim('-', '_', ' ');
        return n.Length > 0 ? n : "구조물";
    }

    /// <summary>★[검토 M2] 해치 안 칠 이름(레이어 · 재료) — 계단 · 난간 · 문 · 창 · 설비. 나머지는 다 칠한다(Revit 기본 A-WALL · A-FLOR · S-FNDN · S-COLS · S-BEAM …).</summary>
    static readonly string[] NoHatchKeys = { "STRS", "HRAL", "RAIL", "DOOR", "GLAZ", "WIND", "EQPM", "계단", "난간", "문", "창" };
    public static bool NoHatch(string? name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        foreach (var k in NoHatchKeys) if (name.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0) return true;
        return false;
    }

    /// <summary>★[D6] 지형 레이어(Revit 지형 «TOPO» · «지형»)는 뺀다.</summary>
    public static bool IsTerrain(string? name)
        => !string.IsNullOrEmpty(name) && (name.IndexOf("TOPO", StringComparison.OrdinalIgnoreCase) >= 0 || name.Contains("지형"));
}
