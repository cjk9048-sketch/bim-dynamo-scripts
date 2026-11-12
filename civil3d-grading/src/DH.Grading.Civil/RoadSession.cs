using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.GraphicsInterface;
using DH.Grading.Core;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;
using CivilDb = Autodesk.Civil.DatabaseServices;

namespace DH.Grading.Civil;

/// <summary>★★[v110 · 계획_v110.md 3.2 · 계획 검토 M5 · M6] 진입도로 «실시간» — 노선 · 계획 종단 · 바탕면이 바뀌면 다시 계산해 임시 선(도면에 안 남음) + 숫자 창.
/// <para>바뀜 감지 = Database.ObjectModified로 그 ID만 «바뀜» 표시(처리기 안에서는 읽지 않음 — 재진입 금지) →
/// 0.35초 시계가 «이 도면이 앞 · 명령 없음 · 우리가 쓰는 중 아님»일 때 문서를 잠그고 한 번 계산(그립 편집 · 종단 격자 편집 · 창 값 모두).</para>
/// <para>미리보기 = 임시 선 몇 가닥(가장자리 · 데이라잇 · 옹벽 · 제안 옹벽 빨강 · 충돌 빨강)을 통째로 바꿔 건다(PickMark처럼 Main 모드 · 넣기 실패 확인 · 끝낼 때 걷음).</para></summary>
internal sealed class RoadSession
{
    static readonly Dictionary<Document, RoadSession> _all = new();
    internal static RoadSession? Of(Document? doc) => doc != null && _all.TryGetValue(doc, out var s) ? s : null;
    internal static event Action<RoadSession?>? Changed;     // 도킹창이 듣는다

    readonly Document _doc;
    internal ObjectId AlignId, PlanId, BaseProfId, BaseSurfId;
    ObjectId _xrecId, _limitId;             // 설계 XRecord(실행 취소로 그것만 바뀜) · 사업 경계 폴리선
    internal RoadDesign Design;
    internal string Name = "";
    internal RoadGrade.Result? Last;
    /// <summary>★[v110.3] 지난 계산의 사면선 · 소단선 · 옹벽선(Core 좌표 = 도면 좌표 · 측점은 Core 측점 — 노선 측점 = + S0).</summary>
    internal List<RoadGrade.TierLine> Lines = new();
    internal double S0;                     // Core 측점 0 = 노선 측점 S0(실제 첫 표본)
    /// <summary>★★[도로 마무리 ① · 계획 5.6 · Q6] 도로 토막(빈 구간이면 둘 이상) — 토막마다 Core 측점 0(노선 측점) · 입력 · 결과 · 선(그 토막 Core 측점).
    /// 토막이 하나면 <see cref="Last"/> · <see cref="Lines"/> · <see cref="S0"/>와 같다(지금과 같음).</summary>
    internal List<Piece> Pieces = new();
    internal sealed class Piece
    {
        public double S0, S1; public RoadGrade.Input Inp = null!; public RoadGrade.Result? R; public List<RoadGrade.TierLine> Lines = new(); public string Note = "";
        /// <summary>★[v116 · 고리 엔진 계획 3판 §5] 이 토막을 고리 엔진으로 셈(미리보기 — 노리선 · 선) · null = 옛 엔진.</summary>
        public RoadRing.Built? Ring;
        /// <summary>★[v116 · 고리 엔진 §5] [사면→옹벽] 주인 구간 · 기록 고르기용 옛 엔진 결과(ProfMain · Trans · Recs — 고리 결과에는 없음) — 고리 토막만 한 번 셈.</summary>
        public RoadGrade.Result? OldR;
        public RoadGrade.Result OldRun() => Ring == null && R != null ? R : OldR ??= RoadGrade.Run(Inp);
    }

    // ── ★★★[v116 · 고리 엔진 계획 3판 §4 · §5 · D2] 엔진 고르기 · 길 이름 ──
    /// <summary>고리 엔진 길 이름(노트 머리 «고리 면 — …» · 설계 기록 SurfacePath).</summary>
    internal const string RingPath = "고리 면";
    /// <summary>★[v116 · D2] 벽 기록이 하나도 없는 토막만 고리 엔진(옹벽 든 토막은 옛 엔진 그대로 — 비트 같음).</summary>
    internal static bool RingOk(RoadGrade.Input inp) => inp.Walls.Count == 0 && inp.TierWalls.Count == 0 && inp.LineWalls.Count == 0;
    /// <summary>★★[v116.7 · JACK 1110 «옹벽 추가 — 어디든 폴리선을 그으면 작성이 되어야» · 계획 `2026-11-10_진입도로_옹벽2단계_고리면.md`] 도로 면 = 고리 엔진(추가한 선 옹벽은 그 위에 얹음).
    /// 가장자리 옹벽 · [사면→옹벽] 벽 기록이 있는 토막만 옛 엔진(옛 판은 선 옹벽만 있어도 옛 엔진 — 반경 7 m 굽이에서 옛 면이 깨져 합성이 막혔다).</summary>
    internal static bool RingBase(RoadGrade.Input inp) => inp.Walls.Count == 0 && inp.TierWalls.Count == 0;
    /// <summary>★[v116] 면 노트 머리 → 길 이름(고리 면 · 선 따라 TIN · 칸 면 · 선 면) — 옛 노트는 옛 판정 그대로.</summary>
    internal static string PathOf(string note) => note.StartsWith(RingPath) ? RingPath : note.StartsWith("선 따라 TIN") ? "선 따라 TIN" : note.StartsWith("⚠") ? "칸 면" : "선 면";
    /// <summary>★[v116] 설계 기록 SurfacePath(토막 차례 «|» 목록 · 옛 기록 = 길 하나 = 모든 토막)의 토막 k 길 · 없으면 null(옛 엔진).</summary>
    internal static string? PathAt(string? list, int k)
    {
        if (string.IsNullOrEmpty(list)) return null;
        var a = list.Split('|');
        return a.Length == 1 ? a[0] : k >= 0 && k < a.Length ? a[k] : null;
    }
    /// <summary>★[v116 · 고리 엔진] 한 토막 — 옛 <see cref="RoadGrade.Run"/>과 같은 범위 넓힘(격자 끝에 닿으면 1.6배 · 두 번까지) · <paramref name="used"/> = 마지막 범위(입력 범위는 되돌림).
    /// <paramref name="build"/> = 세모까지(<see cref="RoadRing.Build"/> · 0.25 m) · 아니면 미리보기(<see cref="RoadRing.Preview"/> · 1 m).</summary>
    internal static RoadRing.Built RingRun(RoadGrade.Input inp, bool build, out double used)
    {
        double reach0 = inp.Reach;
        try
        {
            var b = build ? RoadRing.Build(inp) : RoadRing.Preview(inp, 1.0);
            for (int k = 0; k < 2 && b.Ok && b.ReachHit; k++) { inp.Reach *= 1.6; b = build ? RoadRing.Build(inp) : RoadRing.Preview(inp, 1.0); }
            used = inp.Reach;
            return b;
        }
        finally { inp.Reach = reach0; }
    }
    /// <summary>★[v116] 지난 [업데이트]가 고리 면으로 지은 토막의 꾸러미(노리선용 가벼운 사본 — 도로 면 · 평가기 · 선) · 그 면 지문. [노리선]이 지문이 같으면 다시 안 셈.</summary>
    internal (string Sig, RoadRing.Built?[] B)? RingCache;
    /// <summary>★★[v116.9 · JACK 1110 4-1 «평면 소단선이 갈림 자리에서 꺾임»] 그림 · 미리보기 · 고르기용 소단선 · 사면선(<see cref="RoadRing.DisplayLines"/>) —
    /// 면을 짓는 선(b.Lines · 노리선 · 3D 소단 자리)은 그대로. 갈림 자리에서 «가장 가까운 둘레» 고리를 따라간 거짓 조각은 빼고, 선이 없던 실제 소단 턱(면 규칙)은 더함.
    /// 못 하면 받은 선 그대로(셈은 진단 로그).</summary>
    List<RoadGrade.TierLine> DispLines(RoadRing.Built b, List<RoadGrade.TierLine> lines, bool preview = false)
    {
        try
        {
            var o = RoadRing.DisplayLines(b, lines, out var st, addBench: !preview);
            if (!preview) try { DiagLog.Append(string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0}[진입도로] {1} — 표시 소단선 · 사면선: 점 {2:N0} · 거짓 조각 점 뺌 {3:N0} · 소단 턱 더함 {4}줄 {5:F1} m · {6} ms", System.Environment.NewLine, Name, st.Pts, st.Dropped, st.AddedLines, st.AddedLen, st.Ms)); } catch { }
            return o;
        }
        catch (System.Exception ex) { try { DiagLog.Append(System.Environment.NewLine + $"[진입도로] {Name} — ⚠표시 소단선 못 셈({ex.Message}) — 짓는 선 그대로"); } catch { } return lines; }
    }

    static RoadRing.Built Light(RoadRing.Built b) => new() { Deck = b.Deck, Ev = b.Ev, Lines = b.Lines, Daylight = b.Daylight, Ok = b.Ok, Note = b.Note };   // 격자 · 세모는 버림(Ticks는 안 씀) · ★[v116.5] 데이라잇은 노리선 멈춤 선
    /// <summary>★[v116] 지은 면에 덧붙일 고리 면 물러섬 까닭(빈 글 = 없음) — CreateSurface가 알림 끝에.</summary>
    string _pendingRingNote = "";
    /// <summary>★[도로 마무리 ①] 지금 도로 구간(노선 측점) · 끝 판정 · 제안 글(EndsOn 거짓 = [끝 맞추기] 제안) · 끝 맞춤 필요(EndsOn 참 = [업데이트] 깜박).</summary>
    internal List<(double A, double B)> Ranges = new();
    internal RoadEnds.Result? Ends;
    internal string EndsProposal = "", EndsStale = "";
    /// <summary>★[도로 마무리 ② · 계획 6.7] ■ 결과 측구 수량 한 줄(측구선 길이 · 콘크리트 — 도로 구간 안 몫).</summary>
    internal string DitchQty = "";
    /// <summary>★[v113.0 → ★v114.3 · JACK 1105 진입도로 옹벽 추가] 쉬는 선 옹벽(새 판 칸 · 다른 PC 옛 애드인이 다시 저장해 값이 빠짐 · 지난 [업데이트]에서 못 지음 · 빈 구간) — 상태 줄 ⚠(기록은 안 지움).</summary>
    internal string BoundNote = "";
    /// <summary>★[v114.3] 지난 [업데이트](MakeSurface)가 지은 선 옹벽 · 그때 입력 지문 · 벽 반영 뒤 부피(절 · 성 · 최대 절 · 성) — 지문이 같을 때만 선 자르기 · 팔레트 부피에 쓴다.</summary>
    internal RoadGrade.WallSurface? Walls; internal string WallsSig = ""; internal (double Cut, double Fill, double MaxCut, double MaxFill) WallVol;
    /// <summary>★[v114.3] 이 도면에서 옛 판 1 폴리선 옹벽을 지웠다는 한 줄을 이미 남겼나(도면마다 한 번).</summary>
    bool _oldWallNoted;
    /// <summary>★[v114.3 · 계획 2판 4절 «Classify(Bd = 벽 없는 지문 캐시)»] 벽 없는 면 P0 — 지난 [업데이트] 또는 [옹벽 추가] 판정이 셈(벽 뺀 입력 지문이 같으면 다시 안 셈).</summary>
    internal (string Key, RoadGrade.Input Inp, RoadGrade.Result R, List<WallDaylight.Tri> P0, List<RoadGrade.TierLine> Lines, string Path, List<List<(double X, double Y, double Z)>> Dl, RoadRing.Built? Ring)? P0Cache;
    /// <summary>★[v115 · 설치 전 검토 R1] 지금 짓는 면의 벽 번호 · 길 — CreateSurface가 설계 기록에 적는다.</summary>
    int[]? _pendingWallNos; string? _pendingPath;

    /// <summary>벽 뺀 입력 지문(선 옹벽 기록만 바뀌면 P0는 그대로).</summary>
    internal string KeyNoWalls(List<Piece> pcs)
    {
        var keep = pcs.Select(p => p.Inp.LineWalls).ToList();
        try { foreach (var p in pcs) p.Inp.LineWalls = new(); return SigOfAll(pcs, BaseSurfId); }
        finally { for (int i = 0; i < pcs.Count; i++) pcs[i].Inp.LineWalls = keep[i]; }
    }

    /// <summary>★[v114.3] 지금 설계의 벽 없는 면 P0(MakeSurface와 같은 입력 — 칸 0.5 · 시작 범위 · 사업 경계) · 그 길 이름. 빈 구간 도로면 까닭.</summary>
    /// <remarks>★[v116.7] 고리 면이면 <c>Ring</c> = 그 꾸러미(선 옹벽 단 표 · <see cref="RoadGrade.ApplyWalls"/>의 ring) · 옛 엔진이면 null.</remarks>
    internal (RoadGrade.Input? Inp, RoadGrade.Result? R, List<WallDaylight.Tri>? P0, List<RoadGrade.TierLine>? Lines, string Path, string Why, RoadRing.Built? Ring) P0Now()
    {
        var db = _doc.Database;
        var d = Fresh();
        EnsureBase();
        List<Piece> pcs; List<(double, double)>? limit;
        using (var tr = db.TransactionManager.StartTransaction())
        {
            ComputeEnds(tr, db);
            pcs = MakePieces(tr, AlignId, PlanId, d, EngineGround(tr, db), Ranges);
            limit = ReadLimit(tr);
            tr.Commit();
        }
        if (pcs.Count != 1) return (null, null, null, null, "", "빈 구간이 있는 도로(토막 둘 이상)는 이번 판에 옹벽 추가를 못 합니다", null);
        double reach = Last != null && Last.Error.Length == 0 && !Last.ReachHit && Last.ReachUsed > 40 ? Last.ReachUsed : 40;
        var inp = pcs[0].Inp; inp.Cell = SurfCell; inp.Reach = reach; inp.Limit = limit;
        string key = "";
        try { key = KeyNoWalls(pcs); } catch { }
        if (key.Length > 0 && P0Cache is { } c && c.Key == key)
        {
            c.Inp.LineWalls = inp.LineWalls;
            // ★[v115 · R7] 캐시한 r의 데이라잇은 Try가 벽 든 것으로 바꿨을 수 있다 — 벽 없는 사본으로 되돌려 넘긴다
            c.R.DaylightZ = c.Dl.Select(l => l.ToList()).ToList(); c.R.Daylight = c.R.DaylightZ.Select(l => l.Select(q => (q.X, q.Y)).ToList()).ToList();
            RoadGrade.GridSet(c.R, null);   // ★[v116.7] 앞 [업데이트]가 벽 층을 얹은 격자 — 벽 없는 P0로 되돌림(고리 결과는 B1 층 없음 · 옛 결과는 B1 그대로)
            return (c.Inp, c.R, c.P0, c.Lines, c.Path, "", c.Ring);
        }
        // ★★[v116.7] 고리 면 먼저(MakeSurface와 같은 입력 · 같은 범위 넓힘) — 못 지으면 옛 엔진(지금 그대로).
        //   ★[설치 전 검토 L] 지금 면이 이 설계 그대로인데(지문 같음) 옛 엔진 길로 지어졌으면(Civil이 고리 면을 거절 등) 판정도 옛 엔진 — 미리보기(CalcPiece)와 같은 법
        bool oldBuilt = false;
        try { oldBuilt = d.SurfaceSig.Length > 0 && PathAt(d.SurfacePath, 0) is string pk0 && pk0 != RingPath && SigOfAll(pcs, BaseSurfId) == d.SurfaceSig; } catch { }
        if (RingBase(inp) && !oldBuilt)
        {
            try
            {
                var b = RingRun(inp, true, out double used);
                string whyR = RingWhy(b);
                if (b.Ok && b.Tris.Count > 0)
                {
                    var rr = RoadRing.ToResult(b, inp); rr.ReachUsed = used;
                    if (rr.Error.Length == 0)
                    {
                        // ★[설치 전 검토 M] 캐시엔 가벼운 사본(0.25 m 격자 · 세모 버림 — 단 표는 셈만 씀 · P0 세모는 따로 둠)
                        if (key.Length > 0) P0Cache = (key, inp, rr, b.Tris, b.Lines, RingPath, rr.DaylightZ.Select(l => l.ToList()).ToList(), Light(b));
                        return (inp, rr, b.Tris, b.Lines, RingPath, "", b);
                    }
                    whyR = rr.Error;
                }
                try { DiagLog.Append($"\n[진입도로] {Name} — 옹벽 판정 P0 — ⚠고리 면 못 지음({whyR}) → 옛 엔진"); } catch { }
            }
            catch (System.Exception exR) { try { DiagLog.Append($"\n[진입도로] {Name} — 옹벽 판정 P0 — ⚠고리 면 예외 → 옛 엔진 — {exR}"); } catch { } }
        }
        var r = RoadGrade.Run(inp);
        if (r.Error.Length > 0) return (null, null, null, null, "", "도로 계산 실패 — " + r.Error, null);
        var p0 = RoadGrade.ExactSurface(inp, r, out string note, out var lines);
        string path = PathOf(note);
        if (key.Length > 0) P0Cache = (key, inp, r, p0, lines, path, r.DaylightZ.Select(l => l.ToList()).ToList(), null);
        return (inp, r, p0, lines, path, "", null);
    }

    /// <summary>선 옹벽마다 — 새 판(쉼) · Sig 빠짐(쉼) · 빈 구간(막음) · 지난 [업데이트] 상태(못 지음 · 쉼).</summary>
    internal string BoundNoteOf(RoadDesign d, List<Piece> pcs)
    {
        var lw = d.CoreLineWalls(out var tooNew, out var sigBad);
        var bad = new List<string>();
        foreach (int no in tooNew) bad.Add($"⚠추가한 옹벽 {no} — 이 애드인보다 새 판 기록이라 안 그림");
        foreach (int no in sigBad) bad.Add($"⚠추가한 옹벽 {no} — 다른 PC 옛 애드인이 다시 저장해 값이 빠짐 — 다시 그리세요");
        if (lw.Count == 0) return string.Join(" · ", bad);
        if (pcs.Count > 1) { bad.Add($"⚠추가한 옹벽 {lw.Count}개 — 빈 구간이 있는 도로는 이번 판에 선 옹벽을 안 지음"); return string.Join(" · ", bad); }
        if (Walls != null && WallsSig.Length > 0 && WallsSig == d.SurfaceSig)
            foreach (var w in Walls.Walls) if (!w.Ok) bad.Add(w.Text + " · 다시 그리거나 지우세요([옹벽→사면] W)");
        return string.Join(" · ", bad);
    }
    List<RoadEnds.Floor>? _floors; string _floorNote = "";
    /// <summary>★★[도로 마무리 ① · 계획 5.5] 모든 도로 끝 판정(가지 사슬) · 가지 바탕(바탕면 + 본 도로 면 합침 — 열쇠가 같으면 다시 안 합침) · 그 알림.</summary>
    List<RoadEnds.Result>? _allEnds;
    CachedGroundSurface? _eng; string _engKey = "", _engNote = "", _engNoteKept = "";
    internal double GradeNow;               // 계획 종단 지금 최대 경사(PVI 사이 · 사람이 옮긴 뒤)
    /// <summary>★[v111.7.2 · JACK 1102] 20 %(<see cref="ProfileSheetRules.WarnGrade"/>) 넘는 접선 알림 한 줄(빈 글 = 없음) — ■ 결과 빨간 줄.</summary>
    internal string GradeWarn = "";
    /// <summary>★[v111.7.2] 지난 계산에 넘었나(null = 처음 계산 — 열 때는 명령창에 안 찍음 · 고친 뒤 처음 넘을 때만 한 줄).</summary>
    bool? _steepWas;
    internal string Note = "";
    string _baseNote = "";                  // 바탕면을 다시 고른 일(다음 계산 글에 한 번)
    internal bool Suppress;                // 우리가 노선 · 종단을 쓰는 동안 바뀜 표시 막음
    CachedGroundSurface? _base; bool _baseDirty = true, _dirty;
    readonly System.Windows.Threading.DispatcherTimer _timer;
    readonly List<Entity> _marks = new();

    /// <summary>★★★[설치 전 검토 · 높음] 못 걷은 임시 선을 붙잡아 두는 자리(PickMark와 같은 규칙) — 놓아 주면 소멸자가 네이티브를 지워
    /// 임시 그래픽 관리자가 죽은 포인터를 쥔다(그 도면을 다시 그릴 때 AutoCAD가 죽음). 도면이 닫힐 때 목록에서만 뺀다(해제 안 함).</summary>
    static readonly List<(Document Doc, Entity E)> _held = new();
    static void Hold(Document doc, Entity e)
    {
        try { GC.SuppressFinalize(e); } catch { }
        _held.Add((doc, e));
    }

    RoadSession(Document doc, ObjectId aid, RoadDesign d)
    {
        _doc = doc; AlignId = aid; Design = d;
        _timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        _timer.Tick += (_, __) => Tick();
    }

    /// <summary>노선의 설계(XRecord)로 세션을 연다 — 같은 도면의 앞 세션은 닫는다.</summary>
    /// <param name="fresh">★[v111.7.1 · 검토 낮음] 이 명령이 막 만든 도로(DHROAD · DHROADDRAW 새 노선) — 열 때부터 옛것 표시를 안 함(그린 적이 없음).
    /// ★[v111.10 · 계획_초기화.md 3.4] v111.7.2부터 아무 데도 안 쓰던 값 → <see cref="Making"/>(첫 계산 앞에) — 부르는 명령이 끝에 <see cref="EndMaking"/>.</param>
    internal static RoadSession Start(Document doc, ObjectId aid, bool fresh = false)
    {
        Stop(doc);
        var db = doc.Database;
        RoadDesign d; string name;
        using (var tr = db.TransactionManager.StartTransaction())
        {
            d = RoadDesign.Read(tr, aid) ?? throw new InvalidOperationException("이 노선에는 진입도로 설계가 없습니다");
            name = ((CivilDb.Alignment)tr.GetObject(aid, OpenMode.ForRead)).Name;
            tr.Commit();
        }
        var s = new RoadSession(doc, aid, d) { Name = name, Making = fresh };
        s.PlanId = Handle(db, d.PlanProfile); s.BaseProfId = Handle(db, d.BaseProfile);
        if (s.PlanId.IsNull) throw new InvalidOperationException("계획 종단을 못 찾았습니다(지웠나요?) — [진입도로]로 폴리선부터 다시");
        // ★[v111.2 · 전체 검토 H2] 노선을 복사하면 설계 기록(확장 사전)이 따라와 원래 노선의 종단 · 도로 면을 가리킨다 — 열면 남의 종단을 고치고 남의 도로 면을 지움 → 안 염
        using (var tr = db.TransactionManager.StartTransaction())
        {
            bool Foreign(ObjectId pid) => !pid.IsNull && tr.GetObject(pid, OpenMode.ForRead) is CivilDb.Profile p && p.AlignmentId != aid;
            bool bad = Foreign(s.PlanId) || Foreign(s.BaseProfId);
            tr.Commit();
            if (bad) throw new InvalidOperationException($"«{name}»의 진입도로 기록이 다른 노선의 종단을 가리킵니다(노선을 복사했나요?) — 원래 노선을 열거나, 이 노선을 [진입도로]로 고르면 새로 만듭니다");
        }
        // ★[설치 전 검토 · 중간] 바탕면을 이름으로 못 찾으면(초기화 · 재정지로 바뀜) 지금 도면에서 다시 고른다
        if (!s.ResolveBase()) throw new InvalidOperationException("바탕면(정지면_DH 또는 원지반)이 없습니다");
        using (var tr = db.TransactionManager.StartTransaction()) { s._xrecId = RoadDesign.XrecId(tr, aid); tr.Commit(); }
        s._limitId = Handle(db, d.LimitHandle);
        _all[doc] = s;
        // ★[v111.7.1 · JACK 1102 B-4] 진입도로를 여는 명령 = 이 도면에서 일함 — 설정을 이 도면으로
        try { GradingSettings.SyncToDocument(doc); } catch { }
        db.ObjectModified += s.OnModified;
        db.ObjectErased += s.OnErased;
        // ★[v111.7 · 재검토 중간] 실행 취소 · 다시 실행 명령이 끝나면 — 지켜보는 객체를 안 건드렸어도(선 그리고 U 등) 다시 계산(알림 · 열쇠를 지금으로)
        doc.CommandEnded += s.OnCmdEnd; doc.CommandCancelled += s.OnCmdEnd; doc.CommandFailed += s.OnCmdEnd;
        AcadApp.DocumentManager.DocumentToBeDestroyed -= OnDocGone;      // ★[설치 전 검토 L1] 여러 번 걸리지 않게
        AcadApp.DocumentManager.DocumentToBeDestroyed += OnDocGone;
        s._timer.Start();
        s.Recompute();
        return s;
    }

    internal static void Stop(Document doc)
    {
        if (!_all.TryGetValue(doc, out var s)) return;
        _all.Remove(doc);
        try { s._timer.Stop(); } catch { }
        try { doc.Database.ObjectModified -= s.OnModified; } catch { }
        try { doc.Database.ObjectErased -= s.OnErased; } catch { }
        try { doc.CommandEnded -= s.OnCmdEnd; doc.CommandCancelled -= s.OnCmdEnd; doc.CommandFailed -= s.OnCmdEnd; } catch { }   // ★[v111.7]
        s.ClearMarks();
        Changed?.Invoke(null);
    }

    static void OnDocGone(object? sender, DocumentCollectionEventArgs e)
    {
        try { Stop(e.Document); } catch { }
        _held.RemoveAll(h => ReferenceEquals(h.Doc, e.Document));      // 관리자도 같이 사라진다 — 목록에서만 뺌(소멸자는 꺼 둠)
    }

    /// <summary>바탕면 ID — 설계에 적힌 이름 → 없으면 지금 도면에서 다시 고르고(정지면_DH · 원지반) 설계 이름을 고친다.</summary>
    bool ResolveBase()
    {
        var db = _doc.Database;
        var cdoc = Autodesk.Civil.ApplicationServices.CivilDocument.GetCivilDocument(db);
        var id = FindSurface(db, cdoc, Design.BaseSurface);
        if (id.IsNull)
        {
            var (pid, pname) = RoadBuilder.PickBase(db, cdoc);
            if (pid.IsNull) return false;
            id = pid;
            if (Design.BaseSurface != pname)
            {
                _baseNote = $"바탕면 '{Design.BaseSurface}'이 없어 '{pname}'으로 바꿈";
                Design.BaseSurface = pname;
                try { SaveDesign(); } catch { }
            }
        }
        if (id != BaseSurfId) { BaseSurfId = id; _base = null; }
        return true;
    }

    /// <summary>★[v111.2 · 전체 검토 H2] 설계 기록이 다른 노선의 종단을 가리키나(노선 복사 — 확장 사전이 따라옴).</summary>
    internal static bool ForeignRecord(Transaction tr, ObjectId aid, RoadDesign d)
    {
        var db = aid.Database;
        foreach (var h in new[] { d.PlanProfile, d.BaseProfile })
        {
            var pid = Handle(db, h);
            try { if (!pid.IsNull && tr.GetObject(pid, OpenMode.ForRead) is CivilDb.Profile p && p.AlignmentId != aid) return true; } catch { }
        }
        return false;
    }

    static ObjectId Handle(Database db, string h)
    {
        try { if (h.Length > 0 && db.TryGetObjectId(new Autodesk.AutoCAD.DatabaseServices.Handle(Convert.ToInt64(h, 16)), out var id) && !id.IsErased) return id; } catch { }
        return ObjectId.Null;
    }

    static ObjectId FindSurface(Database db, Autodesk.Civil.ApplicationServices.CivilDocument cdoc, string name)
    {
        using var tr = db.TransactionManager.StartTransaction();
        foreach (ObjectId sid in cdoc.GetSurfaceIds())
            try { if (tr.GetObject(sid, OpenMode.ForRead) is CivilDb.TinSurface t && t.Name == name) return sid; } catch { }
        return ObjectId.Null;
    }

    void OnModified(object? sender, ObjectEventArgs e)
    {
        if (Suppress) return;
        try { FlagObj(e.DBObject); } catch { }
    }

    /// <summary>지움 · 되살림(실행 취소)도 같은 표시 — 노선 · 계획 종단이 지워졌으면 다음 계산에서 세션을 닫는다.</summary>
    void OnErased(object? sender, ObjectErasedEventArgs e)
    {
        if (Suppress) return;
        try { FlagObj(e.DBObject); } catch { }
    }

    /// <summary>★[v111.7 · 재검토 중간] U · UNDO · REDO · MREDO가 끝났다 — 지켜보는 객체를 안 건드린 실행 취소(선 그리고 U)도 · 지켜보지 않는 것(횡단뷰 · 도로 면)만 되돌린 것도 —
    /// 다시 계산해 알림 · 열쇠를 지금으로(노리선이 되살아났는지도).</summary>
    void OnCmdEnd(object? sender, CommandEventArgs e)
    {
        try
        {
            string c = (e.GlobalCommandName ?? "").ToUpperInvariant();
            if (c == "U" || c == "UNDO" || c == "REDO" || c == "MREDO") { _dirty = true; _recheckTicks = true; }
        }
        catch { }
    }

    void FlagObj(DBObject o) { var id = o.ObjectId; if (Watched(id)) Flag(id); }
    bool Watched(ObjectId id) => id == AlignId || id == PlanId || id == _xrecId || id == _limitId || id == BaseSurfId;

    void Flag(ObjectId id)
    {
        if (id == AlignId || id == PlanId || id == _xrecId || id == _limitId) _dirty = true;    // ★[설치 전 검토 · 중간 · L8] 설계 XRecord(실행 취소) · 사업 경계 폴리선도
        else if (id == BaseSurfId) { _dirty = true; _baseDirty = true; }
    }

    /// <summary>창 값이 바뀌었다 — 바로 다시(문서는 여기서 잠금).</summary>
    internal void MarkDirty() => _dirty = true;

    /// <summary>★[v111.2 · 전체 검토 M2] 바뀜 표시가 남아 있으면 지금 다시 계산 — 명령이 시계(350 ms)보다 먼저 들어와 옛 미리보기로 고르지 않게.</summary>
    internal void RecomputeIfDirty() { if (_dirty) { _dirty = false; RecomputeCore(); } }

    /// <summary>★[v111.7.2] 시계(350 ms) — 바뀜 표시가 있으면 다시 계산(미리보기 선 · 알림 · [업데이트] 깜박). 명령 중 · 다른 도면이 앞이면 기다림. 저절로 다시 만들기는 없음.</summary>
    void Tick()
    {
        if (Suppress) return;
        try
        {
            if (AcadApp.DocumentManager.MdiActiveDocument != _doc) return;
            if (!string.IsNullOrEmpty(_doc.CommandInProgress)) return;
        }
        catch { return; }
        if (_dirty) { _dirty = false; RecomputeCore(); }
    }

    // ── ★★★[v111.7.2 · JACK 1102 «측점(PVI) 움직일 때마다 모든 게 재작성되니 오래 걸린다 · 평면 영향선(미리보기 선)은 빠르니 그것만 바로 바뀌고, 마지막에 팝업창 단추를 누르면 한꺼번에 업데이트»] ──
    //   v111.7 · v111.7.1 저절로 다시 만들기(시계가 DHROADAUTOREDRAW를 보냄 · 입력 시각 거르개 · 해 본 바뀜 기억)는 없앰.
    //   고치면 미리보기 선만 바로(시계 350 ms 다시 계산) · ■ 결과 [업데이트](DHROADUPDATE)가 지표면 → 종단도 → 횡단도를 한 명령(실행 취소 한 번)으로.
    /// <summary>★[v111.7 · v111.7.1 지표면 몫 붙임] 지금 바뀜 열쇠 — 지표면(옛것 · 없음 + 지금 설계 지문) · 종단도 · 횡단도 알림 + 그 판정에 든 값(빈 글 = 모두 지금과 같음).
    /// ★[v111.7.2] [업데이트] 단추 깜박임 · 실패 까닭을 다는 자리.</summary>
    internal string StaleKey = "";
    /// <summary>★[v111.7.2] [업데이트]를 해 봤는데도 남은 바뀜 열쇠 → 까닭(실패 · 자리 모름 · 겹침) — ■ 결과 상태 줄. 다시 고치면 열쇠가 바뀌어 «고친 것이 있습니다»로 돌아감.</summary>
    readonly Dictionary<string, string> _failed = new(StringComparer.Ordinal);

    /// <summary>★[v111.7.2] ■ 결과 상태 줄 까닭 — 바뀐 것이 없으면 빈 글 · 계산이 깨졌으면 «계산 오류» · [업데이트]가 못 한 바뀜이면 그 까닭 · 아직 안 눌렀으면 빈 글.</summary>
    internal string FailWhy
    {
        get
        {
            if (StaleKey.Length == 0) return "";
            if (Last == null || Last.Error.Length > 0) return "계산 오류";
            return _failed.TryGetValue(StaleKey, out var w) ? w : "";
        }
    }

    /// <summary>★[v111.7.2] 다시 만들 바뀜이 있다(계산 됨) — [노리선]이 먼저 같은 차례([업데이트])를 돌린다.</summary>
    internal bool Pending => StaleKey.Length > 0 && Last != null && Last.Error.Length == 0;

    /// <summary>★[v111.7.2] [업데이트] 끝 — 다시 계산한 뒤에도 바뀜 열쇠(<paramref name="post"/>)가 남았으면 그 까닭(<paramref name="why"/> — 실패 · 자리 모름 · 겹침 · 빈 글 = «다시 해도 남음»).</summary>
    internal void NoteUpdate(string post, string why)
    {
        if (post.Length > 0) _failed[post] = why.Length > 0 ? why : "다시 해도 남음";
        Changed?.Invoke(this);
    }

    /// <summary>★[v111.7.1] 명령 안에서 실패했다(DHROAD 첫 지표면 등) — 지금 열쇠를 그 까닭으로(상태 줄에 까닭 · [업데이트]로 다시).</summary>
    internal void NoteFail(string why)
    {
        if (StaleKey.Length == 0) return;
        _failed[StaleKey] = why;
        Changed?.Invoke(this);
    }

    /// <summary>★[v111.7.1 · 검토 중간 2] 다시 만든 지표면이 지운 노리선 수(부지 · 도로 — 다음 [노리선]까지) — 도킹창 상태 줄 «노리선 지움 n — [노리선]으로 다시».</summary>
    internal int TicksErased;
    bool _recheckTicks;
    /// <summary>[노리선]을 그렸다 — 상태 줄 알림을 끔.</summary>
    internal void NoteTicksDrawn() { if (TicksErased == 0) return; TicksErased = 0; Changed?.Invoke(this); }
    /// <summary>모형 공간에 노리선(부지 · 도로)이 하나라도 있나 — 실행 취소로 되살아났는지(«노리선 지움» 알림 끄기).</summary>
    static bool AnyTicks(Database db)
    {
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
            foreach (ObjectId id in ms)
            {
                try
                {
                    if (tr.GetObject(id, OpenMode.ForRead) is Entity e && (string.Equals(e.Layer, LayNori, StringComparison.OrdinalIgnoreCase) || string.Equals(e.Layer, SiteNoriLayer, StringComparison.OrdinalIgnoreCase)))
                    { tr.Commit(); return true; }
                }
                catch { }
            }
            tr.Commit();
        }
        catch { }
        return false;
    }

