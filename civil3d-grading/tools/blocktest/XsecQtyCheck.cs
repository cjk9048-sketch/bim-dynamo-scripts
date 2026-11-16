// ── S310 XQ ★[JACK 1116 «횡단 수량표 오른쪽 칸»] 벌개재근 · 표토제거 · 면고르기 · 식생공법 · 층따기 길이 — 만든 횡단(가짜 좌표)에서 손으로 낸 값과 견준다 ──
using DH.Grading.Core;
using DH.Grading.Core.Forest;
using NetTopologySuite.Geometries;

static class XsecQtyCheck
{
    static double[] Line(double[] x, params (double X, double Z)[] pts)
    {
        var z = new double[x.Length];
        for (int i = 0; i < x.Length; i++)
        {
            double v = pts[^1].Z;
            for (int k = 0; k + 1 < pts.Length; k++)
                if (x[i] >= pts[k].X - 1e-9 && x[i] <= pts[k + 1].X + 1e-9) { v = pts[k].Z + (pts[k + 1].Z - pts[k].Z) * (x[i] - pts[k].X) / (pts[k + 1].X - pts[k].X); break; }
            z[i] = v;
        }
        return z;
    }

    public static void Run(Action<string, bool, string> check)
    {
        Console.WriteLine("\n== S310 XQ 횡단 수량표 오른쪽 칸(길이) ==");
        var xs = Enumerable.Range(0, 601).Select(i => i * 0.1).ToArray();
        var on = new XsecQtyRight.Opts(true, true, true, true, true, 0.25);
        bool Near(double a, double b, double tol = 0.15) => Math.Abs(a - b) <= tol;

        // 성토 — 원지반 5(0~20) → 1:2로 0까지(20~30) → 0 / 계획 5 평탄(20~40) → 1:2 사면(40~50)
        var g = Line(xs, (0, 5), (20, 5), (30, 0), (60, 0));
        var p = Line(xs, (0, 5), (20, 5), (40, 5), (50, 0), (60, 0));
        var f = XsecQtyRight.Merge(new[] { (0.0, 25.0), (45.0, 60.0), (20.0, 22.0) });
        check("S310a 입목지 구간 합치기 [0,25] ∪ [45,60]", f.Count == 2 && Near(f[0].A, 0) && Near(f[0].B, 25) && Near(f[1].A, 45), $"{f.Count}개");
        var r = XsecQtyRight.Measure(xs, g, p, f, on);
        check("S310b 성토 — 표토제거 = 공사 범위 30 m(성토부) · 절토부 0", Near(r.StripFill, 30) && Near(r.StripCut, 0), $"{r.StripFill:F2} / {r.StripCut:F2}");
        check("S310c 성토 — 벌개 = 숲 ∩ 공사 범위 = 5 + 5 = 10 m", Near(r.ClearFill, 10) && Near(r.ClearCut, 0), $"{r.ClearFill:F2} / {r.ClearCut:F2}");
        double slope = Math.Sqrt(100 + 25);
        check("S310d 성토 — 면고르기 = 성토 사면 빗길이 11.18 m(평탄 패드 뺌)", Near(r.SmoothFill, slope, 0.2) && Near(r.SmoothCut, 0), $"{r.SmoothFill:F2}");
        check("S310e 식생공법 = 면고르기와 같은 길이(2D 종자살포 = 성토 + 절토 면고르기)", Near(r.SeedFill, r.SmoothFill) && Near(r.SeedCut, r.SmoothCut), $"{r.SeedFill:F2}");
        check("S310f 성토 — 층따기 = 성토 아래 원지반 1:2(> 1:4) 10 m 구간 계단 단면적 2.54 ㎡(손 계산 — 아래 S311)", Near(r.Bench, 2.5435, 0.03), $"{r.Bench:F3}");

        // 절토 — 원지반 5 평탄 · 계획 10~20 1:2로 0까지 · 0 평탄(20~30) · 30~40 올라옴 → 사면 둘 + 골 바닥은 빠짐
        var g2 = Line(xs, (0, 5), (60, 5));
        var p2 = Line(xs, (0, 5), (10, 5), (20, 0), (30, 0), (40, 5), (60, 5));
        var r2 = XsecQtyRight.Measure(xs, g2, p2, null, on);
        check("S310g 절토 — 표토제거 = 30 m(절토부)", Near(r2.StripCut, 30) && Near(r2.StripFill, 0), $"{r2.StripCut:F2} / {r2.StripFill:F2}");
        check("S310h 절토 — 면고르기 = 사면 둘 22.36 m(골 바닥 10 m 뺌)", Near(r2.SmoothCut, 2 * slope, 0.3), $"{r2.SmoothCut:F2}");
        check("S310i 절토 — 층따기 0(성토 아래 아님 · 재서 0)", Near(r2.Bench, 0), $"{r2.Bench:F2}");
        check("S310j 임상도 못 읽음(forest = null) → 벌개 NaN · 나머지는 그대로", double.IsNaN(r2.ClearFill) && double.IsNaN(r2.ClearCut) && !double.IsNaN(r2.StripCut), "");

        // 소단 — 같은 쪽으로 이어진 두 사면 사이 평탄(5 m)은 면고르기에 든다
        var g3 = Line(xs, (0, 20), (60, 20));
        var p3 = Line(xs, (0, 20), (10, 20), (20, 15), (25, 15), (35, 10), (60, 10));
        var r3 = XsecQtyRight.Measure(xs, g3, p3, null, on);
        check("S310k 소단 — 사면 둘(11.18 × 2) + 같은 쪽 소단 5 m = 27.36 m", Near(r3.SmoothCut, Math.Sqrt(100 + 25) + 5 + Math.Sqrt(100 + 25), 0.3), $"{r3.SmoothCut:F2}");

        Bench311(check, xs, on);

        // 끈 항목은 NaN(표 –)
        var off = new XsecQtyRight.Opts(false, true, false, false, false, 0.25);
        var r4 = XsecQtyRight.Measure(xs, g, p, f, off);
        check("S310l 끈 항목 NaN · 켠 항목만 값", double.IsNaN(r4.ClearFill) && double.IsNaN(r4.SmoothFill) && double.IsNaN(r4.Bench) && Near(r4.StripFill, 30), "");

        // 원장 · 열쇠
        var led = new QtyLedger(); XsecQtyRight.Put(led, r4);
        check("S310m 원장 — 표토 성토부 열쇠에 담기고 안 켠 것은 NaN", Near(led.Get(QtyKey.OfRight(QtyKeyKind.Strip, false)), 30) && double.IsNaN(led.Get(QtyKey.OfRight(QtyKeyKind.Smooth, false))), "");
        var spec = QtyTableSpec.BuildFromKeys(Array.Empty<QtyKey>(), null, 4.5);
        int keyed = spec.Right.Count(x => x.Key != null);
        check("S310n 표 얼개 — 오른쪽 열쇠 9줄(벌개 · 표토 · 면고르기 · 식생 각 2 + 층따기)", keyed == 9, $"{keyed}줄");
        check("S310o 얼개 규칙 — AlwaysRight 줄이 그대로 섬", QtyTableSpecRules.Holds(spec, out var note), note ?? "");

        // 횡단선 ∩ 임상도 폴리곤 — 선 (0,0)→(60,0) · 숲 네모 x 10~25 · y −5~5 와 x 45~70
        var gf = new GeometryFactory();
        Polygon Box(double x0, double y0, double x1, double y1) => gf.CreatePolygon(new[] { new Coordinate(x0, y0), new Coordinate(x1, y0), new Coordinate(x1, y1), new Coordinate(x0, y1), new Coordinate(x0, y0) });
        var fs = new ForestSection(new Geometry[] { Box(10, -5, 25, 5), Box(45, -5, 70, 5), Box(200, 200, 210, 210) });
        check("S310p 횡단선 ∩ 숲 구간(가로 위치) — 선이 숲에 안 닿으면 빈 목록", fs.Intervals(0, 100, 60, 100, 0, 60).Count == 0, "");
        var iv2 = fs.Intervals(0, 0, 60, 0, -30, 60);
        check("S310q 횡단선 ∩ 숲 구간 = [−20,−5] · [15,30]", iv2.Count == 2 && Near(iv2[0].A, -20) && Near(iv2[0].B, -5) && Near(iv2[1].A, 15) && Near(iv2[1].B, 30), string.Join(" ", iv2.Select(t => $"[{t.A:F1},{t.B:F1}]")));
        // 엑셀 — 1)시설 시트 AE/AF(벌개) · AG/AH(표토) · AA/AC(면고르기 길이) 측점별 + 2D 평균 식
        var m = new QtyReportModel { Facility = "시험시설", SiteKnown = true };
        for (int i = 0; i < 3; i++) m.Site.Add(new QtyStation { Name = "S" + i, Sta = i * 20, Fill = 10, Cut = 5, Clear = 8 + i, Strip = 30, FillW = 11, CutW = 0, Bench = 2 + i });
        string xp = Path.Combine(Path.GetTempPath(), "dh_xq_" + Guid.NewGuid().ToString("N") + ".xlsx");
        try
        {
            QtyReportXlsx.Write(m, xp);
            using var wb = new ClosedXML.Excel.XLWorkbook(xp);
            var ws = wb.Worksheet("1)시험시설"); var sum = wb.Worksheet("토공집계표");
            check("S310r 엑셀 — 벌개 길이 AE · 표토 길이 AG 측점별 값", ws.Cell("AE8").GetDouble() == 9 && ws.Cell("AG9").GetDouble() == 30, $"{ws.Cell("AE8").GetString()} / {ws.Cell("AG9").GetString()}");
            check("S310s 엑셀 — 면적은 2D 식 ROUND((앞+이번)/2×간격,2) · 계 = SUM", ws.Cell("AF8").FormulaA1 == "ROUND((AE7+AE8)/2*$F8,2)" && ws.Cell("AH10").FormulaA1 == "SUM(AH7:AH9)", $"{ws.Cell("AF8").FormulaA1} | {ws.Cell("AH10").FormulaA1}");
            check("S310t 엑셀 — 면고르기 길이 AA에 새 값 · 집계표 표토제거(H20) 칸 채움", ws.Cell("AA7").GetDouble() == 11 && !sum.Cell("H20").IsEmpty(), $"{ws.Cell("AA7").GetString()} / {sum.Cell("H20").GetString()}");
            check("S311i 엑셀 — 층따기 AK 면적 · AL 입적 = 2D 평균 식 · 계 SUM · 집계표 층따기(H29) 칸 채움", ws.Cell("AK8").GetDouble() == 3 && ws.Cell("AL8").FormulaA1 == "ROUND((AK7+AK8)/2*$F8,2)" && ws.Cell("AL10").FormulaA1 == "SUM(AL7:AL9)" && !sum.Cell("H29").IsEmpty(), $"{ws.Cell("AK8").GetString()} | {ws.Cell("AL8").FormulaA1} | {sum.Cell("H29").FormulaA1}");
        }
        finally { try { File.Delete(xp); } catch { } }
    }

