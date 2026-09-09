using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DH.Grading.Civil;

/// <summary>★★★[JACK 0908 <i>"정지옵션하고 도면설정 팝업창이 너무 옛스럽고 딱딱한데 좀 요즘 스타일로 꾸밀수없어?"</i>
/// · <i>"회사로고파일(도화로고.png) 활용해서 설정창들을 꾸며줘"</i>
/// · <i>"색상까지 통일해서 꾸밀꺼면 도킹창 부분도 일관성있는 색으로 꾸며"</i>]
/// <b>이 애드인의 화면 한 벌 — 색·글꼴·컨트롤 모양·회사 로고가 전부 여기 있다.</b>
///
/// <para><b>왜 한 파일인가.</b> 종전에는 창마다 색을 따로 적고 있었다.
/// 지층 도킹창은 강조색이 <b>파랑</b>(<c>#1F6FEB</c>)이고, 지도 도킹창은 <c>WhiteSmoke·Gainsboro·DimGray</c>였고,
/// 두 팝업은 아무 색도 안 정해 <b>윈도우 기본 회색</b>이었다 — 같은 애드인의 화면 넷이 서로 남남이었다.
/// 색을 여러 곳에 적으면 <b>한쪽만 고쳐 놓고 왜 다른지 모르게 된다</b>
/// (이 저장소가 §20·§26에서 되풀이해 배운 실패다).</para>
///
/// <para><b>기준색은 회사 로고에서 뽑았다.</b> <c>참고자료\도화로고.png</c>의 글자 색이
/// <c>#00484E</c>(짙은 청록)다 — 눈으로 고른 것이 아니라 그림에서 가장 많이 쓰인 불투명 화소를 세어 얻었다.</para>
///
/// <para><b>로고는 색을 바꾸지 않는다.</b> 그래서 머리띠를 <b>흰 바탕</b>으로 두고
/// 그 위에 4px 짜리 청록 띠만 얹었다 — 회사 마크를 흰색으로 뒤집어 쓰는 것은
/// 브랜드 규정을 건드리는 일이고, 우리에겐 흰색 판 파일이 없다.</para>
///
/// <para><b>모양은 XAML 한 덩이로 넣는다</b>(<see cref="Skin"/>). C# 코드로
/// <c>ControlTemplate</c>을 조립하면 스무 줄이 백 줄이 되고 읽을 수가 없다.
/// ★파싱이 실패하면 <b>아무 것도 안 씌우고 그냥 넘어간다</b> — 꾸미다가 창이 안 뜨는 일은 없어야 한다.</para></summary>
internal static class DhBrand
{
    // ── 색 ────────────────────────────────────────────────────────────────
    /// <summary>회사 로고에서 뽑은 기준색 — <c>도화로고.png</c>의 최다 불투명 화소.</summary>
    internal const string BrandHex = "#00484E";

    internal static readonly SolidColorBrush Brand = Frozen(0x00, 0x48, 0x4E);   // 강조·머리띠·기본단추
    internal static readonly SolidColorBrush BrandHi = Frozen(0x0A, 0x62, 0x69); // 그 위(마우스)
    internal static readonly SolidColorBrush BrandDim = Frozen(0xE3, 0xEC, 0xEC); // 아주 옅은 청록(선택 바탕)
    internal static readonly SolidColorBrush Ink = Frozen(0x1B, 0x26, 0x28);     // 글자
    internal static readonly SolidColorBrush Sub = Frozen(0x6B, 0x7A, 0x7C);     // 옅은 글자
    internal static readonly SolidColorBrush Line = Frozen(0xDC, 0xE3, 0xE4);    // 옅은 선
    internal static readonly SolidColorBrush Card = Frozen(0xFF, 0xFF, 0xFF);    // 카드 바탕
    internal static readonly SolidColorBrush Wall = Frozen(0xF5, 0xF8, 0xF8);    // 창 바탕
    internal static readonly SolidColorBrush Zebra = Frozen(0xFA, 0xFC, 0xFC);   // 표 줄무늬
    internal static readonly SolidColorBrush Warn = Frozen(0xB0, 0x30, 0x28);    // 경고 글자
    /// <summary>★[검토 0908] <b>됐다</b>를 알리는 색 — 청록과 어울리는 짙은 초록.
    /// <para>성공 표시를 <see cref="Brand"/>로 칠하면 제목·단추와 <b>같은 색</b>이 되어
    /// "됐다"가 아니라 그냥 진한 글씨로 읽힌다. 신호는 <b>다른 색</b>이어야 신호다.</para></summary>
    internal static readonly SolidColorBrush Ok = Frozen(0x1B, 0x6B, 0x4D);      // 됐다

    private static SolidColorBrush Frozen(byte r, byte g, byte b)
    {
        var br = new SolidColorBrush(Color.FromRgb(r, g, b));
        br.Freeze();   // 얼려 두면 여러 창이 같은 것을 나눠 써도 안전하다
        return br;
    }

