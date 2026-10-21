using System.Globalization;

namespace DH.Grading.Core;

/// <summary>★★★[v104 · 계획_v104.md · JACK 1021 «옹벽 형태별 일반단면도 개념을 우리가 제시(매개변수로 단면 모양 조종) → 그 값을 기준으로 여유폭 · 구배로 터파기 →
/// 옹벽도 그 단면으로 3D(차후)»] <b>옹벽 형식별 일반단면</b> — 형식 · 칸 · 높이 표 · 단면 다각형 · 터파기 범위를 한 곳에서.
/// <para>그림(도킹바) · 터파기(엔진) · 수량(구조물 공제) · 3D(차후)가 <b>같은 단면</b>을 쓴다 — 어긋날 여지를 안 만든다.</para>
/// <para><b>높이 H = 기초 저면(버림 위)부터 벽 꼭대기까지 · 근입 Df 포함</b>(2008 도로옹벽 표준도 · LH 기준과 같다). 엔진이 재는 보이는 높이 h와는 H = h + Df.</para>
/// <para>단면 좌표: 원점 = 벽 앞면 · 앞 땅 높이(toe) · x = 흙 쪽 + · z = 위 +. 기초(패드) 저면 z = −Df.</para>
/// <para>첫 표는 <b>공공 자료만</b>(공개 저장소) — [표준도] 국토해양부 «도로옹벽 표준도 2008» · [기준] KDS 11 80 05 · 10 · 25 · [지침] 건설공사 표준품셈 2009 ·
/// 표가 없는 형식은 [제안](표의 규칙을 늘린 것 · ⚠). 실제 값은 토질부 표로 덮어쓴다(도면 기록).</para></summary>
public static class WallSection
{
    /// <summary>형식 — ★[2판 · 재검토] 번호가 도면 기록에 들어가므로 <b>값을 고정</b>한다(새 형식은 뒤에 새 번호로).</summary>
    public enum Kind { 역T형 = 0, L형 = 1, 역L형 = 2, 중력식 = 3, 반중력식 = 4, 부벽식 = 5, 보강토 = 6, 패널식 = 7, 찰쌓기 = 8, 메쌓기 = 9 }

    /// <summary>형식 하나의 설명 — 이름 · 칸(첫 칸은 늘 H) · 칸 뜻 · 출처 · 공공 표가 있나 · 쓰는 높이 범위 글.</summary>
    public sealed record Info(Kind Id, string Name, string[] Cols, string[] ColNames, string Source, bool PublicTable, string Range, string Note);

    /// <summary>높이 표 — 줄마다 칸(Cols와 같은 차례) · H 오름차순.</summary>
    public sealed class Table
    {
        public Kind Kind;
        public List<double[]> Rows = new();
        public Table Clone() => new() { Kind = Kind, Rows = Rows.Select(r => (double[])r.Clone()).ToList() };
        /// <summary>표 지문(도장에 넣는다 — 표를 고치면 터파기를 다시 짓는다).</summary>
        public string Print
        {
            get
            {
                var sb = new System.Text.StringBuilder(Kind.ToString());
                foreach (var r in Rows) { sb.Append(';'); sb.Append(string.Join(",", r.Select(v => v.ToString("R", CultureInfo.InvariantCulture)))); }
                return sb.ToString();
            }
        }
    }

    // ── 형식 설명 ────────────────────────────────────────────────────────────
    static readonly string[] CantCols = { "H", "t_top", "t_bot", "B1", "B2", "t_f", "t_f2", "Df" };
    static readonly string[] CantNames = { "높이(근입 포함)", "벽 위 두께", "벽 아래 두께", "앞굽", "뒷굽", "저판 두께(벽 아래)", "저판 두께(끝)", "근입" };
    // ★[v104 2판 재검토] 부벽식 — 부벽 간격 · 두께(부벽은 단면에 «평균» 조각으로 — 넓이 × 두께/간격)
    static readonly string[] CfCols = { "H", "t_top", "t_bot", "B1", "B2", "t_f", "t_f2", "s_cf", "t_cf", "Df" };
    static readonly string[] CfNames = { "높이(근입 포함)", "벽 위 두께", "벽 아래 두께", "앞굽", "뒷굽", "저판 두께(벽 아래)", "저판 두께(끝)", "부벽 간격", "부벽 두께", "근입" };
    // ★[v104 2판 재검토] 돌쌓기 — 배수층 폭(뒤채움 뒤)
    static readonly string[] StoneCols = { "H", "n_fill", "n_cut", "t_stone", "t_back_top", "t_back_bot", "w_drain", "b_ft", "t_ft", "Df" };
    static readonly string[] StoneNames = { "높이(근입 포함)", "앞면 경사(성토) 1:n", "앞면 경사(절토) 1:n", "뒷길이", "뒤채움 두께(위)", "뒤채움 두께(아래 · 성토)", "배수층 폭", "기초 폭", "기초 두께", "근입" };

