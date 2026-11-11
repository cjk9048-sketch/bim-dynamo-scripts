using System.Text;
using System.Text.Json;

namespace DH.CivilCoord.Core;

/// <summary>
/// Civil 3D(DH 부지)가 내보낸 «3D 영역» 파일(JSON · format «DH.CivilRegion3D» · version 1) 읽기 — JACK 1111 «박스로 영역을 골라 3D로 볼 수 있게 내보내기 ·
/// Revit 도 3D로 · Civil 지표면을 받아와 Revit 에 뿌리기». 형식은 Civil 터미널과 1111 합의(계획 `.omc/plans/2026-11-11_Revit좌표_손맞춤_3D.md`):
/// box · meshes[{kind, name, layer, color?, verts[[x,y,z]], tris[[i,j,k]]}] · lines[{kind, layer, closed, pts[[x,y,z]]}] · structure{name} · pointsFile.
/// 좌표 = 절대 도면 좌표(X 동 · Y 북 · Z 표고 m). 세모 감기 = 위에서 봐서 반시계(법선 위 — 약속 · 아니어도 그림은 양면).
/// 모양이 틀린 세모 · 선은 버리고 센다(파일 전체를 막지는 않음).
/// </summary>
public sealed class Region3D
{
    public const string Format = "DH.CivilRegion3D";
    public const int MaxVersion = 1;
    /// <summary>읽는 세모 한도(Civil 은 30만에서 막음 — 손으로 만든 큰 파일이 창을 멈추지 않게).</summary>
    public const int MaxTriangles = 1_000_000;

    public sealed class Mesh
    {
        /// <summary>final(최종지표면) · ground · road · wall · bench · struct · other.</summary>
        public string Kind = "final";
        public string Name = "", Layer = "";
        public (byte R, byte G, byte B)? Color;
        /// <summary>꼭짓점 x0,y0,z0, x1,y1,z1 … (m).</summary>
        public double[] V = Array.Empty<double>();
        /// <summary>세모 i0,j0,k0, i1,j1,k1 … (V 의 점 번호).</summary>
        public int[] T = Array.Empty<int>();
        public int VertexCount => V.Length / 3;
        public int TriangleCount => T.Length / 3;
    }

    public sealed class Line
    {
        /// <summary>wall · bench · site · road · slope · other(sketch 와 같은 이름).</summary>
        public string Kind = "other";
        public string Layer = "";
        public bool Closed;
        public readonly List<(double X, double Y, double Z)> Pts = new();
    }

    public int Version;
    public string Name = "", PointsFile = "", Created = "", Addin = "";
    public int? Epsg;
    public (double X0, double Y0, double X1, double Y1)? Box;
    public readonly List<Mesh> Meshes = new();
    public readonly List<Line> Lines = new();
    /// <summary>버린 세모 · 선 개수.</summary>
    public int DroppedTriangles, DroppedLines;

    public int Triangles => Meshes.Sum(m => m.TriangleCount);

    /// <summary>창 한 줄: 세모 12,345 · 선 17.</summary>
    public string Summary => $"세모 {Triangles:N0} · 선 {Lines.Count:N0}";

    /// <summary>세모 · 선 전체의 범위(없으면 null).</summary>
    public (double X0, double Y0, double Z0, double X1, double Y1, double Z1)? Bounds()
    {
        double x0 = double.MaxValue, y0 = double.MaxValue, z0 = double.MaxValue, x1 = double.MinValue, y1 = double.MinValue, z1 = double.MinValue;
        bool any = false;
        void Add(double x, double y, double z)
        {
            any = true;
            if (x < x0) x0 = x; if (x > x1) x1 = x;
            if (y < y0) y0 = y; if (y > y1) y1 = y;
            if (z < z0) z0 = z; if (z > z1) z1 = z;
        }
        foreach (var m in Meshes) for (int i = 0; i + 2 < m.V.Length; i += 3) Add(m.V[i], m.V[i + 1], m.V[i + 2]);
        foreach (var l in Lines) foreach (var p in l.Pts) Add(p.X, p.Y, p.Z);
        return any ? (x0, y0, z0, x1, y1, z1) : null;
    }

