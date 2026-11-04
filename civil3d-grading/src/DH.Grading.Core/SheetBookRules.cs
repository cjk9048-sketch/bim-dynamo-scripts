using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DH.Grading.Core;

/// <summary>★★★[v112.0 · 계획_도면화.md 4.1 · JACK 1103 «도면화가 제일 중요»] <b>도면화(배치탭 + 도곽 + 표제란)의 순수 계산</b> — 하네스(S190 SB)로 잰다.
/// <para>모형 장 알아보기 · 축척 사다리 · 도면명 · 도면번호 · 축척 글 · 날짜 · 페이지 설정 이름 · 장 자리 · 평면 축척 · 글자 칸 맞춤 · 도곽 칸 찾기 · 서명 · 이름 겹침 고리.</para>
/// <para>단위: 종이 = mm · 모형 = m(1:N이면 종이 1 mm = 모형 N/1000 m).</para></summary>
public static class SheetBookRules
{
    public const double PaperW = 841.0, PaperH = 594.0;
    /// <summary>모형 장의 그림 자리 — 바깥 왼쪽 아래 + (25, 50) · 796 × 524(종이 mm · <c>SheetCommand</c> 여백과 같음).</summary>
    public const double DrawLeft = 25.0, DrawBottom = 50.0, DrawW = 796.0, DrawH = 524.0;

    /// <summary>탭 종류 — 이 차례가 곧 탭 차례(① ~ ⑤).</summary>
    public enum Kind { Plan, Prof, Xsec, RProf, RXsec }

    public static readonly Kind[] Kinds = { Kind.Plan, Kind.Prof, Kind.Xsec, Kind.RProf, Kind.RXsec };

    public static string Code(Kind k) => k switch { Kind.Plan => "PLAN", Kind.Prof => "PROF", Kind.Xsec => "XSEC", Kind.RProf => "RPROF", _ => "RXSEC" };
    public static Kind? FromCode(string? c) => c switch { "PLAN" => Kind.Plan, "PROF" => Kind.Prof, "XSEC" => Kind.Xsec, "RPROF" => Kind.RProf, "RXSEC" => Kind.RXsec, _ => null };
    public static string TabName(Kind k) => k switch { Kind.Plan => "전체 계획평면도", Kind.Prof => "부지 종단면도", Kind.Xsec => "부지 횡단면도", Kind.RProf => "진입도로 종단면도", _ => "진입도로 횡단면도" };
    public static string DefaultTitle(Kind k) => k switch { Kind.Plan => "토공 계획평면도", Kind.Prof => "토공 종단면도", Kind.Xsec => "토공 횡단면도", Kind.RProf => "진입도로 종단면도", _ => "진입도로 횡단면도" };
    public static string ShortName(Kind k) => k switch { Kind.Plan => "평면", Kind.Prof => "부지종단", Kind.Xsec => "부지횡단", Kind.RProf => "도로종단", _ => "도로횡단" };

    // ══════════════════════════ 모형 장 알아보기 ══════════════════════════

    /// <summary>모형 공간의 닫힌 사각형 하나(읽은 그대로).</summary>
    /// <param name="FrameKind">장 표식 <c>DH_SHEETFRAME</c>의 종류(PROF/XSEC) — 없으면 빈 글.</param>
    /// <param name="TagScale">표식의 장 축척(없으면 0).</param>
    /// <param name="TagAnno">표식의 그린 때 주석 축척(없으면 0).</param>
    /// <param name="RoadHandle">진입도로 표식 <c>DH_ROADSHEET</c>의 노선 핸들(없으면 빈 글).</param>
    /// <param name="RoadKind">진입도로 표식의 종류 칸(«XSEC» = 횡단).</param>
    public sealed record RectIn(double X0, double Y0, double X1, double Y1, bool XsecLayer, string FrameKind, double TagScale, double TagAnno,
                                string RoadHandle, string RoadKind, string Id = "");

    /// <summary>알아본 장 하나.</summary>
    public sealed record Sheet(Kind Kind, double X0, double Y0, double Scale, double Anno, bool Tagged, string Road, string Id)
    {
        public double S => Scale / 1000.0;
        public double X1 => X0 + PaperW * S;
        public double Y1 => Y0 + PaperH * S;
        /// <summary>모형 그림 자리(m).</summary>
        public (double X0, double Y0, double X1, double Y1) Draw => (X0 + DrawLeft * S, Y0 + DrawBottom * S, X0 + (DrawLeft + DrawW) * S, Y0 + (DrawBottom + DrawH) * S);
        public (double X, double Y) DrawCenter => (X0 + (DrawLeft + DrawW / 2) * S, Y0 + (DrawBottom + DrawH / 2) * S);
    }

    public const double RatioTol = 0.002, LadderTol = 0.005;