    public static readonly IReadOnlyList<Info> Kinds = new Info[]
    {
        new(Kind.역T형, "역T형", CantCols, CantNames, "도로옹벽 표준도 2008 T-1~T-11(배면 수평)", true, "3.0~8.0 m", "캔틸레버 RC · 앞면 1:0.02"),
        new(Kind.L형, "L형", CantCols, CantNames, "도로옹벽 표준도 2008 L-1~L-11(배면 수평)", true, "3.0~8.0 m", "앞굽 없음(용지 경계)"),
        new(Kind.역L형, "역L형", CantCols, CantNames, "공공 표 없음 — 역T형 표를 앞굽에 몰아준 꼴(제안)", false, "2~8 m(LH 절토부)", "뒷굽 없음 · 지반팀 값 필요"),
        new(Kind.중력식, "중력식", new[] { "H", "b_top", "B", "Df" }, new[] { "높이(근입 포함)", "윗폭", "밑폭", "근입" },
            "도로옹벽 표준도 2008 G-1(전면 1:0.02 · 배면 수평)", true, "1.5~4.0 m", "무근 콘크리트 · 저판 없음"),
        new(Kind.반중력식, "반중력식", CantCols, CantNames, "도로옹벽 표준도 2008 M-1~M-5", true, "2.0~4.0 m", "두꺼운 저판 + 얇은 벽"),
        new(Kind.부벽식, "부벽식", CfCols, CfNames, "공공 표 없음 — 역T형 규칙(B = 0.6H + 0.2 · 두께 H/10)을 늘린 것 · 부벽 간격 H/3 · 두께 0.3(제안)", false, "8 m 이상", "부벽 사이 단면 + 부벽 평균(두께/간격) · 지반팀 값 필요"),
        new(Kind.보강토, "보강토", new[] { "H", "L", "D_blk", "Sv", "batter", "pad_t", "pad_e", "w_drain", "Df" },
            new[] { "높이(근입 포함)", "보강재 길이", "블록 깊이", "보강재 수직간격", "전면 기울기 1:n", "기초패드 두께", "기초패드 여유", "배수층 폭", "근입" },
            "KDS 11 80 10 — L ≥ max(0.7H, 2.5) · Df ≥ max(0.6, H/20) · 패드 ≥ 0.15 · 여유 ≥ 0.075 · 배수층 ≥ 0.3", true, "제한 없음(산지 5 m 이상 다단)", "블록 전면 · 블록 깊이는 제품별(제안 0.4)"),
        new(Kind.패널식, "패널식", new[] { "H", "panel_t", "b_ft", "t_ft", "gap", "Df" },
            new[] { "높이(근입 포함)", "패널 두께", "기초 폭", "기초 두께", "패널 뒤 되메움", "근입" },
            "전용 KDS 없음 — 업체 · 해외(Caltrans) 범위의 대표값(제안)", false, "절토부", "프리캐스트 패널 + 네일/앵커 · 기초부만 터파기 · 지반팀 값 필요"),
        new(Kind.찰쌓기, "찰쌓기", StoneCols, StoneNames,
            "KDS 11 80 25 · 건설공사 표준품셈 2009 7-1 돌쌓기 표(대표값 = 범위 가운데)", true, "안정해석 생략 한계 3.0 m", "콘크리트 뒤채움(구조물) + 배수층"),
        new(Kind.메쌓기, "메쌓기", StoneCols, StoneNames,
            "KDS 11 80 25 · 건설공사 표준품셈 2009 7-1 돌쌓기 표(대표값 = 범위 가운데)", true, "안정해석 생략 한계 1.5 m", "모르타르 없음 · 조약돌 뒤채움이 배수층(배수층 폭 0)"),
    };

    public static Info Of(Kind k) => Kinds.First(i => i.Id == k);

