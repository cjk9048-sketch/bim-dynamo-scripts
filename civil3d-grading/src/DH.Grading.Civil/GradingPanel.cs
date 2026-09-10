using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;
using AcDoc = Autodesk.AutoCAD.ApplicationServices.Document;

namespace DH.Grading.Civil;

/// <summary>★★★[계획 5a단계 · JACK 0908 <i>"이것도 도킹창으로 해서 아예 전문적으로 가야 될 것 같아 …
/// 정지옵션의 기능들을 모두 그 도킹창 안에 이식해 줘. 예시 그림까지도."</i>]
/// <b>계획부지 정지 — 도킹창(값 부분).</b>
///
/// <para><b>왜 도킹창인가.</b> 팝업은 값을 정하고 <b>닫아야</b> 일을 시작한다.
/// 구배 하나를 바꾸려면 창을 닫고 → 옵션을 열고 → 고치고 → 명령을 다시 돌려야 했다.
/// 도킹창은 <b>열어 둔 채로</b> 고치고 바로 다시 만든다 — 지층 창이 이미 그렇게 쓰인다.</para>
///
/// <para><b>이번 판(5a)은 값만 다룬다.</b> 찍기·생성·사면수정은 다음 단계다 —
/// ★<b>눌러도 아무 일 없는 단추를 내보내지 않는다</b>(커밋 <c>d1fa6ee</c>의 교훈).
/// 그래서 아직 없는 것은 <b>칸을 아예 안 만들고</b> 맨 위에 한 줄로 밝힌다.</para>
///
/// <para><b>베끼지 않았다.</b> 검사·저장은 <see cref="GradingForm"/>, 예시 그림은
/// <see cref="SlopeDiagram"/> — 팝업과 <b>같은 것</b>을 부른다.
/// 두 화면이 각자 갖고 있으면 한쪽만 고쳐진다(§20·§26).</para></summary>
internal sealed class GradingPanel : UserControl
{
    /// <summary>★[JACK 0910] 이 창의 <b>아코디언 묶음 이름</b> — 네 칸이 한 묶음이라
    /// <b>한 번에 하나만</b> 열린다. 터파기 창은 다른 이름을 쓰므로 서로 안 닫는다.</summary>
    private const string G = "정지창";

    private readonly TextBox _cutH, _cutW, _cutS, _fillH, _fillW, _fillS, _tInt, _tW;
    private readonly RadioButton _shapeMiter, _shapeRound;
    private readonly CheckBox _terrace;
    // ★[JACK 0910] 옹벽 형태 콤보는 <b>[기타 설정]</b>으로 옮겼다 — 이 창에는 없다.
    //   (인프라웍스 내보내기·노리선만 쓰는 값이라 정지 제원과 성격이 다르다.)
    /// <summary>★[JACK 0910] 그림은 <b>두 장 다</b> — 절토·성토를 나란히 두고 고르는 단추를 없앴다.
    /// <para>종전(§77 ⑧)엔 맨 위에 한 장만 크게 두고 라디오로 갈아 끼웠다.
    /// 칸 접기가 생기면서 자리 문제가 풀렸고, <b>고치는 값 옆에 그 그림</b>이 있는 편이 낫다.</para></summary>
    private readonly Canvas _canvasCut, _canvasFill;
    // ★[JACK 0910] 안내문·결과 줄은 화면에서 걷었다 — <see cref="Say"/>는 명령창으로만 적는다.

    // ── ★[5b] 대상 ────────────────────────────────────────────────────────
    private readonly Button _pickPlan, _pickGround, _build;
    private readonly Button _toWall, _toSlope;
    private readonly TextBlock _planWhat, _groundWhat;
    /// <summary>★[검토 M-1] <c>1. 대상</c>을 접었을 때 제목 옆에 뜨는 상태 한 줄.</summary>
    private readonly TextBlock _targetBadge;
    private readonly RadioButton _append, _restart;
    /// <summary>이어서/새로시작 줄 — ★<c>StackPanel</c>이 아니라 <c>DockPanel</c>이다.
    /// 종류를 짐작해 캐스트했다가 <b>언제나 null</b>이 되어 숨김이 통째로 죽었다(검토 0909).</summary>
    private readonly Panel _modeRow;

    /// <summary>★<b>이 도면에 맞췄을 때</b>의 사면형상 — 팝업의 <c>_miterAtOpen</c>에 해당한다.
    ///
    /// <para>팝업은 <i>"창을 연 순간"</i>이 있어 그때 값을 적어 두면 됐다.
    /// 그런데 도킹창은 <b>늘 떠 있어서 그 순간이 없다</b>(검토 0908이 짚은 그대로).
    /// → <b>도면에 맞추는 순간</b>을 그 자리로 삼는다 — 그것이 곧 "이 도면의 값을 받아 온" 때다.</para></summary>
    private bool _miterAtSync;

    /// <summary>지금 값을 채워 넣는 중인가 — 그때 오는 <c>TextChanged</c>로 그림을 다시 그리지 않는다.</summary>
    private bool _loading;

    /// <summary>★[JACK 0910] <c>2. 계획부지생성</c>을 접었을 때 제목 옆에 뜨는 한 마디(값이 틀리면 ⚠).</summary>
    private readonly TextBlock _slopeBadge;

    /// <summary>단추를 켜도 되는가 — 세 조건을 <b>따로</b> 재고 <see cref="SyncEnabled"/>가 합친다.
    /// <para><c>_picksOk</c>=고른 것이 갖춰졌나 · <c>_fixOk</c>=고칠 정지면이 있나 ·
    /// <c>_valOk</c>=칸의 숫자가 쓸 수 있는 값인가.</para></summary>
    private bool _picksOk, _fixOk, _valOk = true;

    /// <summary>절토 쪽 값 세 칸 줄 — <b>이 줄이 한 줄에 들어가는가</b>가 창 폭을 정한 근거라
    /// 열릴 때 실제로 재어 로그에 적는다(<see cref="LogWidthOnce"/>).</summary>
    private readonly WrapPanel _trioCut;

