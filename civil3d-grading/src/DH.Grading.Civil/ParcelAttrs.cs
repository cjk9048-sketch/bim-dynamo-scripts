using Autodesk.AutoCAD.DatabaseServices;
using DH.Grading.Core;

namespace DH.Grading.Civil;

/// <summary>★★[v109.1 · 계획_v109.md 3 · 검토 M3] 필지 속성 XData <c>DH_PARCEL</c> — 필지 선(바깥 고리 · 구멍) · 국공유지 해치 · 지번 글자마다 같은 값 + 자리(역할 · 조각 번호).
/// <para>[3D 보기]가 이것으로 필지를 다시 묶는다(PNU · 조각 · 바깥/구멍). 소유자 이름 칸은 없다(공개 API에 없음 — 공간정보관리법 76조).</para></summary>
internal static class ParcelXData
{
    internal const string App = "DH_PARCEL";
    const string Ver = "v1";

    internal sealed class Info
    {
        public string Role = "", Pnu = "", Jibun = "", Jimok = "", Kind = "", CodeText = "", GosiYm = "", ChgDate = "", Addr = "", Got = "";
        public int Piece = -1, Cnrs = -1;
        public double Ledger = -1, Jiga = -1;
        public string KindName => Kind switch { "pub" => "국공유지", "pri" => "사유지", "mix" => "일부 국공유", _ => "소유 자료 없음" };
    }

    internal static string KindKey(VWorldParcels.OwnKind k) => k switch
    {
        VWorldParcels.OwnKind.Public => "pub", VWorldParcels.OwnKind.Private => "pri", VWorldParcels.OwnKind.Mixed => "mix", _ => "unk",
    };

    static string Cap(string s) => s.Length > 120 ? s.Substring(0, 120) : s;   // XData 글 한 칸 255바이트 — 주소가 길어도

    internal static ResultBuffer Build(VWorldParcels.Parcel p, string role, int piece, string got) => new(
        new TypedValue((int)DxfCode.ExtendedDataRegAppName, App),
        new TypedValue((int)DxfCode.ExtendedDataAsciiString, Ver),
        new TypedValue((int)DxfCode.ExtendedDataAsciiString, role),
        new TypedValue((int)DxfCode.ExtendedDataInteger16, (short)System.Math.Clamp(piece, -1, short.MaxValue)),
        new TypedValue((int)DxfCode.ExtendedDataAsciiString, p.Pnu),
        new TypedValue((int)DxfCode.ExtendedDataAsciiString, p.Jibun),
        new TypedValue((int)DxfCode.ExtendedDataAsciiString, p.Jimok),
        new TypedValue((int)DxfCode.ExtendedDataAsciiString, KindKey(p.Own.Kind)),
        new TypedValue((int)DxfCode.ExtendedDataAsciiString, Cap(p.Own.CodeText)),
        new TypedValue((int)DxfCode.ExtendedDataInteger32, p.Own.Cnrs ?? -1),
        new TypedValue((int)DxfCode.ExtendedDataReal, p.Own.LedgerArea ?? -1.0),
        new TypedValue((int)DxfCode.ExtendedDataReal, p.Jiga ?? -1.0),
        new TypedValue((int)DxfCode.ExtendedDataAsciiString, p.GosiYm),
        new TypedValue((int)DxfCode.ExtendedDataAsciiString, p.Own.ChgDate),
        new TypedValue((int)DxfCode.ExtendedDataAsciiString, Cap(p.Addr)),
        new TypedValue((int)DxfCode.ExtendedDataAsciiString, got));

