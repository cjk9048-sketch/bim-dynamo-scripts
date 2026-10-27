using System.Globalization;
using System.Text.RegularExpressions;
using DH.Takeoff.Core.View3D;

namespace DH.Takeoff.Core.Formwork;

/// <summary>
/// ③ 산출서 확인의 3D 길이 숫자(R60 — JACK 2026-10-23 «해당 수식의 길이에 해당하는 부분이 3D 객체에 텍스트로 투영해서 보고 싶어» · 인터뷰 «판의 변에 길이 숫자»):
/// 판 식이 «a × b» 이면 다듬은 테두리(FormworkPlate.PlanOf)에서 길이가 a 인 변 · b 인 변(서로 꺾인 변)을 찾아 그 변 가운데에 «a» · «b».
/// 못 찾으면(식이 복잡함 · 넓이만 · 사다리꼴 · 이은 판의 꺾인 테두리) 판 가운데에 그 판의 식 한 줄. 자리는 장면 좌표(모델 − 원점 — 3D 의 겉면과 같은 잣대).
/// </summary>
public static class FormworkLabels
{
    /// <summary>변의 길이와 식의 수가 이만큼(m) 안이면 그 수의 변이다 — 식은 둘째 · 셋째 자리로 끊는다.</summary>
    public const double Tolerance = 0.006;

    /// <summary>판 하나의 숫자들(자리 · 글).</summary>
    public static List<(Vec3 at, string text)> Of(FormworkUnit unit, Vec3 origin) => Spots(unit, origin).Select(s => (s.at, s.text)).ToList();

    /// <summary>
    /// 판 하나의 숫자들과 그 숫자가 재는 선분(장면 좌표 — 3D 에서 숫자를 누르면 그 선을 강조한다 · R61c JACK 2026-10-26).
    /// 판 가운데의 식 딱지는 선분이 없다(from = to = at).
    /// </summary>
    public static List<(Vec3 at, string text, Vec3 from, Vec3 to)> Spots(FormworkUnit unit, Vec3 origin)
    {
        var labels = new List<(Vec3, string, Vec3, Vec3)>();
        var regions = FormworkPlate.PlanOf(unit).Regions;
        if (Product(unit.Formula) is { } ab && regions != null)
        {
            var edges = regions.SelectMany(r => r.Outer.Select((p, i) => (a: V(p), b: V(r.Outer[(i + 1) % r.Outer.Count])))).ToList();
            // a: 길이가 맞는 변 가운데 가장 낮은 것(벽이면 아랫변) · b: a 에 꺾인 변 가운데 a 의 시작점에 가장 가까운 것(ㄱ 자로 읽힌다).
            var first = edges.Where(e => Math.Abs((e.b - e.a).Length - ab.a) <= Tolerance)
                             .OrderBy(e => Mid(e).Z).ThenBy(e => Mid(e).X).ThenBy(e => Mid(e).Y).Select(e => ((Vec3, Vec3)?)e).FirstOrDefault();
            if (first is { } fa)
            {
                var dir = (fa.Item2 - fa.Item1).Unit();
                var second = edges.Where(e => Math.Abs((e.b - e.a).Length - ab.b) <= Tolerance && Math.Abs((e.b - e.a).Unit().Dot(dir)) < 0.2)
                                  .OrderBy(e => Math.Min((e.a - fa.Item1).Length, (e.b - fa.Item1).Length)).Select(e => ((Vec3, Vec3)?)e).FirstOrDefault();
                if (second is { } fb)
                {
                    labels.Add(((fa.Item1 + fa.Item2) * 0.5 - origin, ab.textA, fa.Item1 - origin, fa.Item2 - origin));
                    labels.Add(((fb.Item1 + fb.Item2) * 0.5 - origin, ab.textB, fb.Item1 - origin, fb.Item2 - origin));
                    return labels;
                }
            }
        }
        // 판 가운데: 테두리(없으면 세모)의 점들을 싼 상자의 가운데.
        var points = unit.Shape.Outline.SelectMany(r => r).Concat(unit.Shape.Outline.Count == 0 ? unit.Shape.Triangles : Enumerable.Empty<double[]>()).Where(p => p.Length == 3).ToList();
        if (points.Count == 0 || unit.Formula.Length == 0) return labels;
        var box = Box3.Empty;
        foreach (var p in points) box = box.With(V(p));
        labels.Add((box.Center - origin, Pretty(unit.Formula), box.Center - origin, box.Center - origin));
        return labels;
    }

    /// <summary>식의 숫자(식 글의 차례 — ③ 아래 식 띠 · 숫자 딱지 잇기가 같은 셈).</summary>
    public static readonly Regex NumberPattern = new(@"\d+(?:\.\d+)?", RegexOptions.CultureInvariant);

