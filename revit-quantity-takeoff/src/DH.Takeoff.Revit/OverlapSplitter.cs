using System.Globalization;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using DH.Takeoff.Core;

namespace DH.Takeoff.Revit;

/// <summary>
/// 결합으로 두 덩이(이상)가 된 벽 · 기둥을 덩이마다 한 객체로 나눈다(JACK 2026-10-14: "결합으로 인해 두 덩이지만 마우스로 클릭하면 하나의 객체인데
/// … 실제로 두 덩이로 잘라줘야 되는 거 아니야? 그래야 나중에 수식도 넣기 편하지"). 인터뷰: 고칠 때 저절로 · 벽 · 기둥 ·
/// 나눈 둘 다 위치·부위 그대로 + 식 '다시 계산 필요' · 못 나누는 것(문·창·구멍 · 기운 것 · 붙은 것 …)은 나누지 않고 표시.
///  · 이번 결합으로 덩이가 늘었을 때만 본다(입체가 여럿인 패밀리 · 전에 걸린 결합으로 이미 여러 덩이면 말없이 넘긴다).
///  · 덩이가 위아래로 틈을 두고 쌓였을 때만 나눈다(슬래브가 가로로 가른 것). 옆으로 갈리거나 맞닿은 것은 못 나눔.
///  · 가장 큰 덩이 = 원래 객체(번호 · 태그 · 치수 · 옆 벽과의 끝 잇기가 본체에 남게 — JACK 2026-10-15 첫 실행) · 나머지 덩이마다 복사본
///    (아래 끝을 올리고, 맨 위가 아니면 위 끝도 내린다).
///    끝은 레벨의 높이가 아니라 «지금 끝에서 얼마나 옮기나»로 바꾼다 — 레벨의 표고 기준이 공유일 수 있다(R35 M3).
///  · 복사본에는 원래 객체에 걸려 있던 결합(그 복사본과 겹치는 것만)과 자르는 순서 · 벽 끝 잇기 허용 · 작업 세트 ·
///    「위치·부위」 확인 기록(PlaceStamp — 복사하면 원래 객체의 UniqueId 가 든 채 따라와 '확인 필요'가 된다, R35 M1)을 옮긴다.
///  · 스스로 검사: 조각마다 덩이가 하나이고 그 체적 · 높이가 그 덩이와 같다(조각마다 차이를 일지에) · 둘레 부재의 체적이 그대로다(옆 벽의 끝 잇기가
///    바뀌어 늘거나 줄지 않았다) — 아니면 이 트랜잭션만 되돌린다(결합은 남는다).
/// 부르는 쪽(OverlapChecker.Join)의 트랜잭션 묶음 안에서, 열린 트랜잭션이 없을 때 부른다.
/// </summary>
public static class OverlapSplitter
{
    public sealed class Result
    {
        public bool Split;                       // 나눴다
        public bool NotTarget;                   // 대상이 아님(벽 · 기둥이 아니거나 한 덩이) — 말없이 넘어간다
        public string Why = "";                  // 못 나눈 까닭
        public List<ElementId> Pieces = new();   // 나눈 조각(맨 앞 = 원래 객체 — 가장 큰 덩이 · 그 뒤로 나머지 덩이의 복사본을 아래에서 위로)
        public List<string> Lines = new();       // 일지에 적을 줄
    }

    private const double FtToM = 0.3048, Ft3ToM3 = FtToM * FtToM * FtToM;
    private const double Gap = 0.001 / FtToM;                                // 덩이가 위아래로 쌓였다고 볼 틈(1 mm)
    private const double MinLumpFt3 = OverlapChecker.MinOverlapM3 / Ft3ToM3; // 이보다 작은 덩이 = 아주 작은 조각
    private const double TolAbsFt3 = 1e-6 / Ft3ToM3, TolRel = 1e-4;          // 조각 체적 = 덩이 체적으로 볼 폭
    private const double AroundTolRel = 1e-6;                                // 둘레 부재 체적이 그대로로 볼 상대 폭 — 큰 바닥판(100 m³)도 0.1 L 까지만(R41 core L-2)
    private const double ZTol = 0.001 / FtToM;                               // 조각의 위아래 끝 = 덩이의 위아래 끝으로 볼 폭(1 mm)
    private const double MinPieceFt = 0.05 / FtToM;                          // 이보다 낮은 덩이 = 자투리 → 나누지 않음(JACK 2026-10-15 «0.05 m 미만이면 안 나눔»)

    private sealed record Lump(Solid Solid, double MinZ, double MaxZ, double Volume);

    /// <summary>
    /// 나누기 대상(벽 · 구조 기둥)의 지금 덩이 수 — 대상이 아니거나 읽지 못하면 −1. 결합하기 전에 세어 TrySplit 에 넘긴다(R36 core M-3 · revit L5).
    /// </summary>
    public static int LumpCount(Element el)
    {
        if (!IsTarget(el)) return -1;
        try { return CountLumps(SolidReader.Solids(el)); }
        catch { return -1; }
    }

