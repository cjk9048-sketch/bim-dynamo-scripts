using System.Diagnostics;
using DH.Takeoff.Core.View3D;

namespace DH.Takeoff.Core.Formwork;

/// <summary>
/// ② 의 «면» 하나(R73a — JACK 1030 «중간슬래브 · 내측 0.43㎡» · 계획 §3.1 붓 단위): 같은 부재 · 같은 면(FaceResult) · 같은 덩이에 든 조각들(대개 하나 · 7 m 구간 조각은 함께).
/// 덩이에 안 든 조각(그림에 없는 틈 · 윗면 · 땅 위 밑면)도 면 하나다(Piece = −1). 위치 나무 · 마우스 밑 이름표 · 3D 찍기가 같은 단위를 쓴다.
/// 수량은 조각이 들고 있다 — 이것은 보기만 묶는다(세는 면 넓이를 다 더하면 보드 합계).
/// </summary>
public sealed class BoardFace
{
    internal BoardFace(int index, ElementFormwork result, FaceResult face, int piece, List<FacePatch> patches)
    {
        Index = index;
        Result = result;
        Face = face;
        Piece = piece;
        Patches = patches;
        Lead = patches.OrderByDescending(p => p.Area).First();
        Siblings = new[] { this };
        // 가운데(넓이로 무게를 단 세모의 가운데 — 세모가 없으면 테두리 점의 가운데) · 높이 범위.
        double ax = 0, ay = 0, az = 0, w = 0, zMin = double.MaxValue, zMax = double.MinValue;
        foreach (var p in patches)
            for (int i = 0; i + 2 < p.Triangles.Count; i += 3)
            {
                double[] a = p.Triangles[i], b = p.Triangles[i + 1], c = p.Triangles[i + 2];
                var n = new Vec3(b[0] - a[0], b[1] - a[1], b[2] - a[2]).Cross(new Vec3(c[0] - a[0], c[1] - a[1], c[2] - a[2]));
                double s = n.Length / 2;
                if (!(s > 0) || !double.IsFinite(s)) continue;
                ax += s * (a[0] + b[0] + c[0]) / 3; ay += s * (a[1] + b[1] + c[1]) / 3; az += s * (a[2] + b[2] + c[2]) / 3; w += s;
                zMin = Math.Min(zMin, Math.Min(a[2], Math.Min(b[2], c[2])));
                zMax = Math.Max(zMax, Math.Max(a[2], Math.Max(b[2], c[2])));
            }
        if (w <= 0)
        {
            int count = 0;
            foreach (var q in patches.SelectMany(p => p.Outline).SelectMany(r => r).Where(q => q.Length >= 3))
            {
                ax += q[0]; ay += q[1]; az += q[2]; count++;
                zMin = Math.Min(zMin, q[2]);
                zMax = Math.Max(zMax, q[2]);
            }
            w = count;
        }
        Center = w > 0 ? new Vec3(ax / w, ay / w, az / w) : new Vec3(0, 0, (face.ZMin + face.ZMax) / 2);
        (ZMin, ZMax) = zMin <= zMax ? (zMin, zMax) : (face.ZMin, face.ZMax);
    }

    /// <summary>보드의 면 목록에서의 차례(FormworkBoard.Faces).</summary>
    public int Index { get; }
    public ElementFormwork Result { get; }
    public FaceResult Face { get; }

    /// <summary>든 덩이의 차례(FormworkBoard.Pieces · 덩이에 안 들면 −1).</summary>
    public int Piece { get; }

    public IReadOnlyList<FacePatch> Patches { get; }

    /// <summary>가장 넓은 조각(색 · 종류 · 다시 잡는 열쇠).</summary>
    public FacePatch Lead { get; }

    /// <summary>
    /// 다른 부재가 다 덮어 거푸집이 없는 면(R73a6 — JACK 1102 «출입구 디딤판 … 닿는 부분이 있는데 빗금 표현이 안 돼 있고»): 조각이 없던 면에 원본 면 모양의 보기용 조각 하나를 붙여 둔다
    /// (넓이 0 · 세지 않음 — 수량 · 기록 · 엑셀과 상관없음). 빗금 · 고르기 · 도구가 그 면을 쓴다. 나무에는 싣지 않는다(Fixed).
    /// </summary>
    public bool Covered { get; internal init; }

