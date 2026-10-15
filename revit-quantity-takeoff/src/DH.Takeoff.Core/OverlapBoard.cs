using DH.Takeoff.Core.View3D;

namespace DH.Takeoff.Core;

/// <summary>겹친 쌍 하나 — 겹침 검사 결과 창의 한 줄.</summary>
public sealed record OverlapRow
{
    /// <summary>부르는 쪽이 쌍을 다시 찾는 번호(찾은 쌍 목록의 차례).</summary>
    public int Key { get; init; }

    /// <summary>겹친 체적(m³) — 결합하기 전에 잰 값.</summary>
    public double VolumeM3 { get; init; }

    /// <summary>남는 쪽(통째로 세는 부재)의 이름 — "카테고리 유형 (Id n)".</summary>
    public string Winner { get; init; } = "";

    /// <summary>잘리는 쪽(겹친 만큼 빠지는 부재)의 이름.</summary>
    public string Loser { get; init; } = "";

    /// <summary>묶음 제목에 쓰는 이름 — 산출서의 부위 이름(넣어 둔 부위, 없으면 종류로 붙인 이름).</summary>
    public string WinnerPart { get; init; } = "";
    public string LoserPart { get; init; } = "";

    /// <summary>그림에서의 부재 차례(그림에 없으면 −1).</summary>
    public int WinnerIndex { get; init; } = -1;
    public int LoserIndex { get; init; } = -1;

    /// <summary>겹친 덩어리를 그릴 수 있다.</summary>
    public bool HasBlob { get; init; }

    /// <summary>결합 결과의 글(결합하지 않았으면 "").</summary>
    public string Status { get; init; } = "";

    /// <summary>결합한 뒤 다시 잰 겹침(m³). 재지 않았으면 NaN.</summary>
    public double AfterM3 { get; init; } = double.NaN;

    /// <summary>
    /// 결합해서 잘 끝났다(확인할 것이 없다). 찾은 뒤의 창에서도 그 창에서 고친 쌍이면 참이다.
    /// 부르는 쪽이 정한다 — 여기서는 Status 의 글자를 다시 풀지 않는다.
    /// </summary>
    public bool Fine { get; init; }

    /// <summary>결합으로 두 덩이가 된 잘린 쪽을 나눈 결과(«조각 n개» / «나누기 못 함: 까닭» / "" = 대상 아님 · 안 함). 보여 주기만 한다.</summary>
    public string Split { get; init; } = "";

    /// <summary>Split 이 못 나눈 결과일 때의 앞 글자 — 적는 쪽(Revit)과 보이는 쪽(창)이 함께 쓴다.</summary>
    public const string SplitFail = "나누기 못 함";

    /// <summary>
    /// 앞선 결합으로 이 쌍의 겹침이 이미 없어졌다(결합을 걸지 않았다 — 그 자리가 두 부재 가운데 누구 것인지 모른다). Fine 과 함께 참이다.
    /// 부르는 쪽이 정한다(Status 의 글자를 여기서 풀지 않는다).
    /// </summary>
    public bool Gone { get; init; }

    /// <summary>
    /// 이 창에서는 고칠 수 없다 — 나눈 부재의 여러 조각과 겹쳐 겹침 검사를 다시 해야 하는 쌍(고치기 단추 대신 결과 딱지만).
    /// 부르는 쪽이 정한다.
    /// </summary>
    public bool Blocked { get; init; }

    /// <summary>
    /// 남는 쪽을 맞바꿀 수 없다 — 잘린 쪽을 나눈 쌍(조각마다 결합 순서가 따로라 아래 조각만 바뀐다). 사용자 모드의 [남는 쪽에서 빼기]를 두지 않는다
    /// (눌러도 거절만 뜨는 단추를 두지 않게 — R37 revit R1). 부르는 쪽이 정한다.
    /// </summary>
    public bool NoSwap { get; init; }
}

/// <summary>목록의 묶음 하나: 남는 쪽의 부위 → 잘리는 쪽의 부위. 제목일 뿐이다 — 묶음 때문에 빠지는 쌍은 없다.</summary>
public sealed class OverlapGroup
{
    private readonly List<OverlapRow> _rows;
    private readonly bool _joined;

