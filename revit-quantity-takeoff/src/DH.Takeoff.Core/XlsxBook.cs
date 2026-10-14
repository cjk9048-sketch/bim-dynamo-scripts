using System.Globalization;
using System.IO.Compression;
using System.Security;
using System.Text;

namespace DH.Takeoff.Core;

/// <summary>칸의 모양. 산출근거 내보내기가 쓰는 것만 둔다.</summary>
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

/// <summary>
/// 엑셀 파일(.xlsx)을 다른 라이브러리 없이 쓴다 — 글자·숫자·수식, 굵게·숫자 형식, 열 너비까지.
/// (Revit 안에서 돌기 때문에 엑셀 라이브러리 DLL 을 따로 싣지 않으려는 것이다.)
/// 행·열 번호는 1부터.
/// </summary>
public sealed class XlsxBook
{
    public List<XlsxSheet> Sheets { get; } = new();

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
        string s = sb.ToString().Trim('\'', ' ');
        if (s.Length == 0) s = "Sheet";
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
        using var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true);
        Put(zip, "[Content_Types].xml", ContentTypes());
        Put(zip, "_rels/.rels",
            Head + "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
            "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/>" +
            "</Relationships>");
        Put(zip, "xl/workbook.xml", Workbook());
        Put(zip, "xl/_rels/workbook.xml.rels", WorkbookRels());
        Put(zip, "xl/styles.xml", Styles);
        for (int i = 0; i < Sheets.Count; i++)
            Put(zip, $"xl/worksheets/sheet{i + 1}.xml", Sheets[i].ToXml(first: i == 0));
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
        sb.Append("<bookViews><workbookView xWindow=\"0\" yWindow=\"0\" windowWidth=\"28800\" windowHeight=\"15000\"/></bookViews>");
        sb.Append("<sheets>");
        for (int i = 0; i < Sheets.Count; i++)
            sb.Append($"<sheet name=\"{Esc(Sheets[i].Name)}\" sheetId=\"{i + 1}\" r:id=\"rId{i + 1}\"/>");
        sb.Append("</sheets>");
        // 수식의 값은 파일에도 적어 두지만(미리보기용), 엑셀이 열 때 전부 다시 계산하게 한다.
        sb.Append("<calcPr calcId=\"191029\" fullCalcOnLoad=\"1\"/>");
        sb.Append("</workbook>");
        return sb.ToString();
    }

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

    // 모양 표: cellXfs 의 차례가 XlsxStyle 의 차례와 같아야 한다.
    private const string Styles = Head +
        "<styleSheet xmlns=\"" + Main + "\">" +
        "<numFmts count=\"3\">" +
        "<numFmt numFmtId=\"164\" formatCode=\"#,##0.00\"/>" +
        "<numFmt numFmtId=\"165\" formatCode=\"0&quot;개소&quot;\"/>" +
        "<numFmt numFmtId=\"166\" formatCode=\"0.00%\"/>" +
        "</numFmts>" +
        "<fonts count=\"4\">" +
        "<font><sz val=\"10\"/><name val=\"맑은 고딕\"/><family val=\"3\"/><charset val=\"129\"/></font>" +
        "<font><b/><sz val=\"10\"/><name val=\"맑은 고딕\"/><family val=\"3\"/><charset val=\"129\"/></font>" +
        "<font><b/><sz val=\"13\"/><name val=\"맑은 고딕\"/><family val=\"3\"/><charset val=\"129\"/></font>" +
        "<font><sz val=\"9\"/><color rgb=\"FF7F7F7F\"/><name val=\"맑은 고딕\"/><family val=\"3\"/><charset val=\"129\"/></font>" +
        "</fonts>" +
        "<fills count=\"2\"><fill><patternFill patternType=\"none\"/></fill><fill><patternFill patternType=\"gray125\"/></fill></fills>" +
        "<borders count=\"1\"><border><left/><right/><top/><bottom/><diagonal/></border></borders>" +
        "<cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs>" +
        "<cellXfs count=\"10\">" +
        "<xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/>" +                                             // Normal
        "<xf numFmtId=\"0\" fontId=\"1\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyFont=\"1\"/>" +                             // Bold
        "<xf numFmtId=\"0\" fontId=\"2\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyFont=\"1\"/>" +                             // Title
        "<xf numFmtId=\"164\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyNumberFormat=\"1\"/>" +                   // Number2
        "<xf numFmtId=\"164\" fontId=\"1\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyNumberFormat=\"1\" applyFont=\"1\"/>" +   // BoldNumber2
        "<xf numFmtId=\"165\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyNumberFormat=\"1\"/>" +                   // Count
        "<xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyAlignment=\"1\"><alignment horizontal=\"center\"/></xf>" + // Center
        "<xf numFmtId=\"0\" fontId=\"3\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyFont=\"1\"/>" +                             // Gray
        "<xf numFmtId=\"166\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyNumberFormat=\"1\"/>" +                   // Percent
        "<xf numFmtId=\"0\" fontId=\"1\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyFont=\"1\" applyAlignment=\"1\"><alignment horizontal=\"center\"/></xf>" + // BoldCenter
        "</cellXfs>" +
        "<cellStyles count=\"1\"><cellStyle name=\"Normal\" xfId=\"0\" builtinId=\"0\"/></cellStyles>" +
        "</styleSheet>";

    internal static string Esc(string s) => SecurityElement.Escape(Clean(s)) ?? "";

    // XML 에 넣을 수 없는 글자(제어 문자, U+FFFE·U+FFFF)를 뺀다 — 하나라도 들어가면 엑셀이 파일을 열지 못한다.
    private static string Clean(string s)
    {
        if (s.All(Allowed)) return s;
        return new string(s.Where(Allowed).ToArray());
    }

    private static bool Allowed(char c) => (c >= ' ' || c == '\t' || c == '\n' || c == '\r') && c != '￾' && c != '￿';
}

