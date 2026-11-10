using System.IO;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using DH.CivilCoord.Core;

namespace DH.CivilCoord.Revit;

/// <summary>
/// 지금 3D 뷰에 보이는 부재 → 세모 → IFC(Civil 좌표). JACK 1110: «지금 3D 뷰에 보이는 것» · 3D 뷰가 아니면 기본 3D 뷰.
/// <para>형상 = 그 뷰에서 Revit 이 그리는 입체(구멍 · 잘림 · 결합이 이미 반영됨) · 좌표 = Revit 내부 좌표(m) · Civil 로 옮기는 값은 IfcSite 하나에.</para>
/// </summary>
internal static class Exporter
{
    private const double Ft = 0.3048;

    private sealed class Stats
    {
        public int Elements, Links, Imports, Groups, StairsAsParts, StairComponentsSkipped, NoBody, FallbackFaces, MeshObjects;
        public bool SectionBox;
        public readonly Dictionary<string, int> NoBodyByCat = new();
        public readonly Dictionary<string, int> ByEntity = new();
    }

    // 형상이 없거나(방 · 선 · 레벨 …) 따로 세는 것(링크 · CAD · 그룹) — «형상 없음» 셈에 넣지 않는다
    private static readonly HashSet<BuiltInCategory> NotParts = new()
    {
        BuiltInCategory.OST_Rooms, BuiltInCategory.OST_Areas, BuiltInCategory.OST_MEPSpaces, BuiltInCategory.OST_Cameras,
        BuiltInCategory.OST_Levels, BuiltInCategory.OST_Grids, BuiltInCategory.OST_SectionBox, BuiltInCategory.OST_VolumeOfInterest,
        BuiltInCategory.OST_Lines, BuiltInCategory.OST_CLines, BuiltInCategory.OST_RvtLinks, BuiltInCategory.OST_IOSModelGroups,
        BuiltInCategory.OST_ReferencePoints, BuiltInCategory.OST_PointClouds, BuiltInCategory.OST_Viewers, BuiltInCategory.OST_ProjectBasePoint,
        BuiltInCategory.OST_SharedBasePoint, BuiltInCategory.OST_SketchLines, BuiltInCategory.OST_ShaftOpening,
    };

    /// <summary>내보낼 3D 뷰(지금 뷰가 3D 면 그것 · 아니면 기본 3D 뷰).</summary>
    public static View3D? PickView(UIDocument uidoc, out string note)
    {
        note = "";
        if (uidoc.ActiveView is View3D v && !v.IsTemplate) return v;
        var all = new FilteredElementCollector(uidoc.Document).OfClass(typeof(View3D)).Cast<View3D>().Where(x => !x.IsTemplate).ToList();
        var def = all.FirstOrDefault(x => x.Name == "{3D}") ?? all.FirstOrDefault(x => x.Name.StartsWith("{3D", StringComparison.Ordinal)) ?? all.FirstOrDefault(x => !x.IsPerspective);
        if (def != null) note = $"지금 뷰가 3D가 아니라 «{def.Name}» 기준";
        return def;
    }

