using Clipper2Lib;

namespace DH.Takeoff.Core.Formwork;

/// <summary>
/// 판 모양 검사(R68 — JACK 2026-10-28 «이런 거푸집이 있을수있어? · 이런식의 기하적인 모양을 검증하는 시스템이 필요»: 계단실 안쪽 벽 다섯 부재가 한 판 ·
/// 계단참 슬래브가 지나간 틈 두 줄). 판(FormworkSheet.Plates — 평면 판만)마다 면 틀의 2D 다각형으로 본다 — 셈은 바꾸지 않는다:
///  · 좁은 홈 · 틈(⚠): 판 바깥 테두리를 닫은 것(NotchHalf 만큼 늘였다 줄임) − 판 = 입이 0.5 m 보다 좁은 홈 · 틈. 깊이(넓이 / 입) ≥ 0.5 m · 깊이 ≥ 입 × 3 ·
///    넓이 ≥ 0.05 ㎡ 인 것만 — 판 안 구멍(창 · 슬리브)과 계단이 맞닿아 생긴 톱니(입이 길고 얕음)는 아니다(critic R68 M4).
///  · 가는 띠(⚠): 판 − 판을 연 것(ThinHalf 만큼 줄였다 늘임) = 폭 5 cm 미만 띠(JACK 고름의 글) · 조각 넓이 0.005 ㎡ 이상(뾰족한 귀의 부스러기는 아님).
///  · 작은 판(⚠): 넓이 0.05 ㎡ 미만 가운데 이웃이 덮고 남은 부스러기(계단 세모) — 받침처럼 통째로 작은 면은 아니다(critic R68 M4).
///  · 섞임(ⓘ 정보): 부위가 둘 이상인 판 · 덩어리를 따라 제 사슬과 다른 종류를 받은 조각이 든 판. 외측 덩어리(하부슬래브 옆 + 벽 + 상부슬래브 옆)는 녹촌 2D
///    외부면(«(29.6+6.7×2)×(7+2.5)» = 0.8 + 8.3 + 0.4)과 같은 꼴이라 경고가 아니라 정보다(내 판단 — 녹촌 34 장을 ⚠ 로 띄우면 잡음).
///  · 2D 꼴 아님(⚠): 식이 넓이만(AreaOnly — 가로 × 세로 꼴로 못 적음) · 또는 채움(넓이 / 싼 네모) 0.5 미만. 꼭짓점 수는 보지 않는다(창 · 문 많은 큰 벽도 식은 2D 꼴) ·
///    계단의 면은 보지 않는다(계단꼴이 제 모양).
/// </summary>
public static class FormworkShapeCheck
{
    /// <summary>좁은 홈: 닫기 반폭(m) — 폭이 이 두 배(0.5 m)보다 좁은 홈 · 틈을 잡는다(계단참 틈 0.3 · 0.4 는 잡고 문 0.6 이상은 안 잡음).</summary>
    public const double NotchHalf = 0.25;
    public const double NotchMinLength = 0.5;
    public const double NotchAspect = 3;
    public const double NotchMinArea = 0.05;

    /// <summary>가는 띠: 열기 반폭(m) — 폭 5 cm 미만 띠.</summary>
    public const double ThinHalf = 0.025;
    public const double ThinMinArea = 0.005;

    /// <summary>작은 판(㎡).</summary>
    public const double SmallArea = 0.05;

    /// <summary>2D 꼴: 넓이 / 싼 네모가 이보다 작으면.</summary>
    public const double MinFill = 0.5;

    public enum Test { Notch, Thin, Small, Mixed, Shape }

    /// <summary>검사 이름(딱지 · 엑셀).</summary>
    public static string NameOf(Test test) => test switch
    {
        Test.Notch => "좁은 홈",
        Test.Thin => "가는 띠",
        Test.Small => "작은 판",
        Test.Mixed => "섞임",
        _ => "2D 꼴 아님",
    };

    /// <summary>정보(ⓘ)인가 — 아니면 경고(⚠).</summary>
    public static bool IsInfo(Test test) => test == Test.Mixed;

