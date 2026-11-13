using System.Globalization;
using System.Text.RegularExpressions;

namespace DH.Grading.Core;

/// <summary>★[v117.9.3 · JACK 1112 «안 되면 안 되는 사유를 직관적으로 — 초보가 알게 · 어요체 · 습니다체 말고 단답형»] [옹벽 추가] 거절 사유의 <b>화면 글</b>.
/// <para>꼴 = «무엇이 문제 — 왜 → 할 일» 한 줄 단답. 자리는 빨간 X가 보이므로 좌표는 안 씀 · 숫자는 할 일에 필요한 것만.</para>
/// <para>기술 문장(<c>Why</c>)은 그대로 둔다 — 진단 로그 · 시험이 쓴다(원인 찾기용). 프로그램 안쪽 계산 실패는 숨기지 않고 «계산 실패(프로그램 문제)»로 밝힌다.</para></summary>
public static class WallEasy
{
    static readonly CultureInfo Ci = CultureInfo.InvariantCulture;

    /// <summary>사용자가 손쓸 수 없는 안쪽 계산 실패.</summary>
    public const string Bug = "계산 실패(프로그램 문제) → 선을 조금 옮겨 다시 · 계속되면 진단 로그 전달";

    static string? Num(string s, string pattern)
    {
        var m = Regex.Match(s ?? "", pattern);
        if (!m.Success) return null;
        return double.TryParse(m.Groups[1].Value, NumberStyles.Float, Ci, out double v) ? v.ToString("0.00", Ci) : null;
    }

    static string Who(string why)
    {
        var m = Regex.Match(why ?? "", @"(?:자리가|띠가) (.+?)과 ");
        return m.Success ? $"({m.Groups[1].Value})" : "";
    }