    /// <summary>
    /// 바닥(슬래브)의 지금 덩이 수 — 나누기 대상은 아니고 일지에만 쓴다: 결합 순서를 바꿔 벽이 바닥을 자르면 바닥이 두 덩이가 될 수 있고,
    /// 그러면 산출식이 상자로 안 풀려 «직접 넣어야 함»이 될 수 있다(revit-verify R46 L1). 바닥이 아니거나 읽지 못하면 −1.
    /// </summary>
    public static int FloorLumpCount(Element el)
    {
        if (el is not Floor) return -1;
        try { return CountLumps(SolidReader.Solids(el)); }
        catch { return -1; }
    }

    /// <summary>
    /// 결합 · 자르기 · 구멍 전의 덩이 수 — 이미 결합된 부재가 결합 때문에 여러 덩이인지 가를 때(JACK 2026-10-15 «이미 결합된 두 덩이도 나누기»).
    /// 벽 = 1(벽의 형상은 입체 하나 — 송악3 형상 덤프의 벽 25개 모두) · 구조 기둥 = GetOriginalGeometry(결합 · 자르기 전 모양)의 덩이 수
    /// (입체가 여럿인 패밀리면 그 수 — 그래서 몸 + 기둥머리는 늘지 않은 것으로 본다). 대상이 아니거나 읽지 못하면 −1.
    /// solids = 그 원래 모양의 입체 수(일지용 — 벽은 −1). 지금 덩이와 같은 기준(SolidReader 의 거름 · SplitVolumes 체적 > 0)으로 센다.
    /// </summary>
    public static int OriginalLumpCount(Element el, out int solids)
    {
        solids = -1;
        if (!IsTarget(el)) return -1;
        if (el is Wall) return 1;
        try
        {
            var geo = ((FamilyInstance)el).GetOriginalGeometry(SolidReader.ReadOptions());
            if (geo == null) return -1;
            var list = SolidReader.Solids(geo);
            solids = list.Count;
            return CountLumps(list);
        }
        catch { return -1; }
    }

    /// <summary>지금 덩이가 위아래로 틈을 두고 쌓였는가(SplitPlan.Stacked — 쌓였으면 null, 아니면 까닭). 읽지 못하면 그 까닭.</summary>
    public static string? StackReason(Element el)
    {
        try
        {
            var lumps = Lumps(el);
            lumps.Sort((a, b) => a.MinZ.CompareTo(b.MinZ));
            return SplitPlan.Stacked(lumps.Select(l => (l.MinZ, l.MaxZ)).ToList(), Gap);
        }
        catch (Exception ex) { return "덩이를 가르지 못함: " + ex.Message; }
    }

    // 덩이 수만(높이는 재지 않는다 — 결합할 때마다 쌍마다 세므로 · R37 revit R3).
    private static int CountLumps(IEnumerable<Solid> solids)
    {
        int count = 0;
        foreach (var solid in solids)
            foreach (var part in SolidUtils.SplitVolumes(solid))
                if (part != null && part.Volume > 0) count++;
        return count;
    }

