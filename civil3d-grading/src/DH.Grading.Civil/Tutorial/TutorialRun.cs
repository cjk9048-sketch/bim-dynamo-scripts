using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Threading;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using DH.Grading.Civil.Commands;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;
using Step = DH.Grading.Civil.Tutorial.TutorialFlow.Step;

[assembly: CommandClass(typeof(DH.Grading.Civil.Tutorial.TutorialCommand))]   // ★[튜토리얼 · JACK 1117] DHTUTORIAL

namespace DH.Grading.Civil.Tutorial;

/// <summary>★★[튜토리얼 · JACK 1117] 리본 [튜토리얼] — 새 빈 도면(중부원점 EPSG 5186)에서
/// 원지반 가져오기(서버 지표면 · 대모산 · 두 점) → 계획 경계 그리기 → 고도 → 계획부지 생성까지 따라 하기.
/// <para>지금 도면은 안 건드린다(늘 새 도면).</para></summary>
public sealed class TutorialCommand
{
    [CommandMethod("DHTUTORIAL", CommandFlags.Session)]
    public static void Run()
    {
        try { Tut.Current?.End("다시 시작"); } catch { }
        var dm = AcadApp.DocumentManager;
        var doc = NewDoc(dm, out string how);
        if (doc == null)
        {
            try { dm.MdiActiveDocument?.Editor.WriteMessage("\n[튜토리얼] 새 도면을 못 열었습니다 — " + how); } catch { }
            try { DiagLog.Append("\n[튜토리얼] 새 도면 실패 — " + how); } catch { }
            return;
        }
        try { if (!ReferenceEquals(dm.MdiActiveDocument, doc)) dm.MdiActiveDocument = doc; } catch { }
        // ★중부원점(EPSG 5186 = KOREA_GRS80_127TM) — 도면 좌표계 + 정지설정(지도 · 가져오기가 읽는 값) 둘 다
        string csNote;
        try
        {
            using (doc.LockDocument())
            {
                var (ok, note) = KoreaCs.Assign(doc.Database, 5186);
                csNote = (ok ? "" : "⚠") + note;
            }
        }
        catch (System.Exception ex) { csNote = "⚠좌표계 지정 실패 — " + ex.Message; }
        // ★[검토 MED] 끝나면 원래 원점으로 되돌림(전역 값 — 다른 원점으로 일하던 사람의 지도 · 내보내기가 바뀌지 않게)
        Tut.RestoreEpsg ??= GradingSettings.ExportEpsg;
        GradingSettings.ExportEpsg = 5186;
        try { doc.Editor.WriteMessage("\n[튜토리얼] 새 도면 · 좌표계 중부원점(EPSG 5186) — " + csNote); } catch { }
        try { DiagLog.Append($"\n[튜토리얼] 시작 — 템플릿 {how} · {csNote}"); } catch { }
        Tut.Current = new Tut(doc);
        Tut.Current.Begin();
    }

    /// <summary>새 도면 — QNEW 템플릿 → 템플릿 폴더의 Civil 미터 · acadiso → 이름만(찾기 경로).</summary>
    static Document? NewDoc(DocumentCollection dm, out string how)
    {
        var tries = new List<string>();
        try
        {
            dynamic prefs = AcadApp.Preferences;
            string q = prefs.Files.QNewTemplateFile;
            if (!string.IsNullOrWhiteSpace(q) && System.IO.File.Exists(q)) tries.Add(q);
            string tp = prefs.Files.TemplateDwgPath;
            foreach (var n in new[] { "_AutoCAD Civil 3D (Metric) NCS.dwt", "acadiso.dwt" })
            {
                string p = System.IO.Path.Combine(tp ?? "", n);
                if (System.IO.File.Exists(p)) tries.Add(p);
            }
        }
        catch { }
        tries.Add("acadiso.dwt");
        tries.Add("acad.dwt");
        how = "";
        foreach (var t in tries)
        {
            try
            {
                var d = dm.Add(t);
                if (d != null) { how = t; return d; }
            }
            catch (System.Exception ex) { how += $"{t}: {ex.Message} · "; }
        }
        return null;
    }
}

