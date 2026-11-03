// ── S192 DG ★★[도로 마무리 ② · 계획_도로마무리.md 6.2 · 8장 DG] 측구 단면 꼴(U형 · L형 대표단면 · 치수 칸) ──
//   대표값 = JACK 도면 치수(공개 규격 꼴 · 현장 값 아님)
using DH.Grading.Core;

static class DitchCheck
{
    public static void Run(Action<string, bool, string> check)
    {
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        // ① U형 대표(300×300 · 벽 · 바닥 150 · 기초 100 · 내밈 100 · 뚜껑 자리 50×50) — 윗폭 600 · 깊이 550 · 넓이 손셈 · 홈 · 꼭짓점
        {
            var s = DitchSection.U(new());
            static bool Has(List<(double O, double Z)> p, double o, double z) => p.Any(q => Math.Abs(q.O - o) < 1e-12 && Math.Abs(q.Z - z) < 1e-12);
            double want = 0.6 * 0.45 - 0.3 * 0.3 - 2 * 0.05 * 0.05 + 0.8 * 0.1;
            bool ok = Math.Abs(s.TopWidth - 0.6) < 1e-12 && Math.Abs(s.Depth - 0.55) < 1e-12 && Math.Abs(s.Area - want) < 1e-12 && Math.Abs(s.Channel!.Value.O0 - 0.15) < 1e-12 && Math.Abs(s.Channel.Value.O1 - 0.45) < 1e-12 && s.Concrete.Count == 2
                      && Has(s.Concrete[0], 0.10, 0.0) && Has(s.Concrete[0], 0.15, -0.3) && Has(s.Concrete[1], -0.1, -0.55) && s.Warn == "" && s.Label.Contains("B300×H300");
            check("S192 DG① U형 대표 — 윗폭 0.600 · 깊이 0.550 · 콘크리트 넓이 = 몸체 − 홈 − 뚜껑 자리 + 기초(손셈 1e-12) · 홈 0.15~0.45 · 이름", ok, string.Format(ci, "넓이 {0:F4} 기대 {1:F4} · {2}", s.Area, want, s.Label));
        }
        // ② L형 대표(JACK 2019) — 윗폭 0.750 · 콘크리트 0.140 ㎡/m(도면 수량 ±0.002) · 경계석 노출 200 · 묻힘 50 · 끝 두께 220 맞음(경고 없음)
        {
            var s = DitchSection.L(new());
            var curb = s.Curb!;
            double reveal = curb.Max(p => p.Z) - (-0.06 * 0.5), buried = (-0.06 * 0.5) - curb.Min(p => p.Z);
            bool ok = Math.Abs(s.TopWidth - 0.75) < 1e-12 && Math.Abs(s.Area - 0.140) <= 0.002 && Math.Abs(reveal - 0.2) < 1e-12 && Math.Abs(buried - 0.05) < 1e-12 && Math.Abs(s.Depth - 0.22) < 1e-12 && s.Warn == "";
            check("S192 DG② L형 대표 — 윗폭 0.750 · 콘크리트 0.140 ㎡/m(±0.002) · 노출 0.200 · 묻힘 0.050 · 끝 두께 0.220 · 경고 없음", ok, string.Format(ci, "넓이 {0:F4} · 노출 {1:F3} · 묻힘 {2:F3} · {3}", s.Area, reveal, buried, s.Warn));
        }
        // ③ 치수 칸 — 바깥 끝 두께가 셈과 다르면 경고(국방부 꼴 0.200) · 0/음수 · 뚜껑 자리가 벽보다 크면 거절 · 치수 바꾸면 넓이 따라감
        {
            var l2 = DitchSection.L(new() { Te = 0.20 });
            string u0 = DitchSection.Check(new DitchSection.UDims(B: 0)), u1 = DitchSection.Check(new DitchSection.UDims(Sw: 0.2)), l0 = DitchSection.Check(new DitchSection.LDims(Reveal: 0.3));
            var u4 = DitchSection.U(new() { B = 0.4, H = 0.4 });
            bool ok = l2.Warn.Contains("바깥 끝 두께") && u0.Length > 0 && u1.Length > 0 && l0.Length > 0 && Math.Abs(u4.TopWidth - 0.7) < 1e-12 && u4.Area > DitchSection.U(new()).Area;
            check("S192 DG③ 치수 칸 — 끝 두께 안 맞음 경고 · 0 · 뚜껑 자리 > 벽 · 보이는 높이 ≥ 경계석 높이 거절 · 400×400 윗폭 0.700 · 넓이 늘어남", ok, $"{l2.Warn} | {u0} | {u1} | {l0}");
        }
        // ④ 없음 = 꼴 없음
        check("S192 DG④ 형식 없음 = 꼴 없음 · U · L = 제 꼴", DitchSection.Of(DitchSection.Kind.None, null, null) == null && DitchSection.Of(DitchSection.Kind.U, null, null)!.Kind == DitchSection.Kind.U && DitchSection.Of(DitchSection.Kind.L, null, null)!.Kind == DitchSection.Kind.L, "");
        Engine(check);
    }

