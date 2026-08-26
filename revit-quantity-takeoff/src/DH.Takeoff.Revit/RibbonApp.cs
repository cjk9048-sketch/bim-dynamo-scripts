using System.Reflection;
using System.Windows.Media.Imaging;
using Autodesk.Revit.UI;
using DH.Takeoff.Revit.Commands;

namespace DH.Takeoff.Revit;

/// <summary>
/// 애드인 진입점 — 리본 탭 "DH 수량산출" + 아이콘 버튼 구성.
/// </summary>
public sealed class RibbonApp : IExternalApplication
{
    private const string TabName = "DH 수량산출";

    public Result OnStartup(UIControlledApplication app)
    {
        try { app.CreateRibbonTab(TabName); } catch { /* 이미 존재하면 무시 */ }

        var panel = app.CreateRibbonPanel(TabName, "수량산출");
        var asm = Assembly.GetExecutingAssembly().Location;

        var setup = new PushButtonData(
            "DH_Setup", "매개변수\n세팅", asm, typeof(SetupParametersCommand).FullName)
        {
            ToolTip = "공유 매개변수(L1~W3·H·ETC·DH_*)를 생성·바인딩(이미 있으면 재사용)",
            LargeImage = LoadIcon("Setup32.png"),
            Image = LoadIcon("Setup16.png"),
        };
        panel.AddItem(setup);

        var fill = new PushButtonData(
            "DH_Fill", "치수\n자동입력", asm, typeof(FillDimensionsCommand).FullName)
        {
            ToolTip = "선택 부재(없으면 전체)의 경계상자에서 L1·W1·H를 자동 채움(미터)",
            LargeImage = LoadIcon("Measure32.png"),
            Image = LoadIcon("Measure16.png"),
        };
        panel.AddItem(fill); // 치수 자동입력이 끝나면(또는 비정형 검토 마법사가 닫히면) 겹침 공제가 자동 실행됨

        var run = new PushButtonData(
            "DH_Run", "산출·\n내보내기", asm, typeof(RunTakeoffCommand).FullName)
        {
            ToolTip = "net 치수·수식 산출 → VBA 호환 CSV + formula 시트 내보내기",
            LargeImage = LoadIcon("Export32.png"),
            Image = LoadIcon("Export16.png"),
        };
        panel.AddItem(run);

        return Result.Succeeded;
    }

    public Result OnShutdown(UIControlledApplication app) => Result.Succeeded;

    /// <summary>DLL에 포함된 PNG 아이콘을 BitmapImage로 로드.</summary>
    private static BitmapImage? LoadIcon(string fileName)
    {
        var asm = Assembly.GetExecutingAssembly();
        var resName = $"DH.Takeoff.Revit.Resources.{fileName}";
        using var stream = asm.GetManifestResourceStream(resName);
        if (stream == null) return null;

        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.StreamSource = stream;
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.EndInit();
        bmp.Freeze();
        return bmp;
    }
}
