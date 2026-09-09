using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using DH.Grading.Civil;

/// <summary>[4단계] 예시 그림이 <b>정말 그려지는지</b>·<b>값이 그림에 닿는지</b>·
/// <b>줄어들어도 살아 있는지</b>를 잰다.
/// <para>★개수만 세면 모양이 바뀌어도 못 잡는다(첫 판이 그랬다) — <b>좌표를 다 더한 지문</b>으로 견준다.</para></summary>
internal static class DiagCheck
{
    /// <summary>그림의 <b>지문</b> — 선 끝점·글자 자리·글자 길이를 다 더한다.
    /// 모양이 조금만 달라도 값이 달라진다.</summary>
    private static string Sign(Canvas c)
    {
        double acc = 0; int nLine = 0, nText = 0, nOther = 0;
        foreach (UIElement e in c.Children)
        {
            // ★자리를 안 정한 요소는 GetLeft가 NaN이다 — 그것 하나가 지문 전체를 NaN으로 만들어
            //   "모든 그림이 같다"는 <b>거짓 통과</b>가 된다(첫 판이 그랬다). NaN은 건너뛴다.
            static double V(double x) => double.IsNaN(x) || double.IsInfinity(x) ? 0 : x;
            if (e is System.Windows.Shapes.Line ln)
            { acc += V(ln.X1) * 1.1 + V(ln.Y1) * 2.3 + V(ln.X2) * 3.7 + V(ln.Y2) * 5.1 + V(ln.StrokeThickness) * 7; nLine++; }
            else if (e is TextBlock tb)
            { acc += V(Canvas.GetLeft(tb)) * 1.3 + V(Canvas.GetTop(tb)) * 2.9 + tb.Text.Length * 11 + V(tb.FontSize); nText++; }
            else if (e is FrameworkElement fe)
            { acc += V(Canvas.GetLeft(fe)) * 1.7 + V(Canvas.GetTop(fe)) * 3.1 + V(fe.Width) * 4.3 + V(fe.Height) * 6.7; nOther++; }
        }
        return $"선{nLine}/글{nText}/기타{nOther}/합{acc.ToString("F3", CultureInfo.InvariantCulture)}";
    }

