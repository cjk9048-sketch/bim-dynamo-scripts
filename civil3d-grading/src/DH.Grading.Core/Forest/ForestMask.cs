using NetTopologySuite.Geometries;
using NetTopologySuite.Geometries.Prepared;

namespace DH.Grading.Core.Forest;

/// <summary>★[v115 · JACK 1105 임상도 · 재검토 N2] 수목 «설계 자리» 판정(도면 좌표) — ① 설계 다각형(계획면 · 띠 · 옹벽 가림 · 옹벽 발자국 · 도로) 안이거나
/// ② 그 자리 지형 세모의 구분이 <b>원지반이 아님</b>(<see cref="TerrainMesh.Output.TriCls"/>). 바뀐 칸(2 m 격자 · 세모 상자로 표시)을 쓰지 않아 네모난 민둥 자리가 안 생긴다.
/// 세모 찾기 = 고른 격자 색인(세모 상자가 걸친 칸마다) · 점이 어느 세모에도 없으면 «설계 아님»(그 자리는 SurfZ도 없어 따로 빠짐).</summary>
public sealed class ForestMask
{
    readonly double[] _x, _y;
    readonly int[] _f;
    readonly TerrainMesh.Cls[] _cls;
    readonly IPreparedGeometry[] _design;
    readonly double _x0, _y0, _cell;
    readonly int _w, _h;
    readonly int[] _start, _items;

    public ForestMask(double[] x, double[] y, int[] f, TerrainMesh.Cls[] cls, IEnumerable<Geometry?> design, int maxCells = 4_000_000)
    {
        _x = x; _y = y; _f = f; _cls = cls;
        _design = design.Where(g => g != null && !g.IsEmpty).Select(g => PreparedGeometryFactory.Prepare(g!)).ToArray();
        int nT = f.Length / 3;
        double mnx = double.MaxValue, mny = double.MaxValue, mxx = double.MinValue, mxy = double.MinValue;
        foreach (double v in x) { mnx = Math.Min(mnx, v); mxx = Math.Max(mxx, v); }
        foreach (double v in y) { mny = Math.Min(mny, v); mxy = Math.Max(mxy, v); }
        if (nT == 0 || mnx > mxx) { _start = new int[1]; _items = Array.Empty<int>(); _cell = 1; _w = _h = 0; return; }
        // 칸 크기 = 세모 평균 넓이 쯤(칸마다 세모 몇 개) · 칸 수 한도
        double area = Math.Max(1, (mxx - mnx) * (mxy - mny));
        double cell = Math.Max(0.5, Math.Sqrt(area / Math.Max(1, nT)) * 2);
        while ((mxx - mnx) / cell * ((mxy - mny) / cell) > maxCells) cell *= 1.25;
        _x0 = mnx; _y0 = mny; _cell = cell;
        _w = (int)Math.Floor((mxx - mnx) / cell) + 1; _h = (int)Math.Floor((mxy - mny) / cell) + 1;
        var cnt = new int[_w * _h + 1];
        void Span(int t, out int i0, out int i1, out int j0, out int j1)
        {
            int a = f[3 * t], b = f[3 * t + 1], c = f[3 * t + 2];
            i0 = (int)Math.Floor((Math.Min(x[a], Math.Min(x[b], x[c])) - _x0) / _cell); i1 = (int)Math.Floor((Math.Max(x[a], Math.Max(x[b], x[c])) - _x0) / _cell);
            j0 = (int)Math.Floor((Math.Min(y[a], Math.Min(y[b], y[c])) - _y0) / _cell); j1 = (int)Math.Floor((Math.Max(y[a], Math.Max(y[b], y[c])) - _y0) / _cell);
            i0 = Math.Clamp(i0, 0, _w - 1); i1 = Math.Clamp(i1, 0, _w - 1); j0 = Math.Clamp(j0, 0, _h - 1); j1 = Math.Clamp(j1, 0, _h - 1);
        }
        for (int t = 0; t < nT; t++)
        {
            Span(t, out int i0, out int i1, out int j0, out int j1);
            for (int j = j0; j <= j1; j++) for (int i = i0; i <= i1; i++) cnt[j * _w + i + 1]++;
        }
        for (int k = 1; k < cnt.Length; k++) cnt[k] += cnt[k - 1];
        _start = (int[])cnt.Clone();
        _items = new int[cnt[^1]];
        var fill = (int[])cnt.Clone();
        for (int t = 0; t < nT; t++)
        {
            Span(t, out int i0, out int i1, out int j0, out int j1);
            for (int j = j0; j <= j1; j++) for (int i = i0; i <= i1; i++) _items[fill[j * _w + i]++] = t;
        }
    }

    /// <summary>점을 담는 세모(입력 차례) — 없으면 -1.</summary>
    public int TriAt(double px, double py)
    {
        if (_w == 0) return -1;
        int i = (int)Math.Floor((px - _x0) / _cell), j = (int)Math.Floor((py - _y0) / _cell);
        if (i < 0 || j < 0 || i >= _w || j >= _h) return -1;
        int k = j * _w + i;
        for (int q = _start[k]; q < _start[k + 1]; q++)
        {
            int t = _items[q];
            int a = _f[3 * t], b = _f[3 * t + 1], c = _f[3 * t + 2];
            double d = (_y[b] - _y[c]) * (_x[a] - _x[c]) + (_x[c] - _x[b]) * (_y[a] - _y[c]);
            if (d == 0) continue;
            double u = ((_y[b] - _y[c]) * (px - _x[c]) + (_x[c] - _x[b]) * (py - _y[c])) / d;
            double v = ((_y[c] - _y[a]) * (px - _x[c]) + (_x[a] - _x[c]) * (py - _y[c])) / d;
            if (u >= 0 && v >= 0 && u + v <= 1) return t;
        }
        return -1;
    }

    public bool IsDesign(double px, double py)
    {
        if (_design.Length > 0)
        {
            var p = new Point(px, py);
            foreach (var g in _design) if (g.Covers(p)) return true;
        }
        int t = TriAt(px, py);
        return t >= 0 && t < _cls.Length && _cls[t] != TerrainMesh.Cls.Ground;
    }
}
