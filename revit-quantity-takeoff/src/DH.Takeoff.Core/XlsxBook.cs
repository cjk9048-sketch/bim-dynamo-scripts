using System.Globalization;
using System.IO.Compression;
using System.Security;
using System.Text;

namespace DH.Takeoff.Core;

/// <summary>칸의 모양(옛 이름) — 「부재 목록」 · 「미산출 부재」 같은 우리 시트가 쓴다. 쓸 때 <see cref="XlsxCellStyle"/> 로 옮겨 적는다.</summary>
public enum XlsxStyle
{
    Normal,      // 기본
    Bold,        // 굵게
    Title,       // 굵고 큰 글자(시트 제목)
    Number2,     // 0.00 (물량)
    BoldNumber2, // 굵게 0.00 (계)
    Count,       // 0"개소"
    Center,      // 가운데(기호 칸)
    Gray,        // 흐린 글자(안내·비고)
    Percent,     // 0.00%
    BoldCenter,  // 굵게 가운데(표 머리)
}

/// <summary>가로 맞춤. CenterContinuous = 옆의 빈 칸들까지 넘겨 가운데(2D 산출서의 «=» · 값 칸).</summary>
public enum XlsxAlign { General, Left, Center, Right, CenterContinuous }

/// <summary>세로 맞춤.</summary>
public enum XlsxVAlign { Bottom, Center, Top }

/// <summary>
/// 칸 모양 하나(값 — 같은 값이면 파일에서 같은 번호를 쓴다): 글꼴 · 테두리(가는 선) · 맞춤 · 숫자 서식 · 채움색.
/// R49 — JACK 2026-10-16 «첨부한 수량산출서랑 시트모양과 색상 글씨체등 양식을 아예 똑같이»: 2D 의 칸 모양을 그대로 옮겨 적으려고 둔다.
/// </summary>
public sealed record XlsxCellStyle
{
    public string Font { get; init; } = XlsxBook.LegacyFont;
    public double Size { get; init; } = 10;
    public bool Bold { get; init; }
    public bool Underline { get; init; }
    /// <summary>글자색 ARGB(예 «FF7F7F7F») — "" = 자동.</summary>
    public string Color { get; init; } = "";
    public bool BorderLeft { get; init; }
    public bool BorderRight { get; init; }
    public bool BorderTop { get; init; }
    public bool BorderBottom { get; init; }
    public XlsxAlign Align { get; init; }
    public XlsxVAlign VAlign { get; init; }
    public bool Wrap { get; init; }
    /// <summary>칸에 맞게 글자 줄이기(shrinkToFit — 숫자 칸: 큰 수가 «###» 로 안 보이지 않게 · revit-verify R49 계획 M2).</summary>
    public bool Shrink { get; init; }
    /// <summary>숫자 서식(엑셀의 서식 글 — 예 «#,##0.00"㎥"») — "" = 일반.</summary>
    public string Format { get; init; } = "";
    /// <summary>채움색 ARGB — "" = 없음.</summary>
    public string Fill { get; init; } = "";

    /// <summary>옛 이름의 모양(맑은 고딕 10 — R48 까지의 모양 그대로).</summary>
    public static XlsxCellStyle Of(XlsxStyle style) => Legacy[(int)style];

    private static readonly XlsxCellStyle[] Legacy =
    {
        new(),                                                                   // Normal
        new() { Bold = true },                                                   // Bold
        new() { Bold = true, Size = 13 },                                        // Title
        new() { Format = "#,##0.00" },                                           // Number2
        new() { Bold = true, Format = "#,##0.00" },                              // BoldNumber2
        new() { Format = "0\"개소\"" },                                          // Count
        new() { Align = XlsxAlign.Center },                                      // Center
        new() { Size = 9, Color = "FF7F7F7F" },                                  // Gray
        new() { Format = "0.00%" },                                              // Percent
        new() { Bold = true, Align = XlsxAlign.Center },                         // BoldCenter
    };

    /// <summary>테두리 네 변을 다 두른 것.</summary>
    public XlsxCellStyle Boxed() => this with { BorderLeft = true, BorderRight = true, BorderTop = true, BorderBottom = true };
}