    /// <summary>
    /// 한 면처럼 볼 면들(R73a7 — JACK 1102 «계단 같은 경우에 하나의 객체인데 옆면이 여러 개로 조각나 있어서 벽체 벽체 벽체 … 면을 선택하기가 힘들어»):
    /// 같은 부재 · 같은 평면 · 같은 판(판이 없으면 둘 다 다 덮인 면)이고 테두리가 맞닿은 면들 — Revit 계단은 단마다 옆면이 따로다(녹촌 계단 1: 옆면 25 · 26 조각).
    /// 나무 줄 하나 · 이름 하나 · 고르면 함께 · 테두리 하나. 첫 것이 대표(면 차례가 가장 앞) · 혼자면 자기 하나. 보기만 — 조각 · 넓이 · 기록 그대로.
    /// </summary>
    public IReadOnlyList<BoardFace> Siblings { get; internal set; }

    /// <summary>한 면처럼 볼 면들의 넓이(R73a7 — 이름표 · 나무).</summary>
    public double JoinedArea => Siblings.Sum(f => f.Area);

    /// <summary>다 덮인 면의 닿은 넓이(덮음 + 깎임 + 무시 — 이웃끼리 겹치면 면보다 클 수 있다 · 그 밖은 0).</summary>
    public double CoveredArea => Covered ? Face.Covers.Concat(Face.Cut).Concat(Face.Ignored).Sum(c => c.Area) : 0;

    /// <summary>쉬운 이름(FaceNames — «외측면» · «내측면(수조부)» · «마구리면 1» · «하부면» …).</summary>
    public string Name { get; internal set; } = "";

    public long ElementId => Result.Element.Id;
    public double Area => Patches.Sum(p => p.Area);

    /// <summary>세는 조각의 넓이(㎡ — 면마다 더하면 보드 합계).</summary>
    public double CountedArea => Patches.Where(p => p.Counted).Sum(p => p.Area);

    public bool Counted => Patches.Any(p => p.Counted);
    public bool NeedsCheck => Patches.Any(p => p.NeedsCheck);

    /// <summary>형상이 정해 세지 않는 면(윗면 · 땅 위 밑면 — 대응표 줄 밖이고 확인할 것도 아님). 위치 나무에 싣지 않는다.</summary>
    public bool Fixed => Piece < 0 && !NeedsCheck;

    /// <summary>사람이 판마다 정한 기록이 걸린 면(지운 판 · 옮긴 판 · 판 종류 · 판 자리 · 판 쪼갬 — 나무의 ●).</summary>
    public bool Direct => Patches.Any(p => p.DropLead.Length > 0 || p.MoveLead.Length > 0 || p.KindLead.Length > 0 || p.FaceKindLead.Length > 0 || p.PlaceLead.Length > 0 || p.SplitLead.Length > 0);

    /// <summary>
    /// 면 ↺ 로 걷을 기록이 걸린 면(R73b — 면 종류 · 판 종류 · 지운 판 · 옮긴 판: 칠하기 · 산출 제외가 «적을 때 하나만»으로 다루는 넷). 판 자리 · 쪼갬 · 겹침은 판 일.
    /// </summary>
    public bool Painted => Patches.Any(p => p.FaceKindLead.Length > 0 || p.KindLead.Length > 0 || p.DropLead.Length > 0 || p.MoveLead.Length > 0);

    /// <summary>아래를 보는 면(밑면 · 아래 경사) — 레벨에 꼭 맞으면 아래 층(위치 나무).</summary>
    public bool Down => Face.Class is FaceClass.Bottom or FaceClass.SlopeDown || (Face.Normal is { Length: 3 } n && n[2] < -0.3);

    /// <summary>가운데(모델 좌표 m) · 높이 범위.</summary>
    public Vec3 Center { get; }
    public double ZMin { get; }
    public double ZMax { get; }
    public double ZMid => (ZMin + ZMax) / 2;

    public override string ToString() => $"면 {Index} {Result.Element.Id} {Name} {Area:0.##}㎡";
}

/// <summary>
/// 면을 감싸는 네모(R73a — 면 위 치수 · 계획 §3.6): 면 평면의 두 축(U = 가로 — 서 있는 면은 수평 · 누운 면은 가장 긴 변 · V = 세로)으로 잰 가로 × 세로와
/// 네 귀(모델 좌표 · U0V0 → U1V0 → U1V1 → U0V1) · 채운 비율(넓이 ÷ 네모 — 반 넘게 못 채우면 화면이 «≈»).
/// </summary>
public sealed record FaceBox(Vec3 Normal, Vec3 U, Vec3 V, double Width, double Height, double Fill, IReadOnlyList<Vec3> Corners);

