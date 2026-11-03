namespace DH.Takeoff.Core.Formwork;

/// <summary>
/// R73b — ② 붓(JACK 1030 «초보도 원하는 위치를 쉽게 선택해서 거푸집으로 설정» · 1103 «계단 옆면처럼 쪼개지지 않게» · 계획 §3.2 · §10.1):
///  · 붓 단위 = 면 하나 = 한 면처럼 볼 무리(BoardFace.Siblings — 판이 여럿이어도). 칠하기 = 무리의 칠할 수 있는 조각 모두에 «면 종류» 한 기록 ·
///    «산출 제외» = 그 면 크기의 지운 판 한 기록 · ↺ = 그 면의 기록만 걷음(처음 값).
///  · 적을 때 하나만(검토 H1 · H2): 쓰기 전에 그 면 조각의 열쇠만 판 종류 · 면 종류 · 지운 판 · 옮긴 판 기록에서 뺀다(FormworkRules.RemovePatchRecords — 판 통째로 되살리지 않음).
///  · 칠할 수 없는 조각(땅 위 밑면 · 틈 · 윗면 · 다 덮인 면 — 형상이 세지 않는 것)은 건너뛴다 · 지금과 같은 종류면 기록을 쓰지 않는다 · 다시 세기는 한 번.
///  · 쉬는 기록(FormworkRules.Sleeping)이면 아무것도 하지 않고 NeedsDecision(화면이 세 갈래 창 — Wake 뒤 다시).
/// 수량은 칠할 때만 바뀐다(칠하기 전 시트 · 합계는 한 글자도 그대로).
/// </summary>
public sealed partial class FormworkBoard
{
    /// <summary>칠한 결과: 칠한 면 · 칠할 수 없는 면(조각이 모두 형상이 세지 않는 것 · 기록이 듣지 않음) · 이미 그 종류인 면 · 쉬는 기록이라 묻기.</summary>
    public sealed record PaintOutcome(int Painted, int Skipped, int Same, bool NeedsDecision)
    {
        public static readonly PaintOutcome None = new(0, 0, 0, false);
    }

    /// <summary>붓이 칠할 수 있는 조각인가(판 종류 · 지운 판과 같은 잣대 — 형상이 세는 조각 · 아래가 빈 밑면).</summary>
    public static bool Paintable(FacePatch patch) => (patch.Unruled?.Counted ?? patch.Counted) || patch.Below == Below.Floating;

    /// <summary>그 면(무리)이 붓으로 칠할 수 있는 조각을 가졌는가.</summary>
    public static bool Paintable(BoardFace face) => PatchesOf(face).Any(x => Paintable(x.patch));

    // 무리의 진짜 조각(다 덮인 면의 보기용 조각은 뺌 — 수량 · 기록과 상관없음).
    private static List<(FaceResult face, FacePatch patch)> PatchesOf(BoardFace face) =>
        face.Siblings.Where(f => !f.Covered).SelectMany(f => f.Patches.Select(p => (f.Face, p))).ToList();

