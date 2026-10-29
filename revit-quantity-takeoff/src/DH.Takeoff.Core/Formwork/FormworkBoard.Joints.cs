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
            _meshes = new Dictionary<string, View3D.MeshData>(StringComparer.Ordinal);
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
    public int SetJoints(IReadOnlyList<FormworkPiece> pieces, IReadOnlyCollection<long> others, bool ignore)
    {
        if (Remeasure == null || others.Count == 0) return 0;
        string before = Snapshot();
        bool switched = EnsureUser();
        int n = 0;
        foreach (var face in FacesOf(pieces))
            foreach (long other in face.Covers.Concat(face.Cut).Concat(face.Ignored).Select(c => c.ElementId).Distinct().Where(others.Contains))
                if (JointRecord.Of(face, other, ignore) is { } record && Rules.JointOf(record.Element, record.Nx, record.Ny, record.Nz, record.D, other) != ignore)
                {
                    Rules.SetJoint(record);   // 이미 같은 값이면 적지 않는다(되돌릴 일 · 다시 재기가 헛되이 생기지 않게 — 최종 검토 낮음)
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
}