/// <summary>★[튜토리얼] 한 판 — 사건(명령 끝 · 고른 것 · 객체 바뀜 · 지도 페이지)이 오면 사실을 다시 읽고 단계를 정해 그린다.</summary>
internal sealed class Tut
{
    internal static Tut? Current;
    /// <summary>★[검토 MED] 튜토리얼 시작 전 원점 — 끝날 때 되돌림.</summary>
    internal static int? RestoreEpsg;
    /// <summary>★[검토 LOW] 리본 탭은 단계가 바뀔 때 한 번만 앞으로(사용자가 다른 탭을 써도 되돌리지 않게).</summary>
    Step? _tabStep;

    const string LayerHint = "DH-튜토리얼";
    static readonly string Off = "{\"on\":false}";

    readonly Document _doc;
    readonly Database _db;
    Dispatcher? _disp;
    Step _step = Step.Ground;
    bool _ended, _docGone;
    bool _dxf, _netChecking;
    bool? _net;
    string? _note, _drawNote;
    bool _importing, _importEnded, _contourEnded, _buildEnded;
    int _pageSub = 1;
    bool _pageAck, _pageDead;
    string _pageSent = "";
    // 도면 사실 — 명령 중에는 지난 값(명령 한복판에 트랜잭션을 안 연다)
    bool _fGround, _fGrade, _fBoundary, _fElev;
    readonly List<ObjectId> _cands = new();
    ObjectId _boundary = ObjectId.Null;
    double? _recZ;
    readonly List<ObjectId> _hints = new();
    bool _hintWanted, _hintEraseWanted;
    TutBalloon? _balloon;
    TutGlow? _glow;
    TutSpot? _spot;
    AdornerLayer? _spotLayer;
    FrameworkElement? _spotRoot;
    DispatcherTimer? _timer;
    bool _idleQueued;
    Step? _announced;

    internal Tut(Document doc) { _doc = doc; _db = doc.Database; }

    internal void Begin()
    {
        _disp = Dispatcher.CurrentDispatcher;
        try
        {
            _doc.CommandWillStart += OnCmdStart;
            _doc.CommandEnded += OnCmdEnd;
            _doc.CommandCancelled += OnCmdEnd;
            _doc.CommandFailed += OnCmdEnd;
            _db.ObjectAppended += OnAppended;
            _db.ObjectModified += OnModified;
            _db.ObjectErased += OnErased;
            AcadApp.DocumentManager.DocumentActivated += OnDocActivated;
            AcadApp.DocumentManager.DocumentToBeDestroyed += OnDocDestroy;
            PickSession.Changed += Kick;
            MapPalette.TutMessage += OnPage;
        }
        catch (System.Exception ex) { Log("훅 실패 — " + ex.Message); }
        _balloon = new TutBalloon();
        _balloon.Act += Do;
        _glow = new TutGlow();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _timer.Tick += (_, __) => Update();
        _timer.Start();
        CheckNet();
        Kick();
    }

    // ── 사건 ──────────────────────────────────────────────────────────────
    static bool IsImport(string n) => n is "DHCONTOURBOX" or "DHNGII" or "DHGNDCONTOUR";

    void OnCmdStart(object? s, CommandEventArgs e)
    {
        string n = (e.GlobalCommandName ?? "").ToUpperInvariant();
        if (IsImport(n)) _importing = true;
        Kick();
    }

    void OnCmdEnd(object? s, CommandEventArgs e)
    {
        string n = (e.GlobalCommandName ?? "").ToUpperInvariant();
        if (IsImport(n)) { _importing = false; _importEnded = true; }
        else if (n == "DHCONTOUR") _contourEnded = true;
        else if (n == PickCommands.CmdBuild) _buildEnded = true;
        Kick();
    }

