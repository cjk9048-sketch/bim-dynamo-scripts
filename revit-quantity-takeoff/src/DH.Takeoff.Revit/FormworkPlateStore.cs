using System.Globalization;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using DH.Takeoff.Core.Formwork;

namespace DH.Takeoff.Revit;

/// <summary>
/// 거푸집 판(FormworkObjects)의 처음 값 기록과 판 목록(R57 P3 — JACK 2026-10-23 «Revit 판의 종류를 고치면 그 판 한 장만» · 판을 지우면 «다시 만들고 알림»).
///  · 판마다 기록(스키마 «DHTakeoffFormworkPlate» …f2): Uid(판 UniqueId — 복사본 가르기 · PlaceStamp 선례) · Kind(지을 때 · 마지막으로 받아들일 때 적은 종류) ·
///    Base(판 종류를 걸기 전 종류 — 판 안 조각이 모두 같을 때만) · Lead · KindLead · Records(조각 열쇠 — 줄바꿈) · Values(종류 칸 밖의 칸 «이름 탭 값» 줄) ·
///    Extra(여분) · Version. 한 번 내보내면 칸을 바꿀 수 없다(스키마는 고정 — 계획 검토 «빠진 것»).
///  · 판 목록(스키마 «DHTakeoffFormworkPlateList» …f3 · 자료 보관 요소 하나): 마지막 [완료]가 지은 판의 UniqueId 들 — Revit 에서 지운 판을 센다(계획 검토 L7).
/// 읽기는 트랜잭션 없이 · 쓰기는 부르는 쪽의 트랜잭션 안에서. 던지지 않는 읽기.
/// </summary>
public static class FormworkPlateStore
{
    private static readonly Guid PlateSchemaId = new("7a2b6c10-0000-4a00-9b00-0000000000f2");
    private static readonly Guid ListSchemaId = new("7a2b6c10-0000-4a00-9b00-0000000000f3");
    private const string UidField = "Uid", KindField = "Kind", BaseField = "Base", LeadField = "Lead", KindLeadField = "KindLead",
                         RecordsField = "Records", ValuesField = "Values", ExtraField = "Extra", VersionField = "Version";
    private const string UidsField = "Uids", SavedAtField = "SavedAt";
    private const string ListName = "DH 수량산출 — 거푸집 판 목록";
    public const int CurrentVersion = 1;

    private static Schema PlateSchema()
    {
        var schema = Schema.Lookup(PlateSchemaId);
        if (schema != null) return schema;
        var builder = new SchemaBuilder(PlateSchemaId);
        builder.SetSchemaName("DHTakeoffFormworkPlate");
        builder.SetDocumentation("DH Takeoff: formwork plate as built (kind, patch keys, other values) — to take kind edits made in Revit");
        builder.SetReadAccessLevel(AccessLevel.Public);
        builder.SetWriteAccessLevel(AccessLevel.Public);
        foreach (string name in new[] { UidField, KindField, BaseField, LeadField, KindLeadField, RecordsField, ValuesField, ExtraField })
            builder.AddSimpleField(name, typeof(string));
        builder.AddSimpleField(VersionField, typeof(int));
        return builder.Finish();
    }

    private static Schema ListSchema()
    {
        var schema = Schema.Lookup(ListSchemaId);
        if (schema != null) return schema;
        var builder = new SchemaBuilder(ListSchemaId);
        builder.SetSchemaName("DHTakeoffFormworkPlateList");
        builder.SetDocumentation("DH Takeoff: UniqueIds of the formwork plates built by the last [Finish]");
        builder.SetReadAccessLevel(AccessLevel.Public);
        builder.SetWriteAccessLevel(AccessLevel.Public);
        builder.AddSimpleField(UidsField, typeof(string));
        builder.AddSimpleField(SavedAtField, typeof(string));
        builder.AddSimpleField(VersionField, typeof(int));
        return builder.Finish();
    }

    // 종류 칸 밖의 칸(쓰지 않는 칸 — 바뀌면 알림만).
    private static readonly string[] OtherParams =
    {
        FormworkObjects.PartParam, FormworkObjects.ZoneParam, FormworkObjects.SideParam, FormworkObjects.FormulaParam, FormworkObjects.AreaParam, FormworkObjects.MembersParam,
    };

