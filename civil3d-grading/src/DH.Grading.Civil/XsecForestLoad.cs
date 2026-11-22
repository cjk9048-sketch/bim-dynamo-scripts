using Autodesk.AutoCAD.DatabaseServices;
using DH.Grading.Core;
using DH.Grading.Core.Forest;

namespace DH.Grading.Civil;

/// <summary>★[JACK 1116 «횡단 벌개재근»] 횡단선들이 걸친 범위의 임상도 입목지 — <see cref="ClearingQuantity"/>와 같은 임상 자료 줄(회사 API → 창고 → 도면 폴더 사본).
/// 못 읽으면 null + 까닭(벌개 칸 «–» · 표 밑 «벌개재근 — 임상도 못 읽음»).</summary>
internal static class XsecForestLoad
{
    internal static ForestSection? Load(Database db, double minX, double minY, double maxX, double maxY, System.Text.StringBuilder log, out string why)
    {
        why = "";
        try
        {
            // ★[JACK 1117 «튜토리얼.dwg 횡단에 임상도 못 읽음 · 3D는 나무 나옴»] 횡단 벌개도 3D · 지적도 · 서버 지표면과 같은 좌표계 찾기 —
            //   도면 좌표계가 없으면 [기타 설정] 원점(옛 = 도면 좌표계만 봐서 지정 안 된 도면은 늘 «좌표계 모름»)
            int epsg = Commands.ImportGisCommand.ResolveEpsg(db, out string csNote);
            log?.AppendLine("  횡단 벌개 좌표계: " + csNote);
            var belt = ShapefileWriter.Belt(epsg);
            if (belt == null) { why = $"좌표계 EPSG:{epsg} 못 씀"; return null; }
            int cm = belt.Value.cm; double fn = belt.Value.fn;
            (double X, double Y) FromDwg(double x, double y) { var ll = KoreaTm.ToLonLat(x, y, cm, fn); return KoreaTm.UtmkFromLonLat(ll.Lon, ll.Lat); }
            (double X, double Y) ToDwg(double x, double y) { var ll = KoreaTm.UtmkToLonLat(x, y); var p = KoreaTm.FromLonLat(ll.Lon, ll.Lat, cm, fn); return (p.E, p.N); }
            var keys = ForestScatter.TilesFor(ForestScatter.Region5179(minX, minY, maxX, maxY, 5, FromDwg)).ToList();
            if (keys.Count > Web3DForest.MaxTiles) { why = $"횡단 범위가 너무 넓음(임상도 칸 {keys.Count})"; return null; }
            string? dwgDir = null;
            try { string f = db.Filename; if (System.IO.Path.IsPathRooted(f) && System.IO.File.Exists(f)) dwgDir = System.IO.Path.GetDirectoryName(f); } catch { }
            var st = ForestSettings.Load();
            var pick = ForestSource.OpenWithApi(DhKeys.Installed.GisApi, keys, st.Tiles, st.IsDefault, st.Seen, dwgDir, s => BusyPopup.Text("벌개재근 — " + s), out string apiNote, out _, db: DhKeys.Installed.GisDb.Ready ? GisDb.LoadForest : null);
            if (apiNote.Length > 0) log?.AppendLine("  벌개재근(횡단): " + apiNote + " → " + (pick.Source != null ? pick.Log : "창고 없음"));
            if (pick.Source == null) { why = "임상도 없음"; return null; }
            var tiles = ForestSource.Load(pick.Source, keys, () => !pick.FromCopy && !pick.FromApi && ForestSource.OpenCopy(dwgDir, out var cp, out _) ? cp : null, out var used, out int failed, out _);
            if (failed > 0) { why = $"임상도 칸 {failed}개 못 읽음"; return null; }
            if (pick.FromCopy && keys.Any(k => !pick.Source.Index.Knows(ForestTileFormat.Key(k.Tx, k.Ty)))) { why = "도면 폴더 임상도 사본에 없는 칸 있음"; return null; }
            var fs = ForestSection.FromTiles(tiles, ToDwg, new NetTopologySuite.Geometries.Envelope(minX, maxX, minY, maxY), NtsSupport.Factory());
            log?.AppendLine($"  벌개재근(횡단) — 임상도 입목지 폴리곤 {fs.Count}개 · {pick.Log}");
            return fs;
        }
        catch (System.Exception ex) { why = "읽기 실패(" + ex.GetType().Name + ")"; return null; }
    }
}
