using System.Globalization;

namespace DH.Grading.Core;

/// <summary>★★★[v103.3 · JACK 1021 «경계를 기준으로 뒷채움거리와 사면경사로 터파기 지표면을(숨김) 별도로 생성해서 종단과 횡단에서 보여주고 토공량에 반영»]
/// <b>옹벽 뒷채움 터파기</b> — 설정 · 앞면 선 · 수량 층(계획_v103_3.md).
/// <para>터파기면 자체는 종단 · 횡단을 그릴 때 짓는다(옹벽면 `정지순수옹벽_DH`처럼). 여기는 Civil 없이 재는 부분만 둔다 — 하네스가 잰다.</para></summary>
public static partial class WallExcav
{
    /// <summary>도킹바 칸 — <b>첫 값은 가장 흔한 기준</b>(JACK 1021 «모든 도킹바의 칸은 가장 흔한 기준으로 디폴트값»).
    /// <list type="bullet">
    /// <item>켬 = 꺼짐 — JACK 결정(이미 만든 도면이 말없이 안 바뀌게).</item>
    /// <item>★★[v104 · 계획_v104.md] 터파기 바닥 = <b>옹벽 형식별 일반단면</b>(<see cref="WallSection"/> — 높이 표) + 버림 + <b>여유폭</b> — 종전 «뒤 2.0 · 앞 0.6 · 기초 1.0»(3 m 역T 하나를 가정한 근사)을 대신한다.</item>
    /// <item>여유폭 0.5 m — 국도건설공사 설계실무요령(2021) 4.03 «옹벽 0.5 m»(LH 0.3은 흙막이 없을 때).</item>
    /// <item>버림 두께 0.1 · 돌출 0.1 — 국도 실무요령 버림 산식 · 도로옹벽 표준도 2008 재료표.</item>
    /// <item>단면 형식 절토 = 역T형 · 성토 = 보강토 — 단지 정지에서 흔한 짝(제안 · JACK께 알림).</item>
    /// <item>경사 1:1.2 — 산업안전보건기준 규칙 별표 11 «그 밖의 흙».</item>
    /// <item>소단 켬 · 높이 3 m · 폭 1 m(v103.3.1 · JACK «켬/끔 + 두 칸»).</item>
    /// <item>★[v104 B · JACK 1021 «경사는 매개변수로 받되 기본값은 1.5»] 배면사면 켬 · 1:1.5 — 터파기를 켜면 같이(따로 끌 수 있게).</item>
    /// </list></summary>
    public sealed record Settings(bool On, double Margin, double Slope, bool BenchOn, double BenchH, double BenchW, double TLean, double ELean,
                                  WallSection.Kind CutKind, WallSection.Kind FillKind, bool BackOn = true, double BackSlope = 1.5)
    {
        public static readonly Settings Default = new(false, 0.5, 1.2, true, 3.0, 1.0, 0.10, 0.10, WallSection.Kind.역T형, WallSection.Kind.보강토, true, 1.5);

        /// <summary>도면 기록의 판 — 4 = 배면사면(v104 B) · 3 = 단면 형식(v104) · 1 · 2(뒤 · 앞 · 기초 시절)는 켬 · 경사 · 소단만 옮긴다(앞 · 뒤 · 기초는 단면으로 바뀜).</summary>
        public const int Version = 4;

        public const double MarginMax = 5, SlopeMax = 5, LeanMax = 1;
        public const double BenchHMin = 0.5, BenchHMax = 20, BenchWMax = 10;

        /// <summary>값이 쓸 수 있는가 — 칸 검사와 기록 읽기가 같은 규칙을 쓴다.</summary>
        public bool Valid(out string why)
        {
            why = "";
            if (!(Margin >= 0 && Margin <= MarginMax)) why = $"여유폭 {Margin} (0~{MarginMax} m)";
            else if (!(Slope > 0 && Slope <= SlopeMax)) why = $"경사 1:{Slope} (0 초과 ~ 1:{SlopeMax})";
            else if (!(BenchH >= BenchHMin && BenchH <= BenchHMax)) why = $"소단 높이 {BenchH} ({BenchHMin}~{BenchHMax} m)";
            else if (!(BenchW >= 0 && BenchW <= BenchWMax)) why = $"소단 폭 {BenchW} (0~{BenchWMax} m)";
            else if (!(TLean >= 0 && TLean <= LeanMax)) why = $"버림 두께 {TLean} (0~{LeanMax} m)";
            else if (!(ELean >= 0 && ELean <= LeanMax)) why = $"버림 돌출 {ELean} (0~{LeanMax} m)";
            else if (!Enum.IsDefined(typeof(WallSection.Kind), CutKind) || !Enum.IsDefined(typeof(WallSection.Kind), FillKind)) why = "모르는 옹벽 형식";
            else if (!(BackSlope > 0 && BackSlope <= SlopeMax)) why = $"배면사면 경사 1:{BackSlope} (0 초과 ~ 1:{SlopeMax})";
            return why.Length == 0;
        }

        /// <summary>면 도장 머리에 들어가는 설정 글 — 켬/끔까지(검토 1판 중간 5) · 소단 셋 · 여유 · 버림 · 형식 둘(★v104 — 표 지문은 부르는 쪽이 붙인다).</summary>
        public string Head => string.Format(CultureInfo.InvariantCulture, "{0}|{1:R}|{2:R}|{3}|{4:R}|{5:R}|{6:R}|{7:R}|{8}|{9}",
            On ? 1 : 0, Margin, Slope, BenchOn ? 1 : 0, BenchH, BenchW, TLean, ELean, (int)CutKind, (int)FillKind);

        public string Text => On
            ? string.Format(CultureInfo.InvariantCulture, "켬 · 절토 {0} · 성토 {1} · 여유 {2}m · 1:{3} · {4} · {5}", WallSection.Of(CutKind).Name, WallSection.Of(FillKind).Name, Margin, Slope, BenchText, BackText)
            : "꺼짐";

        public string BackText => BackOn ? string.Format(CultureInfo.InvariantCulture, "배면 1:{0}", BackSlope) : "배면 없음";

        /// <summary>★[v104 B] 배면사면 면 도장에 붙는 설정 글 — 터파기면 머리(<see cref="Head"/>)와 따로(배면 경사만 바꾸면 터파기면은 되쓴다).</summary>
        public string BackHead => string.Format(CultureInfo.InvariantCulture, "{0}|{1:R}", BackOn ? 1 : 0, BackSlope);

        public string BenchText => BenchOn ? string.Format(CultureInfo.InvariantCulture, "소단 {0}m마다 {1}m", BenchH, BenchW) : "소단 없음";

        public WallSection.Common Common(double gradeFace) => new(TLean, ELean, 0.02, gradeFace);
    }

