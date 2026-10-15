using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Autodesk.Revit.DB;
using DH.Takeoff.Core;
using ExprState = DH.Takeoff.Revit.WizardLogic.ExprState;

namespace DH.Takeoff.Revit;

/// <summary>
/// 수량산출 창의 [완료] 때 산출식을 못 만든 부재를 하나씩 띄워 식을 넣는 창(JACK 2026-10-15 인터뷰: «하나씩 띄워 직접 입력 후 엑셀»).
/// 수량산출 창 위에 모달로 뜬다. 전의 검토 마법사(ReviewWizard)는 모델리스 + 외부 이벤트라 저장이 명령이 끝난 뒤로 밀린다 —
/// 수량산출 창이 떠 있는 동안에는 그 길을 쓸 수 없어, 여기서는 [저장·다음]을 누를 때 그 자리에서 트랜잭션으로 적는다
/// (수량산출 창의 트랜잭션 그룹 안 — Ctrl+Z 한 번에 같이 돌아간다).
///  · 항목 · 판정 규칙은 검토 마법사와 같다: 같은 모양은 한 번만 묻는다 · 모델과 다른 식은 한 번 묻는다 · 손대지 않은 추천식도 묻는다 ·
///    창을 그냥 닫으면 모델과 맞는 식만 적는다(ReviewWizard.NumericItems · WizardLogic).
///  · Revit 3D 뷰에서 모서리를 재는 [측정]은 쓸 수 없어(창이 떠 있는 동안 Revit 이 다른 일을 받지 않는다) 치수 칸을 감춘다.
///    부재는 수량산출 창의 3D 에서 그 부재를 고른 채 가운데에 보여 준다(focus).
/// </summary>
public sealed class ExceptionReview
{
    /// <summary>이 창에서 한 일.</summary>
    public sealed class Outcome
    {
        public int Items;                    // 띄운 항목 수(같은 모양은 하나)
        public int Elements;                 // 그 항목에 든 부재 수
        public int Saved;                    // 식을 적은 항목 수
        public int Commits;                  // 커밋한 트랜잭션 수(모델에 남은 것이 있는가)
        public List<string> Failed = new();  // 적지 못한 까닭(부재마다)
    }

    private const string Title = "DH 수량산출 — 산출식 확인";

    private readonly Document _doc;
    private readonly List<ReviewItem> _items;
    private readonly WizardView _view;
    private readonly Window _window;
    private readonly Action<ElementId>? _focus;
    private readonly Outcome _outcome = new();
    private readonly HashSet<int> _saved = new();
    private int _i;
    private bool _done;

    /// <summary>창을 띄우고 닫힐 때까지 기다린다(못 만든 부재가 없으면 띄우지 않는다).</summary>
    /// <param name="focus">그 부재를 수량산출 창의 3D 에서 보여 준다(InvalidElementId = 풀기).</param>
    public static Outcome Run(Window owner, Document doc, IList<FormulaOutcome> exceptions, bool dark, Action<ElementId>? focus)
    {
        var items = ReviewWizard.NumericItems(doc, exceptions);
        var review = new ExceptionReview(owner, doc, items, dark, focus);
        if (items.Count == 0) return review._outcome;
        try { review._window.ShowDialog(); }
        finally
        {
            try { focus?.Invoke(ElementId.InvalidElementId); } catch { }
        }
        review._outcome.Saved = review._saved.Count;
        return review._outcome;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint hwnd, int attr, ref int value, int size);

