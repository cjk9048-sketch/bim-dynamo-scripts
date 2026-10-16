using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using Autodesk.Revit.DB;
using DH.Takeoff.Core;

namespace DH.Takeoff.Revit;

/// <summary>
/// 형상 내보내기 — 대상 부재의 '실제 입체'(면·꼭짓점)와 결합·개구부·매개변수를 JSON으로 적는다.
/// 모델은 건드리지 않는다(읽기 전용). 산출식 자동 분해기를 Revit 없이 실제 형상으로 개발·검산하기 위한 자료.
/// 길이 m, 면적 m², 체적 m³.
/// </summary>
public static class GeometryDumper
{
    private const double FtToM = 0.3048;
    private const double Ft2ToM2 = FtToM * FtToM;
    private const double Ft3ToM3 = FtToM * FtToM * FtToM;

    public static string Dump(Document doc, ICollection<ElementId> ids)
    {
        string dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DH.Takeoff", "dump");
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, SafeName(doc.Title) + "_geometry.json");

        int count = 0, noSolid = 0, failed = 0;
        using (var fs = File.Create(path))
        using (var w = new Utf8JsonWriter(fs, WriterOptions))
        {
            w.WriteStartObject();
            w.WriteString("document", doc.Title);
            w.WriteString("path", doc.PathName);
            w.WriteString("dumpedAt", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            w.WriteString("units", "m, m2, m3");

            w.WriteStartArray("elements");
            foreach (var id in ids)
            {
                var el = doc.GetElement(id);
                if (el == null) continue;
                bool hasSolid = false;
                if (Buffered(w, id.Value, ew => hasSolid = WriteElement(ew, doc, el)))
                {
                    if (!hasSolid) noSolid++;
                    count++;
                }
                else failed++;
            }
            w.WriteEndArray();

            w.WriteStartArray("openings");
            foreach (var op in new FilteredElementCollector(doc).OfClass(typeof(Opening)).Cast<Opening>())
            {
                if (!Buffered(w, op.Id.Value, ew => WriteOpening(ew, op))) failed++;
            }
            w.WriteEndArray();

            w.WriteEndObject();
        }

        return $"형상 내보내기 완료.\n  • 부재: {count}개 (입체 없음 {noSolid}개, 읽기 실패 {failed}개)\n  • 파일: {path}";
    }

    private static readonly JsonWriterOptions WriterOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>
    /// 항목 하나를 따로 적은 뒤 통째로 붙인다 — 읽다가 멈춘 항목이 파일 전체를 깨지 않게.
    /// 실패하면 그 자리에 {id, error} 를 남기고 false.
    /// </summary>
    private static bool Buffered(Utf8JsonWriter w, long id, Action<Utf8JsonWriter> write)
    {
        try
        {
            using var ms = new MemoryStream();
            using (var ew = new Utf8JsonWriter(ms, WriterOptions)) write(ew);
            w.WriteRawValue(ms.ToArray()); // 온전한 JSON 인지 확인한 뒤 붙는다
            return true;
        }
        catch (Exception ex)
        {
            w.WriteStartObject();
            w.WriteNumber("id", id);
            w.WriteString("error", ex.GetType().Name + ": " + ex.Message);
            w.WriteEndObject();
            return false;
        }
    }

