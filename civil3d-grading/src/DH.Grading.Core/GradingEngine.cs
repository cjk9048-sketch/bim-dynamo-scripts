namespace DH.Grading.Core;

/// <summary>
/// 계단식 정지(절성토) 엔진 — distance-field 격자(단일값). 한 위치에 높이 하나라 보타이·접힘·자기교차가 불가능.
///
/// 거리장에서 (1) 각 단 모서리를 '등거리 윤곽'으로 추적해 깨끗한 형상선(BenchLoops)을,
/// (2) 정지면이 원지반과 만나는 daylight 폐합선(DaylightLoops)을 만든다. 모두 단일값에서 추출하므로
/// 서로 교차하지 않아 Civil3D 브레이크라인 에러가 0이다. 오목/복잡 다각형에서도 안전.
///
/// v1은 부지를 한 방향(성토 또는 절토, 중심부 기준)으로 보고 단 표고를 정한다. 혼합 절성토는 추후.
/// </summary>
public static class GradingEngine
{
    public static GradingResult Run(IReadOnlyList<Point3> boundary, IGroundSurface ground, GradingParams p)
    {
        if (boundary == null || boundary.Count < 3)
            throw new ArgumentException("경계 폴리곤은 최소 3개 정점이 필요합니다.", nameof(boundary));
        ArgumentNullException.ThrowIfNull(ground);
        p.Validate();

        var result = new GradingResult();
        var plane = PolygonGeometry.FitPlane(boundary);

        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        double cxs = 0, cys = 0;
        foreach (var v in boundary)
        {
            minX = Math.Min(minX, v.X); maxX = Math.Max(maxX, v.X);
            minY = Math.Min(minY, v.Y); maxY = Math.Max(maxY, v.Y);
            cxs += v.X; cys += v.Y;
        }
        double centroidX = cxs / boundary.Count, centroidY = cys / boundary.Count;
        double designZ0 = plane.At(centroidX, centroidY); // 계획면 대표 표고(형상선 단 표고 기준)

        double margin = Math.Max(
            BenchProfile.MaxReach(p.BenchHeight, p.BenchWidth, p.CutSlope, p.MaxBenches),
            BenchProfile.MaxReach(p.BenchHeight, p.BenchWidth, p.FillSlope, p.MaxBenches));
        if (p.MiterConvex) margin *= 1.5; // 직각 모서리는 볼록부에서 바깥으로 더 뻗으므로 격자 여유를 키움
        minX -= margin; minY -= margin; maxX += margin; maxY += margin;

        double cell = p.CellSize;
        double cellArea = cell * cell;
        int nx = (int)Math.Ceiling((maxX - minX) / cell);
        int ny = (int)Math.Ceiling((maxY - minY) / cell);

        var interior = new bool[nx + 1, ny + 1];
        var cons = new bool[nx + 1, ny + 1];     // 구성측(daylight 안쪽)
        var dist = new double[nx + 1, ny + 1];   // 경계까지 바깥거리
        for (int i = 0; i <= nx; i++) for (int j = 0; j <= ny; j++) dist[i, j] = double.MaxValue;
        var zField = new double[nx + 1, ny + 1]; // 셀 표면고(평지=계획고, 비탈=proposed) — 단 표고 등고선 추출용

        for (int iy = 0; iy <= ny; iy++)
        {
            double y = minY + iy * cell;
            for (int ix = 0; ix <= nx; ix++)
            {
                double x = minX + ix * cell;
                if (PolygonGeometry.Contains(boundary, x, y))
                {
                    double zPlat = plane.At(x, y);
                    result.Points.Add(new Point3(x, y, zPlat));
                    result.PlatformCellCount++;
                    interior[ix, iy] = true; cons[ix, iy] = true; dist[ix, iy] = 0;
                    zField[ix, iy] = zPlat;
                    AccumulateVolume(result, ground, x, y, zPlat, cellArea);
                    if (ground.TryGetElevation(x, y, out double gPlat)) // 차이표면 점(계획면 내부)
                        result.DiffPoints.Add(new Point3(x, y, zPlat - gPlat));
                    continue;
                }

                var (d, cx, cy, _) = PolygonGeometry.ClosestBoundary(boundary, x, y, p.MiterConvex, p.MiterLimit);
                if (!ground.TryGetElevation(cx, cy, out double gBoundary)) continue;
                if (!ground.TryGetElevation(x, y, out double gCell)) continue;

                double designZ = plane.At(cx, cy);
                bool fillHere = designZ > gBoundary;
                double nLoc = fillHere ? p.FillSlope : p.CutSlope;
                double h = BenchProfile.Height(d, p.BenchHeight, p.BenchWidth, nLoc, p.MaxBenches);
                double proposed = fillHere ? designZ - h : designZ + h;
                double diff = fillHere ? proposed - gCell : gCell - proposed;
                if (diff < -p.BenchHeight) continue; // daylight 너머 1단 버퍼까지

                // 재설계 1단계: JACK 지시 '+1단까지 더한 통합지표면'. daylight 너머 1단 버퍼(diff>−BenchHeight,
                // 위 :81에서 컷)까지 표면에 포함해 정지면이 원지반을 양쪽으로 넘기게 한다 → JACK이 '지표면 사이
                // 최소거리'로 교선(daylight)을 직접 뽑을 수 있다.
                result.Points.Add(new Point3(x, y, proposed));
                result.DiffPoints.Add(new Point3(x, y, proposed - gCell));
                dist[ix, iy] = d;
                if (diff > 0)
                {
                    cons[ix, iy] = true;
                    zField[ix, iy] = proposed;
                    result.SlopeCellCount++;
                    AccumulateVolume(result, ground, x, y, proposed, cellArea);
                }
            }
        }

        // daylight 폐합선 — 구성측 마스크 경계(격자라 실제 toe를 정확히 따라가나 계단).
        // 격자 계단은 '단순화(Douglas-Peucker, 꺾임점 제거)'로 직선화한다 — 이동평균과 달리 선을 안쪽으로
        // 수축시키지 않으므로 '경계가 과하게 안으로 들어오던' 문제가 없다. 그 뒤 약한 이동평균으로 각진 곳만 부드럽게.
        double dayCloseTol = cell * 3;
        foreach (var raw in TraceBoundary((i, j) => cons[i, j], nx, ny, minX, minY, cell, 0, simplifyTol: cell * 3))
        {
            var loop = SmoothChain(raw, 3, dayCloseTol); // 단순화로 이미 계단이 펴졌으니 약하게(수축 거의 없음)
            for (int i = 0; i < loop.Count; i++)
                if (ground.TryGetElevation(loop[i].X, loop[i].Y, out double gz))
                    loop[i] = new Point3(loop[i].X, loop[i].Y, gz);
            // 첫 점 복제로 명시적으로 닫음 — 화면 폴리라인에서 마지막 한 변이 빠져 틈이 보이지 않게(형상선과 동일 관례).
            if (loop.Count >= 3)
            {
                var f = loop[0]; var e = loop[^1];
                if (Math.Abs(f.X - e.X) > 1e-6 || Math.Abs(f.Y - e.Y) > 1e-6) loop.Add(f);
            }
            result.DaylightLoops.Add(loop);
        }

        // 계획면 외곽선 = 입력 계획 폴리곤 '그대로'(격자 근사가 아니라 원본 정점) → 입력선과 정확히 일치.
        // 격자 마스크 경계로 뽑으면 폴리곤에서 0.5~1셀 밀리므로, 가장 안쪽 기준선은 원본을 쓴다.
        {
            var planRing = new List<Point3>(boundary.Count + 1);
            foreach (var v in boundary) planRing.Add(new Point3(v.X, v.Y, v.Z));
            if (planRing.Count >= 3) { planRing.Add(planRing[0]); result.BenchLoops.Add(planRing); } // 첫 점 복제로 닫음
        }

        // 단(段) 모서리 형상선 — 모서리 처리 방식에 따라 두 엔진으로 갈린다.
        //  · 직각(마이터) 모드: marching-squares 등치선(표고장 zField). 격자가 수평·수직 축에 정렬돼 계단이 안 보인다.
        //  · 라운드 모드: '기하 오프셋'(ExtractRoundBenchLines). 격자를 안 거치므로 곡선부 잔물결이 없다.
        // 두 경우 모두 절토는 계획고+5k, 성토는 계획고−5k 표고. 소단은 평탄이라 같은 표고에 '안쪽 모서리(비탈 위 끝)'와
        // '바깥쪽 모서리(다음 비탈 시작)' 두 선이 있다.
        if (p.MiterConvex)
        {
            // 등치선 level=소단 모서리에 정확(δ만큼만). 격자축 정렬이라 직각 부지에서 계단이 드러나지 않는다.
            double off = p.BenchHeight * 1e-6;
            for (int k = 1; k <= p.MaxBenches; k++)
            {
                double zc = designZ0 + p.BenchHeight * k; // 절토 단 k 표고(위로)
                double zf = designZ0 - p.BenchHeight * k; // 성토 단 k 표고(아래로)
                bool any = false;
                foreach (var lv in new[] { zc, zc + off })
                    foreach (var loop in ExtractIsoContours((i, j) => cons[i, j] ? zField[i, j] : double.NaN, nx, ny, minX, minY, cell, lv, zc))
                        if (loop.Count >= 3) { result.BenchLoops.Add(loop); any = true; }
                foreach (var lv in new[] { zf, zf - off })
                    foreach (var loop in ExtractIsoContours((i, j) => cons[i, j] ? -zField[i, j] : double.NaN, nx, ny, minX, minY, cell, -lv, zf))
                        if (loop.Count >= 3) { result.BenchLoops.Add(loop); any = true; }
                if (!any) break; // 이 단 이상으로는 절토·성토 모두 없음 → 종료(폭주 방지)
            }
        }
        else
        {
            foreach (var loop in ExtractRoundBenchLines(boundary, ground, plane, p, designZ0))
                result.BenchLoops.Add(loop);
        }

        MarkDaylight(result, cell);
        return result;
    }

