// ── S159 ★★[v103.1.13 · 1016 13:42 현장 «⚠옹벽 1(성토)를 정지면_DH에 못 넣었다 — Civil이 넘긴 합성을 그대로 안 받았다»] 넘길 면 전체의 «Civil이 고칠 면» · 먼저 뒤집기 ──
//   그날 정지면_DH(전체구간 절토 옹벽 1:0.01 앞면 위 끝)의 넓이 0 면 하나를 Civil이 가져오며 뒤집어 그 자리 높이가 6.125mm 바뀌었고, 옹벽 쪽 5.929mm와 더해
//   한도 10mm를 넘어 옹벽을 버렸다(약 90m 떨어진 자리). 다듬기는 옹벽 구역 안만 본다 → CivilSafeMesh.FixOutside가 넘길 면 전체에서 같은 판정
//   (높이 < 1e-4 · 긴 변 안쪽 · 긴 변 들로네 아님)으로 찾아 정지면 그대로인 면끼리면 Civil과 같은 뒤집기를 먼저 한다.
//   화면 없는 Civil 실측(1016): 그 합성 그대로 = 같은 두 면이 바뀜 · 먼저 뒤집은 판 = 292,027/292,027 그대로 · 둘레 794면 판(data/현장1016_1342_넓이0.txt)에서
//   꼭짓점을 긴 변에서 10nm 비키면 뒤집음 · 100nm면 그대로 · 네 면 판 36가지(외접원 안) 전부 뒤집음.
using System.Globalization;
using DH.Grading.Core;
using Tri = DH.Grading.Core.WallDaylight.Tri;

