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
    private readonly TextBox _cutH, _cutW, _cutS, _fillH, _fillW, _fillS, _tInt, _tW;
    private readonly RadioButton _shapeMiter, _shapeRound;
    private readonly CheckBox _terrace;
    private readonly ComboBox _cutWall, _fillWall;
    /// <summary>★[UI검토 0909] 그림은 <b>한 장만</b> 그린다 — 아래 <see cref="_showCut"/>가 고른 쪽.</summary>
    private readonly Canvas _canvas;
    private readonly TextBlock _note, _status, _said;
    private readonly RadioButton _showCut, _showFill;

    /// <summary>★<b>이 도면에 맞췄을 때</b>의 사면형상 — 팝업의 <c>_miterAtOpen</c>에 해당한다.
    ///
    /// <para>팝업은 <i>"창을 연 순간"</i>이 있어 그때 값을 적어 두면 됐다.
    /// 그런데 도킹창은 <b>늘 떠 있어서 그 순간이 없다</b>(검토 0908이 짚은 그대로).
    /// → <b>도면에 맞추는 순간</b>을 그 자리로 삼는다 — 그것이 곧 "이 도면의 값을 받아 온" 때다.</para></summary>
    private bool _miterAtSync;

    /// <summary>지금 값을 채워 넣는 중인가 — 그때 오는 <c>TextChanged</c>로 그림을 다시 그리지 않는다.</summary>
    private bool _loading;

    internal GradingPanel()
    {
        var root = new StackPanel { Margin = new Thickness(10, 4, 10, 10) };
        DhBrand.Apply(this);
        Background = DhBrand.Wall;

        // ── 머리띠 ────────────────────────────────────────────────────────
        var head = DhBrand.Header("계획부지 정지", "흙을 어떻게 깎고 쌓을지 — 값을 고치면 예시가 바로 바뀝니다",
                                  drag: null, onClose: null, logoHeight: 17, titleSize: 13,
                                  pad: new Thickness(0, 0, 0, 6));
        head.Background = Brushes.Transparent;

        // ★[5a] 아직 없는 것을 <b>말한다</b> — 없는 단추를 만들어 두는 것보다 낫다.
        _status = new TextBlock
        {
            Text = "이번 판은 값만 정합니다 — 폴리곤 찍기·지표면 생성은 리본 [계획부지 정지]로 하세요.",
            FontSize = 11,
            Foreground = DhBrand.Sub,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8),
        };

        // 결과 한 줄 — 안내문과 <b>다른 칸</b>이다(위 <see cref="Say"/> 참고).
        _said = new TextBlock
        {
            FontSize = 11,
            Foreground = DhBrand.Brand,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8),
            Visibility = Visibility.Collapsed,
        };

        // ── ① 절토 / 성토 ────────────────────────────────────────────────
        GradingDialog.AddSection(root, "1. 절토 / 성토", "사면을 어떻게 계단으로 세울지", first: true);
        _cutH = Row(root, "절토 단높이 (m)", GradingSettings.CutBenchHeight);
        _cutW = Row(root, "절토 소단폭 (m)", GradingSettings.CutBenchWidth);
        _cutS = Row(root, "절토구배  1 :", GradingForm.SlopeShown(GradingSettings.CutSlope));
        _fillH = Row(root, "성토 단높이 (m)", GradingSettings.FillBenchHeight);
        _fillW = Row(root, "성토 소단폭 (m)", GradingSettings.FillBenchWidth);
        _fillS = Row(root, "성토구배  1 :", GradingForm.SlopeShown(GradingSettings.FillSlope));

        _shapeMiter = GradingDialog.AddRadioPair(root, "사면형상", "직각", GradingSettings.MiterConvex,
                                                 out _shapeRound, "라운드",
            "볼록한 모서리를 직각으로 세울지 둥글릴지 — 옹벽 장수가 크게 달라집니다.");

        // ── ② 산지 대소단 ────────────────────────────────────────────────
        GradingDialog.AddSection(root, "2. 산지 대소단", "산지전용허가법 — 일정 높이마다 넓은 소단을 둡니다");
        _terrace = new CheckBox
        {
            Content = "계단식 산지(대소단) 적용",
            IsChecked = GradingSettings.MountainTerrace,
            Margin = new Thickness(0, 0, 0, 8),
        };
        root.Children.Add(_terrace);
        _tInt = Row(root, "대소단 간격 (m)", GradingSettings.TerraceInterval);
        _tW = Row(root, "대소단 폭 (m)", GradingSettings.TerraceWidth);

        // ── ③ 옹벽 형태 ──────────────────────────────────────────────────
        GradingDialog.AddSection(root, "3. 옹벽 형태", "구배가 수직에 가까울 때 그 단을 무엇으로 세울지");
        _cutWall = GradingDialog.AddStyleRow(root, "절토 옹벽", GradingSettings.CutWallStyle, out _);
        _fillWall = GradingDialog.AddStyleRow(root, "성토 옹벽", GradingSettings.FillWallStyle, out _);

        // 값이 바뀌면 예시를 바로 다시 그린다 — 도킹창의 값어치가 여기 있다.
        foreach (var b in new[] { _cutH, _cutW, _cutS, _fillH, _fillW, _fillS, _tInt, _tW })
            b.TextChanged += (_, __) => Redraw();
        _shapeMiter.Checked += (_, __) => Redraw();
        _shapeRound.Checked += (_, __) => Redraw();
        // ★[UI검토 0909] <b>안 쓰는 칸은 잠근다</b> — 팝업·도면설정과 같은 규칙.
        void SyncTerrace()
        {
            bool on = _terrace.IsChecked == true;
            _tInt.IsEnabled = on; _tW.IsEnabled = on;
        }
        _terrace.Checked += (_, __) => { SyncTerrace(); Redraw(); };
        _terrace.Unchecked += (_, __) => { SyncTerrace(); Redraw(); };
        SyncTerrace();
        _cutWall.SelectionChanged += (_, __) => Redraw();
        _fillWall.SelectionChanged += (_, __) => Redraw();

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
        var deck = new DockPanel { LastChildFill = true };

        var top = new StackPanel { Margin = new Thickness(10, 10, 10, 0) };
        top.Children.Add(head);
        top.Children.Add(_status);
        top.Children.Add(_said);
        _showCut = GradingDialog.AddRadioPair(top, "예시", "절토", true, out _showFill, "성토",
            "값을 고치면 이 그림이 바로 바뀝니다 — 두 장을 같이 두면 값 칸이 화면 밖으로 밀려납니다.");
        top.Children.Add(Example(out _canvas, out _note));
        DockPanel.SetDock(top, Dock.Top);
        deck.Children.Add(top);

        var bottom = new Border
        {
            Background = DhBrand.Card,
            BorderBrush = DhBrand.Line,
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(10, 8, 10, 8),
        };
        var save = new Button { Content = "값 저장", MinWidth = 100, Height = 32 };
        try { if (DhBrand.Skin != null) save.Style = (Style)DhBrand.Skin["DhPrimary"]; } catch { }
        save.HorizontalAlignment = HorizontalAlignment.Left;
        save.Click += (_, __) => Save();
        bottom.Child = save;
        DockPanel.SetDock(bottom, Dock.Bottom);
        deck.Children.Add(bottom);

        deck.Children.Add(new ScrollViewer
        {
            Content = root,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        });
        Content = deck;

        _showCut.Checked += (_, __) => Redraw();
        _showFill.Checked += (_, __) => Redraw();
        _miterAtSync = GradingSettings.MiterConvex;
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
            _cutWall.SelectedIndex = (int)GradingSettings.CutWallStyle;
            _fillWall.SelectedIndex = (int)GradingSettings.FillWallStyle;
        }
        catch { }
        finally { _loading = false; }
        // ★여기가 팝업의 "창을 연 순간"에 해당한다(위 <see cref="_miterAtSync"/> 참고).
        _miterAtSync = GradingSettings.MiterConvex;
        Redraw();
        if (dirty) Say("이 도면의 값으로 바꿨습니다 — <b>저장하지 않은 입력은 사라졌습니다</b>.");
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
            if (_cutWall.SelectedIndex != (int)GradingSettings.CutWallStyle) return true;
            if (_fillWall.SelectedIndex != (int)GradingSettings.FillWallStyle) return true;
        }
        catch { }
        return false;
    }

    private void Save()
    {
        if (!GradingForm.Apply(null, new GradingForm.Controls
        {
            CutBenchHeight = _cutH, CutBenchWidth = _cutW, CutSlope = _cutS,
            FillBenchHeight = _fillH, FillBenchWidth = _fillW, FillSlope = _fillS,
            ShapeMiter = _shapeMiter,
            MountainTerrace = _terrace,
            TerraceInterval = _tInt, TerraceWidth = _tW,
            CutWallStyle = _cutWall, FillWallStyle = _fillWall,
            // 기타 설정으로 갈 것들은 이 창에 없다 — null이면 안 건드린다.
            ShowOnlyResult = null, CoordSys = null,
        }, _miterAtSync))
            return;

        _miterAtSync = GradingSettings.MiterConvex;
        Say("값을 저장했습니다 — 다음 [계획부지 정지]부터 이 값으로 만듭니다.");
        Redraw();
    }

    /// <summary>★[검토 0909 · 낮음] <b>안내문과 결과를 같은 칸에 쓰지 않는다.</b>
    /// <para>종전엔 첫 [값 저장]에 <i>"찍기·생성은 리본으로"</i>라는 안내가 영영 사라졌다 —
    /// 5a의 설계 취지(없는 단추 대신 <b>말한다</b>)가 첫 클릭에 무너지는 것이다.</para></summary>
    private void Say(string s)
    {
        _said.Text = s;
        _said.Visibility = Visibility.Visible;
        try { AcadApp.DocumentManager.MdiActiveDocument?.Editor?.WriteMessage("\n[계획부지 정지] " + s); }
        catch { }
    }

    // ── 그림 ──────────────────────────────────────────────────────────────
    private void Redraw()
    {
        if (_loading) return;
        bool cut = _showCut == null || _showCut.IsChecked == true;
        if (cut) Draw(_canvas, _note, _cutH, _cutW, _cutS, _cutWall, cut: true);
        else Draw(_canvas, _note, _fillH, _fillW, _fillS, _fillWall, cut: false);
    }

    private void Draw(Canvas c, TextBlock note, TextBox h, TextBox w, TextBox s, ComboBox wall, bool cut)
    {
        double nRaw = P(s, 1.5, 0, 30, allowZero: true);
        SlopeDiagram.Draw(c, new SlopeDiagram.Spec(
            Cut: cut,
            BenchH: P(h, 5, 0.2, 60),
            BenchW: P(w, 1, 0, 60, allowZero: true),
            Slope: System.Math.Max(nRaw, GradingSettings.MinSlope),
            SlopeRaw: nRaw,
            Terrace: _terrace.IsChecked == true,
            TerraceInterval: P(_tInt, 15, 1, 200),
            TerraceWidth: P(_tW, 15, 0, 120, allowZero: true),
            Style: (WallStyle)System.Math.Max(0, wall.SelectedIndex),
            WallGate: GradingSettings.WallGateSlope));

        // [JACK 0728] 구배<하한 입력 시에만 그림 밑 안내 표시.
        string t = (s.Text ?? "").Trim().Replace(',', '.');
        bool nz = double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out double v)
               && v >= 0 && v < GradingSettings.MinSlope - 1e-9;
        note.Visibility = nz ? Visibility.Visible : Visibility.Collapsed;
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

    
    private static TextBox Row(Panel parent, string label, double value)
        => GradingDialog.AddRow(parent, label, value, "");


    private static StackPanel Example(out Canvas canvas, out TextBlock note)
    {
        var col = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
        var box = new Border
        {
            BorderBrush = DhBrand.Line, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6), Background = DhBrand.Card, Padding = new Thickness(4),
            Child = SlopeDiagram.Wrap(out canvas),
        };
        col.Children.Add(box);
        note = new TextBlock
        {
            Text = $"※ 구배 0(~{GradingSettings.MinSlope:0.###} 미만) 입력은 {GradingSettings.MinSlope:0.###}(수직 옹벽)로 처리됩니다.",
            FontSize = 11, Foreground = DhBrand.Warn, TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 3, 0, 0), Visibility = Visibility.Collapsed,
        };
        col.Children.Add(note);
        return col;
    }
}
