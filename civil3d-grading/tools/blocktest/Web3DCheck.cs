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

        // ⑨ ★[v111.8 · 계획 6장 · JACK 1102] 진입도로 구분 — 차로 = «진입도로»(땅과 같은 높이여도) · 길어깨 = «길어깨» · 사면 = 절/성(정지면 기준) · 평평 = 소단 · 가파름 = 옹벽 자리 ·
        //    부지 다각형이 도로를 못 덮음 · 도로 밖은 지금과 같음 · RoadArea null이면 옛 답 그대로 · 새 열쇠는 맨 끝
        {
            const double OX = 200000, OY = 550000;
            static double Zy(double y) => y <= 4.5 ? 0 : y <= 5 ? -4 * (y - 4.5) : y <= 8.5 ? -2 + 2 * (y - 5) / 3.5 : y <= 11.5 ? 0 : y <= 13 ? (y - 11.5) : y <= 13.5 ? 1.5 + 6 * (y - 13) : y <= 15 ? 4.5 : y <= 15.5 ? 4.5 - 9 * (y - 15) : 0;
            int nx = 60, ny = 40; double d = 0.5;
            var X = new List<double>(); var Y = new List<double>(); var Z = new List<double>();
            for (int j = 0; j <= ny; j++) for (int i = 0; i <= nx; i++) { X.Add(OX + i * d); Y.Add(OY + j * d); Z.Add(Zy(j * d)); }
            var F = new List<int>();
            for (int j = 0; j < ny; j++)
                for (int i = 0; i < nx; i++)
                {
                    int a = j * (nx + 1) + i, b = a + 1, c = a + nx + 1, e = c + 1;
                    if ((i + j) % 2 == 0) { F.AddRange(new[] { a, b, e }); F.AddRange(new[] { a, e, c }); } else { F.AddRange(new[] { a, b, c }); F.AddRange(new[] { b, e, c }); }
                }
            var gf = NtsSupport.Factory();
            Geometry Box(double x0, double y0, double x1, double y1) => gf.CreatePolygon(new[] { new Coordinate(OX + x0, OY + y0), new Coordinate(OX + x1, OY + y0), new Coordinate(OX + x1, OY + y1), new Coordinate(OX + x0, OY + y1), new Coordinate(OX + x0, OY + y0) });
            var old = new TerrainMesh.Input { X = X.ToArray(), Y = Y.ToArray(), Z = Z.ToArray(), F = F.ToArray(), GroundZ = (x, y) => 0.0, Plan = Box(-1, -1, 31, 21) };
            var road = new TerrainMesh.Input
            {
                X = old.X, Y = old.Y, Z = old.Z, F = old.F, GroundZ = old.GroundZ, Plan = old.Plan,
                RoadArea = Box(-1, 5, 31, 15), RoadPave = Box(-1, 8.5, 31, 11.5), RoadLane = Box(-1, 9, 31, 11), BaseZ = (x, y) => 0.0,
            };
            var o0 = TerrainMesh.Build(old, OX, OY);
            var o1 = TerrainMesh.Build(road, OX, OY);
            // 따로 센 답(무게중심 y로) · 도로 밖은 옛 답과 같은 구분
            var nT = F.Count / 3;
            var c0 = new TerrainMesh.Cls[nT]; var c1 = new TerrainMesh.Cls[nT];
            Array.Fill(c0, (TerrainMesh.Cls)255); Array.Fill(c1, (TerrainMesh.Cls)255);
            foreach (var (o, c) in new[] { (o0, c0), (o1, c1) })
                foreach (var r in o.Ranges)
                    for (int k = r.Start; k < r.Start + r.Count; k++)
                    {
                        // 출력 세모 k의 무게중심으로 원래 세모를 찾는다(구분마다 점을 따로 둬 차례가 바뀜)
                        double cx = (o.Pos[o.Idx[k * 3] * 3] + o.Pos[o.Idx[k * 3 + 1] * 3] + o.Pos[o.Idx[k * 3 + 2] * 3]) / 3, cy = (o.Pos[o.Idx[k * 3] * 3 + 1] + o.Pos[o.Idx[k * 3 + 1] * 3 + 1] + o.Pos[o.Idx[k * 3 + 2] * 3 + 1]) / 3;
                        int i = (int)Math.Floor(cx / d), j = (int)Math.Floor(cy / d);
                        int slot = (j * nx + i) * 2;   // 칸 안 두 세모는 같은 띠(띠 경계가 0.5 m 칸 경계)라 구분이 같아야 — 들어온 차례로 채움
                        c[c[slot] == (TerrainMesh.Cls)255 ? slot : slot + 1] = r.Cls;
                    }
            int bad = 0, outsideSame = 0, outsideN = 0; var cnt = new Dictionary<TerrainMesh.Cls, int>();
            for (int j = 0; j < ny; j++)
                for (int i = 0; i < nx; i++)
                    for (int h = 0; h < 2; h++)
                    {
                        double cy = (j + 0.5) * d;
                        var k = c1[(j * nx + i) * 2 + h];
                        cnt[k] = cnt.TryGetValue(k, out int m) ? m + 1 : 1;
                        TerrainMesh.Cls? want = cy > 9 && cy < 11 ? TerrainMesh.Cls.Road : (cy > 8.5 && cy < 9) || (cy > 11 && cy < 11.5) ? TerrainMesh.Cls.Shoulder
                            : cy > 11.5 && cy < 13 ? TerrainMesh.Cls.SlopeFill : cy > 13 && cy < 13.5 ? TerrainMesh.Cls.WallZone : cy > 13.5 && cy < 15 ? TerrainMesh.Cls.Bench
                            : cy > 5 && cy < 8.5 ? TerrainMesh.Cls.SlopeCut : null;
                        if (want != null) { if (k != want) bad++; }
                        else { outsideN++; if (k == c0[(j * nx + i) * 2 + h]) outsideSame++; }
                    }
            // RoadArea null → 옛 답 그대로(배열 비트 같음)
            var oN = TerrainMesh.Build(new TerrainMesh.Input { X = old.X, Y = old.Y, Z = old.Z, F = old.F, GroundZ = old.GroundZ, Plan = old.Plan, RoadPave = road.RoadPave, RoadLane = road.RoadLane, BaseZ = road.BaseZ }, OX, OY);
            bool sameOld = oN.Idx.SequenceEqual(o0.Idx) && oN.Pos.SequenceEqual(o0.Pos) && oN.Count.SequenceEqual(o0.Count);
            bool keys = TerrainMesh.Keys.Length == TerrainMesh.Names.Length && TerrainMesh.Keys.Length == Enum.GetValues<TerrainMesh.Cls>().Length
                        && TerrainMesh.Keys[(int)TerrainMesh.Cls.Road] == "road" && TerrainMesh.Keys[(int)TerrainMesh.Cls.Shoulder] == "shoulder"
                        && (int)TerrainMesh.Cls.Road == 8 && (int)TerrainMesh.Cls.Other == 7 && TerrainMesh.Keys[7] == "other";
            check("S175 ⑨ 진입도로 구분 — 차로(땅과 같은 높이여도) · 길어깨 · 성토/절토 사면(정지면 기준) · 옹벽 자리 · 소단 · 부지 다각형이 못 덮음 · 도로 밖 = 옛 답 · RoadArea null = 옛 답 비트 같음 · 새 열쇠 맨 끝(옛 번호 그대로)",
                bad == 0 && outsideN > 0 && outsideSame == outsideN && sameOld && keys && o1.Count[(int)TerrainMesh.Cls.Road] > 0 && o1.Count[(int)TerrainMesh.Cls.Shoulder] > 0 && o1.Count.Sum() == nT,
                $"틀린 구분 {bad} · 도로 밖 같음 {outsideSame}/{outsideN} · 옛 답 그대로 {sameOld} · 열쇠 {keys} · " + string.Join(" · ", cnt.OrderBy(q => q.Key).Select(q => $"{TerrainMesh.Names[(int)q.Key]} {q.Value}")));
        }

        // ⑩ ★★[v111.8.1 · JACK 1103 «진입도로의 소단이 안 보여»] 도로 소단 = 엔진 계단선 짝 사이 횡단으로 평평한 띠 — 종단 경사 15 %(옛 1:10 기울기로는 소단 0)
        //    가짜 도로(X 방향 · 원점 X 200,000 Y 550,000 · 현장 값 없음): 포장 y 8.5~11.5 · 절토 쪽 사면 1:1.5(높이 1) — 소단 13~14 — 사면 — 15.5 위 평탄 ·
        //    성토 쪽 사면 — 소단 6~7 — 사면 — 4.5 아래 평탄 · 16~17 = 부지 계획면(정지면 그대로 → 부지 규칙 «계획면») · 짝 거리 1.6 m(사면 짝도 후보 — 평평함으로 걸러야)
        //    + 끝과 끝이 이어진 두 선(경사 0 — 진행 방향 짝은 평평해도 소단 아님) · 시공 격자 진입도로 마디 표시 = 다각형 덮음 · u8 덩어리 되읽기
        {
            const double OX = 200000, OY = 550000, G = 0.15;
            double E(double x) => 10 + G * (x - OX);
            double Pz(double y) => y >= 15.5 ? 2 : y >= 14 ? 1 + (y - 14) / 1.5 : y >= 13 ? 1 : y >= 11.5 ? (y - 11.5) / 1.5
                                 : y >= 8.5 ? 0 : y >= 7 ? -(8.5 - y) / 1.5 : y >= 6 ? -1 : y >= 4.5 ? -1 - (6 - y) / 1.5 : -2;
            double Zd(double x, double y) => E(x) + Pz(y - OY);
            double Zg(double x, double y) { double ry = y - OY; return ry < 3 || ry > 17 ? Zd(x, y) : ry >= 16 ? Zd(x, y) - 2 : ry > 10 ? Zd(x, y) + 2 : Zd(x, y) - 2; }
            double Zb(double x, double y) => y - OY >= 16 ? Zd(x, y) : Zg(x, y);
            int nx = 60, ny = 40; double d = 0.5;
            var X = new List<double>(); var Y = new List<double>(); var Z = new List<double>();
            for (int j = 0; j <= ny; j++) for (int i = 0; i <= nx; i++) { X.Add(OX + i * d); Y.Add(OY + j * d); Z.Add(Zd(OX + i * d, OY + j * d)); }
            var F = new List<int>();
            for (int j = 0; j < ny; j++)
                for (int i = 0; i < nx; i++)
                {
                    int a = j * (nx + 1) + i, b = a + 1, c = a + nx + 1, e = c + 1;
                    if ((i + j) % 2 == 0) { F.AddRange(new[] { a, b, e }); F.AddRange(new[] { a, e, c }); } else { F.AddRange(new[] { a, b, c }); F.AddRange(new[] { b, e, c }); }
                }
            var gf = NtsSupport.Factory();
            Geometry Box(double x0, double y0, double x1, double y1) => gf.CreatePolygon(new[] { new Coordinate(OX + x0, OY + y0), new Coordinate(OX + x1, OY + y0), new Coordinate(OX + x1, OY + y1), new Coordinate(OX + x0, OY + y1), new Coordinate(OX + x0, OY + y0) });
            // 계단선(엔진 꼴 — 절토: 끝 아랫선 11.5 · 윗선 13 · 아랫선 14 · 윗선 15.5 / 성토: 윗선 8.5 · 아랫선 7 · 윗선 6 · 아랫선 4.5) · 2 m마다 점 · 높이 = 가장자리 + 누적 높이
            IReadOnlyList<(double X, double Y, double Z)> Ln(double y, double rise, double x0 = 0, double x1 = 30) => Enumerable.Range(0, (int)Math.Round((x1 - x0) / 2) + 1).Select(k => (OX + x0 + k * 2.0, OY + y, E(OX + x0 + k * 2.0) + rise)).ToList();
            var tl = new List<IReadOnlyList<(double X, double Y, double Z)>> { Ln(11.5, 0), Ln(13, 1), Ln(14, 1), Ln(15.5, 2), Ln(8.5, 0), Ln(7, -1), Ln(6, -1), Ln(4.5, -2) };
            var br = RoadBenches.Strips(tl, (x, y) => Zd(x, y), 1.6);
            var mk = new TerrainMesh.Input
            {
                X = X.ToArray(), Y = Y.ToArray(), Z = Z.ToArray(), F = F.ToArray(), GroundZ = (x, y) => Zg(x, y), Plan = Box(-1, 16, 31, 17.5),
                RoadArea = Box(-1, 3, 31, 17), RoadPave = Box(-1, 8.5, 31, 11.5), RoadLane = Box(-1, 9, 31, 11), BaseZ = (x, y) => Zb(x, y),
            };
            var oOld = TerrainMesh.Build(mk, OX, OY);
            mk.RoadBench = br.Strips;
            var oNew = TerrainMesh.Build(mk, OX, OY);
            // 출력 세모 → 무게중심 y 띠별 구분
            Dictionary<double, Dictionary<TerrainMesh.Cls, int>> Bands(TerrainMesh.Output o)
            {
                var res = new Dictionary<double, Dictionary<TerrainMesh.Cls, int>>();
                foreach (var r in o.Ranges)
                    for (int k = r.Start; k < r.Start + r.Count; k++)
                    {
                        double cy = (o.Pos[o.Idx[k * 3] * 3 + 1] + o.Pos[o.Idx[k * 3 + 1] * 3 + 1] + o.Pos[o.Idx[k * 3 + 2] * 3 + 1]) / 3;
                        double band = Math.Floor(cy / d) * d;
                        if (!res.TryGetValue(band, out var m)) res[band] = m = new();
                        m[r.Cls] = m.GetValueOrDefault(r.Cls) + 1;
                    }
                return res;
            }
            TerrainMesh.Cls? Want(double y) => y >= 16 && y < 17 ? TerrainMesh.Cls.Plan : y >= 13 && y < 14 || y >= 6 && y < 7 ? TerrainMesh.Cls.Bench
                : y >= 9 && y < 11 ? TerrainMesh.Cls.Road : y >= 8.5 && y < 11.5 ? TerrainMesh.Cls.Shoulder : y >= 11.5 && y < 16 ? TerrainMesh.Cls.SlopeCut : y >= 3 && y < 8.5 ? TerrainMesh.Cls.SlopeFill : null;
            int bad = 0, nb = 0; string firstBad = "";
            foreach (var (band, m) in Bands(oNew))
            {
                var w = Want(band);
                if (w == null) continue;
                foreach (var (k, n) in m) if (k != w) { bad += n; if (firstBad.Length == 0) firstBad = $"y {band:F1}: {TerrainMesh.Names[(int)k]} {n}(바람 {TerrainMesh.Names[(int)w]})"; }
                if (w == TerrainMesh.Cls.Bench) nb += m.GetValueOrDefault(TerrainMesh.Cls.Bench);
            }
            int oldBench = oOld.Count[(int)TerrainMesh.Cls.Bench];
            double wantArea = 2 * 30;   // 소단 둘 × 폭 1 m × 길이 30 m
            // 끝과 끝이 이어진 두 선(경사 0 · 평평 · 0.3 m 어긋남) — 진행 방향 짝(상대 선 끝점)은 소단이 아님
            var bEnd = RoadBenches.Strips(new List<IReadOnlyList<(double X, double Y, double Z)>> { new[] { (OX, OY, 5.0), (OX + 10, OY, 5.0) }, new[] { (OX + 10.5, OY + 0.3, 5.0), (OX + 20, OY + 0.3, 5.0) } }, (x, y) => 5.0, 1.6);
            check("S175 ⑩ 진입도로 소단 — 엔진 계단선 짝 사이 횡단으로 평평한 띠(종단 15 %: 옛 기울기 규칙 소단 0) · 사면 짝은 걸러짐 · 띠 넓이 = 2 × 30 m² · 정지면 그대로 세모 = 부지 규칙(계획면) · 진행 방향 끝-끝 짝 없음",
                bad == 0 && nb == 2 * 2 * 60 * 2 && oldBench == 0 && Math.Abs(br.Area - wantArea) < 1e-6 && br.MaxDz < 1e-9 && (bEnd.Strips == null || bEnd.Strips.IsEmpty) && oNew.Count.Sum() == F.Count / 3,
                $"틀린 구분 {bad}{(firstBad.Length > 0 ? " — 처음 " + firstBad : "")} · 소단 세모 {nb}(옛 규칙 {oldBench}) · {br.Text} · 끝-끝 짝 띠 {(bEnd.Strips == null ? 0 : bEnd.Strips.Area):F3} ㎡");

            // 시공 격자 진입도로 마디 — 다각형 덮음(경계 포함) · 차례 j·(w+1) + i · 장면 u8 덩어리 되읽기
            var area = Box(3, 3, 17, 13);
            int gw = 24, gh = 20; double gx0 = OX, gy0 = OY, gc = 1.0;
            var mask = TerrainMesh.GridMask(area, gx0, gy0, gc, gw, gh, out int nm);
            int mBad = 0, mOn = 0;
            for (int j = 0; j <= gh; j++)
                for (int i = 0; i <= gw; i++)
                {
                    bool want = i >= 3 && i <= 17 && j >= 3 && j <= 13;
                    if ((mask[j * (gw + 1) + i] == 1) != want) mBad++;
                    if (want) mOn++;
                }
            var sc = new Web3DScene();
            sc.Head["build"] = new Dictionary<string, object> { ["w"] = gw, ["h"] = gh, ["zf"] = sc.F32(new float[(gw + 1) * (gh + 1)]), ["road"] = sc.U8(mask) };
            var (hd, buf, bo) = Web3DScene.Unpack(sc.PackGzip());
            var rr = hd.RootElement.GetProperty("build").GetProperty("road");
            var back = buf.AsSpan(bo + rr.GetProperty("o").GetInt32(), rr.GetProperty("n").GetInt32()).ToArray();
            var emptyM = TerrainMesh.GridMask(gf.CreatePolygon(), gx0, gy0, gc, gw, gh, out int nEmpty);
            check("S175 ⑪ 시공 격자 진입도로 마디 — 다각형 덮음(경계 포함 · j·(w+1) + i) · 수 · u8 덩어리 되읽기 같음 · 4바이트 맞춤 · 빈 다각형 = 0",
                mBad == 0 && nm == mOn && back.SequenceEqual(mask) && rr.GetProperty("t").GetString() == "u8" && rr.GetProperty("o").GetInt32() % 4 == 0 && nEmpty == 0 && emptyM.All(v => v == 0),
                $"틀린 마디 {mBad} · 켠 마디 {nm}/{mOn} · 되읽기 {back.Length} 바이트");
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

        // ⑫ ★★[JACK 1103] 지층 격자(자르기 뷰) — 가짜 좌표(X ≈ 200,000 · Y ≈ 550,000) · 층 상단 셋 + 지하수위 · 범위 밖 NaN · 위 층보다 높은 마디 누름 · 높이 없는 층 뺌 · 되읽기
        {
            const double OX = 200000, OY = 550000;
            double Gz(double x, double y) => 18 + 0.05 * (x - OX) + 2 * Math.Sin((y - OY) / 15);
            // 층 1 표토 = 원지반 · 층 2 풍화암 = 표고 15 평평(원지반이 낮은 자리에서 위로 뚫음 → 누름) · 층 3 연암 = 원지반 − 6(x < OX + 30만) · 층 4 = 높이 없음
            Func<double, double, double?> in60(Func<double, double, double> f) => (x, y) => x >= OX - 60 && x <= OX + 60 && y >= OY - 40 && y <= OY + 40 ? f(x, y) : null;
            var layers = new List<Web3DStrata.Layer>
            {
                new("연암", 3, 255, 0, 191, (x, y) => x < OX + 30 && x >= OX - 60 && x <= OX + 60 && y >= OY - 40 && y <= OY + 40 ? Gz(x, y) - 6 : null),
                new("표토", 1, 255, 127, 0, in60(Gz)),
                new("풍화암", 2, 255, 255, 0, in60((x, y) => 15)),
                new("없는층", 4, 0, 0, 0, (x, y) => null),
            };
            // 범위: 측점 상자 ± 여유 → 2 m부터(마디 한도 안) · 넓으면 키움
            var g = Web3DStrata.Pick(OX - 70, OY - 50, OX + 70, OY + 50)!.Value;
            var gBig = Web3DStrata.Pick(OX - 2000, OY - 1500, OX + 2000, OY + 1500)!.Value;
            bool pickOk = g.Cell == 2.0 && g.W == 70 && g.H == 50 && gBig.Nodes <= Web3DStrata.MaxNodes && gBig.Cell > 1 && gBig.X0 + gBig.W * gBig.Cell >= OX + 2000 - 1e-9
                          && Web3DStrata.Pick(OX, OY, OX, OY + 5) == null && Web3DStrata.Pick(double.NaN, 0, 1, 1) == null;
            var sc = new Web3DScene();
            var h = Web3DStrata.Head(sc, g, layers, (x, y) => x >= OX - 60 && x <= OX + 60 && y >= OY - 40 && y <= OY + 40 ? 17.5 : null, OX, OY, out string txt)!;
            sc.Head["strata"] = h;
            var (hd, buf, bo) = Web3DScene.Unpack(sc.PackGzip());
            var S = hd.RootElement.GetProperty("strata");
            var L = S.GetProperty("layers");
            string names = string.Join(",", L.EnumerateArray().Select(e => e.GetProperty("name").GetString()));
            var z1 = Web3DScene.ReadF32(L[0].GetProperty("z"), buf, bo); var z2 = Web3DScene.ReadF32(L[1].GetProperty("z"), buf, bo); var z3 = Web3DScene.ReadF32(L[2].GetProperty("z"), buf, bo);
            var zw = Web3DScene.ReadF32(S.GetProperty("water").GetProperty("z"), buf, bo);
            int bad = 0, nanOut = 0, pressed = 0, nan3 = 0; string first = "";
            for (int j = 0; j <= g.H; j++)
                for (int i = 0; i <= g.W; i++)
                {
                    int k = j * (g.W + 1) + i;
                    double x = g.X0 + i * g.Cell, y = g.Y0 + j * g.Cell;
                    bool inside = x >= OX - 60 && x <= OX + 60 && y >= OY - 40 && y <= OY + 40;
                    if (!inside) { if (float.IsNaN(z1[k]) && float.IsNaN(z2[k]) && float.IsNaN(z3[k]) && float.IsNaN(zw[k])) nanOut++; else bad++; continue; }
                    double g0 = Gz(x, y), w2 = Math.Min(15, g0), w3 = x < OX + 30 ? Math.Min(g0 - 6, w2) : double.NaN;
                    if (15 > g0) pressed++;
                    if (double.IsNaN(w3)) nan3++;
                    bool ok = Math.Abs(z1[k] - g0) < 1e-4 && Math.Abs(z2[k] - w2) < 1e-4 && (double.IsNaN(w3) ? float.IsNaN(z3[k]) : Math.Abs(z3[k] - w3) < 1e-4) && zw[k] == 17.5f;
                    if (!ok) { bad++; if (first.Length == 0) first = $"({i},{j}) {z1[k]}/{z2[k]}/{z3[k]} 대 {g0:F4}/{w2:F4}/{w3:F4}"; }
                }
            bool headOk = names == "표토,풍화암,연암" && Math.Abs(S.GetProperty("x0").GetDouble() + 70) < 1e-9 && Math.Abs(S.GetProperty("y0").GetDouble() + 50) < 1e-9
                          && S.GetProperty("cell").GetDouble() == 2.0 && S.GetProperty("w").GetInt32() == 70 && S.GetProperty("h").GetInt32() == 50
                          && L[2].GetProperty("color").EnumerateArray().Select(e => e.GetInt32()).SequenceEqual(new[] { 255, 0, 191 }) && L[1].GetProperty("ord").GetInt32() == 2
                          && z1.Length == g.Nodes && txt.Contains("높이 없어 뺀 층 1") && txt.Contains("누른 마디");
            // 지층이 하나도 없으면(범위 밖 · 높이 없음) null — 부르는 쪽이 열쇠를 안 넣는다
            var none = Web3DStrata.Head(new Web3DScene(), g, new[] { layers[3] }, null, OX, OY, out string txt0);
            check("S175 ⑫ 지층 격자(자르기 뷰) — 2 m 격자 · 넓으면 키움(마디 한도) · 층 차례 = 번호 · 위 층보다 높은 마디 누름 · 범위 밖 NaN · 높이 없는 층 뺌 · 지하수위 · 되읽기 · 원점 뺌 · 다 없으면 null",
                pickOk && headOk && bad == 0 && nanOut > 0 && pressed > 0 && nan3 > 0 && none == null,
                $"{txt} · 틀린 마디 {bad}{(first.Length > 0 ? " 첫 " + first : "")} · 범위 밖 NaN {nanOut} · 누른 자리 {pressed} · 연암 없음 {nan3} · 큰 범위 칸 {gBig.Cell:0.###} m 마디 {gBig.Nodes:N0} · «{txt0}»");
        }

        // ⑬ ★★[JACK 1103 «3D 뷰에서 진입도로 측점이 없어 · 부지 측점, 진입도로 측점이 별도로»] 진입도로 측점 머리 — 가짜 좌표(X ≈ 200,000 · Y ≈ 550,000) · 곧은 노선 둘(30° · −60°) ·
        //    부지 «align»과 같은 열쇠 · 측점 차례 · 1 cm 겹침 뺌 · 노선 밖 측점만 뺌 · 정측점 · 좌우 끝 = 옆거리 · 오른쪽이 진행 방향 오른쪽 · 가로선 1 m마다 · 높이 = 지형 + 0.3 · 높이 없음 −99999/NaN ·
        //    중심선 2 m마다 + 끝 · 원점 뺌 · 되읽기 · 측점 없으면 null + 덩어리 0(도로 없는 장면 바이트 그대로) · 검토선 옆거리 → 폭
        {
            const double OX = 200000, OY = 550000;
            double? Z(double x, double y) => x > OX + 70 ? null : 30 + 0.02 * (x - OX) - 0.01 * (y - OY);
            // 곧은 노선 — 시점 (x0, y0) · 방향각 deg · 길이 len · 옆거리 + = 진행 방향 오른쪽(Civil PointLocation과 같음)
            Func<double, double, (double X, double Y)?> Road(double x0, double y0, double deg, double len)
            {
                double dx = Math.Cos(deg * Math.PI / 180), dy = Math.Sin(deg * Math.PI / 180);
                return (s, o) => s < -1e-9 || s > len + 1e-9 ? null : (x0 + s * dx + o * dy, y0 + s * dy - o * dx);
            }
            var at1 = Road(OX + 10, OY - 20, 30, 85.3);
            var at2 = Road(OX - 40, OY + 30, -60, 47.0);
            var marks1 = new List<Web3DRoadStations.Mark>
            {
                new(40.004, "No.2(겹침)", 0), new(32.5, "No.1+12.50", 1), new(0, "No.0", 1), new(90, "No.4+10.00", 0), new(double.NaN, "?", 0),
                new(20, "No.1", 2), new(40, "No.2", 1), new(85.3, "No.4+05.30", 0),
            };
            var sc = new Web3DScene();
            var h1 = Web3DRoadStations.Head(sc, "DH도로_1", 0, 85.3, 20, 12.5, 9.75, marks1, at1, Z, OX, OY, out int skip1)!;
            var h2 = Web3DRoadStations.Head(sc, "DH도로_2", 0, 47.0, 20, 8, 8, new[] { new Web3DRoadStations.Mark(0, "No.0", 0), new Web3DRoadStations.Mark(47, "No.2+07.00", 0) }, at2, Z, OX, OY, out int skip2)!;
            sc.Head["roadAlign"] = new List<object> { h1, h2 };
            var (hd, buf, bo) = Web3DScene.Unpack(sc.PackGzip());
            var R = hd.RootElement.GetProperty("roadAlign");
            var r1 = R[0];
            var st = r1.GetProperty("st");
            string keys = string.Join(",", r1.EnumerateObject().Select(p => p.Name)), skeys = string.Join(",", st[0].EnumerateObject().Select(p => p.Name));
            string labels = string.Join(",", st.EnumerateArray().Select(e => e.GetProperty("label").GetString()));
            string majors = string.Join("", st.EnumerateArray().Select(e => e.GetProperty("major").GetBoolean() ? "1" : "0"));
            int bad = 0; string first = "";
            void Bad(string w) { bad++; if (first.Length == 0) first = w; }
            double c30 = Math.Cos(Math.PI / 6), s30 = Math.Sin(Math.PI / 6);
            for (int k = 0; k < st.GetArrayLength(); k++)
            {
                var e = st[k];
                double s = e.GetProperty("s").GetDouble();
                double[] A(string n) => e.GetProperty(n).EnumerateArray().Select(q => q.GetDouble()).ToArray();
                var c = A("c"); var l = A("l"); var r = A("r");
                var pts = Web3DScene.ReadF32(e.GetProperty("pts"), buf, bo);
                double wx = OX + 10 + s * c30, wy = OY - 20 + s * s30;
                if (Math.Abs(c[0] - (wx - OX)) > 1e-3 || Math.Abs(c[1] - (wy - OY)) > 1e-3) Bad($"{s} 중심 자리");
                var zz = Z(wx, wy);
                if (zz.HasValue ? Math.Abs(c[2] - (zz.Value + Web3DRoadStations.Lift)) > 1e-3 : c[2] != -99999) Bad($"{s} 중심 높이 {c[2]}");
                double dl = Math.Sqrt((l[0] - c[0]) * (l[0] - c[0]) + (l[1] - c[1]) * (l[1] - c[1])), dr = Math.Sqrt((r[0] - c[0]) * (r[0] - c[0]) + (r[1] - c[1]) * (r[1] - c[1]));
                double cross = c30 * (r[1] - c[1]) - s30 * (r[0] - c[0]);     // 진행 방향 × (오른 끝 − 중심) < 0 = 오른쪽
                if (Math.Abs(dl - 12.5) > 1e-3 || Math.Abs(dr - 9.75) > 1e-3 || cross >= 0) Bad($"{s} 좌우 {dl:F3}/{dr:F3} · 외적 {cross:F3}");
                int np = (int)Math.Ceiling(12.5 + 9.75);
                if (pts.Length != (np + 1) * 3 || Math.Abs(pts[0] - l[0]) > 1e-3 || Math.Abs(pts[^3] - r[0]) > 1e-3 || Math.Abs(pts[^2] - r[1]) > 1e-3) Bad($"{s} 가로선 점 {pts.Length / 3}");
                for (int i = 0; i < pts.Length; i += 3)
                {
                    var zp = Z(pts[i] + OX, pts[i + 1] + OY);
                    if (zp.HasValue ? Math.Abs(pts[i + 2] - (zp.Value + Web3DRoadStations.Lift)) > 1e-3 : !float.IsNaN(pts[i + 2])) { Bad($"{s} 가로선 높이 {i / 3}"); break; }
                }
            }
            var line = Web3DScene.ReadF32(r1.GetProperty("line"), buf, bo);
            int nLine = line.Length / 3;
            double lastX = line[^3] + OX, lastY = line[^2] + OY;
            bool lineOk = nLine == 44 && Math.Abs(line[3] - (10 + 2 * c30)) < 1e-3 && Math.Abs(lastX - (OX + 10 + 85.3 * c30)) < 1e-3 && Math.Abs(lastY - (OY - 20 + 85.3 * s30)) < 1e-3 && float.IsNaN(line[^1])
                          && r1.GetProperty("step").GetDouble() == 2 && r1.GetProperty("s1").GetDouble() == 85.3 && r1.GetProperty("index").GetDouble() == 20;
            bool headOk = keys == "name,line,s0,s1,step,index,views,st" && skeys == "s,label,major,views,c,l,r,pts" && R.GetArrayLength() == 2
                          && r1.GetProperty("name").GetString() == "DH도로_1" && R[1].GetProperty("name").GetString() == "DH도로_2" && R[1].GetProperty("st").GetArrayLength() == 2
                          && labels == "No.0,No.1,No.1+12.50,No.2,No.4+05.30" && majors == "11010" && r1.GetProperty("views").GetInt32() == 5 && skip1 == 1 && skip2 == 0;
            // 측점이 없거나 다 노선 밖이면 null · 덩어리 0(부르는 쪽이 열쇠를 안 넣어 도로 없는 장면 바이트 그대로)
            var sc0 = new Web3DScene();
            var n0 = Web3DRoadStations.Head(sc0, "x", 0, 85.3, 20, 10, 10, Array.Empty<Web3DRoadStations.Mark>(), at1, Z, OX, OY, out _);
            var n1 = Web3DRoadStations.Head(sc0, "x", 0, 85.3, 20, 10, 10, new[] { new Web3DRoadStations.Mark(99, "No.4+19.00", 0) }, at1, Z, OX, OY, out int skipN);
            var n2 = Web3DRoadStations.Head(sc0, "x", 0, 85.3, 20, 0, 0, marks1, at1, Z, OX, OY, out _);
            bool nullOk = n0 == null && n1 == null && skipN == 1 && n2 == null && sc0.BlobBytes == 0 && sc0.Pack().SequenceEqual(new Web3DScene().Pack());
            var reach = Web3DRoadStations.Reach(new[] { -12.5, -3, 0, 4, 9.75, double.NaN });
            var reach0 = Web3DRoadStations.Reach(Array.Empty<double>());
            bool reachOk = reach == (12.5, 9.75) && reach0 == (0, 0);
            check("S175 ⑬ 진입도로 측점 머리 — 부지 «align»과 같은 열쇠 · 측점 차례 · 1 cm 겹침 뺌 · 노선 밖만 뺌 · 정측점 · 좌우 끝 = 옆거리(오른쪽 = 진행 방향 오른쪽) · 가로선 1 m · 높이 = 지형 + 0.3 · 높이 없음 −99999/NaN · 중심선 2 m + 끝 · 도로 둘 · 되읽기 · 없으면 null + 덩어리 0 · 검토선 옆거리 → 폭",
                headOk && lineOk && bad == 0 && nullOk && reachOk,
                $"측점 {labels} · 정측점 {majors} · 뺌 {skip1} · 중심선 점 {nLine} · 틀림 {bad}{(first.Length > 0 ? " 첫 " + first : "")} · 열쇠 {keys} / {skeys} · null {nullOk} · 폭 {reach}");
        }
    }
}
