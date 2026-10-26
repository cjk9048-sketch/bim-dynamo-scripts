using DH.Takeoff.Core.View3D;

namespace DH.Takeoff.Core;

// R61b — 식의 항마다 그 도형의 자리를 기억한다(JACK 2026-10-26 «③ 콘크리트 줄에도 길이 숫자 — 정확하게»). 기록은 Explain 이 켤 때만(_record):
// 켜지 않으면 항 · 후보에 도형을 싣지 않고 계산도 하지 않는다 — Decompose 의 식 글자 · 속도는 그대로(계획 R61 §3.1).
// 좌표: 항은 만든 자리에서 바로 «좌표틀(a, b, c)»로 담는다 — 떡가래는 뚜껑의 2D 틀(ReadCap 의 u · v, c = 뚜껑에서 n 방향 거리 · 뚜껑 = 0),
// 높이장은 내려다본 축의 틀(u · v, c = 그 축 높이). 후보가 그 틀(Basis)을 지니고, 모델 = o + a·u + b·v + (c + C0)·n(o = Localized 가 뺀 꼭짓점).
public static partial class FormulaDecomposer
{
    [ThreadStatic] private static bool _record;

    // 항 하나의 도형(좌표틀 (a, b, c)) · 숫자.
    private sealed record TermGeo(string Shape, List<List<double[]>> Outlines, List<GeoLabel> Labels);
    private sealed record GeoLabel(string Text, double[] A, double[] B, bool Angle = false, bool Helper = false);
    private sealed record Basis(double[] U, double[] V, double[] N, double C0);

    /// <summary>
    /// Decompose 와 같은 길로 식을 만들며 항마다의 도형을 기억한다(③ 콘크리트 줄의 길이 숫자 — R61b). 식 글자 · 값 · 판정은 Decompose 와 같다.
    /// 식을 만들지 못했거나(Ok = false 이고 입체 하나라도 식이 없음) 도형이 빠진 입체가 있으면 geo 의 항은 그 입체만큼 비어 있다.
    /// </summary>
    public static DecompResult Explain(IReadOnlyList<ShapeSolid> solids, out FormulaGeometry geo, double precision = 1e-7, double thickness = 0, bool sheetNumbers = true)
    {
        bool before = _record;
        _record = true;
        try
        {
            var kept = new List<(Candidate c, double[] o)>();
            var r = Run(solids, precision, thickness, sheetNumbers, kept);
            geo = new FormulaGeometry { Formula = r.Formula };
            if (kept.Count == solids.Count)
                for (int i = 0; i < kept.Count; i++) Assemble(geo, i, kept[i].c, kept[i].o);
            return r;
        }
        finally { _record = before; }
    }

    // 입체 하나의 후보 도형을 모델 좌표로 옮겨 담는다.
    private static void Assemble(FormulaGeometry geo, int solid, Candidate c, double[] o)
    {
        if (c.Shapes == null || c.Frame is not { } f) return;
        Vec3 M(double[] p) => new(
            o[0] + p[0] * f.U[0] + p[1] * f.V[0] + (p[2] + f.C0) * f.N[0],
            o[1] + p[0] * f.U[1] + p[1] * f.V[1] + (p[2] + f.C0) * f.N[1],
            o[2] + p[0] * f.U[2] + p[1] * f.V[2] + (p[2] + f.C0) * f.N[2]);
        foreach (var (role, text, g) in c.Shapes)
            geo.Terms.Add(new FormulaTerm(solid, role, text, g.Shape,
                g.Outlines.Select(l => (IReadOnlyList<Vec3>)l.Select(M).ToList()).ToList(),
                g.Labels.Select(l => new TermLabel(l.Text, M(l.A), M(l.B), l.Angle, l.Helper)).ToList()));
    }

    // --- 만드는 도구(기록할 때만 부른다) ---

    private static double[] P3(P2 p, double c = 0) => new[] { p.X, p.Y, c };

    private static GeoLabel Lab(string text, P2 a, P2 b, bool helper = false) => new(text, P3(a), P3(b), false, helper);

    private static TermGeo Flat(string shape, List<P2> outline, params GeoLabel[] labels) =>
        new(shape, new List<List<double[]>> { outline.Select(p => P3(p)).ToList() }, labels.ToList());

