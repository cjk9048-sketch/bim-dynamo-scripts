// ── S261 WLB ★[로드맵 v117.3 · JACK D1 ③ «그은 선 = 벽 앞면 + 용지 경계 선을 따로 고름 — 안 고르면 그은 선을 경계로 봄 · 기초 외곽이 경계 밖이면 경고»] 구조물 외곽 · 용지 경계(경고만)
//    만든 좌표만(현장 값 없음 · 크기만 현장처럼 21만 · 50만 m) — 기하 · 자가검증 줄 · 벽 세모는 그대로(LandCheck는 로그 줄 · ⚠ · 미리보기만 더함)
//   판 곧은 절토: L = (X0, Y0) → (X0 + 40, Y0) · 바깥(R_out · 흙 쪽) = +y · 측점 1 m마다 · 벽 높이 5 m · 띠 폭 w 0.05(1:0.01) · 역T 첫 표
//   해석값: 앞면 = L′(y = −w) · 구조물 바깥 끝 y = max x − w · 경계가 앞면 뒤 1 m(y = 0.95)면 밖 거리 = max x − 1.0 · STA 0~40 전부
using DH.Grading.Core;

static class WallLandCheck
{
    const double X0 = 210000, Y0 = 509000, W = 0.05, H = 5;
    static List<Point3> Rect(double x0, double y0, double x1, double y1) => new() { new(x0, y0, 0), new(x1, y0, 0), new(x1, y1, 0), new(x0, y1, 0) };
    static (double MinX, double MaxX, double MinA, double MaxA) Span(WallSection.Shape sh)
    {
        double mn = double.MaxValue, mx = double.MinValue, mnA = double.MaxValue, mxA = double.MinValue;
        foreach (var p in sh.Parts) foreach (var (x, _) in p.Ring) { if (p.Structure) { mn = Math.Min(mn, x); mx = Math.Max(mx, x); } mnA = Math.Min(mnA, x); mxA = Math.Max(mxA, x); }
        return (mn, mx, mnA, mxA);
    }
    /// <summary>측점표 — 점열(세계)을 따라 1 m 안팎 간격 · 절토 높이 h · 띠 w · 형식 단면 기초까지.</summary>
    static WallProfile.Result Prof(IReadOnlyList<(double X, double Y)> line, WallSection.Table t, WallSection.Common c, double h = H)
    {
        var st = new List<WallProfile.Station>();
        double s = 0;
        for (int k = 0; k + 1 < line.Count; k++)
        {
            var a = line[k]; var b = line[k + 1];
            double l = Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));
            int n = Math.Max(1, (int)Math.Round(l));
            for (int u = k == 0 ? 0 : 1; u <= n; u++)
            {
                double f = (double)u / n;
                st.Add(new WallProfile.Station { S = s + l * f, X = a.X + (b.X - a.X) * f, Y = a.Y + (b.Y - a.Y) * f, EG = 100 + h, FG = 100, H = h, Wh = h, W = W });
            }
            s += l;
        }
        var R = WallProfile.Analyze(st, s, double.NaN, double.NaN);
        WallProfile.Footing(R, t, null, c);
        WallProfile.Texts(R);
        return R;
    }
    static string Brief(WallProfile.Result r) => $"{r.LandText.Trim()} | ⚠ {r.LandWarn} | 미리보기 {r.Preview} | {r.LandProbe}";

    public static void Run(Action<string, bool, string> check0)
    {
        void Check(string n, bool ok, string d) => check0("S261 WLB " + n, ok, d);
        Console.WriteLine("\n== S261 WLB 구조물 외곽 · 용지 경계(경고만) ==");
        var com = new WallSection.Common();
        var tT = WallSection.DefaultTable(WallSection.Kind.역T형);
        var sp = Span(WallSection.Build(tT, H, com, true));
        var line = new List<(double, double)> { (X0, Y0), (X0 + 40, Y0) };
        var cutR = Rect(X0 - 5, Y0, X0 + 45, Y0 + 30);   // R_out = +y(L은 그 아래 변 위)

        // (l1) 경계가 앞면 뒤 1 m — 밖 최대 = max x − 1.0(±1 µm) · STA 0~40 · ⚠ 글 · 외곽 1개(측점 41 × 2 점)
        {
            var r = Prof(line, tT, com);
            string pv0 = r.Preview;
            WallProfile.LandCheck(r, cutR, new[] { Rect(X0 - 10, Y0 - 50, X0 + 50, Y0 - W + 1.0) });
            WallProfile.Texts(r);
            double exp = sp.MaxX - 1.0;
            bool ok = !r.LandByLine && Math.Abs(r.LandMax - exp) <= 1e-6 && r.LandRuns.Count == 1 && Math.Abs(r.LandRuns[0].S0) <= 1e-9 && Math.Abs(r.LandRuns[0].S1 - 40) <= 1e-9
                && r.LandWarn.StartsWith(string.Format(System.Globalization.CultureInfo.InvariantCulture, "기초 외곽이 용지 경계 밖 — 최대 {0:F2} m · STA 0.0~40.0 m", exp))
                && r.Outline.Count == 1 && r.Outline[0].Count == 2 * r.Stations.Count && r.LandMarks.Count >= 1 && r.Preview.Contains("기초 외곽 용지 경계 밖 최대") && !pv0.Contains("기초 외곽");
            Check("(l1) 곧은 절토 5 m · 경계 앞면 뒤 1 m — 밖 최대 = max x − 1.0(±1 µm) · STA 0~40 · ⚠ · 외곽 1 · 미리보기(전에는 없음)", ok, $"기대 {exp:F6} · 잰 {r.LandMax:F6} · {Brief(r)}");
        }
        // (l2) 경계가 멀다(뒤 20 m · 옆 10 m) — 밖 없음 · ⚠ 없음 · 여유 = 옆 변까지 10 m(외곽 끝 점)
        {
            var r = Prof(line, tT, com);
            WallProfile.LandCheck(r, cutR, new[] { Rect(X0 - 10, Y0 - 50, X0 + 50, Y0 + 20) });
            bool ok = r.LandRuns.Count == 0 && r.LandWarn.Length == 0 && Math.Abs(r.LandMax + 10) <= 1e-6 && r.LandPreview.StartsWith("기초 외곽 용지 경계 안(여유 10.00 m)");
            Check("(l2) 경계가 멀다 — 밖 없음 · ⚠ 없음 · 여유 10.00 m", ok, Brief(r));
        }
        // (l3) 용지 경계 없음 — 그은 선 = 경계 · 밖 최대 = ⑨-b 구조물 선 밖(= max x − w) · STA 0~40 · ⚠ «그은 선(용지 경계 없음)»
        {
            var r = Prof(line, tT, com);
            WallProfile.LandCheck(r, cutR, null);
            double exp = sp.MaxX - W;
            bool ok = r.LandByLine && Math.Abs(r.LandMax - exp) <= 1e-9 && Math.Abs(r.LandMax - r.Feet[0].OutMax) <= 1e-12 && r.LandRuns.Count == 1 && Math.Abs(r.LandRuns[0].S1 - 40) <= 1e-9
                && r.LandWarn.StartsWith("기초 외곽이 그은 선(용지 경계 없음) 밖 — 최대 ");
            Check("(l3) 용지 경계 없음 — 그은 선 = 경계 · 밖 최대 = ⑨-b 구조물 선 밖 · ⚠", ok, $"기대 {exp:F6} · ⑨-b {r.Feet[0].OutMax:F6} · {Brief(r)}");
            // 빈 목록 · 못 읽는 링(점 2개)뿐 — 그은 선과 같음
            var r2 = Prof(line, tT, com);
            WallProfile.LandCheck(r2, cutR, new[] { new List<Point3> { new(X0, Y0, 0), new(X0 + 1, Y0, 0) } });
            Check("(l3b) 못 읽는 링뿐 — 그은 선 · 글에 «못 읽은 링 1 뺌»", r2.LandByLine && Math.Abs(r2.LandMax - exp) <= 1e-9 && r2.LandText.Contains("못 읽은 링 1 뺌"), Brief(r2));
        }
        // (l4) 비스듬한 경계(기울기 0.1) — 구조물 바깥 끝 y = o · 경계 윗변 y = o − 2 + 0.1(x − X0) → 밖 = x < 20 · 최대 = 2/√1.01(x = 0 · 수선 거리) · STA 0~20(±1 µm)
        {
            var r = Prof(line, tT, com);
            double o = sp.MaxX - W, c = Y0 + o - 2;
            var land = new List<Point3> { new(X0 - 10, Y0 - 50, 0), new(X0 + 50, Y0 - 50, 0), new(X0 + 50, c + 5, 0), new(X0 - 10, c - 1, 0) };
            WallProfile.LandCheck(r, cutR, new[] { land });
            double exp = 2 / Math.Sqrt(1.01);
            bool ok = Math.Abs(r.LandMax - exp) <= 1e-6 && r.LandRuns.Count == 1 && Math.Abs(r.LandRuns[0].S0) <= 1e-9 && Math.Abs(r.LandRuns[0].S1 - 20) <= 1e-6
                && Math.Abs(r.LandAt.X - X0) <= 1e-6 && Math.Abs(r.LandAt.Y - (Y0 + o)) <= 1e-6;
            Check("(l4) 비스듬한 경계 — 최대 2/√1.01(±1 µm) @ x 0 · STA 0~20(±1 µm)", ok, $"기대 {exp:F6} · {Brief(r)}");
        }
        // (l5) 꺾은 선(90°) — 볼록 모서리 외곽 점 = 두 평행선 만남 (X0 + 20 + o, Y0 − o) · 그은 선 기준 최대는 o 그대로(⑨-b와 같음)
        {
            var lineL = new List<(double, double)> { (X0, Y0), (X0 + 20, Y0), (X0 + 20, Y0 + 20) };
            // 바깥 = 오른쪽(−y · +x) — R_out = 아래 · 오른쪽 L자
            var cutL = new List<Point3> { new(X0 - 10, Y0 - 30, 0), new(X0 + 50, Y0 - 30, 0), new(X0 + 50, Y0 + 30, 0), new(X0 + 20, Y0 + 30, 0), new(X0 + 20, Y0, 0), new(X0 - 10, Y0, 0) };
            var r = Prof(lineL, tT, com);
            WallProfile.LandCheck(r, cutL, null);
            double o = sp.MaxX - W;
            bool miter = r.Outline.Count == 1 && r.Outline[0].Any(q => Math.Abs(q.X - (X0 + 20 + o)) <= 1e-9 && Math.Abs(q.Y - (Y0 - o)) <= 1e-9);
            bool ok = miter && Math.Abs(r.LandMax - o) <= 1e-12 && r.LandRuns.Count == 1 && Math.Abs(r.LandRuns[0].S1 - 40) <= 1e-9;
            Check("(l5) 90° 꺾은 선 — 모서리 외곽 = 맞댐 점 · 그은 선 기준 최대 = o", ok, $"맞댐 {miter} · {Brief(r)}");
            // 같은 선 · 경계 = 그은 선에서 바깥 1 m 평행(모서리 볼록) — 맞댐 점까지 거리 = (o − 1)√2 가 최대
            var landL = new List<Point3> { new(X0 - 10, Y0 - 1, 0), new(X0 + 21, Y0 - 1, 0), new(X0 + 21, Y0 + 40, 0), new(X0 - 10, Y0 + 40, 0) };
            var r2 = Prof(lineL, tT, com);
            WallProfile.LandCheck(r2, cutL, new[] { landL });
            double exp = (o - 1) * Math.Sqrt(2);
            Check("(l5b) 꺾은 선 · 평행 경계 1 m — 최대 = 모서리 (o − 1)√2(±1 µm) · STA 0~40", Math.Abs(r2.LandMax - exp) <= 1e-6 && r2.LandRuns.Count == 1 && Math.Abs(r2.LandRuns[0].S1 - 40) <= 1e-9,
                $"기대 {exp:F6} · {Brief(r2)}");
        }
        // (l6) 지은 수직 옹벽(S244 WP 판 U)에 LandCheck(Built) — 기하 · 자가검증 줄 그대로 · 로그 끝에 ⑨-c 한 줄만 · 그은 선 기준 최대 = ⑨-b
        {
            var mu = WallProfileCheck.MakeU(new WallSection.Common(), tT);
            if (mu is not { } u || !u.B.Ok || u.B.Profile == null) { Check("(l6) 판 U 수직 옹벽 짓기", false, mu?.B.SelfCheck ?? "판정"); return; }
            var b = u.B;
            string log0 = b.Log, self0 = b.SelfCheck; int warn0 = b.Warn.Count, tris0 = b.WallTrisC.Count, lines0 = b.Lines.Count;
            WallProfile.LandCheck(b, u.Cut.CutRing, null);
            var p = b.Profile;
            string add = b.Log.StartsWith(log0) ? b.Log.Substring(log0.Length) : "(앞 로그가 바뀜)";
            bool ok = add.StartsWith("  ⑨-c 기초 외곽") && add.Count(ch => ch == '\n') == 1
                && b.SelfCheck == self0 && b.WallTrisC.Count == tris0 && b.Lines.Count == lines0 && b.Warn.Count == warn0 + 1 && b.Warn[^1] == p.LandWarn
                && p.LandByLine && Math.Abs(p.LandMax - p.Feet[0].OutMax) <= 1e-12 && p.Outline.Count >= 1 && b.Profile.Preview.Contains("기초 외곽 그은 선 밖")
                && p.LandRuns.Count == 1 && Math.Abs(p.LandRuns[0].S0 - 0.024) <= 1e-6 && Math.Abs(p.LandRuns[0].S1 - (b.LineLen - 0.024)) <= 1e-6;
            Check("(l6) 판 U 수직 옹벽 — 기하 · 자가검증 줄 그대로 · ⑨-c 한 줄 · ⚠ 하나 · 그은 선 최대 = ⑨-b · STA = 벽 시작 · 끝(|H| 1 cm 건넘 0.024 m ±1 µm)", ok,
                $"STA {string.Join(", ", p.LandRuns.Select(r => $"{r.S0:F6}~{r.S1:F6}"))} · 길이 {b.LineLen:F6} · {Brief(p)}");
            // 경계 = 큰 네모(판 전체) — 밖 없음 · ⚠ 안 늘어남
            var u2 = WallProfileCheck.MakeU(new WallSection.Common(), tT)!.Value;
            int w2 = u2.B.Warn.Count;
            WallProfile.LandCheck(u2.B, u2.Cut.CutRing, new[] { Rect(-100, -100, 200, 100) });
            Check("(l6b) 판 U · 큰 경계 — 밖 없음 · ⚠ 그대로", u2.B.Profile!.LandMax < 0 && u2.B.Warn.Count == w2 && u2.B.Profile.LandRuns.Count == 0, Brief(u2.B.Profile));
            // 다단(Shape 3) — 측점표 = 바깥 벽(2차 수직) · 같은 셈이 돈다
            var u3 = WallProfileCheck.MakeU(new WallSection.Common(), tT, WallByLine.WallShape.Tiered)!.Value;
            WallProfile.LandCheck(u3.B, u3.Cut.CutRing, null);
            Check("(l6c) 다단 — ⑨-c 줄 · 그은 선 최대 = ⑨-b", u3.B.Shape == 3 && u3.B.Profile != null && Math.Abs(u3.B.Profile.LandMax - u3.B.Profile.Feet[0].OutMax) <= 1e-12 && u3.B.Log.Contains("⑨-c 기초 외곽"),
                u3.B.Profile == null ? u3.B.SelfCheck : Brief(u3.B.Profile));
        }
    }
}