    /// <summary>
    /// 결합으로 잘린 el 을 덩이마다 나눈다. before = 결합하기 전의 덩이 수(LumpCount · 모르면 −1) — 결합으로 덩이가 늘었을 때만 나눈다:
    /// 입체가 여럿인 패밀리(기둥 몸 + 기둥머리 등)나 전에 걸린 결합으로 이미 여러 덩이인 부재는 이번 결합이 가른 것이 아니다(말없이 넘긴다 · 일지에만).
    /// </summary>
    public static Result TrySplit(Document doc, Element el, int before)
    {
        var result = new Result();
        if (!IsTarget(el)) { result.NotTarget = true; return result; }

        // 덩이로 가른다.
        List<Lump> lumps;
        try { lumps = Lumps(el); }
        catch (Exception ex) { result.Why = "덩이를 가르지 못함: " + ex.Message; return result; }
        if (lumps.Count <= 1) { result.NotTarget = true; return result; }
        if (before > 0 && lumps.Count <= before)
        {
            result.NotTarget = true;
            result.Lines.Add($"나누기 안 함\tId {el.Id.Value}\t결합 전에도 덩이 {before}개 → 지금 {lumps.Count}개");
            return result;
        }
        lumps.Sort((a, b) => a.MinZ.CompareTo(b.MinZ));
        // 옆으로 갈린 것(벽을 꿰뚫은 기둥 등)은 나누는 대상이 아니다 — «못 나눔»으로 보이지 않고 일지에만(R38 revit P2: 배수지에 흔해 결합할 때마다 쌓인다).
        if (SplitPlan.Stacked(lumps.Select(l => (l.MinZ, l.MaxZ)).ToList(), Gap) == SplitPlan.SideBySide)
        {
            result.NotTarget = true;
            result.Lines.Add($"나누기 안 함\tId {el.Id.Value}\t{SplitPlan.SideBySide}(덩이 {lumps.Count}개)");
            return result;
        }
        if (lumps.Any(l => l.Volume < MinLumpFt3)) return Fail(result, "아주 작은 조각이 있음");

        string? why = WhyNot(doc, el);
        if (why != null) return Fail(result, why);

        // 지금의 아래 끝 · 위 끝(결합으로 잘리기 전의 설계 높이) → 조각마다 옮길 만큼(Core SplitPlan — 위아래로 쌓였는지도 거기서 본다).
        double baseZ, topZ;
        try { (baseZ, topZ) = Extent(el); }
        catch (Exception ex) { return Fail(result, "높이를 읽지 못함: " + ex.Message); }
        if (SplitPlan.Plan(lumps.Select(l => (l.MinZ, l.MaxZ)).ToList(), baseZ, topZ, Gap, MinPieceFt, out var moves) is { } cannot)
            return Fail(result, cannot != SplitPlan.Thin ? cannot     // 높이는 내려 적는다 — 0.0496 m 가 «0.05 m»로 찍혀 한도와 어긋나 보이지 않게(R38 core L-1)
                : string.Create(CultureInfo.InvariantCulture, $"{SplitPlan.Thin}(높이 {Math.Floor(lumps.Min(l => l.MaxZ - l.MinZ) * FtToM * 10000) / 10000:0.0###} m)"));

        // 가장 큰 덩이를 원래 객체가 맡는다 — 번호 · 태그 · 치수와 옆 벽과의 끝 잇기가 본체에 남게(JACK 2026-10-15 첫 실행: 벽 1063297 의
        // 위 본체를 복사본으로 만들자, 원래 벽이 그 높이에서 옆 벽과의 끝 잇기로 늘어나 있던 0.25 m 를 잃어 체적이 0.625 m³ 모자랐다).
        // 나머지 덩이마다 복사본. lumpOf[i] = 조각 i 가 맡는 덩이(조각 0 = 원래 객체).
        int keep = 0;
        for (int k = 1; k < lumps.Count; k++) if (lumps[k].Volume > lumps[keep].Volume) keep = k;
        var lumpOf = new List<int> { keep };
        lumpOf.AddRange(Enumerable.Range(0, lumps.Count).Where(k => k != keep));

        // 둘레 부재의 체적(나누기 전) — 조각끼리는 맞는데 옆 벽의 끝 잇기가 바뀌어 옆 벽의 체적이 바뀌는 것을 잡으려고(같은 첫 실행).
        var around = Around(doc, el);
        var axis = Axis(el);
        string ends = EndJoins(el);

        // 결합 · 끝 잇기 · 작업 세트 · 확인 기록을 바꾸기 전에 적어 둔다.
        var joined = new List<(ElementId id, bool cutsThis)>();
        foreach (var id in JoinGeometryUtils.GetJoinedElements(doc, el))
            if (doc.GetElement(id) is Element other)
                joined.Add((id, JoinGeometryUtils.IsCuttingElementInJoin(doc, other, el)));
        bool[] allowJoin = el is Wall w0 ? new[] { WallUtils.IsWallJoinAllowedAtEnd(w0, 0), WallUtils.IsWallJoinAllowedAtEnd(w0, 1) } : Array.Empty<bool>();
        int? workset = doc.IsWorkshared ? el.get_Parameter(BuiltInParameter.ELEM_PARTITION_PARAM)?.AsInteger() : null;
        var stamp = PlaceStamp.ValueOf(el);
        string mark = FormworkReader.MarkOf(el);

        using var tx = new Transaction(doc, "DH 겹침 결합 — 나누기");
        var quiet = new OverlapChecker.QuietFailures();
        var pieces = new List<Element> { el };
        int joinsMoved = 0, unjoined = 0;
        try
        {
            tx.Start();
            var options = tx.GetFailureHandlingOptions();
            options.SetFailuresPreprocessor(quiet);
            options.SetClearAfterRollback(true);
            tx.SetFailureHandlingOptions(options);

            // 원래 객체가 맡지 않은 덩이마다 복사본: 아래 끝 = 덩이의 아래 · 위 끝 = 덩이의 위(맨 위 덩이는 원래 위 끝 그대로 — 결합이 자른다).
            for (int i = 1; i < lumpOf.Count; i++)
            {
                int k = lumpOf[i];
                var ids = ElementTransformUtils.CopyElement(doc, el.Id, XYZ.Zero);
                if (ids.Count != 1 || doc.GetElement(ids.First()) is not Element copy || copy.GetType() != el.GetType())
                {
                    // 무엇이 함께 복사됐는지 적는다(되돌리면 읽을 수 없다) — 해석 부재 등.
                    string what = string.Join(", ", ids.Select(i => doc.GetElement(i) is Element e ? e.Category?.Name ?? e.GetType().Name : "?"));
                    tx.RollBack();
                    return Fail(result, $"복사본이 하나가 아님({ids.Count}개: {what})");
                }
                Move(copy, moves[k]);
                if (copy is Wall cw && allowJoin.Length == 2)
                    for (int end = 0; end < 2; end++)
                    {
                        if (allowJoin[end]) WallUtils.AllowWallJoinAtEnd(cw, end);
                        else WallUtils.DisallowWallJoinAtEnd(cw, end);
                    }
                if (workset is { } ws && copy.get_Parameter(BuiltInParameter.ELEM_PARTITION_PARAM) is { IsReadOnly: false } part) part.Set(ws);
                PlaceStamp.Set(copy, stamp);
                MemberNamer.MarkPiece(copy, el);   // 부재 이름(R53b): 다음 [완료]에서 조각은 새 번호 · 원래 부재를 남긴다(사람이 복사한 부재와 가름)
                // 복사본의 «마크»를 원래 값으로 — Revit 은 복사할 때 마크를 바꿀 수 있다(나눈 조각도 도면 부호는 원래 부재의 것 · 엑셀 거푸집 줄의 부호 — R47 revit-verify L1).
                if (copy.get_Parameter(BuiltInParameter.ALL_MODEL_MARK) is { IsReadOnly: false } markParam && FormworkReader.MarkOf(copy) != mark) markParam.Set(mark);
                pieces.Add(copy);
            }
            // 원래 객체 = 가장 큰 덩이: 그 덩이의 끝으로 옮긴다(맨 아래면 위 끝만 · 가운데면 둘 다 · 맨 위면 아래 끝만).
            Move(el, moves[keep]);
            doc.Regenerate();

            // 복사본에 결합을 옮긴다(그 복사본과 겹치는 것만 · 같은 자르는 순서). 하나라도 못 옮기면 나누기를 되돌린다 —
            // 원래 부재가 자르던 부재(cutsThis 거짓)는 옮기지 못해도 조각 모양이 그대로라 아래 검사가 못 잡는다(그 부재가 겹친 채 남는다 · R36 revit M1).
            string notMoved = "";
            var moved = new List<(Element copy, Element other, bool cutsThis)>();
            foreach (var copy in pieces.Skip(1))
                foreach (var (id, cutsThis) in joined)
                {
                    if (doc.GetElement(id) is not Element other || !Touches(copy, other)) continue;
                    try
                    {
                        if (!JoinGeometryUtils.AreElementsJoined(doc, copy, other)) JoinGeometryUtils.JoinGeometry(doc, copy, other);
                        bool otherCuts = JoinGeometryUtils.IsCuttingElementInJoin(doc, other, copy);
                        if (otherCuts != cutsThis) JoinGeometryUtils.SwitchJoinOrder(doc, copy, other);
                        moved.Add((copy, other, cutsThis));
                        joinsMoved++;
                    }
                    catch (Exception ex)
                    {
                        result.Lines.Add($"  결합 옮기기 못 함\tId {copy.Id.Value} ↔ {id.Value}\t{ex.Message}");
                        if (notMoved.Length == 0) notMoved = $"Id {id.Value} 와의 결합을 옮기지 못함";
                    }
                }
            doc.Regenerate();
            // 옮긴 결합이 정말 원래와 같은 자르는 순서인지 본다(바꾼 뒤 Revit 이 다시 뒤집는 경우).
            foreach (var (copy, other, cutsThis) in moved)
                if (notMoved.Length == 0 && JoinGeometryUtils.IsCuttingElementInJoin(doc, other, copy) != cutsThis)
                    notMoved = $"Id {other.Id.Value} 와의 자르는 순서를 원래대로 맞추지 못함";
            if (notMoved.Length > 0)
            {
                tx.RollBack();
                return Fail(result, notMoved);
            }

            // 원래 객체가 옮겨 가 이제 닿지 않는 부재와의 결합은 푼다 — 남기면 뒤에 그 부재를 고칠 때 «결합됐지만 교차하지 않음» 경고가 뜰 수 있다(R41 revit R41-1).
            // 닿지 않으니 모양은 그대로다(아래 둘레 검사가 다시 본다). 풀지 못해도 나누기는 이어 간다(일지만).
            foreach (var (id, _) in joined)
            {
                if (doc.GetElement(id) is not Element other || BoxesMeet(el, other)) continue;
                // 풀기는 상대도 고친다 — 다른 사용자 소유 · 그룹 안이면 커밋 때 나누기 전체가 되돌려질 수 있다. 덤으로 하는 정리라 건너뛴다(R41 revit 보탬).
                if (FormulaAutoFill.WhyNotEditable(doc, other) is { } locked)
                {
                    result.Lines.Add($"  결합 풀기 건너뜀\tId {el.Id.Value} ↔ {id.Value}\t편집 못 함: {locked}");
                    continue;
                }
                try
                {
                    if (JoinGeometryUtils.AreElementsJoined(doc, el, other))
                    {
                        JoinGeometryUtils.UnjoinGeometry(doc, el, other);
                        unjoined++;
                    }
                }
                catch (Exception ex) { result.Lines.Add($"  결합 풀기 못 함\tId {el.Id.Value} ↔ {id.Value}\t{ex.Message}"); }
            }
            if (unjoined > 0) doc.Regenerate();

            // 스스로 검사: 조각마다 덩이 하나 · 체적과 위아래 끝이 그 덩이와 같다. 다르면 벽은 길이 방향으로 어느 끝이 다른지도 적는다(진단).
            string bad = "";
            for (int i = 0; i < pieces.Count; i++)
            {
                var target = lumps[lumpOf[i]];
                var got = Lumps(pieces[i]);
                double vol = got.Sum(l => l.Volume);
                double minZ = got.Count > 0 ? got.Min(l => l.MinZ) : double.NaN, maxZ = got.Count > 0 ? got.Max(l => l.MaxZ) : double.NaN;
                double diff = vol - target.Volume;
                bool ok = got.Count == 1 && Math.Abs(diff) <= TolAbsFt3 + TolRel * target.Volume &&
                          Math.Abs(minZ - target.MinZ) <= ZTol && Math.Abs(maxZ - target.MaxZ) <= ZTol;
                result.Lines.Add(string.Create(CultureInfo.InvariantCulture,
                    $"  조각 {i}(덩이 {lumpOf[i]})\tId {pieces[i].Id.Value}\t덩이 {target.Volume * Ft3ToM3:0.######} m3 / 조각 {vol * Ft3ToM3:0.######} m3 (차이 {diff * Ft3ToM3:0.######})" +
                    $"\t덩이 수 {got.Count}\t높이 {(minZ - target.MinZ) * FtToM * 1000:0.#} / {(maxZ - target.MaxZ) * FtToM * 1000:0.#} mm\t{(ok ? "O" : "X")}"));
                if (!ok && axis is { } ax && AxisRange(ax, new[] { target.Solid }) is { } want && AxisRange(ax, got.Select(l => l.Solid)) is { } have)
                    result.Lines.Add(string.Create(CultureInfo.InvariantCulture,
                        $"    길이 방향\t덩이 {want.min * FtToM:0.###} ~ {want.max * FtToM:0.###} m / 조각 {have.min * FtToM:0.###} ~ {have.max * FtToM:0.###} m" +
                        $"\t앞 끝 {(have.min - want.min) * FtToM * 1000:0.#} mm · 뒤 끝 {(have.max - want.max) * FtToM * 1000:0.#} mm"));
                if (!ok && bad.Length == 0)
                    bad = got.Count != 1 ? $"조각 {i} 이 덩이 {got.Count}개" : $"조각 {i} 이 덩이와 다름(체적 차이 {diff * Ft3ToM3:0.######} m³)";
            }
            // 둘레 부재의 체적이 그대로인가 — 끝 잇기 · 결합이 바뀌어 옆 벽 등이 늘거나 줄면 조각이 맞아도 수량이 틀린다.
            foreach (var (other, was) in around)
            {
                double now;
                try { now = SolidReader.Solids(other).Sum(s => s.Volume); }
                catch { now = double.NaN; }
                if (Math.Abs(now - was) <= TolAbsFt3 + AroundTolRel * was) continue;
                result.Lines.Add(string.Create(CultureInfo.InvariantCulture,
                    $"  옆 부재 체적 바뀜\tId {other.Id.Value}\t{was * Ft3ToM3:0.######} → {now * Ft3ToM3:0.######} m3"));
                if (bad.Length == 0)
                    bad = string.Create(CultureInfo.InvariantCulture, $"옆 부재의 체적이 바뀜(Id {other.Id.Value}: {was * Ft3ToM3:0.###} → {now * Ft3ToM3:0.###} m³)");
            }
            if (bad.Length > 0)
            {
                // 끝 잇기: 나누기 전 원래 객체 · 나눈 뒤 조각마다(어느 끝에서 이어지지 않았나 — R41 revit R41-2).
                if (ends.Length > 0) result.Lines.Add("  나누기 전 " + ends);
                for (int i = 0; i < pieces.Count; i++)
                    if (EndJoins(pieces[i]) is { Length: > 0 } after) result.Lines.Add($"  나눈 뒤 조각 {i} Id {pieces[i].Id.Value} {after}");
                tx.RollBack();
                return Fail(result, "나눈 조각이 맞지 않음 — " + bad);
            }
            foreach (var piece in pieces) FormulaAutoFill.MarkVerdictStale(piece);
            // 모양 기록(R52b): 나누기 전에 위치·부위 확인이 모양과 맞았으면 조각마다(원래 부재 포함) 나눈 뒤 모양으로 고쳐 적는다 — 위의 다시 계산 · 스스로 검사 뒤.
            // 맞지 않았으면(이미 설계가 바뀐 부재) 조각도 그 바뀜을 그대로 지닌다(Set 이 «바뀜» 표시를 적었다).
            if (!stamp.Changed) foreach (var piece in pieces) PlaceStamp.Reshape(piece);
            var status = tx.Commit();
            if (status != TransactionStatus.Committed)
                return Fail(result, "Revit 이 되돌림" + (quiet.FirstError.Length > 0 ? $"({quiet.FirstError})" : ""));
        }
        catch (Exception ex)
        {
            try { if (tx.HasStarted() && !tx.HasEnded()) tx.RollBack(); } catch { }
            return Fail(result, "나누다 오류: " + ex.Message);
        }

        // 되읽기: 조각의 「위치·부위」 기록이 원래 부재와 같은가(같아야 '확인 필요'로 뜨지 않는다 — R35 M1/H2).
        foreach (var copy in pieces.Skip(1))
        {
            var now = PlaceStamp.ValueOf(copy);
            result.Lines.Add($"  위치·부위 기록\tId {copy.Id.Value}\t{(now == stamp ? "원래와 같음" : $"다름(원래 {stamp} / 조각 {now})")}");
        }
        result.Split = true;
        result.Pieces = pieces.Select(p => p.Id).ToList();
        result.Lines.Insert(0, string.Create(CultureInfo.InvariantCulture,
            $"나눔\tId {el.Id.Value} → 조각 {pieces.Count}개 [{string.Join(",", pieces.Select(p => p.Id.Value))}]\t원래 객체 = 덩이 {keep}\t옮긴 결합 {joinsMoved} · 푼 결합 {unjoined}"));
        return result;
    }

