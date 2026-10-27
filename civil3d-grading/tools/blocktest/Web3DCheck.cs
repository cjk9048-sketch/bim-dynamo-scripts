// ── S175 ★★★[v107 · 계획_v107.md · JACK 1027 «인프라웍스 내보내기를 3D 웹뷰로»] [3D 보기] 옹벽 메시 · 지형 구분 · 장면 묶음 ──
//   만든 꼴(현장 값 없음 — 공개 저장소)
using System.Text;
using DH.Grading.Core;
using NetTopologySuite.Geometries;

static class Web3DCheck
{
    static readonly WallSection.Common C = new(0.10, 0.10, 0.02, 0.01);

    static WallExcav.SecLine Line(WallSection.Kind k, int soil, IEnumerable<(double X, double Y, double Toe, double H)> pts, bool cut = true)
    {
        var t = WallSection.DefaultTable(k);
        var ln = new WallExcav.SecLine { Kind = k, Soil = soil, Up = cut, SlopeW = 0.01, Src = "시험 " + k };
        double s = 0; (double X, double Y)? prev = null;
        foreach (var (x, y, toe, h) in pts)
        {
            if (prev != null) s += Math.Sqrt((x - prev.Value.X) * (x - prev.Value.X) + (y - prev.Value.Y) * (y - prev.Value.Y));
            prev = (x, y);
            var row = WallSection.Lookup(t, h, out _, out _, out _, out _);
            ln.Pts.Add(new WallExcav.SecPoint { X = x, Y = y, S = s, Toe = toe, H = h, Row = t.Rows.IndexOf(row), Shape = WallSection.Build(t, h, C, cut) });
        }
        return ln;
    }

    static double PolyArea(IReadOnlyList<(double X, double Y)> p)
    {
        double a = 0;
        for (int i = 0; i < p.Count; i++) { var u = p[i]; var v = p[(i + 1) % p.Count]; a += u.X * v.Y - v.X * u.Y; }
        return a / 2;
    }