    /// <summary>★[v111.7.1] 무엇을 다시 만들까(<see cref="RoadAutoRules.Choose"/>) — 지표면(옛것 · 없음) · 종단도(알림 + 지금 놓인 자리 + 다른 종단도와 안 겹침) ·
    /// 횡단도(그린 적 있음 + 지금 놓인 자리 + 그릴 수 있음 — 지표면을 다시 만들면 그 뒤에 다시 잼). 문서는 부르는 쪽이 잠금. ★[v111.7.2] [업데이트]가 못 한 까닭(자리 모름 · 겹침)을 적을 때.</summary>
    internal RoadAutoRules.Plan UpdatePlan()
    {
        var db = _doc.Database;
        Point3d? sp = null; bool xsecPlace = false, xsecHas = Design.XsecViews.Count > 0;
        using (var tr = db.TransactionManager.StartTransaction())
        {
            if (SheetStale.Length > 0) sp = PlaceNow(tr, db, Design.SheetAt, Design.SheetViews, Design.SheetViewAt);
            if (xsecHas) xsecPlace = PlaceNow(tr, db, Design.XsecAt, Design.XsecViews, Design.XsecViewAt) != null;
            tr.Commit();
        }
        bool overlap = sp is Point3d p && SheetOverlap(p).Length > 0;
        bool surf = SurfaceStale.Length > 0 || SurfaceMissing;
        bool refused = xsecHas && xsecPlace && (XsecStale.Length > 0 || surf) && RoadXsec.Refusal(this, surfaceComing: surf).Length > 0;
        return RoadAutoRules.Choose(SurfaceStale.Length > 0, SurfaceMissing, SheetStale.Length > 0, sp != null, overlap, xsecHas, XsecStale.Length > 0, xsecPlace, refused);
    }

    /// <summary>★[v111.7] 이 도로 종단도를 <paramref name="at"/>에 지금 축척 · 장 수로 그리면 겹치는 다른 종단도(빈 글 = 안 겹침).</summary>
    internal string SheetOverlap(Point3d at)
    {
        var db = _doc.Database;
        var (scale, ve) = RoadSheets.PlannedScale(db, Design.SheetScale);   // ★[v113.1] 도면설정 H · V(자동 H = 1:1000으로 넓게 짐작 · 자동 V = 도곽 안) · ★[v117.5.1] 옛 설정 = 그린 H
        return RoadSheets.Overlap(db, AlignId, at, scale, RoadSheets.SheetCount(db, AlignId, scale), ve);
    }

    /// <summary>★[v111.7.2 검토 낮음] 기록한 뷰(핸들) 중 하나라도 살아 있나 — [업데이트] «처음 놓기» · 상태 줄 «✓ 최신 — …»이 지운 뷰를 있다고 하지 않게.</summary>
    internal static bool AnyAlive(Database db, List<string> handles)
    {
        foreach (var h in handles ?? new List<string>())
            try { if (db.TryGetObjectId(new Handle(Convert.ToInt64(h, 16)), out var id) && !id.IsErased) return true; } catch { }
        return false;
    }

    /// <summary>★[v111.7.2 검토 낮음] 이 세션에서 [업데이트]가 «처음 횡단도» 자리를 이미 물었나(한 번만 묻기).</summary>
    internal bool XsecAsked;

    /// <summary>★[v111.7] 지난 자리(x, y)가 적혀 있나.</summary>
    internal static bool HasPlace(double[] at) => at != null && at.Length >= 2 && double.IsFinite(at[0]) && double.IsFinite(at[1]);

    /// <summary>★[설치 전 검토 M1 · M2] 지금 놓인 자리 — 기록한 첫 · 끝 뷰(<paramref name="views"/> 차례)가 살아 있고 그릴 때 자리(<paramref name="viewAt"/> = [첫 x, y, 끝 x, y])에서
    /// 둘 다 같은 만큼 옮겨졌으면 그만큼 옮긴 자리(사람이 MOVE로 옮겼으면 옮긴 자리) · 지웠거나 · 일부만 옮겼거나 · 옛 기록(뷰 자리 없음)이면 null(자리를 다시 물음).</summary>
    internal static Point3d? PlaceNow(Transaction tr, Database db, double[] at, List<string> views, double[] viewAt)
    {
        if (!HasPlace(at) || views == null || views.Count == 0 || viewAt == null || viewAt.Length < 4) return null;
        Point3d? Loc(string h)
        {
            try
            {
                if (!db.TryGetObjectId(new Handle(Convert.ToInt64(h, 16)), out var id) || id.IsErased) return null;
                var o = tr.GetObject(id, OpenMode.ForRead);
                return o is CivilDb.ProfileView pv ? pv.Location : o is CivilDb.SectionView sv ? sv.Location : null;
            }
            catch { return null; }
        }
        var a = Loc(views[0]); var b = Loc(views[^1]);
        if (a == null || b == null) return null;
        double dx = a.Value.X - viewAt[0], dy = a.Value.Y - viewAt[1];
        if (Math.Abs(b.Value.X - viewAt[2] - dx) > 1e-3 || Math.Abs(b.Value.Y - viewAt[3] - dy) > 1e-3) return null;
        return new Point3d(at[0] + dx, at[1] + dy, 0);
    }

    /// <summary>★[설치 전 검토 M2] 그린 뷰의 첫 · 끝 자리 [x0, y0, x1, y1](기록 차례) — 못 재면 빈 배열(그 기록은 [업데이트]가 자리를 다시 물음).</summary>
    internal static double[] ViewAt(Database db, IList<ObjectId> ids)
    {
        try
        {
            if (ids == null || ids.Count == 0) return Array.Empty<double>();
            using var tr = db.TransactionManager.StartTransaction();
            Point3d L(ObjectId id) => tr.GetObject(id, OpenMode.ForRead) switch { CivilDb.ProfileView pv => pv.Location, CivilDb.SectionView sv => sv.Location, _ => throw new InvalidOperationException() };
            var a = L(ids[0]); var b = L(ids[ids.Count - 1]);
            tr.Commit();
            return new[] { a.X, a.Y, b.X, b.Y };
        }
        catch { return Array.Empty<double>(); }
    }

    /// <summary>한 번 계산 — 노선 · 종단 표본 → Core 도로 정지(칸 1 m) → 임시 선 · 숫자(사람 · 명령이 부르는 길 = <see cref="RecomputeCore"/>와 같음).</summary>
    internal void Recompute() => RecomputeCore();

