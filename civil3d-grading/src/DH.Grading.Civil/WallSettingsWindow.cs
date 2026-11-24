using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Autodesk.AutoCAD.Runtime;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;
using AcDoc = Autodesk.AutoCAD.ApplicationServices.Document;
using Database = Autodesk.AutoCAD.DatabaseServices.Database;
using Kind = DH.Grading.Core.WallSection.Kind;
using ObjectId = Autodesk.AutoCAD.DatabaseServices.ObjectId;
using Point3d = Autodesk.AutoCAD.Geometry.Point3d;

namespace DH.Grading.Civil;

/// <summary>★★[JACK 1124 «계획부지 수정 탭의 옹벽 부분을 통째로 옹벽 설정이라고 따로 빼서 일반 팝업 · 계획부지 수정 탭과 진입도로 수정 탭에 옹벽 설정 단추»]
/// <b>옹벽 설정</b> 팝업 — [계획부지] = 계획부지 정지 창에 있던 «옹벽 형식 · 뒷채움 터파기» 칸 통째(값 · 저장 길 · 때 그대로) · [진입도로] = 열린 도로의 절토 · 성토 단면 형식.
/// <para>★★[JACK 1124 «닫기 말고 적용 · 닫기 두 가지 — 적용을 누르면 설정한 게 적용되고 다 업데이트»] 칸을 고쳐도 바로 적지 않는다(고친 값은 창 안에만 · 그림은 따라감).
/// [적용](바뀐 값 있으면 빨강 깜박) = 계획부지 기록(WALLEXCAV) · 도로 설계 기록(<see cref="RoadDesign.WallCutKind"/> · <see cref="RoadDesign.WallFillKind"/>)에 적고
/// 그려 둔 부지 종단도 · 횡단도 · 진입도로 횡단도를 같은 자리에 다시(<see cref="WallSetCommand.Run"/>) · 창은 그대로 «적용됨». [닫기] · Esc = 안 적은 값 버림.</para>
/// <para>모양 있는 창(<see cref="AcadApp.ShowModalWindow(Window)"/>) — 늘 명령(DHWALLSET · DHWALLSETROAD) 안에서 띄운다: 띄운 동안 도면 · 명령이 안 바뀌고 [적용]이 그 명령 안에서 돈다.</para></summary>
internal sealed class WallSettingsWindow : Window
{
    private readonly AcDoc _doc;
    private readonly RoadSession? _road;
    private string? _rdStoredCut, _rdStoredFill;
    private readonly Button _apply;
    private readonly TextBlock _applyMsg;
    private readonly System.Windows.Threading.DispatcherTimer _msgTimer;
    private bool _applying;

    // ── 계획부지(종전 GradingPanel 칸 · 이름 그대로) ──
    private readonly ComboBox _exSlope, _exCutKind, _exFillKind;
    private readonly CheckBox _exOn;
    private readonly TextBox _exMargin, _exTLean, _exELean, _exBackSlope;
    private readonly RadioButton _exBackOn, _exRestore;
    private readonly CheckBox _exBenchOn;
    private readonly TextBox _exBenchH, _exBenchW;
    private DH.Grading.Core.WallExcav.Settings _exStored = DH.Grading.Core.WallExcav.Settings.Default;
    private readonly Dictionary<Kind, DH.Grading.Core.WallSection.Table> _secCache = new();

    // ── 진입도로 ──
    private readonly ComboBox _rdCutKind, _rdFillKind;
    private readonly TextBlock _rdName;

    // ── 그림 · 탭 ──
    private readonly Canvas _canvasExcav, _canvasSec;
    private readonly Viewbox _excavBox, _secBox;
    private readonly TextBlock _cap;
    private readonly StackPanel _siteBody, _roadBody;
    private readonly Border[] _tabs = new Border[2];
    private bool _roadTab;
    private bool _loading;

    internal WallSettingsWindow(AcDoc doc, bool road)
    {
        _doc = doc;
        _road = RoadSession.Of(doc);
        Title = "DH 옹벽 설정";
        Width = 500;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ShowInTaskbar = false;

        var body = new StackPanel { Margin = new Thickness(18, 12, 18, 10) };

        // ── 맨 위 그림 한 칸(종전 도킹창 맨 위 칸의 옹벽 그림 · 형식 단면) ──
        var slot = new Grid { Width = 420, Height = 300 };
        _excavBox = Fit(ExcavDiagram.Wrap(out _canvasExcav));
        _secBox = Fit(WallSectionDiagram.Wrap(out _canvasSec));
        slot.Children.Add(_excavBox); slot.Children.Add(_secBox);
        var picCol = new StackPanel();
        picCol.Children.Add(slot);
        _cap = new TextBlock { MaxWidth = 420, MinHeight = 30, FontSize = 11, Foreground = DhBrand.Ink, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 4, 2, 0) };
        picCol.Children.Add(_cap);
        body.Children.Add(new Border
        {
            BorderBrush = DhBrand.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Background = DhBrand.Card,
            Padding = new Thickness(4), HorizontalAlignment = HorizontalAlignment.Center, Child = picCol,
        });

