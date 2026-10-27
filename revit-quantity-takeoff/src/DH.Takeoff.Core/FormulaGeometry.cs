using DH.Takeoff.Core.View3D;

namespace DH.Takeoff.Core;

/// <summary>산출식 항의 갈래: 더하는 도형 · 빼는 도형(공제) · 곱하는 길이(떡가래의 길이 · 두께).</summary>
public enum TermRole { Add, Deduct, Length }

/// <summary>
/// 식의 숫자 하나와 그 숫자가 재는 선분(모델 좌표 m). Text = 식에 적힌 그 글자 그대로. Angle = 각도(도) · Helper = 도형의 변이 아닌 잰 선
/// (사다리꼴의 높이 · 반지름 · 떡가래의 길이 · 높이장의 꼭짓점 높이).
/// </summary>
public sealed record TermLabel(string Text, Vec3 A, Vec3 B, bool Angle = false, bool Helper = false);

/// <summary>식의 항 하나: 몇째 입체 · 갈래 · 항 글자 · 도형 이름 · 테두리(모델 좌표 m — 닫힌 고리들) · 숫자들.</summary>
public sealed record FormulaTerm(int Solid, TermRole Role, string Text, string Shape, IReadOnlyList<IReadOnlyList<Vec3>> Outlines, IReadOnlyList<TermLabel> Labels);

/// <summary>
/// ③ 콘크리트 줄의 3D 길이 숫자(R61b — JACK 2026-10-26 «정확하게» · 공제 «빨간 점선»): 분해기가 식을 만들며 기억한 항마다의 도형.
/// FormulaDecomposer.Explain 이 채운다 — 같은 입체의 Decompose 와 식 글자가 바이트까지 같다.
/// </summary>
public sealed class FormulaGeometry
{
    public string Formula { get; init; } = "";
    public List<FormulaTerm> Terms { get; } = new();
}

/// <summary>FormulaGeometry → 3D 그림의 숫자 딱지 · 점선(장면 좌표 = 모델 − 원점).</summary>
public static class ConcreteLabels
{
    /// <summary>점선의 갈래: 더하는 도형 · 빼는 도형(빨강) · 잰 선(회색).</summary>
    public const int AddGuide = 0, DeductGuide = 1, MeasureGuide = 2;

    /// <summary>숫자(자리 · 글 · 그 숫자가 재는 선분 — 누르면 강조) · 점선.</summary>
    public static (List<(Vec3 at, string text, Vec3 from, Vec3 to)> labels, List<(IReadOnlyList<Vec3> points, bool closed, int kind)> guides) Of(FormulaGeometry geo, Vec3 origin)
    {
        var labels = new List<(Vec3, string, Vec3, Vec3)>();
        var guides = new List<(IReadOnlyList<Vec3>, bool, int)>();
        foreach (var t in geo.Terms)
        {
            int kind = t.Role == TermRole.Deduct ? DeductGuide : t.Role == TermRole.Length ? MeasureGuide : AddGuide;
            foreach (var loop in t.Outlines)
                if (loop.Count >= 2) guides.Add((loop.Select(p => p - origin).ToList(), true, kind));
            foreach (var l in t.Labels)
            {
                // 각도 숫자는 선분이 아니다(중심 가까이 놓은 자리) — 강조할 선 없음.
                var at = (l.A + l.B) * 0.5 - origin;
                labels.Add(l.Angle ? (at, l.Text + "°", at, at) : (at, l.Text, l.A - origin, l.B - origin));
                if (l.Helper) guides.Add((new List<Vec3> { l.A - origin, l.B - origin }, false, MeasureGuide));
            }
        }
        return (labels, guides);
    }

