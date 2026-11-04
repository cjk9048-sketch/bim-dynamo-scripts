// ── S195 ~ S199 · S201 ★★★[v113.0 · JACK 1104 «옹벽 폴리선 — 경계 2점 자르기 + 안쪽 다단»] 경계 판정 · 띠 · 줄 · 합성 «바꿔 끼움» · 목록 판 6 ──
using DH.Grading.Core;
using Tri = DH.Grading.Core.WallDaylight.Tri;

static class WallLineCheck
{
    // 합성 시험 판 — 부지 [0,40]×[−60,60] z = 100 · 원지반 100 + 0.25(x − 30) · 동쪽 절토 1:1.5 → 데이라잇 x = 46
    static double G(double x, double y) => 100 + 0.25 * (x - 30);
    static double P0(double x, double y) => x <= 40 ? 100 : x < 46 ? 100 + (x - 40) / 1.5 : G(x, y);
    static List<Point3> Rect(double x0, double y0, double x1, double y1, Func<double, double, double>? z = null)
    {
        z ??= (_, _) => 0;
        return new() { new(x0, y0, z(x0, y0)), new(x1, y0, z(x1, y0)), new(x1, y1, z(x1, y1)), new(x0, y1, z(x0, y1)) };
    }
    static List<Tri> Grid(double x0, double y0, double x1, double y1, double h, Func<double, double, double> z)
    {
        var t = new List<Tri>();
        for (double x = x0; x < x1 - 1e-9; x += h)
            for (double y = y0; y < y1 - 1e-9; y += h)
            {
                Point3 P(double a, double b) => new(a, b, z(a, b));
                t.Add(new Tri(P(x, y), P(x + h, y), P(x + h, y + h)));
                t.Add(new Tri(P(x, y), P(x + h, y + h), P(x, y + h)));
            }
        return t;
    }
    static double? At(IEnumerable<Tri> tris, double x, double y)
    {
        foreach (var t in tris)
        {
            double d = (t.B.Y - t.C.Y) * (t.A.X - t.C.X) + (t.C.X - t.B.X) * (t.A.Y - t.C.Y);
            if (Math.Abs(d) < 1e-18) continue;
            double l1 = ((t.B.Y - t.C.Y) * (x - t.C.X) + (t.C.X - t.B.X) * (y - t.C.Y)) / d, l2 = ((t.C.Y - t.A.Y) * (x - t.C.X) + (t.A.X - t.C.X) * (y - t.C.Y)) / d, l3 = 1 - l1 - l2;
            if (l1 >= -1e-9 && l2 >= -1e-9 && l3 >= -1e-9) return l1 * t.A.Z + l2 * t.B.Z + l3 * t.C.Z;
        }
        return null;
    }

    static readonly List<Point3> Bd = Rect(-20, -60, 46, 60);
    static List<Point3> Plan(double zx = 0) => Rect(0, -60, 40, 60, (x, y) => 100 + zx * y);
    static readonly List<Point3> Ok = new() { new(52, -10, 0), new(43, -5, 0), new(43, 5, 0), new(52, 10, 0) };

    static (BoundaryCut.Result, WallByLine.Built?) MakeCut(IReadOnlyList<Point3> src, double h = 2, double t = 1, List<Point3>? plan = null,
        Func<double, double, double>? g = null, List<Tri>? p0 = null, double margin = 10, double dx = 0, double dy = 0, double sz = 1,
        Func<double, double, double?>? p0f = null, List<IReadOnlyList<Point3>>? clip = null)
    {
        g ??= G;
        var pl = (plan ?? Plan()).Select(q => new Point3(q.X + dx, q.Y + dy, sz * q.Z)).ToList();
        var bd = Bd.Select(q => new Point3(q.X + dx, q.Y + dy, 0)).ToList();
        var s = src.Select(q => new Point3(q.X + dx, q.Y + dy, 0)).ToList();
        return WallByLine.Make(s, bd, pl, null, h, t, 0.01, 0.05, 1, margin,
            (x, y) => sz * g(x - dx, y - dy), p0f ?? ((x, y) => sz * P0(x - dx, y - dy)), p0, clip);
    }