/// <summary>인쇄 설정(2D 산출서와 같게 — A4 · 여백 · 배율 · 가로 가운데 · 머리글).</summary>
public sealed class XlsxPage
{
    public bool Landscape { get; set; }
    /// <summary>배율(%) — null = 100.</summary>
    public int? Scale { get; set; }
    /// <summary>여백(인치) 왼 · 오 · 위 · 아래 · 머리글 · 바닥글.</summary>
    public (double left, double right, double top, double bottom, double header, double footer) Margins { get; set; } = (0.7, 0.7, 0.75, 0.75, 0.3, 0.3);
    public bool CenterHorizontally { get; set; }
    /// <summary>머리글(엑셀 머리글 글 — «&amp;C&amp;"돋움,굵게"&amp;16…»). "" = 없음.</summary>
    public string Header { get; set; } = "";
}

/// <summary>
/// 엑셀 파일(.xlsx)을 다른 라이브러리 없이 쓴다 — 글자 · 숫자 · 수식, 칸 모양(글꼴 · 테두리 · 맞춤 · 숫자 서식 · 채움), 병합, 행 높이 · 열 너비,
/// 틀 고정 · 보기 · 인쇄 설정 · 인쇄 영역/제목 · 손 페이지 나눔까지.
/// (Revit 안에서 돌기 때문에 엑셀 라이브러리 DLL 을 따로 싣지 않으려는 것이다.)
/// 행·열 번호는 1부터.
/// </summary>
public sealed class XlsxBook
{
    /// <summary>R48 까지의 글꼴(우리 시트).</summary>
    public const string LegacyFont = "맑은 고딕";

    public List<XlsxSheet> Sheets { get; } = new();

    /// <summary>
    /// 책의 기본 모양(엑셀의 «표준» — 열 너비의 글자 단위가 이 글꼴로 정해진다). 2D 산출서는 굴림체 9 —
    /// 열 너비를 2D 와 같은 숫자로 적어도 같은 폭으로 보이려면 이것도 같아야 한다.
    /// </summary>
    public XlsxCellStyle BaseStyle { get; set; } = new();

    public XlsxSheet Add(string name)
    {
        var sheet = new XlsxSheet(SafeName(name, Sheets.Select(s => s.Name)));
        Sheets.Add(sheet);
        return sheet;
    }

    // 시트 이름: 31자 이내, : \ / ? * [ ] 금지, 겹치면 번호를 붙인다.
    private static string SafeName(string name, IEnumerable<string> taken)
    {
        var sb = new StringBuilder();
        foreach (char c in name) sb.Append(":\\/?*[]".Contains(c) ? '_' : c);
        string s = sb.ToString().Trim('\'');
        if (s.Trim().Length == 0) s = "Sheet";
        if (s.Length > 31) s = s[..31];
        var used = new HashSet<string>(taken, StringComparer.OrdinalIgnoreCase);
        string result = s;
        for (int i = 2; used.Contains(result); i++)
        {
            string tail = $"({i})";
            result = (s.Length + tail.Length > 31 ? s[..(31 - tail.Length)] : s) + tail;
        }
        return result;
    }

    public void Save(string path)
    {
        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
        Save(fs);
    }

