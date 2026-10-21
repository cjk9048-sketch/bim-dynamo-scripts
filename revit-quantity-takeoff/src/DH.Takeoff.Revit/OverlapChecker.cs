using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using Autodesk.Revit.DB;
using DH.Takeoff.Core;
using DH.Takeoff.Core.View3D;

namespace DH.Takeoff.Revit;

/// <summary>겹친 부재 한 쌍 — 형상 결합 없이 겹쳐 있어, 겹친 만큼이 양쪽 체적에 다 들어 있는(두 번 세는) 자리.</summary>
public sealed class OverlapPair
{
    public ElementId Winner = ElementId.InvalidElementId; // 우선순위상 통째로 세는 쪽(결합하면 자르는 쪽)
    public ElementId Loser = ElementId.InvalidElementId;  // 겹친 만큼 잘려야 하는 쪽
    public string WinnerName = "";
    public string LoserName = "";
    public double VolumeM3;            // 겹친 체적(m³)
    public string Status = "";         // 결합 결과("결합됨" / "결합 못 함: …"). 결합을 시도하지 않았으면 ""
    public double AfterM3 = double.NaN; // 결합한 뒤 다시 잰 겹침(m³). 재지 않았으면 NaN
    public bool Swallowed;             // 다시 재 보니 잘린 쪽(또는 그 조각)이 남는 쪽 안에 다 들어가 입체가 남지 않았다(같은 자리에 겹쳐 그린 부재일 수 있음 — JACK 2026-10-19)

    // ── 나누기(JACK 2026-10-14 — 결합으로 두 덩이가 된 벽 · 기둥) ──
    public string Split = "";             // "조각 n개" / "나누기 못 함: 까닭" / ""(대상 아님 · 안 함)
    public List<ElementId> Pieces = new(); // 잘린 쪽(Loser)을 나눈 조각 — 맨 앞 = Loser 자신(가장 큰 덩이를 맡음 · 나누지 않았으면 빈 목록)

    /// <summary>처음 찾기 뒤 다시 찾기(Refresh — 창에서 고친 뒤 · [우선순위대로 결합] 뒤)에서 새로 나온 쌍(JACK 2026-10-19).</summary>
    public bool New;

    // ── 그림용(겹침 검사 결과 창의 3D) — Find 에 withBlobs 를 준 때만 채운다. 쌍 · 체적 · 결합과는 상관없다 ──
    // 덩어리는 쌍에 붙여 둔다: 찾은 뒤에 쌍의 차례가 체적순으로 바뀌므로(Sorted), 따로 쌓아 번호로 맞추면 다른 쌍의 덩어리가 그려진다.
    public MeshBuilder? Blob;             // 겹친 덩어리의 겉면(모델 안쪽 좌표 · m — 그릴 때 Build 에 장면의 가운데를 넘긴다). 못 만들었으면 null
    public double BlobAreaM2 = double.NaN; // 겹친 입체의 겉넓이(Revit 이 준 값) — 만든 세모의 넓이 합과 견준다
    public bool BlobBoxed;                // 세모를 만들지 못해 경계상자로 대신했다
    public string BlobNote = "";          // 덩어리를 만들다 걸린 것("" = 없음)
}

/// <summary>
/// 결합 방향이 기준(겹친 자리 순위 — DeductRank)과 반대인 결합 한 쌍(R54 모델 검증 B): 이미 결합돼 겹치지는 않지만, 겹친 자리가 순위가 높은 쪽(Keep)에서
/// 빠져 있다(순위가 낮은 Cutter 가 Keep 을 자름 — 이번 헌치가 보를 자른 경우). 기준대로 바꾸면 Keep 이 그 자리를 얻고 Cutter 에서 빠진다.
/// </summary>
public sealed class ReversedJoin
{
    public ElementId Keep = ElementId.InvalidElementId;     // 기준대로면 남을 부재(지금은 잘림)
    public ElementId Cutter = ElementId.InvalidElementId;   // 지금 자르는 부재(기준대로면 잘릴 쪽)
    public string KeepName = "", CutterName = "", KeepPart = "", CutterPart = "";
    public int KeepRank, CutterRank;
    /// <summary>
    /// 순위도 «나중에 침»도 같고 카테고리 차례만 반대인 쌍 — 기준표가 말하지 않으므로 «참고»(수 · 일괄 · 알림에서 뺌 — core-verify R54 계획 M3).
    /// 같은 순위라도 한쪽만 «나중에 침»이면 나중 부재가 지는 것이 기준이다(참고 아님).
    /// </summary>
    public bool SameRank;
    /// <summary>사람이 일부러 정한 결합(모델 검증 창의 사용자 모드로 맞바꿔 결합한 쌍 — ZonePlan.Exceptions · revit-verify 계획 H3). 수 · 일괄 · 알림에서 뺀다.</summary>
    public bool Human;
    /// <summary>찾았을 때의 Human(창의 사용자 모드로 적은 기록이 묶음째 되돌려지면 이것으로 돌린다).</summary>
    public bool HumanAtFind;
    /// <summary>기준대로 바꾸면 Keep 이 얻는 체적(m³ — 시험 바꾸기로 잰 것 · NaN = 재지 않음).</summary>
    public double MovedM3 = double.NaN;
    /// <summary>그때 Cutter 가 잃는 체적(m³ — 일지 · 두 값이 다르면 셋째 부재가 끼었다).</summary>
    public double CutterLossM3 = double.NaN;
    /// <summary>재지 못한 까닭 · Revit 이 바꾸기를 거절한 까닭("" = 없음).</summary>
    public string Note = "";
    /// <summary>시험에서 Revit 이 바꾸기를 거절했다(그룹 · 남이 가진 부재 · 구조 결합 규칙 — 고치기 단추를 두지 않는다).</summary>
    public bool Refused;
    /// <summary>"" = 아직 · Switched = 기준대로 바꿈 · 그 밖 = 못 바꾼 까닭.</summary>
    public string Status = "";
    public const string Switched = "기준대로 바꿈";

    /// <summary>목록 · 일괄 · 알림에 드는 줄인가(사람이 정한 쌍 · 같은 순위 «참고»는 빼고).</summary>
    public bool Counts => !Human && !SameRank;
}

/// <summary>
/// 겹침 검사 — 수량을 뽑기 전에, 부재끼리(벽과 벽, 벽과 바닥·상부 슬래브 등) 겹쳐서 두 번 세는 자리를 찾는다.
/// Revit 에서 '형상 결합'이 된 쌍은 Revit 이 한쪽을 잘라 주므로 겹치지 않는다. 결합 없이 겹쳐만 있는 쌍은
/// 양쪽 체적에 겹친 부분이 다 들어 있고, 식도 판정도 각자 자기 체적과는 맞으므로 판정으로는 드러나지 않는다.
/// 찾은 쌍은 우선순위대로 형상 결합을 걸어(Join) Revit 이 한쪽을 잘라 내게 할 수 있다.
/// </summary>
public static class OverlapChecker
{
    /// <summary>이보다 작은 겹침(0.1 L)은 겹침으로 치지 않는다 — 맞닿은 면에서 나오는 계산 오차.</summary>
    public const double MinOverlapM3 = 1e-4;

    private const double FtToM = 0.3048;
    private const double Ft3ToM3 = FtToM * FtToM * FtToM;
    private const double BoxSlackFt = 0.001 / FtToM; // 경계상자가 1mm 넘게 겹쳐야 잰다(맞닿기만 한 쌍은 건너뛴다)

    public sealed class Found
    {
        public List<OverlapPair> Pairs = new();
        public int Checked;       // 검사한 부재 수
        public int Unmeasured;    // 겹쳤을 수 있지만 잴 수 없었던 쌍(Revit 이 두 입체의 겹침을 계산하지 못함)
        public int NotCoexisting; // 자리는 겹치지만 함께 있지 않아 뺀 쌍(디자인 옵션이 다르거나 공정이 겹치지 않음)
        public bool TimedOut;     // 시간 한도 때문에 끝까지 보지 못했다
        public double TotalM3 => Pairs.Sum(p => p.VolumeM3);
        // 결합 방향(B — R54 · JACK 2026-10-20 «보는 그대로 뽑고 헌치에서 제함»): 이미 결합됐는데 순위가 낮은 쪽이 높은 쪽을 자르는 쌍 — 겹친 자리가 기준과
        // 반대 부재에서 빠져 있다. 전의 «버림이 잘림»(LeanCut) · «나중 부재가 깎음»(LaterCut)도 이 안에 든다. FindReversed 가 찾고, 시험 바꾸기로 옮겨질 부피를 잰다.
        // [모두 기준대로] · 줄의 [기준대로 바꾸기] 때 FixLeanOrder 가 순서를 바꾼다(JACK 2026-10-14 «결합할 때 함께 바꾸기»). 바꾸고 나면 Status 가 Switched.
        public List<ReversedJoin> Reversed = new();
        public int ReversedSwitched;     // 바꾼 쌍의 수(이 실행)
        public int Touching;             // 반대로 결합됐지만 바꿔도 두 부재 체적이 같은(닿기만 한) 쌍 — 목록에 넣지 않고 수만(눌러도 아무 일 없는 줄 금지)
        public int ReversedUnmeasured;   // 옮겨질 부피를 재지 못한 쌍(시간 한도 · 읽기 전용 · 시험 오류) — 목록에는 남는다(고치기는 된다)
        public long ReversedMs;          // 결합 방향을 찾고 잰 시간
        public List<string> ReversedLines = new();   // 일지 줄(쌍마다 «자르는 쪽 → 잘리는 쪽 · 순위 · 옮겨질 부피»)
        public List<ElementId> FloorsGrown = new();   // 결합 순서를 바꿔 덩이가 는 바닥(나누지 않음 — 산출식이 «직접 넣어야 함»이 될 수 있어 알린다 · revit-verify R46 L5)

        /// <summary>목록 · 일괄 · 알림에 넣는 결합 방향 줄(사람이 정한 쌍 · 같은 순위의 «참고» 줄 · 이미 바꾼 줄은 빼고).</summary>
        public IEnumerable<ReversedJoin> ReversedToFix => Reversed.Where(r => r.Counts && r.Status.Length == 0);
        // 이미 결합돼(전에 건 결합 · Revit 이 저절로 건 결합) 결합 전보다 덩이가 많은 벽 · 구조 기둥 — Before = 결합 전 덩이 수 · Now = 지금 덩이 수.
        // [우선순위대로 결합] 때(겹친 쌍이 없으면 작은 창 단추) SplitJoined 가 덩이마다 나눈다(JACK 2026-10-15 «이미 결합된 두 덩이도 함께 나누기»). 나누고 나면 비운다.
        public List<(ElementId Id, int Before, int Now)> Unsplit = new();
        public List<string> UnsplitLines = new();               // 찾기가 두 덩이를 보며 남긴 일지 줄(넣지 않은 까닭 · 기둥의 덩이 수)
        // 이미 결합돼 위아래로 갈렸는데 옆으로도 갈린 벽 · 기둥(SplitPlan.Mixed — 기둥이 꿰뚫고 슬래브가 가른 벽 등): 나눌 수 없으니 목록에 넣지 않고,
        // 창이 그 수만 알린다(R39 core N-1 — 말없이 빠지면 JACK 이 왜 안 나뉘었는지 모른다 · 옆으로만 갈린 것은 알리지 않는다).
        public List<ElementId> UnsplitMixed = new();
        internal int ColumnLines, ColumnSkipped;                 // 기둥 줄의 수(한도까지만 적는다) · 한도 뒤로 적지 않은 기둥 수
        public int SplitDone;                                    // SplitJoined 가 나눈 부재 수
        public List<(ElementId Id, string Why)> SplitFailed = new(); // SplitJoined 가 못 나눈 부재와 까닭
        // 이 실행에서 나눈 부재 → 조각(맨 앞 = 원래 부재 · Join 의 나누기와 SplitJoined 둘 다 적는다 — 묶음을 합치지 못해 되돌리면 지운다).
        // 다시 찾기(Refresh)가 조각까지 보고, 원래 부재와 짝이던 쌍을 조각으로 옮기는 데 쓴다(R50 revit-verify ①②).
        public Dictionary<long, List<ElementId>> Splits = new();
    }

    /// <summary>
    /// 겹친 자리의 주인 = 겹친 자리 주인표(R54 — DeductRank · JACK 2026-10-20 «기준은 시공 순서 · 보는 그대로 뽑고 헌치에서 제함»): 부위 목록의 «겹친 자리»
    /// 순위가 높은(숫자가 작은) 부재가 남고, 같은 순위면 카테고리 차례(기초 › 바닥 › 기둥 › 벽 › 보 › 그 밖) → 먼저 만든(Id 가 작은) 부재.
    /// 버림은 무엇으로 그렸든 1(JACK 2026-10-14 «버림은 늘 남기기» — 지침 수도설계 부속서 9 «버림_B × 버림_H × L»). 부위가 빈 부재는 카테고리로
    /// (기초 · 바닥 2 · 기둥 4 · 벽 5 · 보 7 · 그 밖 9 — 옛 카테고리 차례와 같은 차례). 전의 «나중에 침은 늘 짐»(R46)은 순위표가 대신한다 —
    /// «나중에 침»은 거푸집 뜻으로만 남는다(FormworkAnalyzer.GivesWay).
    /// </summary>
    public static int RankOf(Document doc, Element el, DeductRank.Table table)
    {
        if (IsLean(doc, el)) return DeductRank.Lean;
        string part;
        try { part = ZoneScene.Text(el, ZoneScene.PartParam); }
        catch { part = ""; }
        return table.RankOf(part, CalcSheetExporter.KindOf(el), lean: false);
    }

