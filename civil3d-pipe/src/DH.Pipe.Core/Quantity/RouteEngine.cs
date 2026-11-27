using DH.Pipe.Core.Geom;
using DH.Pipe.Core.Model;
using DH.Pipe.Core.Profile;
using DH.Pipe.Core.Section;

namespace DH.Pipe.Core.Quantity;

/// <summary>평면 노선(꺾은선). 측점 ↔ 좌표 · 단면 방향(꺾인 점은 반각).</summary>
public sealed class PlanLine
{
    private readonly P2[] _p;
    private readonly double[] _cum;
    /// <summary>꼭짓점 종류: 0 = 꺾인 점(직선끼리 만나는 점) · 1 = 곡선 안의 점(곡선을 잘게 나눈 점) · 2 = 곡선의 시작 · 끝.</summary>
    private readonly byte[] _kind;
    /// <summary>곡선의 시작 · 끝에서 접선이 이 각(도)보다 크게 어긋나면 «꺾인 점»으로 센다(접하지 않는 호).</summary>
    public const double KinkDeg = 0.2;

    public IReadOnlyList<P2> Pts => _p;
    public double Length => _cum[^1];

    /// <param name="pts">꼭짓점(차례대로).</param>
    /// <param name="kinds">꼭짓점 종류(0 꺾인 점 · 1 곡선 안 · 2 곡선 시작/끝). 없으면 전부 꺾인 점.</param>
    public PlanLine(IEnumerable<P2> pts, IEnumerable<int>? kinds = null)
    {
        var list = new List<P2>();
        var kd = new List<byte>();
        using var ke = kinds?.GetEnumerator();
        foreach (var p in pts)
        {
            byte k = (byte)(ke != null && ke.MoveNext() ? Math.Clamp(ke.Current, 0, 2) : 0);
            if (list.Count > 0 && Dist(list[^1], p) < 1e-6)
            {
                byte a = kd[^1];
                kd[^1] = a == 2 || k == 2 ? (byte)2 : Math.Max(a, k);   // 겹친 점
                continue;
            }
            list.Add(p);
            kd.Add(k);
        }
        if (list.Count < 2) throw new ArgumentException("노선 점이 모자람");
        _p = list.ToArray();
        _kind = kd.ToArray();
        // 양 끝 점은 «곡선 안»일 수 없고 · 곡선 안의 점 묶음 양옆은 반드시 곡선의 시작 · 끝
        if (_kind[0] == 1) _kind[0] = 2;
        if (_kind[^1] == 1) _kind[^1] = 2;
        for (int i = 1; i + 1 < _kind.Length; i++)
        {
            if (_kind[i] != 1) continue;
            if (_kind[i - 1] == 0) _kind[i - 1] = 2;
            if (_kind[i + 1] == 0) _kind[i + 1] = 2;
        }
        _cum = new double[_p.Length];
        for (int i = 1; i < _p.Length; i++) _cum[i] = _cum[i - 1] + Dist(_p[i - 1], _p[i]);
    }

    /// <summary>곡선 조각들: 시작(2) → 안의 점(1)들 → 끝(2). I0 · I1 = 시작 · 끝 번호.</summary>
    private List<(int I0, int I1)> CurvePieces()
    {
        var res = new List<(int, int)>();
        for (int i = 0; i + 1 < _p.Length; i++)
        {
            if (_kind[i] != 2 || _kind[i + 1] != 1) continue;
            int j = i + 1;
            while (j < _p.Length - 1 && _kind[j] == 1) j++;
            res.Add((i, j));
            i = j - 1;
        }
        return res;
    }

    /// <summary>곡선 조각의 교각(도 · 왼쪽 +) = 안의 점들의 꺾임 합 + 양 끝 반 칸(원호는 정확히 맞는다).</summary>
    private double PieceDeg(int i0, int i1)
    {
        double sum = 0;
        for (int i = i0 + 1; i <= i1 - 1; i++) sum += Turn(i);
        return sum + 0.5 * Turn(i0 + 1) + 0.5 * Turn(i1 - 1);
    }