    /// <summary>
    /// 재설계(JACK): 절토 지표면과 성토 지표면을 '각각' 만들 점군을 반환한다.
    /// 원지반은 단수(N=p.MaxBenches, 호출부 BuildParams에서 원지반 깊이로 이미 계산됨)에만 반영되고,
    /// 여기서는 원지반과의 교차를 '무시'하고 N단까지 끝까지 단을 친다.
    /// 둘 다 내부 계획면(평탄, designZ)을 포함한다(JACK: 내부 채움 양쪽 다).
    /// 절토면 = 계획면 + (designZ + h(d)) (위로), 성토면 = 계획면 + (designZ − h(d)) (아래로).
    /// 각 표면과 원지반의 교선(daylight)은 호출부/사용자가 '지표면 사이 최소거리'로 따로 뽑는다.
    /// </summary>
    public static (List<Point3> Cut, List<Point3> Fill) BuildCutFillSurfaces(IReadOnlyList<Point3> boundary, GradingParams p)
    {
        if (boundary == null || boundary.Count < 3)
            throw new ArgumentException("경계 폴리곤은 최소 3개 정점이 필요합니다.", nameof(boundary));
        p.Validate();

        var plane = PolygonGeometry.FitPlane(boundary);
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        foreach (var v in boundary)
        {
            minX = Math.Min(minX, v.X); maxX = Math.Max(maxX, v.X);
            minY = Math.Min(minY, v.Y); maxY = Math.Max(maxY, v.Y);
        }

        // N단까지의 도달거리 — 단은 여기까지만 친다(원지반 위치와 무관).
        double cutReach = BenchProfile.MaxReach(p.BenchHeight, p.BenchWidth, p.CutSlope, p.MaxBenches);
        double fillReach = BenchProfile.MaxReach(p.BenchHeight, p.BenchWidth, p.FillSlope, p.MaxBenches);
        double margin = Math.Max(cutReach, fillReach);
        if (p.MiterConvex) margin *= 1.5;
        minX -= margin; minY -= margin; maxX += margin; maxY += margin;

        double cell = p.CellSize;
        int nx = (int)Math.Ceiling((maxX - minX) / cell);
        int ny = (int)Math.Ceiling((maxY - minY) / cell);

        var cut = new List<Point3>();
        var fill = new List<Point3>();
        for (int iy = 0; iy <= ny; iy++)
        {
            double y = minY + iy * cell;
            for (int ix = 0; ix <= nx; ix++)
            {
                double x = minX + ix * cell;
                if (PolygonGeometry.Contains(boundary, x, y))
                {
                    double z = plane.At(x, y); // 내부 계획면(평탄) — 양쪽 표면에 모두 포함
                    cut.Add(new Point3(x, y, z));
                    fill.Add(new Point3(x, y, z));
                    continue;
                }
                var (d, cx, cy, _) = PolygonGeometry.ClosestBoundary(boundary, x, y, p.MiterConvex, p.MiterLimit);
                double designZ = plane.At(cx, cy);
                if (d <= cutReach)
                {
                    double hc = BenchProfile.Height(d, p.BenchHeight, p.BenchWidth, p.CutSlope, p.MaxBenches);
                    cut.Add(new Point3(x, y, designZ + hc)); // 절토: 위로 단
                }
                if (d <= fillReach)
                {
                    double hf = BenchProfile.Height(d, p.BenchHeight, p.BenchWidth, p.FillSlope, p.MaxBenches);
                    fill.Add(new Point3(x, y, designZ - hf)); // 성토: 아래로 단
                }
            }
        }
        return (cut, fill);
    }

