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

    /// <summary>쉬운 이름(FaceNames — «바깥 면» · «안쪽 면(수조부)» · «끝면 1» · «밑면» …).</summary>
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
    public bool Direct => Patches.Any(p => p.DropLead.Length > 0 || p.MoveLead.Length > 0 || p.KindLead.Length > 0 || p.PlaceLead.Length > 0 || p.SplitLead.Length > 0);

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
            FaceNames.Assign(r, mine.Skip(start).ToList());
        }
        _faces = faces;
        _faceOf = faceOf;
        _facesOfElement = byElement;
        FacesMilliseconds = watch.ElapsedMilliseconds;
    }

    /// <summary>
    /// 면을 감싸는 네모(면 위 치수 — R73a). 곡면(법선 없음) · 테두리가 없는 면은 null. 서 있는 면의 가로 = 수평 · 세로 = 위 ·
    /// 누운 면의 가로 = 가장 긴 변 방향 · 경사면의 가로 = 그 면의 수평 방향.
    /// </summary>
    public static FaceBox? BoxOf(BoardFace face)
    {
        if (face.Face.Normal is not { Length: 3 } nn) return null;
        var n = new Vec3(nn[0], nn[1], nn[2]).Unit();
        var points = face.Patches.SelectMany(p => p.Outline).SelectMany(r => r).Where(q => q.Length >= 3).Select(q => new Vec3(q[0], q[1], q[2])).ToList();
        if (points.Count < 3 || n.Length < 0.5) return null;
        Vec3 u;
        if (Math.Abs(n.Z) < 0.9) u = Vec3.UnitZ.Cross(n).Unit();   // 서 있는 면 · 경사면: 수평
        else
        {
            // 누운 면: 가장 긴 변(면 위로 누른 것)
            u = Vec3.Zero;
            double longest = 0;
            foreach (var ring in face.Patches.SelectMany(p => p.Outline))
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
        return new FaceBox(n, u, v, w, h, face.Area / (w * h), new[] { At(u0, v0), At(u1, v0), At(u1, v1), At(u0, v1) });
    }
}
