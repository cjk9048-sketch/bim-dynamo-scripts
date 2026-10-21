using System.Diagnostics;
using System.Globalization;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using DH.Takeoff.Core;

namespace DH.Takeoff.Revit;

/// <summary>
/// 부재 이름(R53b — SB01 · WA01 …): [수량산출]의 [완료]가 산출식 다음에 콘크리트 부재마다 속성 «DH_부재명»에 적는다(JACK 2026-10-19
/// «수량산출 매개변수를 입력할 때»). 무엇을 붙일지는 Core(MemberNames.Assign)가 정하고, 여기서는 모델을 읽어 넘기고 결정대로 적는다.
///  · 이름 주인 기록(숨은 기록 — 부재마다 {Uid · Name · Auto · Origin · Extra}): 이름과 함께 적는다. Revit 은 부재를 복사하면 기록도 복사하므로
///    기록의 Uid 가 이 부재가 아니면 복사본이다(겹침 검사가 나눈 조각도) — 원래 부재가 그 이름을 쓰는 동안 새 번호, 원래 부재는 그 이름을 지킨다.
///    Auto 는 프로그램이 붙인 이름인지(칸 값이 기록의 이름과 다르면 사람이 고친 것 — 지킨다) · Origin 은 겹침 검사가 나눈 조각이면 원래 부재의 UniqueId
///    (조각의 조각이면 맨 처음 부재 — R53c 기둥 개수를 원래 부재로 셀 때). 조각은 나눌 때 MarkPiece 가 조각 표시(Extra)를 붙인다 — 사람이 복사한
///    부재는 원래 부재를 적지 않는다(복사본도 제 부재로 센다).
///  · 적는 것은 바뀐 부재만(새 이름 · 기록이 없거나 맞지 않는 부재) — 다시 [완료]해도 그대로인 부재는 건드리지 않는다(작업공유에서 남의 부재를 빌리지 않게).
///  · 적을 수 없는 부재(그룹 안 · 다른 사용자가 편집 중 · 중앙에서 바뀜)는 건너뛰고 센다 — 그 이름은 그대로 두고 번호를 셀 때만 쓴다.
///  · 주 옵션이 아닌 디자인 옵션의 부재는 산출서에 실리지 않아 이름을 붙이지 않는다.
///  · 트랜잭션 하나(창의 그룹 안 — [완료]의 Ctrl+Z 한 줄 · [처음 상태로]면 함께 되돌아간다). 칸이 없으면 먼저 만든다(자체 트랜잭션).
/// 알고 둠: 가장 큰 번호는 모델에 지금 있는 이름으로 센다 — 가장 큰 번호의 부재를 지우면 다음 새 부재가 그 번호를 다시 받는다(그 아래의 지운 번호는 비워 둔다).
/// 작업공유에서 두 사람이 같은 때 새 부재에 이름을 붙이면 같은 번호가 날 수 있다(다음 [완료]에서 작은 Id 가 갖고 나머지는 새 번호).
/// </summary>
public static class MemberNamer
{
    private static readonly Guid SchemaId = new("7a2b6c10-0000-4a00-9b00-0000000000e4");
    private const string UidField = "Uid", NameField = "Name", AutoField = "Auto", OriginField = "Origin", ExtraField = "Extra";