    /// <summary>
    /// 라운드(둥근 모서리) 단 형상선을 '기하 오프셋'으로 추출한다 — 격자를 거치지 않으므로 곡선부 잔물결이 없다.
    /// 폴리곤 둘레를 촘촘히 샘플(볼록 코너는 부채꼴=원호)하고, 각 점을 바깥 단위법선으로 단별 누적거리만큼 평행이동한다.
    /// 자기교차는 '참 거리 클립'으로 제거한다: 오프셋점의 실제 최근접 경계거리가 평행이동거리와 같을 때만 채택 →
    /// 오목 코너에서 두 변 오프셋이 겹치는(접히는) 부분이 자동으로 잘려, 단일값 거리장 등거리선과 똑같이 자기교차가 0이다.
    /// 절토 변(계획고&gt;지반)은 위로(designZ0+5k), 성토 변은 아래로(−5k). daylight(단 표고가 원지반과 만나는 곳) 너머는 자른다.
    /// 소단은 평탄이라 같은 표고에 안쪽(dIn=비탈 위 끝)·바깥쪽(dOut=다음 비탈 시작) 두 링을 만든다.
    /// </summary>
    private static List<List<Point3>> ExtractRoundBenchLines(
        IReadOnlyList<Point3> boundary, IGroundSurface ground, Plane plane, GradingParams p, double designZ0)
    {
        int n = boundary.Count;
        var loops = new List<List<Point3>>();
        if (n < 3) return loops;

        double ccw = Math.Sign(PolygonGeometry.SignedArea(boundary));
        if (ccw == 0) ccw = 1;

        // 변별 단위 방향(dir)·바깥 단위 법선(nrm).
        var dir = new (double x, double y)[n];
        var nrm = new (double x, double y)[n];
        for (int i = 0; i < n; i++)
        {
            var a = boundary[i]; var b = boundary[(i + 1) % n];
            double ex = b.X - a.X, ey = b.Y - a.Y;
            double len = Math.Sqrt(ex * ex + ey * ey);
            if (len < 1e-12) { dir[i] = (0, 0); nrm[i] = (0, 0); continue; }
            ex /= len; ey /= len;
            dir[i] = (ex, ey);
            nrm[i] = ccw > 0 ? (ey, -ex) : (-ey, ex);
        }

        // 꼭짓점 볼록/오목 판정 + 변별 절토/성토(변 중점의 계획고 vs 원지반).
        var convex = new bool[n];
        var edgeFill = new bool[n];
        var edgeKnown = new bool[n];
        for (int i = 0; i < n; i++)
        {
            int pe = (i - 1 + n) % n;
            double crs = nrm[pe].x * nrm[i].y - nrm[pe].y * nrm[i].x;
            convex[i] = crs * ccw > 1e-12; // 법선이 폴리곤 방향으로 회전 = 볼록
            var a = boundary[i]; var b = boundary[(i + 1) % n];
            double mx = (a.X + b.X) * 0.5, my = (a.Y + b.Y) * 0.5;
            if (ground.TryGetElevation(mx, my, out double g)) { edgeFill[i] = plane.At(mx, my) > g; edgeKnown[i] = true; }
        }

        double targetChord = Math.Max(0.2, p.BenchWidth * 0.3); // 원호 facet 목표 현(弦) 길이(m) — d가 커도 매끈.
        double edgeStep = Math.Max(0.25, Math.Min(p.CellSize, p.BenchWidth * 0.5)); // 직선 변 샘플 간격 — 클립이 변 중간도 잡아 누락 방지.
        double minOpenLen = Math.Max(p.BenchWidth * 3, p.CellSize * 4); // 열린 형상선 최소 길이 — 이보다 짧은 매달린 단 부스러기(stub)는 안 그림.

        for (int k = 1; k <= p.MaxBenches; k++)
        {
            bool anyThisK = false;
            // 절토 링·성토 링은 표고가 반대라 따로 만든다(한 폴리라인에 두 표고가 섞이지 않게).
            foreach (bool fillRing in new[] { false, true })
            {
                double slopeN = Math.Max(fillRing ? p.FillSlope : p.CutSlope, p.MinSlope);
                double slopeRun = Math.Max(p.BenchHeight * slopeN, p.MinFaceRun);
                double period = slopeRun + p.BenchWidth;
                double zBench = fillRing ? designZ0 - p.BenchHeight * k : designZ0 + p.BenchHeight * k;
                double dIn = (k - 1) * period + slopeRun; // 소단 안쪽 모서리(비탈 위 끝)
                double dOut = k * period;                 // 소단 바깥 모서리(다음 비탈 시작)

                foreach (double d in new[] { dIn, dOut })
                {
                    // 1) 거리 d의 기하 오프셋 링 — 볼록=적응형 원호, 오목=두 오프셋선 교점(직각 모서리). 변 인덱스 태그.
                    var rx = new List<double>(); var ry = new List<double>(); var re = new List<int>();
                    void AddPt(double x, double y, int edge)
                    {
                        if (rx.Count > 0)
                        {
                            double ddx = x - rx[^1], ddy = y - ry[^1];
                            if (ddx * ddx + ddy * ddy < 1e-12) return; // 연속 중복 제거
                        }
                        rx.Add(x); ry.Add(y); re.Add(edge);
                    }
                    // 변별 오프셋 끝점 A[i]=V[i]+d·N[i], 변벡터 EV[i]=V[i+1]−V[i].
                    var Ax = new double[n]; var Ay = new double[n];
                    var EVx = new double[n]; var EVy = new double[n]; var EVlen2 = new double[n];
                    for (int i = 0; i < n; i++)
                    {
                        var Vi = boundary[i]; var Vn = boundary[(i + 1) % n];
                        Ax[i] = Vi.X + nrm[i].x * d; Ay[i] = Vi.Y + nrm[i].y * d;
                        EVx[i] = Vn.X - Vi.X; EVy[i] = Vn.Y - Vi.Y;
                        EVlen2[i] = EVx[i] * EVx[i] + EVy[i] * EVy[i];
                    }

                    // 오목 꼭짓점은 두 변 오프셋선의 교점 X로 모서리를 만든다(마이터 한계 내). 각 변은 X에서 잘린다(되짚기 제거).
                    var Mx = new double[n]; var My = new double[n]; var mitered = new bool[n];
                    for (int i = 0; i < n; i++)
                    {
                        if (convex[i]) continue;
                        int pe = (i - 1 + n) % n;
                        var Vi = boundary[i];
                        if ((nrm[pe].x == 0 && nrm[pe].y == 0) || (nrm[i].x == 0 && nrm[i].y == 0)) continue;
                        if (LineIntersect(Ax[pe], Ay[pe], dir[pe].x, dir[pe].y, Ax[i], Ay[i], dir[i].x, dir[i].y, out double ix, out double iy))
                        {
                            double sx = ix - Vi.X, sy = iy - Vi.Y;
                            double lim = Math.Min(p.MiterLimit, 1.7) * d; // 한계를 조여(≤1.7d) 오목 마이터 스파이크 방지
                            if (sx * sx + sy * sy <= lim * lim) { Mx[i] = ix; My[i] = iy; mitered[i] = true; }
                        }
                    }

                    // 변 i를 따라가는 시작/끝 파라미터(t∈[0,1] = A[i]→B[i]). 오목 마이터면 교점 위치로 잘림.
                    double Proj(int e, double px, double py)
                        => EVlen2[e] < 1e-18 ? 0 : ((px - Ax[e]) * EVx[e] + (py - Ay[e]) * EVy[e]) / EVlen2[e];
                    for (int i = 0; i < n; i++)
                    {
                        int pe = (i - 1 + n) % n; int ni = (i + 1) % n;
                        bool iZero = nrm[i].x == 0 && nrm[i].y == 0;
                        if (iZero || EVlen2[i] < 1e-18) continue;
                        var Vi = boundary[i];

                        // 꼭짓점 i 모서리(이전 변 pe → 변 i 연결).
                        if (convex[i] && !(nrm[pe].x == 0 && nrm[pe].y == 0))
                        {
                            // 볼록: N[pe]→N[i] 원호(반지름 d). 분할을 d에 맞춰(현 길이≈targetChord).
                            double dot = nrm[pe].x * nrm[i].x + nrm[pe].y * nrm[i].y;
                            double crs = nrm[pe].x * nrm[i].y - nrm[pe].y * nrm[i].x;
                            double turn = Math.Atan2(crs, dot);
                            double dth = 2 * Math.Asin(Math.Min(1, targetChord / (2 * Math.Max(d, 1e-6))));
                            int steps = Math.Max(1, Math.Min(96, (int)Math.Ceiling(Math.Abs(turn) / Math.Max(dth, 1e-6))));
                            for (int s = 0; s <= steps; s++)
                            {
                                double th = turn * s / steps;
                                double cx = nrm[pe].x * Math.Cos(th) - nrm[pe].y * Math.Sin(th);
                                double cy = nrm[pe].x * Math.Sin(th) + nrm[pe].y * Math.Cos(th);
                                AddPt(Vi.X + cx * d, Vi.Y + cy * d, s * 2 < steps ? pe : i);
                            }
                        }
                        else if (mitered[i])
                            AddPt(Mx[i], My[i], i); // 오목 직각 모서리(교점)
                        else if (!(nrm[pe].x == 0 && nrm[pe].y == 0))
                        {
                            // 베벨 폴백(마이터 한계 초과/평행): 두 변 오프셋 끝점을 직접 연결.
                            AddPt(Vi.X + nrm[pe].x * d, Vi.Y + nrm[pe].y * d, pe);
                            AddPt(Ax[i], Ay[i], i);
                        }

                        // 변 i 몸통 — 마이터면 교점에서 잘린 t구간만, edgeStep 간격으로 촘촘히(클립이 변 중간도 잡음 → 누락 방지).
                        double ts = mitered[i] ? Proj(i, Mx[i], My[i]) : 0.0;
                        double te = mitered[ni] ? Proj(i, Mx[ni], My[ni]) : 1.0;
                        if (te > ts + 1e-9)
                        {
                            double seglen = (te - ts) * Math.Sqrt(EVlen2[i]);
                            int es = Math.Max(1, (int)Math.Ceiling(seglen / edgeStep));
                            for (int s = 1; s <= es; s++)
                            {
                                double t = ts + (te - ts) * s / es;
                                AddPt(Ax[i] + EVx[i] * t, Ay[i] + EVy[i] * t, i);
                            }
                        }
                    }
                    int m = rx.Count;
                    // 닫힘용으로 첫·끝이 겹치면 끝점 제거(EmitRoundRing이 닫음 처리).
                    if (m >= 2)
                    {
                        double fdx = rx[0] - rx[m - 1], fdy = ry[0] - ry[m - 1];
                        if (fdx * fdx + fdy * fdy < 1e-9) { rx.RemoveAt(m - 1); ry.RemoveAt(m - 1); re.RemoveAt(m - 1); m--; }
                    }
                    if (m < 3) continue;

                    // 2) 클립: 변측(절/성토) 일치 + 참거리(전역 겹침 제거) + daylight.
                    //    위치 q는 '모든' 점에 채운다(마스크 잡음정리 때 짧은 구멍을 메우려면 좌표가 필요).
                    var ok = new bool[m]; var q = new Point3[m];
                    double clipEps = Math.Max(0.05, d * 0.02);
                    for (int i = 0; i < m; i++)
                    {
                        q[i] = new Point3(rx[i], ry[i], zBench);
                        int e = re[i];
                        if (!edgeKnown[e] || edgeFill[e] != fillRing) continue;
                        var (dist, _, _, _) = PolygonGeometry.ClosestBoundary(boundary, rx[i], ry[i], false);
                        if (dist < d - clipEps) continue; // 더 가까운 변이 있으면(겹침/좁은목 충돌) 버림
                        if (!ground.TryGetElevation(rx[i], ry[i], out double gq)) continue;
                        double diff = fillRing ? zBench - gq : gq - zBench;
                        if (diff < -p.BenchHeight * 0.05) continue; // daylight 너머(토 한 줄만 허용)
                        ok[i] = true;
                    }
                    // 마스크 잡음정리 — 실제 TIN 지반 떨림으로 daylight 부근 점이 kept/dropped 번갈아 생기는
                    // 톱니·짧은 stub를 없앤다. 짧은 구멍(~소단폭)은 메우고, 짧은 섬은 지워 '깔끔한 한 번 절단'으로.
                    int span = Math.Max(2, (int)Math.Ceiling(p.BenchWidth / edgeStep));
                    CleanCyclicMask(ok, m, span);
                    if (EmitRoundRing(ok, q, m, minOpenLen, loops)) anyThisK = true;
                }
            }
            if (!anyThisK) break; // 절토·성토 모두 이 단에서 사라짐(전부 daylight 너머) → 종료
        }
        return loops;
    }

