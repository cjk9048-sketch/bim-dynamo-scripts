using System.Globalization;
using NetTopologySuite.Geometries;
using NetTopologySuite.Index.Strtree;
using NetTopologySuite.Operation.Buffer;
using NetTopologySuite.Operation.Distance;
using NetTopologySuite.Operation.Linemerge;
using Tri = DH.Grading.Core.WallDaylight.Tri;

namespace DH.Grading.Core;

/// <summary>★★[v117.0 0단계 · 계획 `2026-11-10_옹벽추가_안쪽소단.md` 6절] 새 모양(그은 선 L = 맨 바깥 벽 · 소단이 선과 나란히 안쪽으로 한 단씩 부지 바닥까지)의 <b>미리 재기</b>.
/// <para>짓지 않고 아무것도 안 바꾼다 — 지금 짓기(<see cref="Build"/>) · 합성은 그대로. 계획 1-2(바닥 Z0 · 기준 Z_k) · 1-3(방향 · 단 수 n · 바깥 벽 관문) ·
/// 1-6(측선 · 끝 각 θ · 날개벽) · 1-7 «자리»의 숫자만 낸다(0단계 M1 · M2 · M4 근거). 거절은 짓기 차례대로 모두 적고(<see cref="Surveyed.Codes"/>) 첫 것이 <see cref="Surveyed.Code"/>.</para></summary>
public static partial class WallByLine
{
    /// <summary>★[v117.0 0단계 · 계획 1-1] 하네스 고정물 전용 단 표 — 바닥 Zp ± k·H(대소단 없음 · 어디서나 같은 높이). 출하 흐름은 안 쓴다.</summary>
    public sealed class PadLevels : BenchLevels
    {
        readonly double _zp, _h, _mg; readonly bool _up; readonly int _n;
        /// <param name="maxGrad">★[1단계] 줄 안 이웃 점 높이 차 한도 기울기(기운 바닥 고정물 — 바닥 Z0 이어짐 관문이 쓴다 · 기본 0 = 0단계 그대로).</param>
        public PadLevels(double zp, double h, bool up, int tiers = 20, double maxGrad = 0) { _zp = zp; _h = h; _up = up; _n = Math.Max(1, tiers); _mg = Math.Max(0, maxGrad); }
        public override bool Up => _up;
        public override double MaxGrad => _mg;
        public override int K => _n;
        public override double TerraceW => 0;
        public override double BenchW => 0;
        public override int TierCount => _n;
        public override (double Rise, double H, bool Terrace) Tier(int k) { int i = Math.Clamp(k, 1, _n); return (i * _h, _h, false); }
        public override double Level(int k, double x, double y) => k < 1 || k > _n ? double.NaN : _zp + (_up ? 1 : -1) * k * _h;
        public override bool InBand(int k, double x, double y) => false;
        public override string Name => "바닥 고정";
    }

    /// <summary>★[v117.0 0단계] 미리 재기 결과 — 못 잰 칸은 NaN(지어내지 않음).</summary>
    public sealed class Surveyed
    {
        /// <summary>거절이 하나도 없음.</summary>
        public bool Ok;
        /// <summary>첫 거절(짓기 차례) 코드 · 글 · 자리 — 계획 1-9 코드(방향 · 단 표 · 바깥 벽 · 측선 · 자리 · 재료 · 원지반 · 정지면).</summary>
        public string Code = "", Why = ""; public (double X, double Y) At;
        /// <summary>걸린 거절 코드 · 글 전부(짓기 차례 · 나란함).</summary>
        public List<string> Codes = new(), Whys = new();
        /// <summary>방향 +1 절토 · −1 성토 · 0 섞임 · 2 같음(거절) — 표본 수(절 · 성 · 같음 0.1 mm 안).</summary>
        public int Dir = 2, NCut, NFill, NEq;
        /// <summary>L 표본 수 · 네모 거리 Gap 안이라 합친 수 · 단 높이(또는 바닥 높이)를 못 읽은 수.</summary>
        public int Samples, Merged, LevelNaN;
        /// <summary>R_out 쪽(왼쪽 + · 오른쪽 −) · 선 길이.</summary>
        public double Side, LineLen;
        /// <summary>단 수 n(선 전체 하나 · 섞임 = 0 · 못 셈 = −1) · 바깥 벽 한도 H_{n+1}(가장 많이 넘은 표본 쪽).</summary>
        public int Tiers = -1; public double TierH = double.NaN;
        /// <summary>★[1단계] 규칙(0 엄격 · 1 턱) · 엄격 규칙의 n(턱 규칙이 올리기 전).</summary>
        public int Rule, TiersStrict = -1;
        /// <summary>★[1단계] 턱 = max(0, −h) — 첫 소단이 선 위 원지반보다 높은(절토) · 낮은(성토) 양 최대 · 자리 · 1 mm 넘은 표본 수(엄격 규칙에서는 0단계 L8 꼴만).</summary>
        public double Ledge; public (double X, double Y) LedgeAt; public int LedgeN;
        public double GMin = double.NaN, GMax = double.NaN, Z0Min = double.NaN, Z0Max = double.NaN;
        /// <summary>바깥 벽 높이 h(s) = G′ − Z′_n(P_n) — 범위 · 가장 높은 자리 · 표본 · 넘은 표본 · 음수(−1 mm 아래) · 넘은 구간 수 · 가장 많이 넘은 구간(호 길이).</summary>
        public double OuterMin = double.NaN, OuterMax = double.NaN, OuterS = double.NaN; public (double X, double Y) OuterAt;
        public int OuterN, OuterOver, OuterNeg, OverRuns; public double OverS0 = double.NaN, OverS1 = double.NaN;
        /// <summary>M2 — 가장 안쪽 벽 발 줄(d = 깊이) 점에서 Z_1 − Z0(절토 꼴) 범위 · 그것 − H_1 범위 · |−H_1|이 가장 큰 점 · 그 점에서 바닥 가장 가까운 점까지 거리.</summary>
        public double Z1Min = double.NaN, Z1Max = double.NaN, DevMin = double.NaN, DevMax = double.NaN, DevNear = double.NaN; public (double X, double Y) DevAt;
        /// <summary>M12(계획 1-7 «바닥 높이 끊김») — L 이웃 표본의 Z0 차가 MaxGrad·거리 + 1 cm를 넘은 수 · 가장 큰 차 · 그 자리.</summary>
        public int Z0Jump; public double Z0JumpMax = double.NaN; public (double X, double Y) Z0JumpAt;
        /// <summary>안쪽 깊이 D_tot · 마구리 폭 vE · 자리(L ↔ 바닥 가장 짧은 거리) · 필요(D_tot + vE) · 자리 잰 L 위 점.</summary>
        public double Depth = double.NaN, VE = double.NaN, Room = double.NaN, Need = double.NaN; public (double X, double Y) RoomAt;
        /// <summary>선 끝 A · B · 바닥 위 가장 가까운 점 A′ · B′ · 측선 길이 · 끝 각 θ(°) · 날개벽 최대 |W − P0| · 그 자리.</summary>
        public (double X, double Y) A, B, A1, B1;
        public double LenA = double.NaN, LenB = double.NaN, ThetaA = double.NaN, ThetaB = double.NaN, WingA = double.NaN, WingB = double.NaN;
        public (double X, double Y) WingAtA, WingAtB;
        /// <summary>로그 한 줄.</summary>
        public string Text = "";
        public string DirName => Dir switch { 1 => "절토", -1 => "성토", 0 => "섞임", _ => "같음" };
    }

    /// <summary>★★[v117.0 0단계] 새 안쪽 모양을 짓지 않고 잰다 — <see cref="Build"/>와 같은 입력(<see cref="Input.LevelMode"/>와 상관없이 <see cref="Input.CutBench"/> · <see cref="Input.FillBench"/>를 단 표로 씀).
    /// <para>① L 표본 = 원지반 정확한 꺾은선(삼각망 변 건넘) ∪ 0.25 m ∪ L 꼭짓점(네모 거리 <see cref="CivilSafeMesh.Gap"/> 안 이웃 합침 · 꼭짓점 남김) ·
    /// ② 바닥 Z0(x, y) = 바닥 고리 위 가장 가까운 점의 합성 전 면(P0) · 안쪽 = R_out 쪽의 반대 ·
    /// ③ 방향 · 단 수 n = min_s kmax(s)(Z′_k(P_k) ≤ G′인 가장 큰 k · P_k = L + 안쪽 × f_{k+1}) · 바깥 벽 h(s) = G′ − Z′_n(P_n) &gt; H_{n+1}이면 거절(여유 없음) ·
    /// ⑤ 측선 A→A′ · B→B′(두 갈래 · 경계 밖 · L 지름 · 엇갈림 · 바닥 지남 · θ &lt; 15°) · 날개벽 = 측선 위 |W − P0| 최대 ·
    /// ⑧ 자리 = L ↔ 바닥 거리 ≥ D_tot + vE.</para>
    /// <para>날개벽은 측선 E 위 점에서 잰다(실제 마구리 줄 E′는 vE 안쪽 — 차이 ≤ vE × 옛 사면 기울기).</para></summary>
    public static Surveyed Survey(Input inp) => SurveyCore(inp, null);

    /// <summary>★[1단계] 미리 재기가 쓴 재료 — <see cref="BuildIn"/>가 같은 셈을 이어 쓴다(같은 표본 · 같은 n · 같은 줄 거리). 재기 끝에서만 채운다.</summary>
    sealed class InCtx
    {
        public PL Pl = null!; public double Ox, Oy, Side; public GeometryFactory Gf = null!;
        public LineString LineG = null!; public Polygon FloorP = null!, CutP = null!; public Polygon? NP;
        public Func<double, double, double> Z0 = null!;
        public Func<double, double, double?> P0w = null!;
        public Func<double, double> GAt = null!;
        public List<(double S, double Z)> Gp = null!;
        public List<(double S, double X, double Y, double G, double Z0, double D)> Smp = null!;
        public BenchLevels? Ob, ObC, ObF; public double Sg;
        public List<(double D, int K)> Rows = null!;
        public Func<BenchLevels, int, double> Fk = null!, Tk = null!;
        public double VE, VEps;
        public bool Filled;
    }

