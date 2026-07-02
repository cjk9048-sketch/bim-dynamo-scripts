using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.Civil.DatabaseServices;
using DH.Grading.Core;
using AcadEntity = Autodesk.AutoCAD.DatabaseServices.Entity;

namespace DH.Grading.Civil;

/// <summary>
/// Civil3D TIN 빌더 — 오버사이즈 가상 사면 TIN 생성(계단 링 = 브레이크라인)과
/// 시각화(daylight 초록선·노리선/소단선). 순수 기하는 Core.GradingGeometry가 담당.
/// </summary>
public static class GradingBuilder
{
    /// <summary>직전 BuildVirtualSlope의 TIN 실측 검증 결과 — 의도한 링 Z와 실제 TIN 표고 대조(진단로그용).</summary>
    public static string LastVerify { get; private set; } = "";

    /// <summary>오버사이즈 가상 사면 TIN — 계단 링을 Standard 브레이크라인으로(동심 비교차 → 톱니 0).
    /// cornerLines(코너 능선)를 주면 열린 브레이크라인으로 추가 — 코너 모따기(사선) 방지(직각 모드).</summary>
    public static ObjectId BuildVirtualSlope(Database db, Transaction tr, IReadOnlyList<List<Point3>> rings, string name,
        IReadOnlyList<List<Point3>>? cornerLines = null)
    {
        // [재실행 정리] 같은 이름(및 _2, _3… 번호 변형)의 옛 DH 가상면을 먼저 삭제 — 실행마다 쌓여
        // 옛 표면을 보고 "안 생겼다"고 오인하는 혼란 방지(JACK). 항상 최신 하나만 남는다.
        EraseSurfacesByBaseName(tr, name);
        ObjectId id = TinSurface.Create(db, UniqueName(db, tr, name));
        var tin = (TinSurface)tr.GetObject(id, OpenMode.ForWrite);
        foreach (var ring in rings) AddRingBreakline(tin, ring);
        int intended = rings.Count;
        if (cornerLines != null)
        {
            foreach (var cl in cornerLines) AddOpenBreakline(tin, cl);
            intended += cornerLines.Count;
        }
        tin.Rebuild();

        // [TIN 실측 검증] 의도한 링 점 Z vs 실제 TIN 표고 — 불일치가 어느 방향에 몰렸는지 기록(비대칭 원인 추적).
        var vb = new System.Text.StringBuilder();
        try
        {
            int defCount = -1;
            try { defCount = tin.BreaklinesDefinition.Count; } catch { }
            vb.AppendLine($"  브레이크라인 의도 {intended} / 정의됨 {defCount}");
            // 부지 중심(첫 링 평균)
            double cx = 0, cy = 0; int cn = 0;
            foreach (var pt in rings[0]) { cx += pt.X; cy += pt.Y; cn++; }
            cx /= Math.Max(cn, 1); cy /= Math.Max(cn, 1);
            for (int r = 0; r < rings.Count; r++)
            {
                var ring = rings[r];
                int sample = 0, bad = 0;
                int e = 0, w = 0, n2 = 0, s2 = 0;
                double maxErr = 0;
                for (int i = 0; i < ring.Count; i += 5) // 5점 간격 표본
                {
                    var pt = ring[i];
                    double zTin;
                    try { zTin = tin.FindElevationAtXY(pt.X, pt.Y); } catch { continue; }
                    sample++;
                    double err = Math.Abs(zTin - pt.Z);
                    if (err > 0.05)
                    {
                        bad++;
                        if (err > maxErr) maxErr = err;
                        double dx = pt.X - cx, dy = pt.Y - cy;
                        if (Math.Abs(dx) >= Math.Abs(dy)) { if (dx > 0) e++; else w++; }
                        else { if (dy > 0) n2++; else s2++; }
                    }
                }
                if (bad > 0)
                    vb.AppendLine($"  링{r}: 표본 {sample} 중 불일치 {bad} (동{e}/서{w}/북{n2}/남{s2}) 최대오차 {maxErr:F2}m");
            }
            // [격자 탐침] ①부지 내부 6×6 ②계단 전체(최외곽 링 bbox) 16×16 — TIN 실측 Z 숫자 지도.
            // '어느 쪽이 안 생겼나'를 스샷 없이 수치로 직접 포착(비대칭 원인 추적).
            void Grid(string title, IReadOnlyList<Point3> extent, int nDiv)
            {
                double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
                foreach (var pt in extent)
                { if (pt.X < minX) minX = pt.X; if (pt.X > maxX) maxX = pt.X; if (pt.Y < minY) minY = pt.Y; if (pt.Y > maxY) maxY = pt.Y; }
                vb.AppendLine($"  [{title} {nDiv}×{nDiv}] X {minX:F1}~{maxX:F1} / Y {minY:F1}~{maxY:F1} (위=북)");
                for (int gy = nDiv - 1; gy >= 0; gy--)
                {
                    var row = new System.Text.StringBuilder("    ");
                    for (int gx = 0; gx < nDiv; gx++)
                    {
                        double x = minX + (maxX - minX) * (gx + 0.5) / nDiv;
                        double y = minY + (maxY - minY) * (gy + 0.5) / nDiv;
                        string cell;
                        try { cell = tin.FindElevationAtXY(x, y).ToString("F1"); }
                        catch { cell = "----"; }
                        row.Append(cell.PadLeft(7));
                    }
                    vb.AppendLine(row.ToString());
                }
            }
            Grid("부지 내부", rings[0], 6);
            Grid("계단 전체", rings[rings.Count - 1], 16);
        }
        catch (System.Exception ex) { vb.AppendLine("  검증 실패: " + ex.Message); }
        LastVerify = vb.ToString();
        return id;
    }