    // ── 첫 표(공공 자료) ─────────────────────────────────────────────────────
    /// <summary>형식의 첫 표 — 공공 자료 그대로 · 표가 없는 형식은 [제안] 규칙으로 채운다.</summary>
    public static Table DefaultTable(Kind k)
    {
        var t = new Table { Kind = k };
        void Add(params double[] r) => t.Rows.Add(r);
        switch (k)
        {
            case Kind.역T형:
                // [표준도] T-1~T-11 — H · t_top · t_bot · B1 · B2 · t_f · t_f2 · Df (B = B1 + t_bot + B2)
                Add(3.0, 0.30, 0.40, 0.30, 1.30, 0.40, 0.40, 1.0);
                Add(3.5, 0.30, 0.40, 0.35, 1.55, 0.40, 0.40, 1.0);
                Add(4.0, 0.30, 0.40, 0.40, 1.80, 0.40, 0.40, 1.0);
                Add(4.5, 0.30, 0.45, 0.45, 2.00, 0.45, 0.45, 1.0);
                Add(5.0, 0.30, 0.50, 0.50, 2.20, 0.50, 0.50, 1.0);
                Add(5.5, 0.30, 0.55, 0.55, 2.40, 0.55, 0.55, 1.0);
                Add(6.0, 0.30, 0.60, 0.60, 2.60, 0.60, 0.60, 1.0);
                Add(6.5, 0.30, 0.65, 0.65, 2.80, 0.65, 0.65, 1.0);
                Add(7.0, 0.30, 0.70, 0.70, 3.00, 0.70, 0.60, 1.0);
                Add(7.5, 0.30, 0.75, 0.75, 3.20, 0.75, 0.65, 1.0);
                Add(8.0, 0.30, 0.90, 0.80, 3.30, 0.90, 0.70, 1.0);
                break;
            case Kind.L형:
                // [표준도] L-1~L-11 — 앞굽 0 (L-5 총폭 표기 3.1은 부분치수 합 3.2로 — 도면 오기로 봄)
                Add(3.0, 0.30, 0.40, 0, 1.60, 0.40, 0.40, 1.0);
                Add(3.5, 0.30, 0.40, 0, 1.90, 0.40, 0.40, 1.0);
                Add(4.0, 0.30, 0.40, 0, 2.20, 0.40, 0.40, 1.0);
                Add(4.5, 0.30, 0.45, 0, 2.45, 0.45, 0.45, 1.0);
                Add(5.0, 0.30, 0.50, 0, 2.70, 0.50, 0.50, 1.0);
                Add(5.5, 0.30, 0.55, 0, 2.95, 0.55, 0.55, 1.0);
                Add(6.0, 0.30, 0.60, 0, 3.20, 0.60, 0.40, 1.0);
                Add(6.5, 0.30, 0.65, 0, 3.45, 0.65, 0.45, 1.0);
                Add(7.0, 0.30, 0.70, 0, 3.70, 0.70, 0.50, 1.0);
                Add(7.5, 0.30, 0.75, 0, 4.25, 0.75, 0.50, 1.0);
                Add(8.0, 0.30, 0.90, 0, 4.70, 0.90, 0.60, 1.0);
                break;
            case Kind.역L형:
                // [제안] 역T형 표의 저판폭 B를 앞굽에 몰아준 꼴(뒷굽 0)
                foreach (var r in DefaultTable(Kind.역T형).Rows) Add(r[0], r[1], r[2], r[3] + r[4], 0, r[5], r[6], r[7]);
                break;
            case Kind.중력식:
                // [표준도] G-1 전면경사 1:0.02 줄 — H · b_top · B · Df
                Add(1.5, 0.40, 1.20, 1.0); Add(2.0, 0.40, 1.60, 1.0); Add(2.5, 0.40, 1.80, 1.0);
                Add(3.0, 0.40, 2.20, 1.0); Add(3.5, 0.40, 2.40, 1.0); Add(4.0, 0.40, 2.80, 1.0);
                break;
            case Kind.반중력식:
                // [표준도] M-1~M-5 — t_f = H/5 · B1 = H/10 · 벽 위 0.3
                Add(2.0, 0.30, 0.50, 0.20, 1.10, 0.40, 0.40, 1.0);
                Add(2.5, 0.30, 0.55, 0.25, 1.20, 0.50, 0.50, 1.0);
                Add(3.0, 0.30, 0.65, 0.30, 1.25, 0.60, 0.60, 1.0);
                Add(3.5, 0.30, 0.75, 0.35, 1.50, 0.70, 0.70, 1.0);
                Add(4.0, 0.30, 0.80, 0.40, 1.60, 0.80, 0.80, 1.0);
                break;
            case Kind.부벽식:
                // [제안] 역T형 규칙을 늘린 것 — B = 0.6H + 0.2 · t_bot = B1 = t_f = H/10 · t_f2 = 0.8 t_f · 8.5 ~ 12 m · 부벽 간격 H/3(0.1 m 내림) · 두께 0.3
                for (double h = 8.5; h <= 12.0 + 1e-9; h += 0.5)
                {
                    double tt = Math.Round(h / 10, 2), B = Math.Round(0.6 * h + 0.2, 2);
                    Add(h, 0.30, tt, tt, Math.Round(B - 2 * tt, 2), tt, Math.Round(0.8 * tt, 2), Math.Floor(h / 3 * 10 + 1e-9) / 10, 0.30, 1.0);
                }
                break;
            case Kind.보강토:
                // [기준] KDS 11 80 10 — L = max(0.7H, 2.5)(0.1 m 올림) · Df = max(0.6, H/20) · 패드 0.15 · 여유 0.075 · 배수층 0.3 · 블록 깊이 0.4 · 수직간격 0.6 · 전면 1:0.05(제안 · 제품별)
                for (double h = 1.5; h <= 12.0 + 1e-9; h += 0.5)
                    Add(h, Math.Max(2.5, Math.Ceiling(0.7 * h * 10 - 1e-9) / 10), 0.40, 0.60, 0.05, 0.15, 0.075, 0.30, Math.Round(Math.Max(0.6, h / 20), 3));
                break;
            case Kind.패널식:
                // [제안] 업체 범위(두께 0.20 ~ 0.36)의 대표 0.26 · 기초 폭 0.6 · 두께 0.4 · 패널 뒤 되메움 0.3 · 근입 0.5(기초 윗면이 땅 아래 0.1)
                for (double h = 2.0; h <= 12.0 + 1e-9; h += 1.0) Add(h, 0.26, 0.60, 0.40, 0.30, 0.50);
                break;
            case Kind.찰쌓기:
                // [지침] 표준품셈 2009 7-1 — 높이 구간 ~1.5 · ~3 · ~5 · ~7 · 7 이상 · 범위 가운데 · 배수층 0.3 · 기초 0.5 × 0.3(제안 · KDS 두께 ≥ 0.15) · 근입 0.6(KDS)
                Add(1.5, 0.25, 0.20, 0.30, 0.30, 0.45, 0.30, 0.50, 0.30, 0.6);
                Add(3.0, 0.30, 0.25, 0.33, 0.30, 0.60, 0.30, 0.55, 0.30, 0.6);
                Add(5.0, 0.35, 0.30, 0.40, 0.30, 0.80, 0.30, 0.65, 0.30, 0.6);
                Add(7.0, 0.40, 0.35, 0.45, 0.30, 1.10, 0.30, 0.70, 0.30, 0.6);
                Add(9.0, 0.45, 0.40, 0.53, 0.30, 1.10, 0.30, 0.80, 0.30, 0.6);
                break;
            case Kind.메쌓기:
                // 조약돌 뒤채움이 곧 배수층 — 배수층 폭 0
                Add(1.5, 0.30, 0.25, 0.30, 0.30, 0.45, 0, 0.50, 0.30, 0.6);
                Add(3.0, 0.35, 0.30, 0.40, 0.30, 0.60, 0, 0.60, 0.30, 0.6);
                Add(5.0, 0.40, 0.35, 0.48, 0.30, 0.80, 0, 0.70, 0.30, 0.6);
                Add(7.0, 0.45, 0.40, 0.60, 0.30, 1.10, 0, 0.80, 0.30, 0.6);
                Add(9.0, 0.50, 0.45, 0.75, 0.30, 1.10, 0, 0.95, 0.30, 0.6);
                break;
        }
        return t;
    }