    static Surveyed SurveyCore(Input inp, InCtx? ctx)
    {
        var S = new Surveyed { Rule = (int)inp.InRule };
        var ci = CultureInfo.InvariantCulture;
        void No(string code, string why, (double X, double Y) at)
        {
            S.Codes.Add(code); S.Whys.Add(why);
            if (S.Code.Length == 0) { S.Code = code; S.Why = why; S.At = at; }
        }
        Surveyed Done() { S.Ok = S.Code.Length == 0; S.Text = SurveyText(S); return S; }
        var L = inp.Line;
        if (L == null || L.Count < 2) { No("재료", "선이 없습니다", (0, 0)); return Done(); }
        var plan = inp.Plan;
        if (plan == null || plan.Count < 3) { No("재료", "바닥(계획선 · 도로 띠)이 없습니다", (L[0].X, L[0].Y)); return Done(); }
        double ox = L[0].X, oy = L[0].Y;
        var gf = new GeometryFactory();
        var lc = new List<Coordinate>();
        foreach (var q in L) { var c = new Coordinate(q.X - ox, q.Y - oy); if (lc.Count == 0 || !lc[^1].Equals2D(c)) lc.Add(c); }
        if (lc.Count < 2) { No("재료", "선 점이 모자랍니다", (ox, oy)); return Done(); }
        var pl = new PL(lc.ToArray());
        S.LineLen = pl.Len;
        var lineG = gf.CreateLineString(pl.C);
        (double X, double Y) Wd(Coordinate c) => (c.X + ox, c.Y + oy);
        S.A = Wd(pl.C[0]); S.B = Wd(pl.C[^1]);
        var floorP = BoundaryCutPoly(gf, plan, ox, oy);
        if (floorP == null) { No("재료", "바닥(계획선 · 도로 띠)을 다각형으로 못 만들었습니다", S.A); return Done(); }
        var cutP = BoundaryCutPoly(gf, inp.CutRing, ox, oy);
        if (cutP == null) { No("재료", "잘라낼 자리(R_out)를 다각형으로 못 만들었습니다", S.A); return Done(); }
        var nP = BoundaryCutPoly(gf, inp.NewRing, ox, oy);

        // ② 안쪽 — R_out 쪽(Build :795-809와 같은 판정)의 반대
        double side;
        {
            int best = 0; double bl = -1;
            for (int i = 0; i + 1 < pl.C.Length; i++) { double l = pl.C[i].Distance(pl.C[i + 1]); if (l > bl) { bl = l; best = i; } }
            var a = pl.C[best]; var b = pl.C[best + 1];
            double mx = (a.X + b.X) / 2, my = (a.Y + b.Y) / 2, tx = (b.X - a.X) / bl, ty = (b.Y - a.Y) / bl, e = Math.Min(1e-5, bl * 0.01);
            bool left = cutP.Contains(gf.CreatePoint(new Coordinate(mx - ty * e, my + tx * e)));
            bool right = cutP.Contains(gf.CreatePoint(new Coordinate(mx + ty * e, my - tx * e)));
            if (left == right) { No("재료", "선의 어느 쪽이 잘라낼 자리인지 못 가렸습니다", S.A); return Done(); }
            side = left ? 1 : -1;
        }
        S.Side = side;
        // 선 위 s에서 안쪽으로 d 민 점(세계 좌표)
        (double X, double Y) In(double s, double d) { var c = pl.At(s); var (tx, ty) = pl.Tan(s); return (c.X + ty * side * d + ox, c.Y - tx * side * d + oy); }

        // 바닥 높이 Z0 = 바닥 고리 위 가장 가까운 점의 P0(못 읽으면 바닥 안쪽으로 1 mm 민 자리)
        var p0Look = inp.P0Tris != null && inp.P0Tris.Count > 0 ? TriLookup(inp.P0Tris) : null;
        double? P0w(double x, double y) => p0Look?.Invoke(x, y) ?? inp.P0(x, y);
        int pn = plan.Count; if (pn >= 2 && plan[0].X == plan[^1].X && plan[0].Y == plan[^1].Y) pn--;
        double pa = 0;
        for (int i = 0; i < pn; i++) { var a = plan[i]; var b = plan[(i + 1) % pn]; pa += (a.X - ox) * (b.Y - oy) - (b.X - ox) * (a.Y - oy); }
        double Z0(double x, double y)
        {
            var q = Nearest(plan, x, y);
            var z = P0w(q.X, q.Y);
            if (z == null)
            {
                var a = plan[q.J]; var b = plan[(q.J + 1) % pn];
                double ex = b.X - a.X, ey = b.Y - a.Y, l = Math.Sqrt(ex * ex + ey * ey), sa = pa >= 0 ? 1 : -1;   // 반시계 고리 = 안쪽이 왼쪽
                if (l > 1e-12) z = P0w(q.X - ey / l * 1e-3 * sa, q.Y + ex / l * 1e-3 * sa);
            }
            return z ?? double.NaN;
        }

        // ① L 위 원지반 — 삼각망 변 건넘까지 정확한 꺾은선
        var gp = new List<(double S, double Z)>();
        for (int i = 0; i + 1 < pl.C.Length; i++)
        {
            var a = new Point3(pl.C[i].X + ox, pl.C[i].Y + oy, 0); var b = new Point3(pl.C[i + 1].X + ox, pl.C[i + 1].Y + oy, 0);
            var pr = ProfileOnTris(a, b, inp.GroundTris, inp.Ground, inp.Grid);
            if (pr == null) { No("원지반", string.Format(ci, "선 위 원지반을 못 읽었습니다 ({0:F1}, {1:F1}) — 측량 범위 밖?", a.X, a.Y), (a.X, a.Y)); return Done(); }
            double segL = pl.Cum[i + 1] - pl.Cum[i];
            foreach (var q in pr)
            {
                double t = segL < 1e-15 ? 0 : Math.Sqrt((q.X - a.X) * (q.X - a.X) + (q.Y - a.Y) * (q.Y - a.Y)) / segL;
                double s = pl.Cum[i] + t * segL;
                if (gp.Count == 0 || s - gp[^1].S > 1e-9) gp.Add((s, q.Z)); else gp[^1] = (gp[^1].S, q.Z);
            }
        }
        double GAt(double s)
        {
            if (s <= gp[0].S) return gp[0].Z;
            if (s >= gp[^1].S) return gp[^1].Z;
            int lo = 0, hi = gp.Count - 1;
            while (hi - lo > 1) { int m = (lo + hi) / 2; if (gp[m].S <= s) lo = m; else hi = m; }
            double t = (s - gp[lo].S) / Math.Max(1e-15, gp[hi].S - gp[lo].S);
            return gp[lo].Z + (gp[hi].Z - gp[lo].Z) * t;
        }
        // 표본 — 원지반 꺾임 ∪ 0.25 m ∪ L 꼭짓점 · 네모 거리 Gap 안 이웃은 합침(꼭짓점이 이김)
        var cand = new List<(double S, bool Keep)>();
        foreach (var q in gp) cand.Add((q.S, false));
        for (int i = 0; i < pl.C.Length; i++) cand.Add((pl.Cum[i], true));
        for (double s = 0.25; s < pl.Len; s += 0.25) cand.Add((s, false));
        cand.Sort((u, v) => u.S != v.S ? u.S.CompareTo(v.S) : v.Keep.CompareTo(u.Keep));
        var ss = new List<(double S, bool Keep)>();
        foreach (var (s, keep) in cand)
        {
            if (ss.Count > 0)
            {
                var (s0, k0) = ss[^1];
                if (s - s0 < 1e-9) { if (keep && !k0) ss[^1] = (s0, true); continue; }
                var c0 = pl.At(s0); var c1 = pl.At(s);
                if (Math.Max(Math.Abs(c0.X - c1.X), Math.Abs(c0.Y - c1.Y)) < CivilSafeMesh.Gap)
                {
                    S.Merged++;
                    if (keep && !k0) ss[^1] = (s, true);
                    continue;
                }
            }
            ss.Add((s, keep));
        }
        var smp = new List<(double S, double X, double Y, double G, double Z0, double D)>();
        foreach (var (s, _) in ss)
        {
            var c = pl.At(s); double x = c.X + ox, y = c.Y + oy;
            double g = GAt(s), z0 = Z0(x, y);
            if (double.IsNaN(z0)) { No("정지면", string.Format(ci, "바닥 높이(합성 전 면)를 못 읽었습니다 ({0:F1}, {1:F1})", x, y), (x, y)); return Done(); }
            smp.Add((s, x, y, g, z0, g - z0));
        }
        S.Samples = smp.Count;
        S.GMin = smp.Min(q => q.G); S.GMax = smp.Max(q => q.G); S.Z0Min = smp.Min(q => q.Z0); S.Z0Max = smp.Max(q => q.Z0);

        // ③ 방향(δ = G − Z0 · 0.1 mm)
        const double Eps0 = 1e-4;
        S.NCut = smp.Count(q => q.D > Eps0); S.NFill = smp.Count(q => q.D < -Eps0); S.NEq = smp.Count - S.NCut - S.NFill;
        S.Dir = S.NCut > 0 && S.NFill > 0 ? 0 : S.NCut > 0 ? 1 : S.NFill > 0 ? -1 : 2;
        if (S.Dir == 2) No("방향", "선 위 원지반이 부지 바닥 높이와 같습니다(0.1 mm 안) — 옹벽이 설 자리가 없습니다", S.A);
        BenchLevels? obC = inp.CutBench, obF = inp.FillBench;
        bool needC = S.Dir == 1 || S.Dir == 0, needF = S.Dir == -1 || S.Dir == 0;
        bool badC = needC && (obC == null || obC.TierCount < 1), badF = needF && (obF == null || obF.TierCount < 1);
        if (badC) No("단 표", $"절토 소단 기준 높이를 못 읽습니다({(inp.CutBenchWhy.Length > 0 ? inp.CutBenchWhy : "단 표 없음")}) — 정지면을 다시 만든 뒤 하세요", S.A);
        if (badF) No("단 표", $"성토 소단 기준 높이를 못 읽습니다({(inp.FillBenchWhy.Length > 0 ? inp.FillBenchWhy : "단 표 없음")}) — 정지면을 다시 만든 뒤 하세요", S.A);
        bool tables = S.Dir != 2 && !badC && !badF;

        double Fk(BenchLevels ob, int k) => Math.Max(ob.Tier(k).H * Math.Max(0, inp.SlopeW), Math.Max(1e-4, inp.FaceRun));
        double Tk(BenchLevels ob, int k) { double t = ob.Tier(k).Terrace ? ob.TerraceW : Math.Max(0, ob.Up ? inp.Tc : inp.Tf); return t < 1e-3 ? 0 : t; }
        double vEps = Math.Max(1e-3, inp.FaceRun);
        // 줄(선에서 거리 · 높이 번호 −1 = L 위 원지반 · 0 = Z0 · k = Z_k) — 날개벽 셈
        var rows = new List<(double D, int K)>();
        BenchLevels? obRow = null;
        double tShow = 0;
        if (tables)
        {
            var outer = new List<(double S, double X, double Y, double H, double Lim, double ZNext)>();
            var dev = new List<(double Dz, double Dv, double X, double Y)>();
            if (S.Dir != 0)
            {
                double sg = S.Dir; var ob = sg > 0 ? obC! : obF!; obRow = ob;
                double Zc(int k, (double X, double Y) p) => k == 0 ? sg * Z0(p.X, p.Y) : sg * ob.Level(k, p.X, p.Y);
                // 단 수 n = 선 어디서도 소단이 원지반 위로 뜨지 않는 가장 높은 기준(절토 꼴)
                int n = int.MaxValue;
                foreach (var q in smp)
                {
                    double g = sg * q.G; int k = 0;
                    while (k < ob.TierCount)
                    {
                        double z = Zc(k + 1, In(q.S, Fk(ob, k + 2)));
                        if (double.IsNaN(z)) { S.LevelNaN++; break; }
                        if (z <= g) k++; else break;
                    }
                    n = Math.Min(n, k);
                }
                S.TiersStrict = n;
                // 바깥 벽 표본(단 수 nn · 높이 못 읽은 수)
                (List<(double S, double X, double Y, double H, double Lim, double ZNext)> O, int Nan) OuterOf(int nn)
                {
                    var o = new List<(double S, double X, double Y, double H, double Lim, double ZNext)>(); int nan = 0;
                    double lim = ob.Tier(nn + 1).H, fo = Fk(ob, nn + 1);
                    foreach (var q in smp)
                    {
                        var p = In(q.S, fo);
                        double zn = Zc(nn, p);
                        if (double.IsNaN(zn)) { nan++; continue; }
                        o.Add((q.S, q.X, q.Y, sg * q.G - zn, lim, sg * (zn + lim)));
                    }
                    return (o, nan);
                }
                var (o0, nan0) = OuterOf(n);
                // ★[1단계 · 턱 규칙] 바깥 벽이 어디서든 한도를 넘으면 n을 올린다(땅이 낮은 자리는 첫 소단이 원지반보다 높게 · 턱) — 바깥 벽이 다 들어오는 가장 낮은 n
                if (inp.InRule == InwardRule.Ledge)
                    while (o0.Count > 0 && o0.Any(o => o.H > o.Lim) && n < ob.TierCount) { n++; (o0, nan0) = OuterOf(n); }
                outer.AddRange(o0); S.LevelNaN += nan0;
                S.Tiers = n;
                double fo = Fk(ob, n + 1);
                double d = fo; rows.Add((0, -1)); rows.Add((fo, n));
                for (int k = n; k >= 1; k--)
                {
                    double t = Tk(ob, k);
                    // ★[1단계 · 계획 1-4] 비틀린 단 표(도로 MidBench)는 소단 가운데 줄을 0.5 m 간격으로(옛 MidRows 그대로 — 소단 1 m 이하는 가운데 한 줄) · 부지 링은 안 넣음
                    if (t >= 0.2 && ob.MidBench) { int m = Math.Max(2, (int)Math.Ceiling(t / 0.5 - 1e-9)); for (int q = 1; q < m; q++) rows.Add((d + (m == 2 ? t / 2 : t * q / m), k)); }
                    if (t > 0) { d += t; rows.Add((d, k)); }
                    d += Fk(ob, k); rows.Add((d, k - 1));
                }
                S.Depth = d;
                double ve = vEps; for (int k = 1; k <= n + 1; k++) ve = Math.Max(ve, Fk(ob, k));
                S.VE = ve;
                tShow = Tk(ob, 1);
                // M2 — 가장 안쪽 벽 발 줄(d = 깊이)에서 Z_1 − Z0
                double h1 = ob.Tier(1).H;
                foreach (var q in smp)
                {
                    var p = In(q.S, S.Depth);
                    double z1 = ob.Level(1, p.X, p.Y), z0 = Z0(p.X, p.Y);
                    if (!double.IsFinite(z1) || !double.IsFinite(z0)) { S.LevelNaN++; continue; }
                    dev.Add((sg * (z1 - z0), sg * (z1 - z0) - h1, p.X, p.Y));
                }
            }
            else
            {
                // 섞임 — n = 0 · 벽 하나(바닥 Z0에 절토 쪽은 위에서 · 성토 쪽은 아래에서) · 표본 쪽마다 그 쪽 H_1이 한도
                S.Tiers = 0; S.TiersStrict = 0;
                double fo = Math.Max(Fk(obC!, 1), Fk(obF!, 1));
                foreach (var q in smp)
                {
                    var p = In(q.S, fo);
                    double z0 = Z0(p.X, p.Y);
                    if (double.IsNaN(z0)) { S.LevelNaN++; continue; }
                    int sgi = q.D > Eps0 ? 1 : q.D < -Eps0 ? -1 : 0;
                    var ob = sgi >= 0 ? obC! : obF!;
                    double lim = ob.Tier(1).H;
                    outer.Add((q.S, q.X, q.Y, sgi == 0 ? Math.Abs(q.G - z0) : sgi * (q.G - z0), lim, z0 + (sgi >= 0 ? lim : -lim)));
                    if (sgi != 0)
                    {
                        double z1 = ob.Level(1, p.X, p.Y);
                        if (double.IsFinite(z1)) dev.Add((sgi * (z1 - z0), sgi * (z1 - z0) - lim, p.X, p.Y)); else S.LevelNaN++;
                    }
                }
                rows.Add((0, -1)); rows.Add((fo, 0));
                S.Depth = fo; S.VE = Math.Max(vEps, fo);
            }
            if (dev.Count > 0)
            {
                S.Z1Min = dev.Min(v => v.Dz); S.Z1Max = dev.Max(v => v.Dz); S.DevMin = dev.Min(v => v.Dv); S.DevMax = dev.Max(v => v.Dv);
                var dw = dev.OrderByDescending(v => Math.Abs(v.Dv)).First();
                S.DevAt = (dw.X, dw.Y); S.DevNear = Nearest(plan, dw.X, dw.Y).D;
            }
            // 바깥 벽 관문(계획 1-3 ⑤ — 정확한 꺾은선 표본 · 여유 없음)
            if (outer.Count > 0)
            {
                S.OuterN = outer.Count;
                int im = 0, imin = 0, iw = 0;
                for (int i = 1; i < outer.Count; i++)
                {
                    if (outer[i].H > outer[im].H) im = i;
                    if (outer[i].H < outer[imin].H) imin = i;
                    if (outer[i].H - outer[i].Lim > outer[iw].H - outer[iw].Lim) iw = i;
                }
                S.OuterMax = outer[im].H; S.OuterMin = outer[imin].H; S.OuterAt = (outer[im].X, outer[im].Y); S.OuterS = outer[im].S; S.TierH = outer[im].Lim;
                S.OuterNeg = outer.Count(o => o.H < -1e-3);
                var over = outer.Select(o => o.H > o.Lim).ToList();
                S.OuterOver = over.Count(v => v);
                for (int i = 0; i < over.Count; i++) if (over[i] && (i == 0 || !over[i - 1])) S.OverRuns++;
                if (S.OuterOver > 0)
                {
                    int a = iw, b = iw;
                    while (a > 0 && over[a - 1]) a--;
                    while (b + 1 < over.Count && over[b + 1]) b++;
                    S.OverS0 = outer[a].S; S.OverS1 = outer[b].S; S.TierH = outer[iw].Lim;
                    var w = outer[iw];
                    No("바깥 벽", string.Format(ci, "바깥 벽 {0:F2} m — 단높이 {1:F2} m를 넘습니다 ({2:F1}, {3:F1}) · 원지반이 {4:F2} 위로 올라가는 구간 {5:F1}~{6:F1} m — 선을 원지반 등고선을 따라 옮기거나(바깥 벽 높이가 고르게) 선을 나눠 그리세요",
                        w.H, w.Lim, w.X, w.Y, w.ZNext, S.OverS0, S.OverS1), (w.X, w.Y));
                }
                // ★[1단계] 턱 = max(0, −h) — 엄격 규칙은 정의상 0(0단계 L8: 바닥 높이 0.1 mm 안 · 기운 바닥만 · OuterNeg) · 턱 규칙은 한도를 넘으면 거절
                foreach (var o in outer)
                {
                    double lg = -o.H;
                    if (lg > S.Ledge) { S.Ledge = lg; S.LedgeAt = (o.X, o.Y); }
                    if (lg > 1e-3) S.LedgeN++;
                }
                if (inp.InRule == InwardRule.Ledge && S.Dir != 0 && S.OuterOver == 0 && S.Ledge > inp.LedgeMax)
                    No("턱", string.Format(ci, "첫 소단이 선 위 원지반보다 {0:F2} m {1} — 턱 한도 {2:F2} m ({3:F1}, {4:F1}) · 바깥 벽을 단높이 안으로 맞추려면 {5}단(엄격 규칙 {6}단) — 선을 원지반 등고선을 따라 옮기거나 선을 나눠 그리세요",
                        S.Ledge, S.Dir > 0 ? "높습니다" : "낮습니다", inp.LedgeMax, S.LedgeAt.X, S.LedgeAt.Y, S.Tiers, S.TiersStrict), S.LedgeAt);
            }
        }

        // M12 바닥 높이 끊김(계획 1-7 · 여기서는 재기만) — L 이웃 표본 Z0 차 > MaxGrad·거리 + 1 cm
        {
            double mg = Math.Max(needC && obC != null ? obC.MaxGrad : 0, needF && obF != null ? obF.MaxGrad : 0);
            for (int i = 1; i < smp.Count; i++)
            {
                double dist = Math.Sqrt((smp[i].X - smp[i - 1].X) * (smp[i].X - smp[i - 1].X) + (smp[i].Y - smp[i - 1].Y) * (smp[i].Y - smp[i - 1].Y)), dz = Math.Abs(smp[i].Z0 - smp[i - 1].Z0);
                if (dz <= mg * dist + 0.01) continue;
                S.Z0Jump++;
                if (double.IsNaN(S.Z0JumpMax) || dz > S.Z0JumpMax) { S.Z0JumpMax = dz; S.Z0JumpAt = (smp[i].X, smp[i].Y); }
            }
        }

        // ⑤ 측선 — A → A′ · B → B′(바닥 고리 위 2D 가장 가까운 점)
        var qa = Nearest(plan, S.A.X, S.A.Y); var qb = Nearest(plan, S.B.X, S.B.Y);
        S.A1 = (qa.X, qa.Y); S.B1 = (qb.X, qb.Y); S.LenA = qa.D; S.LenB = qb.D;
        LineString? Seg2((double X, double Y) p, (double X, double Y) q) =>
            Math.Abs(p.X - q.X) + Math.Abs(p.Y - q.Y) < 1e-12 ? null : gf.CreateLineString(new[] { new Coordinate(p.X - ox, p.Y - oy), new Coordinate(q.X - ox, q.Y - oy) });
        var eA = Seg2(S.A, S.A1); var eB = Seg2(S.B, S.B1);
        foreach (var (nm, p, e) in new[] { ("A", S.A, eA), ("B", S.B, eB) })
        {
            if (TwoWay(plan, p.X, p.Y)) No("측선", $"선 끝 {nm}에서 부지 바닥까지 측선 방향이 두 갈래 — 선 끝을 조금 옮기세요", p);
            if (e == null) { No("측선", $"선 끝 {nm}이 부지 바닥 위입니다 — 선 끝을 옮기세요", p); continue; }
            string F(string what, (double X, double Y) at) => string.Format(ci, "선 끝 {0}에서 부지 바닥까지 곧은 측선이 {1} ({2:F1}, {3:F1}) — 선 끝을 옮기세요", nm, what, at.X, at.Y);
            if (nP != null)
            {
                var outN = e.Difference(nP);
                if (outN.Length > 1e-6) No("측선", F("경계 밖으로 나갑니다", Wd(outN.Centroid.Coordinate)), Wd(outN.Centroid.Coordinate));
            }
            var pc = new Coordinate(p.X - ox, p.Y - oy);
            var hit = e.Intersection(lineG).Coordinates.FirstOrDefault(c => c.Distance(pc) > 1e-6);
            if (hit != null) No("측선", F("선을 지릅니다", Wd(hit)), Wd(hit));
            var inF = e.Intersection(floorP);
            if (inF.Length > 1e-6) No("측선", F("부지 바닥을 지납니다", Wd(inF.Centroid.Coordinate)), Wd(inF.Centroid.Coordinate));
        }
        if (eA != null && eB != null && eA.Intersects(eB))
        {
            var a1 = new Coordinate(S.A1.X - ox, S.A1.Y - oy);
            bool sameEnd = Math.Abs(S.A1.X - S.B1.X) + Math.Abs(S.A1.Y - S.B1.Y) < 1e-6;
            var bad = eA.Intersection(eB).Coordinates.FirstOrDefault(c => !sameEnd || c.Distance(a1) > 1e-6);
            if (bad != null) No("측선", string.Format(ci, "선 끝 A · B의 곧은 측선이 서로 엇갈립니다 ({0:F1}, {1:F1}) — 선 끝을 옮기세요", bad.X + ox, bad.Y + oy), Wd(bad));
        }
        // 끝 각 θ — 바꿀 자리 Z 안에서 L 첫(끝) 토막과 측선 사이(A: 안쪽 = R_out 반대 · B: 거꾸로 걸으니 부호 반대)
        static double Ang(double cr, double dt) { double a = Math.Atan2(cr, dt); if (a < 0) a += 2 * Math.PI; return a * 180 / Math.PI; }
        if (eA != null)
        {
            double ux = pl.C[1].X - pl.C[0].X, uy = pl.C[1].Y - pl.C[0].Y, vx = S.A1.X - S.A.X, vy = S.A1.Y - S.A.Y;
            S.ThetaA = Ang(-side * (ux * vy - uy * vx), ux * vx + uy * vy);
        }
        if (eB != null)
        {
            double ux = pl.C[^2].X - pl.C[^1].X, uy = pl.C[^2].Y - pl.C[^1].Y, vx = S.B1.X - S.B.X, vy = S.B1.Y - S.B.Y;
            S.ThetaB = Ang(side * (ux * vy - uy * vx), ux * vx + uy * vy);
        }
        foreach (var (nm, th, p) in new[] { ("A", S.ThetaA, S.A), ("B", S.ThetaB, S.B) })
            if (double.IsFinite(th) && th < 15)
                No("측선", string.Format(ci, "선 끝 {0}이 부지 쪽으로 너무 곧게 향함(끝 각 {1:F0}° < 15°) ({2:F1}, {3:F1}) — 끝을 경계 따라 꺾어 그리세요", nm, th, p.X, p.Y), p);
        // 날개벽 — 측선 위 |W − P0| 최대(W = 그 자리 줄 높이 · 깊이 너머 = Z0 평평한 띠)
        if (rows.Count >= 2)
        {
            double ZRow(int k, double x, double y, double sNear) => k < 0 ? GAt(sNear) : k == 0 ? Z0(x, y) : obRow!.Level(k, x, y);
            double WallZ(double x, double y)
            {
                var (sN, d, _) = pl.Near(x - ox, y - oy);
                if (d >= rows[^1].D) return Z0(x, y);
                int i = 0; while (i + 2 < rows.Count && d > rows[i + 1].D) i++;
                double z0 = ZRow(rows[i].K, x, y, sN), z1 = ZRow(rows[i + 1].K, x, y, sN), w = rows[i + 1].D - rows[i].D;
                return w < 1e-12 ? z1 : z0 + (z1 - z0) * Math.Clamp((d - rows[i].D) / w, 0, 1);
            }
            (double W, (double X, double Y) At) Wing((double X, double Y) p, (double X, double Y) q)
            {
                double dx = q.X - p.X, dy = q.Y - p.Y, len = Math.Sqrt(dx * dx + dy * dy);
                if (len < 1e-9) return (double.NaN, p);
                (double X, double Y) At(double t) => (p.X + dx * t, p.Y + dy * t);
                int RowOf(double t) { var (x, y) = At(t); double d = pl.Near(x - ox, y - oy).D; int i = 0; while (i < rows.Count && rows[i].D <= d) i++; return i; }
                var ts = new List<double> { 0, 1 };
                var pr = ProfileOnTris(new Point3(p.X, p.Y, 0), new Point3(q.X, q.Y, 0), inp.P0Tris, P0w, inp.Grid);
                if (pr != null) foreach (var r in pr) ts.Add(Math.Clamp(Math.Sqrt((r.X - p.X) * (r.X - p.X) + (r.Y - p.Y) * (r.Y - p.Y)) / len, 0, 1));
                for (double s = 0.25; s < len; s += 0.25) ts.Add(s / len);
                ts.Sort();
                // 줄 건넘(꺾임이 줄 위에 있다) — 이웃 표본 사이 이분법(1 µm)
                var extra = new List<double>();
                void Cross(double a, double b, int ia, int ib, int depth)
                {
                    if (ia == ib) return;
                    if ((b - a) * len < 1e-6 || depth > 60) { extra.Add(a); extra.Add(b); return; }
                    double m = (a + b) / 2; int imd = RowOf(m);
                    Cross(a, m, ia, imd, depth + 1); Cross(m, b, imd, ib, depth + 1);
                }
                for (int i = 0; i + 1 < ts.Count; i++) if (ts[i + 1] - ts[i] > 1e-12) Cross(ts[i], ts[i + 1], RowOf(ts[i]), RowOf(ts[i + 1]), 0);
                double mx = double.NaN; (double X, double Y) at = p;
                foreach (double t in ts.Concat(extra))
                {
                    var (x, y) = At(t);
                    double w = WallZ(x, y); var z = P0w(x, y);
                    if (!double.IsFinite(w) || z == null) { S.LevelNaN++; continue; }
                    double h = Math.Abs(w - z.Value);
                    if (double.IsNaN(mx) || h > mx) { mx = h; at = (x, y); }
                }
                return (mx, at);
            }
            (S.WingA, S.WingAtA) = Wing(S.A, S.A1);
            (S.WingB, S.WingAtB) = Wing(S.B, S.B1);
        }

        // ⑧ 자리 — L ↔ 바닥 가장 짧은 거리 ≥ 깊이 + vE
        var dop = new DistanceOp(lineG, floorP);
        S.Room = dop.Distance(); S.RoomAt = Wd(dop.NearestPoints()[0]);
        if (tables)
        {
            S.Need = S.Depth + S.VE;
            if (S.Room < S.Need)
                No("자리", string.Format(ci, "자리 모자람 — 선에서 부지 바닥까지 {0:F2} m · 필요 {1:F2} m({2}단 · 소단 {3:F1} m) ({4:F1}, {5:F1}) — 선을 바깥으로 옮기거나 소단을 줄이세요",
                    S.Room, S.Need, S.Tiers, tShow, S.RoomAt.X, S.RoomAt.Y), S.RoomAt);
        }
        // ★[1단계] 짓기가 이어 쓸 재료(같은 셈)
        if (ctx != null && tables && rows.Count >= 2)
        {
            ctx.Pl = pl; ctx.Ox = ox; ctx.Oy = oy; ctx.Side = side; ctx.Gf = gf; ctx.LineG = lineG; ctx.FloorP = floorP; ctx.CutP = cutP; ctx.NP = nP;
            ctx.Z0 = Z0; ctx.P0w = P0w; ctx.GAt = GAt; ctx.Gp = gp; ctx.Smp = smp;
            ctx.Ob = obRow; ctx.ObC = obC; ctx.ObF = obF; ctx.Sg = S.Dir;
            ctx.Rows = rows; ctx.Fk = Fk; ctx.Tk = Tk; ctx.VE = S.VE; ctx.VEps = vEps; ctx.Filled = true;
        }
        return Done();
    }