    /// <summary>곡선의 시작 · 끝 점에서 접선이 어긋난 각(도). 접하는 곡선이면 0.</summary>
    private double Kink(int i)
    {
        if (i < 1 || i > _p.Length - 2 || _kind[i] != 2) return 0;
        double expect = 0;
        if (_kind[i - 1] == 1) expect += 0.5 * Turn(i - 1);
        if (_kind[i + 1] == 1) expect += 0.5 * Turn(i + 1);
        double d = Turn(i) - expect;
        while (d > 180) d -= 360;
        while (d <= -180) d += 360;
        return d;
    }

    private static double Dist(P2 a, P2 b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    /// <summary>꼭짓점의 측점들(시점 · 꺾인 점 · 곡선의 시작과 끝 · 종점). 곡선 안의 점은 측점으로 삼지 않는다.</summary>
    public IEnumerable<double> VertexStations()
    {
        for (int i = 0; i < _p.Length; i++)
            if (_kind[i] != 1) yield return _cum[i];
    }

    /// <summary>곡선 안에서 꺾임이 maxDeg 쌓일 때마다의 측점 — 3D 모형이 곡선을 따라가게 하는 데 쓴다(수량 측점은 아님).</summary>
    public List<double> CurveStations(double maxDeg)
    {
        var res = new List<double>();
        foreach (var (i0, i1) in CurvePieces())
        {
            double acc = 0;
            for (int i = i0 + 1; i <= i1 - 1; i++)
            {
                acc += Math.Abs(Turn(i));
                if (acc < maxDeg) continue;
                res.Add(_cum[i]);
                acc = 0;
            }
        }
        return res;
    }

    private int Seg(double s)
    {
        if (s <= 0) return 0;
        for (int i = 0; i + 1 < _p.Length; i++) if (s <= _cum[i + 1]) return i;
        return _p.Length - 2;
    }

    public P2 PointAt(double s)
    {
        s = Math.Clamp(s, 0, Length);
        int i = Seg(s);
        double d = _cum[i + 1] - _cum[i], t = d < 1e-12 ? 0 : (s - _cum[i]) / d;
        return new P2(_p[i].X + (_p[i + 1].X - _p[i].X) * t, _p[i].Y + (_p[i + 1].Y - _p[i].Y) * t);
    }

    private P2 Tangent(int seg)
    {
        double dx = _p[seg + 1].X - _p[seg].X, dy = _p[seg + 1].Y - _p[seg].Y, d = Math.Sqrt(dx * dx + dy * dy);
        return new P2(dx / d, dy / d);
    }

    /// <summary>
    /// 단면 선의 방향(진행 방향의 오른쪽 · 단위 길이).
    /// 꺾인 점에서는 앞뒤 직각 방향의 가운데(반각) — 앞 구간의 끝 단면과 뒤 구간의 첫 단면이 한 장이 되어 겹침이 없다.
    /// </summary>
    public P2 RightAt(double s, double tol = 1e-4)
    {
        s = Math.Clamp(s, 0, Length);
        for (int i = 1; i + 1 < _p.Length; i++)
        {
            if (Math.Abs(s - _cum[i]) > tol) continue;
            var a = Tangent(i - 1); var b = Tangent(i);
            double nx = a.Y + b.Y, ny = -(a.X + b.X), d = Math.Sqrt(nx * nx + ny * ny);
            if (d < 1e-9) break;                                // 되돌아가는 노선 — 한쪽 직각으로
            return new P2(nx / d, ny / d);
        }
        var t = Tangent(Seg(s));
        return new P2(t.Y, -t.X);
    }

    /// <summary>꺾인 점의 꺾임 각(도 · 왼쪽 +). 곡선 안의 점은 세지 않고, 곡선의 시작 · 끝은 접선이 어긋났을 때만 센다.</summary>
    public List<(double S, double Deg)> Deflections()
    {
        var res = new List<(double, double)>();
        for (int i = 1; i + 1 < _p.Length; i++)
        {
            if (_kind[i] == 0) res.Add((_cum[i], Turn(i)));
            else if (_kind[i] == 2)
            {
                double k = Kink(i);
                if (Math.Abs(k) > KinkDeg) res.Add((_cum[i], k));
            }
        }
        return res;
    }

    private double Turn(int i)
    {
        var a = Tangent(i - 1); var b = Tangent(i);
        double cross = a.X * b.Y - a.Y * b.X, dot = a.X * b.X + a.Y * b.Y;
        return Math.Atan2(cross, dot) * 180.0 / Math.PI;
    }

    /// <summary>꺾임 목록: 꺾인 점은 하나씩 · 곡선은 하나로(같은 쪽으로 도는 매끈하게 이어진 조각은 합침 — 가운데 측점 · 교각).</summary>
    public List<(double S, double Deg, bool Curve)> Bends()
    {
        var res = new List<(double, double, bool)>();
        foreach (var (s, deg) in Deflections()) res.Add((s, deg, false));
        var pieces = CurvePieces();
        for (int k = 0; k < pieces.Count; k++)
        {
            int i0 = pieces[k].I0, i1 = pieces[k].I1;
            double deg = PieceDeg(i0, i1);
            while (k + 1 < pieces.Count && pieces[k + 1].I0 == i1 && Math.Abs(Kink(i1)) <= KinkDeg)
            {
                double next = PieceDeg(pieces[k + 1].I0, pieces[k + 1].I1);
                if (Math.Abs(next) > 1e-9 && Math.Abs(deg) > 1e-9 && Math.Sign(next) != Math.Sign(deg)) break;     // 반대로 도는 곡선은 따로
                deg += next;
                i1 = pieces[k + 1].I1;
                k++;
            }
            res.Add((0.5 * (_cum[i0] + _cum[i1]), deg, true));
        }
        res.Sort((x, y) => x.Item1.CompareTo(y.Item1));
        return res;
    }

    /// <summary>한 점을 노선에 내린 측점과 거리(오른쪽 +).</summary>
    public (double S, double Offset) Project(P2 q)
    {
        double bestD = double.MaxValue, bestS = 0, bestOff = 0;
        for (int i = 0; i + 1 < _p.Length; i++)
        {
            double dx = _p[i + 1].X - _p[i].X, dy = _p[i + 1].Y - _p[i].Y, l2 = dx * dx + dy * dy;
            double t = Math.Clamp(((q.X - _p[i].X) * dx + (q.Y - _p[i].Y) * dy) / l2, 0, 1);
            double px = _p[i].X + dx * t, py = _p[i].Y + dy * t;
            double d = Math.Sqrt((q.X - px) * (q.X - px) + (q.Y - py) * (q.Y - py));
            if (d >= bestD) continue;
            double l = Math.Sqrt(l2);
            double cross = dx * (q.Y - _p[i].Y) - dy * (q.X - _p[i].X);    // 왼쪽 +
            bestD = d; bestS = _cum[i] + l * t; bestOff = -Math.Sign(cross) * d;
        }
        return (bestS, bestOff);
    }

    /// <summary>다른 선(꺾은선)과 만나는 점들의 측점.</summary>
    public List<double> Crossings(IReadOnlyList<P2> other)
    {
        var res = new List<double>();
        for (int i = 0; i + 1 < _p.Length; i++)
        {
            for (int j = 0; j + 1 < other.Count; j++)
            {
                if (SegHit(_p[i], _p[i + 1], other[j], other[j + 1], out double t)) res.Add(_cum[i] + (_cum[i + 1] - _cum[i]) * t);
            }
        }
        res.Sort();
        var uniq = new List<double>();
        foreach (double s in res) if (uniq.Count == 0 || s - uniq[^1] > 0.05) uniq.Add(s);
        return uniq;
    }

    internal static bool SegHit(P2 a, P2 b, P2 c, P2 d, out double t)
    {
        t = 0;
        double rx = b.X - a.X, ry = b.Y - a.Y, sx = d.X - c.X, sy = d.Y - c.Y;
        double den = rx * sy - ry * sx;
        if (Math.Abs(den) < 1e-12) return false;
        t = ((c.X - a.X) * sy - (c.Y - a.Y) * sx) / den;
        double u = ((c.X - a.X) * ry - (c.Y - a.Y) * rx) / den;
        return t >= -1e-9 && t <= 1 + 1e-9 && u >= -1e-9 && u <= 1 + 1e-9;
    }
}

/// <summary>평면의 지표 구역(닫힌 다각형).</summary>
public sealed class ZonePoly
{
    public SurfaceKind Kind { get; set; }
    public List<P2> Pts { get; set; } = new();
}

/// <summary>노선 하나를 계산하는 데 필요한 것 모두.</summary>
/// <summary>지층면 하나(부지정지의 지층 지표면 사본) = 그 층의 윗면.</summary>
public sealed class StrataSurface
{
    /// <summary>층 번호(위에서 1 …).</summary>
    public int Order { get; set; }
    public string Name { get; set; } = "";
    /// <summary>암 분류: 0 토사 · 1 풍화암 · 2 연암 · 3 보통암 · 4 경암.</summary>
    public int Rock { get; set; }
    public TinIndex Tin { get; set; } = null!;
    /// <summary>도면에 선을 그릴지(부지정지 [지층]에서 고른 값 · 없으면 맨 위 층만 안 그림). 수량에는 늘 쓴다.</summary>
    public bool Show { get; set; } = true;

