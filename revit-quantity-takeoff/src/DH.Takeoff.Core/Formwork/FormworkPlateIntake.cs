namespace DH.Takeoff.Core.Formwork;

/// <summary>
/// Revit 판의 종류 칸을 받아들인다(R57 P3 — JACK 2026-10-23 «Revit 판의 종류를 고치면 그 판 한 장만» · 판을 지우면 «다시 만들고 알림»).
/// [수량산출]을 열 때 판마다 «지을 때 적은 종류(기록)»와 «지금 종류 칸»을 견줘 대응표에 사람 판단으로 넣는다 — Revit 을 모르는 순수 함수(시험할 수 있게 · 계획 9.5).
///  · 기록이 없는 판(P3 전에 지은 판) · 복사한 판(기록의 UniqueId 가 다름) · 다른 사용자가 가진 판은 받아들이지 않고 센다(알림).
///  · 종류 칸 = «세지 않음» → 지운 판(② 의 지우기와 같은 것) · 대응표 종류 → 그 판 한 장의 판 종류 · Base(판 종류를 걸기 전 종류)로 되돌렸으면 그 판 몫만 판 종류 기록에서 뺀다
///    (같은 기록의 다른 판은 그대로 — 다시 검토 낮음 1) · 그 밖의 글 → 쓰지 않음(알림).
///  · 받아들인 판은 부르는 쪽이 기록의 종류를 지금 값으로 고친다(한 번만 받아들임 — 뒤에 ② 에서 [원래 종류로]를 눌러도 다음 열기에서 다시 덮지 않게).
/// </summary>
public static class FormworkPlateIntake
{
    /// <summary>판 하나에서 본 것.</summary>
    /// <param name="Recorded">판에 처음 값 기록이 있다(P3 뒤에 지은 판).</param>
    /// <param name="Copy">복사한 판(기록의 UniqueId 가 판의 것과 다름).</param>
    /// <param name="Locked">고칠 수 없는 판(다른 사용자가 가짐 · 그룹 안 …) — 기록을 고칠 수 없으니 받아들이지 않는다.</param>
    /// <param name="KindNow">지금 종류 칸의 글.</param>
    /// <param name="KindStored">지을 때(또는 마지막으로 받아들일 때) 적은 종류.</param>
    /// <param name="Base">판 종류를 걸기 전 종류(판 안의 조각이 모두 같을 때만 — 아니면 "").</param>
    /// <param name="Lead">판의 대표 조각 열쇠.</param>
    /// <param name="KindLead">지을 때 그 판을 정한 판 종류 기록의 대표 열쇠("" = 없음).</param>
    /// <param name="Records">판에 든 조각 열쇠들.</param>
    /// <param name="ValuesChanged">종류 칸 밖의 칸(부위 · 식 · 넓이 …)이 지을 때와 다르다 — 쓰지 않는 칸(알림만).</param>
    public sealed record Seen(long Id, bool Recorded, bool Copy, bool Locked, string KindNow, string KindStored, string Base, string Lead, string KindLead,
                              IReadOnlyList<DropRecord> Records, bool ValuesChanged);

    /// <summary>받아들인 결과.</summary>
    public sealed class Outcome
    {
        public List<(long id, string kind, string kindLead)> Taken = new();   // 받아들인 판 · 그 종류 · 이제 그 판을 정하는 판 종류 기록(판 기록의 Kind · KindLead 를 이 값으로 고친다)
        public int Empty;                                    // 판 기록에 조각 열쇠가 없어 받아들이지 못한 판
        public int Unrecorded;                               // 기록 없는 판(P3 전에 지음 — 종류를 바꿔도 읽지 않음)
        public int Copies;                                   // 복사한 판
        public int Locked;                                   // 종류가 바뀌었지만 고칠 수 없어 받아들이지 못한 판
        public int ValuesChanged;                            // 다른 칸이 바뀐 판(쓰지 않음)
        public List<(long id, string text)> Unknown = new(); // 대응표 종류가 아닌 글
        public List<string> Log = new();                     // 판마다 한 줄(일지)
    }

