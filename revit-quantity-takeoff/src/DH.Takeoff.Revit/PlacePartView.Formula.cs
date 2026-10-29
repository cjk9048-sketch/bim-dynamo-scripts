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
    private readonly HashSet<int> _rvFormulaMarks = new();   // 지금 칠한 숫자(식 띠의 숫자 차례)
    // 3D 숫자 딱지 → 식의 숫자 차례(R63d 거푸집 판의 항 · R65b 콘크리트 부재 식의 숫자 + 줄에서 그 식이 시작하는 차례 · −1 = 모름 → 같은 글로). null = 잇지 않은 딱지.
    private List<int>? _rvLabelTokens;
    // 식의 숫자 상자(칠할 때 다시 짓지 않는다 — 마우스 밑의 것을 바꾸면 들고 남이 되풀이된다). 3D 치수처럼 테두리 상자 — 상자 전체가 마우스를 받는다
    // (R63c — JACK 2026-10-26 «수식 부분도 치수처럼 박스로 · 1 같이 얇은 건 마우스 올리기가 너무 힘들어»).
    private readonly List<(Border box, TextBlock text)> _rvFormulaNumbers = new();
    private int _rvFormulaHover = -1;        // 식 띠에서 마우스를 올린 숫자의 차례(R63)
    private int _rvFormulaCount = -1;        // 식 띠 끝 « × n »(같은 줄 개수)의 차례 — 치수가 아니라 3D 와 잇지 않는다(R65c) · 없으면 −1
    private static Regex Number => FormworkLabels.NumberPattern;   // 줄 식의 숫자 차례와 같게(«1/2» 는 숫자 하나 — JACK 2026-10-29)

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
            _view3d.LabelHovered += label => { if (Step == 2) MarkFormulaFromLabel(label); };
            _rvFormulaHooked = true;
        }
        return stack;
    }

    private bool _rvFormulaHooked;

    // 고른 줄의 식을 띠에(없으면 숨김): «부호 (괄호) : 식 × n = 값» — 목록 줄과 같은 글, 줄이지 않고 전부.
    private void ShowReviewFormula()
    {
        _rvFormula = null;
        _rvFormulaCount = -1;
        _rvFormulaMarks.Clear();
        HoverFormulaNumber(-1);
        if (_rv != null && _rvPicked >= 0 && _rvPicked < _rvViews.Count && _rvViews[_rvPicked].item is int item && item >= 0
            && _rv.Items[item].Item is Sheet2D.Line line)
        {
            string head = line.Mark.Length > 0 ? line.Mark + (line.Tag.Length > 0 ? " " + line.Tag : "") + " : " : "";
            string formula = FormworkLabels.Pretty(line.Formula) + (line.Count > 1 ? $" × {line.Count}" : "");
            _rvFormula = (head, formula, $" = {M2(Sheet2D.LineValue(line))}");
            _rvFormulaCount = line.Count > 1 ? FormworkLabels.NumberPattern.Matches(line.Formula).Count : -1;
        }
        RenderReviewFormula();
    }

    // 3D 숫자에 마우스를 올렸다(−1 = 내림): 식에서 그 숫자(R63d — 판의 항으로 이은 그 자리 하나). 잇지 못한 딱지는 같은 숫자 가운데 다른 딱지가 잇지 않은 것만
    // (R65c — FormworkLabels.TokensMarkedBy · 낱말 단위 — 15.6 의 5.6 은 아님 · 각도는 «°» 를 떼고 · 식 딱지(숫자 하나가 아님)와 « × n » 은 칠하지 않는다).
    private void MarkFormulaFromLabel(int label)
    {
        var marks = new HashSet<int>();
        if (label >= 0)
        {
            var tokenTexts = _rvFormulaNumbers.Select((n, t) => t == _rvFormulaCount ? "" : n.text.Text).ToList();
            marks.UnionWith(FormworkLabels.TokensMarkedBy(_rvLabelTokens, LabelTexts(), label, tokenTexts));
        }
        SetFormulaMarks(marks);
    }

    private List<string?> LabelTexts() => Enumerable.Range(0, _view3d.LabelCount).Select(i => _view3d.LabelText(i)).ToList();

    private void SetFormulaMarks(HashSet<int> marks)
    {
        if (marks.SetEquals(_rvFormulaMarks)) return;
        _rvFormulaMarks.Clear();
        _rvFormulaMarks.UnionWith(marks);
        PaintFormulaMarks();
    }

    // 식 띠의 숫자에 마우스를 올렸다(−1 = 내림 — R63 JACK 2026-10-26 «수식창에서 해당 숫자에 마우스를 올렸을 때 해당 치수들이 뷰에서도 바뀌게» ·
    // R63d «숫자가 1 이면 같은 건 전부 다 떠 · 그 1 마다 위치가 있을 텐데»): 식에서 그 숫자 하나 · 3D 에서 그 숫자로 이은 딱지와 선을 주황으로
    // (그 숫자에 이은 딱지가 없을 때만 같은 글의 못 이은 딱지 — R65c LabelsOfToken).
    private void HoverFormulaNumber(int token)
    {
        if (token == _rvFormulaHover) return;
        _rvFormulaHover = token;
        SetFormulaMarks(token >= 0 ? new HashSet<int> { token } : new HashSet<int>());
        _view3d.MarkLabels(token >= 0 && token < _rvFormulaNumbers.Count ? LabelsOfToken(token) : null);
    }

    // 식 숫자 token 에 칠할 3D 딱지(R65c — FormworkLabels.LabelsLitBy): 그 숫자에 이은 딱지만 · 이은 딱지가 없을 때만 같은 글의 못 이은 딱지 · « × n » 은 없음
    // (JACK 2026-10-27 «a × b 구간의 b 에 대면 그 b 만이어야 하는데 다르게 잡혀»).
    private List<int> LabelsOfToken(int token) =>
        token == _rvFormulaCount ? new List<int>() : FormworkLabels.LabelsLitBy(_rvLabelTokens, LabelTexts(), token, _rvFormulaNumbers[token].text.Text);

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
            string value = m.Value;
            var text = new TextBlock { Text = value, FontSize = 13, TextAlignment = TextAlignment.Center };
            var box = new Border
            {
                Child = text, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(3), Padding = new Thickness(5, 0, 5, 1),
                Margin = new Thickness(1, 1, 1, 1), MinWidth = 20, Cursor = System.Windows.Input.Cursors.Hand,
            };
            int token = _rvFormulaNumbers.Count;      // 식 띠의 숫자 차례 = 줄 식의 숫자 차례(« × n » 은 맨 끝)
            box.MouseEnter += (_, _) => HoverFormulaNumber(token);
            box.MouseLeave += (_, _) => { if (_rvFormulaHover == token) HoverFormulaNumber(-1); };
            _rvFormulaText.Inlines.Add(new InlineUIContainer(box) { BaselineAlignment = BaselineAlignment.Center });
            _rvFormulaNumbers.Add((box, text));
            at = m.Index + m.Length;
        }
        if (at < f.formula.Length) _rvFormulaText.Inlines.Add(new Run(f.formula[at..]));
        _rvFormulaText.Inlines.Add(new Run(f.tail) { FontWeight = FontWeights.SemiBold });
        PaintFormulaMarks();
    }

    // 칠한 숫자만 바꾼다(글은 그대로).
    private void PaintFormulaMarks()
    {
        for (int t = 0; t < _rvFormulaNumbers.Count; t++)
        {
            var (box, text) = _rvFormulaNumbers[t];
            bool marked = _rvFormulaMarks.Contains(t);
            box.Background = marked ? MarkBack : _ui["Card"];
            box.BorderBrush = marked ? MarkLine : _ui["InputLine"];
            text.Foreground = marked ? Brushes.White : _text;
            text.FontWeight = marked ? FontWeights.SemiBold : FontWeights.Normal;
        }
    }

    // 3D 에서 마우스를 올린 숫자 상자와 같은 주황(Model3DView).
    private static readonly Brush MarkBack = Frozen(Color.FromRgb(0xF2, 0x8C, 0x28)), MarkLine = Frozen(Color.FromRgb(0xB8, 0x5C, 0x0A));

    private static Brush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    /// <summary>시험용: ③ 아래 식 띠의 글(숫자 상자의 글 포함) · 칠한 숫자들.</summary>
    public string ReviewFormulaText => _rvFormulaBar?.Visibility == Visibility.Visible && _rvFormulaText != null
        ? string.Concat(_rvFormulaText.Inlines.Select(i => i switch
        {
            Run r => r.Text,
            InlineUIContainer { Child: Border { Child: TextBlock t } } => t.Text,
            _ => "",
        })) : "";
    public IReadOnlyList<string> ReviewFormulaMarks => _rvFormulaNumbers.Where(n => n.box.Background == MarkBack).Select(n => n.text.Text).ToList();
    /// <summary>시험용: 칠한 숫자의 차례 · 3D 딱지마다 이은 식의 숫자 차례(null = 잇지 않음).</summary>
    public IReadOnlyList<int> ReviewFormulaMarkIndices => Enumerable.Range(0, _rvFormulaNumbers.Count).Where(t => _rvFormulaNumbers[t].box.Background == MarkBack).ToList();
    public IReadOnlyList<int>? ReviewLabelTokens => _rvLabelTokens;
    /// <summary>시험용: 식 띠의 숫자 상자(마우스를 올릴 자리) · 그 글.</summary>
    public IReadOnlyList<(Border box, TextBlock text)> ReviewFormulaNumbers => _rvFormulaNumbers;
}
