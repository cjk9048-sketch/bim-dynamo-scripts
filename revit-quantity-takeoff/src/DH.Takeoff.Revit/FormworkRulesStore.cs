using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using DH.Takeoff.Core.Formwork;

namespace DH.Takeoff.Revit;

/// <summary>
/// 거푸집 대응표(사람이 고친 종류 줄 · 지운 판 · 옮긴 판 — FormworkRules 의 글)를 모델 파일 안에 둔다(R57 P2 — JACK 2026-10-23 «배포할 것 · 사람마다 다르면 안 됨» ·
/// 전에는 PC 파일 %APPDATA%\DH.Takeoff\map\{문서 제목}_거푸집표.tsv 라 같은 모델도 다른 PC 에서 열면 고친 것이 없었다).
/// Revit 의 '자료 보관 요소'(DataStorage) 하나 — ZonePlanStore 와 같은 방식. 읽기는 트랜잭션 없이, 쓰기는 Save 의 트랜잭션 하나
/// (수량산출 창은 실행 취소 한 줄 그룹 안 — [처음 상태로 되돌리고 닫기]가 그룹째 되돌리면 대응표도 돌아간다).
/// 기록이 여러 벌이면(작업공유에서 두 사람이 동기화 전에 따로 처음 적음) 적은 시각 차례로 열쇠별로 합친다(계획 검토 M4 · P2 검토 중간 4).
/// </summary>
public static class FormworkRulesStore
{
    private const string Field = "Tsv", SavedAtField = "SavedAt", VersionField = "Version";
    private const string ElementName = "DH 수량산출 — 거푸집 대응표";
    private static readonly Guid SchemaId = new("7a2b6c10-0000-4a00-9b00-0000000000f1");

    /// <summary>
    /// 기록의 판 번호. 더 새 판이 적은 기록은 읽기만 하고 덮어쓰지 않는다(ZonePlanStore 의 TooNew 와 같은 잣대 — P2 검토 낮음).
    /// 2 = 판 종류(«판 종류:» 줄 — R57 P3)가 든 기록: 판 종류를 모르는 R58 이 그 줄을 버리고 덮지 않게(계획 검토 M6). 판 종류가 없으면 1 로 적는다.
    /// 3 = 아래 줄 값(«면|아래 줄» — R64)이 든 기록: 아래 줄을 모르는 R63 까지가 읽기만 하게.
    /// </summary>
    public const int CurrentVersion = 3;

    private static Schema GetSchema()
    {
        var schema = Schema.Lookup(SchemaId);
        if (schema != null) return schema;
        var builder = new SchemaBuilder(SchemaId);
        builder.SetSchemaName("DHTakeoffFormworkRules");
        builder.SetDocumentation("DH Takeoff: formwork table rows edited by hand, dropped and moved plates (FormworkRules text)");
        builder.SetReadAccessLevel(AccessLevel.Public);
        builder.SetWriteAccessLevel(AccessLevel.Public);
        builder.AddSimpleField(Field, typeof(string));
        builder.AddSimpleField(SavedAtField, typeof(string));
        builder.AddSimpleField(VersionField, typeof(int));
        return builder.Finish();
    }

    /// <summary>읽은 것.</summary>
    public sealed class Loaded
    {
        public FormworkRules Rules = new();
        public bool Exists;             // 모델에 기록이 있다
        public string Problem = "";     // 기록을 읽지 못했거나 더 새 판이 적은 까닭("" = 읽었거나 없음) — 있으면 Save 가 덮어쓰지 않는다
        public int Copies;              // 기록이 든 요소 수(1 이 정상 — 둘 이상이면 합쳐 읽고, 다음에 적을 때 하나로 정리)
        public int Conflicts;           // 여러 벌을 합칠 때 값이 달랐던 열쇠 수(나중에 적은 것으로)
    }

    // 기록이 든 요소들(만든 차례). 스키마가 아직 없으면 빈 목록.
    private static List<DataStorage> Holders(Document doc)
    {
        if (Schema.Lookup(SchemaId) == null) return new List<DataStorage>();
        return new FilteredElementCollector(doc).OfClass(typeof(DataStorage))
            .WherePasses(new ExtensibleStorageFilter(SchemaId)).Cast<DataStorage>()
            .OrderBy(d => d.Id.Value).ToList();
    }

    /// <summary>모델의 대응표를 읽는다. 없으면 빈 대응표. 던지지 않는다.</summary>
    public static Loaded Read(Document doc)
    {
        var loaded = new Loaded();
        try
        {
            var holders = Holders(doc);
            loaded.Copies = holders.Count;
            if (holders.Count == 0) return loaded;
            loaded.Exists = true;
            var schema = Schema.Lookup(SchemaId)!;
            // 적은 시각 차례(같으면 Id 차례 — 동기화 때 Id 가 다시 매겨질 수 있어 시각을 먼저 본다): 합치기는 나중 것이 이긴다.
            var copies = new List<(string at, long id, string text, int version)>();
            foreach (var holder in holders)
            {
                var entity = holder.GetEntity(schema);
                if (entity == null || !entity.IsValid()) continue;
                copies.Add((entity.Get<string>(SavedAtField) ?? "", holder.Id.Value, entity.Get<string>(Field) ?? "", entity.Get<int>(VersionField)));
            }
            copies.Sort((a, b) => string.CompareOrdinal(a.at, b.at) is int c && c != 0 ? c : a.id.CompareTo(b.id));
            loaded.Rules = FormworkRules.Merge(copies.Select(c => FormworkRules.Parse(c.text)), out loaded.Conflicts);
            if (copies.Count > 0 && copies.Max(c => c.version) > CurrentVersion)
                loaded.Problem = "더 새 판 애드인이 적은 거푸집 대응표입니다 — 이 판에서는 읽기만 하고 고친 것을 모델에 적지 않습니다(애드인을 새 판으로 올려 주세요)";
        }
        catch (Exception ex) { loaded.Rules = new FormworkRules(); loaded.Problem = "모델의 거푸집 대응표를 읽지 못했습니다: " + ex.Message; }
        return loaded;
    }

