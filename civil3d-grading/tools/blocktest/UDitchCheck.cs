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

        // ⑨ ★[설치 전 검토 1] [방향 바꾸기] → [모델 생성](원 선을 그린 차례로 다시 읽음 = UDitchStore.Refresh · FillGround와 같은 길) — 시작 = 상류 끝 유지
        //   그린 차례(x 0 → 100)는 오르막(지어낸 지표면) — 뒤집어야 내리막
        {
            var drawn = new[] { new[] { 0.0, 0.0 }, new[] { 100.0, 0.0 } };
            double G(double x, double y) => 50 + 0.01 * x;
            List<double[]> Reread() => drawn.Select(p => new[] { p[0], p[1], G(p[0], p[1]) }).ToList();
            void Rebuild(UDitch.Ditch x) { UDitch.SetLinePts(x, Reread()); UDitch.ApplyAuto(x, S); }
            // 자동 바닥고
            var a = Line(drawn, G);
            UDitch.Reverse(a); UDitch.ApplyAuto(a, S);
            double a0 = a.StartInvert, a1 = a.EndInvert;
            Rebuild(a);
            bool okA = a.Reversed && a.Pts[0][0] == 100 && Math.Abs(a.StartInvert - a0) < 1e-9 && Math.Abs(a.EndInvert - a1) < 1e-9 && a.StartInvert > a.EndInvert
                       && UDitch.ProfileOf(a, S).SlopePct > 0;
            // 손 바닥고(그린 차례로 친 값 47.0 → 47.8) — 뒤집으면 47.8 → 47.0 · 다시 읽어도 x = 100 끝이 47.8
            var m = Line(drawn, G);
            m.InvertAuto = false; m.StartInvert = 47.0; m.EndInvert = 47.8;
            UDitch.Reverse(m); UDitch.ApplyAuto(m, S);
            Rebuild(m);
            var pm = UDitch.ProfileOf(m, S);
            bool okM = m.Pts[0][0] == 100 && m.StartInvert == 47.8 && m.EndInvert == 47.0 && Math.Abs(pm.Inv[0] - 47.8) < 1e-12 && pm.SlopePct > 0;
            // 두 번 뒤집으면 그린 차례
            var b = Line(drawn, G);
            UDitch.Reverse(b); UDitch.Reverse(b); UDitch.ApplyAuto(b, S);
            Rebuild(b);
            bool okB = !b.Reversed && b.Pts[0][0] == 0 && b.StartInvert < b.EndInvert;
            // 기록 왕복 · 옛 기록(칸 없음) = 거짓 · 모르는 칸은 버림(옛 판이 새 기록을 읽을 때와 같음)
            var r = new UDitch.Record(); r.Ditches.Add(a);
            string js = UDitch.ToJson(r);
            bool okJ = js.Contains("\"Reversed\": true") && UDitch.TryParse(js, out var rb, out string wj) && rb.Ditches[0].Reversed
                       && UDitch.TryParse("{\"Version\": 1, \"Ditches\": [ { \"Id\": \"D-001\", \"Nope\": 1 } ]}", out var ro, out _) && !ro.Ditches[0].Reversed;
            check("S260 UD⑨ [방향 바꾸기] → 다시 읽기 — 자동 · 손 바닥고 모두 시작 = 상류(x 100) · 내리막 유지 · 두 번 = 그린 차례 · 기록 왕복 · 옛 기록 = 안 뒤집음",
                  okA && okM && okB && okJ,
                  string.Format(ci, "자동 {0} 손 {1}(첫 점 x {2} · {3:F3} → {4:F3}) 두 번 {5} 기록 {6}", okA, okM, m.Pts[0][0], m.StartInvert, m.EndInvert, okB, okJ));
        }

        // ── ★[v117.16 · JACK 1112 «애초에 폴리선이 단차 있게 만든 부지거든?»] 지표 따라감 · 낙차 · 단차(집수정 + 관로) · 스틸 그레이팅 — 좌표 · 높이 모두 지어낸 것
        double ox2 = 300000, oy2 = 450000;
        UDitch.Ditch Follow(double len, Func<double, double, double> g, bool follow = true)
        {
            var d = new UDitch.Ditch { Id = "D-010", Pts = new() { new[] { ox2, oy2, g(ox2, oy2) }, new[] { ox2 + len, oy2, g(ox2 + len, oy2) } }, Size = S.Sizes[0].Copy(), C = 0.9, Follow = follow };
            UDitch.FillSamples(d, g, S);
            UDitch.ApplyAuto(d, S);
            return d;
        }
        var rainF = new UDitch.Rain { ReturnYears = 30, DurationMin = 10, IntensityMmh = 100, Source = "지어냄" };
        double covP = Math.Max(S.Cover, S.PipeCover);
        // ★[v117.16 검토 HIGH] 관이 묻혔나 — 다리마다 40등분(끝 포함)해 «참» 지표 함수와 견줌: 관 윗면 + 흙덮개 − 지표 최대(≤ 0 = 묻힘)
        double PipeExcess(UDitch.Layout lay, Func<double, double, double> g)
        {
            double m = double.NegativeInfinity;
            foreach (var p in lay.Pipes)
                foreach (var l in p.Legs)
                    for (int k = 0; k <= 40; k++)
                    {
                        double t = k / 40.0, x = l.X1 + (l.X2 - l.X1) * t, y = l.Y1 + (l.Y2 - l.Y1) * t, z = l.Z1 + (l.Z2 - l.Z1) * t;
                        m = Math.Max(m, z + p.Dout / 2 + covP - g(x, y));
                    }
            return m;
        }
        string PipeText(UDitch.Layout lay) => string.Join(" ; ", lay.Pipes.Select(p => string.Format(ci, "관 {0:F3}→{1:F3} m · 다리 {2} · L {3:F3} m · 관 바닥 {4:F3}→{5:F3} · 덮개 여유 {6:F3} m [{7}]", p.S0, p.S1, p.Legs.Count, p.Length, p.InvIn, p.InvOut, -p.CrownExcess,
                                                                    string.Join(" | ", p.Legs.Select(l => string.Format(ci, "{0:F3}→{1:F3} z {2:F3}→{3:F3} {4:0.##}%", l.S0, l.S1, l.Z1, l.Z2, l.SlopePct)))))
                                                       .Concat(lay.Basins.Select(b => string.Format(ci, "집수정({0}) {1:F3}~{2:F3} 윗면 {3:F3} 안바닥 {4:F3}", b.Lower ? "아래" : "위", b.S0, b.S1, b.ZTop, b.Floor))));

        // ⑩ 곧은 선(점 2개 · 60 m)이 9 m 수직 단차(위 81.40 · 아래 72.50 평지)를 가로지름 → 집수정 둘(위 · 아래) · 관로 1(다리 여럿 · 위 땅 속으로 내려간 뒤 아래 부지 밑) · 구간 2 · 낙차 0
        //   ★[v117.16 검토 HIGH «관이 공중에»] 옛 판 = 관 하나가 1.06 m 수평에 8.75 m 내려가 단차 면에서 지표 위 약 4 m → 이제 관 윗면 + 흙덮개 ≤ 지표(다리마다 40등분 · 참 지표)
        {
            Func<double, double, double> g = (x, y) => x - ox2 < 30 ? 81.4 : 72.5;
            var d = Follow(60, g); d.AreaHa = 0.1;
            var lay = UDitch.LayoutOf(d, S);
            var pr = UDitch.ProfileOf(d, S);
            var sols = UDitch.Meshes(d, S, out string why);
            var h = UDitch.Check(d, rainF, S);
            var (bs, ps) = UDitch.SceneParts(d, S, ox2, oy2);
            var (pos, idx) = UDitch.SceneMesh(d, S, ox2, oy2, out var parts);
            string rep = UDitch.Report(new UDitch.Record { Rain = rainF, Ditches = { d } }, S, "시험.dwg", "");
            var b = lay.Basins.FirstOrDefault(q => !q.Lower); var dn = lay.Basins.FirstOrDefault(q => q.Lower); var p = lay.Pipes.FirstOrDefault();
            double outer = S.BasinInner + 2 * S.BasinWall;
            double ex = PipeExcess(lay, g);
            bool okL = lay.Follow && lay.Error == "" && lay.Segs.Count == 2 && lay.Drops.Count == 0 && lay.Basins.Count == 2 && lay.Pipes.Count == 1
                       && b != null && dn != null && p != null && p.Legs.Count >= 2
                       && Math.Abs(b.S1 - (30 - S.StepSetback)) < 0.1 && b.S1 <= 30 - S.StepSetback + 1e-6 && Math.Abs(b.S1 - b.S0 - outer) < 1e-9
                       && Math.Abs(dn.S0 - (30 + S.StepSetback)) < 1e-6 && Math.Abs(dn.S1 - dn.S0 - outer) < 1e-9
                       && Math.Abs(lay.Segs[0].S1 - b.S0) < 1e-9 && Math.Abs(lay.Segs[1].S0 - dn.S1) < 1e-9 && Math.Abs(p.S0 - b.S1) < 1e-9 && Math.Abs(p.S1 - dn.S0) < 1e-9
                       && lay.Segs.All(q => Math.Abs(q.SMinPct - S.MinSlopePct) < 1e-6 && Math.Abs(q.SMaxPct - S.MinSlopePct) < 1e-6)   // 평지 = 최소 경사로 묻힘
                       && Math.Abs(d.StartInvert - 81.1) < 1e-9 && Math.Abs(lay.Segs[1].V0 - 72.2) < 1e-9
                       && Math.Abs(b.ZTop - 81.4) < 1e-9 && b.Floor <= lay.Segs[0].V1 - S.BasinSump + 1e-9 && Math.Abs(b.Floor - p.InvIn) < 1e-9 && Math.Abs(b.ZBottom - (b.Floor - S.BasinFloor)) < 1e-9
                       && p.InvOut <= 72.2 + 1e-9 && Math.Abs(dn.InV - p.InvOut) < 1e-9 && Math.Abs(dn.OutV - 72.2) < 1e-9 && Math.Abs(dn.Floor - (Math.Min(p.InvOut, 72.2) - S.BasinSump)) < 1e-9 && Math.Abs(dn.ZTop - 72.5) < 1e-9
                       && p.Legs.All(l => l.Z1 >= l.Z2 && l.SlopePct >= S.MinSlopePct - 1e-6) && Math.Abs(p.Length - p.Legs.Sum(l => l.Length)) < 1e-12
                       && lay.PipeCrownExcess <= 1e-9 && p.CrownAbove < 0 && ex <= 1e-9 && !lay.Warn.Any(w => w.Contains("관로"))
                       && lay.MaxAbove <= 1e-9 && pr.Error == "" && !pr.Warn.Any(w => w.Contains("솟은"))
                       && lay.Log.Any(l => l.Contains("단차(집수정 + 관로)"));
            bool okS = why == "" && sols.Count == 2 * 2 + 2 * 2 + p!.Legs.Count && sols.All(Closed) && sols.Count(q => q.Part == "집수정") == 2 && sols.Count(q => q.Part == "집수정 덮개") == 2
                       && sols.Count(q => q.Part == "관로") == p.Legs.Count && sols.All(q => q.Volume > 0);
            int meshTri = sols.Where(q => q.Part is "본체" or "기초").Sum(q => q.F.Sum(f => f.Length - 2));
            var bv = bs.Count > 0 ? bs[0].V : new double[8]; var pv = ps.Count > 0 ? ps[0].V : new double[7];
            var l0 = p.Legs[0];
            bool okH = bs.Count == 2 && ps.Count == p.Legs.Count && Math.Abs(bv[0] - (b!.X - ox2)) < 1e-9 && Math.Abs(bv[1]) < 1e-9 && bv[2] == b.ZTop && bv[3] == b.ZBottom && bv[4] == outer && bv[5] == outer
                       && Math.Abs(bv[6]) < 1e-12 && bv[7] == S.BasinInner
                       && Math.Abs(pv[0] - (l0.X1 - ox2)) < 1e-9 && pv[2] == l0.Z1 && Math.Abs(pv[3] - (l0.X2 - ox2)) < 1e-9 && pv[5] == l0.Z2 && Math.Abs(pv[6] - (S.PipeD + 2 * S.PipeWall)) < 1e-12
                       && ps[0].Rows.Any(x => x.K == "지름" && x.V.StartsWith("D300")) && ps[0].Rows.Any(x => x.K == "판정") == false && bs[0].Name == "D-010 집수정 1(위)" && bs[1].Name == "D-010 집수정 2(아래)"
                       && idx.Length == meshTri * 3 && pos.Max(Math.Abs) < 100
                       && parts.Count == 4 && parts.Select(q => q.Seg).SequenceEqual(new[] { 1, 1, 2, 2 }) && parts.Sum(q => q.N) * 3 == pos.Length && parts[2].V0 == parts[0].N + parts[1].N;
            var (_, ps2) = UDitch.SceneParts(d, S, ox2, oy2, rainF);
            okH &= ps2.Count == p.Legs.Count && ps2.All(q => q.Rows.Any(x => x.K == "판정" && x.V == "OK"));
            var pc = h.Pipes.FirstOrDefault();
            bool okQ = h.Segs.Count == 2 && h.Pipes.Count == 1 && pc != null && pc.Verdict == UDitch.Verdict.Ok && h.Segs.All(c => Math.Abs(c.Qcap - UDitch.ManningQ(0.3, 0.24, S.MinSlopePct / 100, S.ManningN)) < 1e-9)
                       && Math.Abs(pc.SFlat - p.SlopeMinPct / 100) < 1e-12 && Math.Abs(pc.Qfull - UDitch.PipeFullQ(S.PipeD, pc.SFlat, S.ManningN)) < 1e-12 && pc.FallLegs >= 1 && pc.VelMax > 0
                       && rep.Contains("짜임: 구간 2 · 낙차 0") && rep.Contains("집수정 1(위): 측점") && rep.Contains("집수정 2(아래): 측점") && rep.Contains("관로 1: 측점") && rep.Contains("다리 1-1:") && rep.Contains("(낙하)")
                       && rep.Contains("관로 1: 가득 흐름") && rep.Contains("집수정 2 · 관로");
            // 거꾸로(아래 → 위) — 지표가 솟음 → 묻힘 알림(집수정 안 만듦)
            var r = Follow(60, g); UDitch.Reverse(r); UDitch.ApplyAuto(r, S);
            var rl = UDitch.LayoutOf(r, S); var rp = UDitch.ProfileOf(r, S);
            bool okR = rl.Basins.Count == 0 && rp.Warn.Any(w => w.Contains("솟은")) && Math.Abs(r.Samp[0][1] - 72.5) < 1e-9 && r.Samp[0][0] == 0;
            check("S260 UD⑩ 곧은 선이 9 m 수직 단차 → 집수정 둘(위 · 아래) · 관로 1(위 땅 속으로 낙하 → 아래 부지 밑 · 관 윗면 + 흙덮개 ≤ 지표 · 관구 ≤ 아래 U 바닥) · 구간 2(평지 = 최소 경사) · 솔리드 닫힘 · 3D 칸 · 수리(완만한 다리 · 급한 다리) · 보고서 · 거꾸로 = 솟음 알림",
                  okL && okS && okH && okQ && okR,
                  string.Format(ci, "짜임 {0} 솔리드 {1}({2}) 머리 {3} 수리 {4}(Qfull {5:F4} · V {6:F2}) 거꾸로 {7} · 참 지표 덮개 여유 {8:F3} m · {9}",
                                okL, okS, why, okH, okQ, pc?.Qfull, pc?.VelMax, okR, -ex, PipeText(lay)));
        }

        // ⑪ 25 % 내리막 40 m(단차 없음) → 낙차 여러 개 · 구간마다 ≤ 10 % · 낙차 ≤ 0.5 m · 측구 윗면이 지표 위로 안 나옴 · 낙차 합 + 구간 낙하 = 시점 − 종점 · 낙차벽 솔리드
        {
            Func<double, double, double> g = (x, y) => 80 - 0.25 * (x - ox2);
            var d = Follow(40, g);
            var lay = UDitch.LayoutOf(d, S);
            var sols = UDitch.Meshes(d, S, out string why);
            double fall = lay.Drops.Sum(x => x.H) + lay.Segs.Sum(q => q.V0 - q.V1);
            bool ok = lay.Follow && lay.Error == "" && lay.Basins.Count == 0 && lay.Pipes.Count == 0 && lay.Drops.Count >= 5 && lay.Segs.Count == lay.Drops.Count + 1
                      && lay.Segs.All(q => q.SMaxPct <= S.MaxSlopePct + 1e-6 && q.SMinPct >= S.MinSlopePct - 1e-6)
                      && lay.Drops.All(x => x.H <= S.DropMax + 1e-9 && x.H >= S.DropMin - 1e-9)
                      && lay.MaxAbove <= 1e-6 && lay.MaxBelow <= S.DropMax + 1e-6
                      && Math.Abs(fall - (lay.StartInv - lay.EndInv)) < 1e-6
                      && Math.Abs(lay.EndInv - (g(ox2 + 40, 0) - S.Cover - 0.3)) < 1e-6
                      && why == "" && sols.All(Closed) && sols.Count(q => q.Part == "낙차벽") == lay.Drops.Count && !lay.Log.Any(l => l.Contains("단차("));
            // ★[v117.16 검토 MEDIUM] U 덩어리 측점 사이 ≥ 2 mm(1~2 mm 조각 없음 — 솔리드 바꾸기 · 뷰어 구멍)
            double minGap = double.PositiveInfinity;
            foreach (var q in sols.Where(q => q.Part == "본체"))
                for (int j = 0; j + 1 < q.V.Count / 8; j++)
                {
                    double ax = 0, ay = 0, bx = 0, by = 0;
                    for (int i = 0; i < 8; i++) { ax += q.V[j * 8 + i].X; ay += q.V[j * 8 + i].Y; bx += q.V[(j + 1) * 8 + i].X; by += q.V[(j + 1) * 8 + i].Y; }
                    minGap = Math.Min(minGap, Math.Sqrt((ax - bx) * (ax - bx) + (ay - by) * (ay - by)) / 8);
                }
            ok &= minGap >= 2e-3 - 1e-9;
            var old = Follow(40, g, follow: false);
            var po = UDitch.ProfileOf(old, S);
            bool okOld = UDitch.LayoutOf(old, S).Segs.Count == 1 && Math.Abs(po.SlopePct - 25) < 1e-6 && po.Warn.Any(w => w.Contains("최대 경사"));
            check("S260 UD⑪ 25 % 내리막(단차 없음) → 낙차(≤ 0.5 m) · 구간마다 0.3~10 % · 윗면이 지표 위로 안 나옴 · 묻힘 ≤ 0.5 m · 낙차 합 + 구간 낙하 = 시점 − 종점 · 끝 = 지표에 맞음 · 낙차벽 · 옛 방식은 한 경사 25 %",
                  ok && okOld,
                  string.Format(ci, "구간 {0} 낙차 {1}(합 {2:F3}) 경사 {3:F3}~{4:F3} % 위로 {5:F6} 묻힘 {6:F3} 낙하 {7:F6}/{8:F6} 끝 {9:F3} 솔리드 {10}({11}) 옛 {12}",
                                lay.Segs.Count, lay.Drops.Count, lay.Drops.Sum(x => x.H), lay.Segs.Min(q => q.SMinPct), lay.Segs.Max(q => q.SMaxPct), lay.MaxAbove, lay.MaxBelow,
                                fall, lay.StartInv - lay.EndInv, lay.EndInv, sols.Count, why, okOld));
        }

        // ⑫ 1 % 완만 50 m — 지표 따라감도 한 구간 · 바닥고 · 수리 · 메시가 옛 방식과 같음(뒤로 맞음) · 옛 기록(새 칸 없음) = 한 경사 · 새 칸 왕복
        {
            Func<double, double, double> g = (x, y) => 50 - 0.01 * (x - ox2);
            var f = Follow(50, g); f.AreaHa = 0.3;
            var o = Follow(50, g, follow: false); o.AreaHa = 0.3;
            var lf = UDitch.LayoutOf(f, S);
            var hf = UDitch.Check(f, rainF, S); var ho = UDitch.Check(o, rainF, S);
            var mf = UDitch.Meshes(f, S, out _); var mo = UDitch.Meshes(o, S, out _);
            bool same = lf.Follow && lf.Segs.Count == 1 && lf.Drops.Count == 0 && lf.Basins.Count == 0
                        && Math.Abs(f.StartInvert - o.StartInvert) < 1e-9 && Math.Abs(f.EndInvert - o.EndInvert) < 1e-9
                        && hf.Verdict == ho.Verdict && hf.Status == ho.Status && Math.Abs(hf.Q - ho.Q) < 1e-12 && Math.Abs(hf.Qcap - ho.Qcap) < 1e-6 * ho.Qcap && Math.Abs(hf.Vel - ho.Vel) < 1e-6 * ho.Vel
                        && mf.Count == mo.Count && mf.Zip(mo).All(t => Math.Abs(t.First.Volume - t.Second.Volume) < 1e-6 && t.First.V.Count == t.Second.V.Count)
                        && Math.Abs(UDitch.Qty(f, S).Length - UDitch.Qty(o).Length) < 1e-9;
            var r = new UDitch.Record(); r.Ditches.Add(f);
            string js = UDitch.ToJson(r);
            bool okJ = js.Contains("\"Follow\": true") && UDitch.TryParse(js, out var rb, out _) && rb.Ditches[0].Follow && rb.Ditches[0].Samp.Count == f.Samp.Count
                       && UDitch.LayoutOf(rb.Ditches[0], S).Segs.Count == 1;
            // ★[v117.16 검토 LOW] 표본은 한 줄로 · NaN 왕복
            int si = js.IndexOf("\"Samp\": [[", StringComparison.Ordinal), se = si < 0 ? -1 : js.IndexOf("]]", si, StringComparison.Ordinal);
            bool okP = UDitch.TryParse(js, out var rb2, out _);
            okJ &= okP && si >= 0 && se > si && !js.Substring(si, se - si).Contains('\n') && rb2.Ditches[0].Samp.Zip(f.Samp).All(t => t.First[0] == t.Second[0] && t.First[1] == t.Second[1]);
            var rn = new UDitch.Record(); rn.Ditches.Add(new UDitch.Ditch { Samp = new() { new[] { 0.0, double.NaN }, new[] { 1.0, 2.5 } } });
            string jn = UDitch.ToJson(rn);
            okJ &= jn.Contains("[[0,\"NaN\"],[1,2.5]]") && UDitch.TryParse(jn, out var rnb, out _) && double.IsNaN(rnb.Ditches[0].Samp[0][1]) && rnb.Ditches[0].Samp[1][1] == 2.5;
            // 옛 기록(v117.15까지 — Follow · Samp · Grating 칸 없음)
            bool okOld = UDitch.TryParse("{\"Version\": 1, \"Ditches\": [ { \"Id\": \"D-001\", \"Pts\": [[0, 0, 50], [100, 0, 49]], \"StartInvert\": 49.7, \"EndInvert\": 48.7, \"InvertAuto\": true } ]}", out var ro, out _)
                         && !ro.Ditches[0].Follow && !ro.Ditches[0].Grating && ro.Ditches[0].Samp.Count == 0
                         && UDitch.ProfileOf(ro.Ditches[0], S).Lay == null && UDitch.LayoutOf(ro.Ditches[0], S).Segs.Count == 1 && Math.Abs(UDitch.ProfileOf(ro.Ditches[0], S).SlopePct - 1) < 1e-9;
            check("S260 UD⑫ 1 % 완만 — 지표 따라감 = 한 구간 · 바닥고 · 수리(Q · 통수능 · 유속 · 판정) · 메시 · 길이 = 옛 방식 · Follow · 표본 왕복 · 옛 기록 = 한 경사(지표 따라감 아님)",
                  same && okJ && okOld,
                  string.Format(ci, "같음 {0}(구간 {1} · {2:F3}/{3:F3} → {4:F3}/{5:F3} · Qcap {6:F5}/{7:F5}) 왕복 {8} 옛 {9}", same, lf.Segs.Count, f.StartInvert, o.StartInvert, f.EndInvert, o.EndInvert, hf.Qcap, ho.Qcap, okJ, okOld));
        }

        // ⑬ ★[JACK 1112 «측구에 스틸그라우팅도 선택되게»] 덮개 — 기록 왕복 · 판 솔리드(안폭 × 두께 · 윗면 = 벽 윗면) · 보고서 · 풍선 줄 · 구간마다 · 옛 기록 = 없음
        {
            var d = Line(new[] { new[] { 0.0, 0.0 }, new[] { 10.0, 0.0 } }, (x, y) => 50);
            d.Grating = true;
            var r = new UDitch.Record(); r.Ditches.Add(d);
            string js = UDitch.ToJson(r);
            bool okJ = js.Contains("\"Grating\": true") && UDitch.TryParse(js, out var rb, out _) && rb.Ditches[0].Grating
                       && UDitch.TryParse("{\"Version\": 1, \"Ditches\": [ { \"Id\": \"D-001\" } ]}", out var ro, out _) && !ro.Ditches[0].Grating;
            var sols = UDitch.Meshes(d, S, out string why);
            var gr = sols.FirstOrDefault(q => q.Part == "그레이팅");
            bool okS = why == "" && sols.Count == 3 && sols.All(Closed) && gr != null && Math.Abs(gr.Volume - 0.3 * S.GrateT * 10) < 1e-9
                       && Math.Abs(gr.V.Max(v => v.Z) - (d.StartInvert + 0.3)) < 1e-9 && Math.Abs(gr.V.Min(v => v.Z) - (d.StartInvert + 0.3 - S.GrateT)) < 1e-9
                       && Math.Abs(gr.V.Max(v => v.Y) - 0.15) < 1e-9;
            string rep = UDitch.Report(r, S, "시험.dwg", "");
            var rows = UDitch.InfoRows(d, new UDitch.Rain(), S);
            var (_, gi1) = UDitch.SceneMesh(d, S, 0, 0);
            d.Grating = false;
            string rep0 = UDitch.Report(r, S, "시험.dwg", "");
            var (_, gi0) = UDitch.SceneMesh(d, S, 0, 0);
            okS &= gi1.Length == gi0.Length && gi1.Length > 0;   // 3D 메시에는 판 없음 — 뷰어가 grating 칸으로 그림
            // 단차 측구에 덮개 — 구간마다 판 · 길이 = U 길이(집수정 · 관 자리 뺌)
            var st = Follow(60, (x, y) => x - ox2 < 30 ? 81.4 : 72.5); st.Grating = true;
            var ls = UDitch.LayoutOf(st, S);
            var ms = UDitch.Meshes(st, S, out _);
            string reps = UDitch.Report(new UDitch.Record { Ditches = { st } }, S, "시험.dwg", "");
            var ex = UDitch.Extras(st, S);
            bool okF = ms.Count(q => q.Part == "그레이팅") == 2 && Math.Abs(ex.GrateLen - ls.ULength) < 1e-9 && ls.ULength < 60 - 1
                       && reps.Contains("덮개: 스틸 그레이팅 · 구간 2개") && reps.Contains("(구간 1 ") && reps.Contains("스틸 그레이팅 " + ex.GrateLen.ToString("0.00", ci) + " m");
            bool okR = rep.Contains("덮개: 스틸 그레이팅 · 구간 1개 · 길이 10.000 m") && rows.Any(x => x.K == "덮개" && x.V == "스틸 그레이팅 10.00 m") && rep0.Contains("덮개: 없음");
            check("S260 UD⑬ 스틸 그레이팅 — 기록 왕복 · 옛 기록 = 없음 · 판 솔리드(0.3 × 0.05 × 10 m · 윗면 = 벽 윗면) 닫힘 · 보고서 «덮개: 스틸 그레이팅» · 풍선 줄 · 단차 측구 = 구간마다 판 · 길이 = U 길이",
                  okJ && okS && okR && okF,
                  string.Format(ci, "왕복 {0} 솔리드 {1}({2} · {3:F6}) 보고서 {4} 단차 {5}(판 {6} · {7:F3}/{8:F3})", okJ, okS, why, gr?.Volume, okR, okF, ms.Count(q => q.Part == "그레이팅"), ex.GrateLen, ls.ULength));
        }

        // ⑭ ★[v117.16 검토 HIGH] 1:1.5 법면 단차(위 81.40 · 법면 13.35 m · 아래 72.50) — 옛 판 = 관이 법면 위로 최대 1.5 m 드러남 · 끝에서 0.26 m 위 → 이제 법면 밑을 흙덮개 두고 따라 내려감
        {
            Func<double, double, double> g = (x, y) => { double t = x - ox2; return t < 30 ? 81.4 : t > 30 + 8.9 * 1.5 ? 72.5 : 81.4 - (t - 30) / 1.5; };
            var d = Follow(80, g); d.AreaHa = 0.1;
            var lay = UDitch.LayoutOf(d, S);
            var sols = UDitch.Meshes(d, S, out string why);
            var p = lay.Pipes.FirstOrDefault();
            double ex = PipeExcess(lay, g);
            bool ok = lay.Follow && lay.Error == "" && lay.Basins.Count == 2 && lay.Pipes.Count == 1 && p != null && p.Legs.Count >= 2
                      && lay.PipeCrownExcess <= 1e-9 && ex <= 1e-9 && !lay.Warn.Any(w => w.Contains("관로"))
                      && p.Legs.All(l => l.Z1 >= l.Z2 && l.SlopePct >= S.MinSlopePct - 1e-6) && p.InvOut <= lay.Segs[^1].V0 + 1e-9
                      && why == "" && sols.All(Closed) && lay.MaxAbove <= 1e-9;
            check("S260 UD⑭ 1:1.5 법면 단차 → 집수정 둘 · 관로가 법면 밑을 따라(관 윗면 + 흙덮개 ≤ 지표 · 다리마다 40등분 · 참 지표) · 최소 경사 이상 · 솔리드 닫힘",
                  ok, string.Format(ci, "참 지표 덮개 여유 {0:F3} m · {1} · 경고 [{2}]", -ex, PipeText(lay), string.Join(" / ", lay.Warn)));
        }

        // ⑮ ★[v117.16 검토 HIGH] 단차 둘이 1.5 m 사이(90 → 88 → 86) — 관 하나로 이음 · 가운데 부지 밑으로 묻혀 지나감(옛 판은 검사 없이 가로지름)
        {
            Func<double, double, double> g = (x, y) => { double t = x - ox2; return t < 20 ? 90 : t < 21.5 ? 88 : 86; };
            var d = Follow(40, g); d.AreaHa = 0.1;
            var lay = UDitch.LayoutOf(d, S);
            var sols = UDitch.Meshes(d, S, out string why);
            var p = lay.Pipes.FirstOrDefault();
            double ex = PipeExcess(lay, g);
            bool ok = lay.Follow && lay.Error == "" && lay.Basins.Count == 2 && lay.Pipes.Count == 1 && lay.Segs.Count == 2 && p != null
                      && lay.Log.Any(l => l.Contains("관 하나로")) && p.S0 < 20 && p.S1 > 21.5
                      && lay.PipeCrownExcess <= 1e-9 && ex <= 1e-9 && !lay.Warn.Any(w => w.Contains("관로"))
                      && p.Legs.All(l => l.Z1 >= l.Z2) && why == "" && sols.All(Closed);
            check("S260 UD⑮ 단차 둘(1.5 m 사이) → 관 하나로 이음 · 가운데 부지 밑으로 묻힘(관 윗면 + 흙덮개 ≤ 지표) · 집수정 둘 · 구간 2",
                  ok, string.Format(ci, "참 지표 덮개 여유 {0:F3} m · {1} · {2}", -ex, PipeText(lay), string.Join(" / ", lay.Log.Where(l => l.Contains("단차")))));
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
