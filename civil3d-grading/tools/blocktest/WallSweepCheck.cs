// ── S174 ★★★[v106 · 계획_v106.md · JACK 1027 «지금 옹벽 단면으로 스윕 · 밖으로 보이는 표면만 콘크리트는 무늬 · 보강토는 보강토 모양»] 스윕 계획 · 보이는 면 무늬 · 지형 덮기 ──
//   만든 꼴(현장 값 없음 — 공개 저장소)
using DH.Grading.Core;

static class WallSweepCheck
{
    static readonly WallSection.Common C = new(0.10, 0.10, 0.02, 0.01);

    /// <summary>만든 단면 줄 — 점마다 (x, y, 앞 땅, 보이는 높이).</summary>
    static WallExcav.SecLine Line(WallSection.Kind k, int soil, IEnumerable<(double X, double Y, double Toe, double H)> pts, bool cut = true)
    {
        var t = WallSection.DefaultTable(k);
        var ln = new WallExcav.SecLine { Kind = k, Soil = soil, Up = cut, SlopeW = 0.01, Src = "시험" };
        foreach (var (x, y, toe, h) in pts)
        {
            var row = WallSection.Lookup(t, h, out _, out _, out _, out _);
            ln.Pts.Add(new WallExcav.SecPoint { X = x, Y = y, Toe = toe, H = h, Row = t.Rows.IndexOf(row), Shape = WallSection.Build(t, h, C, cut) });
        }
        return ln;
    }

    // ── v105 WallSolidDwg.Populate 그대로(정거장 자리만) — 옮긴 Core가 같은 결과인지 견준다 ──
    record OldSta(double X, double Y, double Toe, double Nx, double Ny, double Scale);
    static (List<List<OldSta>> Runs, List<(double X, double Y, double Ang)> Corners) OldPlan(WallExcav.SecLine ln)
    {
        const double FrontOut = 0.02, StraightDeg = 1.0;
        var runs = new List<List<OldSta>>(); var corners = new List<(double, double, double)>();
        var P = ln.Pts; int n = P.Count;
        var nx = new double[n - 1]; var ny = new double[n - 1]; var len = new double[n - 1];
        for (int k = 0; k + 1 < n; k++)
        {
            double ex = P[k + 1].X - P[k].X, ey = P[k + 1].Y - P[k].Y, L = Math.Sqrt(ex * ex + ey * ey);
            len[k] = L;
            if (L < 1e-9) { nx[k] = double.NaN; continue; }
            nx[k] = -ey / L * ln.Soil; ny[k] = ex / L * ln.Soil;
        }
        List<WallSection.Part> Solids(WallSection.Shape sh) => sh.Parts.Where(p => p.Structure && p.Fraction >= 1 - 1e-12 && p.Ring.Count >= 3).ToList();
        bool Same(WallExcav.SecPoint a, WallExcav.SecPoint b)
        {
            if (a.Row != b.Row) return false;
            var pa = Solids(a.Shape); var pb = Solids(b.Shape);
            if (pa.Count != pb.Count) return false;
            for (int i = 0; i < pa.Count; i++) if (pa[i].Material != pb[i].Material || pa[i].Ring.Count != pb[i].Ring.Count) return false;
            return true;
        }
        double Reach(WallSection.Shape sh) => Solids(sh).SelectMany(p => p.Ring).Select(q => Math.Abs(q.X - FrontOut)).DefaultIfEmpty(0).Max();
        var run = new List<OldSta>();
        void Flush() { if (run.Count >= 2) runs.Add(run.ToList()); run.Clear(); }
        bool IsCorner(int k0, int k1, WallSection.Shape sh, out double ang)
        {
            double cr = nx[k0] * ny[k1] - ny[k0] * nx[k1], dt = nx[k0] * nx[k1] + ny[k0] * ny[k1];
            ang = Math.Atan2(cr, dt);
            double a = Math.Abs(ang);
            if (a * 180 / Math.PI < StraightDeg * 0.25) return false;
            return a * 180 / Math.PI >= 15.0 || Reach(sh) * a >= 0.5 * Math.Min(len[k0], len[k1]);
        }
        int lastK = -1, firstK = -1;
        for (int k = 0; k + 1 < n; k++)
        {
            if (double.IsNaN(nx[k])) continue;
            if (firstK < 0) firstK = k;
            var a = P[k]; var b = P[k + 1];
            if (lastK >= 0)
            {
                if (IsCorner(lastK, k, a.Shape, out double ang)) { Flush(); corners.Add((a.X, a.Y, ang)); }
                else if (run.Count > 0 && Math.Abs(run[^1].X - a.X) < 1e-9 && Math.Abs(run[^1].Y - a.Y) < 1e-9)
                {
                    double bx = nx[lastK] + nx[k], by = ny[lastK] + ny[k], bl = Math.Sqrt(bx * bx + by * by);
                    if (bl > 1e-9) run[^1] = run[^1] with { Nx = bx / bl, Ny = by / bl, Scale = Math.Min(1.1, 1 / Math.Cos(Math.Abs(ang) / 2)) };
                }
            }
            if (Same(a, b))
            {
                if (run.Count == 0) run.Add(new OldSta(a.X, a.Y, a.Toe, nx[k], ny[k], 1.0));
                run.Add(new OldSta(b.X, b.Y, b.Toe, nx[k], ny[k], 1.0));
            }
            else
            {
                double mx = (a.X + b.X) / 2, my = (a.Y + b.Y) / 2, mt = (a.Toe + b.Toe) / 2;
                if (run.Count == 0) run.Add(new OldSta(a.X, a.Y, a.Toe, nx[k], ny[k], 1.0));
                run.Add(new OldSta(mx, my, mt, nx[k], ny[k], 1.0));
                Flush();
                run.Add(new OldSta(mx, my, mt, nx[k], ny[k], 1.0));
                run.Add(new OldSta(b.X, b.Y, b.Toe, nx[k], ny[k], 1.0));
            }
            lastK = k;
        }
        Flush();
        if (firstK >= 0 && lastK > firstK && Math.Abs(P[0].X - P[n - 1].X) < 1e-6 && Math.Abs(P[0].Y - P[n - 1].Y) < 1e-6)
        {
            double cr = nx[lastK] * ny[firstK] - ny[lastK] * nx[firstK], dt = nx[lastK] * nx[firstK] + ny[lastK] * ny[firstK];
            double ang = Math.Atan2(cr, dt);
            if (Math.Abs(ang) * 180 / Math.PI >= StraightDeg * 0.25) corners.Add((P[0].X, P[0].Y, ang));
        }
        return (runs, corners);
    }

