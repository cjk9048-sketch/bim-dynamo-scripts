using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace DH.Grading.Core;

/// <summary>★★★[v103.1.4 · JACK 1008 «재생성이 좀 오래 걸리는데 시간을 줄일 방법 없어?» → «옹벽을 값 그대로 되돌릴 때 재생성 생략»]
/// <b>«다시 지어도 똑같은 정지면이 나오나»</b>를 가리는 견주기 — 제원 두 벌 · 구간 목록 두 벌.
/// <para>이 답이 «같다»면 정지면 생성(실측 16초)을 통째로 건너뛴다. 틀리게 «같다»고 하면 <b>낡은 정지면이 말없이 남는다</b> —
/// 그래서 짐작하지 않고 칸마다 견주고, 못 읽은 칸 · 모르는 꼴은 «다르다»로 본다(틀려도 다시 만드는 쪽으로).
/// AutoCAD에 안 기대는 순수 함수라 하네스(S154)가 같은 함수를 잰다.</para>
/// <para>★[검토 1012 낮음] <b>실수는 비트까지 같아야 같다.</b> 견주는 두 벌은 같은 값을 <b>옮겨 적은 것</b>이다(제원 = 같은 함수가 같은 입력으로 다시 낸 값 ·
/// 구간 규칙 · 자 = 번들에 적었다 읽은 값) — 셈이 끼지 않으니 어긋날 까닭이 없고, 어긋났다면 그것이 곧 «다르다»다. 허용 폭을 두면 그만큼은 모르고 넘긴다.
/// 하나만 예외다 — 구간의 <b>자리(T0 · T1)</b>는 계획선 자(자 없음) 구간이 둘 이상일 때 구간 정리(<see cref="SlopeZone.Flatten"/>)가 목록을 다시 짜며
/// 랩 구간 끝을 <c>(T + 둘레) % 둘레</c>로 다시 낸다 — 더하기에서 실수의 마지막 자리가 흔들린다(S154 실측: 둘레 200m · 랩 구간 200개 중 87개가 움직였고 가장 큰 폭 1.4e-14m.
/// 둘레가 길수록 커지지만 4km여도 5e-13m). 한 번 정리된 목록은 다시 정리해도 비트까지 그대로다(같은 실측 200/200) — 흔들리는 것은 처음 한 번뿐이다.
/// 그래서 자리만 1e-9로 견준다(<see cref="SlopeZone.Compact"/>가 같은 자의 자리를 견줄 때 쓰는 값과 같다). 단 링을 자로 쓰는 구간(변환 창이 만드는 것)은 구간 정리가 손대지 않아
/// 자리도 비트까지 그대로다(S154 «현장 흐름» 100/100).</para></summary>
public static class GradeSame
{
    /// <summary>구간 자리(T0 · T1)를 견주는 폭(m) — 구간 정리의 나머지셈 잡음만 넘긴다(위 설명).</summary>
    public const double ZoneTTol = 1e-9;

    static bool Bits(double a, double b) => BitConverter.DoubleToInt64Bits(a) == BitConverter.DoubleToInt64Bits(b);

    /// <summary>두 값이 같은가 — 실수는 <b>비트까지</b> · 문자열/정수/참거짓/열거는 그대로 · 묶음(튜플)과 목록은 길이와 차례대로 원소 · 그 밖은 Equals. 형이 다르면 다르다.</summary>
    public static bool ValueSame(object? x, object? y)
    {
        if (x == null || y == null) return x == null && y == null;
        if (x.GetType() != y.GetType()) return false;
        if (x is double dx && y is double dy) return Bits(dx, dy);
        if (x is float fx && y is float fy) return BitConverter.SingleToInt32Bits(fx) == BitConverter.SingleToInt32Bits(fy);
        if (x is string || x.GetType().IsPrimitive || x.GetType().IsEnum) return x.Equals(y);
        if (x is ITuple tx && y is ITuple ty)
        {
            if (tx.Length != ty.Length) return false;
            for (int i = 0; i < tx.Length; i++) if (!ValueSame(tx[i], ty[i])) return false;
            return true;
        }
        if (x is IEnumerable ex && y is IEnumerable ey)
        {
            var la = ex.Cast<object?>().ToList(); var lb = ey.Cast<object?>().ToList();
            if (la.Count != lb.Count) return false;
            for (int i = 0; i < la.Count; i++) if (!ValueSame(la[i], lb[i])) return false;
            return true;
        }
        return x.Equals(y);
    }

    static string Fmt(object? v) => v switch
    {
        null => "없음",
        double d => d.ToString("R", CultureInfo.InvariantCulture),
        string s => s,
        IEnumerable e => "[" + string.Join(" ", e.Cast<object?>().Select(o => o?.ToString() ?? "")) + "]",
        _ => v.ToString() ?? "",
    };