    public static void Run(Action<string, bool, string> check0)
    {
        void check(string name, bool ok, string detail = "") => check0(name, ok, detail);
        Console.WriteLine("\n== S175 [3D 보기] 옹벽 메시 · 지형 구분 · 장면 묶음 ==");

        // ① 덩어리 — 형식 × 절/성토 × 흙 쪽: 닫힘 · 부피 > 0 · 부피 = 단면 넓이 × 길이(곧은 벽 · 한 표 줄 · 전단은 넓이 그대로)
        {
            int n = 0, bad = 0; string first = "";
            foreach (WallSection.Kind k in Enum.GetValues<WallSection.Kind>())
                foreach (bool cut in new[] { true, false })
                    foreach (int soil in new[] { 1, -1 })
                    {
                        WallExcav.SecLine ln;
                        try { ln = Line(k, soil, Enumerable.Range(0, 11).Select(i => (i * 1.0, 0.0, 100.0, 3.0)), cut); }
                        catch { continue; }
                        var plan = WallSweep.Make(ln);
                        foreach (var run in plan.Runs)
                        {
                            var lofts = WallMesh.Loft(run);
                            var parts = WallSweep.Solids(run[0].Shape);   // 같은 재료 조각이 둘일 수 있다(역T 벽체 · 기초) — 차례로 짝짓는다
                            for (int pi = 0; pi < lofts.Count; pi++)
                            {
                                var (mat, m) = lofts[pi];
                                n++;
                                if (m == null) { bad++; if (first.Length == 0) first = $"{k} {mat} 링 꼴"; continue; }
                                // 꼭대기 꼭짓점 1 cm 올림(TopLift) 뒤 넓이 — 전단은 넓이 그대로
                                double zt = run[0].Shape.ZTop;
                                var ring = parts[pi].Ring.Select(q => (q.X, q.Z >= zt - 1e-9 ? q.Z + WallSweep.TopLift : q.Z)).ToList();
                                double want = Math.Abs(WallSection.Area(ring)) * 10.0, vol = m.Volume();
                                int open = m.OpenEdges();
                                bool ok = open == 0 && vol > 0 && Math.Abs(vol - want) / want < 1e-6 && m.FanFallback == 0;
                                if (!ok) { bad++; if (first.Length == 0) first = $"{k} {(cut ? "절" : "성")} soil {soil} {mat}: 열린 변 {open} · 부피 {vol:F4} / {want:F4} · 부채꼴 {m.FanFallback}"; }
                            }
                        }
                    }
            check("S175 ① 덩어리 메시 — 모든 형식 × 절/성토 × 흙 쪽: 닫힘 · 부피 > 0(바깥 맞춤) · 부피 = 단면 넓이 × 길이(1e-6) · 마구리 귀 자르기",
                bad == 0 && n > 20, $"{n - bad}/{n}{(first.Length > 0 ? " · 첫 틀림 " + first : "")}");
        }

        // ② 모서리 메움 — 닫힘 · 부피 ≈ 파푸스(반쪽 단면 넓이 × 중심 반지름 × 각 · 5° 조각 0.3% 안)
        {
            int n = 0, bad = 0; string first = ""; double worst = 0;
            foreach (var (k, cut) in new[] { (WallSection.Kind.역T형, true), (WallSection.Kind.보강토, false), (WallSection.Kind.패널식, true), (WallSection.Kind.찰쌓기, true), (WallSection.Kind.중력식, false) })
                foreach (int soil in new[] { 1, -1 })
                    foreach (double turn in new[] { 90.0, -90.0, 30.0, -135.0 })
                    {
                        double a = turn * Math.PI / 180;
                        var pts = new List<(double, double, double, double)>();
                        for (int i = 0; i <= 5; i++) pts.Add((i * 1.0, 0.0, 100.0, 3.0));
                        for (int i = 1; i <= 5; i++) pts.Add((5 + i * Math.Cos(a), i * Math.Sin(a), 100.0, 3.0));
                        var ln = Line(k, soil, pts, cut);
                        var plan = WallSweep.Make(ln);
                        foreach (var c in plan.Corners)
                        {
                            var sta = new WallSweep.Sta(0, 0, 0, 1, 0, c.P.Shape, 1.0, c.Lean, -1);
                            foreach (var part in WallSweep.Solids(c.P.Shape))
                            {
                                var ring = part.Ring.Select(q => { var w = WallSweep.World(sta, q.X, q.Z, true); return (X: w.X, Z: w.Z); }).ToList();
                                foreach (bool sl in new[] { true, false })
                                {
                                    var half = WallMesh.ClipHalf(ring, sl);
                                    if (half.Count < 3 || Math.Abs(WallSection.Area(half)) < 1e-6) continue;
                                    double mx = 0, ar = 0;
                                    for (int i = 0; i < half.Count; i++)
                                    {
                                        var p = half[i]; var q = half[(i + 1) % half.Count];
                                        double cr = p.X * q.Z - q.X * p.Z;
                                        ar += cr; mx += (p.X + q.X) * cr;
                                    }
                                    double pappus = Math.Abs(mx / 6) * Math.Abs(c.Ang);   // ∫∫|x| dA · 각(반쪽은 축 한쪽)
                                    // 같은 반쪽을 Core가 지은 메시와 견준다
                                    var got = WallMesh.Corner(c).Where(z => z.Mat == part.Material).Select(z => z.M).ToList();
                                    var m = got.FirstOrDefault(g => Math.Abs(Math.Abs(g.Volume()) - pappus) / pappus < 0.003);
                                    n++;
                                    if (m == null || m.OpenEdges() != 0 || m.Volume() <= 0)
                                    {
                                        bad++;
                                        if (first.Length == 0) first = $"{k} soil {soil} 꺾임 {turn}: 파푸스 {pappus:F4} · 지은 {string.Join("/", got.Select(g => g.Volume().ToString("F4")))} · 열린 변 {string.Join("/", got.Select(g => g.OpenEdges()))}";
                                    }
                                    else worst = Math.Max(worst, Math.Abs(m.Volume() - pappus) / pappus);
                                }
                            }
                        }
                    }
            check("S175 ② 모서리 메움 메시 — 닫힘 · 부피 > 0 · 부피 ≈ 파푸스(5° 조각 · 0.3% 안) · 꺾임 ±90 · 30 · −135 · 흙 쪽 둘",
                bad == 0 && n > 20, $"{n - bad}/{n} · 최대 차 {worst * 100:F3}%{(first.Length > 0 ? " · 첫 틀림 " + first : "")}");
        }

        // ③ 무늬 조각 — 닫힘 · 부피 = 넓이 × (내밀기 + 묻힘) · 형식 넷(자연석 · 블록 · 패널 + 앵커 · 돌)
        {
            int n = 0, bad = 0; string first = "";
            foreach (var (k, soil) in new[] { (WallSection.Kind.역T형, 1), (WallSection.Kind.보강토, -1), (WallSection.Kind.패널식, 1), (WallSection.Kind.찰쌓기, -1) })
            {
                var ln = Line(k, soil, Enumerable.Range(0, 13).Select(i => (i * 1.0, 0.0, 100.0 + 0.05 * i, 3.0 + 0.1 * i)));
                var spec = WallFacePattern.SpecOf(k);
                if (spec == null) continue;
                var lay = WallFacePattern.Lay(WallSweep.Make(ln), spec);
                foreach (var pc in lay.Pieces)
                {
                    var m = WallMesh.Piece(lay.Sheets[pc.Sheet], pc);
                    var poly = pc.Rect
                        ? new List<(double, double)> { (pc.Poly.Min(q => q.U), pc.Poly.Min(q => q.V)), (pc.Poly.Max(q => q.U), pc.Poly.Min(q => q.V)), (pc.Poly.Max(q => q.U), pc.Poly.Max(q => q.V)), (pc.Poly.Min(q => q.U), pc.Poly.Max(q => q.V)) }
                        : pc.Poly.Select(q => (q.U, q.V)).ToList();
                    double want = Math.Abs(PolyArea(poly)) * (pc.Relief + pc.Embed), vol = m.Volume();
                    n++;
                    if (m.OpenEdges() != 0 || vol <= 0 || Math.Abs(vol - want) > 1e-9 + want * 1e-6 || m.FanFallback > 0)
                    { bad++; if (first.Length == 0) first = $"{k} {pc.Layer}: 열린 변 {m.OpenEdges()} · 부피 {vol:E3} / {want:E3} · 부채꼴 {m.FanFallback}"; }
                }
            }
            check("S175 ③ 무늬 조각 메시 — 닫힘 · 부피 > 0 · 부피 = 넓이 × (내밀기 + 묻힘) · 자연석 · 블록 · 패널 + 앵커 · 돌",
                bad == 0 && n > 200, $"{n - bad}/{n}{(first.Length > 0 ? " · 첫 틀림 " + first : "")}");
        }

        // ④ 귀 자르기 — 오목 다각형(L · U · 톱니) · 감김 지킴 · 넓이 합 = 다각형 넓이 · 일직선 점 섞임
        {
            var shapes = new List<List<(double X, double Y)>>
            {
                new() { (0, 0), (4, 0), (4, 1), (1, 1), (1, 3), (0, 3) },
                new() { (0, 0), (5, 0), (5, 4), (4, 4), (4, 1), (1, 1), (1, 4), (0, 4) },
                new() { (0, 0), (1, 1), (2, 0), (3, 1), (4, 0), (4, 2), (0, 2) },
                new() { (0, 0), (1, 0), (2, 0), (3, 0), (3, 2), (2, 2), (2, 1), (1, 1), (1, 2), (0, 2) },
            };
            int bad = 0; string first = "";
            foreach (var s0 in shapes)
                foreach (bool rev in new[] { false, true })
                {
                    var s = rev ? s0.AsEnumerable().Reverse().ToList() : s0;
                    var m = new WallMesh.Mesh();
                    var id = s.Select(q => m.Add(new Point3(q.X, q.Y, 0))).ToList();
                    m.Poly(id, s);
                    double sum = 0; bool wind = true;
                    for (int i = 0; i < m.T.Count; i += 3)
                    {
                        var a = m.V[m.T[i]]; var b = m.V[m.T[i + 1]]; var c = m.V[m.T[i + 2]];
                        double cr = ((b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X)) / 2;
                        sum += cr;
                        if (rev ? cr > 0 : cr < 0) wind = false;
                    }
                    double want = PolyArea(s);
                    if (Math.Abs(sum - want) > 1e-9 || !wind || m.FanFallback > 0) { bad++; if (first.Length == 0) first = $"{s.Count}각 {(rev ? "역" : "정")}: {sum} / {want} · 감김 {wind} · 부채꼴 {m.FanFallback}"; }
                }
            check("S175 ④ 귀 자르기 — 오목 L · U · 톱니 · 일직선 점: 넓이 합 = 다각형 · 감김 그대로 · 부채꼴 0", bad == 0, bad == 0 ? "8/8" : first);
        }

        // ⑤ 장면 묶음 — 쓰고 읽기 · 페이지 틀 채우기(자리 차례 · 제목 이스케이프 · 조각 base64 = 한 번에)
        {
            var sc = new Web3DScene();
            var f = new float[] { 1.5f, -2.25f, float.NaN, 123456.75f };
            var u = new[] { 0, 1, 2, 70000 };
            var img = Enumerable.Range(0, 1001).Select(i => (byte)(i * 7)).ToArray();
            sc.Head["a"] = sc.F32(f); sc.Head["b"] = sc.U32(u); sc.Head["c"] = sc.Img(img, "image/jpeg"); sc.Head["n"] = Web3DScene.Num(double.NaN);
            var gz = sc.PackGzip();
            var (doc, buf, bo) = Web3DScene.Unpack(gz);
            var f2 = Web3DScene.ReadF32(doc.RootElement.GetProperty("a"), buf, bo);
            var u2 = Web3DScene.ReadU32(doc.RootElement.GetProperty("b"), buf, bo);
            var cEl = doc.RootElement.GetProperty("c");
            var img2 = buf.AsSpan(bo + cEl.GetProperty("o").GetInt32(), cEl.GetProperty("n").GetInt32()).ToArray();
            bool rt = f2.Length == 4 && f2[0] == 1.5f && f2[1] == -2.25f && float.IsNaN(f2[2]) && f2[3] == 123456.75f && u2.SequenceEqual(u) && img2.SequenceEqual(img)
                      && doc.RootElement.GetProperty("n").GetDouble() == 0 && bo % 4 == 0 && cEl.GetProperty("o").GetInt32() % 4 == 0;
            check("S175 ⑤ 장면 묶음 — 쓰고 읽어 같음(f32 · u32 · 그림 · NaN 머리는 0) · 4바이트 맞춤", rt, $"gz {gz.Length} B");

            var big = new byte[7 * 1024 * 1024 + 5];
            new Random(5).NextBytes(big);
            var sw = new StringWriter();
            Web3DScene.WritePage(sw, "<title>__TITLE__</title><x>__DATA__</x><s>/*__VIEWER__*/</s>", "var a='__DATA__';", "옹벽 <A&B>", big);
            string pg = sw.ToString();
            string b64 = pg.Substring(pg.IndexOf("<x>") + 3, pg.IndexOf("</x>") - pg.IndexOf("<x>") - 3);
            bool page = pg.StartsWith("<title>옹벽 &lt;A&amp;B&gt;</title>") && b64 == Convert.ToBase64String(big) && pg.EndsWith("<s>var a='__DATA__';</s>");
            bool refused = false;
            try { Web3DScene.WritePage(new StringWriter(), "__TITLE__ __DATA__ /*__VIEWER__*/", "x='</script>'", "t", big); } catch (InvalidDataException) { refused = true; }
            check("S175 ⑤ 페이지 — 자리 차례로 한 번씩(뷰어 속 같은 글자 그대로) · 제목 이스케이프 · 조각 base64 = 한 번에 · </script 든 뷰어 거부", page && refused, $"{pg.Length:N0} 글자");
        }

        // ⑥ 지형 구분 — 평평한 원지반(0) · 가운데 4×4 m를 1 m 돋움(계획면) · 둘레 띠(사면) · 세 점 기준 · 구분마다 점 따로 · 위에서 반시계
        {
            int N = 20; double cell = 1.0;
            var X = new List<double>(); var Y = new List<double>(); var Z = new List<double>();
            for (int j = 0; j <= N; j++)
                for (int i = 0; i <= N; i++)
                {
                    X.Add(i * cell + 1000); Y.Add(j * cell + 2000);
                    bool up = i >= 8 && i <= 12 && j >= 8 && j <= 12;
                    bool ring = !up && i >= 7 && i <= 13 && j >= 7 && j <= 13;
                    Z.Add(up ? 1.0 : ring ? 0.5 : (i == 3 && j == 3 ? 0.004 : 0.0));   // (3,3)은 4 mm — 1 cm 안이라 원지반
                }
            var F = new List<int>();
            for (int j = 0; j < N; j++)
                for (int i = 0; i < N; i++)
                {
                    int a = j * (N + 1) + i, b = a + 1, c = a + N + 1, d = c + 1;
                    if ((i + j) % 2 == 0) { F.AddRange(new[] { a, b, d }); F.AddRange(new[] { a, d, c }); }
                    else { F.AddRange(new[] { a, c, b }); F.AddRange(new[] { b, c, d }); }   // 절반은 시계 — 반시계로 고쳐 나와야
                }
            var gf = NtsSupport.Factory();
            Geometry Box(double x0, double y0, double x1, double y1) => gf.CreatePolygon(new[] { new Coordinate(x0, y0), new Coordinate(x1, y0), new Coordinate(x1, y1), new Coordinate(x0, y1), new Coordinate(x0, y0) });
            var inp = new TerrainMesh.Input
            {
                X = X.ToArray(), Y = Y.ToArray(), Z = Z.ToArray(), F = F.ToArray(),
                GroundZ = (x, y) => x < 1000 + N * cell - 2 ? 0.0 : null,                       // 오른쪽 끝 두 칸은 원지반 없음
                Plan = Box(1008, 2008, 1012, 2012),
                SlopeCut = Box(1007, 2007, 1013, 2013).Difference(Box(1008, 2008, 1012, 2012)),
            };
            var o = TerrainMesh.Build(inp, 1000, 2000);
            int nT = F.Count / 3;
            bool sum = o.Count.Sum() == nT && o.Idx.Length == nT * 3 && o.Ranges.Sum(r => r.Count) == nT;
            bool ccw = true;
            for (int t = 0; t < nT; t++)
            {
                int a = o.Idx[t * 3], b = o.Idx[t * 3 + 1], c = o.Idx[t * 3 + 2];
                double cr = (o.Pos[b * 3] - o.Pos[a * 3]) * (o.Pos[c * 3 + 1] - o.Pos[a * 3 + 1]) - (o.Pos[b * 3 + 1] - o.Pos[a * 3 + 1]) * (o.Pos[c * 3] - o.Pos[a * 3]);
                if (cr <= 0) ccw = false;
            }
            int plan = o.Count[(int)TerrainMesh.Cls.Plan], slope = o.Count[(int)TerrainMesh.Cls.SlopeCut], ground = o.Count[(int)TerrainMesh.Cls.Ground], other = o.Count[(int)TerrainMesh.Cls.Other];
            // 따로 센 답 — 바뀜 = 세 점 중 하나라도 1 cm 넘게 다름 · 무게중심이 계획면(8~12) → 계획면 · 띠(7~13) → 사면 · 그 밖 바뀜 → 정지
            int ePlan = 0, eSlope = 0, eOther = 0, eGround = 0;
            for (int t = 0; t < nT; t++)
            {
                int a = F[t * 3], b = F[t * 3 + 1], c = F[t * 3 + 2];
                bool ch = new[] { a, b, c }.Any(v => Math.Abs(Z[v]) > 0.01);
                double cx = (X[a] + X[b] + X[c]) / 3 - 1000, cy = (Y[a] + Y[b] + Y[c]) / 3 - 2000;
                if (!ch) eGround++;
                else if (cx > 8 && cx < 12 && cy > 8 && cy < 12) ePlan++;
                else if (cx > 7 && cx < 13 && cy > 7 && cy < 13) eSlope++;
                else eOther++;
            }
            // 돋운 4×4 칸 세모 32개 = 계획면 · 4 mm 점(3,3) 둘레 세모는 원지반
            check("S175 ⑥ 지형 구분 — 세모 수 그대로 · 구분 차례 · 위에서 반시계로 고침 · 따로 센 답과 같음(계획면 32) · 4 mm 돋움은 원지반(세 점 기준)",
                sum && ccw && plan == 32 && plan == ePlan && slope == eSlope && other == eOther && ground == eGround,
                $"원지반 {ground}/{eGround} · 계획면 {plan}/{ePlan} · 사면 {slope}/{eSlope} · 정지 {other}/{eOther} · 반시계 {ccw}");
            // 구분 경계 점은 따로 — 점 수 > 원래 점 수 · 절성토 깊이 = z − 0(원지반 없는 점은 NaN)
            bool split = o.Pos.Length / 3 > X.Count;
            bool cfOk = true; int nan = 0;
            for (int v = 0; v < o.Pos.Length / 3; v++)
            {
                double x = o.Pos[v * 3] + 1000, z = o.Pos[v * 3 + 2];
                if (x >= 1000 + N * cell - 2) { if (!float.IsNaN(o.Cf[v])) cfOk = false; else nan++; }
                else if (Math.Abs(o.Cf[v] - z) > 1e-6) cfOk = false;
            }
            check("S175 ⑥ 구분 경계 점 따로(꺾여 보이게) · 절성토 깊이 = 정지면 − 원지반 · 원지반 없는 점 NaN", split && cfOk && nan > 0, $"점 {o.Pos.Length / 3} / 원래 {X.Count} · NaN {nan}");
            // 바뀐 칸 · 원지반 층 자르기 — 바뀐 자리 둘레만
            var gtris = new List<(Point3, Point3, Point3)>();
            for (int t = 0; t < nT; t++)
            {
                int a = F[t * 3], b = F[t * 3 + 1], c = F[t * 3 + 2];
                gtris.Add((new Point3(X[a], Y[a], 0), new Point3(X[b], Y[b], 0), new Point3(X[c], Y[c], 0)));
            }
            var (gp, gi) = TerrainMesh.Clip(gtris, o, 1000, 2000);
            // 남긴 세모는 모두 바뀐 범위 + (칸 둘레 · 이웃 한 칸 · 세모 한 변) 안 · 바뀐 세모 자리는 다 남김
            double lim = 3 * o.GCell + cell * 1.5;
            bool near = true;
            for (int t = 0; t < gi.Length; t += 3)
            {
                double cx = (gp[gi[t] * 3] + gp[gi[t + 1] * 3] + gp[gi[t + 2] * 3]) / 3 + 1000, cy = (gp[gi[t] * 3 + 1] + gp[gi[t + 1] * 3 + 1] + gp[gi[t + 2] * 3 + 1]) / 3 + 2000;
                if (cx < o.CMinX - lim || cx > o.CMaxX + lim || cy < o.CMinY - lim || cy > o.CMaxY + lim) near = false;
            }
            bool clipOk = near && gi.Length / 3 >= nT - ground && o.InChanged(1010, 2010) && !o.InChanged(1001, 2001);
            check("S175 ⑥ 원지반 층 — 바뀐 칸(2 m · 둘레 한 칸) 안 세모만 · 먼 곳 없음 · 바뀐 자리는 다 덮음", clipOk, $"원지반 세모 {gi.Length / 3}/{nT} · 바뀐 세모 {nT - ground} · 바뀐 범위 {o.CMinX - 1000:F0}~{o.CMaxX - 1000:F0}");
            // 옹벽 면 — 가파른 세모: 3D 발자국 안이면 «옹벽 면»(숨김 후보) · 밖이면 «옹벽 자리»(보임)
            var X2 = new double[] { 0, 10, 0, 10, 0, 10 }; var Y2 = new double[] { 0, 0, 0, 0, 1, 1 }; var Z2 = new double[] { 0, 0, 3, 3, 3, 3 };
            X2 = new double[] { 0, 10, 0, 10, 0, 10 }; Y2 = new double[] { 0, 0, 0.05, 0.05, 1, 1 };
            var inp2 = new TerrainMesh.Input { X = X2, Y = Y2, Z = Z2, F = new[] { 0, 1, 3, 0, 3, 2, 2, 3, 5, 2, 5, 4 }, GroundZ = (x, y) => 0.0, WallMask = Box(-1, -1, 11, 2) };
            var o2 = TerrainMesh.Build(inp2, 0, 0);
            inp2.WallFoot = Box(-1, -0.5, 11, 0.5);
            var o3 = TerrainMesh.Build(inp2, 0, 0);
            // 원지반이 없으면 — 다각형으로만 가름(계획면 · 사면은 그대로 · 그 밖은 원지반 · 바뀐 범위 있음)
            var inp0 = new TerrainMesh.Input { X = inp.X, Y = inp.Y, Z = inp.Z, F = inp.F, GroundZ = null, Plan = inp.Plan, SlopeCut = inp.SlopeCut };
            var o0 = TerrainMesh.Build(inp0, 1000, 2000);
            int p0 = o0.Count[(int)TerrainMesh.Cls.Plan], s0 = o0.Count[(int)TerrainMesh.Cls.SlopeCut], g0 = o0.Count[(int)TerrainMesh.Cls.Ground];
            check("S175 ⑥ 원지반 없음 — 다각형으로만(계획면 · 사면 · 그 밖 원지반) · «정지» 0 · 바뀐 범위 = 다각형 자리",
                p0 >= 32 && s0 > 0 && p0 + s0 + g0 == nT && o0.Count[(int)TerrainMesh.Cls.Other] == 0 && o0.CMinX > 1006 && o0.CMaxX < 1014,
                $"계획면 {p0} · 사면 {s0} · 원지반 {g0} · 범위 {o0.CMinX - 1000:F0}~{o0.CMaxX - 1000:F0}");
            check("S175 ⑥ 옹벽 면 — 가파른 세모: 3D 발자국 밖이면 «옹벽 자리»(구멍 안 냄) · 안이면 «옹벽 면» · 평평한 윗면은 «옹벽 자리»",
                o2.Count[(int)TerrainMesh.Cls.WallFace] == 0 && o2.Count[(int)TerrainMesh.Cls.WallZone] == 4 && o3.Count[(int)TerrainMesh.Cls.WallFace] == 2 && o3.Count[(int)TerrainMesh.Cls.WallZone] == 2,
                $"발자국 없음: 면 {o2.Count[(int)TerrainMesh.Cls.WallFace]} · 자리 {o2.Count[(int)TerrainMesh.Cls.WallZone]} / 있음: 면 {o3.Count[(int)TerrainMesh.Cls.WallFace]} · 자리 {o3.Count[(int)TerrainMesh.Cls.WallZone]}");
        }

        // ⑧ 두 TIN 사이 절성토(Civil 체적면 없이) — 서로 다른 세모 나누기 · 0선이 세모를 가로지름 · 겹친 자리만
        {
            // 기준 z = 0(5×9 칸 · 흔든 점) · 비교 z = x − 5(7×7 칸 · 다른 흔듦) · [0,10]² — 성토 = 절토 = 10 × 12.5 = 125
            List<(Point3, Point3, Point3)> Grid(int nx, int ny, double w, double h, Func<double, double, double> z, int seed, double x0 = 0, double y0 = 0)
            {
                var rnd = new Random(seed);
                var P = new Point3[(nx + 1) * (ny + 1)];
                for (int j = 0; j <= ny; j++)
                    for (int i = 0; i <= nx; i++)
                    {
                        double x = x0 + w * i / nx, y = y0 + h * j / ny;
                        if (i > 0 && i < nx) x += (rnd.NextDouble() - 0.5) * w / nx * 0.6;
                        if (j > 0 && j < ny) y += (rnd.NextDouble() - 0.5) * h / ny * 0.6;
                        P[j * (nx + 1) + i] = new Point3(x, y, z(x, y));
                    }
                var t = new List<(Point3, Point3, Point3)>();
                for (int j = 0; j < ny; j++)
                    for (int i = 0; i < nx; i++)
                    {
                        var a = P[j * (nx + 1) + i]; var b = P[j * (nx + 1) + i + 1]; var c = P[(j + 1) * (nx + 1) + i]; var d = P[(j + 1) * (nx + 1) + i + 1];
                        if ((i + j) % 2 == 0) { t.Add((a, b, d)); t.Add((a, d, c)); } else { t.Add((a, b, c)); t.Add((b, d, c)); }
                    }
                return t;
            }
            var bas = Grid(5, 9, 10, 10, (x, y) => 0, 1, 300000, 500000);
            var cmp = Grid(7, 7, 10, 10, (x, y) => x - 300005, 2, 300000, 500000);
            var r1 = TinVolume.Compute(cmp, bas);
            // 비교면이 기준면 반만 덮음([5,15] × [0,10]) — 겹친 [5,10]에서 x − 5 ≥ 0 → 성토 = 10 × 12.5 = 125 · 절토 0 · 넓이 50
            var cmp2 = Grid(6, 4, 10, 10, (x, y) => x - 300005, 3, 300005, 500000);
            var r2 = TinVolume.Compute(cmp2, bas);
            // 기운 두 면(z = 0.3x + 0.1y + 2 대 z = −0.2x + 0.4y + 1) — 따로 센 값(가는 격자 중점 합)과
            var cmp3 = Grid(9, 6, 10, 10, (x, y) => 0.3 * (x - 300000) + 0.1 * (y - 500000) + 2, 4, 300000, 500000);
            var bas3 = Grid(4, 8, 10, 10, (x, y) => -0.2 * (x - 300000) + 0.4 * (y - 500000) + 1, 5, 300000, 500000);
            var r3 = TinVolume.Compute(cmp3, bas3);
            double ep = 0, en = 0; int N = 2000;
            for (int j = 0; j < N; j++)
                for (int i = 0; i < N; i++)
                {
                    double x = (i + 0.5) * 10.0 / N, y = (j + 0.5) * 10.0 / N, d = (0.3 * x + 0.1 * y + 2) - (-0.2 * x + 0.4 * y + 1);
                    if (d > 0) ep += d; else en -= d;
                }
            ep *= 100.0 / N / N; en *= 100.0 / N / N;
            bool ok = Math.Abs(r1.Fill - 125) < 1e-6 && Math.Abs(r1.Cut - 125) < 1e-6 && Math.Abs(r1.Area - 100) < 1e-6
                      && Math.Abs(r2.Fill - 125) < 1e-6 && r2.Cut < 1e-9 && Math.Abs(r2.Area - 50) < 1e-6
                      && Math.Abs(r3.Fill - ep) < 1e-3 && Math.Abs(r3.Cut - en) < 1e-3;
            check("S175 ⑧ 두 TIN 사이 절성토 — 서로 다른 세모 나누기에서 정확(성토 · 절토 125 · 넓이 100) · 반만 겹침(125 · 0 · 50) · 기운 두 면 = 가는 격자 합",
                ok, $"① {r1.Fill:F6}/{r1.Cut:F6}/{r1.Area:F4} · ② {r2.Fill:F6}/{r2.Cut:F6}/{r2.Area:F4} · ③ {r3.Fill:F4}/{r3.Cut:F4} 대 {ep:F4}/{en:F4}");
        }

        // ⑦ 옹벽 층 — 줄마다 몸통 · 무늬 · 정거장 표 · 발자국 · 뒤집힘 0 · 열린 변 0
        {
            var lines = new List<WallExcav.SecLine>
            {
                Line(WallSection.Kind.역T형, 1, Enumerable.Range(0, 9).Select(i => (500.0 + i, 300.0, 50.0, 2.0 + 0.4 * i))),
                Line(WallSection.Kind.보강토, -1, new[] { (500.0, 310.0, 50.0, 3.0), (504.0, 310.0, 50.0, 3.0), (504.0, 314.0, 50.0, 3.0), (508.0, 314.0, 50.0, 3.5) }, false),
                Line(WallSection.Kind.패널식, 1, Enumerable.Range(0, 7).Select(i => (520.0 + i * 0.8, 300.0 + i * 0.6, 51.0, 4.0))),
            };
            var r = WallScene.Build(lines, 500, 300, true, checkClosed: true);
            bool ok = r.Walls.Count == 3 && r.Walls.All(w => w.Parts.Count > 0 && w.Sta.Length == 5 * lines[w.Id - 1].Pts.Count)
                      && r.Pats.Count >= 3 && r.Foot != null && !r.Foot.IsEmpty && r.NegVolume == 0 && r.OpenEdges == 0 && r.Fails + r.CornerFails + r.PieceFails + r.PatLineFails == 0
                      && r.Walls[0].Sta[0] == 0f && r.Walls[0].Sta[1] == 0f;
            check("S175 ⑦ 옹벽 층 — 줄마다 몸통 · 무늬 · 정거장 표(5칸) · 발자국 · 뒤집힘 0 · 열린 변 0 · 실패 0 · 원점 뺌", ok, r.Text);
        }
    }
}
