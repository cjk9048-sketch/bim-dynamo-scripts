using System.Globalization;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using DH.Takeoff.Core;
using DH.Takeoff.Core.View3D;

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
/// Extra 에는 확인한 때의 모양을 적는다(R52b — JACK 2026-10-19 «설계 변경으로 크기나 부피가 바뀌면 다시 설정할 수 있게»):
/// `geo1:v=체적 m³;b=x0,y0,z0,x1,y1,z1`(둘레 상자 m). 읽을 때 지금 모양과 견줘 바뀌었으면 PickValue.Changed — 값과 확인은 그대로 두고 '확인 필요'.
/// 우리 도구(겹침 검사의 결합 · 결합 순서 바꾸기 · 나누기)가 바꾼 모양은 바뀜이 아니다 — 그 트랜잭션 안에서 다시 계산 뒤 · 커밋 앞에 Reshape 로 고쳐 적는다.
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
        bool zoneOk = false, partOk = false, free = false, changed = false;
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
                    // 확인한 칸이 있을 때만 모양을 본다(지킬 확인이 없으면 바뀜도 뜻이 없다). 모양 기록이 없는 옛 기록은 모름 — 바뀜이 아니다.
                    if (zoneOk || partOk) changed = Differs(entity.Get<string>(ExtraField) ?? "", el);
                }
            }
        }
        catch { zoneOk = partOk = free = changed = false; }
        return PickValue.Of(zone, part, free, zoneOk, partOk) with { Changed = changed };
    }

    /// <summary>
    /// 기록을 값에 맞춘다(트랜잭션 안에서, 속성을 적은 뒤에). 확인된 칸이 하나도 없으면 기록을 뗀다 — 값을 지워 '아직'으로 되돌릴 때 등.
    /// 모양 기록은 지금 모양으로 적는다. 다만 value.Changed 면(목록 일 · 되돌리기 · 나누기가 '바뀐' 부재의 기록을 조각에 옮길 때) «바뀜» 표시를 적는다 —
    /// 늘 바뀜으로 읽혀 '확인 필요'가 넘어간다(사람이 넣거나 확인할 때만 풀린다 — R52b core-verify 높음 1 · 중간 3). 이 트랜잭션에서 모양이 바뀌는
    /// 길(나누기)은 다시 계산 뒤에 Reshape 로 고쳐 적는다.
    /// </summary>
    public static void Set(Element el, PickValue value)
    {
        var existing = Schema.Lookup(SchemaId);
        if (!value.ZoneOk && !value.PartOk)
        {
            if (existing != null) el.DeleteEntity(existing);
            return;
        }
        string geo = value.Changed ? GeoTag + Dirty : Measure(el);
        var entity = new Entity(GetSchema());
        entity.Set(UidField, el.UniqueId ?? "");
        entity.Set(ZoneField, value.Zone);
        entity.Set(PartField, value.Part);
        entity.Set(FreeField, value.Free);
        entity.Set(ZoneOkField, value.ZoneOk);
        entity.Set(PartOkField, value.PartOk);
        entity.Set(ExtraField, geo);
        el.SetEntity(entity);
    }

    /// <summary>
    /// 모양 기록만 지금 모양으로 고친다 — 우리 도구가 모양을 바꾼 트랜잭션 안에서, 다시 계산(Regenerate) 뒤 · 커밋 앞에 부른다
    /// (그 도구를 부르기 전에 이 부재가 바뀌지 않았던 때만 — 부르는 쪽이 ValueOf(..).Changed 로 미리 떠 둔다). 기록이 이 부재의 것이 아니거나
    /// 확인한 칸이 없으면 아무것도 하지 않는다. 던지지 않는다(못 고치면 다음에 '모양 바뀜'으로 보일 뿐이다).
    /// </summary>
    public static void Reshape(Element el)
    {
        try
        {
            var schema = Schema.Lookup(SchemaId);
            var entity = schema == null ? null : el.GetEntity(schema);
            if (entity == null || !entity.IsValid()) return;
            if ((entity.Get<string>(UidField) ?? "") != (el.UniqueId ?? "")) return;
            if (!entity.Get<bool>(ZoneOkField) && !entity.Get<bool>(PartOkField)) return;
            string geo = Measure(el);
            if (geo.Length == 0) return;
            entity.Set(ExtraField, geo);
            el.SetEntity(entity);
        }
        catch { }
    }

    // ── 모양 기록 ─────────────────────────────────────────────────────────────────

    private const string GeoTag = "geo1:";
    private const string Dirty = "dirty";      // «바뀜» 표시 — 모양을 재지 않고 늘 바뀜으로 읽힌다
    private const double VolumeRel = 0.01, VolumeAbsM3 = 1e-4, BoxTolM = 0.01;   // 체적 1%(그리고 0.0001 m³ — 다 깎인 부재 · 작은 헌치) · 상자 1 cm
    private const double Ft3ToM3 = 0.3048 * 0.3048 * 0.3048;

    // 지금 모양: 체적(산출식이 쓰는 값 — Revit 속성의 체적, 없으면 입체의 체적)과 둘레 상자. 재지 못하면 "".
    private static string Measure(Element el)
    {
        try
        {
            var bb = el.get_BoundingBox(null);
            if (bb == null) return "";
            double declared = (el.get_Parameter(BuiltInParameter.HOST_VOLUME_COMPUTED)?.AsDouble() ?? 0) * Ft3ToM3;
            double v = declared > 0 ? declared : SolidReader.Solids(el).Sum(s => s.Volume) * Ft3ToM3;
            Vec3 lo = ZoneScene.Meters(bb.Min), hi = ZoneScene.Meters(bb.Max);
            return string.Create(CultureInfo.InvariantCulture,
                $"{GeoTag}v={v:0.######};b={lo.X:0.####},{lo.Y:0.####},{lo.Z:0.####},{hi.X:0.####},{hi.Y:0.####},{hi.Z:0.####}");
        }
        catch { return ""; }
    }

    // 기록한 모양과 지금 모양이 다른가: 체적이 1%(그리고 0.0001 m³) 넘게 · 상자 꼭짓점 좌표 하나라도 1 cm 넘게. 기록을 읽지 못하면 다르지 않다(모름).
    private static bool Differs(string stored, Element el)
    {
        if (stored == GeoTag + Dirty) return true;
        if (!TryParse(stored, out double v0, out double[] b0)) return false;
        if (!TryParse(Measure(el), out double v1, out double[] b1)) return false;
        if (Math.Abs(v1 - v0) > Math.Max(VolumeRel * Math.Max(v0, v1), VolumeAbsM3)) return true;
        for (int i = 0; i < 6; i++) if (Math.Abs(b1[i] - b0[i]) > BoxTolM) return true;
        return false;
    }

    private static bool TryParse(string text, out double volume, out double[] box)
    {
        volume = 0;
        box = Array.Empty<double>();
        if (!text.StartsWith(GeoTag, StringComparison.Ordinal)) return false;
        double v = double.NaN;
        double[]? b = null;
        foreach (var piece in text[GeoTag.Length..].Split(';'))
        {
            if (piece.StartsWith("v=", StringComparison.Ordinal))
                double.TryParse(piece[2..], NumberStyles.Float, CultureInfo.InvariantCulture, out v);
            else if (piece.StartsWith("b=", StringComparison.Ordinal))
            {
                var parts = piece[2..].Split(',');
                if (parts.Length != 6) return false;
                b = new double[6];
                for (int i = 0; i < 6; i++)
                    if (!double.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out b[i])) return false;
            }
        }
        if (double.IsNaN(v) || b == null) return false;
        volume = v;
        box = b;
        return true;
    }
}
