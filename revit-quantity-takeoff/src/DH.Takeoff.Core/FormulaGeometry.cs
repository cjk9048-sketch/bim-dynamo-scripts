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
}
