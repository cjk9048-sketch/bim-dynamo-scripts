namespace DH.Takeoff.Core.Formwork;

/// <summary>
/// R66 — ② 판마다 지정(JACK 2026-10-27 «모든 면에 대해 거푸집(판)을 먼저 만들고 그다음 지정 · 처음부터 사용자 모드 / 자동 모드»):
///  · 자동 모드 = 규칙의 종류만(기록은 남지만 걸지 않음 — FormworkOptions.ActiveRules) · 사용자 모드 = 자동 결과에서 시작해 판마다 고침(판 기록 &gt; 줄 규칙 &gt; 자동).
///  · 판 하나 · 여러 장을 골라: 종류(판 종류 기록 — 판마다 한 기록이라 판 경계는 그대로 · 계획 검토 H2) · 세지 않음(지운 판) · 자동으로(↺ — 그 판의 판 기록을 걷음) ·
///    부재마다 나누기(판 쪼갬) · 합치기(쪼갬을 걷고, 그래도 따로면 넓은 판의 종류로 한 기록 — JACK 고름) · 자리(엑셀 부위 줄 · 중분류 — 판 자리).
///  · 자동 모드에서 고치면 사용자 모드로 바꾼다(계획 검토 H3 · M3 — ModeSwitched 로 화면이 알린다).
/// 되돌리기는 기록 글 전체(Snapshot · RestoreSnapshot — 모드까지).
/// </summary>
public sealed partial class FormworkBoard
{
    /// <summary>사용자 모드인가(R66 — FormworkRules.UserMode).</summary>
    public bool UserMode => Rules.UserMode;

    /// <summary>마지막으로 고친 일이 자동 모드를 사용자 모드로 바꿨는가(화면이 알린다).</summary>
    public bool ModeSwitched { get; private set; }

    // 고치기 전에: 자동 모드면 사용자 모드로. 바꿨으면 true.
    private bool EnsureUser()
    {
        if (Rules.UserMode)
        {
            if (!Rules.ModeSet) Rules.UserMode = true;   // 기록으로 짐작한 사용자 모드를 적어 둔다(기록을 다 걷어도 사용자 모드로 남게)
            return false;
        }
        Rules.UserMode = true;
        return true;
    }

    /// <summary>모드를 바꾼다(기록은 그대로 — 자동이면 걸지 않음). 바뀌었으면 true(다시 센다).</summary>
    public bool SetMode(bool user)
    {
        ModeSwitched = false;
        bool before = Rules.UserMode;
        Rules.UserMode = user;
        if (before == user) return false;
        Reclassify();
        Refresh();
        return true;
    }

    /// <summary>기록 글 전체(되돌리기 — 모드까지).</summary>
    public string Snapshot() => Rules.ToText();

    /// <summary>기록을 그 글로 되돌리고 다시 센다.</summary>
    public void RestoreSnapshot(string text)
    {
        Rules.CopyFrom(FormworkRules.Parse(text));
        ModeSwitched = false;
        Reclassify();
        Refresh();
    }

    /// <summary>판의 조각 열쇠(Members 차례)와 대표 열쇠(대표 조각의 열쇠 — 지금 판 기록 · Revit 판과 같은 잣대).</summary>
    public static (string lead, List<DropRecord> records) RecordsOf(FormworkPiece piece)
    {
        var records = piece.Members.Select(m => DropRecord.Of(m.Face, m.Patch)).ToList();
        return (records.Count > 0 ? records[piece.LeadIndex].Text : "", records);
    }

    private static List<(FaceResult face, FacePatch patch)> FacesOf(FormworkPiece piece) => piece.Members.Select(m => (m.Face, m.Patch)).ToList();

    /// <summary>사람이 판마다 정한 기록이 든 판인가(R66 «직접» — 지운 판 · 옮긴 판 · 판 종류 · 판 자리 · 판 쪼갬). 대응표 줄의 값은 아니다(RowRuled).</summary>
    public static bool Direct(FormworkPiece piece) =>
        piece.Dropped || piece.Patches.Any(p => p.MoveLead.Length > 0 || p.KindLead.Length > 0 || p.PlaceLead.Length > 0 || p.SplitLead.Length > 0);