    /// <summary>제원 두 벌에서 처음 다른 칸(없으면 빈 문자열) — 공개 속성 · 필드를 <b>전부</b> 견준다(칸이 늘어도 저절로 따라온다). 못 읽은 칸은 «다르다».</summary>
    public static string ParamsDiff(GradingParams a, GradingParams b)
    {
        if (a == null || b == null) return "제원이 없다";
        const BindingFlags BF = BindingFlags.Public | BindingFlags.Instance;
        foreach (var pi in typeof(GradingParams).GetProperties(BF))
        {
            if (!pi.CanRead || pi.GetIndexParameters().Length > 0) continue;
            object? va, vb;
            try { va = pi.GetValue(a); vb = pi.GetValue(b); } catch { return pi.Name + ": 못 읽음"; }
            if (!ValueSame(va, vb)) return $"{pi.Name}: {Fmt(va)} → {Fmt(vb)}";
        }
        foreach (var fi in typeof(GradingParams).GetFields(BF))
        {
            object? va = fi.GetValue(a), vb = fi.GetValue(b);
            if (!ValueSame(va, vb)) return $"{fi.Name}: {Fmt(va)} → {Fmt(vb)}";
        }
        return "";
    }

    /// <summary>구간 목록 두 벌에서 처음 다른 것(없으면 빈 문자열) — 구간 수 · 차례 · 자리(<see cref="ZoneTTol"/>) · 규칙(시작단 · 구배 · 소단 — 비트까지) ·
    /// 자(있고 없음 · 점 수 · 점마다 비트까지) · <b>수직 구간의 부분 지정 표시</b>. 규칙 없는 구간은 안 센다(아무 일도 안 한다).
    /// <para>★[검토 1012 낮음] 부분 지정 표시(<see cref="SlopeZone.Partial"/>)는 번들에도 적힌다(판 12부터). 뜻이 있는 것은 <b>수직 구간</b>뿐이다 —
    /// 정지면 만들기가 «부분 지정 + 수직»인 구간만 정지면에서 떼어 낸다(전체구간 옹벽은 정지면에 그대로 반영). 그래서 수직 규칙이 든 구간은 이 표시도 견준다.
    /// 사면 구간의 표시는 안 본다 — 정지면을 안 바꾸고, 변환 창이 번들의 옛 구간을 옮겨 적을 때 이 표시를 떨구므로 견주면 구간지정 사면 변환을 한 번이라도 한 도면은 늘 «다르다»가 된다.</para></summary>
    /// <param name="wallGate">수직으로 볼 구배 문턱 — 정지면 만들기가 쓰는 값(<c>GradingSettings.WallGateSlope</c>)을 넘긴다.</param>
    public static string ZonesDiff(IReadOnlyList<SlopeZone>? a, IReadOnlyList<SlopeZone>? b, double wallGate = 0.05)
    {
        var la = (a ?? Array.Empty<SlopeZone>()).Where(z => z != null && z.Rules.Count > 0).ToList();
        var lb = (b ?? Array.Empty<SlopeZone>()).Where(z => z != null && z.Rules.Count > 0).ToList();
        if (la.Count != lb.Count) return $"구간 수 {la.Count} → {lb.Count}";
        var ci = CultureInfo.InvariantCulture;
        for (int i = 0; i < la.Count; i++)
        {
            var x = la[i]; var y = lb[i];
            // 음수로 뒤집어 묻는다 — NaN이 끼면 «다르다»
            if (!(Math.Abs(x.T0 - y.T0) <= ZoneTTol) || !(Math.Abs(x.T1 - y.T1) <= ZoneTTol))
                return string.Format(ci, "구간 {0} 자리 [{1:R}..{2:R}] → [{3:R}..{4:R}]", i + 1, x.T0, x.T1, y.T0, y.T1);
            if (x.Rules.Count != y.Rules.Count) return $"구간 {i + 1} 규칙 수 {x.Rules.Count} → {y.Rules.Count}";
            bool vertical = false;
            for (int r = 0; r < x.Rules.Count; r++)
            {
                if (x.Rules[r].FromBench != y.Rules[r].FromBench || !Bits(x.Rules[r].Slope, y.Rules[r].Slope) || !Bits(x.Rules[r].BenchW, y.Rules[r].BenchW))
                    return string.Format(ci, "구간 {0} 규칙 {1}: {2}단~1:{3:R}·소단 {4:R} → {5}단~1:{6:R}·소단 {7:R}",
                        i + 1, r + 1, x.Rules[r].FromBench + 1, x.Rules[r].Slope, x.Rules[r].BenchW, y.Rules[r].FromBench + 1, y.Rules[r].Slope, y.Rules[r].BenchW);
                if (!(x.Rules[r].Slope > wallGate + 1e-9)) vertical = true;      // 정지면 만들기의 수직 판정과 같은 식(NaN도 수직 쪽 = 견주는 쪽)
            }
            if (vertical && x.Partial != y.Partial)
                return $"구간 {i + 1}(수직)의 부분 지정 표시가 다르다({(x.Partial ? "부분" : "전체")} → {(y.Partial ? "부분" : "전체")})";
            var rx = x.Ref; var ry = y.Ref;
            if ((rx == null) != (ry == null)) return $"구간 {i + 1} 자 종류가 다르다(계획선 ↔ 단 링)";
            if (rx != null && ry != null && !ReferenceEquals(rx, ry))
            {
                if (rx.Count != ry.Count) return $"구간 {i + 1} 자 점 수 {rx.Count} → {ry.Count}";
                for (int q = 0; q < rx.Count; q++)
                    if (!Bits(rx[q].X, ry[q].X) || !Bits(rx[q].Y, ry[q].Y) || !Bits(rx[q].Z, ry[q].Z))
                        return $"구간 {i + 1} 자 {q + 1}번 점이 다르다";
            }
        }
        return "";
    }
}