    public string WinnerPart { get; }
    public string LoserPart { get; }
    public IReadOnlyList<OverlapRow> Rows => _rows;
    public double TotalM3 => _rows.Sum(r => r.VolumeM3);

    /// <summary>확인할 쌍의 수(결합한 뒤에만 센다 — 창에서 고치면 그때그때 줄어든다).</summary>
    public int Problems => _joined ? _rows.Count(r => !r.Fine) : 0;

    /// <summary>접어 두었다(줄이 보이지 않는다).</summary>
    public bool Collapsed { get; set; }

    public string Title => WinnerPart + " → " + LoserPart;

    internal OverlapGroup(string winnerPart, string loserPart, List<OverlapRow> rows, bool joined)
    {
        WinnerPart = winnerPart;
        LoserPart = loserPart;
        _rows = rows;
        _joined = joined;
    }

    // 같은 Key 의 줄을 제자리에서 바꾼다(OverlapBoard.Replace).
    internal void Swap(OverlapRow row)
    {
        int at = _rows.FindIndex(r => r.Key == row.Key);
        if (at >= 0) _rows[at] = row;
    }
}

/// <summary>그림에서 부재가 맡은 자리(화면은 둘 다 같은 색으로 칠한다 — 어느 쪽인지는 이름과 차례에 쓴다).</summary>
public enum OverlapRole { None, Winner, Loser }

/// <summary>
/// 겹친 덩어리를 그리는 모습: 안 그림 · 빨강(남은 겹침) · 고쳐서 정리된 자리(남는 쪽의 것) · 이미 풀린 자리(누구 것인지 모름) ·
/// 외곽선만(초점이 다른 쌍에 있을 때 — 찍히지 않는다). 색은 화면이 정한다.
/// </summary>
public enum BlobLook { Hidden, Strong, Done, Faint, Gone }

/// <summary>
/// 겹침 검사 결과 창이 들고 있는 상태와 규칙 — 목록의 묶음과 차례, 초점(쌍 하나만 보기), 그림에서 누가 무슨 색인지, 그림을 찍었을 때 어느 쌍으로 가는지.
/// 화면(OverlapResultView)은 그리기와 전하기만 한다(PlacePartView ↔ PickEditor 와 같은 가름). Revit 도 화면도 쓰지 않는다.
/// JACK 2026-10-14(두 번째 — Revit 에서 써 본 뒤): "목록에서 찍었을 때 겹친 부재만 색상이 있으면 좋겠어. 겹친 부재(노란색), 겹쳐진 부위(빨간색), 나머지는 투명."
/// 인터뷰: 아무것도 고르지 않았을 때는 겹친 부위만 빨강(부재는 전부 투명).
/// 겹친 쌍은 부위와 상관없이 전부 나온다 — 묶음은 제목일 뿐 아무것도 거르지 않는다.
///  · 묶음의 차례: 겹친 체적의 합이 큰 것부터(같으면 이름순). 묶음 안: 체적이 큰 것부터(같으면 Key).
///    결합한 뒤에는 확인할 쌍이 든 묶음이 먼저, 묶음 안에서도 확인할 쌍이 먼저. 차례는 처음 만들 때 한 번 정한다 — 창에서 고쳐도 줄이 움직이지 않는다.
///  · 초점이 없으면(전체 보기) 부재는 하나도 칠하지 않는다(외곽선만). 덩어리는 모두 그린다: 빨강, 고쳐서 정리된 쌍(Fine)은 초록.
///  · 초점이 있으면 그 쌍의 두 부재만 칠하고 그 쌍의 덩어리만 빨강(초록)으로 그린다. 다른 쌍의 덩어리는 외곽선만(찍히지 않는다).
/// </summary>
public sealed class OverlapBoard
{
    /// <summary>한 자리를 확대해 볼 때 상자 둘레에 두는 여백(상자 크기의 배) — 큰 덩어리가 화면에 꽉 차지 않게.</summary>
    public const double ZoomMargin = 0.35;

