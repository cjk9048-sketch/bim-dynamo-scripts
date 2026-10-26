using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using DH.Takeoff.Core;
using DH.Takeoff.Core.Formwork;

namespace DH.Takeoff.Revit;

// R61c — ③ 3D 아래의 식(JACK 2026-10-26 «수량목록에서 선택했을 때 3D 뷰 아래 해당 수식을 전부 보이게 · 치수에 마우스를 갖다 대면 해당 수식에서 그 치수값이
// 들어간 부분만 색상을 바꾸든지»): 고른 산근 줄의 식 전부(목록 줄은 길면 줄임) · 3D 숫자에 마우스를 올리면 식에서 같은 숫자를 주황으로.
public sealed partial class PlacePartView
{
    private Border? _rvFormulaBar;
    private TextBlock? _rvFormulaText;
    private (string head, string formula, string tail)? _rvFormula;
    private string? _rvFormulaMark;          // 지금 칠한 숫자
    private readonly List<Run> _rvFormulaNumbers = new();     // 식의 숫자 글(칠할 때 다시 짓지 않는다 — 마우스 밑의 글을 바꾸면 들고 남이 되풀이된다)
    private string? _rvFormulaHover;         // 식 띠에서 마우스를 올린 숫자(R63)
    private static readonly Regex Number = new(@"\d+(?:\.\d+)?", RegexOptions.CultureInvariant);