    /// <summary>
    /// Of 의 숫자 딱지마다 식(geo.Formula)의 숫자 차례(Formwork.FormworkLabels.NumberPattern — 0부터 · 모르면 −1 · R65b — JACK 2026-10-27 «0.1 에 갖다 대면
    /// 모든 0.1 이 아니라 그 수식이 쓰인 부분의 0.1 만»): 항의 글을 식에서 앞에서부터 찾고(숫자 경계 · 못 찾으면 처음부터 · 이미 짝지은 숫자는 빼고) 그 자리 안의
    /// 숫자를 딱지 글과 같은 것부터 차례로 짝짓는다. 숫자가 없는 항(높이장의 둘째 조각부터)은 자리를 옮기지 않는다.
    /// R65c(JACK 2026-10-27 «a × b 구간의 b 에 대면 그 b 만이어야 하는데 다르게 잡혀»): 같은 크기로 묶인 공제(«-(c*b*n)» — FormulaDecomposer.Deductions ·
    /// 같은 입체 · 크기 열쇠 SizeKey 가 같은 공제 · 곱하는 차례가 달라도)는 여덟 조각의 딱지가 모두 그 묶음의 숫자에 잇는다(전에는 첫 조각만 · 나머지는 −1 이라
    /// 같은 글의 다른 숫자(a × b 의 b)에도 켜졌다).
    /// </summary>
    public static List<int> Tokens(FormulaGeometry geo)
    {
        var numbers = Formwork.FormworkLabels.NumberPattern.Matches(geo.Formula).Select(m => (m.Index, m.Value)).ToList();
        var used = new bool[numbers.Count];
        var tokens = new List<int>();
        var groups = new Dictionary<(int solid, string key), (int first, int end)>();   // 찾은 공제의 숫자 자리(같은 크기 묶음이 함께 쓴다)
        int cursor = 0;
        foreach (var t in geo.Terms)
        {
            if (t.Labels.Count == 0) continue;
            int first = -1, end = -1;
            bool shared = false;
            var group = (t.Solid, FormulaDecomposer.SizeKey(t.Text));
            if (t.Role == TermRole.Deduct && groups.TryGetValue(group, out var span))
            {
                (first, end) = span;   // 같은 크기 공제의 둘째 조각부터 — 묶음 «(a*b*n)» 의 숫자를 함께
                shared = true;
            }
            else
            {
                int pos = Find(geo.Formula, t.Text, cursor);
                bool ahead = pos >= 0;
                // 못 찾으면: 길이 항(떡가래의 «)*h»)은 식 끝에서 · 그 밖은 처음부터(같은 크기로 묶인 공제 «-(a*b*2)» 의 개수를 길이로 잡지 않게 — 검토 낮음 3).
                if (!ahead) pos = t.Role == TermRole.Length ? FindLast(geo.Formula, t.Text) : Find(geo.Formula, t.Text, 0);
                if (pos >= 0)
                {
                    first = numbers.FindIndex(n => n.Index >= pos);
                    end = numbers.FindIndex(n => n.Index >= pos + t.Text.Length);
                    if (first < 0) first = numbers.Count;
                    if (end < 0) end = numbers.Count;
                    if (ahead) cursor = pos + t.Text.Length;
                    if (t.Role == TermRole.Deduct) groups.TryAdd(group, (first, end));
                }
            }
            var mine = new HashSet<int>();   // 이 항 안에서 쓴 숫자(묶음을 함께 써도 한 항의 두 딱지가 한 숫자에 겹치지 않게)
            foreach (var l in t.Labels)
            {
                int hit = -1;
                for (int k = Math.Max(first, 0); k < end; k++)
                    if ((shared ? !mine.Contains(k) : !used[k]) && numbers[k].Value == l.Text && !Operand(k)) { hit = k; break; }
                if (hit >= 0) { mine.Add(hit); if (!shared) used[hit] = true; }
                tokens.Add(hit);
            }
        }
        return tokens;

        // «/2» · «^2» 의 수(사다리꼴의 나누기 · 제곱)는 치수가 아니다 — 같은 글의 높이 딱지가 거기 붙지 않게(R65b 설치 전 검토 중간 1).
        bool Operand(int k) => numbers[k].Index > 0 && geo.Formula[numbers[k].Index - 1] is '/' or '^';

        // 끝에서부터 찾기(숫자 경계).
        static int FindLast(string formula, string text)
        {
            if (text.Length == 0) return -1;
            for (int at = formula.LastIndexOf(text, StringComparison.Ordinal); at >= 0; at = at == 0 ? -1 : formula.LastIndexOf(text, at - 1, StringComparison.Ordinal))
            {
                int end = at + text.Length;
                bool before = at > 0 && (char.IsDigit(formula[at - 1]) || formula[at - 1] == '.');
                bool tail = end < formula.Length && (char.IsDigit(formula[end]) || formula[end] == '.');
                if (!before && !tail) return at;
            }
            return -1;
        }

        // 글 text 를 from 부터 찾되 숫자 한가운데(«10.15» 의 «0.1»)는 건너뛴다.
        static int Find(string formula, string text, int from)
        {
            if (text.Length == 0) return -1;
            for (int at = formula.IndexOf(text, from, StringComparison.Ordinal); at >= 0; at = formula.IndexOf(text, at + 1, StringComparison.Ordinal))
            {
                bool before = at > 0 && (char.IsDigit(formula[at - 1]) || formula[at - 1] == '.') && (char.IsDigit(text[0]) || text[0] == '.');
                int after = at + text.Length;
                bool tail = after < formula.Length && (char.IsDigit(formula[after]) || formula[after] == '.') && (char.IsDigit(text[^1]) || text[^1] == '.');
                if (!before && !tail) return at;
            }
            return -1;
        }
    }
}
