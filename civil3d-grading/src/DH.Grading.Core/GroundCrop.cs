using System.Globalization;
using Tri = DH.Grading.Core.WallDaylight.Tri;

namespace DH.Grading.Core;

/// <summary>★★★[v111.9 · 계획_원지형자르기.md Step 1 · JACK 1103 «어느 단계에서나 · 모든 지표면»] <b>원지형 자르기 — 순수 셈</b>(Civil 없이 시험 · 하네스 S188 CR).
/// <list type="bullet">
/// <item><b>네 띠</b>(<see cref="Frame"/>) — 상자 밖 네 직사각형 Hide 경계. Outer는 숨은 세모를 드러내고(V84 4,908 → 59,828) 이미 Outer가 든 면에 또 걸면 면이 부서진다(0916) — 띠는 숨기기만 한다.</item>
/// <item><b>우리 띠 알아보기</b>(<see cref="OurBoundaries"/>) — 경계 이름을 쓰면 자르기가 안 먹는다(V84) → 기록(넣은 띠 꼭짓점) · 기록이 없으면 «L R B T 네 띠» 꼴로만. 사람 경계는 건드리지 않는다.</item>
/// <item><b>설계 자리 검사</b>(<see cref="Covers"/>) — 상자가 축 정렬 네모라 «상자 ⊇ D + 여유» = 모든 항목 꼭짓점이 «상자 − 여유» 안(정확 · 근사 없음).</item>
/// <item><b>안쪽 지문</b>(<see cref="InsideHash"/>) — 상자 − 여유 안에 세 꼭짓점이 다 든 세모 · 차례와 무관 · 비트 그대로.</item>
/// </list></summary>
public static class GroundCrop
{
    /// <summary>띠가 면의 보이는 범위 밖으로 나가는 길이(m) — 면마다 그 면 범위 + 이 값(나중에 면이 커져도 다시 자를 때 새로 셈).</summary>
    public const double Pad = 100.0;
    /// <summary>설계 자리 여유 · 안쪽 지문 여유(m) — 띠가 가르는 세모가 설계 자리에 닿지 않게(V84/V86 2 m 안쪽부터 비트 같음) · 화면에서 «딱 붙은» 상자 막기(내 판단 · 계획 11장).</summary>
    public const double Margin = 5.0;
    /// <summary>경계 꼭짓점 되읽기 비교(m) — 같은 띠인지 알아보는 데만 쓴다(자르기 결과에는 안 씀).</summary>
    public const double VertexTol = 1e-6;

    /// <summary>축 정렬 네모(WCS). 빈 상자 = <see cref="None"/>(X0 &gt; X1).</summary>
    public readonly record struct Box(double X0, double Y0, double X1, double Y1)
    {
        public static readonly Box None = new(double.PositiveInfinity, double.PositiveInfinity, double.NegativeInfinity, double.NegativeInfinity);
        public bool IsNone => !(X0 <= X1 && Y0 <= Y1);
        public double W => X1 - X0;
        public double H => Y1 - Y0;
        public Box Grow(double d) => IsNone ? this : new Box(X0 - d, Y0 - d, X1 + d, Y1 + d);
        public Box Union(Box o) => o.IsNone ? this : IsNone ? o : new Box(Math.Min(X0, o.X0), Math.Min(Y0, o.Y0), Math.Max(X1, o.X1), Math.Max(Y1, o.Y1));
        /// <summary>겹친 네모(안 겹치면 없음).</summary>
        public Box Intersect(Box o) { if (IsNone || o.IsNone) return None; var r = new Box(Math.Max(X0, o.X0), Math.Max(Y0, o.Y0), Math.Min(X1, o.X1), Math.Min(Y1, o.Y1)); return r.IsNone ? None : r; }
        public Box Add(double x, double y) => IsNone ? new Box(x, y, x, y) : new Box(Math.Min(X0, x), Math.Min(Y0, y), Math.Max(X1, x), Math.Max(Y1, y));
        /// <summary>닫힌 품기(경계 위 = 안).</summary>
        public bool Contains(Box o) => o.IsNone || (!IsNone && o.X0 >= X0 && o.X1 <= X1 && o.Y0 >= Y0 && o.Y1 <= Y1);
        public bool Contains(double x, double y) => !IsNone && x >= X0 && x <= X1 && y >= Y0 && y <= Y1;
        public static Box Of(IEnumerable<(double X, double Y)> pts) { var b = None; foreach (var (x, y) in pts) b = b.Add(x, y); return b; }
        public string Text => IsNone ? "없음" : string.Format(CultureInfo.InvariantCulture, "{0:F1},{1:F1}~{2:F1},{3:F1}({4:F0}×{5:F0} m)", X0, Y0, X1, Y1, W, H);
    }

