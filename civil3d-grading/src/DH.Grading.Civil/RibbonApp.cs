using System.Reflection;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Windows;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

[assembly: ExtensionApplication(typeof(DH.Grading.Civil.RibbonApp))]
[assembly: CommandClass(typeof(DH.Grading.Civil.Commands.CreateGradingCommand))]
[assembly: CommandClass(typeof(DH.Grading.Civil.Commands.GradingSettingsCommand))]

namespace DH.Grading.Civil;

/// <summary>
/// 애드인 진입점 — "DH 정지" 리본 탭 + [정지 설정]·[정지면 생성] 버튼.
/// 리본이 아직 준비 안 됐으면 Idle 시점에 한 번 더 시도한다.
/// </summary>
public sealed class RibbonApp : IExtensionApplication
{
    private const string TabId = "DH_GRADING_TAB";
    private const string TabTitle = "DH 정지";

    public void Initialize()
    {
        if (ComponentManager.Ribbon != null) BuildRibbon();
        else AcadApp.Idle += OnIdleBuild;
    }

    public void Terminate() { }

    private void OnIdleBuild(object? sender, EventArgs e)
    {
        if (ComponentManager.Ribbon == null) return;
        AcadApp.Idle -= OnIdleBuild;
        BuildRibbon();
    }

    private void BuildRibbon()
    {
        try
        {
            var ribbon = ComponentManager.Ribbon;
            if (ribbon == null) return;
            if (ribbon.FindTab(TabId) != null) return; // 이미 있음

            var tab = new RibbonTab { Title = TabTitle, Id = TabId };
            ribbon.Tabs.Add(tab);

            var src = new RibbonPanelSource { Title = "정지(절성토)" };
            tab.Panels.Add(new RibbonPanel { Source = src });

            src.Items.Add(MakeButton(
                "정지\n설정", "DHGRADESET ", "단높이·소단폭·구배·격자 해상도를 설정", "Settings32.png"));
            src.Items.Add(MakeButton(
                "정지면\n생성", "DHGRADE ", "계획 폴리곤+원지반 → 계단식 절성토 TIN Surface 생성", "Grade32.png"));
        }
        catch
        {
            // 리본 구성 실패해도 명령(DHGRADE/DHGRADESET)은 직접 입력으로 동작
        }
    }

    private static RibbonButton MakeButton(string text, string command, string tooltip, string icon)
    {
        return new RibbonButton
        {
            Text = text,
            ShowText = true,
            ShowImage = true,
            LargeImage = LoadIcon(icon),
            Size = RibbonItemSize.Large,
            Orientation = System.Windows.Controls.Orientation.Vertical,
            ToolTip = tooltip,
            CommandHandler = new RelayCommand(command),
            CommandParameter = command,
        };
    }

    /// <summary>DLL에 포함된 PNG 아이콘 로드(없으면 null).</summary>
    private static BitmapImage? LoadIcon(string fileName)
    {
        try
        {
            var asm = Assembly.GetExecutingAssembly();
            using var stream = asm.GetManifestResourceStream($"DH.Grading.Civil.Resources.{fileName}");
            if (stream == null) return null;
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.StreamSource = stream;
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch { return null; }
    }

    /// <summary>리본 버튼 → 명령줄로 명령 문자열 전송.</summary>
    private sealed class RelayCommand(string command) : ICommand
    {
        public event EventHandler? CanExecuteChanged;
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter)
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            doc?.SendStringToExecute(command, true, false, true);
        }
    }
}
