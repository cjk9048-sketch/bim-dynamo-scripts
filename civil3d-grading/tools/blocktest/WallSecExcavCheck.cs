// ── S169 ★★★[v104 · 계획_v104.md 5 · 6 · JACK 1021 «옹벽 형식별 일반단면 → 여유폭 · 구배로 터파기 · 되메우기 = 터파기 − 구조물»] ──
//   바닥 줄 점마다 단면 · 같은 표 줄 구간(최소 2 m · 폭 = 구간 최댓값) · 폭이 다른 띠(넓이 = 적분 · 꺾임 Mitre) · 도랑(패널식 — 앞면 선 앞) ·
//   면 기록 왕복 · 구조물 공제 기둥법(곧은 · 비스듬한 절단 · 지표로 자름 · 빈 공간 쐐기 · 선 끝 너머 · 두 줄 합집합 · 보강토체) · 표 지문
//   만든 꼴 · 공공 표 값만(현장 값 없음 — 공개 저장소)
using DH.Grading.Core;

static class WallSecExcavCheck
{
    static WallExcav.Bottom Line(IReadOnlyList<(double X, double Y)> verts, double step, Func<double, double> hAt, bool up, int soil, double slopeW, double toe = 100)
    {
        var b = new WallExcav.Bottom { Soil = soil, Up = up, SlopeW = slopeW, Src = "시험" };
        double s = 0;
        for (int i = 0; i + 1 < verts.Count; i++)
        {
            var a = verts[i]; var e = verts[i + 1];
            double L = Math.Sqrt((e.X - a.X) * (e.X - a.X) + (e.Y - a.Y) * (e.Y - a.Y));
            int n = Math.Max(1, (int)Math.Round(L / step));
            for (int k = i == 0 ? 0 : 1; k <= n; k++)
            {
                double t = (double)k / n, ss = s + L * t;
                b.Pts.Add(new Point3(a.X + (e.X - a.X) * t, a.Y + (e.Y - a.Y) * t, toe));
                b.Toe.Add(toe); b.H.Add(hAt(ss)); b.Rf.Add(0); b.Rb.Add(0);
            }
            s += L;
        }
        return b;
    }

    static double Area(List<Point3> r) { double a = 0; for (int i = 0; i < r.Count; i++) { var p = r[i]; var q = r[(i + 1) % r.Count]; a += p.X * q.Y - q.X * p.Y; } return a / 2; }