    /// <summary>IFC 쓰기. 돌려줌 = (창에 보일 글, 색 종류).</summary>
    public static (string Text, string Kind) Run(UIDocument uidoc, FitState state, Fit2D fit, LevelHeights heights, string path)
    {
        var doc = uidoc.Document;
        var view = PickView(uidoc, out string viewNote);
        if (view == null) return ("원인: 3D 뷰가 없습니다 — 3D 뷰를 하나 만들고 다시", "Bad");

        double dz = state.DzValue;
        var st = new Stats();
        var storeys = new Dictionary<long, StoreyInfo>();
        var parts = Collect(doc, view, heights, st, storeys);
        Log.Line($"모으기\t뷰 «{view.Name}»{(st.SectionBox ? "(단면 상자)" : "")}\t부재 {parts.Count}/{st.Elements}\t형상 없음 {st.NoBody}\t링크 {st.Links}\tCAD {st.Imports}\t그룹 {st.Groups}" +
                 $"\t계단(부품으로) {st.StairsAsParts}\t계단 부품 건너뜀 {st.StairComponentsSkipped}\t면별 세모 {st.FallbackFaces}\t메시 {st.MeshObjects}");
        if (st.NoBodyByCat.Count > 0) Log.Line("형상 없음\t" + string.Join(", ", st.NoBodyByCat.Select(k => $"{k.Key} {k.Value}")));
        if (parts.Count == 0) return ($"원인: «{view.Name}»에 보이는 부재에 형상이 없습니다", "Bad");

        var t = fit.Translation;
        var info = doc.ProjectInformation;
        string title = Path.GetFileNameWithoutExtension(doc.Title);
        var job = new IfcJob
        {
            FileName = Path.GetFileName(path),
            ProjectName = title,
            ProjectGuid = ModelGuid(doc, info?.Id, "project"),
            SiteName = "Civil",
            BuildingName = title,
            AppVersion = typeof(Exporter).Assembly.GetName().Version?.ToString(3) ?? "1.0.0",
            Time = DateTime.Now,
            Tx = t.X, Ty = t.Y, Tz = heights.OffsetM + dz, Angle = fit.Angle,
            Scale = fit.Scale,   // 평면 비율(JACK 1110 — 두 선분 길이가 다르면 그 비율로 X · Y 만 · 높이 그대로)
        };
        // 맞춤 기록(IfcSite 의 DH_CivilFit — 나비스웍스 속성 창에서 보임)
        job.SiteProps.Add(("Civil 좌표 출처", state.FileMode ? "Civil 파일 " + Path.GetFileName(state.CivilFile) : "직접 입력"));
        if (state.FileMode && state.CivilSummary.Length > 0) job.SiteProps.Add(("Civil 기준점", state.CivilSummary));
        job.SiteProps.Add(("두 점 거리 Revit · Civil (m)", $"{fit.RevitDistance:0.0000} · {fit.CivilDistance:0.0000}"));
        job.SiteProps.Add(("평면 비율", fit.Scale.ToString("0.00000000", System.Globalization.CultureInfo.InvariantCulture)));
        job.SiteProps.Add(("회전 (°)", fit.AngleDegrees.ToString("0.000000", System.Globalization.CultureInfo.InvariantCulture)));
        job.SiteProps.Add(("높이 더함 (m)", (heights.OffsetM + dz).ToString("0.0000", System.Globalization.CultureInfo.InvariantCulture)));
        job.Storeys.AddRange(storeys.Values);
        job.Parts.AddRange(parts);

        string tmp = path + ".tmp";
        try
        {
            IfcWriter.Write(job, tmp);
            File.Move(tmp, path, true);   // 한 번에 바꿈(Civil 이 반쯤 쓴 IFC 를 읽지 않게)
        }
        catch (Exception ex)
        {
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
            Log.Line("IFC 쓰기 오류\t" + ex);
            return ("원인: IFC를 못 씀 — " + ex.Message, "Bad");
        }
        Log.LastFolder = Path.GetDirectoryName(path) ?? "";

        int tris = parts.Sum(p => p.TriangleCount);
        var box = job.WorldBox();
        Log.Line($"IFC\t{path}\t부재 {parts.Count}\t세모 {tris}\t돌림 {fit.AngleDegrees:0.0000}°\t옮김 {t.X:0.000},{t.Y:0.000},{job.Tz:0.000}\t거리 차 {fit.DistanceError * 1000:0.0} mm\t" +
                 string.Join(", ", st.ByEntity.OrderByDescending(k => k.Value).Select(k => $"{k.Key} {k.Value}")));
        var lines = new List<string> { $"IFC 저장 — {Path.GetFileName(path)} · 부재 {parts.Count} · 세모 {tris:N0}" };
        if (box is { } b) lines.Add($"범위 X {b.X0:#,0.0}~{b.X1:#,0.0} · Y {b.Y0:#,0.0}~{b.Y1:#,0.0} · 높이 {b.Z0:0.00}~{b.Z1:0.00} m");
        var skipped = new List<string>();
        if (st.Links > 0) skipped.Add($"링크 {st.Links}");
        if (st.Imports > 0) skipped.Add($"CAD {st.Imports}");
        if (st.NoBody > 0) skipped.Add($"형상 없음 {st.NoBody}");
        if (skipped.Count > 0) lines.Add("빠짐 " + string.Join(" · ", skipped));
        if (viewNote.Length > 0) lines.Add(viewNote);
        if (st.SectionBox) lines.Add("단면 상자에 걸친 부재만(부재는 자르지 않음)");
        return (string.Join("\n", lines), "Good");
    }