    /// <summary>화면 글꼴 — 한글이 또렷한 맑은 고딕, 없으면 Segoe UI.</summary>
    internal static readonly FontFamily Ui = new("Malgun Gothic, 맑은 고딕, Segoe UI");

    // ── 회사 로고 ──────────────────────────────────────────────────────────
    private static ImageSource? _logo;
    private static bool _logoTried;

    /// <summary>DLL에 심어 둔 회사 로고(<c>Resources\DohwaLogo.png</c>).
    /// <para>파일을 따로 챙길 필요가 없게 <b>어셈블리 안</b>에 넣는다 —
    /// <c>DHT.dwt</c>·리본 아이콘과 같은 방식이다. 못 읽으면 <c>null</c>이고,
    /// 그때는 머리띠가 <b>글자만</b>으로 그려진다(창은 그대로 뜬다).</para></summary>
    internal static ImageSource? Logo
    {
        get
        {
            if (_logoTried) return _logo;
            _logoTried = true;
            try
            {
                var asm = Assembly.GetExecutingAssembly();
                string? res = System.Array.Find(asm.GetManifestResourceNames(),
                                                n => n.EndsWith("DohwaLogo.png", System.StringComparison.OrdinalIgnoreCase));
                if (res == null) return null;
                using var s = asm.GetManifestResourceStream(res);
                if (s == null) return null;
                var bi = new BitmapImage();
                bi.BeginInit();
                bi.CacheOption = BitmapCacheOption.OnLoad;   // 스트림을 닫아도 살아 있게
                bi.StreamSource = s;
                bi.EndInit();
                bi.Freeze();
                _logo = bi;
            }
            catch { _logo = null; }
            return _logo;
        }
    }

    // ── 컨트롤 모양 ────────────────────────────────────────────────────────
    private static ResourceDictionary? _skin;
    private static bool _skinTried;

    /// <summary>입력칸·콤보·단추·옵션단추·체크·슬라이더의 <b>요즘 모양</b>.
    /// <para>이름표(<c>x:Key</c>) 없는 스타일이라 <see cref="Apply"/>로 붙이기만 하면
    /// 그 창 안의 컨트롤 <b>전부</b>에 저절로 걸린다 — 만드는 자리를 하나하나 고칠 필요가 없다.</para></summary>
    internal static ResourceDictionary? Skin
    {
        get
        {
            if (_skinTried) return _skin;
            _skinTried = true;
            try { _skin = (ResourceDictionary)XamlReader.Parse(SkinXaml); }
            catch { _skin = null; }   // ★꾸미기 실패가 창을 막아서는 안 된다
            return _skin;
        }
    }

    /// <summary>이 요소와 그 아래 전부에 <see cref="Skin"/>을 씌운다.
    /// 팝업이든 도킹창이든 <b>이 한 줄</b>이면 같은 모양이 된다.</summary>
    internal static void Apply(FrameworkElement fe)
    {
        var sk = Skin;
        if (sk == null) return;
        try
        {
            if (!fe.Resources.MergedDictionaries.Contains(sk))
                fe.Resources.MergedDictionaries.Add(sk);
            // ★<c>FrameworkElement</c>에는 글꼴 속성이 없다 — <b>물려 내려가는 첨부 속성</b>으로 건다.
            //   이렇게 걸면 그 아래 모든 글자에 저절로 닿는다(도킹창의 표 안까지).
            TextElement.SetFontFamily(fe, Ui);
            TextElement.SetFontSize(fe, 12);
        }
        catch { }
    }

    /// <summary>★[JACK 0908] <b>컨트롤 하나에만</b> 새 모양을 건다.
    ///
    /// <para><see cref="Apply"/>는 창 전체에 거는 문이다. 그런데 <b>표가 있는 창</b>에서는
    /// 그것이 지나치다 — <c>DataGrid</c>의 글자 편집칸도 <c>TextBox</c>라서,
    /// 높이 28px에 테두리와 여백이 붙은 상자가 되어 <b>표가 되레 둔해진다</b>.
    /// 그런 창에서는 <b>바꿔야 할 컨트롤만</b> 이 문으로 고른다.</para>
    ///
    /// <para>지층 도킹창이 그런 경우다 — 거기서 남의 색을 쓰던 것은
    /// <b>옵션단추뿐</b>이었다(윈도우 강조색, 보통 파랑).</para></summary>
    internal static void StyleOne(FrameworkElement fe)
    {
        var sk = Skin;
        if (sk == null) return;
        try
        {
            if (sk[fe.GetType()] is Style st) fe.SetValue(FrameworkElement.StyleProperty, st);
        }
        catch { }
    }

