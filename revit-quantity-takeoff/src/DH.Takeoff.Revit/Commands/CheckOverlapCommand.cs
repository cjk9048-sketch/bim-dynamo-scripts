using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using DH.Takeoff.Core;
using DH.Takeoff.Core.View3D;

namespace DH.Takeoff.Revit.Commands;

/// <summary>
/// "겹침 검사" 버튼 — 선택 부재(없으면 전체) 가운데 콘크리트 부재끼리 결합 없이 겹쳐 두 번 세는 자리를 찾아 보여 주고,
/// 원하면 우선순위대로(슬래브 우선) 형상 결합을 걸어 Revit 이 한쪽을 잘라 내게 한다.
/// 수량(산출식 자동입력)을 뽑기 전에 돌린다.
/// 결과 창(OverlapResultView)은 겹친 쌍의 목록과 3D 그림을 함께 보여 준다(JACK 2026-10-14) — 그림에 넘길 것은 OverlapScene 이 읽는다.
/// 3D 를 읽지 못해도 창은 뜬다(목록만). 한 일은 일지(…_overlap_session.log)에 남긴다.
/// </summary>
[Transaction(TransactionMode.Manual)]
public sealed class CheckOverlapCommand : IExternalCommand
{
    private const string Title = "DH 수량산출 — 겹침 검사";
    private const int BlobLinesInJournal = 200;     // 일지에 적는 덩어리 줄의 한도(쌍이 아주 많은 모델)

    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        var uidoc = commandData.Application.ActiveUIDocument;
        if (uidoc?.Document == null) { message = "열린 Revit 문서가 없습니다."; return Result.Failed; }
        var doc = uidoc.Document;

        nint owner = commandData.Application.MainWindowHandle;
        bool dark = false;
        try { dark = UIThemeManager.CurrentTheme == UITheme.Dark; } catch { }