    public void Save(Stream stream)
    {
        if (Sheets.Count == 0) throw new InvalidOperationException("시트가 없습니다.");
        var styles = new StyleTable(BaseStyle);
        foreach (var sheet in Sheets) sheet.CollectStyles(styles);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true);
        Put(zip, "[Content_Types].xml", ContentTypes());
        Put(zip, "_rels/.rels",
            Head + "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
            "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/>" +
            "</Relationships>");
        Put(zip, "xl/workbook.xml", Workbook());
        Put(zip, "xl/_rels/workbook.xml.rels", WorkbookRels());
        Put(zip, "xl/styles.xml", styles.ToXml());
        int selected = Sheets.FindIndex(s => s.Selected);
        if (selected < 0) selected = 0;
        for (int i = 0; i < Sheets.Count; i++)
            Put(zip, $"xl/worksheets/sheet{i + 1}.xml", Sheets[i].ToXml(i == selected, styles));
    }

    private const string Head = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n";
    private const string Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    private static void Put(ZipArchive zip, string name, string xml)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        using var w = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        w.Write(xml);
    }

    private string ContentTypes()
    {
        var sb = new StringBuilder(Head);
        sb.Append("<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">");
        sb.Append("<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>");
        sb.Append("<Default Extension=\"xml\" ContentType=\"application/xml\"/>");
        sb.Append("<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>");
        sb.Append("<Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>");
        for (int i = 0; i < Sheets.Count; i++)
            sb.Append($"<Override PartName=\"/xl/worksheets/sheet{i + 1}.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>");
        sb.Append("</Types>");
        return sb.ToString();
    }

    private string Workbook()
    {
        var sb = new StringBuilder(Head);
        sb.Append($"<workbook xmlns=\"{Main}\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">");
        int selected = Math.Max(0, Sheets.FindIndex(s => s.Selected));
        sb.Append($"<bookViews><workbookView xWindow=\"0\" yWindow=\"0\" windowWidth=\"28800\" windowHeight=\"15000\" activeTab=\"{selected}\"/></bookViews>");
        sb.Append("<sheets>");
        for (int i = 0; i < Sheets.Count; i++)
            sb.Append($"<sheet name=\"{Esc(Sheets[i].Name)}\" sheetId=\"{i + 1}\" r:id=\"rId{i + 1}\"/>");
        sb.Append("</sheets>");
        // 인쇄 영역 · 인쇄 제목(정의된 이름 — 시트마다). 차례: 이름 차례(엑셀이 쓰는 차례 — 다르면 «복구»를 묻는 일이 있다).
        var names = new List<(string name, int sheet, string value)>();
        for (int i = 0; i < Sheets.Count; i++)
        {
            var s = Sheets[i];
            string quoted = "'" + s.Name.Replace("'", "''") + "'!";
            if (s.PrintArea is { } area)
                names.Add(("_xlnm.Print_Area", i, quoted + Abs(area.r1, area.c1) + ":" + Abs(area.r2, area.c2)));
            if (s.PrintTitleRows is { } rows)
                names.Add(("_xlnm.Print_Titles", i, quoted + "$" + rows.from.ToString(CultureInfo.InvariantCulture) + ":$" + rows.to.ToString(CultureInfo.InvariantCulture)));
        }
        if (names.Count > 0)
        {
            sb.Append("<definedNames>");
            foreach (var (name, sheet, value) in names.OrderBy(n => n.name, StringComparer.Ordinal).ThenBy(n => n.sheet))
                sb.Append($"<definedName name=\"{name}\" localSheetId=\"{sheet}\">{Esc(value)}</definedName>");
            sb.Append("</definedNames>");
        }
        // 수식의 값은 파일에도 적어 두지만(미리보기용), 엑셀이 열 때 전부 다시 계산하게 한다.
        sb.Append("<calcPr calcId=\"191029\" fullCalcOnLoad=\"1\"/>");
        sb.Append("</workbook>");
        return sb.ToString();
    }

    private static string Abs(int row, int col) => "$" + XlsxSheet.ColumnName(col) + "$" + row.ToString(CultureInfo.InvariantCulture);

    private string WorkbookRels()
    {
        var sb = new StringBuilder(Head);
        sb.Append("<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">");
        for (int i = 0; i < Sheets.Count; i++)
            sb.Append($"<Relationship Id=\"rId{i + 1}\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet{i + 1}.xml\"/>");
        sb.Append($"<Relationship Id=\"rId{Sheets.Count + 1}\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/>");
        sb.Append("</Relationships>");
        return sb.ToString();
    }

    internal static string Esc(string s) => SecurityElement.Escape(Clean(s)) ?? "";

    // XML 에 넣을 수 없는 글자(제어 문자, U+FFFE·U+FFFF)를 뺀다 — 하나라도 들어가면 엑셀이 파일을 열지 못한다.
    private static string Clean(string s)
    {
        if (s.All(Allowed)) return s;
        return new string(s.Where(Allowed).ToArray());
    }

    private static bool Allowed(char c) => (c >= ' ' || c == '\t' || c == '\n' || c == '\r') && c != '￾' && c != '￿';

    /// <summary>
    /// 책이 쓴 칸 모양을 모아 styles.xml 을 짓는다: 같은 글꼴 · 테두리 · 채움 · 숫자 서식은 하나로. cellXfs[0] = 책의 기본 모양(표준).
    /// </summary>
    internal sealed class StyleTable
    {
        private readonly XlsxCellStyle _base;
        private readonly Dictionary<XlsxCellStyle, int> _xf = new();
        private readonly List<XlsxCellStyle> _order = new();
        private readonly Dictionary<(string, double, bool, bool, string), int> _fonts = new();
        private readonly Dictionary<(bool, bool, bool, bool), int> _borders = new();
        private readonly Dictionary<string, int> _fills = new(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _formats = new(StringComparer.Ordinal);

        // 엑셀이 번호를 정해 둔 숫자 서식(이 글이면 새로 적지 않는다).
        private static readonly Dictionary<string, int> Builtin = new(StringComparer.Ordinal)
        {
            [""] = 0, ["General"] = 0, ["0"] = 1, ["0.00"] = 2, ["#,##0"] = 3, ["#,##0.00"] = 4, ["0%"] = 9, ["0.00%"] = 10,
        };

        public StyleTable(XlsxCellStyle baseStyle)
        {
            _base = baseStyle;
            _borders[(false, false, false, false)] = 0;   // 테두리 0 = 없음(엑셀 기본)
            Index(baseStyle);                             // cellXfs 0 = 책의 기본 모양 · 글꼴 0 = 그 글꼴
        }

        /// <summary>그 모양의 번호(cellXfs 의 차례) — 처음 보는 모양이면 보탠다.</summary>
        public int Index(XlsxCellStyle style)
        {
            if (_xf.TryGetValue(style, out int at)) return at;
            at = _order.Count;
            _xf[style] = at;
            _order.Add(style);
            FontOf(style);
            BorderOf(style);
            FillOf(style);
            FormatOf(style);
            return at;
        }

        private int FontOf(XlsxCellStyle s)
        {
            var key = (s.Font, s.Size, s.Bold, s.Underline, s.Color);
            if (!_fonts.TryGetValue(key, out int at)) _fonts[key] = at = _fonts.Count;
            return at;
        }

        private int BorderOf(XlsxCellStyle s)
        {
            var key = (s.BorderLeft, s.BorderRight, s.BorderTop, s.BorderBottom);
            if (!_borders.TryGetValue(key, out int at)) _borders[key] = at = _borders.Count;
            return at;
        }

        private int FillOf(XlsxCellStyle s)
        {
            if (s.Fill.Length == 0) return 0;
            if (!_fills.TryGetValue(s.Fill, out int at)) _fills[s.Fill] = at = _fills.Count + 2;   // 0 = 없음 · 1 = gray125(엑셀이 늘 요구)
            return at;
        }

        private int FormatOf(XlsxCellStyle s)
        {
            if (Builtin.TryGetValue(s.Format, out int id)) return id;
            if (!_formats.TryGetValue(s.Format, out int at)) _formats[s.Format] = at = 164 + _formats.Count;
            return at;
        }

        public string ToXml()
        {
            var borders = new SortedDictionary<int, (bool l, bool r, bool t, bool b)>();
            foreach (var (key, at) in _borders) borders[at] = key;
            var sb = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n");
            sb.Append("<styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">");
            if (_formats.Count > 0)
            {
                sb.Append(CultureInfo.InvariantCulture, $"<numFmts count=\"{_formats.Count}\">");
                foreach (var (code, at) in _formats.OrderBy(f => f.Value))
                    sb.Append(CultureInfo.InvariantCulture, $"<numFmt numFmtId=\"{at}\" formatCode=\"{Esc(code).Replace("\"", "&quot;")}\"/>");
                sb.Append("</numFmts>");
            }
            sb.Append(CultureInfo.InvariantCulture, $"<fonts count=\"{_fonts.Count}\">");
            foreach (var ((name, size, bold, underline, color), _) in _fonts.OrderBy(f => f.Value))
            {
                sb.Append("<font>");
                if (bold) sb.Append("<b/>");
                if (underline) sb.Append("<u/>");
                sb.Append(CultureInfo.InvariantCulture, $"<sz val=\"{size:0.##}\"/>");
                if (color.Length > 0) sb.Append($"<color rgb=\"{Esc(color)}\"/>");
                sb.Append($"<name val=\"{Esc(name)}\"/><family val=\"3\"/><charset val=\"129\"/></font>");
            }
            sb.Append("</fonts>");
            sb.Append(CultureInfo.InvariantCulture, $"<fills count=\"{_fills.Count + 2}\"><fill><patternFill patternType=\"none\"/></fill><fill><patternFill patternType=\"gray125\"/></fill>");
            foreach (var (rgb, _) in _fills.OrderBy(f => f.Value))
                sb.Append($"<fill><patternFill patternType=\"solid\"><fgColor rgb=\"{Esc(rgb)}\"/><bgColor indexed=\"64\"/></patternFill></fill>");
            sb.Append("</fills>");
            sb.Append(CultureInfo.InvariantCulture, $"<borders count=\"{borders.Count}\">");
            foreach (var (_, (l, r, t, b)) in borders)
                sb.Append("<border>").Append(Side("left", l)).Append(Side("right", r)).Append(Side("top", t)).Append(Side("bottom", b)).Append("<diagonal/></border>");
            sb.Append("</borders>");
            sb.Append(CultureInfo.InvariantCulture, $"<cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"{FontOf(_base)}\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs>");
            sb.Append(CultureInfo.InvariantCulture, $"<cellXfs count=\"{_order.Count}\">");
            foreach (var s in _order)
            {
                int font = FontOf(s), border = BorderOf(s), fill = FillOf(s), format = FormatOf(s);
                sb.Append(CultureInfo.InvariantCulture, $"<xf numFmtId=\"{format}\" fontId=\"{font}\" fillId=\"{fill}\" borderId=\"{border}\" xfId=\"0\"");
                if (format != 0) sb.Append(" applyNumberFormat=\"1\"");
                if (font != FontOf(_base)) sb.Append(" applyFont=\"1\"");
                if (fill != 0) sb.Append(" applyFill=\"1\"");
                if (border != 0) sb.Append(" applyBorder=\"1\"");
                string align = Alignment(s);
                if (align.Length == 0) { sb.Append("/>"); continue; }
                sb.Append(" applyAlignment=\"1\">").Append(align).Append("</xf>");
            }
            sb.Append("</cellXfs>");
            sb.Append("<cellStyles count=\"1\"><cellStyle name=\"Normal\" xfId=\"0\" builtinId=\"0\"/></cellStyles>");
            sb.Append("</styleSheet>");
            return sb.ToString();
        }

        private static string Side(string name, bool thin) => thin ? $"<{name} style=\"thin\"><color indexed=\"64\"/></{name}>" : $"<{name}/>";

        private static string Alignment(XlsxCellStyle s)
        {
            var sb = new StringBuilder();
            string h = s.Align switch
            {
                XlsxAlign.Left => "left", XlsxAlign.Center => "center", XlsxAlign.Right => "right", XlsxAlign.CenterContinuous => "centerContinuous", _ => "",
            };
            string v = s.VAlign switch { XlsxVAlign.Center => "center", XlsxVAlign.Top => "top", _ => "" };
            if (h.Length == 0 && v.Length == 0 && !s.Wrap && !s.Shrink) return "";
            sb.Append("<alignment");
            if (h.Length > 0) sb.Append($" horizontal=\"{h}\"");
            if (v.Length > 0) sb.Append($" vertical=\"{v}\"");
            if (s.Wrap) sb.Append(" wrapText=\"1\"");
            else if (s.Shrink) sb.Append(" shrinkToFit=\"1\"");
            sb.Append("/>");
            return sb.ToString();
        }
    }
}

/// <summary>시트 하나. 칸은 아무 차례로나 넣어도 되고, 저장할 때 행·열 차례로 적는다.</summary>
public sealed class XlsxSheet
{
    private enum Kind { Text, Number, Formula, Blank }

    private readonly record struct Cell(Kind Kind, string Text, double Value, bool HasValue, XlsxCellStyle? Style);

    private readonly SortedDictionary<int, SortedDictionary<int, Cell>> _rows = new();
    private readonly SortedDictionary<int, double> _widths = new();
    private readonly SortedDictionary<int, XlsxCellStyle> _columnStyles = new();
    private readonly SortedDictionary<int, double> _heights = new();
    private readonly List<(int r1, int c1, int r2, int c2)> _merges = new();
    private readonly SortedSet<int> _breaks = new();

    public string Name { get; }

    internal XlsxSheet(string name) { Name = name; }

    // ── 시트 설정(R49 — 2D 산출서와 같은 모양) ──

    /// <summary>열어 볼 때 고른 시트.</summary>
    public bool Selected { get; set; }
    /// <summary>탭 색 ARGB("" = 없음).</summary>
    public string TabColor { get; set; } = "";
    /// <summary>0 을 보인다(false = 2D 수량집계표처럼 0 숨김).</summary>
    public bool ShowZeros { get; set; } = true;

    /// <summary>개요(그룹) 기호를 보인다 — 2D 「구체공산출근거」는 숨김(showOutlineSymbols=0).</summary>
    public bool ShowOutlineSymbols { get; set; } = true;
    /// <summary>보기: 페이지 나누기 미리 보기.</summary>
    public bool PageBreakPreview { get; set; }
    /// <summary>틀 고정(이 칸의 왼쪽 열 · 위쪽 행이 고정된다 — 예 (4, 3) = C4). null = 없음.</summary>
    public (int row, int col)? Freeze { get; set; }
    /// <summary>기본 행 높이(pt) · 기본 열 너비(글자) — null = 엑셀 기본.</summary>
    public double? DefaultRowHeight { get; set; }
    public double? DefaultColWidth { get; set; }
    /// <summary>인쇄 설정 — null = 엑셀 기본.</summary>
    public XlsxPage? Page { get; set; }
    /// <summary>인쇄 영역(행1, 열1, 행2, 열2) — null = 없음.</summary>
    public (int r1, int c1, int r2, int c2)? PrintArea { get; set; }
    /// <summary>쪽마다 되풀이할 행(인쇄 제목) — null = 없음.</summary>
    public (int from, int to)? PrintTitleRows { get; set; }

    /// <summary>열 너비(글자 수 기준).</summary>
    public void Width(int col, double width) => _widths[col] = width;

    /// <summary>열의 기본 모양(빈 칸도 이 모양 — 2D 의 세로 선).</summary>
    public void ColumnStyle(int col, XlsxCellStyle style) => _columnStyles[col] = style;

    /// <summary>행 높이(pt).</summary>
    public void Height(int row, double height) => _heights[row] = height;

    /// <summary>칸을 합친다(왼쪽 위 칸의 값만 보인다).</summary>
    public void Merge(int r1, int c1, int r2, int c2)
    {
        if (r2 < r1 || c2 < c1 || (r1 == r2 && c1 == c2)) return;
        _merges.Add((r1, c1, r2, c2));
    }

    /// <summary>이 행 뒤에서 쪽을 나눈다(손 페이지 나눔).</summary>
    public void BreakAfter(int row) => _breaks.Add(row);

    /// <summary>엑셀의 한 칸에 들어가는 글자 수 한도. 넘는 글자가 있으면 엑셀이 파일을 열지 못한다.</summary>
    public const int MaxText = 32767;

    public void Text(int row, int col, string text, XlsxStyle style = XlsxStyle.Normal) => Text(row, col, text, XlsxCellStyle.Of(style));

    public void Text(int row, int col, string text, XlsxCellStyle style) =>
        Put(row, col, new Cell(Kind.Text, Fit(text ?? ""), 0, false, style));

    // 한도를 넘는 글자는 잘라 적는다(짝을 이루는 글자의 가운데에서 자르지 않는다).
    private static string Fit(string text)
    {
        if (text.Length <= MaxText) return text;
        int cut = MaxText - 1;                                   // 끝에 '…' 한 글자를 붙일 자리
        if (char.IsHighSurrogate(text[cut - 1])) cut--;
        return text[..cut] + "…";
    }

    public void Number(int row, int col, double value, XlsxStyle style = XlsxStyle.Normal) => Number(row, col, value, XlsxCellStyle.Of(style));

    public void Number(int row, int col, double value, XlsxCellStyle style) =>
        Put(row, col, new Cell(Kind.Number, "", value, true, style));

    /// <summary>수식 칸. formula 는 '=' 없이. cached = 미리 계산해 둔 값(엑셀이 열 때 다시 계산한다).</summary>
    public void Formula(int row, int col, string formula, double? cached = null, XlsxStyle style = XlsxStyle.Normal) =>
        Formula(row, col, formula, cached, XlsxCellStyle.Of(style));

    public void Formula(int row, int col, string formula, double? cached, XlsxCellStyle style) =>
        Put(row, col, new Cell(Kind.Formula, formula.TrimStart('='), cached ?? 0, cached != null, style));

    /// <summary>
    /// 칸의 모양만 정한다(값은 그대로 · 빈 칸이면 모양만 있는 빈 칸). 이미 값이 든 칸은 모양만 갈아 끼운다.
    /// </summary>
    public void Style(int row, int col, XlsxCellStyle style)
    {
        if (_rows.TryGetValue(row, out var r) && r.TryGetValue(col, out var c)) { r[col] = c with { Style = style }; return; }
        Put(row, col, new Cell(Kind.Blank, "", 0, false, style));
    }

    /// <summary>그 칸의 모양(값이 든 칸 · 모양만 있는 칸 — 없으면 null).</summary>
    public XlsxCellStyle? StyleAt(int row, int col) =>
        _rows.TryGetValue(row, out var r) && r.TryGetValue(col, out var c) ? c.Style : null;

    /// <summary>그 칸에 값(글 · 숫자 · 수식)이 있는가.</summary>
    public bool HasValue(int row, int col) => _rows.TryGetValue(row, out var r) && r.TryGetValue(col, out var c) && c.Kind != Kind.Blank;

    private void Put(int row, int col, Cell cell)
    {
        if (row < 1 || col < 1 || col > 16384 || row > 1048576) throw new ArgumentOutOfRangeException(nameof(row), $"칸 위치가 범위를 벗어남: {row},{col}");
        if (!_rows.TryGetValue(row, out var r)) _rows[row] = r = new SortedDictionary<int, Cell>();
        r[col] = cell;
    }

    /// <summary>쓴 행 가운데 가장 큰 번호(없으면 0).</summary>
    public int LastRow => _rows.Count == 0 ? 0 : _rows.Keys.Max();

    /// <summary>칸 이름. (7, 8) → "H7"</summary>
    public static string Ref(int row, int col) => ColumnName(col) + row.ToString(CultureInfo.InvariantCulture);

    public static string ColumnName(int col)
    {
        string s = "";
        for (int c = col; c > 0; c = (c - 1) / 26) s = (char)('A' + (c - 1) % 26) + s;
        return s;
    }

    /// <summary>다른 시트에서 이 시트의 칸을 가리키는 글자. 예) '산출근거'!Z15</summary>
    public string FullRef(int row, int col) => $"'{Name.Replace("'", "''")}'!{Ref(row, col)}";

    internal void CollectStyles(XlsxBook.StyleTable styles)
    {
        foreach (var s in _columnStyles.Values) styles.Index(s);
        foreach (var row in _rows.Values)
            foreach (var c in row.Values)
                if (c.Style != null) styles.Index(c.Style);
    }

    internal string ToXml(bool selected, XlsxBook.StyleTable styles)
    {
        var inv = CultureInfo.InvariantCulture;
        var sb = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n");
        sb.Append("<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">");
        if (TabColor.Length > 0) sb.Append($"<sheetPr><tabColor rgb=\"{XlsxBook.Esc(TabColor)}\"/></sheetPr>");
        sb.Append("<sheetViews><sheetView");
        if (!ShowZeros) sb.Append(" showZeros=\"0\"");
        if (!ShowOutlineSymbols) sb.Append(" showOutlineSymbols=\"0\"");
        if (selected) sb.Append(" tabSelected=\"1\"");
        if (PageBreakPreview) sb.Append(" view=\"pageBreakPreview\" zoomScaleNormal=\"100\" zoomScaleSheetLayoutView=\"100\"");
        sb.Append(" workbookViewId=\"0\"");
        if (Freeze is { } f && (f.row > 1 || f.col > 1))
        {
            int xs = f.col - 1, ys = f.row - 1;
            string pane = xs > 0 && ys > 0 ? "bottomRight" : xs > 0 ? "topRight" : "bottomLeft";
            sb.Append("><pane");
            if (xs > 0) sb.Append(inv, $" xSplit=\"{xs}\"");
            if (ys > 0) sb.Append(inv, $" ySplit=\"{ys}\"");
            sb.Append($" topLeftCell=\"{Ref(f.row, f.col)}\" activePane=\"{pane}\" state=\"frozen\"/>");
            sb.Append($"<selection pane=\"{pane}\" activeCell=\"{Ref(f.row, f.col)}\" sqref=\"{Ref(f.row, f.col)}\"/>");
            sb.Append("</sheetView></sheetViews>");
        }
        else sb.Append("/></sheetViews>");
        sb.Append("<sheetFormatPr");
        if (DefaultColWidth is { } dcw) sb.Append(inv, $" defaultColWidth=\"{dcw:0.########}\"");
        sb.Append(inv, $" defaultRowHeight=\"{DefaultRowHeight ?? 16.5:0.##}\"");
        if (DefaultRowHeight != null) sb.Append(" customHeight=\"1\"");
        sb.Append("/>");
        var columns = _widths.Keys.Union(_columnStyles.Keys).OrderBy(c => c).ToList();
        if (columns.Count > 0)
        {
            sb.Append("<cols>");
            foreach (int col in columns)
            {
                sb.Append(inv, $"<col min=\"{col}\" max=\"{col}\"");
                double width = _widths.TryGetValue(col, out double w) ? w : DefaultColWidth ?? 8.43;
                sb.Append(inv, $" width=\"{width:0.########}\"");
                if (_columnStyles.TryGetValue(col, out var cs)) sb.Append(inv, $" style=\"{styles.Index(cs)}\"");
                if (_widths.ContainsKey(col)) sb.Append(" customWidth=\"1\"");
                sb.Append("/>");
            }
            sb.Append("</cols>");
        }
        sb.Append("<sheetData>");
        var rowNumbers = _rows.Keys.Union(_heights.Keys).OrderBy(r => r);
        foreach (int row in rowNumbers)
        {
            sb.Append(inv, $"<row r=\"{row}\"");
            if (_heights.TryGetValue(row, out double h)) sb.Append(inv, $" ht=\"{h:0.##}\" customHeight=\"1\"");
            if (!_rows.TryGetValue(row, out var cells) || cells.Count == 0) { sb.Append("/>"); continue; }
            sb.Append('>');
            foreach (var (col, c) in cells)
            {
                string at = Ref(row, col);
                int id = c.Style == null ? 0 : styles.Index(c.Style);
                string s = id == 0 ? "" : $" s=\"{id.ToString(inv)}\"";
                switch (c.Kind)
                {
                    case Kind.Text:
                        if (c.Text.Length == 0) { if (s.Length > 0) sb.Append($"<c r=\"{at}\"{s}/>"); break; }
                        sb.Append($"<c r=\"{at}\"{s} t=\"inlineStr\"><is><t xml:space=\"preserve\">{XlsxBook.Esc(c.Text)}</t></is></c>");
                        break;
                    case Kind.Number:
                        sb.Append($"<c r=\"{at}\"{s}><v>{Num(c.Value)}</v></c>");
                        break;
                    case Kind.Blank:
                        if (s.Length > 0) sb.Append($"<c r=\"{at}\"{s}/>");
                        break;
                    default:
                        sb.Append($"<c r=\"{at}\"{s}><f>{XlsxBook.Esc(c.Text)}</f>{(c.HasValue ? $"<v>{Num(c.Value)}</v>" : "")}</c>");
                        break;
                }
            }
            sb.Append("</row>");
        }
        sb.Append("</sheetData>");
        if (_merges.Count > 0)
        {
            sb.Append(inv, $"<mergeCells count=\"{_merges.Count}\">");
            foreach (var (r1, c1, r2, c2) in _merges) sb.Append($"<mergeCell ref=\"{Ref(r1, c1)}:{Ref(r2, c2)}\"/>");
            sb.Append("</mergeCells>");
        }
        if (Page is { } page)
        {
            if (page.CenterHorizontally) sb.Append("<printOptions horizontalCentered=\"1\"/>");
            var m = page.Margins;
            sb.Append(inv, $"<pageMargins left=\"{m.left}\" right=\"{m.right}\" top=\"{m.top}\" bottom=\"{m.bottom}\" header=\"{m.header}\" footer=\"{m.footer}\"/>");
            sb.Append("<pageSetup paperSize=\"9\"");
            if (page.Scale is { } scale) sb.Append(inv, $" scale=\"{scale}\"");
            sb.Append(page.Landscape ? " orientation=\"landscape\"/>" : " orientation=\"portrait\"/>");
            if (page.Header.Length > 0) sb.Append($"<headerFooter alignWithMargins=\"0\"><oddHeader>{XlsxBook.Esc(page.Header)}</oddHeader></headerFooter>");
        }
        else sb.Append("<pageMargins left=\"0.7\" right=\"0.7\" top=\"0.75\" bottom=\"0.75\" header=\"0.3\" footer=\"0.3\"/>");
        if (_breaks.Count > 0)
        {
            sb.Append(inv, $"<rowBreaks count=\"{_breaks.Count}\" manualBreakCount=\"{_breaks.Count}\">");
            foreach (int b in _breaks) sb.Append(inv, $"<brk id=\"{b}\" max=\"16383\" man=\"1\"/>");
            sb.Append("</rowBreaks>");
        }
        sb.Append("</worksheet>");
        return sb.ToString();
    }

    // 숫자는 잃는 자리 없이(R), 무한대·NaN 은 0 으로 적는다(엑셀이 못 읽는다).
    private static string Num(double v) =>
        double.IsFinite(v) ? v.ToString("R", CultureInfo.InvariantCulture) : "0";
}
