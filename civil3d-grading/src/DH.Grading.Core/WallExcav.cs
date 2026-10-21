using System.Globalization;

namespace DH.Grading.Core;

/// <summary>★★★[v103.3 · JACK 1021 «경계를 기준으로 뒷채움거리와 사면경사로 터파기 지표면을(숨김) 별도로 생성해서 종단과 횡단에서 보여주고 토공량에 반영»]
/// <b>옹벽 뒷채움 터파기</b> — 설정 · 앞면 선 · 수량 층(계획_v103_3.md).
/// <para>터파기면 자체는 종단 · 횡단을 그릴 때 짓는다(옹벽면 `정지순수옹벽_DH`처럼). 여기는 Civil 없이 재는 부분만 둔다 — 하네스가 잰다.</para></summary>
public static partial class WallExcav
{
    /// <summary>도킹바 칸 다섯 — <b>첫 값은 가장 흔한 기준</b>(JACK 1021 «모든 도킹바의 칸은 가장 흔한 기준으로 디폴트값»).
    /// <list type="bullet">
    /// <item>켬 = 꺼짐 — JACK 결정(이미 만든 도면이 말없이 안 바뀌게).</item>
    /// <item>뒤 2.0 m — 흔한 높이 3 m 역T(3D 치수 B = 0.6H)의 뒷굽 끝 1.35 m + 여유 0.6 m.</item>
    /// <item>앞 0.6 m — 참고 산출서(정수장) 기초 끝 여유폭(그 기준표 깊이 2~5 m 0.6 m).</item>
    /// <item>기초 1.0 m — KDS 11 80 05 4.4.1(4) «지표면 아래 최소 1.0 m».</item>
    /// <item>경사 1:1.2 — 산업안전보건기준 규칙 별표 11 «그 밖의 흙» · 참고 보고서(정수장) «표준기울기».</item>
    /// <item>★[v103.3.1 · JACK 1021 «터파기선의 소단과 소단나누는 높이 기준이 없어» → «켬/끔 + 두 칸»] 소단 켬 · 높이 3 m · 폭 1 m —
    /// 종전 고정 규칙(«5 m 넘으면 3 m마다 1 m» · 계획 인터뷰 · LH 2023)의 높이 · 폭. 켜면 깊이와 상관없이 바닥에서 높이마다 둔다(3 m보다 얕으면 저절로 없다).</item>
    /// </list></summary>
    public sealed record Settings(bool On, double Back, double Front, double Foot, double Slope, bool BenchOn = true, double BenchH = 3.0, double BenchW = 1.0)
    {
        public static readonly Settings Default = new(false, 2.0, 0.6, 1.0, 1.2, true, 3.0, 1.0);

        /// <summary>도면 기록의 판 — 칸을 더하면 올린다(읽는 쪽은 모르는 판을 첫 값으로 보고 ⚠). 2 = 소단 세 칸(v103.3.1) · 1은 소단 첫 값으로 읽는다.</summary>
        public const int Version = 2;

        public const double BackMax = 20, FrontMax = 10, FootMax = 5, SlopeMax = 5;
        public const double BenchHMin = 0.5, BenchHMax = 20, BenchWMax = 10;

        /// <summary>값이 쓸 수 있는가 — 칸 검사와 기록 읽기가 같은 규칙을 쓴다.</summary>
        public bool Valid(out string why)
        {
            why = "";
            if (!(Back >= 0 && Back <= BackMax)) why = $"뒤 거리 {Back} (0~{BackMax} m)";
            else if (!(Front >= 0 && Front <= FrontMax)) why = $"앞 거리 {Front} (0~{FrontMax} m)";
            else if (!(Foot >= 0 && Foot <= FootMax)) why = $"기초 깊이 {Foot} (0~{FootMax} m)";
            else if (!(Slope > 0 && Slope <= SlopeMax)) why = $"경사 1:{Slope} (0 초과 ~ 1:{SlopeMax})";
            else if (!(Back + Front >= 0.01)) why = $"뒤 + 앞 = {Back + Front}m — 터파기 바닥 폭이 1cm는 돼야 한다";
            else if (!(BenchH >= BenchHMin && BenchH <= BenchHMax)) why = $"소단 높이 {BenchH} ({BenchHMin}~{BenchHMax} m)";
            else if (!(BenchW >= 0 && BenchW <= BenchWMax)) why = $"소단 폭 {BenchW} (0~{BenchWMax} m)";
            return why.Length == 0;
        }