    public static void Run(Action<string, bool, string> check)
    {
        Console.WriteLine("\n== S195 경계 판정(BoundaryCut.Classify) ==");
        var floor = Plan();
        void Case(string nm, List<Point3> p, Func<BoundaryCut.Result, bool> ok, IEnumerable<(string, IReadOnlyList<Point3>)>? bl = null)
        {
            var r = BoundaryCut.Classify(p, Bd, floor, bl);
            check("S195 " + nm, ok(r), $"→ {BoundaryCut.CodeName(r.Code)} · 교차 {r.Crossings} · {r.Why}");
        }
        Case("① 두 번 지남 — 통과 · A(46, −6.667) · B(46, 6.667)", Ok, r => r.Ok && Math.Abs(r.A.X - 46) < 1e-9 && Math.Abs(r.A.Y + 20.0 / 3) < 1e-6 && Math.Abs(r.B.Y - 20.0 / 3) < 1e-6 && r.Crossings == 2);
        Case("② 0회(밖에서만)", new() { new(60, -10, 0), new(60, 10, 0) }, r => r.Code == BoundaryCut.Code.Zero);
        Case("③ 끝이 안", new() { new(52, 0, 0), new(43, 0, 0) }, r => r.Code == BoundaryCut.Code.EndInside);
        Case("④ 4회", new() { new(52, -10, 0), new(43, -8, 0), new(52, -6, 0), new(43, -4, 0), new(52, -2, 0) }, r => r.Code == BoundaryCut.Code.Many && r.Crossings == 4);
        Case("⑤ 꼭짓점이 경계 위 — 닿기만", new() { new(52, -10, 0), new(46, 0, 0), new(52, 10, 0) }, r => r.Code == BoundaryCut.Code.Touch);
        Case("⑥ 꼭짓점이 경계 위 — 지나감(교차로 셈)", new() { new(52, -10, 0), new(46, -5, 0), new(43, 0, 0), new(52, 5, 0) }, r => r.Ok && r.Crossings == 2);
        Case("⑦ 경계를 따라 겹쳐 감", new() { new(52, -10, 0), new(46, -8, 0), new(46, 8, 0), new(52, 10, 0) }, r => r.Code == BoundaryCut.Code.Along);
        Case("⑧ 제 몸 지름", new() { new(52, -10, 0), new(43, 5, 0), new(43, -5, 0), new(52, 10, 0) }, r => r.Code == BoundaryCut.Code.SelfCross);
        Case("⑨ 부지 바닥을 지남", new() { new(52, -10, 0), new(38, -5, 0), new(38, 5, 0), new(52, 10, 0) }, r => r.Code == BoundaryCut.Code.Floor || r.Code == BoundaryCut.Code.Split);
        Case("⑩ 부지를 둘로 가름", new() { new(60, 0, 0), new(-30, 0, 0) }, r => r.Code == BoundaryCut.Code.Split || r.Code == BoundaryCut.Code.Floor);
        Case("⑪ 바닥 0.1 m 안", new() { new(52, -10, 0), new(40.05, -5, 0), new(40.05, 5, 0), new(52, 10, 0) }, r => r.Code == BoundaryCut.Code.Floor);
        Case("⑫ 막을 다각형과 겹침", Ok, r => r.Code == BoundaryCut.Code.Blocked, new[] { ("옹벽 7", (IReadOnlyList<Point3>)Rect(44, -2, 48, 2)) });
        Case("⑭ 닫힌 폴리선 → 거절(설치 전 검토 L2)", new() { new(52, -10, 0), new(43, -5, 0), new(43, 5, 0), new(52, 10, 0), new(52, -10, 0) }, r => r.Code == BoundaryCut.Code.Closed);
        {
            // 현장 크기 좌표(5186 · 20만대)
            double dx = 200000, dy = 550000;
            var r = BoundaryCut.Classify(Ok.Select(q => new Point3(q.X + dx, q.Y + dy, 0)).ToList(), Bd.Select(q => new Point3(q.X + dx, q.Y + dy, 0)).ToList(), floor.Select(q => new Point3(q.X + dx, q.Y + dy, q.Z)).ToList());
            check("S195 ⑬ 현장 크기 좌표 — 같은 답(A · B 1µm)", r.Ok && Math.Abs(r.A.X - dx - 46) < 1e-6 && Math.Abs(r.A.Y - dy + 20.0 / 3) < 1e-6 && Math.Abs(r.B.Y - dy - 20.0 / 3) < 1e-6, $"{BoundaryCut.CodeName(r.Code)} A({r.A.X - dx:F7},{r.A.Y - dy:F7})");
        }

        Console.WriteLine("\n== S196 새 경계 · R_out · 띠 · Z ==");
        {
            var r = BoundaryCut.Classify(Ok, Bd, floor);
            double sum = r.AreaNew + r.AreaCut;
            check("S196 ① 넓이 옛 = 새 + R_out(1e-6 상대)", Math.Abs(r.AreaOld - sum) <= 1e-6 * r.AreaOld, $"{r.AreaOld:F6} vs {sum:F6}");
            double expCut = (3 * (40.0 / 3) + 3 * 10) / 2;   // 사다리꼴: x 43 → 46 · 길이 10 → 13.333
            check("S196 ② R_out 넓이 = 사다리꼴 " + expCut.ToString("F4"), Math.Abs(r.AreaCut - expCut) < 1e-6, $"{r.AreaCut:F6}");
            var (_, b) = MakeCut(Ok);
            check("S196 ③ 짓기 통과 · 띠 단순 · Z 한 다각형", b != null && b.Ok && GradingGeometry.RingIsSimple(b.Strip) && GradingGeometry.RingIsSimple(b.Zone), b == null ? "" : $"{b.Code} {b.Why} · {b.Log}");
            if (b != null && b.Ok)
            {
                double sA = GradingGeometry.RingAreaNts(b.Strip);
                double expS = (3 + 3) / 2.0 * 0 + 3 * 10 + 2 * ((6 + 3) / 2.0 * (20.0 / 3 - 5));   // x 40 → 43 · y −5..5 + 끝 두 사다리꼴(x 40..43~46)
                check("S196 ④ 띠 넓이(손셈)", Math.Abs(sA - expS) < 1e-6, $"{sA:F6} vs {expS:F6}");
                check("S196 ⑤ A′ · B′ = 계획선 위 가장 가까운 점", Math.Abs(b.A2.X - 40) < 1e-9 && Math.Abs(b.A2.Y + 20.0 / 3) < 1e-6 && Math.Abs(b.B2.X - 40) < 1e-9, $"A′({b.A2.X},{b.A2.Y}) B′({b.B2.X},{b.B2.Y})");
                check("S196 ⑥ 여유 R_out(경계 밖 10 m까지)", b.MarginUsed && b.CutExt.Max(q => q.X) > 55, $"여유 {b.MarginUsed} · 최대 x {b.CutExt.Max(q => q.X):F2}");
                // ⑦ 열린 질문 — A · B(경계 x = 46 위)가 정지면 삼각망 바깥에 떨어질 때: 1 mm 안쪽으로 읽고 · 그래도 못 읽으면 원지반
                var (_, bN) = MakeCut(Ok, p0f: (x, y) => x >= 46 - 1e-12 ? null : P0(x, y));
                var (_, bG) = MakeCut(Ok, p0f: (x, y) => x > 45.9 ? null : P0(x, y));
                check("S196 ⑦ 경계 위 A · B 정지면 못 읽음 — 1 mm 안쪽(로그 없음) · 그래도 없으면 원지반(로그) · 둘 다 지음", bN != null && bN.Ok && !bN.Log.Contains("⑨-b") && bG != null && bG.Ok && bG.Log.Contains("⑨-b"),
                      $"{bN?.Code} {bN?.Why} / {bG?.Code} {bG?.Why}");
                // ⑧ 여유가 다른 옹벽 폴리곤을 비켜 감(설치 전 검토 M5) — 경계 밖 [48, 60] × [−30, −9] 를 빼고 R_out은 그대로
                var clipR = new List<IReadOnlyList<Point3>> { Rect(48, -30, 60, -9) };
                var (_, bC) = MakeCut(Ok, clip: clipR);
                double ovC = bC != null && bC.Ok ? GradingGeometry.RingAreaNts(bC.CutExt) : 0;
                bool noOv = bC != null && bC.Ok && bC.CutExt.All(q => !(q.X > 48 + 1e-6 && q.X < 60 - 1e-6 && q.Y > -30 + 1e-6 && q.Y < -9 - 1e-6));
                check("S196 ⑧ 여유가 다른 옹벽 자리를 비켜 감 — 넓이 줄고 그 자리 안 꼭짓점 없음", noOv && ovC < GradingGeometry.RingAreaNts(b.CutExt) - 1, $"{bC?.Code} {bC?.Why} · 여유 R_out {ovC:F1} / {GradingGeometry.RingAreaNts(b.CutExt):F1}㎡");
            }
        }

        Console.WriteLine("\n== S197 줄(절토) · 폭 모자람 · 기운 바닥 · 섞임 ==");
        {
            var (_, b) = MakeCut(Ok);
            if (b == null || !b.Ok) { check("S197 짓기", false, b?.Why ?? "판정 거절"); }
            else
            {
                check("S197 ① 절토 · 단 3개(원지반 − 바닥 최대 4.0 = 2단 딱 → 한 단 더)", b.Up && b.Tiers == 3, $"{(b.Up ? "절토" : "성토")} {b.Tiers}단");
                check("S197 ② 줄 수 = 2n(0번 + 면 끝 n + 소단 끝 n−1)", b.Rows.Count == 6, $"{b.Rows.Count}");
                double[] zs = { 106, 104, 104, 102, 102, 100 };
                double[] ds = { 0, 0.05, 1.05, 1.10, 2.10, 2.15 };
                bool zOk = true, dOk = true; string det = "";
                for (int i = 0; i < Math.Min(6, b.Rows.Count); i++)
                {
                    var row = b.Rows[i];
                    double zMax = row.Max(q => Math.Abs(q.Z - zs[i]));
                    var mid = row.OrderBy(q => Math.Abs(q.Y)).First();
                    double d = 43 - mid.X;
                    if (zMax > 1e-9) zOk = false;
                    if (Math.Abs(d - ds[i]) > 1e-3) dOk = false;
                    det += $" [{i}] z {row[0].Z:F3} 물러남 {d:F3}";
                }
                check("S197 ③ 줄 높이 = 바닥 + (n − k)·H(1e-9)", zOk, det);
                check("S197 ④ 물러난 거리(가운데) = 기대 ± 1 mm", dOk, det);
                check("S197 ⑤ 측선 안 단면 · 뚜껑 줄이 있다(선 ≥ 1 + 6 + 2 + 1) · 경고 없음(줄 끝 = 측선 안 단면 점)", b.Lines.Count >= 10 && b.Warn.Count == 0, $"선 {b.Lines.Count} · 경고 {string.Join(" · ", b.Warn)}");
                check("S197 ⑥ 뚜껑 = R_out 원지반 최고 + 1 ≥ 머리 + 1", b.CapZ >= 107 - 1e-9, $"{b.CapZ:F3}");
            }
            var (_, bn) = MakeCut(Ok, h: 1);
            check("S197 ⑦ 폭 모자람 → 거절 + 필요 폭", bn != null && !bn.Ok && bn.Code == "폭" && bn.Why.Contains("필요한 폭"), bn?.Why ?? "판정 거절");
            var (_, bs) = MakeCut(Ok, plan: Plan(0.02));
            bool sOk = bs != null && bs.Ok;
            double sErr = 0;
            if (sOk) foreach (var row in bs!.Rows.Take(1)) foreach (var q in row) sErr = Math.Max(sErr, Math.Abs(q.Z - (100 + 0.02 * q.Y + 6)));
            check("S197 ⑧ 기운 바닥 — 0번 줄이 바닥을 따라 기움(z = 100 + 0.02y + 3·2)", sOk && sErr < 1e-6, sOk ? $"최대 차 {sErr:E2} · 바닥 {bs!.PadMin:F3}~{bs.PadMax:F3}" : bs?.Why ?? "");
            var (_, bm) = MakeCut(Ok, g: (x, y) => 100 + 0.5 * y);
            check("S197 ⑨ 절 · 성 섞임 → 거절(자리)", bm != null && !bm.Ok && bm.Code == "섞임" && bm.Marks.Count > 0, bm?.Why ?? "");
            // ⑩ 폴리선 위는 절토인데 띠 안(계획선 곁)이 성토 — 거절(설치 전 검토 M2)
            var (_, bf) = MakeCut(Ok, g: (x, y) => x < 41 ? 99.5 : G(x, y));
            check("S197 ⑩ 띠 안 성토 섞임 → 거절(자리 · 띠 안)", bf != null && !bf.Ok && bf.Code == "섞임" && bf.Why.Contains("띠") && bf.Marks.Count > 0 && bf.Marks[0].X < 41, bf?.Why ?? "");
            // ⑪ 소단 0 — 소단 끝 줄을 안 넣음(겹친 브레이크라인 없음 · 설치 전 검토 L3): 줄 = 0번 + 면 끝 n
            var (_, b0) = MakeCut(Ok, t: 0);
            check("S197 ⑪ 소단 0 — 줄 n + 1개 · 경고 없음", b0 != null && b0.Ok && b0.Rows.Count == b0.Tiers + 1 && b0.Warn.Count == 0, b0 == null ? "" : $"{b0.Code} {b0.Why} · 줄 {b0.Rows.Count} · 단 {b0.Tiers} · {string.Join(" · ", b0.Warn)}");
        }

        Console.WriteLine("\n== S198 합성 «바꿔 끼움» 절토 · S199 성토 거울 ==");
        foreach (bool fill in new[] { false, true })
        {
            string sec = fill ? "S199" : "S198";
            double sz = fill ? -1 : 1;
            var pT = Grid(20, -40, 70, 40, 1, (x, y) => sz * P0(x, y));
            var gT = Grid(20, -40, 70, 40, 1, (x, y) => sz * G(x, y));
            var (cut, b) = MakeCut(Ok, p0: pT, sz: sz);
            if (b == null || !b.Ok) { check($"{sec} 짓기", false, b?.Why ?? cut.Why); continue; }
            check($"{sec} ⓪ 방향 {(fill ? "성토" : "절토")}", b.Up == !fill, "");
            var W = WallByLine.Mesh(b);
            var r = WallDaylight.BuildComposite(b.Zone, W, gT, pT, down: fill, replace: true);
            check($"{sec} ① 판정 1~2 · 합성 됨", r.Zone != null && r.Tier <= 2, $"판정 {r.Tier} · {r.Fail} · W {W.Count}면");
            if (r.Zone == null) { Console.WriteLine("    " + r.Summary); continue; }
            var all = r.Zone.Concat(r.Untouched).ToList();
            string det = ""; bool ok = true;
            void Probe(string nm, double x, double y, double want)
            {
                var z = At(all, x, y);
                bool good = z != null && Math.Abs(z.Value - sz * want) <= 1e-3;
                if (!good) ok = false;
                det += $" {nm}({x},{y}) {z:F4}/{sz * want:F4}{(good ? "" : "✗")}";
            }
            Probe("R_out", 45, 0, G(45, 0));
            Probe("여유", 50, -1, G(50, -1));
            Probe("바닥", 40.5, 0, 100);
            Probe("소단2", 41.5, 0, 102);
            Probe("묻힌 위 단", 42.5, 0, G(42.5, 0));
            Probe("Z 밖 사면", 42, -20, P0(42, -20));
            check($"{sec} ② 자리마다 높이(1 mm) — R_out = 원지반 · 바닥 = 부지 · 소단 · 묻힌 단 = 원지반 · Z 밖 그대로", ok, det);
            check($"{sec} ③ 테두리 턱 ≤ 1 mm", r.EdgeMax <= 1e-3, $"{r.EdgeMax * 1000:F3}mm @{r.EdgeAt}");
            check($"{sec} ④ 뚜껑(원지반 조각)이 있다", r.CapTris != null && r.CapTris.Count > 0, $"{r.CapTris?.Count}");
        }

        Console.WriteLine("\n== S201 옹벽 목록 판 6 ==");
        {
            var head = new WallListHead { PlanHandle = "1A", GroundHandle = "2B", NextNo = 4 };
            var w0 = new WallRec { No = 1, State = 1, Up = true, Bench = 0, H = 5, T = 1, HeadZ = 105, Seg = new() { new(0, 0, 0), new(1, 0, 0) }, Poly = Rect(0, 0, 1, 1), IsWall = new() { true, true, false, false } };
            var v5 = WallList.Encode(head, new List<WallRec> { w0 });
            check("S201 ① 종류 0뿐이면 판 6을 안 쓴다(옛 바이트 그대로)", (int)v5[1].Value <= 5, $"판 {v5[1].Value}");
            var w1 = new WallRec
            {
                No = 2, State = 0, Up = false, Bench = -1, H = 3, T = 1.5, HeadZ = 91, Kind = 1, Seg = new() { new(5, 5, 0), new(6, 6, 0) }, Poly = Rect(5, 5, 9, 9), IsWall = new() { true, false, false, true },
                Line = new() { new(5, 5, 0), new(6, 6, 0), new(7, 5, 0) }, Src = new() { new(4, 4, 0), new(8, 4, 0) }, PadZ = 100.25, Tiers = 3, CapZ = 80.5, SrcHandle = "3C",
            };
            head.Frozen.Add(new FrozenWall { No = 3, Up = true, H = 2, Poly = Rect(0, 0, 2, 2), IsWall = new() { true, true, true, true }, Kind = 1, Line = new() { new(1, 1, 0), new(2, 2, 0) }, PadZ = 99, Tiers = 2, CapZ = 120 });
            var v6 = WallList.Encode(head, new List<WallRec> { w0, w1 });
            bool dec = WallList.TryDecode(v6, out var h2, out var l2, out string why);
            var r1 = dec ? l2.First(w => w.No == 2) : null; var r0 = dec ? l2.First(w => w.No == 1) : null;
            bool same = dec && (int)v6[1].Value == 6 && r0!.Kind == 0 && r1!.Kind == 1 && r1.Line.Count == 3 && r1.Line[2].X == 7 && r1.Src.Count == 2 && r1.PadZ == 100.25 && r1.Tiers == 3 && r1.CapZ == 80.5 && r1.SrcHandle == "3C"
                        && h2.Frozen.Count == 1 && h2.Frozen[0].Kind == 1 && h2.Frozen[0].Line.Count == 2 && h2.Frozen[0].Tiers == 2 && h2.Frozen[0].CapZ == 120;
            check("S201 ② 판 6 왕복(종류 1 칸 전부 · 굳은 칸)", same, dec ? $"판 {v6[1].Value}" : why);
            bool dec5 = WallList.TryDecode(v5, out _, out var l5, out string w5);
            check("S201 ③ 판 5 이하 바이트 → 읽기 → 종류 0 · 다른 칸 같음", dec5 && l5.Count == 1 && l5[0].Kind == 0 && l5[0].HeadZ == 105 && l5[0].IsWall.Count == 4, w5);
            // 모르는 종류(2)는 거절 — 조용히 엉뚱하게 읽지 않는다
            bool rej = false;
            for (int i = 0; i < v6.Count; i++)
            {
                if (v6[i].Kind != 'I' || (int)v6[i].Value != 1) continue;
                var t2 = new List<(char, object)>(v6); t2[i] = ('I', 2);
                if (!WallList.TryDecode(t2, out _, out _, out string wb) && wb.Contains("종류")) { rej = true; break; }
            }
            check("S201 ④ 모르는 종류 → 거절(까닭에 «종류»)", rej, "");
        }
    }
}