    /// <summary>
    /// 판들을 견줘 rules 에 받아들인다(rules 를 고친다 — 부르는 쪽은 사본을 준다). 판 Id 차례(같은 조각을 두 판이 덮으면 나중 판이 이긴다 — 판 종류 기록의 차례).
    /// </summary>
    public static Outcome Apply(IEnumerable<Seen> plates, FormworkRules rules)
    {
        var outcome = new Outcome();
        foreach (var p in plates.OrderBy(p => p.Id))
        {
            if (!p.Recorded) { outcome.Unrecorded++; continue; }
            if (p.Copy) { outcome.Copies++; continue; }
            if (p.ValuesChanged) outcome.ValuesChanged++;
            string now = p.KindNow.Trim();
            if (now == p.KindStored.Trim()) continue;
            string? kind = KindOf(now);
            if (kind == null) { outcome.Unknown.Add((p.Id, now)); outcome.Log.Add($"판 {p.Id}\t모르는 종류 «{now}» — 쓰지 않음"); continue; }
            if (kind == (KindOf(p.KindStored) ?? p.KindStored.Trim())) continue;   // 띄어쓰기만 다름
            if (p.Locked) { outcome.Locked++; outcome.Log.Add($"판 {p.Id}\t{p.KindStored} → {kind} — 고칠 수 없는 판이라 받아들이지 못함"); continue; }
            if (p.Records.Count == 0 || p.Lead.Length == 0) { outcome.Empty++; outcome.Log.Add($"판 {p.Id}\t판 기록에 조각 열쇠가 없음 — 받아들이지 못함"); continue; }
            string what, kindLead = p.KindLead;
            if (kind == FormworkRules.NotCounted)
            {
                // ② 에서 지우거나 옮긴 기록이 이미 이 판의 조각을 덮으면 그 기록으로(옮긴 판이면 지운 판으로 — 같은 조각에 지운 판 기록이 둘 생기지 않게 · P3 검토 중간 2).
                var had = rules.Drops.FirstOrDefault(d => d.records.Any(r => p.Records.Contains(r)));
                if (had.lead != null)
                {
                    if (rules.MoveOf(had.lead) != null) rules.SetMove(had.lead, null);
                    what = "지운 판(있던 기록)";
                }
                else
                {
                    rules.Drop(p.Lead, p.Records);
                    what = "지운 판";
                }
            }
            else if (p.Base.Length > 0 && kind == p.Base)
            {
                // 원래 종류로: 그 판을 정하던 기록(지을 때의 KindLead · 앞서 받아들인 Lead)에서 그 판 몫만 뺀다. 열쇠가 맞지 않아 뺀 것이 없으면(모양이 바뀌어 열쇠가 달라짐)
                // 그 종류를 판 종류로 적는다 — 셈이 Revit 판과 같게(P3 검토 높음: 아무것도 안 바뀌고 «받아들였습니다»만 뜨지 않게).
                bool fromKind = p.KindLead.Length > 0 && rules.RemoveKindRecords(p.KindLead, p.Records);
                bool fromLead = p.Lead != p.KindLead && rules.RemoveKindRecords(p.Lead, p.Records);
                if (fromKind || fromLead) { kindLead = ""; what = "원래 종류로(그 판 몫만)"; }
                else
                {
                    rules.SetKind(p.Lead, kind, p.Records);
                    kindLead = p.Lead;
                    what = "판 종류(원래 종류 — 그 판의 기록을 찾지 못해 판 종류로 적음)";
                }
            }
            else
            {
                // 전에 그 판을 정하던 다른 기록에서는 그 판 몫을 뺀다(같은 조각에 기록이 둘 남지 않게 — P3 검토 중간 1).
                if (p.KindLead.Length > 0 && p.KindLead != p.Lead) rules.RemoveKindRecords(p.KindLead, p.Records);
                rules.SetKind(p.Lead, kind, p.Records);
                kindLead = p.Lead;
                what = "판 종류";
            }
            outcome.Taken.Add((p.Id, kind, kindLead));
            outcome.Log.Add($"판 {p.Id}\t{p.KindStored} → {kind}\t{what}");
        }
        return outcome;
    }

    /// <summary>종류 칸의 글 → 대응표 종류(띄어쓰기는 보지 않음 — «합판3회» = «합판 3회»). 모르는 글이면 null.</summary>
    public static string? KindOf(string text)
    {
        string bare = text.Replace(" ", "", StringComparison.Ordinal);
        return bare.Length == 0 ? null : FormworkTable.Items.FirstOrDefault(i => i.Replace(" ", "", StringComparison.Ordinal) == bare);
    }

    /// <summary>
    /// 판의 처음 값 기록에 적을 것(지을 때 — FormworkObjects.Rebuild): 대표 조각 열쇠 · 조각 열쇠들 · 판 종류를 걸기 전 종류(조각이 모두 같을 때만 —
    /// 다시 검토 낮음 2) · 그 판을 정한 판 종류 기록.
    /// </summary>
    public static (string lead, List<DropRecord> records, string baseKind, string kindLead) RecordOf(FormworkUnit unit)
    {
        var records = unit.Members.Select(m => DropRecord.Of(m.Face, m.Patch)).Distinct().ToList();
        var leadMember = unit.Lead is { } lp ? unit.Members.FirstOrDefault(m => ReferenceEquals(m.Patch, lp)) : default;
        if (leadMember.Patch == null && unit.Members.Count > 0) leadMember = unit.Members.OrderByDescending(m => m.Patch.Area).First();
        string lead = leadMember.Patch == null ? "" : DropRecord.Of(leadMember.Face, leadMember.Patch).Text;
        var bases = unit.Members.Select(m => m.Patch.KindLead.Length > 0 ? m.Patch.KindBase : m.Patch.Item).Distinct().ToList();
        string kindLead = unit.Members.Select(m => m.Patch.KindLead).FirstOrDefault(k => k.Length > 0) ?? "";
        return (lead, records, bases.Count == 1 ? bases[0] : "", kindLead);
    }
}