    /// <summary>선 옹벽 짓기 거절(<see cref="WallByLine.Built"/>의 Code · Why) → 화면 글.</summary>
    /// <param name="road">★[v117.15] 진입도로 [옹벽 추가] — «끝 높이» 까닭에 «진입도로 면 오차»(옛 경계 = 도로 엔진이 셈한 데이라잇).</param>
    public static string Build(string? code, string? why, bool road = false)
    {
        why ??= "";
        switch (code ?? "")
        {
            case "자리":
            {
                string? need = Num(why, @"필요 (?:≥ )?([0-9.]+) m");
                string? have = Num(why, @"있는 거리 ([0-9.]+) m") ?? Num(why, @"부지 바닥까지 ([0-9.]+) m") ?? Num(why, @"부지 바닥에 ([0-9.]+) m까지");
                if (need != null && have != null)
                    return $"소단 넣을 폭 부족 — 선이 부지 바닥(계획선)에 너무 가까움(필요 {need} m · 있음 {have} m) → 선을 바닥에서 더 떼기 · 또는 수직만(V)";
                if (why.Contains("소단 자리가 없")) return "소단 넣을 자리 없음 — 선 바로 안쪽이 부지 바닥 · 경계 → 선을 바닥에서 더 떼기 · 또는 수직만(V)";
                if (why.Contains("선에 붙은 바꿀 자리가 없")) return "선 곁에 바꿀 사면 없음 — 선이 사면 띠를 안 지남 → 사면 띠 안에 다시 그리기";
                return Bug;
            }
            case "섞인 선": return "선이 깎는 땅(절토) · 쌓는 땅(성토)에 걸침 + 단 필요 → 절토 · 성토 따로 나눠 그리기 · 또는 수직만(V)";
            case "섞임 겹침": return "절토 · 성토가 바뀌는 자리에서 두 옹벽 자리가 겹침 → 그 자리 곁에서 선을 조금 옮기기";
            case "겹침":
                if (why.Contains("못 쟀")) return Bug;
                return $"다른 옹벽 · 구조물과 겹침{Who(why)} → 선 옮기기 · 또는 그 옹벽 먼저 [옹벽 삭제]" + (why.Contains("다단") ? " · 또는 수직만(V)" : "");
            case "구간":
                if (why.Contains("모든 구간이 단 없이")) return "저장된 구간 모두 단 필요 없음 → 다시 그리기(S) · 또는 수직만(V)";
                if (why.Contains("꺾은점")) return "저장된 구간 경계가 선 꺾인 곳 · 끝에 너무 가까움 → 다시 그리기(S)";
                return "저장된 구간 쓸 수 없음 → 다시 그리기(S)";
            case "단 수": return "단을 아무리 내려도 사면 아래에 안 닿음 → 단높이(H) 늘리기 · 또는 수직만(V)";
            case "도달": return "벽이 땅(원지반)에 안 닿음 — 선 바깥 땅이 계속 높아짐 → 선 옮기기 · 또는 단높이 늘리기";
            case "바깥 벽":
            {
                string? h = Num(why, @"바깥 벽 ([0-9.]+) m"), lim = Num(why, @"단높이 ([0-9.]+) m");
                return $"맨 바깥 벽이 단높이보다 높음{(h != null && lim != null ? $"({h} m > {lim} m)" : "")} → 단높이(H) 늘리기 · 또는 선 옮기기";
            }
            case "바닥": return "벽이 부지 바닥(계획선)에 닿음 — 벽 들어갈 폭 없음 → 선을 바닥에서 더 떼기";
            case "방향": return "선 위 땅 높이 = 부지 바닥 높이 — 옹벽 높이 0 → 땅과 바닥 높이가 다른 사면 위에 다시 그리기";
            case "원지반":
                if (why.Contains("높이가 같")) return "선 위 땅 높이 = 부지 높이 — 옹벽 높이 0 → 사면 위에 다시 그리기";
                if (why.Contains("선 위 원지반을 못 읽")) return "선 위 땅(원지반) 높이 없음 — 측량 범위 밖 → 측량 범위 안에 다시 그리기";
                return Bug;
            case "정지면":
                if (why.Contains("높이를 못 읽")) return "선 위 계획 지표면 높이 없음 → 선 옮기기 · 또는 [계획부지 생성] 다시";
                return Bug;
            case "정지면 밖":
                return why.Contains("넓은 소단") ? "넓은 소단이 계획 지표면 끝을 넘음 → 소단 폭 줄이기 · 또는 선 옮기기" : "옹벽 자리 일부가 계획 지표면 밖 → 선을 안쪽으로 옮기기";
            case "옛 사면": return why.Contains("못 쟀") ? Bug : "옹벽 자리가 선 안쪽(남는 사면 · 바닥)으로 넘어감 → 선 끝 옮기기";
            case "끝 높이": return EndStep(why, road);
            case "끝": return "선 끝 높이 없음 → 선 끝을 초록 경계선 위로 다시";
            case "마구리": return "선 끝 마감면이 선을 못 따름 — 오목한(U자) 선의 안쪽 끝 → 선 끝 옮기기";
            case "바닥 높이 끊김": return "굽은 안쪽 · 오목한 바닥 곁에서 바닥 높이가 끊김 → 선 옮기기";
            case "턱": return why.Contains("첫 소단") ? "첫 소단이 땅보다 너무 높거나 낮음 → 단높이(H) 바꾸기 · 또는 수직만(V)" : Bug;
            case "계단": return "천단 계단(블록)을 이 선에 못 맞춤 → 블록 길이 바꾸기 · 또는 천단 경사로";
            case "단 표": return "소단 기준 높이 없음 → [계획부지 생성] 다시";
            case "옛 소단": case "소단 높이": case "면 높이": return "옛 소단 높이와 안 맞는 자리 → 선 옮기기";
            case "상자": return "다단 자리가 너무 깊음 → 단높이 늘리기 · 또는 선을 사면 위쪽으로";
            case "재료": return why.Contains("0보다 커야") ? "단높이 · 기울기 값이 0 이하 → 정지옵션 값 확인" : Bug;
            case "다단 수직":
            {
                // 안쪽 수직 짓기의 까닭을 그대로 풀어 쓴다 — «… 못 지었습니다 — {코드}: {까닭}»
                var m = Regex.Match(why, @"— ([^:—]+): (.*)$");
                return m.Success ? Build(m.Groups[1].Value.Trim(), m.Groups[2].Value, road) : "바깥 벽 짓기 실패 → 수직만(V) · 또는 선 조금 옮기기";
            }
            default: return Bug;   // 다단 합성 · 고리 · 자가검증 · 줄 · 측선 · 가로대 · 띠 …
        }
    }