    /// <summary>
    /// 매끈한 daylight(정지면이 원지반과 만나는 toe) 외곽 폐합선 — 격자 없이 경계 각 점에서 바깥 법선으로
    /// ray-march해 정지 제안고가 원지반과 만나는 지점을 찾는다. 격자 트레이스의 계단을 없애 표면 외곽이 매끈.
    /// 안쪽 봉우리 구멍은 다루지 않는다(외곽 1개 루프만 반환). 어떤 점도 daylight를 못 만나면 빈 결과.
    /// </summary>
    public static List<List<Point3>> ExtractGeometricDaylight(
        IReadOnlyList<Point3> boundary, IGroundSurface ground, Plane plane, GradingParams p)
    {
        var loops = new List<List<Point3>>();
        int n = boundary.Count;
        if (n < 3) return loops;

        var (dir, nrm, convex) = EdgeFrames(boundary);
        double targetChord = Math.Max(0.2, p.BenchWidth * 0.3);
        double edgeStep = Math.Max(0.25, Math.Min(p.CellSize, p.BenchWidth * 0.5));
        var ring0 = BuildOffsetRingN(boundary, dir, nrm, convex, n, 0.0, targetChord, edgeStep, p.MiterLimit);
        int m = ring0.Count;
        if (m < 3) return loops;

        double maxReach = Math.Max(
            BenchProfile.MaxReach(p.BenchHeight, p.BenchWidth, p.CutSlope, p.MaxBenches),
            BenchProfile.MaxReach(p.BenchHeight, p.BenchWidth, p.FillSlope, p.MaxBenches));
        double march = Math.Max(edgeStep, 0.25);
        int steps = Math.Max(4, (int)Math.Ceiling(maxReach / march));

        var pts = new Point3[m]; var ok = new bool[m];
        for (int j = 0; j < m; j++)
        {
            double px = ring0[j].X, py = ring0[j].Y, nx = ring0[j].Nx, ny = ring0[j].Ny;
            if (!ground.TryGetElevation(px, py, out double g0)) continue;
            double dz = plane.At(px, py);
            bool fillHere = dz > g0;
            double nLoc = Math.Max(fillHere ? p.FillSlope : p.CutSlope, p.MinSlope);

            double prevDiff = double.NaN, prevT = 0;
            // 마지막 유효(원지반 데이터 안) 지점 — 비탈이 데이터 끝(또는 최대단수)까지 가도 지반과 안 만나면
            // 그 점을 '건너뛰고 직선으로 가로지르기'(화면을 횡단하는 가짜 daylight 선) 대신 여기서 끊어
            // 둘레를 따라 연속되게 한다.
            double lastX = px, lastY = py, lastZ = g0; bool haveLast = false;
            for (int s = 1; s <= steps; s++)
            {
                double t = maxReach * s / steps;
                double qx = px + nx * t, qy = py + ny * t;
                if (!ground.TryGetElevation(qx, qy, out double gq)) break;
                lastX = qx; lastY = qy; lastZ = gq; haveLast = true;
                double h = BenchProfile.Height(t, p.BenchHeight, p.BenchWidth, nLoc, p.MaxBenches);
                double proposed = fillHere ? dz - h : dz + h;
                double diff = fillHere ? proposed - gq : gq - proposed; // >0 = 아직 구성측(정지면이 지반 안쪽)
                if (diff <= 0)
                {
                    double frac = double.IsNaN(prevDiff) || (prevDiff - diff) == 0 ? 1.0 : prevDiff / (prevDiff - diff);
                    double td = prevT + (t - prevT) * frac;
                    double dx2 = px + nx * td, dy2 = py + ny * td;
                    ground.TryGetElevation(dx2, dy2, out double zd);
                    pts[j] = new Point3(dx2, dy2, zd); ok[j] = true; break;
                }
                prevDiff = diff; prevT = t;
            }
            // 교차 못 찾았으나 데이터 안에서 진행은 했으면(지반 데이터 끝/단수 한계) 마지막 지점으로 채움 → 가로지름 방지.
            if (!ok[j] && haveLast) { pts[j] = new Point3(lastX, lastY, lastZ); ok[j] = true; }
        }

        // 찾은 점을 둘레 순서대로 이어 닫힌 외곽선으로(짧은 빈 구간은 직선으로 메워짐 — daylight는 물리적으로 연속).
        int cnt = 0; for (int j = 0; j < m; j++) if (ok[j]) cnt++;
        if (cnt < m * 0.5 || cnt < 4) return loops; // 절반도 못 만나면 신뢰 불가 → 폴백

        var loop = new List<Point3>(cnt + 1);
        for (int j = 0; j < m; j++) if (ok[j]) loop.Add(pts[j]);
        loop = SimplifyClosed(loop, edgeStep * 0.5); // 미세 떨림만 정리(매끈 유지)
        if (loop.Count < 4) return loops;
        loop.Add(loop[0]); // 닫음
        loops.Add(loop);
        return loops;
    }

    /// <summary>두 직선(점+방향)의 교점. 평행이면 false.</summary>
    private static bool LineIntersect(
        double px, double py, double dx, double dy,
        double qx, double qy, double ex, double ey, out double ix, out double iy)
    {
        ix = iy = 0;
        double denom = dx * ey - dy * ex;
        if (Math.Abs(denom) < 1e-12) return false;
        double t = ((qx - px) * ey - (qy - py) * ex) / denom;
        ix = px + t * dx; iy = py + t * dy;
        return true;
    }

    /// <summary>
    /// 유효 오프셋점(ok=true)을 둘레 순서대로 이어 형상선으로 만든다.
    /// 전부 유효면 닫힌 링(첫 점 복제로 닫음), 중간이 잘리면(오목 겹침·daylight) 연속 구간마다 열린 곡선.
    /// </summary>
    private static bool EmitRoundRing(bool[] ok, Point3[] q, int m, double minOpenLen, List<List<Point3>> loops)
    {
        int cnt = 0; for (int i = 0; i < m; i++) if (ok[i]) cnt++;
        if (cnt < 3) return false;

        if (cnt == m) // 전부 유효 → 한 바퀴 닫힌 링
        {
            var ring = new List<Point3>(m + 1);
            for (int i = 0; i < m; i++) ring.Add(q[i]);
            ring = RemoveSpikes(ring, true);
            if (ring.Count < 3) return false;
            ring.Add(ring[0]); // 첫 점 복제로 닫음(기존 형상선 관례와 동일)
            loops.Add(ring);
            return true;
        }

        // 잘린 지점(ok=false) 바로 다음부터 순환하며 연속 유효구간을 열린 곡선으로 끊어 담는다.
        // 너무 짧은 조각(minOpenLen 미만)은 버린다 — daylight 물결로 생긴 1~2m 매달린 단 부스러기(stub) 제거.
        int start = 0; while (start < m && ok[start]) start++;
        bool added = false;
        var cur = new List<Point3>();
        void Flush()
        {
            if (cur.Count >= 2)
            {
                var c = RemoveSpikes(cur, false);
                // 열린 곡선(daylight에서 잘린 맨 바깥 단)은 원지반 물결을 따라 점단위 채택/탈락으로 톱니가 남는다.
                // 양 끝점을 고정한 이동평균으로 톱니만 펴 매끈하게(형태·길이 보존, 자기교차 없음).
                if (c.Count >= 4) c = SmoothChain(c, 8, 1e-9);
                if (c.Count >= 2 && Perim2D(c) >= minOpenLen) { loops.Add(c); added = true; }
            }
            cur = new List<Point3>();
        }
        for (int s = 0; s < m; s++)
        {
            int i = (start + s) % m;
            if (ok[i]) cur.Add(q[i]);
            else Flush();
        }
        Flush();
        return added;
    }

    /// <summary>
    /// 형상선의 '거의 U턴(꺾임각 &gt;155°)' 스파이크 점을 제거한다 — 코너 조인/변 몸통 순서가 어긋나며 생긴
    /// 한 점짜리 되짚기(헤어핀)를 없앤다. 직각(90°)·완만한 곡선·오목 마이터(≤90°)는 보존한다.
    /// </summary>
    private static List<Point3> RemoveSpikes(List<Point3> pts, bool closed)
    {
        // 진짜 코너(직각 90°·오목 마이터 ≤90°)는 꺾임 ≤90°로 확인됨 → 135° 초과만 스파이크로 제거(안전 여유).
        const double cosThresh = -0.707; // cos(135°)
        var cur = pts;
        for (int pass = 0; pass < 8; pass++)
        {
            int n = cur.Count;
            if (n < 3) break;
            var keep = new bool[n];
            for (int i = 0; i < n; i++) keep[i] = true;
            bool any = false;
            int lo = closed ? 0 : 1, hi = closed ? n : n - 1;
            for (int i = lo; i < hi; i++)
            {
                var a = cur[(i - 1 + n) % n]; var b = cur[i]; var c = cur[(i + 1) % n];
                double v1x = b.X - a.X, v1y = b.Y - a.Y, v2x = c.X - b.X, v2y = c.Y - b.Y;
                double l1 = Math.Sqrt(v1x * v1x + v1y * v1y), l2 = Math.Sqrt(v2x * v2x + v2y * v2y);
                if (l1 < 1e-9 || l2 < 1e-9) continue;
                double cos = (v1x * v2x + v1y * v2y) / (l1 * l2);
                if (cos < cosThresh) { keep[i] = false; any = true; }
            }
            if (!any) break;
            var next = new List<Point3>(n);
            for (int i = 0; i < n; i++) if (keep[i]) next.Add(cur[i]);
            cur = next;
        }
        return cur;
    }

    private static double Perim2D(List<Point3> c)
    {
        double L = 0;
        for (int i = 1; i < c.Count; i++) { double dx = c[i].X - c[i - 1].X, dy = c[i].Y - c[i - 1].Y; L += Math.Sqrt(dx * dx + dy * dy); }
        return L;
    }

    /// <summary>
    /// 순환 불리언 마스크의 짧은 잡음을 제거한다 — 짧은 false 구멍(≤span)은 메우고, 짧은 true 섬(≤span)은 지운다.
    /// daylight 부근에서 실제 지반 떨림으로 채택/탈락이 번갈아 생겨 형상선이 톱니·짧은 stub가 되는 것을 막아
    /// '깔끔한 한 번 절단'으로 만든다. 전부 같은 값이면 손대지 않는다(진짜 큰 절단·변측 전환은 보존).
    /// </summary>
    private static void CleanCyclicMask(bool[] ok, int m, int span)
    {
        if (m < 3 || span < 1) return;
        bool anyT = false, anyF = false;
        for (int i = 0; i < m; i++) { anyT |= ok[i]; anyF |= !ok[i]; }
        if (!anyT || !anyF) return; // 전부 채택/전부 탈락 → 그대로
        FlipShortRuns(ok, m, false, span); // 짧은 구멍 메우기
        FlipShortRuns(ok, m, true, span);  // 짧은 섬 제거
    }

    /// <summary>순환 마스크에서 값이 <paramref name="value"/>인 연속 런의 길이가 <paramref name="maxLen"/> 이하면 뒤집는다.</summary>
    private static void FlipShortRuns(bool[] ok, int m, bool value, int maxLen)
    {
        // 경계(값이 바뀌는 지점)에서 시작해야 순환 런을 한 번에 센다.
        int start = -1;
        for (int i = 0; i < m; i++) if (ok[i] != value && ok[(i + 1) % m] == value) { start = (i + 1) % m; break; }
        if (start < 0) return; // value 런이 없음(전부 !value)
        int s = 0;
        while (s < m)
        {
            int i = (start + s) % m;
            if (ok[i] != value) { s++; continue; }
            int len = 0;
            while (s + len < m && ok[(start + s + len) % m] == value) len++;
            if (len <= maxLen)
                for (int t = 0; t < len; t++) ok[(start + s + t) % m] = !value;
            s += len;
        }
    }