    /// <summary>
    /// 미리보기용 Revit 부재 윤곽(내부 좌표 m · 부재마다 평면 볼록 껍질) — IFC 와 같은 부재(지금 3D 뷰에 보이는 것 · 지형 뺌).
    /// 명령 한 번에 한 번만 셈(창을 다시 열 때는 그대로 씀).
    /// </summary>
    public static List<P2[]> Footprints(UIDocument uidoc, LevelHeights heights)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var res = new List<P2[]>();
        try
        {
            var view = PickView(uidoc, out _);
            if (view == null) return res;
            // 큰 모델이면 창이 오래 멈추지 않게 미리보기 윤곽을 건너뜀(검토 확인 필요 — 명령 켤 때마다 셈)
            int count = new FilteredElementCollector(uidoc.Document, view.Id).WhereElementIsNotElementType().GetElementCount();
            if (count > MaxPreviewElements)
            {
                Log.Line($"미리보기 윤곽 건너뜀\t뷰 «{view.Name}» 요소 {count} > {MaxPreviewElements}");
                return res;
            }
            var st = new Stats();
            var parts = Collect(uidoc.Document, view, heights, st, new Dictionary<long, StoreyInfo>(), preview: true);
            foreach (var p in parts)
            {
                if (p.Entity == "IFCGEOGRAPHICELEMENT") continue;   // 지형은 구조물 윤곽을 덮음
                var hull = Hull.Convex(p.Shells.SelectMany(s => s.V).Select(v => new P2(v.X, v.Y)));
                if (hull.Count >= 3 && Math.Abs(Hull.Area(hull)) > 1e-4) res.Add(hull.ToArray());
            }
            Log.Line($"미리보기 윤곽\t뷰 «{view.Name}»\t부재 {parts.Count}\t윤곽 {res.Count}\t{sw.ElapsedMilliseconds} ms");
        }
        catch (Exception ex) { Log.Line("미리보기 윤곽 못 셈\t" + ex.Message); }
        return res;
    }

    /// <summary>미리보기 윤곽을 셀 요소 수 한도(넘으면 미리보기에 Revit 윤곽 없이).</summary>
    private const int MaxPreviewElements = 8000;

    private static List<MeshPart> Collect(Document doc, View3D view, LevelHeights heights, Stats st, Dictionary<long, StoreyInfo> storeys, bool preview = false)
    {
        var parts = new List<MeshPart>();
        // 형상은 늘 «정밀»로 읽는다(설치 전 검토 높음 — 뷰의 상세 수준이 거침 · 중간이면 관 · 덕트 · 보가 선으로만 와서 빠짐).
        // 뷰는 «어떤 부재를 넣을지»(숨긴 범주 · 숨긴 부재 · 단면 상자)만 정한다.
        // 미리보기 윤곽(preview)은 평면 볼록 껍질만 쓰므로 «중간» · 가장 거친 세모로 가볍게(관이 선으로 와 빠져도 미리보기엔 괜찮음).
        var opt = new Options { DetailLevel = preview ? ViewDetailLevel.Medium : ViewDetailLevel.Fine, ComputeReferences = false, IncludeNonVisibleObjects = false };
        var ctl = new SolidOrShellTessellationControls { LevelOfDetail = preview ? 0 : 0.5 };
        var collector = new FilteredElementCollector(doc, view.Id).WhereElementIsNotElementType();
        // 단면 상자를 켠 뷰: 상자에 걸치는 부재만(뷰 수집기는 상자 밖 부재도 통과시킬 수 있음 — 검토 확인 필요 1) · 부재는 자르지 않고 통째로
        if (view.IsSectionBoxActive && SectionOutline(view) is { } box)
        {
            collector = collector.WherePasses(new BoundingBoxIntersectsFilter(box));
            st.SectionBox = true;
        }
        // 계단을 먼저: 자체 형상(여러 층 계단의 위층까지)이 있으면 그 런 · 참 · 받침은 건너뜀(두 번 넣지 않게 — 검토 중간) ·
        // 자체 형상이 없으면 부품이 따로 들어감
        var elements = collector.ToElements().OrderBy(x => x is Stairs ? 0 : 1).ToList();
        var skip = new HashSet<long>();
        foreach (var e in elements)
        {
            if (skip.Contains(e.Id.Value)) { st.StairComponentsSkipped++; continue; }
            if (e is RevitLinkInstance) { st.Links++; continue; }
            if (e is ImportInstance) { st.Imports++; continue; }
            if (e is Group || e is AssemblyInstance) { st.Groups++; continue; }   // 안의 부재가 따로 모임 — 두 번 넣지 않음
            if (e is MultistoryStairs) continue;   // 여러 층 계단 묶음 — 안의 계단(위층 포함)이 따로 들어감(재검토 확인 필요)
            var cat = e.Category;
            if (cat == null || cat.CategoryType != CategoryType.Model) continue;
            var bic = cat.BuiltInCategory;
            if (NotParts.Contains(bic)) continue;
            st.Elements++;

            var part = new MeshPart();
            GeometryElement? ge = null;
            try { ge = e.get_Geometry(opt); } catch { }
            if (ge != null) Walk(ge, part, ctl, st, 0);
            if (e is Stairs stairs)
            {
                var comps = new List<ElementId>();
                try { comps.AddRange(stairs.GetStairsRuns()); comps.AddRange(stairs.GetStairsLandings()); comps.AddRange(stairs.GetStairsSupports()); } catch { }
                Log.Line($"계단\t{e.Id.Value}\t자체 세모 {part.TriangleCount}\t부품 {comps.Count}");
                if (part.Shells.Count > 0) foreach (var id in comps) skip.Add(id.Value);
                else if (comps.Count > 0) { st.StairsAsParts++; continue; }   // 부품으로 들어감 — «형상 없음» 아님
            }
            if (part.Shells.Count == 0)
            {
                st.NoBody++;
                st.NoBodyByCat[cat.Name] = st.NoBodyByCat.TryGetValue(cat.Name, out int c) ? c + 1 : 1;
                continue;
            }

            (part.Entity, part.Predefined) = Map(e, bic);
            var type = doc.GetElement(e.GetTypeId()) as ElementType;
            string fam = type?.FamilyName ?? "", typeName = type?.Name ?? e.Name ?? "";
            part.ObjectType = fam.Length > 0 && typeName.Length > 0 ? $"{fam}:{typeName}" : fam + typeName;
            part.Name = (part.ObjectType.Length > 0 ? part.ObjectType : cat.Name) + ":" + e.Id.Value;
            part.Tag = e.Id.Value.ToString();
            part.Guid = SafeGuid(doc, e.Id, "element/" + e.UniqueId);

            string levelName = "";
            var lid = LevelOf(e);
            if (lid != null && heights.Levels.TryGetValue(lid.Value, out var lv))
            {
                levelName = lv.Name;
                part.StoreyKey = lid.Value.ToString();
                // 층 Elevation = 건물 0.00(대지 배치에서 이미 Tz 만큼 올라감) 기준 = 내부 Z(검토 낮음 — 절대 높이를 쓰면 두 번 더한 자리)
                if (!storeys.ContainsKey(lid.Value))
                    storeys[lid.Value] = new StoreyInfo { Key = part.StoreyKey, Name = lv.Name, Elevation = lv.ShownM - heights.OffsetM, Guid = ModelGuid(doc, new ElementId(lid.Value), "level/" + lid.Value) };
            }

            part.Props.Add(("Revit ID", part.Tag));
            part.Props.Add(("범주", cat.Name));
            if (fam.Length > 0) part.Props.Add(("패밀리", fam));
            if (typeName.Length > 0) part.Props.Add(("유형", typeName));
            if (levelName.Length > 0) part.Props.Add(("레벨", levelName));
            string mark = e.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)?.AsString() ?? "";
            if (mark.Length > 0) part.Props.Add(("마크", mark));

            parts.Add(part);
            st.ByEntity[part.Entity] = st.ByEntity.TryGetValue(part.Entity, out int k) ? k + 1 : 1;
        }
        return parts;
    }

    private static void Walk(GeometryElement ge, MeshPart part, SolidOrShellTessellationControls ctl, Stats st, int depth)
    {
        if (depth > 16) return;
        foreach (GeometryObject go in ge)
        {
            switch (go)
            {
                case Solid s:
                    AddSolid(s, part, ctl, st);
                    break;
                case GeometryInstance gi:
                    GeometryElement? inner = null;
                    try { inner = gi.GetInstanceGeometry(); } catch { }   // 모델 좌표로 옮긴 사본
                    if (inner != null) Walk(inner, part, ctl, st, depth + 1);
                    break;
                case Mesh m:
                    st.MeshObjects++;
                    AddMesh(m, part);
                    break;
                case GeometryElement g2:
                    Walk(g2, part, ctl, st, depth + 1);
                    break;
            }
        }
    }

    private static void AddSolid(Solid s, MeshPart part, SolidOrShellTessellationControls ctl, Stats st)
    {
        if (s == null || s.Faces.Size == 0) return;
        try
        {
            var tri = SolidUtils.TessellateSolidOrShell(s, ctl);
            for (int i = 0; i < tri.ShellComponentCount; i++)
            {
                var comp = tri.GetShellComponent(i);
                var sh = new MeshShell { Closed = comp.IsClosed };
                for (int k = 0; k < comp.VertexCount; k++) { var p = comp.GetVertex(k); sh.V.Add((p.X * Ft, p.Y * Ft, p.Z * Ft)); }
                for (int k = 0; k < comp.TriangleCount; k++) { var t = comp.GetTriangle(k); sh.T.Add((t.VertexIndex0, t.VertexIndex1, t.VertexIndex2)); }
                if (sh.T.Count > 0) part.Shells.Add(sh);
            }
        }
        catch
        {
            // 세모 나누기가 안 되는 입체 — 면마다 나눔(꼭짓점은 같은 자리끼리 잇는다 · 열린 면으로)
            var sh = new MeshShell { Closed = false };
            var map = new Dictionary<(long, long, long), int>();
            foreach (Face f in s.Faces)
            {
                Mesh? m = null;
                try { m = f.Triangulate(0.5); } catch { }
                if (m == null) continue;
                st.FallbackFaces++;
                AddMeshTo(sh, m, map);
            }
            if (sh.T.Count > 0) part.Shells.Add(sh);
        }
    }

    private static void AddMesh(Mesh m, MeshPart part)
    {
        var sh = new MeshShell { Closed = m.IsClosed };
        AddMeshTo(sh, m, new Dictionary<(long, long, long), int>());
        if (sh.T.Count > 0) part.Shells.Add(sh);
    }

    private static void AddMeshTo(MeshShell sh, Mesh m, Dictionary<(long, long, long), int> map)
    {
        var verts = m.Vertices;
        var idx = new int[verts.Count];
        for (int i = 0; i < verts.Count; i++)
        {
            var p = verts[i];
            double x = p.X * Ft, y = p.Y * Ft, z = p.Z * Ft;
            var key = ((long)Math.Round(x * 1e6), (long)Math.Round(y * 1e6), (long)Math.Round(z * 1e6));   // 1 µm 칸
            if (!map.TryGetValue(key, out int at)) { at = sh.V.Count; sh.V.Add((x, y, z)); map[key] = at; }
            idx[i] = at;
        }
        for (int i = 0; i < m.NumTriangles; i++)
        {
            var t = m.get_Triangle(i);
            int a = (int)t.get_Index(0), b = (int)t.get_Index(1), c = (int)t.get_Index(2);
            if (a < idx.Length && b < idx.Length && c < idx.Length) sh.T.Add((idx[a], idx[b], idx[c]));
        }
    }

    /// <summary>부재가 놓인 레벨(없으면 null).</summary>
    private static long? LevelOf(Element e)
    {
        try
        {
            if (e.LevelId != null && e.LevelId != ElementId.InvalidElementId) return e.LevelId.Value;
            foreach (var bip in new[] { BuiltInParameter.INSTANCE_REFERENCE_LEVEL_PARAM, BuiltInParameter.FAMILY_LEVEL_PARAM, BuiltInParameter.SCHEDULE_LEVEL_PARAM,
                                         BuiltInParameter.STAIRS_BASE_LEVEL_PARAM, BuiltInParameter.WALL_BASE_CONSTRAINT, BuiltInParameter.FAMILY_BASE_LEVEL_PARAM })
            {
                var p = e.get_Parameter(bip);
                if (p != null && p.StorageType == StorageType.ElementId)
                {
                    var id = p.AsElementId();
                    if (id != null && id != ElementId.InvalidElementId) return id.Value;
                }
            }
            if (e is StairsRun run) return LevelOf(run.GetStairs());
            if (e is StairsLanding landing) return LevelOf(landing.GetStairs());
        }
        catch { }
        return null;
    }

    /// <summary>Revit 범주 → IFC4 종류 · PredefinedType(빈 글 = 그 종류 기본). Civil 은 IFC 종류로 레이어 · 해치(계단 · 난간 · 문 · 창 · 설비 · 지형)를 가른다.</summary>
    private static (string, string) Map(Element e, BuiltInCategory c) => c switch
    {
        BuiltInCategory.OST_Walls => ("IFCWALL", ""),
        BuiltInCategory.OST_Floors => ("IFCSLAB", ".FLOOR."),
        BuiltInCategory.OST_StructuralFoundation => e is Floor ? ("IFCSLAB", ".BASESLAB.") : ("IFCFOOTING", ""),
        BuiltInCategory.OST_StructuralColumns or BuiltInCategory.OST_Columns => ("IFCCOLUMN", ""),
        BuiltInCategory.OST_StructuralFraming => ("IFCBEAM", ""),
        BuiltInCategory.OST_StructuralTruss => ("IFCMEMBER", ""),
        BuiltInCategory.OST_Stairs or BuiltInCategory.OST_StairsRuns or BuiltInCategory.OST_StairsLandings
            or BuiltInCategory.OST_StairsStringerCarriage => ("IFCSTAIR", ""),
        BuiltInCategory.OST_Ramps => ("IFCRAMP", ""),
        BuiltInCategory.OST_Roofs => ("IFCROOF", ""),
        BuiltInCategory.OST_Railings or BuiltInCategory.OST_StairsRailing or BuiltInCategory.OST_StairsRailingBaluster
            or BuiltInCategory.OST_RailingTopRail or BuiltInCategory.OST_RailingHandRail or BuiltInCategory.OST_RailingSupport
            or BuiltInCategory.OST_RailingTermination => ("IFCRAILING", ""),
        BuiltInCategory.OST_Ceilings => ("IFCCOVERING", ".CEILING."),
        BuiltInCategory.OST_Doors => ("IFCDOOR", ""),
        BuiltInCategory.OST_Windows => ("IFCWINDOW", ""),
        BuiltInCategory.OST_CurtainWallPanels => ("IFCPLATE", ""),
        BuiltInCategory.OST_CurtainWallMullions => ("IFCMEMBER", ""),
        BuiltInCategory.OST_Topography or BuiltInCategory.OST_Toposolid => ("IFCGEOGRAPHICELEMENT", ""),
        BuiltInCategory.OST_PipeCurves or BuiltInCategory.OST_FlexPipeCurves or BuiltInCategory.OST_DuctCurves or BuiltInCategory.OST_FlexDuctCurves
            or BuiltInCategory.OST_Conduit or BuiltInCategory.OST_CableTray => ("IFCFLOWSEGMENT", ""),
        BuiltInCategory.OST_PipeFitting or BuiltInCategory.OST_DuctFitting or BuiltInCategory.OST_ConduitFitting or BuiltInCategory.OST_CableTrayFitting
            or BuiltInCategory.OST_PipeAccessory or BuiltInCategory.OST_DuctAccessory => ("IFCFLOWFITTING", ""),
        BuiltInCategory.OST_PlumbingFixtures or BuiltInCategory.OST_MechanicalEquipment or BuiltInCategory.OST_Sprinklers
            or BuiltInCategory.OST_DuctTerminal or BuiltInCategory.OST_ElectricalEquipment or BuiltInCategory.OST_LightingFixtures => ("IFCFLOWTERMINAL", ""),
        BuiltInCategory.OST_Furniture or BuiltInCategory.OST_FurnitureSystems or BuiltInCategory.OST_Casework => ("IFCFURNISHINGELEMENT", ""),
        _ => ("IFCBUILDINGELEMENTPROXY", ""),
    };

    /// <summary>단면 상자(돌린 상자면 그것을 감싸는 축 정렬 상자) — 모델 좌표.</summary>
    private static Outline? SectionOutline(View3D view)
    {
        try
        {
            var bb = view.GetSectionBox();
            if (bb == null) return null;
            var t = bb.Transform ?? Transform.Identity;
            double x0 = double.MaxValue, y0 = double.MaxValue, z0 = double.MaxValue, x1 = double.MinValue, y1 = double.MinValue, z1 = double.MinValue;
            foreach (double x in new[] { bb.Min.X, bb.Max.X })
                foreach (double y in new[] { bb.Min.Y, bb.Max.Y })
                    foreach (double z in new[] { bb.Min.Z, bb.Max.Z })
                    {
                        var p = t.OfPoint(new XYZ(x, y, z));
                        x0 = Math.Min(x0, p.X); y0 = Math.Min(y0, p.Y); z0 = Math.Min(z0, p.Z);
                        x1 = Math.Max(x1, p.X); y1 = Math.Max(y1, p.Y); z1 = Math.Max(z1, p.Z);
                    }
            return new Outline(new XYZ(x0, y0, z0), new XYZ(x1, y1, z1));
        }
        catch (Exception ex) { Log.Line("단면 상자 못 읽음\t" + ex.Message); return null; }
    }

    /// <summary>
    /// 모델 한 벌에 하나인 것(프로젝트 · 층)의 GUID — 같은 템플릿에서 만든 rvt 끼리 겹치지 않게 모델이 처음 만들어질 때의 GUID(CreationGUID)를 섞음.
    /// 파일 이름은 안 씀(작업 공유 로컬 사본 «모델_사용자» · 다른 이름 저장마다 바뀌어 나비스웍스 검색 세트가 끊김 — 재검토 낮음).
    /// </summary>
    private static string ModelGuid(Document doc, ElementId? id, string what)
    {
        string model = "", seed = "";
        try { model = doc.CreationGUID.ToString(); } catch { }
        if (model.Length == 0 || model == Guid.Empty.ToString()) model = Path.GetFileNameWithoutExtension(doc.Title);
        try { if (id != null && id != ElementId.InvalidElementId) seed = ExportUtils.GetExportId(doc, id).ToString(); }
        catch { }
        return IfcGuid.Compress(IfcGuid.FromName("DH.CivilCoord/" + model + "/" + seed + "/" + what));
    }

    /// <summary>Revit IFC 내보내기와 같은 GUID(다시 뽑아도 같음) · 못 얻으면 이름에서 만든 늘 같은 값.</summary>
    private static string SafeGuid(Document doc, ElementId? id, string fallbackName)
    {
        try { if (id != null && id != ElementId.InvalidElementId) return IfcGuid.Compress(ExportUtils.GetExportId(doc, id)); }
        catch { }
        return IfcGuid.Compress(IfcGuid.FromName("DH.CivilCoord/" + doc.Title + "/" + fallbackName));
    }
}