    /// <summary>판의 종류를 대응표 줄(유형 · 부위 줄 · 아래 줄)의 사람 값이 정하는가(사용자 모드 · 판 기록이 없는 판 — 계획 검토 M1 «줄 규칙으로»).</summary>
    public bool RowRuled(FormworkPiece piece)
    {
        if (Options.ActiveRules is not { Count: > 0 } rules || piece.Dropped || piece.Members.Count == 0 || piece.KindLead.Length > 0) return false;
        var lead = piece.LeadMember;
        return FormworkAnalyzer.HumanRule(rules, lead.Result.Element, lead.Group, lead.Patch.Sub) != null;
    }

    /// <summary>부재마다 나눌 수 있는 판인가(판 · 든 부재가 둘 이상).</summary>
    public static bool Splittable(FormworkPiece piece) =>
        piece is { Plate: not null, Dropped: false } && piece.Members.Select(m => m.Result.Element.Id).Distinct().Count() > 1;

    /// <summary>
    /// 고른 판들의 종류를 정한다(판마다 판 종류 기록 하나 — 판 경계 · 대표는 그대로). 지운 판 · 옮긴 판은 그 기록을 걷고 센다.
    /// «세지 않음»은 DropPlates. 바꾼 판 수(0 = 아무 일 없음 — 기록이 맞지 않으면 되돌린다). 듣지 않은 판의 기록은 걷는다(설치 전 검토 L3).
    /// </summary>
    public int SetKinds(IReadOnlyList<FormworkPiece> pieces, string item)
    {
        string value = (item ?? "").Trim();
        if (value.Length == 0 || value == FormworkRules.NotCounted) return 0;
        string before = Snapshot();
        bool switched = EnsureUser();
        var leads = new List<(string lead, List<DropRecord> records, FormworkPiece piece)>();
        foreach (var piece in pieces.Where(p => p.Members.Count > 0).Distinct())
        {
            var (lead, records) = RecordsOf(piece);
            Rules.Forget(FacesOf(piece), drops: true, kinds: true, places: false, splits: false);
            Rules.SetKind(lead, value, records);
            leads.Add((lead, records, piece));
        }
        bool Took((string lead, List<DropRecord> records, FormworkPiece piece) x) => AllPatches.Any(p => p.KindLead == x.lead && p.Counted && p.Item == value);
        int n = Finish(before, switched, () => leads.Count(Took));
        if (n > 0) foreach (var x in leads.Where(x => !Took(x))) Rules.RemoveKindRecords(x.lead, x.records);
        return n;
    }

    /// <summary>고른 판들을 세지 않는다(지운 판 — 판마다 한 기록 · 옮긴 판은 옮긴 줄만 걷음). 지운 판 수 · 듣지 않은 판의 기록은 걷는다(L3).</summary>
    public int DropPlates(IReadOnlyList<FormworkPiece> pieces)
    {
        string before = Snapshot();
        bool switched = EnsureUser();
        var leads = new List<(string lead, bool moved, FormworkPiece piece)>();
        foreach (var piece in pieces.Where(p => p.Members.Count > 0 && !p.Dropped).Distinct())
        {
            if (piece.Moved) { Rules.SetMove(piece.MoveLead, null); leads.Add((piece.MoveLead, true, piece)); continue; }
            var (lead, records) = RecordsOf(piece);
            Rules.Drop(lead, records);
            leads.Add((lead, false, piece));
        }
        bool Took((string lead, bool moved, FormworkPiece piece) x) => AllPatches.Any(p => p.DropLead == x.lead);
        int n = Finish(before, switched, () => leads.Count(Took));
        if (n > 0) foreach (var x in leads.Where(x => !x.moved && !Took(x))) Rules.Restore(x.lead);
        return n;
    }