public sealed partial class FormworkBoard
{
    private List<BoardFace>? _faces;
    private Dictionary<FacePatch, BoardFace>? _faceOf;
    private Dictionary<long, List<BoardFace>>? _facesOfElement;

    /// <summary>
    /// 면 목록(R73a): 부재마다 · 면마다 · 덩이마다 하나 — 모든 조각이 꼭 한 면에 든다. 덩이를 다시 지을 때(Refresh) 다시 짓는다(조각은 그대로라 같은 면을 다시 찾을 수 있다).
    /// </summary>
    public IReadOnlyList<BoardFace> Faces
    {
        get
        {
            EnsureFaces();
            return _faces!;
        }
    }

    /// <summary>마지막으로 면 목록을 지은 시간(ms — 일지 · 계획 §5).</summary>
    public long FacesMilliseconds { get; private set; }

    /// <summary>그 조각이 든 면(없으면 null).</summary>
    public BoardFace? FaceOf(FacePatch patch)
    {
        EnsureFaces();
        return _faceOf!.TryGetValue(patch, out var face) ? face : null;
    }

    /// <summary>그 덩이 겉면의 그 세모가 든 면(3D 찍기 — 없으면 null).</summary>
    public BoardFace? FaceAt(int piece, int triangle) => PatchAt(piece, triangle) is { } patch ? FaceOf(patch) : null;

    /// <summary>그 덩이에 든 면들(판 전체 — 카드 머리 · 둘째 테두리).</summary>
    public IEnumerable<BoardFace> FacesOfPiece(int piece) => piece < 0 ? Enumerable.Empty<BoardFace>() : Faces.Where(f => f.Piece == piece);

    /// <summary>그 부재의 면들(면 차례).</summary>
    public IReadOnlyList<BoardFace> FacesOfElement(long id)
    {
        EnsureFaces();
        return _facesOfElement!.TryGetValue(id, out var list) ? list : Array.Empty<BoardFace>();
    }

    /// <summary>
    /// 형상을 다시 잰 뒤(R67) 그 열쇠의 면(고른 면을 다시 잡을 때 — 계획 §3.1): 같은 부재 · 입체 · 면 번호의 면 가운데 대표 조각 열쇠(대표 점)를 품는 것 · 없으면 그 면의 첫 면.
    /// </summary>
    public BoardFace? FaceNear(DropRecord record, int solid, int face)
    {
        BoardFace? first = null;
        foreach (var f in FacesOfElement(record.Element))
        {
            if (f.Face.Solid != solid || f.Face.Face != face) continue;
            if (f.Patches.Any(p => record.Matches(f.Face, p))) return f;
            first ??= f;
        }
        return first;
    }

    /// <summary>
    /// 면 하나의 겉면(R73a6 — 고른 면 분홍 테두리 · 마우스 밑 굵은 테두리 · JACK 1102 «안 헷갈리게 해»): 세모는 조각마다(PatchMesh 와 같은 띄움) ·
    /// 테두리는 조각들을 면 평면에서 합친 모양 하나(받침 높이로 나눈 조각 사이 이음매는 없고 진짜 구멍은 남는다 — UnionOutline). 보기만 — 조각 · 넓이 · 기록 그대로.
    /// 조각이 하나뿐이거나 합칠 수 없으면(곡면 · 고리 없음) PatchMesh 그대로. R73a7: 한 면처럼 볼 면들(Siblings — 단마다 따로인 계단 옆면)을 함께 · 테두리 하나.
    /// </summary>
    public MeshData FaceMesh(BoardFace face)
    {
        var patches = face.Siblings.Count > 1 ? face.Siblings.SelectMany(f => f.Patches).ToList() : face.Patches;
        if (patches.Count < 2 || face.Face.Normal is not { Length: 3 } normal || UnionOutline(patches, normal) is not { Count: > 0 } outline) return PatchMesh(patches);
        return MeshOf(patches, _origin, new FacePatch { Outline = outline }, normal).mesh;
    }

