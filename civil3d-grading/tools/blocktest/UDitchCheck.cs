// ── S260 UD ★★[배수지 U형 측구 1단계 MVP · 명세서 §7] 설정 · 기록 왕복 · 부지 대표점 · 강우 CSV · 종단 · 수리(손셈) · 3D 덩어리(닫힘 · 부피 = 넓이 × 길이) · 보고서 ──
//   값 · 좌표는 모두 지어낸 것(공개 저장소 — 현장 값 없음)
using DH.Grading.Core;

static class UDitchCheck
{
    public static void Run(Action<string, bool, string> check)
    {
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        var S = new UDitch.Settings();
        UDitch.Ditch Line(double[][] xy, Func<double, double, double> ground, UDitch.Size? size = null)
        {
            var d = new UDitch.Ditch { Pts = xy.Select(p => new[] { p[0], p[1], ground(p[0], p[1]) }).ToList(), Size = (size ?? S.Sizes[0]).Copy(), C = 0.9 };
            UDitch.ApplyAuto(d, S);
            return d;
        }

        // ① 설정 — 기본 · JSON 왕복 · 틀린 값은 기본으로(까닭) · 맞지 않는 규격 줄만 뺌
        {
            var s2 = UDitch.ParseSettings(UDitch.SettingsJson(S), out string w0);
            var s3 = UDitch.ParseSettings("{ \"ManningN\": -1, \"DepthRatio\": 2, \"Sizes\": [ { \"Name\": \"bad\", \"B\": 0, \"H\": 0.3, \"Tw\": 0.1, \"Tb\": 0.1 }, { \"Name\": \"x\", \"B\": 0.35, \"H\": 0.35, \"Tw\": 0.15, \"Tb\": 0.15 } ] }", out string w1);
            var s4 = UDitch.ParseSettings("{ nope", out string w2);
            // 번들에 싣는 설정 파일(주석 줄 포함)이 기본값과 같은가
            string bundle = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "DH.Grading.Civil", "DH.Grading.bundle", "Contents", "UDitch.settings.json");
            var sb = File.Exists(bundle) ? UDitch.ParseSettings(File.ReadAllText(bundle), out string wb) : null;
            bool okB = sb != null && UDitch.SettingsJson(sb) == UDitch.SettingsJson(S);
            bool ok = w0 == "" && s2.Sizes.Count == 6 && Math.Abs(s2.ManningN - 0.013) < 1e-15 && s2.Sizes[2].Label == "500×500"
                      && Math.Abs(s3.ManningN - 0.013) < 1e-15 && Math.Abs(s3.DepthRatio - 0.8) < 1e-15 && s3.Sizes.Count == 1 && s3.Sizes[0].Name == "x" && w1.Contains("ManningN") && w1.Contains("규격 bad")
                      && w2.Length > 0 && s4.Sizes.Count == 6 && okB;
            check("S260 UD① 설정 — 기본 6규격 · 왕복 · 틀린 값 → 기본 + 까닭 · 틀린 규격 줄만 뺌 · 깨진 JSON → 기본 · 번들 설정 파일 = 기본값", ok, $"{w1} | {w2} | 번들 {(sb == null ? "없음 " + bundle : okB ? "같음" : "다름")}");
        }