    /// <summary>같은 순위일 때의 카테고리 차례(DeductRank.KindOrder).</summary>
    public static int KindOrderOf(Element el) => DeductRank.KindOrder(CalcSheetExporter.KindOf(el));

    /// <summary>모델에 저장된 부위 목록으로 지은 순위표(읽지 못하면 대표 목록의 처음 값).</summary>
    public static DeductRank.Table RankTable(Document doc)
    {
        try { return new DeductRank.Table(ZonePlanStore.Read(doc).Plan.Parts); }
        catch { return new DeductRank.Table(null); }
    }

    /// <summary>창 · 일지 · 결과표에 쓰는 기준 한 줄(순위표에서 만든다 — core-verify R54 계획 L2).</summary>
    public static string PriorityText => DeductRank.Summary;

    /// <summary>
    /// 부위 이름 → 나중에 치는 부위인가(거푸집 뜻 — R54 부터 겹친 자리 주인은 순위표가 정한다): 모델에 저장된 부위 목록의 '나중에 침'(없는 이름 · 저장된 목록이
    /// 없거나 읽지 못하면 대표 목록의 처음 값 — 받침·PAD 묶음). 수량산출 창의 거푸집(① 의 목록)과 같은 규칙이다(PartCatalog.IsLater).
    /// </summary>
    public static Func<string, bool> LaterParts(Document doc)
    {
        var saved = new Dictionary<string, bool>(StringComparer.Ordinal);
        try
        {
            foreach (var p in ZonePlanStore.Read(doc).Plan.Parts) saved[p.Name] = PartCatalog.IsLater(p);
        }
        catch { saved.Clear(); }
        return name =>
        {
            name = ZonePlan.CleanName(name);
            return name.Length > 0 && (saved.TryGetValue(name, out bool later) ? later : PartCatalog.IsLater(PartCatalog.NewRow(name)));
        };
    }

    /// <summary>나중에 치는 부재인가 — 부위 칸(DH_Part)의 이름으로 본다(유형 이름으로 짐작하지 않는다). 읽지 못하면 아니라고 본다.</summary>
    public static bool IsLater(Element el, Func<string, bool> laterPart)
    {
        try { return laterPart(ZoneScene.Text(el, ZoneScene.PartParam)); }
        catch { return false; }
    }

    /// <summary>
    /// 버림 콘크리트인가 — 유형 이름 · 가장 많이 차지하는 재료 이름 · 부위 칸(DH_Part)에 '버림'(lean concrete · blinding)이 있다(CalcPlan.IsLean).
    /// 이름으로 모르는 버림(송악3 의 'SB_T100(기초)')은 「위치·부위 정하기」에서 부위를 '버림 콘크리트'로 넣으면 알아본다. 읽지 못하면 버림이 아니라고 본다.
    /// </summary>
    public static bool IsLean(Document doc, Element el)
    {
        try
        {
            string type = (doc.GetElement(el.GetTypeId()) as ElementType)?.Name ?? el.Name ?? "";
            return CalcPlan.IsLean(type, ConcreteFilter.Classify(doc, el).Material, ZoneScene.Text(el, ZoneScene.PartParam));
        }
        catch { return false; }
    }

    private sealed class Item
    {
        public required Element El;
        public required List<Solid> Solids;
        public required XYZ Min, Max;
        public int Rank, Order;                             // 겹친 자리 순위 · 같은 순위일 때의 카테고리 차례(DeductRank)
        public bool Later;                                  // 부위 목록의 «나중에 침» — 같은 순위면 진다(DeductRank.Beats 둘째 열쇠)
        public long Option = -1, OptionSet = -1;            // 디자인 옵션과 그 세트(주 모델이면 -1)
        public int Created = -1, Demolished = int.MaxValue; // 만든 공정·철거한 공정의 차례(없으면 처음부터·끝까지)
    }

    /// <summary>
    /// 두 부재가 함께 있을 수 있는가. 같은 옵션 세트의 서로 다른 디자인 옵션에 든 부재, 한쪽이 철거된 뒤에 다른 쪽이 생기는
    /// 부재는 자리가 겹쳐도 함께 있지 않으므로 두 번 세는 것이 아니다(결합을 걸어서도 안 된다).
    /// </summary>
    private static bool CanCoexist(Item a, Item b)
    {
        if (a.Option >= 0 && b.Option >= 0 && a.Option != b.Option && a.OptionSet == b.OptionSet) return false;
        return a.Created < b.Demolished && b.Created < a.Demolished;
    }

    // 부재의 디자인 옵션과 공정을 읽어 둔다. 읽지 못하면 '늘 있는 부재'로 둔다(겹침을 놓치지 않는 쪽).
    private static void ReadExistence(Item item, Dictionary<long, int> phaseOrder)
    {
        try
        {
            var option = item.El.DesignOption;
            if (option != null)
            {
                item.Option = option.Id.Value;
                item.OptionSet = option.get_Parameter(BuiltInParameter.OPTION_SET_ID)?.AsElementId()?.Value ?? -1;
            }
        }
        catch { item.Option = item.OptionSet = -1; }
        try
        {
            if (phaseOrder.TryGetValue(item.El.CreatedPhaseId.Value, out int created)) item.Created = created;
            if (phaseOrder.TryGetValue(item.El.DemolishedPhaseId.Value, out int demolished)) item.Demolished = demolished;
        }
        catch { item.Created = -1; item.Demolished = int.MaxValue; }
    }

    /// <summary>
    /// ids 가운데 결합 없이 겹친 쌍을 찾는다. 모델은 바꾸지 않는다. limit = 시간 한도(없으면 끝까지).
    /// withBlobs = 쌍마다 겹친 덩어리의 겉면도 만들어 둔다(결과 창의 3D 가 그린다). 주지 않으면 쌍 · 체적만 낸다 — 내는 값은 어느 쪽이든 같다.
    /// withUnsplit = 이미 결합돼 두 덩이인 벽 · 기둥도 센다(Found.Unsplit — 겹침 검사만 · 다른 명령의 15초 미리 보기에서는 덩이를 세는 시간을 쓰지 않는다).
    /// </summary>
    public static Found Find(Document doc, ICollection<ElementId> ids, TimeSpan? limit = null, bool withBlobs = false, bool withUnsplit = false)
    {
        var found = new Found();
        var items = new List<Item>();
        var table = RankTable(doc);                     // 겹친 자리 순위 — 저장된 부위 목록으로 한 번 정한다
        var laterPart = LaterParts(doc);                // «나중에 침» — 같은 순위에서 진다
        var phaseOrder = new Dictionary<long, int>(); // 공정 Id → 차례
        try
        {
            int order = 0;
            foreach (Phase phase in doc.Phases) phaseOrder[phase.Id.Value] = order++;
        }
        catch { phaseOrder.Clear(); }
        foreach (var id in ids)
        {
            var el = doc.GetElement(id);
            if (el == null) continue;
            List<Solid> solids;
            BoundingBoxXYZ? bb;
            try { solids = SolidReader.Solids(el); bb = el.get_BoundingBox(null); }
            catch { continue; }
            if (solids.Count == 0 || bb == null) continue;
            var item = new Item { El = el, Solids = solids, Min = bb.Min, Max = bb.Max, Rank = RankOf(doc, el, table), Order = KindOrderOf(el), Later = IsLater(el, laterPart) };
            ReadExistence(item, phaseOrder);
            items.Add(item);
        }
        found.Checked = items.Count;
        items.Sort((a, b) => a.Min.X.CompareTo(b.Min.X));

        var watch = Stopwatch.StartNew();
        for (int i = 0; i < items.Count; i++)
        {
            var a = items[i];
            for (int j = i + 1; j < items.Count; j++)
            {
                var b = items[j];
                if (b.Min.X > a.Max.X - BoxSlackFt) break; // X 로 줄 세웠으므로 이 뒤는 a 와 겹칠 수 없다
                if (!BoxesOverlap(a, b)) continue;
                if (limit != null && watch.Elapsed > limit) { found.TimedOut = true; return Sorted(found); }
                if (!CanCoexist(a, b)) { found.NotCoexisting++; continue; } // 디자인 옵션·공정이 달라 함께 있지 않는 쌍

                // 이미 형상 결합된 쌍은 Revit 이 한쪽을 잘라 놓았다 — 겹치지 않는다. 자르는 방향이 기준과 반대인지는 따로 한 바퀴(FindReversed — 닿기만 한
                // 결합은 상자 여유 때문에 이 순회에 들지 않고 · 시간 한도도 따로다 — revit-verify R54 계획 M2). 판정 오류가 나도 결합된 쌍은 늘 건너뛴다(R36 revit L7).
                bool joinedAlready = false;
                try { joinedAlready = JoinGeometryUtils.AreElementsJoined(doc, a.El, b.El); }
                catch { }
                if (joinedAlready) continue;

                var pieces = withBlobs ? new List<Solid>() : null;
                double m3 = Intersection(a.Solids, b.Solids, out bool failed, pieces);
                if (m3 < MinOverlapM3)
                {
                    if (failed) found.Unmeasured++;
                    continue;
                }
                var (win, lose) = Beats(a, b) ? (a, b) : (b, a);
                var pair = new OverlapPair
                {
                    Winner = win.El.Id, Loser = lose.El.Id, WinnerName = Name(doc, win.El), LoserName = Name(doc, lose.El), VolumeM3 = m3,
                };
                if (pieces != null) AttachBlob(pair, pieces);
                found.Pairs.Add(pair);
            }
        }
        if (withUnsplit)
        {
            foreach (var item in items) NoteUnsplit(doc, item.El, found);
            if (found.ColumnSkipped > 0)
                found.UnsplitLines.Add($"기둥 줄은 {ColumnLinesInJournal}개까지 — 나머지 {found.ColumnSkipped}개는 적지 않음(잘린 것은 그대로 봄)");
        }
        return Sorted(found);
    }

    private const int ColumnLinesInJournal = 100;   // 일지에 적는 기둥 줄의 한도(기둥이 아주 많은 모델)

    /// <summary>
    /// 결합 방향(B — R54 · revit-verify 계획 M2): 대상 부재마다 결합된 상대(GetJoinedElements — 쌍은 한 번)를 보고, 순위가 낮은 쪽이 높은 쪽을 자르면 found.Reversed 에 넣는다.
    /// 상대는 모델의 대상(CollectApplicable) 가운데 콘크리트로 분류되지 않은 것을 뺀 것 — 선택 실행이어도 선택 밖 상대는 본다(NoteUnsplit 과 같은 까닭).
    /// 순위도 카테고리 차례도 같은 쌍은 기준이 말하지 않으므로 보지 않는다 · 순위만 같은 쌍은 «참고»(SameRank). 사람이 정한 결합(exceptions)은 Human.
    /// measure = 시험 바꾸기로 옮겨질 부피를 잰다(MeasureReversed — 트랜잭션을 열고 되돌린다). 재지 않으면(빠른 검사) 수만 센다.
    /// 「형상 잘라내기」(SolidSolidCut)는 GetJoinedElements 에 나오지 않아 대상이 아니다. 모델은 바꾸지 않는다.
    /// </summary>
    public static void FindReversed(Document doc, IEnumerable<ElementId> targets, Found found, IReadOnlyList<JoinException>? exceptions,
                                    bool measure, TimeSpan? limit = null)
    {
        var watch = Stopwatch.StartNew();
        found.Reversed.Clear();
        found.Touching = found.ReversedUnmeasured = 0;
        found.ReversedLines.Clear();
        var table = RankTable(doc);
        var applicable = new HashSet<long>(DimensionExtractor.CollectApplicable(doc).Select(id => id.Value));
        var concrete = new Dictionary<long, bool>();
        bool Concrete(Element el)
        {
            if (!concrete.TryGetValue(el.Id.Value, out bool yes))
            {
                try { yes = ConcreteFilter.Classify(doc, el).Kind != ConcreteFilter.Kind.NotConcrete; }
                catch { yes = false; }
                concrete[el.Id.Value] = yes;
            }
            return yes;
        }
        var laterPart = LaterParts(doc);
        var ranks = new Dictionary<long, (int rank, bool later, int order)>();
        (int rank, bool later, int order) Ranked(Element el)
        {
            if (!ranks.TryGetValue(el.Id.Value, out var r)) ranks[el.Id.Value] = r = (RankOf(doc, el, table), IsLater(el, laterPart), KindOrderOf(el));
            return r;
        }
        var human = new HashSet<(string, string)>((exceptions ?? Array.Empty<JoinException>()).Select(e => (e.Keep, e.Cut)));
        var seen = new HashSet<(long, long)>();
        foreach (var id in targets)
        {
            var el = doc.GetElement(id);
            if (el == null) continue;
            ICollection<ElementId> joined;
            try { joined = JoinGeometryUtils.GetJoinedElements(doc, el); }
            catch { continue; }
            foreach (var oid in joined)
            {
                if (!seen.Add(KeyOf(id, oid)) || !applicable.Contains(oid.Value)) continue;
                var other = doc.GetElement(oid);
                if (other == null || !Concrete(other)) continue;
                var (ra, la, oa) = Ranked(el);
                var (rb, lb, ob) = Ranked(other);
                bool decided = DeductRank.Decided(ra, la, rb, lb);              // 순위 · «나중에 침»으로 갈림(같은 순위면 나중 부재가 진다)
                if (!decided && oa == ob) continue;                              // 기준이 말하지 않는 쌍(카테고리까지 같음 — Id 로만 갈린다)
                var (keep, cutter) = DeductRank.Beats(ra, la, oa, id.Value, rb, lb, ob, oid.Value) ? (el, other) : (other, el);
                bool fine;
                try { fine = JoinGeometryUtils.IsCuttingElementInJoin(doc, keep, cutter); }
                catch { continue; }                                               // 결합 상태를 읽지 못하면 넘어간다
                if (fine) continue;
                var (rk, rc) = keep.Id == el.Id ? (ra, rb) : (rb, ra);
                string ku = SafeUid(keep), cu = SafeUid(cutter);
                found.Reversed.Add(new ReversedJoin
                {
                    Keep = keep.Id, Cutter = cutter.Id, KeepName = Name(doc, keep), CutterName = Name(doc, cutter),
                    KeepPart = PartText(keep), CutterPart = PartText(cutter), KeepRank = rk, CutterRank = rc,
                    SameRank = !decided, Human =ku.Length > 0 && cu.Length > 0 && human.Contains((cu, ku)), HumanAtFind = ku.Length > 0 && cu.Length > 0 && human.Contains((cu, ku)),
                });
            }
        }
        if (measure) MeasureReversed(doc, found, limit);
        found.ReversedMs = watch.ElapsedMilliseconds;
        foreach (var r in found.Reversed) found.ReversedLines.Add(ReversedLine(r));
    }

