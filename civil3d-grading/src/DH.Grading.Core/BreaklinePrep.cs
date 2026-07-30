namespace DH.Grading.Core;

/// <summary>[0729 — JACK] 보조 브레이크라인(코너 능선·플래토 직선·단차 경계선 레이)이 계단 링과 평면에서
/// 교차하면 Civil3D가 교차 지점마다 이벤트 뷰어에 경고를 남긴다(형상은 정상 — 완화로 Z가 맞춰져 있음).
/// 교차점을 두 폴리선 '모두'에 공유 정점으로 미리 삽입해 교차 대신 접점으로 만든다 — 알림 제거.
/// 삽입 Z는 링 보간값(링이 촘촘한 기준 기하) — 보조선도 같은 값으로 스냅(완화 덕에 차이는 사실상 0).</summary>
public static class BreaklinePrep
{
    /// <summary>직전 실행에서 교차점의 max|보조선Z − 링Z|(m) — 완화 전제 검증용 진단(정상≈0).
    /// 값이 크면 상류(완화) 회귀 징후: 보조선이 그만큼 조용히 수직 이동했다는 뜻(리뷰 0729 사소3).</summary>
    public static double LastMaxZGap { get; private set; }

    /// <summary>lines × rings의 2D 진교차점을 양쪽에 삽입(in-place). 반환=삽입 정점 수(양쪽 합).
    /// tol(m)=기존 정점과 이 거리 안이면 그쪽엔 삽입 생략(정점이 이미 접점 역할).</summary>
    public static int SplitLineRingCrossings(
        IReadOnlyList<List<Point3>> rings, IReadOnlyList<List<Point3>> lines, double tol = 1e-3)
    {
        int inserted = 0;
        LastMaxZGap = 0;
        if (rings == null || lines == null) return 0;
        foreach (var line in lines)
        {
            if (line == null || line.Count < 2) continue;
            foreach (var ring in rings)
            {
                if (ring == null || ring.Count < 3) continue;
                inserted += SplitPair(ring, line, tol);
            }
        }
        return inserted;
    }

    /// <summary>한 (링, 열린선) 쌍의 교차 수집 후 일괄 삽입. 링은 닫힘(마지막→첫 세그 포함, 중복 끝점 감지).</summary>
    private static int SplitPair(List<Point3> ring, List<Point3> line, double tol)
    {
        int n = ring.Count;
        bool dupClosed = System.Math.Abs(ring[0].X - ring[n - 1].X) < 1e-9
                      && System.Math.Abs(ring[0].Y - ring[n - 1].Y) < 1e-9;
        int segN = dupClosed ? n - 1 : n;             // 링 세그 수(비중복이면 wrap 세그 포함)

        var insRing = new List<(int Seg, double U, Point3 P)>();
        var insLine = new List<(int Seg, double U, Point3 P)>();

        for (int i = 0; i < segN; i++)
        {
            var a = ring[i]; var b = ring[(i + 1) % n];
            double abLen = Dist2D(a, b);
            if (abLen < 1e-9) continue;
            for (int j = 0; j + 1 < line.Count; j++)
            {
                var c = line[j]; var d = line[j + 1];
                double cdLen = Dist2D(c, d);
                if (cdLen < 1e-9) continue;
                if (!SegX2D(a, b, c, d, out double u, out double v)) continue;

                double x = a.X + (b.X - a.X) * u, y = a.Y + (b.Y - a.Y) * u;
                double z = a.Z + (b.Z - a.Z) * u;               // Z = 링 보간(기준)
                double zLine = c.Z + (d.Z - c.Z) * v;           // 보조선 보간 — 완화가 맞으면 z와 거의 동일
                double gap = System.Math.Abs(z - zLine);
                if (gap > LastMaxZGap) LastMaxZGap = gap;
                var p = new Point3(x, y, z);
                double tu = tol / abLen, tv = tol / cdLen;      // 끝점 근접(거리 tol) → 그쪽 삽입 생략
                if (u > tu && u < 1 - tu) insRing.Add((i, u, p));
                if (v > tv && v < 1 - tv) insLine.Add((j, v, p));
            }
        }

        // [중복 제거] 교차점이 기존 정점 '위'에 떨어지면 인접 두 세그먼트에서 이중 감지됨 → XY 근접 중복 제거
        //   (중복 정점은 0길이 세그먼트가 되어 그 자체로 이벤트 경고를 만들 수 있음).
        Dedup(insRing, tol);
        Dedup(insLine, tol);
        InsertSorted(ring, insRing, n);
        InsertSorted(line, insLine, -1);
        return insRing.Count + insLine.Count;
    }

    private static void Dedup(List<(int Seg, double U, Point3 P)> ins, double tol)
    {
        for (int i = ins.Count - 1; i >= 0; i--)
            for (int j = 0; j < i; j++)
                if (Dist2D(ins[i].P, ins[j].P) < tol) { ins.RemoveAt(i); break; }
    }

    /// <summary>수집된 (세그, u, 점)을 뒤에서부터 삽입 — 인덱스 밀림 없이. wrapCount≥0이면 링(wrap 세그=끝에 덧붙임).</summary>
    private static void InsertSorted(List<Point3> pts, List<(int Seg, double U, Point3 P)> ins, int wrapCount)
    {
        if (ins.Count == 0) return;
        ins.Sort((p1, p2) => p1.Seg != p2.Seg ? p1.Seg.CompareTo(p2.Seg) : p1.U.CompareTo(p2.U));
        for (int k = ins.Count - 1; k >= 0; k--)
        {
            var (seg, _, p) = ins[k];
            int at = seg + 1;
            if (wrapCount >= 0 && at > pts.Count) at = pts.Count;   // 링 wrap 세그(마지막→첫) → 끝에 덧붙임
            pts.Insert(at, p);
        }
    }

    /// <summary>2D 선분 진교차(평행/공선 제외). u=a→b, v=c→d 파라미터(0..1, 경계 포함).</summary>
    private static bool SegX2D(Point3 a, Point3 b, Point3 c, Point3 d, out double u, out double v)
    {
        u = v = 0;
        double rx = b.X - a.X, ry = b.Y - a.Y;
        double sx = d.X - c.X, sy = d.Y - c.Y;
        double den = rx * sy - ry * sx;
        if (System.Math.Abs(den) < 1e-12) return false;
        double qx = c.X - a.X, qy = c.Y - a.Y;
        u = (qx * sy - qy * sx) / den;
        v = (qx * ry - qy * rx) / den;
        return u >= 0 && u <= 1 && v >= 0 && v <= 1;
    }

    private static double Dist2D(Point3 a, Point3 b)
    {
        double dx = b.X - a.X, dy = b.Y - a.Y;
        return System.Math.Sqrt(dx * dx + dy * dy);
    }
}