/// <summary>시트 하나. 칸은 아무 차례로나 넣어도 되고, 저장할 때 행·열 차례로 적는다.</summary>
public sealed class XlsxSheet
{
    private enum Kind { Text, Number, Formula }

    private readonly record struct Cell(Kind Kind, string Text, double Value, bool HasValue, XlsxStyle Style);

    private readonly SortedDictionary<int, SortedDictionary<int, Cell>> _rows = new();
    private readonly SortedDictionary<int, double> _widths = new();

    public string Name { get; }

    internal XlsxSheet(string name) { Name = name; }

    /// <summary>열 너비(글자 수 기준).</summary>
    public void Width(int col, double width) => _widths[col] = width;

    /// <summary>엑셀의 한 칸에 들어가는 글자 수 한도. 넘는 글자가 있으면 엑셀이 파일을 열지 못한다.</summary>
    public const int MaxText = 32767;

    public void Text(int row, int col, string text, XlsxStyle style = XlsxStyle.Normal) =>
        Put(row, col, new Cell(Kind.Text, Fit(text ?? ""), 0, false, style));

    // 한도를 넘는 글자는 잘라 적는다(짝을 이루는 글자의 가운데에서 자르지 않는다).
    private static string Fit(string text)
    {
        if (text.Length <= MaxText) return text;
        int cut = MaxText - 1;                                   // 끝에 '…' 한 글자를 붙일 자리
        if (char.IsHighSurrogate(text[cut - 1])) cut--;
        return text[..cut] + "…";
    }

    public void Number(int row, int col, double value, XlsxStyle style = XlsxStyle.Normal) =>
        Put(row, col, new Cell(Kind.Number, "", value, true, style));

    /// <summary>수식 칸. formula 는 '=' 없이. cached = 미리 계산해 둔 값(엑셀이 열 때 다시 계산한다).</summary>
    public void Formula(int row, int col, string formula, double? cached = null, XlsxStyle style = XlsxStyle.Normal) =>
        Put(row, col, new Cell(Kind.Formula, formula.TrimStart('='), cached ?? 0, cached != null, style));

    private void Put(int row, int col, Cell cell)
    {
        if (row < 1 || col < 1 || col > 16384 || row > 1048576) throw new ArgumentOutOfRangeException(nameof(row), $"칸 위치가 범위를 벗어남: {row},{col}");
        if (!_rows.TryGetValue(row, out var r)) _rows[row] = r = new SortedDictionary<int, Cell>();
        r[col] = cell;
    }

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

    internal string ToXml(bool first)
    {
        var sb = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n");
        sb.Append("<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">");
        sb.Append(first ? "<sheetViews><sheetView tabSelected=\"1\" workbookViewId=\"0\"/></sheetViews>"
                        : "<sheetViews><sheetView workbookViewId=\"0\"/></sheetViews>");
        sb.Append("<sheetFormatPr defaultRowHeight=\"16.5\"/>");
        if (_widths.Count > 0)
        {
            sb.Append("<cols>");
            foreach (var (col, w) in _widths)
                sb.Append(CultureInfo.InvariantCulture, $"<col min=\"{col}\" max=\"{col}\" width=\"{w:0.##}\" customWidth=\"1\"/>");
            sb.Append("</cols>");
        }
        sb.Append("<sheetData>");
        foreach (var (row, cells) in _rows)
        {
            sb.Append(CultureInfo.InvariantCulture, $"<row r=\"{row}\">");
            foreach (var (col, c) in cells)
            {
                string at = Ref(row, col);
                string s = c.Style == XlsxStyle.Normal ? "" : $" s=\"{(int)c.Style}\"";
                switch (c.Kind)
                {
                    case Kind.Text:
                        if (c.Text.Length == 0) break;
                        sb.Append($"<c r=\"{at}\"{s} t=\"inlineStr\"><is><t xml:space=\"preserve\">{XlsxBook.Esc(c.Text)}</t></is></c>");
                        break;
                    case Kind.Number:
                        sb.Append($"<c r=\"{at}\"{s}><v>{Num(c.Value)}</v></c>");
                        break;
                    default:
                        sb.Append($"<c r=\"{at}\"{s}><f>{XlsxBook.Esc(c.Text)}</f>{(c.HasValue ? $"<v>{Num(c.Value)}</v>" : "")}</c>");
                        break;
                }
            }
            sb.Append("</row>");
        }
        sb.Append("</sheetData></worksheet>");
        return sb.ToString();
    }

    // 숫자는 잃는 자리 없이(R), 무한대·NaN 은 0 으로 적는다(엑셀이 못 읽는다).
    private static string Num(double v) =>
        double.IsFinite(v) ? v.ToString("R", CultureInfo.InvariantCulture) : "0";
}
