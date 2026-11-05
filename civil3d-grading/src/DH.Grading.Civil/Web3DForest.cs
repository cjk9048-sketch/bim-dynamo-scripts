using DH.Grading.Core;
using DH.Grading.Core.Forest;
using NetTopologySuite.Geometries;

namespace DH.Grading.Civil;

/// <summary>★[v115 · JACK 1105 임상도] 수목 설정 — <c>%APPDATA%\DH.Grading\forest.json</c>(vworld.json과 같은 자리 · 시험은 DH_GRADING_KEYS_DIR).
/// <c>{ "on": true, "tiles": "&lt;바탕화면&gt;\임상도_타일", "max": 150000, "density": 1.0, "seen": false }</c> · seen = 창고를 한 번이라도 찾음(L8 — 처음 쓰는 사람에게 ⚠를 안 띄움).</summary>
internal sealed class ForestSettings
{
    public bool On = true;
    public string Tiles = DefaultTiles;
    public int Max = 150_000;
    public double Density = 1.0;
    public bool Seen;

    internal static string DefaultTiles => System.IO.Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.DesktopDirectory), "임상도_타일");
    static string Dir => DhKeys.TestDir ?? System.IO.Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData), "DH.Grading");
    static string PathOf => System.IO.Path.Combine(Dir, "forest.json");
    internal bool IsDefault
    {
        get
        {
            try { return string.Equals(System.IO.Path.GetFullPath(Tiles).TrimEnd('\\'), System.IO.Path.GetFullPath(DefaultTiles).TrimEnd('\\'), System.StringComparison.OrdinalIgnoreCase); }
            catch (System.Exception ex) when (ex is System.ArgumentException or System.NotSupportedException or System.IO.PathTooLongException) { return false; }   // http 주소 · 이상한 글
        }
    }

    internal static ForestSettings Load()
    {
        var s = new ForestSettings();
        try
        {
            if (!System.IO.File.Exists(PathOf)) return s;
            using var doc = System.Text.Json.JsonDocument.Parse(System.IO.File.ReadAllText(PathOf));
            var r = doc.RootElement;
            if (r.TryGetProperty("on", out var on) && (on.ValueKind == System.Text.Json.JsonValueKind.True || on.ValueKind == System.Text.Json.JsonValueKind.False)) s.On = on.GetBoolean();
            if (r.TryGetProperty("tiles", out var t) && t.ValueKind == System.Text.Json.JsonValueKind.String && (t.GetString() ?? "").Trim() is { Length: > 0 } tv) s.Tiles = tv;
            if (r.TryGetProperty("max", out var m) && m.ValueKind == System.Text.Json.JsonValueKind.Number && m.TryGetInt32(out int mx) && mx > 0) s.Max = System.Math.Min(mx, 1_000_000);
            if (r.TryGetProperty("density", out var d) && d.ValueKind == System.Text.Json.JsonValueKind.Number && d.TryGetDouble(out double dv) && dv > 0) s.Density = System.Math.Clamp(dv, 0.1, 2.0);
            if (r.TryGetProperty("seen", out var se) && se.ValueKind == System.Text.Json.JsonValueKind.True) s.Seen = true;
        }
        catch (System.Exception ex) { try { DiagLog.Append("\n[수목] forest.json을 못 읽었습니다(기본값으로) — " + ex.GetType().Name); } catch { } }
        return s;
    }

    internal void Save()
    {
        System.IO.Directory.CreateDirectory(Dir);
        var o = new System.Collections.Generic.Dictionary<string, object> { ["on"] = On, ["tiles"] = Tiles, ["max"] = Max, ["density"] = Density, ["seen"] = Seen };
        System.IO.File.WriteAllText(PathOf, System.Text.Json.JsonSerializer.Serialize(o, new System.Text.Json.JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }), new System.Text.UTF8Encoding(false));
    }
}

