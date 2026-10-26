using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using DH.Grading.Core;

namespace DH.Grading.Civil;

/// <summary>★★★[v105 · 계획_v105.md 4 · JACK 1022 «3D 모양 콤보는 없애고 단면 형상을 이용해서 옹벽을 만들고 해당 객체를 인프라웍스 내보내기에 활용» ·
/// «터파기와 같은 단면» · «내보내기 파일에만»] <b>옹벽 단면 덩어리(옹벽3D.dwg)</b>.
/// <para>단면 줄(<see cref="WallExcav.SecLine"/> — 터파기 · 수량과 같은 바닥 줄 · 점마다 같은 표 줄 단면)에서 구조물 조각마다 솔리드를 세운다:
/// 점 i의 링 (X, Z) → 월드 (P_i + n·(X − 앞 내밀기), 앞 땅_i + Z) · n = 그 변의 흙 쪽 법선.</para>
/// <list type="bullet">
/// <item>곧은 자리(꺾임 1° 미만) · 같은 표 줄 · 같은 링 꼴이면 여러 점을 한 덩어리로(링을 차례로 잇는 메시 → 솔리드).</item>
/// <item>표 줄이 바뀌는 변: 가운데에서 끊고 앞 단면으로 끝 · 뒤 단면으로 시작(계단 — <see cref="WallExcav.ColumnAt"/>의 t &lt; 0.5 규칙과 같은 자리 · 계획 검토 중간 4).</item>
/// <item>꺾인 점: 변마다 따로 세우고, 벌어지는 쪽(바깥 모서리)만 그 점 단면의 반쪽(흙 쪽 X ≥ 0 또는 앞쪽 X ≤ 0)을 세로축으로 돌려 메운다(안쪽은 겹칠 뿐 — 계획 검토 중간 5: 꺾인 점 법선으로 비틀면 안쪽 모서리에서 면이 꼬인다).</item>
/// <item>몫이 1보다 작은 조각(부벽 평균)과 비구조물(보강토체 · 배수층 · 조약돌 · 패널 뒤 되메움)은 안 만든다.</item>
/// <item>만들기: 원점 근처 메시 → 도면에 올린 뒤 솔리드로(<see cref="WallPanelDwg"/>의 교훈 — 올리기 전에는 변환이 안 된다 · 면 감기는 부피 부호로 맞춘다) → 제자리로 옮김(대좌표 ACIS 오류 피함). 솔리드가 안 되면 메시로 남기고 센다.</item>
/// </list></summary>
internal static class WallSolidDwg
{
    /// <summary>앞면을 공기 쪽으로 내미는 거리(m) — 지형의 옹벽 면과 겹쳐 깜빡이지 않게(3D 모양만 · 수량과 무관).</summary>
    const double FrontOut = 0.02;
    /// <summary>한 덩어리로 잇는 꺾임 한계(도).</summary>
    const double StraightDeg = 1.0;

    internal sealed class Stats
    {
        public int Lines, Solids, Meshes, Fails, Corners, CornerFails;
        /// <summary>★[설치 전 검토 낮음] 까닭은 계수기마다 따로 — 실패 · 모서리 메움 · 솔리드 변환(메시로 남김).</summary>
        public string FirstFail = "", CornerFirst = "", ConvFirst = "";
        /// <summary>파일을 썼나(아무것도 못 만들면 안 쓴다 — JACK 0724 «빈 파일 안 만듦»).</summary>
        public bool Saved;
        public readonly Dictionary<string, int> ByLayer = new();
        public string Text => $"단면 줄 {Lines} · 솔리드 {Solids} · 메시 {Meshes}(솔리드 변환 안 됨{(ConvFirst.Length > 0 ? " — " + ConvFirst : "")}) · 모서리 메움 {Corners}"
                              + (CornerFails > 0 ? $"(⚠못 함 {CornerFails} — {CornerFirst})" : "") + (Fails > 0 ? $" · ⚠실패 {Fails} — 첫 사유: {FirstFail}" : "")
                              + (ByLayer.Count > 0 ? " · " + string.Join(" · ", ByLayer.OrderBy(k => k.Key).Select(k => $"{k.Key} {k.Value}")) : "");
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

