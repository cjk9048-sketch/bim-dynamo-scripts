namespace DH.Grading.Core;

/// <summary>★[JACK 1111 «종단선을 구조물 축에 맞춤»] 구조물 평면 외곽(세계 XY) → 주축 · 축 따라 부지를 가로지르는 종단선 · 노선이 축과 벌어진 각 — 순수 함수(하네스 S254).
/// <para>횡단은 종단선에 직각이라 종단선이 구조물을 비스듬히 지나면 벽도 비스듬히 잘려 실제보다 두껍게 보인다.</para>
/// <para>주축 = 볼록 껍질의 <b>최소 넓이 둘러싼 네모</b> 방향(직각 구조물 — 벽이 네모 변과 나란함). 중심 = 껍질들의 <b>넓이 무게중심</b>(L자는 네모 가운데가 빈 곳일 수 있다).</para></summary>
public static class StructAxis
{
    /// <summary>비스듬 알림 문턱(°) — 두 축 어느 쪽과도 이보다 더 벌어지면 알림.</summary>
    public const double WarnDeg = 5.0;
    /// <summary>종단선을 부지 경계(없으면 구조물) 밖으로 더 늘이는 길이(m).</summary>
    public const double Margin = 5.0;
    /// <summary>두 변 길이 차가 이 안이면 «거의 정사각»(긴 축을 못 정함) — 평면 외곽 단순화(1 mm)의 두 배.</summary>
    public const double TieTol = 0.002;

    /// <summary>주축. <paramref name="LongDeg"/> = 긴 축 각(X축에서 반시계 · [0, 180)) · <paramref name="Tie"/> = 거의 정사각이라 [0, 90) 쪽 변을 긴 축으로 골랐다.</summary>
    public sealed record Axis(double Cx, double Cy, double LongDeg, double LongLen, double ShortLen, bool Tie)
    {
        public double ShortDeg => LongDeg >= 90 ? LongDeg - 90 : LongDeg + 90;
    }

    /// <summary>껍질들 → 주축. 점이 셋 못 되거나 한 줄이면 null.</summary>
    public static Axis? Of(IReadOnlyList<IReadOnlyList<(double X, double Y)>> shells)
    {
        var pts = new List<(double X, double Y)>();
        foreach (var s in shells) if (s != null) pts.AddRange(s);
        if (pts.Count < 3) return null;
        double ox = pts[0].X, oy = pts[0].Y;   // 큰 세계 좌표 — 첫 점을 빼고 셈
        var loc = pts.Select(p => (X: p.X - ox, Y: p.Y - oy)).ToList();
        var hull = Hull(loc);
        if (hull.Count < 3) return null;

        double bestArea = double.MaxValue, bestA = 0;
        for (int i = 0; i < hull.Count; i++)
        {
            var p = hull[i]; var q = hull[(i + 1) % hull.Count];
            double dx = q.X - p.X, dy = q.Y - p.Y;
            if (dx * dx + dy * dy < 1e-18) continue;
            double a = Math.Atan2(dy, dx) % (Math.PI / 2);
            if (a < 0) a += Math.PI / 2;
            if (a > Math.PI / 2 - 1e-12) a = 0;
            Extent(hull, a, out double u0, out double u1, out double v0, out double v1);
            double area = (u1 - u0) * (v1 - v0), tol = 1e-9 * Math.Max(1, area);
            if (area < bestArea - tol || (Math.Abs(area - bestArea) <= tol && a < bestA)) { bestArea = area; bestA = a; }
        }
        Extent(hull, bestA, out double U0, out double U1, out double V0, out double V1);
        double lu = U1 - U0, lv = V1 - V0;
        bool tie = Math.Abs(lu - lv) <= TieTol;
        bool longIsU = tie || lu > lv;
        double deg = bestA * 180 / Math.PI + (longIsU ? 0 : 90);

        // 넓이 무게중심(껍질마다 부호 넓이 → 절댓값 가중) · 넓이가 없으면 네모 가운데
        double A = 0, sx = 0, sy = 0;
        foreach (var s in shells)
        {
            if (s == null || s.Count < 3) continue;
            double a2 = 0, cx = 0, cy = 0;
            for (int i = 0; i < s.Count; i++)
            {
                var p = s[i]; var q = s[(i + 1) % s.Count];
                double x0 = p.X - ox, y0 = p.Y - oy, x1 = q.X - ox, y1 = q.Y - oy, cr = x0 * y1 - x1 * y0;
                a2 += cr; cx += (x0 + x1) * cr; cy += (y0 + y1) * cr;
            }
            if (Math.Abs(a2) < 1e-12) continue;
            double w = Math.Abs(a2) / 2;
            A += w; sx += cx / (3 * a2) * w; sy += cy / (3 * a2) * w;
        }
        double ccx, ccy;
        if (A > 1e-9) { ccx = sx / A; ccy = sy / A; }
        else
        {
            double c = Math.Cos(bestA), sn = Math.Sin(bestA), um = (U0 + U1) / 2, vm = (V0 + V1) / 2;
            ccx = um * c - vm * sn; ccy = um * sn + vm * c;
        }
        return new Axis(ccx + ox, ccy + oy, deg, Math.Max(lu, lv), Math.Min(lu, lv), tie);
    }