    private static readonly TimeSpan QuickLimit = TimeSpan.FromSeconds(5);   // 빠른 검사의 잘리기 전 모양 교차 한도

    /// <summary>
    /// [수량산출] 시작의 빠른 결합 방향 검사(revit-verify R54 계획 H4 — 알림 수 = 창 수가 되게, 트랜잭션 없이): FindReversed(재지 않음) 뒤
    /// 닿기만 한 결합을 거른다 — ① Revit 경고 «결합됐지만 안 닿음»(JoiningDisjoint · JoiningDisjointWarn)에 든 쌍 ② 지금 잘린 쪽이 패밀리 인스턴스(보 · 기둥)면
    /// 잘리기 전 모양(GetOriginalGeometry)과 자르는 쪽 입체가 겹치지 않는 쌍. 그래도 못 가르는 쌍(잘린 쪽이 벽 · 바닥 — 잘리기 전 모양을 못 읽음)은 «최대»로 센다.
    /// 돌려주는 값 = (겹침이 확실한 수, 최대 수). 사람이 정한 쌍 · 참고 줄은 세지 않는다. 모델은 바꾸지 않는다.
    /// </summary>
    public static (int sure, int most) QuickReversed(Document doc, IEnumerable<ElementId> targets, Found found)
    {
        IReadOnlyList<JoinException>? exceptions = null;
        try { exceptions = ZonePlanStore.Read(doc).Plan.Exceptions; } catch { }
        FindReversed(doc, targets, found, exceptions, measure: false);
        var disjoint = new HashSet<(long, long)>();
        try
        {
            var ids = new[] { BuiltInFailures.JoinElementsFailures.JoiningDisjoint, BuiltInFailures.JoinElementsFailures.JoiningDisjointWarn };
            foreach (var warning in doc.GetWarnings())
            {
                if (!ids.Contains(warning.GetFailureDefinitionId())) continue;
                var failing = warning.GetFailingElements().ToList();
                for (int i = 0; i < failing.Count; i++)
                    for (int j = i + 1; j < failing.Count; j++) disjoint.Add(KeyOf(failing[i], failing[j]));
            }
        }
        catch { disjoint.Clear(); }
        int sure = 0, most = 0;
        var watch = Stopwatch.StartNew();
        foreach (var r in found.Reversed.Where(r => r.Counts).ToList())
        {
            if (disjoint.Contains(KeyOf(r.Keep, r.Cutter))) { found.Reversed.Remove(r); found.Touching++; continue; }
            // 교차는 5 초까지만(revit-verify R54 설치 전 L4 — [수량산출] 시작이 더 늦지 않게) · 넘으면 남은 쌍은 «최대»로 센다.
            if (watch.Elapsed > QuickLimit) { most++; continue; }
            switch (OriginalOverlaps(doc, r))
            {
                case true: sure++; most++; break;
                case false: found.Reversed.Remove(r); found.Touching++; break;
                default: most++; break;
            }
        }
        return (sure, most);
    }

    // 지금 잘린 쪽(Keep)이 패밀리 인스턴스면 잘리기 전 모양과 자르는 쪽 입체가 겹치는가(true · false). 가를 수 없으면 null(벽 · 바닥 · 읽지 못함 — «최대»로 센다).
    // 잘리기 전 모양은 패밀리(심벌) 좌표다 — 인스턴스의 변환을 건다(RevitAPI.xml 2026 GetOriginalGeometry · revit-verify R54 설치 전 M2). 그 자리가 맞는지는
    // «지금 상자 ⊆ 잘리기 전 상자 + 여유»로 본다(결합 · 자르기는 빼기만 하므로 늘 참이다 — 늘림 등으로 거짓이면 모름).
    private static bool? OriginalOverlaps(Document doc, ReversedJoin r)
    {
        try
        {
            if (doc.GetElement(r.Keep) is not FamilyInstance keep || doc.GetElement(r.Cutter) is not Element cutter) return null;
            var geometry = keep.GetOriginalGeometry(SolidReader.ReadOptions());
            if (geometry == null) return null;
            var now = keep.get_BoundingBox(null);
            if (now == null) return null;
            var original = SolidReader.Solids(geometry.GetTransformed(keep.GetTransform()));
            if (original.Count == 0) return null;
            XYZ? lo = null, hi = null;
            foreach (var solid in original)
            {
                var b = solid.GetBoundingBox();
                foreach (var corner in new[] { b.Min, b.Max, new XYZ(b.Min.X, b.Min.Y, b.Max.Z), new XYZ(b.Max.X, b.Max.Y, b.Min.Z),
                                               new XYZ(b.Min.X, b.Max.Y, b.Min.Z), new XYZ(b.Max.X, b.Min.Y, b.Max.Z), new XYZ(b.Min.X, b.Max.Y, b.Max.Z), new XYZ(b.Max.X, b.Min.Y, b.Min.Z) })
                {
                    var p = b.Transform.OfPoint(corner);
                    lo = lo == null ? p : new XYZ(Math.Min(lo.X, p.X), Math.Min(lo.Y, p.Y), Math.Min(lo.Z, p.Z));
                    hi = hi == null ? p : new XYZ(Math.Max(hi.X, p.X), Math.Max(hi.Y, p.Y), Math.Max(hi.Z, p.Z));
                }
            }
            const double slack = 0.01 / FtToM;   // 1 cm
            if (lo == null || hi == null || now.Min.X < lo.X - slack || now.Min.Y < lo.Y - slack || now.Min.Z < lo.Z - slack ||
                now.Max.X > hi.X + slack || now.Max.Y > hi.Y + slack || now.Max.Z > hi.Z + slack) return null;
            double m3 = Intersection(original, SolidReader.Solids(cutter), out bool failed);
            if (failed) return null;
            return m3 >= MinOverlapM3;
        }
        catch { return null; }
    }

    private static string SafeUid(Element el)
    {
        try { return el.UniqueId ?? ""; } catch { return ""; }
    }

    private static string PartText(Element el)
    {
        try { return ZoneScene.Text(el, ZoneScene.PartParam).Trim(); } catch { return ""; }
    }

    /// <summary>일지 한 줄 — «결합 방향 · 자르는 쪽 → 잘리는 쪽 · 순위 · 옮겨질 부피».</summary>
    public static string ReversedLine(ReversedJoin r) =>
        $"결합 방향\t{r.Cutter.Value.ToString(CultureInfo.InvariantCulture)}({(r.CutterPart.Length > 0 ? r.CutterPart : "부위 없음")} · {DeductRank.Label(r.CutterRank)}) 이 자름 → " +
        $"{r.Keep.Value.ToString(CultureInfo.InvariantCulture)}({(r.KeepPart.Length > 0 ? r.KeepPart : "부위 없음")} · {DeductRank.Label(r.KeepRank)})\t" +
        $"옮겨질 {(double.IsNaN(r.MovedM3) ? "-" : r.MovedM3.ToString("0.######", CultureInfo.InvariantCulture))} m3 · 자르는 쪽 변화 {(double.IsNaN(r.CutterLossM3) ? "-" : r.CutterLossM3.ToString("0.######", CultureInfo.InvariantCulture))} m3" +
        (r.SameRank ? "\t참고(같은 순위)" : "") + (r.Human ? "\t사람이 정함" : "") + (r.Note.Length > 0 ? "\t" + r.Note : "") + (r.Status.Length > 0 ? "\t" + r.Status : "");

