// ── S162 · S163 ★★★[v103.2 · JACK 1008 «최종 지표면을 기준으로 … 종단 횡단에도 반영» · 1020 «옹벽변환과 사면변환을 거친 지표면이 반영되게»] 옹벽이 든 순수면 ──
//   S162 뚜껑(CompositeResult.CapTris · PureWithWalls.CapOf · Inside) · 고르기(PureWithWalls.Select) · 목록 판 4(CompNonce · Cap · 굳은 옹벽 칸)
//   S163 걷기(WallWalk — 앞면 구배 문턱 · 5cm · 끊김 · 꺾임 · 비스듬한 선)
//   만든 꼴 + 현장 덤프(진단이력/m0 — 저장소 밖 · 공개 저장소라 현장 값은 코드에 안 적는다: 관계만 잰다)
using System.Globalization;
using System.IO.Compression;
using DH.Grading.Core;
using NetTopologySuite.Geometries;
using Tri = DH.Grading.Core.WallDaylight.Tri;

static class PureWallsCheck
{
    static readonly CultureInfo Ci = CultureInfo.InvariantCulture;

    /// <summary>단위 칸 격자 — 칸마다 삼각형 둘(flip이면 다른 대각선).</summary>
    static List<Tri> Grid(int i0, int i1, int j0, int j1, Func<int, int, double> z, Func<int, int, bool>? flip = null, Func<int, int, bool>? skip = null)
    {
        var l = new List<Tri>();
        for (int i = i0; i < i1; i++)
            for (int j = j0; j < j1; j++)
            {
                if (skip != null && skip(i, j)) continue;
                double h = z(i, j);
                Point3 A = new(i, j, h), B = new(i + 1, j, h), C = new(i + 1, j + 1, h), D = new(i, j + 1, h);
                if (flip != null && flip(i, j)) { l.Add(new Tri(A, B, D)); l.Add(new Tri(B, C, D)); }
                else { l.Add(new Tri(A, B, C)); l.Add(new Tri(A, C, D)); }
            }
        return l;
    }

    /// <summary>x만의 함수인 지형(띠 y∈[−w, w]) — 꺾은선 (x, z) 차례 · 빈 칸(구멍)은 그 구간을 뺀다.</summary>
    static List<Tri> Strip(IReadOnlyList<(double X, double Z)> prof, double w, params (double X0, double X1)[] holes)
    {
        var l = new List<Tri>();
        for (int k = 0; k + 1 < prof.Count; k++)
        {
            var (x0, z0) = prof[k]; var (x1, z1) = prof[k + 1];
            if (holes.Any(h => x0 >= h.X0 - 1e-9 && x1 <= h.X1 + 1e-9)) continue;
            Point3 A = new(x0, -w, z0), B = new(x1, -w, z1), C = new(x1, w, z1), D = new(x0, w, z0);
            l.Add(new Tri(A, B, C)); l.Add(new Tri(A, C, D));
        }
        return l;
    }

    static List<(double X, double Y, double S)> Line(double x0, double y0, double deg, double len, double step = 1.0)
    {
        double c = Math.Cos(deg * Math.PI / 180), s = Math.Sin(deg * Math.PI / 180);
        var l = new List<(double, double, double)>();
        int n = (int)Math.Ceiling(len / step);
        for (int k = 0; k <= n; k++) { double t = Math.Min(len, k * step); l.Add((x0 + t * c, y0 + t * s, t)); }
        return l;
    }