    /// <summary>판 하나의 걸림: 검사 · 까닭(«홈 2.3 × 0.33 m») · 3D 에 그릴 테두리(세계 좌표 고리 — 홈 · 띠는 그 자리, 그 밖은 판 테두리).</summary>
    public sealed record Finding(Test Test, string Text, IReadOnlyList<IReadOnlyList<double[]>> Rings);

    /// <summary>판들의 걸림(걸린 판만).</summary>
    /// <param name="partOf">부재 Id → 부위(없으면 부재가 읽힌 때의 값).</param>
    /// <param name="failed">보지 못한 판의 수(다각형 연산이 예외를 냄 — 검사는 셈을 바꾸지 않으니 판 짓기를 막지 않는다 · 일지).</param>
    public static Dictionary<FormworkUnit, List<Finding>> All(IEnumerable<FormworkUnit> plates, Func<long, string>? partOf, out int failed)
    {
        var found = new Dictionary<FormworkUnit, List<Finding>>(ReferenceEqualityComparer.Instance);
        failed = 0;
        foreach (var plate in plates)
        {
            try
            {
                if (Of(plate, partOf) is { Count: > 0 } list) found[plate] = list;
            }
            catch (Exception) { failed++; }
        }
        return found;
    }

    public static Dictionary<FormworkUnit, List<Finding>> All(IEnumerable<FormworkUnit> plates, Func<long, string>? partOf = null) => All(plates, partOf, out _);