    /// <summary>사각형 목록 → 종류별 장(차례 = 부지 x 오름 · 진입도로 노선 차례 → x). 표식이 있으면 비를 안 본다(그래프가 도곽보다 높은 장 — 4.4-1).
    /// <para>표식 없는 옛 장 = 비 841:594 ±0.2 % · 주석 축척 = <paramref name="cannoscale"/>(⚠). 축척은 사다리 ±0.5 % 안이면 붙이고 아니면 그대로 + ⚠. 같은 자리 겹친 사각형은 하나로.</para></summary>
    /// <param name="roadOrder">노선 핸들 → 정렬 열쇠(노선 이름) — 없으면 핸들 그대로.</param>
    public static List<Sheet> Recognize(IEnumerable<RectIn> rects, IReadOnlyList<double>? ladder, double cannoscale,
                                        Func<string, string>? roadOrder, List<string> warns)
    {
        var list = new List<Sheet>();
        int untagged = 0, offLadder = 0;
        foreach (var r in rects)
        {
            double w = r.X1 - r.X0, h = r.Y1 - r.Y0;
            if (!(w > 0) || !(h > 0) || !double.IsFinite(w) || !double.IsFinite(h)) continue;
            bool tagged = r.FrameKind.Length > 0;
            if (!tagged && Math.Abs(w / h - PaperW / PaperH) > PaperW / PaperH * RatioTol) continue;   // 안쪽 사각형 · 칸 · 남의 사각형
            double sc = tagged && r.TagScale > 0 ? r.TagScale : w * 1000.0 / PaperW;
            double snapped = SnapLadder(sc, ladder, out bool on);
            if (!on) offLadder++;
            Kind k = r.RoadHandle.Length > 0
                ? (r.XsecLayer || r.RoadKind == "XSEC" || r.FrameKind == "XSEC" ? Kind.RXsec : Kind.RProf)
                : (r.XsecLayer || r.FrameKind == "XSEC" ? Kind.Xsec : Kind.Prof);
            double anno = tagged && r.TagAnno > 0 ? r.TagAnno : cannoscale;
            if (!tagged) untagged++;
            // 같은 자리 겹친 사각형(옛 판이 두 번 그린 것) — 하나로
            double tol = Math.Max(1e-6, snapped / 1000.0 * 0.5);
            if (list.Any(s => s.Kind == k && Math.Abs(s.X0 - r.X0) < tol && Math.Abs(s.Y0 - r.Y0) < tol && Math.Abs(s.Scale - snapped) < 1e-6)) continue;
            list.Add(new Sheet(k, r.X0, r.Y0, snapped, anno, tagged, r.RoadHandle, r.Id));
        }
        if (untagged > 0) warns.Add($"옛 장 {untagged} — [종단도]/[횡단도] 다시 그리면 글자 크기가 정확");
        if (offLadder > 0) warns.Add($"축척이 표준 사다리 밖인 장 {offLadder}");
        string Key(Sheet s) => s.Road.Length == 0 ? "" : (roadOrder?.Invoke(s.Road) ?? s.Road);
        return list.OrderBy(s => (int)s.Kind).ThenBy(s => Key(s), StringComparer.Ordinal).ThenBy(s => s.X0).ThenBy(s => -s.Y0).ToList();
    }

    /// <summary>사다리 ±0.5 % 안이면 그 값 · 아니면 그대로(<paramref name="on"/> 거짓).</summary>
    public static double SnapLadder(double v, IReadOnlyList<double>? ladder, out bool on)
    {
        on = true;
        if (ladder == null || ladder.Count == 0 || !(v > 0)) return v;
        foreach (double l in ladder) if (Math.Abs(v - l) <= l * LadderTol) return l;
        on = false;
        return Math.Round(v, 6);
    }

    /// <summary>장 i(0부터)의 배치 원점(종이 mm) — 왼쪽 → 오른쪽 맞붙임.</summary>
    public static (double X, double Y) SheetOrigin(int i, double ox = 0, double oy = 0) => (ox + i * PaperW, oy);

    // ══════════════════════════ 표제란 글 ══════════════════════════

    /// <summary>«{앞말} {기본 이름}» + 장이 둘 이상이면 « (i/n)».</summary>
    public static string DrawingTitle(string? prefix, string? baseTitle, int i, int n)
    {
        string t = ((prefix ?? "").Trim() + " " + (baseTitle ?? "").Trim()).Trim();
        return n > 1 ? $"{t} ({i}/{n})" : t;
    }

    /// <summary>접두 + (시작 + k)를 세 자리 이상 0 채움 — «C-» · 1 · 0 → «C-001».</summary>
    public static string DrawingNo(string? prefix, int start, int k)
    {
        int v = Math.Max(0, start) + Math.Max(0, k);
        return (prefix ?? "") + v.ToString("D3", CultureInfo.InvariantCulture);
    }