    private static Result Fail(Result result, string why)
    {
        result.Why = why;
        result.Pieces.Clear();
        return result;
    }

    /// <summary>나누기 대상인가 — 벽 · 구조 기둥.</summary>
    public static bool IsTarget(Element el) => el is Wall || (el is FamilyInstance fi && IsStructuralColumn(fi));

    private static bool IsStructuralColumn(FamilyInstance fi)
    {
        try { return fi.Category?.BuiltInCategory == BuiltInCategory.OST_StructuralColumns; }
        catch { return false; }
    }

    // 부재의 입체를 덩이로(체적이 있는 것만).
    private static List<Lump> Lumps(Element el) => Lumps(SolidReader.Solids(el));

    private static List<Lump> Lumps(IEnumerable<Solid> solids)
    {
        var lumps = new List<Lump>();
        foreach (var solid in solids)
            foreach (var part in SolidUtils.SplitVolumes(solid))
            {
                if (part == null || !(part.Volume > 0)) continue;
                var (minZ, maxZ) = ZRange(part);
                lumps.Add(new Lump(part, minZ, maxZ, part.Volume));
            }
        return lumps;
    }

    // 입체의 높이 범위 — 모서리를 잘게 나눈 점으로 잰다(경계상자의 변환에 기대지 않는다).
    private static (double min, double max) ZRange(Solid solid)
    {
        double min = double.PositiveInfinity, max = double.NegativeInfinity;
        foreach (Edge edge in solid.Edges)
            foreach (var p in edge.Tessellate())
            {
                if (p.Z < min) min = p.Z;
                if (p.Z > max) max = p.Z;
            }
        return (min, max);
    }