    /// <summary>칸 이름으로 값을 읽는다.</summary>
    public static double Col(Kind k, double[] row, string col)
    {
        int i = Array.IndexOf(Of(k).Cols, col);
        return i >= 0 && i < row.Length ? row[i] : double.NaN;
    }

    /// <summary>표 검사 — 줄이 H 오름차순 · 칸 수 · 값이 유한하고 음이 아닌가 · H &gt; Df.</summary>
    public static bool Valid(Table t, out string why)
    {
        why = "";
        var info = Of(t.Kind);
        if (t.Rows.Count == 0) { why = "줄이 없다"; return false; }
        for (int i = 0; i < t.Rows.Count; i++)
        {
            var r = t.Rows[i];
            if (r.Length != info.Cols.Length) { why = $"{i + 1}째 줄 칸 {r.Length}(기대 {info.Cols.Length})"; return false; }
            if (r.Any(v => double.IsNaN(v) || double.IsInfinity(v) || v < 0)) { why = $"{i + 1}째 줄에 쓸 수 없는 값"; return false; }
            if (i > 0 && !(r[0] > t.Rows[i - 1][0])) { why = $"{i + 1}째 줄 H가 앞 줄보다 크지 않다"; return false; }
            double df = Col(t.Kind, r, "Df");
            if (!(r[0] > df)) { why = $"{i + 1}째 줄 H {r[0]}가 근입 {df}보다 크지 않다"; return false; }
        }
        return true;
    }