    /// <summary>
    /// 정지 영역을 평면 면(面) 폴리곤으로 분류·추출한다 — 계획지표면(평탄)/소단/사면 각각 닫힌 폴리곤.
    /// 격자 각 셀을 종류·단번호로 분류한 뒤, (종류,단)별 마스크 경계를 추적해 폐합선(외곽+구멍)을 만든다.
    /// SHP 내보내기용. Run과 독립적으로 자체 격자 스캔을 수행한다(정지면 생성과 분리).
    /// </summary>
    public static List<AreaPolygon> ExtractAreaPolygons(IReadOnlyList<Point3> boundary, IGroundSurface ground, GradingParams p)
    {
        if (boundary == null || boundary.Count < 3)
            throw new ArgumentException("경계 폴리곤은 최소 3개 정점이 필요합니다.", nameof(boundary));
        ArgumentNullException.ThrowIfNull(ground);
        p.Validate();

        var plane = PolygonGeometry.FitPlane(boundary);
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        double cxs = 0, cys = 0;
        foreach (var v in boundary)
        {
            minX = Math.Min(minX, v.X); maxX = Math.Max(maxX, v.X);
            minY = Math.Min(minY, v.Y); maxY = Math.Max(maxY, v.Y);
            cxs += v.X; cys += v.Y;
        }
        double centroidX = cxs / boundary.Count, centroidY = cys / boundary.Count;
        double designZ0 = plane.At(centroidX, centroidY);
        bool isFill = !ground.TryGetElevation(centroidX, centroidY, out double gC0) || designZ0 >= gC0;

        double slopeN = Math.Max(isFill ? p.FillSlope : p.CutSlope, p.MinSlope);
        double faceRun = Math.Max(p.BenchHeight * slopeN, p.MinFaceRun);
        double period = faceRun + p.BenchWidth;
        double zDir = isFill ? -1.0 : 1.0;

        double margin = Math.Max(
            BenchProfile.MaxReach(p.BenchHeight, p.BenchWidth, p.CutSlope, p.MaxBenches),
            BenchProfile.MaxReach(p.BenchHeight, p.BenchWidth, p.FillSlope, p.MaxBenches));
        if (p.MiterConvex) margin *= 1.5;
        minX -= margin; minY -= margin; maxX += margin; maxY += margin;

        double cell = p.CellSize;
        int nx = (int)Math.Ceiling((maxX - minX) / cell);
        int ny = (int)Math.Ceiling((maxY - minY) / cell);

        // 종류: 0=없음, 1=사면, 2=소단, 3=평탄(계획지표면). band=단 순번.
        var cat = new int[nx + 1, ny + 1];
        var band = new int[nx + 1, ny + 1];

        for (int iy = 0; iy <= ny; iy++)
        {
            double y = minY + iy * cell;
            for (int ix = 0; ix <= nx; ix++)
            {
                double x = minX + ix * cell;
                if (PolygonGeometry.Contains(boundary, x, y)) { cat[ix, iy] = 3; continue; }

                var (d, bx, by, _) = PolygonGeometry.ClosestBoundary(boundary, x, y, p.MiterConvex, p.MiterLimit);
                if (!ground.TryGetElevation(bx, by, out double gBoundary)) continue;
                if (!ground.TryGetElevation(x, y, out double gCell)) continue;

                double designZ = plane.At(bx, by);
                bool fillHere = designZ > gBoundary;
                double nLoc = fillHere ? p.FillSlope : p.CutSlope;
                double h = BenchProfile.Height(d, p.BenchHeight, p.BenchWidth, nLoc, p.MaxBenches);
                double proposed = fillHere ? designZ - h : designZ + h;
                double diff = fillHere ? proposed - gCell : gCell - proposed;
                if (diff <= 0) continue; // daylight 바깥 — 정지 영역 아님

                int full = (int)Math.Floor(d / period);
                int k = full + 1;
                if (k > p.MaxBenches) continue;
                double rem = d - full * period;
                cat[ix, iy] = rem < faceRun ? 1 : 2; // 비탈구간 / 소단구간
                band[ix, iy] = k;
            }
        }

        var list = new List<AreaPolygon>();

        // ① 전체 부지 — 정지면 전체(평탄+사면+소단)의 외곽 = 정지경계(daylight)까지 포함. InfraWorks 바닥 레이어.
        // simplifyTol:0 — 격자 에지를 그대로 두어 소단 폴리곤과 daylight 경계 점을 정확히 공유(위상 일치, InfraWorks 깨짐 방지).
        var siteRings = TraceBoundary((i, j) => cat[i, j] != 0, nx, ny, minX, minY, cell, designZ0, simplifyTol: 0);
        if (siteRings.Count > 0)
            list.Add(new AreaPolygon { Category = "전체부지", Index = 0, Elevation = designZ0, Rings = siteRings });

        // ③ 계획 폴리곤 — 입력 폴리곤 '그대로(직각)'. 맨 위 레이어.
        var planRing = new List<Point3>();
        foreach (var v in boundary) planRing.Add(new Point3(v.X, v.Y, designZ0));
        list.Add(new AreaPolygon { Category = "계획", Index = 0, Elevation = designZ0, Rings = new() { planRing } });

        // ② 소단 — 단별 평탄부(번호·표고 구분). 전체부지 위에 덮는 레이어. (사면은 레이어 겹침으로 드러나므로 생략)
        for (int k = 1; k <= p.MaxBenches; k++)
        {
            int kk = k;
            double zBench = designZ0 + zDir * p.BenchHeight * kk; // 소단 k 표고
            var benchRings = TraceBoundary((i, j) => cat[i, j] == 2 && band[i, j] == kk, nx, ny, minX, minY, cell, zBench, simplifyTol: 0);
            if (benchRings.Count > 0)
                list.Add(new AreaPolygon { Category = "소단", Index = kk, Elevation = zBench, Rings = benchRings });
        }

        return list;
    }

    /// <summary>
    /// 사면 노리선(법면 표시) 생성 — '기하 오프셋'(형상선과 동일 기준) 위에 만든다. 각 사면의 '상단 등거리
    /// 윤곽'(매끈)을 따라가며 내리막 방향으로 빗살을 긋고, 빗살이 사면 경사를 따라 내려가도록 끝점 표고를 낮춘다
    /// (평평하지 않아 3D에서 사면에 붙는다). 긴선(5m 간격)=사면 전체, 짧은선(1m 간격)=절반. daylight에서 잘린다.
    /// </summary>
    public static List<(Point3 A, Point3 B)> ExtractSlopeTicks(
        IReadOnlyList<Point3> boundary, IGroundSurface ground, GradingParams p,
        double shortSpacing, double longSpacing)
    {
        if (boundary == null || boundary.Count < 3)
            throw new ArgumentException("경계 폴리곤은 최소 3개 정점이 필요합니다.", nameof(boundary));
        ArgumentNullException.ThrowIfNull(ground);
        p.Validate();
        if (shortSpacing <= 0) shortSpacing = 1.0;
        if (longSpacing <= 0) longSpacing = 5.0;

        var plane = PolygonGeometry.FitPlane(boundary);
        double cxs = 0, cys = 0;
        foreach (var v in boundary) { cxs += v.X; cys += v.Y; }
        double centroidX = cxs / boundary.Count, centroidY = cys / boundary.Count;
        double designZ0 = plane.At(centroidX, centroidY);
        bool isFill = !ground.TryGetElevation(centroidX, centroidY, out double gC0) || designZ0 >= gC0;
        double slopeN = Math.Max(isFill ? p.FillSlope : p.CutSlope, p.MinSlope);
        double faceRun = Math.Max(p.BenchHeight * slopeN, p.MinFaceRun);
        double period = faceRun + p.BenchWidth;
        double zDir = isFill ? -1.0 : 1.0;
        double downhill = isFill ? 1.0 : -1.0; // 성토=바깥으로 내리막, 절토=안쪽으로 내리막

        int n = boundary.Count;
        var (dir, nrm, convex) = EdgeFrames(boundary);
        double targetChord = Math.Max(0.2, p.BenchWidth * 0.3);
        double edgeStep = Math.Max(0.25, Math.Min(p.CellSize, p.BenchWidth * 0.5));

        var ticks = new List<(Point3, Point3)>();
        int ratio = Math.Max(1, (int)Math.Round(longSpacing / shortSpacing));
        int dry = 0;

        for (int k = 1; k <= p.MaxBenches && dry < 2; k++)
        {
            // 사면 k의 상단 윤곽: 성토는 d=(k-1)*period(z=z0-(k-1)H), 절토는 d=(k-1)*period+faceRun(z=z0+kH).
            double dTop = isFill ? (k - 1) * period : (k - 1) * period + faceRun;
            double zTop = designZ0 + zDir * p.BenchHeight * (isFill ? (k - 1) : k);

            var ring = BuildOffsetRingN(boundary, dir, nrm, convex, n, dTop, targetChord, edgeStep, p.MiterLimit);
            if (ring.Count < 2) { if (k > 1) dry++; continue; }

            bool anyTick = false;
            double acc = shortSpacing; // 첫 점부터 찍도록
            int count = 0;
            for (int j = 0; j < ring.Count; j++)
            {
                if (j > 0)
                {
                    double dx = ring[j].X - ring[j - 1].X, dy = ring[j].Y - ring[j - 1].Y;
                    acc += Math.Sqrt(dx * dx + dy * dy);
                }
                if (acc + 1e-9 < shortSpacing) continue;
                acc = 0;

                double px = ring[j].X, py = ring[j].Y;
                // 겹침(오목/좁은목)부 밑동 제외 — 실제 최근접거리가 dTop와 같을 때만.
                if (dTop > 1e-6)
                {
                    var (bd, _, _, _) = PolygonGeometry.ClosestBoundary(boundary, px, py, false);
                    if (bd < dTop - Math.Max(0.1, dTop * 0.05)) { count++; continue; }
                }
                double dirx = ring[j].Nx * downhill, diry = ring[j].Ny * downhill;

                bool isLong = count % ratio == 0;
                double maxLen = isLong ? faceRun : faceRun * 0.5;
                double L = ClipTickAtDaylight(boundary, px, py, dirx, diry, plane, ground, p, maxLen);
                count++;
                if (L < 0.02) continue;
                double zEnd = zTop - p.BenchHeight * (L / Math.Max(faceRun, 1e-9)); // 사면 경사 따라 하강(안 뜸)
                ticks.Add((new Point3(px, py, zTop), new Point3(px + dirx * L, py + diry * L, zEnd)));
                anyTick = true;
            }
            dry = anyTick ? 0 : (k > 1 ? dry + 1 : dry);
        }
        return ticks;
    }

