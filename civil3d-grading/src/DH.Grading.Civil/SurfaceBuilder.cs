using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.Civil.DatabaseServices;
using DH.Grading.Core;

namespace DH.Grading.Civil;

/// <summary>정지 결과를 Civil3D TinSurface로 만들고 토공량을 산출한다.</summary>
public static class SurfaceBuilder
{
    public readonly record struct BuildOutcome(
        ObjectId SurfaceId, int PointCount, double CutVolume, double FillVolume, double NetVolume,
        bool VolumeBuilt, ObjectId VolumeSurfaceId);

    /// <summary>
    /// 정지점들로 새 TinSurface를 만들고, 원지반과 비교한 절·성토량(m³)을 돌려준다.
    /// 트랜잭션은 호출부가 연다(이 메서드는 그 안에서 동작).
    /// </summary>
    public static BuildOutcome Build(Database db, Transaction tr, GradingResult result,
        ObjectId groundSurfaceId, string baseName)
    {
        if (result.Points.Count < 3)
            throw new InvalidOperationException("정지점이 너무 적습니다(폴리곤·원지반 범위를 확인하세요).");

        // 1) 정지면 TinSurface 생성 + 정점 추가
        string name = UniqueSurfaceName(db, tr, baseName);
        ObjectId surfId = TinSurface.Create(db, name);
        var tin = (TinSurface)tr.GetObject(surfId, OpenMode.ForWrite);

        var pts = new Point3dCollection();
        foreach (var p in result.Points)
            pts.Add(new Point3d(p.X, p.Y, p.Z));
        tin.AddVertices(pts);
        tin.Rebuild();

        // 2) 토공량 — 원지반(base) 대비 정지면(comparison) 체적 surface
        double cut = result.CutVolume, fill = result.FillVolume, net = fill - cut;
        bool volBuilt = false;
        ObjectId volSurfId = ObjectId.Null;
        try
        {
            string volName = UniqueSurfaceName(db, tr, baseName + "_체적");
            ObjectId volId = TinVolumeSurface.Create(volName, groundSurfaceId, surfId);
            volSurfId = volId;
            var vol = (TinVolumeSurface)tr.GetObject(volId, OpenMode.ForWrite);
            vol.Rebuild(); // 생성 직후 재작성 → "최신 아님(!)" 상태 해소(수동 재작성 불필요)
            var vp = vol.GetVolumeProperties();
            volBuilt = true;
            cut = vp.UnadjustedCutVolume;
            fill = vp.UnadjustedFillVolume;
            net = fill - cut; // 순토공량 = 성토 − 절토 (속성 의존 최소화)
        }
        catch
        {
            // 체적 surface 생성 실패 시 격자 기반 근사값(Core 산출) 유지
        }

        return new BuildOutcome(surfId, pts.Count, cut, fill, net, volBuilt, volSurfId);
    }