    // 대응표를 모델에 적는다(트랜잭션 안). 요소가 없으면 만들고, 여럿이면 하나만 남긴다(rules 는 여러 벌을 합쳐 읽은 것에서 고친 것 — Read).
    private static void Write(Document doc, FormworkRules rules)
    {
        var schema = GetSchema();
        var holders = Holders(doc);
        DataStorage holder;
        if (holders.Count == 0)
        {
            holder = DataStorage.Create(doc);
            try { holder.Name = ElementName; } catch { /* 이름은 알아보기 쉬우라고 붙일 뿐이다 */ }
        }
        else
        {
            holder = holders[^1];
            if (holders.Count > 1) doc.Delete(holders.Take(holders.Count - 1).Select(h => h.Id).ToList());
        }
        var entity = new Entity(schema);
        entity.Set(Field, rules.ToText());
        entity.Set(SavedAtField, DateTime.UtcNow.ToString("o"));
        entity.Set(VersionField, rules.SubCount > 0 ? 3 : rules.KindCount > 0 ? 2 : 1);
        holder.SetEntity(entity);
    }

    /// <summary>
    /// 대응표를 지금 고칠 수 없는 까닭("" = 고칠 수 있거나 아직 없음 — ZonePlanStore.WhyLocked 와 같은 잣대). 작업공유 모델에서 다른 사용자가 가진 요소면
    /// 적기가 되돌려진다 — 적기 전에 알린다.
    /// </summary>
    public static string WhyLocked(Document doc)
    {
        try
        {
            if (!doc.IsWorkshared) return "";
            foreach (var holder in Holders(doc))
            {
                if (WorksharingUtils.GetCheckoutStatus(doc, holder.Id) == CheckoutStatus.OwnedByOtherUser) return "다른 사용자가 거푸집 대응표를 편집 중입니다";
                var updates = WorksharingUtils.GetModelUpdatesStatus(doc, holder.Id);
                if (updates == ModelUpdatesStatus.DeletedInCentral || updates == ModelUpdatesStatus.UpdatedInCentral)
                    return "중앙 모델에서 거푸집 대응표가 바뀌었습니다(동기화한 뒤 다시 열어 주세요)";
            }
        }
        catch { /* 상태를 못 읽으면 막지 않는다 — 못 적으면 적기에서 드러난다 */ }
        return "";
    }

    /// <summary>
    /// 대응표를 모델에 적는다 — 트랜잭션 하나(수량산출 창의 그룹 안에서 부르면 그룹째 되돌릴 수 있다). 돌려주는 값 = "" 이면 됨, 아니면 까닭.
    /// 모델의 기록을 읽지 못했거나 더 새 판이 적은 기록이면 덮어쓰지 않는다(P2 검토 낮음 — 빈 표 + 한 줄로 덮지 않게).
    /// </summary>
    /// <param name="alsoInside">같은 트랜잭션 안에서 함께 할 일(Revit 판 종류를 받아들이며 판 기록을 고침 — R57 P3).</param>
    public static string Save(Document doc, FormworkRules rules, Action<Document>? alsoInside = null)
    {
        string locked = WhyLocked(doc);
        if (locked.Length > 0) return locked;
        var stored = Read(doc);
        if (stored.Problem.Length > 0) return stored.Problem;
        try
        {
            using var tx = new Transaction(doc, "DH 거푸집 대응표");
            tx.Start();
            Write(doc, rules);
            alsoInside?.Invoke(doc);
            var status = tx.Commit();
            return status == TransactionStatus.Committed ? "" : $"Revit 이 변경을 되돌렸습니다({status})";
        }
        catch (Exception ex) { return ex.Message; }
    }

    /// <summary>열 때 알릴 것(여러 벌을 합침 · 다른 사용자가 가짐 · 읽지 못함 — "" = 없음). ② 위 띠 · 거푸집 면 확인 창이 같이 쓴다.</summary>
    public static string Notice(Document doc, Loaded stored)
    {
        var notes = new List<string>();
        if (stored.Problem.Length > 0) notes.Add(stored.Problem);
        if (stored.Copies > 1) notes.Add($"모델에 거푸집 대응표 기록이 {stored.Copies}벌 있어 합쳐 읽었습니다" + (stored.Conflicts > 0 ? $"(값이 다른 열쇠 {stored.Conflicts} — 나중에 적은 것)" : "") + " · 고치면 하나로 정리됩니다");
        string locked = WhyLocked(doc);
        if (locked.Length > 0) notes.Add(locked + " — 고치면 모델에 적지 못합니다");
        return string.Join("\n", notes);
    }
}