    /// <summary>대표 경사 — 드롭박스 목록(JACK 1021 «대표 경사를 드롭박스에서 고를 수 있게»).
    /// 1:1.8 · 1:1.2 · 1:1.0 · 1:0.5는 산업안전보건기준 규칙 별표 11(2023.11.14) · 1:0.3은 국도 요령 발파암(JACK이 법정 한도보다 가파름을 알고 넣음).</summary>
    public static readonly double[] Slopes = { 1.8, 1.2, 1.0, 0.5, 0.3 };
    public static readonly string[] SlopeNames = { "1:1.8 모래", "1:1.2 그 밖의 흙", "1:1.0 연암·풍화암", "1:0.5 경암", "1:0.3 발파암" };

    /// <summary>목록에서 그 경사의 자리 — 없으면 −1(다른 판이 적은 값 · 콤보는 «1:n(목록 밖)»으로 보인다).</summary>
    public static int SlopeIndex(double n)
    {
        for (int i = 0; i < Slopes.Length; i++) if (System.Math.Abs(Slopes[i] - n) < 1e-9) return i;
        return -1;
    }

    /// <summary>도면 기록 값(판 4) — [I 판 · I 켬 · D 여유 · D 경사 · I 소단 켬 · D 소단 높이 · D 소단 폭 · D 버림 두께 · D 버림 돌출 · I 절토 형식 · I 성토 형식 · I 배면 켬 · D 배면 경사].
    /// 첫 값과 같으면 null(기록을 지운다 — 기본 도면은 흔적이 없다).</summary>
    public static List<(char K, object V)>? Encode(Settings s)
    {
        if (s == Settings.Default) return null;
        return new List<(char, object)> { ('I', Settings.Version), ('I', s.On ? 1 : 0), ('D', s.Margin), ('D', s.Slope),
                                           ('I', s.BenchOn ? 1 : 0), ('D', s.BenchH), ('D', s.BenchW), ('D', s.TLean), ('D', s.ELean),
                                           ('I', (int)s.CutKind), ('I', (int)s.FillKind), ('I', s.BackOn ? 1 : 0), ('D', s.BackSlope) };
    }