    /// <summary>
    /// 브레이크라인(특성선) 방식 결과로 TinSurface를 만든다.
    /// 모든 점을 정점으로 넣어 밀도를 확보하고, 경계·단모서리·daylight 선을 Standard 브레이크라인으로
    /// 강제해 계단선을 칼같이 만든다. 브레이크라인 추가가 실패해도 정점만으로 표면은 형성된다.
    /// </summary>
    public static BuildOutcome BuildFromBreaklines(Database db, Transaction tr, GradingBreaklineResult result,
        ObjectId groundSurfaceId, string baseName)
    {
        // 정점: 방사 단면(전체 프로파일)의 모든 점 — 표면 밀도 공급
        var src = result.Breaklines.Where(b => b.Kind == BreaklineKind.Radial).ToList();
        if (src.Count == 0)
            src = result.Breaklines.Where(b => b.Kind != BreaklineKind.Daylight).ToList();
        var allPts = src.SelectMany(b => b.Points).ToList();
        if (allPts.Count < 3)
            throw new InvalidOperationException("정지점이 너무 적습니다(폴리곤·원지반 범위를 확인하세요).");

        string name = UniqueSurfaceName(db, tr, baseName);
        ObjectId surfId = TinSurface.Create(db, name);
        var tin = (TinSurface)tr.GetObject(surfId, OpenMode.ForWrite);

        // 1) 전체 점을 정점으로 (한 번에)
        var verts = new Point3dCollection();
        foreach (var p in allPts)
            verts.Add(new Point3d(p.X, p.Y, p.Z));
        tin.AddVertices(verts);

        // 2) 계획 경계선만 Standard 브레이크라인으로(단순 폐곡선, 안전).
        //    단 모서리 링은 오목 다각형에서 자기교차 → TIN crossing 에러를 유발하므로 브레이크라인에서 제외.
        //    (단 모서리는 정점으로 충분히 표현되고, 크리스프한 선은 DH-정지경계 레이어 폴리라인으로 별도 표시)
        foreach (var bl in result.Breaklines)
        {
            if (bl.Kind != BreaklineKind.Boundary) continue;
            if (bl.Points.Count < 2) continue;
            try
            {
                var pc = new Point3dCollection();
                foreach (var p in bl.Points) pc.Add(new Point3d(p.X, p.Y, p.Z));
                if (bl.Closed && bl.Points.Count >= 3)
                {
                    var f = bl.Points[0];
                    pc.Add(new Point3d(f.X, f.Y, f.Z)); // 닫기
                }
                tin.BreaklinesDefinition.AddStandardBreaklines(pc, 1.0, 0.0, 0.0, 0.0);
            }
            catch { /* 이 선 실패해도 정점으로 표면 유지 */ }
        }

        // 3) daylight 폐합선 → 외곽 경계(Outer): 그 바깥(넉넉히 뻗은 계단)을 숨겨 깔끔히 잘라냄
        var dayBl = result.Breaklines.FirstOrDefault(b => b.Kind == BreaklineKind.Daylight);
        if (dayBl is { Points.Count: >= 3 })
        {
            try
            {
                var bc = new Point3dCollection();
                foreach (var p in dayBl.Points) bc.Add(new Point3d(p.X, p.Y, p.Z));
                var f = dayBl.Points[0];
                bc.Add(new Point3d(f.X, f.Y, f.Z)); // 닫기
                tin.BoundariesDefinition.AddBoundaries(bc, 1.0, Autodesk.Civil.SurfaceBoundaryType.Outer, true);
            }
            catch { /* 경계 실패 시 전체 표면 유지(자르기만 생략) */ }
        }

        tin.Rebuild();

        // 토량지표면은 정지면이 '커밋된 뒤' 별도 트랜잭션에서 만든다(CreateVolume) →
        // 같은 트랜잭션에서 만들면 "최신 아님(!)" 상태가 남는 문제 회피.
        return new BuildOutcome(surfId, verts.Count,
            result.CutVolume, result.FillVolume, result.FillVolume - result.CutVolume,
            false, ObjectId.Null);
    }

    /// <summary>
    /// 격자(distance-field) 결과로 단일값 TinSurface를 만든다(접힘 불가). daylight 너머 1단까지 포함된
    /// '넉넉한' 정지면. 트림은 TrimToGroundIntersection에서 체적면 0선으로 수행.
    /// </summary>
    public static (ObjectId SurfaceId, int PointCount) BuildGridSurface(
        Database db, Transaction tr, GradingResult result, IReadOnlyList<Point3> boundary, string baseName)
    {
        if (result.Points.Count < 3)
            throw new InvalidOperationException("정지점이 너무 적습니다(폴리곤·원지반 범위를 확인하세요).");

        string name = UniqueSurfaceName(db, tr, baseName);
        ObjectId surfId = TinSurface.Create(db, name);
        var tin = (TinSurface)tr.GetObject(surfId, OpenMode.ForWrite);

        var verts = new Point3dCollection();
        foreach (var p in result.Points) verts.Add(new Point3d(p.X, p.Y, p.Z));

        // 계획 경계를 '점'으로 촘촘히 추가 — 평지 가장자리가 격자 양자화로 경계 안쪽에 쳐지지 않고
        // 입력 경계에 정확히 닿게 한다(변마다 들쭉날쭉 방지). 브레이크라인이 아니라 점이므로
        // "브레이크라인 교차" 이벤트 폭주가 없다(과거 폭주는 경계를 '브레이크라인'으로 넣었을 때만 발생).
        int bn = boundary.Count;
        const double bstep = 0.5; // 경계 샘플 간격(m) — 직선 변이라 이 간격이면 가장자리가 정확.
        for (int i = 0; i < bn; i++)
        {
            var a = boundary[i]; var b = boundary[(i + 1) % bn];
            double dx = b.X - a.X, dy = b.Y - a.Y, dz = b.Z - a.Z;
            double L = Math.Sqrt(dx * dx + dy * dy);
            int steps = Math.Max(1, (int)Math.Ceiling(L / bstep));
            for (int s = 0; s < steps; s++) // 끝점은 다음 변의 시작점에서 추가됨(중복 회피)
            {
                double t = (double)s / steps;
                verts.Add(new Point3d(a.X + dx * t, a.Y + dy * t, a.Z + dz * t));
            }
        }

        // daylight 정점은 여기서 넣지 않는다 — 격자 daylight(톱니)가 아니라 '매끈한 표면 교선' daylight 점을
        // 트림 단계(TrimToGroundIntersection addVertices)에서 정점으로 박는다. 그래야 표면 외곽이 매끈해진다.
        tin.AddVertices(verts);

        tin.Rebuild();
        return (surfId, verts.Count);
    }

