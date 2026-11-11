using System.Text.Encodings.Web;
using System.Text.Json;
using Box = DH.Grading.Core.GroundCrop.Box;
using Tri = DH.Grading.Core.WallDaylight.Tri;

namespace DH.Grading.Core;

/// <summary>★★[v117.1 · JACK 1111 «박스로 영역 정해서 · 레빗 애드인에서 3D로»] Civil → Revit 애드인 «3D 영역 파일» — 순수 함수(하네스 S247).
/// <list type="bullet">
/// <item>형식 «DH.CivilRegion3D» v1 — 머리는 점 파일과 같은 함수(<see cref="StructPoints.WriteHead"/>) · structure.name · pointsFile(점 파일 이름만 · 기준점 값은 안 넣음 — Revit 터미널 1111).</item>
/// <item>네모 = WCS 축 네모 · 1 mm로 먼저 반올림. 세모는 네모 경계에서 <b>정확히 잘라</b> 부채꼴로 다시 세모(파일의 box = 메시 테두리).</item>
/// <item>숫자 = m · 1 mm(정수 mm로 셈 · 같은 점 합침 키도 정수 mm) · 세모 감기 = 위에서 봐서 반시계 · 세모 번호 0부터 · 들여쓰기 없음 · 한글 그대로.</item>
/// </list></summary>
public static class StructRegion3D
{
    public const string Format = "DH.CivilRegion3D";
    public const int FormatVersion = 1;
    /// <summary>세모 한도 — 최종지표면을 넣을 때만(넘으면 짓지 않음 · 솎아내지 않음).</summary>
    public const int MaxTris = 300_000;
    /// <summary>네모 한 변 최소(m).</summary>
    public const double MinSide = 1.0;
    /// <summary>Z 없는 선을 메시에 입힐 때 점 간격(m).</summary>
    public const double DrapeStep = 1.0;
    /// <summary>[넣을 것] 열쇠 — final(최종지표면) · 선 종류 다섯.</summary>
    public static readonly string[] Keys = { "final", "wall", "bench", "slope", "site", "road" };

    static long K(double v) => (long)Math.Round(v * 1000.0);
    static double M(long k) => k / 1000.0;

    // ══════════════════════════════ 네모 ══════════════════════════════

    /// <summary>두 모서리(m · 순서 무관) → 1 mm로 먼저 반올림한 WCS 축 네모(작은 값이 X0 · Y0). 한 변이 <see cref="MinSide"/> 미만이거나 숫자가 아니면 거짓.</summary>
    public static bool TryBox(double ax, double ay, double bx, double by, out Box box)
    {
        box = Box.None;
        if (!(double.IsFinite(ax) && double.IsFinite(ay) && double.IsFinite(bx) && double.IsFinite(by))) return false;
        long x0 = Math.Min(K(ax), K(bx)), x1 = Math.Max(K(ax), K(bx)), y0 = Math.Min(K(ay), K(by)), y1 = Math.Max(K(ay), K(by));
        if (x1 - x0 < (long)(MinSide * 1000) || y1 - y0 < (long)(MinSide * 1000)) return false;
        box = new Box(M(x0), M(y0), M(x1), M(y1));
        return true;
    }

    // ══════════════════════════════ 세모 자르기 ══════════════════════════════

    /// <summary>자른 메시 — 점(정수 mm) · 세모(0부터 · 위에서 반시계) · 걸쳐 자른 원래 세모 수 · 뺀 납작 조각 수. 이름 · 레이어는 부르는 쪽이 채움.</summary>
    public sealed class Mesh
    {
        public readonly List<(long X, long Y, long Z)> V = new();
        public readonly List<(int A, int B, int C)> T = new();
        public int Cut, Flat;
        public string Name = "", Layer = "";
        public (double X, double Y, double Z) Vm(int i) { var v = V[i]; return (M(v.X), M(v.Y), M(v.Z)); }
    }