    // ③ 의 아래: 식 띠(줄을 골랐을 때만) + ② 와 같은 색 풀이 · 마우스 안내.
    private FrameworkElement BuildReviewBottom()
    {
        var stack = new StackPanel();
        _rvFormulaText = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 13, Foreground = _text };
        _rvFormulaBar = new Border
        {
            Background = _ui["InputBg"], BorderBrush = _ui["InputLine"], BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 6, 10, 6), Margin = new Thickness(2, 8, 2, 0), Child = _rvFormulaText, Visibility = Visibility.Collapsed,
        };
        _ui.Mark(_rvFormulaBar, "고른 줄의 식 — 3D 숫자에 마우스를 올리면 식의 그 숫자가, 식의 숫자에 올리면 3D 의 그 숫자들이 칠해집니다.", "rv.formula");
        stack.Children.Add(_rvFormulaBar);
        stack.Children.Add(BuildFormworkBottom("굴림 = 확대 · 가운데 단추 끌기 = 옮기기 · 오른쪽 단추 끌기 = 돌리기 · 줄을 누르면 그 부재 · 면 · 판을 찍으면 그 줄 · Esc = 풀기"));
        if (!_rvFormulaHooked)
        {
            _view3d.LabelHovered += text => { if (Step == 2) MarkReviewFormula(text); };
            _rvFormulaHooked = true;
        }
        return stack;
    }

    private bool _rvFormulaHooked;

    // 고른 줄의 식을 띠에(없으면 숨김): «부호 (괄호) : 식 × n = 값» — 목록 줄과 같은 글, 줄이지 않고 전부.
    private void ShowReviewFormula()
    {
        _rvFormula = null;
        _rvFormulaMark = null;
        HoverFormulaNumber(null);
        if (_rv != null && _rvPicked >= 0 && _rvPicked < _rvViews.Count && _rvViews[_rvPicked].item is int item && item >= 0
            && _rv.Items[item].Item is Sheet2D.Line line)
        {
            string head = line.Mark.Length > 0 ? line.Mark + (line.Tag.Length > 0 ? " " + line.Tag : "") + " : " : "";
            string formula = FormworkLabels.Pretty(line.Formula) + (line.Count > 1 ? $" × {line.Count}" : "");
            _rvFormula = (head, formula, $" = {M2(Sheet2D.LineValue(line))}");
        }
        RenderReviewFormula();
    }

    // 3D 숫자에 마우스를 올렸다(내리면 null): 식에서 같은 숫자(낱말 단위 — 15.6 의 5.6 은 아님)를 칠한다. 각도는 «°» 를 떼고 · 식 딱지(숫자 하나가 아님)는 칠하지 않는다.
    private void MarkReviewFormula(string? text)
    {
        string? mark = text?.TrimEnd('°');
        if (mark != null && !Number.IsMatch(mark) || mark != null && Number.Match(mark).Value != mark) mark = null;
        if (mark == _rvFormulaMark) return;
        _rvFormulaMark = mark;
        PaintFormulaMarks();
    }

    // 식 띠의 숫자에 마우스를 올렸다(내리면 null — R63 JACK 2026-10-26 «수식창에서 해당 숫자에 마우스를 올렸을 때 해당 치수들이 뷰에서도 바뀌게»):
    // 식에서 같은 숫자 · 3D 에서 같은 숫자의 상자와 선을 주황으로.
    private void HoverFormulaNumber(string? value)
    {
        if (value == _rvFormulaHover) return;
        _rvFormulaHover = value;
        MarkReviewFormula(value);
        _view3d.MarkLabels(value);
    }

    private void RenderReviewFormula()
    {
        if (_rvFormulaBar == null || _rvFormulaText == null) return;
        _rvFormulaText.Inlines.Clear();
        _rvFormulaNumbers.Clear();
        if (_rvFormula is not { } f) { _rvFormulaBar.Visibility = Visibility.Collapsed; return; }
        _rvFormulaBar.Visibility = Visibility.Visible;
        if (f.head.Length > 0) _rvFormulaText.Inlines.Add(new Run(f.head) { Foreground = _sub });
        int at = 0;
        foreach (Match m in Number.Matches(f.formula))
        {
            if (m.Index > at) _rvFormulaText.Inlines.Add(new Run(f.formula[at..m.Index]));
            var run = new Run(m.Value);
            string value = m.Value;
            run.MouseEnter += (_, _) => HoverFormulaNumber(value);
            run.MouseLeave += (_, _) => { if (_rvFormulaHover == value) HoverFormulaNumber(null); };
            _rvFormulaText.Inlines.Add(run);
            _rvFormulaNumbers.Add(run);
            at = m.Index + m.Length;
        }
        if (at < f.formula.Length) _rvFormulaText.Inlines.Add(new Run(f.formula[at..]));
        _rvFormulaText.Inlines.Add(new Run(f.tail) { FontWeight = FontWeights.SemiBold });
        PaintFormulaMarks();
    }

    // 칠한 숫자만 바꾼다(글은 그대로).
    private void PaintFormulaMarks()
    {
        foreach (var run in _rvFormulaNumbers)
        {
            bool marked = _rvFormulaMark != null && run.Text == _rvFormulaMark;
            run.Background = marked ? MarkBack : null;
            run.Foreground = marked ? Brushes.White : _text;
            run.FontWeight = marked ? FontWeights.SemiBold : FontWeights.Normal;
        }
    }

    // 3D 에서 마우스를 올린 숫자 상자와 같은 주황(Model3DView).
    private static readonly Brush MarkBack = Frozen(Color.FromRgb(0xF2, 0x8C, 0x28));

    private static Brush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    /// <summary>시험용: ③ 아래 식 띠의 글 · 칠한 숫자들.</summary>
    public string ReviewFormulaText => _rvFormulaBar?.Visibility == Visibility.Visible && _rvFormulaText != null
        ? string.Concat(_rvFormulaText.Inlines.OfType<Run>().Select(r => r.Text)) : "";
    public IReadOnlyList<string> ReviewFormulaMarks => _rvFormulaText == null ? Array.Empty<string>()
        : _rvFormulaText.Inlines.OfType<Run>().Where(r => r.Background == MarkBack).Select(r => r.Text).ToList();
    /// <summary>시험용: 식 띠의 숫자 글(마우스를 올릴 자리를 찾는다).</summary>
    public IReadOnlyList<Run> ReviewFormulaNumbers => _rvFormulaNumbers;
    public TextBlock? ReviewFormulaBlock => _rvFormulaText;
}