    public static void Run(Action<string, bool, string> check0)
    {
        void check(string name, bool ok, string detail = "") => check0(name, ok, detail);

        // ═════ S162 만든 꼴 — 뚜껑 · 고르기 ═════
        Console.WriteLine("\n== S162 옹벽이 든 순수면 — 뚜껑 · 고르기(만든 꼴) ==");
        {
            bool InCap(int i, int j) => i >= 2 && i < 4 && j >= 2 && j < 4;
            bool InPure(int i, int j) => i >= 0 && i < 8 && j >= 0 && j < 10;
            var fin = Grid(-2, 12, -2, 12, (i, j) => InPure(i, j) ? (InCap(i, j) ? 95 : 100) : 90);
            var pure = Grid(0, 8, 0, 10, (i, j) => 100);
            var capTris = fin.Where(t => { double cx = (t.A.X + t.B.X + t.C.X) / 3, cy = (t.A.Y + t.B.Y + t.C.Y) / 3; return cx > 2 && cx < 4 && cy > 2 && cy < 4; }).ToList();
            var cap = PureWithWalls.CapOf(capTris);
            check("S162 뚜껑 — 2×2칸 = 삼각형 8 · 넓이 4 · 테두리 변 8(한 삼각형만 쓰는 변)", cap.Count == 8 && Math.Abs(cap.Area - 4) < 1e-12 && cap.Edges.Count == 8,
                  $"{cap.Count} · {cap.Area} · 변 {cap.Edges.Count}");
            check("S162 뚜껑 안 판정(홀짝) — 안 (3,3) · (2.5,3.5) / 밖 (5,5) · (1.9,3) · (3,4.1)",
                  PureWithWalls.Inside(cap, 3, 3) && PureWithWalls.Inside(cap, 2.5, 3.5) && !PureWithWalls.Inside(cap, 5, 5) && !PureWithWalls.Inside(cap, 1.9, 3) && !PureWithWalls.Inside(cap, 3, 4.1));
            // 구멍 난 뚜껑(가운데 한 칸 빠진 3×3) — 홀짝이 구멍을 «밖»으로
            var ring = Grid(2, 5, 2, 5, (i, j) => 95, skip: (i, j) => i == 3 && j == 3);
            var capR = PureWithWalls.CapOf(ring);
            check("S162 구멍 난 뚜껑 — 구멍 가운데 (3.5,3.5)는 밖 · 고리 (2.5,2.5) · (4.5,3.5)는 안 · 테두리 변 16",
                  !PureWithWalls.Inside(capR, 3.5, 3.5) && PureWithWalls.Inside(capR, 2.5, 2.5) && PureWithWalls.Inside(capR, 4.5, 3.5) && capR.Edges.Count == 16, $"변 {capR.Edges.Count}");

            var poly = new List<Point3> { new(1, 1, 0), new(5, 1, 0), new(5, 5, 0), new(1, 5, 0) };
            var polys = new Dictionary<int, IReadOnlyList<Point3>> { [7] = poly };
            var sel = PureWithWalls.Select(fin, pure, null, new[] { (7, cap) }, polys);
            var finSet = new HashSet<Tri>(fin);
            var ck = sel.Caps[0];
            check("S162 고르기 — 순수면 발자국 안 160면 − 뚜껑 8면 = 남김 152 · 발자국 밖 232 · 뚜껑으로 뺌 8 · 실패 없음",
                  sel.Fail == "" && sel.Kept.Count == 152 && sel.OutsidePure == 232 && sel.CapRemoved == 8 && sel.CapKeptPrev == 0,
                  $"남김 {sel.Kept.Count} · 밖 {sel.OutsidePure} · 뺌 {sel.CapRemoved} · {sel.Fail}");
            check("S162 고르기 — 남긴 삼각형은 최종면 삼각형 그대로(비트) · 뚜껑 자리(표고 95)는 하나도 안 남는다",
                  sel.Kept.All(finSet.Contains) && sel.Kept.All(t => t.A.Z == 100));
            check("S162 뚜껑 대조 — 같은 삼각형이면 해시까지 같음 · 같은 뚜껑 · 걸침 0 · 폴리곤 안에 남김 24(16칸×2 − 8)",
                  ck.BitSame && ck.Match && ck.Straddle == 0 && ck.KeptInPoly == 24 && sel.PureStraddle == 0, $"수 {ck.Count}/{ck.SavedCount} · 걸침 {ck.Straddle} · 폴리곤 안 {ck.KeptInPoly} · 발자국 걸침 {sel.PureStraddle}");

            // 앞 구역 계획면 위의 뚜껑은 남긴다(«이어서» — 결정 ④)
            var prev = Grid(2, 3, 2, 4, (i, j) => 100);
            var selP = PureWithWalls.Select(fin, pure, prev, new[] { (7, cap) }, polys);
            check("S162 앞 구역 계획면 위의 뚜껑 4면은 남긴다 · 나머지 4면만 뺀다(남김 156)",
                  selP.CapKeptPrev == 4 && selP.CapRemoved == 4 && selP.Kept.Count == 156, $"남김 {selP.CapKeptPrev} · 뺌 {selP.CapRemoved} · 계 {selP.Kept.Count}");

            // Civil이 뚜껑 안 대각선을 뒤집었다 — 해시는 다르지만 같은 뚜껑(수 · 넓이 · 걸침 0)
            var finF = Grid(-2, 12, -2, 12, (i, j) => InPure(i, j) ? (InCap(i, j) ? 95 : 100) : 90, flip: InCap);
            var ckF = PureWithWalls.Select(finF, pure, null, new[] { (7, cap) }, polys).Caps[0];
            check("S162 뚜껑 안 대각선 뒤집힘 — 해시 다름(참고) · 같은 뚜껑으로 본다", !ckF.BitSame && ckF.Match, $"해시 같음 {ckF.BitSame} · 같은 뚜껑 {ckF.Match}");

            // 뚜껑 테두리를 가로지르는 삼각형(두 칸을 한 직사각형으로 다시 나눔) — 걸침으로 잡고 «같은 뚜껑 아님»
            var finX = fin.Where(t => { double cy = (t.A.Y + t.B.Y + t.C.Y) / 3, cx = (t.A.X + t.B.X + t.C.X) / 3; return !(cy > 2 && cy < 3 && cx > 3 && cx < 5); }).ToList();
            finX.Add(new Tri(new(3, 2, 95), new(5, 2, 100), new(5, 3, 100)));
            finX.Add(new Tri(new(3, 2, 95), new(5, 3, 100), new(3, 3, 95)));
            var ckX = PureWithWalls.Select(finX, pure, null, new[] { (7, cap) }, polys).Caps[0];
            check("S162 뚜껑 테두리를 가로지르는 삼각형 — 걸침 ≥ 1 · 같은 뚜껑 아님(옹벽면을 안 짓고 ⚠)", ckX.Straddle >= 1 && !ckX.Match, $"걸침 {ckX.Straddle} · 같은 뚜껑 {ckX.Match}");

            // 발자국 걸침 — 폴리곤 밖에서 꼭짓점이 순수면 안팎에 걸친 삼각형(m0 = 0이어야 · 만든 꼴로 하나 넣어 센다)
            var finS = fin.Where(t => { double cy = (t.A.Y + t.B.Y + t.C.Y) / 3, cx = (t.A.X + t.B.X + t.C.X) / 3; return !(cy > 8 && cy < 9 && cx > 7 && cx < 9); }).ToList();
            finS.Add(new Tri(new(7, 8, 100), new(9, 8, 90), new(9, 9, 90)));
            finS.Add(new Tri(new(7, 8, 100), new(9, 9, 90), new(7, 9, 100)));
            var selS = PureWithWalls.Select(finS, pure, null, new[] { (7, cap) }, polys);
            check("S162 순수면 발자국 걸침 — 폴리곤 밖에서 안팎에 걸친 삼각형을 센다(≥ 1 · 자가검증이 ⚠)", selS.PureStraddle >= 1, $"걸침 {selS.PureStraddle} @{selS.PureStraddleAt}");
            check("S162 순수면이 없으면 실패 글(옹벽면을 안 짓는다)", PureWithWalls.Select(fin, new List<Tri>(), null, new[] { (7, cap) }).Fail.Length > 0);
        }

        // ═════ S162 목록 판 4 — 왕복 · 판 고르기 ═════
        {
            var capW = PureWithWalls.CapOf(Grid(0, 2, 0, 1, (i, j) => 50));
            var h = new WallListHead { PlanHandle = "2A3F", GroundHandle = "1B", NextNo = 12, VerifiedP0 = "7C2" };
            h.Frozen.Add(new FrozenWall { No = 3, PlanHandle = "1F0", Up = true, H = 5, SlopeW = 0.05, FaceRun = 0.25, Reflected = true, Poly = new() { new(0, 0, 1), new(4, 0, 1), new(4, 3, 1) } });
            h.Frozen.Add(new FrozenWall { No = 4, PlanHandle = "1F0", Up = false, H = 3.5, SlopeW = -1, FaceRun = -1, Reflected = false, Poly = new() { new(9, 9, 2), new(12, 9, 2), new(12, 13, 2), new(9, 13, 2) } });
            var a = new WallRec { No = 9, State = 1, Up = true, Bench = 0, H = 5, Seg = new() { new(0, 0, 100), new(10, 0, 100) }, Poly = new() { new(0, -5, 100), new(10, -5, 100), new(10, 0, 100), new(0, 0, 100) },
                                  IsWall = new() { true, false, false, true }, CompNonce = "a1b2c3d4e5f6", Cap = capW, ReplaceNo2 = 2 };
            var b = new WallRec { No = 10, State = 0, Up = false, Bench = 1, H = 3, Seg = new() { new(0, 0, 90), new(5, 0, 90) }, Poly = new() { new(0, 0, 90), new(5, 0, 90), new(5, 4, 90) }, IsWall = new() { true, true, true } };
            var e = WallList.Encode(h, new[] { a, b });
            bool d = WallList.TryDecode(e, out var h2, out var w2, out string why);
            var ca = d && w2.Count == 2 ? w2[0].Cap : null;
            bool capSame = ca != null && ca.Count == capW.Count && ca.Area == capW.Area && ca.Hash == capW.Hash && ca.Edges.SequenceEqual(capW.Edges);
            bool frozSame = d && h2.Frozen.Count == 2 && h2.Frozen.Zip(h.Frozen).All(p => p.First.No == p.Second.No && p.First.PlanHandle == p.Second.PlanHandle && p.First.Up == p.Second.Up
                                && p.First.H == p.Second.H && p.First.SlopeW == p.Second.SlopeW && p.First.FaceRun == p.Second.FaceRun && p.First.Reflected == p.Second.Reflected && p.First.Poly.SequenceEqual(p.Second.Poly));
            check("S162 목록 판 4 왕복 — 판 4 · 일회용 번호 · 뚜껑(수 · 넓이 · 해시 · 변) · 뚜껑 없는 칸 · 굳은 옹벽 두 칸 비트 같음 · 판 3 칸도 그대로",
                  d && e[1].Value is int v4 && v4 == 4 && w2[0].CompNonce == a.CompNonce && capSame && w2[1].Cap == null && w2[1].CompNonce == "" && frozSame && w2[0].ReplaceNo2 == 2,
                  why + $" · 판 {e[1].Value}");
            // 판 4 칸이 하나도 없으면 판 2 · 3 그대로(옛 판 애드인도 읽는다) — 뚜껑만 있고 번호가 없으면 판을 안 올린다(번호로 가른다)
            var a2 = new WallRec { No = 9, State = 1, Up = true, Seg = a.Seg, Poly = a.Poly, IsWall = a.IsWall, Cap = capW };
            var e2 = WallList.Encode(new WallListHead { NextNo = 10 }, new[] { a2 });
            var a3 = new WallRec { No = 9, State = 1, Up = true, Seg = a.Seg, Poly = a.Poly, IsWall = a.IsWall, KeepNo = 4 };
            var e3 = WallList.Encode(new WallListHead { NextNo = 10 }, new[] { a3 });
            bool d2 = WallList.TryDecode(e2, out var h22, out var w22, out _);
            check("S162 판 고르기 — 번호 · 굳은 칸이 없으면 판 2(뚜껑만 있어도) · 이어 짓기 칸이면 판 3 · 읽으면 뚜껑 없음 · 굳은 칸 0",
                  e2[1].Value is int v2 && v2 == 2 && e3[1].Value is int v3 && v3 == 3 && d2 && w22[0].Cap == null && w22[0].CompNonce == "" && h22.Frozen.Count == 0,
                  $"판 {e2[1].Value} · {e3[1].Value}");
            // 굳은 칸만 있어도 판 4(목록이 빈 «이어서» 직후)
            var hf = new WallListHead { NextNo = 12 }; hf.Frozen.Add(h.Frozen[0]);
            var ef = WallList.Encode(hf, new List<WallRec>());
            bool df = WallList.TryDecode(ef, out var hf2, out var wf2, out string whyF);
            check("S162 «이어서» 직후(빈 목록 + 굳은 칸) — 판 4로 쓰고 굳은 칸이 그대로 읽힌다", ef[1].Value is int vf && vf == 4 && df && wf2.Count == 0 && hf2.Frozen.Count == 1 && hf2.Frozen[0].Reflected, whyF);
        }

        // ═════ S163 걷기 — 만든 꼴 ═════
        Console.WriteLine("\n== S163 걷기(WallWalk) — 앞면 구배 문턱 · 5cm · 끊김 · 꺾임 · 비스듬한 선 ==");
        {
            // x만의 지형: 평지 0 → 1:0.05 벽 5m → 평지 → 1:0.05 벽 5m → 평지 → 3cm 턱(급함) → 평지 → 구멍 25~30 → 1:2 오르막 → 평지(꺾임) → 1:0.3 사면(벽 아님) → 평지
            var prof = new List<(double X, double Z)>
            {
                (0, 0), (10, 0), (10.25, 5), (15, 5), (15.25, 10), (20, 10), (20.001, 10.03), (25, 10.03), (30, 10.03), (40, 15.03), (50, 15.03), (53, 25.03), (60, 25.03),
            };
            var tris = Strip(prof, 300, (25, 30));
            foreach (double deg in new[] { 0.0, 30.0, 60.0 })
            {
                double c = Math.Cos(deg * Math.PI / 180), len = 60 / c;
                var al = Line(0, 0, deg, len);
                var pcs = WallWalk.Profile(al, 0, len, tris);
                var r = WallWalk.Walk(pcs, 0, len, 0.05);
                string tag = deg == 0 ? "직각" : $"{deg:0}°";
                double X(double s) => s * c;
                bool walls = r.Walls.Count == 2 && Math.Abs(X(r.Walls[0].Front) - 10) < 1e-6 && Math.Abs(X(r.Walls[0].Back) - 10.25) < 1e-6 && Math.Abs(X(r.Walls[1].Front) - 15) < 1e-6
                             && r.Walls.All(w => Math.Abs(w.ZTop - w.ZBottom - 5) < 1e-6);
                check($"S163 [{tag}] 벽 2단(앞 · 뒤 · 높이 5m) · 3cm 턱은 안 셈 1 · 1:0.3 사면은 벽 아님", walls && r.SmallSkipped == 1,
                      $"벽 {string.Join(" ", r.Walls.Select(w => $"{X(w.Front):F3}~{X(w.Back):F3}/{w.ZTop - w.ZBottom:F3}"))} · 안 셈 {r.SmallSkipped}");
                check($"S163 [{tag}] 끊김 = 구멍 앞뒤(25 · 30) 둘 · 구간 테두리는 안 셈", r.Breaks.Count == 2 && Math.Abs(X(r.Breaks[0]) - 25) < 1e-6 && Math.Abs(X(r.Breaks[1]) - 30) < 1e-6,
                      string.Join(" ", r.Breaks.Select(b => X(b).ToString("F3"))));
                var bx = r.Bends.Select(b => X(b.S)).ToList();
                check($"S163 [{tag}] 꺾임 = 오르막 끝 40 · 사면 시작 50 · 끝 53 셋뿐(평지 · 벽 둘레에는 없다)",
                      bx.Count == 3 && Math.Abs(bx[0] - 40) < 1e-6 && Math.Abs(bx[1] - 50) < 1e-6 && Math.Abs(bx[2] - 53) < 1e-6, string.Join(" ", bx.Select(x => x.ToString("F3"))));
            }
            // 앞면 구배 문턱 — 1:0.05 벽은 구배가 1:0.05로 적힌 옹벽에서 잡히고(2배 여유) · 앞면을 1:0.3으로 지은 옹벽이면 1:0.3 사면도 벽
            var al0 = Line(0, 0, 0, 60);
            var p0 = WallWalk.Profile(al0, 0, 60, Strip(prof, 300, (25, 30)));
            check("S163 앞면 구배 문턱 — 1:0.3으로 지은 옹벽이면 그 사면(53)도 벽(3단) · 1:0.05 벽 둘도 그대로", WallWalk.Walk(p0, 0, 60, 0.3).Walls.Count == 3,
                  $"벽 {WallWalk.Walk(p0, 0, 60, 0.3).Walls.Count}");
            // 높이 문턱 — 5cm는 «묶은 뒤» 높이(두 토막으로 나뉜 벽도 한 벽)
            var tw = Strip(new List<(double, double)> { (0, 0), (5, 0), (5.002, 0.03), (5.004, 0.06), (10, 0.06) }, 10);
            var rw = WallWalk.Walk(WallWalk.Profile(Line(0, 0, 0, 10), 0, 10, tw), 0, 10, 0.05);
            check("S163 높이 문턱은 묶은 뒤 — 3cm + 3cm로 이어진 앞면은 6cm 벽 하나", rw.Walls.Count == 1 && rw.SmallSkipped == 0 && Math.Abs(rw.Walls[0].ZTop - rw.Walls[0].ZBottom - 0.06) < 1e-9,
                  $"벽 {rw.Walls.Count} · 안 셈 {rw.SmallSkipped}");
            check("S163 삼각형이 없으면 토막 0 · 벽 0 · 끊김 0", WallWalk.Walk(WallWalk.Profile(al0, 0, 60, new List<Tri>()), 0, 60, 0.05) is var r0 && r0.Walls.Count == 0 && r0.Breaks.Count == 0);
        }

        // ═════ 현장 덤프(진단이력/m0) ═════
        string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "진단이력", "m0"));
        var dirs = Directory.Exists(root) ? Directory.GetDirectories(root).Where(x => File.Exists(Path.Combine(x, "옹벽목록.txt"))).OrderBy(x => x).ToList() : new List<string>();
        check("S162 현장 덤프가 있다(개발 PC · 저장소 밖 — 없으면 아래 현장 시험을 못 돈다)", dirs.Count > 0, root);
        foreach (var dir in dirs)
            try { Field(dir, dir == dirs[^1], check); }
            catch (Exception ex) { check($"S162 현장 {Path.GetFileName(dir)} — 예외 없이 돈다", false, ex.ToString()); }
    }

    static List<Tri> ReadTris(string dir, string name)
    {
        var res = new List<Tri>();
        var p = Path.Combine(dir, name + ".tri.txt.gz");
        if (!File.Exists(p)) return res;
        using var fs = File.OpenRead(p);
        using var gz = new GZipStream(fs, CompressionMode.Decompress);
        using var sr = new StreamReader(gz);
        string? line;
        while ((line = sr.ReadLine()) != null)
        {
            var f = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (f.Length < 9) continue;
            var v = f.Take(9).Select(s => double.Parse(s, Ci)).ToArray();
            res.Add(new Tri(new Point3(v[0], v[1], v[2]), new Point3(v[3], v[4], v[5]), new Point3(v[6], v[7], v[8])));
        }
        return res;
    }

    /// <summary>삼각형 면의 (x, y) 표고 — 상자 나무로 찾는다(없으면 null).</summary>
    sealed class STRtreeZ
    {
        readonly NetTopologySuite.Index.Strtree.STRtree<Tri> _t = new();
        public STRtreeZ(List<Tri> l)
        {
            foreach (var t in l)
                _t.Insert(new Envelope(Math.Min(t.A.X, Math.Min(t.B.X, t.C.X)), Math.Max(t.A.X, Math.Max(t.B.X, t.C.X)), Math.Min(t.A.Y, Math.Min(t.B.Y, t.C.Y)), Math.Max(t.A.Y, Math.Max(t.B.Y, t.C.Y))), t);
            _t.Build();
        }
        public double? At(double x, double y)
        {
            foreach (var t in _t.Query(new Envelope(x, x, y, y)))
            {
                double dd = (t.B.Y - t.C.Y) * (t.A.X - t.C.X) + (t.C.X - t.B.X) * (t.A.Y - t.C.Y);
                if (Math.Abs(dd) < 1e-18) continue;
                double l1 = ((t.B.Y - t.C.Y) * (x - t.C.X) + (t.C.X - t.B.X) * (y - t.C.Y)) / dd, l2 = ((t.C.Y - t.A.Y) * (x - t.C.X) + (t.A.X - t.C.X) * (y - t.C.Y)) / dd, l3 = 1 - l1 - l2;
                if (l1 >= -1e-9 && l2 >= -1e-9 && l3 >= -1e-9) return l1 * t.A.Z + l2 * t.B.Z + l3 * t.C.Z;
            }
            return null;
        }
    }

    static List<Point3> Pts(string s) => s.Split(' ', StringSplitOptions.RemoveEmptyEntries)
        .Select(q => { var a = q.Split(','); return new Point3(double.Parse(a[0], Ci), double.Parse(a[1], Ci), double.Parse(a[2], Ci)); }).ToList();

    static void Field(string dir, bool last, Action<string, bool, string> check0)
    {
        void check(string name, bool ok, string detail = "") => check0(name, ok, detail);
        string nm = Path.GetFileName(dir);
        string tag = "현장 " + (nm.Length > 15 ? nm.Substring(0, 15) : nm);
        var pure = ReadTris(dir, "정지순수_DH"); var pre = ReadTris(dir, "정지면_DH합성전"); var fin = ReadTris(dir, "정지면_DH");
        var wall = ReadTris(dir, "가상옹벽1_DH"); var ground = ReadTris(dir, "원지반_둘레30m");
        var lines = File.ReadAllLines(Path.Combine(dir, "옹벽목록.txt"));
        var wl = lines.First(l => l.StartsWith("WALL "));
        bool down = wl.Contains("성토");
        int no = int.Parse(wl.Split(' ')[1], Ci);
        var poly = Pts(lines.First(l => l.StartsWith("POLY ")).Substring(5));
        if (poly.Count > 1 && poly[0].X == poly[^1].X && poly[0].Y == poly[^1].Y) poly.RemoveAt(poly.Count - 1);
        var seg = Pts(lines.First(l => l.StartsWith("SEG ")).Substring(4));
        Console.WriteLine($"\n  [{tag}] 옹벽 {no} {(down ? "성토" : "절토")} · 합성 전 {pre.Count} · 최종 {fin.Count} · 순수 {pure.Count}");

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var res = WallDaylight.ComposeWalls(pre, new[] { new WallDaylight.WallInput { No = no, Poly = poly, WallTris = wall, GroundTris = ground, Down = down } });
        long msComp = sw.ElapsedMilliseconds;
        var st = res.Steps.Count > 0 ? res.Steps[0].R : null;
        check($"S162 [{tag}] 합성 → 뚜껑 삼각형이 있다", res.All != null && st?.CapTris != null && st.CapTris.Count > 0, $"판정 {res.Tier} · {res.Fail} · {msComp}ms · 뚜껑 {st?.CapTris?.Count}");
        if (res.All == null || st?.CapTris == null || st.CapTris.Count == 0) return;
        var capTris = st.CapTris;
        var cap = PureWithWalls.CapOf(capTris);

        // ① 뚜껑 ⊂ 폴리곤(1cm)
        var gf = new GeometryFactory();
        var pg = gf.CreatePolygon(poly.Select(q => new Coordinate(q.X, q.Y)).Append(new Coordinate(poly[0].X, poly[0].Y)).ToArray());
        if (!pg.IsValid) pg = (Polygon)pg.Buffer(0);
        var locPg = new NetTopologySuite.Algorithm.Locate.IndexedPointInAreaLocator(pg.Buffer(0.01));
        int outPoly = capTris.Count(t => locPg.Locate(new Coordinate((t.A.X + t.B.X + t.C.X) / 3, (t.A.Y + t.B.Y + t.C.Y) / 3)) == Location.Exterior);
        check($"S162 [{tag}] ① 뚜껑 삼각형은 모두 옹벽 폴리곤 안(1cm)", outPoly == 0, $"밖 {outPoly}/{capTris.Count}");

        // ② 뚜껑 = 합성 규칙이 기준 지반(G)을 고른 자리 — 무게중심에서 세 면을 따로 읽어 P ≤ G ≤ W(절토 · 성토는 거울) · 뚜껑 표고와 G 차는 합성의 높이 한도(1cm) 안
        //    (합성은 Civil이 합칠 점 쌍을 정리하며 높이를 1cm 한도 안에서 옮긴다 — 1mm 넘은 수는 참고로 적는다)
        STRtreeZ zG = new(ground), zP = new(pre), zW = new(wall);
        int noG = 0, farG = 0, over1 = 0, rule = 0; double maxG = 0;
        foreach (var t in capTris)
        {
            double cx = (t.A.X + t.B.X + t.C.X) / 3, cy = (t.A.Y + t.B.Y + t.C.Y) / 3, cz = (t.A.Z + t.B.Z + t.C.Z) / 3;
            var zg = zG.At(cx, cy);
            if (zg == null) { noG++; continue; }
            double dz = Math.Abs(zg.Value - cz); maxG = Math.Max(maxG, dz);
            if (dz > 1e-2) farG++;
            if (dz > 1e-3) over1++;
            var zp = zP.At(cx, cy); var zw = zW.At(cx, cy);
            double s = down ? -1 : 1;   // 성토는 거울: min(P, max(W, G))
            bool okP = zp == null || s * zg.Value >= s * zp.Value - 1e-2;
            bool okW = zw == null || s * zg.Value <= s * zw.Value + 1e-2;
            if (!okP || !okW) rule++;
        }
        check($"S162 [{tag}] ② 뚜껑 = 규칙이 원지반을 고른 자리(P ≤ G ≤ W · 1cm) · 뚜껑 표고와 원지반 차 1cm 이하", farG == 0 && noG == 0 && rule == 0,
              $"1cm 넘음 {farG} · 규칙 어긋남 {rule} · 원지반 없음 {noG} · 1mm 넘음 {over1}(참고) · 최대 {maxG * 1000:F3}mm");

        // ③ 홀짝 판정 — 뚜껑 삼각형은 모두 안 · 구역의 나머지는 모두 밖
        int capOut = capTris.Count(t => !PureWithWalls.Inside(cap, (t.A.X + t.B.X + t.C.X) / 3, (t.A.Y + t.B.Y + t.C.Y) / 3));
        var capSet = new HashSet<Tri>(capTris);
        int zoneIn = (st.Zone ?? new List<Tri>()).Where(t => !capSet.Contains(t)).Count(t => PureWithWalls.Inside(cap, (t.A.X + t.B.X + t.C.X) / 3, (t.A.Y + t.B.Y + t.C.Y) / 3));
        check($"S162 [{tag}] ③ 테두리 변 홀짝 — 뚜껑 {capTris.Count}면 모두 «안» · 구역의 나머지 {(st.Zone?.Count ?? 0) - capTris.Count}면 모두 «밖»", capOut == 0 && zoneIn == 0 && st.Zone != null,
              $"뚜껑 밖 {capOut} · 나머지 안 {zoneIn}");

        // ④ Civil이 가진 최종면에서 고르기 — 같은 뚜껑 · 걸침 0 · 남긴 넓이 = 순수면 − 뚜껑
        var sel = PureWithWalls.Select(fin, pure, null, new[] { (no, cap) }, new Dictionary<int, IReadOnlyList<Point3>> { [no] = poly });
        var ck = sel.Caps[0];
        double keptA = sel.Kept.Sum(PureWithWalls.TriArea), pureA = pure.Sum(PureWithWalls.TriArea), diff = keptA - (pureA - cap.Area);
        check($"S162 [{tag}] ④ Civil 최종면 고르기 — 같은 뚜껑(수 · 넓이) · 뚜껑 걸침 0 · 발자국 걸침 0 · 남긴 넓이 = 순수면 − 뚜껑(10cm²)",
              sel.Fail == "" && ck.Match && ck.Straddle == 0 && sel.PureStraddle == 0 && Math.Abs(diff) <= 1e-3,
              $"뚜껑 {ck.Count}/{ck.SavedCount} · 해시 {(ck.BitSame ? "같음" : "다름(Civil이 꼭짓점을 건드림 — 참고)")} · 걸침 {ck.Straddle} · 발자국 걸침 {sel.PureStraddle} · 넓이 차 {diff * 1e6:F1}mm² · {sel.MsFoot + sel.MsSelect}ms");
        var finSet = new HashSet<Tri>(fin);
        check($"S162 [{tag}] ④ 남긴 삼각형은 Civil 최종면 그대로(비트)", sel.Kept.All(finSet.Contains));

        // ⑤ 성토 거울 — 표고를 뒤집어 성토로 돌려도 같은 뚜껑(수 · 넓이 · 테두리 변)
        if (last)
        {
            static List<Tri> Neg(List<Tri> l) => l.Select(t => new Tri(new Point3(t.A.X, t.A.Y, -t.A.Z), new Point3(t.B.X, t.B.Y, -t.B.Z), new Point3(t.C.X, t.C.Y, -t.C.Z))).ToList();
            var resM = WallDaylight.ComposeWalls(Neg(pre), new[] { new WallDaylight.WallInput { No = no, Poly = poly, WallTris = Neg(wall), GroundTris = Neg(ground), Down = !down } });
            var capM = PureWithWalls.CapOf(resM.Steps.Count > 0 ? resM.Steps[0].R.CapTris : null);
            static HashSet<(long, long, long)> Vs(IEnumerable<Tri> l, double sz) => new(l.SelectMany(t => new[] { t.A, t.B, t.C }).Select(q => ((long)Math.Round(q.X * 1e6), (long)Math.Round(q.Y * 1e6), (long)Math.Round(sz * q.Z * 1e6))));
            static HashSet<(double, double, double, double)> Es(PureWithWalls.Cap c) => new(c.Edges.Select(e => e.X1 < e.X2 || (e.X1 == e.X2 && e.Y1 <= e.Y2) ? (e.X1, e.Y1, e.X2, e.Y2) : (e.X2, e.Y2, e.X1, e.Y1)));
            check($"S162 [{tag}] ⑤ {(down ? "절토" : "성토")} 거울 — 같은 뚜껑(수 · 넓이 · 테두리 변) · 뚜껑 표고는 뒤집힌 값",
                  capM.Count == cap.Count && Math.Abs(capM.Area - cap.Area) <= 1e-9 * Math.Max(1, cap.Area) && Es(capM).SetEquals(Es(cap)) && Vs(capTris, 1).SetEquals(Vs(resM.Steps[0].R.CapTris!, -1)),
                  $"수 {capM.Count}/{cap.Count} · 넓이 {capM.Area:F4}/{cap.Area:F4}");
        }

        // ═════ S163 걷기 — 옹벽면 위 선형 셋(직각 · 30° · 벽과 10°) + 옹벽 없는 순수면 ═════
        var face = sel.Kept;
        var mid = seg[seg.Count / 2]; var a0 = seg[Math.Max(0, seg.Count / 2 - 1)]; var a1 = seg[Math.Min(seg.Count - 1, seg.Count / 2 + 1)];
        double dx = a1.X - a0.X, dy = a1.Y - a0.Y, ln = Math.Sqrt(dx * dx + dy * dy); dx /= ln; dy /= ln;
        double pcx = poly.Average(q => q.X), pcy = poly.Average(q => q.Y);
        double nx = -dy, ny = dx; if ((pcx - mid.X) * nx + (pcy - mid.Y) * ny < 0) { nx = -nx; ny = -ny; }
        var counts = new List<int>();
        foreach (double deg in new[] { 0.0, 30.0, 80.0 })
        {
            double rad = deg * Math.PI / 180, cx = nx * Math.Cos(rad) - ny * Math.Sin(rad), cy = nx * Math.Sin(rad) + ny * Math.Cos(rad);
            var al = new List<(double X, double Y, double S)>();
            for (int k = 0; k <= 360; k++) { double s = k * 0.25; al.Add((mid.X - 30 * cx + s * cx, mid.Y - 30 * cy + s * cy, s)); }
            var r = WallWalk.Walk(WallWalk.Profile(al, 0, 90, face), 0, 90, 0.05);
            counts.Add(r.Walls.Count);
            string t2 = deg == 0 ? "직각" : deg == 80 ? "벽과 10°" : "30°";
            Console.WriteLine($"    걷기 [{t2}] 벽 {r.Walls.Count}(높이 {string.Join("/", r.Walls.Select(w => (w.ZTop - w.ZBottom).ToString("F2")))}) · 5cm 미만 {r.SmallSkipped} · 끊김 {r.Breaks.Count} · 꺾임 {r.Bends.Count} · 덮음 {r.Covered:F1}m");
            check($"S163 [{tag}] [{t2}] 벽이 선다 · 높이 5cm 이상 · 끊김 2개 이하", r.Walls.Count >= 1 && r.Walls.All(w => w.ZTop - w.ZBottom >= 0.05) && r.Breaks.Count <= 2,
                  $"벽 {r.Walls.Count} · 끊김 {r.Breaks.Count}");
            var rp = WallWalk.Walk(WallWalk.Profile(al, 0, 90, pure), 0, 90, 0.05);
            check($"S163 [{tag}] [{t2}] 옹벽 없는 순수면(사면)에서는 벽 0", rp.Walls.Count == 0, $"벽 {rp.Walls.Count}");
        }
        // 벽과 10°로 지나는 선은 옹벽 끝(측선 쪽)을 넘어 옆면 벽도 지난다(계획 9-5 — 최종면에 실제로 그 수직면이 있다) → 직각보다 적지만 않으면 된다
        check($"S163 [{tag}] 비스듬해도 같은 단 수(직각 = 30°) · 벽과 10°는 측선 쪽 옆면까지 셀 수 있어 그 이상", counts[0] == counts[1] && counts[2] >= counts[0], string.Join("/", counts));
    }
}
