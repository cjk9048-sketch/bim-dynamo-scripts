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
            string sheetNote = "";
            using (var tr = db.TransactionManager.StartTransaction())
            {
                (X, Y, Z, s0, s1, note) = RoadBuilder.Sample(tr, AlignId, PlanId);
                limit = ReadLimit(tr);
                try { GradeNow = RoadBuilder.PlanGrade(tr, PlanId); } catch { GradeNow = double.NaN; }
                // ★[v110.4 · 계획 3.3] 종단도가 지금 종단(PVI · 노선 길이) · 축척과 다르면 알림 — 밴드 값은 Civil이 따라가지만 측점 · 표고 범위 · 글씨 벌리기는 그대로
                try
                {
                    if (Design.SheetViews.Count > 0)
                    {
                        double cs = Commands.SheetCommand.CurrentDrawingScale(db);
                        if (RoadSheets.StampOf(tr, AlignId, PlanId) != Design.SheetStamp || (cs > 0 && Math.Abs(cs - Design.SheetScale) > 1e-6))
                            sheetNote = "종단도가 지금 종단 · 축척과 다릅니다 — [종단도 다시]";
                    }
                }
                catch { }
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
                TierWalls = Design.CoreTierWalls(s0),   // ★[v110.3] 계단선 변환 옹벽(옛 가장자리 옹벽은 0단 · 소단 0)
                Base = _base, Cell = 1.0, Reach = reach, SuggestH = Design.SuggestH, Limit = limit,
            };
            Last = RoadGrade.Run(inp);
            // ★[v110.3] 사면선 · 소단선 · 옹벽선(미리보기 · [옹벽 변환] 찍기가 씀)
            var swL = System.Diagnostics.Stopwatch.StartNew();
            try { Lines = Last.Error.Length == 0 ? RoadGrade.TierLines(Last) : new(); } catch (System.Exception exL) { Lines = new(); try { DiagLog.Append("\n[진입도로] 계단선 못 셈 — " + exL.Message); } catch { } }
            Note = string.Join(" · ", new[] { _baseNote, note, sheetNote }.Where(t => t.Length > 0));
            _baseNote = "";
            var swD = System.Diagnostics.Stopwatch.StartNew();
            DrawPreview();
            _retried = false;
            // ★[재검토 열린 질문] 임시 선 걷고 거는 시간도 함께(Core 계산 시간만으로는 화면 멈춤을 못 봄)
            try { DiagLog.Append($"\n[진입도로] {Name} — {Last.Text} · 계단선 {Lines.Count}가닥 {swL.Elapsed.TotalMilliseconds:F0} ms · 그리기 {swD.Elapsed.TotalMilliseconds:F0} ms · 선 {_marks.Count} · 최대 종단경사 {GradeNow * 100:F1} %" + (note.Length > 0 ? " · " + note : "")); } catch { }
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
        bool was = Suppress; Suppress = true;   // ★[v110.4 · 검토 L1] 겹쳐 불려도 바깥 막음을 안 풂
        try
        {
            using var dl = _doc.LockDocument();
            EnsureBase();
            return RoadBuilder.Redraft(_doc, AlignId, _base!, PlanId, Fresh());
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
        try
        {
            using var dl = _doc.LockDocument();
            using (var tr = _doc.Database.TransactionManager.StartTransaction())
            {
                var d = RoadDesign.Read(tr, AlignId);
                tr.Commit();
                if (d != null) Design = d;
            }
            EnsureBase();
            return RoadSheets.Build(this, at, log);
        }
        finally { Suppress = was; }
    }

    internal const string SurfBase = "진입도로_DH";
    const string TempSurf = "진입도로_작업중";
    const double SurfCell = 0.5;
    const int MaxSurfTris = 1_500_000;

    /// <summary>★★[v110.1 · JACK 1029 «도로 면만 따로 보기» — 인터뷰: 도로 + 사면 · 단추로 · 정지면_DH는 안 건드림 · 이번엔 Civil 화면만]
    /// 지금 설계로 0.5 m 칸 한 번 더 계산 → Core 세모(<see cref="RoadGrade.SurfaceTris"/>) → LandXML(새 파일) → Civil 면 → 되읽어 넘긴 면과 같은지 대조 → 굳힘.
    /// 옹벽 배면 면(WallBackBuilder)과 같은 길 — Civil이 그대로 안 받았으면 지우고 실패로 알린다. 다시 누르면 앞의 면을 지우고 같은 이름으로. 명령 안에서 부른다(문서 잠김).</summary>
    internal string MakeSurface()
    {
        var db = _doc.Database;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var d = Fresh();
        EnsureBase();
        double[] X, Y, Z; double s0; string note; List<(double, double)>? limit;
        using (var tr = db.TransactionManager.StartTransaction())
        {
            (X, Y, Z, s0, _, note) = RoadBuilder.Sample(tr, AlignId, PlanId);
            limit = ReadLimit(tr);
            tr.Commit();
        }
        double reach = Last != null && Last.Error.Length == 0 && !Last.ReachHit && Last.ReachUsed > 40 ? Last.ReachUsed : 40;
        var r = RoadGrade.Run(new RoadGrade.Input
        {
            X = X, Y = Y, Z = Z, Sec = d.Section(), Cut = d.Cut(), Fill = d.Fill(),
            TierWalls = d.CoreTierWalls(s0),
            Base = _base!, Cell = SurfCell, Reach = reach, SuggestH = d.SuggestH, Limit = limit,
        });
        if (r.Error.Length > 0) throw new InvalidOperationException(r.Error);
        var tris = RoadGrade.SurfaceTris(r, out string tnote);
        // ★[설치 전 검토 중간 3] 아주 큰 도로(세모 150만 넘음) — 1 m 칸으로 다시(시간 · 메모리)
        if (tris.Count > MaxSurfTris)
        {
            r = RoadGrade.Run(new RoadGrade.Input
            {
                X = X, Y = Y, Z = Z, Sec = d.Section(), Cut = d.Cut(), Fill = d.Fill(),
                TierWalls = d.CoreTierWalls(s0),
                Base = _base!, Cell = 1.0, Reach = reach, SuggestH = d.SuggestH, Limit = limit,
            });
            if (r.Error.Length > 0) throw new InvalidOperationException(r.Error);
            tris = RoadGrade.SurfaceTris(r, out tnote);
            tnote += " · 커서 1 m 칸으로";
        }
        if (tris.Count == 0) throw new InvalidOperationException("바뀐 자리가 없어 면을 안 만들었습니다 — " + tnote);
        long msGrid = sw.ElapsedMilliseconds;
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
                try { var stId = EnsureRoadSurfStyle(tr); if (!stId.IsNull) cTin.StyleId = stId; else { var bs = (CivilDb.TinSurface)tr.GetObject(BaseSurfId, OpenMode.ForRead); cTin.StyleId = bs.StyleId; } } catch { }
                try { cTin.LayerId = Commands.SectionCommand.EnsureLayer(db, tr, RoadBuilder.LaySurf, 3); } catch { }
                d.SurfaceHandle = cid.Handle.ToString();
                d.Write(tr, AlignId);
                tr.Commit();
                made = ObjectId.Null;
                Design = d;
                // ★[v110.3] 실제 선 — 사면선 · 소단선 · 노리선 · 옹벽선(이 도로 것만 지우고 새로 — 검토 M7)
                string lineNote;
                try { lineNote = DrawRoadLines(db, r); } catch (System.Exception exL) { lineNote = "⚠선을 못 그림 — " + exL.Message; }
                // ★[설치 전 검토 낮음 6] 면 끝이 땅에 안 닿은 자리 · 옹벽 자리(가운데 높이)를 알림 · 수량은 이 면의 칸 기준
                string msg = $"도로 면 '{name}' — 절토 {r.CutVol:N0} ㎥ · 성토 {r.FillVol:N0} ㎥({r.Cell:0.#} m 칸) · {sw.ElapsedMilliseconds / 1000.0:F1}초"
                             + (r.ReachHit ? $" · ⚠데이라잇 못 찾음(범위 {r.ReachUsed:F0} m 끝에서 면이 땅에 안 닿음)" : "")
                             + (r.Conflicts > 0 ? $" · ⚠사면 충돌 {r.Conflicts}칸(옹벽 자리 — 가운데 높이로 넣음)" : "")
                             + (frz.Length > 0 ? " · ⚠굳힘 실패 " + frz : "") + " · " + lineNote;
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
    const string LineApp = "DH_ROADLINE";
    internal const string SurfStyleName = "DH-진입도로 경계";

    /// <summary>★★[v110.3 · 계획 4 · 검토 M7] [도로 면 만들기]의 실제 선 — 사면선(윗선) · 소단선(아랫선) · 옹벽선(굵게) · 노리선(짧은 · 긴) · 3D(설계 높이).
    /// 이 도로(노선 핸들 XData)의 것만 먼저 지움 — 한 도면에 도로가 둘이어도 남의 선은 그대로. 못 지우면 안 그림(겹쳐 두 벌이 되지 않게).</summary>
    string DrawRoadLines(Database db, RoadGrade.Result r)
    {
        var lines = RoadGrade.TierLines(r);
        var ticks = RoadGrade.Ticks(r, lines, GradingSettings.HatchShort, GradingSettings.HatchLong);
        string me = AlignId.Handle.ToString();
        var lays = new[] { LaySlope, LayBench, LayNori, LayWall };
        int erased = 0, failErase = 0, nLine = 0, nWall = 0, nTick = 0;
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
                 lN = Commands.SectionCommand.EnsureLayer(db, tr, LayNori, 2), lW = Commands.SectionCommand.EnsureLayer(db, tr, LayWall, 1);
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
        foreach (var (a, b) in ticks)
        {
            if (!double.IsFinite(a.Z) || !double.IsFinite(b.Z)) continue;
            var ln = new Line(new Point3d(a.X, a.Y, a.Z), new Point3d(b.X, b.Y, b.Z)) { LayerId = lN };
            ms.AppendEntity(ln); tr.AddNewlyCreatedDBObject(ln, true);
            using (var tg = Tag()) ln.XData = tg;
            nTick++;
        }
        tr.Commit();
        return $"사면선 · 소단선 {nLine} · 옹벽선 {nWall} · 노리선 {nTick}" + (erased > 0 ? $"(옛 선 {erased} 지움)" : "");
    }

    /// <summary>★[v110.3 · 검토 M8] 진입도로 면 스타일 — 평면 = 경계만 · 모형 = 세모 + 경계(없으면 만들고 · 있으면 그대로 씀 — 사람이 고친 값을 안 덮음).</summary>
    static ObjectId EnsureRoadSurfStyle(Transaction tr)
    {
        var cdoc = Autodesk.Civil.ApplicationServices.CivilApplication.ActiveDocument;
        var styles = cdoc.Styles.SurfaceStyles;
        foreach (ObjectId sid in styles)
        {
            try { if (tr.GetObject(sid, OpenMode.ForRead) is CivilDb.Styles.SurfaceStyle s0 && string.Equals(s0.Name, SurfStyleName, StringComparison.OrdinalIgnoreCase)) return sid; }
            catch { }
        }
        ObjectId id = styles.Add(SurfStyleName);
        if (id.IsNull) return ObjectId.Null;
        var st = (CivilDb.Styles.SurfaceStyle)tr.GetObject(id, OpenMode.ForWrite);
        foreach (CivilDb.Styles.SurfaceDisplayStyleType t in Enum.GetValues(typeof(CivilDb.Styles.SurfaceDisplayStyleType)))
        {
            bool plan = t == CivilDb.Styles.SurfaceDisplayStyleType.Boundary;
            bool model = t == CivilDb.Styles.SurfaceDisplayStyleType.Boundary || t == CivilDb.Styles.SurfaceDisplayStyleType.Triangles;
            try { st.GetDisplayStylePlan(t).Visible = plan; } catch { }
            try { st.GetDisplayStyleModel(t).Visible = model; } catch { }
        }
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
            // ★[v110.3 · 검토 L4] 옹벽 제안 = 자홍(옹벽선 빨강과 구별) · 옛 «가장자리 따라 파란 옹벽»은 옹벽선(아래)으로 대신
            foreach (var (a, b, side, _) in r.Suggest) Line(Along(a + S0, b + S0, side), 6);
            tr.Commit();
        }
        catch { }
        // ★[v110.3 · JACK 1029 «평면 사면 모양 = 2D처럼 소단선 방향»] 사면선 · 소단선(회색) · 옹벽선(빨강) — ★[설치 전 검토 L4] 맨 뒤(조각이 넘치면 제안 · 데이라잇보다 먼저 잘림)
        if (!PickMode) foreach (var tl in Lines) Line(tl.Pts.Select(p => new Point3d(p.X, p.Y, p.Z)), (short)(tl.Role == RoadGrade.LineRole.Wall ? 1 : 8));
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
        if (!PickMode) try
        {
            var tk = RoadGrade.Ticks(r, Lines, GradingSettings.HatchShort, GradingSettings.HatchLong, longOnly: true);
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