    /// <summary>열린 브레이크라인(코너 능선 등) — 링과 달리 닫지 않는다.</summary>
    private static void AddOpenBreakline(TinSurface tin, IReadOnlyList<Point3> pts)
    {
        if (pts.Count < 2) return;
        var pc = new Point3dCollection();
        foreach (var pt in pts) pc.Add(new Point3d(pt.X, pt.Y, pt.Z));
        try { tin.BreaklinesDefinition.AddStandardBreaklines(pc, 1.0, 0.0, 0.0, 0.0); } catch { }
    }

    /// <summary>daylight/교선 외곽선을 초록 폴리라인으로(시각 확인용). 레이어 'DH-정지경계'.</summary>
    public static void DrawDaylight(Database db, Transaction tr, IEnumerable<IReadOnlyList<Point3>> loops)
    {
        ObjectId layerId = EnsureLayer(db, tr, "DH-정지경계", 3);
        EraseOnLayer(db, tr, "DH-정지경계");
        var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
        foreach (var loop in loops)
        {
            if (loop == null || loop.Count < 2) continue;
            // 폐합 판정: 첫~끝 간격 ≤10cm면 '닫힘=예' 속성으로 닫는다(중복 정점 X → 속성창에 닫힘=예).
            // 열린 교선을 강제로 닫으면 시작~끝 대각선(허공 지름길)이 그려지므로 열린 선은 열린 채로 둔다.
            var f = loop[0]; var l = loop[loop.Count - 1];
            double gx = f.X - l.X, gy = f.Y - l.Y;
            double gapSq = gx * gx + gy * gy;
            int count = loop.Count;
            if (gapSq < 1e-12) count--; // 끝점=첫점 중복이면 정점 하나 생략(닫힘 속성이 연결 담당)
            if (count < 2) continue;    // 정점 1개짜리 방어(리뷰 L-6)
            bool closed = (gapSq < 0.10 * 0.10) && count >= 3;

            var pl = new Polyline3d { LayerId = layerId };
            ms.AppendEntity(pl); tr.AddNewlyCreatedDBObject(pl, true);
            for (int i = 0; i < count; i++)
            {
                var p = loop[i];
                var v = new PolylineVertex3d(new Point3d(p.X, p.Y, p.Z));
                pl.AppendVertex(v); tr.AddNewlyCreatedDBObject(v, true);
            }
            if (closed) pl.Closed = true;
        }
    }

    /// <summary>[진단] 표시용 선분 그리기 — 기본: 지름길 컷(빨강 'DH-진단'). 틈메움 연결선은 'DH-틈메움'(하늘색 4)로.
    /// 끊긴 자리에 빨간 선이 있으면 '필터가 자른 것', 없으면 '그 구간 교선이 아예 생성 안 된 것'.</summary>
    public static void DrawDebugSpans(Database db, Transaction tr, IEnumerable<(Point3 A, Point3 B)> spans,
        string layer = "DH-진단", short aci = 1)
    {
        ObjectId layerId = EnsureLayer(db, tr, layer, aci);
        EraseOnLayer(db, tr, layer);
        var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
        foreach (var (a, b) in spans)
        {
            var ln = new Line(new Point3d(a.X, a.Y, a.Z), new Point3d(b.X, b.Y, b.Z)) { LayerId = layerId };
            ms.AppendEntity(ln); tr.AddNewlyCreatedDBObject(ln, true);
        }
    }

    /// <summary>노리선(노란 'DH-노리선')+소단선(흰 'DH-소단')을 그린다 — DHSLOPELINE 전용.</summary>
    public static void DrawSlopeHatch(Database db, Transaction tr,
        IEnumerable<(Point3 A, Point3 B)> ticks, IEnumerable<IReadOnlyList<Point3>> benchLines)
    {
        ObjectId tickLayer = EnsureLayer(db, tr, "DH-노리선", 2);  // 노란
        ObjectId benchLayer = EnsureLayer(db, tr, "DH-소단", 7);   // 흰
        EraseOnLayer(db, tr, "DH-노리선");
        EraseOnLayer(db, tr, "DH-소단");
        var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);