    static string SameAsOld(WallExcav.SecLine ln)
    {
        var (oR, oC) = OldPlan(ln);
        var p = WallSweep.Make(ln, lean: false, keepTop: false);
        if (oR.Count != p.Runs.Count) return $"덩어리 {p.Runs.Count}/{oR.Count}";
        if (oC.Count != p.Corners.Count) return $"모서리 {p.Corners.Count}/{oC.Count}";
        for (int r = 0; r < oR.Count; r++)
        {
            if (oR[r].Count != p.Runs[r].Count) return $"덩어리 {r} 정거장 {p.Runs[r].Count}/{oR[r].Count}";
            for (int i = 0; i < oR[r].Count; i++)
            {
                var a = oR[r][i]; var b = p.Runs[r][i];
                if (Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y) + Math.Abs(a.Toe - b.Toe) + Math.Abs(a.Nx - b.Nx) + Math.Abs(a.Ny - b.Ny) + Math.Abs(a.Scale - b.Scale) > 1e-12 || b.Lean != 0)
                    return $"덩어리 {r} 정거장 {i} 다름";
            }
        }
        for (int c = 0; c < oC.Count; c++)
            if (Math.Abs(oC[c].X - p.Corners[c].P.X) + Math.Abs(oC[c].Y - p.Corners[c].P.Y) + Math.Abs(oC[c].Ang - p.Corners[c].Ang) > 1e-12) return $"모서리 {c} 다름";
        return "";
    }

    /// <summary>조각 꼭짓점(틀 좌표) → 월드 점(면 위 · 겉 내밀기 w).</summary>
    static Point3 W(WallFacePattern.Sheet s, double u, double v, double w)
        => new(s.O.X + s.U.X * u + s.V.X * v + s.W.X * w, s.O.Y + s.U.Y * u + s.V.Y * v + s.W.Y * w, s.O.Z + s.U.Z * u + s.V.Z * v + s.W.Z * w);

    public static void Run(Action<string, bool, string> check0)
    {
        void check(string name, bool ok, string detail = "") => check0(name, ok, detail);
        Console.WriteLine("\n== S174 옹벽 3D 스윕 계획 · 보이는 면 무늬 · 지형 덮기 ==");
        var T = WallSection.Kind.역T형;

        // ① 옮긴 스윕 계획 = v105 그대로 — 곧은 · 완만한 곡선(반지름 15 m · 3°) · 직각 꺾임 · 표 줄 바뀜 · 닫힌 네모
        var straight = Line(T, 1, Enumerable.Range(0, 11).Select(i => (i * 2.0, 0.0, 100.0, 4.0)));
        var arc = Line(T, 1, Enumerable.Range(0, 31).Select(i => { double a = i * 3 * Math.PI / 180; return (15 * Math.Sin(a), 15 - 15 * Math.Cos(a), 100.0, 4.0); }));
        var bend = Line(T, -1, new[] { (0.0, 0.0, 100.0, 4.0), (10.0, 0.0, 100.0, 4.0), (10.0, 10.0, 100.0, 4.0) });
        var rows = Line(T, 1, Enumerable.Range(0, 9).Select(i => (i * 2.0, 0.0, 100.0 + i * 0.1, 3.0 + i * 0.6)));
        var loop = Line(T, 1, new[] { (0.0, 0.0, 100.0, 4.0), (10.0, 0.0, 100.0, 4.0), (10.0, 10.0, 100.0, 4.0), (0.0, 10.0, 100.0, 4.0), (0.0, 0.0, 100.0, 4.0) });
        foreach (var (nm, ln) in new[] { ("곧은", straight), ("곡선", arc), ("직각", bend), ("표 줄 바뀜", rows), ("닫힌 네모", loop) })
        {
            string d = SameAsOld(ln);
            var pl = WallSweep.Make(ln, lean: false, keepTop: false);
            check($"S174 ① 스윕 계획 = v105 그대로 — {nm}", d.Length == 0, d.Length == 0 ? $"덩어리 {pl.Runs.Count} · 모서리 {pl.Corners.Count}" : d);
        }

        // ①b ★[JACK 1027 스샷 «옹벽 끝부분이 뾰족하게 튀어나오는 오류»] 표 줄이 바뀌는 가운데 정거장 — 꼭대기 · 앞 땅이 두 점 사이 직선 위(쐐기 없음) · 땅 위 몫만 늘여 앞면 기울기 그대로 · 바닥 안 뜸
        {
            // 앞 땅 100 → 100.5(기움) · 높이 3 → 6.6(표 줄 여러 번 바뀜) — 꼭대기 직선 103 + 0.35x · 앞 땅 100 + 0.05x
            var plR = WallSweep.Make(rows);
            double worstTop = 0, worstToe = 0, worstFace = 0; int mids = 0;
            foreach (var r in plR.Runs)
                foreach (var st in r)
                {
                    worstTop = Math.Max(worstTop, Math.Abs(st.Toe + st.Shape.ZTop - (103 + 0.35 * st.X)));
                    worstToe = Math.Max(worstToe, Math.Abs(st.Toe - (100 + 0.05 * st.X)));
                    // 앞면 꼭대기 앞 모서리 = FaceX0 + FaceN·높이(늘인 단면도 앞면 기울기 그대로)
                    double ztop = st.Shape.ZTop - 1e-6;
                    worstFace = Math.Max(worstFace, Math.Abs(WallSweep.FrontAt(st.Shape, ztop) - (st.Shape.FaceX0 + st.Shape.FaceN * ztop)));
                    if (Math.Abs(st.X % 2.0 - 1.0) < 1e-9) mids++;
                }
            check("S174 ①b 표 줄 바뀌는 가운데 정거장 — 꼭대기 · 앞 땅이 직선 위(1 µm) · 앞면 기울기 그대로(1 µm) · 가운데 정거장 있음",
                worstTop < 1e-6 && worstToe < 1e-6 && worstFace < 1e-6 && mids > 0, $"꼭대기 {worstTop:E1} · 앞 땅 {worstToe:E1} · 앞면 {worstFace:E1} · 가운데 정거장 {mids}");
            // v105 그대로면(keepTop 끔) 같은 줄에서 꼭대기가 어긋난다 — 시험이 고침을 실제로 잰다
            double oldWorst = WallSweep.Make(rows, keepTop: false).Runs.SelectMany(r => r).Max(st => Math.Abs(st.Toe + st.Shape.ZTop - (103 + 0.35 * st.X)));
            check("S174 ①b v105 규칙이면 가운데 정거장 꼭대기가 어긋남(> 1 cm — 스샷 쐐기의 원인)", oldWorst > 0.01, $"v105 최대 {oldWorst * 100:F1} cm");
            // 높이가 튀는 점(4 → 8 → 4 m · 앞 땅 같음) — 가운데 정거장 바닥(앞 땅)은 그대로 · 꼭대기는 두 점 평균
            var jump = Line(T, 1, new[] { (0.0, 0.0, 100.0, 4.0), (2.0, 0.0, 100.0, 4.0), (4.0, 0.0, 100.0, 8.0), (6.0, 0.0, 100.0, 4.0), (8.0, 0.0, 100.0, 4.0) });
            var plJ = WallSweep.Make(jump);
            double jBad = 0;
            foreach (var st in plJ.Runs.SelectMany(r => r))
            {
                double want = 104;                                                                  // 높이 4 m 점
                if (Math.Abs(st.X - 3) < 1e-9 || Math.Abs(st.X - 5) < 1e-9) want = 106;          // 가운데 = (4 + 8) / 2
                else if (Math.Abs(st.X - 4) < 1e-9) want = 108;
                jBad = Math.Max(jBad, Math.Abs(st.Toe - 100) + Math.Abs(st.Toe + st.Shape.ZTop - want));
            }
            check("S174 ①b 높이가 튀는 점 — 가운데 정거장 앞 땅 그대로(바닥 안 뜸) · 꼭대기 = 두 점 평균", jBad < 1e-6, $"최대 어긋남 {jBad:E1}");
            // ★[v106.0.1 · 검토 낮음 5] 형식마다 — AtHeight(높이 h0 단면, h) = 같은 줄로 높이 h에 다시 세운 단면(꼭짓점마다 1e-9) · 기초는 그대로
            int atBad = 0, atN = 0; string atFirst = "";
            foreach (var (k, cut) in new[] { (T, true), (WallSection.Kind.중력식, true), (WallSection.Kind.보강토, false), (WallSection.Kind.패널식, true), (WallSection.Kind.찰쌓기, true), (WallSection.Kind.메쌓기, false), (WallSection.Kind.L형, true) })
            {
                var t = WallSection.DefaultTable(k);
                foreach (double h0 in new[] { 1.2, 2.5, 4.0, 6.0 })
                    foreach (double h in new[] { 0.5, 1.9, 3.3, 7.4 })
                    {
                        var sh0 = WallSection.Build(t, h0, C, cut);
                        var row = WallSection.Lookup(t, h0, out _, out _, out _, out double df0);
                        var want = WallSection.BuildRow(k, row, h + sh0.Df, C, cut, sh0.Df);
                        var got = WallSweep.AtHeight(sh0, h);
                        atN++;
                        bool ok = got.Parts.Count == want.Parts.Count && Math.Abs(got.ZTop - h) < 1e-12;
                        for (int pi = 0; ok && pi < got.Parts.Count; pi++)
                        {
                            var ra = got.Parts[pi].Ring; var rb = want.Parts[pi].Ring;
                            ok = ra.Count == rb.Count && ra.Zip(rb).All(z => Math.Abs(z.First.X - z.Second.X) < 1e-9 && Math.Abs(z.First.Z - z.Second.Z) < 1e-9);
                        }
                        if (!ok) { atBad++; if (atFirst.Length == 0) atFirst = $"{k} h0 {h0} → {h}"; }
                    }
            }
            check("S174 ①b 형식마다 꼭대기 옮긴 단면 = 그 줄로 다시 세운 단면(역T · 중력 · 보강토 · 패널 · 찰쌓기 · 메쌓기 · L — 꼭짓점 1e-9)", atBad == 0, $"{atN - atBad}/{atN}{(atFirst.Length > 0 ? " · 첫 틀림 " + atFirst : "")}");
        }

        // ② 곧은 벽 · 평평한 땅 · 자연석 — 모든 조각이 보이는 칸 안 · 덮은 비율 · 결정성 · 틀 직교
        var lay = WallFacePattern.Lay(WallSweep.Make(straight), WallFacePattern.자연석);
        double top = 104.0, bot = 100.0 - WallFacePattern.Bury;
        int outside = 0; double area = 0;
        foreach (var pc in lay.Pieces)
        {
            var s = lay.Sheets[pc.Sheet];
            foreach (var (u, v) in pc.Poly) { var q = W(s, u, v, 0); if (q.Z < bot - 1e-6 || q.Z > top + 1e-6 || q.X < -1e-6 || q.X > 20 + 1e-6) outside++; }
            area += WallFacePattern.Area(pc.Poly) * s.V.Z / s.V.Z;   // 틀 넓이(면 위 넓이)
        }
        double faceArea = lay.Sheets.Sum(s => WallFacePattern.Area(s.Region));
        double ratio = area / faceArea;
        check("S174 ② 곧은 벽 자연석 — 조각 > 0 · 칸 밖 꼭짓점 0 · 덮은 비율 0.75 ~ 0.95(줄눈 · 모서리 깎기 몫) · 실패 0",
            lay.Pieces.Count > 100 && outside == 0 && ratio > 0.75 && ratio < 0.95 && lay.ClipFail == 0,
            $"조각 {lay.Pieces.Count}(그대로 {lay.Whole} · 잘림 {lay.Cut} · 버림 {lay.Dropped}) · 판 {lay.Sheets.Count} · 칸 밖 {outside} · 비율 {ratio:F3}");
        var lay2 = WallFacePattern.Lay(WallSweep.Make(straight), WallFacePattern.자연석);
        bool same = lay2.Pieces.Count == lay.Pieces.Count && lay.Pieces.Zip(lay2.Pieces).All(z => z.First.Poly.SequenceEqual(z.Second.Poly));
        check("S174 ② 결정성 — 다시 깔아도 같은 조각", same, $"{lay2.Pieces.Count}/{lay.Pieces.Count}");
        bool ortho = lay.Sheets.All(s =>
        {
            double uu = s.U.X * s.U.X + s.U.Y * s.U.Y + s.U.Z * s.U.Z, vv = s.V.X * s.V.X + s.V.Y * s.V.Y + s.V.Z * s.V.Z, ww = s.W.X * s.W.X + s.W.Y * s.W.Y + s.W.Z * s.W.Z;
            double uv = s.U.X * s.V.X + s.U.Y * s.V.Y + s.U.Z * s.V.Z, uw = s.U.X * s.W.X + s.U.Y * s.W.Y + s.U.Z * s.W.Z, vw = s.V.X * s.W.X + s.V.Y * s.W.Y + s.V.Z * s.W.Z;
            return Math.Abs(uu - 1) + Math.Abs(vv - 1) + Math.Abs(ww - 1) + Math.Abs(uv) + Math.Abs(uw) + Math.Abs(vw) + Math.Abs(s.U.Z) < 1e-9 && s.V.Z > 0;
        });
        // 흙 쪽 +y(왼쪽) → 공기 쪽 −y
        check("S174 ② 틀 — 직교 · U 수평 · V 위 · W 공기 쪽(흙 쪽 반대) · 곧은 벽은 판 하나", ortho && lay.Sheets.All(s => s.W.Y < -0.99) && lay.Sheets.Count == 1, $"판 {lay.Sheets.Count} · W {lay.Sheets[0].W}");

        // ③ 기운 땅 · 기운 꼭대기(앞 땅 100 → 102 · 높이 3 → 5) — 칸 밖 꼭짓점 0 · 가로줄 수평(블록 아래 변 Z가 0.2 배수 + 줄눈 반)
        var slope = Line(WallSection.Kind.보강토, 1, Enumerable.Range(0, 11).Select(i => (i * 2.0, 0.0, 100.0 + i * 0.2, 3.0 + i * 0.2)), cut: false);
        var plB = WallSweep.Make(slope);
        var lb = WallFacePattern.Lay(plB, WallFacePattern.블록);
        int out3 = 0, notLevel = 0, whole = 0, offBad = 0; string first3 = "";
        foreach (var pc in lb.Pieces)
        {
            var s = lb.Sheets[pc.Sheet];
            foreach (var (u, v) in pc.Poly)
            {
                // 몸통과 같은 자리 — 줄 전체 띠(덩어리마다 정거장 사이 직선 보간 · 표 줄 계단 자리는 덩어리 둘 중 꼭대기 높은 쪽 · 앞 땅 낮은 쪽)
                var q = W(s, u, v, 0);
                double toe = double.MaxValue, topZ = double.MinValue;
                foreach (var st in plB.Runs)
                    for (int k = 0; k + 1 < st.Count; k++)
                    {
                        if (q.X < st[k].X - 1e-3 || q.X > st[k + 1].X + 1e-3) continue;
                        double t = Math.Clamp((q.X - st[k].X) / (st[k + 1].X - st[k].X), 0, 1);
                        toe = Math.Min(toe, st[k].Toe + (st[k + 1].Toe - st[k].Toe) * t);
                        topZ = Math.Max(topZ, st[k].Toe + st[k].Shape.ZTop + (st[k + 1].Toe + st[k + 1].Shape.ZTop - st[k].Toe - st[k].Shape.ZTop) * t);
                    }
                if (q.Z < toe - WallFacePattern.Bury - 2e-3 || q.Z > topZ + 2e-3) { out3++; if (first3.Length == 0) first3 = $" · 첫 칸 밖 x {q.X:F3} z {q.Z:F3} 땅 {toe:F3} 꼭대기 {topZ:F3} 판 {pc.Sheet} {(pc.Rect ? "상자" : "다각형")}"; }
            }
            if (!pc.Rect) continue;
            whole++;
            double zb = W(s, pc.Poly[0].U, pc.Poly[0].V, 0).Z, g = WallFacePattern.블록.Joint / 2;
            long j = (long)Math.Round((zb - g) / 0.2);
            if (Math.Abs(zb - g - j * 0.2) > 1e-6) notLevel++;
            double uL = pc.Poly.Min(p => p.U) + s.UG0, off = (j & 1) != 0 ? 0.23 : 0;   // 무늬 u — 줄 전체에서 이어진다
            double r = ((uL - g - off) % 0.46 + 0.46) % 0.46;
            if (Math.Min(r, 0.46 - r) > 1e-6) offBad++;
        }
        double faceB = lb.Sheets.Sum(s => WallFacePattern.Area(s.Region));
        double expect = faceB / (0.46 * 0.20);
        check("S174 ③ 기운 땅 · 꼭대기 보강토 — 칸 밖 꼭짓점 0(2 mm) · 온전한 블록 아래 변 = 0.2 m 배수(가로줄 수평) · 홀수 줄 0.23 엇갈림",
            out3 == 0 && notLevel == 0 && offBad == 0 && whole > 0.6 * expect && whole < 1.05 * expect,
            $"조각 {lb.Pieces.Count} · 온전한 블록 {whole}(넓이로 어림 {expect:F0}) · 칸 밖 {out3} · 기운 줄 {notLevel} · 엇갈림 틀림 {offBad} · 판 {lb.Sheets.Count}{first3}");

        // ④ 곡선(반지름 15 m · 3°) 석축 1:0.3 — 판 비틀림이 내밀기 · 묻힘 안(조각이 몸통에서 뜨거나 숨지 않음)
        var arcS = Line(WallSection.Kind.찰쌓기, 1, Enumerable.Range(0, 31).Select(i => { double a = i * 3 * Math.PI / 180; return (15 * Math.Sin(a), 15 - 15 * Math.Cos(a), 100.0, 2.8); }));
        var ls = WallFacePattern.Lay(WallSweep.Make(arcS), WallFacePattern.석축찰);
        check("S174 ④ 곡선 석축(1:0.3) — 판 비틀림 < 내밀기(5 cm) · 조각 있음 · 실패 0",
            ls.TwistMax < WallFacePattern.석축찰.Relief && ls.Pieces.Count > 50 && ls.ClipFail == 0, $"비틀림 최대 {ls.TwistMax * 100:F1} cm · 판 {ls.Sheets.Count} · 조각 {ls.Pieces.Count}");

        // ⑤ 앵커식 — ★[JACK 1027 «가운데 앵커부분은 해당 부분이 조금이라도 잘리지 않는 한 다 표현»] 머리(0.56 □)가 다 보이는(앞 땅 위 · 꼭대기 아래 · 줄 안) 패널 칸에는
        //   빠짐없이 머리 셋(도넛 0.56 · 0.36 · 정착판 0.15)이 온전한 넓이로 · 그 밖에는 없다 · 패널 칸은 잘렸어도 머리가 다 보이면 있다
        var pan = Line(WallSection.Kind.패널식, 1, Enumerable.Range(0, 11).Select(i => (i * 2.0, 0.0, 100.0, 3.0 + i * 0.4)));
        var plP = WallSweep.Make(pan);
        var lp = WallFacePattern.Lay(plP, WallFacePattern.패널);
        // 몸통 꼭대기(줄 전체 — 덩어리마다 직선 · 같은 x면 높은 쪽) · 앞 땅 100
        double TopAt(double x)
        {
            double t = double.MinValue;
            foreach (var st in plP.Runs)
                for (int k = 0; k + 1 < st.Count; k++)
                {
                    if (x < st[k].X - 1e-9 || x > st[k + 1].X + 1e-9) continue;
                    double f = Math.Clamp((x - st[k].X) / (st[k + 1].X - st[k].X), 0, 1);
                    t = Math.Max(t, st[k].Toe + st[k].Shape.ZTop + (st[k + 1].Toe + st[k + 1].Shape.ZTop - st[k].Toe - st[k].Shape.ZTop) * f);
                }
            return t;
        }
        // 찾은 머리 — 패널 칸(1.5 m 격자)마다 겉 내밀기별 넓이 합(조각이 덩어리 경계에서 나뉘어도 합)
        var found = new Dictionary<(long I, long J), Dictionary<double, double>>();
        foreach (var hd in lp.Pieces.Where(p => p.Layer == WallFacePattern.AnchorLayer))
        {
            var s = lp.Sheets[hd.Sheet];
            double cu = hd.Poly.Average(p => p.U) + s.UG0, cz = W(s, 0, hd.Poly.Average(p => p.V), 0).Z;
            var key = ((long)Math.Floor(cu / 1.5), (long)Math.Floor(cz / 1.5));
            if (!found.TryGetValue(key, out var byOut)) found[key] = byOut = new();
            byOut[hd.Relief] = (byOut.TryGetValue(hd.Relief, out double a0) ? a0 : 0) + WallFacePattern.Area(hd.Poly) * s.V.Z;   // 틀 v는 면 위 길이 — 절대 높이 넓이로
        }
        // 기대 머리 — 머리 네모(가운데 ± 0.28)가 x 0 ~ 20 · 앞 땅 100 위 · 꼭대기 아래(네모 안 몸통 꼭대기 최소)
        int expect5 = 0, missing = 0, extra = 0, partial = 0, onCut = 0;
        for (long i = 0; i < 14; i++)
            for (long j = 64; j < 72; j++)
            {
                double cu = i * 1.5 + 0.75, cz = j * 1.5 + 0.75, h = 0.28;
                double topMin = Math.Min(TopAt(cu - h), Math.Min(TopAt(cu), TopAt(cu + h)));
                bool vis = cu - h >= 0 && cu + h <= 20 && cz - h >= 100 && cz + h <= topMin;
                bool has = found.ContainsKey((i, j));
                if (vis) { expect5++; if (!has) missing++; }
                else if (has) extra++;
                if (has)
                {
                    var byOut = found[(i, j)];
                    bool full = byOut.Count == 3 && Math.Abs(byOut[0.07] - 0.56 * 0.56) < 1e-6 && Math.Abs(byOut[0.12] - 0.36 * 0.36) < 1e-6 && Math.Abs(byOut[0.14] - 0.15 * 0.15) < 1e-6;
                    if (!full) partial++;
                    // 패널 칸 자체는 잘림(꼭대기 아래로 1.5 m를 다 못 채움 · 앞 땅 아래로 내려감)인데 머리는 있음 — 새 규칙의 꼴
                    double z0 = j * 1.5, z1 = z0 + 1.5;
                    if (z1 > Math.Min(TopAt(i * 1.5), TopAt(i * 1.5 + 1.5)) || z0 < 100 - WallFacePattern.Bury) onCut++;
                }
            }
        check("S174 ⑤ 앵커식 — 머리가 다 보이는 칸엔 빠짐없이 머리 셋(넓이 그대로) · 잘리는 자리엔 없음 · 잘린 패널 칸에도 머리가 다 보이면 있음",
            expect5 > 0 && missing == 0 && extra == 0 && partial == 0 && onCut > 0 && lp.Pieces.Count > found.Count * 3,
            $"기대 {expect5} · 빠짐 {missing} · 덤 {extra} · 덜 그림 {partial} · 잘린 칸 위 머리 {onCut} · 뺀 머리 조각 {lp.AnchorSkipped} · 조각 {lp.Pieces.Count}");

        // ⑥ 앞면 맞춤 전단(계획 2판 · 검토 높음 2) — 지형 벽 1:0.01(앞면 선 y = 0 · 흙 쪽 +y) vs 단면 1:0.02 · 5 m
        var cov = Line(T, 1, Enumerable.Range(0, 6).Select(i => (i * 2.0, 0.0, 100.0, 5.0)));
        double? Tin(double x, double y)
        {
            if (y <= 0) return 100.0;                                  // 앞 땅
            double z = 100 + y / 0.01;                                  // 벽면 y = 0.01·(z − 100)
            return Math.Min(z, 105.0);                                  // 꼭대기 뒤 평평
        }
        var cn0 = new WallSweep.CoverNote(); WallSweep.CoverCheck(cov, Tin, cn0, lean: false);
        var cn = new WallSweep.CoverNote(); WallSweep.CoverCheck(cov, Tin, cn);
        double worst = double.MaxValue;
        foreach (var p in cov.Pts)
            for (double z = 0.05; z <= 4.99; z += 0.05)
            {
                double b = WallSweep.LeanOf(p.Shape, cov.SlopeW);
                double x3 = WallSweep.FrontAt(p.Shape, z) - WallSweep.FrontOut - b * z;   // 흙 쪽 + (y)
                worst = Math.Min(worst, 0.01 * z - x3);
            }
        check("S174 ⑥ 전단 — 1:0.01 지형 벽 · 1:0.02 단면 · 5 m: 전단 없으면 지형이 앞(1 cm 넘음 > 0) · 전단 뒤 3D 앞면이 모든 높이에서 지형보다 ≥ 2 cm 앞 · 자가점검 앞선 표본 0",
            cn0.Ahead > 0 && worst >= 0.0199 && cn.Ahead == 0 && cn.Measured == cn.Points,
            $"전단 없을 때 앞선 표본 {cn0.Ahead}/{cn0.Measured}(최대 {cn0.Max * 100:F1} cm) · 전단 뒤 가장 덜 덮은 곳 {worst * 100:F2} cm · 앞선 {cn.Ahead}/{cn.Measured}");
        // 석축 1:0.3(정지면 벽 1:0.01) — 벽 밑(앞 땅 자리)은 그대로 · 앞면은 지형 벽과 나란히 · b·h 큼 ⚠
        var stone = Line(WallSection.Kind.찰쌓기, 1, Enumerable.Range(0, 6).Select(i => (i * 2.0, 0.0, 100.0, 2.8)));
        var plS = WallSweep.Make(stone); var plS0 = WallSweep.Make(stone, lean: false);
        var s0 = plS.Runs[0][0]; var s00 = plS0.Runs[0][0];
        var foot = WallSweep.World(s0, s0.Shape.FaceX0, 0); var foot0 = WallSweep.World(s00, s00.Shape.FaceX0, 0);
        var topF = WallSweep.World(s0, s0.Shape.FaceX0 + s0.Shape.FaceN * s0.Shape.ZTop, s0.Shape.ZTop);
        var lr = WallSweep.LeanReport(plS, "시험");
        check("S174 ⑥ 전단 석축 1:0.3 — 벽 밑(앞 땅) 자리 그대로 · 꼭대기 앞면 = 지형 벽(1:0.01) − 2 cm · b·h > 0.10 m ⚠ 셈",
            Math.Abs(foot.Y - foot0.Y) < 1e-12 && Math.Abs(topF.Y - (0.01 * s0.Shape.ZTop - 0.02)) < 1e-9 && lr.Over > 0 && s0.Shape.FaceN > 0.1,
            $"앞면 1:{s0.Shape.FaceN:0.##} · b {s0.Lean:F3} · 꼭대기 앞 y {topF.Y:F4} · b·h 최대 {lr.Max:F2} m · 넘음 {lr.Over}");
        // 지형 벽이 이미 3D보다 뒤(1:0.05 지형 · 1:0.02 단면) — 전단 없음 · 앞선 표본 0
        var cov2 = Line(T, 1, Enumerable.Range(0, 6).Select(i => (i * 2.0, 0.0, 100.0, 5.0)));
        cov2.SlopeW = 0.05;
        double? Tin2(double x, double y) => y <= 0 ? 100.0 : Math.Min(100 + y / 0.05, 105.0);
        var cn2 = new WallSweep.CoverNote(); WallSweep.CoverCheck(cov2, Tin2, cn2);
        check("S174 ⑥ 전단 — 지형 벽이 단면보다 누우면 b = 0 · 앞선 표본 0", WallSweep.Make(cov2).Runs.All(r => r.All(x => x.Lean == 0)) && cn2.Ahead == 0, $"앞선 {cn2.Ahead}/{cn2.Measured}");
        // 꼭대기 올림 — 몸통 꼭대기 꼭짓점만 +1 cm(무늬 · 앞면 점은 그대로)
        var st6 = WallSweep.Make(cov).Runs[0][0];
        double zt = st6.Shape.ZTop;
        check("S174 ⑥ 꼭대기 올림 — lift면 꼭대기 꼭짓점 +1 cm · 아니면 그대로 · 꼭대기 아래는 그대로",
            Math.Abs(WallSweep.World(st6, 0, zt, lift: true).Z - (st6.Toe + zt + WallSweep.TopLift)) < 1e-12 && Math.Abs(WallSweep.World(st6, 0, zt).Z - (st6.Toe + zt)) < 1e-12
            && Math.Abs(WallSweep.World(st6, 0, zt - 0.5, lift: true).Z - (st6.Toe + zt - 0.5)) < 1e-12, "");

        // ⑦ 무늬 좌표 이음 — 직각 꺾임 두 덩어리 · 판마다 UG0는 덩어리 시작 0에서
        var lc = WallFacePattern.Lay(WallSweep.Make(bend), WallFacePattern.자연석);
        check("S174 ⑦ 직각 꺾임 — 덩어리 둘 · 판 둘 · 조각 양쪽 · 실패 0", lc.Sheets.Select(s => s.Run).Distinct().Count() == 2 && lc.Sheets.Count == 2 && lc.ClipFail == 0
            && lc.Pieces.Any(p => p.Sheet == 0) && lc.Pieces.Any(p => p.Sheet == 1), $"판 {lc.Sheets.Count} · 조각 {lc.Pieces.Count}");
        // ⑦ 꺾인 점을 걸친 조각은 늘 잘린다(재검토 중간 ④) — 조각 u가 제 판 보이는 칸 u 범위 밖으로 안 나감(모서리 너머 허공 0)
        int over7 = lc.Pieces.Count(p =>
        {
            var reg = lc.Sheets[p.Sheet].Region;
            double r0 = reg.Min(q => q.U), r1 = reg.Max(q => q.U);
            return p.Poly.Any(q => q.U < r0 - 1e-6 || q.U > r1 + 1e-6);
        });
        check("S174 ⑦ 꺾인 점 — 판 칸 u 범위 밖 조각 0(모서리 너머로 안 나감)", over7 == 0, $"넘은 조각 {over7}");
        // ⑦ 꺾인 점 쪽 끝은 내밀기 + 1 cm 물림(검토 낮음 5 — 무늬 끝면이 모서리 밖 톱니로 안 보이게) · 반대쪽(벽 끝)은 안 물림
        {
            double inset = WallFacePattern.자연석.Relief + 0.01, worstC = double.MaxValue, worstE = double.MaxValue;
            foreach (var pc in lc.Pieces)
            {
                var s = lc.Sheets[pc.Sheet];
                var reg = s.Region;
                double r0 = reg.Min(q => q.U), r1 = reg.Max(q => q.U);
                var w0 = W(s, r0, 0, 0); var w1 = W(s, r1, 0, 0);
                bool cornerAtR1 = Math.Pow(w1.X - 10, 2) + Math.Pow(w1.Y, 2) < Math.Pow(w0.X - 10, 2) + Math.Pow(w0.Y, 2);
                foreach (var (u, _) in pc.Poly)
                {
                    worstC = Math.Min(worstC, cornerAtR1 ? r1 - u : u - r0);
                    worstE = Math.Min(worstE, cornerAtR1 ? u - r0 : r1 - u);
                }
            }
            check("S174 ⑦ 꺾인 점 쪽 무늬 물림 ≥ 내밀기 + 1 cm · 벽 끝 쪽은 그대로(0까지)", worstC >= inset - 1e-6 && worstE < 0.05,
                $"모서리 쪽 가장 가까운 조각 {worstC * 100:F1} cm(물림 {inset * 100:F1}) · 벽 끝 쪽 {worstE * 100:F1} cm");
        }
        // ⑧ 완만한 곡선(반지름 8 m · 4°) 자연석 — 판 경계 넘어 옮긴 조각의 벗어남 ≤ 1.5 cm · 나눈 조각 있음 · 판 칸 밖 0
        var arc8 = Line(T, 1, Enumerable.Range(0, 23).Select(i => { double a = i * 4 * Math.PI / 180; return (8 * Math.Sin(a), 8 - 8 * Math.Cos(a), 100.0 + 0.05 * i, 3.5); }));
        var l8 = WallFacePattern.Lay(WallSweep.Make(arc8), WallFacePattern.자연석);
        check("S174 ⑧ 곡선 자연석 — 옮긴 조각 벗어남 ≤ 1.5 cm · 실패 0 · 조각 수 / 넓이 ≤ 12(나눔 몫 포함)",
            l8.DevMax <= WallFacePattern.SplitTol + 1e-12 && l8.ClipFail == 0 && l8.Pieces.Count / l8.Sheets.Sum(s => WallFacePattern.Area(s.Region)) <= 12,
            $"판 {l8.Sheets.Count} · 조각 {l8.Pieces.Count}(나눔 {l8.Split}) · 벗어남 최대 {l8.DevMax * 100:F2} cm · 조각/㎡ {l8.Pieces.Count / l8.Sheets.Sum(s => WallFacePattern.Area(s.Region)):F1}");

        // ⑧b ★[설치 전 검토 낮음 L8] 흙 쪽이 오른쪽(Soil −1 — 무늬 u가 줄 차례와 반대로 줄어듦) — 칸 밖 0 · 덮은 비율 · 결정성 · 앵커 머리 다 보이는 자리에만
        {
            var mir = Line(T, -1, Enumerable.Range(0, 11).Select(i => (i * 2.0, 0.0, 100.0 + i * 0.1, 4.0)));
            var plM = WallSweep.Make(mir);
            var lm = WallFacePattern.Lay(plM, WallFacePattern.자연석);
            int outM = 0; double areaM = 0;
            foreach (var pc in lm.Pieces)
            {
                var s = lm.Sheets[pc.Sheet];
                areaM += WallFacePattern.Area(pc.Poly);
                foreach (var (u, v) in pc.Poly)
                {
                    var q = W(s, u, v, 0);
                    double toe = 100 + q.X / 2 * 0.1;
                    if (q.X < -1e-3 || q.X > 20 + 1e-3 || q.Z < toe - WallFacePattern.Bury - 2e-3 || q.Z > toe + 4 + 2e-3) outM++;
                }
            }
            double ratioM = areaM / lm.Sheets.Sum(s => WallFacePattern.Area(s.Region));
            var lm2 = WallFacePattern.Lay(WallSweep.Make(mir), WallFacePattern.자연석);
            bool sameM = lm2.Pieces.Count == lm.Pieces.Count && lm.Pieces.Zip(lm2.Pieces).All(z => z.First.Poly.SequenceEqual(z.Second.Poly));
            check("S174 ⑧b 흙 쪽 오른쪽(Soil −1) 자연석 — 칸 밖 꼭짓점 0 · 덮은 비율 0.75 ~ 0.95 · 결정성 · W 공기 쪽(+y)",
                outM == 0 && ratioM > 0.75 && ratioM < 0.95 && sameM && lm.Sheets.All(s => s.W.Y > 0.99) && lm.ClipFail == 0,
                $"조각 {lm.Pieces.Count} · 칸 밖 {outM} · 비율 {ratioM:F3} · W {lm.Sheets[0].W}");
            var panM = Line(WallSection.Kind.패널식, -1, Enumerable.Range(0, 11).Select(i => (i * 2.0, 0.0, 100.0, 3.0 + i * 0.4)));
            var lpm = WallFacePattern.Lay(WallSweep.Make(panM), WallFacePattern.패널);
            int badM = 0;
            foreach (var hd in lpm.Pieces.Where(p => p.Layer == WallFacePattern.AnchorLayer))
            {
                var s = lpm.Sheets[hd.Sheet];
                foreach (var (u, v) in hd.Poly) { var q = W(s, u, v, 0); if (q.Z < 100 - 1e-6 || q.Z > 103 + q.X / 2 * 0.4 + 0.25 || q.X < -1e-6 || q.X > 20 + 1e-6) badM++; }
            }
            check("S174 ⑧b 흙 쪽 오른쪽(Soil −1) 앵커식 — 머리 있음 · 머리가 앞 땅 아래 · 벽 밖으로 안 나감",
                lpm.Pieces.Any(p => p.Layer == WallFacePattern.AnchorLayer) && badM == 0 && lpm.ClipFail == 0, $"머리 조각 {lpm.Pieces.Count(p => p.Layer == WallFacePattern.AnchorLayer)} · 벗어난 꼭짓점 {badM}");
        }

        // ⑨ SHP — 옹벽 변환이 실제로 바꾼 자리(합성 전 면과 1 cm 넘게 다른 최종면 삼각형) · 띠에서 뺀 넓이
        {
            var tris = new List<WallDaylight.Tri>();
            for (int i = 0; i < 10; i++)
                for (int j = 0; j < 10; j++)
                {
                    double z = i >= 3 && i < 5 ? 1.0 : 0.0;            // x 3 ~ 5 띠만 바뀜
                    tris.Add(new(new Point3(i, j, z), new Point3(i + 1, j, z), new Point3(i + 1, j + 1, z)));
                    tris.Add(new(new Point3(i, j, z), new Point3(i + 1, j + 1, z), new Point3(i, j + 1, z)));
                }
            var gf9 = NtsSupport.Factory();
            NetTopologySuite.Geometries.Geometry Box(double x0, double y0, double x1, double y1) => gf9.CreatePolygon(new[] {
                new NetTopologySuite.Geometries.Coordinate(x0, y0), new NetTopologySuite.Geometries.Coordinate(x1, y0), new NetTopologySuite.Geometries.Coordinate(x1, y1),
                new NetTopologySuite.Geometries.Coordinate(x0, y1), new NetTopologySuite.Geometries.Coordinate(x0, y0) });
            var m1 = GradingPolygons.ChangedMask(tris, (x, y) => 0.0, Box(-1, -1, 11, 11), 0.01, out int n1);
            var m2 = GradingPolygons.ChangedMask(tris, (x, y) => 0.0, Box(-1, -1, 11, 5), 0.01, out int n2);
            var m3 = GradingPolygons.ChangedMask(tris, (x, y) => x < 4 ? 0.0 : null, Box(-1, -1, 11, 11), 0.01, out int n3);   // 합성 전 면이 없는 자리(x ≥ 4) = 바뀜
            check("S174 ⑨ SHP 바뀐 자리 — 1 cm 넘게 다른 삼각형만(띠 20 ㎡) · 범위 안 무게중심만(10 ㎡) · 합성 전 면 없는 자리(x ≥ 4 · 60 ㎡)는 바뀜 + 바뀐 띠 남은 몫(10 ㎡)",
                m1 != null && Math.Abs(m1.Area - 20) < 1e-6 && n1 == 40 && m2 != null && Math.Abs(m2.Area - 10) < 1e-6 && m3 != null && Math.Abs(m3.Area - (10 + 60)) < 1e-6,
                $"{m1?.Area:F2}/{n1} · {m2?.Area:F2}/{n2} · {m3?.Area:F2}/{n3}");
            // 띠에서 뺌 — 네모 링 셋(계획 0..2 · 사면 2..6 · 소단 6..8 둘레) · 바뀐 자리 x 3 ~ 5 띠
            List<Point3> Sq(double a) => new() { new(-a, -a, 0), new(10 + a, -a, 0), new(10 + a, 10 + a, 0), new(-a, 10 + a, 0) };
            var rings9 = new List<IReadOnlyList<Point3>> { Sq(0), Sq(2), Sq(3), Sq(5) };
            var st0 = GradingPolygons.Strips(rings9, Sq(5), Sq(0), out double ma0);
            var st1 = GradingPolygons.Strips(rings9, Sq(5), Sq(0), out double ma1, extraMask: Box(3, -10, 5, 20));
            double a0 = st0.Sum(x => x.Area), a1 = st1.Sum(x => x.Area);
            check("S174 ⑨ SHP 띠에서 뺌 — 줄어든 넓이 = 띠 자리 ∩ 바뀐 자리 · 바뀐 자리 없으면 그대로",
                ma0 == 0 && ma1 > 0 && Math.Abs((a0 - a1) - ma1) < 1e-3, $"띠 {a0:F1} → {a1:F1} ㎡ · 뺀 {ma1:F1} ㎡");
        }

        // 미리보기 — BLOCKTEST_DUMP=폴더면 조각 앞면(월드)을 CSV로(그림은 파이썬)
        string? dump = Environment.GetEnvironmentVariable("BLOCKTEST_DUMP");
        if (!string.IsNullOrEmpty(dump))
        {
            Directory.CreateDirectory(dump);
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            foreach (var (nm, ln, spec) in new[]
            {
                ("자연석", Line(T, 1, Enumerable.Range(0, 13).Select(i => (i * 1.0, 0.0, 100.0 + Math.Sin(i * 0.5) * 0.3, 2.0 + i * 0.25))), WallFacePattern.자연석),
                ("블록", Line(WallSection.Kind.보강토, 1, Enumerable.Range(0, 13).Select(i => (i * 1.0, 0.0, 100.0 + Math.Sin(i * 0.5) * 0.3, 2.0 + i * 0.25)), cut: false), WallFacePattern.블록),
                ("패널", Line(WallSection.Kind.패널식, 1, Enumerable.Range(0, 13).Select(i => (i * 1.0, 0.0, 100.0 + Math.Sin(i * 0.5) * 0.3, 2.0 + i * 0.25))), WallFacePattern.패널),
                ("석축", Line(WallSection.Kind.찰쌓기, 1, Enumerable.Range(0, 13).Select(i => (i * 1.0, 0.0, 100.0 + Math.Sin(i * 0.5) * 0.3, 1.0 + i * 0.15))), WallFacePattern.석축찰),
            })
            {
                var plx = WallSweep.Make(ln);
                var lx = WallFacePattern.Lay(plx, spec);
                var sb = new System.Text.StringBuilder("piece,layer,x,y,z\n");
                int id = 0;
                foreach (var pc in lx.Pieces)
                {
                    var s = lx.Sheets[pc.Sheet];
                    foreach (var (u, v) in pc.Poly) { var q = W(s, u, v, pc.Relief); sb.Append(ci, $"{id},{pc.Layer},{q.X:F4},{q.Y:F4},{q.Z:F4}\n"); }
                    id++;
                }
                // 몸통 윤곽(앞면 아래 · 꼭대기 · 땅) — 정거장마다
                var sb2 = new System.Text.StringBuilder("x,toe,top\n");
                foreach (var p in ln.Pts) sb2.Append(ci, $"{p.X:F4},{p.Toe:F4},{p.Toe + p.H:F4}\n");
                File.WriteAllText(Path.Combine(dump, $"pat_{nm}.csv"), sb.ToString());
                File.WriteAllText(Path.Combine(dump, $"pat_{nm}_wall.csv"), sb2.ToString());
            }
            Console.WriteLine($"  미리보기 CSV → {dump}");
        }
    }
}
