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
using Chapter = DH.Grading.Civil.Tutorial.TutorialFlow.Chapter;

[assembly: CommandClass(typeof(DH.Grading.Civil.Tutorial.TutorialCommand))]   // ★[튜토리얼 · JACK 1117] DHTUTORIAL

namespace DH.Grading.Civil.Tutorial;

/// <summary>★★[튜토리얼 · JACK 1117] 리본 [튜토리얼] — 단원 고르기 → 단원마다 따라 하기.
/// <para>1 원지반 가져오기(새 빈 도면 · 중부원점 — 서버 지표면 맛보기 + 수치지도 DXF) · 2 계획부지 생성 · 3 터파기 생성(기본 자료 사본 또는 앞 단원 내 결과 사본).
/// 지금 도면은 안 건드린다(늘 새 도면 · 사본).</para></summary>
public sealed class TutorialCommand
{
    /// <summary>[다음 단원] · [다시 시작]이 고르기 창 없이 바로 열 단원.</summary>
    internal static (Chapter Ch, bool Mine)? Preset;

    [CommandMethod("DHTUTORIAL", CommandFlags.Session)]
    public static void Run()
    {
        try { Tut.Current?.End("다시 시작"); } catch { }
        Chapter ch; bool mine;
        if (Preset is { } p) { Preset = null; ch = p.Ch; mine = p.Mine; }
        else
        {
            var w = new TutorialPicker();
            bool? ok = null;
            try { ok = AcadApp.ShowModalWindow(w); } catch (System.Exception ex) { Log("고르기 창 실패 — " + ex.Message); }
            if (ok != true || w.Chosen == null) return;
            ch = w.Chosen.Value; mine = w.Mine;
        }
        Start(ch, mine);
    }

    static void Start(Chapter ch, bool mine)
    {
        var dm = AcadApp.DocumentManager;
        Document? doc = null;
        string how, work = "";
        if (ch == Chapter.Ground)
        {
            doc = NewDoc(dm, out how);
            if (doc == null) { Say(dm, "새 도면을 못 열었습니다 — " + how); return; }
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
            how += " · " + csNote;
        }
        else
        {
            string? src = mine ? TutorialData.MineFor(ch) : TutorialData.StartDwg(ch);
            if (src == null) { Say(dm, $"튜토리얼 자료 없음 — {TutorialFlow.ChapterName(ch)}"); return; }
            var w = TutorialData.CopyToWork(src, ch, out string why);
            if (w == null) { Say(dm, "자료 사본을 못 만듦 — " + why); return; }
            work = w;
            try { doc = dm.Open(work, false); } catch (System.Exception ex) { Say(dm, "자료 도면을 못 엶 — " + ex.Message); return; }
            if (doc == null) return;
            try { if (!ReferenceEquals(dm.MdiActiveDocument, doc)) dm.MdiActiveDocument = doc; } catch { }
            how = (mine ? "내 결과 " : "기본 자료 ") + System.IO.Path.GetFileName(src);
        }
        Log($"시작 — {(int)ch}. {TutorialFlow.ChapterName(ch)} · {how}");
        Tut.Current = new Tut(doc, ch, mine, work);
        Tut.Current.Begin();
    }

    static void Say(DocumentCollection dm, string s)
    {
        try { dm.MdiActiveDocument?.Editor.WriteMessage("\n[튜토리얼] " + s); } catch { }
        Log(s);
    }

    static void Log(string s) { try { DiagLog.Append("\n[튜토리얼] " + s); } catch { } }

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

/// <summary>★[튜토리얼] 한 단원 — 사건(명령 끝 · 고른 것 · 객체 바뀜 · 지도 페이지)이 오면 사실을 다시 읽고 단계를 정해 그린다.</summary>
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
    readonly Chapter _ch;
    readonly bool _mine;
    string _work;
    Dispatcher? _disp;
    Step _step;
    bool _ended, _docGone;
    bool _netChecking;
    bool? _net;
    string? _note, _drawNote;
    bool _importing, _importEnded, _contourEnded, _buildEnded, _exBuildEnded, _wallEnded, _cropEnded;
    int _pageSub = 1;
    bool _pageAck, _pageDead;
    string _pageSent = "";
    // ── 1단원: 서버 지표면(1부) → 수치지도 DXF(2부)
    bool _part2;
    int _groundCount, _groundBase = -1;
    bool _fCropped;
    // ── 도면 사실 — 명령 중에는 지난 값(명령 한복판에 트랜잭션을 안 연다)
    bool _fGround, _fGrade, _fBoundary, _fElev, _fFits = true, _fitCache = true, _fExSurf, _fExBoundary, _fExElev;
    string _fitSig = "";
    double? _curZ, _exCurZ;
    // ── 따라 그릴 선(도면에 그려 둔 계획부지선 · 터파기선) — 단원 시작 때 한 번 찾음
    bool _linesFound;
    ObjectId _planGuide = ObjectId.Null, _exGuide = ObjectId.Null;
    List<Point2d>? _planPoly;
    double? _planZ;
    readonly List<ObjectId> _cands = new();
    ObjectId _boundary = ObjectId.Null, _exLine = ObjectId.Null;
    double? _recZ, _exRecZ;
    string? _hintNote;
    readonly List<ObjectId> _hints = new();
    readonly List<ObjectId> _hidden = new();
    bool _hintWanted, _hintEraseWanted;
    // ── 2단원: 구배 · 옹벽
    (string Cut, string Fill)? _slopeBase;
    string? _wallBase;
    bool _wallRan;
    bool _fWall;
    // ── 5단원 진입도로 — 명령 기록(시작 때 지난 기록 · 그때 단계) · 끝낸 일 비트 · 포장 단계 기준 · 노선 따라 그릴 선
    TutorialFlow.RoadSnap _rdLast;
    int _rdDone;
    readonly List<(string Cmd, TutorialFlow.RoadSnap Before, Step At)> _rdRuns = new();
    int? _paveBase;
    bool _rdGuideFound;
    ObjectId _rdGuide = ObjectId.Null;
    List<Point2d>? _rdPts;
    string? _rdNote;
    TutorialFlow.Facts _facts;
    // ── 끝(결과 저장)
    bool _saved;
    TutBalloon? _balloon;
    TutGlow? _glow;
    TutSpot? _spot;
    AdornerLayer? _spotLayer;
    FrameworkElement? _spotRoot;
    DispatcherTimer? _timer;
    bool _idleQueued;
    Step? _announced;