    /// <summary>단면 줄들을 새 DWG로 저장한다(구조물 조각 → 솔리드). 줄이 없으면 파일을 안 만든다(부르는 쪽이 거른다).</summary>
    internal static Stats Export(string path, IReadOnlyList<WallExcav.SecLine> lines)
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
                ObjectId Layer(string mat)
                {
                    var (nm, col) = LayerOf(mat);
                    if (layers.TryGetValue(nm, out var id)) return id;
                    var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
                    if (lt.Has(nm)) id = lt[nm];
                    else
                    {
                        lt.UpgradeOpen();
                        var rec = new LayerTableRecord { Name = nm, Color = Color.FromColorIndex(ColorMethod.ByAci, col) };
                        id = lt.Add(rec); tr.AddNewlyCreatedDBObject(rec, true);
                    }
                    layers[nm] = id;
                    return id;
                }
                foreach (var ln in lines)
                {
                    if (ln?.Pts == null || ln.Pts.Count < 2) continue;
                    st.Lines++;
                    try { Populate(tr, ms, ln, Layer, st); }
                    catch (System.Exception ex) { st.Fails++; if (st.FirstFail.Length == 0) st.FirstFail = $"{ln.Src}: {ex.Message}"; }
                }
                tr.Commit();
            }
            if (st.Solids + st.Meshes + st.Corners > 0) { db.SaveAs(path, DwgVersion.Current); st.Saved = true; }
        }
        finally { HostApplicationServices.WorkingDatabase = prev; }
        return st;
    }

    /// <summary>조각 링 — 구조물 · 온 몫만(재료 · 링).</summary>
    static List<WallSection.Part> Solids(WallSection.Shape sh) => sh.Parts.Where(p => p.Structure && p.Fraction >= 1 - 1e-12 && p.Ring.Count >= 3).ToList();

    /// <summary>두 단면을 한 덩어리로 이을 수 있나 — 같은 표 줄 · 조각 수 · 재료 · 꼭짓점 수가 같다.</summary>
    static bool Same(WallExcav.SecPoint a, WallExcav.SecPoint b)
    {
        if (a.Row != b.Row) return false;
        var pa = Solids(a.Shape); var pb = Solids(b.Shape);
        if (pa.Count != pb.Count) return false;
        for (int i = 0; i < pa.Count; i++) if (pa[i].Material != pb[i].Material || pa[i].Ring.Count != pb[i].Ring.Count) return false;
        return true;
    }

    /// <summary>한 정거장 — 자리(x, y) · 앞 땅 · 흙 쪽 법선 · 단면.</summary>
    readonly record struct Sta(double X, double Y, double Toe, double Nx, double Ny, WallSection.Shape Shape, double Scale = 1.0);

    /// <summary>단면의 흙 쪽 · 앞쪽 가장 먼 폭(앞 내밀기 반영) — 꺾인 점에서 단면이 서로 넘어가는지 가른다.</summary>
    static double Reach(WallSection.Shape sh) => Solids(sh).SelectMany(p => p.Ring).Select(q => Math.Abs(q.X - FrontOut)).DefaultIfEmpty(0).Max();

    static void Populate(Transaction tr, BlockTableRecord ms, WallExcav.SecLine ln, Func<string, ObjectId> layer, Stats st)
    {
        var P = ln.Pts;
        int n = P.Count;
        // 변마다 흙 쪽 법선(왼 법선 × Soil — ColumnAt의 부호 있는 거리와 같은 정의)
        var nx = new double[n - 1]; var ny = new double[n - 1]; var len = new double[n - 1];
        for (int k = 0; k + 1 < n; k++)
        {
            double ex = P[k + 1].X - P[k].X, ey = P[k + 1].Y - P[k].Y, L = Math.Sqrt(ex * ex + ey * ey);
            len[k] = L;
            if (L < 1e-9) { nx[k] = double.NaN; continue; }
            nx[k] = -ey / L * ln.Soil; ny[k] = ex / L * ln.Soil;
        }
        // 덩어리 — 정거장 목록을 모았다가 끊을 때 내보낸다
        var run = new List<Sta>();
        void Flush()
        {
            if (run.Count >= 2) Loft(tr, ms, run, layer, st);
            run.Clear();
        }
        // ★[설치 전 검토 낮음 · 중간] 꺾인 점: 15° 넘거나 단면 폭 × 각이 이웃 변 길이의 반을 넘으면(이어 붙이면 안쪽 점이 서로 넘어가 면이 꼬인다) 끊고 모서리 메움 ·
        //   그 밖(완만한 곡선)은 이어 붙이되 그 점의 법선을 두 변의 이등분으로 · 폭은 1/cos(반각)(평행 오프셋 — 곡선마다 덩어리가 수천 개로 쪼개지지 않게)
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
                if (IsCorner(lastK, k, a.Shape, out double ang))
                {
                    Flush();
                    Corner(tr, ms, a, nx[lastK], ny[lastK], nx[k], ny[k], ang, layer, st);
                }
                else if (run.Count > 0 && Math.Abs(run[^1].X - a.X) < 1e-9 && Math.Abs(run[^1].Y - a.Y) < 1e-9)
                {
                    // 이어 붙임 — 그 점 정거장의 법선을 이등분으로 · 폭 늘임
                    double bx = nx[lastK] + nx[k], by = ny[lastK] + ny[k], bl = Math.Sqrt(bx * bx + by * by);
                    if (bl > 1e-9)
                    {
                        double half = Math.Abs(ang) / 2;
                        run[^1] = run[^1] with { Nx = bx / bl, Ny = by / bl, Scale = Math.Min(1.1, 1 / Math.Cos(half)) };
                    }
                }
            }
            if (Same(a, b))
            {
                if (run.Count == 0) run.Add(new Sta(a.X, a.Y, a.Toe, nx[k], ny[k], a.Shape));
                run.Add(new Sta(b.X, b.Y, b.Toe, nx[k], ny[k], b.Shape));
            }
            else
            {
                // 표 줄이 바뀐다 — 가운데에서 끊는다: 앞 단면으로 가운데까지 · 뒤 단면으로 가운데부터
                double mx = (a.X + b.X) / 2, my = (a.Y + b.Y) / 2, mt = (a.Toe + b.Toe) / 2;
                if (run.Count == 0) run.Add(new Sta(a.X, a.Y, a.Toe, nx[k], ny[k], a.Shape));
                run.Add(new Sta(mx, my, mt, nx[k], ny[k], a.Shape));
                Flush();
                run.Add(new Sta(mx, my, mt, nx[k], ny[k], b.Shape));
                run.Add(new Sta(b.X, b.Y, b.Toe, nx[k], ny[k], b.Shape));
            }
            lastK = k;
        }
        Flush();
        // ★[설치 전 검토 낮음] 닫힌 고리(한 바퀴 옹벽 — 첫 점 = 끝 점) 이음매도 꺾인 점이면 메운다
        if (firstK >= 0 && lastK > firstK && Math.Abs(P[0].X - P[n - 1].X) < 1e-6 && Math.Abs(P[0].Y - P[n - 1].Y) < 1e-6)
        {
            double cr = nx[lastK] * ny[firstK] - ny[lastK] * nx[firstK], dt = nx[lastK] * nx[firstK] + ny[lastK] * ny[firstK];
            double ang = Math.Atan2(cr, dt);
            if (Math.Abs(ang) * 180 / Math.PI >= StraightDeg * 0.25) Corner(tr, ms, P[0], nx[lastK], ny[lastK], nx[firstK], ny[firstK], ang, layer, st);
        }
    }

    /// <summary>정거장들을 잇는 덩어리 — 조각마다 메시 → 솔리드.</summary>
    static void Loft(Transaction tr, BlockTableRecord ms, List<Sta> run, Func<string, ObjectId> layer, Stats st)
    {
        var parts0 = Solids(run[0].Shape);
        for (int pi = 0; pi < parts0.Count; pi++)
        {
            string mat = parts0[pi].Material;
            int m = parts0[pi].Ring.Count;
            // 원점 = 첫 정거장(앞 땅) — 원점 근처에서 짓고 옮긴다
            double ox = run[0].X, oy = run[0].Y, oz = run[0].Toe;
            var verts = new Point3dCollection();
            bool ok = true;
            foreach (var s in run)
            {
                var ps = Solids(s.Shape);
                if (pi >= ps.Count || ps[pi].Ring.Count != m) { ok = false; break; }
                foreach (var q in ps[pi].Ring)
                {
                    double x = (q.X - FrontOut) * s.Scale;
                    verts.Add(new Point3d(s.X + s.Nx * x - ox, s.Y + s.Ny * x - oy, s.Toe + q.Z - oz));
                }
            }
            if (!ok) { st.Fails++; if (st.FirstFail.Length == 0) st.FirstFail = $"{mat} 링 꼴이 정거장마다 다름"; continue; }
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

    /// <summary>바깥 모서리 메움 — 꺾인 점 단면의 벌어지는 쪽 반을 세로축(그 점 · 앞 땅)으로 돌린다.</summary>
    static void Corner(Transaction tr, BlockTableRecord ms, WallExcav.SecPoint p, double n1x, double n1y, double n2x, double n2y, double ang,
                       Func<string, ObjectId> layer, Stats st)
    {
        // 법선이 왼쪽으로 돌면(ang > 0) 흙 쪽이 바깥인지 앞쪽이 바깥인지: 법선 n1 → n2가 ang만큼 돈다 —
        //   흙 쪽(X > 0) 점은 n 방향을 따라 돌므로 벌어지고(틈), 앞쪽(X < 0) 점은 반대로 모인다(겹침). 돌리는 쪽 = X ≥ 0 반 · 각 = ang(부호 그대로).
        //   ※ang의 부호와 무관하게 «n1에서 n2로 도는» 회전이 흙 쪽 반의 틈을 메운다 — 앞쪽 반은 반대 꺾임에서 같은 식으로 틈이 된다:
        //     앞쪽 점 x < 0의 자리는 −|x|·n 이라 n이 돌면 반대편으로 쓸린다 → 앞쪽 반은 n1 → n2 회전으로 겹침 쪽을 지나간다(이미 덮인 자리) — 그래서
        //     앞쪽 반의 틈은 «ang 반대 꺾임»에서 생기며, 그때는 역시 n1 → n2 회전이 그 틈을 쓴다. 두 반을 다 돌리면 한쪽은 겹침만 더한다 — 둘 다 돌린다(겹침은 해가 없다).
        foreach (var part in Solids(p.Shape))
        {
            foreach (bool soilSide in new[] { true, false })
            {
                var half = ClipHalf(part.Ring, soilSide);
                if (half.Count < 3 || Math.Abs(WallSection.Area(half)) < 1e-6) continue;
                // ★[설치 전 검토 중간 1] 실패해도 리전 · 솔리드를 이 스레드에서 치운다(ACIS 객체가 GC 스레드에서 지워지면 AutoCAD가 나중에 죽을 수 있다)
                Solid3d? sol = null;
                DBObjectCollection? regs = null;
                try
                {
                    // 원점 근처 — 축 = (0,0)–Z · 링 평면 = n1 방향과 Z가 이루는 평면
                    var pts = new Point3dCollection();
                    foreach (var q in half) pts.Add(new Point3d(n1x * (q.X - FrontOut), n1y * (q.X - FrontOut), q.Z));
                    using var pl = new Polyline3d(Poly3dType.SimplePoly, pts, true);
                    using var curves = new DBObjectCollection { pl };
                    regs = Region.CreateFromCurves(curves);
                    if (regs.Count == 0) throw new InvalidOperationException("반쪽 단면 리전 0");
                    var reg = (Region)regs[0];
                    sol = new Solid3d();
                    // 회전 방향 — n1 → n2(ang > 0이면 +Z 둘레 반시계)
                    sol.CreateRevolvedSolid(reg, Point3d.Origin, ang >= 0 ? Vector3d.ZAxis : -Vector3d.ZAxis, Math.Abs(ang), 0, new RevolveOptions());
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

    /// <summary>볼록 링을 X ≥ FrontOut(흙 쪽 · 앞 내밀기 반영) 또는 그 앞쪽 반으로 자른다(회전축 = 내민 앞면 선).</summary>
    static List<(double X, double Z)> ClipHalf(IReadOnlyList<(double X, double Z)> r, bool soil)
    {
        double cut = FrontOut;   // 링 X에서 FrontOut을 빼 그리므로 축은 X = FrontOut 자리
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
