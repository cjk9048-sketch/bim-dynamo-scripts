using System.Diagnostics;
using System.Globalization;
using NetTopologySuite.Geometries;
using NetTopologySuite.Geometries.Prepared;
using NetTopologySuite.Operation.OverlayNG;
using NetTopologySuite.Operation.Polygonize;

namespace DH.Grading.Core;

/// <summary>★★★[v103.3 · 계획 v103.3 2-3 · 재검토 중간 1] <b>옹벽 터파기면 — 조각들의 아래 껍질</b>.
/// <para>조각 W_k(옹벽 한 단의 «바닥 + 경사 법면» — Civil이 링으로 지은 TIN의 삼각형)마다, 그리고 기준면 L = Lower(정지면 S, 원지반 G)에 대해
/// 높이 = <b>min W_k</b>이고 <b>min W_k &lt; L</b>인 자리만 남긴다 — 굴착 형상만(바닥 + 법면 · 데이라잇에서 끝).</para>
/// <para>합성 엔진(<see cref="BuildComposite"/>)과 같은 법: 모든 삼각형 변 + 높이 차 0 선(W_k∩W_j · W_k∩S · W_k∩G · S∩G, ±ZeroD)을 1 µm 격자로 한 번에 잇고
/// 조각(셀)마다 안쪽 점에서 면을 <b>따로</b> 읽어 고른 면의 평면으로 삼각분할 → 규칙 대조 → 한 점 한 높이 → <see cref="CivilSafeMesh.MakeZone"/>.
/// 합성 코드는 한 줄도 안 건드린다(재검토 중간 1 — 손 안 댄 그물 · 고정 점 · 미리 맞춤 · 이음매는 터파기에 없다).</para>
/// <para>셀에 없는 W_k는 min에서 뺀다(정상) · S와 G가 둘 다 없으면 그 셀은 «모름»(⚠). 다듬기에 고정 점은 없고(세 선이 만나는 모서리의 µm 쌍을 합쳐야 한다 — S167 ②),
/// 다듬은 뒤 데이라잇 테두리가 기준면에 붙어 있는지 잰다.</para>
/// <para>S는 끊김 없는 최종 정지면(정지면_DH)을 넘긴다 — 옹벽면(정지순수옹벽_DH)은 같은 삼각형을 골라 낸 것이라 값이 같고, 발자국 끝에서 기준면이 끊기지 않는다.</para></summary>
public static partial class WallDaylight
{
    public sealed class EnvelopeResult
    {
        /// <summary>다듬은 삼각형(세계 좌표). 판정 3이면 null.</summary>
        public List<Tri>? Tris;
        public CivilSafeMesh.Report? Clean;
        public int Pieces, Cells, CellsKept, Unknown, TriFail, AreaMis, OverlayFail, RuleSamples, RuleOver1, RuleFlip, LineCount;
        public double UnknownArea, KeptArea;
        /// <summary>★[v103.3.1 · 1021 현장 계측] 모름을 까닭별로 — 조각(W)이 없는 셀 · 기준면(S · G)이 없는 셀 · 첫 자리.</summary>
        public int UnknownNoW, UnknownNoL; public string UnknownAt = "";
        /// <summary>★[v103.3.1] 결과에 못 드는 조각 삼각형 수(선을 안 넣은 것) · 덮개를 통째 합치기로 만든 조각(테두리 엮기 실패).</summary>
        public int Hidden; public string CoverNote = "";
        /// <summary>★[v103.3.2] 뺀 테두리 납작한 면 수 · 그 가장 큰 높이 · 넓이 합 · 꼭짓점이 테두리라 못 뺀 수(<see cref="DropBorderFlats"/>).</summary>
        public int BorderFlats, BorderFlatsKept; public double BorderFlatsMaxH, BorderFlatsArea;
        public double RuleMax; public string RuleAt = "", FlipAt = "";
        /// <summary>데이라잇 테두리 꼭짓점에서 |W − L| 최대 — 테두리가 기준면 위에 붙어 있나.</summary>
        public double EdgeMax; public string EdgeAt = ""; public int EdgeSamples;
        public double ZOneMax; public int ZOneMiss; public string ZOneAt = "";
        /// <summary>조각마다 남긴 넓이 · 남긴 셀이 그 조각 바깥 링(예산 끝)에 닿았나 — 닿았으면 예산이 모자라 굴착이 조각 끝에서 잘렸다(부르는 쪽이 그 조각만 예산을 늘려 다시 짓는다).</summary>
        public double[] PieceArea = Array.Empty<double>();
        public bool[] PieceRim = Array.Empty<bool>();
        public double HeightBudget;
        public long MsZero, MsNode, MsCells, MsClean;
        /// <summary>계측 전용 — <see cref="EnvelopeKeepRaw"/>가 참이면 다듬기 전 셀 삼각형(출하 길은 null).</summary>
        public List<Tri>? Raw;
        public int Tier = 3;
        public string Fail = "";
        public string TierText => Tier == 1 ? "정확" : Tier == 2 ? "⚠거의" : "⚠못 함";
        public string Summary => string.Format(CultureInfo.InvariantCulture,
            "판정 {0}({1}) · 조각 {2} · 선 {3} · 셀 {4}(남김 {5} · {6:F1}㎡){7} · 규칙 대조 {8}점 최대 {9:F3}mm{10}{11} · 데이라잇 테두리 {12}점 최대 {13:F3}mm{14}"
            + " · 한 점 한 높이 최대 {15:F3}mm{16} · 삼각형 {17}{18} · {19}ms(교선 {20} · 잇기 {21} · 셀 {22} · 다듬기 {23}){24}",
            Tier, TierText, $"{Pieces}(가린 삼각형 {Hidden}{(CoverNote.Length > 0 ? " · 덮개" + CoverNote : "")})", LineCount, Cells, CellsKept, KeptArea,
            Unknown > 0 ? $" · ⚠면을 모름 {Unknown}({UnknownArea:F3}㎡ · 조각 없음 {UnknownNoW} · 기준면 없음 {UnknownNoL} @{UnknownAt})" : "",
            RuleSamples, RuleMax * 1000, RuleMax > 1e-3 ? " @" + RuleAt : "", RuleFlip > 0 ? $" · ⚠남김/버림이 표본과 어긋남 {RuleFlip} @{FlipAt}" : "",
            EdgeSamples, EdgeMax * 1000, EdgeMax > 1e-3 ? " @" + EdgeAt : "",
            ZOneMax * 1000, ZOneMiss > 0 ? $" · ⚠못 읽은 꼭짓점 {ZOneMiss} @{ZOneAt}" : "",
            Tris?.Count ?? 0, TriFail + AreaMis > 0 ? $" · ⚠삼각분할 실패 {TriFail} · 넓이 안 맞음 {AreaMis}" : "",
            MsZero + MsNode + MsCells + MsClean, MsZero, MsNode, MsCells, MsClean,
            (Clean != null ? " · 다듬기: " + Clean.Summary : "")
            + (BorderFlats + BorderFlatsKept > 0 ? string.Format(CultureInfo.InvariantCulture, " · 테두리 납작한 면 뺌 {0}(높이 최대 {1:F2}µm · 넓이 {2:E1}㎡){3}", BorderFlats, BorderFlatsMaxH * 1e6, BorderFlatsArea, BorderFlatsKept > 0 ? $" · ⚠꼭짓점이 테두리라 남김 {BorderFlatsKept}" : "") : "")
            + (Fail.Length > 0 ? " · ⚠" + Fail : ""));
    }