    /// <summary>판 하나를 본다(곡면 · 테두리 없는 판은 빈 목록).</summary>
    public static List<Finding> Of(FormworkUnit plate, Func<long, string>? partOf = null)
    {
        var found = new List<Finding>();
        if (plate.Normal.Length != 3 || plate.Shape.Outline.Count == 0 || plate.Shape.Outline[0].Count < 3) return found;
        var n = new V3(plate.Normal[0], plate.Normal[1], plate.Normal[2]).Unit();
        var frame = Frame.Of(V3.Of(plate.Shape.Outline[0][0]), n);
        var raw = new Paths64();
        foreach (var ring in plate.Shape.Outline)
        {
            var path = new Path64();
            foreach (var q in ring)
            {
                var (u, v) = frame.To2D(V3.Of(q));
                path.Add(Poly.P(u, v));
            }
            if (path.Count >= 3) raw.Add(path);
        }
        var shape = Poly.Normalize(raw);
        if (shape.Count == 0) return found;
        var outline = (IReadOnlyList<IReadOnlyList<double[]>>)plate.Shape.Outline;

        // 좁은 홈 · 틈: 바깥 테두리만 본다(판 안 구멍 — 창 · 슬리브 — 은 제 둘레 면이 있는 진짜 개구부 · critic R68 M4). 깊이 = 넓이 / 입(닫기가 메운 쪽 테두리 —
        // 판 테두리 위가 아닌 변) — 입이 좁고 깊은 홈만(계단이 맞닿아 생긴 톱니는 입이 길고 얕아 아니다).
        var outer = new Paths64(shape.Where(path => Clipper.Area(path) > 0));
        var gaps = Poly.Difference(Grow(Grow(outer, NotchHalf), -NotchHalf), outer);
        // 판을 끝까지 가로지른 틈은 입이 둘 — 폭 = 입 길이 / 입 수 · 길이 = 넓이 / 폭(설치 전 검토 낮음).
        var slits = new List<(double len, double wid, Path64 path)>();
        foreach (var gap in gaps)
        {
            double area = Math.Abs(Clipper.Area(gap)) / (Poly.Scale * Poly.Scale);
            if (area < NotchMinArea) continue;
            var (mouth, runs) = Mouth(gap, outer);
            if (mouth <= 0) continue;
            double width = mouth / runs, depth = area / width;
            if (depth >= NotchMinLength && depth >= NotchAspect * width) slits.Add((depth, width, gap));
        }
        if (slits.Count > 0)
        {
            var widest = slits.OrderByDescending(s => s.len).First();
            string more = slits.Count > 1 ? $" 외 {slits.Count - 1}" : "";
            found.Add(new Finding(Test.Notch, FormattableString.Invariant($"홈 {widest.len:0.##} × {widest.wid:0.##} m{more}"), slits.Select(s => World(s.path, frame)).ToList()));
        }

        // 가는 띠.
        var bits = Poly.Difference(shape, Grow(Grow(shape, -ThinHalf), ThinHalf)).Where(b => Math.Abs(Clipper.Area(b)) / (Poly.Scale * Poly.Scale) >= ThinMinArea).ToList();
        if (bits.Count > 0)
        {
            double area = bits.Sum(b => Math.Abs(Clipper.Area(b))) / (Poly.Scale * Poly.Scale);
            found.Add(new Finding(Test.Thin, FormattableString.Invariant($"폭 5 cm 미만 띠 {area:0.###} ㎡"), bits.Select(b => World(b, frame)).ToList()));
        }

        // 작은 판: 이웃이 덮고 남은 부스러기만(면의 일부를 콘크리트가 덮음 · 받침류 아님).
        if (plate.Area < SmallArea && plate.Members.Any(m => m.Face.Covered > 1e-6 && !FormworkTable.IsSmallPart(m.Result.Element.Type)))
            found.Add(new Finding(Test.Small, FormattableString.Invariant($"{plate.Area:0.###} ㎡"), outline));

        // 섞임(정보): 부위 둘 이상 · 덩어리를 따라 제 사슬과 다른 종류를 받은 조각(사람 값 · Revit 판 종류는 아님).
        var parts = plate.Members.Select(m => partOf?.Invoke(m.Result.Element.Id) ?? m.Result.Element.Part).Where(p => p.Length > 0).Distinct(StringComparer.Ordinal).ToList();
        var inherited = plate.Members.Where(m => Inherited(m.Patch)).ToList();
        if (parts.Count > 1 || inherited.Count > 0)
        {
            var words = new List<string>();
            if (parts.Count > 1) words.Add("부위 " + string.Join(" · ", parts));
            if (inherited.Count > 0) words.Add("종류 물려받음 " + string.Join(" · ", inherited.Select(m => $"{m.Result.Element.Type}(제 {m.Patch.Own!.Value.Item})").Distinct(StringComparer.Ordinal)));
            found.Add(new Finding(Test.Mixed, string.Join(" / ", words), outline));
        }

        // 2D 꼴 아님: 채움 = 넓이 / 가장 작은 돌린 네모(바깥 테두리 변의 방향마다 싼 네모 — 수평 판의 틀은 늘 세계 X 라 비스듬히 놓인 건물에서 축 네모로 재면
        // 헛걸림 · 설치 전 검토 중간).
        double boxArea = MinBox(outer);
        double fill = boxArea > 0 ? Poly.Area(shape) / boxArea : 1;
        // 계단의 면은 계단꼴이 제 모양이라 보지 않는다(계단 산출은 R65 계단 줄).
        bool stair = plate.Members.Count > 0 && plate.Members.All(m => m.Result.Element.Stair || m.Result.Element.Kind == MemberKind.Stair);
        if (!stair && (plate.AreaOnly || fill < MinFill))
            found.Add(new Finding(Test.Shape, plate.AreaOnly ? "식이 넓이만" : FormattableString.Invariant($"채움 {fill:0.##}"), outline));
        return found;
    }

    /// <summary>덩어리를 따라 종류를 받은 조각인가(FollowGroups 가 다스리는 조각의 종류를 준 것 — 제 사슬의 종류와 다름 · 사람 값 · Revit 판 종류가 아님).</summary>
    internal static bool Inherited(FacePatch patch) =>
        patch.Follow is { } follow && patch.Item == follow && patch.Own is { } own && own.Item != follow && patch.KindLead.Length == 0;