    /// <summary>
    /// 식을 풀어 쓴 항들(R63d): 항마다 든 숫자의 차례(식 글의 숫자 차례 — 0부터). «(1+1)*2*1» → [0,2,3] · [1,2,3] · «(a*3+b)*c» → [a,3,c] · [b,c].
    /// 더하기 · 빼기는 항을 가르고 곱하기 · 나누기 · 괄호는 곱한다. 못 읽는 글(높이 구간 이름 · 너무 많이 풀림)이면 null.
    /// </summary>
    public static List<List<int>>? Monomials(string formula)
    {
        int at = 0, number = 0;
        bool ok = true;
        void Skip() { while (at < formula.Length && formula[at] == ' ') at++; }
        bool At(params char[] cs) { Skip(); return at < formula.Length && cs.Contains(formula[at]); }
        List<List<int>> Expr()
        {
            var terms = Term();
            while (ok && At('+', '-')) { at++; terms.AddRange(Term()); }
            return terms;
        }
        List<List<int>> Term()
        {
            var result = Factor();
            while (ok && At('*', '/'))
            {
                at++;
                var next = Factor();
                result = result.SelectMany(a => next.Select(b => a.Concat(b).ToList())).ToList();
                if (result.Count > 2000) ok = false;
            }
            return result;
        }
        List<List<int>> Factor()
        {
            if (At('('))
            {
                at++;
                var inner = Expr();
                if (At(')')) at++;
                else ok = false;
                return inner;
            }
            Skip();
            var m = NumberPattern.Match(formula, at);
            if (!m.Success || m.Index != at) { ok = false; return new List<List<int>> { new() }; }
            at += m.Length;
            return new List<List<int>> { new() { number++ } };
        }
        var all = Expr();
        Skip();
        return ok && at == formula.Length && number == NumberPattern.Matches(formula).Count ? all : null;
    }

    /// <summary>
    /// 판의 숫자 딱지(Spots 의 차례)마다 줄의 식에서 그 숫자의 차례(식 글의 숫자 — 0부터 · 모르면 −1 — R63d JACK 2026-10-26 «숫자가 1 이면 같은 건 전부 다 떠 ·
    /// 그 1 마다 위치가 있을 텐데»). 줄을 지을 때 적어 둔 판의 항(FormworkLine.TermOf)으로 고른다: 항의 첫 숫자 = 판의 첫 수(a) · 판의 둘째 수(b) = 둘레 · 공통 수로 묶은 줄은
    /// 항의 끝 숫자(«(a+b)*2*h» · «(a1*n1+a2)*b»), 그 밖은 항의 둘째 숫자(«a*b» · «a*b*n» 칸). 판 가운데 식 딱지는 숫자 하나인 항일 때 그 숫자. 글이 맞지 않으면 −1.
    /// </summary>
    public static int[] SpotTokens(FormworkLine line, FormworkUnit unit, IReadOnlyList<string> spotTexts)
    {
        var result = Enumerable.Repeat(-1, spotTexts.Count).ToArray();
        if (!line.TermOf.TryGetValue(unit, out int k) || Monomials(line.Formula) is not { } monos || k < 0 || k >= monos.Count) return result;
        var mono = monos[k];
        var numbers = NumberPattern.Matches(line.Formula).Select(m => m.Value).ToList();
        if (spotTexts.Count == 2 && mono.Count >= 2)
        {
            int a = mono[0], b = line.Faces > 0 || line.Terms > 0 ? mono[^1] : mono[1];
            if (numbers[a] == spotTexts[0]) result[0] = a;
            if (numbers[b] == spotTexts[1]) result[1] = b;
        }
        else if (spotTexts.Count == 1 && mono.Count == 1 && numbers[mono[0]] == spotTexts[0]) result[0] = mono[0];
        return result;
    }

    /// <summary>식을 화면 글로: «*» → « × » · «+» → « ＋ » · «-» → « − »(2D 꼴 — 계획 검토 L10).</summary>
    public static string Pretty(string formula) => formula.Replace("*", " × ").Replace("+", " ＋ ").Replace("-", " − ");

    // 식이 두 수의 곱 «a*b» 이면 두 수(값 · 글) — 줄을 묶는 잣대(FormworkSheet.Product)와 같다.
    private static (double a, double b, string textA, string textB)? Product(string formula) =>
        FormworkSheet.Product(formula) is { } ab
            ? (double.Parse(ab.a, CultureInfo.InvariantCulture), double.Parse(ab.b, CultureInfo.InvariantCulture), ab.a, ab.b) : null;

    private static Vec3 V(double[] p) => new(p[0], p[1], p[2]);
    private static Vec3 Mid((Vec3 a, Vec3 b) e) => (e.a + e.b) * 0.5;
}