    // 둘레 부재(경계상자가 1 mm 안으로 닿는 것 · 이 부재 빼고 · 입체가 있는 것)와 그 체적(ft³) — 나눈 뒤 그대로인지 본다.
    // 수량을 세는 카테고리만(겹침 검사의 우선순위에 든 것 — 배관 · 가구 등은 읽지 않는다 · R41 core L-1).
    private static readonly BuiltInCategory[] AroundCategories =
    {
        BuiltInCategory.OST_Walls, BuiltInCategory.OST_Floors, BuiltInCategory.OST_StructuralFoundation,
        BuiltInCategory.OST_StructuralColumns, BuiltInCategory.OST_StructuralFraming, BuiltInCategory.OST_GenericModel,
    };

    private static List<(Element El, double Ft3)> Around(Document doc, Element el)
    {
        var list = new List<(Element, double)>();
        BoundingBoxXYZ? bb;
        try { bb = el.get_BoundingBox(null); }
        catch { return list; }
        if (bb == null) return list;
        var pad = new XYZ(Gap, Gap, Gap);
        foreach (var other in new FilteredElementCollector(doc).WhereElementIsNotElementType()
                     .WherePasses(new ElementMulticategoryFilter(AroundCategories))
                     .WherePasses(new BoundingBoxIntersectsFilter(new Outline(bb.Min - pad, bb.Max + pad))))
        {
            if (other.Id == el.Id || FormworkObjects.IsOurs(other)) continue;   // 거푸집 객체(R44)는 콘크리트가 아니다
            double v;
            try { v = SolidReader.Solids(other).Sum(s => s.Volume); }
            catch { continue; }
            if (v > 0) list.Add((other, v));
        }
        return list;
    }

