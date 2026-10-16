using Autodesk.Revit.DB;
using DH.Takeoff.Core.Formwork;

namespace DH.Takeoff.Revit;

/// <summary>
/// 거푸집 넓이를 부재 속성에 적는다(수량산출 창의 [완료] — JACK 2026-10-15 미리보기 확인): K-water 지침의 «내역적용수량» 이름으로
/// 종류마다 넓이(㎡) 한 칸씩 + 합계 DH_면적(거푸집). 그 부재에 없는 종류는 비운다(값이 없으면 숨는 칸 — 속성 창에는 가진 종류만 보인다).
/// 넓이는 수량산출 창의 ② 가 잰 것(FormworkBoard — 고친 대응표까지 건 것)을 쓴다. 한 트랜잭션이고, 커밋 뒤에 다시 읽어 본다.
/// </summary>
public static class FormworkWriter
{
    public sealed class Outcome
    {
        public int Elements;                                // 적은 부재 수
        public double Total;                                // 적은 넓이의 합(㎡)
        public bool ParametersEnsured;                      // 속성 칸을 만드는 트랜잭션을 돌렸다
        public TransactionStatus Status = TransactionStatus.Uninitialized;
        public List<string> Failed = new();                 // 적지 못한 까닭(부재마다)
        public int ReadBackDiffers;                         // 커밋 뒤 다시 읽은 값이 적은 값과 다른 칸 수
        public int StaleCleared;                            // 이번에 재지 않은 부재에서 «7m이하»로 옮긴 전 판의 «7m초과» 값(R46)
        public double StaleArea;                            // 그 넓이의 합(㎡)
        public List<string> StaleMoved = new();             // 옮긴 것(«Id 넓이» — 일지)
        public List<long> StaleLeft = new();                // 옮기지 못한 부재(편집할 수 없음 등 — 값은 그대로 · 알림 창에 Id)
        public bool Committed => Status == TransactionStatus.Committed;
    }

    private static readonly string[] Names = FormworkTable.GuidelineNames.Append(FormworkTable.TotalParam).ToArray();