    /// <summary>고른 판들을 자동으로(↺ — 그 판의 판 기록을 모두 걷음 · 대응표 줄은 그대로). 걷은 기록이 있는 판 수.</summary>
    public int AutoPlates(IReadOnlyList<FormworkPiece> pieces)
    {
        string before = Snapshot();
        if (Rules.UserMode && !Rules.ModeSet) Rules.UserMode = true;   // 기록으로 짐작한 사용자 모드를 적어 둔다 — 마지막 기록을 걷어도 «자동»으로 넘어가지 않게(설치 전 검토 L1)
        int n = pieces.Where(p => p.Members.Count > 0).Distinct().Count(piece => Rules.Forget(FacesOf(piece), joints: true) > 0);   // ↺ 만 «겹침» 기록도 걷는다(R67)
        return Finish(before, false, () => n);
    }

    /// <summary>고른 판들을 부재마다 나눈다(든 부재가 둘 이상인 판만). 나눈 판 수(나뉘지 않은 판은 기록을 되돌린다).</summary>
    public int SplitPlates(IReadOnlyList<FormworkPiece> pieces)
    {
        string before = Snapshot();
        bool switched = EnsureUser();
        var split = new List<(string lead, List<DropRecord> records)>();
        foreach (var piece in pieces.Where(Splittable).Distinct())
        {
            var (lead, records) = RecordsOf(piece);
            Rules.SetSplit(lead, records);
            split.Add((lead, records));
        }
        if (split.Count == 0) return Finish(before, switched, () => 0);
        Reclassify();
        Refresh();
        // 나뉜 판 = 그 판의 조각이 이제 둘 이상의 판에 든다 · 나뉘지 않은 판의 쪼갬 기록은 걷는다(설치 전 검토 L6 — 효과 없는 기록이 남지 않게).
        // 지금 결과의 조각으로 본다(다시 쟀으면 넘겨받은 판의 조각은 옛것 — 최종 검토 높음 2).
        var stuck = split.Where(x => PiecesOfRecords(x.records).Count <= 1).ToList();
        int done = split.Count - stuck.Count;
        if (done == 0)
        {
            RestoreSnapshot(before);
            return 0;
        }
        foreach (var (lead, _) in stuck) Rules.RemoveSplit(lead);
        ModeSwitched = switched;
        return done;
    }

    /// <summary>
    /// 고른 판들을 한 판으로 합친다(JACK 고름 «넓은 판의 종류»): ① 판 쪼갬 기록을 걷는다 — 쪼갠 판이면 다시 한 판 ② 그래도 따로면 고른 판 전부에 판 종류 기록 하나
    /// (넓은 판의 종류)를 걸어 같은 판 기록끼리 잇게 한다. 한 판이 되지 않으면(같은 평면 · 맞닿음 · 같은 부위 · 같은 쪽이 아님) 기록을 되돌린다.
    /// 판 자리는 넓은 판의 것으로 맞춘다(설치 전 검토 L5 — 합친 판의 넓이가 한쪽 판의 자리를 말없이 따라가지 않게). 돌려주는 값 = "" 이면 됨, 아니면 까닭.
    /// </summary>
    public string MergePlates(IReadOnlyList<FormworkPiece> pieces)
    {
        var plates = pieces.Where(p => p.Plate != null && !p.Dropped && p.Members.Count > 0).Distinct().ToList();
        if (plates.Count < 2) return "판을 두 장 이상 고르세요";
        var wide = plates.OrderByDescending(p => p.Area).ThenBy(p => p.Result.Element.Id).First();
        var wideRecords = RecordsOf(wide);
        var widePlace = wide.Patches.Select(p => p.Place).FirstOrDefault(x => x != null);
        string item = wide.Lead.Item;
        bool counted = wide.Lead.Counted;
        string before = Snapshot();
        bool switched = EnsureUser();
        var faces = plates.SelectMany(FacesOf).ToList();
        var records = plates.SelectMany(p => RecordsOf(p).records).ToList();
        bool placed = plates.Any(p => p.Patches.Any(q => q.PlaceLead.Length > 0));
        // 자리 맞추기: 고른 판의 자리 기록을 걷고 넓은 판의 자리를 전부에(자리 기록이 있었을 때만).
        void SamePlace()
        {
            if (!placed) return;
            Rules.Forget(faces, drops: false, kinds: false, places: true, splits: false);
            if (widePlace is { } wp) Rules.SetPlace(wideRecords.lead, wp.Part, wp.Zone, records);
        }
        if (Rules.Forget(faces, drops: false, kinds: false, places: false, splits: true) > 0)
        {
            SamePlace();
            Reclassify();
            Refresh();
            if (Together(records)) { ModeSwitched = switched; return ""; }
        }
        if (item.Length == 0 || !counted)
        {
            RestoreSnapshot(before);
            return "넓은 판의 종류를 모릅니다";
        }
        Rules.Forget(faces, drops: false, kinds: true, places: false, splits: true);
        Rules.SetKind(wideRecords.lead, item, records);
        SamePlace();
        Reclassify();
        Refresh();
        if (Together(records)) { ModeSwitched = switched; return ""; }
        RestoreSnapshot(before);
        return "한 판으로 이을 수 없는 판입니다(같은 평면에 맞닿고 · 같은 부위 · 같은 쪽인 판만)";
    }

