using System.Globalization;
using System.IO;
using System.Text;
using Autodesk.Revit.DB;
using DH.Takeoff.Core;
using DH.Takeoff.Core.Formwork;

namespace DH.Takeoff.Revit;

/// <summary>
/// 거푸집 면 가르기에 넘길 자료를 모델에서 읽는다 — 콘크리트 부재의 입체(SolidReader, 산출식과 같은 읽기).
/// 계산은 전부 Core(FormworkAnalyzer)가 한다. 모델은 바꾸지 않는다.
/// 맞닿음·안팎·밑면 판단에는 모델의 콘크리트가 전부 있어야 하므로, 선택한 부재만 결과를 내더라도 나머지를 '주변'으로 함께 넘긴다.
/// </summary>
public static class FormworkReader
{
    private const double FtToM = 0.3048;

    public sealed class Scene
    {
        public List<FormworkElement> Elements = new();   // 콘크리트 부재 전부(Target = 결과를 낼 부재)
        public List<FormworkZone> Zones = new();
        public int Selected;                              // 선택한 것의 수(0 = 선택 없이 전체가 대상)
        public int Targets;                               // 결과를 낼 부재 수
        // 아래 셋은 대상(선택했으면 선택한 것) 가운데서 센다 — 선택해서 돌렸을 때 모델 전체의 수가 섞이지 않게.
        public int NotConcrete;                           // 콘크리트가 아니어서 뺀 부재
        public int NoSolid;                               // 입체가 없어 뺀 부재
        public int ReadErrors;                            // 형상을 읽다 오류가 난 부재
        public int NeighbourReadErrors;                   // 주변 부재 가운데 형상을 읽지 못한 것 — 그 부재와 맞닿은 자리는 빼지 못한다
        public int UnknownMaterial;                       // 재료를 알 수 없어 콘크리트로 본 대상 부재
        /// <summary>곡면을 세모로 나눈 것(색칠에만 쓴다): (부재 Id, 입체 차례, 면 차례) → 점 셋씩(m).</summary>
        public Dictionary<(long id, int solid, int face), List<double[]>> CurvedTriangles = new();
        /// <summary>
        /// 나중에 치는 부재가 Revit 결합에서 구조체를 자른 쌍(나중 부재 Id, 잘린 구조체 Id) — 구조체 입체에 그 부재 모양의 홈이 패여 있어,
        /// 그 부재 없이 재면 홈의 둘레가 거푸집으로 더 세진다(core R43 M-1). 고치지는 않고 알린다(결합 순서를 바꿔 구조체가 자르게 하면 맞다).
        /// </summary>
        public List<(long later, long cut)> LaterCuts = new();
    }

    /// <param name="selection">선택한 부재(비어 있으면 전체가 결과 대상).</param>
    /// <param name="laterPart">
    /// 부위 이름 → 나중에 치는 부위인가(받침 · PAD · 디딤판 — 부위 목록의 '나중에 침', JACK 2026-10-15). 부재의 부위 칸(DH_Part)으로 본다 —
    /// 유형 이름으로 짐작하지 않는다. 없으면 모두 구조체(전과 같다).
    /// </param>
    public static Scene Read(Document doc, ICollection<ElementId> selection, Func<string, bool>? laterPart = null)
    {
        var scene = new Scene { Selected = selection.Count };
        foreach (var id in DimensionExtractor.CollectApplicable(doc))
        {
            var el = doc.GetElement(id);
            if (el == null) continue;
            bool target = selection.Count == 0 || selection.Contains(id);
            // 대상은 산출식 자동입력과 같다: 콘크리트 부재(무슨 재료인지 알 수 없는 부재는 빼지 않는다).
            var material = ConcreteFilter.Classify(doc, el);
            if (material.Kind == ConcreteFilter.Kind.NotConcrete) { if (target) scene.NotConcrete++; continue; }

            List<ShapeSolid> solids;
            try { solids = SolidReader.Read(el); }
            catch { if (target) scene.ReadErrors++; else scene.NeighbourReadErrors++; continue; }
            if (solids.Count == 0) { if (target) scene.NoSolid++; continue; }

            string type = (doc.GetElement(el.GetTypeId()) as ElementType)?.Name ?? el.Name;
            string part = ZoneScene.Text(el, ZoneScene.PartParam).Trim();
            if (target)
            {
                scene.Targets++;
                if (material.Kind == ConcreteFilter.Kind.Unknown) scene.UnknownMaterial++;
                ReadCurved(el, solids, scene);
            }
            scene.Elements.Add(new FormworkElement
            {
                Id = id.Value,
                Category = el.Category?.Name ?? "",
                Kind = CalcSheetExporter.KindOf(el),
                Type = type,
                Zone = ZoneAssigner.Current(el),
                Part = part,            // ② 대응표의 부위 줄 · 부위의 처음 값(R47)
                Mark = MarkOf(el),      // 엑셀 줄의 부호(R47)
                Solids = solids,
                Target = target,
                // 버림(무근)인지의 초안: 유형·재료 이름에 '무근'·'버림'. 이름으로 모르는 유형은 대응표에서 사람이 정한다.
                Blinding = FormworkTable.LooksLikeBlinding(type, material.Material, part),
                Later = part.Length > 0 && laterPart != null && laterPart(part),
            });
        }

        FindLaterCuts(doc, scene);

        // 안쪽 면에 '어느 공간의 안쪽인지' 이름을 붙이려면 공간의 모양(구역)이 있어야 한다. 10/13 부터 위치는 부재를 찍어서 정하므로
        // 공간의 모양이 없다 — Zones 를 비워 두고, 안쪽 면은 '내측'으로만 낸다(면이 바라보는 공간의 이름은 거푸집 다음 단계에서 다시 정한다).
        return scene;
    }