    public static Outcome Write(Document doc, FormworkBoard board)
    {
        var outcome = new Outcome();
        SharedParameterManager.EnsureParameters(doc, Names);
        outcome.ParametersEnsured = true;

        // 부재마다 종류별 넓이(세는 조각만). 지침 이름이 없는 종류(사람이 대응표 파일에 적은 낯선 값)는 합계에만 든다.
        // ② 3D 에서 뺀 판(R48)은 엑셀 · 거푸집 객체에서만 빠진다 — 지침용 부재 속성은 그 부재 몫 그대로(FacePatch.CountedIgnoringDrop).
        var values = new Dictionary<long, Dictionary<string, double>>();
        foreach (var r in board.Results)
        {
            var map = new Dictionary<string, double>();
            double total = 0;
            foreach (var p in r.Faces.SelectMany(f => f.Patches))
            {
                if (!p.CountedIgnoringDrop) continue;
                total += p.Area;
                if (FormworkTable.GuidelineName(p.Item, p.Item == FormworkTable.Euro ? p.Band : 0) is { } name) map[name] = map.GetValueOrDefault(name) + p.Area;
            }
            map[FormworkTable.TotalParam] = total;
            values[r.Element.Id] = map;
        }

        // 높이 구간을 끈 기본(R46)에서는 «유로폼［보통, 7m초과］» 칸을 쓰지 않는다 — 전 판(R42 ~ R44b)이 적은 값이 이번에 재지 않은 부재
        // (콘크리트에서 빠진 부재 등)에 남으면 «유로폼［보통, 7m이하］»에 더한 뒤 비운다: 유로폼이 한 항목이 된 것이지 수량을 버리는 것이 아니다 —
        // 그 부재의 종류별 합 = 합계 칸 그대로(revit-verify R46 L3 · 설치 전 M1). 이번에 잰 부재는 아래의 줄에서 새로 적는다.
        var stale = board.Options.Bands.Count == 0 ? StaleHigh(doc).Where(id => !values.ContainsKey(id)).ToList() : new List<long>();

        // 적어도 되는 부재인지는 트랜잭션을 열기 전에 본다(작업공유 상태는 트랜잭션 중에는 믿기 어렵다).
        var blocked = new Dictionary<long, string>();
        foreach (long id in values.Keys.Concat(stale))
        {
            var el = doc.GetElement(new ElementId(id));
            string? why = el == null ? "부재를 찾을 수 없음" : FormulaAutoFill.WhyNotEditable(doc, el);
            if (why != null) blocked[id] = why;
        }
        var wrote = new List<long>();
        using (var tx = new Transaction(doc, "DH 거푸집 속성"))
        {
            tx.Start();
            foreach (var (id, map) in values)
            {
                if (blocked.TryGetValue(id, out var why)) { outcome.Failed.Add($"Id {id}: {why}"); continue; }
                var el = doc.GetElement(new ElementId(id))!;
                try
                {
                    var missing = Names.Where(n => el.get_Parameter(SharedParameterManager.Guids[n]) is not { IsReadOnly: false }).ToList();
                    if (missing.Count > 0) { outcome.Failed.Add($"Id {id}: 속성 칸이 없거나 읽기 전용 — {string.Join(", ", missing)}"); continue; }
                    foreach (string name in Names)
                    {
                        var param = el.get_Parameter(SharedParameterManager.Guids[name])!;
                        double v = Math.Round(map.GetValueOrDefault(name), 3);
                        if (name == FormworkTable.TotalParam || v > 0) param.Set(v);
                        else if (param.HasValue)
                        {
                            // 그 종류가 없는 부재: 비운다. 전의 판이 '값이 없으면 숨김' 없이 만든 칸이면 비울 수 없어 0 을 적는다.
                            try { if (!param.ClearValue()) param.Set(0.0); }
                            catch (Autodesk.Revit.Exceptions.InvalidOperationException) { param.Set(0.0); }
                        }
                    }
                    wrote.Add(id);
                    outcome.Total += map[FormworkTable.TotalParam];
                }
                catch (Exception ex) { outcome.Failed.Add($"Id {id}: {ex.Message}"); }
            }
            string low = FormworkTable.GuidelineName(FormworkTable.Euro, 0)!;
            foreach (long id in stale)
            {
                if (blocked.ContainsKey(id)) { outcome.StaleLeft.Add(id); continue; }
                // 부재마다 하위 트랜잭션 — 더한 뒤 비우다 실패하면 둘 다 원래대로(두 번 세거나 잃지 않게 — revit-verify R46 L1).
                using var sub = new SubTransaction(doc);
                try
                {
                    var el = doc.GetElement(new ElementId(id));
                    var high = el?.get_Parameter(SharedParameterManager.Guids[FormworkTable.HighName]);
                    var keep = el?.get_Parameter(SharedParameterManager.Guids[low]);
                    if (high is not { HasValue: true }) continue;
                    // 옮길 칸(7m이하)이 없거나 둘 중 하나라도 읽기 전용이면 손대지 않는다(값을 잃지 않게) — Id 만 알린다.
                    if (high.IsReadOnly || keep is not { IsReadOnly: false }) { outcome.StaleLeft.Add(id); continue; }
                    double area = high.AsDouble();
                    sub.Start();
                    keep.Set(Math.Round((keep.HasValue ? keep.AsDouble() : 0) + area, 3));
                    try { if (!high.ClearValue()) high.Set(0.0); }
                    catch (Autodesk.Revit.Exceptions.InvalidOperationException) { high.Set(0.0); }
                    if (sub.Commit() != TransactionStatus.Committed) { outcome.StaleLeft.Add(id); continue; }
                    outcome.StaleCleared++;
                    outcome.StaleArea += area;
                    outcome.StaleMoved.Add(FormattableString.Invariant($"{id} {area:0.###}"));
                }
                catch
                {
                    try { if (sub.HasStarted() && !sub.HasEnded()) sub.RollBack(); } catch { }
                    outcome.StaleLeft.Add(id);
                }
            }
            outcome.Status = tx.Commit();
        }
        if (!outcome.Committed)
        {
            outcome.Failed.Insert(0, $"Revit 이 변경을 되돌렸습니다({outcome.Status}) — 거푸집 속성은 적히지 않았습니다");
            outcome.Total = 0;
            outcome.StaleLeft = stale.ToList();   // 되돌려졌으면 옮긴 것도 없다
            outcome.StaleCleared = 0;
            outcome.StaleArea = 0;
            outcome.StaleMoved.Clear();
            return outcome;
        }
        outcome.Elements = wrote.Count;

        // 되읽기: 커밋 뒤에 새로 읽은 값이 적은 값과 같은가(비운 칸은 값이 없거나 0).
        foreach (long id in wrote)
        {
            var el = doc.GetElement(new ElementId(id));
            if (el == null) { outcome.ReadBackDiffers++; continue; }
            foreach (string name in Names)
            {
                var param = el.get_Parameter(SharedParameterManager.Guids[name]);
                double want = Math.Round(values[id].GetValueOrDefault(name), 3);
                double now = param is { HasValue: true } ? param.AsDouble() : 0;
                if (param == null || Math.Abs(now - want) > 1e-6) outcome.ReadBackDiffers++;
            }
        }
        return outcome;
    }

    // «유로폼［보통, 7m초과］» 칸에 0 보다 큰 값이 든 부재(그 칸이 모델에 없으면 없음).
    private static List<long> StaleHigh(Document doc)
    {
        try
        {
            if (SharedParameterElement.Lookup(doc, SharedParameterManager.Guids[FormworkTable.HighName]) is not { } shared) return new List<long>();
            var rule = ParameterFilterRuleFactory.CreateGreaterRule(shared.Id, 1e-9, 1e-12);
            // 칸이 묶인 범주만 먼저(빠른 거르기) — 큰 모델에서 모든 인스턴스를 느린 매개변수 거르기로 훑지 않게(revit-verify R46 L4).
            return new FilteredElementCollector(doc).WhereElementIsNotElementType()
                .WherePasses(new ElementMulticategoryFilter(SharedParameterManager.BoundCategories.ToList()))
                .WherePasses(new ElementParameterFilter(rule))
                .Select(e => e.Id.Value).ToList();
        }
        catch { return new List<long>(); }
    }
}