    /// <summary>조각 하나 — 삼각형(볼록 껍질 메움을 버린 것) + 바깥 링(예산 끝).</summary>
    public sealed class ExcavPiece
    {
        public IReadOnlyList<Tri> Tris = Array.Empty<Tri>();
        public IReadOnlyList<Point3> Outer = Array.Empty<Point3>();
    }

    /// <summary>계측 전용 — 참이면 다듬기 전 셀 삼각형을 <see cref="EnvelopeResult.Raw"/>에 남긴다(출하 길은 거짓).</summary>
    public static bool EnvelopeKeepRaw;

    /// <summary>시험 전용 — 참이면 가린 삼각형도 선을 다 넣는다(종전 길 · 하네스 S167이 «가림을 끈 계산과 같은 답»을 잰다 · 출하 길은 거짓).</summary>
    public static bool EnvelopeNoHide;

    /// <summary>계측 전용 — 입력(S · G · 조각)을 글 파일로(한 줄 = 삼각형 9개 수 · 머리 줄 «S n» · «G n» · «P n 바깥링점수»). 오프라인으로 같은 셈을 다시 돌린다.</summary>
    public static void DumpEnvelopeInput(string path, IReadOnlyList<Tri> plan, IReadOnlyList<Tri> ground, IReadOnlyList<ExcavPiece> pieces)
    {
        var ci = CultureInfo.InvariantCulture;
        using var w = new StreamWriter(path, false, new System.Text.UTF8Encoding(false));
        void T(Tri t) => w.WriteLine(string.Format(ci, "{0:R} {1:R} {2:R} {3:R} {4:R} {5:R} {6:R} {7:R} {8:R}", t.A.X, t.A.Y, t.A.Z, t.B.X, t.B.Y, t.B.Z, t.C.X, t.C.Y, t.C.Z));
        w.WriteLine($"S {plan.Count}"); foreach (var t in plan) T(t);
        w.WriteLine($"G {ground.Count}"); foreach (var t in ground) T(t);
        foreach (var p in pieces)
        {
            w.WriteLine($"P {p.Tris.Count} {p.Outer.Count}");
            foreach (var t in p.Tris) T(t);
            foreach (var q in p.Outer) w.WriteLine(string.Format(ci, "{0:R} {1:R} {2:R}", q.X, q.Y, q.Z));
        }
    }

    /// <summary>계측 전용 — <see cref="DumpEnvelopeInput"/>로 쓴 파일을 읽는다.</summary>
    public static (List<Tri> S, List<Tri> G, List<ExcavPiece> P) ReadEnvelopeInput(string path)
    {
        var ci = CultureInfo.InvariantCulture;
        var lines = File.ReadAllLines(path);
        int i = 0;
        Tri Rd(string l) { var a = l.Split(' ').Select(s => double.Parse(s, ci)).ToArray(); return new Tri(new Point3(a[0], a[1], a[2]), new Point3(a[3], a[4], a[5]), new Point3(a[6], a[7], a[8])); }
        List<Tri> Block(int n) { var l = new List<Tri>(n); for (int k = 0; k < n; k++) l.Add(Rd(lines[i++])); return l; }
        var S = Block(int.Parse(lines[i++].Split(' ')[1]));
        var G = Block(int.Parse(lines[i++].Split(' ')[1]));
        var P = new List<ExcavPiece>();
        while (i < lines.Length && lines[i].StartsWith("P "))
        {
            var h = lines[i++].Split(' ');
            var tris = Block(int.Parse(h[1]));
            var outer = new List<Point3>();
            for (int k = 0; k < int.Parse(h[2]); k++) { var a = lines[i++].Split(' ').Select(s => double.Parse(s, ci)).ToArray(); outer.Add(new Point3(a[0], a[1], a[2])); }
            P.Add(new ExcavPiece { Tris = tris, Outer = outer });
        }
        return (S, G, P);
    }