    void OnAppended(object? s, ObjectEventArgs e)
    {
        try { if (_step == Step.Draw && e.DBObject is Polyline) { _cands.Add(e.DBObject.ObjectId); Kick(); } } catch { }
    }

    void OnModified(object? s, ObjectEventArgs e)
    {
        if (_step is not (Step.Draw or Step.Elev or Step.Panel or Step.PickPlan or Step.PickGround or Step.Build)) return;
        try
        {
            var id = e.DBObject.ObjectId;
            if (id == _boundary || (_step == Step.Draw && _cands.Contains(id))) Kick();
        }
        catch { }
    }

    void OnErased(object? s, ObjectErasedEventArgs e)
    {
        try { if (e.DBObject.ObjectId == _boundary) Kick(); } catch { }
    }

    void OnDocActivated(object? s, DocumentCollectionEventArgs e) => Kick();

    void OnDocDestroy(object? s, DocumentCollectionEventArgs e)
    {
        if (!ReferenceEquals(e.Document, _doc)) return;
        _docGone = true;
        End("도면 닫힘");
    }

    /// <summary>지도 페이지가 보낸 튜토리얼 말 — <c>{kind:'tut', ev:…}</c>.</summary>
    void OnPage(string raw)
    {
        if (_ended) return;
        try
        {
            using var d = System.Text.Json.JsonDocument.Parse(raw);
            var r = d.RootElement;
            string ev = r.TryGetProperty("ev", out var e) ? e.GetString() ?? "" : "";
            switch (ev)
            {
                case "ready": _pageAck = false; _pageDead = false; _pageSub = 1; _pageSent = ""; break;
                case "ack": _pageAck = true; break;
                case "sub": if (r.TryGetProperty("n", out var nn) && nn.TryGetInt32(out int n)) _pageSub = n; break;
                case "dead": _pageDead = true; _pageSent = ""; break;
                case "quit": Do("quit"); return;
            }
            Kick();
        }
        catch { }
    }

    /// <summary>다음 한가한 때 한 번 다시 본다(사건마다 바로 하지 않는다 — 명령 끝 알림 안에서는 도면을 못 고친다).</summary>
    void Kick()
    {
        if (_ended || _idleQueued) return;
        _idleQueued = true;
        try { AcadApp.Idle += OnIdle; } catch { _idleQueued = false; }
    }

    void OnIdle(object? s, EventArgs e)
    {
        try { AcadApp.Idle -= OnIdle; } catch { }
        _idleQueued = false;
        Update();
    }

    /// <summary>사내망(GIS DB) 확인 — 뒤 실에서(3초 한도) · 끝나면 다시 그린다.</summary>
    void CheckNet()
    {
        if (_netChecking || _disp == null) return;
        _netChecking = true;
        var disp = _disp;
        System.Threading.Tasks.Task.Run(() =>
        {
            bool ok;
            string why = "";
            try { ok = GisDb.CanConnect(out why); } catch (System.Exception ex) { ok = false; why = ex.Message; }
            try
            {
                disp.BeginInvoke(new Action(() =>
                {
                    _netChecking = false;
                    _net = ok;
                    Log("사내망 " + (ok ? "연결됨" : "안 됨 — " + why));
                    Kick();
                }));
            }
            catch { }
        });
    }

    // ── 단추 ──────────────────────────────────────────────────────────────
    void Do(string act)
    {
        try
        {
            switch (act)
            {
                case "quit":
                    End("그만두기");
                    try { AcadApp.DocumentManager.MdiActiveDocument?.Editor.WriteMessage("\n[튜토리얼] 그만둠"); } catch { }
                    break;
                case "restart":
                    End("다시 시작");
                    AcadApp.DocumentManager.MdiActiveDocument?.SendStringToExecute("\u0003\u0003" + "DHTUTORIAL ", true, false, true);
                    break;
                case "netretry": _net = null; CheckNet(); break;
                case "dxf": _dxf = true; _note = null; Log("수치지도 DXF 길로"); Kick(); break;
                case "back": AcadApp.DocumentManager.MdiActiveDocument = _doc; break;
            }
        }
        catch (System.Exception ex) { Log("단추 " + act + " 실패 — " + ex.Message); }
    }

