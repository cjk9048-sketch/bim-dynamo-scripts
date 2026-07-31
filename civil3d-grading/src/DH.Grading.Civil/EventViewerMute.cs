namespace DH.Grading.Civil;

/// <summary>[JACK 0731] 정지면 생성 중 이벤트 뷰어 자동 알림(파노라마 팝업)만 끄기 — 이벤트 기록 자체는 남아
/// 필요하면 수동(EVENTVIEWER)으로 열어볼 수 있다. Civil3D 앰비언트 설정 'Show Event Viewer'를
/// 명령 실행 동안 false로 바꾸고 끝나면 원복. 전 과정 방어적 — 실패해도 기능엔 영향 없음.</summary>
internal static class EventViewerMute
{
    /// <summary>알림 끄기 — 반환=원래 설정값(원복용, 실패 시 null).</summary>
    public static object? Begin()
    {
        try
        {
            var s = Autodesk.Civil.ApplicationServices.CivilApplication.ActiveDocument
                        .Settings.DrawingSettings.AmbientSettings.General.ShowEventViewer;
            bool prev = s.Value;
            s.Value = false;
            return prev;
        }
        catch { return null; }
    }

    /// <summary>원래 설정으로 복원(Begin 반환값 전달).</summary>
    public static void End(object? prev)
    {
        if (prev is not bool b) return;
        try
        {
            Autodesk.Civil.ApplicationServices.CivilApplication.ActiveDocument
                .Settings.DrawingSettings.AmbientSettings.General.ShowEventViewer.Value = b;
        }
        catch { }
    }
}
