namespace DH.Grading.Core;

/// <summary>★★[v111.5 · 계획_v111_4.md 3 · 검토 M1] 진입도로 횡단의 계획면 — <b>앞 면에 표고가 있으면 그것</b>, 없으면 다음 면(진입도로 → 도로 바탕면).
/// <para><see cref="LowerOfSurfaces"/>와 같은 꼴. 진입도로 면은 도로 + 사면 자리뿐이라 그 밖은 바탕면(정지면_DH 또는 원지반)이 계획면이다 —
/// 부지 [횡단도]가 «정지면»을 계획선으로 쓰듯 이 합친 면이 수량 · 계획고(FGL) 기준이 된다.</para></summary>
public sealed class FirstOfSurfaces : IGroundSurface
{
    private readonly IGroundSurface?[] _s;

    /// <param name="surfaces">앞에서부터 본다(null은 건너뜀).</param>
    public FirstOfSurfaces(params IGroundSurface?[] surfaces) { _s = surfaces ?? Array.Empty<IGroundSurface?>(); }

    public bool TryGetElevation(double x, double y, out double z)
    {
        foreach (var s in _s)
            if (s != null && s.TryGetElevation(x, y, out z)) return true;
        z = 0;
        return false;
    }

    /// <summary>이 자리 표고를 준 면의 차례(0 = 첫 면) · 어느 면에도 없으면 −1. 진단 · 알림용.</summary>
    public int SourceAt(double x, double y)
    {
        for (int i = 0; i < _s.Length; i++)
            if (_s[i] != null && _s[i]!.TryGetElevation(x, y, out _)) return i;
        return -1;
    }
}

/// <summary>★★[v111.5 · 계획_v111_4.md 3 · 4] 진입도로 횡단도 규칙 — 스타일 복사본 이름(축척 짝) · 검토선 이름 꼬리 측점 · 측점 구간 글 · 그린 횡단도 지문과 «바뀜» 판정.
/// 화면 없는 시험(S183)이 본다 — 만든 값만(현장 값 없음 · 공개 저장소).</summary>
public static class RoadXsecRules
{
    /// <summary>진입도로 복사본 스타일 이름 표지 — 부지 고르기(이름에 «DH_횡단 뷰 스타일»이 든 첫 것)가 복사본을 집지 않게 이 글이 든 이름은 뺀다.
    /// 종단 구배 밴드 복사본 «…(진입도로)»도 같은 표지에 걸린다.</summary>
    public const string CloneTag = "(진입도로";

    /// <summary>축척을 고르기 전에 뷰 크기를 잴 때만 잠깐 쓰는 복사본 꼬리(제목을 끈 꼴 — 부지와 같은 잣대로 재게).</summary>
    public const string MeasureSuffix = "(진입도로 측정)";

    /// <summary>복사본 꼬리 — 축척 짝(도로 횡단 축척 @ 도면 주석 축척). 축척에 묶인 값(글자 · 눈금 · 밴드 칸)은 이 두 값으로만 정해지므로
    /// 이름이 같으면 내용도 같다. 도면 주석 축척을 못 읽으면(0) «@» 없이.</summary>
    public static string CloneSuffix(double scale, double anno)
    {
        string s = double.IsFinite(scale) && scale > 0 ? Math.Round(scale).ToString("0") : "0";
        return double.IsFinite(anno) && anno > 0 ? $"(진입도로 1:{s}@{Math.Round(anno):0})" : $"(진입도로 1:{s})";
    }

    /// <summary>★[v111.5 · 화면 없는 Civil 실측 1030] AutoCAD 사전 이름(표 스타일)에 못 쓰는 글자(<c>&lt; &gt; / \ " : ; ? * | , = `</c>)를 «-»로 —
    /// «DH-수량표(진입도로 1:200@150)»는 표 스타일을 못 만들었다(Civil 스타일 이름은 «:»를 받음).</summary>
    public static string DictKey(string? name)
    {
        var sb = new System.Text.StringBuilder(name ?? "");
        for (int i = 0; i < sb.Length; i++) if ("<>/\\\":;?*|,=`".IndexOf(sb[i]) >= 0) sb[i] = '-';
        return sb.ToString();
    }

    /// <summary>진입도로 복사본 스타일인가(부지 고르기에서 뺀다).</summary>
    public static bool IsRoadClone(string? name) => name != null && name.Contains(CloneTag, StringComparison.Ordinal);

    /// <summary>복사본 이름의 원본 이름(꼬리 «(진입도로 …)» 앞) — 복사본이 아니면 그대로. 다른 축척 복사본에서 새 복사본을 지을 때 꼬리가 겹치지 않게.</summary>
    public static string BaseName(string? name)
    {
        string n = name ?? "";
        int k = n.IndexOf(CloneTag, StringComparison.Ordinal);
        return k > 0 ? n.Substring(0, k) : n;
    }