    /// <summary>★[1단계 · 검토 M3] 넘기는 선(L · 줄 · E · E′) 위 이웃 점 간격 하한 — 세모 높이 ≤ 가장 짧은 변이라 줄 간격(≥ FaceRun)만으로는 바늘을 못 막는다.
    /// 지키는 점(A · B · A* · B* · A′ · B′ · A″ · B″ · L 꼭짓점 · 줄 끝)끼리 이보다 가까우면 옮기지 않고 <see cref="Built.ShortEdges"/>에 센다.</summary>
    const double MinSp = 0.25e-3;

    /// <summary>★★★[v117.0 1단계 · 계획 `2026-11-10_옹벽추가_안쪽소단.md` 1-2~1-11 + 검토 M3 · M4 · M6 · L8] 새 모양 짓기 — 그은 선 L = 맨 바깥 벽 · 소단이 선과 나란히 안쪽으로 부지 바닥까지.
    /// <code>
    ///   R_out(원지반 G) │L━ 바깥 벽 ━┓ f_o ── Z_n 소단 T ──┓ f ── … ── Z_1 ──┓ f ── Z0 평평한 띠 ── 바닥 호(P0)
    ///                  └ 측선 E(A → A′ · 바닥 가장 가까운 점) · 마감선 E′ = E를 안쪽으로 vE 민 선 · 그 사이 가로대(날개벽)
    /// </code>
    /// <para>① ~ ⑧은 <see cref="Survey"/>와 같은 셈(같은 표본 · 단 수 n · 규칙 <see cref="Input.InRule"/> · 거절 차례) — 거절이면 그 첫 코드로 멈춘다.
    /// 그다음: 바꿀 자리 Z(L → E_B → 바닥 호 → E_A) · 끝 띠(E~E′)를 뺀 Z′ · 줄 = 늘인 L의 둥근 버퍼(SimplifyFactor 0) 테두리 ∩ Z′(줄마다 한 줄 · 두 끝이 E′ 위) ·
    /// 점 간격 ≥ 0.25 mm · 높이는 점마다 그 단(링) · 끝 띠는 가로대 · 세모 = <see cref="MeshLines"/>.
    /// 관문: 고리 · 줄 · 자리 · 바닥 높이 끊김 · 옛 소단 이어짐 · 안쪽 면 높이 · 겹침 · 정지면 밖 · 마구리 · 소단(소단 가운데 + 평평한 띠 = Z · L = G).</para>
    /// <para>아무것도 안 바꾼다(순수 셈). 옛 <see cref="Build"/>는 <see cref="Input.Shape"/> = In일 때만 여기로 온다.</para></summary>
    public static Built BuildIn(Input inp)
    {
        var R = new Built { Shape = 1, Rule = (int)inp.InRule, LedgeLim = inp.LedgeMax };
        var log = new System.Text.StringBuilder();
        var ci = Ci;
        string F(double v, string f) => double.IsFinite(v) ? v.ToString(f, ci) : "—";
        Built No(string code, string why, params (double, double)[] at)
        {
            R.Ok = false; R.Code = code; R.Why = why; R.Marks.AddRange(at);
            log.Append($"  거절 {code} — {why}\n");
            R.SelfCheck = SelfLineIn(R); R.Log = log.ToString(); return R;
        }
        if (inp.Line == null || inp.Line.Count < 2) return No("재료", "선이 없습니다");
        R.A = inp.Line[0]; R.B = inp.Line[^1];
        var C = new InCtx();
        var S = SurveyCore(inp, C);
        R.SurveyText = S.Text;
        R.Dir = S.Dir; R.Tiers = S.Tiers; R.TiersStrict = S.TiersStrict; R.LineLen = S.LineLen; R.Depth = double.IsFinite(S.Depth) ? S.Depth : 0;
        R.OuterN = S.OuterN; R.OuterOver = S.OuterOver; R.OuterNeg = S.OuterNeg; R.OuterMin = S.OuterMin; R.OuterMax = S.OuterMax; R.OuterAt = S.OuterAt; R.OuterLim = S.TierH;
        R.LedgeMax = S.Ledge; R.LedgeAt = S.LedgeAt; R.LedgeN = S.LedgeN;
        R.EndAngleA = S.ThetaA; R.EndAngleB = S.ThetaB; R.Z1Min = S.Z1Min; R.Z1Max = S.Z1Max;
        if (double.IsFinite(S.VE)) R.VEps = S.VE;
        log.Append(string.Format(ci, "  ① 선 {0} m · 점 {1} · 표본 {2}(Gap 합침 {3}) · 원지반 {4}~{5} · 바닥 Z0 {6}~{7}\n",
            F(S.LineLen, "F2"), inp.Line.Count, S.Samples, S.Merged, F(S.GMin, "F3"), F(S.GMax, "F3"), F(S.Z0Min, "F3"), F(S.Z0Max, "F3")));
        log.Append(string.Format(ci, "  ② 안쪽 {0} · 바닥 고리 {1}점 · 단 표 {2}\n", S.Side > 0 ? "오른쪽" : S.Side < 0 ? "왼쪽" : "—", inp.Plan?.Count ?? 0,
            S.Dir == 1 ? inp.CutBench?.Text ?? "—" : S.Dir == -1 ? inp.FillBench?.Text ?? "—" : $"절 {inp.CutBench?.Text ?? "—"} · 성 {inp.FillBench?.Text ?? "—"}"));
        log.Append(string.Format(ci, "  ③ 방향 {0}(절 {1} · 성 {2} · 같음 {3}) · 단 수 n {4}(규칙 {5} · 엄격 n {6}) · 바깥 벽 {7}~{8} m @({9}, {10}) · 한도 H{11} {12} · 넘음 {13} · 음수 {14} · 턱 {15} m({16}점) @({17}, {18})\n",
            S.DirName, S.NCut, S.NFill, S.NEq, S.Tiers, inp.InRule == InwardRule.Ledge ? "턱" : "엄격", S.TiersStrict, F(S.OuterMin, "F3"), F(S.OuterMax, "F3"), F(S.OuterAt.X, "F1"), F(S.OuterAt.Y, "F1"),
            S.Tiers + 1, F(S.TierH, "F2"), S.OuterOver, S.OuterNeg, S.Ledge.ToString("F3", ci), S.LedgeN, F(S.LedgeAt.X, "F1"), F(S.LedgeAt.Y, "F1")));
        if (!S.Ok) return No(S.Code, S.Why, S.At);
        if (!C.Filled) return No("재료", "재기 재료가 비었습니다(단 표 · 줄)", (R.A.X, R.A.Y));
        if (inp.TopStep) R.Warn.Add("천단 계단은 새 모양에서 아직 — 경사로 지었습니다(JACK 결정 대기)");
        foreach (var (nm, th, p) in new[] { ("A", S.ThetaA, S.A), ("B", S.ThetaB, S.B) })
            if (double.IsFinite(th) && th >= 180)
                return No("측선", string.Format(ci, "선 끝 {0}의 측선이 선 뒤쪽으로 꺾입니다(끝 각 {1:F0}° ≥ 180° — 바꿀 자리가 선 끝을 감쌈) ({2:F1}, {3:F1}) — 선 끝을 옮기세요", nm, th, p.X, p.Y), p);

        var pl = C.Pl; double ox = C.Ox, oy = C.Oy, side = C.Side; var gf = C.Gf;
        double vE = C.VE, sg = C.Sg;
        var rows = C.Rows; double D = rows[^1].D; R.Depth = D;
        double fo = rows[1].D; R.Face = fo; R.VEps = vE;
        var ob = C.Ob;
        double mg = S.Dir != 0 ? ob!.MaxGrad : Math.Max(C.ObC!.MaxGrad, C.ObF!.MaxGrad);
        (double, double) Wd(Coordinate c) => (c.X + ox, c.Y + oy);
        Point3 Pw(Coordinate c, double z) => new(c.X + ox, c.Y + oy, z);
        // 단 높이(실제 z · 지역 좌표) — −1 = 선 위 원지반(정확한 꺾은선) · 0 = 바닥 Z0 · k = Z_k
        double ZK(int k, double lx, double ly) => k < 0 ? C.GAt(pl.Near(lx, ly).S) : k == 0 ? C.Z0(lx + ox, ly + oy) : ob!.Level(k, lx + ox, ly + oy);
        // 안쪽 법선(가장 가까운 L 토막)
        (double X, double Y) Nrm(double lx, double ly) { var (tx, ty) = pl.Tan(pl.Near(lx, ly).S); return (ty * side, -tx * side); }
        double? P0Read(Coordinate c, (double X, double Y) nudge) => C.P0w(c.X + ox, c.Y + oy) ?? C.P0w(c.X + ox + nudge.X * 1e-3, c.Y + oy + nudge.Y * 1e-3);

        // ⑤ 바꿀 자리 Z — A → L → B → E_B → 바닥 호(B′ → A′) → E_A → A · 두 호 가운데 단순하고 바닥과 안 겹치는 쪽(계획 1-6)
        var plan = inp.Plan!; int pn = plan.Count; if (pn >= 2 && plan[0].X == plan[^1].X && plan[0].Y == plan[^1].Y) pn--;   // 재기가 바닥 없음을 이미 거절함
        var fr = new Coordinate[pn]; for (int i = 0; i < pn; i++) fr[i] = new Coordinate(plan[i].X - ox, plan[i].Y - oy);
        var qa = Nearest(plan, R.A.X, R.A.Y); var qb = Nearest(plan, R.B.X, R.B.Y);
        var cA = pl.C[0]; var cB = pl.C[^1];
        var a1 = new Coordinate(qa.X - ox, qa.Y - oy); var b1 = new Coordinate(qb.X - ox, qb.Y - oy);
        List<Coordinate> Arc(bool fwd)
        {
            var r = new List<Coordinate> { b1 };
            bool direct = qa.J == qb.J && (fwd ? qa.T >= qb.T : qa.T <= qb.T);
            if (!direct)
            {
                int j = qb.J;
                if (fwd) for (int g = 0; g <= pn + 1; g++) { j = (j + 1) % pn; r.Add(fr[j]); if (j == qa.J) break; }
                else for (int g = 0; g <= pn + 1; g++) { r.Add(fr[j]); if (j == (qa.J + 1) % pn) break; j = (j - 1 + pn) % pn; }
            }
            r.Add(a1);
            var o = new List<Coordinate>();
            foreach (var c in r) if (o.Count == 0 || o[^1].Distance(c) > 1e-12) o.Add(c);
            return o;
        }
        List<Coordinate> RingOf(List<Coordinate> arc)
        {
            var cs = new List<Coordinate>(pl.C);
            foreach (var c in arc) if (cs[^1].Distance(c) > 1e-12) cs.Add(c);
            while (cs.Count > 3 && cs[^1].Distance(cs[0]) <= 1e-12) cs.RemoveAt(cs.Count - 1);
            return cs;
        }
        Polygon? Cand(List<Coordinate> rc)
        {
            if (rc.Count < 3) return null;
            try { var p = gf.CreatePolygon(rc.Append(rc[0].Copy()).ToArray()); return p.IsValid && p.Area > 1e-9 && p.Intersection(C.FloorP).Area <= 1e-6 ? p : null; } catch { return null; }
        }
        var arcF = Arc(true); var arcBk = Arc(false);
        var rcF = RingOf(arcF); var rcB = RingOf(arcBk);
        var zF = Cand(rcF); var zB = Cand(rcB);
        // 두 호가 같은 점(바닥 모서리 하나 — A′ = B′)이면 한 고리다
        if (zF != null && zB != null && arcF.Count == arcBk.Count && arcF.Zip(arcBk).All(u => u.First.Distance(u.Second) <= 1e-12)) zB = null;
        if ((zF != null) == (zB != null))
            return No("고리", "선 · 측선 · 부지 바닥으로 바꿀 자리를 닫지 못합니다 — 선이 부지를 감싸 돌거나 꼬였습니다" + (zF != null ? "(두 호가 다 닫힘)" : ""), (R.A.X, R.A.Y), (R.B.X, R.B.Y));
        var zc = zF ?? zB!; var arc = zF != null ? arcF : arcBk; var ringC = zF != null ? rcF : rcB;
        double arcLen = 0; for (int i = 0; i + 1 < arc.Count; i++) arcLen += arc[i].Distance(arc[i + 1]);
        double outN = C.NP != null ? zc.Difference(C.NP).Area : 0;
        if (outN > 1e-6) return No("고리", string.Format(ci, "바꿀 자리가 경계 밖으로 {0:F4}㎡ 나갑니다 — 선 끝을 옮기세요", outN), Wd(zc.Difference(C.NP!).InteriorPoint.Coordinate));
        // 꼭짓점 안쪽 각(바꿀 자리 안) — 끝 띠를 끝 너머로 늘일 길이(둔각이면 띠가 수선 뒤로 넘어간다)
        double sArea = 0; for (int i = 0; i < ringC.Count; i++) { var p = ringC[i]; var q = ringC[(i + 1) % ringC.Count]; sArea += p.X * q.Y - q.X * p.Y; }
        bool ccw = sArea > 0;
        double Ang(int i)
        {
            int m = ringC.Count; var p = ringC[(i - 1 + m) % m]; var c = ringC[i]; var q = ringC[(i + 1) % m];
            double ix = c.X - p.X, iy = c.Y - p.Y, ux = q.X - c.X, uy = q.Y - c.Y, turn = Math.Atan2(ix * uy - iy * ux, ix * ux + iy * uy);
            return (ccw ? Math.PI - turn : Math.PI + turn) * 180 / Math.PI;
        }
        int nL = pl.C.Length;
        double thA = Ang(0), thB = Ang(nL - 1), phB = Ang(nL), phA = Ang(ringC.Count - 1);
        double Ext(double ang) => ang >= 180 - 1e-9 ? 0 : vE * (1 + Math.Max(0, -1 / Math.Tan(ang * Math.PI / 180)));
        double lenA = cA.Distance(a1), lenB = cB.Distance(b1);
        (double X, double Y) eA = ((a1.X - cA.X) / lenA, (a1.Y - cA.Y) / lenA), eB = ((b1.X - cB.X) / lenB, (b1.Y - cB.Y) / lenB);
        (double X, double Y) SideN(Coordinate p, (double X, double Y) e, double len)
        {
            double mx = p.X + e.X * len / 2, my = p.Y + e.Y * len / 2, h = Math.Min(1e-4, len * 1e-3);
            bool in1 = zc.Contains(gf.CreatePoint(new Coordinate(mx - e.Y * h, my + e.X * h))), in2 = zc.Contains(gf.CreatePoint(new Coordinate(mx + e.Y * h, my - e.X * h)));
            return in1 == in2 ? (0, 0) : in1 ? (-e.Y, e.X) : (e.Y, -e.X);
        }
        var nA = SideN(cA, eA, lenA); var nB = SideN(cB, eB, lenB);
        if (nA == (0, 0) || nB == (0, 0)) return No("고리", "측선의 어느 쪽이 바꿀 자리인지 못 가렸습니다 — 선 끝을 옮기세요", (R.A.X, R.A.Y));
        // 끝 띠(E ~ E′ · 폭 vE · 바꿀 자리 쪽) — 끝 너머로 늘여 둔각 끝도 덮는다
        Polygon Strip(Coordinate p, (double X, double Y) e, double len, (double X, double Y) n, double xp, double xq)
        {
            var p1 = new Coordinate(p.X - e.X * xp, p.Y - e.Y * xp); var p2 = new Coordinate(p.X + e.X * (len + xq), p.Y + e.Y * (len + xq));
            var p3 = new Coordinate(p2.X + n.X * vE, p2.Y + n.Y * vE); var p4 = new Coordinate(p1.X + n.X * vE, p1.Y + n.Y * vE);
            return gf.CreatePolygon(new[] { p1, p2, p3, p4, p1.Copy() });
        }
        Polygon zp;
        try
        {
            var zg = zc.Difference(Strip(cA, eA, lenA, nA, Ext(thA), Ext(phA)).Union(Strip(cB, eB, lenB, nB, Ext(thB), Ext(phB))));
            var parts = Enumerable.Range(0, zg.NumGeometries).Select(i => zg.GetGeometryN(i)).OfType<Polygon>().Where(p => p.Area > 1e-9).ToList();
            if (parts.Count != 1) return No("고리", string.Format(ci, "끝 마감면(폭 {0:F3} m)이 바꿀 자리를 {1}조각으로 가릅니다 — 선 끝을 벌리세요", vE, parts.Count), (R.A.X, R.A.Y), (R.B.X, R.B.Y));
            zp = parts[0];
        }
        catch (Exception ex) { return No("고리", $"끝 마감면을 못 뺐습니다({ex.GetType().Name}) — 선 끝을 옮기세요", (R.A.X, R.A.Y)); }
        // E′ 위(바꿀 자리 쪽으로 vE 민 측선) — 법선 거리 · 측선 따라 거리
        double DnE(Coordinate c, Coordinate p, (double X, double Y) n) => (c.X - p.X - n.X * vE) * n.X + (c.Y - p.Y - n.Y * vE) * n.Y;
        double TE(Coordinate c, Coordinate p, (double X, double Y) e) => (c.X - p.X) * e.X + (c.Y - p.Y) * e.Y;
        (Coordinate? Lo, Coordinate? Hi) Ends(Coordinate p, (double X, double Y) e, (double X, double Y) n)
        {
            Coordinate? lo = null, hi = null; double tlo = double.MaxValue, thi = double.MinValue;
            foreach (var v in zp.ExteriorRing.Coordinates)
            {
                if (Math.Abs(DnE(v, p, n)) > 1e-8) continue;
                double t = TE(v, p, e);
                if (t < tlo) { tlo = t; lo = v; }
                if (t > thi) { thi = t; hi = v; }
            }
            return (lo, hi);
        }
        var (aS, a2) = Ends(cA, eA, nA); var (bS, b2) = Ends(cB, eB, nB);
        if (aS == null || a2 == null || bS == null || b2 == null || aS.Distance(a2) < 1e-6 || bS.Distance(b2) < 1e-6)
            return No("고리", "끝 마감선(측선을 안쪽으로 민 선)이 바꿀 자리를 가로지르지 못합니다 — 선 끝을 옮기세요", (R.A.X, R.A.Y), (R.B.X, R.B.Y));
        bool OnFloor(Coordinate c) => Nearest(plan, c.X + ox, c.Y + oy).D < 1e-7;
        if (pl.Near(aS.X, aS.Y).D > 1e-7 || pl.Near(bS.X, bS.Y).D > 1e-7)
            return No("고리", "끝 마감선이 선에 닿지 않습니다 — 선 끝을 옮기세요", Wd(aS), Wd(bS));
        if (!(OnFloor(a2) || Math.Abs(DnE(a2, cB, nB)) < 1e-7) || !(OnFloor(b2) || Math.Abs(DnE(b2, cA, nA)) < 1e-7))
            return No("고리", "끝 마감선이 부지 바닥에 닿지 않습니다(바닥 뾰족 모서리 곁) — 선 끝을 옮기세요", Wd(a2), Wd(b2));
        double tA0 = TE(aS, cA, eA), tA1 = TE(a2, cA, eA), tB0 = TE(bS, cB, eB), tB1 = TE(b2, cB, eB);
        bool OnEA(Coordinate c) { double t = TE(c, cA, eA); return Math.Abs(DnE(c, cA, nA)) < 1e-7 && t >= tA0 - 1e-7 && t <= tA1 + 1e-7; }
        bool OnEB(Coordinate c) { double t = TE(c, cB, eB); return Math.Abs(DnE(c, cB, nB)) < 1e-7 && t >= tB0 - 1e-7 && t <= tB1 + 1e-7; }
        log.Append(string.Format(ci, "  ⑤ 측선 A {0:F2} m θ {1:F0}° · B {2:F2} m θ {3:F0}° · 바닥 쪽 각 A′ {4:F0}° · B′ {5:F0}° · 바닥 호 {6:F2} m({7}점) · Z {8:F1}㎡ · Z′ {9:F1}㎡ · Z⊄N {10:E1}㎡\n",
            lenA, thA, lenB, thB, phA, phB, arcLen, arc.Count, zc.Area, zp.Area, outN));

        // 0.25 mm 솎기 — 지키는 점은 남기고(둘 다 지키는 점이면 둘 다 · 짧은 변으로 셈) 아닌 점을 뺀다(높이는 점마다 다시 읽음)
        int shortKeep = 0;
        List<(double T, Coordinate C, bool Keep)> Thin(List<(double T, Coordinate C, bool Keep)> src, ref int merged)
        {
            var o = new List<(double T, Coordinate C, bool Keep)>();
            foreach (var p in src)
            {
                if (o.Count > 0)
                {
                    var q = o[^1]; double dd = p.C.Distance(q.C);
                    if (dd < MinSp)
                    {
                        if (dd <= 1e-12) { if (p.Keep && !q.Keep) o[^1] = p; continue; }
                        if (p.Keep && q.Keep) { shortKeep++; o.Add(p); continue; }
                        merged++;
                        if (p.Keep) o[^1] = p;
                        continue;
                    }
                }
                o.Add(p);
            }
            return o;
        }
        // 0번 줄(L) — L 꼭짓점 · 원지반 변 건넘(높이를 실은 점) · A* · B*(마감선이 L과 만나는 점)
        double sAs = pl.Near(aS.X, aS.Y).S, sBs = pl.Near(bS.X, bS.Y).S;
        if (!(sAs < sBs - MinSp)) return No("고리", "두 끝 마감선이 선 위에서 만나거나 엇갈립니다 — 선이 너무 짧거나 끝이 모입니다", Wd(aS));
        // 쐐기 모서리 비움(검토 M3 · 1단계 계측 — 원지반 건넘이 A에서 0.3 mm면 끝 쐐기 세모 높이 0.15 mm) — 안쪽 각 α인 모서리에서 s 떨어진 점이 만드는 세모 높이 ≤ s·sin α라
        //   지키지 않는 점은 모서리에서 MinSp/sin α 안이면 뺀다(A · A* = θ_A · B · B* = θ_B · A′ · A″ = 바닥 쪽 각)
        double Rc(double angDeg) { double s = Math.Sin(Math.Clamp(angDeg, 1, 179) * Math.PI / 180); return MinSp / Math.Max(s, 1e-3); }
        var corners = new List<(Coordinate P, double R)> { (cA, Rc(thA)), (aS, Rc(thA)), (cB, Rc(thB)), (bS, Rc(thB)), (a1, Rc(phA)), (a2, Rc(phA)), (b1, Rc(phB)), (b2, Rc(phB)) };
        bool Clear(Coordinate c) { foreach (var (p, r) in corners) { double dd = c.Distance(p); if (dd > 1e-12 && dd < r) return false; } return true; }
        var l0 = new List<(double T, Coordinate C, bool Keep)>();
        for (int i = 0; i < nL; i++) l0.Add((pl.Cum[i], pl.C[i], true));
        foreach (var (s, _) in C.Gp) { var c = pl.At(s); if (Clear(c)) l0.Add((s, c, false)); }
        l0.Add((sAs, aS, true)); l0.Add((sBs, bS, true));
        l0 = l0.OrderBy(q => q.T).ThenByDescending(q => q.Keep).ToList();
        int merged = 0;
        l0 = Thin(l0, ref merged);
        var lPts = l0.Select(q => Pw(q.C, C.GAt(q.T))).ToList();
        var row0 = l0.Where(q => q.T >= sAs - 1e-12 && q.T <= sBs + 1e-12).Select(q => Pw(q.C, C.GAt(q.T))).ToList();

        // ④ 줄 — 늘인 L(Z 가장 먼 꼭짓점 + 5 m)의 둥근 버퍼(8분할 · SimplifyFactor 0) 테두리 ∩ Z′
        double extBy = 5; foreach (var v in ringC) extBy = Math.Max(extBy, Math.Max(v.Distance(cA), v.Distance(cB)) + 5);
        var extG = gf.CreateLineString(WallInPoly.ExtendEnds(pl.C.Select(c => new Point3(c.X, c.Y, 0)).ToList(), extBy).Select(q => new Coordinate(q.X, q.Y)).ToArray());
        var bufP = new BufferParameters { QuadrantSegments = 8, SimplifyFactor = 0, JoinStyle = JoinStyle.Round, EndCapStyle = EndCapStyle.Round };
        var curves = new Dictionary<double, Geometry>();
        List<LineString> Pieces(double d)
        {
            if (!curves.TryGetValue(d, out var g)) curves[d] = g = extG.Buffer(d, bufP).Boundary;
            Geometry cut; try { cut = g.Intersection(zp); } catch { return new(); }
            var lm = new LineMerger(); lm.Add(cut);
            return lm.GetMergedLineStrings().OfType<LineString>().Where(l => l.Length > 1e-9).ToList();
        }
        // 줄에 높이 — 사면 코드와 같은 간격(RowDens)으로 채우고 토막 가운데 높이가 1차와 1 mm 넘게 다르면 반씩(2 cm까지) · 끝점에서 0.25 mm 안 채움 점은 버림
        List<Point3> Dens(List<Coordinate> pc, Func<double, double, double> z)
        {
            double dd = Math.Clamp(inp.RowDens, 0.3, 1.0);
            bool Far(Coordinate q, Coordinate a, Coordinate b) => q.Distance(a) >= MinSp && q.Distance(b) >= MinSp;
            var o = new List<Point3> { Pw(pc[0], z(pc[0].X, pc[0].Y)) };
            void Refine(Coordinate a, double za, Coordinate b, double zb, int depth)
            {
                if (depth >= 8 || a.Distance(b) < 0.02) return;
                var m = new Coordinate((a.X + b.X) / 2, (a.Y + b.Y) / 2); double zm = z(m.X, m.Y);
                if (Math.Abs(zm - (za + zb) / 2) <= 1e-3 || !Far(m, a, b)) return;
                Refine(a, za, m, zm, depth + 1); o.Add(Pw(m, zm)); Refine(m, zm, b, zb, depth + 1);
            }
            for (int i = 1; i < pc.Count; i++)
            {
                var a = pc[i - 1]; var b = pc[i];
                int nn = (int)Math.Ceiling(a.Distance(b) / dd);
                var seg = new List<Coordinate> { a };
                for (int u = 1; u < nn; u++) { var q = new Coordinate(a.X + (b.X - a.X) * u / nn, a.Y + (b.Y - a.Y) * u / nn); if (Far(q, a, b)) seg.Add(q); }
                seg.Add(b);
                for (int j = 1; j < seg.Count; j++)
                {
                    double za = z(seg[j - 1].X, seg[j - 1].Y), zb = z(seg[j].X, seg[j].Y);
                    Refine(seg[j - 1], za, seg[j], zb, 0); o.Add(Pw(seg[j], zb));
                }
            }
            return o;
        }
        // 늘인 L 위 가장 가까운 점 — 거리 · 그 점이 늘인 L 꼭짓점인가(둥근 이음 부채 = 8분할 현 · 현 위 점은 d·cos(π/32) ~ d)
        var extC = extG.Coordinates;
        (double D, bool AtV) ExtNear(Coordinate c)
        {
            double bd = double.MaxValue; bool atv = false;
            for (int i = 0; i + 1 < extC.Length; i++)
            {
                double ex = extC[i + 1].X - extC[i].X, ey = extC[i + 1].Y - extC[i].Y, l2 = ex * ex + ey * ey;
                double t = l2 < 1e-24 ? 0 : Math.Clamp(((c.X - extC[i].X) * ex + (c.Y - extC[i].Y) * ey) / l2, 0, 1);
                double dx = c.X - extC[i].X - ex * t, dy = c.Y - extC[i].Y - ey * t, d2 = Math.Sqrt(dx * dx + dy * dy);
                if (d2 < bd - 1e-12) { bd = d2; atv = t <= 1e-12 || t >= 1 - 1e-12; }
                else if (Math.Abs(d2 - bd) <= 1e-12) atv |= t <= 1e-12 || t >= 1 - 1e-12;
            }
            return (bd, atv);
        }
        double chordLo = Math.Cos(Math.PI / 32);
        var ladder = new List<List<Point3>>();
        var wall = new List<(double D, int K, List<Point3> P)>();
        for (int i = 1; i < rows.Count; i++)
        {
            var (d, k) = rows[i];
            R.RowN++;
            var ps = Pieces(d);
            Coordinate[]? cs = null;
            if (ps.Count == 1)
            {
                cs = ps[0].Coordinates;
                bool f0 = OnEA(cs[0]) && OnEB(cs[^1]), f1 = OnEB(cs[0]) && OnEA(cs[^1]);
                if (f1 && !f0) cs = cs.Reverse().ToArray(); else if (!f0) cs = null;
            }
            if (cs == null)
            {
                R.RowPieceBad++;
                var at = ps.Count > 0 ? Wd(ps.OrderBy(l => l.Length).First().Coordinates[0]) : Wd(pl.At(pl.Len / 2));
                return No("줄", string.Format(ci, "{0} 줄(선에서 {1:F2} m)이 한 줄로 안 이어집니다({2}조각) ({3:F1}, {4:F1}) — 선이 크게 꺾이거나 측선이 줄을 두 번 자릅니다 · 선을 펴세요",
                    k == 0 ? "바닥" : $"{k}단 소단", d, ps.Count, at.Item1, at.Item2), at);
            }
            var lst = new List<(double T, Coordinate C, bool Keep)>();
            var lil = new NetTopologySuite.LinearReferencing.LengthIndexedLine(gf.CreateLineString(cs));
            for (int j = 0; j < cs.Length; j++) lst.Add((lil.Project(cs[j]), cs[j], j == 0 || j == cs.Length - 1));
            lst = Thin(lst, ref merged);
            // 줄 거리(검토 M4 — 늘인 L에서 d ± 1 µm) · 둥근 이음 부채(가장 가까운 점 = 늘인 L 꼭짓점)의 현 위 점은 d·cos(π/32)~d(8분할 현 — 평면 모양만 · 계획 1-5)라 따로 셈
            for (int j = 1; j + 1 < lst.Count; j++)
            {
                var (dd, atv) = ExtNear(lst[j].C);
                if (Math.Abs(dd - d) <= 1e-6) continue;
                if (atv && dd >= d * chordLo - 1e-9 && dd <= d + 1e-6) R.RowChord++; else R.RowDistBad++;
            }
            // ★[검토 M3 · 1단계 계측 — 원지반 꼭짓점이 L에서 0.3 mm(변 건넘 0.3 mm 간격)이면 f 5 mm 띠에 바늘 75] 좁은 띠(앞 줄과 0.5 m 안)는 앞 줄 점마다 이 줄 위 수선 발(사다리 짝)을 넣는다 —
            //   짝이 없으면 촘촘한 앞 줄 두 점과 1 m 떨어진 이 줄 점이 세모를 이뤄 높이 ≈ 간격 × 띠 폭 / 1 m(0.3 mm × 5 mm / 0.5 m = 3 µm)
            //   짝과 앞 줄 점을 잇는 가로대도 선으로 넣는다(1단계 계측: 짝이 있어도 우리 들로네가 두 점 + 먼 점 세모(높이 3 µm)를 골랐다 — 선으로 칸을 막으면 세모 짜는 쪽과 상관없음)
            double wPrev = d - rows[i - 1].D;
            var pairs = new List<(Point3 Src, Coordinate Dst)>();
            if (wPrev < 0.5)
            {
                var prevW = i == 1 ? row0 : wall[^1].P;
                double lEnd = lil.EndIndex;
                foreach (var pw in prevW)
                {
                    var pq = new Coordinate(pw.X - ox, pw.Y - oy);
                    double ix = lil.Project(pq);
                    if (ix <= MinSp || ix >= lEnd - MinSp) continue;
                    var pc2 = lil.ExtractPoint(ix);
                    if (pc2.Distance(pq) > wPrev * 1.5 + 1e-6) continue;   // 짝이 아님(줄 끝 곁 비스듬한 투영)
                    if (pc2.Distance(cs[0]) < Rc(thA) || pc2.Distance(cs[^1]) < Rc(thB)) continue;   // 줄 끝(E′ 위 · 쐐기 각 θ) 비움
                    lst.Add((ix, pc2, false)); pairs.Add((pw, pc2));
                }
                int mgP = 0;
                lst = Thin(lst.OrderBy(q => q.T).ThenByDescending(q => q.Keep).ToList(), ref mgP);
            }
            var pc = lst.Select(q => q.C).ToList();
            int kk = k;
            var pts = Dens(pc, (x, y) => ZK(kk, x, y));
            if (pairs.Count > 0)
            {
                // 짝이 솎기에서 곁 꼭짓점에 합쳐졌으면 그 꼭짓점과 잇는다(0.25 mm 안)
                var byKey = new Dictionary<(long, long), Point3>();
                (long, long) Kq(double x, double y) => ((long)Math.Floor((x - ox) / MinSp), (long)Math.Floor((y - oy) / MinSp));
                foreach (var q in pts) byKey[Kq(q.X, q.Y)] = q;
                foreach (var (src, dst) in pairs)
                {
                    var (kx, ky) = Kq(dst.X + ox, dst.Y + oy); Point3? best = null; double bd = MinSp * 1.01;
                    for (long a = -1; a <= 1; a++) for (long bb = -1; bb <= 1; bb++)
                            if (byKey.TryGetValue((kx + a, ky + bb), out var q)) { double dd = Math.Sqrt((q.X - ox - dst.X) * (q.X - ox - dst.X) + (q.Y - oy - dst.Y) * (q.Y - oy - dst.Y)); if (dd < bd) { bd = dd; best = q; } }
                    if (best != null) ladder.Add(new List<Point3> { src, best.Value });
                }
            }
            wall.Add((d, k, pts));
        }
        R.RowMerged = merged;
        // 자리 — 줄이 바닥에 vE보다 가까우면(늘인 L 끝 쪽 등) 막는다
        {
            R.RoomMin = double.MaxValue;
            foreach (var (_, _, P) in wall)
            {
                var ls = gf.CreateLineString(P.Select(q => new Coordinate(q.X - ox, q.Y - oy)).ToArray());
                var dop = new DistanceOp(ls, C.FloorP); double dd = dop.Distance();
                if (dd < R.RoomMin) { R.RoomMin = dd; R.RoomAt = Wd(dop.NearestPoints()[0]); }
            }
            log.Append(string.Format(ci, "  ④ 줄 {0}(줄 조각 어긋남 {1} · 거리 어긋남 {2} · 합친 점 {3}) · 거리 {4} · 깊이 D {5:F3} m · Z_1−Z0 {6}~{7}\n",
                R.RowN, R.RowPieceBad, R.RowDistBad, R.RowMerged, string.Join(" · ", rows.Select(r => r.D.ToString("0.###", ci))), D, F(R.Z1Min, "F3"), F(R.Z1Max, "F3")));
            log.Append(string.Format(ci, "  ⑧ 자리 — 가장 안쪽 줄 ↔ 바닥 {0:F3} m(≥ vE {1:F3}) @({2:F1},{3:F1}) · 선 ↔ 바닥 {4} m\n", R.RoomMin, vE, R.RoomAt.Item1, R.RoomAt.Item2, F(S.Room, "F3")));
            if (R.RoomMin < vE - 1e-9)
                return No("자리", string.Format(ci, "자리 모자람 — 소단 줄이 부지 바닥에 {0:F3} m까지 붙습니다(필요 ≥ {1:F3} m · {2}단 · 깊이 {3:F2} m) ({4:F1}, {5:F1}) — 선을 바깥으로 옮기거나 소단을 줄이세요",
                    R.RoomMin, vE, R.Tiers, D, R.RoomAt.Item1, R.RoomAt.Item2), R.RoomAt);
        }
        // 평평한 띠 부채선(계획 1-4 · 1단계 계측 — 기운 부지 3 %에서 띠 표본 최대 9.8 mm가 바닥 모서리 부채 경계 위) — 바닥 볼록 모서리(호 안쪽 꼭짓점)마다 두 변의 바깥 법선으로
        //   가장 안쪽 줄까지 곧은 선(Z0 = 그 모서리 P0). Z0(가장 가까운 바닥 점의 P0)는 이 선에서 기울기가 꺾여(변 몫 = 변 따라 기욺 · 부채 몫 = 모서리 높이) 세모가 건너면 1차로 못 따른다
        var fans = new List<List<Point3>>();
        var fanOnEp = new List<(bool AtA, Coordinate C)>();   // 부채선이 끝 띠를 지나 들어오면 E′ 위 시작점(E′ 꼭짓점으로 넣음)
        {
            var inner = wall[^1].P;
            var innerLs = gf.CreateLineString(inner.Select(q => new Coordinate(q.X - ox, q.Y - oy)).ToArray());
            double reachF = 0; foreach (var v in arc) reachF = Math.Max(reachF, extG.Distance(gf.CreatePoint(v)));
            var zpCov = NetTopologySuite.Geometries.Prepared.PreparedGeometryFactory.Prepare(zp.Buffer(1e-9));
            (double X, double Y) OutN(Coordinate a, Coordinate b)
            {
                double ex = b.X - a.X, ey = b.Y - a.Y, l = Math.Sqrt(ex * ex + ey * ey);
                if (l < 1e-12) return (0, 0);
                var m = new Coordinate((a.X + b.X) / 2 - ey / l * 1e-6, (a.Y + b.Y) / 2 + ex / l * 1e-6);
                return C.FloorP.Contains(gf.CreatePoint(m)) ? (ey / l, -ex / l) : (-ey / l, ex / l);
            }
            for (int jv = 0; jv < pn; jv++)
            {
                var q = fr[jv];
                if (!arc.Any(v => v.Distance(q) < 1e-9)) continue;   // 바꿀 자리 바닥 호 위 꼭짓점만(끝 A′ · B′ 포함)
                var pv = fr[(jv - 1 + pn) % pn]; var nx = fr[(jv + 1) % pn];
                var n1 = OutN(pv, q); var n2 = OutN(q, nx);
                if (n1 == (0, 0) || n2 == (0, 0) || (nx.X - q.X) * n1.X + (nx.Y - q.Y) * n1.Y >= -1e-12) continue;   // 볼록 모서리만(오목은 부채가 없다)
                foreach (var nn in new[] { n1, n2 })
                {
                    var far = new Coordinate(q.X + nn.X * (reachF + 1), q.Y + nn.Y * (reachF + 1));
                    var ray = gf.CreateLineString(new[] { q, far });
                    var hit = ray.Intersection(innerLs).Coordinates.OrderBy(c => c.Distance(q)).FirstOrDefault();
                    if (hit == null) continue;
                    // 시작점 — 모서리가 Z′ 테두리면 그 점 · 아니면(끝 띠 안) 가장 안쪽 줄 앞에서 Z′로 들어오는 자리(E′ 위여야)
                    double tHit = hit.Distance(q);
                    var start = q; bool onEpA = false, onEpB = false;
                    if (!zp.Covers(gf.CreatePoint(q)))
                    {
                        var xs = ray.Intersection(zp.ExteriorRing).Coordinates.Where(c => c.Distance(q) < tHit - 1e-9).OrderByDescending(c => c.Distance(q)).ToList();
                        if (xs.Count == 0) continue;
                        start = xs[0];
                        onEpA = Math.Abs(DnE(start, cA, nA)) < 1e-7; onEpB = Math.Abs(DnE(start, cB, nB)) < 1e-7;
                        if (!onEpA && !onEpB) continue;
                    }
                    var seg = gf.CreateLineString(new[] { start, hit });
                    if (seg.Length < MinSp || !zpCov.Covers(seg)) continue;
                    // 가장 안쪽 줄에 꼭짓점으로(0.25 mm 안 꼭짓점이 있으면 그것)
                    int bi = -1; double bd = double.MaxValue;
                    for (int j = 0; j < inner.Count; j++) { double dd = Math.Sqrt((inner[j].X - ox - hit.X) * (inner[j].X - ox - hit.X) + (inner[j].Y - oy - hit.Y) * (inner[j].Y - oy - hit.Y)); if (dd < bd) { bd = dd; bi = j; } }
                    Point3 end;
                    if (bd < MinSp) end = inner[bi];
                    else
                    {
                        int js = -1;
                        for (int j = 0; j + 1 < inner.Count && js < 0; j++)
                            if (gf.CreateLineString(new[] { new Coordinate(inner[j].X - ox, inner[j].Y - oy), new Coordinate(inner[j + 1].X - ox, inner[j + 1].Y - oy) }).Distance(gf.CreatePoint(hit)) < 1e-9) js = j;
                        if (js < 0) continue;
                        end = Pw(hit, ZK(0, hit.X, hit.Y));
                        inner.Insert(js + 1, end);
                    }
                    fans.Add(new List<Point3> { Pw(start, ZK(0, start.X, start.Y)), end });
                    if (onEpA || onEpB) fanOnEp.Add((onEpA, start));
                }
            }
        }

        // ⑥ 끝 — 마감선 E′(A* → 줄 끝들 → A″ · 0.5 m 채움) · E(A → A′ · z = P0 · 변 건넘) · 사다리(같은 측선 거리 t의 E′ 점 ↔ E 점 = 가로대)
        //   E′ ∥ E(거리 vE)라 t가 같으면 수선 짝 — E′ 점마다 E 위 발 · E의 P0 변 건넘마다 E′ 위 짝(검토 M3 — 짝 없는 촘촘한 점이 vE 띠에 바늘을 만든다)
        var rungs = new List<List<Point3>>();
        (List<Point3> E, List<Point3> Ep, double Max, (double, double) At, double Area, string Bad) End(Coordinate p, (double X, double Y) e, double len, (double X, double Y) n, Coordinate s0, Coordinate s1, bool atStart, Coordinate pEnd)
        {
            // E′ 바탕 점(t · z) — A*(z = G) · 줄 끝(그 줄 높이) · A″(z = Z0)
            var bp = new List<(double T, Coordinate C, double Z)> { (TE(s0, p, e), s0, ZK(-1, s0.X, s0.Y)), (TE(s1, p, e), s1, ZK(0, s1.X, s1.Y)) };
            foreach (var (_, _, P) in wall) { var q = atStart ? P[0] : P[^1]; var c = new Coordinate(q.X - ox, q.Y - oy); bp.Add((TE(c, p, e), c, q.Z)); }
            foreach (var (atA, c) in fanOnEp) if (atA == atStart) bp.Add((TE(c, p, e), c, ZK(0, c.X, c.Y)));   // 부채선 시작(띠 안 · z = Z0)
            bp.Sort((u, v) => u.T.CompareTo(v.T));
            double t0 = bp[0].T, t1 = bp[^1].T;
            double ZEp(double t)   // E′ 높이 — 바탕 점 사이 1차(벽 면이 줄 사이 1차)
            {
                if (t <= bp[0].T) return bp[0].Z;
                for (int i = 0; i + 1 < bp.Count; i++) if (t <= bp[i + 1].T) { double w = bp[i + 1].T - bp[i].T; return w < 1e-15 ? bp[i + 1].Z : bp[i].Z + (bp[i + 1].Z - bp[i].Z) * (t - bp[i].T) / w; }
                return bp[^1].Z;
            }
            Coordinate OnEp(double t) => new(p.X + e.X * t + n.X * vE, p.Y + e.Y * t + n.Y * vE);
            Coordinate OnE(double t) => new(p.X + e.X * t, p.Y + e.Y * t);
            // E 위 P0 변 건넘(정확한 꺾은선)
            var cross = new List<(double T, double Z)>();
            var pr = ProfileOnTris(Pw(p, 0), Pw(pEnd, 0), inp.P0Tris, C.P0w, inp.Grid);
            if (pr != null) for (int i = 1; i + 1 < pr.Count; i++) { var c = new Coordinate(pr[i].X - ox, pr[i].Y - oy); cross.Add((TE(c, p, e), pr[i].Z)); }
            // E′ 점 — 바탕(지킴) + 0.5 m 채움 + 건넘 짝(E′ 범위 안)
            var ep = bp.Select(u => (u.T, u.C, Keep: true)).ToList();
            for (int i = 0; i + 1 < bp.Count; i++)
            {
                double sl = bp[i + 1].T - bp[i].T; int nn = (int)Math.Ceiling(sl / 0.5);
                for (int u = 1; u < nn; u++) { double t = bp[i].T + sl * u / nn; ep.Add((t, OnEp(t), false)); }
            }
            foreach (var (t, _) in cross) if (t > t0 + MinSp && t < t1 - MinSp && Clear(OnEp(t))) ep.Add((t, OnEp(t), false));
            ep = ep.Where(u => u.Keep || Clear(u.C)).ToList();
            int mgE = 0;
            ep = Thin(ep.OrderBy(u => u.T).ThenByDescending(u => u.Keep).ToList(), ref mgE);
            // E 점 — A(z = G · 고리 L과 같은 점) · A′ · 건넘 · E′ 점마다 발
            var es = new List<(double T, Coordinate C, bool Keep)> { (0, p, true), (len, pEnd, true) };
            var zE = new Dictionary<Coordinate, double>(ReferenceEqualityComparer.Instance) { [p] = ZK(-1, p.X, p.Y), [pEnd] = ZK(0, pEnd.X, pEnd.Y) };
            foreach (var (t, _, _) in ep)
            {
                if (t < MinSp || t > len - MinSp) continue;
                var fc = OnE(t); var zf = P0Read(fc, n);
                if (zf == null) return (new(), new(), 0, Wd(fc), 0, string.Format(ci, "측선 위 합성 전 면 높이를 못 읽었습니다 ({0:F1}, {1:F1})", fc.X + ox, fc.Y + oy));
                es.Add((t, fc, true)); zE[fc] = zf.Value;
            }
            foreach (var (t, z) in cross) { if (t <= MinSp || t >= len - MinSp) continue; var c = OnE(t); if (!Clear(c)) continue; es.Add((t, c, false)); zE[c] = z; }
            int mgE2 = 0;
            es = Thin(es.OrderBy(u => u.T).ThenByDescending(u => u.Keep).ToList(), ref mgE2);
            // E′ 선 · 가로대(E′ 점의 발이 E에 남은 것) · 날개 높이 |W − P0|
            var zBp = new Dictionary<Coordinate, double>(ReferenceEqualityComparer.Instance);
            foreach (var b in bp) zBp[b.C] = b.Z;
            var epP = ep.Select(u => Pw(u.C, zBp.TryGetValue(u.C, out double zb) ? zb : ZEp(u.T))).ToList();
            var eByT = new Dictionary<double, Coordinate>();
            foreach (var u in es) if (u.Keep && u.T > 0 && u.T < len) eByT.TryAdd(u.T, u.C);
            double mx = 0; (double, double) at = Wd(p);
            var hs = new List<(double T, double H)> { (0, Math.Abs(zE[p] - (P0Read(p, n) ?? zE[p]))), (len, 0) };
            for (int i = 0; i < ep.Count; i++)
            {
                if (!eByT.TryGetValue(ep[i].T, out var fc)) continue;
                var q = epP[i]; double zf = zE[fc];
                rungs.Add(new List<Point3> { q, Pw(fc, zf) });
                double h = Math.Abs(q.Z - zf); hs.Add((ep[i].T, h));
                if (h > mx) { mx = h; at = (q.X, q.Y); }
            }
            hs.Sort((u, v) => u.T.CompareTo(v.T));
            double area = 0; for (int i = 0; i + 1 < hs.Count; i++) area += (hs[i].H + hs[i + 1].H) / 2 * (hs[i + 1].T - hs[i].T);
            return (es.Select(u => Pw(u.C, zE[u.C])).ToList(), epP, mx, at, area, "");
        }
        var endA = End(cA, eA, lenA, nA, aS, a2, true, a1); if (endA.Bad.Length > 0) return No("정지면", endA.Bad, endA.At);
        int rA = rungs.Count;
        var endB = End(cB, eB, lenB, nB, bS, b2, false, b1); if (endB.Bad.Length > 0) return No("정지면", endB.Bad, endB.At);
        var epA = endA.Ep; var epB = endB.Ep;
        R.EndRungs = rungs.Count;
        R.EndMaxA = endA.Max; R.EndAtA = endA.At; R.WingAreaA = endA.Area; R.WingLenA = aS.Distance(a2);
        R.EndMaxB = endB.Max; R.EndAtB = endB.At; R.WingAreaB = endB.Area; R.WingLenB = bS.Distance(b2);
        R.EndStepA = Math.Abs(C.GAt(0) - (P0Read(cA, nA) ?? C.GAt(0))); R.EndStepB = Math.Abs(C.GAt(pl.Len) - (P0Read(cB, nB) ?? C.GAt(pl.Len)));
        log.Append(string.Format(ci, "  ⑥ 마구리 vE {0:F3} · 가로대 A {1} · B {2} · 날개 최대 A {3:F2} m @({4:F1},{5:F1}) · B {6:F2} m @({7:F1},{8:F1}) · 날개 길이 A {9:F2} · B {10:F2} m · 넓이 A {11:F1} · B {12:F1}㎡ · 끝 G−P0 A {13:F3} · B {14:F3} m\n",
            vE, rA, rungs.Count - rA, R.EndMaxA, R.EndAtA.X, R.EndAtA.Y, R.EndMaxB, R.EndAtB.X, R.EndAtB.Y, R.WingLenA, R.WingLenB, R.WingAreaA, R.WingAreaB, R.EndStepA, R.EndStepB));

        // 바닥 호(B′ → A′) — 고리 꼭짓점 · A″ · B″ · P0 변 건넘(z = Z0 = P0)
        var arcPts = new List<(double T, Coordinate C, bool Keep)>();
        {
            double acc = 0;
            for (int i = 0; i < arc.Count; i++)
            {
                arcPts.Add((acc, arc[i], true));
                if (i + 1 >= arc.Count) break;
                var p = arc[i]; var q = arc[i + 1]; double sl = p.Distance(q);
                foreach (var x in new[] { a2, b2 })
                {
                    double t = sl < 1e-12 ? 0 : ((x.X - p.X) * (q.X - p.X) + (x.Y - p.Y) * (q.Y - p.Y)) / (sl * sl);
                    double dx = p.X + (q.X - p.X) * t - x.X, dy = p.Y + (q.Y - p.Y) * t - x.Y;
                    if (t > 0 && t < 1 && Math.Sqrt(dx * dx + dy * dy) < 1e-7) arcPts.Add((acc + t * sl, x, true));
                }
                var pr = ProfileOnTris(Pw(p, 0), Pw(q, 0), inp.P0Tris, C.P0w, inp.Grid);
                if (pr != null) for (int k = 1; k + 1 < pr.Count; k++) { var c = new Coordinate(pr[k].X - ox, pr[k].Y - oy); if (Clear(c)) arcPts.Add((acc + p.Distance(c), c, false)); }
                acc += sl;
            }
            int mg4 = 0;
            arcPts = Thin(arcPts.OrderBy(u => u.T).ThenByDescending(u => u.Keep).ToList(), ref mg4);
        }
        var arcFin = arcPts.Select(u => Pw(u.C, ZK(0, u.C.X, u.C.Y))).ToList();
        foreach (var q in arcFin) { var z = C.P0w(q.X, q.Y); if (z != null) R.FloorStep = Math.Max(R.FloorStep, Math.Abs(q.Z - z.Value)); }

        // ⑦ Z 고리(첫 점 반복 없음) — L(A → B · z = G) → E_B(z = P0) → 바닥 호(z = P0) → E_A 거꾸로
        var ring = new List<Point3>(lPts);
        ring.AddRange(endB.E.Skip(1));
        ring.AddRange(arcFin.Skip(1));
        var eaRev = Enumerable.Reverse(endA.E).ToList();
        ring.AddRange(eaRev.Skip(1).Take(Math.Max(0, eaRev.Count - 2)));
        for (int i = ring.Count - 1; i >= 1; i--) if (Math.Abs(ring[i].X - ring[i - 1].X) < 1e-12 && Math.Abs(ring[i].Y - ring[i - 1].Y) < 1e-12) ring.RemoveAt(i);
        Polygon zFin;
        try { zFin = gf.CreatePolygon(ring.Select(q => new Coordinate(q.X - ox, q.Y - oy)).Append(new Coordinate(ring[0].X - ox, ring[0].Y - oy)).ToArray()); }
        catch (Exception ex) { return No("고리", $"바꿀 자리 고리를 못 만들었습니다({ex.GetType().Name})", (R.A.X, R.A.Y)); }
        if (!zFin.IsValid || Math.Abs(zFin.Area - zc.Area) > 1e-6 + 1e-9 * zc.Area)
            return No("고리", string.Format(ci, "바꿀 자리 고리가 꼬입니다(넓이 {0:F4} → {1:F4}㎡) — 선 끝을 옮기세요", zc.Area, zFin.Area), (R.A.X, R.A.Y));
        // 막을 다각형 · 정지면 밖
        if (inp.Blockers != null)
            foreach (var (name, br) in inp.Blockers)
            {
                var bp2 = BoundaryCutPoly(gf, br, ox, oy);
                if (bp2 == null) continue;
                Geometry? og = null; try { og = zFin.Intersection(bp2); } catch { try { og = zFin.Buffer(0).Intersection(bp2.Buffer(0)); } catch { } }
                if (og == null) return No("겹침", $"바꿀 자리와 {name}의 겹침을 못 쟀습니다 — 막습니다", (R.A.X, R.A.Y));
                if (og.Area > 1e-2) return No("겹침", string.Format(ci, "바꿀 자리가 {0}과 {1:F2}㎡ 겹칩니다 — 막습니다(자르면 턱이 생긴다)", name, og.Area), Wd(og.InteriorPoint.Coordinate));
            }
        {
            int miss = 0; (double, double)? at = null;
            foreach (var q in ring) if (C.P0w(q.X, q.Y) == null) { miss++; at ??= (q.X, q.Y); }
            if (miss > 0) return No("정지면 밖", $"바꿀 자리 {miss}점이 정지면_DH 밖입니다 — 선을 옮기세요", at!.Value);
        }

        // ⑦ 관문 — 줄 이어짐(옛 소단 · JumpOver) · 바닥 Z0 끊김 · 안쪽 면 높이
        {
            foreach (var (_, k, P) in wall)
                for (int i = 0; i + 1 < P.Count; i++)
                {
                    double dist = Math.Sqrt((P[i + 1].X - P[i].X) * (P[i + 1].X - P[i].X) + (P[i + 1].Y - P[i].Y) * (P[i + 1].Y - P[i].Y));
                    if (k == 0)
                    {
                        double dz = Math.Abs(P[i + 1].Z - P[i].Z);
                        if (dz > mg * dist + 0.01) { if (R.Z0Jump++ == 0) R.Z0JumpAt = (P[i].X, P[i].Y); }
                        continue;
                    }
                    R.JumpN++;
                    double over = ob!.JumpOver(k, P[i].X, P[i].Y, P[i].Z, P[i + 1].X, P[i + 1].Y, P[i + 1].Z, dist);
                    if (double.IsNaN(over)) { if (R.JumpLeg++ == 0) { R.JumpLegAt = (P[i].X, P[i].Y); R.JumpLegWhy = ob.JumpWhy; } R.JumpBad++; continue; }
                    if (over > R.JumpMax) { R.JumpMax = over; R.JumpAt = (P[i].X, P[i].Y); }
                    if (over > 0.01) R.JumpBad++;
                }
            for (int j = 0; j + 1 < wall.Count; j++)
            {
                var (d0, k0, P0) = wall[j]; var (d1, k1, _) = wall[j + 1];
                if (k0 < 1 || k1 != k0 - 1) continue;
                double w = d1 - d0, lim = ob!.Tier(k0).H + mg * w + 0.01;
                foreach (var q in P0)
                {
                    var (nx, ny) = Nrm(q.X - ox, q.Y - oy);
                    double h = sg * (q.Z - ZK(k1, q.X - ox + nx * w, q.Y - oy + ny * w));
                    R.FaceHN++; R.FaceHMin = Math.Min(R.FaceHMin, h); R.FaceHMax = Math.Max(R.FaceHMax, h);
                    if (!(h > 1e-4 && h <= lim)) { if (R.FaceHBad++ == 0) R.FaceHAt = (q.X, q.Y); }
                }
            }
            // 계획 1-11 ④-c — L 위 «규칙»(옛 소단 띠 안 |P0 − 옛 소단|)은 막지 않고 로그만(새 기하는 L 위 P0를 안 쓴다)
            if (ob != null)
                foreach (var q in C.Smp)
                    for (int k = 1; k <= Math.Min(ob.K, 400); k++)
                    {
                        if (!ob.InBand(k, q.X, q.Y)) continue;
                        var pz = C.P0w(q.X, q.Y); if (pz == null) continue;
                        double dz = Math.Abs(pz.Value - ob.Level(k, q.X, q.Y));
                        R.RuleN++; if (dz > R.RuleMax) { R.RuleMax = dz; R.RuleAt = (q.X, q.Y); }
                        if (dz > 0.01) R.RuleMiss++;
                    }
            log.Append(string.Format(ci, "  ⑦ 이어짐 {0}(끊김 {1} · 골 {2}) · 바닥 Z0 끊김 {3} · 안쪽 면 높이 {4}~{5} m({6}점 · 어긋남 {7}) · L 위 옛 소단 규칙(로그만) {8}점(1 cm 넘음 {9} · 최대 {10:F1} mm)\n",
                R.JumpN, R.JumpBad, R.JumpLeg, R.Z0Jump, R.FaceHN > 0 ? R.FaceHMin.ToString("F3", ci) : "—", R.FaceHN > 0 ? R.FaceHMax.ToString("F3", ci) : "—", R.FaceHN, R.FaceHBad, R.RuleN, R.RuleMiss, R.RuleMax * 1000));
            if (R.Z0Jump > 0)
                return No("바닥 높이 끊김", string.Format(ci, "바닥 높이 끊김 — 굽은 안쪽 · 오목한 바닥 곁 ({0:F1}, {1:F1}) · {2}곳 — 선을 옮기세요", R.Z0JumpAt.X, R.Z0JumpAt.Y, R.Z0Jump), R.Z0JumpAt);
            if (R.JumpLeg > 0)
                return No("옛 소단", string.Format(ci, "옛 소단 높이를 이어 읽지 못함 ({0:F1}, {1:F1}) — {2}(이웃 점 {3}쌍 중 {4}쌍) — 선을 옮기세요",
                    R.JumpLegAt.X, R.JumpLegAt.Y, R.JumpLegWhy.Length > 0 ? R.JumpLegWhy : "이어지지 않는 자리", R.JumpN, R.JumpLeg), R.JumpLegAt);
            if (R.JumpBad > 0)
                return No("옛 소단", string.Format(ci, "옛 소단 높이가 이 자리에서 끊김 ({0:F1}, {1:F1}) Δ {2:F2} m(한도 maxGrad·거리 + 0.01 · 이웃 점 {3}쌍 중 {4}쌍) — 선을 옮기세요(링 이등분선 · 접힘 곁)",
                    R.JumpAt.X, R.JumpAt.Y, R.JumpMax + 0.01, R.JumpN, R.JumpBad), R.JumpAt);
            if (R.FaceHBad > 0)
                return No("면 높이", string.Format(ci, "옛 소단 사이 벽 면 높이가 맞지 않음 ({0:F1}, {1:F1}) h {2:F3}~{3:F3} m(0 < h ≤ 단 간격 + maxGrad·면 폭 + 1 cm · {4}점 중 {5}점) — 선을 옮기세요",
                    R.FaceHAt.X, R.FaceHAt.Y, R.FaceHMin, R.FaceHMax, R.FaceHN, R.FaceHBad), R.FaceHAt);
        }

        // 선(Civil 가상옹벽 브레이크라인) · 세모 — [0] = Z 고리(닫힘) · 줄 · E′ 둘 · 가로대
        var ringClosed = new List<Point3>(ring) { ring[0] };
        var lines = new List<List<Point3>> { ringClosed };
        foreach (var (_, _, P) in wall) lines.Add(P);
        lines.Add(epA); lines.Add(epB); lines.AddRange(rungs); lines.AddRange(fans); lines.AddRange(ladder);
        R.Lines = lines;
        var tris = new List<Tri>();
        foreach (var t in MeshLines(lines.Skip(1).Append(ringClosed).ToList(), ox, oy))
        {
            double ar = Math.Abs((t.B.X - t.A.X) * (t.C.Y - t.A.Y) - (t.B.Y - t.A.Y) * (t.C.X - t.A.X));
            double lm = Math.Sqrt(Math.Max((t.A.X - t.B.X) * (t.A.X - t.B.X) + (t.A.Y - t.B.Y) * (t.A.Y - t.B.Y), Math.Max((t.B.X - t.C.X) * (t.B.X - t.C.X) + (t.B.Y - t.C.Y) * (t.B.Y - t.C.Y), (t.C.X - t.A.X) * (t.C.X - t.A.X) + (t.C.Y - t.A.Y) * (t.C.Y - t.A.Y))));
            if (lm > 0 && ar / lm >= 1e-9) tris.Add(t);
        }
        if (tris.Count == 0) return No("줄", "벽 면을 못 지었습니다", (R.A.X, R.A.Y));
        (S.Dir >= 0 ? R.WallTrisC : R.WallTrisF).AddRange(tris);
        R.RawTris.AddRange(tris);

        // ⑨ 자가검증 — 0.1 mm 안 점 쌍(네모 · Civil 합침) · 0.25 mm 미만 세모 변 · 바늘(높이 < 1.22e-4) · 끝 쐐기
        var needleAt = new List<string>();
        {
            var all = new HashSet<(double, double)>();
            foreach (var l in R.Lines) foreach (var q in l) all.Add((q.X, q.Y));
            var cell = new Dictionary<(long, long), List<(double X, double Y)>>();
            (long, long) CK(double x, double y) => ((long)Math.Floor((x - ox) / CivilSafeMesh.Gap), (long)Math.Floor((y - oy) / CivilSafeMesh.Gap));
            foreach (var p in all) { var k = CK(p.Item1, p.Item2); if (!cell.TryGetValue(k, out var l)) cell[k] = l = new(); l.Add(p); }
            foreach (var p in all)
            {
                var (kx, ky) = CK(p.Item1, p.Item2);
                for (long a = -1; a <= 1; a++) for (long b = -1; b <= 1; b++)
                        if (cell.TryGetValue((kx + a, ky + b), out var l))
                            foreach (var q in l)
                            {
                                if (q.X == p.Item1 && q.Y == p.Item2) continue;
                                if (Math.Max(Math.Abs(q.X - p.Item1), Math.Abs(q.Y - p.Item2)) < CivilSafeMesh.Gap && (q.X, q.Y).CompareTo(p) > 0) R.PairsUnder01++;
                            }
            }
            var seen = new HashSet<((double, double), (double, double))>();
            R.WedgeMin = double.MaxValue;
            foreach (var t in tris)
            {
                foreach (var (u, v) in new[] { (t.A, t.B), (t.B, t.C), (t.C, t.A) })
                {
                    var ku = (u.X, u.Y); var kv = (v.X, v.Y); var key = ku.CompareTo(kv) < 0 ? (ku, kv) : (kv, ku);
                    if (!seen.Add(key)) continue;
                    double l2 = Math.Sqrt((u.X - v.X) * (u.X - v.X) + (u.Y - v.Y) * (u.Y - v.Y));
                    if (l2 > 0 && l2 < MinSp) R.ShortEdges++;
                }
                double ar = Math.Abs((t.B.X - t.A.X) * (t.C.Y - t.A.Y) - (t.B.Y - t.A.Y) * (t.C.X - t.A.X));
                double lm = Math.Sqrt(Math.Max((t.A.X - t.B.X) * (t.A.X - t.B.X) + (t.A.Y - t.B.Y) * (t.A.Y - t.B.Y), Math.Max((t.B.X - t.C.X) * (t.B.X - t.C.X) + (t.B.Y - t.C.Y) * (t.B.Y - t.C.Y), (t.C.X - t.A.X) * (t.C.X - t.A.X) + (t.C.Y - t.A.Y) * (t.C.Y - t.A.Y))));
                double hgt = lm > 0 ? ar / lm : 0;
                if (hgt < 1.22e-4) { if (R.Needles++ < 3) needleAt.Add(string.Format(ci, "({0:F4},{1:F4})·({2:F4},{3:F4})·({4:F4},{5:F4}) h {6:F1} µm 변 {7:F4} m", t.A.X, t.A.Y, t.B.X, t.B.Y, t.C.X, t.C.Y, hgt * 1e6, lm)); }
                bool AtEnd(Point3 q) => (Math.Abs(q.X - R.A.X) < 1e-9 && Math.Abs(q.Y - R.A.Y) < 1e-9) || (Math.Abs(q.X - R.B.X) < 1e-9 && Math.Abs(q.Y - R.B.Y) < 1e-9);
                if (AtEnd(t.A) || AtEnd(t.B) || AtEnd(t.C)) R.WedgeMin = Math.Min(R.WedgeMin, hgt);
            }
            if (R.WedgeMin == double.MaxValue) R.WedgeMin = double.NaN;
        }

        // ⑩ 관문 표본 — Kind 0 = 소단 가운데(± 0.5 m · z = Z_k) + 평평한 띠(0.5 m · z = Z0) · Kind 1 = L 위(0.5 m · z = G) · E′에서 vE 안은 뺀다
        bool NearE(Coordinate c)
        {
            static double SegD(Coordinate c, Coordinate a, Coordinate b)
            {
                double ex = b.X - a.X, ey = b.Y - a.Y, l2 = ex * ex + ey * ey, t = l2 < 1e-24 ? 0 : Math.Clamp(((c.X - a.X) * ex + (c.Y - a.Y) * ey) / l2, 0, 1);
                double dx = c.X - a.X - ex * t, dy = c.Y - a.Y - ey * t; return Math.Sqrt(dx * dx + dy * dy);
            }
            return SegD(c, aS, a2) < vE || SegD(c, bS, b2) < vE;
        }
        var bandIdx = new HashSet<int>();
        void Samp(double dm, Func<double, double, double> z, bool band)
        {
            foreach (var ls in Pieces(dm))
            {
                var cs = ls.Coordinates;
                for (int i = 0; i + 1 < cs.Length; i++)
                {
                    int nn = Math.Max(1, (int)Math.Ceiling(cs[i].Distance(cs[i + 1]) / 0.5));
                    for (int u = i == 0 ? 0 : 1; u <= nn; u++)
                    {
                        var c = new Coordinate(cs[i].X + (cs[i + 1].X - cs[i].X) * u / nn, cs[i].Y + (cs[i + 1].Y - cs[i].Y) * u / nn);
                        if (NearE(c)) continue;
                        if (band) bandIdx.Add(R.BenchPts.Count);
                        R.BenchPts.Add((c.X + ox, c.Y + oy, z(c.X, c.Y), 0));
                    }
                }
            }
        }
        for (int i = 1; i + 1 < rows.Count; i++)
        {
            var (d0, k0) = rows[i]; var (d1, k1) = rows[i + 1];
            if (k0 < 1 || k1 != k0) continue;   // 소단(같은 단 두 줄)
            double w0 = d0 + vE, w1 = d1 - vE;
            if (w1 - w0 < 1e-6) continue;
            double mid = (w0 + w1) / 2; int nh = (int)Math.Floor((w1 - w0) / 2 / 0.5 + 1e-9);
            int kk = k0;
            for (int h = -nh; h <= nh; h++) Samp(mid + h * 0.5, (x, y) => ZK(kk, x, y), false);
        }
        double bandFar = 0; foreach (var q in arcFin) bandFar = Math.Max(bandFar, extG.Distance(gf.CreatePoint(new Coordinate(q.X - ox, q.Y - oy))));
        for (double dm = D + vE; dm < bandFar; dm += 0.5) Samp(dm, (x, y) => ZK(0, x, y), true);
        for (int i = 0; i + 1 < row0.Count; i++)
        {
            var a = row0[i]; var e2 = row0[i + 1];
            int nn = Math.Max(1, (int)Math.Ceiling(Math.Sqrt((e2.X - a.X) * (e2.X - a.X) + (e2.Y - a.Y) * (e2.Y - a.Y)) / 0.5));
            for (int u = i == 0 ? 0 : 1; u <= nn; u++) R.BenchPts.Add((a.X + (e2.X - a.X) * u / nn, a.Y + (e2.Y - a.Y) * u / nn, a.Z + (e2.Z - a.Z) * u / nn, 1));
        }
        {
            var wallAt = TriLookup(tris);
            int atKind = 0;
            for (int i = 0; i < R.BenchPts.Count; i++)
            {
                var (x, y, z0, kind) = R.BenchPts[i];
                double? z = wallAt(x, y);
                bool band = bandIdx.Contains(i);
                if (kind == 0) R.BenchN++; else R.FootN++;
                if (band) R.BandN++;
                if (z == null) { if (kind == 0) { R.BenchMiss++; if (R.BenchSag + R.FootBad == 0 && R.BenchMiss == 1) R.BenchAt = (x, y); } else R.FootMiss++; continue; }
                double dz = Math.Abs(z.Value - z0);
                if (dz > 1e-2) { if (kind == 0) R.BenchSag++; else R.FootBad++; if (band) R.BandBad++; }
                if (dz > R.BenchMax) { R.BenchMax = dz; R.BenchAt = (x, y); atKind = kind; }
            }
            // 마구리 관문 — E′(바꿀 자리 안)를 0.25 m마다: 세모 변 위(제약으로 들어감)면 통과 · 세모 안을 지나면 면 높이 = 선 높이(1 cm)
            var wtTree = new STRtree<int>();
            for (int i = 0; i < tris.Count; i++) { var t = tris[i]; wtTree.Insert(new Envelope(Math.Min(t.A.X, Math.Min(t.B.X, t.C.X)), Math.Max(t.A.X, Math.Max(t.B.X, t.C.X)), Math.Min(t.A.Y, Math.Min(t.B.Y, t.C.Y)), Math.Max(t.A.Y, Math.Max(t.B.Y, t.C.Y))), i); }
            wtTree.Build();
            static double SegD3(double px, double py, Point3 a, Point3 b)
            {
                double ex = b.X - a.X, ey = b.Y - a.Y, l2 = ex * ex + ey * ey, t = l2 < 1e-24 ? 0 : Math.Clamp(((px - a.X) * ex + (py - a.Y) * ey) / l2, 0, 1);
                double dx = px - a.X - ex * t, dy = py - a.Y - ey * t; return Math.Sqrt(dx * dx + dy * dy);
            }
            foreach (var el in new[] { epA, epB })
                for (int i = 0; i + 1 < el.Count; i++)
                {
                    var a = el[i]; var e2 = el[i + 1];
                    int nn = Math.Max(1, (int)Math.Ceiling(Math.Sqrt((e2.X - a.X) * (e2.X - a.X) + (e2.Y - a.Y) * (e2.Y - a.Y)) / 0.25));
                    for (int u = i == 0 ? 0 : 1; u <= nn; u++)
                    {
                        double t = (double)u / nn, x = a.X + (e2.X - a.X) * t, y = a.Y + (e2.Y - a.Y) * t, zl = a.Z + (e2.Z - a.Z) * t;
                        double? zw = null; bool onEdge = false;
                        foreach (int ti in wtTree.Query(new Envelope(x, x, y, y)))
                        {
                            var tr = tris[ti];
                            double dd = (tr.B.Y - tr.C.Y) * (tr.A.X - tr.C.X) + (tr.C.X - tr.B.X) * (tr.A.Y - tr.C.Y);
                            if (Math.Abs(dd) < 1e-18) continue;
                            double l1 = ((tr.B.Y - tr.C.Y) * (x - tr.C.X) + (tr.C.X - tr.B.X) * (y - tr.C.Y)) / dd, l2 = ((tr.C.Y - tr.A.Y) * (x - tr.C.X) + (tr.A.X - tr.C.X) * (y - tr.C.Y)) / dd, l3 = 1 - l1 - l2;
                            if (l1 < -1e-9 || l2 < -1e-9 || l3 < -1e-9) continue;
                            if (Math.Min(SegD3(x, y, tr.A, tr.B), Math.Min(SegD3(x, y, tr.B, tr.C), SegD3(x, y, tr.C, tr.A))) < 1e-6) { onEdge = true; break; }
                            zw ??= l1 * tr.A.Z + l2 * tr.B.Z + l3 * tr.C.Z;
                        }
                        if (onEdge || zw == null) continue;
                        R.EndN++;
                        double dz = Math.Abs(zw.Value - zl);
                        if (dz > 1e-2) R.EndMiss++;
                        if (dz > R.EndMax) { R.EndMax = dz; R.EndAt = (x, y); }
                    }
                }
            log.Append(string.Format(ci, "  ⑨ 자가검증 — 0.1 mm 안 점 쌍 {0}(0) · 0.25 mm 미만 변 {1}(지키는 점끼리 {2}) · 바늘 {3}(0){4} · 바닥 턱 {5:F1} mm · 끝 쐐기 최소 높이 {6} mm · 벽 세모 {7} · 부채선 {8} · 현 위 줄 점 {9}\n",
                R.PairsUnder01, R.ShortEdges, shortKeep, R.Needles, needleAt.Count > 0 ? " " + string.Join(" / ", needleAt) : "", R.FloorStep * 1000, F(R.WedgeMin * 1000, "F2"), tris.Count, fans.Count, R.RowChord));
            log.Append(string.Format(ci, "  ⑩ 관문 — 소단 {0}(틀림 {1} · 면 없음 {2}) · 띠 {3}(틀림 {4}) · L {5}(턱 {6} · 못 읽음 {7}) · 마구리 {8}(어긋남 {9} · 최대 {10:F1} mm) · 최대 {11:F1} mm @({12:F2},{13:F2})\n",
                R.BenchN - R.BandN, R.BenchSag - R.BandBad, R.BenchMiss, R.BandN, R.BandBad, R.FootN, R.FootBad, R.FootMiss, R.EndN, R.EndMiss, R.EndMax * 1000, R.BenchMax * 1000, R.BenchAt.X, R.BenchAt.Y));
            if (R.EndMiss > 0 && !BenchGateOff)
                return No("마구리", string.Format(ci, "벽 면이 끝 마감선을 안 따름({0:F1}, {1:F1} · 차 {2:F0} cm) — 선 끝을 옮기세요", R.EndAt.X, R.EndAt.Y, R.EndMax * 100), R.EndAt);
            if (R.BenchSag + R.BenchMiss + R.FootBad > 0 && !BenchGateOff)
                return No("소단 높이", R.BenchSag + R.FootBad == 0
                    ? string.Format(ci, "옹벽 소단에 벽 면이 없음({0:F1}, {1:F1} · {2}점) — 소단 폭을 줄이거나 선을 펴세요", R.BenchAt.X, R.BenchAt.Y, R.BenchMiss)
                    : string.Format(ci, "옹벽 {0} 높이가 맞지 않음({1:F1}, {2:F1} · 차 {3:F0} cm) — 소단 폭을 줄이거나 선을 펴세요", atKind == 0 ? "소단" : "선(L)", R.BenchAt.X, R.BenchAt.Y, R.BenchMax * 100), R.BenchAt);
        }

        // ⑪ 벽 안쪽 메움(절토 벽인데 W > G + 1 cm · 성토 거울 — 막지 않고 ⚠) · 바닥 넓힘(평평한 띠)
        //   벽 세모를 0.5 m 안 작은 세모(n × n · 무게중심 표본)로 나눠 잰다 — 띠 세모는 m 단위라 무게중심 하나로는 골짜기를 놓치거나 통째로 센다
        if (S.Dir != 0)
            foreach (var t in tris)
            {
                double ar = Math.Abs((t.B.X - t.A.X) * (t.C.Y - t.A.Y) - (t.B.Y - t.A.Y) * (t.C.X - t.A.X)) / 2;
                double lm = Math.Max(Math.Max(Math.Sqrt((t.A.X - t.B.X) * (t.A.X - t.B.X) + (t.A.Y - t.B.Y) * (t.A.Y - t.B.Y)), Math.Sqrt((t.B.X - t.C.X) * (t.B.X - t.C.X) + (t.B.Y - t.C.Y) * (t.B.Y - t.C.Y))), Math.Sqrt((t.C.X - t.A.X) * (t.C.X - t.A.X) + (t.C.Y - t.A.Y) * (t.C.Y - t.A.Y)));
                int n = Math.Clamp((int)Math.Ceiling(lm / 0.5), 1, 40);
                foreach (var q in new[] { t.A, t.B, t.C }) { var gq = inp.Ground(q.X, q.Y); if (gq != null) R.InnerFillMax = Math.Max(R.InnerFillMax, sg * (q.Z - gq.Value)); }
                for (int i = 0; i < n; i++)
                    for (int j = 0; j < n - i; j++)
                        for (int up = 0; up < (j < n - i - 1 ? 2 : 1); up++)
                        {
                            // 작은 세모 무게중심(무게중심 좌표 u · v)
                            double u = up == 0 ? (i + 1.0 / 3) / n : (i + 2.0 / 3) / n, v = up == 0 ? (j + 1.0 / 3) / n : (j + 2.0 / 3) / n;
                            double x = t.A.X + (t.B.X - t.A.X) * u + (t.C.X - t.A.X) * v, y = t.A.Y + (t.B.Y - t.A.Y) * u + (t.C.Y - t.A.Y) * v, z = t.A.Z + (t.B.Z - t.A.Z) * u + (t.C.Z - t.A.Z) * v;
                            var g = inp.Ground(x, y); if (g == null) continue;
                            double dz = sg * (z - g.Value);
                            R.InnerFillMax = Math.Max(R.InnerFillMax, dz);
                            if (dz > 0.01) R.InnerFillArea += ar / (n * n);
                        }
            }
        try { R.BandArea = zp.Area - zp.Intersection(extG.Buffer(D, bufP)).Area; } catch { R.BandArea = double.NaN; }
        R.BandMaxW = Math.Max(0, bandFar - D);
        log.Append(string.Format(ci, "  ⑪ 바닥 넓힘 {0:F1}㎡(최대 폭 {1:F2} m) · 벽 안쪽 메움 {2:F2}㎡(최대 {3:F3} m)\n", R.BandArea, R.BandMaxW, R.InnerFillArea, R.InnerFillMax));

        // 면 토막(소비처 형식 그대로) — Kind 0: 바깥 벽(Tier 1) · 안쪽 벽(2..) · Kind 1: 두 끝 날개벽(가로대마다 W · P0)
        {
            int tier = 0;
            void Face(List<Point3> outer, double w, int kIn)
            {
                tier++;
                var runs = new List<(bool Up, List<(Point3 O, Point3 I)> P)>();
                foreach (var q in outer)
                {
                    var (nx, ny) = Nrm(q.X - ox, q.Y - oy);
                    double ix = q.X - ox + nx * w, iy = q.Y - oy + ny * w;
                    var qi = new Point3(ix + ox, iy + oy, ZK(kIn, ix, iy));
                    bool up = S.Dir != 0 ? S.Dir > 0 : q.Z - qi.Z > 0;
                    if (S.Dir == 0 && runs.Count > 0 && Math.Abs(q.Z - qi.Z) <= 1e-4) up = runs[^1].Up;
                    if (runs.Count == 0 || runs[^1].Up != up)
                    {
                        if (runs.Count > 0) { runs[^1].P.Add((q, qi)); R.Points.Add((q.X, q.Y, 0)); }
                        runs.Add((up, new()));
                    }
                    runs[^1].P.Add((q, qi));
                }
                foreach (var (up, P) in runs)
                {
                    if (P.Count < 2) continue;
                    var fs = new FaceSeg { Up = up, Tier = tier, Kind = 0, Part = 1 };
                    foreach (var (o, i) in P) { fs.Top.Add(up ? o : i); fs.Foot.Add(up ? i : o); fs.HMax = Math.Max(fs.HMax, Math.Abs(o.Z - i.Z)); }
                    for (int j = 0; j + 1 < fs.Top.Count; j++) fs.Len += Math.Sqrt((fs.Top[j + 1].X - fs.Top[j].X) * (fs.Top[j + 1].X - fs.Top[j].X) + (fs.Top[j + 1].Y - fs.Top[j].Y) * (fs.Top[j + 1].Y - fs.Top[j].Y));
                    var mid = P[P.Count / 2].I; fs.Level = mid.Z;
                    if (fs.HMax > 1e-4) R.Faces.Add(fs);
                }
            }
            Face(row0, fo, rows[1].K);
            for (int j = 0; j + 1 < wall.Count; j++) if (wall[j + 1].K == wall[j].K - 1) Face(wall[j].P, wall[j + 1].D - wall[j].D, wall[j + 1].K);
            foreach (var (rs, len) in new[] { (rungs.Take(rA).ToList(), R.WingLenA), (rungs.Skip(rA).ToList(), R.WingLenB) })
            {
                if (rs.Count == 0) continue;
                var fs = new FaceSeg { Up = S.Dir >= 0, Tier = 0, Kind = 1, Part = 1, Len = len, Level = double.MinValue };
                foreach (var r in rs)
                {
                    var hi = r[0].Z >= r[1].Z ? r[0] : r[1]; var lo = r[0].Z >= r[1].Z ? r[1] : r[0];
                    fs.Top.Add(hi); fs.Foot.Add(lo); fs.HMax = Math.Max(fs.HMax, hi.Z - lo.Z); fs.Level = Math.Max(fs.Level, hi.Z);
                }
                if (fs.HMax > 1e-4) R.Faces.Add(fs);
            }
        }

        // 목록 몫 — 바꿀 자리 · Poly(= Z ∪ R_out · R_out의 L 몫은 우리 L 점 배열) · 새 데이라잇(= L) · 줄 · 마감선
        (S.Dir >= 0 ? R.ZoneC : R.ZoneF).Add(ring);
        R.Foot.Add(lPts);
        R.Rows.Add(row0); foreach (var (_, _, P) in wall) R.Rows.Add(P);
        R.RawRows.AddRange(R.Rows);
        R.EndFaces.Add(epA); R.EndFaces.Add(epB);
        {
            var cr = inp.CutRing; int m = cr.Count; if (m >= 2 && cr[0].X == cr[^1].X && cr[0].Y == cr[^1].Y) m--;
            int Idx(Point3 p) { int bi = -1; double bd = 1e-6; for (int i = 0; i < m; i++) { double dd = Math.Sqrt((cr[i].X - p.X) * (cr[i].X - p.X) + (cr[i].Y - p.Y) * (cr[i].Y - p.Y)); if (dd < bd) { bd = dd; bi = i; } } return bi; }
            int iAo = Idx(R.A), iBo = Idx(R.B);
            if (iAo < 0 || iBo < 0 || iAo == iBo) return No("고리", "잘라낼 자리(R_out) 고리에서 선 끝 A · B를 못 찾았습니다", (R.A.X, R.A.Y));
            List<Point3> Run(int i0, int i1) { var r = new List<Point3>(); for (int j = i0, g = 0; g <= m; j = (j + 1) % m, g++) { r.Add(cr[j]); if (j == i1) break; } return r; }
            bool OnLw(double x, double y) => pl.Near(x - ox, y - oy).D < 1e-7;
            // 경계 호 = 변 가운데가 L 위가 아닌 변이 있는 쪽(경계 호에 꼭짓점이 없을 수 있다 — A · B가 경계 한 변 위)
            bool OffL(List<Point3> r) { for (int j = 0; j + 1 < r.Count; j++) if (!OnLw((r[j].X + r[j + 1].X) / 2, (r[j].Y + r[j + 1].Y) / 2) || (j > 0 && !OnLw(r[j].X, r[j].Y))) return true; return false; }
            var f1 = Run(iAo, iBo); var f2 = Run(iBo, iAo);
            bool bd1 = OffL(f1), bd2 = OffL(f2);
            if (bd1 == bd2) return No("고리", "잘라낼 자리(R_out) 고리에서 경계 호를 못 가렸습니다", (R.A.X, R.A.Y));
            var bdRun = bd1 ? f1 : Enumerable.Reverse(f2).ToList();   // A → B(경계 호)
            var poly = new List<Point3>(bdRun.Select(q => new Point3(q.X, q.Y, 0)));
            foreach (var q in ring.Skip(lPts.Count)) poly.Add(new Point3(q.X, q.Y, 0));   // B 다음 → E_B → 바닥 호 → E_A → (A 앞)
            Polygon? pp = null; try { pp = gf.CreatePolygon(poly.Select(q => new Coordinate(q.X - ox, q.Y - oy)).Append(new Coordinate(poly[0].X - ox, poly[0].Y - oy)).ToArray()); } catch { }
            double want = zFin.Area + C.CutP.Area;
            if (pp == null || !pp.IsValid || Math.Abs(pp.Area - want) > 1e-6 + 1e-9 * want)
                return No("고리", string.Format(ci, "바꿀 자리 ∪ R_out을 한 다각형으로 못 만들었습니다(넓이 {0:F3} · 기대 {1:F3}㎡)", pp?.Area ?? double.NaN, want), (R.A.X, R.A.Y));
            R.Poly = poly; R.IsWall = poly.Select(_ => false).ToList();
            // R_out 고리(L 몫 = 우리 L 점 배열) — 2단계 합성이 Z와 같은 L 점으로 맞붙이게
            R.OutRing = lPts.Select(q => new Point3(q.X, q.Y, 0)).Concat(Enumerable.Reverse(bdRun).Skip(1).Take(bdRun.Count - 2).Select(q => new Point3(q.X, q.Y, 0))).ToList();
        }

        if (R.InnerFillArea > 1e-3) R.Warn.Add(string.Format(ci, "벽 안쪽 메움 {0:F1}㎡(최대 {1:F2} m) — 바꿀 자리 안 원지반이 벽 면보다 {2}", R.InnerFillArea, R.InnerFillMax, S.Dir > 0 ? "낮음(성토로 메움)" : "높음(절토로 깎음)"));
        if (R.LedgeMax > 1e-3 && inp.InRule == InwardRule.Ledge) R.Warn.Add(string.Format(ci, "턱 {0:F2} m @({1:F1}, {2:F1}) — 첫 소단이 선 위 원지반보다 {3}(턱 규칙 · 엄격 n {4} → {5})", R.LedgeMax, R.LedgeAt.X, R.LedgeAt.Y, S.Dir > 0 ? "높음" : "낮음", R.TiersStrict, R.Tiers));
        double eMax = Math.Max(R.EndMaxA, R.EndMaxB);
        if (double.IsFinite(R.OuterMax) && eMax > R.OuterMax + 1e-3) R.Warn.Add(string.Format(ci, "날개벽 최대 {0:F2} m(A {1:F2} · B {2:F2})가 바깥 벽 최대 {3:F2} m보다 높음", eMax, R.EndMaxA, R.EndMaxB, R.OuterMax));
        if (R.PairsUnder01 > 0) R.Warn.Add($"0.1 mm 안 점 쌍 {R.PairsUnder01}개(Civil이 합친다)");
        if (R.Needles > 0) R.Warn.Add($"바늘 세모 {R.Needles}개(Civil이 뒤집을 수 있다)");
        R.Ok = true; R.Code = "통과";
        R.SelfCheck = SelfLineIn(R);
        log.Append("  " + R.SelfCheck + "\n");
        R.Log = log.ToString();
        return R;
    }