static class OutsideFixCheck
{
    public static void Run(Action<string, bool, string> check0, string dataDir)
    {
        void check(string name, bool ok, string detail = "") => check0(name, ok, detail);
        Console.WriteLine("\n== S159 넘길 면 전체의 «Civil이 고칠 면» — 정지면 그대로인 면끼리 먼저 뒤집기 ==");
        var ic = CultureInfo.InvariantCulture;
        static double Cr(Tri t) => (t.B.X - t.A.X) * (t.C.Y - t.A.Y) - (t.B.Y - t.A.Y) * (t.C.X - t.A.X);
        static string K6(Tri t)
        {
            var v = new[] { t.A, t.B, t.C }.Select(q => string.Format(CultureInfo.InvariantCulture, "({0:F6},{1:F6})", q.X, q.Y)).OrderBy(s => s, StringComparer.Ordinal);
            return string.Join(" ", v);
        }
        static int Changed(IReadOnlyList<Tri> a, IReadOnlyList<Tri> b) => a.Count != b.Count ? -1 : Enumerable.Range(0, a.Count).Count(i => !a[i].Equals(b[i]));

        // ① 네 면 판 — T2 = (A, X, B) · T1 = (A, B, O) 바늘(O는 AB에서 h) · T3 = (A, O, Y) · T4 = (O, B, Y) · 현장 크기 좌표
        const double X0 = 210261.0, Y0 = 509879.0;
        List<Tri> Quad(double L, double h, double xDepth, double zO, double dx = 0)
        {
            Point3 A = new(X0 + dx, Y0, 120.0), B = new(X0 + dx + L, Y0, 119.99), X = new(X0 + dx + L / 2, Y0 - xDepth, 115.0);
            Point3 O = new(X0 + dx + 0.3 * L, Y0 + h, zO), Yp = new(X0 + dx + 0.3 * L, Y0 + L, 125.0);
            return new List<Tri> { new(A, X, B), new(A, B, O), new(A, O, Yp), new(O, B, Yp) };
        }
        // O 자리 긴 변 높이 = 120 + 0.3 × (119.99 − 120) = 119.997 → 세운 턱 = 119.997 − zO
        var q1 = Quad(1.0, 5e-9, 1.0, 119.995);
        var f1 = CivilSafeMesh.FixOutside(q1, _ => true, out var r1);
        check("S159 ① 넓이 0 가까운 바늘(5nm · 외접원 깊이 안) — 찾음 1 · 뒤집음 1 · 남음 0", r1.Found == 1 && r1.Flipped == 1 && r1.Left == 0, r1.Text);
        check("S159 ① 뒤집은 두 면 = (B, O, X) · (O, A, X) — 긴 변 AB를 지우고 O–X를 잇는다 · 제자리(차례 그대로) · 같은 방향으로 감김",
            f1[0].Equals(new Tri(q1[0].C, q1[1].C, q1[0].B)) && f1[1].Equals(new Tri(q1[1].C, q1[0].A, q1[0].B)) && f1[2].Equals(q1[2]) && f1[3].Equals(q1[3])
            && f1.All(t => Cr(t) > 0), string.Join(" / ", f1.Take(2).Select(K6)));
        check("S159 ① 높이 변화 = O 자리의 세운 턱 2.000mm(대각선이 만나는 자리)", Math.Abs(r1.MaxDz - 2e-3) < 1e-7, $"{r1.MaxDz * 1000:F6}mm @{r1.At}");
        check("S159 ① 넓이 그대로(뒤집기는 사각형 안에서만)", Math.Abs(f1.Sum(t => Math.Abs(Cr(t))) - q1.Sum(t => Math.Abs(Cr(t)))) < 1e-9);
        check("S159 ① 받은 목록은 안 바꾼다(바꿀 때 복사)", !ReferenceEquals(f1, q1) && q1[1].C.Y == Y0 + 5e-9 && Changed(q1, Quad(1.0, 5e-9, 1.0, 119.995)) == 0);
        var f1b = CivilSafeMesh.FixOutside(f1, _ => true, out var r1b);
        check("S159 ① 한 번 더 — 고칠 면 0 · 같은 목록을 돌려준다", r1b.Found == 0 && ReferenceEquals(f1b, f1), r1b.Text);

        // ② 납작하지 않은 면(1mm) — 판정 밖 · 받은 목록 그대로
        var q2 = Quad(1.0, 1e-3, 1.0, 119.995);
        var f2 = CivilSafeMesh.FixOutside(q2, _ => true, out var r2);
        check("S159 ② 높이 1mm 면은 Civil이 고칠 면이 아니다 — 찾음 0 · 같은 목록", r2.Found == 0 && ReferenceEquals(f2, q2), r2.Text);

        // ③ 들로네 가르기 — 긴 변 1cm · 맞은편 1m 아래: O 자리 외접원 위 끝 ≈ 21µm → 30µm는 밖(들로네 · 둠) · 10µm는 안(뒤집음). 둘 다 높이 < 1e-4
        var f3a = CivilSafeMesh.FixOutside(Quad(0.01, 30e-6, 1.0, 119.995), _ => true, out var r3a);
        var f3b = CivilSafeMesh.FixOutside(Quad(0.01, 10e-6, 1.0, 119.995), _ => true, out var r3b);
        check("S159 ③ 바늘 30µm · 긴 변 들로네 = 둔다 / 10µm · 들로네 아님 = 뒤집는다(높이만으로 안 가른다)", r3a.Found == 0 && r3b.Found == 1 && r3b.Flipped == 1, $"30µm {r3a.Found} · 10µm {r3b.Found}/{r3b.Flipped}");

        // ④ 옹벽 구역 면이 끼면 안 건드린다 — 세고 남긴다(되읽기 관문이 지킨다)
        var q4 = Quad(1.0, 5e-9, 1.0, 119.995);
        var f4 = CivilSafeMesh.FixOutside(q4, t => !t.Equals(q4[0]), out var r4);
        check("S159 ④ 긴 변 건너 면이 옹벽 구역 면 — 찾음 1 · 뒤집음 0 · 남음 1(옹벽 구역 면 낌) · 같은 목록", r4.Found == 1 && r4.Flipped == 0 && r4.Left == 1 && r4.LeftZone == 1 && ReferenceEquals(f4, q4) && r4.Text.Contains("⚠남은"), r4.Text);

        // ⑤ 긴 변이 테두리(건너 면 없음) — Civil이 고칠 면이 아니다
        var q5 = Quad(1.0, 5e-9, 1.0, 119.995).Skip(1).ToList();
        var f5 = CivilSafeMesh.FixOutside(q5, _ => true, out var r5);
        check("S159 ⑤ 긴 변이 테두리인 바늘 — 찾음 0", r5.Found == 0 && ReferenceEquals(f5, q5), r5.Text);

        // ⑥ 꼭짓점이 긴 변 위에 정확히(넓이 0) — 쓰기(WriteLandXmlTin)가 버릴 면 · 외접원 판정 없이 뒤집는다 · 새 두 면은 넓이가 있다
        var q6 = Quad(1.0, 0.0, 1.0, 119.995);
        var f6 = CivilSafeMesh.FixOutside(q6, _ => true, out var r6);
        check("S159 ⑥ 넓이 정확히 0 — 찾음 1 · 뒤집음 1 · 새 면 넓이 > 0 · 쓰기가 버리는 면 0",
            Cr(q6[1]) == 0 && r6.Found == 1 && r6.Flipped == 1 && f6.All(t => Math.Abs(Cr(t)) >= 1e-14), $"{r6.Text}");
        string tmp = Path.Combine(Path.GetTempPath(), "dh_s159_" + Guid.NewGuid().ToString("N") + ".xml");
        try
        {
            WallDaylight.WriteLandXmlTin(tmp, "S159", q6, 1e-7, out _, out int nf0, out int nd0);
            WallDaylight.WriteLandXmlTin(tmp, "S159", f6, 1e-7, out _, out int nf1, out int nd1);
            check("S159 ⑥ 쓰기 — 뒤집기 전엔 넓이 0 면 1개를 버리고(합성이 멈추던 길) 뒤집은 뒤엔 0", nd0 == 1 && nd1 == 0 && nf1 == 4, $"전 {nf0}면 버림 {nd0} · 뒤 {nf1}면 버림 {nd1}");
        }
        finally { try { File.Delete(tmp); } catch { } }

        // ⑦ 현장 1016 둘레 794면 — 출하 판정이 Civil이 실제로 뒤집은 그 두 면을 만드나 · 10nm/100nm 가르기(실측과 같은가)
        string fz = Path.Combine(dataDir, "현장1016_1342_넓이0.txt");
        var site = new List<Tri>();
        foreach (var ln in File.ReadLines(fz))
        {
            if (ln.StartsWith("#") || ln.Trim().Length == 0) continue;
            var v = ln.Split(' ').Select(s => double.Parse(s, ic)).ToArray();
            site.Add(new Tri(new(v[0], v[1], v[2]), new(v[3], v[4], v[5]), new(v[6], v[7], v[8])));
        }
        check("S159 ⑦ 현장 판을 읽었다 — 794면", site.Count == 794, $"{site.Count}");
        var f7 = CivilSafeMesh.FixOutside(site, _ => true, out var r7);
        var civil = new HashSet<string>
        {
            "(210261.365247,509879.973183) (210261.378000,509879.973891) (210274.613333,509881.497000)",
            "(210261.378000,509879.973891) (210261.379034,509879.973948) (210274.613333,509881.497000)",
        };
        var made = new HashSet<string>(Enumerable.Range(0, site.Count).Where(i => !f7[i].Equals(site[i])).Select(i => K6(f7[i])));
        check("S159 ⑦ 현장 판 — 찾음 1 · 뒤집음 1 · 높이 변화 6.125mm @(210261.3780,509879.9739)",
            r7.Found == 1 && r7.Flipped == 1 && r7.Left == 0 && Math.Abs(r7.MaxDz - 6.1252e-3) < 1e-6 && r7.At == "(210261.3780,509879.9739)", r7.Text);
        check("S159 ⑦ ★뒤집은 두 면 = 화면 없는 Civil이 이 판을 받아 만든 두 면(실측)", made.SetEquals(civil), string.Join(" / ", made));
        // 그 꼭짓점을 긴 변에서 비켜 보기 — 실측: 10nm 비킴 = Civil이 뒤집음 · 100nm = 그대로
        int flatI = Enumerable.Range(0, site.Count).First(i => Math.Abs(Cr(site[i])) / new[] { (site[i].A, site[i].B), (site[i].B, site[i].C), (site[i].C, site[i].A) }
            .Max(e => Math.Sqrt((e.Item1.X - e.Item2.X) * (e.Item1.X - e.Item2.X) + (e.Item1.Y - e.Item2.Y) * (e.Item1.Y - e.Item2.Y))) < 1e-9);
        var ft = site[flatI];
        var es = new[] { (ft.A, ft.B, ft.C), (ft.B, ft.C, ft.A), (ft.C, ft.A, ft.B) };
        var (u, w, o) = es.OrderByDescending(e => (e.Item1.X - e.Item2.X) * (e.Item1.X - e.Item2.X) + (e.Item1.Y - e.Item2.Y) * (e.Item1.Y - e.Item2.Y)).First();
        double lx = w.X - u.X, ly = w.Y - u.Y, ll = Math.Sqrt(lx * lx + ly * ly);
        double side = Math.Sign((w.X - u.X) * (o.Y - u.Y) - (w.Y - u.Y) * (o.X - u.X));
        double nx = -ly / ll * side, ny = lx / ll * side;           // O가 있는 쪽으로(넓이 0 면이 커지는 쪽 — 건너 면과 겹치지 않게)
        List<Tri> Moved(double h)
        {
            var o2 = new Point3(o.X + nx * h, o.Y + ny * h, o.Z);
            Point3 M(Point3 q) => q.X == o.X && q.Y == o.Y ? o2 : q;
            return site.Select(t => new Tri(M(t.A), M(t.B), M(t.C))).ToList();
        }
        CivilSafeMesh.FixOutside(Moved(1e-8), _ => true, out var r7a);
        CivilSafeMesh.FixOutside(Moved(1e-7), _ => true, out var r7b);
        check("S159 ⑦ 비킨 판 — 10nm = 뒤집음 · 100nm = 둠(화면 없는 Civil 실측과 같다)", side != 0 && r7a.Found == 1 && r7a.Flipped == 1 && r7b.Found == 0, $"10nm {r7a.Found}/{r7a.Flipped} · 100nm {r7b.Found}");

        // ⑧ 합성(ComposeWalls) 끝에서 — 현장 0930 절토 덤프 + 1km 떨어진 네 면 판(정지면 그대로) · 턱 5mm는 뒤집고 판의 오차 = 큰 쪽 · 12mm는 멈추고 까닭
        string fa = Path.Combine(dataDir, "현장0930_1338_절토_합성.txt");
        if (!WallDaylight.TryReadBandInput(fa, out var pA, out var wA, out var gA, out var plA, out _)) { check("S159 ⑧ 0930 절토 덤프를 읽었다", false, fa); return; }
        var A = new WallDaylight.WallInput { No = 1, Poly = pA, WallTris = wA, GroundTris = gA, Down = false };
        var c0 = WallDaylight.ComposeWalls(plA, new[] { A });
        var p5 = new List<Tri>(plA); p5.AddRange(Quad(1.0, 5e-9, 1.0, 119.992, dx: pA[0].X - X0 + 1000));
        var c5 = WallDaylight.ComposeWalls(p5, new[] { A });
        check("S159 ⑧ 덤프 그대로 — 고칠 면 0(정지면 먼저 뒤집기 안 돎)", c0.All != null && c0.Outside.Found == 0 && c0.Outside.Flipped == 0, c0.Outside.Text);
        check("S159 ⑧ 턱 5mm 판 — 합성 됨 · 뒤집음 1 · 면 수 +4 · 높이 오차 합 = max(옹벽, 5mm) · 판정 2",
            c5.All != null && c0.All != null && c5.Outside.Flipped == 1 && c5.All.Count == c0.All.Count + 4
            && Math.Abs(c5.HeightBudget - Math.Max(c0.HeightBudget, 5e-3)) < 1e-7 && c5.Tier == Math.Max(c0.Tier, 2) && c5.Summary.Contains("정지면 먼저 뒤집기"),
            $"{c5.Summary}");
        if (c5.All != null && c0.All != null)
        {
            var keep = new HashSet<Tri>(c5.All);
            check("S159 ⑧ 턱 5mm 판 — 옹벽 합성 쪽은 비트 그대로(덤프만 넣은 합성의 면이 다 있다)", c0.All.All(keep.Contains), $"{c0.All.Count(keep.Contains)}/{c0.All.Count}");
        }
        var p12 = new List<Tri>(plA); p12.AddRange(Quad(1.0, 5e-9, 1.0, 119.985, dx: pA[0].X - X0 + 1000));
        var c12 = WallDaylight.ComposeWalls(p12, new[] { A });
        check("S159 ⑧ 턱 12mm 판 — 한도 10mm 넘음 → 합성 없음 · 끝의 높이 오차(FailFinal) · 까닭 «옹벽이 손 안 댄 정지면_DH 자리» · 옹벽 탓 아님",
            c12.All == null && c12.FailFinal && c12.Fail.Contains("옹벽이 손 안 댄 정지면_DH 자리") && c12.Fail.Contains("12.0mm"), c12.Fail);

        // ⑨ ★[코드 검토 중간 1] 사슬 — 긴 변 P1–P3 위에 꼭짓점 P2a(1/3 · 턱 6mm) · P2b(2/3 · 턱 11mm) · 넓이 0 면 둘(Z1 = P1 P3 P2a · Z2 = P2a P3 P2b).
        //   출하와 같은 술어(처음 면만 정지면 그대로)로 — 첫 뒤집기가 만든 면이 Z2의 긴 변 건너 면이 된다. 종전엔 2판부터 «옹벽 구역 면 낌»으로 남았다
        {
            Point3 P1 = new(X0, Y0, 100), P3 = new(X0 + 1, Y0, 100), P2a = new(X0 + 1.0 / 3, Y0, 100.006), P2b = new(X0 + 2.0 / 3, Y0, 100.011);
            Point3 Xb = new(X0 + 0.5, Y0 - 1, 95), Yt = new(X0 + 0.5, Y0 + 1, 105);
            var ch = new List<Tri> { new(P1, Xb, P3), new(P1, P3, P2a), new(P2a, P3, P2b), new(P1, P2a, Yt), new(P2a, P2b, Yt), new(P2b, P3, Yt) };
            var orig9 = new HashSet<Tri>(ch);
            var f9 = CivilSafeMesh.FixOutside(ch, t => orig9.Contains(t), out var r9);
            check("S159 ⑨ 사슬 — 찾음 2 · 뒤집음 2쌍 · 남음 0 · 끝 모양 = X에서 부채꼴(P1 P2a · P2a P2b · P2b P3)",
                r9.Found == 2 && r9.Flipped == 2 && r9.Left == 0
                && f9[0].Equals(new Tri(P3, P2b, Xb)) && f9[1].Equals(new Tri(P2a, P1, Xb)) && f9[2].Equals(new Tri(P2b, P2a, Xb)) && f9.Skip(3).SequenceEqual(ch.Skip(3)) && f9.All(t => Cr(t) > 0),
                $"{r9.Text} · " + string.Join(" / ", f9.Take(3).Select(K6)));
            // 높이 위 한계 — 둘째 뒤집기 8mm(첫 뒤집기로 바뀐 면 위에서) + 그 면이 이미 바뀐 6mm = 14mm ≥ P2b 자리 실제 변화 11mm(판마다 max였으면 8mm로 모자랐다)
            check("S159 ⑨ 사슬 높이 — 위 한계 14mm(8 + 6) ≥ 실제 변화 11mm(P2b 턱)", Math.Abs(r9.MaxDz - 14e-3) < 1e-7 && r9.MaxDz >= 11e-3 - 1e-9, $"{r9.MaxDz * 1000:F4}mm @{r9.At}");
        }

        // ⑩ ★[코드 검토 중간 2] «Civil이 반드시 합칠 쌍 곁» 허용(한도 15mm)을 쓴 옹벽이 있어도 정지면 먼저 뒤집기는 1cm — 0930 성토(허용 판) + 1km 떨어진 턱 12mm 판
        string fb = Path.Combine(dataDir, "현장0930_1340_성토_합성.txt");
        if (!WallDaylight.TryReadBandInput(fb, out var pB, out var wB, out var gB, out var plB, out _)) { check("S159 ⑩ 0930 성토 덤프를 읽었다", false, fb); return; }
        var B = new WallDaylight.WallInput { No = 2, Poly = pB, WallTris = wB, GroundTris = gB, Down = true };
        var pb12 = new List<Tri>(plB); pb12.AddRange(Quad(1.0, 5e-9, 1.0, 119.985, dx: pB[0].X - X0 + 1000));
        var cb12 = WallDaylight.ComposeWalls(pb12, new[] { B });
        var pb5 = new List<Tri>(plB); pb5.AddRange(Quad(1.0, 5e-9, 1.0, 119.992, dx: pB[0].X - X0 + 1000));
        var cb5 = WallDaylight.ComposeWalls(pb5, new[] { B });
        check("S159 ⑩ 허용 판 옹벽(한도 15mm) + 정지면 턱 12mm — 1cm 넘음으로 멈춤(종전 관문처럼 받지 않음) · 까닭 «한도 10mm · 옹벽 탓이 아니다»",
            cb5.OverForced && cb5.HeightLimit > 1e-2 && cb12.All == null && cb12.FailFinal && cb12.Fail.Contains("한도 10mm · 옹벽 탓이 아니다"), $"허용 {cb5.OverForced} · 한도 {cb5.HeightLimit * 1000:F0}mm · {cb12.Fail}");
        check("S159 ⑩ 허용 판 옹벽 + 정지면 턱 5mm — 합성 됨 · 뒤집음 1 · 높이 오차 합 = max(옹벽, 5mm)",
            cb5.All != null && cb5.Outside.Flipped == 1 && Math.Abs(cb5.HeightBudget - Math.Max(cb5.Steps[0].R.HeightBudget, 5e-3)) < 1e-7, cb5.Summary);
    }
}