    /// <summary>보이는 높이 h(앞 땅 ~ 벽 위)로 줄을 고른다 — H = h + Df(그 줄의 근입 · 보강토는 앞 땅 경사 규칙과 큰 쪽)를 넘는 첫 줄(올림).
    /// 마지막 줄보다 높으면 마지막 줄 + <paramref name="over"/> · ★[v104 2판 · 검토 중간 7] 첫 줄보다 낮으면 첫 줄 + <paramref name="below"/>(말없이 부풀지 않게 부르는 쪽이 길이를 센다).</summary>
    /// <param name="frontSlope">앞 땅 기울기(벽 쪽으로 오르는 기울기 · 수평 0) — ★[2판 · 검토 중간 6] 보강토 근입(KDS 11 80 10 표 4.1-1)에만 쓴다.</param>
    public static double[] Lookup(Table t, double hVisible, out bool over, out double hTotal, out bool below, out double df, double frontSlope = 0)
    {
        over = false;
        below = false;
        double ruleDf = EmbedRule(t.Kind, hVisible, frontSlope);
        for (int i = 0; i < t.Rows.Count; i++)
        {
            var r = t.Rows[i];
            df = Math.Max(Col(t.Kind, r, "Df"), ruleDf);
            if (r[0] >= hVisible + df - 1e-9)
            {
                hTotal = hVisible + df;
                below = i == 0 && r[0] > hTotal + 0.5 + 1e-9;             // 첫 줄이 실제보다 0.5 m 넘게 높다(표 간격 한 칸 넘게)
                return r;
            }
        }
        var last = t.Rows[^1];
        over = true;
        df = Math.Max(Col(t.Kind, last, "Df"), ruleDf);
        hTotal = hVisible + df;
        return last;
    }

    public static double[] Lookup(Table t, double hVisible, out bool over, out double hTotal) => Lookup(t, hVisible, out over, out hTotal, out _, out _);

    /// <summary>★[2판 · 검토 중간 6] 근입 규칙 — 보강토만: KDS 11 80 10 표 4.1-1(앞 땅 수평 H/20 · 3H:1V H/10 · 2H:1V H/7 · 3H:2V H/5 · 구간 위쪽 값) · ≥ 0.6 m.
    /// H = h + Df라서 Df = f·(h + Df) → Df = f·h/(1 − f). 그 밖의 형식은 0(표 값만).</summary>
    public static double EmbedRule(Kind k, double hVisible, double frontSlope)
    {
        if (k != Kind.보강토) return 0;
        double s = Math.Max(0, frontSlope);
        double f = s <= 0.05 ? 1.0 / 20 : s <= 1.0 / 3 + 1e-9 ? 1.0 / 10 : s <= 0.5 + 1e-9 ? 1.0 / 7 : 1.0 / 5;
        return Math.Max(0.6, f * Math.Max(0, hVisible) / (1 - f));
    }

    // ── 단면 ────────────────────────────────────────────────────────────────
    /// <summary>단면 한 조각 — 재료 · 반시계 링(x, z) · 구조물인가(되메우기에서 뺄 것 · 3D로 세울 것) ·
    /// ★[v104 2판 재검토] 몫(부벽처럼 길이 방향으로 띄엄띄엄 선 것 — 두께/간격 · 넓이 · 공제에 곱한다).</summary>
    public sealed record Part(string Material, IReadOnlyList<(double X, double Z)> Ring, bool Structure, double Fraction = 1.0);

    /// <summary>단면 — 조각들 + 터파기 범위(앞 끝 · 뒤 끝 · 바닥) + 도랑인가(패널식 — 옆면 거의 수직 · 벽 뒤로 법면 없음).</summary>
    public sealed class Shape
    {
        public List<Part> Parts = new();
        public double XFront, XBack, ZBottom, HTotal, Df;
        /// <summary>도랑(패널식) — 옆면 거의 수직 · <b>뒤 여유 0</b> · 뒤 끝은 깎기면 앞 · 예산 늘리기에서 뺀다.</summary>
        public bool Trench;
        public bool Over, Below;
        /// <summary>단면 앞면 기울기(1:n) — 앞 땅(x 0, z 0)에서 꼭대기 앞 모서리로(쐐기 공제 · 기울기 차이 ⚠에 쓴다).</summary>
        public double FaceBatter;
        public double StructArea => Parts.Where(p => p.Structure).Sum(p => p.Fraction * Math.Abs(Area(p.Ring)));
    }

    /// <summary>공통 칸 — 버림 두께 · 돌출 · 콘크리트 앞면 경사(1:n) · 정지면 벽 앞면 경사(1:n — 패널식 깎기면 · 쐐기 공제 기준).</summary>
    public sealed record Common(double TLean = 0.10, double ELean = 0.10, double FrontBatter = 0.02, double GradeFace = 0.01);