    // ── 사실 읽기 · 단계 정하기 ──────────────────────────────────────────
    void Update()
    {
        if (_ended) return;
        try
        {
            if (!ReferenceEquals(AcadApp.DocumentManager.MdiActiveDocument, _doc)) { RenderAway(); return; }
            bool quiet = string.IsNullOrEmpty(_doc.CommandInProgress) && !PickSession.Busy;
            if (quiet) ReadDb();

            bool mapOpen = MapPalette.IsOpen;
            if (!mapOpen) { _pageSub = 1; _pageAck = false; _pageDead = false; _pageSent = ""; }
            else _net = true;   // 지도가 떴다 = DHCONTOUR가 사내망 확인을 넘었다
            if (_contourEnded && quiet)
            {
                _contourEnded = false;
                if (!mapOpen && !_fGround) _net = false;   // DHCONTOUR가 지도를 못 열었다 = 사내망 접속 실패(명령이 알림 창도 띄움)
            }
            var panel = GradingPalette.OpenPanel;
            var f = new TutorialFlow.Facts(
                Ground: _fGround, Importing: _importing, MapOpen: mapOpen, PageSub: _pageSub, Dxf: _dxf,
                Boundary: _fBoundary, Elev: _fElev, PanelOpen: panel != null,
                PlanPicked: PickSession.Peek(_doc, PickSession.KeyPlan) != null,
                GroundPicked: PickSession.Peek(_doc, PickSession.KeyGround) != null,
                Grade: _fGrade);
            var s = TutorialFlow.Derive(f);

            string? evNote = null;
            if (_importEnded && quiet) { _importEnded = false; if (!_fGround) evNote = TutorialFlow.NoteImportFail; }
            if (_buildEnded && quiet) { _buildEnded = false; if (!_fGrade) evNote = TutorialFlow.NoteGradeFail; }

            if (s != _step)
            {
                var old = _step;
                _step = s;
                _note = null;
                if (TutorialFlow.IsMap(old) && s == Step.Ground) _note = TutorialFlow.NoteMapClosed;
                else if (TutorialFlow.IsPanel(old) && s == Step.Panel) _note = TutorialFlow.NotePanelClosed;
                else if (old >= Step.Elev && old != Step.Done && s == Step.Draw) _note = TutorialFlow.NoteLineLost;
                if (_balloon != null) _balloon.Dragged = false;
                if (old == Step.Draw) { _hintWanted = false; _hintEraseWanted = true; }
                if (s == Step.Draw) { _hintWanted = true; _hintEraseWanted = false; }
                Log($"{old} → {s}");
            }
            if (evNote != null) _note = evNote;
            if (quiet) Hints();
            Render(panel);
        }
        catch (System.Exception ex) { Log("갱신 실패 — " + ex.Message); }
    }

