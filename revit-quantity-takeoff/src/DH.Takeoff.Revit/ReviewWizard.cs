using System.Globalization;
using System.Windows;
using System.Windows.Interop;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using DH.Takeoff.Core;
using ExprState = DH.Takeoff.Revit.WizardLogic.ExprState;
using TextBox = System.Windows.Controls.TextBox; // Autodesk.Revit.UI.TextBox 와 충돌 회피

namespace DH.Takeoff.Revit;

/// <summary>검토 대상 1건(부재 + 안내 + 추천 산출식 + 현재 입력값).</summary>
public sealed class ReviewItem
{
    public ElementId Id = ElementId.InvalidElementId; // 대표 부재
    public List<ElementId> Members = new();           // 동일 형상 전원(대표 포함)
    public string Category = "";
    public string Subtitle = "";       // 제목 아래 한 줄(분류 · 유형 · Id)
    public string Reason = "";         // 예외 부재 모드: 자동으로 못 만든 사유
    public string Note = "";           // 덧붙이는 말(같은 모양 몇 개, 지금 칸에 적힌 식)
    public string AutoExpr = "";       // 애드인이 만든 추천 산출식 — [추천식 넣기] 버튼·기본값
    public string Expr = "";           // 사용자가 작성한 산출식 → DH_Formula 에 저장
    public Dictionary<string, double> Dims = new(); // 미터
    public string Code = "";
    public double ModelVolume;         // 모델 체적(m³) — 예외 부재 모드에서 식과 맞춰 본다
    public bool Glued;                 // 예외 부재 모드: 칸 이름이 기호 없이 숫자·괄호에 붙어 있다(저장하지 않는다)
}

/// <summary>
/// 비정형 부재 순환 검토 마법사(모델리스). 부재를 하나씩 단독 격리·확대하고,
/// [다음]으로 순환하며, 형상별 추천 산출식을 식 칸에 미리 넣어 보여준다.
/// 창 안에서 L1~W3·H·부재코드를 직접 입력하면 [다음]·[닫기] 때 Revit에 기록된다.
/// 규칙: L1·L2·L3 = 평면 북방향(세로/Y), W1·W2·W3 = 가로(X), H = 높이/두께(Z). 단위 m.
/// 화면(배치·모양)은 WizardView 가 그리고, 여기는 값의 뜻과 저장·측정 흐름을 맡는다.
/// </summary>
public sealed class ReviewWizard : Window
{
    private static readonly string[] DimNames = WizardView.DimNames;

    private static ReviewWizard? _current; // GC 방지 + 중복 방지

    private readonly List<ReviewItem> _items;
    private readonly ExternalEvent _evt;
    private readonly WizardHandler _handler;
    private readonly WizardView _view;
    private int _i;
    private bool _restoreRaised;
    private bool _suppressTextEvents; // 프로그램이 칸을 채우는 동안 TextChanged 무시
    private readonly nint _revitHwnd; // Revit 메인창 핸들(Esc 전달용 포그라운드 전환)

    private Dictionary<string, TextBox> _fields => _view.Fields;

    /// <summary>마법사 시작(이미 떠 있으면 닫고 새로). Revit API 컨텍스트에서 호출.</summary>
    public static void Launch(Document doc, IList<ElementId> ids, nint ownerHandle)
    {
        var items = BuildItems(doc, ids);
        if (items.Count == 0) return;
        _current?.Close();
        _current = new ReviewWizard(doc, items, ownerHandle, false);
        _current.Show();
    }

    /// <summary>
    /// 예외 부재 모드 — 산출식 자동입력이 못 만든 부재를 하나씩 띄운다.
    /// 식은 숫자로 저장하고([L1] 같은 칸 이름은 잰 값으로 바꿔서), 모델 체적과 맞는지 그 자리에서 보여준다.
    /// 치수 칸(L1~H)은 재는 데만 쓰고 모델에 쓰지 않는다. 닫을 때 겹침 공제를 돌리지 않는다.
    /// </summary>
    public static void LaunchNumeric(Document doc, IList<FormulaOutcome> exceptions, nint ownerHandle)
    {
        var items = NumericItems(doc, exceptions);
        if (items.Count == 0) return;

        _current?.Close();
        _current = new ReviewWizard(doc, items, ownerHandle, true);
        _current.Show();
    }