    /// <summary>보이는 높이 h의 단면 — 표에서 줄을 고르고(올림) 벽 높이는 <b>실제 높이</b>(h + Df)로 세운다(기초 · 두께는 그 줄 값 — 보수적).</summary>
    public static Shape Build(Table t, double hVisible, Common c, bool cut, double frontSlope = 0)
    {
        var row = Lookup(t, Math.Max(0, hVisible), out bool over, out double hTot, out bool below, out double df, frontSlope);
        var s = BuildRow(t.Kind, row, hTot, c, cut, df);
        s.Over = over; s.Below = below;
        return s;
    }

    /// <summary>줄 하나 · 실제 전체 높이 <paramref name="hTot"/>(근입 포함)로 단면을 세운다 · <paramref name="dfUse"/>가 있으면 그 근입(규칙이 표보다 깊을 때).</summary>
    public static Shape BuildRow(Kind k, double[] row, double hTot, Common c, bool cut, double dfUse = double.NaN)
    {
        var s = new Shape { HTotal = hTot };
        double V(string col) => Col(k, row, col);
        double df = double.IsNaN(dfUse) ? V("Df") : dfUse; s.Df = df;
        double zTop = hTot - df;                                      // 벽 꼭대기(앞 땅 기준)
        double nf = Math.Max(0, c.FrontBatter);
        double tl = Math.Max(0, c.TLean), el = Math.Max(0, c.ELean);
        void Ring(string mat, bool st, params (double X, double Z)[] p) => s.Parts.Add(new Part(mat, Ccw(p), st));
        void RingF(string mat, double f, params (double X, double Z)[] p) => s.Parts.Add(new Part(mat, Ccw(p), true, f));

        switch (k)
        {
            case Kind.역T형: case Kind.L형: case Kind.역L형: case Kind.반중력식: case Kind.부벽식:
            {
                double tTop = V("t_top"), tBot = V("t_bot"), b1 = V("B1"), b2 = V("B2"), tf = V("t_f"), tf2 = V("t_f2");
                double zb = -df, zft = zb + tf;                          // 저판 아래 · 저판 위(벽 자리)
                double xs0 = nf * zft;                                   // 벽 앞면이 저판 위와 만나는 x(앞면 1:nf — 앞 땅에서 x = 0)
                double xToe = xs0 - b1, xHeel = xs0 + tBot + b2;
                // 저판 — 벽 아래 두께 tf · 앞굽 · 뒷굽 끝은 tf2(헌치)
                Ring("콘크리트", true, (xToe, zb), (xHeel, zb), (xHeel, zb + tf2), (xs0 + tBot, zft), (xs0, zft), (xToe, zb + (b1 > 1e-9 ? tf2 : tf)));
                // 벽체 — 앞면 1:nf · 위 t_top · 아래 t_bot
                double xTopF = nf * zTop;
                Ring("콘크리트", true, (xs0, zft), (xs0 + tBot, zft), (xTopF + tTop, zTop), (xTopF, zTop));
                // 버림
                Ring("버림", true, (xToe - el, zb - tl), (xHeel + el, zb - tl), (xHeel + el, zb), (xToe - el, zb));
                // ★[v104 2판 재검토] 부벽 — 벽 뒷면 · 뒷굽 끝 · 벽 꼭대기 뒤를 잇는 세모를 «평균»(두께/간격)으로
                if (k == Kind.부벽식)
                {
                    double sc = V("s_cf"), tc = V("t_cf");
                    if (sc > 1e-9 && tc > 1e-9 && b2 > 1e-9)
                        RingF("부벽(평균)", Math.Min(1, tc / sc), (xs0 + tBot, zft), (xHeel, zb + tf2), (xTopF + tTop, zTop));
                }
                s.XFront = xToe - el; s.XBack = xHeel + el; s.ZBottom = zb - tl; s.FaceBatter = nf;
                break;
            }
            case Kind.중력식:
            {
                double bTop = V("b_top"), B = V("B");
                double zb = -df, x0 = nf * zb, xTopF = nf * zTop;
                Ring("콘크리트", true, (x0, zb), (x0 + B, zb), (xTopF + bTop, zTop), (xTopF, zTop));
                Ring("버림", true, (x0 - el, zb - tl), (x0 + B + el, zb - tl), (x0 + B + el, zb), (x0 - el, zb));
                s.XFront = x0 - el; s.XBack = x0 + B + el; s.ZBottom = zb - tl; s.FaceBatter = nf;
                break;
            }
            case Kind.보강토:
            {
                double L = V("L"), dB = V("D_blk"), pt = V("pad_t"), pe = V("pad_e"), wd = V("w_drain");
                double nb = Math.Max(0, V("batter"));                    // 블록 뒤로 기울기(첫 표 1:0.05 · 제품별 3° 안팎)
                double zb = -df, x0 = nb * zb, xT = nb * zTop;
                Ring("블록", true, (x0, zb), (x0 + dB, zb), (xT + dB, zTop), (xT, zTop));
                Ring("기초패드", true, (x0 - pe, zb - pt), (x0 + dB + pe, zb - pt), (x0 + dB + pe, zb), (x0 - pe, zb));
                // 보강토체(선별 뒤채움) — 블록 뒤 ~ 보강재 끝 L(앞면에서) · 배수층은 블록 바로 뒤 w_drain
                double wdd = Math.Min(wd, Math.Max(0, L - dB));
                Ring("배수층", false, (x0 + dB, zb), (x0 + dB + wdd, zb), (xT + dB + wdd, zTop), (xT + dB, zTop));
                if (L > dB + wdd + 1e-9) Ring("보강토체", false, (x0 + dB + wdd, zb), (x0 + L, zb), (xT + L, zTop), (xT + dB + wdd, zTop));
                s.XFront = x0 - pe; s.XBack = x0 + Math.Max(L, dB + pe); s.ZBottom = zb - pt; s.FaceBatter = nb;
                break;
            }
            case Kind.패널식:
            {
                // ★[v104 2판 · 검토 높음 1] 기준선 = 정지면 앞면 = <b>깎기면</b>(x = GradeFace·z) · 패널 · 패널 뒤 되메움은 그 <b>앞</b>(x &lt; 깎기면) ·
                //   패널은 깎기면과 나란히(gap 일정) · 기초는 패널 가운데 아래 · 도랑 뒤 끝은 깎기면 앞에서 끝난다(벽 뒤를 파지 않게)
                double pt = V("panel_t"), bf = V("b_ft"), tf = V("t_ft"), gap = V("gap");
                double ng = Math.Max(0, c.GradeFace);
                double zb = -df, zft = zb + tf;
                double xc0 = ng * zft, xcT = ng * zTop;                  // 깎기면(패널 아래 · 꼭대기)
                double xpb0 = xc0 - gap, xpbT = xcT - gap;               // 패널 뒷면
                double xpf0 = xpb0 - pt, xpfT = xpbT - pt;               // 패널 앞면
                double xf0 = (xpf0 + xpb0) / 2 - bf / 2;                 // 기초는 패널 가운데 아래
                Ring("기초", true, (xf0, zb), (xf0 + bf, zb), (xf0 + bf, zft), (xf0, zft));
                Ring("패널", true, (xpf0, zft), (xpb0, zft), (xpbT, zTop), (xpfT, zTop));
                if (gap > 1e-9) Ring("패널 뒤 되메움", false, (xpb0, zft), (xc0, zft), (xcT, zTop), (xpbT, zTop));
                Ring("버림", true, (xf0 - el, zb - tl), (xf0 + bf + el, zb - tl), (xf0 + bf + el, zb), (xf0 - el, zb));
                s.XFront = xf0 - el;
                s.XBack = Math.Min(xf0 + bf + el, ng * (zb - tl));         // 깎기면(아래로 늘인 것) 앞에서 끝
                s.ZBottom = zb - tl; s.Trench = true;
                s.FaceBatter = ng;                                        // 정지면 앞면 = 깎기면 — 쐐기 없음
                break;
            }
            case Kind.찰쌓기: case Kind.메쌓기:
            {
                double n = cut ? V("n_cut") : V("n_fill"), ts = V("t_stone"), tbT = V("t_back_top"), tbB = cut ? tbT : V("t_back_bot");
                double bf = V("b_ft"), tf = V("t_ft"), wd = Math.Max(0, V("w_drain"));
                double zb = -df, zft = zb + tf;
                double x0 = n * zft, xT = n * zTop;                       // 앞면 1:n
                double xf0 = x0 - Math.Max(0, (bf - ts) / 2);
                Ring("기초", true, (xf0, zb), (xf0 + bf, zb), (xf0 + bf, zft), (xf0, zft));
                Ring(k == Kind.찰쌓기 ? "찰쌓기" : "메쌓기", true, (x0, zft), (x0 + ts, zft), (xT + ts, zTop), (xT, zTop));
                // ★[v104 2판 재검토] 찰쌓기 뒤채움은 콘크리트(구조물 — 되메우기에서 뺀다) · 메쌓기는 조약돌(구조물 아님) · 그 뒤 배수층
                bool wet = k == Kind.찰쌓기;
                Ring(wet ? "뒤채움 콘크리트" : "뒤채움(조약돌)", wet, (x0 + ts, zft), (x0 + ts + tbB, zft), (xT + ts + tbT, zTop), (xT + ts, zTop));
                if (wd > 1e-9) Ring("배수층", false, (x0 + ts + tbB, zft), (x0 + ts + tbB + wd, zft), (xT + ts + tbT + wd, zTop), (xT + ts + tbT, zTop));
                // ★[v104 2판 재검토] 기초 밑 버림(다른 형식과 같은 공통 칸)
                Ring("버림", true, (xf0 - el, zb - tl), (xf0 + bf + el, zb - tl), (xf0 + bf + el, zb), (xf0 - el, zb));
                s.XFront = xf0 - el; s.XBack = Math.Max(xf0 + bf + el, x0 + ts + tbB + wd); s.ZBottom = zb - tl; s.FaceBatter = n;
                break;
            }
        }
        return s;
    }

