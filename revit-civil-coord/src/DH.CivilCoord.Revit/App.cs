using System.Reflection;
using System.Windows.Media.Imaging;
using Autodesk.Revit.UI;

namespace DH.CivilCoord.Revit;

/// <summary>애드인 진입점 — 리본 탭 «DH 좌표» · 단추 «Civil 좌표 맞춤» 하나.</summary>
public sealed class App : IExternalApplication
{
    private const string TabName = "DH 좌표";

    public Result OnStartup(UIControlledApplication app)
    {
        try { app.CreateRibbonTab(TabName); } catch { /* 이미 있으면 그대로 */ }
        var panel = app.CreateRibbonPanel(TabName, "Civil 3D");
        var asm = Assembly.GetExecutingAssembly().Location;
        var button = new PushButtonData("DH_CivilCoord", "Civil\n좌표 맞춤", asm, typeof(CoordCommand).FullName)
        {
            ToolTip = "구조물 점 두 개와 그 점의 Civil 좌표로 맞춰, Civil 3D(DH 부지) · 나비스웍스에 제자리로 들어가는 IFC를 내보냅니다. " +
                      "같은 값으로 Revit 공유 좌표도 바꿀 수 있습니다.",
            LargeImage = LoadIcon("Coord32.png"),
            Image = LoadIcon("Coord16.png"),
        };
        panel.AddItem(button);
        return Result.Succeeded;
    }

    public Result OnShutdown(UIControlledApplication app) => Result.Succeeded;

    private static BitmapImage? LoadIcon(string fileName)
    {
        var asm = Assembly.GetExecutingAssembly();
        using var stream = asm.GetManifestResourceStream($"DH.CivilCoord.Revit.Resources.{fileName}");
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
