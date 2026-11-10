namespace DH.Grading.Core;

/// <summary>★★[v116.8 · JACK 1110 «진입도로 도로 설정에 포장 — 아스팔트 · 콘크리트를 고르고 횡단 · 3D에 따라 표현» · 계획 `2026-11-10_진입도로_포장종류.md`]
/// 포장 층 — 순수 산수(현장 값 없음). 도로 면 · 토공은 그대로(포장 윗면 = 마감면 = 지금 도로 면) — 표시만(JACK 결정 3).
/// 두께 첫 값 · 해치 무늬는 예시다(표준 아님 — 창에서 고침).</summary>
public static class RoadPave
{
    public enum Kind { Asphalt = 0, Concrete = 1 }

    /// <summary>층 — 이름 · 두께(m · 연직) · 해치 무늬(acadiso.pat 기본) · 무늬 기본 칸(acadiso 단위 — ANSI31 · 37 3.175 · GRAVEL 약 25.4 · AR-CONC 조각 약 19) ·
    /// 종이 위 칸 크기(mm · 예시 — 화면 보고 고침). 해치 축척 = 종이 mm × 축척 ÷ 1000 ÷ 기본 칸.</summary>
    public sealed record Layer(string Name, double T, string Pattern, double PatMm, double PaperMm);

    /// <summary>예시 첫 값(m) — 아스팔트 표층 · 기층 · 보조기층 / 콘크리트 슬래브 · 보조기층 · 줄눈 간격.</summary>
    public static readonly double[] DefaultA = { 0.05, 0.15, 0.25 };
    public static readonly double[] DefaultC = { 0.25, 0.20 };
    public const double DefaultJointGap = 6.0;
    /// <summary>3D 줄눈 폭(m) · 지형 위 띄움(m) — 화면 보고 고침.</summary>
    public const double JointWidth = 0.03, JointLift = 0.03;

    static readonly string[] NamesA = { "표층", "기층", "보조기층" };
    static readonly string[] NamesC = { "콘크리트 슬래브", "보조기층" };
    static readonly (string Pat, double Mm, double Paper)[] PatA = { ("ANSI37", 3.175, 1.0), ("ANSI31", 3.175, 1.0), ("GRAVEL", 25.4, 5.0) };
    static readonly (string Pat, double Mm, double Paper)[] PatC = { ("AR-CONC", 19.05, 2.0), ("GRAVEL", 25.4, 5.0) };
    /// <summary>종이 위 두께가 이보다 얇은 층은 해치 없이 윤곽만(mm · 결정 D5).</summary>
    public const double ThinPaperMm = 1.0;

    /// <summary>배열 바로잡기 — 길이 · 유한 · 0 ~ 상한(m). 깨지면 첫 값.</summary>
    public static double[] Clean(double[]? v, double[] def, double max = 2.0)
    {
        var o = (double[])def.Clone();
        if (v == null) return o;
        for (int i = 0; i < o.Length && i < v.Length; i++) if (double.IsFinite(v[i]) && v[i] >= 0 && v[i] <= max) o[i] = v[i];
        return o;
    }

    /// <summary>층 차례(위 → 아래) — 두께 0인 층은 뺌.</summary>
    public static List<Layer> Layers(Kind kind, IReadOnlyList<double>? a, IReadOnlyList<double>? c)
    {
        var o = new List<Layer>();
        bool conc = kind == Kind.Concrete;
        var t = conc ? Clean(c?.ToArray(), DefaultC) : Clean(a?.ToArray(), DefaultA);
        var nm = conc ? NamesC : NamesA; var pt = conc ? PatC : PatA;
        for (int i = 0; i < t.Length; i++) if (t[i] > 1e-6) o.Add(new Layer(nm[i], t[i], pt[i].Pat, pt[i].Mm, pt[i].Paper));
        return o;
    }