    /// <summary>세모 꼭짓점 범위.</summary>
    public static Box ExtentOf(IEnumerable<Tri> tris)
    {
        var b = Box.None;
        foreach (var t in tris) b = b.Add(t.A.X, t.A.Y).Add(t.B.X, t.B.Y).Add(t.C.X, t.C.Y);
        return b;
    }

    // ══════════════════════════════ 네 띠 ══════════════════════════════

    /// <summary>상자 밖 네 띠 — 차례 왼쪽 · 오른쪽 · 아래 · 위(넓이 0인 띠는 뺌). E = <paramref name="surfExt"/> + <paramref name="pad"/>.
    /// 왼쪽 [E.X0, x0]×[E.Y0, E.Y1] · 오른쪽 [x1, E.X1]×[E.Y0, E.Y1] · 아래 [x0, x1]×[E.Y0, y0] · 위 [x0, x1]×[y1, E.Y1] — 서로 겹치지 않고 상자와 틈 없이 맞닿는다(합집합 = E − 열린 상자).</summary>
    public static List<Box> Frame(Box box, Box surfExt, double pad = Pad) => FrameIn(box, surfExt.Grow(pad));

    /// <summary><see cref="Frame"/>의 E를 그대로 받는 꼴(알아보기가 씀).</summary>
    public static List<Box> FrameIn(Box box, Box e)
    {
        var res = new List<Box>(4);
        void Add(double a, double b, double c, double d) { if (c > a && d > b) res.Add(new Box(a, b, c, d)); }
        if (box.IsNone || e.IsNone) return res;
        Add(e.X0, e.Y0, box.X0, e.Y1);
        Add(box.X1, e.Y0, e.X1, e.Y1);
        Add(box.X0, e.Y0, box.X1, box.Y0);
        Add(box.X0, box.Y1, box.X1, e.Y1);
        return res;
    }

    /// <summary>자를 면인가 — 보이는 세모 범위가 상자를 한 쪽이라도 넘는다(경계 위는 안).</summary>
    public static bool NeedsCrop(Box visibleExt, Box box)
        => !visibleExt.IsNone && (visibleExt.X0 < box.X0 || visibleExt.X1 > box.X1 || visibleExt.Y0 < box.Y0 || visibleExt.Y1 > box.Y1);

    // ══════════════════════════════ 우리 띠 알아보기 ══════════════════════════════