    /// <summary>
    /// 부재의 «마크»(Revit Mark — 한국어 화면의 속성 이름은 «마크», ko-KR 리소스에서 확인) — 도면 부호(W1 · S1 …)를 넣는 칸. 비었으면 "".
    /// 엑셀 거푸집 줄의 부호가 된다(비었으면 유형 이름 — R47).
    /// </summary>
    public static string MarkOf(Element el)
    {
        try { return el.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)?.AsString()?.Trim() ?? ""; }
        catch { return ""; }
    }

    // 나중에 치는 부재와 결합된 구조체 가운데, 나중 부재가 자르는 쪽(IsCuttingElementInJoin)인 쌍을 모은다. 읽기만 한다.
    private static void FindLaterCuts(Document doc, Scene scene)
    {
        var known = new Dictionary<long, FormworkElement>();
        foreach (var e in scene.Elements) known[e.Id] = e;
        foreach (var later in scene.Elements.Where(e => e.Later))
        {
            var el = doc.GetElement(new ElementId(later.Id));
            if (el == null) continue;
            ICollection<ElementId> joined;
            try { joined = JoinGeometryUtils.GetJoinedElements(doc, el); }
            catch { continue; }
            foreach (var id in joined)
            {
                if (!known.TryGetValue(id.Value, out var other) || other.Later) continue;
                try
                {
                    if (doc.GetElement(id) is Element cut && JoinGeometryUtils.IsCuttingElementInJoin(doc, el, cut)) scene.LaterCuts.Add((later.Id, id.Value));
                }
                catch { }   // 결합 상태를 읽지 못하면 넘어간다(알림만 못 할 뿐 계산은 그대로)
            }
        }
    }

    /// <summary>
    /// 곡면이 있는 부재의 곡면을 세모로 나눠 둔다(Revit 의 Face.Triangulate). 계산에는 쓰지 않고, '확인할 자리'로 분홍을 칠할 때만 쓴다
    /// — 곡면은 넓이는 세지만 맞닿은 자리와 안팎을 재지 못해 확인을 청하는데, 색이 없으면 '세지 않음'으로 읽힌다.
    /// 면의 차례는 SolidReader 가 읽은 차례와 같다(같은 입체를 같은 방법으로 한 번 더 받는다). 수가 다르면 믿지 않고 건너뛴다.
    /// </summary>
    private static void ReadCurved(Element el, List<ShapeSolid> shapes, Scene scene)
    {
        if (!shapes.Any(s => s.Faces.Any(f => f.Kind != "PlanarFace"))) return;
        try
        {
            var solids = SolidReader.Solids(el);
            if (solids.Count != shapes.Count) return;
            for (int s = 0; s < solids.Count; s++)
            {
                if (solids[s].Faces.Size != shapes[s].Faces.Count) continue;
                int f = -1;
                foreach (Face face in solids[s].Faces)
                {
                    f++;
                    if (face is PlanarFace) continue;
                    try
                    {
                        // 같은 면인지 한 번 더 본다(종류와 넓이) — 차례가 어긋났다면 엉뚱한 면에 칠하지 않는다.
                        var read = shapes[s].Faces[f];
                        if (read.Kind != face.GetType().Name || Math.Abs(read.Area - face.Area * FtToM * FtToM) > 1e-6 + 1e-6 * read.Area) continue;
                        var mesh = face.Triangulate();
                        if (mesh == null || mesh.NumTriangles == 0) continue;
                        var list = new List<double[]>(mesh.NumTriangles * 3);
                        for (int t = 0; t < mesh.NumTriangles; t++)
                        {
                            var triangle = mesh.get_Triangle(t);
                            XYZ a = triangle.get_Vertex(0), b = triangle.get_Vertex(1), c = triangle.get_Vertex(2);
                            // 색은 세모의 법선 쪽으로 띄워 그린다 → 세모가 면의 바깥을 보게 돌려 둔다.
                            // 면의 바깥 법선은 ComputeNormal(문서: 입체 밖을 본다). 그 자리를 찾지 못하면 양쪽으로 다 넣는다.
                            var hit = face.Project((a + b + c) / 3);
                            if (hit == null)
                            {
                                list.Add(Pt(a)); list.Add(Pt(c)); list.Add(Pt(b));
                            }
                            else if ((b - a).CrossProduct(c - a).DotProduct(face.ComputeNormal(hit.UVPoint)) < 0) (b, c) = (c, b);
                            list.Add(Pt(a)); list.Add(Pt(b)); list.Add(Pt(c));
                        }
                        scene.CurvedTriangles[(el.Id.Value, s, f)] = list;
                    }
                    catch { /* 이 면은 색을 칠하지 못한다 — 요약에 수를 적는다 */ }
                }
            }
        }
        catch { }
    }

    private static double[] Pt(XYZ p) => new[] { p.X * FtToM, p.Y * FtToM, p.Z * FtToM };

    /// <summary>
    /// 곡면 조각에 Revit 이 나눠 준 세모를 붙인다(색을 칠하려고 — Core 는 곡면의 세모를 만들지 못한다, R42 core M-3).
    /// 세모를 얻지 못한 곡면 가운데 칠할 것(세는 것 · 확인할 것)의 수를 돌려준다.
    /// </summary>
    public static int AttachCurved(Scene scene, IReadOnlyList<ElementFormwork> results)
    {
        int missing = 0;
        foreach (var face in results.SelectMany(r => r.Faces))
        {
            if (face.Class != FaceClass.Curved) continue;
            if (scene.CurvedTriangles.TryGetValue((face.ElementId, face.Solid, face.Face), out var triangles) && triangles.Count >= 3)
                foreach (var patch in face.Patches) patch.Triangles = triangles;
            else if (face.Patches.Any(p => p.Counted || p.NeedsCheck)) missing++;   // 대응표에서 세지 않기로 한 곡면은 칠할 것이 없다
        }
        return missing;
    }

    /// <summary>사람이 고친 대응표를 두는 파일(프로젝트마다 하나).</summary>
    public static string RulesPath(Document doc)
    {
        string name = doc.Title;
        foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DH.Takeoff", "map", name + "_거푸집표.tsv");
    }

    /// <summary>면마다의 결과표(TSV).</summary>
    public static string WriteLog(Document doc, IReadOnlyList<ElementFormwork> results, FormworkOptions options)
    {
        string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DH.Takeoff", "log");
        Directory.CreateDirectory(dir);
        string name = doc.Title;
        foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        string path = Path.Combine(dir, name + "_formwork.tsv");

        // 면넓이 = Revit 이 준 넓이, 맞닿음 = 다른 콘크리트가 붙은(또는 그 속에 묻힌) 넓이, 포개짐 = 다른 부재의 면과 같은 평면으로 겹쳐 그쪽에서 세는 넓이,
        // 드러남 = 나머지. 조각 = 드러난 자리를 안팎·높이 구간·받침 높이로 나눈 것(X = 세지 않음). 가린부재 = 맞닿은 이웃 Id:넓이.
        const string T = "\t";
        var ic = CultureInfo.InvariantCulture;
        var sb = new StringBuilder(string.Join(T, "id", "카테고리", "유형", "구역", "입체", "면", "종류", "기울기", "z아래", "z위",
            "면넓이", "맞닿음", "포개짐", "드러남", "거푸집", "조각", "가린부재", "표시") + "\n");
        foreach (var r in results)
            foreach (var f in r.Faces)
            {
                string patches = string.Join(" | ", f.Patches.Select(p => string.Join(" ", new[]
                {
                    p.Area.ToString("0.###", ic), p.Counted ? p.Item : "X", p.Side,
                    p.Counted && p.Item == FormworkTable.Euro ? FormworkTable.BandName(p.Band, options.Bands) : "",
                    p.Height > 0 ? "받침 " + p.Height.ToString("0.###", ic) + "m" : "", p.Note,
                }.Where(s => s.Length > 0))));
                string covers = string.Join(" ", f.Covers.Select(c => c.ElementId.ToString(ic) + ":" + c.Area.ToString("0.###", ic)));
                sb.Append(string.Join(T, r.Element.Id.ToString(ic), Cell(r.Element.Category), Cell(r.Element.Type), Cell(r.Element.Zone),
                    f.Solid.ToString(ic), f.Face.ToString(ic), f.Class.ToString(), f.SlopeDeg.ToString("0.#", ic),
                    (f.ZMin).ToString("0.###", ic), (f.ZMax).ToString("0.###", ic),
                    f.Gross.ToString("0.####", ic), f.Covered.ToString("0.####", ic), f.Shared.ToString("0.####", ic), f.Net.ToString("0.####", ic),
                    f.CountedArea.ToString("0.####", ic), Cell(patches), covers, Cell(f.Flag)) + "\n");
            }
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
        try
        {
            string hist = Path.Combine(dir, "history");
            Directory.CreateDirectory(hist);
            File.Copy(path, Path.Combine(hist, $"{name}_formwork_{DateTime.Now:yyyyMMdd_HHmmss}.tsv"), true);
        }
        catch { }
        return path;
    }

    private static string Cell(string s) => s.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
}