    /// <summary>
    /// 조각들의 테두리를 면 평면(법선 normal)에서 합친 고리들(R73a6 — 바깥 + 구멍 · 모델 좌표): 조각마다 짝홀로 정리한 뒤 합치고, 1 mm 안의 틈(조각 사이 실 같은 틈)은 메운다.
    /// 고리가 없으면 빈 목록.
    /// </summary>
    public static List<List<double[]>> UnionOutline(IEnumerable<FacePatch> patches, double[] normal)
    {
        var result = new List<List<double[]>>();
        var first = patches.SelectMany(p => p.Outline).SelectMany(r => r).FirstOrDefault(q => q.Length >= 3);
        if (first == null || normal is not { Length: 3 }) return result;
        var frame = Frame.Of(V3.Of(first), V3.Of(normal));
        var all = new Clipper2Lib.Paths64();
        foreach (var patch in patches)
        {
            var rings = new Clipper2Lib.Paths64();
            foreach (var ring in patch.Outline.Where(r => r.Count >= 3))
            {
                var path = new Clipper2Lib.Path64(ring.Count);
                foreach (var q in ring) { var (u, v) = frame.To2D(V3.Of(q)); path.Add(Poly.P(u, v)); }
                rings.Add(path);
            }
            if (rings.Count > 0) all.AddRange(Poly.Normalize(rings));
        }
        if (all.Count == 0) return result;
        const double Seal = 0.0005 * Poly.Scale;   // 0.5 mm 부풀렸다 줄임 = 1 mm 안 틈 메움(네모 귀는 그대로 — 뾰족 이음)
        var merged = Clipper2Lib.Clipper.Union(all, Clipper2Lib.FillRule.NonZero);
        merged = Clipper2Lib.Clipper.InflatePaths(merged, Seal, Clipper2Lib.JoinType.Miter, Clipper2Lib.EndType.Polygon);
        merged = Clipper2Lib.Clipper.InflatePaths(merged, -Seal, Clipper2Lib.JoinType.Miter, Clipper2Lib.EndType.Polygon);
        foreach (var path in Clipper2Lib.Clipper.SimplifyPaths(merged, 1))
        {
            if (path.Count < 3) continue;
            result.Add(path.Select(p => { var w = frame.To3D(p.X / Poly.Scale, p.Y / Poly.Scale); return new[] { w.X, w.Y, w.Z }; }).ToList());
        }
        return result;
    }

    /// <summary>조각 안의 대표 점(모델 좌표 — 넓이 가운데가 조각 밖이면 가장 큰 세모의 가운데 · R73a6 화면이 그 면이 보이는 방향을 찾을 때 시선을 쏘는 자리).</summary>
    public static Vec3 AnchorOf(FacePatch patch)
    {
        var (x, y, z) = PatchGeometry.Anchor(patch);
        return new Vec3(x, y, z);
    }

    /// <summary>
    /// 부재 겉면 위의 점(모델 좌표)이 든 면(R73a6 — ② 오른쪽 «전체 모델» 그림은 다른 부재를 판 없이 부재 겉면으로만 찍는다): 시선(direction)을 마주한 면 가운데
    /// 평면에서 2 cm 안이고 · 조각 테두리 안(SpotEdge 여유)이면 그 조각의 면 · 아니면 맞닿은 · 깎인 · 무시한 자리 고리 안이면 그 면(덩이에 든 면 먼저) · 없으면 null.
    /// 여럿이면 평면이 가장 가까운 것.
    /// </summary>
    public BoardFace? FaceAtPoint(long elementId, Vec3 point, Vec3 direction)
    {
        const double PlaneGap = 0.02;
        var dir = direction.Length > 1e-12 ? direction.Unit() : Vec3.Zero;
        BoardFace? best = null, bare = null;
        double bestGap = double.MaxValue, bareGap = double.MaxValue;
        bool bestPatch = false;
        foreach (var f in FacesOfElement(elementId))
        {
            if (f.Face.Normal is not { Length: 3 } nn) continue;
            var n = new Vec3(nn[0], nn[1], nn[2]);
            if (dir.Length > 0 && n.Dot(dir) > -0.02) continue;                // 뒤를 보는 면
            double gap = Math.Abs(n.Dot(point) - f.Face.PlaneD);
            if (gap > PlaneGap) continue;
            var covers = f.Face.Covers.Concat(f.Face.Cut).Concat(f.Face.Ignored).ToList();
            // 테두리가 없는 면(형상이 정해 세지 않는 윗면 등 — 조각에 고리가 없다)은 평면만으로(다른 면이 품지 않을 때).
            if (f.Patches.All(p => p.Outline.Count == 0) && covers.All(c => c.Outline.Count == 0))
            {
                if (gap < bareGap) (bare, bareGap) = (f, gap);
                continue;
            }
            bool onPatch = f.Patches.Any(p => Inside(p.Outline, point, n));
            bool onCover = !onPatch && covers.Any(c => Inside(c.Outline, point, n));
            if (!onPatch && !onCover) continue;
            // 조각 안 > 고리 안(같은 FaceResult 를 덩이마다 나눈 면 — 고리 안이면 덩이에 든 면) > 평면이 가까운 것.
            bool better = best == null || (onPatch && !bestPatch) || (onPatch == bestPatch && (gap < bestGap - 1e-6 || (Math.Abs(gap - bestGap) <= 1e-6 && best.Piece < 0 && f.Piece >= 0)));
            if (better) (best, bestGap, bestPatch) = (f, gap, onPatch);
        }
        return best ?? bare;
    }