    /// <summary>반시계로 · 연달아 같은 점(앞굽 0인 L형 등)은 뺀다.</summary>
    static List<(double X, double Z)> Ccw((double X, double Z)[] p)
    {
        var l = new List<(double X, double Z)>();
        foreach (var q in p)
            if (l.Count == 0 || Math.Abs(l[^1].X - q.X) > 1e-12 || Math.Abs(l[^1].Z - q.Z) > 1e-12) l.Add(q);
        while (l.Count > 1 && Math.Abs(l[0].X - l[^1].X) <= 1e-12 && Math.Abs(l[0].Z - l[^1].Z) <= 1e-12) l.RemoveAt(l.Count - 1);
        if (Area(l) < 0) l.Reverse();
        return l;
    }

    /// <summary>링 넓이(부호 — 반시계 +).</summary>
    public static double Area(IReadOnlyList<(double X, double Z)> r)
    {
        double a = 0;
        for (int i = 0; i < r.Count; i++) { var p = r[i]; var q = r[(i + 1) % r.Count]; a += p.X * q.Z - q.X * p.Z; }
        return a / 2;
    }

    // ── 도면 기록(표) ───────────────────────────────────────────────────────
    /// <summary>표 기록 판.</summary>
    public const int TableVersion = 1;

    /// <summary>표 기록 값 — [I 판 · I 형식 · I 칸 수 · I 줄 수 · D…]. 첫 표와 같으면 null(기록을 지운다).</summary>
    public static List<(char K, object V)>? Encode(Table t)
    {
        if (t.Print == DefaultTable(t.Kind).Print) return null;
        int nc = Of(t.Kind).Cols.Length;
        var l = new List<(char, object)> { ('I', TableVersion), ('I', (int)t.Kind), ('I', nc), ('I', t.Rows.Count) };
        foreach (var r in t.Rows) foreach (var v in r) l.Add(('D', v));
        return l;
    }