    // 열쇠들의 조각이 모두 한 판(덩이)에 드는가(지금 결과의 조각으로 — 최종 검토 높음 2).
    private bool Together(List<DropRecord> records)
    {
        var at = PiecesOfRecords(records);
        return at.Count == 1 && Pieces[at[0]].Plate != null;
    }

    // 지금 결과의 조각(다시 재면 새것 — 넘겨받은 판의 조각이 아니라 이것으로 판정한다).
    private IEnumerable<FacePatch> AllPatches => Results.SelectMany(r => r.Faces).SelectMany(f => f.Patches);

    // 열쇠들에 맞는 지금 조각이 든 덩이들.
    private List<int> PiecesOfRecords(IReadOnlyList<DropRecord> records)
    {
        var found = new List<int>();
        var byElement = records.GroupBy(r => r.Element).ToDictionary(g => g.Key, g => g.ToList());
        foreach (var r in Results)
            if (byElement.TryGetValue(r.Element.Id, out var mine))
                foreach (var f in r.Faces)
                    foreach (var p in f.Patches)
                        if (mine.Any(rec => rec.Matches(f, p)) && IndexOf(p) is int at && at >= 0 && !found.Contains(at)) found.Add(at);
        return found;
    }

    /// <summary>
    /// 고른 판들의 엑셀 자리(부위 줄 · 중분류 — 빈 값 = 그대로)를 정한다. 둘 다 비면 자리 기록을 걷는다(걷을 것이 없으면 아무 일 없음 — 설치 전 검토 M2:
    /// 자동 모드에서 «그대로 · 그대로»를 눌러 사용자 모드로 바뀌며 쉬던 기록이 다시 걸리지 않게). 바꾼 판 수.
    /// </summary>
    public int PlacePlates(IReadOnlyList<FormworkPiece> pieces, string part, string zone)
    {
        string before = Snapshot();
        bool clear = (part ?? "").Trim().Length == 0 && (zone ?? "").Trim().Length == 0;
        var plates = pieces.Where(p => p.Plate != null && !p.Dropped && p.Members.Count > 0).Distinct().ToList();
        if (clear)
        {
            int forgot = plates.Count(piece => Rules.Forget(FacesOf(piece), drops: false, kinds: false, places: true, splits: false) > 0);
            return Finish(before, false, () => forgot);
        }
        bool switched = EnsureUser();
        var leads = new List<(string lead, FormworkPiece piece)>();
        foreach (var piece in plates)
        {
            var (lead, records) = RecordsOf(piece);
            Rules.Forget(FacesOf(piece), drops: false, kinds: false, places: true, splits: false);
            Rules.SetPlace(lead, part ?? "", zone ?? "", records);
            leads.Add((lead, piece));
        }
        return Finish(before, switched, () => leads.Count(x => AllPatches.Any(p => p.PlaceLead == x.lead)));
    }

