using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace DH.Grading.Core;

/// <summary>★★[v116.9 · JACK 1110 «구조물 좌표계 내보내기»] Civil → Revit 애드인(DH.CivilCoord) «구조물 기준점 파일» — 순수 함수(하네스 S242).
/// 형식 = 약속 «DH.CivilStructPoints» v1(계획_Civil구조물기준점_내보내기.md §3 — 바꾸면 version 올리고 Revit 쪽과 함께).
/// 좌표는 도면 WCS를 m로 · 숫자는 InvariantCulture 왕복 값 · UTF-8(한글 그대로) · 모르는 칸은 빈 글 · null(지어내지 않음).</summary>
public static class StructPoints
{
    public const string Format = "DH.CivilStructPoints";
    public const int FormatVersion = 1;
    /// <summary>두 점 최소 거리(m) — Revit 쪽 규칙과 같음.</summary>
    public const double MinDist = 1.0;

    /// <summary>찍은 점 하나(m · WCS) · 그 XY의 원지반 · 최종 지표면 표고(면이 없거나 면 밖이면 null).</summary>
    public sealed record Pt(double X, double Y, double Z, string GroundName, double? Ground, string FinalName, double? Final);

    public sealed record Info(string Name, int Epsg, string CivilCode, string CrsSource, string File, string Path,
                              int Insunits, bool UcsWasWorld, string Civil, string Addin, DateTimeOffset Created);

    /// <summary>INSUNITS → m 계수. 0(단위 없음) = m로 봄(<paramref name="assumed"/>). 표에 없는 단위(마일 · 킬로미터 · 마이크로 …)는 거짓 — 지어낸 값으로 안 씀.</summary>
    public static bool TryToMeter(int insunits, out double f, out bool assumed)
    {
        assumed = insunits == 0;
        f = insunits switch { 0 => 1.0, 1 => 0.0254, 2 => 0.3048, 4 => 0.001, 5 => 0.01, 6 => 1.0, 14 => 0.1, _ => double.NaN };
        return !double.IsNaN(f);
    }

    public static string UnitName(int insunits) => insunits switch
    {
        0 => "Unitless", 1 => "Inches", 2 => "Feet", 4 => "Millimeters", 5 => "Centimeters", 6 => "Meters", 14 => "Decimeters", _ => "INSUNITS " + insunits,
    };

    /// <summary>EPSG 이름(한국 좌표계만 — 모르면 빈 글).</summary>
    public static string EpsgName(int epsg) => epsg switch
    {
        5179 => "Korea 2000 / Unified CS",
        5180 => "Korea 2000 / West Belt", 5181 => "Korea 2000 / Central Belt", 5182 => "Korea 2000 / Central Belt Jeju",
        5183 => "Korea 2000 / East Belt", 5184 => "Korea 2000 / East Sea Belt",
        5185 => "Korea 2000 / West Belt 2010", 5186 => "Korea 2000 / Central Belt 2010",
        5187 => "Korea 2000 / East Belt 2010", 5188 => "Korea 2000 / East Sea Belt 2010",
        _ => "",
    };

    /// <summary>두 점 평면 거리(m) · 점1→점2 방향(+X에서 반시계 °).</summary>
    public static (double Dist, double BearingDeg) Check(Pt p1, Pt p2)
    {
        double dx = p2.X - p1.X, dy = p2.Y - p1.Y;
        return (Math.Sqrt(dx * dx + dy * dy), Math.Atan2(dy, dx) * 180 / Math.PI);
    }

    /// <summary>파일 이름에 못 쓰는 글자를 «_»로(빈 이름이면 «구조물»).</summary>
    public static string SafeFileName(string name)
    {
        var bad = new HashSet<char>(System.IO.Path.GetInvalidFileNameChars()) { '\\', '/', ':', '*', '?', '"', '<', '>', '|' };
        var s = new string((name ?? "").Trim().Select(c => bad.Contains(c) || char.IsControl(c) ? '_' : c).ToArray()).Trim(' ', '.');
        return s.Length == 0 ? "구조물" : s;
    }

    /// <summary>JSON 글(UTF-8로 쓸 것). 점은 정확히 둘(찍은 순서).</summary>
    public static string Json(Info i, Pt p1, Pt p2)
    {
        TryToMeter(i.Insunits, out _, out bool assumed);
        var (dist, brg) = Check(p1, p2);
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            w.WriteStartObject();
            w.WriteString("format", Format);
            w.WriteNumber("version", FormatVersion);
            w.WriteStartObject("structure"); w.WriteString("name", i.Name); w.WriteString("note", ""); w.WriteEndObject();
            w.WriteStartObject("units"); w.WriteString("length", "m"); w.WriteNumber("toMeter", 1.0); w.WriteString("angle", "deg"); w.WriteEndObject();
            w.WriteString("axes", "X=동(Easting) · Y=북(Northing) · Z=표고 — 도면 WCS");
            w.WriteStartObject("crs");
            if (i.Epsg > 0) w.WriteNumber("epsg", i.Epsg); else w.WriteNull("epsg");
            w.WriteString("name", i.Epsg > 0 ? EpsgName(i.Epsg) : "");
            w.WriteString("civilCode", i.CivilCode ?? "");
            w.WriteString("source", i.CrsSource ?? "none");
            w.WriteEndObject();
            w.WriteStartObject("drawing");
            w.WriteString("file", i.File ?? ""); w.WriteString("path", i.Path ?? "");
            w.WriteNumber("insunits", i.Insunits); w.WriteString("insunitsName", UnitName(i.Insunits));
            w.WriteBoolean("unitAssumed", assumed); w.WriteBoolean("ucsWasWorld", i.UcsWasWorld);
            w.WriteEndObject();
            w.WriteStartObject("app"); w.WriteString("civil", i.Civil ?? ""); w.WriteString("addin", i.Addin ?? ""); w.WriteEndObject();
            w.WriteString("created", i.Created.ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture));
            w.WriteStartArray("points");
            int no = 0;
            foreach (var p in new[] { p1, p2 })
            {
                w.WriteStartObject();
                w.WriteNumber("no", ++no);
                w.WriteNumber("x", p.X); w.WriteNumber("y", p.Y); w.WriteNumber("z", p.Z);
                Surf("ground", p.GroundName, p.Ground); Surf("final", p.FinalName, p.Final);
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteStartObject("check"); w.WriteNumber("distance", Math.Round(dist, 6)); w.WriteNumber("bearingDeg", Math.Round(brg, 6)); w.WriteEndObject();
            w.WriteEndObject();

            void Surf(string key, string nm, double? z)
            {
                if (z is not double v || !double.IsFinite(v) || string.IsNullOrEmpty(nm)) { w.WriteNull(key); return; }
                w.WriteStartObject(key); w.WriteString("surface", nm); w.WriteNumber("elevation", Math.Round(v, 4)); w.WriteEndObject();
            }
        }
        return System.Text.Encoding.UTF8.GetString(ms.ToArray());
    }
}