    internal Tut(Document doc, Chapter ch, bool mine, string work)
    {
        _doc = doc; _db = doc.Database; _ch = ch; _mine = mine; _work = work;
        _step = ch switch { Chapter.Ground => Step.Ground, Chapter.Excav => Step.ExDraw, Chapter.Road => Step.RdOpen, _ => Step.Draw };
    }

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
        if (_ch == Chapter.Ground) CheckNet();
        if (IsDraw(_step)) _hintWanted = true;
        Kick();
    }

    static bool IsDraw(Step s) => TutorialFlow.IsDraw(s);
    /// <summary>노란 점선을 띄우는 단계 — 따라 그리기 · 원지형 자르기(두 모서리 자리) · 진입도로 노선.</summary>
    static bool HasHint(Step s) => IsDraw(s) || s == Step.Crop || s == Step.RdDraw;

    // ── 사건 ──────────────────────────────────────────────────────────────
    static bool IsImport(string n) => n is "DHCONTOURBOX" or "DHNGII" or "DHGNDCONTOUR";

    void OnCmdStart(object? s, CommandEventArgs e)
    {
        string n = (e.GlobalCommandName ?? "").ToUpperInvariant();
        if (IsImport(n)) _importing = true;
        // ★[1부 DXF] 파일 창을 튜토리얼 DXF 자리에서 연다(한 번)
        if (n == "DHNGII" && _ch == Chapter.Ground && _part2 && TutorialData.DxfFile is string dxf) NgiiCommand.PresetFile = dxf;
        // ★[5단원] 진입도로 명령 — 시작 앞 기록(지난 한가할 때 읽은 것) · 그때 단계를 적어 둠 → 끝난 뒤 한가할 때 견줌(ReadRoad)
        if (_ch == Chapter.Road && n.StartsWith("DHROAD", StringComparison.Ordinal)) _rdRuns.Add((n, _rdLast, _step));
        Kick();
    }

    void OnCmdEnd(object? s, CommandEventArgs e)
    {
        string n = (e.GlobalCommandName ?? "").ToUpperInvariant();
        if (IsImport(n)) { _importing = false; _importEnded = true; }
        else if (n == "DHCONTOUR") _contourEnded = true;
        else if (n == PickCommands.CmdBuild) _buildEnded = true;
        else if (n == PickCommands.CmdExcavBuild) _exBuildEnded = true;
        else if (n == "DHWALL") { _wallRan = true; _wallEnded = true; }
        else if (n == "DHCROP") _cropEnded = true;
        Kick();
    }

    void OnAppended(object? s, ObjectEventArgs e)
    {
        try { if (IsDraw(_step) && e.DBObject is Polyline && !_hints.Contains(e.DBObject.ObjectId)) { _cands.Add(e.DBObject.ObjectId); Kick(); } } catch { }
    }

    void OnModified(object? s, ObjectEventArgs e)
    {
        if (_step is Step.Importing or Step.Ground or Step.Search or Step.Pick or Step.Send or Step.Dxf or Step.Crop) return;
        try
        {
            var id = e.DBObject.ObjectId;
            if (id == _boundary || id == _exLine || (IsDraw(_step) && _cands.Contains(id))) Kick();
        }
        catch { }
    }

    void OnErased(object? s, ObjectErasedEventArgs e)
    {
        try { var id = e.DBObject.ObjectId; if (id == _boundary || id == _exLine) Kick(); } catch { }
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
                    TutorialCommand.Preset = (_ch, _mine);
                    AcadApp.DocumentManager.MdiActiveDocument?.SendStringToExecute("\u0003\u0003" + "DHTUTORIAL ", true, false, true);
                    break;
                case "next":
                    if (TutorialFlow.Next(_ch) is Chapter nx)
                    {
                        End("다음 단원");
                        TutorialCommand.Preset = (nx, _saved && TutorialData.MineFor(nx) != null);
                        AcadApp.DocumentManager.MdiActiveDocument?.SendStringToExecute("\u0003\u0003" + "DHTUTORIAL ", true, false, true);
                    }
                    break;
                case "menu":
                    End("단원 고르기");
                    AcadApp.DocumentManager.MdiActiveDocument?.SendStringToExecute("\u0003\u0003" + "DHTUTORIAL ", true, false, true);
                    break;
                case "netretry": _net = null; CheckNet(); break;
                case "dxf": ToPart2("서버 지표면 건너뜀"); Kick(); break;
                case "useline":
                    // ★[JACK 1117 «그려 둔 선 쓰기»] 따라 그리기 대신 도면에 있던 선을 그대로 — 점선을 걷고 원래 선을 보인다
                    if (_step == Step.Draw && !_planGuide.IsNull) { _boundary = _planGuide; Log("그려 둔 계획부지선 씀"); }
                    else if (_step == Step.ExDraw && !_exGuide.IsNull) { _exLine = _exGuide; Log("그려 둔 터파기선 씀"); }
                    _hintEraseWanted = true;
                    Kick();
                    break;
                case "back": AcadApp.DocumentManager.MdiActiveDocument = _doc; break;
            }
        }
        catch (System.Exception ex) { Log("단추 " + act + " 실패 — " + ex.Message); }
    }

    /// <summary>1단원 2부(수치지도 DXF)로 — 지금 원지반 수가 새 기준.</summary>
    void ToPart2(string why)
    {
        if (_part2) return;
        _part2 = true;
        _groundBase = _groundCount;
        _note = null;
        Log("1단원 2부(수치지도 DXF) — " + why + (TutorialData.DxfFile == null ? " · 튜토리얼 DXF 없음" : ""));
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
            var exPanel = ExcavPalette.OpenPanel;
            if (panel != null && _slopeBase == null) _slopeBase = (panel.SlopeCutBox.Text, panel.SlopeFillBox.Text);
            bool slopeChanged = panel != null && _slopeBase is { } sb && (panel.SlopeCutBox.Text != sb.Cut || panel.SlopeFillBox.Text != sb.Fill);
            if (slopeChanged) _slopeSeen = true;
            var f = new TutorialFlow.Facts(
                Ground: _fGround, Importing: _importing, MapOpen: mapOpen, PageSub: _pageSub, Dxf: _part2,
                Boundary: _fBoundary, Elev: _fElev, PanelOpen: panel != null,
                PlanPicked: PickSession.Peek(_doc, PickSession.KeyPlan) != null,
                GroundPicked: PickSession.Peek(_doc, PickSession.KeyGround) != null,
                Grade: _fGrade, Fits: _fFits, Ch: _ch, SlopeChanged: _slopeSeen, Wall: _fWall,
                ExBoundary: _fExBoundary, ExElev: _fExElev, ExPanelOpen: exPanel != null,
                ExPicked: PickSession.Peek(_doc, PickSession.KeyExcav) != null,
                Shore: exPanel?.WallCheck.IsChecked == true, ExSurf: _fExSurf, Cropped: _fCropped,
                RdPanel: RoadPalette.OpenPanel != null, RdSecOpen: SectionPalette.RoadOpenPanel != null,
                RdRoad: _rdLast.Road && _rdLast.Surf, RdDone: _rdDone);
            _facts = f;
            var s = TutorialFlow.Derive(f);
            // 1단원: 서버 지표면이 됐으면 2부(수치지도 DXF)로 — DXF가 없으면 그대로 끝
            if (_ch == Chapter.Ground && s == Step.Done && !_part2 && TutorialData.DxfFile != null)
            {
                ToPart2("서버 지표면 됨");
                _note = TutorialFlow.NoteServerOk;
                s = TutorialFlow.Derive(f with { Ground = false, Dxf = true, Importing = false });
            }

            string? evNote = null;
            if (_importEnded && quiet) { _importEnded = false; if (!_fGround) evNote = TutorialFlow.NoteImportFail; }
            if (_buildEnded && quiet) { _buildEnded = false; if (!_fGrade) evNote = TutorialFlow.NoteGradeFail; }
            if (_exBuildEnded && quiet) { _exBuildEnded = false; if (!_fExSurf) evNote = TutorialFlow.NoteExFail; }
            if (_wallEnded && quiet) { _wallEnded = false; if (!_fWall) evNote = TutorialFlow.NoteWallFail; }
            if (_cropEnded && quiet) { _cropEnded = false; if (s == Step.Crop) evNote = TutorialFlow.NoteCropFail; }
            if (_rdNote != null && quiet) { evNote = _rdNote; _rdNote = null; }

            if (s != _step)
            {
                var old = _step;
                _step = s;
                if (_note != TutorialFlow.NoteServerOk || s != Step.Dxf) _note = null;
                if (TutorialFlow.IsMap(old) && s == Step.Ground) _note = TutorialFlow.NoteMapClosed;
                else if (TutorialFlow.IsPanel(old) && s == Step.Panel) _note = TutorialFlow.NotePanelClosed;
                else if (TutorialFlow.IsExPanel(old) && s == Step.ExPanel) _note = TutorialFlow.NoteExPanelClosed;
                else if (old is Step.Elev or Step.Panel or Step.PickPlan or Step.PickGround or Step.Slope or Step.Build && s == Step.Draw) _note = TutorialFlow.NoteLineLost;
                else if (TutorialFlow.IsRdPanel(old) && s == Step.RdOpen) _note = TutorialFlow.NoteRdClosed;
                else if (TutorialFlow.IsRdSec(old) && s == Step.RdSec) _note = TutorialFlow.NoteRdSecClosed;
                if (s == Step.RdPave) _paveBase = _rdLast.Pave;   // ★[5단원] 포장 단계가 된 때의 종류 — 바꾸면 끝
                if (_balloon != null) _balloon.Dragged = false;
                if (HasHint(old) && !HasHint(s)) { _hintWanted = false; _hintEraseWanted = true; }
                if (HasHint(s)) { _hintWanted = true; _hintEraseWanted = false; }
                if (s == Step.Done && _ch == Chapter.Ground && _part2 && TutorialData.DxfFile == null) _note = TutorialFlow.NoteNoDxf;
                Log($"{old} → {s}");
            }
            if (evNote != null) _note = evNote;
            if (quiet) { Hints(); if (_step == Step.Done && !_saved) SaveResult(); }
            Render(panel, exPanel);
        }
        catch (System.Exception ex) { Log("갱신 실패 — " + ex.Message); }
    }
    bool _slopeSeen;

    void ReadDb()
    {
        if (_ch == Chapter.Road) { ReadRoad(); return; }
        using var tr = _db.TransactionManager.StartTransaction();
        var gid = GradingBuilder.FindSurfaceByBaseName(tr, ImportGisCommand.GroundSurfaceName);
        if (_ch == Chapter.Ground)
        {
            _groundCount = CountGrounds(tr);
            if (_groundBase < 0) _groundBase = _groundCount;
            _fGround = _groundCount > _groundBase;
            _fCropped = !GroundCropBuilder.ReadBox(_db).IsNone;   // ★[JACK 1117] 잘라 남긴 기록(잘됐을 때만 씀 · 새 도면이라 앞 기록 없음)
            tr.Commit();
            return;
        }
        _fGround = !gid.IsNull;
        _fGrade = !GradingBuilder.FindSurfaceByBaseName(tr, "정지면_DH").IsNull;
        _fExSurf = !GradingBuilder.FindSurfaceByBaseName(tr, Commands.ExcavCommand.SurfName).IsNull;
        if (!_linesFound) FindLines(tr);
        var surf = gid.IsNull ? null : tr.GetObject(gid, OpenMode.ForRead) as Autodesk.Civil.DatabaseServices.Surface;
        if (_ch == Chapter.Site) ReadSite(tr, surf);
        else if (_ch == Chapter.Excav) ReadExcav(tr);
        tr.Commit();
    }

    /// <summary>★[5단원 진입도로] 기록 읽기 → 명령마다 앞뒤 견주기(그 단계에서 누른 명령만) → 끝낸 일 비트 · 실패 알림. 도로가 없어지면(U 등 · 초기화 아님) 처음부터.</summary>
    void ReadRoad()
    {
        if (!_rdGuideFound)
        {
            _rdGuideFound = true;
            try
            {
                using var tr = _db.TransactionManager.StartTransaction();
                (_rdGuide, _rdPts) = TutRoad.Guide(_db, tr);
                tr.Commit();
                Log($"노선 따라 그릴 선 — {(!_rdGuide.IsNull ? "그려 둔 선 " + _rdGuide.Handle : _rdPts != null ? $"부지에서 셈 · 점 {_rdPts.Count}" : "없음")}");
            }
            catch (System.Exception ex) { Log("노선 선 찾기 실패 — " + ex.Message); }
        }
        var now = TutRoad.Read(_doc);
        foreach (var (cmd, before, at) in _rdRuns)
        {
            bool did = TutorialFlow.RoadDid(cmd, before, now);
            if (cmd is "DHROADDRAW" or "DHROAD")
            {
                if (at == Step.RdDraw && !did) _rdNote = TutorialFlow.NoteRdDrawFail;
                continue;
            }
            var st = TutorialFlow.RoadStepOf(cmd);
            if (st == Step.Done || st != at) continue;   // 그 단계에서 누른 명령만(차례 밖은 안 셈)
            if (did) { _rdDone |= TutorialFlow.RdBit(st); Log($"{cmd} → {st} 끝"); }
            else { _rdNote = TutorialFlow.RoadFailNote(cmd); Log($"{cmd} → 기록 안 바뀜 ({st})"); }
        }
        _rdRuns.Clear();
        if (_step == Step.RdPave && _paveBase is int pb && TutorialFlow.PaveDid(pb, now)) { _rdDone |= TutorialFlow.RdBit(Step.RdPave); Log($"포장 {pb} → {now.Pave}"); }
        if (_rdLast.Road && !now.Road && (_rdDone & TutorialFlow.RdBit(Step.RdReset)) == 0) { _rdDone = 0; _rdNote = TutorialFlow.NoteRdLost; Log("진입도로 없어짐 → 처음부터"); }
        _rdLast = now;
    }

    /// <summary>가장 새 원지반(원지반 · 원지반_N 중 핸들이 가장 큰 것) — 1단원 2부 DXF 원지반.</summary>
    ObjectId NewestGround(Transaction tr)
    {
        ObjectId best = ObjectId.Null;
        try
        {
            string b = ImportGisCommand.GroundSurfaceName;
            foreach (ObjectId sid in Autodesk.Civil.ApplicationServices.CivilApplication.ActiveDocument.GetSurfaceIds())
                if (!sid.IsErased && tr.GetObject(sid, OpenMode.ForRead) is Autodesk.Civil.DatabaseServices.Surface s
                    && (s.Name == b || (s.Name.StartsWith(b + "_") && int.TryParse(s.Name.Substring(b.Length + 1), out _)))
                    && (best.IsNull || sid.Handle.Value > best.Handle.Value)) best = sid;
        }
        catch { }
        return best;
    }

    int CountGrounds(Transaction tr)
    {
        int n = 0;
        try
        {
            string b = ImportGisCommand.GroundSurfaceName;
            foreach (ObjectId sid in Autodesk.Civil.ApplicationServices.CivilApplication.ActiveDocument.GetSurfaceIds())
                if (!sid.IsErased && tr.GetObject(sid, OpenMode.ForRead) is Autodesk.Civil.DatabaseServices.Surface s
                    && (s.Name == b || (s.Name.StartsWith(b + "_") && int.TryParse(s.Name.Substring(b.Length + 1), out _)))) n++;
        }
        catch { }
        return n;
    }

    /// <summary>도면에 그려 둔 계획부지선 · 터파기선 찾기 — 닫힌 폴리선(DH 레이어 · 튜토리얼 레이어 · 닫힌 선이 많은 레이어(등고선) 빼고) 중
    /// 가장 넓은 것 = 계획부지선 · 그 안에 든 것 중 가장 넓은 것(계획부지선의 90 % 아래) = 터파기선.</summary>
    void FindLines(Transaction tr)
    {
        _linesFound = true;
        var all = new List<(ObjectId Id, string Layer, double Area, Point2d C)>();
        var perLayer = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (ObjectId id in (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(_db), OpenMode.ForRead))
        {
            if (tr.GetObject(id, OpenMode.ForRead) is not Polyline pl || !pl.Closed || pl.NumberOfVertices < 3) continue;
            perLayer[pl.Layer] = perLayer.TryGetValue(pl.Layer, out int c) ? c + 1 : 1;
            if (pl.Layer.StartsWith("DH", StringComparison.OrdinalIgnoreCase) || pl.Area < 50) continue;
            var e = pl.GeometricExtents;
            all.Add((id, pl.Layer, pl.Area, new Point2d((e.MinPoint.X + e.MaxPoint.X) / 2, (e.MinPoint.Y + e.MaxPoint.Y) / 2)));
        }
        all.RemoveAll(a => perLayer[a.Layer] >= 20);
        all.Sort((a, b) => b.Area.CompareTo(a.Area));
        if (all.Count == 0) { Log("그려 둔 선 없음"); return; }
        _planGuide = all[0].Id;
        var plan = (Polyline)tr.GetObject(_planGuide, OpenMode.ForRead);
        _planPoly = Pts(plan);
        _planZ = Math.Abs(plan.Elevation) > 1e-6 ? plan.Elevation : null;
        foreach (var a in all)
            if (a.Area < all[0].Area * 0.9 && Inside(_planPoly, a.C.X, a.C.Y)) { _exGuide = a.Id; break; }
        Log($"그려 둔 선 — 계획부지선 {plan.Handle} (고도 {plan.Elevation:0.0}) · 터파기선 {(_exGuide.IsNull ? "없음" : _exGuide.Handle.ToString())}");
        if (_ch == Chapter.Excav && !_exGuide.IsNull) { var ex = (Polyline)tr.GetObject(_exGuide, OpenMode.ForRead); if (Math.Abs(ex.Elevation) > 1e-6) _exRecZ = Math.Round(ex.Elevation, 1); }
        if (_ch == Chapter.Excav && _exRecZ == null && _planZ is double pz) _exRecZ = Math.Round(pz - 3.0, 1);
        if (_ch == Chapter.Site && _planZ is double pz2) _recZ = Math.Round(pz2, 1);
    }

    void ReadSite(Transaction tr, Autodesk.Civil.DatabaseServices.Surface? surf)
    {
        if (!_boundary.IsNull && Closed(tr, _boundary) == null) _boundary = ObjectId.Null;
        _drawNote = null;
        if (_boundary.IsNull && surf != null && _cands.Count > 0)
        {
            for (int i = _cands.Count - 1; i >= 0; i--)
            {
                var id = _cands[i];
                if (id.IsNull || id.IsErased || !id.IsValid || _hints.Contains(id)) continue;
                if (tr.GetObject(id, OpenMode.ForRead) is not Polyline pl) continue;
                if (!pl.Closed || pl.NumberOfVertices < 3) { _drawNote ??= TutorialFlow.NoteOpenLine; continue; }
                var pts = Pts(pl);
                bool inside = true;
                foreach (var p in pts) { try { surf.FindElevationAtXY(p.X, p.Y); } catch { inside = false; break; } }
                if (!inside) { _drawNote ??= TutorialFlow.NoteOutside; continue; }
                // ★[JACK 1117] 추천 고도로 정지해도 사면이 원지반 안에 드나 — 아니면 안 받는다(«사면이 원지반 경계를 벗어남» 막기)
                var rz = _recZ ?? MeanZ(surf, pts);
                var (fit, reach) = rz is double rzv ? TutorialFlow.Check(Xy(pts), rzv, Gz(surf), NEff()) : (false, double.NaN);
                if (!fit) { _drawNote ??= TutorialFlow.NoteEdge; Log($"경계 거절 — 사면 닿는 거리 {reach:F0} m가 원지반 밖 · 추천 고도 {rz:0.0}"); continue; }
                _boundary = id;
                _recZ = rz;
                _drawNote = null;
                Log($"경계 받음 — 꼭짓점 {pts.Count} · 추천 고도 {_recZ:0.0} · 사면 닿는 거리 {reach:F0} m");
                break;
            }
        }
        var bl = _boundary.IsNull ? null : Closed(tr, _boundary);
        if (bl != null && _recZ == null && surf != null) _recZ = MeanZ(surf, Pts(bl));
        _fBoundary = bl != null;
        _fElev = bl != null && Math.Abs(bl.Elevation) > 1e-6;
        _curZ = bl?.Elevation;
        // ★[JACK 1117] 넣은 고도로 다시 잰다(바뀔 때만) — 사면이 원지반 밖이면 고도 단계에 머문다
        _fFits = true;
        if (bl != null && _fElev && surf != null)
        {
            var pts = Pts(bl);
            double ne = NEff();
            var sig = new System.Text.StringBuilder($"{bl.Handle}|{bl.Elevation:R}|{ne:R}");
            foreach (var p in pts) sig.Append($"|{p.X:F2},{p.Y:F2}");
            if (sig.ToString() != _fitSig)
            {
                _fitSig = sig.ToString();
                var (ok, reach) = TutorialFlow.Check(Xy(pts), bl.Elevation, Gz(surf), ne);
                _fitCache = ok;
                Log($"고도 {bl.Elevation:0.0} 확인 — 사면 닿는 거리 {reach:F0} m · {(ok ? "원지반 안" : "원지반 밖")}");
            }
            _fFits = _fitCache;
        }
        // 옹벽 — 정지면이 생긴 뒤의 옹벽 구간 모양을 기준으로, [옹벽 변환]이 돈 뒤 달라졌나
        if (_fGrade)
        {
            string ws = WallSig(tr);
            if (_wallBase == null) _wallBase = ws;
            _fWall = _wallRan && ws != _wallBase;
        }
    }

    /// <summary>옹벽 구간 모양(정지 기록의 절토 · 성토 옹벽 구간) 한 줄.</summary>
    string WallSig(Transaction tr)
    {
        try
        {
            var regs = GradingBundleStore.TryLoadAll(_db, tr, out _);
            if (regs == null) return "";
            var sb = new System.Text.StringBuilder();
            foreach (var r in regs)
            {
                foreach (var z in r.CutWallZones ?? new List<DH.Grading.Core.SlopeZone>()) sb.Append($"c{z.T0:F2}-{z.T1:F2};");
                foreach (var z in r.FillWallZones ?? new List<DH.Grading.Core.SlopeZone>()) sb.Append($"f{z.T0:F2}-{z.T1:F2};");
                sb.Append('|');
            }
            return sb.ToString();
        }
        catch { return ""; }
    }

    void ReadExcav(Transaction tr)
    {
        if (!_exLine.IsNull && Closed(tr, _exLine) == null) _exLine = ObjectId.Null;
        _drawNote = null;
        if (_exLine.IsNull && _cands.Count > 0)
        {
            for (int i = _cands.Count - 1; i >= 0; i--)
            {
                var id = _cands[i];
                if (id.IsNull || id.IsErased || !id.IsValid || _hints.Contains(id)) continue;
                if (tr.GetObject(id, OpenMode.ForRead) is not Polyline pl) continue;
                if (!pl.Closed || pl.NumberOfVertices < 3) { _drawNote ??= TutorialFlow.NoteOpenLine; continue; }
                var pts = Pts(pl);
                if (_planPoly != null && !pts.TrueForAll(p => Inside(_planPoly, p.X, p.Y) || Near(_planPoly, p, 1.0))) { _drawNote ??= TutorialFlow.NoteExOutside; continue; }
                _exLine = id;
                _drawNote = null;
                Log($"터파기선 받음 — 꼭짓점 {pts.Count} · 추천 고도 {_exRecZ:0.0}");
                break;
            }
        }
        var el = _exLine.IsNull ? null : Closed(tr, _exLine);
        _fExBoundary = el != null;
        _exCurZ = el?.Elevation;
        bool high = el != null && _planZ is double pz && el.Elevation >= pz - 0.05;
        _fExElev = el != null && Math.Abs(el.Elevation) > 1e-6 && !high;
        _exHigh = el != null && Math.Abs(el.Elevation) > 1e-6 && high;
    }
    bool _exHigh;

    /// <summary>실효 구배 — 지금 계획부지 값(절토 · 성토 중 큰 쪽).</summary>
    static double NEff() => TutorialFlow.NEff(GradingSettings.CutSlope, GradingSettings.CutBenchHeight, GradingSettings.CutBenchWidth,
                                              GradingSettings.FillSlope, GradingSettings.FillBenchHeight, GradingSettings.FillBenchWidth);

    static Func<double, double, double?> Gz(Autodesk.Civil.DatabaseServices.Surface s) =>
        (x, y) => { try { return s.FindElevationAtXY(x, y); } catch { return null; } };

    static List<(double X, double Y)> Xy(List<Point2d> pts) => pts.ConvertAll(p => (p.X, p.Y));

    static List<Point2d> Pts(Polyline pl)
    {
        var l = new List<Point2d>();
        for (int k = 0; k < pl.NumberOfVertices; k++) l.Add(pl.GetPoint2dAt(k));
        return l;
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

    /// <summary>점이 다각형 변에서 <paramref name="tol"/> 안인가(선 위에 찍은 꼭짓점 허용).</summary>
    static bool Near(List<Point2d> poly, Point2d p, double tol)
    {
        for (int i = 0; i < poly.Count; i++)
        {
            var a = poly[i]; var b = poly[(i + 1) % poly.Count];
            var ab = b - a; double l2 = ab.DotProduct(ab);
            double t = l2 > 1e-12 ? Math.Clamp((p - a).DotProduct(ab) / l2, 0, 1) : 0;
            if ((a + ab * t).GetDistanceTo(p) <= tol) return true;
        }
        return false;
    }

    // ── 도면 안 표시(노란 점선) — 튜토리얼 사본 도면에만 · 레이어 DH-튜토리얼 ─────
    //   그려 둔 선이 있으면: 그 선을 노란 점선으로 덧그리고 원래 선은 잠깐 숨김(따라 그리기 · 끝나면 되돌림)
    //   없으면: 2단원 = 사면이 원지반 안에 드는 네모 · 3단원 = 계획부지 가운데 30×20 m
    void Hints()
    {
        try
        {
            if (_hintEraseWanted) { EraseHints(); _hintEraseWanted = false; }
            if (_hintWanted && _hints.Count == 0 && HasHint(_step)) DrawHints();
        }
        catch (System.Exception ex) { Log("점선 표시 실패 — " + ex.Message); _hintWanted = false; }
    }

    void DrawHints()
    {
        _hintWanted = false;
        using var lk = _doc.LockDocument();
        using var tr = _db.TransactionManager.StartTransaction();
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
        var guide = _step == Step.ExDraw ? _exGuide : _step == Step.Crop ? ObjectId.Null : _step == Step.RdDraw ? _rdGuide : _planGuide;
        Polyline? shape = null;
        if (!guide.IsNull && tr.GetObject(guide, OpenMode.ForWrite) is Polyline g)
        {
            shape = (Polyline)g.Clone();
            g.Visible = false;   // 원래 선은 잠깐 숨김 — 점선만 보이게(끝나면 되돌림)
            _hidden.Add(guide);
        }
        else if (_step == Step.RdDraw)
        {
            // ★[5단원] 그려 둔 노선 선이 없으면(내 결과) 부지에서 셈한 꺾인 길 — 못 셈이면 점선 없음(글이 «부지 끝에서 바깥 땅으로»)
            if (_rdPts == null || _rdPts.Count < 2) { tr.Commit(); return; }
            shape = new Polyline();
            for (int k = 0; k < _rdPts.Count; k++) shape.AddVertexAt(k, _rdPts[k], 0, 0, 0);
        }
        else
        {
            var r = HintRect(tr);
            if (r == null) { tr.Commit(); return; }
            var (cx, cy, hx, hy) = r.Value;
            shape = new Polyline();
            shape.AddVertexAt(0, new Point2d(cx - hx, cy - hy), 0, 0, 0); shape.AddVertexAt(1, new Point2d(cx + hx, cy - hy), 0, 0, 0);
            shape.AddVertexAt(2, new Point2d(cx + hx, cy + hy), 0, 0, 0); shape.AddVertexAt(3, new Point2d(cx - hx, cy + hy), 0, 0, 0);
            shape.Closed = true;
        }
        shape.Layer = LayerHint; shape.ColorIndex = 2;
        double size = 50;
        try { var e = shape.GeometricExtents; size = Math.Max(e.MaxPoint.X - e.MinPoint.X, e.MaxPoint.Y - e.MinPoint.Y); } catch { }
        if (dashed) { try { shape.Linetype = "DASHED"; shape.LinetypeScale = Math.Max(0.05, size / 400.0); } catch { } }
        try { shape.ConstantWidth = Math.Max(0.1, size / 300.0); } catch { }
        _hints.Add(ms.AppendEntity(shape));
        tr.AddNewlyCreatedDBObject(shape, true);
        tr.Commit();
        Log($"점선 — {(guide.IsNull ? "네모" : "그려 둔 선 덧그림")} · 크기 {size:F0} m");
    }

    /// <summary>그려 둔 선이 없을 때 점선 네모(가운데 x · y · 반폭 x · y). 못 정하면 null.</summary>
    (double, double, double, double)? HintRect(Transaction tr)
    {
        if (_step == Step.Crop)
        {
            // ★[JACK 1117] 원지형 자르기 — 방금 만든(가장 새) 원지반 가운데 60 % 네모
            var nid = NewestGround(tr);
            if (nid.IsNull || tr.GetObject(nid, OpenMode.ForRead) is not Autodesk.Civil.DatabaseServices.Surface ns) return null;
            var ng = ns.GetGeneralProperties();
            double w = ng.MaximumCoordinateX - ng.MinimumCoordinateX, h = ng.MaximumCoordinateY - ng.MinimumCoordinateY;
            return ((ng.MinimumCoordinateX + ng.MaximumCoordinateX) / 2, (ng.MinimumCoordinateY + ng.MaximumCoordinateY) / 2, w * 0.3, h * 0.3);
        }
        if (_step == Step.ExDraw)
        {
            if (_planPoly == null) { _hintNote = TutorialFlow.NoteNoGuide; return null; }
            double x0 = double.MaxValue, y0 = double.MaxValue, x1 = double.MinValue, y1 = double.MinValue;
            foreach (var p in _planPoly) { x0 = Math.Min(x0, p.X); y0 = Math.Min(y0, p.Y); x1 = Math.Max(x1, p.X); y1 = Math.Max(y1, p.Y); }
            double hx = Math.Min(15, (x1 - x0) * 0.3), hy = Math.Min(10, (y1 - y0) * 0.3);
            return ((x0 + x1) / 2, (y0 + y1) / 2, hx, hy);
        }
        var gid = GradingBuilder.FindSurfaceByBaseName(tr, ImportGisCommand.GroundSurfaceName);
        if (gid.IsNull || tr.GetObject(gid, OpenMode.ForRead) is not Autodesk.Civil.DatabaseServices.Surface s) return null;
        var gp = s.GetGeneralProperties();
        // ★[JACK 1117] 크기 = 평균 고도로 정지해도 사면(닿는 거리 = 단차 × 실효 구배 + 10 m)이 원지반 안에 드는 가장 큰 네모(150 m 한도)
        var (cx, cy, half, fits) = TutorialFlow.BestHint(gp.MinimumCoordinateX, gp.MinimumCoordinateY, gp.MaximumCoordinateX, gp.MaximumCoordinateY, Gz(s), NEff());
        _hintNote = fits ? null : TutorialFlow.NoteGroundTight;
        return (cx, cy, half, half);
    }

    void EraseHints()
    {
        if ((_hints.Count == 0 && _hidden.Count == 0) || _docGone) return;
        using var lk = _doc.LockDocument();
        using var tr = _db.TransactionManager.StartTransaction();
        foreach (var id in _hints)
            try { if (!id.IsNull && id.IsValid && !id.IsErased) tr.GetObject(id, OpenMode.ForWrite).Erase(); } catch { }
        foreach (var id in _hidden)
            try { if (!id.IsNull && id.IsValid && !id.IsErased && tr.GetObject(id, OpenMode.ForWrite) is Entity en) en.Visible = true; } catch { }
        tr.Commit();
        _hints.Clear();
        _hidden.Clear();
    }

    // ── 단원 끝 — 내 결과 저장(다음 단원 «내 것으로 이어서») ─────────────
    void SaveResult()
    {
        _saved = true;   // 한 번만(실패해도 다시 안 함 — 알림만)
        try
        {
            EraseHints();
            if (string.IsNullOrEmpty(_work)) _work = TutorialData.NewWorkPath(_ch);
            using (_doc.LockDocument())
                _db.SaveAs(_work, true, DwgVersion.Current, _db.SecurityParameters);
            System.IO.Directory.CreateDirectory(TutorialData.Home);
            System.IO.File.Copy(_work, TutorialData.DonePath(_ch), true);
            TutorialData.MarkDone(_ch);
            Log($"결과 저장 — {TutorialData.DonePath(_ch)}");
        }
        catch (System.Exception ex)
        {
            _saved = false;
            _note = TutorialFlow.NoteSaveFail;
            _saveFailed = true;
            TutorialData.MarkDone(_ch);
            Log("결과 저장 실패 — " + ex.Message);
        }
    }
    bool _saveFailed;

    // ── 그리기 ────────────────────────────────────────────────────────────
    static readonly (string, string)[] BtnBase = { ("다시 시작", "restart"), ("그만두기", "quit") };

    void Render(GradingPanel? panel, ExcavPanel? exPanel)
    {
        if (_balloon == null || _glow == null) return;
        var s = _step;
        int n = _ch == Chapter.Road ? TutorialFlow.RoadNum(s, _facts) : TutorialFlow.Num(s, dxf: _part2, graded: _fGrade);
        int total = TutorialFlow.Total(_ch);
        // ★[5단원] 단원 번호 = 여는 차례(종횡단도 4는 쉼 — 진입도로는 4번째)
        string head = $"{Array.IndexOf(TutorialFlow.Active, _ch) + 1}. {TutorialFlow.ChapterName(_ch)} · ";
        string num = s == Step.Done ? head + "완료" : head + $"{n} / {total}";
        bool guide = s == Step.ExDraw ? !_exGuide.IsNull : s == Step.RdDraw ? !_rdGuide.IsNull || _rdPts != null : !_planGuide.IsNull;
        string text = s == Step.Done ? TutorialFlow.DoneText(_ch) : TutorialFlow.Text(s, _part2, guide);
        string? extra = null;
        if (s == Step.Elev && _recZ is double z) extra = $"추천 고도: {z:0.0}";
        if (s is Step.Elev or Step.Build && _fElev && _recZ is double rz && _curZ is double cz && Math.Abs(cz - rz) > 3) extra = $"추천 고도: {rz:0.0} (지금 {cz:0.0})";
        if (s == Step.ExElev && _exRecZ is double ez) extra = $"추천 고도: {ez:0.0}" + (_planZ is double pz ? $" (부지 {pz:0.0} − {pz - ez:0.0} m)" : "");
        if (s == Step.Dxf && TutorialData.DxfFile is string dxf) extra = "파일: " + System.IO.Path.GetFileName(dxf);
        string? note = _note;
        if (IsDraw(s) && _drawNote != null) note = _drawNote;
        else if (IsDraw(s) && _hintNote != null && note == null) note = _hintNote;
        if (s == Step.Elev && _fElev && !_fFits) note = TutorialFlow.NoteEdge;
        if (s == Step.ExElev && _exHigh) note = TutorialFlow.NoteExHigh;
        if (s == Step.ExPick && PickSession.ExcavNoZ(_doc)) note = TutorialFlow.NoteNoZ;
        if (s == Step.Dxf && TutorialData.DxfFile == null) note = TutorialFlow.NoteNoDxf;
        if (s == Step.RdUpdate && _rdLast.Road && !_rdLast.Need) note ??= TutorialFlow.NoteRdUpdateOff;
        var btns = new List<(string, string)>();
        if (s == Step.Ground)
        {
            if (_net == false) { note = TutorialFlow.NoteNet; btns.Add(("다시 확인", "netretry")); btns.Add(("수치지도 DXF로", "dxf")); }
            else if (_netChecking && note == null) note = TutorialFlow.NoteNetChecking;
        }
        if (TutorialFlow.IsMap(s) && _pageDead) note = TutorialFlow.NoteMapDead;
        if (s == Step.Draw && !_planGuide.IsNull) btns.Add(("그려 둔 선 쓰기", "useline"));
        if (s == Step.ExDraw && !_exGuide.IsNull) btns.Add(("그려 둔 선 쓰기", "useline"));
        if (s == Step.Done)
        {
            if (TutorialFlow.Next(_ch) is Chapter nx) btns.Add(($"다음: {TutorialFlow.ChapterName(nx)}", "next"));
            btns.Add(("단원 고르기", "menu"));
            btns.Add(("끝내기", "quit"));
        }
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
        List<FrameworkElement>? targets = null;
        FrameworkElement? root = null;
        if (TutorialFlow.IsPanel(s) && panel != null)
        {
            root = panel.SpotRoot;
            if (s == Step.Wall) panel.ShowFixTab(); else panel.ShowMakeTab();
            targets = s switch
            {
                Step.PickPlan => new() { panel.PickPlanButton },
                Step.PickGround => new() { panel.PickGroundButton },
                Step.Slope => new() { Cell(panel.SlopeCutBox), Cell(panel.SlopeFillBox) },
                Step.Wall => new() { panel.ToWallButton },
                _ => new() { panel.BuildButton },
            };
            // 구배 칸이 접힌 칸 안이면(안 보임) 덮지 않는다 — 덮으면 펼칠 머리도 못 누른다
            if (s == Step.Slope && !targets.Exists(t => t.IsVisible)) { targets = null; note ??= TutorialFlow.NoteOpenSlope; }
        }
        else if (TutorialFlow.IsExPanel(s) && exPanel != null)
        {
            root = exPanel.SpotRoot;
            targets = s switch
            {
                Step.ExPick => new() { exPanel.PickButton },
                Step.ExShore => new() { exPanel.WallCheck },
                _ => new() { exPanel.BuildButton },
            };
        }
        else if (TutorialFlow.IsRdPanel(s) && RoadPalette.OpenPanel is RoadPanel rp)
        {
            // ★[5단원] 진입도로 창 — 누를 단추(안 보이면 안 덮고 알림)
            // ★[JACK 1119] 탭 둘 — 단추가 든 탭으로 돌림(옹벽 넷 = 진입도로 수정 · 그리기 · 포장 = 진입도로 생성 · 초기화 = 아래 줄이라 그대로)
            root = rp.SpotRoot;
            if (s is Step.RdWall or Step.RdSlope or Step.RdWallAdd or Step.RdWallDel) rp.ShowFixTab();
            else if (s is Step.RdDraw or Step.RdPave) rp.ShowMakeTab();
            FrameworkElement? t = s switch
            {
                Step.RdDraw => rp.MakeButton,
                Step.RdWall => rp.WallConvButton,
                Step.RdSlope => rp.SlopeConvButton,
                Step.RdWallAdd => rp.WallAddButton,
                Step.RdWallDel => rp.WallDelButton,
                Step.RdPave => Cell(rp.PaveKindBox),
                Step.RdReset => rp.ResetButton,
                _ => null,
            };
            // [진입도로 초기화]는 아래 줄(DockClose.Wrap) — 창 몸(SpotRoot) 밖이라 바깥 판(덮개 자리 바로 아래)을 덮는다
            if (s == Step.RdReset && t != null)
                for (DependencyObject? q = t; q != null; q = System.Windows.Media.VisualTreeHelper.GetParent(q))
                    if (System.Windows.Media.VisualTreeHelper.GetParent(q) is AdornerDecorator && q is FrameworkElement qf) { root = qf; break; }
            targets = t != null && t.IsVisible ? new() { t } : null;
            if (targets == null && s is Step.RdWall or Step.RdSlope or Step.RdWallAdd or Step.RdWallDel) note ??= TutorialFlow.NoteRdOpenEdit;
        }
        else if (TutorialFlow.IsRdSec(s) && SectionPalette.RoadOpenPanel is SectionPanel sp)
        {
            root = sp.SpotRoot;
            sp.ShowTab(s == Step.RdXsec);   // ★[JACK 1119] 횡단도 생성만 «횡단도 생성» 탭 · 나머지(종단 · 측점 · 지표면 업데이트)는 «종단도 생성» 탭
            string cmd = s switch
            {
                Step.RdProf => "DHROADSHEET", Step.RdPviAdd => "DHROADPVIADD", Step.RdPviDel => "DHROADPVIDEL", Step.RdPviDraw => "DHROADPVIDRAW",
                Step.RdRedraft => "DHROADREDRAFT", Step.RdRev => "DHROADREV", Step.RdXsec => "DHROADXSEC", _ => "DHROADUPDATE",
            };
            var b = sp.RoadButton(cmd);
            targets = b != null && b.IsVisible ? new() { b } : null;
        }
        // ★[JACK 1119] 4단원 종단도 · 횡단도 — 계획지표면 종횡단도 창이 떠 있으면 누를 단추가 든 탭으로(덮개 없음 · 글만인 것은 그대로)
        if ((s is Step.Prof or Step.Xsec) && SectionPalette.SiteOpenPanel is SectionPanel ssp) ssp.ShowTab(s == Step.Xsec);
        Spot(root, targets, n);

        // 리본 빛 테
        Rect? rib = null;
        string? ribText = s switch
        {
            Step.Ground or Step.Dxf => "원지반\n가져오기",
            Step.Crop => "원지형\n자르기",
            Step.Panel or Step.ExPanel => "계획부지\n생성",
            _ => null,
        };
        // ★[5단원] 진입도로 리본 단추 — «종횡단도»는 부지 · 진입도로 둘이라 명령으로 찾음
        string? ribCmd = s switch { Step.RdOpen => "DHROADNEW ", Step.RdSec => "DHROADSECPANEL ", _ => null };
        if (ribText != null || ribCmd != null)
        {
            if (_tabStep != s) { _tabStep = s; TutRibbon.ActivateTab(); }
            var item = ribCmd != null ? TutRibbon.FindItemByCmd(ribCmd) : TutRibbon.FindItem(ribText!);
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

    /// <summary>값 칸이면 그 줄(이름 + 칸)을 비춘다.</summary>
    static FrameworkElement Cell(FrameworkElement tb) => tb.Parent as FrameworkElement ?? tb;

    string PageOn()
    {
        var o = new
        {
            on = true,
            @base = TutorialFlow.Num(Step.Search),
            total = TutorialFlow.Total(Chapter.Ground),
            t = new[] { TutorialFlow.Text(Step.Search, false), TutorialFlow.Text(Step.Pick, false), TutorialFlow.Text(Step.Send, false) },
            skip = "건너뛰기",
            min = TutorialFlow.MinRangeM,
            small = TutorialFlow.NoteSmallRange,
            quit = "그만두기",
        };
        return System.Text.Json.JsonSerializer.Serialize(o);
    }

    void Spot(FrameworkElement? root, List<FrameworkElement>? targets, int num)
    {
        if (targets == null || targets.Count == 0 || root == null) { RemoveSpot(); return; }
        if (_spot != null && ReferenceEquals(_spotRoot, root) && _spot.Targets.Count == targets.Count && SameAll(_spot.Targets, targets)) return;
        RemoveSpot();
        var layer = AdornerLayer.GetAdornerLayer(root);
        if (layer == null) { Log("도킹창 덮개 자리(AdornerLayer) 없음"); return; }
        _spot = new TutSpot(root, targets, num);
        layer.Add(_spot);
        _spotLayer = layer;
        _spotRoot = root;
        root.PreviewKeyDown += OnPanelKey;
        try { targets[0].BringIntoView(); } catch { }
    }

    static bool SameAll(IReadOnlyList<FrameworkElement> a, List<FrameworkElement> b)
    {
        for (int i = 0; i < a.Count; i++) if (!ReferenceEquals(a[i], b[i])) return false;
        return true;
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
        try { NgiiCommand.PresetFile = null; } catch { }
        RemoveSpot();
        try { _glow?.Close(); } catch { }
        try { _balloon?.Close(); } catch { }
        try { MapPalette.TutPost(Off); } catch { }
        // 점선 · 숨긴 선 — 명령 중이면(도면을 못 고침) 끝날 때까지 기다렸다 되돌린다(2분 한도)
        if (!_docGone && (_hints.Count > 0 || _hidden.Count > 0))
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
        Log("끝 — " + why + $" · {(int)_ch}단원 마지막 단계 {_step}" + (_saveFailed ? " · 결과 저장 실패" : ""));
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