    const double OX = 200000, OY = 550000;

    /// <summary>기운 평면 땅(꺾임 없음) — 왼쪽(+Y) 오르막(절토) · 오른쪽 내리막(성토).</summary>
    sealed class Plane : IGroundSurface, IGroundBreaks
    {
        public bool TryGetElevation(double x, double y, out double z) { z = 100 + 0.3 * (y - OY); return Math.Abs(x - OX - 60) < 200 && Math.Abs(y - OY) < 150; }
        public void Breaks(double x0, double y0, double x1, double y1, List<GroundBreak> o) => o.Clear();
    }

    /// <summary>세모 목록에서 점 (x, y)의 높이(품는 세모 평면 · 없으면 NaN).</summary>
    static double MeshZ(List<WallDaylight.Tri> tris, double x, double y)
    {
        foreach (var t in tris)
        {
            double x1 = t.B.X - t.A.X, y1 = t.B.Y - t.A.Y, x2 = t.C.X - t.A.X, y2 = t.C.Y - t.A.Y, px = x - t.A.X, py = y - t.A.Y, d = x1 * y2 - y1 * x2;
            if (d == 0) continue;
            double l1 = (px * y2 - py * x2) / d, l2 = (x1 * py - y1 * px) / d;
            if (l1 >= -1e-9 && l2 >= -1e-9 && l1 + l2 <= 1 + 1e-9) return t.A.Z + l1 * (t.B.Z - t.A.Z) + l2 * (t.C.Z - t.A.Z);
        }
        return double.NaN;
    }