    private void EnsureFaces()
    {
        if (_faces != null) return;
        var watch = Stopwatch.StartNew();
        var pieceOf = new Dictionary<FacePatch, int>(ReferenceEqualityComparer.Instance);
        for (int i = 0; i < Pieces.Count; i++)
            foreach (var p in Pieces[i].Patches) pieceOf[p] = i;
        var faces = new List<BoardFace>();
        var faceOf = new Dictionary<FacePatch, BoardFace>(ReferenceEqualityComparer.Instance);
        var byElement = new Dictionary<long, List<BoardFace>>();
        foreach (var r in Results)
        {
            if (!byElement.TryGetValue(r.Element.Id, out var mine)) byElement[r.Element.Id] = mine = new List<BoardFace>();
            int start = mine.Count;
            foreach (var f in r.Faces)
            {
                // 면 안의 조각을 든 덩이마다 묶는다(조각의 차례 그대로 — 대개 덩이 하나).
                var groups = new List<(int piece, List<FacePatch> patches)>();
                foreach (var p in f.Patches)
                {
                    int at = pieceOf.TryGetValue(p, out int i) ? i : -1;
                    int g = groups.FindIndex(x => x.piece == at);
                    if (g < 0) groups.Add((at, new List<FacePatch> { p }));
                    else groups[g].patches.Add(p);
                }
                foreach (var (piece, patches) in groups)
                {
                    var face = new BoardFace(faces.Count, r, f, piece, patches);
                    faces.Add(face);
                    mine.Add(face);
                    foreach (var p in patches) faceOf[p] = face;
                }
            }
            var own = mine.Skip(start).ToList();
            // R73a6: 다른 부재가 다 덮어 조각이 없는 면 — 원본 면 모양의 보기용 조각 하나로 «다 덮인 면»(빗금 · 고르기 · 도구가 쓴다 · 수량 · 기록과 상관없음).
            // 이름은 거푸집 면의 차례 번호를 흔들지 않게 따로 짓고 «(전부 공제)»를 붙인다.
            // R73a7(JACK 1102 «드레인 피트 상부는 빗금 처리하고 하부슬래브 표현 · 바닥슬래브 위에 벽체 등 — 왜 어떤 건 표현되고 어떤 건 안 되는 거야»): 윗면도.
            var covered = new List<BoardFace>();
            foreach (var f in r.Faces)
            {
                if (f.Patches.Count > 0 || f.Covers.Concat(f.Cut).Concat(f.Ignored).Sum(c => c.Area) < JointChipMin || CoveredPatch(f) is not { } patch) continue;
                var face = new BoardFace(faces.Count, r, f, -1, new List<FacePatch> { patch }) { Covered = true };
                faces.Add(face);
                mine.Add(face);
                covered.Add(face);
                faceOf[patch] = face;
            }
            // R73b(JACK 1103 «쪼개지지 않게»): 한 면처럼 볼 면 = 같은 평면에서 맞닿은 면 모두(판 · 세지 않는 조각 · 다 덮인 면 상관없이) — 무리를 먼저 짓고 이름을 붙인다.
            Join(own.Concat(covered).ToList());
            FaceNames.Assign(r, own);
            if (covered.Count > 0)
            {
                FaceNames.Assign(r, covered);
                foreach (var face in covered.Where(f => ReferenceEquals(f.Siblings[0], f) || f.Siblings[0].Covered)) face.Name += "(전부 공제)";
            }
            foreach (var face in own.Concat(covered)) if (!ReferenceEquals(face.Siblings[0], face)) face.Name = face.Siblings[0].Name;
        }
        _faces = faces;
        _faceOf = faceOf;
        _facesOfElement = byElement;
        FacesMilliseconds = watch.ElapsedMilliseconds;
    }