    /// <summary>축척 칸 — 평면 · 횡단 «1:600» · 종단 «H=1:1000, V=1:200»(세로 = 가로 ÷ 수직과장 · 같으면 «1:200»).</summary>
    public static string ScaleText(Kind k, double scale, double vExag = 1.0)
    {
        string N(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);
        if ((k == Kind.Prof || k == Kind.RProf) && vExag > 0 && double.IsFinite(vExag))
        {
            double v = scale / vExag;
            if (Math.Abs(v - scale) > 1e-6) return $"H=1:{N(scale)}, V=1:{N(v)}";
        }
        return $"1:{N(scale)}";
    }

    /// <summary>★[v112.2 · JACK 1104 «날짜는 기본 현재 날짜 · 사용자가 바꿀 수 있게»] 날짜 기본 꼴 — 오늘(yyyy.MM.dd).</summary>
    public const string DateFmt = "yyyy.MM.dd";

    /// <summary>날짜 칸 — 비면 오늘(<see cref="DateFmt"/> · 도곽 칸 꼴이 있으면 그 꼴).</summary>
    public static string DateText(string? input, DateTime now, string fmt = DateFmt)
        => string.IsNullOrWhiteSpace(input) ? now.ToString(string.IsNullOrWhiteSpace(fmt) ? DateFmt : fmt, CultureInfo.InvariantCulture) : input.Trim();

    /// <summary>페이지 설정 이름 — «DH {약칭} {i}-{n} {종이}» · 장이 하나면 «DH {약칭} {종이}»(«/» 안 씀).</summary>
    public static string PageSetupName(Kind k, int i, int n, string paper)
        => n > 1 ? $"DH {ShortName(k)} {i}-{n} {paper}" : $"DH {ShortName(k)} {paper}";

    /// <summary>이름 겹침 고리 — 그대로 → «(DH)» → «(DH2)» … 빈 이름까지.</summary>
    public static string UniqueName(string want, Func<string, bool> taken)
    {
        if (!taken(want)) return want;
        if (!taken(want + "(DH)")) return want + "(DH)";
        for (int i = 2; i < 10000; i++) { string c = $"{want}(DH{i})"; if (!taken(c)) return c; }
        return want + "(DH" + Guid.NewGuid().ToString("N").Substring(0, 6) + ")";
    }

    // ══════════════════════════ 평면 ══════════════════════════

    /// <summary>평면 축척 사다리(JACK 참고도면 · 계획 3장).</summary>
    public static readonly double[] PlanLadder = { 200, 250, 300, 500, 600, 1000, 1200, 2000, 2500, 3000, 5000 };

    /// <summary>범위(m)가 종이 자리(mm)에 들어가는 가장 작은 사다리 값 — 고정(<paramref name="pinned"/> &gt; 0)이면 그 값. 1:5000에도 안 들면 5000 + 넘침.</summary>
    public static (double Scale, bool Fits) PlanScale(double wM, double hM, double availWmm, double availHmm, double pinned = 0)
    {
        bool Fit(double s) => wM * 1000.0 / s <= availWmm + 1e-9 && hM * 1000.0 / s <= availHmm + 1e-9;
        if (pinned > 0) return (pinned, Fit(pinned));
        foreach (double s in PlanLadder) if (Fit(s)) return (s, true);
        return (PlanLadder[^1], false);
    }

    /// <summary>★[v112.5 · JACK 1104 «도면 기울기»] 폭 × 높이 상자를 <paramref name="rad"/>만큼 돌렸을 때 감싸는 상자 크기.</summary>
    public static (double W, double H) RotatedBox(double w, double h, double rad)
    {
        double c = Math.Abs(Math.Cos(rad)), s = Math.Abs(Math.Sin(rad));
        return (w * c + h * s, w * s + h * c);
    }

    /// <summary>평면도 자리(종이 mm · 그림 자리 기준) — 위 40 제목 · 오른쪽 140 띠 · 나머지 뷰포트(K-water 그림 자리면 25–681 × 50–534 = 656 × 484).
    /// 다른 도곽이면 같은 비율로.</summary>
    public static (double[] Viewport, double[] Band, double[] Title) PlanLayout(double[] draw)
    {
        double w = draw[2] - draw[0], h = draw[3] - draw[1];
        double band = 140.0 * w / DrawW, title = 40.0 * h / DrawH;
        return (new[] { draw[0], draw[1], draw[2] - band, draw[3] - title },
                new[] { draw[2] - band, draw[1], draw[2], draw[3] },
                new[] { draw[0], draw[3] - title, draw[2] - band, draw[3] });
    }

    // ══════════════════════════ 글자 칸 맞춤 ══════════════════════════

