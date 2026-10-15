using System.IO;
using Autodesk.Revit.DB;

namespace DH.Takeoff.Revit;

/// <summary>
/// 공유 매개변수(치수 L1~W3·H, 횟수 ETC, 분류 DH_*)를 생성·바인딩(멱등).
/// ★ GUID 고정: 부서 전 PC에서 동일 매개변수 ID 사용(중복 방지). 읽기도 이 GUID로 한다.
/// </summary>
public static class SharedParameterManager
{
    // 이름 → 고정 GUID (부서 공통, 절대 변경 금지). 생성·읽기 모두 이 GUID 사용.
    public static readonly IReadOnlyDictionary<string, Guid> Guids = new Dictionary<string, Guid>
    {
        ["L1"] = Guid.Parse("7a2b6c10-0000-4a00-9b00-000000000001"),
        ["L2"] = Guid.Parse("7a2b6c10-0000-4a00-9b00-000000000002"),
        ["L3"] = Guid.Parse("7a2b6c10-0000-4a00-9b00-000000000003"),
        ["W1"] = Guid.Parse("7a2b6c10-0000-4a00-9b00-000000000004"),
        ["W2"] = Guid.Parse("7a2b6c10-0000-4a00-9b00-000000000005"),
        ["W3"] = Guid.Parse("7a2b6c10-0000-4a00-9b00-000000000006"),
        ["H"] = Guid.Parse("7a2b6c10-0000-4a00-9b00-000000000007"),
        ["ETC"] = Guid.Parse("7a2b6c10-0000-4a00-9b00-000000000008"),
        ["DH_ElementCode"] = Guid.Parse("7a2b6c10-0000-4a00-9b00-000000000009"),
        ["DH_Class"] = Guid.Parse("7a2b6c10-0000-4a00-9b00-000000000010"),
        ["DH_Category"] = Guid.Parse("7a2b6c10-0000-4a00-9b00-000000000011"),
        ["DH_Zone"] = Guid.Parse("7a2b6c10-0000-4a00-9b00-000000000012"),
        ["DH_Part"] = Guid.Parse("7a2b6c10-0000-4a00-9b00-000000000013"),
        ["DH_Formula"] = Guid.Parse("7a2b6c10-0000-4a00-9b00-000000000014"), // 산출식(문자)
        ["DH_BIM물량"] = Guid.Parse("7a2b6c10-0000-4a00-9b00-000000000015"),  // 모델 체적(m³, 숫자)
        ["DH_수식물량"] = Guid.Parse("7a2b6c10-0000-4a00-9b00-000000000016"), // 산출식을 계산한 값(m³, 숫자)
        ["DH_물량차"] = Guid.Parse("7a2b6c10-0000-4a00-9b00-000000000017"),   // 수식물량 − BIM물량(m³, 숫자)
        ["DH_판정"] = Guid.Parse("7a2b6c10-0000-4a00-9b00-000000000018"),     // 일치 / 불일치 / 수식 없음(문자)
        // 거푸집(R42 — JACK 2026-10-15): 종류별 넓이(㎡, 숫자 — K-water 지침 내역적용수량 이름) + 합계. 이름은 FormworkTable 에 있다.
        ["유로폼［보통, 7m이하］"] = Guid.Parse("7a2b6c10-0000-4a00-9b00-000000000019"),
        ["유로폼［보통, 7m초과］"] = Guid.Parse("7a2b6c10-0000-4a00-9b00-000000000020"),
        ["합판거푸집［복잡, 0∼7m, 3회］"] = Guid.Parse("7a2b6c10-0000-4a00-9b00-000000000021"),
        ["합판거푸집［보통, 0∼7m, 4회］"] = Guid.Parse("7a2b6c10-0000-4a00-9b00-000000000022"),
        ["합판거푸집［간단, 0∼7m, 6회］"] = Guid.Parse("7a2b6c10-0000-4a00-9b00-000000000023"),
        ["DH_면적(거푸집)"] = Guid.Parse("7a2b6c10-0000-4a00-9b00-000000000024"),
    };

    /// <summary>값이 없으면 속성 창에서 숨는 칸(그 부재에 없는 거푸집 종류는 비워 둔다 — Parameter.ClearValue 는 이런 칸에만 된다).</summary>
    private static readonly HashSet<string> HideWhenEmpty = new(DH.Takeoff.Core.Formwork.FormworkTable.GuidelineNames);

