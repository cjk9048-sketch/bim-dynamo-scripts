using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Windows;
using DH.Grading.Civil;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

[assembly: ExtensionApplication(typeof(DH.Pipe.Civil.PipeApp))]
[assembly: CommandClass(typeof(DH.Pipe.Civil.Commands.PipeCommands))]
[assembly: CommandClass(typeof(DH.Pipe.Civil.SelfTest))]

namespace DH.Pipe.Civil;

/// <summary>애드인 시작 · 리본 탭 «DH 관로». 틀은 부지정지 RibbonApp 방식(시작 도중이 아니라 한가할 때 만들고 · 작업공간이 바뀌면 다시).</summary>
public sealed class PipeApp : IExtensionApplication
{
    internal const string TabId = "DH_PIPE_TAB";
    private const string TabTitle = "DH 관로";

    public void Initialize()
    {
        try { PipeLog.Line($"=== {PipeSettings.ProductName} {PipeSettings.Version} 시작{(PipeLog.Headless ? " (화면 없음)" : "")}"); } catch { }
        if (PipeLog.Headless) return;
        try
        {
            if (ComponentManager.Ribbon != null) BuildRibbon();
            else AcadApp.Idle += OnIdleBuild;
            AcadApp.SystemVariableChanged += OnSysVar;
            // 리본을 닫았다 다시 열면(RIBBONCLOSE → RIBBON) 코드로 만든 탭이 사라질 수 있다 → 리본 부품이 새로 생길 때 한 번 더 본다
            ComponentManager.ItemInitialized += (_, _) =>
            {
                try { if (ComponentManager.Ribbon != null && ComponentManager.Ribbon.FindTab(TabId) == null) { AcadApp.Idle -= OnIdleBuild; AcadApp.Idle += OnIdleBuild; } }
                catch { }
            };
        }
        catch (System.Exception ex) { PipeLog.Line("[리본] " + ex); }
    }

    public void Terminate() { }

    private void OnIdleBuild(object? s, EventArgs e)
    {
        if (ComponentManager.Ribbon == null) return;
        AcadApp.Idle -= OnIdleBuild;
        BuildRibbon();
    }

    private void OnSysVar(object s, Autodesk.AutoCAD.ApplicationServices.SystemVariableChangedEventArgs e)
    {
        // 작업공간을 바꾸면 리본이 다시 짜이면서 코드로 만든 탭이 사라진다 → 다음 한가할 때 다시
        if (!string.Equals(e.Name, "WSCURRENT", StringComparison.OrdinalIgnoreCase)) return;
        AcadApp.Idle -= OnIdleBuild;
        AcadApp.Idle += OnIdleBuild;
    }

