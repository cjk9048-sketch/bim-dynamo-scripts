namespace DH.Grading.Core;

/// <summary>
/// 폴리곤 평면(XY) 기하 유틸 — 내부판정, 최근접 경계점·거리, 계획면 평면적합.
/// distance-field 정지 알고리즘의 토대. 모두 순수 함수(AutoCAD 의존성 없음).
/// </summary>
public static class PolygonGeometry
{
    /// <summary>점-in-폴리곤 (XY, ray casting). 정점은 닫히지 않은 목록(마지막→첫) 가정.</summary>
    public static bool Contains(IReadOnlyList<Point3> poly, double x, double y)
    {
        bool inside = false;
        int n = poly.Count;
        for (int i = 0, j = n - 1; i < n; j = i++)
        {
            double xi = poly[i].X, yi = poly[i].Y;
            double xj = poly[j].X, yj = poly[j].Y;
            bool intersect = ((yi > y) != (yj > y)) &&
                             (x < (xj - xi) * (y - yi) / ((yj - yi) == 0 ? 1e-12 : (yj - yi)) + xi);
            if (intersect) inside = !inside;
        }
        return inside;
    }

    /// <summary>폴리곤 경계까지의 최단거리와 그 위치의 계획고(경계 Z 선형보간).</summary>
    /// <param name="miter">true면 볼록 모서리를 직각(마이터)으로 — 거리를 두 변 연장선 기준 max로 계산.</param>
    /// <param name="miterLimit">마이터 최대 연장 비율(넘으면 라운드로 폴백). miter=false면 무시.</param>
    /// <returns>(거리, 최근접점X, 최근접점Y, 최근접점의 boundaryZ)</returns>
    public static (double Dist, double Cx, double Cy, double Cz) ClosestBoundary(
        IReadOnlyList<Point3> poly, double x, double y, bool miter = false, double miterLimit = 2.0)
    {
        double best = double.MaxValue;
        double bcx = 0, bcy = 0, bcz = 0;
        double bestT = 0; int bestJ = -1, bestI = -1;
        int n = poly.Count;
        for (int i = 0, j = n - 1; i < n; j = i++)
        {
            var a = poly[j];
            var b = poly[i];
            var (px, py, t) = ClosestOnSegment(a.X, a.Y, b.X, b.Y, x, y);
            double dx = x - px, dy = y - py;
            double dist = Math.Sqrt(dx * dx + dy * dy);
            if (dist < best)
            {
                best = dist;
                bcx = px; bcy = py;
                bcz = a.Z + (b.Z - a.Z) * t; // 경계 표고 선형보간
                bestT = t; bestJ = j; bestI = i;
            }
        }

        // 직각(마이터) 모드: 최근접점이 '볼록 꼭짓점'이면 두 변 연장선 기준 거리(max)로 모서리를 뾰족하게.
        if (miter && best > 1e-9 && bestJ >= 0 && (bestT <= 1e-6 || bestT >= 1 - 1e-6))
        {
            int m = bestT < 0.5 ? bestJ : bestI; // 최근접 꼭짓점 인덱스
            double dm = MiteredVertexDistance(poly, x, y, m, best, miterLimit);
            if (dm < best) best = dm; // 마이터는 거리를 줄여 같은 단이 모서리 바깥으로 뾰족하게 뻗게 함
        }
        return (best, bcx, bcy, bcz);
    }

    /// <summary>
    /// 볼록 꼭짓점 V에서의 마이터(직각) 거리 = V를 공유하는 두 변의 바깥수직거리 중 큰 값.
    /// 오목 꼭짓점이거나 바깥 쐐기 밖이면 라운드(euclid) 그대로. 마이터 한계 초과 시에도 라운드 폴백.
    /// </summary>
    private static double MiteredVertexDistance(
        IReadOnlyList<Point3> poly, double x, double y, int m, double euclid, double miterLimit)
    {
        int n = poly.Count;
        var V = poly[m];
        var P = poly[(m - 1 + n) % n]; // 이전 꼭짓점
        var Q = poly[(m + 1) % n];     // 다음 꼭짓점

        double ccw = Math.Sign(SignedArea(poly));
        if (ccw == 0) ccw = 1;

        var nPrev = OutwardNormal(P.X, P.Y, V.X, V.Y, ccw); // 변 P→V 바깥법선
        var nNext = OutwardNormal(V.X, V.Y, Q.X, Q.Y, ccw); // 변 V→Q 바깥법선
        if (nPrev is null || nNext is null) return euclid;

        // 볼록 판정: 법선 회전 방향이 폴리곤 방향과 같으면 볼록.
        double crs = nPrev.Value.X * nNext.Value.Y - nPrev.Value.Y * nNext.Value.X;
        if (crs * ccw <= 1e-12) return euclid; // 오목/직선 → 손대지 않음

        // 두 변 연장선까지의 바깥수직거리(꼭짓점 V를 두 직선 위의 점으로 사용).
        double sPrev = (x - V.X) * nPrev.Value.X + (y - V.Y) * nPrev.Value.Y;
        double sNext = (x - V.X) * nNext.Value.X + (y - V.Y) * nNext.Value.Y;
        if (sPrev <= 0 || sNext <= 0) return euclid; // 바깥 쐐기(코너 너머)가 아님

        double mitered = Math.Max(sPrev, sNext); // 항상 ≤ euclid(=|P-V|). 작을수록 모서리가 더 뾰족
        if (mitered <= 1e-9) return euclid;
        if (euclid > miterLimit * mitered) return euclid; // 스파이크가 단거리의 miterLimit배 초과 → 라운드 폴백
        return mitered;
    }