    // ── 머리띠(로고 + 제목) ────────────────────────────────────────────────
    /// <summary>회사 로고와 제목이 든 <b>머리띠</b>.
    /// <param name="drag">여기를 끌어 창을 옮길 수 있게 할 창. 도킹창이면 <c>null</c>.</param>
    /// <param name="onClose">닫기(✕) 단추를 놓을지 — 도킹창은 자기 닫기가 따로 있으므로 <c>null</c>.</param></summary>
    /// <param name="pad">머리띠 안쪽 여백. 도킹창처럼 <b>옆에 놓인 카드와 세로줄을 맞춰야</b>
    /// 하는 자리에서는 이 값을 줄여 준다 — 종전엔 팝업용 16px이 고정이라
    /// 도킹창에서 로고가 카드 글자보다 7px 더 안쪽으로 밀려 있었다(검토 실측).</param>
    internal static Border Header(string title, string? subtitle, Window? drag, System.Action? onClose,
                                  double logoHeight = 26, double titleSize = 15, Thickness? pad = null)
    {
        var bar = new DockPanel { LastChildFill = true, Margin = pad ?? new Thickness(16, 11, 12, 11) };

        if (Logo != null)
        {
            var img = new Image
            {
                Source = Logo,
                Height = logoHeight,
                Stretch = Stretch.Uniform,
                VerticalAlignment = VerticalAlignment.Center,
                SnapsToDevicePixels = true,
            };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
            DockPanel.SetDock(img, Dock.Left);
            bar.Children.Add(img);

            // 로고와 제목 사이 세로 칸막이 — 회사 마크와 창 이름을 눈으로 갈라 준다
            var sep = new Border
            {
                Width = 1,
                Background = Line,
                Margin = new Thickness(14, 3, 14, 3),
            };
            DockPanel.SetDock(sep, Dock.Left);
            bar.Children.Add(sep);
        }

        if (onClose != null)
        {
            var x = new Button
            {
                Content = "✕",
                Width = 28,
                Height = 28,
                // ★★★[검토 0908 · 높음] <b>이 두 줄이 없으면 ✕가 한 픽셀도 안 그려진다.</b>
                //   창 전체에 걸린 단추 모양표의 기본 여백은 <c>14,0,14,0</c>이다.
                //   폭 28에서 좌우 14씩 빼면 <b>남는 폭이 0</b> — 글자가 통째로 잘려 나간다
                //   (검토 실측: <c>ContentPresenter.ActualWidth = 0</c> · 흰색 아닌 화소 <b>0개</b>).
                //   ★<c>MinHeight</c>도 0으로 눌러야 한다 — 모양표의 <c>MinHeight=30</c>이
                //   <c>Height=28</c>을 이겨(WPF는 Height를 Min/Max 사이로 조인다) 혼자 2px 커진다.
                //   ★<b>제목줄을 우리가 그리기로 한 창</b>이라 이 단추가 곧 창의 닫기다.
                //   안 보이면 사람은 "닫기가 없는 창"으로 본다.
                Padding = new Thickness(0),
                MinHeight = 0,
                FontSize = 13,
                Foreground = Sub,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                VerticalAlignment = VerticalAlignment.Top,
                Cursor = Cursors.Hand,
                ToolTip = "닫기",
                Focusable = false,
            };
            x.Click += (_, __) => onClose();
            DockPanel.SetDock(x, Dock.Right);
            bar.Children.Add(x);
        }

        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        texts.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = titleSize,
            FontWeight = FontWeights.Bold,
            Foreground = Brand,
        });
        if (!string.IsNullOrEmpty(subtitle))
            texts.Children.Add(new TextBlock
            {
                Text = subtitle,
                FontSize = 11,
                Foreground = Sub,
                Margin = new Thickness(0, 2, 0, 0),
                TextWrapping = TextWrapping.NoWrap,
            });
        bar.Children.Add(texts);

        var band = new Border { Background = Card, Child = bar };
        if (drag != null)
        {
            band.Cursor = Cursors.SizeAll;
            band.MouseLeftButtonDown += (_, e) =>
            {
                // ★단추를 눌렀을 때는 끌지 않는다. DragMove는 왼쪽 단추가 눌린 동안에만 되므로 감싼다.
                if (e.ButtonState != MouseButtonState.Pressed) return;
                try { drag.DragMove(); } catch { }
            };
        }
        return band;
    }

    /// <summary>가로 실선 한 줄 — 머리띠·바닥띠를 본문과 가른다.</summary>
    internal static Border Rule(double h = 1) => new() { Height = h, Background = Line };

    /// <summary>맨 위 4px 청록 띠 — <b>회사 색</b>을 화면에 남기는 자리.
    /// <para>로고 자체를 청록 바탕에 흰색으로 뒤집을 수 없으므로(원본이 짙은 청록 단색이다)
    /// 색은 이 띠와 제목 글자·기본 단추가 맡는다.</para></summary>
    internal static Border Accent(double h = 4) => new() { Height = h, Background = Brand };

    // ── 팝업 창 한 벌 ──────────────────────────────────────────────────────
    /// <summary>팝업 창을 <b>통째로</b> 꾸민다 — 띠·머리띠·본문·바닥단추.
    ///
    /// <para><b>제목줄을 우리가 그린다</b>(<see cref="WindowStyle"/>=None).
    /// 윈도우 기본 제목줄을 두면 <b>제목이 두 줄</b>이 되어 오히려 어수선하다.
    /// 대신 옮기기(머리띠 끌기)와 닫기(✕)를 직접 달았고, Esc는 [취소] 단추가 받는다.</para>
    ///
    /// <para>★<b>실패해도 창은 뜬다.</b> 꾸미다 무슨 일이 나면 본문만 그대로 붙인다 —
    /// 정지 옵션은 이 애드인에서 가장 자주 여는 창이라 <b>안 뜨는 것이 가장 나쁘다</b>.</para></summary>
    internal static void Dress(Window w, string title, string? subtitle, UIElement body,
                              params Button[] footer)
    {
        try
        {
            Apply(w);
            w.WindowStyle = WindowStyle.None;
            w.AllowsTransparency = false;      // 투명은 SizeToContent과 함께 말썽이 난다 — 안 쓴다
            w.Background = Wall;
            w.Foreground = Ink;

            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });   // 0 청록 띠
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });   // 1 머리띠
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });   // 2 실선
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // 3 본문
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });   // 4 실선
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });   // 5 바닥

            void Put(UIElement e, int r) { Grid.SetRow(e, r); grid.Children.Add(e); }

            Put(Accent(), 0);
            Put(Header(title, subtitle, w, w.Close), 1);
            Put(Rule(), 2);

            // ★★[UI검토 0909] <b>화면보다 커지면 아래가 잘려 손댈 수가 없다.</b>
            //   정지 옵션 창은 높이가 약 1,015px인데, 노트북 125% 배율의 작업 영역은 826px다 —
            //   <b>약 190px(기타 옵션·좌표계)가 화면 밖</b>으로 나가고
            //   <c>ResizeMode=NoResize</c>라 늘릴 수도 없다.
            //   → 본문에 스크롤을 두고 창 높이에 <b>화면 상한</b>을 건다.
            //   ([저장]/[취소]는 이미 별도 행이라 잘리지 않는다.)
            var bodyWrap = new Border
            {
                Background = Wall,
                Child = new ScrollViewer
                {
                    Content = body,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                },
            };
            Put(bodyWrap, 3);
            try { w.MaxHeight = System.Windows.SystemParameters.WorkArea.Height - 40; } catch { }

            Put(Rule(), 4);

            var bar = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(18, 12, 18, 14),
            };
            foreach (var b in footer) bar.Children.Add(b);
            Put(new Border { Background = Card, Child = bar }, 5);

            w.Content = new Border
            {
                BorderBrush = Line,
                BorderThickness = new Thickness(1),
                Child = grid,
            };
        }
        catch
        {
            // ★★[검토 0908 · 보통] <b>제목줄을 되돌려 놓고</b> 물러선다.
            //   <c>WindowStyle=None</c>은 맨 처음에 걸었다. 그 뒤에서 무엇이 터지면
            //   본문만 되돌려서는 <b>옮길 수도 닫을 수도 없는 창</b>이 남는다 —
            //   머리띠(끌 손잡이)도 ✕도 아직 안 붙었기 때문이다.
            //   "실패해도 창은 뜬다"가 <b>뜨기만 하고 못 쓴다</b>가 되어서는 안 된다.
            try { w.WindowStyle = WindowStyle.SingleBorderWindow; } catch { }
            try { w.Content = body; } catch { }
        }
    }

    /// <summary>바닥 단추 둘 — 왼쪽이 [취소](흰색), 오른쪽이 기본 동작(청록).
    /// <para>순서를 여기서 정해 두면 두 창이 어긋날 일이 없다.</para></summary>
    internal static (Button Ok, Button Cancel) FooterButtons(string okText)
    {
        // ★<b>순서를 바꾸지 않았다</b>(저장이 왼쪽, 취소가 오른쪽).
        //   요즘 창은 기본 단추를 오른쪽 끝에 두는 것이 흔하지만, 이 창은 JACK이
        //   <b>몇 달째 같은 자리</b>에서 눌러 왔다 — 꾸미자고 자리를 옮기면
        //   <b>손이 기억하는 곳</b>이 어긋나 취소를 누르게 된다. 모양만 바꾸고 자리는 둔다.
        var ok = new Button { Content = okText, MinWidth = 96, Height = 32, Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button
        {
            Content = "취소",
            MinWidth = 84,
            Height = 32,
            IsCancel = true,          // Esc로 닫힌다 — 기본 제목줄을 없앤 만큼 이것이 중요하다
        };
        var sk = Skin;
        if (sk != null)
            try { ok.Style = (Style)sk["DhPrimary"]; } catch { }
        return (ok, cancel);
    }

    // ── 모양표(XAML) ───────────────────────────────────────────────────────
    /// <summary>이름표 없는 스타일 모음 — 붙이기만 하면 그 아래 전부에 걸린다.
    /// <para>편집형 콤보(<c>IsEditable</c>)는 이 저장소에 <b>하나도 없다</b>(0908 확인).
    /// 그래서 콤보 모양표에 글자 입력칸을 넣지 않았다 — 나중에 편집형을 쓰게 되면
    /// <c>PART_EditableTextBox</c>를 여기 더해야 한다.</para></summary>
    private const string SkinXaml = """
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">

  <SolidColorBrush x:Key="DhBrandB"    Color="#00484E"/>
  <SolidColorBrush x:Key="DhBrandHiB"  Color="#0A6269"/>
  <SolidColorBrush x:Key="DhBrandDimB" Color="#E3ECEC"/>
  <SolidColorBrush x:Key="DhInkB"      Color="#1B2628"/>
  <SolidColorBrush x:Key="DhSubB"      Color="#6B7A7C"/>
  <SolidColorBrush x:Key="DhLineB"     Color="#DCE3E4"/>
  <SolidColorBrush x:Key="DhCardB"     Color="#FFFFFF"/>
  <SolidColorBrush x:Key="DhWallB"     Color="#F5F8F8"/>

  <!-- 입력칸 -->
  <Style TargetType="TextBox">
    <Setter Property="FontFamily" Value="Malgun Gothic"/>
    <Setter Property="FontSize" Value="12"/>
    <Setter Property="Foreground" Value="{StaticResource DhInkB}"/>
    <Setter Property="Background" Value="{StaticResource DhCardB}"/>
    <Setter Property="BorderBrush" Value="{StaticResource DhLineB}"/>
    <Setter Property="BorderThickness" Value="1"/>
    <Setter Property="Padding" Value="8,0,8,0"/>
    <Setter Property="MinHeight" Value="28"/>
    <Setter Property="CaretBrush" Value="{StaticResource DhBrandB}"/>
    <Setter Property="SelectionBrush" Value="{StaticResource DhBrandB}"/>
    <Setter Property="VerticalContentAlignment" Value="Center"/>
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="TextBox">
          <Border x:Name="bd" CornerRadius="4" SnapsToDevicePixels="True"
                  Background="{TemplateBinding Background}"
                  BorderBrush="{TemplateBinding BorderBrush}"
                  BorderThickness="{TemplateBinding BorderThickness}">
            <ScrollViewer x:Name="PART_ContentHost" Focusable="False"
                          Margin="{TemplateBinding Padding}"
                          VerticalAlignment="Center"
                          HorizontalScrollBarVisibility="Hidden"
                          VerticalScrollBarVisibility="Hidden"/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property="IsMouseOver" Value="True">
              <Setter TargetName="bd" Property="BorderBrush" Value="{StaticResource DhBrandHiB}"/>
            </Trigger>
            <Trigger Property="IsKeyboardFocusWithin" Value="True">
              <Setter TargetName="bd" Property="BorderBrush" Value="{StaticResource DhBrandB}"/>
              <Setter TargetName="bd" Property="BorderThickness" Value="1.6"/>
            </Trigger>
            <Trigger Property="IsEnabled" Value="False">
              <Setter TargetName="bd" Property="Background" Value="{StaticResource DhWallB}"/>
              <Setter Property="Foreground" Value="{StaticResource DhSubB}"/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <!-- 단추: 기본은 흰 바탕(보조). 청록 바탕은 DhPrimary. -->
  <Style TargetType="Button">
    <Setter Property="FontFamily" Value="Malgun Gothic"/>
    <Setter Property="FontSize" Value="12"/>
    <Setter Property="Foreground" Value="{StaticResource DhInkB}"/>
    <Setter Property="Background" Value="{StaticResource DhCardB}"/>
    <Setter Property="BorderBrush" Value="{StaticResource DhLineB}"/>
    <Setter Property="BorderThickness" Value="1"/>
    <Setter Property="Padding" Value="14,0,14,0"/>
    <Setter Property="MinHeight" Value="30"/>
    <Setter Property="Cursor" Value="Hand"/>
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="Button">
          <Border x:Name="bd" CornerRadius="4" SnapsToDevicePixels="True"
                  Background="{TemplateBinding Background}"
                  BorderBrush="{TemplateBinding BorderBrush}"
                  BorderThickness="{TemplateBinding BorderThickness}">
            <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"
                              Margin="{TemplateBinding Padding}" RecognizesAccessKey="True"/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property="IsMouseOver" Value="True">
              <Setter TargetName="bd" Property="Background" Value="{StaticResource DhBrandDimB}"/>
              <Setter TargetName="bd" Property="BorderBrush" Value="{StaticResource DhBrandB}"/>
            </Trigger>
            <Trigger Property="IsPressed" Value="True">
              <Setter TargetName="bd" Property="Background" Value="{StaticResource DhBrandDimB}"/>
              <Setter TargetName="bd" Property="BorderBrush" Value="{StaticResource DhBrandHiB}"/>
            </Trigger>
            <Trigger Property="IsEnabled" Value="False">
              <Setter TargetName="bd" Property="Background" Value="{StaticResource DhWallB}"/>
              <Setter Property="Foreground" Value="{StaticResource DhSubB}"/>
              <Setter Property="Cursor" Value="Arrow"/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style x:Key="DhPrimary" TargetType="Button">
    <Setter Property="FontFamily" Value="Malgun Gothic"/>
    <Setter Property="FontSize" Value="12"/>
    <Setter Property="FontWeight" Value="SemiBold"/>
    <Setter Property="Foreground" Value="White"/>
    <Setter Property="Background" Value="{StaticResource DhBrandB}"/>
    <Setter Property="Padding" Value="16,0,16,0"/>
    <Setter Property="MinHeight" Value="30"/>
    <Setter Property="Cursor" Value="Hand"/>
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="Button">
          <Border x:Name="bd" CornerRadius="4" SnapsToDevicePixels="True"
                  Background="{TemplateBinding Background}">
            <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"
                              Margin="{TemplateBinding Padding}" RecognizesAccessKey="True"/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property="IsMouseOver" Value="True">
              <Setter TargetName="bd" Property="Background" Value="{StaticResource DhBrandHiB}"/>
            </Trigger>
            <Trigger Property="IsPressed" Value="True">
              <Setter TargetName="bd" Property="Opacity" Value="0.82"/>
            </Trigger>
            <Trigger Property="IsEnabled" Value="False">
              <Setter TargetName="bd" Property="Background" Value="{StaticResource DhLineB}"/>
              <Setter Property="Foreground" Value="{StaticResource DhSubB}"/>
              <Setter Property="Cursor" Value="Arrow"/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <!-- 콤보: 펼침단추 + 목록 -->
  <ControlTemplate x:Key="DhComboToggle" TargetType="ToggleButton">
    <Border x:Name="bd" CornerRadius="4" SnapsToDevicePixels="True"
            Background="{StaticResource DhCardB}"
            BorderBrush="{StaticResource DhLineB}" BorderThickness="1">
      <Path x:Name="ar" HorizontalAlignment="Right" VerticalAlignment="Center"
            Margin="0,0,11,0" Data="M0,0 L4.5,4.5 L9,0"
            Stroke="{StaticResource DhSubB}" StrokeThickness="1.6"
            StrokeStartLineCap="Round" StrokeEndLineCap="Round"/>
    </Border>
    <ControlTemplate.Triggers>
      <Trigger Property="IsMouseOver" Value="True">
        <Setter TargetName="bd" Property="BorderBrush" Value="{StaticResource DhBrandHiB}"/>
        <Setter TargetName="ar" Property="Stroke" Value="{StaticResource DhBrandB}"/>
      </Trigger>
      <Trigger Property="IsChecked" Value="True">
        <Setter TargetName="bd" Property="BorderBrush" Value="{StaticResource DhBrandB}"/>
        <Setter TargetName="ar" Property="Stroke" Value="{StaticResource DhBrandB}"/>
      </Trigger>
      <Trigger Property="IsEnabled" Value="False">
        <Setter TargetName="bd" Property="Background" Value="{StaticResource DhWallB}"/>
      </Trigger>
    </ControlTemplate.Triggers>
  </ControlTemplate>

  <Style TargetType="ComboBox">
    <Setter Property="FontFamily" Value="Malgun Gothic"/>
    <Setter Property="FontSize" Value="12"/>
    <Setter Property="Foreground" Value="{StaticResource DhInkB}"/>
    <Setter Property="MinHeight" Value="28"/>
    <Setter Property="Padding" Value="9,0,28,0"/>
    <Setter Property="Cursor" Value="Hand"/>
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="ComboBox">
          <Grid>
            <ToggleButton Template="{StaticResource DhComboToggle}"
                          Focusable="False" ClickMode="Press"
                          IsEnabled="{TemplateBinding IsEnabled}"
                          IsChecked="{Binding IsDropDownOpen, Mode=TwoWay,
                                      RelativeSource={RelativeSource TemplatedParent}}"/>
            <ContentPresenter Margin="{TemplateBinding Padding}"
                              VerticalAlignment="Center" HorizontalAlignment="Left"
                              IsHitTestVisible="False"
                              Content="{TemplateBinding SelectionBoxItem}"
                              ContentTemplate="{TemplateBinding SelectionBoxItemTemplate}"
                              ContentStringFormat="{TemplateBinding SelectionBoxItemStringFormat}"/>
            <Popup x:Name="PART_Popup" Placement="Bottom" Focusable="False"
                   AllowsTransparency="True" PopupAnimation="Fade"
                   MinWidth="{TemplateBinding ActualWidth}"
                   IsOpen="{TemplateBinding IsDropDownOpen}">
              <Border Background="{StaticResource DhCardB}"
                      BorderBrush="{StaticResource DhLineB}" BorderThickness="1"
                      CornerRadius="4" Margin="0,3,0,10" Padding="0,4,0,4"
                      MaxHeight="{TemplateBinding MaxDropDownHeight}">
                <Border.Effect>
                  <DropShadowEffect BlurRadius="14" ShadowDepth="2" Opacity="0.2" Color="#00484E"/>
                </Border.Effect>
                <ScrollViewer><ItemsPresenter/></ScrollViewer>
              </Border>
            </Popup>
          </Grid>
          <ControlTemplate.Triggers>
            <Trigger Property="IsEnabled" Value="False">
              <Setter Property="Foreground" Value="{StaticResource DhSubB}"/>
              <Setter Property="Cursor" Value="Arrow"/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style TargetType="ComboBoxItem">
    <Setter Property="FontFamily" Value="Malgun Gothic"/>
    <Setter Property="FontSize" Value="12"/>
    <Setter Property="Foreground" Value="{StaticResource DhInkB}"/>
    <Setter Property="Padding" Value="9,6,9,6"/>
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="ComboBoxItem">
          <Border x:Name="bd" Background="Transparent" CornerRadius="3"
                  Margin="4,0,4,0" Padding="{TemplateBinding Padding}">
            <ContentPresenter/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property="IsHighlighted" Value="True">
              <Setter TargetName="bd" Property="Background" Value="{StaticResource DhBrandDimB}"/>
            </Trigger>
            <Trigger Property="IsSelected" Value="True">
              <Setter TargetName="bd" Property="Background" Value="{StaticResource DhBrandDimB}"/>
              <Setter Property="FontWeight" Value="SemiBold"/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <!-- 옵션단추(동그라미) -->
  <Style TargetType="RadioButton">
    <Setter Property="FontFamily" Value="Malgun Gothic"/>
    <Setter Property="FontSize" Value="12"/>
    <Setter Property="Foreground" Value="{StaticResource DhInkB}"/>
    <Setter Property="Cursor" Value="Hand"/>
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="RadioButton">
          <StackPanel Orientation="Horizontal" Background="Transparent">
            <Grid Width="16" Height="16" VerticalAlignment="Center">
              <Ellipse x:Name="ring" StrokeThickness="1.5"
                       Stroke="{StaticResource DhLineB}" Fill="{StaticResource DhCardB}"/>
              <Ellipse x:Name="dot" Width="8" Height="8" Visibility="Collapsed"
                       Fill="{StaticResource DhBrandB}"/>
            </Grid>
            <ContentPresenter Margin="7,0,0,0" VerticalAlignment="Center" RecognizesAccessKey="True"/>
          </StackPanel>
          <ControlTemplate.Triggers>
            <Trigger Property="IsChecked" Value="True">
              <Setter TargetName="dot" Property="Visibility" Value="Visible"/>
              <Setter TargetName="ring" Property="Stroke" Value="{StaticResource DhBrandB}"/>
            </Trigger>
            <Trigger Property="IsMouseOver" Value="True">
              <Setter TargetName="ring" Property="Stroke" Value="{StaticResource DhBrandHiB}"/>
            </Trigger>
            <Trigger Property="IsEnabled" Value="False">
              <Setter Property="Foreground" Value="{StaticResource DhSubB}"/>
              <Setter TargetName="dot" Property="Fill" Value="{StaticResource DhSubB}"/>
              <Setter Property="Cursor" Value="Arrow"/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <!-- 체크상자 -->
  <Style TargetType="CheckBox">
    <Setter Property="FontFamily" Value="Malgun Gothic"/>
    <Setter Property="FontSize" Value="12"/>
    <Setter Property="Foreground" Value="{StaticResource DhInkB}"/>
    <Setter Property="Cursor" Value="Hand"/>
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="CheckBox">
          <StackPanel Orientation="Horizontal" Background="Transparent">
            <Border x:Name="box" Width="16" Height="16" CornerRadius="3.5"
                    VerticalAlignment="Center" BorderThickness="1.5"
                    BorderBrush="{StaticResource DhLineB}" Background="{StaticResource DhCardB}">
              <Path x:Name="tick" Data="M0,4 L3.2,7.2 L9,1" Visibility="Collapsed"
                    Stroke="White" StrokeThickness="1.8"
                    StrokeStartLineCap="Round" StrokeEndLineCap="Round" StrokeLineJoin="Round"
                    HorizontalAlignment="Center" VerticalAlignment="Center"/>
            </Border>
            <ContentPresenter Margin="7,0,0,0" VerticalAlignment="Center" RecognizesAccessKey="True"/>
          </StackPanel>
          <ControlTemplate.Triggers>
            <Trigger Property="IsChecked" Value="True">
              <Setter TargetName="box" Property="Background" Value="{StaticResource DhBrandB}"/>
              <Setter TargetName="box" Property="BorderBrush" Value="{StaticResource DhBrandB}"/>
              <Setter TargetName="tick" Property="Visibility" Value="Visible"/>
            </Trigger>
            <Trigger Property="IsMouseOver" Value="True">
              <Setter TargetName="box" Property="BorderBrush" Value="{StaticResource DhBrandHiB}"/>
            </Trigger>
            <Trigger Property="IsEnabled" Value="False">
              <Setter Property="Foreground" Value="{StaticResource DhSubB}"/>
              <Setter TargetName="box" Property="Background" Value="{StaticResource DhWallB}"/>
              <Setter Property="Cursor" Value="Arrow"/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <!-- 밀대(슬라이더) -->
  <Style x:Key="DhSliderThumb" TargetType="Thumb">
    <Setter Property="Width" Value="15"/>
    <Setter Property="Height" Value="15"/>
    <Setter Property="Cursor" Value="Hand"/>
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="Thumb">
          <Grid>
            <Ellipse Fill="{StaticResource DhBrandB}"/>
            <Ellipse Margin="4" Fill="White"/>
          </Grid>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style x:Key="DhSliderBlank" TargetType="RepeatButton">
    <Setter Property="Focusable" Value="False"/>
    <Setter Property="IsTabStop" Value="False"/>
    <Setter Property="Background" Value="Transparent"/>
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="RepeatButton">
          <Border Background="Transparent"/>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style TargetType="Slider">
    <Setter Property="MinHeight" Value="26"/>
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="Slider">
          <Grid VerticalAlignment="Center">
            <Border Height="4" CornerRadius="2" VerticalAlignment="Center"
                    Background="{StaticResource DhLineB}"/>
            <TickBar x:Name="tb" Height="4" VerticalAlignment="Bottom" Margin="0,0,0,-7"
                     Placement="Bottom" Visibility="Collapsed"
                     Fill="{StaticResource DhLineB}"
                     Ticks="{TemplateBinding Ticks}"
                     Minimum="{TemplateBinding Minimum}"
                     Maximum="{TemplateBinding Maximum}"
                     TickFrequency="{TemplateBinding TickFrequency}"/>
            <Track x:Name="PART_Track">
              <Track.DecreaseRepeatButton>
                <RepeatButton Style="{StaticResource DhSliderBlank}"
                              Command="Slider.DecreaseLarge"/>
              </Track.DecreaseRepeatButton>
              <Track.IncreaseRepeatButton>
                <RepeatButton Style="{StaticResource DhSliderBlank}"
                              Command="Slider.IncreaseLarge"/>
              </Track.IncreaseRepeatButton>
              <Track.Thumb>
                <Thumb Style="{StaticResource DhSliderThumb}"/>
              </Track.Thumb>
            </Track>
          </Grid>
          <ControlTemplate.Triggers>
            <Trigger Property="TickPlacement" Value="BottomRight">
              <Setter TargetName="tb" Property="Visibility" Value="Visible"/>
            </Trigger>
            <Trigger Property="IsEnabled" Value="False">
              <Setter Property="Opacity" Value="0.5"/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <!-- 도움말 풍선 -->
  <Style TargetType="ToolTip">
    <Setter Property="FontFamily" Value="Malgun Gothic"/>
    <Setter Property="FontSize" Value="11.5"/>
    <Setter Property="Foreground" Value="{StaticResource DhInkB}"/>
    <Setter Property="MaxWidth" Value="420"/>
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="ToolTip">
          <Border Background="{StaticResource DhCardB}" CornerRadius="5"
                  BorderBrush="{StaticResource DhLineB}" BorderThickness="1"
                  Padding="10,7,10,8">
            <Border.Effect>
              <DropShadowEffect BlurRadius="12" ShadowDepth="2" Opacity="0.18" Color="#00484E"/>
            </Border.Effect>
            <!-- ★[회귀검토 0909] <b>줄바꿈을 걸어야 한다.</b> MaxWidth 420만 주고 ContentPresenter를 쓰면
                 그 안 TextBlock이 NoWrap이라 <b>긴 글이 줄바꿈 없이 잘린다</b>(11.5px 한글 약 36자).
                 종전(기본 스타일)에는 MaxWidth가 없어 옆으로 길게 늘어나 전부 보였다 — 내가 줄인 것이다.
                 ★ContentStringFormat까지 넘겨야 숫자 서식이 안 깨진다. -->
            <TextBlock Text="{TemplateBinding Content}" TextWrapping="Wrap"/>
          </Border>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

</ResourceDictionary>
""";
}