    // 벽의 위치선(직선)의 시작점과 방향 — 길이 방향 진단용. 벽이 아니거나 직선이 아니면 null.
    private static (XYZ origin, XYZ dir)? Axis(Element el)
    {
        try
        {
            if (el is Wall && el.Location is LocationCurve { Curve: Line line } && line.Length > 1e-9)
                return (line.GetEndPoint(0), (line.GetEndPoint(1) - line.GetEndPoint(0)).Normalize());
        }
        catch { }
        return null;
    }

    // 입체들을 위치선 방향으로 잰 범위(모서리를 잘게 나눈 점으로) — 진단용.
    private static (double min, double max)? AxisRange((XYZ origin, XYZ dir) axis, IEnumerable<Solid> solids)
    {
        double min = double.PositiveInfinity, max = double.NegativeInfinity;
        try
        {
            foreach (var solid in solids)
                foreach (Edge edge in solid.Edges)
                    foreach (var p in edge.Tessellate())
                    {
                        double s = (p - axis.origin).DotProduct(axis.dir);
                        if (s < min) min = s;
                        if (s > max) max = s;
                    }
        }
        catch { return null; }
        return min <= max ? (min, max) : null;
    }

    // 벽의 위치선 끝점과 끝마다 이어진 부재 · 잇기 방식(진단용 한 줄 — 못 나눴을 때 일지에). 벽이 아니면 "".
    private static string EndJoins(Element el)
    {
        try
        {
            if (el is not Wall w || w.Location is not LocationCurve lc) return "";
            var parts = new List<string>();
            for (int end = 0; end < 2; end++)
            {
                var p = lc.Curve.GetEndPoint(end);
                var at = new List<long>();
                foreach (Element e in lc.get_ElementsAtJoin(end))
                    if (e != null && e.Id != w.Id) at.Add(e.Id.Value);
                parts.Add(string.Create(CultureInfo.InvariantCulture,
                    $"끝 {end} ({p.X * FtToM:0.###}, {p.Y * FtToM:0.###}, {p.Z * FtToM:0.###}) 잇기 {(WallUtils.IsWallJoinAllowedAtEnd(w, end) ? "허용" : "막음")} [{string.Join(",", at)}] {lc.get_JoinType(end)}"));
            }
            return "위치선\t" + string.Join("\t", parts);
        }
        catch (Exception ex) { return "위치선\t읽지 못함: " + ex.Message; }
    }

