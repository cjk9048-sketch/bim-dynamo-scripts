using System.Diagnostics;
using System.Globalization;
using Autodesk.Revit.DB;
using DH.Takeoff.Core;
using DH.Takeoff.Core.View3D;

namespace DH.Takeoff.Revit;

/// <summary>
/// 겹침 검사 결과 창의 3D 에 보여 줄 것을 모델에서 읽는다 — 콘크리트 부재의 겉면(둘레로 그릴 것까지 전부)과 쌍마다의 겹친 덩어리.
/// 모델은 바꾸지 않는다. Revit 의 형상 객체는 들고 있지 않는다(숫자 배열 MeshData 로 옮겨 넘긴다 — ZoneScene 과 같은 까닭).
///  · 부재 = 콘크리트 부재 전부(「위치·부위 정하기」와 같은 모으기) + 쌍에 든 부재. 선택한 부재만 검사했어도 둘레는 다 그린다.
///  · 부재의 겉면은 ZoneScene.Read 와 같은 규칙으로 만든다(ZoneScene.AddSolid · 세모를 못 만들면 경계상자 · 그것도 없으면 그리지 못함).
///    그 규칙을 여기 한 번 더 적은 까닭: 설치돼 쓰이고 있는 ZoneScene.Read 의 본문을 건드리지 않으려고(2026-10-14).
///  · 덩어리는 OverlapChecker.Find 가 쌍에 붙여 둔 것(OverlapPair.Blob)을 부재와 같은 '장면의 가운데'로 굳힌다.
/// 자가검증(일지에 남기고, 틀린 덩어리는 그리지 않는다): 덩어리는 두 부재의 경계상자가 겹친 자리 안에 있어야 한다 — 벗어나면 다른 쌍의 것이거나
/// 좌표가 어긋난 것이다. 만든 세모의 넓이 합은 Revit 이 준 겉넓이와 같아야 한다(1% 넘게 다르면 면이 빠진 것 — 그리기는 하고 일지에 남긴다).
/// </summary>
public static class OverlapScene
{
    private const double BoxSlackM = 0.001;       // 덩어리 상자가 두 부재 상자의 겹침을 이보다 더 벗어나면 그 쌍의 덩어리가 아니다
    private const double AreaTolerance = 0.01;    // 세모 넓이 합과 Revit 겉넓이가 이보다 더 다르면 일지에 남긴다

    public sealed class Data
    {
        public List<ElementId> Ids = new();                   // 부재(그림의 차례)
        public List<string> Names = new();                    // "카테고리 유형 (Id n)" — Ids 와 같은 차례
        public List<MeshData> Meshes = new();                 // 부재의 겉면 — Ids 와 같은 차례
        public List<MeshData?> Blobs = new();                 // 겹친 덩어리 — 넘겨받은 쌍과 같은 차례(그리지 않으면 null)
        public Vec3 Origin = Vec3.Zero;                       // 장면의 가운데(m, 모델 안쪽 좌표) — 겉면의 좌표에서 이것을 뺐다
        public int Boxed, Undrawn, ReadErrors;                // 경계상자로 그린 부재 · 그리지 못한 부재 · 형상을 읽다 오류가 난 부재
        public int FacesFallback, FacesSkipped, FacesTwisted, Triangles, Segments;
        public int BlobsDrawn, BlobTriangles, BlobBoxed;      // 그린 덩어리 · 그 세모 · 경계상자로 대신한 덩어리
        public int BlobMissing, BlobOutside, BlobAreaOff;     // 만들지 못한 덩어리 · 두 부재가 겹친 자리를 벗어나 그리지 않은 덩어리 · 넓이가 다른 덩어리
        public List<string> BlobLines = new();                // 쌍마다 한 줄(일지에 적는다)
        public long ReadMs;

        private readonly Dictionary<long, int> _at = new();

        /// <summary>그 부재의 그림에서의 차례(없으면 −1).</summary>
        public int IndexOf(ElementId id) => _at.TryGetValue(id.Value, out int at) ? at : -1;

