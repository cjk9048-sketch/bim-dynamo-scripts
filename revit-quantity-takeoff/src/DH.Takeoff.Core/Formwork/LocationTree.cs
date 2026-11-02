using System.Diagnostics;

namespace DH.Takeoff.Core.Formwork;

/// <summary>
/// ② 왼쪽의 위치 나무(R73a — JACK 1030 «① 처럼 위치 차례로» · 계획 §3.4): 층 → 중분류 → 부재 → 면.
///  · 층 = 면마다 그 면 가운데 높이로(PickEditor.Floors 의 띠 · JACK 정함 «면마다») — 가운데가 레벨과 1 mm 안으로 같으면 아래를 보는 면(밑면 · 아래 경사)은 아래 층,
///    그 밖은 위 층. 두 층에 걸친 부재는 두 층에 다 나오고 그 층의 면만. 띠가 둘보다 적으면 층 단계 없이 중분류부터.
///  · 중분류 · 부위 = 보드의 PlaceOf(① 의 지금 값 — 계획 검토 M6) · 빈 중분류 = «중분류 없음». 부재 = «부위 + 마크(없으면 유형)».
///  · 면 = FormworkBoard.Faces(형상이 정해 세지 않는 면 — 윗면 · 땅 위 밑면 — 은 싣지 않는다). keep 으로 거른다([전체 | 확인할 자리 | 안 셈]).
/// 수량은 보기만 한다(면의 세는 넓이 합 — 기록 · 엑셀 줄과 상관없음).
/// </summary>
public sealed class LocationTree
{
    public enum Level { Floor, Zone, Member, Face }

    public const string NoZone = "중분류 없음";

    public sealed class Node
    {
        public Level Level { get; init; }

        /// <summary>나무 안에서 하나뿐인 열쇠(펼침을 기억 — 거름을 바꾸거나 다시 지어도 같다).</summary>
        public string Key { get; init; } = "";
        public string Name { get; init; } = "";
        public string Tip { get; internal set; } = "";
        public int Depth { get; init; }
        public List<Node> Children { get; } = new();

        /// <summary>이 줄 아래의 면 전부(면 줄은 자기 하나).</summary>
        public List<BoardFace> Faces { get; } = new();

        /// <summary>부재 줄 · 면 줄의 부재 Id(그 밖은 0).</summary>
        public long Element { get; init; }

        /// <summary>면 줄의 면(그 밖은 null).</summary>
        public BoardFace? Face { get; init; }

        /// <summary>세는 넓이(㎡).</summary>
        public double Area => Faces.Sum(f => f.CountedArea);

        public override string ToString() => $"{Level} {Name} · 면 {Faces.Count}";
    }

    public IReadOnlyList<Node> Roots { get; }

    /// <summary>층 단계가 있는가(띠 둘 이상).</summary>
    public bool HasFloors { get; }

    /// <summary>지은 시간(ms — 일지 · 계획 §5).</summary>
    public long Milliseconds { get; private init; }

    private readonly Dictionary<BoardFace, Node> _faceNodes;
    private readonly Dictionary<Node, Node> _parent;

    private LocationTree(List<Node> roots, bool floors, Dictionary<BoardFace, Node> faceNodes, Dictionary<Node, Node> parent)
    {
        Roots = roots;
        HasFloors = floors;
        _faceNodes = faceNodes;
        _parent = parent;
    }