    private ExceptionReview(Window owner, Document doc, List<ReviewItem> items, bool dark, Action<ElementId>? focus)
    {
        _doc = doc;
        _items = items;
        _focus = focus;
        _outcome.Items = items.Count;
        _outcome.Elements = items.Sum(i => i.Members.Count);
        _view = new WizardView(true, dark, measure: false);
        _window = new Window
        {
            Title = Title,
            Content = _view.Root,
            Background = _view.Background,
            Width = 448,
            SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.Manual,
            ShowInTaskbar = false,
        };
        if (items.Count == 0) return;
        _window.Owner = owner;
        // 수량산출 창의 왼쪽 판 위에 뜬다 — 오른쪽의 3D 그림(그 부재를 보여 주는 자리)을 가리지 않게.
        _window.Left = owner.Left + 24;
        _window.Top = owner.Top + 80;
        _window.MaxHeight = Math.Max(420, SystemParameters.WorkArea.Height - _window.Top - 12);
        if (dark)
            _window.SourceInitialized += (_, _) =>
            {
                try
                {
                    int on = 1;
                    DwmSetWindowAttribute(new WindowInteropHelper(_window).Handle, 20 /* DWMWA_USE_IMMERSIVE_DARK_MODE */, ref on, sizeof(int));
                }
                catch { }
            };
        _view.Prev += () => { if (_i > 0) Go(_i - 1, save: true); };
        _view.Next += () => { if (_i < _items.Count - 1) Go(_i + 1, save: true); else Finish(save: true); };
        _view.Skip += () => { if (_i < _items.Count - 1) Go(_i + 1, save: false); else Finish(save: false); };
        _view.SaveClose += () => Finish(save: true);
        _view.Recommend += () => _view.SetExpr(_items[_i].AutoExpr);
        _view.ExprChanged += UpdatePreview;
        _window.Closing += OnClosing;
        Load();
    }

    // ── 넘기기 · 닫기 ────────────────────────────────────────────────────────────

    private void Go(int to, bool save)
    {
        if (save && !SaveCurrent()) return;       // 식을 더 고치도록 이 부재에 머문다
        _i = to;
        Load();
    }

    private void Finish(bool save)
    {
        if (save && !SaveCurrent()) return;
        _done = true;
        _window.Close();
    }