    /// <summary>기록을 읽는다 — 없으면 첫 표(참) · 못 읽거나 틀리면 첫 표 + 까닭(거짓).</summary>
    public static bool TryDecode(Kind k, IReadOnlyList<(char K, object V)>? vals, out Table t, out string why)
    {
        t = DefaultTable(k); why = "";
        if (vals == null || vals.Count == 0) return true;
        try
        {
            if (vals.Count < 4 || vals[0].K != 'I' || (int)vals[0].V != TableVersion) { why = $"{k} 표 기록 판이 다르다"; return false; }
            if ((int)vals[1].V != (int)k) { why = $"{k} 표 기록의 형식이 다르다({vals[1].V})"; return false; }
            int nc = (int)vals[2].V, nr = (int)vals[3].V;
            if (nc != Of(k).Cols.Length || nr < 1 || vals.Count != 4 + nc * nr) { why = $"{k} 표 기록 꼴이 다르다(칸 {nc} · 줄 {nr} · 값 {vals.Count - 4})"; return false; }
            var r = new Table { Kind = k };
            for (int i = 0; i < nr; i++)
            {
                var row = new double[nc];
                for (int j = 0; j < nc; j++) { var e = vals[4 + i * nc + j]; if (e.K != 'D') { why = $"{k} 표 기록 {i + 1}째 줄 값 형이 다르다"; return false; } row[j] = (double)e.V; }
                r.Rows.Add(row);
            }
            if (!Valid(r, out string bad)) { why = $"{k} 표 기록 값이 틀리다 — {bad}"; return false; }
            t = r;
            return true;
        }
        catch (Exception ex) { why = $"{k} 표 기록을 못 읽었다 {ex.GetType().Name}"; t = DefaultTable(k); return false; }
    }
}
