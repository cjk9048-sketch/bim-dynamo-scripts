// ── S170 ★★★[v104 B · 계획_v104_B.md 2판 · JACK 1021 «터파기 끝에서 지정 경사로 내려오다 옹벽 꼭대기 높이에서 평탄»] 옹벽 뒷채움 배면사면 ──
//   해석 B(곧은 벽 · 손셈) · 앞쪽 · 터파기 밖 · 선 끝 너머(T가 1:n으로 올라 땅에 닿음) · 가파른 땅 · 평탄 없음 ⚠ · 터파기 1:1.8(B ≥ E) · 설계 사면(정지면 ≠ 원지반)
//   · 끊김 없음(|ΔB| ≤ h/n — 높이가 다른 데이라잇 둘) · 기록 왕복 · 그림용 격자 면 = 해석 B · 데이라잇 뽑기
//   만든 꼴(현장 값 없음 — 공개 저장소)
using DH.Grading.Core;

static class WallBackCheck
{
    public static void Run(Action<string, bool, string> check0)
    {
        void check(string name, bool ok, string detail = "") => check0(name, ok, detail);
        Console.WriteLine("\n== S170 옹벽 뒷채움 배면사면 — 해석 높이 · 구역 · 끊김 · 기록 · 그림용 면 · 데이라잇 ==");
        // 벽: (0,0)→(20,0) · 흙 +y · 꼭대기 105 · 데이라잇 y = 8 · z 110 · n 1.5
        // 땅 L = 105 + 0.625y(정지면 = 원지반) · 터파기 E = 100 + 1.25y(y = 8에서 땅과 만남)
        var rec = new WallExcav.BackRecord { N = 1.5 };
        rec.Walls.Add(new WallExcav.BackWall { Soil = +1, Pts = Enumerable.Range(0, 41).Select(i => new Point3(i * 0.5, 0, 105)).ToList() });
        rec.Daylight.Add(Enumerable.Range(0, 49).Select(i => new Point3(-2 + i * 0.5, 8, 110)).ToList());
        double Lg(double x, double y) => 105 + 0.625 * Math.Max(0, y);
        double Ex(double x, double y) => 100 + 1.25 * Math.Max(0, y);
        double Bat(WallExcav.BackRecord r, double x, double y, out bool ins) => WallExcav.BackAt(r, x, y, Lg(x, y), Lg(x, y), Ex(x, y), out ins, out _);
        // 손셈: D = 110 − (8 − y)/1.5 · 평탄 끝 y = 0.5(D = 105) · D &lt; L(y &lt; 8)
        double b1 = Bat(rec, 10, 0.25, out bool i1), b2 = Bat(rec, 10, 4, out bool i2), b3 = Bat(rec, 10, 7.5, out bool i3);
        check("S170 ① 곧은 벽 — 벽 곁 평탄 105 · y 4 = 110 − 4/1.5 · y 7.5 = 110 − 0.5/1.5",
            i1 && i2 && i3 && Math.Abs(b1 - 105) < 1e-9 && Math.Abs(b2 - (110 - 4 / 1.5)) < 1e-9 && Math.Abs(b3 - (110 - 0.5 / 1.5)) < 1e-9, $"{b1:F4} · {b2:F4} · {b3:F4}");
        Bat(rec, 10, -1, out bool iF); Bat(rec, 10, 9, out bool iO);
        double bE1 = Bat(rec, 21, 4, out bool iE1); Bat(rec, 25, 4, out bool iE5);
        check("S170 ① 구역 — 앞쪽 · 터파기 밖은 Ω 밖 · 선 끝 너머는 T가 1:n으로 올라(x 21: T 105.67 &lt; D) 안 · x 25(T 108.3 &gt; 땅 107.5)는 땅과 같아 밖",
            !iF && !iO && iE1 && Math.Abs(bE1 - (110 - 4 / 1.5)) < 1e-9 && !iE5, $"{iF} {iO} · x21 {bE1:F4}/{iE1} · x25 {iE5}");
        // 가파른 땅(1:0.5) — 경사면이 어디서나 땅보다 높다 → 배면 = 땅(Ω 밖)
        var recS = new WallExcav.BackRecord { N = 1.5, Walls = rec.Walls };
        recS.Daylight.Add(Enumerable.Range(0, 49).Select(i => new Point3(-2 + i * 0.5, 8, 121)).ToList());
        double bS = WallExcav.BackAt(recS, 10, 4, 113, 113, 100.4, out bool iS, out _);
        check("S170 ② 가파른 땅(1:0.5) — 배면 = 땅(Ω 밖 · 되메우기는 땅까지)", !iS && double.IsNaN(bS), $"{bS}");
        // 데이라잇이 높아(116) 벽 곁에서도 경사면이 꼭대기(105)보다 높고 땅(112)보다 낮다 — 평탄 없음 ⚠
        var recH = new WallExcav.BackRecord { N = 1.5, Walls = rec.Walls };
        recH.Daylight.Add(Enumerable.Range(0, 49).Select(i => new Point3(-2 + i * 0.5, 8, 116)).ToList());
        double bF = WallExcav.BackAt(recH, 10, 0.3, 112.0, 112.0, 100.3, out bool iFh, out bool fh2);
        check("S170 ② 벽 곁 경사가 꼭대기보다 높음 — 평탄 없음 ⚠ · B = 경사면", iFh && fh2 && Math.Abs(bF - (116 - 7.7 / 1.5)) < 1e-9, $"{bF:F4} · ⚠{fh2}");
        // 터파기 1:1.8(배면 1:1.5보다 완만) — 경사면이 터파기선 아래로 가는 자리는 B = E(되메우기 0 · 검토 높음 1)
        double E18(double y) => 110 - (8 - y) / 1.8;
        double L25(double y) => 110 - (8 - y) * 0.4;     // 땅 1:2.5(터파기보다 완만 — E &lt; L)
        double b18 = WallExcav.BackAt(rec, 10, 6, L25(6), L25(6), E18(6), out bool i18, out _);
        check("S170 ③ 터파기 1:1.8 — B = max(E, …) = 터파기선(경사면 110 − 2/1.5 &lt; 110 − 2/1.8)", i18 && Math.Abs(b18 - E18(6)) < 1e-9, $"{b18:F4} / E {E18(6):F4}");
        // 설계 사면 — 정지면이 원지반보다 낮으면(벽 뒤 설계 절토) 배면을 안 씌운다(그 면이 되메우기 윗면)
        double bD = WallExcav.BackAt(rec, 10, 4, Lg(10, 4) - 0.5, Lg(10, 4), Ex(10, 4), out bool iD, out _);
        check("S170 ③ 정지면 ≠ 원지반(설계 사면) — Ω 밖", !iD && double.IsNaN(bD), $"{bD}");
        // 끊김 없음 — 높이가 다른 데이라잇 둘(뒤 110 · 끝을 감싼 115) · 격자 0.25마다 |ΔB| ≤ 0.25/1.5 + 1e-9
        var recC = new WallExcav.BackRecord { N = 1.5, Walls = rec.Walls };
        recC.Daylight.Add(Enumerable.Range(0, 49).Select(i => new Point3(-2 + i * 0.5, 8, 110)).ToList());
        recC.Daylight.Add(Enumerable.Range(0, 17).Select(i => new Point3(22, 8 - i * 0.5, 115)).ToList());
        double worstJump = 0; string jat = "";
        for (double x = 0; x < 21.9; x += 0.25)
            for (double y = 0.25; y < 7.9; y += 0.25)
            {
                double ba = WallExcav.BackAt(recC, x, y, 130, 130, 90, out bool ia, out _);
                double bb = WallExcav.BackAt(recC, x + 0.25, y, 130, 130, 90, out bool ib, out _);
                double bc = WallExcav.BackAt(recC, x, y + 0.25, 130, 130, 90, out bool ic, out _);
                if (ia && ib && Math.Abs(ba - bb) > worstJump) { worstJump = Math.Abs(ba - bb); jat = $"({x},{y})→x"; }
                if (ia && ic && Math.Abs(ba - bc) > worstJump) { worstJump = Math.Abs(ba - bc); jat = $"({x},{y})→y"; }
            }
        check("S170 ④ 끊김 없음 — 높이가 다른 데이라잇 둘 · 이웃 격자 |ΔB| ≤ 0.25/1.5(원뿔 위 덮개 · 끝 너머 T 오름)", worstJump <= 0.25 / 1.5 + 1e-9, $"최대 {worstJump:F4} @{jat}");
        // 기록 왕복 · 틀린 기록
        var enc = WallExcav.EncodeBack(rec);
        bool ok = WallExcav.TryDecodeBack(enc, out var back, out string why);
        check("S170 ⑤ 기록 왕복", ok && back.N == 1.5 && back.Walls.Count == 1 && back.Walls[0].Pts.SequenceEqual(rec.Walls[0].Pts) && back.Daylight[0].SequenceEqual(rec.Daylight[0]) && back.Walls[0].Soil == 1, why);
        var bad = new List<(char, object)>(enc); bad[0] = ('I', 9);
        check("S170 ⑤ 모르는 판은 거짓 + 까닭", !WallExcav.TryDecodeBack(bad, out _, out string w2) && w2.Contains("판"), w2);
        // 그림용 격자 면 — 안쪽 표본에서 해석 B와 같음 · 앞쪽 · 데이라잇 너머에는 세모 없음
        var tris = WallExcav.BackSurface(rec, (x, y) => Lg(x, y), (x, y) => Lg(x, y), (x, y) => Ex(x, y), 0.5, out string note, out _);
        double worst = 0; int n = 0;
        var rnd = new Random(1021);
        for (int s = 0; s < 2000; s++)
        {
            double x = 1 + rnd.NextDouble() * 18, y = 1.0 + rnd.NextDouble() * 6.4;
            double? zt = null;
            foreach (var t in tris)
            {
                double d = (t.B.Y - t.C.Y) * (t.A.X - t.C.X) + (t.C.X - t.B.X) * (t.A.Y - t.C.Y);
                double u = ((t.B.Y - t.C.Y) * (x - t.C.X) + (t.C.X - t.B.X) * (y - t.C.Y)) / d, v = ((t.C.Y - t.A.Y) * (x - t.C.X) + (t.A.X - t.C.X) * (y - t.C.Y)) / d;
                if (u >= -1e-12 && v >= -1e-12 && 1 - u - v >= -1e-12) { zt = u * t.A.Z + v * t.B.Z + (1 - u - v) * t.C.Z; break; }
            }
            if (zt == null) continue;
            n++; worst = Math.Max(worst, Math.Abs(zt.Value - Bat(rec, x, y, out _)));
        }
        bool noFront = tris.All(t => t.A.Y >= -1e-9 && t.B.Y >= -1e-9 && t.C.Y >= -1e-9) && tris.All(t => t.A.Y <= 8 + 1e-9 && t.B.Y <= 8 + 1e-9 && t.C.Y <= 8 + 1e-9);
        check("S170 ⑥ 그림용 격자 면 — 표본 1,900점 넘게 덮음 · 해석 B와 1 mm 안 · 앞쪽 · 데이라잇 너머 세모 없음", n > 1900 && worst < 1e-3 && noFront, $"{note} · 표본 {n} · 최대 차 {worst * 1000:F3}mm");
        // 데이라잇 뽑기 — 터파기면(띠 −1 ~ 8 · 땅과 y = 8에서 만남)의 테두리 중 땅에 닿은 뒤쪽 변만(앞 y = −1 · 양 끝은 땅보다 낮아 빠짐)
        var exc = new List<WallDaylight.Tri>();
        for (double x = 0; x < 20; x += 1) for (double y = -1; y < 8; y += 1)
        {
            Point3 Q(double xx, double yy) => new(xx, yy, yy < 0 ? 100 : Ex(xx, yy));
            exc.Add(new(Q(x, y), Q(x + 1, y), Q(x + 1, y + 1))); exc.Add(new(Q(x, y), Q(x + 1, y + 1), Q(x, y + 1)));
        }
        var day = WallExcav.DaylightOf(exc, (x, y) => Lg(x, y), rec.Walls);
        check("S170 ⑦ 데이라잇 — 뒤쪽 y = 8 한 줄(21점) · z = 땅", day.Count == 1 && day[0].Count == 21 && day[0].All(p => Math.Abs(p.Y - 8) < 1e-12 && Math.Abs(p.Z - 110) < 1e-12),
            $"줄 {day.Count} · 점 {string.Join(",", day.Select(d => d.Count))}");
    }
}