    /// <summary>글자 폭이 칸 폭 − 2 mm를 넘으면 WidthFactor를 0.7까지 줄이고, 그래도 넘치면 높이를 줄인다.
    /// <paramref name="textW1"/> = 높이 <paramref name="h"/> · 폭 배율 1일 때 글 폭(mm). 높이는 칸 높이 − 1 mm도 넘지 않게.</summary>
    public static (double H, double WidthFactor) FitText(double textW1, double h, double cellW, double cellH)
    {
        double avail = Math.Max(1.0, cellW - 2.0);
        double hh = cellH > 1.5 ? Math.Min(h, cellH - 1.0) : h;
        double w = textW1 * hh / h;
        if (!(w > 0) || w <= avail) return (hh, 1.0);
        double wf = avail / w;
        if (wf >= 0.7) return (hh, wf);
        return (hh * avail / (w * 0.7), 0.7);
    }

    /// <summary>글 지우기 판정 — 경계상자 가운데가 칸 안(정렬 글은 삽입점이 칸 밖일 수 있어 삽입점으로 안 잼).</summary>
    public static bool CenterIn(double bx0, double by0, double bx1, double by1, double[] cell)
        => (bx0 + bx1) / 2 >= cell[0] - 1e-9 && (bx0 + bx1) / 2 <= cell[2] + 1e-9 && (by0 + by1) / 2 >= cell[1] - 1e-9 && (by0 + by1) / 2 <= cell[3] + 1e-9;

    // ══════════════════════════ 서명(옛것 판정) ══════════════════════════

    /// <summary>장마다 (주인, 바깥 왼쪽 아래 0.001 m, 축척, 그린 때 주석 축척) + 도곽 이름(+ 평면은 범위 · 축척 · 면 도장) — 같은 모형이면 같은 글.</summary>
    public static string Signature(IEnumerable<Sheet> sheets, string frameName, string extra = "")
    {
        var sb = new StringBuilder();
        foreach (var s in sheets)
            sb.Append(s.Road).Append('@').Append(s.X0.ToString("F3", CultureInfo.InvariantCulture)).Append(',').Append(s.Y0.ToString("F3", CultureInfo.InvariantCulture))
              .Append(":1/").Append(s.Scale.ToString("0.###", CultureInfo.InvariantCulture)).Append("a").Append(s.Anno.ToString("0.###", CultureInfo.InvariantCulture)).Append(';');
        sb.Append('|').Append(frameName).Append('|').Append(extra);
        return Hash8(sb.ToString());
    }

    public static string Hash8(string s)
    {
        using var sha = System.Security.Cryptography.SHA256.Create();
        var h = sha.ComputeHash(Encoding.UTF8.GetBytes(s));
        return Convert.ToHexString(h, 0, 4);
    }

    // ══════════════════════════ 도곽 정의(json) ══════════════════════════

    public sealed class Cell
    {
        [JsonPropertyName("rect")] public double[] Rect { get; set; } = new double[4];
        [JsonPropertyName("h")] public double H { get; set; } = 3.0;
        /// <summary>«always» = 그 칸 도곽 글을 늘 지움 · «ifValue» = 우리 값이 있을 때만.</summary>
        [JsonPropertyName("clear")] public string Clear { get; set; } = "always";
        [JsonPropertyName("fmt")] [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Fmt { get; set; }
    }

    /// <summary>도곽 정의 — 좌표는 모두 <b>종이 mm · 바깥 왼쪽 아래 기준</b>. 블록은 <see cref="Origin"/>(도곽 단위)을 빼고 <see cref="K"/>배로 넣는다.</summary>
    public sealed class FrameDef
    {
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        [JsonPropertyName("unit")] public string Unit { get; set; } = "mm";
        [JsonPropertyName("k")] public double K { get; set; } = 1.0;
        [JsonPropertyName("origin")] public double[] Origin { get; set; } = { 0, 0 };
        [JsonPropertyName("outer")] public double[] Outer { get; set; } = { 0, 0, PaperW, PaperH };
        [JsonPropertyName("border")] public double[] Border { get; set; } = { 0, 0, PaperW, PaperH };
        [JsonPropertyName("drawArea")] public double[] DrawArea { get; set; } = { 0, 0, PaperW, PaperH };
        [JsonPropertyName("textStyle")] public string TextStyle { get; set; } = "";
        [JsonPropertyName("cells")] public Dictionary<string, Cell> Cells { get; set; } = new();
        [JsonPropertyName("clearOnly")] public List<double[]> ClearOnly { get; set; } = new();
        [JsonPropertyName("found")] public List<string> Found { get; set; } = new();
        /// <summary>속성 정의 표제란 — 태그 → 칸 이름.</summary>
        [JsonPropertyName("attrs")] public Dictionary<string, string> Attrs { get; set; } = new();
        /// <summary>검출 때 남긴 짧은 말(칸 못 찾음 · 그림 자리 작음 …) — 도면설정 딱지.</summary>
        [JsonPropertyName("notes")] public List<string> Notes { get; set; } = new();

