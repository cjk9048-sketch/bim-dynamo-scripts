using System.Globalization;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using DH.Takeoff.Core;

namespace DH.Takeoff.Revit;

/// <summary>
/// Revit «계단»(Architecture.Stairs) 읽기 — R56c-1(JACK 2026-10-22 «중간슬래브의 계단이 붙는 세로면은 거푸집이 필요 없다» · 고름 «계단의 재질이 콘크리트면 수량산출에»).
/// 이번 판은 콘크리트 계단을 거푸집 엔진의 «둘레 콘크리트»로만 쓴다(맞닿은 · 덮인 면을 가리는 데 — 셈 대상 아님). 계단을 부재로 세는 일(식 · 칸 · 이름 · 겹침)은
/// 첫 실행 일지를 본 뒤(R56c-2): 런 · 참 · 형상을 어디서 읽는지 · 체적 칸 · 구조 깊이를 잰 방향이 공식 문서에 없다(document-specialist 1022) — 그래서 계단마다 값을 일지에 남긴다.
/// 계단 하나가 깨져도 그 계단만 건너뛴다.
/// </summary>
public static class StairReader
{
    private const double FtToM = 0.3048;

    /// <summary>계단 하나: 형상(장면 좌표가 아닌 모델 m) · 콘크리트인가 · 일지 한 줄.</summary>
    public sealed record Stair(Element Element, string Type, bool Concrete, List<ShapeSolid> Solids, string Line);