    // 두 부재의 경계상자가 맞닿거나 겹치는가(1 mm 여유). 못 읽으면 닿는다고 본다 — 결합을 함부로 풀지 않게.
    private static bool BoxesMeet(Element a, Element b)
    {
        BoundingBoxXYZ? ba, bb;
        try { ba = a.get_BoundingBox(null); bb = b.get_BoundingBox(null); }
        catch { return true; }
        if (ba == null || bb == null) return true;
        return ba.Min.X <= bb.Max.X + Gap && bb.Min.X <= ba.Max.X + Gap &&
               ba.Min.Y <= bb.Max.Y + Gap && bb.Min.Y <= ba.Max.Y + Gap &&
               ba.Min.Z <= bb.Max.Z + Gap && bb.Min.Z <= ba.Max.Z + Gap;
    }

    // 두 부재가 맞닿거나 겹치는가(경계상자로 먼저 거르고, 입체가 겹친 체적으로 본다).
    private static bool Touches(Element a, Element b)
    {
        var ba = a.get_BoundingBox(null);
        var bb = b.get_BoundingBox(null);
        if (ba == null || bb == null) return false;
        if (ba.Max.X < bb.Min.X - Gap || bb.Max.X < ba.Min.X - Gap || ba.Max.Y < bb.Min.Y - Gap || bb.Max.Y < ba.Min.Y - Gap ||
            ba.Max.Z < bb.Min.Z - Gap || bb.Max.Z < ba.Min.Z - Gap) return false;
        foreach (var sa in SolidReader.Solids(a))
            foreach (var sb in SolidReader.Solids(b))
            {
                try
                {
                    var x = BooleanOperationsUtils.ExecuteBooleanOperation(sa, sb, BooleanOperationsType.Intersect);
                    if (x != null && x.Volume > 0) return true;
                }
                catch { return true; }   // 잴 수 없으면 겹친다고 본다(결합을 걸어 두는 쪽이 안전 — 겹침이 없으면 결합은 해가 없다)
            }
        return false;
    }