    /// <summary>세모 하나 ∩ 네모(XY) — 볼록 다각형(반올림 전 · Z = 같은 세모 평면 위). 밖이면 빈 목록. 경계 위 좌표는 네모 값 그대로.
    /// 모서리를 자르는 점은 끝점 차례를 정해 셈(이웃 세모가 같은 모서리를 거꾸로 돌아도 같은 비트 — 틈 없음).</summary>
    public static List<Point3> ClipTri(Tri t, Box b)
    {
        var poly = new List<Point3>(7) { t.A, t.B, t.C };
        var tmp = new List<Point3>(7);
        for (int plane = 0; plane < 4 && poly.Count > 0; plane++)
        {
            tmp.Clear();
            for (int i = 0; i < poly.Count; i++)
            {
                var p = poly[i]; var q = poly[(i + 1) % poly.Count];
                bool pin = In(p, b, plane), qin = In(q, b, plane);
                if (pin) tmp.Add(p);
                if (pin != qin) tmp.Add(Cut(p, q, b, plane));
            }
            (poly, tmp) = (tmp, poly);
        }
        // 꼭짓점이 경계 위면 자른 점이 그 점과 같다 — 이어진 같은 점은 하나로(부채꼴에 납작 세모가 덜 생기게)
        for (int i = poly.Count - 1; i > 0 && poly.Count > 1; i--) if (poly[i] == poly[i - 1]) poly.RemoveAt(i);
        if (poly.Count > 1 && poly[0] == poly[^1]) poly.RemoveAt(poly.Count - 1);
        return poly;
    }

    static bool In(Point3 p, Box b, int plane) => plane switch
    {
        0 => p.X >= b.X0, 1 => p.X <= b.X1, 2 => p.Y >= b.Y0, _ => p.Y <= b.Y1,
    };

    static Point3 Cut(Point3 p, Point3 q, Box b, int plane)
    {
        // 끝점 차례 고정(X · Y · Z 순) — 같은 모서리는 어느 세모에서 셈해도 같은 값
        if (q.X < p.X || (q.X == p.X && (q.Y < p.Y || (q.Y == p.Y && q.Z < p.Z)))) (p, q) = (q, p);
        if (plane < 2)
        {
            double v = plane == 0 ? b.X0 : b.X1, u = (v - p.X) / (q.X - p.X);
            return new Point3(v, p.Y + (q.Y - p.Y) * u, p.Z + (q.Z - p.Z) * u);
        }
        double w = plane == 2 ? b.Y0 : b.Y1, s = (w - p.Y) / (q.Y - p.Y);
        return new Point3(p.X + (q.X - p.X) * s, w, p.Z + (q.Z - p.Z) * s);
    }

    /// <summary>세모 하나의 내보낼 조각(정수 mm · 위에서 반시계) — 납작한 것(같은 점 둘 · 정수 mm 3D 외적 0)은 빼고 그 수를 <paramref name="flat"/>에 더함.
    /// 돌려줌 = 걸쳐 자른 세모인가.</summary>
    static bool Pieces(in Tri t, Box b, List<(long X, long Y, long Z)> outV, ref int flat)
    {
        outV.Clear();
        double minX = Math.Min(t.A.X, Math.Min(t.B.X, t.C.X)), maxX = Math.Max(t.A.X, Math.Max(t.B.X, t.C.X));
        double minY = Math.Min(t.A.Y, Math.Min(t.B.Y, t.C.Y)), maxY = Math.Max(t.A.Y, Math.Max(t.B.Y, t.C.Y));
        if (!(maxX >= b.X0 && minX <= b.X1 && maxY >= b.Y0 && minY <= b.Y1)) return false;   // 밖(숫자 아님도)
        bool inside = minX >= b.X0 && maxX <= b.X1 && minY >= b.Y0 && maxY <= b.Y1;
        if (inside) { Emit(Q(t.A, b), Q(t.B, b), Q(t.C, b), outV, ref flat); return false; }
        var poly = ClipTri(t, b);
        for (int i = 1; i + 1 < poly.Count; i++) Emit(Q(poly[0], b), Q(poly[i], b), Q(poly[i + 1], b), outV, ref flat);
        return true;
    }

    /// <summary>1 mm 반올림 + 네모 안으로 눌러 넣기(반올림이 경계를 넘지 않게 — 네모는 이미 mm).</summary>
    static (long X, long Y, long Z) Q(Point3 p, Box b) => (Math.Clamp(K(p.X), K(b.X0), K(b.X1)), Math.Clamp(K(p.Y), K(b.Y0), K(b.Y1)), K(p.Z));