    /// <summary>
    /// 예외 부재 모드의 검토 항목 — 같은 유형·같은 크기·같은 체적이면 한 번만 묻고 함께 적는다. 추천식이 있으면 식 칸에 미리 넣는다.
    /// 수량산출 창의 [완료]가 띄우는 창(ExceptionReview)도 같은 항목을 쓴다.
    /// </summary>
    public static List<ReviewItem> NumericItems(Document doc, IList<FormulaOutcome> exceptions)
    {
        // 같은 유형·같은 크기·같은 체적이면 한 번만 묻고 함께 적는다.
        var groups = new Dictionary<string, ReviewItem>();
        var items = new List<ReviewItem>();
        foreach (var o in exceptions)
        {
            var el = doc.GetElement(o.Id);
            if (el == null) continue;
            // 같은 유형·같은 크기·같은 체적(여섯째 자리까지)이면 한 번만 묻고 함께 적는다. 판정은 부재마다 따로 계산해 적힌다.
            string key = ShapeKey(el) + "|" + Math.Round(o.Bim, 6).ToString(CultureInfo.InvariantCulture);
            if (groups.TryGetValue(key, out var same)) { same.Members.Add(o.Id); continue; }

            string made = o.Result.Formula; // 프로그램이 만든 식(검산이 어긋났어도 만들던 식이 남아 있다)
            // 전에 적혀 있던 글자는 숫자 식으로 계산될 때만 쓸 수 있다. 칸 이름([L1] 등)이 든 옛 식이나 메모 같은 글자를
            // 넣어 두면 저장할 수 없는 식이라 [저장·다음]으로 넘어가지 못한다.
            bool existingUsable = WizardLogic.NumericState(o.Existing, o.Bim, out _) is ExprState.Match or ExprState.Differs;
            var item = new ReviewItem
            {
                Id = o.Id,
                Members = new List<ElementId> { o.Id },
                Category = el.Category?.Name ?? "(분류없음)",
                Subtitle = $"{o.Category} · {o.TypeName} · Id {o.Id.Value}",
                // '다시 계산 필요'를 남긴 부재(결합으로 체적이 바뀌었는데 식을 새로 만들지 못함 — 「위치·부위 정하기」에 이어서 돈 산출식만 이렇게 넘긴다):
                // 까닭을 앞에 적는다 — 넣어 둔 식이 ±1% 안이라 초록으로 보여도 그대로 맞는 식이 아닐 수 있다.
                Reason = o.StaleLeft
                    ? "결합으로 체적이 바뀐 부재입니다 — 넣어 둔 식이 깎인 만큼을 아직 세고 있을 수 있습니다. 식을 확인해 다시 넣어 주세요. (" + o.Result.Reason + ")"
                    : o.Result.Reason,
                AutoExpr = made,
                // 추천식을 식 칸에 미리 넣는다. 모델과 다른 식은 빨갛게 보이고, 그대로 저장하려 하면 한 번 더 묻는다
                // (창을 그냥 닫으면 모델과 맞는 식만 적힌다). 추천식이 없으면 전에 적혀 있던 숫자 식을 넣어 고쳐 쓰게 한다.
                // 다만 '다시 계산 필요'를 남긴 부재는 넣어 둔 식을 식 칸에 미리 넣지 않는다(아래 쪽지로만 보인다): 미리 넣어 두면 창을 그냥 닫기만 해도
                // 그 식이 '모델과 맞는 식'으로 적혀 표시가 사라진다 — 사람이 직접 넣어야 확인한 것이다.
                Expr = made.Length > 0 ? made : existingUsable && !o.StaleLeft ? o.Existing : "",
                ModelVolume = o.Bim, // 판정(DH_판정)이 쓰는 것과 같은 BIM 물량과 맞춰 본다
            };
            // 식 칸에 넣지 않은 '지금 칸에 적힌 글자'가 있으면 보여 준다(안 보이는 채로 덮어쓰지 않게).
            if (o.Existing.Length > 0 && o.Existing != item.Expr) item.Note = "지금 칸에 적힌 식  " + o.Existing;
            groups[key] = item;
            items.Add(item);
        }
        foreach (var it in items.Where(it => it.Members.Count > 1))
            it.Note = (it.Note.Length > 0 ? it.Note + "\n" : "") + $"같은 모양 {it.Members.Count}개에 함께 적습니다.";
        return items;
    }

