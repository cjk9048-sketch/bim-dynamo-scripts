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

namespace DH.Takeoff.Revit.Commands;

/// <summary>
/// "위치·부위 정하기" 버튼(JACK 2026-10-13) — 팝업의 3D 그림에서 부재를 찍어(또는 끌어) 고르고, 왼쪽 판에서 위치와 부위를 누르면
/// 바로 부재의 속성(DH_Zone · DH_Part)에 적는다. 값이 들어간 부재만 색이 바뀌고, 색이 안 바뀐 부재가 없을 때까지 한다.
///  · 구역 네모 방식(10/12)은 없앴다: 상수도 구조물은 모양이 제각각이라 네모로 가르기보다 직접 찍는 쪽이 맞다.
///  · 넣을 때마다 트랜잭션 하나를 커밋한다. 창이 떠 있는 동안 트랜잭션 그룹을 열어 두고, 닫을 때 하나로 묶는다(Assimilate) —
///    Revit 의 실행 취소 한 번으로 이 창에서 한 일이 모두 돌아간다. [처음 상태로 되돌리고 닫기]는 그룹을 되돌린다(RollBack).
///  · 그룹을 닫지 못하면 Revit 이 나중에 전부 되돌린다(문서) — 그래서 그룹을 연 뒤의 모든 길은 finally 에서 닫는다.
///  · 그룹이 Assimilate/Commit 으로 닫혔으면(모델에 남았으면) 무슨 일이 있어도 Result.Succeeded 를 돌려준다.
///  · 산출식을 이어서(JACK 2026-10-14): 남은 부재가 0 이 되어 닫을 때만, 그룹을 닫은 뒤에, 필요한 부재만 —
///    식이 없거나 틀려진 부재('다시 계산 필요' 포함)만 다시 적고 지금도 맞는 식은 건드리지 않는다(FormulaAutoFill.PlanRefresh).
///    산출식 트랜잭션은 위치·부위의 그룹과 따로다(실행 취소 줄도 따로). 이 단계에서 무엇이든 커밋됐으면 Succeeded 를 돌려준다.
///    이 단계의 오류는 여기서 잡는다 — 위치·부위에 넣은 값은 그대로 남는다.
/// </summary>
[Transaction(TransactionMode.Manual)]
public sealed class AssignZoneCommand : IExternalCommand
{
    private const string Title = "DH 수량산출 — 위치·부위";
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
            using (var tx = new Transaction(_doc, "DH 위치·부위"))
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
            Dialogs.Show(Title, "이 문서는 읽기 전용이라 「위치·부위 정하기」를 열지 않았습니다(넣는 즉시 모델에 적는 창입니다).");
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
            Dialogs.Show(Title, "위치·부위를 정할 콘크리트 부재가 없습니다.\n(대상: 기둥 · 보 · 벽 · 바닥 · 기초 · 일반 모델 가운데 콘크리트 부재)");
            return Result.Cancelled;
        }
        // 더 새로운 판의 애드인이 저장한 목록: 이 판이 모르는 칸이 있을 수 있어, 열어서 다시 적으면 그 자료가 망가진다 — 열지 않는다.
        if (data.Stored.TooNew)
        {
            Dialogs.Show(Title, "이 모델의 위치·부위 목록은 더 새로운 판의 DH 수량산출이 저장한 것이라, 이 판으로는 열지 않습니다(열어서 다시 적으면 그 자료가 망가집니다).\n" +
                                "애드인을 새 판으로 올린 뒤 다시 눌러 주세요.\n\n" + data.Stored.Problem);
            return Result.Cancelled;
        }
        // 저장된 목록을 읽지 못했다: 조용히 빈 것으로 시작하면 목록을 고칠 때 옛 자료를 덮어쓴다 — 먼저 묻는다.
        if (data.Stored.Problem.Length > 0)
        {
            var ask = Dialogs.New(Title);
            ask.MainInstruction = "모델에 저장돼 있는 위치·부위 목록을 읽지 못했습니다";
            ask.MainContent = data.Stored.Problem + "\n\n기본 목록으로 시작할 수 있습니다. 그렇게 열어 목록을 고치면 저장돼 있던 목록을 덮어씁니다(부재에 적힌 위치·부위 값은 그대로 보입니다).";
            ask.AllowCancellation = true;
            ask.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "기본 목록으로 시작");
            ask.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "그만두기");
            if (ask.Show() != TaskDialogResult.CommandLink1) return Result.Cancelled;
        }

        var session = new Session(doc, data);
        session.Note($"열기\t{doc.Title}\t부재 {data.Members.Count} · 세모 {data.Triangles} · 모서리 {data.Segments} · 읽기 {data.ReadMs} ms" +
                     $" · 상자로 그림 {data.Boxed} · 못 그림 {data.Undrawn} · 읽기 오류 {data.ReadErrors} · 띠 나누기로 만든 면 {data.FacesFallback} · 못 만든 면 {data.FacesSkipped} · 엇갈린 면 {data.FacesTwisted}" +
                     $" · 레벨 {data.Levels.Count} · 작업공유 {(doc.IsWorkshared ? "O" : "X")} · 그림 등급 {RenderCapability.Tier >> 16}");

        // 창이 떠 있는 동안 여는 그룹: 이 문서를 지금 고칠 수 없으면(읽기 전용 등) 3D 를 띄우기 전에 여기서 드러난다.
        var group = new TransactionGroup(doc, "DH 위치·부위 정하기");
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
            Dialogs.Show(Title, "이 문서는 지금 고칠 수 없어 「위치·부위 정하기」를 열지 않았습니다.\n" + ex.Message);
            return Result.Cancelled;
        }

        PickEditor? editor = null;
        bool revert = false, kept = false;
        string crash = "", closeProblem = "", paintProblem = "";
        View3D? checkView = null;
        var legend = new Dictionary<string, string>();
        try
        {
            editor = new PickEditor(data.Members, data.Levels, data.Stored.Plan, session.Write, () => ZoneScene.Reread(doc, data.Members), doc.Title);
            session.Note($"목록\t구조물 {editor.Structure}({(PartCatalog.FindStructure(data.Stored.Plan.Structure) != null ? "저장된 것" : "제목으로 알아냄")})" +
                         $"\t위치 {editor.Places.Count} · 부위 {editor.Parts.Count}\t저장된 판 v{data.Stored.Plan.ReadVersion}{(data.Stored.Exists ? "" : "(저장된 것 없음)")}");
            // Revit 에서 부재를 골라 놓고 눌렀으면 팝업에서도 그 부재를 골라 둔다.
            editor.Select(uidoc.Selection.GetElementIds().Select(id => id.Value));
            revert = ShowWindow(commandData.Application.MainWindowHandle, editor, data, session, Notices(data));

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
            (kept, closeProblem) = CloseGroup(group, keep: !revert && session.Commits > 0, session);
            try { group.Dispose(); } catch { }
        }

        // ★ 이 아래에서 kept 가 참이면(모델에 남았으면) 무슨 일이 있어도 Succeeded 를 돌려준다.
        //   Failed/Cancelled 를 돌려주면 Revit 이 방금 모델에 적은 것까지 전부 되돌린다.

        // 산출식을 이어서: 남은 부재가 0 이 되어 닫을 때만(FormulaCheck.FollowUp). 창이 알고 있는 값이 모델에 그대로 있을 때만 돈다 —
        // 넣은 것이 모델에 남았거나(kept), 넣은 것이 없다. 넣었는데 남기지 못했으면 창의 '남은 0'이 모델과 달라 돌지 않는다.
        FormulaFollow? follow = null;
        try
        {
            bool modelMatchesEditor = kept || (!revert && session.Commits == 0);
            if (editor != null && FormulaCheck.FollowUp(editor.Progress(), revert, crash.Length > 0, modelMatchesEditor))
                follow = FollowWithFormulas(doc, session);
            else if (editor != null)
            {
                var p = editor.Progress();
                session.Note($"산출식 이어서 — 돌지 않음\t남은 {p.Remaining} · 끝 {p.Done} · 처음 상태로 {(revert ? "O" : "X")} · 창 오류 {(crash.Length > 0 ? "O" : "X")} · 모델에 남음 {(kept ? "O" : "X")} · 커밋 {session.Commits}");
            }
        }
        catch (Exception ex)
        {
            session.Note("산출식 이어서 — 오류\t" + ex);
            follow ??= new FormulaFollow();
            follow.Problem = ex.Message;
        }
        bool formulasCommitted = follow?.Plan.MayHaveCommitted == true;
        session.Note("돌려주는 값: " + (kept || formulasCommitted ? "Succeeded" : crash.Length > 0 || closeProblem.Length > 0 ? "Failed" : "Cancelled") +
                     $"(위치·부위가 모델에 남음 {(kept ? "O" : "X")} · 산출식 단계에서 커밋했을 수 있음 {(formulasCommitted ? "O" : "X")})");

        string table = "", journal = "";
        try { (table, journal) = WriteLogs(doc, editor, session, kept, revert); }
        catch (Exception ex) { table = "(쓰지 못했습니다 — 결과표가 엑셀에 열려 있으면 닫아 주세요: " + ex.Message + ")"; }
        try { Report(commandData, editor, session, kept, revert, crash, closeProblem, paintProblem, checkView, legend, table, journal, follow); }
        catch { /* 알림 창을 못 띄워도 결과는 바뀌지 않는다 */ }

        // 산출식 단계에서 무엇이든 커밋됐으면(속성 칸 만들기 포함) Succeeded — 위치·부위를 이번에 넣지 않았어도.
        if (kept || formulasCommitted) return Result.Succeeded;
        if (crash.Length > 0 || closeProblem.Length > 0) { message = crash.Length > 0 ? crash : closeProblem; return Result.Failed; }
        return Result.Cancelled;
    }

    // 산출식을 이어서 돌린다(필요한 부재만). 대상 = 모델의 대상 부재 전부(Revit 의 선택은 보지 않는다 — «다 끝났다»는 모델 전체의 일이다).
    // 여기서 나는 오류는 받아 적는다: 위치·부위에 넣은 값은 이미 모델에 남아 있다.
    private static FormulaFollow FollowWithFormulas(Document doc, Session session)
    {
        var follow = new FormulaFollow();
        var cursor = Mouse.OverrideCursor;
        try
        {
            try { Mouse.OverrideCursor = Cursors.Wait; } catch { }
            follow.Plan = FormulaAutoFill.PlanRefresh(doc, DimensionExtractor.CollectApplicable(doc), count =>
            {
                if (count < AskBeforeCompute) return true;
                // 식을 새로 만들 부재가 많다(식이 하나도 없는 큰 모델을 처음 닫을 때) — 오래 걸릴 수 있어 먼저 묻는다.
                try { Mouse.OverrideCursor = cursor; } catch { }
                var ask = Dialogs.New(Title);
                ask.MainInstruction = $"부재 {count}개의 산출식을 지금 만들까요?";
                ask.MainContent = "위치·부위가 모두 끝나 산출식을 이어서 만듭니다. 부재가 많아 시간이 걸릴 수 있습니다(끝날 때까지 Revit 이 멈춘 것처럼 보입니다).\n" +
                                  "나중에 [산출식 자동입력] 버튼으로 해도 결과는 같습니다.";
                ask.AllowCancellation = true;
                ask.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "지금 만들기");
                ask.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "나중에 하기");
                bool go = ask.Show() == TaskDialogResult.CommandLink1;
                if (go) { try { Mouse.OverrideCursor = Cursors.Wait; } catch { } }
                return go;
            });
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
            text.AppendLine(plan.MayHaveCommitted ? "  (일부는 모델에 적혔을 수 있습니다 — [산출식 자동입력] 버튼으로 다시 맞춰 주세요.)"
                                                  : "  ([산출식 자동입력] 버튼으로 따로 실행해 주세요.)");
            return;
        }
        if (plan.Declined)
        {
            text.AppendLine($"산출식은 나중에 하기로 했습니다(식을 새로 만들 부재 {plan.Recompute}개) — [산출식 자동입력] 버튼으로 실행해 주세요.");
            return;
        }
        if (follow.RolledBack)
        {
            text.AppendLine($"★ 산출식이 모델에 저장되지 않았습니다(Revit 이 변경을 되돌림: {plan.Status}). [산출식 자동입력] 버튼으로 다시 실행해 주세요.");
            return;
        }
        text.AppendLine("산출식(위치·부위가 모두 끝나 이어서 — 필요한 부재만):");
        if (follow.NewFormula > 0) text.AppendLine($"  • 새로 적음: {follow.NewFormula}개(식이 없던 부재)");
        if (follow.Replaced > 0)
            text.AppendLine($"  • 바꿔 적음: {follow.Replaced}개(식이 지금 모델과 맞지 않던 부재 {follow.Replaced - follow.ReplacedStale} · 결합으로 '다시 계산 필요'였던 부재 {follow.ReplacedStale}) — 전의 식은 결과표에 있습니다");
        if (follow.VerdictOnly > 0) text.AppendLine($"  • 판정만 고침: {follow.VerdictOnly}개(같은 식이 다시 나온 부재 — 결합에서 남는 쪽 등)");
        if (follow.CellsFixed > 0) text.AppendLine($"  • 검산 칸만 고침: {follow.CellsFixed}개(식은 그대로)");
        text.AppendLine($"  • 그대로 둠: {follow.Kept - follow.CellsFixed}개(지금도 맞는 식)");
        if (follow.Drifted > 0)
            text.AppendLine($"      - 그 가운데 {follow.Drifted}개는 식을 적은 뒤에 모델 체적이 달라졌습니다(±1% 안이라 '일치') — 새 치수로 맞추려면 [산출식 자동입력]");
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
            text.AppendLine(placesKept ? "  산출식은 위치·부위와 따로 되돌아갑니다(Ctrl+Z 를 한 번 누르면 산출식, 한 번 더 누르면 위치·부위)."
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
            if (status == TransactionStatus.Committed) return (true, "넣은 값은 모델에 남았지만 실행 취소 한 줄로 묶지는 못했습니다(" + why + ").");
            why += " / " + status;
        }
        catch (Exception ex) { why += " / " + ex.Message; session.Note("그룹 닫기\tCommit 오류\t" + ex.Message); }
        return (false, "★ 이 창에서 넣은 값을 모델에 남기지 못했습니다(Revit 이 되돌립니다): " + why);
    }

    private static string Notices(ZoneScene.Data data)
    {
        var notices = new List<string>();
        if (data.LevelNotice.Length > 0) notices.Add(data.LevelNotice);
        if (data.Stored.Copies > 1) notices.Add($"모델에 위치·부위 목록이 {data.Stored.Copies}벌 있어 가장 나중 것을 열었습니다(목록을 고치면 하나로 정리됩니다).");
        if (data.Stored.Exists && data.Stored.Plan.ReadVersion < 2)
            notices.Add("전의 방식(구역 네모)으로 저장한 자료가 있어 이름만 위치 목록으로 옮겼습니다. 목록을 고치면 네모는 지워집니다(지우기 전에 원문을 로그 폴더에 떠 둡니다).");
        else if (data.Stored.Exists && data.Stored.Plan.ReadVersion < ZonePlan.CurrentVersion && data.Stored.Plan.Parts.Count > 0)
            notices.Add("전의 판이 저장한 부위 목록을 구조물의 대표 목록으로 옮겼습니다(직접 넣은 이름과 고친 표시는 그대로 · 바꿔 둔 차례는 묶음의 차례로 다시 놓였습니다).");
        if (data.Boxed > 0) notices.Add($"형상을 세모로 만들지 못한 부재 {data.Boxed}개는 경계상자로 그렸습니다(찍어서 값을 넣을 수 있습니다).");
        if (data.Undrawn > 0) notices.Add($"형상도 경계상자도 없는 부재 {data.Undrawn}개는 그림에 나오지 않습니다(그 부재의 위치·부위는 건드리지 않습니다).");
        if (data.FacesSkipped > 0) notices.Add($"세모로 만들지 못한 면이 {data.FacesSkipped}개 있어 그 자리는 비어 보입니다(부재는 다른 면으로 찍을 수 있습니다).");
        return string.Join("\n", notices);
    }

    // 팝업을 띄운다. [처음 상태로 되돌리고 닫기]를 골랐으면 true.
    private static bool ShowWindow(nint owner, PickEditor editor, ZoneScene.Data data, Session session, string notice)
    {
        bool dark = false;
        try { dark = UIThemeManager.CurrentTheme == UITheme.Dark; } catch { }
        var view = new PlacePartView(editor, data.Meshes, dark, notice);
        var window = view.BuildWindow();
        new WindowInteropHelper(window).Owner = owner;
        bool revert = false;
        view.Close += () => window.Close();
        view.RevertAll += () =>
        {
            if (view.Busy) return;
            if (session.Commits == 0) { view.ShowMessage("이 창에서 모델에 적은 것이 없어 되돌릴 것이 없습니다."); return; }
            var answer = MessageBox.Show(window,
                $"이 창을 연 뒤에 넣은 것을 모두 없던 일로 하고 닫을까요?\n(모델에 적은 것 {editor.Writes}번이 모두 되돌려집니다.)",
                Title, MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
            if (answer != MessageBoxResult.Yes) return;
            revert = true;
            window.Close();
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
        session.Note($"창 닫힘\t적은 횟수 {editor.Writes}\t처음 상태로 {(revert ? "O" : "X")}");
        return revert;
    }

    private static void Report(ExternalCommandData commandData, PickEditor? editor, Session session, bool kept, bool revert, string crash, string closeProblem,
                               string paintProblem, View3D? view, Dictionary<string, string> legend, string table, string journal, FormulaFollow? follow)
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
            td.MainInstruction = p.Remaining == 0 ? "위치·부위 — 모두 끝났습니다" : $"위치·부위 — 남은 부재 {p.Remaining}개";
            text.AppendLine($"부재 {p.Total}개 — 끝 {p.Done} · 아직 {p.Todo} · 하나만 {p.Partial}" + (p.Outside > 0 ? $" · 확인 필요 {p.Outside}" : "") +
                            (p.Locked > 0 ? $" · 고칠 수 없음 {p.Locked}" : "") + (p.Undrawn > 0 ? $" · 그리지 못함 {p.Undrawn}" : ""));
            var places = editor.Places.Select(x => x.Name).Where(n => editor.CountInPlace(n) > 0).ToList();
            if (places.Count > 0)
                text.AppendLine("위치별: " + string.Join(" · ", places.Select(n => $"{n} {editor.CountInPlace(n)}개" + (view != null && legend.TryGetValue(n, out var color) ? $"({color})" : ""))));
            if (editor.CountNoPlace > 0) text.AppendLine($"'{ZonePlan.NoPlaceLabel}'으로 정한 부재: {editor.CountNoPlace}개");
            text.AppendLine();
            text.AppendLine(closeProblem.Length == 0
                ? "이 창에서 넣은 위치·부위는 Revit 의 실행 취소(Ctrl+Z) 한 번으로 모두 되돌릴 수 있습니다."
                : "이 창에서 넣은 위치·부위는 Revit 의 실행 취소(Ctrl+Z)를 넣은 횟수만큼 눌러야 되돌아갑니다.");
            if (session.LegacyDump.Length > 0) text.AppendLine("전의 판이 저장한 목록의 원문: " + session.LegacyDump);
        }
        else if (revert) td.MainInstruction = "처음 상태로 되돌렸습니다 — 이 창에서 넣은 것은 모델에 남지 않았습니다";
        else if (crash.Length > 0 || closeProblem.Length > 0) td.MainInstruction = "위치·부위가 모델에 남지 않았습니다";
        else
        {
            // 이번에 넣은 것은 없다(모두 끝나 있는 모델을 열었다 닫았다) — 이어서 돈 산출식만 알린다.
            td.MainInstruction = "위치·부위는 모두 끝나 있습니다";
            text.AppendLine("이번에 넣거나 바꾼 위치·부위는 없습니다.");
        }
        if (follow != null && formulaNews) AppendFormulaReport(text, follow, kept);
        if (paintProblem.Length > 0) text.AppendLine().AppendLine(paintProblem);
        text.AppendLine();
        text.AppendLine("결과표: " + table);
        text.Append("일지: " + journal);
        td.MainContent = text.ToString();
        if (view != null && kept)
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, $"확인용 3D 뷰 열기 ('{ZoneAssigner.CheckViewName}')",
                "콘크리트 부재를 위치별 색으로 칠해 둔 Revit 뷰입니다(팝업과 같은 색 차례 — 끝났는지는 팝업에서 봅니다).");
        // 식을 만들지 못한 부재: 산출식 버튼과 같은 검토 창으로 하나씩 본다(모델에 적은 것이 되돌려졌으면 띄우지 않는다 — 버튼과 같은 조건).
        var ask = follow != null && follow.Problem.Length == 0 && !follow.Plan.Declined && !follow.RolledBack ? follow.Ask : new List<FormulaOutcome>();
        // 단추는 번호 차례로 놓인다: ① 확인용 뷰 ② 식 확인 ③ 닫기(맨 아래).
        if (ask.Count > 0)
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, $"식을 확인할 부재 {ask.Count}개를 하나씩 띄워 확인·입력",
                "식을 만들지 못했거나, 결합한 뒤 '다시 계산 필요'가 남은 부재입니다(추천식이 있으면 미리 넣어 둡니다).");
        td.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "닫기");
        var pressed = td.Show();
        if (pressed == TaskDialogResult.CommandLink2 && ask.Count > 0)
        {
            try { ReviewWizard.LaunchNumeric(uidoc.Document, ask, commandData.Application.MainWindowHandle); }
            catch (Exception ex) { Dialogs.Show(Title, "검토 창을 띄우지 못했습니다: " + ex.Message + "\n\n[산출식 자동입력] 버튼에서 다시 열 수 있습니다."); }
        }
        if (pressed == TaskDialogResult.CommandLink1 && view != null)
        {
            try { uidoc.ActiveView = view; }
            catch (Exception ex)
            {
                Dialogs.Show(Title, $"뷰를 열지 못했습니다: {ex.Message}\n\n프로젝트 탐색기의 3D 뷰에서 '{ZoneAssigner.CheckViewName}'을 열어 주세요.");
            }
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
        sb.Append("부위 목록\t" + string.Join(" · ", editor.Parts.Select(p => p.Name + (p.NoZone ? "(위치 없이)" : "") + (p.Plain ? "(무근)" : ""))) + "\n");
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
