using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Windows;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

[assembly: ExtensionApplication(typeof(DH.Grading.Civil.RibbonApp))]
[assembly: CommandClass(typeof(DH.Grading.Civil.Commands.CreateGradingCommand))]
[assembly: CommandClass(typeof(DH.Grading.Civil.Commands.GradingSettingsCommand))]
[assembly: CommandClass(typeof(DH.Grading.Civil.Commands.SurfaceIntersectionCommand))] // DHXSEC(지표면 교선 TEST)
[assembly: CommandClass(typeof(DH.Grading.Civil.Commands.SlopeLineCommand))]           // DHSLOPELINE(노리선 수동, 레거시)
[assembly: CommandClass(typeof(DH.Grading.Civil.Commands.NoriCommand))]                // DHNORI(노리선 버튼 — 번들 기반)
[assembly: CommandClass(typeof(DH.Grading.Civil.Commands.InfraworksCommand))]          // DHINFRA(INFRAWORKS SHP 내보내기)
[assembly: CommandClass(typeof(DH.Grading.Civil.Commands.CoordSysProbeCommand))]       // DHCS(좌표계 API 진단 — 임시)

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
                "정지\n설정", "DHGRADESET ", "단높이·소단폭·구배·격자 해상도를 설정", "설정"));
            src.Items.Add(MakeButton(
                "정지면\n생성", "DHGRADE ", "계획 폴리곤+원지반 → 계단식 절성토 TIN Surface 생성", "정지면"));
            src.Items.Add(MakeButton(
                "노리선", "DHNORI ", "정지 결과(번들)로 사면선·소단선·노리선을 한 번에 작도 — DHGRADE 실행 후 사용", "노리선"));
            src.Items.Add(MakeButton(
                "infra\nworks 기초자료", "DHINFRA ", "InfraWorks 기초자료 내보내기 — 폴더 선택 후 지형·옹벽3D·SHP·위성GeoTIFF·토공량을 내보냄(있는 것만). DHGRADE 후 사용", "infra"));
        }
        catch
        {
            // 리본 구성 실패해도 명령(DHGRADE/DHGRADESET)은 직접 입력으로 동작
        }
    }

    private static RibbonButton MakeButton(string text, string command, string tooltip, string glyph)
    {
        return new RibbonButton
        {
            Text = text,
            ShowText = true,
            ShowImage = true,
            LargeImage = MakeGlyph(glyph),
            Size = RibbonItemSize.Large,
            Orientation = System.Windows.Controls.Orientation.Vertical,
            ToolTip = tooltip,
            CommandHandler = new RelayCommand(command),
            CommandParameter = command,
        };
    }

    /// <summary>버튼 아이콘을 런타임에 그려 각 명령을 직관적으로 구분(PNG 리소스 대신, JACK 요청).
    /// 설정=슬라이더 / 정지면=계단 / 노리선=사면+빗금 / infra=상자+내보내기 화살표.</summary>
    private static ImageSource? MakeGlyph(string kind)
    {
        try
        {
            const int S = 32;
            Pen P(byte r, byte g, byte b) => new(new SolidColorBrush(Color.FromRgb(r, g, b)), 2.2)
            { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };

            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, S, S)); // 투명 배경
                switch (kind)
                {
                    case "설정": // 슬라이더 3줄 + 노브(회색)
                        var gy = P(0x9e, 0xb0, 0xc4);
                        for (int i = 0; i < 3; i++)
                        {
                            double y = 8 + i * 8;
                            dc.DrawLine(gy, new Point(5, y), new Point(27, y));
                            dc.DrawEllipse(Brushes.White, gy, new Point(9 + i * 7, y), 2.6, 2.6);
                        }
                        break;
                    case "정지면": // 계단(초록) — 절성토 계단 정지면
                        var gr = P(0x6a, 0xc8, 0x7a);
                        var st = new[] { new Point(4, 27), new Point(4, 21), new Point(12, 21),
                            new Point(12, 15), new Point(20, 15), new Point(20, 9), new Point(28, 9) };
                        for (int i = 0; i + 1 < st.Length; i++) dc.DrawLine(gr, st[i], st[i + 1]);
                        break;
                    case "노리선": // 사면(대각선, 주황) + 빗금 틱
                        var or = P(0xf0, 0xa8, 0x3a);
                        dc.DrawLine(or, new Point(6, 27), new Point(27, 6));
                        for (int i = 1; i <= 3; i++)
                        {
                            double t = i / 4.0;
                            var bp = new Point(6 + 21 * t, 27 - 21 * t);
                            dc.DrawLine(or, bp, new Point(bp.X + 4.5, bp.Y + 4.5)); // 빗금(사면 아래로)
                        }
                        break;
                    default: // infra: 상자 + 내보내기 화살표(파랑)
                        var bl = P(0x4a, 0x90, 0xe2);
                        dc.DrawRectangle(null, bl, new Rect(5, 13, 13, 14)); // 파일 상자
                        dc.DrawLine(bl, new Point(15, 8), new Point(28, 8));  // 화살 축
                        dc.DrawLine(bl, new Point(24, 4), new Point(28, 8));  // 화살촉
                        dc.DrawLine(bl, new Point(24, 12), new Point(28, 8));
                        break;
                }
            }
            var rtb = new RenderTargetBitmap(S, S, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(dv);
            rtb.Freeze();
            return rtb;
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