    // ── S311 층따기 — KDS 44 30 00 (JACK 1116 제공): 1:4보다 급한 성토 아래 원지반 · 높이 ≥ 0.5 · 폭 ≥ 1.0 · 배수 4 %(바깥으로) ──
    static void Bench311(Action<string, bool, string> check, double[] xs, XsecQtyRight.Opts on)
    {
        bool Near(double a, double b, double tol) => Math.Abs(a - b) <= tol;
        // 계단 꺾은선 검사 — 수직 턱 H ≥ hMin · 디딤 기울기 = 배수 · 디딤 폭 ≥ wMin(맨 끝 자른 한 칸만 예외)
        (bool Ok, string Msg, int Steps) Chk(List<(double X, double Z)[]> lines, double hMin, double wMin, double drain)
        {
            if (lines.Count != 1) return (false, $"구간 {lines.Count}개", 0);
            var p = lines[0]; int steps = 0; double minH = 1e9, minW = 1e9, minSl = 1e9, maxSl = -1e9;
            for (int k = 0; k + 1 < p.Length; k++)
            {
                double dx = p[k + 1].X - p[k].X, dz = p[k].Z - p[k + 1].Z;
                if (Math.Abs(dx) < 1e-9) { steps++; minH = Math.Min(minH, dz); }
                else
                {
                    bool last = k + 2 >= p.Length;
                    if (!last) minW = Math.Min(minW, Math.Abs(dx));
                    double sl = dz / Math.Abs(dx); minSl = Math.Min(minSl, sl); maxSl = Math.Max(maxSl, sl);
                }
            }
            bool ok = minH >= hMin - 1e-6 && minW >= wMin - 1e-6 && Math.Abs(minSl - drain) < 1e-6 && Math.Abs(maxSl - drain) < 1e-6;
            return (ok, $"턱 최소 {minH:F3} · 폭 최소 {minW:F3} · 디딤 경사 {minSl:F4}~{maxSl:F4}", steps);
        }
        double[] Ln(params (double X, double Z)[] pts)
        {
            var z = new double[xs.Length];
            for (int i = 0; i < xs.Length; i++)
            {
                double v = pts[^1].Z;
                for (int k = 0; k + 1 < pts.Length; k++)
                    if (xs[i] >= pts[k].X - 1e-9 && xs[i] <= pts[k + 1].X + 1e-9) { v = pts[k].Z + (pts[k + 1].Z - pts[k].Z) * (xs[i] - pts[k].X) / (pts[k + 1].X - pts[k].X); break; }
                z[i] = v;
            }
            return z;
        }
        var flat10 = Ln((0, 10), (60, 10));
        // ① 1:3 — g = 1/3 · H = max(0.5, 1.0 × (1/3 − 0.04)) = 0.5 · 디딤 폭 = 0.5/0.29333 = 1.7045 · 걸음 면적 0.42614
        //    30 m ÷ 1.7045 = 17 걸음 + 자른 끝 1.0227 m(면적 (0.5 + 0.2)/2 × 1.0227 = 0.35795) → 17 × 0.42614 + 0.35795 = 7.6023 ㎡
        var r3 = XsecQtyRight.Measure(xs, Ln((0, 10), (30, 0), (60, 0)), flat10, null, on);
        var c3 = Chk(r3.BenchLines, 0.5, 1.0, 0.04);
        check("S311a 1:3 — 턱 ≥ 0.5 · 디딤 폭 ≥ 1.0 · 디딤 경사 4 % · 18걸음", c3.Ok && c3.Steps == 18, c3.Msg + $" · {c3.Steps}걸음");
        check("S311b 1:3 — 층따기 단면적 7.60 ㎡(손 계산)", Near(r3.Bench, 7.6023, 0.03), $"{r3.Bench:F3}");
        // ② 1:5 · 1:4(경계) — 안 함
        var r5 = XsecQtyRight.Measure(xs, Ln((0, 10), (50, 0), (60, 0)), flat10, null, on);
        check("S311c 1:5 — 층따기 없음(면적 0 · 계단 0)", Near(r5.Bench, 0, 1e-9) && r5.BenchLines.Count == 0, $"{r5.Bench:F3} / {r5.BenchLines.Count}");
        var r4 = XsecQtyRight.Measure(xs, Ln((0, 10), (40, 0), (60, 0)), flat10, null, on);
        check("S311d 1:4(경계) — 층따기 없음(1:4는 «보다 급한»에 안 듦)", Near(r4.Bench, 0, 1e-9) && r4.BenchLines.Count == 0, $"{r4.Bench:F3} / {r4.BenchLines.Count}");
        // ③ 섞인 땅 — 평탄 · 1:2(10~20) · 1:5(20~40) · 평탄: 1:2 구간만 · 2.5435 ㎡(10 m ÷ 1.087 = 9걸음 + 0.217 m)
        var rm = XsecQtyRight.Measure(xs, Ln((0, 5), (10, 5), (20, 0), (40, -4), (60, -4)), Ln((0, 5), (60, 5)), null, on);
        bool inside = rm.BenchLines.Count == 1 && rm.BenchLines[0].All(p => p.X >= 10 - 1e-6 && p.X <= 20 + 1e-6);
        var cm = Chk(rm.BenchLines, 0.5, 1.0, 0.04);
        check("S311e 섞인 땅 — 1:2 구간(10~20 m)만 계단 · 2.54 ㎡", inside && cm.Ok && Near(rm.Bench, 2.5435, 0.03), $"{rm.Bench:F3} · {cm.Msg}");
        // ④ 암반 — 턱 최소 0.4 → H = max(0.4, 0.46) = 0.46 · 폭 1.0 · 10걸음 · 10 × 0.23 = 2.30 ㎡
        var rr = XsecQtyRight.Measure(xs, Ln((0, 5), (10, 5), (20, 0), (40, -4), (60, -4)), Ln((0, 5), (60, 5)), null, on, a => true);
        check("S311f 암반 — 턱 최소 0.4(H = 0.46 · 폭 1.0) · 2.30 ㎡", Near(rr.Bench, 2.30, 0.03), $"{rr.Bench:F3}");
        // ⑤ 왼쪽이 낮은 땅(왼쪽으로 내려감) — 좌우 뒤집어도 같은 면적
        var rl = XsecQtyRight.Measure(xs, Ln((0, 0), (30, 10), (60, 10)), flat10, null, on);
        var cl = Chk(rl.BenchLines, 0.5, 1.0, 0.04);
        check("S311g 왼쪽으로 내려가는 1:3 — 같은 7.60 ㎡ · 디딤은 내리막(왼쪽) 쪽", cl.Ok && Near(rl.Bench, 7.6023, 0.03) && rl.BenchLines[0][0].X > rl.BenchLines[0][^1].X, $"{rl.Bench:F3} · {cl.Msg}");
        // ⑥ 설정값 — 폭 최소 3.0(장비 작업) → H = max(0.5, 3 × 0.2933 = 0.88)
        var rw = XsecQtyRight.Measure(xs, Ln((0, 10), (30, 0), (60, 0)), flat10, null, on with { Spec = BenchSpec.Soil with { WMin = 3.0 } });
        var cw = Chk(rw.BenchLines, 0.88, 3.0, 0.04);
        check("S311h 폭 최소 3.0 — 턱 ≥ 0.88 · 디딤 폭 ≥ 3.0", cw.Ok, cw.Msg);
    }
}