    // 평면 도형의 점을 다른 2D 틀로(Frames 의 돌림 되돌리기 · Slabs 의 X/Y 바꿈) — c 는 그대로.
    private static TermGeo MapGeo(TermGeo g, Func<P2, P2> to)
    {
        double[] Map(double[] p)
        {
            var q = to(new P2(p[0], p[1]));
            return new[] { q.X, q.Y, p[2] };
        }
        return g with
        {
            Outlines = g.Outlines.Select(l => l.Select(Map).ToList()).ToList(),
            Labels = g.Labels.Select(l => l with { A = Map(l.A), B = Map(l.B) }).ToList(),
        };
    }

    private static List<Term> MapTerms(List<Term> terms, Func<P2, P2> to) =>
        terms.Select(t => t.Geo == null ? t : t with { Geo = MapGeo(t.Geo, to) }).ToList();

    // 호 위의 점들(a0 에서 sweep 만큼 — 반시계 +). 원은 48 칸 기준으로 나눈다.
    private static List<P2> ArcPoints(P2 c, double r, double a0, double sweep)
    {
        int n = Math.Max(4, (int)Math.Ceiling(48 * Math.Abs(sweep) / (2 * Math.PI)));
        var pts = new List<P2>(n + 1);
        for (int k = 0; k <= n; k++)
        {
            double a = a0 + sweep * k / n;
            pts.Add(new P2(c.X + r * Math.Cos(a), c.Y + r * Math.Sin(a)));
        }
        return pts;
    }

    private static double AngleOf(P2 c, P2 p) => Math.Atan2(p.Y - c.Y, p.X - c.X);

    // 부채꼴(중심 → 호 → 중심)의 도형: 반지름은 중심 → 시작점 · 각도(원의 몇 분의 몇이 아닌 «*각도/360» 일 때)는 중심 가까이.
    private static TermGeo SectorGeo(E2 e, double r, string rText, string? angleText)
    {
        var outline = new List<P2> { e.C };
        outline.AddRange(ArcPoints(e.C, r, AngleOf(e.C, e.A), SignedSweep(e)));
        var labels = new List<GeoLabel> { Lab(rText, e.C, e.A, helper: true) };
        if (angleText != null)
            labels.Add(new GeoLabel(angleText, P3(e.C), P3(new P2(e.C.X + (e.M.X - e.C.X) * 0.35, e.C.Y + (e.M.Y - e.C.Y) * 0.35)), Angle: true, Helper: false));
        return new TermGeo("sector", new List<List<double[]>> { outline.Select(p => P3(p)).ToList() }, labels);
    }

    // «*각도/360» 의 각도 글자(반원 · 사분원 · 3/4 원은 없음).
    private static string? AngleText(double theta, double r, double eps)
    {
        string factor = AngleFactor(theta, r, eps);
        return factor.EndsWith("/360", StringComparison.Ordinal) ? factor[1..^4] : null;
    }

    // 떡가래 후보의 도형: 더함(적힌 차례) · 뺌(적힌 차례) · 길이(첫 도형의 첫 점에서 n 의 반대로 len).
    private static List<(TermRole, string, TermGeo)> PrismShapes(List<Term> plus, List<Term> minus, string h, double len)
    {
        var shapes = new List<(TermRole, string, TermGeo)>();
        foreach (var t in plus) if (t.Geo != null) shapes.Add((TermRole.Add, t.Text, t.Geo));
        foreach (var t in minus) if (t.Geo != null) shapes.Add((TermRole.Deduct, t.Text, t.Geo));
        // 기준점 = 첫 도형 테두리의 첫 점 — 부채꼴 · 활꼴 삼각형은 첫 점이 호의 중심(휜 벽이면 벽에서 수십 m 밖)이라 그 다음 점(호 위 — R61b 설치 전 검토 중간).
        var first = plus.Select(t => t.Geo).FirstOrDefault(g => g != null && g.Outlines.Count > 0 && g.Outlines[0].Count > 1);
        var anchor = first == null ? null : first.Shape is "sector" or "triangle" ? first.Outlines[0][1] : first.Outlines[0][0];
        if (anchor != null)
            shapes.Add((TermRole.Length, h, new TermGeo("length", new List<List<double[]>>(),
                new List<GeoLabel> { new(h, new[] { anchor[0], anchor[1], 0.0 }, new[] { anchor[0], anchor[1], -len }, false, true) })));
        return shapes;
    }

