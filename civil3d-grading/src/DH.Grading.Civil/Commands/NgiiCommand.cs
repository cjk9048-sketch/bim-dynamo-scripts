using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Civil.DatabaseServices;
using DH.Grading.Core;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace DH.Grading.Civil.Commands;

/// <summary>★★★[JACK 0901 "브이월드에서 받은 DXF 수치지도를 인식해서 해당 도엽의 원지반을 생성해 주는 버튼"]
///
/// <para><b>수치지도를 열지 않는다.</b> 도엽 하나에는 도로·건물·수계까지 수만 개가 들어 있는데
/// 우리가 쓰는 것은 <b>등고선과 표고점</b>뿐이다. 파일을 글자로 읽어 그 둘만 뽑아 온다
/// (<see cref="NgiiDxf"/> — 도면 없이 하니스 S94가 <b>실제 도엽으로</b> 검사한다).</para>
///
/// <para>★[JACK] <b>여러 도엽을 한 번에</b> 고를 수 있다. 계획부지가 도엽 경계에 걸치면
/// 두세 장을 같이 골라야 <b>이어진 지표면 하나</b>가 된다.</para>
///
/// <para>★★<b>표고가 0이면 버린다</b>(JACK 지시). 측량이 안 된 자리라 0이 들어간 것이지
/// 해발 0m가 아니다 — 한 가닥만 섞여도 지표면에 절벽이 생기는데 오류는 안 난다.</para>
///
/// <para><b>좌표계는 DXF에 없다</b>(R12에는 담기지 않는다). 정지옵션을 따르되,
/// 좌표 범위가 그 원점에 안 맞으면 <b>말해 준다</b> — 틀린 원점은 조용히 수십 km를 옮긴다.</para></summary>
public sealed class NgiiCommand
{
    // 등고선·표고점이 올라가는 자리는 <b>서버 지표면과 같은 레이어</b>다 —
    // 그래야 [초기화]가 같이 지우고, 다시 불러오면 교체된다(§50).
    // 이름은 ImportGisCommand 한 곳에만 적는다.