    /// <param name="plan">정지면 S 삼각형 — 조각 바깥 링 상자에 닿는 것만 넘겨도 된다.</param>
    /// <param name="ground">원지반 G 삼각형 — 같은 범위.</param>
    public static EnvelopeResult LowerEnvelope(IReadOnlyList<Tri> plan, IReadOnlyList<Tri> ground, IReadOnlyList<ExcavPiece> pieces)
    {
        var R = new EnvelopeResult { Pieces = pieces?.Count ?? 0 };
        var sw = Stopwatch.StartNew();
        if (pieces == null || pieces.Count == 0 || pieces.All(p => p.Tris.Count == 0)) { R.Fail = "조각이 없다"; return R; }
        if ((plan == null || plan.Count == 0) && (ground == null || ground.Count == 0)) { R.Fail = "정지면 · 원지반이 둘 다 없다"; return R; }
        plan ??= Array.Empty<Tri>(); ground ??= Array.Empty<Tri>();
        var first = pieces.First(p => p.Tris.Count > 0).Tris[0].A;
        double ox = first.X, oy = first.Y;
        var gf = new GeometryFactory();
        var pm = new PrecisionModel(1e6);
        int n = pieces.Count;
        R.PieceArea = new double[n]; R.PieceRim = new bool[n];

        // ① 조각 덮개(삼각형이 실제로 있는 자리) · 그 합 = 범위
        //   ★[v103.3.1 · 1021 현장 계측] 종전엔 바깥 링(예산 끝)으로 잡았다 — Civil이 지은 삼각형 테두리가 링과 µm 어긋나
        //   그 틈에 «조각이 없는 셀»(1.9e-6㎡ 둘)이 생겨 판정 3이 났다. 덮개는 삼각형 테두리 그대로라 범위 안 셀은 언제나 조각이 있다.
        var W = new TriSet[n];
        var cov = new Geometry?[n];
        var prepCov = new IPreparedGeometry?[n];
        var rimDist = new NetTopologySuite.Operation.Distance.IndexedFacetDistance?[n];
        for (int k = 0; k < n; k++)
        {
            W[k] = new TriSet(pieces[k].Tris, ox, oy);
            if (W[k].N == 0) continue;
            cov[k] = Coverage(W[k], gf, out string cw);
            if (cov[k] == null) { R.Fail = $"조각 {k + 1} 덮개를 못 만들었다({cw})"; return R; }
            if (cw.Length > 0) R.CoverNote += $" 조각{k + 1}:{cw}";
            prepCov[k] = PreparedGeometryFactory.Prepare(cov[k]);
            rimDist[k] = new NetTopologySuite.Operation.Distance.IndexedFacetDistance(cov[k]!.Boundary);
        }
        Geometry Reg;
        try { Reg = OverlayNGRobust.Union(cov.Where(g => g != null).Cast<Geometry>().ToList()); }
        catch (Exception ex) { R.Fail = $"범위를 못 합쳤다 {ex.GetType().Name}"; return R; }
        var prepReg = PreparedGeometryFactory.Prepare(Reg);
        var S = new TriSet(plan, ox, oy);
        var G = new TriSet(ground, ox, oy);

        // ①-b ★★[v103.3.1 · 1021 현장 계측] <b>결과에 못 드는 삼각형은 선을 안 넣는다</b> — 다른 면이 그 삼각형을 다 덮고 그 상자 안 최고 높이가
        //   삼각형의 최저 높이보다 1 mm 넘게 낮으면(= 그 삼각형 어디서나 더 낮은 면이 있다) 그 조각은 거기서 min도 아니고 기준면 아래도 아니다.
        //   현장: 1단 바닥(104) 위 5 ~ 18 m에 뜬 2 ~ 4단 조각 · 땅 위 예산 법면의 변과 교선까지 다 넣어 선 42,486 · 셀 136,282 · 71초가 되고,
        //   그 선들이 1 mm 안에서 엇갈려 다듬기가 못 푼 쌍을 남겼다(판정 3). 높이는 여전히 모든 조각에서 읽는다(MinW) — 선만 뺀다.
        //   확실할 때만 빼므로(덮개 포함 + 상자 최고 &lt; 최저 − 1 mm) 결과는 같고, 규칙 대조가 그대로 지킨다.
        string cwS = "", cwG = "";
        var covS = S.N > 0 ? Coverage(S, gf, out cwS) : null;
        var covG = G.N > 0 ? Coverage(G, gf, out cwG) : null;
        if (cwS.Length > 0) R.CoverNote += " 정지면:" + cwS;   // ★[설치 전 검토 낮음] 통째 합치기로 간 까닭을 남긴다(느려진 까닭)
        if (cwG.Length > 0) R.CoverNote += " 원지반:" + cwG;
        if (EnvelopeNoHide) { covS = covG = null; }
        var prepS = covS != null ? PreparedGeometryFactory.Prepare(covS) : null;
        var prepG = covG != null ? PreparedGeometryFactory.Prepare(covG) : null;
        var use = new bool[n][];
        for (int k = 0; k < n; k++)
        {
            use[k] = new bool[W[k].N];
            for (int i = 0; i < W[k].N; i++)
            {
                if (!W[k].Ok[i]) continue;
                bool hide = !EnvelopeNoHide && (HiddenTri(W[k], i, S, prepS, gf) || HiddenTri(W[k], i, G, prepG, gf));
                for (int j = 0; j < n && !hide && !EnvelopeNoHide; j++) if (j != k) hide = HiddenTri(W[k], i, W[j], prepCov[j], gf);
                use[k][i] = !hide;
                if (hide) R.Hidden++;
            }
        }

        // ② 높이 차 0 선 — 꼭 필요한 것만 한 벌씩(S167 ②: ±ZeroD 두 벌은 세 선이 만나는 모서리에 1~2µm 점 무리를 만들어 다듬기가 못 풀었다)
        //   · W_k − W_j = 0 — 어느 조각 평면으로 삼각분할할지가 바뀌는 자리(같으면 어느 쪽이든 같은 높이)
        //   · W_k − S = −ZeroD · W_k − G = −ZeroD — 남김 조건 «S − W > ZeroD 이고 G − W > ZeroD»(= L − W > ZeroD)의 경계 그대로.
        //     S = G 선은 필요 없다(남김은 두 조건의 «그리고»라 각자 제 선으로 갈린다)
        //   ★[v103.3.1] 결과에 못 드는 삼각형(①-b)의 교선은 안 긋는다 — W_a = W_b인데 둘 중 하나가 거기서 가려졌으면 min은 제3의 면이거나 기준면 위다.
        var zs = new List<ZeroSet>();
        for (int a = 0; a < n; a++)
        {
            for (int b = a + 1; b < n; b++) zs.Add(ZeroLines(W[a], W[b], gf, ox, oy, null, 0.0, use[a], use[b]));
            if (S.N > 0) zs.Add(ZeroLines(W[a], S, gf, ox, oy, null, -ZeroD, use[a]));
            if (G.N > 0) zs.Add(ZeroLines(W[a], G, gf, ox, oy, null, -ZeroD, use[a]));
        }
        foreach (var z in zs) R.OverlayFail += z.OverlayFail;
        R.MsZero = sw.ElapsedMilliseconds; sw.Restart();

        // ③ 한 번에 잇기 — 범위 테두리 + 조각 변 + (범위로 자른) S · G 변 + (범위로 자른) 교선
        var lines = new List<Geometry> { Reg.Boundary };
        var seen = new HashSet<((double, double), (double, double))>();
        void AddEdges(TriSet T0, bool clip, bool[]? only = null)
        {
            var ls = new List<LineString>();
            for (int i = 0; i < T0.N; i++)
            {
                if (!T0.Ok[i] || (only != null && !only[i])) continue;
                var p3 = new[] { (T0.X1[i], T0.Y1[i]), (T0.X2[i], T0.Y2[i]), (T0.X3[i], T0.Y3[i]) };
                for (int e = 0; e < 3; e++)
                {
                    var p = p3[e]; var q = p3[(e + 1) % 3];
                    var key = p.CompareTo(q) <= 0 ? (p, q) : (q, p);
                    if (!seen.Add(key)) continue;
                    ls.Add(gf.CreateLineString(new[] { new Coordinate(p.Item1, p.Item2), new Coordinate(q.Item1, q.Item2) }));
                }
            }
            if (ls.Count == 0) return;
            Geometry g = gf.CreateMultiLineString(ls.ToArray());
            if (clip) { try { g = OverlayNGRobust.Overlay(g, Reg, NetTopologySuite.Operation.Overlay.SpatialFunction.Intersection); } catch { R.Fail = "변을 범위로 못 잘랐다"; } }
            lines.Add(g);
        }
        for (int k = 0; k < n; k++) AddEdges(W[k], false, use[k]);   // ★[v103.3.1] 가려진 삼각형 변은 뺀다(①-b) — 범위 테두리는 위에서 넣었다
        // ★[1021 화면 없는 Civil 시험 ⑨ — 계측] S · G 변은 <b>안 넣는다</b>. 남긴 셀의 높이는 언제나 조각(W) 평면이고, 남길지는 «W − S · W − G = −ZeroD» 교선이
        //   정확히 가른다(그 교선은 S · G 삼각형마다 따로 구해 이어진다) — S · G 변은 셀을 나눌 까닭이 없다.
        //   넣었더니 정지면 격자 변이 조각 선과 93µm로 나란히 지나 Civil이 합칠 쌍(0.1mm 안)을 만들어 다듬기가 못 풀었다(판정 3).
        foreach (var z in zs)
        {
            if (z.Lines.Count == 0) continue;
            Geometry g = gf.BuildGeometry(z.Lines);
            try { g = OverlayNGRobust.Overlay(g, Reg, NetTopologySuite.Operation.Overlay.SpatialFunction.Intersection); }
            catch { R.Fail = "교선을 범위로 못 잘랐다"; }
            lines.Add(g);
        }
        R.LineCount = lines.Sum(g => g.NumGeometries);
        Geometry noded;
        try { noded = UnaryUnionNG.Union((Geometry)gf.CreateGeometryCollection(lines.ToArray()), pm); }
        catch (Exception ex) { R.Fail = $"선 잇기가 터졌다 {ex.GetType().Name}"; return R; }
        R.MsNode = sw.ElapsedMilliseconds; sw.Restart();

        // ④ 셀마다 — W = min W_k(있는 것만) · L = Lower(S, G) · W < L이면 남기고 그 조각 평면으로 삼각분할
        bool MinW(double x, double y, out double zw, out int kb)
        {
            zw = double.PositiveInfinity; kb = -1;
            for (int k = 0; k < n; k++)
                if (W[k].TryZ(x, y, out double z) && z < zw) { zw = z; kb = k; }
            return kb >= 0;
        }
        bool Low(double x, double y, out double zl)
        {
            bool hs = S.TryZ(x, y, out double zs1), hg = G.TryZ(x, y, out double zg1);
            zl = hs && hg ? Math.Min(zs1, zg1) : hs ? zs1 : hg ? zg1 : double.NaN;
            return hs || hg;
        }
        var pz = new Polygonizer(); pz.Add(noded);
        var raw = new List<Tri>();
        var keptPieceOf = new List<int>();
        foreach (var gpz in pz.GetPolygons())
        {
            if (gpz is not Polygon f || f.IsEmpty || f.Area <= 0) continue;
            var ip = f.InteriorPoint;
            if (ip == null || !prepReg.Contains(ip)) continue;
            R.Cells++;
            if (!MinW(ip.X, ip.Y, out double zw, out int kb)) { R.Unknown++; R.UnknownNoW++; R.UnknownArea += f.Area; if (R.UnknownAt.Length == 0) R.UnknownAt = $"({ip.X + ox:F4},{ip.Y + oy:F4}) {f.Area:E1}㎡"; continue; }
            if (!Low(ip.X, ip.Y, out double zl)) { R.Unknown++; R.UnknownNoL++; R.UnknownArea += f.Area; if (R.UnknownAt.Length == 0) R.UnknownAt = $"({ip.X + ox:F4},{ip.Y + oy:F4}) {f.Area:E1}㎡"; continue; }
            bool keep = zl - zw > ZeroD;
            int tIdx = W[kb].At(ip.X, ip.Y);
            // 표본 — 안 점 + 꼭짓점마다 안쪽 25 %(셀 안인 것만): 남김/버림이 셀 안에서 같아야 하고, 남기면 고른 평면 = min W
            var samples = new List<Coordinate> { ip.Coordinate };
            var ec = f.ExteriorRing.Coordinates;
            for (int q = 0; q + 1 < ec.Length; q++)
            {
                var s = new Coordinate(ec[q].X + 0.25 * (ip.X - ec[q].X), ec[q].Y + 0.25 * (ip.Y - ec[q].Y));
                if (f.Contains(gf.CreatePoint(s))) samples.Add(s);
            }
            foreach (var s in samples)
            {
                if (!MinW(s.X, s.Y, out double zws, out _) || !Low(s.X, s.Y, out double zls)) continue;
                bool k2 = zls - zws > ZeroD;
                if (k2 != keep && Math.Abs(zls - zws) > 1e-3) { R.RuleFlip++; if (R.FlipAt.Length == 0) R.FlipAt = $"({s.X + ox:F3},{s.Y + oy:F3})"; }
                if (!keep) continue;
                R.RuleSamples++;
                double dz = Math.Abs(W[kb].Z(tIdx, s.X, s.Y) - zws);       // 고른 평면 vs 따로 읽은 min W
                if (dz > 1e-3) R.RuleOver1++;
                if (dz > R.RuleMax) { R.RuleMax = dz; R.RuleAt = $"({s.X + ox:F4},{s.Y + oy:F4})"; }
            }
            if (!keep) continue;
            Geometry trs;
            try { trs = NetTopologySuite.Triangulate.Polygon.ConstrainedDelaunayTriangulator.Triangulate(f); }
            catch { R.TriFail++; continue; }
            double fa = 0; var local = new List<Tri>();
            for (int q = 0; q < trs.NumGeometries; q++)
            {
                if (trs.GetGeometryN(q) is not Polygon tq || tq.Area <= 0) continue;
                var c = tq.ExteriorRing.Coordinates;
                Point3 Pt(Coordinate k0) => new(k0.X + ox, k0.Y + oy, W[kb].Z(tIdx, k0.X, k0.Y));
                local.Add(new Tri(Pt(c[0]), Pt(c[1]), Pt(c[2])));
                fa += tq.Area;
            }
            if (Math.Abs(fa - f.Area) > 1e-9 + 1e-9 * f.Area) { R.AreaMis++; continue; }
            raw.AddRange(local);
            R.CellsKept++; R.KeptArea += f.Area; R.PieceArea[kb] += f.Area;
            // 조각 바깥 링(예산 끝)에 닿았나
            if (rimDist[kb] != null)
                foreach (var c in ec)
                    if (rimDist[kb]!.Distance(gf.CreatePoint(c)) < 1e-6) { R.PieceRim[kb] = true; break; }
        }
        R.MsCells = sw.ElapsedMilliseconds; sw.Restart();

        // ⑤ 한 점 = 한 높이 — 꼭짓점마다 min W(1 µm 격자로 옮겨진 교선 · 테두리 점도 면끼리 같은 값)
        {
            var zOne = new Dictionary<(double, double), double>();
            Point3 One(Point3 q)
            {
                var key = (q.X, q.Y);
                if (!zOne.TryGetValue(key, out double z))
                {
                    double x = q.X - ox, y = q.Y - oy, best = double.PositiveInfinity;
                    for (int k = 0; k < n; k++) if (W[k].TryZNear(x, y, 1e-5, out double zk) && zk < best) best = zk;
                    if (double.IsPositiveInfinity(best)) { z = q.Z; R.ZOneMiss++; if (R.ZOneAt.Length == 0) R.ZOneAt = $"({q.X:F3},{q.Y:F3})"; }
                    else z = best;
                    zOne[key] = z;
                }
                double dz = Math.Abs(z - q.Z);
                if (dz > R.ZOneMax) R.ZOneMax = dz;
                return dz == 0 ? q : new Point3(q.X, q.Y, z);
            }
            for (int i = 0; i < raw.Count; i++) raw[i] = new Tri(One(raw[i].A), One(raw[i].B), One(raw[i].C));
        }

        // ⑥ 다듬기 — 고정 점 없음(참값 = 셀 삼각형). ★테두리 점을 고정하면 세 선이 만나는 모서리의 µm 쌍을 못 합쳐 판정 3이 났다(S167 ②) —
        //   대신 <b>다듬은 뒤</b> 데이라잇 테두리가 기준면에 붙어 있는지 잰다(⑦)
        double rest = R.RuleMax + R.ZOneMax;
        if (EnvelopeKeepRaw) R.Raw = new List<Tri>(raw);
        if (raw.Count > 0)
        {
            R.Tris = CivilSafeMesh.MakeZone(raw, Array.Empty<Point3>(), out var cr, wallTol2: 1e-2 - rest, wallCap: CivilSafeMesh.ForcedCap - rest);
            R.Clean = cr;
            R.HeightBudget = rest + cr.WallDz;
            // ⑥-b ★[v103.3.2 · 1021 17:06 현장] 테두리의 납작한 면을 뺀다(<see cref="DropBorderFlats"/> — Civil이 말없이 버려 되읽기가 어긋났다)
            if (R.Tris != null)
            {
                R.Tris = DropBorderFlats(R.Tris, CivilSafeMesh.Gap, out R.BorderFlats, out R.BorderFlatsKept, out R.BorderFlatsMaxH, out R.BorderFlatsArea);
                if (R.Tris.Count == 0) R.Tris = null;
            }
        }
        R.MsClean = sw.ElapsedMilliseconds;

        // ⑦ 데이라잇 테두리(다듬은 결과의 바깥 변 꼭짓점) — |터파기 − L| 재기 · 조각 바깥 링(예산 끝) 위 점은 따로(PieceRim)
        if (R.Tris != null)
        {
            static ((double, double), (double, double)) EK(Point3 a, Point3 b)
            { var ka = (a.X, a.Y); var kb = (b.X, b.Y); return ka.CompareTo(kb) <= 0 ? (ka, kb) : (kb, ka); }
            var cnt = new Dictionary<((double, double), (double, double)), int>();
            foreach (var t in R.Tris) foreach (var k in new[] { EK(t.A, t.B), EK(t.B, t.C), EK(t.C, t.A) }) cnt[k] = cnt.TryGetValue(k, out int c) ? c + 1 : 1;
            var bv = new Dictionary<(double, double), Point3>();
            foreach (var t in R.Tris)
                foreach (var (a, b) in new[] { (t.A, t.B), (t.B, t.C), (t.C, t.A) })
                    if (cnt[EK(a, b)] == 1) { bv.TryAdd((a.X, a.Y), a); bv.TryAdd((b.X, b.Y), b); }
            foreach (var q in bv.Values)
            {
                double x = q.X - ox, y = q.Y - oy;
                var cpt = gf.CreatePoint(new Coordinate(x, y));
                bool onRim = false;
                for (int k = 0; k < n && !onRim; k++) if (rimDist[k] != null && rimDist[k]!.Distance(cpt) < 1e-5) onRim = true;
                if (onRim) continue;
                bool hs = S.TryZNear(x, y, 1e-5, out double zs2), hg = G.TryZNear(x, y, 1e-5, out double zg2);
                if (!hs && !hg) continue;
                double zl = hs && hg ? Math.Min(zs2, zg2) : hs ? zs2 : zg2;
                R.EdgeSamples++;
                double dz = Math.Abs(q.Z - zl);
                if (dz > R.EdgeMax) { R.EdgeMax = dz; R.EdgeAt = $"({q.X:F3},{q.Y:F3}) 터파기 {q.Z:F3} · 기준면 {zl:F3}"; }
            }
        }

        // ⑧ 판정
        int tier = R.Clean?.Tier ?? 3;
        var why = new List<string>();
        if (raw.Count == 0) { tier = 3; why.Add("남길 셀이 없다(조각이 모두 기준면 위 — 팔 데가 없다)"); }
        if (R.OverlayFail > 0) { tier = 3; why.Add($"교선 겹침 계산 실패 {R.OverlayFail}"); }
        if (R.Unknown > 0) { tier = 3; why.Add($"면을 모르는 셀 {R.Unknown}({R.UnknownArea:F3}㎡ — 정지면 · 원지반이 둘 다 없는 자리)"); }
        if (R.TriFail + R.AreaMis > 0) { tier = 3; why.Add($"삼각분할 실패 {R.TriFail} · 넓이 안 맞음 {R.AreaMis}"); }
        if (R.RuleFlip > 0) { tier = 3; why.Add($"셀 안에서 남김/버림이 표본과 어긋남 {R.RuleFlip} @{R.FlipAt}"); }
        if (R.RuleMax > 1e-2) { tier = 3; why.Add($"규칙과 {R.RuleMax * 1000:F1}mm @{R.RuleAt}"); }
        else if (R.RuleMax > 1e-3) { tier = Math.Max(tier, 2); why.Add($"규칙과 {R.RuleMax * 1000:F2}mm @{R.RuleAt}"); }
        if (R.ZOneMiss > 0) { tier = Math.Max(tier, 2); why.Add($"min W를 못 읽은 꼭짓점 {R.ZOneMiss} @{R.ZOneAt}"); }
        if (R.EdgeMax > 1e-2) { tier = 3; why.Add($"데이라잇 테두리가 기준면과 {R.EdgeMax * 1000:F1}mm 떨어짐 @{R.EdgeAt}"); }
        else if (R.EdgeMax > 1e-3) { tier = Math.Max(tier, 2); why.Add($"데이라잇 테두리와 기준면 {R.EdgeMax * 1000:F2}mm @{R.EdgeAt}"); }
        if (raw.Count > 0 && R.Tris == null) { tier = 3; why.Add("다듬기 못 함 — " + (R.Clean?.Fail ?? "")); }
        if (R.HeightBudget > 1e-2) { tier = Math.Max(tier, 2); why.Add($"높이 오차 합 {R.HeightBudget * 1000:F2}mm"); }
        if (R.Fail.Length > 0) { tier = 3; why.Add(R.Fail); }
        R.Tier = tier;
        if (why.Count > 0) R.Fail = string.Join(" · ", why);
        if (tier == 3) R.Tris = null;
        return R;
    }

