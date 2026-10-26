using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Events;
using Autodesk.Revit.UI;

namespace DH.Takeoff.Revit;

/// <summary>
/// 띄운 채 쓰는 「수량산출」 창 하나(R62 — JACK 2026-10-26 «팝업을 쓸 때 Revit 이 락 걸리지 않았으면»): 명령은 창을 띄우고 바로 끝난다.
/// 창이 모델을 건드리는 일은 이 요청 줄로 — Revit 이 손이 빌 때(ExternalEvent) 넣은 차례대로 돌고, 이름이 있는 일은 TransactionGroup 하나로 묶어
/// Revit 실행 취소 목록의 한 줄이 된다(인터뷰 «Revit 실행 취소로»). 창 · 디스패처 · ExternalEvent 는 모두 Revit 의 한 스레드다.
///  · Revit 에서 모델이 바뀌면(우리 일이 돌지 않을 때) Stale — 창에 알리고 줄 선 쓰기 · 다음 쓰기를 막는다(계획 검토 H1).
///  · 다른 문서가 앞이면 창을 숨기고 줄 선 일을 붙들었다가, 돌아오면 다시 보이고 잇는다(인터뷰 · 계획 검토 M4). 그 문서를 닫으면 강제로 닫는다.
/// </summary>
internal sealed class TakeoffSession : IExternalEventHandler, IApiRunner
{
    /// <summary>지금 떠 있는 창(없으면 null) — 한 번에 하나.</summary>
    public static TakeoffSession? Current { get; private set; }

    private sealed record Request(string Name, string? Undo, bool Write, Action Work, Action<Exception?>? Done, long Queued);

    private readonly Queue<Request> _queue = new();
    private readonly ExternalEvent _event;
    private readonly Action<string> _log;
    private readonly DispatcherTimer _waitTimer, _retryTimer;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private bool _hiddenBySwitch;
    private int _externalChanges;

    public Document Doc { get; }
    public Window Window { get; }
    public PlacePartView View { get; }
    public KeyboardGuard? Keys { get; set; }

    /// <summary>우리 일이 도는 중(그동안의 DocumentChanged 는 우리 것).</summary>
    public bool Executing { get; private set; }

    /// <summary>Revit 에서 모델이 바뀌었다(우리 일 밖에서).</summary>
    public bool Stale { get; private set; }

    /// <summary>마무리가 끝나 정말 닫는 중(창의 닫기 처리기가 다시 묻지 않게).</summary>
    public bool Ending { get; private set; }

    /// <summary>
    /// 닫기의 마지막 — 명령이 넣는다(API 문맥에서 돈다). forced = 문서 · Revit 을 닫는 중: 확인 · 위치 색 · 선택 되돌리기 · 알림 창 없이 일지만(계획 검토 M4).
    /// </summary>
    public Action<bool>? Finalizer { get; set; }

    public TakeoffSession(Document doc, Window window, PlacePartView view, Action<string> log)
    {
        Doc = doc;
        Window = window;
        View = view;
        _log = log;
        _event = ExternalEvent.Create(this);       // 명령(API 문맥) 안에서 만든다
        _waitTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
        _waitTimer.Tick += (_, _) =>
        {
            _waitTimer.Stop();
            // 우리 일이 묻는 창을 띄우고 있는 동안은 «다른 작업 중»이 아니다(계획 검토 L1).
            if (Pending && !Executing) View.ShowWaiting(true);
        };
        _retryTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _retryTimer.Tick += (_, _) => { _retryTimer.Stop(); Raise(); };
        Current = this;
    }

    // ── 요청 ──────────────────────────────────────────────────────────────

    /// <summary>창의 일 하나(IApiRunner — undoName 이 있으면 쓰는 일 · 실행 취소 한 줄).</summary>
    public void Run(string? undoName, Action work, Action<Exception?>? done = null) =>
        Enqueue(new Request(undoName ?? "읽기", undoName, undoName != null, work, done, _clock.ElapsedMilliseconds));

