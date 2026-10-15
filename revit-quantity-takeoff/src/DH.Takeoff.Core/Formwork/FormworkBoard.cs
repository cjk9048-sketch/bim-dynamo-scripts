using DH.Takeoff.Core.View3D;

namespace DH.Takeoff.Core.Formwork;

/// <summary>
/// 거푸집의 색 — 종류마다 한 색, 안쪽을 보는 면은 같은 색의 연한 쪽, 유로폼의 높은 구간은 짙은 쪽, 확인할 자리는 분홍.
/// Revit 3D 뷰에 덧그리는 색(FormworkOverlay)과 수량산출 창의 3D 가 같은 색을 쓴다.
/// </summary>
public static class FormworkPalette
{
    public static readonly (byte r, byte g, byte b) Check = (0xE9, 0x1E, 0x63);

    /// <summary>종류의 바탕색(모르는 종류는 회색).</summary>
    public static (byte r, byte g, byte b) Base(string item) => item switch
    {
        FormworkTable.Euro => (37, 99, 235),
        FormworkTable.Ply3 => (245, 124, 0),
        FormworkTable.Ply4 => (46, 160, 67),
        FormworkTable.Ply6 => (121, 85, 72),
        _ => (120, 120, 120),
    };

    /// <summary>조각의 색: 확인할 자리는 분홍 · 유로폼의 둘째 높이 구간부터는 짙게 · 안쪽을 보는 면은 연하게.</summary>
    public static (byte r, byte g, byte b) Of(FacePatch patch)
    {
        if (patch.NeedsCheck) return Check;
        var (br, bg, bb) = Base(patch.Item);
        int r = br, g = bg, b = bb;
        if (patch.Item == FormworkTable.Euro && patch.Band > 0) (r, g, b) = (r * 55 / 100, g * 55 / 100, b * 55 / 100);
        if (patch.Side.StartsWith("내측", StringComparison.Ordinal)) (r, g, b) = ((r + 255) / 2, (g + 255) / 2, (b + 255) / 2);
        return ((byte)r, (byte)g, (byte)b);
    }
}

/// <summary>
/// 3D 에 칠하는 거푸집 조각 한 덩이 — 한 부재에서, 대응표 한 줄(유형 × 면의 갈래)에 들고 안팎 · 높이 구간이 같은 조각들.
/// 덩이 안의 조각은 대응표를 어떻게 고쳐도 늘 같은 색 · 같은 '세는가'다(FormworkBoard 의 시험이 지킨다) — 그래서 덩이마다 겉면 하나로 그린다.
/// </summary>
public sealed class FormworkPiece
{
    public required ElementFormwork Result { get; init; }
    public required string Type { get; init; }
    public required string Group { get; init; }
    public required string Side { get; init; }
    public required int Band { get; init; }
    public List<FacePatch> Patches { get; } = new();
    /// <summary>바깥으로 조금 띄운 세모 + 테두리(장면 좌표 — 부재의 겉면과 같은 원점을 뺐다).</summary>
    public MeshData Mesh { get; internal set; } = new();
    public double Area => Patches.Sum(p => p.Area);
    /// <summary>덩이의 색 · 상태를 대표하는 조각.</summary>
    public FacePatch Lead => Patches[0];
}

/// <summary>
/// 수량산출 창의 ② 거푸집면 설정 걸음의 상태(JACK 2026-10-15 «대응표 + 3D»): 대응표의 줄 · 줄마다 고친 종류 · 3D 에 칠할 덩이 · 종류별 합계.
/// 종류를 고치면 대응표만 다시 건다(FormworkAnalyzer.Reclassify — 형상은 다시 재지 않는다). 화면(WPF)과 파일 저장은 부르는 쪽이 한다.
/// </summary>
public sealed class FormworkBoard
{
    /// <summary>덩이를 면에서 바깥으로 띄우는 거리(m) — 부재의 겉면과 같은 깊이에서 깜박이지 않게(Revit 덧그림과 같은 4mm).</summary>
    public const double Lift = 0.004;

