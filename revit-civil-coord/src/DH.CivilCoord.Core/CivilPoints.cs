using System.Globalization;
using System.Text.Json;

namespace DH.CivilCoord.Core;

/// <summary>
/// Civil 3D(DH 부지 애드인)가 내보낸 «구조물 기준점 파일»(JSON · format «DH.CivilStructPoints» · version 1) 읽기 — JACK 1110 «타이핑 말고 Civil에서 받아오기».
/// 약속(형식)은 `AI\계획_Civil구조물기준점_내보내기.md` 3절. 필수 = format · version · units.length(m) · 점 2개(no 1 · 2) · x · y.
/// 나머지(좌표계 · 도면 · 지반고 …)는 있으면 읽어 보여 준다. check(거리 · 방향)가 있으면 x · y로 다시 셈해 다르면 막는다(손으로 고친 파일).
/// </summary>
public sealed class CivilPoints
{
    public const string Format = "DH.CivilStructPoints";
    public const int MaxVersion = 1;

    public sealed class Pt
    {
        public int No;
        public double X, Y, Z;
        public double? Ground, Final;
        public string GroundSurface = "", FinalSurface = "";
    }

    public int Version;
    public string Name = "", Note = "";
    public string DrawingFile = "";
    public int? Epsg;
    public string CrsName = "", CrsSource = "";
    public int? Insunits;
    public bool UnitAssumed;
    public bool? UcsWasWorld;
    public string Created = "", CivilApp = "", Addin = "";
    public readonly Pt[] Points = { new(), new() };

    /// <summary>개략 평면 선 하나(Civil 도면 WCS · m) — 미리보기용(JACK 1110 «Civil 평면뷰를 개략적으로 같이 내보내 미리보기처럼»).</summary>
    public sealed class SketchLine
    {
        /// <summary>site(계획 부지 경계) · wall(옹벽) · road(도로) · struct(구조물) · bench(소단) · contour(등고선) · other.</summary>
        public string Kind = "other";
        public string Layer = "";
        public bool Closed;
        public readonly List<P2> Pts = new();
    }

    /// <summary>미리보기 선(파일에 «sketch»가 없으면 빈 목록 — 선택 칸).</summary>
    public readonly List<SketchLine> Sketch = new();
    /// <summary>못 읽어 버린 선 · 한도(<see cref="MaxSketchVertices"/>)를 넘어 버린 선 개수.</summary>
    public int SketchDropped;
    public const int MaxSketchVertices = 50000;

    /// <summary>
    /// 짝 3D 영역 파일 이름(«region3d.file» — 같은 폴더 · 없으면 빈 글 · JACK 1111 Civil 과 합의). 경로는 <see cref="Region3DPath"/>.
    /// </summary>
    public string Region3DFile = "";

    /// <summary>짝 3D 영역 파일의 전체 경로(이 기준점 파일 폴더 기준 · 없으면 빈 글). 폴더 밖을 가리키는 이름은 받지 않음.</summary>
    public static string Region3DPath(string pointsPath, string file)
    {
        if (file.Length == 0 || pointsPath.Length == 0) return "";
        string name = Path.GetFileName(file);   // «..\» · 다른 폴더를 가리키지 않게 이름만
        if (name.Length == 0) return "";
        return Path.Combine(Path.GetDirectoryName(pointsPath) ?? "", name);
    }

    /// <summary>창 한 줄: «이름» · 도면 · EPSG.</summary>
    public string Summary
    {
        get
        {
            var parts = new List<string>();
            if (Name.Length > 0) parts.Add($"«{Name}»");
            if (DrawingFile.Length > 0) parts.Add(DrawingFile);
            if (Epsg is int e) parts.Add("EPSG " + e.ToString(CultureInfo.InvariantCulture));
            return string.Join(" · ", parts);
        }
    }

    /// <summary>점 옆 한 줄: 지반 49.850 · 계획 50.200 m(없으면 빈 글).</summary>
    public static string Heights(Pt p)
    {
        var s = new List<string>();
        if (p.Ground is double g) s.Add($"지반 {g:0.000}");
        if (p.Final is double f) s.Add($"계획 {f:0.000}");
        return s.Count == 0 ? "" : string.Join(" · ", s) + " m";
    }