        // ── 탭 [계획부지] | [진입도로] ──
        var tabGrid = new Grid { Margin = new Thickness(0, 10, 0, 8) };
        tabGrid.ColumnDefinitions.Add(new ColumnDefinition()); tabGrid.ColumnDefinitions.Add(new ColumnDefinition());
        string[] names = { "계획부지", "진입도로" };
        for (int i = 0; i < 2; i++)
        {
            int idx = i;
            var tb = new TextBlock { Text = names[i], FontSize = 13, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            _tabs[i] = new Border { Child = tb, Height = 30, Cursor = System.Windows.Input.Cursors.Hand, Focusable = true, CornerRadius = i == 0 ? new CornerRadius(6, 0, 0, 6) : new CornerRadius(0, 6, 6, 0), BorderBrush = DhBrand.Brand, BorderThickness = new Thickness(1) };
            _tabs[i].MouseLeftButtonUp += (_, __) => ShowPart(idx == 1);
            Grid.SetColumn(_tabs[i], i); tabGrid.Children.Add(_tabs[i]);
        }
        body.Children.Add(tabGrid);

        // ══ [계획부지] — 종전 GradingPanel «옹벽 형식 · 뒷채움 터파기» 칸 그대로(풍선 글 · 이름 · 첫 값) ══
        // 두 쪽을 한 칸에 겹쳐 둔다(안 보이는 쪽 = Hidden) — 탭을 바꿔도 창 높이 · [닫기] 자리가 그대로
        var parts = new Grid();
        body.Children.Add(parts);
        _siteBody = new StackPanel();
        parts.Children.Add(_siteBody);
        var secWall = _siteBody;
        var exDef = DH.Grading.Core.WallExcav.Settings.Default;
        var kRow = new WrapPanel { Margin = new Thickness(2, 0, 0, 2) };
        _exCutKind = KindCell(kRow, "절토 단면", exDef.CutKind);
        _exFillKind = KindCell(kRow, "성토 단면", exDef.FillKind);
        secWall.Children.Add(kRow);
        var secBtn = new Button
        {
            Content = "단면 표", Height = 30, MinWidth = 120, Padding = new Thickness(14, 0, 14, 0), Margin = new Thickness(2, 2, 0, 8),
            HorizontalAlignment = HorizontalAlignment.Left, VerticalContentAlignment = VerticalAlignment.Center,
            ToolTip = "형식별 일반단면도와 높이 구간 표 · 벽 뒤 조건(2008 도로옹벽 표준도) — 토질부 단면 값을 넣는 곳(이 도면에만).",
        };
        secBtn.Click += (_, __) => OpenSectionTable();
        secWall.Children.Add(secBtn);
        _exOn = new CheckBox
        {
            Content = "뒷채움 터파기",
            IsChecked = exDef.On,
            Margin = new Thickness(0, 2, 0, 6),
            ToolTip = "켜면 종단·횡단을 그릴 때 옹벽 앞면마다 단면 형식의 바닥(+ 여유폭)과 경사 법면으로 터파기면(옹벽터파기_DH · 숨김)을 만들어\n"
                    + "종단·횡단에 보이고 횡단 수량의 터파기 · 되메우기(옹벽 몸체는 뺌)에 넣습니다. 이 도면에만 적용됩니다.",
        };
        secWall.Children.Add(_exOn);
        _exOn.FontWeight = FontWeights.SemiBold;
        var exSub = new StackPanel();
        secWall.Children.Add(new Border { BorderBrush = DhBrand.Line, BorderThickness = new Thickness(2, 0, 0, 0), Margin = new Thickness(7, 0, 0, 6), Padding = new Thickness(10, 2, 0, 0), Child = exSub });
        var exRow = new WrapPanel { Margin = new Thickness(2, 0, 0, 6) };
        _exMargin = Cell(exRow, "여유폭(m)", exDef.Margin);
        ((FrameworkElement)_exMargin.Parent).ToolTip = "단면(기초 · 버림) 앞뒤 끝에서 터파기 바닥 끝까지의 작업 여유. 첫 값 0.5m — 국도건설공사 설계실무요령\n패널식은 앞쪽만(기초부만 판다).";
        var slopeCell = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 6, 4) };
        slopeCell.Children.Add(new TextBlock { Text = "경사", FontSize = 11, Foreground = DhBrand.Sub, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) });
        _exSlope = new ComboBox { Width = 140, Height = 26, VerticalContentAlignment = VerticalAlignment.Center };
        slopeCell.Children.Add(_exSlope);
        slopeCell.ToolTip = "터파기 법면 기울기.";
        exRow.Children.Add(slopeCell);
        exSub.Children.Add(exRow);
        var bRowEx = new WrapPanel { Margin = new Thickness(2, 0, 0, 6) };
        _exBenchOn = new CheckBox
        {
            Content = "법면 소단",
            IsChecked = exDef.BenchOn,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 4),
            ToolTip = "켜면 터파기 바닥에서 높이마다 법면에 소단을 둡니다(깊이가 높이보다 얕으면 생기지 않습니다).",
        };
        bRowEx.Children.Add(_exBenchOn);
        _exBenchH = Cell(bRowEx, "높이(m)", exDef.BenchH);
        _exBenchW = Cell(bRowEx, "폭(m)", exDef.BenchW);
        ((FrameworkElement)_exBenchH.Parent).ToolTip = "터파기 바닥에서 이 높이마다 소단. 첫 값 3m";
        ((FrameworkElement)_exBenchW.Parent).ToolTip = "소단 폭. 첫 값 1m";
        exSub.Children.Add(bRowEx);
        var lRow = new WrapPanel { Margin = new Thickness(2, 0, 0, 6) };
        _exTLean = Cell(lRow, "버림 두께(m)", exDef.TLean);
        _exELean = Cell(lRow, "버림 돌출(m)", exDef.ELean);
        ((FrameworkElement)_exTLean.Parent).ToolTip = "기초 아래 버림 콘크리트 두께. 첫 값 0.1m";
        ((FrameworkElement)_exELean.Parent).ToolTip = "버림이 기초 앞뒤로 나오는 폭. 첫 값 0.1m";
        exSub.Children.Add(lRow);
        var bkRow = new WrapPanel { Margin = new Thickness(2, 0, 0, 6) };
        _exRestore = new RadioButton
        {
            Content = "원상복구",
            GroupName = "DH_exBack",
            IsChecked = !exDef.BackOn,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 12, 4),
            ToolTip = "절토 옹벽 뒤 되메우기를 원지반 높이까지 — 배면사면을 만들지 않고 원지반을 그대로 살립니다(첫 값).",
        };
        _exBackOn = new RadioButton
        {
            Content = "배면사면",
            GroupName = "DH_exBack",
            IsChecked = exDef.BackOn,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 4),
            ToolTip = "절토 옹벽(맨 위 단) 뒤 되메우기 윗면 — 터파기 끝에서 이 경사로 내려오다 옹벽 꼭대기 높이에서 평탄(숨긴 면 옹벽배면_DH).\n그 위 원지반은 절토 · 아래는 되메우기로 셉니다.",
        };
        bkRow.Children.Add(_exRestore);
        bkRow.Children.Add(_exBackOn);
        _exBackSlope = Cell(bkRow, "경사 1:", exDef.BackSlope);
        ((FrameworkElement)_exBackSlope.Parent).ToolTip = "배면사면 경사(1:n). 첫 값 1:1.5";
        exSub.Children.Add(bkRow);
        FillSlopeItems(exDef.Slope);
        // ★★[JACK 1124] 고쳐도 바로 안 적음 — 그림만 따라가고 [적용]이 깜박(Edited)
        foreach (var b in new[] { _exMargin, _exTLean, _exELean, _exBenchH, _exBenchW, _exBackSlope })
            b.TextChanged += (_, __) => { RedrawExcav(); ShowExcavOnEdit(); Edited(); };
        _exOn.Checked += (_, __) => { SyncExcavLock(); RedrawExcav(); ShowExcavOnEdit(); Edited(); };
        _exOn.Unchecked += (_, __) => { SyncExcavLock(); RedrawExcav(); ShowExcavOnEdit(); Edited(); };
        _exBenchOn.Checked += (_, __) => { SyncExcavLock(); RedrawExcav(); ShowExcavOnEdit(); Edited(); };
        _exBenchOn.Unchecked += (_, __) => { SyncExcavLock(); RedrawExcav(); ShowExcavOnEdit(); Edited(); };
        _exBackOn.Checked += (_, __) => { SyncExcavLock(); RedrawExcav(); ShowExcavOnEdit(); Edited(); };
        _exBackOn.Unchecked += (_, __) => { SyncExcavLock(); RedrawExcav(); ShowExcavOnEdit(); Edited(); };
        _exSlope.SelectionChanged += (_, __) => { RedrawExcav(); ShowExcavOnEdit(); Edited(); };
        _exCutKind.SelectionChanged += (_, __) => { RedrawExcav(); if (!_loading) ShowSec(KindOf(_exCutKind), true, false); Edited(); };
        _exFillKind.SelectionChanged += (_, __) => { RedrawExcav(); if (!_loading) ShowSec(KindOf(_exFillKind), false, false); Edited(); };
        HookTop(_siteBody, _exCutKind, _exFillKind, road: false);

        // ══ [진입도로] — 열린 도로의 절토 · 성토 단면 형식(빈 값 = 계획부지와 같음) ══
        _roadBody = new StackPanel { Visibility = Visibility.Hidden };
        parts.Children.Add(_roadBody);
        _rdName = new TextBlock { FontSize = 12, Foreground = DhBrand.Ink, Margin = new Thickness(2, 0, 0, 6), TextTrimming = TextTrimming.CharacterEllipsis };
        _roadBody.Children.Add(_rdName);
        var rRow = new WrapPanel { Margin = new Thickness(2, 0, 0, 2) };
        _rdCutKind = RoadKindCell(rRow, "절토 단면");
        _rdFillKind = RoadKindCell(rRow, "성토 단면");
        _roadBody.Children.Add(rRow);
        _roadBody.Children.Add(new TextBlock { Text = "진입도로 횡단 표준 단면에 적용 · 수량 · 뒷채움 터파기 제외", FontSize = 11, Foreground = DhBrand.Sub, Margin = new Thickness(2, 4, 0, 4) });
        _rdCutKind.SelectionChanged += (_, __) => { if (_loading) return; ShowRoadSec(true); Edited(); };
        _rdFillKind.SelectionChanged += (_, __) => { if (_loading) return; ShowRoadSec(false); Edited(); };
        HookTop(_roadBody, _rdCutKind, _rdFillKind, road: true);

        // ── [적용] 결과 한 마디(«적용됨» · 틀린 값) — 높이 고정(글이 나고 들어도 창 높이 그대로) ──
        _applyMsg = new TextBlock { Height = 18, FontSize = 11, Foreground = DhBrand.Brand, Margin = new Thickness(2, 6, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
        body.Children.Add(_applyMsg);
        _msgTimer = new System.Windows.Threading.DispatcherTimer { Interval = System.TimeSpan.FromSeconds(2.5) };
        _msgTimer.Tick += (_, __) => { _msgTimer.Stop(); try { if (_applyMsg.Foreground == DhBrand.Brand) _applyMsg.Text = ""; } catch { } };

        // ★★[JACK 1124] 바닥 = [적용](기본 · 바뀐 값 없으면 꺼짐 · 있으면 빨강 깜박 — 종횡단도 [변경 설정 적용]과 같은 꼴) · [닫기](안 적은 값 버림 · Esc)
        var (apply, close) = DhBrand.FooterButtons("적용");
        _apply = apply;
        _apply.IsEnabled = false;
        _apply.ToolTip = "바꾼 값 저장 → 그려 둔 종단도 · 횡단도 · 진입도로 횡단도 다시";
        ToolTipService.SetShowOnDisabled(_apply, true);
        _apply.Click += (_, __) => Apply();
        close.Content = "닫기";
        close.ToolTip = "안 적은 값 버림 · Esc";
        close.Click += (_, __) => Close();
        DhBrand.Dress(this, "옹벽 설정", "계획부지 · 진입도로 절성토 옹벽 단면", body, _apply, close);
        Closed += (_, __) => { try { _msgTimer.Stop(); RoadPanel.Blink(_apply, false); } catch { } };

        FillExcav();
        FillRoad();
        ShowPart(road);
        SetDirty(false);
    }

    /// <summary>도킹창 단추([계획부지 수정] · [진입도로 수정] 탭) — 명령으로 띄운다(DHWALLSET · DHWALLSETROAD): [적용]이 그 명령 안에서 돈다.</summary>
    internal static void Open(bool road)
    {
        var doc = AcadApp.DocumentManager.MdiActiveDocument;
        if (doc == null) return;
        PickSession.Send(doc, road ? "DHWALLSETROAD" : "DHWALLSET");
    }

    /// <summary>창을 띄운다(명령 안 · 도면 없으면 안 띄움) — 닫힌 뒤 정지 창 그림을 도면 값으로.</summary>
    internal static void ShowIn(AcDoc doc, bool road)
    {
        try
        {
            var win = new WallSettingsWindow(doc, road);
            AcadApp.ShowModalWindow(win);
        }
        catch (System.Exception ex)
        {
            try { doc.Editor.WriteMessage("\n[옹벽 설정] 창 못 엶 — " + ex.Message); } catch { }
            try { DiagLog.Append("\n■ 옹벽 설정 창 실패 — " + ex + "\n"); } catch { }
        }
        GradingPalette.ReloadExcav();
    }

    // ══ [적용] ════════════════════════════════════════════════════════════

    /// <summary>칸을 고칠 때마다 — 적은 값과 다르면 [적용] 켬 + 빨강 깜박.</summary>
    private void Edited()
    {
        if (_loading || _apply == null || _applying) return;
        _msgTimer.Stop(); _applyMsg.Text = "";
        SetDirty(Dirty());
    }

    /// <summary>창 값이 적은 값과 다른가(틀린 칸도 «바뀜» — [적용]이 알려 줌).</summary>
    private bool Dirty()
    {
        if (!ExcavFromUi(out var s) || s != _exStored) return true;
        return _road != null && (RoadKindOf(_rdCutKind)?.ToString() != _rdStoredCut || RoadKindOf(_rdFillKind)?.ToString() != _rdStoredFill);
    }

    private void SetDirty(bool d)
    {
        if (_apply == null) return;
        _apply.IsEnabled = d && !_applying;
        try { if (DhBrand.Skin != null) _apply.Style = (Style)DhBrand.Skin[d ? "DhCaution" : "DhPrimary"]; } catch { }
        RoadPanel.Blink(_apply, d);
    }

    private void Msg(string t, bool warn)
    {
        _msgTimer.Stop();
        _applyMsg.Foreground = warn ? DhBrand.Warn : DhBrand.Brand;
        _applyMsg.Text = t;
        if (!warn && t.Length > 0) _msgTimer.Start();
    }

    /// <summary>[적용] — 칸 검사 → 바뀐 값만 담아 <see cref="WallSetCommand.Run"/>(이 창을 띄운 명령 안 = 문서 잠김 · 실행 취소 한 번) → 도면 값으로 다시 채움 · «적용됨».
    /// 명령 밖에서 띄운 창이면(드묾) 값을 넘기고 DHWALLSETAPPLY를 보낸 뒤 닫는다(모양 있는 창 아래에선 보낸 명령이 안 돎).</summary>
    internal void Apply()
    {
        if (_applying) return;
        var doc = _doc;
        if (doc == null || !DocAlive(doc)) { Msg("도면 닫힘", true); return; }
        if (!ExcavFromUi(out var s)) { ShowPart(false); Msg("빨간 칸 확인", true); return; }
        string? cut = _road != null ? RoadKindOf(_rdCutKind)?.ToString() : null, fill = _road != null ? RoadKindOf(_rdFillKind)?.ToString() : null;
        var p = new WallSetCommand.Pending(s, s != _exStored, _road?.AlignId ?? Autodesk.AutoCAD.DatabaseServices.ObjectId.Null, cut, fill,
                                           _road != null && (cut != _rdStoredCut || fill != _rdStoredFill));
        if (!p.SiteChanged && !p.RoadChanged) { SetDirty(false); return; }
        if (!Busy(doc)) { WallSetCommand.Queue(doc, p); Close(); return; }
        _applying = true;
        _apply.IsEnabled = false;
        RoadPanel.Blink(_apply, false);
        Msg("적용 중", false);
        try { Dispatcher.Invoke(System.Windows.Threading.DispatcherPriority.Render, new System.Action(() => { })); } catch { }
        bool ok = false; string res = "";
        try { res = WallSetCommand.Run(doc, p, out ok); }
        catch (System.Exception ex) { res = "실패 — " + ex.Message; try { DiagLog.Append("\n■ 옹벽 설정 적용 실패 — " + ex + "\n"); } catch { } }
        finally { _applying = false; }
        if (!DocAlive(doc)) return;
        FillExcav();
        FillRoad();
        if (_roadTab) ShowRoadSec(true); else ShowExcav();
        SetDirty(Dirty());
        Msg(ok ? "적용됨" : "⚠" + res, !ok);
    }

    /// <summary>탭 — 계획부지(거짓) · 진입도로(참). 그림 칸도 그 쪽으로.</summary>
    internal void ShowPart(bool road)
    {
        _roadTab = road;
        for (int i = 0; i < 2; i++)
        {
            bool on = (i == 1) == road;
            _tabs[i].Background = on ? DhBrand.Brand : DhBrand.BrandDim;
            ((TextBlock)_tabs[i].Child).Foreground = on ? Brushes.White : DhBrand.Brand;
        }
        _siteBody.Visibility = road ? Visibility.Hidden : Visibility.Visible;
        _roadBody.Visibility = road ? Visibility.Visible : Visibility.Hidden;
        if (road) ShowRoadSec(true); else ShowExcav();
    }

    // ══ 그림 ═════════════════════════════════════════════════════════════

    private static Viewbox Fit(Viewbox vb)
    {
        vb.Width = double.NaN; vb.Height = double.NaN; vb.MaxWidth = double.PositiveInfinity; vb.MaxHeight = double.PositiveInfinity;
        vb.Stretch = Stretch.Uniform; vb.StretchDirection = StretchDirection.Both;
        vb.HorizontalAlignment = HorizontalAlignment.Center; vb.VerticalAlignment = VerticalAlignment.Center;
        return vb;
    }

    private void ShowExcav()
    {
        _excavBox.Visibility = Visibility.Visible; _secBox.Visibility = Visibility.Collapsed;
        _cap.Text = "옹벽 — 형식 · 뒷채움 터파기 · 소단 · 배면사면 값이 그림에 바로 반영";
    }

    /// <summary>옹벽 칸 값이 바뀌면(손으로) 터파기 그림을 맨 위에 — 불러오는 중이면 건드리지 않는다.</summary>
    private void ShowExcavOnEdit() { if (!_loading) ShowExcav(); }

    /// <summary>★[v117.14.1 · JACK 1112] 마우스 · 커서 자리 → 그림: 절토 단면 칸 → 절토 형식 단면 · 성토 단면 칸 → 성토 형식 단면 · 그 밖 → 뒷채움 터파기(진입도로 쪽은 절토 단면).</summary>
    private void HookTop(FrameworkElement part, ComboBox cut, ComboBox fill, bool road)
    {
        string Where(object src)
        {
            for (var d = src as DependencyObject; d != null && !ReferenceEquals(d, part); d = d is Visual || d is System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
            {
                if (ReferenceEquals(d, cut.Parent)) return "cut";
                if (ReferenceEquals(d, fill.Parent)) return "fill";
            }
            return "else";
        }
        string _last = "";
        void Go(object src)
        {
            string w = cut.IsDropDownOpen ? "cut" : fill.IsDropDownOpen ? "fill" : Where(src);
            if (w == _last && (w != "else" || _excavBox.Visibility == Visibility.Visible || road)) return;
            _last = w;
            if (road) ShowRoadSec(w != "fill");
            else if (w == "else") ShowExcav();
            else ShowSec(KindOf(w == "cut" ? _exCutKind : _exFillKind), w == "cut", false);
        }
        part.MouseMove += (_, e) => Go(e.OriginalSource);
        part.GotKeyboardFocus += (_, e) => Go(e.OriginalSource);
    }

    /// <summary>진입도로 쪽 단면 — 고른 형식(«계획부지와 같음»이면 계획부지 칸 형식) · 도로 단높이에 가까운 표 줄.</summary>
    private void ShowRoadSec(bool cut)
    {
        var own = RoadKindOf(cut ? _rdCutKind : _rdFillKind);
        var k = own ?? KindOf(cut ? _exCutKind : _exFillKind);
        ShowSec(k, cut, true, own == null);
    }

    /// <summary>고른 형식의 일반단면 — 단높이(계획부지 = 사면 설정 · 진입도로 = 도로 사면 규칙)에 가까운 표 줄 · 값 치수(기호 아님).</summary>
    private void ShowSec(Kind k, bool cut, bool road, bool same = false)
    {
        try
        {
            var t = SecTable(k);
            if (t == null || t.Rows.Count == 0) { _canvasSec.Children.Clear(); }
            else
            {
                double want = road && _road != null ? (cut ? _road.Design.CutBenchH : _road.Design.FillBenchH)
                                                    : (cut ? GradingSettings.CutBenchHeight : GradingSettings.FillBenchHeight);
                if (!double.IsFinite(want) || want <= 0) want = 5;
                double[] row = t.Rows[0]; double best = double.MaxValue;
                foreach (var r in t.Rows)
                {
                    double hv = r[0] - DH.Grading.Core.WallSection.Col(k, r, "Df");
                    if (double.IsNaN(hv) || double.IsInfinity(hv)) continue;
                    double dd = System.Math.Abs(hv - want);
                    if (dd < best) { best = dd; row = r; }
                }
                double h = row[0] - DH.Grading.Core.WallSection.Col(k, row, "Df");
                if (double.IsNaN(h) || double.IsInfinity(h)) h = row[0];
                WallSectionDiagram.Draw(_canvasSec, new WallSectionDiagram.Spec(k, row, h, CommonNow(), MarginNow(), SlopeNow(), cut, Symbols: false, Cond: t.Cond), out _);
                _cap.Text = $"{(road ? "진입도로 " : "")}{(cut ? "절토" : "성토")} 옹벽 — {DH.Grading.Core.WallSection.Of(k).Name}{(same ? "(계획부지와 같음)" : "")} · 높이 {h:0.##} m 예";
            }
        }
        catch (System.Exception ex)
        {
            _canvasSec.Children.Clear();
            _cap.Text = (cut ? "절토" : "성토") + " 단면 그림 못 그림";
            try { DiagLog.Append("\n■ 옹벽 형식 단면 그림 실패 — " + ex.Message + "\n"); } catch { }
        }
        _excavBox.Visibility = Visibility.Collapsed; _secBox.Visibility = Visibility.Visible;
    }

    private DH.Grading.Core.WallSection.Common CommonNow()
    {
        var d = DH.Grading.Core.WallExcav.Settings.Default;
        return new DH.Grading.Core.WallSection.Common(P(_exTLean, d.TLean, 0, DH.Grading.Core.WallExcav.Settings.LeanMax, allowZero: true),
                                                      P(_exELean, d.ELean, 0, DH.Grading.Core.WallExcav.Settings.LeanMax, allowZero: true), 0.02, 0.01);
    }
    private double MarginNow() => P(_exMargin, DH.Grading.Core.WallExcav.Settings.Default.Margin, 0, DH.Grading.Core.WallExcav.Settings.MarginMax, allowZero: true);
    private double SlopeNow()
    {
        int i = _exSlope.SelectedIndex;
        return i >= 0 && i < DH.Grading.Core.WallExcav.Slopes.Length ? DH.Grading.Core.WallExcav.Slopes[i]
             : _exSlope.Tag is double t ? t : DH.Grading.Core.WallExcav.Settings.Default.Slope;
    }

    /// <summary>★[v103.3.1 · JACK 1021 «사면설정처럼 연동되는 설명»] 뒷채움 터파기 예시 — 칸 값(틀리면 첫 값)으로 다시 그린다 · ★[v105] 꺼짐이면 옹벽 · 땅만.
    /// 단높이 · 소단폭은 사면 설정 절토 값(종전 도킹창 칸 = 세션 값).</summary>
    private void RedrawExcav()
    {
        if (_canvasExcav == null || _exSlope == null) return;
        try
        {
            var d = DH.Grading.Core.WallExcav.Settings.Default;
            ExcavDiagram.Draw(_canvasExcav, new ExcavDiagram.Spec(
                On: _exOn.IsChecked == true,
                Table: SecTable(KindOf(_exCutKind)),
                Common: CommonNow(),
                Margin: MarginNow(),
                Slope: SlopeNow(),
                BenchOn: _exBenchOn.IsChecked == true,
                BenchH: P(_exBenchH, d.BenchH, DH.Grading.Core.WallExcav.Settings.BenchHMin, DH.Grading.Core.WallExcav.Settings.BenchHMax),
                BenchW: P(_exBenchW, d.BenchW, 0, DH.Grading.Core.WallExcav.Settings.BenchWMax, allowZero: true),
                BackOn: _exBackOn?.IsChecked == true,
                BackSlope: _exBackSlope == null ? d.BackSlope : P(_exBackSlope, d.BackSlope, 0.05, DH.Grading.Core.WallExcav.Settings.SlopeMax),
                StepH: Clamp(GradingSettings.CutBenchHeight, 5, 0.5, 20),
                StepW: Clamp(GradingSettings.CutBenchWidth, 1, 0, 10)));
        }
        catch { }
    }

    private static double Clamp(double v, double dflt, double min, double max) => double.IsFinite(v) && v >= 0 ? System.Math.Clamp(v, min, max) : dflt;

    // ══ 계획부지 값(종전 GradingPanel 그대로) ═══════════════════════════════

    /// <summary>경사 콤보 목록 — 대표 경사 다섯 + 기록에 목록 밖 값이 있으면 그 값 한 줄(«1:n(목록 밖)»).</summary>
    private void FillSlopeItems(double n)
    {
        bool was = _loading; _loading = true;
        try
        {
            _exSlope.Items.Clear();
            foreach (var s in DH.Grading.Core.WallExcav.SlopeNames) _exSlope.Items.Add(s);
            int i = DH.Grading.Core.WallExcav.SlopeIndex(n);
            if (i < 0) { _exSlope.Items.Add($"1:{N(n)}(목록 밖)"); i = _exSlope.Items.Count - 1; }
            _exSlope.SelectedIndex = i;
            _exSlope.Tag = n;
        }
        finally { _loading = was; }
    }

    /// <summary>꺼지면 칸 · 콤보를 잠근다(«안 쓰는 칸은 잠근다» — UI검토 0909).</summary>
    private void SyncExcavLock()
    {
        bool on = _exOn.IsChecked == true;
        _exMargin.IsEnabled = _exTLean.IsEnabled = _exELean.IsEnabled = _exSlope.IsEnabled = _exBenchOn.IsEnabled = on;
        _exBenchH.IsEnabled = _exBenchW.IsEnabled = on && _exBenchOn.IsChecked == true;
        _exBackOn.IsEnabled = _exRestore.IsEnabled = on;
        _exBackSlope.IsEnabled = on && _exBackOn.IsChecked == true;
    }

    /// <summary>화면 값 → 설정. 칸이 틀리면 그 칸을 물들이고 거짓(적지 않는다).</summary>
    private bool ExcavFromUi(out DH.Grading.Core.WallExcav.Settings s)
    {
        s = DH.Grading.Core.WallExcav.Settings.Default;
        bool okM = Gate(_exMargin, 0, DH.Grading.Core.WallExcav.Settings.MarginMax, out double margin);
        bool okT = Gate(_exTLean, 0, DH.Grading.Core.WallExcav.Settings.LeanMax, out double tl);
        bool okE = Gate(_exELean, 0, DH.Grading.Core.WallExcav.Settings.LeanMax, out double el);
        bool okK = Gate(_exBackSlope, 0.05, DH.Grading.Core.WallExcav.Settings.SlopeMax, out double bks);
        bool okBH = Gate(_exBenchH, DH.Grading.Core.WallExcav.Settings.BenchHMin, DH.Grading.Core.WallExcav.Settings.BenchHMax, out double bh);
        bool okBW = Gate(_exBenchW, 0, DH.Grading.Core.WallExcav.Settings.BenchWMax, out double bw);
        double n = SlopeNow();
        if (!(okM && okT && okE && okBH && okBW && okK)) return false;
        s = new DH.Grading.Core.WallExcav.Settings(_exOn.IsChecked == true, margin, n, _exBenchOn.IsChecked == true, bh, bw, tl, el, KindOf(_exCutKind), KindOf(_exFillKind),
                                                   _exBackOn.IsChecked == true, bks);
        return s.Valid(out _);
    }

    static bool DocAlive(AcDoc doc)
    {
        try { foreach (AcDoc d in AcadApp.DocumentManager) if (d == doc) return true; } catch { }
        return false;
    }

    /// <summary>그 도면 기록으로 칸을 채운다(못 읽으면 첫 값 + 명령창 ⚠).</summary>
    private void FillExcav()
    {
        var s = WallListStore.ExcavOf(_doc?.Database, out string why);
        _exStored = s;
        _loading = true;
        try
        {
            _exOn.IsChecked = s.On;
            _exMargin.Text = N(s.Margin);
            _exTLean.Text = N(s.TLean);
            _exELean.Text = N(s.ELean);
            _exCutKind.SelectedIndex = KindIndex(_exCutKind, s.CutKind);
            _exFillKind.SelectedIndex = KindIndex(_exFillKind, s.FillKind);
            _exBackOn.IsChecked = s.BackOn;
            _exRestore.IsChecked = !s.BackOn;   // 라디오는 거짓을 넣어도 짝이 켜지지 않는다 — 둘 다 넣는다
            _exBackSlope.Text = N(s.BackSlope);
            _secCache.Clear();
            _exBenchOn.IsChecked = s.BenchOn;
            _exBenchH.Text = N(s.BenchH);
            _exBenchW.Text = N(s.BenchW);
        }
        finally { _loading = false; }
        FillSlopeItems(s.Slope);
        foreach (var b in new[] { _exMargin, _exTLean, _exELean, _exBenchH, _exBenchW, _exBackSlope }) Gate(b, 0, 1e9, out _);   // 색만 되돌린다
        SyncExcavLock();
        RedrawExcav();
        if (why.Length > 0) Say("⚠옹벽 터파기 기록을 못 읽어 첫 값으로 보입니다 — " + why);
    }

    // ══ 진입도로 값 ═══════════════════════════════════════════════════════

    private void FillRoad()
    {
        bool on = _road != null;
        _rdCutKind.IsEnabled = _rdFillKind.IsEnabled = on;
        _rdName.Text = on ? "도로 " + _road!.Name : "열린 도로 없음 — 진입도로 창에서 도로 열기";
        _rdName.Foreground = on ? DhBrand.Ink : DhBrand.Sub;
        _loading = true;
        try
        {
            var d = _road?.Fresh();
            _rdCutKind.SelectedIndex = RoadKindIndex(_rdCutKind, d?.WallCutKind);
            _rdFillKind.SelectedIndex = RoadKindIndex(_rdFillKind, d?.WallFillKind);
            // 적은 값 = 칸이 보이는 값(모르는 이름 = «계획부지와 같음» — 형식을 고를 때 쓰는 규칙 RoadWallKinds와 같음)
            _rdStoredCut = RoadKindOf(_rdCutKind)?.ToString();
            _rdStoredFill = RoadKindOf(_rdFillKind)?.ToString();
        }
        finally { _loading = false; }
    }

    static bool Busy(AcDoc doc) { try { return !string.IsNullOrEmpty(doc.CommandInProgress); } catch { return false; } }

    /// <summary>진입도로 형식 칸 — 첫 줄 «계획부지와 같음»(null) + Core 보이는 형식. <c>Tag</c> = 줄마다 형식(null = 같음).</summary>
    private static ComboBox RoadKindCell(Panel parent, string label)
    {
        var cell = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 8, 4) };
        cell.Children.Add(new TextBlock { Text = label, FontSize = 11, Foreground = DhBrand.Sub, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) });
        var cb = new ComboBox { Width = 130, Height = 26, VerticalContentAlignment = VerticalAlignment.Center, Tag = new List<Kind?> { null } };
        cb.Items.Add("계획부지와 같음");
        foreach (var k in DH.Grading.Core.WallSection.Listed) { cb.Items.Add(KindLabel(k)); ((List<Kind?>)cb.Tag).Add(k.Id); }
        cb.SelectedIndex = 0;
        cb.ToolTip = "이 진입도로 옹벽 단면 형식 — «계획부지와 같음» = 계획부지 칸 형식.\n⚠ = 공공 표가 없는 형식(지반팀 값 필요).";
        cell.Children.Add(cb);
        parent.Children.Add(cell);
        return cb;
    }

    private static int RoadKindIndex(ComboBox cb, string? stored)
    {
        var l = (List<Kind?>)cb.Tag;
        if (string.IsNullOrEmpty(stored) || !System.Enum.TryParse<Kind>(stored, out var k)) return 0;
        int nListed = DH.Grading.Core.WallSection.Listed.Count + 1;
        while (l.Count > nListed && l[^1] != k) { l.RemoveAt(l.Count - 1); cb.Items.RemoveAt(cb.Items.Count - 1); }
        int i = l.IndexOf(k);
        if (i < 0) { l.Add(k); cb.Items.Add(KindLabel(DH.Grading.Core.WallSection.Of(k)) + " · 숨김"); i = l.Count - 1; }
        return i;
    }

    private static Kind? RoadKindOf(ComboBox cb)
    {
        var l = (List<Kind?>)cb.Tag;
        return l[System.Math.Clamp(cb.SelectedIndex, 0, l.Count - 1)];
    }

    // ══ 단면 형식(종전 GradingPanel 그대로) ════════════════════════════════

    /// <summary>단면 형식 콤보 칸 — 목록 = Core 보이는 형식 · 공공 표 없는 형식은 ⚠ · 콤보마다 형식 차례를 <c>Tag</c>에(숨긴 형식은 끝에 «숨김»).</summary>
    private static ComboBox KindCell(Panel parent, string label, Kind value)
    {
        var cell = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 8, 4) };
        cell.Children.Add(new TextBlock { Text = label, FontSize = 11, Foreground = DhBrand.Sub, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) });
        var cb = new ComboBox { Width = 104, Height = 26, VerticalContentAlignment = VerticalAlignment.Center, Tag = new List<Kind>() };
        foreach (var k in DH.Grading.Core.WallSection.Listed) { cb.Items.Add(KindLabel(k)); ((List<Kind>)cb.Tag).Add(k.Id); }
        cb.SelectedIndex = KindIndex(cb, value);
        cb.ToolTip = "터파기 바닥 · 폭과 횡단 수량(옹벽 몸체 공제)이 쓰는 옹벽 단면 형식 — 높이마다 표의 단면을 씁니다.\n⚠ = 공공 표가 없는 형식(지반팀 값 필요).";
        cell.Children.Add(cb);
        parent.Children.Add(cell);
        return cb;
    }

    private static string KindLabel(DH.Grading.Core.WallSection.Info k) => k.Name + (k.PublicTable ? "" : " ⚠");

    private static int KindIndex(ComboBox cb, Kind k)
    {
        var l = (List<Kind>)cb.Tag;
        int nListed = DH.Grading.Core.WallSection.Listed.Count;
        while (l.Count > nListed && l[^1] != k) { l.RemoveAt(l.Count - 1); cb.Items.RemoveAt(cb.Items.Count - 1); }
        int i = l.IndexOf(k);
        if (i < 0) { l.Add(k); cb.Items.Add(KindLabel(DH.Grading.Core.WallSection.Of(k)) + " · 숨김"); i = l.Count - 1; }
        return i;
    }

    private static Kind KindOf(ComboBox cb)
    {
        var l = (List<Kind>)cb.Tag;
        return l[System.Math.Clamp(cb.SelectedIndex, 0, l.Count - 1)];
    }

    /// <summary>그 형식의 도면 표(그림용 · 읽은 것은 기억) — 못 읽으면 첫 표.</summary>
    private DH.Grading.Core.WallSection.Table SecTable(Kind k)
    {
        if (!_secCache.TryGetValue(k, out var t)) _secCache[k] = t = WallListStore.SectionOf(_doc?.Database, k, out _);
        return t;
    }

    /// <summary>[단면 표] — 표 창(절토 단면 형식부터) · 저장은 그 도면에(문서 잠금 · 되읽어 견줌 · 로그 · 자가점검).</summary>
    private void OpenSectionTable()
    {
        var doc = _doc;
        if (doc == null) { Say("도면이 없어 단면 표를 열 수 없습니다."); return; }
        try
        {
            DH.Grading.Core.WallSection.Table Load(Kind k) => WallListStore.SectionOf(doc.Database, k, out _);
            string Save(IReadOnlyList<DH.Grading.Core.WallSection.Table> list)
            {
                if (!DocAlive(doc)) return "도면이 닫혔습니다";
                string err = "";
                try
                {
                    using (doc.LockDocument())
                    using (var tr = doc.Database.TransactionManager.StartTransaction())
                    {
                        foreach (var t in list) WallListStore.WriteSection(doc.Database, tr, t);
                        tr.Commit();
                    }
                }
                catch (System.Exception ex) { err = ex.GetType().Name + ": " + ex.Message; }
                var bad = list.Where(t => WallListStore.SectionOf(doc.Database, t.Kind, out _).Print != t.Print).Select(t => t.Kind.ToString()).ToList();
                if (err.Length == 0 && bad.Count > 0) err = "되읽은 표가 다릅니다(" + string.Join(",", bad) + ")";
                string names = string.Join(", ", list.Select(t => t.Kind));
                try { DiagLog.Append($"\n■ 옹벽 단면 표 저장 {names}{(err.Length > 0 ? " ⚠" + err : "")}\n"); } catch { }
                SelfCheckLog.Line("옹벽 단면 표", err.Length == 0, $"{names} · {string.Join(" / ", list.Select(t => $"{t.Kind} {t.Rows.Count}줄"))}{(err.Length > 0 ? " ⚠" + err : "")}", doc.Name);
                _secCache.Clear();
                return err;
            }
            var win = new WallSectionWindow(KindOf(_exCutKind), cut: true, Load, Save, CommonNow(), MarginNow(), SlopeNow());
            AcadApp.ShowModalWindow(win);
            if (win.Saved) { _secCache.Clear(); RedrawExcav(); Say("옹벽 단면 표를 저장했습니다 — 종단·횡단을 다시 그리면 반영됩니다."); }
        }
        catch (System.Exception ex) { Say("단면 표 창을 못 열었습니다 — " + ex.Message); try { DiagLog.Append("\n■ 단면 표 창 실패 — " + ex + "\n"); } catch { } }
    }

    // ══ 자잘한 것 ═════════════════════════════════════════════════════════

    /// <summary>명령창 한 줄 — 종전 정지 창과 같은 머리(«[계획부지 정지]»).</summary>
    private static void Say(string s)
    {
        try { AcadApp.DocumentManager.MdiActiveDocument?.Editor?.WriteMessage("\n[계획부지 정지] " + s.Replace("<b>", "").Replace("</b>", "")); }
        catch { }
    }

    private static string N(double v) => v.ToString(CultureInfo.InvariantCulture);

    private static double P(TextBox box, double dflt, double min, double max, bool allowZero = false)
    {
        string t = (box?.Text ?? "").Trim().Replace(',', '.');
        if (!double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out double v)) return dflt;
        if (v < 0 || (!allowZero && v <= 0)) return dflt;
        return System.Math.Clamp(v, min, max);
    }

    /// <summary>칸 하나를 재고, 틀리면 그 칸을 물들인다(종전 정지 창과 같음).</summary>
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

    /// <summary>칸 모양 — 종전 정지 창 칸과 같다(폭 40 · 패딩 2 · 높이 26 · 이름 11px).</summary>
    private static TextBox Cell(Panel parent, string label, double value)
    {
        var cell = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 6, 4) };
        cell.Children.Add(new TextBlock { Text = label, FontSize = 11, Foreground = DhBrand.Sub, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) });
        var box = new TextBox
        {
            Text = value.ToString(CultureInfo.InvariantCulture),
            Width = 40,
            Padding = new Thickness(2, 0, 2, 0),
            Height = 26,
            VerticalContentAlignment = VerticalAlignment.Center,
            HorizontalContentAlignment = HorizontalAlignment.Center,
        };
        cell.Children.Add(box);
        parent.Children.Add(cell);
        return box;
    }
}

