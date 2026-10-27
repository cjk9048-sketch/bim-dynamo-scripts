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

        // ① 정지면 점 · 면(지형.xml과 같은 것)
        if (c.TerrainId.IsNull) throw new System.InvalidOperationException("정지면_DH가 없습니다 — 먼저 부지정지를 하세요");
        List<(double x, double y, double z)> pts; List<(int a, int b, int c)> faces;
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
        try { using var trD = db.TransactionManager.StartTransaction(); designC = new CachedGroundSurface((Autodesk.Civil.DatabaseServices.TinSurface)trD.GetObject(c.TerrainId, OpenMode.ForRead)); trD.Commit(); } catch { }
        double? SurfZ(double x, double y)
        {
            if (designC != null && designC.TryGetElevation(x, y, out double zd)) return zd;
            if (c.Ground != null && c.Ground.TryGetElevation(x, y, out double zg)) return zg;
            return null;
        }
        object? drainHead = null, cadHead = null, alignHead = null, buildHead = null;
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
                buildHead = new Dictionary<string, object> { ["x0"] = Web3DScene.Num(gx0 - ox, 3), ["y0"] = Web3DScene.Num(gy0 - oy, 3), ["cell"] = Web3DScene.Num(gc, 3), ["w"] = gw, ["h"] = gh, ["zf"] = sc.F32(zf), ["zg"] = sc.F32(zg) };
                log.AppendLine($"  시공 격자: {gw}×{gh} · 칸 {gc:0.##} m");
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
            }
        }
        catch (System.Exception aex) { c.Warn.Add("종단 측점을 못 읽었습니다 — " + aex.Message); log.AppendLine("  측점: 실패 — " + aex); }
        Lap("빗물 · 지적 · 측점", skipSat ? null : "위성사진 받는 중");

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
        H["drain"] = drainHead; H["cad"] = cadHead; H["align"] = alignHead; H["build"] = buildHead;
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

    /// <summary>그림 범위(원점 뺀 minx, miny, maxx, maxy) — 돌려받은 W·res로.</summary>
    static double[] Ext(double minE, double maxN, int w, int h, double res, double ox, double oy)
        => new[] { minE - ox, maxN - h * res - oy, minE + w * res - ox, maxN - oy };

    /// <summary>★[설치 전 검토 낮음] 페이지 경고 글에서 PC 경로(C:\Users\…)를 뺀다 — 페이지는 NAS로 공개된다 · 전체 예외는 진단 로그에만.</summary>
    static readonly System.Text.RegularExpressions.Regex PathRx = new(@"(?:[A-Za-z]:\\|\\\\)[^\s'""«»·—]*", System.Text.RegularExpressions.RegexOptions.Compiled);
    internal static string NoPath(string s) => PathRx.Replace(s ?? "", "(PC 경로)");
}