    public IReadOnlyList<ElementFormwork> Results { get; }
    public FormworkOptions Options { get; }
    public FormworkRules Rules { get; }
    public IReadOnlyList<FormworkPiece> Pieces { get; }
    public List<FormworkRow> Rows { get; private set; } = new();
    public List<FormworkTotal> Totals { get; private set; } = new();

    /// <param name="results">Analyze 의 결과(곡면 조각의 세모는 붙인 뒤 — 없으면 그 곡면은 칠하지 않는다).</param>
    /// <param name="origin">장면의 원점(m) — 부재의 겉면(ZoneScene)에서 뺀 것과 같은 값.</param>
    public FormworkBoard(IReadOnlyList<ElementFormwork> results, FormworkOptions options, Vec3 origin)
    {
        Results = results;
        Options = options;
        Rules = options.Rules ??= new FormworkRules();
        Pieces = BuildPieces(results, origin);
        Refresh();
    }

    /// <summary>모든 조각의 거푸집 넓이(㎡).</summary>
    public double Total => Totals.Sum(t => t.Area);

    /// <summary>
    /// 확인할 자리 가운데 대응표 줄에 드는 것(아래가 빈 밑면 · 안팎을 정하지 못한 면 · 곡면 — 3D 에 분홍으로 칠한 자리) · 대응표에서 정하면 줄어든다.
    /// </summary>
    public int CheckCount => FormworkSummary.RowPatches(Results).Count(k => k.Patch.NeedsCheck);

    /// <summary>
    /// 확인할 자리 가운데 대응표 줄에 들지 않아 3D 에 칠하지 않는 것 — 아래 콘크리트와 너무 가까워 거푸집을 댈 수 없는 밑면(틈 · 세지 않는다).
    /// 화면은 이 수를 따로 보인다(«확인할 자리 n»에 섞으면 3D 에서 찾을 수 없는 자리가 든다 — core R42 M-3).
    /// </summary>
    public int UncoveredChecks => Results.SelectMany(r => r.Faces).SelectMany(f => f.Patches).Count(p => p.NeedsCheck) - CheckCount;

    /// <summary>
    /// 엑셀에서 맞닿은 옆면을 한 줄로 이은 결과(FormworkMerge — 중분류는 부재가 읽힌 때의 값). 대응표를 바꿀 때마다 다시 찾는다:
    /// 종류가 같아져 이어지거나, 달라져 끊긴다.
    /// </summary>
    public FormworkMerge.Outcome Merge { get; private set; } = new();

    /// <summary>같은 평면에 맞닿았지만 대응표의 종류가 달라 엑셀에서 한 줄로 잇지 못한 조각 쌍의 수(JACK 2026-10-15: 잇지 않고 알린다).</summary>
    public int KindSplits => Merge.KindSplits;

    /// <summary>그 줄의 지금 값("" = 초안대로).</summary>
    public string RuleOf(string type, string group) => Rules.Lookup(type, group) ?? "";

    /// <summary>
    /// 줄의 종류를 바꾼다(choice = "" 이면 초안대로). 바뀌었으면 true — 대응표만 다시 걸고 줄 · 합계를 다시 센다.
    /// 파일에 적는 것은 부르는 쪽이 한다(Rules).
    /// </summary>
    public bool SetRule(string type, string group, string choice)
    {
        string next = (choice ?? "").Trim();
        if (RuleOf(type, group) == next) return false;
        Rules.Set(type, group, next);
        FormworkAnalyzer.Reclassify(Results, Options);
        Refresh();
        return true;
    }

    /// <summary>대응표 전체를 갈아 끼운다(창을 연 때로 되돌릴 때). 다시 센다.</summary>
    public void ReplaceRules(FormworkRules rules)
    {
        foreach (var (type, group, _) in Rules.All.ToList()) Rules.Set(type, group, "");
        foreach (var (type, group, item) in rules.All) Rules.Set(type, group, item);
        FormworkAnalyzer.Reclassify(Results, Options);
        Refresh();
    }