/// <summary>★★[JACK 1124 «옹벽 설정 창 — 적용을 누르면 설정한 게 적용되고 다 업데이트» · «옹벽 설정 단추를 공통에도»] 옹벽 설정 명령 —
/// 창 열기(<c>DHWALLSET</c> = 계획부지 탭 · 리본 [공통] · 계획부지 수정 탭 / <c>DHWALLSETROAD</c> = 진입도로 탭 · 진입도로 수정 탭) ·
/// [적용] 본체(<see cref="Run"/> — 창을 띄운 명령 안에서 돎) · 명령 밖 창이 넘긴 값(<see cref="Queue"/> → <c>DHWALLSETAPPLY</c>).</summary>
public static class WallSetCommand
{
    /// <summary>[적용]이 넘기는 값 — 계획부지 설정 · 열린 도로(노선 Id) 절토 · 성토 형식(null = 계획부지와 같음) · 바뀐 쪽.</summary>
    internal sealed record Pending(DH.Grading.Core.WallExcav.Settings Site, bool SiteChanged, ObjectId RoadAlign, string? RoadCut, string? RoadFill, bool RoadChanged);

    private static Pending? _pending;

    /// <summary>명령 밖에서 띄운 창 — 값을 담아 두고 DHWALLSETAPPLY를 보냄(명령이 한 번 꺼내 씀).</summary>
    internal static void Queue(AcDoc doc, Pending p)
    {
        _pending = p;
        PickSession.Send(doc, "DHWALLSETAPPLY");
    }