    static void Emit((long X, long Y, long Z) a, (long X, long Y, long Z) c1, (long X, long Y, long Z) c2, List<(long X, long Y, long Z)> outV, ref int flat)
    {
        if (a == c1 || c1 == c2 || c2 == a) { flat++; return; }
        long ux = c1.X - a.X, uy = c1.Y - a.Y, uz = c1.Z - a.Z, vx = c2.X - a.X, vy = c2.Y - a.Y, vz = c2.Z - a.Z;
        long nx = uy * vz - uz * vy, ny = uz * vx - ux * vz, nz = ux * vy - uy * vx;
        if (nx == 0 && ny == 0 && nz == 0) { flat++; return; }
        if (nz < 0) (c1, c2) = (c2, c1);                       // 위에서 보아 반시계(수직 면 nz = 0은 그대로)
        outV.Add(a); outV.Add(c1); outV.Add(c2);
    }

    /// <summary>저장 없이 정확한 세모 수(<see cref="Clip"/>과 같은 자르기 · 같은 빼기 규칙) — 팝업 숫자 · 한도 판정.</summary>
    public static int CountTris(IEnumerable<Tri> tris, Box b)
    {
        var buf = new List<(long X, long Y, long Z)>(24);
        int n = 0, flat = 0;
        foreach (var t in tris) { Pieces(t, b, buf, ref flat); n += buf.Count / 3; }
        return n;
    }

    /// <summary>네모 안 메시 — 세모 상자가 밖이면 바로 버림 · 안이면 그대로 · 걸친 것만 자름 · 점 합침(정수 mm 키) · 세모 0부터.
    /// 세모 수가 <paramref name="max"/>를 넘으면 짓지 않고 null(<paramref name="count"/> = 정확한 수).</summary>
    public static Mesh? Clip(IEnumerable<Tri> tris, Box b, out int count, int max = int.MaxValue)
    {
        var list = tris as IReadOnlyCollection<Tri> ?? tris.ToList();
        count = CountTris(list, b);
        if (count > max) return null;
        var m = new Mesh();
        var weld = new Dictionary<(long, long, long), int>();
        var buf = new List<(long X, long Y, long Z)>(24);
        int Idx((long X, long Y, long Z) k) { if (!weld.TryGetValue(k, out int i)) { i = m.V.Count; weld[k] = i; m.V.Add(k); } return i; }
        foreach (var t in list)
        {
            if (Pieces(t, b, buf, ref m.Flat)) m.Cut++;
            for (int i = 0; i + 2 < buf.Count; i += 3) m.T.Add((Idx(buf[i]), Idx(buf[i + 1]), Idx(buf[i + 2])));
        }
        return m;
    }

    // ══════════════════════════════ 선 ══════════════════════════════

    /// <summary>3D 선 하나 — 종류(wall · bench · slope · site · road) · 원래 레이어 · 닫힘 · 점(m · WCS).</summary>
    public sealed record Line3(string Kind, string Layer, bool Closed, IReadOnlyList<Point3> Pts);

    /// <summary>선 ∩ 네모(XY) — 토막마다 안 부분만(Z는 토막 따라 직선) · 들락날락하면 여러 조각 · 닫힌 선이 통째로 안이면 닫힘 · 잘리면 열린 조각(시작점 이음매에서 갈린 두 조각은 하나로).
    /// 경계 위 좌표는 네모 값 그대로.</summary>
    public static List<(List<Point3> Pts, bool Closed)> ClipLine(IReadOnlyList<Point3> pts, bool closed, Box b)
    {
        var res = new List<(List<Point3> Pts, bool Closed)>();
        var v = pts.Where(p => double.IsFinite(p.X) && double.IsFinite(p.Y)).ToList();
        if (closed && v.Count > 2 && v[0].X == v[^1].X && v[0].Y == v[^1].Y) v.RemoveAt(v.Count - 1);
        if (v.Count < 2) return res;
        int nSeg = closed ? v.Count : v.Count - 1;
        List<Point3>? cur = null;
        bool firstAtStart = false, lastAtEnd = false, all = true;
        for (int i = 0; i < nSeg; i++)
        {
            var p = v[i]; var q = v[(i + 1) % v.Count];
            if (!Seg(p, q, b, out double t0, out double t1, out int e0, out int e1)) { all = false; if (cur != null) { res.Add((cur, false)); cur = null; } continue; }
            if (t0 > 0 || t1 < 1) all = false;
            var a = t0 <= 0 ? p : Lerp(p, q, t0, b, e0); var c = t1 >= 1 ? q : Lerp(p, q, t1, b, e1);
            if (cur != null && t0 > 0) { res.Add((cur, false)); cur = null; }
            if (cur == null) { cur = new List<Point3> { a }; if (i == 0 && t0 <= 0) firstAtStart = true; }
            cur.Add(c);
            if (t1 < 1) { res.Add((cur, false)); cur = null; }
            else if (i == nSeg - 1) lastAtEnd = true;
        }
        if (cur != null) res.Add((cur, false));
        if (closed && all && res.Count == 1)
        {
            var only = res[0].Pts;
            if (only.Count > 1 && only[0] == only[^1]) only.RemoveAt(only.Count - 1);
            res[0] = (only, true);
        }
        else if (closed && firstAtStart && lastAtEnd && res.Count > 1)
        {
            // 이음매(시작점)에서 갈린 끝 조각 + 첫 조각 = 한 조각
            var head = res[0].Pts; var tail = res[^1].Pts;
            tail.AddRange(head.Skip(1));
            res[^1] = (tail, false); res.RemoveAt(0);
        }
        res.RemoveAll(r => r.Pts.Count < 2);
        return res;
    }

