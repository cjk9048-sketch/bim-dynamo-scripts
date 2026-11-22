using Autodesk.AutoCAD.DatabaseServices;
using DH.Grading.Core;
using DH.Grading.Core.Forest;

namespace DH.Grading.Civil;

/// <summary>★[v117.19 · JACK 1116 «벌개제근»] 벌개제근 면적(㎡) — 공사 범위(<c>PlanSheet.PlanArea</c> = 정지 경계 ∪ 진입도로 면 자리 = 노리선 안쪽) ∩ 임상도 입목지.
/// 임상 자료 = [3D 보기]와 같은 줄(회사 API → 창고 → 도면 폴더 사본). 못 재면 «-» + 까닭(0이 아님).
/// 임상 · 경급별 값은 <see cref="LastOf"/> 장부에 — 토공량표는 합계 한 줄만(엑셀 수량산출서가 나중에 읽는다).</summary>
internal static class ClearingQuantity
{
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Database, ClearingResult> Ledger = new();

    /// <summary>마지막으로 잰 벌개제근(도면별) — 없으면 null.</summary>
    internal static ClearingResult? LastOf(Database db) => Ledger.TryGetValue(db, out var r) ? r : null;

    internal static ClearingResult Measure(Database db, System.Text.StringBuilder log, List<string> warns)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var r = Measure0(db, log);
        if (!r.Known) warns.Add(r.Note);
        log.AppendLine("  " + (r.Known ? r.Detail() + (r.Note.Length > 0 ? " · " + r.Note : "") : "벌개제근 — " + r.Note) + $" · {sw.ElapsedMilliseconds} ms");
        try { DiagLog.Append("\n[벌개제근] " + (r.Known ? r.Detail() : r.Note)); } catch { }
        Ledger.Remove(db); Ledger.Add(db, r);
        return r;
    }

    static ClearingResult Measure0(Database db, System.Text.StringBuilder log)
    {
        try
        {
            NetTopologySuite.Geometries.Geometry? fp; string pn;
            using (var tr = db.TransactionManager.StartTransaction()) { fp = PlanSheet.PlanArea(db, tr, NtsSupport.Factory(), out pn); tr.Commit(); }
            if (fp == null || fp.IsEmpty) return ClearingResult.None("공사 범위 없음 → 벌개제근 못 셈");
            // ★[JACK 1117 «벌개재근 임상도 못 읽음 원인»] 도면 좌표계만 보던 것을 다른 기능(서버 지표면 · 지적도 · 3D)과 같게 — 도면 좌표계가 없으면 [기타 설정] 원점
            int epsg = Commands.ImportGisCommand.ResolveEpsg(db, out string csNote);
            log.AppendLine("  벌개제근 좌표계: " + csNote);
            var belt = ShapefileWriter.Belt(epsg);
            if (belt == null) return ClearingResult.None($"좌표계 EPSG:{epsg} 못 씀 → 벌개제근 못 셈");
            int cm = belt.Value.cm; double fn = belt.Value.fn;
            (double X, double Y) FromDwg(double x, double y) { var ll = KoreaTm.ToLonLat(x, y, cm, fn); return KoreaTm.UtmkFromLonLat(ll.Lon, ll.Lat); }
            (double X, double Y) ToDwg(double x, double y) { var ll = KoreaTm.UtmkToLonLat(x, y); var p = KoreaTm.FromLonLat(ll.Lon, ll.Lat, cm, fn); return (p.E, p.N); }
            var fe = fp.EnvelopeInternal;
            var keys = ForestScatter.TilesFor(ForestScatter.Region5179(fe.MinX, fe.MinY, fe.MaxX, fe.MaxY, 5, FromDwg)).ToList();
            if (keys.Count > Web3DForest.MaxTiles) return ClearingResult.None($"공사 범위가 너무 넓음(임상도 칸 {keys.Count}) → 벌개제근 못 셈");
            string? dwgDir = null;
            try { string f = db.Filename; if (System.IO.Path.IsPathRooted(f) && System.IO.File.Exists(f)) dwgDir = System.IO.Path.GetDirectoryName(f); } catch { }
            var st = ForestSettings.Load();
            var pick = ForestSource.OpenWithApi(DhKeys.Installed.GisApi, keys, st.Tiles, st.IsDefault, st.Seen, dwgDir, s => BusyPopup.Text("벌개제근 — " + s), out string apiNote, out _, db: DhKeys.Installed.GisDb.Ready ? GisDb.LoadForest : null);
            if (apiNote.Length > 0) log.AppendLine("  벌개제근: " + apiNote + " → " + (pick.Source != null ? pick.Log : "창고 없음"));
            if (pick.Source == null) return ClearingResult.None("임상도 없음 → 벌개제근 못 셈");
            var tiles = ForestSource.Load(pick.Source, keys, () => !pick.FromCopy && !pick.FromApi && ForestSource.OpenCopy(dwgDir, out var cp, out _) ? cp : null, out var used, out int failed, out _);
            if (failed > 0) return ClearingResult.None($"임상도 칸 {failed}개 못 읽음 → 벌개제근 못 셈");
            if (pick.FromCopy && keys.Any(k => !pick.Source.Index.Knows(ForestTileFormat.Key(k.Tx, k.Ty)))) return ClearingResult.None("도면 폴더 임상도 사본에 없는 칸 있음 → 벌개제근 못 셈");
            var r = ForestClearing.Compute(tiles, ToDwg, fp);
            r.Source = pick.Log;
            if (pick.FromApi && dwgDir != null)
                try { ForestSource.CopyUsed(pick.Source, keys, used, dwgDir); } catch (System.Exception ex) { log.AppendLine("  벌개제근: 사본 못 씀 — " + ex.GetType().Name); }
            return r;
        }
        catch (System.Exception ex) { return ClearingResult.None("셈 실패(" + ex.GetType().Name + ") → 벌개제근 못 셈"); }
    }
}
