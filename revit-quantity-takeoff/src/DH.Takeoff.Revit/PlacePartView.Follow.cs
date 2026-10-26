using System.Windows.Threading;
using DH.Takeoff.Core.Formwork;
using DH.Takeoff.Core.View3D;

namespace DH.Takeoff.Revit;

// R61a — ② · ③ 에서 고른 것을 Revit 에서도(JACK 2026-10-26 «3D 뷰에서 클릭한 객체로 줌인되게 실제 Revit 3D 뷰에서도 보고 싶어» ·
// 인터뷰 «팝업 그대로 + Revit 뷰 따라가기» · «② · ③» · «선택 + 확대»). 이 창은 Revit API 를 모른다 — 명령이 Follow 를 넣는다.
public sealed partial class PlacePartView
{
    /// <summary>
    /// Revit 뷰 따라가기: 고른 부재 Id 와 그 자리 상자(장면 좌표 — 비었으면 부재 상자로). 명령이 넣는다 · 없으면 하지 않는다.
    /// 고른 뒤 잠깐 기다려 마지막 것만 넘긴다(연달아 눌러도 Revit 이 매번 다시 그리지 않게 · 팝업을 먼저 그린 뒤).
    /// </summary>
    public Action<IReadOnlyList<long>, Box3>? Follow { get; set; }

    private const int FollowDelayMs = 150;
    private DispatcherTimer? _followTimer;
    private (List<long> ids, Box3 box)? _followNext;

    /// <summary>시험용: 넘기려고 기다리는 것이 있다.</summary>
    public bool FollowPending => _followNext != null;

    private void FollowLater(IEnumerable<long> ids, Box3 box)
    {
        if (Follow == null) return;
        var list = ids.Distinct().ToList();
        if (list.Count == 0) { CancelFollow(); return; }
        _followNext = (list, box);
        if (_followTimer == null)
        {
            _followTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(FollowDelayMs) };
            _followTimer.Tick += (_, _) => FollowNow();
        }
        _followTimer.Stop();
        _followTimer.Start();
    }

    private void FollowNow()
    {
        _followTimer?.Stop();
        if (_followNext is not { } next) return;
        _followNext = null;
        // 산출식 적기 · [완료] 중(그 안의 묻는 창에서 틱이 와도)에는 버린다 — 트랜잭션 한가운데서 Revit 화면을 건드리지 않게(계획 검토 H1).
        if (Busy || Follow == null || Step < 1) return;   // ① 로 돌아갔으면 버린다(설치 전 검토 L1)
        try { Follow(next.ids, next.box); }
        catch (Exception ex) { Log?.Invoke("Revit 따라가기\t오류\t" + ex.Message); }
    }

    // 고른 것을 풀었다(Esc · 빈 줄 · 걸음 바꾸기): 기다리던 따라가기를 버린다 — 푼 뒤에 앞서 고른 것이 Revit 에 가지 않게(설치 전 검토 L1).
    private void CancelFollow()
    {
        _followTimer?.Stop();
        _followNext = null;
    }

    /// <summary>창이 닫힐 때: 기다리던 따라가기를 버리고 멈춘다 — 창이 닫힌 뒤 Revit API 를 부르지 않게(계획 검토 H1).</summary>
    public void StopFollow()
    {
        _followTimer?.Stop();
        _followNext = null;
        Follow = null;
    }

    // 덩이 몫들의 부재: 통째로 그 줄인 덩이 = 든 부재 모두 · 다른 줄과 이은 덩이 = 그 줄 몫 조각의 부재만.
    private List<long> OwnersOf(IEnumerable<(int piece, bool whole, List<FacePatch> patches)> parts)
    {
        var ids = new List<long>();
        if (_board == null) return ids;
        foreach (var (piece, whole, patches) in parts)
        {
            var mine = whole ? null : patches.ToHashSet(ReferenceEqualityComparer.Instance);
            foreach (var m in _board.Pieces[piece].Members)
                if (mine == null || mine.Contains(m.Patch)) ids.Add(m.Result.Element.Id);
        }
        return ids;
    }

    // 판 하나(② 에서 찍은 판 · 지운 판 줄): 그 판의 부재 · 판 상자.
    private void FollowPiece(FormworkPiece piece) =>
        FollowLater(piece.Members.Select(m => m.Result.Element.Id), piece.Mesh.Box);
}