    // --- 떡가래 조각(Slabs): p 틀(swap 이면 X/Y 바꾼 틀)의 조각 → 좌표틀(Frames 가 돌린 틀) ---
    private static TermGeo SlabGeo(Piece q, bool swap, string kind, string? wideText, string? narrowText, string? hText, string? w0Text)
    {
        P2 Un(double x, double y) => swap ? new P2(y, x) : new P2(x, y);
        P2 a = Un(q.XL0, q.Y0), b = Un(q.XR0, q.Y0), c = Un(q.XR1, q.Y1), d = Un(q.XL1, q.Y1);
        var outline = new List<P2> { a, b, c, d };
        var labels = new List<GeoLabel>();
        double w0 = q.XR0 - q.XL0, w1 = q.XR1 - q.XL1;
        bool bottomWide = w0 >= w1;
        // 높이(띠의 두께)를 재는 선: 위아래 변이 겹치는 x 의 가운데(없으면 왼쪽 아래 점)에서 수직.
        // 바로 선 네모는 왼변 그대로(재는 선이 아니라 변).
        double lo = Math.Max(q.XL0, q.XL1), hi = Math.Min(q.XR0, q.XR1);
        double hx = kind == "rect" ? q.XL0 : hi >= lo ? (lo + hi) / 2 : q.XL0;
        if (w0Text != null) labels.Add(Lab(w0Text, a, b));                                         // 네모 · 평행사변형: 아랫변
        if (wideText != null) labels.Add(bottomWide ? Lab(wideText, a, b) : Lab(wideText, d, c));  // 넓은 변
        if (narrowText != null) labels.Add(bottomWide ? Lab(narrowText, d, c) : Lab(narrowText, a, b));
        if (hText != null) labels.Add(Lab(hText, Un(hx, q.Y0), Un(hx, q.Y1), helper: kind != "rect"));
        return new TermGeo(kind, new List<List<double[]>> { outline.Select(p => P3(p)).ToList() }, labels);
    }

    // 네모 [x0, x1] × [y0, y1](p 틀) — 아랫변에 x 폭 · 왼변에 y 폭.
    private static TermGeo RectGeo(double x0, double x1, double y0, double y1, bool swap, string xText, string yText)
    {
        P2 Un(double x, double y) => swap ? new P2(y, x) : new P2(x, y);
        P2 a = Un(x0, y0), b = Un(x1, y0), c = Un(x1, y1), d = Un(x0, y1);
        return Flat("rect", new List<P2> { a, b, c, d }, Lab(xText, a, b), Lab(yText, a, d));
    }

    // --- 원 · 호(ReadCap — 처음부터 뚜껑 틀) ---

    private static TermGeo CircleGeo(P2 c, double r, string rText) =>
        Flat("circle", ArcPoints(c, r, 0, 2 * Math.PI), Lab(rText, c, new P2(c.X + r, c.Y), helper: true));

    // 활꼴의 삼각형(중심 · 호의 두 끝): 사분원이면 r × r(두 반지름) · 아니면 현 × (중심에서 현까지).
    private static TermGeo ArcTriangleGeo(E2 e, string first, string second, bool quarter)
    {
        var outline = new List<P2> { e.C, e.A, e.B };
        if (quarter) return Flat("triangle", outline, Lab(first, e.C, e.A, helper: true), Lab(second, e.C, e.B, helper: true));
        var mid = new P2((e.A.X + e.B.X) / 2, (e.A.Y + e.B.Y) / 2);
        return Flat("triangle", outline, Lab(first, e.A, e.B), Lab(second, e.C, mid, helper: true));
    }

    // 떡가래 뚜껑의 좌표틀(ReadCap 과 같은 u · v) · C0 = 뚜껑 면의 n 거리.
    private static Basis CapFrame(double[] n, double dc)
    {
        double[] helper = Math.Abs(n[2]) < 0.9 ? new[] { 0.0, 0.0, 1.0 } : new[] { 1.0, 0.0, 0.0 };
        double[] u = Unit(Cross(helper, n));
        double[] v = Cross(n, u);
        return new Basis(u, v, n, dc);
    }

    // --- 높이장(넓이 × 평균높이): 칸은 돌린 평면 틀(HeightField 의 Rotate(a)) · 높이 = 윗면 · 밑면 평면 ---

    private static double TopOf(Cell c, P2 p) => c.Top.A + c.Top.B * p.X + c.Top.C * p.Y;
    private static double BotOf(Cell c, P2 p) => c.Bot.A + c.Bot.B * p.X + c.Bot.C * p.Y;

    // 평면 치수(폭 · dy)를 재는 선: 첫 점의 윗면 높이에서 수평(윗면이 기울어도 선분 길이 = 평면 치수).
    private static GeoLabel PlanLabel(Cell c, string text, P2 a, P2 b, bool helper = false)
    {
        double z = TopOf(c, a);
        return new GeoLabel(text, P3(a, z), P3(b, z), false, helper);
    }