    /// <summary>면 경계 한 줄(정의 차례대로) — 종류 · 꼭짓점(되읽은 그대로).</summary>
    public sealed record Bnd(string Type, IReadOnlyList<(double X, double Y)> V)
    {
        public bool IsHide => string.Equals(Type, "Hide", StringComparison.OrdinalIgnoreCase);
        public bool IsOuter => string.Equals(Type, "Outer", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>꼭짓점이 축 정렬 네모 하나인가(모든 점이 네모 둘레 위 · 네 모서리가 다 있음 · 닫는 점 · 둘레 위 덧점은 받음) — 그 네모.</summary>
    public static bool IsRect(IReadOnlyList<(double X, double Y)> v, out Box r, double tol = VertexTol)
    {
        r = Box.Of(v);
        if (v.Count < 4 || r.IsNone || !(r.W > tol) || !(r.H > tol)) return false;
        var rr = r;
        bool OnEdge((double X, double Y) p) => (Math.Abs(p.X - rr.X0) <= tol || Math.Abs(p.X - rr.X1) <= tol) && p.Y >= rr.Y0 - tol && p.Y <= rr.Y1 + tol
                                             || (Math.Abs(p.Y - rr.Y0) <= tol || Math.Abs(p.Y - rr.Y1) <= tol) && p.X >= rr.X0 - tol && p.X <= rr.X1 + tol;
        if (!v.All(OnEdge)) return false;
        bool Has(double x, double y) => v.Any(p => Math.Abs(p.X - x) <= tol && Math.Abs(p.Y - y) <= tol);
        return Has(r.X0, r.Y0) && Has(r.X1, r.Y0) && Has(r.X1, r.Y1) && Has(r.X0, r.Y1);
    }

    static bool Same(Box a, Box b, double tol) => Math.Abs(a.X0 - b.X0) <= tol && Math.Abs(a.Y0 - b.Y0) <= tol && Math.Abs(a.X1 - b.X1) <= tol && Math.Abs(a.Y1 - b.Y1) <= tol;

    /// <summary>알아본 까닭.</summary>
    public enum Why { Record, Shape, OldOuter }

    /// <summary>우리가 넣은 경계 색인(차례대로) · 까닭.
    /// <list type="number">
    /// <item><b>기록</b>(<paramref name="recorded"/> — 지난 자르기가 이 면에 넣은 띠): Hide 네모가 기록 띠 하나와 같으면 우리 것.</item>
    /// <item><b>꼴</b>(기록이 없을 때만): Hide 네모 <b>넷이 잇달아</b> 어떤 상자의 L R B T 네 띠(<see cref="FrameIn"/>)와 같으면 우리 것 — 사람이 만든 Hide 하나 · 둘은 안 건드림.</item>
    /// <item><b>옛 DHCROP Outer</b>(<paramref name="oldOuterOk"/> — 원지반만): 꼭짓점이 축 정렬 네모 하나인 Outer(v111.8까지 DHCROP가 «경계 모두 지우고 Outer 하나»).</item>
    /// </list></summary>
    public static List<(int Index, Why Why)> OurBoundaries(IReadOnlyList<Bnd> bnds, IReadOnlyList<Box>? recorded, bool oldOuterOk, double tol = VertexTol)
    {
        var res = new List<(int, Why)>();
        var rect = new Box?[bnds.Count];
        for (int i = 0; i < bnds.Count; i++) rect[i] = IsRect(bnds[i].V, out var r, tol) ? r : null;
        var taken = new bool[bnds.Count];
        if (recorded != null && recorded.Count > 0)
        {
            for (int i = 0; i < bnds.Count; i++)
                if (bnds[i].IsHide && rect[i] is Box r && recorded.Any(q => Same(q, r, tol))) { res.Add((i, Why.Record)); taken[i] = true; }
        }
        else
        {
            for (int i = 0; i + 3 < bnds.Count; i++)
            {
                if (taken[i]) continue;
                bool run = true;
                for (int k = 0; k < 4; k++) if (!bnds[i + k].IsHide || rect[i + k] == null) { run = false; break; }
                if (!run) continue;
                var L = rect[i]!.Value; var R = rect[i + 1]!.Value; var B = rect[i + 2]!.Value; var T = rect[i + 3]!.Value;
                var e = L.Union(R).Union(B).Union(T);
                var box = new Box(L.X1, B.Y1, R.X0, T.Y0);
                var f = FrameIn(box, e);
                if (f.Count == 4 && Same(f[0], L, tol) && Same(f[1], R, tol) && Same(f[2], B, tol) && Same(f[3], T, tol))
                {
                    for (int k = 0; k < 4; k++) { res.Add((i + k, Why.Shape)); taken[i + k] = true; }
                    i += 3;
                }
            }
        }
        if (oldOuterOk)
            for (int i = 0; i < bnds.Count; i++)
                if (!taken[i] && bnds[i].IsOuter && rect[i] != null) { res.Add((i, Why.OldOuter)); taken[i] = true; }
        res.Sort((a, b) => a.Item1.CompareTo(b.Item1));
        return res;
    }

    // ══════════════════════════════ 설계 자리 ══════════════════════════════

    /// <summary>설계 자리 항목 하나 — 이름표(«계획부지 1» · «진입도로 DH도로_1» · «횡단 표본선 …») · 꺾은선(닫힘이면 첫 점으로 돌아옴).</summary>
    public sealed record Item(string Label, IReadOnlyList<(double X, double Y)> Pts, bool Closed);

    /// <summary>설계 자리 상자 D = 모든 항목 꼭짓점 범위(빈 목록 = 없음).</summary>
    public static Box DesignBox(IEnumerable<Item> items)
    {
        var b = Box.None;
        foreach (var it in items) foreach (var (x, y) in it.Pts) b = b.Add(x, y);
        return b;
    }

    /// <summary>상자 밖으로 나간 항목 하나 — 나간 길이(m) · 나간 조각(빨간 실선) · 가장 멀리 나간 거리(m).</summary>
    public sealed record Out(string Label, double Length, double Far, List<List<(double X, double Y)>> Parts);

    /// <summary>검사 결과 — 통과 · 필요한 가장 작은 상자(D + 여유) · 나간 항목.</summary>
    public sealed record Cover(bool Ok, Box Need, List<Out> Outs);

    /// <summary>★[계획 4장] <b>«상자 ⊇ D + 여유»</b> — 축 정렬 네모라 «모든 꼭짓점이 상자 − 여유 안»과 같다(볼록이라 꼭짓점이 안이면 선분도 안 · 근사 없음).
    /// 나간 항목마다 «상자 − 여유» 밖 조각과 그 길이(점 하나뿐인 항목은 길이 0 · 나간 거리로 보임). 항목 없음 = 통과.</summary>
    public static Cover Covers(Box box, IReadOnlyList<Item> items, double margin = Margin)
    {
        var need = DesignBox(items).Grow(margin);
        var inner = box.Grow(-margin);
        var outs = new List<Out>();
        foreach (var it in items)
        {
            if (it.Pts.Count == 0) continue;
            double far = 0;
            foreach (var (x, y) in it.Pts)
            {
                double d = inner.IsNone ? double.PositiveInfinity : Math.Max(Math.Max(inner.X0 - x, x - inner.X1), Math.Max(inner.Y0 - y, y - inner.Y1));
                if (d > far) far = d;
            }
            if (!(far > 0)) continue;
            var parts = OutsideParts(it.Pts, it.Closed, inner);
            double len = parts.Sum(Len);
            outs.Add(new Out(it.Label, len, far, parts));
        }
        bool ok = outs.Count == 0 && box.Contains(need);
        return new Cover(ok, need, outs);
    }

    /// <summary>꺾은선 길이.</summary>
    public static double Len(IReadOnlyList<(double X, double Y)> p)
    {
        double s = 0;
        for (int i = 0; i + 1 < p.Count; i++) s += Math.Sqrt((p[i + 1].X - p[i].X) * (p[i + 1].X - p[i].X) + (p[i + 1].Y - p[i].Y) * (p[i + 1].Y - p[i].Y));
        return s;
    }

    /// <summary>★[계획 4장 ②] 꺾은선에서 네모 <paramref name="inner"/> <b>밖</b> 조각(빨간 실선) — 선분마다 Liang–Barsky로 안 구간 [t0, t1]을 구해 그 앞 · 뒤를 밖으로(이어지는 조각은 하나로).
    /// 빈 네모(여유가 상자보다 큼)면 통째로 밖.</summary>
    public static List<List<(double X, double Y)>> OutsideParts(IReadOnlyList<(double X, double Y)> pts, bool closed, Box inner)
    {
        var res = new List<List<(double X, double Y)>>();
        var v = pts.ToList();
        if (closed && v.Count > 2 && (v[0].X != v[^1].X || v[0].Y != v[^1].Y)) v.Add(v[0]);
        if (v.Count == 1) { if (!inner.Contains(v[0].X, v[0].Y)) res.Add(new() { v[0] }); return res; }
        List<(double X, double Y)>? cur = null;
        void Seg((double X, double Y) a, (double X, double Y) b)
        {
            if (a.X == b.X && a.Y == b.Y) return;
            if (cur != null && (cur[^1].X != a.X || cur[^1].Y != a.Y)) { res.Add(cur); cur = null; }
            if (cur == null) cur = new() { a };
            cur.Add(b);
        }
        void Flush() { if (cur != null) { res.Add(cur); cur = null; } }
        for (int i = 0; i + 1 < v.Count; i++)
        {
            var a = v[i]; var b = v[i + 1];
            if (!Clip(a, b, inner, out double t0, out double t1)) { Seg(a, b); continue; }
            (double X, double Y) P(double t) => t <= 0 ? a : t >= 1 ? b : (a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);
            if (t0 > 0) Seg(a, P(t0));
            if (t1 < 1) { Flush(); Seg(P(t1), b); }
            else Flush();
        }
        Flush();
        return res;
    }

    /// <summary>선분 a→b가 네모 안에 드는 매개 구간 [t0, t1](닫힌) — 안 들면 거짓(Liang–Barsky).</summary>
    static bool Clip((double X, double Y) a, (double X, double Y) b, Box r, out double t0, out double t1)
    {
        t0 = 0; t1 = 1;
        if (r.IsNone) return false;
        double dx = b.X - a.X, dy = b.Y - a.Y;
        double lo = 0, hi = 1;
        bool Edge(double p, double q)
        {
            if (p == 0) return q >= 0;
            double t = q / p;
            if (p < 0) { if (t > hi) return false; if (t > lo) lo = t; }
            else { if (t < lo) return false; if (t < hi) hi = t; }
            return true;
        }
        if (!Edge(-dx, a.X - r.X0) || !Edge(dx, r.X1 - a.X) || !Edge(-dy, a.Y - r.Y0) || !Edge(dy, r.Y1 - a.Y)) return false;
        t0 = lo; t1 = hi;
        return true;
    }

    // ══════════════════════════════ 안쪽 지문 · 다시 키우기 ══════════════════════════════

    /// <summary>★[계획 5장 3 · 6] 상자 − <paramref name="margin"/> 안에 세 꼭짓점이 다 든 세모의 지문 — 세모 차례 · 꼭짓점 차례와 무관 · 좌표 비트 그대로(«세모 수:합:xor»).</summary>
    public static string InsideHash(IEnumerable<Tri> tris, Box box, double margin = Margin)
    {
        var inner = box.Grow(-margin);
        ulong sum = 0, x = 0; int n = 0;
        foreach (var t in tris)
        {
            if (!inner.Contains(t.A.X, t.A.Y) || !inner.Contains(t.B.X, t.B.Y) || !inner.Contains(t.C.X, t.C.Y)) continue;
            ulong h = TriKey(t);
            unchecked { sum += h; }
            x ^= h; n++;
        }
        return $"{n}:{sum:x16}{x:x16}";
    }

    static ulong Mix(ulong h) { unchecked { h ^= h >> 33; h *= 0xff51afd7ed558ccdUL; h ^= h >> 33; h *= 0xc4ceb9fe1a85ec53UL; h ^= h >> 33; return h; } }
    static ulong V(Point3 p) { unchecked { return Mix(Mix((ulong)BitConverter.DoubleToInt64Bits(p.X)) * 3UL + Mix((ulong)BitConverter.DoubleToInt64Bits(p.Y)) * 5UL + Mix((ulong)BitConverter.DoubleToInt64Bits(p.Z)) * 7UL); } }
    /// <summary>꼭짓점 차례와 무관한 세모 열쇠(셋을 정렬해 이어 섞음 — 더하기만 쓰면 다른 세모 셋이 같아질 수 있어 차례 있게 섞는다).</summary>
    static ulong TriKey(Tri t)
    {
        ulong a = V(t.A), b = V(t.B), c = V(t.C);
        if (a > b) (a, b) = (b, a);
        if (b > c) (b, c) = (c, b);
        if (a > b) (a, b) = (b, a);
        unchecked { return Mix(Mix(Mix(a) ^ b) ^ c); }
    }

    /// <summary>새 상자가 옛 상자보다 넓어진 쪽(왼 · 오 · 아래 · 위).</summary>
    public static (bool L, bool R, bool B, bool T) Grew(Box oldBox, Box newBox)
        => oldBox.IsNone || newBox.IsNone ? (false, false, false, false) : (newBox.X0 < oldBox.X0, newBox.X1 > oldBox.X1, newBox.Y0 < oldBox.Y0, newBox.Y1 > oldBox.Y1);

    /// <summary>★[Q3 · 계획 5장 10] 다시 키웠는데 못 커진 면인가 — 넓어진 쪽 가운데, 처음 자르기 전 범위(<paramref name="ext0"/>)는 옛 상자를 넘었는데 지금 보이는 범위는 옛 상자를 못 넘은 쪽이 있다.
    /// (처음부터 그쪽에 자료가 없던 면은 «못 커짐»이 아님.)</summary>
    public static bool NotGrown(Box oldBox, Box newBox, Box ext0, Box extNow)
    {
        var g = Grew(oldBox, newBox);
        if (ext0.IsNone) return false;
        if (extNow.IsNone) return true;
        return g.L && ext0.X0 < oldBox.X0 && !(extNow.X0 < oldBox.X0)
            || g.R && ext0.X1 > oldBox.X1 && !(extNow.X1 > oldBox.X1)
            || g.B && ext0.Y0 < oldBox.Y0 && !(extNow.Y0 < oldBox.Y0)
            || g.T && ext0.Y1 > oldBox.Y1 && !(extNow.Y1 > oldBox.Y1);
    }

    // ══════════════════════════════ 기록 글 ══════════════════════════════

    /// <summary>상자 네 수(«R» 그대로 · 쉼표).</summary>
    public static string BoxText(Box b) => b.IsNone ? "-" : string.Join(",", new[] { b.X0, b.Y0, b.X1, b.Y1 }.Select(v => v.ToString("R", CultureInfo.InvariantCulture)));

    public static bool TryBox(string s, out Box b)
    {
        b = Box.None;
        var a = (s ?? "").Split(',');
        if (a.Length != 4) return false;
        var v = new double[4];
        for (int i = 0; i < 4; i++) if (!double.TryParse(a[i], NumberStyles.Float, CultureInfo.InvariantCulture, out v[i]) || !double.IsFinite(v[i])) return false;
        b = new Box(v[0], v[1], v[2], v[3]);
        return !b.IsNone;
    }

    /// <summary>띠 목록(4×4 = 16수까지) «;»로.</summary>
    public static string BandsText(IEnumerable<Box> bands) => string.Join(";", bands.Select(BoxText));

    public static List<Box> ParseBands(string s)
    {
        var res = new List<Box>();
        foreach (var p in (s ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries)) if (TryBox(p, out var b)) res.Add(b);
        return res;
    }
}