        // ② 기록 왕복 — NaN 지표면 · 강우 · 핸들 · 판이 더 높으면 거절
        {
            var r = new UDitch.Record { Rain = new UDitch.Rain { ReturnYears = 30, DurationMin = 10, IntensityMmh = 123.4, Source = "지어낸 표", Method = "수동" } };
            r.Ditches.Add(new UDitch.Ditch { Id = "D-007", Pts = new() { new[] { 1.0, 2.0, double.NaN }, new[] { 3.0, 4.0, 5.0 } }, StartInvert = 1.5, EndInvert = double.NaN, Solids = new() { "1A2B" } });
            bool ok1 = UDitch.TryParse(UDitch.ToJson(r), out var b, out string why);
            var d = ok1 ? b.Ditches[0] : null;
            bool ok = ok1 && d != null && double.IsNaN(d.Pts[0][2]) && d.Pts[1][2] == 5.0 && double.IsNaN(d.EndInvert) && d.Solids[0] == "1A2B" && b.Rain.IntensityMmh == 123.4 && b.Rain.Has
                      && UDitch.NextId(b.Ditches) == "D-008" && UDitch.NextId(new List<UDitch.Ditch>()) == "D-001"
                      && !UDitch.TryParse("{\"Version\": 99}", out _, out string w99) && w99.Contains("새 판")
                      && !new UDitch.Rain().Has && new UDitch.Rain().Text == "자료 미확보";
            check("S260 UD② 기록 왕복 — NaN 지표면 · 강우 · 솔리드 핸들 · 다음 이름 D-008 · 새 판 거절 · 빈 강우 = 자료 미확보", ok, why);
        }

        // ③ 부지 대표점 — 네모 둘(넓이 가중) · 대좌표 · 경위도(5186 원점 = 127°E 38°N) · 모르는 좌표계 / 한반도 밖 = null
        {
            var a = new List<(double X, double Y)> { (200000, 550000), (200100, 550000), (200100, 550100), (200000, 550100) };   // 1만 ㎡ · 가운데 (200050, 550050)
            var b = new List<(double X, double Y)> { (200200, 550000), (200200, 550050), (200300, 550050), (200300, 550000) };   // 시계 · 5천 ㎡ · (200250, 550025)
            var c = UDitch.Centroid(new[] { a, b });
            double wx = (200050.0 * 10000 + 200250.0 * 5000) / 15000.0, wy = (550050.0 * 10000 + 550025.0 * 5000) / 15000.0;
            var ll = UDitch.LonLat(200000, 600000, 5186);
            bool ok = c != null && Math.Abs(c.Value.X - wx) < 1e-6 && Math.Abs(c.Value.Y - wy) < 1e-6 && Math.Abs(c.Value.Area - 15000) < 1e-6
                      && UDitch.Centroid(new List<List<(double, double)>>()) == null
                      && ll != null && Math.Abs(ll.Value.Lon - 127) < 1e-9 && Math.Abs(ll.Value.Lat - 38) < 1e-9
                      && UDitch.LonLat(200000, 600000, 4326) == null && UDitch.LonLat(9e6, 9e6, 5186) == null && UDitch.LonLat(double.NaN, 0, 5186) == null;
            check("S260 UD③ 부지 대표점 — 넓이 가중 무게중심 · 감김 무관 · 5186 원점 → 127° · 38° · 모르는 좌표계 · 한반도 밖 = 확인 필요", ok,
                  c == null ? "null" : string.Format(ci, "({0:F3}, {1:F3}) 기대 ({2:F3}, {3:F3}) · {4}", c.Value.X, c.Value.Y, wx, wy, ll));
        }

