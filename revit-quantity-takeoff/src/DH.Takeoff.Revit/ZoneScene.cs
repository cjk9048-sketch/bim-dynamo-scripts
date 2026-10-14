using System.Diagnostics;
using Autodesk.Revit.DB;
using DH.Takeoff.Core;
using DH.Takeoff.Core.View3D;

namespace DH.Takeoff.Revit;

/// <summary>
/// 「위치·부위 정하기」 3D 팝업에 보여 줄 것을 모델에서 읽는다 — 콘크리트 부재(겉면의 세모 · 지금 적힌 위치와 부위), 레벨, 저장된 목록.
/// 모델은 바꾸지 않는다(레벨의 높이를 잴 때 연 트랜잭션은 되돌린다).
/// Revit 의 형상 객체는 들고 있지 않는다: 팝업이 떠 있는 동안 값을 넣을 때마다 트랜잭션이 커밋되는데, 요소가 바뀌면 앞서 받은 형상은
/// 못 쓰게 될 수 있다(Element.Geometry 문서) — 그래서 겉면을 숫자 배열(MeshData)로 옮겨 넘긴다.
/// </summary>
public static class ZoneScene
{
    private const double FtToM = 0.3048;
    private const double Ft3ToM3 = FtToM * FtToM * FtToM;
    public const string PartParam = "DH_Part";

    public sealed class Data
    {
        public List<PickMember> Members = new();
        public List<MeshData> Meshes = new();                 // Members 와 같은 차례
        public List<ZoneLevel> Levels = new();
        public ZonePlanStore.Loaded Stored = new();
        public Vec3 Origin = Vec3.Zero;                           // 장면의 가운데(m, 모델 안쪽 좌표) — 겉면의 좌표에서 이것을 뺐다
        public string LevelNotice = "";                       // 층으로 걸러 볼 수 없는 까닭("" = 볼 수 있음)
        public int Boxed;                                     // 세모를 만들지 못해 경계상자로 그린 부재
        public int Undrawn;                                   // 형상도 경계상자도 없어 그리지 못한 부재(찍을 수 없다)
        public int ReadErrors;                                // 형상을 읽다 오류가 난 부재
        public int FacesFallback;                             // Revit 이 세모를 주지 않아 우리 띠 나누기로 만든 면
        public int FacesSkipped;                              // 세모로 만들지 못한 면
        public int FacesTwisted;                              // 테두리가 스스로 엇갈린 면(띠 나누기에서)
        public int Triangles, Segments;
        public long ReadMs;
    }

