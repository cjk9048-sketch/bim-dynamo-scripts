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
        public bool Committed => Status == TransactionStatus.Committed;
    }

    private static readonly string[] Names = FormworkTable.GuidelineNames.Append(FormworkTable.TotalParam).ToArray();

    public static Outcome Write(Document doc, FormworkBoard board)
    {
        var outcome = new Outcome();
        SharedParameterManager.EnsureParameters(doc, Names);
        outcome.ParametersEnsured = true;

        // 부재마다 종류별 넓이(세는 조각만). 지침 이름이 없는 종류(사람이 대응표 파일에 적은 낯선 값)는 합계에만 든다.
        var values = new Dictionary<long, Dictionary<string, double>>();
        foreach (var r in board.Results)
        {
            var map = new Dictionary<string, double>();
            double total = 0;
            foreach (var p in r.Faces.SelectMany(f => f.Patches))
            {
                if (!p.Counted) continue;
                total += p.Area;
                if (FormworkTable.GuidelineName(p.Item, p.Item == FormworkTable.Euro ? p.Band : 0) is { } name) map[name] = map.GetValueOrDefault(name) + p.Area;
            }
            map[FormworkTable.TotalParam] = total;
            values[r.Element.Id] = map;
        }

        // 적어도 되는 부재인지는 트랜잭션을 열기 전에 본다(작업공유 상태는 트랜잭션 중에는 믿기 어렵다).
        var blocked = new Dictionary<long, string>();
        foreach (long id in values.Keys)
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
            outcome.Status = tx.Commit();
        }
        if (!outcome.Committed)
        {
            outcome.Failed.Insert(0, $"Revit 이 변경을 되돌렸습니다({outcome.Status}) — 거푸집 속성은 적히지 않았습니다");
            outcome.Total = 0;
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
}
