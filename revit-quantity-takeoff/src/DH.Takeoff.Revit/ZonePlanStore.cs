using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using DH.Takeoff.Core;

namespace DH.Takeoff.Revit;

/// <summary>
/// 「위치·부위 정하기」의 목록(ZonePlan — 위치 목록 · 부위 목록)을 모델 파일 안에 둔다 — Revit 의 '자료 보관 요소'(DataStorage) 하나에 글자(JSON) 한 덩이로.
/// 부재의 값은 부재의 속성에 있고, 여기에는 목록의 차례와 표시만 있다(JACK 2026-10-13: 구역 네모는 없앴다).
/// 읽기는 트랜잭션 없이, 쓰기는 부르는 쪽의 트랜잭션 안에서 한다.
/// </summary>
public static class ZonePlanStore
{
    private const string Field = "Json";
    private const string ElementName = "DH 수량산출 — 위치·부위";
    private static readonly Guid SchemaId = new("7a2b6c10-0000-4a00-9b00-0000000000e2");

    private static Schema GetSchema()
    {
        var schema = Schema.Lookup(SchemaId);
        if (schema != null) return schema;
        var builder = new SchemaBuilder(SchemaId);
        builder.SetSchemaName("DHTakeoffZonePlan");
        builder.SetDocumentation("DH Takeoff: zones drawn in the zone window, pinned members and the part list (JSON)");
        builder.SetReadAccessLevel(AccessLevel.Public);
        builder.SetWriteAccessLevel(AccessLevel.Public);
        builder.AddSimpleField(Field, typeof(string));
        return builder.Finish();
    }

    /// <summary>읽은 것.</summary>
    public sealed class Loaded
    {
        public ZonePlan Plan = new();
        public bool Exists;             // 모델에 저장된 자료가 있다
        public string Problem = "";     // 저장된 자료를 읽지 못한 까닭("" = 읽었거나 없음). 이때 Plan 은 빈 것이다
        public bool TooNew;             // 더 새로운 판의 애드인이 저장한 자료다 — 읽지도 덮어쓰지도 않는다
        public int Copies;              // 자료가 든 요소의 수(1 이 정상)
    }

    // 자료가 든 요소들(만든 차례대로). 스키마가 아직 없으면 빈 목록.
    private static List<DataStorage> Holders(Document doc)
    {
        if (Schema.Lookup(SchemaId) == null) return new List<DataStorage>();
        return new FilteredElementCollector(doc).OfClass(typeof(DataStorage))
            .WherePasses(new ExtensibleStorageFilter(SchemaId)).Cast<DataStorage>()
            .OrderBy(d => d.Id.Value).ToList();
    }

    private static string? Json(DataStorage holder)
    {
        var schema = Schema.Lookup(SchemaId);
        if (schema == null) return null;
        var entity = holder.GetEntity(schema);
        return entity != null && entity.IsValid() ? entity.Get<string>(Field) : null;
    }

    /// <summary>모델에 저장된 계획을 읽는다. 없으면 빈 계획. 던지지 않는다.</summary>
    public static Loaded Read(Document doc)
    {
        var loaded = new Loaded();
        try
        {
            var holders = Holders(doc);
            loaded.Copies = holders.Count;
            if (holders.Count == 0) return loaded;
            loaded.Exists = true;
            // 요소가 둘 이상이면(두 사람이 각자 처음 저장한 뒤 합쳐진 모델 등) 저장한 때가 가장 늦은 것을 쓴다(같으면 나중에 만든 요소).
            // 저장할 때 하나로 정리한다. 읽을 수 없는 벌은 건너뛰되, 읽히는 것이 하나도 없으면 그 까닭을 알린다.
            ZonePlan? best = null;
            string problem = "";
            foreach (var holder in holders)
            {
                try
                {
                    var plan = ZonePlan.FromJson(Json(holder) ?? "");
                    if (best == null || string.CompareOrdinal(plan.SavedAt, best.SavedAt) >= 0) best = plan;
                }
                catch (ZonePlanTooNewException ex) { loaded.TooNew = true; problem = ex.Message; }
                catch (FormatException ex) { problem = ex.Message; }
            }
            if (loaded.TooNew) { loaded.Problem = problem; return loaded; }     // 더 새 판의 자료가 한 벌이라도 있으면 손대지 않는다
            if (best != null) loaded.Plan = best;
            else loaded.Problem = problem;
        }
        catch (Exception ex) { loaded.Problem = "저장된 위치·부위 목록을 읽지 못했습니다: " + ex.Message; loaded.Plan = new ZonePlan(); }
        return loaded;
    }

    /// <summary>계획을 모델에 적는다(트랜잭션 안에서 부른다). 자료 요소가 없으면 만들고, 여럿이면 하나만 남긴다. 돌려주는 값 = 적은 글자.</summary>
    public static string Write(Document doc, ZonePlan plan)
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
        plan.SavedAt = DateTime.UtcNow.ToString("o", System.Globalization.CultureInfo.InvariantCulture);
        string json = plan.ToJson();
        var entity = new Entity(schema);
        entity.Set(Field, json);
        holder.SetEntity(entity);
        return json;
    }

    /// <summary>
    /// 구역 자료를 지금 고칠 수 없는 까닭("" = 고칠 수 있거나 아직 없음). 작업공유 모델에서 다른 사용자가 가진 요소면 저장이 되돌려진다 —
    /// 저장을 시도하기 전에 알려 주려는 것이다(Revit 이 주는 소유 상태는 로컬에 적어 둔 값이라, "" 이어도 저장이 안 될 수는 있다).
    /// </summary>
    public static string WhyLocked(Document doc)
    {
        try
        {
            if (!doc.IsWorkshared) return "";
            foreach (var holder in Holders(doc))
            {
                if (WorksharingUtils.GetCheckoutStatus(doc, holder.Id) == CheckoutStatus.OwnedByOtherUser) return "다른 사용자가 위치·부위 목록을 편집 중입니다";
                var updates = WorksharingUtils.GetModelUpdatesStatus(doc, holder.Id);
                if (updates == ModelUpdatesStatus.DeletedInCentral || updates == ModelUpdatesStatus.UpdatedInCentral)
                    return "중앙 모델에서 위치·부위 목록이 바뀌었습니다(동기화한 뒤 다시 열어 주세요)";
            }
        }
        catch { /* 상태를 못 읽으면 막지 않는다 — 못 고치면 저장에서 드러난다 */ }
        return "";
    }

    /// <summary>지금 모델에 적혀 있는 글자(되읽기용). 없으면 null.</summary>
    public static string? ReadRaw(Document doc)
    {
        try
        {
            var holders = Holders(doc);
            return holders.Count == 0 ? null : Json(holders[^1]);
        }
        catch { return null; }
    }
}