    /// <summary>토막 위 점 — 자른 경계(<paramref name="plane"/> 0 x=X0 · 1 x=X1 · 2 y=Y0 · 3 y=Y1)의 좌표는 네모 값 그대로 · 다른 좌표는 네모 안으로.</summary>
    static Point3 Lerp(Point3 p, Point3 q, double t, Box b, int plane)
    {
        double x = Math.Clamp(p.X + (q.X - p.X) * t, b.X0, b.X1), y = Math.Clamp(p.Y + (q.Y - p.Y) * t, b.Y0, b.Y1);
        if (plane == 0) x = b.X0; else if (plane == 1) x = b.X1; else if (plane == 2) y = b.Y0; else if (plane == 3) y = b.Y1;
        return new Point3(x, y, p.Z + (q.Z - p.Z) * t);
    }

    /// <summary>Liang–Barsky — 토막의 네모 안 매개변수 [t0, t1](닿기만 해도 참) · 그 끝을 정한 경계 번호(-1 = 안 자름).</summary>
    static bool Seg(Point3 p, Point3 q, Box b, out double t0, out double t1, out int e0, out int e1)
    {
        t0 = 0; t1 = 1; e0 = -1; e1 = -1;
        double dx = q.X - p.X, dy = q.Y - p.Y;
        double[] pp = { -dx, dx, -dy, dy }, qq = { p.X - b.X0, b.X1 - p.X, p.Y - b.Y0, b.Y1 - p.Y };
        for (int i = 0; i < 4; i++)
        {
            if (pp[i] == 0) { if (qq[i] < 0) return false; continue; }
            double r = qq[i] / pp[i];
            if (pp[i] < 0) { if (r > t1) return false; if (r > t0) { t0 = r; e0 = i; } }
            else { if (r < t0) return false; if (r < t1) { t1 = r; e1 = i; } }
        }
        return t1 >= t0;
    }

    /// <summary>점 간격이 <paramref name="step"/>(m)를 넘지 않게 채움(원래 꼭짓점 그대로 · 닫힌 선이면 닫는 토막도).</summary>
    public static List<Point3> Densify(IReadOnlyList<Point3> pts, bool closed, double step = DrapeStep)
    {
        var r = new List<Point3>();
        int n = pts.Count; if (n == 0) return r;
        int nSeg = closed ? n : n - 1;
        r.Add(pts[0]);
        for (int i = 0; i < nSeg; i++)
        {
            var p = pts[i]; var q = pts[(i + 1) % n];
            double len = Math.Sqrt((q.X - p.X) * (q.X - p.X) + (q.Y - p.Y) * (q.Y - p.Y));
            int k = Math.Max(1, (int)Math.Ceiling(len / step - 1e-9));
            for (int j = 1; j < k; j++) { double u = (double)j / k; r.Add(new Point3(p.X + (q.X - p.X) * u, p.Y + (q.Y - p.Y) * u, p.Z + (q.Z - p.Z) * u)); }
            if (!closed || i < nSeg - 1) r.Add(q);
        }
        return r;
    }