    internal GradingPanel()
    {
        var root = new StackPanel { Margin = new Thickness(10, 4, 10, 10) };
        DhBrand.Apply(this);
        Background = DhBrand.Wall;

        // ── 머리띠 ────────────────────────────────────────────────────────
        // ★[JACK 0910 "부연설명 다없애"] 부제를 비웠다 — 제목만 남긴다.
        var head = DhBrand.Header("계획부지 정지", "",
                                  drag: null, onClose: null, logoHeight: 17, titleSize: 13,
                                  pad: new Thickness(0, 0, 0, 6));
        head.Background = Brushes.Transparent;

        // ★★[JACK 0910 <i>"계획부지정지 대제목아래 … 부연설명 다없애"</i>]
        //   차례 안내(<c>_status</c>)와 결과 한 줄(<c>_said</c>)을 <b>화면에서 걷었다</b>.
        //   ★<b>말이 사라지는 것은 아니다</b> — <see cref="Say"/>가 그대로 <b>명령창</b>에 적는다.
        //     화면은 값과 그림만 남기고, 읽을 말은 명령창에서 본다.

        // ══ ★[5b] 대상 ═══════════════════════════════════════════════════
        //   JACK 0908: <i>"창 안에서 선택버튼을 누르고 폴리곤을 선택하면 빨간색으로 바뀌고,
        //   이어서 지표면 선택하고 엔터 또는 도킹창의 지표면생성 버튼을 누르면 부지가 정지되게"</i>
        //   ★단추는 <b>이름 있는 명령</b>을 부른다 — 창 클릭은 명령 문맥이 아니라
        //     여기서 바로 도면을 찍으면 안 되거나 AutoCAD가 죽는다(<see cref="PickSession"/>).
        // ★[JACK 0910] 칸마다 <b>접었다 펼친다</b> — 다섯 칸이 다 펼쳐지면 값 줄이 화면 밖으로 나간다.
        //   내용은 <c>root</c>가 아니라 <b>돌려받은 상자</b>에 담아야 같이 접힌다.
        var secTarget = GradingDialog.AddCollapsible(root, "1. 대상", out _targetBadge,
                                                     "무엇을 가지고 정지면을 만들지",
                                                     first: true, open: false,
                                                     key: "정지:1.대상", group: G);
        _pickPlan = PickRow(secTarget, "계획 경계 선택", out _planWhat,
                            () => PickSession.Send(Doc, PickCommands.CmdPlan));
        _pickGround = PickRow(secTarget, "원지반 선택", out _groundWhat,
                              () => PickSession.Send(Doc, PickCommands.CmdGround));

        // ★[계획 §5.1] <b>이어서/새로시작</b> — 기존 정지 결과가 있을 때만 보인다.
        //   종전 명령은 계획선을 찍은 <b>뒤</b> 이것을 묻고, 그 답에 따라 원지반을 아예 안 물었다.
        //   창에서는 <b>미리 보여 주고</b>, 뜻을 가리는 것은 [생성]을 누르는 순간 다시 한다.
        _append = GradingDialog.AddRadioPair(secTarget, "기존 결과", "이어서", true, out _restart, "새로시작",
            "이어서 = 지금 정지면을 기준 삼아 새 구역을 더합니다. 새로시작 = 처음부터 다시 만듭니다.",
            out _modeRow);
        _append.Checked += (_, __) => PickSession.AppendMode = true;
        _restart.Checked += (_, __) => PickSession.AppendMode = false;

        // ── ① 절토성토 옵션 (절토/성토 + 산지 대소단) ─────────────────────
        //   ★[JACK 0910] <b>둘을 한 칸으로 합쳤다.</b> 대소단은 절성토 사면의 <b>변형</b>이지
        //   따로 정하는 물건이 아니다 — 켜면 그 값이 두 예시 그림에 바로 나타난다.
        var secSlope = GradingDialog.AddCollapsible(root, "2. 계획부지생성", out _slopeBadge,
                                                    "사면을 어떻게 계단으로 세울지 — 산지 대소단까지, 그리고 만들기",
                                                    open: false, key: "정지:2.절성토", group: G);
        // ══ ★★★[JACK 0910] <b>예시는 이 칸 안에, 두 장 다.</b> ═══════════════
        //
        //   <para>JACK: <i>"예시 이미지는 절토/성토안에 넣어줘. 그림을 작게하더라도
        //   절토 성토 선택버튼없이 두개 다뜨게해줘."</i></para>
        //
        //   <para>§77 ⑧은 <b>반대로</b> 정했었다 — 그림을 맨 위 고정 자리에 두고 토글로 한 장만.
        //   그 판단의 근거는 <b>그림이 값 칸을 화면 밖으로 밀어낸다</b>는 것이었는데,
        //   그 전제가 <b>이번 판에서 사라졌다</b>: 칸을 접을 수 있게 됐고(0910),
        //   옹벽 형태 두 줄이 [기타 설정]으로 빠졌다.</para>
        //
        //   <para>★<b>고치는 값 옆에 그 그림이 있는 것</b>이 원래 옳다 —
        //   절토를 고치면서 성토 그림을 보려고 라디오를 누르는 것은 군더더기였다.
        //   그림은 <c>Viewbox</c>가 <b>줄이기만</b> 하므로 좁은 칸에 들어가면 알아서 작아진다.</para>
        //   <para>★★<b>최종 배치(JACK 0910)</b>: <i>"예시이미지는 한번에 보여야하고"</i> —
        //   두 장을 <b>가로로 나란히</b> 놓고, 각 그림 <b>바로 밑에</b> 그 쪽 값 세 칸을 가로로 붙인다.
        //   세로로 쌓으면 한 화면에 한 장씩만 보인다(그림 하나가 300px).</para>
        //
        //   <para>★그래서 <b>창 폭을 넓혔다</b>(<see cref="GradingPalette.PanelW"/>).
        //   나란히 놓으면 한 장에 돌아가는 폭이 절반이고, 그림 글자는 420px 캔버스에
        //   10~12px로 그려져 <b>0.55배면 6px</b>이 되어 못 읽는다(실측). 폭이 곧 읽히는 조건이다.</para>
        var picGrid = new Grid { Margin = new Thickness(0, 0, 0, 2) };
        picGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        picGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var colCut = new StackPanel { Margin = new Thickness(0, 0, 5, 0) };
        var colFill = new StackPanel { Margin = new Thickness(5, 0, 0, 0) };
        Grid.SetColumn(colCut, 0);
        Grid.SetColumn(colFill, 1);
        picGrid.Children.Add(colCut);
        picGrid.Children.Add(colFill);
        secSlope.Children.Add(picGrid);

        colCut.Children.Add(Example("절토", out _canvasCut));
        _trioCut = Trio(colCut, GradingSettings.CutBenchHeight, GradingSettings.CutBenchWidth,
                        GradingForm.SlopeShown(GradingSettings.CutSlope), out _cutH, out _cutW, out _cutS);

        colFill.Children.Add(Example("성토", out _canvasFill));
        Trio(colFill, GradingSettings.FillBenchHeight, GradingSettings.FillBenchWidth,
             GradingForm.SlopeShown(GradingSettings.FillSlope), out _fillH, out _fillW, out _fillS);

        // ★사면형상은 <b>두 칸 아래 온 폭</b>에 둔다 — 한쪽 칸에 넣으면 절토에만 붙는 값처럼 보인다.
        //   ★[JACK 0910] 제목 글씨를 <b>절토·성토·산지 대소단과 같게</b> 맞춘다(중간 제목).
        secSlope.Children.Add(SubHead("사면형상"));
        _shapeMiter = GradingDialog.AddRadioPair(secSlope, "", "직각", GradingSettings.MiterConvex,
                                                 out _shapeRound, "라운드",
            "볼록한 모서리를 직각으로 세울지 둥글릴지 — 옹벽 장수가 크게 달라집니다.",
            out Panel shapeRow);
        // 제목을 위로 뺐으니 왼쪽 이름칸(150px)은 자리를 안 차지하게 한다.
        if (shapeRow.Children.Count > 0 && shapeRow.Children[0] is TextBlock shapeLab) shapeLab.Width = 0;

        // ── ② 산지 대소단 — ★[JACK 0910] 같은 칸 안으로 들어왔다 ──────────
        //   제목만 <b>작은 머리글</b>로 남긴다(칸을 또 나누면 접기가 두 겹이 된다).
        var terraceHead = SubHead("산지 대소단");
        terraceHead.ToolTip = "산지전용허가법 — 일정 높이마다 넓은 소단을 둡니다";
        secSlope.Children.Add(terraceHead);
        _terrace = new CheckBox
        {
            Content = "계단식 산지(대소단) 적용",
            IsChecked = GradingSettings.MountainTerrace,
            Margin = new Thickness(0, 0, 0, 8),
        };
        secSlope.Children.Add(_terrace);
        // ★값 두 칸도 <b>가로로</b> — 위 절성토 줄과 같은 모양이라야 눈이 안 헷갈린다.
        var tRow = new WrapPanel { Margin = new Thickness(2, 0, 0, 6) };
        _tInt = Cell(tRow, "대소단 간격(m)", GradingSettings.TerraceInterval);
        _tW = Cell(tRow, "대소단 폭(m)", GradingSettings.TerraceWidth);
        secSlope.Children.Add(tRow);

        // ══ ★★★[JACK 0910] <b>만들기는 값을 다 정한 뒤에</b> ═══════════════
        //
        //   <para>순서가 곧 일하는 차례다 — <b>대상을 고르고 → 절성토·대소단 값을 정하고 →
        //   그제야 만든다</b>. 종전엔 이 단추가 <c>1. 대상</c> 안에 있어서,
        //   찍자마자 누르게 되고 <b>값은 옛것인 채로</b> 만들어졌다.</para>
        //
        //   <para>★<b>칸 밖에 둔다</b> — 접히면 안 되는 단추다.
        //   위 칸들을 다 접어도 이것은 자리를 지킨다.</para>
        // ★★★[JACK 0910] 단추 이름과 자리 — <i>"계획부지생성하기 버튼은 2.계획부지생성 카테고리안에
        //   해당 카테고리의 마지막에"</i>. 칸 이름과 단추 이름이 같은 말을 하게 됐다.
        //   ★<b>누를 때 값을 먼저 반영한다</b>([값 저장]이 없어졌으므로) —
        //     값이 틀리면 <see cref="Apply"/>가 그 칸을 잡아 주고 <b>만들기로 넘어가지 않는다</b>.
        _build = new Button
        {
            Content = "계획부지생성하기",
            MinWidth = 150,
            Height = 34,
            Margin = new Thickness(0, 12, 0, 4),
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        try { if (DhBrand.Skin != null) _build.Style = (Style)DhBrand.Skin["DhPrimary"]; } catch { }
        _build.Click += (_, __) => { if (Apply()) PickSession.Send(Doc, PickCommands.CmdBuild); };
        secSlope.Children.Add(_build);

        // ── 5. 사면 수정 ──────────────────────────────────────────────────
        //   ★★★[계획 8단계 · JACK 지시 3 "도킹창안에 사면수정 기능을 넣어서 … UI에서 사면수정버튼은 없애고"]
        //     ★<b>리본에서 빼기 전에 갈 곳부터 만든다.</b> 순서를 뒤집으면 그 사이에
        //       <b>사면 수정을 아예 못 쓰는 판</b>이 나간다 — 터파기 창에는 이미 넣어 두고
        //       정지 창에는 안 넣은 채로 리본을 지울 뻔했다.
        //     ★단추는 <b>기존 명령을 그대로 부른다</b>(DHWALL/DHSLOPE) — 671줄짜리 편집기를
        //       베끼지 않는다(§20). 도킹창은 <b>부르는 자리</b>일 뿐이다.
        var secFix = GradingDialog.AddCollapsible(root, "3. 계획부지수정",
            "이미 만든 사면의 한 구간을 옹벽으로 세우거나 다시 사면으로 되돌립니다",
            open: false, key: "정지:4.사면수정", group: G);
        var wRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
        Button WBtn(string t, string cmd, string tip)
        {
            var b = new Button { Content = t, MinWidth = 104, Height = 30, Margin = new Thickness(0, 0, 6, 0), ToolTip = tip };
            // ★★★[JACK 0910 질문 <i>"사면수정에 문제가생기나?"</i>] <b>그렇다 — 그래서 여기도 먼저 반영한다.</b>
            //   <para><c>DHWALL</c>/<c>DHSLOPE</c>는 단높이·소단폭 <b>기본값을 <c>GradingSettings</c>에서 읽는다</b>
            //   (<c>ZoneEditCommon</c>: <i>"사용자가 방금 바꾼 값이 그대로 기본값이 된다"</i>).
            //   [값 저장]이 없어진 지금, 창에 친 숫자를 안 옮기고 이 명령을 보내면
            //   <b>화면에 적힌 값과 다른 기본값</b>으로 묻는다 — 창을 믿은 사람이 틀린 값을 그대로 Enter 친다.</para>
            b.Click += (_, __) => { if (Apply()) PickSession.Send(Doc, cmd); };
            wRow.Children.Add(b);
            return b;
        }
        _toWall = WBtn("옹벽 변환", "DHWALL",
            "고른 선부터 바깥 단을 옹벽으로 세웁니다. 단높이·소단길이를 그 자리에서 정합니다.");
        _toSlope = WBtn("사면 변환", "DHSLOPE",
            "옹벽선을 골라 그 단부터 다시 사면으로 되돌립니다. 사면구배도 그 자리에서 정합니다.");
        secFix.Children.Add(wRow);
        secFix.Children.Add(new TextBlock
        {
            Text = "※ 정지면을 먼저 만들어야 고칠 사면이 생깁니다.",
            FontSize = 11, Foreground = DhBrand.Sub, TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 4),
        });

        // 값이 바뀌면 예시를 바로 다시 그린다 — 도킹창의 값어치가 여기 있다.
        // ★★★[JACK 0910 <i>"별도의 값저장없이 도킹창에 넣은 값이 상시 저장된다고 생각하면 될것같은데"</i>]
        //   <b>치는 즉시 세션 값이 된다.</b> 그래야 손으로 <c>DHWALL</c>을 쳐서 부르는 길에서도
        //   <b>화면에 적힌 값</b>이 기본값이 된다(그 명령은 선을 클릭할 때마다 설정을 다시 읽는다).
        //
        //   ★<b>여기에 <see cref="Apply"/>를 걸면 안 된다.</b> 그것은 값이 틀리면
        //   <b>모달 경고창</b>을 띄우고 그 칸을 <c>SelectAll</c> 한다 — 단높이 <c>0.5</c>를 치려고
        //   <c>0</c>을 찍는 순간 팝업이 뜨고, 닫으면 다음 글자가 앞 글자를 지운다.
        //   그래서 <b>말없이 미는 길</b>을 따로 둔다(<see cref="PushValues"/>).
        //
        //   ★칸을 벗어날 때(LostFocus)는 <b>이 저장소가 이미 버린 길</b>이다 —
        //   지층 창에서 JACK 0831: <i>"XY 좌표를 쳐서 바꿀 때도 바로바로 안 바뀌고
        //   꼭 어딘가 다른 곳을 눌러 줘야 한다."</i> 되밟지 않는다.
        foreach (var b in new[] { _cutH, _cutW, _cutS, _fillH, _fillW, _fillS, _tInt, _tW })
            b.TextChanged += (_, __) => { PushValues(); Redraw(); };
        _shapeMiter.Checked += (_, __) => { PushValues(); Redraw(); };
        _shapeRound.Checked += (_, __) => { PushValues(); Redraw(); };
        // ★[UI검토 0909] <b>안 쓰는 칸은 잠근다</b> — 팝업·도면설정과 같은 규칙.
        void SyncTerrace()
        {
            bool on = _terrace.IsChecked == true;
            _tInt.IsEnabled = on; _tW.IsEnabled = on;
        }
        _terrace.Checked += (_, __) => { SyncTerrace(); PushValues(); Redraw(); };
        _terrace.Unchecked += (_, __) => { SyncTerrace(); PushValues(); Redraw(); };
        SyncTerrace();
        // ★[JACK 0910] 옹벽 형태는 <b>[기타 설정]</b>으로 갔다 — 이 창에 콤보가 없다.
        //   예시 그림은 그래도 그 값을 따른다(<see cref="Draw"/>가 <c>GradingSettings</c>를 직접 읽는다).
        //   기타 설정에서 저장하면 <c>GradingPalette.RedrawExample</c>이 여기 그림을 다시 그린다.

        // ══ 배치 ★★★[UI검토 0909 · P1] ═════════════════════════════════
        //
        //   <b>무엇이 문제였나.</b> 값·그림·저장을 한 <c>ScrollViewer</c>에 세로로 쌓으니
        //   창이 <b>1,400px</b>이 됐다. 그런데 우측 도킹 팔레트가 쓸 수 있는 세로는
        //   FHD에서 <b>850~950px</b>뿐이다. 그림 두 장이 650px를 먹고 맨 아래에 있어서,
        //   <b>값 칸과 그림이 같은 화면에 절대 같이 안 나왔다</b>.
        //   머리띠에 <i>"값을 고치면 예시가 바로 바뀝니다"</i>라고 적어 놓고
        //   그 그림은 1,000px 아래에서 혼자 바뀌고 있었던 것이다 —
        //   <b>도킹창을 만든 이유가 화면 배치 때문에 무효</b>였다.
        //
        //   → ①그림을 <b>맨 위 고정 자리</b>로 ②절토/성토는 <b>토글로 한 장만</b>(325px 절약)
        //     ③<b>저장은 바닥에 못 박는다</b>(지층창이 이미 그렇게 한다).
        //     값 칸만 가운데에서 스크롤한다.
        //
        //   ★★[JACK 0910] <b>①과 ②는 뒤집혔다.</b> 그림은 <c>2. 절토 / 성토</c> 칸 안으로
        //     들어가 <b>값 옆에 두 장</b>이 뜬다. 위 판단의 근거였던 "그림이 값을 밀어낸다"는
        //     칸 접기가 생기면서 사라졌다. ③(저장은 바닥에)은 그대로다.
        var deck = new DockPanel { LastChildFill = true };

        var top = new StackPanel { Margin = new Thickness(10, 10, 10, 0) };
        top.Children.Add(head);
        DockPanel.SetDock(top, Dock.Top);
        deck.Children.Add(top);

        // ★★[JACK 0910 <i>"도킹창 맨아래 값저장은 필요없잖아 삭제시켜"</i>] <b>바닥 띠를 걷었다.</b>
        //   값은 <b>[계획부지생성하기]를 누를 때</b> 반영된다(<see cref="Apply"/>) —
        //   따로 "저장"을 기억할 필요가 없다.

        // ★[JACK 0910] 값 칸 <b>뒤에</b> 회사 로고를 크게 옅게 깐다.
        //   머리띠·바닥 단추 자리가 아니라 <b>가운데(스크롤되는 칸)</b>에 둔다 —
        //   머리띠엔 이미 진짜 로고가 있어서 겹치면 둘 다 지저분해진다.
        //   ★칸을 접을수록 빈자리가 넓어져 로고가 더 잘 보인다.
        deck.Children.Add(DhBrand.Watermark(new ScrollViewer
        {
            Content = root,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Background = Brushes.Transparent,   // ★바탕이 칠해지면 로고가 통째로 가려진다
        }));
        Content = deck;


        // ★[검토 0909] <c>Changed</c>는 <b>정적 이벤트</b>다 — 창이 사라질 때 반드시 해지한다.
        //   안 하면 죽은 UI로 호출이 가고, 창이 영원히 살아 있게 된다.
        // ★★[검토 0909 · 높음] <b>다시 붙는 자리도 있어야 한다.</b>
        //   <c>PaletteSet</c>은 도킹·부유·자동숨김을 오갈 때 시각 트리에서 뺐다 붙인다.
        //   해지만 있고 <c>Loaded</c>가 없으면, 한 번 접었다 편 뒤로 <b>영영 안 갱신</b>된다 —
        //   도면엔 빨간 띠가 뜨는데 창은 "○ 미선택"에 [지표면 생성]이 회색인 채로 남는다.
        //   ★두 번 붙지 않게 <b>떼고 붙인다</b>(이벤트는 중복 구독이 된다).
        void Wire() { try { PickSession.Changed -= RefreshPicks; PickSession.Changed += RefreshPicks; } catch { } }
        Loaded += (_, __) => { Wire(); RefreshPicks(); };
        // ★★[검토 0910 · 보통4] <b>폭은 짐작하지 말고 잰다.</b>
        //   <c>PaletteSet.Size</c>는 <b>장치 픽셀</b>인데 WPF 배치는 <b>DIP</b>다 —
        //   글자 배율 125%인 화면이면 780px가 624dip가 되어, 780으로 넓힌 이유(그림 글자가 읽히는 것)가
        //   <b>그 화면에서만 조용히 무너진다</b>. 한 줄 재어 로그에 남기면 다음에 헤매지 않는다.
        SizeChanged += (_, __) => LogWidthOnce();
        Unloaded += (_, __) => { try { PickSession.Changed -= RefreshPicks; } catch { } };
        Wire();
        RefreshPicks();
        _miterAtSync = GradingSettings.MiterConvex;
        PushValues();   // ★칸 색·단추 켜짐을 처음부터 맞춰 둔다
        Redraw();
    }

