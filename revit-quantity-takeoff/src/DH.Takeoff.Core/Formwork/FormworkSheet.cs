using System.Globalization;

namespace DH.Takeoff.Core.Formwork;

/// <summary>거푸집 산출근거의 한 줄 — 같은 종류 · 부위 · 유형 · (중분류 · 안팎)에서 식이 같은 조각들을 묶은 것.</summary>
public sealed class FormworkLine
{
    public string Section { get; set; } = "";   // 종류(엑셀의 절): 유로폼  H=0~7m / 합판거푸집 (3회) …
    public string Part { get; set; } = "";      // 부위 제목: 벽체 / 바닥슬래브 (옆면) / 상부슬래브 (밑면) …
    public string Mark { get; set; } = "";      // 부호(유형 이름)
    public string Tag { get; set; } = "";       // 괄호 안: 배수지·내측 / 외측 / ""
    public string Formula { get; set; } = "";   // 숫자 식: 18*5 / 21*5.3-1*2.1 / 10.8(넓이만)
    public bool AreaOnly { get; set; }          // 직사각형이 아니라 넓이만 적은 줄
    public int Count { get; set; } = 1;         // 이 식인 조각 수
    public double Bim { get; set; }             // 이 줄에 든 조각 넓이의 합(㎡)
    public bool Joined { get; set; }            // 같은 평면에 맞닿은 조각을 이어 한 장으로 적은 줄(FormworkMerge)
    public SortedSet<long> Ids { get; } = new();
}

/// <summary>거푸집 시트에 실을 것 — 줄(종류 → 부위 → 줄 차례) + 종류마다 BIM 넓이 합.</summary>
public sealed class FormworkSheetData
{
    public List<FormworkLine> Lines { get; } = new();
    public List<(string section, double bim)> Sections { get; } = new();
}

/// <summary>
/// 거푸집 산출근거(엑셀 «거푸집» 시트) — 참고 산출서 「배수지 산근」의 모양(JACK 2026-10-15 미리보기 확인):
/// 종류(유로폼 높이 구간 · 합판거푸집 3/4/6회) → 부위 → 줄 «유형 (중분류·내측/외측) : 가로 × 높이 [− 구멍] = 값» → 계.
/// 숫자와 기호는 칸마다 따로 적고 값은 엑셀 수식으로 둔다(콘크리트 「산출근거」 시트와 같은 칸 짜임 — CalcSheet.WriteLine).
/// 조각이 직사각형(구멍도 직사각형)이 아니면 넓이만 적는다. 수량집계표에는 종류마다 산출 · BIM · 차이를 더한다.
/// </summary>
public static class FormworkSheet
{
    public const string SheetName = "거푸집";

    /// <summary>종류의 절 이름. 유로폼은 높이 구간을 붙인다.</summary>
    public static string SectionName(string item, int band, IReadOnlyList<double> bands) => item switch
    {
        FormworkTable.Euro => $"{FormworkTable.Euro}  {FormworkTable.BandName(band, bands)}".TrimEnd(),
        FormworkTable.Ply3 => "합판거푸집 (3회)",
        FormworkTable.Ply4 => "합판거푸집 (4회)",
        FormworkTable.Ply6 => "합판거푸집 (6회)",
        _ => item,
    };

    private static int ItemOrder(string item)
    {
        int at = Array.IndexOf(FormworkTable.Items, item);
        return at < 0 ? FormworkTable.Items.Length : at;
    }

    private static int SideOrder(string side) => side switch
    {
        "외측" => 0,
        _ when side.StartsWith("내측", StringComparison.Ordinal) => 1,
        "" => 2,
        FormworkAnalyzer.Opening => 3,
        _ => 4,
    };

