using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using DH.Grading.Core;

namespace DH.Grading.Civil;

/// <summary>★★★[v105 · 계획_v105.md 4 · JACK 1022 «3D 모양 콤보는 없애고 단면 형상을 이용해서 옹벽을 만들고 해당 객체를 인프라웍스 내보내기에 활용» ·
/// «터파기와 같은 단면» · «내보내기 파일에만»] <b>옹벽 단면 덩어리(옹벽3D.dwg)</b>.
/// <para>★★★[v106 · 계획_v106.md 2판 · JACK 1027 «지금 옹벽 단면으로 스윕 · 밖으로 보이는 표면만 콘크리트는 무늬 · 보강토는 보강토 모양»]
/// 정거장 · 덩어리 · 모서리 메움은 Core <see cref="WallSweep"/>가 정하고(하네스 S174), 여기서는 짓기만 한다 —
/// 몸통 · 모서리 메움 · 무늬가 모두 <see cref="WallSweep.World"/> 하나(앞면 맞춤 전단 · 꼭대기 1 cm 올림)를 거친다(재검토 중간 ①).
/// 보이는 면 무늬는 <see cref="WallFacePattern"/>의 조각을 면 판 틀로 옮겨 솔리드로(직사각형 = 상자 · 그 밖 = 리전 돌출).</para>
/// <list type="bullet">
/// <item>곧은 자리(꺾임 1° 미만) · 같은 표 줄 · 같은 링 꼴이면 여러 점을 한 덩어리로(링을 차례로 잇는 메시 → 솔리드).</item>
/// <item>표 줄이 바뀌는 변: 가운데에서 끊고 앞 단면으로 끝 · 뒤 단면으로 시작(계단 — <see cref="WallExcav.ColumnAt"/>의 t &lt; 0.5 규칙과 같은 자리 · 계획 검토 중간 4).</item>
/// <item>꺾인 점: 변마다 따로 세우고, 그 점 단면(전단 뒤)의 반쪽(흙 쪽 x' ≥ 0 · 앞쪽 x' ≤ 0)을 세로축으로 돌려 메운다.</item>
/// <item>몫이 1보다 작은 조각(부벽 평균)과 비구조물(보강토체 · 배수층 · 조약돌 · 패널 뒤 되메움)은 안 만든다.</item>
/// <item>만들기: 원점 근처 메시 → 도면에 올린 뒤 솔리드로(<see cref="WallPanelDwg"/>의 교훈 — 올리기 전에는 변환이 안 된다 · 면 감기는 부피 부호로 맞춘다) → 제자리로 옮김(대좌표 ACIS 오류 피함). 솔리드가 안 되면 메시로 남기고 센다.</item>
/// </list></summary>
internal static class WallSolidDwg
{
    internal sealed class Stats
    {
        public int Lines, Solids, Meshes, Fails, Corners, CornerFails;
        /// <summary>★[설치 전 검토 낮음] 까닭은 계수기마다 따로 — 실패 · 모서리 메움 · 솔리드 변환(메시로 남김).</summary>
        public string FirstFail = "", CornerFirst = "", ConvFirst = "";
        /// <summary>파일을 썼나(아무것도 못 만들면 안 쓴다 — JACK 0724 «빈 파일 안 만듦»).</summary>
        public bool Saved;
        public readonly Dictionary<string, int> ByLayer = new();
        /// <summary>★[v106] 무늬 — 지은 조각 · 못 지은 조각(첫 까닭) · 무늬 셈(깐 · 그대로 · 잘림 · 나눔 · 버림 · 땅속 · 앵커 뺌 · 자르기 실패) · 판 비틀림 · 옮긴 조각 벗어남 최대.</summary>
        public int Pieces, PieceFails, PatItems, PatWhole, PatCut, PatSplit, PatDropped, PatLow, PatAnchorSkipped, PatClipFail;
        public string PieceFirst = "";
        /// <summary>★[검토 중간 M2] 무늬를 통째로 못 깐 줄(그 줄은 몸통 색만) · 줄 이름.</summary>
        public int PatLineFails; public string PatLineFirst = "";
        public double PatTwistMax, PatDevMax;
        /// <summary>★[v106 · 계획 2판 2.3] 앞면 맞춤 전단 b·h 최대 · 그 자리 · <see cref="WallSweep.LeanWarn"/>(0.30 m) 넘는 정거장 수(팝업 ⚠ — 그 아래는 로그 글만).</summary>
        public double LeanMax; public string LeanAt = ""; public int LeanOver;
        public long MsBody, MsPattern;
        public string Text => $"단면 줄 {Lines} · 솔리드 {Solids} · 메시 {Meshes}(솔리드 변환 안 됨{(ConvFirst.Length > 0 ? " — " + ConvFirst : "")}) · 모서리 메움 {Corners}"
                              + (CornerFails > 0 ? $"(⚠못 함 {CornerFails} — {CornerFirst})" : "") + (Fails > 0 ? $" · ⚠실패 {Fails} — 첫 사유: {FirstFail}" : "")
                              + (ByLayer.Count > 0 ? " · " + string.Join(" · ", ByLayer.OrderBy(k => k.Key).Select(k => $"{k.Key} {k.Value}")) : "");
        public string PatternText => $"무늬 조각 {Pieces}(깐 {PatItems} · 그대로 {PatWhole} · 잘림 {PatCut} · 판 경계로 나눔 {PatSplit} · 작아서 버림 {PatDropped} · 땅속 버림 {PatLow} · 앵커 머리 뺌 {PatAnchorSkipped})"
                                     + (PieceFails + PatClipFail > 0 ? $" · ⚠못 지음 {PieceFails} · 자르기 실패 {PatClipFail}{(PieceFirst.Length > 0 ? " — " + PieceFirst : "")}" : "")
                                     + (PatLineFails > 0 ? $" · ⚠무늬 못 깐 줄 {PatLineFails}({PatLineFirst})" : "")
                                     + $" · 판 비틀림 최대 {PatTwistMax * 100:F1} cm · 옮긴 조각 벗어남 최대 {PatDevMax * 100:F1} cm · 몸통 {MsBody / 1000.0:F1}초 · 무늬 {MsPattern / 1000.0:F1}초";
    }