    public static Region3D Read(string path, out string? error)
    {
        string text;
        try { text = File.ReadAllText(path); }
        catch (Exception ex) { error = "파일을 못 읽음 — " + ex.Message; return new Region3D(); }
        try { return Parse(text, out error); }
        catch (Exception ex) { error = "파일 꼴이 틀림 — " + ex.Message; return new Region3D(); }
    }

    /// <summary>JSON → 3D 영역. 못 쓰면 error = 까닭(«원인:» 없이 한 줄).</summary>
    public static Region3D Parse(string json, out string? error)
    {
        var r = new Region3D();
        error = null;
        JsonDocument doc;
        try { doc = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip }); }
        catch (JsonException ex) { error = "JSON이 아님 — " + ex.Message; return r; }
        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) { error = "JSON 맨 위가 객체가 아님"; return r; }
            if (CivilPoints.Str(root, "format") != Format) { error = $"Civil 3D 영역 파일이 아님(format ≠ {Format})"; return r; }
            var v = CivilPoints.Num(root, "version");
            if (v is not double vv || vv != Math.Floor(vv) || vv < 1) { error = "version이 없음"; return r; }
            if (vv > MaxVersion) { error = $"파일 판 {vv} — 이 애드인은 {MaxVersion}판까지 읽음(Revit 애드인을 새로)"; return r; }
            r.Version = (int)vv;
            if (!root.TryGetProperty("units", out var units) || units.ValueKind != JsonValueKind.Object) { error = "units가 없음"; return r; }
            string len = CivilPoints.Str(units, "length");
            if (!string.Equals(len, "m", StringComparison.OrdinalIgnoreCase)) { error = $"길이 단위가 m가 아님({(len.Length > 0 ? len : "없음")})"; return r; }
            if (CivilPoints.Num(units, "toMeter") is double tm && Math.Abs(tm - 1) > 1e-12) { error = $"units.toMeter = {tm} — m 파일인데 1이 아님"; return r; }

            if (root.TryGetProperty("structure", out var st) && st.ValueKind == JsonValueKind.Object) r.Name = CivilPoints.Str(st, "name");
            r.PointsFile = CivilPoints.Str(root, "pointsFile");
            r.Created = CivilPoints.Str(root, "created");
            if (root.TryGetProperty("crs", out var crs) && crs.ValueKind == JsonValueKind.Object && CivilPoints.Num(crs, "epsg") is double e && e > 0 && e == Math.Floor(e)) r.Epsg = (int)e;
            if (root.TryGetProperty("app", out var app) && app.ValueKind == JsonValueKind.Object) r.Addin = CivilPoints.Str(app, "addin");
            if (root.TryGetProperty("box", out var bx) && bx.ValueKind == JsonValueKind.Object &&
                CivilPoints.Num(bx, "x0") is double bx0 && CivilPoints.Num(bx, "y0") is double by0 && CivilPoints.Num(bx, "x1") is double bx1 && CivilPoints.Num(bx, "y1") is double by1)
                r.Box = (Math.Min(bx0, bx1), Math.Min(by0, by1), Math.Max(bx0, bx1), Math.Max(by0, by1));

            int total = 0;
            if (root.TryGetProperty("meshes", out var meshes) && meshes.ValueKind == JsonValueKind.Array)
                foreach (var me in meshes.EnumerateArray())
                {
                    if (me.ValueKind != JsonValueKind.Object) continue;
                    var m = new Mesh
                    {
                        Kind = CivilPoints.Str(me, "kind") is { Length: > 0 } k ? k : "other",
                        Name = CivilPoints.Str(me, "name"), Layer = CivilPoints.Str(me, "layer"),
                    };
                    if (me.TryGetProperty("color", out var col) && col.ValueKind == JsonValueKind.Array && col.GetArrayLength() >= 3 &&
                        col[0].ValueKind == JsonValueKind.Number && col[1].ValueKind == JsonValueKind.Number && col[2].ValueKind == JsonValueKind.Number &&
                        col[0].TryGetInt32(out int cr) && col[1].TryGetInt32(out int cg) && col[2].TryGetInt32(out int cb))
                        m.Color = ((byte)Math.Clamp(cr, 0, 255), (byte)Math.Clamp(cg, 0, 255), (byte)Math.Clamp(cb, 0, 255));
                    // 꼭짓점: 틀린 점은 NaN 으로 두고 그 점을 쓰는 세모를 버림(번호가 밀리지 않게)
                    var vs = new List<double>();
                    if (me.TryGetProperty("verts", out var va) && va.ValueKind == JsonValueKind.Array)
                        foreach (var q in va.EnumerateArray())
                        {
                            if (Xyz(q, out double x, out double y, out double z)) { vs.Add(x); vs.Add(y); vs.Add(z); }
                            else { vs.Add(double.NaN); vs.Add(double.NaN); vs.Add(double.NaN); }
                        }
                    m.V = vs.ToArray();
                    int nv = m.VertexCount;
                    var ts = new List<int>();
                    if (me.TryGetProperty("tris", out var ta) && ta.ValueKind == JsonValueKind.Array)
                        foreach (var q in ta.EnumerateArray())
                        {
                            if (q.ValueKind != JsonValueKind.Array || q.GetArrayLength() < 3 ||
                                !Index(q[0], nv, out int a) || !Index(q[1], nv, out int b) || !Index(q[2], nv, out int c) ||
                                a == b || b == c || a == c || double.IsNaN(m.V[a * 3]) || double.IsNaN(m.V[b * 3]) || double.IsNaN(m.V[c * 3]))
                            { r.DroppedTriangles++; continue; }
                            if (total >= MaxTriangles) { r.DroppedTriangles++; continue; }
                            ts.Add(a); ts.Add(b); ts.Add(c);
                            total++;
                        }
                    m.T = ts.ToArray();
                    if (m.TriangleCount > 0) r.Meshes.Add(m);
                }

            if (root.TryGetProperty("lines", out var lines) && lines.ValueKind == JsonValueKind.Array)
                foreach (var ln in lines.EnumerateArray())
                {
                    if (ln.ValueKind != JsonValueKind.Object || !ln.TryGetProperty("pts", out var pa) || pa.ValueKind != JsonValueKind.Array) { r.DroppedLines++; continue; }
                    var line = new Line
                    {
                        Kind = CivilPoints.Str(ln, "kind") is { Length: > 0 } k ? k : "other",
                        Layer = CivilPoints.Str(ln, "layer"), Closed = CivilPoints.Bool(ln, "closed") ?? false,
                    };
                    bool bad = false;
                    foreach (var q in pa.EnumerateArray())
                    {
                        if (!Xyz(q, out double x, out double y, out double z)) { bad = true; break; }
                        line.Pts.Add((x, y, z));
                    }
                    if (bad || line.Pts.Count < 2) { r.DroppedLines++; continue; }
                    r.Lines.Add(line);
                }
        }
        return r;
    }

    private static bool Xyz(JsonElement q, out double x, out double y, out double z)
    {
        x = y = z = 0;
        // TryGetDouble 은 숫자가 아닌 칸에서 예외를 던짐 — 종류부터 봄
        return q.ValueKind == JsonValueKind.Array && q.GetArrayLength() >= 3 &&
               q[0].ValueKind == JsonValueKind.Number && q[1].ValueKind == JsonValueKind.Number && q[2].ValueKind == JsonValueKind.Number &&
               q[0].TryGetDouble(out x) && q[1].TryGetDouble(out y) && q[2].TryGetDouble(out z) &&
               double.IsFinite(x) && double.IsFinite(y) && double.IsFinite(z);
    }

    private static bool Index(JsonElement e, int count, out int i)
    {
        i = -1;
        return e.ValueKind == JsonValueKind.Number && e.TryGetInt32(out i) && i >= 0 && i < count;
    }

    /// <summary>
    /// 파일 맨 위의 «format» 값만 앞부분에서 읽음(큰 3D 파일을 다 읽지 않고 어떤 파일인지 가리기). 못 찾으면 빈 글.
    /// </summary>
    public static string PeekFormat(string path) => PeekString(path, "format");

    /// <summary>파일 맨 위 칸(글) 하나를 앞 64 KB 에서만 읽음 — Civil 은 format · pointsFile 을 세모보다 앞에 씀. 못 찾으면 빈 글.</summary>
    public static string PeekString(string path, string property)
    {
        try
        {
            var buf = new byte[65536];
            int n;
            using (var fs = File.OpenRead(path)) n = fs.Read(buf, 0, buf.Length);
            var reader = new Utf8JsonReader(buf.AsSpan(0, n), isFinalBlock: false, new JsonReaderState(new JsonReaderOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip }));
            // 첫 바이트가 BOM 이면 건너뜀
            if (n >= 3 && buf[0] == 0xEF && buf[1] == 0xBB && buf[2] == 0xBF)
                reader = new Utf8JsonReader(buf.AsSpan(3, n - 3), isFinalBlock: false, new JsonReaderState(new JsonReaderOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip }));
            while (reader.Read())
            {
                if (reader.CurrentDepth == 1 && reader.TokenType == JsonTokenType.PropertyName && reader.ValueTextEquals(property))
                {
                    if (!reader.Read()) return "";
                    return reader.TokenType == JsonTokenType.String ? reader.GetString() ?? "" : "";
                }
                // 맨 위 칸의 값이 큰 배열이면 건너뛰기 — 앞부분만 읽으므로 «format»이 앞에 있어야 찾음(Civil 은 맨 앞에 씀)
            }
        }
        catch { }
        return "";
    }
}