    /// <summary>★[v117.15 · JACK 1112 «끝이 초록 경계 위인데 "선 끝이 초록 경계가 아님"이라고 나옴»] «끝 높이»(1 cm 계약 — 한도는 그대로) 화면 글.
    /// <para>그 관문이 재는 자리(선 끝 A · B · 잘라낼 자리 바깥 테두리 · 띠 끝 X)는 모두 옛 경계(초록) 위 · 곁이다(BoundaryCut이 선을 경계에서 자름) —
    /// 걸린 것은 «선 끝이 경계 밖»이 아니라 «그 자리 경계가 땅과 어긋남»(정지면 − 원지반 · 부호 없는 양). 옛 글은 끝이 경계 위여도 «선 끝을 경계선 위로»라 거짓이었다.</para>
    /// <para>진입도로(<paramref name="road"/>)의 바깥 테두리 = 도로 엔진이 셈한 데이라잇(검토 실측 1112: 끝은 경계 위 · 어긋남은 그 데이라잇 자리) → «진입도로 면 오차».
    /// (데이라잇 점 사이 곧은 변이 그 사이 원지반 굴곡을 못 따르는 것으로 보임 — 기구는 아직 안 잼.)
    /// 선 끝 자리는 도로 끝 마구리 단면 위일 수도 있어 까닭은 안 붙임(재지 않은 원인을 적지 않음).</para></summary>
    static string EndStep(string why, bool road)
    {
        string? amt = null;
        var m = Regex.Match(why, @"(?:원지반이|반대쪽으로) ([0-9.]+) m");
        if (m.Success && double.TryParse(m.Groups[1].Value, NumberStyles.Float, Ci, out double v))
            amt = v < 1 ? (v * 100).ToString("0.#", Ci) + " cm" : v.ToString("0.00", Ci) + " m";
        string by = amt != null ? $"땅과 {amt} 어긋남" : "땅과 어긋남";
        if (why.Contains("바깥 테두리"))   // 잘라낼 자리 바깥 테두리(옛 경계 A → B)
            return road ? $"경계가 {by} — 진입도로 면 오차 → 선을 조금 옮기기(다음 판에서 고침)" : $"잘라낼 자리 경계가 {by} → 선을 조금 옮기기";
        if (why.Length > 0) return $"선 끝 자리 경계가 {by} → 선 끝을 조금 옮기기";   // 끝 A · B · 띠 끝 X(경계 위 · 곁)
        return $"경계가 {by} → 선을 조금 옮기기";
    }

    /// <summary>그린 선 판정 거절(<see cref="BoundaryCut.Result"/>의 Code · Why) → 화면 글.</summary>
    public static string Cut(BoundaryCut.Code code, string? why)
    {
        why ??= "";
        switch (code)
        {
            case BoundaryCut.Code.BadRing:
                return why.Contains("없습니다") ? "계획지표면 경계 없음 → [계획부지 생성] 먼저" : "계획지표면 경계 · 바닥 모양 읽기 실패 → [계획부지 생성] 다시";
            case BoundaryCut.Code.Closed: return "닫힌 폴리선은 안 됨 → 경계에서 시작해 경계에서 끝나는 열린 선";
            case BoundaryCut.Code.Few: return "점 부족(0.5 m 안 점은 하나로 침) → 점 둘 이상";
            case BoundaryCut.Code.SelfCross: return "선이 자기 자신과 엇갈림 → 꼬이지 않게 다시";
            case BoundaryCut.Code.EndInside: return why.Contains("첫 점") ? "첫 점이 경계 안 → 첫 점은 초록 경계선 위나 밖" : "끝 점이 경계 안 → 끝 점은 초록 경계선 위나 밖";
            case BoundaryCut.Code.Along:
            {
                string? m = Num(why, @"겹쳐 갑니다\(([0-9.]+) m\)");
                return $"선이 경계를 따라 겹쳐 감{(m != null ? $"({m} m)" : "")} → 경계를 가로지르게";
            }
            case BoundaryCut.Code.Touch: return "선이 경계에 살짝 닿는 자리 있음 → 확실히 지나가거나 떼기";
            case BoundaryCut.Code.Zero: return "선이 경계를 안 지남 → 경계에서 시작 · 사면 띠를 지나 · 경계에서 끝";
            case BoundaryCut.Code.One: return "경계를 한 번만 지남 → 양 끝 모두 경계 위나 밖";
            case BoundaryCut.Code.Many:
            {
                var m = Regex.Match(why, @"경계를 (\d+)번");
                return $"경계를 {(m.Success ? m.Groups[1].Value : "여러")}번 지남 → 딱 두 번만(시작 · 끝)";
            }
            case BoundaryCut.Code.Split: return "선이 부지를 둘로 가름 → 사면 띠 안에서만 그리기";
            case BoundaryCut.Code.Floor:
            {
                string? d = Num(why, @"([0-9.]+) m까지 붙습니다");
                return d != null ? $"선이 부지 바닥(계획선)에 너무 붙음({d} m) → 0.1 m 넘게 떼기" : "선이 부지 바닥(계획선)을 지남 → 사면 띠 안에서만 그리기";
            }
            case BoundaryCut.Code.Blocked:
            {
                var m = Regex.Match(why, @"^(.+?)과 겹칩니다");
                return $"다른 옹벽과 겹침{(m.Success ? $"({m.Groups[1].Value})" : "")} → 선 옮기기 · 또는 그 옹벽 먼저 [옹벽 삭제]";
            }
            default: return why.Length > 0 ? why : Bug;
        }
    }
}