    public sealed class Outcome
    {
        public bool ParametersEnsured;              // 칸(DH_부재명)을 만드는 트랜잭션을 커밋했다(그룹 안)
        public bool Wrote;                          // 이름 트랜잭션을 열었다(적을 것이 없으면 열지 않는다)
        public TransactionStatus Status = TransactionStatus.Uninitialized;
        public bool Committed => Wrote && Status == TransactionStatus.Committed;
        public int Members, Alternates, NoParam;    // 대상 · 디자인 옵션 대안이라 뺌 · 칸이 없어 뺌
        public int Fresh, Copies, Duplicates, Prefixed, Written;   // 실제로 새 이름을 적은 부재(MemberNames.Why 별 — 되돌려지면 0) · 칸이나 기록을 적은 부재
        public int Kept, Locked;                                    // 이름을 그대로 둔 부재 · 적을 수 없어 그대로 둔 부재
        public List<string> Failed = new();         // 적지 못한 부재(Id · 까닭)
        public Dictionary<long, string> Names = new();   // 되읽은 이름(적은 뒤 — 엑셀 · 거푸집 줄이 쓴다) · 이름 없는 부재는 ""
        public List<string> Examples = new();       // 일지: «Id 앞 → 뒤(까닭)» 몇 개
        public long Ms;
        public string Problem = "";                 // 되돌려졌다 등("" = 없음)
    }

    /// <summary>
    /// 이름을 정하고 적는다(창의 그룹 안 — 트랜잭션은 여기서 연다). 대상 = 창의 콘크리트 부재(editor.Members). 앞 글자는 ① 목록의 지금 묶음
    /// (사용자가 옮긴 것까지 — 거푸집과 같은 잣대) · 위치 차례는 ① 의 위치 목록. 던질 수 있다(부르는 쪽이 받아 적고 엑셀은 이어서 만든다).
    /// </summary>
    public static Outcome Assign(Document doc, PickEditor editor)
    {
        var watch = Stopwatch.StartNew();
        var outcome = new Outcome();
        var groups = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var p in editor.Parts) groups.TryAdd(p.Name, p.Group ?? "");
        var places = editor.Places.Select(p => p.Name).ToList();

        var targets = new List<(PickMember member, Element el)>();
        foreach (var m in editor.Members)
        {
            if (doc.GetElement(new ElementId(m.Id)) is not Element el) continue;
            if (CalcSheetExporter.AlternateOption(el) != null) { outcome.Alternates++; continue; }
            targets.Add((m, el));
        }
        if (targets.Count > 0 && !targets.Any(t => t.el.GetParameters(MemberNames.Param).Count > 0))
        {
            // 칸이 아직 없다(이 모델에서 처음) — 만든다(자체 트랜잭션 · 그룹 안).
            SharedParameterManager.EnsureParameters(doc, new[] { MemberNames.Param });
            outcome.ParametersEnsured = true;
            // 칸을 묶은 뒤의 부재로 다시 받는다(앞서 받은 부재 객체가 새 칸을 못 볼 수 있다 — revit-verify R53b 열린 질문).
            targets = targets.Select(t => (t.member, doc.GetElement(t.el.Id) ?? t.el)).ToList();
        }

        var inputs = new List<MemberNames.Member>();
        var elements = new Dictionary<long, Element>();
        foreach (var (m, el) in targets)
        {
            if (!el.GetParameters(MemberNames.Param).Any(p => p.StorageType == StorageType.String)) { outcome.NoParam++; continue; }
            string part = m.Value.Part.Length > 0 ? m.Value.Part : m.AutoPart;
            string group = groups.TryGetValue(part, out var g) && g.Length > 0 ? g : PartCatalog.GroupOf(part);
            var (stamp, stampName, stampAuto, origin) = ReadStamp(el);
            // 칸이 읽기 전용인 부재(적층벽의 하위 벽 등)도 적을 수 없는 부재로 센다 — 번호를 받아 못 적으면 그 번호가 빈다(revit-verify R53b L).
            bool writable = el.GetParameters(MemberNames.Param).Any(p => !p.IsReadOnly && p.StorageType == StorageType.String);
            int zone = places.IndexOf(m.Value.Zone);
            Vec(el, out double x, out double y, out double z);
            elements[m.Id] = el;
            inputs.Add(new MemberNames.Member
            {
                Id = m.Id, Name = CalcSheetExporter.Text(el, MemberNames.Param), Stamp = stamp, StampName = stampName, StampAuto = stampAuto, Origin = origin,
                Prefix = MemberNames.PrefixOf(group, part, m.Kind), Editable = writable && FormulaAutoFill.WhyNotEditable(doc, el) == null,
                Zone = zone < 0 || m.Value.Zone.Length == 0 ? int.MaxValue : zone, X = x, Y = y, Z = z,
            });
        }
        outcome.Members = inputs.Count;
        var decisions = MemberNames.Assign(inputs, OtherNames(doc, elements.Keys.ToHashSet()));
        outcome.Kept = decisions.Count(d => d.Why == MemberNames.Why.Kept);
        outcome.Locked = decisions.Count(d => d.Why == MemberNames.Why.Locked);