    /// <summary>메시 높이 찾기(칸 색인) — 내보내는 메시(자른 세모) 위의 Z. 위에서 본 넓이가 0인 세모(수직 면)는 안 씀.</summary>
    public sealed class MeshZ
    {
        readonly (double X, double Y, double Z)[] _v;
        readonly List<(int A, int B, int C)> _t;
        readonly Dictionary<(int, int), List<int>> _cells = new();
        readonly double _x0, _y0, _cs;

        public MeshZ(Mesh m)
        {
            _v = new (double, double, double)[m.V.Count];
            for (int i = 0; i < _v.Length; i++) _v[i] = m.Vm(i);
            _t = m.T;
            double x0 = double.MaxValue, y0 = double.MaxValue, x1 = double.MinValue, y1 = double.MinValue;
            foreach (var p in _v) { x0 = Math.Min(x0, p.X); y0 = Math.Min(y0, p.Y); x1 = Math.Max(x1, p.X); y1 = Math.Max(y1, p.Y); }
            _x0 = x0; _y0 = y0;
            _cs = _t.Count == 0 ? 1 : Math.Max(1.0, Math.Sqrt(Math.Max(1e-6, (x1 - x0) * (y1 - y0)) / _t.Count) * 2);
            for (int i = 0; i < _t.Count; i++)
            {
                var (a, b, c) = (_v[_t[i].A], _v[_t[i].B], _v[_t[i].C]);
                int cx0 = Cell(Math.Min(a.X, Math.Min(b.X, c.X)) - _x0), cx1 = Cell(Math.Max(a.X, Math.Max(b.X, c.X)) - _x0);
                int cy0 = Cell(Math.Min(a.Y, Math.Min(b.Y, c.Y)) - _y0), cy1 = Cell(Math.Max(a.Y, Math.Max(b.Y, c.Y)) - _y0);
                for (int gx = cx0; gx <= cx1; gx++)
                    for (int gy = cy0; gy <= cy1; gy++)
                    {
                        if (!_cells.TryGetValue((gx, gy), out var l)) _cells[(gx, gy)] = l = new List<int>();
                        l.Add(i);
                    }
            }
        }

        int Cell(double d) => (int)Math.Floor(d / _cs);

        /// <summary>(x, y)의 메시 높이 — 메시 밖이면 거짓(모서리 위 = 안).</summary>
        public bool TryZ(double x, double y, out double z)
        {
            z = double.NaN;
            if (_t.Count == 0) return false;
            if (!_cells.TryGetValue((Cell(x - _x0), Cell(y - _y0)), out var l)) return false;
            foreach (int i in l)
            {
                var (a, b, c) = (_v[_t[i].A], _v[_t[i].B], _v[_t[i].C]);
                double d = (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
                if (Math.Abs(d) < 1e-12) continue;
                double l1 = ((b.X - x) * (c.Y - y) - (b.Y - y) * (c.X - x)) / d;
                double l2 = ((c.X - x) * (a.Y - y) - (c.Y - y) * (a.X - x)) / d;
                double l3 = 1 - l1 - l2;
                const double e = -1e-9;
                if (l1 < e || l2 < e || l3 < e) continue;
                z = l1 * a.Z + l2 * b.Z + l3 * c.Z;
                return true;
            }
            return false;
        }
    }

    /// <summary>Z 없는 선(진입도로 윤곽 · 2D 계획 경계)에 높이 입히기 — 차례 «2D 자르기(부르는 쪽) → <see cref="DrapeStep"/> 채우기 → 입히기».
    /// 메시 밖 점에서 선을 끊는다(2점 미만 조각은 버림). 통째로 다 입혀진 닫힌 선은 닫힘 유지.</summary>
    public static List<(List<Point3> Pts, bool Closed)> Drape(IReadOnlyList<Point3> pts, bool closed, MeshZ mz, double step = DrapeStep)
    {
        var res = new List<(List<Point3> Pts, bool Closed)>();
        var d = Densify(pts, closed, step);
        List<Point3>? cur = null; bool all = true;
        foreach (var p in d)
        {
            if (mz.TryZ(p.X, p.Y, out double z)) { (cur ??= new List<Point3>()).Add(new Point3(p.X, p.Y, z)); continue; }
            all = false;
            if (cur != null) { res.Add((cur, false)); cur = null; }
        }
        if (cur != null) res.Add((cur, false));
        if (closed && all && res.Count == 1) res[0] = (res[0].Pts, true);
        res.RemoveAll(r => r.Pts.Count < 2);
        return res;
    }

