using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;
using AcDoc = Autodesk.AutoCAD.ApplicationServices.Document;

namespace DH.Grading.Civil;

/// <summary>★★★[계획 6단계 · JACK 0908 <i>"구조물터파기 도킹창이 뜨고 도킹창 안에
/// 앞서 만들어진 원지반과 계획지표면을 고를 수 있는 선택박스를 놔줘. 그리고 선택박스를
/// 실시간으로 연동해서 해당 지표면을 선택할 때 도면상에 해당 지표면만 뜨게 해줘"</i>]
/// <b>구조물 터파기 — 도킹창.</b>
///
/// <para><b>JACK 인터뷰 확정(0908)</b>: 이 선택은 <b>보기와 기준면 둘 다</b>다 —
/// 고른 면이 화면에도 뜨고, 터파기를 <b>그 면부터</b> 재기 시작한다.</para>
///
/// <para>★<b>보기는 라디오에 안 매단다</b>(검토 0908). <see cref="ViewSurfaceCommand"/>는
/// 화면만 바꾸는 것이 아니라 <b>끈 레이어를 도면에 저장</b>하고 <c>VSCURRENT</c>를 전역으로 바꾼다.
/// 라디오를 옮길 때마다 그것이 돌면 안 된다 — <b>단추 한 번</b>으로 충분하다.</para>
///
/// <para><b>가시설·사면수정은 7단계다.</b> 옹벽과 가시설은 이 저장소에서 <b>이미 별개 개념</b>이라
/// (종단 막대 끝이 다르고 밴드 표기가 다르다) 겉이름만 바꾸면 <b>종단면도가 틀리게 그려진다</b>.
/// ★그래서 <b>칸을 아예 안 만든다</b> — 눌러도 아무 일 없는 단추가 제일 나쁘다(<c>d1fa6ee</c>).</para></summary>
internal sealed class ExcavPanel : UserControl
{
    private readonly RadioButton _basePlan, _baseGround;
    private readonly Button _viewBase, _viewAll, _pick, _pickGround, _build;
    private readonly TextBlock _pickWhat, _groundWhat, _status, _said, _baseHint;
    private readonly TextBox _slope;

    private bool _loading;

    /// <summary>지금 화면이 맞춰져 있는 도면 — <b>같은 도면이면 사람이 고른 것을 안 덮는다</b>.
    /// <para>★★★[검토 0909 · 치명] 종전엔 <see cref="SyncTo"/>가 <b>부를 때마다</b> 도면 기록으로
    /// 라디오를 덮어썼다. 기록이 없으면 <b>언제나 원지반</b>이라, [계획지표면]을 고른 뒤
    /// 도면 탭을 한 번 왕복하거나 창을 접었다 펴면 <b>아무 말 없이 원지반으로 돌아갔다</b> —
    /// 그리고 그대로 파면 성토 두께만큼 물량이 조용히 빠진다(§77 ①).
    /// 되감김이 <b>언제나 한 방향</b>이라 더 나쁘다.</para>
    /// <para>바로 옆 <see cref="GradingPanel"/>이 하루 전에 같은 것을 맞고 고쳤는데
    /// (<i>"사람이 친 것을 말없이 지우지 않는다"</i>) 그 규칙을 안 가져왔다.</para></summary>
    private object _syncedDoc;

    /// <summary>사람이 이 도면에서 라디오를 <b>직접 만졌는가</b> — 만졌으면 안 덮는다.</summary>
    private bool _userChoseBasis;

