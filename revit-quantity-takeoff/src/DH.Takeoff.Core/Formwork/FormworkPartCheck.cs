namespace DH.Takeoff.Core.Formwork;

/// <summary>
/// «부위와 모양이 안 맞음» 경고(R57 P4 — JACK 2026-10-23: «출입구 기초» 부재가 부위 «출입구 디딤판»인데 실은 바닥에서 4 m 뜬 캐노피 ·
/// «캐노피» 유형 둘이 실은 지붕 위 디딤판). 경고만 한다 — 셈은 바꾸지 않는다(규칙은 애드인에 고정 · 특수사항은 사람이 부위를 고친다).
///  · 이름: 유형 이름의 괄호 밖 낱말이 가리키는 묶음(PartCatalog.GuessGroup)이 부위의 묶음과 다르다. 바닥 ~ 위 슬래브 · 바닥 ~ 채움은 서로 맞는 것으로 본다
///    («슬래브» 유형을 바닥 · 상부 어디에나 쓰고, 버림 · 구배를 바닥 유형으로 그린다 — 송악3 «SB_T100(기초)» = 버림). 유형 이름에 묶음 낱말이 없으면
///    (SB_T200 · WA_T500 처럼 부호만) · 부위가 없거나 «그 밖» 묶음이면 보지 않는다.
///    짧은 낱말 «보»는 이름 끝에 있을 때만(PartCatalog — 계획 검토 L3 «낱말 경계»).
///  · 모양: 받침류(부위 묶음 «받침·PAD» · 유형 이름 받침류)인데 아래가 빈 밑면이 가장 낮은 바닥판(Revit 바닥) 윗면보다 1 m 넘게 높다.
/// </summary>
public static class FormworkPartCheck
{
    /// <summary>받침류 밑면이 이만큼(m) 넘게 떠 있으면 알린다(계획 §2 P4).</summary>
    public const double FloatingRise = 1.0;

    /// <summary>유형 이름이 가리키는 묶음이 부위의 묶음과 다르면 그 묶음(PartGroup.Key), 아니면 null.</summary>
    /// <param name="partGroup">그 부위가 목록에서 든 묶음(사용자가 옮겼으면 옮긴 묶음) — 없으면 이름으로 짐작.</param>
    public static string? NameMismatch(string type, string part, string? partGroup = null)
    {
        if (string.IsNullOrWhiteSpace(type) || string.IsNullOrWhiteSpace(part)) return null;
        string mine = string.IsNullOrEmpty(partGroup) ? PartCatalog.GroupOf(part) : partGroup;
        if (mine == PartCatalog.Etc) return null;
        if (PartCatalog.GuessGroup(type, bareOnly: true) is not { } named || named == mine) return null;
        if (Near(named, mine) || Near(mine, named)) return null;
        return named;

        // 서로 맞는 것으로 보는 묶음: 바닥 ~ 위 슬래브(«슬래브» 유형) · 바닥 ~ 채움(기초 · 버림 · 구배를 바닥 유형으로 그림).
        static bool Near(string a, string b) => a == PartCatalog.Floor && b is PartCatalog.Upper or PartCatalog.Fill;
    }

    /// <summary>한 부재의 경고 하나.</summary>
    public sealed record Finding(long Id, string Type, string Part, string Text);

    /// <summary>
    /// 거푸집을 잰 결과에서 경고를 모은다(이름 · 모양 — 한 부재에 둘 다면 둘). 부재 Id 차례.
    /// </summary>
    /// <param name="groupOf">부위 이름 → 목록에서 든 묶음(없으면 이름으로 짐작).</param>
    public static List<Finding> Check(IReadOnlyList<ElementFormwork> results, Func<string, string?>? groupOf = null)
    {
        var found = new List<Finding>();
        // 가장 낮은 바닥판(Revit 바닥 · 버림 아님 · 받침류 아님)의 윗면 높이.
        double floorTop = double.MaxValue;
        foreach (var r in results)
        {
            var el = r.Element;
            if (el.Kind != MemberKind.Slab || el.Blinding || FormworkTable.StandsAlone(el)) continue;
            double top = double.MinValue;
            foreach (var solid in el.Solids)
                foreach (var face in solid.Faces)
                    foreach (var loop in face.Loops)
                        foreach (var p in loop.Pts)
                            top = Math.Max(top, p[2]);
            if (top > double.MinValue) floorTop = Math.Min(floorTop, top);
        }
        foreach (var r in results.OrderBy(r => r.Element.Id))
        {
            var el = r.Element;
            if (!el.Target) continue;
            string? group = el.Part.Length > 0 ? groupOf?.Invoke(el.Part) : null;
            if (NameMismatch(el.Type, el.Part, group) is { } named)
                found.Add(new Finding(el.Id, el.Type, el.Part, $"유형 이름은 «{PartCatalog.GroupLabel(named)}»인데 부위는 «{el.Part}»"));
            bool small = FormworkTable.IsSmallPart(el.Type) || (el.Part.Length > 0 && (group ?? PartCatalog.GroupOf(el.Part)) == PartCatalog.Small);
            if (!small || floorTop == double.MaxValue) continue;
            double low = double.MaxValue;
            foreach (var face in r.Faces)
                foreach (var patch in face.Patches)
                {
                    if (patch.Below != Below.Floating) continue;
                    foreach (var ring in patch.Outline)
                        foreach (var p in ring)
                            low = Math.Min(low, p[2]);
                }
            if (low < double.MaxValue && low - floorTop > FloatingRise)
                found.Add(new Finding(el.Id, el.Type, el.Part, $"받침류인데 밑면이 바닥판 위 {low - floorTop:0.0} m 에 떠 있음"));
        }
        return found;
    }
}
