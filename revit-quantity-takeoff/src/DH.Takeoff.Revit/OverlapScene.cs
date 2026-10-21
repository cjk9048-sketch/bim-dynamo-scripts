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
        public int CutAway;                                   // 결합으로 다 깎여 입체가 없어 그리지 않은 부재(경계상자로 되살리지 않는다 — R51c revit-verify ①)
        public int FacesFallback, FacesSkipped, FacesTwisted, Triangles, Segments;
        public int BlobsDrawn, BlobTriangles, BlobBoxed;      // 그린 덩어리 · 그 세모 · 경계상자로 대신한 덩어리
        public int BlobMissing, BlobOutside, BlobAreaOff;     // 만들지 못한 덩어리 · 두 부재가 겹친 자리를 벗어나 그리지 않은 덩어리 · 넓이가 다른 덩어리
        public List<string> BlobLines = new();                // 쌍마다 한 줄(일지에 적는다)
        public long ReadMs;

        private readonly Dictionary<long, int> _at = new();
        private readonly Dictionary<int, List<long>> _slot = new();   // 칸 → 그 칸에 그리는 부재 전부(원래 부재 + 나눈 조각)

        /// <summary>그 부재의 그림에서의 차례(없으면 −1).</summary>
        public int IndexOf(ElementId id) => _at.TryGetValue(id.Value, out int at) ? at : -1;

        /// <summary>그 칸에 그리는 부재 전부(원래 부재 + Alias 로 이은 조각).</summary>
        public IReadOnlyList<ElementId> InSlot(int at) =>
            _slot.TryGetValue(at, out var ids) ? ids.Select(v => new ElementId(v)).ToList() : Array.Empty<ElementId>();

        /// <summary>
        /// 나눈 조각을 원래 부재의 칸에 잇는다(창이 떠 있는 동안 — 그림에는 원래 부재의 칸 하나에 조각 전부를 한 겉면으로 그린다).
        /// 이 칸을 가리키는 쌍의 줄은 그림에서 같은 부재로 칠해진다. 조각을 또 나누면 그 조각의 칸(= 원래 칸)에 잇는다 — 늘 평평하다.
        /// </summary>
        public void Alias(ElementId piece, int at)
        {
            if (at < 0 || !_slot.TryGetValue(at, out var ids)) return;
            _at[piece.Value] = at;
            if (!ids.Contains(piece.Value)) ids.Add(piece.Value);
        }

        internal void Add(ElementId id, string name)
        {
            _at[id.Value] = Ids.Count;
            _slot[Ids.Count] = new List<long> { id.Value };
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
    /// <param name="extra">그림에 꼭 넣을 부재(R54 결합 방향 쌍의 두 부재 — 거르기의 답과 상관없이).</param>
    public static Data Read(Document doc, IReadOnlyList<OverlapPair> pairs, bool verify, IEnumerable<ElementId>? extra = null)
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
        foreach (var id in extra ?? Enumerable.Empty<ElementId>())
            if (doc.GetElement(id) != null && seen.Add(id.Value)) ids.Add(id);

        var builders = new List<MeshBuilder?>();
        var boxes = new List<Box3>();              // 부재의 경계상자(모델 안쪽 좌표 · m) — 덩어리의 자가검증에 쓴다
        var scene = Box3.Empty;
        foreach (var id in ids)
        {
            var el = doc.GetElement(id)!;
            var (builder, box, how) = Member(el, counters);
            if (how == Drawn.ReadError) data.ReadErrors++;
            if (how == Drawn.CutAway) data.CutAway++;
            else if (builder == null) data.Undrawn++;
            else if (how != Drawn.Faces) data.Boxed++;
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
            int w = data.IndexOf(pairs[k].Winner), l = data.IndexOf(pairs[k].Loser);
            data.Blobs.Add(Blob(data, pairs[k], k, verify ? (w >= 0 ? boxes[w] : Box3.Empty, l >= 0 ? boxes[l] : Box3.Empty) : null));
        }
        data.ReadMs = watch.ElapsedMilliseconds;
        return data;
    }

    /// <summary>
    /// 다시 찾기(OverlapChecker.Refresh) 뒤: 그 쌍의 덩어리를 새로 굳혀 data.Blobs 의 k 번째에 둔다(k = 찾은 쌍의 차례 — 끝에 붙인 새 쌍이면 늘린다).
    /// Read 와 같은 규칙 · 같은 자가검증(두 부재가 겹친 자리는 지금 부재의 상자로 — 지금 모양으로 잰 덩어리다). 돌려주는 값 = 그 덩어리(그리지 않으면 null).
    /// </summary>
    public static MeshData? Reblob(Document doc, Data data, OverlapPair pair, int k)
    {
        while (data.Blobs.Count <= k) data.Blobs.Add(null);
        var blob = Blob(data, pair, k, (BoxOf(doc, pair.Winner), BoxOf(doc, pair.Loser)));
        data.Blobs[k] = blob;
        return blob;
    }

    /// <summary>
    /// 다시 찾기(OverlapChecker.Refresh)의 덩어리를 지금 두 부재의 상자로 자리 검증한다 — Read 의 자리 검증과 같은 잣대. 벗어나면 쌍에서 뗀다
    /// (R50 revit-verify ⑤: [우선순위대로 결합] 뒤에는 결합 전 덩어리라 자리 검증 없이 다시 읽는다 — 다시 찾을 때 걸러 두면 엉뚱한 자리의 덩어리가 그려지지 않는다).
    /// 상자를 읽지 못하면 그대로 둔다.
    /// </summary>
    public static void DropStrayBlob(Document doc, OverlapPair pair)
    {
        if (pair.Blob == null) return;
        var winner = BoxOf(doc, pair.Winner);
        var loser = BoxOf(doc, pair.Loser);
        if (winner.IsEmpty || loser.IsEmpty) return;
        var world = pair.Blob.Build(Vec3.Zero);
        if (world.Triangles == 0) return;
        var allowed = OverlapBoard.Intersect(winner, loser);
        double off = allowed.IsEmpty ? double.PositiveInfinity : Outside(world.Box, allowed);
        if (off <= BoxSlackM) return;
        pair.Blob = null;
        pair.BlobNote = string.Create(CultureInfo.InvariantCulture, $"두 부재가 겹친 자리를 {(double.IsInfinity(off) ? "완전히" : off.ToString("0.###") + " m")} 벗어나 그리지 않음");
    }

    // 부재의 경계상자(모델 안쪽 좌표 · m). 없거나 읽지 못하면 빈 상자.
    private static Box3 BoxOf(Document doc, ElementId id)
    {
        try { return doc.GetElement(id)?.get_BoundingBox(null) is { } bb ? new Box3(ZoneScene.Meters(bb.Min), ZoneScene.Meters(bb.Max)) : Box3.Empty; }
        catch { return Box3.Empty; }
    }

    // 쌍 하나의 덩어리를 장면의 가운데로 굳힌다(일지 줄 · 수를 data 에 남긴다). boxes = 자리 검증에 쓸 두 부재의 상자(null = 보지 않음).
    // 자리를 벗어난 덩어리는 쌍에서도 떼어 둔다(뒤에 다시 읽을 때도 그리지 않게).
    private static MeshData? Blob(Data data, OverlapPair pair, int k, (Box3 winner, Box3 loser)? boxes)
    {
        string head = string.Create(CultureInfo.InvariantCulture, $"쌍 {k}\t{pair.Winner.Value}\t{pair.Loser.Value}\t{pair.VolumeM3:0.######} m3\t");
        if (pair.Blob == null)
        {
            data.BlobMissing++;
            data.BlobLines.Add(head + "덩어리 없음" + (pair.BlobNote.Length > 0 ? "(" + pair.BlobNote + ")" : ""));
            return null;
        }
        var blob = pair.Blob.Build(data.Origin);
        if (blob.Triangles == 0)
        {
            data.BlobMissing++;
            data.BlobLines.Add(head + "덩어리 없음(세모 0)");
            return null;
        }
        string checks = "";
        if (boxes is { } pairBoxes)
        {
            var (winnerBox, loserBox) = pairBoxes;
            if (winnerBox.IsEmpty || loserBox.IsEmpty) checks = "\t자리 검증 못 함(부재의 경계상자 없음)";
            else
            {
                var allowed = OverlapBoard.Intersect(winnerBox, loserBox);
                var world = new Box3(blob.Box.Min + data.Origin, blob.Box.Max + data.Origin);
                double off = allowed.IsEmpty ? double.PositiveInfinity : Outside(world, allowed);
                if (off > BoxSlackM)
                {
                    // 이 쌍의 덩어리가 아니다 — 그리지 않는다(엉뚱한 자리에 빨간 덩어리가 뜨는 것보다 없는 편이 낫다).
                    pair.Blob = null;
                    pair.BlobNote = string.Create(CultureInfo.InvariantCulture, $"두 부재가 겹친 자리를 {(double.IsInfinity(off) ? "완전히" : off.ToString("0.###") + " m")} 벗어나 그리지 않음");
                    data.BlobOutside++;
                    data.BlobLines.Add(head + pair.BlobNote);
                    return null;
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
        data.BlobsDrawn++;
        data.BlobTriangles += blob.Triangles;
        if (pair.BlobBoxed) data.BlobBoxed++;
        data.BlobLines.Add(head + (pair.BlobBoxed ? "경계상자로 그림" : $"세모 {blob.Triangles}") + checks);
        return blob;
    }

    // 부재의 겉면을 어떻게 만들었나: 면의 세모로 · 세모를 못 만들어 경계상자로(또는 그것도 없음) · 형상을 읽다 오류(경계상자로, 또는 그것도 없음) ·
    // 결합으로 다 깎여 입체가 없음(그리지 않음).
    private enum Drawn { Faces, Boxed, ReadError, CutAway }

    // 입체가 없는 부재가 결합으로 다 깎였는가: 이것을 자르는 결합이 하나라도 있다. 그런 부재를 경계상자로 그리면 다 깎인 벽이 원래 모양 그대로
    // «유령»으로 보인다(같은 자리에 겹쳐 그린 벽을 고친 뒤 — JACK 2026-10-19 · R51c revit-verify ①). 결합을 읽지 못하면 아니라고 친다(상자로 그림).
    private static bool CutAway(Element el)
    {
        try
        {
            var doc = el.Document;
            return JoinGeometryUtils.GetJoinedElements(doc, el)
                .Any(id => doc.GetElement(id) is { } other && JoinGeometryUtils.IsCuttingElementInJoin(doc, other, el));
        }
        catch { return false; }
    }

    // 부재 하나의 겉면 — Read 와 Remesh 가 같은 규칙으로 만든다(ZoneScene.Read 와 같은 규칙).
    // builder = null 이면 그릴 수 없다(형상도 경계상자도 없음). box = 경계상자(모델 안쪽 좌표 · m, 없으면 빈 상자).
    private static (MeshBuilder? builder, Box3 box, Drawn how) Member(Element el, ZoneScene.Data counters)
    {
        var box = Box3.Empty;
        try
        {
            var bb = el.get_BoundingBox(null);
            if (bb != null) box = new Box3(ZoneScene.Meters(bb.Min), ZoneScene.Meters(bb.Max));
        }
        catch { }

        MeshBuilder? builder = null;
        var how = Drawn.Faces;
        try
        {
            var solids = SolidReader.Solids(el);
            if (solids.Count > 0)
            {
                builder = new MeshBuilder();
                foreach (var solid in solids) ZoneScene.AddSolid(builder, solid, counters);
            }
            else if (CutAway(el)) return (null, box, Drawn.CutAway);
        }
        catch
        {
            builder = null;
            how = Drawn.ReadError;
        }
        // 세모를 하나도 못 만든 부재: 경계상자로라도 그린다. 경계상자도 없으면 그릴 수 없다.
        if (builder == null || builder.Triangles == 0)
        {
            if (how == Drawn.Faces) how = Drawn.Boxed;
            builder = !box.IsEmpty && box.Size.Length > 1e-6 ? ZoneScene.BoxBuilder(box) : null;
        }
        return (builder, box, how);
    }

    /// <summary>
    /// 모양이 바뀐 부재(겹침 창에서 결합한 뒤)의 겉면을 다시 읽는다 — Read 와 같은 규칙 · 같은 장면의 가운데. 모델은 바꾸지 않는다.
    /// 받은 부재가 든 칸을 **칸 전부로** 다시 그린다(나눈 조각 — Alias — 이 한 칸에 여럿이면 하나만 받아도 전부를 한 겉면으로: R35 core M1).
    /// 돌려주는 것 = 그림의 차례 → 새 겉면(그림에 없는 부재 · 이제 그릴 수 없는 부재는 빈 겉면). data.Meshes 도 그것으로 바꾼다.
    /// </summary>
    public static Dictionary<int, MeshData> Remesh(Document doc, Data data, IEnumerable<ElementId> ids)
    {
        var changed = MeshesNow(doc, data, ids);
        foreach (var (slot, mesh) in changed) data.Meshes[slot] = mesh;
        return changed;
    }

    /// <summary>
    /// 그 부재들의 지금 겉면(그림의 칸 → 겉면 — 나눈 조각은 원래 칸에 함께). 장면 자료는 바꾸지 않는다 — 작성오류 미리보기(R54)가 시험 트랜잭션 안에서
    /// 바꾼 뒤 모양을 읽고 되돌리는 데 쓴다(되돌린 뒤 장면이 바뀐 모양을 들고 있지 않게).
    /// </summary>
    public static Dictionary<int, MeshData> MeshesNow(Document doc, Data data, IEnumerable<ElementId> ids)
    {
        var changed = new Dictionary<int, MeshData>();
        var counters = new ZoneScene.Data();
        foreach (int slot in ids.Select(data.IndexOf).Where(at => at >= 0).Distinct())
        {
            var els = data.InSlot(slot).Select(doc.GetElement).Where(el => el != null).ToList();
            MeshBuilder? builder;
            if (els.Count == 1) builder = Member(els[0]!, counters).builder;
            else
            {
                // 조각 전부의 면을 한 그릇에(Read 와 같은 규칙 — 면의 세모, 하나도 못 만들면 조각들을 싼 경계상자).
                builder = new MeshBuilder();
                var box = Box3.Empty;
                bool allCutAway = true;                   // 조각 전부가 결합으로 다 깎였다(입체 없음) — 상자로 되살리지 않는다
                foreach (var el in els)
                {
                    try
                    {
                        var bb = el!.get_BoundingBox(null);
                        if (bb != null) box = box.With(new Box3(ZoneScene.Meters(bb.Min), ZoneScene.Meters(bb.Max)));
                        var solids = SolidReader.Solids(el);
                        if (solids.Count > 0 || !CutAway(el)) allCutAway = false;
                        foreach (var solid in solids) ZoneScene.AddSolid(builder, solid, counters);
                    }
                    catch { allCutAway = false; }
                }
                if (builder.Triangles == 0) builder = allCutAway ? null : !box.IsEmpty && box.Size.Length > 1e-6 ? ZoneScene.BoxBuilder(box) : null;
            }
            changed[slot] = builder?.Build(data.Origin) ?? new MeshData();
        }
        return changed;
    }

    /// <summary>
    /// 고치기 미리보기(JACK 2026-10-19 «갖다 대면 변환 후의 모습»): 그 쌍을 결합하면 잘릴 부재의 모양 = 잘릴 부재(cut)에서 남을 부재(keep)의
    /// 입체를 뺀 것(Revit 불리언 빼기 — 결합이 하는 일과 같다 · 결합처럼 쌍의 두 부재만). 그 칸의 다른 조각은 그대로 함께 그린다(Remesh 처럼 칸 전부로). 모델은 바꾸지 않는다.
    /// 돌려주는 것 = 그 칸의 새 겉면(장면의 가운데로 굳힘 · 다 잘려 나가면 세모 0) · 칸 · 못 만들었으면 겉면 null 과 까닭(부르는 쪽이 대신 그린다).
    /// 빼기가 하나라도 실패하면 반쯤 잘린 모양을 보이지 않고 null 이다.
    /// </summary>
    public static (MeshData? mesh, int slot, string why) CutPreview(Document doc, Data data, ElementId cut, ElementId keep)
    {
        int slot = data.IndexOf(cut);
        if (slot < 0) return (null, -1, "잘릴 부재가 그림에 없음");
        if (doc.GetElement(keep) is not { } keepEl) return (null, slot, "남을 부재를 찾을 수 없음");
        // 이미 결합된 쌍(«결합됐지만 겹침이 남음» 같은 줄): 두 부재의 지금 모양은 결합이 깎은 뒤라 «잘릴 것 − 남을 것»이 고친 결과와 다르다 —
        // 기본은 Join 이 아무것도 바꾸지 않고, 순서를 뒤집으면 결합 전 모양으로 깎인다(그 모양은 API 로 얻을 수 없음 · R51 revit-verify ①).
        if (doc.GetElement(cut) is { } cutEl && JoinGeometryUtils.AreElementsJoined(doc, cutEl, keepEl))
            return (null, slot, "이미 결합된 쌍 — 결합 전 모양을 몰라 미리 그리지 않음");
        var keepSolids = SolidReader.Solids(keepEl);
        if (keepSolids.Count == 0) return (null, slot, "남을 부재의 입체 없음");
        var counters = new ZoneScene.Data();
        var builder = new MeshBuilder();
        foreach (var el in data.InSlot(slot).Select(doc.GetElement))
        {
            if (el == null) continue;
            var solids = SolidReader.Solids(el);
            if (el.Id != cut)
            {
                foreach (var solid in solids) ZoneScene.AddSolid(builder, solid, counters);
                continue;
            }
            if (solids.Count == 0) return (null, slot, $"잘릴 부재 {el.Id.Value} 의 입체 없음");
            foreach (var solid in solids)
            {
                var rest = solid;
                foreach (var k in keepSolids)
                {
                    try { rest = BooleanOperationsUtils.ExecuteBooleanOperation(rest, k, BooleanOperationsType.Difference); }
                    catch (Exception ex) { return (null, slot, $"빼기 실패({el.Id.Value}): {ex.Message}"); }
                    if (rest == null) return (null, slot, $"빼기 결과 없음({el.Id.Value})");
                }
                if (rest.Volume > 1e-9) ZoneScene.AddSolid(builder, rest, counters);
            }
        }
        return (builder.Build(data.Origin), slot, "");
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
