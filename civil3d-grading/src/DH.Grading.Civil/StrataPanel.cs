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

    /// <summary>지금 열려 있는 도킹바 — 명령이 여기로 값을 넣는다.</summary>
    internal static StrataPanel Current { get; private set; }

    public StrataPanel()
    {
        Current = this;
        Build();
        SeedDefaultLayers();
        Layers.CollectionChanged += (_, _) => { RebuildThicknessColumns(); SyncThicknessLength(); };
    }

    /// <summary>처음 열 때 흔한 다섯 층을 깔아 둔다 — <b>빈 표는 무엇을 해야 할지 안 알려 준다</b>.
    /// 이름은 사용자가 고치면 되고, 안 쓰는 줄은 지우면 된다.</summary>
    private void SeedDefaultLayers()
    {
        // ★★[JACK 0828] <b>적용값은 전부 '두께'로 시작한다.</b>
        //   JACK: <i>"적용값은 디폴트로 모두 두께로 해 줘."</i>
        //   <b>맞는 기본값이다.</b> 처음 판에서 연암·경암을 <c>GL</c>로 깔았더니
        //   JACK 부지에서 <b>역전이 936곳</b> 났다 — <c>GL</c>은 보링공 표고에 매여 있어
        //   지형이 보링공보다 낮아지는 자리마다 암반이 흙을 뚫고 올라온다.
        //   <b>두께는 역전이 원천 불가</b>하므로, 안전한 쪽을 기본으로 두고
        //   암반을 눕히고 싶을 때만 사람이 <c>GL</c>로 바꾸는 것이 맞다.
        void L(string n, RockClass r) => Layers.Add(new LayerRow { Name = n, Rock = r, Mode = InterpMode.Thickness });
        L("표토", RockClass.Soil);
        L("풍화토", RockClass.Soil);
        L("풍화암", RockClass.Weathered);
        L("연암", RockClass.Soft);
        L("경암", RockClass.Hard);
        RebuildThicknessColumns();
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
        c1.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        c1.RowDefinitions.Add(new RowDefinition { Height = new GridLength(140) });
        c1.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        c1.Children.Add(MakeHead("① 지층", "위에서 아래 차례로. 이름은 조사보고서 그대로 쓰세요."));

        _gLayer.ItemsSource = Layers;
        _gLayer.AutoGenerateColumns = false;
        _gLayer.CanUserAddRows = false;
        _gLayer.HeadersVisibility = DataGridHeadersVisibility.Column;
        Skin(_gLayer);
        _gLayer.Columns.Add(new DataGridTextColumn
        { Header = "이름", Binding = new Binding("Name"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        // ★[JACK 0828] 우리말로 보인다 — 속은 열거형 그대로, 껍데기만 바꾼다.
        //   수량 분류 이름은 <b>토적표가 쓰는 그것</b>(QtyTableSpec.NameOf)에서 나온다 —
        //   두 곳이 따로 이름을 지으면 화면과 도면이 어긋난다(JACK: "표에 들어갈 명칭하고 통일").
        _gLayer.Columns.Add(new DataGridComboBoxColumn
        {
            Header = "수량 분류",
            ItemsSource = LayerRow.RockChoices,
            SelectedItemBinding = new Binding("RockText") { Mode = BindingMode.TwoWay },
            Width = 92,
        });
        _gLayer.Columns.Add(new DataGridComboBoxColumn
        {
            Header = "적용값",
            ItemsSource = LayerRow.ModeChoices,
            SelectedItemBinding = new Binding("ModeText") { Mode = BindingMode.TwoWay },
            Width = 74,
        });
        Grid.SetRow(_gLayer, 1); c1.Children.Add(_gLayer);

        var lbtn = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        lbtn.Children.Add(MakeBtn("＋ 층 추가", (_, _) => Layers.Add(new LayerRow { Name = "새 층" })));
        // ★★★[JACK 0828 검토] <b>지운 자리에서 빼야 한다.</b>
        //   종전엔 층만 지우고 두께는 <c>SyncThicknessLength</c>가 <b>언제나 끝에서</b> 뺐다 —
        //   5층 중 2번째를 지우면 열 머리는 한 칸 당겨지는데 값은 그대로라
        //   <b>모든 공의 두께가 통째로 한 칸씩 밀린다</b>. 예외도 로그도 없고 표는 멀쩡해 보이는데
        //   만들어지는 지층면만 조용히 틀린다 — 도면에서 알아채기 가장 어려운 종류다.
        lbtn.Children.Add(MakeBtn("－ 선택 삭제", (_, _) =>
        {
            if (_gLayer.SelectedItem is not LayerRow r) return;
            int ix = Layers.IndexOf(r);
            if (ix < 0) return;
            foreach (var b in Bores)
                if (ix < b.Th.Count) b.Th.RemoveAt(ix);   // ★같은 자리에서
            Layers.Remove(r);                              // 그다음에 층을 지운다
            Say($"'{r.Name}' 층 삭제 — 모든 공의 {ix + 1}번째 두께도 같이 뺐다");
        }));
        Grid.SetRow(lbtn, 2); c1.Children.Add(lbtn);
        root.Children.Add(MakeCard(c1, 0));

        // ── ② 보링공 카드 ────────────────────────────────────────────
        var c2 = new Grid();
        c2.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        c2.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        c2.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        c2.Children.Add(MakeHead("② 보링공",
            "지반고는 원지반에서 자동으로 읽습니다 — 치는 것은 두께뿐입니다."));

        _gBore.ItemsSource = Bores;
        _gBore.AutoGenerateColumns = false;
        _gBore.CanUserAddRows = false;
        _gBore.HeadersVisibility = DataGridHeadersVisibility.Column;
        Skin(_gBore);
        Grid.SetRow(_gBore, 1); c2.Children.Add(_gBore);

        var bbtn = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        bbtn.Children.Add(MakeBtn("평면에서 찍기", (_, _) => Pick(), primary: true));
        bbtn.Children.Add(MakeBtn("－ 선택 삭제", (_, _) =>
        {
            if (_gBore.SelectedItem is BoreRow r) { StrataDraw.EraseMark(r); Bores.Remove(r); }
        }));
        // ★[JACK 0828] <b>[지반고 다시 읽기]를 없앴다.</b>
        //   JACK: <i>"XY값을 쳐서 바꾸면 그 위치로 블록이 실시간 이동하고 지반고가 업데이트되어야 해.
        //   그래서 지반고 다시 읽기 기능은 필요가 없어."</i>
        //   맞다 — 자리가 바뀌는 길이 <b>둘뿐</b>(찍기·표 편집)이고 둘 다 그때 다시 읽는다.
        //   <b>손으로 눌러야 맞는 값이 되는 단추</b>는 안 누르면 틀린 값이 남는다는 뜻이라 없는 편이 낫다.
        Grid.SetRow(bbtn, 2); c2.Children.Add(bbtn);
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
        var ok = MakeBtn("확인 — 지층 만들기", (_, _) => Confirm(), primary: true);
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
    private static Button MakeBtn(string t, RoutedEventHandler h, bool primary = false)
    {
        var b = new Button
        {
            Content = t,
            Margin = new Thickness(0, 0, 6, 0),
            Padding = new Thickness(12, 5, 12, 5),
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
    internal void RebuildThicknessColumns()
    {
        _gBore.Columns.Clear();
        _gBore.Columns.Add(new DataGridTextColumn { Header = "이름", Binding = new Binding("Name"), Width = 60, IsReadOnly = true });
        _gBore.Columns.Add(new DataGridTextColumn { Header = "X", Binding = Fmt("X", "0.###"), Width = 80 });
        _gBore.Columns.Add(new DataGridTextColumn { Header = "Y", Binding = Fmt("Y", "0.###"), Width = 80 });
        // ★지반고는 <b>읽기 전용</b> — 사람이 안 친다(JACK 확정). 원지반에서 읽은 값이다.
        _gBore.Columns.Add(new DataGridTextColumn
        { Header = "지반고", Binding = Fmt("Gl", "0.00"), Width = 70, IsReadOnly = true });

        for (int i = 0; i < Layers.Count; i++)
        {
            string head = string.IsNullOrWhiteSpace(Layers[i].Name) ? $"층{i + 1}" : Layers[i].Name;
            _gBore.Columns.Add(new DataGridTextColumn
            { Header = head, Binding = Fmt($"Th[{i}]", "0.##"), Width = 60 });
        }
        _gBore.Columns.Add(new DataGridTextColumn { Header = "지하수위 심도", Binding = Fmt("Water", "0.##"), Width = 90 });
    }

    /// <summary>숫자 칸 — <c>NaN</c>은 <b>빈칸</b>으로 보인다(0이 아니다).</summary>
    private static Binding Fmt(string path, string f) => new(path)
    {
        StringFormat = f,
        TargetNullValue = "",
        Mode = BindingMode.TwoWay,
        UpdateSourceTrigger = UpdateSourceTrigger.LostFocus,
    };

    /// <summary>층 수가 바뀌면 모든 공의 두께 칸 수를 맞춘다 — <b>모자라면 모른다(NaN)로 채운다</b>.</summary>
    internal void SyncThicknessLength()
    {
        foreach (var b in Bores)
        {
            while (b.Th.Count < Layers.Count) b.Th.Add(double.NaN);
            while (b.Th.Count > Layers.Count) b.Th.RemoveAt(b.Th.Count - 1);
        }
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
        var row = new BoreRow { Name = $"GP{Bores.Count + 1}", X = Math.Round(p.X, 3), Y = Math.Round(p.Y, 3) };
        while (row.Th.Count < Layers.Count) row.Th.Add(double.NaN);
        // ★★[JACK 0828] 자리가 바뀌면 <b>표식이 따라가고 지반고를 다시 읽는다</b>.
        //   ★<b>여기서 <c>Items.Refresh()</c>를 부르면 안 된다.</b> 이 일은 사람이 X·Y 칸을
        //   고치는 <b>도중</b>에 일어나는데, 표가 편집 중일 때 새로 고치면
        //   <i>"AddNew·EditItem 중에는 Refresh할 수 없다"</i>는 예외가 난다.
        //   <see cref="BoreRow.Gl"/>이 바뀌면서 스스로 알리므로 <b>새로 고칠 필요가 없다</b> —
        //   표는 그 알림을 듣고 저절로 다시 그린다.
        row.Moved += b =>
        {
            StrataDraw.MoveMark(b);
            StrataDraw.ReadGl(b);
            Say($"{b.Name} 옮김 — 지반고 {(double.IsNaN(b.Gl) ? "못 읽음(원지반 밖?)" : b.Gl.ToString("0.00"))}");
        };
        Bores.Add(row);
        StrataDraw.ReadGl(row);
        StrataDraw.DrawMark(row);
        SafeRefresh();
        Say($"{row.Name} 추가 — 지반고 {(double.IsNaN(row.Gl) ? "못 읽음(원지반 밖?)" : row.Gl.ToString("0.00"))}");
    }

    /// <summary>★ [확인] — 지층을 만든다.</summary>
    private void Confirm()
    {
        var defs = Layers.Where(l => !string.IsNullOrWhiteSpace(l.Name))
                         .Select(l => new StratumDef(l.Name, l.Rock, l.Mode)).ToList();
        var logs = Bores.Select(b => new BoreLog(b.Name, b.X, b.Y, b.Gl, b.Th.ToArray(), b.Water)).ToList();

        var model = StrataModel.Build(defs, logs, out string why);
        if (model == null) { Say("만들 수 없습니다 — " + why); return; }

        string r = StrataDraw.BuildSurfaces(model, out int made, out string note);
        Say($"지층 {made}장 만들었습니다{(why.Length > 0 ? " · " + why : "")}{(note.Length > 0 ? " · " + note : "")}"
          + (r.Length > 0 ? " · " + r : ""));
    }
}