    // 반폭 d(m) 만큼 늘이기(−면 줄이기) — 모서리는 뾰족하게(직각 오목 귀가 닫기 · 열기에 그대로 남게).
    private static Paths64 Grow(Paths64 paths, double d) => Clipper.InflatePaths(paths, d * Poly.Scale, JoinType.Miter, EndType.Polygon);

    // 홈의 입(m)과 입 수: 홈 테두리 가운데 판 테두리 위에 있지 않은 변(닫기가 메운 쪽)의 길이 · 그런 변이 이어진 토막 수(고리를 돌며). 변의 가운데 점이 판의 변에서
    // 2 µm 안이면 판 테두리 위.
    private static (double mouth, int runs) Mouth(Path64 gap, Paths64 shape)
    {
        var open = new bool[gap.Count];
        double mouth = 0;
        for (int i = 0; i < gap.Count; i++)
        {
            var a = gap[i];
            var b = gap[(i + 1) % gap.Count];
            double len = Math.Sqrt((double)(a.X - b.X) * (a.X - b.X) + (double)(a.Y - b.Y) * (a.Y - b.Y));
            if (len < 1) { open[i] = i > 0 && open[i - 1]; continue; }
            double mx = (a.X + b.X) / 2.0, my = (a.Y + b.Y) / 2.0;
            open[i] = !shape.Any(path => OnRing(path, mx, my));
            if (open[i]) mouth += len / Poly.Scale;
        }
        int runs = Enumerable.Range(0, gap.Count).Count(i => open[i] && !open[(i + gap.Count - 1) % gap.Count]);
        if (runs == 0 && mouth > 0) runs = 1;
        return (mouth, Math.Max(1, runs));
    }

    // 가장 작은 돌린 네모의 넓이(㎡): 바깥 고리의 변(1 mm 넘는 것 · 긴 차례 256 개까지) 방향마다 싼 네모를 재어 가장 작은 것.
    private static double MinBox(Paths64 outer)
    {
        var pts = outer.SelectMany(p => p).ToList();
        if (pts.Count < 3) return 0;
        var dirs = outer.SelectMany(p => p.Select((q, i) => (dx: (double)(p[(i + 1) % p.Count].X - q.X), dy: (double)(p[(i + 1) % p.Count].Y - q.Y))))
                        .Select(d => (d.dx, d.dy, len: Math.Sqrt(d.dx * d.dx + d.dy * d.dy))).Where(d => d.len > 1000)
                        .OrderByDescending(d => d.len).Take(256).Select(d => (ux: d.dx / d.len, uy: d.dy / d.len)).ToList();
        dirs.Add((1, 0));
        double best = double.MaxValue;
        foreach (var (ux, uy) in dirs)
        {
            double u0 = double.MaxValue, u1 = double.MinValue, v0 = double.MaxValue, v1 = double.MinValue;
            foreach (var q in pts)
            {
                double u = q.X * ux + q.Y * uy, v = -q.X * uy + q.Y * ux;
                u0 = Math.Min(u0, u); u1 = Math.Max(u1, u); v0 = Math.Min(v0, v); v1 = Math.Max(v1, v);
            }
            best = Math.Min(best, (u1 - u0) * (v1 - v0));
        }
        return best / (Poly.Scale * Poly.Scale);
    }

    private static bool OnRing(Path64 path, double x, double y)
    {
        for (int i = 0; i < path.Count; i++)
        {
            var p = path[i];
            var q = path[(i + 1) % path.Count];
            double dx = q.X - p.X, dy = q.Y - p.Y, l2 = dx * dx + dy * dy;
            double t = l2 > 0 ? Math.Clamp(((x - p.X) * dx + (y - p.Y) * dy) / l2, 0, 1) : 0;
            double ex = p.X + t * dx - x, ey = p.Y + t * dy - y;
            if (ex * ex + ey * ey <= 4) return true;
        }
        return false;
    }

    private static IReadOnlyList<double[]> World(Path64 path, Frame frame) =>
        path.Select(q => { var p = frame.To3D(q.X / Poly.Scale, q.Y / Poly.Scale); return new[] { p.X, p.Y, p.Z }; }).ToList();
}