    [CommandMethod("DHWALLSET")]
    public static void OpenSite()
    {
        var doc = AcadApp.DocumentManager.MdiActiveDocument;
        if (doc != null) WallSettingsWindow.ShowIn(doc, road: false);
    }

    [CommandMethod("DHWALLSETROAD")]
    public static void OpenRoad()
    {
        var doc = AcadApp.DocumentManager.MdiActiveDocument;
        if (doc != null) WallSettingsWindow.ShowIn(doc, road: true);
    }

    [CommandMethod("DHWALLSETAPPLY")]
    public static void ApplyQueued()
    {
        var p = _pending; _pending = null;
        var doc = AcadApp.DocumentManager.MdiActiveDocument;
        if (doc == null || p == null) return;
        try { Run(doc, p, out _); }
        catch (System.Exception ex)
        {
            try { doc.Editor.WriteMessage("\n[옹벽 설정] 적용 실패 — " + ex.Message); } catch { }
            try { DiagLog.Append("\n■ 옹벽 설정 적용 실패 — " + ex + "\n"); } catch { }
        }
    }

    /// <summary>[적용] 본체(명령 안 · 문서 잠김) — ① 바뀐 값 저장(계획부지 WALLEXCAV · 도로 설계 기록 — 되읽어 견줌) ②그려 둔 부지 종단도가 있으면 같은 자리에 다시
    /// (<see cref="Commands.ProfileCommand.Rebuild(AcDoc, out bool, string)"/> — 옹벽 터파기 기록도 새로) · 그 뒤 부지 횡단도가 있었으면 같은 자리에(<see cref="Commands.SheetSettingsCommand.Saved"/>와 같은 차례)
    /// ③ 열린 도로가 쓰는 형식(«계획부지와 같음» 포함)이 바뀌고 진입도로 횡단도가 있으면 같은 자리에 다시. 그린 것이 없으면 저장만. 명령창 한 줄 · 로그 · 자가점검.</summary>
    /// <returns>명령창 한 줄 내용(실패면 실패 까닭만).</returns>
    internal static string Run(AcDoc doc, Pending p, out bool ok)
    {
        var ed = doc.Editor; var db = doc.Database;
        var done = new List<string>(); var bad = new List<string>();
        var log = new System.Text.StringBuilder("\n■ 옹벽 설정 적용\n");
        GradingSettings.SyncToDocument(doc);
        var rs = RoadSession.Of(doc);
        if (rs != null && rs.AlignId != p.RoadAlign) rs = null;   // 창을 연 뒤 다른 도로를 열었음 — 그 도로 값은 안 건드림
        var k0 = RoadKinds(db, rs);

        // ① 저장
        bool siteSaved = false;
        if (p.SiteChanged)
        {
            siteSaved = WriteSite(doc, p.Site, out string e);
            if (siteSaved) done.Add("계획부지 저장"); else bad.Add("계획부지 저장 실패 — " + e);
        }
        if (p.RoadChanged)
        {
            if (rs == null) bad.Add("진입도로 닫힘 — 진입도로 값 안 적음");
            else if (WriteRoad(rs, p.RoadCut, p.RoadFill, out string e)) done.Add("진입도로 저장");
            else bad.Add("진입도로 저장 실패 — " + e);
        }
        var k1 = RoadKinds(db, rs);
        log.AppendLine($"  계획부지 {(p.SiteChanged ? p.Site.Text : "그대로")} · 진입도로 형식 {(k0 == null ? "도로 없음" : $"{k0} → {k1}")}");

        // ② 부지 종단도 · 횡단도(그려 둔 것만)
        if (siteSaved && Commands.ProfileCommand.ReadExistingRoute(db, out _, out _, out _))
        {
            bool hasX = Commands.SheetSettingsCommand.HasSiteXsec(db);
            bool pOk = Commands.ProfileCommand.Rebuild(doc, out bool started, "옹벽 설정");
            if (pOk) done.Add("종단도"); else bad.Add(started ? "종단도 다시 그리다 멈춤" : "종단도 못 그림");
            if (hasX && started)
            {
                bool xOk = false;
                BusyPopup.Show(Commands.ProfileCommand.BusyText);
                try { xOk = Commands.XsecViewCommand.Refresh(doc); }
                catch (System.Exception ex) { log.AppendLine("  횡단도 예외 — " + ex.Message); }
                finally { BusyPopup.Close(); }
                if (xOk) done.Add("횡단도"); else bad.Add("횡단도 못 그림 → 계획지표면 [횡단도]");
            }
            log.AppendLine($"  부지 종단 {(pOk ? "다시 그림" : started ? "멈춤" : "못 그림")} · 횡단도 {(hasX ? "있음" : "없음")}");
        }

        // ③ 진입도로 횡단도(쓰는 형식이 바뀐 때 · 그려 둔 것만)
        if (rs != null && k0 != k1)
        {
            var d = rs.Fresh();
            if (RoadSession.AnyAlive(db, d.XsecViews))
            {
                Point3d? at;
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    at = RoadSession.PlaceNow(tr, db, d.XsecAt, d.XsecViews, d.XsecViewAt);
                    tr.Commit();
                }
                string refuse = RoadXsec.Refusal(rs);
                if (refuse.Length > 0) bad.Add("진입도로 횡단도 — " + refuse);
                else if (at == null) bad.Add("진입도로 횡단도 자리 모름 → 진입도로 [횡단도]");
                else
                {
                    var xlog = new System.Text.StringBuilder();
                    string msg;
                    using (BusyPopup.Scope("횡단도를 같은 자리에 다시 그리는 중입니다.")) msg = rs.MakeXsec(at.Value, xlog);
                    try { DiagLog.Append("\n" + xlog); } catch { }
                    if (msg.StartsWith("⚠")) bad.Add("진입도로 횡단도 — " + msg.TrimStart('⚠'));
                    else done.Add("진입도로 횡단도");
                    log.AppendLine("  진입도로 횡단도 — " + (msg.Length > 200 ? msg[..200] : msg));
                }
                try { rs.Recompute(); } catch { }
            }
            else log.AppendLine("  진입도로 횡단도 없음 — 저장만");
        }