    /// <summary>변 (ax,ay)→(bx,by)의 단위 바깥법선. 길이 0이면 null.</summary>
    private static (double X, double Y)? OutwardNormal(double ax, double ay, double bx, double by, double ccw)
    {
        double ex = bx - ax, ey = by - ay;
        double len = Math.Sqrt(ex * ex + ey * ey);
        if (len < 1e-12) return null;
        ex /= len; ey /= len;
        // CCW 폴리곤은 내부가 진행방향 왼쪽 → 바깥(오른쪽) 법선 = (ey,-ex)
        return ccw > 0 ? (ey, -ex) : (-ey, ex);
    }

    /// <summary>선분 AB 위에서 점 P에 가장 가까운 점과 매개변수 t∈[0,1].</summary>
    private static (double X, double Y, double T) ClosestOnSegment(
        double ax, double ay, double bx, double by, double px, double py)
    {
        double vx = bx - ax, vy = by - ay;
        double len2 = vx * vx + vy * vy;
        if (len2 <= 1e-18) return (ax, ay, 0);
        double t = ((px - ax) * vx + (py - ay) * vy) / len2;
        t = Math.Clamp(t, 0, 1);
        return (ax + t * vx, ay + t * vy, t);
    }

    /// <summary>계획면 평면(z = a·x + b·y + c) 최소제곱 적합. 평탄 폴리곤이면 c=상수.</summary>
    /// <remarks>
    /// 실제 Civil3D 좌표(동·북 좌표 수십만 m)에서 정규방정식의 Σx²이 매우 커져
    /// 정밀도가 무너지므로, 좌표를 무게중심 기준으로 중심화하여 수치 안정성을 확보한다.
    /// </remarks>
    public static Plane FitPlane(IReadOnlyList<Point3> poly)
    {
        int n = poly.Count;
        if (n == 0) return new Plane(0, 0, 0);

        double mx = 0, my = 0;
        foreach (var p in poly) { mx += p.X; my += p.Y; }
        mx /= n; my /= n;

        // 중심화 좌표(dx,dy)로 정규방정식 구성 → Σdx ≈ Σdy ≈ 0 이라 조건수 양호.
        double sxx = 0, sxy = 0, sx = 0, syy = 0, sy = 0;
        double sxz = 0, syz = 0, sz = 0;
        foreach (var p in poly)
        {
            double dx = p.X - mx, dy = p.Y - my;
            sxx += dx * dx; sxy += dx * dy; sx += dx;
            syy += dy * dy; sy += dy;
            sxz += dx * p.Z; syz += dy * p.Z; sz += p.Z;
        }

        // 3x3 선형계 풀이 (Cramer). 특이(공선/평탄)면 상수평면으로 폴백.
        double[,] m =
        {
            { sxx, sxy, sx },
            { sxy, syy, sy },
            { sx,  sy,  n  },
        };
        double[] rhs = { sxz, syz, sz };
        double det = Det3(m);
        if (Math.Abs(det) < 1e-9)
        {
            double avg = sz / n;
            return new Plane(0, 0, avg);
        }

        double a = Det3(Replace(m, 0, rhs)) / det;
        double b = Det3(Replace(m, 1, rhs)) / det;
        double c0 = Det3(Replace(m, 2, rhs)) / det; // 중심 좌표계의 절편

        // 중심화 평면 z = a·dx + b·dy + c0 → 원좌표 절편으로 환원
        double c = c0 - a * mx - b * my;
        return new Plane(a, b, c);
    }