    public static void Run(Action<string, bool, string> check0)
    {
        void check(string name, bool ok, string detail = "") => check0(name, ok, detail);
        Console.WriteLine("\n== S169 옹벽 단면 → 터파기 띠 · 면 기록 · 구조물 공제(기둥법) ==");
        var st = WallExcav.Settings.Default with { On = true };
        var c = st.Common(0.01);
        var tT = WallSection.DefaultTable(WallSection.Kind.역T형);
        var tR = WallSection.DefaultTable(WallSection.Kind.보강토);
        var tP = WallSection.DefaultTable(WallSection.Kind.패널식);

        // ① 곧은 줄 · 높이 하나 — 역T h 3.6 → H 4.6 → 5.0 줄 · 앞 끝 −0.61 · 뒤 끝 2.79 · 바닥 −1.1 · 여유 0.5
        var b1 = Line(new[] { (0.0, 0.0), (20.0, 0.0) }, 0.5, _ => 3.6, up: true, soil: +1, slopeW: 0.01);
        var n1 = new WallExcav.SecNote();
        var s1 = WallExcav.Sectionize(b1, tT, c, st.Margin, n1, out string w1);
        var sh5 = WallSection.BuildRow(WallSection.Kind.역T형, tT.Rows[4], 4.6, c, cut: true);
        check("S169 ① 곧은 줄 — 구간 하나 · 5.0 줄 · 뒤 = 뒤 끝 + 여유 · 앞 = −앞 끝 + 여유 · 바닥 z = 100 + 단면 바닥 · ⚠ 없음",
            s1 != null && s1.Runs.Count == 1 && s1.Runs[0].Row == 4 && Math.Abs(s1.Runs[0].Back - (sh5.XBack + 0.5)) < 1e-12 && Math.Abs(s1.Runs[0].Front - (-sh5.XFront + 0.5)) < 1e-12
            && b1.Pts.All(p => Math.Abs(p.Z - (100 + sh5.ZBottom)) < 1e-12) && !n1.Warn,
            s1 == null ? w1 : $"뒤 {s1.Runs[0].Back:F3} · 앞 {s1.Runs[0].Front:F3} · {n1.Text}");
        var r1 = s1 == null ? null : WallExcav.StripRing(b1, s1, out string rw1);
        double want1 = 20 * (sh5.XBack + 0.5 - sh5.XFront + 0.5);
        check("S169 ① 띠 — 넓이 = 20 × (뒤 + 앞) · 반시계 · y −앞 ~ +뒤 · z = 바닥",
            r1 != null && Math.Abs(Area(r1) - want1) < 1e-6 && Math.Abs(r1.Min(p => p.Y) - (sh5.XFront - 0.5)) < 1e-9 && Math.Abs(r1.Max(p => p.Y) - (sh5.XBack + 0.5)) < 1e-9
            && r1.All(p => Math.Abs(p.Z - (100 + sh5.ZBottom)) < 1e-9), r1 == null ? "" : $"넓이 {Area(r1):F6} / {want1:F6}");

        // ② 높이가 바뀌는 줄 — h 2.0 → 7.0(20 m) · 구간 ≥ 2 m · 폭은 구간 최댓값 · 넓이 = Σ 구간 길이 × 폭(곧은 줄은 넓은 띠가 좁은 띠를 덮는다)
        var b2 = Line(new[] { (0.0, 0.0), (20.0, 0.0) }, 0.5, s => 2.0 + 5.0 * s / 20, up: true, soil: +1, slopeW: 0.01);
        var s2 = WallExcav.Sectionize(b2, tT, c, st.Margin, new WallExcav.SecNote(), out _);
        bool long2 = s2 != null && s2.Runs.All(r => r == s2.Runs[^1] || r.S1 - r.S0 >= 2.0 - 0.5 - 1e-9);
        bool cont2 = s2 != null && s2.Runs.Zip(s2.Runs.Skip(1)).All(p => Math.Abs(p.First.S1 - p.Second.S0) < 1e-12) && s2.Runs[0].S0 == 0 && Math.Abs(s2.Runs[^1].S1 - 20) < 1e-9;
        bool max2 = s2 != null && s2.Runs.All(r => Enumerable.Range(r.I0, r.I1 - r.I0 + 1).All(i =>
        {
            var sh = s2.Pts[i].Shape;
            return sh.XBack + 0.5 <= r.Back + 1e-12 && -sh.XFront + 0.5 <= r.Front + 1e-12;
        }));
        bool z2 = s2 != null && Enumerable.Range(0, b2.Pts.Count).All(i => Math.Abs(b2.Pts[i].Z - (100 + s2.Pts[i].Shape.ZBottom)) < 1e-12);
        string rw2 = ""; var r2 = s2 == null ? null : WallExcav.StripRing(b2, s2, out rw2);
        double want2 = s2 == null ? 0 : s2.Runs.Sum(r => (r.S1 - r.S0) * (r.Back + r.Front));
        check("S169 ② 높이가 바뀌는 줄 — 구간 여럿 · 이어짐 · 폭 = 구간 최댓값 · 바닥 z 점마다 · 띠 넓이 = Σ 구간 × 폭",
            s2 != null && s2.Runs.Count >= 3 && long2 && cont2 && max2 && z2 && r2 != null && Math.Abs(Area(r2) - want2) < 1e-4,
            s2 == null ? "" : $"구간 {s2.Runs.Count}({string.Join(" ", s2.Runs.Select(r => $"{r.S0:F1}~{r.S1:F1}:{r.Back:F2}"))}) · 넓이 {(r2 == null ? -1 : Area(r2)):F4} / {want2:F4} · {rw2}");

        // ③ 꺾임 — ㄱ자(동 10 m → 북 10 m) · 흙 = 왼쪽(안쪽) · 앞 = 바깥(Mitre) · 폭 하나면 넓이 = 20(B + F) − B² + F²
        var b3 = Line(new[] { (0.0, 0.0), (10.0, 0.0), (10.0, 10.0) }, 0.5, _ => 3.6, up: true, soil: +1, slopeW: 0.01);
        var s3 = WallExcav.Sectionize(b3, tT, c, st.Margin, new WallExcav.SecNote(), out _);
        var r3 = s3 == null ? null : WallExcav.StripRing(b3, s3, out _);
        double B3 = sh5.XBack + 0.5, F3 = -sh5.XFront + 0.5;
        double want3 = 20 * (B3 + F3) - B3 * B3 + F3 * F3;
        check("S169 ③ ㄱ자 꺾임 — 넓이 = 20(B + F) − B² + F²(바깥 Mitre · 안쪽 겹침 한 번)", r3 != null && Math.Abs(Area(r3) - want3) < 1e-6,
            r3 == null ? "" : $"{Area(r3):F6} / {want3:F6}");
        //   꺾임점이 구간 경계 — 동쪽 다리 h 3.6(5.0 줄) · 북쪽 다리 h 6.6(8.0 줄) · 바깥 모서리 V홈이 없다(좁은 쪽 폭의 Mitre 자리가 띠 안)
        var b4 = Line(new[] { (0.0, 0.0), (10.0, 0.0), (10.0, 10.0) }, 0.5, s => s <= 10 + 1e-9 ? 3.6 : 6.6, up: true, soil: +1, slopeW: 0.01);
        var s4 = WallExcav.Sectionize(b4, tT, c, st.Margin, new WallExcav.SecNote(), out _);
        string rw4 = ""; var r4 = s4 == null ? null : WallExcav.StripRing(b4, s4, out rw4);
        double fMin = s4 == null ? 0 : s4.Runs.Min(r => r.Front);
        bool inCorner = r4 != null && GradingGeometry.PointInRing(r4, 10 + 0.9 * fMin, -0.9 * fMin);
        double rects = s4 == null ? 0 : s4.Runs.Sum(r => (r.S1 - r.S0) * (r.Back + r.Front));
        check("S169 ③ 꺾임점이 구간 경계 — 구간 둘 · 바깥 모서리(좁은 앞 폭 Mitre)가 띠 안 · 조각 하나",
            s4 != null && s4.Runs.Count == 2 && inCorner && rw4 == "", s4 == null ? "" : $"구간 {s4.Runs.Count} · 앞 폭 {string.Join("/", s4.Runs.Select(r => r.Front.ToString("F2")))} · 넓이 {(r4 == null ? -1 : Area(r4)):F3} · 직사각형 합 {rects:F3} · {rw4}");

        // ④ 도랑(패널식) — 앞면 선 앞의 띠(뒤 폭 0 · 바닥 뒤 끝 = 앞면 선) · 넓이 = 20 × 앞
        var b5 = Line(new[] { (0.0, 0.0), (20.0, 0.0) }, 0.5, _ => 4.0, up: true, soil: +1, slopeW: 0.01);
        var s5 = WallExcav.Sectionize(b5, tP, c, st.Margin, new WallExcav.SecNote(), out _);
        string rw5 = ""; var r5 = s5 == null ? null : WallExcav.StripRing(b5, s5, out rw5);
        check("S169 ④ 도랑(패널식) — 뒤 폭 0 · 띠 전체가 앞면 선 앞(y ≤ 0) · 넓이 = 20 × 앞",
            s5 != null && s5.Trench && s5.BackMax == 0 && r5 != null && r5.Max(p => p.Y) <= 1e-9 && Math.Abs(Area(r5) - 20 * s5.Runs[0].Front) < 1e-6,
            s5 == null ? "" : $"앞 {s5.Runs[0].Front:F3} · y최대 {(r5 == null ? 0 : r5.Max(p => p.Y)):F4} · {rw5}");
        //   ★[1021 화면 없는 Civil ⑧b · V34] 벽 뒤 띠 들어내기 — 앞면 선(0,0)→(10,0) · 흙 +y · 거리 50: 선 길이 안의 뒤쪽만 들어낸다
        //   걸친 세모 둘(y −2 ~ +2)은 y ≤ 0 몫(20) · 앞쪽 통째(1) 그대로 · 뒤쪽 통째(선 길이 안) 버림 · 선 끝 너머 뒤쪽(1)은 그대로 · 높이 = 원래 평면
        var triz = new List<WallDaylight.Tri>
        {
            new(new Point3(0, -2, 10), new Point3(10, -2, 10), new Point3(10, 2, 14)),
            new(new Point3(0, -2, 10), new Point3(10, 2, 14), new Point3(0, 2, 14)),
            new(new Point3(2, -5, 7), new Point3(4, -5, 7), new Point3(3, -4, 8)),
            new(new Point3(2, 5, 7), new Point3(4, 5, 7), new Point3(3, 4, 8)),
            new(new Point3(12, 5, 7), new Point3(14, 5, 7), new Point3(13, 4, 8)),
        };
        var clipped = WallExcav.ClipBehind(triz, new WallExcav.Bottom { Pts = new() { new(0, 0, 0), new(10, 0, 0) }, Soil = +1 }, 50, out int cutN, out string cw);
        double TriA(WallDaylight.Tri t) => Math.Abs((t.B.X - t.A.X) * (t.C.Y - t.A.Y) - (t.C.X - t.A.X) * (t.B.Y - t.A.Y)) / 2;
        bool zOk = clipped.All(t => new[] { t.A, t.B, t.C }.All(q => q.X > 11 || q.Y < -3.5 || Math.Abs(q.Z - (12 + q.Y)) < 1e-9));
        bool spanOk = clipped.All(t => new[] { t.A, t.B, t.C }.All(q => q.X > 10 + 1e-9 || q.Y <= 1e-9));
        check("S169 ④ 벽 뒤 띠 들어내기 — 선 길이 안 뒤쪽만 · 걸친 세모 반(20) + 앞 통째(1) + 끝 너머 뒤(1) · 높이 = 원래 평면",
            spanOk && Math.Abs(clipped.Sum(TriA) - 22) < 1e-9 && zOk && cutN == 3 && cw == "",
            $"세모 {clipped.Count} · 넓이 {clipped.Sum(TriA):F6} · 자름 {cutN} · {cw}");
        //   ★[V33 ⑧b] 끝이 꺾인 선(날개) — 도랑 제 조각 자르기(거리 ∞ · 늘임 30 m)가 앞쪽(x < 38.5)을 지우면 안 된다 · 뒤쪽(x > 38.5 · 선 길이 안)은 다 지운다
        var wing = new WallExcav.Bottom { Pts = new() { new(39.8, 14.8, 0), new(38.5, 15, 0), new(38.5, 5, 0), new(39.8, 5.2, 0) }, Soil = +1 };
        var grid = new List<WallDaylight.Tri>();
        for (double gx = 34; gx < 44; gx += 0.5) for (double gy = 3; gy < 17; gy += 0.5)
        { grid.Add(new(new Point3(gx, gy, 1), new Point3(gx + 0.5, gy, 1), new Point3(gx + 0.5, gy + 0.5, 1))); grid.Add(new(new Point3(gx, gy, 1), new Point3(gx + 0.5, gy + 0.5, 1), new Point3(gx, gy + 0.5, 1))); }
        var wingC = WallExcav.ClipBehind(grid, wing, double.PositiveInfinity, out _, out string wcw, extend: 30);
        double frontA = wingC.Where(t => (t.A.X + t.B.X + t.C.X) / 3 < 38.5).Sum(TriA), behindA = wingC.Where(t => (t.A.X + t.B.X + t.C.X) / 3 > 38.6 && (t.A.Y + t.B.Y + t.C.Y) / 3 > 6 && (t.A.Y + t.B.Y + t.C.Y) / 3 < 14).Sum(TriA);
        check("S169 ④ 끝이 꺾인 선 — 앞쪽(x < 38.5) 넓이 그대로 · 뒤쪽 선 길이 안은 0", Math.Abs(frontA - 4.5 * 14) < 1e-6 && behindA < 1e-9 && wcw == "", $"앞 {frontA:F4}/{4.5 * 14} · 뒤 {behindA:F4}");
        var gap = WallExcav.ClipBehind(triz.Take(2).ToList(), new WallExcav.Bottom { Pts = new() { new(0, 0, 0), new(10, 0, 0) }, Soil = +1 }, WallExcav.TrenchGap, out _, out _);
        check("S169 ④ 틈 2 mm — 걸친 세모에서 0 < y ≤ 2 mm 띠만 빠진다(넓이 40 − 10 × 0.002)", Math.Abs(gap.Sum(TriA) - (40 - 10 * WallExcav.TrenchGap)) < 1e-9
            && gap.All(t => new[] { t.A, t.B, t.C }.All(q => q.Y <= 1e-12 || q.Y >= WallExcav.TrenchGap - 1e-12)), $"{gap.Sum(TriA):F6}");

        // ⑤ 단면 고르기 ⚠ — 표 밖(h 9) · 첫 줄 아래(h 0.5) · 경사 배면(rb 0.5) · 쐐기(보강토 앞면 1:0.05 vs 정지면 1:0.01)
        var b6 = Line(new[] { (0.0, 0.0), (8.0, 0.0) }, 0.5, s => s < 2 ? 0.5 : s < 4 ? 9.0 : 3.6, up: true, soil: +1, slopeW: 0.01);
        for (int i = 0; i < b6.Rb.Count; i++) if (b6.Pts[i].X >= 6) b6.Rb[i] = 0.5;
        var n6 = new WallExcav.SecNote();
        WallExcav.Sectionize(b6, tT, c, st.Margin, n6, out _);
        var b7 = Line(new[] { (0.0, 0.0), (8.0, 0.0) }, 0.5, _ => 4.0, up: false, soil: +1, slopeW: 0.01);
        var n7 = new WallExcav.SecNote();
        WallExcav.Sectionize(b7, tR, c, st.Margin, n7, out _);
        check("S169 ⑤ ⚠ — 표 밖 · 첫 줄 아래 · 경사 배면 길이 · 보강토 쐐기(앞면 1:0.05)",
            n6.Over > 1.5 && n6.Below > 1.5 && n6.SlopedBack > 1.5 && n6.OverHMax == 9.0 && n7.Wedge > 7 && n6.Wedge == 0, $"{n6.Text} || {n7.Text}");
        check("S169 ⑤ 점마다 h가 없는 바닥 줄은 못 세움 + 까닭",
            WallExcav.Sectionize(new WallExcav.Bottom { Pts = new() { new(0, 0, 1), new(1, 0, 1) } }, tT, c, 0.5, new WallExcav.SecNote(), out string w8) == null && w8.Length > 0, w8);

        // ⑥ 면 기록 왕복 · 표 스냅숏 · 틀린 기록
        var rec = WallExcav.RecordOf(new[] { s1!, s5! }, st.Margin, c, k => WallSection.DefaultTable(k));
        var enc = WallExcav.EncodeRecord(rec);
        bool okR = WallExcav.TryDecodeRecord(enc, out var rb, out string wr);
        check("S169 ⑥ 면 기록 왕복 — 줄 2 · 점 · 표 둘(역T · 패널식) · 공통 칸",
            okR && rb.Lines.Count == 2 && rb.Lines[0].Pts.SequenceEqual(rec.Lines[0].Pts) && rb.Tables.Count == 2 && rb.Tables[WallSection.Kind.역T형].Print == tT.Print
            && rb.Common.TLean == c.TLean && rb.Margin == st.Margin && rb.Lines[1].Kind == WallSection.Kind.패널식, wr);
        var badR = new List<(char, object)>(enc); badR.RemoveAt(badR.Count - 1);
        check("S169 ⑥ 잘린 기록은 거짓 + 까닭", !WallExcav.TryDecodeRecord(badR, out _, out string wr2) && wr2.Length > 0, wr2);
        var badR2 = new List<(char, object)>(enc); badR2[0] = ('I', 7);
        check("S169 ⑥ 모르는 판은 거짓 + 까닭", !WallExcav.TryDecodeRecord(badR2, out _, out string wr3) && wr3.Contains("판"), wr3);

        // ⑦ 구조물 공제 — 기둥법
        var recT = WallExcav.RecordOf(new[] { s1! }, st.Margin, c, k => tT);
        var shT = WallSection.BuildRow(WallSection.Kind.역T형, tT.Rows[4], 4.6, c, cut: true);
        double structT = shT.StructArea;
        (double[] xs, double[] lo, double[] hi) Axis(double wl, double wr, double zLo, double zHi)
        {
            int n = (int)Math.Round((wl + wr) / 0.1) + 1;
            var xs = Enumerable.Range(0, n).Select(i => -wl + (wl + wr) * i / (n - 1.0)).ToArray();
            return (xs, xs.Select(_ => zLo).ToArray(), xs.Select(_ => zHi).ToArray());
        }
        var (x7, lo7, hi7) = Axis(5, 8, 0, 200);
        var cs7 = WallExcav.Integrate(recT, 10, -5, 10, 8, 5, 8, x7, lo7, hi7);
        check("S169 ⑦ 직각 절단 — 구조물 = 단면 넓이(0.5 %)", Math.Abs(cs7.Struct - structT) < 0.005 * structT && cs7.Lines == 1, $"{cs7.Struct:F4} / {structT:F4} · {cs7.Text}");
        double wl8 = 5 * Math.Sqrt(2), wr8 = 8 * Math.Sqrt(2);
        var (x8, lo8, hi8) = Axis(wl8, wr8, 0, 200);
        var cs8 = WallExcav.Integrate(recT, 5, -5, 18, 8, wl8, wr8, x8, lo8, hi8);
        check("S169 ⑦ 45° 절단 — 구조물 = 단면 × √2(1 %)", Math.Abs(cs8.Struct - structT * Math.Sqrt(2)) < 0.01 * structT * Math.Sqrt(2), $"{cs8.Struct:F4} / {structT * Math.Sqrt(2):F4}");
        var (x9, lo9, hi9) = Axis(5, 8, 0, 100);
        var cs9 = WallExcav.Integrate(recT, 10, -5, 10, 8, 5, 8, x9, lo9, hi9);
        double zft = -1 + 0.5, hStem = 3.6 - zft;
        double below = 3.2 * 0.5 + 3.4 * 0.1 + (0.5 * (0 - zft) - 0.2 / hStem * (0 - zft) * (0 - zft) / 2);
        check("S169 ⑦ 지표(앞 땅 100)로 자름 — 앞 땅 아래 몫만(저판 · 버림 · 벽 아래 0.5 m · 1 %)", Math.Abs(cs9.Struct - below) < 0.01 * below, $"{cs9.Struct:F4} / {below:F4}");
        var (x10, lo10, hi10) = Axis(5, 8, 0, 200);
        var cs10 = WallExcav.Integrate(recT, 25, -5, 25, 8, 5, 8, x10, lo10, hi10);
        check("S169 ⑦ 선 끝 너머 절단 — 0", cs10.Struct == 0 && cs10.Air == 0, cs10.Text);
        var recTT = WallExcav.RecordOf(new[] { s1!, s1! }, st.Margin, c, k => tT);
        var cs11 = WallExcav.Integrate(recTT, 10, -5, 10, 8, 5, 8, x7, lo7, hi7);
        check("S169 ⑦ 같은 줄 두 번 — 합집합(두 번 안 뺀다)", Math.Abs(cs11.Struct - cs7.Struct) < 1e-9, $"{cs11.Struct:F4} / {cs7.Struct:F4}");
        //   빈 공간 — 정지면 앞면 1:0(연직) · 단면 앞면 1:0.02 → 쐐기 ½ × 3.6 × 0.072 = 0.1296
        var b12 = Line(new[] { (0.0, 0.0), (20.0, 0.0) }, 0.5, _ => 3.6, up: true, soil: +1, slopeW: 0.0);
        var s12 = WallExcav.Sectionize(b12, tT, c with { GradeFace = 0 }, st.Margin, new WallExcav.SecNote(), out _);
        var cs12 = WallExcav.Integrate(WallExcav.RecordOf(new[] { s12! }, st.Margin, c, k => tT), 10, -5, 10, 8, 5, 8, x7, lo7, hi7);
        check("S169 ⑦ 빈 공간 쐐기 — 정지면 1:0 · 단면 1:0.02 → 0.1296㎡(3 %)", Math.Abs(cs12.Air - 0.1296) < 0.03 * 0.1296 && Math.Abs(cs12.Struct - structT) < 0.005 * structT,
            cs12.Text);
        //   보강토 — 블록 · 패드는 구조물 · 보강토체 · 배수층은 보강토체 등(되메우기에 남김)
        var b13 = Line(new[] { (0.0, 0.0), (20.0, 0.0) }, 0.5, _ => 5.0, up: false, soil: +1, slopeW: 0.05);
        var s13 = WallExcav.Sectionize(b13, tR, c with { GradeFace = 0.05 }, st.Margin, new WallExcav.SecNote(), out _);
        var shR = s13!.Pts[0].Shape;
        double structR = shR.StructArea, reinfR = shR.Parts.Where(p => !p.Structure).Sum(p => WallSection.Area(p.Ring));
        var (x13, lo13, hi13) = Axis(5, 10, 0, 200);
        var cs13 = WallExcav.Integrate(WallExcav.RecordOf(new[] { s13 }, st.Margin, c, k => tR), 10, -5, 10, 10, 5, 10, x13, lo13, hi13);
        check("S169 ⑦ 보강토 — 구조물 = 블록 + 패드 · 보강토체 등 = 보강토체 + 배수층(1 %) · 빈 공간 0(앞면 같음)",
            Math.Abs(cs13.Struct - structR) < 0.01 * structR && Math.Abs(cs13.Reinf - reinfR) < 0.01 * reinfR && cs13.Air < 1e-9, $"{cs13.Text} · 참 {structR:F3}/{reinfR:F3}");

        //   부벽식 — 부벽(평균)도 몫만큼 뺀다(직각 절단 = 단면 구조물 넓이 · 몫 포함)
        var tCf = WallSection.DefaultTable(WallSection.Kind.부벽식);
        var b14 = Line(new[] { (0.0, 0.0), (20.0, 0.0) }, 0.5, _ => 8.0, up: true, soil: +1, slopeW: 0.01);
        var s14 = WallExcav.Sectionize(b14, tCf, c, st.Margin, new WallExcav.SecNote(), out _);
        var shC = s14!.Pts[0].Shape;
        var (x14, lo14, hi14) = Axis(5, 10, 0, 200);
        var cs14 = WallExcav.Integrate(WallExcav.RecordOf(new[] { s14 }, st.Margin, c, k => tCf), 10, -5, 10, 10, 5, 10, x14, lo14, hi14);
        check("S169 ⑦ 부벽식 — 구조물 = 단면 넓이(부벽 몫 포함 · 0.5 %)", Math.Abs(cs14.Struct - shC.StructArea) < 0.005 * shC.StructArea, $"{cs14.Struct:F4} / {shC.StructArea:F4}");

        // ⑧ 표 지문 — 같은 표면 같고 · 한 칸을 고치면 다르다(«종단 먼저»)
        var mod = tT.Clone(); mod.Rows[4][4] += 0.1;
        check("S169 ⑧ 표 지문 — 같으면 같고 고치면 다르다",
            WallExcav.TablesPrint(tT, tR) == WallExcav.TablesPrint(tT.Clone(), tR.Clone()) && WallExcav.TablesPrint(tT, tR) != WallExcav.TablesPrint(mod, tR), WallExcav.TablesPrint(tT, tR));
    }
}
