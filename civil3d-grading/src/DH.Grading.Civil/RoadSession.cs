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
    internal double S0;                     // Core 측점 0 = 노선 측점 S0(실제 첫 표본)
    internal double GradeNow;               // 계획 종단 지금 최대 경사(PVI 사이 · 사람이 옮긴 뒤)
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
    internal static RoadSession Start(Document doc, ObjectId aid)
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
        var s = new RoadSession(doc, aid, d) { Name = name };
        s.PlanId = Handle(db, d.PlanProfile); s.BaseProfId = Handle(db, d.BaseProfile);
        if (s.PlanId.IsNull) throw new InvalidOperationException("계획 종단을 못 찾았습니다(지웠나요?) — [진입도로]로 폴리선부터 다시");
        // ★[설치 전 검토 · 중간] 바탕면을 이름으로 못 찾으면(초기화 · 재정지로 바뀜) 지금 도면에서 다시 고른다
        if (!s.ResolveBase()) throw new InvalidOperationException("바탕면(정지면_DH 또는 원지반)이 없습니다");
        using (var tr = db.TransactionManager.StartTransaction()) { s._xrecId = RoadDesign.XrecId(tr, aid); tr.Commit(); }
        s._limitId = Handle(db, d.LimitHandle);
        _all[doc] = s;
        db.ObjectModified += s.OnModified;
        db.ObjectErased += s.OnErased;
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
        try { Flag(e.DBObject.ObjectId); } catch { }
    }

    /// <summary>지움 · 되살림(실행 취소)도 같은 표시 — 노선 · 계획 종단이 지워졌으면 다음 계산에서 세션을 닫는다.</summary>
    void OnErased(object? sender, ObjectErasedEventArgs e)
    {
        if (Suppress) return;
        try { Flag(e.DBObject.ObjectId); } catch { }
    }

    void Flag(ObjectId id)
    {
        if (id == AlignId || id == PlanId || id == _xrecId || id == _limitId) _dirty = true;    // ★[설치 전 검토 · 중간 · L8] 설계 XRecord(실행 취소) · 사업 경계 폴리선도
        else if (id == BaseSurfId) { _dirty = true; _baseDirty = true; }
    }

    /// <summary>창 값이 바뀌었다 — 바로 다시(문서는 여기서 잠금).</summary>
    internal void MarkDirty() => _dirty = true;

    void Tick()
    {
        if (!_dirty || Suppress) return;
        try
        {
            if (AcadApp.DocumentManager.MdiActiveDocument != _doc) return;
            if (!string.IsNullOrEmpty(_doc.CommandInProgress)) return;
        }
        catch { return; }
        _dirty = false;
        Recompute();
    }

    /// <summary>한 번 계산 — 노선 · 종단 표본 → Core 도로 정지(칸 1 m) → 임시 선 · 숫자.</summary>
    internal void Recompute()
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
            using (var tr = db.TransactionManager.StartTransaction())
            {
                (X, Y, Z, s0, s1, note) = RoadBuilder.Sample(tr, AlignId, PlanId);
                limit = ReadLimit(tr);
                try { GradeNow = RoadBuilder.PlanGrade(tr, PlanId); } catch { GradeNow = double.NaN; }
                tr.Commit();
            }
            S0 = s0;
            // ★[설치 전 검토 · 중간 — 성능] 지난번에 넓혀서 찾았으면 그 범위로 바로(넓혀 다시 세 판을 PVI마다 되풀이하지 않게)
            //   지난 사면이 모두 35 m 안에서 끝났으면 다시 40부터(넓은 범위를 계속 끌고 가지 않게)
            double reach = Last != null && Last.Error.Length == 0 && !Last.ReachHit && Last.ReachUsed > 40
                           && Last.Slopes.Count > 0 && Last.Slopes.Max(q => q.Width) > 35 ? Last.ReachUsed : 40;
            var inp = new RoadGrade.Input
            {
                X = X, Y = Y, Z = Z, Sec = Design.Section(), Cut = Design.Cut(), Fill = Design.Fill(),
                Walls = Design.CoreWalls().Select(w => w with { S0 = w.S0 - s0, S1 = w.S1 - s0 }).ToList(),
                Base = _base, Cell = 1.0, Reach = reach, SuggestH = Design.SuggestH, Limit = limit,
            };
            Last = RoadGrade.Run(inp);
            Note = string.Join(" · ", new[] { _baseNote, note }.Where(t => t.Length > 0));
            _baseNote = "";
            var swD = System.Diagnostics.Stopwatch.StartNew();
            DrawPreview();
            _retried = false;
            // ★[재검토 열린 질문] 임시 선 걷고 거는 시간도 함께(Core 계산 시간만으로는 화면 멈춤을 못 봄)
            try { DiagLog.Append($"\n[진입도로] {Name} — {Last.Text} · 그리기 {swD.Elapsed.TotalMilliseconds:F0} ms · 선 {_marks.Count} · 최대 종단경사 {GradeNow * 100:F1} %" + (note.Length > 0 ? " · " + note : "")); } catch { }
        }
        catch (System.Exception ex)
        {
            // ★[설치 전 검토 · 중간] 실패하면 옛 미리보기를 걷는다 — 결과처럼 계속 보이지 않게
            Note = "계산 실패 — " + ex.Message;
            Last = null;
            ClearMarks();
            try { _doc.Editor.UpdateScreen(); } catch { }
            try { DiagLog.Append($"\n[진입도로] {Name} — 계산 실패 — {ex}"); } catch { }
            // ★[재검토 낮음 6] 잠금 충돌 같은 일시 실패면 한 번만 다시(계속 실패하는 것을 0.35초마다 되풀이하지 않게)
            if (!_retried) { _retried = true; _dirty = true; }
        }
        Changed?.Invoke(this);
    }
    bool _retried;

    /// <summary>★[설치 전 검토 · 중간] 바탕면이 바뀌었다(지움 · 이름 바뀜 = 재정지) — 이름으로 다시 찾고(옛 면을 말없이 따라가지 않게) 높이 표를 다시 만든다. 문서는 부르는 쪽이 잠금.</summary>
    void EnsureBase()
    {
        if (!(_baseDirty || _base == null || BaseSurfId.IsErased)) return;
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

    /// <summary>[종단 초안 다시] — 지금 바탕면에서 노선을 따라 읽어 계획 종단 PVI를 다시(우리가 쓰는 동안 바뀜 표시 막음).</summary>
    internal RoadProfileDraft.Result RedraftNow()
    {
        Suppress = true;
        try
        {
            using var dl = _doc.LockDocument();
            EnsureBase();
            return RoadBuilder.Redraft(_doc, AlignId, _base!, PlanId, Fresh());
        }
        finally { Suppress = false; }
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

    List<(double, double)>? ReadLimit(Transaction tr)
    {
        var id = Handle(_doc.Database, Design.LimitHandle);
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

    void DrawPreview()
    {
        ClearMarks();
        var r = Last;
        if (r == null || r.Error.Length > 0) return;
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
        float Zat(double x, double y)
        {
            int i = Math.Clamp((int)((x - r.X0) / r.Cell), 0, r.W - 1), j = Math.Clamp((int)((y - r.Y0) / r.Cell), 0, r.H - 1);
            float z = r.Z[j * r.W + i]; return float.IsFinite(z) ? z : r.Base[j * r.W + i];
        }
        // 가장자리(하늘) · 데이라잇(주황) · 충돌(빨강)
        Line(r.EdgeL.Select(p => new Point3d(p.X, p.Y, p.Z)), 4);
        Line(r.EdgeR.Select(p => new Point3d(p.X, p.Y, p.Z)), 4);
        foreach (var l in r.Daylight) Line(l.Select(p => new Point3d(p.X, p.Y, Zat(p.X, p.Y))), 30);
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
            foreach (var w in Design.CoreWalls()) Line(Along(w.S0, w.S1, w.Side), 5);
            foreach (var (a, b, side, _) in r.Suggest) Line(Along(a + S0, b + S0, side), 1);
            tr.Commit();
        }
        catch { }
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

    /// <summary>설계를 노선에 쓴다(문서 잠금 · 바뀜 표시 막고) — 창 값 · 옹벽 구간 · 경계.</summary>
    internal void SaveDesign()
    {
        Suppress = true;
        try
        {
            using var dl = _doc.LockDocument();
            using var tr = _doc.Database.TransactionManager.StartTransaction();
            Design.Write(tr, AlignId);
            tr.Commit();
        }
        finally { Suppress = false; }
    }

    internal Document Doc => _doc;
}