    /// <summary>
    /// 층 이름으로 암 분류를 어림한다(지표면에 적힌 분류를 못 읽었을 때): «암»이 들면 암 —
    /// 풍화암 = 1 · 보통암 = 3 · 경암(극경암) = 4 · 그 밖의 암(연암 · 기반암 · 암반 …) = 2.
    /// 풍화토 · 매립토 · 퇴적토 · 붕적토 · 표토 = 토사.
    /// </summary>
    public static int RockOfName(string name)
    {
        string n = (name ?? "").Replace(" ", "");
        if (!n.Contains('암')) return 0;
        if (n.Contains("토사") || n.EndsWith("토", StringComparison.Ordinal) || n.Contains("전석")) return 0;     // «암괴 섞인 토사» · «암편 섞인 점토»
        if (n.Contains("풍화암") || n.Contains("리핑")) return 1;
        if (n.Contains("보통암")) return 3;
        if (n.Contains("경암")) return 4;
        return 2;
    }

    /// <summary>
    /// 옛 지층면(부지정지 0831 이전 — n번 면 = n번 층의 **아랫면**)을 윗면 기준으로 바꾼다:
    /// n번 층의 윗면 = (n − 1)번 면 · 1번 층의 윗면 = 원지반. 맨 아래 면(마지막 층의 아랫면)은 쓰지 않는다(그 아래는 마지막 층이 이어진다고 본다).
    /// 그대로 윗면으로 읽으면 암 분류가 한 층씩 밀린다 — 합계는 같아서 대조로는 안 잡힌다.
    /// </summary>
    public static List<StrataSurface> FromBottomBased(IReadOnlyList<StrataSurface> bottoms, TinIndex ground)
    {
        // 층 번호로 짝짓는다: n번 층의 윗면 = (n − 1)번 면. 가운데 면 하나를 못 읽어 빠졌으면 그 아래 층은 윗면을 모르므로 뺀다
        // (목록 차례로 밀면 틀린 면과 짝지어진다 — 재검토 낮음).
        var byOrder = new Dictionary<int, StrataSurface>();
        foreach (var b in bottoms) byOrder[b.Order] = b;
        int first = bottoms.Count > 0 ? bottoms.Min(b => b.Order) : 0;
        var res = new List<StrataSurface>();
        foreach (var b in bottoms.OrderBy(x => x.Order))
        {
            if (b.Order == first) { res.Add(new StrataSurface { Order = b.Order, Name = b.Name, Rock = b.Rock, Show = false, Tin = ground }); continue; }
            if (!byOrder.TryGetValue(b.Order - 1, out var above)) continue;
            res.Add(new StrataSurface { Order = b.Order, Name = b.Name, Rock = b.Rock, Show = b.Show, Tin = above.Tin });
        }
        return res;
    }

