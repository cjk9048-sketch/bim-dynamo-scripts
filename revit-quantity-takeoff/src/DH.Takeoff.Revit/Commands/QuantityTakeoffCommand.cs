using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using DH.Takeoff.Core;
using DH.Takeoff.Core.Formwork;

namespace DH.Takeoff.Revit.Commands;

/// <summary>
/// "수량산출" 버튼(JACK 2026-10-15 — «위치·부위 정하기 · 산출식 자동입력 · 산출근거 엑셀은 결국 수량산출이라 한 버튼 안에»).
/// 한 창에서 ① 부위·중분류 → ② 거푸집면 설정 → [완료](산출식 · 산출식 확인 · 거푸집 속성 · 산출근거 엑셀)로 간다. [닫기]는 그냥 나간다.
/// ① 은 전의 "위치·부위 정하기"(JACK 2026-10-13): 팝업의 3D 그림에서 부재를 찍어(또는 끌어) 고르고, 왼쪽 판에서 부위와 중분류(전의 '위치')를 넣으면
/// 바로 부재의 속성(DH_Zone · DH_Part)에 적는다. 값이 들어간 부재만 색이 바뀌고, 색이 안 바뀐 부재가 없을 때까지 한다.
/// ② 는 [다음 단계]에서 모델의 콘크리트를 재어(FormworkReader · FormworkAnalyzer) 대응표 + 3D 로 보여 준다(PlacePartView.Formwork.cs).
///  · R62(JACK 2026-10-26 «팝업을 쓸 때 Revit 이 락 걸리지 않았으면»): 명령은 창을 띄우고 바로 끝난다(늘 Succeeded — 열 때 적은 것이 남게).
///    창의 일은 TakeoffSession 의 요청 줄(ExternalEvent)로 돌고, 이름 있는 일 하나가 TransactionGroup 하나 = Revit 실행 취소 목록의 한 줄이다
///    («DH 부위·중분류» · «DH 거푸집 대응표» · «DH 산출서 준비» · «DH 거푸집 객체» · «DH 위치 색» · 열 때 «DH 수량산출 열기»). [처음 상태로 되돌리고 닫기]는 없앴다.
///  · 창이 떠 있는 동안 Revit 에서 모델이 바뀌면 쓰는 일을 막고 [다시 열기].
///  · [완료](Complete): 산출식은 필요한 부재만(식이 없거나 틀려진 부재 · '다시 계산 필요' — FormulaAutoFill.PlanRefresh) · 못 만든 부재는
///    창 위의 모달 창으로 하나씩(ExceptionReview) · 거푸집 속성(FormworkWriter) · 엑셀(콘크리트 + 거푸집 시트). 엑셀을 저장하면 창을 닫는다.
/// </summary>
[Transaction(TransactionMode.Manual)]
public sealed class QuantityTakeoffCommand : IExternalCommand
{
    private const string Title = "DH 수량산출";
    private const int AskBeforeCompute = 150;    // 식을 새로 만들 부재가 이보다 많으면 시작하기 전에 묻는다(오래 걸릴 수 있다)

    // 닫을 때 이어서 돈 산출식의 결과(알림 창과 일지에 쓴다).
    private sealed class FormulaFollow
    {
        public FormulaAutoFill.Refreshed Plan = new();
        public string Problem = "";       // 이어서 하지 못한 까닭("" = 없음)
        public string Table = "";         // 결과표(쓰지 않았으면 "")

        private IEnumerable<FormulaOutcome> All => Plan.All;
        public int Total => Plan.All.Count;
        public int Kept => All.Count(o => o.Kept);
        public int CellsFixed => All.Count(o => o.Kept && o.Verdict.Length > 0 && o.NoWrite.Length == 0 && o.Skipped.Length == 0);
        public int Drifted => All.Count(o => o.Kept && o.Drifted);
        public List<FormulaOutcome> NotExportable => All.Where(o => o.Kept && o.NotExportable.Length > 0).ToList();
        public int NewFormula => All.Count(o => !o.Kept && o.Written && o.Existing.Length == 0);
        public int Replaced => All.Count(o => !o.Kept && o.Overwrote);
        public int ReplacedStale => All.Count(o => !o.Kept && o.Overwrote && o.WasStale);
        public int SameFormula => All.Count(o => !o.Kept && o.Result.Ok && o.Existing.Length > 0 && o.Existing == o.Result.Formula);

        /// <summary>같은 식이 다시 나와 판정만 고쳐 적은 부재(결합에서 남는 쪽: '다시 계산 필요' → 같은 식 → '일치').</summary>
        public int VerdictOnly => All.Count(o => !o.Kept && o.Written && o.Existing.Length > 0 && o.Existing == o.Result.Formula);

        /// <summary>
        /// 적으려 했지만 적히지 않은 부재: 식을 새로 만들었는데 식 칸에 붙지 않았거나(칸이 없다 · 읽기 전용), 적다가 오류가 났다.
        /// 결과표에 까닭이 있다. (적지 않기로 한 부재 — 그룹 안 · 적을 것 없음 — 는 여기 들지 않는다.)
        /// </summary>
        public int NotWritten => All.Count(o => o.WriteError.Length > 0 || (o.Result.Ok && !o.Written && o.Skipped.Length == 0 && o.NoWrite.Length == 0));
        public int WasStale => All.Count(o => o.WasStale);
        public int StaleLeft => All.Count(o => o.StaleLeft);
        public int NotTarget => All.Count(o => o.NoVolume || o.NotConcrete);
        public int Skipped => All.Count(o => o.Skipped.Length > 0);
        public int BadReadBack => All.Count(o => (o.Written && !o.ReadBackOk) || (o.Verdict.Length > 0 && !o.VerdictReadBack));

        /// <summary>사람이 확인할 부재: 식을 만들지 못했고 넣어 둔 식도 맞지 않는 부재 + '다시 계산 필요'를 남긴 부재.</summary>
        public List<FormulaOutcome> Exceptions =>
            All.Where(o => !o.Kept && !o.Result.Ok && !o.NoVolume && !o.NotConcrete && (!o.ManualOk || o.StaleLeft)).ToList();

        /// <summary>검토 창으로 넘길 수 있는 부재(적지 않기로 한 부재는 창에서도 저장할 수 없다).</summary>
        public List<FormulaOutcome> Ask => Exceptions.Where(o => o.Skipped.Length == 0).ToList();

        /// <summary>모델에 적은 것이 되돌려졌다(Write 를 불렀는데 커밋되지 않았다).</summary>
        public bool RolledBack => Plan.Wrote && Plan.Status != TransactionStatus.Committed;

        /// <summary>
        /// 이번에 일어난 일이 있다(모델에 적었다 · 못 한 까닭이 있다 · 나중에 하기로 했다) — 그것만으로도 알림 창을 띄운다.
        /// 사람이 확인할 부재만 남아 있는 것은 여기 들지 않는다: 그것만으로는 창을 띄우지 않고(볼 때마다 뜨지 않게),
        /// 다른 까닭으로 창이 뜰 때 함께 적는다.
        /// </summary>
        public bool HasNews => Plan.Wrote || Plan.ParametersEnsured || Problem.Length > 0 || Plan.Declined;
    }

    // 이번 창에서 모델에 적는 일과 그 일지.
    private sealed class Session
    {
        private readonly Document _doc;
        private readonly StringBuilder _log = new();
        private bool _paramsReady;
        private string? _legacyJson;                 // 옛 판(v1 · v2)의 자료 원문 — 처음 덮어쓰기 전에 한 번 떠 둔다
        private readonly int _legacyVersion;

        public int Commits { get; private set; }     // 그룹 안에서 커밋된 트랜잭션 수(속성 칸 만들기 · 색 입히기 포함)
        public string LegacyDump { get; private set; } = "";
        public int LaterCuts { get; set; }           // ② 를 마지막으로 잴 때 나중에 치는 부재가 결합에서 구조체를 깎은 곳의 수(알림 창 — revit-verify R46 M1)
        public string ModelCheck { get; set; } = ""; // [수량산출] 시작의 빠른 모델 검증 글(R54 — ① 위 띠의 칩 · ④ 엑셀 메모 · "" = 걸린 것 없음)
        public string ModelCheckRanks { get; set; } = ""; // 그 검사를 한 때의 «겹친 자리» 순위(부위:순위 — ① 에서 바뀌면 ④ 전에 다시 검사)
        // 그 검사를 창의 딱지로(R63b — JACK 2026-10-26 «말이 많을수록 안 좋다 · 중요하면 위치 확인 가능하게»): 겹침 · 결합 순서(누르면 그 부재) ·
        // 딱지로 못 보이는 글(검사 오류 · 끝까지 못 봄) · 결합 순서가 반대인 쌍(자르는 부재 → 잘린 부재 — ② 에서 «나중 부재가 깎은 곳»과 합친다).
        public List<PlacePartView.Issue> ModelCheckIssues { get; set; } = new();
        public string ModelCheckNote { get; set; } = "";
        public List<(long cutter, long cut)> ModelCheckJoins { get; set; } = new();

        public Session(Document doc, ZoneScene.Data data)
        {
            _doc = doc;
            _legacyVersion = data.Stored.Plan.ReadVersion;
            if (data.Stored.Exists && _legacyVersion < ZonePlan.CurrentVersion) _legacyJson = ZonePlanStore.ReadRaw(doc);
        }

        public void Note(string line) => _log.Append(DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture)).Append('\t').AppendLine(line);
        public string LogText => _log.ToString();
        public void Committed() => Commits++;

        /// <summary>
        /// 부재의 값(과, 바뀌었으면 목록)을 모델에 적는다 — 트랜잭션 하나. 커밋 뒤 부재마다 값을 새로 읽어 돌려준다.
        /// 커밋되지 않았으면(RolledBack · Pending) 적힌 것이 없다. 던질 수 있다(편집기가 받아 모델을 다시 읽는다).
        /// </summary>
        public WriteResult Write(IReadOnlyList<MemberWrite> writes, ZonePlan? plan)
        {
            var watch = Stopwatch.StartNew();
            var result = new WriteResult();
            if (plan != null)
            {
                // 목록(자료 보관 요소)을 다른 사용자가 가졌으면 트랜잭션이 통째로 되돌려진다 — 열기 전에 까닭을 알린다.
                string locked = ZonePlanStore.WhyLocked(_doc);
                if (locked.Length > 0)
                {
                    result.Problem = locked;
                    foreach (var w in writes)
                    {
                        var now = _doc.GetElement(new ElementId(w.Member.Id));
                        result.After[w.Member.Id] = now == null ? PickValue.None : PlaceStamp.ValueOf(now);
                    }
                    Note($"적기\t부재 {writes.Count}\t목록 O\t열지 않음: {locked}");
                    return result;
                }
            }
            if (!_paramsReady)
            {
                // 속성 칸(DH_Zone · DH_Part)이 없으면 만든다 — 자체 트랜잭션으로 커밋한다(그룹 안).
                SharedParameterManager.EnsureParameters(_doc, new[] { ZoneAssigner.ParamName, ZoneScene.PartParam });
                _paramsReady = true;
                Commits++;
            }
            TransactionStatus status;
            using (var tx = new Transaction(_doc, "DH 부위·중분류"))
            {
                tx.Start();
                foreach (var w in writes)
                {
                    long id = w.Member.Id;
                    var el = _doc.GetElement(new ElementId(id));
                    if (el == null) { result.Locked[id] = "부재를 찾지 못함(지워짐)"; continue; }
                    try
                    {
                        // 이미 그 값인 칸은 건드리지 않는다(값을 확인만 하는 경우 — 기록만 붙인다).
                        bool zoneOk = ZoneAssigner.Current(el) == w.Target.Zone || DimensionExtractor.WriteString(el, ZoneAssigner.ParamName, w.Target.Zone);
                        bool partOk = ZoneScene.Text(el, ZoneScene.PartParam).Trim() == w.Target.Part || DimensionExtractor.WriteString(el, ZoneScene.PartParam, w.Target.Part);
                        if (!zoneOk || !partOk) { result.Locked[id] = "속성 칸(DH_Zone · DH_Part)이 없거나 읽기 전용"; continue; }
                        PlaceStamp.Set(el, w.Target);
                    }
                    catch (Exception ex) { result.Locked[id] = "적지 못함: " + ex.Message; }
                }
                if (plan != null)
                {
                    DumpLegacy();
                    // 창을 띄운 채 쓰면 그사이 다른 명령이 모델의 겹침 예외를 고칠 수 있다 — 창이 열 때 떠 둔 사본으로 덮지 않게 모델의 것을 옮겨 적는다(R62 — 설계 조사 높음).
                    var stored = ZonePlanStore.Read(_doc);
                    if (stored.Exists && stored.Problem.Length == 0 && !stored.TooNew) plan.Exceptions = stored.Plan.Exceptions;
                    ZonePlanStore.Write(_doc, plan);
                }
                status = tx.Commit();
            }
            result.Committed = status == TransactionStatus.Committed;
            if (result.Committed) Commits++;
            else
            {
                // 되돌려졌다: 적힌 것이 없다. 그 사이 고칠 수 없게 된 부재(다른 사용자가 가져감)가 있는지 다시 본다 — 다음에는 빼고 적게.
                result.Problem = $"Revit 이 변경을 되돌렸습니다({status})";
                result.Locked.Clear();
                foreach (var w in writes)
                {
                    var el = _doc.GetElement(new ElementId(w.Member.Id));
                    string? why = el == null ? "부재를 찾지 못함(지워짐)" : FormulaAutoFill.WhyNotEditable(_doc, el);
                    if (why != null) result.Locked[w.Member.Id] = why;
                }
            }
            // 되읽기: 커밋 뒤에 새로 읽은 값(화면은 이것으로만 고친다).
            foreach (var w in writes)
            {
                var el = _doc.GetElement(new ElementId(w.Member.Id));
                result.After[w.Member.Id] = el == null ? PickValue.None : PlaceStamp.ValueOf(el);
            }
            Note($"적기\t부재 {writes.Count}\t목록 {(plan != null ? "O" : "-")}\t{status}\t{watch.ElapsedMilliseconds} ms" +
                 (result.Locked.Count > 0 ? $"\t못 적음 {result.Locked.Count}: {string.Join(" / ", result.Locked.Values.Distinct().Take(3))}" : ""));
            return result;
        }

