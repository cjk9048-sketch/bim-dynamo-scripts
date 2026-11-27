// 부지정지(DH.Grading)에서 «파일 연결»로 가져온 화면 틀(DhBrand · DhDock · DockClose · BusyPopup)이 기대는 이름들의 대역.
// 연결한 파일은 네임스페이스가 DH.Grading.Civil 이라 여기서도 그 이름으로 둔다(이 DLL 안에만 있으므로 부지정지 DLL 과 부딪히지 않는다).
namespace DH.Grading.Civil;

/// <summary>진단 기록 — 관로 애드인의 기록 파일로 보낸다.</summary>
internal static class DiagLog
{
    internal static void Append(string text) => DH.Pipe.Civil.PipeLog.Append(text);
    internal static string FilePath => DH.Pipe.Civil.PipeLog.FilePath;
}

/// <summary>화면 없는 Civil(accoreconsole)인지.</summary>
internal static class SelfCheckLog
{
    internal static bool Headless => DH.Pipe.Civil.PipeLog.Headless;
}