    /// <summary>
    /// 결합 방향 쌍마다 기준대로 바꾸면 옮겨질 부피를 잰다(revit-verify R54 계획 M1): 시험 트랜잭션에서 SwitchJoinOrder → Regenerate → 두 부재의 입체 체적(SolidReader —
    /// 볼륨 매개변수 아님) → RollBack. 바꿀 수 없는 부재(그룹 · 남이 가진 부재 · 활성 아닌 디자인 옵션 — WhyNotEditable)는 시험 없이 «못 바꿈».
    /// 서로 부재를 나누지 않는 쌍끼리 한 시험에 묶어 Regenerate 한 번(체적 변화가 그 쌍의 것이게). 바꾼 뒤 정말 기준대로 잘리는지 다시 본다(오류는 Commit 때만
    /// 처리되고 RollBack 은 QuietFailures 를 부르지 않는다). 시험 안에서는 판정 · 모양 기록을 건드리지 않는다(롤백 — 남는 것 없음).
    /// 옮겨질 부피가 0(1e-4 m³ 미만 — 닿기만 한 결합)이면 목록에서 빼고 수만 센다(Touching — 눌러도 아무 일 없는 줄 금지). 시간 한도를 넘으면 남은 쌍은 «안 잼».
    /// 문서를 고칠 수 없으면(읽기 전용 — 트랜잭션을 열 수 없음) 하나도 재지 않는다.
    /// </summary>
    public static void MeasureReversed(Document doc, Found found, TimeSpan? limit)
    {
        var watch = Stopwatch.StartNew();
        var todo = new List<ReversedJoin>();
        foreach (var r in found.Reversed)
        {
            if (!double.IsNaN(r.MovedM3) || r.Status.Length > 0) continue;
            var keep = doc.GetElement(r.Keep);
            var cutter = doc.GetElement(r.Cutter);
            if (keep == null || cutter == null) { r.Note = "부재를 찾을 수 없음"; r.Refused = true; continue; }
            string? blocked = FormulaAutoFill.WhyNotEditable(doc, keep) ?? FormulaAutoFill.WhyNotEditable(doc, cutter);
            if (blocked != null) { r.Note = "못 바꿈: " + blocked; r.Refused = true; continue; }
            todo.Add(r);
        }
        if (doc.IsReadOnly)
        {
            foreach (var r in todo) r.Note = "안 잼(읽기 전용 문서)";
            found.ReversedUnmeasured += todo.Count;
            return;
        }
        // 서로 부재를 나누지 않는 쌍끼리 묶는다(먼저 온 묶음에 넣을 수 있으면 넣는다).
        var batches = new List<(List<ReversedJoin> pairs, HashSet<long> ids)>();
        foreach (var r in todo)
        {
            var slot = batches.FirstOrDefault(b => !b.ids.Contains(r.Keep.Value) && !b.ids.Contains(r.Cutter.Value));
            if (slot.pairs == null) { slot = (new List<ReversedJoin>(), new HashSet<long>()); batches.Add(slot); }
            slot.pairs.Add(r);
            slot.ids.Add(r.Keep.Value);
            slot.ids.Add(r.Cutter.Value);
        }
        bool Late() => limit != null && watch.Elapsed > limit;
        void Unmeasured(IEnumerable<ReversedJoin> pairs)
        {
            foreach (var r in pairs) (r.MovedM3, r.CutterLossM3, r.Note) = (double.NaN, double.NaN, "안 잼(시간 한도)");
            found.ReversedUnmeasured += pairs.Count();
        }
        // 한 시험 트랜잭션: 쌍들을 바꾸고 Regenerate 한 번 · 두 부재 체적 · RollBack.
        void Trial(List<ReversedJoin> pairs)
        {
            var before = new Dictionary<long, double>();
            foreach (var r in pairs)
            {
                before[r.Keep.Value] = VolumeM3(doc.GetElement(r.Keep));
                before[r.Cutter.Value] = VolumeM3(doc.GetElement(r.Cutter));
            }
            using var tx = new Transaction(doc, "DH 모델 검증 — 작성오류 시험");
            try
            {
                if (tx.Start() != TransactionStatus.Started) throw new InvalidOperationException("시험 트랜잭션을 열 수 없음");
                var options = tx.GetFailureHandlingOptions();
                options.SetFailuresPreprocessor(new QuietFailures());
                options.SetClearAfterRollback(true);
                tx.SetFailureHandlingOptions(options);
                var tried = new List<ReversedJoin>();
                foreach (var r in pairs)
                {
                    try
                    {
                        var keep = doc.GetElement(r.Keep);
                        var cutter = doc.GetElement(r.Cutter);
                        if (!JoinGeometryUtils.IsCuttingElementInJoin(doc, keep, cutter)) JoinGeometryUtils.SwitchJoinOrder(doc, keep, cutter);
                        tried.Add(r);
                    }
                    catch (Exception ex)
                    {
                        r.Refused = true;
                        r.Note = "못 바꿈: " + (ex is Autodesk.Revit.Exceptions.ApplicationException ? "Revit 이 이 두 부재의 자르는 순서를 바꾸지 못하게 함 (" + ex.Message + ")" : ex.Message);
                    }
                }
                doc.Regenerate();
                foreach (var r in tried)
                {
                    var keep = doc.GetElement(r.Keep);
                    var cutter = doc.GetElement(r.Cutter);
                    bool switched = keep != null && cutter != null && JoinGeometryUtils.AreElementsJoined(doc, keep, cutter)
                                    && JoinGeometryUtils.IsCuttingElementInJoin(doc, keep, cutter);
                    if (!switched) { r.Refused = true; r.Note = "못 바꿈: 자르는 순서를 바꾸지 못함"; continue; }
                    double keepAfter = VolumeM3(keep), cutterAfter = VolumeM3(cutter);
                    if (double.IsNaN(keepAfter) || double.IsNaN(cutterAfter) || double.IsNaN(before[r.Keep.Value]) || double.IsNaN(before[r.Cutter.Value]))
                    {
                        r.Note = "안 잼(입체를 읽지 못함)";
                        found.ReversedUnmeasured++;
                        continue;
                    }
                    r.MovedM3 = keepAfter - before[r.Keep.Value];
                    r.CutterLossM3 = before[r.Cutter.Value] - cutterAfter;
                }
            }
            catch (Exception ex)
            {
                foreach (var r in pairs.Where(r => double.IsNaN(r.MovedM3) && !r.Refused)) r.Note = "안 잼(시험 오류: " + ex.Message + ")";
                found.ReversedUnmeasured += pairs.Count(r => double.IsNaN(r.MovedM3) && !r.Refused);
            }
            finally
            {
                try { if (tx.HasStarted() && !tx.HasEnded()) tx.RollBack(); } catch { }
            }
        }
        static bool Touches(ReversedJoin r) => !double.IsNaN(r.MovedM3) && Math.Abs(r.MovedM3) < MinOverlapM3 && Math.Abs(r.CutterLossM3) < MinOverlapM3;
        foreach (var (pairs, _) in batches)
        {
            if (Late()) Unmeasured(pairs);
            else Trial(pairs);
        }
        // 묶어서 잰 쌍 가운데 바꿔도 같게 나온 것은 홀로 한 번 더 잰다 — 같은 이웃(모서리 기둥 · 슬래브)을 나눠 가진 다른 쌍의 바꾸기가 그 쌍의 체적을
        // 바꿔 «닿기만 함»으로 잘못 빠지지 않게(revit-verify R54 설치 전 L5). 시간이 없으면 빼지 않고 «안 잼»으로 남긴다.
        foreach (var r in batches.Where(b => b.pairs.Count > 1).SelectMany(b => b.pairs).Where(Touches).ToList())
        {
            if (Late()) { Unmeasured(new[] { r }); continue; }
            (r.MovedM3, r.CutterLossM3) = (double.NaN, double.NaN);
            Trial(new List<ReversedJoin> { r });
        }
        // 닿기만 한 결합(바꿔도 두 부재 체적이 같음)은 목록에서 뺀다.
        int before0 = found.Reversed.Count;
        found.Reversed.RemoveAll(Touches);
        found.Touching += before0 - found.Reversed.Count;
        // 많이 옮겨지는 것부터(재지 못한 것은 뒤 — 쌍의 차례 = 창의 열쇠라 찾은 뒤 한 번만 정한다).
        found.Reversed = found.Reversed.OrderByDescending(r => double.IsNaN(r.MovedM3) ? -1 : Math.Abs(r.MovedM3)).ThenBy(r => r.Keep.Value).ThenBy(r => r.Cutter.Value).ToList();
    }

    // 부재 입체 체적의 합(m³). 부재가 없거나 입체를 읽지 못하면 NaN.
    private static double VolumeM3(Element? el)
    {
        if (el == null) return double.NaN;
        try { return SolidReader.Solids(el).Sum(s => s.Volume) * Ft3ToM3; }
        catch { return double.NaN; }
    }

    /// <summary>
    /// 이미 결합돼(전에 건 결합 · Revit 이 저절로 건 결합) 결합 전보다 덩이가 많은 벽 · 구조 기둥이면 found.Unsplit 에 적는다(JACK 2026-10-15).
    /// 부재마다 결합된 상대(GetJoinedElements)를 보고, 그 가운데 하나라도 이 부재를 자르면 본다 — 대상끼리의 쌍이 아니므로 선택 실행이나
    /// 콘크리트로 분류되지 않은 상대가 자른 것도 잡힌다(R38 revit P1). 위아래로 틈을 두고 쌓인 것만 목록에 넣는다 — 옆으로 갈림(벽을 꿰뚫은 기둥) ·
    /// 맞닿음은 일지만(R38 revit P2). 적층 · 커튼월 · 프로파일을 편집한 벽은 결합과 상관없이 여러 덩이일 수 있어 보지 않는다(P6).
    /// 결합 전 모양을 못 읽으면 넣지 않고 일지만(P3). 구조 기둥은 모두 일지에 «원래 입체 · 원래 덩이 · 지금 덩이»를 적는다 —
    /// GetOriginalGeometry 가 입체를 합치는지 첫 실행에서 가르려고(R38 core M-3).
    /// </summary>
    private static void NoteUnsplit(Document doc, Element el, Found found)
    {
        try
        {
            if (!OverlapSplitter.IsTarget(el)) return;
            if (el is Wall w && (w.WallType?.Kind != WallKind.Basic || w.IsStackedWall || w.SketchId != ElementId.InvalidElementId)) return;
            // 상대마다 따로 본다 — 한 상대에서 판정 오류가 나도 다른 상대가 자르면 잡히게(R39 revit N-1).
            bool cut = JoinGeometryUtils.GetJoinedElements(doc, el).Any(id =>
            {
                try { return doc.GetElement(id) is Element other && JoinGeometryUtils.IsCuttingElementInJoin(doc, other, el); }
                catch { return false; }
            });
            bool logColumn = el is FamilyInstance && found.ColumnLines < ColumnLinesInJournal;
            if (el is FamilyInstance && !logColumn) found.ColumnSkipped++;
            if (!cut && !logColumn) return;
            int now = OverlapSplitter.LumpCount(el);
            int before = OverlapSplitter.OriginalLumpCount(el, out int solids);
            string head = $"\tId {el.Id.Value}\t덩이 {now}(결합 전 {before})";
            if (logColumn)
            {
                found.ColumnLines++;
                found.UnsplitLines.Add($"기둥{head}\t원래 입체 {solids}{(cut ? "" : "\t자르는 결합 없음")}");
            }
            if (!cut || now < 2) return;
            if (before <= 0) { found.UnsplitLines.Add($"두 덩이 안 넣음{head}\t결합 전 모양을 읽지 못함"); return; }
            if (now <= before) return;
            if (OverlapSplitter.StackReason(el) is { } notStacked)
            {
                if (notStacked == SplitPlan.Mixed) found.UnsplitMixed.Add(el.Id);
                found.UnsplitLines.Add($"두 덩이 안 넣음{head}\t{notStacked}");
                return;
            }
            found.Unsplit.Add((el.Id, before, now));
        }
        catch (Exception ex) { found.UnsplitLines.Add($"두 덩이 안 넣음\tId {el.Id.Value}\t읽다 오류: {ex.Message}"); }
    }

    private static Found Sorted(Found f)
    {
        f.Pairs = f.Pairs.OrderByDescending(p => p.VolumeM3).ThenBy(p => p.Winner.Value).ThenBy(p => p.Loser.Value).ToList();
        return f;
    }

    private static bool BoxesOverlap(Item a, Item b) =>
        Math.Min(a.Max.X, b.Max.X) - Math.Max(a.Min.X, b.Min.X) > BoxSlackFt &&
        Math.Min(a.Max.Y, b.Max.Y) - Math.Max(a.Min.Y, b.Min.Y) > BoxSlackFt &&
        Math.Min(a.Max.Z, b.Max.Z) - Math.Max(a.Min.Z, b.Min.Z) > BoxSlackFt;

    // a 가 b 를 이긴다(겹친 자리를 가진다) = 순위가 높다 → 같으면 «나중에 침»이 아닌 쪽 → 카테고리 차례 → Id 가 작다(DeductRank.Beats).
    private static bool Beats(Item a, Item b) => DeductRank.Beats(a.Rank, a.Later, a.Order, a.El.Id.Value, b.Rank, b.Later, b.Order, b.El.Id.Value);

    /// <summary>
    /// 두 부재의 입체가 겹친 체적(m³). 일부라도 계산하지 못했으면 failed.
    /// pieces 를 주면 체적에 셈한 겹친 입체를 거기에 모아 준다(그림용) — 모으는 일은 체적 · failed 에 닿지 않는다.
    /// </summary>
    private static double Intersection(List<Solid> a, List<Solid> b, out bool failed, List<Solid>? pieces = null)
    {
        failed = false;
        double ft3 = 0;
        foreach (var sa in a)
            foreach (var sb in b)
            {
                Solid? counted = null;
                try
                {
                    var x = BooleanOperationsUtils.ExecuteBooleanOperation(sa, sb, BooleanOperationsType.Intersect);
                    if (x != null && x.Volume > 0) { ft3 += x.Volume; counted = x; }
                }
                catch { failed = true; } // 일부 입체 쌍은 Revit 이 겹침을 계산하지 못한다
                if (counted != null) pieces?.Add(counted);
            }
        return ft3 * Ft3ToM3;
    }

    // 겹친 입체를 그림용 숫자 배열로 옮겨 쌍에 붙인다 — 부재의 겉면과 같은 길(ZoneScene.AddSolid: 면마다 Revit 의 세모 · ft → m)로 만든다.
    // 여기서 무엇이 틀어져도 쌍과 체적은 그대로다(덩어리만 없다 — 창이 그 까닭을 알린다).
    private static void AttachBlob(OverlapPair pair, List<Solid> pieces)
    {
        try
        {
            var builder = new MeshBuilder();
            var counters = new ZoneScene.Data();        // AddSolid 가 세는 칸(여기서는 쓰지 않는다)
            double area = 0;
            foreach (var piece in pieces)
            {
                ZoneScene.AddSolid(builder, piece, counters);
                try { area += piece.SurfaceArea * FtToM * FtToM; } catch { area = double.NaN; }
            }
            pair.BlobAreaM2 = area;
            if (builder.Triangles > 0)
            {
                pair.Blob = builder;
                return;
            }
            // 세모를 하나도 만들지 못한 덩어리(아주 얇은 조각 등): 모서리 점으로 잰 경계상자로라도 보이고 찍히게 한다.
            var box = Box3.Empty;
            foreach (var piece in pieces)
                foreach (Edge edge in piece.Edges)
                {
                    IList<XYZ> points;
                    try { points = edge.Tessellate(); } catch { continue; }
                    foreach (var p in points) box = box.With(ZoneScene.Meters(p));
                }
            if (!box.IsEmpty && box.Size.Length > 1e-6)
            {
                pair.Blob = ZoneScene.BoxBuilder(box);
                pair.BlobBoxed = true;
                pair.BlobNote = "세모를 만들지 못해 경계상자로 그림";
            }
            else pair.BlobNote = "세모도 경계상자도 만들지 못함";
        }
        catch (Exception ex)
        {
            pair.Blob = null;
            pair.BlobNote = "덩어리를 만들지 못함: " + ex.Message;
        }
    }

    internal static string Name(Document doc, Element el)
    {
        string type = (doc.GetElement(el.GetTypeId()) as ElementType)?.Name ?? el.Name;
        return $"{el.Category?.Name} {type} (Id {el.Id.Value})";
    }