    /// <summary>다시 계산 — 시계 · 명령 · [업데이트].</summary>
    internal void RecomputeCore()
    {
        // ★[설치 전 검토 · 높음] 임시 선은 지금 보는 도면의 관리자에만 걸고 걷는다 — 뒤에 있는 도면이면 앞으로 올 때 다시(Tick)
        try { if (!ReferenceEquals(AcadApp.DocumentManager.MdiActiveDocument, _doc)) { _dirty = true; return; } } catch { return; }
        var db = _doc.Database;
        try
        {
            using var dl = _doc.LockDocument();
            // ★[설치 전 검토 · 중간] 노선 · 계획 종단이 지워졌으면(DHROAD 실행 취소 · 지움) 세션을 닫는다 — 없는 도로의 선이 남지 않게
            if (AlignId.IsNull || AlignId.IsErased || PlanId.IsNull || PlanId.IsErased)
            {
                try { DiagLog.Append($"\n[진입도로] {Name} — 노선 또는 계획 종단이 지워져 미리보기를 닫음"); } catch { }
                Stop(_doc);
                return;
            }
            // ★[설치 전 검토 · 중간] 설계는 늘 노선에서 다시 읽는다 — 실행 취소로 XRecord만 되돌아가도 메모리 값이 그것을 다시 덮어쓰지 않게
            using (var trD = db.TransactionManager.StartTransaction())
            {
                var d = RoadDesign.Read(trD, AlignId);
                _xrecId = RoadDesign.XrecId(trD, AlignId);
                trD.Commit();
                if (d == null)
                {
                    try { DiagLog.Append($"\n[진입도로] {Name} — 노선에 설계 기록이 없어(실행 취소?) 미리보기를 닫음"); } catch { }
                    Stop(_doc);
                    return;
                }
                Design = d;
            }
            _limitId = Handle(db, Design.LimitHandle);
            EnsureBase();
            double[] X, Y, Z; double s0, s1; string note; List<(double, double)>? limit = null;
            string sheetNote = "", xsecNote = "";
            string stampNow = "", xsecInputs = ""; double wantSheet = 0;   // ★[v111.7] 바뀜 열쇠(StaleKey)에 넣을 판정 값
            // ★[v111 · 설치 전 검토 M6] 옛 판 기록 틈 메움 알림(실행 취소 · [사면 변환]으로 되돌릴 수 있게 자리를 적음)
            string gapNote = Design.GapsMerged > 0
                ? $"옛 옹벽 기록 틈 {Design.GapsMerged}곳 이어 붙임(" + string.Join(", ", Design.GapsAt.Select(g => $"{(g.Side < 0 ? "좌" : "우")} {g.S0:F2}~{g.S1:F2}")) + ")"
                : "";
            if (gapNote.Length > 0) { try { DiagLog.Append($"\n[진입도로] {Name} — {gapNote}"); } catch { } }
            List<Piece> pcs;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                // ★★[도로 마무리 ① · 계획 5.8] 도로 구간(가상 자르기 · 빈 구간) → 토막마다 표본
                ComputeEnds(tr, db);
                pcs = MakePieces(tr, AlignId, PlanId, Design, EngineGround(tr, db), Ranges);
                (X, Y, Z, s0, s1, note) = (pcs[0].Inp.X, pcs[0].Inp.Y, pcs[0].Inp.Z, pcs[0].S0, pcs[^1].S1, pcs[0].Note);
                try { DitchQty = RoadDitchLines.Quantities(RoadDitchLines.Lines(tr, AlignId, PlanId, Design, Ranges)); } catch { DitchQty = ""; }
                limit = ReadLimit(tr);
                try { GradeNow = RoadBuilder.PlanGrade(tr, PlanId); } catch { GradeNow = double.NaN; }
                // ★★[v111.7.2 · JACK 1102 «20 %까지는 문제없이 — 그 이상이면 경고»] 20 %(고정 · «최대 경사 %»와 따로) 넘는 접선 — ■ 결과 빨간 줄 · 처음 넘을 때 명령창 한 줄
                try
                {
                    var prW = (CivilDb.Profile)tr.GetObject(PlanId, OpenMode.ForRead);
                    var pvW = new List<(double, double)>();
                    foreach (CivilDb.ProfilePVI q in prW.PVIs) pvW.Add((q.RawStation, q.Elevation));
                    var st = ProfileSheetRules.Steep(pvW, ProfileSheetRules.WarnGrade);
                    double ivW = Math.Max(1.0, GradingSettings.XsecInterval);
                    GradeWarn = st.Count == 0 ? ""
                              : $"⚠ 종단경사 {Math.Abs(st.WorstPct):0.0} % — {ProfileSheetRules.WarnGrade * 100:0} % 넘음 (측점 {StationMarks.Fmt(st.S0, ivW)} ~ {StationMarks.Fmt(st.S1, ivW)}"
                                + (st.Count > 1 ? $" 외 {st.Count - 1}곳" : "") + ")";
                }
                catch { GradeWarn = ""; }
                // ★[v110.4 · 계획 3.3] 종단도가 지금 종단(PVI · 노선 길이) · 축척과 다르면 알림 — 밴드 값은 Civil이 따라가지만 측점 · 표고 범위 · 글씨 벌리기는 그대로
                //   ★[v111.3.1 · JACK 1030 «PVI 수정되면 아래 멘트 넣고 단추가 깜박거리게»] 위 알림 줄이 아니라 [종단도 새로 그리기] 아래 한 줄(SheetStale) + 단추 깜박임
                try
                {
                    if (Design.SheetViews.Count > 0)
                    {
                        // ★[v111.4] 축척 = 지금 규칙의 축척(부지 종단도 축척 · 없으면 도면설정 진입도로 칸) — 도면 주석 축척만 보면 진입도로 칸을 바꿔도 못 알아챔
                        // ★★[v113.1] 판 2 설정(도면설정 «2. 진입도로» H · V) = 칸 열쇠 · 그린 뒤 주석 축척으로 견줌(자동 축척은 그 값을 미리 모름) · 옛 설정 = 지금 비교 그대로
                        var (pl, v2, key, _, _) = RoadSheets.Setting(db, "", Design.SheetScale);   // ★[v117.5.1] 옛 설정 — 그린 H 그대로(부지 축척을 안 따름)
                        double want = wantSheet = pl.PinH;
                        stampNow = RoadSheets.StampOf(tr, AlignId, PlanId);
                        if (stampNow != Design.SheetStamp) sheetNote = "종단이 수정되었습니다";
                        else if (v2 || Design.SheetScaleKey != null)
                        {
                            if (!v2 || key != Design.SheetScaleKey) sheetNote = "축척이 바뀌었습니다";
                            // ★[설치 전 검토 열린 질문 a] 지금 주석 축척을 못 읽으면(배치탭 1:1 등 DH 규약 아님 — 0) 견주지 않음
                            else if (Design.SheetAnno is double an0 && Commands.SheetCommand.CurrentDrawingScale(db) is double anNow && anNow > 0 && Math.Abs(an0 - anNow) > 1e-6) sheetNote = "축척이 바뀌었습니다";
                        }
                        else if (want > 0 && Math.Abs(want - Design.SheetScale) > 1e-6) sheetNote = "축척이 바뀌었습니다";
                        // ★[설치 전 검토 4] 옛 설정 — 그린 뒤 주석 축척(SheetAnno)이 지금과 다르면(부지 주석 축척 변경 뒤 KeepLook 굽기 실패) 판 2와 같은 알림
                        else if (Design.SheetAnno is double an1 && Commands.SheetCommand.CurrentDrawingScale(db) is double anNow1 && anNow1 > 0 && Math.Abs(an1 - anNow1) > 1e-6) sheetNote = "축척이 바뀌었습니다";
                    }
                }
                catch { }
                // ★★[v111.5 · 계획 3 · JACK 1030] 그린 횡단도가 지금과 다르면 [횡단도 그리기] 아래 한 줄 + 깜박 — 종단도 측점이 바뀜 · 지표면을 다시 만듦(뷰에서 도로 선이 빠짐) · 축척(도면설정 칸 · 도면 주석 축척)
                //   ★[v111.7] 판정은 XsecStaleIn 하나(저절로 다시 그리기가 종단도를 다시 그린 뒤에도 같은 판정으로 다시 잼)
                try { xsecNote = XsecStaleIn(tr, db, out xsecInputs); }
                catch { }
                tr.Commit();
            }
            S0 = s0;
            // ★[설치 전 검토 · 중간 — 성능] 지난번에 넓혀서 찾았으면 그 범위로 바로(넓혀 다시 세 판을 PVI마다 되풀이하지 않게)
            //   지난 사면이 모두 35 m 안에서 끝났으면 다시 40부터(넓은 범위를 계속 끌고 가지 않게)
            double reach = Last != null && Last.Error.Length == 0 && !Last.ReachHit && Last.ReachUsed > 40
                           && Last.Slopes.Count > 0 && Last.Slopes.Max(q => q.Width) > 35 ? Last.ReachUsed : 40;
            foreach (var pc in pcs) { pc.Inp.Cell = 1.0; pc.Inp.Reach = reach; pc.Inp.Limit = limit; }
            // ★[v111.2 · 전체 검토 L2] 그린 도로 면이 지금 설계(종단 · 횡단 · 사면 · 옹벽 기록 · 바탕면)와 다르면 알림 — PVI를 끌거나 창 값을 바꾼 뒤
            //   ★[v111.3.1 · JACK 1030 «종단이 수정되면 도로면 만들기 아래 종단이 수정되었다고 · 버튼이 깜박»] [진입도로 지표면 생성] 아래 한 줄(SurfaceStale) + 깜박임 · 까닭은 바뀐 몫으로
            string surfNote = "", sigNow = "";
            bool hasSurf = HasSurface();
            try { sigNow = SigOfAll(pcs, BaseSurfId); } catch { }   // ★[v111.7.1] 바뀜 열쇠의 지표면 몫(지금 설계 지문 — 실패 뒤 다시 고치면 새 열쇠)
            try
            {
                if (Design.SurfaceSig.Length > 0 && sigNow.Length > 0 && sigNow != Design.SurfaceSig && hasSurf)
                {
                    var now = SigPartsAll(pcs, BaseSurfId); var was = Design.SurfaceSigParts.Split('|');
                    // ★[v115 · 검토 M1] 엔진 판 토큰만 다르면(옛 판 면 · 입력은 그대로) 값이 바뀐 것이 아니다 — 판이 바뀌어 한 번 다시 만들어야 한다고
                    bool engineOnly = EngineOnly(pcs, BaseSurfId, Design.SurfaceSig);   // ★[v116] 앞 판(v115) 토큰 면도
                    surfNote = engineOnly ? EngineNote
                             : was.Length != now.Length ? "설계가 바뀌었습니다"
                             : was[0] != now[0] ? "노선이 수정되었습니다"
                             : was[1] != now[1] ? "종단이 수정되었습니다"
                             : was[2] != now[2] ? "횡단 · 사면 · 옹벽 값이 바뀌었습니다"
                             : "바탕면이 바뀌었습니다";
                }
                // ★[v111.7.1] 지문 없는 옛 판(v111.2.1 전) 지표면 — 지금 설계와 같은지 알 길이 없어 옛것으로 본다(열 때는 «열 때부터 옛것» · 한 번 고치면 저절로 새로 만들어 지문이 생김)
                else if (hasSurf && Design.SurfaceSig.Length == 0 && sigNow.Length > 0) surfNote = "옛 판에서 만든 지표면입니다";
            }
            catch { }
            // ★[v110.3] 사면선 · 소단선 · 옹벽선(미리보기 · [옹벽 변환] 찍기가 씀)
            // ★★[v111 · 계획_v111.md 2.4] 단면 광선 선(선 끝 = 기록 경계 정확히 · 곧은 곳은 곧게) — 못 하면 옛 칸 등치선(알림)
            // ★★★[v116 · 고리 엔진 계획 3판 §5 · D2] 벽 기록이 없는 토막 = 고리 엔진 미리보기(격자 1 m · 선 · 데이라잇 — 세모는 [업데이트] 때만) · 벽 든 토막은 옛 엔진 그대로(비트 같음).
            //   지난 [업데이트]가 그 토막을 옛 엔진으로 물러서 지었고 설계가 그대로면(지문 같음) 미리보기도 옛 엔진 — ■ 결과 숫자 · [사면→옹벽] 고를 선이 그려 둔 면과 같게
            var swL = System.Diagnostics.Stopwatch.StartNew();
            bool surfNow = hasSurf && sigNow.Length > 0 && sigNow == Design.SurfaceSig;
            string CalcPiece(int k)
            {
                var pc = pcs[k]; string ln = "", ringNote = "";
                pc.Ring = null; pc.OldR = null;
                if (RingBase(pc.Inp))   // ★[v116.7] 선 옹벽만 있는 토막도 고리(면 · 옹벽 판정과 같은 엔진)
                {
                    if (surfNow && PathAt(Design.SurfacePath, k) != RingPath) ringNote = "⚠고리 면을 못 지어 옛 엔진으로 지은 면";
                    else
                    {
                        var swR = System.Diagnostics.Stopwatch.StartNew();
                        try
                        {
                            var b = RingRun(pc.Inp, false, out double used);
                            if (b.Ok)
                            {
                                var rr = RoadRing.ToResult(b, pc.Inp); rr.ReachUsed = used; rr.Ms = swR.Elapsed.TotalMilliseconds;
                                pc.Ring = b; pc.R = rr; pc.Lines = DispLines(b, b.Lines, preview: true);   // ★[v116.9 · 4-1] 표시 선(미리보기 = 거짓 조각 빼기만 — 끌기 중 가볍게)
                                return b.Note;
                            }
                            ringNote = "⚠" + b.Note + " → 옛 엔진";
                        }
                        catch (System.Exception exR) { ringNote = "⚠고리 면 미리보기 예외 — " + exR.Message + " → 옛 엔진"; try { DiagLog.Append($"\n[진입도로] {Name} — 토막 {k + 1} {ringNote} — {exR}"); } catch { } }
                    }
                }
                var r = RoadGrade.Run(pc.Inp); pc.R = r;
                try { pc.Lines = r.Error.Length == 0 ? RoadGrade.ExactLines(pc.Inp, r, out ln) : new(); }
                catch (System.Exception exL) { pc.Lines = new(); if (k == 0) try { DiagLog.Append("\n[진입도로] 계단선 못 셈 — " + exL.Message); } catch { } }
                return ringNote.Length == 0 ? ln : ln.StartsWith("⚠") ? ringNote + " · " + ln : ringNote;
            }
            string lineNote = CalcPiece(0); var ringWarn = new List<string>();
            Last = pcs[0].R!; Lines = pcs[0].Lines;
            // ★★[도로 마무리 ① · 계획 5.6] 빈 구간 뒤 토막 — 따로 계산 · 선은 첫 토막 Core 측점으로 옮겨 합침(미리보기) · [옹벽 변환]은 토막마다
            if (pcs.Count > 1)
            {
                Lines = new List<RoadGrade.TierLine>(Lines);
                for (int k = 1; k < pcs.Count; k++)
                {
                    string lnK = CalcPiece(k);
                    if (lnK.StartsWith("⚠고리")) ringWarn.Add($"토막 {k + 1} " + lnK.Split(" · ")[0]);   // ★[v116] 뒤 토막 고리 면 물러섬도 알림(옛 엔진 선 알림은 지금처럼 첫 토막만)
                    var rk = pcs[k].R!;
                    if (rk.Error.Length > 0) { if (Last.Error.Length == 0) Last = rk; continue; }
                    Lines.AddRange(ShiftLines(pcs[k].Lines, pcs[k].S0 - pcs[0].S0));
                }
            }
            // ★[v114.3 · JACK 1105 진입도로 옹벽 추가 · 계획 2판 3절] 지난 [업데이트]가 지은 선 옹벽 — 입력 지문이 같을 때만 미리보기 · 찍기 선도 그 벽으로 자름(R_out 안 사면선 뺌 · 벽 선 더함).
            //   끌기 중에는 벽을 다시 짓지 않는다(지문이 바뀌면 옛 선 그대로 + SurfaceStale · [업데이트]에서 지음)
            if (Walls != null && Walls.Any && pcs.Count == 1 && WallsSig.Length > 0 && WallsSig == sigNow)
            {
                try { Lines = RoadGrade.CutLines(Lines, Walls); pcs[0].Lines = Lines; } catch (System.Exception exW) { try { DiagLog.Append($"\n[진입도로] {Name} — 선 옹벽 선 자르기 못 함 — {exW.Message}"); } catch { } }
            }
            // ★[v114.3 · 계획 0-8] 옛 판 1 폴리선 옹벽을 지웠으면 도면마다 한 번 한 줄(확인창 없음 · 저장은 다음 DH 명령의 SaveDesign)
            if (Design.DroppedOld > 0 && !_oldWallNoted)
            {
                _oldWallNoted = true;
                string on = $"옛 방식 폴리선 옹벽 {Design.DroppedOld}개를 지웠습니다(v114 새 방식으로 다시 그리세요)";
                try { _doc.Editor.WriteMessage($"\n[진입도로] {Name} — {on}"); } catch { }
                try { DiagLog.Append($"\n[진입도로] {Name} — {on}"); } catch { }
                gapNote = gapNote.Length > 0 ? gapNote + " · " + on : on;
            }
            Pieces = pcs;
            BoundNote = BoundNoteOf(Design, pcs);
            Note = string.Join(" · ", new[] { _baseNote, note, gapNote, lineNote.StartsWith("⚠") ? lineNote : "", string.Join(" · ", ringWarn), _engNote.StartsWith("⚠") ? _engNote : "", BoundNote }.Where(t => t.Length > 0));
            SurfaceStale = surfNote; SheetStale = sheetNote; XsecStale = xsecNote;
            // ★★[v111.7.1] 지표면이 없음(계산은 됨) — [업데이트]가 만든다(첫 지표면은 DHROAD 끝에서)
            SurfaceMissing = Last.Error.Length == 0 && !hasSurf;
            // ★[v111.7] 바뀜 열쇠 — 알림 + 그 판정에 든 값(그린 도장 · 지금 도장 · 축척 · 측점 · 지표면). 실행 취소로 같은 상태가 되면 같은 열쇠
            //   ★[v111.7.1] 지표면 몫(옛것 · 없음 + 지금 설계 지문)도 — 지표면만 옛것이어도 열쇠가 생김
            var ciK = System.Globalization.CultureInfo.InvariantCulture;
            string surfKey = SurfaceMissing ? "없음:" + sigNow : surfNote.Length > 0 ? "옛것:" + sigNow + ":" + Design.SurfaceSig : "";
            // ★[v111.8 · 계획 4장 · M2] 도로 면은 맞는데 최종지표면이 옛것 · 없음(실패 포함) — 읽기만(핸들 + 지문) · [업데이트] 깜박 · 열쇠에 지금 입력 지문
            string finalKey = "";
            try
            {
                FinalState = FinalSurfaceBuilder.StateOf(db, out string fs0);
                FinalStale = hasSurf && surfKey.Length == 0 && Last.Error.Length == 0 && (FinalState == FinalSurfaceBuilder.State.Stale || FinalState == FinalSurfaceBuilder.State.Missing);
                if (FinalStale) finalKey = "최종:" + FinalState + ":" + Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(System.Text.Encoding.UTF8.GetBytes(fs0)))[..16];
            }
            catch { FinalStale = false; }
            // ★[도로 마무리 ① · 계획 5.3] 끝 맞춤 필요(EndsOn 참)도 바뀜 열쇠에 — [업데이트] 깜박 · ⓪이 맞춤
            string endsKey = Design.EndsOn && EndsStale.Length > 0 ? "끝:" + EndsStale : "";
            StaleKey = surfKey.Length + sheetNote.Length + xsecNote.Length + finalKey.Length + endsKey.Length == 0 ? ""
                     : string.Join("|", surfKey, sheetNote, xsecNote, Design.SheetStamp, stampNow, Design.SheetScale.ToString("R", ciK), wantSheet.ToString("R", ciK), Design.XsecStamp, xsecInputs)
                       + (finalKey.Length > 0 ? "|" + finalKey : "") + (endsKey.Length > 0 ? "|" + endsKey : "");
            _baseNote = "";
            // ★[v111.7.1 · 검토 중간 2] 실행 취소 · 다시 실행 뒤 한 번 — 노리선이 되살아났으면 «노리선 지움» 알림을 끔
            if (_recheckTicks) { _recheckTicks = false; if (TicksErased > 0 && AnyTicks(db)) TicksErased = 0; }
            // ★[v111.7.2] 20 % 넘음 — 고친 뒤 처음 넘을 때만 명령창 한 줄(다시 계산할 때마다 안 찍음 · 열 때 이미 넘었으면 ■ 결과 빨간 줄만)
            bool steep = GradeWarn.Length > 0;
            if (steep && _steepWas == false) { try { _doc.Editor.WriteMessage($"\n[진입도로] {Name} {GradeWarn}"); } catch { } }
            _steepWas = steep;
            var swD = System.Diagnostics.Stopwatch.StartNew();
            DrawPreview();
            _retried = false;
            // ★[재검토 열린 질문] 임시 선 걷고 거는 시간도 함께(Core 계산 시간만으로는 화면 멈춤을 못 봄)
            try { DiagLog.Append($"\n[진입도로] {Name} — {Last.Text} · 엔진 {string.Join("/", pcs.Select(p => p.Ring != null ? "고리" : "옛"))} · 계단선 {Lines.Count}가닥 {swL.Elapsed.TotalMilliseconds:F0} ms({lineNote}) · 그리기 {swD.Elapsed.TotalMilliseconds:F0} ms · 선 {_marks.Count} · 최대 종단경사 {GradeNow * 100:F1} %" + (note.Length > 0 ? " · " + note : "")); } catch { }
        }
        catch (System.Exception ex)
        {
            // ★[설치 전 검토 · 중간] 실패하면 옛 미리보기를 걷는다 — 결과처럼 계속 보이지 않게
            Note = "계산 실패 — " + ex.Message;
            Last = null;
            // ★[v111.7.1 · 검토 낮음] 열쇠도 «계산 실패» 표지로 — 옛 열쇠가 남아 [업데이트] 끝(NoteUpdate)이 엉뚱한 열쇠에 까닭을 다는 일이 없게(상태 줄 = 계산 오류)
            StaleKey = "계산 실패:" + ex.Message;
            ClearMarks();
            try { _doc.Editor.UpdateScreen(); } catch { }
            try { DiagLog.Append($"\n[진입도로] {Name} — 계산 실패 — {ex}"); } catch { }
            // ★[재검토 낮음 6] 잠금 충돌 같은 일시 실패면 한 번만 다시(계속 실패하는 것을 0.35초마다 되풀이하지 않게)
            if (!_retried) { _retried = true; _dirty = true; }
        }
        Changed?.Invoke(this);
    }
    bool _retried;

    /// <summary>★[v111.2 · 전체 검토 L2] 도로 면 입력 지문 — 중심선 표본(자리 · 계획고) · 횡단 · 사면 · 옹벽 기록 · 해상도 · 바탕면(칸 · 범위는 뺌 — 미리보기 1 m · 면 0.5 m가 같게).</summary>
    /// <param name="tag">★[v116] 엔진 토큰을 이것으로(앞 판 면과 «엔진만 다름»을 가리려고 — <see cref="EngineOnly"/>) · null = 지금 판 + 토막 엔진.</param>
    static string SigOf(RoadGrade.Input inp, ObjectId baseSurf, bool engine = true, string? tag = null)
    {
        var sb = new System.Text.StringBuilder();
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        for (int i = 0; i < inp.X.Length; i++) sb.Append(inp.X[i].ToString("R", ci)).Append(',').Append(inp.Y[i].ToString("R", ci)).Append(',').Append(inp.Z[i].ToString("R", ci)).Append(';');
        var opt = new System.Text.Json.JsonSerializerOptions { IncludeFields = true };
        sb.Append(System.Text.Json.JsonSerializer.Serialize(new { inp.Sec, inp.Cut, inp.Fill, inp.TierWalls, inp.Walls, inp.MinSlope, inp.SectionGap, inp.EdgeStep }, opt));
        // ★[도로 마무리 ② · 계획 6.1 · 비평 M6] 측구는 있을 때만(측구 없는 도로 지문 = 옛 문자열 그대로)
        if (inp.Ditches.Count > 0) sb.Append("|측구").Append(System.Text.Json.JsonSerializer.Serialize(inp.Ditches, opt));
        if (inp.LineWalls.Count > 0) sb.Append("|선옹벽2").Append(System.Text.Json.JsonSerializer.Serialize(inp.LineWalls, opt));   // ★[v114.3] 있을 때만(판 2 · 여유 포함 — 벽 없는 도로 지문 그대로)
        // ★[v115 · 설치 전 검토 R8] 엔진이 바뀌면(굽은 안쪽 새 길 · B1 격자 · 선 옹벽) 옛 판 면은 한 번 «수정됨 — [업데이트]»
        //   ★[v116 · 고리 엔진 §4 M5a] 토막 엔진(고리 · 옛)도 — 벽을 넣거나 빼 엔진이 바뀌면 지문이 바뀐다
        // ★[v116.7] tag 끝 «|옛!» = 선 옹벽 도로의 앞 판 토큰(벽 있는 토막 «|옛» · 없는 토막 «|고리» — 그때 판 EngineOf)
        if (engine) sb.Append(tag == null ? EngineTag + EngineOf(inp)
                            : tag.EndsWith(OldWallSfx) ? tag[..^OldWallSfx.Length] + (RingOk(inp) ? "|고리" : "|옛")
                            : tag.StartsWith("|엔진 v116") ? tag + EngineOf(inp) : tag);   // v116 · v116.1 지문은 토막 엔진 토큰까지
        sb.Append('|').Append(baseSurf.IsNull ? "" : baseSurf.Handle.ToString());
        // ★[v111.3 · 설치 전 검토 M2] 바탕면 내용도 — 같은 핸들로 고쳐진(정지면 다시 · 원지반 편집) 바탕이면 [노리선]이 면과 안 맞는 노리선을 말없이 그렸다.
        //   중심선 표본 10개마다 양쪽 5 · 15 · 30 · 50 m 바탕 높이(0.1 mm) — 도로 자리 곁 바탕이 바뀌면 지문이 바뀐다(먼 곳 편집은 도로 면과 상관없음)
        if (inp.Base != null)
            for (int i = 0; i + 1 < inp.X.Length; i += 10)
            {
                double dx = inp.X[i + 1] - inp.X[i], dy = inp.Y[i + 1] - inp.Y[i], l = Math.Sqrt(dx * dx + dy * dy);
                if (l < 1e-9) continue;
                foreach (double o in new[] { -50.0, -30, -15, -5, 5, 15, 30, 50 })
                {
                    sb.Append(inp.Base.TryGetElevation(inp.X[i] - dy / l * o, inp.Y[i] + dx / l * o, out double z) ? Math.Round(z, 4).ToString("R", ci) : "-").Append(',');
                }
            }
        return Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(System.Text.Encoding.UTF8.GetBytes(sb.ToString())));
    }
    /// <summary>★[v115 · 설치 전 검토 R8] 도로 면 엔진 판 — 지문에 넣어 엔진이 바뀐 판의 옛 면을 한 번 «수정됨»으로(값을 바꿀 때마다 판을 올림).
    /// ★[v116 · 고리 엔진] v115 → v116(앞 판 토큰은 <see cref="OldEngineTags"/>).</summary>
    const string EngineTag = "|엔진 v116.4";   // ★[v116.4 · JACK 1109 반경 · 곡률 깨짐] 바늘이 남아도 고쳐 다시 잼(옛 엔진으로 안 물러섬) · 맞춤 뒤집기 · 꺾임 밖 점 뺌 · 선 높이 맞춤 — 세모가 바뀜
    // 앞: "|엔진 v116.3"   // ★[v116.3] 설치 전 검토 M1 · M3(조각 합친 뒤 빼기 · 골 따라가기 짝 확인) — 세모가 바뀔 수 있음
    // 앞: "|엔진 v116.2"(가는 사면 띠 · 골 줄 따라가기 · T자는 틈 변만) · "|엔진 v116.1"   // ★[v116.1 · JACK 1109 «여전히 오목부 뭉개져»] 골 찾기 0.6 m + 보조 고리 끊김선 — 세모가 바뀜
    /// <summary>★[v116] 앞 판 엔진 토큰(그 판 면의 지문을 다시 셀 때 — 토막 엔진 토큰 없음).</summary>
    static readonly string[] OldEngineTags = { "|엔진 v115", "|엔진 v116", "|엔진 v116.1", "|엔진 v116.2", "|엔진 v116.3" };
    /// <summary>★[v116 · 고리 엔진 §4 M5a] 토막 엔진 토큰 — 벽 기록 없음 = 고리 · 있음 = 옛(실제로 지은 길은 설계 기록 SurfacePath).</summary>
    /// ★[v116.7] 선 옹벽만 있는 토막도 고리(<see cref="RingBase"/>) — 그런 도로의 앞 판 지문(«|옛»)은 <see cref="EngineOnly"/>가 «엔진만 다름»으로 본다.
    static string EngineOf(RoadGrade.Input inp) => RingBase(inp) ? "|고리" : "|옛";
    /// <summary>★[v116.7] <see cref="SigOf"/> tag 끝 표지 — 선 옹벽 도로의 앞 판 토큰(그때 판 EngineOf: 벽 없음 «|고리» · 있음 «|옛»)으로 셈.</summary>
    const string OldWallSfx = "|옛!";
    /// <summary>★[v115 · 검토 M1] 엔진 판만 바뀐 옛 면 알림.</summary>
    const string EngineNote = "애드인 판(v116.7)이 바뀌어 지표면을 한 번 다시 만들어야 합니다 — [업데이트]";
    /// <summary>★[v115 · 검토 M1 → ★v116] 엔진 판만 다른가(입력은 그대로) — 판 토큰 없는 지문(v115 전) · 앞 판 토큰 지문(v115)과 견줌.</summary>
    internal static bool EngineOnly(List<Piece> pcs, ObjectId baseSurf, string sig)
    {
        if (sig.Length == 0) return false;
        try
        {
            if (SigOfAll(pcs, baseSurf, engine: false) == sig) return true;
            foreach (var t in OldEngineTags) if (SigOfAll(pcs, baseSurf, tag: t) == sig) return true;
            // ★[v116.7] 선 옹벽만 있는 도로 — 앞 판(v116 ~ v116.6)은 옛 엔진 토큰(«|옛»)
            if (pcs.Any(p => p.Inp.LineWalls.Count > 0 && RingBase(p.Inp)))
                foreach (var t in OldEngineTags.Where(q => q.StartsWith("|엔진 v116")).Append(EngineTag))
                    if (SigOfAll(pcs, baseSurf, tag: t + OldWallSfx) == sig) return true;
        }
        catch { }
        return false;
    }
    /// <summary>★[v111.3.1] 지문을 몫으로 — 노선(X · Y) · 종단(Z) · 값(횡단 · 사면 · 옹벽 기록) · 바탕면(핸들 · 곁 높이). [진입도로 지표면 생성] 아래 «무엇이 바뀌었나» 한 줄용(SigOf 값은 그대로 — 이미 만든 면의 지문이 안 바뀌게).</summary>
    static string[] SigParts(RoadGrade.Input inp, ObjectId baseSurf, bool engine = true)
    {
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        string H(string t) => Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(System.Text.Encoding.UTF8.GetBytes(t))).Substring(0, 16);
        var xy = new System.Text.StringBuilder(); var z = new System.Text.StringBuilder();
        for (int i = 0; i < inp.X.Length; i++) { xy.Append(inp.X[i].ToString("R", ci)).Append(',').Append(inp.Y[i].ToString("R", ci)).Append(';'); z.Append(inp.Z[i].ToString("R", ci)).Append(';'); }
        var opt = new System.Text.Json.JsonSerializerOptions { IncludeFields = true };
        string vals = System.Text.Json.JsonSerializer.Serialize(new { inp.Sec, inp.Cut, inp.Fill, inp.TierWalls, inp.Walls, inp.MinSlope, inp.SectionGap, inp.EdgeStep }, opt);
        if (inp.Ditches.Count > 0) vals += "|측구" + System.Text.Json.JsonSerializer.Serialize(inp.Ditches, opt);   // ★[도로 마무리 ②] 있을 때만
        if (inp.LineWalls.Count > 0) vals += "|선옹벽2" + System.Text.Json.JsonSerializer.Serialize(inp.LineWalls, opt);   // ★[v114.3] 있을 때만
        if (engine) vals += EngineTag + EngineOf(inp);   // ★[v115 · R8] «무엇이 바뀌었나»에서는 값 몫으로 보임 · ★[v116] 토막 엔진도
        var bs = new System.Text.StringBuilder(baseSurf.IsNull ? "" : baseSurf.Handle.ToString());
        if (inp.Base != null)
            for (int i = 0; i + 1 < inp.X.Length; i += 10)
            {
                double dx = inp.X[i + 1] - inp.X[i], dy = inp.Y[i + 1] - inp.Y[i], l = Math.Sqrt(dx * dx + dy * dy);
                if (l < 1e-9) continue;
                foreach (double o in new[] { -50.0, -30, -15, -5, 5, 15, 30, 50 })
                    bs.Append(inp.Base.TryGetElevation(inp.X[i] - dy / l * o, inp.Y[i] + dx / l * o, out double zb) ? Math.Round(zb, 4).ToString("R", ci) : "-").Append(',');
            }
        return new[] { H(xy.ToString()), H(z.ToString()), H(vals), H(bs.ToString()) };
    }
    /// <summary>★[도로 마무리 ① · 계획 5.6] 토막이 여럿이면 토막 지문을 이어 한 번 더(하나면 <see cref="SigOf"/> 그대로 — 비트 같음).</summary>
    /// <param name="engine">★[v115 · 검토 M1] 거짓 = 엔진 판 토큰 없이(옛 판 면과 «엔진만 다름»을 가리려고).</param>
    internal static string SigOfAll(List<Piece> pcs, ObjectId baseSurf, bool engine = true, string? tag = null)
        => pcs.Count == 1 ? SigOf(pcs[0].Inp, baseSurf, engine, tag)
           : Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(System.Text.Encoding.UTF8.GetBytes("토막" + string.Join("+", pcs.Select(p => SigOf(p.Inp, baseSurf, engine, tag))))));
    static string[] SigPartsAll(List<Piece> pcs, ObjectId baseSurf, bool engine = true)
    {
        if (pcs.Count == 1) return SigParts(pcs[0].Inp, baseSurf, engine);
        var all = pcs.Select(p => SigParts(p.Inp, baseSurf, engine)).ToList();
        return Enumerable.Range(0, all[0].Length).Select(i => string.Join("+", all.Select(a => a[i]))).ToArray();
    }

    /// <summary>★★[도로 마무리 ① · 계획 5.8] 도로 구간마다 엔진 입력(표본 · 횡단 · 사면 · 옹벽 · 측구 · 해상도) — 칸 · 시작 범위 · 사업 경계는 부르는 쪽이.
    /// 구간이 하나(빈 구간 없음)면 지금과 같은 입력(비트 같음).</summary>
    internal static List<Piece> MakePieces(Transaction tr, ObjectId aid, ObjectId planId, RoadDesign d, IGroundSurface ground, List<(double A, double B)> ranges)
    {
        var o = new List<Piece>();
        var rs = ranges.Count > 0 ? ranges : new List<(double A, double B)> { (double.NaN, double.NaN) };
        foreach (var (a, b) in rs)
        {
            var (X, Y, Z, s0, s1, note) = RoadBuilder.SampleRange(tr, aid, planId, a, b);
            if (X.Length < 2 && rs.Count > 1) continue;
            o.Add(new Piece
            {
                S0 = s0, S1 = s1, Note = note,
                Inp = new RoadGrade.Input
                {
                    X = X, Y = Y, Z = Z, Sec = d.Section(), Cut = d.Cut(), Fill = d.Fill(),
                    TierWalls = d.CoreTierWalls(s0),   // ★[v110.3] 계단선 변환 옹벽(옛 가장자리 옹벽은 0단 · 소단 0)
                    Ditches = d.CoreDitches(s0),       // ★[도로 마무리 ②] 측구(없으면 빈 목록 — 옛 동작)
                    LineWalls = d.CoreLineWalls(out _, out _),   // ★[v114.3 · JACK 1105 진입도로 옹벽 추가] 선 옹벽(세계 XY · 엔진은 안 씀 — 면을 지은 뒤 얹음 · 지문에만)
                    Base = ground, SuggestH = 0,   // ★[v116.4 · JACK 1109] 옹벽 제안 없앰 — 옛 기록 SuggestH는 안 씀
                    SectionGap = Math.Max(0.5, GradingSettings.VertexSpacing),   // ★[v111.2] 삼각망 해상도 = 부지 둘레 점 간격
                },
            });
        }
        if (o.Count == 0) throw new InvalidOperationException("도로 구간에서 표본을 못 읽었습니다");
        return o;
    }

    /// <summary>선 측점을 옮긴 사본(빈 구간 뒤 토막 → 첫 토막 Core 측점).</summary>
    internal static List<RoadGrade.TierLine> ShiftLines(List<RoadGrade.TierLine> ls, double ds)
        => ls.Select(l => new RoadGrade.TierLine { Cut = l.Cut, Side = l.Side, K = l.K, Role = l.Role, Prof = l.Prof, Cross = l.Cross, WallNo = l.WallNo, DisplayOnly = l.DisplayOnly, Pts = l.Pts.Select(p => (p.X, p.Y, p.Z, p.S + ds)).ToList() }).ToList();

    /// <summary>★[v113.3] 계획 종단이 노선 시점 · 종점을 <see cref="RoadEndFix.CoverTol"/> 넘게 못 덮으면 «종단 끝 N m»(아니면 빈 글).</summary>
    string ProfileShort(Transaction tr, CivilDb.Alignment al)
    {
        try
        {
            var pr = (CivilDb.Profile)tr.GetObject(PlanId, OpenMode.ForRead);
            double a = pr.StartingStation - al.StartingStation, b = al.EndingStation - pr.EndingStation;
            var w = new List<string>();
            if (a > RoadEndFix.CoverTol) w.Add($"시점 {a:F1} m");
            if (b > RoadEndFix.CoverTol) w.Add($"종점 {b:F1} m");
            return w.Count > 0 ? "종단이 노선 끝까지 못 감(" + string.Join(" · ", w) + ")" : "";
        }
        catch { return ""; }
    }

    /// <summary>★★[도로 마무리 ① · 계획 5.3] 끝 판정 · 도로 구간 · 제안 글 · 끝 맞춤 필요(읽기만). 바닥 캐시는 바탕 바뀜 때만 다시.</summary>
    void ComputeEnds(Transaction tr, Database db)
    {
        Ends = null; _allEnds = null; EndsProposal = ""; EndsStale = "";
        var ap = RoadRanges.AlignProfile(tr, AlignId, Design);
        if (ap == null) { Ranges = new(); return; }
        var al = (CivilDb.Alignment)tr.GetObject(AlignId, OpenMode.ForRead);
        try
        {
            if (_floors == null) _floors = PadFloors.Load(db, tr, out _floorNote);
            _allEnds = RoadRanges.SolveAll(tr, db, AlignId, _floors, (x, y) => _base != null && _base.TryGetElevation(x, y, out double z) ? z : null, true,
                                           new RoadEnds.Options { CurveR = Design.CurveR > 0 ? Design.CurveR : 30 });
            Ends = _allEnds?.FirstOrDefault(r => r.Handle == AlignId.Handle.Value);
        }
        catch (Exception ex) { Ends = null; _allEnds = null; try { DiagLog.Append($"\n[진입도로 끝] {Name} — 판정 실패(끝 그대로) — {ex.Message}"); } catch { } }
        Ranges = Design.EndsOn ? RoadRanges.FromResult(Ends, ap.Value, al.StartingStation, al.EndingStation) : new() { ap.Value };
        string prop = RoadRanges.Proposal(Ends, al.StartingStation, al.EndingStation, Design.OwnCurves);
        if (!Design.EndsOn) { EndsProposal = prop; return; }
        // 끝 맞춤 필요 — 실제 노선을 바꿀 것(우리 노선) · 끝 PVI가 고정 높이와 1 mm 넘게 다름 · 빈 구간 PVI 없음
        var why = new List<string>();
        if (Ends != null)
        {
            bool own = Design.OwnCurves;
            if (own && (Ends.Start.Kind == RoadEnds.Kind.Extend || Ends.Finish.Kind == RoadEnds.Kind.Extend)) why.Add("늘림");
            if (own && ((Ends.Start.Kind is RoadEnds.Kind.Floor or RoadEnds.Kind.Branch && Ends.Start.Station > al.StartingStation + 0.01)
                     || (Ends.Finish.Kind is RoadEnds.Kind.Floor or RoadEnds.Kind.Branch && Ends.Finish.Station < al.EndingStation - 0.01))) why.Add("자름");
            try
            {
                var pr = (CivilDb.Profile)tr.GetObject(PlanId, OpenMode.ForRead);
                var pv = new List<(double S, double Z)>(); foreach (CivilDb.ProfilePVI q in pr.PVIs) pv.Add((q.RawStation, q.Elevation));
                bool Pin(double s, double z) => !double.IsFinite(z) || pv.Any(p => Math.Abs(p.S - s) < 0.01 && Math.Abs(p.Z - z) <= 1e-3);
                double sa = Ranges[0].A, sb = Ranges[^1].B;
                if (Ends.Start.Kind != RoadEnds.Kind.Free && Ends.Start.Kind != RoadEnds.Kind.Extend && !Pin(sa, Ends.Start.Z)) why.Add("시점 높이");
                if (Ends.Finish.Kind != RoadEnds.Kind.Free && Ends.Finish.Kind != RoadEnds.Kind.Extend && !Pin(sb, Ends.Finish.Z)) why.Add("종점 높이");
                foreach (var g in Ends.Gaps) if (!Pin(g.A, g.Za) || !Pin(g.B, g.Zb) || pv.Any(p => p.S > g.A + 0.01 && p.S < g.B - 0.01)) { why.Add("빈 구간 PVI"); break; }
            }
            catch { }
        }
        // ★[v113.3 · JACK 1105 «폴리선 선택 → 부지와 끊어짐»] 계획 종단이 노선 끝까지 못 감(노선이 만든 뒤 길어짐 · 바탕면 높이 없는 끝) — 끝 맞추기가 종단 끝을 노선 끝으로 잇는다
        if (ProfileShort(tr, al) is { Length: > 0 } sh) why.Add(sh);
        EndsStale = why.Count > 0 ? prop + (prop.Length > 0 ? " · " : "") + string.Join("+", why) : "";
    }

    /// <summary>★★★[도로 마무리 ① · 계획 5.5 · 계획_v111_8.md 7.5] 이 도로 엔진의 바탕 — 끝 맞춤 도로가 가지(끝이 다른 도로 포장 안)면 «바탕면 + 본 도로 면» 합침(<see cref="BranchBase"/>) ·
    /// 아니면 바탕면 그대로. <see cref="ComputeEnds"/> 뒤에(같은 트랜잭션). 합친 면은 열쇠(바탕 지문 · 본 도로 면 핸들)가 같으면 다시 안 만듦 — 실패도 열쇠가 바뀔 때까지 다시 안 함(시계마다 되풀이 안 함 · 알림 남김).</summary>
    IGroundSurface EngineGround(Transaction tr, Database db)
    {
        _engNote = "";
        if (!Design.EndsOn || _allEnds == null || _base == null) return _base!;
        var mains = BranchBase.Mains(tr, db, _allEnds, AlignId, out string mn);
        if (mains.Count == 0) { _engNote = mn; return _base; }
        string key = BranchBase.Key(_base, mains);
        if (key != _engKey)
        {
            _eng = null;
            try { _eng = BranchBase.Compose(tr, _base, mains, out _engNoteKept); }
            catch (Exception ex) { _engNoteKept = "⚠가지 바탕 못 합침 — " + ex.Message + "(바탕면만으로 계산)"; }
            _engKey = key;
            if (_engNoteKept.StartsWith("⚠")) { try { DiagLog.Append($"\n[진입도로 가지 바탕] {Name} — {_engNoteKept}"); } catch { } }
        }
        _engNote = string.Join(" · ", new[] { mn, _engNoteKept }.Where(t => t.Length > 0));
        return _eng ?? (IGroundSurface)_base;
    }

    /// <summary>★★★[도로 마무리 ① · 계획 5.3 ⓪] 끝 맞추기 — 노선 자르기 · 늘리기(우리 노선) + 끝 · 빈 구간 PVI 고정 + 시점이 움직이면 측점 기록을 옮기고 1 mm 확인.
    /// 실패하면 그 트랜잭션을 물리고 PVI 고정만(가상 자르기) 한 번 더 · 그것도 안 되면 그대로. <paramref name="turnOn"/> = [끝 맞추기](EndsOn을 켬 — 실패해도 켬: 가상 자르기로 계속).
    /// 돌려줌 = 한 줄(«⚠»로 시작 = 실패). 부르는 명령 안(실행 취소 한 줄 · 문서 잠금).</summary>
    internal string FixEndsNow(bool turnOn)
    {
        bool was = Suppress; Suppress = true;
        string note;
        try
        {
            using var dl = _doc.LockDocument();
            EnsureBase();
            var db = _doc.Database;
            RoadEnds.Result? res; RoadDesign? d0;
            using (var tr0 = db.TransactionManager.StartTransaction())
            {
                d0 = RoadDesign.Read(tr0, AlignId);
                if (_floors == null) _floors = PadFloors.Load(db, tr0, out _floorNote);
                res = d0 == null ? null : RoadRanges.Solve(tr0, db, AlignId, _floors, (x, y) => _base != null && _base.TryGetElevation(x, y, out double z) ? z : null, true,
                                                           new RoadEnds.Options { CurveR = d0.CurveR > 0 ? d0.CurveR : 30 });
                // ★[v113.3] 바닥 · 다른 도로가 없어도 종단이 노선 끝까지 못 가면 두 끝 «자유»로 — 끝 맞추기가 종단 끝만 노선 끝으로 잇는다
                if (res == null && d0 != null && ProfileShort(tr0, (CivilDb.Alignment)tr0.GetObject(AlignId, OpenMode.ForRead)).Length > 0)
                {
                    var al0 = (CivilDb.Alignment)tr0.GetObject(AlignId, OpenMode.ForRead);
                    RoadEnds.End Free(int w, double s) => new(w, RoadEnds.Kind.Free, s, 0, 0, double.NaN, 0, -1, null, "");
                    res = new RoadEnds.Result { Handle = AlignId.Handle.Value, Start = Free(0, al0.StartingStation), Finish = Free(1, al0.EndingStation) };
                    res.Ranges.Add((al0.StartingStation, al0.EndingStation));
                }
                tr0.Commit();
            }
            if (d0 == null) return "⚠설계 기록이 없습니다";
            if (!turnOn && !d0.EndsOn) return "";
            note = "";
            string fail = "";
            if (res != null)
                foreach (bool edits in new[] { true, false })
                {
                    using var tr = db.TransactionManager.StartTransaction();
                    try
                    {
                        var d = RoadDesign.Read(tr, AlignId)!;
                        var rep = RoadEndFix.Fix(tr, AlignId, PlanId, d, res, edits, (x, y) => _base != null && _base.TryGetElevation(x, y, out double z) && double.IsFinite(z) ? z : null);
                        d.EndsOn = true;
                        d.Write(tr, AlignId);
                        tr.Commit();
                        Design = d;
                        note = (edits ? "" : "노선은 그대로(가상 자르기) — ") + (rep.Text.Length > 0 ? rep.Text : "바꿀 것 없음");
                        if (fail.Length > 0) note += $" · ⚠노선 고치기 물림({fail})";
                        try { DiagLog.Append($"\n[진입도로 끝] {Name} — {note}" + (res.Notes.Count > 0 ? " · " + string.Join(" · ", res.Notes) : "")); } catch { }
                        fail = "";
                        break;
                    }
                    catch (Exception ex)
                    {
                        tr.Abort();
                        fail = ex.Message;
                        try { DiagLog.Append($"\n[진입도로 끝] {Name} — {(edits ? "노선 고치기" : "PVI 고정")} 물림 — {ex}"); } catch { }
                    }
                }
            if (res == null || fail.Length > 0)
            {
                if (turnOn)
                    using (var tr = db.TransactionManager.StartTransaction())
                    {
                        var d = RoadDesign.Read(tr, AlignId)!; d.EndsOn = true; d.Write(tr, AlignId); tr.Commit(); Design = d;
                    }
                note = res == null ? "부지 바닥 · 다른 도로가 없어 끝은 그대로" : "⚠끝 맞추기 실패 — " + fail;
            }
        }
        catch (Exception ex) { note = "⚠끝 맞추기 실패 — " + ex.Message; }
        finally { Suppress = was; }
        _dirty = true;
        return note;
    }

    string _pendingSig = "", _pendingParts = "";
    double _pendingReach = 40;
    /// <summary>★[v111.3.1] 만든 지표면이 지금 설계와 다른 까닭(빈 글 = 같음 · 면 없음) · 그린 종단도가 지금 종단 · 축척과 다른 까닭 — 도킹창 단추 아래 한 줄 + 깜박임.</summary>
    internal string SurfaceStale = "", SheetStale = "";
    /// <summary>★[v111.5 · 계획 3] 그린 횡단도가 지금과 다른 까닭(빈 글 = 같음 · 횡단도 없음) — [횡단도 그리기] 아래 한 줄 + 깜박임.</summary>
    internal string XsecStale = "";
    /// <summary>★[v111.7.1] 계산은 됐는데 진입도로 지표면이 없다 — 저절로 다시 만들기가 만든다 · 그동안 미리보기 선을 보인다.</summary>
    internal bool SurfaceMissing;
    /// <summary>★[v111.10 · 계획_초기화.md 3.4 · JACK 1103 «처음 만들면 벌써 고친 것이 있다고 나옴»] DHROAD · DHROADDRAW가 이 도로를 처음 만드는 중(종단도 자리 찍기 · 첫 지표면) —
    /// 상태 줄 «처음 만드는 중» · [업데이트] 꺼짐(누르면 ^C^C가 그 명령을 끊음). 명령이 끝나면 <see cref="EndMaking"/>.</summary>
    internal bool Making;

    /// <summary>★[v111.10] 처음 만들기 끝 — 표시를 내리고 창을 다시 그림(명령 안 실패 · Esc여도 부름).</summary>
    internal void EndMaking()
    {
        if (!Making) return;
        Making = false;
        Changed?.Invoke(this);
    }

    /// <summary>★[v111.7] 그린 횡단도가 지금과 다른 까닭(<see cref="XsecStale"/>와 같은 판정 — 다시 계산 안에서 · 저절로 다시 그리기가 종단도를 다시 그린 뒤).
    /// <paramref name="inputs"/> = 판정에 든 지금 값(측점 · 지표면 · 축척 — 바뀜 열쇠용).</summary>
    string XsecStaleIn(Transaction tr, Database db, out string inputs)
    {
        inputs = "";
        if (Design.XsecViews.Count == 0) return "";
        string surfNow = HasSurface() ? Design.SurfaceHandle : "", stations = RoadXsec.StationsKeyIn(tr, db, Design, AlignId);
        // ★[v112.1 검토 M2] 가지 도로 = 본 도로 면 핸들도(RoadXsec 열쇠와 같은 차례) · 끝 판정을 아직 못 했으면 그린 때 것 그대로로 봄
        if (surfNow.Length > 0 && Design.EndsOn)
        {
            if (_allEnds != null)
            {
                try
                {
                    var own = db.TryGetObjectId(new Handle(Convert.ToInt64(Design.SurfaceHandle, 16)), out var oid) ? oid : ObjectId.Null;
                    surfNow += RoadXsec.MainsKey(RoadXsec.MainOrder(BranchBase.Mains(tr, db, _allEnds, AlignId, out _)).Select(m => m.Surf).Where(x => x != own).Distinct());
                }
                catch { }
            }
            else { var w = (Design.XsecStamp ?? "").Split('|'); if (w.Length >= 3 && w[1].StartsWith(surfNow + "+", StringComparison.Ordinal)) surfNow = w[1]; }
        }
        double pinNow = RoadDrawingSettings.Read(db).Xsec, annoNow = Commands.SheetCommand.CurrentDrawingScale(db);
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        // ★[v111.7.2] 횡단도 배치도 판정에(바뀌면 다시 그림) · ★[검토 중간 2] 이 도면에 저장된 배치(세션 값은 다른 도면 것일 수 있음 — 시계가 뒤 도면을 다시 셀 때 앞 도면 배치로 «바뀜»이 뜨지 않게) · 기록 없으면 기본 1×2
        int layNow = RoadDrawingSettings.ReadLayoutRoad(db); if (layNow < 0) layNow = RoadXsecRules.LayoutDefault;   // ★[v113.2] 진입도로 배치(부지 배치를 바꿔도 «바뀜» 아님)
        string paveNow = Design.PaveKey;   // ★[v116.8 · JACK 1110 포장 종류] 포장 토큰도 판정에(바뀌면 횡단도만 다시 · 지표면 그대로)
        inputs = string.Join("|", stations, surfNow, Design.SurfaceSig, pinNow.ToString("R", ci), annoNow.ToString("R", ci), layNow.ToString(ci), paveNow);
        return RoadXsecRules.Stale(Design.XsecStamp, stations, surfNow, Design.SurfaceSig, paveNow, Design.XsecPin, pinNow, Design.XsecAnno, annoNow, Design.XsecLayout, layNow);
    }

    /// <summary>★[v111.7] 지금 횡단도 바뀜(설계는 노선에서 다시 읽음) — 저절로 다시 그리기가 종단도를 다시 그린 뒤 측점이 바뀌었나 볼 때.</summary>
    internal string XsecStaleNow()
    {
        try
        {
            using var dl = _doc.LockDocument();
            var db = _doc.Database;
            using var tr = db.TransactionManager.StartTransaction();
            var d = RoadDesign.Read(tr, AlignId);
            if (d != null) Design = d;
            string r = XsecStaleIn(tr, db, out _);
            tr.Commit();
            return r;
        }
        catch { return ""; }
    }
    /// <summary>★★[v111.7.1 · JACK 1102 «미리보기 선 숨기기 단추 없앰»] 미리보기 임시 선은 지표면이 옛것이거나 없을 때만(고친 뒤 · 다시 만들기 전 몇 초) —
    /// 지표면이 지금 설계와 같으면 도면의 진짜 선(사면선 · 소단선 · 옹벽선 · 지표면 경계)만 보인다. 옹벽 제안(자홍) · 사면 충돌(빨강)은 진짜 선이 없어 늘 보인다.</summary>
    bool PreviewFull => SurfaceStale.Length > 0 || SurfaceMissing;

    /// <summary>★[설치 전 검토 · 중간] 바탕면이 바뀌었다(지움 · 이름 바뀜 = 재정지) — 이름으로 다시 찾고(옛 면을 말없이 따라가지 않게) 높이 표를 다시 만든다. 문서는 부르는 쪽이 잠금.</summary>
    void EnsureBase()
    {
        if (!(_baseDirty || _base == null || BaseSurfId.IsErased)) return;
        _floors = null;   // ★[도로 마무리 ① · 계획 5.1] 바닥 캐시 = 바탕 바뀜 때만 다시
        _eng = null; _engKey = "";   // ★[계획 5.5] 가지 바탕도
        if (!ResolveBase()) throw new InvalidOperationException("바탕면(정지면_DH 또는 원지반)이 없습니다");
        if (_base == null || _baseDirty)
        {
            var db = _doc.Database;
            using var trB = db.TransactionManager.StartTransaction();
            _base = new CachedGroundSurface((CivilDb.TinSurface)trB.GetObject(BaseSurfId, OpenMode.ForRead));
            trB.Commit();
        }
        _baseDirty = false;
    }

    /// <summary>★[재검토 낮음 2] 바꾸기 전에 부른다 — 아직 다시 계산 전(실행 취소 직후 0.35초 안)이면 노선의 설계를 다시 읽어, 옛 메모리 값을 저장해 실행 취소를 말없이 되돌리지 않게.</summary>
    internal RoadDesign Fresh()
    {
        if (!_dirty) return Design;
        try
        {
            using var dl = _doc.LockDocument();
            using var tr = _doc.Database.TransactionManager.StartTransaction();
            var d = RoadDesign.Read(tr, AlignId);
            tr.Commit();
            if (d != null) Design = d;
        }
        catch { }
        return Design;
    }

    /// <summary>[종단 자동으로 다시](옛 [종단 초안 다시] — ★[v111.7] 이름만 바꿈) — 지금 바탕면에서 노선을 따라 읽어 계획 종단 PVI를 다시(우리가 쓰는 동안 바뀜 표시 막음).</summary>
    internal RoadProfileDraft.Result RedraftNow()
    {
        bool was = Suppress; Suppress = true;   // ★[v110.4 · 검토 L1] 겹쳐 불려도 바깥 막음을 안 풂
        try
        {
            using var dl = _doc.LockDocument();
            EnsureBase();
            var d = Fresh();
            // ★[도로 마무리 ① · 계획 5.5] 가지 도로는 본 도로 면을 합친 바탕을 따라 읽음(끝 = 본 도로 포장 높이)
            IGroundSurface g;
            using (var tr = _doc.Database.TransactionManager.StartTransaction()) { ComputeEnds(tr, _doc.Database); g = EngineGround(tr, _doc.Database); tr.Commit(); }
            return RoadBuilder.Redraft(_doc, AlignId, g, PlanId, d);
        }
        finally { Suppress = was; }
    }

    /// <summary>★[v110.2 · JACK 1029 «곡선 반경 = 도킹창 값 하나»] 이 노선의 직선 ↔ 직선 곡선 반경을 바꾸고 다시 계산(우리가 쓰는 동안 바뀜 표시 막음). 돌려줌 = 결과 한 줄.</summary>
    internal string ReCurve(double r)
    {
        string note;
        if (!Design.OwnCurves) { Recompute(); return "기존 Civil 노선으로 연 도로라 곡선은 그대로(곡선 반경 칸은 새 도로에만)"; }
        bool was = Suppress; Suppress = true;   // ★[v110.4 · 검토 L1]
        try
        {
            using var dl = _doc.LockDocument();
            note = RoadBuilder.ReCurve(_doc, AlignId, r);
        }
        finally { Suppress = was; }
        Recompute();
        return note;
    }

    /// <summary>★★[v110.4 · 계획_v110_4.md 3.3] [종단도 다시] · 노선 만들 때 — 부지 [종단도]와 같은 꼴(A1 도곽 · 밴드 6칸)로 이 도로 종단도를 <paramref name="at"/>에 새로.
    /// 바뀜 표시를 막고(겹쳐 불려도 바깥 막음 유지 — 검토 L1) 설계는 노선에서 다시 읽는다 · 바탕면은 이름으로 다시 찾는다 · 다시 계산(Recompute)은 안 부른다.</summary>
    internal string MakeSheets(Point3d at, System.Text.StringBuilder log)
    {
        bool was = Suppress; Suppress = true;
        var before = new List<string>(Design.SheetViews);
        try
        {
            using var dl = _doc.LockDocument();
            using (var tr = _doc.Database.TransactionManager.StartTransaction())
            {
                var d = RoadDesign.Read(tr, AlignId);
                tr.Commit();
                if (d != null) { Design = d; before = new List<string>(d.SheetViews); }
            }
            EnsureBase();
            return RoadSheets.Build(this, at, log);   // ★[v111.7] 그려졌으면 Build가 «못 그림» 기록을 끔
        }
        catch { MarkLost(true, before); throw; }
        finally { Suppress = was; }
    }

    /// <summary>★[v111.7 · 설치 전 검토 M3 · 재검토 낮음] 다시 그리다 실패해 옛 뷰가 모두 지워졌으면 — 설계 기록에 «못 그림»을 적는다(도킹창 단추 아래 늘 남는 알림 + 깜박 ·
    /// 미리보기를 껐다 켜도 · 도면을 다시 열어도 남음 · 그 명령을 U로 되돌리면 기록도 되돌아가 꺼짐). 옛 뷰가 하나라도 살아 있으면(지우기 전에 실패) 안 적음.</summary>
    void MarkLost(bool sheet, List<string> before)
    {
        try
        {
            if (before.Count == 0) return;
            var db = _doc.Database;
            using var tr = db.TransactionManager.StartTransaction();
            bool live = AnyLive(tr, db, before);
            tr.Commit();
            if (live) return;
            if (sheet) Design.SheetLost = true; else Design.XsecLost = true;
            SaveDesign();
        }
        catch { }
    }

    /// <summary>★[v111.7] 핸들 가운데 살아 있는(지워지지 않은) 종단뷰 · 횡단뷰가 있나.</summary>
    static bool AnyLive(Transaction tr, Database db, List<string> handles)
    {
        foreach (var h in handles)
            try
            {
                if (db.TryGetObjectId(new Handle(Convert.ToInt64(h, 16)), out var id) && !id.IsErased
                    && tr.GetObject(id, OpenMode.ForRead) is CivilDb.ProfileView or CivilDb.SectionView) return true;
            }
            catch { }
        return false;
    }

    /// <summary>★[v111.7 · 설치 전 검토 M3] 다시 그리다 실패해 옛 종단도 · 횡단도가 지워진 채(빈 글 = 아님 · 설계 기록 <see cref="RoadDesign.SheetLost"/>) — [종단도] · [횡단도] 아래 한 줄 + 깜박.</summary>
    internal string SheetLost => Design.SheetLost ? LostSheet : "";
    internal string XsecLost => Design.XsecLost ? LostXsec : "";
    internal const string LostSheet = "종단도를 다시 그리지 못했습니다 — U로 되돌리면 옛 종단도가 돌아옵니다";
    internal const string LostXsec = "횡단도를 다시 그리지 못했습니다 — U로 되돌리면 옛 횡단도가 돌아옵니다";

    /// <summary>★★[v111.5 · 계획_v111_4.md 3] [횡단도 그리기] — 부지 [횡단도]와 같은 꼴로 이 도로 횡단도를 <paramref name="at"/>에 새로(옛것은 이 도로 표식 · 기록으로만 지움).
    /// 바뀜 표시를 막고(겹쳐 불려도 바깥 막음 유지) 설계는 노선에서 다시 읽는다 · 바탕면은 이름으로 다시 찾는다.</summary>
    internal string MakeXsec(Point3d at, System.Text.StringBuilder log)
    {
        bool was = Suppress; Suppress = true;
        var before = new List<string>(Design.XsecViews);
        try
        {
            using var dl = _doc.LockDocument();
            using (var tr = _doc.Database.TransactionManager.StartTransaction())
            {
                var d = RoadDesign.Read(tr, AlignId);
                tr.Commit();
                if (d != null) { Design = d; before = new List<string>(d.XsecViews); }
            }
            EnsureBase();
            string msg = RoadXsec.Build(this, at, log);
            XsecStale = "";
            // ★[v111.7 · 설치 전 검토 M3] 못 그렸으면(옛것은 이미 지움) 늘 남는 알림(그렸으면 Build가 끔)
            if (msg.StartsWith("⚠")) MarkLost(false, before);
            return msg;
        }
        catch { MarkLost(false, before); throw; }
        finally { Suppress = was; }
    }

    internal const string SurfBase = "진입도로_DH";
    internal const string TempSurf = "진입도로_작업중";
    const double SurfCell = 0.5;
    const int MaxSurfTris = 1_500_000;

    /// <summary>★★[v110.1 · JACK 1029 «도로 면만 따로 보기» — 인터뷰: 도로 + 사면 · 단추로 · 정지면_DH는 안 건드림 · 이번엔 Civil 화면만]
    /// 지금 설계로 0.5 m 칸 한 번 더 계산 → Core 세모(<see cref="RoadGrade.SurfaceTris"/>) → LandXML(새 파일) → Civil 면 → 되읽어 넘긴 면과 같은지 대조 → 굳힘.
    /// 옹벽 배면 면(WallBackBuilder)과 같은 길 — Civil이 그대로 안 받았으면 지우고 실패로 알린다. 다시 누르면 앞의 면을 지우고 같은 이름으로. 명령 안에서 부른다(문서 잠김).
    /// <para>★★[v111.1 · JACK 1030 «두 단추 다»] 면을 만든 뒤 부지 노리선이 이미 그려져 있으면(DH-노리선 — [사면 변환] 뒤 저절로와 같은 판정) 저절로 다시 그린다 — 도로 면 자리는 빼고.</para></summary>
    internal string MakeSurface()
    {
        // ★[v111.3 · JACK 1030 스샷 «자꾸 정보 오류창이 뜨는데 안 뜨게»] LandXML 면 가져오기 정보(«V1.1 이전 알고리즘 … 인접이 없습니다») 한 줄에 이벤트 뷰어 창이 뜸 —
        //   계획부지 생성 · 옹벽 합성 · 터파기 면과 같은 법(EventViewerMute — 자동 알림만 끔 · 기록은 남음 · 끝나면 원래 설정)
        var evPrev = EventViewerMute.Begin();
        try { return MakeSurfaceMuted(); }
        finally { EventViewerMute.End(evPrev); }
    }

    string MakeSurfaceMuted()
    {
        string msg = MakeSurfaceCore();
        // ★[v111.3.1] 막 만든 면 = 지금 설계 — 단추 아래 «바뀜» 한 줄 · 깜박임을 바로 끔(다음 다시 계산을 기다리지 않음)
        SurfaceStale = ""; SurfaceMissing = false;
        try { Changed?.Invoke(this); } catch { }
        // ★★[v111.3 · JACK 1030 «노리선은 노리선 버튼을 누를 때 다 한꺼번에» · 인터뷰: 도로 면을 다시 만들면 «부지 · 도로 모두 지움»]
        //   v111.1의 «도로 면 뒤 부지 노리선 저절로 다시»를 거둠 — 도로 면이 바뀌면 그려 둔 노리선(부지 DH-노리선 · 모든 도로 DH도로-노리선)은 옛 모양이라 지운다. [노리선]이 둘 다 새로.
        //   (면은 이미 만들어짐 — 지우기에서 무엇이 터져도 «도로 면 실패»로 알리지 않게 · 설치 전 검토 L6과 같은 뜻)
        try
        {
            int nT = EraseAllTicks(_doc.Database, out int nLocked);   // ★[JACK 1112] 지운 만큼 아래 AutoDraw가 새로 그림 — 도킹창 «노리선 지움» 안 셈
            if (nT > 0) msg += $" · 노리선 {nT}개 지움(옛 모양)";
            if (nLocked > 0) msg += $" · ⚠노리선 {nLocked}개 못 지움(레이어 잠금?)";
        }
        catch (Exception exN) { msg += " · ⚠옛 노리선 못 지움 — " + exN.Message; }
        // ★★★[v111.8 · 계획_v111_8.md 4장 · JACK Q3 = A «매번 최종지표면까지»] 도로 면을 만든 같은 명령 안에서 최종지표면_DH(같은 실행 취소 묶음) — 실패해도 도로 면은 그대로 성공(M2: 글로 알림)
        msg += " · " + MakeFinalCore();
        // ★[JACK 1112 «노리선은 첫생성이나 업데이트 때마다 저절로»] 부지 · 모든 도로 노리선을 새 모양으로(도면 설정 «노리선 그리기» 꺼져 있으면 안 함)
        string nori = Commands.NoriCommand.AutoDraw(_doc);
        if (nori.Length > 0) msg += " · " + nori;
        return msg;
    }

    /// <summary>★[v111.8 · M2] 지난 최종지표면 짓기 결과 한 줄 — «⚠»로 시작하면 실패([업데이트] 실패 글 · 깜박 · 다시 하기).</summary>
    internal string FinalNote = "";
    /// <summary>★[v111.8] 도로 면은 지금 설계와 같은데 최종지표면이 옛것 · 없음(실패 포함) — [업데이트] 깜박 · ① 지표면 단계에서 최종면만 다시.</summary>
    internal bool FinalStale;
    internal FinalSurfaceBuilder.State FinalState = FinalSurfaceBuilder.State.NoRoads;

    /// <summary>★[v111.8] [업데이트]의 «최종면만» — 이벤트 뷰어 알림 끔(LandXML 가져오기 정보 창) · 바뀜 막음 · 로딩 글.</summary>
    internal string MakeFinal()
    {
        var evPrev = EventViewerMute.Begin();
        try { return MakeFinalCore(); }
        finally { EventViewerMute.End(evPrev); }
    }

    string MakeFinalCore()
    {
        bool was = Suppress; Suppress = true;   // 정지면_DH 숨김이 바탕면 바뀜으로 잡히지 않게(모양은 안 바뀜 — Step 0 측정)
        try
        {
            using (BusyPopup.Scope("최종지표면 만드는 중입니다."))
            {
                BusyPopup.Text("최종지표면 만드는 중입니다.");
                FinalNote = FinalSurfaceBuilder.Build(_doc);
            }
        }
        catch (Exception exF) { FinalNote = "⚠최종지표면 못 만듦 — " + exF.Message; }
        finally { Suppress = was; }
        FinalStale = false;
        try { Changed?.Invoke(this); } catch { }
        return FinalNote;
    }

    /// <summary>★[v111.8 · 7.7] 이 도로 면의 바탕 몫 지문(중심선 곁 바탕 높이 · 바탕면 핸들)이 <paramref name="baseSurf"/> · <paramref name="baseC"/>로 잰 지금과 같은가 — 가파른 띠 까닭(«지금 계획부지와 안 맞음» / «사면이 못 닿음»). 지문 없는 옛 면은 거짓.</summary>
    internal static bool BaseMatches(Database db, Transaction tr, ObjectId aid, RoadDesign d, ObjectId baseSurf, CachedGroundSurface baseC)
    {
        var was = d.SurfaceSigParts.Split('|');
        if (was.Length < 4) return false;
        var planId = Handle(db, d.PlanProfile);
        if (planId.IsNull) return false;
        // ★[도로 마무리 ① · 계획 5.6] 도로 구간 토막마다(하나면 지금과 같음)
        var g = BranchBase.For(tr, db, aid, d, baseC, null, out _);   // ★[계획 5.5] 가지면 그 도로가 지어진 합친 바탕으로 잼
        var now = SigPartsAll(MakePieces(tr, aid, planId, d, g, RoadRanges.Of(tr, aid, d)), baseSurf);
        return now[3] == was[3];
    }

    string MakeSurfaceCore()
    {
        var db = _doc.Database;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var d = Fresh();
        EnsureBase();
        _pendingWallNos = null; _pendingPath = null;   // ★[v115 · R1] 토막 도로 · 앞 호출 값이 남지 않게(선 옹벽 없는 면 = null)
        _pendingRingNote = ""; RingCache = null;      // ★[v116] 고리 면 물러섬 까닭 · 노리선 꾸러미도
        List<(double, double)>? limit; List<Piece> pcs;
        using (var tr = db.TransactionManager.StartTransaction())
        {
            ComputeEnds(tr, db);   // ★[도로 마무리 ① · 계획 5.8] 도로 구간 · 토막
            pcs = MakePieces(tr, AlignId, PlanId, d, EngineGround(tr, db), Ranges);   // ★[계획 5.5] 가지면 바탕면 + 본 도로 면
            limit = ReadLimit(tr);
            tr.Commit();
        }
        double reach = Last != null && Last.Error.Length == 0 && !Last.ReachHit && Last.ReachUsed > 40 ? Last.ReachUsed : 40;
        foreach (var pc in pcs) { pc.Inp.Cell = SurfCell; pc.Inp.Reach = reach; pc.Inp.Limit = limit; }
        var inp = pcs[0].Inp;
        try { _pendingSig = SigOfAll(pcs, BaseSurfId); } catch { _pendingSig = ""; }   // ★[v111.2 · 전체 검토 L2] 이 면의 입력 지문 — 나중에 설계가 바뀌면 알림(못 세면 알림 안 함 · 면은 그대로 만듦)
        _pendingReach = reach;   // ★[v111.3] [노리선]이 같은 입력(시작 범위 포함)으로 다시 셈
        try { _pendingParts = string.Join("|", SigPartsAll(pcs, BaseSurfId)); } catch { _pendingParts = ""; }
        if (pcs.Count > 1) return MakeSurfacePieces(d, pcs, sw);
        // ★★★[v116 · 고리 엔진 계획 3판 §5 · D2] 벽 기록이 없는 도로 = 고리 면 먼저(세모 0.25 m · Core 관문) — Civil 관문(LandXML · 되읽기 대조 · 굳힘 · 이름)은 그대로.
        //   Core가 못 짓거나 Civil이 거절하면 아래 옛 엔진 사슬(선 면 → 선 따라 TIN → 칸 면)로 물러서고 까닭 한 줄(_pendingRingNote)
        //   ★★[v116.7] 선 옹벽만 있는 도로도 고리 면 위에 벽(RingBase) — 옛 판은 벽 기록이 있으면 늘 옛 엔진이라 굽은 곳 옛 면이 깨져 합성이 막혔다
        if (RingBase(inp))
        {
            string keyR = ""; if (inp.LineWalls.Count > 0) try { keyR = KeyNoWalls(pcs); } catch { }
            if (TryRingSurface(d, inp, sw, out string ringMsg, keyR)) return ringMsg;
        }
        var r = RoadGrade.Run(inp);
        if (r.Error.Length > 0) throw new InvalidOperationException(r.Error);
        // ★★★[v111 · 계획_v111.md 2.5] 선 면(단면 광선 띠) — Core 검사를 못 넘으면 칸 면(알림) · Civil이 못 받으면 아래에서 칸 면으로 한 번 더(계획 검토 H1)
        var tris = RoadGrade.ExactSurface(inp, r, out string tnote, out var lines);
        bool exact = !tnote.StartsWith("⚠");
        // ★[설치 전 검토 중간 3] 아주 큰 도로(세모 150만 넘음) — 1 m 칸으로 다시(시간 · 메모리) — 칸 면일 때만
        if (!exact && tris.Count > MaxSurfTris)
        {
            inp.Cell = 1.0;
            r = RoadGrade.Run(inp);
            if (r.Error.Length > 0) throw new InvalidOperationException(r.Error);
            tris = RoadGrade.SurfaceTris(r, out string t1);
            lines = RoadGrade.TierLines(r);
            tnote += " · " + t1 + " · 커서 1 m 칸으로";
        }
        if (tris.Count == 0) throw new InvalidOperationException("바뀐 자리가 없어 면을 안 만들었습니다 — " + tnote);
        // ★★★[v114.3 · JACK 1105 진입도로 옹벽 추가 · 계획 2판 3절 «물러섬»] 시도 하나 = P0_k(그 길의 벽 없는 세모) → 벽마다 짓기 · 합성 → Civil.
        //   Civil이 벽 든 면을 거절하면 같은 P0_k로 벽을 다 쉬고 한 번 더(도로 길은 그대로) → 그것도 거절되면 그때 길을 내린다(선 면 → 선 따라 TIN → 칸 면).
        //   칸 면 위에는 벽을 안 짓는다. 길마다 Civil 왕복 ≤ 2. 불변식: CreateSurface 앞에는 늘 GridSet(r, 그 시도 벽) — 격자 · 부피가 그 면과 같다.
        var lw = inp.LineWalls;
        var bls = WallBlockers(db);
        var dl0 = r.DaylightZ; var d0 = r.Daylight;
        Walls = null; WallsSig = "";
        RoadGrade.WallSurface? Wl(List<WallDaylight.Tri> p0, List<RoadGrade.TierLine> ls, string path, ISet<int>? rest)
        {
            if (lw.Count == 0) return null;
            r.DaylightZ = dl0; r.Daylight = d0;
            // ★[v115 · 설치 전 검토 R4] 벽 얹기에서 예외가 새도 MakeSurface는 살린다 — 벽을 모두 쉬고 그 길의 벽 없는 면으로
            RoadGrade.WallSurface ws;
            try { ws = RoadGrade.ApplyWalls(inp, r, p0, ls, path, lw, bls, rest); }
            catch (System.Exception exW) { ws = RoadGrade.Rested(p0, ls, dl0, path, lw, "선 옹벽 짓기 예외 — " + exW.Message + " — 이 시도에서 벽을 쉽니다"); }
            try { DiagLog.Append($"\n[진입도로] {Name} — 선 옹벽({path}{(rest != null ? " · 쉼 " + rest.Count : "")}) {ws.Ms} ms — {ws.Note}" + string.Concat(ws.Walls.Select(w => w.Log.Length > 0 ? "\n" + w.Log.TrimEnd() : ""))); } catch { }
            return ws;
        }
        // ★[v115 · 설치 전 검토 R7] P0 캐시는 Civil이 받은 뒤 · 실제로 보낸 길로(옛 판은 Civil에 보내기 전에 채워, 거절되어 길을 내려도 [옹벽 추가] 판정이 옛 길 P0를 썼다).
        //   데이라잇은 사본을 둔다(GridSet · Try가 r.DaylightZ를 벽 든 것으로 바꾼다)
        string keyNW = ""; try { keyNW = KeyNoWalls(pcs); } catch { }
        var dl0c = dl0.Select(l => l.ToList()).ToList();
        string Try(List<WallDaylight.Tri> p0, List<RoadGrade.TierLine> ls, string note, RoadGrade.WallSurface? ws, string pathK, RoadGrade.Result? lineR = null)
        {
            bool any = ws != null && ws.Any;
            if (ws != null) RoadGrade.GridSet(r, any ? ws : null);   // 벽 기록이 없는 도로는 격자를 안 건드림(오늘과 비트 같음)
            r.DaylightZ = any ? ws!.DaylightZ : dl0; r.Daylight = r.DaylightZ.Select(l => l.Select(q => (q.X, q.Y)).ToList()).ToList();
            string wn = ws == null ? "" : " · " + string.Join(" · ", ws.Walls.Select(w => w.Text));
            // ★[v115 · 설치 전 검토 R1] 이 면에 실제로 지은 벽 번호 · 보낸 길을 설계 기록에(SurfaceSig 곁) — [노리선]이 같은 벽 · 같은 길로만 다시 얹는다(벽 기록 없는 도로 = null · 옛 JSON 그대로)
            _pendingWallNos = lw.Count == 0 ? null : (any ? ws!.Built.Select(w => w.No).ToArray() : Array.Empty<int>());
            _pendingPath = lw.Count == 0 ? null : pathK;
            string msg = CreateSurface(d, r, any ? ws!.Tris : p0, note, any ? ws!.Lines : ls, sw, lineR, wn);
            Walls = ws; WallsSig = d.SurfaceSig; WallVol = (r.CutVol, r.FillVol, r.MaxCut, r.MaxFill);
            if (keyNW.Length > 0) P0Cache = (keyNW, inp, r, p0, ls, pathK, dl0c, null);
            return msg;
        }
        bool Refused(InvalidOperationException e) => e.Message.StartsWith("Civil이 면을 그대로 안 받았습니다");
        string path0 = PathOf(tnote);   // ★[v116] 판정 한 곳(옛 노트 = 옛 판정 그대로 — exact = ⚠ 아님)
        P0Cache = null;   // ★[v115 · R7] Civil이 받기 전엔 비움(받은 뒤 Try가 그 길로 채움)
        // ★[v115 · 설치 전 검토 R10] 굽은 안쪽 «옛 길로 지음» ⚠은 Civil 거절로 길을 내려도 남긴다(내린 길도 같은 옛 길 광선 · 격자)
        string oldTag = tnote.Contains("→ 옛 흘러내림 길") ? " · (→ 옛 흘러내림 길)" : "";
        var ws1 = Wl(tris, lines, path0, null);
        try { return Try(tris, lines, tnote, ws1, path0); }
        catch (InvalidOperationException exG) when (exact && Refused(exG))
        {
            r.DaylightZ = dl0; r.Daylight = d0;
            // ★[v114.3] 벽 든 면을 Civil이 거절 — 같은 P0로 벽을 다 쉬고 한 번 더(도로 길 안 내림)
            if (ws1 != null && ws1.Any)
            {
                try { DiagLog.Append($"\n[진입도로] {Name} — ⚠선 옹벽 든 면을 Civil이 못 받음({exG.Message}) → 같은 {path0}로 벽을 쉬고 다시"); } catch { }
                // ★[v115 · 설치 전 검토 R2] 기록된 벽 모두 쉼(지은 것만 쉬면 못 지은 벽을 다시 짓는다) — 못 지은 벽은 첫 시도 까닭 그대로 보인다
                var built1 = new HashSet<int>(ws1.Built.Select(w => w.No));
                var restAll = new HashSet<int>(lw.Select(w => w.No));
                var ws1b = Wl(tris, lines, path0, restAll);
                for (int i = 0; i < ws1b!.Walls.Count && i < ws1.Walls.Count; i++)
                    if (built1.Contains(ws1b.Walls[i].No)) ws1b.Walls[i].Why = "Civil 거절(" + exG.Message.Replace("Civil이 면을 그대로 안 받았습니다 — ", "") + ")";
                    else if (ws1.Walls[i].No == ws1b.Walls[i].No) ws1b.Walls[i] = ws1.Walls[i];
                try { return Try(tris, lines, tnote, ws1b, path0); }
                catch (InvalidOperationException exGb) when (Refused(exGb)) { exG = exGb; r.DaylightZ = dl0; r.Daylight = d0; }
            }
            // ★★[JACK 1104] Civil이 선 면을 안 받으면 칸 면 전에 선 따라 TIN(이미 선 따라 TIN이었으면 바로 칸 면) — 단계마다 까닭을 남김
            string path1 = tnote.StartsWith("선 따라 TIN") ? "선 따라 TIN" : "선 면";
            if (path1 == "선 면")
            {
                if (lw.Count > 0) RoadGrade.GridSet(r, null);   // ★[v114.3] 시도를 바꿀 때마다 먼저 — 벽 층을 걷고 B1 층을 그 횡단으로(벽 기록 없는 도로는 그대로)
                var bt = RoadGrade.BreakTinSurface(inp, r, out string bn, lines);
                try { DiagLog.Append($"\n[진입도로] {Name} — ⚠선 면을 Civil이 못 받음({exG.Message}) → " + (bt != null ? "선 따라 TIN으로 다시" : $"선 따라 TIN 못 함({bn}) → 칸 면으로 다시")); } catch { }
                if (bt != null && bt.Count > 0)
                {
                    var ws2 = Wl(bt, lines, "선 따라 TIN", null);
                    try { return Try(bt, lines, bn + " · (선 면을 Civil이 못 받음)" + oldTag, ws2, "선 따라 TIN"); }
                    catch (InvalidOperationException exG2) when (Refused(exG2))
                    {
                        r.DaylightZ = dl0; r.Daylight = d0;
                        bool again = false;
                        if (ws2 != null && ws2.Any)
                        {
                            try { DiagLog.Append($"\n[진입도로] {Name} — ⚠선 옹벽 든 선 따라 TIN을 Civil이 못 받음({exG2.Message}) → 벽을 쉬고 다시"); } catch { }
                            var built2 = new HashSet<int>(ws2.Built.Select(w => w.No));   // ★[v115 · R2] 기록된 벽 모두 쉼 · 못 지은 벽은 제 까닭
                            var rest2 = new HashSet<int>(lw.Select(w => w.No));
                            var ws2b = Wl(bt, lines, "선 따라 TIN", rest2);
                            for (int i = 0; i < ws2b!.Walls.Count && i < ws2.Walls.Count; i++)
                                if (built2.Contains(ws2b.Walls[i].No)) ws2b.Walls[i].Why = "Civil 거절(" + exG2.Message.Replace("Civil이 면을 그대로 안 받았습니다 — ", "") + ")";
                                else if (ws2.Walls[i].No == ws2b.Walls[i].No) ws2b.Walls[i] = ws2.Walls[i];
                            try { return Try(bt, lines, bn + " · (선 면을 Civil이 못 받음)" + oldTag, ws2b, "선 따라 TIN"); }
                            catch (InvalidOperationException exG3) when (Refused(exG3)) { again = true; r.DaylightZ = dl0; r.Daylight = d0; try { DiagLog.Append($"\n[진입도로] {Name} — ⚠벽 쉰 선 따라 TIN도 Civil이 못 받음({exG3.Message}) → 칸 면으로 다시"); } catch { } }
                        }
                        if (!again) try { DiagLog.Append($"\n[진입도로] {Name} — ⚠선 따라 TIN도 Civil이 못 받음({exG2.Message}) → 칸 면으로 다시"); } catch { }
                    }
                }
            }
            else try { DiagLog.Append($"\n[진입도로] {Name} — ⚠선 따라 TIN을 Civil이 못 받음({exG.Message}) → 칸 면으로 다시"); } catch { }
            if (lw.Count > 0) RoadGrade.GridSet(r, null);   // ★[v114.3] 칸 면은 격자만 읽는다 — 벽 층을 걷은 뒤(벽이 두 번 들어가지 않게)
            var old = RoadGrade.SurfaceTris(r, out string t2);
            var lineR = r;   // ★[v111.1] 선 · 노리선은 처음 결과의 광선으로(1 m 칸 결과엔 광선이 없음)
            // ★[설치 전 검토 L9] 칸 면도 너무 크면 1 m 칸으로
            if (old.Count > MaxSurfTris)
            {
                inp.Cell = 1.0;
                var r1 = RoadGrade.Run(inp);
                if (r1.Error.Length > 0) throw new InvalidOperationException(r1.Error);
                old = RoadGrade.SurfaceTris(r1, out t2);
                t2 += " · 커서 1 m 칸으로";
                r = r1;
            }
            // ★[설치 전 검토 M3] 면만 칸 면 — 선은 단면 광선 선 그대로(미리보기 · [옹벽 변환] 찍기 선과 같게)
            r.DaylightZ = dl0; r.Daylight = d0; Walls = null;
            string wn3 = lw.Count > 0 ? $" · ⚠추가한 옹벽 {lw.Count}개 쉼 — 칸 면 위에는 선 옹벽 안 지음(선 면 · 선 따라 TIN을 Civil이 거절)" : "";
            _pendingWallNos = lw.Count == 0 ? null : Array.Empty<int>(); _pendingPath = lw.Count == 0 ? null : "칸 면";   // ★[v115 · R1]
            string msg3 = CreateSurface(d, r, old, $"⚠{path1}을 Civil이 못 받음 → 칸 면 · " + t2 + oldTag, lines, sw, lineR, wn3);
            if (keyNW.Length > 0) P0Cache = (keyNW, inp, r, old, lines, "칸 면", dl0c, null);   // ★[v115 · R7] 받은 뒤 · 보낸 길
            return msg3;
        }
    }

    /// <summary>★[v114.3 · JACK 1105 진입도로 옹벽 추가] 선 옹벽 막을 다각형 — 부지 계획선(바꿀 자리가 부지 바닥에 들어가면 막음).</summary>
    static List<(string Name, IReadOnlyList<Point3> Ring)> WallBlockers(Database db)
    {
        var bl = new List<(string, IReadOnlyList<Point3>)>();
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            var regs = GradingBundleStore.TryLoadAll(db, tr, out _);
            if (regs != null) for (int k = 0; k < regs.Count; k++) if (regs[k]?.Boundary is { Count: >= 3 } b) bl.Add(($"부지 계획선 {k + 1}", b));
            tr.Commit();
        }
        catch { }
        return bl;
    }

    /// <summary>★★★[v116 · 고리 엔진 계획 3판 §5] 한 토막 도로 고리 면 — Core 세모 → <see cref="CreateSurface"/>(Civil 관문 그대로) · 지었으면 참(<paramref name="msg"/> = 알림).
    /// Core가 못 지음 · 예외 · Civil 거절이면 거짓 — 까닭은 <see cref="_pendingRingNote"/>(부르는 쪽이 옛 엔진 사슬 · 그 알림 끝에 붙음).</summary>
    bool TryRingSurface(RoadDesign d, RoadGrade.Input inp, System.Diagnostics.Stopwatch sw, out string msg, string keyNW = "")
    {
        msg = "";
        string why;
        try
        {
            var b = RingRun(inp, true, out double used);
            if (b.Ok && b.Tris.Count > 0)
            {
                var rr = RoadRing.ToResult(b, inp); rr.ReachUsed = used;
                Walls = null; WallsSig = "";
                _pendingWallNos = null; _pendingPath = RingPath;
                var lw = inp.LineWalls;
                if (lw.Count == 0)
                {
                    try
                    {
                        msg = CreateSurface(d, rr, b.Tris, b.Note, DispLines(b, b.Lines), sw);
                        RingCache = (d.SurfaceSig, new RoadRing.Built?[] { Light(b) });
                        return true;
                    }
                    // 고리 세모를 Civil이 그대로 안 받음(되읽기 대조) · 그 밖 Civil 예외 — 커밋 전이라 만든 면은 CreateSurface가 지움 → 옛 엔진으로
                    catch (System.Exception exC) { why = CivilWhy(exC); }
                }
                else
                {
                    // ★★★[v116.7 · JACK 1110 «어디든 폴리선을 그으면 작성이 되어야»] 선 옹벽 든 도로 — 고리 면 P0 위에 벽을 얹는다(단 높이 = 고리 셈 RingBench).
                    //   옛 엔진 사슬과 같은 물러섬: Civil이 벽 든 면을 거절하면 같은 고리 P0로 벽을 다 쉬고 한 번 더 → 그것도 거절이면 옛 엔진 사슬로
                    var bls = WallBlockers(_doc.Database);
                    var dl0 = rr.DaylightZ.Select(l => l.ToList()).ToList();
                    RoadGrade.WallSurface Wl(ISet<int>? rest)
                    {
                        RoadGrade.GridSet(rr, null);
                        rr.DaylightZ = dl0.Select(l => l.ToList()).ToList(); rr.Daylight = rr.DaylightZ.Select(l => l.Select(q => (q.X, q.Y)).ToList()).ToList();
                        RoadGrade.WallSurface ws;
                        try { ws = RoadGrade.ApplyWalls(inp, rr, b.Tris, b.Lines, RingPath, lw, bls, rest, ring: b); }
                        catch (System.Exception exW) { ws = RoadGrade.Rested(b.Tris, b.Lines, rr.DaylightZ, RingPath, lw, "선 옹벽 짓기 예외 — " + exW.Message + " — 이 시도에서 벽을 쉽니다"); }
                        try { DiagLog.Append($"\n[진입도로] {Name} — 선 옹벽({RingPath}{(rest != null ? " · 쉼 " + rest.Count : "")}) {ws.Ms} ms — {ws.Note}" + string.Concat(ws.Walls.Select(w => w.Log.Length > 0 ? "\n" + w.Log.TrimEnd() : ""))); } catch { }
                        return ws;
                    }
                    string TryW(RoadGrade.WallSurface ws)
                    {
                        bool any = ws.Any;
                        RoadGrade.GridSet(rr, any ? ws : null);
                        rr.DaylightZ = any ? ws.DaylightZ : dl0.Select(l => l.ToList()).ToList(); rr.Daylight = rr.DaylightZ.Select(l => l.Select(q => (q.X, q.Y)).ToList()).ToList();
                        string wn = " · " + string.Join(" · ", ws.Walls.Select(w => w.Text));
                        _pendingWallNos = any ? ws.Built.Select(w => w.No).ToArray() : Array.Empty<int>();
                        _pendingPath = RingPath;
                        // ★[설치 전 검토 H] 표시 선 → 지은 벽 자리 자르기(미리보기와 같은 차례 — 벽 자리 안 소단 턱을 더하지 않게)
                        string m = CreateSurface(d, rr, any ? ws.Tris : b.Tris, b.Note, any ? RoadGrade.CutLines(DispLines(b, b.Lines), ws) : DispLines(b, b.Lines), sw, null, wn);
                        Walls = ws; WallsSig = d.SurfaceSig; WallVol = (rr.CutVol, rr.FillVol, rr.MaxCut, rr.MaxFill);
                        if (keyNW.Length > 0) P0Cache = (keyNW, inp, rr, b.Tris, b.Lines, RingPath, dl0.Select(l => l.ToList()).ToList(), Light(b));
                        RingCache = (d.SurfaceSig, new RoadRing.Built?[] { Light(b) });
                        return m;
                    }
                    static bool Refused(System.Exception e) => e is InvalidOperationException && e.Message.StartsWith("Civil이 면을 그대로 안 받았습니다");
                    var ws1 = Wl(null);
                    try { msg = TryW(ws1); return true; }
                    catch (System.Exception exC) when (ws1.Any && Refused(exC))
                    {
                        try { DiagLog.Append($"\n[진입도로] {Name} — ⚠선 옹벽 든 고리 면을 Civil이 못 받음({exC.Message}) → 고리 면으로 벽을 쉬고 다시"); } catch { }
                        var built1 = new HashSet<int>(ws1.Built.Select(w => w.No));
                        var ws1b = Wl(new HashSet<int>(lw.Select(w => w.No)));
                        for (int i = 0; i < ws1b.Walls.Count && i < ws1.Walls.Count; i++)
                            if (built1.Contains(ws1b.Walls[i].No)) ws1b.Walls[i].Why = "Civil 거절(" + exC.Message.Replace("Civil이 면을 그대로 안 받았습니다 — ", "") + ")";
                            else if (ws1.Walls[i].No == ws1b.Walls[i].No) ws1b.Walls[i] = ws1.Walls[i];
                        try { msg = TryW(ws1b); return true; }
                        catch (System.Exception exC2) { why = CivilWhy(exC2); }
                    }
                    catch (System.Exception exC) { why = CivilWhy(exC); }
                    Walls = null; WallsSig = ""; _pendingWallNos = null;
                }
            }
            else why = RingWhy(b);
        }
        catch (System.Exception exR) { why = "예외 — " + exR.Message; try { DiagLog.Append($"\n[진입도로] {Name} — 고리 면 예외 — {exR}"); } catch { } }
        _pendingPath = null;
        _pendingRingNote = $" · ⚠고리 면 못 지음({why}) → 옛 엔진";
        try { DiagLog.Append($"\n[진입도로] {Name} —{_pendingRingNote}"); } catch { }
        return false;
    }

    /// <summary>★[v116] 고리 면을 Civil에 넣다 난 예외 → 까닭 한 줄(되읽기 대조 거절 · 그 밖 예외).</summary>
    static string CivilWhy(System.Exception e)
    {
        const string h = "Civil이 면을 그대로 안 받았습니다";
        return e is InvalidOperationException && e.Message.StartsWith(h) ? "Civil이 안 받음" + e.Message[h.Length..] : "면 만들기 예외 — " + e.Message;
    }

    /// <summary>★[v116] 고리 면 못 지은 까닭(노트 머리 «고리 면 …» 뺌).</summary>
    static string RingWhy(RoadRing.Built b)
    {
        if (b.Ok) return "세모 없음";
        const string h1 = "고리 면 못 지음 — ", h2 = "고리 면 ";
        return b.Note.StartsWith(h1) ? b.Note[h1.Length..] : b.Note.StartsWith(h2) ? b.Note[h2.Length..] : b.Note;
    }

    /// <summary>★★[도로 마무리 ① · 계획 5.6 · Q6] 빈 구간이 있는 도로 — 토막마다 엔진(양 끝 마구리 · 끝 높이 고정은 종단이) · 세모를 합쳐 도로 면 하나(서로 안 닿는 조각 · LandXML 하나).
    /// Civil이 선 면을 안 받으면 모든 토막을 칸 면으로 한 번 더(한 토막 도로와 같은 법).</summary>
    string MakeSurfacePieces(RoadDesign d, List<Piece> pcs, System.Diagnostics.Stopwatch sw)
    {
        int n = pcs.Count;
        // 옛 엔진 토막 결과(한 번 셈 — 섞은 시도 · 아래 옛 사슬이 같이 씀)
        var oldR = new RoadGrade.Result?[n]; var oldT = new List<WallDaylight.Tri>?[n]; var oldN = new string[n]; var oldL = new List<RoadGrade.TierLine>?[n];
        void Old(int k)
        {
            if (oldR[k] != null) return;
            var r = RoadGrade.Run(pcs[k].Inp);
            if (r.Error.Length > 0) throw new InvalidOperationException(r.Error);
            oldT[k] = RoadGrade.ExactSurface(pcs[k].Inp, r, out string tn, out var ls); oldN[k] = tn; oldL[k] = ls; oldR[k] = r;
        }
        // ★★★[v116 · 고리 엔진 계획 3판 §5 · D2 · M5a] 벽 기록 없는 토막 = 고리 면 — 하나라도 지으면 고리 토막 + 나머지 옛 엔진(그 토막 첫 길)을 섞어 Civil에 한 번(토막마다 길 기록).
        //   Civil이 거절하면 모든 토막을 아래 옛 사슬로(무엇을 못 받았는지 모름) · 못 지은 까닭은 알림 끝에
        var ringB = new RoadRing.Built?[n]; var ringUsed = new double[n]; var why = new List<string>();
        for (int k = 0; k < n; k++)
        {
            if (!RingBase(pcs[k].Inp)) continue;   // ★[v116.7] 빈 구간 도로는 선 옹벽을 안 지음(BoundNote) — 바탕은 고리
            try
            {
                var b = RingRun(pcs[k].Inp, true, out ringUsed[k]);
                if (b.Ok && b.Tris.Count > 0) ringB[k] = b; else why.Add($"토막 {k + 1} " + RingWhy(b));
            }
            catch (System.Exception exR) { why.Add($"토막 {k + 1} 예외 — {exR.Message}"); try { DiagLog.Append($"\n[진입도로] {Name} — 토막 {k + 1} 고리 면 예외 — {exR}"); } catch { } }
        }
        if (ringB.Any(b => b != null))
        {
            var trisM = new List<WallDaylight.Tri>(); var linesM = new List<RoadGrade.TierLine>(); var rsM = new List<RoadGrade.Result>(); var notesM = new List<string>(); var paths = new List<string>();
            bool exactM = true;
            for (int k = 0; k < n; k++)
            {
                RoadGrade.Result? rr = null;
                if (ringB[k] is { } b)
                {
                    // ★[v116 · 설치 전 검토 L] 결과 변환도 예외면 그 토막만 옛 엔진으로(전체 [업데이트] 실패 대신)
                    try { rr = RoadRing.ToResult(b, pcs[k].Inp); rr.ReachUsed = ringUsed[k]; }
                    catch (System.Exception exT) { rr = null; why.Add($"토막 {k + 1} 결과 변환 예외 — {exT.Message}"); }
                    if (rr != null) { trisM.AddRange(b.Tris); linesM.AddRange(DispLines(b, b.Lines)); rsM.Add(rr); notesM.Add(b.Note); paths.Add(RingPath); }
                }
                if (rr == null)
                {
                    Old(k);
                    if (oldN[k].StartsWith("⚠")) exactM = false;
                    trisM.AddRange(oldT[k]!); linesM.AddRange(oldL[k]!); rsM.Add(oldR[k]!); notesM.Add(oldN[k]); paths.Add(PathOf(oldN[k]));
                }
            }
            string volM = $"토막 {n}(빈 구간) · 절토 {rsM.Sum(q => q.CutVol):N0} ㎥ · 성토 {rsM.Sum(q => q.FillVol):N0} ㎥";
            _pendingWallNos = null; _pendingPath = string.Join("|", paths);   // 토막 차례 길(고리 면 · 선 면 · 선 따라 TIN · 칸 면)
            _pendingRingNote = why.Count > 0 ? $" · ⚠고리 면 못 지음({string.Join(" / ", why)}) → 그 토막 옛 엔진" : "";
            try
            {
                string m = CreateSurface(d, rsM[0], trisM, (exactM ? "" : "⚠") + string.Join(" / ", notesM), linesM, sw) + " · " + volM;
                RingCache = (d.SurfaceSig, ringB.Select(b => b == null ? null : Light(b)).ToArray());
                return m;
            }
            catch (System.Exception exC)
            {
                why.Add(CivilWhy(exC));
                _pendingPath = null;
                try { DiagLog.Append($"\n[진입도로] {Name} — ⚠고리 면 섞은 토막 면을 Civil이 못 받음({exC.Message}) → 모든 토막 옛 엔진으로 다시(토막 {n})"); } catch { }
            }
        }
        _pendingRingNote = why.Count > 0 ? $" · ⚠고리 면 못 지음({string.Join(" / ", why)}) → 옛 엔진" : "";
        var tris = new List<WallDaylight.Tri>(); var lines = new List<RoadGrade.TierLine>();
        var rs = new List<RoadGrade.Result>(); var notes = new List<string>(); bool exact = true;
        for (int k = 0; k < n; k++)
        {
            Old(k);
            string tn = oldN[k];
            if (tn.StartsWith("⚠")) exact = false;
            tris.AddRange(oldT[k]!); lines.AddRange(oldL[k]!); rs.Add(oldR[k]!); notes.Add(tn);
        }
        if (tris.Count == 0) throw new InvalidOperationException("바뀐 자리가 없어 면을 안 만들었습니다");
        string vol = $"토막 {pcs.Count}(빈 구간) · 절토 {rs.Sum(q => q.CutVol):N0} ㎥ · 성토 {rs.Sum(q => q.FillVol):N0} ㎥";
        string tnote = (exact ? "" : "⚠") + string.Join(" / ", notes);
        string oldTag = tnote.Contains("→ 옛 흘러내림 길") ? " · (→ 옛 흘러내림 길)" : "";   // ★[v115 · 설치 전 검토 R10] 길을 내려도 «옛 길로 지음» ⚠ 남김
        try { return CreateSurface(d, rs[0], tris, tnote, lines, sw) + " · " + vol; }
        catch (InvalidOperationException exG) when (exact && exG.Message.StartsWith("Civil이 면을 그대로 안 받았습니다"))
        {
            // ★★[JACK 1104 (c)] 빈 구간 도로도 한 토막 도로와 같은 사슬 — 선 면 토막은 선 따라 TIN으로 다시(이미 TIN인 토막은 그대로) → 그래도 못 받으면 칸 면
            {
                var bt = new List<WallDaylight.Tri>(); var bns = new List<string>(); bool okAll = true;
                for (int k = 0; k < pcs.Count && okAll; k++)
                {
                    if (notes[k].StartsWith("선 따라 TIN")) { okAll = false; break; }   // 이미 TIN이었던 토막이 있으면 칸 면으로(무엇을 못 받았는지 모름)
                    var b = RoadGrade.BreakTinSurface(pcs[k].Inp, rs[k], out string bn);
                    if (b == null || b.Count == 0) { okAll = false; bns.Add(bn); break; }
                    bt.AddRange(b); bns.Add(bn);
                }
                try { DiagLog.Append($"\n[진입도로] {Name} — ⚠선 면을 Civil이 못 받음({exG.Message}) → " + (okAll ? $"선 따라 TIN으로 다시(토막 {pcs.Count})" : $"선 따라 TIN 못 함({string.Join(" / ", bns)}) → 칸 면으로 다시(토막 {pcs.Count})")); } catch { }
                if (okAll)
                {
                    try { return CreateSurface(d, rs[0], bt, "선 따라 TIN — " + string.Join(" / ", bns) + " · (선 면을 Civil이 못 받음)" + oldTag, lines, sw) + " · " + vol; }
                    catch (InvalidOperationException exG2) when (exG2.Message.StartsWith("Civil이 면을 그대로 안 받았습니다"))
                    { try { DiagLog.Append($"\n[진입도로] {Name} — ⚠선 따라 TIN도 Civil이 못 받음({exG2.Message}) → 칸 면으로 다시(토막 {pcs.Count})"); } catch { } }
                }
            }
            var old = new List<WallDaylight.Tri>(); var t2 = new List<string>();
            foreach (var r in rs) { old.AddRange(RoadGrade.SurfaceTris(r, out string n2)); t2.Add(n2); }
            return CreateSurface(d, rs[0], old, "⚠선 면을 Civil이 못 받음 → 칸 면 · " + string.Join(" / ", t2) + oldTag, lines, sw) + " · " + vol;
        }
    }

    /// <summary>세모 → LandXML → Civil 면 → 되읽기 대조(못 맞으면 예외 · 만든 면 지움) → 굳힘 · 이름 · 스타일 → 실제 선. 돌려줌 = 알림 한 줄.
    /// <paramref name="lineR"/> = 선 · 노리선을 뽑을 결과(없으면 <paramref name="r"/>).</summary>
    string CreateSurface(RoadDesign d, RoadGrade.Result r, List<WallDaylight.Tri> tris, string tnote, List<RoadGrade.TierLine> lines, System.Diagnostics.Stopwatch sw, RoadGrade.Result? lineR = null, string wallNote = "")
    {
        var db = _doc.Database;
        long msGrid = sw.ElapsedMilliseconds;
        // ★[v111.7.1 · 검토 낮음] 고칠 때마다 저절로 새 면 = 새 XML 파일 — 새 파일을 쓰기 «앞»에서 아무 면도 안 무는 옛 원본을 치운다(부지 생성 · 옹벽 합성과 같은 TidyXml · 되돌리기 여유는 XmlStore가 지킴)
        try
        {
            string td;
            using (var trT = db.TransactionManager.StartTransaction()) { td = GradingBuilder.TidyXml(db, trT); trT.Commit(); }
            DiagLog.Append($"\n[진입도로] {Name} — XML 정리: " + (td.Length > 0 ? td : "지울 것 없음"));
        }
        catch (System.Exception exT) { try { DiagLog.Append($"\n[진입도로] {Name} — XML 정리 못 함 — {exT.Message}"); } catch { } }
        string xml = GradingBuilder.NewXmlPath(db, "진입도로");
        WallDaylight.WriteLandXmlTin(xml, "ROAD", tris, 1e-7, out int np, out int nf, out int nd);
        long msXml = sw.ElapsedMilliseconds;
        ObjectId made = ObjectId.Null;
        bool was = Suppress; Suppress = true;   // ★[v110.4 · 검토 L1]
        try
        {
            // ★[설치 전 검토 낮음 4] 앞에서 못 치운 작업 면이 남았으면 먼저 치운다
            using (var trC = db.TransactionManager.StartTransaction())
            {
                var cdocC = Autodesk.Civil.ApplicationServices.CivilDocument.GetCivilDocument(db);
                foreach (ObjectId sid in cdocC.GetSurfaceIds())
                {
                    if (sid.IsNull || sid.IsErased) continue;
                    try { if (trC.GetObject(sid, OpenMode.ForRead) is CivilDb.Surface sS && sS.Name == TempSurf) trC.GetObject(sid, OpenMode.ForWrite, false, true).Erase(); } catch { }
                }
                trC.Commit();
            }
            // 실패하면 트랜잭션을 버린다(앞 면 지움 · 기록도 되돌아감) — 만든 면이 그래도 남았으면 아래 finally가 따로 지운다
            using (var tr = db.TransactionManager.StartTransaction())
            {
                ObjectId cid = CivilDb.TinSurface.CreateFromLandXML(db, TempSurf, xml, "ROAD");
                if (cid.IsNull) throw new InvalidOperationException("CreateFromLandXML이 빈 ObjectId를 돌려줬다");
                made = cid;
                var cTin = (CivilDb.TinSurface)tr.GetObject(cid, OpenMode.ForWrite, false, true);
                int civPts = -1; try { civPts = cTin.GetGeneralProperties().NumberOfPoints; } catch { }
                var theirs = PureWithWallsBuilder.ReadTris(cTin, out int lost);
                long msMake = sw.ElapsedMilliseconds;
                // ★[설치 전 검토 중간 3] 수부터 틀리면 대조(다른 세모 전부 합집합 — 큰 면에서 수 분)를 하지 않고 바로 실패
                if (lost != 0 || nd != 0 || theirs.Count != nf || civPts != np)
                    throw new InvalidOperationException($"Civil이 면을 그대로 안 받았습니다 — Civil 점 {civPts}/{np} · 세모 {theirs.Count}/{nf} · 못 읽음 {lost} · 합친 세모 {nd}");
                var cmp = CivilSafeMesh.Compare(tris, theirs);
                long msCmp = sw.ElapsedMilliseconds;
                double area = 0; foreach (var t in tris) area += Math.Abs((t.B.X - t.A.X) * (t.C.Y - t.A.Y) - (t.B.Y - t.A.Y) * (t.C.X - t.A.X)) / 2;
                bool gate = lost == 0 && nd == 0 && theirs.Count == nf && civPts == np && cmp.Exceptions == 0
                            && cmp.SymArea <= 1e-8 + 1e-12 * area && cmp.Hausdorff <= 1e-6 && cmp.MaxDz <= 1e-3;
                string back = $"Civil 점 {civPts}/{np} · 세모 {theirs.Count}/{nf} · {cmp.Summary}";
                if (!gate) throw new InvalidOperationException("Civil이 면을 그대로 안 받았습니다 — " + back);
                string frz = GradingBuilder.FreezeXmlBacked(cTin);
                // 앞에 만든 면 — 지우고 그 이름을 그대로
                //   ★[설치 전 검토 중간 1] 이름이 우리 꼴(«진입도로_DH» · «진입도로_DH_N»)일 때만 — 사람이 이름을 바꿔 남겨 둔 안(«진입도로_1안»)이나
                //   다른 도면에서 온 기록이 가리키는 남의 면(원지반 등)은 건드리지 않고 새 이름을 붙인다
                string name = "";
                var old = Handle(db, d.SurfaceHandle);
                if (!old.IsNull && old != cid && tr.GetObject(old, OpenMode.ForRead) is CivilDb.TinSurface oldTin
                    && Commands.SectionCommand.IsBase(oldTin.Name, SurfBase))
                {
                    name = oldTin.Name;
                    tr.GetObject(old, OpenMode.ForWrite, false, true).Erase();      // ★[낮음 5] 잠긴 레이어여도
                }
                if (name.Length == 0) name = UniqueSurfaceName(db, tr, SurfBase);
                cTin.Name = name;
                try { cTin.Description = $"DH 진입도로 지표면(도로 + 사면 · 정지면_DH와 따로) — {Name} · {DateTime.Now:yyyy-MM-dd HH:mm} · {GradingSettings.Version}"; } catch { }
                // ★[v110.3 · JACK 1029 «평면 사면 모양 = 2D처럼» · 검토 M8] 평면 = 경계만(사면 모양은 사면선 · 소단선 · 노리선이 보여 줌) · 모형(3D) = 세모
                try { var stId = EnsureRoadSurfStyle(tr, BaseSurfId); if (!stId.IsNull) cTin.StyleId = stId; else { var bs = (CivilDb.TinSurface)tr.GetObject(BaseSurfId, OpenMode.ForRead); cTin.StyleId = bs.StyleId; } } catch { }
                try { cTin.LayerId = Commands.SectionCommand.EnsureLayer(db, tr, RoadBuilder.LaySurf, 3); } catch { }
                d.SurfaceHandle = cid.Handle.ToString();
                d.SurfaceSig = _pendingSig;
                d.SurfaceReach = _pendingReach;
                d.SurfaceSigParts = _pendingParts;
                d.SurfaceWallNos = _pendingWallNos; d.SurfacePath = _pendingPath;   // ★[v115 · 설치 전 검토 R1] 실제로 지은 벽 · 보낸 길(벽 기록 없는 도로 = null)
                d.Write(tr, AlignId);
                tr.Commit();
                made = ObjectId.Null;
                Design = d;
                // ★[v110.3] 실제 선 — 사면선 · 소단선 · 노리선 · 옹벽선(이 도로 것만 지우고 새로 — 검토 M7)
                string lineNote;
                try { lineNote = DrawRoadLines(db, lineR ?? r, lines); } catch (System.Exception exL) { lineNote = "⚠선을 못 그림 — " + exL.Message; }
                // ★[설치 전 검토 낮음 6] 면 끝이 땅에 안 닿은 자리 · 옹벽 자리(가운데 높이)를 알림 · 수량은 이 면의 칸 기준
                //   ★[v116 · 고리 엔진 §2 M5e] 고리 면(첫 토막 r)은 부딪히면 낮은 쪽(절토) 사면을 넣는다 · 길 이름은 토막 길 기록(고리 면이 들면 «고리 면 + 선 면» 꼴) · 칸 0.25 m도 보이게(0.## — 0.5 · 1은 그대로)
                var paths = (_pendingPath ?? "").Split('|');
                bool ring0 = PathAt(_pendingPath, 0) == RingPath, ringAny = paths.Contains(RingPath);
                string msg = $"진입도로 지표면 '{name}' — 절토 {r.CutVol:N0} ㎥ · 성토 {r.FillVol:N0} ㎥({r.Cell:0.##} m 칸) · {sw.ElapsedMilliseconds / 1000.0:F1}초"
                             + (r.ReachHit ? $" · ⚠데이라잇 못 찾음(범위 {r.ReachUsed:F0} m 끝에서 면이 땅에 안 닿음)" : "")
                             + (r.Conflicts > 0 ? $" · ⚠사면 충돌 {r.Conflicts}칸(옹벽 자리 — {(ring0 ? "낮은 쪽 사면을 넣음 · 옹벽 검토" : "가운데 높이로 넣음")})" : "")
                             + (frz.Length > 0 ? " · ⚠굳힘 실패 " + frz : "") + " · " + lineNote
                             + (tnote.StartsWith("⚠") ? " · " + tnote : $" · {(ringAny ? string.Join(" + ", paths.Distinct()) : tnote.StartsWith("선 따라 TIN") ? "선 따라 TIN" : "선 면")} · 세모 {tris.Count:N0}")   // ★[JACK 1104] 어느 길로 지었나(선 면 · 선 따라 TIN · 칸 면 · ★v116 고리 면)
                             + (tnote.Contains("→ 옛 흘러내림 길") ? " · ⚠굽은 안쪽 새 길을 못 써 옛 길로 지음(뭉개짐이 남을 수 있음)" : "")   // ★[v114.3 · 재검토 M-a] 물러섬은 화면에도(note 꼬리만이면 안 보임)
                             + wallNote   // ★[v114.3 · JACK 1105 진입도로 옹벽 추가] 선 옹벽마다 지음 · 못 지음 · 쉼(까닭 한 줄)
                             + _pendingRingNote;   // ★[v116] 고리 면을 못 지어 옛 엔진으로 물러섬(까닭 한 줄)
                try { DiagLog.Append($"\n[진입도로] {Name} — {msg} · {tnote} · 격자 {msGrid} · XML {msXml - msGrid} · Civil 면 + 되읽기 {msMake - msXml} · 대조 {msCmp - msMake} ms · 되읽기 {back}"); } catch { }
                return msg;
            }
        }
        finally
        {
            Suppress = was;
            if (!made.IsNull)
            {
                try { using var trE = db.TransactionManager.StartTransaction(); if (!made.IsErased) trE.GetObject(made, OpenMode.ForWrite, false, true).Erase(); trE.Commit(); }
                catch (System.Exception exE) { try { DiagLog.Append($"\n[진입도로] ⚠작업 면 «{TempSurf}»을 못 지움(다음 누를 때 다시 치움) — {exE.Message}"); } catch { } }
            }
        }
    }

    internal const string LaySlope = "DH도로-사면선", LayBench = "DH도로-소단선", LayNori = "DH도로-노리선", LayWall = "DH도로-옹벽선";
    internal const string LineApp = "DH_ROADLINE";
    internal const string SurfStyleName = "DH-진입도로 경계";

    /// <summary>★★[v110.3 · 계획 4 · 검토 M7] [도로 면 만들기]의 실제 선 — 사면선(윗선) · 소단선(아랫선) · 옹벽선(굵게) · 노리선(짧은 · 긴) · 3D(설계 높이).
    /// 이 도로(노선 핸들 XData)의 것만 먼저 지움 — 한 도면에 도로가 둘이어도 남의 선은 그대로. 못 지우면 안 그림(겹쳐 두 벌이 되지 않게).</summary>
    string DrawRoadLines(Database db, RoadGrade.Result r, List<RoadGrade.TierLine> lines)
    {
        // ★[v111.3 · JACK 1030] 노리선은 여기서 안 그림 — [노리선] 단추(부지 + 모든 도로 한꺼번에 · DrawRoadTicks). 이 도로의 옛 노리선은 아래에서 함께 지움
        string me = AlignId.Handle.ToString();
        var lays = new[] { LaySlope, LayBench, LayNori, LayWall, RoadDitchLines.Layer };
        int erased = 0, failErase = 0, nLine = 0, nWall = 0, nDitch = 0;
        using var tr = db.TransactionManager.StartTransaction();
        GradingBuilder.EnsureRegApp(db, tr, LineApp);
        var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
        var want = new HashSet<string>(lays, StringComparer.OrdinalIgnoreCase);
        var victims = new List<ObjectId>();
        foreach (ObjectId id in ms)
        {
            try
            {
                if (tr.GetObject(id, OpenMode.ForRead) is not Entity e || !want.Contains(e.Layer)) continue;
                using var rb = e.GetXDataForApplication(LineApp);
                var v = rb?.AsArray();
                if (v != null && v.Length >= 2 && v[1].Value as string == me) victims.Add(id);
            }
            catch { }
        }
        foreach (var id in victims) { try { tr.GetObject(id, OpenMode.ForWrite, false, true).Erase(); erased++; } catch { failErase++; } }
        if (failErase > 0) { tr.Abort(); return $"⚠옛 선 {failErase}개를 못 지워 선을 안 그림(레이어 잠금 확인)"; }
        ObjectId lS = Commands.SectionCommand.EnsureLayer(db, tr, LaySlope, 8), lB = Commands.SectionCommand.EnsureLayer(db, tr, LayBench, 8),
                 lW = Commands.SectionCommand.EnsureLayer(db, tr, LayWall, 1);
        ResultBuffer Tag() => new(new TypedValue((int)DxfCode.ExtendedDataRegAppName, LineApp), new TypedValue((int)DxfCode.ExtendedDataAsciiString, me));
        foreach (var tl in lines)
        {
            // ★[설치 전 검토 L5] 0.5 m 등치선 점을 1 cm 허용으로 줄임(꼭짓점 객체 수)
            var pc = new Point3dCollection();
            foreach (var p in Simplify(tl.Pts.Where(q => double.IsFinite(q.Z)).Select(q => new Point3d(q.X, q.Y, q.Z)).ToList(), 0.01)) pc.Add(p);
            if (pc.Count < 2) continue;
            var pl = new Polyline3d(Poly3dType.SimplePoly, pc, false);
            bool wall = tl.Role == RoadGrade.LineRole.Wall;
            pl.LayerId = wall ? lW : tl.Role == RoadGrade.LineRole.Crest ? lS : lB;
            if (wall) pl.LineWeight = LineWeight.LineWeight050;
            ms.AppendEntity(pl); tr.AddNewlyCreatedDBObject(pl, true);
            using (var tg = Tag()) pl.XData = tg;
            if (wall) nWall++; else nLine++;
        }
        // ★★[도로 마무리 ② · 계획 6.4] 측구 가운데 선(«DH도로-측구선» · 하늘) — 형식은 선의 기록(XData)
        try
        {
            var dls = RoadDitchLines.Lines(tr, AlignId, PlanId, Design, Ranges);
            if (dls.Count > 0)
            {
                ObjectId lD = Commands.SectionCommand.EnsureLayer(db, tr, RoadDitchLines.Layer, RoadDitchLines.Aci);
                foreach (var dl in dls)
                {
                    var pc = new Point3dCollection(); foreach (var p in dl.Pts) pc.Add(p);
                    var pl = new Polyline3d(Poly3dType.SimplePoly, pc, false) { LayerId = lD };
                    ms.AppendEntity(pl); tr.AddNewlyCreatedDBObject(pl, true);
                    using (var tg = new ResultBuffer(new TypedValue((int)DxfCode.ExtendedDataRegAppName, LineApp), new TypedValue((int)DxfCode.ExtendedDataAsciiString, me),
                                                    new TypedValue((int)DxfCode.ExtendedDataAsciiString, "측구 " + dl.Shape.Label))) pl.XData = tg;
                    nDitch++;
                }
            }
        }
        catch (System.Exception exD) { try { DiagLog.Append($"\n[진입도로] {Name} — ⚠측구선 못 그림 — {exD.Message}"); } catch { } }
        tr.Commit();
        return $"사면선 · 소단선 {nLine} · 옹벽선 {nWall}" + (nDitch > 0 ? $" · 측구선 {nDitch}" : "") + (erased > 0 ? $"(옛 선 {erased} 지움)" : "");
    }

    internal const string SiteNoriLayer = "DH-노리선";

    /// <summary>★[v111.3] 도면에 살아 있는 우리 진입도로 면 수(설계 + 레이어 · 설명) — [노리선]이 부지 결과가 없어도 도로 노리선은 그리게.</summary>
    internal static int CountRoadSurfaces(Database db)
    {
        int n = 0;
        try
        {
            var cdoc = Autodesk.Civil.ApplicationServices.CivilDocument.GetCivilDocument(db);
            using var tr = db.TransactionManager.StartTransaction();
            foreach (ObjectId aid in cdoc.GetAlignmentIds())
            {
                try
                {
                    if (aid.IsErased || !RoadDesign.Has(tr, aid)) continue;
                    var d = RoadDesign.Read(tr, aid);
                    var sid = d == null ? ObjectId.Null : Handle(db, d.SurfaceHandle);
                    if (!sid.IsNull && tr.GetObject(sid, OpenMode.ForRead) is CivilDb.TinSurface ts
                        && (string.Equals(ts.Layer, RoadBuilder.LaySurf, StringComparison.OrdinalIgnoreCase) || (ts.Description ?? "").StartsWith("DH 진입도로 지표면"))) n++;
                }
                catch { }
            }
            tr.Commit();
        }
        catch { }
        return n;
    }

    /// <summary>★★[v111.3 · JACK 1030 인터뷰 «부지 · 도로 모두 지움»] 그려 둔 노리선을 모두 지운다 — 부지(DH-노리선) · 모든 진입도로(DH도로-노리선). 모형 공간만 · 잠긴 레이어도.
    /// 돌려줌 = 지운 수 · <paramref name="failed"/> = 못 지운 수.</summary>
    internal static int EraseAllTicks(Database db, out int failed)
    {
        failed = 0; int n = 0;
        using var tr = db.TransactionManager.StartTransaction();
        var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
        foreach (ObjectId id in ms)
        {
            Entity? e = null;
            try { e = tr.GetObject(id, OpenMode.ForRead) as Entity; } catch { }   // ★[설치 전 검토 L6] 읽기 실패(남의 객체)는 «못 지움»으로 세지 않음
            if (e == null || (!string.Equals(e.Layer, LayNori, StringComparison.OrdinalIgnoreCase) && !string.Equals(e.Layer, SiteNoriLayer, StringComparison.OrdinalIgnoreCase))) continue;
            try { tr.GetObject(id, OpenMode.ForWrite, false, true).Erase(); n++; }
            catch { failed++; }
        }
        tr.Commit();
        return n;
    }

    /// <summary>★★[v111.3 · JACK 1030 «노리선은 노리선 버튼을 누를 때 다 한꺼번에»] [노리선]이 부른다 — 도면의 진입도로마다(설계 + 살아 있는 우리 도로 면)
    /// 그 면을 만든 입력(설계 · 바탕면 · 시작 범위)으로 다시 계산 → 단면 광선 노리선 → 그 도로의 옛 노리선을 지우고 새로(DH도로-노리선 · 도로 표식).
    /// 설계가 면과 다르면(지문이 다름) 그 도로는 건너뛰고 알림(면과 안 맞는 노리선을 그리지 않게). 돌려줌 = 한 줄 · <paramref name="drawn"/> = 그린 수 · <paramref name="roads"/> = 그린 도로 수.</summary>
    /// <summary>★★[v116.5 · JACK 1110 «소단 표시 누락»(3D 보기)] 고리 면 도로의 소단 자리 — 면 높이 규칙(E2)에서(<see cref="RoadRing.BenchArea"/> · 미리보기 1 m 격자로 고른 뒤 0.25 m).
    /// 면과 같은 입력(<see cref="DrawRoadTicks"/>와 같은 토막 · 칸 · 시작 범위)이고 지문이 지금 설계와 같고 모든 토막이 고리 면일 때만 — 아니면 null(부르는 쪽이 옛 계단선 짝).
    /// 계단선 짝은 두 다리 사면이 만나는 안쪽에서 «가장 가까운 둘레» 거리 고리를 따라 꺾여 실제 소단 턱을 놓쳤다(JACK 도로 셈 소단 270 ㎡ 중 32 ㎡).</summary>
    internal static NetTopologySuite.Geometries.Geometry? RingBenchArea(Database db, ObjectId aid, RoadDesign d, Func<double, double, bool> onSurf, out string note)
    {
        note = "";
        var cdoc = Autodesk.Civil.ApplicationServices.CivilDocument.GetCivilDocument(db);
        if (d.SurfaceSig.Length == 0) { note = "옛 판 면"; return null; }
        var planId = Handle(db, d.PlanProfile); var baseId = FindSurface(db, cdoc, d.BaseSurface);
        if (planId.IsNull || baseId.IsNull) { note = "계획 종단 · 바탕면 없음"; return null; }
        List<Piece> pcs;
        using (var tr = db.TransactionManager.StartTransaction())
        {
            var ground = new CachedGroundSurface((CivilDb.TinSurface)tr.GetObject(baseId, OpenMode.ForRead));
            var gB = BranchBase.For(tr, db, aid, d, ground, null, out _);
            pcs = MakePieces(tr, aid, planId, d, gB, RoadRanges.Of(tr, aid, d));
            tr.Commit();
        }
        foreach (var pc in pcs) { pc.Inp.Cell = SurfCell; pc.Inp.Reach = d.SurfaceReach > 0 ? d.SurfaceReach : 40; pc.Inp.Limit = null; }
        if (pcs.Count == 0) { note = "토막 없음"; return null; }
        if (SigOfAll(pcs, baseId) != d.SurfaceSig) { note = "면이 지금 설계와 다름"; return null; }
        for (int k = 0; k < pcs.Count; k++) if (PathAt(d.SurfacePath, k) != RingPath) { note = "옛 엔진 토막"; return null; }
        var gf = NtsSupport.Factory();
        var parts = new List<NetTopologySuite.Geometries.Geometry>(); var texts = new List<string>();
        foreach (var pc in pcs)
        {
            var pv = RingRun(pc.Inp, false, out _);
            if (!pv.Ok) { note = "고리 미리보기 못 함 — " + pv.Note; return null; }
            var ba = RoadRing.BenchArea(pv, onSurf);
            texts.Add(ba.Text);
            if (ba.Area != null && !ba.Area.IsEmpty) parts.Add(ba.Area);
        }
        note = string.Join(" · ", texts);
        var area = parts.Count == 0 ? gf.CreateGeometryCollection() : parts.Count == 1 ? parts[0] : NetTopologySuite.Operation.Union.CascadedPolygonUnion.Union(parts);
        // ★[v116.7 · wall2 검토 L5] 선 옹벽을 지은 자리(벽 Poly = 바꿀 자리 ∪ R_out)에는 옛 소단이 없다 — 3D 소단 띠에서 뺀다.
        //   지난 [업데이트] 벽(지문 같음)을 쓰고 · 없으면 면과 같은 입력으로 다시 얹음(면에 실제로 지은 벽만 — 기록 SurfaceWallNos)
        if (pcs.Count == 1 && pcs[0].Inp.LineWalls.Count > 0 && !area.IsEmpty && (d.SurfaceWallNos == null || d.SurfaceWallNos.Length > 0))
        {
            try
            {
                RoadGrade.WallSurface? ws = null;
                var doc = AcadApp.DocumentManager.GetDocument(db);
                var sess = doc != null ? Of(doc) : null;
                if (sess != null && sess.AlignId == aid && sess.Walls != null && sess.WallsSig.Length > 0 && sess.WallsSig == d.SurfaceSig) ws = sess.Walls;
                else
                {
                    var bF = RingRun(pcs[0].Inp, true, out _);
                    if (bF.Ok && bF.Tris.Count > 0)
                    {
                        var rrF = RoadRing.ToResult(bF, pcs[0].Inp);
                        ISet<int>? restF = d.SurfaceWallNos == null ? null : new HashSet<int>(pcs[0].Inp.LineWalls.Select(w => w.No).Where(no => !d.SurfaceWallNos.Contains(no)));
                        ws = RoadGrade.ApplyWalls(pcs[0].Inp, rrF, bF.Tris, bF.Lines, RingPath, pcs[0].Inp.LineWalls, WallBlockers(db), restF, ring: bF);
                    }
                }
                if (ws != null && ws.Any)
                {
                    var wp = new List<NetTopologySuite.Geometries.Geometry>();
                    foreach (var w in ws.Built)
                        if (w.Built?.Poly is { Count: >= 3 } pl)
                        {
                            var g = NtsSupport.ToCleanPolygon(pl.ToList(), gf);
                            if (g != null && !g.IsEmpty) wp.Add(g);
                        }
                    if (wp.Count > 0)
                    {
                        double a0 = area.Area;
                        area = area.Difference(NetTopologySuite.Operation.Union.CascadedPolygonUnion.Union(wp));
                        note += string.Format(System.Globalization.CultureInfo.InvariantCulture, " · 선 옹벽 자리 뺌 {0:F1} ㎡", a0 - area.Area);
                    }
                }
            }
            catch (System.Exception exW) { note += " · ⚠선 옹벽 자리를 못 뺌(" + exW.Message + ")"; }
        }
        return area;
    }

    internal static string DrawRoadTicks(Document doc, out int drawn, out int roads)
    {
        drawn = 0; roads = 0;
        var db = doc.Database;
        var cdoc = Autodesk.Civil.ApplicationServices.CivilDocument.GetCivilDocument(db);
        var notes = new List<string>();
        var aids = new List<ObjectId>();
        using (var tr = db.TransactionManager.StartTransaction())
        {
            foreach (ObjectId aid in cdoc.GetAlignmentIds()) { try { if (!aid.IsErased && RoadDesign.Has(tr, aid)) aids.Add(aid); } catch { } }
            tr.Commit();
        }
        foreach (var aid in aids)
        {
            string name = "";
            try
            {
                RoadDesign? d; bool ours;
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    d = RoadDesign.Read(tr, aid);
                    name = ((CivilDb.Alignment)tr.GetObject(aid, OpenMode.ForRead)).Name;
                    var sid = d == null ? ObjectId.Null : Handle(db, d.SurfaceHandle);
                    ours = !sid.IsNull && tr.GetObject(sid, OpenMode.ForRead) is CivilDb.TinSurface ts
                           && (string.Equals(ts.Layer, RoadBuilder.LaySurf, StringComparison.OrdinalIgnoreCase) || (ts.Description ?? "").StartsWith("DH 진입도로 지표면"));
                    tr.Commit();
                }
                if (d == null || !ours)
                {
                    // 도로 면이 없는 도로 — 노리선 없음 · ★[설치 전 검토 L6] 면을 손으로 지웠으면 남은 그 도로 노리선도 지움
                    using var trO = db.TransactionManager.StartTransaction();
                    var msO = (BlockTableRecord)trO.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
                    string meO = aid.Handle.ToString();
                    foreach (ObjectId id in msO)
                    {
                        try
                        {
                            if (trO.GetObject(id, OpenMode.ForRead) is not Entity eo || !string.Equals(eo.Layer, LayNori, StringComparison.OrdinalIgnoreCase)) continue;
                            using var rbo = eo.GetXDataForApplication(LineApp);
                            var vo = rbo?.AsArray();
                            if (vo != null && vo.Length >= 2 && vo[1].Value as string == meO) trO.GetObject(id, OpenMode.ForWrite, false, true).Erase();
                        }
                        catch { }
                    }
                    trO.Commit();
                    continue;
                }
                var planId = Handle(db, d.PlanProfile);
                var baseId = FindSurface(db, cdoc, d.BaseSurface);
                if (planId.IsNull || baseId.IsNull) { notes.Add($"{name} — ⚠계획 종단 · 바탕면을 못 찾아 건너뜀"); continue; }
                CachedGroundSurface ground; List<Piece> pcs; List<(double, double)>? limitT;
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    ground = new CachedGroundSurface((CivilDb.TinSurface)tr.GetObject(baseId, OpenMode.ForRead));
                    var gB = BranchBase.For(tr, db, aid, d, ground, null, out string gNote);   // ★[계획 5.5] 가지면 바탕면 + 본 도로 면(면을 지은 바탕과 같게 — 지문이 맞음)
                    if (gNote.StartsWith("⚠")) notes.Add($"{name} — {gNote}");
                    pcs = MakePieces(tr, aid, planId, d, gB, RoadRanges.Of(tr, aid, d));   // ★[도로 마무리 ① · 계획 5.8] 도로 구간 · 토막
                    limitT = null;   // ★[v116.4 · JACK 1109] 옹벽 제안 없앰 — 사업 경계 안 읽음
                    tr.Commit();
                }
                // MakeSurfaceCore와 같은 입력(칸 0.5 · 해상도 · 시작 범위 · ★[v115 · R1] 사업 경계 Limit도 — 면과 같은 입력)
                foreach (var pc in pcs) { pc.Inp.Cell = SurfCell; pc.Inp.Reach = d.SurfaceReach > 0 ? d.SurfaceReach : 40; pc.Inp.Limit = limitT; }
                // 지문이 없으면(v111.2.1 전에 만든 면) 그 면과 맞는지 알 길이 없음 — 말없이 틀린 노리선을 그리지 않고 건너뜀
                if (d.SurfaceSig.Length == 0) { notes.Add($"{name} — ⚠옛 판에서 만든 진입도로 지표면이라 건너뜀(진입도로 창 [업데이트]로 한 번 다시 만든 뒤 [노리선])"); continue; }
                if (SigOfAll(pcs, baseId) != d.SurfaceSig)
                {
                    bool engineOnly = EngineOnly(pcs, baseId, d.SurfaceSig);   // ★[v115 · 검토 M1] 엔진 판만 다름 — 같은 말로 · ★[v116] 앞 판(v115) 면도
                    notes.Add(engineOnly ? $"{name} — ⚠{EngineNote} 뒤 [노리선]" : $"{name} — ⚠진입도로 지표면이 지금 설계와 달라 건너뜀(진입도로 창 [업데이트] 뒤 [노리선])");
                    continue;
                }
                var ticks = new List<(Point3 A, Point3 B)>(); string tickErr = "";
                var sess = Of(doc);
                for (int k = 0; k < pcs.Count; k++)
                {
                    var pc = pcs[k];
                    // ★★★[v116 · 고리 엔진 계획 3판 §4] 고리 면으로 지은 토막(길 기록 SurfacePath) — 고리 노리선(옛 Ticks는 단면 광선이 있어야 함).
                    //   지난 [업데이트] 꾸러미가 지금 면 지문과 같으면 그것을 · 아니면 면과 같은 입력(칸 · 시작 범위 · 사업 경계)으로 다시 지음
                    if (PathAt(d.SurfacePath, k) == RingPath)
                    {
                        RoadRing.Built? rb = sess != null && sess.AlignId == aid && sess.RingCache is { } rc && rc.Sig == d.SurfaceSig && rc.B.Length == pcs.Count ? rc.B[k] : null;
                        if (rb == null)
                        {
                            try { rb = RingRun(pc.Inp, true, out _); }
                            catch (System.Exception exR) { tickErr = "고리 면 예외 — " + exR.Message; break; }
                            if (!rb.Ok) { tickErr = rb.Note; break; }
                        }
                        var tkR = RoadRing.Ticks(rb, GradingSettings.HatchShort, GradingSettings.HatchLong);
                        // ★[v116.7] 선 옹벽 든 고리 면 — 면에 실제로 지은 벽(기록 SurfaceWallNos) 자리 빗금을 뺀다(옛 엔진 길과 같은 법 · 지난 [업데이트] 벽이 지문 같으면 그것)
                        bool noWallsR = d.SurfaceWallNos != null && d.SurfaceWallNos.Length == 0;
                        if (pc.Inp.LineWalls.Count > 0 && pcs.Count == 1 && !noWallsR)
                        {
                            try
                            {
                                RoadGrade.WallSurface? wsR = sess != null && sess.AlignId == aid && sess.Walls != null && sess.WallsSig.Length > 0 && sess.WallsSig == d.SurfaceSig ? sess.Walls : null;
                                if (wsR == null)
                                {
                                    var rbF = rb.Grid != null && rb.Tris.Count > 0 ? rb : RingRun(pc.Inp, true, out _);
                                    if (rbF.Ok && rbF.Tris.Count > 0)
                                    {
                                        var rrF = RoadRing.ToResult(rbF, pc.Inp);
                                        ISet<int>? restR = d.SurfaceWallNos == null ? null : new HashSet<int>(pc.Inp.LineWalls.Select(w => w.No).Where(no => !d.SurfaceWallNos.Contains(no)));
                                        if (d.SurfaceWallNos == null) notes.Add($"{name} — ⚠옛 면이라 지은 옹벽 기록이 없음 — 모든 추가 옹벽 자리 빗금을 뺌([업데이트]로 한 번 다시 만들면 맞음)");
                                        wsR = RoadGrade.ApplyWalls(pc.Inp, rrF, rbF.Tris, rbF.Lines, RingPath, pc.Inp.LineWalls, WallBlockers(db), restR, ring: rbF);
                                    }
                                    else notes.Add($"{name} — ⚠고리 면을 다시 못 지어 선 옹벽 자리 빗금을 못 뺌({rbF.Note})");
                                }
                                if (wsR != null && wsR.Any) tkR = RoadGrade.CutTicks(tkR, wsR);
                            }
                            catch (System.Exception exT) { notes.Add($"{name} — ⚠선 옹벽 자리 빗금을 못 뺌({exT.Message})"); }
                        }
                        ticks.AddRange(tkR);
                        continue;
                    }
                    var r = RoadGrade.Run(pc.Inp);
                    if (r.Error.Length > 0) { tickErr = r.Error; break; }
                    var lines = RoadGrade.ExactLines(pc.Inp, r, out _);
                    var tk = RoadGrade.Ticks(r, lines, GradingSettings.HatchShort, GradingSettings.HatchLong);
                    // ★[v114.3 · JACK 1105 진입도로 옹벽 추가] 선 옹벽이 있으면 면과 같은 길로 다시 얹어(같은 지문 · 같은 입력) 그 자리 빗금을 뺀다
                    //   ★[v115 · 설치 전 검토 R1] 면에 실제로 지은 벽만 · 실제로 보낸 길로(기록 SurfaceWallNos · SurfacePath) — Civil 거절로 벽을 쉬었거나 길을 내린 면에
                    //   옛 판은 ExactSurface 길로 모든 벽을 다시 지어 없는 벽 자리 빗금을 뺐다. 기록이 없는 옛 면은 옛 판처럼(⚠)
                    string? pathK = PathAt(d.SurfacePath, k);   // ★[v116] 토막 길(옛 기록 = 길 하나)
                    bool noWalls = d.SurfaceWallNos != null && (d.SurfaceWallNos.Length == 0 || pathK == "칸 면");
                    if (pc.Inp.LineWalls.Count > 0 && pcs.Count == 1 && !noWalls)
                    {
                        try
                        {
                            var p0 = RoadGrade.ExactSurface(pc.Inp, r, out string tn, out var l0);
                            string path = PathOf(tn);
                            if (pathK == "선 따라 TIN" && path == "선 면")
                            {
                                var btT = RoadGrade.BreakTinSurface(pc.Inp, r, out string bnT, l0);
                                if (btT != null && btT.Count > 0) { p0 = btT; path = "선 따라 TIN"; }
                                else notes.Add($"{name} — ⚠면을 지은 길(선 따라 TIN)을 다시 못 지음({bnT}) — 선 면 위 벽으로 빗금을 뺌");
                            }
                            ISet<int>? restT = d.SurfaceWallNos == null ? null : new HashSet<int>(pc.Inp.LineWalls.Select(w => w.No).Where(no => !d.SurfaceWallNos.Contains(no)));
                            if (d.SurfaceWallNos == null) notes.Add($"{name} — ⚠옛 면이라 지은 옹벽 기록이 없음 — 모든 추가 옹벽 자리 빗금을 뺌([업데이트]로 한 번 다시 만들면 맞음)");
                            var ws = RoadGrade.ApplyWalls(pc.Inp, r, p0, l0, path, pc.Inp.LineWalls, WallBlockers(db), restT);
                            if (ws.Any) tk = RoadGrade.CutTicks(tk, ws);
                        }
                        catch (System.Exception exT) { notes.Add($"{name} — ⚠선 옹벽 자리 빗금을 못 뺌({exT.Message})"); }
                    }
                    ticks.AddRange(tk);
                }
                if (tickErr.Length > 0) { notes.Add($"{name} — ⚠계산 실패 {tickErr}"); continue; }
                int n = 0;
                string me = aid.Handle.ToString();
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    GradingBuilder.EnsureRegApp(db, tr, LineApp);
                    var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
                    foreach (ObjectId id in ms)
                    {
                        try
                        {
                            if (tr.GetObject(id, OpenMode.ForRead) is not Entity e || !string.Equals(e.Layer, LayNori, StringComparison.OrdinalIgnoreCase)) continue;
                            using var rb = e.GetXDataForApplication(LineApp);
                            var v = rb?.AsArray();
                            if (v != null && v.Length >= 2 && v[1].Value as string == me) tr.GetObject(id, OpenMode.ForWrite, false, true).Erase();
                        }
                        catch { }
                    }
                    var lN = Commands.SectionCommand.EnsureLayer(db, tr, LayNori, 2);
                    foreach (var (a, b) in ticks)
                    {
                        if (!double.IsFinite(a.Z) || !double.IsFinite(b.Z)) continue;
                        var ln = new Line(new Point3d(a.X, a.Y, a.Z), new Point3d(b.X, b.Y, b.Z)) { LayerId = lN };
                        ms.AppendEntity(ln); tr.AddNewlyCreatedDBObject(ln, true);
                        using (var tg = new ResultBuffer(new TypedValue((int)DxfCode.ExtendedDataRegAppName, LineApp), new TypedValue((int)DxfCode.ExtendedDataAsciiString, me))) ln.XData = tg;
                        n++;
                    }
                    tr.Commit();
                }
                drawn += n; roads++;
            }
            catch (Exception ex) { notes.Add($"{name} — ⚠노리선 못 그림 {ex.Message}"); try { DiagLog.Append($"\n[진입도로 노리선] {name} — {ex}"); } catch { } }
        }
        string msg = roads > 0 ? $"진입도로 노리선 {drawn}(도로 {roads}개)" : "";
        if (notes.Count > 0) msg += (msg.Length > 0 ? " · " : "") + string.Join(" · ", notes);
        return msg;
    }

    /// <summary>★[v111.2] 이 도로의 [도로 면 만들기] 면이 살아 있나(설계에 적힌 핸들 · 우리 면 — 레이어 DH도로-지표면 또는 설명 «DH 진입도로 지표면»).</summary>
    internal bool HasSurface()
    {
        try
        {
            var db = _doc.Database;
            var sid = Handle(db, Fresh().SurfaceHandle);
            if (sid.IsNull) return false;
            using var tr = db.TransactionManager.StartTransaction();
            bool ok = tr.GetObject(sid, OpenMode.ForRead) is CivilDb.TinSurface ts
                      && (string.Equals(ts.Layer, RoadBuilder.LaySurf, StringComparison.OrdinalIgnoreCase) || (ts.Description ?? "").StartsWith("DH 진입도로 지표면"));
            tr.Commit();
            return ok;
        }
        catch { return false; }
    }

    /// <summary>★★[v111.1 · 계획_v111_1.md 2.1 · JACK 1030 «합쳐진 노리선»] 도면의 진입도로 면 자리(발자국) — 진입도로 설계마다 기록된 면(<see cref="RoadDesign.SurfaceHandle"/>)이 살아 있으면 그 세모로.
    /// 이름을 바꿔 남겨 둔 안 · 미리보기는 안 본다(지금 도로 면만). 없으면 null.</summary>
    internal static NetTopologySuite.Geometries.Geometry? Footprints(Database db, Transaction tr, out int nRoads, out string note)
    {
        nRoads = 0; note = "";
        var cdoc = Autodesk.Civil.ApplicationServices.CivilDocument.GetCivilDocument(db);
        var geoms = new List<NetTopologySuite.Geometries.Geometry>();
        foreach (ObjectId aid in cdoc.GetAlignmentIds())
        {
            if (aid.IsErased || !RoadDesign.Has(tr, aid)) continue;
            var d = RoadDesign.Read(tr, aid);
            if (d == null) continue;
            var sid = Handle(db, d.SurfaceHandle);
            if (sid.IsNull || tr.GetObject(sid, OpenMode.ForRead) is not CivilDb.TinSurface ts) continue;
            // ★[설치 전 검토 M1] 우리 도로 면일 때만(레이어 · 설명) — 다른 도면에서 온 기록이 정지면_DH · 원지반을 가리키면 부지 선이 다 지워진다(CreateSurface 이름 확인과 같은 뜻)
            string desc = ""; try { desc = ts.Description ?? ""; } catch { }
            if (!string.Equals(ts.Layer, RoadBuilder.LaySurf, StringComparison.OrdinalIgnoreCase) && !desc.StartsWith("DH 진입도로 지표면"))
            {
                note += (note.Length > 0 ? " · " : "") + $"«{ts.Name}» 진입도로 면이 아니라 건너뜀";
                continue;
            }
            var g = RoadFootprint.FromTris(PureWithWallsBuilder.ReadTris(ts, out _), out string fn);
            if (fn.Length > 0) note += (note.Length > 0 ? " · " : "") + $"«{ts.Name}» {fn}";
            if (g != null) { geoms.Add(g); nRoads++; }
        }
        if (geoms.Count == 0) return null;
        return geoms.Count == 1 ? geoms[0] : NetTopologySuite.Operation.Union.CascadedPolygonUnion.Union(geoms);
    }

    /// <summary>★[v110.3 · 검토 M8] 진입도로 면 스타일 — 평면 = 경계만(없으면 만들고 · 평면은 사람이 고친 값을 안 덮음) · ★[v111.2] 모형(3D)은 면을 만들 때마다 바탕면 스타일로 맞춤(사람이 고친 3D 값은 덮임).</summary>
    internal static ObjectId EnsureRoadSurfStyle(Transaction tr, ObjectId baseSurfId)
    {
        var cdoc = Autodesk.Civil.ApplicationServices.CivilApplication.ActiveDocument;
        var styles = cdoc.Styles.SurfaceStyles;
        ObjectId id = ObjectId.Null;
        foreach (ObjectId sid in styles)
        {
            try { if (tr.GetObject(sid, OpenMode.ForRead) is CivilDb.Styles.SurfaceStyle s0 && string.Equals(s0.Name, SurfStyleName, StringComparison.OrdinalIgnoreCase)) { id = sid; break; } }
            catch { }
        }
        if (id.IsNull)
        {
            id = styles.Add(SurfStyleName);
            if (id.IsNull) return ObjectId.Null;
            var st = (CivilDb.Styles.SurfaceStyle)tr.GetObject(id, OpenMode.ForWrite);
            foreach (CivilDb.Styles.SurfaceDisplayStyleType t in Enum.GetValues(typeof(CivilDb.Styles.SurfaceDisplayStyleType)))
            {
                bool plan = t == CivilDb.Styles.SurfaceDisplayStyleType.Boundary;
                bool model = t == CivilDb.Styles.SurfaceDisplayStyleType.Boundary || t == CivilDb.Styles.SurfaceDisplayStyleType.Triangles;
                try { st.GetDisplayStylePlan(t).Visible = plan; } catch { }
                try { st.GetDisplayStyleModel(t).Visible = model; } catch { }
            }
        }
        // ★★[v111.2 · JACK 1030 «3D 화면으로 볼 때 진입도로면만 하늘색 선이 … 가시성이 떨어져»] 모형(3D) 표시는 바탕면(정지면_DH · 원지반) 스타일과 같게 — 켜고 끔 · 색 · 등고선 간격.
        //   평면은 그대로(경계만 — 사면 모양은 사면선 · 소단선 · 노리선). 매번 맞춘다(바탕 스타일을 바꾸면 따라감).
        try
        {
            if (!baseSurfId.IsNull && !baseSurfId.IsErased && tr.GetObject(baseSurfId, OpenMode.ForRead) is CivilDb.Surface bsf
                && tr.GetObject(bsf.StyleId, OpenMode.ForRead) is CivilDb.Styles.SurfaceStyle bst && bsf.StyleId != id)
            {
                var st = (CivilDb.Styles.SurfaceStyle)tr.GetObject(id, OpenMode.ForWrite);
                foreach (CivilDb.Styles.SurfaceDisplayStyleType t in Enum.GetValues(typeof(CivilDb.Styles.SurfaceDisplayStyleType)))
                {
                    try
                    {
                        var src = bst.GetDisplayStyleModel(t); var dst = st.GetDisplayStyleModel(t);
                        dst.Visible = src.Visible; dst.Color = src.Color;
                        try { dst.Lineweight = src.Lineweight; } catch { }
                        try { dst.Layer = src.Layer; } catch { }        // ★[검토 M1] 도면층 색(ByLayer)으로 그리는 바탕 스타일도 같은 색
                        try { dst.Linetype = src.Linetype; } catch { }
                    }
                    catch { }
                }
                var sc = bst.ContourStyle; var cc = st.ContourStyle;
                try { cc.MinorContourInterval = sc.MinorContourInterval; cc.MajorContourInterval = sc.MajorContourInterval; }
                catch { try { cc.MajorContourInterval = sc.MajorContourInterval; cc.MinorContourInterval = sc.MinorContourInterval; } catch { } }
                try { cc.BaseElevationInterval = sc.BaseElevationInterval; } catch { }
            }
        }
        catch { }
        return id;
    }

    /// <summary>같은 이름이 없게(지운 면은 건너뜀) — «진입도로_DH» · «진입도로_DH_2» …</summary>
    static string UniqueSurfaceName(Database db, Transaction tr, string baseName)
    {
        var have = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var cdoc = Autodesk.Civil.ApplicationServices.CivilDocument.GetCivilDocument(db);
        foreach (ObjectId id in cdoc.GetSurfaceIds())
        {
            if (id.IsNull || id.IsErased) continue;
            try { if (tr.GetObject(id, OpenMode.ForRead) is CivilDb.Surface s) have.Add(s.Name); } catch { }
        }
        if (!have.Contains(baseName)) return baseName;
        for (int i = 2; ; i++) { string c = $"{baseName}_{i}"; if (!have.Contains(c)) return c; }
    }

    /// <summary>사업 경계 밖 사면 길이(m) — 쪽마다 측점이 이어진(1 m 안) 표본 구간의 길이 합.</summary>
    internal static double OutOfLimitLength(RoadGrade.Result r)
    {
        double len = 0;
        foreach (int side in new[] { -1, 1 })
        {
            var ss = r.Slopes.Where(q => q.Side == side && q.OutOfLimit).Select(q => q.S).OrderBy(v => v).ToList();
            for (int i = 1; i < ss.Count; i++) { double ds = ss[i] - ss[i - 1]; if (ds <= 1.0 + 1e-9) len += ds; }
        }
        return len;
    }

    /// <summary>★[v116.4 · JACK 1109 «옹벽 제안 없앰»] 사업 경계(제안용)는 더 안 읽는다 — 옛 기록 LimitHandle은 남겨 두되 늘 null.</summary>
    List<(double, double)>? ReadLimit(Transaction tr) => null;

    /// <summary>★[v115 · 설치 전 검토 R1] 사업 경계 폴리선(핸들) — [노리선]이 도면의 다른 도로에도 면과 같은 입력(Limit 포함)을 주려고 정적으로.</summary>
    internal static List<(double, double)>? ReadLimitOf(Transaction tr, Database db, string handle)
    {
        var id = Handle(db, handle);
        if (id.IsNull) return null;
        try
        {
            if (tr.GetObject(id, OpenMode.ForRead) is Autodesk.AutoCAD.DatabaseServices.Polyline pl && pl.NumberOfVertices >= 3)
            {
                var l = new List<(double, double)>();
                for (int i = 0; i < pl.NumberOfVertices; i++) { var p = pl.GetPoint2dAt(i); l.Add((p.X, p.Y)); }
                return l;
            }
        }
        catch { }
        return null;
    }

    // ── 미리보기(임시 선) ──
    /// <summary>★★★[설치 전 검토 · 높음] 걷기 — PickMark 규칙: 지금 보는 도면이 이 세션의 도면일 때만 걷고, <b>걷힌 것만</b> 해제한다.
    /// 다른 도면이거나 못 걷었으면 소멸자를 끄고 붙잡는다(관리 객체 몇 개가 새는 것이 AutoCAD가 죽는 것보다 낫다).</summary>
    void ClearMarks()
    {
        if (_marks.Count == 0) return;
        bool mine;
        try { mine = ReferenceEquals(AcadApp.DocumentManager.MdiActiveDocument, _doc); } catch { mine = false; }
        if (!mine) { foreach (var m in _marks) Hold(_doc, m); _marks.Clear(); return; }
        try
        {
            var tm = TransientManager.CurrentTransientManager;
            var noIds = new IntegerCollection();
            foreach (var m in _marks)
            {
                bool erased = false;
                try { erased = tm.EraseTransient(m, noIds); } catch { erased = false; }
                if (erased) { try { m.Dispose(); } catch { } }
                else Hold(_doc, m);
            }
        }
        catch { foreach (var m in _marks) Hold(_doc, m); }
        _marks.Clear();
    }

    /// <summary>꺾은선을 덜 꺾이게(더글라스-포이커 · 3D) — 임시 선 개수를 줄인다(1 m 격자 윤곽 · 0.5 m 가장자리 표본).</summary>
    static List<Point3d> Simplify(List<Point3d> p, double tol)
    {
        if (p.Count <= 2) return p;
        var keep = new bool[p.Count]; keep[0] = keep[^1] = true;
        var st = new Stack<(int A, int B)>(); st.Push((0, p.Count - 1));
        while (st.Count > 0)
        {
            var (a, b) = st.Pop();
            if (b <= a + 1) continue;
            var ab = p[b] - p[a]; double l2 = ab.DotProduct(ab);
            double best = -1; int bi = -1;
            for (int i = a + 1; i < b; i++)
            {
                var ap = p[i] - p[a];
                double t = l2 > 1e-12 ? Math.Clamp(ap.DotProduct(ab) / l2, 0, 1) : 0;
                double d = (ap - ab * t).Length;
                if (d > best) { best = d; bi = i; }
            }
            if (best > tol) { keep[bi] = true; st.Push((a, bi)); st.Push((bi, b)); }
        }
        var o = new List<Point3d>();
        for (int i = 0; i < p.Count; i++) if (keep[i]) o.Add(p[i]);
        return o;
    }

    /// <summary>★[v110.3.1 · JACK 1029 «계획지표면때 기능을 답습»] [옹벽 변환] · [사면 변환]이 고르는 동안 — 미리보기에서 계단선 · 노리선을 빼서
    /// 고를 선(시안 · 도면에 잠깐 그린 실제 선)이 회색 임시 선에 가려지지 않게. 끝나면 끄고 다시 그림.</summary>
    internal bool PickMode { get; private set; }
    internal void SetPickMode(bool on)
    {
        if (PickMode == on) return;
        PickMode = on;
        try { DrawPreview(); } catch { }
    }

    void DrawPreview()
    {
        ClearMarks();
        if (Last == null || Last.Error.Length > 0) return;
        bool full = PreviewFull;   // ★[v111.7.1] 지표면이 지금 설계와 같으면 가장자리 · 데이라잇 · 계단선 · 노리선은 안 그림(진짜 선이 있음) — 충돌 · 옹벽 제안만
        // ★[설치 전 검토 · 열린 질문] 도면에 안 넣은 Polyline3d가 임시 선으로 보이는지 근거가 없다 — 저장소에서 보이는 것이 확인된 Line 조각으로(PickMark와 같음).
        //   꺾은선을 먼저 모으고 덜 꺾이게 줄인 뒤, 전체가 많으면 허용을 키워 다시(임시 선 수 상한)
        var polys = new List<(List<Point3d> P, short Aci)>();
        void Line(IEnumerable<Point3d> pts, short aci)
        {
            // ★[재검토 낮음 3] 높이가 숫자가 아닌 점(바탕면 밖 칸)에서 끊는다 — 끝점이 NaN이면 줄이기가 고리 하나를 통째로 버린다
            var c = new List<Point3d>();
            foreach (var p in pts)
            {
                if (double.IsFinite(p.Z)) { c.Add(p); continue; }
                if (c.Count >= 2) polys.Add((c, aci));
                c = new List<Point3d>();
            }
            if (c.Count >= 2) polys.Add((c, aci));
        }
        // ★[도로 마무리 ① · 계획 5.6] 토막마다(빈 구간이면 둘 이상 — 하나면 지금과 같음)
        var pieces = Pieces.Count > 0 && ReferenceEquals(Pieces[0].R, Last) ? Pieces : new List<Piece> { new Piece { S0 = S0, R = Last, Lines = Lines } };
        foreach (var pc in pieces)
        {
        var r = pc.R;
        if (r == null || r.Error.Length > 0) continue;
        float Zat(double x, double y)
        {
            int i = Math.Clamp((int)((x - r.X0) / r.Cell), 0, r.W - 1), j = Math.Clamp((int)((y - r.Y0) / r.Cell), 0, r.H - 1);
            float z = r.Z[j * r.W + i]; return float.IsFinite(z) ? z : r.Base[j * r.W + i];
        }
        // 가장자리(하늘) · 데이라잇(주황) · 충돌(빨강)
        if (full) Line(r.EdgeL.Select(p => new Point3d(p.X, p.Y, p.Z)), 4);
        if (full) Line(r.EdgeR.Select(p => new Point3d(p.X, p.Y, p.Z)), 4);
        // ★[v111] 단면 광선 데이라잇은 점마다 땅 높이를 갖고 있다(칸 높이로 다시 읽지 않음)
        if (!full) { }
        else if (r.DaylightZ.Count > 0) foreach (var l in r.DaylightZ) Line(l.Select(p => new Point3d(p.X, p.Y, p.Z)), 30);
        else foreach (var l in r.Daylight) Line(l.Select(p => new Point3d(p.X, p.Y, Zat(p.X, p.Y))), 30);
        foreach (var l in RoadGrade.Outline(r, k => k == 4)) Line(l.Select(p => new Point3d(p.X, p.Y, Zat(p.X, p.Y))), 1);
        // 옹벽(파랑 · 설계) · 제안 옹벽(빨강) — 가장자리를 따라
        try
        {
            using var tr = _doc.Database.TransactionManager.StartTransaction();
            var al = (CivilDb.Alignment)tr.GetObject(AlignId, OpenMode.ForRead);
            var pr = (CivilDb.Profile)tr.GetObject(PlanId, OpenMode.ForRead);
            var sec = Design.Section();
            IEnumerable<Point3d> Along(double a, double b, int side)
            {
                double off = side < 0 ? -sec.OuterL : sec.OuterR;          // Civil 오프셋 = 오른쪽 +
                for (double s = a; ; s += 1.0)
                {
                    double sc = Math.Min(s, b), x = 0, y = 0, z = 0;
                    bool ok = true;
                    try { al.PointLocation(sc, off, ref x, ref y); z = pr.ElevationAt(sc) + sec.Drop(side * Math.Abs(off)); } catch { ok = false; }
                    if (ok) yield return new Point3d(x, y, z);
                    if (sc >= b) yield break;
                }
            }
            // ★[v116.4 · JACK 1109] 옹벽 제안(자홍) 선 없앰
            tr.Commit();
        }
        catch { }
        }
        // ★[도로 마무리 ② · 계획 6.4] 측구 가운데 선(하늘 · 도면 «DH도로-측구선»과 같은 자리) — 지표면이 옛것일 때만(진짜 선이 있으면 안 그림)
        if (full) try
        {
            using var trD = _doc.Database.TransactionManager.StartTransaction();
            foreach (var dl in RoadDitchLines.Lines(trD, AlignId, PlanId, Design, Ranges)) Line(dl.Pts, 4);
            trD.Commit();
        }
        catch { }
        // ★[v110.3 · JACK 1029 «평면 사면 모양 = 2D처럼 소단선 방향»] 사면선 · 소단선(회색) · 옹벽선(빨강) — ★[설치 전 검토 L4] 맨 뒤(조각이 넘치면 제안 · 데이라잇보다 먼저 잘림)
        if (full && !PickMode) foreach (var tl in Lines) Line(tl.Pts.Select(p => new Point3d(p.X, p.Y, p.Z)), (short)(tl.Role == RoadGrade.LineRole.Wall ? 1 : 8));
        var add = new List<Entity>();
        for (double tol = 0.05; ; tol *= 2)
        {
            add.Clear();
            int segs = 0;
            var simp = polys.Select(q => (P: Simplify(q.P, tol), q.Aci)).ToList();
            foreach (var q in simp) segs += q.P.Count - 1;
            if (segs > MaxSegs && tol < 5) continue;
            // ★[재검토 낮음 4] 그래도 많으면(충돌 칸 섬이 많음) 작은 고리(점 3개 미만)부터 버리고, 그래도면 상한에서 자름 — 알림에 적음
            if (segs > MaxSegs) simp = simp.Where(q => q.P.Count >= 3).ToList();
            foreach (var (p, aci) in simp)
                for (int i = 0; i + 1 < p.Count && add.Count < MaxSegs; i++)
                    if (p[i].DistanceTo(p[i + 1]) > 1e-6) add.Add(new Autodesk.AutoCAD.DatabaseServices.Line(p[i], p[i + 1]) { ColorIndex = aci });
            if (segs > MaxSegs) Note = (Note.Length > 0 ? Note + " · " : "") + $"미리보기 선이 많아 일부만({MaxSegs}조각)";
            break;
        }
        // ★[v110.3 · 검토 M6] 노리선(긴 선만 · 노랑) — 줄이기 · 점 수 버리기와 따로 · 몫 넘으면 고르게 솎음
        if (full && !PickMode) try
        {
            var tk = new List<(Point3 A, Point3 B)>();
            // ★[v116 · 고리 엔진 §4] 고리 토막은 고리 노리선(옛 Ticks는 단면 광선 r.Sections가 있어야 함)
            foreach (var pc in pieces) if (pc.R != null && pc.R.Error.Length == 0)
                    tk.AddRange(pc.Ring != null ? RoadRing.Ticks(pc.Ring, GradingSettings.HatchShort, GradingSettings.HatchLong, longOnly: true)
                                                : RoadGrade.Ticks(pc.R, pc.Lines, GradingSettings.HatchShort, GradingSettings.HatchLong, longOnly: true));
            int stride = Math.Max(1, (int)Math.Ceiling(tk.Count / (double)MaxTicks));
            for (int i = 0; i < tk.Count; i += stride)
            {
                var (a, b) = tk[i];
                if (double.IsFinite(a.Z) && double.IsFinite(b.Z)) add.Add(new Autodesk.AutoCAD.DatabaseServices.Line(new Point3d(a.X, a.Y, a.Z), new Point3d(b.X, b.Y, b.Z)) { ColorIndex = 2 });
            }
        }
        catch { }
        try
        {
            var tm = TransientManager.CurrentTransientManager;
            var noIds = new IntegerCollection();
            foreach (var e in add)
            {
                bool ok = false;
                try { ok = tm.AddTransient(e, TransientDrawingMode.Main, 128, noIds); } catch { ok = false; }
                if (ok) _marks.Add(e); else { try { e.Dispose(); } catch { } }   // 안 들어간 것은 관리자가 안 쥠 — 바로 놓아도 됨
            }
            try { _doc.Editor.UpdateScreen(); } catch { }
        }
        catch { foreach (var e in add) if (!_marks.Contains(e)) { try { e.Dispose(); } catch { } } }
    }

    /// <summary>임시 선 조각 상한 — 넘으면 덜 꺾이게 줄이는 허용을 두 배씩(0.05 m부터).</summary>
    const int MaxSegs = 3000;
    /// <summary>★[v110.3] 미리보기 노리선 몫(긴 선).</summary>
    const int MaxTicks = 1200;

    /// <summary>설계를 노선에 쓴다(문서 잠금 · 바뀜 표시 막고) — 창 값 · 옹벽 구간 · 경계.</summary>
    internal void SaveDesign()
    {
        bool was = Suppress; Suppress = true;   // ★[v110.4 · 검토 L1] [종단도 다시] 안에서 불려도 바깥 막음을 안 풂
        try
        {
            using var dl = _doc.LockDocument();
            using var tr = _doc.Database.TransactionManager.StartTransaction();
            Design.Write(tr, AlignId);
            tr.Commit();
        }
        finally { Suppress = was; }
    }

    internal Document Doc => _doc;
}