    /// <summary>폴리곤 변별 단위방향(dir)·바깥 단위법선(nrm)·꼭짓점 볼록여부(convex).</summary>
    private static ((double x, double y)[] dir, (double x, double y)[] nrm, bool[] convex) EdgeFrames(IReadOnlyList<Point3> boundary)
    {
        int n = boundary.Count;
        double ccw = Math.Sign(PolygonGeometry.SignedArea(boundary)); if (ccw == 0) ccw = 1;
        var dir = new (double x, double y)[n];
        var nrm = new (double x, double y)[n];
        for (int i = 0; i < n; i++)
        {
            var a = boundary[i]; var b = boundary[(i + 1) % n];
            double ex = b.X - a.X, ey = b.Y - a.Y, len = Math.Sqrt(ex * ex + ey * ey);
            if (len < 1e-12) { dir[i] = (0, 0); nrm[i] = (0, 0); continue; }
            ex /= len; ey /= len; dir[i] = (ex, ey);
            nrm[i] = ccw > 0 ? (ey, -ex) : (-ey, ex);
        }
        var convex = new bool[n];
        for (int i = 0; i < n; i++)
        {
            int pe = (i - 1 + n) % n;
            double crs = nrm[pe].x * nrm[i].y - nrm[pe].y * nrm[i].x;
            convex[i] = crs * ccw > 1e-12;
        }
        return (dir, nrm, convex);
    }

    /// <summary>거리 d의 기하 오프셋 링을 (점 + 바깥 단위법선)으로 반환 — 노리선 밑동·방향용. 볼록=원호, 오목=마이터 교점.</summary>
    private static List<(double X, double Y, double Nx, double Ny)> BuildOffsetRingN(
        IReadOnlyList<Point3> boundary, (double x, double y)[] dir, (double x, double y)[] nrm, bool[] convex,
        int n, double d, double targetChord, double edgeStep, double miterLimit)
    {
        var outp = new List<(double, double, double, double)>();
        var Ax = new double[n]; var Ay = new double[n]; var EVx = new double[n]; var EVy = new double[n]; var EVl2 = new double[n];
        for (int i = 0; i < n; i++)
        {
            var Vi = boundary[i]; var Vn = boundary[(i + 1) % n];
            Ax[i] = Vi.X + nrm[i].x * d; Ay[i] = Vi.Y + nrm[i].y * d;
            EVx[i] = Vn.X - Vi.X; EVy[i] = Vn.Y - Vi.Y; EVl2[i] = EVx[i] * EVx[i] + EVy[i] * EVy[i];
        }
        var Mx = new double[n]; var My = new double[n]; var mit = new bool[n];
        for (int i = 0; i < n; i++)
        {
            if (convex[i]) continue;
            int pe = (i - 1 + n) % n;
            if ((nrm[pe].x == 0 && nrm[pe].y == 0) || (nrm[i].x == 0 && nrm[i].y == 0)) continue;
            if (LineIntersect(Ax[pe], Ay[pe], dir[pe].x, dir[pe].y, Ax[i], Ay[i], dir[i].x, dir[i].y, out double ix, out double iy))
            {
                double sx = ix - boundary[i].X, sy = iy - boundary[i].Y;
                double lim = Math.Min(miterLimit, 1.7) * d;
                if (sx * sx + sy * sy <= lim * lim) { Mx[i] = ix; My[i] = iy; mit[i] = true; }
            }
        }
        double Proj(int e, double px, double py) => EVl2[e] < 1e-18 ? 0 : ((px - Ax[e]) * EVx[e] + (py - Ay[e]) * EVy[e]) / EVl2[e];
        void Add(double x, double y, double nx, double ny)
        {
            double nl = Math.Sqrt(nx * nx + ny * ny); if (nl < 1e-12) return; nx /= nl; ny /= nl;
            if (outp.Count > 0) { double ddx = x - outp[^1].Item1, ddy = y - outp[^1].Item2; if (ddx * ddx + ddy * ddy < 1e-12) return; }
            outp.Add((x, y, nx, ny));
        }
        for (int i = 0; i < n; i++)
        {
            int pe = (i - 1 + n) % n, ni = (i + 1) % n;
            if ((nrm[i].x == 0 && nrm[i].y == 0) || EVl2[i] < 1e-18) continue;
            var Vi = boundary[i];
            if (convex[i] && !(nrm[pe].x == 0 && nrm[pe].y == 0))
            {
                double dot = nrm[pe].x * nrm[i].x + nrm[pe].y * nrm[i].y;
                double crs = nrm[pe].x * nrm[i].y - nrm[pe].y * nrm[i].x;
                double turn = Math.Atan2(crs, dot);
                double dth = 2 * Math.Asin(Math.Min(1, targetChord / (2 * Math.Max(d, 1e-6))));
                int steps = Math.Max(1, Math.Min(96, (int)Math.Ceiling(Math.Abs(turn) / Math.Max(dth, 1e-6))));
                for (int s = 0; s <= steps; s++)
                {
                    double th = turn * s / steps;
                    double cx = nrm[pe].x * Math.Cos(th) - nrm[pe].y * Math.Sin(th);
                    double cy = nrm[pe].x * Math.Sin(th) + nrm[pe].y * Math.Cos(th);
                    Add(Vi.X + cx * d, Vi.Y + cy * d, cx, cy);
                }
            }
            else if (mit[i])
                Add(Mx[i], My[i], nrm[pe].x + nrm[i].x, nrm[pe].y + nrm[i].y);
            else if (!(nrm[pe].x == 0 && nrm[pe].y == 0))
            {
                Add(Vi.X + nrm[pe].x * d, Vi.Y + nrm[pe].y * d, nrm[pe].x, nrm[pe].y);
                Add(Ax[i], Ay[i], nrm[i].x, nrm[i].y);
            }
            double ts = mit[i] ? Proj(i, Mx[i], My[i]) : 0.0;
            double te = mit[ni] ? Proj(i, Mx[ni], My[ni]) : 1.0;
            if (te > ts + 1e-9)
            {
                double seglen = (te - ts) * Math.Sqrt(EVl2[i]);
                int es = Math.Max(1, (int)Math.Ceiling(seglen / edgeStep));
                for (int s = 1; s <= es; s++)
                {
                    double t = ts + (te - ts) * s / es;
                    Add(Ax[i] + EVx[i] * t, Ay[i] + EVy[i] * t, nrm[i].x, nrm[i].y);
                }
            }
        }
        return outp;
    }

    /// <summary>
    /// 노리선을 daylight(정지면이 원지반과 만나는 곳)에서 잘라낼 길이를 구한다.
    /// 상단점에서 내리막 방향으로 가며 정지 제안고가 원지반과 만나는 지점까지의 길이를 반환.
    /// </summary>
    private static double ClipTickAtDaylight(
        IReadOnlyList<Point3> boundary, double px, double py, double dirx, double diry,
        Plane plane, IGroundSurface ground, GradingParams p, double maxLen)
    {
        int sub = 12;
        double last = maxLen;
        double prevLen = 0;
        for (int s = 1; s <= sub; s++)
        {
            double len = maxLen * s / sub;
            double qx = px + dirx * len, qy = py + diry * len;
            var (dq, bx, by, _) = PolygonGeometry.ClosestBoundary(boundary, qx, qy, p.MiterConvex, p.MiterLimit);
            if (!ground.TryGetElevation(bx, by, out double gB)) return prevLen;
            if (!ground.TryGetElevation(qx, qy, out double gQ)) return prevLen;
            double designZ = plane.At(bx, by);
            bool fillHere = designZ > gB;
            double nLoc = fillHere ? p.FillSlope : p.CutSlope;
            double h = BenchProfile.Height(dq, p.BenchHeight, p.BenchWidth, nLoc, p.MaxBenches);
            double proposed = fillHere ? designZ - h : designZ + h;
            double diff = fillHere ? proposed - gQ : gQ - proposed;
            if (diff <= 0) return prevLen; // daylight 도달 — 직전 길이까지만
            prevLen = len;
        }
        return last;
    }