    /// <summary>부지정지가 지표면 설명란에 적는 분류 이름(Soil · Weathered · Soft · Medium · Hard) → 번호. 모르면 −1.</summary>
    public static int RockOfClass(string? className) => (className ?? "").Trim() switch
    {
        "Soil" => 0, "Weathered" => 1, "Soft" => 2, "Medium" => 3, "Hard" => 4, _ => -1,
    };
}

public sealed class RouteContext
{
    public RouteData Route { get; set; } = new();
    public PlanLine Plan { get; set; } = null!;
    public TinIndex Tin { get; set; } = null!;
    public ProfileLine Profile { get; set; } = new();
    public List<ZonePoly> Zones { get; set; } = new();
    public List<List<P2>> Curbs { get; set; } = new();
    /// <summary>지층면들(위에서 아래 차례). 있으면 «땅속 값»보다 먼저 쓴다.</summary>
    public List<StrataSurface> Strata { get; set; } = new();
    /// <summary>지하수위 면. 있으면 «땅속 값»의 지하수위보다 먼저 쓴다.</summary>
    public TinIndex? WaterTin { get; set; }
}

/// <summary>노선 · 지형 · 종단 · 공법을 엮어 측점마다 단면을 만든다.</summary>
public static class RouteEngine
{
    /// <summary>노선을 따라간 지반 종단(측점 → 지반고). 세모망 밖 구간은 빠진다 — 가장 긴 이어진 토막을 준다.</summary>
    public static Pl? GroundAlong(PlanLine plan, TinIndex tin, List<string>? notes = null)
    {
        var runs = RunsAlong(plan, tin);
        if (runs.Count == 0) return null;
        var best = runs.OrderByDescending(r => r[^1].S - r[0].S).First();
        if (runs.Count > 1 || best[0].S > 0.01 || best[^1].S < plan.Length - 0.01)
            notes?.Add($"원지반 범위 밖 구간 있음(쓰는 범위 {best[0].S:0.0} ~ {best[^1].S:0.0})");
        return Pl.From(best.Select(q => (q.S, q.Z)));
    }