        // 옛 판의 자료를 새 모양으로 덮어쓰기 전에 원문을 로그 폴더에 한 번 떠 둔다
        // (v1 = 네모 방식: 네모의 모양은 새 자료에 없다 · v2 = 기본 16개 목록: 바꿔 둔 차례가 묶음의 차례로 다시 놓인다).
        private void DumpLegacy()
        {
            if (_legacyJson == null) return;
            try
            {
                string path = Path.Combine(LogDir(), SafeName(_doc.Title) + $"_zoneplan_v{_legacyVersion}_{DateTime.Now:yyyyMMdd_HHmmss}.json");
                File.WriteAllText(path, _legacyJson, new UTF8Encoding(false));
                LegacyDump = path;
                Note($"옛 판(v{_legacyVersion}) 자료를 떠 둠\t" + path);
            }
            catch (Exception ex) { Note($"옛 판(v{_legacyVersion}) 자료를 떠 두지 못함\t" + ex.Message); }
            _legacyJson = null;
        }
    }

    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        var uidoc = commandData.Application.ActiveUIDocument;
        if (uidoc?.Document == null) { message = "열린 Revit 문서가 없습니다."; return Result.Failed; }
        return Open(uidoc, commandData.Application, ref message);
    }

    // 창을 연다(명령 · [다시 열기] — R62). 한 번에 창 하나: 같은 문서면 떠 있는 창을 앞으로, 다른 문서면 먼저 닫게 한다.
    internal Result Open(UIDocument uidoc, UIApplication uiapp, ref string message)
    {
        var doc = uidoc.Document;
        if (TakeoffSession.Current is { } open)
        {
            if (open.Doc.Equals(doc)) { open.Activate(); return Result.Succeeded; }
            Dialogs.Show(Title, "다른 문서에 「수량산출」 창이 떠 있습니다 — 먼저 그 창을 닫아 주세요.");
            return Result.Cancelled;
        }
        // 고칠 수 없는 문서는 3D 자료를 읽기 전에 가른다(큰 모델에서 한참 읽은 뒤에 거절당하지 않게). 그룹을 시작할 때 한 번 더 걸린다.
        bool readOnly = false;
        try { readOnly = doc.IsReadOnly; } catch { }
        if (readOnly)
        {
            Dialogs.Show(Title, "이 문서는 읽기 전용이라 「수량산출」을 열지 않았습니다(넣는 즉시 모델에 적는 창입니다).");
            return Result.Cancelled;
        }

        ZoneScene.Data data;
        try { data = ZoneScene.Read(doc); }
        catch (Exception ex)
        {
            message = "모델을 읽는 중 오류: " + ex.Message;
            return Result.Failed;       // 아직 모델에 적은 것이 없다(레벨을 재느라 연 트랜잭션은 되돌렸다)
        }
        if (data.Members.Count == 0)
        {
            Dialogs.Show(Title, "수량을 낼 콘크리트 부재가 없습니다.\n(대상: 기둥 · 보 · 벽 · 바닥 · 기초 · 일반 모델 가운데 콘크리트 부재)");
            return Result.Cancelled;
        }
        // 더 새로운 판의 애드인이 저장한 목록: 이 판이 모르는 칸이 있을 수 있어, 열어서 다시 적으면 그 자료가 망가진다 — 열지 않는다.
        if (data.Stored.TooNew)
        {
            Dialogs.Show(Title, "이 모델의 부위·중분류 목록은 더 새로운 판의 DH 수량산출이 저장한 것이라, 이 판으로는 열지 않습니다(열어서 다시 적으면 그 자료가 망가집니다).\n" +
                                "애드인을 새 판으로 올린 뒤 다시 눌러 주세요.\n\n" + data.Stored.Problem);
            return Result.Cancelled;
        }
        // 저장된 목록을 읽지 못했다: 조용히 빈 것으로 시작하면 목록을 고칠 때 옛 자료를 덮어쓴다 — 먼저 묻는다.
        if (data.Stored.Problem.Length > 0)
        {
            var ask = Dialogs.New(Title);
            ask.MainInstruction = "모델에 저장돼 있는 부위·중분류 목록을 읽지 못했습니다";
            ask.MainContent = data.Stored.Problem + "\n\n기본 목록으로 시작할 수 있습니다. 그렇게 열어 목록을 고치면 저장돼 있던 목록을 덮어씁니다(부재에 적힌 부위·중분류 값은 그대로 보입니다).";
            ask.AllowCancellation = true;
            ask.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "기본 목록으로 시작");
            ask.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "그만두기");
            if (ask.Show() != TaskDialogResult.CommandLink1) return Result.Cancelled;
        }

        var session = new Session(doc, data);
        session.Note($"열기\t{doc.Title}\t부재 {data.Members.Count} · 세모 {data.Triangles} · 모서리 {data.Segments} · 읽기 {data.ReadMs} ms" +
                     $" · 상자로 그림 {data.Boxed} · 못 그림 {data.Undrawn} · 읽기 오류 {data.ReadErrors} · 띠 나누기로 만든 면 {data.FacesFallback} · 못 만든 면 {data.FacesSkipped} · 엇갈린 면 {data.FacesTwisted}" +
                     $" · 레벨 {data.Levels.Count} · 작업공유 {(doc.IsWorkshared ? "O" : "X")} · 그림 등급 {RenderCapability.Tier >> 16}" +
                     $" · 넣은 뒤 모양 바뀜 {data.Members.Count(m => m.Value.Changed && !m.Value.IsEmpty)}" +
                     (data.BoxedIds.Count > 0 ? $" · 상자로 그린 부재 Id {string.Join(",", data.BoxedIds.Take(12))}" : "") +
                     (data.UndrawnIds.Count > 0 ? $" · 못 그린 부재 Id {string.Join(",", data.UndrawnIds.Take(12))}" : ""));
        // 구멍 내는 보이드(입체 없이 다른 부재를 자름)는 대상에서 뺀다(JACK 2026-10-21) — 뺀 것을 일지에만.
        try
        {
            var voids = DimensionExtractor.TargetVoids(doc);
            if (voids.Count > 0) session.Note($"보이드 빼임 {voids.Count} (Id {string.Join(",", voids.Take(12))}{(voids.Count > 12 ? " …" : "")})");
        }
        catch (Exception ex) { session.Note("보이드 찾기 오류\t" + ex.Message); }

        // Revit 화면을 3D 뷰로(R61a — JACK 2026-10-26 «창을 열 때»): 그만두는 갈래들 뒤 · 그룹을 열기 전(모달 창 밑에서 새 뷰 창을 열지 않게).
        // Revit 선택은 먼저 읽어 둔다 — 뷰를 바꾸면 선택이 풀릴 수 있다(계획 검토 H2). 닫을 때 이 선택으로 되돌린다(H3).
        List<ElementId> selectedBefore;
        try { selectedBefore = uidoc.Selection.GetElementIds().ToList(); } catch { selectedBefore = new List<ElementId>(); }
        var (viewNote, viewSwitched) = RevitFollow.Use3DView(uidoc);
        session.Note($"3D 뷰\t{viewNote}\t열 때 선택 {selectedBefore.Count}");

        // 열 때 쓰는 것(옛 대응표 파일 옮기기 · Revit 판의 종류 받아들이기)은 Revit 실행 취소 한 줄 «DH 수량산출 열기»(R62 — 계획 검토 M5).
        // 이 문서를 지금 고칠 수 없으면(읽기 전용 등) 3D 를 띄우기 전에 여기서 드러난다.
        var group = new TransactionGroup(doc, "DH 수량산출 열기");
        try
        {
            // 안의 트랜잭션이 '실패 처리 대기'로 남지 않게 한다 — 남으면 그룹을 닫을 수 없다(문서: failure mode 에서는 닫기가 예외).
            group.IsFailureHandlingForcedModal = true;
            var started = group.Start();
            if (started != TransactionStatus.Started) throw new InvalidOperationException($"트랜잭션 그룹을 시작하지 못했습니다({started}).");
        }
        catch (Exception ex)
        {
            try { group.Dispose(); } catch { }
            if (viewSwitched) try { uidoc.Selection.SetElementIds(selectedBefore); } catch { }   // 뷰를 바꾸며 풀린 선택을 되돌린다(R61a 설치 전 검토 L5)
            Dialogs.Show(Title, "이 문서는 지금 고칠 수 없어 「수량산출」을 열지 않았습니다.\n" + ex.Message);
            return Result.Cancelled;
        }
        var opening = new Opening();
        try
        {
            var (initialRules, initialProblem, rulesMigrated) = LoadRules(doc, session);
            // Revit 판의 종류 칸(R57 P3) — 대응표를 읽은 뒤 그 위에 받아들인다.
            var (takenRules, plateNote, platesTaken) = TakePlateKinds(doc, initialRules, session);
            opening.Rules = takenRules;
            opening.Problem = plateNote.Length > 0 ? (initialProblem.Length > 0 ? initialProblem + "\n" + plateNote : plateNote) : initialProblem;
            opening.Migrated = rulesMigrated;
            opening.PlatesTaken = platesTaken;
        }
        finally
        {
            CloseGroup(group, keep: true, session);
            try { group.Dispose(); } catch { }
        }

        PickEditor? editor = null;
        try
        {
            editor = new PickEditor(data.Members, data.Levels, data.Stored.Plan, session.Write, () => ZoneScene.Reread(doc, data.Members), doc.Title);
            session.Note($"목록\t구조물 {editor.Structure}({(PartCatalog.FindStructure(data.Stored.Plan.Structure) != null ? "저장된 것" : "제목으로 알아냄")})" +
                         $"\t중분류 {editor.Places.Count} · 부위 {editor.Parts.Count}\t저장된 판 v{data.Stored.Plan.ReadVersion}{(data.Stored.Exists ? "" : "(저장된 것 없음)")}");
            // Revit 에서 부재를 골라 놓고 눌렀으면 팝업에서도 그 부재를 골라 둔다.
            editor.Select(selectedBefore.Select(id => id.Value));
            // ① 을 열기 전에 한 번 빠르게 모델 검증(최대 15 초 남짓 · 알림만 — R54).
            session.ModelCheck = QuickModelCheck(doc, session);
            session.ModelCheckRanks = RankSignature(editor);
            OpenWindow(uiapp, uidoc, editor, data, session, Notices(data), new RevitFollow(uidoc, data.Origin, session.Note, selectedBefore, viewSwitched), opening);
        }
        catch (Exception ex)
        {
            session.Note("오류\t" + ex);
            // 창을 반쯤 만들다 났으면 그 창을 거둔다 — 남으면 [수량산출]이 그것만 앞으로 부르고 다른 명령이 막힌다(R62 설치 전 검토 낮음).
            try { if (TakeoffSession.Current is { } half && half.Doc.Equals(doc)) half.End(); } catch { }
            try { WriteLogs(doc, editor, session, kept: true, revert: false); } catch { }
            Dialogs.Show(Title, "「수량산출」 창을 열지 못했습니다: " + ex.Message);
        }
        // ★ 늘 Succeeded — 창은 띄운 채로 남고(R62), 열 때 적은 것(대응표 옮기기 · 판 종류)이 남아야 한다(Failed/Cancelled 면 Revit 이 이 명령에서 적은 것을 되돌린다).
        return Result.Succeeded;
    }

    // 열 때 정한 대응표(창의 처음 대응표) · 알릴 것 · 옛 파일을 옮겼는가 · Revit 판의 종류를 받아들였는가.
    private sealed class Opening
    {
        public FormworkRules Rules = new();
        public string Problem = "";
        public bool Migrated, PlatesTaken;
    }

    // [완료]로 닫았을 때의 알림: 엑셀 파일 · 산출식 · 산출식 확인 창에서 한 일 · 부위·중분류. [엑셀 파일 열기]가 첫 단추다.
    private static void ReportDone(UIDocument uidoc, PickEditor? editor, Session session, Completion done, string closeProblem, string paintProblem,
                                   View3D? view, Dictionary<string, string> legend, string table, string journal)
    {
        var plan = done.Plan;
        var text = new StringBuilder();
        if (closeProblem.Length > 0) text.AppendLine(closeProblem).AppendLine();
        if (done.Overlap.Length > 0) text.AppendLine("⚠ " + done.Overlap + " · 고치기: [모델 검증]").AppendLine();
        if (plan != null)
        {
            text.AppendLine($"산출근거에 실은 부재 {plan.Listed}개 — {plan.Rows.Count}줄 · 산출 수량 합 {FormulaDecomposer.RoundQuantity(plan.Quantity).ToString("#,##0.00", CultureInfo.InvariantCulture)} m³" +
                            (plan.Missing > 0 ? $" · 싣지 못한 부재 {plan.Missing}개(「미산출 부재」 시트)" : ""));
        }
        if (done.Review != null)
            text.AppendLine($"산출식 확인 창: {done.Review.Items}건(부재 {done.Review.Elements}개) 가운데 식을 넣은 것 {done.Review.Saved}건" +
                            (done.Review.Failed.Count > 0 ? $" · 적지 못한 것 {done.Review.Failed.Count}건" : ""));
        if (done.Names is { } names)
        {
            int renamed = names.Fresh + names.Copies + names.Duplicates + names.Prefixed;
            var why = new List<string>();
            if (names.Fresh > 0) why.Add($"새 부재 {names.Fresh}");
            if (names.Copies > 0) why.Add($"복사본 · 나눈 조각 {names.Copies}");
            if (names.Duplicates > 0) why.Add($"같은 이름이 있던 부재 {names.Duplicates}");
            if (names.Prefixed > 0) why.Add($"부위가 바뀌어 앞 글자를 바꾼 부재 {names.Prefixed}");
            text.AppendLine($"부재 이름({MemberNames.Param}): 새로 붙임 {renamed}개" + (why.Count > 0 ? "(" + string.Join(" · ", why) + ")" : "") + $" · 그대로 {names.Kept}개" +
                            (names.Locked > 0 ? $" · ★ 고칠 수 없어 그대로 둔 부재 {names.Locked}개(그룹 안 · 다른 사용자가 편집 중 — 이름이 없으면 엑셀에 유형 이름)" : "") +
                            (names.Failed.Count > 0 ? $" · ★ 적지 못한 부재 {names.Failed.Count}개(일지)" : "") +
                            (names.NoParam > 0 ? $" · ★ 이름 칸이 없는 부재 {names.NoParam}개(같은 이름의 다른 칸이 있거나 칸을 만들지 못함 — 엑셀에 유형 이름)" : ""));
        }
        if (done.NamesProblem.Length > 0) text.AppendLine("★ 부재 이름을 다 붙이지 못했습니다: " + done.NamesProblem + " — 엑셀의 이름 없는 줄은 유형 이름입니다.");
        if (done.Formwork is { Committed: true } fw && done.FormworkProblem.Length == 0)
            text.AppendLine($"거푸집 속성: 부재 {fw.Elements}개에 종류별 넓이(지침 내역적용수량 이름) · 합계 {fw.Total.ToString("#,##0.00", CultureInfo.InvariantCulture)} ㎡" +
                            (fw.Failed.Count > 0 ? $" · 적지 못한 부재 {fw.Failed.Count}개({fw.Failed[0]})" : "") +
                            (fw.ReadBackDiffers > 0 ? $" · ★ 적은 뒤 다시 읽은 값이 다른 칸 {fw.ReadBackDiffers}개" : ""));
        else if (done.Formwork != null || done.FormworkProblem.Length > 0)
            text.AppendLine("★ 거푸집 속성을 다 적지 못했습니다: " + (done.FormworkProblem.Length > 0 ? done.FormworkProblem : "되돌려짐"));
        if (done.Formwork is { StaleLeft.Count: > 0 } stale)
            text.AppendLine($"★ 전 판이 적은 «{FormworkTable.HighName}» 값을 «7m이하»로 옮기지 못한 부재 {stale.StaleLeft.Count}개(Id {string.Join(", ", stale.StaleLeft.Take(6))}{(stale.StaleLeft.Count > 6 ? " …" : "")})" +
                            " — 이제 7 m 구분이 없어 쓰지 않는 칸입니다. 그 부재를 편집할 수 있게 한 뒤 다시 [완료]");
        if (done.Sheet != null)
            text.AppendLine($"거푸집(「구체공산출근거」 시트): {done.Sheet.Lines.Count}줄 · 종류 {done.Sheet.Sections.Count}가지 · 합계 {done.Sheet.Sections.Sum(s => s.bim).ToString("#,##0.00", CultureInfo.InvariantCulture)} ㎡");
        if (done.Dropped > 0)
            text.AppendLine($"사람이 지운 거푸집 판: {done.Dropped}장 · {done.DroppedArea.ToString("#,##0.00", CultureInfo.InvariantCulture)} ㎡ — 엑셀 · 거푸집 객체에는 넣지 않음 · 부재 속성(지침용) 합계에는 들어 있음");
        if (done.Moved > 0)
            text.AppendLine($"다른 줄로 옮긴 판: {done.Moved}장 · {done.MovedArea.ToString("#,##0.00", CultureInfo.InvariantCulture)} ㎡ — 그 줄 · 그 종류로 엑셀 · 거푸집 객체에 넣음");
        if (done.Orphans > 0)
            text.AppendLine($"★ 옮길 줄이 없어져 지운 판으로 돌아간 판 {done.Orphans}장 — ② 에서 다시 [옮기기]");
        if (done.LostDrops > 0)
            text.AppendLine($"★ 모양이 바뀌어 자리를 찾지 못한 뺀 판 {done.LostDrops}장 — 그 자리는 다시 셌습니다(② 3D 에서 확인)");
        if (done.ChangedDrops > 0)
            text.AppendLine($"★ 모양이 바뀐 뺀 판 {done.ChangedDrops}장 — 일부 조각만 찾았거나 빠진 넓이가 뺄 때와 다릅니다(② 3D 에서 확인)");
        if (session.LaterCuts > 0) text.AppendLine("⚠ " + LaterCutsNote(session.LaterCuts));
        if (done.Objects is { Committed: true } plates && done.Objects.Blocked.Length == 0)
        {
            text.AppendLine($"거푸집 객체: 판 {plates.Created}개(일반 모델 · 12 mm)" + (plates.Deleted > 0 ? $" · 전 판 {plates.Deleted}개 지움" : "") +
                            (plates.Failed.Count > 0 ? $" · ★ 못 만든 판 {plates.Failed.Count}개(일지)" : "") + " — 납품 전에 리본 [거푸집 › 거푸집 삭제]");
            if (FormworkHiddenNote(uidoc, plates.Created) is { Length: > 0 } hidden) text.AppendLine("⚠ " + hidden);
            if (PlatesNote(plates) is { Length: > 0 } platesNote) text.AppendLine(platesNote);
            if (plates.Suspicious > 0 || plates.ReadBackProblem.Length > 0)
                text.AppendLine($"  ★ 다시 읽어 보니 걸리는 판 {plates.Suspicious}개(칸을 다 못 적음 {plates.MissingParams} · 칸 값이 다름 {plates.ReadBackDiffers} · 자리 어긋남 {Math.Max(0, plates.Misplaced)})" +
                                (plates.ReadBackProblem.Length > 0 ? " · 되읽기 오류: " + plates.ReadBackProblem : "") + " — 일지");
        }
        else if (done.Objects != null || done.ObjectsProblem.Length > 0)
            text.AppendLine("★ 거푸집 객체를 만들지 못했습니다: " + (done.ObjectsProblem.Length > 0 ? done.ObjectsProblem : done.Objects?.Error is { Length: > 0 } error ? error : "되돌려짐"));
        if (editor != null)
        {
            var p = editor.Progress();
            text.AppendLine($"부위·중분류: 부재 {p.Total}개 — 끝 {p.Done} · 아직 {p.Todo} · 하나만 {p.Partial}" + (p.Outside > 0 ? $" · 확인 필요 {p.Outside}" : ""));
        }
        AppendFormulaReport(text, done.Formulas, placesKept: true);
        text.AppendLine();
        text.AppendLine("이 창에서 넣은 것은 누를 때마다 Revit 실행 취소 목록의 한 줄(«DH 부위·중분류» · «DH 산출서 준비» · «DH 거푸집 객체» …)로 남았습니다.");
        if (session.LegacyDump.Length > 0) text.AppendLine("전의 판이 저장한 목록의 원문: " + session.LegacyDump);
        if (paintProblem.Length > 0) text.AppendLine().AppendLine(paintProblem);
        text.AppendLine();
        text.AppendLine("엑셀: " + (done.Saved ? done.ExcelPath : "★ 저장하지 못했습니다" + (done.ExcelProblem.Length > 0 ? " — " + done.ExcelProblem : "") + " ([완료]를 다시 누르면 다시 만듭니다)"));
        text.AppendLine("결과표: " + table);
        text.Append("일지: " + journal);

        var td = Dialogs.New(Title);
        td.AllowCancellation = true;
        td.MainInstruction = done.Saved ? "수량산출 — 완료(엑셀 저장)" : "수량산출 — 완료(엑셀은 저장하지 못함)";
        // 본문은 핵심 두세 줄 + 걸리는 것(★ · ⚠)만, 나머지(내역 · 파일 경로 · 되돌리기 안내)는 «자세히»(JACK 2026-10-21 «내용이 너무 많아 가독성이 떨어져 짧게 요약»).
        td.MainContent = Summary(done, text.ToString());
        td.ExpandedContent = text.ToString();
        if (done.Saved) td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "엑셀 파일 열기");
        if (view != null)
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, $"확인용 3D 뷰 열기 ('{ZoneAssigner.CheckViewName}')", "중분류별 색 · 거푸집 객체 숨김");
        var schedule = done.Objects is { Committed: true, ScheduleId: { } scheduleId } ? uidoc.Document.GetElement(scheduleId) as View : null;
        if (schedule != null)
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, $"거푸집 객체 일람표 열기 ('{FormworkObjects.ScheduleName}')", "판마다 종류 · 넓이 · 산출식");
        td.AddCommandLink(TaskDialogCommandLinkId.CommandLink4, "닫기");
        var pressed = td.Show();
        if (pressed == TaskDialogResult.CommandLink1)
        {
            try { Process.Start(new ProcessStartInfo(done.ExcelPath) { UseShellExecute = true }); }
            catch (Exception ex) { Dialogs.Show(Title, "파일을 열지 못했습니다: " + ex.Message + "\n\n" + done.ExcelPath); }
        }
        else if (pressed == TaskDialogResult.CommandLink2 && view != null)
        {
            try { uidoc.ActiveView = view; }
            catch (Exception ex) { Dialogs.Show(Title, $"뷰를 열지 못했습니다: {ex.Message}\n\n프로젝트 탐색기의 3D 뷰에서 '{ZoneAssigner.CheckViewName}'을 열어 주세요."); }
        }
        else if (pressed == TaskDialogResult.CommandLink3 && schedule != null)
        {
            try { uidoc.ActiveView = schedule; }
            catch (Exception ex) { Dialogs.Show(Title, $"일람표를 열지 못했습니다: {ex.Message}\n\n프로젝트 탐색기의 일람표에서 '{FormworkObjects.ScheduleName}'을 열어 주세요."); }
        }
    }

    // 지금 뷰에서 거푸집 객체가 꺼져 있으면 한 줄(R56a — JACK 2026-10-22 «거푸집 삭제 뒤 다시 [수량산출] → 거푸집 생성이 안 돼»: 일지로는 만들어져
    // 모델에 남았는데 보이지 않았다 — 확인용 3D 뷰는 거푸집 객체를 숨기고 · [거푸집 끄기]를 한 뷰는 새 판도 꺼진 채다). 읽지 못하면 "".
    private static string FormworkHiddenNote(UIDocument uidoc, int plates)
    {
        if (plates <= 0) return "";
        try
        {
            var doc = uidoc.Document;
            var view = uidoc.ActiveView;
            if (view == null || view.IsTemplate || view is ViewSchedule) return "";
            // 까닭마다 푸는 길이 다르다(계획 검토 L1): 임시 숨기기/격리 · 일반 모델 범주 · 하위 범주(뷰 템플릿이 정하면 템플릿에서) · 판을 낱개로 숨김.
            var sub = FormworkObjects.FindSubcategory(doc);
            var parent = Category.GetCategory(doc, BuiltInCategory.OST_GenericModel);
            bool template = view.ViewTemplateId != ElementId.InvalidElementId;
            var first = FormworkObjects.Find(doc).FirstOrDefault();
            string where = $"거푸집 판 {plates}장 — 지금 뷰 «{view.Name}»에서 ";
            if (first != null && view.IsTemporaryHideIsolateActive() && !view.IsElementVisibleInTemporaryViewMode(TemporaryViewMode.TemporaryHideIsolate, first))
                return where + "임시 숨기기/격리로 안 보임 · 뷰 아래 [임시 숨기기/격리 재설정]";
            if (parent != null && view.GetCategoryHidden(parent.Id))
                return where + "«일반 모델» 범주가 꺼져 있음 · 보이기/그래픽에서 켜기" + (template ? "(뷰 템플릿이 정함)" : "");
            if (sub != null && view.GetCategoryHidden(sub.Id))
                return where + (template ? "꺼져 있음 · 뷰 템플릿이 정함(템플릿에서 «DH 거푸집 객체» 켜기)" : "꺼져 있음 · 리본 [거푸집 › 거푸집 켜기]");
            if (first != null && doc.GetElement(first) is Element plate && plate.IsHidden(view))
                return where + "판이 숨겨져 있음 · 리본 [거푸집 › 거푸집 켜기]";
            return "";
        }
        catch { return ""; }
    }

    // 완료 알림의 본문(JACK 2026-10-21 고름 «핵심 두세 줄 + 자세히 펼치기»): 콘크리트 · 거푸집 합 / 부재 · 줄 · 판 / 새 이름 · 바꾼 식(0 인 것은 뺀다) +
    // 자세한 글에서 걸리는 줄(★ · ⚠ — 줄 가운데의 ★ 는 그 줄의 이름표 + ★ 뒤) + 입체 없는 대상 아님 부재(Id · 카테고리 · 유형 — JACK 고름 «완료 팝업에만 한 줄»).
    private static string Summary(Completion done, string detail)
    {
        static string N(double v) => v.ToString("#,##0.00", CultureInfo.InvariantCulture);
        var lines = new List<string>();
        var plan = done.Plan;
        double formwork = done.Sheet?.Sections.Sum(s => s.bim) ?? done.Formwork?.Total ?? 0;
        if (plan != null) lines.Add($"콘크리트 {N(FormulaDecomposer.RoundQuantity(plan.Quantity))} m³" + (formwork > 0 ? $" · 거푸집 {N(formwork)} ㎡" : ""));
        var counts = new List<string>();
        if (plan != null) counts.Add($"부재 {plan.Listed}개 · 산출근거 {plan.Rows.Count}줄");
        if (done.Objects is { Committed: true } plates) counts.Add($"거푸집 판 {plates.Created}장");
        if (counts.Count > 0) lines.Add(string.Join(" · ", counts));
        var changes = new List<string>();
        if (done.Names is { } names && names.Fresh + names.Copies + names.Duplicates + names.Prefixed is int renamed && renamed > 0) changes.Add($"새 부재 이름 {renamed}");
        if (done.Formulas.NewFormula > 0) changes.Add($"산출식 새로 적음 {done.Formulas.NewFormula}");
        if (done.Formulas.Replaced > 0) changes.Add($"산출식 바꿔 적음 {done.Formulas.Replaced}");
        if (done.VoidsWiped.Count > 0) changes.Add($"보이드 값 지움 {done.VoidsWiped.Count}");
        if (changes.Count > 0) lines.Add(string.Join(" · ", changes));
        // 입체가 없어 대상에서 뺀 부재(보이드는 처음부터 빠져 여기에 없다).
        var solidless = done.Formulas.Plan.All.Where(o => o.NoVolume && !o.NotConcrete).ToList();
        if (solidless.Count > 0)
            lines.Add($"대상 아님 {solidless.Count}개 · 입체 없음 (" + string.Join(", ", solidless.Take(3).Select(o => $"{o.Id.Value} {o.Category} {o.TypeName}".Trim())) +
                      (solidless.Count > 3 ? $" 외 {solidless.Count - 3}" : "") + ")");
        // 걸리는 것: 자세한 글의 ★ · ⚠ 줄.
        var problems = Flags(detail);
        if (problems.Count > 0)
        {
            lines.Add("");
            lines.AddRange(problems);
        }
        return string.Join("\n", lines);
    }

    // 자세한 글에서 걸리는 줄(★ · ⚠ 로 시작 · 줄 가운데의 ★ 는 그 줄의 이름표 + ★ 뒤) — 알림 창 본문에 올린다.
    private static List<string> Flags(string detail)
    {
        var problems = new List<string>();
        foreach (string raw in detail.Split('\n'))
        {
            string line = raw.Trim();
            if (line.StartsWith('★') || line.StartsWith('⚠')) problems.Add(line);
            else if (line.IndexOf('★') is int at && at > 0)
            {
                int colon = line.IndexOf(':');
                string label = colon > 0 && colon < at ? line[..colon].Trim() + ": " : "";
                problems.Add(label + line[at..].Trim());
            }
        }
        return problems.Distinct().ToList();
    }

    // [완료]의 결과(창을 닫은 뒤 알림 창과 일지에 쓴다).
    private sealed class Completion
    {
        public FormulaFollow Formulas = new();
        public ExceptionReview.Outcome? Review;
        public FormworkWriter.Outcome? Formwork;
        public string FormworkProblem = "";  // 거푸집 속성을 적지 못한 까닭("" = 없음)
        public FormworkSheetData? Sheet;
        public FormworkObjects.Outcome? Objects;  // 거푸집 객체(R44)
        public string ObjectsProblem = "";         // 거푸집 객체를 만들지 못한 까닭("" = 없음)
        public MemberNamer.Outcome? Names;         // 부재 이름(R53b)
        public List<long> VoidsWiped = new();      // 전에 적은 값을 지운 보이드(JACK 2026-10-21)
        public string VoidProblem = "";            // 보이드 값을 지우다 난 오류("" = 없음)
        public CalcSheetExporter.Collected? Read;  // ③ 에 들어갈 때 모은 산출근거(R59 — [완료]가 그대로 엑셀로)
        public bool Prepared;                      // ③ 에 들어갈 때의 일이 끝까지 돌았다(산출근거를 모음)
        public Dictionary<long, FormulaGeometry?> Geometry = new();   // ③ 콘크리트 숫자의 부재 도형(R61b · R62 — 창은 API 를 부르지 않아 ③ 에 들어갈 때 미리 푼다)

        /// <summary>
        /// ③ 에 들어갈 때의 단계가 모두 됐다(R59 계획 검토 M5): 산출식 · 이름 · 거푸집 속성(트랜잭션) · 보이드 · 시트 · 산출근거. 이것만 다시 쓴다 — 아니면 [다음 단계]가
        /// 다시 돈다(실패한 단계를 다시 시도). 부재별 막힘(그룹 안 · 남이 가진 부재 — 다시 돌아도 같음)은 보지 않는다(설치 전 검토 중간 — ③ 위 띠에 알림).
        /// </summary>
        public bool Clean => Prepared && Formulas.Problem.Length == 0 && !Formulas.Plan.Declined && !Formulas.RolledBack
                             && NamesProblem.Length == 0 && FormworkProblem.Length == 0 && VoidProblem.Length == 0 && Sheet != null && Plan != null;
        public string NamesProblem = "";           // 부재 이름을 다 붙이지 못한 까닭("" = 없음)
        public CalcPlan? Plan;
        public XlsxBook? Book;              // 지은 엑셀(쓰기는 창을 닫아 그룹이 모델에 남은 뒤에 — revit R42 A1)
        public string ExcelTarget = "";     // 고른 저장 위치("" = 고르지 않았다)
        public string ExcelPath = "";       // 실제로 저장한 파일("" = 아직/못 했다)
        public string ExcelProblem = "";    // 쓰지 못한 까닭("" = 없음)
        public string Overlap = "";         // 겹친 부재 알림("" = 없음)
        public int Dropped;                 // ② 3D 에서 사람이 뺀 거푸집 판(R48 — 엑셀 · 거푸집 객체에 넣지 않음)
        public double DroppedArea;          // 그 넓이(㎡)
        public int Moved, Orphans;          // 다른 줄로 옮긴 판 · 옮길 줄이 없어져 지운 판으로 돌아간 판(R55b)
        public double MovedArea;            // 옮긴 판의 넓이(㎡ — 세는 것만)
        public int LostDrops;               // 모양이 바뀌어 자리를 찾지 못해 다시 센 뺀 판
        public int ChangedDrops;            // 찾았지만 모양이 바뀐 뺀 판(일부 조각만 찾음 · 넓이가 달라짐)
        /// <summary>엑셀을 지었고 저장 위치를 골랐다 — 창을 닫는다(파일은 그룹이 남은 뒤에 쓴다).</summary>
        public bool Ready => Book != null && ExcelTarget.Length > 0;
        public bool Saved => ExcelPath.Length > 0;
    }

    // [완료](JACK 2026-10-15 «완료버튼을 누르면 자동으로 산출식 자동입력이 되고 엑셀 저장위치가 뜨게하고 저장하면 팝업은 닫히고»):
    //  ① 산출식 — 필요한 부재만(식이 없거나 지금 모델과 맞지 않는 부재 · '다시 계산 필요'). 지금도 맞는 식은 그대로 둔다.
    //  ② 식을 못 만든 부재를 하나씩 띄워 넣는다(인터뷰 — 창 위의 모달 창, 저장은 그 자리에서).
    //  ③ 거푸집 속성: 부재마다 종류별 넓이(K-water 지침 내역적용수량 이름) + 합계 — ② 거푸집면 설정에서 잰 것(미리보기 확인).
    //     이어서 거푸집 객체(R44): 거푸집 시트의 줄 조각마다 판 하나(일반 모델 — FormworkObjects). 엑셀 저장 창보다 먼저 짓는다
    //     (JACK 2026-10-16 «저장 취소해도 만들기»). 저장을 그만두고 다시 [완료]를 눌러도 줄 조각이 그대로면 다시 짓지 않는다(BuildPlates).
    //  ④ 엑셀 저장 위치 → 그 파일을 지금 쓸 수 있는지 미리 보고(엑셀에 열려 있으면 알리고 머문다) 산출근거 엑셀(콘크리트 + 거푸집 시트 ·
    //     수량집계표)을 지어 둔 뒤 창을 닫는다(돌려주는 값의 Ready). 파일은 창을 닫아 그룹이 모델에 남은 것을 본 뒤에 쓴다 — 남지 못하면
    //     모델은 되돌아가는데 엑셀만 그 값으로 남는 일이 없게(revit R42 A1 · SaveExcel). 그만두면 창에 머문다 — 채운 식 · 속성은 그대로이고,
    //     다시 [완료]를 누르면 필요한 것만 다시 본다.
    // 모델에 적는 것은 모두 요청 하나(«DH 산출서 준비» — R62)의 묶음 안에서 적는다(Revit 실행 취소 한 줄).
    private static bool Prepare(Window window, PlacePartView view, PickEditor editor, Document doc, Session session, bool dark, Completion done)
    {
        session.Note("③ 산출서 확인\t들어가기 시작");
        // 넣은 뒤 모양이 바뀌었는데 확인하지 않은 부재(R52b — JACK 2026-10-19 인터뷰 «알리고 진행»): 묻고, 계속하면 지금 값으로 만든다(엑셀 «부재 목록»에 남는다).
        // 고칠 수 없는 부재 · 그려지지 않은 부재도 센다 — 확인할 수는 없어도 산출서에는 실린다(R52b core-verify 중간 4).
        // ②-0 보이드(JACK 2026-10-21 «구멍 내는 보이드는 처음부터 빼기 · 이미 적힌 값도 지워줘»): 대상에서 빠진 보이드에 전에 적은 값을 지운다(창의 그룹 안 — Ctrl+Z 한 줄).
        done.VoidProblem = view.RunLocked(() => done.VoidsWiped = WipeVoids(doc, session));
        if (done.VoidProblem.Length > 0) session.Note("완료\t보이드 값 지우기 오류\t" + done.VoidProblem);
        int changed = editor.Members.Count(m => m.Value.Changed && !m.Value.IsEmpty);
        if (changed > 0)
        {
            int locked = editor.Members.Count(m => m.Value.Changed && !m.Value.IsEmpty && m.Locked.Length > 0);
            var go = MessageBox.Show(window,
                $"넣은 뒤 모양(부피·크기·자리)이 바뀐 부재 {changed}개의 위치·부위를 확인하지 않았습니다" +
                (locked > 0 ? $"(그 가운데 고칠 수 없는 부재 {locked}개)" : "") + ".\n" +
                "지금 값으로 산출서를 만들까요?\n(만들면 엑셀 «부재 목록»의 판정에 «" + CalcPlan.ShapeChangedNote + "»을 남깁니다. " +
                "[아니요]는 멈춥니다 — [← 이전 단계]로 ① 에 가서 «확인 필요»를 고르면 그 부재들이 나옵니다.)",
                Title, MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
            session.Note($"완료\t모양 바뀜 미확인 {changed}\t{(go == MessageBoxResult.Yes ? "계속" : "돌아감")}");
            if (go != MessageBoxResult.Yes) return false;
        }
        FormulaFollow? follow = null;
        string problem = view.RunLocked(() => follow = FollowWithFormulas(doc, session, count => AskCompute(window, count)));
        done.Formulas = follow ?? new FormulaFollow { Problem = problem };
        if (problem.Length > 0 && done.Formulas.Problem.Length == 0) done.Formulas.Problem = problem;
        if (done.Formulas.Plan.MayHaveCommitted) session.Committed();
        if (done.Formulas.Problem.Length > 0)
        {
            view.ShowMessage("산출식을 채우지 못했습니다(③ 으로 넘어가지 않았습니다): " + done.Formulas.Problem, true);
            return false;
        }
        if (done.Formulas.Plan.Declined)
        {
            view.ShowMessage("산출식을 지금 만들지 않기로 해 ③ 으로 넘어가지 않았습니다.", true);
            return false;
        }
        if (done.Formulas.RolledBack)
        {
            view.ShowMessage($"산출식이 모델에 저장되지 않았습니다(Revit 이 변경을 되돌림: {done.Formulas.Plan.Status}) — ③ 으로 넘어가지 않았습니다.", true);
            return false;
        }

        // ② 식을 못 만든 부재: 하나씩 띄운다(창의 3D 에서 그 부재를 보여 준다).
        var ask = done.Formulas.Ask;
        if (ask.Count > 0)
        {
            done.Review = ExceptionReview.Run(window, doc, ask, dark, id => view.FocusMember(id.Value > 0 ? id.Value : 0));
            for (int i = 0; i < done.Review.Commits; i++) session.Committed();
            session.Note($"완료\t산출식 확인 창: 항목 {done.Review.Items}(부재 {done.Review.Elements}) · 적음 {done.Review.Saved} · 커밋 {done.Review.Commits} · 못 적음 {done.Review.Failed.Count}");
        }

        // ②-1 부재 이름(R53b — SB01 · WA01 …): 산출식 다음, 거푸집 시트 · 엑셀 전에(두 시트 줄의 부호가 이름이다). 못 붙여도 엑셀은 이어서 만든다
        // (이름 없는 부재는 전처럼 마크 · 유형 이름 — 알림 창에 까닭). 바뀐 부재만 적는다 — 다시 [완료]해도 그대로인 이름은 건드리지 않는다.
        MemberNamer.Outcome? named = null;
        problem = view.RunLocked(() => named = MemberNamer.Assign(doc, editor));
        done.Names = named;
        done.NamesProblem = problem.Length > 0 ? problem : named?.Problem ?? "";
        if (named?.ParametersEnsured == true || problem.Length > 0) session.Committed();   // 오류로 끝났어도 칸 만들기는 커밋됐을 수 있다
        if (named?.Committed == true) session.Committed();
        session.Note(named != null ? MemberNamer.Note(named) : "완료\t부재 이름\t오류: " + problem);
        // 이름 단계가 오류로 끝났으면 거푸집 줄도 모델에 남은 이름(앞선 [완료]의 것)으로 — 콘크리트 줄은 모델에서 읽는다(두 시트가 같은 이름 · revit-verify R53b L).
        var names = named?.Names;
        if (names == null)
        {
            try { names = MemberNamer.ReadNames(doc, editor.Members.Select(m => m.Id)); }
            catch (Exception ex) { session.Note("완료\t부재 이름 다시 읽기 오류\t" + ex.Message); }
        }

        // ③ 거푸집 속성(② 에서 잰 것 — [완료]는 ② 에만 있어 늘 있다). 적지 못해도 엑셀은 이어서 만든다(알림 창에 까닭을 적는다).
        if (view.Board is { } board)
        {
            (done.Dropped, done.DroppedArea, done.LostDrops, done.ChangedDrops) = (board.DroppedCount, board.DroppedArea, board.LostDrops.Count, board.DropCheck.Changed);
            (done.Moved, done.MovedArea, done.Orphans) = (board.MovedCount, board.MovedArea, board.OrphanCount);
            if (done.Dropped + done.LostDrops + done.Moved + done.Orphans > 0)
                session.Note(FormattableString.Invariant($"완료\t사람이 지운 거푸집 판 {done.Dropped}장({done.DroppedArea:0.###} ㎡) · 옮긴 판 {done.Moved}장({done.MovedArea:0.###} ㎡) · 옮길 줄이 없어진 판 {done.Orphans}장 · 모양이 바뀌어 다시 센 판 {done.LostDrops}장 · 모양이 바뀐 판 {done.ChangedDrops}장"));
            FormworkWriter.Outcome? written = null;
            problem = view.RunLocked(() => written = FormworkWriter.Write(doc, board));
            done.Formwork = written;
            if (written?.ParametersEnsured == true) session.Committed();
            if (written?.Committed == true) session.Committed();
            done.FormworkProblem = problem.Length > 0 ? problem
                : written == null ? "적지 못함"
                : !written.Committed ? written.Failed.FirstOrDefault() ?? "되돌려짐"
                : "";
            if (written != null)
                session.Note($"완료\t거푸집 속성\t부재 {written.Elements} · 넓이 {written.Total:0.##} ㎡ · {written.Status} · 못 적음 {written.Failed.Count} · 되읽기 다름 {written.ReadBackDiffers}" +
                             FormattableString.Invariant($" · 전 판의 7m초과 값을 7m이하로 옮김 {written.StaleCleared}({written.StaleArea:0.###} ㎡") +
                             (written.StaleMoved.Count > 0 ? " · " + string.Join(", ", written.StaleMoved.Take(12)) + (written.StaleMoved.Count > 12 ? " …" : "") : "") +
                             $") · 못 옮김 {written.StaleLeft.Count}" +
                             (written.StaleLeft.Count > 0 ? "(Id " + string.Join(",", written.StaleLeft.Take(12)) + (written.StaleLeft.Count > 12 ? " …" : "") + ")" : "") +
                             (problem.Length > 0 ? " · 오류: " + problem : ""));
            try { done.Sheet = SheetData(board, editor, names); }
            catch (Exception ex) { session.Note("완료\t거푸집 시트를 짓지 못함\t" + ex); done.FormworkProblem += (done.FormworkProblem.Length > 0 ? " · " : "") + "거푸집 시트를 짓지 못함: " + ex.Message; }
        }

        // ④ 산출근거 엑셀.
        CalcSheetExporter.Collected? read = null;
        problem = view.RunLocked(() =>
        {
            read = CalcSheetExporter.Collect(doc, DimensionExtractor.CollectApplicable(doc));
            // [수량산출] 시작의 빠른 모델 검증(R54). ① 에서 «겹친 자리»를 바꿨으면 기준이 달라졌으므로 지금 순위로 다시 한다(revit-verify R54 설치 전 M3 —
            // ① 은 바꾼 목록을 바로 모델에 적어 둔다). 바꾸지 않았으면 다시 재지 않는다(① · ② 는 모양을 안 바꾼다).
            string ranksNow = RankSignature(editor);
            if (ranksNow != session.ModelCheckRanks)
            {
                session.Note("모델 검증(빠른)\t① 에서 겹친 자리를 바꿈 — 지금 순위로 다시 검사");
                session.ModelCheck = QuickModelCheck(doc, session);
                session.ModelCheckRanks = ranksNow;
            }
            done.Overlap = session.ModelCheck;
        });
        if (read == null)
        {
            view.ShowMessage("산출근거를 모으지 못했습니다: " + problem, true);
            return false;
        }
        done.Plan = read.Plan;
        done.Read = read;
        done.Prepared = true;
        // ③ 콘크리트 줄의 숫자(R61b): 창을 띄운 채 쓰면 창은 Revit API 를 부를 수 없다 — 지금(요청 안) 부재마다 다시 풀어 둔다(R62 · 약 10 ms/부재 · 한도 넘으면 나머지는 가운데 식).
        view.RunLocked(() => done.Geometry = ConcreteGeometries(doc, read.Plan, session));
        session.Note($"③ 산출서 확인\t들어감\t콘크리트 줄 {read.Plan.Rows.Count} · 실은 부재 {read.Plan.Listed} · 싣지 못한 부재 {read.Plan.Missing} · 거푸집 줄 {done.Sheet?.Lines.Count ?? 0}");
        return true;
    }

    private const int GeometryBudgetMs = 6000;

    // ③ 콘크리트 숫자의 부재 도형(R61b — 산출식 채우기와 같은 입력으로 다시 풂). 시간 한도를 넘으면 그만 — 그 부재는 가운데 식.
    private static Dictionary<long, FormulaGeometry?> ConcreteGeometries(Document doc, CalcPlan plan, Session session)
    {
        var found = new Dictionary<long, FormulaGeometry?>();
        var watch = Stopwatch.StartNew();
        int failed = 0, skipped = 0;
        foreach (long id in plan.Rows.SelectMany(r => r.Ids).Distinct())
        {
            if (watch.ElapsedMilliseconds > GeometryBudgetMs) { skipped++; continue; }
            try { found[id] = doc.GetElement(new ElementId(id)) is { } el ? FormulaAutoFill.Explain(el) : null; }
            catch (Exception ex) { failed++; if (failed <= 3) session.Note($"③ 콘크리트 숫자	{id}	오류	{ex.Message}"); }
        }
        session.Note($"③ 콘크리트 숫자 미리 풂	부재 {found.Count} · 오류 {failed} · 시간 넘어 못 함 {skipped} · {watch.ElapsedMilliseconds} ms");
        return found;
    }

    // [완료](③ — R59): Prepare 가 모은 것으로 거푸집 객체(저장 창보다 먼저 — JACK 2026-10-16 «저장 취소해도 만들기») → 엑셀 저장 위치 → 쓸 수 있는지 →
    // 엑셀을 지어 둔다(파일은 창을 닫아 그룹이 모델에 남은 뒤에 쓴다 — revit R42 A1). 그만두면 ③ 에 머문다.
    private static void Finish(Window window, PlacePartView view, Document doc, Session session, Plates plates, Completion done)
    {
        session.Note("완료\t시작");
        // 거푸집 객체(R44): 시트를 짓지 못했으면 만들지 않는다(판은 엑셀 줄과 같은 단위다). 못 만들어도 엑셀은 이어서 만든다(알림 창에 까닭).
        if (view.Board is { } board && done.Sheet is { } sheet) BuildPlates(doc, view, session, board, sheet, plates, done);
        if (done.Read is not { } read || done.Plan is not { } plan) return;
        if (plan.Rows.Count == 0)
        {
            view.ShowMessage("산출근거에 실을 식이 하나도 없습니다 — 엑셀은 저장하지 않았습니다.", true);
            return;
        }
        string name = SafeName(doc.Title);
        string initDir = !string.IsNullOrEmpty(doc.PathName) && Directory.Exists(Path.GetDirectoryName(doc.PathName))
            ? Path.GetDirectoryName(doc.PathName)!
            : Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = "산출근거 엑셀 저장",
            FileName = name + "_산출근거.xlsx",
            DefaultExt = ".xlsx",
            Filter = "엑셀 통합 문서 (*.xlsx)|*.xlsx",
            InitialDirectory = initDir,
            OverwritePrompt = true,
        };
        if (dlg.ShowDialog(window) != true)
        {
            session.Note("완료\t엑셀 저장을 그만둠");
            view.ShowMessage("엑셀을 저장하지 않았습니다 — [완료]를 다시 누르면 저장 위치를 다시 묻습니다(채운 산출식은 그대로입니다).", true);
            return;
        }
        string busy = Writable(dlg.FileName);
        if (busy.Length > 0)
        {
            session.Note("완료\t그 파일을 쓸 수 없음\t" + busy);
            view.ShowMessage("그 파일에 쓸 수 없습니다(같은 파일이 엑셀에 열려 있으면 닫고 다시 [완료]를 눌러 주세요): " + busy, true);
            return;
        }
        string note = $"{DateTime.Now:yyyy-MM-dd HH:mm} 내보냄" +
                      (done.Overlap.Length > 0 ? " · ⚠ " + done.Overlap : "") +
                      (done.Dropped > 0 ? $" · 사람이 뺀 거푸집 판 {done.Dropped}장({done.DroppedArea.ToString("#,##0.00", CultureInfo.InvariantCulture)} ㎡) 제외" : "") +
                      (done.LostDrops > 0 ? $" · ⚠ 모양이 바뀌어 다시 센 뺀 판 {done.LostDrops}장" : "") +
                      (done.ChangedDrops > 0 ? $" · ⚠ 모양이 바뀐 뺀 판 {done.ChangedDrops}장" : "") +
                      (read.Demolished > 0 ? $" · ⚠ 철거로 표시된 부재 {read.Demolished}개 포함" : "") +
                      (read.UnknownMaterial.Count > 0 ? $" · 재료 미상 부재 {read.UnknownMaterial.Count}개 포함" : "");
        done.Book = CalcSheet.Build(doc.Title, plan.Rows, plan.Elements, note, done.Sheet);
        done.ExcelTarget = dlg.FileName;
        session.Note($"완료\t엑셀 지음(창을 닫고 그룹이 남으면 씀)\t{dlg.FileName}\t줄 {plan.Rows.Count} · 실은 부재 {plan.Listed} · 싣지 못한 부재 {plan.Missing}");
    }

    // 이 창에서 지은 거푸집 객체 — [완료]를 다시 눌러도(엑셀 저장을 그만둔 뒤 등) 같은 판을 통째로 다시 짓지 않게 한다(큰 모델은 오래 걸린다).
    private sealed class Plates
    {
        public FormworkBoard? Board;                  // 판을 지을 때의 거푸집 판(다시 재면 새 판이라 다시 짓는다)
        public string Key = "";                       // 그때의 줄 조각(KeyOf)
        public FormworkObjects.Outcome? Built;        // 그때 지은 결과(커밋된 것만)
    }

    // 거푸집 객체를 짓는다(R44): 전 판은 지우고 새로 — 요청 «DH 거푸집 객체»의 묶음 안이라 Revit 실행 취소 한 줄. 이 창에서 이미 지은 판과 거푸집 판 · 줄 조각
    // (종류 · 부위 · 중분류 · 안팎 · 유형 · 식 · 넓이 · 원 부재)이 모두 같으면 다시 짓지 않고 그 결과를 쓴다 — 판에 적는 것이 하나도 바뀌지 않는다.
    // 대응표를 고치거나(줄 조각이 바뀜) ① 에서 부위를 바꿔 다시 재면(새 거푸집 판) 다시 짓는다.
    private static void BuildPlates(Document doc, PlacePartView view, Session session, FormworkBoard board, FormworkSheetData sheet, Plates plates, Completion done)
    {
        string key = KeyOf(sheet);
        if (plates.Built is { Committed: true } built && ReferenceEquals(plates.Board, board) && plates.Key == key)
        {
            done.Objects = built;
            session.Note($"완료\t거푸집 객체\t그대로 둠(이 창에서 지은 판 {built.Created}개 — 거푸집 판과 줄 조각이 같다)");
            return;
        }
        FormworkObjects.Outcome? objects = null;
        string why = view.RunLocked(() => objects = FormworkObjects.Rebuild(doc, sheet.Plates));   // R45: 판 = 줄 조각을 높이 구간을 넘어 이은 것
        done.Objects = objects;
        done.ObjectsProblem = why.Length > 0 ? why : objects?.Blocked ?? "";
        if (objects?.ParametersEnsured == true || why.Length > 0) session.Committed();   // 오류로 끝났어도 칸 만들기는 커밋됐을 수 있다
        if (objects?.Committed == true)
        {
            session.Committed();
            plates.Board = board;
            plates.Key = key;
            plates.Built = objects;
        }
        else if (plates.Built is { Committed: true } before)
        {
            // 다시 짓기가 되돌려졌거나 막혔다 — 지우기도 함께 되돌아가 전에 지은 판이 그대로 있다(열쇠가 달라 다음 [완료]에서 다시 짓는다 — revit-verify R44b L-c).
            done.ObjectsProblem = (done.ObjectsProblem.Length > 0 ? done.ObjectsProblem : objects?.Error is { Length: > 0 } error ? error : "되돌려짐") +
                                  $" (이 창에서 전에 지은 판 {before.Created}개는 그대로 남음)";
        }
        session.Note(ObjectsNote(objects, why, sheet));
    }

    // 줄 조각의 열쇠 — 판에 적는 값(종류 · 부위 · 중분류 · 안팎 · 유형 · 식 · 넓이 · 원 부재)과 판이 덮는 면(부재 · 입체 · 면 · 조각 차례)이 같으면 같다
    // (넓이까지 같은 두 면이 대응표 고침으로 세는 쪽이 서로 바뀌어도 다시 짓게 — revit-verify R44b L-b).
    private static string KeyOf(FormworkSheetData sheet) =>
        string.Join("\n", sheet.Units.Select(u => string.Join("|", u.Section, u.Part, u.Sub, u.Zone, u.Side, u.Mark, u.Tag, u.Formula,
            u.Area.ToString("R", CultureInfo.InvariantCulture), string.Join(",", u.Ids),
            string.Join(";", u.Members.Select(m => $"{m.Result.Element.Id}:{m.Face.Solid}:{m.Face.Face}:{m.Face.Patches.IndexOf(m.Patch)}")))));

    // 거푸집 객체의 일지 줄 — 첫 실행에서 «판 수 = 줄 조각 수 = Σ 줄 조각(줄마다 Count × 칸) · 못 만든 판 0 · 되읽기 같음»을 바로 가르게.
    // 슬래브 밑면 한 줄(R46)은 줄 하나에 칸이 여럿이다 — 그 줄 수와 든 칸 수를 따로 적는다.
    private static string ObjectsNote(FormworkObjects.Outcome? o, string problem, FormworkSheetData sheet)
    {
        var slabLines = sheet.Lines.Where(l => l.Cells > 1).ToList();
        var merged = sheet.Lines.Where(l => l.Terms > 1).ToList();   // 공통 수로 묶은 줄(R60)
        string head = $"완료\t거푸집 객체\t시트 줄 {sheet.Lines.Count}(× n 합 {sheet.Lines.Sum(l => l.Count)} · 슬래브 밑면 한 줄 {slabLines.Count}(칸 {slabLines.Sum(l => l.Units.Count)})" +
                      $" · 묶은 줄 {merged.Count}(원래 줄 {merged.Sum(l => l.Terms)}))" +
                      $" · 줄 조각 {sheet.Units.Count}(줄에 든 조각 {sheet.Lines.Sum(l => l.Units.Count)}) · 판 {sheet.Plates.Count}" +
                      (sheet.Plates.Count != sheet.Units.Count ? $"(구간을 넘어 이은 판 {sheet.Plates.Count(p => p.Parts.Count > 1)})" : "") +
                      (o != null && o.Parts != o.Units ? $" · 판에 든 줄 조각 {o.Parts}" : "") +
                      (o is { ParamTypes.Length: > 0 } ? " · ★ " + o.ParamTypes : "");
        if (o == null) return head + " · 오류: " + problem;
        if (o.Blocked.Length > 0) return head + " · 하지 않음: " + o.Blocked;
        return head + $" · 지움 {o.Deleted} · 만듦 {o.Created}(테두리 {o.Solids} · 가는 곳 뗌 {o.Opened} · 가는 띠 넓힘 {o.Widened} · 조각마다 {o.PerMember} · 면만 {o.Meshes})" +
               $" · 못 만듦 {o.Failed.Count}" + (o.Failed.Count > 0 ? "(" + string.Join(" / ", o.Failed.Take(5)) + (o.Failed.Count > 5 ? " …" : "") + ")" : "") +
               (o.Why.Count > 0 ? " · 테두리를 그대로 못 쓴 까닭: " + string.Join(" · ", o.Why.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key} {kv.Value}")) +
                                  " (예: " + string.Join(" / ", o.WhyExamples) + ")" : "") +
               (o.Dropped > 0 ? FormattableString.Invariant($" · 1 mm 보다 가늘어 판에서 빠진 넓이 {o.Dropped:0.######} ㎡") : "") +
               $" · 칸 못 적음 {o.MissingParams} · 일람표 {o.Schedule} · {o.Status}" + (o.Error.Length > 0 ? "(" + o.Error + ")" : "") +
               $" · 되읽기 판 {o.ReadBack} · 칸 다름 {o.ReadBackDiffers} · 자리 어긋남 {o.Misplaced} · 방 경계 끔 {o.RoomBoundingOff}" +
               (o.ReadBackProblem.Length > 0 ? " · 되읽기 오류: " + o.ReadBackProblem : "") +
               $" · Revit 에서 지운 판 {(o.Missing < 0 ? "모름" : o.Missing.ToString(CultureInfo.InvariantCulture))} · 기록 못 붙임 {o.Unstamped}" + (o.ListProblem.Length > 0 ? " · 판 목록 못 적음: " + o.ListProblem : "") +
               (o.MaterialProblem.Length > 0 ? " · 재료 못 만듦: " + o.MaterialProblem : "") +
               (o.SubcategoryProblem.Length > 0 ? " · 하위 범주 못 만듦: " + o.SubcategoryProblem : "") +
               (o.TypeProblem.Length > 0 ? " · 유형 «거푸집» 못 만듦: " + o.TypeProblem : "") + (o.Untyped > 0 ? $" · 유형을 못 붙인 판 {o.Untyped}" : "") + $" · {o.Ms} ms";
    }

    // 대상에서 빠진 보이드에 이 애드인이 전에 적은 값(칸 · 숨은 기록)을 지운다 — 값이 있고 고칠 수 있는 보이드만 · 트랜잭션 하나. 지운 보이드 Id 를 돌려준다.
    private static List<long> WipeVoids(Document doc, Session session)
    {
        var wiped = new List<long>();
        var voids = DimensionExtractor.TargetVoids(doc);
        if (voids.Count == 0) return wiped;
        var dirty = voids.Select(id => doc.GetElement(new ElementId(id))).OfType<Element>()
            .Where(el => FormulaAutoFill.WhyNotEditable(doc, el) == null && MemberNamer.HasValues(el)).ToList();
        if (dirty.Count == 0) return wiped;
        TransactionStatus status;
        using (var tx = new Transaction(doc, "DH 보이드 값 지움"))
        {
            tx.Start();
            // 오류는 창을 띄우지 않고 이 트랜잭션만 되돌린다(«요소 삭제»를 눌러 보이드가 지워지지 않게 — 설치 전 검토 높음).
            var options = tx.GetFailureHandlingOptions();
            options.SetFailuresPreprocessor(new OverlapChecker.QuietFailures());
            options.SetClearAfterRollback(true);
            tx.SetFailureHandlingOptions(options);
            foreach (var el in dirty)
                if (MemberNamer.Wipe(el)) wiped.Add(el.Id.Value);
            status = tx.Commit();
        }
        if (status == TransactionStatus.Committed) session.Committed();
        else wiped.Clear();
        session.Note($"완료\t보이드 값 지움 {wiped.Count} (Id {string.Join(",", wiped.Take(12))}) · 보이드 {voids.Count} · {status}");
        return wiped;
    }

    // 그 파일을 지금 쓸 수 있는가("" = 쓸 수 있음, 아니면 까닭) — 엑셀에 열려 있는 파일 · 쓸 수 없는 폴더를 창을 닫기 전에 가린다.
    // 없던 파일이면 빈 파일을 만들어 보고 지운다.
    private static string Writable(string path)
    {
        try
        {
            bool existed = File.Exists(path);
            using (new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None)) { }
            if (!existed) File.Delete(path);
            return "";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return ex.Message; }
    }

    // [완료]의 엑셀을 쓴다 — 창을 닫아 그룹이 모델에 남은 뒤에만 부른다. 그사이 파일이 잠기는 드문 경우에는 다른 위치를 묻는다(그만두면 쓰지 않음).
    private static void SaveExcel(Completion done, Session session)
    {
        string target = done.ExcelTarget;
        while (done.Book != null && target.Length > 0)
        {
            try
            {
                done.Book.Save(target);
                done.ExcelPath = target;
                done.ExcelProblem = "";
                session.Note("완료\t엑셀 저장\t" + target);
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                done.ExcelProblem = ex.Message;
                session.Note("완료\t엑셀을 쓰지 못함\t" + target + "\t" + ex.Message);
                var ask = Dialogs.New(Title);
                ask.MainInstruction = "엑셀 파일을 쓰지 못했습니다";
                ask.MainContent = ex.Message + "\n\n" + target + "\n\n모델에 넣은 것(부위·중분류 · 산출식 · 거푸집 속성)은 남았습니다.";
                ask.AllowCancellation = true;
                ask.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "다른 위치에 저장");
                ask.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "저장하지 않기", "[수량산출]의 [완료]를 다시 누르면 다시 만듭니다.");
                if (ask.Show() != TaskDialogResult.CommandLink1) return;
                var dlg = new Microsoft.Win32.SaveFileDialog
                {
                    Title = "산출근거 엑셀 저장",
                    FileName = Path.GetFileName(target),
                    DefaultExt = ".xlsx",
                    Filter = "엑셀 통합 문서 (*.xlsx)|*.xlsx",
                    InitialDirectory = Path.GetDirectoryName(target) ?? "",
                    OverwritePrompt = true,
                };
                if (dlg.ShowDialog() != true) return;
                target = dlg.FileName;
            }
        }
    }

    // 거푸집 시트에 실을 것: 부위 · 중분류는 창이 알고 있는 지금 값(부위가 비었으면 산출서가 붙이는 자동 이름), 같은 거푸집 차례 안의 부위 차례는 「부위」 목록의 차례.
    // 중분류는 줄에 적지 않는다(R47 — 2D 거푸집 줄에는 위치가 없다) · 밑면 잇기와 거푸집 객체의 중분류 칸에만 쓴다.
    // names = [완료]가 붙이고 되읽은 부재 이름(R53b — 줄의 부호 · null 이면 전처럼 마크 · 유형 이름).
    private static FormworkSheetData SheetData(FormworkBoard board, PickEditor editor, IReadOnlyDictionary<long, string>? names = null)
    {
        var (placeOf, partOrder) = Places(board.Results, editor);
        return FormworkSheet.Build(board.Results, board.Options, placeOf, partOrder, names == null ? null : id => names.GetValueOrDefault(id, ""));
    }

    // 거푸집 시트 · ② 의 판(R48 — 3D 의 판이 엑셀 줄과 같은 한 장이게 같은 함수를 쓴다): 부재 Id → (부위 — 비었으면 산출서의 자동 이름, 중분류)는
    // 창이 알고 있는 지금 값, 부위 이름 → 「부위」 목록의 차례.
    private static (Func<long, (string part, string zone)> placeOf, Func<string, int> partOrder) Places(IReadOnlyList<ElementFormwork> results, PickEditor editor)
    {
        var parts = editor.Parts.Select(p => p.Name).ToList();
        var elements = new Dictionary<long, FormworkElement>();
        foreach (var r in results) elements.TryAdd(r.Element.Id, r.Element);
        (string part, string zone) PlaceOf(long id)
        {
            if (editor.Find(id) is { } m) return (m.Value.Part.Length > 0 ? m.Value.Part : m.AutoPart, m.Value.Zone);
            var el = elements.GetValueOrDefault(id);
            return (el == null ? "" : CalcPlan.KindName(el.Kind, el.Type), el?.Zone ?? "");
        }
        int Order(string name) { int at = parts.IndexOf(name); return at < 0 ? int.MaxValue : at; }
        return (PlaceOf, Order);
    }

    // 식을 새로 만들 부재가 많으면(식이 하나도 없는 큰 모델) 오래 걸릴 수 있어 먼저 묻는다 — 수량산출 창 위에 뜬다.
    private static bool AskCompute(Window window, int count)
    {
        if (count < AskBeforeCompute) return true;
        var cursor = Mouse.OverrideCursor;
        try
        {
            Mouse.OverrideCursor = null;
            return MessageBox.Show(window,
                $"부재 {count}개의 산출식을 지금 만들까요?\n부재가 많아 시간이 걸릴 수 있습니다(끝날 때까지 창이 멈춘 것처럼 보입니다).",
                Title, MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.Yes) == MessageBoxResult.Yes;
        }
        finally { Mouse.OverrideCursor = cursor; }
    }

    // 부위 목록의 «겹친 자리» 순위를 한 줄로(부위:순위 — 빠른 검사를 다시 할지 가른다).
    private static string RankSignature(PickEditor editor) =>
        string.Join("|", editor.Parts.Select(p => p.Name + ":" + PartCatalog.RankOf(p).ToString(CultureInfo.InvariantCulture)));

    // [수량산출] 시작의 빠른 모델 검증(R54 — 인터뷰 «먼저 빠르게 검사 · 알림만» · revit-verify 계획 H4): 겹침(15 초 한도) + 결합 방향(트랜잭션 없이 닿기만 한
    // 결합을 거른 수). 모델은 바꾸지 않는다. 걸려도 계속한다 — ① 위 띠의 칩과 ④ 엑셀 «내보냄» 메모가 같은 글을 쓴다(① · ② 는 모양을 안 바꾼다 — 다시 재지 않음).
    // 결합 방향을 Revit 의 결합으로 못 가르는 쌍(잘린 쪽이 벽 · 바닥)은 «최대»로 센다. 돌려주는 값 = «모델 검증: 겹침오류 n쌍 · 작성오류 m쌍»("" = 걸린 것 없음).
    private static string QuickModelCheck(Document doc, Session session)
    {
        var watch = Stopwatch.StartNew();
        try
        {
            var targets = DimensionExtractor.CollectApplicable(doc)
                .Where(id => doc.GetElement(id) is Element el && ConcreteFilter.Classify(doc, el).Kind != ConcreteFilter.Kind.NotConcrete).ToList();
            var found = OverlapChecker.Find(doc, targets, TimeSpan.FromSeconds(15));
            long findMs = watch.ElapsedMilliseconds;
            var (sure, most) = OverlapChecker.QuickReversed(doc, targets, found);
            var parts = new List<string>();
            if (found.Pairs.Count > 0)
                parts.Add($"겹침오류 {found.Pairs.Count}쌍{(found.TimedOut ? " 이상" : "")}(중복 {FormulaDecomposer.RoundQuantity(found.TotalM3).ToString("#,##0.00", CultureInfo.InvariantCulture)} m³)");
            else if (found.TimedOut) parts.Add("겹침오류는 끝까지 못 봄(15초)");
            if (most > 0) parts.Add(most > sure ? $"작성오류 {sure}쌍(최대 {most}쌍)" : $"작성오류 {sure}쌍");
            session.Note($"모델 검증(빠른)\t대상 {targets.Count} · 겹침 {found.Pairs.Count}{(found.TimedOut ? "(시간 한도)" : "")} · 결합 방향 {sure}~{most} · 닿기만 해서 뺌 {found.Touching}" +
                         $" · 사람이 정함 {found.Reversed.Count(r => r.Human)} · 참고 {found.Reversed.Count(r => r.SameRank && !r.Human)}\t찾기 {findMs} ms · 모두 {watch.ElapsedMilliseconds} ms");
            foreach (var r in found.Reversed.Where(r => r.Counts).Take(30)) session.Note("  " + OverlapChecker.ReversedLine(r));
            // 창의 딱지(R63b): 겹침 · 결합 순서 — 누르면 그 부재.
            var issues = new List<PlacePartView.Issue>();
            if (found.Pairs.Count > 0)
                issues.Add(new PlacePartView.Issue("overlap", "겹침", found.Pairs.Count, found.Pairs.SelectMany(p => new[] { p.Winner.Value, p.Loser.Value }).Distinct().ToList(),
                    $"콘크리트가 겹친 자리{(found.TimedOut ? "(15초 안에 찾은 것만)" : "")} — 겹친 {FormulaDecomposer.RoundQuantity(found.TotalM3).ToString("#,##0.00", CultureInfo.InvariantCulture)} m³ 를 두 번 셉니다.\n고치기: 리본 [모델 검증]."));
            session.ModelCheckJoins = found.Reversed.Where(r => r.Counts).Select(r => (r.Cutter.Value, r.Keep.Value)).Distinct().ToList();
            if (session.ModelCheckJoins.Count > 0) issues.Add(JoinsIssue(session.ModelCheckJoins));
            session.ModelCheckIssues = issues;
            session.ModelCheckNote = found.Pairs.Count == 0 && found.TimedOut ? "겹침은 끝까지 못 봄(15초)" : "";
            return parts.Count > 0 ? "모델 검증: " + string.Join(" · ", parts) : "";
        }
        catch (Exception ex)
        {
            session.Note("모델 검증(빠른)\t오류\t" + ex);
            session.ModelCheckIssues = new();
            session.ModelCheckJoins = new();
            session.ModelCheckNote = "모델 검증을 하지 못함";
            return "모델 검증을 하지 못함(" + ex.Message + ")";
        }
    }

    // 결합 순서가 시공 순서와 반대인 쌍(자르는 부재 → 잘린 부재)의 딱지(R63b — 열 때 검사 · ② 에서 본 «나중 부재가 깎은 곳»).
    private static PlacePartView.Issue JoinsIssue(IReadOnlyCollection<(long cutter, long cut)> pairs) =>
        new("joins", "결합 순서", pairs.Count, pairs.SelectMany(p => new[] { p.cutter, p.cut }).Distinct().ToList(),
            "Revit 결합이 시공 순서와 반대인 자리입니다(예: 헌치가 보를 깎음) — 거푸집이 조금 더 셀 수 있습니다.\n고치기: 리본 [모델 검증] → [모두 기준대로] 뒤 다시 [수량산출].");


    // 산출식을 돌린다(필요한 부재만). 대상 = 모델의 대상 부재 전부(Revit 의 선택은 보지 않는다 — 산출은 모델 전체의 일이다).
    // 여기서 나는 오류는 받아 적는다: 부위·중분류에 넣은 값은 이미 모델에 남아 있다.
    private static FormulaFollow FollowWithFormulas(Document doc, Session session, Func<int, bool> beforeCompute)
    {
        var follow = new FormulaFollow();
        var cursor = Mouse.OverrideCursor;
        try
        {
            try { Mouse.OverrideCursor = Cursors.Wait; } catch { }
            follow.Plan = FormulaAutoFill.PlanRefresh(doc, DimensionExtractor.CollectApplicable(doc), beforeCompute);
            if (!follow.Plan.Declined) FormulaAutoFill.ApplyRefresh(doc, follow.Plan);
        }
        catch (Exception ex)
        {
            follow.Problem = ex.Message;
            session.Note("산출식 이어서 — 오류\t" + ex);
        }
        finally
        {
            try { Mouse.OverrideCursor = cursor; } catch { }
        }

        // 결과표와 일지 줄을 적다 오류가 나도 follow(무엇을 커밋했는가)는 그대로 돌려준다 — 잃으면 명령이 Cancelled 를 돌려줘 방금 적은 식이 되돌려진다.
        try { NoteFollow(doc, follow, session); }
        catch (Exception ex) { session.Note("산출식 이어서 — 결과를 적다 오류: " + ex.Message); }
        return follow;
    }

    // 이어서 돈 산출식의 결과표와 일지 줄.
    private static void NoteFollow(Document doc, FormulaFollow follow, Session session)
    {
        var plan = follow.Plan;
        // 결과표는 적은 것이나 사람이 확인할 것이 있을 때만 쓴다(닫을 때마다 사본이 쌓이지 않게).
        if (plan.Wrote || follow.Exceptions.Count > 0)
        {
            try { follow.Table = FormulaAutoFill.WriteLog(doc, plan.All, plan.Wrote ? plan.Status : TransactionStatus.Committed); }
            catch (Exception ex) { follow.Table = "(쓰지 못했습니다 — 결과표가 엑셀에 열려 있으면 닫아 주세요: " + ex.Message + ")"; }
        }
        session.Note($"산출식 이어서\t대상 {follow.Total} · 그대로 {follow.Kept}(검산 칸만 고침 {follow.CellsFixed} · 체적이 달라졌지만 ±1% 안 {follow.Drifted} · 산출근거에 못 싣는 식 {follow.NotExportable.Count})" +
                     $" · 다시 만들기로 한 것 {plan.Recompute} → 새로 적음 {follow.NewFormula} · 바꿔 적음 {follow.Replaced} · 같은 식 {follow.SameFormula} · 예외 {follow.Exceptions.Count} · 대상 아님 {follow.NotTarget}" +
                     $" · 다시 계산 필요였던 것 {follow.WasStale} → 식이 바뀐 것 {follow.ReplacedStale} · 표시를 남긴 것 {follow.StaleLeft}" +
                     $" · 적을 부재 {plan.ToWrite.Count} · 적지 않음(그룹 등) {plan.NotEditable} · 칸 새로 만듦 {(plan.ParametersEnsured ? "O" : "X")}" +
                     $" · 트랜잭션 {(plan.Wrote ? "O(" + plan.Status + ")" : "X")} · 되읽기 다름 {follow.BadReadBack} · 적지 못함 {follow.NotWritten} · 판정만 고침 {follow.VerdictOnly} · 나중에 하기로 함 {(plan.Declined ? "O" : "X")}" +
                     $" · 계획 {plan.PlanMs} ms / 적기 {plan.WriteMs} ms" + (follow.Problem.Length > 0 ? " · 오류: " + follow.Problem : ""));
    }

    // 닫는 알림 창에 넣는 «산출식» 단락.
    private static void AppendFormulaReport(StringBuilder text, FormulaFollow follow, bool placesKept)
    {
        var plan = follow.Plan;
        text.AppendLine();
        if (follow.Problem.Length > 0)
        {
            text.AppendLine("산출식을 이어서 하지 못했습니다: " + follow.Problem);
            text.AppendLine(plan.MayHaveCommitted ? "  (일부는 모델에 적혔을 수 있습니다 — [수량산출] ② 의 [다음 단계]를 다시 눌러 맞춰 주세요.)"
                                                  : "  ([수량산출] ② 의 [다음 단계]를 다시 눌러 주세요.)");
            return;
        }
        if (plan.Declined)
        {
            text.AppendLine($"산출식은 나중에 하기로 했습니다(식을 새로 만들 부재 {plan.Recompute}개) — [수량산출] ② 의 [다음 단계]를 다시 눌러 주세요.");
            return;
        }
        if (follow.RolledBack)
        {
            text.AppendLine($"★ 산출식이 모델에 저장되지 않았습니다(Revit 이 변경을 되돌림: {plan.Status}). [수량산출] ② 의 [다음 단계]를 다시 눌러 주세요.");
            return;
        }
        text.AppendLine("산출식(필요한 부재만):");
        if (follow.NewFormula > 0) text.AppendLine($"  • 새로 적음: {follow.NewFormula}개(식이 없던 부재)");
        if (follow.Replaced > 0)
            text.AppendLine($"  • 바꿔 적음: {follow.Replaced}개(식이 지금 모델과 맞지 않던 부재 {follow.Replaced - follow.ReplacedStale} · 결합으로 '다시 계산 필요'였던 부재 {follow.ReplacedStale}) — 전의 식은 결과표에 있습니다");
        if (follow.VerdictOnly > 0) text.AppendLine($"  • 판정만 고침: {follow.VerdictOnly}개(같은 식이 다시 나온 부재 — 결합에서 남는 쪽 등)");
        if (follow.CellsFixed > 0) text.AppendLine($"  • 검산 칸만 고침: {follow.CellsFixed}개(식은 그대로)");
        text.AppendLine($"  • 그대로 둠: {follow.Kept - follow.CellsFixed}개(지금도 맞는 식)");
        if (follow.Drifted > 0)
            text.AppendLine($"      - 그 가운데 {follow.Drifted}개는 식을 적은 뒤에 모델 체적이 달라졌습니다(±1% 안이라 '일치') — 새 치수로 맞추려면 그 부재의 식을 지우고 [완료]");
        var notExportable = follow.NotExportable;
        if (notExportable.Count > 0)
            text.AppendLine($"      - 그 가운데 {notExportable.Count}개는 산출근거 엑셀에 싣지 못하는 식입니다: " +
                            string.Join(" / ", notExportable.GroupBy(o => o.NotExportable).Select(g => $"{g.Key} {g.Count()}개").Take(3)));
        int exceptions = follow.Exceptions.Count;
        if (exceptions - follow.StaleLeft > 0) text.AppendLine($"  • 직접 넣어야 함: {exceptions - follow.StaleLeft}개(식을 만들지 못한 부재)");
        if (follow.StaleLeft > 0)
            text.AppendLine($"  • '다시 계산 필요'를 남김: {follow.StaleLeft}개(결합한 부재인데 식을 새로 만들지 못했습니다 — 넣어 둔 식을 확인해 주세요)");
        if (follow.NotWritten > 0) text.AppendLine($"  • ★ 적지 못함: {follow.NotWritten}개(식 칸이 없거나 읽기 전용 · 적다가 오류 — 결과표를 확인해 주세요)");
        if (follow.Skipped > 0) text.AppendLine($"  • 적지 않음: {follow.Skipped}개(그룹 안의 부재 · 다른 사용자가 편집 중 — 위의 수와 겹칠 수 있습니다)");
        if (follow.NotTarget > 0) text.AppendLine($"  • 대상 아님: {follow.NotTarget}개(콘크리트가 아니거나 체적이 없는 부재)");
        if (follow.BadReadBack > 0) text.AppendLine($"  • ★ 적은 뒤 다시 읽은 값이 다른 부재: {follow.BadReadBack}개(결과표를 확인해 주세요)");
        if (plan.Wrote)
            text.AppendLine(placesKept ? "  산출식도 이 창의 실행 취소 한 줄에 들어 있습니다."
                                       : "  산출식은 Revit 의 실행 취소(Ctrl+Z)로 되돌릴 수 있습니다.");
        if (follow.Table.Length > 0) text.AppendLine("  산출식 결과표: " + follow.Table);
    }

    // 그룹을 닫는다. keep = 모델에 남길 것이 있다 → 하나로 묶어 닫는다(Assimilate). 없거나 되돌리기로 했으면 RollBack.
    // 돌려주는 값: (모델에 남았는가, 알릴 문제).
    private static (bool kept, string problem) CloseGroup(TransactionGroup group, bool keep, Session session)
    {
        if (!keep)
        {
            try { session.Note("그룹 닫기\tRollBack\t" + group.RollBack()); }
            catch (Exception ex) { session.Note("그룹 닫기\tRollBack 오류\t" + ex.Message); }
            return (false, "");
        }
        string why;
        try
        {
            var status = group.Assimilate();
            session.Note("그룹 닫기\tAssimilate\t" + status);
            if (status == TransactionStatus.Committed) return (true, "");
            why = status.ToString();
        }
        catch (Exception ex) { why = ex.Message; session.Note("그룹 닫기\tAssimilate 오류\t" + ex.Message); }
        // 하나로 묶지 못했다: 묶지 않고라도 남긴다(실행 취소가 넣은 횟수만큼 나뉠 뿐이다). 이것도 안 되면 Revit 이 전부 되돌린다.
        try
        {
            var status = group.Commit();
            session.Note("그룹 닫기\tCommit\t" + status);
            // 적은 것이 없는 창(보기만 하고 닫음)이면 '한 줄로 묶지 못함'은 알릴 일이 아니다 — 묶을 것이 없다(되돌리기가 아니면 늘 남기므로 생기는 길).
            if (status == TransactionStatus.Committed)
                return (true, session.Commits > 0 ? "넣은 값은 모델에 남았지만 실행 취소 한 줄로 묶지는 못했습니다(" + why + ")." : "");
            why += " / " + status;
        }
        catch (Exception ex) { why += " / " + ex.Message; session.Note("그룹 닫기\tCommit 오류\t" + ex.Message); }
        return (false, "★ 이 창에서 넣은 값을 모델에 남기지 못했습니다(Revit 이 되돌립니다): " + why);
    }

    private static string Notices(ZoneScene.Data data)
    {
        var notices = new List<string>();
        if (data.LevelNotice.Length > 0) notices.Add(data.LevelNotice);
        if (data.Stored.Copies > 1) notices.Add($"모델에 부위·중분류 목록이 {data.Stored.Copies}벌 있어 가장 나중 것을 열었습니다(목록을 고치면 하나로 정리됩니다).");
        if (data.Stored.Exists && data.Stored.Plan.ReadVersion < 2)
            notices.Add("전의 방식(구역 네모)으로 저장한 자료가 있어 이름만 위치 목록으로 옮겼습니다. 목록을 고치면 네모는 지워집니다(지우기 전에 원문을 로그 폴더에 떠 둡니다).");
        else if (data.Stored.Exists && data.Stored.Plan.ReadVersion < ZonePlan.CurrentVersion && data.Stored.Plan.Parts.Count > 0)
            notices.Add("전의 판이 저장한 부위 목록을 구조물의 대표 목록으로 옮겼습니다(직접 넣은 이름과 고친 표시는 그대로 · 바꿔 둔 차례는 묶음의 차례로 다시 놓였습니다).");
        // 상자로 그린 부재 · 못 그린 부재는 3D 그림의 일이라 알리지 않는다(JACK 2026-10-21 «큰 오류처럼 보임» — 일지 «열기» 줄에 Id).
        if (data.FacesSkipped > 0) notices.Add($"세모로 만들지 못한 면이 {data.FacesSkipped}개 있어 그 자리는 비어 보입니다(부재는 다른 면으로 찍을 수 있습니다).");
        return string.Join("\n", notices);
    }

    // 거푸집 대응표(R57 P2 — 모델 안 · FormworkRulesStore): 모델에 기록이 있으면 그것 · 없고 이 PC 에 옛 파일(R57 전 — %APPDATA%\DH.Takeoff\map\{문서 제목}_거푸집표.tsv)이
    // 있으면 한 번 묻고 모델로 옮긴다(계획 검토 M5 — 문서 이름이 같은 다른 모델의 파일일 수 있어 고친 줄 · 지운 판 수를 보이고 묻는다). 옮기면 바로 모델에 적는다(그룹 안).
    // 옛 파일은 지우지 않는다. «아니요»도 빈 대응표를 모델에 적어 그 결정을 남긴다(열 때마다 다시 묻지 않게 — P2 검토 중간 2).
    // 옮기지 못하면 빈 대응표로 센다(창의 대응표 = 모델의 대응표 — 다른 PC 에서 같은 수량이 나오게).
    // 돌려주는 값 = 처음 대응표 · 알릴 것 · 옛 파일을 이 창에서 모델로 옮겼는가.
    private static (FormworkRules rules, string notice, bool migrated) LoadRules(Document doc, Session session)
    {
        var stored = FormworkRulesStore.Read(doc);
        string notice = FormworkRulesStore.Notice(doc, stored);
        if (stored.Exists || stored.Problem.Length > 0)
        {
            session.Note($"대응표 읽음\t모델 · 기록 {stored.Copies}벌 · 합칠 때 다른 값 {stored.Conflicts} · 고친 줄 {stored.Rules.Count} · 뺀 판 {stored.Rules.DropCount} · 옮긴 판 {stored.Rules.MoveCount}" +
                         (notice.Length > 0 ? " · " + notice.Replace('\n', ' ') : ""));
            return (stored.Rules, notice, false);
        }
        string path = FormworkReader.RulesPath(doc);
        FormworkRules old;
        try { old = FormworkRules.Load(path); }
        catch (Exception ex) { session.Note("옛 대응표 파일 읽지 못함\t" + ex.Message); return (new FormworkRules(), notice, false); }
        if (old.Count == 0 && old.DropCount == 0 && old.MoveCount == 0) return (new FormworkRules(), notice, false);
        var answer = MessageBox.Show(
            $"이 PC 에 «{doc.Title}» 이름의 거푸집 대응표 기록이 있습니다(고친 줄 {old.Count} · 지운 판 {old.DropCount} · 옮긴 판 {old.MoveCount}).\n" +
            "이제부터 대응표는 모델 안에 적습니다. 이 기록을 이 모델로 옮길까요?",
            Title, MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.Yes);
        bool move = answer == MessageBoxResult.Yes;
        var start = move ? old : new FormworkRules();
        string problem = FormworkRulesStore.Save(doc, start);
        if (problem.Length == 0) session.Committed();
        session.Note($"옛 대응표 파일 {(move ? "→ 모델" : "옮기지 않음 — 빈 대응표를 모델에 적음")}\t고친 줄 {old.Count} · 뺀 판 {old.DropCount} · 옮긴 판 {old.MoveCount}" +
                     (problem.Length > 0 ? " · 모델에 적지 못함: " + problem : " · 적음"));
        if (problem.Length > 0)
            return (new FormworkRules(), (notice.Length > 0 ? notice + "\n" : "") + $"옛 대응표를 모델에 {(move ? "옮기지" : "적지")} 못해 빈 대응표로 셉니다: " + problem, false);
        return (start, notice, move);
    }

    // Revit 판의 종류 칸을 받아들인다(R57 P3 — JACK 2026-10-23 «그 판 한 장만»): 창을 열 때 한 번 · 그룹 안 트랜잭션 하나(대응표 + 받아들인 판의 기록 —
    // «처음 상태로»가 함께 되돌림). 대응표를 적을 수 없으면(남이 가짐 · 못 읽음 · 더 새 판) 받아들이지 않고 알린다. 돌려주는 값 = 받아들인 뒤 대응표 · 알릴 것 · 적었는가.
    private static (FormworkRules rules, string notice, bool took) TakePlateKinds(Document doc, FormworkRules rules, Session session)
    {
        List<FormworkPlateIntake.Seen> seen;
        try { seen = FormworkPlateStore.Scan(doc); }
        catch (Exception ex) { session.Note("판 종류 읽기 오류\t" + ex.Message); return (rules, "", false); }
        if (seen.Count == 0) return (rules, "", false);
        var copy = FormworkRules.Parse(rules.ToText());
        var o = FormworkPlateIntake.Apply(seen, copy);
        foreach (string line in o.Log) session.Note("판 종류\t" + line);
        var notes = new List<string>();
        bool took = false;
        string problem = "";
        if (o.Taken.Count > 0)
        {
            problem = FormworkRulesStore.Save(doc, copy, d => FormworkPlateStore.MarkTaken(d, o.Taken));
            if (problem.Length == 0)
            {
                session.Committed();
                rules = copy;
                took = true;
                notes.Add($"Revit 판 {o.Taken.Count}장의 종류를 받아들였습니다");
            }
            else notes.Add($"Revit 에서 종류를 바꾼 판 {o.Taken.Count}장을 받아들이지 못했습니다: {problem}");
        }
        if (o.Locked > 0) notes.Add($"다른 사용자가 가진 판 {o.Locked}장의 종류는 받아들이지 못했습니다");
        if (o.Unknown.Count > 0) notes.Add($"판 종류 칸의 모르는 값 {o.Unknown.Count}장(«{o.Unknown[0].text}»{(o.Unknown.Count > 1 ? " …" : "")}) — 쓰지 않음");
        if (o.Copies > 0) notes.Add($"복사한 판 {o.Copies}장은 쓰지 않습니다([완료] 때 지워짐)");
        if (o.Empty > 0) notes.Add($"판 기록이 비어 종류를 받아들이지 못한 판 {o.Empty}장([완료] 한 번 뒤부터)");
        if (o.ValuesChanged > 0) notes.Add($"판 {o.ValuesChanged}장의 다른 칸(넓이 · 식 …)은 고쳐도 쓰지 않습니다([완료] 때 다시 적힘)");
        if (o.Unrecorded > 0) notes.Add($"[완료] 전에 만든 판 {o.Unrecorded}장 — 종류를 바꿔도 읽지 않음([완료] 한 번 뒤부터)");
        int missing = -1;
        if (o.Unrecorded == 0 && FormworkPlateStore.ReadList(doc) is { } listed)
        {
            var present = FormworkPlateStore.PlateUids(doc);
            // 판이 있는데 목록의 판이 하나도 없으면 오래된 목록 — 모름(P3 다시 검토 낮음 3).
            bool stale = present.Count > 0 && listed.Count > 0 && !listed.Any(present.Contains);
            missing = stale ? -1 : listed.Count(u => !present.Contains(u));
            if (missing > 0) notes.Add($"Revit 에서 지운 판 {missing}장 — [완료] 때 다시 만듭니다");
        }
        session.Note($"판 종류 받아들이기\t판 {seen.Count} · 받아들임 {o.Taken.Count}" + (problem.Length > 0 ? "(못 적음: " + problem + ")" : "") +
                     $" · 기록 없음 {o.Unrecorded} · 복사본 {o.Copies} · 남이 가짐 {o.Locked} · 모르는 값 {o.Unknown.Count} · 다른 칸 바뀜 {o.ValuesChanged} · 지운 판 {(missing < 0 ? "모름" : missing.ToString(CultureInfo.InvariantCulture))}");
        return (rules, string.Join("\n", notes), took);
    }

    // ③ 에 보일 것(R59): 엑셀과 같은 항목(콘크리트 줄 · 거푸집 줄 — 이름으로 묶인 시트) · 미산출 부재(이름 · 유형 · 까닭) · ② 위 띠의 알림 한 줄.
    private static PlacePartView.ReviewData ReviewOf(Completion done, string boardNotice, string checkNote)
    {
        var items = CalcSheet.Items(done.Plan?.Rows ?? new List<CalcRow>(), done.Sheet);
        var missing = (done.Plan?.Elements ?? new List<CalcElement>()).Where(e => !e.Excluded && e.Missing.Length > 0).OrderBy(e => e.Id)
            .Select(e => (e.Id, $"{(e.Name.Length > 0 ? e.Name + " · " : "")}{e.Type} · Id {e.Id}", e.Missing)).ToList();
        var notes = new List<string>();
        if (checkNote.Length > 0) notes.Add(checkNote);      // 겹침 · 결합 순서는 딱지로(R63b) — 검사 오류 · 끝까지 못 봄만 글로
        if (done.Plan is { Rows.Count: 0 }) notes.Add("산출근거에 실을 콘크리트 식이 없습니다 — [완료]는 엑셀을 저장하지 않습니다");
        if (done.FormworkProblem.Length > 0) notes.Add("거푸집 속성: " + done.FormworkProblem);
        if (done.NamesProblem.Length > 0) notes.Add("부재 이름: " + done.NamesProblem);
        int formworkFailed = done.Formwork?.Failed.Count ?? 0, reviewFailed = done.Review?.Failed.Count ?? 0;
        if (formworkFailed + reviewFailed > 0)
            notes.Add($"모델에 적지 못한 부재 — 거푸집 속성 {formworkFailed} · 식 {reviewFailed}(그룹 안 · 다른 사용자가 가진 부재 등 — 일지)");
        if (boardNotice.Length > 0) notes.Add(boardNotice);
        // 부재마다 산출근거에 실린 식(③ 콘크리트 숫자의 문지기 — R61b).
        var formulas = new Dictionary<long, string>();
        foreach (var row in done.Plan?.Rows ?? new List<CalcRow>())
            foreach (long id in row.Ids) formulas.TryAdd(id, row.Formula);
        return new PlacePartView.ReviewData(items, missing, string.Join("\n", notes), formulas);
    }

    // [완료] 알림의 판 기록 줄(R57 P3): Revit 에서 지운 판을 다시 만듦(JACK «다시 만들고 알림») · 처음 값 기록을 못 붙인 판.
    private static string PlatesNote(FormworkObjects.Outcome plates)
    {
        var lines = new List<string>();
        if (plates.Missing > 0) lines.Add($"Revit 에서 지운 판 {plates.Missing}장 — 이번 [완료]가 다시 지었습니다(② 에서 지운 판은 빼고) · 빼려면 ② 에서 지우거나 판의 종류를 «세지 않음»으로");
        if (plates.Unstamped > 0 || plates.ListProblem.Length > 0)
            lines.Add($"  ★ 처음 값 기록을 못 붙인 판 {plates.Unstamped}개" + (plates.ListProblem.Length > 0 ? " · 판 목록을 못 적음: " + plates.ListProblem : "") + " — 그 판은 종류를 바꿔도 읽지 않습니다(일지)");
        return string.Join("\n", lines);
    }

    // 나중에 치는 부재(중간슬래브 · 트라프 · 헌치 · 받침 …)가 결합에서 구조체를 깎은 곳의 알림 — [완료] 알림 창(② · ③ 은 «결합 순서» 딱지 — R63b).
    // 짧게(JACK 2026-10-26 «프로그램이 말이 많을수록 안 좋은 법»).
    private static string LaterCutsNote(int count) =>
        $"결합 순서 {count}곳(나중에 치는 부재가 구조체를 깎음 — 거푸집이 조금 더 셀 수 있음) · 고치기: [모델 검증]";

    // ② 의 상태를 짓는다: 모델의 콘크리트 전부(산출식 · 거푸집 면 확인과 같은 거르기)를 재고, 대응표를 건다.
    // 대응표: 앞서 ② 에서 고친 것이 있으면 그것(이 창의 메모리 — 모델에 적지 못했어도 고친 종류가 사라지지 않게, revit R42 L2), 없으면 파일.
    // 곡면은 Revit 이 나눠 준 세모를 붙인다(Core 는 곡면의 세모를 만들지 못한다). notice = 창에 알릴 것("" = 없음).
    // 나중에 치는 부재(받침 · PAD · 디딤판 — 부위 목록의 '나중에 침')는 ① 의 지금 목록으로 정한다: ① 에서 표시를 바꾸면 목록이 모델에 적혀
    // editor.Writes 가 늘고, [다음 단계]에서 다시 잰다.
    private static FormworkBoard BuildBoard(Document doc, ZoneScene.Data data, FormworkRules initial, string initialProblem, FormworkRules? current, Session session, PickEditor editor, out string notice,
                                            out List<PlacePartView.Issue> issues)
    {
        var watch = Stopwatch.StartNew();
        var later = editor.Parts.Where(PartCatalog.IsLater).Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
        // 겹친 자리 순위도 ① 의 지금 목록으로(R54 — revit-verify 계획 M3: 순위 출처 하나 · 수량산출은 ① 의 메모리 값).
        var scene = FormworkReader.Read(doc, Array.Empty<ElementId>(), later.Contains, new DeductRank.Table(editor.Parts));
        // 대응표: 앞서 ② 에서 고친 것(이 창의 메모리) · 없으면 창을 열 때 읽은 모델의 대응표(R57 P2). 사본 — 옛 판과 같은 표를 나눠 쓰지 않게.
        string rulesProblem = current != null ? "" : initialProblem;
        var rules = FormworkRules.Parse((current ?? initial).ToText());
        // 부위의 거푸집 처음 값(R47 — 계단 · 턱·맨홀 · DRY AREA = 합판 3회)은 ① 목록의 묶음으로 본다(목록에서 묶음을 옮긴 부위도 그대로 — core-verify R47 L2).
        var groups = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var p in editor.Parts) groups.TryAdd(p.Name, p.Group);
        // 7 m 넘는 유로폼 면이 있으면 0~7m / 7m 초과로(R49 — JACK 1016 · R53a 녹촌: 높이는 이어진 옆면의 아래 끝부터 — 하부슬래브 옆면 위의 벽은 그 밑부터).
        var options = new FormworkOptions
        {
            Rules = rules, PartDefault = part => PartCatalog.FormworkDefault(part, groups.GetValueOrDefault(part)), Bands = FormworkOptions.AutoBands(scene.Elements),
            // ① 목록의 묶음(사용자가 옮긴 것까지) — 기초 카테고리의 밑면 · 턱 덩어리 빼기가 쓴다(R53a).
            PartGroup = part => groups.TryGetValue(part, out var g) && g.Length > 0 ? g : PartCatalog.GroupOf(part),
        };
        var results = FormworkAnalyzer.AnalyzeAutoBands(scene.Elements, scene.Zones, options);   // 7 m 를 넘는 유로폼 조각이 없으면 구간 없이
        int unpainted = FormworkReader.AttachCurved(scene, results);
        var (placeOf, partOrder) = Places(results, editor);
        var board = new FormworkBoard(results, options, data.Origin, placeOf, partOrder);
        var audit = FormworkAnalyzer.Check(results);
        // 엑셀 줄의 부호(«마크») · 부위 줄(R47): 마크를 넣은 부재 수와 보기 · 부위 줄 · 쓰이지 않는 부위 줄(부위 이름을 바꾼 뒤 남은 것 — core-verify R47 L5).
        var marked = results.Where(r => r.Element.Mark.Length > 0).ToList();
        var unusedParts = FormworkSummary.UnusedPartRules(results, rules);
        var lumpChanges = FormworkSummary.LumpChanges(results);
        session.Note($"거푸집 부호·부위\t마크 있는 부재 {marked.Count}/{results.Count}" +
                     (marked.Count > 0 ? " (예 " + string.Join(", ", marked.Take(5).Select(r => $"{r.Element.Id}={r.Element.Mark}")) + ")" : "") +
                     $" · 부위 줄 {board.Rows.Count(r => r.Category == FormworkSummary.PartCategory)}(고친 것 {rules.All.Count(x => FormworkRules.PartOf(x.type) != null)})" +
                     $" · 쓰이지 않는 부위 줄 {unusedParts.Count}" + (unusedParts.Count > 0 ? "(" + string.Join(", ", unusedParts.Take(6)) + ")" : ""));
        session.Note(FormattableString.Invariant(
            $"거푸집\t대상 {scene.Targets} · 콘크리트 {scene.Elements.Count} · 줄 {board.Rows.Count} · 덩이 {board.Pieces.Count}(세모 {board.Pieces.Sum(p => p.Mesh.Triangles)} · 모서리 {board.Pieces.Sum(p => p.Mesh.Segments)} — 부재 세모 {data.Triangles}) · 거푸집 {board.Total:0.##} ㎡ · 확인할 자리 {board.CheckCount}") +
            $" · 곡면 못 칠함 {unpainted} · 고친 줄 {rules.Count} · 검산 다름 {audit.PolygonMismatch + audit.SumMismatch + audit.PatchMismatch} · 읽기 오류 {scene.ReadErrors + scene.NeighbourReadErrors}" +
            $" · 이은 면 {board.Merge.Groups.Count}(조각 {board.Merge.Groups.Sum(g => g.Members.Count)}) · 종류 달라 못 이음 {board.KindSplits}" +
            (board.KindSplits > 0 ? "(" + string.Join(" · ", board.Merge.SplitSummary()) + ")" : "") + $" · 잇기 거절 {board.Merge.Refused}" +
            $" · 판 {board.Pieces.Count(p => p.Plate != null)}(시트 판 {board.Sheet.Plates.Count}) · 뺀 판 {board.DroppedCount}({board.DroppedArea.ToString("0.##", CultureInfo.InvariantCulture)} ㎡ · 파일 {rules.DropCount})" +
            $" · 모양이 바뀌어 다시 센 뺀 판 {board.LostDrops.Count} · 일부만 찾음 {board.DropCheck.Partial.Count} · 넓이 달라짐 {board.DropCheck.Resized.Count}" +
            $" · 가려진 줄 {board.HiddenTypeRules.Count}" +
            (board.HiddenTypeRules.Count > 0 ? "(" + string.Join(", ", board.HiddenTypeRules.Take(6).Select(h => $"{h.type}·{h.group}={h.item}")) + ")" : "") +
            $" · 높이 구간 {(options.Bands.Count > 0 ? string.Join(",", options.Bands) + " m(이어진 옆면이 7 m 넘는 유로폼 있음)" : "없음")}" +
            $" · 수직면 덩어리 {FormworkAnalyzer.LastGrouping.groups}(종류만 따라감 — R57 · {FormworkAnalyzer.LastGrouping.ms} ms)" +
            $" · 순위 문맥 {FormworkAnalyzer.LastContexts.contexts}(마지막 재기 {FormworkAnalyzer.LastContexts.ms} ms — R54)" +
            $" · 덩어리로 값 바뀐 조각 {lumpChanges.Sum(c => c.count)}({lumpChanges.Sum(c => c.area).ToString("0.##", CultureInfo.InvariantCulture)} ㎡" +
            (lumpChanges.Count > 0 ? ": " + string.Join(" · ", lumpChanges.Take(6).Select(c => FormattableString.Invariant($"{c.type} {c.from}→{c.to} {c.count}개 {c.area:0.##}"))) : "") + ")" +
            $" · 덩어리에 든 확인할 자리 {FormworkSummary.LumpChecks(results).Count}(늘 0 — 확인할 자리는 덩어리에 넣지 않음)" +
            $" · 둘레 한 줄 {board.Sheet.Rings.found}(값이 안 맞아 면마다 {board.Sheet.Rings.refused}) · (양측) 줄 {board.Sheet.Lines.Count(l => l.Tag == FormworkSheet.BothSidesTag)}" +
            $" · 판 지음 {board.RefreshMilliseconds} ms" +
            (rulesProblem.Length > 0 ? " · 대응표를 읽지 못함: " + rulesProblem : "") + $" · {watch.ElapsedMilliseconds} ms");
        // 나중에 치는 부재(부위 목록의 '나중에 침'): 어느 부재를 그렇게 봤는지 · 구조체에 얼마를 돌려 셌는지 · 결합에서 구조체를 깎은 곳 — 첫 실행에서 바로 가르게 Id 를 적는다.
        var laterIds = scene.Elements.Where(e => e.Later).Select(e => e.Id).ToList();
        if (laterIds.Count > 0 || scene.LaterCuts.Count > 0)
        {
            var returned = results.Select(r => (id: r.Element.Id, area: r.Faces.Sum(f => f.LaterCovered))).Where(x => x.area > 1e-6).OrderByDescending(x => x.area).ToList();
            session.Note(FormattableString.Invariant(
                $"거푸집 나중에 침\t부재 {laterIds.Count}(Id {string.Join(",", laterIds.Take(12))}{(laterIds.Count > 12 ? " …" : "")}) · 구조체로 돌려 센 넓이 {returned.Sum(x => x.area):0.####} ㎡(") +
                string.Join(" · ", returned.Take(12).Select(x => FormattableString.Invariant($"{x.id} {x.area:0.####}"))) +
                $") · 결합에서 구조체를 깎은 곳 {scene.LaterCuts.Count}(" + string.Join(" · ", scene.LaterCuts.Take(12).Select(c => $"{c.later}→{c.cut}")) + ")");
        }
        // Revit 계단(R56c-1): 둘레 콘크리트로 넣은 수 + 계단마다 한 줄(런 · 참 · 재료 · 형상 출처 · 체적 — 계단을 부재로 세기(R56c-2) 전에 첫 실행 값을 본다).
        if (scene.StairLines.Count > 0)
        {
            session.Note($"계단(둘레 콘크리트)\t계단 {scene.StairLines.Count} · 콘크리트로 넣음 {scene.StairContexts}");
            foreach (string line in scene.StairLines) session.Note("  계단\t" + line);
        }
        // «부위와 모양이 안 맞음»(R57 P4 — 경고만 · 셈은 그대로): 유형 이름이 다른 부위 묶음을 가리킴 · 받침류인데 밑면이 바닥판 위 1 m 넘게 떠 있음.
        var odd = FormworkPartCheck.Check(results, part => groups.TryGetValue(part, out var g) && g.Length > 0 ? g : null);
        foreach (var f in odd) session.Note($"부위 경고\t{f.Id}\t{f.Type}\t{f.Part}\t{f.Text}");
        var oddIds = odd.Select(f => f.Id).Distinct().ToList();
        // 창의 딱지(R63b — JACK 2026-10-26 «말이 많을수록 안 좋다 · 중요하면 위치 확인 가능하게»): 겹침(열 때 검사) · 결합 순서(열 때 검사 + 나중 부재가 깎은 곳) ·
        // 부위 확인. 뺀 판의 자리 · 모양이 바뀐 판은 ② 위 딱지에 이미 있다. 그림 위 글은 드문 것(대응표 · 검산 · 읽기 오류 · 곡면)만.
        issues = session.ModelCheckIssues.Where(i => i.Key == "overlap").ToList();
        // 나중 부재가 구조체를 깎은 결합: 구조체 면에 홈이 남아 벽이 전체 높이가 되지 않는다(revit-verify R46 M1) — 열 때 찾은 반대 결합과 한 딱지.
        session.LaterCuts = scene.LaterCuts.Count;
        var joins = session.ModelCheckJoins.Concat(scene.LaterCuts.Select(c => (cutter: c.later, cut: c.cut))).Distinct().ToList();
        if (joins.Count > 0) issues.Add(JoinsIssue(joins));
        if (oddIds.Count > 0)
            issues.Add(new PlacePartView.Issue("parts", "부위 확인", oddIds.Count, oddIds,
                "부위가 모양과 맞지 않아 거푸집 · 산출서 줄이 틀릴 수 있습니다 — ① 에서 부위를 고치세요.\n" +
                string.Join("\n", odd.GroupBy(f => f.Id).Take(5).Select(g => $"{g.Key} {g.First().Type}: {string.Join(" · ", g.Select(f => f.Text))}")) + (oddIds.Count > 5 ? "\n…" : "")));
        var notes = new List<string>();
        if (rulesProblem.Length > 0) notes.Add(rulesProblem);
        if (audit.PolygonMismatch + audit.SumMismatch + audit.PatchMismatch > 0) notes.Add("검산이 맞지 않는 면이 있습니다 — 일지를 보내 주세요.");
        int unread = scene.ReadErrors + scene.NeighbourReadErrors;
        if (unread > 0) notes.Add($"형상을 읽지 못한 부재 {unread}개와 맞닿은 자리는 빼지 못했습니다.");
        if (unpainted > 0) notes.Add($"색을 칠하지 못한 곡면 {unpainted}개(넓이는 셉니다).");
        notice = string.Join(" · ", notes);
        session.Note($"딱지\t{string.Join(" · ", issues.Select(i => $"{i.Name} {i.Count}(부재 {i.Members.Count})"))}{(notice.Length > 0 ? "\t그림 위 글: " + notice : "")}");
        return board;
    }

    // 창을 띄운다(R62 — JACK 2026-10-26 «팝업을 쓸 때 Revit 이 락 걸리지 않았으면»): 명령은 띄우고 끝나고, 창의 일은 TakeoffSession 의 요청 줄로
    // (Revit 이 손이 빌 때 · 이름 있는 일은 실행 취소 한 줄 — 계획 §8 요청 표). 닫기의 마무리(위치 색 · 일지 · 알림 창)도 요청 하나다.
    private static void OpenWindow(UIApplication uiapp, UIDocument uidoc, PickEditor editor, ZoneScene.Data data, Session session, string notice,
                                   RevitFollow revitFollow, Opening opening)
    {
        var doc = uidoc.Document;
        bool dark = false;
        try { dark = UIThemeManager.CurrentTheme == UITheme.Dark; } catch { }
        var view = new PlacePartView(editor, data.Meshes, dark, notice) { Log = session.Note, ModelCheck = session.ModelCheckNote, Issues = session.ModelCheckIssues };
        var window = view.BuildWindow();
        new WindowInteropHelper(window).Owner = uiapp.MainWindowHandle;
        WindowMemory.Apply(window, session.Note);
        var takeoff = new TakeoffSession(doc, window, view, session.Note);
        view.Runner = takeoff;
        Completion? completion = null;
        var initialRules = opening.Rules;
        string initialProblem = opening.Problem;
        bool rulesSaved = false;
        // 모델에 적힌 대응표의 글(마지막으로 적었거나 읽은 것) · 적지 못한 까닭: 창의 대응표가 모델과 다른 채로는 [완료]를 하지 않는다
        // (부재 속성 · 판 · 엑셀이 모델에 없는 대응표로 나가 다른 PC 에서 다른 수량이 되지 않게 — P2 검토 중간 3). «저장 대기» = 적는 일이 줄에 있음(R62 계획 검토 M1).
        string rulesInModel = initialRules.ToText();
        string rulesUnsaved = "";
        bool rulesQueued = false;
        var plates = new Plates();
        string boardNotice = "";      // ② 위 띠의 알림(③ 에도 한 줄 — R59 계획 §4 · ② 를 지을 때 정함)
        // ③ 산출서 확인(R59): ③ 에 들어갈 때 산출식 · 이름 · 거푸집 속성을 모델에 적고(Prepare) 그 결과로 산근을 보인다. 표시(보드 · ① 에서 적은 횟수 ·
        // 대응표 글)가 같고 완전히 된 Prepare 면 다시 돌지 않는다(계획 검토 M5). prepareWrote = 이 창에서 Prepare 가 모델에 무언가 적었다(닫기 확인 — M3).
        (FormworkBoard? board, int writes, string rules) PrepKey() => (view.Board, editor.Writes, view.Board?.Rules.ToText() ?? "");
        (FormworkBoard? board, int writes, string rules)? preparedKey = null;
        bool prepareWrote = false, closingChecked = false, finished = false;
        bool Fresh() => completion is { Clean: true } && preparedKey is { } key && ReferenceEquals(key.board, view.Board) && key.writes == editor.Writes && key.rules == (view.Board?.Rules.ToText() ?? "");

        // ② · ③ 에서 고르면 Revit 에서도 선택 + 확대(R61a) — 읽기 요청(마지막 것만 · 돌 때 다시 봄: ② · ③ 이고 모델이 그대로일 때만 — 계획 검토 M3).
        (IReadOnlyList<long> ids, DH.Takeoff.Core.View3D.Box3 box)? followNext = null;
        view.Follow = (ids, box) =>
        {
            bool queued = followNext != null;
            followNext = (ids, box);
            if (queued) return;
            takeoff.Read("따라가기", () =>
            {
                if (followNext is not { } next) return;
                followNext = null;
                if (view.Step >= 1 && !takeoff.Stale && window.IsVisible) revitFollow.Follow(next.ids, next.box);
            }, error => { if (error != null) followNext = null; });      // [취소]로 거둔 것 — 남겨 두면 다음 따라가기가 줄 서지 않는다(R62 설치 전 검토 중간)
        };
        // ③ 콘크리트 줄의 숫자(R61b): ③ 에 들어갈 때(Prepare — 요청 안) 부재마다 다시 풀어 둔 도형(R62 — 창은 API 를 부르지 않는다).
        view.ConcreteGeometry = id => completion?.Geometry.GetValueOrDefault(id);
        revitFollow.Notice = text =>
        {
            if (view.MessageText.Length == 0) view.ShowMessage(text);
            else session.Note("Revit 따라가기\t알림 안 띄움(띠에 다른 알림)\t" + text);
        };

        // 모델이 바뀐 뒤에는 쓰는 일 · 다음 단계 · [완료]를 하지 않는다(R62 계획 검토 H1 — [다시 열기]).
        bool Blocked()
        {
            if (!takeoff.Stale) return false;
            view.ShowMessage("Revit 에서 모델이 바뀌었습니다 — 위의 [다시 열기]로 지금 모델을 다시 읽어 주세요.", true);
            return true;
        }
        // 창이 하는 긴 일(요청 하나): 그동안 창을 잡아 두고(Held — 그 틈에 쌓인 누르기가 또 돌지 않게), 끝나면 푼다. 오류는 창에 적는다.
        void Request(string? undo, string name, Action work, Action? after = null)
        {
            view.Held = true;
            void Done(Exception? error)
            {
                view.Held = false;
                if (error is OperationCanceledException) view.ShowMessage("그만두었습니다(Revit 이 다른 작업 중이었음).");
                else if (error != null) { session.Note($"{name}\t오류\t{error}"); view.ShowMessage($"{name}에서 오류가 났습니다(넣은 값은 모델에 그대로 있습니다): " + error.Message, true); }
                after?.Invoke();
            }
            if (undo != null) takeoff.Write(undo, work, Done);
            else takeoff.Read(name, work, Done);
        }

        view.Close += () => window.Close();
        view.Reopen += () =>
        {
            // 모델이 바뀌었다: 창을 닫고(일지만 — 위치 색 · 알림 창 없이) 같은 요청 안에서 지금 모델로 다시 연다(단계 2 의 제자리 다시 읽기 전까지 — 계획 검토 H1).
            if (view.Busy) return;
            session.Note("창\t[다시 열기]");
            window.Hide();
            takeoff.Read("다시 열기", () =>
            {
                CloseUp(forced: true);
                takeoff.End();
                Reopen(uiapp);
            });
        };
        view.CancelPending += () => takeoff.CancelPending();
        view.Review += () =>
        {
            if (view.Busy || view.Board is null || Blocked()) return;
            Request("DH 산출서 준비", "③ 산출서 확인", () =>
            {
                // 차례: 대응표를 모델에 적지 못함 → 처리 중 표시(계획 검토 M4 — 확인 창 · 물음이 도는 틈에 쌓인 누르기가 또 돌지 않게).
                if (view.Board is not { } now) return;
                if (rulesUnsaved.Length > 0 && now.Rules.ToText() == rulesInModel) rulesUnsaved = "";   // 되돌려 모델과 같아졌다
                if (rulesUnsaved.Length > 0)
                {
                    session.Note("③ 산출서 확인\t막음 — 대응표를 모델에 적지 못함\t" + rulesUnsaved);
                    view.ShowMessage("거푸집 대응표를 모델에 적지 못해 넘어갈 수 없습니다(고친 것을 되돌리거나 다시 고쳐 적어 주세요): " + rulesUnsaved, true);
                    return;
                }
                if (Fresh() && completion is { } same)
                {
                    session.Note("③ 산출서 확인\t바뀐 것 없음 — 앞서 모은 산근 그대로");
                    view.ShowReview(ReviewOf(same, boardNotice, session.ModelCheckNote));
                    return;
                }
                var watch = Stopwatch.StartNew();
                var done = new Completion { Objects = plates.Built };
                int commitsBefore = session.Commits;
                bool ok = false;
                try { ok = Prepare(window, view, editor, doc, session, dark, done); }
                finally
                {
                    // 예외가 나도 적은 것은 기록한다(닫기 확인 — 설치 전 검토 낮음).
                    if (session.Commits > commitsBefore) prepareWrote = true;
                    completion = done;
                    preparedKey = PrepKey();
                }
                session.Note($"③ 산출서 확인\t{(ok ? "들어감" : "멈춤")} · 완전히 됨 {(done.Clean ? "O" : "X")} · {watch.ElapsedMilliseconds} ms");
                if (ok) view.ShowReview(ReviewOf(done, boardNotice, session.ModelCheckNote));
            });
        };
        view.ReviewBack += () => session.Note("걸음\t③ → ② 거푸집면 설정");
        view.Finish += () =>
        {
            if (view.Busy || completion is not { Prepared: true } || Blocked()) return;
            Completion? ready = null;
            // [완료] 전체가 요청 하나 · 실행 취소 한 줄 «DH 거푸집 객체». 엑셀은 그 묶음이 모델에 남은 뒤(요청 끝)에 쓴다(revit R42 A1 의 뜻 — 계획 검토 L2).
            Request("DH 거푸집 객체", "완료", () =>
            {
                if (completion is not { Prepared: true } done) return;
                // ② · ① 에서 바뀐 뒤 그대로 [완료]로 오는 길은 없지만(③ 은 [다음 단계]로만 온다) 한 번 더 본다(계획 검토 H1).
                if (!(preparedKey is { } key && ReferenceEquals(key.board, view.Board) && key.writes == editor.Writes && key.rules == (view.Board?.Rules.ToText() ?? "")))
                {
                    view.ShowMessage("② 에서 바뀐 것이 있습니다 — [← 이전 단계] 뒤 [다음 단계]를 다시 눌러 주세요.", true);
                    return;
                }
                Finish(window, view, doc, session, plates, done);
                if (done.Ready) ready = done;
            }, after: () =>
            {
                if (ready == null) return;
                if (takeoff.Stale)
                {
                    session.Note("완료\t모델이 바뀌어 엑셀을 쓰지 않음");
                    view.ShowMessage("Revit 에서 모델이 바뀌어 엑셀을 쓰지 않았습니다 — [다시 열기] 뒤 다시 [완료]", true);
                    return;
                }
                try { SaveExcel(ready, session); }
                catch (Exception ex) { ready.ExcelProblem = ex.Message; session.Note("완료\t엑셀 저장 오류\t" + ex); }
                finished = true;
                CloseNow();
            });
        };

        // ② 거푸집면 설정: [다음 단계]를 누를 때 모델의 콘크리트 전부를 잰다(읽기 요청). ① 에서 부위를 바꾸면(버림 콘크리트 등) 거푸집이 달라질 수 있어,
        // 그 사이 모델에 적은 것이 있으면 다시 잰다(대응표는 창의 메모리 것 — ② 에서 고친 것은 모델에도 적혀 있다).
        FormworkBoard? board = null;
        int boardAt = -1;
        view.Next += () =>
        {
            if (Blocked()) return;
            Request(null, "거푸집 재기", () =>
            {
                if (board == null || boardAt != editor.Writes)
                {
                    FormworkBoard? made = null;
                    string note = "";
                    List<PlacePartView.Issue> issues = new();
                    string problem = view.RunLocked(() => made = BuildBoard(doc, data, initialRules, initialProblem, board?.Rules, session, editor, out note, out issues));
                    if (made == null)
                    {
                        session.Note("거푸집\t재지 못함\t" + problem);
                        view.ShowMessage("거푸집 면을 재지 못했습니다(① 의 값은 그대로입니다): " + problem, true);
                        return;
                    }
                    board = made;
                    boardAt = editor.Writes;
                    boardNotice = note;
                    view.Issues = issues;
                    view.ShowFormwork(board);
                    if (note.Length > 0) view.ShowMessage(note, true);
                }
                else view.ShowFormwork(board);
                session.Note("걸음\t② 거푸집면 설정");
            });
        };
        view.Back += () => session.Note("걸음\t① 부위 · 중분류");
        // 대응표를 고칠 때마다 모델에 적는다(R57 P2 — 실행 취소 한 줄 «DH 거푸집 대응표»). 줄에 이미 있으면 그 일이 그때의 대응표를 적는다(합침 — R62 계획 검토 M1).
        view.SaveRules = () =>
        {
            if (takeoff.Stale) return "Revit 에서 모델이 바뀌었습니다 — [다시 열기]";
            if (view.Board == null) return "";
            if (rulesQueued) return "";
            rulesQueued = true;
            rulesUnsaved = "저장 대기";
            string problem = "";
            takeoff.Write("DH 거푸집 대응표", () =>
            {
                rulesQueued = false;
                var now = view.Board?.Rules;
                if (now == null) { rulesUnsaved = ""; return; }
                string text = now.ToText();
                problem = FormworkRulesStore.Save(doc, now);
                if (problem.Length == 0)
                {
                    rulesSaved = true;
                    rulesInModel = text;
                    rulesUnsaved = "";
                    session.Committed();
                    session.Note($"대응표 적음\t고친 줄 {now.Count} · 뺀 판 {now.DropCount} · 옮긴 판 {now.MoveCount}\t모델");
                }
                else
                {
                    rulesUnsaved = text == rulesInModel ? "" : problem;   // 되돌려 모델과 같아졌으면 막을 것이 없다
                    session.Note("대응표 적지 못함\t" + problem + (rulesUnsaved.Length > 0 ? "\t[완료] 막음" : "\t모델과 같음"));
                }
            }, error =>
            {
                rulesQueued = false;
                if (error != null && rulesUnsaved == "저장 대기") rulesUnsaved = error.Message;
                string why = error?.Message ?? problem;
                if (why.Length > 0) view.ShowMessage("거푸집 대응표를 모델에 적지 못했습니다: " + why, true);
            });
            return "";
        };
        // 닫기: 일이 줄에 있거나 도는 중이면 닫지 않는다. ③ 에 들어가며 모델에 적은 뒤 ② · ① 에서 바뀌었는데 그대로 닫으면 한 번 묻는다
        // (산출식 · 이름 · 거푸집 속성이 옛 값 — 계획 검토 M3 · 다시 검토 낮음 1 · 2: [닫기] · 창 X 모두 · [완료]는 묻지 않음).
        // 묻고 나면 창을 숨기고 마무리(위치 색 · 일지 · 알림 창)를 요청으로 — 끝나면 정말 닫는다.
        window.Closing += (_, e) =>
        {
            if (takeoff.Ending) return;
            e.Cancel = true;
            if (view.Busy || takeoff.Pending) { view.ShowMessage("하던 일이 끝나면 닫을 수 있습니다."); return; }
            bool current = completion is { Prepared: true } && preparedKey is { } k && ReferenceEquals(k.board, view.Board) && k.writes == editor.Writes && k.rules == (view.Board?.Rules.ToText() ?? "");
            if (!(closingChecked || finished || !prepareWrote || current || takeoff.Stale))
            {
                var answer = MessageBox.Show(window,
                    "③ 에 들어간 뒤 바뀐 것이 있어 산출식 · 부재 이름 · 거푸집 속성이 지금 값과 다릅니다(엑셀 · 거푸집 객체는 만들지 않았습니다).\n" +
                    "그래도 닫을까요? ([아니요] 뒤 [다음 단계]를 다시 누르면 반영됩니다.)",
                    Title, MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
                session.Note("창 닫기\t③ 뒤 바뀐 채 닫기 물음\t" + (answer == MessageBoxResult.Yes ? "닫음" : "머묾"));
                if (answer != MessageBoxResult.Yes) return;
                closingChecked = true;
            }
            CloseNow();
        };
        // 정말 닫는다: 창 자리를 적고 숨긴 뒤 마무리(위치 색 · 일지 · 알림 창)를 요청으로 — 끝나면 창을 거둔다.
        // [완료]는 제 요청 안에서 부른다(줄에 잇대어 같은 Execute 에서 돈다 — window.Close 로 Closing 을 거치면 «일이 도는 중»이라 닫히지 않는다 · R62 설치 전 검토 높음).
        // 쓰는 요청 안([완료])이면 숨기기는 닫기 요청에서 — 그동안 Revit 창이 꺼져 있어, 지금 숨기면 다른 프로그램이 앞으로 온다(R62 다시 검토 중간).
        void CloseNow()
        {
            WindowMemory.Save(window, session.Note);
            if (!takeoff.Executing) window.Hide();
            takeoff.Read("닫기", () =>
            {
                if (window.IsVisible) window.Hide();
                CloseUp(forced: false);
            }, _ => takeoff.End());
        }
        // 창이 떠 있는 동안 우리 처리기에서 새는 오류: 창에 적고 머문다. 창을 띄운 채 쓰면 이 디스패처는 Revit 의 것이라 우리 것만 받는다(R62 계획 검토 L6).
        System.Windows.Threading.DispatcherUnhandledExceptionEventHandler onError = (_, e) =>
        {
            try
            {
                // 우리 처리기에서 난 오류인지는 호출 기록으로 가린다(싸여 온 예외의 안쪽 기록까지 본다 — ToString).
                if (!e.Exception.ToString().Contains("DH.Takeoff", StringComparison.Ordinal)) return;
                session.Note("창의 오류\t" + e.Exception);
                view.RefreshAll();
                view.ShowMessage("창에서 오류가 났습니다(넣은 값은 모델에 그대로 있습니다): " + e.Exception.Message, true);
                e.Handled = true;
            }
            catch { }
        };
        window.Dispatcher.UnhandledException += onError;
        window.Closed += (_, _) => window.Dispatcher.UnhandledException -= onError;

        // 닫기의 마무리(API 문맥 — 닫기 · 다시 열기 요청, 또는 문서 · Revit 을 닫는 중의 강제 닫기). forced = 확인 · 위치 색 · 알림 창 없이 일지만.
        void CloseUp(bool forced)
        {
            view.StopFollow();
            if (!forced) revitFollow.Closed();
            View3D? checkView = null;
            var legend = new Dictionary<string, string>();
            string paintProblem = "";
            if (!forced && !takeoff.Stale && editor.Writes > 0)
            {
                // 확인용 Revit 뷰에도 위치 색을 입힌다 — 실행 취소 한 줄 «DH 위치 색»(R62 계획 검토 L3). 못 입혀도 넣은 값은 남는다.
                using var paint = new TransactionGroup(doc, "DH 위치 색") { IsFailureHandlingForcedModal = true };
                try
                {
                    paint.Start();
                    var zoneOf = new Dictionary<ElementId, string>();
                    foreach (var m in editor.Members) zoneOf[new ElementId(m.Id)] = m.Value.Zone;
                    checkView = ZoneAssigner.Paint(doc, zoneOf, editor.Places.Select(p => p.Name).ToList(), out legend, out paintProblem);
                    if (checkView != null) session.Committed();
                }
                catch (Exception ex) { paintProblem = "색을 입히지 못했습니다: " + ex.Message; }
                finally { try { if (paint.HasStarted()) paint.Assimilate(); } catch { } }
            }
            session.Note($"창 닫힘\t적은 횟수 {editor.Writes}\t걸음 {view.Step + 1}\t대응표 고침 {(rulesSaved ? "O" : "X")}\t완료 {(completion?.Saved == true ? "O" : "X")}" +
                         $"\t모델 바뀜 {(takeoff.Stale ? "O" : "X")}{(forced ? "\t강제" : "")}");
            string table = "", journal = "";
            try { (table, journal) = WriteLogs(doc, editor, session, kept: true, revert: false); }
            catch (Exception ex) { table = "(쓰지 못했습니다 — 결과표가 엑셀에 열려 있으면 닫아 주세요: " + ex.Message + ")"; }
            if (forced) return;
            try
            {
                var front = uiapp.ActiveUIDocument;
                if (completion?.Ready == true) ReportDone(front, editor, session, completion, "", paintProblem, checkView, legend, table, journal);
                else Report(front, editor, session, session.Commits > 0, false, "", "", paintProblem, checkView, legend, table, journal, completion?.Formulas, completion);
            }
            catch { /* 알림 창을 못 띄워도 결과는 바뀌지 않는다 */ }
        }
        takeoff.Finalizer = CloseUp;

        var shown = Stopwatch.StartNew();
        window.ContentRendered += (_, _) => session.Note($"창이 그려짐\t{shown.ElapsedMilliseconds} ms\t소프트웨어로만 그림 {(RenderOptions.ProcessRenderMode == RenderMode.SoftwareOnly ? "O" : "X")}");
        if (opening.Migrated || opening.PlatesTaken) session.Note($"열 때 적음\t옛 대응표 옮김 {(opening.Migrated ? "O" : "X")} · 판 종류 받아들임 {(opening.PlatesTaken ? "O" : "X")}");
        window.Show();
        // 창의 글쇠를 Revit 의 단축키보다 먼저(계획 검토 H2 — 창에서 누른 Del · Ctrl+Z 가 Revit 의 지우기 · 실행 취소가 되지 않게).
        takeoff.Keys = new KeyboardGuard(window);
        session.Note($"창\t띄움(Revit 도 쓸 수 있음) · 글쇠 지킴 {(takeoff.Keys.Active ? "O" : "X")}");
    }

    // [다시 열기](R62): 닫은 창 대신 지금 모델로 새 창을 연다(API 문맥 — 요청 안). 명령의 Execute 와 같은 길.
    private static void Reopen(UIApplication uiapp)
    {
        var uidoc = uiapp.ActiveUIDocument;
        if (uidoc == null) return;
        string message = "";
        try { new QuantityTakeoffCommand().Open(uidoc, uiapp, ref message); }
        catch (Exception ex) { Dialogs.Show(Title, "「수량산출」 창을 다시 열지 못했습니다: " + ex.Message); }
    }

    private static void Report(UIDocument uidoc, PickEditor? editor, Session session, bool kept, bool revert, string crash, string closeProblem,
                               string paintProblem, View3D? view, Dictionary<string, string> legend, string table, string journal, FormulaFollow? follow, Completion? done)
    {
        // 보기만 하고 닫았으면(적은 것도 문제도 없고, 이어서 돈 산출식도 이번에 한 일이 없다) 알림 창을 띄우지 않는다.
        if (!kept && !revert && crash.Length == 0 && closeProblem.Length == 0 && follow?.HasNews != true) return;
        // 창이 뜰 때는 산출식 쪽에 남아 있는 것(사람이 확인할 부재)도 함께 적는다.
        bool formulaNews = follow != null && (follow.HasNews || follow.Exceptions.Count > 0);

        var text = new StringBuilder();
        if (crash.Length > 0) text.AppendLine("★ 창에서 오류가 났습니다: " + crash).AppendLine();
        if (closeProblem.Length > 0) text.AppendLine(closeProblem).AppendLine();
        var td = Dialogs.New(Title);
        td.AllowCancellation = true;
        if (editor != null && kept)
        {
            var p = editor.Progress();
            td.MainInstruction = p.Remaining == 0 ? "부위·중분류 — 모두 끝났습니다" : $"부위·중분류 — 남은 부재 {p.Remaining}개";
            text.AppendLine($"부재 {p.Total}개 — 끝 {p.Done} · 아직 {p.Todo} · 하나만 {p.Partial}" + (p.Outside > 0 ? $" · 확인 필요 {p.Outside}" : "") +
                            (p.Locked > 0 ? $" · 고칠 수 없음 {p.Locked}" : "") + (p.Undrawn > 0 ? $" · 그리지 못함 {p.Undrawn}" : ""));
            var places = editor.Places.Select(x => x.Name).Where(n => editor.CountInPlace(n) > 0).ToList();
            if (places.Count > 0)
                text.AppendLine("중분류별: " + string.Join(" · ", places.Select(n => $"{n} {editor.CountInPlace(n)}개" + (view != null && legend.TryGetValue(n, out var color) ? $"({color})" : ""))));
            if (editor.CountNoPlace > 0) text.AppendLine($"'{ZonePlan.NoPlaceLabel}'으로 정한 부재: {editor.CountNoPlace}개");
            text.AppendLine();
            text.AppendLine("이 창에서 넣은 것은 누를 때마다 Revit 실행 취소 목록의 한 줄로 남았습니다(«DH 부위·중분류» …).");
            if (session.LegacyDump.Length > 0) text.AppendLine("전의 판이 저장한 목록의 원문: " + session.LegacyDump);
        }
        else if (revert) td.MainInstruction = "처음 상태로 되돌렸습니다 — 이 창에서 넣은 것은 모델에 남지 않았습니다";
        else if (crash.Length > 0 || closeProblem.Length > 0) td.MainInstruction = "이 창에서 넣은 것이 모델에 남지 않았습니다";
        else
        {
            // 이번에 넣은 것은 없다(모두 끝나 있는 모델을 열었다 닫았다) — 이어서 돈 산출식만 알린다.
            td.MainInstruction = "부위·중분류는 모두 끝나 있습니다";
            text.AppendLine("이번에 넣거나 바꾼 부위·중분류는 없습니다.");
        }
        if (follow != null && formulaNews) AppendFormulaReport(text, follow, kept);
        // [완료]에서 엑셀 저장을 그만두고 닫았어도 판은 만들어져 남았다(JACK 2026-10-16 «저장 취소해도 만들기») — 여기에도 적는다.
        if (kept && done?.Objects is { Committed: true } plates)
        {
            text.AppendLine().AppendLine($"거푸집 객체: 판 {plates.Created}개(일반 모델 · 12 mm — 엑셀은 저장하지 않았습니다)" +
                                         (plates.Failed.Count > 0 ? $" · ★ 못 만든 판 {plates.Failed.Count}개(일지)" : "") + " — 납품 전에 리본 [거푸집 › 거푸집 삭제]");
            if (FormworkHiddenNote(uidoc, plates.Created) is { Length: > 0 } hidden) text.AppendLine("⚠ " + hidden);
            if (PlatesNote(plates) is { Length: > 0 } platesNote) text.AppendLine(platesNote);
        }
        else if (kept && done != null && done.ObjectsProblem.Length > 0)
            text.AppendLine().AppendLine("★ 거푸집 객체를 만들지 못했습니다: " + done.ObjectsProblem);
        if (paintProblem.Length > 0) text.AppendLine().AppendLine(paintProblem);
        text.AppendLine();
        text.AppendLine("결과표: " + table);
        text.Append("일지: " + journal);
        // 본문은 핵심 몇 줄 + 걸리는 것만, 나머지(중분류별 수 · 산출식 내역 · 되돌리기 안내 · 파일 경로)는 «자세히»(JACK 2026-10-21 «여전히 마지막 팝업에 말이 많아» —
        // [완료]에서 엑셀을 저장하지 않고 닫은 때의 이 창도 ReportDone 과 같은 꼴로).
        var brief = new List<string>();
        if (crash.Length > 0) brief.Add("★ 창에서 오류가 났습니다: " + crash);
        if (closeProblem.Length > 0) brief.Add(closeProblem);
        if (editor != null && kept)
        {
            var p = editor.Progress();
            brief.Add($"부재 {p.Total}개 — 끝 {p.Done}" + (p.Todo > 0 ? $" · 아직 {p.Todo}" : "") + (p.Partial > 0 ? $" · 하나만 {p.Partial}" : ""));
        }
        else if (!revert && crash.Length == 0 && closeProblem.Length == 0) brief.Add("이번에 바꾼 부위·중분류 없음");
        if (follow != null && formulaNews && kept && !revert)
        {
            if (follow.Problem.Length > 0) brief.Add("★ 산출식을 이어서 하지 못했습니다 — ② 의 [다음 단계]를 다시");
            else if (follow.Plan.Declined) brief.Add("산출식은 나중에 — ② 의 [다음 단계]를 다시");
            else if (!follow.RolledBack)
            {
                var formulas = new List<string>();
                if (follow.NewFormula > 0) formulas.Add($"새로 적음 {follow.NewFormula}");
                if (follow.Replaced > 0) formulas.Add($"바꿔 적음 {follow.Replaced}");
                formulas.Add($"그대로 {follow.Kept - follow.CellsFixed}");
                brief.Add("산출식: " + string.Join(" · ", formulas));
            }
        }
        if (kept && done?.Objects is { Committed: true } made) brief.Add($"거푸집 판 {made.Created}장 · 엑셀 저장 안 함");
        // ③ 에 들어가 산출식 · 이름 · 거푸집 속성은 적었지만 [완료]를 하지 않고 닫음(R59 계획 검토 L6).
        else if (kept && done is { Prepared: true, Ready: false }) brief.Add("③ 에서 산출식 · 부재 이름 · 거푸집 속성을 적음 · 엑셀 · 거푸집 판은 만들지 않음([완료]에서)");
        var flags = Flags(text.ToString()).Where(f => !brief.Contains(f)).ToList();
        if (flags.Count > 0)
        {
            brief.Add("");
            brief.AddRange(flags);
        }
        td.MainContent = string.Join("\n", brief);
        td.ExpandedContent = text.ToString();
        if (view != null && kept)
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, $"확인용 3D 뷰 열기 ('{ZoneAssigner.CheckViewName}')", "중분류별 색 · 거푸집 객체 숨김");
        var schedule = kept && done?.Objects is { Committed: true, ScheduleId: { } scheduleId } ? uidoc.Document.GetElement(scheduleId) as View : null;
        if (schedule != null)
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, $"거푸집 객체 일람표 열기 ('{FormworkObjects.ScheduleName}')", "판마다 종류 · 넓이 · 산출식");
        // 식을 못 만든 부재를 하나씩 보는 일은 [완료]가 창 안에서 한다(ExceptionReview) — 여기서 따로 뜨는 검토 창(모델리스)은 더 띄우지 않는다:
        // 그 창이 떠 있는 채로 다시 [완료]를 하면 두 창이 같은 부재를 고치고, 따로 뜬 창의 저장이 나중에 덮어쓸 수 있다(revit R42 L3).
        td.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "닫기");
        var pressed = td.Show();
        if (pressed == TaskDialogResult.CommandLink1 && view != null)
        {
            try { uidoc.ActiveView = view; }
            catch (Exception ex)
            {
                Dialogs.Show(Title, $"뷰를 열지 못했습니다: {ex.Message}\n\n프로젝트 탐색기의 3D 뷰에서 '{ZoneAssigner.CheckViewName}'을 열어 주세요.");
            }
        }
        else if (pressed == TaskDialogResult.CommandLink2 && schedule != null)
        {
            try { uidoc.ActiveView = schedule; }
            catch (Exception ex) { Dialogs.Show(Title, $"일람표를 열지 못했습니다: {ex.Message}\n\n프로젝트 탐색기의 일람표에서 '{FormworkObjects.ScheduleName}'을 열어 주세요."); }
        }
    }

    /// <summary>부재별 결과표(TSV — 닫을 때의 상태)와 이번 창의 일지.</summary>
    private static (string table, string journal) WriteLogs(Document doc, PickEditor? editor, Session session, bool kept, bool revert)
    {
        string dir = LogDir();
        string name = SafeName(doc.Title);
        string journal = Path.Combine(dir, name + "_place_session.log");
        session.Note($"끝\t모델에 남음 {(kept ? "O" : "X")}\t처음 상태로 {(revert ? "O" : "X")}\t커밋 {session.Commits}");
        File.AppendAllText(journal, session.LogText + Environment.NewLine, new UTF8Encoding(true));

        string table = Path.Combine(dir, name + "_zones.tsv");
        if (editor == null) return ("(만들지 않음)", journal);
        // 값은 닫을 때 화면이 알고 있던 것(되읽은 값)이다. 처음 상태로 되돌렸거나 남기지 못했으면 모델의 값은 열기 전과 같다.
        var sb = new StringBuilder("id\t카테고리\t유형\t위치\t부위\t위치없음\t위치확인\t부위확인\t상태\t고칠수없음\n");
        foreach (var m in editor.Members)
        {
            string state = !m.Drawn ? "그리지 못함" : m.State switch
            {
                PickState.Done => "끝", PickState.Todo => "아직", PickState.Partial => "하나만", PickState.Outside => "확인 필요(창 밖에서 들어온 값)", _ => "고칠 수 없음",
            };
            sb.Append(string.Join("\t", m.Id.ToString(CultureInfo.InvariantCulture), Cell(m.Category), Cell(m.Type), Cell(m.Value.Zone), Cell(m.Value.Part),
                m.Value.Free ? "O" : "", m.Value.ZoneOk ? "O" : "", m.Value.PartOk ? "O" : "", state, Cell(m.Locked))).Append('\n');
        }
        sb.Append('\n');
        sb.Append(kept ? "모델에 남음\n" : revert ? "처음 상태로 되돌림 — 위의 값은 모델에 남지 않았다\n" : "모델에 남지 않음 — 위의 값은 창을 닫을 때 화면의 값이다\n");
        sb.Append("구조물\t" + editor.Structure + "\n");
        sb.Append("위치 목록\t" + string.Join(" · ", editor.Places.Select(p => p.Name)) + "\n");
        sb.Append("부위 목록\t" + string.Join(" · ", editor.Parts.Select(p => p.Name + (p.NoZone ? "(위치 없이)" : "") + (p.Plain ? "(무근)" : "") + (PartCatalog.IsLater(p) ? "(나중에 침)" : "") + $"[겹친 자리 {PartCatalog.RankOf(p)}]")) + "\n");
        File.WriteAllText(table, sb.ToString(), new UTF8Encoding(true));
        try
        {
            string hist = Path.Combine(dir, "history");
            Directory.CreateDirectory(hist);
            File.Copy(table, Path.Combine(hist, $"{name}_zones_{DateTime.Now:yyyyMMdd_HHmmss}.tsv"), true);
        }
        catch { }
        return (table, journal);
    }

    private static string LogDir()
    {
        string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DH.Takeoff", "log");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static string SafeName(string name)
    {
        foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        return name;
    }

    private static string Cell(string s) => s.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
}