    public sealed class JoinReport
    {
        public int Joined;        // 결합했고 겹침이 없어졌다
        public int StillOverlap;  // 결합은 됐지만 겹침이 남았다
        public int Failed;        // 결합하지 못했다
        public int NotNeeded;     // 앞선 결합으로 겹침이 이미 없어져 결합을 걸지 않았다
        public int Pending;       // 아직 결합을 시도하지 않았다(겹침 창에서 하나씩 고치는 중) — Join 이 돌려주는 수에서는 늘 0
        public double RemainingM3; // 결합 뒤에도 남은 겹침의 합(못 한 쌍 · 아직 안 한 쌍 포함)
        public List<string> Lines = new(); // 일지에 적을 줄(나누기 · 다른 쌍 옮기기)
    }

    // 쌍의 결합 결과로 적는 글자(OverlapPair.Status).
    public const string Joined = "결합됨";
    public const string NotNeeded = "결합 필요 없음(앞선 결합으로 겹침이 없어짐)";
    public const string StillOverlapText = "결합됐지만 겹침이 남음";
    public const string UnremeasuredText = "결합됨(다시 재지 못함)";
    public const string MultiPieceText = "결합 못 함: 나눈 부재의 여러 조각과 겹침 — 모델 검증을 다시 실행해 주세요";
    public const string SplitFailText = OverlapRow.SplitFail + ": ";

    /// <summary>
    /// 쌍들의 결합 결과를 센다 — Join 이 돌려주는 수와 겹침 창의 숫자 칸이 같은 셈을 쓴다.
    /// 결합을 시도하지 않은 쌍(Status "")은 Pending 으로 세고, 그 체적은 남은 중복 체적에 넣는다.
    /// </summary>
    public static JoinReport Tally(IEnumerable<OverlapPair> pairs)
    {
        var report = new JoinReport();
        foreach (var p in pairs)
        {
            if (p.Status == NotNeeded) report.NotNeeded++;
            else if (p.Status == Joined) report.Joined++;
            else if (p.Status == StillOverlapText || p.Status == UnremeasuredText)
            {
                report.StillOverlap++;
                report.RemainingM3 += double.IsNaN(p.AfterM3) ? p.VolumeM3 : p.AfterM3;
            }
            else if (p.Status.Length == 0) { report.Pending++; report.RemainingM3 += p.VolumeM3; }
            else { report.Failed++; report.RemainingM3 += p.VolumeM3; }
        }
        return report;
    }

    /// <summary>
    /// 그 쌍의 겹침이 지금은 없어졌는가(앞선 결합으로) — 지금의 두 입체로 다시 잰다. 잴 수 없으면 false(없어졌다고 하지 않는다).
    /// now = 다시 잰 겹침(m³ — 잴 수 없었으면 NaN).
    /// </summary>
    public static bool Gone(Document doc, OverlapPair p, out double now)
    {
        now = Remeasure(doc, p);
        return now < MinOverlapM3;     // NaN 이면 거짓
    }

    // 그 쌍을 지금의 두 입체로 다시 잰 겹침(m³). 잴 수 없으면 NaN — 부재가 없음 · 입체를 하나도 못 읽음(빈 입체를 '겹침 0'으로 치지 않는다) ·
    // 일부라도 겹침을 계산하지 못함 · 읽다 오류. 잘린 쪽을 나눴으면 조각 전부와 잰다(R35 M4).
    // 다만 잘린 쪽(조각)의 입체가 없고 남는 쪽이 그것을 자르는 결합이 걸려 있으면, 결합이 다 깎아 낸 것이다 — 겹침 0 · swallowed
    // (JACK 2026-10-19 첫 실행: 같은 자리에 겹쳐 그린 벽 둘을 고치니 «다시 재지 못함» — 미리보기도 «잘린 쪽 세모 0»).
    private static double Remeasure(Document doc, OverlapPair p) => Remeasure(doc, p, out _);

    private static double Remeasure(Document doc, OverlapPair p, out bool swallowed)
    {
        swallowed = false;
        try
        {
            var win = doc.GetElement(p.Winner);
            if (win == null) return double.NaN;
            var a = SolidReader.Solids(win);
            if (a.Count == 0) return double.NaN;
            double total = 0;
            foreach (var id in p.Pieces.Count > 0 ? p.Pieces : new List<ElementId> { p.Loser })
            {
                var lose = doc.GetElement(id);
                if (lose == null) return double.NaN;
                var b = SolidReader.Solids(lose);
                if (b.Count == 0)
                {
                    if (!JoinGeometryUtils.AreElementsJoined(doc, win, lose) || !JoinGeometryUtils.IsCuttingElementInJoin(doc, win, lose)) return double.NaN;
                    swallowed = true;
                    continue;
                }
                total += Intersection(a, b, out bool unmeasured);
                if (unmeasured) return double.NaN;
            }
            return total;
        }
        catch { return double.NaN; }
    }

    private static bool SameMembers(OverlapPair a, OverlapPair b) =>
        (a.Winner == b.Winner && a.Loser == b.Loser) || (a.Winner == b.Loser && a.Loser == b.Winner);

    /// <summary>다시 찾기(Refresh)가 바꾼 것 — 찾은 쌍의 차례(= 겹침 창의 Key).</summary>
    public sealed class RefreshReport
    {
        public List<int> Added = new();       // 새로 붙인 쌍(찾은 쌍 끝)
        public List<int> Gone = new();        // 겹침이 없어져 «이미 풀림»(결합돼 있으면 «결합됨»)이 된 쌍
        public List<int> Resized = new();     // 결합 안 된 쌍 가운데 체적이 바뀐 쌍(덩어리도 새 것)
        public List<int> Reopened = new();    // «여러 조각과 겹침» · «이미 풀림»이던 쌍이 다시 겹쳐 «아직 안 함»이 된 쌍(덩어리도 새 것)
        public List<int> Remeasured = new();  // 결합된 쌍을 지금 입체로 다시 재 결과가 바뀐 쌍
        public List<int> Flipped = new();     // 겹침이 없어진 쌍 가운데 반대 순서로 결합돼 있어 남는 쪽 · 잘리는 쪽을 실제대로 맞바꾼 쌍(Gone 에도 듦)
        public int Checked, Unmeasured;       // 다시 찾기에서 본 부재 · 잴 수 없었던 쌍
        public long Ms;

        /// <summary>줄을 다시 보내야 하는 쌍(새 쌍은 빼고).</summary>
        public IEnumerable<int> Changed => Gone.Concat(Resized).Concat(Reopened).Concat(Remeasured).Distinct();

        /// <summary>덩어리를 다시 만들어야 하는 쌍(새 쌍 포함).</summary>
        public IEnumerable<int> Reblob => Added.Concat(Resized).Concat(Reopened).Distinct();

        public string Line => string.Create(CultureInfo.InvariantCulture,
            $"다시 찾기\t본 부재 {Checked} · 새로 {Added.Count} · 없어짐 {Gone.Count} · 체적 바뀜 {Resized.Count} · 다시 열림 {Reopened.Count} · 결합된 쌍 다시 잼 {Remeasured.Count} · 반대로 결합돼 맞바꿈 {Flipped.Count} · 잴 수 없음 {Unmeasured}\t{Ms} ms");
    }

    /// <summary>
    /// 찾은 쌍을 지금 모델로 다시 맞춘다(JACK 2026-10-19 «하나를 결합하더라도 새로고침» — 결합 · 결합 순서 바꾸기 · 나누기가 다른 쌍의 겹침을 없애거나 새 겹침을 만든다).
    /// 모델은 바꾸지 않는다. 검사한 부재(targets) · 쌍들의 부재 · 이 실행에서 나눈 조각(Found.Splits)으로 Find 를 다시 돌려 쌍마다 짝을 맞춘다(열쇠 = 순서 없는 두 Id):
    ///  · 결합된 쌍(결합됨 · 겹침 남음 · 다시 재지 못함): 다시 찾기에 자기 열쇠가 나오면 결합이 풀린 것 — «아직 안 함»으로 다시 연다. 아니면 지금 입체로 다시 잰다
    ///    (결합 고리 · 순서 바꾸기 뒤 다시 생긴 겹침 — 잴 수 없으면 그대로). 남는 쪽과 조각의 열쇠는 이 쌍의 몫이라 새 쌍으로 붙이지 않는다.
    ///  · 결합 안 된 쌍: 같은 두 부재가 있으면 체적을 새 것으로(바뀌었을 때만 덩어리도 — 처음 찾기 때 자리 검증한 덩어리를 덮지 않게) · «여러 조각과 겹침» ·
    ///    «이미 풀림»이던 쌍은 «아직 안 함»으로 다시 연다 · «결합 못 함»은 까닭 그대로.
    ///    없으면(둘째 바퀴 — 다른 쌍이 제 열쇠를 다 가져간 뒤) 나눈 부재의 조각끼리 겹친 쌍 가운데 가장 큰 것으로 옮긴다(원래 부재와는 안 겹치고 조각과만 겹침 —
    ///    «이미 풀림»으로 끝내지 않게). 그것도 없으면 Gone 으로 다시 재 없어졌으면 «이미 풀림» — 결합돼 있으면 «결합됨»(자르는 쪽이 반대면 남는 쪽 · 잘리는 쪽을
    ///    실제대로 맞바꾼다) · 잴 수 없으면 그대로.
    ///  · 남는 쪽 · 잘리는 쪽은 그 쌍의 것 그대로(사용자 모드로 맞바꾼 쌍이 우선순위 쪽으로 돌아가지 않게).
    ///  · 짝이 없는 다시 찾기의 쌍 = 새 쌍(New) — 찾은 쌍 끝에 붙인다(차례 = 창의 Key 가 바뀌지 않게).
    /// 덩어리는 다시 찾기 때 지금 두 부재의 상자로 자리 검증한다(OverlapScene.DropStrayBlob — 뒤에 결합해 상자가 줄어도 믿을 수 있게).
    /// found 의 버림 · 나중 부재 결합 순서(LeanCut · LaterCut) · 두 덩이(Unsplit) · 검사한 부재 수는 바꾸지 않는다 — 사용자 모드로 일부러 뒤집은 결합을
    /// [우선순위대로 결합]이 도로 뒤집지 않게(그 목록은 처음 찾기 때의 것 + 고치기가 맞춰 둔 것). 잴 수 없었던 쌍의 수는 큰 쪽으로 둔다.
    /// </summary>
    /// <param name="withBlobs">새 쌍 · 체적이 바뀐 쌍의 덩어리도 만든다(겹침 창의 3D).</param>
    public static RefreshReport Refresh(Document doc, Found found, IEnumerable<ElementId> targets, bool withBlobs)
    {
        var watch = Stopwatch.StartNew();
        var report = new RefreshReport();
        // 같은 Id 는 한 번만 — Find 는 같은 Id 가 둘이면 그 부재를 자기와 겹친 쌍으로 낸다.
        var ids = new List<ElementId>();
        var seen = new HashSet<long>();
        void Take(ElementId id)
        {
            if (id != ElementId.InvalidElementId && doc.GetElement(id) != null && seen.Add(id.Value)) ids.Add(id);
        }
        foreach (var id in targets) Take(id);
        foreach (var p in found.Pairs)
        {
            Take(p.Winner);
            Take(p.Loser);
            foreach (var id in p.Pieces) Take(id);
        }
        foreach (var pieces in found.Splits.Values)
            foreach (var id in pieces) Take(id);
        var fresh = Find(doc, ids, withBlobs: withBlobs);
        report.Checked = fresh.Checked;
        report.Unmeasured = fresh.Unmeasured;
        found.Unmeasured = Math.Max(found.Unmeasured, fresh.Unmeasured);
        if (withBlobs)
            foreach (var f in fresh.Pairs) OverlapScene.DropStrayBlob(doc, f);
        var byKey = new Dictionary<(long, long), OverlapPair>();
        foreach (var f in fresh.Pairs) byKey.TryAdd(KeyOf(f.Winner, f.Loser), f);
        var used = new HashSet<(long, long)>();

        // 첫 바퀴: 결합된 쌍 · 제 열쇠가 나온 쌍. 제 열쇠가 없는 결합 안 된 쌍은 둘째 바퀴로(다른 쌍이 제 열쇠를 먼저 가져가게 — 조각으로 옮길 때 빼앗지 않게).
        var unmatched = new List<int>();
        for (int k = 0; k < found.Pairs.Count; k++)
        {
            var p = found.Pairs[k];
            var own = KeyOf(p.Winner, p.Loser);
            bool joinedPair = p.Status == Joined || p.Status == StillOverlapText || p.Status == UnremeasuredText;
            if (byKey.TryGetValue(own, out var match))
            {
                used.Add(own);
                if (joinedPair || p.Status == MultiPieceText || p.Status == NotNeeded)
                {
                    Reopen(p, match);
                    report.Reopened.Add(k);
                }
                else if (Math.Abs(match.VolumeM3 - p.VolumeM3) > 1e-6)
                {
                    p.VolumeM3 = match.VolumeM3;
                    TakeBlob(p, match);
                    report.Resized.Add(k);
                }
                continue;
            }
            if (!joinedPair) { unmatched.Add(k); continue; }
            foreach (var piece in p.Pieces) used.Add(KeyOf(p.Winner, piece));
            double now = Remeasure(doc, p, out bool swallowed);
            if (double.IsNaN(now)) continue;                      // 잴 수 없으면 앞의 결과 그대로
            p.Swallowed = swallowed;
            string status = now < MinOverlapM3 ? Joined : StillOverlapText;
            if (status == p.Status && Math.Abs(now - (double.IsNaN(p.AfterM3) ? 0 : p.AfterM3)) <= 1e-6) continue;
            (p.Status, p.AfterM3) = (status, now);
            report.Remeasured.Add(k);
        }

        // 둘째 바퀴: 제 열쇠가 없는 결합 안 된 쌍.
        var family = Families(found.Splits);
        foreach (int k in unmatched)
        {
            var p = found.Pairs[k];
            if (MovedToPiece(p, fresh.Pairs, used, family) is { } moved)
            {
                used.Add(KeyOf(moved.Winner, moved.Loser));
                bool winnerFirst = Related(family, p.Winner, moved.Winner);
                (p.Winner, p.WinnerName) = winnerFirst ? (moved.Winner, moved.WinnerName) : (moved.Loser, moved.LoserName);
                (p.Loser, p.LoserName) = winnerFirst ? (moved.Loser, moved.LoserName) : (moved.Winner, moved.WinnerName);
                Reopen(p, moved);
                report.Reopened.Add(k);
                continue;
            }
            if (p.Status == NotNeeded) continue;
            if (!Gone(doc, p, out double left)) continue;             // 아직 겹침 · 잴 수 없음: 그대로
            var a = doc.GetElement(p.Winner);
            var b = doc.GetElement(p.Loser);
            bool joined = a != null && b != null && JoinedSafe(doc, a, b);
            if (joined && !CutsSafe(doc, a!, b!))
            {
                // 우리 결합 없이 반대 순서로 결합돼 있다(Revit 이 저절로 · 앞서 손으로) — 그 자리를 가진 쪽이 남는 쪽이다.
                (p.Winner, p.Loser, p.WinnerName, p.LoserName) = (p.Loser, p.Winner, p.LoserName, p.WinnerName);
                report.Flipped.Add(k);
            }
            (p.Status, p.AfterM3) = (joined ? Joined : NotNeeded, left);
            report.Gone.Add(k);
        }

        foreach (var f in fresh.Pairs)
        {
            if (!used.Add(KeyOf(f.Winner, f.Loser))) continue;
            f.New = true;
            report.Added.Add(found.Pairs.Count);
            found.Pairs.Add(f);
        }
        report.Ms = watch.ElapsedMilliseconds;
        return report;
    }

