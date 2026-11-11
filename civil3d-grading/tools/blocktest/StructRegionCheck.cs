// ── S247 ★[v117.1 · JACK 1111 «박스로 영역 · 팝업 · Revit에서 3D로»] 구조물 3D 영역 파일 — 만든 좌표(현장 값 없음 · (1000, 2000) 둘레)
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DH.Grading.Core;
using Box = DH.Grading.Core.GroundCrop.Box;
using Tri = DH.Grading.Core.WallDaylight.Tri;

static class StructRegionCheck
{
    // ★[계획 9절 M5] WriteHead를 떼기 전(HEAD 9e82ef4 · v117.0.0) StructPoints.Json 출력의 SHA-256 — 네모 없으면 글자 하나 안 다름
    const string Gold0 = "578D5D53473D1F091E320AF09F3473ADC38C495B5AC29F9ABBA20F553CBD084C";   // sketch 없음
    const string Gold1 = "60B58E871748DE9DC1DA33346546AC07DCEEB49AA42208935DCC645F324CA8E4";   // sketch 있음
    const string Gold2 = "5087AB3EA447DC756719C1C25CA86A5146E100EAB2DC8839DF870895771E3959";   // 단위 없음 · EPSG 모름 · UCS 돌림

    static string Sha(string s) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s)));
    static long K(double v) => (long)Math.Round(v * 1000.0);

    /// <summary>(x0, y0)에서 n×n 1 m 격자 · z = 평면 · <paramref name="mixWinding"/>면 세모 감기를 섞음(시계 · 반시계).</summary>
    static List<Tri> Grid(double x0, double y0, int n, Func<double, double, double> z, bool mixWinding = true)
    {
        var r = new List<Tri>();
        Point3 P(int i, int j) { double x = x0 + i, y = y0 + j; return new Point3(x, y, z(x, y)); }
        for (int i = 0; i < n; i++)
            for (int j = 0; j < n; j++)
            {
                var (a, b, c, d) = (P(i, j), P(i + 1, j), P(i + 1, j + 1), P(i, j + 1));
                bool cw = mixWinding && (i + j) % 2 == 1;
                r.Add(cw ? new Tri(a, c, b) : new Tri(a, b, c));
                r.Add(cw ? new Tri(a, d, c) : new Tri(a, c, d));
            }
        return r;
    }

    static double AreaXY(StructRegion3D.Mesh m)
    {
        double s = 0;
        foreach (var (a, b, c) in m.T)
        {
            var (pa, pb, pc) = (m.Vm(a), m.Vm(b), m.Vm(c));
            s += ((pb.X - pa.X) * (pc.Y - pa.Y) - (pb.Y - pa.Y) * (pc.X - pa.X)) / 2;
        }
        return s;
    }

    static bool JsonSame(JsonElement a, JsonElement b)
    {
        if (a.ValueKind != b.ValueKind) return false;
        switch (a.ValueKind)
        {
            case JsonValueKind.Object:
                var pa = a.EnumerateObject().ToList(); var pb = b.EnumerateObject().ToList();
                return pa.Count == pb.Count && pa.Zip(pb).All(z => z.First.Name == z.Second.Name && JsonSame(z.First.Value, z.Second.Value));
            case JsonValueKind.Array:
                return a.GetArrayLength() == b.GetArrayLength() && a.EnumerateArray().Zip(b.EnumerateArray()).All(z => JsonSame(z.First, z.Second));
            case JsonValueKind.Number: return a.GetDouble() == b.GetDouble();
            case JsonValueKind.String: return a.GetString() == b.GetString();
            default: return true;
        }
    }

    public static void Run(Action<string, bool, string> check0)
    {
        void Check(string n, bool ok, string d) => check0("S247 R3" + n, ok, d);
        Console.WriteLine("\n== S247 구조물 3D 영역 파일(Civil → Revit) ==");
        var (info, p1, p2, sk) = Sample();
        var info0 = info with { Insunits = 0, Epsg = 0, CivilCode = "", CrsSource = "settings", UcsWasWorld = false };

        // ⓪ 점 파일 — 네모 없으면 v117.0과 글자 하나 안 다름(정답 글 SHA-256)
        string g0 = Sha(StructPoints.Json(info, p1, p2)), g1 = Sha(StructPoints.Json(info, p1, p2, sk)), g2 = Sha(StructPoints.Json(info0, p1, p2));
        Check(" ⓪ 점 파일 — 네모 없으면 v117.0 출력과 같음(sketch 없음 · 있음 · 단위 없음)", g0 == Gold0 && g1 == Gold1 && g2 == Gold2, $"{g0[..8]} {g1[..8]} {g2[..8]}");

        // ① 네모 — 순서 무관 · 1 mm 반올림 · 1 m 미만 막음
        bool b1 = StructRegion3D.TryBox(1003.75049, 2003.2504, 1000.2496, 2000.7501, out var bx) && bx == new Box(1000.25, 2000.75, 1003.75, 2003.25);
        bool b2 = StructRegion3D.TryBox(1000.2496, 2000.7501, 1003.75049, 2003.2504, out var bx2) && bx2 == bx;
        bool b3 = !StructRegion3D.TryBox(0, 0, 0.9994, 5, out _) && StructRegion3D.TryBox(0, 0, 0.9996, 5, out var bx3) && bx3.X1 == 1.0 && !StructRegion3D.TryBox(0, 0, 5, double.NaN, out _);
        Check(" ① 네모 — 모서리 순서 무관 · 1 mm 반올림 · 한 변 1 m 미만 막음", b1 && b2 && b3, bx.Text);

        // ② 세모 — 안 = 그대로 · 밖 = 없음 · 걸침 = 모든 점이 네모 안 · 새 점 Z가 원래 평면 위(반올림 전 · 평면까지 수직 거리)
        var box = new Box(1000.25, 2000.75, 1003.75, 2003.25);
        var tin = new Tri(new Point3(1001, 2001, 50.1234), new Point3(1002, 2001, 50.5), new Point3(1001, 2002, 51));
        var mIn = StructRegion3D.Clip(new[] { tin }, box, out _)!;
        bool inOk = mIn.T.Count == 1 && mIn.V.Count == 3 && mIn.V.Contains((1001000, 2001000, 50123)) && mIn.Cut == 0;
        var tout = new Tri(new Point3(990, 1990, 0), new Point3(995, 1990, 0), new Point3(990, 1995, 0));
        bool outOk = StructRegion3D.Clip(new[] { tout }, box, out int cOut)!.T.Count == 0 && cOut == 0;
        var steep = new Tri(new Point3(999.3, 2000.1, 40), new Point3(1005.7, 2001.3, 95.5), new Point3(1001.1, 2006.9, 61.7));
        var mSt = StructRegion3D.Clip(new[] { steep }, box, out _)!;
        bool inside = mSt.V.All(v => v.X >= K(box.X0) && v.X <= K(box.X1) && v.Y >= K(box.Y0) && v.Y <= K(box.Y1));
        var poly = StructRegion3D.ClipTri(steep, box);
        var (A, B, C) = (steep.A, steep.B, steep.C);
        double ux = B.X - A.X, uy = B.Y - A.Y, uz = B.Z - A.Z, vx = C.X - A.X, vy = C.Y - A.Y, vz = C.Z - A.Z;
        double nx = uy * vz - uz * vy, ny = uz * vx - ux * vz, nz = ux * vy - uy * vx, nl = Math.Sqrt(nx * nx + ny * ny + nz * nz);
        double dMax = poly.Max(p => Math.Abs((p.X - A.X) * nx + (p.Y - A.Y) * ny + (p.Z - A.Z) * nz) / nl);
        bool polyIn = poly.All(p => p.X >= box.X0 && p.X <= box.X1 && p.Y >= box.Y0 && p.Y <= box.Y1);
        Check(" ② 세모 — 안 그대로 · 밖 없음 · 걸침 = 점 모두 네모 안(차이 0) · 새 점 Z 평면 위(≤ 1 mm)", inOk && outOk && inside && polyIn && dMax <= 1e-3 && mSt.Cut == 1 && mSt.T.Count > 0,
            $"걸침 세모 {mSt.T.Count} · 평면 거리 최대 {dMax:E1} m");

        // ③ 넓이 — 1 m 격자 10×10 ∩ 3.5×2.5 m = 8.75 m² · 기울어진 평면이어도 같음
        var flat = Grid(1000, 2000, 10, (x, y) => 50);
        var tilt = Grid(1000, 2000, 10, (x, y) => 50 + 0.3 * (x - 1000) + 0.2 * (y - 2000));
        var mF = StructRegion3D.Clip(flat, box, out _)!; var mT = StructRegion3D.Clip(tilt, box, out _)!;
        double aF = AreaXY(mF), aT = AreaXY(mT);
        Check(" ③ 넓이 — 10×10 격자 ∩ 3.5×2.5 m = 8.75 m²(±1e-6) · 기운 평면도", Math.Abs(aF - 8.75) <= 1e-6 && Math.Abs(aT - 8.75) <= 1e-6, $"평평 {aF:F9} · 기움 {aT:F9}");

        // ④ 점 합침 — n×n 격자가 통째로 안 → (n+1)² 점 · 번호 0 ≤ i < 점 수
        var g6 = Grid(1000, 2000, 6, (x, y) => 50 + 0.1 * x);
        var m6 = StructRegion3D.Clip(g6, new Box(999, 1999, 1010, 2010), out _)!;
        bool idx6 = m6.T.All(t => t.A >= 0 && t.B >= 0 && t.C >= 0 && t.A < m6.V.Count && t.B < m6.V.Count && t.C < m6.V.Count);
        Check(" ④ 점 합침 — 6×6 격자 → 49 점 · 세모 72 · 번호 0부터 범위 안", m6.V.Count == 49 && m6.T.Count == 72 && idx6 && m6.Cut == 0, $"점 {m6.V.Count} · 세모 {m6.T.Count}");

        // ⑤ CountTris = Clip 결과 수 · 한도 넘으면 짓지 않음
        int cnt = StructRegion3D.CountTris(tilt, box);
        var over = StructRegion3D.Clip(tilt, box, out int cOver, max: 10);
        Check(" ⑤ CountTris = Clip 세모 수 · 한도 10으로 넘기면 짓지 않음(null · 수는 정확)", cnt == mT.T.Count && over == null && cOver == cnt && cnt > 10, $"{cnt} = {mT.T.Count}");

        // ⑥ 1 mm 반올림 뒤 납작해진 세모 뺌 · 수직 면(XY 넓이 0)은 남김(L2)
        var sliver = new Tri(new Point3(1001, 2001, 50), new Point3(1002, 2001, 50), new Point3(1001.5, 2001.0002, 50));
        var vert = new Tri(new Point3(1001, 2002, 50), new Point3(1002, 2002, 50), new Point3(1002, 2002, 53));
        var dup = new Tri(new Point3(1001, 2001, 50), new Point3(1001.0002, 2001, 50), new Point3(1001, 2002, 50));
        var mFl = StructRegion3D.Clip(new[] { sliver, vert, dup }, box, out int cFl)!;
        Check(" ⑥ 납작 세모(반올림 뒤 한 줄 · 같은 점) 뺌 · 수직 옹벽 면은 남김", mFl.T.Count == 1 && mFl.Flat == 2 && cFl == 1, $"남김 {mFl.T.Count} · 뺌 {mFl.Flat}");

        // ⑥-b 감기(계획 8절) — 위에서 봐서 반시계(섞인 감기 · 잘린 조각까지) · L1 안쪽 모서리는 세모 둘이 나눔 · 열린 모서리는 네모 테두리 위
        bool ccw = mT.T.All(t => { var (a, b, c) = (mT.V[t.A], mT.V[t.B], mT.V[t.C]); return (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X) > 0; });
        var edges = new Dictionary<(int, int), int>();
        foreach (var (a, b, c) in mT.T) foreach (var (u, v) in new[] { (a, b), (b, c), (c, a) }) { var e = u < v ? (u, v) : (v, u); edges[e] = edges.GetValueOrDefault(e) + 1; }
        bool OnBorder(int i, int j)
        {
            var (p, q) = (mT.V[i], mT.V[j]);
            return (p.X == q.X && (p.X == K(box.X0) || p.X == K(box.X1))) || (p.Y == q.Y && (p.Y == K(box.Y0) || p.Y == K(box.Y1)));
        }
        int bad2 = edges.Count(e => e.Value > 2), openOff = edges.Count(e => e.Value == 1 && !OnBorder(e.Key.Item1, e.Key.Item2));
        double perim = edges.Where(e => e.Value == 1).Sum(e => { var (p, q) = (mT.V[e.Key.Item1], mT.V[e.Key.Item2]); return Math.Sqrt((double)(p.X - q.X) * (p.X - q.X) + (double)(p.Y - q.Y) * (p.Y - q.Y)) / 1000; });
        Check(" ⑥-b 감기 반시계 · 안쪽 모서리 = 세모 둘 · 열린 모서리 = 네모 테두리(둘레 12 m)", ccw && bad2 == 0 && openOff == 0 && Math.Abs(perim - 12) < 1e-9, $"모서리 {edges.Count} · 열린 둘레 {perim:F6} · 테두리 밖 열린 {openOff}");

        // ⑦ 선 — 걸친 토막 Z 보간 · 들락날락 = 2조각 · 안의 닫힌 네모 = 닫힘 · 걸친 닫힌 선 = 이음매 이은 1조각
        var l1 = StructRegion3D.ClipLine(new[] { new Point3(999, 2001, 10), new Point3(1005, 2001, 16) }, false, box);
        bool z1 = l1.Count == 1 && l1[0].Pts[0].X == 1000.25 && Math.Abs(l1[0].Pts[0].Z - 11.25) < 1e-9 && l1[0].Pts[^1].X == 1003.75 && Math.Abs(l1[0].Pts[^1].Z - 14.75) < 1e-9;
        var l2 = StructRegion3D.ClipLine(new[] { new Point3(1001, 2001, 0), new Point3(1012, 2001, 0), new Point3(1012, 2002, 0), new Point3(1001, 2002, 0) }, false, box);
        var sq = new[] { new Point3(1001, 2001, 5), new Point3(1002, 2001, 5), new Point3(1002, 2002, 5), new Point3(1001, 2002, 5) };
        var l3 = StructRegion3D.ClipLine(sq, true, box);
        var l4 = StructRegion3D.ClipLine(new[] { new Point3(1001, 2001, 0), new Point3(1012, 2001, 0), new Point3(1012, 2002, 0), new Point3(1001, 2002, 0) }, true, box);
        bool seam = l4.Count == 1 && !l4[0].Closed && l4[0].Pts.Count == 4 && l4[0].Pts[0] == new Point3(1003.75, 2002, 0) && l4[0].Pts[^1] == new Point3(1003.75, 2001, 0);
        Check(" ⑦ 선 — Z 보간 · 들락날락 2조각 · 안의 닫힌 선 닫힘 · 걸친 닫힌 선 이음매 1조각", z1 && l2.Count == 2 && l3.Count == 1 && l3[0].Closed && l3[0].Pts.Count == 4 && seam,
            $"들락 {l2.Count} · 닫힘 {l3.Count}/{(l3.Count > 0 && l3[0].Closed)} · 이음매 {l4.Count}({(l4.Count > 0 ? l4[0].Pts.Count : 0)}점)");

        // ⑧ 높이 입히기 — 기운 평면 Z 맞음(1 mm) · 메시 밖에서 끊김
        var mzBig = StructRegion3D.Clip(tilt, new Box(1000, 2000, 1010, 2010), out _)!;
        var mz = new StructRegion3D.MeshZ(mzBig);
        double Pz(double x, double y) => 50 + 0.3 * (x - 1000) + 0.2 * (y - 2000);
        var d1 = StructRegion3D.Drape(new[] { new Point3(1001, 2001, 0), new Point3(1004, 2005, 0) }, false, mz);
        bool dz = d1.Count == 1 && d1[0].Pts.Count == 6 && d1[0].Pts.All(p => Math.Abs(p.Z - Pz(p.X, p.Y)) <= 1e-3);
        var d2 = StructRegion3D.Drape(new[] { new Point3(1002, 2002, 0), new Point3(1015, 2002, 0), new Point3(1015, 2004, 0), new Point3(1002, 2004, 0) }, false, mz);
        var d3 = StructRegion3D.Drape(sq, true, mz);
        Check(" ⑧ 높이 입히기 — 기운 평면 Z(1 mm) · 1 m 채움 · 메시 밖에서 끊김 · 다 입힌 닫힌 선은 닫힘", dz && d2.Count == 2 && d3.Count == 1 && d3[0].Closed,
            $"점 {(d1.Count > 0 ? d1[0].Pts.Count : 0)} · 끊긴 조각 {d2.Count}");

        // ⑨ 3D 파일 — format · version · structure · pointsFile · 머리 값이 점 파일 머리와 같음 · box · 세모 0부터 · 소수 셋째 자리 이하 · 한글 그대로
        mT.Name = "최종지표면_DH"; mT.Layer = "C-지표면";
        var lines = StructRegion3D.Tidy(new[]
        {
            new StructRegion3D.Line3("wall", "DH-옹벽선", false, new[] { new Point3(1001.00049, 2001.1234, 50.5), new Point3(1001.0004, 2001.1234, 50.5), new Point3(1003, 2003, 52.12345) }),
            new StructRegion3D.Line3("slope", "DH-사면선-절토", true, sq),
            new StructRegion3D.Line3("road", "진입도로 포장(가상)", false, new[] { new Point3(1002, 2002, 1), new Point3(1002.0001, 2002, 1) }),
        });
        string F(StructRegion3D.Mesh? m, IReadOnlyList<StructRegion3D.Line3> ls, StructPoints.Info inf)
        {
            using var ms = new MemoryStream();
            StructRegion3D.Write(ms, inf, "가상 구조물 «A»_Civil기준점.json", box, m, ls);
            return Encoding.UTF8.GetString(ms.ToArray());
        }
        string j3 = F(mT, lines, info);
        using var d3d = JsonDocument.Parse(j3);
        using var dpt = JsonDocument.Parse(StructPoints.Json(info, p1, p2));
        var r3 = d3d.RootElement; var rp = dpt.RootElement;
        bool head = new[] { "units", "axes", "crs", "drawing", "app", "created" }.All(k => JsonSame(r3.GetProperty(k), rp.GetProperty(k)));
        var jb = r3.GetProperty("box");
        bool boxOk = jb.GetProperty("x0").GetDouble() == 1000.25 && jb.GetProperty("y0").GetDouble() == 2000.75 && jb.GetProperty("x1").GetDouble() == 1003.75 && jb.GetProperty("y1").GetDouble() == 2003.25;
        var mesh0 = r3.GetProperty("meshes")[0];
        int nv = mesh0.GetProperty("verts").GetArrayLength();
        bool tri0 = mesh0.GetProperty("tris").GetArrayLength() == mT.T.Count && mesh0.GetProperty("tris").EnumerateArray().All(t => t.GetArrayLength() == 3 && t.EnumerateArray().All(i => i.GetInt32() >= 0 && i.GetInt32() < nv))
                    && mesh0.GetProperty("tris").EnumerateArray().Any(t => t.EnumerateArray().Any(i => i.GetInt32() == 0));
        bool dec3 = mesh0.GetProperty("verts").EnumerateArray().Concat(r3.GetProperty("lines").EnumerateArray().SelectMany(l => l.GetProperty("pts").EnumerateArray()))
                    .SelectMany(p => p.EnumerateArray()).All(n => { string s = n.GetRawText(); int dot = s.IndexOf('.'); return dot < 0 || s.Length - dot - 1 <= 3; });
        bool kinds = r3.GetProperty("lines").GetArrayLength() == 2 && r3.GetProperty("lines")[0].GetProperty("kind").GetString() == "wall" && r3.GetProperty("lines")[0].GetProperty("pts").GetArrayLength() == 2
                     && r3.GetProperty("lines")[1].GetProperty("kind").GetString() == "slope" && r3.GetProperty("lines")[1].GetProperty("closed").GetBoolean();
        bool fm3 = r3.GetProperty("format").GetString() == "DH.CivilRegion3D" && r3.GetProperty("version").GetInt32() == 1 && r3.GetProperty("structure").GetProperty("name").GetString() == "가상 구조물 «A»"
                   && r3.GetProperty("pointsFile").GetString() == "가상 구조물 «A»_Civil기준점.json" && mesh0.GetProperty("kind").GetString() == "final" && mesh0.GetProperty("name").GetString() == "최종지표면_DH"
                   && !r3.TryGetProperty("points", out _) && j3.Contains("가상 구조물 «A»") && !j3.Contains("\\u") && !j3.Contains('\n');
        using var dEmpty = JsonDocument.Parse(F(null, lines, info0));
        bool empty = dEmpty.RootElement.GetProperty("meshes").GetArrayLength() == 0 && JsonSame(dEmpty.RootElement.GetProperty("crs"), JsonDocument.Parse(StructPoints.Json(info0, p1, p2)).RootElement.GetProperty("crs"));
        Check(" ⑨ 3D 파일 — 형식 · 판 · structure · pointsFile · 머리 = 점 파일 머리(값) · box · 세모 0부터 · 소수 셋째 이하 · 한글 · 들여쓰기 없음 · 짧은 선 뺌 · 지표면 끔 = []",
            fm3 && head && boxOk && tri0 && dec3 && kinds && empty, $"글 {j3.Length:N0} 자 · 점 {nv} · 세모 {mT.T.Count} · 머리 {head} · box {boxOk} · 0부터 {tri0} · 셋째 {dec3} · 선 {kinds}");

        // ⑩ 점 파일 — 네모 있음 = region3d{file, box, tris, lines} · sketch.box · 가운데 = 네모 가운데 · 반지름 = 대각선 절반 · slope 종류
        var skb = StructRegion3D.SketchOf(box, new[] { new StructPoints.SketchLine("slope", "DH-사면선-절토", false, new[] { (1001.0, 2001.0), (1002.0, 2002.0) }) });
        var rr = new StructPoints.Region3DRef(StructRegion3D.FileFor("가상 구조물 «A»_Civil기준점.json"), box, mT.T.Count, lines.Count);
        using var dpb = JsonDocument.Parse(StructPoints.Json(info, p1, p2, skb, rr));
        var rb = dpb.RootElement;
        var reg = rb.GetProperty("region3d"); var sbx = rb.GetProperty("sketch");
        bool regOk = reg.GetProperty("file").GetString() == "가상 구조물 «A»_3D영역.json" && reg.GetProperty("tris").GetInt32() == mT.T.Count && reg.GetProperty("lines").GetInt32() == 2
                     && JsonSame(reg.GetProperty("box"), r3.GetProperty("box")) && JsonSame(sbx.GetProperty("box"), r3.GetProperty("box"))
                     && sbx.GetProperty("center")[0].GetDouble() == 1002.0 && sbx.GetProperty("center")[1].GetDouble() == 2002.0 && Math.Abs(sbx.GetProperty("radius").GetDouble() - Math.Round(Math.Sqrt(3.5 * 3.5 + 2.5 * 2.5) / 2, 3)) < 1e-12
                     && sbx.GetProperty("lines")[0].GetProperty("kind").GetString() == "slope" && rb.GetProperty("points")[0].GetProperty("x").GetDouble() == p1.X;
        bool noBox = !dpt.RootElement.TryGetProperty("region3d", out _) && !JsonDocument.Parse(StructPoints.Json(info, p1, p2, sk)).RootElement.GetProperty("sketch").TryGetProperty("box", out _);
        Check(" ⑩ 점 파일 — 네모 없음 = region3d · sketch.box 없음 / 있음 = region3d{file · box · tris · lines} · sketch.box · 가운데 · 반지름", regOk && noBox, $"반지름 {sbx.GetProperty("radius").GetDouble()}");

        // ⑩-b [내보내기] 켜짐 규칙(L9) · 넣을 것 글자 · 3D 파일 이름(L4)
        bool en = StructRegion3D.WhyNot(true, true, true, false, true, 999_999, 0) == ""
                  && StructRegion3D.WhyNot(false, true, true, false, true, 0, 0).Length > 0 && StructRegion3D.WhyNot(true, false, true, false, true, 0, 0).Length > 0 && StructRegion3D.WhyNot(true, true, false, false, true, 0, 0).Length > 0
                  && StructRegion3D.WhyNot(true, true, true, true, true, 300_001, 300_001).Contains("네모를 줄이세요") && StructRegion3D.WhyNot(true, true, true, true, true, 300_000, 300_000) == ""
                  && StructRegion3D.WhyNot(true, true, true, true, false, 300_001, 4) == "" && StructRegion3D.WhyNot(true, true, true, true, false, 300_001, 0).Contains("내보낼 것이 없음");
        bool inc = StructRegion3D.ParseInclude("", out var kAll) && kAll.Count == 6 && StructRegion3D.ParseInclude("전부", out _) && StructRegion3D.ParseInclude("fw", out var kFw) && kFw.SetEquals(new[] { "final", "wall" })
                   && StructRegion3D.ParseInclude("BSLR", out var k4) && k4.SetEquals(new[] { "bench", "slope", "site", "road" }) && !StructRegion3D.ParseInclude("FX", out _);
        bool fn = StructRegion3D.FileFor("A_Civil기준점.json") == "A_3D영역.json" && StructRegion3D.FileFor(@"C:\x\B.json") == "B_3D영역.json";
        Check(" ⑩-b [내보내기] 켜짐 규칙 · 넣을 것 글자(F W B S L R) · 3D 파일 이름", en && inc && fn, "");

        // ⑪ 시간 기록(합격 조건 아님) — 만든 30만 세모(약 387×387 m) 자르기 · 쓰기
        var big = Grid(1000, 2000, 388, (x, y) => 50 + 0.05 * (x - 1000) + 0.02 * (y - 2000), mixWinding: false);
        var sw = Stopwatch.StartNew();
        var bb = new Box(1000.5, 2000.5, 1387.5, 2387.5);
        int nb = StructRegion3D.CountTris(big, bb); long tc = sw.ElapsedMilliseconds; sw.Restart();
        var mb = StructRegion3D.Clip(big, bb, out _)!; long tk = sw.ElapsedMilliseconds; sw.Restart();
        long size;
        using (var ms = new MemoryStream()) { StructRegion3D.Write(ms, info, "x_Civil기준점.json", bb, mb, Array.Empty<StructRegion3D.Line3>()); size = ms.Length; }
        long tw = sw.ElapsedMilliseconds;
        Console.WriteLine($"  ⑪ 시간 — 세모 {big.Count:N0} → 네모 안 {nb:N0} · 세기 {tc} ms · 자르기 {tk} ms · 쓰기 {tw} ms · 파일 {size / 1e6:F1} MB");
        Check(" ⑪ 30만 세모 자르기 · 쓰기 기록(시간은 합격 조건 아님)", mb.T.Count == nb && nb > 290_000, $"{nb:N0}");
    }

    /// <summary>S242와 같은 만든 값 — 점 파일 정답 글(SHA-256)을 박는 데 쓴다.</summary>
    internal static (StructPoints.Info, StructPoints.Pt, StructPoints.Pt, StructPoints.Sketch) Sample()
    {
        var p1 = new StructPoints.Pt(123456.78912345678, 234567.89123456789, 0, "원지반", 71.85012, "최종지표면_DH", 72.1);
        var p2 = new StructPoints.Pt(123488.6891, 234566.8301, 0.25, "원지반", null, "", 72.2);
        var info = new StructPoints.Info("가상 구조물 «A»", 5186, "KOREA_GRS80_127TM", "drawing", "가상.dwg", @"C:\가상\가상.dwg", 6, true, "Civil 3D · AutoCAD 25.1", "DH.Grading v116.9.0", new DateTimeOffset(2026, 11, 10, 15, 20, 0, TimeSpan.FromHours(9)));
        var sk = new StructPoints.Sketch(123472.73, 234567.36, StructPoints.SketchRadius(31.9), new[]
        {
            new StructPoints.SketchLine("site", "가상-경계", true, new[] { (123400.0, 234500.0), (123550.0, 234500.0), (123550.0, 234650.0) }),
            new StructPoints.SketchLine("wall", "DH-옹벽선", false, new[] { (123410.1234, 234510.5678), (123420.0, 234520.0) }),
        });
        return (info, p1, p2, sk);
    }
}
