using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives; // UniformGrid
using System.Windows.Interop;
using System.Windows.Media;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using TextBox = System.Windows.Controls.TextBox; // Autodesk.Revit.UI.TextBox 와 충돌 회피

namespace DH.Takeoff.Revit;

/// <summary>검토 대상 1건(부재 + 안내 + 추천 산출식 + 현재 입력값).</summary>
public sealed class ReviewItem
{
    public ElementId Id = ElementId.InvalidElementId; // 대표 부재
    public List<ElementId> Members = new();           // 동일 형상 전원(대표 포함)
    public string Category = "";
    public string Info = "";
    public string Formula = "";        // 추천 산출식(읽기 전용 힌트)
    public string AutoExpr = "";       // 애드인이 만든 값-포함 산출식(개구부 자동 차감) — [추천식] 버튼·기본값
    public string Expr = "";           // 사용자가 작성한 산출식 → DH_Formula 에 저장
    public Dictionary<string, double> Dims = new(); // 미터
    public string Code = "";
}

/// <summary>
/// 비정형 부재 순환 검토 마법사(모델리스). 부재를 하나씩 단독 격리·확대하고,
/// [다음]으로 순환하며, 형상별 추천 산출식을 보여준다.
/// 창 안에서 L1~W3·H·부재코드를 직접 입력하면 [다음]·[닫기] 때 Revit에 기록된다.
/// 규칙: L1·L2·L3 = 평면 북방향(세로/Y), W1·W2·W3 = 가로(X), H = 높이/두께(Z). 단위 m.
/// </summary>
public sealed class ReviewWizard : Window
{
    private static readonly string[] DimNames = { "L1", "L2", "L3", "W1", "W2", "W3", "H" };

    private static ReviewWizard? _current; // GC 방지 + 중복 방지

    private readonly List<ReviewItem> _items;
    private readonly ExternalEvent _evt;
    private readonly WizardHandler _handler;
    private int _i;
    private bool _restoreRaised;
    private bool _suppressTextEvents; // 프로그램이 칸을 채우는 동안 TextChanged 무시
    private readonly nint _revitHwnd; // Revit 메인창 핸들(Esc 전달용 포그라운드 전환)