    /// <summary>기록을 읽는다 — 없으면 첫 값(참) · 못 읽거나 값이 틀리면 첫 값 + 까닭(거짓 — 부르는 쪽이 ⚠).
    /// <para>★[v104 2판 · 검토 낮음] <b>판을 먼저 읽고</b> 판마다 꼴을 본다. 판 1 · 2(뒤 · 앞 · 기초 시절 — [I 판 · I 켬 · D 뒤 · D 앞 · D 기초 · D 경사 (· I 소단 · D · D)])는
    /// 켬 · 경사 · 소단만 옮기고 나머지는 첫 값(앞 · 뒤 · 기초는 단면 + 여유로 바뀌었다 — 도장이 바뀌어 «종단 먼저»가 뜬다).</para></summary>
    public static bool TryDecode(IReadOnlyList<(char K, object V)>? vals, out Settings s, out string why)
    {
        s = Settings.Default; why = "";
        if (vals == null || vals.Count == 0) return true;
        try
        {
            if (vals[0].K != 'I') { why = "옹벽 터파기 기록 꼴이 다르다(판 칸 없음)"; return false; }
            int ver = (int)vals[0].V;
            var d = Settings.Default;
            Settings r;
            if (ver == 1 || ver == 2)
            {
                if (vals.Count < 6 || vals[1].K != 'I' || vals[5].K != 'D') { why = $"옹벽 터파기 기록 판 {ver} 꼴이 다르다(칸 {vals.Count})"; return false; }
                bool bOn = d.BenchOn; double bH = d.BenchH, bW = d.BenchW;
                if (ver == 2)
                {
                    if (vals.Count < 9 || vals[6].K != 'I' || vals[7].K != 'D' || vals[8].K != 'D') { why = $"옹벽 터파기 기록 판 2 꼴이 다르다(칸 {vals.Count})"; return false; }
                    bOn = (int)vals[6].V != 0; bH = (double)vals[7].V; bW = (double)vals[8].V;
                }
                r = d with { On = (int)vals[1].V != 0, Slope = (double)vals[5].V, BenchOn = bOn, BenchH = bH, BenchW = bW };
            }
            else if (ver == 3 || ver == Settings.Version)
            {
                int need = ver == 3 ? 11 : 13;
                if (vals.Count < need) { why = $"옹벽 터파기 기록 판 {ver} 꼴이 다르다(칸 {vals.Count})"; return false; }
                char[] want = { 'I', 'I', 'D', 'D', 'I', 'D', 'D', 'D', 'D', 'I', 'I', 'I', 'D' };
                for (int i = 0; i < need; i++) if (vals[i].K != want[i]) { why = $"옹벽 터파기 기록 판 {ver} {i + 1}째 칸 형이 다르다"; return false; }
                // 판 3(배면 칸 없음)은 배면 첫 값
                r = new Settings((int)vals[1].V != 0, (double)vals[2].V, (double)vals[3].V, (int)vals[4].V != 0, (double)vals[5].V, (double)vals[6].V,
                                 (double)vals[7].V, (double)vals[8].V, (WallSection.Kind)(int)vals[9].V, (WallSection.Kind)(int)vals[10].V,
                                 ver == 3 ? d.BackOn : (int)vals[11].V != 0, ver == 3 ? d.BackSlope : (double)vals[12].V);
            }
            else { why = $"옹벽 터파기 기록 판 {ver}(이 판은 1 ~ {Settings.Version}만 읽는다)"; return false; }
            if (!r.Valid(out string bad)) { why = "옹벽 터파기 기록 값이 틀리다 — " + bad; return false; }
            s = r;
            return true;
        }
        catch (System.Exception ex) { why = "옹벽 터파기 기록을 못 읽었다 " + ex.GetType().Name; s = Settings.Default; return false; }
    }
}