    private static bool WriteElement(Utf8JsonWriter w, Document doc, Element el)
    {
        var solids = SolidReader.Read(el);

        w.WriteStartObject();
        w.WriteNumber("id", el.Id.Value);
        w.WriteString("category", el.Category?.Name ?? "");
        w.WriteString("class", el.GetType().Name);
        w.WriteString("name", el.Name);
        var type = doc.GetElement(el.GetTypeId()) as ElementType;
        w.WriteString("family", type?.FamilyName ?? "");
        w.WriteString("type", type?.Name ?? "");
        w.WriteString("mark", FormworkReader.MarkOf(el));   // «마크»(도면 부호 — 거푸집 엑셀 줄의 부호 · R47). 공유 · 프로젝트 칸(params)에는 들지 않는다
        if (el is FamilyInstance fi) w.WriteBoolean("inPlace", fi.Symbol?.Family?.IsInPlace ?? false);

        var vp = el.get_Parameter(BuiltInParameter.HOST_VOLUME_COMPUTED);
        if (vp != null && vp.HasValue) w.WriteNumber("volumeParam", R(vp.AsDouble() * Ft3ToM3, 6));

        var bb = el.get_BoundingBox(null);
        if (bb != null) { WritePoint(w, "bbMin", bb.Min); WritePoint(w, "bbMax", bb.Max); }

        // 산출식 자동입력이 분해기에 같이 넘기는 두께(벽·바닥, 그 외 0)
        w.WriteNumber("thickness", R(SolidReader.Thickness(el), 9));

        if (el is Wall wall)
        {
            w.WriteStartObject("wall");
            w.WriteNumber("width", R(wall.Width * FtToM, 5));
            w.WriteBoolean("profileEdited", wall.SketchId != ElementId.InvalidElementId);
            if ((wall.Location as LocationCurve)?.Curve is Curve c)
            {
                w.WriteString("curve", c.GetType().Name);
                WritePoint(w, "start", c.GetEndPoint(0));
                WritePoint(w, "end", c.GetEndPoint(1));
            }
            w.WriteEndObject();
        }

        // 결합·끼움 조회가 어떤 부재에서 오류를 내도 그 부재의 입체까지 잃지 않게, 목록을 먼저 따로 얻는다.
        ICollection<ElementId> joinedIds;
        try { joinedIds = JoinGeometryUtils.GetJoinedElements(doc, el); }
        catch { joinedIds = Array.Empty<ElementId>(); }
        IList<ElementId> insertIds = Array.Empty<ElementId>();
        if (el is HostObject hostObj)
        {
            try { insertIds = hostObj.FindInserts(true, true, true, true); }
            catch { insertIds = Array.Empty<ElementId>(); }
        }

        // 결합: 상대 부재와 '누가 깎는가'
        w.WriteStartArray("joined");
        foreach (var jid in joinedIds)
        {
            var other = doc.GetElement(jid);
            if (other == null) continue;
            w.WriteStartObject();
            w.WriteNumber("id", jid.Value);
            try { w.WriteBoolean("thisCuts", JoinGeometryUtils.IsCuttingElementInJoin(doc, el, other)); } catch { }
            w.WriteEndObject();
        }
        w.WriteEndArray();

        // 끼움(문·창·개구부 등)
        if (el is HostObject)
        {
            w.WriteStartArray("inserts");
            foreach (var iid in insertIds)
            {
                var ins = doc.GetElement(iid);
                if (ins == null) continue;
                w.WriteStartObject();
                w.WriteNumber("id", iid.Value);
                w.WriteString("category", ins.Category?.Name ?? "");
                w.WriteString("class", ins.GetType().Name);
                w.WriteString("name", ins.Name);
                var ibb = ins.get_BoundingBox(null);
                if (ibb != null) { WritePoint(w, "bbMin", ibb.Min); WritePoint(w, "bbMax", ibb.Max); }
                w.WriteEndObject();
            }
            w.WriteEndArray();
        }

        // 사용자(공유·프로젝트) 매개변수 — 이미 들어 있는 칸과 값을 본다
        w.WriteStartObject("params");
        var seen = new HashSet<string>();
        foreach (Parameter p in el.Parameters)
        {
            if (!(p.IsShared || p.Id.Value > 0)) continue;
            string name = p.Definition?.Name ?? "";
            if (name.Length == 0 || !seen.Add(name)) continue;
            string? val = p.StorageType == StorageType.String ? p.AsString() : p.AsValueString();
            w.WriteString(name, val ?? "");
        }
        w.WriteEndObject();

        w.WriteStartArray("solids");
        foreach (var s in solids) WriteSolid(w, s);
        w.WriteEndArray();

        w.WriteEndObject();
        return solids.Count > 0;
    }

