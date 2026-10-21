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
///  · 넣을 때마다 트랜잭션 하나를 커밋한다. 창이 떠 있는 동안 트랜잭션 그룹을 열어 두고, 닫을 때 하나로 묶는다(Assimilate) —
///    Revit 의 실행 취소 한 번으로 이 창에서 한 일(부위·중분류 · [완료]의 산출식 · 거푸집 속성)이 모두 돌아간다.
///    [처음 상태로 되돌리고 닫기]는 그룹을 되돌리고(RollBack), 트랜잭션 밖인 거푸집 대응표 파일도 열 때로 되돌린다.
///  · 그룹을 닫지 못하면 Revit 이 나중에 전부 되돌린다(문서) — 그래서 그룹을 연 뒤의 모든 길은 finally 에서 닫는다.
///  · 그룹이 Assimilate/Commit 으로 닫혔으면(모델에 남았으면) 무슨 일이 있어도 Result.Succeeded 를 돌려준다.
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
        var doc = uidoc.Document;
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

        // 창이 떠 있는 동안 여는 그룹: 이 문서를 지금 고칠 수 없으면(읽기 전용 등) 3D 를 띄우기 전에 여기서 드러난다.
        var group = new TransactionGroup(doc, "DH 수량산출");
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
            Dialogs.Show(Title, "이 문서는 지금 고칠 수 없어 「수량산출」을 열지 않았습니다.\n" + ex.Message);
            return Result.Cancelled;
        }

        PickEditor? editor = null;
        bool revert = false, kept = false;
        string crash = "", closeProblem = "", paintProblem = "";
        View3D? checkView = null;
        Completion? completion = null;
        var legend = new Dictionary<string, string>();
        try
        {
            editor = new PickEditor(data.Members, data.Levels, data.Stored.Plan, session.Write, () => ZoneScene.Reread(doc, data.Members), doc.Title);
            session.Note($"목록\t구조물 {editor.Structure}({(PartCatalog.FindStructure(data.Stored.Plan.Structure) != null ? "저장된 것" : "제목으로 알아냄")})" +
                         $"\t중분류 {editor.Places.Count} · 부위 {editor.Parts.Count}\t저장된 판 v{data.Stored.Plan.ReadVersion}{(data.Stored.Exists ? "" : "(저장된 것 없음)")}");
            // Revit 에서 부재를 골라 놓고 눌렀으면 팝업에서도 그 부재를 골라 둔다.
            editor.Select(uidoc.Selection.GetElementIds().Select(id => id.Value));
            // ① 을 열기 전에 한 번 빠르게 모델 검증(최대 15 초 남짓 · 알림만 — R54).
            session.ModelCheck = QuickModelCheck(doc, session);
            session.ModelCheckRanks = RankSignature(editor);
            (revert, completion) = ShowWindow(commandData.Application.MainWindowHandle, doc, editor, data, session, Notices(data));

            if (!revert && session.Commits > 0)
            {
                // 확인용 Revit 뷰에도 위치 색을 입힌다(그룹 안 — 같은 실행 취소 한 줄에 들어간다). 못 입혀도 넣은 값은 남는다.
                try
                {
                    var zoneOf = new Dictionary<ElementId, string>();
                    foreach (var m in editor.Members) zoneOf[new ElementId(m.Id)] = m.Value.Zone;
                    checkView = ZoneAssigner.Paint(doc, zoneOf, editor.Places.Select(p => p.Name).ToList(), out legend, out paintProblem);
                    if (checkView != null) session.Committed();
                }
                catch (Exception ex) { paintProblem = "색을 입히지 못했습니다: " + ex.Message; }
            }
        }
        catch (Exception ex)
        {
            crash = ex.Message;
            session.Note("오류\t" + ex);
        }
        finally
        {
            // 되돌리기가 아니면 늘 하나로 묶어 남긴다(빈 묶음을 합쳐도 해가 없다) — 커밋을 세다 하나를 놓쳐도(적은 직후 되읽다 오류 등)
            // 적은 것이 말없이 되돌려지지 않게(revit R42 중간 ②). '적은 것이 있는가'는 알림을 띄울지 가를 때만 따로 본다(wrote).
            (kept, closeProblem) = CloseGroup(group, keep: !revert, session);
            try { group.Dispose(); } catch { }
        }
        bool wrote = kept && session.Commits > 0;

        // ★ 이 아래에서 kept 가 참이면(모델에 남았으면) 무슨 일이 있어도 Succeeded 를 돌려준다.
        //   Failed/Cancelled 를 돌려주면 Revit 이 방금 모델에 적은 것까지 전부 되돌린다.
        // 산출식은 [완료]가 창 안에서(같은 그룹 안에서) 채운다 — 전처럼 [닫기] 뒤에 이어서 돌지 않는다
        // (JACK 2026-10-15: [닫기] = 그냥 나가기 · [완료] = 산출식 → 엑셀).
        var follow = completion?.Formulas;
        // [완료]의 엑셀: 그룹이 모델에 남았을 때만 쓴다(revit R42 A1). 남지 못했으면 쓰지 않고 알린다 — 되돌아간 모델과 어긋난 산출근거가 남지 않게.
        if (completion?.Ready == true)
        {
            if (kept)
            {
                try { SaveExcel(completion, session); }
                catch (Exception ex) { completion.ExcelProblem = ex.Message; session.Note("완료\t엑셀 저장 오류\t" + ex); }
            }
            else
            {
                session.Note("완료\t그룹이 모델에 남지 않아 엑셀을 쓰지 않음");
                closeProblem += (closeProblem.Length > 0 ? "\n" : "") + "[완료]의 산출근거 엑셀은 쓰지 않았습니다(넣은 것이 모델에 남지 않아서).";
            }
        }
        session.Note("돌려주는 값: " + (kept ? "Succeeded" : crash.Length > 0 || closeProblem.Length > 0 ? "Failed" : "Cancelled") +
                     $"(모델에 남음 {(kept ? "O" : "X")} · 완료 {(completion?.Saved == true ? "O(엑셀 저장)" : completion?.Ready == true ? "엑셀을 쓰지 못함" : completion != null ? "눌렀지만 엑셀 저장 안 함" : "X")})");

        string table = "", journal = "";
        try { (table, journal) = WriteLogs(doc, editor, session, kept, revert); }
        catch (Exception ex) { table = "(쓰지 못했습니다 — 결과표가 엑셀에 열려 있으면 닫아 주세요: " + ex.Message + ")"; }
        try
        {
            if (completion?.Ready == true && kept)
                ReportDone(commandData, editor, session, completion, closeProblem, paintProblem, checkView, legend, table, journal);
            // 알림 창은 적은 것이 있을 때(wrote)만 '남았다'로 쓴다 — 보기만 하고 닫은 창은 전처럼 말없이 닫는다(묶음은 늘 남기므로 kept 는 늘 참이다).
            else Report(commandData, editor, session, wrote, revert, crash, closeProblem, paintProblem, checkView, legend, table, journal, follow, completion);
        }
        catch { /* 알림 창을 못 띄워도 결과는 바뀌지 않는다 */ }

        if (kept) return Result.Succeeded;
        if (crash.Length > 0 || closeProblem.Length > 0) { message = crash.Length > 0 ? crash : closeProblem; return Result.Failed; }
        return Result.Cancelled;
    }

    // [완료]로 닫았을 때의 알림: 엑셀 파일 · 산출식 · 산출식 확인 창에서 한 일 · 부위·중분류. [엑셀 파일 열기]가 첫 단추다.
    private static void ReportDone(ExternalCommandData commandData, PickEditor? editor, Session session, Completion done, string closeProblem, string paintProblem,
                                   View3D? view, Dictionary<string, string> legend, string table, string journal)
    {
        var uidoc = commandData.Application.ActiveUIDocument;
        var plan = done.Plan;
        var text = new StringBuilder();
        if (closeProblem.Length > 0) text.AppendLine(closeProblem).AppendLine();
        if (done.Overlap.Length > 0) text.AppendLine("⚠ " + done.Overlap + " — [모델 검증]으로 고친 뒤 다시 [수량산출]을 하면 맞는 수량입니다.").AppendLine();
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
            text.AppendLine($"사람이 뺀 거푸집 판: {done.Dropped}장 · {done.DroppedArea.ToString("#,##0.00", CultureInfo.InvariantCulture)} ㎡ — 엑셀 · 거푸집 객체에는 넣지 않음 · 부재 속성(지침용) 합계에는 들어 있음");
        if (done.LostDrops > 0)
            text.AppendLine($"★ 모양이 바뀌어 자리를 찾지 못한 뺀 판 {done.LostDrops}장 — 그 자리는 다시 셌습니다(② 3D 에서 확인)");
        if (done.ChangedDrops > 0)
            text.AppendLine($"★ 모양이 바뀐 뺀 판 {done.ChangedDrops}장 — 일부 조각만 찾았거나 빠진 넓이가 뺄 때와 다릅니다(② 3D 에서 확인)");
        if (session.LaterCuts > 0) text.AppendLine("⚠ " + LaterCutsNote(session.LaterCuts));
        if (done.Objects is { Committed: true } plates && done.Objects.Blocked.Length == 0)
        {
            text.AppendLine($"거푸집 객체: 판 {plates.Created}개(일반 모델 · 12 mm)" + (plates.Deleted > 0 ? $" · 전 판 {plates.Deleted}개 지움" : "") +
                            (plates.Failed.Count > 0 ? $" · ★ 못 만든 판 {plates.Failed.Count}개(일지)" : "") + " — 납품 전에 리본 [거푸집 › 거푸집 삭제]");
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
        text.AppendLine(closeProblem.Length == 0
            ? "이 창에서 넣은 것(부위·중분류 · 산출식 · 거푸집 속성 · 거푸집 객체)은 Revit 의 실행 취소(Ctrl+Z) 한 번으로 모두 되돌릴 수 있습니다."
            : "이 창에서 넣은 것은 Revit 의 실행 취소(Ctrl+Z)를 여러 번 눌러야 되돌아갑니다.");
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
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, $"확인용 3D 뷰 열기 ('{ZoneAssigner.CheckViewName}')", "콘크리트 부재를 중분류별 색으로 칠해 둔 Revit 뷰입니다(거푸집 객체는 숨김).");
        var schedule = done.Objects is { Committed: true, ScheduleId: { } scheduleId } ? uidoc.Document.GetElement(scheduleId) as View : null;
        if (schedule != null)
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, $"거푸집 객체 일람표 열기 ('{FormworkObjects.ScheduleName}')", "판마다 종류 · 부위 · 중분류 · 안팎 · 산출식 · 넓이 · 원 부재 Id");
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
        if (problems.Count > 0)
        {
            lines.Add("");
            lines.AddRange(problems.Distinct());
        }
        return string.Join("\n", lines);
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
        public string NamesProblem = "";           // 부재 이름을 다 붙이지 못한 까닭("" = 없음)
        public CalcPlan? Plan;
        public XlsxBook? Book;              // 지은 엑셀(쓰기는 창을 닫아 그룹이 모델에 남은 뒤에 — revit R42 A1)
        public string ExcelTarget = "";     // 고른 저장 위치("" = 고르지 않았다)
        public string ExcelPath = "";       // 실제로 저장한 파일("" = 아직/못 했다)
        public string ExcelProblem = "";    // 쓰지 못한 까닭("" = 없음)
        public string Overlap = "";         // 겹친 부재 알림("" = 없음)
        public int Dropped;                 // ② 3D 에서 사람이 뺀 거푸집 판(R48 — 엑셀 · 거푸집 객체에 넣지 않음)
        public double DroppedArea;          // 그 넓이(㎡)
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
    // 모델에 적는 것은 모두 창이 떠 있는 동안의 트랜잭션 그룹 안에서 적는다(창을 닫을 때 하나로 묶인다 — Ctrl+Z 한 번).
    private static Completion Complete(Window window, PlacePartView view, PickEditor editor, Document doc, Session session, bool dark, Plates plates)
    {
        // 이 창에서 이미 지은 판(앞의 [완료])으로 시작한다 — 이번 [완료]가 판을 짓기 전에 멈춰도 [닫기] 알림에 판이 나오게(revit-verify R44b L-a).
        var done = new Completion { Objects = plates.Built };
        session.Note("완료\t시작");
        // 넣은 뒤 모양이 바뀌었는데 확인하지 않은 부재(R52b — JACK 2026-10-19 인터뷰 «알리고 진행»): 묻고, 계속하면 지금 값으로 만든다(엑셀 «부재 목록»에 남는다).
        // 고칠 수 없는 부재 · 그려지지 않은 부재도 센다 — 확인할 수는 없어도 산출서에는 실린다(R52b core-verify 중간 4).
        // ②-0 보이드(JACK 2026-10-21 «구멍 내는 보이드는 처음부터 빼기 · 이미 적힌 값도 지워줘»): 대상에서 빠진 보이드에 전에 적은 값을 지운다(창의 그룹 안 — Ctrl+Z 한 줄).
        string voidProblem = view.RunLocked(() => done.VoidsWiped = WipeVoids(doc, session));
        if (voidProblem.Length > 0) session.Note("완료\t보이드 값 지우기 오류\t" + voidProblem);
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
            if (go != MessageBoxResult.Yes) return done;
        }
        FormulaFollow? follow = null;
        string problem = view.RunLocked(() => follow = FollowWithFormulas(doc, session, count => AskCompute(window, count)));
        done.Formulas = follow ?? new FormulaFollow { Problem = problem };
        if (problem.Length > 0 && done.Formulas.Problem.Length == 0) done.Formulas.Problem = problem;
        if (done.Formulas.Plan.MayHaveCommitted) session.Committed();
        if (done.Formulas.Problem.Length > 0)
        {
            view.ShowMessage("산출식을 채우지 못했습니다(엑셀은 저장하지 않았습니다): " + done.Formulas.Problem, true);
            return done;
        }
        if (done.Formulas.Plan.Declined)
        {
            view.ShowMessage("산출식을 지금 만들지 않기로 해 엑셀은 저장하지 않았습니다.", true);
            return done;
        }
        if (done.Formulas.RolledBack)
        {
            view.ShowMessage($"산출식이 모델에 저장되지 않았습니다(Revit 이 변경을 되돌림: {done.Formulas.Plan.Status}) — 엑셀은 저장하지 않았습니다.", true);
            return done;
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
            if (done.Dropped + done.LostDrops > 0)
                session.Note(FormattableString.Invariant($"완료\t사람이 뺀 거푸집 판 {done.Dropped}장({done.DroppedArea:0.###} ㎡) · 모양이 바뀌어 다시 센 뺀 판 {done.LostDrops}장 · 모양이 바뀐 뺀 판 {done.ChangedDrops}장"));
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

            // 거푸집 객체(R44): 시트를 짓지 못했으면 만들지 않는다(판은 엑셀 줄과 같은 단위다). 못 만들어도 엑셀은 이어서 만든다(알림 창에 까닭).
            if (done.Sheet is { } sheet) BuildPlates(doc, view, session, board, sheet, plates, done);
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
            return done;
        }
        var plan = done.Plan = read.Plan;
        if (plan.Rows.Count == 0)
        {
            view.ShowMessage("산출근거에 실을 식이 하나도 없습니다 — 엑셀은 저장하지 않았습니다.", true);
            return done;
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
            return done;
        }
        string busy = Writable(dlg.FileName);
        if (busy.Length > 0)
        {
            session.Note("완료\t그 파일을 쓸 수 없음\t" + busy);
            view.ShowMessage("그 파일에 쓸 수 없습니다(같은 파일이 엑셀에 열려 있으면 닫고 다시 [완료]를 눌러 주세요): " + busy, true);
            return done;
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
        return done;
    }

    // 이 창에서 지은 거푸집 객체 — [완료]를 다시 눌러도(엑셀 저장을 그만둔 뒤 등) 같은 판을 통째로 다시 짓지 않게 한다(큰 모델은 오래 걸린다).
    private sealed class Plates
    {
        public FormworkBoard? Board;                  // 판을 지을 때의 거푸집 판(다시 재면 새 판이라 다시 짓는다)
        public string Key = "";                       // 그때의 줄 조각(KeyOf)
        public FormworkObjects.Outcome? Built;        // 그때 지은 결과(커밋된 것만)
    }

    // 거푸집 객체를 짓는다(R44): 전 판은 지우고 새로 — 이 창의 그룹 안이라 Ctrl+Z 한 번. 이 창에서 이미 지은 판과 거푸집 판 · 줄 조각
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
        string head = $"완료\t거푸집 객체\t시트 줄 {sheet.Lines.Count}(× n 합 {sheet.Lines.Sum(l => l.Count)} · 슬래브 밑면 한 줄 {slabLines.Count}(칸 {slabLines.Sum(l => l.Units.Count)}))" +
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
            return parts.Count > 0 ? "모델 검증: " + string.Join(" · ", parts) : "";
        }
        catch (Exception ex)
        {
            session.Note("모델 검증(빠른)\t오류\t" + ex);
            return "모델 검증을 하지 못함(" + ex.Message + ")";
        }
    }

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
            text.AppendLine(plan.MayHaveCommitted ? "  (일부는 모델에 적혔을 수 있습니다 — [수량산출]의 [완료]를 다시 눌러 맞춰 주세요.)"
                                                  : "  ([수량산출]의 [완료]를 다시 눌러 주세요.)");
            return;
        }
        if (plan.Declined)
        {
            text.AppendLine($"산출식은 나중에 하기로 했습니다(식을 새로 만들 부재 {plan.Recompute}개) — [수량산출]의 [완료]를 다시 눌러 주세요.");
            return;
        }
        if (follow.RolledBack)
        {
            text.AppendLine($"★ 산출식이 모델에 저장되지 않았습니다(Revit 이 변경을 되돌림: {plan.Status}). [수량산출]의 [완료]를 다시 눌러 주세요.");
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

    // 거푸집 대응표 파일(트랜잭션 밖 — 그룹을 되돌려도 돌아가지 않는다): 창을 열 때의 글을 떠 두고, [처음 상태로 되돌리고 닫기]에서 되돌린다.
    private sealed class RulesFile
    {
        public required string Path;
        public bool Existed;
        public string? Before;          // 열 때의 글(읽지 못했으면 null — 그때는 되돌리지 않는다)
        public bool Saved;              // 이 창에서 한 번이라도 적었다

        public static RulesFile Snapshot(string path)
        {
            var file = new RulesFile { Path = path };
            try
            {
                file.Existed = File.Exists(path);
                file.Before = file.Existed ? File.ReadAllText(path, Encoding.UTF8) : "";
            }
            catch { file.Before = null; }
            return file;
        }

        /// <summary>열 때로 되돌린다("" = 됨 · 할 것이 없음, 아니면 까닭).</summary>
        public string Restore()
        {
            if (!Saved) return "";
            if (Before == null) return "열 때의 대응표 파일을 읽지 못해 되돌리지 않았습니다";
            try
            {
                if (Existed) File.WriteAllText(Path, Before, new UTF8Encoding(true));
                else if (File.Exists(Path)) File.Delete(Path);
                return "";
            }
            catch (Exception ex) { return ex.Message; }
        }
    }

    // 나중에 치는 부재(중간슬래브 · 트라프 · 헌치 · 받침 …)가 결합에서 구조체를 깎은 곳의 알림 — ② 위 띠와 [완료] 알림 창이 같이 쓴다.
    private static string LaterCutsNote(int count) =>
        $"나중에 치는 부재가 구조체를 깎은 결합 {count}곳 — 그 자리 벽이 끊기고 홈 둘레만큼 더 셀 수 있습니다. [모델 검증]의 «작성오류»에서" +
        "([기준대로 바꾸기] 또는 [모두 기준대로]) 구조체가 자르게 바뀝니다(그 뒤 다시 [수량산출]).";

    // ② 의 상태를 짓는다: 모델의 콘크리트 전부(산출식 · 거푸집 면 확인과 같은 거르기)를 재고, 대응표를 건다.
    // 대응표: 앞서 ② 에서 고친 것이 있으면 그것(이 창의 메모리 — 파일 저장에 실패했어도 고친 종류가 사라지지 않게, revit R42 L2), 없으면 파일.
    // 곡면은 Revit 이 나눠 준 세모를 붙인다(Core 는 곡면의 세모를 만들지 못한다). notice = 창에 알릴 것("" = 없음).
    // 나중에 치는 부재(받침 · PAD · 디딤판 — 부위 목록의 '나중에 침')는 ① 의 지금 목록으로 정한다: ① 에서 표시를 바꾸면 목록이 모델에 적혀
    // editor.Writes 가 늘고, [다음 단계]에서 다시 잰다.
    private static FormworkBoard BuildBoard(Document doc, ZoneScene.Data data, string rulesPath, FormworkRules? current, Session session, PickEditor editor, out string notice)
    {
        var watch = Stopwatch.StartNew();
        var later = editor.Parts.Where(PartCatalog.IsLater).Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
        // 겹친 자리 순위도 ① 의 지금 목록으로(R54 — revit-verify 계획 M3: 순위 출처 하나 · 수량산출은 ① 의 메모리 값).
        var scene = FormworkReader.Read(doc, Array.Empty<ElementId>(), later.Contains, new DeductRank.Table(editor.Parts));
        string rulesProblem = "";
        FormworkRules rules;
        if (current != null) rules = FormworkRules.Parse(current.ToText());     // 사본 — 옛 판과 같은 표를 나눠 쓰지 않게
        else
        {
            try { rules = FormworkRules.Load(rulesPath); }
            catch (Exception ex) { rules = new FormworkRules(); rulesProblem = ex.Message; }
        }
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
            $" · 수직면 덩어리 {FormworkAnalyzer.LastGrouping.groups}(한 고리 아니라 안 묶음 {FormworkAnalyzer.LastGrouping.refused} · {FormworkAnalyzer.LastGrouping.ms} ms)" +
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
        var notes = new List<string>();
        if (rulesProblem.Length > 0) notes.Add("대응표 파일을 읽지 못해 초안 규칙으로 셉니다: " + rulesProblem);
        // 나중 부재가 구조체를 깎은 결합: 구조체 면에 홈이 남아 벽이 전체 높이가 되지 않는다 — 할 일을 적는다(revit-verify R46 M1).
        session.LaterCuts = scene.LaterCuts.Count;
        if (scene.LaterCuts.Count > 0) notes.Add(LaterCutsNote(scene.LaterCuts.Count));
        if (audit.PolygonMismatch + audit.SumMismatch + audit.PatchMismatch > 0) notes.Add("검산이 맞지 않는 면이 있습니다 — 일지를 보내 주세요.");
        int unread = scene.ReadErrors + scene.NeighbourReadErrors;
        if (unread > 0) notes.Add($"형상을 읽지 못한 부재 {unread}개와 맞닿은 자리는 빼지 못했습니다.");
        if (unpainted > 0) notes.Add($"색을 칠하지 못한 곡면 {unpainted}개(넓이는 셉니다).");
        if (board.LostDrops.Count > 0) notes.Add($"전에 뺀 판 {board.LostDrops.Count}장의 자리를 이번 모양에서 찾지 못해 다시 셉니다 — 필요하면 다시 찍어 빼세요.");
        if (board.DropCheck.Changed > 0) notes.Add($"전에 뺀 판 {board.DropCheck.Changed}장은 모양이 바뀌었습니다(일부 조각만 찾음 · 넓이가 달라짐) — ② 에서 회갈색 판을 확인하세요.");
        notice = string.Join(" · ", notes);
        return board;
    }

    // 팝업을 띄운다. revert = [처음 상태로 되돌리고 닫기]를 골랐다 · completion = [완료]를 누른 결과(누르지 않았으면 null — 엑셀을 저장하면 창이 닫힌다).
    private static (bool revert, Completion? completion) ShowWindow(nint owner, Document doc, PickEditor editor, ZoneScene.Data data, Session session, string notice)
    {
        bool dark = false;
        try { dark = UIThemeManager.CurrentTheme == UITheme.Dark; } catch { }
        var view = new PlacePartView(editor, data.Meshes, dark, notice) { Log = session.Note, ModelCheck = session.ModelCheck };
        var window = view.BuildWindow();
        new WindowInteropHelper(window).Owner = owner;
        bool revert = false;
        Completion? completion = null;
        var rules = RulesFile.Snapshot(FormworkReader.RulesPath(doc));
        var plates = new Plates();
        view.Close += () => window.Close();
        view.Finish += () =>
        {
            if (view.Busy) return;
            // [완료] 전체를 '처리 중'으로 묶는다: 그 안의 작은 창(산출식 확인 · 저장 위치 · 물음)이 도는 틈에 쌓여 있던 누르기가
            // [완료] · [다음 단계] · [이전 단계] · [닫기] · 되돌리기를 또 돌리지 않게(revit R42 중간 ① — 창의 Busy 가 이 표시까지 본다).
            view.Held = true;
            try { completion = Complete(window, view, editor, doc, session, dark, plates); }
            finally { view.Held = false; }
            if (completion.Ready) window.Close();
        };
        view.RevertAll += () =>
        {
            if (view.Busy) return;
            if (session.Commits == 0 && !rules.Saved) { view.ShowMessage("이 창에서 모델에 적거나 고친 것이 없어 되돌릴 것이 없습니다."); return; }
            var answer = MessageBox.Show(window,
                $"이 창을 연 뒤에 넣거나 고친 것을 모두 없던 일로 하고 닫을까요?\n(모델에 적은 것 {editor.Writes}번" + (rules.Saved ? " · 거푸집 대응표에서 고친 것" : "") +
                (plates.Built is { Committed: true } built ? $" · [완료]에서 만든 거푸집 객체 {built.Created}개" : "") + "이 모두 되돌려집니다.)",
                Title, MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
            if (answer != MessageBoxResult.Yes) return;
            revert = true;
            window.Close();
        };

        // ② 거푸집면 설정: [다음 단계]를 누를 때 모델의 콘크리트 전부를 잰다. ① 에서 부위를 바꾸면(버림 콘크리트 등) 거푸집이 달라질 수 있어,
        // 그 사이 모델에 적은 것이 있으면 다시 잰다(대응표는 파일에서 다시 읽는다 — ② 에서 고친 것은 이미 파일에 있다).
        FormworkBoard? board = null;
        int boardAt = -1;
        view.Next += () =>
        {
            if (board == null || boardAt != editor.Writes)
            {
                FormworkBoard? made = null;
                string note = "";
                string problem = view.RunLocked(() => made = BuildBoard(doc, data, rules.Path, board?.Rules, session, editor, out note));
                if (made == null)
                {
                    session.Note("거푸집\t재지 못함\t" + problem);
                    view.ShowMessage("거푸집 면을 재지 못했습니다(① 의 값은 그대로입니다): " + problem, true);
                    return;
                }
                board = made;
                boardAt = editor.Writes;
                view.ShowFormwork(board);
                if (note.Length > 0) view.ShowMessage(note, true);
            }
            else view.ShowFormwork(board);
            session.Note("걸음\t② 거푸집면 설정");
        };
        view.Back += () => session.Note("걸음\t① 부위 · 중분류");
        view.SaveRules = () =>
        {
            var now = view.Board?.Rules;
            if (now == null) return "";
            try
            {
                now.Save(rules.Path);
                rules.Saved = true;
                session.Note($"대응표 적음\t고친 줄 {now.Count} · 뺀 판 {now.DropCount}\t{rules.Path}");
                return "";
            }
            catch (Exception ex)
            {
                session.Note("대응표 적지 못함\t" + ex.Message);
                return ex.Message;
            }
        };
        // 모델에 적는 중(트랜잭션이 열려 있다)에는 창을 닫지 못한다.
        window.Closing += (_, e) => { if (view.Busy) e.Cancel = true; };
        // 창이 떠 있는 동안 처리기에서 새는 오류: 그냥 두면 ShowDialog 밖으로 튀어 창이 닫힌다 — 창에 적고 머문다.
        System.Windows.Threading.DispatcherUnhandledExceptionEventHandler onError = (_, e) =>
        {
            try
            {
                // 우리 처리기에서 난 오류인지는 호출 기록으로 가린다(싸여 온 예외의 안쪽 기록까지 본다 — ToString).
                bool ours = e.Exception.ToString().Contains("DH.Takeoff", StringComparison.Ordinal);
                session.Note("창의 오류\t" + e.Exception);
                if (ours) view.RefreshAll();
                view.ShowMessage((ours ? "창에서 오류가 났습니다(넣은 값은 모델에 그대로 있습니다): " : "이 창이 떠 있는 동안 다른 곳에서 오류가 났습니다: ") + e.Exception.Message, true);
            }
            catch { }
            e.Handled = true;
        };
        window.Dispatcher.UnhandledException += onError;
        var shown = Stopwatch.StartNew();
        window.ContentRendered += (_, _) => session.Note($"창이 그려짐\t{shown.ElapsedMilliseconds} ms\t소프트웨어로만 그림 {(RenderOptions.ProcessRenderMode == RenderMode.SoftwareOnly ? "O" : "X")}");
        try { window.ShowDialog(); }
        finally { window.Dispatcher.UnhandledException -= onError; }
        if (revert)
        {
            string restored = rules.Restore();
            if (rules.Saved) session.Note("대응표 되돌림\t" + (restored.Length == 0 ? "됨" : restored));
        }
        session.Note($"창 닫힘\t적은 횟수 {editor.Writes}\t처음 상태로 {(revert ? "O" : "X")}\t걸음 {view.Step + 1}\t대응표 고침 {(rules.Saved ? "O" : "X")}\t완료 {(completion?.Saved == true ? "O" : "X")}");
        return (revert, completion);
    }

    private static void Report(ExternalCommandData commandData, PickEditor? editor, Session session, bool kept, bool revert, string crash, string closeProblem,
                               string paintProblem, View3D? view, Dictionary<string, string> legend, string table, string journal, FormulaFollow? follow, Completion? done)
    {
        var uidoc = commandData.Application.ActiveUIDocument;
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
            text.AppendLine(closeProblem.Length == 0
                ? "이 창에서 넣은 것은 Revit 의 실행 취소(Ctrl+Z) 한 번으로 모두 되돌릴 수 있습니다."
                : "이 창에서 넣은 것은 Revit 의 실행 취소(Ctrl+Z)를 넣은 횟수만큼 눌러야 되돌아갑니다.");
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
            text.AppendLine().AppendLine($"거푸집 객체: 판 {plates.Created}개(일반 모델 · 12 mm — 엑셀은 저장하지 않았습니다)" +
                                         (plates.Failed.Count > 0 ? $" · ★ 못 만든 판 {plates.Failed.Count}개(일지)" : "") + " — 납품 전에 리본 [거푸집 › 거푸집 삭제]");
        else if (kept && done != null && done.ObjectsProblem.Length > 0)
            text.AppendLine().AppendLine("★ 거푸집 객체를 만들지 못했습니다: " + done.ObjectsProblem);
        if (paintProblem.Length > 0) text.AppendLine().AppendLine(paintProblem);
        text.AppendLine();
        text.AppendLine("결과표: " + table);
        text.Append("일지: " + journal);
        td.MainContent = text.ToString();
        if (view != null && kept)
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, $"확인용 3D 뷰 열기 ('{ZoneAssigner.CheckViewName}')",
                "콘크리트 부재를 중분류별 색으로 칠해 둔 Revit 뷰입니다(팝업과 같은 색 차례 — 끝났는지는 팝업에서 봅니다 · 거푸집 객체는 숨김).");
        var schedule = kept && done?.Objects is { Committed: true, ScheduleId: { } scheduleId } ? uidoc.Document.GetElement(scheduleId) as View : null;
        if (schedule != null)
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, $"거푸집 객체 일람표 열기 ('{FormworkObjects.ScheduleName}')", "판마다 종류 · 부위 · 중분류 · 안팎 · 산출식 · 넓이 · 원 부재 Id");
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
