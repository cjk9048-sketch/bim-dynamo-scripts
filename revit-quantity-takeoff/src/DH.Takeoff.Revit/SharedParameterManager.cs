using System.IO;
using Autodesk.Revit.DB;
using DH.Takeoff.Core;

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
        // 거푸집 객체(R44 — JACK 2026-10-15): 판(일반 모델 DirectShape)에만 묶는 칸. 콘크리트 부재의 칸(위)은 쓰지 않는다 — 두 번 세지 않게.
        [FormworkObjects.KindParam] = Guid.Parse("7a2b6c10-0000-4a00-9b00-000000000025"),      // 종류(대응표의 종류 이름 — 글 · R46 까지는 엑셀 절 이름)
        [FormworkObjects.PartParam] = Guid.Parse("7a2b6c10-0000-4a00-9b00-000000000026"),      // 엑셀 줄의 부위 제목(글)
        [FormworkObjects.ZoneParam] = Guid.Parse("7a2b6c10-0000-4a00-9b00-000000000027"),      // 줄의 중분류(글)
        [FormworkObjects.SideParam] = Guid.Parse("7a2b6c10-0000-4a00-9b00-000000000028"),      // 안팎(글)
        [FormworkObjects.FormulaParam] = Guid.Parse("7a2b6c10-0000-4a00-9b00-000000000029"),   // 엑셀 줄의 식(글)
        [FormworkObjects.AreaParam] = Guid.Parse("7a2b6c10-0000-4a00-9b00-000000000030"),      // 넓이(㎡ — 숫자)
        [FormworkObjects.MembersParam] = Guid.Parse("7a2b6c10-0000-4a00-9b00-000000000031"),   // 원 부재 Id(글)
        // …32 · …33 은 쓰지 않는다: R45 의 판 구간 넓이 칸(DH_거푸집_넓이_7m이하 / _7m초과 — 숫자)에 주었다가 7 m 구분을 끄면서 걷어냈다(R46 · 설치 전).
        // 다른 칸에 다시 주지 말 것(어느 PC 의 공유 매개변수 파일에 숫자 꼴로 남아 있을 수 있다 — revit-verify R46 L5).
        // 부재 이름(R53b — SB01 · WA01 …, 글): [수량산출]의 [완료]가 붙인다. 속성 창의 «식별 정보»(마크 곁).
        [MemberNames.Param] = Guid.Parse("7a2b6c10-0000-4a00-9b00-000000000034"),
    };

    /// <summary>
    /// 값이 없으면 속성 창에서 숨는 칸(그 부재에 없는 거푸집 종류는 비워 둔다 — Parameter.ClearValue 는 이런 칸에만 된다).
    /// 거푸집 객체의 칸도 숨긴다: 일반 모델로 그린 콘크리트(디딤판 등)의 속성 창에 빈 칸이 늘지 않게.
    /// </summary>
    private static readonly HashSet<string> HideWhenEmpty = new(DH.Takeoff.Core.Formwork.FormworkTable.GuidelineNames.Concat(FormworkObjects.Params));

    /// <summary>칸의 풍선 글(속성 창에서 칸 이름에 마우스를 올리면 보인다) — 거푸집 객체의 칸만.</summary>
    private static readonly Dictionary<string, string> Descriptions = new()
    {
        [FormworkObjects.KindParam] = "거푸집 종류 — 유로폼 · 합판 3회 · 합판 4회 · 합판 6회",
        [FormworkObjects.PartParam] = "엑셀 «거푸집» 시트 줄의 부위(구멍 둘레는 «HOLE 주위 - 부위»)",
        [FormworkObjects.ZoneParam] = "엑셀 줄의 중분류(이은 판의 중분류가 서로 다르면 빈칸)",
        [FormworkObjects.SideParam] = "외측 · 내측 · 개구부 · 확인 필요",
        [FormworkObjects.FormulaParam] = "이 판의 식 — 엑셀 «거푸집» 시트 줄의 × n 가운데 하나 · 슬래브 밑면은 그 슬래브 줄의 한 칸",
        [FormworkObjects.AreaParam] = "모델에서 잰 넓이(㎡) — 수량집계표의 BIM 칸과 같은 값 · 산출식의 값과는 반올림만큼 다를 수 있음",
        [FormworkObjects.MembersParam] = "이 판이 덮는 콘크리트 부재 Id",
        [MemberNames.Param] = "수량산출 부재 이름(SB01 · WA01 …) — [수량산출]의 [완료]가 붙입니다 · 고쳐 쓴 이름은 그대로 둡니다 · 산출근거 엑셀의 줄 앞 이름",
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
        BuiltInCategory.OST_Stairs,   // R65 — 콘크리트 계단을 부재로 센다(옛 바인딩에는 BindParam 이 더한다)
    };

    /// <summary>DH 칸(거푸집 종류별 넓이 등)을 묶는 범주 — 값이 남은 부재를 찾을 때 먼저 거르는 데 쓴다.</summary>
    internal static IReadOnlyList<BuiltInCategory> BoundCategories => TargetCategories;

    private static ForgeTypeId SpecOf(string name)
        // 분류는 문자, 그 외(치수 L1~W3·H, 횟수 ETC)는 '단위 없는 숫자'.
        // → 사용자가 입력한 값(미터 기준 숫자)이 그대로 CSV에 나가도록(단위 환산 혼선 제거).
        => name.StartsWith("DH_") && !NumberKeys.Contains(name) ? SpecTypeId.String.Text : SpecTypeId.Number;

    /// DH_ 로 시작하지만 숫자인 칸(물량 검산 · 거푸집 넓이 합 · 거푸집 객체의 넓이).
    private static readonly HashSet<string> NumberKeys = new()
    {
        "DH_BIM물량", "DH_수식물량", "DH_물량차", "DH_면적(거푸집)", FormworkObjects.AreaParam,
    };

    /// <param name="only">이 이름들만 만든다(없으면 전부).</param>
    /// <remarks>거푸집 객체의 칸(FormworkObjects.Params)은 누가 부르든 일반 모델에만 묶는다 — 판이 일반 모델이고, 벽 · 바닥의 속성 창에 칸이 늘지 않게.</remarks>
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

            CategorySet Cats(IEnumerable<BuiltInCategory> bics)
            {
                var set = app.Create.NewCategorySet();
                foreach (var bic in bics)
                {
                    var c = Category.GetCategory(doc, bic);
                    if (c != null && c.AllowsBoundParameters) set.Insert(c);
                }
                return set;
            }
            var catSet = Cats(TargetCategories);
            var objectSet = Cats(new[] { BuiltInCategory.OST_GenericModel });

            using var tx = new Transaction(doc, "DH 공유매개변수 생성");
            tx.Start();
            foreach (var kv in Guids)
            {
                if (only != null && !only.Contains(kv.Key)) continue;
                if (BindParam(doc, group, kv.Key, SpecOf(kv.Key), kv.Value, FormworkObjects.Params.Contains(kv.Key) ? objectSet : catSet)) created++;
            }
            tx.Commit();
        }
        finally
        {
            try { app.SharedParametersFilename = previousFile; } catch { }
        }

        return $"공유 매개변수 준비 완료.\n  • 신규 생성: {created}개 / 전체 {only?.Count ?? Guids.Count}개\n" +
               $"  • 대상: 기둥·보·벽·바닥·기초·일반모델·계단\n  • 파일: {spPath}";
    }

    private static bool BindParam(Document doc, DefinitionGroup group, string name,
                                  ForgeTypeId spec, Guid guid, CategorySet cats)
    {
        // Revit 이 이름에 받지 않는 글자가 있으면 만들지 않는다(만들다 예외가 나 다른 칸까지 못 만들지 않게 — 쓰는 쪽이 '칸 없음'으로 알린다).
        // 거푸집 이름의 '0∼7m' 는 이 때문에 '~' 대신 '∼'(U+223C)다.
        if (!NamingUtils.IsValidName(name)) return false;
        Definition def = group.Definitions.get_Item(name)
            ?? group.Definitions.Create(new ExternalDefinitionCreationOptions(name, spec)
            {
                GUID = guid, HideWhenNoValue = HideWhenEmpty.Contains(name), Description = Descriptions.GetValueOrDefault(name, ""),
            });

        BindingMap map = doc.ParameterBindings;
        var groupId = name == MemberNames.Param ? GroupTypeId.IdentityData : GroupTypeId.Geometry;
        if (map.Contains(def))
        {
            // 이미 묶인 칸: 지금 대상 범주가 빠진 옛 바인딩(R65 전 — 계단 없음)이면 지금 범주 ∪ 빠진 범주로 다시 묶는다 · 빠진 것이 없으면 그대로(멱등 —
            // critic R65 M9 · 거푸집 객체 칸은 cats 가 일반 모델뿐이라 그대로). 바인딩은 GUID 로 찾고 범주는 Id 로 견준다(설치 전 검토 열린 질문 — 외부 정의 ·
            // 범주 객체의 같음에 기대지 않게).
            var (key, old) = BindingOf(map, def, guid, doc);
            if (old == null) return false;
            var have = new HashSet<long>(old.Categories.Cast<Category>().Select(c => c.Id.Value));
            var missing = cats.Cast<Category>().Where(c => !have.Contains(c.Id.Value)).ToList();
            if (missing.Count == 0) return false;
            var union = doc.Application.Create.NewCategorySet();
            foreach (Category c in old.Categories) union.Insert(c);
            foreach (var c in missing) union.Insert(c);
            return map.ReInsert(key, doc.Application.Create.NewInstanceBinding(union), groupId);
        }

        var binding = doc.Application.Create.NewInstanceBinding(cats);
        return map.Insert(def, binding, groupId);
    }

    // 묶인 칸의 바인딩(R65): 외부 정의로 먼저 · 안 되면 문서의 정의를 돌며 같은 GUID 의 공유 매개변수로. 인스턴스 바인딩이 아니면 null.
    private static (Definition key, InstanceBinding? binding) BindingOf(BindingMap map, Definition def, Guid guid, Document doc)
    {
        try { if (map.get_Item(def) is InstanceBinding direct) return (def, direct); }
        catch { }
        var it = map.ForwardIterator();
        while (it.MoveNext())
        {
            if (it.Key is not InternalDefinition inner || it.Current is not InstanceBinding b) continue;
            if (doc.GetElement(inner.Id) is SharedParameterElement shared && shared.GuidValue == guid) return (inner, b);
        }
        return (def, null);
    }
}