    /// <summary>확대해도 화면 가로가 이보다 좁아지지 않는다(m).</summary>
    public const double ZoomMinWidth = 4.0;

    /// <summary>확대할 때 덩어리 둘레로 더 보여 주는 길이(m, 한쪽) — 작은 덩어리도 어느 벽 · 어느 바닥의 어디인지 보이게.</summary>
    public const double ZoomPad = 4.0;

    /// <summary>그 상자를 볼 때 화면 가로에 들어와야 하는 가장 작은 길이(m): 상자의 대각선 + 양쪽의 둘레.</summary>
    public static double ZoomWidth(Box3 box) => Math.Max(ZoomMinWidth, (box.IsEmpty ? 0 : box.Size.Length) + 2 * ZoomPad);

    private readonly Dictionary<int, OverlapRow> _byKey = new();
    private readonly Dictionary<int, OverlapGroup> _groupOf = new();
    private readonly List<OverlapRow> _ordered = new();
    private readonly Dictionary<int, List<int>> _ofMember = new();    // 부재(그림의 차례) → 그 부재가 든 쌍의 Key(체적이 큰 것부터)

    /// <summary>결합한 뒤의 결과를 보고 있다.</summary>
    public bool Joined { get; }

    public IReadOnlyList<OverlapGroup> Groups { get; }

    /// <summary>목록에 놓이는 차례 그대로의 쌍(묶음의 차례 → 묶음 안의 차례).</summary>
    public IReadOnlyList<OverlapRow> Ordered => _ordered;

    /// <summary>확인할 쌍의 수(결합한 뒤에만 센다 — 창에서 고치면 그때그때 줄어든다).</summary>
    public int Problems => Joined ? Unfinished : 0;

    /// <summary>아직 끝나지 않은 쌍의 수(Fine 이 아닌 쌍 — 찾은 뒤의 창이든 결합한 뒤의 창이든).</summary>
    public int Unfinished => _byKey.Values.Count(r => !r.Fine);

    /// <summary>[우선순위대로 결합]이 거는 쌍의 수 = 끝나지 않았고 이 창에서 고칠 수 있는 쌍(Blocked — 여러 조각과 겹친 쌍 — 은 빼고).</summary>
    public int Joinable => _byKey.Values.Count(r => !r.Fine && !r.Blocked);

    /// <summary>초점인 쌍의 Key. 없으면(전체 보기) −1.</summary>
    public int Focus { get; private set; } = -1;

    public OverlapRow? Focused => Focus >= 0 && _byKey.TryGetValue(Focus, out var row) ? row : null;

    public OverlapBoard(IEnumerable<OverlapRow> rows, bool joined)
    {
        Joined = joined;
        var all = rows.ToList();
        foreach (var row in all)
            if (!_byKey.TryAdd(row.Key, row)) throw new ArgumentException($"쌍의 번호가 겹칩니다: {row.Key}", nameof(rows));

        bool Problem(OverlapRow r) => joined && !r.Fine;
        var groups = all
            .GroupBy(r => (r.WinnerPart, r.LoserPart))
            .Select(g => new OverlapGroup(g.Key.WinnerPart, g.Key.LoserPart,
                g.OrderBy(r => Problem(r) ? 0 : 1).ThenByDescending(r => r.VolumeM3).ThenBy(r => r.Key).ToList(), joined))
            .OrderBy(g => g.Problems > 0 ? 0 : 1)
            .ThenByDescending(g => g.TotalM3)
            .ThenBy(g => g.WinnerPart, StringComparer.Ordinal)
            .ThenBy(g => g.LoserPart, StringComparer.Ordinal)
            .ToList();
        Groups = groups;
        foreach (var group in groups)
            foreach (var row in group.Rows)
            {
                _ordered.Add(row);
                _groupOf[row.Key] = group;
            }

        var ofMember = new Dictionary<int, List<OverlapRow>>();
        foreach (var row in all)
            foreach (int member in new[] { row.WinnerIndex, row.LoserIndex })
            {
                if (member < 0) continue;
                if (!ofMember.TryGetValue(member, out var list)) ofMember[member] = list = new List<OverlapRow>();
                if (!list.Contains(row)) list.Add(row);
            }
        foreach (var (member, list) in ofMember)
        {
            list.Sort((a, b) => a.VolumeM3 != b.VolumeM3 ? b.VolumeM3.CompareTo(a.VolumeM3) : a.Key.CompareTo(b.Key));
            _ofMember[member] = list.Select(r => r.Key).ToList();
        }
    }

