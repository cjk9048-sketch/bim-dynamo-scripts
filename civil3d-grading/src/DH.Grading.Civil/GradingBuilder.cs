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
        // [0729 — JACK] 보조선(코너 능선·플래토 직선·단차 레이)이 링과 평면 교차하면 Civil3D가 교차마다
        //   이벤트 뷰어 경고를 남김(단차 부지에서 수십 개) → 교차점을 양쪽에 공유 정점으로 삽입해 접점화.
        int sharedPts = 0;
        if (cornerLines != null && cornerLines.Count > 0)
            sharedPts = BreaklinePrep.SplitLineRingCrossings(rings, cornerLines);
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
            vb.AppendLine($"  브레이크라인 의도 {intended} / 정의됨 {defCount}" +
                          (sharedPts > 0 ? $" · 보조선-링 공유정점 {sharedPts}개 삽입(교차 경고 제거, maxΔZ {BreaklinePrep.LastMaxZGap:F3}m)" : "") +
                          (BreaklinePrep.LastMaxZGap > 2.0 ? " · ΔZ>2m 교차는 스냅 생략(안전판 — 형상 무해, Civil3D 경고만 남음)" : ""));
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
            // ★[0806 JACK '로그가 너무 길다'] 계단 전체 16×16 숫자 지도는 **16줄**을 차지하는데,
            //   '어느 쪽이 안 생겼나'를 찾던 시절(v13~v15 비대칭 추적)에 만든 것이고 그 문제는 닫혔다.
            //   구멍(빈 셀)이 실제로 있을 때만 지도를 펼치고, 없으면 한 줄 요약으로 끝낸다.
            GridOrSummary("계단 전체", rings[rings.Count - 1], 16);

            void GridOrSummary(string title, IReadOnlyList<Point3> extent, int nDiv)
            {
                double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
                foreach (var pt in extent)
                { if (pt.X < minX) minX = pt.X; if (pt.X > maxX) maxX = pt.X; if (pt.Y < minY) minY = pt.Y; if (pt.Y > maxY) maxY = pt.Y; }
                int hole = 0, hit = 0; double lo = double.MaxValue, hi = double.MinValue;
                for (int gy = 0; gy < nDiv; gy++)
                    for (int gx = 0; gx < nDiv; gx++)
                    {
                        double x = minX + (maxX - minX) * (gx + 0.5) / nDiv;
                        double y = minY + (maxY - minY) * (gy + 0.5) / nDiv;
                        try { double z = tin.FindElevationAtXY(x, y); hit++; if (z < lo) lo = z; if (z > hi) hi = z; }
                        catch { hole++; }
                    }
                // 바깥 모서리는 원래 표면 밖이라 비는 게 정상 — 전체의 45%까지는 정상으로 본다(원뿔형 계단면).
                if (hole > nDiv * nDiv * 0.45) Grid(title, extent, nDiv);      // 이상하게 많이 비었다 → 지도를 펼친다
                else vb.AppendLine($"  [{title}] X {minX:F1}~{maxX:F1} / Y {minY:F1}~{maxY:F1} · 표본 {hit}/{nDiv * nDiv}개 Z {lo:F1}~{hi:F1}m(빈칸 {hole} — 정상 범위)");
            }
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
        // ★[JACK 0807 'DH정지면에 스냅샷 재작성 느낌표가 뜬 상태로 작성됨'] 순서가 문제였다.
        //   종전엔 `CreateSnapshot()`을 부르고 **예외가 났을 때만** `RebuildSnapshot()`으로 갔다.
        //   그런데 스냅샷이 이미 있을 때 CreateSnapshot이 예외를 안 던지면(조용히 무시) 갱신이 영영 안 된다 —
        //   그러면 스냅샷은 **첫 붙여넣기 시점**에 머물고, 뒤에 쌓인 붙여넣기가 스냅샷보다 새것이 되어
        //   Prospector가 '스냅샷 재작성 필요(!)'를 띄운다. 예외에 기대지 말고 **둘 다 순서대로** 부른다.
        try { s.CreateSnapshot(); } catch { }      // 없으면 만든다(있으면 무시/예외 — 어느 쪽이든 상관없다)
        try { s.RebuildSnapshot(); } catch { }     // 있으면 **반드시** 최신 정의로 갱신한다
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
        // [JACK] 숨김 옵션 — 데이터(선)는 남기되 레이어 on/off로 화면 제어. 기존 도면에서 꺼져 있던 레이어도
        // layerOff=false면 다시 켠다(0728: 정지경계 초록 표시 요청 — 이전 실행이 꺼둔 상태 복구).
        try { var ltr = (LayerTableRecord)tr.GetObject(layerId, OpenMode.ForWrite); ltr.IsOff = layerOff; } catch { }
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
    /// 사면선: 절토=색150(밝은 하늘색)/성토=색210(밝은 자주) · 소단선: 절토=색1(빨강)/성토=색30(주황).
    /// (JACK 0727: 구 250·8 진회색이 검은 배경에서 안 보여 밝은 색으로 교체.)</summary>
    public static void DrawSlopeEdges(Database db, Transaction tr,
        IEnumerable<IReadOnlyList<Point3>> cutSlopeLines, IEnumerable<IReadOnlyList<Point3>> cutBermLines,
        IEnumerable<IReadOnlyList<Point3>> fillSlopeLines, IEnumerable<IReadOnlyList<Point3>> fillBermLines)
    {
        var sets = new (string Layer, short Aci, IEnumerable<IReadOnlyList<Point3>> Lines)[]
        {
            ("DH-사면선-절토", 150, cutSlopeLines),
            ("DH-소단선-절토", 1,   cutBermLines),
            ("DH-사면선-성토", 210, fillSlopeLines),
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

    /// <summary>[§75 1-A] XData 앱(RegApp) 등록 보장 — 없으면 추가.</summary>
    private static void EnsureRegApp(Database db, Transaction tr, string appName)
    {
        var rat = (RegAppTable)tr.GetObject(db.RegAppTableId, OpenMode.ForRead);
        if (rat.Has(appName)) return;
        rat.UpgradeOpen();
        var r = new RegAppTableRecord { Name = appName };
        rat.Add(r); tr.AddNewlyCreatedDBObject(r, true);
    }

    /// <summary>[§75] 사면선·소단선 4개 레이어 이름(절/성토 × 사면/소단) — DHWALL 색 전환·복원에 공용.</summary>
    public static readonly string[] EdgeLayerNames =
        { "DH-사면선-절토", "DH-소단선-절토", "DH-사면선-성토", "DH-소단선-성토" };
    /// <summary>사면선/소단선 기본색 — 등고선처럼 희미한 회색(ACI 8, JACK 0728). DHWALL 밖에서는 이 색.</summary>
    public const short EdgeGrayAci = 8;
    /// <summary>DHWALL 실행 중 '고를 수 있음' 강조색(ACI 4 = 시안).</summary>
    public const short EdgePickAci = 4;

    /// <summary>[§75] 지정 레이어들의 색을 aci로 설정(있는 것만). DHWALL 진입=강조/종료=회색 복원에 사용.</summary>
    public static void SetLayersColor(Database db, Transaction tr, IEnumerable<string> names, short aci)
    {
        var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
        foreach (var n in names)
        {
            if (!lt.Has(n)) continue;
            var ltr = (LayerTableRecord)tr.GetObject(lt[n], OpenMode.ForWrite);
            ltr.Color = Color.FromColorIndex(ColorMethod.ByAci, aci);
        }
    }

    /// <summary>[§75 1-A] 사면선·소단선을 식별정보(XData: 방향 up·사면/소단·단 index·구간 index)와 함께 작도.
    /// 옹벽 전환(DHWALL)이 클릭할 대상. 4개 레이어(사면선/소단선 × 절/성토, 색은 DrawSlopeEdges와 동일) 청소 후
    /// 태그된 3D 폴리선으로 그린다. up=true 절토/false 성토. XData=[appName, up, isSlope, bench, seg].</summary>
    public static void DrawSlopeEdgesTagged(Database db, Transaction tr,
        IEnumerable<(bool IsSlope, int Bench, int Seg, System.Collections.Generic.List<Point3> Pts)> cutEdges,
        IEnumerable<(bool IsSlope, int Bench, int Seg, System.Collections.Generic.List<Point3> Pts)> fillEdges,
        string planHandle = "", bool clearFirst = true)
    {
        EnsureRegApp(db, tr, GradingSettings.WallPickAppName);
        // [§75 1-A UX] 사면선/소단선은 기본 '회색'(JACK). 옹벽생성(DHWALL) 실행 중에만 색이 바뀌고 종료 시 복원.
        // [다중 구역 0729] clearFirst=false면 기존 선을 지우지 않고 덧그림(구역 루프의 2번째 이후 호출용).
        //   planHandle은 XData 끝에 문자열로 붙어 어느 구역(계획선)의 선인지 식별 — DHWALL이 마지막 구역만 허용.
        var layerId = new Dictionary<string, ObjectId>();
        foreach (var name in EdgeLayerNames)
        {
            layerId[name] = EnsureLayer(db, tr, name, EdgeGrayAci);
            if (clearFirst) EraseOnLayer(db, tr, name);
        }
        SetLayersColor(db, tr, EdgeLayerNames, EdgeGrayAci); // 기존 레이어면 EnsureLayer가 색을 안 바꾸므로 강제 회색

        var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);

        void DrawSet(IEnumerable<(bool IsSlope, int Bench, int Seg, System.Collections.Generic.List<Point3> Pts)> edges, bool up)
        {
            // [0728 버그수정 — JACK] Seg를 '방향 전체 유일 번호'로 재부여. 원래 seg는 영역(finalRing)마다
            //   0부터 다시 시작해, 성토 2곳이면 서로 다른 선이 같은 (단·구간) 신분을 가짐 →
            //   DHWALL 두 번째 클릭이 첫 선택을 토글 해제(둘 중 하나만 적용되던 원인).
            int uid = 0;
            foreach (var (isSlope, bench, _, pts) in edges)
            {
                if (pts == null || pts.Count < 2) continue;
                string layer = (isSlope ? "DH-사면선-" : "DH-소단선-") + (up ? "절토" : "성토");
                var pl = new Polyline3d { LayerId = layerId[layer] };
                ms.AppendEntity(pl); tr.AddNewlyCreatedDBObject(pl, true);
                foreach (var q in pts)
                {
                    var v = new PolylineVertex3d(new Point3d(q.X, q.Y, q.Z));
                    pl.AppendVertex(v); tr.AddNewlyCreatedDBObject(v, true);
                }
                pl.XData = new ResultBuffer(
                    new TypedValue((int)DxfCode.ExtendedDataRegAppName, GradingSettings.WallPickAppName),
                    new TypedValue((int)DxfCode.ExtendedDataInteger16, (short)(up ? 1 : 0)),
                    new TypedValue((int)DxfCode.ExtendedDataInteger16, (short)(isSlope ? 1 : 0)),
                    new TypedValue((int)DxfCode.ExtendedDataInteger16, (short)bench),
                    new TypedValue((int)DxfCode.ExtendedDataInteger16, (short)(uid++)),
                    new TypedValue((int)DxfCode.ExtendedDataAsciiString, planHandle ?? ""));
            }
        }
        DrawSet(cutEdges, true);
        DrawSet(fillEdges, false);
    }

    /// <summary>[FGL 표기 — JACK 0729 샘플 스샷] 계획 부지 중앙에 수준점형 심볼(4분할 원, 북서·남동 빨강 채움)
    /// + 위에 "FGL(+)191.00" 텍스트(색 7 — 배경 따라 흰/검). 레이어 'DH-FGL', 재실행 시 자기 레이어 청소.
    /// 노리선(DHNORI)에서 구역마다 1개씩 호출.</summary>
    public static void DrawFglMarkers(Database db, Transaction tr,
        IEnumerable<(double X, double Y, double Z)> marks,
        double radius = 2.0, double textH = 3.0)
    {
        ObjectId layerId = EnsureLayer(db, tr, "DH-FGL", 7);
        EraseOnLayer(db, tr, "DH-FGL");
        var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
        var red = Color.FromColorIndex(ColorMethod.ByAci, 1);
        const double bulge90 = 0.41421356237;   // tan(22.5°) — 90° 호

        foreach (var m in marks)
        {
            var c = new Point3d(m.X, m.Y, m.Z);
            var circ = new Circle(c, Vector3d.ZAxis, radius) { LayerId = layerId, Color = red };
            ms.AppendEntity(circ); tr.AddNewlyCreatedDBObject(circ, true);
            var lh = new Line(new Point3d(m.X - radius, m.Y, m.Z), new Point3d(m.X + radius, m.Y, m.Z)) { LayerId = layerId, Color = red };
            ms.AppendEntity(lh); tr.AddNewlyCreatedDBObject(lh, true);
            var lv = new Line(new Point3d(m.X, m.Y - radius, m.Z), new Point3d(m.X, m.Y + radius, m.Z)) { LayerId = layerId, Color = red };
            ms.AppendEntity(lv); tr.AddNewlyCreatedDBObject(lv, true);

            // 채움 사분면 2개(체크무늬) — 북서(북→서 호)·남동(남→동 호), 둘 다 반시계(양수 bulge)로 볼록한
            //   진짜 부채꼴이 되게 한다(0730 스샷: 서→북 순서는 호가 반대로 휘어 오목 조각이 됐음).
            foreach (var (ax, ay, bx, by) in new[]
            {
                (m.X, m.Y + radius, m.X - radius, m.Y),   // NW: 북 → 서 (반시계, 11시 사분면)
                (m.X, m.Y - radius, m.X + radius, m.Y),   // SE: 남 → 동 (반시계, 5시 사분면)
            })
            {
                var pie = new Polyline(3) { Elevation = m.Z, LayerId = layerId, Closed = true };
                pie.AddVertexAt(0, new Point2d(m.X, m.Y), 0, 0, 0);
                pie.AddVertexAt(1, new Point2d(ax, ay), bulge90, 0, 0);
                pie.AddVertexAt(2, new Point2d(bx, by), 0, 0, 0);
                ms.AppendEntity(pie); tr.AddNewlyCreatedDBObject(pie, true);
                try
                {
                    // [0730 스샷] 해치 평면 고도(Elevation)를 심볼 고도와 일치 — 안 주면 Z=0에 그려져 테두리와 분리.
                    var h = new Hatch { LayerId = layerId, Color = red, Associative = false, Elevation = m.Z };
                    ms.AppendEntity(h); tr.AddNewlyCreatedDBObject(h, true);
                    h.SetHatchPattern(HatchPatternType.PreDefined, "SOLID");
                    h.AppendLoop(HatchLoopTypes.Default, new ObjectIdCollection { pie.ObjectId });
                    h.EvaluateHatch(true);
                    pie.Erase();   // 윤곽 폴리선은 해치 생성 후 제거(원·십자선이 윤곽 담당)
                }
                catch { }          // 해치 실패 시 부채꼴 윤곽선이라도 남김
            }

            var txt = new DBText
            {
                TextString = $"FGL(+){m.Z:F2}",
                Height = textH,
                LayerId = layerId,
                Color = Color.FromColorIndex(ColorMethod.ByAci, 7),
                HorizontalMode = TextHorizontalMode.TextCenter,
                VerticalMode = TextVerticalMode.TextBottom,
                AlignmentPoint = new Point3d(m.X, m.Y + radius * 1.4, m.Z),
            };
            ms.AppendEntity(txt); tr.AddNewlyCreatedDBObject(txt, true);
            try { txt.AdjustAlignment(db); } catch { }
        }
    }

    /// <summary>[사면생성 DHSLOPE — JACK 0729] 클릭 대상용 '태그된' 옹벽선 작도 — 레이어 DH-옹벽선에
    /// XData [app, up, isSlope=1, bench, seg(방향 전체 유일), planHandle] 부착. 기존 선은 지우지 않고 덧그림
    /// (명령 종료 시 호출부가 반환된 ObjectId들만 지워 원상 복구). 반환=생성한 엔티티들.</summary>
    public static List<ObjectId> DrawWallLinesTagged(Database db, Transaction tr,
        IEnumerable<(bool IsSlope, int Bench, int Seg, System.Collections.Generic.List<Point3> Pts)> cutEdges,
        IEnumerable<(bool IsSlope, int Bench, int Seg, System.Collections.Generic.List<Point3> Pts)> fillEdges,
        string planHandle)
    {
        EnsureRegApp(db, tr, GradingSettings.WallPickAppName);
        ObjectId layerId = EnsureLayer(db, tr, "DH-옹벽선", 1);
        var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
        var made = new List<ObjectId>();

        void DrawSet(IEnumerable<(bool IsSlope, int Bench, int Seg, System.Collections.Generic.List<Point3> Pts)> edges, bool up)
        {
            int uid = 0;   // [DHWALL과 동일 원칙] 방향 전체 유일 번호 — 영역/단마다 0부터 재시작하는 seg 충돌 방지
            foreach (var (_, bench, _, pts) in edges)
            {
                if (pts == null || pts.Count < 2) continue;
                var pl = new Polyline3d { LayerId = layerId };
                ms.AppendEntity(pl); tr.AddNewlyCreatedDBObject(pl, true);
                foreach (var q in pts)
                {
                    var v = new PolylineVertex3d(new Point3d(q.X, q.Y, q.Z));
                    pl.AppendVertex(v); tr.AddNewlyCreatedDBObject(v, true);
                }
                pl.XData = new ResultBuffer(
                    new TypedValue((int)DxfCode.ExtendedDataRegAppName, GradingSettings.WallPickAppName),
                    new TypedValue((int)DxfCode.ExtendedDataInteger16, (short)(up ? 1 : 0)),
                    new TypedValue((int)DxfCode.ExtendedDataInteger16, (short)1),
                    new TypedValue((int)DxfCode.ExtendedDataInteger16, (short)bench),
                    new TypedValue((int)DxfCode.ExtendedDataInteger16, (short)(uid++)),
                    new TypedValue((int)DxfCode.ExtendedDataAsciiString, planHandle ?? ""));
                made.Add(pl.ObjectId);
            }
        }
        DrawSet(cutEdges, true);
        DrawSet(fillEdges, false);
        return made;
    }

    /// <summary>[§75] 옹벽 구간의 옹벽선(계단 상단 모서리) — 두꺼운 빨간 선(JACK 0728: 옹벽 구간은 노리선 대신 이것만).
    /// 레이어 'DH-옹벽선' ACI 1(빨강)·선가중치 0.50mm. 재실행 시 자기 레이어 청소.</summary>
    public static void DrawWallLines(Database db, Transaction tr,
        IEnumerable<IReadOnlyList<Point3>> lines)
    {
        ObjectId layerId = EnsureLayer(db, tr, "DH-옹벽선", 1);
        var ltr = (LayerTableRecord)tr.GetObject(layerId, OpenMode.ForWrite);
        ltr.Color = Color.FromColorIndex(ColorMethod.ByAci, 1);
        ltr.LineWeight = LineWeight.LineWeight050;
        EraseOnLayer(db, tr, "DH-옹벽선");
        var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
        foreach (var loop in lines)
        {
            if (loop == null || loop.Count < 2) continue;
            var pl = new Polyline3d { LayerId = layerId };
            ms.AppendEntity(pl); tr.AddNewlyCreatedDBObject(pl, true);
            foreach (var q in loop)
            {
                var v = new PolylineVertex3d(new Point3d(q.X, q.Y, q.Z));
                pl.AppendVertex(v); tr.AddNewlyCreatedDBObject(v, true);
            }
        }
    }

    /// <summary>[§75 — JACK 0728] 지표면 표시 정리: keepBaseName(정지면_DH)만 보이고 나머지(원지반·가상면 등)는
    /// 전부 숨김. keepBaseName=null이면 모든 지표면 표시 복원(DHGRADE 시작 시 — 원지반을 클릭 선택해야 하므로).</summary>
    public static void IsolateSurfaces(Transaction tr, string? keepBaseName)
    {
        var civilDoc = Autodesk.Civil.ApplicationServices.CivilApplication.ActiveDocument;
        foreach (ObjectId sid in civilDoc.GetSurfaceIds())
        {
            if (tr.GetObject(sid, OpenMode.ForRead) is not Autodesk.Civil.DatabaseServices.Surface s) continue;
            bool keep = keepBaseName == null
                || s.Name == keepBaseName
                || (s.Name.StartsWith(keepBaseName + "_") && int.TryParse(s.Name.Substring(keepBaseName.Length + 1), out _));
            try
            {
                var e = (AcadEntity)tr.GetObject(sid, OpenMode.ForWrite);
                e.Visible = keep;
            }
            catch { }
        }
    }

    /// <summary>[0728 — JACK] baseName 지표면의 표시 스타일을 이름 후보들 중 존재하는 것으로 설정.
    /// 정확 일치 우선, 없으면 '2'와 '10'이 들어간 등고선 스타일 폴백. 적용된 스타일명 반환("" = 미적용).</summary>
    public static string SetSurfaceStyle(Transaction tr, string baseName, params string[] candidates)
    {
        var civilDoc = Autodesk.Civil.ApplicationServices.CivilApplication.ActiveDocument;
        ObjectId styleId = ObjectId.Null; string styleName = "";
        var all = new List<(ObjectId id, string name)>();
        foreach (ObjectId sid in civilDoc.Styles.SurfaceStyles)
        {
            if (tr.GetObject(sid, OpenMode.ForRead) is Autodesk.Civil.DatabaseServices.Styles.SurfaceStyle st)
                all.Add((sid, st.Name));
        }
        foreach (var cand in candidates)
        {
            foreach (var (id2, nm) in all)
                if (string.Equals(nm, cand, StringComparison.OrdinalIgnoreCase)) { styleId = id2; styleName = nm; break; }
            if (!styleId.IsNull) break;
        }
        if (styleId.IsNull)
            foreach (var (id2, nm) in all)
                if (nm.Contains("2") && nm.Contains("10")) { styleId = id2; styleName = nm; break; }
        if (styleId.IsNull) return "";
        // [JACK 0728] 스타일에서 취하는 건 등고선 간격뿐 — '경계' 표시는 켜서 예전처럼 지표면 둘레가 보이고
        //   클릭 시 지표면이 선택되게(별도 초록 객체 불필요).
        try
        {
            var stw = (Autodesk.Civil.DatabaseServices.Styles.SurfaceStyle)tr.GetObject(styleId, OpenMode.ForWrite);
            stw.GetDisplayStylePlan(Autodesk.Civil.DatabaseServices.Styles.SurfaceDisplayStyleType.Boundary).Visible = true;
            stw.GetDisplayStyleModel(Autodesk.Civil.DatabaseServices.Styles.SurfaceDisplayStyleType.Boundary).Visible = true;
        }
        catch { }
        foreach (ObjectId sid in civilDoc.GetSurfaceIds())
        {
            if (tr.GetObject(sid, OpenMode.ForRead) is not Autodesk.Civil.DatabaseServices.Surface s) continue;
            string nm = s.Name;
            if (nm != baseName && !(nm.StartsWith(baseName + "_") && int.TryParse(nm.Substring(baseName.Length + 1), out _)))
                continue;
            try
            {
                var w = (Autodesk.Civil.DatabaseServices.Surface)tr.GetObject(sid, OpenMode.ForWrite);
                w.StyleId = styleId;
            }
            catch { }
        }
        return styleName;
    }

    /// <summary>[0728] 이름이 baseName(또는 _N)인 지표면 재작성 — 소스 숨김(Visible) 등으로 붙는
    /// '정의 구식(⚠)' 표시 해소용. 실패해도 무시.</summary>
    public static string RebuildSurfacesByBaseName(Transaction tr, string baseName)
    {
        var civilDoc = Autodesk.Civil.ApplicationServices.CivilApplication.ActiveDocument;
        int hit = 0, snapOk = 0, snapNo = 0, reOk = 0; string first = "";
        foreach (ObjectId sid in civilDoc.GetSurfaceIds())
        {
            if (tr.GetObject(sid, OpenMode.ForRead) is not Autodesk.Civil.DatabaseServices.Surface s) continue;
            string nm = s.Name;
            if (nm != baseName && !(nm.StartsWith(baseName + "_") && int.TryParse(nm.Substring(baseName.Length + 1), out _)))
                continue;
            hit++;
            try
            {
                var w = (Autodesk.Civil.DatabaseServices.Surface)tr.GetObject(sid, OpenMode.ForWrite);
                // [0807] 스냅샷 갱신이 **실제로 됐는지** 남긴다 — 종전엔 catch{}로 삼켜서
                //   느낌표가 왜 안 없어지는지 로그로 알 길이 없었다.
                try { w.RebuildSnapshot(); snapOk++; }
                catch (System.Exception ex) { snapNo++; if (first.Length == 0) first = ex.Message; }
                try { w.Rebuild(); reOk++; }
                catch (System.Exception ex) { if (first.Length == 0) first = ex.Message; }
            }
            catch (System.Exception ex) { if (first.Length == 0) first = ex.Message; }
        }
        return $"'{baseName}' 표면 {hit}개 — 스냅샷 갱신 {snapOk}/실패 {snapNo} · 재작성 {reOk}" +
               (first.Length > 0 ? $" · 첫 사유: {first}" : "");
    }

    /// <summary>이름(또는 이름_숫자)의 지표면 존재 여부 — DHNORI/DHINFRA 실행 게이트 ③용.</summary>
    public static bool SurfaceExistsByBaseName(Transaction tr, string baseName)
        => !FindSurfaceByBaseName(tr, baseName).IsNull;

    /// <summary>[다중 구역 0729] baseName(또는 _N 번호 변형) 지표면의 ObjectId — 없으면 Null.</summary>
    public static ObjectId FindSurfaceByBaseName(Transaction tr, string baseName)
    {
        var civilDoc = Autodesk.Civil.ApplicationServices.CivilApplication.ActiveDocument;
        foreach (ObjectId sid in civilDoc.GetSurfaceIds())
        {
            if (tr.GetObject(sid, OpenMode.ForRead) is not Autodesk.Civil.DatabaseServices.Surface s) continue;
            string nm = s.Name;
            if (nm == baseName || (nm.StartsWith(baseName + "_") && int.TryParse(nm.Substring(baseName.Length + 1), out _)))
                return sid;
        }
        return ObjectId.Null;
    }

    // ── helpers ──
    private static void AddRingBreakline(TinSurface tin, IReadOnlyList<Point3> loop)
    {
        if (loop.Count < 3) return;
        var seen = new HashSet<(long, long)>(); // 링마다 독립 — 링 간 정점 충돌로 정점이 스킵되어 브레이크라인에 구멍 나는 것 방지
        var pts = new List<Point3d>();
        foreach (var pt in loop)
        {
            var key = ((long)Math.Round(pt.X * 1000), (long)Math.Round(pt.Y * 1000));
            if (!seen.Add(key)) continue;
            pts.Add(new Point3d(pt.X, pt.Y, pt.Z));
        }
        if (pts.Count < 3) return;

        // [§75 — 0728] 옹벽 구간 좌우 끝의 '급하강(다이브)' 긴 선분(>2.5m)은 브레이크라인에서 제외.
        //   깊은 단일수록 다이브가 수십 m라 이웃 링(1m 간격)의 다이브끼리 평면 교차 → 이벤트 뷰어 오류 홍수.
        //   정상 링은 densify로 전 선분 ≤1m라 무영향. 측벽 면은 TIN 삼각화가 채우므로 형상은 유지된다.
        const double MaxSeg = 2.5;
        const double MaxSeg2 = MaxSeg * MaxSeg;
        var runs = new List<List<Point3d>>();
        var cur = new List<Point3d> { pts[0] };
        for (int i = 1; i < pts.Count; i++)
        {
            double dx = pts[i].X - pts[i - 1].X, dy = pts[i].Y - pts[i - 1].Y;
            if (dx * dx + dy * dy > MaxSeg2) { if (cur.Count >= 2) runs.Add(cur); cur = new List<Point3d>(); }
            cur.Add(pts[i]);
        }
        double cdx = pts[0].X - pts[^1].X, cdy = pts[0].Y - pts[^1].Y;
        bool closeOk = cdx * cdx + cdy * cdy <= MaxSeg2;
        if (runs.Count == 0 && closeOk)
        {
            // 다이브 없음 = 정상 링 — 기존과 동일하게 닫아서 등록(이음매 거대 삼각형 방지).
            var pc = new Point3dCollection();
            foreach (var q in cur) pc.Add(q);
            pc.Add(cur[0]);
            try { tin.BreaklinesDefinition.AddStandardBreaklines(pc, 1.0, 0.0, 0.0, 0.0); } catch { }
            return;
        }
        if (closeOk && runs.Count > 0 && cur.Count > 0)
        {
            cur.AddRange(runs[0]); // 꼬리 run이 이음새(마지막→첫)로 머리 run과 이어짐 → 병합
            runs[0] = cur;
        }
        else if (cur.Count >= 2) runs.Add(cur);
        foreach (var run in runs)
        {
            if (run.Count < 2) continue;
            var pc = new Point3dCollection();
            foreach (var q in run) pc.Add(q);
            try { tin.BreaklinesDefinition.AddStandardBreaklines(pc, 1.0, 0.0, 0.0, 0.0); } catch { }
        }
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
