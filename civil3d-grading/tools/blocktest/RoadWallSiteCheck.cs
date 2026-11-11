// ── S262 RV ★★[v117.x · JACK 1111 «옹벽 추가는 부지나 진입도로나 똑같은 로직 · 부지로 다 완성하고 진입도로에 대입» · 계획 `2026-11-12_진입도로_옹벽추가_부지로직.md` 5절]
//   진입도로 [옹벽 추가] 수직 · 다단 = 부지 Core(BuildVertical · BuildTiered) 그대로 · 고리 면 P0 위 · 합성 = 부지와 같은 인자(NoZero · ExactG) + 도로 테두리 관문(Bd 위만 뺌).
//   만든 꼴(현장 값 없음 — 공개 저장소) · 좌표는 현장 크기(RoadWallCheck와 같은 원점) · 원지반 = 1 m 격자 TIN.
//   받음: 지어짐 · 도로 면에 R_out(선 밖) 세모 0 · L 위 도로 면 꼭짓점 = 바탕(1 mm) · 띠 폭 = 0.01·h · 관문(바늘 · 0.1 mm 변 · 세 세모 변 · 쌍) 0 · 표본 되읽기 1 cm · 옹벽선 = L · 노리선 L 밖 0 ·
//   옛 모양(Out) 벽과 함께여도 옛 벽 세모 그대로 · 지문(Shape 기본값 JSON = 옛 판 바이트).
using DH.Grading.Core;
using NetTopologySuite.Geometries;
using Tri = DH.Grading.Core.WallDaylight.Tri;

static class RoadWallSiteCheck
{
    const double OX = RoadWallCheck.OX, OY = RoadWallCheck.OY;
    sealed class RP { public RoadGrade.Input Inp = null!; public RoadRing.Built B = null!; public RoadGrade.Result R = null!; }

    static RP? Ring(RoadGrade.Input inp, Action<string, bool, string> check, string name)
    {
        var b = RoadRing.Build(inp);
        check(name + " 고리 면 지어짐", b.Ok && b.Tris.Count > 0, b.Note.Length > 160 ? b.Note[..160] : b.Note);
        return b.Ok ? new RP { Inp = inp, B = b, R = RoadRing.ToResult(b, inp) } : null;
    }
    static RoadGrade.WallSurface Apply(RP p, params RoadGrade.LineWall[] w) => RoadGrade.ApplyWalls(p.Inp, p.R, p.B.Tris, p.B.Lines, "고리 면", w, ring: p.B);
    static RoadGrade.LineWall W(int no, RoadGrade.LineWall src, WallByLine.WallShape shape, double? h = null, double? b = null, double? z1 = null) => src with { No = no, Shape = shape, TierH = h, TierB = b, TierZ1 = z1 };
    static string Txt(RoadGrade.WallState w) => w.Ok ? w.Text + " · " + (w.Built?.SelfCheck ?? "") : $"{w.Code} — {(w.Why.Length > 260 ? w.Why[..260] : w.Why)}";