        /// <summary>면 도장 머리에 들어가는 설정 글 — 켬/끔까지(검토 1판 중간 5: 끈 뒤 횡단만 누르면 옛 면을 쓰던 틈) · 소단 셋(v103.3.1).</summary>
        public string Head => string.Format(CultureInfo.InvariantCulture, "{0}|{1:R}|{2:R}|{3:R}|{4:R}|{5}|{6:R}|{7:R}", On ? 1 : 0, Back, Front, Foot, Slope, BenchOn ? 1 : 0, BenchH, BenchW);

        public string Text => On
            ? string.Format(CultureInfo.InvariantCulture, "켬 · 뒤 {0}m · 앞 {1}m · 기초 {2}m · 1:{3} · {4}", Back, Front, Foot, Slope, BenchText)
            : "꺼짐";

        public string BenchText => BenchOn ? string.Format(CultureInfo.InvariantCulture, "소단 {0}m마다 {1}m", BenchH, BenchW) : "소단 없음";
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

    /// <summary>도면 기록 값 — [I 판 · I 켬 · D 뒤 · D 앞 · D 기초 · D 경사 · I 소단 켬 · D 소단 높이 · D 소단 폭]. 첫 값과 같으면 null(기록을 지운다 — 기본 도면은 흔적이 없다).</summary>
    public static List<(char K, object V)>? Encode(Settings s)
    {
        if (s == Settings.Default) return null;
        return new List<(char, object)> { ('I', Settings.Version), ('I', s.On ? 1 : 0), ('D', s.Back), ('D', s.Front), ('D', s.Foot), ('D', s.Slope),
                                           ('I', s.BenchOn ? 1 : 0), ('D', s.BenchH), ('D', s.BenchW) };
    }

    /// <summary>기록을 읽는다 — 없으면 첫 값(참) · 못 읽거나 값이 틀리면 첫 값 + 까닭(거짓 — 부르는 쪽이 ⚠).</summary>
    public static bool TryDecode(IReadOnlyList<(char K, object V)>? vals, out Settings s, out string why)
    {
        s = Settings.Default; why = "";
        if (vals == null || vals.Count == 0) return true;
        try
        {
            if (vals.Count < 6 || vals[0].K != 'I' || vals[1].K != 'I' || vals[2].K != 'D' || vals[3].K != 'D' || vals[4].K != 'D' || vals[5].K != 'D')
            { why = $"옹벽 터파기 기록 꼴이 다르다(칸 {vals.Count})"; return false; }
            int ver = (int)vals[0].V;
            if (ver != 1 && ver != Settings.Version) { why = $"옹벽 터파기 기록 판 {ver}(이 판은 1 · {Settings.Version}만 읽는다)"; return false; }
            var d = Settings.Default;
            bool bOn = d.BenchOn; double bH = d.BenchH, bW = d.BenchW;    // 판 1(v103.3.0) — 소단 칸이 없던 판 · 첫 값으로
            if (ver >= 2)
            {
                if (vals.Count < 9 || vals[6].K != 'I' || vals[7].K != 'D' || vals[8].K != 'D') { why = $"옹벽 터파기 기록 판 2 꼴이 다르다(칸 {vals.Count})"; return false; }
                bOn = (int)vals[6].V != 0; bH = (double)vals[7].V; bW = (double)vals[8].V;
            }
            var r = new Settings((int)vals[1].V != 0, (double)vals[2].V, (double)vals[3].V, (double)vals[4].V, (double)vals[5].V, bOn, bH, bW);
            if (!r.Valid(out string bad)) { why = "옹벽 터파기 기록 값이 틀리다 — " + bad; return false; }
            s = r;
            return true;
        }
        catch (System.Exception ex) { why = "옹벽 터파기 기록을 못 읽었다 " + ex.GetType().Name; s = Settings.Default; return false; }
    }
}