    /// <summary>부호 있는 면적 (XY). &gt;0 = 반시계(CCW), &lt;0 = 시계(CW).</summary>
    public static double SignedArea(IReadOnlyList<Point3> poly)
    {
        double a = 0;
        int n = poly.Count;
        for (int i = 0, j = n - 1; i < n; j = i++)
            a += poly[j].X * poly[i].Y - poly[i].X * poly[j].Y;
        return a * 0.5;
    }

    /// <summary>경계 둘레를 spacing 간격으로 촘촘히 나누고, 각 점의 바깥쪽 단위법선을 함께 돌려준다.</summary>
    /// <remarks>코너 정점은 인접 두 에지 법선의 평균(각 이등분) → 오프셋이 코너를 부드럽게 돈다.</remarks>
    public static List<(Point3 P, double Nx, double Ny)> DensifyWithNormals(
        IReadOnlyList<Point3> poly, double spacing)
    {
        int n = poly.Count;
        double ccw = Math.Sign(SignedArea(poly)); // +1 CCW, -1 CW
        if (ccw == 0) ccw = 1;

        // 각 에지의 바깥 법선
        var en = new (double X, double Y)[n];
        for (int i = 0; i < n; i++)
        {
            var a = poly[i];
            var b = poly[(i + 1) % n];
            double ex = b.X - a.X, ey = b.Y - a.Y;
            double len = Math.Sqrt(ex * ex + ey * ey);
            if (len < 1e-12) { en[i] = (0, 0); continue; }
            ex /= len; ey /= len;
            // CCW 폴리곤은 내부가 진행방향 왼쪽 → 바깥(오른쪽) 법선 = (ey,-ex)
            en[i] = ccw > 0 ? (ey, -ex) : (-ey, ex);
        }

        const double angStep = 0.2618; // 15° — 볼록 코너 부채꼴 각 간격
        var outp = new List<(Point3, double, double)>();
        for (int i = 0; i < n; i++)
        {
            var a = poly[i];
            var b = poly[(i + 1) % n];
            int prev = (i - 1 + n) % n;
            var n0 = en[prev];
            var n1 = en[i];

            // 코너에서 법선이 도는 각(부호). CCW 폴리곤은 좌회전(+)이 볼록.
            double dot = n0.X * n1.X + n0.Y * n1.Y;
            double crs = n0.X * n1.Y - n0.Y * n1.X;
            double turn = Math.Atan2(crs, dot); // (-π, π]
            bool convex = turn * ccw > 1e-9;

            if (convex && Math.Abs(turn) > angStep)
            {
                // 볼록 코너 — 외부 부채꼴을 법선 보간으로 촘촘히 채움(둥근 오프셋처럼, 가시 제거)
                int fan = Math.Max(1, (int)Math.Ceiling(Math.Abs(turn) / angStep));
                for (int s = 0; s <= fan; s++)
                {
                    double th = turn * s / fan;
                    double cx = n0.X * Math.Cos(th) - n0.Y * Math.Sin(th);
                    double cy = n0.X * Math.Sin(th) + n0.Y * Math.Cos(th);
                    outp.Add((a, cx, cy));
                }
            }
            else
            {
                // 직선/오목 코너 — 이등분 법선 1개
                double cnx = n0.X + n1.X, cny = n0.Y + n1.Y;
                double cl = Math.Sqrt(cnx * cnx + cny * cny);
                if (cl < 1e-12) { cnx = n1.X; cny = n1.Y; cl = 1; }
                outp.Add((a, cnx / cl, cny / cl));
            }

            // 에지 중간 점들 — 해당 에지 법선
            double dx = b.X - a.X, dy = b.Y - a.Y;
            double L = Math.Sqrt(dx * dx + dy * dy);
            int steps = (int)Math.Floor(L / spacing);
            for (int s = 1; s <= steps; s++)
            {
                double t = s * spacing / L;
                if (t >= 1 - 1e-9) break;
                outp.Add((new Point3(a.X + dx * t, a.Y + dy * t, 0), n1.X, n1.Y));
            }
        }
        return outp;
    }

    private static double Det3(double[,] m) =>
        m[0, 0] * (m[1, 1] * m[2, 2] - m[1, 2] * m[2, 1]) -
        m[0, 1] * (m[1, 0] * m[2, 2] - m[1, 2] * m[2, 0]) +
        m[0, 2] * (m[1, 0] * m[2, 1] - m[1, 1] * m[2, 0]);

    private static double[,] Replace(double[,] m, int col, double[] v)
    {
        var r = (double[,])m.Clone();
        for (int i = 0; i < 3; i++) r[i, col] = v[i];
        return r;
    }
}

/// <summary>계획면 평면 z = A·x + B·y + C.</summary>
public readonly record struct Plane(double A, double B, double C)
{
    public double At(double x, double y) => A * x + B * y + C;
}