    private readonly TextBlock _title = new() { FontWeight = FontWeights.Bold, FontSize = 14, Margin = new Thickness(0, 0, 0, 6) };
    private readonly TextBlock _info = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };
    private readonly TextBox _formula = new() { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, MinHeight = 46, Margin = new Thickness(0, 0, 0, 8) };
    private readonly Dictionary<string, TextBox> _fields = new();
    private readonly TextBox _code = new() { Width = 180 };
    private readonly TextBox _expr = new() { TextWrapping = TextWrapping.Wrap, MinHeight = 40, Margin = new Thickness(0, 0, 0, 4) };
    private readonly TextBlock _preview = new() { FontWeight = FontWeights.Bold, Foreground = Brushes.DarkGreen, Margin = new Thickness(0, 0, 0, 4) };

    /// <summary>마법사 시작(이미 떠 있으면 닫고 새로). Revit API 컨텍스트에서 호출.</summary>
    public static void Launch(Document doc, IList<ElementId> ids, nint ownerHandle)
    {
        var items = BuildItems(doc, ids);
        if (items.Count == 0) return;
        _current?.Close();
        _current = new ReviewWizard(items, ownerHandle);
        _current.Show();
    }

    private ReviewWizard(List<ReviewItem> items, nint ownerHandle)
    {
        _items = items;
        _revitHwnd = ownerHandle;
        _handler = new WizardHandler();
        _evt = ExternalEvent.Create(_handler);

        Title = "DH 비정형 부재 검토";
        Width = 360;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = 60; Top = 160;
        _ = new WindowInteropHelper(this) { Owner = ownerHandle };

        _handler.OnPicked = OnPickResult;

        BuildUi();
        Closed += OnClosed;

        // 첫 부재: 기록 없이 단독 격리만
        LoadFields(_items[0]);
        RefreshTexts();
        _handler.WriteTargets = null; _handler.Values = null; _handler.Code = null;
        _handler.Restore = false; _handler.IsolateTarget = _items[0].Id;
        _evt.Raise();
    }

    private void BuildUi()
    {
        var root = new StackPanel { Margin = new Thickness(12) };
        root.Children.Add(_title);
        root.Children.Add(_info);

        root.Children.Add(new TextBlock { Text = "추천 산출식", FontWeight = FontWeights.Bold });
        root.Children.Add(_formula);

        root.Children.Add(new TextBlock
        {
            Text = "1. 치수 입력 (m)",
            FontWeight = FontWeights.Bold, Margin = new Thickness(0, 2, 0, 2),
        });
        root.Children.Add(new TextBlock
        {
            Text = "L=세로(북/Y) · W=가로(X) · H=높이/두께",
            Foreground = Brushes.Gray, FontSize = 11, Margin = new Thickness(0, 0, 0, 4),
        });
        foreach (var name in DimNames) root.Children.Add(FieldRow(name));
        root.Children.Add(new TextBlock
        {
            Text = "개구부는 자동 감지되어 산출식에 값으로 차감됩니다(예: − (0.6*0.6+0.8*1.0)*[H]).",
            Foreground = Brushes.Gray, FontSize = 11, Margin = new Thickness(0, 4, 0, 0),
        });
        root.Children.Add(Row(Lbl("코드"), _code));

        // --- 수량산출식 작성(계산기) ---
        root.Children.Add(new TextBlock
        {
            Text = "2. 수량산출식 작성", FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 8, 0, 4),
        });
        root.Children.Add(_expr);

        var vars = new UniformGrid { Columns = 8, Margin = new Thickness(0, 0, 0, 2) };
        foreach (var v in new[] { "L1", "L2", "L3", "W1", "W2", "W3", "H", "ETC" })
            vars.Children.Add(CalcBtn(v, $"[{v}]"));
        root.Children.Add(vars);

        var ops = new UniformGrid { Columns = 8, Margin = new Thickness(0, 0, 0, 2) };
        ops.Children.Add(CalcBtn("(", "("));
        ops.Children.Add(CalcBtn(")", ")"));
        ops.Children.Add(CalcBtn("+", "+"));
        ops.Children.Add(CalcBtn("−", "-"));
        ops.Children.Add(CalcBtn("×", "*"));
        ops.Children.Add(CalcBtn("÷", "/"));
        ops.Children.Add(CalcBtn("^", "^")); // 제곱
        ops.Children.Add(CalcBtn(".", "."));
        root.Children.Add(ops);

        var ops2 = new UniformGrid { Columns = 8, Margin = new Thickness(0, 0, 0, 6) };
        var rec = new Button { Content = "추천식", Height = 24, Margin = new Thickness(1) };
        rec.Click += (_, _) => { _expr.Text = _items[_i].AutoExpr; _expr.CaretIndex = _expr.Text.Length; _expr.Focus(); };
        var back = new Button { Content = "⌫", Height = 24, Margin = new Thickness(1) };
        back.Click += (_, _) => Backspace();
        var clr = new Button { Content = "C", Height = 24, Margin = new Thickness(1) };
        clr.Click += (_, _) => { _expr.Text = ""; _expr.Focus(); };
        ops2.Children.Add(rec);
        ops2.Children.Add(back);
        ops2.Children.Add(clr);
        root.Children.Add(ops2);

        // 수량(m³) 라이브 미리보기 — 현재 산출식을 칸값으로 즉시 계산(개구부 차감 확인용)
        root.Children.Add(_preview);
        _expr.TextChanged += (_, _) => UpdatePreview();

        root.Children.Add(new TextBlock
        {
            Text = "입력 방법 ①직접 타이핑  또는  ②[측정] 클릭 후 3D뷰에서 모서리를 하나씩 클릭(선택색으로 표시·합계 실시간) → " +
                   "다 고르면 Esc로 완료. 여러 개면 합산(꺾인 벽 등 전체 길이). 끝나면 [저장·다음 ▶].",
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brushes.Gray,
            Margin = new Thickness(0, 8, 0, 10),
        });

        var btns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var prev = new Button { Content = "◀ 이전", Width = 70, Margin = new Thickness(4, 0, 0, 0) };
        var next = new Button { Content = "저장·다음 ▶", Width = 100, Margin = new Thickness(4, 0, 0, 0) };
        var close = new Button { Content = "저장·닫기", Width = 80, Margin = new Thickness(4, 0, 0, 0) };
        prev.Click += (_, _) => { if (_i > 0) GoTo(_i - 1, false); };
        next.Click += (_, _) => { if (_i < _items.Count - 1) GoTo(_i + 1, false); else GoTo(_i, true); };
        close.Click += (_, _) => GoTo(_i, true);
        btns.Children.Add(prev);
        btns.Children.Add(next);
        btns.Children.Add(close);
        root.Children.Add(btns);

        Content = root;
    }

    // --- 네비게이션: 현재 값 저장 + 대상 격리 (1회 Raise) ---
    private void GoTo(int newIndex, bool closing)
    {
        CaptureCurrent();
        var cur = _items[_i];
        _handler.WriteTargets = cur.Members;
        _handler.Values = cur.Dims;
        _handler.Code = cur.Code;
        _handler.Formula = cur.Expr;

        if (closing)
        {
            _handler.Restore = true;
            _handler.IsolateTarget = null;
            _handler.RunDeductionOnClose = true; // 마법사 종료 후 겹침 공제 자동
            _restoreRaised = true;
            _evt.Raise();
            Close();
            return;
        }

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
            var cur = _items[_i];
            _handler.WriteTargets = cur.Members;
            _handler.Values = cur.Dims;
            _handler.Code = cur.Code;
            _handler.Formula = cur.Expr;
            _handler.Restore = true;
            _handler.IsolateTarget = null;
            _handler.RunDeductionOnClose = true; // 마법사 종료 후 겹침 공제 자동
            _restoreRaised = true;
            try { _evt.Raise(); } catch { }
        }
        _current = null;
    }

    // 입력칸 → 현재 항목에 반영(저장 직전)
    private void CaptureCurrent()
    {
        var it = _items[_i];
        var d = new Dictionary<string, double>();
        foreach (var kv in _fields)
            if (double.TryParse(kv.Value.Text.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var m) && m > 0)
                d[kv.Key] = Math.Round(m, 4);
        it.Dims = d;
        it.Code = _code.Text.Trim();
        it.Expr = _expr.Text.Trim();
    }

    // 현재 항목 값 → 입력칸
    private void LoadFields(ReviewItem it)
    {
        _suppressTextEvents = true; // 네비게이션 중 빈 칸 설정이 라벨 삭제를 부르지 않게
        foreach (var name in DimNames)
            _fields[name].Text = it.Dims.TryGetValue(name, out var v) && v > 0
                ? v.ToString("0.####", CultureInfo.InvariantCulture) : "";
        _code.Text = it.Code;
        _expr.Text = it.Expr;
        _suppressTextEvents = false;
    }

    private void RefreshTexts()
    {
        var it = _items[_i];
        _title.Text = $"비정형 {_i + 1} / {_items.Count}";
        _info.Text = it.Info;
        _formula.Text = it.Formula;
    }

    // --- UI 헬퍼 ---
    private static StackPanel Row(params UIElement[] kids)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
        foreach (var k in kids) sp.Children.Add(k);
        return sp;
    }

    // [라벨][입력칸][3D 측정] 한 줄
    private StackPanel FieldRow(string name)
    {
        var tb = new TextBox { Width = 90, Margin = new Thickness(2, 0, 8, 0) };
        _fields[name] = tb;
        tb.TextChanged += (_, _) => OnDimTextChanged(name, tb);
        var pick = new Button { Content = "측정", Width = 56, Height = 22 };
        pick.Click += (_, _) => StartPick(name);
        return Row(Lbl(name), tb, pick);
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, nuint dwExtraInfo);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(nint hWnd);
    private const byte VK_ESCAPE = 0x1B;

    // 3D 측정 시작: 진행 중이면 Esc를 보내 현재 측정을 끝낸 뒤(값은 이미 실시간 반영됨) 새 칸 측정 시작
    private void StartPick(string field)
    {
        if (_handler.Measuring) // 다른 칸 측정 중 → 자동 종료(Esc)
        {
            // ★ [측정] 버튼 클릭으로 포커스가 마법사 창에 있으면 Esc가 PickObject(3D뷰)로 가지 않는다.
            //    Revit 메인창을 먼저 포그라운드로 돌려 Esc가 현재 측정 루프를 취소하게 한다.
            if (_revitHwnd != 0) SetForegroundWindow(_revitHwnd);
            keybd_event(VK_ESCAPE, 0, 0, 0);
            keybd_event(VK_ESCAPE, 0, 2, 0); // KEYEVENTF_KEYUP
        }
        _handler.PickField = field;
        _handler.WriteTargets = null; _handler.Values = null; _handler.Code = null;
        _handler.Restore = false; _handler.IsolateTarget = null;
        _evt.Raise();
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

    private static readonly Regex TokenRx = new(@"\[([A-Za-z0-9_]+)\]", RegexOptions.Compiled);

    /// <summary>현재 산출식을 칸값으로 즉시 평가해 수량(m³)을 표시(개구부 차감 확인용, 최선노력).</summary>
    private void UpdatePreview()
    {
        try
        {
            string sub = TokenRx.Replace(_expr.Text ?? "", m =>
            {
                if (_fields.TryGetValue(m.Groups[1].Value, out var tb) &&
                    double.TryParse(tb.Text.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var v))
                    return v.ToString(CultureInfo.InvariantCulture);
                return "0"; // 칸에 없는 토큰(ETC·사용자정의 등)은 0으로 — 미리보기 한정
            });
            double q = DH.Takeoff.Core.FormulaEvaluator.Evaluate(sub);
            _preview.Text = $"수량 ≈ {Math.Round(q, 4).ToString("0.####", CultureInfo.InvariantCulture)} m³";
            _preview.Foreground = q < 0 ? Brushes.Red : Brushes.DarkGreen;
        }
        catch
        {
            _preview.Text = "수량: (산출식 확인 필요)";
            _preview.Foreground = Brushes.Gray;
        }
    }

    private static TextBlock Lbl(string t) =>
        new() { Text = t, Width = 28, VerticalAlignment = VerticalAlignment.Center };

    // --- 계산기: 버튼 → 산출식 칸에 삽입 ---
    private Button CalcBtn(string label, string insert)
    {
        var b = new Button { Content = label, Height = 24, Margin = new Thickness(1), Padding = new Thickness(0) };
        b.Click += (_, _) => Insert(insert);
        return b;
    }

    private void Insert(string s)
    {
        int i = _expr.CaretIndex;
        _expr.Text = _expr.Text.Insert(i, s);
        _expr.CaretIndex = i + s.Length;
        _expr.Focus();
    }

    private void Backspace()
    {
        int i = _expr.CaretIndex;
        var t = _expr.Text;
        if (i <= 0 || t.Length == 0) return;

        // 커서 바로 앞이 ']' 면 매개변수 토큰 [..]을 통째로 삭제
        if (t[i - 1] == ']')
        {
            int open = t.LastIndexOf('[', i - 1);
            if (open >= 0)
            {
                _expr.Text = t.Remove(open, i - open);
                _expr.CaretIndex = open;
                _expr.Focus();
                return;
            }
        }

        _expr.Text = t.Remove(i - 1, 1); // 그 외는 한 글자
        _expr.CaretIndex = i - 1;
        _expr.Focus();
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
                Formula = autoExpr,   // 읽기전용 힌트 = 실제 생성된 값-포함 산출식
                AutoExpr = autoExpr,
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
            it.Info = n > 1
                ? $"부재 분류: {it.Category}   ·   같은 형상 {n}개 일괄 적용 (대표 Id {it.Id.Value})"
                : $"부재 분류: {it.Category}   ·   Id {it.Id.Value}";
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
