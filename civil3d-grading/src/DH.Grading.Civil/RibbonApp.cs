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
[assembly: CommandClass(typeof(DH.Grading.Civil.Commands.ViewSurfaceCommand))]         // DHVIEW(지표면 보기 전환)
[assembly: CommandClass(typeof(DH.Grading.Civil.Commands.ExcavCommand))]              // DHEXCAV(터파기 지표면 — 지하구조물)
[assembly: CommandClass(typeof(DH.Grading.Civil.Commands.ResetCommand))]               // DHRESET(초기화 — 정지면 생성 전으로)
[assembly: CommandClass(typeof(DH.Grading.Civil.Commands.BasemapCommand))]             // DHMAP/DHMAPOFF(위성 배경지도 켜기·끄기)
[assembly: CommandClass(typeof(DH.Grading.Civil.Commands.SectionCommand))]             // DHSECTION(종단·횡단 생성)
[assembly: CommandClass(typeof(DH.Grading.Civil.Commands.ProfileCommand))]             // DHPROFILE(종단도 — 노선 직접 그리기)
[assembly: CommandClass(typeof(DH.Grading.Civil.Commands.ImportGisCommand))]           // DHCONTOUR/DHPARCEL(등고선·지적도 가져오기)
[assembly: CommandClass(typeof(DH.Grading.Civil.Commands.CoordSysProbeCommand))]       // DHCS(좌표계 API 진단 — 임시)
[assembly: CommandClass(typeof(DH.Grading.Civil.Commands.BandInfoCommand))]            // DHBANDINFO(밴드 검토 — 읽기 전용 진단)
[assembly: CommandClass(typeof(DH.Grading.Civil.Commands.StationCommand))]             // DHSTATION(측점 추가·삭제 — 밸브실 등)
[assembly: CommandClass(typeof(DH.Grading.Civil.Commands.SampleLineCommand))]          // DHSAMPLE(단면검토선 — 측점 목록대로 생성)
[assembly: CommandClass(typeof(DH.Grading.Civil.Commands.SheetCommand))]               // DHSHEET(도곽 — A1 배치·모형 도곽범위)
[assembly: CommandClass(typeof(DH.Grading.Civil.Commands.XsecViewCommand))]            // DHXVIEW(횡단도 — 옹벽·가시설은 (전)(후) 두 장)
[assembly: CommandClass(typeof(DH.Grading.Civil.Commands.SheetSettingsCommand))]       // DHSHEETSET(도면 설정 — 횡단·원지반굴곡·표·배경지도)
[assembly: CommandClass(typeof(DH.Grading.Civil.StrataPalette))]                       // DHSTRATA(지층 구성 — 우측 도킹바)
[assembly: CommandClass(typeof(DH.Grading.Civil.StrataDraw))]                          // DHSTRATAPICK(평면에서 시추 위치 찍기)
[assembly: CommandClass(typeof(DH.Grading.Civil.Commands.MapPickCommand))]            // DHMAPPICK(지도에서 범위 고르기)
[assembly: CommandClass(typeof(DH.Grading.Civil.Commands.MapPalette))]                // DHCONTOURBOX(지도 도킹바가 넘긴 범위로 지표면 가져오기)
[assembly: CommandClass(typeof(DH.Grading.Civil.Commands.NgiiCommand))]               // DHNGII(수치지도 DXF → 원지반)
[assembly: CommandClass(typeof(DH.Grading.Civil.Commands.CropGroundCommand))]         // DHCROP(원지형 자르기)

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
        // [재시작 보존 0805] 사면형상(직각/라운드) 마지막 저장값 복원 — 첫 명령 전에 끝나야 하므로 여기서(레지스트리 1회 읽기라 부하 없음).
        GradingSettings.LoadUserPrefs();
        if (ComponentManager.Ribbon != null) BuildRibbon();
        else AcadApp.Idle += OnIdleBuild;
        // [배포 0728] 한국 좌표계 9종 자동 검사·설치 — 시작 부하를 피해 Idle 1회로 미룬다.
        AcadApp.Idle += OnIdleCoordSys;
        // [JACK 0730] 작업공간(제도 및 주석 ↔ Civil 3D 등) 전환 시 리본이 CUI 기준으로 재구성되며
        //   코드로 만든 탭이 사라짐 → WSCURRENT 변경을 감지해 다음 Idle에 탭 재생성(FindTab이 중복 방지).
        AcadApp.SystemVariableChanged += OnSysVarChanged;

        // ★★[JACK 0825] 보기 버튼 켜고 끄기 — 배선은 <b>Idle로 미룬다</b>.
        //   ★[JACK 0826] 종전엔 여기서 바로 했다. <b>Initialize는 AutoCAD 시작 도중</b>이라
        //   그 시점에 DocumentManager를 순회하거나 트랜잭션을 여는 것은 위험하다 —
        //   리본 만들기·좌표계 검사도 같은 이유로 이미 Idle에 넘기고 있었는데 이것만 규칙을 벗어났다.
        AcadApp.Idle += OnIdleHookDocs;

        // ★★[JACK 0831 "civil3d를 키면 바로 지층구성 도킹바가 떠 있는데 지층구성을 눌러야만 뜨게 해줘"]
        //   GUID를 뗀 것으로 끝날 일이지만, <b>제멋대로 뜨지 않는다</b>는 것은 JACK이 직접 말한 요건이라
        //   시작이 끝난 뒤 <b>한 번 더 확인한다</b>. 여기(Initialize)가 아니라 Idle인 이유는 위와 같다 —
        //   시작 도중에는 창을 만지는 것도 위험하다.
        AcadApp.Idle += OnIdleCloseStrata;
    }

    /// <summary>시작 직후 한 번 — 사용자가 안 눌렀는데 떠 있는 지층 구성 창을 닫는다.</summary>
    private void OnIdleCloseStrata(object? sender, System.EventArgs e)
    {
        AcadApp.Idle -= OnIdleCloseStrata;
        try { StrataPalette.CloseIfNotAsked(); } catch { }
    }

    /// <summary>★[JACK 0826] 시작이 끝난 뒤(첫 Idle) 문서 이벤트를 단다 — 한 번만.</summary>
    private void OnIdleHookDocs(object? sender, System.EventArgs e)
    {
        AcadApp.Idle -= OnIdleHookDocs;
        try
        {
            AcadApp.DocumentManager.DocumentActivated += (_, _) => RefreshEnabled();
            AcadApp.DocumentManager.DocumentCreated += (_, e2) => HookDoc(e2.Document);
            foreach (Document d0 in AcadApp.DocumentManager) HookDoc(d0);
            RefreshEnabled();
        }
        catch { }
    }

    /// <summary>그 도면의 명령이 끝날 때마다 보기 버튼 상태를 다시 본다 — <c>DH</c>로 시작하는 것만.</summary>
    private static void HookDoc(Document? d)
    {
        if (d == null) return;
        try
        {
            d.CommandEnded += (_, e3) =>
            {
                if (e3.GlobalCommandName != null &&
                    e3.GlobalCommandName.StartsWith("DH", System.StringComparison.OrdinalIgnoreCase))
                    RefreshEnabled();
            };
        }
        catch { }
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

            // [리본 분류 — JACK 0724/0731] 부지정지 / 도면화 / 가져오기 / 내보내기 / 기타.
            var pGrade = new RibbonPanelSource { Title = "부지정지" };
            var btnSet = MakeButton(
                "정지\n옵션", "DHGRADESET ", "단높이·소단폭·구배·사면형상·대소단·옹벽형태·좌표계·표시 옵션", "설정");
            // ★★[JACK 0824] <b>지표면 생성 = 스플릿 버튼.</b> 계획지표면과 터파기 지표면을 한 자리에 둔다.
            //   기본(윗부분 클릭)은 계획지표면 — 지금까지 하던 그것. 드롭다운에서 터파기를 고른다.
            var btnPlan = MakeButton(
                "지표면\n생성", "DHGRADE ", "계획 폴리곤+원지반 → 계단식 절성토 TIN Surface 생성", "정지면");
            btnPlan.ToolTip = MakeTip("계획지표면 생성 (DHGRADE)",
                "계획 경계 폴리선과 원지반을 고르면 계단식 절·성토 지표면을 만듭니다.\n" +
                "제원은 [정지 설정]에서 정합니다.", null);
            var btnExc = MakeButton(
                "터파기\n지표면", "DHEXCAV ", "구조물 바닥 폴리선 → 굴착 법면·바닥만 지표면으로 생성", "터파기");
            btnExc.ToolTip = MakeTip("터파기 지표면 생성 (DHEXCAV)",
                "배수지·정수장 같은 **지하구조물**의 터파기를 만듭니다.\n" +
                "구조물 바닥계획고가 들어간 닫힌 폴리선을 고르고, 제원(단높이·구배·소단)을 그 자리에서 정합니다.\n" +
                "법면이 올라가 닿는 목표면은 **계획면과 원지반 중 낮은 쪽**입니다 —\n" +
                "절토부는 이미 깎아 둔 계획면에서, 성토부는 원지반에서 팝니다(시공 순서).\n" +
                "결과는 **굴착 형상만**(바닥+법면)이라 종단에도 구조물 위에만 나옵니다.", null);
            var splitSurf = new RibbonSplitButton
            {
                Text = "계획부지\n생성",
                ShowText = true,
                ShowImage = true,
                // ★[JACK 0824] 스플릿 버튼은 **자기 이미지를 따로 줘야** 한다 —
                //   목록 항목의 이미지를 자동으로 물려받지 않아 아이콘 자리가 비어 보인다.
                LargeImage = MakeGlyph("계획부지"),
                Image = MakeGlyph("계획부지"),
                Size = RibbonItemSize.Large,
                Orientation = System.Windows.Controls.Orientation.Vertical,
                IsSplit = true,
                IsSynchronizedWithCurrentItem = false,
                ListStyle = RibbonSplitButtonListStyle.List,
                ToolTip = MakeTip("지표면 생성",
                    "**계획지표면** — 부지 정지(지금까지 하던 그것).\n" +
                    "**터파기 지표면** — 배수지·정수장 같은 지하구조물의 굴착.\n" +
                    "아래 화살표를 눌러 고릅니다.", null),
            };
            splitSurf.Items.Add(btnPlan);
            splitSurf.Items.Add(btnExc);
            splitSurf.Current = btnPlan;

            // ★[JACK 0824] 보기 전환은 **별도 버튼**으로 뺀다 — 생성 명령이 화면을 껐다 켜는 부작용을
            //   품으면, 명령이 중간에 실패하거나 Esc를 누를 때 지표면이 꺼진 채로 남는다("사라졌다"로 보인다).
            //   상태가 눈에 보이고 언제든 되돌릴 수 있는 쪽이 안전하다.
            // ★[JACK 0824] 보기도 스플릿 버튼 — 누를 때마다 묻지 않고 **바로** 바뀐다.
            var vAll = MakeButton("전부\n보기", "DHVIEWALL ", "원지반·계획지표면·터파기를 모두 표시", "보기전부");
            var vGnd = MakeButton("원지반\n만", "DHVIEWG ", "원지반만 표시(계획·터파기 숨김)", "보기원지반");
            var vPln = MakeButton("계획\n지표면", "DHVIEWP ", "계획지표면만 표시", "보기계획");
            var vExc = MakeButton("터파기\n만", "DHVIEWE ", "터파기 지표면만 표시", "보기터파기");

            // ★★[JACK 0825] <b>없는 것은 누를 수 없게 한다.</b>
            //   JACK: <i>"계획지표면이나 터파기 등을 수행하지 않았을 때는 버튼이 비활성화되게 해주고."</i>
            //   종전엔 눌러야 "아직 없습니다"를 알려 줬다 — 눌러 보기 전엔 알 수 없었다.
            _btnViewPlan = vPln; _btnViewExcav = vExc;   // 상태 갱신은 첫 Idle에서(OnIdleHookDocs)
            var splitView = new RibbonSplitButton
            {
                Text = "보기",
                ShowText = true,
                ShowImage = true,
                LargeImage = MakeGlyph("보기"),
                Image = MakeGlyph("보기"),
                Size = RibbonItemSize.Large,
                Orientation = System.Windows.Controls.Orientation.Vertical,
                IsSplit = true,
                IsSynchronizedWithCurrentItem = false,
                ListStyle = RibbonSplitButtonListStyle.List,
                ToolTip = MakeTip("지표면 보기",
                    "화면에 **무엇을 보일지**만 바꿉니다 — 지표면 형상은 건드리지 않습니다.\n" +
                    "터파기를 만들 때 계획지표면이 겹쳐 보여 헷갈리면 여기서 끄면 됩니다.\n" +
                    "작업용 중간 산물(목표면·가상면)은 언제나 숨겨 둡니다.", null),
            };
            splitView.Items.Add(vAll);
            splitView.Items.Add(vGnd);
            splitView.Items.Add(vPln);
            splitView.Items.Add(vExc);
            splitView.Current = vAll;
            // [§75 — JACK 0728/0729 개명] 옹벽변환은 부지정지 패널, 정지면 생성 오른쪽(별도 '옹벽' 중분류 없음).
            //   [JACK 0729] 툴팁에 개념도 이미지(확장 툴팁) — 마우스를 올려두면 그림까지 표시.
            var btnWall = MakeButton(
                "옹벽\n변환", "DHWALL ", "옹벽이 시작될 선을 클릭하면 그 선부터 바깥 단이 옹벽으로 전환", "옹벽");
            btnWall.ToolTip = MakeTip("옹벽 변환 (DHWALL)",
                "시안색 선(옹벽이 시작될 선 — 절토=소단선·성토=사면선)을 클릭하면\n" +
                "그 선부터 바깥(데이라잇 방향) 단이 전부 옹벽으로 바뀝니다.\n" +
                "Enter=적용 · 재사용해도 기존 옹벽 유지(같은 구간은 교체) · '전체해제'로 전부 초기화.",
                MakeTipImage(toWall: true));
            // [사면변환 — JACK 0729] 옹벽의 역방향: 옹벽선을 선택하면 그 단부터 바깥이 다시 사면.
            var btnSlope = MakeButton(
                "사면\n변환", "DHSLOPE ", "옹벽선을 클릭하면 그 단부터 바깥이 다시 사면으로 복귀", "사면");
            btnSlope.ToolTip = MakeTip("사면 변환 (DHSLOPE)",
                "옹벽선을 클릭하면 그 단부터 바깥(데이라잇 방향)이 다시 사면으로 돌아갑니다.\n" +
                "절토 옹벽의 마지막 단 사면 마무리, 성토 사면-옹벽-사면 구성에 사용. Enter=적용.",
                MakeTipImage(toWall: false));

            var pDraw = new RibbonPanelSource { Title = "도면화" };
            // ★★[v32.28 · JACK 0813] <b>도면 설정을 정지옵션에서 떼어 여기로.</b>
            //   JACK: <i>"어차피 이름은 정지옵션인데 횡단이나 종단같이 도면화관련내용이 많은데,
            //   아예 도면화챕터에 도면설정을 별도로 단추를 만들고 새로 팝업을 띄워서 관리하는건 어때?"</i>
            //   가른 기준: <b>흙의 모양을 바꾸는가(정지옵션), 도면의 모양을 바꾸는가(여기).</b>
            var btnSheetSet = MakeButton(
                "도면\n설정", "DHSHEETSET ", "횡단 간격·폭, 원지반 굴곡, 종단도 표 종류, 배경지도 화질을 정합니다", "도면설정");
            btnSheetSet.ToolTip = MakeTip("도면 설정 (DHSHEETSET)",
                "**도면을 어떻게 그릴지**를 정합니다 — 흙의 모양(정지옵션)과는 별개입니다.\n\n" +
                "· 횡단 간격 · 폭 좌/우 · 횡단도 가로 배치 수\n" +
                "· 원지반 굴곡 — 종단도 원지반선을 얼마나 단순한 직선으로 그릴지\n" +
                "· 종단도 정보표시 표 — 토공 / 관로\n" +
                "· 배경지도 화질\n\n" +
                "여기 값은 정지면 형상에 영향을 주지 않으므로 **정지면을 다시 만들 필요가 없습니다**.\n" +
                "이미 만든 종단도에 반영하려면 [종단도]를 다시 눌러 '지우고새로'를 고르세요.", null);
            var btnNori = MakeButton(
                "노리선", "DHNORI ", "정지 결과(번들)로 사면선·소단선·노리선을 한 번에 작도 — DHGRADE 실행 후 사용", "노리선");
            // [JACK 0731] 배경지도·지도끄기는 도면화 중분류로 이동(별도 패널 폐지).
            var btnMap = MakeButton(
                "배경지도", "DHMAP ", "두 점으로 범위를 찍으면 그 범위의 위성사진을 도면 좌표계에 맞춰 깔아줍니다(화질=도면설정)", "지도");
            btnMap.ToolTip = MakeTip("배경지도 (DHMAP)",
                "범위 두 모서리를 클릭하면 브이월드 위성사진을 받아\n" +
                "도면 좌표계(정지옵션의 좌표계)에 정확히 맞춰 깔아줍니다.\n" +
                "여러 번 눌러 여러 곳에 깔 수 있고, 화질은 [도면설정]에서 선택합니다.", null);
            var btnMapOff = MakeButton(
                "지도끄기", "DHMAPOFF ", "이 기능으로 깐 위성사진을 한 번에 전부 제거", "지도끄기");
            btnMapOff.ToolTip = MakeTip("지도끄기 (DHMAPOFF)",
                "배경지도로 깔아둔 위성사진을 한 번에 모두 제거합니다.\n" +
                "직접 붙이신 다른 이미지는 그대로 둡니다.", null);
            // ★[종단도 — JACK 0807] **버튼을 누르면 노선을 직접 그린다**(노란 꺾은 선) → 그 노선으로 종단면도.
            //   종전엔 다른 명령으로 선을 먼저 그려 두고 골라야 해서 손이 두 번 갔다.
            //   횡단은 아직 옆 버튼(DHSECTION)에 있다 — 종단도가 말끔해지면 같은 방식으로 옮긴다.
            var btnProf = MakeButton(
                "종단\n생성", "DHPROFILE ", "버튼을 누르고 노선을 직접 그리면(노란 선) 그 노선을 따라 종단면도를 만듭니다", "종단생성");
            btnProf.ToolTip = MakeTip("종단도 (DHPROFILE)",
                "버튼을 누르고 **화면에 노선을 직접 그립니다**.\n" +
                "점을 연달아 찍고 Enter로 끝냅니다(U=마지막 점 취소, Esc=전체 취소).\n" +
                "그 노선을 따라 원지반·정지면의 종단면도를 만듭니다.\n" +
                "그린 노란 선은 도면에 남아, 나중에 고쳐서 다시 돌릴 수 있습니다.", null);

            // ★[측점 — JACK 0810] 종단도와 횡단도가 **같은 측점**을 쓰게 하는 두 버튼.
            //   측점 목록 한 곳에 모으고([측점]), 그 목록대로 횡단 위치를 놓는다([단면검토선]).
            //   종전엔 종단은 종단대로 횡단은 횡단대로 만들어 두 도면의 측점이 어긋날 수 있었다.
            var btnStn = MakeButton(
                "측점\n추가", "DHSTATION ", "밸브실처럼 원하는 자리에 측점을 더하거나 지웁니다(목록도 여기서 봅니다)", "측점추가");
            btnStn.ToolTip = MakeTip("측점 (DHSTATION)",
                "노선 위 원하는 자리를 클릭해 **측점을 더합니다**(밸브실·밸브 등).\n" +
                "노선 꺾임점과 계획면 구배변화점은 **자동으로 잡히므로** 더할 필요가 없습니다.\n" +
                "측점은 도면에 그려지지 않고 노선에 숨겨 저장됩니다 — 목록은 이 명령에서 봅니다.\n" +
                "여기서 정한 측점이 종단도 밴드와 단면검토선에 그대로 쓰입니다.", null);

            // ★★★[JACK 0828 "측점 기능을 스플릿 버튼으로 바꾸고 그 안에
            //   현재 측점 버튼하고 새로 전/후 측점 버튼을 만들어 줘"]
            //   두 명령은 <b>손놀림이 같다</b>(찍고·지우고·목록 보고) — 다른 것은 결과뿐이라
            //   나란히 놓기보다 <b>한 자리에 겹쳐 두는 것</b>이 맞다.
            //   ※[JACK 0824 교훈] 스플릿 버튼은 <b>자기 이미지를 따로 줘야</b> 한다 —
            //     목록 항목의 아이콘을 물려받지 않아 자리가 비어 보인다.
            var btnStnFb = MakeButton(
                "전/후\n측점", "DHSTATIONFB ", "찍은 자리를 횡단면도에서만 (전)(후) 두 장으로 만듭니다", "측점전후");
            btnStnFb.ToolTip = MakeTip("전/후 측점 (DHSTATIONFB)",
                "노선 위를 클릭하면 **종단도에는 측점 하나**가 서고,\n" +
                "**횡단면도만 (전)(후) 두 장**으로 나옵니다 — 측점 기준 좌우 **5cm**.\n" +
                "옹벽·가시설은 형상에서 저절로 (전)(후)가 나오므로 여기서 찍을 필요가 없습니다.\n" +
                "**구조물이 아직 모델에 없는 자리**를 앞뒤로 보여야 할 때 씁니다.\n" +
                "주로 **구조물을 투영한 자리**에 찍습니다.", null);


            // ★★[JACK 0826] <b>[단면검토선]·[종단·횡단] 버튼을 없앴다</b> — 쓸 일이 없어졌다.
            //   검토선은 [종단도]가 측점과 함께 알아서 놓고, 종단·횡단을 한 번에 만들던 옛 명령은
            //   [종단도]·[횡단도]로 갈렸다. 명령(DHSAMPLE·DHSECTION) 자체는 남겨 둔다 —
            //   버튼만 뺀 것이라 옛 스크립트나 손버릇이 깨지지 않는다.
            // ★★★[JACK 0828] <b>지층 구성 — 우측 도킹바.</b>
            //   토적표의 풍화암·연암 칸을 채우려면 지층이 있어야 한다.
            //   팝업이 아니라 도킹바인 이유: 평면도를 보면서 <b>여러 번 찍어야</b> 하기 때문이다.
            var btnStrata = MakeButton(
                "지층\n구성", "DHSTRATA ", "시추주상도를 보고 지층을 만듭니다(우측 도킹창)", "지층");
            btnStrata.ToolTip = MakeTip("지층 구성 (DHSTRATA)",
                "우측에 도킹창이 열립니다.\n" +
                "**① 지층**을 정하고(이름은 조사보고서 그대로, 수량 분류는 다섯 중 하나)\n" +
                "**② 평면에서 찍기**로 시추 위치를 클릭하면 `BH1`부터 차례로 표에 들어갑니다.\n" +
                "지반고는 **원지반에서 자동으로 읽습니다** — 사람이 치는 것은 **각 층의 두께**뿐입니다.\n" +
                "**[확인]**을 누르면 지층면이 만들어집니다(평면에서는 숨겨 둡니다 — 종단·횡단에서만 보입니다).", null);

            var btnXsec = MakeButton(
                "횡단", "DHXVIEW ", "종단도에서 정한 측점대로 횡단면도를 만듭니다(옹벽·가시설은 (전)(후) 두 장)", "횡단");
            btnXsec.ToolTip = MakeTip("횡단도 (DHXVIEW)",
                "**[종단도]를 먼저 돌린 뒤** 이 버튼을 누릅니다." + "\n" +
                "놓을 자리를 클릭하면 그 자리에서 횡단면도를 늘어놓습니다." + "\n" +
                "옹벽·가시설 자리는 **(전)(후) 두 장**이 나옵니다 — 한쪽엔 벽이 있고 한쪽엔 없습니다." + "\n" +
                "배치·축척은 아직 손보는 중입니다(초안).", null);

            // [가져오기 — JACK 0731] 사내 지형·지적 DB에서 도면 좌표계로 바로 받아온다. 내보내기 바로 앞에 배치.
            // ★[JACK 0901] 가져오기 → <b>기타</b>. 원지반 가져오기가 부지정지로 올라가면서
            //   여기 남는 것은 보조 기능뿐이 됐다.
            var pMisc = new RibbonPanelSource { Title = "기타" };
            var btnParcel = MakeButton(
                "지적도", "DHPARCEL ", "두 점으로 범위를 찍으면 그 범위 필지 경계와 지번을 도면 좌표계로 가져옵니다", "지적");
            btnParcel.ToolTip = MakeTip("지적도 가져오기 (DHPARCEL)",
                "범위 두 모서리를 클릭하면 그 사각 범위대로 잘라서\n" +
                "필지 경계를 가져옵니다. 지번은 별도 레이어(DH-지번)에 들어갑니다.\n" +
                "※ GIS_Design_Loader server 제공", null);
            var btnContour = MakeButton(
                "서버\n지표면", "DHCONTOUR ", "지도 도킹바가 열립니다 — 지도에서 박스로 범위를 고르면 그 자리의 수치지형도 등고선을 3D로 가져오고 '원지반' 지표면까지 자동 생성(지적도 동시 가능)", "서버지표면");
            btnContour.ToolTip = MakeTip("서버지표면 (DHCONTOUR)",
                "누르면 오른쪽에 <b>지도 도킹바</b>가 열립니다(항공사진·지적도).\n" +
                "지도에서 모서리 두 곳을 클릭해 범위를 정하고 [이 범위 가져오기]를 누르면\n" +
                "그 자리의 등고선이 도면 좌표로 들어오고 도킹바는 스스로 닫힙니다.\n\n" +
                "빈 도면에서 시작할 때 쓰세요 — 검은 화면에서 찍을 곳을 찾지 않아도 됩니다.\n\n" +
                "· [지적도도 같이]를 체크하면 같은 범위의 지적도까지 한 번에 들어옵니다.\n" +
                "· 사내 서버의 수치지형도 등고선을 표고가 들어간 3D 선으로 가져오고,\n" +
                "  곧바로 '원지반' 지표면을 만듭니다(등고선 표시 주 10m·보조 2m).\n" +
                "· 만들어진 원지반으로 바로 [정지면 생성]을 실행하면 됩니다.", null);

            // ★★★[JACK 0901] 브이월드에서 내려받은 <b>수치지도 DXF</b>로 원지반을 만든다.
            //   사내망이 없거나 서버에 없는 자리에서도 쓸 수 있는 두 번째 길이다.
            var btnNgii = MakeButton(
                "수치지도\nDXF", "DHNGII ", "국토지리정보원 수치지도 DXF를 골라 등고선·표고점만 읽어 '원지반' 지표면을 만듭니다(도엽 여러 장 동시 선택 가능)", "수치지도");
            btnNgii.ToolTip = MakeTip("수치지도 DXF (DHNGII)",
                "브이월드에서 받은 수치지도 DXF를 고르면 <b>도면을 열지 않고</b>\n" +
                "등고선·표고점만 읽어 '원지반' 지표면을 만듭니다.\n\n" +
                "· 도엽이 여러 장이면 <b>한 번에 골라</b> 이어진 지표면 하나로 만듭니다.\n" +
                "· 읽는 레이어: 등고선(F001…) · 표고점(F002…). 표고를 적어 둔 글자는 뺍니다.\n" +
                "· <b>표고가 0인 등고선·표고점은 버립니다</b> — 측량이 안 된 자리라\n" +
                "  그대로 두면 지표면에 절벽이 생깁니다.\n\n" +
                "좌표계는 정지옵션을 따릅니다(DXF에는 좌표계가 안 담겨 있습니다).", null);
            // ★★★[JACK 0901 "원지반 가져오기 스플릿 버튼 만들고 그 안에 수치지도 DXF, 서버 지표면 넣어 줘"]
            //   원지반을 얻는 길이 둘이 됐다 — 나란히 두면 <b>어느 것을 눌러야 하나</b>가 된다.
            //   하나로 묶고 화살표로 고르게 한다.
            var splitGround = new RibbonSplitButton
            {
                Text = "원지반\n가져오기",
                ShowText = true,
                ShowImage = true,
                // ★스플릿 버튼은 <b>자기 이미지를 따로 줘야</b> 한다 — 항목 이미지를 안 물려받는다(§v32 그 자리).
                LargeImage = MakeGlyph("원지반"),
                Image = MakeGlyph("원지반"),
                Size = RibbonItemSize.Large,
                Orientation = System.Windows.Controls.Orientation.Vertical,
                IsSplit = true,
                IsSynchronizedWithCurrentItem = false,
                ListStyle = RibbonSplitButtonListStyle.List,
                ToolTip = MakeTip("원지반 가져오기",
                    "**수치지도 DXF** — 브이월드에서 내려받은 파일로 만듭니다(사내망 없어도 됨).\n" +
                    "**서버 지표면** — 지도에서 범위를 골라 사내 서버에서 받습니다(VPN 필요).\n\n" +
                    "둘 다 결과는 같은 '원지반' 지표면입니다 — 이후 공정은 똑같습니다.\n" +
                    "아래 화살표를 눌러 고릅니다.", null),
            };
            splitGround.Items.Add(btnNgii);
            splitGround.Items.Add(btnContour);
            splitGround.Current = btnNgii;

            var pExport = new RibbonPanelSource { Title = "내보내기" };
            var btnInfra = MakeButton(
                "INFRA\nWORKS", "DHINFRA ", "InfraWorks 기초자료 내보내기 — 폴더 선택 후 지형·옹벽3D·SHP·위성GeoTIFF·토공량을 내보냄(있는 것만). DHGRADE 후 사용", "infra");

            // ★★★[JACK 0901 "수치지도를 불러 버리면 계획지역보다 너무 클 수 있어서"]
            //   도엽 한 장이 2×3km인데 현장은 200m다 — 삼각형 수만 개가 계산을 매번 따라다닌다.
            var btnCrop = MakeButton(
                "원지형\n자르기", "DHCROP ", "드래그로 박스를 그리면 그 안의 지형만 남기고 나머지는 지웁니다", "자르기");
            btnCrop.ToolTip = MakeTip("원지형 자르기 (DHCROP)",
                "드래그로 <b>남길 범위</b>를 그리면 그 안의 지형만 남습니다.\n\n" +
                "수치지도 도엽은 한 장이 2×3km라 현장보다 훨씬 넓습니다 —\n" +
                "잘라 두면 종단·횡단·수량이 눈에 띄게 빨라집니다.\n\n" +
                "· <b>먼저 잘라 보고, 쓸 만한지 확인한 뒤에</b> 원본을 지웁니다 —\n" +
                "  범위가 지형 밖이면 아무것도 지우지 않습니다.\n" +
                "· 범위 밖에 통째로 있는 원본 등고선·표고점도 같이 정리합니다.", null);

            var btnReset = MakeButton(
                "초기화", "DHRESET ", "정지면 생성 전(원지반+계획폴리곤) 상태로 초기화 — DH가 만든 지표면·선을 모두 삭제", "초기화");
            btnReset.ToolTip = MakeTip("초기화 (DHRESET)",
                "정지 지표면(정지면_DH 등)과 사면선·소단선·노리선·옹벽선 등\n" +
                "DH가 만든 객체를 모두 지워 '정지면 생성 전' 상태로 되돌립니다.\n" +
                "원지반과 계획폴리곤은 그대로 유지 — 부지를 바꿔 다시 검토할 때 사용.", null);

            // ★★★[JACK 0901 UI 재배치] <b>일하는 순서대로</b> 늘어놓는다 —
            //   정지옵션 → 원지반 가져오기 → 계획부지 생성 → 사면 수정 → 보기.
            //   단추를 만드는 자리와 <b>늘어놓는 자리를 갈랐다</b>: 순서를 바꿀 때
            //   설명문 수십 줄을 통째로 옮기지 않아도 여기 한 곳만 고치면 된다.
            var splitSlope = new RibbonSplitButton
            {
                Text = "사면\n수정",
                ShowText = true,
                ShowImage = true,
                LargeImage = MakeGlyph("사면수정"),
                Image = MakeGlyph("사면수정"),
                Size = RibbonItemSize.Large,
                Orientation = System.Windows.Controls.Orientation.Vertical,
                IsSplit = true,
                IsSynchronizedWithCurrentItem = false,
                ListStyle = RibbonSplitButtonListStyle.List,
                ToolTip = MakeTip("사면 수정",
                    "**옹벽 변환** — 고른 선부터 바깥 단을 옹벽으로.\n" +
                    "**사면 변환** — 옹벽선을 골라 그 단부터 다시 사면으로.\n" +
                    "아래 화살표를 눌러 고릅니다.", null),
            };
            splitSlope.Items.Add(btnWall);
            splitSlope.Items.Add(btnSlope);
            splitSlope.Current = btnWall;

            var splitProf = new RibbonSplitButton
            {
                Text = "종단",
                ShowText = true,
                ShowImage = true,
                LargeImage = MakeGlyph("종단"),
                Image = MakeGlyph("종단"),
                Size = RibbonItemSize.Large,
                Orientation = System.Windows.Controls.Orientation.Vertical,
                IsSplit = true,
                IsSynchronizedWithCurrentItem = false,
                ListStyle = RibbonSplitButtonListStyle.List,
                ToolTip = MakeTip("종단",
                    "**지층 구성** — 시추주상도로 지층을 만듭니다(우측 도킹창).\n" +
                    "**종단 생성** — 노선을 그리면 그 노선의 종단면도.\n" +
                    "**측점 추가** — 밸브실처럼 원하는 자리에 측점을 더합니다.\n" +
                    "**전/후 측점** — 횡단만 (전)(후) 두 장으로.\n" +
                    "아래 화살표를 눌러 고릅니다.", null),
            };
            splitProf.Items.Add(btnStrata);
            splitProf.Items.Add(btnProf);
            splitProf.Items.Add(btnStn);
            splitProf.Items.Add(btnStnFb);
            splitProf.Current = btnProf;

            // ── 패널 늘어놓기 ─────────────────────────────────────────────────
            tab.Panels.Add(new RibbonPanel { Source = pGrade });
            pGrade.Items.Add(Spacer());
            pGrade.Items.Add(btnSet);
            pGrade.Items.Add(Spacer());
            pGrade.Items.Add(splitGround);
            pGrade.Items.Add(Spacer());
            pGrade.Items.Add(splitSurf);
            pGrade.Items.Add(Spacer());
            pGrade.Items.Add(splitSlope);
            pGrade.Items.Add(Spacer());
            pGrade.Items.Add(splitView);
            pGrade.Items.Add(Spacer());

            tab.Panels.Add(new RibbonPanel { Source = pDraw });
            pDraw.Items.Add(Spacer());
            pDraw.Items.Add(btnSheetSet);
            pDraw.Items.Add(Spacer());
            pDraw.Items.Add(btnNori);
            pDraw.Items.Add(Spacer());
            pDraw.Items.Add(splitProf);
            pDraw.Items.Add(Spacer());
            pDraw.Items.Add(btnXsec);
            pDraw.Items.Add(Spacer());

            tab.Panels.Add(new RibbonPanel { Source = pMisc });
            pMisc.Items.Add(Spacer());
            pMisc.Items.Add(btnCrop);
            pMisc.Items.Add(Spacer());
            pMisc.Items.Add(btnParcel);
            pMisc.Items.Add(Spacer());
            pMisc.Items.Add(btnMap);
            pMisc.Items.Add(Spacer());
            pMisc.Items.Add(btnMapOff);
            pMisc.Items.Add(Spacer());
            pMisc.Items.Add(btnReset);
            pMisc.Items.Add(Spacer());

            tab.Panels.Add(new RibbonPanel { Source = pExport });
            pExport.Items.Add(Spacer());
            pExport.Items.Add(btnInfra);
            pExport.Items.Add(Spacer());
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
    /// <summary>눈 모양 하나 — 보기 계열이 같이 쓴다(가로폭 w, 중심 cx,cy).</summary>
    private static void DrawEye(DrawingContext dc, Pen pen, double cx, double cy, double w)
    {
        var eye = new StreamGeometry();
        using (var g = eye.Open())
        {
            g.BeginFigure(new Point(cx - w, cy), false, false);
            g.QuadraticBezierTo(new Point(cx, cy - w * 0.92), new Point(cx + w, cy), true, false);
            g.QuadraticBezierTo(new Point(cx, cy + w * 0.92), new Point(cx - w, cy), true, false);
        }
        eye.Freeze();
        dc.DrawGeometry(null, pen, eye);
        dc.DrawEllipse(Brushes.Transparent, pen, new Point(cx, cy), w * 0.33, w * 0.33);
    }

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
                // ★★★[JACK 0901 "UI에 그림들이 좀 중복되는 애들이 많은데 직관적인 UI 그림으로
                //   다시 다 만들어 줘. 중복되지 않게"]
                //
                //   <b>원인: 이름이 13개인데 단추가 23개였다.</b> 없는 이름은 전부 default로 떨어져
                //   다섯 단추가 <b>똑같은 '내보내기 상자'</b>를 달고 있었다. 그림이 이름을 못 따라간 것이다.
                //   이제 <b>단추마다 제 이름</b>을 갖고, 아래 switch가 그 전부를 그린다.
                //
                //   색으로도 갈래를 읽게 한다 —
                //   <b>갈색=땅(원지반) · 초록=계획(정지) · 파랑=보기 · 주황=도면 · 회색=보조</b>
                switch (kind)
                {
                    // ── 부지정지 ─────────────────────────────────────────────
                    case "설정": // 슬라이더 3줄 + 노브 — 값을 정하는 곳
                        var gy = P(0x9e, 0xb0, 0xc4);
                        for (int i = 0; i < 3; i++)
                        {
                            double y = 8 + i * 8;
                            dc.DrawLine(gy, new Point(5, y), new Point(27, y));
                            dc.DrawEllipse(Brushes.White, gy, new Point(9 + i * 7, y), 2.6, 2.6);
                        }
                        break;

                    case "원지반": // 등고선이 감긴 언덕 — 있는 그대로의 땅
                        var gnd = P(0x8d, 0x6e, 0x48);
                        var hill = new StreamGeometry();
                        using (var g = hill.Open())
                        {
                            g.BeginFigure(new Point(3, 27), false, false);
                            g.QuadraticBezierTo(new Point(11, 8), new Point(19, 20), true, false);
                            g.QuadraticBezierTo(new Point(24, 27), new Point(29, 22), true, false);
                        }
                        hill.Freeze();
                        dc.DrawGeometry(null, gnd, hill);
                        dc.DrawLine(P(0xc0, 0xa0, 0x80), new Point(7, 22), new Point(15, 22));
                        dc.DrawLine(P(0xc0, 0xa0, 0x80), new Point(9, 17), new Point(14, 17));
                        break;

                    case "수치지도": // 접힌 도면 한 장 + 그 안의 등고선 — 파일에서 읽는다
                        var dxf = P(0x8d, 0x6e, 0x48);
                        var sheet = new StreamGeometry();
                        using (var g = sheet.Open())
                        {
                            g.BeginFigure(new Point(7, 4), false, true);
                            g.LineTo(new Point(20, 4), true, false);
                            g.LineTo(new Point(25, 9), true, false);
                            g.LineTo(new Point(25, 28), true, false);
                            g.LineTo(new Point(7, 28), true, false);
                        }
                        sheet.Freeze();
                        dc.DrawGeometry(null, dxf, sheet);
                        dc.DrawLine(dxf, new Point(20, 4), new Point(20, 9));   // 접힌 모서리
                        dc.DrawLine(dxf, new Point(20, 9), new Point(25, 9));
                        var thin = P(0xc0, 0xa0, 0x80);
                        dc.DrawLine(thin, new Point(10, 22), new Point(22, 22));
                        dc.DrawLine(thin, new Point(11, 18), new Point(21, 18));
                        dc.DrawLine(thin, new Point(13, 14), new Point(19, 14));
                        break;

                    case "서버지표면": // 구름 + 내려받기 화살표 — 서버에서 받는다
                        var srv = P(0x8d, 0x6e, 0x48);
                        dc.DrawEllipse(null, srv, new Point(12, 12), 6.0, 5.0);
                        dc.DrawEllipse(null, srv, new Point(21, 13), 5.0, 4.2);
                        dc.DrawLine(srv, new Point(16, 18), new Point(16, 28));  // 화살 축
                        dc.DrawLine(srv, new Point(11, 23), new Point(16, 28));  // 화살촉
                        dc.DrawLine(srv, new Point(21, 23), new Point(16, 28));
                        break;

                    case "계획부지": // 다듬어진 평지 + 양쪽 사면 — 만들려는 부지
                        var pad = P(0x3f, 0xa8, 0x5e);
                        dc.DrawLine(pad, new Point(9, 16), new Point(23, 16));   // 평지
                        dc.DrawLine(pad, new Point(3, 26), new Point(9, 16));    // 왼쪽 사면
                        dc.DrawLine(pad, new Point(23, 16), new Point(29, 26));  // 오른쪽 사면
                        dc.DrawLine(P(0xb8, 0xd8, 0xc0), new Point(2, 26), new Point(30, 26)); // 지반선
                        break;

                    case "정지면": // 계단(초록) — 절성토 계단 정지면
                        var gr = P(0x6a, 0xc8, 0x7a);
                        var st = new[] { new Point(4, 27), new Point(4, 21), new Point(12, 21),
                            new Point(12, 15), new Point(20, 15), new Point(20, 9), new Point(28, 9) };
                        for (int i = 0; i + 1 < st.Length; i++) dc.DrawLine(gr, st[i], st[i + 1]);
                        break;

                    case "터파기": // 파인 구덩이(갈색) — 바닥 + 양쪽 법면
                        var ex = P(0xb0, 0x7a, 0x46);
                        var pit = new[] { new Point(3, 9), new Point(11, 24), new Point(21, 24), new Point(29, 9) };
                        for (int i = 0; i + 1 < pit.Length; i++) dc.DrawLine(ex, pit[i], pit[i + 1]);
                        dc.DrawLine(P(0x8a, 0x8a, 0x8a), new Point(2, 9), new Point(30, 9));   // 목표면(지표)
                        break;

                    case "사면수정": // 사면 ↔ 옹벽 — 서로 바꾸는 두 방향 화살표
                        var sw = P(0x9b, 0x6b, 0xd6);
                        dc.DrawLine(sw, new Point(3, 26), new Point(12, 9));       // 사면 쪽
                        dc.DrawLine(sw, new Point(22, 9), new Point(22, 26));      // 옹벽 쪽(수직)
                        dc.DrawLine(sw, new Point(22, 26), new Point(29, 26));
                        dc.DrawLine(sw, new Point(13, 15), new Point(19, 15));     // 화살표 →
                        dc.DrawLine(sw, new Point(17, 12), new Point(19, 15));
                        dc.DrawLine(sw, new Point(17, 18), new Point(19, 15));
                        break;

                    case "옹벽": // 쌓은 블록 벽(회청색) — 수직 벽면
                        var wl = P(0x6b, 0x86, 0xa8);
                        dc.DrawLine(wl, new Point(10, 5), new Point(10, 27));
                        dc.DrawLine(wl, new Point(22, 5), new Point(22, 27));
                        for (int i = 0; i <= 3; i++) dc.DrawLine(wl, new Point(10, 5 + i * 7.3), new Point(22, 5 + i * 7.3));
                        dc.DrawLine(P(0x8a, 0x8a, 0x8a), new Point(3, 27), new Point(29, 27)); // 지반
                        break;

                    case "사면": // 비탈 + 빗금 — 흙으로 돌아간다
                        var sl = P(0xd2, 0x8c, 0x3c);
                        dc.DrawLine(sl, new Point(4, 27), new Point(26, 7));
                        for (int i = 1; i <= 3; i++)
                        {
                            double t = i / 4.0;
                            var bp = new Point(4 + 22 * t, 27 - 20 * t);
                            dc.DrawLine(sl, bp, new Point(bp.X + 4.0, bp.Y + 4.0));
                        }
                        dc.DrawLine(P(0x8a, 0x8a, 0x8a), new Point(3, 28), new Point(29, 28));
                        break;

                    // ── 보기(전부 파랑 계열, 무엇을 보이느냐만 다르다) ─────────
                    case "보기": // 눈 — 무엇을 보일지
                        DrawEye(dc, P(0x4a, 0x90, 0xd9), 16, 16, 12);
                        break;

                    case "보기전부": // 눈 + 세 겹 — 전부 보기
                        DrawEye(dc, P(0x4a, 0x90, 0xd9), 16, 10, 11);
                        var al = P(0x4a, 0x90, 0xd9);
                        for (int i = 0; i < 3; i++) dc.DrawLine(al, new Point(7, 21 + i * 4), new Point(25, 21 + i * 4));
                        break;

                    case "보기원지반": // 언덕만
                        var vg = P(0x4a, 0x90, 0xd9);
                        var vh = new StreamGeometry();
                        using (var g = vh.Open())
                        {
                            g.BeginFigure(new Point(3, 25), false, false);
                            g.QuadraticBezierTo(new Point(13, 7), new Point(22, 19), true, false);
                            g.QuadraticBezierTo(new Point(26, 24), new Point(29, 21), true, false);
                        }
                        vh.Freeze();
                        dc.DrawGeometry(null, vg, vh);
                        dc.DrawLine(P(0xb0, 0xcc, 0xe8), new Point(3, 29), new Point(29, 29));
                        break;

                    case "보기계획": // 평지만
                        var vp = P(0x4a, 0x90, 0xd9);
                        dc.DrawLine(vp, new Point(9, 14), new Point(23, 14));
                        dc.DrawLine(vp, new Point(3, 24), new Point(9, 14));
                        dc.DrawLine(vp, new Point(23, 14), new Point(29, 24));
                        dc.DrawLine(P(0xb0, 0xcc, 0xe8), new Point(3, 29), new Point(29, 29));
                        break;

                    case "보기터파기": // 구덩이만
                        var ve = P(0x4a, 0x90, 0xd9);
                        var vpit = new[] { new Point(4, 11), new Point(12, 25), new Point(20, 25), new Point(28, 11) };
                        for (int i = 0; i + 1 < vpit.Length; i++) dc.DrawLine(ve, vpit[i], vpit[i + 1]);
                        dc.DrawLine(P(0xb0, 0xcc, 0xe8), new Point(3, 11), new Point(29, 11));
                        break;

                    // ── 도면화 ───────────────────────────────────────────────
                    case "도면설정": // 도곽 + 톱니 — 도면을 어떻게 그릴지
                        var ds = P(0xf0, 0xa8, 0x3a);
                        dc.DrawRectangle(null, ds, new Rect(4, 6, 17, 20));
                        dc.DrawLine(ds, new Point(7, 11), new Point(18, 11));
                        dc.DrawLine(ds, new Point(7, 16), new Point(18, 16));
                        dc.DrawEllipse(null, ds, new Point(24, 23), 4.5, 4.5);   // 톱니(설정)
                        for (int i = 0; i < 4; i++)
                        {
                            double a = i * System.Math.PI / 2 + 0.4;
                            dc.DrawLine(ds, new Point(24 + 4.5 * System.Math.Cos(a), 23 + 4.5 * System.Math.Sin(a)),
                                            new Point(24 + 7.5 * System.Math.Cos(a), 23 + 7.5 * System.Math.Sin(a)));
                        }
                        break;

                    case "노리선": // 사면(대각선) + 빗금 틱
                        var or_ = P(0xf0, 0xa8, 0x3a);
                        dc.DrawLine(or_, new Point(6, 27), new Point(27, 6));
                        for (int i = 1; i <= 3; i++)
                        {
                            double t = i / 4.0;
                            var bp = new Point(6 + 21 * t, 27 - 21 * t);
                            dc.DrawLine(or_, bp, new Point(bp.X + 4.5, bp.Y + 4.5));
                        }
                        break;

                    case "종단": // 도곽 안의 종단선 — 세로로 자른 그림
                        var pf = P(0xe0, 0x7b, 0x39);
                        dc.DrawRectangle(null, P(0xd8, 0xc0, 0xa8), new Rect(3, 6, 26, 21));
                        var pg = new StreamGeometry();
                        using (var g = pg.Open())
                        {
                            g.BeginFigure(new Point(5, 22), false, false);
                            g.QuadraticBezierTo(new Point(12, 9), new Point(18, 17), true, false);
                            g.QuadraticBezierTo(new Point(23, 23), new Point(27, 12), true, false);
                        }
                        pg.Freeze();
                        dc.DrawGeometry(null, pf, pg);
                        break;

                    case "지층": // 세 겹 띠 — 토사·풍화암·연암
                        dc.DrawLine(P(0xc8, 0xa8, 0x70), new Point(4, 10), new Point(28, 10));
                        dc.DrawLine(P(0xa8, 0x86, 0x58), new Point(4, 17), new Point(28, 17));
                        dc.DrawLine(P(0x78, 0x66, 0x50), new Point(4, 24), new Point(28, 24));
                        dc.DrawEllipse(Brushes.White, P(0x50, 0x50, 0x50), new Point(9, 7), 2.2, 2.2); // 시추공
                        break;

                    case "종단생성": // 꺾은 노선 + 꼭짓점 — 선을 직접 그린다
                        var rt = P(0xe0, 0x7b, 0x39);
                        var route = new[] { new Point(4, 24), new Point(12, 12), new Point(20, 20), new Point(28, 7) };
                        for (int i = 0; i + 1 < route.Length; i++) dc.DrawLine(rt, route[i], route[i + 1]);
                        foreach (var q in route) dc.DrawEllipse(Brushes.White, rt, q, 2.2, 2.2);
                        break;

                    case "측점추가": // 노선 위 눈금 + 더하기
                        var stn = P(0xe0, 0x7b, 0x39);
                        dc.DrawLine(stn, new Point(3, 20), new Point(29, 20));
                        for (int i = 0; i < 3; i++) dc.DrawLine(stn, new Point(6 + i * 8, 16), new Point(6 + i * 8, 24));
                        dc.DrawLine(stn, new Point(26, 6), new Point(26, 14));   // +
                        dc.DrawLine(stn, new Point(22, 10), new Point(30, 10));
                        break;

                    case "측점전후": // 눈금 하나 + 앞뒤 화살표
                        var fb = P(0xe0, 0x7b, 0x39);
                        dc.DrawLine(fb, new Point(3, 20), new Point(29, 20));
                        dc.DrawLine(fb, new Point(16, 13), new Point(16, 27));   // 측점
                        dc.DrawLine(fb, new Point(4, 9), new Point(12, 9));      // ← 전
                        dc.DrawLine(fb, new Point(4, 9), new Point(7, 6));
                        dc.DrawLine(fb, new Point(4, 9), new Point(7, 12));
                        dc.DrawLine(fb, new Point(20, 9), new Point(28, 9));     // 후 →
                        dc.DrawLine(fb, new Point(28, 9), new Point(25, 6));
                        dc.DrawLine(fb, new Point(28, 9), new Point(25, 12));
                        break;

                    case "횡단": // 원지반선 + 그 위 계획단면 — 가로로 자른 그림
                        var xs = P(0xe0, 0x7b, 0x39);
                        var grd = new StreamGeometry();
                        using (var g = grd.Open())
                        {
                            g.BeginFigure(new Point(2, 24), false, false);
                            g.QuadraticBezierTo(new Point(10, 14), new Point(17, 20), true, false);
                            g.QuadraticBezierTo(new Point(24, 26), new Point(30, 17), true, false);
                        }
                        grd.Freeze();
                        dc.DrawGeometry(null, P(0xb8, 0xa0, 0x88), grd);
                        dc.DrawLine(xs, new Point(10, 12), new Point(22, 12));   // 계획 평지
                        dc.DrawLine(xs, new Point(5, 21), new Point(10, 12));
                        dc.DrawLine(xs, new Point(22, 12), new Point(27, 21));
                        break;

                    // ── 기타 ─────────────────────────────────────────────────
                    case "자르기": // 언덕 위에 점선 네모 + 가위 — 남길 자리만 오려낸다
                        var cg = P(0xb8, 0xa0, 0x88);
                        var ch = new StreamGeometry();
                        using (var g = ch.Open())
                        {
                            g.BeginFigure(new Point(2, 26), false, false);
                            g.QuadraticBezierTo(new Point(11, 10), new Point(19, 20), true, false);
                            g.QuadraticBezierTo(new Point(25, 27), new Point(30, 18), true, false);
                        }
                        ch.Freeze();
                        dc.DrawGeometry(null, cg, ch);
                        // 남길 범위 — 점선 네모(빨강)
                        var cut = P(0xd8, 0x5a, 0x4a);
                        cut.DashStyle = new DashStyle(new double[] { 2.2, 1.8 }, 0);
                        dc.DrawRectangle(null, cut, new Rect(8, 8, 15, 15));
                        break;

                    case "지적": // 필지 경계(노랑) — 나뉜 땅
                        var pc = P(0xe8, 0xc0, 0x4a);
                        dc.DrawRectangle(null, pc, new Rect(4, 6, 24, 20));
                        dc.DrawLine(pc, new Point(15, 6), new Point(15, 26));
                        dc.DrawLine(pc, new Point(15, 16), new Point(28, 16));
                        break;

                    case "지도": // 지구본(파랑) — 배경 위성지도
                        var mp = P(0x3f, 0x8f, 0xd8);
                        dc.DrawEllipse(null, mp, new Point(16, 16), 12.0, 12.0);
                        dc.DrawEllipse(null, mp, new Point(16, 16), 5.0, 12.0);
                        dc.DrawLine(mp, new Point(4, 16), new Point(28, 16));
                        break;

                    case "지도끄기": // 지구본 + 빗금 — 끈다
                        var mo = P(0xd0, 0x5a, 0x5a);
                        dc.DrawEllipse(null, mo, new Point(16, 16), 11.0, 11.0);
                        dc.DrawLine(mo, new Point(7, 25), new Point(25, 7));
                        break;

                    case "초기화": // 되돌리는 화살표(회색)
                        var rs = P(0x8a, 0x9a, 0xaa);
                        var arc = new StreamGeometry();
                        using (var g = arc.Open())
                        {
                            g.BeginFigure(new Point(26, 16), false, false);
                            g.ArcTo(new Point(9, 10), new Size(10, 10), 0, true, SweepDirection.Clockwise, true, false);
                        }
                        arc.Freeze();
                        dc.DrawGeometry(null, rs, arc);
                        dc.DrawLine(rs, new Point(9, 10), new Point(9, 17));
                        dc.DrawLine(rs, new Point(9, 10), new Point(15, 10));
                        break;

                    // ── 내보내기 ─────────────────────────────────────────────
                    default: // infra: 상자 + 내보내기 화살표(파랑)
                        var bl = P(0x4a, 0x90, 0xe2);
                        dc.DrawRectangle(null, bl, new Rect(5, 13, 13, 14));
                        dc.DrawLine(bl, new Point(15, 8), new Point(28, 8));
                        dc.DrawLine(bl, new Point(24, 4), new Point(28, 8));
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

    /// <summary>★[JACK 0825] 상태에 따라 켜고 꺼야 하는 보기 버튼 — 만들 때 여기 담아 둔다.</summary>
    private static RibbonButton? _btnViewPlan, _btnViewExcav;

    /// <summary>★★[JACK 0825] <b>계획지표면·터파기가 없으면 그 보기 버튼을 끈다.</b>
    ///
    /// <para>도면을 바꾸거나 우리 명령이 끝날 때마다 부른다. 실패해도 조용히 넘어간다 —
    /// 버튼 상태는 <b>편의</b>이지 안전장치가 아니다(명령 자체도 없으면 안내하고 물러난다).</para></summary>
    internal static void RefreshEnabled()
    {
        try
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            bool hasPlan = false, hasExc = false;
            if (doc != null)
            {
                var (pp, ee) = Commands.ViewSurfaceCommand.WhatExists(doc.Database);
                hasPlan = pp; hasExc = ee;
            }
            if (_btnViewPlan != null) _btnViewPlan.IsEnabled = hasPlan;
            if (_btnViewExcav != null) _btnViewExcav.IsEnabled = hasExc;
        }
        catch { }
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