    /// <summary>칸의 글(없으면 "") — 적을 때와 볼 때 같은 잣대.</summary>
    public static string ParamText(Element el, string name)
    {
        var param = el.get_Parameter(SharedParameterManager.Guids[name]);
        if (param == null || !param.HasValue) return "";
        return param.StorageType switch
        {
            StorageType.String => param.AsString() ?? "",
            StorageType.Double => param.AsDouble().ToString("R", CultureInfo.InvariantCulture),
            StorageType.Integer => param.AsInteger().ToString(CultureInfo.InvariantCulture),
            StorageType.ElementId => param.AsElementId().Value.ToString(CultureInfo.InvariantCulture),
            _ => "",
        };
    }

    private static string ValuesOf(Element el) => string.Join("\n", OtherParams.Select(n => n + "\t" + ParamText(el, n)));

    /// <summary>판에 처음 값 기록을 붙인다(트랜잭션 안 · 칸을 적은 뒤).</summary>
    public static void Stamp(DirectShape ds, FormworkUnit unit)
    {
        var (lead, records, baseKind, kindLead) = FormworkPlateIntake.RecordOf(unit);
        var entity = new Entity(PlateSchema());
        entity.Set(UidField, ds.UniqueId ?? "");
        entity.Set(KindField, ParamText(ds, FormworkObjects.KindParam));
        entity.Set(BaseField, baseKind);
        entity.Set(LeadField, lead);
        entity.Set(KindLeadField, kindLead);
        entity.Set(RecordsField, string.Join("\n", records.Select(r => r.Text)));
        entity.Set(ValuesField, ValuesOf(ds));
        entity.Set(ExtraField, "");
        entity.Set(VersionField, CurrentVersion);
        ds.SetEntity(entity);
    }

    /// <summary>받아들인 판의 기록 종류 · 그 판을 정하는 판 종류 기록을 지금 값으로 고친다(트랜잭션 안 — 한 번만 받아들임 · P3 검토 중간 1).</summary>
    public static void MarkTaken(Document doc, IEnumerable<(long id, string kind, string kindLead)> taken)
    {
        var schema = Schema.Lookup(PlateSchemaId);
        if (schema == null) return;
        foreach (var (id, _, kindLead) in taken)
        {
            if (doc.GetElement(new ElementId(id)) is not DirectShape ds || ds.GetEntity(schema) is not { } entity || !entity.IsValid()) continue;
            var fresh = new Entity(schema);
            foreach (string name in new[] { UidField, BaseField, LeadField, RecordsField, ValuesField, ExtraField })
                fresh.Set(name, entity.Get<string>(name) ?? "");
            fresh.Set(KindField, ParamText(ds, FormworkObjects.KindParam));
            fresh.Set(KindLeadField, kindLead);
            fresh.Set(VersionField, entity.Get<int>(VersionField));
            ds.SetEntity(fresh);
        }
    }

    /// <summary>모델의 판(이 애드인이 만든 것)마다 본 것. 던지지 않는다(못 읽은 판은 기록 없음으로).</summary>
    public static List<FormworkPlateIntake.Seen> Scan(Document doc)
    {
        var seen = new List<FormworkPlateIntake.Seen>();
        var schema = Schema.Lookup(PlateSchemaId);
        foreach (var id in FormworkObjects.Find(doc))
        {
            if (doc.GetElement(id) is not DirectShape ds) continue;
            try
            {
                string now = ParamText(ds, FormworkObjects.KindParam);
                var entity = schema == null ? null : ds.GetEntity(schema);
                if (entity == null || !entity.IsValid())
                {
                    seen.Add(new FormworkPlateIntake.Seen(id.Value, false, false, false, now, "", "", "", "", Array.Empty<DropRecord>(), false));
                    continue;
                }
                string uid = entity.Get<string>(UidField) ?? "";
                var records = (entity.Get<string>(RecordsField) ?? "").Split('\n').Select(t => DropRecord.Parse(t.Trim())).OfType<DropRecord>().ToList();
                bool copy = uid.Length == 0 || uid != (ds.UniqueId ?? "");
                string stored = entity.Get<string>(KindField) ?? "";
                // 고칠 수 있는지는 종류가 바뀐 판만 본다(작업공유 상태 읽기 — 판이 많으면 느리다 · P3 검토 낮음 4).
                bool locked = now.Trim() != stored.Trim() && FormulaAutoFill.WhyNotEditable(doc, ds) != null;
                bool values = (entity.Get<string>(ValuesField) ?? "") != ValuesOf(ds);
                seen.Add(new FormworkPlateIntake.Seen(id.Value, true, copy, locked, now, stored, entity.Get<string>(BaseField) ?? "",
                    entity.Get<string>(LeadField) ?? "", entity.Get<string>(KindLeadField) ?? "", records, values));
            }
            catch
            {
                seen.Add(new FormworkPlateIntake.Seen(id.Value, false, false, false, "", "", "", "", "", Array.Empty<DropRecord>(), false));
            }
        }
        return seen;
    }