    /// <summary>
    /// JACK 방식의 자동화: 격자에서 추적한 daylight 폐합 루프(외곽 + 안쪽 봉우리 구멍)로 정지면을 트림한다.
    /// 가장 큰 루프 = 외곽(Outer, 바깥 숨김), 나머지 = Hide(안쪽 구멍). 트림만 수행하고 커밋한다.
    /// 토량지표면(체적면)은 이 트랜잭션이 '커밋된 뒤' 별도 트랜잭션(CreateVolume)에서 만든다 →
    /// 같은 트랜잭션에서 만들면 방금 수정한 정지면을 참조해 "!"(최신 아님) 상태가 남기 때문.
    /// </summary>
    public static bool TrimToGroundIntersection(
        Transaction tr, ObjectId gradeSurfaceId, IReadOnlyList<List<Point3>> daylightLoops, bool addVertices = false)
    {
        try
        {
            var grade = (TinSurface)tr.GetObject(gradeSurfaceId, OpenMode.ForWrite);

            // 매끈한 교선 daylight 점을 표면 정점으로 박는다 → 삼각망 가장자리가 그 선을 따라 잘려 계단이 사라진다.
            // 점 간격이 크면 0.5m로 보간해 촘촘히(가장자리 삼각형이 daylight 선을 비스듬히 가로지르지 않게).
            if (addVertices)
            {
                var dpc = new Point3dCollection();
                foreach (var loop in daylightLoops)
                    for (int i = 0; i < loop.Count; i++)
                    {
                        var a = loop[i];
                        dpc.Add(new Point3d(a.X, a.Y, a.Z));
                        if (i + 1 >= loop.Count) continue;
                        var b = loop[i + 1];
                        double dx = b.X - a.X, dy = b.Y - a.Y;
                        int steps = (int)(Math.Sqrt(dx * dx + dy * dy) / 0.5);
                        for (int s = 1; s < steps; s++)
                        {
                            double t = (double)s / steps;
                            dpc.Add(new Point3d(a.X + dx * t, a.Y + dy * t, a.Z + (b.Z - a.Z) * t));
                        }
                    }
                if (dpc.Count > 0) { grade.AddVertices(dpc); grade.Rebuild(); }
            }

            // 점목록(Point3dCollection)으로 직접 경계 추가 — 임시 엔티티/삭제 없음 → 참조 안 끊김("!" 방지).
            // 면적 큰 루프=외곽(Outer, 바깥 숨김), 나머지=Hide(안쪽 구멍).
            var sorted = daylightLoops.Where(l => l.Count >= 3)
                .Select(l => (Loop: l, Area: Math.Abs(ShoelaceArea(l))))
                .OrderByDescending(x => x.Area).ToList();

            for (int i = 0; i < sorted.Count; i++)
            {
                var pc = new Point3dCollection();
                foreach (var pt in sorted[i].Loop) pc.Add(new Point3d(pt.X, pt.Y, pt.Z));
                var f = sorted[i].Loop[0]; pc.Add(new Point3d(f.X, f.Y, f.Z)); // 닫기
                var type = i == 0 ? Autodesk.Civil.SurfaceBoundaryType.Outer : Autodesk.Civil.SurfaceBoundaryType.Hide;
                // 파괴적(useNonDestructiveBreakline=false): 경계선을 '브레이크라인'으로 넣지 않고 바깥 삼각형을
                // 잘라내기만 한다 → 0.1m 격자에서 경계선 점이 수천 번 교차해 생기던 "브레이크라인 교차" 이벤트 소멸.
                try { grade.BoundariesDefinition.AddBoundaries(pc, 1.0, type, false); } catch { }
            }
            grade.Rebuild();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static double ShoelaceArea(List<Point3> pts)
    {
        double a = 0;
        int n = pts.Count;
        for (int i = 0, j = n - 1; i < n; j = i++)
            a += pts[j].X * pts[i].Y - pts[i].X * pts[j].Y;
        return a * 0.5;
    }

    /// <summary>
    /// JACK 방식의 daylight — 차이표면(정지면고−원지반고)을 임시 TinSurface로 만들고 그 '0 등고선'을 추출한다.
    /// 0 등고선 = 정지면과 원지반이 만나는 교선 = 진짜 daylight. 격자 마스크 추적(톱니)이 아니라 표면 삼각형
    /// 교선이라 매끈하다. 각 등고선 점의 Z는 정지면 표고로 보정(교점이라 원지반과 같음). 실패 시 빈 리스트(호출부가 격자 폴백).
    /// 임시 차이표면과 ExtractContoursAt이 만든 등고선 엔티티는 좌표만 읽고 모두 삭제한다.
    /// </summary>
    public static List<List<Point3>> ExtractDaylightContour(
        Database db, Transaction tr, GradingResult result, ObjectId gradeSurfaceId, IReadOnlyList<Point3> boundary,
        double cellSize)
    {
        var loops = new List<List<Point3>>();
        if (result.DiffPoints.Count < 3) return loops;

        ObjectId tmpId = ObjectId.Null;
        var contourIds = new ObjectIdCollection();
        try
        {
            string nm = UniqueSurfaceName(db, tr, "__DH_diff_tmp");
            tmpId = TinSurface.Create(db, nm);
            var tmp = (TinSurface)tr.GetObject(tmpId, OpenMode.ForWrite);
            var pc = new Point3dCollection();
            foreach (var p in result.DiffPoints) pc.Add(new Point3d(p.X, p.Y, p.Z));
            tmp.AddVertices(pc);
            tmp.Rebuild();

            // 0 등고선 = 교선. ExtractContoursAt은 ITerrainSurface(TinSurface가 구현)에 정의.
            contourIds = ((ITerrainSurface)tmp).ExtractContoursAt(0.0);

            var grade = (TinSurface)tr.GetObject(gradeSurfaceId, OpenMode.ForRead);
            foreach (ObjectId cid in contourIds)
            {
                if (tr.GetObject(cid, OpenMode.ForRead) is not Autodesk.AutoCAD.DatabaseServices.Polyline pl) continue;
                int nv = (int)pl.NumberOfVertices;
                var pts = new List<Point3>(nv + 1);
                for (int i = 0; i < nv; i++)
                {
                    var p2 = pl.GetPoint2dAt(i);
                    double z;
                    try { z = grade.FindElevationAtXY(p2.X, p2.Y); } catch { z = pl.Elevation; }
                    pts.Add(new Point3(p2.X, p2.Y, z));
                }
                // 차이표면 격자에서 물려받은 작은 계단을 단순화로 편다(수축 없음). 닫기 전에 수행.
                if (pts.Count >= 4) pts = GradingEngine.SimplifyLoop(pts, cellSize * 1.5);
                if (pl.Closed && pts.Count >= 3) pts.Add(pts[0]); // 닫힌 등고선은 첫점 복제로 닫음
                if (pts.Count < 2) continue;

                // 경계 '안'에 완전히 들어있는 0등고선 = 내부 봉우리 밑둘레(절토 전환선) → daylight 아님.
                // JACK 방침(경계 안 봉우리는 평탄하게 깎음)대로 표면에 구멍을 내지 않도록 제외한다.
                // 진짜 daylight(정지 비탈이 원지반과 만나는 바깥선)는 경계 밖에 있으므로 남는다.
                if (boundary != null && boundary.Count >= 3)
                {
                    int inside = 0;
                    foreach (var pp in pts)
                        if (DH.Grading.Core.PolygonGeometry.Contains(boundary, pp.X, pp.Y)) inside++;
                    if (inside > pts.Count * 0.5) continue; // 절반 이상 경계 안 → 내부 봉우리, 제외
                }
                loops.Add(pts);
            }
        }
        catch { loops.Clear(); }
        finally
        {
            // ExtractContoursAt이 도면에 생성한 등고선 폴리라인 삭제(좌표만 사용).
            foreach (ObjectId cid in contourIds)
            {
                try { if (tr.GetObject(cid, OpenMode.ForWrite) is Autodesk.AutoCAD.DatabaseServices.Entity e) e.Erase(); } catch { }
            }
            // 임시 차이표면 삭제.
            if (!tmpId.IsNull)
            {
                try { if (tr.GetObject(tmpId, OpenMode.ForWrite) is Autodesk.AutoCAD.DatabaseServices.Entity e) e.Erase(); } catch { }
            }
        }

        // JACK 방침: 무조건 '최외곽선' 1개만 daylight로 사용 — 내부 선(절↔성 전환선·봉우리 밑둘레·구멍)은
        // 전부 버려 표면이 Hide(구멍)로 뚫리거나 찢기는 것을 막는다. 면적이 가장 큰 루프 하나만 남긴다.
        if (loops.Count > 1)
        {
            int best = 0; double bestA = -1;
            for (int i = 0; i < loops.Count; i++)
            {
                double a = Math.Abs(ShoelaceArea(loops[i]));
                if (a > bestA) { bestA = a; best = i; }
            }
            var only = loops[best];
            loops = new List<List<Point3>> { only };
        }
        return loops;
    }

    /// <summary>
    /// AutoCAD 네이티브 오프셋(GetOffsetCurves)으로 단 모서리(브레이크라인)를 만들어 정지면을 구성한다.
    /// 오목 코너를 AutoCAD 오프셋 엔진이 깔끔히 처리 → 직접 법선 계산의 접힘 문제 없음.
    /// 경계(designZ) + 각 단(비탈끝·소단끝) 동심 오프셋 링을 브레이크라인으로. daylight 트림은 별도.
    /// </summary>
    public static (ObjectId SurfaceId, int Rings) BuildFromOffsets(
        Database db, Transaction tr, ObjectId boundaryCurveId, double designZ,
        GradingParams p, bool isFill, string baseName)
    {
        var curve = (Autodesk.AutoCAD.DatabaseServices.Curve)tr.GetObject(boundaryCurveId, OpenMode.ForRead);
        double sign = OutwardSign(curve);

        double n = isFill ? p.FillSlope : p.CutSlope;
        double effN = Math.Max(n, p.MinSlope);
        double faceRun = Math.Max(p.BenchHeight * effN, p.MinFaceRun);
        double dz = isFill ? -p.BenchHeight : p.BenchHeight;
        double period = faceRun + p.BenchWidth;

        string name = UniqueSurfaceName(db, tr, baseName);
        ObjectId surfId = TinSurface.Create(db, name);
        var tin = (TinSurface)tr.GetObject(surfId, OpenMode.ForWrite);

        int rings = 0;
        if (AddCurveAsBreakline(tin, curve, designZ)) rings++; // 경계(평지 가장자리)

        for (int k = 1; k <= p.MaxBenches; k++)
        {
            double z = designZ + dz * k;
            double dSlopeBot = (k - 1) * period + faceRun; // 비탈 k 바닥
            double dBenchOut = k * period;                 // 소단 k 바깥
            foreach (double dist in new[] { dSlopeBot, dBenchOut })
            {
                DBObjectCollection offs;
                try { offs = curve.GetOffsetCurves(dist * sign); }
                catch { continue; }
                foreach (Autodesk.AutoCAD.DatabaseServices.DBObject o in offs)
                {
                    if (o is Autodesk.AutoCAD.DatabaseServices.Curve oc && AddCurveAsBreakline(tin, oc, z)) rings++;
                    o.Dispose();
                }
            }
        }

        tin.Rebuild();
        return (surfId, rings);
    }

    /// <summary>오프셋 +/- 중 면적이 큰 쪽 = 바깥 방향.</summary>
    private static double OutwardSign(Autodesk.AutoCAD.DatabaseServices.Curve c)
    {
        try
        {
            using var plus = c.GetOffsetCurves(1.0);
            using var minus = c.GetOffsetCurves(-1.0);
            return MaxArea(plus) >= MaxArea(minus) ? 1.0 : -1.0;
        }
        catch { return 1.0; }
    }

    private static double MaxArea(DBObjectCollection curves)
    {
        double m = 0;
        foreach (Autodesk.AutoCAD.DatabaseServices.DBObject o in curves)
            if (o is Autodesk.AutoCAD.DatabaseServices.Curve c)
            {
                try { m = Math.Max(m, Math.Abs(c.Area)); } catch { }
            }
        return m;
    }

    /// <summary>곡선의 정점을 뽑아 표고 z로 세팅해 Standard 브레이크라인으로 추가.</summary>
    private static bool AddCurveAsBreakline(TinSurface tin, Autodesk.AutoCAD.DatabaseServices.Curve c, double z)
    {
        var pc = new Point3dCollection();
        if (c is Autodesk.AutoCAD.DatabaseServices.Polyline pl)
        {
            int nv = pl.NumberOfVertices;
            for (int i = 0; i < nv; i++)
            {
                var pt = pl.GetPoint3dAt(i);
                pc.Add(new Point3d(pt.X, pt.Y, z));
            }
            if (pl.Closed && nv >= 2)
            {
                var f = pl.GetPoint3dAt(0);
                pc.Add(new Point3d(f.X, f.Y, z));
            }
        }
        if (pc.Count < 2) return false;
        try { tin.BreaklinesDefinition.AddStandardBreaklines(pc, 1.0, 0.0, 0.0, 0.0); return true; }
        catch { return false; }
    }

    /// <summary>
    /// 단 모서리 형상선(BenchLoops)들을 브레이크라인으로 정지면 TIN을 만든다. 단일값에서 추출한 깨끗한
    /// 동심 폐합선이라 자기교차 없음 → 브레이크라인 에러 0. 계획 경계(designZ)도 브레이크라인으로.
    /// </summary>
    public static (ObjectId SurfaceId, int Lines) BuildFromBenchLoops(
        Database db, Transaction tr, GradingResult result, IReadOnlyList<Point3> boundary, string baseName)
    {
        if (result.BenchLoops.Count == 0 && boundary.Count < 3)
            throw new InvalidOperationException("형상선이 없습니다(계획고·원지반 표고차 확인).");

        string name = UniqueSurfaceName(db, tr, baseName);
        ObjectId surfId = TinSurface.Create(db, name);
        var tin = (TinSurface)tr.GetObject(surfId, OpenMode.ForWrite);

        // 전역 중복점 제거(먼저 추가된 게 우선) → Civil3D "중복 무시" 정보 로그 도배 방지
        var seen = new HashSet<(long, long)>();
        int lines = 0;
        if (AddLoopBreakline(tin, boundary, seen)) lines++;       // 계획 경계(평지 가장자리)
        foreach (var loop in result.BenchLoops)
            if (AddLoopBreakline(tin, loop, seen)) lines++;       // 각 단 모서리
        foreach (var loop in result.DaylightLoops)
            AddLoopBreakline(tin, loop, seen);                    // daylight toe(원지반 표고) → 정확히 맞닿음

        tin.Rebuild();
        return (surfId, lines);
    }

    /// <summary>
    /// Ray-casting 단면 결과로 TIN 생성 — 단면 점들은 '정점(AddVertices)'으로(교차 위험 0), 계획 경계와
    /// daylight는 브레이크라인으로(daylight는 외부 AI 자문대로 Breakline+Outer Boundary 둘 다). 입력은 세척됨.
    /// </summary>
    public static (ObjectId SurfaceId, int VertexCount) BuildSurfaceFromRaycast(
        Database db, Transaction tr, IReadOnlyList<Point3> vertices, IReadOnlyList<Point3> daylight,
        IReadOnlyList<Point3> boundary, string baseName)
    {
        if (vertices.Count < 3)
            throw new InvalidOperationException("단면 점이 너무 적습니다(계획고·원지반/설정 확인).");

        string name = UniqueSurfaceName(db, tr, baseName);
        ObjectId surfId = TinSurface.Create(db, name);
        var tin = (TinSurface)tr.GetObject(surfId, OpenMode.ForWrite);

        // 단면 점 = 정점(브레이크라인 아님 → 서로 교차 불가). 전역 중복점 제거(초근접 오류 방지).
        var seen = new HashSet<(long, long)>();
        var vc = new Point3dCollection();
        foreach (var v in vertices)
        {
            var key = ((long)Math.Round(v.X * 100), (long)Math.Round(v.Y * 100)); // 0.01m 격자 중복 제거
            if (!seen.Add(key)) continue;
            vc.Add(new Point3d(v.X, v.Y, v.Z));
        }
        if (vc.Count >= 3) tin.AddVertices(vc);
        tin.Rebuild();

        // 계획 경계(평지 가장자리) 브레이크라인 — 평면 가장자리 고정.
        var blSeen = new HashSet<(long, long)>();
        AddLoopBreakline(tin, BoundaryRing(boundary), blSeen);
        // daylight 브레이크라인(원지반 고도 고정) — 정확히 1mm 맞물림.
        if (daylight != null && daylight.Count >= 3) AddLoopBreakline(tin, daylight, blSeen);
        tin.Rebuild();

        // daylight = 외부 경계(파괴식)로 바깥 삼각형 잘라 graded 영역만.
        if (daylight != null && daylight.Count >= 3)
        {
            var pc = new Point3dCollection();
            foreach (var pt in daylight) pc.Add(new Point3d(pt.X, pt.Y, pt.Z));
            var f = daylight[0]; pc.Add(new Point3d(f.X, f.Y, f.Z));
            try { tin.BoundariesDefinition.AddBoundaries(pc, 1.0, Autodesk.Civil.SurfaceBoundaryType.Outer, false); } catch { }
            tin.Rebuild();
        }
        return (surfId, vc.Count);
    }

    private static List<Point3> BoundaryRing(IReadOnlyList<Point3> boundary)
    {
        var r = new List<Point3>(boundary.Count + 1);
        foreach (var v in boundary) r.Add(v);
        if (boundary.Count >= 1) r.Add(boundary[0]);
        return r;
    }

    /// <summary>
    /// NTS 통합 정지면 TIN — 단 모서리 링들을 브레이크라인으로(동심 nested, 비교차), daylight 외곽선은
    /// '브레이크라인이 아니라' 외부 경계(Outer Boundary, 파괴식)로만 넣는다 → 브레이크라인 교차 오류 0.
    /// 입력 링/경계는 NtsGrading에서 이미 세척(weed)·Z복원됨. 전역 중복점 제거로 초근접 오류도 방지.
    /// </summary>
    public static (ObjectId SurfaceId, int Lines) BuildSurfaceFromRings(
        Database db, Transaction tr, IReadOnlyList<List<Point3>> rings, IReadOnlyList<Point3> outerBoundary, string baseName)
    {
        string name = UniqueSurfaceName(db, tr, baseName);
        ObjectId surfId = TinSurface.Create(db, name);
        var tin = (TinSurface)tr.GetObject(surfId, OpenMode.ForWrite);

        var seen = new HashSet<(long, long)>();
        int lines = 0;
        foreach (var ring in rings)
            if (AddLoopBreakline(tin, ring, seen)) lines++; // 단 모서리(비교차)

        // [0624-ah: af 복원] daylight를 브레이크라인으로도 추가 → toe(원지반과 만나는 선)가 crisp.
        //   단 모서리는 daylight 0.1m 안쪽 인셋이라 daylight 브레이크라인과 교차하지 않음(교차 오류 0 유지).
        //   ag에서 이걸 빼고 Outer 전용으로 했더니 toe가 뭉개졌다 → 복원.
        if (outerBoundary != null && outerBoundary.Count >= 3)
            AddLoopBreakline(tin, outerBoundary, seen);
        tin.Rebuild();

        // daylight = 외부 경계(파괴식): 바깥 삼각형을 잘라 graded 영역만 남김.
        if (outerBoundary != null && outerBoundary.Count >= 3)
        {
            var pc = new Point3dCollection();
            foreach (var pt in outerBoundary) pc.Add(new Point3d(pt.X, pt.Y, pt.Z));
            var f = outerBoundary[0]; pc.Add(new Point3d(f.X, f.Y, f.Z)); // 닫기
            try { tin.BoundariesDefinition.AddBoundaries(pc, 1.0, Autodesk.Civil.SurfaceBoundaryType.Outer, false); } catch { }
            tin.Rebuild();
        }
        return (surfId, lines);
    }

    private static bool AddLoopBreakline(TinSurface tin, IReadOnlyList<Point3> loop, HashSet<(long, long)> seen)
    {
        if (loop.Count < 3) return false;
        var pc = new Point3dCollection();
        foreach (var pt in loop)
        {
            var key = ((long)Math.Round(pt.X * 1000), (long)Math.Round(pt.Y * 1000));
            if (!seen.Add(key)) continue; // 이미 다른 선이 쓴 (x,y)는 건너뜀(중복 방지)
            pc.Add(new Point3d(pt.X, pt.Y, pt.Z));
        }
        if (pc.Count < 2) return false;
        try { tin.BreaklinesDefinition.AddStandardBreaklines(pc, 1.0, 0.0, 0.0, 0.0); return true; }
        catch { return false; }
    }

    public readonly record struct VolumeOutcome(bool Ok, ObjectId VolumeSurfaceId, double Cut, double Fill, double Net);

    /// <summary>
    /// 원지반(base) 대비 정지면(comparison) 토량지표면을 만들고 재작성한다.
    /// 두 표면이 모두 커밋된 뒤 별도 트랜잭션에서 호출해야 "!"(최신 아님) 없이 만들어진다.
    /// </summary>
    public static VolumeOutcome CreateVolume(Database db, Transaction tr,
        ObjectId groundSurfaceId, ObjectId gradeSurfaceId, string baseName)
    {
        try
        {
            string volName = UniqueSurfaceName(db, tr, baseName + "_체적");
            ObjectId volId = TinVolumeSurface.Create(volName, groundSurfaceId, gradeSurfaceId);
            var vol = (TinVolumeSurface)tr.GetObject(volId, OpenMode.ForWrite);
            vol.Rebuild();
            var vp = vol.GetVolumeProperties();
            double cut = vp.UnadjustedCutVolume, fill = vp.UnadjustedFillVolume;
            return new VolumeOutcome(true, volId, cut, fill, fill - cut);
        }
        catch
        {
            return new VolumeOutcome(false, ObjectId.Null, 0, 0, 0);
        }
    }

    /// <summary>같은 이름의 surface가 있으면 _2, _3 … 으로 회피.</summary>
    private static string UniqueSurfaceName(Database db, Transaction tr, string baseName)
    {
        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (ObjectId id in CivilSurfaceIds(db, tr))
        {
            if (tr.GetObject(id, OpenMode.ForRead) is Autodesk.Civil.DatabaseServices.Surface s)
                existing.Add(s.Name);
        }
        if (!existing.Contains(baseName)) return baseName;
        for (int i = 2; ; i++)
        {
            string cand = $"{baseName}_{i}";
            if (!existing.Contains(cand)) return cand;
        }
    }

    private static IEnumerable<ObjectId> CivilSurfaceIds(Database db, Transaction tr)
    {
        var civilDoc = Autodesk.Civil.ApplicationServices.CivilApplication.ActiveDocument;
        foreach (ObjectId id in civilDoc.GetSurfaceIds())
            yield return id;
    }
}