    /// <summary>★[1단계 · 계획 4절] 자가검증 한 줄.</summary>
    static string SelfLineIn(Built b)
    {
        var ci = CultureInfo.InvariantCulture;
        string F(double v, string f) => double.IsFinite(v) ? v.ToString(f, ci) : "—";
        string dir = b.Dir switch { 1 => "절", -1 => "성", 0 => "섞", _ => "같음" };
        return $"[옹벽 추가 안쪽] {dir} n {b.Tiers}({(b.Rule == 1 ? "턱" : "엄격")}) · D {F(b.Depth, "F2")} · 바깥 {F(b.OuterMax, "F2")}/{F(b.OuterLim, "F2")} · 턱 {F(b.LedgeMax, "F2")}"
             + $" · 날개 A {F(b.EndMaxA, "F2")} · B {F(b.EndMaxB, "F2")} · 쌍 {b.PairsUnder01} · 짧은 변 {b.ShortEdges} · 바늘 {b.Needles} · 관문 {(b.Ok ? "통과" : "거절 " + b.Code)}";
    }

    /// <summary>★[v117.0 0단계 · 계획 1-6] 점에서 고리까지 가장 가까운 자리가 두 갈래인가 — 고리를 따라 거리의 국소 최소(변 안 수선 발 · 꼭짓점) 가운데 둘째가 첫째와 1 mm 안.
    /// (한 변을 잘게 나눈 곧은 테두리는 최소가 하나라 걸리지 않는다.)</summary>
    static bool TwoWay(IReadOnlyList<Point3> ring, double x, double y)
    {
        int n = ring.Count; if (n >= 2 && ring[0].X == ring[^1].X && ring[0].Y == ring[^1].Y) n--;
        if (n < 3) return false;
        var t = new double[n]; var d = new double[n];
        for (int j = 0; j < n; j++)
        {
            var a = ring[j]; var b = ring[(j + 1) % n];
            double ex = b.X - a.X, ey = b.Y - a.Y, l2 = ex * ex + ey * ey;
            t[j] = l2 < 1e-18 ? 0 : Math.Clamp(((x - a.X) * ex + (y - a.Y) * ey) / l2, 0, 1);
            double px = a.X + ex * t[j] - x, py = a.Y + ey * t[j] - y;
            d[j] = Math.Sqrt(px * px + py * py);
        }
        var mins = new List<double>();
        for (int j = 0; j < n; j++)
        {
            if (t[j] > 0 && t[j] < 1) mins.Add(d[j]);
            if (t[(j - 1 + n) % n] >= 1 && t[j] <= 0) mins.Add(d[j]);
        }
        if (mins.Count < 2) return false;
        mins.Sort();
        return mins[1] - mins[0] < 1e-3;
    }

