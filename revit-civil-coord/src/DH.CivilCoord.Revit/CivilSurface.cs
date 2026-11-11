using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using DH.CivilCoord.Core;

namespace DH.CivilCoord.Revit;

/// <summary>[모델에 넣기]에서 고른 것(JACK 1111 인터뷰 «둘 다 하되 고를 수 있게 · 3D 선도 팝업에서 넣을지 고름»).</summary>
internal sealed record InsertOptions(bool Exact, bool Topo, bool Lines);

/// <summary>
/// Civil 3D 영역(최종지표면 · 3D 선)을 Revit 모델에 넣기(JACK 1111 «Civil 지표면을 받아와 우리 Revit 에 뿌리기»).
/// <para>인터뷰 결정: 정확한 면(DirectShape — Civil 세모 그대로) · 지형 솔리드(Toposolid — Revit 이 점으로 다시 삼각망) 가운데 고름(둘 다 가능) ·
/// 3D 선은 고름 · 다시 넣으면 지난 것을 바꿈(이 애드인이 넣은 것만 — 확장 저장소 표시로 찾음) · 자리 = 미리보기와 같은 자리
/// (구조물 맞춤의 거꾸로: Revit 평면 = <see cref="Fit2D.Inverse"/> — 비율 · 손 맞춤 포함 · Revit 내부 Z = Civil 표고 − (레벨 높이 맞춤 + 더할 값)).</para>
/// 한 트랜잭션 — Ctrl+Z 한 번에 되돌림.
/// </summary>
internal static class CivilSurface
{
    private const double Ft = 0.3048;
    private static readonly Guid SchemaId = new("9a3d6b1e-4c27-4f85-b0e2-7d5c8a1f3e64");
    private const string Field = "Kind";
    public const string AppId = "DH.CivilCoord";

    private static Schema GetSchema()
    {
        var s = Schema.Lookup(SchemaId);
        if (s != null) return s;
        var b = new SchemaBuilder(SchemaId);
        b.SetSchemaName("DH_CivilCoordMade");
        b.SetReadAccessLevel(AccessLevel.Public);
        b.SetWriteAccessLevel(AccessLevel.Public);
        b.SetVendorId("DHEC");
        b.SetDocumentation("Element made by DH Civil coordinate add-in from a Civil 3D region (kind)");
        b.AddSimpleField(Field, typeof(string));
        return b.Finish();
    }

    private static void Mark(Element e, string kind)
    {
        var en = new Entity(GetSchema());
        en.Set(Field, kind);
        e.SetEntity(en);
    }

    /// <summary>이 애드인이 넣은 요소 Id(형상 모으기에서 뺄 때 — 없으면 빈 모음).</summary>
    public static HashSet<long> MadeIds(Document doc)
    {
        try
        {
            if (Schema.Lookup(SchemaId) == null) return new HashSet<long>();
            return new FilteredElementCollector(doc).WherePasses(new ExtensibleStorageFilter(SchemaId)).ToElementIds().Select(i => i.Value).ToHashSet();
        }
        catch { return new HashSet<long>(); }
    }

    /// <summary>이 애드인이 전에 넣은 요소 수.</summary>
    public static int CountMade(Document doc)
    {
        try
        {
            if (Schema.Lookup(SchemaId) == null) return 0;
            return new FilteredElementCollector(doc).WherePasses(new ExtensibleStorageFilter(SchemaId)).GetElementCount();
        }
        catch { return 0; }
    }

    /// <returns>(창 알림 색 Good · Warn · Bad, 글)</returns>
    public static (string Kind, string Message) Insert(Document doc, Region3D region, Fit2D fit, double tzM, InsertOptions opt)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        XYZ In(double x, double y, double z) { var p = fit.Inverse(new P2(x, y)); return new XYZ(p.X / Ft, p.Y / Ft, (z - tzM) / Ft); }
        double tol = doc.Application.ShortCurveTolerance;