    /// <summary>★[계획 6.1 · 8장 DG] 엔진 — 측구 구간 가장자리 = 길어깨 끝 + 윗폭 · 측구 자리 평평 · 구간 경계 직각 끝면 짝 · 끝 마구리 폭 · 옹벽은 측구 바깥 · 폭 0 기록 = 비트 같음.</summary>
    static void Engine(Action<string, bool, string> check)
    {
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        int n = 121;
        var X = new double[n]; var Y = new double[n]; var Z = new double[n];
        for (int i = 0; i < n; i++) { X[i] = OX + i; Y[i] = OY; Z[i] = 100 + 0.01 * i; }
        var g = new Plane();
        RoadGrade.Input Inp(List<RoadGrade.Ditch> d, List<RoadGrade.TierWall>? tw = null) => new()
        {
            X = X, Y = Y, Z = Z, Base = g, Cell = 0.5, Reach = 40, SectionGap = 2, Ditches = d, TierWalls = tw ?? new(),
            Cut = new RoadGrade.SlopeRule { Slope = 1.5, BenchH = 5, BenchW = 1 }, Fill = new RoadGrade.SlopeRule { Slope = 1.8, BenchH = 5, BenchW = 1 },
        };
        double wU = DitchSection.U(new()).TopWidth, wL = DitchSection.L(new()).TopWidth;
        var sec = new RoadGrade.Section();
        // ⑤ U형 좌 [30, 70] · L형 우 [40, 90] · 우 시점 [0, 15](끝 마구리)
        var dd = new List<RoadGrade.Ditch> { new(30, 70, -1, wU), new(40, 90, 1, wL), new(0, 15, 1, wL) };
        var inp = Inp(dd); var r = RoadGrade.Run(inp);
        var tris = RoadGrade.ExactSurface(inp, r, out string note, out _);
        var ctx = new RoadGrade.RayCtx(inp, r);
        var l50 = ctx.SideRay(-1, 50); var r50 = ctx.SideRay(1, 50); var l10 = ctx.SideRay(-1, 10); var r60 = ctx.SideRay(1, 60);
        double zc50 = 100 + 0.5, zEdgeL = zc50 + sec.Drop(-sec.OuterL), zEdgeR = zc50 + sec.Drop(sec.OuterR);
        bool rays = Math.Abs((l50.Ey - OY) - (sec.OuterL + wU)) < 1e-9 && Math.Abs(l50.Ez - zEdgeL) < 1e-9
                    && Math.Abs((OY - r50.Ey) - (sec.OuterR + wL)) < 1e-9 && Math.Abs(r50.Ez - zEdgeR) < 1e-9
                    && Math.Abs((l10.Ey - OY) - sec.OuterL) < 1e-9 && Math.Abs((OY - r60.Ey) - (sec.OuterR + wL)) < 1e-9;
        // 측구 자리 도로 면 = 길어깨 끝 높이(평평 · 1 mm) — 좌 측구 가운데 · 우 측구 가운데
        double zDl = MeshZ(tris, OX + 50.3, OY + sec.OuterL + wU / 2), zDr = MeshZ(tris, OX + 50.3, OY - sec.OuterR - wL / 2);
        double zcm = 100 + 0.503;
        bool flat = Math.Abs(zDl - (zcm + sec.Drop(-sec.OuterL))) < 1e-3 && Math.Abs(zDr - (zcm + sec.Drop(sec.OuterR))) < 1e-3;
        // 측구 없는 자리(좌 s 20)의 같은 옵셋 = 사면(길어깨 끝보다 높음 — 절토)
        double zNo = MeshZ(tris, OX + 20, OY + sec.OuterL + wU / 2);
        bool slope = zNo > 100.2 + sec.Drop(-sec.OuterL) + 0.1;
        check("S192 DG⑤ 엔진 측구 — 사면 시작 = 길어깨 끝 + 윗폭(U 좌 0.600 · L 우 0.750 · 1e-9) · 가장자리 높이 = 길어깨 끝 · 측구 자리 도로 면 평평(1 mm) · 측구 없는 곳은 그대로 사면 · 선 면 검사 통과",
            rays && flat && slope && !note.StartsWith("⚠") && note.Contains("검사 통과"),
            string.Format(ci, "좌 {0:F4} 우 {1:F4} · 평평 {2:F4}/{3:F4} · 사면 {4:F3} · {5}", l50.Ey - OY, OY - r50.Ey, zDl - (zcm + sec.Drop(-sec.OuterL)), zDr - (zcm + sec.Drop(sec.OuterR)), zNo, note.Length > 140 ? note[..140] : note));
        // ⑥ 구간 경계 = 직각 끝면 짝(5 cm) — 30 ± 0.025 · 70 ± 0.025(좌) · 40 · 90(우) 횡단이 있음 · 그 사이 세분 없음
        var xs = r.Sections!.X;
        bool Pair(double c) { int i = xs.FindIndex(q => Math.Abs(q.S - (c - 0.025)) < 1e-9); return i >= 0 && i + 1 < xs.Count && xs[i].PairStart && Math.Abs(xs[i + 1].S - (c + 0.025)) < 1e-9; }
        bool pairs = Pair(30) && Pair(70) && Pair(40) && Pair(90) && Pair(15);
        check("S192 DG⑥ 측구 구간 경계 = 직각 끝면 짝(±2.5 cm) — 좌 30 · 70 · 우 15 · 40 · 90", pairs, $"{Pair(30)}/{Pair(70)}/{Pair(15)}/{Pair(40)}/{Pair(90)}");
        // ⑦ 끝 마구리 폭 = 그 끝 측점의 폭 — 시점 우측 측구(0~15) → 끝 선 오른쪽 끝 = OuterR + wL · 왼쪽 = OuterL
        var cap0 = r.Sections.Caps[0].Where(q => q.Side == 0).ToList();
        bool cap = cap0.Count > 0 && Math.Abs(cap0.Max(q => q.Lat) - (sec.OuterR + wL)) < 1e-9 || r.Sections.Caps[0].Any(q => q.Side == 1 && Math.Abs((OY - q.Ey) - (sec.OuterR + wL)) < 1e-9);
        bool capL = r.Sections.Caps[0].Where(q => q.Side == -1).All(q => Math.Abs((q.Ey - OY) - sec.OuterL) < 1e-9);
        check("S192 DG⑦ 끝 마구리 폭 — 시점 우측 측구 = 끝 선이 길어깨 끝 + 0.750까지 · 좌측(측구 없음)은 길어깨 끝", cap && capL, $"{cap}/{capL}");
        // ⑧ 옹벽은 측구 바깥(비평 M8) — 가장자리 옹벽(0단 · 소단 0) 좌 [40, 60] + U형 → 벽 광선 뿌리 = OuterL + 0.6 · 계단 옹벽(2단) 좌 [40, 60]도
        {
            var iW = Inp(dd, new() { new(40, 60, -1, 0, 0) }); var rW = RoadGrade.Run(iW); var cW = new RoadGrade.RayCtx(iW, rW); var wr = cW.SideRay(-1, 50);
            var iT = Inp(dd, new() { new(40, 60, -1, 1, 1) }); var rT = RoadGrade.Run(iT); var cT = new RoadGrade.RayCtx(iT, rT); var tr2 = cT.SideRay(-1, 50);
            var tW = RoadGrade.ExactSurface(iW, rW, out string nW, out _); var tT = RoadGrade.ExactSurface(iT, rT, out string nT, out _);
            bool ok = wr.EdgeWall && Math.Abs((wr.Ey - OY) - (sec.OuterL + wU)) < 1e-9 && tr2.Prof > 0 && Math.Abs((tr2.Ey - OY) - (sec.OuterL + wU)) < 1e-9 && !nW.StartsWith("⚠") && !nT.StartsWith("⚠");
            check("S192 DG⑧ 측구 + 옹벽 같은 구간 — 가장자리 옹벽 · 계단 옹벽 모두 측구 바깥(길어깨 끝 + 0.600)에서 섬 · 선 면 검사 통과", ok, $"{wr.EdgeWall} {wr.Ey - OY:F4} · {tr2.Prof} {tr2.Ey - OY:F4} · {(nW.Length > 60 ? nW[..60] : nW)} · {(nT.Length > 60 ? nT[..60] : nT)}");
        }
        // ⑨ 측구 없는 도로 · 폭 0 기록만 있는 도로 = 기록 없는 도로와 세모 비트 같음(지문 · 면 그대로)
        {
            var i0 = Inp(new()); var t0 = RoadGrade.ExactSurface(i0, RoadGrade.Run(i0), out _, out _);
            var i1 = Inp(new() { new(20, 80, -1, 0) }); var t1 = RoadGrade.ExactSurface(i1, RoadGrade.Run(i1), out _, out _);
            check("S192 DG⑨ 폭 0 측구 기록 = 기록 없는 도로와 세모 비트 같음", t0.Count > 0 && t0.SequenceEqual(t1), $"{t0.Count}/{t1.Count}");
        }
        // ⑩ 나중 기록이 이김 · 폭 0(«없음») 기록이 지움 — 좌 [30, 70] U 뒤 좌 [45, 55] 0 → s 50 가장자리 = OuterL · s 35 = OuterL + 0.6
        {
            var i2 = Inp(new() { new(30, 70, -1, wU), new(45, 55, -1, 0) });
            bool ok = Math.Abs(i2.Outer(-1, 50) - sec.OuterL) < 1e-12 && Math.Abs(i2.Outer(-1, 35) - (sec.OuterL + wU)) < 1e-12 && Math.Abs(i2.Outer(1, 35) - sec.OuterR) < 1e-12;
            check("S192 DG⑩ 겹친 기록 — 나중 기록이 이김 · «없음»(폭 0)이 그 구간을 지움 · 다른 쪽은 그대로", ok, "");
        }
    }
}