    /// <summary>② 판 목록의 한 줄(R66): 엑셀 줄(Line 이 있음) · 확인할 자리(덩이 하나) · 세지 않는 면(부재 · 면 · 까닭이 같은 덩이 묶음).</summary>
    /// <param name="Group">묶음 제목(엑셀 줄 = 절 이름 · 그 밖 = «확인할 자리» · «세지 않는 면»).</param>
    /// <param name="Title">작은 제목(엑셀 줄 = 부위 제목 « - 작은 제목» · 그 밖 = "").</param>
    /// <param name="Value">값(엑셀 줄 = 식 값 × n — 엑셀과 같은 반올림 · 그 밖 = 넓이).</param>
    public sealed record PlateLine(string Group, string Title, string Text, double Value, IReadOnlyList<int> Pieces, FormworkLine? Line);

    public const string CheckGroup = "확인할 자리", UncountedGroup = "세지 않는 면";

    /// <summary>
    /// ② 판 목록(R66 — JACK «판을 먼저 만들고 그다음 지정» · ③ 산출서와 같은 차례): 엑셀 줄마다(절 → 부위 → 부호 — 줄의 판들) · 그다음 확인할 자리(분홍 덩이마다) ·
    /// 세지 않는 면(부재 · 면 갈래 · 까닭마다). 지운 판은 화면이 따로 맨 아래에 둔다.
    /// </summary>
    public List<PlateLine> PlateLines()
    {
        var list = new List<PlateLine>();
        foreach (var line in Sheet.Lines)
        {
            var pieces = PiecesOfLine(line).Select(x => x.piece).Distinct().ToList();
            if (pieces.Count == 0) continue;
            string title = line.Part + (line.Sub.Length > 0 ? " - " + line.Sub : "");
            string head = line.Mark.Length > 0 ? line.Mark + (line.Tag.Length > 0 ? $" ({line.Tag})" : "") + " : " : "";
            double value;
            try { value = FormulaDecomposer.RoundQuantity(FormulaEvaluator.Evaluate(line.Formula) * line.Count); }
            catch (FormatException) { value = line.Bim; }
            list.Add(new PlateLine(line.Section, title, head + FormworkLabels.Pretty(line.Formula) + (line.Count > 1 ? $" × {line.Count}" : ""), value, pieces, line));
        }
        for (int i = 0; i < Pieces.Count; i++)
            if (Pieces[i] is { Plate: null, Dropped: false } piece && piece.Lead.NeedsCheck)
                list.Add(new PlateLine(CheckGroup, "", ChunkText(piece) + (piece.Lead.Note.Length > 0 ? " · " + piece.Lead.Note : ""), piece.Area, new[] { i }, null));
        var uncounted = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        for (int i = 0; i < Pieces.Count; i++)
            if (Pieces[i] is { Plate: null, Dropped: false } piece && !piece.Lead.NeedsCheck)
            {
                string note = piece.Lead.Note.Length > 0 ? piece.Lead.Note : FormworkRules.NotCounted;
                string key = ChunkText(piece) + " · " + note;
                if (!uncounted.TryGetValue(key, out var at)) uncounted[key] = at = new List<int>();
                at.Add(i);
            }
        foreach (var (text, pieces) in uncounted.OrderBy(kv => kv.Key, StringComparer.Ordinal))
            list.Add(new PlateLine(UncountedGroup, "", text + (pieces.Count > 1 ? $" · {pieces.Count}곳" : ""), pieces.Sum(i => Pieces[i].Area), pieces, null));
        return list;
    }

    // 판이 아닌 덩이의 이름: «{부위 · 없으면 유형} · {면 갈래}({안팎})».
    private static string ChunkText(FormworkPiece piece)
    {
        var el = piece.Result.Element;
        string side = piece.Side.Length > 0 ? $"({piece.Side})" : "";
        return $"{(el.Part.Length > 0 ? el.Part : el.Type)} · {piece.Group}{side}";
    }

    // 다시 세고 바뀐 판 수를 잰다(done — 다시 센 뒤의 조각으로). 하나도 듣지 않았으면 기록 · 모드를 되돌린다.
    private int Finish(string before, bool switched, Func<int> done)
    {
        ModeSwitched = false;
        Reclassify();
        Refresh();
        int n = done();
        if (n == 0)
        {
            if (Snapshot() != before) RestoreSnapshot(before);
            return 0;
        }
        ModeSwitched = switched;
        return n;
    }
}