/// <summary>
/// 3D 미리보기에서 돌리거나 끄는 동안 쓸 성긴 지표면(JACK 1111 — Revit 은 WPF 를 소프트웨어로만 그려 세모 30만이면 한 장 0.36 s):
/// 고른 격자 점의 높이를 TIN 에서 읽어(점을 덮는 세모가 여럿이면 — 옹벽 수직면 — 가장 높은 것) 격자 세모로. 손을 떼면 원래 세모로 다시 그린다.
/// </summary>
public static class TerrainLod
{
    /// <summary>세모 수가 target 근처인 격자 메시(꼭짓점 xyz · 세모 번호 · 위에서 봐서 반시계). 못 만들면 빈 것.</summary>
    public static (double[] V, int[] T) Coarse(Region3D.Mesh m, int target)
    {
        int nv = m.VertexCount, nt = m.TriangleCount;
        if (nv < 3 || nt < 1 || target < 2) return (Array.Empty<double>(), Array.Empty<int>());
        double x0 = double.MaxValue, y0 = double.MaxValue, x1 = double.MinValue, y1 = double.MinValue;
        for (int i = 0; i < nv; i++)
        {
            double x = m.V[i * 3], y = m.V[i * 3 + 1];
            if (double.IsNaN(x)) continue;
            if (x < x0) x0 = x; if (x > x1) x1 = x; if (y < y0) y0 = y; if (y > y1) y1 = y;
        }
        double w = x1 - x0, h = y1 - y0;
        if (!(w > 0) || !(h > 0)) return (Array.Empty<double>(), Array.Empty<int>());
        double cell = Math.Sqrt(w * h / (target / 2.0));
        int cx = Math.Max(1, (int)Math.Ceiling(w / cell)), cy = Math.Max(1, (int)Math.Ceiling(h / cell));
        double sx = w / cx, sy = h / cy;

        // 세모를 칸(격자 칸과 같은 크기)에 나눠 담기
        var bins = new List<int>?[cx * cy];
        for (int t = 0; t < nt; t++)
        {
            int a = m.T[t * 3], b = m.T[t * 3 + 1], c = m.T[t * 3 + 2];
            double tx0 = Math.Min(m.V[a * 3], Math.Min(m.V[b * 3], m.V[c * 3])), tx1 = Math.Max(m.V[a * 3], Math.Max(m.V[b * 3], m.V[c * 3]));
            double ty0 = Math.Min(m.V[a * 3 + 1], Math.Min(m.V[b * 3 + 1], m.V[c * 3 + 1])), ty1 = Math.Max(m.V[a * 3 + 1], Math.Max(m.V[b * 3 + 1], m.V[c * 3 + 1]));
            int i0 = Math.Clamp((int)Math.Floor((tx0 - x0) / sx), 0, cx - 1), i1 = Math.Clamp((int)Math.Floor((tx1 - x0) / sx), 0, cx - 1);
            int j0 = Math.Clamp((int)Math.Floor((ty0 - y0) / sy), 0, cy - 1), j1 = Math.Clamp((int)Math.Floor((ty1 - y0) / sy), 0, cy - 1);
            for (int j = j0; j <= j1; j++) for (int i = i0; i <= i1; i++) (bins[j * cx + i] ??= new List<int>()).Add(t);
        }

        // 격자 점 높이
        int gx = cx + 1, gy = cy + 1;
        var z = new double[gx * gy];
        for (int j = 0; j < gy; j++)
            for (int i = 0; i < gx; i++)
            {
                double px = x0 + i * sx, py = y0 + j * sy;
                double best = double.NaN;
                // 격자 점은 칸 네 개((i−1 · i) × (j−1 · j))의 꼭짓점 — 그 칸들에 담긴 세모를 봄
                for (int dj = -1; dj <= 0; dj++)
                    for (int di = -1; di <= 0; di++)
                    {
                        int ii = i + di, jj = j + dj;
                        if (ii < 0 || jj < 0 || ii >= cx || jj >= cy || bins[jj * cx + ii] is not { } list) continue;
                        foreach (int t in list)
                            if (ZAt(m, t, px, py) is double zz && !(zz <= best)) best = zz;
                    }
                z[j * gx + i] = best;
            }

        var vs = new List<double>(gx * gy * 3);
        var map = new int[gx * gy];
        for (int k = 0; k < map.Length; k++)
        {
            if (double.IsNaN(z[k])) { map[k] = -1; continue; }
            map[k] = vs.Count / 3;
            vs.Add(x0 + (k % gx) * sx); vs.Add(y0 + (k / gx) * sy); vs.Add(z[k]);
        }
        var ts = new List<int>(cx * cy * 6);
        for (int j = 0; j < cy; j++)
            for (int i = 0; i < cx; i++)
            {
                int a = map[j * gx + i], b = map[j * gx + i + 1], c = map[(j + 1) * gx + i + 1], d = map[(j + 1) * gx + i];
                if (a >= 0 && b >= 0 && c >= 0) { ts.Add(a); ts.Add(b); ts.Add(c); }
                if (a >= 0 && c >= 0 && d >= 0) { ts.Add(a); ts.Add(c); ts.Add(d); }
            }
        return (vs.ToArray(), ts.ToArray());
    }

    /// <summary>평면 점 (x, y) 가 세모 t 안(경계 포함 · 아주 작은 여유)이면 그 자리 높이.</summary>
    private static double? ZAt(Region3D.Mesh m, int t, double x, double y)
    {
        int a = m.T[t * 3] * 3, b = m.T[t * 3 + 1] * 3, c = m.T[t * 3 + 2] * 3;
        double ax = m.V[a], ay = m.V[a + 1], bx = m.V[b], by = m.V[b + 1], cx = m.V[c], cy = m.V[c + 1];
        double d = (by - cy) * (ax - cx) + (cx - bx) * (ay - cy);
        if (Math.Abs(d) < 1e-12) return null;   // 위에서 보면 선(수직면) — 높이를 못 정함
        double l1 = ((by - cy) * (x - cx) + (cx - bx) * (y - cy)) / d;
        double l2 = ((cy - ay) * (x - cx) + (ax - cx) * (y - cy)) / d;
        double l3 = 1 - l1 - l2;
        const double eps = -1e-9;
        if (l1 < eps || l2 < eps || l3 < eps) return null;
        return l1 * m.V[a + 2] + l2 * m.V[b + 2] + l3 * m.V[c + 2];
    }
}
