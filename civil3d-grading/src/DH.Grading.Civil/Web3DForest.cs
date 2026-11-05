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
    internal sealed record Terrain(double[] X, double[] Y, double[] Z, int[] F, TerrainMesh.Cls[] Cls, double MinX, double MinY, double MaxX, double MaxY, double CMinX, double CMinY, double CMaxX, double CMaxY);

    /// <summary>★[설치 전 검토 M1] 칸 한도(2 km 칸 64개 ≈ 256 ㎢) · 넘으면 바뀐 자리 + 이만큼(m)으로 줄임.</summary>
    internal const int MaxTiles = 64;
    internal const double ShrinkMargin = 2000;

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
            // ★[설치 전 검토 M1(a)] 심는 범위 = 지형 전체 · 칸이 64개(≈ 256 ㎢)를 넘으면 바뀐 자리 + 2 km로 줄임 · 그래도 넘거나 바뀐 자리가 없으면 넣지 않음(⚠)
            //   (멀리 떨어진 TIN 점 하나가 범위를 전국으로 키우는 일 · 서버 원지반 수십 km)
            double rx0 = t.MinX, ry0 = t.MinY, rx1 = t.MaxX, ry1 = t.MaxY;
            var env = ForestScatter.Region5179(rx0, ry0, rx1, ry1, margin, FromDwg);
            var keys = ForestScatter.TilesFor(env).ToList();
            int keys0 = keys.Count;
            if (keys.Count > MaxTiles && double.IsFinite(t.CMinX))
            {
                rx0 = System.Math.Max(t.MinX, t.CMinX - ShrinkMargin); ry0 = System.Math.Max(t.MinY, t.CMinY - ShrinkMargin);
                rx1 = System.Math.Min(t.MaxX, t.CMaxX + ShrinkMargin); ry1 = System.Math.Min(t.MaxY, t.CMaxY + ShrinkMargin);
                env = ForestScatter.Region5179(rx0, ry0, rx1, ry1, margin, FromDwg);
                keys = ForestScatter.TilesFor(env).ToList();
                if (keys.Count <= MaxTiles)
                {
                    c.Warn.Add($"지형이 넓어 수목은 바뀐 자리 + {ShrinkMargin / 1000:0} km 안에만 넣었습니다");
                    log.AppendLine($"  수목: 지형이 넓음(칸 {keys0}) → 바뀐 자리 + {ShrinkMargin:0} m(칸 {keys.Count})");
                }
            }
            if (keys.Count > MaxTiles)
            {
                log.AppendLine($"  수목: 범위가 너무 넓음 — 칸 {keys0}(줄인 뒤 {keys.Count}) · 한도 {MaxTiles}");
                c.Warn.Add($"수목 범위가 너무 넓어 넣지 않았습니다(임상도 칸 {keys0:N0}개 · 한도 {MaxTiles})");
                return Missing("수목 범위가 너무 넓습니다", false);
            }
            // ★[M3] 칸마다 따로 읽음 — 창고에서 못 읽은 칸은 도면 폴더 사본에서 한 번 더
            IForestTileSource? Fallback() => !pick.FromCopy && ForestSource.OpenCopy(dwgDir, out var cp, out _) ? cp : null;
            var tiles = ForestSource.Load(pick.Source, keys, Fallback, out var used, out int failed, out int fromCopy);
            if (failed > 0) c.Warn.Add($"임상도 칸 {failed}개 못 읽음 — 그 자리는 수목 없음");
            if (fromCopy > 0) log.AppendLine($"  수목: 창고에서 못 읽은 칸 {fromCopy}개를 도면 폴더 사본에서 읽음");
            // ★[M2] 사본만으로 읽는데 사본을 만들 때 달라고 안 한 칸 — 숲이 있는지 모른다(빈 자리가 «숲 없음»으로 보이지 않게 한 번 알림)
            if (pick.FromCopy)
            {
                int unknown = keys.Count(k => !pick.Source.Index.Knows(ForestTileFormat.Key(k.Tx, k.Ty)));
                if (unknown > 0) { c.Warn.Add($"도면 폴더 임상도 사본에 없는 칸 {unknown}개 — 그 자리는 수목 없음(창고 폴더 확인)"); log.AppendLine($"  수목: 사본이 모르는 칸 {unknown}/{keys.Count}"); }
            }
            if (tiles.Count == 0 || tiles.All(q => q.Polys.All(p => p.Frtp < 1 || p.Frtp > 4)))
            {
                log.AppendLine($"  수목: 이 범위에 산림 없음 · 칸 {tiles.Count}/{keys.Count}({pick.Log})");
                return failed > 0 ? Missing($"임상도 칸 {failed}개 못 읽음", false) : Missing("이 범위에 산림이 없습니다", true);
            }
            var mask = new ForestMask(t.X, t.Y, t.F, t.Cls, design, t.Z);
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
                        catch (System.Exception bex) when (bex is System.ArgumentException or TopologyException) { }   // ★[검토 L2] 건물 하나가 이상해도 나머지는 뺌
                if (polys.Count > 0)
                {
                    var idx = new NetTopologySuite.Index.Strtree.STRtree<NetTopologySuite.Geometries.Prepared.IPreparedGeometry>();
                    foreach (var g in polys) idx.Insert(g.EnvelopeInternal, NetTopologySuite.Geometries.Prepared.PreparedGeometryFactory.Prepare(g));
                    idx.Build();
                    inBldg = (x, y) => { var pt = new Point(x, y); foreach (var pg in idx.Query(new Envelope(x, x, y, y))) if (pg.Covers(pt)) return true; return false; };
                }
            }
            // ★[검토 L3] 보이는 지형 메시 밖(세모 없음)이면 «면 없음» — SurfZ(정지면 · 원지반 격자)가 메시보다 넓은 자리에 떠 있는 나무가 없게
            double? OnMeshZ(double x, double y) => mask.TriAt(x, y) < 0 ? null : surfZ(x, y);
            var fi = new ForestScatter.Input
            {
                Tiles = tiles, ToDrawing = ToDwg, FromDrawing = FromDwg, X0 = rx0, Y0 = ry0, X1 = rx1, Y1 = ry1,
                CX0 = t.CMinX, CY0 = t.CMinY, CX1 = t.CMaxX, CY1 = t.CMaxY, SurfZ = OnMeshZ, Design = mask.IsDesign, Building = inBldg,
                Max = st.Max, Density = st.Density, Margin = margin, MeshZ = mask.ZAt,
            };
            var r = ForestScatter.Run(fi);
            log.AppendLine("  " + r.Text + $" · 창고 {(pick.FromCopy ? "도면 폴더 사본" : "폴더")} · 칸 파일 {used.Count}/{keys.Count}" + (failed > 0 ? $" · 못 읽은 칸 {failed}" : ""));
            // ★[검토 L8] 자가검증 = 밑동 + 0.3 ↔ 화면에 보이는 지형 메시(세모 보간) — SurfZ(Civil 면 격자)와 메시가 다르면 여기서 드러난다
            log.AppendLine($"  수목 자가검증: 표본 {r.SelfN:N0}그루 «밑동 + {fi.Sink} m ↔ 보이는 지형 메시» 최대 차 {r.SelfDz * 1000:F3} mm" + (r.SelfDz > 1e-3 ? " ⚠" : ""));
            if (r.SelfDz > 1e-3) c.Warn.Add($"수목 자가검증 ⚠ — 밑동 높이가 보이는 지형과 최대 {r.SelfDz * 1000:F1} mm 다름");
            if (r.Trees.Count == 0) return Missing("이 범위에 심을 숲 자리가 없습니다", true);
            if (r.Capped) c.Warn.Add($"수목이 많아 {r.Trees.Count:N0}그루만 넣었습니다(한도 {st.Max:N0})");
            // 갱신년도 범위 — 쓴 칸의 원본
            var ids = tiles.SelectMany(q => q.Polys).Select(p => p.Source).ToHashSet();
            var srcs = pick.Source.Index.Sources.Where(s => ids.Contains(s.Id) && s.YearMin > 0).ToList();
            var years = srcs.Count > 0 ? (srcs.Min(s => s.YearMin), srcs.Max(s => s.YearMax)) : (0, 0);
            var head = ForestScatter.Head(sc, r, ox, oy, years);
            // 사본(M5) — 저장된 도면만 · 못 써도 로그 한 줄(수목은 그대로)
            if (dwgDir != null && !pick.FromCopy)
                try { int w = ForestSource.CopyUsed(pick.Source, keys, used, dwgDir); log.AppendLine($"  수목 사본: 도면 폴더 {ForestSource.CopyDir}\\ · 새로 쓴 칸 {w}/{used.Count} · 달라고 한 칸 {keys.Count}"); }
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