        // ④ 강우 CSV — 긴 꼴(머리 · 출처 주석 · 틀린 줄) · 넓은 꼴(년 칸) · 겹침 · 정확한 줄만(보간 안 함)
        {
            string lng = "# 출처: 지어낸 강우표 2026\nduration_min,return_period,intensity_mmh\n10,30,150.5\n60,30,80\nx,30,1\n10,30,999\n";
            var r1 = UDitch.ParseIdfCsv(lng, out string src1, out var e1);
            string wide = "﻿지속기간(분);10년;30년;50년\n10;120;150;170\n20;90;;130\n";
            var r2 = UDitch.ParseIdfCsv(wide, out string src2, out var e2);
            var r3 = UDitch.ParseIdfCsv("5,10,200\n15,10,120\n", out _, out var e3);   // 머리 없음 = 차례
            var r4 = UDitch.ParseIdfCsv("a,b,c\n1,2,3\n", out _, out var e4);
            bool ok = r1.Count == 2 && src1 == "지어낸 강우표 2026" && e1.Count == 2 && e1.Any(e => e.Contains("5줄")) && e1.Any(e => e.Contains("겹침"))
                      && UDitch.Find(r1, 30, 10)?.IntensityMmh == 150.5 && UDitch.Find(r1, 30, 30) == null
                      && r2.Count == 5 && e2.Count == 0 && src2 == "" && UDitch.Find(r2, 50, 20)?.IntensityMmh == 130 && UDitch.Find(r2, 30, 20) == null
                      && r3.Count == 2 && e3.Count == 0 && UDitch.Find(r3, 10, 15)?.IntensityMmh == 120
                      && r4.Count == 0 && e4.Count == 1 && e4[0].Contains("머리");
            check("S260 UD④ 강우 CSV — 긴 꼴 · 출처 주석 · 틀린 줄/겹침 알림 · 넓은 꼴(;) · 빈 칸 · 머리 없음 · 모르는 머리 · 표에 없으면 null(보간 안 함)", ok,
                  $"긴 {r1.Count}({string.Join("/", e1)}) · 넓은 {r2.Count}({string.Join("/", e2)}) · 차례 {r3.Count} · 모름 {string.Join("/", e4)}");
        }

        // ⑤ 종단 — ㄱ자 선(20 m) · 지표면 경사 · 기본 바닥고 = 지표면 − H · 경사 · 역경사 · 최소 경사 · 윗면이 지표면 위 · 너무 꺾임
        {
            Func<double, double, double> g = (x, y) => 50.0 - 0.01 * (x + y);   // 1 % 내림
            var d = Line(new[] { new[] { 0.0, 0.0 }, new[] { 10.0, 0.0 }, new[] { 10.0, 10.0 } }, g);
            var p = UDitch.ProfileOf(d, S);
            bool ok1 = p.Error == "" && Math.Abs(p.Length - 20) < 1e-12 && Math.Abs(d.StartInvert - 49.7) < 1e-9 && Math.Abs(d.EndInvert - 49.5) < 1e-9
                       && Math.Abs(p.SlopePct - 1.0) < 1e-9 && p.Warn.Count == 0 && Math.Abs(p.MaxAbove) < 1e-9 && Math.Abs(p.MaxDeflectDeg - 90) < 1e-9 && Math.Abs(p.Inv[1] - 49.6) < 1e-9;
            var rev = Line(new[] { new[] { 10.0, 10.0 }, new[] { 10.0, 0.0 }, new[] { 0.0, 0.0 } }, g);
            var pr = UDitch.ProfileOf(rev, S);
            var flat = Line(new[] { new[] { 0.0, 0.0 }, new[] { 100.0, 0.0 } }, (x, y) => 50 - 0.001 * x);   // 0.1 %
            var pf = UDitch.ProfileOf(flat, S);
            var hand = Line(new[] { new[] { 0.0, 0.0 }, new[] { 10.0, 0.0 } }, (x, y) => 50);
            hand.InvertAuto = false; hand.StartInvert = 49.9; hand.EndInvert = 49.85;   // 윗면 50.2 → 지표면 위 0.2
            var ph = UDitch.ProfileOf(hand, S);
            var sharp = Line(new[] { new[] { 0.0, 0.0 }, new[] { 10.0, 0.0 }, new[] { 0.0, 1.0 } }, g);
            var ps = UDitch.ProfileOf(sharp, S);
            var big = Line(new[] { new[] { 0.0, 0.0 }, new[] { 10.0, 0.0 } }, (x, y) => 50, S.Sizes[3]);   // 600 → 바닥고 49.4
            bool ok = ok1 && pr.Warn.Any(w => w.Contains("역경사")) && pf.Warn.Any(w => w.Contains("최소 경사")) && ph.Warn.Any(w => w.Contains("지표면 위로 최대 0.20"))
                      && ps.Error.Contains("꺾임") && Math.Abs(big.StartInvert - 49.4) < 1e-9;
            check("S260 UD⑤ 종단 — ㄱ자 20 m · 바닥고 49.700 → 49.500 · 1 % · 꺾임 90° · 역경사 · 최소 경사 미달 · 윗면 0.20 m 나옴 · 120° 넘는 꺾임 거절 · 규격 따라 바닥고", ok,
                  string.Format(ci, "L {0:F3} · {1:F3}→{2:F3} · {3:F3} % · 경고 [{4}] · 역 [{5}] · 평 [{6}] · 손 [{7}] · 급 «{8}»", p.Length, d.StartInvert, d.EndInvert, p.SlopePct,
                                string.Join("/", p.Warn), string.Join("/", pr.Warn), string.Join("/", pf.Warn), string.Join("/", ph.Warn), ps.Error));
        }