    [CommandMethod("DHNGII")]
    public void Run()
    {
        Document doc = AcadApp.DocumentManager.MdiActiveDocument;
        if (doc == null) return;
        GradingSettings.SyncToDocument(doc);
        Editor ed = doc.Editor;
        Database db = doc.Database;

        try
        {
            var files = AskFiles(doc);
            if (files == null || files.Count == 0) { ed.WriteMessage("\n[수치지도] 취소"); return; }

            // ── 읽기 ──────────────────────────────────────────────────────────
            var sheets = new List<NgiiDxf.Sheet>();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            foreach (var f in files)
            {
                var one = NgiiDxf.Read(f, out string why);
                string name = System.IO.Path.GetFileName(f);
                if (why != null)
                {
                    ed.WriteMessage($"\n  ⚠{name} — {why}");
                    continue;
                }
                ed.WriteMessage($"\n  {name} — 등고선 {one.Contours.Count}가닥 · 표고점 {one.Spots.Count}개"
                              + (one.DroppedZeroContours > 0 || one.DroppedZeroSpots > 0
                                 ? $" · 표고0 버림(등고 {one.DroppedZeroContours}·표고점 {one.DroppedZeroSpots})" : ""));
                sheets.Add(one);
            }
            if (sheets.Count == 0)
            {
                Refuse(ed, "읽을 수 있는 도엽이 없습니다.\n\n" +
                           "국토지리정보원 수치지도(DXF)인지, ASCII 형식인지 확인해 주세요.");
                return;
            }

            var map = sheets.Count == 1 ? sheets[0] : NgiiDxf.Merge(sheets);
            int dupSpots = sheets.Count == 1 ? 0 : NgiiDxf.DuplicateSpots(sheets, map);
            if (map.Contours.Count == 0 && map.Spots.Count == 0)
            {
                Refuse(ed, "등고선·표고점이 하나도 없습니다.\n\n" +
                           "표고가 전부 0이었을 수 있습니다(측량 안 된 도엽).");
                return;
            }

            // ── 좌표계 확인 — 틀린 원점은 조용히 수십 km를 옮긴다 ──────────────
            NgiiDxf.Extent(map, out double x0, out double y0, out double x1, out double y1);
            NgiiDxf.ElevRange(map, out double z0, out double z1);
            int epsg = GradingSettings.ExportEpsg;
            string csWarn = BeltCheck(epsg, x0, y0, x1, y1);
            ed.WriteMessage($"\n[수치지도] 좌표계: 정지옵션 EPSG:{epsg}" + (csWarn == null ? "" : "\n  " + csWarn));
            // ★[JACK 0901] 도면에 좌표계가 <b>없을 때만</b> 채운다 — 있으면 안 건드린다.
            //   비워 두면 이 도면이 밖으로 나갔을 때 여기가 어디인지 아무도 모른다.
            try
            {
                var (setIt, csFix) = KoreaCs.AssignIfMissing(db, epsg);
                if (setIt && csFix.Contains("지정")) ed.WriteMessage("\n[수치지도] " + csFix);
            }
            catch { }

            // ── 작도 ──────────────────────────────────────────────────────────
            // ★★<b>등고선과 표고점을 갈라 담는다</b>(검토 0901) — 한 자루에 넣으면
            //   등고선 정의가 점을 조용히 무시해 <b>봉우리·계곡 바닥이 납작해진다</b>.
            var contourIds = new ObjectIdCollection();
            var spotIds = new ObjectIdCollection();
            int nSpot = 0, nDrawn = 0;
            using (var dl = doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                // 다시 불러오면 교체 — 안 지우면 겹겹이 쌓인다.
                ImportGisCommand.EraseOnLayers(db, tr, ImportGisCommand.GroundImportLayers);
                ObjectId layMain = ImportGisCommand.EnsureLayer(db, tr, ImportGisCommand.LayerContour, 8);
                ObjectId layIdx = ImportGisCommand.EnsureLayer(db, tr, ImportGisCommand.LayerContourIndex, 30);
                ObjectId laySpot = ImportGisCommand.EnsureLayer(db, tr, ImportGisCommand.LayerSpot, 2);
                // ★[JACK 0901 "원본 선을 아예 안 그리게"] 지우지 않고 <b>끈다</b> —
                //   지표면이 이 선·점을 자료로 물고 있어 지우면 정의가 끊긴다.
                ImportGisCommand.HideLayer(db, tr, ImportGisCommand.LayerContour);
                ImportGisCommand.HideLayer(db, tr, ImportGisCommand.LayerContourIndex);
                ImportGisCommand.HideLayer(db, tr, ImportGisCommand.LayerSpot);
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);

                foreach (var c in map.Contours)
                {
                    try
                    {
                        using var pts = new Point3dCollection();
                        foreach (var p in c.Pts) pts.Add(new Point3d(p.X, p.Y, p.Z));
                        if (pts.Count < 2) continue;
                        // 닫힌 고리(봉우리·웅덩이)는 닫아 준다 — 안 닫으면 한 변이 빈다.
                        var pl = new Polyline3d(Poly3dType.SimplePoly, pts, c.Closed)
                        {
                            LayerId = c.IsIndex ? layIdx : layMain,
                        };
                        ms.AppendEntity(pl);
                        tr.AddNewlyCreatedDBObject(pl, true);
                        contourIds.Add(pl.ObjectId);
                        nDrawn++;
                    }
                    catch { }
                }
                foreach (var p in map.Spots)
                {
                    try
                    {
                        var pt = new DBPoint(new Point3d(p.X, p.Y, p.Z)) { LayerId = laySpot };
                        ms.AppendEntity(pt);
                        tr.AddNewlyCreatedDBObject(pt, true);
                        spotIds.Add(pt.ObjectId);
                        nSpot++;
                    }
                    catch { }
                }
                tr.Commit();
            }
            ed.WriteMessage($"\n[수치지도] 등고선 {nDrawn}가닥 · 표고점 {nSpot}개 작도"
                          + (nDrawn < map.Contours.Count ? $" ⚠{map.Contours.Count - nDrawn}가닥은 못 그림" : "")
                          + $" ({sw.ElapsedMilliseconds}ms)");

            // ── 원지반 ────────────────────────────────────────────────────────
            //   ★서버 지표면과 <b>같은 이름·같은 스타일</b>이라 이후 공정이 그대로 이어진다.
            string surfNote = ImportGisCommand.BuildGroundSurfaceFrom(db, ed, contourIds, spotIds);
            DrawOrderFix.Apply(db);
            ed.Regen();

            string done = $"도엽 {sheets.Count}장 · 등고선 {nDrawn}가닥 · 표고점 {nSpot}개 · {surfNote}";
            string extra =
                (map.DroppedZeroContours > 0 || map.DroppedZeroSpots > 0
                    ? $"\n표고 0이라 버림 — 등고선 {map.DroppedZeroContours}가닥 · 표고점 {map.DroppedZeroSpots}개" : "")
              + (map.DroppedMixed > 0 ? $"\n표고가 제각각이라 버림 — 등고선 {map.DroppedMixed}가닥" : "")
              + (dupSpots > 0 ? $"\n도엽 경계에서 겹친 표고점 {dupSpots}개 정리" : "")
              + (csWarn == null ? "" : "\n\n⚠ " + csWarn);

            ed.WriteMessage($"\n[수치지도] {done}");
            ZoomTo(doc, x0, y0, x1, y1);
            // ★★★[JACK 0903 "잘되는데 별도 팝업은 안 띄웠으면 좋겠어" · "DXF 가져오기도"]
            //   잘된 일은 <b>화면이 이미 말해 준다</b> — 결과가 그려지고 그 범위로 확대된다.
            //   거기에 확인 단추를 더하면 클릭만 하나 늘 뿐이다.
            //   명령창과 로그에는 그대로 남는다 — 나중에 숫자를 짚을 수 있게.
            //   ※못 한 때와 오류는 그대로 띄운다 — 아무 일도 안 일어난 것을 명령창만으로 알리면 모르고 지나간다.
            try
            {
                DiagLog.Append($"\n■ DHNGII — {done} · 표고 {z0:F1}~{z1:F1}m · EPSG:{epsg}"
                             + $" · 레이어 {string.Join(",", map.ByLayer.Keys)}\n");
            }
            catch { }
        }
        catch (System.Exception ex)
        {
            ed.WriteMessage("\n[수치지도 오류] " + ex.Message);
            AcadApp.ShowAlertDialog("수치지도 읽기 중 오류:\n" + ex.Message);
        }
    }

    /// <summary>DXF를 <b>여러 개</b> 고른다.</summary>
    private static List<string> AskFiles(Document doc)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "수치지도 DXF 선택 (여러 개 고를 수 있습니다)",
            Filter = "수치지도 DXF (*.dxf)|*.dxf|모든 파일 (*.*)|*.*",
            Multiselect = true,
            CheckFileExists = true,
        };
        bool? ok;
        try { ok = dlg.ShowDialog(); }
        catch { ok = false; }
        if (ok != true) return null;
        return new List<string>(dlg.FileNames);
    }

    /// <summary>좌표 범위가 이 원점에 <b>말이 되나</b>. 이상하면 한 줄로 알린다(막지는 않는다).
    /// <para>★DXF(R12)에는 좌표계가 안 담긴다. 틀린 원점을 쓰면 지형이 <b>수십 km 옆</b>에 놓이는데
    /// 숫자는 그럴듯해서 화면만 봐서는 모른다.</para></summary>
    private static string BeltCheck(int epsg, double x0, double y0, double x1, double y1)
    {
        var belt = ShapefileWriter.Belt(epsg);
        if (belt == null) return $"EPSG:{epsg}는 아직 못 다루는 원점입니다 — 정지설정에서 다시 고르세요.";

        // 한국 TM: 원점가산 E=200,000이고 국내는 중앙자오선 ±150km 안이다.
        double cx = (x0 + x1) / 2, cy = (y0 + y1) / 2;
        double offE = Math.Abs(cx - 200000.0);
        if (offE > 200000) return $"가로좌표가 원점에서 {offE / 1000:F0}km 떨어져 있습니다 — 좌표계가 다를 수 있습니다.";

        // 세로좌표로 신(FN 600,000)·구(FN 500,000)를 갈라 본다 — 남한은 원점(38°N) 아래 0~350km.
        double dNew = belt.Value.fn - cy;
        if (dNew < -50000 || dNew > 400000)
        {
            double other = belt.Value.fn == 600000 ? 500000 : 600000;
            double dOther = other - cy;
            if (dOther >= -50000 && dOther <= 400000)
                return $"세로좌표를 보면 원점가산 {other:N0} 쪽입니다 — 정지설정에서 신/구를 바꿔 보세요.";
            return $"세로좌표({cy:N0})가 이 원점과 안 맞습니다 — 정지설정의 좌표계를 확인해 주세요.";
        }
        return null;
    }

    private static void ZoomTo(Document doc, double x0, double y0, double x1, double y1)
    {
        var ed = doc.Editor;
        double w = Math.Max(1.0, x1 - x0), h = Math.Max(1.0, y1 - y0);
        try
        {
            ed.Command("_.ZOOM", "_W",
                new Point3d(x0 - w * 0.05, y0 - h * 0.05, 0),
                new Point3d(x1 + w * 0.05, y1 + h * 0.05, 0));
        }
        catch
        {
            try { ed.Command("_.ZOOM", "_E"); }
            catch { ed.WriteMessage("\n  화면은 못 옮겼습니다 — ZOOM E(범위)로 찾으세요."); }
        }
    }

    private static void Refuse(Editor ed, string msg)
    {
        ed.WriteMessage("\n[수치지도] " + msg.Replace("\n\n", " — ").Replace("\n", " "));
        AcadApp.ShowAlertDialog("수치지도 — 가져오지 못했습니다\n\n" + msg);
    }
}
