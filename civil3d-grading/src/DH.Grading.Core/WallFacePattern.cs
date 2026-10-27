using NetTopologySuite.Geometries;

namespace DH.Grading.Core;

/// <summary>★★★[v106 · 계획_v106.md 2.4 · JACK 1027 «스윕 후 밖으로 보이는 표면만 기존처럼 콘크리트는 무늬나 보강토는 보강토 모양으로»]
/// <b>옹벽 보이는 면 무늬</b> — 무늬 좌표(u = 그 옹벽 줄 앞면을 따라 잰 길이 · Z = 절대 높이)에 조각을 깔고, 면 판(<see cref="WallSweep.Sheet"/>)의 보이는 칸으로 자른다.
/// <para>조각은 면에서 <see cref="Spec.Relief"/>만큼 공기 쪽으로 튀어나오고 <see cref="Spec.Embed"/>만큼 몸통 속으로 들어간다(8월 «무늬 두께 4배» — 면이 칸 안에서 조금 휘어도 뜨지 않게).</para>
/// <para>가로줄은 <b>절대 높이</b> 기준(땅 기울기를 따라 기울지 않게) · 같은 입력이면 같은 조각(자리 해시 — 난수 없음).</para>
/// <para>무늬 종류(JACK 1027 인터뷰): 콘크리트 = 자연석 · 보강토 = 블록 0.46 × 0.20 엇갈려 쌓기 · 앵커식 = 1.5 m 패널 격자 + 온전한 칸 가운데 앵커 머리 · 석축 = 돌.</para></summary>
public static class WallFacePattern
{
    /// <summary>무늬 종류 — 형식에서 정해진다(<see cref="StyleOf"/>).</summary>
    public enum Style { 없음 = 0, 자연석 = 1, 석축 = 2, 블록 = 3, 패널 = 4 }

    /// <summary>무늬 치수(첫 값 — 고칠 자리 한 곳). 길이 m.</summary>
    public sealed record Spec(Style Style, string Layer, double Relief, double Embed, double Joint,
        double RowMin, double RowMax, double WMin, double WMax, double Chamfer, double Jitter);

    public static readonly Spec 자연석 = new(Style.자연석, "옹벽_무늬_자연석", 0.035, 0.10, 0.03, 0.28, 0.45, 0.35, 0.80, 0.05, 0.04);
    public static readonly Spec 석축찰 = new(Style.석축, "옹벽_무늬_석축", 0.05, 0.10, 0.04, 0.30, 0.40, 0.35, 0.55, 0.07, 0.03);
    public static readonly Spec 석축메 = new(Style.석축, "옹벽_무늬_석축", 0.05, 0.10, 0.02, 0.30, 0.40, 0.35, 0.55, 0.07, 0.03);
    public static readonly Spec 블록 = new(Style.블록, "옹벽_무늬_블록", 0.03, 0.10, 0.012, 0.20, 0.20, 0.46, 0.46, 0, 0);
    public static readonly Spec 패널 = new(Style.패널, "옹벽_무늬_패널", 0.02, 0.10, 0.02, 1.50, 1.50, 1.50, 1.50, 0, 0);

    /// <summary>앵커 머리(8월 치수) — 도넛 1단 0.56 □ · 2단 0.36 □ · 정착판 0.15 □ · 겉 내밀기는 몸통 앞면에서 잰 값.</summary>
    public const string AnchorLayer = "옹벽_앵커머리";
    static readonly (double Side, double Out)[] Anchor = { (0.56, 0.07), (0.36, 0.12), (0.15, 0.14) };

    /// <summary>층 색(RGB) — 몸통 층(회색 173)보다 조금 밝게 해 줄눈이 어둡게 보인다.</summary>
    public static (byte R, byte G, byte B) ColorOf(string layer) => layer switch
    {
        "옹벽_무늬_자연석" => (196, 190, 178),
        "옹벽_무늬_석축" => (168, 160, 146),
        "옹벽_무늬_블록" => (186, 178, 164),
        "옹벽_무늬_패널" => (200, 198, 192),
        AnchorLayer => (120, 120, 120),
        _ => (180, 180, 180),
    };

    /// <summary>형식 → 무늬. 부벽식 · 역L형(숨김 형식)도 콘크리트라 자연석.</summary>
    public static Spec? SpecOf(WallSection.Kind k) => k switch
    {
        WallSection.Kind.역T형 or WallSection.Kind.L형 or WallSection.Kind.역L형 or WallSection.Kind.중력식
            or WallSection.Kind.반중력식 or WallSection.Kind.부벽식 => 자연석,
        WallSection.Kind.보강토 => 블록,
        WallSection.Kind.패널식 => 패널,
        WallSection.Kind.찰쌓기 => 석축찰,
        WallSection.Kind.메쌓기 => 석축메,
        _ => null,
    };

    public static Style StyleOf(WallSection.Kind k) => SpecOf(k)?.Style ?? Style.없음;