        static readonly JsonSerializerOptions Opt = new()
        {
            WriteIndented = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        public string ToJson() => JsonSerializer.Serialize(this, Opt);
        public static FrameDef? FromJson(string s)
        {
            try { return JsonSerializer.Deserialize<FrameDef>(s, Opt); } catch { return null; }
        }
        public string JsonHash4() => Hash8(ToJson()).Substring(0, 4);

        public bool DrawAreaSmall => DrawArea[2] - DrawArea[0] < DrawW - 0.5 || DrawArea[3] - DrawArea[1] < DrawH - 0.5;
    }

    /// <summary>칸 이름(차례 = 도킹창 · 경고 글).</summary>
    public static readonly string[] CellNames = { "도면명", "도면번호", "축척", "날짜", "공사명" };

    /// <summary>코드로 그리는 «단순 도곽» 정의 — K-water와 같은 바깥 · 테두리 · 띠 · 다섯 칸 자리(파일이 없어도 도면화는 된다).</summary>
    public static FrameDef SimpleFrame() => new()
    {
        Name = "단순 도곽", Unit = "mm", K = 1.0,
        Outer = new double[] { 0, 0, PaperW, PaperH }, Border = new double[] { 25, 20, 821, 574 }, DrawArea = new double[] { 25, 50, 821, 574 },
        Cells = new()
        {
            ["도면명"] = new() { Rect = new[] { 420, 38.02, 535, 50 }, H = 4.0 },
            ["도면번호"] = new() { Rect = new[] { 490, 32.02, 535, 38.02 }, H = 3.0 },
            ["축척"] = new() { Rect = new[] { 420, 26.02, 465, 32.02 }, H = 3.0 },
            ["날짜"] = new() { Rect = new[] { 560, 26.02, 585, 32.02 }, H = 3.0, Fmt = DateFmt },
            ["공사명"] = new() { Rect = new[] { 50.0, 20, 185, 30 }, H = 2.5 },
        },
        Found = new() { "도면명", "도면번호", "축척", "날짜", "공사명" },
    };

    /// <summary>단순 도곽의 선(종이 mm) — 바깥 · 테두리 · 띠 윗선 · 칸 세로선 · 칸 가로선 · 이름표(글, x, y, 높이).</summary>
    public static (List<(double X0, double Y0, double X1, double Y1)> Lines, List<(string T, double X, double Y, double H)> Labels) SimpleFrameDrawing()
    {
        var l = new List<(double, double, double, double)>
        {
            (25, 50, 821, 50),                                   // 띠 윗선
            (50, 20, 50, 30), (25, 25, 50, 25), (185, 20, 185, 50), (25, 30, 185, 30),   // 공사명
            (395, 20, 395, 50), (420, 20, 420, 50), (465, 20, 465, 38.02), (490, 20, 490, 38.02), (535, 20, 535, 50),
            (395, 38.02, 535, 38.02), (395, 32.02, 535, 32.02), (395, 26.02, 535, 26.02),
            (535, 26.02, 821, 26.02), (535, 32.02, 821, 32.02), (560, 20, 560, 32.02), (585, 20, 585, 32.02),
        };
        var t = new List<(string, double, double, double)>
        {
            ("공사명", 37.5, 27.5, 2.0), ("도면명", 407.5, 44.0, 3.0), ("도면번호", 477.5, 35.0, 3.0), ("축척", 407.5, 29.0, 3.0),
            ("날짜", 572.5, 23.0, 3.0), ("구도면번호", 407.5, 35.0, 2.2), ("편철번호", 477.5, 29.0, 2.2),
        };
        return (l, t);
    }

    // ══════════════════════════ 사용자 도곽 검출 ══════════════════════════

    public readonly record struct Seg(double X0, double Y0, double X1, double Y1);
    public readonly record struct Txt(string Text, double X0, double Y0, double X1, double Y1)
    {
        public double Cx => (X0 + X1) / 2;
        public double Cy => (Y0 + Y1) / 2;
    }
    public readonly record struct AttDef(string Tag, string Prompt, double X, double Y);

    /// <summary>칸 이름 낱말 표(대소문자 · 띄어쓰기 · 마침표 · 쌍점 · 밑줄 무시 · 같을 때만 — «구도면번호»가 «도면번호»에 안 걸리게).</summary>
    public static readonly (string Cell, string[] Words)[] Keywords =
    {
        ("도면명", new[] { "도면명", "TITLE", "DRAWING TITLE", "DRAWINGTITLE", "DWG TITLE" }),
        ("도면번호", new[] { "도면번호", "DRAWING NO", "DWG NO", "DRAWING NUMBER", "DWG NUMBER" }),
        ("축척", new[] { "축척", "도면축척", "SCALE" }),
        ("날짜", new[] { "날짜", "DATE", "작성일" }),
        ("공사명", new[] { "공사명", "사업명", "용역명", "프로젝트명", "PROJECT", "PROJECT NAME" }),
    };

    public static string Norm(string s)
    {
        var sb = new StringBuilder();
        foreach (char c in s ?? "") if (!char.IsWhiteSpace(c) && c != '.' && c != ':' && c != '_' && c != '-' && c != '：') sb.Append(char.ToUpperInvariant(c));
        return sb.ToString();
    }