    internal ExcavPanel()
    {
        var root = new StackPanel { Margin = new Thickness(10, 4, 10, 10) };
        DhBrand.Apply(this);
        Background = DhBrand.Wall;

        var head = DhBrand.Header("구조물 터파기", "지하 구조물을 앉히려고 파는 자리",
                                  drag: null, onClose: null, logoHeight: 17, titleSize: 13,
                                  pad: new Thickness(0, 0, 0, 6));
        head.Background = Brushes.Transparent;

        _status = new TextBlock
        {
            Text = "① 기준면을 고르고 → ② 터파기선을 찍고 → ③ [터파기 지표면 생성].",
            FontSize = 11, Foreground = DhBrand.Sub, TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8),
        };
        _said = new TextBlock
        {
            FontSize = 11, Foreground = DhBrand.Brand, TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8), Visibility = System.Windows.Visibility.Collapsed,
        };

        // ── 1. 기준면 ─────────────────────────────────────────────────────
        GradingDialog.AddSection(root, "1. 터파기 기준면", "성토부를 어디서부터 팔지 — 절토부는 어느 쪽이든 계획면", first: true);
        _basePlan = GradingDialog.AddRadioPair(root, "기준면", "계획지표면", false, out _baseGround, "원지반",
            "계획지표면 = 계획고까지 성토·다짐한 뒤 팝니다(그만큼 깊어집니다).\n"
          + "원지반 = 먼저 파고 구조물을 세운 뒤 둘레를 성토합니다.\n"
          + "★절토부는 어느 쪽을 골라도 계획면부터입니다 — 원지반부터 재면 부지 절토와 이중 계상입니다.",
            out Panel bRow);
        _ = bRow;   // ★[검토] 지금은 안 쓴다 — 쓸 때는 <b>Panel</b>로 받는다(DockPanel이다)

        _baseHint = new TextBlock
        {
            FontSize = 11, Foreground = DhBrand.Sub, TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 6),
        };
        root.Children.Add(_baseHint);

        _viewBase = new Button { Content = "이 면만 보기", MinWidth = 110, Height = 28, Margin = new Thickness(0, 0, 0, 8) };
        _viewBase.HorizontalAlignment = HorizontalAlignment.Left;
        _viewBase.ToolTip = "고른 기준면만 도면에 남기고 나머지를 숨깁니다. [보기] 리본 단추와 같은 일입니다.";
        _viewBase.ToolTip = "고른 기준면만 도면에 남기고 나머지를 숨깁니다."
                          + "\n★이 상태는 <b>도면에 저장됩니다</b> — 되돌리려면 옆의 [전부 보기]를 누르세요.";
        _viewBase.Click += (_, __) => PickSession.Send(Doc, _basePlan.IsChecked == true ? "DHVIEWP" : "DHVIEWG");

        // ★★[검토 0909 · 높음] <b>되돌아올 길을 같이 둔다.</b>
        //   숨김은 레이어 OFF와 지표면 Visible로 <b>도면에 저장된다</b> — 저장하고 나가면 굳는다.
        //   창에 되돌리는 단추가 없으면 리본 [보기]를 아는 사람만 빠져나올 수 있다.
        _viewAll = new Button { Content = "전부 보기", MinWidth = 90, Height = 28, Margin = new Thickness(8, 0, 0, 8) };
        _viewAll.Click += (_, __) => PickSession.Send(Doc, "DHVIEWALL");

        var vRow = new StackPanel { Orientation = Orientation.Horizontal };
        vRow.Children.Add(_viewBase);
        vRow.Children.Add(_viewAll);
        root.Children.Add(vRow);

        // ── 2. 대상 ───────────────────────────────────────────────────────
        GradingDialog.AddSection(root, "2. 대상", "구조물 바닥 경계(계획고가 들어간 닫힌 폴리선)");
        var row = new DockPanel { Margin = new Thickness(0, 0, 0, 8), LastChildFill = true };
        _pick = new Button { Content = "터파기선 선택", MinWidth = 130, Height = 30 };
        _pick.Click += (_, __) => PickSession.Send(Doc, PickCommands.CmdExcav);
        DockPanel.SetDock(_pick, Dock.Left);
        row.Children.Add(_pick);
        _pickWhat = new TextBlock
        {
            Text = "○ 미선택", Margin = new Thickness(10, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center, Foreground = DhBrand.Sub,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        row.Children.Add(_pickWhat);
        root.Children.Add(row);

        // ★★★[검토 0909 · 치명] <b>원지반을 고를 길이 있어야 한다.</b>
        //   종전엔 <c>GradingSettings.LastGroundHandle</c> 하나에 기댔는데, 그 값은
        //   <b>도면이 바뀔 때마다 지워지고 도면에서 되살아나지 않는다</b> —
        //   즉 <b>어제 만든 도면을 열면 [생성]이 늘 "원지반을 못 찾았습니다"로 끝났다</b>.
        //   리본 명령은 이 자리에서 <b>물어본다</b>. 배관(<c>DHPICKGROUND</c>)은 이미 있었는데
        //   창에 단추가 없었을 뿐이다.
        var gRow = new DockPanel { Margin = new Thickness(0, 0, 0, 8), LastChildFill = true };
        _pickGround = new Button { Content = "원지반 선택", MinWidth = 130, Height = 30 };
        _pickGround.ToolTip = "비워 두면 마지막 정지에 쓴 지반을 자동으로 씁니다 — 못 찾으면 여기서 고르세요.";
        _pickGround.Click += (_, __) => PickSession.Send(Doc, PickCommands.CmdGround);
        DockPanel.SetDock(_pickGround, Dock.Left);
        gRow.Children.Add(_pickGround);
        _groundWhat = new TextBlock
        {
            Text = "○ 자동", Margin = new Thickness(10, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center, Foreground = DhBrand.Sub,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        gRow.Children.Add(_groundWhat);
        root.Children.Add(gRow);

        _build = new Button { Content = "터파기 지표면 생성", MinWidth = 150, Height = 32, Margin = new Thickness(0, 2, 0, 4) };
        try { if (DhBrand.Skin != null) _build.Style = (Style)DhBrand.Skin["DhPrimary"]; } catch { }
        _build.HorizontalAlignment = HorizontalAlignment.Left;
        _build.Click += (_, __) => PickSession.Send(Doc, PickCommands.CmdExcavBuild);
        root.Children.Add(_build);

        // ── 3. 굴착 ───────────────────────────────────────────────────────
        //   ★[JACK 0824] 터파기 제원은 <b>구배 하나뿐</b>이다 — <i>"단높이 설정은 필요 없어,
        //     어차피 구배로만 치는 거야."</i> 소단·대소단·옹벽형태는 여기 없다.
        GradingDialog.AddSection(root, "3. 굴착", "터파기 제원은 구배 하나입니다(단높이·소단은 안 씁니다)");
        _slope = GradingDialog.AddRow(root, "굴착 구배  1 :", Commands.ExcavCommand.Slope, "");
        _slope.ToolTip = "0을 넣으면 수직(가시설)입니다. 너무 작은 값은 자동으로 하한으로 올립니다.";
        root.Children.Add(new TextBlock
        {
            Text = "※ 사면형상은 터파기에서 직각만 씁니다 — 라운드·소단·옹벽형태는 여기 없습니다.",
            FontSize = 11, Foreground = DhBrand.Sub, TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 4),
        });
        root.Children.Add(new TextBlock
        {
            Text = "※ 가시설 구간 지정(사면수정)은 다음 판에 들어갑니다 — 지금은 구조물 전체 구배가"
                 + " 수직일 때만 가시설로 인식합니다.",
            FontSize = 11, Foreground = DhBrand.Warn, TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 4),
        });

        // 값이 바뀌면 세션에 바로 옮긴다 — 만들기가 그것을 읽는다.
        _slope.TextChanged += (_, __) => PushSlope();
        _basePlan.Checked += (_, __) => PushBasis();
        _baseGround.Checked += (_, __) => PushBasis();

        // ── 배치 ──────────────────────────────────────────────────────────
        var deck = new DockPanel { LastChildFill = true };
        var top = new StackPanel { Margin = new Thickness(10, 10, 10, 0) };
        top.Children.Add(head);
        top.Children.Add(_status);
        top.Children.Add(_said);
        DockPanel.SetDock(top, Dock.Top);
        deck.Children.Add(top);
        deck.Children.Add(new ScrollViewer
        {
            Content = root,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        });
        Content = deck;

        // ★[검토 0909] 붙었다 떨어졌다 하는 창이므로 <b>붙을 때마다</b> 다시 구독한다.
        void Wire() { try { PickSession.Changed -= Refresh; PickSession.Changed += Refresh; } catch { } }
        Loaded += (_, __) => { Wire(); Refresh(); };
        Unloaded += (_, __) => { try { PickSession.Changed -= Refresh; } catch { } };
        Wire();
    }

    private static AcDoc Doc => AcadApp.DocumentManager.MdiActiveDocument;

    /// <summary>도면이 바뀌었다 — 그 도면 값으로 맞춘다.</summary>
    internal void SyncTo(AcDoc doc)
    {
        if (doc == null) return;
        try { GradingSettings.SyncToDocument(doc); } catch { }

        bool newDoc = !ReferenceEquals(_syncedDoc, doc);
        int rec = ReadBasisFromDrawing(doc);          // −1이면 기록 없음
        _loading = true;
        try
        {
            _slope.Text = Commands.ExcavCommand.Slope.ToString(CultureInfo.InvariantCulture);

            // ★★★[검토 0909 · 치명] <b>덮는 경우를 좁힌다.</b>
            //   ① 도면이 <b>정말 바뀌었으면</b> 그 도면 값으로 맞춘다(맞는 동작).
            //   ② 같은 도면인데 사람이 라디오를 만졌으면 <b>안 덮는다</b>.
            //   ③ 기록이 <b>없으면</b>(첫 구조물) 아무것도 안 덮는다 —
            //      종전엔 이때 무조건 원지반으로 되돌려 놓았다.
            if (newDoc || (!_userChoseBasis && rec >= 0))
            {
                bool plan = rec == 1;
                if (rec >= 0)
                {
                    _basePlan.IsChecked = plan;
                    _baseGround.IsChecked = !plan;
                    Commands.ExcavCommand.Basis = plan
                        ? Core.CrossSectionArea.ExcavBase.Plan
                        : Core.CrossSectionArea.ExcavBase.Lower;
                }
                if (newDoc) _userChoseBasis = false;
            }
        }
        catch { }
        finally { _loading = false; }

        // ★덮었으면 <b>말한다</b> — 조용히 바꾸면 사람이 자기가 고른 줄 안다.
        if (newDoc && rec >= 0)
            Say($"이 도면은 <{(rec == 1 ? "계획지표면" : "원지반")}> 기준으로 기록돼 있어 그쪽으로 맞췄습니다.");
        _syncedDoc = doc;
        Refresh();
    }

    /// <summary>이 도면이 이미 쓰고 있는 기준면 — 없으면 −1.</summary>
    private static int ReadBasisFromDrawing(AcDoc doc)
    {
        try
        {
            using var tr = doc.Database.TransactionManager.StartTransaction();
            var recs = ExcavBundleStore.TryLoadAll(doc.Database, tr, out _, out bool tooNew);
            tr.Commit();
            if (tooNew || recs == null || recs.Count == 0) return -1;
            return recs[0].Base;
        }
        catch { return -1; }
    }

    /// <summary>구배가 <b>쓸 수 있는 값인가</b> — 아니면 만들기를 막는다(검토 0909 · 보통).
    /// <para>종전엔 못 읽으면 <b>조용히 버렸다</b> — 화면엔 친 글자가 있는데 실제로는 옛 값으로 팠다.
    /// 리본 쪽은 <i>"0~30 범위여야 합니다"</i>라고 말해 준다.</para></summary>
    private bool _slopeOk = true;

    private void PushSlope()
    {
        if (_loading) return;
        string t = (_slope.Text ?? "").Trim().Replace(',', '.');
        _slopeOk = double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out double v)
                && v >= 0 && v <= 30;
        if (_slopeOk) Commands.ExcavCommand.Slope = v;
        Refresh();
    }

    private void PushBasis()
    {
        if (_loading) return;
        _userChoseBasis = true;   // ★사람이 골랐다 — 이제 도면 기록이 덮지 않는다(치명 1)
        Commands.ExcavCommand.Basis = _basePlan.IsChecked == true
            ? Core.CrossSectionArea.ExcavBase.Plan
            : Core.CrossSectionArea.ExcavBase.Lower;
        Refresh();
    }

    /// <summary>★찍기 상태·기준면 상태를 화면에 옮긴다.</summary>
    internal void Refresh()
    {
        try
        {
            var doc = Doc;
            bool busy = PickSession.Busy;
            var s = doc == null ? null : PickSession.Peek(doc, PickSession.KeyExcav);
            bool has = s != null;
            _pickWhat.Text = has ? "● " + s.What : "○ 미선택";
            _pickWhat.Foreground = has ? DhBrand.Brand : DhBrand.Sub;

            // 계획지표면은 <b>정지를 먼저 해야</b> 고를 수 있다 — 없으면 그렇다고 말한다.
            bool hasPlan = doc != null && GradeStart.HasPrevious(doc.Database, out int nR) && nR > 0;
            _basePlan.IsEnabled = !busy && hasPlan;
            _baseGround.IsEnabled = !busy;

            // ★★★[검토 0909 · 치명] <b>자동으로 뒤집지 않는다.</b>
            //   종전엔 계획면이 안 보이면 라디오를 원지반으로 <b>넘겨 버렸다</b>.
            //   그러면 <see cref="Commands.ExcavCommand.DoExcav"/>는 "사용자가 원지반을 골랐다"고 믿고
            //   <b>기록된 계획면 의사를 지운 채</b> Base=0으로 굳힌다 —
            //   그 함수가 스스로 <i>"기록해 둔 기준면을 말없이 바꾸지 않습니다"</i>라고 적어 둔 바로 그것을
            //   <b>패널이 그 관문 앞에서 먼저 해 버린</b> 셈이다.
            //   ★게다가 계획면은 <b>잠깐 안 보일 수 있다</b> — [이어서] 정지가 도는 동안 개명된다.
            //   → 고를 수 없게만 하고 <b>선택은 그대로 둔다</b>. 진짜 판정은 만들 때 그 함수가 한다.
            bool wantPlan = _basePlan.IsChecked == true;
            _baseHint.Text = !hasPlan
                ? (wantPlan
                   ? "⚠계획지표면을 지금 못 찾았습니다 — 이대로 [생성]하면 멈춥니다([계획부지 정지]를 먼저 하세요)."
                   : "계획지표면이 아직 없습니다 — [계획부지 정지]를 먼저 하면 그 기준도 고를 수 있습니다.")
                : (wantPlan
                   ? "계획고까지 성토·다짐한 뒤 팝니다 — 성토부에서 그만큼 깊어집니다."
                   : "먼저 파고 구조물을 세운 뒤 둘레를 성토합니다.");
            _baseHint.Foreground = hasPlan ? DhBrand.Sub : DhBrand.Warn;

            var g = doc == null ? null : PickSession.Peek(doc, PickSession.KeyGround);
            _groundWhat.Text = g != null ? "● " + g.What : "○ 자동(마지막 정지에 쓴 지반)";
            _groundWhat.Foreground = g != null ? DhBrand.Brand : DhBrand.Sub;

            _pick.IsEnabled = !busy;
            _pickGround.IsEnabled = !busy;
            _viewBase.IsEnabled = !busy;
            _viewAll.IsEnabled = !busy;
            _build.IsEnabled = !busy && has && _slopeOk;
            if (!_slopeOk)
            {
                _baseHint.Text = "굴착 구배가 0~30 사이 숫자여야 합니다 — 고치면 [생성]이 켜집니다.";
                _baseHint.Foreground = DhBrand.Warn;
            }
        }
        catch (System.Exception ex)
        {
            try { DiagLog.Append("\n■ 터파기창 상태 갱신 실패 — " + ex.Message + "\n"); } catch { }
        }
    }

    internal void Say(string s)
    {
        _said.Text = s;
        _said.Visibility = System.Windows.Visibility.Visible;
    }
}