/// <summary>★★[v115 · JACK 1105 임상도 · 계획 2판 5절] [3D 보기] 수목 — 창고(또는 도면 폴더 사본)에서 구역을 덮는 칸만 읽어 <see cref="ForestScatter"/>로 심고 H["forest"]를 만든다.
/// <para>실패해도 3D는 뜬다 — 모든 갈래가 머리(missing · quiet)나 경고 한 줄로 끝난다. 사본은 저장된 도면과 같은 폴더의 <c>DH_임상도\</c>에만(쓴 칸만).</para></summary>
internal static class Web3DForest
{
    internal sealed record Terrain(double[] X, double[] Y, int[] F, TerrainMesh.Cls[] Cls, double MinX, double MinY, double MaxX, double MaxY, double CMinX, double CMinY, double CMaxX, double CMaxY);

    static Dictionary<string, object> Missing(string why, bool quiet) => quiet ? new() { ["missing"] = why, ["quiet"] = true } : new() { ["missing"] = why };

    /// <summary>null = 층 없음(꺼짐). 그 밖은 H["forest"]에 넣을 머리.</summary>
    internal static Dictionary<string, object>? Build(Web3DCollect c, Terrain t, IEnumerable<Geometry?> design, IReadOnlyList<Web3DBuildings.Bldg>? bldgs,
                                                     Func<double, double, double?> surfZ, double ox, double oy, Web3DScene sc, System.Text.StringBuilder log)
    {
        var st = ForestSettings.Load();
        if (!st.On) { log.AppendLine("  수목: 꺼짐(기타 설정)"); return null; }
        try
        {
            if (c.Epsg <= 0) { log.AppendLine("  수목: 도면 좌표계 없음"); return Missing("도면 좌표계를 몰라 수목을 못 넣었습니다", false); }
            string? dwgDir = null;
            try { if (System.IO.Path.IsPathRooted(c.DwgKey) && System.IO.File.Exists(c.DwgKey)) dwgDir = System.IO.Path.GetDirectoryName(c.DwgKey); } catch { }
            var pick = ForestSource.Open(st.Tiles, st.IsDefault, st.Seen, dwgDir);
            if (pick.Source == null)
            {
                log.AppendLine($"  수목: 창고 없음 — {pick.Log} · 설정 {(st.IsDefault ? "기본 경로" : "지정 경로")}{(st.Seen ? " · 전에 찾은 적 있음" : "")}");
                if (pick.Warn) c.Warn.Add(pick.Missing + " — 3D에 수목 없음([기타 설정] 임상도 타일)");
                return Missing(pick.Missing, pick.Quiet);
            }
            if (!pick.FromCopy && !st.Seen) { st.Seen = true; try { st.Save(); } catch (System.Exception ex) { log.AppendLine("  수목: 설정 저장 못 함 — " + ex.GetType().Name); } }
            int cm = c.BeltCm; double fn = c.BeltFn;
            (double X, double Y) FromDwg(double x, double y) { var ll = KoreaTm.ToLonLat(x, y, cm, fn); return KoreaTm.UtmkFromLonLat(ll.Lon, ll.Lat); }
            (double X, double Y) ToDwg(double x, double y) { var ll = KoreaTm.UtmkToLonLat(x, y); var p = KoreaTm.FromLonLat(ll.Lon, ll.Lat, cm, fn); return (p.E, p.N); }
            const double margin = 50;
            var env = ForestScatter.Region5179(t.MinX, t.MinY, t.MaxX, t.MaxY, margin, FromDwg);
            var tiles = ForestSource.Load(pick.Source, ForestScatter.TilesFor(env), out var used);
            if (tiles.Count == 0 || tiles.All(q => q.Polys.All(p => p.Frtp < 1 || p.Frtp > 4)))
            {
                log.AppendLine($"  수목: 이 범위에 산림 없음 · 칸 {tiles.Count}({pick.Log})");
                return Missing("이 범위에 산림이 없습니다", true);
            }
            var mask = new ForestMask(t.X, t.Y, t.F, t.Cls, design);
            Func<double, double, bool>? inBldg = null;
            if (bldgs != null && bldgs.Count > 0)
            {
                var gf = NtsSupport.Factory();
                var polys = new List<Geometry>();
                foreach (var b in bldgs)
                    foreach (var p in b.Polys)
                        try
                        {
                            var shell = gf.CreateLinearRing(p.Shell.Append(p.Shell[0]).Select(q => new Coordinate(q.X, q.Y)).ToArray());
                            var holes = p.Holes.Select(h => gf.CreateLinearRing(h.Append(h[0]).Select(q => new Coordinate(q.X, q.Y)).ToArray())).ToArray();
                            Geometry g = gf.CreatePolygon(shell, holes);
                            if (!g.IsValid) g = g.Buffer(0);
                            if (!g.IsEmpty) polys.Add(g.Buffer(1.0));
                        }
                        catch (System.ArgumentException) { }
                if (polys.Count > 0)
                {
                    var idx = new NetTopologySuite.Index.Strtree.STRtree<NetTopologySuite.Geometries.Prepared.IPreparedGeometry>();
                    foreach (var g in polys) idx.Insert(g.EnvelopeInternal, NetTopologySuite.Geometries.Prepared.PreparedGeometryFactory.Prepare(g));
                    idx.Build();
                    inBldg = (x, y) => { var pt = new Point(x, y); foreach (var pg in idx.Query(new Envelope(x, x, y, y))) if (pg.Covers(pt)) return true; return false; };
                }
            }
            var fi = new ForestScatter.Input
            {
                Tiles = tiles, ToDrawing = ToDwg, FromDrawing = FromDwg, X0 = t.MinX, Y0 = t.MinY, X1 = t.MaxX, Y1 = t.MaxY,
                CX0 = t.CMinX, CY0 = t.CMinY, CX1 = t.CMaxX, CY1 = t.CMaxY, SurfZ = surfZ, Design = mask.IsDesign, Building = inBldg,
                Max = st.Max, Density = st.Density, Margin = margin,
            };
            var r = ForestScatter.Run(fi);
            log.AppendLine("  " + r.Text + $" · 창고 {(pick.FromCopy ? "도면 폴더 사본" : "폴더")} · 칸 파일 {used.Count}");
            log.AppendLine($"  수목 자가검증: 표본 {r.SelfN:N0}그루 «밑동 + {fi.Sink} m ↔ SurfZ» 최대 차 {r.SelfDz * 1000:F3} mm" + (r.SelfDz > 1e-6 ? " ⚠" : ""));
            if (r.SelfDz > 1e-6) c.Warn.Add($"수목 자가검증 ⚠ — 밑동 높이가 지형과 {r.SelfDz * 1000:F1} mm 다름");
            if (r.Trees.Count == 0) return Missing("이 범위에 심을 숲 자리가 없습니다", true);
            if (r.Capped) c.Warn.Add($"수목이 많아 {r.Trees.Count:N0}그루만 넣었습니다(한도 {st.Max:N0})");
            // 갱신년도 범위 — 쓴 칸의 원본
            var ids = tiles.SelectMany(q => q.Polys).Select(p => p.Source).ToHashSet();
            var srcs = pick.Source.Index.Sources.Where(s => ids.Contains(s.Id) && s.YearMin > 0).ToList();
            var years = srcs.Count > 0 ? (srcs.Min(s => s.YearMin), srcs.Max(s => s.YearMax)) : (0, 0);
            var head = ForestScatter.Head(sc, r, ox, oy, years);
            // 사본(M5) — 저장된 도면만 · 못 써도 로그 한 줄(수목은 그대로)
            if (dwgDir != null && !pick.FromCopy)
                try { int w = ForestSource.CopyUsed(pick.Source, used, dwgDir); log.AppendLine($"  수목 사본: 도면 폴더 {ForestSource.CopyDir}\\ · 새로 쓴 칸 {w}/{used.Count}"); }
                catch (System.Exception ex) { log.AppendLine("  수목 사본: 못 씀 — " + ex.GetType().Name + " " + ex.Message); }
            else log.AppendLine(dwgDir == null ? "  수목 사본: 저장 안 한 도면 — 안 만듦" : "  수목 사본: 사본에서 읽음 — 안 씀");
            return head;
        }
        catch (System.Exception ex)
        {
            log.AppendLine("  수목: 실패 — " + ex);
            return Missing("수목을 넣지 못했습니다(진단 로그 참고)", false);
        }
    }
}