    static string SurveyText(Surveyed s)
    {
        var ci = CultureInfo.InvariantCulture;
        string F(double v, string f) => double.IsFinite(v) ? v.ToString(f, ci) : "—";
        return $"[옹벽 안쪽 재기] {s.DirName}(절 {s.NCut} · 성 {s.NFill} · 같음 {s.NEq}) · n {(s.Tiers >= 0 ? s.Tiers.ToString(ci) : "—")} · 선 {F(s.LineLen, "F2")} m · 표본 {s.Samples}(Gap 합침 {s.Merged})"
             + $" · 원지반 {F(s.GMin, "F3")}~{F(s.GMax, "F3")} · 바닥 Z0 {F(s.Z0Min, "F3")}~{F(s.Z0Max, "F3")}"
             + $" · 바깥 벽 {F(s.OuterMin, "F3")}~{F(s.OuterMax, "F3")} m @({F(s.OuterAt.X, "F1")}, {F(s.OuterAt.Y, "F1")}) 한도 {F(s.TierH, "F2")}(넘음 {s.OuterOver} · 구간 {s.OverRuns} · 음수 {s.OuterNeg})"
             + $" · Z1−Z0 {F(s.Z1Min, "F3")}~{F(s.Z1Max, "F3")}(−H1 {F(s.DevMin, "F3")}~{F(s.DevMax, "F3")} · 가장 큰 곳 ({F(s.DevAt.X, "F1")}, {F(s.DevAt.Y, "F1")}) 바닥까지 {F(s.DevNear, "F2")} m)"
             + $" · Z0 끊김 {s.Z0Jump}(최대 {F(s.Z0JumpMax, "F3")} m @({F(s.Z0JumpAt.X, "F1")}, {F(s.Z0JumpAt.Y, "F1")})) · 깊이 {F(s.Depth, "F3")} · vE {F(s.VE, "F3")} · 자리 {F(s.Room, "F2")}/{F(s.Need, "F2")} m"
             + $" · 측선 A {F(s.LenA, "F2")} m θ {F(s.ThetaA, "F1")}° · B {F(s.LenB, "F2")} m θ {F(s.ThetaB, "F1")}° · 날개 A {F(s.WingA, "F2")} · B {F(s.WingB, "F2")} m · 높이 못 읽음 {s.LevelNaN}"
             + $" · 규칙 {(s.Rule == 1 ? "턱" : "엄격")}(엄격 n {(s.TiersStrict >= 0 ? s.TiersStrict.ToString(ci) : "—")}) · 턱 {s.Ledge.ToString("F3", ci)} m({s.LedgeN})"
             + (s.Ok ? " · 통과" : $" · 거절 {s.Code} — {s.Why}" + (s.Codes.Count > 1 ? " (그 밖 " + string.Join(", ", s.Codes.Skip(1)) + ")" : ""));
    }
}
