using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DH.Grading.Core;

namespace DH.Grading.Civil;

/// <summary>★[JACK 1117 «단면표 우측 맨 위 도움말 — 누르면 도움말 창 · 옹벽 단면표 메뉴얼 · 설명 이미지»] 옹벽 단면 표 도움말 창.
/// <para>그림은 단면 표 창과 같은 그리기(<see cref="WallSectionDiagram"/>)로 표준 표 값을 그대로 그린다 — 그림과 표가 어긋날 일이 없다.</para>
/// <para>글은 초보용 단답형(JACK 1112) — 한 줄에 하나.</para></summary>
internal sealed class WallSectionHelpWindow : Window
{
    readonly WallSection.Common _common;
    readonly double _margin, _slope;
    readonly bool _cut;

    internal WallSectionHelpWindow(WallSection.Common common, double margin, double slope, bool cut)
    {
        _common = common; _margin = margin; _slope = slope; _cut = cut;
        Title = "DH 옹벽 단면 표 도움말";
        Width = 940;
        Height = System.Math.Min(860, SystemParameters.WorkArea.Height - 40);
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        var sp = new StackPanel { Margin = new Thickness(18, 10, 18, 10) };

        // 1 무엇
        H(sp, "1. 이 창은");
        P(sp, "옹벽 형식마다 높이(H)별 단면 치수 표.");
        P(sp, "옹벽 터파기 · 옹벽 몸통 모양이 이 표의 치수를 씀.");
        P(sp, "처음 값 = 국토해양부 «도로옹벽 표준도 2008» 일람표.");
        P(sp, "고친 표는 이 도면에만 저장.");

        // 2 화면
        H(sp, "2. 화면");
        sp.Children.Add(Layout());
        P(sp, "① 형식 · 벽 뒤 조건 고르기 → 그 조건의 표준 표로 바뀜.");
        P(sp, "② 그림 = 표에서 고른 줄의 단면.");
        P(sp, "③ 표 = 높이 구간마다 한 줄. 칸을 누르면 그림의 그 치수가 주황.");
        P(sp, "④ [저장] 도면에 적음 · [표준 표로] 처음 값 · [닫기].");

        // 3 형식
        H(sp, "3. 형식");
        var kinds = new WrapPanel();
        foreach (var k in new[] { WallSection.Kind.역T형, WallSection.Kind.L형, WallSection.Kind.반중력식, WallSection.Kind.중력식 })
            kinds.Children.Add(Pic(WallSection.Of(k).Name, k, WallSection.Cond.Default, 5.0, false, 205, 190));
        sp.Children.Add(kinds);
        P(sp, "역T형 · L형 = 철근콘크리트(캔틸레버) · 높은 벽(3 ~ 8 m).");
        P(sp, "반중력식 · 중력식 = 무게로 버팀 · 낮은 벽.");

        // 4 기호
        H(sp, "4. 표 기호");
        var symRow = new WrapPanel();
        symRow.Children.Add(Pic("역T형 · H 5 m", WallSection.Kind.역T형, new WallSection.Cond(1, 3, true), 5.0, true, 440, 330));
        symRow.Children.Add(SymbolTable(WallSection.Kind.역T형));
        sp.Children.Add(symRow);
        P(sp, "H = 근입(Df) 포함 전체 높이 · 단위 m.");

        // 5 벽 뒤 조건
        H(sp, "5. 벽 뒤 · 쌓인 높이");
        var backs = new WrapPanel();
        backs.Children.Add(Pic("벽 뒤 평평", WallSection.Kind.역T형, new WallSection.Cond(0, 0, true), 5.0, false, 280, 220));
        backs.Children.Add(Pic("1:1.8 비탈 · 쌓인 높이 1.0H", WallSection.Kind.역T형, new WallSection.Cond(1, 2, true), 5.0, false, 280, 220));
        backs.Children.Add(Pic("1:1.5 비탈 · 쌓인 높이 2.0H", WallSection.Kind.역T형, new WallSection.Cond(2, 3, true), 5.0, false, 280, 220));
        sp.Children.Add(backs);
        P(sp, "벽 뒤 = 벽 꼭대기 뒤 흙 모양(평평 · 1:1.8 · 1:1.5 비탈).");
        P(sp, "쌓인 높이 = 벽 뒤 비탈이 벽 높이 H의 몇 배까지 오르나(¼H · ½H · 1.0H · 2.0H · ∞ 끝없이).");
        P(sp, "흙이 많이 얹힐수록 미는 힘이 커짐 → 뒷굽(B2) · 저판이 커짐.");

        // 6 지진
        H(sp, "6. 지진 고려 / 미고려");
        var seis = new WrapPanel();
        var cS = new WallSection.Cond(1, 3, true); var cN = new WallSection.Cond(1, 3, false);
        seis.Children.Add(Pic("지진 고려", WallSection.Kind.역T형, cS, 5.0, true, 400, 300));
        seis.Children.Add(Pic("지진 미고려", WallSection.Kind.역T형, cN, 5.0, true, 400, 300));
        sp.Children.Add(seis);
        sp.Children.Add(Compare(WallSection.Kind.역T형, cS, cN, 5.0));
        P(sp, "표준도 일람표가 두 벌 — 지진 때 흙이 더 미는 힘까지 넣은 표(고려) · 안 넣은 표(미고려).");
        P(sp, "같은 높이 · 같은 벽 뒤 조건이라도 저판 두께 · 뒷굽 · 활동방지벽(키) 치수가 다름 — 아래 표(빨강 = 다른 칸).");
        P(sp, "벽 뒤 평평 = 두 표가 같은 단면.");
        P(sp, "미고려에만 «쌓인 높이 ∞» 칸이 있음.");
        P(sp, "중력식 = 구분 없음(이 칸 숨김) · 대신 앞면 경사(1:0.02 ~ 1:0.30)를 고름.");
        P(sp, "무엇을 쓸지는 설계 기준(내진 대상인지)을 따름. 처음 값 = 지진 고려.", warn: true);

        // 7 표 고치기
        H(sp, "7. 표 고치기");
        P(sp, "칸 두 번 눌러 고침 → 그림 바로 바뀜.");
        P(sp, "맨 아래 빈 줄에 쓰면 줄 늘어남 · 칸 누르고 Delete = 그 줄 지움.");
        P(sp, "조건을 바꾸면 그 조건의 표준 표로 바뀜(고친 값이 있으면 물어봄).");
        P(sp, "형식 옆 ⚠ = 공개 표준 표 없음 → 직접 넣은 값.");
        P(sp, "터파기 · 3D에 반영 = [저장] 뒤 그 창에서 다시 만들기.");

        var (_, close) = DhBrand.FooterButtons("닫기");
        close.Content = "닫기";
        var scroll = new ScrollViewer { Content = sp, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Height = Height - 150 };
        DhBrand.Dress(this, "옹벽 단면 표 도움말", "형식 · 기호 · 벽 뒤 조건 · 지진 고려 · 표 고치기", scroll, close);
    }

