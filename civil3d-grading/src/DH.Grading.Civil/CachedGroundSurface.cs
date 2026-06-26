using Autodesk.Civil.DatabaseServices;
using DH.Grading.Core;

namespace DH.Grading.Civil;

/// <summary>
/// 원지반 TinSurface의 삼각형을 메모리에 한 번 캐싱하고, 순수 C# 기하(점-삼각형 포함 + 평면 보간)로
/// 표고를 조회한다. ray-march 루프마다 Surface.FindElevationAtXY를 호출하는 비용을 제거(JACK 성능 최적화).
/// 격자 버킷 인덱스로 후보 삼각형만 검사 → O(1)에 가깝게 조회.
/// </summary>
public sealed class CachedGroundSurface : IGroundSurface
{
    // 삼각형 정점 XY/Z (정점 3개 평탄 배열)
    private readonly double[] _ax, _ay, _az, _bx, _by, _bz, _cx, _cy, _cz;
    private readonly int _count;

    // 격자 인덱스
    private readonly double _minX, _minY, _cell;
    private readonly int _nx, _ny;
    private readonly List<int>[] _grid;

    private readonly double _minZ, _maxZ;
    private readonly double _maxX, _maxY;

    /// <summary>표면의 XY 평면 경계(최소거리 비교 시 후보 삼각형 bbox 필터용).</summary>
    public (double MinX, double MinY, double MaxX, double MaxY) XYBounds => (_minX, _minY, _maxX, _maxY);

    public CachedGroundSurface(TinSurface surface)
    {
        var tris = surface.GetTriangles(false); // false = 모든 삼각형 (TinSurfaceTriangleCollection)
        int n = tris.Count;
        _ax = new double[n]; _ay = new double[n]; _az = new double[n];
        _bx = new double[n]; _by = new double[n]; _bz = new double[n];
        _cx = new double[n]; _cy = new double[n]; _cz = new double[n];

        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        double minZ = double.MaxValue, maxZ = double.MinValue;
        int k = 0;
        foreach (var t in tris)
        {
            try
            {
                var p1 = t.Vertex1.Location; var p2 = t.Vertex2.Location; var p3 = t.Vertex3.Location;
                _ax[k] = p1.X; _ay[k] = p1.Y; _az[k] = p1.Z;
                _bx[k] = p2.X; _by[k] = p2.Y; _bz[k] = p2.Z;
                _cx[k] = p3.X; _cy[k] = p3.Y; _cz[k] = p3.Z;
                double tMinX = Math.Min(p1.X, Math.Min(p2.X, p3.X));
                double tMaxX = Math.Max(p1.X, Math.Max(p2.X, p3.X));
                double tMinY = Math.Min(p1.Y, Math.Min(p2.Y, p3.Y));
                double tMaxY = Math.Max(p1.Y, Math.Max(p2.Y, p3.Y));
                if (tMinX < minX) minX = tMinX; if (tMaxX > maxX) maxX = tMaxX;
                if (tMinY < minY) minY = tMinY; if (tMaxY > maxY) maxY = tMaxY;
                double tMinZ = Math.Min(p1.Z, Math.Min(p2.Z, p3.Z));
                double tMaxZ = Math.Max(p1.Z, Math.Max(p2.Z, p3.Z));
                if (tMinZ < minZ) minZ = tMinZ; if (tMaxZ > maxZ) maxZ = tMaxZ;
                k++;
            }
            catch { }
            finally { t.Dispose(); }
        }
        _count = k;
        _minX = minX; _minY = minY; _minZ = minZ; _maxZ = maxZ;
        _maxX = maxX; _maxY = maxY;

        // 격자: 셀 ≈ 삼각형 1개 크기 → nx*ny ≈ 삼각형 수. 후보가 적게 잡히도록.
        double extX = Math.Max(maxX - minX, 1e-6), extY = Math.Max(maxY - minY, 1e-6);
        double cell = Math.Max(Math.Sqrt(extX * extY / Math.Max(k, 1)), 1e-3);
        _cell = cell;
        _nx = Math.Max(1, (int)(extX / cell) + 1);
        _ny = Math.Max(1, (int)(extY / cell) + 1);
        _grid = new List<int>[_nx * _ny];

        for (int i = 0; i < _count; i++)
        {
            double tMinX = Math.Min(_ax[i], Math.Min(_bx[i], _cx[i]));
            double tMaxX = Math.Max(_ax[i], Math.Max(_bx[i], _cx[i]));
            double tMinY = Math.Min(_ay[i], Math.Min(_by[i], _cy[i]));
            double tMaxY = Math.Max(_ay[i], Math.Max(_by[i], _cy[i]));
            int x0 = Clamp((int)((tMinX - minX) / cell), 0, _nx - 1);
            int x1 = Clamp((int)((tMaxX - minX) / cell), 0, _nx - 1);
            int y0 = Clamp((int)((tMinY - minY) / cell), 0, _ny - 1);
            int y1 = Clamp((int)((tMaxY - minY) / cell), 0, _ny - 1);
            for (int gy = y0; gy <= y1; gy++)
                for (int gx = x0; gx <= x1; gx++)
                {
                    int idx = gy * _nx + gx;
                    (_grid[idx] ??= new List<int>()).Add(i);
                }
        }
    }

    public bool TryGetElevation(double x, double y, out double z)
    {
        z = 0;
        if (_count == 0) return false;
        int gx = (int)((x - _minX) / _cell);
        int gy = (int)((y - _minY) / _cell);
        if (gx < 0 || gx >= _nx || gy < 0 || gy >= _ny) return false;
        var bucket = _grid[gy * _nx + gx];
        if (bucket == null) return false;

        foreach (int i in bucket)
        {
            // 2D 무게중심 좌표(barycentric)로 점 포함 판정 + Z 보간.
            double d = (_by[i] - _cy[i]) * (_ax[i] - _cx[i]) + (_cx[i] - _bx[i]) * (_ay[i] - _cy[i]);
            if (Math.Abs(d) < 1e-12) continue;
            double u = ((_by[i] - _cy[i]) * (x - _cx[i]) + (_cx[i] - _bx[i]) * (y - _cy[i])) / d;
            double v = ((_cy[i] - _ay[i]) * (x - _cx[i]) + (_ax[i] - _cx[i]) * (y - _cy[i])) / d;
            double w = 1.0 - u - v;
            if (u >= -1e-9 && v >= -1e-9 && w >= -1e-9)
            {
                z = u * _az[i] + v * _bz[i] + w * _cz[i];
                return true;
            }
        }
        return false;
    }

    /// <summary>표면의 최저/최고 표고 — 마진(필요 단수) 추정에 사용.</summary>
    public (double Min, double Max) ElevationRange() => (_minZ, _maxZ);

    private static int Clamp(int v, int lo, int hi) => v < lo ? lo : (v > hi ? hi : v);
}