    /// <summary>지은 벽 하나를 잰다 — 선 밖 아무것도 없음 · L 위 = 바탕 · 띠 폭 · 관문 · 표본 되읽기 · 옹벽선 · 노리선.</summary>
    static void Measure(string nm, RP p, RoadGrade.WallSurface ws, RoadGrade.WallState st, bool up, int shape, Action<string, bool, string> check)
    {
        var b = st.Built!; var cut = st.Cut!;
        var gf = new GeometryFactory();
        Polygon Pg(IReadOnlyList<Point3> r) => gf.CreatePolygon(r.Append(r[0]).Select(q => new Coordinate(q.X, q.Y)).ToArray());
        check($"{nm} 모양 {shape} · {(up ? "절토" : "성토")}", b.Shape == shape && (up ? b.HasCut && !b.HasFill : b.HasFill && !b.HasCut) && (shape != 3 || b.TierN >= 2),
            $"모양 {b.Shape} · 절 {b.HasCut} · 성 {b.HasFill} · 단 {b.TierN} · {b.SelfCheck}");
        // ① 선 밖 아무것도 없음 — 도로 면 세모 무게중심 · 꼭짓점(1 µm 넘게 안)이 R_out 안 = 0
        var rOut = Pg(cut.CutRing); var rOutIn = rOut.Buffer(-1e-6);
        var rP = NetTopologySuite.Geometries.Prepared.PreparedGeometryFactory.Prepare(rOut); var rPi = NetTopologySuite.Geometries.Prepared.PreparedGeometryFactory.Prepare(rOutIn);
        int cIn = 0, vIn = 0;
        foreach (var t in ws.Tris)
        {
            if (rP.Contains(gf.CreatePoint(new Coordinate((t.A.X + t.B.X + t.C.X) / 3, (t.A.Y + t.B.Y + t.C.Y) / 3)))) cIn++;
            foreach (var v in new[] { t.A, t.B, t.C }) if (rPi.Contains(gf.CreatePoint(new Coordinate(v.X, v.Y)))) { vIn++; break; }
        }
        check($"{nm} 선 밖(R_out) 도로 세모 0 · 꼭짓점 0 · 뺀 세모 > 0", cIn == 0 && vIn == 0 && st.Removed > 0 && b.OutsideN == 0, $"무게중심 {cIn} · 꼭짓점 {vIn} · 뺌 {st.Removed} · Core 바깥 {b.OutsideN}");
        // ② L 위 도로 면 꼭짓점 = 바탕(1 mm) — 도로 면은 L에서 끝나고 그 높이가 원지반(최종지표면이 선 밖을 바탕으로 채워도 턱 없음)
        var lG = gf.CreateLineString(cut.Line.Select(q => new Coordinate(q.X, q.Y)).ToArray());
        int nL = 0; double dL = 0;
        foreach (var v in ws.Tris.SelectMany(t => new[] { t.A, t.B, t.C }).Distinct())
        {
            if (lG.Distance(gf.CreatePoint(new Coordinate(v.X, v.Y))) > 1e-6) continue;
            if (!p.Inp.Base.TryGetElevation(v.X, v.Y, out double zg)) continue;
            nL++; dL = Math.Max(dL, Math.Abs(v.Z - zg));
        }
        check($"{nm} L 위 도로 면 꼭짓점 = 바탕(1 mm)", nL >= 10 && dL <= 1e-3, $"꼭짓점 {nL} · 최대 {dL * 1000:F3} mm");
        // ③ 띠 폭 = s·h(꼬리 0.25 mm) · 최고 h ≈ L 가운데 |G − P0|(1:0.01 풀이 · 3 %)
        var p0at = WallByLine.TriLookup(p.B.Tris);
        var mid = lG.GetCoordinateN(0);
        var lc = lG.Coordinates; double half = lG.Length / 2, acc = 0;
        for (int i = 0; i + 1 < lc.Length; i++) { double l = lc[i].Distance(lc[i + 1]); if (acc + l >= half) { double f = (half - acc) / l; mid = new Coordinate(lc[i].X + f * (lc[i + 1].X - lc[i].X), lc[i].Y + f * (lc[i + 1].Y - lc[i].Y)); break; } acc += l; }
        double hMid = double.NaN;
        if (p.Inp.Base.TryGetElevation(mid.X, mid.Y, out double gm) && p0at(mid.X, mid.Y) is double pm) hMid = Math.Abs(gm - pm);
        double bandOk = shape == 2 ? Math.Abs(b.WMax - Math.Max(2.5e-4, b.Batter * b.HMax)) : 0;
        check($"{nm} 띠 폭 = 0.01·최고 h · 최고 h ≥ L 가운데 |G − P0|", shape == 3 ? b.OuterHMax > 0 && b.OuterHMax <= (b.TierH + 1e-6) : bandOk < 2e-4 && double.IsFinite(hMid) && b.HMax >= hMid * 0.97,
            $"띠 {b.WMin * 1000:F2}~{b.WMax * 1000:F2} mm · 최고 {b.HMax:F3} m · 가운데 |G − P0| {hMid:F3} m · 바깥 최고 {b.OuterHMax:F2} · 안쪽 최고 {b.InnerHMax:F2} · 단높이 {b.TierH:F2}");
        // ④ 관문 흉내(짓는 길과 같은 잣대) 0
        var g = RoadWallCheck.Gate(ws.Tris);
        check($"{nm} 관문(바늘 · 0.1 mm 변 · 세 세모 변 · 0.1 mm 점 쌍) 0 · F 건넘 0", g == (0, 0, 0, 0) && st.Straddle == 0, $"{g} · 건넘 {st.Straddle} · 다듬기 한 번 더 {st.ReCleaned}");
        // ⑤ 표본 되읽기(부지 합성 되읽기 관문과 같은 1 cm) — 도로 면에서 다시 잼
        var rl = WallByLine.TriLookup(ws.Tris);
        int nB = 0; double dB = 0;
        foreach (var q in b.BenchPts) { if (rl(q.X, q.Y) is double z) { nB++; dB = Math.Max(dB, Math.Abs(z - q.Z)); } }
        check($"{nm} 표본 되읽기(도로 면 · 1 cm)", nB > 0 && dB <= 0.01, $"표본 {nB}/{b.BenchPts.Count} · 최대 {dB * 1000:F2} mm");
        // ⑥ 옹벽선(DH 옹벽선 · 횡단 옹벽 표시) — 바깥 벽 줄 = 그은 선 L(부지 WallLinePlan과 같음)
        var wl = ws.Lines.Where(l => l.WallNo == st.No).ToList();
        double offL = 0; int nW = 0;
        foreach (var l in wl.Take(1)) foreach (var q in l.Pts) { nW++; offL = Math.Max(offL, lG.Distance(gf.CreatePoint(new Coordinate(q.X, q.Y)))); }
        check($"{nm} 옹벽선 — 첫 줄 = L(1 µm) · {wl.Count}줄", wl.Count >= 1 && nW >= 2 && offL <= 1e-6, $"점 {nW} · L에서 최대 {offL * 1000:F4} mm");
        // ⑦ 노리선 — L을 건너 선 밖으로 뻗는 빗금은 Poly에서 잘림 · 선 밖 빗금은 빠짐 · Poly 밖 빗금 그대로
        var poly = Pg(b.Poly); var polyIn = NetTopologySuite.Geometries.Prepared.PreparedGeometryFactory.Prepare(poly.Buffer(-1e-6));
        var lm = cut.Line[cut.Line.Count / 2]; var lmN = cut.Line[cut.Line.Count / 2 - 1 >= 0 ? cut.Line.Count / 2 - 1 : 1];
        double tx = lm.X - lmN.X, ty = lm.Y - lmN.Y, tl = Math.Sqrt(tx * tx + ty * ty); tx /= tl; ty /= tl;
        var c0 = new Coordinate((lm.X + lmN.X) / 2, (lm.Y + lmN.Y) / 2);
        double sgn = rP.Contains(gf.CreatePoint(new Coordinate(c0.X - ty * 0.5, c0.Y + tx * 0.5))) ? 1 : -1;   // R_out 쪽 법선
        Point3 At(double d) => new(c0.X - sgn * ty * d, c0.Y + sgn * tx * d, 100);
        double d0 = shape == 3 ? b.TierDepth + 3 : 3;   // 다단은 소단 구역(깊이 D_T)도 Poly — 그 안쪽에서 시작
        var ticks = new List<(Point3 A, Point3 B)> { (At(-d0), At(2)), (At(0.3), At(1.0)), (At(-40), At(-35)) };
        var ct = RoadGrade.CutTicks(ticks, ws);
        int inside = ct.Count(t => polyIn.Contains(gf.CreatePoint(new Coordinate((t.A.X + t.B.X) / 2, (t.A.Y + t.B.Y) / 2))) || polyIn.Contains(gf.CreatePoint(new Coordinate(t.A.X, t.A.Y))) || polyIn.Contains(gf.CreatePoint(new Coordinate(t.B.X, t.B.Y))));
        double cutLen = ct.Count > 0 ? Math.Sqrt(Math.Pow(ct[0].B.X - ct[0].A.X, 2) + Math.Pow(ct[0].B.Y - ct[0].A.Y, 2)) : 0;
        bool farKept = ct.Any(t => t.A.Equals(At(-40)) && t.B.Equals(At(-35)));
        bool lenOk = shape == 2 ? Math.Abs(cutLen - (3 - b.WMax)) < b.WMax - b.WMin + 1e-6 : cutLen > 0.5 && cutLen < d0;
        check($"{nm} 노리선 — 건넌 빗금 안쪽 테두리에서 잘림 · 선 밖 빗금 빠짐 · 먼 빗금 그대로", inside == 0 && ct.Count == 2 && lenOk && farKept,
            $"남은 {ct.Count}(3 중) · 안 {inside} · 잘린 길이 {cutLen:F4} m(시작 {d0:F2} m 안쪽) · 먼 빗금 {farKept}");
    }