    /// <summary>검토선 이름 «{그룹}_{측점}»에서 측점 꼬리 — 진입도로 종단도 검토선(«DH도로_1-검토_1_No.3+12.50»)의 이름이 곧 종단도 측점 이름이다.
    /// 그룹 이름으로 시작하지 않으면 마지막 «_» 뒤 · 그것도 없으면 <paramref name="fallback"/>.</summary>
    public static string LabelOf(string? sampleLineName, string? groupName, string fallback)
    {
        string n = sampleLineName ?? "";
        string g = groupName ?? "";
        if (g.Length > 0 && n.Length > g.Length + 1 && n.StartsWith(g + "_", StringComparison.Ordinal))
            return n.Substring(g.Length + 1);
        // 그룹 이름을 모를 때 — «…-검토_1_No.0»처럼 꼬리가 «No.»로 시작하는 마지막 조각
        int k = n.LastIndexOf("_No.", StringComparison.Ordinal);
        if (k >= 0 && k + 1 < n.Length) return n.Substring(k + 1);
        return fallback;
    }

    /// <summary>차례대로 늘어선 측점 이름 중 <paramref name="hit"/>에 든 것을 이어진 구간으로 — «No.0 ~ No.2 · No.5». 하나뿐이면 그 이름만 · 없으면 빈 글.</summary>
    public static string Ranges(IReadOnlyList<string> order, ICollection<string> hit)
    {
        if (order == null || hit == null || hit.Count == 0) return "";
        var parts = new List<string>();
        int i = 0;
        while (i < order.Count)
        {
            if (!hit.Contains(order[i])) { i++; continue; }
            int j = i;
            while (j + 1 < order.Count && hit.Contains(order[j + 1])) j++;
            parts.Add(i == j ? order[i] : $"{order[i]} ~ {order[j]}");
            i = j + 1;
        }
        return string.Join(" · ", parts);
    }

    /// <summary>측점 목록 지문 — 측점(mm 반올림) · 이름 차례대로 FNV-1a 64비트. 진입도로 종단도 검토선이 바뀌면(측점 더함 · 뺌 · 노선 길이) 달라진다.</summary>
    public static string StationsKey(IEnumerable<(double St, string Label)>? stations)
    {
        ulong h = 14695981039346656037UL;
        void MixB(byte b) { h ^= b; h *= 1099511628211UL; }
        void Mix(long v) { for (int i = 0; i < 8; i++) MixB((byte)(v >> (8 * i))); }
        long n = 0;
        if (stations != null)
            foreach (var (st, lb) in stations)
            {
                Mix(double.IsFinite(st) ? (long)Math.Round(st * 1000.0) : long.MinValue);
                foreach (char c in lb ?? "") { MixB((byte)c); MixB((byte)(c >> 8)); }
                MixB(0);
                n++;
            }
        Mix(n);
        return h.ToString("x16");
    }

    /// <summary>그린 횡단도 지문 — «측점 지문|지표면 핸들|지표면 입력 지문».</summary>
    public static string Stamp(string stationsKey, string surfHandle, string surfSig)
        => $"{stationsKey ?? ""}|{surfHandle ?? ""}|{surfSig ?? ""}";

    /// <summary>★[계획 3 · 도킹창] 그린 진입도로 횡단도가 지금과 다른 까닭 한 줄(같으면 빈 글) — 차례: 측점(종단) → 지표면(다시 만들어 핸들 · 지문이 바뀜) → 축척(도면설정 칸 · 도면 주석 축척).
    /// 지문이 비었으면(옛 기록) 축척만 본다.</summary>
    public static string Stale(string stampWas, string stationsNow, string surfHandleNow, string surfSigNow,
                               double pinWas, double pinNow, double annoWas, double annoNow)
    {
        var w = (stampWas ?? "").Split('|');
        if (w.Length >= 3)
        {
            if (w[0] != (stationsNow ?? "")) return "종단이 수정되었습니다";
            if (w[1] != (surfHandleNow ?? "") || w[2] != (surfSigNow ?? "")) return "지표면이 다시 만들어졌습니다";
        }
        static double P(double v) => double.IsFinite(v) && v > 0 ? v : 0;
        if (Math.Abs(P(pinWas) - P(pinNow)) > 1e-6 || Math.Abs(P(annoWas) - P(annoNow)) > 1e-6) return "축척이 바뀌었습니다";
        return "";
    }