    internal static int Run()
    {
        int bad = 0;
        void Chk(string what, bool ok, string got)
        { Console.WriteLine((ok ? "PASS  " : "FAIL  ") + what + "  " + got); if (!ok) bad++; }

        SlopeDiagram.Spec S(bool cut, double slope, bool terrace, WallStyle st) =>
            new(Cut: cut, BenchH: 5, BenchW: 1, Slope: Math.Max(slope, 0.05), SlopeRaw: slope,
                Terrace: terrace, TerraceInterval: 15, TerraceWidth: 15, Style: st, WallGate: 0.05);

        string Draw(SlopeDiagram.Spec sp, out int n)
        {
            SlopeDiagram.Wrap(out Canvas c);
            SlopeDiagram.Draw(c, sp);
            n = c.Children.Count;
            return Sign(c);
        }
        string Sig(SlopeDiagram.Spec sp) => Draw(sp, out _);   // 지역 함수는 이름을 겹칠 수 없다

        var baseCut = S(true, 1.5, false, WallStyle.없음_사면);
        string sCut = Draw(baseCut, out int nCut);
        string sFill = Draw(S(false, 1.5, false, WallStyle.없음_사면), out int nFill);
        Chk("D1 절토 예시가 그려진다", nCut >= 10, sCut);
        Chk("D1 성토 예시가 그려진다", nFill >= 10, sFill);
        Chk("D1 ★절토와 성토는 다른 그림이다", sCut != sFill, "");

        // ★값이 그림에 <b>정말 닿는지</b> — 하나씩 바꿔 지문이 달라지는지 본다.
        (string Name, SlopeDiagram.Spec Sp)[] probes =
        {
            ("단높이 5→2",   baseCut with { BenchH = 2 }),
            ("소단폭 1→4",   baseCut with { BenchW = 4 }),
            ("구배 1.5→0.5", baseCut with { Slope = 0.5, SlopeRaw = 0.5 }),
            ("대소단 켬",     baseCut with { Terrace = true }),
            ("대소단폭 15→30", baseCut with { Terrace = true, TerraceWidth = 30 }),
            ("대소단간격 15→30", baseCut with { Terrace = true, TerraceInterval = 30 }),
        };
        foreach (var p in probes)
        {
            string sig = Sig(p.Sp);
            Chk($"D2 ★{p.Name} — 그림이 달라진다", sig != sCut, sig);
        }
        // 대소단 폭·간격은 대소단을 켠 것끼리 견줘야 뜻이 있다
        string sT = Sig(baseCut with { Terrace = true });
        Chk("D2 ★대소단 폭이 그림에 닿는다", Sig(baseCut with { Terrace = true, TerraceWidth = 30 }) != sT, "");
        Chk("D2 ★대소단 간격이 그림에 닿는다", Sig(baseCut with { Terrace = true, TerraceInterval = 30 }) != sT, "");

        // 수직이면 옹벽 단면이 붙는다 — 형태마다 다르게
        string w0 = Draw(S(true, 0.0, false, WallStyle.없음_사면), out int nW0);
        string wt = Draw(S(true, 0.0, false, WallStyle.역T형), out int nWt);
        string wb = Draw(S(true, 0.0, false, WallStyle.보강토), out int nWb);
        string wa = Draw(S(true, 0.0, false, WallStyle.앵커판넬), out int nWa);
        Chk("D3 ★수직+옹벽형태면 그림이 더 붙는다", nWt > nW0 && nWb > nW0 && nWa > nW0,
            $"사면 {nW0} · 역T {nWt} · 보강토 {nWb} · 앵커 {nWa}");
        Chk("D3 ★형태 셋이 서로 다른 그림이다", wt != wb && wb != wa && wt != wa, "");
        Chk("D3 사면일 때는 옹벽 형태를 안 본다",
            Sig(S(true, 1.5, false, WallStyle.역T형)) == sCut, "");

        // ★크기 맞추기 — Viewbox가 줄여도 안쪽 계산은 420×300 그대로여야 한다
        var vb2 = SlopeDiagram.Wrap(out Canvas c2);
        SlopeDiagram.Draw(c2, baseCut);
        var host = new Window { Width = 300, Height = 240, ShowActivated = false };
        var panel = new StackPanel { Width = 240 };
        panel.Children.Add(vb2);
        host.Content = panel;
        host.Show(); host.UpdateLayout();
        Chk("D4 ★좁은 창에서도 그림이 그대로다", Sign(c2) == sCut, Sign(c2));
        Chk("D4 ★안쪽 계산 자리는 420×300 그대로",
            Math.Abs(c2.Width - 420) < 0.01 && Math.Abs(c2.Height - 300) < 0.01, $"{c2.Width}x{c2.Height}");
        Chk("D4 ★화면에서는 창 폭에 맞춰 줄어든다", vb2.ActualWidth <= 241 && vb2.ActualWidth > 10,
            $"{vb2.ActualWidth:F0}px");
        host.Close();

        // 두 번 그려도 같은 그림 — 그릴 때마다 달라지면 창을 새로고침할 수 없다
        Chk("D5 ★두 번 그려도 같다", Sig(baseCut) == sCut, "");

        // 이상한 값에도 안 터진다
        try
        {
            Sig(new SlopeDiagram.Spec(true, 0, 0, 0, 0, true, 0, 0, WallStyle.역T형, 0.05));
            Sig(new SlopeDiagram.Spec(false, 1e6, 1e6, 1e6, 1e6, true, 1e6, 1e6, WallStyle.보강토, 0.05));
            Sig(new SlopeDiagram.Spec(true, double.NaN, double.NaN, double.NaN, double.NaN, false, 1, 1, WallStyle.없음_사면, 0.05));
            Chk("D6 이상한 값(0·거대·NaN)에도 안 터진다", true, "");
        }
        catch (Exception ex) { Chk("D6 이상한 값(0·거대·NaN)에도 안 터진다", false, ex.Message); }

        return bad;
    }
}