    /// <summary>노선을 따라 뜬 면의 높이 — 면이 덮는 토막마다 하나(지층면 · 지하수위 면은 노선 일부만 덮을 수 있다).</summary>
    public static List<Pl> SurfaceAlong(PlanLine plan, TinIndex tin)
        => RunsAlong(plan, tin).Select(r => Pl.From(r.Select(q => (q.S, q.Z)))).ToList();

    private static List<List<(double S, double Z)>> RunsAlong(PlanLine plan, TinIndex tin)
    {
        var runs = new List<List<(double S, double Z)>>();
        List<(double S, double Z)>? cur = null;
        double baseS = 0;
        var pts = plan.Pts;
        for (int i = 0; i + 1 < pts.Count; i++)
        {
            double len = Math.Sqrt(Math.Pow(pts[i + 1].X - pts[i].X, 2) + Math.Pow(pts[i + 1].Y - pts[i].Y, 2));
            var segRuns = tin.Profile(pts[i].X, pts[i].Y, pts[i + 1].X, pts[i + 1].Y);
            foreach (var r in segRuns)
            {
                bool joins = cur != null && Math.Abs(baseS + r[0].D - cur[^1].S) < 1e-3;
                if (!joins) { cur = new List<(double, double)>(); runs.Add(cur); }
                foreach (var q in r)
                {
                    double s = baseS + q.D;
                    if (cur!.Count > 0 && s - cur[^1].S < 1e-6) continue;
                    cur.Add((s, q.Z));
                }
            }
            if (segRuns.Count == 0 || Math.Abs(segRuns[^1][^1].D - len) > 1e-3) cur = null;   // 구간 끝이 세모망 밖 — 다음과 안 이어짐
            baseS += len;
        }
        runs.RemoveAll(r => r.Count < 2);
        return runs;
    }