    void ReadDb()
    {
        using var tr = _db.TransactionManager.StartTransaction();
        var gid = GradingBuilder.FindSurfaceByBaseName(tr, ImportGisCommand.GroundSurfaceName);
        _fGround = !gid.IsNull;
        _fGrade = !GradingBuilder.FindSurfaceByBaseName(tr, "정지면_DH").IsNull;
        if (!_boundary.IsNull && Closed(tr, _boundary) == null) _boundary = ObjectId.Null;
        _drawNote = null;
        if (_boundary.IsNull && _fGround && _cands.Count > 0 && tr.GetObject(gid, OpenMode.ForRead) is Autodesk.Civil.DatabaseServices.Surface surf)
        {
            for (int i = _cands.Count - 1; i >= 0; i--)
            {
                var id = _cands[i];
                if (id.IsNull || id.IsErased || !id.IsValid) continue;
                if (tr.GetObject(id, OpenMode.ForRead) is not Polyline pl) continue;
                if (!pl.Closed || pl.NumberOfVertices < 3) { _drawNote ??= TutorialFlow.NoteOpenLine; continue; }
                var pts = new List<Point2d>();
                for (int k = 0; k < pl.NumberOfVertices; k++) pts.Add(pl.GetPoint2dAt(k));
                bool inside = true;
                foreach (var p in pts) { try { surf.FindElevationAtXY(p.X, p.Y); } catch { inside = false; break; } }
                if (!inside) { _drawNote ??= TutorialFlow.NoteOutside; continue; }
                _boundary = id;
                _recZ = MeanZ(surf, pts);
                _drawNote = null;
                Log($"경계 받음 — 꼭짓점 {pts.Count} · 추천 고도 {_recZ:0.0}");
                break;
            }
        }
        var bl = _boundary.IsNull ? null : Closed(tr, _boundary);
        _fBoundary = bl != null;
        _fElev = bl != null && Math.Abs(bl.Elevation) > 1e-6;
        tr.Commit();
    }

    static Polyline? Closed(Transaction tr, ObjectId id)
    {
        try
        {
            if (id.IsNull || id.IsErased || !id.IsValid) return null;
            return tr.GetObject(id, OpenMode.ForRead) is Polyline pl && pl.Closed && pl.NumberOfVertices >= 3 ? pl : null;
        }
        catch { return null; }
    }

    /// <summary>경계 안 원지반 평균 높이(격자 30×30 · 0.1 m 반올림) — 안에 든 점이 없으면 꼭짓점 평균.</summary>
    static double? MeanZ(Autodesk.Civil.DatabaseServices.Surface surf, List<Point2d> pts)
    {
        double x0 = double.MaxValue, y0 = double.MaxValue, x1 = double.MinValue, y1 = double.MinValue;
        foreach (var p in pts) { x0 = Math.Min(x0, p.X); y0 = Math.Min(y0, p.Y); x1 = Math.Max(x1, p.X); y1 = Math.Max(y1, p.Y); }
        double sum = 0; int cnt = 0;
        const int N = 30;
        for (int i = 0; i < N; i++)
            for (int j = 0; j < N; j++)
            {
                double x = x0 + (i + 0.5) / N * (x1 - x0), y = y0 + (j + 0.5) / N * (y1 - y0);
                if (!Inside(pts, x, y)) continue;
                try { sum += surf.FindElevationAtXY(x, y); cnt++; } catch { }
            }
        if (cnt == 0)
            foreach (var p in pts) { try { sum += surf.FindElevationAtXY(p.X, p.Y); cnt++; } catch { } }
        return cnt == 0 ? null : Math.Round(sum / cnt, 1);
    }

    static bool Inside(List<Point2d> poly, double x, double y)
    {
        bool c = false;
        for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
        {
            var a = poly[i]; var b = poly[j];
            if ((a.Y > y) != (b.Y > y) && x < (b.X - a.X) * (y - a.Y) / (b.Y - a.Y) + a.X) c = !c;
        }
        return c;
    }

    // ── 도면 안 표시(노란 점선 네모) — 튜토리얼 새 도면에만 · 레이어 DH-튜토리얼 ─────
    void Hints()
    {
        try
        {
            if (_hintEraseWanted) { EraseHints(); _hintEraseWanted = false; }
            if (_hintWanted && _hints.Count == 0 && _step == Step.Draw) DrawHints();
        }
        catch (System.Exception ex) { Log("점선 표시 실패 — " + ex.Message); _hintWanted = false; }
    }

