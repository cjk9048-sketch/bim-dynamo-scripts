using System.Globalization;
using System.Text.RegularExpressions;
using DH.Takeoff.Core.View3D;

namespace DH.Takeoff.Core.Formwork;

/// <summary>
/// ③ 산출서 확인의 3D 길이 숫자(R60 — JACK 2026-10-23 «해당 수식의 길이에 해당하는 부분이 3D 객체에 텍스트로 투영해서 보고 싶어» · 인터뷰 «판의 변에 길이 숫자»):
/// 판 식이 «a × b» 이면 다듬은 테두리(FormworkPlate.PlanOf)에서 길이가 a 인 변 · b 인 변(서로 꺾인 변)을 찾아 그 변 가운데에 «a» · «b».
/// 못 찾으면(식이 복잡함 · 넓이만 · 이은 판의 꺾인 테두리) 판 가운데에 그 판의 식 한 줄. 자리는 장면 좌표(모델 − 원점 — 3D 의 겉면과 같은 잣대).
/// R65b: 사다리꼴 «(a+b)*0.5*h» 는 나란한 두 변에 a · b · 두 변 사이 선에 h · 삼각형 «w*h*0.5» 는 밑변에 w · 꼭짓점에서 밑변까지 선에 h ·
/// 평행사변형 «a*b»(b 가 꺾인 변이 아니라 나란한 변 사이 거리)도 그 선에.
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
        if (regions != null && SlopedSpots(unit.Formula, regions.SelectMany(r => r.Outer.Select((p, i) => (a: V(p), b: V(r.Outer[(i + 1) % r.Outer.Count])))).ToList(), origin) is { } sloped)
            return sloped;
        if (Product(unit.Formula) is null && Detail(unit) is { Count: > 0 } detail)
            return detail.Select(d => ((V(d.A) + V(d.B)) * 0.5 - origin, d.Text, V(d.A) - origin, V(d.B) - origin)).ToList();
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
        // a × b 인데 변을 못 찾은 판도 식 조각의 자리로(R66a).
        if (Detail(unit) is { Count: > 0 } any)
            return any.Select(d => ((V(d.A) + V(d.B)) * 0.5 - origin, d.Text, V(d.A) - origin, V(d.B) - origin)).ToList();
        // 판 가운데: 테두리(없으면 세모)의 점들을 싼 상자의 가운데(넓이만인 판 — R66a 부터).
        var points = unit.Shape.Outline.SelectMany(r => r).Concat(unit.Shape.Outline.Count == 0 ? unit.Shape.Triangles : Enumerable.Empty<double[]>()).Where(p => p.Length == 3).ToList();
        if (points.Count == 0 || unit.Formula.Length == 0) return labels;
        var box = Box3.Empty;
        foreach (var p in points) box = box.With(V(p));
        labels.Add((box.Center - origin, Pretty(unit.Formula), box.Center - origin, box.Center - origin));
        return labels;
    }

    // 판 식 조각의 숫자 자리(R66a — FormworkSheet.ShapeDetail · 판마다 한 번 · 판 식이 판 모양에서 다시 지은 식과 글자까지 같을 때만).
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<FormworkUnit, List<FormworkSheet.ShapeLabel>> Details = new();

    internal static List<FormworkSheet.ShapeLabel>? Detail(FormworkUnit unit)
    {
        if (unit.AreaOnly || unit.Formula.Length == 0) return null;
        if (Details.TryGetValue(unit, out var known)) return known.Count > 0 ? known : null;
        var (formula, areaOnly, labels) = FormworkSheet.ShapeDetail(unit.Shape);
        var made = !areaOnly && formula == unit.Formula ? labels : new List<FormworkSheet.ShapeLabel>();
        Details.AddOrUpdate(unit, made);
        return made.Count > 0 ? made : null;
    }

    private static readonly Regex TrapezoidText = new(@"^\((\d+(?:\.\d+)?)\+(\d+(?:\.\d+)?)\)\*1/2\*(\d+(?:\.\d+)?)$", RegexOptions.CultureInvariant);
    private static readonly Regex TriangleText = new(@"^(\d+(?:\.\d+)?)\*(\d+(?:\.\d+)?)\*1/2$", RegexOptions.CultureInvariant);

    // 비스듬한 판의 숫자(R65b — FormworkSheet.Sloped 의 한 항 식): 사다리꼴 · 삼각형 · 평행사변형. 변을 못 찾으면 null(부르는 쪽이 다른 길로).
    private static List<(Vec3 at, string text, Vec3 from, Vec3 to)>? SlopedSpots(string formula, List<(Vec3 a, Vec3 b)> edges, Vec3 origin)
    {
        static double Len((Vec3 a, Vec3 b) e) => (e.b - e.a).Length;
        // 점 p 에서 선(e)으로 내린 발.
        static Vec3 Foot(Vec3 p, (Vec3 a, Vec3 b) e)
        {
            var d = (e.b - e.a).Unit();
            return e.a + d * (p - e.a).Dot(d);
        }
        (Vec3, string, Vec3, Vec3) On((Vec3 a, Vec3 b) e, string text) => ((e.a + e.b) * 0.5 - origin, text, e.a - origin, e.b - origin);
        (Vec3, string, Vec3, Vec3) Across(Vec3 p, Vec3 q, string text) => ((p + q) * 0.5 - origin, text, p - origin, q - origin);
        // 길이가 len 인 변 · 나란한 변(거리 h)의 짝: 아래 · 앞 변부터.
        ((Vec3 a, Vec3 b) first, (Vec3 a, Vec3 b) second)? Parallel(double a, double b, double h)
        {
            foreach (var e1 in edges.Where(e => Math.Abs(Len(e) - a) <= Tolerance).OrderBy(e => Mid(e).Z).ThenBy(e => Mid(e).X).ThenBy(e => Mid(e).Y))
                foreach (var e2 in edges.Where(e => !e.Equals(e1) && (b < 0 || Math.Abs(Len(e) - b) <= Tolerance)))
                {
                    if (Math.Abs((e1.b - e1.a).Unit().Dot((e2.b - e2.a).Unit())) < 0.999) continue;
                    if (Math.Abs((Foot(Mid(e1), e2) - Mid(e1)).Length - h) > Tolerance) continue;
                    return (e1, e2);
                }
            return null;
        }
        if (TrapezoidText.Match(formula) is { Success: true } t)
        {
            string ta = t.Groups[1].Value, tb = t.Groups[2].Value, th = t.Groups[3].Value;
            if (Parallel(Parse(ta), Parse(tb), Parse(th)) is not { } pair) return null;
            // 높이 선: 짧은 변의 가운데에서 긴 변으로.
            var m = Mid(pair.first);
            return new() { On(pair.first, ta), On(pair.second, tb), Across(m, Foot(m, pair.second), th) };
        }
        if (TriangleText.Match(formula) is { Success: true } w)
        {
            string tw = w.Groups[1].Value, th = w.Groups[2].Value;
            double hh = Parse(th);
            foreach (var e in edges.Where(e => Math.Abs(Len(e) - Parse(tw)) <= Tolerance).OrderBy(e => Mid(e).Z).ThenBy(e => Mid(e).X).ThenBy(e => Mid(e).Y))
            {
                var apex = edges.Select(x => x.a).Where(p => (p - e.a).Length > Tolerance && (p - e.b).Length > Tolerance)
                                .Select(p => (Vec3?)p).FirstOrDefault(p => Math.Abs((Foot(p!.Value, e) - p.Value).Length - hh) <= Tolerance);
                if (apex is { } top) return new() { On(e, tw), Across(top, Foot(top, e), th) };
            }
            return null;
        }
        // 평행사변형 «a*b»: 길이 a 인 변과 나란한 변 사이 거리가 b(꺾인 변의 길이가 b 가 아님 — 직각 판은 Spots 의 a × b 길이 그대로).
        if (Product(formula) is { } ab && !edges.Any(e => Math.Abs(Len(e) - ab.b) <= Tolerance) && Parallel(ab.a, -1, ab.b) is { } pg)
        {
            var m = Mid(pg.first);
            return new() { On(pg.first, ab.textA), Across(m, Foot(m, pg.second), ab.textB) };
        }
        return null;

        static double Parse(string s) => double.Parse(s, CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// 식의 숫자(식 글의 차례 — ③ 아래 식 띠 · 숫자 딱지 잇기가 같은 셈). 삼각형 · 사다리꼴의 반 «1/2»(JACK 2026-10-29 «일반적인 산근에서 1/2 로 표시 —
    /// 0.5 로 하면 안 됨»)은 숫자 하나로 읽는다(«1» · «2» 둘로 갈리면 사다리꼴 «(a+b)*1/2*h» 의 h 차례가 어긋난다).
    /// </summary>
    public static readonly Regex NumberPattern = new(@"(?<![\d.])1/2(?![\d.])|\d+(?:\.\d+)?", RegexOptions.CultureInvariant);

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
        // R66a: 판 식 조각의 자리로 놓은 숫자(복잡한 판 식) — 줄 식에서 판 식이 시작하는 차례 + 판 식 안의 차례.
        // 줄 식에서 판 식의 자리를 모르면(StartOf −1 — 둘레 · 공통 수 줄) 아래 TermOf 길로(설치 전 검토 L8).
        if (Detail(unit) is { } detail && detail.Count == spotTexts.Count && detail.Select(d => d.Text).SequenceEqual(spotTexts)
            && (Product(unit.Formula) is null || spotTexts.Count != 2) && TrapezoidText.Match(unit.Formula) is { Success: false } && TriangleText.Match(unit.Formula) is { Success: false }
            && line.StartOf.TryGetValue(unit, out int start) && start >= 0)
        {
            var lineNumbers = NumberPattern.Matches(line.Formula).Select(m => m.Value).ToList();
            for (int i = 0; i < detail.Count; i++)
                if (detail[i].Token >= 0 && start + detail[i].Token < lineNumbers.Count && lineNumbers[start + detail[i].Token] == spotTexts[i]) result[i] = start + detail[i].Token;
            return result;
        }
        if (!line.TermOf.TryGetValue(unit, out int k) || Monomials(line.Formula) is not { } monos || k < 0 || k >= monos.Count) return result;
        var mono = monos[k];
        var numbers = NumberPattern.Matches(line.Formula).Select(m => m.Value).ToList();
        if (spotTexts.Count == 2 && mono.Count >= 2)
        {
            int a = mono[0], b = line.Faces > 0 || line.Terms > 0 ? mono[^1] : mono[1];
            if (numbers[a] == spotTexts[0]) result[0] = a;
            if (numbers[b] == spotTexts[1]) result[1] = b;
        }
        else if (spotTexts.Count == 3 && mono.Count >= 3 && k + 1 < monos.Count && monos[k + 1].Count >= 3)
        {
            // 사다리꼴 «(a+b)*1/2*h»(R65b — 설치 전 검토 중간 4): 펼치면 [a, 1/2, h] · [b, 1/2, h] — a = 이 항의 첫 수 · b = 다음 항의 첫 수 · h = 이 항의 셋째 수.
            int a = mono[0], b = monos[k + 1][0], h = mono[2];
            if (numbers[a] == spotTexts[0]) result[0] = a;
            if (numbers[b] == spotTexts[1]) result[1] = b;
            if (numbers[h] == spotTexts[2]) result[2] = h;
        }
        else if (spotTexts.Count == 1 && mono.Count == 1 && numbers[mono[0]] == spotTexts[0]) result[0] = mono[0];
        return result;
    }

    /// <summary>
    /// 식 숫자 token(글 tokenText)에 마우스를 올렸을 때 칠할 3D 딱지(R65c — JACK 2026-10-27 «같은 숫자인데 다 잡힌 것도 아니고 안 잡힌 것도 있어»): 그 숫자에 이은 딱지만 ·
    /// 그 숫자에 이은 딱지가 하나도 없을 때만 못 이은(−1) 같은 글의 딱지. 못 이은 딱지가 이미 다른 딱지가 이은 숫자를 따라 켜지지 않는다.
    /// labelTokens 가 없으면(잇지 않은 판) 같은 글이면 모두(전과 같음). 딱지 글의 «°» 는 뗀다.
    /// </summary>
    public static List<int> LabelsLitBy(IReadOnlyList<int>? labelTokens, IReadOnlyList<string?> labelTexts, int token, string tokenText)
    {
        var hits = new List<int>();
        if (labelTokens != null)
            for (int i = 0; i < labelTexts.Count; i++)
                if (i < labelTokens.Count && labelTokens[i] == token) hits.Add(i);
        if (hits.Count > 0) return hits;
        for (int i = 0; i < labelTexts.Count; i++)
            if ((labelTokens == null || i >= labelTokens.Count || labelTokens[i] < 0) && labelTexts[i]?.TrimEnd('°') == tokenText) hits.Add(i);
        return hits;
    }

    /// <summary>
    /// 3D 딱지 label 에 마우스를 올렸을 때 칠할 식 숫자(R65c): 이은 숫자 하나 · 못 이은 딱지는 같은 글 가운데 이은 딱지가 없는 숫자만(다른 딱지 몫의 숫자는 칠하지 않는다).
    /// labelTokens 가 없으면 같은 글이면 모두(전과 같음).
    /// </summary>
    public static List<int> TokensMarkedBy(IReadOnlyList<int>? labelTokens, IReadOnlyList<string?> labelTexts, int label, IReadOnlyList<string> tokenTexts)
    {
        var marks = new List<int>();
        if (label < 0 || label >= labelTexts.Count) return marks;
        if (labelTokens != null && label < labelTokens.Count && labelTokens[label] >= 0) { marks.Add(labelTokens[label]); return marks; }
        string? text = labelTexts[label]?.TrimEnd('°');
        if (text == null) return marks;
        var owned = labelTokens == null ? new HashSet<int>() : labelTokens.Where(t => t >= 0).ToHashSet();
        for (int t = 0; t < tokenTexts.Count; t++)
            if (tokenTexts[t] == text && !owned.Contains(t)) marks.Add(t);
        return marks;
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
