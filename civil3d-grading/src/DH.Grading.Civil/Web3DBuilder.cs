using Autodesk.AutoCAD.DatabaseServices;
using DH.Grading.Core;
using NetTopologySuite.Geometries;

namespace DH.Grading.Civil;

/// <summary>★★★[v107 · 계획_v107.md 2] [3D 보기]가 DHINFRA 수집에서 받아 가는 것 — InfraWorks 파일과 <b>같은 값</b>(계획면 · 띠 · 옹벽 변환 자리 · 단면 줄 · 정지면 · 원지반 · 토공량).</summary>
internal sealed class Web3DCollect
{
    public string Dwg = "", DwgKey = "";
    public int Epsg, BeltCm = 127;
    public double BeltFn = 600000;
    /// <summary>계획면 피처(링 목록 — 첫 링 바깥 · 나머지 구멍).</summary>
    public readonly List<IReadOnlyList<IReadOnlyList<Point3>>> Plan = new();
    /// <summary>띠 SHP 피처 — 열쇠 «소단_절토» · «사면_성토» …</summary>
    public readonly Dictionary<string, List<IReadOnlyList<IReadOnlyList<Point3>>>> Strips = new();
    public Geometry? WallMask;
    public List<WallExcav.SecLine>? Lines;
    public ObjectId TerrainId = ObjectId.Null;
    /// <summary>★[v111.8 · 계획 5장] 메모리에서 합친 최종지표면(최종지표면_DH가 없거나 옛것) — 있으면 TerrainId 대신(점 · 면은 지형.xml과 같은 mm 합침 · 세모는 높이 표용).</summary>
    public List<(double x, double y, double z)>? MeshPts;
    public List<(int a, int b, int c)>? MeshFaces;
    public IReadOnlyList<WallDaylight.Tri>? MeshTris;
    /// <summary>★[v111.8] 진입도로 자리(F ⊕ ε 합) · 포장(차로 + 길어깨) · 차로 · 정지면 높이(절/성 가름) · 차선을 그릴 도로 — 지형에 도로가 들었을 때만.</summary>
    public Geometry? RoadArea, RoadPave, RoadLane;
    /// <summary>★[v111.8.1] 도로 소단 띠(엔진 계단선 짝) — 지형에 도로가 들었을 때만.</summary>
    public Geometry? RoadBench;
    public CachedGroundSurface? BaseZ;
    public readonly List<RoadLanes.Road> Lanes = new();
    /// <summary>★[v111.8 · M6] 도면에 진입도로가 있다 — 토공량 칸 «진입도로 제외».</summary>
    public bool HasRoads;
    /// <summary>지형 원천 이름(로그 · 시험) — «정지면_DH» · «최종지표면_DH» · «메모리 합침».</summary>
    public string TerrainName = "정지면_DH";
    public CachedGroundSurface? Ground;
    public string GroundName = "", GroundOfName = "";
    public double Cut = double.NaN, Fill = double.NaN, PlanArea = double.NaN;
    /// <summary>DHINFRA와 같은 옹벽 · SHP 자가진단 + 3D 몫.</summary>
    public readonly List<string> Warn = new();
}