    /// <summary>횡단 도장 토큰 — «A:50,150,250» · «C:250,200»(mm 반올림 · 줄눈 간격은 안 넣음 — 횡단에 없음).</summary>
    public static string Key(Kind kind, IReadOnlyList<double>? a, IReadOnlyList<double>? c)
    {
        bool conc = kind == Kind.Concrete;
        var t = conc ? Clean(c?.ToArray(), DefaultC) : Clean(a?.ToArray(), DefaultA);
        return (conc ? "C:" : "A:") + string.Join(",", t.Select(v => Math.Round(v * 1000).ToString(System.Globalization.CultureInfo.InvariantCulture)));
    }

    /// <summary>층마다 닫힌 고리(오프셋 O · 중심 높이 기준 높이 차 Z) — 꼭짓점 = 왼 끝 −HalfL · 크라운 0 · 오른 끝 +HalfR.
    /// 윗선 = <see cref="RoadGrade.Section.Drop"/> − 위 층 두께 합 · 밑선 = 윗선 − T · 양 끝 세로(두께는 연직). 반시계(위에서 보아 왼 위 → 오른 위가 아니라 단면 평면 O–Z에서 반시계).</summary>
    public static List<List<(double O, double Z)>> Rings(RoadGrade.Section sec, IReadOnlyList<Layer> layers)
    {
        var o = new List<List<(double O, double Z)>>();
        double[] xs = { -sec.HalfL, 0, sec.HalfR };
        double acc = 0;
        foreach (var l in layers)
        {
            var top = xs.Select(x => (x, sec.Drop(x) - acc)).ToList();
            var bot = xs.Select(x => (x, sec.Drop(x) - acc - l.T)).ToList();
            // O–Z 평면 반시계: 밑 왼 → 밑 오른 → 위 오른 → 위 왼
            var ring = new List<(double O, double Z)>();
            ring.AddRange(bot);
            ring.AddRange(Enumerable.Reverse(top));
            o.Add(ring);
            acc += l.T;
        }
        return o;
    }

    /// <summary>3D 가로 줄눈 띠 — 도로 구간마다 A + k·간격(k ≥ 1 · 구간 끝 0.5 m 안은 안 넣음) · 측점 s에서 −HalfL ~ +HalfR를 0.5 m마다 자른 두 가로선(s ∓ w/2) 사이.
    /// 높이 = 지형 + <paramref name="lift"/> · 버림 까닭은 <see cref="RoadMarks.Ribbon"/> 것 그대로. 결과 줄 수 = 칸을 하나라도 남긴 줄눈 수.</summary>
    public static RoadMarks.RibbonResult Joints(IReadOnlyList<(double A, double B)> spans, double gap, Func<double, double, (double X, double Y)?> pointAt,
        Func<double, (double L, double R)> halfAt, Func<double, double, double?> zAt, double width = JointWidth, double lift = JointLift, double step = 0.5)
    {
        var all = new RoadMarks.RibbonResult();
        if (!(gap >= 0.5) || !(width > 0)) return all;   // ★[설치 전 검토 L] 간격 하한(공개 함수 — 아주 작은 간격으로 끝없이 돌지 않게)
        int lines = 0;
        foreach (var (A, B) in spans)
        {
            if (!(B > A)) continue;
            for (int k = 1; ; k++)
            {
                double s = A + k * gap;
                if (s > B - 0.5) break;
                var (hl, hr) = halfAt(s);
                if (!(hl + hr > 0.1)) continue;
                var offs = new List<double>();
                for (double q = -hl; q < hr - 1e-6; q += step) offs.Add(q);
                offs.Add(hr);
                var a = new List<(double X, double Y)>(offs.Count); var b = new List<(double X, double Y)>(offs.Count);
                foreach (double q in offs)
                {
                    a.Add(pointAt(s - width / 2, q) ?? (double.NaN, double.NaN));
                    b.Add(pointAt(s + width / 2, q) ?? (double.NaN, double.NaN));
                }
                var r = RoadMarks.Ribbon(a, b, zAt, lift, 0);
                int keptBefore = all.Kept;
                r.Lines = 0;
                all.Append(r);
                if (all.Kept > keptBefore) lines++;
            }
        }
        all.Lines = lines;
        return all;
    }
}