    /// <summary>무늬 조각 하나 — 무늬 좌표 다각형(반시계) · 겉 내밀기 · 층 · 축에 나란한 직사각형인가 ·
    /// <see cref="WholeOf"/>가 있으면 그 칸이 <b>잘리지 않고 다 보일 때만</b> 남긴다(앵커 머리 — 머리 자신(0.56 □)이 띠 안 · 앞 땅 위일 때 · JACK 1027).</summary>
    public sealed class Item
    {
        public List<(double U, double Z)> Poly = new();
        public double Relief;
        public string Layer = "";
        public bool Rect;
        public (double U0, double Z0, double U1, double Z1)? WholeOf;
    }

    // ── 자리 해시 ─────────────────────────────────────────────────────────────
    /// <summary>정수 둘(+ 갈래)로 [0, 1) — 같은 자리는 늘 같은 값.</summary>
    static double H01(long a, long b, int salt)
    {
        unchecked
        {
            ulong x = (ulong)a * 0x9E3779B97F4A7C15UL ^ (ulong)b * 0xC2B2AE3D27D4EB4FUL ^ (ulong)(salt + 1) * 0x165667B19E3779F9UL;
            x ^= x >> 33; x *= 0xFF51AFD7ED558CCDUL; x ^= x >> 33; x *= 0xC4CEB9FE1A85EC53UL; x ^= x >> 33;
            return (x >> 11) * (1.0 / (1UL << 53));
        }
    }

    /// <summary>[zMin, zMax]를 덮는 줄(아래 Z · 위 Z · 줄 번호) — 절대 높이 0에서 위아래로 줄 높이를 쌓는다(자리 해시).</summary>
    static List<(double Z0, double Z1, long J)> Rows(Spec s, double zMin, double zMax)
    {
        var res = new List<(double, double, long)>();
        if (!(zMax > zMin)) return res;
        double Hj(long j) => s.RowMax <= s.RowMin ? s.RowMin : s.RowMin + (s.RowMax - s.RowMin) * H01(j, 7, 1);
        if (s.RowMax <= s.RowMin)
        {
            // 고른 줄(블록 · 패널) — 바로 셈
            long j0 = (long)Math.Floor(zMin / s.RowMin), j1 = (long)Math.Floor(zMax / s.RowMin);
            for (long j = j0; j <= j1; j++) res.Add((j * s.RowMin, (j + 1) * s.RowMin, j));
            return res;
        }
        // 고르지 않은 줄 — 0에서 zMin 쪽으로 걸어간 뒤 zMax까지
        double z = 0; long jj = 0;
        if (zMin >= 0) { while (z + Hj(jj) <= zMin) { z += Hj(jj); jj++; } }
        else { while (z > zMin) { jj--; z -= Hj(jj); } }
        while (z < zMax) { double h = Hj(jj); res.Add((z, z + h, jj)); z += h; jj++; }
        return res;
    }

    /// <summary>[uMin, uMax] × [zMin, zMax]를 덮는 무늬 조각(무늬 좌표). 잘리지 않은 칸 그대로 — 자르기는 면 판이 한다.</summary>
    public static List<Item> Items(Spec s, double uMin, double uMax, double zMin, double zMax)
    {
        var items = new List<Item>();
        if (!(uMax > uMin) || !(zMax > zMin)) return items;
        double g = s.Joint / 2;
        foreach (var (z0, z1, j) in Rows(s, zMin, zMax))
        {
            switch (s.Style)
            {
                case Style.블록:
                {
                    double off = (j & 1) != 0 ? s.WMin / 2 : 0;
                    long i0 = (long)Math.Floor((uMin - off) / s.WMin), i1 = (long)Math.Floor((uMax - off) / s.WMin);
                    for (long i = i0; i <= i1; i++)
                    {
                        double u0 = off + i * s.WMin;
                        items.Add(RectItem(u0 + g, z0 + g, u0 + s.WMin - g, z1 - g, s.Relief, s.Layer));
                    }
                    break;
                }
                case Style.패널:
                {
                    long i0 = (long)Math.Floor(uMin / s.WMin), i1 = (long)Math.Floor(uMax / s.WMin);
                    for (long i = i0; i <= i1; i++)
                    {
                        double u0 = i * s.WMin, u1 = u0 + s.WMin;
                        items.Add(RectItem(u0 + g, z0 + g, u1 - g, z1 - g, s.Relief, s.Layer));
                        // ★[JACK 1027 «가운데 앵커부분은 해당 부분이 조금이라도 잘리지 않는 한 다 표현»] 판정 칸 = 앵커 머리 자신(가장 큰 도넛 0.56 □) — 패널 칸이 잘려도 머리가 다 보이면 그린다 · 셋(도넛 · 도넛 · 정착판)은 같이 나고 같이 빠진다
                        double cu = (u0 + u1) / 2, cz = (z0 + z1) / 2, ha = Anchor[0].Side / 2;
                        foreach (var (side, outv) in Anchor)
                        {
                            var a = RectItem(cu - side / 2, cz - side / 2, cu + side / 2, cz + side / 2, outv, AnchorLayer);
                            a.WholeOf = (cu - ha, cz - ha, cu + ha, cz + ha);
                            items.Add(a);
                        }
                    }
                    break;
                }
                default:
                    Stones(s, z0, z1, j, uMin, uMax, items);
                    break;
            }
        }
        return items;
    }

    static Item RectItem(double u0, double z0, double u1, double z1, double relief, string layer) => new()
    {
        Poly = new() { (u0, z0), (u1, z0), (u1, z1), (u0, z1) },
        Relief = relief, Layer = layer, Rect = true,
    };