    /// 치수(길이) 매개변수 — 내부 피트 저장, 내보낼 때 m로 변환.
    public static readonly string[] LengthKeys = { "L1", "L2", "L3", "W1", "W2", "W3", "H" };

    private static readonly BuiltInCategory[] TargetCategories =
    {
        BuiltInCategory.OST_StructuralColumns,
        BuiltInCategory.OST_StructuralFraming,
        BuiltInCategory.OST_Walls,
        BuiltInCategory.OST_Floors,
        BuiltInCategory.OST_StructuralFoundation,
        BuiltInCategory.OST_GenericModel,
    };

    private static ForgeTypeId SpecOf(string name)
        // 분류는 문자, 그 외(치수 L1~W3·H, 횟수 ETC)는 '단위 없는 숫자'.
        // → 사용자가 입력한 값(미터 기준 숫자)이 그대로 CSV에 나가도록(단위 환산 혼선 제거).
        => name.StartsWith("DH_") && !NumberKeys.Contains(name) ? SpecTypeId.String.Text : SpecTypeId.Number;

    /// DH_ 로 시작하지만 숫자인 칸(물량 검산 · 거푸집 넓이 합).
    private static readonly HashSet<string> NumberKeys = new() { "DH_BIM물량", "DH_수식물량", "DH_물량차", "DH_면적(거푸집)" };

    /// <param name="only">이 이름들만 만든다(없으면 전부).</param>
    public static string EnsureParameters(Document doc, IReadOnlyCollection<string>? only = null)
    {
        var app = doc.Application;
        string previousFile = app.SharedParametersFilename; // 사용자가 쓰던 공유 매개변수 파일 설정은 끝나고 되돌린다

        string spPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "DH.Takeoff", "DH_SharedParams.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(spPath)!);
        // ★ 항상 새로 비움 — 옛 버전이 남긴 잘못된 단위(길이) 정의를 제거하고
        //   현재 스펙(숫자)으로 다시 정의되게 한다.
        File.WriteAllText(spPath, string.Empty);

        int created = 0;
        try
        {
            app.SharedParametersFilename = spPath;
            DefinitionFile defFile = app.OpenSharedParameterFile()
                ?? throw new InvalidOperationException("공유 매개변수 파일을 열 수 없습니다: " + spPath);
            DefinitionGroup group = defFile.Groups.get_Item("DH") ?? defFile.Groups.Create("DH");

            var catSet = app.Create.NewCategorySet();
            foreach (var bic in TargetCategories)
            {
                var c = Category.GetCategory(doc, bic);
                if (c != null && c.AllowsBoundParameters) catSet.Insert(c);
            }

            using var tx = new Transaction(doc, "DH 공유매개변수 생성");
            tx.Start();
            foreach (var kv in Guids)
            {
                if (only != null && !only.Contains(kv.Key)) continue;
                if (BindParam(doc, group, kv.Key, SpecOf(kv.Key), kv.Value, catSet)) created++;
            }
            tx.Commit();
        }
        finally
        {
            try { app.SharedParametersFilename = previousFile; } catch { }
        }

        return $"공유 매개변수 준비 완료.\n  • 신규 생성: {created}개 / 전체 {only?.Count ?? Guids.Count}개\n" +
               $"  • 대상: 기둥·보·벽·바닥·기초·일반모델\n  • 파일: {spPath}";
    }

    private static bool BindParam(Document doc, DefinitionGroup group, string name,
                                  ForgeTypeId spec, Guid guid, CategorySet cats)
    {
        // Revit 이 이름에 받지 않는 글자가 있으면 만들지 않는다(만들다 예외가 나 다른 칸까지 못 만들지 않게 — 쓰는 쪽이 '칸 없음'으로 알린다).
        // 거푸집 이름의 '0∼7m' 는 이 때문에 '~' 대신 '∼'(U+223C)다.
        if (!NamingUtils.IsValidName(name)) return false;
        Definition def = group.Definitions.get_Item(name)
            ?? group.Definitions.Create(new ExternalDefinitionCreationOptions(name, spec) { GUID = guid, HideWhenNoValue = HideWhenEmpty.Contains(name) });

        BindingMap map = doc.ParameterBindings;
        if (map.Contains(def)) return false; // 멱등 skip

        var binding = doc.Application.Create.NewInstanceBinding(cats);
        return map.Insert(def, binding, GroupTypeId.Geometry);
    }
}
