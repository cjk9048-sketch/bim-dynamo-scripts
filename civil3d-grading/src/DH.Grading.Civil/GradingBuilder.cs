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
        IReadOnlyList<List<Point3>>? cornerLines = null, ObjectId protect = default)
    {
        // [재실행 정리] 같은 이름(및 _2, _3… 번호 변형)의 옛 DH 가상면을 먼저 삭제 — 실행마다 쌓여
        // 옛 표면을 보고 "안 생겼다"고 오인하는 혼란 방지(JACK). 항상 최신 하나만 남는다. 원지반(protect)은 제외.
        EraseSurfacesByBaseName(tr, name, protect);
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

    /// <summary>교선(폐합 루프)을 가상면의 Outer 경계로 주입(비파괴 = 경계선에서 삼각형 정밀 절단) 후 Rebuild.
    /// 경계는 표면 정의에 저장되므로 이후 다른 표면 작업/재그리기에 영향받지 않는다.</summary>
    public static void AddOuterBoundary(TinSurface tin, IReadOnlyList<Point3> ring)
    {
        int n = ring.Count;
        if (n >= 2)
        {
            var f = ring[0]; var l = ring[n - 1];
            if ((f.X - l.X) * (f.X - l.X) + (f.Y - l.Y) * (f.Y - l.Y) < 1e-12) n--; // 중복 닫음점 제거(길이 0 변 방지, 리뷰 M-1)
        }
        if (n < 3) return;
        var pc = new Point3dCollection();
        for (int i = 0; i < n; i++) pc.Add(new Point3d(ring[i].X, ring[i].Y, ring[i].Z));
        // nonDestructive=true: 경계에 걸친 삼각형을 경계선에서 '정밀 절단'(정점 삽입).
        // ※false로 A/B 실험 결과 절토까지 톱니(걸친 삼각형 통째 제거) — true가 올바른 의미로 확정(2026-07-03).
        //   성토가 경계 밖으로 튀어나오던 문제는 별개 원인 → VerifyBoundaryClip 실측으로 추적.
        tin.BoundariesDefinition.AddBoundaries(pc, 1.0, Autodesk.Civil.SurfaceBoundaryType.Outer, true);
        tin.Rebuild();
    }

    /// <summary>[검증] 경계 주입 후 표면이 경계선대로 잘렸는지 실측 — 링 표본의 안(25cm)·밖(25cm~8m) 표고 유무.
    /// 밖에 표면이 남아 있으면(outHit) 경계가 안 먹은 것, 안이 비면(inMiss) 과도 절단.</summary>
    public static string VerifyBoundaryClip(TinSurface tin, IReadOnlyList<Point3> ring)
    {
        bool TryElev(double x, double y) { try { tin.FindElevationAtXY(x, y); return true; } catch { return false; } }
        int n = ring.Count;
        if (n >= 2)
        {
            var f0 = ring[0]; var l0 = ring[n - 1];
            if ((f0.X - l0.X) * (f0.X - l0.X) + (f0.Y - l0.Y) * (f0.Y - l0.Y) < 1e-12) n--; // 닫음 중복 제외
        }
        if (n < 3) return "  [경계 정합 검증] 링 정점 부족\n";
        double area = 0;
        for (int i = 0; i < n; i++)
        { var a = ring[i]; var b = ring[(i + 1) % n]; area += a.X * b.Y - b.X * a.Y; }
        double s = area > 0 ? 1.0 : -1.0; // CCW면 내부는 진행방향 왼쪽
        int samples = 0, outHit = 0, inMiss = 0; double maxSpill = 0; string worst = "";
        int step = System.Math.Max(1, n / 200);
        for (int i = 0; i < n; i += step)
        {
            var a = ring[i]; var b = ring[(i + 1) % n];
            double ex = b.X - a.X, ey = b.Y - a.Y;
            double el = System.Math.Sqrt(ex * ex + ey * ey); if (el < 1e-9) continue;
            double mx = (a.X + b.X) * 0.5, my = (a.Y + b.Y) * 0.5;
            double nx = s * (-ey / el), ny = s * (ex / el); // 내부 방향 법선
            samples++;
            if (!TryElev(mx + nx * 0.25, my + ny * 0.25)) inMiss++;
            if (TryElev(mx - nx * 0.25, my - ny * 0.25))
            {
                outHit++;
                double spill = 0.25;
                foreach (var dOut in new[] { 0.5, 1.0, 2.0, 4.0, 8.0 })
                { if (TryElev(mx - nx * dOut, my - ny * dOut)) spill = dOut; else break; }
                if (spill > maxSpill) { maxSpill = spill; worst = $"({(mx - nx * spill):F1},{(my - ny * spill):F1})"; }
            }
        }
        return $"  [경계 정합 검증] 표본 {samples} · 경계밖 표면존재 {outHit}(최대 {maxSpill:F1}m 이탈{(worst == "" ? "" : " 예 " + worst)}) · 경계안 비어있음 {inMiss}\n";
    }

    /// <summary>기존 경계 정의를 모두 제거하고 새 Outer(+선택 Hide)로 교체 — paste 거부 시 정규화 링 재주입용.</summary>
    public static void ReplaceOuterBoundary(TinSurface tin, IReadOnlyList<Point3> ring, IReadOnlyList<Point3>? hideRing = null)
    {
        try { var bd = tin.BoundariesDefinition; while (bd.Count > 0) bd.RemoveAt(0); } catch { }
        AddOuterBoundary(tin, ring);
        if (hideRing != null) AddHideBoundary(tin, hideRing);
        try { tin.Rebuild(); } catch { }
    }

    /// <summary>내부 숨김(Hide) 경계 — 링 안쪽을 도넛처럼 뚫는다(절토면에서 pad 제거 → 성토와 겹침 제거).</summary>
    public static void AddHideBoundary(TinSurface tin, IReadOnlyList<Point3> ring)
    {
        int n = ring.Count;
        if (n >= 2)
        {
            var f = ring[0]; var l = ring[n - 1];
            if ((f.X - l.X) * (f.X - l.X) + (f.Y - l.Y) * (f.Y - l.Y) < 1e-12) n--; // 중복 닫음점 제거
        }
        if (n < 3) return;
        var pc = new Point3dCollection();
        for (int i = 0; i < n; i++) pc.Add(new Point3d(ring[i].X, ring[i].Y, ring[i].Z));
        tin.BoundariesDefinition.AddBoundaries(pc, 1.0, Autodesk.Civil.SurfaceBoundaryType.Hide, true);
        tin.Rebuild();
    }

    /// <summary>최종 합성 — 빈 TIN에 pasteOrder 순서로 PasteSurface(각 단계 스냅샷 굳히기).
    /// paste별 성공/실패와 Civil 예외 메시지를 log로 반환(병합 느낌표 원인 특정용, JACK 검증 지시).</summary>
    public static ObjectId Composite(Database db, Transaction tr, string name,
        IReadOnlyList<(ObjectId id, string label)> pasteOrder, out string log, bool freezeEach = true,
        ObjectId protect = default)
    {
        var sb = new System.Text.StringBuilder();
        EraseSurfacesByBaseName(tr, name, protect); // 재실행 스택 방지 — 원지반(protect)은 이름이 겹쳐도 보호(JACK 0715)
        ObjectId id = TinSurface.Create(db, UniqueName(db, tr, name));
        var final = (TinSurface)tr.GetObject(id, OpenMode.ForWrite);
        foreach (var (sid, label) in pasteOrder)
        {
            if (sid.IsNull) { sb.Append($"{label}:없음  "); continue; }
            try
            {
                final.PasteSurface(sid);
                if (freezeEach) Freeze(final); // paste 직후 스냅샷 굳히기(조합 실험 대상)
                else { try { final.Rebuild(); } catch { } }
                sb.Append($"{label}:OK  ");
            }
            catch (System.Exception ex) { sb.Append($"{label}:실패[{ex.GetType().Name}] {ex.Message}  "); }
        }
        try { Freeze(final); } catch { }
        log = sb.ToString().Trim();
        return id;
    }

    private static void Freeze(TinSurface s)
    {
        try { s.CreateSnapshot(); }
        catch { try { s.RebuildSnapshot(); } catch { } } // 이미 스냅샷 있으면 갱신
        try { s.Rebuild(); } catch { }
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
    public static void DrawDaylight(Database db, Transaction tr, IEnumerable<IReadOnlyList<Point3>> loops,
        string layerName = "DH-정지경계", short colorIndex = 3, bool layerOff = false)
    {
        ObjectId layerId = EnsureLayer(db, tr, layerName, colorIndex);
        // [JACK] 기본 숨김 옵션 — 데이터(선)는 남기되 레이어를 꺼서 화면은 깨끗하게. 필요 시 레이어 켜서 확인.
        if (layerOff)
            try { var ltr = (LayerTableRecord)tr.GetObject(layerId, OpenMode.ForWrite); ltr.IsOff = true; } catch { }
        EraseOnLayer(db, tr, layerName);
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

    /// <summary>노리선(노란 'DH-노리선')+소단선(흰 'DH-소단')을 그린다 — DHGRADE 4단계·DHSLOPELINE 공용.</summary>
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

    /// <summary>사면선·소단선 3D폴리선(ralplan Phase A) — 절/성토별 레이어 4개, 재실행 시 자기 레이어 청소.
    /// 사면선: 절토=색250(진회)/성토=색8(회) · 소단선: 절토=색1(빨강)/성토=색30(주황).</summary>
    public static void DrawSlopeEdges(Database db, Transaction tr,
        IEnumerable<IReadOnlyList<Point3>> cutSlopeLines, IEnumerable<IReadOnlyList<Point3>> cutBermLines,
        IEnumerable<IReadOnlyList<Point3>> fillSlopeLines, IEnumerable<IReadOnlyList<Point3>> fillBermLines)
    {
        var sets = new (string Layer, short Aci, IEnumerable<IReadOnlyList<Point3>> Lines)[]
        {
            ("DH-사면선-절토", 250, cutSlopeLines),
            ("DH-소단선-절토", 1,   cutBermLines),
            ("DH-사면선-성토", 8,   fillSlopeLines),
            ("DH-소단선-성토", 30,  fillBermLines),
        };
        foreach (var (layer, aci, lines) in sets) Draw3dPolys(db, tr, layer, aci, lines);
    }

    /// <summary>부지 내부 단차 전환사면(Phase F) 모서리 — 상단=DH-사면선-전환(색6)/하단=DH-소단선-전환(색4).</summary>
    public static void DrawTransitionEdges(Database db, Transaction tr,
        IEnumerable<IReadOnlyList<Point3>> crestLines, IEnumerable<IReadOnlyList<Point3>> toeLines)
    {
        Draw3dPolys(db, tr, "DH-사면선-전환", 6, crestLines);
        Draw3dPolys(db, tr, "DH-소단선-전환", 4, toeLines);
    }

    /// <summary>레이어 보장+청소 후 3D 폴리선 일괄 작도(사면선/소단선/전환선 공용).</summary>
    private static void Draw3dPolys(Database db, Transaction tr, string layer, short aci,
        IEnumerable<IReadOnlyList<Point3>> lines)
    {
        ObjectId layerId = EnsureLayer(db, tr, layer, aci);
        EraseOnLayer(db, tr, layer);
        var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
        foreach (var loop in lines)
        {
            if (loop == null || loop.Count < 2) continue;
            var pl = new Polyline3d { LayerId = layerId };
            ms.AppendEntity(pl); tr.AddNewlyCreatedDBObject(pl, true);
            foreach (var p in loop)
            {
                var v = new PolylineVertex3d(new Point3d(p.X, p.Y, p.Z));
                pl.AppendVertex(v); tr.AddNewlyCreatedDBObject(v, true);
            }
        }
    }

    /// <summary>이름(또는 이름_숫자)의 지표면 존재 여부 — DHNORI/DHINFRA 실행 게이트 ③용.</summary>
    public static bool SurfaceExistsByBaseName(Transaction tr, string baseName)
    {
        var civilDoc = Autodesk.Civil.ApplicationServices.CivilApplication.ActiveDocument;
        foreach (ObjectId sid in civilDoc.GetSurfaceIds())
        {
            if (tr.GetObject(sid, OpenMode.ForRead) is not Autodesk.Civil.DatabaseServices.Surface s) continue;
            string nm = s.Name;
            if (nm == baseName || (nm.StartsWith(baseName + "_") && int.TryParse(nm.Substring(baseName.Length + 1), out _)))
                return true;
        }
        return false;
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
    internal static void EraseSurfacesByBaseName(Transaction tr, string baseName, ObjectId protect = default)
    {
        var civilDoc = Autodesk.Civil.ApplicationServices.CivilApplication.ActiveDocument;
        var victims = new List<ObjectId>();
        foreach (ObjectId sid in civilDoc.GetSurfaceIds())
        {
            if (sid == protect) continue; // [JACK 0715] 선택된 원지반 보호 — LandXML 지반 이름이 '정지면_DH'여도 삭제 금지
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

    internal static string UniqueName(Database db, Transaction tr, string baseName)
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