    /// <summary>모델의 판이 모두 처음 값 기록을 가졌는가(P3 전에 지은 판이 없나 — 지운 판을 셀 수 있나). 판이 없으면 참.</summary>
    public static bool AllRecorded(Document doc)
    {
        var schema = Schema.Lookup(PlateSchemaId);
        var ids = FormworkObjects.Find(doc);
        if (ids.Count == 0) return true;
        if (schema == null) return false;
        foreach (var id in ids)
        {
            try { if (doc.GetElement(id) is not DirectShape ds || ds.GetEntity(schema) is not { } e || !e.IsValid()) return false; }
            catch { return false; }
        }
        return true;
    }

    /// <summary>판 목록을 지금 고칠 수 없는 까닭(null = 고칠 수 있음 — 남이 가짐 · 중앙에서 바뀜: 커밋에서 [완료] 판 짓기 전체가 되돌려지지 않게 미리 본다).</summary>
    public static string? WhyListLocked(Document doc)
    {
        foreach (var holder in ListHolders(doc))
            if (FormulaAutoFill.WhyNotEditable(doc, holder) is { } why) return why;
        return null;
    }

    /// <summary>모델의 판 UniqueId 들(이 애드인이 만든 판 전부).</summary>
    public static HashSet<string> PlateUids(Document doc) =>
        FormworkObjects.Find(doc).Select(id => doc.GetElement(id)?.UniqueId ?? "").Where(u => u.Length > 0).ToHashSet(StringComparer.Ordinal);

    // 판 목록이 든 요소들(만든 차례).
    private static List<DataStorage> ListHolders(Document doc)
    {
        if (Schema.Lookup(ListSchemaId) == null) return new List<DataStorage>();
        return new FilteredElementCollector(doc).OfClass(typeof(DataStorage)).WherePasses(new ExtensibleStorageFilter(ListSchemaId))
            .Cast<DataStorage>().OrderBy(d => d.Id.Value).ToList();
    }

    /// <summary>마지막 [완료]가 지은 판의 UniqueId 들(없으면 null — 목록을 적은 적 없음). 여러 벌이면 적은 시각이 늦은 것. 던지지 않는다.</summary>
    public static HashSet<string>? ReadList(Document doc)
    {
        try
        {
            var schema = Schema.Lookup(ListSchemaId);
            if (schema == null) return null;
            string text = "", at = "";
            bool any = false;
            foreach (var holder in ListHolders(doc))
            {
                var entity = holder.GetEntity(schema);
                if (entity == null || !entity.IsValid()) continue;
                string saved = entity.Get<string>(SavedAtField) ?? "";
                if (any && string.CompareOrdinal(saved, at) < 0) continue;
                (text, at, any) = (entity.Get<string>(UidsField) ?? "", saved, true);
            }
            return any ? text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet(StringComparer.Ordinal) : null;
        }
        catch { return null; }
    }

    /// <summary>판 목록을 적는다(트랜잭션 안 · 요소 하나로 정리).</summary>
    public static void WriteList(Document doc, IEnumerable<string> uids)
    {
        var schema = ListSchema();
        var holders = ListHolders(doc);
        DataStorage holder;
        if (holders.Count == 0)
        {
            holder = DataStorage.Create(doc);
            try { holder.Name = ListName; } catch { /* 이름은 알아보기 쉬우라고 붙일 뿐이다 */ }
        }
        else
        {
            holder = holders[^1];
            if (holders.Count > 1) doc.Delete(holders.Take(holders.Count - 1).Select(h => h.Id).ToList());
        }
        var entity = new Entity(schema);
        entity.Set(UidsField, string.Join("\n", uids));
        entity.Set(SavedAtField, DateTime.UtcNow.ToString("o"));
        entity.Set(VersionField, CurrentVersion);
        holder.SetEntity(entity);
    }

    /// <summary>판 목록을 지운다(트랜잭션 안 — [거푸집 객체 지우기]: 판을 다 지웠으면 «Revit 에서 지운 판» 알림이 뜨지 않게).</summary>
    public static void ClearList(Document doc)
    {
        var holders = ListHolders(doc);
        if (holders.Count > 0) doc.Delete(holders.Select(h => h.Id).ToList());
    }
}