    /// <summary>그 줄에 든 덩이의 차례들.</summary>
    public IEnumerable<int> PiecesOf(string type, string group)
    {
        for (int i = 0; i < Pieces.Count; i++)
            if (Pieces[i].Type == type && Pieces[i].Group == group) yield return i;
    }

    private void Refresh()
    {
        Rows = FormworkSummary.Rows(Results, Rules);
        Totals = FormworkSummary.Totals(Results, Options);
        Merge = FormworkMerge.Find(Results, Options);
    }

    // 덩이를 짓는다: 대응표의 줄에 드는 조각(RowPatches)을 (부재 · 유형 · 면의 갈래 · 안팎 · 높이 구간 · 까닭이 있는가)로 묶는다.
    // 이 열쇠가 같으면 대응표를 어떻게 고쳐도 색 · '세는가' · '확인할 자리인가'가 같다: 종류는 (유형, 갈래)마다 하나이고(대응표의 열쇠),
    // 초안의 종류도 부재와 갈래로 정해지며(FormworkTable.ItemOf), 확인할 자리인가는 안팎 · 아래 사정 · 까닭이 있는가로 정해진다.
    // '까닭'은 대응표를 걸기 전의 값으로 본다(대응표가 '세지 않음' 등으로 까닭을 덮어쓰기 때문).
    private static List<FormworkPiece> BuildPieces(IReadOnlyList<ElementFormwork> results, Vec3 origin)
    {
        var pieces = new List<FormworkPiece>();
        var at = new Dictionary<(ElementFormwork r, string type, string group, string side, int band, bool note), FormworkPiece>();
        foreach (var (r, _, p, type, group) in FormworkSummary.RowPatches(results))
        {
            bool note = (p.Unruled?.Note ?? p.Note).Length > 0;
            var key = (r, type, group, p.Side, p.Band, note);
            if (!at.TryGetValue(key, out var piece))
            {
                at[key] = piece = new FormworkPiece { Result = r, Type = type, Group = group, Side = p.Side, Band = p.Band };
                pieces.Add(piece);
            }
            piece.Patches.Add(p);
        }
        foreach (var piece in pieces) piece.Mesh = MeshOf(piece.Patches, origin);
        return pieces;
    }

    // 조각들의 세모를 바깥(세모의 법선 쪽 — 세모는 면의 바깥을 보게 돌아 있다)으로 Lift 만큼 띄워 담고, 테두리를 모서리 선분으로 담는다.
    private static MeshData MeshOf(IEnumerable<FacePatch> patches, Vec3 origin)
    {
        var builder = new MeshBuilder();
        foreach (var patch in patches)
        {
            Vec3? normal = null;
            for (int i = 0; i + 2 < patch.Triangles.Count; i += 3)
            {
                Vec3 a = Of(patch.Triangles[i]), b = Of(patch.Triangles[i + 1]), c = Of(patch.Triangles[i + 2]);
                var n = (b - a).Cross(c - a);
                if (!(n.Length > 1e-12) || !n.IsFinite) continue;
                var lift = n.Unit() * Lift;
                normal ??= n.Unit();
                builder.AddTriangle(a + lift, b + lift, c + lift);
            }
            // 테두리(평면 조각만 있다 — 곡면은 세모만): 면과 같이 띄운다. 법선을 모르면(세모가 없는 조각) 테두리도 그리지 않는다.
            if (normal is not { } up) continue;
            var raise = up * Lift;
            foreach (var loop in patch.Outline)
                for (int i = 0; i < loop.Count; i++)
                    builder.AddSegment(Of(loop[i]) + raise, Of(loop[(i + 1) % loop.Count]) + raise, up, up);
        }
        return builder.Build(origin);
    }

    private static Vec3 Of(double[] p) => new(p[0], p[1], p[2]);
}
