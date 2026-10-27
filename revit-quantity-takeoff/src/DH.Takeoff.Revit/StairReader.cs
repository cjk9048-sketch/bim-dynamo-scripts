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

    /// <summary>계단 하나: 형상(장면 좌표가 아닌 모델 m) · 콘크리트인가 · 일지 한 줄 · 런마다 챌면(높이 · 폭 — R65 거푸집 챌면 빼기).</summary>
    public sealed record Stair(Element Element, string Type, bool Concrete, List<ShapeSolid> Solids, string Line, List<(double height, double width)> Risers);

    /// <summary>
    /// 셈 대상 계단의 Id(R65 — JACK 2026-10-27 «계단 산출»): 재료가 콘크리트이고 다른 재료가 섞이지 않았고 입체가 있는 Revit 계단. 모든 읽기(모으기 —
    /// DimensionExtractor.CollectAll · 재료 — ConcreteFilter · 거푸집 — FormworkReader)가 이 판정 하나를 쓴다(critic R65 H1 · H2). 섞인 계단 · 콘크리트가
    /// 아닌 계단은 전(R56c-1)처럼 거푸집의 둘레 콘크리트로만 쓴다. 계단 하나가 깨져도 그 계단만 뺀다.
    /// </summary>
    public static List<ElementId> CountableIds(Document doc)
    {
        var ids = new List<ElementId>();
        IEnumerable<Element> found;
        try { found = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Stairs).WhereElementIsNotElementType().ToElements().Where(e => e is Stairs); }
        catch { return ids; }
        foreach (var el in found)
        {
            try
            {
                var (concrete, mixed, _) = Judge(doc, (Stairs)el);
                if (concrete && !mixed && SolidReader.Solids(el).Any(s => s.Volume > 1e-9)) ids.Add(el.Id);
            }
            catch { /* 읽지 못한 계단은 셈 대상이 아니다(일지 — Read) */ }
        }
        return ids;
    }

    /// <summary>
    /// 계단의 재료 판정(R65 — ReadOne 과 같은 재료: 런(일체형이면 타입 재료) · 참 · 지지재 · 계단): 콘크리트 재료가 있는가 · 콘크리트가 아닌 재료도 섞였는가 ·
    /// 대표 재료 이름(콘크리트 재료 먼저).
    /// </summary>
    public static (bool concrete, bool mixed, string material) Judge(Document doc, Stairs stairs)
    {
        var materials = new List<Material>();
        foreach (var id in Safe(() => stairs.GetStairsRuns()))
        {
            if (doc.GetElement(id) is not StairsRun run) continue;
            var rt = doc.GetElement(run.GetTypeId()) as StairsRunType;
            materials.AddRange(MaterialsOf(doc, run));
            if (Try(() => rt?.IsMonolithic ?? false) && rt != null && doc.GetElement(Try(() => rt.MaterialId) ?? ElementId.InvalidElementId) is Material m) materials.Add(m);
        }
        foreach (var id in Safe(() => stairs.GetStairsLandings()).Concat(Safe(() => stairs.GetStairsSupports())))
            if (doc.GetElement(id) is Element piece) materials.AddRange(MaterialsOf(doc, piece));
        materials.AddRange(MaterialsOf(doc, stairs));
        var kinds = materials.Select(m => (m, kind: ConcreteFilter.KindOf(doc, m))).ToList();
        bool concrete = kinds.Any(k => k.kind == ConcreteFilter.Kind.Concrete);
        bool mixed = concrete && kinds.Any(k => k.kind == ConcreteFilter.Kind.NotConcrete);
        string name = kinds.OrderBy(k => k.kind == ConcreteFilter.Kind.Concrete ? 0 : 1).Select(k => k.m.Name ?? "").FirstOrDefault() ?? "";
        return (concrete, mixed, name);
    }

    /// <summary>
    /// 계단과 겹친 콘크리트(R65 — 계단은 형상 결합이 되지 않아 겹친 자리는 늘 계단에서 뺀다 · critic R65 M4): 계단 상자에 걸친 대상 범주 부재 가운데
    /// 콘크리트가 아니라고 뚜렷한 것 · 거푸집 객체 · 계단은 빼고, 입체끼리 겹친 체적(m³)을 더한다(0.1 L 미만은 버림 — 겹침 검사와 같은 잣대).
    /// 계산하지 못한 쌍은 failed 로 센다(일지).
    /// </summary>
    public static (double m3, List<(long id, double m3)> with, int failed) Overlap(Document doc, Stairs stairs)
    {
        var with = new List<(long, double)>();
        int failed = 0;
        var mine = Try(() => SolidReader.Solids(stairs)) ?? new List<Solid>();
        var bb = Try(() => stairs.get_BoundingBox(null));
        if (mine.Count == 0 || bb == null) return (0, with, 0);
        var filter = new LogicalAndFilter(new ElementMulticategoryFilter(DimensionExtractor.MemberCategories), new BoundingBoxIntersectsFilter(new Outline(bb.Min, bb.Max)));
        var collector = new FilteredElementCollector(doc).WhereElementIsNotElementType().WherePasses(filter);
        var formwork = FormworkObjects.Find(doc);
        if (formwork.Count > 0) collector = collector.Excluding(formwork);
        foreach (var other in collector)
        {
            try
            {
                if (ConcreteFilter.Classify(doc, other).Kind == ConcreteFilter.Kind.NotConcrete) continue;
                double m3 = OverlapChecker.Intersection(mine, SolidReader.Solids(other), out bool bad);
                if (bad) failed++;
                if (m3 >= OverlapChecker.MinOverlapM3) with.Add((other.Id.Value, m3));
            }
            catch { failed++; }
        }
        return (with.Sum(w => w.Item2), with, failed);
    }

    /// <summary>계단의 BIM 물량(R65): 입체 체적 − 겹친 체적(m³ — 계단 식과 같은 뺄셈 · critic R65 M5).</summary>
    public static double NetVolume(Stairs stairs)
    {
        double solid = SolidReader.Read(stairs).Sum(s => s.Volume);
        return solid - Overlap(stairs.Document, stairs).m3;
    }

    /// <summary>계단마다 마지막으로 지은 식의 일지 한 줄(R65 — 수량산출 일지 «계단» 줄에 붙인다).</summary>
    public static readonly Dictionary<long, string> LastFormula = new();

    /// <summary>
    /// 계단 콘크리트 식(R65 — Core StairFormula · 녹촌 2D 꼴): 런(폭 · 디딤 수 · 챌면 · 디딤 · 구조 깊이 · 바닥에 섰는가)과 참(일반 분해기로 푼 식 · 못 풀면 환산)으로 짓고,
    /// 겹침을 빼기 전 값이 계단 입체 체적과 ±1% 안이면 쓴다(critic R65 M5) · 안 맞으면 일반 분해기로 계단 입체 전체 · 그것도 안 되면 미산출(까닭).
    /// 돌려주는 net = 입체 − 겹침(BIM 물량).
    /// </summary>
    public static (DecompResult result, double net, int solids) Formula(Document doc, Stairs stairs)
    {
        var shapes = SolidReader.Read(stairs);
        double volume = shapes.Sum(s => s.Volume);
        var (overlap, with, failed) = Overlap(doc, stairs);
        double net = volume - overlap;
        string overlapNote = string.Create(CultureInfo.InvariantCulture,
            $"겹침 {overlap:0.####} m3{(with.Count > 0 ? "(" + string.Join(", ", with.Select(w => $"{w.id} {w.m3:0.####}")) + ")" : "")}{(failed > 0 ? $" · 못 잰 쌍 {failed}" : "")}");
        double treadDepth = Try(() => stairs.ActualTreadDepth) * FtToM;
        var runs = new List<StairFormula.Run>();
        var runNotes = new List<string>();
        double widest = 0;
        foreach (var id in Safe(() => stairs.GetStairsRuns()))
        {
            if (doc.GetElement(id) is not StairsRun run) continue;
            var rt = doc.GetElement(run.GetTypeId()) as StairsRunType;
            bool mono = Try(() => rt?.IsMonolithic ?? false);
            int risers = Try(() => run.ActualRisersNumber);
            double height = Try(() => run.Height) * FtToM;
            var r = new StairFormula.Run(id.Value, Try(() => run.ActualRunWidth) * FtToM, Try(() => run.ActualTreadsNumber), risers > 0 ? height / risers : 0,
                                         treadDepth, mono ? Try(() => rt!.StructuralDepth) * FtToM : 0, Math.Abs(Try(() => run.BaseElevation)) * FtToM < 0.001);
            double solid = Try(() => SolidReader.Read(run).Sum(s => s.Volume));
            // 참 없이 바닥에서 끝나는 런은 끝 챌면 · 허리 끝이 런 입체에 들 수 있다(설치 전 검토 열린 질문): 디딤 수로 맞지 않고 디딤 수 + 1(= 챌면 수)로 런 입체와
            // ±1% 안이면 그 수로 쓴다(일지 «디딤+1»). 계단 전체는 아래에서 다시 ±1% 로 견준다.
            bool plusOne = false;
            if (solid > 0 && StairFormula.Usable(r) && !FormulaDecomposer.Matches(StairFormula.RunVolume(r), solid))
            {
                var more = r with { Treads = r.Treads + 1 };
                if (FormulaDecomposer.Matches(StairFormula.RunVolume(more), solid)) { r = more; plusOne = true; }
            }
            runs.Add(r);
            widest = Math.Max(widest, r.Width);
            runNotes.Add(string.Create(CultureInfo.InvariantCulture,
                $"런 {id.Value}(L {StairFormula.Slope(r):0.###} · 디딤 {r.Treads}{(plusOne ? "(디딤+1)" : "")} · 챌면 {r.Riser:0.####} · 쐐기 {(r.OnFloor ? "O" : "X")} · 식 {StairFormula.RunVolume(r):0.####} / 입체 {solid:0.####})"));
        }
        var landings = new List<StairFormula.Landing>();
        foreach (var id in Safe(() => stairs.GetStairsLandings()))
        {
            if (doc.GetElement(id) is not StairsLanding landing) continue;
            double thickness = Try(() => landing.Thickness) * FtToM;
            var solids = Try(() => SolidReader.Read(landing)) ?? new List<ShapeSolid>();
            double v = solids.Sum(s => s.Volume);
            var d = solids.Count > 0 ? Try(() => FormulaDecomposer.Decompose(solids, thickness: thickness)) : null;
            bool solved = d is { Ok: true } && FormulaDecomposer.Matches(d.Value, v);
            landings.Add(new StairFormula.Landing(id.Value, solved ? d!.Formula : "", v, widest, thickness));
            runNotes.Add(string.Create(CultureInfo.InvariantCulture, $"참 {id.Value}({(solved ? "풂 " + d!.Formula : "환산")} · 입체 {v:0.####})"));
        }

        DecompResult result;
        string how;
        if (runs.Count > 0 && runs.All(StairFormula.Usable) && volume > 0)
        {
            var built = StairFormula.Build(runs, landings, overlap);
            how = string.Create(CultureInfo.InvariantCulture,
                $"런·참 식 {built.Gross:0.####} / 입체 {volume:0.####}({(built.Gross - volume) / volume * 100:+0.##;-0.##;0}%) · 쐐기 {built.Wedges:0.####} · 참 {built.Landings:0.####}(환산 {built.Converted})");
            if (FormulaDecomposer.Matches(built.Gross, volume))
            {
                result = new DecompResult
                {
                    Ok = true, Formula = built.Formula, Value = built.Value, Exact = built.Value, ModelVolume = net,
                    Kind = string.Create(CultureInfo.InvariantCulture, $"계단(런 {runs.Count} · 참 {landings.Count}{(built.Converted > 0 ? $" — 환산 {built.Converted}" : "")}{(overlap >= 0.0005 ? $" · 겹침 {overlap:0.###} m3 뺌" : "")})"),
                };
                LastFormula[stairs.Id.Value] = $"{how}\t{overlapNote}\t식 {built.Formula}\t{string.Join(" · ", runNotes)}";
                return (result, net, shapes.Count);
            }
        }
        else how = runs.Count == 0 ? "런 없음" : "런 값 모자람(폭 · 디딤 · 챌면 · 구조 깊이 — 일체형이 아닌 런?)";

        // 런 · 참 식이 안 맞으면 일반 분해기로 계단 입체 전체(겹침은 끝에 뺀다).
        var whole = shapes.Count > 0 ? FormulaDecomposer.Decompose(shapes) : new DecompResult { Ok = false, Reason = "입체 없음" };
        if (whole.Ok && FormulaDecomposer.Matches(whole.Value, volume))
        {
            string formula = overlap >= 0.0005 ? (whole.Formula.Contains('+') || whole.Formula.Contains('-') ? "(" + whole.Formula + ")" : whole.Formula) + "-" + StairFormula.N(overlap) : whole.Formula;
            double value = FormulaEvaluator.Evaluate(formula);
            result = new DecompResult { Ok = true, Formula = formula, Value = value, Exact = value, ModelVolume = net, Kind = "계단(일반 분해 — " + how + ")" };
        }
        else result = new DecompResult
        {
            Ok = false, ModelVolume = net, Formula = whole.Formula, Value = whole.Value,
            Reason = "계단 식이 입체와 맞지 않음: " + how + " · 일반 분해 " + (whole.Ok ? string.Create(CultureInfo.InvariantCulture, $"{whole.Value:0.####}") : whole.Reason),
        };
        LastFormula[stairs.Id.Value] = $"{how}\t{overlapNote}\t{(result.Ok ? "식 " + result.Formula : "미산출 " + result.Reason)}\t{string.Join(" · ", runNotes)}";
        return (result, net, shapes.Count);
    }

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
            catch (Exception ex) { list.Add(new Stair(el, "", false, new List<ShapeSolid>(), $"Id {el.Id.Value}\t읽기 오류: {ex.Message}", new())); }
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
        var risers = new List<(double height, double width)>();
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
            int count = Try(() => run.ActualRisersNumber);
            if (count > 0) risers.Add((Try(() => run.Height) * FtToM / count, Try(() => run.ActualRunWidth) * FtToM));
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
        return new Stair(stairs, type, concrete, solids, line, risers);
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