    private static void BuildRibbon()
    {
        try
        {
            var ribbon = ComponentManager.Ribbon;
            if (ribbon == null || ribbon.FindTab(TabId) != null) return;
            var tab = new RibbonTab { Title = TabTitle, Id = TabId };
            ribbon.Tabs.Add(tab);

            Panel(tab, "① 원지반",
                Btn("지표면\n고르기", "DHPIPESURF", "원지반으로 쓸 TIN 지표면을 고릅니다.", "surf"),
                Btn("원지반\n만들기", "DHPIPEGRADING", "부지정지 애드인의 원지반 창 · 수치지도 · 자르기 · 지층 · 로드뷰를 엽니다.", "ground"));
            Panel(tab, "② 노선",
                Btn("노선\n만들기", "DHPIPEROUTE", "폴리선을 고르면 노선(Civil 선형)이 됩니다. 관종 · 관경도 함께 정합니다.", "route"),
                Btn("관\n바꾸기", "DHPIPERUN", "구간의 관종 · 관경을 바꿉니다.", "pipe"),
                Btn("지표\n구역", "DHPIPEZONE", "닫힌 폴리선을 아스콘 · 콘크리트 · 보도블록 구역으로, 선을 연석으로 정합니다.\n포장 깨기 · 절단 · 복구가 여기서 나옵니다.", "zone"));
            Panel(tab, "③ 종단",
                Btn("처음\n종단", "DHPIPEPROFILE", "토피 띠 안에서 꺾임이 가장 적은 종단을 만듭니다.\n그 뒤 종단뷰에서 PVI 를 끌어 고칩니다.", "profile"),
                Btn("지장물\n찍기", "DHPIPEOBST", "평면에서 자리를 찍고 종류 · 크기 · 깊이를 넣습니다.", "obst"),
                Btn("지장물\n선", "DHPIPEOBSTSEL", "지하시설물 선을 고르면 노선과 만나는 곳마다 지장물이 됩니다.", "obstsel"),
                Btn("가이드안", "DHPIPEGUIDE", "지장물 회피 · 공기밸브 · 이토밸브 · 제수밸브 · 소화전 자리를 제안합니다.", "guide"),
                Btn("종단\n도면", "DHPIPEPROFSHEET", "2D 종단면도를 그립니다(밴드 · 구간 치수선 · 지장물 · 부속).", "profsheet"));
            Panel(tab, "④ 공법 · 횡단",
                Btn("구간\n공법", "DHPIPEMETHOD", "두 점을 찍고 터파기 방식을 고릅니다:\n자연 터파기 · 조립식 · 조절식 · H파일 · 시트파일 · 하천 횡단 · 추진", "method"),
                Btn("지층", "DHPIPEBORE", "시추공 · 층 · 지하수위를 넣는 지층 구성 창을 엽니다(부지정지).\n만든 지층면이 단면 · 종단 · 토적표에 반영됩니다.", "strata"),
                Btn("땅속\n값", "DHPIPESTRATA", "지층면이 없을 때: 시추 자리의 토사 · 풍화암 두께 · 지하수위를 넣습니다.", "strata"),
                Btn("횡단도", "DHPIPEXSEC", "측점마다 단면과 토적표를 A1 장에 그립니다.", "xsec"));
            Panel(tab, "⑥ 수량 · 3D",
                Btn("토적표\n엑셀", "DHPIPEQTY", "토적표 · 토공 집계 · 공법 구간 · 관 · 부속 · 지장물을 엑셀로 냅니다.", "xlsx"),
                Btn("3D\n모형", "DHPIPE3D", "터파기 · 관 · 케이싱 · 수직구 · 흙막이를 3D 로 만듭니다.", "cube"),
                Btn("도면화", "DHPIPESHEET", "배치탭에 납품 도면 꼴로 만듭니다: 평면도 · 종단면도 · 횡단면도 · 도곽 · 표제란 · A1/A3 페이지 설정.\n값은 «DH 관로» 창의 ⑦ 도면화에서 넣습니다.", "sheet"));
            Panel(tab, "창",
                Btn("관로\n창", "DHPIPE", "오른쪽에 «DH 관로» 창을 엽니다. 단계마다 상태와 단면 미리보기가 보입니다.", "panel"),
                Btn("검사", "DHPIPECHECK", "토피 · 이격 · 경사 · 못 만든 단면을 알려 줍니다.", "check"),
                Btn("정보", "DHPIPEABOUT", PipeSettings.ProductName + " " + PipeSettings.Version, "info"));
            PipeLog.Line("[리본] 탭 만듦");
        }
        catch (System.Exception ex) { PipeLog.Line("[리본] 실패 — " + ex); }
    }

    private static void Panel(RibbonTab tab, string title, params RibbonButton[] buttons)
    {
        var src = new RibbonPanelSource { Title = title };
        foreach (var b in buttons) src.Items.Add(b);
        tab.Panels.Add(new RibbonPanel { Source = src });
    }

    private static RibbonButton Btn(string text, string command, string tip, string glyph)
        => new()
        {
            Text = text, ShowText = true, ShowImage = true, LargeImage = PipeIcons.Glyph(glyph, 32), Image = PipeIcons.Glyph(glyph, 16),
            Size = RibbonItemSize.Large, Orientation = System.Windows.Controls.Orientation.Vertical,
            ToolTip = new RibbonToolTip { Title = text.Replace("\n", " ") + $" ({command})", Content = tip, IsHelpEnabled = false },
            CommandHandler = new Relay(command), CommandParameter = command,
        };

    /// <summary>리본 단추 → 명령. 앞에 ESC 두 번을 붙여 돌던 명령을 끊는다.</summary>
    private sealed class Relay(string command) : ICommand
    {
        public event EventHandler? CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter)
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            doc?.SendStringToExecute("\u0003\u0003" + command + " ", true, false, true);
        }
    }
}