    /// <summary>읽기만 하는 일(따라가기 · 거푸집 재기) — 실행 취소 줄 없음 · 모델이 바뀌어도 돈다.</summary>
    public void Read(string name, Action work, Action<Exception?>? done = null) =>
        Enqueue(new Request(name, null, false, work, done, _clock.ElapsedMilliseconds));

    /// <summary>쓰는 일이지만 실행 취소 줄을 따로 정한 것(이름 = 줄 이름).</summary>
    public void Write(string undoName, Action work, Action<Exception?>? done = null) =>
        Enqueue(new Request(undoName, undoName, true, work, done, _clock.ElapsedMilliseconds));

    public bool Pending => _queue.Count > 0 || Executing;

    /// <summary>[취소](Revit 이 다른 작업 중): 아직 시작하지 않은 일을 거둔다 — 거둔 일의 done 은 «취소»로 부른다.</summary>
    public int CancelPending()
    {
        int n = 0;
        while (_queue.Count > 0)
        {
            var r = _queue.Dequeue();
            n++;
            try { r.Done?.Invoke(new OperationCanceledException("취소함")); } catch { }
        }
        View.ShowWaiting(false);
        _log($"요청\t취소\t{n}개");
        return n;
    }

    private void Enqueue(Request r)
    {
        // 끝난 뒤(문서를 닫아 강제로 닫힘 — 그 틈에 떠 있던 묻는 창이 돌아와 넣는 것)는 받지 않는다: 버린 ExternalEvent 를 깨우며 끝없이 다시 시도하지 않게(R62 설치 전 검토 낮음).
        if (Ending || Current != this)
        {
            _log($"요청\t{r.Name}\t받지 않음 — 창이 닫힘");
            try { r.Done?.Invoke(new OperationCanceledException("창이 닫혔습니다")); } catch { }
            return;
        }
        _queue.Enqueue(r);
        if (!Executing) Raise();     // 도는 중이면 그 Execute 가 줄이 빌 때까지 돈다
        _waitTimer.Stop();
        _waitTimer.Start();
    }

    private void Raise()
    {
        if (_queue.Count == 0) return;
        ExternalEventRequest result;
        try { result = _event.Raise(); }
        catch (Exception ex) { _log("요청\tRaise 오류\t" + ex.Message); result = ExternalEventRequest.Denied; }
        if (result is ExternalEventRequest.Accepted or ExternalEventRequest.Pending) return;
        // 거절 · 시간 넘김: 줄은 그대로 두고 조금 뒤 다시(계획 검토 L1).
        _log("요청\tRaise " + result + " — 다시 시도");
        _retryTimer.Stop();
        _retryTimer.Start();
    }

    public void Execute(UIApplication app)
    {
        if (!Doc.IsValidObject) { Drop("문서가 닫힘"); return; }
        // 다른 문서가 앞이면 붙들어 둔다 — 돌아오면 ViewActivated 가 다시 깨운다(계획 검토 M4).
        if (app.ActiveUIDocument?.Document is not { } active || !active.Equals(Doc))
        {
            _log($"요청\t다른 문서가 앞 — 붙들어 둠 {_queue.Count}");
            return;
        }
        Executing = true;
        _waitTimer.Stop();
        View.ShowWaiting(false);
        try
        {
            // 줄이 빌 때까지(done 이 넣은 일도 — 다음 Raise 를 기다리지 않게 · 계획 검토 L1).
            while (_queue.Count > 0)
            {
                var r = _queue.Dequeue();
                if (Stale && r.Write)
                {
                    _log($"요청\t{r.Name}\t버림 — 모델이 바뀜");
                    try { r.Done?.Invoke(new InvalidOperationException("Revit 에서 모델이 바뀌어 하지 않았습니다 — [다시 열기]")); } catch { }
                    continue;
                }
                // 쓰는 일 동안은 Revit 창을 끈다 — 그 일이 띄우는 묻는 창 · 저장 창은 우리 창만 끄므로, 묶음이 열린 채 Revit 의 단추가 눌리지 않게(R62 설치 전 검토 · 띄운 채 쓰기 전과 같게).
                nint main = r.Write ? app.MainWindowHandle : 0;
                bool off = main != 0 && IsWindowEnabled(main) && !EnableWindow(main, false);
                try { RunOne(r); }
                finally { if (off) EnableWindow(main, true); }
            }
        }
        finally { Executing = false; }
    }

