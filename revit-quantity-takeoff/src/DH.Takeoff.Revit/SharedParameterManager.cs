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
    };

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
        => name.StartsWith("DH_") ? SpecTypeId.String.Text : SpecTypeId.Number;

    public static string EnsureParameters(Document doc)
    {
        var app = doc.Application;

        string spPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "DH.Takeoff", "DH_SharedParams.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(spPath)!);
        // ★ 항상 새로 비움 — 옛 버전이 남긴 잘못된 단위(길이) 정의를 제거하고
        //   현재 스펙(숫자)으로 다시 정의되게 한다.
        File.WriteAllText(spPath, string.Empty);

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

        int created = 0;
        using (var tx = new Transaction(doc, "DH 공유매개변수 생성"))
        {
            tx.Start();
            foreach (var kv in Guids)
                if (BindParam(doc, group, kv.Key, SpecOf(kv.Key), kv.Value, catSet)) created++;
            tx.Commit();
        }

        return $"공유 매개변수 준비 완료.\n  • 신규 생성: {created}개 / 전체 {Guids.Count}개\n" +
               $"  • 대상: 기둥·보·벽·바닥·기초·일반모델\n  • 파일: {spPath}";
    }

    private static bool BindParam(Document doc, DefinitionGroup group, string name,
                                  ForgeTypeId spec, Guid guid, CategorySet cats)
    {
        Definition def = group.Definitions.get_Item(name)
            ?? group.Definitions.Create(new ExternalDefinitionCreationOptions(name, spec) { GUID = guid });

        BindingMap map = doc.ParameterBindings;
        if (map.Contains(def)) return false; // 멱등 skip

        var binding = doc.Application.Create.NewInstanceBinding(cats);
        return map.Insert(def, binding, GroupTypeId.Geometry);
    }
}