    // ── 도면 따라가기 ─────────────────────────────────────────────────────
    /// <summary>★★★[검토 0908이 짚은 것] <b>도면이 바뀌면 그 도면 값으로 맞춘다.</b>
    ///
    /// <para>종전에는 <see cref="GradingSettings.SyncToDocument"/>를 <b>명령이 시작할 때</b>만 불렀다.
    /// 창이 UI가 되면 <b>아무도 안 부른다</b> — 그러면 창은 <b>앞 도면 값</b>을 보여주고,
    /// [값 저장]이 그 값을 <b>새 도면에 밀어 넣는다</b>.</para></summary>
    internal void SyncTo(AcDoc doc)
    {
        if (doc == null) return;

        // ★★★[UI검토 0909 · P1] <b>사람이 친 것을 말없이 지우지 않는다.</b>
        //
        //   값을 고치고 <b>저장을 안 한 채</b> 다른 도면 탭을 누르면, 여기가 화면을
        //   그 도면 값으로 <b>덮어쓴다</b>. 종전엔 아무 말이 없었다 —
        //   "안 하고 지나가는 것"이 아니라 <b>한 일을 지우는 것</b>이라 반드시 말해야 한다.
        //   ★막지는 않는다. 도면이 바뀌면 그 도면 값을 보여 주는 것이 맞기 때문이다.
        bool dirty = Dirty();

        try { GradingSettings.SyncToDocument(doc); } catch { }
        _loading = true;
        try
        {
            _cutH.Text = N(GradingSettings.CutBenchHeight);
            _cutW.Text = N(GradingSettings.CutBenchWidth);
            _cutS.Text = N(GradingForm.SlopeShown(GradingSettings.CutSlope));
            _fillH.Text = N(GradingSettings.FillBenchHeight);
            _fillW.Text = N(GradingSettings.FillBenchWidth);
            _fillS.Text = N(GradingForm.SlopeShown(GradingSettings.FillSlope));
            _tInt.Text = N(GradingSettings.TerraceInterval);
            _tW.Text = N(GradingSettings.TerraceWidth);
            _shapeMiter.IsChecked = GradingSettings.MiterConvex;
            _shapeRound.IsChecked = !GradingSettings.MiterConvex;
            _terrace.IsChecked = GradingSettings.MountainTerrace;
        }
        catch { }
        finally { _loading = false; }
        // ★★[검토 0909 · 보통] <b>이어서/새로시작도 도면마다 되돌린다.</b>
        //   라디오 → 정적값 방향만 있고 반대가 없어, 도면 A에서 [새로시작]을 고른 뒤
        //   도면 B로 넘어가면 <b>A의 답</b>을 들고 있었다 —
        //   계획 §4가 경고한 "앞 도면 값을 새 도면에 밀어 넣는다" 그대로다.
        _append.IsChecked = true;
        _restart.IsChecked = false;
        PickSession.AppendMode = true;

        // ★여기가 팝업의 "창을 연 순간"에 해당한다(위 <see cref="_miterAtSync"/> 참고).
        _miterAtSync = GradingSettings.MiterConvex;
        PushValues();   // ★도면 값으로 갈아 끼운 뒤 칸 색·단추도 다시 맞춘다
        Redraw();
        RefreshPicks();
        // ★[검토 0910] [값 저장]이 없어졌으므로 문구도 고친다 — 없는 단추를 가리키면 안 된다.
        if (dirty) Say("이 도면 값으로 맞췄습니다 — 치고 있던 숫자는 이 도면 값으로 바뀌었습니다.");
    }