        internal void Add(ElementId id, string name)
        {
            _at[id.Value] = Ids.Count;
            Ids.Add(id);
            Names.Add(name);
        }
    }

    /// <summary>묶음 제목에 쓰는 부위 이름 — 산출서와 같은 함수(넣어 둔 부위가 먼저, 없으면 종류로 붙인 이름).</summary>
    public static string PartOf(Document doc, Element el)
    {
        string type = (doc.GetElement(el.GetTypeId()) as ElementType)?.Name ?? el.Name;
        return CalcPlan.PartName(ZoneScene.Text(el, ZoneScene.PartParam), CalcSheetExporter.KindOf(el), type);
    }

    /// <param name="pairs">찾은 쌍(Find 에 withBlobs 를 준 것).</param>
    /// <param name="verify">
    /// 덩어리가 두 부재의 겹친 자리 안에 있는지 본다 — 찾은 직후(결합 전)에만 볼 수 있다: 결합하면 잘린 부재의 상자가 줄어, 결합 전에 잰 덩어리가 그 밖에 놓인다.
    /// 틀린 덩어리는 쌍에서도 떼어 둔다(결합 뒤에 다시 읽을 때도 그리지 않게).
    /// </param>
    public static Data Read(Document doc, IReadOnlyList<OverlapPair> pairs, bool verify)
    {
        var watch = Stopwatch.StartNew();
        var data = new Data();
        var counters = new ZoneScene.Data();

        var ids = new List<ElementId>();
        var seen = new HashSet<long>();
        foreach (var id in DimensionExtractor.CollectApplicable(doc))
        {
            var el = doc.GetElement(id);
            if (el == null) continue;
            if (ConcreteFilter.Classify(doc, el).Kind == ConcreteFilter.Kind.NotConcrete) continue;
            if (seen.Add(id.Value)) ids.Add(id);
        }
        // 쌍에 든 부재는 거르기와 상관없이 그림에 넣는다(대상을 고른 때와 지금 사이에 거르기의 답이 달라져도 쌍의 부재가 빠지지 않게).
        foreach (var pair in pairs)
            foreach (var id in new[] { pair.Winner, pair.Loser })
                if (doc.GetElement(id) != null && seen.Add(id.Value)) ids.Add(id);

        var builders = new List<MeshBuilder?>();
        var boxes = new List<Box3>();              // 부재의 경계상자(모델 안쪽 좌표 · m) — 덩어리의 자가검증에 쓴다
        var scene = Box3.Empty;
        foreach (var id in ids)
        {
            var el = doc.GetElement(id)!;
            var box = Box3.Empty;
            try
            {
                var bb = el.get_BoundingBox(null);
                if (bb != null) box = new Box3(ZoneScene.Meters(bb.Min), ZoneScene.Meters(bb.Max));
            }
            catch { }

            MeshBuilder? builder = null;
            try
            {
                var solids = SolidReader.Solids(el);
                if (solids.Count > 0)
                {
                    builder = new MeshBuilder();
                    foreach (var solid in solids) ZoneScene.AddSolid(builder, solid, counters);
                }
            }
            catch
            {
                builder = null;
                data.ReadErrors++;
            }
            // 세모를 하나도 못 만든 부재: 경계상자로라도 그린다. 경계상자도 없으면 그릴 수 없다.
            if (builder == null || builder.Triangles == 0)
            {
                builder = null;
                if (!box.IsEmpty && box.Size.Length > 1e-6)
                {
                    builder = ZoneScene.BoxBuilder(box);
                    data.Boxed++;
                }
                else data.Undrawn++;
            }
            data.Add(id, OverlapChecker.Name(doc, el));
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
        data.FacesFallback = counters.FacesFallback;

        for (int k = 0; k < pairs.Count; k++)
        {
            var pair = pairs[k];
            string head = string.Create(CultureInfo.InvariantCulture, $"쌍 {k}\t{pair.Winner.Value}\t{pair.Loser.Value}\t{pair.VolumeM3:0.######} m3\t");
            if (pair.Blob == null)
            {
                data.Blobs.Add(null);
                data.BlobMissing++;
                data.BlobLines.Add(head + "덩어리 없음" + (pair.BlobNote.Length > 0 ? "(" + pair.BlobNote + ")" : ""));
                continue;
            }
            var blob = pair.Blob.Build(data.Origin);
            if (blob.Triangles == 0)
            {
                data.Blobs.Add(null);
                data.BlobMissing++;
                data.BlobLines.Add(head + "덩어리 없음(세모 0)");
                continue;
            }
            string checks = "";
            if (verify)
            {
                int w = data.IndexOf(pair.Winner), l = data.IndexOf(pair.Loser);
                var allowed = w >= 0 && l >= 0 ? OverlapBoard.Intersect(boxes[w], boxes[l]) : Box3.Empty;
                if (w < 0 || l < 0 || boxes[w].IsEmpty || boxes[l].IsEmpty) checks = "\t자리 검증 못 함(부재의 경계상자 없음)";
                else
                {
                    var world = new Box3(blob.Box.Min + data.Origin, blob.Box.Max + data.Origin);
                    double off = allowed.IsEmpty ? double.PositiveInfinity : Outside(world, allowed);
                    if (off > BoxSlackM)
                    {
                        // 이 쌍의 덩어리가 아니다 — 그리지 않는다(엉뚱한 자리에 빨간 덩어리가 뜨는 것보다 없는 편이 낫다).
                        pair.Blob = null;
                        pair.BlobNote = string.Create(CultureInfo.InvariantCulture, $"두 부재가 겹친 자리를 {(double.IsInfinity(off) ? "완전히" : off.ToString("0.###") + " m")} 벗어나 그리지 않음");
                        data.Blobs.Add(null);
                        data.BlobOutside++;
                        data.BlobLines.Add(head + pair.BlobNote);
                        continue;
                    }
                    checks = string.Create(CultureInfo.InvariantCulture, $"\t자리 O(벗어남 {off:0.####} m)");
                }
            }
            if (!pair.BlobBoxed && double.IsFinite(pair.BlobAreaM2) && pair.BlobAreaM2 > 0)
            {
                double area = TriangleArea(blob);
                bool same = Math.Abs(area - pair.BlobAreaM2) <= AreaTolerance * pair.BlobAreaM2;
                if (!same) data.BlobAreaOff++;
                checks += string.Create(CultureInfo.InvariantCulture, $"\t넓이 {(same ? "O" : "X")}(세모 {area:0.####} / Revit {pair.BlobAreaM2:0.####} m2)");
            }
            data.Blobs.Add(blob);
            data.BlobsDrawn++;
            data.BlobTriangles += blob.Triangles;
            if (pair.BlobBoxed) data.BlobBoxed++;
            data.BlobLines.Add(head + (pair.BlobBoxed ? "경계상자로 그림" : $"세모 {blob.Triangles}") + checks);
        }
        data.ReadMs = watch.ElapsedMilliseconds;
        return data;
    }

    // inner 가 outer 를 가장 많이 벗어난 길이(m). 안에 있으면 0.
    private static double Outside(Box3 inner, Box3 outer)
    {
        double worst = 0;
        worst = Math.Max(worst, outer.Min.X - inner.Min.X);
        worst = Math.Max(worst, outer.Min.Y - inner.Min.Y);
        worst = Math.Max(worst, outer.Min.Z - inner.Min.Z);
        worst = Math.Max(worst, inner.Max.X - outer.Max.X);
        worst = Math.Max(worst, inner.Max.Y - outer.Max.Y);
        worst = Math.Max(worst, inner.Max.Z - outer.Max.Z);
        return worst;
    }

    private static double TriangleArea(MeshData mesh)
    {
        double area = 0;
        for (int i = 0; i + 2 < mesh.T.Length; i += 3)
        {
            Vec3 a = mesh.Vertex(mesh.T[i]), b = mesh.Vertex(mesh.T[i + 1]), c = mesh.Vertex(mesh.T[i + 2]);
            area += (b - a).Cross(c - a).Length / 2;
        }
        return area;
    }
}