    static void H(StackPanel sp, string s) => sp.Children.Add(new TextBlock { Text = s, FontSize = 15, FontWeight = FontWeights.SemiBold, Foreground = DhBrand.Brand, Margin = new Thickness(0, 16, 0, 6) });
    static void P(StackPanel sp, string s, bool warn = false) => sp.Children.Add(new TextBlock { Text = "· " + s, FontSize = 12.5, Foreground = warn ? DhBrand.Warn : DhBrand.Ink, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4, 1, 0, 1) });

    /// <summary>표준 표에서 H에 가장 가까운 줄.</summary>
    static double[]? RowNear(WallSection.Kind k, WallSection.Cond c, double h)
    {
        var t = WallSection.DefaultTable(k, c);
        return t.Rows.Count == 0 ? null : t.Rows.OrderBy(r => System.Math.Abs(r[0] - h)).First();
    }

    /// <summary>제목 + 그 형식 · 조건 · 높이의 단면 그림(단면 표 창과 같은 그리기).</summary>
    FrameworkElement Pic(string title, WallSection.Kind k, WallSection.Cond c, double h, bool symbols, double w, double ht)
    {
        var box = new StackPanel { Margin = new Thickness(0, 0, 10, 8) };
        box.Children.Add(new TextBlock { Text = title, FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = DhBrand.Ink, Margin = new Thickness(2, 0, 0, 2) });
        var row = RowNear(k, c, h);
        FrameworkElement pic;
        try
        {
            var vb = WallSectionDiagram.Wrap(out var canvas, 410);
            if (row != null)
            {
                double hv = row[0] - WallSection.Col(k, row, "Df");
                WallSectionDiagram.Draw(canvas, new WallSectionDiagram.Spec(k, row, hv, _common, _margin, _slope, _cut, Symbols: symbols, Cond: c), out _);
            }
            vb.Width = w; vb.Height = ht;
            pic = vb;
        }
        catch { pic = new TextBlock { Text = "(그림 못 그림)", Foreground = DhBrand.Sub, Width = w, Height = ht }; }
        box.Children.Add(new Border { BorderBrush = DhBrand.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Background = DhBrand.Card, Padding = new Thickness(4), Child = pic });
        return box;
    }

    /// <summary>기호 · 뜻 표(형식의 열 이름 그대로).</summary>
    static FrameworkElement SymbolTable(WallSection.Kind k)
    {
        var info = WallSection.Of(k);
        var g = new Grid { Margin = new Thickness(6, 22, 0, 8), VerticalAlignment = VerticalAlignment.Top };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        for (int i = 0; i < info.Cols.Length; i++)
        {
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var a = new TextBlock { Text = info.Cols[i], FontFamily = new FontFamily("Consolas"), FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = DhBrand.Brand, Margin = new Thickness(0, 2, 14, 2) };
            var b = new TextBlock { Text = info.ColNames[i].Replace("\n", " "), FontSize = 12, Foreground = DhBrand.Ink, Margin = new Thickness(0, 2, 0, 2) };
            Grid.SetRow(a, i); Grid.SetRow(b, i); Grid.SetColumn(b, 1);
            g.Children.Add(a); g.Children.Add(b);
        }
        return g;
    }

    /// <summary>지진 고려 / 미고려 같은 높이 줄 견줌 — 다른 칸만 굵게.</summary>
    static FrameworkElement Compare(WallSection.Kind k, WallSection.Cond a, WallSection.Cond b, double h)
    {
        var info = WallSection.Of(k);
        var ra = RowNear(k, a, h); var rb = RowNear(k, b, h);
        var g = new Grid { Margin = new Thickness(0, 2, 0, 8), HorizontalAlignment = HorizontalAlignment.Left };
        if (ra == null || rb == null) return g;
        g.RowDefinitions.Add(new RowDefinition()); g.RowDefinitions.Add(new RowDefinition()); g.RowDefinitions.Add(new RowDefinition());
        void Cell(int r, int c, string s, bool bold, Brush fg)
        {
            var t = new TextBlock { Text = s, FontSize = 12, FontWeight = bold ? FontWeights.Bold : FontWeights.Normal, Foreground = fg, Margin = new Thickness(6, 3, 6, 3), HorizontalAlignment = HorizontalAlignment.Right };
            var bd = new Border { BorderBrush = DhBrand.Line, BorderThickness = new Thickness(0, 0, 1, 1), Background = r == 0 ? DhBrand.BrandDim : DhBrand.Card, Child = t };
            Grid.SetRow(bd, r); Grid.SetColumn(bd, c); g.Children.Add(bd);
        }
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Cell(0, 0, $"H {ra[0]:0.0#} m · 1:1.8 · 2.0H", true, DhBrand.Ink); Cell(1, 0, "지진 고려", true, DhBrand.Ink); Cell(2, 0, "지진 미고려", true, DhBrand.Ink);
        int col = 1;
        for (int i = 1; i < info.Cols.Length && i < ra.Length && i < rb.Length; i++)
        {
            if (info.Cols[i] == "Df") continue;
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            bool diff = System.Math.Abs(ra[i] - rb[i]) > 1e-9;
            Cell(0, col, info.Cols[i], true, DhBrand.Brand);
            Cell(1, col, ra[i].ToString("0.0#"), diff, diff ? DhBrand.Warn : DhBrand.Ink);
            Cell(2, col, rb[i].ToString("0.0#"), diff, DhBrand.Ink);
            col++;
        }
        return new ScrollViewer { Content = g, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled };
    }

    /// <summary>창 모양 그림 — 네 자리에 번호(① 조건 · ② 그림 · ③ 표 · ④ 단추).</summary>
    static FrameworkElement Layout()
    {
        var c = new Canvas { Width = 620, Height = 250, Background = DhBrand.Card };
        void Box(double x, double y, double w, double h, string label, string no)
        {
            var r = new System.Windows.Shapes.Rectangle { Width = w, Height = h, Stroke = DhBrand.Brand, StrokeThickness = 1.4, Fill = DhBrand.Wall, RadiusX = 4, RadiusY = 4 };
            Canvas.SetLeft(r, x); Canvas.SetTop(r, y); c.Children.Add(r);
            var t = new TextBlock { Text = no + " " + label, FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = DhBrand.Brand };
            Canvas.SetLeft(t, x + 10); Canvas.SetTop(t, y + 8); c.Children.Add(t);
        }
        Box(10, 10, 600, 34, "형식 · 벽 뒤 · 쌓인 높이 · 지진", "①");
        Box(10, 54, 290, 140, "단면 그림", "②");
        Box(310, 54, 300, 140, "높이별 표", "③");
        Box(330, 204, 280, 34, "저장 · 표준 표로 · 닫기", "④");
        // 표 칸 → 그림 치수 화살표
        var ar = new System.Windows.Shapes.Path { Stroke = WallSectionDiagram.HiBrush, StrokeThickness = 2, Data = Geometry.Parse("M 360,140 C 330,170 280,170 220,140 M 220,140 L 232,138 M 220,140 L 226,150") };
        c.Children.Add(ar);
        var at = new TextBlock { Text = "칸 누르면 치수 주황", FontSize = 11, Foreground = WallSectionDiagram.HiBrush };
        Canvas.SetLeft(at, 230); Canvas.SetTop(at, 168); c.Children.Add(at);
        return new Border { BorderBrush = DhBrand.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Child = c, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 6) };
    }
}