    private static void AccumulateVolume(GradingResult r, IGroundSurface ground, double x, double y, double z, double area)
    {
        if (!ground.TryGetElevation(x, y, out double g)) return;
        double diff = z - g;
        if (diff > 0) r.FillVolume += diff * area;
        else r.CutVolume += -diff * area;
    }

    /// <summary>마스크 경계 추적(상수 표고 버전) — 모든 점 Z를 같은 elevation으로.</summary>
    private static List<List<Point3>> TraceBoundary(Func<int, int, bool> inMask, int nx, int ny, double minX, double minY, double cell, double elevation, double simplifyTol = -1)
        => TraceBoundary(inMask, nx, ny, minX, minY, cell, (x, y) => elevation, simplifyTol);

    /// <summary>
    /// 마스크 영역의 경계를 따라 닫힌 루프들을 추적(외곽 + 안쪽 구멍).
    /// 점 Z는 '평활화가 끝난 최종 XY'에서 <paramref name="elevAt"/>로 부여한다 — 형상선이 표면(절토는 위·성토는 아래)을
    /// 그대로 따라가 한 방향 가정 없이 혼합 절성토에서도 표면과 정확히 일치한다.
    /// </summary>
    private static List<List<Point3>> TraceBoundary(Func<int, int, bool> inMask, int nx, int ny, double minX, double minY, double cell, Func<double, double, double> elevAt, double simplifyTol = -1)
    {
        bool C(int ix, int iy) => ix >= 0 && iy >= 0 && ix <= nx && iy <= ny && inMask(ix, iy);

        // 방향성 단위 경계에지(구성측이 왼쪽). 대각 모호점(saddle)에서 한 노드에 출에지 2개가 생길 수 있어
        // 다중맵으로 보관 → 추적 시 회전우선으로 분리해 '항상 닫힌' 루프를 만든다(누락·끊김·사선 방지).
        var outs = new Dictionary<(int, int), List<(int, int)>>();
        void AddEdge((int, int) a, (int, int) b)
        {
            if (!outs.TryGetValue(a, out var l)) { l = new List<(int, int)>(); outs[a] = l; }
            l.Add(b);
        }
        for (int iy = 0; iy <= ny; iy++)
            for (int ix = 0; ix <= nx; ix++)
            {
                if (!C(ix, iy)) continue;
                if (!C(ix + 1, iy)) AddEdge((ix + 1, iy), (ix + 1, iy + 1));
                if (!C(ix, iy + 1)) AddEdge((ix + 1, iy + 1), (ix, iy + 1));
                if (!C(ix - 1, iy)) AddEdge((ix, iy + 1), (ix, iy));
                if (!C(ix, iy - 1)) AddEdge((ix, iy), (ix + 1, iy));
            }

        var loops = new List<List<Point3>>();
        var startNodes = new List<(int, int)>(outs.Keys);
        long guardMax = (long)(nx + 2) * (ny + 2) * 4 + 16;
        foreach (var startNode in startNodes)
        {
            while (outs.TryGetValue(startNode, out var sl) && sl.Count > 0)
            {
                var keys = new List<(int, int)>();
                var cur = startNode;
                (int, int)? prev = null;
                bool closed = false;
                long guard = 0;
                while (guard++ < guardMax)
                {
                    keys.Add(cur);
                    if (!outs.TryGetValue(cur, out var lst) || lst.Count == 0) break; // 막힘(비정상)
                    int pick = ChooseNext(prev, cur, lst);
                    var nxt = lst[pick];
                    lst.RemoveAt(pick);
                    prev = cur; cur = nxt;
                    if (cur == startNode) { closed = true; break; }
                }
                if (!closed || keys.Count < 4) continue;

                var pts = new List<Point3>(keys.Count);
                foreach (var (kx, ky) in keys)
                    pts.Add(new Point3(minX + (kx - 0.5) * cell, minY + (ky - 0.5) * cell, 0)); // Z는 평활화 후 부여

                // 격자 계단(톱니) 단순화 — Douglas-Peucker로 톱니는 펴고 '실제 직각/오목 모서리는 보존'한다.
                // 볼록부 라운드/직각은 거리장(MiterConvex)이 이미 결정하므로 추가 둥글림(Chaikin)을 하지 않는다 →
                // 직각 부지에서 형상선이 둥글지 않고 반듯하게 나온다.
                // simplifyTol=0이면 격자 에지를 그대로 둔다(SHP: 폴리곤끼리 같은 경계 점을 공유 → 위상 정확 일치).
                double tol = simplifyTol < 0 ? cell * 1.2 : simplifyTol;
                var simp = SimplifyClosed(pts, tol);
                if (simp.Count < 3) continue;
                if (Math.Abs(LoopArea(simp)) < cell * cell * 0.5) continue; // 진짜 한 칸짜리 잡티만 제거(짧은 단은 보존)

                // 표고 부여 — 평활화로 확정된 최종 XY에서 표면 높이를 읽는다(절토 위·성토 아래 자동).
                for (int i = 0; i < simp.Count; i++)
                {
                    var pp = simp[i];
                    simp[i] = pp with { Z = elevAt(pp.X, pp.Y) };
                }
                loops.Add(simp);
            }
        }
        return loops;
    }

    /// <summary>
    /// 스칼라장 field의 등치선 level을 marching-squares로 추출한다. 셀 보간이라 격자 계단이 없고(폴리곤
    /// 오프셋처럼 매끈·정밀), 단일값장이라 자기교차가 없다. 정지면 밖 셀에 −∞를 주면 거기서 등치선이 닫힌다.
    /// 반환: 닫힌 루프들(점 Z=zOut).
    /// </summary>
    private static List<List<Point3>> ExtractIsoContours(
        Func<int, int, double> field, int nx, int ny, double minX, double minY, double cell, double level, double zOut)
    {
        var segs = new List<(double ax, double ay, double bx, double by)>();
        for (int iy = 0; iy < ny; iy++)
        {
            double y0 = minY + iy * cell, y1 = y0 + cell;
            for (int ix = 0; ix < nx; ix++)
            {
                double x0 = minX + ix * cell, x1 = x0 + cell;
                double v0 = field(ix, iy), v1 = field(ix + 1, iy), v2 = field(ix + 1, iy + 1), v3 = field(ix, iy + 1);
                if (double.IsNaN(v0) || double.IsNaN(v1) || double.IsNaN(v2) || double.IsNaN(v3)) continue;
                int idx = (v0 >= level ? 1 : 0) | (v1 >= level ? 2 : 0) | (v2 >= level ? 4 : 0) | (v3 >= level ? 8 : 0);
                if (idx == 0 || idx == 15) continue;

                // 네 변의 등치선 교차점(선형 보간). c0=좌하, c1=우하, c2=우상, c3=좌상.
                (double, double) EB() { double t = Clamp01((level - v0) / (v1 - v0)); return (x0 + t * cell, y0); } // 하변 c0-c1
                (double, double) ER() { double t = Clamp01((level - v1) / (v2 - v1)); return (x1, y0 + t * cell); } // 우변 c1-c2
                (double, double) ET() { double t = Clamp01((level - v3) / (v2 - v3)); return (x0 + t * cell, y1); } // 상변 c3-c2
                (double, double) EL() { double t = Clamp01((level - v0) / (v3 - v0)); return (x0, y0 + t * cell); } // 좌변 c0-c3
                void Add((double, double) a, (double, double) b) => segs.Add((a.Item1, a.Item2, b.Item1, b.Item2));

                switch (idx)
                {
                    case 1: case 14: Add(EL(), EB()); break;
                    case 2: case 13: Add(EB(), ER()); break;
                    case 3: case 12: Add(EL(), ER()); break;
                    case 4: case 11: Add(ER(), ET()); break;
                    case 6: case 9: Add(EB(), ET()); break;
                    case 7: case 8: Add(EL(), ET()); break;
                    case 5: Add(EL(), EB()); Add(ER(), ET()); break;   // saddle
                    case 10: Add(EB(), ER()); Add(EL(), ET()); break;  // saddle
                }
            }
        }
        return LinkSegments(segs, cell, zOut);
    }

    private static double Clamp01(double t) => t < 0 ? 0 : (t > 1 ? 1 : t);

    /// <summary>
    /// 폴리라인 톱니(격자 계단)를 이동평균으로 편다 — 점을 늘리지 않고 위치만 부드럽게(잔물결·꼬임 없음).
    /// 닫힌 루프(첫·끝이 가까움)는 순환으로, 열린 곡선(daylight에서 잘림)은 양 끝점을 고정해 형태를 보존한다.
    /// </summary>
    private static List<Point3> SmoothChain(List<Point3> pts, int iterations, double closeTol)
    {
        int n = pts.Count;
        if (n < 4) return pts;
        double dx0 = pts[0].X - pts[n - 1].X, dy0 = pts[0].Y - pts[n - 1].Y;
        bool closed = dx0 * dx0 + dy0 * dy0 < closeTol * closeTol;

        var cur = new List<Point3>(pts);
        for (int it = 0; it < iterations; it++)
        {
            var nxt = new List<Point3>(cur.Count);
            int m = cur.Count;
            for (int i = 0; i < m; i++)
            {
                if (!closed && (i == 0 || i == m - 1)) { nxt.Add(cur[i]); continue; } // 열린 끝점 고정
                var p = cur[(i - 1 + m) % m];
                var c = cur[i];
                var q = cur[(i + 1) % m];
                nxt.Add(new Point3(c.X * 0.5 + p.X * 0.25 + q.X * 0.25, c.Y * 0.5 + p.Y * 0.25 + q.Y * 0.25, c.Z));
            }
            cur = nxt;
        }
        return cur;
    }