    // 결합 안 된 쌍으로 다시 연다: 다시 찾기의 체적 · 덩어리 · 아직 안 함.
    private static void Reopen(OverlapPair p, OverlapPair from)
    {
        p.VolumeM3 = from.VolumeM3;
        TakeBlob(p, from);
        (p.Status, p.AfterM3) = ("", double.NaN);
    }

    private static void TakeBlob(OverlapPair p, OverlapPair from) =>
        (p.Blob, p.BlobAreaM2, p.BlobBoxed, p.BlobNote) = (from.Blob, from.BlobAreaM2, from.BlobBoxed, from.BlobNote);

    // 나눈 부재의 집안: 원래 부재와 그 조각(조각을 또 나눈 것까지)을 한 집안으로 — 부재 Id → 같은 집안의 Id 전부.
    private static Dictionary<long, HashSet<long>> Families(Dictionary<long, List<ElementId>> splits)
    {
        var family = new Dictionary<long, HashSet<long>>();
        foreach (var (root, pieces) in splits)
        {
            var merged = new HashSet<long> { root };
            foreach (var id in pieces) merged.Add(id.Value);
            foreach (long id in merged.ToList())
                if (family.TryGetValue(id, out var other)) merged.UnionWith(other);
            foreach (long id in merged) family[id] = merged;
        }
        return family;
    }

    // a 와 b 가 같은 부재이거나 같은 집안(나눈 조각)인가.
    private static bool Related(Dictionary<long, HashSet<long>> family, ElementId a, ElementId b) =>
        a == b || (family.TryGetValue(a.Value, out var members) && members.Contains(b.Value));

    // 제 열쇠가 없는 쌍: 두 부재(또는 그 조각)끼리 겹친 다시 찾기의 쌍 가운데 아직 아무 쌍도 가져가지 않은 가장 큰 것. 나눈 부재가 없는 쌍이면 null.
    private static OverlapPair? MovedToPiece(OverlapPair p, List<OverlapPair> fresh, HashSet<(long, long)> used, Dictionary<long, HashSet<long>> family)
    {
        if (!family.ContainsKey(p.Winner.Value) && !family.ContainsKey(p.Loser.Value)) return null;
        OverlapPair? best = null;
        foreach (var f in fresh)
        {
            if (used.Contains(KeyOf(f.Winner, f.Loser))) continue;
            bool fits = (Related(family, p.Winner, f.Winner) && Related(family, p.Loser, f.Loser)) ||
                        (Related(family, p.Winner, f.Loser) && Related(family, p.Loser, f.Winner));
            if (fits && (best == null || f.VolumeM3 > best.VolumeM3)) best = f;
        }
        return best;
    }

    private static bool CutsSafe(Document doc, Element cutting, Element cut)
    {
        try { return JoinGeometryUtils.IsCuttingElementInJoin(doc, cutting, cut); }
        catch { return true; }      // 모르면 정한 대로라고 본다(앞의 길과 같다)
    }

    private static (long, long) KeyOf(ElementId a, ElementId b) => a.Value < b.Value ? (a.Value, b.Value) : (b.Value, a.Value);

    // 두 부재의 경계상자가 맞닿거나 겹치는가(1 mm 여유) — 결합으로 잘린 뒤라 입체는 겹치지 않으므로 상자로 본다.
    private static bool BoxesMeet(Element a, Element b)
    {
        BoundingBoxXYZ? ba, bb;
        try { ba = a.get_BoundingBox(null); bb = b.get_BoundingBox(null); }
        catch { return false; }
        if (ba == null || bb == null) return false;
        const double slack = 0.001 / FtToM;
        return ba.Min.X <= bb.Max.X + slack && bb.Min.X <= ba.Max.X + slack &&
               ba.Min.Y <= bb.Max.Y + slack && bb.Min.Y <= ba.Max.Y + slack &&
               ba.Min.Z <= bb.Max.Z + slack && bb.Min.Z <= ba.Max.Z + slack;
    }

    private static bool JoinedSafe(Document doc, Element a, Element b)
    {
        try { return JoinGeometryUtils.AreElementsJoined(doc, a, b); }
        catch { return false; }
    }

    // 나눈 뒤(R35 M4): 아직 끝나지 않은 다른 쌍 가운데 원래 부재(조각 0)가 든 쌍을, 그 쌍의 다른 부재와 겹치는 조각으로 옮긴다 —
    // 안 옮기면 위 조각과 겹치던 쌍이 원래 Id 로는 겹침 0 이 되어 '필요 없음'(초록)인데 실제로는 겹친 채 남는다.
    // 조각 하나와 겹치면 그 조각으로 · 어느 조각과도 안 겹치면 그대로(뒤에서 '필요 없음') · 둘 이상과 겹치면 «여러 조각과 겹침»으로 두고 결합하지 않는다.
    // 조각으로 옮긴 쌍의 앞선 결과(못 함의 까닭 · 남은 겹침 · 나눈 조각)는 원래 부재에 대한 것이라 지우고 아직 안 한 쌍으로 둔다(R36 revit L2).
    // 고치는 그 쌍 자신(self — 같은 두 부재) · 이미 «여러 조각과 겹침»인 쌍(다시 검사해야 한다)은 건드리지 않는다. undo = 묶음을 합치지 못했을 때 되돌릴 일.
    // pieces = 나눈 조각(맨 앞 = 원래 부재). self 가 없으면(SplitJoined — 이미 결합돼 있던 부재) 원래 부재가 든 쌍 모두가 대상이다.
    private static void Remap(Document doc, IReadOnlyList<ElementId> pieces, OverlapPair? self, IReadOnlyList<OverlapPair> all, List<string> lines, List<Action> undo)
    {
        var orig = pieces[0];
        foreach (var q in all)
        {
            if (self != null && (ReferenceEquals(q, self) || SameMembers(q, self))) continue;
            if (q.Status == MultiPieceText) continue;
            bool asWinner = q.Winner == orig, asLoser = q.Loser == orig;
            if (!asWinner && !asLoser) continue;
            var other = doc.GetElement(asWinner ? q.Loser : q.Winner);
            if (other == null) continue;
            if (q.Status == Joined || q.Status == NotNeeded)
            {
                // 끝난 쌍(R38 core M-1): 겹침이 0 이라 «겹치는 조각»으로는 못 고른다 — 그 상대와 맞닿은(결합된 쌍이면 결합도 된) 조각이 하나뿐이면
                // 기록만 그 조각으로 옮긴다(결과는 그대로). 안 옮기면 결합 뒤 창이 아래 조각을 칠하고, 결과표의 번호도 아래 조각이 된다.
                // 그 쌍 스스로 나눈 기록이 있으면 건드리지 않는다. 둘 이상이면(그 상대가 가른 자리 — 위아래 조각 모두에 닿음) 원래 부재 그대로.
                if (q.Pieces.Count > 0) continue;
                var meets = pieces.Where(id => doc.GetElement(id) is Element piece && BoxesMeet(piece, other) &&
                                               (q.Status != Joined || JoinedSafe(doc, piece, other))).ToList();
                if (meets.Count == 1 && meets[0] != orig && doc.GetElement(meets[0]) is Element dest)
                {
                    var (w0, l0, wn0, ln0) = (q.Winner, q.Loser, q.WinnerName, q.LoserName);
                    if (asWinner) { q.Winner = dest.Id; q.WinnerName = Name(doc, dest); }
                    else { q.Loser = dest.Id; q.LoserName = Name(doc, dest); }
                    undo.Add(() => { (q.Winner, q.Loser, q.WinnerName, q.LoserName) = (w0, l0, wn0, ln0); });
                    lines.Add($"  옮기기\t쌍 {w0.Value}→{l0.Value}\t{orig.Value} → 조각 {dest.Id.Value}(끝난 쌍 — 기록만)");
                }
                continue;
            }
            var b = SolidReader.Solids(other);
            var hits = new List<ElementId>();
            foreach (var id in pieces)
            {
                if (doc.GetElement(id) is not Element piece) continue;
                double m3 = Intersection(SolidReader.Solids(piece), b, out bool unmeasured);
                if (unmeasured || m3 >= MinOverlapM3) hits.Add(id);
            }
            string head = $"  옮기기\t쌍 {q.Winner.Value}→{q.Loser.Value}\t";
            if (hits.Count == 1 && hits[0] != orig && doc.GetElement(hits[0]) is Element to)
            {
                var (winner, loser, winnerName, loserName) = (q.Winner, q.Loser, q.WinnerName, q.LoserName);
                var (status, after, note, oldPieces) = (q.Status, q.AfterM3, q.Split, q.Pieces);
                if (asWinner) { q.Winner = to.Id; q.WinnerName = Name(doc, to); }
                else { q.Loser = to.Id; q.LoserName = Name(doc, to); }
                (q.Status, q.AfterM3, q.Split, q.Pieces) = ("", double.NaN, "", new List<ElementId>());
                undo.Add(() =>
                {
                    (q.Winner, q.Loser, q.WinnerName, q.LoserName) = (winner, loser, winnerName, loserName);
                    (q.Status, q.AfterM3, q.Split, q.Pieces) = (status, after, note, oldPieces);
                });
                lines.Add(head + $"{orig.Value} → 조각 {to.Id.Value}" + (status.Length > 0 ? $"\t앞의 결과 지움({status})" : ""));
            }
            else if (hits.Count >= 2)
            {
                string status = q.Status;
                q.Status = MultiPieceText;
                undo.Add(() => q.Status = status);
                lines.Add(head + $"조각 {hits.Count}개와 겹침 — 결합하지 않음");
            }
            else lines.Add(head + (hits.Count == 0 ? "어느 조각과도 안 겹침(그대로)" : "아래 조각(원래 부재) 그대로"));
        }
    }