        foreach (var (a, b) in ticks)
        {
            var ln = new Line(new Point3d(a.X, a.Y, a.Z), new Point3d(b.X, b.Y, b.Z)) { LayerId = tickLayer };
            ms.AppendEntity(ln); tr.AddNewlyCreatedDBObject(ln, true);
        }
        foreach (var loop in benchLines)
        {
            if (loop == null || loop.Count < 2) continue;
            var pl = new Polyline3d { LayerId = benchLayer };
            ms.AppendEntity(pl); tr.AddNewlyCreatedDBObject(pl, true);
            foreach (var p in loop)
            {
                var v = new PolylineVertex3d(new Point3d(p.X, p.Y, p.Z));
                pl.AppendVertex(v); tr.AddNewlyCreatedDBObject(v, true);
            }
        }
    }

    // ── helpers ──
    private static void AddRingBreakline(TinSurface tin, IReadOnlyList<Point3> loop)
    {
        if (loop.Count < 3) return;
        var seen = new HashSet<(long, long)>(); // 링마다 독립 — 링 간 정점 충돌로 정점이 스킵되어 브레이크라인에 구멍 나는 것 방지
        var pc = new Point3dCollection();
        foreach (var pt in loop)
        {
            var key = ((long)Math.Round(pt.X * 1000), (long)Math.Round(pt.Y * 1000));
            if (!seen.Add(key)) continue;
            pc.Add(new Point3d(pt.X, pt.Y, pt.Z));
        }
        if (pc.Count < 3) return;
        // 링 닫기 — 첫 점을 끝에 다시 추가해 마지막→첫 점을 연결. 열린 이음매(Seam)를 가로지르는 거대 삼각형 방지.
        var f = pc[0];
        pc.Add(new Point3d(f.X, f.Y, f.Z));
        try { tin.BreaklinesDefinition.AddStandardBreaklines(pc, 1.0, 0.0, 0.0, 0.0); } catch { }
    }

    /// <summary>이름이 baseName 또는 baseName_N 인 지표면을 모두 삭제(잠긴/참조 중이면 그 항목만 건너뜀).</summary>
    private static void EraseSurfacesByBaseName(Transaction tr, string baseName)
    {
        var civilDoc = Autodesk.Civil.ApplicationServices.CivilApplication.ActiveDocument;
        var victims = new List<ObjectId>();
        foreach (ObjectId sid in civilDoc.GetSurfaceIds())
        {
            if (tr.GetObject(sid, OpenMode.ForRead) is not Autodesk.Civil.DatabaseServices.Surface s) continue;
            string nm = s.Name;
            if (nm == baseName || (nm.StartsWith(baseName + "_") && int.TryParse(nm.Substring(baseName.Length + 1), out _)))
                victims.Add(sid);
        }
        foreach (var sid in victims)
        {
            try { (tr.GetObject(sid, OpenMode.ForWrite) as AcadEntity)?.Erase(); } catch { }
        }
    }

    private static string UniqueName(Database db, Transaction tr, string baseName)
    {
        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var civilDoc = Autodesk.Civil.ApplicationServices.CivilApplication.ActiveDocument;
        foreach (ObjectId id in civilDoc.GetSurfaceIds())
            if (tr.GetObject(id, OpenMode.ForRead) is Autodesk.Civil.DatabaseServices.Surface s) existing.Add(s.Name);
        if (!existing.Contains(baseName)) return baseName;
        for (int i = 2; ; i++) { string c = $"{baseName}_{i}"; if (!existing.Contains(c)) return c; }
    }

    private static ObjectId EnsureLayer(Database db, Transaction tr, string name, short aci)
    {
        var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
        if (lt.Has(name)) return lt[name];
        lt.UpgradeOpen();
        var ltr = new LayerTableRecord { Name = name, Color = Color.FromColorIndex(ColorMethod.ByAci, aci) };
        ObjectId id = lt.Add(ltr); tr.AddNewlyCreatedDBObject(ltr, true);
        return id;
    }

    private static void EraseOnLayer(Database db, Transaction tr, string layerName)
    {
        var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
        if (!lt.Has(layerName)) return;
        var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
        var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);
        var ids = new List<ObjectId>();
        foreach (ObjectId id in ms)
            if (tr.GetObject(id, OpenMode.ForRead) is AcadEntity ent && ent.Layer == layerName) ids.Add(id);
        foreach (ObjectId id in ids)
            if (tr.GetObject(id, OpenMode.ForWrite) is AcadEntity e) e.Erase();
    }
}