    public OverlapRow? Row(int key) => _byKey.TryGetValue(key, out var row) ? row : null;

    public OverlapGroup? GroupOf(int key) => _groupOf.TryGetValue(key, out var group) ? group : null;

    /// <summary>
    /// 창에서 쌍을 고친 뒤: 같은 Key 의 줄을 제자리에서 바꾼다 — 묶음 · 목록의 차례 · 부재별 쌍의 차례 그대로(고쳐도 줄이 움직이지 않게).
    /// 남는 쪽과 잘리는 쪽이 맞바뀐 줄(사용자 모드)도 받는다 — 그 줄은 옛 묶음에 그대로 남는다.
    /// 받지 않는 것(false · 아무것도 바꾸지 않음): 모르는 Key · 두 부재가 다른 줄(그림의 차례가 맞바뀐 것 말고는 같아야 한다 — 부재별 쌍의 목록이 어긋난다).
    /// </summary>
    public bool Replace(OverlapRow row)
    {
        if (!_byKey.TryGetValue(row.Key, out var old)) return false;
        bool same = (row.WinnerIndex == old.WinnerIndex && row.LoserIndex == old.LoserIndex) ||
                    (row.WinnerIndex == old.LoserIndex && row.LoserIndex == old.WinnerIndex);
        if (!same) return false;
        _byKey[row.Key] = row;
        int at = _ordered.FindIndex(r => r.Key == row.Key);
        if (at >= 0) _ordered[at] = row;
        _groupOf[row.Key].Swap(row);
        return true;
    }

    // ── 초점 ─────────────────────────────────────────────────────────────────────

    /// <summary>그 쌍만 본다. 없는 번호면 그대로 두고 false. 접힌 묶음 안의 쌍이면 그 묶음을 편다.</summary>
    public bool SetFocus(int key)
    {
        if (!_byKey.ContainsKey(key)) return false;
        Focus = key;
        _groupOf[key].Collapsed = false;
        return true;
    }

    /// <summary>전체 보기로 돌아간다.</summary>
    public void ClearFocus() => Focus = -1;

    /// <summary>목록의 다음 쌍으로(초점이 없으면 첫 쌍 · 끝에서는 처음으로 돈다). 돌려주는 값 = 새 초점(쌍이 없으면 −1).</summary>
    public int Next() => Step(+1);

    /// <summary>목록의 앞 쌍으로(초점이 없으면 마지막 쌍 · 처음에서는 끝으로 돈다).</summary>
    public int Previous() => Step(-1);

    private int Step(int by)
    {
        int n = _ordered.Count;
        if (n == 0) return -1;
        int at = _ordered.FindIndex(r => r.Key == Focus);
        int next = at < 0 ? (by > 0 ? 0 : n - 1) : ((at + by) % n + n) % n;
        SetFocus(_ordered[next].Key);
        return Focus;
    }

    // ── 그림 ─────────────────────────────────────────────────────────────────────

    /// <summary>그 부재를 지금 무슨 자리로 칠할 것인가(None = 외곽선만). 초점인 쌍의 두 부재만 칠한다 — 전체 보기에서는 아무 부재도 칠하지 않는다.</summary>
    public OverlapRole RoleOf(int member)
    {
        if (member < 0 || Focused is not { } focused) return OverlapRole.None;
        return member == focused.LoserIndex ? OverlapRole.Loser
             : member == focused.WinnerIndex ? OverlapRole.Winner : OverlapRole.None;
    }

