using System.Globalization;
using System.IO;
using System.Text;
using Autodesk.Revit.DB;

namespace DH.Takeoff.Revit;

/// <summary>
/// 모델 부재의 DH 매개변수를 읽어 기존 VBA 엑셀이 먹는 CSV(14열, UTF-8 BOM)로 내보낸다.
/// 읽기는 '이름'으로 하되 값이 든 칸을 우선 선택한다(동명 매개변수가 여러 개여도 채워진 값 사용).
/// 치수(길이)는 내부 피트 → m 변환.
/// </summary>
public static class TakeoffExporter
{
    private const double FtToM = 0.3048;

    private static readonly string[] Header =
    {
        "DH_ElementCode", "DH_Class", "DH_Category",
        "L1", "L2", "L3", "W1", "W2", "W3", "H", "ETC",
        "DH_Zone", "DH_Part", "ElementID",
        "DH_Formula",   // 값-포함 산출식(개구부 자동 차감) — 기존 14열 뒤에 덧붙임(옛 VBA 위치 호환)
    };

    /// <summary>DH_ElementCode가 채워진 부재를 CSV 문자열로. (csv, 행수) 반환.</summary>
    public static (string csv, int rows) BuildCsv(Document doc)
    {
        var sb = new StringBuilder();
        sb.Append(string.Join(",", Header)).Append("\r\n");

        int rows = 0;
        var collector = new FilteredElementCollector(doc).WhereElementIsNotElementType();
        foreach (var el in collector)
        {
            string code = Text(el, "DH_ElementCode");
            if (string.IsNullOrWhiteSpace(code)) continue; // 부재코드가 채워진 부재만

            var cells = new[]
            {
                code,
                Text(el, "DH_Class"),
                Text(el, "DH_Category"),
                Num(el, "L1"), Num(el, "L2"), Num(el, "L3"),
                Num(el, "W1"), Num(el, "W2"), Num(el, "W3"),
                Num(el, "H"),
                Num(el, "ETC"),
                Text(el, "DH_Zone"),
                Text(el, "DH_Part"),
                el.Id.Value.ToString(CultureInfo.InvariantCulture),
                Text(el, "DH_Formula"),
            };
            sb.Append(string.Join(",", Array.ConvertAll(cells, Escape))).Append("\r\n");
            rows++;
        }
        return (sb.ToString(), rows);
    }

    /// <summary>UTF-8 BOM으로 저장(엑셀에서 한글 안 깨짐).</summary>
    public static void WriteFile(string path, string csv)
        => File.WriteAllText(path, csv, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

    // --- 읽기 헬퍼: 이름으로 찾되 '값이 든' 칸을 우선 선택 (동명 매개변수 충돌 대응) ---
    private static Parameter? Pick(Element el, string name)
    {
        Parameter? fallback = null;
        foreach (Parameter p in el.GetParameters(name))
        {
            fallback ??= p;
            if (!p.HasValue) continue;
            switch (p.StorageType)
            {
                case StorageType.String:
                    if (!string.IsNullOrEmpty(p.AsString())) return p;
                    break;
                case StorageType.Double:
                    if (p.AsDouble() != 0.0) return p;
                    break;
                default:
                    return p;
            }
        }
        return fallback;
    }

    private static string Text(Element el, string name)
        => Pick(el, name)?.AsString() ?? "";

    /// <summary>
    /// 숫자 읽기 — '단위 없는 숫자' 칸은 입력값 그대로, '길이(Length)' 칸은 내부 피트→m로 환산.
    /// (옛 버전의 Length 칸을 채웠어도 미터로 안전하게 읽힘)
    /// </summary>
    private static string Num(Element el, string name)
    {
        var p = Pick(el, name);
        if (p == null || !p.HasValue || p.StorageType != StorageType.Double)
            return "0";

        double raw = p.AsDouble();
        double val = raw;
        try
        {
            ForgeTypeId spec = p.Definition.GetDataType();
            if (spec != null && spec.TypeId == SpecTypeId.Length.TypeId)
                val = UnitUtils.ConvertFromInternalUnits(raw, UnitTypeId.Meters);
        }
        catch { /* 스펙 조회 실패 시 원값 사용 */ }

        return Math.Round(val, 4).ToString("0.####", CultureInfo.InvariantCulture);
    }

    private static string Escape(string v)
        => (v.Contains(',') || v.Contains('"') || v.Contains('\n'))
            ? "\"" + v.Replace("\"", "\"\"") + "\"" : v;
}
