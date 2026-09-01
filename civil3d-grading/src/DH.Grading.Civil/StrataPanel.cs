using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using DH.Grading.Core;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace DH.Grading.Civil;

/// <summary>★★★[JACK 0828] 지층 구성 도킹바의 <b>속</b> — 층 표 · 보링공 표 · 단추.
///
/// <para><b>두 표를 위아래로 둔다.</b> 층을 먼저 정하고 그다음 공을 찍는 순서라,
/// 화면도 그 순서대로 놓는다 — 아래 표의 두께 칸이 <b>위 표의 층 수만큼</b> 생기기 때문이다.</para>
///
/// <para><b>표 칸이 실행 중에 늘고 준다.</b> WPF 표는 고정 속성만 자동으로 묶으므로
/// 두께 칸은 <see cref="RebuildThicknessColumns"/>가 <b>코드로 만든다</b>.</para></summary>
public sealed class StrataPanel : UserControl
{
    internal readonly ObservableCollection<LayerRow> Layers = new();
    internal readonly ObservableCollection<BoreRow> Bores = new();

    private readonly DataGrid _gLayer = new();
    private readonly DataGrid _gBore = new();
    private readonly TextBlock _status = new();
    /// <summary>표식 크기 바의 양 끝 — ★[JACK 0831] <b>×1이 정확히 1/3 자리</b>에 오도록 잡았다.
    /// <para><c>(1 − 0.2) / (2.6 − 0.2) = 1/3</c>. 더 크게 키울 일이 있으면 이 두 값을 함께 옮긴다
    /// (한쪽만 바꾸면 ×1이 1/3에서 벗어난다 — 그 관계가 이 값들의 이유다).</para></summary>
    private const double SizeMin = 0.2, SizeMax = 2.6;

    /// <summary>★★★[JACK 0901] <b>지층 높이를 어떻게 치는가 — 도면 전체에서 하나다.</b>
    /// <para>층마다 고르게 했더니 <i>"헷갈린다"</i>는 말이 많았다. 시추주상도를 읽는 방식도
    /// <b>깊이를 읽거나 표고를 읽거나</b> 둘 중 하나다.</para></summary>
    /// <para>★[JACK 0901] 기본은 <b>층별 GL값</b>이다 — 실무에서 암선 표고를 그대로 옮겨 적는 일이 흔하다.</para>
    internal StrataHeightMode Mode { get; private set; } = StrataHeightMode.Elevation;

    private readonly RadioButton _rbTh = new();
    private readonly RadioButton _rbGl = new();
    private readonly TextBlock _modeHint = new();

    /// <summary>모드별 층 목록 — <b>여기 하나</b>에서 정한다.
    /// <para>★<b>보통암을 빠뜨리지 않는다</b> — 수량 분류는 다섯이고, 하나가 없으면
    /// 그 암이 있는 현장을 아예 못 만든다.</para>
    /// <para>GL 모드에는 토사가 없다 — <b>저절로</b> 생긴다(지표~첫 암선). 그래서 칠 필요가 없다.</para></summary>
    /// <summary>모드 안내 문구 — <b>여기 하나</b>에서 정한다(JACK 0901 문구 확정).</summary>
    private static string HintFor(StrataHeightMode m) =>
        m == StrataHeightMode.Elevation
            ? "암층별 상단 표고(GL.m) 입력 — 암층별 GL로 지층 생성."
            : "각 층의 두께(m) 입력 — 원지반에서 지층별 두께만큼 생성.";

    private static (string Name, RockClass Rock)[] SeedFor(StrataHeightMode m) =>
        m == StrataHeightMode.Elevation
            ? new[]
            {
                ("풍화암", RockClass.Weathered), ("연암", RockClass.Soft),
                ("보통암", RockClass.Medium),    ("경암", RockClass.Hard),
            }
            : new[]
            {
                ("표토", RockClass.Soil),        ("풍화토", RockClass.Soil),
                ("풍화암", RockClass.Weathered), ("연암", RockClass.Soft),
                ("보통암", RockClass.Medium),    ("경암", RockClass.Hard),
            };

    private readonly Slider _sizeBar = new();
    private readonly TextBlock _sizeText = new();

    /// <summary>지금 열려 있는 도킹바 — 명령이 여기로 값을 넣는다.</summary>
    internal static StrataPanel Current { get; private set; }

    public StrataPanel()
    {
        Current = this;
        Build();
        // ★★★[JACK 0831 검증] <b>배선을 씨앗보다 먼저 건다.</b>
        //   종전엔 <c>SeedDefaultLayers()</c> 뒤에 걸어서, 처음 다섯 층은
        //   <c>HookLayers</c>가 <b>한 번도 안 돌아</b> 이름을 고쳐도 열 머리가 안 따라왔다.
        //   기능은 다 만들어 놓고 <b>부르는 자리가 늦어</b> 안 도는, 알아채기 어려운 종류다.
        Layers.CollectionChanged += (_, e) =>
        {
            HookLayers(e);
            RebuildThicknessColumns();
            SyncThicknessLength();
        };
        SeedDefaultLayers();
    }

    /// <summary>처음 열 때 흔한 다섯 층을 깔아 둔다 — <b>빈 표는 무엇을 해야 할지 안 알려 준다</b>.
    /// 이름은 사용자가 고치면 되고, 안 쓰는 줄은 지우면 된다.</summary>
    /// <summary>모드에 맞는 층 목록을 깐다. <b>모드를 바꾸면 다시 깐다</b>.</summary>
    private void SeedDefaultLayers()
    {
        Layers.Clear();
        foreach (var (nm, rk) in SeedFor(Mode))
            Layers.Add(new LayerRow { Name = nm, Rock = rk, Mode = InterpMode.Thickness });
        RebuildThicknessColumns();
    }

    /// <summary>★★★[JACK 0901] 모드를 바꾼다 — <b>친 값을 지운다</b>.
    /// <para>두께 <c>3</c>과 표고 <c>3</c>은 <b>다른 값</b>이다. 그대로 두면 엉뚱한 지층이 생긴다.
    /// 지운다는 것을 <b>말해 주고</b> 지운다 — 조용히 지우면 사람이 친 것을 잃은 줄 모른다.</para></summary>
    private void SetMode(StrataHeightMode m)
    {
        if (Mode == m) return;
        Mode = m;
        SeedDefaultLayers();
        foreach (var b in Bores) { b.Th.Clear(); }
        StrataEdit.SyncLength(Layers.Count, Bores.Select(b => b.Th));
        SafeRefresh();
        _modeHint.Text = HintFor(Mode);
        Say(Mode == StrataHeightMode.Elevation
            ? "층별 GL값으로 바꿨습니다 — 친 값의 뜻이 달라져 표를 비웠습니다."
            : "층별 두께로 바꿨습니다 — 친 값의 뜻이 달라져 표를 비웠습니다.");
    }