/// <summary>★★★[v107 · 계획_v107.md 2 · 3 · 검토 반영 8] <b>[3D 보기] 장면 짓기</b> — 수집 → 지형(구분 · 절성토) · 원지반(바뀐 칸만) · 옹벽(몸통 · 무늬) · 위성(자세히 + 바탕) · 토공량 → 페이지 하나.
/// <para>도면은 읽기만 한다. 페이지 틀 · 뷰어는 DLL 안 리소스(<c>web3d/page.html</c> · <c>web3d/dist/viewer.js</c>).</para></summary>
internal static class Web3DBuilder
{
    internal static string Dir => System.IO.Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData), "DH.Grading", "3d");

    /// <summary>★[v111.7 · JACK 1102] 건물 한도 — 넘으면 범위를 가운데로 줄인다(<see cref="VWorldBuildings"/>).</summary>
    internal const int MaxBuildings = 5000;
    /// <summary>★[v111.7.1 · JACK 1102] 건물 층 풍선 글 끝 한 줄 — 자료 한계.</summary>
    internal const string BldgNote = "VWorld 자료 기준 — 철거된 건물이 남아 있을 수 있음";

    /// <summary>★[v111.8 · 시험 이음매] 마지막 장면의 지형 — 원천 · 세모 수 · 구분별 수 · 차선(줄 · 칸 · 남긴 칸 · 차선 높이 − (지형 + 0.03) 최대 |m|).</summary>
    internal sealed record TerrainStats(string Source, int Tris, Dictionary<string, int> Classes, int LaneLines, int LaneCells, int LaneKept, double LaneDzMax, string LaneText);
    internal static TerrainStats? LastTerrainStats;

    /// <summary>마지막에 지은 페이지 — 저장 단추 · 팔레트가 쓴다.</summary>
    internal static string LastPath = "", LastDwg = "", LastDwgKey = "";
    internal static long LastBytes;

    internal static string Resource(string name)
    {
        var asm = typeof(Web3DBuilder).Assembly;
        using var s = asm.GetManifestResourceStream(name) ?? throw new System.IO.FileNotFoundException("리소스 없음 — " + name);
        using var r = new System.IO.StreamReader(s, System.Text.Encoding.UTF8);
        return r.ReadToEnd();
    }

    static Geometry? Union(IEnumerable<IReadOnlyList<IReadOnlyList<Point3>>> feats, GeometryFactory gf)
    {
        LinearRing? Ring(IReadOnlyList<Point3> r)
        {
            if (r == null || r.Count < 3) return null;
            var cs = r.Select(p => new Coordinate(p.X, p.Y)).ToList();
            if (!cs[0].Equals2D(cs[^1])) cs.Add(cs[0].Copy());
            if (cs.Count < 4) return null;
            return gf.CreateLinearRing(cs.ToArray());
        }
        var polys = new List<Geometry>();
        foreach (var rings in feats)
        {
            try
            {
                if (rings.Count == 0) continue;
                var shell = Ring(rings[0]);
                if (shell == null) continue;
                var holes = rings.Skip(1).Select(Ring).Where(h => h != null).Cast<LinearRing>().ToArray();
                Geometry p = gf.CreatePolygon(shell, holes);
                if (!p.IsValid) p = p.Buffer(0);
                if (!p.IsEmpty) polys.Add(p);
            }
            catch { }
        }
        if (polys.Count == 0) return null;
        try { return NetTopologySuite.Operation.Union.CascadedPolygonUnion.Union(polys); }
        catch { return gf.BuildGeometry(polys).Buffer(0); }
    }

    /// <summary>짓고 페이지 파일 경로를 돌려준다. <paramref name="skipSat"/>면 위성 안 받음(화면 없는 시험).</summary>
    internal static string Build(Database db, Web3DCollect c, System.Text.StringBuilder log, bool skipSat)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var times = new List<string>();
        // 단계마다 시간 · 로딩 창 글(다음 단계 — 종단 «구성 중» 창과 같은 창 · 화면 없는 시험은 아무것도 안 함)
        void Lap(string nm, string? next = null) { times.Add($"{nm} {sw.ElapsedMilliseconds / 1000.0:F1}초"); sw.Restart(); if (next != null) BusyPopup.Show("3D 보기 — " + next); }
        BusyPopup.Show("3D 보기 — 지형 읽는 중");
        var gf = NtsSupport.Factory();
        var sc = new Web3DScene();

        // ① 정지면 점 · 면(지형.xml과 같은 것) · ★[v111.8] 메모리에서 합친 최종지표면이면 그 점 · 면
        if (c.TerrainId.IsNull && c.MeshPts == null) throw new System.InvalidOperationException("정지면_DH가 없습니다 — 먼저 부지정지를 하세요");
        List<(double x, double y, double z)> pts; List<(int a, int b, int c)> faces;
        if (c.MeshPts != null && c.MeshFaces != null) { pts = c.MeshPts; faces = c.MeshFaces; }
        else
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var tin = (Autodesk.Civil.DatabaseServices.TinSurface)tr.GetObject(c.TerrainId, OpenMode.ForRead);
                (pts, faces) = LandXmlExport.ReadMesh(tin);
                tr.Commit();
            }
        if (pts.Count < 3 || faces.Count == 0) throw new System.InvalidOperationException("정지면_DH 삼각형이 없습니다");
        double mnx = pts.Min(p => p.x), mxx = pts.Max(p => p.x), mny = pts.Min(p => p.y), mxy = pts.Max(p => p.y), mnz = pts.Min(p => p.z), mxz = pts.Max(p => p.z);
        double ox = System.Math.Round((mnx + mxx) / 2 / 100) * 100, oy = System.Math.Round((mny + mxy) / 2 / 100) * 100;
        Lap("정지면 읽기", "옹벽 짓는 중");

        // ② 옹벽(몸통 · 무늬) — 옹벽3D.dwg와 같은 차례
        WallScene.Result? walls = null;
        if (c.Lines != null && c.Lines.Count > 0)
        {
            walls = WallScene.Build(c.Lines, ox, oy);
            log.AppendLine("  옹벽: " + walls.Text);
            if (walls.Fails + walls.CornerFails > 0) c.Warn.Add($"옹벽 3D 일부 못 지음 {walls.Fails + walls.CornerFails}개 — {walls.FirstFail}");
            if (walls.PieceFails + walls.PatClipFail > 0) c.Warn.Add($"옹벽 무늬 조각 일부 못 지음 {walls.PieceFails + walls.PatClipFail}개(그 자리는 몸통 색)");
            if (walls.PatLineFails > 0) c.Warn.Add($"무늬를 못 깐 옹벽 줄 {walls.PatLineFails}개(그 줄은 몸통 색만)");
            if (walls.NegVolume > 0) c.Warn.Add($"안팎이 뒤집힌 옹벽 덩어리 {walls.NegVolume}개");
        }
        Lap("옹벽", "지형 나누는 중");

        // ③ 지형 구분 — SHP와 같은 다각형 · 옹벽 변환 자리 · 3D 옹벽 발자국
        var inp = new TerrainMesh.Input
        {
            X = pts.Select(p => p.x).ToArray(), Y = pts.Select(p => p.y).ToArray(), Z = pts.Select(p => p.z).ToArray(),
            F = faces.SelectMany(f => new[] { f.a - 1, f.b - 1, f.c - 1 }).ToArray(),
            GroundZ = c.Ground == null ? null : (x, y) => c.Ground.TryGetElevation(x, y, out double z) ? z : null,
            Plan = Union(c.Plan, gf),
            Bench = Union(c.Strips.Where(k => k.Key.StartsWith("소단", System.StringComparison.Ordinal)).SelectMany(k => k.Value), gf),
            SlopeCut = Union(c.Strips.TryGetValue("사면_절토", out var sc0) ? sc0 : Enumerable.Empty<IReadOnlyList<IReadOnlyList<Point3>>>(), gf),
            SlopeFill = Union(c.Strips.TryGetValue("사면_성토", out var sf0) ? sf0 : Enumerable.Empty<IReadOnlyList<IReadOnlyList<Point3>>>(), gf),
            WallMask = c.WallMask,
            WallFoot = walls?.Foot,
            // ★[v111.8 · 계획 6장] 진입도로 자리 · 포장 · 차로 · 정지면 높이(도로가 지형에 들었을 때만 — 아니면 null = 옛 규칙)
            RoadArea = c.RoadArea, RoadPave = c.RoadPave, RoadLane = c.RoadLane, RoadBench = c.RoadBench,
            BaseZ = c.BaseZ == null ? null : (x, y) => c.BaseZ.TryGetElevation(x, y, out double z) ? z : null,
        };
        var tm = TerrainMesh.Build(inp, ox, oy);
        log.AppendLine("  지형: 세모 " + faces.Count.ToString("N0") + " · " + string.Join(" · ", tm.Ranges.Select(r => $"{TerrainMesh.Names[(int)r.Cls]} {r.Count:N0}"))
                       + (c.Ground == null ? " · 원지반 없음(절성토 색 없음)" : $" · 원지반 없는 점 {tm.NoGround:N0}"));
        Lap("지형 구분");

        // ④ 원지반 층 — 바뀐 칸 안만
        (float[] Pos, int[] Idx) ground = (System.Array.Empty<float>(), System.Array.Empty<int>());
        if (c.Ground != null && tm.GMark.Length > 0)
        {
            ground = TerrainMesh.Clip(c.Ground.AllTriangles().Select(t => (t.A, t.B, t.C)), tm, ox, oy);
            log.AppendLine($"  원지반 층: 세모 {ground.Idx.Length / 3:N0}(바뀐 칸 안)");
        }
        Lap("원지반", "빗물 · 지적 · 측점");

        // ④b ★[v108 · 계획_v108.md 2.1 · 2.6 · 2.7] 빗물(바뀐 자리 + 30 m) · 지적(도면 레이어) · 종단 측점(Civil 선형) — 높이는 정지면(없으면 원지반)
        CachedGroundSurface? designC = null;
        if (c.MeshTris != null) designC = new CachedGroundSurface(c.MeshTris);   // ★[v111.8] 메모리 합침 — 같은 격자 코드
        else try { using var trD = db.TransactionManager.StartTransaction(); designC = new CachedGroundSurface((Autodesk.Civil.DatabaseServices.TinSurface)trD.GetObject(c.TerrainId, OpenMode.ForRead)); trD.Commit(); } catch { }
        // ④a ★★[v111.8 · 계획 6장 · JACK Q1 · Q5] 흰 차선 — 설계(노선 + 폭)에서 · 높이 = 지형 + 0.03 m · 그 도로 면 발자국 F 밖 칸은 버림(도로 면이 지금 설계와 다름) · 5 % 넘게 버리면 ⚠
        object? laneHead = null;
        int laneLines = 0, laneCells = 0, laneKept = 0; double laneDz = 0; var laneTexts = new List<string>();
        if (c.Lanes.Count > 0 && designC != null)
        {
            try
            {
                var lp = new List<float>(); var li = new List<int>();
                using var trL = db.TransactionManager.StartTransaction();
                foreach (var rd in c.Lanes)
                {
                    var pf = rd.Foot == null ? null : NetTopologySuite.Geometries.Prepared.PreparedGeometryFactory.Prepare(rd.Foot);
                    double? Z(double x, double y) => (pf == null || pf.Covers(gf.CreatePoint(new Coordinate(x, y)))) && designC.TryGetElevation(x, y, out double z) ? z : null;
                    var rr = RoadLanes.Lanes(trL, rd, Z);
                    int b0 = lp.Count / 3;
                    foreach (var p in rr.Pos)
                    {
                        lp.Add((float)(p.X - ox)); lp.Add((float)(p.Y - oy)); lp.Add((float)p.Z);
                        // 자가 점검 — 차선 높이 = 지형 + 0.03(그 자리 지형을 다시 읽어 견줌)
                        if (designC.TryGetElevation(p.X, p.Y, out double zt)) laneDz = System.Math.Max(laneDz, System.Math.Abs(p.Z - (zt + RoadMarks.Lift)));
                    }
                    foreach (int i in rr.Idx) li.Add(b0 + i);
                    laneLines += rr.Lines; laneCells += rr.Cells; laneKept += rr.Kept;
                    laneTexts.Add($"«{rd.Name}» {rr.Text}");
                    if (rr.Cells > 0 && rr.DropNoZ > 0.05 * rr.Cells) c.Warn.Add($"진입도로 «{rd.Name}» 지표면이 지금 설계와 다름 — [업데이트] 뒤 [3D 보기]");
                }
                trL.Commit();
                if (li.Count > 0) laneHead = new Dictionary<string, object> { ["pos"] = sc.F32(lp.ToArray()), ["idx"] = sc.U32(li.ToArray()), ["n"] = laneLines };
                log.AppendLine($"  차선: {string.Join(" · ", laneTexts)} · 높이 − (지형 + {RoadMarks.Lift} m) 최대 {laneDz * 1000:F3} mm");
            }
            catch (System.Exception lex) { c.Warn.Add("흰 차선을 못 그렸습니다 — " + lex.Message); log.AppendLine("  차선: 실패 — " + lex); }
        }
        // ④a2 ★★[도로 마무리 ② · 계획 6.6] 측구 — 윗면 = 도로 면 측구 자리(«길어깨» 콘크리트 구분 · RoadLanes 포장 폭) · 그 위:
        //   U형 = 홈 자리(안폭)에 어두운 띠(그레이팅 · 뚜껑 없으면 물길) · L형 = 경계석 몸통(폭 · 보이는 높이)을 지형 위로 · 땅속 몸통은 안 그림(계획 12장).
        //   측구 기록이 없으면 머리를 안 넣음(장면 바이트 그대로)
        object? ditchHead = null;
        if (c.Lanes.Any(r => r.D != null && r.D.Ditches.Count > 0) && designC != null)
        {
            try
            {
                var gp = new List<float>(); var gi = new List<int>(); var wp = new List<float>(); var wi = new List<int>(); var cp = new List<float>(); var ci = new List<int>();
                int nU = 0, nL = 0;
                using var trD = db.TransactionManager.StartTransaction();
                foreach (var rd in c.Lanes)
                {
                    if (rd.D == null || rd.D.Ditches.Count == 0) continue;
                    var al = (Autodesk.Civil.DatabaseServices.Alignment)trD.GetObject(rd.Aid, OpenMode.ForRead);
                    var sec = rd.D.Section();
                    (double X, double Y)? At(double s, double o) { try { double x = 0, y = 0; al.PointLocation(s, o, ref x, ref y); return double.IsFinite(x) && double.IsFinite(y) ? (x, y) : null; } catch { return null; } }
                    double? Z(double x, double y) => designC.TryGetElevation(x, y, out double z) ? z : null;
                    void Put(List<float> P, List<int> I, RoadMarks.RibbonResult rr)
                    {
                        int b0 = P.Count / 3;
                        foreach (var p in rr.Pos) { P.Add((float)(p.X - ox)); P.Add((float)(p.Y - oy)); P.Add((float)p.Z); }
                        foreach (int i in rr.Idx) I.Add(b0 + i);
                    }
                    foreach (int side in new[] { -1, 1 })
                        foreach (var (a0, b0, rec) in rd.D.DitchSpans(side))
                        {
                            var sh = RoadDesign.ShapeOf(rec);
                            if (sh == null) continue;
                            double outer = side < 0 ? sec.OuterL : sec.OuterR;
                            foreach (var (a, b) in RoadRanges.Clip(rd.Spans, a0, b0))
                            {
                                var st = RoadDitchLines.Stations(al, a, b);
                                List<(double X, double Y)> Line(double o) => st.Select(s => At(s, side * (outer + o)) ?? (double.NaN, double.NaN)).ToList();
                                var ctr = st.Select(s => At(s, 0) ?? (double.NaN, double.NaN)).ToList();
                                if (sh.Kind == DH.Grading.Core.DitchSection.Kind.U && sh.Channel is { } ch)
                                {
                                    bool lid = rec.Length > 12 && rec[12] > 0.5;
                                    Put(lid ? gp : wp, lid ? gi : wi, RoadMarks.Ribbon(Line(ch.O0), Line(ch.O1), Z, RoadMarks.Lift, RoadMarks.MinRadius, ctr));
                                    nU++;
                                }
                                else if (sh.Kind == DH.Grading.Core.DitchSection.Kind.L && sh.Curb is { Count: >= 4 } cb)
                                {
                                    double o0 = cb.Min(q => q.O), o1 = cb.Max(q => q.O), reveal = rec.Length > 8 ? rec[8] : 0.2;
                                    var la = Line(o0); var lb = Line(o1);
                                    Put(cp, ci, RoadMarks.Ribbon(la, lb, Z, reveal, RoadMarks.MinRadius, ctr));   // 윗면(지형 + 보이는 높이)
                                    foreach (var edge in new[] { la, lb })   // 앞 · 뒤 옆면(지형 → 윗면)
                                        for (int i = 0; i + 1 < edge.Count; i++)
                                        {
                                            var p = edge[i]; var q = edge[i + 1];
                                            if (!double.IsFinite(p.X) || !double.IsFinite(q.X) || Z(p.X, p.Y) is not double zp || Z(q.X, q.Y) is not double zq) continue;
                                            int k = cp.Count / 3;
                                            foreach (var (x, y, z) in new[] { (p.X, p.Y, zp), (q.X, q.Y, zq), (q.X, q.Y, zq + reveal), (p.X, p.Y, zp + reveal) }) { cp.Add((float)(x - ox)); cp.Add((float)(y - oy)); cp.Add((float)z); }
                                            ci.AddRange(new[] { k, k + 1, k + 2, k, k + 2, k + 3 });
                                        }
                                    nL++;
                                }
                            }
                        }
                }
                trD.Commit();
                var dh = new Dictionary<string, object>();
                if (gi.Count > 0) dh["grate"] = new Dictionary<string, object> { ["pos"] = sc.F32(gp.ToArray()), ["idx"] = sc.U32(gi.ToArray()) };
                if (wi.Count > 0) dh["water"] = new Dictionary<string, object> { ["pos"] = sc.F32(wp.ToArray()), ["idx"] = sc.U32(wi.ToArray()) };
                if (ci.Count > 0) dh["curb"] = new Dictionary<string, object> { ["pos"] = sc.F32(cp.ToArray()), ["idx"] = sc.U32(ci.ToArray()), ["mat"] = "콘크리트" };
                if (dh.Count > 0) ditchHead = dh;
                log.AppendLine($"  측구: U형 {nU}구간 · L형 {nL}구간 · 세모 그레이팅 {gi.Count / 3:N0} · 물길 {wi.Count / 3:N0} · 경계석 {ci.Count / 3:N0}");
            }
            catch (System.Exception dex) { c.Warn.Add("측구를 못 그렸습니다 — " + dex.Message); log.AppendLine("  측구: 실패 — " + dex); }
        }
        LastTerrainStats = new TerrainStats(c.TerrainName, faces.Count, tm.Ranges.GroupBy(r => TerrainMesh.Keys[(int)r.Cls]).ToDictionary(g => g.Key, g => g.Sum(r => r.Count)),
                                            laneLines, laneCells, laneKept, laneDz, string.Join(" · ", laneTexts));
        double? SurfZ(double x, double y)
        {
            if (designC != null && designC.TryGetElevation(x, y, out double zd)) return zd;
            if (c.Ground != null && c.Ground.TryGetElevation(x, y, out double zg)) return zg;
            return null;
        }
        object? drainHead = null, cadHead = null, alignHead = null, buildHead = null, strataHead = null;
        double[]? alignBox = null;   // 측점 가로선 범위(도면 좌표 minx, miny, maxx, maxy) — 지층 격자 범위
        // ★[계획 검토 높음 4] 시공 재생 격자 — 바뀐 자리 + 10 m · 1 m(40만 칸 넘으면 키움) · 마디마다 정지면 · 원지반 높이(원지반 없으면 정지면) — 꼭짓점만 내리면 부지 밑 봉우리가 사라진다
        try
        {
            if (double.IsFinite(tm.CMinX) && c.Ground != null && designC != null)
            {
                double gx0 = tm.CMinX - 10, gy0 = tm.CMinY - 10, gx1 = tm.CMaxX + 10, gy1 = tm.CMaxY + 10, gc = 1.0;
                while ((gx1 - gx0) / gc * ((gy1 - gy0) / gc) > 400_000) gc *= 1.25;
                int gw = (int)System.Math.Ceiling((gx1 - gx0) / gc), gh = (int)System.Math.Ceiling((gy1 - gy0) / gc);
                var zf = new float[(gw + 1) * (gh + 1)]; var zg = new float[zf.Length];
                for (int j = 0; j <= gh; j++)
                    for (int i = 0; i <= gw; i++)
                    {
                        double x = gx0 + i * gc, y = gy0 + j * gc; int k = j * (gw + 1) + i;
                        zf[k] = designC.TryGetElevation(x, y, out double a) ? (float)a : float.NaN;
                        zg[k] = c.Ground.TryGetElevation(x, y, out double b) ? (float)b : zf[k];
                    }
                var bh = new Dictionary<string, object> { ["x0"] = Web3DScene.Num(gx0 - ox, 3), ["y0"] = Web3DScene.Num(gy0 - oy, 3), ["cell"] = Web3DScene.Num(gc, 3), ["w"] = gw, ["h"] = gh, ["zf"] = sc.F32(zf), ["zg"] = sc.F32(zg) };
                // ★★[v111.8.1 · JACK 1103 «시공순서도 현실성있게 · 보통 진입도로부터 하지 않아?»] 진입도로 자리 마디(1 = 진입도로 자리 RoadArea 안 — 도로 면 + 그 절 · 성토 사면 + 이음 띠) —
                //   뷰어가 «원지반 → 진입도로 → 절토 → 옹벽 → 성토 → 포장·녹화»로 돈다(장비가 들어갈 길을 먼저 연다 · 포장은 장비 일이 끝난 뒤).
                //   도로가 지형에 없으면 이 열쇠를 안 넣는다(옛 장면 바이트 그대로 · 옛 다섯 단계).
                int nRoad = 0;
                if (c.RoadArea != null && !c.RoadArea.IsEmpty) bh["road"] = sc.U8(TerrainMesh.GridMask(c.RoadArea, gx0, gy0, gc, gw, gh, out nRoad));
                buildHead = bh;
                log.AppendLine($"  시공 격자: {gw}×{gh} · 칸 {gc:0.##} m" + (c.RoadArea != null ? $" · 진입도로 자리 마디 {nRoad:N0}" : ""));
            }
        }
        catch (System.Exception bex) { c.Warn.Add("시공 순서 자료를 못 만들었습니다 — " + bex.Message); log.AppendLine("  시공 격자: 실패 — " + bex); }
        try
        {
            if (double.IsFinite(tm.CMinX))
            {
                // ★[계획 검토 중간 5] 범위 = 바뀐 자리 + 200 m — 비탈 위 산에서 내려오는 물(산마루 배수)도 센다
                var dr = Drainage.Analyze(inp.X, inp.Y, inp.Z, inp.F, tm.CMinX - 200, tm.CMinY - 200, tm.CMaxX + 200, tm.CMaxY + 200, cell: 1.0, streamMin: 300, flatZone: tm.InChanged);
                log.AppendLine("  빗물: " + dr.Text);
                // 그리는 물길은 현장(바뀐 자리 + 30 m)에 닿는 것만 — 계산은 200 m 둘레(위에서 내려오는 물)까지 하지만 먼 골짜기까지 그리면 현장 물길이 묻힌다(1027 시험 180줄)
                bool Near(Point3 p) => p.X >= tm.CMinX - 30 && p.X <= tm.CMaxX + 30 && p.Y >= tm.CMinY - 30 && p.Y <= tm.CMaxY + 30;
                var shown = dr.Streams.Where(s => s.Pts.Any(Near)).ToList();
                log.AppendLine($"  빗물 물길 그림: 현장에 닿는 {shown.Count}줄 / {dr.Streams.Count}줄");
                drainHead = new Dictionary<string, object>
                {
                    ["cell"] = Web3DScene.Num(dr.Cell, 3),
                    ["streams"] = shown.Select(s => (object)new Dictionary<string, object>
                    {
                        ["pos"] = sc.F32(s.Pts.SelectMany(p => new[] { (float)(p.X - ox), (float)(p.Y - oy), (float)(p.Z + 0.08) }).ToArray()),
                        ["acc"] = sc.F32(s.Acc.Select(a => (float)a).ToArray()),
                    }).ToList(),
                    ["ponds"] = dr.Ponds.Select(p => (object)new Dictionary<string, object>
                    {
                        ["level"] = Web3DScene.Num(p.Level, 3), ["area"] = Web3DScene.Num(p.Area, 1), ["depth"] = Web3DScene.Num(p.Depth, 3), ["vol"] = Web3DScene.Num(p.Volume, 1),
                        ["runs"] = sc.F32(p.Runs.SelectMany(q => new[] { (float)(q.X0 - ox), (float)(q.Y0 - oy), (float)(q.X1 - ox), (float)(q.Y1 - oy) }).ToArray()),
                    }).ToList(),
                    ["flats"] = sc.F32(dr.FlatRuns.SelectMany(q => new[] { (float)(q.X0 - ox), (float)(q.Y0 - oy), (float)(q.X1 - ox), (float)(q.Y1 - oy), (float)q.Z0, (float)q.Z1 }).ToArray()),
                    ["flatArea"] = Web3DScene.Num(dr.FlatArea, 1),
                };
            }
        }
        catch (System.Exception dex) { c.Warn.Add("빗물 검토를 못 했습니다 — " + dex.Message); log.AppendLine("  빗물: 실패 — " + dex); }
        try
        {
            // ★[계획 검토 낮음 10] 지적은 현장 + 200 m만(서버 원지반 범위 수 km 전체를 그리면 무겁고 어지럽다)
            bool hasC = double.IsFinite(tm.CMinX);
            var (seg, labels, lines) = Web3DExtras.Cadastral(db, SurfZ, hasC ? tm.CMinX - 200 : mnx, hasC ? tm.CMinY - 200 : mny, hasC ? tm.CMaxX + 200 : mxx, hasC ? tm.CMaxY + 200 : mxy, ox, oy);
            log.AppendLine($"  지적: 선 {lines:N0} · 지번 {labels.Count:N0}");
            cadHead = lines == 0 ? null : new Dictionary<string, object>
            {
                ["seg"] = sc.F32(seg),
                ["labels"] = labels.Select(l => (object)new Dictionary<string, object> { ["t"] = l.T, ["x"] = Web3DScene.Num(l.X, 2), ["y"] = Web3DScene.Num(l.Y, 2), ["z"] = Web3DScene.Num(l.Z, 2) }).ToList(),
            };
            if (lines == 0) c.Warn.Add("지적이 도면에 없습니다 — [지적도 가져오기] 뒤 [새로고침]");
        }
        catch (System.Exception cex) { c.Warn.Add("지적을 못 읽었습니다 — " + cex.Message); log.AppendLine("  지적: 실패 — " + cex); }
        try
        {
            var stn = Web3DExtras.Stations(db, SurfZ, ox, oy);
            if (stn != null)
            {
                var (aname, aline, as0, as1, aindex, sts, nviews, askip) = stn.Value;
                if (askip > 0) c.Warn.Add($"선형 밖 측점 {askip}개는 3D에 안 그렸습니다(선형을 고친 뒤 남은 샘플 선)");
                log.AppendLine($"  측점: 선형 {aname} · 측점 {sts.Count} · 횡단 뷰 {nviews}");
                double[] N3(double[] v) => v.Select(q => double.IsFinite(q) ? Web3DScene.Num(q, 3) : -99999).ToArray();
                alignHead = new Dictionary<string, object>
                {
                    // 중심선 = 시작 측점 s0부터 2 m마다 + 끝 측점 s1(마지막 토막은 2 m보다 짧을 수 있음)
                    ["name"] = aname, ["line"] = sc.F32(aline), ["s0"] = Web3DScene.Num(as0, 3), ["s1"] = Web3DScene.Num(as1, 3), ["step"] = 2, ["index"] = Web3DScene.Num(aindex, 3), ["views"] = nviews,
                    ["st"] = sts.Select(s => (object)new Dictionary<string, object>
                    {
                        ["s"] = Web3DScene.Num(s.S, 3), ["label"] = s.Label, ["major"] = s.Major, ["views"] = s.Views, ["c"] = N3(s.C), ["l"] = N3(s.L), ["r"] = N3(s.R), ["pts"] = sc.F32(s.P),
                    }).ToList(),
                };
                double bx0 = double.MaxValue, by0 = double.MaxValue, bx1 = double.MinValue, by1 = double.MinValue;
                foreach (var s in sts)
                    foreach (var q in new[] { s.L, s.C, s.R })
                        if (double.IsFinite(q[0]) && double.IsFinite(q[1])) { bx0 = System.Math.Min(bx0, q[0] + ox); by0 = System.Math.Min(by0, q[1] + oy); bx1 = System.Math.Max(bx1, q[0] + ox); by1 = System.Math.Max(by1, q[1] + oy); }
                if (bx0 <= bx1) alignBox = new[] { bx0, by0, bx1, by1 };
            }
        }
        catch (System.Exception aex) { c.Warn.Add("종단 측점을 못 읽었습니다 — " + aex.Message); log.AppendLine("  측점: 실패 — " + aex); }
        // ④e ★★[JACK 1103 «3D 뷰에서 진입도로 측점이 없어 · 부지 측점, 진입도로 측점이 별도로»] 진입도로 측점 — 도로마다 종단도 검토선(«{노선}-검토») · 도로가 없으면 열쇠를 안 넣는다(옛 장면 바이트 그대로)
        List<object>? roadAlignHead = null;
        try { roadAlignHead = RoadStations(db, SurfZ, ox, oy, sc, log, c.Warn); }
        catch (System.Exception rex) { c.Warn.Add("진입도로 측점을 못 읽었습니다 — " + rex.Message); log.AppendLine("  진입도로 측점: 실패 — " + rex); }
        // ④d ★★[JACK 1103 «3D 뷰에서 지층을 모델링했다면 자르기 뷰에서 지층도 보이게»] 지층 — 자르기는 측점 가로선이라 측점이 있을 때만 ·
        //   지층면(DH_지층_n_*)이 없으면 열쇠를 안 넣는다(옛 장면 바이트 그대로)
        if (alignBox != null)
            try { strataHead = Strata(db, alignBox, ox, oy, sc, log); }
            catch (System.Exception stx) { c.Warn.Add("지층을 3D에 못 넣었습니다 — " + stx.Message); log.AppendLine("  지층: 실패 — " + stx); }
        Lap("빗물 · 지적 · 측점", skipSat ? null : "건물 받는 중");

        // ④c ★★[v111.7 · JACK 1102 «3D 보기에 건물 · 층수로 높이 · 켜고 끄기»] 건물 — VWorld 건물정보
        //   ★[v111.7.1 · JACK 1102 «주변 아파트가 빠짐»] 범위 = 3D 지형 전체(위성 바탕과 같은 범위) · 많으면 바뀐 자리 + 500 → + 200 → + 50 m → 가운데
        //   높이 = 지상층수 × 3.5 m(0 · 빈칸은 안 그림) · 바닥 = 발자국 아래 원지반 가장 낮은 곳(그 건물 밑에 원지반이 없으면 보이는 지형) · 바뀌는 자리에 닿으면 빨강 반투명
        //   못 받아도(키 · 인터넷) 3D는 그대로 — 층 목록에 회색 + ⚠ · 장면에만(도면 · InfraWorks 파일에는 안 넣음) · 화면 없는 시험(skipSat)은 인터넷을 안 쓴다
        //   ★[검토 M · 보안] 페이지(경고 · 층 글)에는 정해 둔 짧은 글만 — 받은 글 · 예외 글은 진단 로그에만(키를 지우고)
        object? bldgHead = null;
        IReadOnlyList<Web3DBuildings.Bldg>? bldgItems = null;   // ★[v115 · JACK 1105 임상도] 수목 빼기(건물 + 1 m)
        if (skipSat) log.AppendLine("  건물: 시험 — 건너뜀");
        else
        {
            string why = "", detail = "", key = VWorldKey.Current;
            try
            {
                bool hasC = double.IsFinite(tm.CMinX);
                var terrain = new VWorldParcels.Box(mnx, mny, mxx, mxy);
                VWorldParcels.Box? changed = hasC ? new VWorldParcels.Box(tm.CMinX, tm.CMinY, tm.CMaxX, tm.CMaxY) : null;
                VWorldBuildings.Got? got = null;
                if (c.Epsg <= 0) why = "도면 좌표계를 몰라 건물을 받지 못했습니다";
                else if (key.Length == 0) why = VWorldKey.NoKeyHelp;
                else
                {
                    // ★[검토 L] 도면에 좌표계가 없으면(설정값으로 받음) 알림 — 원점이 다르면 건물이 엉뚱한 자리에 선다
                    string csCode = "";
                    try { csCode = KoreaCs.Read(db); } catch { }
                    if (KoreaCs.ResolveEpsgFromCode(csCode) == null)
                        c.Warn.Add((string.IsNullOrEmpty(csCode) ? "도면 좌표계 없음" : "도면 좌표계 자동인식 밖") + $" — 설정 EPSG:{c.Epsg}로 건물을 받음");
                    try { got = VWorldBuildings.Fetch(Web3DBuildings.Ranges(terrain, changed), changed ?? terrain, hasC ? "바뀐 자리" : "지형", c.Epsg, key, MaxBuildings, s => BusyPopup.Text("3D 보기 — " + s)); }
                    catch (VWorldBuildings.Fail f) { why = f.Page; detail = f.Message; }
                }
                if (got != null)
                {
                    // 바닥 — 원지반(없으면 보이는 지형) · 원지반이 있어도 그 건물 밑에 없으면 보이는 지형으로 다시(검토 M)
                    Func<double, double, double?> baseZ = c.Ground != null ? (x, y) => c.Ground.TryGetElevation(x, y, out double z) ? z : null : SurfZ;
                    var pr = Web3DBuildings.Place(got.Raws, baseZ, tm.InChanged, c.Ground != null ? SurfZ : null);
                    bldgItems = pr.Items;
                    log.AppendLine($"  건물: {pr.Text} · {got.Diag}");
                    if (got.Shrunk)
                        c.Warn.Add($"건물이 많아(지형 전체 약 {got.Total0:N0}동) {got.Box.W:N0}×{got.Box.H:N0} m 범위만 3D에 넣었습니다({got.Label} · 한도 {MaxBuildings:N0}동)");
                    // 그릴 것이 없음 — 실패가 아니라 ⚠ 없이 회색(quiet) · 뺀 까닭은 셈만
                    if (pr.Raws == 0) bldgHead = new Dictionary<string, object> { ["missing"] = "이 범위에 건물 자료가 없습니다", ["quiet"] = true };
                    else if (pr.Items.Count == 0) bldgHead = new Dictionary<string, object> { ["missing"] = $"그릴 건물이 없습니다 — 받은 {pr.Raws:N0}동 · {pr.DropText}", ["quiet"] = true };
                    else
                    {
                        var ar = Web3DBuildings.Pack(pr.Items, ox, oy);
                        bldgHead = new Dictionary<string, object>
                        {
                            ["n"] = pr.Items.Count, ["xy"] = sc.F32(ar.Xy), ["ring"] = sc.U32(ar.Ring), ["poly"] = sc.U32(ar.Poly), ["bldg"] = sc.U32(ar.Bldg),
                            ["z0"] = sc.F32(ar.Z0), ["h"] = sc.F32(ar.H), ["hit"] = sc.U8(ar.Hit),
                            // ★[v111.7.1 · JACK 1102] 층 풍선 글 끝에 자료 한계 한 줄(철거된 건물이 남아 있을 수 있음)
                            ["note"] = $"건물 {pr.Items.Count:N0}동 · 바뀌는 자리에 닿음 {pr.Touch:N0}동(빨강) · 뺌: {pr.DropText} · 높이 = 지상층수 × {Web3DBuildings.FloorHeight} m\n{BldgNote}",
                        };
                    }
                }
            }
            catch (System.Exception bex) { why = "건물을 넣지 못했습니다(진단 로그 참고)"; detail = bex.ToString(); }
            if (why.Length > 0)
            {
                c.Warn.Add(why + " — 3D에 건물 없음");
                log.AppendLine("  건물: 못 받음 — " + why + (detail.Length > 0 ? " · " + VWorldBuildings.Scrub(detail, key) : ""));
                bldgHead = new Dictionary<string, object> { ["missing"] = why };
            }
        }
        Lap("건물", "수목 심는 중");

        // ④f ★★[v115 · JACK 1105 임상도 · 계획 2판 5절] 수목 — 임상도 숲 폴리곤 안 추정 배치(설계 자리 · 건물 + 1 m · 급경사 · 면 없음은 뺌) ·
        //   창고 = [기타 설정] 임상도 타일 폴더(기본 바탕화면\임상도_타일) → 도면 폴더 사본 → 없으면 층만 회색 · 꺼짐이면 열쇠를 안 넣는다(옛 장면 바이트 그대로)
        //   화면 없는 시험(skipSat)에서도 폴더 창고면 돈다
        object? forestHead = null;
        try
        {
            var ft = new Web3DForest.Terrain(inp.X, inp.Y, inp.F, tm.TriCls, mnx, mny, mxx, mxy, tm.CMinX, tm.CMinY, tm.CMaxX, tm.CMaxY);
            forestHead = Web3DForest.Build(c, ft, new[] { inp.Plan, inp.Bench, inp.SlopeCut, inp.SlopeFill, inp.WallMask, inp.WallFoot, inp.RoadArea }, bldgItems, SurfZ, ox, oy, sc, log);
        }
        catch (System.Exception fex) { log.AppendLine("  수목: 실패 — " + fex); forestHead = new Dictionary<string, object> { ["missing"] = "수목을 넣지 못했습니다(진단 로그 참고)" }; }
        Lap("수목", skipSat ? null : "위성사진 받는 중");

        // ⑤ 위성 — 자세히(바뀐 자리 + 50 m · 0.3 m/px) + 바탕(지형 전체 + 30 m)
        object? satHead = null;
        if (skipSat) log.AppendLine("  위성: 시험 — 건너뜀");
        else
        try   // ★[설치 전 검토 낮음] 위성에서 무슨 일이 나도 3D는 지형 색으로 뜬다
        {
            string cache = System.IO.Path.Combine(Dir, "sat");
            var sat = new Dictionary<string, object?>();
            double bw = mxx - mnx + 60, bh = mxy - mny + 60;
            // ★[JACK 1027 «전체적인 해상도를 높였으면»] 바탕 0.5 m/px 안팎(한 변 8192 · 36 MP 한도 — 넓으면 자동으로 낮춤) · 자세히 0.25 m/px · 바뀐 자리 + 150 m
            var b = VWorldImagery.Texture(mnx - 30, mny - 30, mxx + 30, mxy + 30, System.Math.Max(0.5, System.Math.Max(bw, bh) / 8192), c.BeltCm, c.BeltFn, cache,
                                          maxPixels: 36_000_000, maxSide: 8192, maxTiles: 900, quality: 82);
            log.AppendLine("  위성 바탕: " + b.Msg);
            if (b.Jpeg != null) sat["base"] = new Dictionary<string, object> { ["img"] = sc.Img(b.Jpeg, "image/jpeg"), ["ext"] = Ext(b.MinE, b.MaxN, b.W, b.H, b.Res, ox, oy), ["res"] = b.Res };
            bool offline = b.Jpeg == null && b.Msg.StartsWith("위성 첫 타일", System.StringComparison.Ordinal);   // 인터넷이 막혔으면 자세히는 또 기다리지 않는다
            if (double.IsFinite(tm.CMinX) && !offline)
            {
                var d = VWorldImagery.Texture(tm.CMinX - 150, tm.CMinY - 150, tm.CMaxX + 150, tm.CMaxY + 150, 0.25, c.BeltCm, c.BeltFn, cache,
                                              maxPixels: 30_000_000, maxSide: 8192, maxTiles: 900, quality: 86);
                log.AppendLine("  위성 자세히: " + d.Msg);
                if (d.Jpeg != null) sat["detail"] = new Dictionary<string, object> { ["img"] = sc.Img(d.Jpeg, "image/jpeg"), ["ext"] = Ext(d.MinE, d.MaxN, d.W, d.H, d.Res, ox, oy), ["res"] = d.Res };
                else if (b.Jpeg != null) c.Warn.Add("위성 자세히 그림을 못 받아 흐린 바탕만 씁니다 — " + d.Msg);
            }
            if (b.Jpeg == null && !sat.ContainsKey("detail")) c.Warn.Add("위성을 못 받았습니다(지형 색만) — " + b.Msg);
            if (sat.Count > 0) satHead = sat;
        }
        catch (System.Exception sex) { c.Warn.Add("위성을 못 넣었습니다(지형 색만) — " + sex.Message); log.AppendLine("  위성: 실패 — " + sex); }
        Lap("위성", "3D 파일 만드는 중");

        // ⑥ 머리
        var H = sc.Head;
        H["app"] = "DH.Grading " + GradingSettings.Version;
        H["made"] = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm");
        // ★[보안 검토 중간 1] 페이지는 웹으로 나간다 — 도면 전체 경로(사용자 · 프로젝트 폴더)는 넣지 않고 지문만(사진기 자리 기억 열쇠)
        H["dwg"] = c.Dwg; H["dwgKey"] = System.Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(c.DwgKey)))[..16]; H["epsg"] = c.Epsg;
        H["origin"] = new[] { ox, oy };
        H["bbox"] = new[] { Web3DScene.Num(mnx - ox), Web3DScene.Num(mny - oy), Web3DScene.Num(mnz), Web3DScene.Num(mxx - ox), Web3DScene.Num(mxy - oy), Web3DScene.Num(mxz) };
        // 처음 보기 = 바뀐 자리(+20 m) — 지형 전체(서버 원지반 수 km)로 열면 현장이 점이 된다
        if (double.IsFinite(tm.CMinX))
        {
            double fx0 = tm.CMinX - 20, fy0 = tm.CMinY - 20, fx1 = tm.CMaxX + 20, fy1 = tm.CMaxY + 20, fz0 = double.MaxValue, fz1 = double.MinValue;
            foreach (var p in pts) if (p.x >= fx0 && p.x <= fx1 && p.y >= fy0 && p.y <= fy1) { fz0 = System.Math.Min(fz0, p.z); fz1 = System.Math.Max(fz1, p.z); }
            if (fz0 <= fz1) H["focus"] = new[] { Web3DScene.Num(fx0 - ox), Web3DScene.Num(fy0 - oy), Web3DScene.Num(fz0), Web3DScene.Num(fx1 - ox), Web3DScene.Num(fy1 - oy), Web3DScene.Num(fz1) };
        }
        H["terrain"] = new Dictionary<string, object?>
        {
            ["pos"] = sc.F32(tm.Pos), ["idx"] = sc.U32(tm.Idx), ["cf"] = c.Ground == null ? null : sc.F32(tm.Cf),
            ["ranges"] = tm.Ranges.Select(r => new[] { (int)r.Cls, r.Start, r.Count }).ToList(),
            ["classes"] = TerrainMesh.Keys.Select((k, i) => new Dictionary<string, object> { ["key"] = k, ["name"] = TerrainMesh.Names[i] }).ToList(),
        };
        H["sat"] = satHead;
        H["drain"] = drainHead; H["cad"] = cadHead; H["align"] = alignHead; H["build"] = buildHead; H["bldg"] = bldgHead;
        // 해 그림자 — 원점의 위경도(도면 좌표계 벨트)
        try { var ll = KoreaTm.ToLonLat(ox, oy, c.BeltCm, c.BeltFn); H["geo"] = new Dictionary<string, object> { ["lat"] = Web3DScene.Num(ll.Lat, 6), ["lon"] = Web3DScene.Num(ll.Lon, 6) }; } catch { }
        H["ground"] = ground.Idx.Length > 0 ? new Dictionary<string, object> { ["pos"] = sc.F32(ground.Pos), ["idx"] = sc.U32(ground.Idx), ["name"] = c.GroundName } : null;
        H["walls"] = walls == null ? new List<object>() : walls.Walls.Select(w => (object)new Dictionary<string, object>
        {
            ["id"] = w.Id, ["name"] = w.Name, ["kind"] = w.Kind, ["up"] = w.Up, ["src"] = w.Src,
            ["len"] = Web3DScene.Num(w.Len, 2), ["hmin"] = Web3DScene.Num(w.HMin, 2), ["hmax"] = Web3DScene.Num(w.HMax, 2),
            ["box"] = w.Box.Select(v => Web3DScene.Num(v, 2)).ToArray(), ["sta"] = sc.F32(w.Sta),
            ["parts"] = w.Parts.Select(p => (object)new Dictionary<string, object> { ["mat"] = p.Mat, ["pos"] = sc.F32(p.Pos), ["idx"] = sc.U32(p.Idx) }).ToList(),
        }).ToList();
        H["patterns"] = walls == null ? new List<object>() : walls.Pats.Select(p => (object)new Dictionary<string, object>
        {
            ["wall"] = p.Wall, ["layer"] = p.Layer, ["color"] = new[] { (int)p.Rgb.R, p.Rgb.G, p.Rgb.B }, ["pos"] = sc.F32(p.Pos), ["idx"] = sc.U32(p.Idx),
        }).ToList();
        H["earth"] = double.IsFinite(c.Cut) && double.IsFinite(c.Fill)
            ? new Dictionary<string, object> { ["cut"] = Web3DScene.Num(c.Cut, 1), ["fill"] = Web3DScene.Num(c.Fill, 1), ["area"] = Web3DScene.Num(c.PlanArea, 1), ["ground"] = c.GroundName }
            : null;
        // ★[v111.8 · M6] 토량은 계획부지만(다음 판) — 진입도로가 있을 때만 표 아래 한 줄 · 차선은 있을 때만(도로 없는 장면은 머리 그대로)
        if (c.HasRoads && H["earth"] is Dictionary<string, object> eh) eh["note"] = "진입도로 제외";
        if (laneHead != null) H["lanes"] = laneHead;
        if (ditchHead != null) H["ditches"] = ditchHead;   // ★[도로 마무리 ②] 측구가 있을 때만
        if (strataHead != null) H["strata"] = strataHead;
        if (roadAlignHead != null) H["roadAlign"] = roadAlignHead;
        if (forestHead != null) H["forest"] = forestHead;   // ★[v115] 수목(꺼짐이면 열쇠 없음)
        if (c.Ground != null && c.GroundOfName.Length > 0 && c.GroundOfName != c.GroundName)
            c.Warn.Add($"원지반이 둘입니다 — 토공량 · 절성토 색은 «{c.GroundName}», 옹벽 변환 자리 · 단면은 «{c.GroundOfName}»");
        H["warn"] = c.Warn.Select(NoPath).Distinct().ToList();

        // ⑦ 묶음 · 페이지 — 50 MB 넘으면 ⚠를 페이지에도(머리를 고쳐 다시 묶는다)
        var gz = sc.PackGzip();
        if (gz.LongLength * 4 / 3 > 50L * 1024 * 1024)
        {
            c.Warn.Add($"3D 파일이 큽니다(약 {gz.LongLength * 4 / 3 / 1048576.0:F0} MB) — 메일로 보내기 어려울 수 있음");
            H["warn"] = c.Warn.Select(NoPath).Distinct().ToList();
            gz = sc.PackGzip();
        }
        Lap("묶음");
        string viewDir = System.IO.Path.Combine(Dir, "view");
        System.IO.Directory.CreateDirectory(viewDir);
        foreach (var old in new System.IO.DirectoryInfo(viewDir).GetFiles("3d_*.html").OrderByDescending(f => f.LastWriteTimeUtc).Skip(3))
            try { old.Delete(); } catch { }
        string path = System.IO.Path.Combine(viewDir, $"3d_{System.DateTime.Now:yyyyMMdd_HHmmss_fff}.html");
        using (var w = new System.IO.StreamWriter(path, false, new System.Text.UTF8Encoding(false)))
            Web3DScene.WritePage(w, Resource("DH.Grading.Web3D.page.html"), Resource("DH.Grading.Web3D.viewer.js"), $"3D 보기 — {c.Dwg}", gz);
        long bytes = new System.IO.FileInfo(path).Length;
        Lap("페이지");
        LastPath = path; LastDwg = c.Dwg; LastDwgKey = c.DwgKey; LastBytes = bytes;
        log.AppendLine($"  장면: 묶음 {sc.BlobBytes / 1048576.0:F1} MB → gzip {gz.Length / 1048576.0:F1} MB → 페이지 {bytes / 1048576.0:F1} MB · {string.Join(" · ", times)}");
        return path;
    }

    /// <summary>★★[JACK 1103] 지층 격자 — 지층면(<c>DH_지층_n_이름</c> = 그 층 상단 · 번호 차례) · 지하수위를 측점 가로선 범위 + 가로선 폭(30 m 이상) 안 격자로 읽는다(지층면 범위와 겹친 곳만).
    /// 색 = 2D 횡단 · 종단과 같은 색표(<see cref="Commands.XsecViewCommand.StrataAciOf"/>). 도면은 읽기만. 지층면이 없으면 null.</summary>
    static Dictionary<string, object>? Strata(Database db, double[] box, double ox, double oy, Web3DScene sc, System.Text.StringBuilder log)
    {
        var cdoc = Autodesk.Civil.ApplicationServices.CivilApplication.ActiveDocument;
        var found = new List<(int Ord, string Name, CachedGroundSurface S)>();
        CachedGroundSurface? water = null;
        using (var tr = db.TransactionManager.StartTransaction())
        {
            foreach (ObjectId sid in cdoc.GetSurfaceIds())
            {
                if (tr.GetObject(sid, OpenMode.ForRead) is not Autodesk.Civil.DatabaseServices.TinSurface ts) continue;
                string nm = ts.Name ?? "";
                if (nm == StrataDraw.WaterSurfName) water = new CachedGroundSurface(ts);
                else if (nm.StartsWith(StrataDraw.SurfPrefix, System.StringComparison.Ordinal)) found.Add((Commands.ProfileCommand.StrataOrdOf(nm), StrataDraw.ShortName(nm), new CachedGroundSurface(ts)));
            }
            tr.Commit();
        }
        if (found.Count == 0) return null;
        // 범위 = 측점 가로선 + 가로선 폭(자르기가 마주 보는 화면 폭 ±0.75폭을 넉넉히 덮음 · 30 m 이상) ∩ 지층면 범위 — 그 밖 자른 면은 종전 흙 빗금
        double m = System.Math.Max(30, System.Math.Max(5, GradingSettings.XsecLeft) + System.Math.Max(5, GradingSettings.XsecRight));
        double sx0 = double.MaxValue, sy0 = double.MaxValue, sx1 = double.MinValue, sy1 = double.MinValue;
        foreach (var f in found) { var b = f.S.XYBounds; sx0 = System.Math.Min(sx0, b.MinX); sy0 = System.Math.Min(sy0, b.MinY); sx1 = System.Math.Max(sx1, b.MaxX); sy1 = System.Math.Max(sy1, b.MaxY); }
        var g = Web3DStrata.Pick(System.Math.Max(box[0] - m, sx0), System.Math.Max(box[1] - m, sy0), System.Math.Min(box[2] + m, sx1), System.Math.Min(box[3] + m, sy1));
        if (g == null) { log.AppendLine($"  지층: {found.Count}층 — 측점 범위 밖(안 실음)"); return null; }
        var layers = found.OrderBy(f => f.Ord).ThenBy(f => f.Name, System.StringComparer.Ordinal).Select(f =>
        {
            var col = Autodesk.AutoCAD.Colors.Color.FromColorIndex(Autodesk.AutoCAD.Colors.ColorMethod.ByAci, Commands.XsecViewCommand.StrataAciOf(f.Ord)).ColorValue;
            var s = f.S;
            return new Web3DStrata.Layer(f.Name, f.Ord, col.R, col.G, col.B, (x, y) => s.TryGetElevation(x, y, out double z) ? z : null);
        }).ToList();
        var h = Web3DStrata.Head(sc, g.Value, layers, water == null ? null : (x, y) => water.TryGetElevation(x, y, out double z) ? z : null, ox, oy, out string text);
        log.AppendLine("  지층: " + text);
        return h;
    }

    /// <summary>★★[JACK 1103] 진입도로 측점 — 진입도로 설계 기록이 있는 노선마다(먼저 만든 노선 먼저) 종단도 검토선 그룹(<see cref="RoadXsec.SheetGroup"/> — 횡단도와 같은 측점 · 같은 이름)을
    /// <see cref="Web3DRoadStations.Head"/>로. 가로선 폭 = 검토선 꼭짓점의 옆거리(못 재면 포장 끝 + 5 m). 도면은 읽기만. 진입도로가 없으면 null(열쇠를 안 넣는다).</summary>
    static List<object>? RoadStations(Database db, Func<double, double, double?> z, double ox, double oy, Web3DScene sc, System.Text.StringBuilder log, List<string> warn)
    {
        var cdoc = Autodesk.Civil.ApplicationServices.CivilApplication.ActiveDocument;
        var heads = new List<object>();
        var texts = new List<string>();
        int roads = 0;
        using var tr = db.TransactionManager.StartTransaction();
        var ids = new List<ObjectId>();
        foreach (ObjectId aid in cdoc.GetAlignmentIds())
            try { if (!aid.IsErased && RoadDesign.Has(tr, aid)) ids.Add(aid); } catch { }
        ids.Sort((a, b) => a.Handle.Value.CompareTo(b.Handle.Value));
        foreach (var aid in ids)
        {
            var d = RoadDesign.Read(tr, aid);
            if (d == null || tr.GetObject(aid, OpenMode.ForRead) is not Autodesk.Civil.DatabaseServices.Alignment al) continue;
            roads++;
            string name = al.Name ?? "";
            var (gid, gname) = RoadXsec.SheetGroup(tr, db, d);
            if (gid.IsNull) { warn.Add($"진입도로 «{name}» 측점 없음 — 진입도로 [종단도]를 그리면 3D에 측점이 나옵니다"); texts.Add($"«{name}» 종단도 없음"); continue; }
            double index = al.StationIndexIncrement > 1e-6 ? al.StationIndexIncrement : 20.0;
            // 측점마다 횡단 뷰 수 — 진입도로 횡단도는 따로 묶음(«{노선}-횡단_n»)에 뷰를 단다 → 이 노선 모든 묶음의 샘플 선에서 같은 측점(1 cm)의 뷰를 셈
            var viewsAt = new Dictionary<long, int>();
            foreach (ObjectId vg in al.GetSampleLineGroupIds())
                try
                {
                    if (tr.GetObject(vg, OpenMode.ForRead) is not Autodesk.Civil.DatabaseServices.SampleLineGroup sg) continue;
                    foreach (ObjectId vs in sg.GetSampleLineIds())
                        if (tr.GetObject(vs, OpenMode.ForRead) is Autodesk.Civil.DatabaseServices.SampleLine vl)
                        {
                            int nv = vl.GetSectionViewIds().Count;
                            if (nv > 0) { long k = (long)System.Math.Round(vl.Station * 100); viewsAt[k] = viewsAt.GetValueOrDefault(k) + nv; }
                        }
                }
                catch { }
            var marks = new List<Web3DRoadStations.Mark>();
            var offs = new List<double>();
            // ★[도로 마무리 ① · 계획 5.8] 도로 구간(잘린 끝 · 빈 구간 뺌) — 그 밖 측점은 3D에 안 그림 · 끝 맞춤 꺼진 도로 = 노선 ∩ 종단(측점은 지금처럼 노선 전체)
            var rg = d.EndsOn ? RoadRanges.Of(tr, aid, d) : new List<(double A, double B)>();
            var g = (Autodesk.Civil.DatabaseServices.SampleLineGroup)tr.GetObject(gid, OpenMode.ForRead);
            foreach (ObjectId sid in g.GetSampleLineIds())
            {
                try
                {
                    if (tr.GetObject(sid, OpenMode.ForRead) is not Autodesk.Civil.DatabaseServices.SampleLine sl) continue;
                    double st = sl.Station;
                    if (rg.Count > 0 && !RoadRanges.Inside(rg, st)) continue;
                    marks.Add(new Web3DRoadStations.Mark(st, RoadXsecRules.LabelOf(sl.Name, gname, StationNaming.Fmt(st, index)), viewsAt.GetValueOrDefault((long)System.Math.Round(st * 100))));
                    foreach (Autodesk.Civil.DatabaseServices.SampleLineVertex v in sl.Vertices)
                        try { double s = 0, o = 0; al.StationOffset(v.Location.X, v.Location.Y, ref s, ref o); offs.Add(o); } catch { }
                }
                catch { }
            }
            var (wl, wr) = Web3DRoadStations.Reach(offs);
            if (wl < 1 || wr < 1) { var sec = d.Section(); var w = ProfileSheetRules.RoadWidth(sec.OuterL, sec.OuterR, 0, 0); if (wl < 1) wl = w.L; if (wr < 1) wr = w.R; }
            (double X, double Y)? At(double s, double o)
            {
                try { double x = 0, y = 0; al.PointLocation(s, o, ref x, ref y); return double.IsFinite(x) && double.IsFinite(y) ? (x, y) : null; }
                catch { return null; }
            }
            var h = Web3DRoadStations.Head(sc, name, rg.Count > 0 ? rg[0].A : al.StartingStation, rg.Count > 0 ? rg[^1].B : al.EndingStation, index, wl, wr, marks, At, z, ox, oy, out int skipped);
            if (skipped > 0) warn.Add($"진입도로 «{name}» 노선 밖 측점 {skipped}개는 3D에 안 그렸습니다(노선을 고친 뒤 남은 검토선)");
            texts.Add($"«{name}» 측점 {marks.Count - skipped} · 폭 좌 {wl:0.#} / 우 {wr:0.#} m");
            if (h != null) heads.Add(h);
        }
        tr.Commit();
        if (roads == 0) return null;
        log.AppendLine("  진입도로 측점: " + string.Join(" · ", texts));
        return heads.Count > 0 ? heads : null;
    }

    /// <summary>그림 범위(원점 뺀 minx, miny, maxx, maxy) — 돌려받은 W·res로.</summary>
    static double[] Ext(double minE, double maxN, int w, int h, double res, double ox, double oy)
        => new[] { minE - ox, maxN - h * res - oy, minE + w * res - ox, maxN - oy };

    /// <summary>★[설치 전 검토 낮음] 페이지 경고 글에서 PC 경로(C:\Users\…)를 뺀다 — 페이지는 NAS로 공개된다 · 전체 예외는 진단 로그에만.</summary>
    static readonly System.Text.RegularExpressions.Regex PathRx = new(@"(?:[A-Za-z]:\\|\\\\)[^\s'""«»·—]*", System.Text.RegularExpressions.RegexOptions.Compiled);
    internal static string NoPath(string s) => PathRx.Replace(s ?? "", "(PC 경로)");
}