    /// <summary>★★[v111.7.2 · JACK 1102 «횡단도 배치 드롭박스를 바꿔도 안 바뀌어»] 위 판정 + 횡단도 배치(도면설정 «횡단도 배치» 차례 번호) — 그릴 때 배치와 지금 배치가 다르면 «횡단도 배치가 바뀌었습니다».
    /// <paramref name="layoutWas"/> &lt; 0 = 배치를 안 적은 옛 기록(v111.7.1 이전) → <see cref="LayoutDefault"/>(1×2)로 그렸다고 본다 — 그때까지 배치는 저장되지 않아 Civil을 켤 때마다 1×2였다.</summary>
    public static string Stale(string stampWas, string stationsNow, string surfHandleNow, string surfSigNow,
                               double pinWas, double pinNow, double annoWas, double annoNow, int layoutWas, int layoutNow)
    {
        string r = Stale(stampWas, stationsNow, surfHandleNow, surfSigNow, pinWas, pinNow, annoWas, annoNow);
        if (r.Length > 0) return r;
        int was = layoutWas >= 0 ? layoutWas : LayoutDefault;
        return layoutNow >= 0 && was != layoutNow ? "횡단도 배치가 바뀌었습니다" : "";
    }

    /// <summary>★[v116.8 · JACK 1110 포장 종류] 그린 횡단도 지문 4칸 — «측점|지표면 핸들|지표면 지문|포장 토큰»(<see cref="RoadPave.Key"/>).</summary>
    public static string Stamp(string stationsKey, string surfHandle, string surfSig, string paveKey)
        => $"{stationsKey ?? ""}|{surfHandle ?? ""}|{surfSig ?? ""}|{paveKey ?? ""}";

    /// <summary>★[v116.8] 옛 3칸 도장(포장 토큰 없이 그린 횡단도) 알림.</summary>
    public const string PaveOld = "포장 층 없이 그린 횡단도입니다";
    /// <summary>★[v116.8] 포장 토큰이 다름.</summary>
    public const string PaveChanged = "포장이 바뀌었습니다";

    /// <summary>★[v116.8 · JACK 1110 포장 종류] 판정 차례 = 측점 → 지표면 → <b>포장</b> → 축척 → 배치. 도장 칸이 정확히 셋(옛 판)이면 <see cref="PaveOld"/> 한 번 ·
    /// 넷이면 넷째 칸 ≠ <paramref name="paveNow"/>일 때 <see cref="PaveChanged"/>. 실패 도장(빈 글 · 한 칸)은 옛 판처럼 축척 · 배치만.</summary>
    public static string Stale(string stampWas, string stationsNow, string surfHandleNow, string surfSigNow, string paveNow,
                               double pinWas, double pinNow, double annoWas, double annoNow, int layoutWas, int layoutNow)
    {
        var w = (stampWas ?? "").Split('|');
        if (w.Length >= 3)
        {
            if (w[0] != (stationsNow ?? "")) return "종단이 수정되었습니다";
            if (w[1] != (surfHandleNow ?? "") || w[2] != (surfSigNow ?? "")) return "지표면이 다시 만들어졌습니다";
            if (w.Length == 3) return PaveOld;
            if (w[3] != (paveNow ?? "")) return PaveChanged;
        }
        return Stale("", stationsNow, surfHandleNow, surfSigNow, pinWas, pinNow, annoWas, annoNow, layoutWas, layoutNow);
    }

    /// <summary>도면설정 «횡단도 배치» 기본 차례 번호(1 = 1×2) — <c>GradingSettings.XsecLayout</c> 기본값과 같다.</summary>
    public const int LayoutDefault = 1;

    /// <summary>★★[v113.2 · JACK 1104 «횡단도 배치는 부지 별도 진입도로 별도»] 도면 기록 «DH_SHEETLAYOUT»(정수 칸 차례) → (부지, 진입도로) 배치.
    /// 판 1 = [배치] (옛 «공통» — 둘 다 그 값) · 판 2 = [부지, 2, 진입도로]. 기록이 없거나 깨졌으면 −1(부르는 쪽이 기본값).
    /// 옛 애드인은 첫 칸만 읽으므로 판 2도 부지 배치로 읽는다.</summary>
    public static (int Site, int Road) DecodeLayouts(int[] v)
    {
        int s = v != null && v.Length >= 1 && v[0] >= 0 ? v[0] : -1;
        int r = IsLayoutsV2(v) ? v[2] : s;
        return (s, r);
    }

    /// <summary>판 2 기록인가(진입도로 배치를 따로 적음).</summary>
    public static bool IsLayoutsV2(int[] v) => v != null && v.Length >= 3 && v[0] >= 0 && v[1] == 2 && v[2] >= 0;

    /// <summary>쓸 기록 — 판 2가 아니던 도면에서 둘이 같으면 판 1 그대로(옛 도면 기록이 바뀌지 않게) · 그 밖엔 판 2.</summary>
    public static int[] EncodeLayouts(int site, int road, bool wasV2)
        => !wasV2 && site == road ? new[] { site } : new[] { site, 2, road };
}