    /// <summary>
    /// 줄을 짓는다. placeOf = 부재 Id → (부위 이름, 중분류) — 부위가 비었으면 부르는 쪽이 자동 이름을 넣어 준다.
    /// partOrder · zoneOrder = 이름 → 차례(작을수록 앞; 모르는 이름은 큰 값) — 「위치·부위」 목록의 차례(산출서의 차례)다.
    /// 세는 조각(Counted)만 싣는다. 같은 평면에 맞닿은 옆면 조각은 부재를 넘어 한 줄로 잇는다(FormworkMerge — 그 줄의 유형 · 부위는
    /// 가장 넓은 조각의 부재 것, 중분류는 조각들이 모두 같을 때만). merge 를 주지 않으면 여기서 찾는다.
    /// </summary>
    public static FormworkSheetData Build(IReadOnlyList<ElementFormwork> results, FormworkOptions options,
        Func<long, (string part, string zone)> placeOf, Func<string, int> partOrder, Func<string, int> zoneOrder, FormworkMerge.Outcome? merge = null)
    {
        merge ??= FormworkMerge.Find(results, options, id => placeOf(id).zone);
        var data = new FormworkSheetData();
        var lines = new Dictionary<(string section, string part, string mark, string tag, string formula, bool joined), (FormworkLine line, int item, int band, int part, int zone, int side, int seen)>();
        var sections = new Dictionary<string, (double bim, int item, int band)>();
        int seen = 0;
        foreach (var r in results)
        {
            var (partName, zone) = placeOf(r.Element.Id);
            foreach (var f in r.Faces)
                foreach (var p in f.Patches)
                {
                    if (!p.Counted || p.Item.Length == 0) continue;
                    int band = p.Item == FormworkTable.Euro ? p.Band : 0;
                    string section = SectionName(p.Item, band, options.Bands);
                    // 절의 BIM 넓이는 조각마다 더한다(이은 줄에 든 조각도 — 줄은 하나라도 넓이는 조각의 합이다).
                    var s = sections.GetValueOrDefault(section, (bim: 0.0, item: ItemOrder(p.Item), band: band));
                    sections[section] = (s.bim + p.Area, s.item, s.band);
                    // 이은 묶음은 대표 조각(가장 넓은 것)을 만났을 때 한 줄로 적고, 나머지 조각은 줄을 따로 내지 않는다.
                    var joined = merge.GroupOf(p);
                    if (joined != null && !ReferenceEquals(joined.Lead.Patch, p)) continue;
                    string group = FormworkTable.GroupOf(f.Class, p);
                    // 옆면이 기본인 부재(벽 · 기둥)는 부위 이름만, 나머지는 면의 갈래를 붙인다(바닥슬래브 (옆면) · 상부슬래브 (밑면) …).
                    string part = group == FormworkTable.GroupSide && r.Element.Kind is MemberKind.Wall or MemberKind.Column ? partName : $"{partName} ({group})";
                    // 중분류: 이은 줄은 조각들의 중분류가 모두 같을 때만 적는다(외측 이음이 중분류를 넘으면 «외측»만 — core R43 M-2).
                    string lineZone = joined is { MixedZones: true } ? "" : zone;
                    var tags = new List<string>();
                    if (lineZone.Length > 0) tags.Add(lineZone);
                    if (p.Side.Length > 0 && p.Side != FormworkAnalyzer.Opening) tags.Add(p.Side);
                    string tag = string.Join("·", tags);
                    var (formula, areaOnly) = Shape(joined?.Merged ?? p);
                    var key = (section, part, r.Element.Type, tag, formula, joined != null);
                    if (!lines.TryGetValue(key, out var entry))
                    {
                        entry = (new FormworkLine { Section = section, Part = part, Mark = r.Element.Type, Tag = tag, Formula = formula, AreaOnly = areaOnly, Count = 0, Joined = joined != null },
                                 ItemOrder(p.Item), band, partOrder(partName), lineZone.Length > 0 ? zoneOrder(lineZone) : int.MaxValue, SideOrder(p.Side), seen++);
                        lines[key] = entry;
                    }
                    entry.line.Count++;
                    entry.line.Bim += joined?.Merged.Area ?? p.Area;
                    if (joined == null) entry.line.Ids.Add(r.Element.Id);
                    else foreach (var m in joined.Members) entry.line.Ids.Add(m.Result.Element.Id);
                }
        }
        // 차례: 종류 → 부위(목록의 차례) → 중분류(목록의 차례) → 안팎 → 유형 → 처음 나온 차례.
        foreach (var e in lines.Values.OrderBy(e => e.item).ThenBy(e => e.band)
                     .ThenBy(e => e.part).ThenBy(e => e.line.Part, StringComparer.Ordinal)
                     .ThenBy(e => e.zone).ThenBy(e => e.side).ThenBy(e => e.line.Mark, StringComparer.Ordinal).ThenBy(e => e.seen))
            data.Lines.Add(e.line);
        foreach (var (name, s) in sections.OrderBy(kv => kv.Value.item).ThenBy(kv => kv.Value.band))
            data.Sections.Add((name, s.bim));
        return data;
    }

    // ── 조각 → 식 ────────────────────────────────────────────────────────────────

    private static string Num(double v) => Math.Round(v, 3).ToString("0.###", CultureInfo.InvariantCulture);