    /// <summary>부재 이름(나무 · 카드 머리 · 이름표): «부위 + 마크(없으면 유형)» — 부위는 보드의 PlaceOf(비었으면 자동 이름).</summary>
    /// <summary>
    /// 면 넓이 글(R73a6 — 나무 줄 · 이름표 · 딱지 공통): 둘째 자리(«1,234.50»). 0.005 ㎡ 아래 0 아닌 넓이는 셋째 자리까지(«0.003» — 전에는 «0.00»으로 보여 빈 면처럼 읽혔다 · JACK 1102 계단 «0.00㎡»).
    /// </summary>
    public static string AreaText(double area) =>
        area > 0 && area < 0.005 ? (area < 0.0005 ? "<0.001" : area.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture))
        : area.ToString("#,##0.00", System.Globalization.CultureInfo.InvariantCulture);

    public static string MemberName(FormworkBoard board, FormworkElement el) =>
        (board.PlaceOf(el.Id).part is { Length: > 0 } p ? p : CalcPlan.KindName(el.Kind, el.Type)) + " " + (el.Mark.Length > 0 ? el.Mark : el.Type);

    /// <summary>그 면의 줄(걸러져 없으면 null).</summary>
    public Node? NodeOf(BoardFace face) => _faceNodes.TryGetValue(face, out var node) ? node : null;

    /// <summary>그 줄의 윗줄들(뿌리부터 · 자기는 빼고).</summary>
    public List<Node> PathTo(Node node)
    {
        var path = new List<Node>();
        for (var at = node; _parent.TryGetValue(at, out var up); at = up) path.Insert(0, up);
        return path;
    }

    /// <summary>나무의 모든 줄(위에서 아래로 · 다 펼친 차례).</summary>
    public IEnumerable<Node> All()
    {
        var stack = new Stack<Node>(Roots.Reverse());
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            yield return node;
            for (int i = node.Children.Count - 1; i >= 0; i--) stack.Push(node.Children[i]);
        }
    }

    /// <summary>
    /// 면이 드는 층(floors 의 차례 · 띠가 없으면 −1): 가운데 높이가 든 띠. 레벨(띠의 경계)과 1 mm 안이면 아래를 보는 면은 아래 띠 · 그 밖은 위 띠.
    /// 어느 띠에도 들지 않으면(부재가 없어 빠진 띠의 높이) 가장 가까운 띠.
    /// </summary>
    public static int FloorOf(BoardFace face, IReadOnlyList<FloorBand> floors)
    {
        if (floors.Count == 0) return -1;
        const double tol = 0.001;
        double z = face.ZMid;
        int lower = -1, upper = -1;
        for (int i = 0; i < floors.Count; i++)
        {
            if (double.IsFinite(floors[i].Hi) && Math.Abs(z - floors[i].Hi) <= tol) lower = i;
            if (double.IsFinite(floors[i].Lo) && Math.Abs(z - floors[i].Lo) <= tol) upper = i;
        }
        if (lower >= 0 && upper >= 0) return face.Down ? lower : upper;
        if (lower >= 0 || upper >= 0) return Math.Max(lower, upper);
        int best = -1;
        double gap = double.MaxValue;
        for (int i = 0; i < floors.Count; i++)
        {
            double d = z < floors[i].Lo ? floors[i].Lo - z : z >= floors[i].Hi ? z - floors[i].Hi : 0;
            if (d < gap) { gap = d; best = i; }
        }
        return best;
    }

    /// <param name="floors">층 띠(PickEditor.Floors — 아래부터).</param>
    /// <param name="keep">실을 면(없으면 형상이 정해 세지 않는 면만 뺀다 — 넘긴 것도 그 면은 늘 뺀다).</param>
    /// <param name="zoneOrder">중분류 이름 → 차례(① 목록 · 없으면 이름순 · «중분류 없음»은 늘 끝).</param>
    public static LocationTree Build(FormworkBoard board, IReadOnlyList<FloorBand> floors, Func<BoardFace, bool>? keep = null, Func<string, int>? zoneOrder = null)
    {
        var watch = Stopwatch.StartNew();
        bool useFloors = floors.Count >= 2;
        var faceNodes = new Dictionary<BoardFace, Node>(ReferenceEqualityComparer.Instance);
        var parent = new Dictionary<Node, Node>(ReferenceEqualityComparer.Instance);
        var faces = board.Faces.Where(f => !f.Fixed && (keep == null || keep(f))).ToList();
        // 부재가 든 층(그 층 밖에도 면이 있으면 풍선 글).
        var floorsOf = new Dictionary<long, HashSet<int>>();
        var floorOf = new Dictionary<BoardFace, int>(ReferenceEqualityComparer.Instance);
        foreach (var f in board.Faces.Where(f => !f.Fixed))
        {
            int at = useFloors ? FloorOf(f, floors) : -1;
            floorOf[f] = at;
            if (!floorsOf.TryGetValue(f.ElementId, out var set)) floorsOf[f.ElementId] = set = new HashSet<int>();
            set.Add(at);
        }
        // 판 하나에 든 부재(«판: 벽체 W1 외 부재 2와 한 장»).
        var plateMembers = new Dictionary<int, List<long>>();
        foreach (var f in board.Faces)
            if (f.Piece >= 0)
            {
                if (!plateMembers.TryGetValue(f.Piece, out var ids)) plateMembers[f.Piece] = ids = new List<long>();
                if (!ids.Contains(f.ElementId)) ids.Add(f.ElementId);
            }
        var elements = new Dictionary<long, FormworkElement>();
        foreach (var r in board.Results) elements.TryAdd(r.Element.Id, r.Element);
        // 같은 이름 부재(마크 없는 모델 — 녹촌 «기둥 600x600» 17개)는 평면 자리 차례(서 → 동 · 남 → 북)로 번호 «기둥 600x600 3» — 거름 · 층과 상관없이
        // 모델 전체에서 한 번(JACK 1030 첫 확인 전 — 줄이 다 같아 못 가름).
        var spots = board.Faces.Where(f => !f.Fixed).GroupBy(f => f.ElementId)
            .ToDictionary(g => g.Key, g => (Math.Round(g.Average(f => f.Center.X), 1), Math.Round(g.Average(f => f.Center.Y), 1)));
        var names = new Dictionary<long, string>();
        foreach (var same in spots.Keys.Where(elements.ContainsKey).GroupBy(id => MemberName(board, elements[id])))
        {
            var ids = same.OrderBy(id => spots[id].Item1).ThenBy(id => spots[id].Item2).ThenBy(id => id).ToList();
            for (int i = 0; i < ids.Count; i++) names[ids[i]] = ids.Count > 1 ? $"{same.Key} {i + 1}" : same.Key;
        }

        var roots = new List<Node>();
        var floorNodes = new Dictionary<int, Node>();
        var zoneNodes = new Dictionary<(int floor, string zone), Node>();
        var memberNodes = new Dictionary<(int floor, string zone, long id), Node>();
        var joinedRows = new Dictionary<(Node member, BoardFace lead), Node>();
        foreach (var f in faces)
        {
            int floor = floorOf.TryGetValue(f, out int fl) ? fl : -1;
            var el = f.Result.Element;
            string zone = board.PlaceOf(el.Id).zone is { Length: > 0 } z ? z : NoZone;
            Node? up = null;
            if (useFloors && floor >= 0)
            {
                if (!floorNodes.TryGetValue(floor, out var fn))
                    floorNodes[floor] = fn = new Node { Level = Level.Floor, Key = $"f{floor}", Name = floors[floor].Name, Depth = 0 };
                up = fn;
            }
            int depth = up == null ? 0 : 1;
            if (!zoneNodes.TryGetValue((floor, zone), out var zn))
            {
                zoneNodes[(floor, zone)] = zn = new Node { Level = Level.Zone, Key = $"f{floor}/z{zone}", Name = zone, Depth = depth };
                if (up != null) { up.Children.Add(zn); parent[zn] = up; }
            }
            if (!memberNodes.TryGetValue((floor, zone, el.Id), out var mn))
            {
                memberNodes[(floor, zone, el.Id)] = mn = new Node { Level = Level.Member, Key = $"f{floor}/z{zone}/m{el.Id}", Name = names.GetValueOrDefault(el.Id) ?? MemberName(board, el), Depth = depth + 1, Element = el.Id };
                mn.Tip = $"Id {el.Id} · {el.Category} {el.Type}" + (useFloors && floorsOf.TryGetValue(el.Id, out var set) && set.Count > 1 ? " · 다른 층에도 면 있음" : "");
                zn.Children.Add(mn);
                parent[mn] = zn;
            }
            // 한 면처럼 볼 면(R73a7 — 단마다 따로인 계단 옆면)은 부재 줄마다 줄 하나에(넓이는 줄의 면을 더함 — Node.Area · 층이 갈리면 층마다 하나 — 설치 전 검토).
            if (f.Siblings.Count > 1 && joinedRows.TryGetValue((mn, f.Siblings[0]), out var joined))
            {
                joined.Faces.Add(f);
                faceNodes[f] = joined;
                for (Node? at = mn; at != null; at = parent.TryGetValue(at, out var p) ? p : null) at.Faces.Add(f);
                continue;
            }
            var node = new Node { Level = Level.Face, Key = $"x{el.Id}:{f.Face.Solid}:{f.Face.Face}:{f.Piece}", Name = f.Name, Depth = depth + 2, Element = el.Id, Face = f };
            if (f.Piece >= 0 && plateMembers.TryGetValue(f.Piece, out var with) && with.Count > 1)
            {
                long lead = board.Pieces[f.Piece].Result.Element.Id;
                string leadName = elements.TryGetValue(lead, out var leadEl) ? MemberName(board, leadEl) : $"Id {lead}";
                node.Tip = $"판: {leadName} 외 부재 {with.Count - 1}와 한 장";
            }
            node.Faces.Add(f);
            if (f.Siblings.Count > 1) joinedRows[(mn, f.Siblings[0])] = node;
            mn.Children.Add(node);
            parent[node] = mn;
            faceNodes[f] = node;
            for (Node? at = mn; at != null; at = parent.TryGetValue(at, out var p) ? p : null) at.Faces.Add(f);
        }
        // 차례: 층(아래부터) → 중분류(① 목록 · 없음은 끝) → 부재(부위 차례 · 이름 · 평면 자리 · Id) → 면(이름 머리말 · 이름).
        (double x, double y) Spot(Node member) => spots.TryGetValue(member.Element, out var at) ? at : (0, 0);
        string BaseName(Node member) => elements.TryGetValue(member.Element, out var el) ? MemberName(board, el) : member.Name;
        int ZoneRank(string name) => name == NoZone ? int.MaxValue : zoneOrder?.Invoke(name) ?? 0;
        void SortZones(List<Node> list) => list.Sort((a, b) =>
        {
            int c = ZoneRank(a.Name).CompareTo(ZoneRank(b.Name));
            return c != 0 ? c : string.CompareOrdinal(a.Name, b.Name);
        });
        foreach (var zn in zoneNodes.Values)
        {
            zn.Children.Sort((a, b) =>
            {
                int c = board.PartOrder(board.PlaceOf(a.Element).part).CompareTo(board.PartOrder(board.PlaceOf(b.Element).part));
                if (c == 0) c = string.CompareOrdinal(BaseName(a), BaseName(b));   // 번호 붙기 전 이름(«기둥 4» 가 «기둥 39» 뒤로 가지 않게)
                if (c == 0) c = Spot(a).CompareTo(Spot(b));
                return c != 0 ? c : a.Element.CompareTo(b.Element);
            });

            foreach (var mn in zn.Children)
                mn.Children.Sort((a, b) =>
                {
                    int c = FaceNames.Order(a.Name).CompareTo(FaceNames.Order(b.Name));
                    return c != 0 ? c : string.CompareOrdinal(a.Name, b.Name);
                });
        }
        if (useFloors)
        {
            foreach (var (_, fn) in floorNodes.OrderBy(kv => kv.Key)) { SortZones(fn.Children); roots.Add(fn); }
            // 층에 들지 않은 면(띠가 있어도 −1 은 없다 — 혹시 모를 때 중분류부터)
            var loose = zoneNodes.Where(kv => kv.Key.floor < 0).Select(kv => kv.Value).ToList();
            SortZones(loose);
            roots.AddRange(loose);
        }
        else
        {
            roots.AddRange(zoneNodes.Values);
            SortZones(roots);
        }
        return new LocationTree(roots, useFloors, faceNodes, parent) { Milliseconds = watch.ElapsedMilliseconds };
    }
}