    /// <summary>측점 목록(꼭 있어야 하는 곳 포함).</summary>
    /// <summary>지층면이 있는데 그 단면 자리를 안 덮을 때의 알림 글.</summary>
    public const string OutsideStrata = "지층면 밖";

    /// <summary>경계(공법 · 관이 바뀌는 곳)에서 이 거리(m) 안의 다른 측점은 뺀다.</summary>
    public const double NearBreak = 0.10;

    public static List<double> Stations(RouteContext c, double s0, double s1, IEnumerable<double>? extra = null)
    {
        // 경계 — 전 · 후 두 장이 필요하므로 자리를 그대로 지킨다
        var breaks = new List<double>();
        foreach (var m in c.Route.Methods) { breaks.Add(m.StaFrom); breaks.Add(m.StaTo); }
        foreach (var r in c.Route.Runs) { breaks.Add(r.StaFrom); breaks.Add(r.StaTo); }
        var must = new List<double>(breaks);
        // 그 밖의 측점(꺾인 점 · PVI …)이 경계에 바짝 붙어 있으면 뺀다 — 추진 수직구의 수직 관 PVI(5 cm 옆) · 경계와 몇 mm 어긋난 꺾인 점.
        // 안 빼면 같은 이름의 단면이 두 장 나온다(1127 화면 없는 Civil 도면에서 봄). 수량은 경계의 전 · 후 단면이 맡는다.
        void Soft(IEnumerable<double> list)
        {
            foreach (double x in list)
            {
                bool near = false;
                foreach (double b in breaks) if (Math.Abs(b - x) < NearBreak) { near = true; break; }
                if (!near) must.Add(x);
            }
        }
        if (extra != null) Soft(extra);
        Soft(c.Plan.VertexStations());
        Soft(c.Profile.Pvi.Select(p => p.S));
        Soft(c.Route.ExtraStations);
        return StationPlan.Build(s0, s1, c.Route.Std.StationInterval, must);
    }

    /// <summary>그 측점이 공법 · 관 구간의 경계인지(전 · 후 단면이 달라지는 곳).</summary>
    public static bool IsBreak(RouteData r, double sta)
    {
        if (r.MethodAt(sta, -1) != r.MethodAt(sta, +1)) return true;
        var a = r.PipeAt(sta - 1e-4); var b = r.PipeAt(sta + 1e-4);
        return a.Dn != b.Dn || a.Material != b.Material || Math.Abs(a.Od - b.Od) > 1e-9;
    }

