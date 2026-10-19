using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Markup;
using System.Windows.Media;

namespace DH.Takeoff.Revit;

/// <summary>
/// 팝업의 모양 — 색과, 단추 · 입력칸 · 고르는 칸 · 목록 줄 · 굴림 막대 · 풍선 글의 틀(JACK 2026-10-13: "너무 옛날 테마").
/// WPF 가 기본으로 주는 틀(회색 네모 단추 · 옛 굴림 막대)을 쓰지 않고, 평평하고 모서리가 둥근 틀을 직접 준다.
/// 색은 산출식 검토 창(WizardView)과 같은 값이다 — 두 창이 한 프로그램으로 보이게(WizardView 를 다음에 고칠 때 이쪽으로 합친다).
/// Revit API 를 쓰지 않는다(tools/uipreview 가 그림으로 뽑고, 진짜 창에 마우스 신호를 넣어 눌러 본다).
/// </summary>
public sealed class UiTheme
{
    public static readonly FontFamily UiFont = new("Segoe UI, Malgun Gothic");
    public static readonly FontFamily IconFont = new("Segoe MDL2 Assets");     // 윈도우 10·11 기본 아이콘 글꼴

    // 아이콘(Segoe MDL2 Assets)
    public const string IconUndo = "", IconEdit = "", IconAdd = "", IconCheck = "", IconClose = "",
                        IconUp = "", IconDown = "", IconNext = "", IconFit = "", IconHide = "", IconShow = "", IconWarn = "";

    private static readonly Dictionary<string, string> LightColors = new()
    {
        ["Bg"] = "#F3F4F6", ["Card"] = "#FFFFFF", ["Line"] = "#E2E5EA", ["Text"] = "#1B1F24", ["Sub"] = "#667085",
        ["Accent"] = "#2563EB", ["AccentHover"] = "#1D4FD7", ["AccentPress"] = "#1A43B8", ["OnAccent"] = "#FFFFFF",
        ["AccentSoft"] = "#E8F0FE", ["AccentSoftHover"] = "#DAE6FD", ["AccentText"] = "#1D4FD7",
        ["Hover"] = "#F2F4F7", ["Press"] = "#E4E7EC", ["Key"] = "#EEF1F5", ["KeyHover"] = "#E1E6ED", ["KeyPress"] = "#D3DAE3",
        ["InputBg"] = "#FFFFFF", ["InputLine"] = "#CBD2DB", ["InputHover"] = "#98A2B3", ["Track"] = "#E2E5EA",
        ["Good"] = "#22A55B", ["GoodBg"] = "#E7F6EC", ["GoodFg"] = "#0E7A3C", ["BadBg"] = "#FDECEC", ["BadHover"] = "#FAD9D5", ["BadFg"] = "#B42318",
        ["WarnBg"] = "#FFF4D6", ["WarnFg"] = "#8A5A00", ["NeutralBg"] = "#EEF1F5", ["NeutralFg"] = "#475467",
        ["Thumb"] = "#C5CBD5", ["ThumbHover"] = "#98A2B3", ["TipBg"] = "#1F2937", ["TipFg"] = "#F9FAFB",
        ["Glass"] = "#F2FFFFFF", ["None"] = "#00FFFFFF",
    };

    private static readonly Dictionary<string, string> DarkColors = new()
    {
        ["Bg"] = "#1E2126", ["Card"] = "#272B32", ["Line"] = "#3A404A", ["Text"] = "#E7EAEE", ["Sub"] = "#9AA4B2",
        ["Accent"] = "#4C8DF6", ["AccentHover"] = "#6AA1F8", ["AccentPress"] = "#3B78DD", ["OnAccent"] = "#FFFFFF",
        ["AccentSoft"] = "#23324E", ["AccentSoftHover"] = "#2B3D60", ["AccentText"] = "#A9C8FC",
        ["Hover"] = "#313640", ["Press"] = "#3A404A", ["Key"] = "#313640", ["KeyHover"] = "#3B424D", ["KeyPress"] = "#464E5B",
        ["InputBg"] = "#1B1E23", ["InputLine"] = "#4A515C", ["InputHover"] = "#6B7482", ["Track"] = "#3A404A",
        ["Good"] = "#3CCB7F", ["GoodBg"] = "#173524", ["GoodFg"] = "#5FD38D", ["BadBg"] = "#3B1D1D", ["BadHover"] = "#4D2626", ["BadFg"] = "#FF8A80",
        ["WarnBg"] = "#3A2E12", ["WarnFg"] = "#F5C451", ["NeutralBg"] = "#313640", ["NeutralFg"] = "#B6BEC9",
        ["Thumb"] = "#4A515C", ["ThumbHover"] = "#6B7482", ["TipBg"] = "#3A404A", ["TipFg"] = "#E7EAEE",
        ["Glass"] = "#F2272B32", ["None"] = "#00FFFFFF",
    };

    public bool IsDark { get; }
    public ResourceDictionary Resources { get; }

