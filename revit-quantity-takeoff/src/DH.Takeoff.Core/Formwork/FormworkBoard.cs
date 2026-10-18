using System.Diagnostics;
using DH.Takeoff.Core.View3D;

namespace DH.Takeoff.Core.Formwork;

/// <summary>
/// 거푸집의 색 — 종류마다 한 색, 안쪽을 보는 면은 같은 색의 연한 쪽, 유로폼의 높은 구간은 짙은 쪽, 확인할 자리는 분홍.
/// Revit 3D 뷰에 덧그리는 색(FormworkOverlay)과 수량산출 창의 3D 가 같은 색을 쓴다.
/// </summary>
public static class FormworkPalette
{
    public static readonly (byte r, byte g, byte b) Check = (0xE9, 0x1E, 0x63);

    /// <summary>사람이 ② 3D 에서 뺀 판(R48) — 옅은 회갈색(세지 않는 회색 부재와 갈리고, 어느 종류의 색과도 겹치지 않게).</summary>
    public static readonly (byte r, byte g, byte b) Dropped = (204, 188, 170);

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
/// 3D 에 칠하는 거푸집 한 덩이(R48 — JACK 2026-10-16 «② 3D = 완성된 판»):
///  · 판(<see cref="Plate"/>) — 엑셀 줄의 조각 · Revit 거푸집 객체 하나와 같은 한 장(FormworkSheet 의 Plates). 맞닿은 면을 부재를 넘어 이은 판은 든 조각이 여럿이다.
///  · 판이 아닌 덩이 — 세지 않는 조각(대응표 · 초안에서 세지 않음)과 아직 정하지 않은 아래가 빈 밑면: 한 부재에서 같은 줄 · 안팎 · 높이 구간 · 까닭인 조각들.
///  · 뺀 판(<see cref="Dropped"/>) — 사람이 ② 3D 에서 뺀 판 하나(대표 열쇠마다).
/// 덩이는 대응표 · 뺀 판을 바꿀 때마다 다시 짓는다(FormworkBoard.Refresh).
/// </summary>
public sealed class FormworkPiece
{
    /// <summary>든 조각과 그 조각의 유형 × 면의 갈래(드는 줄은 FormworkSummary.RowOf).</summary>
    public List<FormworkSummary.Keyed> Members { get; } = new();

    /// <summary>든 조각(Members 의 차례 그대로).</summary>
    public List<FacePatch> Patches { get; } = new();

    /// <summary>판이면 그 판(엑셀 줄 조각 · 거푸집 객체), 아니면 null.</summary>
    public FormworkUnit? Plate { get; init; }

    /// <summary>뺀 판이면 그 대표 열쇠(대응표 파일의 «뺀 판:…»), 아니면 "".</summary>
    public string DropLead { get; init; } = "";

    public bool Dropped => DropLead.Length > 0;

    /// <summary>바깥으로 조금 띄운 세모 + 테두리(장면 좌표 — 부재의 겉면과 같은 원점을 뺐다). 이은 판의 테두리는 합친 테두리 하나다.</summary>
    public MeshData Mesh { get; internal set; } = new();

    internal int LeadIndex { get; set; }

    /// <summary>덩이를 대표하는 조각(판은 가장 넓은 조각 — 엑셀 줄의 부호 · 부위를 준 조각 · 뺀 판은 대표 열쇠의 조각).</summary>
    public FormworkSummary.Keyed LeadMember => Members[LeadIndex];

    /// <summary>덩이의 색 · 상태를 대표하는 조각.</summary>
    public FacePatch Lead => LeadMember.Patch;

    public ElementFormwork Result => LeadMember.Result;
    public string Type => LeadMember.Type;
    public string Group => LeadMember.Group;
    public string Side => Lead.Side;
    public int Band => Lead.Band;
    public double Area => Patches.Sum(p => p.Area);