        ok = bad.Count == 0;
        string res = (done.Count > 0 ? string.Join(" · ", done) : "바뀐 것 없음") + (bad.Count > 0 ? " · ⚠" + string.Join(" · ", bad) : "");
        try { ed.WriteMessage("\n[옹벽 설정] 적용 — " + res); } catch { }
        log.AppendLine("  끝 — " + res);
        try { DiagLog.Append(log.ToString()); } catch { }
        SelfCheckLog.Line("옹벽 설정 적용", ok, res, doc.Name);
        GradingPalette.ReloadExcav();   // 정지 창 사면 그림의 옹벽 모양(형식)
        return ok ? res : string.Join(" · ", bad);
    }

    /// <summary>열린 도로가 지금 쓰는 절토 · 성토 형식(<see cref="RoadWallKinds.Of"/> — «계획부지와 같음»이면 계획부지 형식) · 도로 없으면 null.</summary>
    private static (Kind Cut, Kind Fill)? RoadKinds(Database db, RoadSession? rs)
    {
        if (rs == null) return null;
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            var k = RoadWallKinds.Of(tr, rs.AlignId);
            tr.Commit();
            return k;
        }
        catch { return null; }
    }

    /// <summary>계획부지 옹벽 터파기 설정 → 그 도면 기록(WALLEXCAV) · 되읽어 견줌 · 로그 · 자가점검(종전 바로 적기와 같은 길).</summary>
    private static bool WriteSite(AcDoc doc, DH.Grading.Core.WallExcav.Settings s, out string err)
    {
        err = "";
        try
        {
            using (doc.LockDocument())
            using (var tr = doc.Database.TransactionManager.StartTransaction())
            {
                WallListStore.WriteExcav(doc.Database, tr, s);
                tr.Commit();
            }
        }
        catch (System.Exception ex) { err = ex.GetType().Name + ": " + ex.Message; }
        var now = WallListStore.ExcavOf(doc.Database, out string why);
        bool ok = err.Length == 0 && now == s;
        try { DiagLog.Append($"\n■ 옹벽 터파기 설정 {s.Text}{(err.Length > 0 ? " ⚠못 적음 " + err : "")} · 도면에 적힌 값 {now.Text}{(why.Length > 0 ? " ⚠" + why : "")}\n"); } catch { }
        SelfCheckLog.Line("옹벽 터파기 설정", ok, $"{s.Text} · 도면에 적힌 값 {now.Text}{(err.Length > 0 ? " ⚠못 적음 " + err : "")}", doc.Name);
        if (!ok && err.Length == 0) err = "되읽은 값 다름";
        return ok;
    }

    /// <summary>도로 절토 · 성토 형식 → 도로 설계 기록(<see cref="RoadSession.SaveDesign"/> — 진입도로 창 다른 칸과 같은 길) · 되읽어 견줌 · 로그 · 자가점검.</summary>
    private static bool WriteRoad(RoadSession s, string? cut, string? fill, out string err)
    {
        err = "";
        var d = s.Fresh();
        if (d.WallCutKind == cut && d.WallFillKind == fill) return true;
        d.WallCutKind = cut; d.WallFillKind = fill;
        try { s.SaveDesign(); } catch (System.Exception ex) { err = ex.GetType().Name + ": " + ex.Message; }
        string? nowCut = null, nowFill = null;
        try
        {
            using var tr = s.Doc.Database.TransactionManager.StartTransaction();
            var back = RoadDesign.Read(tr, s.AlignId);
            tr.Commit();
            nowCut = back?.WallCutKind; nowFill = back?.WallFillKind;
        }
        catch (System.Exception ex) { if (err.Length == 0) err = "되읽기 " + ex.Message; }
        bool ok = err.Length == 0 && nowCut == cut && nowFill == fill;
        static string Txt(string? a, string? b) => $"절토 {a ?? "계획부지와 같음"} · 성토 {b ?? "계획부지와 같음"}";
        try { DiagLog.Append($"\n■ 진입도로 옹벽 형식 {s.Name} {Txt(cut, fill)}{(err.Length > 0 ? " ⚠" + err : "")} · 기록 {Txt(nowCut, nowFill)}\n"); } catch { }
        SelfCheckLog.Line("진입도로 옹벽 형식", ok, $"{s.Name} {Txt(cut, fill)} · 기록 {Txt(nowCut, nowFill)}{(err.Length > 0 ? " ⚠" + err : "")}", s.Doc.Name);
        if (!ok && err.Length == 0) err = "되읽은 값 다름";
        return ok;
    }
}