        var writes = decisions.Where(d => d.Write).ToList();
        if (writes.Count > 0)
        {
            outcome.Wrote = true;
            using var tx = new Transaction(doc, "DH 부재 이름");
            tx.Start();
            foreach (var d in writes)
            {
                var el = elements[d.Id];
                string before = CalcSheetExporter.Text(el, MemberNames.Param);
                bool changed = false;
                try
                {
                    if (before.Trim() != d.Name)
                    {
                        if (!DimensionExtractor.WriteString(el, MemberNames.Param, d.Name))
                        {
                            outcome.Failed.Add($"{d.Id}: 칸({MemberNames.Param})이 없거나 읽기 전용");
                            continue;
                        }
                        changed = true;
                    }
                    WriteStamp(el, d.Name, d.Auto, d.Origin);
                    outcome.Written++;
                    switch (d.Why)
                    {
                        case MemberNames.Why.New: outcome.Fresh++; break;
                        case MemberNames.Why.Copy: outcome.Copies++; break;
                        case MemberNames.Why.Duplicate: outcome.Duplicates++; break;
                        case MemberNames.Why.Prefix: outcome.Prefixed++; break;
                    }
                    if (d.Why != MemberNames.Why.Kept && outcome.Examples.Count < 30)
                        outcome.Examples.Add($"{d.Id} {(d.Before.Length > 0 ? d.Before : "(없음)")}→{d.Name}({WhyText(d.Why)})");
                }
                catch (Exception ex)
                {
                    // 기록을 못 적었으면 칸도 전 값으로 — 칸만 바뀌면 다음 [완료]에서 사람이 적은 이름으로 굳는다(revit-verify R53b L).
                    if (changed) try { DimensionExtractor.WriteString(el, MemberNames.Param, before); } catch { }
                    outcome.Failed.Add($"{d.Id}: {ex.Message}");
                }
            }
            outcome.Status = tx.Commit();
            if (outcome.Status != TransactionStatus.Committed)
            {
                outcome.Problem = $"Revit 이 부재 이름을 되돌렸습니다({outcome.Status})";
                outcome.Fresh = outcome.Copies = outcome.Duplicates = outcome.Prefixed = outcome.Written = 0;
            }
        }
        // 되읽기: 엑셀 · 거푸집 줄은 모델에 실제로 남은 이름을 쓴다(되돌려졌으면 전의 이름).
        foreach (var (id, el) in elements) outcome.Names[id] = CalcSheetExporter.Text(el, MemberNames.Param).Trim();
        outcome.Ms = watch.ElapsedMilliseconds;
        return outcome;
    }

    /// <summary>일지 · 알림 창의 까닭 낱말.</summary>
    public static string WhyText(MemberNames.Why why) => why switch
    {
        MemberNames.Why.New => "새 부재",
        MemberNames.Why.Copy => "복사본 · 나눈 조각",
        MemberNames.Why.Duplicate => "같은 이름",
        MemberNames.Why.Prefix => "부위가 바뀜",
        MemberNames.Why.Locked => "고칠 수 없음",
        _ => "그대로",
    };

    // 둘레 상자의 작은 x · y · 아래 끝 z(m — 새 번호의 차례). 못 읽으면 0.
    private static void Vec(Element el, out double x, out double y, out double z)
    {
        x = y = z = 0;
        try
        {
            if (el.get_BoundingBox(null) is { } bb)
            {
                var lo = ZoneScene.Meters(bb.Min);
                (x, y, z) = (lo.X, lo.Y, lo.Z);
            }
        }
        catch { }
    }

    // 대상 밖의 부재(이름 칸이 묶인 범주 전부)에 적힌 이름 — 가장 큰 번호 · 복사본이 이름을 이어받을 수 있는가를 볼 때.
    // 칸은 GUID 로 읽는다(이름으로 찾기보다 빠르다 — 큰 모델에서 [완료]마다 훑는다 · revit-verify R53b L).
    private static List<string> OtherNames(Document doc, HashSet<long> mine)
    {
        var names = new List<string>();
        var guid = SharedParameterManager.Guids[MemberNames.Param];
        var filter = new ElementMulticategoryFilter(SharedParameterManager.BoundCategories.ToList());
        foreach (var el in new FilteredElementCollector(doc).WhereElementIsNotElementType().WherePasses(filter))
        {
            if (mine.Contains(el.Id.Value)) continue;
            string name = (el.get_Parameter(guid)?.AsString() ?? "").Trim();
            if (name.Length > 0) names.Add(name);
        }
        return names;
    }

    /// <summary>
    /// 부재들의 지금 이름(모델에서 읽음 · 없으면 "") — 이름 단계가 오류로 끝났을 때 거푸집 줄이 콘크리트 줄(모델에서 읽는다)과 같은 이름을 쓰게
    /// (revit-verify R53b L).
    /// </summary>
    public static Dictionary<long, string> ReadNames(Document doc, IEnumerable<long> ids)
    {
        var names = new Dictionary<long, string>();
        foreach (long id in ids)
        {
            try { names[id] = doc.GetElement(new ElementId(id)) is Element el ? CalcSheetExporter.Text(el, MemberNames.Param).Trim() : ""; }
            catch { names[id] = ""; }
        }
        return names;
    }

    /// <summary>
    /// 겹침 검사가 나눈 조각(복사본)에 «원래 부재의 조각» 기록을 붙인다 — 부르는 쪽의 트랜잭션 안(OverlapSplitter.TrySplit). 기록의 주인은 원래 부재라
    /// 다음 [완료]에서 복사본으로 읽혀 새 번호를 받고, Origin = 원래 부재(원래 부재가 조각이면 그 맨 처음 부재)가 남는다(R53c — 기둥 개수를 원래 부재로).
    /// 사람이 복사한 부재와 가르려고 조각 표시(Extra = piece)를 둔다: 사람이 복사한 부재는 원래 부재를 적지 않는다(revit-verify R53b 중간 —
    /// 기둥 하나를 39번 복사해도 40EA). 원래 부재에 이름이나 기록이 없어도 적는다. 던지지 않는다.
    /// </summary>
    public static void MarkPiece(Element copy, Element original)
    {
        try
        {
            var (stamp, stampName, stampAuto, origin) = ReadStamp(original);
            string name = CalcSheetExporter.Text(original, MemberNames.Param).Trim();
            bool auto = stamp == MemberNames.Stamp.Own && stampAuto && stampName == name;
            string root = stamp == MemberNames.Stamp.Own && origin.Length > 0 ? origin : original.UniqueId ?? "";
            var entity = new Entity(GetSchema());
            entity.Set(UidField, original.UniqueId ?? "");
            entity.Set(NameField, name);
            entity.Set(AutoField, auto);
            entity.Set(OriginField, root);
            entity.Set(ExtraField, PieceTag);
            copy.SetEntity(entity);
        }
        catch { }
    }

    private const string PieceTag = "piece";

    private static Schema GetSchema()
    {
        var schema = Schema.Lookup(SchemaId);
        if (schema != null) return schema;
        var builder = new SchemaBuilder(SchemaId);
        builder.SetSchemaName("DHTakeoffMemberName");
        builder.SetDocumentation("DH Takeoff: who owns this element's member name (DH_부재명) and whether the add-in assigned it");
        builder.SetReadAccessLevel(AccessLevel.Public);
        builder.SetWriteAccessLevel(AccessLevel.Public);
        builder.AddSimpleField(UidField, typeof(string));
        builder.AddSimpleField(NameField, typeof(string));
        builder.AddSimpleField(AutoField, typeof(bool));
        builder.AddSimpleField(OriginField, typeof(string));
        builder.AddSimpleField(ExtraField, typeof(string));   // 나중에 더 담을 것(스키마는 한 번 쓰이면 칸을 바꿀 수 없다)
        return builder.Finish();
    }

    // 부재의 이름 기록: (누구의 것 · 이름 · 프로그램이 붙임 · 원래 부재). 이 부재의 기록이면 원래 부재 = 기록의 원래 부재. 복사본이면 겹침 검사가 나눈
    // 조각(조각 표시)일 때만 기록의 원래 부재 — 사람이 복사한 부재는 제가 원래 부재다(""). 못 읽으면 없음.
    private static (MemberNames.Stamp stamp, string name, bool auto, string origin) ReadStamp(Element el)
    {
        try
        {
            var schema = Schema.Lookup(SchemaId);
            var entity = schema == null ? null : el.GetEntity(schema);
            if (entity == null || !entity.IsValid()) return (MemberNames.Stamp.None, "", false, "");
            string uid = entity.Get<string>(UidField) ?? "";
            string name = entity.Get<string>(NameField) ?? "";
            bool auto = entity.Get<bool>(AutoField);
            string origin = entity.Get<string>(OriginField) ?? "";
            if (uid.Length > 0 && uid == (el.UniqueId ?? "")) return (MemberNames.Stamp.Own, name, auto, origin);
            bool piece = (entity.Get<string>(ExtraField) ?? "") == PieceTag;
            return (MemberNames.Stamp.Foreign, name, auto, piece ? origin : "");
        }
        catch { return (MemberNames.Stamp.None, "", false, ""); }
    }

    private static void WriteStamp(Element el, string name, bool auto, string origin)
    {
        var entity = new Entity(GetSchema());
        entity.Set(UidField, el.UniqueId ?? "");
        entity.Set(NameField, name);
        entity.Set(AutoField, auto);
        entity.Set(OriginField, origin);
        entity.Set(ExtraField, "");
        el.SetEntity(entity);
    }

    /// <summary>일지 한 줄.</summary>
    public static string Note(Outcome o) =>
        $"완료\t부재 이름\t대상 {o.Members} · 새 부재 {o.Fresh} · 복사본·조각 새 번호 {o.Copies} · 같은 이름 새 번호 {o.Duplicates} · 부위가 바뀌어 앞 글자 바꿈 {o.Prefixed}" +
        $" · 그대로 {o.Kept} · 고칠 수 없음 {o.Locked} · 디자인 옵션 대안 뺌 {o.Alternates} · 칸 없음 {o.NoParam} · 적음 {o.Written}" +
        $" · 칸 새로 만듦 {(o.ParametersEnsured ? "O" : "X")} · 트랜잭션 {(o.Wrote ? o.Status.ToString() : "X(적을 것 없음)")} · 못 적음 {o.Failed.Count}" +
        (o.Failed.Count > 0 ? "(" + string.Join(" / ", o.Failed.Take(5)) + ")" : "") +
        (o.Problem.Length > 0 ? " · " + o.Problem : "") + $" · {o.Ms.ToString(CultureInfo.InvariantCulture)} ms" +
        (o.Examples.Count > 0 ? "\n  " + string.Join(" · ", o.Examples) : "");
}