    /// <summary>
    /// 겹친 쌍마다 형상 결합을 걸고, 쌍의 Winner 가 Loser 를 자르게 한다(찾은 쌍은 우선순위대로 — 겹침 창의 사용자 모드는 둘을 맞바꾼 쌍을 넘긴다).
    /// 쌍 하나가 실패해도 나머지는 계속한다(쌍마다 따로 되돌린다). 전체가 되돌리기 한 번(Ctrl+Z)으로 묶인다(바깥에 열린 묶음이 있으면 그 안으로).
    /// 그룹 안의 부재·다른 사용자가 편집 중인 부재는 건드리지 않는다.
    /// 앞선 결합으로 겹침이 이미 없어진 쌍은 걸지 않고, 자르는 순서를 정한 대로 만들지 못한 쌍은 결합까지 되돌린다.
    /// 결합한 두 부재의 판정(DH_판정)은 '다시 계산 필요'로 바꿔 둔다 — 체적이 바뀌어 전의 '일치'를 믿을 수 없다.
    /// 결합으로 잘린 쪽(벽 · 기둥)이 위아래 두 덩이 이상이 되면 덩이마다 한 객체로 나눈다(OverlapSplitter — JACK 2026-10-14 "고칠 때 저절로").
    /// 나눴으면 all(찾은 쌍 전부)에서 원래 부재가 든 다른 쌍을 겹치는 조각으로 옮긴다(Remap) · splits(Found.Splits)에 조각을 적는다.
    /// </summary>
    public static JoinReport Join(Document doc, List<OverlapPair> pairs, IReadOnlyList<OverlapPair>? all = null, Dictionary<long, List<ElementId>>? splits = null)
    {
        var lines = new List<string>();
        var undo = new List<Action>();           // 묶음을 합치지 못하면 되돌릴 것(나누기 · 옮기기의 기록)
        using (var group = new TransactionGroup(doc, "DH 겹침 결합"))
        {
            group.Start();
            bool anyCommitted = false;
            foreach (var p in pairs)
            {
                if (p.Status == MultiPieceText) continue;   // 나눈 부재의 여러 조각과 겹친 쌍 — 다시 검사해야 한다(결합하지 않는다)
                var win = doc.GetElement(p.Winner);
                var lose = doc.GetElement(p.Loser);
                if (win == null || lose == null) { p.Status = "결합 못 함: 부재를 찾을 수 없음"; continue; }
                string? blocked = FormulaAutoFill.WhyNotEditable(doc, win) ?? FormulaAutoFill.WhyNotEditable(doc, lose);
                if (blocked != null) { p.Status = "결합 못 함: " + blocked; continue; }

                // 세 부재가 한 자리에서 겹치면 앞선 결합으로 이 쌍의 겹침이 이미 없어졌을 수 있다 — 그러면 결합을 걸지 않는다.
                // (다시 잴 수 없으면 찾았을 때의 값대로 결합을 건다.)
                if (Gone(doc, p, out double now)) { p.Status = NotNeeded; p.AfterM3 = now; continue; }

                // 잘리는 쪽(벽 · 기둥)의 덩이 수를 결합하기 전에 센다 — 결합으로 늘었을 때만 나눈다(R36 core M-3 · revit L5).
                int lumpsBefore = OverlapSplitter.LumpCount(lose);
                // 모양 기록(R52b): 결합 전에 위치·부위 확인이 지금 모양과 맞았으면, 결합 뒤 모양으로 고쳐 적는다 — 우리 결합은 «설계 변경»이 아니다.
                bool winSame = !PlaceStamp.ValueOf(win).Changed, loseSame = !PlaceStamp.ValueOf(lose).Changed;

                using var tx = new Transaction(doc, "DH 겹침 결합");
                var quiet = new QuietFailures();
                string stage = "";              // 어디서 Revit 이 거절했는지(거절 글은 영어라 한국어로 바꿔 적는다)
                try
                {
                    tx.Start();
                    var options = tx.GetFailureHandlingOptions();
                    options.SetFailuresPreprocessor(quiet);
                    options.SetClearAfterRollback(true);
                    tx.SetFailureHandlingOptions(options);
                    stage = "Revit 이 이 두 부재를 결합하지 못하게 함";
                    if (!JoinGeometryUtils.AreElementsJoined(doc, win, lose)) JoinGeometryUtils.JoinGeometry(doc, win, lose);
                    // Winner 가 자르게 한다(Revit 이 정한 기본 순서가 반대면 바꾼다).
                    stage = "Revit 이 이 두 부재의 자르는 순서를 바꾸지 못하게 함";
                    if (!JoinGeometryUtils.IsCuttingElementInJoin(doc, win, lose)) JoinGeometryUtils.SwitchJoinOrder(doc, win, lose);
                    stage = "";
                    // 바꾼 뒤에 정말 그 순서가 됐는지 본다. 아니면 이 쌍은 결합까지 되돌린다(반대로 잘린 채 남기지 않는다).
                    doc.Regenerate();
                    if (!JoinGeometryUtils.IsCuttingElementInJoin(doc, win, lose))
                    {
                        tx.RollBack();
                        p.Status = "결합 못 함: 자르는 순서를 정한 대로 바꾸지 못함";
                        continue;
                    }
                    // 결합하면 체적이 바뀐다 — 전에 적힌 '일치'가 그대로 남지 않게 판정을 바꿔 둔다(판정이 적힌 부재만).
                    FormulaAutoFill.MarkVerdictStale(win);
                    FormulaAutoFill.MarkVerdictStale(lose);
                    if (winSame) PlaceStamp.Reshape(win);
                    if (loseSame) PlaceStamp.Reshape(lose);
                    var status = tx.Commit();
                    anyCommitted |= status == TransactionStatus.Committed;
                    p.Status = status == TransactionStatus.Committed ? Joined
                             : "결합 못 함: Revit 이 되돌림" + (quiet.FirstError.Length > 0 ? $"({quiet.FirstError})" : "");
                }
                catch (Exception ex)
                {
                    try { if (tx.HasStarted() && !tx.HasEnded()) tx.RollBack(); } catch { }
                    // Revit 의 거절(결합: ArgumentException «cannot be joined» · 순서: InvalidOperationException «Unable to switch …»)은
                    // 한국어로 적고 원문은 괄호에 둔다.
                    p.Status = "결합 못 함: " + (stage.Length > 0 && ex is Autodesk.Revit.Exceptions.ApplicationException ? $"{stage} ({ex.Message})" : ex.Message);
                }

                // 결합으로 잘린 쪽이 위아래 덩이로 갈렸으면 나눈다(따로 트랜잭션 — 못 나누면 그것만 되돌리고 결합은 남는다).
                if (p.Status != Joined) continue;
                try
                {
                    var split = OverlapSplitter.TrySplit(doc, lose, lumpsBefore);
                    var (pieces, note) = (p.Pieces, p.Split);
                    if (split.Split)
                    {
                        // 이미 나눈 쌍을 다시 고치다 또 나눴으면 앞의 조각도 둔다(맨 앞 = 이번에 나눈 원래 부재 — Remap 이 그것을 본다).
                        p.Pieces = split.Pieces.Concat(pieces.Where(id => !split.Pieces.Contains(id))).ToList();
                        p.Split = $"조각 {p.Pieces.Count}개";
                        undo.Add(() => { p.Pieces = pieces; p.Split = note; });
                        if (splits != null)
                        {
                            long orig = lose.Id.Value;
                            bool had = splits.TryGetValue(orig, out var old);
                            splits[orig] = split.Pieces.ToList();
                            undo.Add(() => { if (had) splits[orig] = old!; else splits.Remove(orig); });
                        }
                        lines.AddRange(split.Lines);
                        Remap(doc, p.Pieces, p, all ?? pairs, lines, undo);
                    }
                    else if (!split.NotTarget)
                    {
                        p.Split = SplitFailText + split.Why;
                        undo.Add(() => p.Split = note);     // 묶음이 되돌려지면 이 결합도 없다(R36 revit L1)
                        lines.Add($"나누기 못 함\tId {lose.Id.Value}\t{split.Why}");
                        lines.AddRange(split.Lines);
                    }
                    else lines.AddRange(split.Lines);    // 대상 아님 — 까닭이 있으면 일지에만(결합 전에도 여러 덩이 등)
                }
                catch (Exception ex) { lines.Add($"나누기 오류\tId {lose.Id.Value}\t{ex}"); }
            }
            // 하나라도 결합했으면 되돌리기 한 번으로 묶고, 아무것도 못 했으면 흔적 없이 닫는다.
            // 묶지 못하면(드묾) 이 묶음이 되돌려진다 — 여기서 결합한 쌍은 '결합 못 함'으로 바꿔 모델과 맞춘다(결합됨으로 남지 않게) ·
            // 나눈 것과 다른 쌍 옮긴 것도 되돌린다(조각이 모델에서 사라진다).
            if (anyCommitted)
            {
                string why = "";
                try
                {
                    var closed = group.Assimilate();
                    if (closed != TransactionStatus.Committed) why = closed.ToString();
                }
                catch (Exception ex) { why = ex.Message; }
                if (why.Length > 0)
                {
                    for (int i = undo.Count - 1; i >= 0; i--) undo[i]();
                    foreach (var p in pairs)
                        if (p.Status == Joined) p.Status = "결합 못 함: 결합을 하나로 묶지 못해 Revit 이 되돌림(" + why + ")";
                    lines.Add("묶음을 합치지 못함 — 결합 · 나누기를 모두 되돌림\t" + why);
                }
            }
            else group.RollBack();
        }

        // 결합한 쌍을 다시 잰다 — 정말 겹침이 없어졌는가(화면을 정하는 값은 결합 '뒤'의 입체다).
        // 잴 수 없으면(일부라도 · 입체를 못 읽음) '겹침이 없어졌다'고 하지 않는다.
        foreach (var p in pairs)
        {
            if (p.Status != Joined) continue;
            p.AfterM3 = Remeasure(doc, p, out bool swallowed);
            p.Swallowed = swallowed;
            if (swallowed) lines.Add($"다 들어감\t잘린 쪽 {p.Loser.Value} 이 남는 쪽 {p.Winner.Value} 안에 다 들어가 입체가 남지 않음 — 같은 자리에 겹쳐 그린 부재일 수 있음");
            if (!(p.AfterM3 < MinOverlapM3)) p.Status = double.IsNaN(p.AfterM3) ? UnremeasuredText : StillOverlapText;
        }
        var report = Tally(pairs);
        report.Lines = lines;
        return report;
    }