    /// <summary>화면에 <b>저장 안 한 고침</b>이 있는가 — 덮어쓰기 전에 묻는 자리.
    /// <para>여덟 칸과 세 스위치를 저장된 값과 견준다. 글자로 견주므로
    /// <c>1.5</c>와 <c>1.50</c>이 다르게 잡힐 수 있는데, <b>안전한 쪽</b>이다
    /// (안 지웠는데 지웠다고 말하는 것이, 지워 놓고 말 안 하는 것보다 낫다).</para></summary>
    private bool Dirty()
    {
        try
        {
            if (_cutH.Text != N(GradingSettings.CutBenchHeight)) return true;
            if (_cutW.Text != N(GradingSettings.CutBenchWidth)) return true;
            if (_cutS.Text != N(GradingForm.SlopeShown(GradingSettings.CutSlope))) return true;
            if (_fillH.Text != N(GradingSettings.FillBenchHeight)) return true;
            if (_fillW.Text != N(GradingSettings.FillBenchWidth)) return true;
            if (_fillS.Text != N(GradingForm.SlopeShown(GradingSettings.FillSlope))) return true;
            if (_tInt.Text != N(GradingSettings.TerraceInterval)) return true;
            if (_tW.Text != N(GradingSettings.TerraceWidth)) return true;
            if ((_shapeMiter.IsChecked == true) != GradingSettings.MiterConvex) return true;
            if ((_terrace.IsChecked == true) != GradingSettings.MountainTerrace) return true;
            // 옹벽 형태는 이 창에 없다 — [기타 설정]이 가진다.
        }
        catch { }
        return false;
    }