        using var t = new Transaction(doc, "DH Civil 지표면 넣기");
        var fo = t.GetFailureHandlingOptions();
        fo.SetFailuresPreprocessor(new SharedCoords.WarningSwallower());
        t.SetFailureHandlingOptions(fo);
        if (t.Start() != TransactionStatus.Started) return ("Bad", "원인: 트랜잭션을 못 엶");
        var lines = new List<string>();
        int created = 0;
        try
        {
            // 1) 지난 것 지우기(이 애드인이 넣은 것만)
            int removed = 0, dependents = 0;
            if (Schema.Lookup(SchemaId) != null)
            {
                var old = new FilteredElementCollector(doc).WherePasses(new ExtensibleStorageFilter(SchemaId)).ToElementIds();
                if (old.Count > 0)
                {
                    // 지운 목록에는 딸린 요소(사용자가 지형 솔리드에 만든 하위 구역 등)도 들어 옴 — 말없이 사라지지 않게 알림(설치 전 검토 중간)
                    var gone = doc.Delete(old);
                    removed = old.Count;
                    dependents = Math.Max(0, (gone?.Count ?? removed) - removed);
                }
            }
            var cat = Category(doc);

            // 2) 정확한 면(DirectShape · 세모 그대로)
            if (opt.Exact && region.Meshes.Count > 0)
            {
                int faces = 0, skipped = 0;
                foreach (var m in region.Meshes)
                {
                    var tris = new List<TessellatedFace>();
                    for (int k = 0; k + 2 < m.T.Length; k += 3)
                    {
                        int a = m.T[k] * 3, c = m.T[k + 1] * 3, d = m.T[k + 2] * 3;
                        var pa = In(m.V[a], m.V[a + 1], m.V[a + 2]); var pb = In(m.V[c], m.V[c + 1], m.V[c + 2]); var pc = In(m.V[d], m.V[d + 1], m.V[d + 2]);
                        // Revit 이 받지 않는 아주 짧은 변(짧은 선 한도)은 뺌
                        if (pa.DistanceTo(pb) < tol || pb.DistanceTo(pc) < tol || pc.DistanceTo(pa) < tol || (pb - pa).CrossProduct(pc - pa).GetLength() < tol * tol) { skipped++; continue; }
                        tris.Add(new TessellatedFace(new List<XYZ> { pa, pb, pc }, ElementId.InvalidElementId));
                    }
                    // 빈 면 묶음은 Build 가 예외를 낼 수 있음 — 건너뜀(설치 전 검토 미확인)
                    if (tris.Count == 0) { lines.Add($"정확한 면: «{m.Name}» 세모가 모두 너무 작음"); continue; }
                    var b = new TessellatedShapeBuilder { Target = TessellatedShapeBuilderTarget.Mesh, Fallback = TessellatedShapeBuilderFallback.Salvage };
                    b.OpenConnectedFaceSet(false);
                    foreach (var f in tris) b.AddFace(f);
                    faces += tris.Count;
                    b.CloseConnectedFaceSet();
                    b.Build();
                    var res = b.GetBuildResult();
                    var geo = res.GetGeometricalObjects();
                    if (geo.Count == 0) { lines.Add($"정확한 면: «{m.Name}» 세모를 Revit이 받지 않음"); continue; }
                    var ds = DirectShape.CreateElement(doc, cat);
                    ds.ApplicationId = AppId;
                    ds.ApplicationDataId = "CivilSurface";
                    ds.SetShape(geo);
                    ds.SetName("Civil " + (m.Name.Length > 0 ? m.Name : "지표면"));
                    Mark(ds, "surface");
                    created++;
                }
                lines.Add($"정확한 면 세모 {faces:N0}" + (skipped > 0 ? $"(아주 작은 세모 {skipped:N0} 뺌)" : ""));
                Log.Line($"Civil 지표면 넣기 — 정확한 면\t세모 {faces}\t뺌 {skipped}\t범주 {cat.Value}\t{sw.ElapsedMilliseconds} ms");
            }

            // 3) 지형 솔리드(점 → Revit 이 다시 삼각망)
            if (opt.Topo && region.Meshes.Count > 0)
            {
                var (ok, msg) = MakeToposolid(doc, region, In);
                lines.Add(msg);
                if (!ok) { t.RollBack(); return ("Bad", "원인: " + msg); }
                created++;
                Log.Line($"Civil 지표면 넣기 — 지형 솔리드\t{msg}\t{sw.ElapsedMilliseconds} ms");
            }

            // 4) 3D 선(DirectShape 선)
            if (opt.Lines && region.Lines.Count > 0)
            {
                var curves = new List<GeometryObject>();
                int shortSkipped = 0;
                foreach (var l in region.Lines)
                {
                    int n = l.Pts.Count;
                    for (int i = 0; i < (l.Closed ? n : n - 1); i++)
                    {
                        var a = l.Pts[i]; var b = l.Pts[(i + 1) % n];
                        var pa = In(a.X, a.Y, a.Z); var pb = In(b.X, b.Y, b.Z);
                        if (pa.DistanceTo(pb) < tol) { shortSkipped++; continue; }
                        curves.Add(Line.CreateBound(pa, pb));
                    }
                }
                DirectShape? ds = curves.Count > 0 ? DirectShape.CreateElement(doc, cat) : null;
                if (ds != null && !ds.IsValidShape(curves)) { doc.Delete(ds.Id); ds = null; }
                if (ds != null)
                {
                    ds.ApplicationId = AppId;
                    ds.ApplicationDataId = "CivilLines";
                    ds.SetShape(curves);
                    ds.SetName("Civil 3D 선");
                    Mark(ds, "lines");
                    created++;
                    lines.Add($"3D 선 {region.Lines.Count:N0}(선분 {curves.Count:N0})");
                }
                else lines.Add("3D 선: Revit이 받지 않음");
                Log.Line($"Civil 지표면 넣기 — 3D 선\t선 {region.Lines.Count}\t선분 {curves.Count}\t짧아서 뺌 {shortSkipped}\t{sw.ElapsedMilliseconds} ms");
            }

            // 새로 만든 것이 없으면 지난 것만 지운 채 «넣음»이 되지 않게 되돌림(설치 전 검토 낮음)
            if (created == 0)
            {
                t.RollBack();
                return ("Bad", "원인: 넣을 것을 Revit이 하나도 받지 않음 — 바꾸지 않음" + (lines.Count > 0 ? "\n" + string.Join(" · ", lines) : ""));
            }
            var st = t.Commit();
            if (st != TransactionStatus.Committed) return ("Bad", "원인: Revit이 넣기를 받지 않음(" + st + ")");
            string head = "Civil 지표면을 모델에 넣음" + (removed > 0 ? $" — 지난 것 {removed}개 바꿈" : "") + " · Ctrl+Z로 되돌림";
            if (dependents > 0) head += $"\n지난 것에 딸린 요소 {dependents}개도 함께 지워짐(지형 솔리드 하위 구역 등) — 필요하면 Ctrl+Z";
            Log.Line($"Civil 지표면 넣기 끝\t{string.Join(" · ", lines)}\t지움 {removed}\t{sw.ElapsedMilliseconds} ms\t손 맞춤 {(fit.HasHand ? "있음" : "없음")}\t비율 {fit.Scale:0.000000}\t높이 맞춤 {tzM:0.000} m");
            return (dependents > 0 ? "Warn" : "Good", head + "\n" + string.Join(" · ", lines));
        }
        catch (Exception ex)
        {
            if (t.HasStarted() && !t.HasEnded()) t.RollBack();
            Log.Line("Civil 지표면 넣기 오류\t" + ex);
            return ("Bad", "원인: " + ex.Message);
        }
    }

    /// <summary>DirectShape 범주: 지형 → 지형 솔리드 → 일반 모델 가운데 처음 되는 것(인터뷰 «지형 · 안 되면 일반 모델»).</summary>
    private static ElementId Category(Document doc)
    {
        foreach (var bic in new[] { BuiltInCategory.OST_Topography, BuiltInCategory.OST_Toposolid, BuiltInCategory.OST_GenericModel })
        {
            var id = new ElementId(bic);
            try { if (DirectShape.IsValidCategoryId(id, doc)) return id; } catch { }
        }
        return new ElementId(BuiltInCategory.OST_GenericModel);
    }

    /// <summary>지형 솔리드 점 한도 — 넘으면 고른 격자로 줄임(Revit 이 점으로 다시 삼각망을 만드는 데 오래 걸림).</summary>
    private const int MaxTopoPoints = 60_000;

    private static (bool Ok, string Message) MakeToposolid(Document doc, Region3D region, Func<double, double, double, XYZ> toInternal)
    {
        var typeId = new FilteredElementCollector(doc).OfClass(typeof(ToposolidType)).FirstElementId();
        if (typeId == ElementId.InvalidElementId) return (false, "지형 솔리드 유형이 이 모델에 없음");

        // 점: 같은 평면 자리(5 mm 안)는 가장 높은 것 하나(옹벽 수직면의 위아래 점 — 지형 솔리드는 수직 단차를 못 담음)
        var best = new Dictionary<(long, long), (double X, double Y, double Z)>();
        bool thinned = false;
        // 최종지표면(final)만 — 원지반 등 다른 면이 같이 오면 두 면을 오가는 톱니 면이 됨(설치 전 검토 낮음) · final 이 없으면 있는 것 전부
        var meshes = region.Meshes.Where(m => m.Kind == "final").ToList();
        if (meshes.Count == 0) meshes = region.Meshes.ToList();
        int vertices = meshes.Sum(m => m.VertexCount);
        IEnumerable<(double[] V, int[] T)> sets = vertices <= MaxTopoPoints
            ? meshes.Select(m => (m.V, m.T))
            : meshes.Select(m => TerrainLod.Coarse(m, Math.Max(1000, (int)(2.0 * MaxTopoPoints * m.VertexCount / vertices))));
        thinned = vertices > MaxTopoPoints;
        foreach (var (v, t) in sets)
        {
            var on = new bool[v.Length / 3];
            foreach (int i in t) on[i] = true;
            for (int i = 0; i < on.Length; i++)
            {
                if (!on[i]) continue;
                double x = v[i * 3], y = v[i * 3 + 1], z = v[i * 3 + 2];
                var key = ((long)Math.Round(x / 0.005), (long)Math.Round(y / 0.005));
                if (!best.TryGetValue(key, out var have) || z > have.Z) best[key] = (x, y, z);
            }
        }
        if (best.Count < 3) return (false, "지형 솔리드에 쓸 점이 3개보다 적음");
        var pts = best.Values.Select(p => toInternal(p.X, p.Y, p.Z)).ToList();

        // 레벨: 지표면 평균 높이에 가장 가까운 레벨
        double avg = pts.Average(p => p.Z);
        var level = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().OrderBy(l => Math.Abs(l.ProjectElevation - avg)).FirstOrDefault();
        if (level == null) return (false, "레벨이 없음");

        var topo = Toposolid.Create(doc, pts, typeId, level.Id);
        Mark(topo, "toposolid");
        doc.Regenerate();
        // 높이 확인: 윗면 가장 높은 점 = 넣은 점의 가장 높은 Z 이어야 — 점 Z 를 레벨 기준으로 읽는 판이면 그만큼 옮김(짐작 말고 잼)
        string note = "";
        var bb = topo.get_BoundingBox(null);
        double want = pts.Max(p => p.Z);
        if (bb != null && Math.Abs(bb.Max.Z - want) > 0.002 / Ft)
        {
            double move = want - bb.Max.Z;
            ElementTransformUtils.MoveElement(doc, topo.Id, new XYZ(0, 0, move));
            doc.Regenerate();
            var bb2 = topo.get_BoundingBox(null);
            note = $" · 높이 {move * Ft:+0.000;-0.000} m 맞춤";
            Log.Line($"지형 솔리드 높이 맞춤\t윗면 {bb.Max.Z * Ft:0.000} → 넣은 점 {want * Ft:0.000} m\t옮김 {move * Ft:0.000}\t뒤 {(bb2 != null ? (bb2.Max.Z * Ft).ToString("0.000") : "?")}");
        }
        return (true, $"지형 솔리드 점 {pts.Count:N0}{(thinned ? "(촘촘한 면을 격자로 줄임)" : "")} · 레벨 «{level.Name}»{note}");
    }
}