    /// <summary>
    /// 조각을 «가로 × 높이 [+ 떨어진 조각 …] [− 구멍 …]» 숫자 식으로. 고리가 모두 직사각형이고, 길이를 mm(셋째 자리)로 끊은 식의 값이
    /// 조각 넓이와 0.5% 안일 때만 — 아니면 넓이만(셋째 자리) 적는다(areaOnly).
    /// 고리가 구멍인지 떨어진 조각인지는 도는 방향으로 가른다: 가장 큰 고리(바깥)의 법선에 비춘 부호 넓이가 같은 쪽이면 떨어진 조각(더한다),
    /// 거꾸로면 구멍(뺀다) — 조각의 테두리는 Clipper 의 결과(바깥 고리와 구멍이 서로 반대로 돈다)를 그 차례 그대로 3D 로 옮긴 것이다.
    /// (절댓값 넓이로만 보면 떨어진 자투리를 구멍으로 빼는 식이 0.5% 안에 들 수 있었다 — core R42 M-1.)
    /// 가로 = 더 수평에 가까운 변(벽이면 길이, 그다음이 높이) · 수평인 면은 긴 변이 앞.
    /// </summary>
    public static (string formula, bool areaOnly) Shape(FacePatch patch)
    {
        string areaText = Num(patch.Area);
        if (patch.Outline.Count == 0) return (areaText, true);
        var rings = patch.Outline.Select(Clean).Where(r => r.Count >= 3).ToList();
        if (rings.Count == 0) return (areaText, true);
        var outer = rings.OrderByDescending(RingArea).First();
        var up = Newell(outer);
        var pieces = new List<string>();
        var holes = new List<string>();
        foreach (var ring in rings.OrderByDescending(r => ReferenceEquals(r, outer)))
        {
            if (Rect(ring) is not { } r) return (areaText, true);
            (Newell(ring).Dot(up) > 0 ? pieces : holes).Add($"{Num(r.a)}*{Num(r.b)}");
        }
        string formula = string.Join("+", pieces) + string.Concat(holes.Select(h => "-" + h));
        double value;
        try { value = FormulaEvaluator.Evaluate(formula); }
        catch (FormatException) { return (areaText, true); }
        if (!double.IsFinite(value) || Math.Abs(value - patch.Area) > 0.005 * patch.Area + 1e-4) return (areaText, true);
        return (formula, false);
    }