    /// <summary>한 줄의 돌 — 줄마다 6 m 주기 폭 열(자리 해시 · 합 6 m로 맞춤) · 줄마다 다른 어긋남 · 세로 줄눈 위아래 비틀기 · 모서리 깎기(4 ~ 8각).</summary>
    static void Stones(Spec s, double z0, double z1, long j, double uMin, double uMax, List<Item> items)
    {
        const double Cycle = 6.0;
        // 이 줄의 폭 열 — 6 m를 채우도록 뽑고 합을 6 m로 맞춘다
        var w = new List<double>();
        double sum = 0;
        for (int i = 0; sum < Cycle - 1e-9 && i < 64; i++) { double v = s.WMin + (s.WMax - s.WMin) * H01(j, i, 2); w.Add(v); sum += v; }
        double k = Cycle / sum;
        for (int i = 0; i < w.Count; i++) w[i] *= k;
        var edges = new double[w.Count + 1];
        for (int i = 0; i < w.Count; i++) edges[i + 1] = edges[i] + w[i];
        double shift = Cycle * H01(j, 0, 3);                     // 줄마다 어긋남
        double g = s.Joint / 2;
        long c0 = (long)Math.Floor((uMin - shift) / Cycle) - 1, c1 = (long)Math.Floor((uMax - shift) / Cycle) + 1;
        // 세로 줄눈 i(주기 c)의 위 · 아래 비틀기 — 이웃 두 돌이 같은 값을 쓴다
        double Jit(long c, int i, int top) { int ii = i == w.Count ? 0 : i; long cc = i == w.Count ? c + 1 : c; return s.Jitter * (2 * H01(j * 1000 + ii, cc, 4 + top) - 1); }
        for (long c = c0; c <= c1; c++)
            for (int i = 0; i < w.Count; i++)
            {
                double ua = shift + c * Cycle + edges[i], ub = shift + c * Cycle + edges[i + 1];
                if (ub < uMin - s.Jitter || ua > uMax + s.Jitter) continue;
                double bl = ua + Jit(c, i, 0) + g, br = ub + Jit(c, i + 1, 0) - g, tl = ua + Jit(c, i, 1) + g, tr = ub + Jit(c, i + 1, 1) - g;
                double zb = z0 + g, zt = z1 - g;
                if (br - bl < 0.05 || tr - tl < 0.05 || zt - zb < 0.05) continue;
                // 모서리 깎기 — 네 모서리마다 0 ~ Chamfer(셋에 하나는 안 깎음)
                double Ch(int corner) { double h = H01(j * 131 + c, i * 8 + corner, 5); return h < 0.33 ? 0 : s.Chamfer * h; }
                var quad = new (double U, double Z)[] { (bl, zb), (br, zb), (tr, zt), (tl, zt) };
                var poly = new List<(double U, double Z)>();
                for (int q = 0; q < 4; q++)
                {
                    var p = quad[q]; var prev = quad[(q + 3) % 4]; var next = quad[(q + 1) % 4];
                    double cv = Math.Min(Ch(q), 0.3 * Math.Min(Dist(p, prev), Dist(p, next)));
                    if (cv < 0.01) { poly.Add(p); continue; }
                    poly.Add(Toward(p, prev, cv));
                    poly.Add(Toward(p, next, cv));
                }
                items.Add(new Item { Poly = poly, Relief = s.Relief, Layer = s.Layer });
            }
    }

    static double Dist((double U, double Z) a, (double U, double Z) b) => Math.Sqrt((a.U - b.U) * (a.U - b.U) + (a.Z - b.Z) * (a.Z - b.Z));
    static (double U, double Z) Toward((double U, double Z) a, (double U, double Z) b, double d)
    {
        double L = Dist(a, b);
        return L < 1e-12 ? a : (a.U + (b.U - a.U) * d / L, a.Z + (b.Z - a.Z) * d / L);
    }

    // ── 면 판 · 자르기 ────────────────────────────────────────────────────────
    /// <summary>땅속으로 내려 그리는 깊이(m) — 지형과 무늬 아래 끝 사이 틈이 안 보이게.</summary>
    public const double Bury = 0.15;
    /// <summary>한 면 판으로 묶는 평면 허용(m) — 판의 아래 · 위 · 땅속 점이 판 평면에서 이만큼 안이면 같은 판.</summary>
    public const double SheetTol = 0.005;
    /// <summary>버리는 조각 — 넓이(㎡) · 가장 짧은 폭(m).</summary>
    public const double MinArea = 0.003, MinSide = 0.03;
    /// <summary>조각을 판 경계로 나누는 한계(m) — 무게중심 판으로 옮긴 꼭짓점이 제 자리 판 평면에서 이보다 멀면 나눈다(내밀기 ≥ 2 cm · 묻힘 10 cm 안에서 뜨거나 숨지 않게).</summary>
    public const double SplitTol = 0.015;

