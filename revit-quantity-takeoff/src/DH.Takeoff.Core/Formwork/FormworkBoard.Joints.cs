using DH.Takeoff.Core.View3D;

namespace DH.Takeoff.Core.Formwork;

/// <summary>
/// R67 — 판마다 «겹침» 칸(JACK 2026-10-27 «거푸집 객체를 누르고 겹침 무시 · 공제할 때와 안 할 때를 단추로» · 고름 «깎은 부재마다 칸»):
/// 고른 판들의 면을 덮거나 깎은 이웃을 이름(부위 · 없으면 종류)마다 모아 보이고, 누르면 그 판들의 면 × 그 이웃의 «겹침» 기록(무시 / 공제)을 적는다.
/// 면 모양이 바뀌므로 형상부터 다시 잰다(Remeasure — 명령이 넣는다). «겹침» 기록이 판을 잰 때와 다르면 대응표를 다시 걸 때마다(Reclassify) 다시 잰다 —
/// 바꾸기 · 되돌리기 · 모드 · ↺ 가 한 길.
/// </summary>
public sealed partial class FormworkBoard
{
    /// <summary>형상부터 다시 잰다(같은 장면 · 이 판의 옵션 — 명령이 넣는다). 없으면 «겹침» 칸을 쓰지 않는다(모양을 바꿀 수 없다).</summary>
    public Func<FormworkOptions, IReadOnlyList<ElementFormwork>>? Remeasure { get; set; }

    /// <summary>형상을 다시 잰 횟수(화면이 부재 모서리 · 고른 판을 다시 잡는다).</summary>
    public int MeasureVersion { get; private set; }

    /// <summary>마지막으로 다시 잰 시간(ms — 일지).</summary>
    public long RemeasureMilliseconds { get; private set; }

    private string _measuredJoints = "";

    // 판을 잰 «겹침» 기록(사용자 모드일 때만 걸린다 — 자동은 "") + 시공 순서표 짝 예외(R71 — 모드와 상관없이).
    private string MeasureKey() => (Options.ActiveRules?.JointText ?? "") + "\u0001" + Rules.PairText;