    /// <summary>그 쌍의 겹친 덩어리를 지금 어떻게 그릴 것인가: 초점이 다른 쌍에 있으면 외곽선만, 아니면 남은 겹침 · 정리된 자리 · 이미 풀린 자리.</summary>
    public BlobLook LookOf(int key)
    {
        if (!_byKey.TryGetValue(key, out var row) || !row.HasBlob) return BlobLook.Hidden;
        if (Focus >= 0 && Focus != key) return BlobLook.Faint;
        return !row.Fine ? BlobLook.Strong : row.Gone ? BlobLook.Gone : BlobLook.Done;
    }

    /// <summary>그 부재가 든 쌍의 번호들(체적이 큰 것부터).</summary>
    public IReadOnlyList<int> PairsOf(int member) =>
        _ofMember.TryGetValue(member, out var keys) ? keys.ToList() : Array.Empty<int>();

    // ── 그림을 찍었을 때 ─────────────────────────────────────────────────────────

    /// <summary>
    /// 그림에서 겹친 덩어리를 찍었다. keys = 마우스 밑의 덩어리들(가까운 것부터 — 세 부재가 한 자리에서 겹치면 덩어리도 겹친다).
    /// 초점이 그 가운데 없으면 맨 앞의 것으로, 있으면 그 다음 것으로 돈다(가려진 덩어리도 다시 누르면 고를 수 있다). 돌려주는 값 = 새 초점.
    /// </summary>
    public int ClickBlobs(IReadOnlyList<int> keys)
    {
        var known = keys.Where(_byKey.ContainsKey).Distinct().ToList();
        if (known.Count == 0) return Focus;
        int at = known.IndexOf(Focus);
        SetFocus(known[at < 0 ? 0 : (at + 1) % known.Count]);
        return Focus;
    }

    /// <summary>
    /// 그림에서 색칠된 부재를 찍었다: 그 부재가 든 쌍 가운데 가장 큰 것으로, 이미 그 부재의 쌍을 보고 있으면 다음 것으로 돈다.
    /// 쌍에 들지 않은 부재면 그대로. 돌려주는 값 = 새 초점.
    /// </summary>
    public int ClickMember(int member)
    {
        if (!_ofMember.TryGetValue(member, out var keys) || keys.Count == 0) return Focus;
        int at = keys.IndexOf(Focus);
        SetFocus(keys[at < 0 ? 0 : (at + 1) % keys.Count]);
        return Focus;
    }

    // ── 확대 ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// 그 쌍을 볼 때 화면에 맞출 상자: 겹친 덩어리의 상자, 덩어리가 없으면 두 부재의 상자가 겹친 자리(겹치지 않으면 둘을 싼 상자),
    /// 부재도 하나뿐이면 그 부재. 아무것도 없으면 null(확대하지 않는다).
    /// </summary>
    public Box3? ZoomBox(int key, Func<int, Box3> memberBox, Func<int, Box3> blobBox)
    {
        if (!_byKey.TryGetValue(key, out var row)) return null;
        if (row.HasBlob)
        {
            var blob = blobBox(key);
            if (!blob.IsEmpty) return blob;
        }
        var winner = row.WinnerIndex >= 0 ? memberBox(row.WinnerIndex) : Box3.Empty;
        var loser = row.LoserIndex >= 0 ? memberBox(row.LoserIndex) : Box3.Empty;
        if (!winner.IsEmpty && !loser.IsEmpty)
        {
            var both = Intersect(winner, loser);
            return both.IsEmpty ? winner.With(loser) : both;
        }
        if (!winner.IsEmpty) return winner;
        if (!loser.IsEmpty) return loser;
        return null;
    }

    /// <summary>두 상자가 겹친 자리. 겹치지 않으면 빈 상자.</summary>
    public static Box3 Intersect(Box3 a, Box3 b)
    {
        if (a.IsEmpty || b.IsEmpty) return Box3.Empty;
        var box = new Box3(
            new Vec3(Math.Max(a.Min.X, b.Min.X), Math.Max(a.Min.Y, b.Min.Y), Math.Max(a.Min.Z, b.Min.Z)),
            new Vec3(Math.Min(a.Max.X, b.Max.X), Math.Min(a.Max.Y, b.Max.Y), Math.Min(a.Max.Z, b.Max.Z)));
        return box.IsEmpty ? Box3.Empty : box;
    }
}