    internal void Add(FormworkSummary.Keyed member)
    {
        Members.Add(member);
        Patches.Add(member.Patch);
    }
}

/// <summary>
/// 수량산출 창의 ② 거푸집면 설정 걸음의 상태(JACK 2026-10-15 «대응표 + 3D»): 대응표의 줄 · 줄마다 고친 종류 · 3D 에 칠할 덩이(판) · 종류별 합계 ·
/// 엑셀 거푸집 시트에 실을 것(Sheet — 3D 의 판과 같은 것). 종류를 고치거나 판을 빼면 대응표만 다시 건다(FormworkAnalyzer.Reclassify — 형상은 다시 재지 않는다).
/// 화면(WPF)과 파일 저장은 부르는 쪽이 한다.
/// </summary>
public sealed class FormworkBoard
{
    /// <summary>덩이를 면에서 바깥으로 띄우는 거리(m) — 부재의 겉면과 같은 깊이에서 깜박이지 않게(Revit 덧그림과 같은 4mm).</summary>
    public const double Lift = 0.004;

    public IReadOnlyList<ElementFormwork> Results { get; }
    public FormworkOptions Options { get; }
    public FormworkRules Rules { get; }
    public IReadOnlyList<FormworkPiece> Pieces { get; private set; } = Array.Empty<FormworkPiece>();
    public List<FormworkRow> Rows { get; private set; } = new();
    public List<FormworkTotal> Totals { get; private set; } = new();

    /// <summary>엑셀 거푸집 시트에 실을 것(지금의 대응표 · 뺀 판 · 부르는 쪽이 준 부위 · 중분류로 지은 것) — 3D 의 판은 이것의 Plates 다.</summary>
    public FormworkSheetData Sheet { get; private set; } = new();

    /// <summary>뺀 판을 지금 모양에서 다시 찾은 결과(FormworkSummary.CheckDrops — 하나도 못 찾음 · 일부만 찾음 · 넓이가 달라짐).</summary>
    public FormworkSummary.DropCheck DropCheck { get; private set; } = FormworkSummary.DropCheck.None;

    /// <summary>모양이 바뀌어 하나도 찾지 못해 다시 센 뺀 판의 대표 열쇠(DropCheck.Lost).</summary>
    public IReadOnlyList<string> LostDrops => DropCheck.Lost;

    /// <summary>부위 줄에 가려진 옛 유형 줄(FormworkSummary.HiddenTypeRules — ② 표에 없고 파일에만 남은 줄).</summary>
    public IReadOnlyList<(string type, string group, string item)> HiddenTypeRules { get; private set; } = Array.Empty<(string, string, string)>();

    /// <summary>마지막으로 다시 셀 때 걸린 시간(ms — 줄 · 합계 · 시트 · 덩이를 다시 지은 것). 일지에 남긴다(core-verify R48 계획 L5).</summary>
    public long RefreshMilliseconds { get; private set; }

    private readonly Vec3 _origin;
    private readonly Func<long, (string part, string zone)> _placeOf;
    private readonly Func<string, int> _partOrder;
    // 덩이의 겉면은 든 조각이 같으면 다시 쓴다(대응표를 고칠 때 바뀐 판만 다시 짓게).
    private readonly Dictionary<FacePatch, int> _serial = new(ReferenceEqualityComparer.Instance);
    private Dictionary<string, MeshData> _meshes = new(StringComparer.Ordinal);