    [DllImport("user32.dll")] private static extern bool EnableWindow(nint hwnd, bool enable);
    [DllImport("user32.dll")] private static extern bool IsWindowEnabled(nint hwnd);

    private void RunOne(Request r)
    {
        long waited = _clock.ElapsedMilliseconds - r.Queued;
        var watch = Stopwatch.StartNew();
        Exception? error = null;
        TransactionGroup? group = null;
        string closed = "";
        try
        {
            if (r.Undo != null)
            {
                group = new TransactionGroup(Doc, r.Undo) { IsFailureHandlingForcedModal = true };
                if (group.Start() != TransactionStatus.Started) { group.Dispose(); group = null; closed = " · 그룹 못 엶"; }
            }
            r.Work();
        }
        catch (Exception ex) { error = ex; }
        finally
        {
            if (group != null)
            {
                closed = " · " + CloseGroup(group);
                try { group.Dispose(); } catch { }
            }
        }
        _log($"요청\t{r.Name}\t기다림 {waited} ms · 실행 {watch.ElapsedMilliseconds} ms{closed}" + (error != null ? "\t오류 " + error.Message : ""));
        try { r.Done?.Invoke(error); }
        catch (Exception ex) { _log($"요청\t{r.Name}\t마친 뒤 오류\t{ex}"); }
    }

    // 요청 하나의 묶음을 닫는다: 하나로(Assimilate) — 안 되면 묶지 않고라도 남긴다(Commit). 그것도 안 되면 Revit 이 되돌린다(알림은 done 이 본다).
    private static string CloseGroup(TransactionGroup group)
    {
        if (!group.HasStarted()) return "그룹 없음";
        try
        {
            var status = group.Assimilate();
            if (status == TransactionStatus.Committed) return "Assimilate";
            return "Assimilate " + status + " · Commit " + group.Commit();
        }
        catch (Exception ex)
        {
            try { return "Assimilate 오류(" + ex.Message + ") · Commit " + group.Commit(); }
            catch (Exception again) { return "닫지 못함(" + again.Message + ")"; }
        }
    }

    private void Drop(string why)
    {
        int n = _queue.Count;
        _queue.Clear();
        _log($"요청\t버림 {n}\t{why}");
    }

    // ── Revit 의 사건(RibbonApp 이 넘긴다 — 한 번 구독) ─────────────────────

    /// <summary>
    /// 모델이 바뀌었다: 우리 일이 도는 동안은 우리 것 · 다른 문서 · 되돌려진 트랜잭션은 뺀다. 뷰만 바뀐 것(임시 숨기기 · 보이기 등 — 뷰 요소만)도 뺀다.
    /// 그 밖은 넓게 모두(사람의 Ctrl+Z 로 우리 «DH …» 줄을 되돌린 것도 — 그것도 모델이 바뀐 것).
    /// </summary>
    public void DocumentChanged(DocumentChangedEventArgs e)
    {
        try
        {
            if (Executing || !e.GetDocument().Equals(Doc)) return;
            if (e.Operation is UndoOperation.TransactionRolledBack or UndoOperation.TransactionGroupRolledBack) return;
            var added = e.GetAddedElementIds();
            var deleted = e.GetDeletedElementIds();
            var modified = e.GetModifiedElementIds();
            // 보는 것만 바뀐 일(뷰 · 주석 · 치수 · 태그 · 단면 상자 · 자르기 영역 · 시트의 뷰포트)은 모델이 바뀐 것이 아니다(R62 설치 전 검토 중간 — 넓게 잡으면 늘 [다시 열기]).
            if (deleted.Count == 0 && added.Concat(modified).All(id => ViewOnly(Doc.GetElement(id)))) return;
            _externalChanges++;
            string what = string.Join(" · ", e.GetTransactionNames());
            _log($"모델 바뀜\t{e.Operation} · {what} · 더함 {added.Count} · 지움 {deleted.Count} · 고침 {modified.Count}");
            if (Stale) return;
            Stale = true;
            Window.Dispatcher.BeginInvoke(new Action(View.ShowStale));
        }
        catch (Exception ex) { _log("모델 바뀜\t읽기 오류\t" + ex.Message); }
    }