        // ⑥ 수리 — Manning 손셈(300×300 · 수심 0.24 · 0.5 % · n 0.013 ≈ 0.0800) · 합리식 0.125 · 판정 다섯 갈래 · 추천 400×400
        {
            double qm = UDitch.ManningQ(0.3, 0.24, 0.005, 0.013);
            double qr = UDitch.RationalQ(0.9, 100, 0.5);
            var d = Line(new[] { new[] { 0.0, 0.0 }, new[] { 100.0, 0.0 } }, (x, y) => 50 - 0.005 * x);
            var none = UDitch.Check(d, new UDitch.Rain(), S);
            var rain = new UDitch.Rain { ReturnYears = 30, DurationMin = 10, IntensityMmh = 100, Source = "지어냄" };
            var noA = UDitch.Check(d, rain, S);
            d.AreaHa = 0.5;
            var sh = UDitch.Check(d, rain, S);
            var d4 = Line(new[] { new[] { 0.0, 0.0 }, new[] { 100.0, 0.0 } }, (x, y) => 50 - 0.005 * x, S.Sizes[1]); d4.AreaHa = 0.5;
            var okk = UDitch.Check(d4, rain, S);
            var dl = Line(new[] { new[] { 0.0, 0.0 }, new[] { 100.0, 0.0 } }, (x, y) => 50 - 0.005 * x); dl.AreaHa = 0.01;
            var low = UDitch.Check(dl, rain, S);
            var dr = Line(new[] { new[] { 0.0, 0.0 }, new[] { 100.0, 0.0 } }, (x, y) => 50 + 0.005 * x); dr.AreaHa = 0.5;
            var rv = UDitch.Check(dr, rain, S);
            double yn = UDitch.NormalDepth(0.4, 0.4, 0.005, 0.013, 0.125);
            bool ok = Math.Abs(qm - 0.0800) < 5e-4 && Math.Abs(qr - 0.125) < 1e-15
                      && none.Verdict == UDitch.Verdict.NoRain && none.Status == "강우자료 없음" && double.IsNaN(none.Q) && Math.Abs(none.Qcap - qm) < 1e-9
                      && noA.Verdict == UDitch.Verdict.NoArea
                      && sh.Verdict == UDitch.Verdict.Short && sh.Status == "부족" && Math.Abs(sh.Q - 0.125) < 1e-12 && sh.Recommend == "400×400"
                      && okk.Verdict == UDitch.Verdict.Ok && okk.Status == "OK" && okk.Vel > 0.6 && okk.Vel < 3 && Math.Abs(UDitch.ManningQ(0.4, yn, 0.005, 0.013) - 0.125) < 1e-9
                      && low.Verdict == UDitch.Verdict.Caution && low.Notes.Any(n => n.Contains("퇴적"))
                      && rv.Verdict == UDitch.Verdict.NoSlope;
            check("S260 UD⑥ 수리 — Manning 손셈 0.0800 · 합리식 0.125 · 강우 없음(통수능은 셈) · 면적 없음 · 부족 → 추천 400×400 · OK(유속 범위) · 등류 수심 · 저유속 주의 · 역경사", ok,
                  string.Format(ci, "Qm {0:F5} · 부족 Q {1:F4}/{2:F4} 추천 {3} · OK V {4:F2} y {5:F3} · 저 {6} {7:F2} · 역 {8}", qm, sh.Q, sh.Qcap, sh.Recommend, okk.Vel, yn, low.Status, low.Vel, rv.Status));
        }

