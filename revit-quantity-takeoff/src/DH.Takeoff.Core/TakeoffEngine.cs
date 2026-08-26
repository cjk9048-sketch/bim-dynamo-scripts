using System.Globalization;
using System.Text.RegularExpressions;

namespace DH.Takeoff.Core;

/// <summary>
/// 산출 엔진 — 현행 VBA(GenerateSmartBIMReport) 의미를 이식.
/// 인스턴스별로 (공종)수식의 토큰을 net 치수·Calc 값으로 치환→동일 수식문자열 그룹화→
/// ROUND(값×개수) 합산. (기둥,콘크리트) 등 Representative 조합은 '대표 1개'로 계상.
/// 골든(Quantity_Report)을 재현함이 golden_verify.py 로 검증됨.
/// </summary>
public sealed class TakeoffEngine
{
    private static readonly string[] LocalKeys = { "L1", "L2", "L3", "W1", "W2", "W3", "H", "ETC" };
    private static readonly HashSet<string> LocalSet = new(LocalKeys);
    // 토큰: [영문/숫자/밑줄]. 단일 패스 치환으로 부분문자열 충돌([H1] vs [H1_haunch]) 방지.
    private static readonly Regex TokenRx = new(@"\[[A-Za-z0-9_]+\]", RegexOptions.Compiled);

    private readonly WorkbookModel _m;

    public TakeoffEngine(WorkbookModel model) => _m = model;

    public TakeoffResult Compute(IReadOnlyList<Instance> instances)
    {
        var code2cat = new Dictionary<string, string>();
        foreach (var i in instances)
            if (!code2cat.ContainsKey(i.Code)) code2cat[i.Code] = i.Category;

        // disc -> code -> 치환된수식 -> 개수
        var groups = new Dictionary<string, Dictionary<string, Dictionary<string, int>>>();
        foreach (var inst in instances)
        {
            if (!_m.Formulas.TryGetValue(inst.Code, out var byDisc)) continue;
            foreach (var (disc, pattern) in byDisc)
            {
                foreach (var rawSub in pattern.Split('|'))
                {
                    var sub = rawSub.Trim();
                    if (sub.Length == 0) continue;
                    var f = Substitute(sub, inst);
                    if (f.Contains('[')) continue; // 미해결 토큰 → skip (Calc 미공급 등)
                    var fmap = Nested(Nested(groups, disc), inst.Code);
                    fmap[f] = fmap.TryGetValue(f, out var c) ? c + 1 : 1;
                }
            }
        }

        var res = new TakeoffResult();
        foreach (var (disc, byCode) in groups)
        {
            double discTotal = 0;
            foreach (var (code, fmap) in byCode)
            {
                bool rep = _m.Representative.Contains((code2cat.GetValueOrDefault(code, ""), disc));
                double subtotal = 0;
                foreach (var (f, cnt) in fmap)
                {
                    double v;
                    try { v = FormulaEvaluator.Evaluate(f); }
                    catch (FormatException) { continue; } // 평가 불가 수식 skip (Python except: pass 의미)
                    if (!double.IsFinite(v)) continue;     // 0 나눗셈 등 NaN/Inf skip
                    subtotal += Round2(v * (rep ? 1 : cnt));
                }
                Nested(res.CodeSubtotal, disc)[code] = Round2(subtotal);
                discTotal += subtotal;
            }
            res.DisciplineTotal[disc] = Round2(discTotal);
        }
        return res;
    }

    /// 패턴 1개(| 분할 후, # 라벨 제거)를 net 치수·Calc 로 단일패스 치환.
    /// 토큰 경계가 명확해 [H1]/[H1_haunch], [L1]/[L1_L_Long] 부분문자열 충돌이 없다.
    private string Substitute(string sub, Instance inst)
    {
        int h = sub.IndexOf('#');
        if (h >= 0) sub = sub[(h + 1)..];
        return TokenRx.Replace(sub, m =>
        {
            string tok = m.Value;          // 예: "[L1]"
            string key = tok[1..^1];        // 예: "L1"
            if (key == "H1") return Num(inst.Dims.GetValueOrDefault("H")); // [H1] 토큰 = 인스턴스 H
            if (LocalSet.Contains(key)) return Num(inst.Dims.GetValueOrDefault(key));
            if (_m.Calc.TryGetValue(tok, out var cv)) return Num(cv);
            return tok;                     // 미해결 토큰은 그대로 → 상위에서 skip
        });
    }

    private static string Num(double d) => d.ToString("R", CultureInfo.InvariantCulture);

    // Excel ROUND = 반올림(half away from zero), 소수 2자리
    private static double Round2(double v) => Math.Round(v, 2, MidpointRounding.AwayFromZero);

    private static Dictionary<string, TV> Nested<TV>(Dictionary<string, Dictionary<string, TV>> d, string k)
        => d.TryGetValue(k, out var v) ? v : (d[k] = new());
}