    public static CivilPoints Read(string path, out string? error)
    {
        string text;
        try { text = File.ReadAllText(path); }
        catch (Exception ex) { error = "파일을 못 읽음 — " + ex.Message; return new CivilPoints(); }
        try { return Parse(text, out error); }
        catch (Exception ex) { error = "파일 꼴이 틀림 — " + ex.Message; return new CivilPoints(); }   // 생각 못 한 꼴도 창이 터지지 않게
    }

    /// <summary>JSON → 기준점. 못 쓰면 error = 까닭(«원인:» 없이 한 줄).</summary>
    public static CivilPoints Parse(string json, out string? error)
    {
        var r = new CivilPoints();
        error = null;
        JsonDocument doc;
        try { doc = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip }); }
        catch (JsonException ex) { error = "JSON이 아님 — " + ex.Message; return r; }
        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) { error = "JSON 맨 위가 객체가 아님"; return r; }
            if (Str(root, "format") != Format) { error = $"Civil 기준점 파일이 아님(format ≠ {Format})"; return r; }
            var v = Num(root, "version");
            if (v is not double vv || vv != Math.Floor(vv) || vv < 1) { error = "version이 없음"; return r; }
            // int 로 바꾸기 전에 견줌(1e10 같은 값이 int 로 넘쳐 음수가 되면 판 검사를 지나감 — 설치 전 검토 낮음)
            if (vv > MaxVersion) { error = $"파일 판 {vv} — 이 애드인은 {MaxVersion}판까지 읽음(Revit 애드인을 새로)"; return r; }
            r.Version = (int)vv;

            // 단위: 길이는 m 만(Civil 이 m 로 바꿔 씀 — 약속)
            if (!root.TryGetProperty("units", out var units) || units.ValueKind != JsonValueKind.Object) { error = "units가 없음"; return r; }
            string len = Str(units, "length");
            if (!string.Equals(len, "m", StringComparison.OrdinalIgnoreCase)) { error = $"길이 단위가 m가 아님({(len.Length > 0 ? len : "없음")})"; return r; }
            if (Num(units, "toMeter") is double tm && Math.Abs(tm - 1) > 1e-12) { error = $"units.toMeter = {tm} — m 파일인데 1이 아님"; return r; }

            if (root.TryGetProperty("structure", out var st) && st.ValueKind == JsonValueKind.Object) { r.Name = Str(st, "name"); r.Note = Str(st, "note"); }
            if (root.TryGetProperty("crs", out var crs) && crs.ValueKind == JsonValueKind.Object)
            {
                if (Num(crs, "epsg") is double e && e > 0 && e == Math.Floor(e)) r.Epsg = (int)e;
                r.CrsName = Str(crs, "name"); r.CrsSource = Str(crs, "source");
            }
            if (root.TryGetProperty("drawing", out var dw) && dw.ValueKind == JsonValueKind.Object)
            {
                r.DrawingFile = Str(dw, "file");
                if (Num(dw, "insunits") is double iu) r.Insunits = (int)iu;
                r.UnitAssumed = Bool(dw, "unitAssumed") ?? false;
                r.UcsWasWorld = Bool(dw, "ucsWasWorld");
            }
            if (root.TryGetProperty("app", out var app) && app.ValueKind == JsonValueKind.Object) { r.CivilApp = Str(app, "civil"); r.Addin = Str(app, "addin"); }
            r.Created = Str(root, "created");

            // 점: 정확히 2개 · no 1 · 2
            if (!root.TryGetProperty("points", out var pts) || pts.ValueKind != JsonValueKind.Array) { error = "points가 없음"; return r; }
            if (pts.GetArrayLength() != 2) { error = $"점이 {pts.GetArrayLength()}개 — 2개여야 함"; return r; }
            var got = new Pt?[3];
            int i = 0;
            foreach (var p in pts.EnumerateArray())
            {
                i++;
                if (p.ValueKind != JsonValueKind.Object) { error = $"{i}번째 점이 객체가 아님"; return r; }
                int no = Num(p, "no") is double n && n == Math.Floor(n) ? (int)n : i;
                if (no is < 1 or > 2 || got[no] != null) { error = "점 번호(no)가 1 · 2가 아님"; return r; }
                if (Num(p, "x") is not double x || Num(p, "y") is not double y) { error = $"점 {no}의 x · y가 숫자가 아님"; return r; }
                var pt = new Pt { No = no, X = x, Y = y, Z = Num(p, "z") ?? 0 };
                (pt.Ground, pt.GroundSurface) = Elev(p, "ground");
                (pt.Final, pt.FinalSurface) = Elev(p, "final");
                got[no] = pt;
            }
            r.Points[0] = got[1]!; r.Points[1] = got[2]!;

            // sketch(선택): 미리보기 선 — 모양이 틀린 선은 버리고 셈(파일 전체를 막지는 않음)
            if (root.TryGetProperty("sketch", out var sk) && sk.ValueKind == JsonValueKind.Object &&
                sk.TryGetProperty("lines", out var lines) && lines.ValueKind == JsonValueKind.Array)
            {
                int total = 0;
                foreach (var ln in lines.EnumerateArray())
                {
                    if (ln.ValueKind != JsonValueKind.Object || !ln.TryGetProperty("pts", out var pa) || pa.ValueKind != JsonValueKind.Array) { r.SketchDropped++; continue; }
                    var line = new SketchLine { Kind = Str(ln, "kind") is { Length: > 0 } k ? k : "other", Layer = Str(ln, "layer"), Closed = Bool(ln, "closed") ?? false };
                    bool bad = false;
                    foreach (var q in pa.EnumerateArray())
                    {
                        // TryGetDouble 은 숫자가 아닌 칸에서 예외를 던짐 — 종류부터 봄
                        if (q.ValueKind != JsonValueKind.Array || q.GetArrayLength() < 2 ||
                            q[0].ValueKind != JsonValueKind.Number || q[1].ValueKind != JsonValueKind.Number ||
                            !q[0].TryGetDouble(out double qx) || !q[1].TryGetDouble(out double qy) || !double.IsFinite(qx) || !double.IsFinite(qy)) { bad = true; break; }
                        line.Pts.Add(new P2(qx, qy));
                    }
                    if (bad || line.Pts.Count < 2 || total + line.Pts.Count > MaxSketchVertices) { r.SketchDropped++; continue; }
                    total += line.Pts.Count;
                    r.Sketch.Add(line);
                }
            }

            // region3d(선택): 짝 3D 영역 파일 이름
            if (root.TryGetProperty("region3d", out var rg) && rg.ValueKind == JsonValueKind.Object) r.Region3DFile = Str(rg, "file");

            // check: x · y 로 다시 셈한 거리 · 방향과 같아야(손으로 고친 파일 막기)
            if (root.TryGetProperty("check", out var ck) && ck.ValueKind == JsonValueKind.Object)
            {
                double dx = r.Points[1].X - r.Points[0].X, dy = r.Points[1].Y - r.Points[0].Y;
                double d = Math.Sqrt(dx * dx + dy * dy), b = Math.Atan2(dy, dx) * 180 / Math.PI;
                if (Num(ck, "distance") is double cd && Math.Abs(cd - d) > 0.001)
                { error = $"점 좌표로 셈한 거리 {d:0.000} m가 파일의 check {cd:0.000} m와 다름 — 파일이 손으로 고쳐졌을 수 있음"; return r; }
                if (Num(ck, "bearingDeg") is double cb && d > 1e-9)
                {
                    double diff = Math.Abs(Fit2D.Normalize((cb - b) * Math.PI / 180)) * 180 / Math.PI;
                    if (diff > 0.01) { error = $"점 좌표로 셈한 방향 {b:0.000}°가 파일의 check {cb:0.000}°와 다름 — 파일이 손으로 고쳐졌을 수 있음"; return r; }
                }
            }
        }
        return r;
    }

    private static (double?, string) Elev(JsonElement p, string name)
    {
        if (!p.TryGetProperty(name, out var e) || e.ValueKind != JsonValueKind.Object) return (null, "");
        return (Num(e, "elevation"), Str(e, "surface"));
    }

    internal static string Str(JsonElement o, string name) =>
        o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    internal static bool? Bool(JsonElement o, string name) =>
        o.TryGetProperty(name, out var v) ? v.ValueKind switch { JsonValueKind.True => true, JsonValueKind.False => false, _ => null } : null;

    /// <summary>숫자(또는 숫자 글 «200123.45») · 없거나 null 이면 null.</summary>
    internal static double? Num(JsonElement o, string name)
    {
        if (!o.TryGetProperty(name, out var v)) return null;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out double d) && double.IsFinite(d)) return d;
        if (v.ValueKind == JsonValueKind.String && double.TryParse(v.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double s) && double.IsFinite(s)) return s;
        return null;
    }
}