    /// <summary>한 측점의 단면 입력을 만든다. side = −1(전) · +1(후) · 0.</summary>
    public static SectionInput? InputAt(RouteContext c, double sta, int side, out string why, double halfScale = 1.0)
    {
        why = "";
        var route = c.Route; var std = route.Std;
        double sm = sta + side * 1e-4;
        var pipe = route.PipeAt(sm);
        var method = route.MethodAt(sta, side);
        // 추진 구간 끝의 바깥쪽(개착) 단면은 수직구 안 수직 관의 아래가 아니라 그 바깥의 관저고로 본다(검토 낮음)
        bool riserSide = side != 0 && method.Kind != MethodKind.Jacking && route.MethodAt(sta, -side).Kind == MethodKind.Jacking;
        double invert = c.Profile.At(riserSide ? sta + side * (InitialProfile.RiserGap + 0.01) : sta);
        if (double.IsNaN(invert)) { why = "종단 없음"; return null; }

        var center = c.Plan.PointAt(sta);
        var right = c.Plan.RightAt(sta);
        if (!c.Tin.TryElevation(center.X, center.Y, out double gCl)) { why = "원지반 범위 밖"; return null; }

        // 지반을 읽을 반폭: 터파기 예상 폭 + 여유
        double depth = Math.Max(0, gCl - (invert - pipe.Wall - std.Bedding));
        double n = method.IsVertical || method.Kind == MethodKind.Jacking ? 0 : (method.SlopeSoil ?? (method.Kind == MethodKind.RiverOpen ? std.RiverSlope : std.SlopeSoil));
        double half = Math.Max(std.XsecHalfWidthMin, pipe.Od * 0.5 + 1.5 + n * depth * 1.3 + 2.0) * halfScale;
        var run = c.Tin.ProfileAround(center.X - right.X * half, center.Y - right.Y * half, center.X + right.X * half, center.Y + right.Y * half, half);
        if (run == null || run[0].D > half - 0.3 || run[^1].D < half + 0.3) { why = "원지반 범위 밖"; return null; }
        var ground = Pl.From(run.Select(q => (q.D - half, q.Z)));

        var st = route.StrataAt(sta);
        var input = new SectionInput
        {
            Station = sta,
            Label = StationPlan.Name(sta, std.StationInterval),
            Ground = ground,
            Method = method,
            Std = std,
            SoilThk = st.SoilThk,
            WrThk = st.WrThk,
            WaterElev = st.WaterDepth.HasValue ? gCl - st.WaterDepth.Value : null,
        };
        input.Pipes.Add(SectionPipe.Of(pipe, invert));

        var a = new P2(center.X - right.X * half, center.Y - right.Y * half);
        var b = new P2(center.X + right.X * half, center.Y + right.Y * half);
        // 지층면 · 지하수위 면: 단면 선을 따라 뜬다(면이 이 자리를 안 덮으면 그 층은 «땅속 값»으로 물러난다)
        // 면이 단면 가운데 절반(중심 ± 반폭의 절반)을 덮어야 쓴다 — 귀퉁이만 걸친 면의 끝값을 트렌치 한가운데까지 늘려 쓰지 않게(검토 높음).
        // 그 밖(바깥 사분의 일)은 끝값으로 이어 준다.
        bool Covers(List<(double D, double Z)>? run) => run != null && run.Count >= 2 && run[0].D <= half * 0.5 + 1e-6 && run[^1].D >= half * 1.5 - 1e-6;
        foreach (var ly in c.Strata)
        {
            var lr = ly.Tin.ProfileAround(a.X, a.Y, b.X, b.Y, half);
            if (!Covers(lr))
            {
                // 한 층이라도 못 덮으면 이 측점은 통째로 «땅속 값»으로 물러난다(풍화암 면은 없고 연암 면만 있으면 풍화암이 토사로 세어진다)
                input.Layers.Clear();
                break;
            }
            input.Layers.Add(new StrataLine { Name = ly.Name, Rock = ly.Rock, Show = ly.Show, Top = Pl.From(lr!.Select(q => (q.D - half, q.Z))) });
        }
        // 지층면이 있는데 이 자리를 안 덮는다 — 말없이 토사로 세지 않게 알린다(«땅속 값»이 있으면 그것으로, 없으면 전부 토사)
        if (c.Strata.Count > 0 && input.Layers.Count == 0) input.Notes.Add(OutsideStrata);
        if (c.WaterTin != null)
        {
            var wr = c.WaterTin.ProfileAround(a.X, a.Y, b.X, b.Y, half);
            if (Covers(wr)) input.WaterLine = Pl.From(wr!.Select(q => (q.D - half, q.Z)));
        }

        // 지표 구역 · 연석: 단면 선과 만나는 범위
        foreach (var z in c.Zones)
        {
            // 단면 선을 구역 테두리와 만나는 점에서 자르고, 토막마다 가운데 점이 구역 안인지 본다.
            // (만나는 점을 둘씩 짝짓는 방식은 테두리 꼭짓점이 단면 선 위에 오면 어긋나 구역이 뒤집힌다 — 1127 검토 높음)
            var raw = new List<double> { 0, 1 };
            for (int i = 0; i < z.Pts.Count; i++)
            {
                if (PlanLine.SegHit(a, b, z.Pts[i], z.Pts[(i + 1) % z.Pts.Count], out double t)) raw.Add(Math.Clamp(t, 0, 1));
            }
            raw.Sort();
            var cuts = new List<double>();
            foreach (double t in raw) if (cuts.Count == 0 || t - cuts[^1] > 1e-9) cuts.Add(t);
            double open = double.NaN;
            for (int i = 0; i + 1 < cuts.Count; i++)
            {
                double tm = 0.5 * (cuts[i] + cuts[i + 1]);
                bool inside = Inside(z.Pts, new P2(a.X + (b.X - a.X) * tm, a.Y + (b.Y - a.Y) * tm));
                if (inside && double.IsNaN(open)) open = cuts[i];
                bool last = i + 2 == cuts.Count;
                if (double.IsNaN(open) || (inside && !last)) continue;
                double end = inside ? cuts[i + 1] : cuts[i];
                double x0 = open * 2 * half - half, x1 = end * 2 * half - half;
                if (x1 - x0 > 1e-4) input.Zones.Add(new ZoneSpan { X0 = x0, X1 = x1, Kind = z.Kind });
                open = double.NaN;
            }
        }
        foreach (var curb in c.Curbs)
        {
            // 연석 선의 꼭짓점이 단면 선 위에 오면 두 변이 같은 점을 한 번씩 낸다 → 같은 자리는 하나로
            var xs = new List<double>();
            for (int i = 0; i + 1 < curb.Count; i++)
                if (PlanLine.SegHit(a, b, curb[i], curb[i + 1], out double t)) xs.Add(t * 2 * half - half);
            xs.Sort();
            foreach (double x in xs) if (input.Curbs.Count == 0 || Math.Abs(x - input.Curbs[^1]) > 1e-4) input.Curbs.Add(x);
        }

        return input;
    }