    private static void WriteSolid(Utf8JsonWriter w, ShapeSolid s)
    {
        w.WriteStartObject();
        w.WriteNumber("volume", R(s.Volume, 9));
        w.WriteNumber("area", R(s.Area, 9));
        w.WriteStartArray("faces");
        foreach (var f in s.Faces)
        {
            w.WriteStartObject();
            w.WriteString("kind", f.Kind);
            w.WriteNumber("area", R(f.Area, 9));
            // 분해기가 받는 이름 그대로 적는다(원통 면은 이 칸에 축 방향이 들어 있다).
            if (f.Normal != null) WriteTriple(w, "normal", f.Normal);
            if (f.Origin != null) WriteTriple(w, "origin", f.Origin);
            w.WriteStartArray("loops");
            foreach (var loop in f.Loops)
            {
                w.WriteStartObject();
                w.WriteBoolean("curved", loop.Curved);
                w.WriteStartArray("pts");
                foreach (var p in loop.Pts)
                {
                    w.WriteStartArray();
                    foreach (double v in p) w.WriteNumberValue(R(v, 9));
                    w.WriteEndArray();
                }
                w.WriteEndArray();
                w.WriteStartArray("edges");
                foreach (var e in loop.Edges)
                {
                    w.WriteStartObject();
                    w.WriteString("kind", e.Kind);
                    WriteTriple(w, "start", e.Start);
                    WriteTriple(w, "end", e.End);
                    if (e.Center != null) WriteTriple(w, "center", e.Center);
                    if (e.Mid != null) WriteTriple(w, "mid", e.Mid);
                    if (e.Radius > 0) w.WriteNumber("radius", R(e.Radius, 9));
                    w.WriteEndObject();
                }
                w.WriteEndArray();
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteEndObject();
        }
        w.WriteEndArray();
        w.WriteEndObject();
    }

    private static void WriteTriple(Utf8JsonWriter w, string name, double[] v)
    {
        w.WriteStartArray(name);
        foreach (double x in v) w.WriteNumberValue(R(x, 9));
        w.WriteEndArray();
    }

    private static void WriteOpening(Utf8JsonWriter w, Opening op)
    {
        w.WriteStartObject();
        w.WriteNumber("id", op.Id.Value);
        w.WriteString("category", op.Category?.Name ?? "");
        w.WriteNumber("host", op.Host?.Id.Value ?? -1);
        var bb = op.get_BoundingBox(null);
        if (bb != null) { WritePoint(w, "bbMin", bb.Min); WritePoint(w, "bbMax", bb.Max); }
        w.WriteBoolean("rect", op.IsRectBoundary);
        w.WriteStartArray("boundary");
        if (op.IsRectBoundary)
        {
            foreach (var p in op.BoundaryRect) WritePointValue(w, p);
        }
        else
        {
            foreach (Curve c in op.BoundaryCurves)
            {
                var tess = c.Tessellate();
                for (int i = 0; i < tess.Count - 1; i++) WritePointValue(w, tess[i]);
            }
        }
        w.WriteEndArray();
        w.WriteEndObject();
    }

    private static void WritePoint(Utf8JsonWriter w, string name, XYZ p)
    {
        w.WritePropertyName(name);
        WritePointValue(w, p);
    }

    private static void WritePointValue(Utf8JsonWriter w, XYZ p)
    {
        w.WriteStartArray();
        w.WriteNumberValue(R(p.X * FtToM, 5));
        w.WriteNumberValue(R(p.Y * FtToM, 5));
        w.WriteNumberValue(R(p.Z * FtToM, 5));
        w.WriteEndArray();
    }

    private static double R(double v, int digits) => Math.Round(v, digits);

    private static string SafeName(string s)
    {
        foreach (char c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
        return s;
    }
}