    void DrawHints()
    {
        _hintWanted = false;
        using var lk = _doc.LockDocument();
        using var tr = _db.TransactionManager.StartTransaction();
        var gid = GradingBuilder.FindSurfaceByBaseName(tr, ImportGisCommand.GroundSurfaceName);
        if (gid.IsNull || tr.GetObject(gid, OpenMode.ForRead) is not Autodesk.Civil.DatabaseServices.Surface s) { tr.Commit(); return; }
        var gp = s.GetGeneralProperties();
        double cx = (gp.MinimumCoordinateX + gp.MaximumCoordinateX) / 2, cy = (gp.MinimumCoordinateY + gp.MaximumCoordinateY) / 2;
        double hw = (gp.MaximumCoordinateX - gp.MinimumCoordinateX) * 0.3, hh = (gp.MaximumCoordinateY - gp.MinimumCoordinateY) * 0.3;
        if (!(hw > 0.5 && hh > 0.5)) { tr.Commit(); return; }
        var lt = (LayerTable)tr.GetObject(_db.LayerTableId, OpenMode.ForRead);
        if (!lt.Has(LayerHint))
        {
            lt.UpgradeOpen();
            var rec = new LayerTableRecord { Name = LayerHint, Color = Autodesk.AutoCAD.Colors.Color.FromColorIndex(Autodesk.AutoCAD.Colors.ColorMethod.ByAci, 2) };
            lt.Add(rec);
            tr.AddNewlyCreatedDBObject(rec, true);
        }
        bool dashed = false;
        try
        {
            var ltt = (LinetypeTable)tr.GetObject(_db.LinetypeTableId, OpenMode.ForRead);
            if (!ltt.Has("DASHED")) _db.LoadLineTypeFile("DASHED", "acadiso.lin");
            dashed = ((LinetypeTable)tr.GetObject(_db.LinetypeTableId, OpenMode.ForRead)).Has("DASHED");
        }
        catch { }
        var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(_db), OpenMode.ForWrite);
        var c = new[] { new Point3d(cx - hw, cy - hh, 0), new Point3d(cx + hw, cy - hh, 0), new Point3d(cx + hw, cy + hh, 0), new Point3d(cx - hw, cy + hh, 0) };
        for (int i = 0; i < 4; i++)
        {
            var ln = new Line(c[i], c[(i + 1) % 4]) { Layer = LayerHint, ColorIndex = 2 };
            if (dashed) { try { ln.Linetype = "DASHED"; ln.LinetypeScale = Math.Max(hw, hh) / 200.0; } catch { } }
            ms.AppendEntity(ln);
            tr.AddNewlyCreatedDBObject(ln, true);
            _hints.Add(ln.ObjectId);
        }
        tr.Commit();
        Log($"점선 네모 — 가운데 ({cx:F1}, {cy:F1}) · {hw * 2:F0}×{hh * 2:F0} m");
    }

    void EraseHints()
    {
        if (_hints.Count == 0 || _docGone) return;
        using var lk = _doc.LockDocument();
        using var tr = _db.TransactionManager.StartTransaction();
        foreach (var id in _hints)
        {
            try { if (!id.IsNull && id.IsValid && !id.IsErased) tr.GetObject(id, OpenMode.ForWrite).Erase(); } catch { }
        }
        tr.Commit();
        _hints.Clear();
    }

    // ── 그리기 ────────────────────────────────────────────────────────────
    static readonly (string, string)[] BtnBase = { ("다시 시작", "restart"), ("그만두기", "quit") };

    void Render(GradingPanel? panel)
    {
        if (_balloon == null || _glow == null) return;
        var s = _step;
        int n = TutorialFlow.Num(s);
        string num = s == Step.Done ? "완료" : $"{n} / {TutorialFlow.Total}";
        string text = TutorialFlow.Text(s, _dxf);
        string? extra = s == Step.Elev && _recZ is double z ? $"추천 고도: {z:0.0}" : null;
        string? note = _note;
        if (s == Step.Draw && _drawNote != null) note = _drawNote;
        var btns = new List<(string, string)>();
        if (s == Step.Ground && !_dxf)
        {
            if (_net == false) { note = TutorialFlow.NoteNet; btns.Add(("다시 확인", "netretry")); btns.Add(("수치지도 DXF로", "dxf")); }
            else if (_netChecking && note == null) note = TutorialFlow.NoteNetChecking;
        }
        if (TutorialFlow.IsMap(s) && _pageDead) note = TutorialFlow.NoteMapDead;
        if (s == Step.Done) { btns.Add(("끝내기", "quit")); btns.Add(("다시 시작", "restart")); }
        else btns.AddRange(BtnBase);

        if (_announced != s)
        {
            _announced = s;
            try { _doc.Editor.WriteMessage($"\n[튜토리얼] {num} — {text}" + (extra != null ? " · " + extra : "")); } catch { }
        }

        // 지도 페이지 — 지도 단계에서만 켠다(나머지는 끔)
        string want = TutorialFlow.IsMap(s) && !_pageDead ? PageOn() : Off;
        if (want != _pageSent && (MapPalette.IsOpen || want == Off)) { MapPalette.TutPost(want); _pageSent = want; }

        // 도킹창 덮개
        FrameworkElement? target = null;
        if (TutorialFlow.IsPanel(s) && panel != null)
        {
            panel.ShowMakeTab();
            target = s switch { Step.PickPlan => panel.PickPlanButton, Step.PickGround => panel.PickGroundButton, _ => panel.BuildButton };
        }
        Spot(panel, target, n);

        // 리본 빛 테
        Rect? rib = null;
        if (s is Step.Ground or Step.Panel)
        {
            if (_tabStep != s) { _tabStep = s; TutRibbon.ActivateTab(); }
            var item = TutRibbon.FindItem(s == Step.Ground ? "원지반\n가져오기" : "계획부지\n생성");
            var fe = item != null ? TutRibbon.FindVisual(item) : null;
            rib = fe != null ? TutRibbon.ScreenRect(fe) : null;
            if (rib == null && note == null) note = "리본 [DH 정지] 탭에서 찾기";
        }
        if (rib is Rect rr) _glow.ShowAround(rr); else if (_glow.IsVisible) _glow.Hide();

        _balloon.Set(num, text, extra, note, btns);
        if (TutorialFlow.IsMap(s) && _pageAck && !_pageDead && MapPalette.IsOpen) { if (_balloon.IsVisible) _balloon.Hide(); return; }   // 지도 페이지가 제 말풍선을 띄운다
        if (rib is Rect r1) _balloon.ShowNear(r1, left: false);
        else if (_spot?.HoleOnScreen() is Rect r2) _balloon.ShowNear(r2, left: true);
        else { var m = MainRect(); _balloon.ShowAt(m.Left + 40, m.Top + 210); }
    }

    string PageOn()
    {
        var o = new
        {
            on = true,
            @base = TutorialFlow.Num(Step.Search),
            total = TutorialFlow.Total,
            t = new[] { TutorialFlow.Text(Step.Search, false), TutorialFlow.Text(Step.Pick, false), TutorialFlow.Text(Step.Send, false) },
            skip = "건너뛰기",
            quit = "그만두기",
        };
        return System.Text.Json.JsonSerializer.Serialize(o);
    }

    void Spot(GradingPanel? panel, FrameworkElement? target, int num)
    {
        if (target == null || panel?.SpotRoot == null) { RemoveSpot(); return; }
        if (_spot != null && ReferenceEquals(_spot.Target, target)) return;
        RemoveSpot();
        var root = panel.SpotRoot;
        var layer = AdornerLayer.GetAdornerLayer(root);
        if (layer == null) { Log("도킹창 덮개 자리(AdornerLayer) 없음"); return; }
        _spot = new TutSpot(root, target, num);
        layer.Add(_spot);
        _spotLayer = layer;
        _spotRoot = root;
        root.PreviewKeyDown += OnPanelKey;
        try { target.BringIntoView(); } catch { }
    }

    void RemoveSpot()
    {
        if (_spot == null) return;
        try { _spot.Stop(); _spotLayer?.Remove(_spot); } catch { }
        try { if (_spotRoot != null) _spotRoot.PreviewKeyDown -= OnPanelKey; } catch { }
        _spot = null; _spotLayer = null; _spotRoot = null;
    }

    void OnPanelKey(object? s, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        e.Handled = true;
        Do("quit");
    }

    /// <summary>다른 도면이 앞에 있다 — 덮개 · 테 · 지도 표시를 걷고 [돌아가기]만.</summary>
    void RenderAway()
    {
        RemoveSpot();
        try { if (_glow?.IsVisible == true) _glow.Hide(); } catch { }
        if (_pageSent != Off) { MapPalette.TutPost(Off); _pageSent = Off; }
        if (_balloon == null) return;
        _balloon.Set("—", TutorialFlow.NoteAway, null, null, new[] { ("돌아가기", "back"), ("그만두기", "quit") });
        var m = MainRect();
        _balloon.ShowAt(m.Left + 40, m.Top + 210);
    }

    // ── 끝 ────────────────────────────────────────────────────────────────
    internal void End(string why)
    {
        if (_ended) return;
        _ended = true;
        if (ReferenceEquals(Current, this)) Current = null;
        if (Current == null && RestoreEpsg is int pe) { GradingSettings.ExportEpsg = pe; RestoreEpsg = null; }
        try { _timer?.Stop(); } catch { }
        try { if (_idleQueued) AcadApp.Idle -= OnIdle; } catch { }
        try
        {
            _doc.CommandWillStart -= OnCmdStart;
            _doc.CommandEnded -= OnCmdEnd;
            _doc.CommandCancelled -= OnCmdEnd;
            _doc.CommandFailed -= OnCmdEnd;
        }
        catch { }
        try { _db.ObjectAppended -= OnAppended; _db.ObjectModified -= OnModified; _db.ObjectErased -= OnErased; } catch { }
        try { AcadApp.DocumentManager.DocumentActivated -= OnDocActivated; AcadApp.DocumentManager.DocumentToBeDestroyed -= OnDocDestroy; } catch { }
        try { PickSession.Changed -= Kick; } catch { }
        try { MapPalette.TutMessage -= OnPage; } catch { }
        RemoveSpot();
        try { _glow?.Close(); } catch { }
        try { _balloon?.Close(); } catch { }
        try { MapPalette.TutPost(Off); } catch { }
        // 점선 네모 — 명령 중이면(도면을 못 고침) 끝날 때까지 기다렸다 지운다(2분 한도)
        if (!_docGone && _hints.Count > 0)
        {
            var until = DateTime.UtcNow.AddMinutes(2);
            void Retry(object? s, EventArgs e)
            {
                try
                {
                    if (DateTime.UtcNow > until || _docGone) { AcadApp.Idle -= Retry; return; }
                    if (!string.IsNullOrEmpty(_doc.CommandInProgress)) return;
                    AcadApp.Idle -= Retry;
                    EraseHints();
                }
                catch { try { AcadApp.Idle -= Retry; } catch { } }
            }
            try { AcadApp.Idle += Retry; } catch { }
        }
        Log("끝 — " + why + $" · 마지막 단계 {_step}");
    }

    // ── 자리 ──────────────────────────────────────────────────────────────
    [StructLayout(LayoutKind.Sequential)] struct RECT { public int L, T, R, B; }
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] static extern uint GetDpiForWindow(IntPtr h);

    /// <summary>AutoCAD 본창 네모(DIP) — 못 재면 화면 작업 영역.</summary>
    static Rect MainRect()
    {
        try
        {
            var h = AcadApp.MainWindow.Handle;
            if (GetWindowRect(h, out var r))
            {
                double k = 1;
                try { uint d = GetDpiForWindow(h); if (d >= 96) k = d / 96.0; } catch { }
                return new Rect(r.L / k, r.T / k, (r.R - r.L) / k, (r.B - r.T) / k);
            }
        }
        catch { }
        return SystemParameters.WorkArea;
    }

    static void Log(string s) { try { DiagLog.Append("\n[튜토리얼] " + s); } catch { } }
}
