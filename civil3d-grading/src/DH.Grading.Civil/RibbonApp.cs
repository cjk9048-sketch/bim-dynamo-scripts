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
[assembly: CommandClass(typeof(DH.Grading.Civil.Commands.WallPickCommand))]            // DHWALL(옹벽 변환 — 사면선/소단선 선택, §75)
[assembly: CommandClass(typeof(DH.Grading.Civil.Commands.SlopeReleaseCommand))]        // DHSLOPE(사면 변환 — 옹벽선 선택해 그 단부터 사면 복귀)
[assembly: CommandClass(typeof(DH.Grading.Civil.Commands.InfraworksCommand))]          // DHINFRA(INFRAWORKS SHP 내보내기)
[assembly: CommandClass(typeof(DH.Grading.Civil.Commands.ResetCommand))]               // DHRESET(초기화 — 정지면 생성 전으로)
[assembly: CommandClass(typeof(DH.Grading.Civil.Commands.BasemapCommand))]             // DHMAP/DHMAPOFF(위성 배경지도 켜기·끄기)
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
        // [배포 0728] 한국 좌표계 9종 자동 검사·설치 — 시작 부하를 피해 Idle 1회로 미룬다.
        AcadApp.Idle += OnIdleCoordSys;
        // [JACK 0730] 작업공간(제도 및 주석 ↔ Civil 3D 등) 전환 시 리본이 CUI 기준으로 재구성되며
        //   코드로 만든 탭이 사라짐 → WSCURRENT 변경을 감지해 다음 Idle에 탭 재생성(FindTab이 중복 방지).
        AcadApp.SystemVariableChanged += OnSysVarChanged;
    }

    /// <summary>WSCURRENT 후 Idle 재확인 잔여 횟수 — 리본 재구성이 첫 Idle보다 늦게 끝나
    /// FindTab이 '곧 지워질 옛 탭'을 보고 no-op하는 레이스 대비(리뷰 0730 ①안).</summary>
    private int _recheck;

    private void OnSysVarChanged(object? sender, Autodesk.AutoCAD.ApplicationServices.SystemVariableChangedEventArgs e)
    {
        if (!string.Equals(e.Name, "WSCURRENT", StringComparison.OrdinalIgnoreCase)) return;
        _recheck = 3;
        AcadApp.Idle -= OnIdleBuild;   // 재구독 전 해제 — 빠르게 연속 전환해도 중복 구독 없음
        AcadApp.Idle += OnIdleBuild;
    }

    private void OnIdleCoordSys(object? sender, EventArgs e)
    {
        AcadApp.Idle -= OnIdleCoordSys;
        CoordSysInstaller.EnsureInstalled();
        EnsureRasterDetachMode();
    }

    /// <summary>[JACK 0731] Raster Design '이미지 분리 방식'을 "항상 분리(1)"로 — 배경지도 삭제/갱신 때마다
    /// "이미지를 분리할까요?" 창이 뜨는 것을 근본 차단(설정 0=물어보기가 원인, 코드 우회 불가).
    /// 레지스트리 값이라 다음 세션부터 확실히 적용. RD 미설치 PC에선 무해한 키만 생김.</summary>
    private static void EnsureRasterDetachMode()
    {
        try
        {
            string root = Autodesk.AutoCAD.DatabaseServices.HostApplicationServices.Current.UserRegistryProductRootKey;
            using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(root + @"\Applications\AeciIbApi\Options");
            if (key == null) return;
            key.SetValue("ImageDetachMode", 1, Microsoft.Win32.RegistryValueKind.DWord);
            key.SetValue("TmpImageDetachMode", 1, Microsoft.Win32.RegistryValueKind.DWord);
        }
        catch { }
    }

    public void Terminate() { }

    private void OnIdleBuild(object? sender, EventArgs e)
    {
        if (ComponentManager.Ribbon == null) return;   // 리본 준비 전 — 구독 유지한 채 다음 Idle 대기
        BuildRibbon();                                  // 탭이 살아있으면 FindTab이 no-op
        if (_recheck-- > 0) return;                     // 작업공간 전환 직후엔 몇 번 더 지켜봄(늦은 재구성 대비)
        _recheck = 0;
        AcadApp.Idle -= OnIdleBuild;
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

            // [리본 3분류 — JACK 0724] 정지(절성토) / 도면화 / 내보내기. 버튼 사이 여백(Spacer)으로 간격 확보.
            var pGrade = new RibbonPanelSource { Title = "부지정지" };
            tab.Panels.Add(new RibbonPanel { Source = pGrade });
            pGrade.Items.Add(Spacer());
            pGrade.Items.Add(MakeButton(
                "정지\n옵션", "DHGRADESET ", "단높이·소단폭·구배·사면형상·대소단·옹벽형태·좌표계·표시 옵션", "설정"));
            pGrade.Items.Add(Spacer());
            pGrade.Items.Add(MakeButton(
                "정지면\n생성", "DHGRADE ", "계획 폴리곤+원지반 → 계단식 절성토 TIN Surface 생성", "정지면"));
            pGrade.Items.Add(Spacer());
            // [§75 — JACK 0728/0729 개명] 옹벽변환은 부지정지 패널, 정지면 생성 오른쪽(별도 '옹벽' 중분류 없음).
            //   [JACK 0729] 툴팁에 개념도 이미지(확장 툴팁) — 마우스를 올려두면 그림까지 표시.
            var btnWall = MakeButton(
                "옹벽\n변환", "DHWALL ", "옹벽이 시작될 선을 클릭하면 그 선부터 바깥 단이 옹벽으로 전환", "옹벽");
            btnWall.ToolTip = MakeTip("옹벽 변환 (DHWALL)",
                "시안색 선(옹벽이 시작될 선 — 절토=소단선·성토=사면선)을 클릭하면\n" +
                "그 선부터 바깥(데이라잇 방향) 단이 전부 옹벽으로 바뀝니다.\n" +
                "Enter=적용 · 재사용해도 기존 옹벽 유지(같은 구간은 교체) · '전체해제'로 전부 초기화.",
                MakeTipImage(toWall: true));
            pGrade.Items.Add(btnWall);
            pGrade.Items.Add(Spacer());
            // [사면변환 — JACK 0729] 옹벽의 역방향: 옹벽선을 선택하면 그 단부터 바깥이 다시 사면.
            var btnSlope = MakeButton(
                "사면\n변환", "DHSLOPE ", "옹벽선을 클릭하면 그 단부터 바깥이 다시 사면으로 복귀", "사면");
            btnSlope.ToolTip = MakeTip("사면 변환 (DHSLOPE)",
                "옹벽선을 클릭하면 그 단부터 바깥(데이라잇 방향)이 다시 사면으로 돌아갑니다.\n" +
                "절토 옹벽의 마지막 단 사면 마무리, 성토 사면-옹벽-사면 구성에 사용. Enter=적용.",
                MakeTipImage(toWall: false));
            pGrade.Items.Add(btnSlope);
            pGrade.Items.Add(Spacer());

            var pDraw = new RibbonPanelSource { Title = "도면화" };
            tab.Panels.Add(new RibbonPanel { Source = pDraw });
            pDraw.Items.Add(Spacer());
            pDraw.Items.Add(MakeButton(
                "노리선", "DHNORI ", "정지 결과(번들)로 사면선·소단선·노리선을 한 번에 작도 — DHGRADE 실행 후 사용", "노리선"));
            pDraw.Items.Add(Spacer());

            var pExport = new RibbonPanelSource { Title = "내보내기" };
            tab.Panels.Add(new RibbonPanel { Source = pExport });
            pExport.Items.Add(Spacer());
            pExport.Items.Add(MakeButton(
                "INFRA\nWORKS", "DHINFRA ", "InfraWorks 기초자료 내보내기 — 폴더 선택 후 지형·옹벽3D·SHP·위성GeoTIFF·토공량을 내보냄(있는 것만). DHGRADE 후 사용", "infra"));
            pExport.Items.Add(Spacer());

            // [배경지도 — JACK 0731] 위성사진을 도면 좌표계에 맞춰 깔기 / 한 번에 전부 끄기.
            var pMap = new RibbonPanelSource { Title = "배경지도" };
            tab.Panels.Add(new RibbonPanel { Source = pMap });
            pMap.Items.Add(Spacer());
            var btnMap = MakeButton(
                "배경지도", "DHMAP ", "두 점으로 범위를 찍으면 그 범위의 위성사진을 도면 좌표계에 맞춰 깔아줍니다(화질=정지옵션)", "지도");
            btnMap.ToolTip = MakeTip("배경지도 (DHMAP)",
                "범위 두 모서리를 클릭하면 브이월드 위성사진을 받아\n" +
                "도면 좌표계(정지옵션의 좌표계)에 정확히 맞춰 깔아줍니다.\n" +
                "여러 번 눌러 여러 곳에 깔 수 있고, 화질은 정지옵션에서 선택합니다.", null);
            pMap.Items.Add(btnMap);
            pMap.Items.Add(Spacer());
            var btnMapOff = MakeButton(
                "지도끄기", "DHMAPOFF ", "이 기능으로 깐 위성사진을 한 번에 전부 제거", "지도끄기");
            btnMapOff.ToolTip = MakeTip("지도끄기 (DHMAPOFF)",
                "배경지도로 깔아둔 위성사진을 한 번에 모두 제거합니다.\n" +
                "직접 붙이신 다른 이미지는 그대로 둡니다.", null);
            pMap.Items.Add(btnMapOff);
            pMap.Items.Add(Spacer());

            // [기타 — JACK 0731] 초기화 등 보조 기능. 내보내기와 분리.
            var pMisc = new RibbonPanelSource { Title = "기타" };
            tab.Panels.Add(new RibbonPanel { Source = pMisc });
            pMisc.Items.Add(Spacer());
            // [초기화 — JACK 0731] 정지면 생성 전(원지반+계획폴리곤)으로 되돌림. 부지를 바꿔가며 반복 검토할 때
            //   Ctrl+Z 누적으로 지표면 정의가 꼬이는 것을 방지 — 우리 산출물만 깨끗이 걷어낸다.
            var btnReset = MakeButton(
                "초기화", "DHRESET ", "정지면 생성 전(원지반+계획폴리곤) 상태로 초기화 — DH가 만든 지표면·선을 모두 삭제", "초기화");
            btnReset.ToolTip = MakeTip("초기화 (DHRESET)",
                "정지 지표면(정지면_DH 등)과 사면선·소단선·노리선·옹벽선 등\n" +
                "DH가 만든 객체를 모두 지워 '정지면 생성 전' 상태로 되돌립니다.\n" +
                "원지반과 계획폴리곤은 그대로 유지 — 부지를 바꿔 다시 검토할 때 사용.", null);
            pMisc.Items.Add(btnReset);
            pMisc.Items.Add(Spacer());
        }
        catch
        {
            // 리본 구성 실패해도 명령(DHGRADE/DHGRADESET)은 직접 입력으로 동작
        }
    }

    /// <summary>버튼 사이 빈 여백(선 없는 Spacer) — 리본 버튼이 다닥다닥 붙는 것 방지(JACK 0724).</summary>
    private static RibbonSeparator Spacer() => new() { SeparatorStyle = RibbonSeparatorStyle.Spacer };

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
                    case "사면": // 사면 복귀(초록 사선 + 위 화살)
                        var sl = P(0x6a, 0xc8, 0x7a);
                        dc.DrawLine(sl, new Point(5, 27), new Point(22, 10));
                        dc.DrawLine(sl, new Point(22, 10), new Point(28, 10));
                        dc.DrawLine(sl, new Point(13, 19), new Point(13, 12));   // 위로 화살(사면 복귀)
                        dc.DrawLine(sl, new Point(10.5, 14.5), new Point(13, 12));
                        dc.DrawLine(sl, new Point(15.5, 14.5), new Point(13, 12));
                        break;
                    case "지도": // 지구본형 배경지도(파랑) — 사각 프레임 + 경위선
                        var mp = P(0x3f, 0x8f, 0xd0);
                        dc.DrawRectangle(null, mp, new Rect(5, 7, 22, 18));
                        dc.DrawLine(mp, new Point(16, 7), new Point(16, 25));      // 세로 중앙선
                        dc.DrawLine(mp, new Point(5, 16), new Point(27, 16));      // 가로 중앙선
                        dc.DrawEllipse(null, mp, new Point(16, 16), 6.5, 9);       // 지구본 느낌 타원
                        break;
                    case "지도끄기": // 지도 프레임 + 사선(끄기, 빨강)
                        var mo = P(0x9a, 0xa3, 0xad);
                        dc.DrawRectangle(null, mo, new Rect(5, 7, 22, 18));
                        dc.DrawLine(mo, new Point(5, 16), new Point(27, 16));
                        var xr = P(0xe0, 0x5a, 0x3a);
                        dc.DrawLine(xr, new Point(7, 27), new Point(25, 5));       // 금지 사선
                        break;
                    case "초기화": // 원형 되돌림 화살표(초록) — 리셋(JACK 0731 스샷 참고: 위 트인 원 + 좌상단 화살촉)
                        var rs = P(0x2e, 0xa8, 0x4c);
                        // 중심(16,16)·반지름 9. 위(북)에 틈을 두고 큰 호(300°)를 시계방향으로: 우상(20.5,8.2)→좌상(11.5,8.2).
                        var fig = new PathFigure { StartPoint = new Point(20.5, 8.21), IsClosed = false };
                        fig.Segments.Add(new ArcSegment(new Point(11.5, 8.21), new Size(9, 9), 0,
                            true, SweepDirection.Clockwise, true));
                        var pg = new PathGeometry(); pg.Figures.Add(fig);
                        dc.DrawGeometry(null, rs, pg);
                        // 좌상단 끝의 화살촉 — 위-왼쪽을 향하게(스샷과 동일 방향).
                        dc.DrawLine(rs, new Point(11.5, 8.21), new Point(16.49, 8.59));
                        dc.DrawLine(rs, new Point(11.5, 8.21), new Point(13.66, 3.70));
                        break;
                    case "옹벽": // 옹벽(벽돌 2단, 흙색)
                        var wl = P(0xc0, 0xa0, 0x72);
                        dc.DrawRectangle(null, wl, new Rect(6, 15, 20, 6));
                        dc.DrawRectangle(null, wl, new Rect(6, 22, 20, 6));
                        dc.DrawLine(wl, new Point(16, 15), new Point(16, 21));
                        dc.DrawLine(wl, new Point(11, 22), new Point(11, 28));
                        dc.DrawLine(wl, new Point(21, 22), new Point(21, 28));
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

    /// <summary>[JACK 0729] 확장 툴팁(제목+설명+개념도 이미지) — 마우스를 올려두면 그림까지 표시.</summary>
    private static RibbonToolTip MakeTip(string title, string content, ImageSource? image)
    {
        var tip = new RibbonToolTip
        {
            Title = title,
            Content = content,
            IsHelpEnabled = false,
        };
        if (image != null)
        {
            tip.ExpandedContent = "개념도: 사선=사면 · 수직 계단=옹벽 · ●=클릭한 선";
            tip.ExpandedImage = image;
        }
        return tip;
    }

    /// <summary>[JACK 0729] 옹벽변환/사면변환 툴팁 개념도 — 코드 벡터로 그린 전/후 단면.
    /// 왼쪽=클릭 전 상태(●=클릭 지점), 가운데 화살표, 오른쪽=적용 후. toWall=true면 사면→옹벽.</summary>
    private static ImageSource? MakeTipImage(bool toWall)
    {
        try
        {
            const int W = 360, H = 150;
            var slope = new Pen(new SolidColorBrush(Color.FromRgb(0x2e, 0xa8, 0x4c)), 3)
            { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
            var wall = new Pen(new SolidColorBrush(Color.FromRgb(0xd8, 0x2c, 0x2c)), 3)
            { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
            var baseP = new Pen(new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)), 2);
            var arrowP = new Pen(new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44)), 3)
            { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };

            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, W, H));

                // 4단 단면 — 단마다 (경사부 or 수직부) + 소단. [wallFrom..wallTo] 단이 옹벽. 반환=클릭 표식 좌표.
                Point Steps(double x0, double y0, int wallFrom, int wallTo, int markBench)
                {
                    double x = x0, y = y0;
                    var mark = new Point(x0, y0);
                    const double run = 20, rise = 24, bench = 9;
                    dc.DrawLine(baseP, new Point(x - 12, y), new Point(x, y));   // 바닥
                    for (int i = 0; i < 4; i++)
                    {
                        bool w = i >= wallFrom && i <= wallTo;
                        double nx = w ? x : x + run;
                        dc.DrawLine(w ? wall : slope, new Point(x, y), new Point(nx, y - rise));
                        x = nx; y -= rise;
                        if (i == markBench) mark = new Point(x, y);
                        dc.DrawLine(w ? wall : slope, new Point(x, y), new Point(x + bench, y));
                        x += bench;
                    }
                    dc.DrawLine(baseP, new Point(x, y), new Point(x + 12, y));   // 상단 지반
                    return mark;
                }

                // 왼쪽 = 클릭 전(●=클릭한 선) / 오른쪽 = 적용 후.
                //   옹벽변환: 사면 4단 → 클릭 단부터 옹벽.  사면변환: 전부 옹벽 → 클릭 단부터 사면 복귀.
                var mk = toWall
                    ? Steps(28, 128, wallFrom: 99, wallTo: 99, markBench: 1)   // 전: 전부 사면
                    : Steps(28, 128, wallFrom: 0, wallTo: 99, markBench: 1);   // 전: 전부 옹벽
                if (toWall) Steps(232, 128, wallFrom: 2, wallTo: 99, markBench: -1);   // 후: 2단 위부터 옹벽
                else Steps(232, 128, wallFrom: 0, wallTo: 1, markBench: -1);           // 후: 2단까지 옹벽, 위는 사면

                // 클릭 표식(노란 점 + 검정 테두리) — 왼쪽 그림의 클릭한 선 위치.
                dc.DrawEllipse(Brushes.Gold, new Pen(Brushes.Black, 1.4), mk, 6, 6);

                // 가운데 화살표.
                dc.DrawLine(arrowP, new Point(178, 75), new Point(210, 75));
                dc.DrawLine(arrowP, new Point(202, 68), new Point(210, 75));
                dc.DrawLine(arrowP, new Point(202, 82), new Point(210, 75));
            }
            var rtb = new RenderTargetBitmap(W, H, 96, 96, PixelFormats.Pbgra32);
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