    /// <summary>축 따라 종단선 두 점 — 구조물 길이와, 부지 경계 선(닫힘이면 끝 → 첫 변도)이 축선과 만나는 곳을 모두 덮고 양끝 <paramref name="margin"/> 더.
    /// 방향 = 축 각 쪽(각 [0, 180)이라 시점이 서쪽 · 90°면 남쪽). <paramref name="hitSite"/> = 부지 경계와 만났나.</summary>
    public static ((double X, double Y) A, (double X, double Y) B) Line(Axis ax, bool shortAxis,
        IReadOnlyList<IReadOnlyList<(double X, double Y)>> shells,
        IReadOnlyList<(IReadOnlyList<(double X, double Y)> Pts, bool Closed)> site, double margin, out bool hitSite)
    {
        double r = (shortAxis ? ax.ShortDeg : ax.LongDeg) * Math.PI / 180, dx = Math.Cos(r), dy = Math.Sin(r);
        double tmin = double.MaxValue, tmax = double.MinValue;
        foreach (var s in shells)
            if (s != null)
                foreach (var p in s) { double t = (p.X - ax.Cx) * dx + (p.Y - ax.Cy) * dy; tmin = Math.Min(tmin, t); tmax = Math.Max(tmax, t); }
        if (tmin > tmax) tmin = tmax = 0;
        hitSite = false;
        foreach (var (pts, closed) in site)
        {
            if (pts == null) continue;
            int n = pts.Count, m = closed ? n : n - 1;
            for (int i = 0; i < m; i++)
            {
                var p = pts[i]; var q = pts[(i + 1) % n];
                double px = p.X - ax.Cx, py = p.Y - ax.Cy, qx = q.X - ax.Cx, qy = q.Y - ax.Cy;
                double sp = px * -dy + py * dx, sq = qx * -dy + qy * dx;   // 축선에서 잰 옆 거리
                if ((sp > 0 && sq > 0) || (sp < 0 && sq < 0)) continue;
                var ts = new List<double>();
                if (sp == sq) { ts.Add(px * dx + py * dy); ts.Add(qx * dx + qy * dy); }   // 축선 위에 놓인 변
                else { double f = sp / (sp - sq); ts.Add((px + f * (qx - px)) * dx + (py + f * (qy - py)) * dy); }
                foreach (double t in ts) { tmin = Math.Min(tmin, t); tmax = Math.Max(tmax, t); hitSite = true; }
            }
        }
        tmin -= margin; tmax += margin;
        return ((ax.Cx + tmin * dx, ax.Cy + tmin * dy), (ax.Cx + tmax * dx, ax.Cy + tmax * dy));
    }

    /// <summary>선 방향과 축이 벌어진 각 — 두 축(서로 직각) 중 가까운 쪽과의 차 [0, 45]°.</summary>
    public static double Oblique(double lineDeg, double axisDeg)
    {
        double d = ((lineDeg - axisDeg) % 90 + 90) % 90;
        return Math.Min(d, 90 - d);
    }