        // ⑦ 3D 덩어리 — ㄱ자 · 30° · 곧은 대좌표: 닫힘(변마다 반대 방향 둘) · 부피 = 넓이 × 중심선(1e-6) · 바닥/윗면 높이 · 장면 메시 세모 수
        {
            double ox = 200000, oy = 550000;
            Func<double, double, double> g = (x, y) => 50.0;
            var cases = new (string Name, double[][] Xy)[]
            {
                ("ㄱ자", new[] { new[] { ox, oy }, new[] { ox + 10, oy }, new[] { ox + 10, oy + 10 } }),
                ("30°", new[] { new[] { ox, oy }, new[] { ox + 10, oy }, new[] { ox + 10 + 10 * Math.Cos(Math.PI / 6), oy + 10 * Math.Sin(Math.PI / 6) } }),
                ("곧은", new[] { new[] { ox, oy }, new[] { ox + 3, oy + 4 } }),
            };
            bool all = true; string at = "";
            foreach (var (nm, xy) in cases)
                foreach (double drop in new[] { 0.0, 0.1 })   // 평탄 = 부피 정확(1e-6) · 0.5 % 경사 = 꺾인 마디 사이 면이 살짝 비틀려 세모로 → 1 % 안
                {
                    var d = Line(xy, g);
                    d.InvertAuto = false; d.StartInvert = 49.7; d.EndInvert = 49.7 - drop;
                    var sols = UDitch.Meshes(d, S, out string why);
                    var q = UDitch.Qty(d);
                    bool closed = sols.Count == 2 && sols.All(Closed);
                    double vb = sols.Count > 0 ? sols[0].Volume : 0, vf = sols.Count > 1 ? sols[1].Volume : 0;
                    double zmin = sols.Count > 0 ? sols[0].V.Min(v => v.Z) : 0, zmax = sols.Count > 0 ? sols[0].V.Max(v => v.Z) : 0;
                    bool bent = xy.Length > 2;
                    double tol = drop == 0 || !bent ? 1e-6 : 0.01;
                    bool ok = why == "" && closed && Math.Abs(vb - q.BodyVol) <= tol * Math.Max(1, q.BodyVol) && Math.Abs(vf - q.FootVol) <= tol * Math.Max(1, q.FootVol)
                              && Math.Abs(q.BodyArea - 0.18) < 1e-12 && Math.Abs(q.FootArea - 0.08) < 1e-12
                              && Math.Abs(zmin - (49.7 - drop - 0.15)) < 1e-9 && Math.Abs(zmax - (49.7 + 0.3)) < 1e-9;
                    string tag = string.Format(ci, " [{0} 낙차 {1:F1}: 본체 {2:F6}/{3:F6} · 기초 {4:F6}/{5:F6}]", nm, drop, vb, q.BodyVol, vf, q.FootVol);
                    at += tag;
                    if (!ok) { all = false; at += string.Format(ci, "✗ {0} 닫힘 {1} z {2:F3}~{3:F3}", why, closed, zmin, zmax); }
                }
            var dd = Line(cases[0].Xy, g);
            var (pos, idx) = UDitch.SceneMesh(dd, S, ox, oy);
            // 본체 8각 · 기초 4각 — 옆면 (8+4)×2 마디 사이 네모 = 세모 2개씩 + 마구리 (6+2)×2
            int wantTri = (8 + 4) * 2 * 2 + (6 + 2) * 2;
            bool okScene = idx.Length == wantTri * 3 && pos.Length == (8 + 4) * 3 * 3 && pos.Max(Math.Abs) < 100;
            var bad = Line(new[] { new[] { 0.0, 0.0 }, new[] { 10.0, 0.0 } }, (x, y) => double.NaN);
            bool okBad = UDitch.Meshes(bad, S, out string wb).Count == 0 && wb.Contains("바닥고");
            check("S260 UD⑦ 3D 덩어리 — ㄱ자 · 30° · 곧은(대좌표) 본체 · 기초 닫힘 · 부피 = 단면적 × 중심선 길이(평탄 · 곧은 1e-6 · 경사 꺾임 1 % 안) · 바닥 · 윗면 50.00 · 장면 세모 수 · 바닥고 없으면 안 지음",
                  all && okScene && okBad, at + $" 장면 세모 {idx.Length / 3}/{wantTri} · 점 {pos.Length / 3} · 안 지음 «{wb}»");
        }