    /// <summary>
    /// 등치선 세그먼트들을 끝점 좌표로 이어 폴리라인으로 만든다.
    /// 닫힌 루프(부지를 한 바퀴 도는 단)와 열린 곡선(땅에 닿아 daylight에서 잘린 단)을 모두 살린다.
    /// </summary>
    private static List<List<Point3>> LinkSegments(List<(double ax, double ay, double bx, double by)> segs, double cell, double zOut)
    {
        double q = cell * 1e-3; // 끝점 좌표 양자화(교차점은 인접 셀과 정확히 공유되므로 동일 키)
        (long, long) Key(double x, double y) => ((long)Math.Round(x / q), (long)Math.Round(y / q));

        var adj = new Dictionary<(long, long), List<(long, long)>>();
        var pos = new Dictionary<(long, long), (double x, double y)>();
        void Link(double ax, double ay, double bx, double by)
        {
            var ka = Key(ax, ay); var kb = Key(bx, by);
            pos[ka] = (ax, ay);
            if (!adj.TryGetValue(ka, out var l)) { l = new List<(long, long)>(); adj[ka] = l; }
            if (!l.Contains(kb)) l.Add(kb);
        }
        foreach (var s in segs) { Link(s.ax, s.ay, s.bx, s.by); Link(s.bx, s.by, s.ax, s.ay); }

        var used = new HashSet<(long, long)>();
        List<Point3> Walk((long, long) startK)
        {
            var chain = new List<Point3>();
            var ck = startK; (long, long)? prevK = null;
            int guard = 0, max = adj.Count * 2 + 16;
            while (guard++ < max && !used.Contains(ck))
            {
                used.Add(ck);
                var (px, py) = pos[ck];
                chain.Add(new Point3(px, py, zOut));
                if (!adj.TryGetValue(ck, out var nbrs)) break;
                (long, long)? next = null;
                foreach (var nk in nbrs)
                {
                    if (prevK.HasValue && nk == prevK.Value) continue;
                    if (used.Contains(nk)) continue;
                    next = nk; break;
                }
                if (next == null) break;
                prevK = ck; ck = next.Value;
            }
            return chain;
        }
        double Perim(List<Point3> c)
        {
            double L = 0;
            for (int i = 1; i < c.Count; i++) { double dx = c[i].X - c[i - 1].X, dy = c[i].Y - c[i - 1].Y; L += Math.Sqrt(dx * dx + dy * dy); }
            return L;
        }

        var loops = new List<List<Point3>>();
        // 1) 열린 곡선: 끝점(이웃 1개)부터 추적해야 통째로 잡힌다.
        foreach (var kv in adj)
            if (kv.Value.Count == 1 && !used.Contains(kv.Key))
            {
                var chain = Walk(kv.Key);
                if (chain.Count >= 2 && Perim(chain) >= cell * 3) loops.Add(chain);
            }
        // 2) 닫힌 루프: 남은 노드에서.
        foreach (var kv in adj)
            if (!used.Contains(kv.Key))
            {
                var chain = Walk(kv.Key);
                if (chain.Count >= 3 && Math.Abs(LoopArea(chain)) >= cell * cell * 0.5) loops.Add(chain);
            }
        return loops;
    }

    /// <summary>
    /// 경계 추적 다음 에지 선택 — 도착방향 기준 '좌회전→직진→우회전' 우선. saddle(대각 모호점)에서
    /// 두 가닥을 일관되게 분리해 자기교차 없는 닫힌 루프를 만든다.
    /// </summary>
    private static int ChooseNext((int, int)? prev, (int, int) cur, List<(int, int)> cands)
    {
        if (prev is null || cands.Count == 1) return 0;
        int ax = cur.Item1 - prev.Value.Item1, ay = cur.Item2 - prev.Value.Item2;
        // 좌회전(-ay,ax) → 직진(ax,ay) → 우회전(ay,-ax)
        var prefs = new[] { (-ay, ax), (ax, ay), (ay, -ax) };
        foreach (var (dx, dy) in prefs)
        {
            var target = (cur.Item1 + dx, cur.Item2 + dy);
            int idx = cands.IndexOf(target);
            if (idx >= 0) return idx;
        }
        return 0;
    }

    /// <summary>
    /// 폴리라인의 격자 계단(작은 수평/수직 꺾임)을 단순화로 편다 — Douglas-Peucker로 tol 이내 점을 제거해
    /// 비스듬한 직선으로 펴되 위치는 유지(수축 없음). 교선 daylight가 차이표면 격자에서 물려받은 계단을 펴는 용도.
    /// </summary>
    public static List<Point3> SimplifyLoop(List<Point3> pts, double tol) => SimplifyClosed(pts, tol);

    /// <summary>닫힌 루프 Douglas-Peucker 단순화 — 첫 점 고정, 허용오차 tol 이내 점 제거.</summary>
    private static List<Point3> SimplifyClosed(List<Point3> pts, double tol)
    {
        int n = pts.Count;
        if (n < 4) return pts;
        var work = new List<Point3>(pts) { pts[0] }; // 닫아 열린 경로로 처리
        var keep = new bool[work.Count];
        keep[0] = keep[work.Count - 1] = true;

        var stack = new Stack<(int, int)>();
        stack.Push((0, work.Count - 1));
        while (stack.Count > 0)
        {
            var (i0, i1) = stack.Pop();
            if (i1 <= i0 + 1) continue;
            double ax = work[i0].X, ay = work[i0].Y;
            double dx = work[i1].X - ax, dy = work[i1].Y - ay;
            double len = Math.Sqrt(dx * dx + dy * dy);
            double maxD = -1; int maxI = -1;
            for (int k = i0 + 1; k < i1; k++)
            {
                double d;
                if (len < 1e-12) { double ex = work[k].X - ax, ey = work[k].Y - ay; d = Math.Sqrt(ex * ex + ey * ey); }
                else d = Math.Abs((work[k].X - ax) * dy - (work[k].Y - ay) * dx) / len;
                if (d > maxD) { maxD = d; maxI = k; }
            }
            if (maxD > tol && maxI > 0)
            {
                keep[maxI] = true;
                stack.Push((i0, maxI));
                stack.Push((maxI, i1));
            }
        }

        var outp = new List<Point3>();
        for (int i = 0; i < pts.Count; i++) if (keep[i]) outp.Add(pts[i]);
        return outp;
    }

    private static double LoopArea(List<Point3> pts)
    {
        double a = 0; int n = pts.Count;
        for (int i = 0, j = n - 1; i < n; j = i++)
            a += pts[j].X * pts[i].Y - pts[i].X * pts[j].Y;
        return a * 0.5;
    }

    /// <summary>
    /// 모서리 보존 Chaikin 코너커팅(닫힌 루프) — 완만한 굽이만 둥글려 부드럽게 하고,
    /// 직선부와 뾰족 모서리(꺾임각 큰 곳)는 그대로 둔다. cornerCos보다 작은 cos(=큰 꺾임각)는 모서리로 보존.
    /// </summary>
    private static List<Point3> SmoothCorners(List<Point3> pts, int iterations, double cornerCos)
    {
        var cur = pts;
        for (int it = 0; it < iterations && cur.Count >= 4; it++)
        {
            int n = cur.Count;
            var nxt = new List<Point3>(n * 2);
            for (int i = 0; i < n; i++)
            {
                var p = cur[(i - 1 + n) % n];
                var c = cur[i];
                var q = cur[(i + 1) % n];
                double v1x = c.X - p.X, v1y = c.Y - p.Y;
                double v2x = q.X - c.X, v2y = q.Y - c.Y;
                double l1 = Math.Sqrt(v1x * v1x + v1y * v1y);
                double l2 = Math.Sqrt(v2x * v2x + v2y * v2y);
                double cos = (l1 < 1e-12 || l2 < 1e-12) ? 1.0 : (v1x * v2x + v1y * v2y) / (l1 * l2);

                if (cos > 0.985 || cos < cornerCos)
                    nxt.Add(c); // 직선부(거의 일직선) 또는 뾰족 모서리 → 유지
                else
                {
                    nxt.Add(new Point3(c.X * 0.75 + p.X * 0.25, c.Y * 0.75 + p.Y * 0.25, c.Z));
                    nxt.Add(new Point3(c.X * 0.75 + q.X * 0.25, c.Y * 0.75 + q.Y * 0.25, c.Z));
                }
            }
            cur = nxt;
        }
        return cur;
    }

    private static void MarkDaylight(GradingResult r, double cell)
    {
        var set = new HashSet<(long, long)>();
        foreach (var pt in r.Points) set.Add((Key(pt.X, cell), Key(pt.Y, cell)));
        foreach (var pt in r.Points)
        {
            long kx = Key(pt.X, cell), ky = Key(pt.Y, cell);
            if (!set.Contains((kx + 1, ky)) || !set.Contains((kx - 1, ky)) ||
                !set.Contains((kx, ky + 1)) || !set.Contains((kx, ky - 1)))
                r.DaylightPoints.Add(pt);
        }
    }

    private static long Key(double v, double cell) => (long)Math.Round(v / cell);
}