    public static string? CellOfWord(string text)
    {
        string n = Norm(text);
        if (n.Length == 0) return null;
        foreach (var (cell, words) in Keywords) foreach (var w in words) if (Norm(w) == n) return cell;
        return null;
    }

    /// <summary>값처럼 보이는 글(숫자가 있거나 긴 글) — 이름표 같은 글(짧고 숫자 없음)과 가른다.</summary>
    public static bool LooksLikeValue(string text)
    {
        string n = Norm(text);
        return n.Any(char.IsDigit) || n.Length > 10;
    }

    /// <summary>★[계획 4.2] 사용자 도곽 검출 — 선분 · 글자 · 속성 정의(도곽 단위 그대로) → 정의(종이 mm). 비가 A 계열 가로(841:594 ±1 %)가 아니면 null + 까닭.</summary>
    public static FrameDef? DetectFrame(IReadOnlyList<Seg> segs0, IReadOnlyList<Txt> texts0, IReadOnlyList<AttDef> atts0, string name, out string error)
    {
        error = "";
        var segs = segs0.Where(s => double.IsFinite(s.X0) && double.IsFinite(s.Y0) && double.IsFinite(s.X1) && double.IsFinite(s.Y1)).ToList();
        if (segs.Count < 4) { error = "선이 없음"; return null; }
        double mnx = segs.Min(s => Math.Min(s.X0, s.X1)), mxx = segs.Max(s => Math.Max(s.X0, s.X1));
        double mny = segs.Min(s => Math.Min(s.Y0, s.Y1)), mxy = segs.Max(s => Math.Max(s.Y0, s.Y1));
        double W = mxx - mnx, H = mxy - mny;
        if (!(W > 0) || !(H > 0)) { error = "범위 없음"; return null; }
        double k = PaperW / W;
        // 바깥 = 가장 큰 축정렬 사각형 → 선분 범위(바깥 네 변이 없어도 범위)
        if (Math.Abs(W / H - PaperW / PaperH) > PaperW / PaperH * 0.01)
        {
            error = $"A 계열 가로 도곽만 — {W * k:0.#}×{H * k:0.#}(비 {W / H:0.###})";
            return null;
        }
        // 종이 mm로
        Seg P(Seg s) => new((s.X0 - mnx) * k, (s.Y0 - mny) * k, (s.X1 - mnx) * k, (s.Y1 - mny) * k);
        var ps = segs.Select(P).ToList();
        var tx = texts0.Select(t => new Txt(t.Text, (t.X0 - mnx) * k, (t.Y0 - mny) * k, (t.X1 - mnx) * k, (t.Y1 - mny) * k)).ToList();
        var hs = MergeLines(ps, horizontal: true, tol: 0.5);
        var vs = MergeLines(ps, horizontal: false, tol: 0.5);

        var def = new FrameDef { Name = name, Unit = "mm", K = k, Origin = new[] { mnx, mny }, Outer = new[] { 0, 0, PaperW, PaperH } };
        // 테두리 — 바깥 안쪽 각 변 0~60 mm 안의 긴 선(바깥 변 자신은 빼고 · 바깥에 가장 가까운 것)
        double tolE = 0.5;
        double? bl = vs.Where(l => l.C > tolE && l.C <= 60 && l.B - l.A >= 0.8 * PaperH * 0.8).OrderBy(l => l.C).Select(l => (double?)l.C).FirstOrDefault();
        double? br = vs.Where(l => l.C < PaperW - tolE && l.C >= PaperW - 60 && l.B - l.A >= 0.8 * PaperH * 0.8).OrderByDescending(l => l.C).Select(l => (double?)l.C).FirstOrDefault();
        double? bb = hs.Where(l => l.C > tolE && l.C <= 60 && l.B - l.A >= 0.8 * PaperW * 0.8).OrderBy(l => l.C).Select(l => (double?)l.C).FirstOrDefault();
        double? bt = hs.Where(l => l.C < PaperH - tolE && l.C >= PaperH - 60 && l.B - l.A >= 0.8 * PaperW * 0.8).OrderByDescending(l => l.C).Select(l => (double?)l.C).FirstOrDefault();
        def.Border = bl != null && br != null && bb != null && bt != null ? new[] { bl.Value, bb.Value, br.Value, bt.Value } : new double[] { 0, 0, PaperW, PaperH };
        double[] B = def.Border;
        double bw = B[2] - B[0], bh = B[3] - B[1];
        // 표제란 띠 — 아래 25 % 안의 가로선(테두리 폭 80 % 넘게) 중 가장 높은 것 · 없으면 오른쪽 30 % 안의 세로선(높이 80 % 넘게)
        var band = hs.Where(l => l.C > B[1] + tolE && l.C < B[1] + 0.25 * bh && l.A <= B[0] + 0.2 * bw + tolE && l.B - l.A >= 0.8 * bw).OrderByDescending(l => l.C).FirstOrDefault();
        double[] bandRect;
        if (band.B > band.A) { def.DrawArea = new[] { B[0], band.C, B[2], B[3] }; bandRect = new[] { B[0], B[1], B[2], band.C }; }
        else
        {
            var vb = vs.Where(l => l.C < B[2] - tolE && l.C > B[2] - 0.3 * bw && l.B - l.A >= 0.8 * bh).OrderBy(l => l.C).FirstOrDefault();
            if (vb.B > vb.A) { def.DrawArea = new[] { B[0], B[1], vb.C, B[3] }; bandRect = new[] { vb.C, B[1], B[2], B[3] }; }
            else { def.DrawArea = (double[])B.Clone(); bandRect = Array.Empty<double>(); }
        }
        // 5′ 속성 정의 — 선 격자보다 먼저
        foreach (var a in atts0)
        {
            string? c = CellOfWord(a.Tag) ?? CellOfWord(a.Prompt) ?? CellOfTag(a.Tag);
            if (c == null || def.Attrs.ContainsValue(c)) continue;
            def.Attrs[a.Tag] = c;
            if (!def.Found.Contains(c)) def.Found.Add(c);
        }
        // 5 칸 — 띠 안 이름표 글
        if (bandRect.Length == 4)
        {
            var inBand = tx.Where(t => t.Cx >= bandRect[0] - tolE && t.Cx <= bandRect[2] + tolE && t.Cy >= bandRect[1] - tolE && t.Cy <= bandRect[3] + tolE).ToList();
            // 이름표마다 값 칸 후보 — 같은 칸 이름이 여럿이면(개정 표 머리 «DATE» · 표제 «DATE») 값 칸에 이미 값(숫자)이 든 쪽이 이김 · 같으면 먼저 것
            var cand = new List<(string Cell, double[] Val, int Score, int Order)>();
            int order = 0;
            bool Free(double[]? r) => r != null && !inBand.Where(o => o.Cx > r[0] && o.Cx < r[2] && o.Cy > r[1] && o.Cy < r[3]).Any(o => !LooksLikeValue(o.Text));
            foreach (var t in inBand)
            {
                string? c = CellOfWord(t.Text);
                if (c == null || def.Attrs.ContainsValue(c)) continue;
                var cell = CellAt(t.Cx, t.Cy, hs, vs, bandRect);
                if (cell == null) continue;
                double[]? val = null;
                double cw = cell[2] - cell[0], ch = cell[3] - cell[1];
                bool small = t.X1 - t.X0 < 0.4 * cw;   // 이름표가 칸의 40 % 미만 — 칸 안에 값 자리가 따로 있음
                double dx = t.Cx - (cell[0] + cell[2]) / 2, dy = t.Cy - (cell[1] + cell[3]) / 2;
                if (small && dy > 0.25 * ch) val = new[] { cell[0], cell[1], cell[2], Math.Max(cell[1] + 1, t.Y0 - 0.5) };   // ② «TITLE» 머리글(위) — 그 아래
                else if (small && dx < -0.2 * cw) val = new[] { Math.Min(cell[2] - 1, t.X1 + 1.0), cell[1], cell[2], cell[3] }; // ② 이름표가 왼쪽 — 그 오른쪽
                else if (small && Math.Abs(dx) > 0.2 * cw) val = cell;                                                        //    그 밖 구석 — 같은 칸
                else
                {
                    var right = CellAt(cell[2] + 0.6, t.Cy, hs, vs, bandRect);
                    var up = CellAt(t.Cx, cell[3] + 0.6, hs, vs, bandRect);
                    var down = CellAt(t.Cx, cell[1] - 0.6, hs, vs, bandRect);
                    if (Free(right)) val = right;                                // ① 오른쪽 이웃이 비었거나 값만
                    else if (Free(up)) val = up;                                 // ③ 위
                    else if (Free(down)) val = down;                             //    → 아래
                }
                if (val == null) continue;
                int score = inBand.Any(o => o.Cx > val[0] && o.Cx < val[2] && o.Cy > val[1] && o.Cy < val[3] && LooksLikeValue(o.Text) && o.Text.Any(char.IsDigit)) ? 2 : 1;
                cand.Add((c, val, score, order++));
            }
            foreach (var g in cand.GroupBy(x => x.Cell))
            {
                var best = g.OrderByDescending(x => x.Score).ThenBy(x => x.Order).First();
                var val = best.Val;
                double hgt = Math.Max(1.8, Math.Min(4.0, (val[3] - val[1]) * 0.5));
                def.Cells[g.Key] = new Cell { Rect = val.Select(v => Math.Round(v, 3)).ToArray(), H = Math.Round(hgt, 2), Clear = "ifValue", Fmt = g.Key == "날짜" ? DateFmt : null };
                if (!def.Found.Contains(g.Key)) def.Found.Add(g.Key);
            }
        }
        var miss = CellNames.Where(c => !def.Found.Contains(c)).ToList();
        if (miss.Count > 0) def.Notes.Add("칸 못 찾음: " + string.Join(" · ", miss));
        if (def.DrawAreaSmall) def.Notes.Add($"그림 자리 {def.DrawArea[2] - def.DrawArea[0]:0}×{def.DrawArea[3] - def.DrawArea[1]:0} — 모형 장 가장자리 잘림");
        def.Border = def.Border.Select(v => Math.Round(v, 3)).ToArray();
        def.DrawArea = def.DrawArea.Select(v => Math.Round(v, 3)).ToArray();
        return def;
    }

