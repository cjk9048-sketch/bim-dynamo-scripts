// ── S160 ★★★[v103.1.14 · JACK 1014 ② «맞닿은 옹벽은 단높이·소단이 같을 때만 늘려 한 옹벽으로 · 번호 그대로 · 양쪽이 닿으면 셋을 하나로» · 1019 «새 쪽 끝 한 점만 · 머리는 이어진 전체로»] 이어 짓기 ──
//   판정(WallPlace.ExtendPlan) · 측선 방향 되살리기(WallInPoly.SideDirs) · 이은 폴리곤(AssembleManual 그대로) · 겹침 판정(WallList.Conflict + 이어 지을 옹벽) ·
//   목록 판 3(이어 짓기가 걸려 있을 때만) · 번호 물려받기(Renumber) · 합성 차례(ComposeKey) · 바꿈 따라가기(ReplaceNo2) · 합성 실패 열쇠(새 칸 0이면 종전 그대로).
using DH.Grading.Core;

static class ExtendCheck
{
    public static void Run(Action<string, bool, string> check0)
    {
        void check(string name, bool ok, string detail = "") => check0(name, ok, detail);
        Console.WriteLine("\n== S160 이어 짓기 — 판정 · 측선 방향 · 이은 폴리곤 · 겹침 판정 · 목록 판 3 · 번호 물려받기 ==");
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        const double Z = 100, MG = 10;
        // 자 = 절토 1단 링(반시계 100×40 · z 100) — 아랫변 T = x · 오른변 100..140 · 윗변 140..240 · 왼변 240..280
        var ruler = new List<Point3> { new(0, 0, Z), new(100, 0, Z), new(100, 40, Z), new(0, 40, Z) };
        var cum = GradingGeometry.CumLen2D(ruler);
        double tot = cum[^1];
        var day = new List<Point3> { new(-20, -20, Z), new(120, -20, Z), new(120, 60, Z), new(-20, 60, Z) };   // 데이라잇(바깥 20m)
        int ow = GradingGeometry.OutwardSideOfTravel(ruler);
        (double X, double Y) U(double x, double y) { double l = Math.Sqrt(x * x + y * y); return (x / l, y / l); }
        static double Dd((double X, double Y) a, (double X, double Y) b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
        // ZoneEditCommon → WallPolyDraw.Run → AssembleManual과 같은 차례로 손 폴리곤을 짓는다(찍은 방향 = dA(end1 · 시점) · dB(end0 · 종점))
        (List<Point3> Poly, List<bool> Flags, bool Ray, List<Point3> Seg) Build(double t0, double t1, (double X, double Y) dA, (double X, double Y) dB)
        {
            var seg = WallInPoly.SegmentPoints(ruler, cum, t0, t1);
            var nA = WallInPoly.FenceNormal(ruler, cum, t1); var nB = WallInPoly.FenceNormal(ruler, cum, t0);
            GradingGeometry.RayRingHitOutward(day, seg[^1].X, seg[^1].Y, dA.X, dA.Y, nA.X, nA.Y, out double ax, out double ay, out _, 0.05);
            GradingGeometry.RayRingHitOutward(day, seg[0].X, seg[0].Y, dB.X, dB.Y, nB.X, nB.Y, out double bx, out double by, out _, 0.05);
            var ring = WallInPoly.AssembleManual(seg, dA, dB, ow, new Point3(ax, ay, Z), new Point3(bx, by, Z), day, MG, 1.0, Z, out var flags, out bool ray, out _) ?? new List<Point3>();
            return (ring, flags, ray, seg);
        }
        WallRec Wall(int no, double t0, double t1, (double X, double Y) dA, (double X, double Y) dB, double h = 5, double t = 1, string hold = "", string note = "판정 1(깨끗)", int bench = 1)
        {
            var b = Build(t0, t1, dA, dB);
            return new WallRec { No = no, State = 1, Up = true, Bench = bench, H = h, T = t, T0 = t0, T1 = t1, HeadZ = WallInPoly.HeadZ(ruler, cum, t0, GradingGeometry.SpanOf(t0, t1, tot), true),
                                 Seg = b.Seg, Poly = b.Poly, IsWall = b.Flags, Hold = hold, LastNote = note };
        }
        var dA1 = U(0.3, -1); var dB1 = U(-0.25, -1);
        var A = Wall(1, 20, 40, dA1, dB1);
        var B = Wall(2, 60, 80, U(0.2, -1), U(-0.1, -1));
        check("S160 준비 — 옹벽 1 · 2 폴리곤이 찍은 방향 그대로 지어졌다", A.Poly.Count > 10 && B.Poly.Count > 10 && A.IsWall.Count == A.Poly.Count, $"점 {A.Poly.Count} · {B.Poly.Count}");

        // ② 측선 방향 되살리기 — 지은 폴리곤에서 찍은 방향 그대로
        var sdA = WallInPoly.SideDirs(A.Poly, A.IsWall, A.Seg, out string wA);
        check("S160 측선 방향 — 옹벽 1에서 시점(end1) · 종점(end0) 방향을 그대로 되살림(1e-9)", sdA != null && Dd(sdA.Value.AtEnd1, dA1) < 1e-9 && Dd(sdA.Value.AtEnd0, dB1) < 1e-9,
            sdA == null ? wA : string.Format(ci, "{0:F12},{1:F12} · {2:F12},{3:F12}", sdA.Value.AtEnd1.X, sdA.Value.AtEnd1.Y, sdA.Value.AtEnd0.X, sdA.Value.AtEnd0.Y));
        {
            var bad = new List<bool>(A.IsWall); int mid = bad.IndexOf(false) + 1; if (mid > 0 && mid < bad.Count && !bad[mid]) bad[mid] = true;   // 두른 선 가운데를 옹벽으로 — 수직 덩어리가 둘
            var sdBad = WallInPoly.SideDirs(A.Poly, bad, A.Seg, out string wBad);
            check("S160 측선 방향 — 꼴이 다르면(수직 덩어리 둘) 못 함(null + 까닭)", sdBad == null && wBad.Contains("덩어리"), wBad);
            var sdNoSeg = WallInPoly.SideDirs(A.Poly, A.IsWall, B.Seg, out string wNs);
            check("S160 측선 방향 — 다른 옹벽의 안쪽 변이면 못 함", sdNoSeg == null, wNs);
        }
        // 물러난 길(측선이 테두리 모서리로)로 지은 폴리곤도 측선이 곧은 선 — 되살린 방향이 실제 측선 방향
        {
            var bIn = Build(20, 40, U(-1, -0.2), U(1, -0.2));   // 두 측선이 엇갈린다 — 제 몸을 질러 물러난다
            if (bIn.Poly.Count > 0 && !bIn.Ray)
            {
                var sdF = WallInPoly.SideDirs(bIn.Poly, bIn.Flags, bIn.Seg, out string wF);
                check("S160 측선 방향 — 물러난 길로 지은 폴리곤에서도 되살림(측선 = 곧은 선)", sdF != null, wF);
            }
            else Console.WriteLine($"      (물러난 길을 못 만들었다 — 링 {bIn.Poly.Count} · 찍은 방향 {bIn.Ray})");
        }

        // ① 판정
        List<WallRec> L(params WallRec[] w) => w.ToList();
        var e1 = WallPlace.ExtendPlan(ruler, cum, 40, 60, true, 1, L(A), 5, 1);
        check("S160 판정 — 옹벽 1 끝에 새 구간 [40..60]이 붙음 → 이어 짓기 · 구간 [20..60] · 번호 1 · 종점 쪽 측선 = 옹벽 1의 종점 측선 · 시점은 찍기",
            e1.Kind == WallPlace.ExtendKind.Extend && string.Join(",", e1.Nos) == "1" && e1.KeepNo == 1 && Math.Abs(e1.T0 - 20) < 1e-6 && Math.Abs(e1.T1 - 60) < 1e-6
            && e1.FixB != null && Dd(e1.FixB.Value, dB1) < 1e-9 && e1.FixA == null, $"{e1.Kind} {e1.Why} {e1.Note}");
        var e2 = WallPlace.ExtendPlan(ruler, cum, 5, 20, true, 1, L(A), 5, 1);
        check("S160 판정 — 새 구간 [5..20] 끝이 옹벽 1 첫 끝에 붙음 → 구간 [5..40] · 시점 쪽 측선 = 옹벽 1의 시점 측선 · 종점은 찍기",
            e2.Kind == WallPlace.ExtendKind.Extend && Math.Abs(e2.T0 - 5) < 1e-6 && Math.Abs(e2.T1 - 40) < 1e-6 && e2.FixA != null && Dd(e2.FixA.Value, dA1) < 1e-9 && e2.FixB == null, $"{e2.Kind} {e2.Why} {e2.Note}");
        var e3 = WallPlace.ExtendPlan(ruler, cum, 40, 60, true, 1, L(B, A), 5, 1);
        check("S160 판정 — 옹벽 1과 2 사이 [40..60] → 셋을 하나로 · 앞 1 · 뒤 2 · 번호 1(작은 것) · 구간 [20..80] · 찍을 끝 없음",
            e3.Kind == WallPlace.ExtendKind.Extend && string.Join(",", e3.Nos) == "1,2" && e3.KeepNo == 1 && Math.Abs(e3.T0 - 20) < 1e-6 && Math.Abs(e3.T1 - 80) < 1e-6 && e3.FixA != null && e3.FixB != null, $"{e3.Note}");
        var B9 = Wall(9, 60, 80, U(0.2, -1), U(-0.1, -1));
        var A7 = Wall(7, 20, 40, dA1, dB1);
        var e3b = WallPlace.ExtendPlan(ruler, cum, 40, 60, true, 1, L(B9, A7), 5, 1);
        check("S160 판정 — 셋을 하나로의 번호는 작은 것(7 · 9 → 7)", e3b.Kind == WallPlace.ExtendKind.Extend && e3b.KeepNo == 7, $"{e3b.KeepNo}");
        var e4 = WallPlace.ExtendPlan(ruler, cum, 40, 60, true, 1, L(A), 4, 1);
        check("S160 판정 — 단높이가 다르면 멈춤(까닭에 두 값)", e4.Kind == WallPlace.ExtendKind.Stop && e4.Why.Contains("단높이 5m") && e4.Why.Contains("단높이 4m"), e4.Why);
        var e5 = WallPlace.ExtendPlan(ruler, cum, 40, 60, true, 1, L(A), 5, 2);
        check("S160 판정 — 소단이 다르면 멈춤", e5.Kind == WallPlace.ExtendKind.Stop && e5.Why.Contains("소단"), e5.Why);
        var Acf = Wall(1, 20, 40, dA1, dB1, hold: WallList.CompFailTag + "(P0 2A3 · 열쇠 0123456789abcdef) — 앞 옹벽과 겹침");
        var e6 = WallPlace.ExtendPlan(ruler, cum, 40, 60, true, 1, L(Acf), 5, 1);
        check("S160 판정 — «합성 실패» 보류 옹벽에 맞닿으면 멈춤", e6.Kind == WallPlace.ExtendKind.Stop && e6.Why.Contains("보류"), e6.Why);
        var Amv = Wall(1, 20, 40, dA1, dB1, hold: "밑선이 840.0mm 움직였다");
        var e7 = WallPlace.ExtendPlan(ruler, cum, 40, 60, true, 1, L(Amv), 5, 1);
        check("S160 판정 — 움직인 보류 옹벽은 판정에서 뺌(없음 — 종전 길)", e7.Kind == WallPlace.ExtendKind.None, $"{e7.Kind} {e7.Why}");
        var Ab2 = Wall(1, 20, 40, dA1, dB1, bench: 2);
        var e8 = WallPlace.ExtendPlan(ruler, cum, 40, 60, true, 1, L(Ab2), 5, 1);
        var e8b = WallPlace.ExtendPlan(ruler, cum, 40, 60, false, 1, L(A), 5, 1);
        check("S160 판정 — 다른 단 · 다른 방향 옹벽은 없음", e8.Kind == WallPlace.ExtendKind.None && e8b.Kind == WallPlace.ExtendKind.None, $"{e8.Kind} · {e8b.Kind}");
        // 미룬 이어 짓기 옹벽 X(옹벽 1을 [20..45]로 늘린 것 · 물려받을 번호 1)에 또 붙음 → X를 이어 짓기 · 번호 1(옛 옹벽 1은 바뀔 옹벽이라 뺌)
        var X = Wall(5, 20, 45, dA1, dB1, note: ""); X.ReplaceNo = 1; X.KeepNo = 1;
        var e9 = WallPlace.ExtendPlan(ruler, cum, 45, 60, true, 1, L(A, X), 5, 1);
        check("S160 판정 — 미룬 이어 짓기 옹벽 5(물려받을 번호 1)에 또 붙음 → 5를 이어 짓기 · 번호 1 · 옛 옹벽 1은 뺌",
            e9.Kind == WallPlace.ExtendKind.Extend && string.Join(",", e9.Nos) == "5" && e9.KeepNo == 1 && Math.Abs(e9.T0 - 20) < 1e-6, $"{e9.Kind} {e9.Why} {e9.Note}");
        // 같은 선 겹침(바꿈)은 이어 짓기가 아니다 — 첫 끝이 옹벽 첫 끝과 같아도
        var e10 = WallPlace.ExtendPlan(ruler, cum, 20, 50, true, 1, L(A), 5, 1);
        var e10b = WallPlace.ExtendPlan(ruler, cum, 20, 40, true, 1, L(A), 5, 1);
        check("S160 판정 — 옹벽 위를 덮는 구간(첫 끝이 같음 · 똑같은 구간)은 없음(종전 바꿈 길)", e10.Kind == WallPlace.ExtendKind.None && e10b.Kind == WallPlace.ExtendKind.None, $"{e10.Kind} {e10.Why} · {e10b.Kind}");
        // 한 바퀴 — 옹벽이 [20..270]이고 새 구간이 [270..20](이음매 넘김)이면 두 끝이 다 그 옹벽
        var Big = Wall(3, 20, 270, U(-1, 0.3), dB1);
        var e11 = WallPlace.ExtendPlan(ruler, cum, 270, 20, true, 1, L(Big), 5, 1);
        check("S160 판정 — 두 끝이 같은 옹벽(한 바퀴)이면 멈춤", e11.Kind == WallPlace.ExtendKind.Stop && e11.Why.Contains("한 바퀴"), e11.Why);
        // 자에서 벗어난 옹벽(안쪽 변 5mm 비킴 — 끝은 1cm 안이라 닿음)
        var Aoff = Wall(1, 20, 40, dA1, dB1); Aoff.Seg = Aoff.Seg.Select(q => new Point3(q.X, q.Y - 0.005, q.Z)).ToList();
        var e12 = WallPlace.ExtendPlan(ruler, cum, 40, 60, true, 1, L(Aoff), 5, 1);
        check("S160 판정 — 안쪽 변이 자에서 5mm 벗어난 옹벽이면 멈춤", e12.Kind == WallPlace.ExtendKind.Stop && e12.Why.Contains("벗어났다"), e12.Why);
        // 이음매(T = 0) 넘김 — 왼변 옹벽 [255..275](위→아래) 뒤에 [275..10](모서리 (0,0) 돌아 아랫변으로)
        var Cw = Wall(4, 255, 275, U(-1, -0.2), U(-1, 0.3));
        var e13 = WallPlace.ExtendPlan(ruler, cum, 275, 10, true, 1, L(Cw), 5, 1);
        check("S160 판정 — 이음매를 넘는 구간 [275..10]이 옹벽 [255..275] 뒤에 붙음 → 구간 [255..10] 35m",
            e13.Kind == WallPlace.ExtendKind.Extend && Math.Abs(e13.T0 - 255) < 1e-6 && Math.Abs(e13.T1 - 10) < 1e-6 && Math.Abs(GradingGeometry.SpanOf(e13.T0, e13.T1, tot) - 35) < 1e-6, $"{e13.Kind} {e13.Why} {e13.Note}");
        // 이으면 한 바퀴(옹벽 1 [20..40] + 6 [45..20](이음매 넘어 옹벽 1 첫 끝까지) 사이 [40..45])
        var Long = Wall(6, 45, 20, U(-0.2, -1), U(0.2, -1));
        var e14 = WallPlace.ExtendPlan(ruler, cum, 40, 45, true, 1, L(A, Long), 5, 1);
        check("S160 판정 — 이으면 한 바퀴면 멈춤", e14.Kind == WallPlace.ExtendKind.Stop && e14.Why.Contains("한 바퀴"), $"{e14.Kind} {e14.Why}");
        var e15 = WallPlace.ExtendPlan(ruler, cum, 60, 75, true, 1, L(A), 5, 1);
        check("S160 판정 — 떨어진 구간은 없음", e15.Kind == WallPlace.ExtendKind.None, $"{e15.Kind}");
        // ★[계획 검토 낮음 10] 미룬 옹벽 Y(옹벽 1을 [25..35]로 바꿈)가 있으면 옛 옹벽 1 끝에 맞닿은 구간은 그리기 전에 멈춤
        var Y = Wall(5, 25, 35, U(0, -1), U(0, -1), note: ""); Y.ReplaceNo = 1;
        var e16 = WallPlace.ExtendPlan(ruler, cum, 40, 60, true, 1, L(A, Y), 5, 1);
        check("S160 판정 — 미룬 옹벽이 바꿀 옛 옹벽 끝에만 맞닿으면 멈춤(폴리곤 그리기 전)", e16.Kind == WallPlace.ExtendKind.Stop && e16.Why.Contains("미룬 옹벽이 바꿀"), e16.Why);
        // ★[계획 검토 중간 1] 맞닿은 옹벽 쪽 측선을 못 되살리면 찍기로 물러나지 않고 멈춤
        var Abad = Wall(1, 20, 40, dA1, dB1); { int k = Abad.IsWall.IndexOf(false) + 1; if (k > 0 && k < Abad.IsWall.Count) Abad.IsWall[k] = true; }
        var e17 = WallPlace.ExtendPlan(ruler, cum, 40, 60, true, 1, L(Abad), 5, 1);
        check("S160 판정 — 맞닿은 옹벽 쪽 측선을 지은 그대로 못 되살리면 멈춤", e17.Kind == WallPlace.ExtendKind.Stop && e17.Why.Contains("지은 그대로"), e17.Why);
        // ★[계획 검토 낮음 6] 합성에 든 옹벽에 남은 지난 KeepNo는 안 본다(미룬 옹벽의 것만)
        var Astale = Wall(1, 20, 40, dA1, dB1); Astale.KeepNo = 9;
        var e18 = WallPlace.ExtendPlan(ruler, cum, 40, 60, true, 1, L(Astale), 5, 1);
        check("S160 판정 — 합성에 든 옹벽의 지난 KeepNo는 안 보고 제 번호", e18.Kind == WallPlace.ExtendKind.Extend && e18.KeepNo == 1, $"{e18.KeepNo}");
        // ★[계획 검토 낮음 8] 앞면 값 — 맞닿은 옹벽이 지은 값을 쓴다 · 두 옹벽이 다르면 멈춤
        var Af = Wall(1, 20, 40, dA1, dB1); Af.SlopeW = 0.01; Af.FaceRun = 0.005; Af.Grid = 2;
        var Bf = Wall(2, 60, 80, U(0.2, -1), U(-0.1, -1)); Bf.SlopeW = 0.01; Bf.FaceRun = 0.005; Bf.Grid = 3;
        var e19 = WallPlace.ExtendPlan(ruler, cum, 40, 55, true, 1, L(Af), 5, 1);
        var e20 = WallPlace.ExtendPlan(ruler, cum, 40, 60, true, 1, L(Af, Bf), 5, 1);
        check("S160 판정 — 앞면 값은 맞닿은 옹벽이 지은 값(1:0.01 · 0.005m · 2m) · 두 옹벽이 다르면(간격 2 · 3) 멈춤",
            e19.Kind == WallPlace.ExtendKind.Extend && e19.SlopeW == 0.01 && e19.FaceRun == 0.005 && e19.Grid == 2 && e20.Kind == WallPlace.ExtendKind.Stop && e20.Why.Contains("앞면 값"), $"{e19.SlopeW} · {e20.Why}");

        // ③ 이은 폴리곤 — 새 쪽(시점)만 찍고 옹벽 1 쪽(종점)은 되살린 방향 그대로
        var dN = U(0.2, -1);
        var M = Build(e1.T0, e1.T1, dN, e1.FixB!.Value);
        check("S160 이은 폴리곤 — 찍은 방향 그대로 · 제 몸 안 지름 · 넓이가 옹벽 1보다 큼", M.Ray && M.Poly.Count > 0 && GradingGeometry.RingIsSimple(M.Poly) && GradingGeometry.RingAreaNts(M.Poly) > GradingGeometry.RingAreaNts(A.Poly) + 100,
            string.Format(ci, "{0:F1}㎡ · 옹벽 1 {1:F1}㎡", M.Poly.Count > 0 ? GradingGeometry.RingAreaNts(M.Poly) : 0, GradingGeometry.RingAreaNts(A.Poly)));
        {
            // 옹벽 1의 종점 측선 점(두른 선 뒤 ~ 안쪽 변 첫 점)과 이은 폴리곤의 종점 측선 점이 비트까지 같다(같은 데이라잇)
            List<Point3> SideB(List<Point3> r, List<bool> f) { int n = r.Count, last = -1; for (int i = 0; i < n; i++) if (!f[i]) last = i; var o = new List<Point3>(); for (int i = last + 1; i < n; i++) o.Add(r[i]); return o; }
            var sA = SideB(A.Poly, A.IsWall); var sM = SideB(M.Poly, M.Flags);
            bool same = sA.Count == sM.Count && sA.Zip(sM).All(p => p.First.X == p.Second.X && p.First.Y == p.Second.Y);
            check("S160 이은 폴리곤 — 옹벽 1 쪽 측선 점이 옹벽 1 폴리곤의 그 측선 점과 비트까지 같다", same, $"{sA.Count} · {sM.Count}");
            // 맞닿던 자리(x = 40)에 측벽이 없다 — 옹벽 점 중 안쪽 변(y = 0) 밖에 있는 것은 두 끝 측선 위(x ≈ 20 · 60 곁)뿐
            int junk = 0;
            for (int i = 0; i < M.Poly.Count; i++) if (M.Flags[i] && Math.Abs(M.Poly[i].Y) > 1e-6 && Math.Abs(M.Poly[i].X - 40) < 5) junk++;
            check("S160 이은 폴리곤 — 맞닿던 자리(x = 40 곁)에 측벽 점이 없다(한 벽)", junk == 0, $"{junk}");
        }

        // ④ 겹침 판정 + 이어 지을 옹벽
        var c1 = WallList.Conflict(M.Poly, M.Seg, true, 1, L(A), new[] { 1 });
        check("S160 겹침 판정 — 이어 지을 옹벽 1 → 바꿀 번호 1 · 둘째 0 · 멈춤 없음", c1.Stop.Length == 0 && c1.ReplaceNo == 1 && c1.ReplaceNo2 == 0, $"{c1.Stop} {c1.Note}");
        var c0 = WallList.Conflict(M.Poly, M.Seg, true, 1, L(A));
        check("S160 겹침 판정 — 이어 지을 옹벽 없이 부르면 종전(같은 선 겹침 = 바꿈 1)", c0.Stop.Length == 0 && c0.ReplaceNo == 1, $"{c0.Stop}");
        var M3 = Build(e3.T0, e3.T1, e3.FixA!.Value, e3.FixB!.Value);
        var c2 = WallList.Conflict(M3.Poly, M3.Seg, true, 1, L(A, B), new[] { 1, 2 });
        check("S160 겹침 판정 — 셋을 하나로 → 바꿀 번호 1 · 둘째 2", c2.Stop.Length == 0 && c2.ReplaceNo == 1 && c2.ReplaceNo2 == 2, $"{c2.Stop}");
        var c2o = WallList.Conflict(M3.Poly, M3.Seg, true, 1, L(A, B));
        check("S160 겹침 판정 — 셋을 하나로를 이어 지을 옹벽 없이 부르면 종전처럼 «함께 걸친다» 멈춤", c2o.Stop.Contains("함께 걸친다"), c2o.Stop);
        var c3 = WallList.Conflict(M.Poly, M.Seg, true, 1, L(A, B), new[] { 2 });
        check("S160 겹침 판정 — 이어 지을 옹벽 2가 같은 선에서 안 잡히면 멈춤(1은 더 걸침)", c3.Stop.Length > 0 && (c3.Stop.Contains("안 잡혔다") || c3.Stop.Contains("밖의")), c3.Stop);
        var c4 = WallList.Conflict(M3.Poly, M3.Seg, true, 1, L(A, B), new[] { 1 });
        check("S160 겹침 판정 — 이은 구간이 이어 지을 옹벽 밖의 같은 선 옹벽 2에도 걸치면 멈춤", c4.Stop.Contains("밖의 같은 선 옹벽 2"), c4.Stop);
        var Other = Wall(8, 40, 60, U(0, -1), U(0, -1), bench: 2);   // 다른 단 — 폴리곤이 이은 폴리곤과 겹친다
        var c5 = WallList.Conflict(M.Poly, M.Seg, true, 1, L(A, Other), new[] { 1 });
        check("S160 겹침 판정 — 다른 단 옹벽과 겹치면 이어 짓기여도 멈춤", c5.Stop.Contains("옹벽 8"), c5.Stop);

        // ⑤ 목록 판 3 — 이어 짓기가 걸려 있을 때만
        var head = new WallListHead { PlanHandle = "2A0", GroundHandle = "2A1", NextNo = 6, VerifiedP0 = "2B0" };
        var plain = L(Wall(1, 20, 40, dA1, dB1), Wall(2, 60, 80, U(0.2, -1), U(-0.1, -1)));
        var v2 = WallList.Encode(head, plain);
        bool okD2 = WallList.TryDecode(v2, out _, out var back2, out string why2);
        check("S160 목록 — 이어 짓기 칸이 없으면 판 2로 쓴다 · 되읽기 · 새 칸 0", (int)v2[1].Value == 2 && okD2 && back2.All(w => w.ReplaceNo2 == 0 && w.KeepNo == 0), why2);
        int perWall2 = (v2.Count - 7) / 2;
        var ext = L(Wall(1, 20, 40, dA1, dB1), Wall(5, 20, 80, dN, dB1)); ext[1].State = 0; ext[1].ReplaceNo = 1; ext[1].ReplaceNo2 = 2; ext[1].KeepNo = 1;
        var v3 = WallList.Encode(head, ext);
        bool okD3 = WallList.TryDecode(v3, out var h3, out var back3, out string why3);
        check("S160 목록 — 이어 짓기가 걸리면 판 3 · 되읽으면 ReplaceNo2 2 · KeepNo 1 · 머리 그대로", (int)v3[1].Value == 3 && okD3 && back3[1].ReplaceNo2 == 2 && back3[1].KeepNo == 1 && back3[1].ReplaceNo == 1 && back3[0].KeepNo == 0 && h3.NextNo == 6, why3);
        {
            // 판 2 목록의 옹벽 하나 몫 값 수 = 판 3보다 둘 적다(같은 옹벽 · 점 수 같을 때) — 칸을 덧붙이기만 했다
            var one2 = WallList.Encode(head, L(Wall(1, 20, 40, dA1, dB1)));
            var oneE = Wall(1, 20, 40, dA1, dB1); oneE.KeepNo = 3;
            var one3 = WallList.Encode(head, L(oneE));
            check("S160 목록 — 판 3은 판 2 끝에 값 둘(ReplaceNo2 · KeepNo)만 덧붙인다", one3.Count == one2.Count + 2 && one3.Take(one2.Count).Skip(2).SequenceEqual(one2.Skip(2)), $"{one2.Count} → {one3.Count}");
        }

        // ⑥ 번호 물려받기 · 합성 차례 · 바꿈 따라가기 · 미룬 옹벽이 바꿀 옹벽
        {
            var ws = new List<WallRec> { new() { No = 3, State = 1 }, new() { No = 5, State = 0, KeepNo = 1, ReplaceNo = 1, ReplaceNo2 = 2 }, new() { No = 6, State = 0, ReplaceNo = 5 }, new() { No = 1, State = 1 } };
            bool noYet = WallList.Renumber(ws, 5, 1);
            check("S160 번호 물려받기 — 옛 옹벽 1이 목록에 남아 있으면 안 함(아무것도 안 바뀜)", !noYet && ws.Any(w => w.No == 5 && w.KeepNo == 1), "");
            ws.RemoveAll(w => w.No == 1);
            bool did = WallList.Renumber(ws, 5, 1);
            var me = ws.FirstOrDefault(w => w.No == 1);
            check("S160 번호 물려받기 — 5 → 1 · 물려받을 번호 · 바꿈 칸 비움 · 5를 바꾸려던 6은 1을 가리킴", did && me != null && me.KeepNo == 0 && me.ReplaceNo == 0 && me.ReplaceNo2 == 0 && ws.First(w => w.No == 6).ReplaceNo == 1 && !ws.Any(w => w.No == 5), "");
            var ord = new List<WallRec> { new() { No = 3 }, new() { No = 5, KeepNo = 1 }, new() { No = 4 } }.OrderBy(WallList.ComposeKey).ThenBy(w => w.No).Select(w => w.No);
            check("S160 합성 차례 — 이어 지은 옹벽 5(물려받을 1)가 3 · 4보다 앞", string.Join(",", ord) == "5,3,4", string.Join(",", ord));
            var chain = new List<WallRec> { new() { No = 1, State = 1, LastNote = "판정 1" }, new() { No = 2, State = 1, LastNote = "판정 1" },
                                             new() { No = 5, State = 1, LastNote = "", ReplaceNo = 1, ReplaceNo2 = 2, KeepNo = 1 }, new() { No = 7, State = 0, ReplaceNo = 5, KeepNo = 1 } };
            check("S160 바꿈 따라가기 — 새 7 → 미룬 5 → 옛 1 · 2", string.Join(",", WallList.ReplacedBy(chain, new[] { 7 }).OrderBy(n => n)) == "1,2,5", string.Join(",", WallList.ReplacedBy(chain, new[] { 7 })));
            check("S160 미룬 옹벽이 바꿀 옹벽 — 미룬 5의 둘째 바꿈 2도 듦", string.Join(",", WallList.GoingAway(chain).OrderBy(n => n)) == "1,2", string.Join(",", WallList.GoingAway(chain)));
        }
        // ⑦ 합성 실패 열쇠 — 새 칸이 0인 목록은 종전 식과 같다(지난 «합성 실패» 보류를 판 올림 탓에 다시 해 보지 않게)
        {
            var ws = new List<WallRec> { new() { No = 1, State = 1, WallHandle = "A1", P0Handle = "P", LastNote = "판정 1" }, new() { No = 2, State = 1, WallHandle = "A2", P0Handle = "P", LastNote = "", Hold = WallList.CompFailTag + "(P0 P · 열쇠 x) — y", ReplaceNo = 1 } };
            ulong h = 1469598103934665603UL;
            void Add(string s) { foreach (char c in s) { h ^= c; h *= 1099511628211UL; } h ^= 0x1F; h *= 1099511628211UL; }
            Add("P"); Add("1:1:n:P:A1:판정 1:0"); Add("self:2:A2:P:1");
            string old = h.ToString("x16");
            check("S160 합성 실패 열쇠 — 새 칸 0이면 종전 식 그대로", WallList.CompFailKey(ws, 2, "P") == old, $"{WallList.CompFailKey(ws, 2, "P")} vs {old}");
            ws[1].KeepNo = 1;
            check("S160 합성 실패 열쇠 — 물려받을 번호가 걸리면 열쇠가 바뀐다", WallList.CompFailKey(ws, 2, "P") != old, "");
        }
    }
}