    /// <summary>
    /// 이미 결합됐는데 버림이 잘리는 쪽인 쌍(Found.LeanCut)의 자르는 순서를 바꿔 버림이 남게 한다(JACK 2026-10-14 «결합할 때 함께 바꾸기» —
    /// «버림은 늘 남기기»가 전에 건 결합에도 닿게). 쌍마다 트랜잭션 · 바꾼 뒤 정말 버림이 자르는지 본다(아니면 그 쌍만 되돌림) ·
    /// 두 부재의 판정은 '다시 계산 필요'로. 제 묶음 안에서 바꾸고 하나로 합친다(Join 과 같은 꼴 — 바깥 묶음이 있으면 그 안으로, 없어도 되돌리기 한 번 · R36 revit N2).
    /// 돌려주는 것 = 바꾸지 못한 쌍 · 바꾼 수 · 일지 줄 · 바꾼 뒤 덩이가 는 상대(벽 · 기둥 — 부르는 쪽이 Found.Unsplit 에 넣어 SplitJoined 가 함께 나눈다 · R36 revit N3).
    /// </summary>
    /// <param name="title">묶음 · 트랜잭션 · 일지의 이름 — 나중에 치는 부재(Found.LaterCut — 쌍의 앞 = 남을 구조체, 뒤 = 잘릴 나중 부재)도 같은 길로 바꾼다.</param>
    /// <param name="keepName">일지에서 남는 쪽을 부르는 말.</param>
    public static (List<(ElementId Lean, ElementId Other)> left, int switched, List<string> lines, List<(ElementId Id, int Before, int Now)> grown)
        FixLeanOrder(Document doc, IReadOnlyList<(ElementId Lean, ElementId Other)> pairs, string title = "버림 순서", string keepName = "버림",
                     List<ElementId>? floorsGrown = null)
    {
        var left = new List<(ElementId, ElementId)>();
        var lines = new List<string>();
        var grown = new List<(ElementId, int, int)>();
        var grownFloors = new List<ElementId>();             // 덩이가 는 바닥(묶음을 합친 뒤에만 부르는 쪽에 넘긴다)
        var done = new List<(ElementId, ElementId)>();      // 바꾼 쌍(묶음을 합치지 못하면 left 로 되돌린다)
        if (pairs.Count == 0) return (left, 0, lines, grown);
        using var group = new TransactionGroup(doc, "DH 겹침 결합 — " + title);
        group.Start();
        foreach (var pair in pairs)
        {
            var lean = doc.GetElement(pair.Lean);
            var other = doc.GetElement(pair.Other);
            string head = $"{title}\t{keepName} {pair.Lean.Value} ← {pair.Other.Value}\t";
            if (lean == null || other == null) { lines.Add(head + "부재를 찾을 수 없음"); left.Add(pair); continue; }
            string? blocked = FormulaAutoFill.WhyNotEditable(doc, lean) ?? FormulaAutoFill.WhyNotEditable(doc, other);
            if (blocked != null) { lines.Add(head + "못 바꿈: " + blocked); left.Add(pair); continue; }
            int lumpsBefore = OverlapSplitter.LumpCount(other);
            int floorBefore = OverlapSplitter.FloorLumpCount(other);
            // 모양 기록(R52b): 순서를 바꾸면 두 부재의 모양이 다 바뀐다 — 바꾸기 전에 확인이 지금 모양과 맞았으면 바꾼 뒤 모양으로 고쳐 적는다.
            bool leanSame = !PlaceStamp.ValueOf(lean).Changed, otherSame = !PlaceStamp.ValueOf(other).Changed;
            using var tx = new Transaction(doc, "DH 겹침 결합 — " + title);
            var quiet = new QuietFailures();
            try
            {
                tx.Start();
                var options = tx.GetFailureHandlingOptions();
                options.SetFailuresPreprocessor(quiet);
                options.SetClearAfterRollback(true);
                tx.SetFailureHandlingOptions(options);
                if (!JoinGeometryUtils.AreElementsJoined(doc, lean, other)) { tx.RollBack(); lines.Add(head + "결합이 풀려 있음(바꿀 것 없음)"); continue; }
                if (!JoinGeometryUtils.IsCuttingElementInJoin(doc, lean, other)) JoinGeometryUtils.SwitchJoinOrder(doc, lean, other);
                doc.Regenerate();
                if (!JoinGeometryUtils.IsCuttingElementInJoin(doc, lean, other))
                {
                    tx.RollBack();
                    lines.Add(head + "못 바꿈: 자르는 순서를 바꾸지 못함");
                    left.Add(pair);
                    continue;
                }
                FormulaAutoFill.MarkVerdictStale(lean);
                FormulaAutoFill.MarkVerdictStale(other);
                if (leanSame) PlaceStamp.Reshape(lean);
                if (otherSame) PlaceStamp.Reshape(other);
                var status = tx.Commit();
                if (status == TransactionStatus.Committed)
                {
                    done.Add(pair);
                    lines.Add(head + "바꿈");
                    int lumpsAfter = OverlapSplitter.LumpCount(other);
                    if (lumpsBefore > 0 && lumpsAfter > lumpsBefore)
                    {
                        grown.Add((other.Id, lumpsBefore, lumpsAfter));
                        lines.Add($"  덩이가 늘었음\tId {other.Id.Value}\t{lumpsBefore} → {lumpsAfter}개 — 두 덩이 나누기에 넘김");
                    }
                    // 바닥(중간슬래브 등 — R46 의 나중 부재)은 나누지 않는다 — 덩이가 늘었으면 일지에만(산출식이 «직접 넣어야 함»이 될 수 있다).
                    int floorAfter = floorBefore > 0 ? OverlapSplitter.FloorLumpCount(other) : -1;
                    if (floorBefore > 0 && floorAfter > floorBefore)
                    {
                        lines.Add($"  바닥 덩이가 늘었음\tId {other.Id.Value}\t{floorBefore} → {floorAfter}개 — 나누지 않음(산출식이 «직접 넣어야 함»이 될 수 있음)");
                        grownFloors.Add(other.Id);
                    }
                }
                else { lines.Add(head + "Revit 이 되돌림" + (quiet.FirstError.Length > 0 ? $"({quiet.FirstError})" : "")); left.Add(pair); }
            }
            catch (Exception ex)
            {
                try { if (tx.HasStarted() && !tx.HasEnded()) tx.RollBack(); } catch { }
                lines.Add(head + "못 바꿈: " + (ex is Autodesk.Revit.Exceptions.ApplicationException ? "Revit 이 이 두 부재의 자르는 순서를 바꾸지 못하게 함 (" + ex.Message + ")" : ex.Message));
                left.Add(pair);
            }
        }
        // 하나라도 바꿨으면 되돌리기 한 번으로 묶고, 아무것도 못 했으면 흔적 없이 닫는다. 묶지 못하면 Revit 이 이 묶음을 되돌린다 — 바꾼 쌍은 못 바꾼 쌍으로.
        if (done.Count > 0)
        {
            string why = "";
            try
            {
                var closed = group.Assimilate();
                if (closed != TransactionStatus.Committed) why = closed.ToString();
            }
            catch (Exception ex) { why = ex.Message; }
            if (why.Length > 0)
            {
                left.AddRange(done);
                done.Clear();
                grown.Clear();
                grownFloors.Clear();   // 되돌려졌으면 바닥도 그대로다(revit-verify R46 L2)
                lines.Add($"묶음을 합치지 못함 — 바꾼 {title}를 모두 되돌림\t" + why);
            }
        }
        else group.RollBack();
        floorsGrown?.AddRange(grownFloors);
        return (left, done.Count, lines, grown);
    }

    /// <summary>
    /// 이미 결합돼 결합 전보다 덩이가 많은 벽 · 기둥(Found.Unsplit)을 덩이마다 나눈다(JACK 2026-10-15 «이미 결합된 두 덩이도 함께 나누기» —
    /// [우선순위대로 결합] 때 · 겹친 쌍이 없으면 작은 창의 단추). 부재마다 OverlapSplitter.TrySplit(before = 결합 전 덩이 수) —
    /// 못 나누는 까닭(문 · 창 · 얇은 조각 …)은 Join 의 나누기와 같다. 나눴으면 찾은 쌍 가운데 그 부재가 든 쌍을 겹치는 조각으로 옮긴다(Remap).
    /// 같은 실행의 Join 에서 나누기를 해 본 부재(쌍의 Loser 이고 Split 이 적힘)는 건너뛴다(같은 까닭이 두 번 세이지 않게).
    /// 제 묶음 안에서 하고 하나로 합친다(Join 과 같은 꼴 — 바깥 묶음이 있으면 그 안으로). 묶지 못하면 나눈 것 · 옮긴 쌍을 되돌리고 못 나눈 것으로 적는다.
    /// 끝나면 Unsplit 을 비우고 결과를 SplitDone · SplitFailed 에 더한다. 돌려주는 것 = 일지 줄.
    /// </summary>
    public static List<string> SplitJoined(Document doc, Found found)
    {
        var lines = new List<string>();
        var todo = found.Unsplit.ToList();
        found.Unsplit.Clear();
        if (todo.Count == 0) return lines;
        // 어느 쌍에서든 이미 나누기를 해 본 부재(Loser · 그 조각) — 첫 쌍만 보면 놓친다(R38 revit P5).
        var tried = new HashSet<long>(found.Pairs.Where(p => p.Split.Length > 0).SelectMany(p => p.Pieces.Append(p.Loser)).Select(id => id.Value));
        var undo = new List<Action>();
        var split = new List<ElementId>();
        var failed = new List<(ElementId, string)>();
        using var group = new TransactionGroup(doc, "DH 겹침 결합 — 두 덩이 나누기");
        group.Start();
        foreach (var (id, before, now) in todo)
        {
            string head = $"두 덩이\tId {id.Value}\t덩이 {now}(결합 전 {before})\t";
            if (tried.Contains(id.Value)) { lines.Add(head + "이번 결합에서 나누기를 해 봄 — 건너뜀"); continue; }
            if (doc.GetElement(id) is not Element el) { lines.Add(head + "부재를 찾을 수 없음"); continue; }
            try
            {
                var result = OverlapSplitter.TrySplit(doc, el, before);
                if (result.Split)
                {
                    split.Add(id);
                    found.Splits[id.Value] = result.Pieces.ToList();
                    undo.Add(() => found.Splits.Remove(id.Value));
                    lines.AddRange(result.Lines);
                    // 쌍 옮기기는 따로 감싼다 — 오류가 나도 이 부재는 나뉜 채다(«나눔»으로만 센다 · 옮긴 만큼의 undo 는 남는다 · R38 core L-a).
                    try { Remap(doc, result.Pieces, null, found.Pairs, lines, undo); }
                    catch (Exception ex) { lines.Add(head + "쌍 옮기기 오류\t" + ex.Message); }
                }
                else if (!result.NotTarget)
                {
                    failed.Add((id, result.Why));
                    lines.Add(head + "나누기 못 함: " + result.Why);
                    lines.AddRange(result.Lines);
                }
                else lines.AddRange(result.Lines.Count > 0 ? result.Lines : new List<string> { head + "대상 아님(지금은 한 덩이 · 벽 · 기둥이 아님 등)" });
            }
            catch (Exception ex)
            {
                failed.Add((id, "나누다 오류: " + ex.Message));
                lines.Add(head + "나누기 오류\t" + ex);
            }
        }
        if (split.Count > 0)
        {
            string why = "";
            try
            {
                var closed = group.Assimilate();
                if (closed != TransactionStatus.Committed) why = closed.ToString();
            }
            catch (Exception ex) { why = ex.Message; }
            if (why.Length > 0)
            {
                for (int i = undo.Count - 1; i >= 0; i--) undo[i]();
                failed.AddRange(split.Select(id => (id, "나눈 것을 하나로 묶지 못해 Revit 이 되돌림(" + why + ")")));
                split.Clear();
                lines.Add("묶음을 합치지 못함 — 두 덩이 나누기를 모두 되돌림\t" + why);
            }
        }
        else group.RollBack();
        found.SplitDone += split.Count;
        found.SplitFailed.AddRange(failed);
        return lines;
    }

    // 결합하다 Revit 이 띄우려는 경고는 지우고, 오류가 나면 그 쌍만 되돌린다(창을 띄워 전체를 멈추지 않는다).
    internal sealed class QuietFailures : IFailuresPreprocessor
    {
        public string FirstError = "";

        public FailureProcessingResult PreprocessFailures(FailuresAccessor accessor)
        {
            bool error = false;
            foreach (var f in accessor.GetFailureMessages())
            {
                if (f.GetSeverity() == FailureSeverity.Warning) accessor.DeleteWarning(f);
                else
                {
                    error = true;
                    if (FirstError.Length == 0) { try { FirstError = f.GetDescriptionText(); } catch { } }
                }
            }
            return error ? FailureProcessingResult.ProceedWithRollBack : FailureProcessingResult.Continue;
        }
    }

    /// <summary>겹침 목록을 결과표(TSV)로 남긴다.</summary>
    public static string WriteLog(Document doc, Found found)
    {
        string dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DH.Takeoff", "log");
        Directory.CreateDirectory(dir);
        string name = doc.Title;
        foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        string path = Path.Combine(dir, name + "_overlaps.tsv");

        // 남는쪽 = 우선순위상 통째로 세는 부재, 잘리는쪽 = 겹친 만큼 빠져야 하는 부재.
        var sb = new StringBuilder("남는쪽Id\t남는쪽\t잘리는쪽Id\t잘리는쪽\t겹친체적(m3)\t결합결과\t결합뒤겹침(m3)\t나누기\t새로\n");
        foreach (var p in found.Pairs)
            sb.Append(CultureInfo.InvariantCulture,
                $"{p.Winner.Value}\t{Cell(p.WinnerName)}\t{p.Loser.Value}\t{Cell(p.LoserName)}\t{p.VolumeM3:0.######}\t{Cell(p.Status + (p.Status == Joined && p.Swallowed ? " · 잘린 쪽이 다 들어감(겹쳐 그린 부재?)" : ""))}\t" +
                $"{(double.IsNaN(p.AfterM3) ? "" : p.AfterM3.ToString("0.######", CultureInfo.InvariantCulture))}\t" +
                $"{Cell(p.Split)}{(p.Pieces.Count > 1 ? " [" + string.Join(",", p.Pieces.Select(id => id.Value)) + "]" : "")}\t{(p.New ? "고친 뒤 새로 찾음" : "")}\n");
        // 결합 방향(R54): 남을 쪽 = 기준대로면 남는 부재(지금 잘림) · 잘릴 쪽 = 지금 자르는 부재.
        if (found.Reversed.Count > 0)
        {
            sb.Append("\n작성오류(기준과 반대로 결합됨)\n남을쪽Id\t남을쪽\t남을쪽 순위\t잘릴쪽Id\t잘릴쪽(지금 자름)\t잘릴쪽 순위\t바뀔체적(m3)\t결과\t표시\n");
            foreach (var r in found.Reversed)
                sb.Append(CultureInfo.InvariantCulture,
                    $"{r.Keep.Value}\t{Cell(r.KeepName)}\t{DeductRank.Label(r.KeepRank)}\t{r.Cutter.Value}\t{Cell(r.CutterName)}\t{DeductRank.Label(r.CutterRank)}\t" +
                    $"{(double.IsNaN(r.MovedM3) ? "" : r.MovedM3.ToString("0.######", CultureInfo.InvariantCulture))}\t{Cell(r.Status)}\t" +
                    $"{Cell(string.Join(" · ", new[] { r.Human ? "사람이 정함" : "", r.SameRank ? "참고(같은 순위)" : "", r.Note }.Where(t => t.Length > 0)))}\n");
        }
        sb.Append(CultureInfo.InvariantCulture,
            $"\n검사한 부재\t{found.Checked}\n겹침오류(겹친 쌍)\t{found.Pairs.Count}\n겹친 체적 합(m3)\t{found.TotalM3:0.######}\n" +
            $"작성오류(기준과 반대로 결합됨)\t{found.Reversed.Count(r => r.Counts)}\n바꿔도 같아 뺀 반대 결합\t{found.Touching}\n" +
            $"잴 수 없었던 쌍\t{found.Unmeasured}\n함께 있지 않아 뺀 쌍(디자인 옵션·공정)\t{found.NotCoexisting}\n끝까지 봤는가\t{(found.TimedOut ? "아니오(시간 한도)" : "예")}\n우선순위\t{PriorityText}\n");
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
        try
        {
            string hist = Path.Combine(dir, "history");
            Directory.CreateDirectory(hist);
            File.Copy(path, Path.Combine(hist, $"{name}_overlaps_{DateTime.Now:yyyyMMdd_HHmmss}.tsv"), true);
        }
        catch { }
        return path;
    }

    private static string Cell(string s) => s.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
}