    static string? CellOfTag(string tag) => Norm(tag) switch
    {
        "DWGTITLE" or "DRAWINGTITLE" or "TITLE" => "도면명",
        "DWGNO" or "DRAWINGNO" or "DWGNUMBER" or "DRAWINGNUMBER" or "SHEETNO" => "도면번호",
        "SCALE" => "축척",
        "DATE" => "날짜",
        "PROJECT" or "PROJECTNAME" => "공사명",
        _ => null,
    };

    /// <summary>합친 선 — 가로면 C = y · [A, B] = x 범위 · 세로면 C = x · [A, B] = y 범위.</summary>
    public readonly record struct Line1(double C, double A, double B);

    /// <summary>같은 선 위에 이어진(틈 ≤ tol) 조각을 합친다 — 가로/세로만(기운 선은 버림). 선 넷으로 그린 사각형 · 조각 난 선도 하나로.</summary>
    public static List<Line1> MergeLines(IEnumerable<Seg> segs, bool horizontal, double tol)
    {
        var raw = new List<Line1>();
        foreach (var s in segs)
        {
            double dx = Math.Abs(s.X1 - s.X0), dy = Math.Abs(s.Y1 - s.Y0);
            if (horizontal && dy <= tol * 0.2 && dx > tol) raw.Add(new((s.Y0 + s.Y1) / 2, Math.Min(s.X0, s.X1), Math.Max(s.X0, s.X1)));
            if (!horizontal && dx <= tol * 0.2 && dy > tol) raw.Add(new((s.X0 + s.X1) / 2, Math.Min(s.Y0, s.Y1), Math.Max(s.Y0, s.Y1)));
        }
        var outL = new List<Line1>();
        foreach (var grp in GroupBy(raw.OrderBy(r => r.C).ToList(), tol * 0.4))
        {
            double c = grp.Average(r => r.C);
            double a = double.NaN, b = double.NaN;
            foreach (var r in grp.OrderBy(r => r.A))
            {
                if (double.IsNaN(a)) { a = r.A; b = r.B; continue; }
                if (r.A <= b + tol) b = Math.Max(b, r.B);
                else { outL.Add(new(c, a, b)); a = r.A; b = r.B; }
            }
            if (!double.IsNaN(a)) outL.Add(new(c, a, b));
        }
        return outL;
    }