    /// <summary>모델의 Revit 계단 전부(계단 하나마다 — 런 · 참은 그 계단에 든다). 읽지 못한 계단은 일지 줄만(형상 없음).</summary>
    public static List<Stair> Read(Document doc)
    {
        var list = new List<Stair>();
        IEnumerable<Element> found;
        try { found = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Stairs).WhereElementIsNotElementType().ToElements().Where(e => e is Stairs); }
        catch { return list; }
        foreach (var el in found)
        {
            try { list.Add(ReadOne(doc, (Stairs)el)); }
            catch (Exception ex) { list.Add(new Stair(el, "", false, new List<ShapeSolid>(), $"Id {el.Id.Value}\t읽기 오류: {ex.Message}")); }
        }
        return list;
    }

    private static Stair ReadOne(Document doc, Stairs stairs)
    {
        string type = (doc.GetElement(stairs.GetTypeId()) as ElementType)?.Name ?? stairs.Name;
        var materials = new List<Material>();
        var parts = new List<string>();
        var concretePieces = new List<Element>();   // 재료가 콘크리트인 런 · 참 · 지지재(섞인 계단은 이것만 넣는다 — 설치 전 검토 M3)
        var bareLandings = new List<Element>();     // 재료를 읽지 못한 참(참 유형에는 재료 칸이 없다 — 일체형 콘크리트 런을 따른다 · 다시 검토 L5)
        bool monoConcreteRun = false;
        bool IsConcrete(List<Material> own) => own.Any(x => ConcreteFilter.KindOf(doc, x) == ConcreteFilter.Kind.Concrete);
        // 런: 일체형이면 타입의 재료 · 구조 깊이(문서: 일체형 런에만 뜻이 있다).
        foreach (var id in Safe(() => stairs.GetStairsRuns()))
        {
            if (doc.GetElement(id) is not StairsRun run) continue;
            var rt = doc.GetElement(run.GetTypeId()) as StairsRunType;
            bool mono = Try(() => rt?.IsMonolithic ?? false);
            var own = MaterialsOf(doc, run).ToList();
            if (mono && rt != null && doc.GetElement(Try(() => rt.MaterialId) ?? ElementId.InvalidElementId) is Material m) own.Add(m);
            materials.AddRange(own);
            if (IsConcrete(own)) { concretePieces.Add(run); monoConcreteRun |= mono; }
            parts.Add(string.Create(CultureInfo.InvariantCulture,
                $"런 {id.Value}(폭 {Try(() => run.ActualRunWidth) * FtToM:0.###} · 챌면 수 {Try(() => run.ActualRisersNumber)} · 디딤 수 {Try(() => run.ActualTreadsNumber)} · 높이 {Try(() => run.Height) * FtToM:0.###}" +
                $" · 일체형 {(mono ? "O" : "X")} · 구조 깊이 {(mono ? Try(() => rt!.StructuralDepth) * FtToM : 0):0.###} · 처음 챌면 {(Try(() => run.BeginsWithRiser) ? "O" : "X")} · 끝 챌면 {(Try(() => run.EndsWithRiser) ? "O" : "X")}" +
                $" · 재료 {own.Count})"));
        }
        foreach (var id in Safe(() => stairs.GetStairsLandings()))
        {
            if (doc.GetElement(id) is not StairsLanding landing) continue;
            var lt = doc.GetElement(landing.GetTypeId()) as StairsLandingType;
            var own = MaterialsOf(doc, landing).ToList();
            materials.AddRange(own);
            if (IsConcrete(own)) concretePieces.Add(landing);
            else if (own.Count == 0) bareLandings.Add(landing);
            parts.Add(string.Create(CultureInfo.InvariantCulture,
                $"참 {id.Value}(두께 {Try(() => landing.Thickness) * FtToM:0.###} · 일체형 {(Try(() => lt?.IsMonolithic ?? false) ? "O" : "X")} · 재료 {own.Count})"));
        }
        // 지지재(스트링거 · 받침): 강재 지지재가 계단 쪽 재료에 안 잡히면 «섞임»을 놓친다(다시 검토 L6).
        foreach (var id in Safe(() => stairs.GetStairsSupports()))
        {
            if (doc.GetElement(id) is not Element support) continue;
            var own = MaterialsOf(doc, support).ToList();
            materials.AddRange(own);
            if (IsConcrete(own)) concretePieces.Add(support);
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"지지재 {id.Value}(재료 {own.Count})"));
        }
        materials.AddRange(MaterialsOf(doc, stairs));
        var kinds = materials.Select(m => (m, kind: ConcreteFilter.KindOf(doc, m))).ToList();
        bool concrete = kinds.Any(k => k.kind == ConcreteFilter.Kind.Concrete);
        // 섞인 계단(콘크리트 참 + 강재 런 · 강재 지지재 …): 계단 형상 전체를 넣으면 강재 자리까지 «맞닿은 콘크리트»가 되어 슬래브 · 벽 거푸집이 빠진다 —
        // 재료가 콘크리트인 런 · 참의 형상만 넣는다(설치 전 검토 M3). 콘크리트인 런 · 참이 없으면(계단 쪽 재료만 콘크리트) 넣지 않고 일지에 남긴다.
        bool mixed = concrete && kinds.Any(k => k.kind == ConcreteFilter.Kind.NotConcrete);

        // 형상: 계단에서(인스턴스 속까지 — SolidReader) · 없으면 런 · 참마다. 섞였으면 콘크리트 런 · 참에서만.
        string source = "계단";
        List<ShapeSolid> solids = mixed ? new List<ShapeSolid>() : Try(() => SolidReader.Read(stairs)) ?? new List<ShapeSolid>();
        if (mixed)
        {
            if (monoConcreteRun) concretePieces.AddRange(bareLandings);   // 재료를 못 읽은 참은 일체형 콘크리트 런을 따른다
            source = $"섞임 — 콘크리트 런·참·지지재 {concretePieces.Count}개만{(monoConcreteRun && bareLandings.Count > 0 ? $"(재료 없는 참 {bareLandings.Count}개 포함)" : "")}";
            foreach (var piece in concretePieces) solids.AddRange(Try(() => SolidReader.Read(piece)) ?? new List<ShapeSolid>());
        }
        else if (solids.Count == 0)
        {
            source = "런·참";
            foreach (var id in Safe(() => stairs.GetStairsRuns()).Concat(Safe(() => stairs.GetStairsLandings())))
                if (doc.GetElement(id) is Element piece) solids.AddRange(Try(() => SolidReader.Read(piece)) ?? new List<ShapeSolid>());
        }
        double volume = solids.Sum(s => s.Volume);
        double hostVolume = Try(() => stairs.get_Parameter(BuiltInParameter.HOST_VOLUME_COMPUTED)?.AsDouble() ?? -1);
        int stories = Try(() => stairs.NumberOfStories);
        bool copy = Try(() => stairs.MultistoryStairsId != ElementId.InvalidElementId);
        string line = string.Create(CultureInfo.InvariantCulture,
            $"Id {stairs.Id.Value}\t{type}\t{(concrete ? "콘크리트" : "콘크리트 아님")}\t재료 {string.Join(", ", kinds.Select(k => $"{k.m.Name}({k.kind})").Distinct())}" +
            $"\t디딤 {Try(() => stairs.ActualTreadDepth) * FtToM:0.###} · 챌면 {Try(() => stairs.ActualRiserHeight) * FtToM:0.###} · 챌면 수 {Try(() => stairs.ActualRisersNumber)} · 디딤 수 {Try(() => stairs.ActualTreadsNumber)}" +
            $" · 높이 {Try(() => stairs.Height) * FtToM:0.###} · 층 {stories}{(copy ? " · 다층 계단의 일부" : "")}" +
            $"\t형상 {source} · 입체 {solids.Count} · 체적 {volume:0.####} m3{(hostVolume >= 0 ? $" · 체적 칸 {hostVolume * FtToM * FtToM * FtToM:0.####} m3" : " · 체적 칸 없음")}" +
            $"\t{string.Join(" · ", parts)}");
        return new Stair(stairs, type, concrete, solids, line);
    }

    private static IEnumerable<Material> MaterialsOf(Document doc, Element el)
    {
        ICollection<ElementId> ids;
        try { ids = el.GetMaterialIds(false); }
        catch { return Array.Empty<Material>(); }
        return ids.Select(doc.GetElement).OfType<Material>().ToList();
    }

    private static ICollection<ElementId> Safe(Func<ICollection<ElementId>> get)
    {
        try { return get() ?? new List<ElementId>(); }
        catch { return new List<ElementId>(); }
    }

    private static T? Try<T>(Func<T> get)
    {
        try { return get(); }
        catch { return default; }
    }
}