    /// <summary>쓰기 전 다듬기 — 1 mm 반올림 · 이어진 같은 점 · 닫힌 선의 끝 = 첫 점 빼기 · 2점 미만 · 숫자 아닌 점의 선 버림. 쓰는 선 수 = 이 결과 수.</summary>
    public static List<Line3> Tidy(IEnumerable<Line3> lines)
    {
        var r = new List<Line3>();
        foreach (var l in lines)
        {
            if (l.Pts.Any(p => !double.IsFinite(p.X) || !double.IsFinite(p.Y) || !double.IsFinite(p.Z))) continue;
            var q = new List<Point3>(l.Pts.Count);
            foreach (var p in l.Pts)
            {
                var m = new Point3(M(K(p.X)), M(K(p.Y)), M(K(p.Z)));
                if (q.Count == 0 || q[^1] != m) q.Add(m);
            }
            if (l.Closed && q.Count > 2 && q[0] == q[^1]) q.RemoveAt(q.Count - 1);
            if (q.Count < 2 || (l.Closed && q.Count < 3)) { if (q.Count >= 2) r.Add(l with { Closed = false, Pts = q }); continue; }
            r.Add(l with { Pts = q });
        }
        return r;
    }

    // ══════════════════════════════ [내보내기] 켜짐 ══════════════════════════════

    /// <summary>[내보내기]가 켜지는가 — 빈 글 = 됨 · 아니면 까닭(팝업 상태 줄). <paramref name="chosen"/> = 고른 것들의 개수 합(최종지표면을 골랐으면 세모 수 포함).
    /// 한도는 최종지표면을 골랐을 때만(계획 9절 M8). <paramref name="max"/> = 세모 한도(시험 고리만 낮춤).</summary>
    public static string WhyNot(bool hasName, bool hasPts, bool hasPath, bool hasBox, bool surfOn, int tris, int chosen, int max = MaxTris)
    {
        if (!hasName) return "이름을 넣으세요";
        if (!hasPts) return "기준점 두 점을 찍으세요";
        if (!hasPath) return "저장할 파일을 정하세요";
        if (!hasBox) return "";
        if (surfOn && tris > max) return string.Format(System.Globalization.CultureInfo.InvariantCulture, "세모 {0:N0} — 네모를 줄이세요", tris);
        if (chosen <= 0) return "네모 안에 내보낼 것이 없음";
        return "";
    }

    /// <summary>화면 없는 Civil «넣을 것» 글 → 열쇠 모음. 빈 글 · «전부» · «A» = 전부 · 글자 F(최종지표면) W(옹벽선) B(소단선) S(사면선) L(정지 경계) R(진입도로 윤곽) 조합. 모르는 글자면 거짓.</summary>
    public static bool ParseInclude(string? s, out HashSet<string> keys)
    {
        keys = new HashSet<string>();
        string t = (s ?? "").Trim().ToUpperInvariant();
        if (t.Length == 0 || t == "전부" || t == "A") { keys.UnionWith(Keys); return true; }
        foreach (char ch in t)
        {
            string? k = ch switch { 'F' => "final", 'W' => "wall", 'B' => "bench", 'S' => "slope", 'L' => "site", 'R' => "road", ' ' or ',' => "", _ => null };
            if (k == null) return false;
            if (k.Length > 0) keys.Add(k);
        }
        return keys.Count > 0;
    }

    /// <summary>네모가 있을 때 점 파일 «sketch» — 가운데 = 네모 가운데 · 반지름 = 대각선 절반(v1.1.0 호환) · box = 네모 · 선은 부르는 쪽이 네모로 자른 것.</summary>
    public static StructPoints.Sketch SketchOf(Box b, IReadOnlyList<StructPoints.SketchLine> lines)
        => new((b.X0 + b.X1) / 2, (b.Y0 + b.Y1) / 2, Math.Sqrt(b.W * b.W + b.H * b.H) / 2, lines, b);

    /// <summary>점 파일 이름 → 3D 영역 파일 이름(«_Civil기준점» → «_3D영역» · 없으면 이름 뒤에 «_3D영역»).</summary>
    public static string FileFor(string pointsFile)
    {
        string name = Path.GetFileNameWithoutExtension(pointsFile ?? "");
        const string tag = "_Civil기준점";
        string stem = name.EndsWith(tag, StringComparison.Ordinal) ? name[..^tag.Length] : name;
        return (stem.Length == 0 ? "구조물" : stem) + "_3D영역.json";
    }