        var journal = new Journal();
        try { return Run(uidoc, doc, owner, dark, journal, ref message); }
        finally { journal.Save(doc); }
    }

    private static Result Run(UIDocument uidoc, Document doc, nint owner, bool dark, Journal journal, ref string message)
    {
        bool everything = false;      // 창에서 [전체 다시 검사]를 눌렀다 — Revit 의 선택을 보지 않는다
        while (true)
        {
            OverlapChecker.Found found;
            string log = "", logProblem = "";
            string joinSummary = "";       // 결합을 걸었으면 그 결과(결과 창을 띄우다 오류가 나도 알릴 수 있게)
            bool joinedAny = false;        // 결합이 하나라도 모델에 걸렸다(하나도 못 했으면 Join 이 그룹을 되돌려 실행 취소 줄도 없다)
            bool selectionOnly;
            try
            {
                var all = DimensionExtractor.CollectApplicable(doc);
                var sel = uidoc.Selection.GetElementIds();
                selectionOnly = sel.Count > 0 && !everything;
                var targets = (selectionOnly ? all.Where(id => sel.Contains(id)) : all)
                    // 콘크리트 부재만 본다(재료를 알 수 없는 부재는 빼지 않는다) — 산출식 자동입력의 대상과 같다.
                    .Where(id => doc.GetElement(id) is Element el && ConcreteFilter.Classify(doc, el).Kind != ConcreteFilter.Kind.NotConcrete)
                    .ToList();
                journal.Note($"시작\t{doc.Title}\t{(selectionOnly ? $"선택한 부재만(선택 {sel.Count})" : everything ? "전체(창에서 [전체 다시 검사])" : "전체")}\t대상 {targets.Count}");
                if (targets.Count == 0)
                {
                    var none = Popup(owner, dark, new OverlapResultView.Model { Mode = OverlapResultView.Mode.NoTargets, SelectionOnly = selectionOnly, CanCheckAll = selectionOnly }, journal);
                    if (none.Result == OverlapResultView.Choice.CheckAll) { everything = true; continue; }
                    return Result.Cancelled;
                }
                var watch = Stopwatch.StartNew();
                found = OverlapChecker.Find(doc, targets, withBlobs: true);
                journal.Note(string.Create(CultureInfo.InvariantCulture,
                    $"찾기\t쌍 {found.Pairs.Count} · 체적 합 {found.TotalM3:0.######} m3 · 검사한 부재 {found.Checked} · 잴 수 없었던 쌍 {found.Unmeasured} · 함께 있지 않아 뺀 쌍 {found.NotCoexisting}\t{watch.ElapsedMilliseconds} ms"));
                try { log = OverlapChecker.WriteLog(doc, found); }
                catch (Exception ex) { logProblem = ex.Message; }
            }
            catch (Exception ex)
            {
                // 여기까지는 모델을 바꾸지 않았다.
                message = ex.Message;
                journal.Note("오류(찾기)\t" + ex);
                Dialogs.Show("DH 수량산출 — 오류", "겹침 검사 중 오류:\n" + ex.Message);
                return Result.Failed;
            }

            // ★ 이 아래에서 모델을 바꿀 수 있다(결합). 그 뒤에는 무슨 일이 있어도 Succeeded 를 돌려준다 —
            //   Failed/Cancelled 를 돌려주면 Revit 이 방금 건 결합까지 전부 되돌린다.
            //   ([전체 다시 검사]로 처음부터 다시 도는 길은 결합하기 전의 창에만 있다.)
            try
            {
                // 대상에는 콘크리트 부재와, 무슨 재료인지 알 수 없어 빼지 않은 부재가 들어 있다.
                OverlapResultView.Model Model(OverlapResultView.Mode mode, OverlapScene.Data? scene, string sceneProblem)
                {
                    var model = new OverlapResultView.Model
                    {
                        Mode = mode, Checked = found.Checked, SelectionOnly = selectionOnly,
                        CanCheckAll = selectionOnly && mode != OverlapResultView.Mode.Joined,
                        TotalM3 = found.TotalM3, LogPath = log, LogProblem = logProblem,
                        PriorityText = OverlapChecker.PriorityText, StaleVerdict = FormulaAutoFill.StaleVerdict, SceneProblem = sceneProblem,
                    };
                    for (int i = 0; i < found.Pairs.Count; i++)
                    {
                        var p = found.Pairs[i];
                        model.Rows.Add(new OverlapRow
                        {
                            Key = i, VolumeM3 = p.VolumeM3, Winner = p.WinnerName, Loser = p.LoserName,
                            WinnerPart = PartOf(doc, p.Winner), LoserPart = PartOf(doc, p.Loser),
                            WinnerIndex = scene?.IndexOf(p.Winner) ?? -1, LoserIndex = scene?.IndexOf(p.Loser) ?? -1,
                            HasBlob = scene != null && scene.Blobs[i] != null,
                            Status = p.Status, AfterM3 = p.AfterM3,
                            // 잘 끝난 쌍 = 결합됐고 다시 재도 겹침이 없다 · 앞선 결합으로 이미 풀렸다. 그 밖(못 함 · 겹침 남음 · 다시 재지 못함)은 확인할 쌍이다.
                            Fine = p.Status == OverlapChecker.Joined || p.Status == OverlapChecker.NotNeeded,
                        });
                    }
                    if (scene != null)
                    {
                        model.MemberNames = scene.Names;
                        model.Meshes = scene.Meshes;
                        model.Blobs = scene.Blobs;
                        if (scene.Undrawn > 0) model.Notes.Add($"형상도 경계상자도 없는 부재 {scene.Undrawn}개는 그림에 나오지 않습니다(목록과 결합에는 들어 있습니다).");
                        int withoutBlob = scene.BlobMissing + scene.BlobOutside;
                        if (withoutBlob > 0) model.Notes.Add($"겹친 덩어리를 그리지 못한 쌍이 {withoutBlob}개 있습니다(목록에는 있고, 줄을 누르면 두 부재를 칠해 보여 줍니다).");
                    }
                    if (found.Unmeasured > 0)
                        model.Notes.Add($"잴 수 없었던 쌍 {found.Unmeasured}개(Revit 이 두 입체의 겹침을 계산하지 못함)는 목록에 없습니다.");
                    if (found.NotCoexisting > 0)
                        model.Notes.Add($"디자인 옵션·공정이 달라 함께 있지 않는 쌍 {found.NotCoexisting}개는 겹침으로 치지 않았습니다.");
                    if (found.TimedOut)
                        model.Notes.Add("시간 한도 때문에 끝까지 보지 못했습니다. 부재를 나누어 선택한 뒤 다시 실행해 주세요.");
                    return model;
                }

                if (found.Pairs.Count == 0)
                {
                    var clean = Popup(owner, dark, Model(OverlapResultView.Mode.Clean, null, ""), journal);
                    if (clean.Result == OverlapResultView.Choice.CheckAll) { everything = true; continue; }
                    return Result.Succeeded;
                }

                var (scene, sceneProblem) = ReadScene(doc, found.Pairs, verify: true, journal);
                var asked = Popup(owner, dark, Model(OverlapResultView.Mode.Found, scene, sceneProblem), journal);
                if (asked.Result == OverlapResultView.Choice.CheckAll) { everything = true; continue; }
                if (asked.Result == OverlapResultView.Choice.SelectAll) { Select(uidoc, found.Pairs, zoom: false); return Result.Succeeded; }
                if (asked.Result == OverlapResultView.Choice.Pair)
                {
                    if (asked.PickedKey >= 0 && asked.PickedKey < found.Pairs.Count) Select(uidoc, new[] { found.Pairs[asked.PickedKey] }, zoom: true);
                    return Result.Succeeded;
                }
                if (asked.Result != OverlapResultView.Choice.Join) return Result.Succeeded;

                var joining = Stopwatch.StartNew();
                var report = OverlapChecker.Join(doc, found.Pairs);
                journal.Note(string.Create(CultureInfo.InvariantCulture,
                    $"결합\t결합 {report.Joined} · 겹침 남음 {report.StillOverlap} · 못 함 {report.Failed} · 필요 없음 {report.NotNeeded} · 남은 체적 {report.RemainingM3:0.######} m3\t{joining.ElapsedMilliseconds} ms"));
                joinSummary = string.Create(CultureInfo.InvariantCulture,
                    $"결합 {report.Joined + report.NotNeeded}쌍 · 겹침이 남음 {report.StillOverlap}쌍 · 결합 못 함 {report.Failed}쌍 · 남은 중복 체적 {report.RemainingM3:0.00} m³");
                joinedAny = report.Joined + report.StillOverlap > 0;   // 결합이 커밋된 쌍(겹침이 남은 쌍도 결합은 걸렸다)
                // 다시 쓰지 못했으면(결과표를 엑셀로 열어 둔 채 결합한 경우) 결합 전의 표를 결과처럼 열어 주지 않는다.
                try { log = OverlapChecker.WriteLog(doc, found); logProblem = ""; }
                catch (Exception ex) { log = ""; logProblem = ex.Message; }
                var failed = found.Pairs.Where(p => p.Status != OverlapChecker.Joined && p.Status != OverlapChecker.NotNeeded).ToList();
                // 결합하지 못했거나 겹침이 남은 부재는 선택해 둔다(화면에서 바로 볼 수 있게).
                if (failed.Count > 0) Select(uidoc, failed, zoom: false);

                // 결합으로 부재의 모양이 바뀌었다 — 그림을 다시 읽는다. 덩어리는 결합하기 전에 잰 것 그대로다(정리된 자리 · 확인할 자리를 보여 준다).
                var (after, afterProblem) = ReadScene(doc, found.Pairs, verify: false, journal);
                var result = Model(OverlapResultView.Mode.Joined, after, afterProblem);
                result.Joined = report.Joined;
                result.StillOverlap = report.StillOverlap;
                result.Failed = report.Failed;
                result.NotNeeded = report.NotNeeded;
                result.RemainingM3 = report.RemainingM3;
                var shown = Popup(owner, dark, result, journal);
                if (shown.Result == OverlapResultView.Choice.Pair && shown.PickedKey >= 0 && shown.PickedKey < found.Pairs.Count)
                    Select(uidoc, new[] { found.Pairs[shown.PickedKey] }, zoom: true);
            }
            catch (Exception ex)
            {
                journal.Note("오류(결과)\t" + ex);
                try
                {
                    Dialogs.Show("DH 수량산출 — 오류", "겹침 검사 결과를 처리하는 중 오류가 났습니다:\n" + ex.Message +
                        (joinSummary.Length == 0 ? ""
                         : joinedAny ? "\n\n결합은 이미 모델에 걸렸습니다(Ctrl+Z 한 번으로 되돌릴 수 있습니다): " + joinSummary
                         : "\n\n결합은 하나도 걸리지 않았습니다(모델은 그대로입니다): " + joinSummary) + "\n\n결과표: " + log);
                }
                catch { }
            }
            return Result.Succeeded;
        }
    }

    // 묶음 제목에 쓰는 부위 이름(산출서와 같은 함수). 읽지 못하면 "기타".
    private static string PartOf(Document doc, ElementId id)
    {
        try { return doc.GetElement(id) is Element el ? OverlapScene.PartOf(doc, el) : "기타"; }
        catch { return "기타"; }
    }

    // 3D 에 보여 줄 것을 읽는다. 읽지 못해도 창은 뜬다(목록만) — 까닭을 돌려준다.
    private static (OverlapScene.Data? scene, string problem) ReadScene(Document doc, List<OverlapPair> pairs, bool verify, Journal journal)
    {
        try
        {
            var scene = OverlapScene.Read(doc, pairs, verify);
            journal.Note(string.Create(CultureInfo.InvariantCulture,
                $"장면 읽기({(verify ? "찾은 뒤" : "결합한 뒤")})\t부재 {scene.Ids.Count} · 세모 {scene.Triangles} · 모서리 {scene.Segments} · 읽기 {scene.ReadMs} ms" +
                $" · 가운데 ({scene.Origin.X:0}, {scene.Origin.Y:0}, {scene.Origin.Z:0}) · 상자로 그림 {scene.Boxed} · 못 그림 {scene.Undrawn} · 읽기 오류 {scene.ReadErrors}" +
                $" · 띠 나누기로 만든 면 {scene.FacesFallback} · 못 만든 면 {scene.FacesSkipped} · 엇갈린 면 {scene.FacesTwisted}"));
            journal.Note($"덩어리\t그림 {scene.BlobsDrawn} / 쌍 {pairs.Count} · 세모 {scene.BlobTriangles} · 경계상자로 대신 {scene.BlobBoxed} · 없음 {scene.BlobMissing}" +
                         $" · 겹친 자리를 벗어나 그리지 않음 {scene.BlobOutside} · 넓이 다름 {scene.BlobAreaOff}");
            foreach (string line in scene.BlobLines.Take(BlobLinesInJournal)) journal.Raw("  " + line);
            if (scene.BlobLines.Count > BlobLinesInJournal) journal.Raw($"  … 나머지 {scene.BlobLines.Count - BlobLinesInJournal}줄은 줄임");
            return (scene, "");
        }
        catch (Exception ex)
        {
            journal.Note("오류(장면 읽기)\t" + ex);
            return (null, "모델의 형상을 읽는 중 오류가 났습니다: " + ex.Message);
        }
    }

    private static OverlapResultView Popup(nint owner, bool dark, OverlapResultView.Model model, Journal journal)
    {
        OverlapResultView view;
        try { view = new OverlapResultView(model, dark, OverlapChecker.Joined, OverlapChecker.NotNeeded); }
        catch (Exception ex) when (model.Meshes.Count > 0)
        {
            // 3D 가 든 창을 만들다 오류가 났다 — 결과와 단추는 보여야 한다: 그림 없이(목록만) 한 번 더 만든다.
            journal.Note("오류(창 만들기 — 그림 없이 다시)\t" + ex);
            model.Meshes = Array.Empty<MeshData>();
            model.Blobs = Array.Empty<MeshData?>();
            model.MemberNames = Array.Empty<string>();
            model.SceneProblem = "3D 그림을 만드는 중 오류가 났습니다: " + ex.Message;
            view = new OverlapResultView(model, dark, OverlapChecker.Joined, OverlapChecker.NotNeeded);
        }
        try { view.ShowDialog(owner, Title); }
        finally
        {
            journal.Note($"창\t{model.Mode}\t쌍 {model.Rows.Count}\t누른 것 {view.Result}{(view.PickedKey >= 0 ? " " + view.PickedKey.ToString(CultureInfo.InvariantCulture) : "")}");
            foreach (string line in view.Trail) journal.Raw("  " + line);
        }
        return view;
    }

    // 쌍의 두 부재를 선택한다. zoom = 화면도 그 부재에 맞춘다(한 쌍을 보러 갈 때).
    private static void Select(UIDocument uidoc, IEnumerable<OverlapPair> pairs, bool zoom)
    {
        var ids = pairs.SelectMany(p => new[] { p.Winner, p.Loser }).Distinct().ToList();
        uidoc.Selection.SetElementIds(ids);
        if (!zoom) return;
        try { uidoc.ShowElements(ids); } catch { }
    }

    // 이번 실행의 일지: 찾기 · 덩어리의 자가검증 · 장면 읽기 · 창에서 누른 것 · 결합 — Revit 을 돌린 뒤에 원인을 가를 수 있게.
    private sealed class Journal
    {
        private readonly StringBuilder _text = new();

        public void Note(string line) =>
            _text.Append(DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture)).Append('\t').Append(line).Append('\n');

        public void Raw(string line) => _text.Append(line).Append('\n');

        public void Save(Document doc)
        {
            try
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DH.Takeoff", "log");
                Directory.CreateDirectory(dir);
                string name = doc.Title;
                foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
                File.AppendAllText(Path.Combine(dir, name + "_overlap_session.log"),
                    $"── {DateTime.Now:yyyy-MM-dd HH:mm:ss} ──\n" + _text + "\n", new UTF8Encoding(true));
            }
            catch { }
        }
    }
}