    /// <summary>레이어 이름 · 색 — 재료마다.</summary>
    static (string Name, short Color) LayerOf(string mat) => mat switch
    {
        "콘크리트" => ("옹벽_콘크리트", 253),
        "버림" => ("옹벽_버림", 8),
        "활동방지벽" => ("옹벽_활동방지벽", 252),
        "블록" => ("옹벽_블록", 41),
        "기초패드" => ("옹벽_기초패드", 8),
        "패널" => ("옹벽_패널", 254),
        "기초" => ("옹벽_기초", 8),
        "찰쌓기" or "메쌓기" => ("옹벽_석축", 33),
        "뒤채움 콘크리트" => ("옹벽_뒤채움콘크리트", 252),
        _ => ("옹벽_" + mat.Replace(" ", ""), 7),
    };

    /// <summary>단면 줄들을 새 DWG로 저장한다(구조물 조각 → 솔리드 · <paramref name="pattern"/>이면 보이는 면 무늬). 줄이 없으면 파일을 안 만든다(부르는 쪽이 거른다).
    /// <paramref name="lean"/> · <paramref name="pattern"/> · <paramref name="lift"/> 모두 거짓이면 v105와 같은 덩어리(회귀 견줌 — 화면 없는 Civil V52).</summary>
    internal static Stats Export(string path, IReadOnlyList<WallExcav.SecLine> lines, bool pattern = true, bool lean = true, bool lift = true)
    {
        var st = new Stats();
        using var db = new Database(true, true);
        Database prev = HostApplicationServices.WorkingDatabase;
        HostApplicationServices.WorkingDatabase = db;   // Solid3d · 메시 변환은 WorkingDatabase 문맥을 요구한다(WallDwg와 같은 법)
        try
        {
            db.Insunits = UnitsValue.Meters;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
                var layers = new Dictionary<string, ObjectId>();
                ObjectId Layer(string nm, Color col)
                {
                    if (layers.TryGetValue(nm, out var id)) return id;
                    var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
                    if (lt.Has(nm)) id = lt[nm];
                    else
                    {
                        lt.UpgradeOpen();
                        var rec = new LayerTableRecord { Name = nm, Color = col };
                        id = lt.Add(rec); tr.AddNewlyCreatedDBObject(rec, true);
                    }
                    layers[nm] = id;
                    return id;
                }
                ObjectId MatLayer(string mat) { var (nm, c) = LayerOf(mat); return Layer(nm, Color.FromColorIndex(ColorMethod.ByAci, c)); }
                ObjectId PatLayer(string nm) { var (r, g, b) = WallFacePattern.ColorOf(nm); return Layer(nm, Color.FromRgb(r, g, b)); }
                var sw = System.Diagnostics.Stopwatch.StartNew();
                foreach (var ln in lines)
                {
                    if (ln?.Pts == null || ln.Pts.Count < 2) continue;
                    st.Lines++;
                    WallSweep.Plan plan;
                    try
                    {
                        sw.Restart();
                        plan = WallSweep.Make(ln, lean);
                        var (lm, la, lo) = WallSweep.LeanReport(plan, ln.Src);
                        st.LeanOver += lo;
                        if (lm > st.LeanMax) { st.LeanMax = lm; st.LeanAt = la; }
                        foreach (var run in plan.Runs) Loft(tr, ms, run, MatLayer, st, lift);
                        foreach (var c in plan.Corners) Corner(tr, ms, c, MatLayer, st, lift);
                        st.MsBody += sw.ElapsedMilliseconds;
                    }
                    catch (System.Exception ex) { st.Fails++; if (st.FirstFail.Length == 0) st.FirstFail = $"{ln.Src}: {ex.Message}"; continue; }
                    // ★[v106] 보이는 면 무늬 — 형식마다(콘크리트 자연석 · 보강토 블록 · 앵커식 패널 + 앵커 머리 · 석축 돌)
                    var spec = WallFacePattern.SpecOf(ln.Kind);
                    if (!pattern || spec == null) continue;
                    sw.Restart();
                    try
                    {
                        var lay = WallFacePattern.Lay(plan, spec);
                        st.PatItems += lay.Items; st.PatWhole += lay.Whole; st.PatCut += lay.Cut; st.PatSplit += lay.Split; st.PatDropped += lay.Dropped;
                        st.PatLow += lay.Low; st.PatAnchorSkipped += lay.AnchorSkipped; st.PatClipFail += lay.ClipFail;
                        st.PatTwistMax = Math.Max(st.PatTwistMax, lay.TwistMax); st.PatDevMax = Math.Max(st.PatDevMax, lay.DevMax);
                        if (lay.FirstFail.Length > 0 && st.PieceFirst.Length == 0) st.PieceFirst = "자르기: " + lay.FirstFail;
                        foreach (var pc in lay.Pieces) Piece(tr, ms, lay.Sheets[pc.Sheet], pc, PatLayer(pc.Layer), st);
                    }
                    catch (System.Exception ex) { st.PatLineFails++; if (st.PatLineFirst.Length == 0) st.PatLineFirst = $"{ln.Src}: {ex.Message}"; }
                    st.MsPattern += sw.ElapsedMilliseconds;
                }
                tr.Commit();
            }
            if (st.Solids + st.Meshes + st.Corners > 0) { db.SaveAs(path, DwgVersion.Current); st.Saved = true; }
        }
        finally { HostApplicationServices.WorkingDatabase = prev; }
        return st;
    }

    /// <summary>정거장들을 잇는 덩어리 — 조각마다 메시 → 솔리드. 링 점은 <see cref="WallSweep.World"/>(전단 · 꼭대기 올림)로.
    /// ★[v107] 링 점은 Core <see cref="WallMesh.LoftRings"/> 하나를 [3D 보기]와 함께 쓴다(같은 점) — 면은 여기서 사각 그대로(ACIS 변환이 지금과 같게 · 계획 검토 중간 4).</summary>
    static void Loft(Transaction tr, BlockTableRecord ms, List<WallSweep.Sta> run, Func<string, ObjectId> layer, Stats st, bool lift)
    {
        var rings = WallMesh.LoftRings(run, lift);
        for (int pi = 0; pi < rings.Count; pi++)
        {
            var (mat, m, V) = rings[pi];
            // 원점 = 첫 정거장(앞 땅) — 원점 근처에서 짓고 옮긴다
            double ox = run[0].X, oy = run[0].Y, oz = run[0].Toe;
            if (V == null) { st.Fails++; if (st.FirstFail.Length == 0) st.FirstFail = $"{mat} 링 꼴이 정거장마다 다름"; continue; }
            var verts = new Point3dCollection();
            foreach (var w in V) verts.Add(new Point3d(w.X - ox, w.Y - oy, w.Z - oz));
            var faces = new List<int[]>();
            int ns = run.Count;
            for (int k = 0; k + 1 < ns; k++)
                for (int j = 0; j < m; j++)
                {
                    int a0 = k * m + j, a1 = k * m + (j + 1) % m, b0 = (k + 1) * m + j, b1 = (k + 1) * m + (j + 1) % m;
                    faces.Add(new[] { a0, a1, b1, b0 });
                }
            faces.Add(Enumerable.Range(0, m).Reverse().ToArray());                     // 시작 마구리
            faces.Add(Enumerable.Range((ns - 1) * m, m).ToArray());                    // 끝 마구리
            Emit(tr, ms, verts, faces, new Vector3d(ox, oy, oz), layer(mat), LayerOf(mat).Name, st);
        }
    }

    /// <summary>바깥 모서리 메움 — 꺾인 점 단면을 <b>먼저 전단 · 꼭대기 올림</b>(덩어리 끝 링과 같은 식)한 뒤 x' = 0(앞면 선 축)에서 반으로 잘라 세로축으로 돌린다.
    /// <para>★[재검토 중간 ①] 순서가 «전단 먼저»여야 한다 — 먼저 자르고 전단하면 자른 선이 기울어 반쪽이 회전축을 넘는다. 회전 0°에서 앞 덩어리 끝 링 · ang에서 뒤 덩어리 시작 링과 같다.</para></summary>
    static void Corner(Transaction tr, BlockTableRecord ms, WallSweep.Corner c, Func<string, ObjectId> layer, Stats st, bool lift)
    {
        var p = c.P;
        // 법선 n1 → n2로 ang만큼 돈다 — 흙 쪽(x' > 0) 반은 벌어지는 꺾임에서, 앞쪽(x' < 0) 반은 반대 꺾임에서 틈을 메운다. 둘 다 돌린다(겹침은 해가 없다 — v105 그대로).
        foreach (var part in WallSweep.Solids(p.Shape))
        {
            // 덩어리와 같은 식 — 꺾인 점 정거장(폭 늘임 1 · 법선 n1)의 월드 점에서 앞 땅 · 점 자리를 뺀 값이 n1·x'
            var sta = new WallSweep.Sta(0, 0, 0, 1, 0, p.Shape, 1.0, c.Lean, -1);
            var ring = part.Ring.Select(q => { var w = WallSweep.World(sta, q.X, q.Z, lift); return (X: w.X, Z: w.Z); }).ToList();
            foreach (bool soilSide in new[] { true, false })
            {
                var half = ClipHalf(ring, soilSide);
                if (half.Count < 3 || Math.Abs(WallSection.Area(half)) < 1e-6) continue;
                // ★[설치 전 검토 중간 1] 실패해도 리전 · 솔리드를 이 스레드에서 치운다(ACIS 객체가 GC 스레드에서 지워지면 AutoCAD가 나중에 죽을 수 있다)
                Solid3d? sol = null;
                DBObjectCollection? regs = null;
                try
                {
                    // 원점 근처 — 축 = (0,0)–Z · 링 평면 = n1 방향과 Z가 이루는 평면
                    var pts = new Point3dCollection();
                    foreach (var q in half) pts.Add(new Point3d(c.N1x * q.X, c.N1y * q.X, q.Z));
                    using var pl = new Polyline3d(Poly3dType.SimplePoly, pts, true);
                    using var curves = new DBObjectCollection { pl };
                    regs = Region.CreateFromCurves(curves);
                    if (regs.Count == 0) throw new InvalidOperationException("반쪽 단면 리전 0");
                    var reg = (Region)regs[0];
                    sol = new Solid3d();
                    // 회전 방향 — n1 → n2(ang > 0이면 +Z 둘레 반시계)
                    sol.CreateRevolvedSolid(reg, Point3d.Origin, c.Ang >= 0 ? Vector3d.ZAxis : -Vector3d.ZAxis, Math.Abs(c.Ang), 0, new RevolveOptions());
                    sol.TransformBy(Matrix3d.Displacement(new Vector3d(p.X, p.Y, p.Toe)));
                    double vol = 0; try { vol = sol.MassProperties.Volume; } catch { }
                    if (!(vol > 1e-9)) continue;                                   // finally가 치운다
                    sol.LayerId = layer(part.Material);
                    ms.AppendEntity(sol); tr.AddNewlyCreatedDBObject(sol, true);
                    sol = null;                                                    // 도면 것 — 치우지 않는다
                    st.Corners++;
                    Count(st, LayerOf(part.Material).Name);
                }
                catch (System.Exception ex) { st.CornerFails++; if (st.CornerFirst.Length == 0) st.CornerFirst = ex.Message; }
                finally
                {
                    sol?.Dispose();
                    if (regs != null) { foreach (DBObject o in regs) o.Dispose(); regs.Dispose(); }
                }
            }
        }
    }

    /// <summary>링(전단 뒤 x')을 x' ≥ 0(흙 쪽) 또는 x' ≤ 0(앞쪽) 반으로 자른다(회전축 = 내민 앞면 선 x' = 0).</summary>
    static List<(double X, double Z)> ClipHalf(IReadOnlyList<(double X, double Z)> r, bool soil)
    {
        const double cut = 0;
        var o = new List<(double X, double Z)>();
        bool In((double X, double Z) q) => soil ? q.X >= cut - 1e-12 : q.X <= cut + 1e-12;
        for (int i = 0; i < r.Count; i++)
        {
            var a = r[i]; var b = r[(i + 1) % r.Count];
            bool ia = In(a), ib = In(b);
            if (ia) o.Add(a);
            if (ia != ib)
            {
                double t = (cut - a.X) / (b.X - a.X);
                o.Add((cut, a.Z + (b.Z - a.Z) * t));
            }
        }
        // 연달아 같은 점 빼기
        var res = new List<(double X, double Z)>();
        foreach (var q in o) if (res.Count == 0 || Math.Abs(res[^1].X - q.X) > 1e-9 || Math.Abs(res[^1].Z - q.Z) > 1e-9) res.Add(q);
        while (res.Count > 1 && Math.Abs(res[0].X - res[^1].X) <= 1e-9 && Math.Abs(res[0].Z - res[^1].Z) <= 1e-9) res.RemoveAt(res.Count - 1);
        return res;
    }

    /// <summary>★[v106] 무늬 조각 하나 — 판 틀 좌표(u, v · w = 공기 쪽)에서 원점 근처로 짓고(직사각형 = 상자 · 그 밖 = 3D 폴리선 → 리전 → w 방향 돌출) 틀 행렬로 옮긴다.
    /// 두께 = 묻힘(몸통 속) + 겉 내밀기 — w ∈ [−묻힘, +내밀기].</summary>
    static void Piece(Transaction tr, BlockTableRecord ms, WallFacePattern.Sheet sh, WallFacePattern.Piece pc, ObjectId lay, Stats st)
    {
        double t = pc.Relief + pc.Embed;
        var frame = Matrix3d.AlignCoordinateSystem(Point3d.Origin, Vector3d.XAxis, Vector3d.YAxis, Vector3d.ZAxis,
            new Point3d(sh.O.X, sh.O.Y, sh.O.Z), new Vector3d(sh.U.X, sh.U.Y, sh.U.Z), new Vector3d(sh.V.X, sh.V.Y, sh.V.Z), new Vector3d(sh.W.X, sh.W.Y, sh.W.Z));
        Solid3d? sol = null;
        DBObjectCollection? regs = null;
        try
        {
            if (pc.Rect)
            {
                double u0 = pc.Poly.Min(q => q.U), u1 = pc.Poly.Max(q => q.U), v0 = pc.Poly.Min(q => q.V), v1 = pc.Poly.Max(q => q.V);
                sol = new Solid3d();
                sol.CreateBox(u1 - u0, v1 - v0, t);
                sol.TransformBy(Matrix3d.Displacement(new Vector3d((u0 + u1) / 2, (v0 + v1) / 2, (pc.Relief - pc.Embed) / 2)));
            }
            else
            {
                var pts = new Point3dCollection();
                foreach (var (u, v) in pc.Poly) pts.Add(new Point3d(u, v, -pc.Embed));
                using var pl = new Polyline3d(Poly3dType.SimplePoly, pts, true);
                using var curves = new DBObjectCollection { pl };
                regs = Region.CreateFromCurves(curves);
                if (regs.Count == 0) throw new InvalidOperationException("조각 리전 0");
                sol = new Solid3d();
                sol.CreateExtrudedSolid((Region)regs[0], new Vector3d(0, 0, t), new SweepOptions());
            }
            sol.TransformBy(frame);
            sol.LayerId = lay;
            ms.AppendEntity(sol); tr.AddNewlyCreatedDBObject(sol, true);
            sol = null;                                                            // 도면 것 — 치우지 않는다
            st.Pieces++;
        }
        catch (System.Exception ex) { st.PieceFails++; if (st.PieceFirst.Length == 0) st.PieceFirst = $"{pc.Layer}: {ex.Message}"; }
        finally
        {
            sol?.Dispose();
            if (regs != null) { foreach (DBObject o in regs) o.Dispose(); regs.Dispose(); }
        }
    }

    static void Count(Stats st, string layerName) => st.ByLayer[layerName] = st.ByLayer.TryGetValue(layerName, out int c) ? c + 1 : 1;

    /// <summary>메시를 올리고 솔리드로 바꾼다 — 퇴화 면(겹친 점)은 접고 · 면 감기는 부피 부호로 맞추고 · 솔리드가 되면 메시는 지운다 · 마지막에 제자리로.</summary>
    static void Emit(Transaction tr, BlockTableRecord ms, Point3dCollection verts, List<int[]> faces0, Vector3d shift, ObjectId lay, string layName, Stats st)
    {
        bool SameV(int a, int b) => verts[a].DistanceTo(verts[b]) < 1e-7;
        var faces = new List<int[]>();
        foreach (var f in faces0)
        {
            var ring = f.ToList();
            for (int i = ring.Count - 1; i >= 0 && ring.Count > 3; i--) if (SameV(ring[i], ring[(i + 1) % ring.Count])) ring.RemoveAt(i);
            if (ring.Count < 3) continue;
            if (ring.Count == 3 && (SameV(ring[0], ring[1]) || SameV(ring[1], ring[2]) || SameV(ring[2], ring[0]))) continue;
            faces.Add(ring.ToArray());
        }
        // 부피 부호(발산 정리 — 면을 부채꼴 세모로) — 음수면 모두 뒤집는다
        double vol = 0;
        foreach (var f in faces)
            for (int i = 1; i + 1 < f.Length; i++)
            {
                var a = verts[f[0]]; var b = verts[f[i]]; var c = verts[f[i + 1]];
                vol += a.X * (b.Y * c.Z - b.Z * c.Y) - a.Y * (b.X * c.Z - b.Z * c.X) + a.Z * (b.X * c.Y - b.Y * c.X);
            }
        if (vol < 0) faces = faces.Select(f => f.Reverse().ToArray()).ToList();
        if (Math.Abs(vol) / 6 < 1e-9) { st.Fails++; if (st.FirstFail.Length == 0) st.FirstFail = $"{layName} 부피 0"; return; }
        var fc = new Int32Collection();
        foreach (var f in faces) { fc.Add(f.Length); foreach (int v in f) fc.Add(v); }
        var mesh = new SubDMesh();
        mesh.SetSubDMesh(verts, fc, 0);
        mesh.LayerId = lay;
        ms.AppendEntity(mesh); tr.AddNewlyCreatedDBObject(mesh, true);
        var move = Matrix3d.Displacement(shift);
        Solid3d? sol = null;
        try
        {
            sol = mesh.ConvertToSolid(false, false);
            bool live;
            try { var _ = sol.GeometricExtents; live = sol.MassProperties.Volume > 1e-9; } catch { live = false; }
            if (live)
            {
                sol.LayerId = lay;
                sol.TransformBy(move);
                ms.AppendEntity(sol); tr.AddNewlyCreatedDBObject(sol, true);
                sol = null;                                                        // 도면 것 — 치우지 않는다
                mesh.Erase();
                st.Solids++;
                Count(st, layName);
                return;
            }
            if (st.ConvFirst.Length == 0) st.ConvFirst = $"{layName} 빈 솔리드";
        }
        catch (System.Exception ex) { if (st.ConvFirst.Length == 0) st.ConvFirst = $"{layName}: {ex.Message}"; }
        finally { sol?.Dispose(); }   // ★[설치 전 검토 중간 1] 도면에 안 올린 솔리드는 이 스레드에서 치운다
        mesh.TransformBy(move);
        st.Meshes++;
        Count(st, layName + "(메시)");
    }
}