    /// <summary>★★★[JACK 0910] <b>치는 즉시 세션 값으로 민다 — 말없이.</b>
    ///
    /// <para><see cref="Apply"/>와 <b>일부러 다른 함수</b>다. 저쪽은 <b>관문</b>(틀리면 팝업으로 막는다),
    /// 이쪽은 <b>동기화</b>(맞을 때만 조용히 옮기고, 틀리면 표시만 한다).
    /// 검사식이 두 벌이 되는 것을 막으려고 <b>범위를 <c>Apply</c>보다 좁거나 같게</b> 잡았다 —
    /// 그러면 여기를 통과한 값은 <c>Apply</c>도 반드시 통과하므로 <b>팝업이 뜰 길이 구조적으로 없다</b>.</para>
    ///
    /// <para>틀린 칸은 <b>글자와 테두리를 경고색</b>으로 물들이고, 칸을 접었을 때는 제목 옆에
    /// <c>⚠값 확인</c>이 뜨며, <b>동작 단추 셋을 잠근다</b>. 안내문 줄은 JACK이 걷어냈으므로
    /// 남은 길이 이 셋이다.</para></summary>
    private void PushValues()
    {
        if (_loading) return;
        try
        {
            // ★한 줄씩 <b>다</b> 재고 나서 판단한다 — 짧게 끊으면 뒤 칸이 안 물든다.
            bool okCH = Gate(_cutH, GradingForm.BenchMin, GradingForm.BenchMax, out double cbh);
            bool okCW = Gate(_cutW, 0, 60, out double cbw);
            bool okCS = Gate(_cutS, 0, GradingForm.SlopeMax, out double cs);
            bool okFH = Gate(_fillH, GradingForm.BenchMin, GradingForm.BenchMax, out double fbh);
            bool okFW = Gate(_fillW, 0, 60, out double fbw);
            bool okFS = Gate(_fillS, 0, GradingForm.SlopeMax, out double fs);
            bool okTI = Gate(_tInt, 1, 200, out double ti);
            bool okTW = Gate(_tW, 0, 120, out double tw);
            _valOk = okCH && okCW && okCS && okFH && okFW && okFS && okTI && okTW;

            if (_valOk)
            {
                // ★구배 0은 <b>수직으로 세우겠다는 뜻</b>이다 — <c>Apply</c>와 같은 규칙으로 하한까지 올린다.
                //   칸에는 그대로 0으로 보인다(JACK 0825).
                double floor = GradingSettings.MinSlope;
                if (cs < floor) cs = floor;
                if (fs < floor) fs = floor;

                GradingSettings.CutBenchHeight = cbh;
                GradingSettings.CutBenchWidth = cbw;
                GradingSettings.CutSlope = cs;
                GradingSettings.FillBenchHeight = fbh;
                GradingSettings.FillBenchWidth = fbw;
                GradingSettings.FillSlope = fs;
                GradingSettings.TerraceInterval = ti;
                GradingSettings.TerraceWidth = tw;
                GradingSettings.MountainTerrace = _terrace.IsChecked == true;
                GradingSettings.MiterConvex = _shapeMiter.IsChecked == true;
                // ★레지스트리(다음 세션 기본값)는 <b>여기서 안 쓴다</b> — 그것은 사람이
                //   <b>만들기를 눌렀을 때</b>의 뜻이다(<see cref="Apply"/>가 한다).
            }

            if (_slopeBadge != null) _slopeBadge.Text = _valOk ? "" : "· ⚠값 확인";
            SyncEnabled();
        }
        catch { }
    }