    /// <summary>대상 = 콘크리트 부재 전부(산출식·거푸집과 같은 거르기: 무슨 재료인지 알 수 없는 부재는 빼지 않는다).</summary>
    public static Data Read(Document doc)
    {
        var watch = Stopwatch.StartNew();
        var data = new Data { Stored = ZonePlanStore.Read(doc) };
        ReadLevels(doc, data);

        var builders = new List<MeshBuilder?>();
        var scene = Box3.Empty;
        var boxes = new List<Box3>();
        foreach (var id in DimensionExtractor.CollectApplicable(doc))
        {
            var el = doc.GetElement(id);
            if (el == null) continue;
            if (ConcreteFilter.Classify(doc, el).Kind == ConcreteFilter.Kind.NotConcrete) continue;

            var box = Box3.Empty;
            try
            {
                var bb = el.get_BoundingBox(null);
                if (bb != null) box = new Box3(Meters(bb.Min), Meters(bb.Max));
            }
            catch { }

            MeshBuilder? builder = null;
            double volume = 0;
            try
            {
                var solids = SolidReader.Solids(el);
                if (solids.Count > 0)
                {
                    builder = new MeshBuilder();
                    foreach (var solid in solids) AddSolid(builder, solid, data);
                    try { volume = solids.Sum(s => s.Volume) * Ft3ToM3; } catch { }
                }
            }
            catch
            {
                builder = null;
                data.ReadErrors++;
            }
            // 세모를 하나도 못 만든 부재: 경계상자로라도 그린다(찍을 수 있어야 값을 넣는다). 경계상자도 없으면 그릴 수 없다.
            if (builder == null || builder.Triangles == 0)
            {
                builder = null;
                if (!box.IsEmpty && box.Size.Length > 1e-6)
                {
                    builder = BoxBuilder(box);
                    data.Boxed++;
                }
                else data.Undrawn++;
            }

            string type = (doc.GetElement(el.GetTypeId()) as ElementType)?.Name ?? el.Name;
            var kind = CalcSheetExporter.KindOf(el);
            data.Members.Add(new PickMember
            {
                Id = id.Value,
                Uid = el.UniqueId ?? "",
                Category = el.Category?.Name ?? "",
                Kind = kind,
                Type = type,
                AutoPart = CalcPlan.KindName(kind, type),
                ZMin = box.IsEmpty ? 0 : box.Min.Z,
                ZMax = box.IsEmpty ? 0 : box.Max.Z,
                Volume = volume,
                Drawn = builder != null,
                Locked = FormulaAutoFill.WhyNotEditable(doc, el) ?? "",
                Value = PlaceStamp.ValueOf(el),
            });
            builders.Add(builder);
            boxes.Add(box);
            if (builder != null) scene = scene.With(box);
        }

        // 장면의 가운데를 뺀 좌표로 굳힌다(측량 좌표처럼 큰 수는 그림에서 떨린다). 가운데는 m 단위로 끊는다.
        if (!scene.IsEmpty)
        {
            var c = scene.Center;
            data.Origin = new Vec3(Math.Round(c.X), Math.Round(c.Y), Math.Round(c.Z));
        }
        foreach (var builder in builders)
        {
            var mesh = builder?.Build(data.Origin) ?? new MeshData();
            data.Meshes.Add(mesh);
            data.Triangles += mesh.Triangles;
            data.Segments += mesh.Segments;
            data.FacesSkipped += mesh.Skipped;
            data.FacesTwisted += mesh.Twisted;
        }
        data.ReadMs = watch.ElapsedMilliseconds;
        return data;
    }

    internal static Vec3 Meters(XYZ p) => new(p.X * FtToM, p.Y * FtToM, p.Z * FtToM);

    // 입체 하나의 겉면: 면마다 Revit 이 주는 세모(Face.Triangulate — 구멍 · 굽은 면 포함)를 받고, 테두리는 모서리 선분으로 넣는다.
    // 세모를 받지 못한 평면 면은 테두리로 직접 세모를 만든다(Core 의 띠 나누기).
    internal static void AddSolid(MeshBuilder builder, Solid solid, Data data)
    {
        foreach (Face face in solid.Faces)
        {
            Vec3? flat = face is PlanarFace planar ? new Vec3(planar.FaceNormal.X, planar.FaceNormal.Y, planar.FaceNormal.Z) : null;

            // 테두리(모서리 선): 면마다 넣는다 — 모서리 하나가 맞닿은 두 면에 한 번씩 들어가는 것이 맞다(선은 저마다 제 면 위에 얹힌다).
            var loops = new List<IReadOnlyList<Vec3>>();
            try
            {
                foreach (EdgeArray loop in face.EdgeLoops)
                {
                    var ring = new List<Vec3>();
                    bool whole = true;      // 이 고리의 모서리를 모두 읽었다(하나라도 못 읽으면 닫히지 않으므로 세모를 만드는 데 쓰지 않는다)
                    foreach (Edge edge in loop)
                    {
                        // 모서리 하나를 못 읽어도 나머지 모서리는 그린다.
                        IList<XYZ> points;
                        try { points = PointsOf(edge.AsCurveFollowingFace(face)); }
                        catch { points = Array.Empty<XYZ>(); }
                        if (points.Count < 2) { whole = false; continue; }
                        // 굽은 면이면 점마다 그 자리의 법선을 구한다(면 위로 내려 긋는 계산이라 값이 싸지 않다 — 점마다 한 번만).
                        Vec3 from = Meters(points[0]), fromNormal = flat ?? NormalAt(face, points[0]);
                        for (int i = 1; i < points.Count; i++)
                        {
                            Vec3 to = Meters(points[i]), toNormal = flat ?? NormalAt(face, points[i]);
                            builder.AddSegment(from, to, fromNormal, toNormal);
                            ring.Add(from);
                            (from, fromNormal) = (to, toNormal);
                        }
                    }
                    if (whole && ring.Count >= 3) loops.Add(ring);
                }
            }
            catch { /* 테두리를 못 읽어도 세모는 그린다 */ }

            bool done = false;
            try
            {
                var mesh = face.Triangulate();
                if (mesh != null && mesh.NumTriangles > 0)
                {
                    var vertices = mesh.Vertices.Select(Meters).ToList();
                    var triangles = new List<int>(mesh.NumTriangles * 3);
                    for (int i = 0; i < mesh.NumTriangles; i++)
                    {
                        var t = mesh.get_Triangle(i);
                        triangles.Add((int)t.get_Index(0));
                        triangles.Add((int)t.get_Index(1));
                        triangles.Add((int)t.get_Index(2));
                    }
                    builder.AddIndexed(vertices, triangles);
                    done = true;
                }
            }
            catch { }
            if (done) continue;
            if (flat is { } normal && loops.Count > 0)
            {
                // AddPlanarFace 는 테두리 선분도 넣는데, 위에서 이미 넣었다 — 세모만 얻으려고 따로 만든 그릇에서 세모를 옮긴다.
                var spare = new MeshBuilder();
                if (spare.AddPlanarFace(loops, normal) > 0)
                {
                    var made = spare.Build(Vec3.Zero);
                    var vertices = new List<Vec3>(made.P.Length / 3);
                    for (int i = 0; i < made.P.Length / 3; i++) vertices.Add(made.Vertex(i));
                    builder.AddIndexed(vertices, made.T);
                    data.FacesFallback++;
                    continue;
                }
            }
            builder.Skip();
        }
    }