    /// <summary>한 면처럼 볼 면의 평면 거리 여유(m — R73a7 · 1 mm).</summary>
    public const double SamePlaneGap = 0.001;

    // 한 부재의 면들을 한 면처럼 볼 무리로 묶는다(R73a7 — BoardFace.Siblings): 같은 평면(법선 1e-6 · 거리 SamePlaneGap) · 조각 테두리가 맞닿거나 겹침(SpotEdge 여유).
    // R73b(JACK 1103 «계단 옆면처럼 그런 경우의 원인을 찾아서 쪼개지지 않게 · 비단 계단만 그런 일이 벌어지진 않을 거잖아»): 판이 달라도 · 판이 없어도(좁은 틈 자투리 · 땅 위 ·
    // 확인할 자리) · 다 덮인 면이어도 묶는다 — 한 평면의 맞닿은 자리는 한 면(벽 내측면 + 개구부 옆 띠 · 계단 밑면 + 틈 자투리 · 기초 하부면 받침 높이 둘).
    // 보 사이 칸처럼 떨어진 자리는 묶지 않는다. Revit 이 나눠 준 원본 면은 분석이 이미 한 면으로 합쳤다(FormworkAnalyzer.MergeSplitFaces). 무리 차례 = 면 차례.
    private static void Join(IReadOnlyList<BoardFace> faces)
    {
        var parent = Enumerable.Range(0, faces.Count).ToArray();
        int Root(int i) { while (parent[i] != i) i = parent[i] = parent[parent[i]]; return i; }
        static List<List<double[]>> Rings(BoardFace f) => f.Patches.SelectMany(p => p.Outline).Where(r => r.Count >= 3).ToList();
        for (int i = 0; i < faces.Count; i++)
        {
            var a = faces[i];
            if (a.Face.Normal is not { Length: 3 } na) continue;
            var n = new Vec3(na[0], na[1], na[2]);
            List<List<double[]>>? ra = null;
            for (int j = i + 1; j < faces.Count; j++)
            {
                var b = faces[j];
                if (Root(i) == Root(j)) continue;
                if (b.Face.Normal is not { Length: 3 } nb || n.Dot(new Vec3(nb[0], nb[1], nb[2])) < 1 - 1e-6 || Math.Abs(a.Face.PlaneD - b.Face.PlaneD) > SamePlaneGap) continue;
                ra ??= Rings(a);
                var rb = Rings(b);
                if (ra.Count == 0 || rb.Count == 0) continue;
                if (ra.SelectMany(r => r).Any(q => Inside(rb, V(q), n)) || rb.SelectMany(r => r).Any(q => Inside(ra, V(q), n))) parent[Root(j)] = Root(i);
            }
        }
        foreach (var g in Enumerable.Range(0, faces.Count).GroupBy(Root).Where(g => g.Count() > 1))
        {
            var group = g.OrderBy(i => i).Select(i => faces[i]).ToList();
            foreach (var f in group) f.Siblings = group;
        }
        static Vec3 V(double[] q) => new(q[0], q[1], q[2]);
    }

    // 다 덮인 면의 보기용 조각(R73a6): 원본 면 고리(바깥 + 구멍)와 그 세모(면 평면에서 — 바깥을 보게 돈다) · 넓이 0 · 세지 않음 · 정한 것(확인할 자리 아님).
    private readonly Dictionary<FaceResult, FacePatch?> _coveredPatches = new(ReferenceEqualityComparer.Instance);
    private IReadOnlyList<ElementFormwork>? _coveredFor;