    /// <summary>면 판 — 한 덩어리(run) 안 이어진 정거장들을 덮는 평면 틀: 원점 O(첫 정거장 앞면 아래) · U = 면 안 수평(보는 사람 오른쪽) · V = 면 안 U에 수직(위) · W = 공기 쪽.
    /// 보이는 칸(틀 좌표 · 반시계) · 이 판 첫 정거장의 무늬 u · 판 평면에서 가장 먼 면 점(비틀림).</summary>
    public sealed class Sheet
    {
        public Point3 O;
        public (double X, double Y, double Z) U, V, W;
        public List<(double U, double V)> Region = new();
        public int Run, Sta0, Sta1;
        public double UG0, Twist;
    }

    /// <summary>자른 조각 — 판 번호 · 틀 좌표 다각형(반시계) · 직사각형이면 Rect(그대로 상자) · 겉 내밀기 · 묻힘 · 층.</summary>
    public sealed class Piece
    {
        public int Sheet;
        public List<(double U, double V)> Poly = new();
        public bool Rect;
        public double Relief, Embed;
        public string Layer = "";
    }

    public sealed class Result
    {
        public List<Sheet> Sheets = new();
        public List<Piece> Pieces = new();
        /// <summary>깐 조각 · 그대로 둔 조각 · 띠에 잘린 조각 · 버린 조각(작음) · 땅속 조각(앞 땅 + 3 cm 아래) · 판 경계로 나눈 조각 · 앵커 머리 뺀 것(머리가 잘리는 자리) · 자르기 실패.</summary>
        public int Items, Whole, Cut, Dropped, Low, Split, AnchorSkipped, ClipFail;
        /// <summary>판 묶음 비틀림 최대 · 판 경계를 넘어 옮긴 조각의 제 자리 판 평면 벗어남 최대(≤ <see cref="SplitTol"/>).</summary>
        public double TwistMax, DevMax;
        public string FirstFail = "";
    }