    /// <summary>★[v103.3.1] 결과에 못 드는 삼각형인가 — 면 <paramref name="O"/>가 그 삼각형을 다 덮고(<paramref name="covO"/>),
    /// 그 상자에 걸친 O 삼각형의 최고 높이가 이 삼각형의 최저 높이보다 <see cref="HideMargin"/> 넘게 낮으면 참(삼각형 어디서나 O가 더 낮다).
    /// 상자에 걸친 삼각형의 꼭짓점 최고는 삼각형 위 O의 최고보다 크거나 같다 — 참이면 확실하다.</summary>
    private static bool HiddenTri(TriSet T, int i, TriSet O, IPreparedGeometry? covO, GeometryFactory gf)
    {
        if (covO == null || O.N == 0) return false;
        double lo = T.MinZ[i] - HideMargin, mx = double.NegativeInfinity;
        foreach (int j in O.Tree.Query(T.Env(i))) { if (O.MaxZ[j] > mx) mx = O.MaxZ[j]; if (mx >= lo) return false; }
        if (double.IsNegativeInfinity(mx)) return false;
        return covO.Contains(T.Poly(i, gf));
    }

    /// <summary>가렸다고 볼 높이 여유(m).</summary>
    private const double HideMargin = 1e-3;

    /// <summary>★[v103.3.2 · 1021 17:06 현장 계측] <b>테두리의 납작한 면</b>을 뺀다 — 가장 긴 변이 테두리(면 하나만 씀)이고 가장 낮은 높이가 <paramref name="maxH"/> 미만인 면.
    /// <para>다듬기(<see cref="CivilSafeMesh.MakeZone"/>)는 납작한 면을 짧은 변으로 접거나 긴 변을 뒤집어 없애는데, 긴 변이 테두리면 뒤집을 옆 면이 없고 짧은 변이 길면(24 · 47 mm) 접지도 못해 남긴다.
    /// 현장 1:0.3 판: 그런 면 넷 중 높이 0.28 · 0.29 µm 둘을 Civil이 버렸고(되읽기 25,579/25,581 → 판정 «못 지음») 0.64 µm 면은 받았다 — Civil의 문턱을 모르니 <b>우리가 먼저 다 뺀다</b>.
    /// 테두리가 그 높이(&lt; 0.1 mm)만큼 안쪽으로 들어올 뿐 높이는 그대로다.</para>
    /// <para>맞은 꼭짓점이 이미 테두리 점이면 빼지 않는다(빼면 두 면이 한 점에서만 맞닿는 꼴이 된다) — 센다(<paramref name="skipped"/>).
    /// 한 바퀴에 서로 점을 나누지 않는 면만 빼고 다시 센다(뺀 면 옆이 새로 테두리 납작한 면이 될 수 있다).</para></summary>
    public static List<Tri> DropBorderFlats(IReadOnlyList<Tri> tris, double maxH, out int dropped, out int skipped, out double maxHDropped, out double areaDropped)
    {
        dropped = 0; skipped = 0; maxHDropped = 0; areaDropped = 0;
        var cur = new List<Tri>(tris);
        static ((double, double), (double, double)) EK(Point3 a, Point3 b) { var ka = (a.X, a.Y); var kb = (b.X, b.Y); return ka.CompareTo(kb) <= 0 ? (ka, kb) : (kb, ka); }
        static double L(Point3 a, Point3 b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
        for (int pass = 0; pass < 100; pass++)
        {
            var cnt = new Dictionary<((double, double), (double, double)), int>();
            foreach (var t in cur) foreach (var k in new[] { EK(t.A, t.B), EK(t.B, t.C), EK(t.C, t.A) }) cnt[k] = cnt.TryGetValue(k, out int c) ? c + 1 : 1;
            var bdV = new HashSet<(double, double)>();
            foreach (var kv in cnt) if (kv.Value == 1) { bdV.Add(kv.Key.Item1); bdV.Add(kv.Key.Item2); }
            var used = new HashSet<(double, double)>();
            var gone = new HashSet<int>();
            skipped = 0;
            for (int i = 0; i < cur.Count; i++)
            {
                var t = cur[i];
                var (u, v, w) = new[] { (t.A, t.B, t.C), (t.B, t.C, t.A), (t.C, t.A, t.B) }.OrderByDescending(x => L(x.Item1, x.Item2)).First();
                double lo = L(u, v);
                if (lo <= 0) continue;
                double ar = Math.Abs((t.B.X - t.A.X) * (t.C.Y - t.A.Y) - (t.C.X - t.A.X) * (t.B.Y - t.A.Y)) / 2, h = 2 * ar / lo;
                if (!(h < maxH) || cnt[EK(u, v)] != 1) continue;
                if (bdV.Contains((w.X, w.Y))) { skipped++; continue; }
                var vs = new[] { (u.X, u.Y), (v.X, v.Y), (w.X, w.Y) };
                if (vs.Any(used.Contains)) continue;                  // 이 바퀴에 이미 뺀 면과 점을 나눈다 — 다음 바퀴에
                foreach (var q in vs) used.Add(q);
                gone.Add(i);
                dropped++; maxHDropped = Math.Max(maxHDropped, h); areaDropped += ar;
            }
            if (gone.Count == 0) break;
            var next = new List<Tri>(cur.Count - gone.Count);
            for (int i = 0; i < cur.Count; i++) if (!gone.Contains(i)) next.Add(cur[i]);
            cur = next;
        }
        return cur;
    }

    /// <summary>★[v103.3.1] 삼각형 모음이 덮는 자리 — 한 번만 나온 변(테두리)을 엮어 다각형으로 만들고 안쪽 점이 삼각형 안인 것만 합친다.
    /// 넓이가 삼각형 넓이 합과 안 맞으면(엮기 실패) 삼각형을 통째로 합친다(<paramref name="why"/>에 적는다 · 느리지만 같은 답).</summary>
    private static Geometry? Coverage(TriSet T, GeometryFactory gf, out string why)
    {
        why = "";
        var cnt = new Dictionary<((double, double), (double, double)), int>();
        double area = 0;
        for (int i = 0; i < T.N; i++)
        {
            var p = new[] { (T.X1[i], T.Y1[i]), (T.X2[i], T.Y2[i]), (T.X3[i], T.Y3[i]) };
            for (int e = 0; e < 3; e++)
            {
                var a = p[e]; var b = p[(e + 1) % 3];
                if (a == b) continue;
                var key = a.CompareTo(b) <= 0 ? (a, b) : (b, a);
                cnt[key] = cnt.TryGetValue(key, out int c) ? c + 1 : 1;
            }
            area += Math.Abs((T.X2[i] - T.X1[i]) * (T.Y3[i] - T.Y1[i]) - (T.X3[i] - T.X1[i]) * (T.Y2[i] - T.Y1[i])) / 2;
        }
        try
        {
            var pz = new Polygonizer();
            pz.Add(cnt.Where(kv => kv.Value == 1)
                      .Select(kv => (Geometry)gf.CreateLineString(new[] { new Coordinate(kv.Key.Item1.Item1, kv.Key.Item1.Item2), new Coordinate(kv.Key.Item2.Item1, kv.Key.Item2.Item2) }))
                      .ToList());
            var faces = new List<Geometry>();
            foreach (var g in pz.GetPolygons())
                if (g is Polygon f && f.Area > 0 && f.InteriorPoint is Point ip && T.TryZ(ip.X, ip.Y, out _)) faces.Add(f);
            if (faces.Count > 0)
            {
                Geometry u = faces.Count == 1 ? faces[0] : OverlayNGRobust.Union(faces);
                if (Math.Abs(u.Area - area) <= 1e-6 * Math.Max(1.0, area)) return u;
                why = string.Format(CultureInfo.InvariantCulture, "테두리 엮기 넓이 {0:F6} ≠ 삼각형 합 {1:F6}", u.Area, area);
            }
            else why = "테두리 엮기 0면";
        }
        catch (Exception ex) { why = "테두리 엮기 " + ex.GetType().Name; }
        try { return OverlayNGRobust.Union(Enumerable.Range(0, T.N).Where(i => T.Ok[i]).Select(i => (Geometry)T.Poly(i, gf)).ToList()); }
        catch (Exception ex) { why += " · 통째 합치기 " + ex.GetType().Name; return null; }
    }
}