    // 창을 그냥 닫을 때는 물어볼 수 없으니, 모델 체적과 맞는 식만 적는다(손대지 않은 추천식은 적지 않는다 — 검토 마법사와 같다).
    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_done) return;
        _done = true;
        try
        {
            var it = _items[_i];
            Capture(it);
            if (State(it) == ExprState.Match && !WizardLogic.IsUntouchedRecommendation(it.Expr, it.AutoExpr)) Write(it);
        }
        catch (Exception ex) { _outcome.Failed.Add("닫을 때 적지 못함: " + ex.Message); }
    }

    // 지금 부재의 식을 적는다(비어 있으면 적지 않는다 — 있던 식을 지우지 않는다). 모델과 다른 식은 한 번 묻는다. 적지 않기로 하면 false(머문다).
    private bool SaveCurrent()
    {
        var it = _items[_i];
        Capture(it);
        if (!Confirm(it)) return false;
        if (string.IsNullOrWhiteSpace(it.Expr)) return true;
        Write(it);
        return true;
    }

    // 식 칸 → 항목(칸 이름 [L1] 같은 것은 바꿀 값이 없어 그대로 남는다 — 그런 식은 '값이 없는 칸 이름'으로 막힌다).
    private void Capture(ReviewItem it)
    {
        string raw = (_view.Expr.Text ?? "").Trim();
        it.Glued = WizardLogic.GluedRx.IsMatch(raw);
        it.Expr = WizardLogic.Substituted(raw, _ => null);
    }

    private static ExprState State(ReviewItem it) => it.Glued ? ExprState.Glued : WizardLogic.NumericState(it.Expr, it.ModelVolume, out _);

    /// <summary>저장해도 되는지(검토 마법사의 ConfirmNumeric 과 같은 규칙).</summary>
    private bool Confirm(ReviewItem it)
    {
        double v = 0;
        switch (it.Glued ? ExprState.Glued : WizardLogic.NumericState(it.Expr, it.ModelVolume, out v))
        {
            case ExprState.Empty:
                return true;
            case ExprState.Match:
                if (!WizardLogic.IsUntouchedRecommendation(it.Expr, it.AutoExpr)) return true;
                return MessageBox.Show(_window, WizardLogic.ConfirmUnverifiedText(v, it.ModelVolume, it.Reason),
                    Title, MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes;
            case ExprState.Glued:
                MessageBox.Show(_window, "숫자와 괄호 사이에 ×, + 같은 기호가 빠졌습니다.", Title, MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            case ExprState.Unresolved:
                MessageBox.Show(_window, "칸 이름([L1] 등)이 식에 남아 있습니다 — 이 창에서는 숫자로 넣어 주세요.", Title, MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            case ExprState.Invalid:
                MessageBox.Show(_window, "식을 계산할 수 없습니다. 괄호와 기호를 확인해 주세요.", Title, MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            default:
                return MessageBox.Show(_window, WizardLogic.ConfirmText(v, it.ModelVolume),
                    Title, MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes;
        }
    }

    // 같은 모양의 부재 모두에 식 + 검산 칸(판정)을 한 트랜잭션으로 적고, 커밋 뒤에 다시 읽어 본다(검토 마법사의 FlushWrites 와 같은 순서).
    private void Write(ReviewItem it)
    {
        var failed = new List<string>();
        var wrote = new List<ElementId>();
        try
        {
            var blocked = new Dictionary<ElementId, string>();
            foreach (var id in it.Members)
            {
                var el = _doc.GetElement(id);
                string? why = el == null ? null : FormulaAutoFill.WhyNotEditable(_doc, el);
                if (why != null) blocked[id] = why;
            }
            TransactionStatus status;
            using (var tx = new Transaction(_doc, "DH 예외 부재 식"))
            {
                tx.Start();
                foreach (var id in it.Members)
                {
                    var el = _doc.GetElement(id);
                    if (el == null) { failed.Add($"Id {id.Value}: 부재를 찾을 수 없음"); continue; }
                    if (blocked.TryGetValue(id, out var why)) { failed.Add($"Id {id.Value}: {why}"); continue; }
                    try
                    {
                        if (!DimensionExtractor.WriteString(el, FormulaAutoFill.ParamName, it.Expr))
                        {
                            failed.Add($"Id {id.Value}: 식 칸(DH_Formula)이 없거나 읽기 전용이라 식을 적지 못함");
                            continue;
                        }
                        wrote.Add(id);
                    }
                    catch (Exception ex) { failed.Add($"Id {id.Value}: {ex.Message}"); continue; }
                    try { FormulaAutoFill.WriteVerdict(el, FormulaAutoFill.CurrentFormula(el)); }
                    catch (Exception ex)
                    {
                        failed.Add($"Id {id.Value}: 식은 적었지만 판정 칸을 고치지 못함 — {ex.Message}");
                        FormulaAutoFill.MarkVerdictUnknown(el);
                    }
                }
                status = tx.Commit();
            }
            if (status != TransactionStatus.Committed)
            {
                failed.Insert(0, $"Revit 이 변경을 되돌렸습니다({status}) — 이 부재의 식은 적히지 않았습니다");
                wrote.Clear();
            }
            else
            {
                _outcome.Commits++;
                foreach (var id in wrote)
                {
                    var el = _doc.GetElement(id);
                    if (el == null || FormulaAutoFill.CurrentFormula(el) != it.Expr) failed.Add($"Id {id.Value}: 적은 식이 다시 읽은 값과 다름");
                }
                if (wrote.Count > 0) _saved.Add(_i);
            }
        }
        catch (Exception ex) { failed.Add("적지 못함: " + ex.Message); }
        if (failed.Count == 0) return;
        _outcome.Failed.AddRange(failed);
        MessageBox.Show(_window, $"{failed.Count}건을 적지 못했습니다.\n\n" + string.Join("\n", failed.Take(8)), Title, MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    // ── 화면 ─────────────────────────────────────────────────────────────────────

    private void Load()
    {
        var it = _items[_i];
        _view.Expr.Text = it.Expr;
        _view.SetHeader("예외 부재", _i, _items.Count, it.Subtitle);
        _view.SetReason(it.Reason);
        _view.SetNote(it.Note);
        _view.SetRecommend(it.AutoExpr.Length > 0, it.AutoExpr);
        _view.SetNav(_i > 0, _i == _items.Count - 1);
        UpdatePreview();
        try { _focus?.Invoke(it.Id); } catch { }
    }

    // 식을 바로 계산해 모델 체적과 맞는지 보여 준다(검토 마법사의 예외 부재 모드와 같은 글).
    private void UpdatePreview()
    {
        if (_i < 0 || _i >= _items.Count) return;
        try
        {
            var it = _items[_i];
            string raw = _view.Expr.Text ?? "";
            _view.SetCaption(WizardLogic.Caption(true, it.AutoExpr, raw));
            var st = WizardLogic.NumericStatus(raw, _ => null, it.ModelVolume, it.AutoExpr);
            _view.SetStatus(st.Tone, st.Head, st.Detail);
        }
        catch { _view.SetStatus(WizardView.Tone.Warn, "식 확인 필요"); }
    }
}
