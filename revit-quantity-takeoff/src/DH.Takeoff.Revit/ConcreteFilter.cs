using System.Text.RegularExpressions;
using Autodesk.Revit.DB;

namespace DH.Takeoff.Revit;

/// <summary>
/// 부재가 콘크리트인지 재료로 가린다 — 산출식 자동입력의 대상은 콘크리트 부재뿐이다(JACK 2026-10-07).
/// 빼는 것은 '다른 재료임이 뚜렷한' 부재뿐이다: 커튼월, 금속·유리·목재 같은 재료, 조적(블록·벽돌).
/// 콘크리트라고 적혀 있지 않아도 무슨 재료인지 알 수 없는 부재(기본 벽·일반 재료·재료 없음)는 빼지 않고 그대로 둔다
/// — 콘크리트 부재를 잘못 빼는 쪽이 더 나쁘다.
/// 무엇을 뺐는지는 요약 창과 결과표('재료'·'콘크리트' 칸)에 남긴다.
/// </summary>
public static class ConcreteFilter
{
    public enum Kind { Concrete, NotConcrete, Unknown }

    /// <summary>판단 결과. Material = 가장 많이 차지하는 재료 이름, Why = 뺀 사유 또는 알 수 없는 까닭.</summary>
    public readonly record struct Info(Kind Kind, string Material, string Why);

    public static Info Classify(Document doc, Element el)
    {
        // Revit 계단(R65): 런 · 참 · 지지재 · 계단의 재료를 함께 본다(StairReader.Judge — 모으기와 같은 판정 · critic R65 H2).
        if (el is Autodesk.Revit.DB.Architecture.Stairs stairs)
        {
            var (concrete, mixed, material) = StairReader.Judge(doc, stairs);
            return !concrete ? new(Kind.NotConcrete, material, "계단 재료가 콘크리트가 아님")
                 : mixed ? new(Kind.NotConcrete, material, "섞인 계단(콘크리트 + 다른 재료) — 거푸집의 둘레 콘크리트로만")
                 : new(Kind.Concrete, material, "");
        }
        // 커튼월(유리벽)은 벽 자체에 재료가 없다(패널·멀리언이 따로 있는 부재다) — 콘크리트가 아니다.
        if (el is Wall w)
        {
            try { if (w.WallType?.Kind == WallKind.Curtain) return new(Kind.NotConcrete, "", "커튼월"); }
            catch { }
        }

        ICollection<ElementId> ids;
        try { ids = el.GetMaterialIds(false); }
        catch { return new(Kind.Unknown, "", "재료를 읽지 못함"); }

        // 재료마다 차지하는 체적을 보고, 가장 많이 차지하는 재료로 판단한다(마감이 얇게 붙은 벽도 본체 재료로 가리게).
        string name = "";
        double most = -1;
        Kind mostKind = Kind.Unknown;
        bool anyConcrete = false, allOther = true;
        foreach (var id in ids)
        {
            if (doc.GetElement(id) is not Material m) continue;
            Kind kind = KindOf(doc, m);
            anyConcrete |= kind == Kind.Concrete;
            allOther &= kind == Kind.NotConcrete;
            double v = 0;
            try { v = el.GetMaterialVolume(id); } catch { }
            if (v > most) { most = v; name = m.Name ?? ""; mostKind = kind; }
        }
        if (most < 0) return new(Kind.Unknown, "", "재료 없음");

        // 재료별 체적을 못 읽는 부재(전부 0)는 콘크리트 재료가 하나라도 있으면 콘크리트, 전부 다른 재료일 때만 뺀다.
        Kind result = most > 0 ? mostKind : anyConcrete ? Kind.Concrete : allOther ? Kind.NotConcrete : Kind.Unknown;
        return result switch
        {
            Kind.Concrete => new(Kind.Concrete, name, ""),
            Kind.NotConcrete => new(Kind.NotConcrete, name, "재료: " + name),
            _ => new(Kind.Unknown, name, "무슨 재료인지 알 수 없음: " + name),
        };
    }

    /// <summary>
    /// 재료 하나의 판단. 구조 물성의 종류(언어와 무관) → 재료 종류 글자 → 재료 이름 순으로 본다.
    /// 조적(블록·벽돌)은 이름에 '콘크리트'가 들어가도 콘크리트로 보지 않는다.
    /// 콘크리트도 아니고 다른 재료라는 표시도 없으면 Unknown 이다(기본 벽, 일반, 구조체, C24 …).
    /// </summary>
    public static Kind KindOf(Document doc, Material m)
    {
        string cls = m.MaterialClass ?? "", name = m.Name ?? "";
        if (Has(cls, Masonry) || Has(name, Masonry)) return Kind.NotConcrete;

        var asset = StructuralAssetClass.Undefined;
        try
        {
            if (m.StructuralAssetId != ElementId.InvalidElementId &&
                doc.GetElement(m.StructuralAssetId) is PropertySetElement pse)
                asset = pse.GetStructuralAsset()?.StructuralAssetClass ?? StructuralAssetClass.Undefined;
        }
        catch { }

        if (asset == StructuralAssetClass.Concrete || Has(cls, Concrete) || Has(name, Concrete)) return Kind.Concrete;
        if (asset is StructuralAssetClass.Metal or StructuralAssetClass.Wood or StructuralAssetClass.Plastic
                  or StructuralAssetClass.Liquid or StructuralAssetClass.Gas ||
            Has(cls, Other) || Has(name, Other) || SteelCode.IsMatch(name))
            return Kind.NotConcrete;
        return Kind.Unknown;
    }

    private static readonly string[] Concrete = { "콘크리트", "레미콘", "concrete" };
    private static readonly string[] Masonry = { "조적", "블록", "벽돌", "masonry", "block", "brick", "cmu" };

    // 콘크리트가 아님이 뚜렷한 재료의 낱말.
    private static readonly string[] Other =
    {
        "금속", "강재", "강철", "철골", "스틸", "스테인", "스텐", "알루미늄", "유리", "글라스", "목재", "나무", "합판", "석고", "플라스틱", "고무", "단열",
        "metal", "steel", "stainless", "alumin", "glass", "glazing", "wood", "timber", "plywood", "gypsum", "plastic", "rubber", "insulation",
    };

    // 강재 기호(STS304, SUS304 …) — 다른 낱말 속의 글자와 섞이지 않게 앞뒤가 영문자가 아닐 때만.
    private static readonly Regex SteelCode = new(@"(?<![A-Za-z])(STS|SUS)(?![A-Za-z])", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static bool Has(string text, string[] words) =>
        words.Any(w => text.Contains(w, StringComparison.OrdinalIgnoreCase));
}