    /// <param name="results">Analyze 의 결과(곡면 조각의 세모는 붙인 뒤 — 없으면 그 곡면은 칠하지 않는다).</param>
    /// <param name="origin">장면의 원점(m) — 부재의 겉면(ZoneScene)에서 뺀 것과 같은 값.</param>
    /// <param name="placeOf">
    /// 부재 Id → (부위 이름, 중분류) — 판을 지을 때(엑셀과 같은 잇기 · 줄). 수량산출 창은 ① 의 지금 값(부위가 비었으면 자동 이름)을 준다.
    /// 없으면 부재가 읽힌 때의 부위(비었으면 자동 이름) · 중분류.
    /// </param>
    /// <param name="partOrder">부위 이름 → 목록의 차례(시트 줄의 차례). 없으면 모두 같다.</param>
    public FormworkBoard(IReadOnlyList<ElementFormwork> results, FormworkOptions options, Vec3 origin,
        Func<long, (string part, string zone)>? placeOf = null, Func<string, int>? partOrder = null)
    {
        Results = results;
        Options = options;
        Rules = options.Rules ??= new FormworkRules();
        _origin = origin;
        var elements = new Dictionary<long, FormworkElement>();
        foreach (var r in results) elements.TryAdd(r.Element.Id, r.Element);
        _placeOf = placeOf ?? (id => elements.TryGetValue(id, out var el)
            ? (el.Part.Length > 0 ? el.Part : CalcPlan.KindName(el.Kind, el.Type), el.Zone)
            : ("", ""));
        _partOrder = partOrder ?? (_ => 0);
        foreach (var r in results)
            foreach (var f in r.Faces)
                foreach (var p in f.Patches)
                    _serial.TryAdd(p, _serial.Count);
        // 뺀 판은 곡면의 세모를 붙인 뒤에 다시 맞춘다(Analyze 때는 곡면에 세모가 없어 그 조각을 알아보지 못한다).
        if (Rules.DropCount > 0) FormworkAnalyzer.Reclassify(results, options);
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

    /// <summary>엑셀에서 맞닿은 면을 한 줄로 이은 결과(시트를 지을 때 쓴 잇기 그대로 — 중분류 · 부위는 부르는 쪽이 준 값).</summary>
    public FormworkMerge.Outcome Merge => Sheet.Merge;

    /// <summary>같은 평면에 맞닿았지만 대응표의 종류가 달라 엑셀에서 한 줄로 잇지 못한 조각 쌍의 수(JACK 2026-10-15: 잇지 않고 알린다).</summary>
    public int KindSplits => Merge.KindSplits;

    /// <summary>뺀 판(지금 결과에서 찾은 것)의 수와 넓이.</summary>
    public int DroppedCount => Pieces.Count(p => p.Dropped);
    public double DroppedArea => Pieces.Where(p => p.Dropped).Sum(p => p.Area);

    /// <summary>그 줄의 지금 값("" = 초안대로).</summary>
    public string RuleOf(string type, string group) => Rules.Lookup(type, group) ?? "";

    /// <summary>
    /// 줄의 종류를 바꾼다(choice = "" 이면 초안대로). 바뀌었으면 true — 대응표만 다시 걸고 줄 · 합계 · 판을 다시 센다.
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

    /// <summary>대응표 전체(뺀 판까지)를 갈아 끼운다(창을 연 때로 되돌릴 때). 다시 센다.</summary>
    public void ReplaceRules(FormworkRules rules)
    {
        // 넘긴 것이 이 대응표 자신이어도 지우기 전에 떠 둔다(core-verify R48 L4 — 비우는 반복이 먼저 돌면 다 사라진다).
        var items = rules.All.ToList();
        var drops = rules.Drops.Select(d => (d.lead, records: d.records.ToList())).ToList();
        foreach (var (type, group, _) in Rules.All.ToList()) Rules.Set(type, group, "");
        foreach (var (type, group, item) in items) Rules.Set(type, group, item);
        foreach (var (lead, _) in Rules.Drops.ToList()) Rules.Restore(lead);
        foreach (var (lead, records) in drops) Rules.Drop(lead, records);
        FormworkAnalyzer.Reclassify(Results, Options);
        Refresh();
    }

    /// <summary>
    /// 판 한 장을 뺀다(R48 — JACK 2026-10-16 «찍어서 빼기 · 판 한 장»): 판에 든 조각마다 모양 열쇠를 대응표에 적고 다시 센다. 뺐으면 true.
    /// 판이 아닌 덩이(세지 않는 덩이 · 아직 정하지 않은 아래가 빈 밑면)와 이미 뺀 판은 false. 열쇠가 판의 어느 조각에도 맞지 않으면(빠지지 않음)
    /// 적은 것을 되돌리고 false(revit-verify R48 설치 전 L4 — «뺐습니다» 알림만 뜨고 판은 그대로 세는 일이 없게). 파일에 적는 것은 부르는 쪽이 한다(Rules).
    /// </summary>
    public bool Drop(FormworkPiece piece)
    {
        if (piece.Dropped || piece.Plate == null || piece.Members.Count == 0) return false;
        var records = piece.Members.Select(m => DropRecord.Of(m.Face, m.Patch)).ToList();
        string lead = records[piece.LeadIndex].Text;
        Rules.Drop(lead, records);
        FormworkAnalyzer.Reclassify(Results, Options);
        if (!piece.Patches.Any(p => p.DropLead == lead))
        {
            Rules.Restore(lead);
            FormworkAnalyzer.Reclassify(Results, Options);
            Refresh();
            return false;
        }
        Refresh();
        return true;
    }

    /// <summary>뺀 판을 되살린다. 되살렸으면 true.</summary>
    public bool Restore(FormworkPiece piece) => piece.Dropped && Restore(piece.DropLead);

    /// <summary>그 대표 열쇠의 뺀 판을 되살린다(모양이 바뀌어 찾지 못한 판도). 되살렸으면 true.</summary>
    public bool Restore(string lead)
    {
        if (!Rules.Restore(lead)) return false;
        FormworkAnalyzer.Reclassify(Results, Options);
        Refresh();
        return true;
    }

    /// <summary>
    /// 모양이 바뀌어 하나도 찾지 못한 뺀 판의 기록을 대응표에서 지운다(revit-verify R48 설치 전 M1 — 지울 길이 없으면 경고가 그 모델에 영영 남는다).
    /// 그 자리는 이미 다시 세고 있어 합계는 그대로다. 지운 수.
    /// </summary>
    public int ForgetLostDrops()
    {
        int n = LostDrops.ToList().Count(lead => Rules.Restore(lead));
        if (n == 0) return 0;
        FormworkAnalyzer.Reclassify(Results, Options);
        Refresh();
        return n;
    }

    // 부재 Id → 그 부재의 덩이 조각(판 · 세지 않는 덩이 · 뺀 판 — 대응표 줄에 드는 조각 전부: 대응표를 바꿔도 같은 모임이다)과 그 면의 바깥 법선.
    private Dictionary<long, List<(double nx, double ny, double nz, FacePatch patch)>>? _covering;

    /// <summary>모서리 선분을 덮였는지 볼 간격(m) — 이보다 긴 선분은 나눠 보고 덮이지 않은 자리만 남긴다.</summary>
    public const double EdgeProbeStep = 0.1;

    /// <summary>
    /// ② 의 부재 겉면(R48b — JACK 2026-10-16 «거푸집 뒤에 비쳐서 보여»): 덩이(면에서 4 mm 띄운 판 · 세지 않는 덩이 · 뺀 판)가 덮은 면 위의 모서리 선분을 뺀 것.
    /// 부재 모서리 선은 면에서 3 mm 띄워 그리는데(Model3DView) 판과 1 mm 밖에 안 떨어져, 화면의 깊이 셈이 가르지 못해 판을 뚫고 점선처럼 비쳤다.
    /// 그 자리의 경계는 판의 테두리 선이 보인다. 덩이가 없는 면(윗면 · 맞닿아 덮인 자리 · 버림 밑면 …)의 모서리 선은 그대로 둔다.
    /// 선분의 면 법선(두 끝점)이 그 덩이 조각의 면 법선과 같고 그 자리가 조각 위(평면에서 5 mm · 테두리에서 5 mm 안)인 곳만 뺀다 — 긴 선분은
    /// EdgeProbeStep 간격으로 나눠 덮이지 않은 동안만 남긴다. 곡면(법선 없음)의 선분은 그대로. 세모 · 꼭짓점은 같은 것을 함께 쓴다.
    /// </summary>
    /// <param name="mesh">부재의 겉면(장면 좌표 — 이 판의 origin 을 뺀 것).</param>
    public MeshData WithoutCoveredEdges(MeshData mesh, long elementId)
    {
        _covering ??= CoveringIndex();
        if (!_covering.TryGetValue(elementId, out var mine) || mesh.Segments == 0) return mesh;
        var kept = new List<double>(mesh.E.Length);
        var e = mesh.E;
        bool removed = false;
        for (int s = 0; s < mesh.Segments; s++)
        {
            int at = s * MeshData.SegmentStride;
            var a = new Vec3(e[at], e[at + 1], e[at + 2]);
            var b = new Vec3(e[at + 3], e[at + 4], e[at + 5]);
            var na = new Vec3(e[at + 6], e[at + 7], e[at + 8]);
            var nb = new Vec3(e[at + 9], e[at + 10], e[at + 11]);
            // 평면의 테두리(두 끝점의 법선이 같다)만 본다.
            var candidates = na.Length > 0.5 && na.Dot(nb) > 0.999
                ? mine.Where(c => c.nx * na.X + c.ny * na.Y + c.nz * na.Z > 0.999).ToList()
                : null;
            if (candidates == null || candidates.Count == 0) { Keep(at, 0, 1); continue; }
            double length = (b - a).Length;
            int steps = Math.Clamp((int)Math.Ceiling(length / EdgeProbeStep), 1, 1000);
            int runStart = -1;
            for (int k = 0; k <= steps; k++)
            {
                bool covered = false;
                if (k < steps)
                {
                    var mid = a + (b - a) * ((k + 0.5) / steps) + _origin;
                    covered = candidates.Any(c => PatchGeometry.Contains(c.patch, mid.X, mid.Y, mid.Z, CoverTol));
                }
                if (k < steps && !covered) { if (runStart < 0) runStart = k; continue; }
                if (runStart >= 0) { Keep(at, (double)runStart / steps, (double)k / steps); runStart = -1; }
                if (k < steps) removed = true;
            }
        }
        return removed ? new MeshData { P = mesh.P, T = mesh.T, E = kept.ToArray(), Box = mesh.Box, Skipped = mesh.Skipped, Twisted = mesh.Twisted } : mesh;

        // 선분의 [from, to] 구간을 남긴다(법선은 그대로).
        void Keep(int at, double from, double to)
        {
            for (int i = 0; i < 3; i++) kept.Add(e[at + i] + (e[at + 3 + i] - e[at + i]) * from);
            for (int i = 0; i < 3; i++) kept.Add(e[at + i] + (e[at + 3 + i] - e[at + i]) * to);
            for (int i = 6; i < 12; i++) kept.Add(e[at + i]);
        }
    }

    // 덮였다고 보는 거리(m) — 평면에서 · 조각 테두리에서(부재 겉면의 세모와 거푸집 조각은 같은 형상에서 오지만 따로 다듬어진다).
    private const double CoverTol = 0.005;

    private Dictionary<long, List<(double nx, double ny, double nz, FacePatch patch)>> CoveringIndex()
    {
        var index = new Dictionary<long, List<(double, double, double, FacePatch)>>();
        foreach (var k in FormworkSummary.RowPatches(Results))
        {
            if (k.Face.Normal.Length != 3 || k.Patch.Triangles.Count < 3) continue;
            if (!index.TryGetValue(k.Result.Element.Id, out var list)) index[k.Result.Element.Id] = list = new List<(double, double, double, FacePatch)>();
            list.Add((k.Face.Normal[0], k.Face.Normal[1], k.Face.Normal[2], k.Patch));
        }
        return index;
    }

    /// <summary>그 조각이 든 덩이의 차례(없으면 −1) — 다시 센 뒤 고른 판을 다시 찾을 때.</summary>
    public int IndexOf(FacePatch patch)
    {
        for (int i = 0; i < Pieces.Count; i++)
            if (Pieces[i].Patches.Any(p => ReferenceEquals(p, patch))) return i;
        return -1;
    }

    /// <summary>그 대표 열쇠의 뺀 판 덩이의 차례(없으면 −1).</summary>
    public int IndexOfDrop(string lead)
    {
        for (int i = 0; i < Pieces.Count; i++)
            if (Pieces[i].DropLead == lead) return i;
        return -1;
    }

    /// <summary>
    /// 그 줄에 든 덩이의 차례들: 든 조각 하나라도 그 줄에 드는 덩이(FormworkSummary.RowOf — 이은 판은 통째로: «벽체»를 누르면 벽 + 슬래브 옆을 이은 판 한 장).
    /// 뺀 판은 어느 줄에도 들지 않는다(core-verify R48 계획 M1 — 줄의 넓이에서도 빠진다).
    /// </summary>
    public IEnumerable<int> PiecesOf(string type, string group)
    {
        for (int i = 0; i < Pieces.Count; i++)
            if (InRow(Pieces[i], type, group)) yield return i;
    }

    /// <summary>그 덩이가 그 줄에 드는가(PiecesOf 와 같은 잣대 — 화면이 덩이마다 묻는다).</summary>
    public bool InRow(FormworkPiece piece, string type, string group) =>
        !piece.Dropped && piece.Members.Any(m => FormworkSummary.RowOf(m) == (type, group));

    /// <summary>그 덩이에서 그 줄에 든 조각의 넓이(㎡) — 줄마다 합하면 ② 표의 넓이다.</summary>
    public double AreaIn(FormworkPiece piece, string type, string group) =>
        piece.Dropped ? 0 : piece.Members.Where(m => FormworkSummary.RowOf(m) == (type, group)).Sum(m => m.Patch.Area);

    /// <summary>
    /// 그 덩이를 정한 줄(revit-verify R47 M1 — 3D 에서 덩이를 누르면 ② 표에서 이 줄을 고른다): 대표 조각이 드는 줄(부위를 넣은 부재의 면이면 부위 줄,
    /// 아니면 유형 줄 — R48). 뺀 판은 null(띠에 [되살리기]만).
    /// </summary>
    public (string type, string group)? RowOf(FormworkPiece piece) => piece.Dropped ? null : FormworkSummary.RowOf(piece.LeadMember);

    private void Refresh()
    {
        var watch = Stopwatch.StartNew();
        Rows = FormworkSummary.Rows(Results, Options);
        Totals = FormworkSummary.Totals(Results, Options);
        Sheet = FormworkSheet.Build(Results, Options, _placeOf, _partOrder);
        DropCheck = FormworkSummary.CheckDrops(Results, Rules);
        HiddenTypeRules = FormworkSummary.HiddenTypeRules(Results, Rules);
        Pieces = BuildPieces();
        RefreshMilliseconds = watch.ElapsedMilliseconds;
    }

    // 덩이를 짓는다(R48): ① 판 — 시트의 판마다 하나(든 조각 전부) ② 뺀 조각 — 대표 열쇠마다 하나 ③ 그 밖의 줄 조각(RowPatches — 세지 않는 조각 ·
    // 아직 정하지 않은 아래가 빈 밑면)은 (부재 · 유형 · 면의 갈래 · 안팎 · 높이 구간 · 까닭이 있는가 · 맞닿은 벽)마다 하나 — 열쇠가 같으면 색 · 상태가 같다.
    // 줄 조각은 꼭 한 덩이에 든다(세는 조각은 모두 판에 든다 — FormworkSheet 는 세는 조각을 모두 싣는다).
    private List<FormworkPiece> BuildPieces()
    {
        var pieces = new List<FormworkPiece>();
        var keyedOf = new Dictionary<FacePatch, FormworkSummary.Keyed>(ReferenceEqualityComparer.Instance);
        foreach (var k in FormworkSummary.RowPatches(Results)) keyedOf[k.Patch] = k;
        var meshes = new Dictionary<string, MeshData>(StringComparer.Ordinal);
        var placed = new HashSet<FacePatch>(ReferenceEqualityComparer.Instance);

        foreach (var plate in Sheet.Plates)
        {
            var piece = new FormworkPiece { Plate = plate };
            foreach (var m in plate.Members)
                if (keyedOf.TryGetValue(m.Patch, out var k) && placed.Add(m.Patch)) piece.Add(k);
            if (piece.Members.Count == 0) continue;
            piece.LeadIndex = Largest(piece);
            piece.Mesh = MeshFor("plate", piece, meshes, plate.Joined ? plate.Shape : null, plate.Normal);
            pieces.Add(piece);
        }

        var drops = new Dictionary<string, FormworkPiece>(StringComparer.Ordinal);
        var chunks = new Dictionary<(ElementFormwork? r, string type, string group, string side, int band, bool note, int vertical), FormworkPiece>();
        foreach (var k in FormworkSummary.RowPatches(Results))
        {
            var p = k.Patch;
            if (placed.Contains(p)) continue;
            FormworkPiece? piece;
            if (p.DropLead.Length > 0)
            {
                if (!drops.TryGetValue(p.DropLead, out piece)) { drops[p.DropLead] = piece = new FormworkPiece { DropLead = p.DropLead }; pieces.Add(piece); }
            }
            else
            {
                // 까닭은 대응표를 걸기 전의 값으로 본다(대응표가 '세지 않음' 등으로 까닭을 덮어쓰기 때문).
                // 수직면 덩어리의 조각(세지 않는 덩어리 — R49)은 덩어리마다 한 덩이(판 = 덩어리 · 색이 하나).
                bool note = (p.Unruled?.Note ?? p.Note).Length > 0;
                var key = p.Vertical is { } g ? ((ElementFormwork?)null, "", "", "", p.Band, false, g.Id) : (k.Result, k.Type, k.Group, p.Side, p.Band, note, 0);   // 덩어리는 안팎과 상관없이 한 덩이(core-verify R49 L5)
                if (!chunks.TryGetValue(key, out piece)) { chunks[key] = piece = new FormworkPiece(); pieces.Add(piece); }
            }
            piece.Add(k);
            placed.Add(p);
        }
        foreach (var piece in drops.Values)
        {
            // 대표 = 대표 열쇠의 조각(모양이 바뀌어 없으면 가장 넓은 조각).
            int at = piece.Members.FindIndex(m => DropRecord.Of(m.Face, m.Patch).Text == piece.DropLead);
            piece.LeadIndex = at >= 0 ? at : Largest(piece);
            piece.Mesh = MeshFor("drop", piece, meshes, null, null);
        }
        foreach (var piece in chunks.Values) piece.Mesh = MeshFor("chunk", piece, meshes, null, null);
        _meshes = meshes;
        return pieces;
    }

    // 가장 넓은 조각(같으면 부재 Id 가 작은 것 — FormworkMerge 의 대표 조각과 같은 잣대).
    private static int Largest(FormworkPiece piece)
    {
        int best = 0;
        for (int i = 1; i < piece.Members.Count; i++)
        {
            var (a, b) = (piece.Members[i], piece.Members[best]);
            if (a.Patch.Area > b.Patch.Area || (a.Patch.Area == b.Patch.Area && a.Result.Element.Id < b.Result.Element.Id)) best = i;
        }
        return best;
    }

    // 덩이의 겉면 — 든 조각(과 이은 판의 테두리)이 지난번과 같으면 그 겉면을 다시 쓴다.
    private MeshData MeshFor(string kind, FormworkPiece piece, Dictionary<string, MeshData> meshes, FacePatch? outline, double[]? normal)
    {
        string key = kind + ":" + (outline != null ? "j" : "") + string.Join(",", piece.Patches.Select(p => _serial.TryGetValue(p, out int n) ? n : -1).Order());
        if (!_meshes.TryGetValue(key, out var mesh) && !meshes.TryGetValue(key, out mesh)) mesh = MeshOf(piece.Patches, _origin, outline, normal);
        meshes[key] = mesh;
        return mesh;
    }

    // 조각들의 세모를 바깥(세모의 법선 쪽 — 세모는 면의 바깥을 보게 돌아 있다)으로 Lift 만큼 띄워 담고, 테두리를 모서리 선분으로 담는다.
    // outline 을 주면(이은 판) 조각마다의 테두리 대신 합친 테두리 하나를 그린다 — 이음매 선이 없이 엑셀 한 줄 · Revit 판 한 장과 같은 모양.
    private static MeshData MeshOf(IEnumerable<FacePatch> patches, Vec3 origin, FacePatch? outline = null, double[]? normal = null)
    {
        var builder = new MeshBuilder();
        Vec3? first = null;
        foreach (var patch in patches)
        {
            Vec3? up = null;
            for (int i = 0; i + 2 < patch.Triangles.Count; i += 3)
            {
                Vec3 a = Of(patch.Triangles[i]), b = Of(patch.Triangles[i + 1]), c = Of(patch.Triangles[i + 2]);
                var n = (b - a).Cross(c - a);
                if (!(n.Length > 1e-12) || !n.IsFinite) continue;
                var lift = n.Unit() * Lift;
                up ??= n.Unit();
                builder.AddTriangle(a + lift, b + lift, c + lift);
            }
            first ??= up;
            // 테두리(평면 조각만 있다 — 곡면은 세모만): 면과 같이 띄운다. 법선을 모르면(세모가 없는 조각) 테두리도 그리지 않는다.
            if (outline == null && up is { } own) AddOutline(builder, patch.Outline, own);
        }
        if (outline != null && (normal is { Length: 3 } ? new Vec3(normal[0], normal[1], normal[2]).Unit() : first) is { } plane)
            AddOutline(builder, outline.Outline, plane);
        return builder.Build(origin);
    }

    private static void AddOutline(MeshBuilder builder, List<List<double[]>> rings, Vec3 up)
    {
        var raise = up * Lift;
        foreach (var loop in rings)
            for (int i = 0; i < loop.Count; i++)
                builder.AddSegment(Of(loop[i]) + raise, Of(loop[(i + 1) % loop.Count]) + raise, up, up);
    }

    private static Vec3 Of(double[] p) => new(p[0], p[1], p[2]);
}