    /// <summary>
    /// 면들(무리마다 한 번)을 그 종류로 칠한다 — item = 대응표 종류 · «세지 않음»(산출 제외). 기록을 쓰고 한 번 다시 센다. 되돌리기 · 저장은 부르는 쪽(Snapshot 을 먼저 떠 둔다).
    /// </summary>
    public PaintOutcome Paint(IReadOnlyList<BoardFace> faces, string item)
    {
        string value = (item ?? "").Trim();
        var groups = faces.Select(f => f.Siblings[0]).Distinct().ToList();
        if (value.Length == 0 || groups.Count == 0) return PaintOutcome.None;
        if (Blocked()) return new PaintOutcome(0, 0, 0, true);
        bool drop = value == FormworkRules.NotCounted;
        string before = Snapshot();
        bool switched = EnsureUser();
        var written = new List<string>();
        var plans = new List<(string key, List<(FaceResult face, FacePatch patch)> mine, List<DropRecord> records)>();
        int skipped = 0, same = 0;
        foreach (var group in groups)
        {
            var mine = PatchesOf(group).Where(x => Paintable(x.patch)).ToList();
            if (mine.Count == 0) { skipped++; continue; }
            // 지금과 같으면 쓰지 않는다: 종류 = 모두 그 종류로 셈 · 산출 제외 = 모두 세지 않고 정한 것(아직 정하지 않은 아래가 빈 밑면은 칠하면 정해진다).
            if (mine.All(x => drop ? !x.patch.Counted && !x.patch.NeedsCheck : x.patch.Counted && x.patch.Item == value)) { same++; continue; }
            var records = mine.Select(x => DropRecord.Of(x.face, x.patch)).Distinct().ToList();
            var lead = mine.OrderByDescending(x => x.patch.Area).First();
            plans.Add((DropRecord.Of(lead.face, lead.patch).Text, mine, records));
        }
        void Write(IEnumerable<(string key, List<(FaceResult face, FacePatch patch)> mine, List<DropRecord> records)> list)
        {
            foreach (var (key, mine, records) in list)
            {
                Rules.RemovePatchRecords(mine);
                if (drop) Rules.Drop(key, records);
                else Rules.SetFaceKind(key, value, records);
                written.Add(key);
            }
        }
        Write(plans);
        if (written.Count == 0)
        {
            // 쓴 것이 없다 — 모드 줄만 바뀌었으면 되돌린다(셈은 그대로라 다시 세지 않는다).
            if (Snapshot() != before) Rules.CopyFrom(FormworkRules.Parse(before));
            ModeSwitched = false;
            return new PaintOutcome(0, skipped, same, false);
        }
        Reclassify();
        Refresh();
        bool Took(string key) => drop ? AllPatches.Any(p => p.DropLead == key) : AllPatches.Any(p => p.FaceKindLead == key && p.Counted && p.Item == value);
        var stuck = written.Where(k => !Took(k)).ToList();
        if (stuck.Count == written.Count)
        {
            RestoreSnapshot(before);
            return new PaintOutcome(0, skipped + stuck.Count, same, false);
        }
        if (stuck.Count > 0)
        {
            // 듣지 않은 면은 처음 기록 그대로(설치 전 검토 R73b 중간 3 — 새 기록만 걷으면 먼저 뺀 판 종류 · 지운 판 몫이 돌아오지 않았다): 처음 글로 되돌린 뒤 들은 면만 다시 쓴다.
            RestoreSnapshot(before);
            EnsureUser();
            written.Clear();
            Write(plans.Where(x => !stuck.Contains(x.key)));
            Reclassify();
            Refresh();
        }
        ModeSwitched = switched;
        return new PaintOutcome(written.Count, skipped + stuck.Count, same, false);
    }

    /// <summary>
    /// 면들의 ↺ 처음 값(R73b): 그 면 조각의 열쇠만 면 종류 · 판 종류 · 지운 판 · 옮긴 판 기록에서 뺀다(판 · 면 기록을 걸기 전 값 — 줄 규칙이 있는 모델이면 그 값).
    /// 걷은 기록이 있는 면(무리) 수 · 다시 세기는 한 번.
    /// </summary>
    public int ResetFaces(IReadOnlyList<BoardFace> faces)
    {
        var groups = faces.Select(f => f.Siblings[0]).Distinct().ToList();
        if (groups.Count == 0 || Blocked()) return 0;
        string before = Snapshot();
        if (Rules.UserMode && !Rules.ModeSet) Rules.UserMode = true;   // 기록으로 짐작한 사용자 모드를 적어 둔다(마지막 기록을 걷어도 «자동»으로 넘어가지 않게 — AutoPlates 와 같이)
        int n = groups.Count(g => Rules.RemovePatchRecords(PatchesOf(g)) > 0);
        if (n == 0)
        {
            if (Snapshot() != before) Rules.CopyFrom(FormworkRules.Parse(before));
            return 0;
        }
        ModeSwitched = false;
        Reclassify();
        Refresh();
        return n;
    }

    /// <summary>
    /// 그 면의 «처음 값»(↺ 풍선 글 — R73b): 칠하기 · 판 종류를 걸기 전 종류(지운 · 옮긴 판이면 지우기 전 종류) · 세지 않던 면이면 «세지 않음». 걸린 기록이 없으면 지금 종류.
    /// </summary>
    public static string FirstKindOf(BoardFace face)
    {
        var mine = PatchesOf(face).Where(x => Paintable(x.patch)).Select(x => x.patch).ToList();
        if (mine.Count == 0) return FormworkRules.NotCounted;
        string Base(FacePatch p) =>
            p.FaceKindLead.Length > 0 ? p.FaceKindBase
            : p.KindLead.Length > 0 ? p.KindBase
            : p.MoveLead.Length > 0 ? p.BeforeMove
            : p.DropLead.Length > 0 ? ((p.Unruled?.Counted ?? false) ? p.Item : "")
            : p.Counted ? p.Item : "";
        var kinds = mine.GroupBy(p => Base(p)).OrderByDescending(g => g.Sum(p => p.Area)).Select(g => g.Key).ToList();
        string first = kinds[0];
        return first.Length > 0 ? first : FormworkRules.NotCounted;
    }
}
