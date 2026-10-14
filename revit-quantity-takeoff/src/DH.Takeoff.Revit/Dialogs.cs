using Autodesk.Revit.UI;

namespace DH.Takeoff.Revit;

/// <summary>
/// Revit 의 알림 창을 띄우는 한 곳.
/// Revit 은 창 제목 앞에 애드인 이름("DH 수량산출")을 스스로 붙이는데, 우리 제목에 이미 들어 있어 두 번 찍혔다
/// ("DH 수량산출 - DH 수량산출 — 구역 지정", 2026-10-08 스샷). 여기서 그 자동 붙임을 끈다.
/// </summary>
public static class Dialogs
{
    /// <summary>제목만 정한 창(버튼·내용은 부르는 쪽이 채운다).</summary>
    public static TaskDialog New(string title) => new(title) { TitleAutoPrefix = false };

    /// <summary>글 하나와 [닫기] 버튼 — TaskDialog.Show(제목, 글)과 같은 모습.</summary>
    public static TaskDialogResult Show(string title, string instruction)
    {
        var dialog = New(title);
        dialog.MainInstruction = instruction;
        return dialog.Show();
    }
}
