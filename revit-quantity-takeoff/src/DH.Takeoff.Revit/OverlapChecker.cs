using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using Autodesk.Revit.DB;
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

    // ── 그림용(겹침 검사 결과 창의 3D) — Find 에 withBlobs 를 준 때만 채운다. 쌍 · 체적 · 결합과는 상관없다 ──
    // 덩어리는 쌍에 붙여 둔다: 찾은 뒤에 쌍의 차례가 체적순으로 바뀌므로(Sorted), 따로 쌓아 번호로 맞추면 다른 쌍의 덩어리가 그려진다.
    public MeshBuilder? Blob;             // 겹친 덩어리의 겉면(모델 안쪽 좌표 · m — 그릴 때 Build 에 장면의 가운데를 넘긴다). 못 만들었으면 null
    public double BlobAreaM2 = double.NaN; // 겹친 입체의 겉넓이(Revit 이 준 값) — 만든 세모의 넓이 합과 견준다
    public bool BlobBoxed;                // 세모를 만들지 못해 경계상자로 대신했다
    public string BlobNote = "";          // 덩어리를 만들다 걸린 것("" = 없음)
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
    }

    /// <summary>
    /// 겹친 자리의 주인(JACK 2026-10-07 "슬래브 우선"): 기초 > 슬래브(바닥) > 기둥 > 벽 > 보 > 일반 모델.
    /// 슬래브는 통째로 세고 벽·기둥·보는 슬래브에 닿는 면까지만 센다(참고 산출서의 배수지 벽체가 이 방식 — 벽 높이 = 내부 높이).
    /// 숫자가 작을수록 우선. 같은 급이면 먼저 만든(Id 가 작은) 부재가 남는다.
    /// </summary>
    public static int Priority(Element el)
    {
        BuiltInCategory cat;
        try { cat = el.Category?.BuiltInCategory ?? BuiltInCategory.INVALID; }
        catch { cat = BuiltInCategory.INVALID; }
        return cat switch
        {
            BuiltInCategory.OST_StructuralFoundation => 0,
            BuiltInCategory.OST_Floors => 1,
            BuiltInCategory.OST_StructuralColumns => 2,
            BuiltInCategory.OST_Walls => 3,
            BuiltInCategory.OST_StructuralFraming => 4,
            BuiltInCategory.OST_GenericModel => 5,
            _ => 9,
        };
    }

    public const string PriorityText = "기초 > 슬래브(바닥) > 기둥 > 벽 > 보 > 일반 모델 (같은 급이면 먼저 만든 부재)";

    private sealed class Item
    {
        public required Element El;
        public required List<Solid> Solids;
        public required XYZ Min, Max;
        public int Prio;
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
    /// </summary>
    public static Found Find(Document doc, ICollection<ElementId> ids, TimeSpan? limit = null, bool withBlobs = false)
    {
        var found = new Found();
        var items = new List<Item>();
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
            var item = new Item { El = el, Solids = solids, Min = bb.Min, Max = bb.Max, Prio = Priority(el) };
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

                // 이미 형상 결합된 쌍은 Revit 이 한쪽을 잘라 놓았다 — 겹치지 않는다.
                try { if (JoinGeometryUtils.AreElementsJoined(doc, a.El, b.El)) continue; }
                catch { }

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
        return Sorted(found);
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

    // a 가 b 를 이긴다(겹친 자리를 가진다) = 우선순위가 높다, 같으면 Id 가 작다.
    private static bool Beats(Item a, Item b) => a.Prio != b.Prio ? a.Prio < b.Prio : a.El.Id.Value < b.El.Id.Value;

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
        public double RemainingM3; // 결합 뒤에도 남은 겹침의 합(못 한 쌍 포함)
    }

    // 쌍의 결합 결과로 적는 글자(OverlapPair.Status).
    public const string Joined = "결합됨";
    public const string NotNeeded = "결합 필요 없음(앞선 결합으로 겹침이 없어짐)";

    /// <summary>
    /// 겹친 쌍마다 형상 결합을 걸고, 우선순위가 높은 쪽(Winner)이 낮은 쪽(Loser)을 자르게 한다.
    /// 쌍 하나가 실패해도 나머지는 계속한다(쌍마다 따로 되돌린다). 전체가 되돌리기 한 번(Ctrl+Z)으로 묶인다.
    /// 그룹 안의 부재·다른 사용자가 편집 중인 부재는 건드리지 않는다.
    /// 앞선 결합으로 겹침이 이미 없어진 쌍은 걸지 않고, 자르는 순서를 우선순위대로 만들지 못한 쌍은 결합까지 되돌린다.
    /// 결합한 두 부재의 판정(DH_판정)은 '다시 계산 필요'로 바꿔 둔다 — 체적이 바뀌어 전의 '일치'를 믿을 수 없다.
    /// </summary>
    public static JoinReport Join(Document doc, List<OverlapPair> pairs)
    {
        var report = new JoinReport();
        using (var group = new TransactionGroup(doc, "DH 겹침 결합"))
        {
            group.Start();
            bool anyCommitted = false;
            foreach (var p in pairs)
            {
                var win = doc.GetElement(p.Winner);
                var lose = doc.GetElement(p.Loser);
                if (win == null || lose == null) { p.Status = "결합 못 함: 부재를 찾을 수 없음"; continue; }
                string? blocked = FormulaAutoFill.WhyNotEditable(doc, win) ?? FormulaAutoFill.WhyNotEditable(doc, lose);
                if (blocked != null) { p.Status = "결합 못 함: " + blocked; continue; }

                // 세 부재가 한 자리에서 겹치면 앞선 결합으로 이 쌍의 겹침이 이미 없어졌을 수 있다 — 그러면 결합을 걸지 않는다.
                try
                {
                    double now = Intersection(SolidReader.Solids(win), SolidReader.Solids(lose), out bool unmeasured);
                    if (!unmeasured && now < MinOverlapM3) { p.Status = NotNeeded; p.AfterM3 = now; continue; }
                }
                catch { /* 다시 잴 수 없으면 찾았을 때의 값대로 결합을 건다 */ }

                using var tx = new Transaction(doc, "DH 겹침 결합");
                var quiet = new QuietFailures();
                try
                {
                    tx.Start();
                    var options = tx.GetFailureHandlingOptions();
                    options.SetFailuresPreprocessor(quiet);
                    options.SetClearAfterRollback(true);
                    tx.SetFailureHandlingOptions(options);
                    if (!JoinGeometryUtils.AreElementsJoined(doc, win, lose)) JoinGeometryUtils.JoinGeometry(doc, win, lose);
                    // 우선순위가 높은 쪽이 자르게 한다(Revit 이 정한 기본 순서가 반대면 바꾼다).
                    if (!JoinGeometryUtils.IsCuttingElementInJoin(doc, win, lose)) JoinGeometryUtils.SwitchJoinOrder(doc, win, lose);
                    // 바꾼 뒤에 정말 그 순서가 됐는지 본다. 아니면 이 쌍은 결합까지 되돌린다(반대로 잘린 채 남기지 않는다).
                    doc.Regenerate();
                    if (!JoinGeometryUtils.IsCuttingElementInJoin(doc, win, lose))
                    {
                        tx.RollBack();
                        p.Status = "결합 못 함: 자르는 순서를 우선순위대로 바꾸지 못함";
                        continue;
                    }
                    // 결합하면 체적이 바뀐다 — 전에 적힌 '일치'가 그대로 남지 않게 판정을 바꿔 둔다(판정이 적힌 부재만).
                    FormulaAutoFill.MarkVerdictStale(win);
                    FormulaAutoFill.MarkVerdictStale(lose);
                    var status = tx.Commit();
                    anyCommitted |= status == TransactionStatus.Committed;
                    p.Status = status == TransactionStatus.Committed ? Joined
                             : "결합 못 함: Revit 이 되돌림" + (quiet.FirstError.Length > 0 ? $"({quiet.FirstError})" : "");
                }
                catch (Exception ex)
                {
                    try { if (tx.HasStarted() && !tx.HasEnded()) tx.RollBack(); } catch { }
                    p.Status = "결합 못 함: " + ex.Message;
                }
            }
            // 하나라도 결합했으면 되돌리기 한 번으로 묶고, 아무것도 못 했으면 흔적 없이 닫는다.
            if (anyCommitted) group.Assimilate(); else group.RollBack();
        }

        // 결합한 쌍을 다시 잰다 — 정말 겹침이 없어졌는가(화면을 정하는 값은 결합 '뒤'의 입체다).
        foreach (var p in pairs)
        {
            if (p.Status == NotNeeded) { report.NotNeeded++; continue; }
            if (p.Status != Joined) { report.Failed++; report.RemainingM3 += p.VolumeM3; continue; }
            try
            {
                var win = doc.GetElement(p.Winner);
                var lose = doc.GetElement(p.Loser);
                bool unmeasured = true;
                p.AfterM3 = win == null || lose == null ? double.NaN
                    : Intersection(SolidReader.Solids(win), SolidReader.Solids(lose), out unmeasured);
                if (unmeasured) p.AfterM3 = double.NaN; // 일부라도 재지 못했으면 '겹침이 없어졌다'고 하지 않는다
            }
            catch { p.AfterM3 = double.NaN; }
            if (p.AfterM3 < MinOverlapM3) report.Joined++;
            else
            {
                report.StillOverlap++;
                report.RemainingM3 += double.IsNaN(p.AfterM3) ? p.VolumeM3 : p.AfterM3;
                p.Status = double.IsNaN(p.AfterM3) ? "결합됨(다시 재지 못함)" : "결합됐지만 겹침이 남음";
            }
        }
        return report;
    }

    // 결합하다 Revit 이 띄우려는 경고는 지우고, 오류가 나면 그 쌍만 되돌린다(창을 띄워 전체를 멈추지 않는다).
    private sealed class QuietFailures : IFailuresPreprocessor
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
        var sb = new StringBuilder("남는쪽Id\t남는쪽\t잘리는쪽Id\t잘리는쪽\t겹친체적(m3)\t결합결과\t결합뒤겹침(m3)\n");
        foreach (var p in found.Pairs)
            sb.Append(CultureInfo.InvariantCulture,
                $"{p.Winner.Value}\t{Cell(p.WinnerName)}\t{p.Loser.Value}\t{Cell(p.LoserName)}\t{p.VolumeM3:0.######}\t{Cell(p.Status)}\t" +
                $"{(double.IsNaN(p.AfterM3) ? "" : p.AfterM3.ToString("0.######", CultureInfo.InvariantCulture))}\n");
        sb.Append(CultureInfo.InvariantCulture,
            $"\n검사한 부재\t{found.Checked}\n겹친 쌍\t{found.Pairs.Count}\n겹친 체적 합(m3)\t{found.TotalM3:0.######}\n" +
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