    // 못 나누는 까닭(나누지 않고 표시) — 없으면 null.
    private static string? WhyNot(Document doc, Element el)
    {
        try
        {
            if (FormulaAutoFill.WhyNotEditable(doc, el) is { } locked) return locked;
            if (el.AssemblyInstanceId != ElementId.InvalidElementId) return "조립품 안의 부재";
            if (PartUtils.HasAssociatedParts(doc, el.Id)) return "부품(Parts)이 있음";
            if (RebarHostData.GetRebarHostData(el) is { } host &&
                (host.GetRebarsInHost().Count > 0 || host.GetAreaReinforcementsInHost().Count > 0 ||
                 host.GetPathReinforcementsInHost().Count > 0 || host.GetFabricSheetsInHost().Count > 0))
                return "철근이 들어 있음";
            if (el is Wall w)
            {
                if (w.WallType?.Kind != WallKind.Basic) return "기본 벽이 아님(커튼월 · 적층 벽)";
                if (w.IsStackedWall) return "적층 벽";
                if (w.SketchId != ElementId.InvalidElementId) return "프로파일을 편집한 벽";
                if (w.CrossSection != WallCrossSection.Vertical) return "기운 벽";
                if (w.get_Parameter(BuiltInParameter.WALL_TOP_IS_ATTACHED)?.AsInteger() == 1 ||
                    w.get_Parameter(BuiltInParameter.WALL_BOTTOM_IS_ATTACHED)?.AsInteger() == 1) return "위나 아래가 다른 부재에 붙은 벽";
                if (w.FindInserts(true, true, true, true).Count > 0) return "문 · 창 · 구멍이 있음";
                if (new FilteredElementCollector(doc).OfClass(typeof(WallSweep)).Cast<WallSweep>().Any(s => s.GetHostIds().Contains(w.Id)))
                    return "벽 장식(걸레받이 · 줄눈)이 있음";
                if (w.Location is not LocationCurve) return "벽의 위치선이 없음";
            }
            else if (el is FamilyInstance fi)
            {
                if (fi.IsSlantedColumn) return "기운 기둥";
                if (ColumnAttachment.GetColumnAttachment(fi, 0) != null || ColumnAttachment.GetColumnAttachment(fi, 1) != null)
                    return "위나 아래가 다른 부재에 붙은 기둥";
                if (fi.Location is not LocationPoint) return "기둥의 위치점이 없음";
            }
            // 이 부재에 붙은 패밀리(면 기반 · 벽 기반 — 슬리브 등): 위 조각 자리에 붙은 것이 아래 조각에 남아 붙을 곳을 잃는다(R36 revit L4).
            // 문 · 창 확인(FindInserts)보다 뒤에 본다 — 문 · 창이 있는 벽에는 그쪽 까닭이 더 구체적이다(R37 revit R2).
            if (new FilteredElementCollector(doc).OfClass(typeof(FamilyInstance)).Cast<FamilyInstance>()
                    .Any(f => f.Id != el.Id && f.Host is { } on && on.Id == el.Id))
                return "붙은 패밀리가 있음";
            return null;
        }
        catch (Exception ex) { return "나눌 수 있는지 읽지 못함: " + ex.Message; }
    }

    // 결합으로 잘리기 전의 설계 높이(내부 좌표 · ft): 벽 = 위치선의 높이 + 높이 칸 · 기둥 = 위치점 + 길이.
    private static (double baseZ, double topZ) Extent(Element el)
    {
        if (el is Wall w && w.Location is LocationCurve lc)
        {
            double z = lc.Curve.GetEndPoint(0).Z;
            double h = w.get_Parameter(BuiltInParameter.WALL_USER_HEIGHT_PARAM)?.AsDouble() ?? double.NaN;
            return (z, z + h);
        }
        if (el is FamilyInstance fi && fi.Location is LocationPoint lp)
        {
            double z = lp.Point.Z;
            double len = fi.get_Parameter(BuiltInParameter.INSTANCE_LENGTH_PARAM)?.AsDouble() ?? double.NaN;
            return (z, z + len);
        }
        throw new InvalidOperationException("벽 · 기둥이 아님");
    }

    // 아래 끝을 Up 만큼 올리고, 위 끝을 Down 만큼 내린다(레벨은 그대로 · 오프셋만). 벽의 위가 «연결 안 됨»이면 높이 칸으로(SplitPlan).
    private static void Move(Element el, SplitPlan.Move move)
    {
        if (el is Wall w)
        {
            if (move.Up != 0) Add(w, BuiltInParameter.WALL_BASE_OFFSET, move.Up);
            bool unconnected = w.get_Parameter(BuiltInParameter.WALL_HEIGHT_TYPE)?.AsElementId() is not { } top || top == ElementId.InvalidElementId;
            if (unconnected)
            {
                double change = SplitPlan.UnconnectedHeightChange(move);
                if (change != 0) Add(w, BuiltInParameter.WALL_USER_HEIGHT_PARAM, change);
            }
            else if (move.Down != 0) Add(w, BuiltInParameter.WALL_TOP_OFFSET, -move.Down);
            return;
        }
        if (move.Up != 0) Add(el, BuiltInParameter.FAMILY_BASE_LEVEL_OFFSET_PARAM, move.Up);
        if (move.Down != 0) Add(el, BuiltInParameter.FAMILY_TOP_LEVEL_OFFSET_PARAM, -move.Down);
    }

    private static void Add(Element el, BuiltInParameter bip, double delta)
    {
        var p = el.get_Parameter(bip) ?? throw new InvalidOperationException($"칸이 없음({bip})");
        if (p.IsReadOnly) throw new InvalidOperationException($"칸이 읽기 전용({bip})");
        if (!p.Set(p.AsDouble() + delta)) throw new InvalidOperationException($"칸을 바꾸지 못함({bip})");
    }
}