    private static bool Inside(IReadOnlyList<P2> poly, P2 q)
    {
        bool inside = false;
        for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
        {
            if ((poly[i].Y > q.Y) != (poly[j].Y > q.Y) &&
                q.X < (poly[j].X - poly[i].X) * (q.Y - poly[i].Y) / (poly[j].Y - poly[i].Y) + poly[i].X) inside = !inside;
        }
        return inside;
    }

    /// <summary>한 측점의 단면. 지반 범위가 모자라면 폭을 넓혀 두 번 더 해 본다.</summary>
    public static SectionResult SectionAt(RouteContext c, double sta, int side)
    {
        SectionResult? last = null;
        foreach (double scale in new[] { 1.0, 2.0, 4.0 })
        {
            var input = InputAt(c, sta, side, out string why, scale);
            if (input == null)
            {
                if (last != null) return last;
                var bad = new SectionResult { Station = sta, Label = StationPlan.Name(sta, c.Route.Std.StationInterval), Method = c.Route.MethodAt(sta, side).Kind, Ok = false };
                bad.Messages.Add(why);
                return bad;
            }
            last = SectionBuilder.Build(input);
            if (last.Ok || !last.Messages.Contains("지반 범위 부족")) return last;
        }
        return last!;
    }

    /// <summary>측점마다 단면(경계는 전 · 후).</summary>
    /// <param name="extra">더 넣을 측점(3D 모형이 곡선을 따라가게 할 때 등 — 수량 계산에는 넣지 않는다).</param>
    public static List<StationRow> BuildRows(RouteContext c, double s0, double s1, IEnumerable<double>? extra = null)
    {
        var rows = new List<StationRow>();
        var stas = Stations(c, s0, s1, extra);
        for (int i = 0; i < stas.Count; i++)
        {
            double s = stas[i];
            var row = new StationRow { Sta = s, Name = StationPlan.Name(s, c.Route.Std.StationInterval) };
            bool first = i == 0, lastOne = i == stas.Count - 1;
            if (!first && !lastOne && IsBreak(c.Route, s))
            {
                row.Before = SectionAt(c, s, -1);
                row.After = SectionAt(c, s, +1);
                row.Before.Label = row.Name + "(전)";
                row.After.Label = row.Name + "(후)";
            }
            else
            {
                var sec = SectionAt(c, s, first ? +1 : lastOne ? -1 : 0);
                row.Before = sec;
                row.After = sec;
            }
            rows.Add(row);
        }
        return rows;
    }
}