    // 높이가 일정한 칸: 윗면의 칸 테두리 · 폭(같으면 w, 아니면 넓은 변 · 좁은 변) · dy(재는 선) · 높이(첫 꼭짓점의 수직선).
    private static TermGeo FlatCellGeo(Cell c, string? wText, string? wideText, string? narrowText, string dyText, string hText)
    {
        P2 a = new(c.XL0, c.Y0), b = new(c.XR0, c.Y0), cc = new(c.XR1, c.Y1), d = new(c.XL1, c.Y1);
        double[] T(P2 p) => P3(p, TopOf(c, p));
        var labels = new List<GeoLabel>();
        bool bottomWide = c.XR0 - c.XL0 >= c.XR1 - c.XL1;
        if (wText != null) labels.Add(PlanLabel(c, wText, a, b));
        if (wideText != null) labels.Add(bottomWide ? PlanLabel(c, wideText, a, b) : PlanLabel(c, wideText, d, cc));
        if (narrowText != null) labels.Add(bottomWide ? PlanLabel(c, narrowText, d, cc) : PlanLabel(c, narrowText, a, b));
        double lo = Math.Max(c.XL0, c.XL1), hi = Math.Min(c.XR0, c.XR1), x = hi >= lo ? (lo + hi) / 2 : c.XL0;
        labels.Add(PlanLabel(c, dyText, new P2(x, c.Y0), new P2(x, c.Y1), helper: true));
        labels.Add(new GeoLabel(hText, P3(a, BotOf(c, a)), T(a), Helper: true));
        return new TermGeo("cell", new List<List<double[]>> { new[] { a, b, cc, d }.Select(T).ToList() }, labels);
    }

    // 높이가 변하는 칸의 조각(평행사변형 4점 · 삼각형 3점 — 첫 두 점이 밑변): 폭 · dy · 적힌 높이(0 아닌 높이마다 그 꼭짓점의 수직선 — 같은 글은 한 번).
    private static TermGeo CellPolyGeo(Cell c, P2[] poly, string wText, string dyText, double eps)
    {
        double[] T(P2 p) => P3(p, TopOf(c, p));
        var labels = new List<GeoLabel> { PlanLabel(c, wText, poly[0], poly[1]) };
        if (poly.Length == 3) labels.Add(PlanLabel(c, dyText, new P2(poly[2].X, poly[0].Y), poly[2], helper: true));
        else
        {
            double lo = Math.Max(poly[0].X, poly[3].X), hi = Math.Min(poly[1].X, poly[2].X), x = hi >= lo ? (lo + hi) / 2 : poly[0].X;
            labels.Add(PlanLabel(c, dyText, new P2(x, poly[0].Y), new P2(x, poly[3].Y), helper: true));
        }
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var p in poly)
        {
            double h = TopOf(c, p) - BotOf(c, p);
            if (Math.Abs(h) <= eps) continue;
            string text = F(h);
            if (seen.Add(text)) labels.Add(new GeoLabel(text, P3(p, BotOf(c, p)), T(p), Helper: true));
        }
        return new TermGeo(poly.Length == 3 ? "cell-tri" : "cell-para", new List<List<double[]>> { poly.Select(T).ToList() }, labels);
    }

    // 높이장 후보의 도형: 무리(같은 글자)마다 첫 것만 숫자 · 도형은 모두(계획 검토 — «(조각)*개수»).
    private static List<(TermRole, string, TermGeo)> HeightShapes(IEnumerable<(string text, List<Term> members)> groups)
    {
        var shapes = new List<(TermRole, string, TermGeo)>();
        foreach (var (text, members) in groups)
            for (int k = 0; k < members.Count; k++)
                if (members[k].Geo is { } g) shapes.Add((TermRole.Add, text, k == 0 ? g : g with { Labels = new List<GeoLabel>() }));
        return shapes;
    }

    // 돌린 평면 틀(Rotate(a)) → 그 축의 틀(u · v · n) — Rotate 의 반대.
    private static Candidate Unrotated(Candidate c, double a, double[] u, double[] v, double[] n)
    {
        double cs = Math.Cos(a), sn = Math.Sin(a);
        var shapes = c.Shapes?.Select(s => (s.role, s.text, MapGeo(s.geo, q => new P2(q.X * cs - q.Y * sn, q.X * sn + q.Y * cs)))).ToList();
        return c with { Shapes = shapes, Frame = new Basis(u, v, n, 0) };
    }
}