    public static void Run(Action<string, bool, string> check0)
    {
        void Check(string n, bool ok, string d) => check0("S262 RV" + n, ok, d);
        Console.WriteLine("\n== S262 진입도로 [옹벽 추가] = 부지와 같은 로직(수직 · 다단 · 고리 면 P0) ==");
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var V = WallByLine.WallShape.Vertical; var T = WallByLine.WallShape.Tiered;
        // 선 — U자(양 끝은 데이라잇 밖 · 가운데 곧은 몫은 사면 속) · 왼쪽(+y) 사면
        RoadGrade.LineWall U(double y, double yOut) => RoadWallCheck.W(1, 1, 1, (28, yOut), (40, y), (80, y), (92, yOut));

        // (a) 곧은 길 5 % · 절토 6 m · 수직 — 선 y = 9(사면 가운데)
        {
            var p = Ring(RoadWallCheck.Straight(0.05, (x, y, zc) => zc + 6.0), Check, " (a)");
            if (p != null)
            {
                var ws = Apply(p, W(1, U(9, 19), V)); var st = ws.Walls[0];
                Check(" (a) 절토 수직 — 지어짐", st.Ok && st.Built != null, Txt(st));
                if (st.Ok) Measure(" (a)", p, ws, st, true, 2, Check);
                // (a′) 다단 기본(Enter)인데 1차 최고 ≤ 단높이 → 수직 그대로(부지와 같음 · 비트 같음)
                var wt = Apply(p, W(1, U(9, 19), T)); var s2 = wt.Walls[0];
                bool same = s2.Ok && st.Ok && s2.Built!.Shape == 2 && s2.Built.TierNone && wt.Tris.Count == ws.Tris.Count && wt.Tris.Zip(ws.Tris).All(z => z.First.Equals(z.Second));
                Check(" (a′) 다단 기본 · 최고 ≤ 단높이 → 수직 결과 비트 같음", same, Txt(s2));
                // (a″) 옛 엔진 P0(선 면 · 선 따라 TIN)에서도 같은 Core — 지어지거나 까닭 한 줄(예외 없음)
                var old = RoadWallCheck.Make(p.Inp);
                var wo = RoadWallCheck.Apply(old, new[] { W(1, U(9, 19), V) }).Walls[0];
                Check(" (a″) 옛 엔진 P0 — 지어지거나 까닭(예외 없음)", wo.Code != "예외", $"{old.Path} · {Txt(wo)}");
            }
        }
        // (b) 곧은 길 8 % · 성토 6 m(땅 굽이침) · 수직 — S239 ①과 같은 선
        {
            static double Wave(double x, double y) => 0.15 * Math.Sin(x / 7.3) * Math.Cos(y / 5.1);
            var p = Ring(RoadWallCheck.Straight(0.08, (x, y, zc) => zc - 6.0 + Wave(x, y)), Check, " (b)");
            if (p != null)
            {
                var lw = RoadWallCheck.W(1, 1, 1, (28, 19), (40, 9.0), (80, 12.5), (92, 19));
                var ws = Apply(p, W(1, lw, V)); var st = ws.Walls[0];
                Check(" (b) 성토 수직 — 지어짐", st.Ok && st.Built != null, Txt(st));
                if (st.Ok) Measure(" (b)", p, ws, st, false, 2, Check);
                // (f) 옛 모양(Out) + 수직 함께 — 옛 벽 세모 · 상태가 옛 벽 혼자일 때와 같음(다른 자리)
                var lwOld = RoadWallCheck.W(1, 1, 1, (8, 19), (14, 9.0), (24, 9.0), (30, 19));
                var lwV = W(2, RoadWallCheck.W(2, 1, 1, (54, 19), (62, 9.0), (100, 9.0), (108, 19)), V);
                var w1 = Apply(p, lwOld); var w2 = Apply(p, lwOld, lwV);
                bool oldSame = w1.Walls[0].Ok == w2.Walls[0].Ok && w1.Walls[0].Text == w2.Walls[0].Text && w1.Walls[0].ZoneTris.Count == w2.Walls[0].ZoneTris.Count && w1.Walls[0].ZoneTris.Zip(w2.Walls[0].ZoneTris).All(z => z.First.Equals(z.Second));
                Check(" (f) 옛 모양 + 수직 함께 — 옛 벽 세모 · 글 그대로 · 수직 지어짐", oldSame && w2.Walls[1].Ok, $"옛 {Txt(w2.Walls[0])} · 수직 {Txt(w2.Walls[1])}");
                // (f′) 벽마다 규칙 — 옛 벽 곁 노리선 · 옹벽선은 옛 벽 혼자일 때와 같음(가운데 규칙 · Top)
                var tk = new List<(Point3 A, Point3 B)>();
                for (double x = 12; x <= 26; x += 1) { tk.Add((new Point3(OX + x, OY + 4, 100), new Point3(OX + x, OY + 20, 100))); tk.Add((new Point3(OX + x, OY + 9.5, 100), new Point3(OX + x, OY + 11, 100))); }
                var t1 = RoadGrade.CutTicks(tk, w1); var t2 = RoadGrade.CutTicks(tk, w2);
                var l1 = w1.Lines.Where(l => l.WallNo == 1).SelectMany(l => l.Pts).ToList(); var l2 = w2.Lines.Where(l => l.WallNo == 1).SelectMany(l => l.Pts).ToList();
                Check(" (f′) 옛 벽 노리선 · 옹벽선 = 옛 벽 혼자일 때(벽마다 규칙)", t1.Count == t2.Count && t1.Zip(t2).All(z => z.First.Equals(z.Second)) && l1.Count == l2.Count && l1.Zip(l2).All(z => z.First.Equals(z.Second)) && l1.Count > 0,
                    $"빗금 {t1.Count}/{t2.Count}(넣은 {tk.Count}) · 옹벽선 점 {l1.Count}/{l2.Count}");
                if (w2.Walls[1].Ok) Measure(" (f)", p, w2, w2.Walls[1], false, 2, Check);
            }
        }
        // (c) 곧은 길 5 % · 절토 12 m(사면 소단 둘) · 다단 — 선 y = 13(1차 최고 > 5 m → 2단 이상)
        {
            var p = Ring(RoadWallCheck.Straight(0.05, (x, y, zc) => zc + 12.0), Check, " (c)");
            if (p != null)
            {
                var ws = Apply(p, W(1, U(13, 30), T)); var st = ws.Walls[0];
                Check(" (c) 절토 다단 — 지어짐", st.Ok && st.Built != null, Txt(st));
                if (st.Ok) Measure(" (c)", p, ws, st, true, 3, Check);
                // (c′) 수직만(V) — 같은 선 · 최고 > 5 m 수직
                var wv = Apply(p, W(1, U(13, 30), V)); var sv = wv.Walls[0];
                Check(" (c′) 같은 선 수직만 — 지어짐 · 최고 > 단높이", sv.Ok && sv.Built!.Shape == 2 && sv.Built.HMax > 5, Txt(sv));
                // (c″) 기록 길(도면 기록 칸 → RoadGrade.LineWallOf → ApplyWalls) — 첫 소단 자동(null · 다시 셈) · 손(자동 값을 손으로) 둘 다 처음과 비트 같음
                if (st.Ok)
                {
                    var b = st.Built!; var u = U(13, 30);
                    foreach (var (nm, z1) in new[] { ("자동", (double?)null), ("손", (double?)b.TierZ1) })
                    {
                        var rw = RoadGrade.LineWallOf(1, u.SrcX, u.SrcY, u.Tc, u.Tf, u.Reach, false, 0, 3, b.TierH, b.TierB, z1);
                        var wr = rw == null ? null : Apply(p, rw); var sr = wr?.Walls[0];
                        bool same = wr != null && sr!.Ok && sr.Built!.Shape == 3 && wr.Tris.Count == ws.Tris.Count && wr.Tris.Zip(ws.Tris).All(z => z.First.Equals(z.Second));
                        Check($" (c″) 기록 길(LineWallOf 모양 3 · 첫 소단 {nm})로 다시 짓기 = 처음과 비트 같음", same, sr == null ? "LineWallOf null" : Txt(sr));
                    }
                }
                // (c‴) 다단 소단 자리가 막을 다각형(앞 벽 · 구멍 · 부지 계획선)과 겹침 → «겹침»(수직 띠는 안 닿게 — 첫 소단 자리 안 네모)
                if (st.Ok)
                {
                    var blk = new List<(string, IReadOnlyList<Point3>)> { ("부지 계획선 1", new List<Point3> { new(OX + 55, OY + 12.0, 0), new(OX + 65, OY + 12.0, 0), new(OX + 65, OY + 12.6, 0), new(OX + 55, OY + 12.6, 0) }) };
                    var so = RoadGrade.ApplyWalls(p.Inp, p.R, p.B.Tris, p.B.Lines, "고리 면", new[] { W(1, U(13, 30), T) }, blk, ring: p.B).Walls[0];
                    var sv2 = RoadGrade.ApplyWalls(p.Inp, p.R, p.B.Tris, p.B.Lines, "고리 면", new[] { W(1, U(13, 30), V) }, blk, ring: p.B).Walls[0];
                    Check(" (c‴) 다단 소단 자리 ∩ 막을 다각형 → «겹침» · 같은 선 수직만은 지어짐", so.Code == "겹침" && sv2.Ok, $"다단 {Txt(so)} · 수직 {Txt(sv2)}");
                }
            }
        }
        // (d) 곧은 길 · 성토 11~12 m · 다단 — 선 y = 14(평평한 1 m 격자 땅 곧은 길은 고리 면이 바늘을 못 고칠 수 있다 — S239 ⓪ 알려진 한계 · 지어지는 꼴을 차례로)
        {
            RP? p = null; string used = "";
            foreach (var (gr, dz) in new[] { (0.05, -12.0), (0.06, -11.5), (0.04, -11.0), (0.07, -12.0) })
            {
                var b0 = RoadRing.Build(RoadWallCheck.Straight(gr, (x, y, zc) => zc + dz + 0.15 * Math.Sin(x / 7.3) * Math.Cos(y / 5.1)));
                if (b0.Ok && b0.Tris.Count > 0) { var i0 = RoadWallCheck.Straight(gr, (x, y, zc) => zc + dz + 0.15 * Math.Sin(x / 7.3) * Math.Cos(y / 5.1)); p = new RP { Inp = i0, B = b0, R = RoadRing.ToResult(b0, i0) }; used = $"경사 {gr} · 성토 {-dz}"; break; }
                Console.WriteLine($"INFO  S262 RV (d) 경사 {gr} · 성토 {-dz} 고리 면 못 지음 — {(b0.Note.Length > 100 ? b0.Note[..100] : b0.Note)}");
            }
            Check(" (d) 고리 면 지어짐", p != null, used);
            if (p != null)
            {
                var ws = Apply(p, W(1, U(14, 32), T)); var st = ws.Walls[0];
                Check(" (d) 성토 다단 — 지어짐", st.Ok && st.Built != null, Txt(st));
                if (st.Ok) Measure(" (d)", p, ws, st, false, 3, Check);
            }
        }
        // (e) 땅이 길 따라 기울어 왼쪽 사면이 절토 → 성토로 바뀌는 길 — 그 자리를 가로지르는 선(부지와 같은 거절 규칙: 단이 필요한 섞인 선 = «섞인 선» · 사면 폭 0 자리 = 판정)
        {
            var p = Ring(RoadWallCheck.Straight(0.0, (x, y, zc) => zc + 8.0 - 0.2 * (x - 20)), Check, " (e)");
            if (p != null)
            {
                foreach (var (nm, sh) in new[] { ("다단", T), ("수직", V) })
                {
                    var st = Apply(p, W(1, RoadWallCheck.W(1, 1, 1, (18, 30), (25, 8), (75, 8), (82, 30)), sh)).Walls[0];
                    Console.WriteLine($"INFO  S262 RV (e) 절 → 성 가로지르는 선({nm}) — {Txt(st)}");
                    Check($" (e) 절 → 성 가로지르는 선({nm}) — 짓거나 알기 쉬운 까닭(예외 없음 · 섞여 지으면 선 밖 0)", st.Code != "예외" && (!st.Ok || st.Built!.OutsideN == 0), Txt(st));
                }
            }
        }
        // (g) 지문 — 기본값(옛 모양) LineWall JSON = 옛 판 칸(No · SrcX · SrcY · Tc · Tf · Reach) 바이트 그대로 · 새 모양만 칸이 늘어남
        {
            var opt = new System.Text.Json.JsonSerializerOptions { IncludeFields = true };
            var o = new RoadGrade.LineWall(3, new[] { 1.5, 2.25 }, new[] { 3.0, 4.0 }, 1, 0.5, 40);
            string now = System.Text.Json.JsonSerializer.Serialize(new[] { o }, opt);
            string was = System.Text.Json.JsonSerializer.Serialize(new[] { new { No = 3, SrcX = new[] { 1.5, 2.25 }, SrcY = new[] { 3.0, 4.0 }, Tc = 1.0, Tf = 0.5, Reach = 40.0 } }, opt);
            string vj = System.Text.Json.JsonSerializer.Serialize(new[] { o with { Shape = V } }, opt);
            string tj = System.Text.Json.JsonSerializer.Serialize(new[] { o with { Shape = T, TierH = 5, TierB = 1, TierZ1 = 111.2 } }, opt);
            Check(" (g) 지문 — 옛 모양 JSON 바이트 같음 · 수직 · 다단은 다름", now == was && vj != now && vj.Contains("\"Shape\":2") && tj.Contains("\"TierZ1\":111.2") && !now.Contains("Shape") && !now.Contains("SiteLike"),
                $"옛 {now} · 수직 {vj} · 다단 {tj}");
            // (g′) 기록 → Core(LineWallOf) · 해시 꼬리(ShapeSig) — 모양 0 = 옛 셈 그대로(천단 계단 포함) · 2 · 3 · 모르는 모양 · 숫자 아닌 값 = null(쉼)
            var x = new[] { 1.5, 2.25 }; var y = new[] { 3.0, 4.0 };
            var r0 = RoadGrade.LineWallOf(3, x, y, 1, 0.5, 40, false, 0, 0, 0, 0, null); var r0s = RoadGrade.LineWallOf(3, x, y, 1, 0.5, 40, true, 0, 0, 0, 0, null);
            var r2 = RoadGrade.LineWallOf(3, x, y, 1, 0.5, 40, true, 4, 2, 0, 0, null); var r3 = RoadGrade.LineWallOf(3, x, y, 1, 0.5, 40, false, 0, 3, 5, 1, null);
            bool okMap = r0 == new RoadGrade.LineWall(3, x, y, 1, 0.5, 40, false, 0) && r0s == new RoadGrade.LineWall(3, x, y, 1, 0.5, 40, true, 9)
                && r2 is { Shape: WallByLine.WallShape.Vertical, TopStep: false, BlockLen: 0, TierH: null } && r3 is { Shape: WallByLine.WallShape.Tiered, TierH: 5, TierB: 1, TierZ1: null }
                && RoadGrade.LineWallOf(3, x, y, 1, 0.5, 40, false, 0, 1, 0, 0, null) == null && RoadGrade.LineWallOf(3, x, y, 1, 0.5, 40, false, 0, 7, 0, 0, null) == null
                && RoadGrade.LineWallOf(3, x, y, 1, 0.5, 40, false, 0, 3, double.NaN, 1, null) == null && RoadGrade.LineWallOf(3, x, y, 1, 0.5, 40, false, 0, 3, 5, 1, double.NaN) == null;
            string s0 = RoadGrade.ShapeSig(0, 5, 1, 2), s2 = RoadGrade.ShapeSig(2, 0, 0, null), s3a = RoadGrade.ShapeSig(3, 5, 1, null), s3h = RoadGrade.ShapeSig(3, 5, 1, 111.2);
            Check(" (g′) 기록 → Core 모양 · 해시 꼬리(모양 0 = 빈 글 · 자동 ≠ 손)", okMap && s0 == "" && s2 == "|모양2" && s3a.EndsWith("자동") && s3h.Contains("손") && s3a != s3h, $"{s2} · {s3a} · {s3h}");
        }
        Console.WriteLine($"   S262 {sw.ElapsedMilliseconds} ms");
    }
}