    // 모서리 곡선을 잘게 나눈 점들. 끝이 있는 곡선은 Revit 이 나눠 주고, 끝이 없는 닫힌 곡선(모서리 하나가 원 전체 — 둥근 바닥판의 테두리)은
    // 한 바퀴를 48 토막으로 나눈다(Tessellate 는 끝이 있는 곡선에만 쓴다 — SolidReader 와 같은 가름).
    private static IList<XYZ> PointsOf(Curve curve)
    {
        if (curve.IsBound) return curve.Tessellate();
        if (!curve.IsCyclic) return Array.Empty<XYZ>();
        const int pieces = 48;
        var points = new List<XYZ>(pieces + 1);
        for (int k = 0; k <= pieces; k++) points.Add(curve.Evaluate(k * curve.Period / pieces, false));
        return points;
    }

    // 굽은 면 위의 한 점에서 면의 바깥 방향(모서리 선을 면 위에 얹는 데 쓴다). 못 구하면 0(그리는 쪽이 어림한다).
    private static Vec3 NormalAt(Face face, XYZ point)
    {
        try
        {
            var hit = face.Project(point);
            if (hit == null) return Vec3.Zero;
            var n = face.ComputeNormal(hit.UVPoint);
            return new Vec3(n.X, n.Y, n.Z);
        }
        catch { return Vec3.Zero; }
    }

    // 경계상자 하나를 여섯 면의 겉면으로.
    internal static MeshBuilder BoxBuilder(Box3 box)
    {
        var builder = new MeshBuilder();
        Vec3 P(int k) => new((k & 1) == 0 ? box.Min.X : box.Max.X, (k & 2) == 0 ? box.Min.Y : box.Max.Y, (k & 4) == 0 ? box.Min.Z : box.Max.Z);
        void Face(Vec3 n, params int[] corners) => builder.AddPlanarFace(new[] { (IReadOnlyList<Vec3>)corners.Select(P).ToList() }, n);
        Face(new Vec3(0, 0, -1), 0, 2, 3, 1);
        Face(new Vec3(0, 0, 1), 4, 5, 7, 6);
        Face(new Vec3(0, -1, 0), 0, 1, 5, 4);
        Face(new Vec3(0, 1, 0), 2, 6, 7, 3);
        Face(new Vec3(-1, 0, 0), 0, 4, 6, 2);
        Face(new Vec3(1, 0, 0), 1, 3, 7, 5);
        return builder;
    }