    static IEnumerable<List<Line1>> GroupBy(List<Line1> sorted, double tol)
    {
        var cur = new List<Line1>();
        foreach (var r in sorted)
        {
            if (cur.Count > 0 && r.C - cur[^1].C > tol) { yield return cur; cur = new List<Line1>(); }
            cur.Add(r);
        }
        if (cur.Count > 0) yield return cur;
    }

    /// <summary>점을 둘러싼 칸 — 네 방향으로 그 자리를 덮는 가장 가까운 선(띠 사각형 변도 선으로 봄). 못 찾으면 null.</summary>
    public static double[]? CellAt(double x, double y, List<Line1> hs, List<Line1> vs, double[] box)
    {
        if (x < box[0] - 1e-6 || x > box[2] + 1e-6 || y < box[1] - 1e-6 || y > box[3] + 1e-6) return null;
        const double e = 0.3;
        double left = vs.Where(l => l.C < x - 1e-6 && l.A <= y + e && l.B >= y - e).Select(l => l.C).DefaultIfEmpty(box[0]).Max();
        double right = vs.Where(l => l.C > x + 1e-6 && l.A <= y + e && l.B >= y - e).Select(l => l.C).DefaultIfEmpty(box[2]).Min();
        double bot = hs.Where(l => l.C < y - 1e-6 && l.A <= x + e && l.B >= x - e).Select(l => l.C).DefaultIfEmpty(box[1]).Max();
        double top = hs.Where(l => l.C > y + 1e-6 && l.A <= x + e && l.B >= x - e).Select(l => l.C).DefaultIfEmpty(box[3]).Min();
        left = Math.Max(left, box[0]); right = Math.Min(right, box[2]); bot = Math.Max(bot, box[1]); top = Math.Min(top, box[3]);
        if (right - left < 0.5 || top - bot < 0.5) return null;
        return new[] { left, bot, right, top };
    }
}
