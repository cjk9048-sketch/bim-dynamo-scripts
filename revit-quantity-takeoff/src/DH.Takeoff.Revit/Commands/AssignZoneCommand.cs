using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
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
/// </summary>
[Transaction(TransactionMode.Manual)]
public sealed class AssignZoneCommand : IExternalCommand
{
    private const string Title = "DH 수량산출 — 위치·부위";

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
        string table = "", journal = "";
        try { (table, journal) = WriteLogs(doc, editor, session, kept, revert); }
        catch (Exception ex) { table = "(쓰지 못했습니다 — 결과표가 엑셀에 열려 있으면 닫아 주세요: " + ex.Message + ")"; }
        try { Report(uidoc, editor, session, kept, revert, crash, closeProblem, paintProblem, checkView, legend, table, journal); }
        catch { /* 알림 창을 못 띄워도 결과는 바뀌지 않는다 */ }

        if (kept) return Result.Succeeded;
        if (crash.Length > 0 || closeProblem.Length > 0) { message = crash.Length > 0 ? crash : closeProblem; return Result.Failed; }
        return Result.Cancelled;
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

    private static void Report(UIDocument uidoc, PickEditor? editor, Session session, bool kept, bool revert, string crash, string closeProblem,
                               string paintProblem, View3D? view, Dictionary<string, string> legend, string table, string journal)
    {
        // 보기만 하고 닫았으면(적은 것도 문제도 없다) 알림 창을 띄우지 않는다.
        if (!kept && !revert && crash.Length == 0 && closeProblem.Length == 0) return;

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
                ? "이 창에서 넣은 것은 Revit 의 실행 취소(Ctrl+Z) 한 번으로 모두 되돌릴 수 있습니다."
                : "이 창에서 넣은 것은 Revit 의 실행 취소(Ctrl+Z)를 넣은 횟수만큼 눌러야 되돌아갑니다.");
            if (session.LegacyDump.Length > 0) text.AppendLine("전의 판이 저장한 목록의 원문: " + session.LegacyDump);
        }
        else if (revert) td.MainInstruction = "처음 상태로 되돌렸습니다 — 이 창에서 넣은 것은 모델에 남지 않았습니다";
        else td.MainInstruction = "위치·부위가 모델에 남지 않았습니다";
        if (paintProblem.Length > 0) text.AppendLine().AppendLine(paintProblem);
        text.AppendLine();
        text.AppendLine("결과표: " + table);
        text.Append("일지: " + journal);
        td.MainContent = text.ToString();
        if (view != null && kept)
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, $"확인용 3D 뷰 열기 ('{ZoneAssigner.CheckViewName}')",
                "콘크리트 부재를 위치별 색으로 칠해 둔 Revit 뷰입니다(팝업과 같은 색 차례 — 끝났는지는 팝업에서 봅니다).");
        td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "닫기");
        if (td.Show() == TaskDialogResult.CommandLink1 && view != null)
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
