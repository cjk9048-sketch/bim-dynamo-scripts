using System.Windows.Threading;

namespace DH.Takeoff.Revit;

/// <summary>
/// 창에서 모델을 건드리는 일(Revit API)을 돌리는 길(R62 — JACK 2026-10-26 «팝업을 쓸 때 Revit 이 락 걸리지 않았으면»).
/// 창을 띄운 채 Revit 도 쓰면 창의 처리기는 API 문맥 밖이다 — 일은 Revit 이 손이 빌 때(ExternalEvent) 돈다. 이 창은 Revit API 를 모르므로
/// 이 모양만 안다(PlacePartView 의 규칙 그대로). undoName 이 있으면 그 일이 Revit 실행 취소 목록의 한 줄이 된다(인터뷰 «Revit 실행 취소로»).
/// </summary>
public interface IApiRunner
{
    /// <summary>
    /// 일 하나를 넣는다. work 는 API 문맥에서 돌고, 그 뒤 done(오류면 그 예외 · 아니면 null)을 같은 스레드에서 부른다.
    /// 바로 돌 수도(ImmediateRunner) 나중에 돌 수도 있다 — 부르는 쪽은 done 에서 화면을 맞춘다.
    /// </summary>
    void Run(string? undoName, Action work, Action<Exception?>? done = null);

    /// <summary>넣었지만 아직 끝나지 않은 일이 있다.</summary>
    bool Pending { get; }
}

/// <summary>그 자리에서 바로 돈다(창이 명령 안에서 모달로 떠 있던 때와 같다 — 시험의 한 길).</summary>
public sealed class ImmediateRunner : IApiRunner
{
    public static readonly ImmediateRunner Instance = new();

    public void Run(string? undoName, Action work, Action<Exception?>? done = null)
    {
        Exception? error = null;
        try { work(); }
        catch (Exception ex) { error = ex; }
        done?.Invoke(error);
    }

    public bool Pending => false;
}

/// <summary>
/// 디스패처가 손이 빌 때 돈다(Revit 의 ExternalEvent 처럼 «나중에» — 하네스가 출하 길을 타게 · 계획 검토 M2). 넣은 차례대로 하나씩.
/// </summary>
public sealed class DispatcherRunner : IApiRunner
{
    private readonly Queue<(string? undo, Action work, Action<Exception?>? done)> _queue = new();
    private bool _posted, _running;

    /// <summary>시험용: 돈 일의 실행 취소 이름(null 이면 «—»).</summary>
    public List<string> Ran { get; } = new();

    /// <summary>시험용: 모든 DispatcherRunner 에 넣고 아직 돌지 않은 일의 수(진짜 입력 시험이 누른 뒤 그 일이 끝날 때까지 기다리게).</summary>
    public static int Outstanding { get; private set; }

    public void Run(string? undoName, Action work, Action<Exception?>? done = null)
    {
        _queue.Enqueue((undoName, work, done));
        Outstanding++;
        if (_posted) return;
        _posted = true;
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(Drain));
    }

    private void Drain()
    {
        _posted = false;
        _running = true;
        try
        {
            // 줄이 빌 때까지(done 이 넣은 일도 — ExternalEvent 의 Execute 와 같이).
            while (_queue.Count > 0)
            {
                var (undo, work, done) = _queue.Dequeue();
                Outstanding--;
                Ran.Add(undo ?? "—");
                Exception? error = null;
                try { work(); }
                catch (Exception ex) { error = ex; }
                done?.Invoke(error);
            }
        }
        finally { _running = false; }
    }

    public bool Pending => _queue.Count > 0 || _running;
}