    /// <summary>칸 하나를 재고, 틀리면 <b>그 칸을 물들인다</b>.</summary>
    private static bool Gate(TextBox box, double min, double max, out double v)
    {
        v = 0;
        if (box == null) return true;
        string t = (box.Text ?? "").Trim().Replace(',', '.');
        bool ok = double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out v)
               && v >= min - 1e-9 && v <= max + 1e-9;
        box.Foreground = ok ? DhBrand.Ink : DhBrand.Warn;
        box.BorderBrush = ok ? DhBrand.Line : DhBrand.Warn;
        box.BorderThickness = new Thickness(ok ? 1 : 2);
        box.ToolTip = ok ? null : $"{min:0.###} ~ {max:0.###} 사이 숫자여야 합니다.";
        return ok;
    }

    /// <summary>동작 단추 셋의 켜짐을 <b>한 자리</b>에서 정한다 — 조건이 셋이라 흩어 두면 서로 덮는다.</summary>
    private void SyncEnabled()
    {
        try
        {
            if (_build != null) _build.IsEnabled = _picksOk && _valOk;
            if (_toWall != null) _toWall.IsEnabled = _fixOk && _valOk;
            if (_toSlope != null) _toSlope.IsEnabled = _fixOk && _valOk;
        }
        catch { }
    }

    /// <summary>★★★[JACK 0910 <i>"도킹창 맨아래 값저장은 필요없잖아 삭제시켜"</i>]
    /// <b>단추는 없앴지만 이 일은 없앨 수 없다.</b>
    ///
    /// <para>이 함수가 <b>창에 친 숫자를 실제 설정으로 옮기는 유일한 자리</b>다 —
    /// 그림 다시 그리기(<see cref="Redraw"/>)는 화면만 건드린다.
    /// 그냥 지웠다면 <b>친 값이 결과에 하나도 안 들어가는</b> 판이 나갔을 것이다.</para>
    ///
    /// <para>→ <b>[계획부지생성하기]가 누를 때 먼저 이것을 부른다.</b> 값이 틀렸으면
    /// 그 칸을 잡아 주고 <c>false</c>를 돌려주므로 <b>만들기로 넘어가지 않는다</b>.
    /// 사람이 따로 "저장"을 기억할 필요가 없어졌다 — 만들면 그 값으로 만들어진다.</para></summary>
    private bool Apply()
    {
        if (!GradingForm.Apply(null, new GradingForm.Controls
        {
            CutBenchHeight = _cutH, CutBenchWidth = _cutW, CutSlope = _cutS,
            FillBenchHeight = _fillH, FillBenchWidth = _fillW, FillSlope = _fillS,
            ShapeMiter = _shapeMiter,
            MountainTerrace = _terrace,
            TerraceInterval = _tInt, TerraceWidth = _tW,
            // 기타 설정으로 간 것들은 이 창에 없다 — null이면 안 건드린다.
            CutWallStyle = null, FillWallStyle = null,
            ShowOnlyResult = null, CoordSys = null,
        }, _miterAtSync))
            return false;

        _miterAtSync = GradingSettings.MiterConvex;
        Redraw();
        return true;
    }

    /// <summary>★[검토 0909 · 낮음] <b>안내문과 결과를 같은 칸에 쓰지 않는다.</b>
    /// <para>종전엔 첫 [값 저장]에 <i>"찍기·생성은 리본으로"</i>라는 안내가 영영 사라졌다 —
    /// 5a의 설계 취지(없는 단추 대신 <b>말한다</b>)가 첫 클릭에 무너지는 것이다.</para></summary>
    /// <summary>지금 도면 — 창은 늘 떠 있으므로 <b>부를 때마다</b> 묻는다(붙잡아 두지 않는다).</summary>
    private static AcDoc Doc => AcadApp.DocumentManager.MdiActiveDocument;

    /// <summary>★<b>찍기 상태를 화면에 옮긴다</b> — 고른 것 이름, 단추 켜짐, 이어서 칸 보임.
    /// <para><see cref="PickSession.Changed"/>가 이것을 부른다. 찍는 중에는 단추를 <b>회색으로</b> 내려
    /// 겹쳐 누르지 못하게 한다 — 계획 §4가 적어 둔 자리다.</para></summary>
    internal void RefreshPicks()
    {
        try
        {
            var doc = Doc;
            bool busy = PickSession.Busy;

            var pl = doc == null ? null : PickSession.Peek(doc, PickSession.KeyPlan);
            var gr = doc == null ? null : PickSession.Peek(doc, PickSession.KeyGround);
            bool hasPlan = pl != null, hasGround = gr != null;
            _planWhat.Text = hasPlan ? "● " + pl.What : "○ 미선택";
            _planWhat.Foreground = hasPlan ? DhBrand.Brand : DhBrand.Sub;

            // 기존 결과가 있을 때만 이어서/새로시작을 보여 준다 — 없으면 고를 것이 없다.
            bool hasPrev = doc != null && GradeStart.HasPrevious(doc.Database, out int nR) && nR > 0;
            // ★[JACK 0910 <i>"이어서 하기옵션이 빠졌는데 1. 대상에 넣어 디폴트는 이어서로"</i>]
            //   <b>늘 보인다.</b> 종전엔 기존 정지 결과가 있을 때만 보여 줬는데,
            //   그러면 <b>있는지 없는지를 창이 대신 판단</b>해 버려 "왜 안 보이지"가 된다.
            //   기존 결과가 없으면 어느 쪽을 골라도 결과가 같으므로 보여 줘도 해롭지 않다.
            if (_modeRow != null) _modeRow.Visibility = Visibility.Visible;

            // ★이어서·다시는 원지반이 <b>자동</b>으로 정해진다 — 그때는 안 골라도 만들 수 있다.
            bool needGround = !(hasPrev && _append.IsChecked == true);
            _pickPlan.IsEnabled = !busy;
            // ★★[검토 0909 · 높음] <b>안 쓸 거면 잠근다.</b> 종전엔 흐리게만 하고 눌리게 뒀는데,
            //   눌러서 고른 것이 <b>결과에 안 쓰였다</b> — 알림은 명령창 한 줄뿐이었다.
            //   "고를 수 있는데 무시한다"는 것이 사용자에게 가장 나쁜 상태다.
            _pickGround.IsEnabled = !busy && needGround;
            _groundWhat.Text = needGround
                ? (hasGround ? "● " + gr.What : "○ 미선택")
                : "● 자동 — 지금 정지면_DH";
            _groundWhat.Foreground = needGround && !hasGround ? DhBrand.Sub : DhBrand.Brand;

            // ★★[JACK 0910 · 검토 M-1] <b>접어 놓으면 무엇을 골랐는지 안 보인다.</b>
            //   그 칸을 접은 동안만 제목 옆에 상태를 적는다 — 접힌 채로 [지표면 생성]을 누르는 일이
            //   흔할 텐데, <b>무엇으로 만드는지 모르고 누르는 것</b>이 가장 나쁘다.
            //   ★<c>이어서</c>인지도 함께 적는다 — 그것이 원지반을 자동으로 바꿔 버리기 때문이다.
            if (_targetBadge != null)
                _targetBadge.Text = "· 경계 " + (hasPlan ? "●" : "○")
                                  + " 지반 " + (needGround ? (hasGround ? "●" : "○") : "자동")
                                  + (hasPrev ? (_append.IsChecked == true ? " · 이어서" : " · 새로시작") : "");
            _picksOk = !busy && hasPlan && (hasGround || !needGround);
            // ★[8단계] 사면 수정은 <b>이미 만든 정지면</b>이 있어야 한다 —
            //   "이어서/새로시작"을 보여 주는 그 조건과 같다(기존 결과가 있는가).
            //   ★<b>화면 상태가 아니라 사실을 읽는다</b> — 바로 위에서 구한 <c>hasPrev</c>다.
            //     <c>_modeRow.Visibility</c>를 읽으면 <b>줄 순서에 기대는</b> 코드가 되어,
            //     나중에 배치를 바꾸는 순간 단추가 조용히 잠긴다.
            bool madeAny = hasPrev;
            _fixOk = !busy && madeAny;
            SyncEnabled();
        }
        catch (System.Exception ex)
        {
            // ★[검토 0909] <b>조용히 안 바뀌면 아무도 모른다.</b> 이 저장소 규칙 —
            //   자주 고치는 자리는 로그 한 줄(JACK: "단계마다 로그").
            try { DiagLog.Append("\n■ 정지창 상태 갱신 실패 — " + ex.Message + "\n"); } catch { }
        }
    }

    /// <summary>★[JACK 0910] 화면 칸이 없어졌으므로 <b>명령창에만</b> 적는다.
    /// <para>강조 표시(<c>&lt;b&gt;</c>)는 이 저장소 관례대로 <b>벗겨서</b> 보낸다 —
    /// 명령창은 서식을 안 그려서 태그가 글자 그대로 보인다.</para></summary>
    private void Say(string s)
    {
        try
        {
            AcadApp.DocumentManager.MdiActiveDocument?.Editor?
                .WriteMessage("\n[계획부지 정지] " + s.Replace("<b>", "").Replace("</b>", ""));
        }
        catch { }
    }

    /// <summary>★[검토 0910] 이 창이 <b>실제로</b> 얼마를 받았는지 한 번만 잰다 — 배율 환경 확인용.</summary>
    private bool _widthLogged;

    private void LogWidthOnce()
    {
        if (_widthLogged || ActualWidth <= 0) return;
        try
        {
            double pic = _canvasCut?.Parent is FrameworkElement fe ? fe.ActualWidth : -1;
            if (pic <= 0) return;                       // 아직 배치 전 — 다음 SizeChanged에 다시 잰다
            _widthLogged = true;
            double k = pic / SlopeDiagram.W0;
            DiagLog.Append($"\n[계획부지 정지] 창 실측 — 패널 {ActualWidth:F0}dip"
                         + $" · 예시 그림 자리 {pic:F0}dip · 배율 {k:0.00}"
                         + $" · 치수 글자 {11 * k:0.0}px"
                         + (11 * k < 8 ? "  ⚠<b>작아서 못 읽는다 — 창을 넓히면 커진다</b>" : "")
                         // ★[JACK 0910] <b>창 폭을 정한 조건</b>을 그 자리에서 되읽는다 —
                         //   값 세 칸이 한 줄이면 줄 높이가 한 칸(약 30dip)이고, 접히면 두 칸이 된다.
                         + (_trioCut != null && _trioCut.ActualHeight > 0
                            ? $" · 값 세 칸 {_trioCut.ActualWidth:F0}dip/{_trioCut.ActualHeight:F0}높이"
                              + (_trioCut.ActualHeight > 44 ? "  ⚠<b>한 줄에 안 들어가 접혔다</b>" : " (한 줄)")
                            : "")
                         + "\n");
        }
        catch { }
    }

    // ── 그림 ──────────────────────────────────────────────────────────────
    /// <summary>★[JACK 0910] 밖에서 그림만 다시 그리게 하는 문 — [기타 설정]이 옹벽 형태를 바꾸면 부른다.</summary>
    internal void RedrawExample() => Redraw();

    private void Redraw()
    {
        if (_loading) return;
        // ★[JACK 0910] 둘 다 그린다 — 한쪽 값을 고치면 그쪽 그림만 바뀌는 것이 눈에 보인다.
        Draw(_canvasCut, _cutH, _cutW, _cutS, GradingSettings.CutWallStyle, cut: true);
        Draw(_canvasFill, _fillH, _fillW, _fillS, GradingSettings.FillWallStyle, cut: false);
    }

    /// <summary>★[JACK 0910] 옹벽 형태를 <b>콤보가 아니라 저장된 값</b>에서 받는다 —
    /// 그 칸이 이 창을 떠나 [기타 설정]으로 갔기 때문이다.</summary>
    private void Draw(Canvas c, TextBox h, TextBox w, TextBox s, WallStyle wall, bool cut)
    {
        double nRaw = P(s, 1.5, 0, GradingForm.SlopeMax, allowZero: true);   // ★검사와 <b>같은 상한</b>
        SlopeDiagram.Draw(c, new SlopeDiagram.Spec(
            Cut: cut,
            BenchH: P(h, 5, 0.2, 60),
            BenchW: P(w, 1, 0, 60, allowZero: true),
            Slope: System.Math.Max(nRaw, GradingSettings.MinSlope),
            SlopeRaw: nRaw,
            Terrace: _terrace.IsChecked == true,
            TerraceInterval: P(_tInt, 15, 1, 200),
            TerraceWidth: P(_tW, 15, 0, 120, allowZero: true),
            Style: wall,
            WallGate: GradingSettings.WallGateSlope));

        // ★[JACK 0910] 구배 0일 때 뜨던 <i>"0.01(수직 옹벽)로 처리됩니다"</i> 안내를 없앴다 —
        //   0을 치는 것은 <b>수직으로 세우겠다는 뜻</b>이고 그림이 이미 옹벽으로 바뀌어 보여 준다.
    }

    private static double P(TextBox box, double dflt, double min, double max, bool allowZero = false)
    {
        string t = (box?.Text ?? "").Trim().Replace(',', '.');
        if (!double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out double v)) return dflt;
        if (v < 0 || (!allowZero && v <= 0)) return dflt;
        return System.Math.Clamp(v, min, max);
    }

    // ── 자잘한 것 ─────────────────────────────────────────────────────────
    private static string N(double v) => v.ToString(CultureInfo.InvariantCulture);

    
    /// <summary>찍기 한 줄 — [단추] 와 <b>고른 것 이름</b>.</summary>
    private static Button PickRow(Panel parent, string label, out TextBlock what, System.Action onClick)
    {
        var row = new DockPanel { Margin = new Thickness(0, 0, 0, 8), LastChildFill = true };
        var b = new Button { Content = label, MinWidth = 130, Height = 30 };
        b.Click += (_, __) => onClick();
        DockPanel.SetDock(b, Dock.Left);
        row.Children.Add(b);
        what = new TextBlock
        {
            Text = "○ 미선택",
            Margin = new Thickness(10, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = DhBrand.Sub,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        row.Children.Add(what);
        parent.Children.Add(row);
        return b;
    }

    private static TextBox Row(Panel parent, string label, double value)
        => GradingDialog.AddRow(parent, label, value, "");

    /// <summary>★[JACK 0910] 예시 그림 <b>바로 밑에 가로로</b> 붙는 값 세 칸 —
    /// <c>단높이 [ ] · 소단폭 [ ] · 구배 1:[ ]</c>.
    ///
    /// <para>JACK: <i>"예시사진 밑에 가로 배치로 절토 | 단높이 [입력칸] 소단폭(m) [입력칸]
    /// 구배 1:[입력칸]"</i>. 세로 여섯 줄이 <b>가로 두 줄</b>이 되고, 무엇보다
    /// <b>그림이 온 폭을 그대로 쓴다</b> — 좁히면 치수 글자가 6px이 되어 못 읽는다(실측).</para>
    ///
    /// <para>★<c>WrapPanel</c>이라 창을 좁히면 <b>칸이 아래로 접혀 내려간다</b> —
    /// 잘려서 안 보이는 것보다 낫다.</para></summary>
    private static WrapPanel Trio(Panel parent, double h, double w, double s,
                                  out TextBox tbH, out TextBox tbW, out TextBox tbS)
    {
        var row = new WrapPanel { Margin = new Thickness(2, 0, 0, 10) };
        tbH = Cell(row, "단높이(m)", h);
        tbW = Cell(row, "소단폭(m)", w);
        tbS = Cell(row, "구배 1:", s);
        parent.Children.Add(row);
        return row;   // ★한 줄에 들어갔는지 <b>재려고</b> 돌려준다(창 폭의 근거가 이 줄이다)
    }

    private static TextBox Cell(Panel parent, string label, double value)
    {
        var cell = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 6, 4) };
        cell.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 11,
            Foreground = DhBrand.Sub,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 4, 0),
        });
        var box = new TextBox
        {
            Text = value.ToString(CultureInfo.InvariantCulture),
            // ★[검토 0910 · 보통4] 56 → 50 → <b>48</b>. 세 칸이 <b>한 줄에</b> 들어가는 것이
            //   창 폭을 정하는 조건이 됐으므로(JACK 0910), 칸을 조금 줄여 여유를 만든다.
            Width = 48,
            Height = 26,
            VerticalContentAlignment = VerticalAlignment.Center,
            HorizontalContentAlignment = HorizontalAlignment.Center,
        };
        cell.Children.Add(box);
        parent.Children.Add(cell);
        return box;
    }


    /// <summary>예시 한 장 — 머리글(절토/성토) + 그림 + 그 값에 붙는 안내.
    /// <para>★[JACK 0910] 이제 <b>두 장이 나란히</b> 뜨므로 <b>어느 쪽인지 이름을 붙인다</b> —
    /// 고르는 라디오가 없어져 이름이 없으면 어느 그림인지 알 길이 없다.</para></summary>
    /// <summary>★[JACK 0910] <b>중간 제목 한 벌</b> — 절토·성토·사면형상·산지 대소단이 <b>같은 글씨</b>여야 한다.
    /// <para>JACK: <i>"중간 제목정도 되니깐 통일하고 두껍게해 … 중간제목들 사이의 줄간격도 통일"</i>.
    /// 크기·굵기·색·위아래 여백을 여기 한 곳에서 정한다 — 흩어 두면 또 제각각이 된다.</para></summary>
    private static TextBlock SubHead(string text, bool first = false) => new()
    {
        Text = text,
        FontSize = 12,
        FontWeight = FontWeights.Bold,
        Foreground = DhBrand.Ink,
        Margin = new Thickness(0, first ? 0 : 12, 0, 6),   // ★위 12 · 아래 6 — 이 값이 곧 "줄간격 통일"이다
    };

    private static StackPanel Example(string caption, out Canvas canvas)
    {
        var col = new StackPanel { Margin = new Thickness(0, 0, 0, 4) };
        col.Children.Add(SubHead(caption, first: true));
        var box = new Border
        {
            BorderBrush = DhBrand.Line, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6), Background = DhBrand.Card, Padding = new Thickness(4),
            Child = SlopeDiagram.Wrap(out canvas),
        };
        col.Children.Add(box);
        // ★[JACK 0910] <b>"구배 0 입력은 0.01(수직 옹벽)로 처리됩니다" 경고를 없앴다.</b>
        //   0을 치는 사람은 <b>수직으로 세우겠다는 뜻</b>이고, 그림이 이미 옹벽으로 바뀌어 보여 준다 —
        //   같은 말을 빨간 글씨로 또 하면 <b>잘못한 것처럼</b> 보인다.
        return col;
    }
}