    // 대응표를 다시 건다 — «겹침» 기록 · 짝 예외가 판을 잰 때와 다르면 형상부터 다시 잰다(시공 순서표는 대응표에서 다시 짓는다).
    private void Reclassify()
    {
        Options.Order = PourOrder.From(Rules);
        if (Remeasure != null && MeasureKey() != _measuredJoints)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            Results = Remeasure(Options);
            _measuredJoints = MeasureKey();
            _serial.Clear();
            foreach (var r in Results)
                foreach (var f in r.Faces)
                    foreach (var p in f.Patches)
                        _serial.TryAdd(p, _serial.Count);
            _covering = null;
            _meshes = new Dictionary<string, (View3D.MeshData, PatchSpan[])>(StringComparer.Ordinal);
            _lines = null;
            MeasureVersion++;
            RemeasureMilliseconds = watch.ElapsedMilliseconds;
        }
        FormworkAnalyzer.Reclassify(Results, Options);
    }

    /// <summary>«겹침» 칸 하나: 이름(부위 · 없으면 종류) · 넓이(공제 + 무시) · 모두 무시인가 · 일부만 무시인가 · 이웃 부재 Id 들.</summary>
    public sealed record JointChip(string Label, double Area, bool Ignored, bool Mixed, IReadOnlyList<long> Others);

    /// <summary>칸에 보일 가장 작은 넓이(㎡ — 이보다 작은 겹침은 접합부가 아니라 맞댄 변의 찌꺼기).</summary>
    public const double JointChipMin = 0.005;

    /// <summary>고른 판들의 면을 덮거나 깎은 이웃(이름마다 · 넓은 차례). 다시 잴 수 없으면 빈 목록.</summary>
    public List<JointChip> JointChips(IReadOnlyList<FormworkPiece> pieces)
    {
        var chips = new List<JointChip>();
        if (Remeasure == null) return chips;
        var elements = new Dictionary<long, FormworkElement>();
        foreach (var r in Results) elements.TryAdd(r.Element.Id, r.Element);
        var sums = new Dictionary<string, (double kept, double ignored, HashSet<long> ids)>(StringComparer.Ordinal);
        foreach (var face in FacesOf(pieces))
        {
            void Add(IEnumerable<FaceCover> list, bool ignored)
            {
                foreach (var c in list)
                {
                    string label = elements.TryGetValue(c.ElementId, out var el) ? JointLabel(el) : "주변 콘크리트";
                    var s = sums.GetValueOrDefault(label, (0, 0, new HashSet<long>()));
                    s.ids.Add(c.ElementId);
                    sums[label] = ignored ? (s.kept, s.ignored + c.Area, s.ids) : (s.kept + c.Area, s.ignored, s.ids);
                }
            }
            Add(face.Covers, false);
            Add(face.Cut, false);
            Add(face.Ignored, true);
        }
        foreach (var (label, s) in sums.OrderByDescending(kv => kv.Value.kept + kv.Value.ignored).ThenBy(kv => kv.Key, StringComparer.Ordinal))
        {
            if (s.kept + s.ignored < JointChipMin) continue;
            bool ignored = s.kept < JointChipMin;
            chips.Add(new JointChip(label, s.kept + s.ignored, ignored, !ignored && s.ignored >= JointChipMin, s.ids.Order().ToList()));
        }
        return chips;
    }

    // 칸 이름: 부위(사람이 넣은 이름 — «중간슬래브» · «바닥 헌치») · 없으면 부재 종류(보 · 벽 · 슬래브 …).
    private static string JointLabel(FormworkElement el) => el.Part.Length > 0 ? el.Part : CalcPlan.KindName(el.Kind, el.Type);

    private static List<FaceResult> FacesOf(IReadOnlyList<FormworkPiece> pieces) =>
        pieces.SelectMany(p => p.Members).Select(m => m.Face).Distinct(ReferenceEqualityComparer.Instance).Cast<FaceResult>().ToList();

    /// <summary>
    /// 고른 판들의 면에서 그 이웃들의 겹침을 무시(ignore = true — 그 자리도 거푸집) / 공제로 적는다(사용자 모드 · 형상부터 다시 잼). 적은 기록 수(0 = 아무 일 없음).
    /// </summary>
    public int SetJoints(IReadOnlyList<FormworkPiece> pieces, IReadOnlyCollection<long> others, bool ignore) => SetJoints(FacesOf(pieces), others, ignore);

    /// <summary>고른 면(R73a2 — 빗금이 보이는 그 면만 · 판 전체가 아님)에서 그 이웃들의 겹침을 무시 / 공제로 적는다. 나머지는 SetJoints 와 같다.</summary>
    public int SetFaceJoints(IEnumerable<BoardFace> faces, IReadOnlyCollection<long> others, bool ignore) =>
        SetJoints(faces.Select(f => f.Face).Distinct(ReferenceEqualityComparer.Instance).Cast<FaceResult>().ToList(), others, ignore);

    private int SetJoints(IReadOnlyList<FaceResult> faces, IReadOnlyCollection<long> others, bool ignore)
    {
        if (Remeasure == null || others.Count == 0) return 0;
        string before = Snapshot();
        bool wasUser = Rules.UserMode;
        bool switched = EnsureUser();
        int n = 0;
        foreach (var face in faces)
            foreach (long other in face.Covers.Concat(face.Cut).Concat(face.Ignored).Select(c => c.ElementId).Distinct().Where(others.Contains))
            {
                if (JointRecord.Of(face, other, ignore) is not { } record) continue;
                bool? had = Rules.JointOf(record.Element, record.Nx, record.Ny, record.Nz, record.D, other);
                // 자동 모드에서는 기록이 걸려 있지 않았다 — 같은 값의 묵은 기록이 있어도 사용자로 바뀌며 처음 걸리는 것이라 «바뀜»(R73a2 설치 전 검토 M1:
                // 단추가 «바뀐 것 없음»만 띄우고 그대로 남지 않게).
                if ((wasUser ? had : null) == ignore) continue;
                if (had != ignore) Rules.SetJoint(record);   // 이미 같은 값이면 적지 않는다(되돌릴 일 · 다시 재기가 헛되이 생기지 않게 — 최종 검토 낮음)
                n++;
            }
        return Finish(before, switched, () => n);
    }

    /// <summary>
    /// 다시 잰 뒤 그 조각이 든 덩이(화면이 고른 판을 다시 잡을 때): 같은 부재 · 같은 입체 · 같은 면의 조각 가운데 열쇠(대표 점)를 품는 것 · 없으면 그 면의 첫 조각. 없으면 −1.
    /// </summary>
    public int PieceNear(DropRecord record, int solid, int face)
    {
        int first = -1;
        for (int i = 0; i < Pieces.Count; i++)
            foreach (var m in Pieces[i].Members)
            {
                if (m.Face.ElementId != record.Element || m.Face.Solid != solid || m.Face.Face != face) continue;
                if (record.Matches(m.Face, m.Patch)) return i;
                if (first < 0) first = i;
            }
        return first;
    }

    // ── R73a2(JACK 1030 «그 부재만 보이게 하되 조금이라도 닿은 부재도 함께» · «빠진 자리를 3D에 빗금으로») ──

    private Dictionary<long, FormworkElement> ElementsById()
    {
        var elements = new Dictionary<long, FormworkElement>();
        foreach (var r in Results) elements.TryAdd(r.Element.Id, r.Element);
        return elements;
    }

    /// <summary>
    /// 그 부재에 조금이라도 닿은 부재(② «부재만 보기»): 분석이 잰 맞닿음 · 깎인 자리 · 무시한 자리를 두 쪽으로 — 그 부재의 면에 닿은 이웃 + 제 면이 그 부재에 닿은 부재
    /// (나중에 치는 부재(계단 등)는 먼저 친 면을 잴 때 없는 것으로 보아 한쪽에만 남는다) — 그리고 그 부재와 한 판에 든 부재(같은 평면으로 이은 판). 자기는 빼고 Id 차례.
    /// 모서리 · 점으로만 닿은 부재(넓이 0)는 들지 않는다.
    /// </summary>
    public List<long> TouchingOf(long element)
    {
        var found = new HashSet<long>();
        foreach (var r in Results)
        {
            bool mine = r.Element.Id == element;
            foreach (var f in r.Faces)
                foreach (var c in f.Covers.Concat(f.Cut).Concat(f.Ignored))
                {
                    if (mine) found.Add(c.ElementId);
                    else if (c.ElementId == element) found.Add(r.Element.Id);
                }
        }
        foreach (var piece in Pieces)
            if (piece.Members.Any(m => m.Result.Element.Id == element))
                foreach (var m in piece.Members) found.Add(m.Result.Element.Id);
        found.Remove(element);
        return found.Order().ToList();
    }

    /// <summary>
    /// 그 면들과 «겹침» 기록 열쇠(부재 + 평면)가 같은 면 전부(R73a2 설치 전 검토 M2 — 기록은 부재 · 평면 · 이웃이라 같은 부재 · 같은 평면의 다른 면도 함께 바뀐다:
    /// 입체가 여럿인 패밀리 · 결합으로 갈라진 같은 평면 면). 빗금 · 넓이 · 단추가 이 범위를 같이 본다.
    /// </summary>
    public List<BoardFace> SamePlaneFaces(IReadOnlyCollection<BoardFace> faces)
    {
        var keys = faces.Select(f => JointRecord.Of(f.Face, 0, false)).OfType<JointRecord>().ToList();
        if (keys.Count == 0) return faces.ToList();
        var picked = new HashSet<BoardFace>(faces, ReferenceEqualityComparer.Instance);
        foreach (var f in Faces)
            if (!picked.Contains(f) && f.Face.Normal is { Length: 3 } n && keys.Any(k => k.Matches(f.ElementId, n[0], n[1], n[2], f.Face.PlaneD, 0))) picked.Add(f);
        return Faces.Where(picked.Contains).ToList();
    }

    /// <summary>
    /// 빠진 자리 한 갈래(R73a2): 이웃 이름(부위 · 없으면 종류 — «겹침» 칸과 같은 이름) · 그 이름의 이웃들 · 빠진 넓이(공제 — 맞닿음 + 깎인 자리) · 빼지 않은 넓이(무시) ·
    /// 빠진 자리의 고리(모델 좌표 — 3D 빨간 빗금) · 빼지 않은 자리의 고리(R73a4 — 3D 청록 빗금 · JACK 1102 «다른 객체와 닿는 부분만 확인할 수 있으면 돼»).
    /// </summary>
    public sealed record FaceCut(string Label, IReadOnlyList<long> Others, double Area, double Kept, IReadOnlyList<IReadOnlyList<Vec3>> Rings, IReadOnlyList<IReadOnlyList<Vec3>> KeptRings);

    /// <summary>
    /// 그 면들이 다른 부재 때문에 빠진 자리(이름마다 · 넓은 차례 · JointChipMin 아래는 뺌). 넓이 = 그 면들의 Covers + Cut(무시 = Ignored)를 그대로 더한 값 — 땅 위 밑면 ·
    /// 7 m 띠 · 포갬(Shared) · 실 같은 자투리처럼 이웃 탓이 아닌 것은 들지 않는다. 한 면(FaceResult)을 여러 판이 나눠 가져도 한 번만 센다.
    /// </summary>
    public List<FaceCut> CutsOf(IEnumerable<BoardFace> faces) => CutsOf(faces.Select(f => f.Face).Distinct(ReferenceEqualityComparer.Instance).Cast<FaceResult>(), ElementsById());

    private static List<FaceCut> CutsOf(IEnumerable<FaceResult> faces, Dictionary<long, FormworkElement> elements)
    {
        var sums = new Dictionary<string, (double area, double kept, HashSet<long> ids, List<IReadOnlyList<Vec3>> rings, List<IReadOnlyList<Vec3>> keptRings)>(StringComparer.Ordinal);
        static IEnumerable<IReadOnlyList<Vec3>> Rings(FaceCover c) => c.Outline.Where(r => r.Count >= 3).Select(r => (IReadOnlyList<Vec3>)r.Select(q => new Vec3(q[0], q[1], q[2])).ToList());
        foreach (var face in faces)
        {
            void Add(IEnumerable<FaceCover> list, bool kept)
            {
                foreach (var c in list)
                {
                    string label = elements.TryGetValue(c.ElementId, out var el) ? JointLabel(el) : "주변 콘크리트";
                    if (!sums.TryGetValue(label, out var s)) sums[label] = s = (0, 0, new HashSet<long>(), new List<IReadOnlyList<Vec3>>(), new List<IReadOnlyList<Vec3>>());
                    s.ids.Add(c.ElementId);
                    (kept ? s.keptRings : s.rings).AddRange(Rings(c));
                    sums[label] = kept ? (s.area, s.kept + c.Area, s.ids, s.rings, s.keptRings) : (s.area + c.Area, s.kept, s.ids, s.rings, s.keptRings);
                }
            }
            Add(face.Covers, false);
            Add(face.Cut, false);
            Add(face.Ignored, true);
        }
        return sums.Where(kv => kv.Value.area + kv.Value.kept >= JointChipMin)
            .OrderByDescending(kv => kv.Value.area + kv.Value.kept).ThenBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => new FaceCut(kv.Key, kv.Value.ids.Order().ToList(), kv.Value.area, kv.Value.kept, kv.Value.rings, kv.Value.keptRings)).ToList();
    }

    /// <summary>
    /// 빗금 딱지 하나(R73a4 «이 부재만 보기»): 이웃 이름 · 딱지 자리(그 면 · 그 이름의 가장 넓은 고리의 넓이 가운데 — 모델 좌표) · 넓이 · 빼지 않은 자리인가.
    /// </summary>
    public sealed record CutMark(string Label, Vec3 At, double Area, bool Kept);

    /// <summary>
    /// 면 하나(FaceResult)의 빠진 · 빼지 않은 자리(R73a4): 바깥 법선(곡면이면 영 벡터 — 뒤를 보는 면의 빗금 · 딱지는 화면이 숨긴다) · 이름마다의 갈래(CutsOf 와 같은 셈) ·
    /// 딱지(이름마다 · 빠진 / 빼지 않은 자리마다 하나 — 한 면에 같은 이웃이 여러 고리여도 가장 넓은 고리에 하나 · min ㎡ 아래는 없음 · 넓은 차례).
    /// </summary>
    public sealed record FaceCuts(FaceResult Face, Vec3 Normal, IReadOnlyList<FaceCut> Cuts, IReadOnlyList<CutMark> Marks);

    /// <summary>
    /// 그 면들의 빠진 · 빼지 않은 자리를 면(FaceResult)마다(R73a4 — JACK 1102 «그 객체에서 거푸집이 어디에 생기는지 … 다른 객체와 닿는 부분만 확인할 수 있으면 돼»):
    /// 3D 빗금(면의 법선과 함께) · 이웃 이름 딱지. 갈래는 면마다 CutsOf 와 같은 셈(JointChipMin 아래는 면마다 뺌 — 그래서 면마다 더한 것은 CutsOf(같은 면들)보다 그만큼 작을 수 있다).
    /// 갈래가 없는 면은 뺀다.
    /// </summary>
    public List<FaceCuts> CutsByFace(IEnumerable<BoardFace> faces, double markMin)
    {
        var elements = ElementsById();
        var list = new List<FaceCuts>();
        foreach (var face in faces.Select(f => f.Face).Distinct(ReferenceEqualityComparer.Instance).Cast<FaceResult>())
        {
            var cuts = CutsOf(new[] { face }, elements);
            if (cuts.Count == 0) continue;
            var n = face.Normal is { Length: 3 } nn ? new Vec3(nn[0], nn[1], nn[2]) : Vec3.Zero;
            var marks = new List<CutMark>();
            if (n.Length > 0.5)
                foreach (var cut in cuts)
                    foreach (var (rings, area, kept) in new[] { (cut.Rings, cut.Area, false), (cut.KeptRings, cut.Kept, true) })
                        if (area >= markMin && RingCenter(rings, n) is { } at) marks.Add(new CutMark(cut.Label, at, area, kept));
            list.Add(new FaceCuts(face, n, cuts, marks.OrderByDescending(m => m.Area).ToList()));
        }
        return list;
    }

    // 가장 넓은 고리의 넓이 가운데(면 평면 위 — 첫 점에서 부채꼴 세모로 · 넓이가 거의 0 이면 점의 가운데). 고리가 없으면 null.
    private static Vec3? RingCenter(IReadOnlyList<IReadOnlyList<Vec3>> rings, Vec3 n)
    {
        Vec3? best = null;
        double most = -1;
        foreach (var ring in rings)
        {
            if (ring.Count < 3) continue;
            double sum = 0;
            var c = Vec3.Zero;
            for (int i = 1; i + 1 < ring.Count; i++)
            {
                double a = n.Dot((ring[i] - ring[0]).Cross(ring[i + 1] - ring[0])) / 2;
                sum += a;
                c += (ring[0] + ring[i] + ring[i + 1]) * (a / 3);
            }
            if (Math.Abs(sum) <= most) continue;
            most = Math.Abs(sum);
            best = Math.Abs(sum) > 1e-9 ? c * (1 / sum) : ring.Aggregate(Vec3.Zero, (s, p) => s + p) * (1.0 / ring.Count);
        }
        return best;
    }

    // ── R73a5([빼기] · [넣기] 도구 — JACK 1102 «위에서 고르는 것보다는 빼기 버튼을 누르고 직접 선택하는 방식으로») ──

    /// <summary>
    /// 도구가 누른 닿은 자리 하나(R73a5): 누른 면 · 이웃 부재(«겹침» 기록의 이웃 — 갈래 이름이 같아도 부재마다 따로) · 이름 · 지금 넣었는가(무시) ·
    /// 바뀔 넓이(같은 부재 · 같은 평면 면에서 그 이웃의 지금 상태 넓이) · 기록이 바꾸는 면(SamePlaneFaces — 단추와 같은 범위) ·
    /// 그 면들의 그 이웃 고리(지금 상태 — 마우스 밑 굵은 테두리) · 누른 면의 바깥 법선.
    /// </summary>
    public sealed record CutSpot(BoardFace Face, long Other, string Label, bool Kept, double Area, IReadOnlyList<BoardFace> Faces,
                                 IReadOnlyList<IReadOnlyList<Vec3>> Rings, Vec3 Normal);

    /// <summary>고리 안 판정의 여유(m — 변에서 이만큼 밖도 안으로 친다 · 5 mm).</summary>
    public const double SpotEdge = 0.005;

    /// <summary>면 평면이 보이는 겉면보다 이만큼 뒤까지는 그 면으로 친다(m — 판은 몸에서 4 mm 띄워 그린다 · 2 cm).</summary>
    public const double SpotBehind = 0.02;

    /// <summary>
    /// 시선(모델 좌표 — origin · direction)이 지나는 닿은 자리(R73a5): 후보 면(화면이 빗금을 그린 면)마다 시선이 그 평면을 지나는 점을 그 면의 빠진(맞닿음 + 깎인 자리) ·
    /// 넣은(무시) 고리에 견준다(면 평면 위 · 변에서 SpotEdge 안까지). 시선을 마주한 면만 · 보이는 겉면(visible — 그 부재의 몸 · 판까지의 거리)보다 SpotBehind 넘게 뒤인 평면은
    /// 가려진 것으로 뺀다 — 빠진 자리는 판의 구멍이라 시선이 그 부재의 몸(면 평면)에 닿고 · 넣은 자리는 판 위에 닿는다. 여럿이면 가장 앞(시선이 먼저 지나는) 평면.
    /// 없으면 null.
    /// </summary>
    public CutSpot? CutAt(IEnumerable<BoardFace> faces, Vec3 origin, Vec3 direction, double visible = double.PositiveInfinity)
    {
        if (direction.Length < 1e-12) return null;
        var dir = direction.Unit();
        BoardFace? best = null;
        FaceCover? cover = null;
        bool kept = false;
        double bestT = double.MaxValue;
        var seen = new HashSet<FaceResult>(ReferenceEqualityComparer.Instance);
        foreach (var f in faces)
        {
            var face = f.Face;
            if (!seen.Add(face) || face.Normal is not { Length: 3 } nn) continue;
            var n = new Vec3(nn[0], nn[1], nn[2]);
            double dn = n.Dot(dir);
            if (dn > -0.05) continue;                                           // 뒤를 보는 면 · 시선과 거의 나란한 면(화면도 빗금을 그리지 않는다)
            double t = (face.PlaneD - n.Dot(origin)) / dn;
            if (t > visible + SpotBehind || t > bestT + 1e-6) continue;
            var q = origin + dir * t;
            // 이웃끼리 겹친 자리(버림 위 벽 등)는 좁은 것 — 넓은 것은 다른 자리를 눌러 바꿀 수 있지만 좁은 것은 그 자리뿐이다.
            foreach (var (c, isKept) in face.Covers.Concat(face.Cut).Select(c => (c, false)).Concat(face.Ignored.Select(c => (c, true))))
                if ((t < bestT - 1e-6 || c.Area < cover!.Area) && Inside(c.Outline, q, n))
                    (best, cover, kept, bestT) = (f, c, isKept, Math.Min(t, bestT));
        }
        if (best == null || cover == null) return null;
        var elements = ElementsById();
        string label = elements.TryGetValue(cover.ElementId, out var el) ? JointLabel(el) : "주변 콘크리트";
        var scope = SamePlaneFaces(new[] { best });
        double area = 0;
        var rings = new List<IReadOnlyList<Vec3>>();
        foreach (var face in scope.Select(x => x.Face).Distinct(ReferenceEqualityComparer.Instance).Cast<FaceResult>())
            foreach (var c in kept ? face.Ignored : face.Covers.Concat(face.Cut))
            {
                if (c.ElementId != cover.ElementId) continue;
                area += c.Area;
                rings.AddRange(c.Outline.Where(r => r.Count >= 3).Select(r => (IReadOnlyList<Vec3>)r.Select(p => new Vec3(p[0], p[1], p[2])).ToList()));
            }
        var normal = new Vec3(best.Face.Normal[0], best.Face.Normal[1], best.Face.Normal[2]);
        return new CutSpot(best, cover.ElementId, label, kept, area, scope, rings, normal);
    }

    // 면 평면 위의 점이 고리들(바깥 + 구멍 — 홀짝) 안인가 · 변에서 SpotEdge 안이면 안으로 친다. 법선의 가장 큰 축을 버린 평면에서 센다.
    private static bool Inside(List<List<double[]>> outline, Vec3 q, Vec3 n)
    {
        double ax = Math.Abs(n.X), ay = Math.Abs(n.Y), az = Math.Abs(n.Z);
        (double u, double v) Flat(double x, double y, double z) => az >= ax && az >= ay ? (x, y) : ay >= ax ? (x, z) : (y, z);
        var (qu, qv) = Flat(q.X, q.Y, q.Z);
        bool inside = false;
        foreach (var ring in outline)
        {
            if (ring.Count < 3) continue;
            for (int i = 0, j = ring.Count - 1; i < ring.Count; j = i++)
            {
                var (u1, v1) = Flat(ring[i][0], ring[i][1], ring[i][2]);
                var (u0, v0) = Flat(ring[j][0], ring[j][1], ring[j][2]);
                if ((v1 > qv) != (v0 > qv) && qu < (u0 - u1) * (qv - v1) / (v0 - v1) + u1) inside = !inside;
                // 변까지의 거리(3D — 기울어진 면도 그대로)
                var a = new Vec3(ring[j][0], ring[j][1], ring[j][2]);
                var b = new Vec3(ring[i][0], ring[i][1], ring[i][2]);
                var ab = b - a;
                double len = ab.Dot(ab), s = len > 1e-18 ? Math.Clamp((q - a).Dot(ab) / len, 0, 1) : 0;
                if ((a + ab * s - q).Length <= SpotEdge) return true;
            }
        }
        return inside;
    }

    /// <summary>
    /// [같은 곳 모두](R73a2): 고른 면 가운데 그 갈래의 이웃이 가장 넓게 빠뜨린 면의 «주인 열쇠 ← 이웃 열쇠»를 «자리만 거푸집»으로 하는 짝 예외 한 줄(그 면이 옆면이면 «옆면만»)과
    /// 그 줄을 걸면 빠진 자리가 거푸집으로 바뀌는 면 수(판마다 «겹침» 기록이 정한 면 · 이미 빼지 않는 면 · 윗면은 빼고). 걸 짝이 없으면 (null, 0). 거는 일은 SetPair.
    /// </summary>
    public (PourOrder.Pair? pair, int faces) SamePlaces(IEnumerable<BoardFace> faces, FaceCut cut)
    {
        var elements = ElementsById();
        FaceResult? best = null;
        FormworkElement? owner = null, near = null;
        double most = 0;
        foreach (var f in faces)
            foreach (var c in f.Face.Covers.Concat(f.Face.Cut))
                if (c.Area > most && cut.Others.Contains(c.ElementId) && elements.TryGetValue(c.ElementId, out var x))
                    (best, owner, near, most) = (f.Face, f.Result.Element, x, c.Area);
        if (best == null || owner == null || near == null || best.Class == FaceClass.Top) return (null, 0);
        var pair = new PourOrder.Pair(PourOrder.KeyOf(owner, Options), PourOrder.KeyOf(near, Options), PourOrder.Kind.Keep,
                                      best.Class == FaceClass.Side ? PourOrder.Scope.Side : PourOrder.Scope.All);
        var order = PourOrder.From(Rules);
        int at = order.Pairs.FindIndex(p => p.Owner == pair.Owner && p.Other == pair.Other);
        if (at >= 0) order.Pairs[at] = pair;
        else order.Pairs.Add(pair);
        int n = 0;
        foreach (var r in Results)
        {
            if (PourOrder.KeyOf(r.Element, Options) != pair.Owner) continue;
            foreach (var f in r.Faces)
                if (f.Class != FaceClass.Top && f.Normal.Length == 3 && f.Covers.Concat(f.Cut).Any(c =>
                        elements.TryGetValue(c.ElementId, out var x) && PourOrder.KeyOf(x, Options) == pair.Other
                        && Options.ActiveRules?.JointOf(r.Element.Id, f.Normal[0], f.Normal[1], f.Normal[2], f.PlaneD, c.ElementId) == null
                        && order.Keeps(r.Element, x, f.Class, c.Area, f.Polygon, Options)))
                    n++;
        }
        return (pair, n);
    }
}
