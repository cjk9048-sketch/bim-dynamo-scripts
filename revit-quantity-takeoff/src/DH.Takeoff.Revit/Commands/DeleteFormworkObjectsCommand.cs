using System.IO;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace DH.Takeoff.Revit.Commands;

/// <summary>
/// "거푸집 객체 지우기"(R44 — JACK 2026-10-15 «납품 전 한 번에 지운다»): [수량산출]의 [완료]가 만든 거푸집 객체(일반 모델 판)를 모두 지운다.
/// 지침은 거푸집을 비객체로 두고(수도편 표 2.3-6) 비객체 형상은 BIM 모델에서 뺀다(공통 2.3.1(1)) — 그래서 납품 전에 지운다.
/// 콘크리트 부재의 거푸집 속성(지침 이름 수량)과 엑셀은 그대로다. 일람표 'DH 거푸집 객체'는 남긴다(판이 없으면 빈 표). Ctrl+Z 로 되돌릴 수 있다.
/// </summary>
[Transaction(TransactionMode.Manual)]
public sealed class DeleteFormworkObjectsCommand : IExternalCommand
{
    private const string Title = "DH 거푸집 삭제";

    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        var uidoc = commandData.Application.ActiveUIDocument;
        if (uidoc?.Document == null) { message = "열린 Revit 문서가 없습니다."; return Result.Failed; }
        var doc = uidoc.Document;
        bool readOnly = false;
        try { readOnly = doc.IsReadOnly; } catch { }
        if (readOnly)
        {
            Dialogs.Show(Title, "이 문서는 읽기 전용이라 거푸집 객체를 지울 수 없습니다.");
            return Result.Cancelled;
        }

        List<ElementId> found;
        try { found = FormworkObjects.Find(doc); }
        catch (Exception ex) { message = "거푸집 객체를 찾지 못했습니다: " + ex.Message; return Result.Failed; }
        if (found.Count == 0)
        {
            Dialogs.Show(Title, "이 모델에는 지울 거푸집 객체가 없습니다.");
            return Result.Cancelled;
        }

        var ask = Dialogs.New(Title);
        ask.MainInstruction = $"거푸집 객체 {found.Count}개를 지울까요?";
        ask.MainContent = "[수량산출]의 [완료]가 만든 판(일반 모델)입니다 — 납품 전에 지웁니다.\n콘크리트 부재의 거푸집 넓이 속성과 엑셀은 그대로입니다. Ctrl+Z 로 되돌릴 수 있습니다.";
        ask.AllowCancellation = true;
        ask.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "지우기");
        ask.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "그만두기");
        if (ask.Show() != TaskDialogResult.CommandLink1) return Result.Cancelled;

        FormworkObjects.Removal removal;
        try { removal = FormworkObjects.DeleteAll(doc); }
        catch (Exception ex)
        {
            // 트랜잭션 안에서 났으면 커밋 전이라 되돌려졌다 — 지운 것이 없다.
            Log(doc, $"오류\t{ex.Message}");
            message = "거푸집 객체를 지우지 못했습니다: " + ex.Message;
            return Result.Failed;
        }
        Log(doc, $"찾음 {removal.Found} · 지움 {removal.Deleted} · 못 지움 {removal.Kept.Count}" + (removal.Kept.Count > 0 ? "(" + removal.Kept[0] + ")" : "") +
                 $" · {removal.Status}" + (removal.Error.Length > 0 ? " · " + removal.Error : ""));

        // ★ 커밋했으면 무슨 일이 있어도 Succeeded — Failed/Cancelled 를 돌려주면 Revit 이 방금 지운 것을 되돌린다.
        try
        {
            var text = new StringBuilder();
            if (removal.Committed) text.Append($"거푸집 객체 {removal.Deleted}개를 지웠습니다.");
            else if (removal.Status == TransactionStatus.Uninitialized) text.Append("지울 수 있는 거푸집 객체가 없습니다.");
            else text.Append($"지우지 못했습니다(Revit 이 변경을 되돌림: {removal.Status})" + (removal.Error.Length > 0 ? " — " + removal.Error : "."));
            if (removal.Kept.Count > 0)
                text.Append($"\n\n지우지 못한 판 {removal.Kept.Count}개 — {removal.Kept[0]}" + (removal.Kept.Count > 1 ? " 외" : "") +
                            "\n(다른 사용자가 편집 중인 판은 그 사용자가 동기화한 뒤 다시 누르세요. 그룹 안의 판은 그룹에서 빼야 지울 수 있습니다.)");
            Dialogs.Show(Title, text.ToString());
        }
        catch { /* 알림 창을 못 띄워도 결과는 바뀌지 않는다 */ }
        return removal.Committed ? Result.Succeeded : Result.Cancelled;
    }

    // 지운 일을 일지 한 줄로(%APPDATA%\DH.Takeoff\log\<문서>_formwork_objects.log). 못 적어도 넘어간다.
    private static void Log(Document doc, string line)
    {
        try
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DH.Takeoff", "log");
            Directory.CreateDirectory(dir);
            string name = doc.Title;
            foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            File.AppendAllText(Path.Combine(dir, name + "_formwork_objects.log"), $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}\t지우기\t{line}{Environment.NewLine}", new UTF8Encoding(true));
        }
        catch { }
    }
}