    private void Build()
    {
        var root = new Grid { Margin = new Thickness(10) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });                      // ① 카드
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // ② 카드
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });                      // 알림
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });                      // 확인

        // ── ① 지층 카드 ──────────────────────────────────────────────
        var c1 = new Grid();
        c1.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });     // 제목
        c1.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });     // 옵션단추
        c1.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });     // 설명
        c1.RowDefinitions.Add(new RowDefinition { Height = new GridLength(150) }); // 표
        c1.Children.Add(MakeHead("① 지층 높이 설정",
            "시추주상도를 어떻게 읽어 오실지 고르세요."));

        // ★★★[JACK 0901] <b>모드는 도면 전체에서 하나</b> — 층마다 고르면 헷갈린다.
        var modeBox = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 2) };
        void Rb(RadioButton rb, string t, StrataHeightMode m, bool on)
        {
            rb.Content = t;
            rb.GroupName = "지층높이";
            rb.IsChecked = on;
            rb.Margin = new Thickness(0, 0, 14, 0);
            rb.Foreground = Ink;
            rb.FontSize = 12;
            rb.VerticalContentAlignment = VerticalAlignment.Center;
            rb.Checked += (_, _) => SetMode(m);
            modeBox.Children.Add(rb);
        }
        // ★[JACK 0901] <b>층별 GL값이 먼저이고 기본</b>이다.
        Rb(_rbGl, "층별 GL값", StrataHeightMode.Elevation, true);
        Rb(_rbTh, "층별 두께", StrataHeightMode.Thickness, false);
        Grid.SetRow(modeBox, 1); c1.Children.Add(modeBox);

        _modeHint.Text = HintFor(Mode);
        _modeHint.Foreground = Sub;
        _modeHint.FontSize = 11;
        _modeHint.TextWrapping = TextWrapping.Wrap;
        // ★[JACK 0901 "아래 표하고 간격을 한 칸만 띄워 줘 — 너무 붙었어"]
        _modeHint.Margin = new Thickness(0, 0, 0, 12);
        Grid.SetRow(_modeHint, 2); c1.Children.Add(_modeHint);

        _gLayer.ItemsSource = Layers;
        _gLayer.AutoGenerateColumns = false;
        _gLayer.CanUserAddRows = false;
        _gLayer.HeadersVisibility = DataGridHeadersVisibility.Column;
        Skin(_gLayer);
        _gLayer.Columns.Add(new DataGridTextColumn
        { Header = "도면표시이름", Binding = new Binding("Name"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        // ★[JACK 0901] <b>적용값·도면표시 칸을 없앴다.</b>
        //   적용값은 위 옵션단추 하나로 정해지고, 도면표시는 <b>암층만</b>으로 고정이다.
        //   수량 분류는 <b>읽기 전용</b>이다 — 줄 자체가 그 분류이기 때문이다.
        _gLayer.Columns.Add(new DataGridTextColumn
        {
            Header = "수량 분류",
            Binding = new Binding("RockText"),
            Width = 92,
            IsReadOnly = true,
        });
        Grid.SetRow(_gLayer, 3); c1.Children.Add(_gLayer);
        // ★★[JACK 0901 "층 추가·선택 삭제 등 기능은 없애자 — 버튼이 많으면 헷갈리거든"]
        //   층 목록은 <b>모드가 정한다</b>(SeedFor). 수량 분류가 다섯뿐이라
        //   더 넣을 것도 뺄 것도 없다 — 안 쓰는 층은 <b>비워 두면</b> 수량에서 저절로 빠진다.
        root.Children.Add(MakeCard(c1, 0));

        // ── ② 보링공 카드 ────────────────────────────────────────────
        var c2 = new Grid();
        // ★[JACK 0901 "평면에서 찍기랑 선택 삭제가 있는 그 줄을 보링공 표 위로 올려 줘"]
        //   <b>일하는 차례대로</b> 놓는다 — 찍고 나서 표에 값을 친다.
        c2.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });                      // 제목
        c2.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });                      // 단추 줄
        c2.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // 표
        // ★[JACK 0901 문구 확정] 짧게 — 무엇을 치는지는 <b>위 모드 안내</b>가 이미 말한다.
        c2.Children.Add(MakeHead("② 보링공", "지층 데이터 입력. (지반고 자동 로딩)"));

        _gBore.ItemsSource = Bores;
        _gBore.AutoGenerateColumns = false;
        _gBore.CanUserAddRows = false;
        _gBore.HeadersVisibility = DataGridHeadersVisibility.Column;
        // ★★[JACK 0831 검토] <b>열 옮기기·정렬을 막는다.</b>
        //   칸이 무엇인지는 <see cref="_cols"/>가 <b>만든 차례</b>로 알고 있는데,
        //   사용자가 열을 끌어 옮기면 <b>보이는 차례</b>와 어긋나 붙여넣기가 엉뚱해 보인다.
        //   정렬은 더 나쁘다 — 화면 줄 차례와 <c>Bores</c> 차례가 달라져
        //   <b>엉뚱한 공에</b> 부어 넣는다(이쪽은 실제로 값이 틀어진다).
        //   막아 두면 "보이는 대로 붙는다"가 언제나 참이다.
        _gBore.CanUserReorderColumns = false;
        _gBore.CanUserSortColumns = false;
        Skin(_gBore);
        // ★★[JACK 0831] <b>Ctrl+V로도 붙는다.</b> 단추만 두면 엑셀에서 복사한 손이
        //   자연스럽게 누르는 그 조합이 아무 일도 안 해서 "안 되는 기능"으로 보인다.
        _gBore.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.V
                && (System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Control) != 0)
            { PasteFromClipboard(); e.Handled = true; }
        };
        Grid.SetRow(_gBore, 2); c2.Children.Add(_gBore);

        var bbtn = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 8) };
        bbtn.Children.Add(MakeBtn("평면에서 찍기", (_, _) => Pick(), primary: true));
        // ★★[JACK 0828 검토] <b>도면에서 못 지웠으면 표에서도 지우지 않는다.</b>
        //   종전엔 <c>EraseMark</c>가 실패해도 줄을 지웠다 — 그러면 평면에 <b>유령 표식</b>이 남고
        //   그것을 가리키던 줄이 사라져 <b>지울 길이 영영 없어진다</b>.
        bbtn.Children.Add(MakeBtn("선택 삭제", (_, _) =>
        {
            if (_gBore.SelectedItem is not BoreRow r) return;
            bool gone = StrataDraw.EraseMark(r);
            if (!gone && !r.MarkId.IsNull)
            { Say($"{r.Name}의 평면 표식을 못 지웠습니다 — 표에서도 지우지 않습니다(유령이 남습니다)."); return; }
            Bores.Remove(r);
            Say($"{r.Name} 삭제 — 다음에 찍으면 {r.Name}부터 다시 씁니다");
        }));
        // ★★[JACK 0831 "세모 버튼 위아래로 되어 있는 것 두 개"] <b>줄 차례를 바꾼다.</b>
        //   조사보고서의 공 차례와 표 차례를 맞추려면 필요하다 —
        //   찍는 차례를 미리 정해 두기 어렵기 때문이다.
        //   ★<b>이름은 안 바꾼다.</b> 이름은 평면 표식에 글자로 박혀 있어서,
        //   줄을 옮길 때마다 다시 매기면 <b>도면의 BH1이 다른 공을 가리키게</b> 된다 —
        //   표만 보면 깔끔한데 도면과 어긋나는, 가장 알아채기 어려운 종류다.
        //   (차례대로 다시 매기고 싶으면 말해 주세요 — 표식 글자까지 같이 고쳐야 합니다.)
        bbtn.Children.Add(MakeBtn("▲", (_, _) => MoveRow(-1), narrow: true));
        bbtn.Children.Add(MakeBtn("▼", (_, _) => MoveRow(+1), narrow: true));
        // ★[JACK 0828] <b>[지반고 다시 읽기]를 없앴다.</b>
        //   JACK: <i>"XY값을 쳐서 바꾸면 그 위치로 블록이 실시간 이동하고 지반고가 업데이트되어야 해.
        //   그래서 지반고 다시 읽기 기능은 필요가 없어."</i>
        //   맞다 — 자리가 바뀌는 길이 <b>둘뿐</b>(찍기·표 편집)이고 둘 다 그때 다시 읽는다.
        //   <b>손으로 눌러야 맞는 값이 되는 단추</b>는 안 누르면 틀린 값이 남는다는 뜻이라 없는 편이 낫다.
        // ★★[JACK 0831 "BH점 크기를 조절할 수 있는 바 넣어(음량조절처럼 좌우로 드래그)"]
        //   부지가 100m인지 2km인지에 따라 알맞은 표식 크기가 <b>열 배씩</b> 다르다.
        //   숫자를 치게 하면 몇을 쳐야 할지 모르므로 <b>끌어서 보면서</b> 맞추는 것이 맞다.
        //   ★끌 때마다 도면을 다시 그리면 뻑뻑하다 → <see cref="ScheduleResize"/>가 모았다 한 번 한다.
        bbtn.Children.Add(new TextBlock
        {
            Text = "  크기",
            Foreground = Sub,
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(6, 0, 6, 0),
        });
        // ★★[JACK 0831 "크기 바가 처음 1일 때를 기준으로 바의 1/3 지점에 있게 해 줘"]
        //   <b>기본값이 바의 어디에 앉는지가 곧 손잡이의 뜻</b>이다.
        //   0.2~8.0이면 ×1이 왼쪽 끝에서 <b>10%</b> 자리라, 기본 크기가 "가장 작은 축"처럼 보이고
        //   줄이는 쪽으로는 거의 못 움직인다.
        //   → 줄이는 쪽 1 : 키우는 쪽 2 로 나눈다. <c>(1 − 0.2) / (2.6 − 0.2) = 1/3</c>.
        _sizeBar.Minimum = SizeMin;
        _sizeBar.Maximum = SizeMax;
        // 지난번에 저장된 값이 이 범위를 벗어날 수 있다(옛 판은 8.0까지 허용했다) → 가둔다.
        _sizeBar.Value = System.Math.Max(SizeMin, System.Math.Min(SizeMax, StrataDraw.MarkScale));
        StrataDraw.MarkScale = _sizeBar.Value;
        _sizeBar.Width = 96;
        _sizeBar.SmallChange = 0.1;
        _sizeBar.LargeChange = 0.5;
        _sizeBar.IsSnapToTickEnabled = false;
        _sizeBar.VerticalAlignment = VerticalAlignment.Center;
        _sizeBar.ToolTip = $"보링공 표식 크기 ×{SizeMin:0.0} ~ ×{SizeMax:0.0} — 끌어서 맞추세요(도면에 바로 반영됩니다). 가운데 왼쪽 1/3이 기본 ×1.0";
        _sizeBar.ValueChanged += (_, _) => ScheduleResize();
        bbtn.Children.Add(_sizeBar);
        _sizeText.Foreground = Sub;
        _sizeText.FontSize = 11;
        _sizeText.VerticalAlignment = VerticalAlignment.Center;
        _sizeText.Margin = new Thickness(6, 0, 0, 0);
        _sizeText.Text = $"×{StrataDraw.MarkScale:0.0}";
        bbtn.Children.Add(_sizeText);

        Grid.SetRow(bbtn, 1); c2.Children.Add(bbtn);
        root.Children.Add(MakeCard(c2, 1));

        // ── 알림 줄 — 옅은 바탕에 담아 <b>말이 눈에 띄되 시끄럽지 않게</b>.
        _status.TextWrapping = TextWrapping.Wrap;
        _status.Foreground = Sub;
        _status.FontSize = 11;
        var sbox = new Border
        {
            Background = Wall,
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(9, 6, 9, 6),
            Margin = new Thickness(0, 0, 0, 8),
            Child = _status,
        };
        Grid.SetRow(sbox, 2); root.Children.Add(sbox);
        Say("① 지층을 정하고 ② 평면에서 찍기로 시추 위치를 클릭하세요.");

        // ── 확인 — 하나뿐인 큰 단추. 무엇을 눌러야 하는지 헷갈릴 자리가 없다.
        var ok = MakeBtn("확인 — 지층데이터 만들기", (_, _) => Confirm(), primary: true);
        ok.Height = 38;
        ok.FontSize = 13;
        ok.FontWeight = FontWeights.SemiBold;
        ok.Margin = new Thickness(0);
        Grid.SetRow(ok, 3); root.Children.Add(ok);

        Background = Wall;
        Content = root;
    }

    // ── 겉모습 ★★[JACK 0828 "도킹바 UI를 좀 예쁘게 못하나? 너무 각져서"] ──────────────
    //   기본 WPF는 <b>90년대 회색 상자</b>다. 색을 요란하게 쓰는 대신
    //   <b>모서리를 둥글리고 · 여백을 주고 · 선을 옅게</b> 하는 셋만으로 훨씬 부드러워진다.
    //   ★색은 <b>한 가지 강조색</b>만 쓴다 — 여러 색을 쓰면 어디를 봐야 할지 알 수 없다.

    private static readonly Brush Ink = new SolidColorBrush(Color.FromRgb(0x24, 0x2A, 0x33));      // 글자
    private static readonly Brush Sub = new SolidColorBrush(Color.FromRgb(0x6B, 0x74, 0x84));      // 옅은 글자
    private static readonly Brush Line = new SolidColorBrush(Color.FromRgb(0xDF, 0xE4, 0xEA));     // 옅은 선
    private static readonly Brush Card = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF));     // 카드 바탕
    private static readonly Brush Wall = new SolidColorBrush(Color.FromRgb(0xF4, 0xF6, 0xF9));     // 창 바탕
    private static readonly Brush Zebra = new SolidColorBrush(Color.FromRgb(0xFA, 0xFB, 0xFD));    // 줄무늬
    private static readonly Brush Accent = new SolidColorBrush(Color.FromRgb(0x1F, 0x6F, 0xEB));   // 강조
    private static readonly Brush AccentDim = new SolidColorBrush(Color.FromRgb(0xE8, 0xF0, 0xFE));

    /// <summary>카드 — 둥근 모서리에 옅은 테두리. 구역을 <b>눈으로</b> 가른다.</summary>
    private static Border MakeCard(UIElement inner, int row)
    {
        var b = new Border
        {
            Background = Card,
            BorderBrush = Line,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(8),
            Margin = new Thickness(0, 0, 0, 8),
            Child = inner,
        };
        Grid.SetRow(b, row);
        return b;
    }

    /// <summary>구역 제목 — 앞에 <b>강조색 막대</b>를 세워 눈이 먼저 닿게 한다.</summary>
    private static StackPanel MakeHead(string t, string hint)
    {
        var s = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
        s.Children.Add(new Border
        {
            Background = Accent,
            Width = 3,
            CornerRadius = new CornerRadius(2),
            Margin = new Thickness(0, 1, 7, 1),
        });
        var col = new StackPanel();
        col.Children.Add(new TextBlock { Text = t, FontWeight = FontWeights.SemiBold, FontSize = 13, Foreground = Ink });
        if (!string.IsNullOrEmpty(hint))
            col.Children.Add(new TextBlock { Text = hint, FontSize = 11, Foreground = Sub, Margin = new Thickness(0, 1, 0, 0) });
        s.Children.Add(col);
        return s;
    }

    /// <summary>단추 — 둥근 모서리에 <b>손을 올리면 밝아진다</b>. 각진 회색 상자를 안 쓴다.</summary>
    /// <param name="narrow">▲▼처럼 <b>글자 하나짜리</b> 단추 — 옆 여백을 줄여 나란히 붙게 한다.</param>
    private static Button MakeBtn(string t, RoutedEventHandler h, bool primary = false, bool narrow = false)
    {
        var b = new Button
        {
            Content = t,
            Margin = new Thickness(0, 0, narrow ? 3 : 6, 0),
            Padding = narrow ? new Thickness(9, 5, 9, 5) : new Thickness(12, 5, 12, 5),
            Foreground = primary ? Brushes.White : Ink,
            BorderThickness = new Thickness(primary ? 0 : 1),
            Cursor = System.Windows.Input.Cursors.Hand,
            FontSize = 12,
        };
        var tpl = new ControlTemplate(typeof(Button));
        var bd = new FrameworkElementFactory(typeof(Border));
        bd.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
        bd.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
        bd.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
        bd.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));
        bd.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
        var cp = new FrameworkElementFactory(typeof(ContentPresenter));
        cp.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        cp.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        bd.AppendChild(cp);
        tpl.VisualTree = bd;

        var over = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        over.Setters.Add(new Setter(Control.BackgroundProperty,
            primary ? (Brush)new SolidColorBrush(Color.FromRgb(0x17, 0x5C, 0xC8)) : AccentDim));
        tpl.Triggers.Add(over);

        b.Template = tpl;
        b.Background = primary ? Accent : Card;
        b.BorderBrush = Line;
        b.Click += h;
        return b;
    }

    /// <summary>표 — <b>세로 칸선을 없애고</b> 가로만 옅게. 줄무늬로 읽기 쉽게.</summary>
    private static void Skin(DataGrid g)
    {
        g.Background = Card;
        g.BorderThickness = new Thickness(0);
        g.RowBackground = Card;
        g.AlternatingRowBackground = Zebra;
        g.GridLinesVisibility = DataGridGridLinesVisibility.Horizontal;
        g.HorizontalGridLinesBrush = Line;
        g.Foreground = Ink;
        g.FontSize = 12;
        g.RowHeight = 24;
        g.CanUserResizeRows = false;
        g.SelectionUnit = DataGridSelectionUnit.FullRow;

        // 머리줄 — 굵지 않게, 아래에 옅은 선 하나.
        var hs = new Style(typeof(System.Windows.Controls.Primitives.DataGridColumnHeader));
        hs.Setters.Add(new Setter(Control.BackgroundProperty, Wall));
        hs.Setters.Add(new Setter(Control.ForegroundProperty, Sub));
        hs.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.SemiBold));
        hs.Setters.Add(new Setter(Control.FontSizeProperty, 11.0));
        hs.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(6, 4, 6, 4)));
        hs.Setters.Add(new Setter(Control.BorderBrushProperty, Line));
        hs.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0, 0, 0, 1)));
        g.ColumnHeaderStyle = hs;

        var cs = new Style(typeof(DataGridCell));
        cs.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
        cs.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(6, 2, 6, 2)));
        var sel = new Trigger { Property = DataGridCell.IsSelectedProperty, Value = true };
        sel.Setters.Add(new Setter(Control.BackgroundProperty, AccentDim));
        sel.Setters.Add(new Setter(Control.ForegroundProperty, Ink));
        cs.Triggers.Add(sel);
        g.CellStyle = cs;
    }

    /// <summary>알림 한 줄 — <b>조용히 실패하지 않는다</b>.</summary>
    internal void Say(string s) => _status.Text = s;

    /// <summary>★ 두께 칸을 <b>층 수만큼</b> 다시 만든다.
    /// <para>고정 칸(이름·X·Y·지반고)은 그대로 두고 <b>가운데 두께 칸만</b> 갈아 끼운다.</para></summary>
    // ── 보링공 표의 칸 폭 ★★[JACK 0901 "도킹바 처음 켜질 때 2번 보링공 표가 안 잘릴 만큼 길이로"]
    //   <b>창 너비가 이 값들을 읽는다</b>(<see cref="WidestWidth"/>) — 칸을 넓히면 창도 따라 넓어진다.
    //   두 곳에 적으면 칸을 고칠 때 창이 안 따라와 <b>또 잘린다</b>(§50).
    private const double WName = 60, WXy = 80, WGl = 70, WWater = 90, WLayer = 60;

    /// <summary>가장 넓을 때의 표 너비 + 창 테두리 — <b>도킹바를 이만큼 열어야 안 잘린다</b>.
    /// <para>가장 넓은 것은 <b>층별 두께</b> 모드다(층 여섯). GL 모드(넷)로 시작해도
    /// 모드를 바꾸는 순간 넓어지므로 <b>처음부터 넓은 쪽</b>에 맞춘다 —
    /// 바꿀 때마다 창이 들썩이는 것보다 낫다.</para></summary>
    internal static int WidestWidth
    {
        get
        {
            int layers = 0;
            foreach (var m in new[] { StrataHeightMode.Elevation, StrataHeightMode.Thickness })
                if (SeedFor(m).Length > layers) layers = SeedFor(m).Length;
            double table = WName + WXy * 2 + WGl + WWater + WLayer * layers;
            // 세로 스크롤막대 · 카드 테두리·여백(8×2) · 뿌리 여백(10×2) · 팔레트 테두리
            const double Chrome = 18 + 16 + 20 + 24;
            return (int)System.Math.Ceiling(table + Chrome);
        }
    }

    /// <summary>칸 하나가 <b>무엇인가</b>. 붙여넣기가 이것을 보고 어디에 넣을지 정한다.</summary>
    private enum ColKind { Name, X, Y, Gl, Water, Layer }

    /// <summary>★★★[검토 대비] <b>칸 차례를 아는 곳은 여기 하나다.</b>
    /// <para>처음에는 <c>ColX = 1</c>처럼 번호를 따로 적어 뒀는데, 그러면 열을 하나 끼워 넣는 순간
    /// 붙여넣기가 <b>조용히 어긋난다</b> — Y 값이 지반고 칸으로 들어가는 식이다.
    /// 예외도 안 나고 표는 멀쩡해 보인다. <b>같은 것을 두 곳에서 세는</b> §50 그 함정이다.</para>
    /// <para>→ 열을 만들면서 <b>동시에</b> 이 목록을 채운다. 둘이 어긋날 수가 없다.</para></summary>
    private readonly List<(ColKind Kind, int Layer)> _cols = new();

    internal void RebuildThicknessColumns()
    {
        _gBore.Columns.Clear();
        _cols.Clear();

        void Col(DataGridColumn c, ColKind k, int layer = -1)
        { _gBore.Columns.Add(c); _cols.Add((k, layer)); }

        Col(new DataGridTextColumn { Header = "이름", Binding = new Binding("Name"), Width = WName, IsReadOnly = true },
            ColKind.Name);
        Col(new DataGridTextColumn { Header = "X", Binding = Fmt("X", "0.###"), Width = WXy }, ColKind.X);
        Col(new DataGridTextColumn { Header = "Y", Binding = Fmt("Y", "0.###"), Width = WXy }, ColKind.Y);
        // ★지반고는 <b>읽기 전용</b> — 사람이 안 친다(JACK 확정). 원지반에서 읽은 값이다.
        Col(new DataGridTextColumn { Header = "지반고", Binding = Fmt("Gl", "0.00"), Width = WGl, IsReadOnly = true },
            ColKind.Gl);

        // ★★[JACK 0831 "지하수위 심도를 지반고 다음으로 놔줘 — 층 추가 시 계속 새 층이 밀려서 보기가 어려워"]
        //   맞는 지적이다. 층은 <b>몇 개가 될지 모르고 계속 늘어나는</b> 칸이라
        //   그 뒤에 둔 것은 무엇이든 오른쪽으로 밀려 <b>가로로 긁어야</b> 보인다.
        //   지하수위는 층 수와 무관하게 <b>공마다 하나</b>인 값이므로 지반고 옆에 붙는 것이 자리다.
        //   → <b>늘어나는 칸은 언제나 맨 끝</b>. 고정 칸을 그 뒤에 두지 않는다.
        Col(new DataGridTextColumn { Header = "지하수위 심도", Binding = Fmt("Water", "0.##"), Width = WWater },
            ColKind.Water);

        for (int i = 0; i < Layers.Count; i++)
        {
            // ★★[JACK 0831·0901] 열 머리가 <b>무엇을 치는 칸인지</b> 말한다 — 모드가 정한다.
            // ★★[JACK 0901 "연 암 GL 이런 식으로 쓴 거 표에서 헷갈리니깐 다 붙여"]
            //   <b>이름만 쓴다.</b> 무엇을 치는지(GL이냐 두께냐)는 <b>바로 위 옵션단추와 안내</b>가
            //   크게 말하고 있어 칸마다 되풀이할 필요가 없다 — 좁은 칸에서 오히려 읽기 어렵다.
            string head = string.IsNullOrWhiteSpace(Layers[i].Name) ? $"층{i + 1}" : Layers[i].Name;
            Col(new DataGridTextColumn { Header = head, Binding = Fmt($"Th[{i}]", "0.##"), Width = WLayer },
                ColKind.Layer, i);
        }
    }

    /// <summary>숫자 칸 — <c>NaN</c>은 <b>빈칸</b>으로 보인다(0이 아니다).
    ///
    /// <para>★★[JACK 0831 "XY 좌표를 쳐서 바꿀 때도 바로바로 안 바뀌고 꼭 어딘가 다른 곳을 눌러 줘야 한다"]
    /// <b>원인: <c>LostFocus</c>였다.</b> 그 칸에서 <b>빠져나가야</b> 값이 넘어간다 —
    /// 그래서 아무 데나 한 번 눌러야 표식이 움직였다.
    /// → <c>PropertyChanged</c>로 바꾼다. 치는 즉시 넘어간다.</para>
    ///
    /// <para>다만 이것만 바꾸면 <b>한 글자마다</b> 도면 일이 돈다 —
    /// 그래서 무거운 일(표식 옮기기·지반고 읽기)은 <see cref="ScheduleMove"/>가 <b>모았다 한 번</b> 한다.
    /// 둘을 같이 해야 "바로 바뀌고 안 느리다"가 된다.</para></summary>
    private static Binding Fmt(string path, string f) => new(path)
    {
        StringFormat = f,
        TargetNullValue = "",
        Mode = BindingMode.TwoWay,
        UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged,
    };

    /// <summary>★[JACK 0831] 층의 <b>이름·적용값</b>이 바뀌면 보링공 표의 열 머리도 따라간다.
    /// <para>종전엔 층을 <b>더하거나 뺄 때만</b> 다시 만들어, 이름을 고쳐도 열 머리가 옛 이름이었다.
    /// 적용값을 <c>GL</c>로 바꿔도 표시가 없어 <b>무엇을 치는 칸인지</b> 알 수 없었다.</para></summary>
    private void HookLayers(System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        // ★[JACK 0831 검토] <b>순서변경(Move)에서는 아무것도 안 한다.</b>
        //   Move는 <c>NewItems</c>·<c>OldItems</c>가 <b>같은 줄</b>이라 <c>+=</c> 뒤 <c>-=</c>가 되어
        //   그 줄의 구독이 <b>조용히 죽는다</b>. 지금 층 표엔 순서변경 UI가 없지만,
        //   나중에 ▲▼를 달면 옮긴 줄만 안 따라오는 일이 생긴다.
        if (e != null && e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Move) return;
        if (e?.NewItems != null)
            foreach (LayerRow r in e.NewItems)
                r.PropertyChanged += OnLayerChanged;
        if (e?.OldItems != null)
            foreach (LayerRow r in e.OldItems)
                r.PropertyChanged -= OnLayerChanged;
    }

    private void OnLayerChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(LayerRow.Name)) return;
        // 표를 치던 중에 열을 다시 만들면 WPF가 막는다 — 편집을 먼저 끝낸다(SafeRefresh와 같은 이유).
        try { _gBore.CommitEdit(DataGridEditingUnit.Row, true); } catch { }
        try { RebuildThicknessColumns(); } catch { }
    }

    /// <summary>층 수가 바뀌면 모든 공의 두께 칸 수를 맞춘다 — <b>모자라면 모른다(NaN)로 채운다</b>.</summary>
    internal void SyncThicknessLength()
    {
        StrataEdit.SyncLength(Layers.Count, Bores.Select(b => b.Th));
        SafeRefresh();
    }

    /// <summary>★★★[JACK 0828 검토] <b>표를 치던 중에 단추를 누르면 터졌다.</b>
    /// <para>WPF는 <c>Items.Refresh()</c>를 <b>편집 중</b>에 막는다(<c>InvalidOperationException</c>).
    /// DataGrid는 바깥 단추로 포커스가 가도 <b>행 편집을 저절로 안 끝낸다</b> —
    /// 그래서 두께를 치다가 [＋ 층 추가]를 누르면 AutoCAD 오류 대화상자가 떴다.</para>
    /// <para>이 파일 <c>AddBore</c> 주석이 <b>바로 이 예외</b> 때문에 <c>Moved</c>에서 Refresh를 뺐다고
    /// 적어 놓고, 정작 다른 두 자리에는 그대로 남겨 뒀다 — <b>같은 것을 두 곳에서</b> 따로 다룬 것이다.
    /// 이제 고치는 자리는 여기 하나다.</para></summary>
    private void SafeRefresh()
    {
        // 먼저 편집을 끝낸다(칸 → 행 차례로). 편집 중이 아니면 아무 일도 안 한다.
        try { _gBore.CommitEdit(System.Windows.Controls.DataGridEditingUnit.Cell, true); } catch { }
        try { _gBore.CommitEdit(System.Windows.Controls.DataGridEditingUnit.Row, true); } catch { }
        try { _gBore.Items.Refresh(); }
        catch (System.Exception ex)
        {
            // 그래도 막히면 <b>표만 안 새로 그려질 뿐</b> 자료는 멀쩡하다 — 터뜨리지 않는다.
            Say("표 새로 그리기를 미뤘습니다(편집 중) — " + ex.GetType().Name);
        }
    }

    /// <summary>표를 다시 그린다(창을 다시 열 때).</summary>
    internal void Refresh() { RebuildThicknessColumns(); SyncThicknessLength(); }

    // ── 줄 차례 바꾸기 · 표식 크기 ───────────────────────────────────────────

    /// <summary>★[JACK 0831] 고른 공을 <b>한 칸 위/아래</b>로.
    /// <para>이름은 그대로 따라간다 — 평면 표식의 글자와 어긋나면 안 되기 때문이다.
    /// 즉 <b>줄만 옮기고 공은 그대로</b>다.</para></summary>
    private void MoveRow(int delta)
    {
        if (_gBore.SelectedItem is not BoreRow r) { Say("옮길 공을 먼저 고르세요."); return; }
        int i = Bores.IndexOf(r);
        int j = i + delta;
        if (i < 0 || j < 0 || j >= Bores.Count) return;      // 끝에서는 아무 일도 안 한다
        try { _gBore.CommitEdit(DataGridEditingUnit.Row, true); } catch { }
        Bores.Move(i, j);
        // 옮긴 줄을 계속 고른 채로 둔다 — 그래야 연달아 누를 수 있다.
        _gBore.SelectedItem = r;
        try { _gBore.ScrollIntoView(r); } catch { }
        Say($"{r.Name} — {j + 1}번째 줄로 옮겼습니다(이름과 좌표는 그대로)");
    }

    private System.Windows.Threading.DispatcherTimer _sizeTimer;

    /// <summary>★[JACK 0831] 크기 바를 끄는 동안 <b>도면을 매번 다시 그리지 않는다</b>.
    /// <para>바를 한 번 끌면 값이 수십 번 바뀐다. 그때마다 표식을 지우고 다시 그리면
    /// 바가 <b>뻑뻑하게</b> 끌린다 — XY 칸에서 이미 겪은 것과 같은 문제라 같은 길로 푼다.</para></summary>
    private void ScheduleResize()
    {
        double v = _sizeBar.Value;
        _sizeText.Text = $"×{v:0.0}";        // 숫자는 <b>바로</b> 따라간다(끄는 느낌이 살아야 한다)
        StrataDraw.MarkScale = v;
        if (_sizeTimer == null)
        {
            _sizeTimer = new System.Windows.Threading.DispatcherTimer
            { Interval = TimeSpan.FromMilliseconds(250) };
            _sizeTimer.Tick += (_, _) =>
            {
                _sizeTimer.Stop();
                if (Bores.Count == 0) return;
                int n = StrataDraw.Redraw(Bores);
                try { GradingSettings.SaveUserPrefs(); } catch { }   // 다음에 켤 때도 이 크기
                Say($"표식 크기 ×{StrataDraw.MarkScale:0.0} — {n}개 다시 그림");
            };
        }
        _sizeTimer.Stop();
        _sizeTimer.Start();
    }

    // ── 이름 짓기 ────────────────────────────────────────────────────────────

    /// <summary>★★[JACK 0831 "BH1을 선택 삭제하고 평면에서 찍기를 하면 다시 BH2가 생겨서 BH2가 두 개가 돼"]
    ///
    /// <para><b>원인: 이름을 <c>개수 + 1</c>로 지었다.</b> BH1·BH2에서 BH1을 지우면 개수가 1이 되어
    /// 다음 이름이 다시 <c>BH2</c>다 — <b>이미 있는 이름</b>이다.
    /// 개수는 <b>지금 몇 개인가</b>일 뿐 <b>어떤 이름이 쓰였는가</b>를 모른다. 그 둘을 같은 것으로 봤다.</para>
    ///
    /// <para>→ <b>비어 있는 가장 작은 번호</b>를 쓴다. BH1을 지우면 다음은 다시 BH1이다.
    /// (계속 커지게 하는 길도 있지만, 표가 짧고 눈으로 보는 것이라 <b>번호에 구멍이 없는 편</b>이
    /// 읽기 좋다. 지운 공의 번호를 다시 쓰는 것이 곤란해지면 그때 바꾸면 된다.)</para></summary>
    private string NextName()
    {
        var used = new HashSet<int>();
        foreach (var b in Bores)
        {
            if (b?.Name == null || !b.Name.StartsWith("BH", StringComparison.Ordinal)) continue;
            if (int.TryParse(b.Name.Substring(2), out int k)) used.Add(k);
        }
        int n = 1;
        while (used.Contains(n)) n++;
        return "BH" + n;
    }

    // ── 엑셀에서 붙여넣기 ────────────────────────────────────────────────────

    /// <summary>★★★[JACK 0831 "엑셀에 작성한 값만을 복사해서 보링공표에 미리 만들어 놓은 GP 칸들에 붙여넣기할 수 없나?"]
    ///
    /// <para><b>WPF 표는 복사는 되지만 붙여넣기는 안 들어 있다</b> — 직접 만든다.
    /// 엑셀에서 여러 칸을 복사하면 클립보드에 <b>탭으로 나뉜 글자</b>가 들어온다
    /// (줄바꿈=행, 탭=열). 그것을 <b>지금 고른 칸부터</b> 차례로 부어 넣는다.</para>
    ///
    /// <para><b>고른 칸부터인 이유.</b> 언제나 X부터 채우면 두께만 뽑아 온 엑셀을 못 쓴다.
    /// 채울 자리를 사람이 고르게 하면 <b>X·Y만</b>이든 <b>두께만</b>이든 다 된다.</para>
    ///
    /// <para><b>안 건드리는 칸이 있다.</b> 이름은 우리가 짓고(중복이 나면 안 된다),
    /// 지반고는 <b>원지반에서 읽는 값</b>이다 — 사람이 친 값으로 덮으면 도면과 어긋난다.
    /// 그 자리에 온 값은 <b>조용히 버리지 않고 몇 개를 건너뛰었는지 말한다</b>.</para>
    ///
    /// <para><b>행이 모자라면 공을 더 만든다</b> — 단, X·Y가 같이 온 경우만.
    /// 좌표를 모르면 평면에 표식을 세울 수 없어 <b>표에만 있는 유령 공</b>이 되기 때문이다.</para></summary>
    private void PasteFromClipboard()
    {
        string text;
        try { text = System.Windows.Clipboard.GetText(); }
        catch (System.Exception ex) { Say("클립보드를 못 읽었습니다 — " + ex.Message); return; }
        if (string.IsNullOrWhiteSpace(text)) { Say("클립보드가 비어 있습니다. 엑셀에서 칸을 복사한 뒤 다시 누르세요."); return; }

        // 편집 중이면 먼저 끝낸다 — 안 그러면 아래 값 넣기가 되돌려진다.
        try { _gBore.CommitEdit(DataGridEditingUnit.Cell, true); } catch { }
        try { _gBore.CommitEdit(DataGridEditingUnit.Row, true); } catch { }

        // ★★★[스스로 잡음] <b>가운데 빈 줄을 버리면 안 된다.</b>
        //   엑셀에서 한 열을 복사할 때 <b>중간 칸이 비어 있으면 빈 줄</b>로 온다.
        //   그것을 걸러 내면 아래 줄들이 <b>한 칸씩 위로 당겨져</b> BH3 값이 BH2에 들어간다 —
        //   예외도 안 나고 숫자는 그럴듯해서 <b>도면이 다 만들어진 뒤에야</b> 이상한 것을 안다.
        //   → <b>맨 끝의 빈 줄만</b> 뗀다(엑셀은 늘 줄바꿈으로 끝난다). 가운데는 그대로 둔다.
        var all = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        int last = all.Length;
        while (last > 0 && all[last - 1].Length == 0) last--;
        var lines = all.Take(last).ToArray();
        if (lines.Length == 0) { Say("붙여넣을 줄이 없습니다."); return; }

        // 시작 자리 — 고른 칸이 없으면 첫 공의 X부터.
        int row0 = 0, col0 = _cols.FindIndex(c => c.Kind == ColKind.X);
        if (col0 < 0) col0 = 0;
        try
        {
            if (_gBore.CurrentItem is BoreRow cur)
            {
                int ix = Bores.IndexOf(cur);
                if (ix >= 0) row0 = ix;
            }
            // ★만든 차례로 짚는다 — 화면 차례(DisplayIndex)가 아니다.
            //   <c>_cols</c>가 만든 차례와 <b>같은 순서로</b> 채워지므로 둘이 어긋날 수 없다.
            if (_gBore.CurrentColumn != null)
            {
                int ci0 = _gBore.Columns.IndexOf(_gBore.CurrentColumn);
                if (ci0 >= 0) col0 = ci0;
            }
        }
        catch { }

        int nCell = 0, nSkipRO = 0, nNew = 0, nBad = 0, nOver = 0, nKeep = 0;
        var touched = new List<BoreRow>();
        var moved = new HashSet<BoreRow>();

        for (int r = 0; r < lines.Length; r++)
        {
            var cells = lines[r].Split('\t');
            int ri = row0 + r;

            BoreRow b;
            if (ri < Bores.Count) b = Bores[ri];
            else
            {
                // 새 공은 <b>좌표가 있어야</b> 만든다 — 표식을 세울 자리를 알아야 한다.
                double nx = double.NaN, ny = double.NaN;
                for (int c = 0; c < cells.Length; c++)
                {
                    int ci = col0 + c;
                    if (ci < 0 || ci >= _cols.Count) continue;
                    if (_cols[ci].Kind == ColKind.X) nx = Num(cells[c]);
                    else if (_cols[ci].Kind == ColKind.Y) ny = Num(cells[c]);
                }
                if (double.IsNaN(nx) || double.IsNaN(ny)) { nOver += lines.Length - r; break; }
                b = NewRow(nx, ny);
                Bores.Add(b);
                StrataDraw.ReadGl(b);
                StrataDraw.DrawMark(b);
                nNew++;
            }

            for (int c = 0; c < cells.Length; c++)
            {
                int ci = col0 + c;
                if (ci < 0 || ci >= _cols.Count) break;          // 표보다 넓게 복사했다
                var (kind, li) = _cols[ci];
                // 우리가 정하는 칸 — 이름은 겹치면 안 되고, 지반고는 원지반에서 읽는 값이다.
                if (kind == ColKind.Name || kind == ColKind.Gl) { nSkipRO++; continue; }
                string s = cells[c].Trim();
                double v = Num(s);
                if (s.Length > 0 && double.IsNaN(v)) { nBad++; continue; }   // 글자가 섞였다
                // ★★★[스스로 잡음] <b>좌표는 빈칸을 못 받는다.</b>
                //   다른 칸은 빈칸이 "모른다(NaN)"로 남아도 되지만, X·Y가 NaN이 되면
                //   <b>표식을 NaN 자리로 옮기려 든다</b> — 블록이 사라지거나 도면이 깨진다.
                //   빈칸이면 <b>있던 좌표를 그대로 둔다</b>. 좌표를 지우고 싶다는 뜻일 리가 없다.
                if (s.Length == 0 && (kind == ColKind.X || kind == ColKind.Y)) { nKeep++; continue; }
                switch (kind)
                {
                    case ColKind.X: if (!Same(b.X, v)) { b.X = v; moved.Add(b); } break;
                    case ColKind.Y: if (!Same(b.Y, v)) { b.Y = v; moved.Add(b); } break;
                    case ColKind.Water: b.Water = v; break;
                    case ColKind.Layer:
                        if (li < 0 || li >= b.Th.Count) continue;
                        b.Th[li] = v;
                        break;
                    default: continue;
                }
                nCell++;
            }
            if (!touched.Contains(b)) touched.Add(b);
        }

        SafeRefresh();
        foreach (var b in moved) ScheduleMove(b);

        var msg = new System.Text.StringBuilder($"붙여넣기 — 공 {touched.Count}개 · 칸 {nCell}개");
        if (nNew > 0) msg.Append($" · 새 공 {nNew}개");
        if (nSkipRO > 0) msg.Append($" · 이름·지반고 칸 {nSkipRO}개는 건너뜀(우리가 정하는 값)");
        if (nBad > 0) msg.Append($" · 숫자가 아니라 못 넣은 것 {nBad}개");
        if (nKeep > 0) msg.Append($" · 빈 좌표 칸 {nKeep}개는 있던 값을 그대로 둡니다");
        if (nOver > 0) msg.Append($" · ⚠좌표가 없어 못 만든 줄 {nOver}개");
        Say(msg.ToString());
    }

    /// <summary>엑셀 칸 하나 → 숫자. 빈칸은 <b>모른다(NaN)</b>다 — 0이 아니다.
    /// <para>천 단위 쉼표를 떼고, 한글 도면에서 흔한 <c>1,234.5</c>도 받는다.</para></summary>
    private static double Num(string s)
    {
        if (s == null) return double.NaN;
        s = s.Trim().Replace(",", "");
        if (s.Length == 0) return double.NaN;
        return double.TryParse(s, System.Globalization.NumberStyles.Float,
                               System.Globalization.CultureInfo.InvariantCulture, out double v)
            || double.TryParse(s, out v) ? v : double.NaN;
    }

    private static bool Same(double a, double b) =>
        (double.IsNaN(a) && double.IsNaN(b)) || Math.Abs(a - b) < 1e-9;

    /// <summary>공 한 줄을 만든다 — 두께 칸 수와 <c>Moved</c> 배선까지 <b>한자리에서</b>.
    /// <para>★찍기와 붙여넣기가 <b>따로</b> 만들면 한쪽만 고쳐지는 §50 함정에 빠진다.</para></summary>
    private BoreRow NewRow(double x, double y)
    {
        var row = new BoreRow { Name = NextName(), X = Math.Round(x, 3), Y = Math.Round(y, 3) };
        while (row.Th.Count < Layers.Count) row.Th.Add(double.NaN);
        row.Moved += ScheduleMove;
        return row;
    }


    // ── 자리 바뀜을 모았다 한 번에 ───────────────────────────────────────────
    private System.Windows.Threading.DispatcherTimer _moveTimer;
    private readonly HashSet<BoreRow> _movePending = new();

    /// <summary>★★★[JACK 0831 "어떨 때는 딜레이가 심해"]
    ///
    /// <para><b>원인: 값이 바뀔 때마다 도면 일을 통째로 돌렸다.</b>
    /// 표식 옮기기는 <b>문서 잠금 + 트랜잭션</b>이고, 지반고 읽기는 <b>지표면 조회</b>다
    /// (부지 밖이면 예외까지 난다). 좌표 한 번 고치는 데 이 둘이 붙어 있었다.</para>
    ///
    /// <para>게다가 <see cref="Fmt"/>를 <c>PropertyChanged</c>로 바꾸면 <b>한 글자마다</b> 돈다 —
    /// <c>123.456</c>을 치면 일곱 번이다. 반응은 빨라지고 <b>도면은 더 느려지는</b> 맞바꿈이 된다.</para>
    ///
    /// <para>→ <b>신호는 즉시 받고, 일은 잠깐 모았다 한 번 한다.</b>
    /// 치는 동안에는 타이머가 계속 미뤄지고, 손을 멈추면 그때 한 번 움직인다.
    /// 여러 공을 잇달아 고쳐도 <b>공마다 한 번</b>이다.</para></summary>
    private void ScheduleMove(BoreRow b)
    {
        if (b == null) return;
        _movePending.Add(b);
        if (_moveTimer == null)
        {
            _moveTimer = new System.Windows.Threading.DispatcherTimer
            { Interval = TimeSpan.FromMilliseconds(350) };
            _moveTimer.Tick += (_, _) =>
            {
                _moveTimer.Stop();
                var todo = _movePending.ToArray();
                _movePending.Clear();
                foreach (var r in todo)
                {
                    try
                    {
                        StrataDraw.MoveMark(r);
                        StrataDraw.ReadGl(r);
                    }
                    catch (System.Exception ex) { Say($"{r.Name} 옮기기 실패 — {ex.Message}"); continue; }
                }
                if (todo.Length == 1)
                {
                    var r = todo[0];
                    Say($"{r.Name} 옮김 — 지반고 {(double.IsNaN(r.Gl) ? "못 읽음(원지반 밖?)" : r.Gl.ToString("0.00"))}");
                }
                else if (todo.Length > 1) Say($"{todo.Length}개 공을 옮겼습니다");
            };
        }
        _moveTimer.Stop();    // 치는 중이면 계속 미룬다
        _moveTimer.Start();
    }

    /// <summary>평면에서 찍기 — <b>명령으로 넘긴다</b>.
    /// <para>도킹바는 화면 스레드에서 도는데 점 찍기는 <b>도면 스레드</b>의 일이다.
    /// 직접 <c>GetPoint</c>를 부르면 AutoCAD가 굳는다 — 명령으로 보내야 한다.</para></summary>
    private void Pick()
    {
        var doc = AcadApp.DocumentManager.MdiActiveDocument;
        if (doc == null) { Say("도면이 없습니다."); return; }
        Say("평면도에서 시추 위치를 클릭하세요…");
        doc.SendStringToExecute("DHSTRATAPICK ", true, false, true);
    }

    /// <summary>★ 찍은 자리를 표에 더한다 — 명령이 부른다.</summary>
    internal void AddBore(Point3d p)
    {
        // ★★[JACK 0831 "BH1을 선택 삭제하고 평면에서 찍기를 하면 BH2가 두 개가 돼"]
        //   이름은 <see cref="NextName"/>이 짓는다 — <c>개수 + 1</c>은 <b>이미 쓰인 이름</b>을 모른다.
        // ★★[JACK 0828] 자리가 바뀌면 <b>표식이 따라가고 지반고를 다시 읽는다</b>.
        //   ★<b>여기서 <c>Items.Refresh()</c>를 부르면 안 된다.</b> 이 일은 사람이 X·Y 칸을
        //   고치는 <b>도중</b>에 일어나는데, 표가 편집 중일 때 새로 고치면
        //   <i>"AddNew·EditItem 중에는 Refresh할 수 없다"</i>는 예외가 난다.
        //   <see cref="BoreRow.Gl"/>이 바뀌면서 스스로 알리므로 <b>새로 고칠 필요가 없다</b> —
        //   표는 그 알림을 듣고 저절로 다시 그린다.
        //   ★줄 만들기는 <see cref="NewRow"/> 하나로 — 찍기와 붙여넣기가 따로 만들면
        //   한쪽만 고쳐지는 §50 함정에 빠진다.
        var row = NewRow(p.X, p.Y);
        Bores.Add(row);
        StrataDraw.ReadGl(row);
        StrataDraw.DrawMark(row);
        SafeRefresh();
        Say($"{row.Name} 추가 — 지반고 {(double.IsNaN(row.Gl) ? "못 읽음(원지반 밖?)" : row.Gl.ToString("0.00"))}");
    }

    /// <summary>★ [확인] — 지층을 만든다.</summary>
    private void Confirm()
    {
        // ★★★[JACK 0901] <b>두 모드는 치는 값의 뜻이 다르다.</b>
        //   두께 모드 — 표에 친 것이 그대로 두께다.
        //   GL 모드   — 표에 친 것은 <b>암층 상단 표고</b>이고, 토사층은 <b>저절로</b> 생긴다.
        //   옮기는 셈은 <see cref="StrataInput"/> 한 곳에 있다(하니스 S90이 지킨다).
        List<StratumDef> defs;
        List<BoreLog> logs;
        List<bool> shows;

        if (Mode == StrataHeightMode.Elevation)
        {
            var rocks = Layers.Where(l => !string.IsNullOrWhiteSpace(l.Name))
                              .Select(l => (l.Name, l.Rock)).ToList();
            var tops = Bores.Select(b => b.Th.ToArray()).ToList();
            var gls = Bores.Select(b => b.Gl).ToList();
            if (!StrataInput.FromRockTops(rocks, tops, gls, out defs, out var thRows, out string cw))
            { Say("만들 수 없습니다 — " + cw); return; }
            logs = new List<BoreLog>();
            for (int i = 0; i < Bores.Count; i++)
                logs.Add(new BoreLog(Bores[i].Name, Bores[i].X, Bores[i].Y, Bores[i].Gl, thRows[i], Bores[i].Water));
            // ★토사는 도면에 안 그린다 — 그 상단이 곧 원지반이다. 암층만 그린다.
            shows = new List<bool> { false };
            for (int i = 0; i < rocks.Count; i++) shows.Add(true);
        }
        else
        {
            defs = Layers.Where(l => !string.IsNullOrWhiteSpace(l.Name))
                         .Select(l => new StratumDef(l.Name, l.Rock, InterpMode.Thickness)).ToList();
            // ★★[JACK 0901] 도면표시 스위치를 없앴다 — <b>암층만</b> 그린다.
            //   값이 하나뿐인 스위치는 사람을 헷갈리게만 한다.
            shows = Layers.Where(l => !string.IsNullOrWhiteSpace(l.Name))
                          .Select(l => l.Rock != RockClass.Soil).ToList();
            // ★★★[JACK 0901] <b>만들기 전에 짝이 맞는지 묻는다.</b>
            //   두께가 층과 어긋난 채 만들면 두께가 <b>엉뚱한 층</b>에 붙어
            //   조용히 틀린 지층면이 나온다 — 표는 멀쩡해 보인다.
            if (!StrataEdit.Aligned(defs.Count, Bores.Select(b => b.Th), out string alignWhy))
            { Say("만들지 않았습니다 — " + alignWhy + " (층을 다시 확인해 주세요)"); return; }
            logs = Bores.Select(b => new BoreLog(b.Name, b.X, b.Y, b.Gl, b.Th.ToArray(), b.Water)).ToList();
        }

        var model = StrataModel.Build(defs, logs, out string why);
        if (model == null) { Say("만들 수 없습니다 — " + why); return; }

        string r = StrataDraw.BuildSurfaces(model, out int made, out string note, shows);
        // ★[JACK 0901 문구 확정] <b>어디서 보이는지</b>를 같이 말한다 —
        //   평면에서는 안 보이므로, 안 그러면 "만들었다는데 왜 안 보이지"가 된다.
        Say($"지층 {made}개를 작성하였습니다. (지층은 종단, 횡단에서만 보입니다.)"
          + (note.Length > 0 ? " · " + note : "")
          + (r.Length > 0 ? " · " + r : ""));
    }
}