    static (double X, double Y, double Z) Sub(Point3 a, Point3 b) => (a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    static double Dot((double X, double Y, double Z) a, (double X, double Y, double Z) b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
    static (double X, double Y, double Z) Cross((double X, double Y, double Z) a, (double X, double Y, double Z) b) => (a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
    static (double X, double Y, double Z) Unit((double X, double Y, double Z) a) { double l = Math.Sqrt(Dot(a, a)); return l < 1e-15 ? (0, 0, 0) : (a.X / l, a.Y / l, a.Z / l); }

    /// <summary>정거장의 앞면 세 점 — 땅속(−Bury) · 앞 땅 · 꼭대기(덩어리와 같은 식 <see cref="WallSweep.World"/>).</summary>
    static (Point3 Q, Point3 B, Point3 T) FacePts(in WallSweep.Sta s)
    {
        var sh = s.Shape;
        double h = Math.Max(0, sh.ZTop);
        return (WallSweep.World(s, sh.FaceX0 - sh.FaceN * Bury, -Bury), WallSweep.World(s, sh.FaceX0, 0), WallSweep.World(s, sh.FaceX0 + sh.FaceN * h, h));
    }

    /// <summary>판 평면 — 원점 = i0 앞면 아래 · 면 안 두 방향 = (i1 아래 − i0 아래) · 기울기 방향 평균(i0 ~ i1) · W는 공기 쪽(흙 쪽 법선 반대).</summary>
    static bool Frame(IReadOnlyList<WallSweep.Sta> run, int i0, int i1, out Point3 o, out (double X, double Y, double Z) u, out (double X, double Y, double Z) v, out (double X, double Y, double Z) w)
    {
        o = FacePts(run[i0]).B;
        u = v = w = (0, 0, 0);
        var b1 = FacePts(run[i1]).B;
        var along = Sub(b1, o);
        double dx = 0, dy = 0, dz = 0, ax = 0, ay = 0;
        for (int k = i0; k <= i1; k++)
        {
            var (_, b, t) = FacePts(run[k]);
            var d = Sub(t, b);
            double fn = run[k].Shape.FaceN - run[k].Lean;          // 전단 뒤 앞면 기울기(검토 낮음 L6)
            if (d.Z < 1e-9) d = (run[k].Nx * fn * run[k].Scale, run[k].Ny * fn * run[k].Scale, 1);
            var du = Unit(d);
            dx += du.X; dy += du.Y; dz += du.Z;
            ax -= run[k].Nx; ay -= run[k].Ny;                      // 공기 쪽
        }
        // ★[검토 낮음 L6] 두 방향이 거의 나란하면(sin < 0.05) 법선이 엉뚱해진다 — 틀 없음
        var cr = Cross(along, (dx, dy, dz));
        double la = Math.Sqrt(Dot(along, along)), ld = Math.Sqrt(dx * dx + dy * dy + dz * dz);
        if (!(la > 1e-9) || !(ld > 1e-9) || Math.Sqrt(Dot(cr, cr)) < 0.05 * la * ld) return false;
        var n = Unit(cr);
        if (n.X * ax + n.Y * ay < 0) n = (-n.X, -n.Y, -n.Z);
        w = n;
        u = Unit(Cross((0, 0, 1), w));                             // 면 안 수평(보는 사람 오른쪽)
        if (Dot(u, u) < 0.5) return false;
        v = Cross(w, u);                                           // 면 안 위(V.z > 0)
        return v.Z > 1e-6;
    }

    static (double U, double V) Local(Point3 p, Point3 o, (double X, double Y, double Z) u, (double X, double Y, double Z) v) { var d = Sub(p, o); return (Dot(d, u), Dot(d, v)); }

    /// <summary>스윕 계획(옹벽 줄 하나)의 보이는 면에 무늬를 깔고 판으로 자른다.
    /// <para>무늬 u는 <b>줄 전체에서 이어진다</b> — 표 줄이 바뀌어 덩어리가 끊긴 자리 · 꺾인 점에서도 다시 0부터 시작하지 않는다(시작하면 그 자리마다 무늬가 어긋난 이음매가 생긴다).</para></summary>
    public static Result Lay(WallSweep.Plan plan, Spec spec)
    {
        var R = new Result();
        var gf = new GeometryFactory();
        // 1. 덩어리마다 판 나누기 · 무늬 u(줄 전체에서 이어서)
        var runSheets = new List<(int Ri, List<Sheet> Sheets, double[] Ug)>();
        double cur = 0;
        for (int ri = 0; ri < plan.Runs.Count; ri++)
        {
            var run = plan.Runs[ri];
            if (run.Count < 2) continue;
            // 앞에서부터 평면 허용 안에서 가장 길게
            var sheets = new List<Sheet>();
            int a = 0;
            while (a < run.Count - 1)
            {
                int bEnd = a + 1;
                Frame(run, a, bEnd, out var o, out var u, out var v, out var w);
                double twist = PlaneDev(run, a, bEnd, o, w);
                for (int c = a + 2; c < run.Count; c++)
                {
                    if (!Frame(run, a, c, out var o2, out var u2, out var v2, out var w2)) break;
                    double dev = PlaneDev(run, a, c, o2, w2);
                    if (dev > SheetTol) break;
                    bEnd = c; o = o2; u = u2; v = v2; w = w2; twist = dev;
                }
                if (Dot(w, w) > 0.5 && v.Z > 1e-6)
                    sheets.Add(new Sheet { O = o, U = u, V = v, W = w, Run = ri, Sta0 = a, Sta1 = bEnd, Twist = twist });
                R.TwistMax = Math.Max(R.TwistMax, twist);
                a = bEnd;
            }
            if (sheets.Count == 0) continue;
            // 무늬 u — 판마다 이어서(판 안은 U 방향 거리) · 덩어리가 바뀌어도 앞 덩어리 끝에서 이어서
            var ug = new double[run.Count];
            for (int k = 0; k < run.Count; k++) ug[k] = double.NaN;
            foreach (var sh in sheets)
            {
                sh.UG0 = double.IsNaN(ug[sh.Sta0]) ? cur : ug[sh.Sta0];
                for (int k = sh.Sta0; k <= sh.Sta1; k++) ug[k] = sh.UG0 + Local(FacePts(run[k]).B, sh.O, sh.U, sh.V).U;
                cur = ug[sh.Sta1];
                // 보이는 칸 — 땅속 점(앞에서 뒤로) + 꼭대기(뒤에서 앞으로)
                var reg = new List<(double U, double V)>();
                for (int k = sh.Sta0; k <= sh.Sta1; k++) reg.Add(Local(FacePts(run[k]).Q, sh.O, sh.U, sh.V));
                for (int k = sh.Sta1; k >= sh.Sta0; k--) reg.Add(Local(FacePts(run[k]).T, sh.O, sh.U, sh.V));
                if (Area(reg) < 0) reg.Reverse();
                sh.Region = reg;
            }
            runSheets.Add((ri, sheets, ug));
        }
        if (runSheets.Count == 0) return R;
        // 2. 줄 전체 보이는 띠(무늬 좌표 u, Z) — 덩어리마다 (u, 땅속 Z)를 앞에서 뒤로 + (u, 꼭대기 Z)를 뒤에서 앞으로 · 합집합
        //   ★[계획 2판 · 검토 높음 1] 자르기는 이 띠에 한 번 — 판(0.5 m 정거장)마다 자르면 조각이 다 잘리고 앵커 머리가 하나도 안 생긴다
        var prof = new List<(double U, double Bot, double Top)>();
        var bands = new List<Geometry>();
        var runBand = new Dictionary<int, Geometry>();
        foreach (var (ri, _, ug) in runSheets)
        {
            var run = plan.Runs[ri];
            var lo = new List<(double U, double Z)>(); var hi = new List<(double U, double Z)>();
            for (int k = 0; k < run.Count; k++)
            {
                if (double.IsNaN(ug[k])) continue;
                var (q, _, t) = FacePts(run[k]);
                prof.Add((ug[k], q.Z, t.Z));
                lo.Add((ug[k], q.Z)); hi.Add((ug[k], t.Z));
            }
            if (lo.Count < 2) continue;
            hi.Reverse();
            var ring = lo.Concat(hi).ToList();
            if (Math.Abs(Area(ring)) < 1e-9) continue;
            Geometry bg;
            try { var pg = gf.CreatePolygon(Ring(gf, ring)); bg = pg.IsValid ? pg : pg.Buffer(0); }
            catch (Exception ex) { R.ClipFail++; if (R.FirstFail.Length == 0) R.FirstFail = "띠: " + ex.Message; continue; }
            // ★[검토 낮음 5 · V52 그림] 꺾인 점에 닿는 덩어리 끝은 무늬를 내밀기 + 1 cm 물린다 — 그대로 두면 무늬 끝면(내밀기 두께)이 모서리 밖으로 톱니처럼 보인다
            bool AtCorner(WallSweep.Sta s) => plan.Corners.Any(c => Math.Abs(c.P.X - s.X) < 1e-9 && Math.Abs(c.P.Y - s.Y) < 1e-9);
            double uF = lo[0].U, uL = lo[^1].U, inset = spec.Relief + 0.01;
            bool cF = AtCorner(run[0]), cL = AtCorner(run[^1]);
            if (cF || cL)
            {
                double a0 = Math.Min(uF, uL), a1 = Math.Max(uF, uL);
                double i0 = (uF <= uL ? cF : cL) ? inset : 0, i1 = (uF <= uL ? cL : cF) ? inset : 0;
                if (a1 - i1 > a0 + i0 + 1e-6)
                {
                    var slab = gf.CreatePolygon(Ring(gf, new[] { (a0 + i0, lo.Min(p => p.Z) - 1), (a1 - i1, lo.Min(p => p.Z) - 1), (a1 - i1, hi.Max(p => p.Z) + 1), (a0 + i0, hi.Max(p => p.Z) + 1) }));
                    try { bg = bg.Intersection(slab); } catch { }
                }
            }
            runBand[ri] = bg;
            bands.Add(bg);                                    // 합집합(앵커 머리 판정)도 물린 띠로 — 머리가 모서리에서 잘리지 않게
        }
        if (bands.Count == 0) return R;
        // ★[검토 중간 M2] 합집합이 터지면 모아서 Buffer(0)
        Geometry band;
        try { band = bands.Count == 1 ? bands[0] : NetTopologySuite.Operation.Union.CascadedPolygonUnion.Union(bands); }
        catch { band = gf.BuildGeometry(bands).Buffer(0); }
        var bandPrep = NetTopologySuite.Geometries.Prepared.PreparedGeometryFactory.Prepare(band);
        // 앞 땅 높이(무늬 좌표 u) — 띠 아래(땅속) + Bury · 같은 u에 둘이면(덩어리 이음) 높은 쪽
        double ToeAt(double u)
        {
            double best = double.NaN;
            for (int k = 0; k + 1 < prof.Count; k++)
            {
                double ua = prof[k].U, ub = prof[k + 1].U;
                if ((u - ua) * (u - ub) > 1e-12) continue;
                double t = Math.Abs(ub - ua) < 1e-12 ? 0 : (u - ua) / (ub - ua);
                double z = prof[k].Bot + (prof[k + 1].Bot - prof[k].Bot) * t + Bury;
                if (double.IsNaN(best) || z > best) best = z;
            }
            return best;
        }
        // 판 — 무늬 u 범위로 줄 세움(줄 따라 u가 한쪽으로만 간다)
        // ★[2판 재검토 중간 ④] 덩어리 경계(꺾인 점 · 표 줄 계단)는 늘 자른다 — 조각은 그 덩어리 띠로 자르고 그 덩어리 판에만 놓는다(앞 덩어리 판에 놓이면 바깥 모서리 너머 허공으로 나간다)
        var slOf = new Dictionary<int, List<(Sheet S, int Si, double U0, double U1)>>();
        foreach (var (ri, sheets, ug) in runSheets)
        {
            var l = new List<(Sheet S, int Si, double U0, double U1)>();
            foreach (var sh in sheets)
            {
                double a0 = ug[sh.Sta0], a1 = ug[sh.Sta1];
                l.Add((sh, R.Sheets.Count, Math.Min(a0, a1), Math.Max(a0, a1)));
                R.Sheets.Add(sh);
            }
            l.Sort((x, y) => x.U0.CompareTo(y.U0));
            slOf[ri] = l;
        }
        static int SheetAt(List<(Sheet S, int Si, double U0, double U1)> sl, double u)
        {
            int lo = 0, hi = sl.Count - 1;
            if (u <= sl[0].U0) return 0;
            if (u >= sl[hi].U1) return hi;
            while (lo < hi) { int m = (lo + hi + 1) / 2; if (sl[m].U0 <= u) lo = m; else hi = m - 1; }
            return lo;
        }
        static (double U, double V) ToLocal(Sheet s, double u, double z) => (u - s.UG0, (z - s.O.Z) / s.V.Z);
        static Point3 ToWorld(Sheet s, double u, double z)
        {
            var (lu, lv) = ToLocal(s, u, z);
            return new Point3(s.O.X + s.U.X * lu + s.V.X * lv, s.O.Y + s.U.Y * lu + s.V.Y * lv, s.O.Z + s.U.Z * lu + s.V.Z * lv);
        }
        // ★[JACK 1027] 앵커 머리 — 머리 자신(0.56 □)이 조금도 안 잘릴 때만: 줄 전체 띠(꼭대기 · 꺾인 점 물림 · 벽 끝) 안 + 아래 변이 앞 땅 위(땅에 묻히면 잘린 것) ·
        //   덩어리 경계(표 줄 계단)에 걸쳐도 다 보이면 그린다(두 덩어리 판으로 나뉘어 붙음). 머리마다 한 번 판정(덩어리 둘에 깔려도 같은 답)
        var anchorOk = new Dictionary<(double, double), bool>();
        bool AnchorVisible((double U0, double Z0, double U1, double Z1) wo)
        {
            if (anchorOk.TryGetValue((wo.U0, wo.Z0), out bool ok)) return ok;
            var cell = gf.CreatePolygon(Ring(gf, new[] { (wo.U0, wo.Z0), (wo.U1, wo.Z0), (wo.U1, wo.Z1), (wo.U0, wo.Z1) }));
            ok = bandPrep.Contains(cell);
            if (ok)
            {
                var probes = new List<double> { wo.U0, (wo.U0 + wo.U1) / 2, wo.U1 };
                foreach (var p in prof) if (p.U > wo.U0 && p.U < wo.U1) probes.Add(p.U);
                foreach (double u in probes) { double toe = ToeAt(u); if (!double.IsNaN(toe) && wo.Z0 < toe - 1e-9) { ok = false; break; } }
            }
            anchorOk[(wo.U0, wo.Z0)] = ok;
            if (!ok) R.AnchorSkipped++;
            return ok;
        }
        // 3. 덩어리마다 — ★[검토 중간 M1] 그 덩어리 띠 상자에만 무늬를 깔고(줄 전체에 깔면 길고 기운 벽에서 조각이 수십만 · 덩어리마다 다 견주면 길이의 제곱) 미리 준비한 띠로 자른다.
        //   덩어리 경계를 넘는 조각은 양쪽 덩어리에서 같은 자리 해시로 같은 모양이 깔려 각자 제 몫만 남는다.
        //   조각은 무게중심 u의 (그 덩어리) 판으로 옮기되 꼭짓점이 제 자리 판 평면에서 SplitTol 넘게 벗어나면 판 경계로 나눈다.
        foreach (var (ri, _, _) in runSheets)
        {
            if (!runBand.TryGetValue(ri, out var rb) || rb.IsEmpty) continue;
            var rbPrep = NetTopologySuite.Geometries.Prepared.PreparedGeometryFactory.Prepare(rb);
            var re = rb.EnvelopeInternal;
            var sl = slOf[ri];
            var items = Items(spec, re.MinX, re.MaxX, re.MinY, re.MaxY);
            R.Items += items.Count;
            foreach (var it in items)
            {
                // ★[검토 중간 M2] 조각 하나의 예외가 그 줄 무늬를 통째로 없애지 않게 — 그 조각만 «자르기 실패»로 세고 넘어간다
                try
                {
                    double mnU = it.Poly.Min(p => p.U), mxU = it.Poly.Max(p => p.U), mnZ = it.Poly.Min(p => p.Z), mxZ = it.Poly.Max(p => p.Z);
                    if (mxU < re.MinX || mnU > re.MaxX || mxZ < re.MinY || mnZ > re.MaxY) continue;
                    if (it.WholeOf is { } wo && !AnchorVisible(wo)) continue;
                    var ip = gf.CreatePolygon(Ring(gf, it.Poly));
                    var parts = new List<(List<(double U, double Z)> Poly, bool Whole)>();
                    if (rbPrep.Contains(ip)) parts.Add((it.Poly, true));
                    else
                    {
                        if (!rbPrep.Intersects(ip)) continue;
                        foreach (var pl in Polys(rb.Intersection(ip))) parts.Add((pl, false));
                    }
                    foreach (var (poly, whole) in parts)
                    {
                        // 앵커 머리는 덩어리 경계에서 나뉜 가는 몫도 버리지 않는다(버리면 머리가 그만큼 잘려 보인다 — JACK 1027)
                        if (it.WholeOf == null ? !Keep(poly) : Math.Abs(Area(poly)) < 1e-6) { R.Dropped++; continue; }
                        double cu = poly.Average(p => p.U);
                        double toe = ToeAt(cu);
                        if (!double.IsNaN(toe) && poly.Max(p => p.Z) < toe + 0.03) { R.Low++; continue; }   // ★[검토 낮음 8] 앞 땅 위로 3 cm도 안 나오는 조각(땅속)
                        var sc = sl[SheetAt(sl, cu)];
                        double dev = 0;
                        foreach (var (u, z) in poly)
                        {
                            var sv = sl[SheetAt(sl, u)].S;
                            if (ReferenceEquals(sv, sc.S)) continue;
                            var w = ToWorld(sc.S, u, z);
                            dev = Math.Max(dev, Math.Abs(Dot(Sub(w, sv.O), sv.W)));
                        }
                        if (dev <= SplitTol)
                        {
                            R.DevMax = Math.Max(R.DevMax, dev);
                            if (whole) R.Whole++; else R.Cut++;
                            R.Pieces.Add(new Piece { Sheet = sc.Si, Poly = poly.Select(p => ToLocal(sc.S, p.U, p.Z)).ToList(), Rect = whole && it.Rect, Relief = it.Relief, Embed = spec.Embed, Layer = it.Layer });
                            continue;
                        }
                        // 판 경계로 나눈다 — 이 조각 u 범위에 걸친 판마다 그 판 u 띠와 겹친 몫(경계를 여러 번 넘으면 여러 번 — 재검토 중간 ⑤)
                        R.Split++;
                        var pp = gf.CreatePolygon(Ring(gf, poly));
                        double pu0 = poly.Min(p => p.U), pu1 = poly.Max(p => p.U), pz0 = poly.Min(p => p.Z) - 1, pz1 = poly.Max(p => p.Z) + 1;
                        for (int si = SheetAt(sl, pu0); si < sl.Count && sl[si].U0 <= pu1; si++)
                        {
                            var s = sl[si];
                            if (s.U1 < pu0) continue;
                            var slab = gf.CreatePolygon(Ring(gf, new[] { (s.U0, pz0), (s.U1, pz0), (s.U1, pz1), (s.U0, pz1) }));
                            foreach (var q in Polys(pp.Intersection(slab)))
                            {
                                if (it.WholeOf == null ? !Keep(q) : Math.Abs(Area(q)) < 1e-6) { R.Dropped++; continue; }
                                R.Cut++;
                                R.Pieces.Add(new Piece { Sheet = s.Si, Poly = q.Select(p => ToLocal(s.S, p.U, p.Z)).ToList(), Rect = false, Relief = it.Relief, Embed = spec.Embed, Layer = it.Layer });
                            }
                        }
                    }
                }
                catch (Exception ex) { R.ClipFail++; if (R.FirstFail.Length == 0) R.FirstFail = ex.Message; }
            }
        }
        return R;
    }

    /// <summary>나뉜 조각 꺾은선들(반시계 · 바깥 링) — 폴리곤 · 다중 폴리곤만.</summary>
    static IEnumerable<List<(double U, double Z)>> Polys(Geometry g)
    {
        for (int gi = 0; gi < g.NumGeometries; gi++)
        {
            if (g.GetGeometryN(gi) is not Polygon pg || pg.IsEmpty) continue;
            var cs = pg.ExteriorRing.Coordinates;
            var poly = new List<(double U, double Z)>();
            for (int q = 0; q + 1 < cs.Length; q++) poly.Add((cs[q].X, cs[q].Y));
            poly = Clean(poly);
            if (poly.Count < 3) continue;
            if (Area(poly) < 0) poly.Reverse();
            yield return poly;
        }
    }

    /// <summary>★[v106 · V52 석축 조각 1 eInvalidInput] 자른 다각형 다듬기 — 1 mm 안에 붙은 점 · 거의 일직선(꺾임 높이 0.1 mm 안) 점을 뺀다(리전이 못 받는 퇴화 변).</summary>
    public static List<(double U, double Z)> Clean(List<(double U, double Z)> p)
    {
        var a = new List<(double U, double Z)>();
        foreach (var q in p) if (a.Count == 0 || Dist(a[^1], q) > 1e-3) a.Add(q);
        while (a.Count > 1 && Dist(a[0], a[^1]) <= 1e-3) a.RemoveAt(a.Count - 1);
        bool changed = true;
        while (changed && a.Count > 3)
        {
            changed = false;
            for (int i = 0; i < a.Count && a.Count > 3; i++)
            {
                var pr = a[(i + a.Count - 1) % a.Count]; var q = a[i]; var nx = a[(i + 1) % a.Count];
                double L = Dist(pr, nx);
                double h = L < 1e-12 ? 0 : Math.Abs((q.U - pr.U) * (nx.Z - pr.Z) - (q.Z - pr.Z) * (nx.U - pr.U)) / L;
                if (h < 1e-4) { a.RemoveAt(i); changed = true; break; }
            }
        }
        return a;
    }

    /// <summary>남길 조각인가 — 넓이 ≥ <see cref="MinArea"/> · 상자 짧은 변 ≥ <see cref="MinSide"/>.</summary>
    static bool Keep(List<(double U, double Z)> p)
    {
        if (p.Count < 3 || Math.Abs(Area(p)) < MinArea) return false;
        return Math.Min(p.Max(q => q.U) - p.Min(q => q.U), p.Max(q => q.Z) - p.Min(q => q.Z)) >= MinSide;
    }

    static LinearRing Ring(GeometryFactory gf, IReadOnlyList<(double U, double V)> p)
    {
        var cs = new Coordinate[p.Count + 1];
        for (int i = 0; i < p.Count; i++) cs[i] = new Coordinate(p[i].U, p[i].V);
        cs[p.Count] = cs[0];
        return gf.CreateLinearRing(cs);
    }

    /// <summary>판 평면에서 가장 먼 앞면 점(땅속 · 아래 · 꼭대기 — i0 ~ i1).</summary>
    static double PlaneDev(IReadOnlyList<WallSweep.Sta> run, int i0, int i1, Point3 o, (double X, double Y, double Z) w)
    {
        double m = 0;
        for (int k = i0; k <= i1; k++)
        {
            var (q, b, t) = FacePts(run[k]);
            m = Math.Max(m, Math.Max(Math.Abs(Dot(Sub(q, o), w)), Math.Max(Math.Abs(Dot(Sub(b, o), w)), Math.Abs(Dot(Sub(t, o), w)))));
        }
        return m;
    }

    /// <summary>다각형 넓이(부호 — 반시계 +).</summary>
    public static double Area(IReadOnlyList<(double U, double Z)> p)
    {
        double a = 0;
        for (int i = 0; i < p.Count; i++) { var q = p[i]; var r = p[(i + 1) % p.Count]; a += q.U * r.Z - r.U * q.Z; }
        return a / 2;
    }
}