    /// <summary>객체의 DH_PARCEL — 없거나 판이 다르면 null.</summary>
    internal static Info? Read(DBObject o)
    {
        using var rb = o.GetXDataForApplication(App);
        if (rb == null) return null;
        var v = rb.AsArray();
        if (v.Length < 16 || v[1].Value as string != Ver) return null;
        string S(int i) => v[i].Value as string ?? "";
        try
        {
            return new Info
            {
                Role = S(2), Piece = System.Convert.ToInt32(v[3].Value), Pnu = S(4), Jibun = S(5), Jimok = S(6), Kind = S(7), CodeText = S(8),
                Cnrs = System.Convert.ToInt32(v[9].Value), Ledger = System.Convert.ToDouble(v[10].Value), Jiga = System.Convert.ToDouble(v[11].Value),
                GosiYm = S(12), ChgDate = S(13), Addr = S(14), Got = S(15),
            };
        }
        catch { return null; }
    }
}

/// <summary>★★[v109.1 · 계획_v109.md 3 · 검토 M9] 필지 속성 → Map 3D <b>개체 데이터</b> 표 <c>DH_PARCEL_V1</c>(특성 창 · Map 질의에 보이게).
/// <para><b>따로 클래스</b> — Map 모듈이 없는 판(화면 없는 시험 등)에서 이 클래스를 처음 부르다 터져도 부르는 쪽이 잡고 XData만 남긴다.
/// 칸 이름은 ASCII · 설명은 한글 · 판 이름(V1)이라 칸을 바꾸면 V2로(옛 도면의 표를 안 건드림). 값은 모두 글(모르는 값 = 빈칸 — 0으로 채우면 «0원»처럼 거짓이 된다).</para></summary>
internal sealed class ParcelObjectData
{
    internal const string TableName = "DH_PARCEL_V1";

    static readonly (string Name, string Desc)[] Cols =
    {
        ("PNU", "PNU(필지 고유번호)"), ("JIBUN", "지번"), ("JIMOK", "지목"), ("OWN_KIND", "국공유지 · 사유지"), ("OWN_CODE", "소유 구분"),
        ("SHARE_CNT", "공유인수"), ("LEDGER_M2", "대장면적(㎡)"), ("JIGA_WON", "개별공시지가(원/㎡)"), ("JIGA_YM", "공시 기준년월"),
        ("OWN_CHG", "소유권 변동일"), ("ADDR", "소재지"), ("SOURCE", "출처"), ("GOT", "받은 날"),
    };

    readonly Autodesk.Gis.Map.ObjectData.Table _t;
    ParcelObjectData(Autodesk.Gis.Map.ObjectData.Table t) { _t = t; }

    /// <summary>표를 찾거나 만든다(도면마다).</summary>
    internal static ParcelObjectData Open(Database db)
    {
        var proj = Autodesk.Gis.Map.HostMapApplicationServices.Application.GetProjectForDB(db);
        var tables = proj.ODTables;
        if (!tables.IsTableDefined(TableName))
        {
            var defs = Autodesk.Gis.Map.ObjectData.FieldDefinitions.Create();
            for (int i = 0; i < Cols.Length; i++) defs.Add(Cols[i].Name, Cols[i].Desc, Autodesk.Gis.Map.Constants.DataType.Character, i);
            tables.Add(TableName, defs, "DH 지적(VWorld) 속성", false);
        }
        return new ParcelObjectData(tables[TableName]);
    }

    /// <summary>열어 둔(쓰기) 객체에 한 줄.</summary>
    internal void Attach(DBObject o, VWorldParcels.Parcel p, string got)
    {
        static string N(double? v, string f) => v.HasValue ? v.Value.ToString(f, System.Globalization.CultureInfo.InvariantCulture) : "";
        string[] val =
        {
            p.Pnu, p.Jibun, p.JimokName, VWorldParcels.KindName(p.Own.Kind), p.Own.CodeText,
            p.Own.Cnrs?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "", N(p.Own.LedgerArea, "0.##"), N(p.Jiga, "0"), p.GosiYm,
            p.Own.ChgDate, p.Addr, "VWorld(연속지적 · 토지소유정보)", got,
        };
        var rec = Autodesk.Gis.Map.ObjectData.Record.Create();
        _t.InitRecord(rec);
        for (int i = 0; i < val.Length && i < rec.Count; i++) rec[i].Assign(val[i]);
        _t.AddRecord(rec, o);
    }
}