    public UiTheme(bool dark)
    {
        IsDark = dark;
        var xaml = new StringBuilder();
        xaml.Append("<ResourceDictionary xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" ")
            .Append("xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">");
        foreach (var color in dark ? DarkColors : LightColors)
            xaml.Append($"<SolidColorBrush x:Key=\"{color.Key}\" Color=\"{color.Value}\"/>");
        xaml.Append(ButtonStyle("BtnSecondary", "Card", "Hover", "Press", "Text", "InputLine"));
        xaml.Append(ButtonStyle("BtnPrimary", "Accent", "AccentHover", "AccentPress", "OnAccent", "Accent"));
        xaml.Append(ButtonStyle("BtnSoft", "Key", "KeyHover", "KeyPress", "Text", "Key"));
        xaml.Append(ButtonStyle("BtnGhost", "None", "Hover", "Press", "Text", "None"));
        xaml.Append(ButtonStyle("BtnSeg", "None", "Card", "Press", "Text", "None", radius: 5));
        xaml.Append(ButtonStyle("BtnChip", "NeutralBg", "AccentSoft", "AccentSoftHover", "NeutralFg", "None", radius: 10));
        xaml.Append(ButtonStyle("BtnDanger", "BadBg", "BadHover", "BadHover", "BadFg", "BadFg"));
        xaml.Append(Styles);
        xaml.Append("</ResourceDictionary>");
        Resources = (ResourceDictionary)XamlReader.Parse(xaml.ToString());
    }

    /// <summary>색.</summary>
    public Brush this[string key] => (Brush)Resources[key];

    /// <summary>틀.</summary>
    public Style StyleOf(string key) => (Style)Resources[key];

    // ── 부품 ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// 단추. kind = BtnSecondary(테두리) · BtnPrimary(강조색) · BtnSoft(옅은 바탕) · BtnGhost(바탕 없음) · BtnChip(둥근 딱지) · BtnDanger(지우기) ·
    /// BtnPick(고른 부재에 넣는 단추 — Tag 가 "on" 이면 고른 부재가 모두 그 값, "some" 이면 일부만).
    /// </summary>
    public Button Button(string text, string kind = "BtnSecondary", string tip = "", string id = "", string icon = "")
    {
        var button = new Button { Style = StyleOf(kind), Content = icon.Length > 0 ? WithIcon(icon, text) : text };
        Mark(button, tip, id);
        return button;
    }

    /// <summary>켜고 끄는 단추(켜지면 강조색 바탕).</summary>
    public ToggleButton Toggle(string text, string tip = "", string id = "", string icon = "")
    {
        var toggle = new ToggleButton { Style = StyleOf("Toggle"), Content = icon.Length > 0 ? WithIcon(icon, text) : text };
        Mark(toggle, tip, id);
        return toggle;
    }

    /// <summary>
    /// 여럿 가운데 하나를 고르는 칸의 한 조각(Segments 로 묶는다). 묶음 이름(GroupName)은 주지 않는다 — 같은 부모 안의 조각끼리 묶이고,
    /// 이름을 주면 WPF 가 그 이름을 프로그램이 끝날 때까지 표에 들고 있다.
    /// </summary>
    public RadioButton Segment(string text, string tip = "", string id = "")
    {
        var segment = new RadioButton { Style = StyleOf("Seg"), Content = text };
        Mark(segment, tip, id);
        return segment;
    }

    /// <summary>조각들을 한 줄로 묶는 옅은 바탕(안에 둥근 조각이 나란히 놓인다).</summary>
    public Border Segments(params UIElement[] items)
    {
        var line = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var item in items) line.Children.Add(item);
        return new Border { Background = this["Key"], CornerRadius = new CornerRadius(7), Padding = new Thickness(2), Child = line, VerticalAlignment = VerticalAlignment.Center };
    }

    /// <summary>글 적는 칸. hint = 비어 있을 때 흐리게 보이는 안내 글.</summary>
    public TextBox Input(string hint = "", string tip = "", string id = "")
    {
        var box = new TextBox { Style = StyleOf("Input"), Tag = hint };
        Mark(box, tip, id);
        return box;
    }

    /// <summary>펼쳐서 고르는 칸.</summary>
    public ComboBox Combo(string tip = "", string id = "")
    {
        var combo = new ComboBox { Style = StyleOf("Combo") };
        Mark(combo, tip, id);
        return combo;
    }

    /// <summary>적거나 펼쳐서 고르는 칸(글 칸 + 목록). hint = 비어 있을 때 흐리게 보이는 안내 글. 적는 대로 이름을 채워 넣지 않는다(IsTextSearchEnabled 끔).</summary>
    public ComboBox ComboEdit(string hint = "", string tip = "", string id = "")
    {
        var combo = new ComboBox { Style = StyleOf("ComboEdit"), Tag = hint };
        Mark(combo, tip, id);
        return combo;
    }

    /// <summary>체크 칸.</summary>
    public CheckBox Check(string text, bool on, string tip = "", string id = "")
    {
        var check = new CheckBox { Style = StyleOf("Check"), Content = text, IsChecked = on };
        Mark(check, tip, id);
        return check;
    }

    /// <summary>아이콘 글자 하나.</summary>
    public static TextBlock Icon(string glyph, double size = 12) =>
        new() { Text = glyph, FontFamily = IconFont, FontSize = size, VerticalAlignment = VerticalAlignment.Center };

    private static StackPanel WithIcon(string glyph, string text)
    {
        var line = new StackPanel { Orientation = Orientation.Horizontal };
        var icon = Icon(glyph, 11);
        icon.Margin = new Thickness(0, 1, 6, 0);
        line.Children.Add(icon);
        line.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
        return line;
    }

    /// <summary>둥근 딱지(바탕색 + 글).</summary>
    public Border Pill(TextBlock text, string background = "NeutralBg")
    {
        text.VerticalAlignment = VerticalAlignment.Center;
        return new Border { Background = this[background], CornerRadius = new CornerRadius(10), Padding = new Thickness(9, 2, 9, 3), VerticalAlignment = VerticalAlignment.Center, Child = text };
    }

    /// <summary>풍선 글을 단다(길면 줄을 바꾸고, 읽을 틈을 넉넉히 준다) + 시험에서 찾는 이름을 붙인다.</summary>
    public void Mark(FrameworkElement on, string tip, string id = "")
    {
        if (id.Length > 0) AutomationProperties.SetAutomationId(on, id);
        Tip(on, tip);
    }

    /// <summary>풍선 글을 단다("" = 뗀다).</summary>
    public void Tip(FrameworkElement on, string tip)
    {
        if (tip.Length == 0) { on.ToolTip = null; return; }
        on.ToolTip = new ToolTip { Style = StyleOf("Tip"), Content = tip };
        ToolTipService.SetInitialShowDelay(on, 450);
        ToolTipService.SetShowDuration(on, 30000);
        ToolTipService.SetShowOnDisabled(on, true);
    }

    // ── 틀(XAML) ─────────────────────────────────────────────────────────────────

    // 평평하고 모서리가 둥근 단추. 마우스를 올리면 · 누르면 바탕색이 바뀐다.
    private static string ButtonStyle(string key, string bg, string hover, string press, string fg, string line, int radius = 6) => $$"""
        <Style x:Key="{{key}}" TargetType="Button">
          <Setter Property="Foreground" Value="{StaticResource {{fg}}}"/>
          <Setter Property="Background" Value="{StaticResource {{bg}}}"/>
          <Setter Property="BorderBrush" Value="{StaticResource {{line}}}"/>
          <Setter Property="BorderThickness" Value="1"/>
          <Setter Property="Padding" Value="11,0,11,1"/>
          <Setter Property="MinHeight" Value="30"/>
          <Setter Property="HorizontalContentAlignment" Value="Center"/>
          <Setter Property="VerticalContentAlignment" Value="Center"/>
          <Setter Property="VerticalAlignment" Value="Center"/>
          <Setter Property="FocusVisualStyle" Value="{x:Null}"/>
          <Setter Property="SnapsToDevicePixels" Value="True"/>
          <Setter Property="Template">
            <Setter.Value>
              <ControlTemplate TargetType="Button">
                <Border x:Name="b" Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}"
                        BorderThickness="{TemplateBinding BorderThickness}" CornerRadius="{{radius}}">
                  <ContentPresenter Margin="{TemplateBinding Padding}" RecognizesAccessKey="False"
                                    HorizontalAlignment="{TemplateBinding HorizontalContentAlignment}"
                                    VerticalAlignment="{TemplateBinding VerticalContentAlignment}"/>
                </Border>
                <ControlTemplate.Triggers>
                  <Trigger Property="IsMouseOver" Value="True">
                    <Setter TargetName="b" Property="Background" Value="{StaticResource {{hover}}}"/>
                  </Trigger>
                  <Trigger Property="IsPressed" Value="True">
                    <Setter TargetName="b" Property="Background" Value="{StaticResource {{press}}}"/>
                  </Trigger>
                  <Trigger Property="IsKeyboardFocused" Value="True">
                    <Setter TargetName="b" Property="BorderBrush" Value="{StaticResource Accent}"/>
                  </Trigger>
                  <Trigger Property="IsEnabled" Value="False">
                    <Setter Property="Opacity" Value="0.38"/>
                  </Trigger>
                </ControlTemplate.Triggers>
              </ControlTemplate>
            </Setter.Value>
          </Setter>
        </Style>
        """;

    private const string Styles = """
        <!-- 켜고 끄는 단추: 꺼짐 = 테두리 단추 · 켜짐 = 강조색 옅은 바탕 + 강조색 글 -->
        <Style x:Key="Toggle" TargetType="ToggleButton">
          <Setter Property="Foreground" Value="{StaticResource Text}"/>
          <Setter Property="Background" Value="{StaticResource Card}"/>
          <Setter Property="BorderBrush" Value="{StaticResource InputLine}"/>
          <Setter Property="BorderThickness" Value="1"/>
          <Setter Property="Padding" Value="11,0,11,1"/>
          <Setter Property="MinHeight" Value="30"/>
          <Setter Property="VerticalAlignment" Value="Center"/>
          <Setter Property="FocusVisualStyle" Value="{x:Null}"/>
          <Setter Property="SnapsToDevicePixels" Value="True"/>
          <Setter Property="Template">
            <Setter.Value>
              <ControlTemplate TargetType="ToggleButton">
                <Border x:Name="b" Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}"
                        BorderThickness="{TemplateBinding BorderThickness}" CornerRadius="6">
                  <ContentPresenter Margin="{TemplateBinding Padding}" RecognizesAccessKey="False" HorizontalAlignment="Center" VerticalAlignment="Center"/>
                </Border>
                <ControlTemplate.Triggers>
                  <Trigger Property="IsMouseOver" Value="True">
                    <Setter TargetName="b" Property="Background" Value="{StaticResource Hover}"/>
                  </Trigger>
                  <Trigger Property="IsPressed" Value="True">
                    <Setter TargetName="b" Property="Background" Value="{StaticResource Press}"/>
                  </Trigger>
                  <Trigger Property="IsChecked" Value="True">
                    <Setter TargetName="b" Property="Background" Value="{StaticResource AccentSoft}"/>
                    <Setter TargetName="b" Property="BorderBrush" Value="{StaticResource Accent}"/>
                    <Setter Property="Foreground" Value="{StaticResource AccentText}"/>
                  </Trigger>
                  <MultiTrigger>
                    <MultiTrigger.Conditions>
                      <Condition Property="IsChecked" Value="True"/>
                      <Condition Property="IsMouseOver" Value="True"/>
                    </MultiTrigger.Conditions>
                    <Setter TargetName="b" Property="Background" Value="{StaticResource AccentSoftHover}"/>
                  </MultiTrigger>
                  <Trigger Property="IsEnabled" Value="False">
                    <Setter Property="Opacity" Value="0.38"/>
                  </Trigger>
                </ControlTemplate.Triggers>
              </ControlTemplate>
            </Setter.Value>
          </Setter>
        </Style>

        <!-- 여럿 가운데 하나: 옅은 바탕 위에 놓인 둥근 조각. 고른 조각만 흰 바탕 -->
        <Style x:Key="Seg" TargetType="RadioButton">
          <Setter Property="Foreground" Value="{StaticResource Sub}"/>
          <Setter Property="Padding" Value="11,0,11,1"/>
          <Setter Property="MinHeight" Value="26"/>
          <Setter Property="FocusVisualStyle" Value="{x:Null}"/>
          <Setter Property="SnapsToDevicePixels" Value="True"/>
          <Setter Property="Template">
            <Setter.Value>
              <ControlTemplate TargetType="RadioButton">
                <Border x:Name="b" Background="Transparent" BorderBrush="Transparent" BorderThickness="1" CornerRadius="5">
                  <ContentPresenter Margin="{TemplateBinding Padding}" RecognizesAccessKey="False" HorizontalAlignment="Center" VerticalAlignment="Center"/>
                </Border>
                <ControlTemplate.Triggers>
                  <Trigger Property="IsMouseOver" Value="True">
                    <Setter Property="Foreground" Value="{StaticResource Text}"/>
                  </Trigger>
                  <Trigger Property="IsChecked" Value="True">
                    <Setter TargetName="b" Property="Background" Value="{StaticResource Card}"/>
                    <Setter TargetName="b" Property="BorderBrush" Value="{StaticResource Line}"/>
                    <Setter Property="Foreground" Value="{StaticResource Text}"/>
                    <Setter Property="FontWeight" Value="SemiBold"/>
                  </Trigger>
                  <Trigger Property="IsEnabled" Value="False">
                    <Setter Property="Opacity" Value="0.38"/>
                  </Trigger>
                </ControlTemplate.Triggers>
              </ControlTemplate>
            </Setter.Value>
          </Setter>
        </Style>

        <!-- 고른 부재에 넣는 단추: 글은 왼쪽에 맞춘다(세로로 나란히 놓여 목록처럼 읽힌다).
             Tag = "on" → 고른 부재가 모두 이 값(강조색 바탕 + 체크) · "some" → 고른 부재의 일부만 이 값(강조색 테두리).
             글자판을 가져가지 않는다(누른 뒤에도 Esc · Ctrl+Z 가 그림에서 듣게). -->
        <Style x:Key="BtnPick" TargetType="Button">
          <Setter Property="Foreground" Value="{StaticResource Text}"/>
          <Setter Property="Background" Value="{StaticResource Card}"/>
          <Setter Property="BorderBrush" Value="{StaticResource InputLine}"/>
          <Setter Property="BorderThickness" Value="1"/>
          <Setter Property="Padding" Value="9,0,8,1"/>
          <Setter Property="MinHeight" Value="28"/>
          <Setter Property="Focusable" Value="False"/>
          <Setter Property="Cursor" Value="Hand"/>
          <Setter Property="HorizontalContentAlignment" Value="Left"/>
          <Setter Property="SnapsToDevicePixels" Value="True"/>
          <Setter Property="Template">
            <Setter.Value>
              <ControlTemplate TargetType="Button">
                <Border x:Name="b" Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}"
                        BorderThickness="{TemplateBinding BorderThickness}" CornerRadius="6">
                  <DockPanel Margin="{TemplateBinding Padding}">
                    <TextBlock x:Name="mark" DockPanel.Dock="Right" Text="&#xE73E;" FontFamily="Segoe MDL2 Assets" FontSize="10" FontWeight="Normal"
                               Foreground="{StaticResource Accent}" VerticalAlignment="Center" Margin="4,1,0,0" Visibility="Collapsed"/>
                    <ContentPresenter RecognizesAccessKey="False" VerticalAlignment="Center"
                                      HorizontalAlignment="{TemplateBinding HorizontalContentAlignment}"/>
                  </DockPanel>
                </Border>
                <ControlTemplate.Triggers>
                  <Trigger Property="IsMouseOver" Value="True">
                    <Setter TargetName="b" Property="Background" Value="{StaticResource Hover}"/>
                    <Setter TargetName="b" Property="BorderBrush" Value="{StaticResource InputHover}"/>
                  </Trigger>
                  <Trigger Property="IsPressed" Value="True">
                    <Setter TargetName="b" Property="Background" Value="{StaticResource Press}"/>
                  </Trigger>
                  <Trigger Property="Tag" Value="some">
                    <Setter TargetName="b" Property="BorderBrush" Value="{StaticResource Accent}"/>
                  </Trigger>
                  <Trigger Property="Tag" Value="on">
                    <Setter TargetName="b" Property="Background" Value="{StaticResource AccentSoft}"/>
                    <Setter TargetName="b" Property="BorderBrush" Value="{StaticResource Accent}"/>
                    <Setter TargetName="mark" Property="Visibility" Value="Visible"/>
                    <Setter Property="Foreground" Value="{StaticResource AccentText}"/>
                    <Setter Property="FontWeight" Value="SemiBold"/>
                  </Trigger>
                  <MultiTrigger>
                    <MultiTrigger.Conditions>
                      <Condition Property="Tag" Value="on"/>
                      <Condition Property="IsMouseOver" Value="True"/>
                    </MultiTrigger.Conditions>
                    <Setter TargetName="b" Property="Background" Value="{StaticResource AccentSoftHover}"/>
                  </MultiTrigger>
                  <Trigger Property="IsEnabled" Value="False">
                    <Setter Property="Opacity" Value="0.38"/>
                  </Trigger>
                </ControlTemplate.Triggers>
              </ControlTemplate>
            </Setter.Value>
          </Setter>
        </Style>

        <!-- 목록의 한 줄(누르면 넣기): 바탕 없이 있다가 마우스를 올리면 옅은 바탕 -->
        <Style x:Key="RowButton" TargetType="Button">
          <Setter Property="Foreground" Value="{StaticResource Text}"/>
          <Setter Property="Focusable" Value="False"/>
          <Setter Property="Cursor" Value="Hand"/>
          <Setter Property="HorizontalContentAlignment" Value="Stretch"/>
          <Setter Property="SnapsToDevicePixels" Value="True"/>
          <Setter Property="Template">
            <Setter.Value>
              <ControlTemplate TargetType="Button">
                <Border x:Name="b" Background="Transparent" CornerRadius="6" Padding="8,0,6,0" MinHeight="28">
                  <ContentPresenter VerticalAlignment="Center" HorizontalAlignment="Stretch" RecognizesAccessKey="False"/>
                </Border>
                <ControlTemplate.Triggers>
                  <Trigger Property="IsMouseOver" Value="True">
                    <Setter TargetName="b" Property="Background" Value="{StaticResource Hover}"/>
                  </Trigger>
                  <Trigger Property="IsPressed" Value="True">
                    <Setter TargetName="b" Property="Background" Value="{StaticResource Press}"/>
                  </Trigger>
                </ControlTemplate.Triggers>
              </ControlTemplate>
            </Setter.Value>
          </Setter>
        </Style>

        <!-- 목록의 한 줄(누르면 그 줄을 본다): RowButton 과 같고, Tag = "on" 이면 지금 보고 있는 줄(강조색 바탕 + 왼쪽 띠) -->
        <Style x:Key="RowPick" TargetType="Button">
          <Setter Property="Foreground" Value="{StaticResource Text}"/>
          <Setter Property="Focusable" Value="False"/>
          <Setter Property="Cursor" Value="Hand"/>
          <Setter Property="HorizontalContentAlignment" Value="Stretch"/>
          <Setter Property="SnapsToDevicePixels" Value="True"/>
          <Setter Property="Template">
            <Setter.Value>
              <ControlTemplate TargetType="Button">
                <Border x:Name="b" Background="Transparent" BorderBrush="Transparent" BorderThickness="3,0,0,0" CornerRadius="6" Padding="5,0,6,0" MinHeight="28">
                  <ContentPresenter VerticalAlignment="Center" HorizontalAlignment="Stretch" RecognizesAccessKey="False"/>
                </Border>
                <ControlTemplate.Triggers>
                  <Trigger Property="IsMouseOver" Value="True">
                    <Setter TargetName="b" Property="Background" Value="{StaticResource Hover}"/>
                  </Trigger>
                  <Trigger Property="IsPressed" Value="True">
                    <Setter TargetName="b" Property="Background" Value="{StaticResource Press}"/>
                  </Trigger>
                  <Trigger Property="Tag" Value="on">
                    <Setter TargetName="b" Property="Background" Value="{StaticResource AccentSoft}"/>
                    <Setter TargetName="b" Property="BorderBrush" Value="{StaticResource Accent}"/>
                  </Trigger>
                  <MultiTrigger>
                    <MultiTrigger.Conditions>
                      <Condition Property="Tag" Value="on"/>
                      <Condition Property="IsMouseOver" Value="True"/>
                    </MultiTrigger.Conditions>
                    <Setter TargetName="b" Property="Background" Value="{StaticResource AccentSoftHover}"/>
                  </MultiTrigger>
                </ControlTemplate.Triggers>
              </ControlTemplate>
            </Setter.Value>
          </Setter>
        </Style>

        <!-- 목록 줄 안의 부재 이름(누르면 그 부재만 본다): 바탕 없이 있다가 마우스를 올리면 카드 바탕 + 옅은 테두리.
             Tag = "on" 이면 지금 그 부재만 보는 중 — 카드 바탕 + 그 부재의 색 테두리(BorderBrush 로 받는다). 테두리 + 안쪽 여백 = 6(부르는 쪽이 왼쪽 -6 으로 글자 자리를 맞춘다). -->
        <Style x:Key="CellPick" TargetType="Button">
          <Setter Property="Foreground" Value="{StaticResource Text}"/>
          <Setter Property="Focusable" Value="False"/>
          <Setter Property="Cursor" Value="Hand"/>
          <Setter Property="BorderBrush" Value="Transparent"/>
          <Setter Property="HorizontalAlignment" Value="Left"/>
          <Setter Property="VerticalAlignment" Value="Center"/>
          <Setter Property="SnapsToDevicePixels" Value="True"/>
          <Setter Property="Template">
            <Setter.Value>
              <ControlTemplate TargetType="Button">
                <Border x:Name="b" Background="Transparent" BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="1.5" CornerRadius="5" Padding="4.5,1,6,2">
                  <ContentPresenter VerticalAlignment="Center" HorizontalAlignment="Left" RecognizesAccessKey="False"/>
                </Border>
                <ControlTemplate.Triggers>
                  <Trigger Property="IsMouseOver" Value="True">
                    <Setter TargetName="b" Property="Background" Value="{StaticResource Card}"/>
                    <Setter TargetName="b" Property="BorderBrush" Value="{StaticResource InputLine}"/>
                  </Trigger>
                  <Trigger Property="Tag" Value="on">
                    <Setter TargetName="b" Property="Background" Value="{StaticResource Card}"/>
                    <Setter TargetName="b" Property="BorderBrush" Value="{Binding BorderBrush, RelativeSource={RelativeSource TemplatedParent}}"/>
                  </Trigger>
                </ControlTemplate.Triggers>
              </ControlTemplate>
            </Setter.Value>
          </Setter>
        </Style>

        <!-- 글 적는 칸: 모서리가 둥글고, 적는 중이면 테두리가 강조색. 비어 있으면 Tag 의 글이 흐리게 보인다.
             안쪽 여백(Padding)은 TextBox 가 스스로 PART_ContentHost 에 넘기므로 틀에서는 주지 않는다(주면 두 배가 된다). -->
        <Style x:Key="Input" TargetType="TextBox">
          <Setter Property="Foreground" Value="{StaticResource Text}"/>
          <Setter Property="Background" Value="{StaticResource InputBg}"/>
          <Setter Property="BorderBrush" Value="{StaticResource InputLine}"/>
          <Setter Property="BorderThickness" Value="1"/>
          <Setter Property="Padding" Value="7,0,7,1"/>
          <Setter Property="MinHeight" Value="30"/>
          <Setter Property="CaretBrush" Value="{StaticResource Text}"/>
          <Setter Property="SelectionBrush" Value="{StaticResource Accent}"/>
          <Setter Property="VerticalContentAlignment" Value="Center"/>
          <Setter Property="VerticalAlignment" Value="Center"/>
          <Setter Property="FocusVisualStyle" Value="{x:Null}"/>
          <Setter Property="SnapsToDevicePixels" Value="True"/>
          <Setter Property="Template">
            <Setter.Value>
              <ControlTemplate TargetType="TextBox">
                <Border x:Name="b" Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}"
                        BorderThickness="{TemplateBinding BorderThickness}" CornerRadius="6">
                  <Grid>
                    <TextBlock x:Name="hint" Text="{Binding Tag, RelativeSource={RelativeSource TemplatedParent}}" Foreground="{StaticResource Sub}" Opacity="0.8"
                               Margin="10,0,10,1" VerticalAlignment="Center" IsHitTestVisible="False" Visibility="Collapsed" TextTrimming="CharacterEllipsis"/>
                    <ScrollViewer x:Name="PART_ContentHost" Focusable="False"
                                  HorizontalScrollBarVisibility="Hidden" VerticalScrollBarVisibility="Hidden"/>
                  </Grid>
                </Border>
                <ControlTemplate.Triggers>
                  <Trigger Property="Text" Value="">
                    <Setter TargetName="hint" Property="Visibility" Value="Visible"/>
                  </Trigger>
                  <Trigger Property="IsMouseOver" Value="True">
                    <Setter TargetName="b" Property="BorderBrush" Value="{StaticResource InputHover}"/>
                  </Trigger>
                  <Trigger Property="IsKeyboardFocused" Value="True">
                    <Setter TargetName="b" Property="BorderBrush" Value="{StaticResource Accent}"/>
                  </Trigger>
                  <Trigger Property="IsEnabled" Value="False">
                    <Setter Property="Opacity" Value="0.5"/>
                  </Trigger>
                </ControlTemplate.Triggers>
              </ControlTemplate>
            </Setter.Value>
          </Setter>
        </Style>

        <!-- 체크 칸: 둥근 네모, 켜지면 강조색 바탕에 흰 체크 -->
        <Style x:Key="Check" TargetType="CheckBox">
          <Setter Property="Foreground" Value="{StaticResource Sub}"/>
          <Setter Property="Cursor" Value="Hand"/>
          <Setter Property="VerticalAlignment" Value="Center"/>
          <Setter Property="FocusVisualStyle" Value="{x:Null}"/>
          <Setter Property="SnapsToDevicePixels" Value="True"/>
          <Setter Property="Template">
            <Setter.Value>
              <ControlTemplate TargetType="CheckBox">
                <StackPanel Orientation="Horizontal" Background="Transparent">
                  <Border x:Name="box" Width="16" Height="16" CornerRadius="4" Background="{StaticResource InputBg}"
                          BorderBrush="{StaticResource InputLine}" BorderThickness="1" VerticalAlignment="Center">
                    <TextBlock x:Name="mark" Text="&#xE73E;" FontFamily="Segoe MDL2 Assets" FontSize="10" Foreground="{StaticResource OnAccent}"
                               HorizontalAlignment="Center" VerticalAlignment="Center" Visibility="Collapsed"/>
                  </Border>
                  <ContentPresenter Margin="6,0,0,1" VerticalAlignment="Center" RecognizesAccessKey="False"/>
                </StackPanel>
                <ControlTemplate.Triggers>
                  <Trigger Property="IsMouseOver" Value="True">
                    <Setter TargetName="box" Property="BorderBrush" Value="{StaticResource InputHover}"/>
                  </Trigger>
                  <Trigger Property="IsChecked" Value="True">
                    <Setter TargetName="box" Property="Background" Value="{StaticResource Accent}"/>
                    <Setter TargetName="box" Property="BorderBrush" Value="{StaticResource Accent}"/>
                    <Setter TargetName="mark" Property="Visibility" Value="Visible"/>
                  </Trigger>
                  <Trigger Property="IsEnabled" Value="False">
                    <Setter Property="Opacity" Value="0.38"/>
                  </Trigger>
                </ControlTemplate.Triggers>
              </ControlTemplate>
            </Setter.Value>
          </Setter>
        </Style>

        <!-- 펼쳐서 고르는 칸의 한 줄 -->
        <Style x:Key="ComboItem" TargetType="ComboBoxItem">
          <Setter Property="Foreground" Value="{StaticResource Text}"/>
          <Setter Property="Padding" Value="10,6,10,7"/>
          <Setter Property="FocusVisualStyle" Value="{x:Null}"/>
          <Setter Property="Template">
            <Setter.Value>
              <ControlTemplate TargetType="ComboBoxItem">
                <Border x:Name="b" Background="Transparent" CornerRadius="5" Padding="{TemplateBinding Padding}">
                  <ContentPresenter/>
                </Border>
                <ControlTemplate.Triggers>
                  <Trigger Property="IsSelected" Value="True">
                    <Setter TargetName="b" Property="Background" Value="{StaticResource AccentSoft}"/>
                    <Setter Property="Foreground" Value="{StaticResource AccentText}"/>
                  </Trigger>
                  <Trigger Property="IsHighlighted" Value="True">
                    <Setter TargetName="b" Property="Background" Value="{StaticResource Hover}"/>
                  </Trigger>
                  <MultiTrigger>
                    <MultiTrigger.Conditions>
                      <Condition Property="IsSelected" Value="True"/>
                      <Condition Property="IsHighlighted" Value="True"/>
                    </MultiTrigger.Conditions>
                    <Setter TargetName="b" Property="Background" Value="{StaticResource AccentSoftHover}"/>
                  </MultiTrigger>
                </ControlTemplate.Triggers>
              </ControlTemplate>
            </Setter.Value>
          </Setter>
        </Style>

        <!-- 펼쳐서 고르는 칸의 얼굴(누르면 펼친다): 테두리 + 오른쪽의 꺾쇠 -->
        <Style x:Key="ComboFace" TargetType="ToggleButton">
          <Setter Property="Focusable" Value="False"/>
          <Setter Property="ClickMode" Value="Press"/>
          <Setter Property="Template">
            <Setter.Value>
              <ControlTemplate TargetType="ToggleButton">
                <Border x:Name="b" Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="1" CornerRadius="6">
                  <TextBlock Text="&#xE70D;" FontFamily="Segoe MDL2 Assets" FontSize="9" Foreground="{StaticResource Sub}"
                             HorizontalAlignment="Right" VerticalAlignment="Center" Margin="0,1,10,0"/>
                </Border>
                <ControlTemplate.Triggers>
                  <Trigger Property="IsMouseOver" Value="True">
                    <Setter TargetName="b" Property="BorderBrush" Value="{StaticResource InputHover}"/>
                  </Trigger>
                  <Trigger Property="IsChecked" Value="True">
                    <Setter TargetName="b" Property="BorderBrush" Value="{StaticResource Accent}"/>
                  </Trigger>
                </ControlTemplate.Triggers>
              </ControlTemplate>
            </Setter.Value>
          </Setter>
        </Style>

        <!-- 펼쳐서 고르는 칸: 펼친 목록은 둥근 카드에 옅은 그림자(그림자는 글자가 흐려지지 않게 뒤의 판에만 준다) -->
        <Style x:Key="Combo" TargetType="ComboBox">
          <Setter Property="Foreground" Value="{StaticResource Text}"/>
          <Setter Property="Background" Value="{StaticResource InputBg}"/>
          <Setter Property="BorderBrush" Value="{StaticResource InputLine}"/>
          <Setter Property="Padding" Value="10,0,28,1"/>
          <Setter Property="MinHeight" Value="30"/>
          <Setter Property="MaxDropDownHeight" Value="320"/>
          <Setter Property="VerticalAlignment" Value="Center"/>
          <Setter Property="FocusVisualStyle" Value="{x:Null}"/>
          <Setter Property="SnapsToDevicePixels" Value="True"/>
          <Setter Property="ItemContainerStyle" Value="{StaticResource ComboItem}"/>
          <Setter Property="ScrollViewer.HorizontalScrollBarVisibility" Value="Disabled"/>
          <Setter Property="ScrollViewer.VerticalScrollBarVisibility" Value="Auto"/>
          <Setter Property="Template">
            <Setter.Value>
              <ControlTemplate TargetType="ComboBox">
                <Grid>
                  <ToggleButton Style="{StaticResource ComboFace}" Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}"
                                IsChecked="{Binding IsDropDownOpen, Mode=TwoWay, RelativeSource={RelativeSource TemplatedParent}}"/>
                  <ContentPresenter IsHitTestVisible="False" Margin="{TemplateBinding Padding}" VerticalAlignment="Center" HorizontalAlignment="Left"
                                    Content="{TemplateBinding SelectionBoxItem}" ContentTemplate="{TemplateBinding SelectionBoxItemTemplate}"/>
                  <Popup x:Name="PART_Popup" AllowsTransparency="True" Focusable="False" Placement="Bottom" HorizontalOffset="-8" PopupAnimation="Fade"
                         IsOpen="{Binding IsDropDownOpen, Mode=TwoWay, RelativeSource={RelativeSource TemplatedParent}}">
                    <Grid Margin="8,4,8,12" MinWidth="{TemplateBinding ActualWidth}" MaxHeight="{TemplateBinding MaxDropDownHeight}">
                      <Border Background="{StaticResource Card}" CornerRadius="8">
                        <Border.Effect>
                          <DropShadowEffect BlurRadius="12" ShadowDepth="3" Direction="270" Opacity="0.22"/>
                        </Border.Effect>
                      </Border>
                      <Border Background="{StaticResource Card}" BorderBrush="{StaticResource Line}" BorderThickness="1" CornerRadius="8" Padding="4">
                        <ScrollViewer>
                          <ItemsPresenter KeyboardNavigation.DirectionalNavigation="Contained"/>
                        </ScrollViewer>
                      </Border>
                    </Grid>
                  </Popup>
                </Grid>
                <ControlTemplate.Triggers>
                  <Trigger Property="IsEnabled" Value="False">
                    <Setter Property="Opacity" Value="0.45"/>
                  </Trigger>
                </ControlTemplate.Triggers>
              </ControlTemplate>
            </Setter.Value>
          </Setter>
        </Style>

        <!-- 적거나 펼쳐서 고르는 칸(IsEditable): 글 칸 + 오른쪽 꺾쇠. 비어 있으면 Tag 의 안내 글이 흐리게 보인다 -->
        <Style x:Key="ComboEdit" TargetType="ComboBox" BasedOn="{StaticResource Combo}">
          <Setter Property="IsEditable" Value="True"/>
          <Setter Property="IsTextSearchEnabled" Value="False"/>
          <Setter Property="Template">
            <Setter.Value>
              <ControlTemplate TargetType="ComboBox">
                <Grid>
                  <ToggleButton x:Name="face" Style="{StaticResource ComboFace}" Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}"
                                IsChecked="{Binding IsDropDownOpen, Mode=TwoWay, RelativeSource={RelativeSource TemplatedParent}}"/>
                  <TextBlock x:Name="hint" Text="{Binding Tag, RelativeSource={RelativeSource TemplatedParent}}" Foreground="{StaticResource Sub}" Opacity="0.8"
                             Margin="11,0,30,1" VerticalAlignment="Center" IsHitTestVisible="False" Visibility="Collapsed" TextTrimming="CharacterEllipsis"/>
                  <TextBox x:Name="PART_EditableTextBox" Margin="1,1,26,1" Padding="8,0,0,1" Background="Transparent" BorderThickness="0"
                           Foreground="{TemplateBinding Foreground}" CaretBrush="{StaticResource Text}" SelectionBrush="{StaticResource Accent}"
                           VerticalContentAlignment="Center" FocusVisualStyle="{x:Null}"
                           IsReadOnly="{Binding IsReadOnly, RelativeSource={RelativeSource TemplatedParent}}">
                    <TextBox.Template>
                      <ControlTemplate TargetType="TextBox">
                        <ScrollViewer x:Name="PART_ContentHost" Focusable="False" HorizontalScrollBarVisibility="Hidden" VerticalScrollBarVisibility="Hidden"/>
                      </ControlTemplate>
                    </TextBox.Template>
                  </TextBox>
                  <Popup x:Name="PART_Popup" AllowsTransparency="True" Focusable="False" Placement="Bottom" HorizontalOffset="-8" PopupAnimation="Fade"
                         IsOpen="{Binding IsDropDownOpen, Mode=TwoWay, RelativeSource={RelativeSource TemplatedParent}}">
                    <Grid Margin="8,4,8,12" MinWidth="{TemplateBinding ActualWidth}" MaxHeight="{TemplateBinding MaxDropDownHeight}">
                      <Border Background="{StaticResource Card}" CornerRadius="8">
                        <Border.Effect>
                          <DropShadowEffect BlurRadius="12" ShadowDepth="3" Direction="270" Opacity="0.22"/>
                        </Border.Effect>
                      </Border>
                      <Border Background="{StaticResource Card}" BorderBrush="{StaticResource Line}" BorderThickness="1" CornerRadius="8" Padding="4">
                        <ScrollViewer>
                          <ItemsPresenter KeyboardNavigation.DirectionalNavigation="Contained"/>
                        </ScrollViewer>
                      </Border>
                    </Grid>
                  </Popup>
                </Grid>
                <ControlTemplate.Triggers>
                  <Trigger Property="Text" Value="">
                    <Setter TargetName="hint" Property="Visibility" Value="Visible"/>
                  </Trigger>
                  <Trigger Property="IsKeyboardFocusWithin" Value="True">
                    <Setter TargetName="face" Property="BorderBrush" Value="{StaticResource Accent}"/>
                  </Trigger>
                  <Trigger Property="IsEnabled" Value="False">
                    <Setter Property="Opacity" Value="0.45"/>
                  </Trigger>
                </ControlTemplate.Triggers>
              </ControlTemplate>
            </Setter.Value>
          </Setter>
        </Style>

        <!-- 굴림 막대: 화살표 단추 없이 가는 둥근 손잡이만 -->
        <Style x:Key="ScrollThumb" TargetType="Thumb">
          <Setter Property="Template">
            <Setter.Value>
              <ControlTemplate TargetType="Thumb">
                <Border Background="Transparent">
                  <Border x:Name="t" Background="{StaticResource Thumb}" CornerRadius="2" Margin="3"/>
                </Border>
                <ControlTemplate.Triggers>
                  <Trigger Property="IsMouseOver" Value="True">
                    <Setter TargetName="t" Property="Background" Value="{StaticResource ThumbHover}"/>
                  </Trigger>
                  <Trigger Property="IsDragging" Value="True">
                    <Setter TargetName="t" Property="Background" Value="{StaticResource ThumbHover}"/>
                  </Trigger>
                </ControlTemplate.Triggers>
              </ControlTemplate>
            </Setter.Value>
          </Setter>
        </Style>
        <Style x:Key="ScrollPage" TargetType="RepeatButton">
          <Setter Property="Focusable" Value="False"/>
          <Setter Property="IsTabStop" Value="False"/>
          <Setter Property="Template">
            <Setter.Value>
              <ControlTemplate TargetType="RepeatButton">
                <Border Background="Transparent"/>
              </ControlTemplate>
            </Setter.Value>
          </Setter>
        </Style>
        <Style TargetType="ScrollBar">
          <Setter Property="Background" Value="Transparent"/>
          <Setter Property="Width" Value="10"/>
          <Setter Property="MinWidth" Value="10"/>
          <Setter Property="Template">
            <Setter.Value>
              <ControlTemplate TargetType="ScrollBar">
                <Grid Background="{TemplateBinding Background}">
                  <Track x:Name="PART_Track" IsDirectionReversed="True">
                    <Track.DecreaseRepeatButton>
                      <RepeatButton Style="{StaticResource ScrollPage}" Command="ScrollBar.PageUpCommand"/>
                    </Track.DecreaseRepeatButton>
                    <Track.IncreaseRepeatButton>
                      <RepeatButton Style="{StaticResource ScrollPage}" Command="ScrollBar.PageDownCommand"/>
                    </Track.IncreaseRepeatButton>
                    <Track.Thumb>
                      <Thumb Style="{StaticResource ScrollThumb}"/>
                    </Track.Thumb>
                  </Track>
                </Grid>
              </ControlTemplate>
            </Setter.Value>
          </Setter>
          <Style.Triggers>
            <Trigger Property="Orientation" Value="Horizontal">
              <Setter Property="Width" Value="Auto"/>
              <Setter Property="MinWidth" Value="0"/>
              <Setter Property="Height" Value="10"/>
              <Setter Property="MinHeight" Value="10"/>
              <Setter Property="Template">
                <Setter.Value>
                  <ControlTemplate TargetType="ScrollBar">
                    <Grid Background="{TemplateBinding Background}">
                      <Track x:Name="PART_Track">
                        <Track.DecreaseRepeatButton>
                          <RepeatButton Style="{StaticResource ScrollPage}" Command="ScrollBar.PageLeftCommand"/>
                        </Track.DecreaseRepeatButton>
                        <Track.IncreaseRepeatButton>
                          <RepeatButton Style="{StaticResource ScrollPage}" Command="ScrollBar.PageRightCommand"/>
                        </Track.IncreaseRepeatButton>
                        <Track.Thumb>
                          <Thumb Style="{StaticResource ScrollThumb}"/>
                        </Track.Thumb>
                      </Track>
                    </Grid>
                  </ControlTemplate>
                </Setter.Value>
              </Setter>
            </Trigger>
          </Style.Triggers>
        </Style>

        <!-- 풍선 글: 짙은 둥근 판, 길면 줄을 바꾼다(기본 풍선은 한 줄로 길게 늘어진다) -->
        <Style x:Key="Tip" TargetType="ToolTip">
          <Setter Property="OverridesDefaultStyle" Value="True"/>
          <Setter Property="HasDropShadow" Value="False"/>
          <Setter Property="FontFamily" Value="Segoe UI, Malgun Gothic"/>
          <Setter Property="FontSize" Value="12.5"/>
          <Setter Property="Foreground" Value="{StaticResource TipFg}"/>
          <Setter Property="Template">
            <Setter.Value>
              <ControlTemplate TargetType="ToolTip">
                <Grid Margin="6,4,6,10">
                  <Border Background="{StaticResource TipBg}" CornerRadius="6">
                    <Border.Effect>
                      <DropShadowEffect BlurRadius="10" ShadowDepth="2" Direction="270" Opacity="0.25"/>
                    </Border.Effect>
                  </Border>
                  <Border Background="{StaticResource TipBg}" CornerRadius="6" Padding="10,7,10,8">
                    <TextBlock Text="{Binding Content, RelativeSource={RelativeSource TemplatedParent}}" TextWrapping="Wrap" MaxWidth="360" LineHeight="18"/>
                  </Border>
                </Grid>
              </ControlTemplate>
            </Setter.Value>
          </Setter>
        </Style>
        """;
}