    /// <summary>노선(꺾은 선)에서 구조물 외곽(고리들)을 지나는 토막 가운데 가장 비스듬한 각 — 안 지나면 null.</summary>
    public static double? CrossDeg(IReadOnlyList<(double X, double Y)> route, IReadOnlyList<IReadOnlyList<(double X, double Y)>> rings, double axisDeg)
    {
        double? worst = null;
        for (int i = 0; i + 1 < route.Count; i++)
        {
            var p = route[i]; var q = route[i + 1];
            if (Math.Abs(q.X - p.X) + Math.Abs(q.Y - p.Y) < 1e-9 || !Hits(p, q, rings)) continue;
            double o = Oblique(Math.Atan2(q.Y - p.Y, q.X - p.X) * 180 / Math.PI, axisDeg);
            if (worst == null || o > worst) worst = o;
        }
        return worst;
    }

    static bool Hits((double X, double Y) p, (double X, double Y) q, IReadOnlyList<IReadOnlyList<(double X, double Y)>> rings)
    {
        foreach (var r in rings)
        {
            if (r == null || r.Count < 3) continue;
            if (Inside(p, r) || Inside(q, r)) return true;
            for (int i = 0; i < r.Count; i++)
                if (SegX(p, q, r[i], r[(i + 1) % r.Count])) return true;
        }
        return false;
    }

    static bool Inside((double X, double Y) p, IReadOnlyList<(double X, double Y)> r)
    {
        bool c = false;
        for (int i = 0, j = r.Count - 1; i < r.Count; j = i++)
            if ((r[i].Y > p.Y) != (r[j].Y > p.Y) && p.X < (r[j].X - r[i].X) * (p.Y - r[i].Y) / (r[j].Y - r[i].Y) + r[i].X) c = !c;
        return c;
    }

    static double Orient((double X, double Y) a, (double X, double Y) b, (double X, double Y) c)
        => (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);

    static bool SegX((double X, double Y) p, (double X, double Y) q, (double X, double Y) a, (double X, double Y) b)
    {
        double d1 = Orient(a, b, p), d2 = Orient(a, b, q), d3 = Orient(p, q, a), d4 = Orient(p, q, b);
        if (((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) && ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0))) return true;
        static bool On((double X, double Y) s, (double X, double Y) e, (double X, double Y) x)
            => Math.Min(s.X, e.X) <= x.X && x.X <= Math.Max(s.X, e.X) && Math.Min(s.Y, e.Y) <= x.Y && x.Y <= Math.Max(s.Y, e.Y);
        return (d1 == 0 && On(a, b, p)) || (d2 == 0 && On(a, b, q)) || (d3 == 0 && On(p, q, a)) || (d4 == 0 && On(p, q, b));
    }

    /// <summary>볼록 껍질(Andrew 단조 사슬 · 반시계 · 한 줄 위 점 뺌).</summary>
    static List<(double X, double Y)> Hull(List<(double X, double Y)> pts)
    {
        var p = pts.Distinct().OrderBy(a => a.X).ThenBy(a => a.Y).ToList();
        if (p.Count < 3) return p;
        var h = new List<(double X, double Y)>();
        for (int pass = 0; pass < 2; pass++)
        {
            int start = h.Count;
            foreach (var a in p)
            {
                while (h.Count >= start + 2 && Orient(h[^2], h[^1], a) <= 0) h.RemoveAt(h.Count - 1);
                h.Add(a);
            }
            h.RemoveAt(h.Count - 1);
            p.Reverse();
        }
        return h;
    }

    static void Extent(List<(double X, double Y)> hull, double a, out double u0, out double u1, out double v0, out double v1)
    {
        double c = Math.Cos(a), s = Math.Sin(a);
        u0 = v0 = double.MaxValue; u1 = v1 = double.MinValue;
        foreach (var p in hull)
        {
            double u = p.X * c + p.Y * s, v = -p.X * s + p.Y * c;
            u0 = Math.Min(u0, u); u1 = Math.Max(u1, u); v0 = Math.Min(v0, v); v1 = Math.Max(v1, v);
        }
    }
}