    private readonly record struct P3(double X, double Y, double Z)
    {
        public static P3 operator -(P3 a, P3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public double Dot(P3 b) => X * b.X + Y * b.Y + Z * b.Z;
        public P3 Cross(P3 b) => new(Y * b.Z - Z * b.Y, Z * b.X - X * b.Z, X * b.Y - Y * b.X);
        public double Length => Math.Sqrt(Dot(this));
    }

    // 고리를 다듬는다: 겹친 점(1µm 안)과 한 줄 위의 점(꺾이지 않는 점)을 뺀다.
    private static List<P3> Clean(List<double[]> ring)
    {
        var pts = new List<P3>();
        foreach (var q in ring)
        {
            if (q.Length < 3) continue;
            var p = new P3(q[0], q[1], q[2]);
            if (pts.Count == 0 || (p - pts[^1]).Length > 1e-6) pts.Add(p);
        }
        if (pts.Count > 1 && (pts[0] - pts[^1]).Length <= 1e-6) pts.RemoveAt(pts.Count - 1);
        bool removed = true;
        while (removed && pts.Count >= 3)
        {
            removed = false;
            for (int i = 0; i < pts.Count; i++)
            {
                P3 a = pts[(i + pts.Count - 1) % pts.Count], b = pts[i], c = pts[(i + 1) % pts.Count];
                P3 u = b - a, v = c - b;
                if (u.Cross(v).Length <= 1e-9 * Math.Max(1e-9, u.Length * v.Length) || u.Length <= 1e-6 || v.Length <= 1e-6)
                {
                    pts.RemoveAt(i);
                    removed = true;
                    break;
                }
            }
        }
        return pts;
    }

    // 고리의 넓이 벡터(Newell — 도는 방향의 오른손 법선 쪽, 길이 = 넓이의 두 배).
    private static P3 Newell(List<P3> ring)
    {
        var n = new P3(0, 0, 0);
        for (int i = 0; i < ring.Count; i++)
        {
            var c = ring[i].Cross(ring[(i + 1) % ring.Count]);
            n = new P3(n.X + c.X, n.Y + c.Y, n.Z + c.Z);
        }
        return n;
    }

    private static double RingArea(List<P3> ring) => Newell(ring).Length / 2;

    // 직사각형이면 (가로, 높이): 꼭짓점 넷 · 네 각이 직각(1e-6 안).
    private static (double a, double b)? Rect(List<P3> ring)
    {
        if (ring.Count != 4) return null;
        for (int i = 0; i < 4; i++)
        {
            P3 u = ring[(i + 1) % 4] - ring[i], v = ring[(i + 2) % 4] - ring[(i + 1) % 4];
            if (Math.Abs(u.Dot(v)) > 1e-6 * u.Length * v.Length) return null;
        }
        P3 e0 = ring[1] - ring[0], e1 = ring[2] - ring[1];
        double l0 = e0.Length, l1 = e1.Length;
        double rise0 = Math.Abs(e0.Z) / l0, rise1 = Math.Abs(e1.Z) / l1;
        // 더 수평에 가까운 변이 앞(벽: 길이 × 높이). 둘 다 수평이면(수평인 면) 긴 변이 앞.
        if (Math.Abs(rise0 - rise1) > 1e-6) return rise0 < rise1 ? (l0, l1) : (l1, l0);
        return l0 >= l1 ? (l0, l1) : (l1, l0);
    }

    // ── 엑셀 ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// «거푸집» 시트를 적고(산출근거와 같은 칸 짜임 — 종류 → 부위 → 줄 → 계 · 종류마다 합계), 종류마다 (합계 칸, BIM 넓이, 미리 계산한 값)을 돌려준다.
    /// </summary>
    internal static List<(string section, string sumRef, double bim, double value)> Write(XlsxSheet sheet, string title, FormworkSheetData data)
    {
        CalcSheet.Columns(sheet);
        int r = 1;
        sheet.Text(r++, 1, title + " — 거푸집 산출근거", XlsxStyle.Title);
        sheet.Text(r++, 1, "단위: 치수 m, 넓이 ㎡ · 줄 = 유형 (중분류·안팎) : 가로 × 높이 [− 구멍] — 직사각형이 아닌 면은 넓이만 · 값 = ROUND(식, 2)", XlsxStyle.Gray);
        r++;
        var totals = new List<(string, string, double, double)>();
        int no = 0;
        foreach (var section in data.Lines.GroupBy(l => l.Section))
        {
            no++;
            sheet.Text(r, 1, $"{no}. {section.Key}", XlsxStyle.Bold);
            r += 2;
            int partNo = 0;
            var partSums = new List<int>();
            double sectionValue = 0;
            foreach (var part in section.GroupBy(l => l.Part))
            {
                partNo++;
                sheet.Text(r, CalcSheet.MarkColumn, $"{partNo})", XlsxStyle.Bold);
                sheet.Text(r, CalcSheet.ZoneColumn, part.Key, XlsxStyle.Bold);
                r++;
                int first = r;
                double partValue = 0;
                foreach (var line in part)
                {
                    string note = (line.AreaOnly ? "넓이(직사각형 아님) · " : "") + (line.Joined ? "이은 면 · " : "") + "Id " + string.Join(", ", line.Ids.Take(6)) + (line.Ids.Count > 6 ? $" 외 {line.Ids.Count - 6}개" : "");
                    r = CalcSheet.WriteLine(sheet, r, line.Mark, line.Tag.Length > 0 ? "(" + line.Tag + ")" : "", line.Formula, line.Count, note, out double value);
                    partValue += value;
                }
                int last = r - 1;
                sheet.Text(r, CalcSheet.EqualsColumn, "계", XlsxStyle.BoldCenter);
                sheet.Formula(r, CalcSheet.SumColumn, $"SUM({XlsxSheet.Ref(first, CalcSheet.ResultColumn)}:{XlsxSheet.Ref(last, CalcSheet.ResultColumn)})",
                    FormulaDecomposer.RoundQuantity(partValue), XlsxStyle.BoldNumber2);
                partSums.Add(r);
                sectionValue += FormulaDecomposer.RoundQuantity(partValue);
                r += 2;
            }
            // 종류의 합계(수량집계표가 가리킨다).
            sheet.Text(r, CalcSheet.ZoneColumn, section.Key + " 합계", XlsxStyle.Bold);
            sheet.Formula(r, CalcSheet.SumColumn, string.Join("+", partSums.Select(s => XlsxSheet.Ref(s, CalcSheet.SumColumn))), FormulaDecomposer.RoundQuantity(sectionValue), XlsxStyle.BoldNumber2);
            double bim = data.Sections.FirstOrDefault(s => s.section == section.Key).bim;
            totals.Add((section.Key, sheet.FullRef(r, CalcSheet.SumColumn), bim, sectionValue));
            r += 3;
        }
        if (data.Lines.Count == 0) sheet.Text(r, 1, "거푸집으로 셀 면이 없습니다.", XlsxStyle.Gray);
        return totals;
    }
}
