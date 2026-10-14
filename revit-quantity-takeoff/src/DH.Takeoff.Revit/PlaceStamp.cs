using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using DH.Takeoff.Core;

namespace DH.Takeoff.Revit;

/// <summary>
/// 부재마다 붙이는 숨은 기록 — "이 부재의 위치(부위)는 「위치·부위 정하기」 창이 넣었거나 확인한 값이다"와 "위치 없음으로 정했다".
/// 부재의 값 자체는 속성(DH_Zone · DH_Part)에 있다. 이 기록은 그 값이 **창 밖에서 들어온 것인지**를 가리는 데 쓴다:
///  · 부재를 복사하면 복사본이 원본의 속성 값을 그대로 들고 온다. Revit 은 이 기록도 같이 복사하므로(문서: "If an Element containing an Entity is
///    copied, the Element copy will also contain a copy of the Entity"), 기록에 **부재의 UniqueId** 를 적어 두고 견준다 — 다르면 복사본이다.
///  · 속성 창에서 손으로 값을 고치면 기록의 값과 달라진다.
/// 어느 쪽이든 팝업은 그 칸을 '창 밖에서 들어온 값 — 확인 필요'로 보여 준다(색이 안 바뀐 부재가 없을 때까지 하려면 이것도 걸려야 한다).
/// 위치와 부위는 **칸마다 따로** 확인한다(ZoneOk · PartOk): 여러 부재에 부위만 넣었을 때 복사본의 옛 위치까지 확인된 것으로 넘어가지 않게.
/// '위치 없음'은 속성으로는 '아직 안 넣음'과 구별할 수 없어(둘 다 빈칸) 여기에 적는다.
/// 읽기는 트랜잭션 없이, 쓰기는 부르는 쪽의 트랜잭션 안에서 한다.
/// 스키마는 한 번 모델에 쓰이면 칸을 바꿀 수 없다(같은 GUID 로 다른 모양을 만들 수 없다) — 나중에 더 담을 것을 위해 빈 글자 칸(Extra)을 하나 둔다.
/// </summary>
public static class PlaceStamp
{
    private static readonly Guid SchemaId = new("7a2b6c10-0000-4a00-9b00-0000000000e3");
    private const string UidField = "Uid", ZoneField = "Zone", PartField = "Part", FreeField = "Free",
                         ZoneOkField = "ZoneOk", PartOkField = "PartOk", ExtraField = "Extra";

    private static Schema GetSchema()
    {
        var schema = Schema.Lookup(SchemaId);
        if (schema != null) return schema;
        var builder = new SchemaBuilder(SchemaId);
        builder.SetSchemaName("DHTakeoffPlaceStamp");
        builder.SetDocumentation("DH Takeoff: marks which of this element's place/part values were set or confirmed in the place-part window");
        builder.SetReadAccessLevel(AccessLevel.Public);
        builder.SetWriteAccessLevel(AccessLevel.Public);
        builder.AddSimpleField(UidField, typeof(string));
        builder.AddSimpleField(ZoneField, typeof(string));
        builder.AddSimpleField(PartField, typeof(string));
        builder.AddSimpleField(FreeField, typeof(bool));
        builder.AddSimpleField(ZoneOkField, typeof(bool));
        builder.AddSimpleField(PartOkField, typeof(bool));
        builder.AddSimpleField(ExtraField, typeof(string));
        return builder.Finish();
    }

    /// <summary>
    /// 부재에 지금 적혀 있는 값: 속성의 위치·부위 + 칸마다 그것이 이 창이 넣은(확인한) 값인지 + '위치 없음'으로 정했는지.
    /// 칸이 확인됨 = 기록이 이 부재의 것이고(UniqueId 가 같다), 그 칸이 확인된 것으로 적혀 있고, 기록의 값이 지금 속성 값과 같다.
    /// 던지지 않는다(기록을 읽지 못하면 확인 안 된 값으로 본다).
    /// </summary>
    public static PickValue ValueOf(Element el)
    {
        string zone = ZoneAssigner.Current(el);
        string part = ZoneScene.Text(el, ZoneScene.PartParam).Trim();
        bool zoneOk = false, partOk = false, free = false;
        try
        {
            var schema = Schema.Lookup(SchemaId);
            var entity = schema == null ? null : el.GetEntity(schema);
            if (entity != null && entity.IsValid())
            {
                string uid = entity.Get<string>(UidField) ?? "";
                if (uid.Length > 0 && uid == (el.UniqueId ?? ""))
                {
                    zoneOk = entity.Get<bool>(ZoneOkField) && (entity.Get<string>(ZoneField) ?? "") == zone;
                    partOk = entity.Get<bool>(PartOkField) && (entity.Get<string>(PartField) ?? "") == part;
                    free = zoneOk && entity.Get<bool>(FreeField);
                }
            }
        }
        catch { zoneOk = partOk = free = false; }
        return PickValue.Of(zone, part, free, zoneOk, partOk);
    }

    /// <summary>
    /// 기록을 값에 맞춘다(트랜잭션 안에서, 속성을 적은 뒤에). 확인된 칸이 하나도 없으면 기록을 뗀다 — 값을 지워 '아직'으로 되돌릴 때 등.
    /// </summary>
    public static void Set(Element el, PickValue value)
    {
        if (!value.ZoneOk && !value.PartOk)
        {
            var existing = Schema.Lookup(SchemaId);
            if (existing != null) el.DeleteEntity(existing);
            return;
        }
        var entity = new Entity(GetSchema());
        entity.Set(UidField, el.UniqueId ?? "");
        entity.Set(ZoneField, value.Zone);
        entity.Set(PartField, value.Part);
        entity.Set(FreeField, value.Free);
        entity.Set(ZoneOkField, value.ZoneOk);
        entity.Set(PartOkField, value.PartOk);
        entity.Set(ExtraField, "");
        el.SetEntity(entity);
    }
}