    /// <summary>모든 부재의 값을 모델에서 다시 읽는다(적는 중 오류가 났을 때 화면과 모델을 다시 맞춘다).</summary>
    public static Dictionary<long, (PickValue value, string locked)> Reread(Document doc, IEnumerable<PickMember> members)
    {
        var now = new Dictionary<long, (PickValue, string)>();
        foreach (var m in members)
        {
            var el = doc.GetElement(new ElementId(m.Id));
            if (el == null) { now[m.Id] = (PickValue.None, "부재를 찾지 못함(지워짐)"); continue; }
            now[m.Id] = (PlaceStamp.ValueOf(el), FormulaAutoFill.WhyNotEditable(doc, el) ?? "");
        }
        return now;
    }

    /// <summary>부재에 지금 적혀 있는 글자 칸의 값(없으면 ""). 앞뒤 빈칸은 그대로 둔다.</summary>
    public static string Text(Element el, string name) =>
        el.GetParameters(name)
            .Where(p => p.StorageType == StorageType.String)
            .Select(p => p.AsString())
            .FirstOrDefault(s => !string.IsNullOrEmpty(s)) ?? "";

    // 레벨의 높이는 부재의 좌표(모델 안쪽 좌표)와 같아야 한다. Level.ProjectElevation 이 그 좌표인지는 문서로 확인되지 않는다
    // (프로젝트 기준점을 위아래로 옮긴 모델) — 그래서 직접 잰다(MeasureLevels). 재지 못했을 때는 프로젝트 기준점의 높이가 0 인
    // 모델에서만 ProjectElevation 을 쓴다(그때는 어느 뜻으로 읽어도 같다).
    private static void ReadLevels(Document doc, Data data)
    {
        List<Level> levels;
        try { levels = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().ToList(); }
        catch { levels = new List<Level>(); }
        if (levels.Count == 0) return;

        var heights = MeasureLevels(doc, levels);
        if (heights == null)
        {
            bool atOrigin = false;
            try { atOrigin = Math.Abs(BasePoint.GetProjectBasePoint(doc).Position.Z) < 1e-6; } catch { }
            if (atOrigin) heights = levels.ToDictionary(l => l.Id, l => l.ProjectElevation);
            else
            {
                data.LevelNotice = "레벨의 높이를 모델 좌표로 재지 못해 이번에는 층으로 걸러 볼 수 없습니다(종류 거르기와 숨기기는 됩니다).";
                return;
            }
        }
        foreach (var level in levels.OrderBy(l => heights[l.Id]))
            data.Levels.Add(new ZoneLevel(level.UniqueId ?? "", level.Name, heights[level.Id] * FtToM, level.Elevation * FtToM));
    }

    // 레벨마다 모델 안쪽 좌표의 높이(ft)를 잰다: 레벨 위에 스케치 평면을 잠깐 만들어 그 평면의 높이를 읽고 되돌린다(모델은 바뀌지 않는다).
    // 재지 못했거나 되돌리지 못했으면 null.
    private static Dictionary<ElementId, double>? MeasureLevels(Document doc, List<Level> levels)
    {
        try
        {
            var heights = new Dictionary<ElementId, double>();
            TransactionStatus status;
            using (var tx = new Transaction(doc, "DH 레벨 높이 재기(되돌림)"))
            {
                tx.Start();
                try
                {
                    foreach (var level in levels)
                        heights[level.Id] = SketchPlane.Create(doc, level.Id).GetPlane().Origin.Z;
                }
                finally { status = tx.RollBack(); }
            }
            // 되돌려졌는지 확인한다 — 되돌려지지 않았다면 잰 값을 쓰지 않는다(모델에 스케치 평면이 남았을 수 있음을 숨기지 않게 null).
            if (status != TransactionStatus.RolledBack) return null;
            return heights.Count == levels.Count && heights.Values.All(double.IsFinite) ? heights : null;
        }
        catch { return null; }
    }
}