    // ══════════════════════════════ 되읽기 ══════════════════════════════

    /// <summary>두 파일이 값으로 같아야 하는 머리 칸.</summary>
    public static readonly string[] HeadKeys = { "units", "axes", "crs", "drawing", "app", "created" };

    /// <summary>JSON 값 같음(들여쓰기 · 숫자 꼴과 무관 — 계획 9절 L6).</summary>
    public static bool SameValue(JsonElement a, JsonElement b)
    {
        if (a.ValueKind != b.ValueKind) return false;
        switch (a.ValueKind)
        {
            case JsonValueKind.Object:
                var pa = a.EnumerateObject().ToList(); var pb = b.EnumerateObject().ToList();
                return pa.Count == pb.Count && pa.Zip(pb).All(z => z.First.Name == z.Second.Name && SameValue(z.First.Value, z.Second.Value));
            case JsonValueKind.Array:
                return a.GetArrayLength() == b.GetArrayLength() && a.EnumerateArray().Zip(b.EnumerateArray()).All(z => SameValue(z.First, z.Second));
            case JsonValueKind.Number: return a.GetDouble() == b.GetDouble();
            case JsonValueKind.String: return a.GetString() == b.GetString();
            default: return true;
        }
    }

    // ══════════════════════════════ 파일 쓰기 ══════════════════════════════

    /// <summary>3D 영역 파일을 스트림으로 바로 쓴다(큰 글 문자열 안 만듦 · 1 MB마다 내보냄) — 들여쓰기 없음 · 한글 그대로.
    /// <paramref name="mesh"/>가 null이면 «meshes»: []. 선은 <see cref="Tidy"/>를 거친 것을 그대로 쓴다.</summary>
    public static void Write(Stream s, StructPoints.Info i, string pointsFile, Box box, Mesh? mesh, IReadOnlyList<Line3> lines)
    {
        using var w = new Utf8JsonWriter(s, new JsonWriterOptions { Indented = false, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        void Spill() { if (w.BytesPending > (1 << 20)) w.Flush(); }
        w.WriteStartObject();
        w.WriteString("format", Format);
        w.WriteNumber("version", FormatVersion);
        w.WriteStartObject("structure"); w.WriteString("name", i.Name ?? ""); w.WriteEndObject();
        w.WriteString("pointsFile", pointsFile ?? "");
        StructPoints.WriteHead(w, i);
        StructPoints.WriteBox(w, "box", box);
        w.WriteStartArray("meshes");
        if (mesh != null && mesh.T.Count > 0)
        {
            w.WriteStartObject();
            w.WriteString("kind", "final"); w.WriteString("name", mesh.Name ?? ""); w.WriteString("layer", mesh.Layer ?? "");
            w.WriteStartArray("verts");
            foreach (var v in mesh.V) { w.WriteStartArray(); w.WriteNumberValue(M(v.X)); w.WriteNumberValue(M(v.Y)); w.WriteNumberValue(M(v.Z)); w.WriteEndArray(); Spill(); }
            w.WriteEndArray();
            w.WriteStartArray("tris");
            foreach (var t in mesh.T) { w.WriteStartArray(); w.WriteNumberValue(t.A); w.WriteNumberValue(t.B); w.WriteNumberValue(t.C); w.WriteEndArray(); Spill(); }
            w.WriteEndArray();
            w.WriteEndObject();
        }
        w.WriteEndArray();
        w.WriteStartArray("lines");
        foreach (var l in lines)
        {
            w.WriteStartObject();
            w.WriteString("kind", l.Kind); w.WriteString("layer", l.Layer ?? ""); w.WriteBoolean("closed", l.Closed);
            w.WriteStartArray("pts");
            foreach (var p in l.Pts) { w.WriteStartArray(); w.WriteNumberValue(M(K(p.X))); w.WriteNumberValue(M(K(p.Y))); w.WriteNumberValue(M(K(p.Z))); w.WriteEndArray(); }
            w.WriteEndArray();
            w.WriteEndObject();
            Spill();
        }
        w.WriteEndArray();
        w.WriteEndObject();
        w.Flush();
    }
}