        // ⑧ 보고서 · 풍선 — 강우 출처 · 식 · 산근 · 자료 미확보 · 모델과 수리 따로
        {
            var d = Line(new[] { new[] { 0.0, 0.0 }, new[] { 100.0, 0.0 } }, (x, y) => 50 - 0.005 * x);
            d.AreaHa = 0.5; d.Id = "D-001";
            var rec = new UDitch.Record { Rain = new UDitch.Rain { ReturnYears = 30, DurationMin = 10, IntensityMmh = 100, Source = "지어낸 표", SourceVersion = "2026", Method = "CSV" }, Updated = "2026-11-11 00:00" };
            rec.Ditches.Add(d);
            string t = UDitch.Report(rec, S, "시험.dwg", "DH.Grading 시험");
            var rows = UDitch.InfoRows(d, rec.Rain, S);
            rec.Rain = new UDitch.Rain();
            string t0 = UDitch.Report(rec, S, "시험.dwg", "");
            var rows0 = UDitch.InfoRows(d, rec.Rain, S);
            bool ok = t.Contains("지어낸 표") && t.Contains("Q = C·I·A/360 = 0.90 × 100.0 × 0.5000 / 360 = 0.1250") && t.Contains("판정: 부족") && t.Contains("본체 0.1800 ㎡ × 100.000 m = 18.000 ㎥")
                      && t.Contains("합 26.000 ㎥") && t.Contains("수리(모델과 따로)")
                      && rows.Any(r => r.K == "판정" && r.V.StartsWith("부족")) && rows.Any(r => r.K == "추천 규격" && r.V == "400×400") && rows.Any(r => r.K == "콘크리트" && r.V == "26.00 ㎥")
                      && t0.Contains("자료 미확보 — 수리검토 안 함") && t0.Contains("강우자료 없음") && rows0.Any(r => r.K == "판정" && r.V == "강우자료 없음");
            check("S260 UD⑧ 보고서 · 풍선 — 출처 · 합리식 산근 · 부족 · 수량 산근(본체 18.000 · 합 26.000 ㎥) · 추천 · 자료 미확보면 «강우자료 없음»", ok, ok ? "" : t + "\n" + t0);
        }
    }

    /// <summary>닫힌 다양체 — 모든 변이 반대 방향으로 정확히 한 번씩.</summary>
    static bool Closed(UDitch.Solid s)
    {
        var e = new Dictionary<(int, int), int>();
        foreach (var f in s.F)
            for (int i = 0; i < f.Length; i++)
            {
                var k = (f[i], f[(i + 1) % f.Length]);
                e[k] = e.TryGetValue(k, out int c) ? c + 1 : 1;
            }
        return e.All(kv => kv.Value == 1 && e.TryGetValue((kv.Key.Item2, kv.Key.Item1), out int r) && r == 1);
    }
}