    private FacePatch? CoveredPatch(FaceResult f)
    {
        // 같은 면 결과면 같은 조각(설치 전 검토 낮음 — 판을 다시 지을 때마다 새 조각이면 고른 다 덮인 면이 열쇠를 잃어 풀렸다) · 형상을 다시 재면 새로.
        if (!ReferenceEquals(_coveredFor, Results)) { _coveredPatches.Clear(); _coveredFor = Results; }
        if (_coveredPatches.TryGetValue(f, out var made)) return made;
        return _coveredPatches[f] = MakeCoveredPatch(f);
    }

    private FacePatch? MakeCoveredPatch(FaceResult f)
    {
        if (FacePolygon(f) is not { } pg) return null;
        var outline = pg.paths.Select(path => path.Select(q => { var w = pg.frame.To3D(q.X / Poly.Scale, q.Y / Poly.Scale); return new[] { w.X, w.Y, w.Z }; }).ToList()).ToList();
        var triangles = Poly.Triangles(pg.paths).Select(t => { var w = pg.frame.To3D(t.u, t.v); return new[] { w.X, w.Y, w.Z }; }).ToList();
        if (triangles.Count < 3) return null;
        return new FacePatch { Area = 0, Counted = false, Decided = true, Outline = outline, Triangles = triangles, Note = "다른 부재와 접합" };
    }

    /// <summary>
    /// 면을 감싸는 네모(면 위 치수 — R73a). 곡면(법선 없음) · 테두리가 없는 면은 null. 서 있는 면의 가로 = 수평 · 세로 = 위 ·
    /// 누운 면의 가로 = 가장 긴 변 방향 · 경사면의 가로 = 그 면의 수평 방향.
    /// </summary>
    public static FaceBox? BoxOf(BoardFace face) => BoxOf(new[] { face });

    /// <summary>한 면처럼 볼 면들(R73a7 — Siblings · 같은 평면)을 함께 감싸는 네모 · 채운 비율은 넓이를 더해서. 첫 면의 법선.</summary>
    public static FaceBox? BoxOf(IReadOnlyList<BoardFace> faces)
    {
        if (faces.Count == 0 || faces[0].Face.Normal is not { Length: 3 } nn) return null;
        var n = new Vec3(nn[0], nn[1], nn[2]).Unit();
        var patches = faces.SelectMany(f => f.Patches).ToList();
        var points = patches.SelectMany(p => p.Outline).SelectMany(r => r).Where(q => q.Length >= 3).Select(q => new Vec3(q[0], q[1], q[2])).ToList();
        if (points.Count < 3 || n.Length < 0.5) return null;
        Vec3 u;
        if (Math.Abs(n.Z) < 0.9) u = Vec3.UnitZ.Cross(n).Unit();   // 서 있는 면 · 경사면: 수평
        else
        {
            // 누운 면: 가장 긴 변(면 위로 누른 것)
            u = Vec3.Zero;
            double longest = 0;
            foreach (var ring in patches.SelectMany(p => p.Outline))
                for (int i = 0; i < ring.Count; i++)
                {
                    double[] a = ring[i], b = ring[(i + 1) % ring.Count];
                    if (a.Length < 3 || b.Length < 3) continue;
                    var e = new Vec3(b[0] - a[0], b[1] - a[1], b[2] - a[2]);
                    e -= n * e.Dot(n);
                    if (e.Length > longest) { longest = e.Length; u = e.Unit(); }
                }
            if (longest < 1e-9) return null;
        }
        var v = n.Cross(u).Unit();
        if (v.Z < -1e-9 || (Math.Abs(v.Z) <= 1e-9 && v.Y < 0)) { u = -u; v = -v; }   // 세로는 위(누운 면은 북)를 보게
        var o = points[0];
        double u0 = double.MaxValue, u1 = double.MinValue, v0 = double.MaxValue, v1 = double.MinValue;
        foreach (var p in points)
        {
            var d = p - o;
            double du = d.Dot(u), dv = d.Dot(v);
            u0 = Math.Min(u0, du); u1 = Math.Max(u1, du); v0 = Math.Min(v0, dv); v1 = Math.Max(v1, dv);
        }
        double w = u1 - u0, h = v1 - v0;
        if (!(w > 1e-6) || !(h > 1e-6)) return null;
        Vec3 At(double a, double b) => o + u * a + v * b;
        return new FaceBox(n, u, v, w, h, faces.Sum(f => f.Area) / (w * h), new[] { At(u0, v0), At(u1, v0), At(u1, v1), At(u0, v1) });
    }
}