    private readonly bool _numeric; // 예외 부재 모드

    // 닫힌 뒤 마지막 저장·정리(ExternalEvent)가 돌 때까지 붙들어 두는 창들. 이게 없으면 그 전에 치워질 수 있다.
    private static readonly HashSet<ReviewWizard> _closing = new();

    private ReviewWizard(Document doc, List<ReviewItem> items, nint ownerHandle, bool numeric)
    {
        _items = items;
        _numeric = numeric;
        _revitHwnd = ownerHandle;
        _handler = new WizardHandler { TargetDoc = doc }; // 값은 항상 마법사를 띄운 이 문서에 적는다
        _evt = ExternalEvent.Create(_handler);
        // 문서가 닫히면 더 저장할 곳이 없다 — 창을 닫는다.
        _handler.OnDocumentGone = () => Dispatcher.Invoke(() => { _restoreRaised = true; Close(); });
        // 닫은 뒤에도 마지막 저장·정리가 끝날 때까지 이 창(이벤트·핸들러)을 붙들어 둔다.
        _handler.OnRestored = () => _closing.Remove(this);

        // Revit 화면 테마(밝게/어둡게)를 따른다. 못 읽으면 밝게.
        bool dark = false;
        try { dark = UIThemeManager.CurrentTheme == UITheme.Dark; } catch { }

        Title = _numeric ? "DH 예외 부재 — 산출식 확인" : "DH 비정형 부재 검토";
        Width = 448;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = 60; Top = 90;
        // 화면이 낮아도 아래 버튼 줄이 화면 밖으로 나가지 않게 한다(넘치는 내용은 창 안에서 스크롤된다).
        MaxHeight = Math.Max(420, SystemParameters.WorkArea.Height - Top - 12);
        _ = new WindowInteropHelper(this) { Owner = ownerHandle };
        if (dark) SourceInitialized += (_, _) => DarkTitleBar();

        _handler.OnPicked = OnPickResult;

        _view = new WizardView(_numeric, dark);
        _view.Prev += () => { if (_i > 0) GoTo(_i - 1, false); };
        _view.Next += () => { if (_i < _items.Count - 1) GoTo(_i + 1, false); else GoTo(_i, true); };
        _view.Skip += () => { if (_i < _items.Count - 1) GoTo(_i + 1, false, save: false); else GoTo(_i, true, save: false); };
        _view.SaveClose += () => GoTo(_i, true);
        _view.Recommend += () => _view.SetExpr(_items[_i].AutoExpr);
        _view.Measure += StartPick;
        _view.DimChanged += OnDimTextChanged;
        _view.ExprChanged += UpdatePreview;
        Background = _view.Background;
        Content = _view.Root;
        Closed += OnClosed;

        // 첫 부재: 기록 없이 단독 격리만
        LoadFields(_items[0]);
        RefreshTexts();
        _handler.Restore = false; _handler.IsolateTarget = _items[0].Id;
        _evt.Raise();
    }

    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint hwnd, int attr, ref int value, int size);

    // 어두운 테마일 때 창 제목 줄도 어둡게(안 되는 윈도우에서는 그냥 둔다).
    private void DarkTitleBar()
    {
        try
        {
            int on = 1;
            DwmSetWindowAttribute(new WindowInteropHelper(this).Handle, 20 /* DWMWA_USE_IMMERSIVE_DARK_MODE */, ref on, sizeof(int));
        }
        catch { }
    }

    // --- 네비게이션: 현재 값 저장 + 대상 격리 (1회 Raise) ---
    // save=false 는 [건너뛰기]: 이 부재는 아무것도 적지 않고 넘어간다(고치던 글자는 창 안에만 남는다).
    private void GoTo(int newIndex, bool closing, bool save = true)
    {
        CaptureCurrent(substitute: save);
        if (save)
        {
            if (_numeric && !ConfirmNumeric(_items[_i])) return; // 식을 더 고치도록 이 부재에 머문다
            StageWrite(_items[_i]);
        }

        if (closing)
        {
            _handler.Restore = true;
            _handler.IsolateTarget = null;
            _handler.RunDeductionOnClose = !_numeric; // 마법사 종료 후 겹침 공제 자동(예외 부재 모드는 안 함)
            _restoreRaised = true;
            _closing.Add(this);
            CancelPickIfMeasuring();
            _evt.Raise();
            Close();
            return;
        }

        // 측정 중에 넘기면 측정부터 끝낸다 — 안 그러면 화면은 앞 부재에 머문 채, 이어서 찍는 모서리 길이가 다음 부재의 칸에 들어간다.
        CancelPickIfMeasuring();
        _i = newIndex;
        var nxt = _items[_i];
        _handler.Restore = false;
        _handler.IsolateTarget = nxt.Id;
        _evt.Raise();

        LoadFields(nxt);
        RefreshTexts();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        if (!_restoreRaised)
        {
            try { CaptureCurrent(); } catch { }
            // 창을 그냥 닫을 때는 물어볼 수 없으니, 모델 체적과 맞는 식만 적는다.
            // 손대지 않은 추천식은 맞아 보여도 적지 않는다 — 프로그램이 스스로 검산에서 떨어뜨린 식이라 사람이 확인한 것이 아니다.
            var cur = _items[_i];
            if (_numeric && (NumericState(cur) != ExprState.Match || WizardLogic.IsUntouchedRecommendation(cur.Expr, cur.AutoExpr)))
                cur.Expr = "";
            StageWrite(_items[_i]);
            _handler.Restore = true;
            _handler.IsolateTarget = null;
            _handler.RunDeductionOnClose = !_numeric; // 마법사 종료 후 겹침 공제 자동(예외 부재 모드는 안 함)
            _restoreRaised = true;
            _closing.Add(this);
            CancelPickIfMeasuring();
            try { _evt.Raise(); } catch { }
        }
        _current = null;
    }

    // 이번에 모델에 쓸 것을 핸들러에 쌓는다(다음 Raise 때 적힌다).
    private void StageWrite(ReviewItem cur)
    {
        if (_numeric)
        {
            // 식만 쓴다(비어 있으면 아무것도 쓰지 않아 기존 값을 지우지 않는다). 치수 칸·코드는 건드리지 않는다.
            _handler.Stage(cur.Members, null, null, string.IsNullOrWhiteSpace(cur.Expr) ? null : cur.Expr);
            return;
        }
        _handler.Stage(cur.Members, cur.Dims, cur.Code, cur.Expr);
    }

    /// <summary>칸 이름 → 그 칸에 지금 든 글자(그런 칸이 없으면 null).</summary>
    private string? FieldText(string name) => _fields.TryGetValue(name, out var tb) ? tb.Text : null;

    /// <summary>예외 부재 모드: 저장될 식 글자(칸 이름을 숫자로 바꾼 뒤)가 어떤 상태인가.</summary>
    private static ExprState NumericState(ReviewItem it) =>
        it.Glued ? ExprState.Glued : WizardLogic.NumericState(it.Expr, it.ModelVolume, out _);

    /// <summary>저장해도 되는지. 모델과 다른 식은 사람이 알고 넣는 것인지 한 번 묻는다(경사진 덩어리의 약산식 등).</summary>
    private bool ConfirmNumeric(ReviewItem it)
    {
        double v = 0;
        switch (it.Glued ? ExprState.Glued : WizardLogic.NumericState(it.Expr, it.ModelVolume, out v))
        {
            case ExprState.Empty:
                return true;
            case ExprState.Match:
                // 손대지 않은 추천식은 프로그램이 스스로 검산에서 떨어뜨린 식이다. 물량이 같아 보여도 한 번 묻는다.
                if (!WizardLogic.IsUntouchedRecommendation(it.Expr, it.AutoExpr)) return true;
                return MessageBox.Show(this, WizardLogic.ConfirmUnverifiedText(v, it.ModelVolume, it.Reason),
                    Title, MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes;
            case ExprState.Glued:
                MessageBox.Show(this, "칸 이름 앞뒤에 ×, + 같은 기호가 빠졌습니다.\n예) 2*[L1], [L1]*[W1]",
                    Title, MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            case ExprState.Unresolved:
                MessageBox.Show(this, "값이 없는 칸 이름([L1] 등)이 식에 남아 있습니다.\n그 칸을 재거나 숫자로 바꿔 주세요.",
                    Title, MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            case ExprState.Invalid:
                MessageBox.Show(this, "식을 계산할 수 없습니다. 괄호와 기호를 확인해 주세요.",
                    Title, MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            default:
                return MessageBox.Show(this, WizardLogic.ConfirmText(v, it.ModelVolume),
                    Title, MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes;
        }
    }

    // 입력칸 → 현재 항목에 반영(저장 직전).
    // substitute=false([건너뛰기]): 식 글자를 숫자로 바꾸지 않고 적힌 그대로 둔다 — 돌아왔을 때 사용자가 쓰던 글자가 보이게.
    private void CaptureCurrent(bool substitute = true)
    {
        var it = _items[_i];
        var d = new Dictionary<string, double>();
        foreach (var kv in _fields)
            if (double.TryParse(kv.Value.Text.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var m) && m > 0)
                d[kv.Key] = Math.Round(m, 4);
        it.Dims = d;
        it.Code = _view.Code.Text.Trim();
        it.Expr = _numeric && substitute ? WizardLogic.Substituted(_view.Expr.Text.Trim(), FieldText) : _view.Expr.Text.Trim();
        it.Glued = _numeric && WizardLogic.GluedRx.IsMatch(_view.Expr.Text);
    }

    // 현재 항목 값 → 입력칸
    private void LoadFields(ReviewItem it)
    {
        _suppressTextEvents = true; // 네비게이션 중 빈 칸 설정이 라벨 삭제를 부르지 않게
        foreach (var name in DimNames)
            _fields[name].Text = it.Dims.TryGetValue(name, out var v) && v > 0
                ? v.ToString("0.####", CultureInfo.InvariantCulture) : "";
        _view.Code.Text = it.Code;
        _view.Expr.Text = it.Expr;
        _suppressTextEvents = false;
    }

    private void RefreshTexts()
    {
        var it = _items[_i];
        _view.SetHeader(_numeric ? "예외 부재" : "비정형 부재", _i, _items.Count, it.Subtitle);
        _view.SetReason(_numeric ? it.Reason : "");
        _view.SetNote(it.Note);
        _view.SetRecommend(it.AutoExpr.Length > 0, it.AutoExpr);
        _view.SetNav(_i > 0, _i == _items.Count - 1);
        // 예외 부재 모드에서 추천식이 있으면 치수 재기는 접어 둔다(잴 일이 드물다). 사용자가 직접 여닫았으면 그대로 둔다.
        _view.SuggestDimsOpen(!_numeric || it.AutoExpr.Length == 0);
        UpdatePreview(); // 식 칸이 그대로여도 모델 체적은 부재마다 다르다
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, nuint dwExtraInfo);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(nint hWnd);
    private const byte VK_ESCAPE = 0x1B;

    // 3D 측정 시작: 진행 중이면 Esc를 보내 현재 측정을 끝낸 뒤(값은 이미 실시간 반영됨) 새 칸 측정 시작
    private void StartPick(string field)
    {
        CancelPickIfMeasuring(); // 다른 칸 측정 중 → 자동 종료(Esc)
        // 보여줄 부재(IsolateTarget)는 그대로 둔다 — 아직 안 띄운 부재가 있으면 핸들러가 측정보다 먼저 띄운다.
        _handler.PickField = field;
        _evt.Raise();
    }

    // 측정 중이면 Esc를 보내 PickObject 를 끝낸다(값은 이미 실시간으로 칸에 들어가 있다).
    private void CancelPickIfMeasuring()
    {
        if (!_handler.Measuring) return;
        // ★ 버튼 클릭으로 포커스가 마법사 창에 있으면 Esc가 PickObject(3D뷰)로 가지 않는다.
        //    Revit 메인창을 먼저 포그라운드로 돌려 Esc가 현재 측정 루프를 취소하게 한다.
        if (_revitHwnd != 0) SetForegroundWindow(_revitHwnd);
        keybd_event(VK_ESCAPE, 0, 0, 0);
        keybd_event(VK_ESCAPE, 0, 2, 0); // KEYEVENTF_KEYUP
    }

    // 사용자가 칸 값을 직접 비우면 → 그 칸의 임시 라벨도 삭제(측정/네비게이션 등 프로그램 변경은 무시) + 수량 미리보기 갱신
    private void OnDimTextChanged(string field, TextBox tb)
    {
        if (!_suppressTextEvents && !_handler.Measuring && string.IsNullOrWhiteSpace(tb.Text))
        {
            _handler.ClearLabelField = field;
            try { _evt.Raise(); } catch { }
        }
        UpdatePreview();
    }

    // 측정 결과(거리 m)를 해당 칸에 기입(저장은 [저장·다음]에서)
    private void OnPickResult(string field, double meters)
    {
        Dispatcher.Invoke(() =>
        {
            if (_fields.TryGetValue(field, out var tb))
            {
                _suppressTextEvents = true;
                tb.Text = meters.ToString("0.####", CultureInfo.InvariantCulture);
                _suppressTextEvents = false;
            }
            UpdatePreview();
        });
    }

    /// <summary>현재 산출식을 즉시 계산해 상태 줄에 보여 준다(예외 부재 모드: 모델 체적과 맞는지 / 비정형 모드: 수량).</summary>
    private void UpdatePreview()
    {
        if (_i < 0 || _i >= _items.Count) return;
        try // 글자 입력 중에 나는 어떤 오류도 Revit 으로 새지 않게
        {
            var it = _items[_i];
            string raw = _view.Expr.Text ?? "";
            _view.SetCaption(WizardLogic.Caption(_numeric, it.AutoExpr, raw));
            // 예외 부재 모드는 저장될 글자 그대로(칸 이름 → mm로 반올림한 숫자)를 모델 체적과 맞춰 본다.
            var st = _numeric ? WizardLogic.NumericStatus(raw, FieldText, it.ModelVolume, it.AutoExpr) : WizardLogic.TokenStatus(raw, FieldText);
            _view.SetStatus(st.Tone, st.Head, st.Detail);
        }
        catch
        {
            _view.SetStatus(WizardView.Tone.Warn, "식 확인 필요");
        }
    }

    // --- 검토 항목 구성: 동일 형상끼리 묶어 대표 1건만 + 추천 산출식 + 현재 입력값 스냅샷 ---
    private static List<ReviewItem> BuildItems(Document doc, IList<ElementId> ids)
    {
        // 형상키(카테고리|타입|경계상자 치수)별로 그룹핑 — 입력 순서 보존
        var groups = new Dictionary<string, ReviewItem>();
        var order = new List<string>();

        foreach (var id in ids)
        {
            var el = doc.GetElement(id);
            if (el == null) continue;
            string key = ShapeKey(el);

            if (groups.TryGetValue(key, out var item))
            {
                item.Members.Add(id);
                continue;
            }

            string cat = el.Category?.Name ?? "(분류없음)";
            string code = el.LookupParameter("DH_ElementCode")?.AsString() ?? "";
            string expr = el.LookupParameter("DH_Formula")?.AsString() ?? "";
            var dims = new Dictionary<string, double>();
            foreach (var name in DimNames)
            {
                double v = DimensionExtractor.ReadMeters(el, name);
                if (v > 0) dims[name] = v;
            }

            // 개구부 자동 감지 → 값-포함 산출식 생성(개수 제한 없음). 예) [L1]*[W1]*[H] - (0.6*0.6+0.8*1.0)*[H]
            string autoExpr = BuildAutoExpr(cat, OpeningFinder.OpeningTerm(el));
            if (string.IsNullOrWhiteSpace(expr)) expr = autoExpr;

            var ni = new ReviewItem
            {
                Id = id,
                Members = new List<ElementId> { id },
                Category = cat,
                AutoExpr = autoExpr,  // 추천식 = 실제 생성된 값-포함 산출식
                Expr = expr,
                Dims = dims,
                Code = code,
            };
            groups[key] = ni;
            order.Add(key);
        }

        var list = new List<ReviewItem>();
        foreach (var key in order)
        {
            var it = groups[key];
            int n = it.Members.Count;
            it.Subtitle = n > 1
                ? $"{it.Category} · 같은 형상 {n}개 일괄 적용 (대표 Id {it.Id.Value})"
                : $"{it.Category} · Id {it.Id.Value}";
            // 칸에 이미 다른 식이 들어 있으면 추천식이 안 보인다 — 한 줄로 보여 준다.
            if (it.AutoExpr.Length > 0 && it.Expr != it.AutoExpr) it.Note = "추천식  " + it.AutoExpr;
            list.Add(it);
        }
        return list;
    }

    /// <summary>동일 형상 판별 키 — 카테고리 + 타입 + 경계상자 치수(mm 반올림).</summary>
    private static string ShapeKey(Element el)
    {
        string type = el is FamilyInstance fi
            ? (fi.Symbol?.Id.Value.ToString() ?? "")
            : el.GetTypeId().Value.ToString();

        string dim = "";
        var bb = el.get_BoundingBox(null);
        if (bb != null)
        {
            double dx = Math.Round((bb.Max.X - bb.Min.X) * 0.3048, 3);
            double dy = Math.Round((bb.Max.Y - bb.Min.Y) * 0.3048, 3);
            double dz = Math.Round((bb.Max.Z - bb.Min.Z) * 0.3048, 3);
            dim = $"{dx}x{dy}x{dz}";
        }
        return $"{el.Category?.Id.Value}|{type}|{dim}";
    }

    /// <summary>부재 외형(파라미터) 산출식.</summary>
    private static string MainExpr(string category)
    {
        if (category.Contains("벽")) return "[L1]*[H]*[W1]";          // 길이×높이×두께
        return "[L1]*[W1]*[H]";                                       // 슬래브·기초·기둥·보 등
    }

    /// <summary>개구부 깊이로 곱할 두께 토큰 — 벽=[W1](벽두께), 그 외=[H](부재두께).</summary>
    private static string ThickToken(string category) => category.Contains("벽") ? "[W1]" : "[H]";

    /// <summary>외형 산출식 + (개구부 값항)×두께 자동 차감. 개구부 없으면 외형만.</summary>
    private static string BuildAutoExpr(string category, string? openingTerm)
    {
        string main = MainExpr(category);
        if (string.IsNullOrEmpty(openingTerm)) return main;
        return $"{main} - {openingTerm}*{ThickToken(category)}";
    }
}