    // 보기에만 속한 요소: 뷰 자체 · 한 뷰에 매인 것(주석 · 치수 · 태그 · 글 · 상세 선 · 뷰포트) · 주석 범주 · 단면 상자 · 뷰 경계 · 자르기 영역.
    private static bool ViewOnly(Element? el)
    {
        if (el == null) return false;
        if (el is View || el.ViewSpecific) return true;
        try
        {
            var category = el.Category;
            if (category == null) return false;
            if (category.CategoryType == CategoryType.Annotation) return true;
            var bic = category.BuiltInCategory;
            return bic is BuiltInCategory.OST_SectionBox or BuiltInCategory.OST_Viewers or BuiltInCategory.OST_CropBoundary or BuiltInCategory.OST_Cameras;
        }
        catch { return false; }
    }

    /// <summary>뷰가 바뀌었다: 다른 문서면 창을 숨기고, 이 문서로 돌아오면 다시 보이고 줄 선 일을 잇는다.</summary>
    public void ViewActivated(Document? doc)
    {
        try
        {
            bool mine = doc != null && doc.Equals(Doc);
            if (!mine && Window.IsVisible)
            {
                _hiddenBySwitch = true;
                Window.Hide();
                _log("창\t다른 문서 — 숨김");
            }
            else if (mine && _hiddenBySwitch)
            {
                _hiddenBySwitch = false;
                Window.Show();
                _log("창\t이 문서로 돌아옴 — 다시 보임");
                Raise();
            }
            // 우리가 숨긴 채(닫기 · 다시 열기를 넣은 뒤) 다른 문서에 붙들렸던 일도 돌아오면 잇는다(R62 설치 전 검토 낮음).
            else if (mine && _queue.Count > 0 && !Executing) Raise();
        }
        catch (Exception ex) { _log("창\t뷰 바뀜 처리 오류\t" + ex.Message); }
    }

    /// <summary>문서 · Revit 을 닫는 중: 줄 선 일은 버리고, 일지만 남기고 창을 닫는다(확인 · 위치 색 · 알림 창 없이 — 계획 검토 M4).</summary>
    public void ForceClose(string why)
    {
        if (Current != this) return;
        Ending = true;
        Drop(why);
        _log("창\t강제 닫기\t" + why);
        try { Finalizer?.Invoke(true); } catch (Exception ex) { _log("창\t강제 닫기 마무리 오류\t" + ex.Message); }
        End();
    }

    /// <summary>[수량산출]을 또 눌렀다(같은 문서): 떠 있는 창을 앞으로.</summary>
    public void Activate()
    {
        try
        {
            if (!Window.IsVisible) Window.Show();
            if (Window.WindowState == WindowState.Minimized) Window.WindowState = WindowState.Normal;
            Window.Activate();
        }
        catch (Exception ex) { _log("창\t앞으로 오류\t" + ex.Message); }
    }

    /// <summary>창을 정말 닫았다(마무리 뒤): 훅 · 타이머 · 사건을 거둔다.</summary>
    public void End()
    {
        if (Current != this) return;
        Ending = true;
        Current = null;
        _queue.Clear();
        _waitTimer.Stop();
        _retryTimer.Stop();
        Keys?.Dispose();
        Keys = null;
        // 제 Execute 안에서 끝나면(닫기 · 다시 열기 요청) 그 Execute 가 끝난 뒤에 ExternalEvent 를 버린다.
        if (Executing) Window.Dispatcher.BeginInvoke(new Action(() => { try { _event.Dispose(); } catch { } }));
        else try { _event.Dispose(); } catch { }
        try { if (Window.IsLoaded) Window.Close(); } catch { }
        _log($"창\t끝 · 바깥 변경 {_externalChanges}번");
    }

    public string GetName() => "DH 수량산출 창";
}
