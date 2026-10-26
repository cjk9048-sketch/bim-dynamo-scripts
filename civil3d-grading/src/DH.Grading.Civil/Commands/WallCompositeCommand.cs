using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Civil.DatabaseServices;
using DH.Grading.Core;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace DH.Grading.Civil.Commands;

/// <summary>★★★[v101.0 · JACK 0928 «커밋하고 원지반 뚜껑만들어서 합성하는걸로»] <b>옹벽 합성(DHWALLCOMP)</b>.
/// <para>높이 = 옹벽 폴리곤 안 max(정지면, min(옹벽, 원지반)) · 밖은 정지면 그대로 — Core <see cref="WallDaylight.BuildComposite"/>.
/// Civil은 LandXML을 받기만 한다(0929 현장: 정지면 253,517면 중 1,177면만 다시 · Civil 281,755면 그대로 · 17초).</para>
/// <para>★★★[v101.1 · JACK 0929 <i>«옹벽으로 변환하든 사면으로 변환하든 이 과정 모두가 결국 계획지표면을 생성하고 수정하는
/// 과정이니깐 계획지표면이어야해»</i>] 결과가 곧 <b>정지면_DH</b>다 — v101.0은 따로 합성지표면_DH를 만들었는데 [보기]가
/// 계획지표면 = 정지면_DH로 정해 두고 나머지를 꺼서 합성이 사라졌다. 이제 보기·터파기·InfraWorks가 그대로 합성을 본다
/// (종단·횡단은 순수 정지면을 보므로 다음 단계 — 인터뷰).</para>
/// <para>인터뷰(0929): 합성 전 정지면은 <b>숨겨서 남긴다</b>(<see cref="BaseName"/>) · 옹벽 변환을 다시 돌리면 <b>합성을 자동으로 푼다</b>
/// (<see cref="UndoComposite"/> — 계획부지 생성 «마지막 구역 다시»와 옹벽 변환 시작) · 합성은 다시 누른다.
/// 계획부지 생성 «이어서»는 합성이 새 구역의 기준면에 굳고, «새로»는 다 치운다(합성 전 면은 둘 다 낡음 → 지움).</para>
/// <para>새 면은 <b>임시 이름</b>으로 짓고 되읽기 관문을 지난 뒤에만 바꿔 끼운다(검토 0929 v101 · 높음 1). 순서가 실패 안전을 정한다 —
/// 못 하면 명령 전 상태 그대로다.</para></summary>
public sealed class WallCompositeCommand
{
    /// <summary>계획지표면 — 옹벽 합성 결과가 곧 이 면이다.</summary>
    public const string PlanName = WallDaylightBuilder.PlanSurfaceBase;
    /// <summary>합성 전 정지면(계획부지 생성이 만든 붙여넣기 합성면)을 숨겨 두는 이름 — 옹벽 변환을 다시 돌리면 정지면_DH로 되돌린다.
    /// «_DH»가 들어 있어 원지반 찾기(이름에 _DH가 있으면 뺀다)에서 빠지고, «정지면_DH(_N)»과 이름이 달라 정지면 찾기에도 안 걸린다.</summary>
    public const string BaseName = "정지면_DH합성전";
    /// <summary>v101.0이 따로 만들던 면 — v101.1부터 안 만든다(남아 있으면 치운다).</summary>
    public const string OldCompName = "합성지표면_DH";
    /// <summary>관문 전 이름 — «_DH»가 들어 있어 원지반 찾기에서 빠진다.
    /// 종전 «DH합성_짓는중»은 «_DH»가 없어, 명령이 터져 남으면 원지반 후보가 될 수 있었다(삼각형 28만 &gt; 원지반 25만).</summary>
    public const string TempName = "합성_짓는중_DH";
    public const string OldTempName = "DH합성_짓는중";
    /// <summary>★[계획 검토 v101.1 · 높음 1] <b>합성 표지</b> — 옹벽 합성이 만든 정지면_DH에 확장 사전 XRecord로 붙인다.
    /// <para>새로·이어서가 중간에 멈추거나 합성 전 면을 손으로 지우면 «합성 전 면 없는 합성»이 정지면_DH로 남는다. 그 위에 또 합성하면
    /// 옛 옹벽 머리가 남은 채 판정 1로 조용히 틀린다 — 옹벽 합성(입력)과 옹벽 변환(순수옹벽 관문)이 이 표지를 보고 멈춘다.
    /// 설명 문자열은 사람이 고칠 수 있어 안 쓴다.</para></summary>
    const string TagKey = "DH_WALLCOMP";
    internal static bool IsComposite(Transaction tr, Autodesk.AutoCAD.DatabaseServices.DBObject o)
    {
        try
        {
            if (o.ExtensionDictionary.IsNull) return false;
            return tr.GetObject(o.ExtensionDictionary, OpenMode.ForRead) is DBDictionary d && d.Contains(TagKey);
        }
        catch { return false; }
    }
    /// <param name="baseHandle">합성 전 면(<see cref="BaseName"/>)의 핸들 — 풀기·다시 합성이 «이 합성의 합성 전 면인가»를 본다(코드 검토 v101.1 · 중간 1).
    /// 핸들은 도면을 닫았다 열어도 같다(ObjectId는 아니다).</param>
    /// <param name="nos">★[v103.0] 든 옹벽 번호(«1,2,4») — 셋째 값. 없으면 v102 표지(옮길 한 벌 합성).</param>
    /// <param name="nonce">★★[v103.2 · 계획 4판] 이 합성의 일회용 번호 — 넷째 값(번호가 있을 때만). 옹벽 칸의 <see cref="WallRec.CompNonce"/>가 이것과 같으면 그 칸의 뚜껑이 지금 최종면의 것이다.</param>
    static void MarkComposite(Transaction tr, Autodesk.AutoCAD.DatabaseServices.DBObject o, string info, string baseHandle, string nos = "", string nonce = "")
    {
        if (!o.IsWriteEnabled) o.UpgradeOpen();
        if (o.ExtensionDictionary.IsNull) o.CreateExtensionDictionary();
        var d = (DBDictionary)tr.GetObject(o.ExtensionDictionary, OpenMode.ForWrite);
        if (d.Contains(TagKey)) throw new System.InvalidOperationException("새로 지은 면에 합성 표지가 벌써 있다");
        var rb = new ResultBuffer(new TypedValue((int)DxfCode.Text, info), new TypedValue((int)DxfCode.Text, baseHandle));
        if (!string.IsNullOrEmpty(nos))
        {
            rb.Add(new TypedValue((int)DxfCode.Text, nos));
            if (!string.IsNullOrEmpty(nonce)) rb.Add(new TypedValue((int)DxfCode.Text, nonce));
        }
        var xr = new Xrecord { Data = rb };
        d.SetAt(TagKey, xr);
        tr.AddNewlyCreatedDBObject(xr, true);
    }
    /// <summary>표지에 적힌 합성 전 면 핸들 — 없으면 빈 문자열(짝 확인에서 «어긋남»이 된다).</summary>
    /// <summary>★[v103.1] 지금 합성 전 면(P0) 핸들 — 정지면_DH가 합성이면 그 표지가 가리키는 합성 전 면 · 아니면 정지면_DH. 정지면_DH가 하나가 아니면 빈 값.</summary>
    internal static string CurrentP0Handle(Transaction tr, ObjectId groundId)
    {
        try
        {
            var civilDoc = Autodesk.Civil.ApplicationServices.CivilApplication.ActiveDocument;
            var (cIds, _) = Candidates(civilDoc, tr, PlanName, groundId);
            if (cIds.Count != 1) return "";
            var cur = tr.GetObject(cIds[0], OpenMode.ForRead);
            return IsComposite(tr, cur) ? CompositeBaseHandle(tr, cur) : cIds[0].Handle.ToString();
        }
        catch { return ""; }
    }

    /// <summary>★★★[v103.1 · JACK 1002 «부분 변환한 곳은 반대로 돌리면 그 모양으로»] 고른 선이 덮은 옹벽을 사면으로 되돌린다 — 결과물을 지우고 목록에서 뺀다.
    /// DoGrade 들머리가 합성을 푼 <b>같은 트랜잭션</b>에서 부른다(합성에서 빠지는 것과 목록에서 빠지는 것이 함께 커밋 · 함께 되돌림 — 계획 v103.1 3판 2-3).
    /// 목록을 못 읽으면 거짓(덮어쓰지 않고 멈춘다).</summary>
    internal static (string Note, bool Ok) RevertCoveredWalls(Database db, Transaction tr, int[] covered, string why, string region, ObjectId groundId)
    {
        if (covered == null || covered.Length == 0) return ("", true);
        if (!WallListStore.TryLoad(db, tr, out var head, out var walls, out string lwhy)) return ($"⚠옹벽 목록을 못 읽어 옹벽을 되돌리지 못했다 — {lwhy}", false);
        var nos = new System.Collections.Generic.HashSet<int>(covered);
        var hit = walls.Where(w => nos.Contains(w.No)).Select(w => $"{w.No}({w.Side} {w.Bench + 1}단)").ToList();
        if (hit.Count == 0) return ("", true);
        var er = WallTags.Erase(db, tr, (n, r) => nos.Contains(n) && (r.Length == 0 || r == region), false, groundId);
        walls.RemoveAll(w => nos.Contains(w.No));
        WallListStore.Save(db, tr, head, walls);
        return ($"옹벽 {string.Join(",", hit)}을 사면으로 되돌렸다({why} · 지움 {er.Text}) · 남은 옹벽 {walls.Count(w => w.State == 1)}개", true);
    }

    internal static string CompositeBaseHandle(Transaction tr, Autodesk.AutoCAD.DatabaseServices.DBObject o)
    {
        try
        {
            if (o.ExtensionDictionary.IsNull) return "";
            if (tr.GetObject(o.ExtensionDictionary, OpenMode.ForRead) is not DBDictionary d || !d.Contains(TagKey)) return "";
            if (tr.GetObject(d.GetAt(TagKey), OpenMode.ForRead) is not Xrecord xr || xr.Data == null) return "";
            var a = xr.Data.AsArray();
            return a.Length >= 2 && a[1].Value is string h ? h : "";
        }
        catch { return ""; }
    }
    /// <summary>★★[v103.2] 합성 표지의 일회용 번호(넷째 값) — 표지가 없거나 v103.2 전 합성이면 빈 문자열.</summary>
    internal static string CompositeNonce(Transaction tr, Autodesk.AutoCAD.DatabaseServices.DBObject o)
    {
        try
        {
            if (o.ExtensionDictionary.IsNull) return "";
            if (tr.GetObject(o.ExtensionDictionary, OpenMode.ForRead) is not DBDictionary d || !d.Contains(TagKey)) return "";
            if (tr.GetObject(d.GetAt(TagKey), OpenMode.ForRead) is not Xrecord xr || xr.Data == null) return "";
            var a = xr.Data.AsArray();
            return a.Length >= 4 && a[3].Value is string n ? n : "";
        }
        catch { return ""; }
    }
    /// <summary>★[v103.0] 합성 표지에 적힌 <b>든 옹벽 번호</b> — 표지가 없으면 null · v102까지의 표지(셋째 값 없음)면 빈 배열(옮길 한 벌 합성).</summary>
    internal static int[]? CompositeWallNos(Transaction tr, Autodesk.AutoCAD.DatabaseServices.DBObject o)
    {
        try
        {
            if (o.ExtensionDictionary.IsNull) return null;
            if (tr.GetObject(o.ExtensionDictionary, OpenMode.ForRead) is not DBDictionary d || !d.Contains(TagKey)) return null;
            if (tr.GetObject(d.GetAt(TagKey), OpenMode.ForRead) is not Xrecord xr || xr.Data == null) return null;
            var a = xr.Data.AsArray();
            if (a.Length < 3 || a[2].Value is not string s) return System.Array.Empty<int>();
            var l = new System.Collections.Generic.List<int>();
            foreach (var part in s.Split(',', System.StringSplitOptions.RemoveEmptyEntries)) if (int.TryParse(part.Trim(), out int n) && n > 0) l.Add(n);
            return l.ToArray();
        }
        catch { return null; }
    }

    /// <summary>합성 전 면 없이 남은 합성에서 빠져나가는 길 — 정지면을 통째로 다시 짓는 것만 늘 된다
    /// (옹벽 구간이 남아 있으면 옹벽·사면 변환은 폴리곤만 만들고 정지면을 다시 짓지 않는다 — CreateGradingCommand «폴리곤만»).</summary>
    internal const string B1Help = "계획부지 생성 «새로 시작»으로 정지면을 다시 지은 뒤 옹벽 변환을 다시 하세요";
    /// <summary>짝이 어긋난 합성 전 면을 치우는 길.</summary>
    internal const string PairHelp = "합성 전 면을 치우려면 계획부지 생성 «새로 시작»";
    /// <summary>바꿔 끼우기를 시작한 뒤의 실패 — 던져서 트랜잭션째 되돌린다(코드 검토 v101.1 · 낮음 3: 돌려주고 커밋하면 반쯤 바꾼 것이 굳는다).</summary>
    sealed class SwapFailed : System.Exception { public SwapFailed(string m) : base(m) { } }
    /// <summary>이번 실행이 Civil에 만든 새 면 — 되돌린 뒤에도 남았으면(가져오기가 트랜잭션 밖에서 만들었을 때) 이름과 상관없이 지운다.</summary>
    static ObjectId _made = ObjectId.Null;
    /// <summary>★[v103.0] 이번 실행이 바꿔 끼우기까지 갔다(⚠가 있어도 합성은 됐다) — 알림창 문구를 가른다.</summary>
    static bool _lastOk;
    /// <summary>★[v103.0 · 재검토 낮음 4] 이번 실행이 버린 «대기» 옹벽 번호 — 알림창이 «DHWALLCOMP»가 아니라 «옹벽 변환을 다시»를 권하게(버린 옹벽은 합성으로 안 돌아온다).</summary>
    static int _rejectedNo;
    /// <summary>★[v103.1] 이번 실행이 새 정지면에 다시 지은 옹벽 수 · 보류 글 · 새 합성(토량을 다시 잴 때)</summary>
    static int _rebuiltN;
    static string _heldNote = "";
    internal const string RebuildFailTag = "다시 짓기 실패";   // ★[v103.1.7] 되돌리기만 길도 이 보류를 «다시 시도할 옹벽»으로 센다(CreateGradingCommand.FinishRevertOnly)
    /// <summary>★[v103.1 · 코드 검토 낮음 4] 이번 실행이 다시 지은 순수옹벽 — 합성이 터져 트랜잭션이 되돌려져도 LandXML로 따로 생긴 면은 남을 수 있어 그때 지운다.</summary>
    static readonly System.Collections.Generic.List<ObjectId> _rebuiltPure = new();
    static ObjectId _compId = ObjectId.Null, _compGround = ObjectId.Null;

    // ══ ★★★[v103.1.12 · JACK 1014 «합성 미루기» · 1015 ① «그 옹벽만 보류(지우지 않음) · 나머지는 합성»] 합성 한 번 = 여러 «회» ══
    //   계획 4판 H1: 미룬 옹벽(«넣음» · 합성에 든 적 없음)은 State 1이라 종전 출구 규칙(«가장 새 대기» = State 0)이 못 본다 →
    //   회마다 «새» 옹벽(탓 후보)을 인자로 준다. State는 메모리에서도 안 바꾼다. 모든 회는 한 명령 · 한 트랜잭션(RunInner).
    /// <summary>한 회의 범위와 결과.</summary>
    sealed class Attempt
    {
        /// <summary>이 번호만 넣는다(null = 목록 전부).</summary>
        public System.Collections.Generic.HashSet<int>? Only;
        /// <summary>이번 회에 든 «새» 옹벽 전부(대기 · 미룬 옹벽) — 바꿈(ReplacedBy)을 이것으로 센다(4판 H2).</summary>
        public System.Collections.Generic.HashSet<int> NewIn = new();
        /// <summary>못 하면 탓을 돌릴 후보(하나씩 회에는 하나).</summary>
        public System.Collections.Generic.HashSet<int> Blame = new();
        /// <summary>이번 명령에서 이미 보류 · 버린 옹벽 — 안 넣는다.</summary>
        public System.Collections.Generic.HashSet<int> Skip = new();
        /// <summary>⑦ 되읽기 관문까지만 — 임시 면을 지우고 «됨»(바꿔 끼우기 · 목록 · 보이기 안 함).</summary>
        public bool Probe;
        // 결과
        public bool Ok, Empty;
        /// <summary>아는 탓(0 = 모름) · 탓이 새 것 가운데(누군지 모름) — 둘 다 아니면 «전체» 못 함(종전대로 멈춤).</summary>
        public int Culprit;
        public bool AmongNew;
        public string Why = "";
        public bool Global => !Ok && !Empty && Culprit == 0 && !AmongNew;
    }
    /// <summary>★[v103.1.12 · 4판 중간 5] 이번 명령에서 새 정지면에 다시 짓기를 못 한 옹벽 — 다음 회에서 뺀다(회마다 30초씩 다시 짓지 않게).</summary>
    static readonly System.Collections.Generic.HashSet<int> _rebuildFailedRun = new();
    /// <summary>★[v103.1.12] 이번 명령이 보류한 미룬 옹벽 수 — 알림창 글을 가른다(버린 옹벽 글과 다르다 · 4판 낮음 3).</summary>
    static int _heldThisRun;
    /// <summary>★[v103.1.12] 관문이 부른 합성 — 알림창 · 토량 표시를 안 건드린다(관문이 한 번 묻는다 · 4판 낮음 4).</summary>
    static bool _quiet;
    static string _lastSummary = "";
    /// <summary>★[v103.1.12] 이번 합성에 미룬 옹벽이 들었다 — 합성 뒤 토량을 늘 잰다.</summary>
    static bool _hadDeferred;
    /// <summary>★[v103.1.12 · 코드 검토 낮음 4] 손으로 친 합성(옹벽 변환이 이어 태운 것 · 관문이 부른 것 아님) — «합성 실패» 보류의 열쇠를 안 보고 다시 해 본다.</summary>
    static bool _manualRetry;
    /// <summary>★[v103.1.12 · 4판 재검토 새 높음 1] 이번 합성이 목록에서 <b>지운</b> 옹벽(버린 대기 · 면이 없어진 넣음) — 관문이 «빠짐»이 비어도 묻게 한다.</summary>
    static string _lostNote = "";
    internal static string LastLost => _lostNote;
    /// <summary>★[v103.1.12] 화면 없는 Civil 시험 칸(BuildOnce ⑦) — 0이면 아무 일 없음.</summary>
    internal static int _failGateForNoTest;

    /// <summary>★★★[v103.1] 정지면을 다시 지은 뒤 옹벽을 <b>같은 번호 · 저장한 입력</b>으로 다시 짓는다(옹벽 더하기와 같은 <see cref="CreateGradingCommand.BuildWallInPolygon"/>).
    /// 그 번호 결과물을 지우고(번호 + 구역) 폴리곤 선부터 다시 그린다(그 함수는 폴리곤 선을 안 그린다 · 검토 v103.1 중간 4). 못 지었거나 합성을 막을 까닭이 생기면 그 까닭(빈 값 = 됐다).
    /// 판 1 옹벽(지은 값 모름)은 지금 값으로 짓고 로그에 적는다(검토 v103.1 중간 5).</summary>
    static string RebuildWall(Database db, Transaction tr, WallRec w, string region, IGroundSurface ground, ObjectId groundId, System.Text.StringBuilder log)
    {
        if (w.Poly == null || w.Poly.Count < 3) return "저장한 폴리곤이 없다";
        if (w.IsWall == null || w.IsWall.Count != w.Poly.Count) return $"변 나누기 표가 폴리곤과 안 맞는다({w.IsWall?.Count ?? 0}/{w.Poly.Count})";
        var er = WallTags.Erase(db, tr, (n, r) => n == w.No && (r.Length == 0 || r == region), false, groundId);
        var p = GradingSettings.ToParams();
        bool v1 = w.SlopeW < 0 || w.FaceRun < 0 || w.Grid < 0;
        double sW = w.SlopeW >= 0 ? w.SlopeW : System.Math.Max(GradingSettings.MinSlope, 0);
        double fR = w.FaceRun >= 0 ? w.FaceRun : p.MinFaceRun;
        double gr = w.Grid >= 0 ? w.Grid : p.VertexSpacing;
        GradingSettings.ClearLastWall(true);
        GradingSettings.WallPolyIsWall = new System.Collections.Generic.List<bool>(w.IsWall);
        WallDaylightBuilder.Summaries.Clear();
        string txt;
        try
        {
            CreateGradingCommand.DrawWallPolygon(db, tr, w.Poly, w.No, region);
            txt = CreateGradingCommand.BuildWallInPolygon(db, tr, w.Poly, w.HeadZ, w.Up, p, ground, groundId, w.No, region, w.H, w.T, sW, fR, gr);
        }
        catch (System.Exception ex) { return $"다시 짓다 터졌다({ex.GetType().Name}: {ex.Message})"; }
        finally { GradingSettings.WallPolyIsWall = null; }
        log.AppendLine($"   옹벽 {w.No}({w.Side} {w.Bench + 1}단) 다시 짓기 — 지운 것 {er.Text}"
            + (v1 ? $" · 판 1 옹벽이라 지은 값을 지금 값으로(앞면 1:{sW:0.###} · 최소 너비 {fR:0.###}m · 간격 {gr:0.##}m)" : ""));
        foreach (var line in System.Text.RegularExpressions.Regex.Replace(txt, "<[^>]+>", "").Split('\n'))
            if (line.Trim().Length > 0) log.AppendLine("      " + line.Trim());
        bool built = !GradingSettings.LastWallInvalid && GradingSettings.LastWallPoly != null && GradingSettings.LastWallSurfHandle.Length > 0;
        if (!built)
            return "데이라잇까지 못 지었다" + (WallDaylightBuilder.Summaries.Count > 0 ? " — " + string.Join(" · ", WallDaylightBuilder.Summaries.Where(x => x.Contains("⚠")).Take(3)) : "");
        w.WallHandle = GradingSettings.LastWallSurfHandle;
        try { var pid = GradingBuilder.FindSurfaceByBaseName(tr, WallRec.PureSurfaceName(w.No)); w.PureHandle = pid.IsNull ? "" : pid.Handle.ToString(); if (!pid.IsNull) _rebuiltPure.Add(pid); } catch { }
        w.Block = GradingSettings.LastWallPlanNote ?? "";
        if (v1) { w.SlopeW = sW; w.FaceRun = fR; w.Grid = gr; }
        if (w.Block.Length > 0) return "합성을 막을 까닭이 생겼다 — " + w.Block;
        return "";
    }

    const string LogName = "DHWALLCOMP_진단.log";
    const string DumpName = "DHWALLCOMP_입력.txt";

    [CommandMethod("DHWALLCOMP")]
    public void Run()
    {
        // ★[v103.0.1 · JACK 1002 스샷 «저런 정보창»] 계획부지 생성처럼 이벤트 뷰어 <b>자동 알림만</b> 끈다(기록은 남는다 · EventViewerMute) —
        //   합성 가져오기(LandXML)가 남기는 정보 한 줄(«V1.1 이전 알고리즘 … 인접이 없습니다»)에 창이 떴다. 옹벽 변환이 이어 태우는 명령이라 DoGrade의 끄기 밖이었다
        var evPrev = EventViewerMute.Begin();
        // ★★★[v103.1.7 · JACK 1014 «현장확인 말고 로그로»] 자가점검 — 합성 한 번 = 판정 한 줄(DH_자가점검.log) · Civil이 도중에 죽으면 진행 표지가 남는다
        var docJ = AcadApp.DocumentManager.MdiActiveDocument;
        using var job = new SelfCheckLog.Job("옹벽 합성", docJ?.Name ?? "");
        _job = job;
        _issuesBefore = SelfCheck.IssuesNow(docJ?.Database);   // ★[v103.1.7 · 검토 낮음 6] 작업 앞 상태(새로 생긴 문제만 ⚠로)
        _quiet = false;
        // ★[v105.1.1 · JACK 1023] 로딩 창 — 옹벽 변환이 이어 태운 합성 · [옹벽 합성] 단추 모두(관문 합성 RunFor는 관문이 띄운다 · 종단이 띄운 창이면 그 창을 쓴다)
        using var busy = BusyPopup.Scope(CreateGradingCommand.BusyText);
        try { if (docJ != null) RunInner(docJ); }
        catch (System.Exception ex) { job.Done(false, $"예외 — {ex.GetType().Name}: {ex.Message}"); throw; }
        finally
        {
            EventViewerMute.End(evPrev); _job = null; _issuesBefore = null;
            // ★[v103.2 · 계획 2-6] 그려 둔 종단 · 횡단이 옛 면 기준이면 한 줄 — 관문이 부른 합성(RunFor)은 곧 종단을 그리므로 안 알린다
            if (docJ != null) PureWithWallsBuilder.NotifyStale(docJ);
        }
    }

    /// <summary>★★★[v103.1.12 · 계획 4판 2-5 · 낮음 4] 관문이 부르는 합성 — 명령(Run)과 같은 껍데기(이벤트 뷰어 끄기 · 자가점검 작업 · 앞 상태)로 같은 RunInner를 돌린다.
    /// 알림창은 안 띄우고(관문이 한 번 묻는다) · 토량 표시(재생성이 켜 둔 것)는 안 꺼낸다 · 옹벽 변환이 이어 태운 표시도 안 건드린다. 명령 안(문서 잠금 가능)에서 부른다 — 트랜잭션 밖.</summary>
    internal static (bool Ok, string Summary) RunFor(Document doc, string caller)
    {
        var evPrev = EventViewerMute.Begin();
        using var job = new SelfCheckLog.Job($"옹벽 합성(관문 — {caller})", doc?.Name ?? "");
        _job = job;
        _issuesBefore = SelfCheck.IssuesNow(doc?.Database);
        _quiet = true;
        _lastSummary = "";
        try { if (doc != null) RunInner(doc); }
        catch (System.Exception ex) { job.Done(false, $"예외 — {ex.GetType().Name}: {ex.Message}"); return (false, $"예외 — {ex.GetType().Name}: {ex.Message}"); }
        finally
        {
            EventViewerMute.End(evPrev); _job = null; _issuesBefore = null; _quiet = false;
            // ★[v103.2 · 코드 검토 낮음 5] 종단을 곧 그리는 관문(종단도 · 종단/횡단) · 정지면 생성이 끝에서 알리는 «이어서»가 아니면 여기서 «옛 면 기준» 한 줄(InfraWorks · 터파기 앞 합성)
            if (doc != null && caller != "종단도" && caller != "종단/횡단" && caller != "이어서") PureWithWallsBuilder.NotifyStale(doc);
        }
        return (_lastOk, _lastSummary);
    }

    /// <summary>★★[v103.1.12] 지금 정지면_DH와 옹벽 목록의 합성 셈(Core <see cref="WallList.CompositeState"/> — 하네스 S158). 목록을 못 읽으면 null(까닭).
    /// 정지면_DH가 하나가 아니거나 합성이 아니면 표지 없음(대상 전부가 «표지에 없음»).</summary>
    internal static WallList.CompState? StateNow(Database db, Transaction tr, out string why) => StateNow(db, tr, out why, out _, out _, out _);
    internal static WallList.CompState? StateNow(Database db, Transaction tr, out string why, out string p0, out int[]? tagNos, out System.Collections.Generic.List<WallRec> walls)
    {
        why = ""; p0 = ""; tagNos = null; walls = new();
        if (!WallListStore.TryLoad(db, tr, out var head, out var ws, out string lwhy)) { why = "옹벽 목록을 못 읽었다 — " + lwhy; return null; }
        walls = ws;
        var gid = NoriCommand.FindByHandle(db, head.GroundHandle);
        try
        {
            var (ids, _) = Candidates(Autodesk.Civil.ApplicationServices.CivilApplication.ActiveDocument, tr, PlanName, gid);
            if (ids.Count == 1)
            {
                var o = tr.GetObject(ids[0], OpenMode.ForRead);
                bool comp = IsComposite(tr, o);
                tagNos = comp ? CompositeWallNos(tr, o) : null;
                p0 = comp ? CompositeBaseHandle(tr, o) : ids[0].Handle.ToString();
            }
            else why = $"{PlanName}가 {ids.Count}개";
        }
        catch (System.Exception ex) { why = $"{PlanName}를 못 읽었다 — {ex.GetType().Name}"; }
        return WallList.CompositeState(ws, tagNos, p0);
    }

    /// <summary>★[v103.1.7] 지금 도는 합성의 자가점검 작업(<see cref="Run"/>이 만든다).</summary>
    static SelfCheckLog.Job? _job;
    /// <summary>★[v103.1.7] 그 합성 앞의 우리 지표면 문제(이름 → 까닭).</summary>
    static System.Collections.Generic.Dictionary<string, string>? _issuesBefore;

    /// <summary>★[v103.1.7 · 검토 1012 재검토 중간 1] 지금 정지면_DH의 토량 — 합성을 못 했을 때의 «옹벽 미합성 토량». 원지반은 합성과 같은 차례로 찾는다(목록 머리 → 옹벽 변환이 쓴 것 → 이름).</summary>
    static string VolumeNow(Database db)
    {
        ObjectId groundId = ObjectId.Null, planId = ObjectId.Null; bool stale = false;
        using (var tr = db.TransactionManager.StartTransaction())
        {
            if (WallListStore.TryLoad(db, tr, out var head, out _, out _)) groundId = NoriCommand.FindByHandle(db, head.GroundHandle);
            if (groundId.IsNull) groundId = NoriCommand.FindByHandle(db, GradingSettings.LastGroundHandle);
            if (groundId.IsNull) groundId = ImportGisCommand.FindGroundSurface(db, out _, out _);
            if (!groundId.IsNull)
            {
                var (ids, _) = Candidates(Autodesk.Civil.ApplicationServices.CivilApplication.ActiveDocument, tr, PlanName, groundId);
                if (ids.Count == 1) { planId = ids[0]; try { stale = ((Autodesk.Civil.DatabaseServices.Surface)tr.GetObject(planId, OpenMode.ForRead)).IsOutOfDate; } catch { } }
            }
            tr.Commit();
        }
        if (groundId.IsNull) return "원지반을 못 찾아 재지 않았다";
        if (planId.IsNull) return $"{PlanName}를 하나로 못 잡아 재지 않았다";
        // ★[v103.1.7 · 검토 낮음 5] 낡은 면(일부러 재작성하지 않은 면일 수 있다) 위에는 체적면을 만들지 않는다 — 체적면이 그 면을 다시 짓게 할 수 있다
        if (stale) return $"{PlanName}가 낡아(Out of date) 재지 않았다";
        string vol = CreateGradingCommand.ComputeVolumes(db, groundId, planId);
        // 체적면을 만들고 지우면 정지면이 «구식»이 될 수 있다 — 다른 길처럼 마무리 재작성(정지면 생성 끝 · 되돌리기만과 같은 함수)
        try { GradingBuilder.RebuildSurfacesStaged(db); } catch { }
        return vol;
    }

    static void RunInner(Document doc)
    {
        if (doc == null) return;
        GradingSettings.SyncToDocument(doc);
        var ed = doc.Editor; var db = doc.Database;
        var log = new System.Text.StringBuilder();
        string dir = System.IO.Path.GetDirectoryName(DiagLog.FilePath) ?? ".";
        string logPath = System.IO.Path.Combine(dir, LogName);
        log.AppendLine($"[DHWALLCOMP 진단] {System.DateTime.Now:yyyy-MM-dd HH:mm:ss} {GradingSettings.Version} · 도면 {db.Filename}");
        string summary;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        bool crashed = false;
        // ★[v103.1.4] 합성 뒤 토량을 재라는 표시 — 꺼내면서 비운다(정지면을 다시 안 만든 되돌리기 · 합성 전 토량을 생략한 재생성이 켠다)
        // ★[v103.1.7 · 검토 1012 재검토 낮음 2] <b>같은 도면일 때만</b> 꺼낸다 — 다른 도면에서 켠 표시는 그대로 둔다(그 도면의 다음 합성이 쓴다)
        // ★[v103.1.12 · 4판 낮음 4] 관문이 부른 합성(_quiet)은 이 표시를 안 꺼낸다 — 재생성이 켠 «합성 뒤 토량»은 그 재생성이 줄 세운 합성이 쓴다
        bool wantVol = !_quiet && GradingSettings.AutoCompWantVolume && string.Equals(GradingSettings.AutoCompVolDoc, doc.Name, System.StringComparison.OrdinalIgnoreCase);
        if (wantVol) { GradingSettings.AutoCompWantVolume = false; GradingSettings.AutoCompVolDoc = ""; }
        _lastOk = false; _rejectedNo = 0;
        _made = ObjectId.Null;
        _rebuiltN = 0; _heldNote = ""; _compId = ObjectId.Null; _compGround = ObjectId.Null; _rebuiltPure.Clear();
        _rebuildFailedRun.Clear(); _heldThisRun = 0; _hadDeferred = false; _lostNote = "";
        _manualRetry = !_quiet && !(GradingSettings.AutoCompDoc.Length > 0 && GradingSettings.AutoCompDoc == doc.Name);
        try
        {
            using (doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                summary = Build(db, tr, log, dir);
                // 못 한 길(Fail)도 커밋한다 — Fail은 바꿔 끼우기 전에만 나오고 남는 것은 «새 면 지움»뿐이다.
                //   바꿔 끼우기를 시작한 뒤의 실패는 SwapFailed로 던져 트랜잭션째 되돌린다
                tr.Commit();
            }
        }
        catch (SwapFailed sf)
        {
            crashed = true; _lastOk = false; _rejectedNo = 0;   // ★[검증 낮음] 되돌렸으면 «버렸다»가 아니다 — 대기 옹벽은 그대로 남는다
            _heldThisRun = 0; _lostNote = "";   // ★[v103.1.12 · 코드 검토 중간 1] 앞 회의 보류 · 버림도 통째로 되돌아갔다 — 알림창 · 관문이 «보류했다 · 뺐다»고 말하지 않게
            summary = $"⚠옹벽 합성 못 함 — {sf.Message} · 바꿔 끼우기를 통째로 되돌렸다(정지면_DH·보이기는 명령 전 그대로)";
            log.AppendLine(sf.ToString());
        }
        catch (System.Exception ex)
        {
            crashed = true; _lastOk = false; _rejectedNo = 0;
            _heldThisRun = 0; _lostNote = "";   // ★[v103.1.12 · 코드 검토 중간 1]
            summary = $"⚠옹벽 합성 못 함 — {ex.GetType().Name}: {ex.Message} · 이번 명령이 바꾼 것은 되돌렸다(정지면_DH·보이기는 명령 전 그대로)";
            log.AppendLine(ex.ToString());
        }
        // 트랜잭션을 되돌려도 CreateFromLandXML이 따로 만든 임시 면은 남을 수 있다 — 이름으로 찾아 지운다
        if (crashed)
        {
            try
            {
                using (doc.LockDocument())
                using (var tr2 = db.TransactionManager.StartTransaction())
                {
                    int n = EraseTemp(tr2);
                    try
                    {
                        if (!_made.IsNull && _made.IsValid && !_made.IsErased) { tr2.GetObject(_made, OpenMode.ForWrite).Erase(); n++; }
                    }
                    catch (System.Exception ex3) { log.AppendLine("새 면 치우기 실패: " + ex3.GetType().Name); }
                    // ★[v103.1 · 코드 검토 낮음 4] 되돌려졌는데 남은 다시 지은 순수옹벽(목록은 옛 핸들로 돌아갔다)
                    foreach (var pid in _rebuiltPure)
                        try { if (pid.IsValid && !pid.IsErased) { tr2.GetObject(pid, OpenMode.ForWrite).Erase(); n++; } } catch { }
                    // ★[v103.1.5 · 검토 1012 낮음 3] 되돌려지면 본 트랜잭션의 굳힘도 되돌아간다(옛 판 파일 이름 바꾸기는 안 되돌아간다) — 옛 면을 여기서 다시 굳힌다
                    try { string fz2 = GradingBuilder.FreezeAllXmlBacked(tr2); if (fz2.Length > 0) log.AppendLine("되돌린 뒤 굳힘: " + fz2); } catch { }
                    // ★[검토 1012 2차 낮음 1] 되돌려져 살아난 면의 원본에 «안 물림»이 적혀 있다(되돌려진 트랜잭션 안에서 적은 것) — 다시 훑어 그 기록을 지운다
                    try { string td2 = GradingBuilder.TidyXml(db, tr2); if (td2.Length > 0) log.AppendLine("되돌린 뒤 " + td2); } catch { }
                    tr2.Commit();
                    if (n > 0) summary += $" · 남은 임시 면 {n}개 지움";
                }
            }
            catch (System.Exception ex2) { log.AppendLine("임시 면 치우기 실패: " + ex2); }
        }
        // ★[v103.1 · 검토 v103.1 중간 7] 정지면을 다시 지은 뒤 옹벽을 다시 합성했으면 토량을 다시 잰다(계획부지 생성 팝업 토량은 옹벽 합성 전)
        // ★[v103.1.4] 정지면을 다시 안 만들고 남은 옹벽만 다시 합성했을 때(되돌리기만)도 잰다 — 그때는 옹벽을 다시 짓지 않아 «다시 지은 옹벽 수»가 0이다.
        //   재생성 쪽은 합성 전 토량을 생략했으므로(v103.1.4 C) 이 값이 그 실행의 <b>하나뿐인</b> 토량이다 — 못 쟀으면 그렇다고 적는다
        string volAfter = "";
        if (_lastOk && (_rebuiltN > 0 || wantVol || _hadDeferred) && !_compId.IsNull && !_compGround.IsNull)
        {
            try { volAfter = CreateGradingCommand.ComputeVolumes(db, _compGround, _compId); summary += " · 옹벽 합성 뒤 " + volAfter.Replace("\n", " "); }
            catch (System.Exception vx) { summary += $" · ⚠옹벽 합성 뒤 토량을 못 쟀다({vx.GetType().Name})"; log.AppendLine("토량 다시 재기 실패: " + vx.GetType().Name); }
        }
        else if (wantVol && !_lastOk)
        {
            // ★[검토 1012 중간] 표시를 <b>다시 켜 둔다</b> — 다시 짓기는 이미 목록에 저장돼(Fail도 커밋) 다음 합성은 «다시 지은 옹벽 0»이고,
            //   되돌리기만 길은 애초에 옹벽을 다시 안 짓는다. 안 켜 두면 손으로 다시 합성해 성공해도 토량이 끝내 안 나온다
            GradingSettings.WantVolumeAfterComposite(doc.Name);
            // ★★[v103.1.7 · 검토 1012 재검토 중간 1 — 미뤘던 것] 합성이 계속 실패하면 그 재생성의 토량이 끝내 안 나왔다 → 지금 정지면_DH(옹벽 미합성)로 한 번 재 적는다
            string vNow = "";
            try { vNow = VolumeNow(db); } catch (System.Exception vx) { vNow = $"못 쟀다({vx.GetType().Name})"; }
            summary += " · 옹벽 합성 뒤 토량은 아직 없다(합성을 못 했다 — 다시 합성하면 나온다)"
                     + (vNow.Length > 0 ? " · 옹벽 미합성 토량(지금 정지면_DH): " + vNow.Replace("\n", " ") : "");
        }
        summary += $" · 전체 {sw.ElapsedMilliseconds / 1000.0:F1}초";
        // ★[v102.2 · 계획 검토 중간 8] 옹벽 변환이 이어서 태운 합성이 못 했으면 알림창 — 버튼이 없어져 명령줄 한 줄로는 묻힌다
        // ★[v103.1.12 · 4판 낮음 4] 관문이 부른 합성은 옹벽 변환이 이어 태운 표시를 안 꺼낸다(관문 뒤에 줄 선 합성이 쓴다)
        bool auto = !_quiet && GradingSettings.AutoCompDoc.Length > 0 && GradingSettings.AutoCompDoc == doc.Name;
        int autoNo = GradingSettings.AutoCompNo;
        if (!_quiet) { GradingSettings.AutoCompDoc = ""; GradingSettings.AutoCompNo = 0; }
        // ★[v103.0] 쌓기 — 못 하면 새 옹벽만 빠지고 지난 옹벽이 든 정지면_DH는 그대로다
        // ★[v103.0 · 코드 검토 중간 3] 합성은 됐는데 넣은 옹벽이 빠졌으면(⚠) 손으로 친 합성이어도 알린다 — 명령줄 한 줄로는 묻힌다
        // ★[v103.1.7 · v103.0 코드 검토 낮음 — 미뤘던 것] 손으로 친 합성이 새 옹벽을 <b>버렸을 때</b>도 알림창 — 버린 옹벽은 돌아오지 않는데 명령줄 한 줄로는 묻힌다
        // ★[v103.1.12] 관문이 부른 합성(_quiet) · 화면 없는 Civil은 알림창을 안 띄운다(관문이 한 번 묻는다 · 화면 없는 Civil은 창을 띄우면 죽는다)
        bool canAlert = !_quiet && !SelfCheckLog.Headless;
        if (canAlert && ((auto && (crashed || summary.StartsWith("⚠"))) || (_lastOk && summary.StartsWith("⚠")) || (!auto && _rejectedNo > 0) || _heldThisRun > 0))
            try
            {
                // ★[v103.1.12 · JACK 1015 ① · 4판 낮음 3] 미룬 옹벽을 보류했으면 그 글 — 지우지 않았고 옹벽 면은 보인다(버린 옹벽 글과 다르다)
                string head0 = _heldThisRun > 0 ? (_lastOk ? $"옹벽 합성은 했지만 모아 둔 옹벽 {_heldThisRun}개는 정지면_DH에 못 넣어 보류했습니다(지우지 않았습니다)."
                                                           : $"모아 둔 옹벽 {_heldThisRun}개를 정지면_DH에 못 넣어 보류했습니다(지우지 않았습니다).")
                             : _lastOk ? "옹벽 합성은 했지만 빠진 옹벽이 있습니다."
                             : _rejectedNo > 0 ? $"옹벽 {_rejectedNo}를 정지면_DH에 못 넣어 버렸습니다(지난 옹벽과 정지면_DH는 그대로)."
                             : $"옹벽 {(autoNo > 0 ? autoNo + " " : "")}합성을 못 했습니다(지난 옹벽과 정지면_DH는 그대로).";
                string tail0 = _heldThisRun > 0 ? "보류한 옹벽은 옹벽 면으로 보입니다 — 옹벽 변환으로 다시 지정하거나 사면 변환으로 되돌리세요(정지면이 바뀌면 다음 합성이 다시 해 봅니다)."
                             : _lastOk ? "빠진 옹벽은 옹벽 변환에서 다시 지정하세요."
                             : _rejectedNo > 0 ? "버린 옹벽은 합성으로 돌아오지 않습니다 — 옹벽 변환을 다시 하세요."
                             : "다시 하려면 옹벽 변환을 다시 하거나 명령줄에 DHWALLCOMP를 치세요.";
                BusyPopup.Close();   // ★[v105.1.1] 마지막 알림 앞에서 로딩 창을 닫는다
                AcadApp.ShowAlertDialog(head0 + "\n\n" + System.Text.RegularExpressions.Regex.Replace(summary, "<[^>]+>", "") + "\n\n" + tail0 + " 자세한 것은 " + LogName);
            }
            catch { }
        // ★[v103.1.7 · 검토 1012 재검토 낮음 5 — 미뤘던 것] 토량을 미뤄 둔 길(정지면을 다시 안 만든 되돌리기 · 합성 전 토량을 생략한 재생성)은 팝업에 토량이 안 떴다 →
        //   그 토량이 나오는 이 자리에서 팝업으로도(팝업은 성패 + 토량 — JACK 0720)
        else if (canAlert && wantVol && _lastOk && volAfter.Length > 0)
            try { BusyPopup.Close(); AcadApp.ShowAlertDialog("옹벽 합성 완료\n\n" + volAfter + "\n\n(옹벽 합성 뒤 — 이번 변환의 토량입니다)"); } catch { }
        _lastSummary = summary;
        log.AppendLine("■ " + summary);
        // ★★★[v103.1.7 · JACK 1014] 자가점검 판정 한 줄 — 합성이 됐나 · 빠진 옹벽 · 우리 지표면의 구식 표시(커밋 뒤 새 트랜잭션) · 다음 Idle에 한 번 더
        try
        {
            string st; int sb;
            using (var trJ = db.TransactionManager.StartTransaction()) { (st, sb, _) = SelfCheck.StateVs(trJ, db, _issuesBefore); trJ.Commit(); }
            // ★[v103.1.7 · 검토 중간 2] 글 속 ⚠를 다 본다 — 종전엔 글이 «⚠»로 시작할 때만 봐 «<b>⚠1cm 넘은 자리</b>…» · «판정 2 ⚠거의» · «⚠토량을 못 쟀다»가 ✔로 적혔다
            // ★[확인 검토 열린 물음] 판정 2 «거의» · «1cm 넘은 자리»는 JACK이 정한 «짓고 ⚠»(0929 · 0930)라 판정에 안 넣는다(글에는 남는다) — 나머지 ⚠만 판정
            string sumWarn = string.Join(" · ", SelfCheck.WarnBits(summary).Split(new[] { " · " }, System.StringSplitOptions.RemoveEmptyEntries)
                                                    .Where(x => !x.Contains("거의") && !x.Contains("1cm 넘은 자리")));
            bool okJ = _lastOk && sumWarn.Length == 0 && sb == 0;
            _job?.Done(okJ, (_quiet ? "관문이 부름" : auto ? "옹벽 변환이 이어 태움" : "손으로 친 합성") + " · " + summary + " · " + st);
            log.AppendLine("■ 자가점검: " + (okJ ? "✔ " : "⚠ ") + st + (sumWarn.Length > 0 ? " · " + sumWarn : ""));
            if (sb > 0) SelfCheck.Warn(doc, "옹벽 합성 뒤 " + st);
            SelfCheck.RecheckAtIdle(doc, "옹벽 합성", sb, _issuesBefore);
        }
        catch (System.Exception jx) { _job?.Done(false, $"자가점검 실패 — {jx.GetType().Name}: {jx.Message} · {summary}"); }
        // ★★[v103.1.8 · JACK 1014 «현장확인 말고 로그로»] v103.2 첫 계측(m0)에 쓸 현장 지표면 — 도면 사본 대신 합성이 성공한 뒤 한 번 떠 둔다(개발 PC만 · 도면마다 판마다 한 번 · 읽기만)
        if (_lastOk && !_compGround.IsNull)
            try
            {
                string m0 = M0Dump.MaybeDump(doc, _compGround);
                if (m0.Length > 0) { log.AppendLine("■ " + m0); SelfCheckLog.Line("m0 지표면 덤프", !m0.StartsWith("⚠"), m0, doc.Name); try { ed.WriteMessage("\n[옹벽 합성] " + m0); } catch { } }
            }
            catch { }
        try { System.IO.File.WriteAllText(logPath, log.ToString()); WallDaylightBuilder.ArchiveDumpPublic(logPath); } catch { }
        try { ed.WriteMessage($"\n[옹벽 합성] {summary.Replace("<b>", "").Replace("</b>", "")}\n  로그: {logPath}\n"); } catch { }
        try { ed.Regen(); } catch { }
        if (!SelfCheckLog.Headless) try { GradingPalette.RefreshComposite(); } catch { }   // ★[v103.1.12] 정지 창 «대기 N»
    }

    /// <summary>재료 모으기 → Core 합성 → LandXML(임시 이름) → Civil → 되읽기 관문 → 정지면_DH로 바꿔 끼우기 → 보이기. 못 하면 까닭(⚠) 문자열.
    /// <para>★★★[v103.0 · JACK 0930 «옹벽이 남아야»] 재료는 <b>옹벽 목록</b> — «넣음» 옹벽(바꿀 것 빼고) + «대기» 옹벽을 번호 차례로 <b>합성 전 면 위에</b> 짓는다
    /// (<see cref="WallDaylight.ComposeWalls"/> — 더하기 = 다시 짓기의 마지막 단계). 세션 «짝»(LastWall*)은 안 쓴다 — Civil을 다시 켜도 같다.
    /// 대기 옹벽이 못 들어가면 그 옹벽만 버린다(결과물 지움 · 칸 지움) — 지난 옹벽과 정지면_DH는 그대로.</para></summary>
    private static string Build(Database db, Transaction tr, System.Text.StringBuilder log, string dir)
    {
        // ★★[v103.1.5 · 검토 1012 2차 중간 1] XML 원본 정리를 <b>면을 지우기 전에</b> 한 번 — 지금 물려 있는 원본의 낡은 «안 물림» 기록을 지운다
        //   (저장 안 하고 닫았다 다시 연 도면 — GradingBuilder.TidyXml · XmlStore.Deletable 설명). 손으로 친 DHWALLCOMP는 계획부지 생성의 들머리를 안 거친다
        try { string td0 = GradingBuilder.TidyXml(db, tr); if (td0.Length > 0) log.AppendLine("⓪ " + td0); }
        catch (System.Exception tx0) { log.AppendLine("⓪ XML 원본 정리 실패: " + tx0.GetType().Name + " " + tx0.Message); }
        int leftover = EraseTemp(tr);
        if (leftover > 0) log.AppendLine($"   지난번에 남은 임시 면 {leftover}개 지움");

        // ══ ★★★[v103.1.12 · 계획 4판 H1] 미룬 옹벽이 이번 합성에 드나 — 안 들면 종전 길 그대로(한 회 · 못 넣은 «대기»는 버림) ══
        if (!WallListStore.TryLoad(db, tr, out var head0, out var walls0, out string lwhy0)) return Fail("옹벽 목록을 못 읽었다 — " + lwhy0);
        string p0 = CurrentP0Handle(tr, NoriCommand.FindByHandle(db, head0.GroundHandle));
        // ★[코드 검토 낮음 4] 손으로 친 [옹벽 합성](옹벽 변환이 이어 태운 것 · 관문이 부른 것이 아님)은 열쇠를 안 본다 — «합성 실패» 보류를 다시 해 본다(일시적 실패가 굳지 않게)
        bool Stuck(WallRec w) => !_manualRetry && w.Hold.StartsWith(WallList.CompFailTag, System.StringComparison.Ordinal) && !WallList.CompFailFresh(w, walls0, p0);
        // ★[코드 검토 중간 2] «그대로라 안 해 봄»은 <b>시작 때 한 번</b> 정해 모든 회에서 뺀다 — 회 도중 다른 옹벽이 보류되면 열쇠가 바뀌어 «확인된 옹벽»으로 끼어들었다
        var stuck0 = new System.Collections.Generic.HashSet<int>(walls0.Where(w => w.State == 1 && Stuck(w)).Select(w => w.No));
        var defIn = walls0.Where(w => WallList.IsDeferred(w) && !WallList.IsMovedHold(w.Hold) && !stuck0.Contains(w.No)).Select(w => w.No).ToList();
        if (defIn.Count == 0) return BuildOnce(db, tr, log, dir, null);
        _hadDeferred = true;   // 합성 뒤 토량은 늘 잰다(4판 2-4 ④ — 세션 표시에 안 기댐)

        var swAll = System.Diagnostics.Stopwatch.StartNew();
        var unv = walls0.Where(w => w.State == 0 && w.Block.Length == 0).Select(w => w.No).Concat(defIn).Distinct().OrderBy(n => n).ToList();
        var unvSet = new System.Collections.Generic.HashSet<int>(unv);
        var ver = new System.Collections.Generic.HashSet<int>(walls0.Where(w => w.State == 1 && !unvSet.Contains(w.No) && !stuck0.Contains(w.No) && !WallList.IsMovedHold(w.Hold)).Select(w => w.No));
        var blamed = new System.Collections.Generic.HashSet<int>();
        System.Collections.Generic.HashSet<int> SkipSet() { var s = new System.Collections.Generic.HashSet<int>(blamed); s.UnionWith(stuck0); return s; }
        if (stuck0.Count > 0) log.AppendLine($"   «합성 실패» 보류 {string.Join(",", stuck0.OrderBy(n => n))}은 정지면 · 이웃이 그대로라 이번 합성에서 뺌(모든 회)");
        var blameTxt = new System.Collections.Generic.List<string>();
        int tries = 0;
        log.AppendLine($"■ 미룬 옹벽이 든 합성 — 새 옹벽(대기 · 미룸) {string.Join(",", unv)} · 확인된 옹벽(합성에 든 적 있음) {(ver.Count == 0 ? "없음" : string.Join(",", ver.OrderBy(n => n)))} · P0 {p0}");

        // 탓이 정해진 옹벽 — 대기(State 0)는 종전처럼 버리고(지움) · 미룬 옹벽은 «합성 실패» 보류(지우지 않음 — JACK 1015 ①). 목록은 이 트랜잭션에 저장(못 한 길도 커밋된다)
        void BlameWall(int no, string why)
        {
            if (!blamed.Add(no)) return;
            if (!WallListStore.TryLoad(db, tr, out var hB, out var wB, out string bw)) { blameTxt.Add($"옹벽 {no}(목록을 못 읽어 표시 못 함 — {bw})"); return; }
            var w = wB.FirstOrDefault(x => x.No == no);
            if (w == null) return;
            string why1 = System.Text.RegularExpressions.Regex.Replace(why, "<[^>]+>", "");
            if (w.State == 0)
            {
                _rejectedNo = no;
                var er = WallTags.Erase(db, tr, (n, r) => n == no, false, NoriCommand.FindByHandle(db, hB.GroundHandle));
                wB.RemoveAll(x => x.No == no);
                WallListStore.Save(db, tr, hB, wB);
                blameTxt.Add($"대기 옹벽 {no}를 버렸다({why1} · {er.Text})");
                _lostNote += (_lostNote.Length > 0 ? "\n" : "") + $"옹벽 {no}({w.Side} {w.Bench + 1}단) — 합성 전 «대기»를 버림: {why1}";
                log.AppendLine($"   ⚠대기 옹벽 {no} 버림 — {why1} · {er.Text}");
            }
            else
            {
                w.Hold = WallList.CompFailHold(p0, WallList.CompFailKey(wB, no, p0), why1);
                WallListStore.Save(db, tr, hB, wB);
                _heldThisRun++;
                // ★[JACK 1015 ③] 보류한 옹벽은 옹벽 면을 보인다(마지막 정식 합성을 건너뛰어도 — ⑩이 안 돈다)
                try { log.AppendLine("   보류 옹벽 면 " + WallTags.ShowOutsideWall(tr, w, p0)); } catch { }
                blameTxt.Add($"옹벽 {no}({w.Side} {w.Bench + 1}단) 보류 — {why1}");
                log.AppendLine($"   ⚠미룬 옹벽 {no} 보류(지우지 않음) — {why1}");
            }
        }
        // 끝에 남은 «합성 실패» 보류의 열쇠를 끝 상태로 다시 적는다(4판 중간 6 — 다음 합성이 그대로면 다시 안 해 본다)
        //   ★[코드 검토 중간 2] 이번 명령에서 <b>해 본 옹벽만</b>(blamed) — 해 보지 않은 보류의 열쇠를 끝 상태로 바꾸면 이웃이 바뀌었는데도 «같은 상태에서 실패»로 남는다
        void RekeyHolds()
        {
            try
            {
                if (!WallListStore.TryLoad(db, tr, out var hK, out var wK, out _)) return;
                bool any = false;
                foreach (var w in wK.Where(x => blamed.Contains(x.No) && x.Hold.StartsWith(WallList.CompFailTag, System.StringComparison.Ordinal)))
                { w.Hold = WallList.CompFailHold(p0, WallList.CompFailKey(wK, w.No, p0), WallList.HoldWhy(w.Hold)); any = true; }
                if (any) WallListStore.Save(db, tr, hK, wK);
            }
            catch (System.Exception kx) { log.AppendLine("   (보류 열쇠 다시 적기 실패: " + kx.GetType().Name + ")"); }
        }
        string Tail() => $" · 합성 {tries}회 · {swAll.ElapsedMilliseconds / 1000.0:F1}초";
        // ★[4판 재검토 중간 — 짐작] 한 트랜잭션에서 임시 면(수십만 면)을 여러 번 만들고 지운다 — 회마다 메모리를 적어 커밋 전까지 쌓이는지 본다
        string Mem() => $" · {swAll.ElapsedMilliseconds / 1000.0:F1}초 · 메모리 {System.GC.GetTotalMemory(false) / 1048576}MB";
        string Head() => (blameTxt.Count == 0 ? "" : $"⚠모아 둔 옹벽 가운데 {blameTxt.Count}개를 정지면_DH에 못 넣었다 — {string.Join(" · ", blameTxt)} · ")
                       + (stuck0.Count == 0 ? "" : $"⚠옹벽 {string.Join(",", stuck0.OrderBy(n => n))}은 지난 합성에 못 들어가 보류 중이고 정지면 · 이웃 옹벽이 그대로라 다시 안 해 봤다(옹벽 면은 그대로 보인다 · [옹벽 합성]을 손으로 누르면 다시 해 본다) · ");
        string Done(string r) { RekeyHolds(); log.AppendLine($"■ 미룬 옹벽 합성 끝 — 보류 · 버림 {blamed.Count}{(blamed.Count > 0 ? $"({string.Join(",", blamed)})" : "")}{Tail()}"); return Head() + r + Tail(); }

        // ① 한꺼번에(정식 — 성공하면 종전과 같은 시간)
        tries++;
        var a1 = new Attempt { NewIn = new(unv), Blame = new(unv), Skip = SkipSet() };
        string r1 = BuildOnce(db, tr, log, dir, a1);
        log.AppendLine($"   회 {tries} 한꺼번에 — {(a1.Ok ? "됨" : a1.Culprit > 0 ? $"탓 옹벽 {a1.Culprit}" : a1.AmongNew ? "새 옹벽 가운데(누군지 모름)" : a1.Empty ? "넣을 옹벽 없음" : "전체 못 함")}{(a1.Why.Length > 0 ? " — " + a1.Why : "")}{Mem()}");
        if (a1.Ok) return Done(r1);
        if (a1.Global || a1.Empty) return Done(r1);
        // ② 아는 탓이면 그것만 보류하고 한 번 더
        if (a1.Culprit > 0)
        {
            BlameWall(a1.Culprit, a1.Why);
            var rest = unv.Where(n => !blamed.Contains(n)).ToList();
            if (rest.Count > 0)
            {
                tries++;
                var a2 = new Attempt { NewIn = new(rest), Blame = new(rest), Skip = SkipSet() };
                string r2 = BuildOnce(db, tr, log, dir, a2);
                log.AppendLine($"   회 {tries} 탓 옹벽을 빼고 한꺼번에 — {(a2.Ok ? "됨" : a2.Culprit > 0 ? $"탓 옹벽 {a2.Culprit}" : a2.AmongNew ? "새 옹벽 가운데" : "전체 못 함")}{(a2.Why.Length > 0 ? " — " + a2.Why : "")}{Mem()}");
                if (a2.Ok) return Done(r2);
                if (a2.Global) return Done(r2);
            }
        }
        // ③ 하나씩 — 확인된 옹벽만 먼저(관문까지) → 새 옹벽을 번호 차례로 하나씩(그 회의 탓 후보는 그 하나) → 통과한 묶음으로 마지막 정식 한 번
        //   남은 새 옹벽이 없으면(탓 옹벽 하나뿐이었다) 확인된 옹벽만 회도 건너뛴다 — 아래에서 정지면_DH가 이미 그 묶음의 합성인지 보고 아니면 정식으로 한 번
        var remaining = unv.Where(n => !blamed.Contains(n)).ToList();
        if (remaining.Count > 0 && ver.Count > 0)
        {
            tries++;
            var a0 = new Attempt { Only = new(ver), Skip = SkipSet(), Probe = true };
            string r0 = BuildOnce(db, tr, log, dir, a0);
            log.AppendLine($"   회 {tries} 확인된 옹벽만(관문까지) — {(a0.Ok ? "됨" : a0.Empty ? "넣을 옹벽 없음" : "못 함")}{(a0.Why.Length > 0 ? " — " + a0.Why : "")}{Mem()}");
            if (!a0.Ok && !a0.Empty) return Done(r0);   // 확인된 옹벽만으로도 안 된다 — 미룸과 무관(종전대로 멈춤)
        }
        var inc = new System.Collections.Generic.HashSet<int>();
        foreach (int u in remaining)
        {
            tries++;
            var only = new System.Collections.Generic.HashSet<int>(ver); only.UnionWith(inc); only.Add(u);
            var newIn = new System.Collections.Generic.HashSet<int>(inc) { u };
            var au = new Attempt { Only = only, NewIn = newIn, Blame = new() { u }, Skip = SkipSet(), Probe = true };
            string ru = BuildOnce(db, tr, log, dir, au);
            log.AppendLine($"   회 {tries} 옹벽 {u} 더해 보기(관문까지) — {(au.Ok ? "됨" : au.Global ? "전체 못 함" : "못 함 → 보류")}{(au.Why.Length > 0 ? " — " + au.Why : "")}{Mem()}");
            if (au.Ok) inc.Add(u);
            else if (au.Global) return Done(ru);
            else BlameWall(u, au.Why.Length > 0 ? au.Why : "합성에 안 들어간다");
        }
        // 넣을 것이 확인된 옹벽뿐이고 지금 정지면_DH가 이미 그 묶음의 합성이면 다시 짓지 않는다
        var fin = new System.Collections.Generic.HashSet<int>(ver); fin.UnionWith(inc);
        try
        {
            var (pIds, _) = Candidates(Autodesk.Civil.ApplicationServices.CivilApplication.ActiveDocument, tr, PlanName, NoriCommand.FindByHandle(db, head0.GroundHandle));
            if (inc.Count == 0 && pIds.Count == 1 && WallListStore.TryLoad(db, tr, out _, out var wNow, out _))
            {
                var mk = CompositeWallNos(tr, tr.GetObject(pIds[0], OpenMode.ForRead));
                var want = wNow.Where(w => fin.Contains(w.No) && w.State == 1 && w.Hold.Length == 0).Select(w => w.No).OrderBy(n => n).ToList();
                if (mk != null && mk.Length > 0 && mk.OrderBy(n => n).SequenceEqual(want))
                {
                    log.AppendLine($"   정지면_DH가 이미 확인된 옹벽 {string.Join(",", want)}의 합성이다 — 다시 짓지 않음");
                    return Done($"⚠정지면_DH는 그대로다(옹벽 {string.Join(",", want)} 합성 · 모아 둔 옹벽은 못 넣었다)");
                }
            }
        }
        catch { }
        if (fin.Count == 0) return Done(Fail("모아 둔 옹벽을 하나도 합성에 못 넣었다(정지면_DH는 그대로)"));
        tries++;
        var af = new Attempt { Only = fin, NewIn = new(inc), Skip = SkipSet() };
        string rf = BuildOnce(db, tr, log, dir, af);
        log.AppendLine($"   회 {tries} 마지막 정식 합성(옹벽 {string.Join(",", fin.OrderBy(n => n))}) — {(af.Ok ? "됨" : "⚠못 함")}{(af.Why.Length > 0 ? " — " + af.Why : "")}{Mem()}");
        if (!af.Ok) rf += " · ⚠하나씩 통과한 묶음인데 마지막 정식 합성이 안 됐다(진단 로그를 보여 주세요)";
        return Done(rf);
    }

    /// <summary>합성 한 회 — <paramref name="at"/>가 null이면 종전 그대로(목록 전부 · 못 넣은 «대기»는 버림). 있으면 그 범위만 넣고, 못 하면 버리지 않고 탓만 돌려준다(부른 쪽이 정한다).</summary>
    private static string BuildOnce(Database db, Transaction tr, System.Text.StringBuilder log, string dir, Attempt? at)
    {
        var civilDoc = Autodesk.Civil.ApplicationServices.CivilApplication.ActiveDocument;
        _made = ObjectId.Null;   // ★[4판 중간 5] 회마다 — 이 회가 만든 새 면만 기억한다
        // ★[v103.1.12] 탓 후보인가 — 종전 길은 «대기»(State 0) · 회가 있으면 그 회의 후보만(State는 안 바꾼다)
        bool IsNew(WallRec w) => at == null ? w.State == 0 : at.Blame.Contains(w.No);
        string Miss(string why, bool amongNew) { if (at != null) { at.Why = why; if (amongNew) at.AmongNew = true; } return Fail(why); }

        // ① 옹벽 목록
        if (!WallListStore.TryLoad(db, tr, out var head, out var walls, out string lwhy)) return Miss("옹벽 목록을 못 읽었다 — " + lwhy, false);
        // ★[v103.1.14] 바꿀 옛 옹벽 — ReplaceNo · ReplaceNo2(셋을 하나로의 뒤 옹벽)
        var replaced = at == null
            ? new System.Collections.Generic.HashSet<int>(walls.Where(w => w.State == 0 && w.Block.Length == 0).SelectMany(WallList.Replaces))
            : WallList.ReplacedBy(walls, at.NewIn.Where(n => at.Only == null || at.Only.Contains(n)));   // ★[4판 H2] 회마다 — 이번 회에 든 새 옹벽이 바꾸는 것만
        // ★[v103.1.14 · JACK 1014 ② «번호 그대로»] 합성 차례 = 물려받을 번호(이어 지은 옹벽은 옛 옹벽 자리 차례 — 번호를 물려받은 뒤 다음 합성과 같은 차례) · 같으면 제 번호
        var use = walls.Where(w => (w.State == 1 && !replaced.Contains(w.No)) || (w.State == 0 && w.Block.Length == 0)).OrderBy(WallList.ComposeKey).ThenBy(w => w.No).ToList();
        if (at != null)
        {
            // ★[4판 H3] 이번 회에 안 넣는 옹벽(범위 밖 · 이미 보류/버림)은 use에서 뺀다 — ⑨가 «빠진 넣음 옹벽»으로 지우지 않게
            use.RemoveAll(w => (at.Only != null && !at.Only.Contains(w.No)) || at.Skip.Contains(w.No));
        }
        var blocked = walls.Where(w => w.State == 0 && w.Block.Length > 0).ToList();
        log.AppendLine($"① 옹벽 목록 — 넣음 {walls.Count(w => w.State == 1)}({string.Join(",", walls.Where(w => w.State == 1).Select(w => w.No))}) · 대기 {walls.Count(w => w.State == 0)}"
            + (replaced.Count > 0 ? $" · 바꿀 옹벽 {string.Join(",", replaced)}" : "") + (blocked.Count > 0 ? $" · 합성 막힌 대기 {string.Join(",", blocked.Select(w => w.No))}" : "")
            + (at != null ? $" · 이번 회 — 범위 {(at.Only == null ? "전부" : string.Join(",", at.Only.OrderBy(n => n)))} · 탓 후보 {(at.Blame.Count == 0 ? "없음" : string.Join(",", at.Blame.OrderBy(n => n)))}{(at.Skip.Count > 0 ? $" · 뺌 {string.Join(",", at.Skip.OrderBy(n => n))}" : "")}{(at.Probe ? " · 관문까지만" : "")}" : "")
            + $" → 합성할 옹벽 {use.Count}개({string.Join(",", use.Select(w => w.No))}) · 다음 번호 {head.NextNo}");
        if (use.Count == 0)
        {
            if (at != null) at.Empty = true;
            return Fail(walls.Count == 0 ? "옹벽 목록이 비었다 — 옹벽 변환(부분 지정)으로 옹벽을 먼저 지으세요"
                                         : $"합성할 옹벽이 없다{(blocked.Count > 0 ? $" — 대기 옹벽 {string.Join(",", blocked.Select(w => w.No))}는 합성이 막혔다({blocked[0].Block})" : "")}");
        }

        // ② 원지반 — 목록 머리 → 옹벽변환이 쓴 것 → 이름
        ObjectId groundId = NoriCommand.FindByHandle(db, head.GroundHandle);
        if (groundId.IsNull) groundId = NoriCommand.FindByHandle(db, GradingSettings.LastGroundHandle);
        string gName = "";
        if (groundId.IsNull) groundId = ImportGisCommand.FindGroundSurface(db, out gName, out _);
        if (groundId.IsNull || tr.GetObject(groundId, OpenMode.ForRead) is not TinSurface gTin) return Fail("원지반 지표면을 못 찾았다");
        gName = gTin.Name;

        // ③ 합성 전 면(P0) — 정지면_DH가 합성이면 그 표지가 가리키는 합성 전 면 · 아니면 정지면_DH 그대로
        var (cIds, cNames) = Candidates(civilDoc, tr, PlanName, groundId);
        if (cIds.Count != 1)
            return Fail(cIds.Count == 0 ? $"{PlanName}가 없다 — 계획부지 생성을 먼저 돌리세요" : $"{PlanName} 후보가 {cIds.Count}개({string.Join(", ", cNames)}) — 어느 것과 합성할지 모른다");
        if (tr.GetObject(cIds[0], OpenMode.ForRead) is not TinSurface curTin) return Fail("정지면이 TIN 지표면이 아니다");
        TinSurface pTin; bool inS1;
        if (IsComposite(tr, curTin))
        {
            string bh = CompositeBaseHandle(tr, curTin);
            var (bIds, bNames) = Candidates(civilDoc, tr, BaseName, groundId);
            if (bIds.Count != 1 || bIds[0].Handle.ToString() != bh || tr.GetObject(bIds[0], OpenMode.ForRead) is not TinSurface b0)
                return Fail($"'{curTin.Name}'는 옹벽 합성인데 그 합성 전 면을 못 찾았다(합성 전 면 {bIds.Count}개{(bIds.Count > 0 ? ": " + string.Join(", ", bNames) : "")}) — {B1Help}");
            pTin = b0; inS1 = true;
        }
        else
        {
            var (bx, bxNames) = Candidates(civilDoc, tr, BaseName, groundId);
            if (bx.Count > 0) return Fail($"합성 전 면({string.Join(", ", bxNames)})이 남아 있는데 정지면_DH는 합성이 아니다(짝이 어긋남) — {PairHelp}");
            pTin = curTin; inS1 = false;
        }
        string rbWhy = "";
        try
        {
            if (pTin.IsOutOfDate)
            {
                string rb = GradingBuilder.RebuildSurfacesByBaseName(tr, pTin.Name);
                log.AppendLine($"   '{pTin.Name}'가 구식이라 다시 지음: {rb}");
                rbWhy = SelfCheck.WarnBits(rb);
                // ★★[v103.1.7 · 검토 1013 확인 낮음 4] 되찍기까지 실패했거나 되찍은 면이 바뀌었으면 이 트랜잭션을 통째로 물린다(그대로 커밋하면 다음 재작성에서 죽거나 바뀐 모양이 굳는다)
                if (GradingBuilder.MustAbort(rb)) throw new SwapFailed($"합성 전 면 '{pTin.Name}'을 다시 짓다 스냅샷을 못 다뤘다({rbWhy})");
            }
        }
        catch (SwapFailed) { throw; }
        catch (System.Exception rx) { log.AppendLine($"   다시 짓기 실패 {rx.GetType().Name}"); }
        // ★[v103.1.7 · 검토 1013 확인 낮음 2] 아직 낡았으면 그 까닭(되찍지 못해 재작성하지 않은 면 등)도 함께 — «다시 지어도 안 풀린다»만으로는 왜인지 모른다
        try { if (pTin.IsOutOfDate) return Fail($"'{pTin.Name}'가 낡음(Out of date) — 다시 지어도 안 풀린다{(rbWhy.Length > 0 ? $"({rbWhy})" : "")}"); } catch { }
        log.AppendLine($"② 원지반 '{gName}' · 합성 전 면 '{pTin.Name}'{(inS1 ? "(정지면_DH는 지난 합성 — 그 합성 전 면 위에 처음부터 다시)" : "")}");

        // ★★★[v103.1 · JACK 1002 «부분 변환한 곳은 반대로 돌리면 그 모양으로 · 남은 옹벽은 그대로»] 정지면을 다시 지었으면(옹벽을 지을 때 잰 P0와 지금 P0가 다르다)
        //   그 옹벽을 같은 번호 · 저장한 입력으로 다시 짓는다(가상옹벽 · 데이라잇 · 띠 · 순수옹벽 — 옹벽 더하기와 같은 함수). 끝까지 성공한 정지면 생성이 확인한 P0일 때만(검토 v103.1 높음 3)
        string p0h = pTin.ObjectId.Handle.ToString();
        if (head.VerifiedP0.Length > 0 && head.VerifiedP0 != p0h)
            return Fail($"'{pTin.Name}'는 끝까지 만든 정지면이 아니다(확인된 핸들 {head.VerifiedP0} · 지금 {p0h}) — 계획부지 생성 «다시»나 사면 변환으로 정지면을 다시 만든 뒤 합성하세요(옹벽 목록은 그대로)");
        if (inS1)
        {
            // 판 1 옹벽(P0 모름) — 지금 합성 표지가 그 번호를 들고 있으면 지금 P0 위에 지은 것이다
            var mk = CompositeWallNos(tr, curTin);
            if (mk != null) foreach (var w in use) if (w.State == 1 && w.P0Handle.Length == 0 && System.Array.IndexOf(mk, w.No) >= 0) w.P0Handle = p0h;
        }
        // ★[v103.1 · 코드 검토 낮음 3] 보류는 둘 — 자리가 움직인 것(끝 확인 · 사면 변환으로 되돌리거나 다시 지정)은 빼고, 다시 짓기에 실패한 것은 이번에 다시 시도한다
        // ★[v103.1.12 · 4판 중간 6] «합성 실패» 보류는 정지면 · 이웃이 그대로면(열쇠가 같으면) 빼고 — 바뀌었으면 다시 해 본다
        var heldNow = use.Where(w => WallList.IsMovedHold(w.Hold)).ToList();
        //   (회가 있으면 «안 해 봄»은 부른 쪽이 시작 때 정해 at.Skip으로 이미 뺐다 — 여기는 종전 길용 · 손으로 친 합성은 열쇠를 안 본다)
        var stuckNow = use.Where(w => !_manualRetry && w.Hold.StartsWith(WallList.CompFailTag, System.StringComparison.Ordinal) && !WallList.CompFailFresh(w, walls, p0h)).ToList();
        foreach (var w in heldNow) use.Remove(w);
        foreach (var w in stuckNow) use.Remove(w);
        // ★[4판 중간 5] 이번 명령의 앞 회에서 다시 짓기를 못 한 옹벽은 다시 안 짓는다(회마다 30초)
        if (at != null) use.RemoveAll(w => _rebuildFailedRun.Contains(w.No));
        var failedNow = new System.Collections.Generic.List<WallRec>();
        // ★[v103.1.12 · 4판 중간 5] _rebuiltN은 명령 전체의 합 — RunInner가 비운다(회마다 비우면 첫 회에 다시 지은 수를 잃어 합성 뒤 토량을 안 잰다)
        var toRebuild = use.Where(w => w.State == 1 && w.P0Handle != p0h).ToList();
        if (toRebuild.Count > 0)
        {
            var gIfc = new CachedGroundSurface(gTin);
            foreach (var w in toRebuild)
            {
                var swR = System.Diagnostics.Stopwatch.StartNew();
                string rb = RebuildWall(db, tr, w, head.PlanHandle, gIfc, groundId, log);
                if (rb.Length > 0) { w.Hold = RebuildFailTag + " — " + rb; use.Remove(w); failedNow.Add(w); _rebuildFailedRun.Add(w.No); log.AppendLine($"   ⚠옹벽 {w.No} 보류 — {rb}"); }
                else { w.P0Handle = p0h; w.Hold = ""; _rebuiltN++; log.AppendLine($"   옹벽 {w.No}를 새 정지면에 다시 지었다 · {swR.ElapsedMilliseconds}ms"); }
            }
            // 다시 지은 것 · 보류는 적어 둔다 — 아래 합성이 못 해도(Fail도 커밋) 옹벽은 새 정지면에 맞게 남는다
            try { WallListStore.Save(db, tr, head, walls); } catch (System.Exception sx) { log.AppendLine("목록 저장 실패: " + sx.GetType().Name); }
            // ★★[v103.1.14 · 코드 검토 중간 1 — v103.1.12부터 있던 틈] 이번 회의 <b>새 옹벽</b>(미룬 옹벽 — 바꿀 옛 옹벽이 있을 수 있다)을 다시 짓지 못했으면 조용히 빼지 않는다 —
            //   빼고 합성하면 바꿀 옛 옹벽(위 replaced — 이미 정했다)만 지워져 옛 옹벽도 새 옹벽도 정지면_DH에 없게 된다(이어 짓기면 번호도 잃는다).
            //   이 회를 그 옹벽 탓으로 끝낸다 → 부른 쪽이 그 옹벽만 보류하고 나머지로 다시 합성한다(옛 옹벽은 그대로). 탓 후보가 아니면(마지막 정식 회) 회 전체 못 함
            var newFail = at != null ? failedNow.FirstOrDefault(w => at.NewIn.Contains(w.No)) : null;
            if (newFail != null)
            {
                string whyR = $"새 정지면에 다시 짓지 못했다 — {WallList.HoldWhy(newFail.Hold)}";
                if (at!.Blame.Contains(newFail.No)) { at.Culprit = newFail.No; at.Why = whyR; }
                return Fail($"옹벽 {newFail.No}({newFail.Side})를 {whyR}(바꿀 옛 옹벽 {string.Join(",", WallList.Replaces(newFail))}{(newFail.KeepNo > 0 ? $" · 물려받을 번호 {newFail.KeepNo}" : "")}은 그대로)");
            }
        }
        _heldNote = (heldNow.Count > 0 ? $"⚠옹벽 {string.Join(",", heldNow.Select(w => w.No))}은 서 있던 단이 움직여 보류라 합성에서 뺐다({string.Join(" · ", heldNow.Select(w => $"{w.No}: {w.Hold}"))}) — 사면 변환으로 되돌리거나 옹벽 변환으로 다시 지정하세요 · " : "")
                  + (stuckNow.Count > 0 ? $"⚠옹벽 {string.Join(",", stuckNow.Select(w => w.No))}은 지난 합성에 못 들어가 보류 중이고 정지면 · 이웃 옹벽이 그대로라 다시 안 해 봤다({string.Join(" · ", stuckNow.Select(w => $"{w.No}: {WallList.HoldWhy(w.Hold)}"))}) — 옹벽 변환으로 다시 지정하거나 사면 변환으로 되돌리세요(옹벽 면은 그대로 보인다) · " : "")
                  + (failedNow.Count > 0 ? $"⚠옹벽 {string.Join(",", failedNow.Select(w => w.No))}을 새 정지면에 다시 짓지 못해 이번 합성에서 뺐다({string.Join(" · ", failedNow.Select(w => $"{w.No}: {w.Hold}"))}) — 옹벽은 목록에 그대로 · 명령줄 DHWALLCOMP나 계획부지 생성 «다시»로 다시 시도하세요(사면 변환은 그 옹벽을 지웁니다) · " : "");
        if (use.Count == 0)
        {
            if (at != null) at.Empty = true;
            return Fail($"합성할 옹벽이 없다 — {_heldNote}");
        }

        // ★★[v103.1.14 · JACK 1014 ② «번호 그대로» · 계획 검토 중간 4] 이어 지은 새 옹벽(물려받을 번호 K)의 번호 물려받기를 합성 <b>전에</b> 잰다 —
        //   K가 이번 합성이 지울 옛 옹벽(바꿈 대상 · 목록에 있음)이고 K 이름의 살아 있는 표면이 다 K 것일 때만. 안 되면 물려받지 않고 합성한다(⚠ — 회마다 같은 실패가 되풀이되지 않게)
        var renames = new System.Collections.Generic.Dictionary<int, int>();
        foreach (var w in use)
        {
            if (w.KeepNo <= 0 || !(w.State == 0 || WallList.IsDeferred(w))) continue;
            string rw = !replaced.Contains(w.KeepNo) ? $"옹벽 {w.KeepNo}이 이번 합성이 지울 옛 옹벽이 아니다"
                      : !walls.Any(x => x.No == w.KeepNo) ? $"옹벽 {w.KeepNo}이 목록에 없다"
                      : renames.ContainsValue(w.KeepNo) ? $"옹벽 {w.KeepNo} 번호를 다른 새 옹벽도 물려받으려 한다"
                      : WallTags.RenumberBlock(tr, w.No, w.KeepNo, groundId);
            if (rw.Length == 0) renames[w.No] = w.KeepNo;
            log.AppendLine($"②-b 번호 물려받기(미리 잼) — 새 옹벽 {w.No} → 옹벽 {w.KeepNo}: {(rw.Length == 0 ? "된다" : "⚠안 함 — " + rw)}");
        }

        // 대기 옹벽을 버린다 — 결과물 지움 · 칸 지움 · 목록 저장(이 트랜잭션은 Fail이어도 커밋된다 — 바꿔 끼우기 전이라 남는 것은 이것뿐)
        string Reject(WallRec w, string why)
        {
            _rejectedNo = w.No;
            var er = WallTags.Erase(db, tr, (n, r) => n == w.No, false, groundId);
            walls.RemoveAll(x => x.No == w.No);
            try { WallListStore.Save(db, tr, head, walls); } catch (System.Exception sx) { log.AppendLine("목록 저장 실패: " + sx.GetType().Name); }
            log.AppendLine($"   대기 옹벽 {w.No} 버림 — {why} · {er.Text}");
            _lostNote += (_lostNote.Length > 0 ? "\n" : "") + $"옹벽 {w.No}({w.Side} {w.Bench + 1}단) — 합성 전 «대기»를 버림: {System.Text.RegularExpressions.Regex.Replace(why, "<[^>]+>", "")}";
            return Fail($"옹벽 {w.No}({w.Side})를 정지면_DH에 못 넣었다 — {why} · 그 옹벽 결과물을 지웠다({er.Text}) · 지난 옹벽 {walls.Count(x => x.State == 1)}개와 정지면_DH는 그대로");
        }

        // ④ 옹벽마다 재료 — 가상옹벽{번호}_DH · 방향 표지 · 막음 까닭 · 모양(머리 위/아래)
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var gCache = new CachedGroundSurface(gTin); var planCache = new CachedGroundSurface(pTin);
        if (gCache.ReadFailed + planCache.ReadFailed > 0)
            return Fail($"삼각형을 다 못 읽었다(원지반 {gCache.ReadFailed} · 정지면 {planCache.ReadFailed}개 빠짐)");
        var inputs = new System.Collections.Generic.List<WallDaylight.WallInput>();
        var useOk = new System.Collections.Generic.List<WallRec>();
        var dropped = new System.Collections.Generic.List<string>();
        var droppedNos = new System.Collections.Generic.HashSet<int>();   // ★[4판 H3] ⑨가 지울 «면이 없어진 넣음 옹벽» — 이것만
        foreach (var w in use)
        {
            string bad = "";
            ObjectId wId = NoriCommand.FindByHandle(db, w.WallHandle);
            TinSurface? wTin = null;
            bool gone = false;   // ★[v103.0 · 재검토 낮음 3] 다시는 못 읽는 것(면이 지워짐 · 이름이 다름)만 뺀다 — 낡음·읽기 실패 등은 일시적일 수 있다
            if (wId.IsNull || tr.GetObject(wId, OpenMode.ForRead) is not TinSurface wt) { bad = $"가상옹벽{w.No}_DH가 없다(지워졌다)"; gone = true; }
            else
            {
                wTin = wt;
                if (!WallTags.IsWallSurface(wt.Name, out int nn) || nn != w.No) { bad = $"기억한 가상옹벽 이름이 다르다('{wt.Name}')"; gone = true; }
                else
                {
                    string side = WallDaylightBuilder.ReadSide(tr, wt, out string sideBlock);
                    if (side != w.Side) bad = $"'{wt.Name}'의 방향 표지('{side}')가 목록('{w.Side}')과 다르다";
                    else if (sideBlock.Length > 0) bad = $"옹벽 변환이 이 옹벽의 합성을 막아 두었다 — {sideBlock}";
                    else
                    {
                        // ★[v103.0 · 코드 검토 중간 3] 낡았으면 다시 지어 본다(합성 전 면과 같은 법) — 그래도 낡았을 때만 걸린다
                        string rbW = "";
                        try { if (wt.IsOutOfDate) { rbW = GradingBuilder.RebuildSurfacesByBaseName(tr, wt.Name); log.AppendLine($"   '{wt.Name}'가 낡아 다시 지음"); } } catch { }
                        // ★[v103.1.7 · 검토 낮음 3③] 스냅샷을 못 다뤘으면 통째로 되돌린다(SwapFailed)
                        if (GradingBuilder.MustAbort(rbW)) throw new SwapFailed($"'{wt.Name}'을 다시 짓다 스냅샷을 못 다뤘다({SelfCheck.WarnBits(rbW)})");
                        try { if (wt.IsOutOfDate) bad = $"'{wt.Name}'가 낡음(Out of date) — 다시 지어도 안 풀린다"; } catch { }
                    }
                }
            }
            if (bad.Length == 0 && wTin != null)
            {
                var wCache = new CachedGroundSurface(wTin);
                if (wCache.ReadFailed > 0) bad = $"가상옹벽{w.No}_DH 삼각형을 다 못 읽었다({wCache.ReadFailed}개 빠짐)";
                else
                {
                    double mnx = w.Poly.Min(q => q.X) - 1, mny = w.Poly.Min(q => q.Y) - 1, mxx = w.Poly.Max(q => q.X) + 1, mxy = w.Poly.Max(q => q.Y) + 1;
                    var wallTris = wCache.TrianglesIn(mnx, mny, mxx, mxy);
                    // ★[v102.0 · 계획 검토 M3 ①] 기하로 한 번 더 — 옹벽 첫 줄 = 폴리곤 z(머리). 절토면 옹벽이 전부 그 위 · 성토면 전부 그 아래(1mm)
                    double z0 = w.Poly[0].Z; int wrong = 0; double worst = 0;
                    foreach (var t in wallTris)
                        foreach (var q in new[] { t.A, t.B, t.C })
                        { double dz = w.Up ? z0 - q.Z : q.Z - z0; if (dz > 1e-3) { wrong++; worst = System.Math.Max(worst, dz); } }
                    if (wrong > 0) bad = $"방향('{w.Side}')과 옹벽 모양이 안 맞는다 — 꼭짓점 {wrong}개가 머리 {z0:F3}m {(w.Up ? "아래" : "위")}(최대 {worst:F3}m)";
                    else
                    {
                        var groundTris = gCache.TrianglesIn(mnx, mny, mxx, mxy);
                        inputs.Add(new WallDaylight.WallInput { No = w.No, Poly = w.Poly, WallTris = wallTris, GroundTris = groundTris, Down = !w.Up });
                        useOk.Add(w);
                        // 입력을 떨군다(옹벽마다 — 폴리곤 ±5m 합성 전 면) — 오프라인 재생
                        try
                        {
                            string dump = System.IO.Path.Combine(dir, $"DHWALLCOMP_입력_옹벽{w.No}.txt");
                            WallDaylight.WriteBandInput(dump, w.Poly, wallTris, groundTris, planCache.TrianglesIn(mnx - 4, mny - 4, mxx + 4, mxy + 4),
                                $"{System.DateTime.Now:yyyy-MM-dd HH:mm:ss} {GradingSettings.Version} 원지반='{gName}' 옹벽='{wTin.Name}' 정지면='{pTin.Name}' · 옹벽 {w.No}({w.Side}) · 목록 차례 {inputs.Count}");
                            WallDaylight.AppendInputTail(dump, w.Side, null, null);
                            WallDaylightBuilder.ArchiveDumpPublic(dump);
                        }
                        catch (System.Exception de) { log.AppendLine($"   (옹벽 {w.No} 입력 못 떨굼: {de.GetType().Name})"); }
                    }
                }
            }
            if (bad.Length > 0)
            {
                if (at == null)
                {
                    if (w.State == 0) return Reject(w, bad);
                    if (gone) { dropped.Add($"옹벽 {w.No}({bad})"); droppedNos.Add(w.No); }
                    else
                    {
                        // 넣은 옹벽을 지금 못 읽는다(일시적일 수 있다) — 빼지 않는다. 이번 새 옹벽이 있으면 그것을 버리고 지난 합성은 그대로 · 없으면 멈춘다
                        var nw = use.LastOrDefault(x => x.State == 0);
                        if (nw != null) return Reject(nw, $"넣은 옹벽 {w.No}를 지금 못 읽는다({bad}) — 지난 합성을 그대로 둔다");
                        return Fail($"넣은 옹벽 {w.No}를 지금 못 읽는다 — {bad}");
                    }
                }
                else
                {
                    // ★[v103.1.12 · 4판 H1] 회 — 버리지 않고 탓만 돌려준다(부른 쪽이 대기는 버리고 · 미룬 옹벽은 보류한다)
                    //   미룬 옹벽의 면이 없어졌으면(gone) 종전 «넣음»처럼 뺀다(⚠ · ⑨가 결과물을 지운다 — 4판 열린 질문 2) · 확인된 옹벽을 지금 못 읽으면 전체 못 함(종전처럼 멈춤)
                    if (w.State == 0 && at.Blame.Contains(w.No)) { at.Culprit = w.No; at.Why = bad; return Fail($"옹벽 {w.No}({w.Side})를 정지면_DH에 못 넣는다 — {bad}"); }
                    if (gone) { dropped.Add($"옹벽 {w.No}({bad})"); droppedNos.Add(w.No); }
                    else if (at.Blame.Contains(w.No)) { at.Culprit = w.No; at.Why = bad; return Fail($"옹벽 {w.No}({w.Side})를 정지면_DH에 못 넣는다 — {bad}"); }
                    else return Fail($"넣은 옹벽 {w.No}를 지금 못 읽는다 — {bad}");
                }
            }
        }
        if (dropped.Count > 0) log.AppendLine("   ⚠넣음 옹벽을 뺀다 — " + string.Join(" · ", dropped));
        if (inputs.Count == 0) { if (at != null) at.Empty = true; return Fail("합성할 수 있는 옹벽이 없다 — " + string.Join(" · ", dropped)); }
        var planAll = planCache.AllTriangles();
        log.AppendLine($"③ 삼각형 — 옹벽 {inputs.Count}개({string.Join(",", inputs.Select(i => $"{i.No}:{i.WallTris.Count}"))}) · 정지면 전체 {planAll.Count} · {sw.ElapsedMilliseconds}ms");

        // ⑤ Core 차례 합성 — P_k = BuildComposite(P_{k−1}, 옹벽 k)
        var res = WallDaylight.ComposeWalls(planAll, inputs);
        foreach (var st in res.Steps)
            log.AppendLine($"   옹벽 {st.No}: {(st.R.Zone != null || st.R.Tier < 3 ? st.R.Summary : (st.IntoPrev > 0 ? $"앞 옹벽 구역과 겹침 {st.IntoPrev} @{st.IntoPrevAt}" : st.R.Summary))} · 앞 구역 삼각형 {st.TouchedPrevZone} · {st.Ms}ms");
        log.AppendLine("④ 합성: " + res.Summary);
        if (res.All == null)
        {
            int failNo = res.FailIndex >= 0 && res.FailIndex < inputs.Count ? inputs[res.FailIndex].No : 0;
            var fw = useOk.FirstOrDefault(w => w.No == failNo);
            if (at != null)
            {
                // ★[v103.1.12 · 4판 H1 · 3판 중간 3] 옹벽 한 단계가 못 했고 그 옹벽이 탓 후보면 «아는 탓» · 끝의 높이 오차 합(FailFinal — 늘 마지막 옹벽을 가리킨다)이나
                //   확인된 옹벽 단계면 «새 것 가운데» · 후보가 없으면 전체 못 함
                at.Why = res.Fail;
                if (fw != null && IsNew(fw) && !res.FailFinal) at.Culprit = fw.No;
                else if (useOk.Any(IsNew)) at.AmongNew = true;
                return Fail("옹벽 합성을 못 만들었다 — " + res.Fail);
            }
            if (fw != null && fw.State == 0) return Reject(fw, res.Fail);
            // ★[v103.0 · 코드 검토 낮음 6] 넣음 옹벽이 걸렸어도 이번에 새 옹벽이 있으면 그 탓일 수 있다(바꿈으로 차례가 바뀜 등) — 새 옹벽을 버리고 지난 합성은 그대로
            var newest0 = useOk.LastOrDefault(w => w.State == 0);
            if (newest0 != null) return Reject(newest0, $"넣은 옹벽 {failNo}가 이번 옹벽과 함께 안 들어간다 — {res.Fail}");
            return Fail("옹벽 합성을 못 만들었다 — " + res.Fail);
        }

        // ⑥ LandXML → Civil(임시 이름 — 정지면_DH는 아직 안 건드린다)
        var all = res.All;
        // ★★★[v103.1.5 · JACK 1012 «지금 바로 고침»] LandXML로 만든 면은 재작성 때 원본 파일을 다시 읽는다(바뀌어 있으면 말없이 그 내용으로 바뀐다 — 1012 실측 ·
        //   GradingBuilder의 «LandXML로 만든 면» 설명). 그래서 ①도면의 XML 면(옛 판이 지은 합성 · 순수옹벽)을 지금 모양 그대로 굳히고
        //   ②옛 판의 고정 이름 파일은 이름을 바꿔 두고 안 물린 묵은 파일을 치운 뒤 ③<b>새 이름의 파일</b>에 쓴다 — 한 번 쓴 파일은 다시 쓰지 않는다
        try { string fz0 = GradingBuilder.FreezeAllXmlBacked(tr); if (fz0.Length > 0) log.AppendLine("④-b 굳힘: " + fz0); }
        catch (System.Exception fx) { log.AppendLine("④-b 굳힘 실패: " + fx.GetType().Name + " " + fx.Message); }
        try { string td = GradingBuilder.TidyXml(db, tr); if (td.Length > 0) log.AppendLine("④-c " + td); }
        catch (System.Exception tx) { log.AppendLine("④-c XML 원본 정리 실패: " + tx.GetType().Name + " " + tx.Message); }
        // ★[v103.0 · 코드 검토 낮음 8] 여기부터 못 하면 이번 새 옹벽(대기)을 버린다 — 대기 칸이 남아 다음 옹벽 변환까지 떠돌지 않게
        string FailNew(string why)
        {
            if (at != null) return Miss(why, useOk.Any(IsNew));   // ★[4판 H1] 회 — 탓은 새 것 가운데(하나씩 회면 그 하나)
            var nw = useOk.LastOrDefault(w => w.State == 0); return nw != null ? Reject(nw, why) : Fail(why);
        }
        // ★[검토 1012 2차 낮음 2] 폴더를 못 만들면(권한 · 디스크) 까닭을 알아볼 말로 던진다 — RunInner가 잡아 통째로 되돌린다(종전의 «파일을 못 썼다»와 같은 길).
        //   새 옹벽(대기)을 버리지 않는다 — PC 사정이지 옹벽 탓이 아니다. 고친 뒤 DHWALLCOMP를 다시 치면 된다
        string xml;
        try { xml = GradingBuilder.NewXmlPath(db, "합성"); }
        catch (System.Exception px) { throw new System.IO.IOException($"XML 원본을 둘 폴더를 못 만들었다 — {px.GetType().Name}: {px.Message}", px); }
        log.AppendLine("④-d XML 원본: " + xml);
        WallDaylight.WriteLandXmlTin(xml, "PUREWALL", all, 1e-7, out int np, out int nf, out int nd);
        if (nd > 0) return FailNew($"합성을 못 만들었다 — 넓이 0이라 뺀 면 {nd}개");
        sw.Restart();
        ObjectId cid = TinSurface.CreateFromLandXML(db, TempName, xml, "PUREWALL");
        if (cid.IsNull) return FailNew("합성을 못 만들었다 — CreateFromLandXML이 빈 ObjectId를 돌려줬다");
        _made = cid;
        var cTin = (TinSurface)tr.GetObject(cid, OpenMode.ForWrite);
        long tCreate = sw.ElapsedMilliseconds;
        // ★자가검증(검토 1012 2차 중간 3) — 새 면이 넘긴 경로 그대로를 무는가. 어긋나면 이 실행에서는 XML 원본을 지우지 않는다(합성은 계속한다 — 모양과 무관)
        try { string rc = GradingBuilder.XmlRefCheck(cTin, xml); log.AppendLine(rc.Length == 0 ? "④-e 원본 경로 확인: 새 면이 넘긴 경로를 문다" : "④-e ⚠" + rc + " — 이 실행에서는 XML 원본을 지우지 않는다"); }
        catch (System.Exception rx) { log.AppendLine("④-e 원본 경로 확인 실패: " + rx.GetType().Name); }
        void Drop() { try { cTin.Erase(); } catch { } }

        // ⑦ 되읽기 관문 — 점 수 · 삼각형 수 · 넘긴 면과 같은가 · 높이 오차 합이 판정 한도 안(옹벽마다의 최대 · 겹친 바깥은 잰 값)
        string back;
        bool gateOk;
        try
        {
            sw.Restart();
            int civPts = -1; try { civPts = cTin.GetGeneralProperties().NumberOfPoints; } catch { }
            var theirs = new System.Collections.Generic.List<WallDaylight.Tri>(nf);
            using (var tc = cTin.GetTriangles(false))
                foreach (TinSurfaceTriangle t in tc)
                {
                    try
                    {
                        var a = t.Vertex1.Location; var b = t.Vertex2.Location; var c = t.Vertex3.Location;
                        theirs.Add(new WallDaylight.Tri(new Point3(a.X, a.Y, a.Z), new Point3(b.X, b.Y, b.Z), new Point3(c.X, c.Y, c.Z)));
                    }
                    finally { t.Dispose(); }
                }
            var cmp = CivilSafeMesh.Compare(all, theirs);
            double zTol = res.Tier == 1 ? 1e-3 : res.HeightLimit;
            // ★[v102.1 · 계획 검토 M3] «Civil이 반드시 합칠 쌍» 허용 판은 Civil이 넘긴 면을 <b>하나도 안 바꿨을 때만</b> 받는다
            bool over = res.OverForced;
            bool exact = !over || (cmp.Same == nf && theirs.Count == nf && cmp.MaxDz == 0);
            gateOk = theirs.Count == nf && civPts == np && cmp.Exceptions == 0 && cmp.SymArea <= 1e-8 && cmp.Hausdorff <= 1e-6 && res.HeightBudget + cmp.MaxDz <= zTol && exact;
            back = $"Civil 점 {civPts}/{np} · 삼각형 {theirs.Count}/{nf} · {cmp.Summary} · 높이 오차 합 {res.HeightBudget * 1000:F3} + Civil {cmp.MaxDz * 1000:F3}mm(한도 {zTol * 1000:F0}mm)"
                 + (over ? $" · 허용 판 — Civil이 넘긴 면 그대로 {cmp.Same}/{nf}{(exact ? "" : " ⚠아님(허용 판은 그대로여야 받는다)")}" : "")
                 + $" · 가져오기 {tCreate}ms · 대조 {sw.ElapsedMilliseconds}ms";
        }
        catch (System.Exception gx) { Drop(); log.AppendLine(gx.ToString()); return FailNew($"새 합성을 되읽다 터졌다({gx.GetType().Name}) — 새 면은 지웠다"); }
        // ★[v103.1.12] 화면 없는 Civil 시험 칸 — 이 번호 옹벽이 든 회는 되읽기 관문을 못 지난 것으로 친다(«누가 탓인지 모르는 실패» → 하나씩 길을 시험). 화면 있는 Civil에서는 늘 0
        if (SelfCheckLog.Headless && _failGateForNoTest > 0 && useOk.Any(w => w.No == _failGateForNoTest)) { gateOk = false; back += $" · (시험 칸 — 옹벽 {_failGateForNoTest}가 든 회는 관문 실패로 침)"; }
        log.AppendLine("⑤ 되읽기: " + back);
        if (!gateOk)
        {
            Drop();
            if (at != null) return Miss("Civil이 넘긴 합성을 그대로 안 받았다 — " + back, useOk.Any(IsNew));
            var newest = useOk.LastOrDefault(w => w.State == 0);
            if (newest != null) return Reject(newest, "Civil이 넘긴 합성을 그대로 안 받았다 — " + back);
            return Fail("Civil이 넘긴 합성을 그대로 안 받았다 — 새 면은 지웠다 · " + back);
        }

        // ★[v103.1.12 · 4판 H1] 하나씩 회 — 관문까지 왔으면 «됨». 임시 면은 지우고 바꿔 끼우기 · 목록 · 보이기는 안 한다(마지막 정식 회가 한다)
        if (at != null && at.Probe)
        {
            Drop(); _made = ObjectId.Null;
            at.Ok = true;
            return $"(관문까지 됨 — 옹벽 {string.Join(",", useOk.Select(w => w.No))} · 판정 {res.Tier})";
        }

        // ⑦-b ★★★[v103.1.5] 새 합성을 <b>굳힌다</b> — 관문을 지난 삼각형 그대로 스냅샷(실측 5~6ms). 굳힌 면의 재작성(Rebuild)은 스냅샷에서 시작해 원본 XML 없이도 그대로다.
        //   (스냅샷 재작성은 원본을 다시 읽는다 — 그래서 원본을 새 이름으로 쓰고 다시는 안 쓴다: 안 바뀐 원본이면 무엇을 눌러도 같은 내용이다 · 1012 실측)
        //   못 굳혀도 합성은 맞으므로 멈추지 않는다 — 요약에 ⚠로 알린다(우리 재작성 도우미는 굳히지 못한 XML 면을 건드리지 않는다)
        string frz = GradingBuilder.FreezeXmlBacked(cTin);
        int frzTris = -1; try { frzTris = cTin.GetTinProperties().NumberOfTriangles; } catch { }
        // 자가검증(검토 1012 중간 2) — 굳힌 뒤 삼각형 수가 넘긴 수와 같은가
        if (frz.Length == 0 && frzTris != nf) frz = $"굳힌 뒤 삼각형 수가 다르다({frzTris} ≠ 넘긴 {nf})";
        log.AppendLine(frz.Length == 0 ? $"⑤-b 굳힘: 새 합성에 스냅샷(삼각형 {frzTris}/{nf}) — 재작성은 원본 XML 없이도 그대로다"
                                       : $"⑤-b ⚠새 합성을 굳히지 못했다 — {frz}");
        string frzNote = frz.Length == 0 ? "" : $" · ⚠합성을 굳히지 못했다({frz}) — 진단 로그를 보여 주세요";

        // ⑧ 정지면_DH로 바꿔 끼운다 — 관문을 지난 뒤에만. 한 단계라도 못 하면 SwapFailed — 트랜잭션째 되돌려 명령 전 상태로
        var pW = (Autodesk.Civil.DatabaseServices.Surface)tr.GetObject(pTin.ObjectId, OpenMode.ForWrite);
        if (inS1)
        {
            GradingBuilder.EraseSurfacesByBaseName(tr, PlanName, groundId);
            var (left, leftNames) = Candidates(civilDoc, tr, PlanName, groundId);
            if (left.Count > 0) throw new SwapFailed($"지난 합성({string.Join(", ", leftNames)})을 못 지웠다(레이어 잠김?)");
        }
        else
        {
            GradingBuilder.EraseSurfacesByBaseName(tr, BaseName, groundId);
            var (left, leftNames) = Candidates(civilDoc, tr, BaseName, groundId);
            if (left.Count > 0) throw new SwapFailed($"낡은 {string.Join(", ", leftNames)}를 못 지웠다(레이어 잠김?)");
            string e1 = "";
            try { pW.Name = BaseName; } catch (System.Exception nx) { e1 = nx.GetType().Name; log.AppendLine("합성 전 이름 바꾸기 실패: " + nx); }
            if (e1.Length > 0 || pW.Name != BaseName)
                throw new SwapFailed($"정지면_DH 이름을 '{BaseName}'로 못 바꿨다({(e1.Length > 0 ? e1 : pW.Name)})");
        }
        string e2 = "";
        try { cTin.Name = PlanName; } catch (System.Exception nx) { e2 = nx.GetType().Name; log.AppendLine("합성 이름 바꾸기 실패: " + nx); }
        if (e2.Length > 0 || cTin.Name != PlanName)
            throw new SwapFailed($"합성은 지었는데 이름을 '{PlanName}'로 못 바꿨다({(e2.Length > 0 ? e2 : $"'{cTin.Name}'")})");
        // 정지면_DH가 늘 보이던 모양 그대로 — 합성 전 면의 스타일·레이어를 물려받는다(계획 검토 v103 H1 — 지울 지난 합성이 아니라 합성 전 면에서)
        try { cTin.StyleId = pW.StyleId; } catch (System.Exception sx) { log.AppendLine("스타일 물려받기 실패: " + sx.GetType().Name); }
        // ★[v103.0.1 · JACK 1002 «등고선 1cm 올려 그리기»] v103.0.1 전에 지은 합성 전 면은 원본 스타일이다 — 합성(정지면_DH)은 +1cm 사본으로(원본은 안 건드린다)
        try
        {
            var lid = GradingBuilder.LiftedContourStyle(tr, pW.StyleId, out string ln);
            if (!lid.IsNull && lid != pW.StyleId) { cTin.StyleId = lid; log.AppendLine("정지면_DH 스타일 — " + ln); }
        }
        catch (System.Exception lx) { log.AppendLine("등고선 +1cm 사본 실패(원본 스타일 그대로): " + lx.GetType().Name); }
        try { ((Autodesk.AutoCAD.DatabaseServices.Entity)cTin).LayerId = ((Autodesk.AutoCAD.DatabaseServices.Entity)pW).LayerId; } catch (System.Exception lx) { log.AppendLine("레이어 물려받기 실패: " + lx.GetType().Name); }
        int FinalNo(WallRec w) => renames.TryGetValue(w.No, out int kk) ? kk : w.No;   // ★[v103.1.14] 번호를 물려받을 새 옹벽은 물려받은 번호로(합성 표지 · 설명 · 요약)
        string nos = string.Join(",", useOk.Select(FinalNo));
        var forcedStep = res.Steps.FirstOrDefault(s => s.R.Clean?.OverForced == true);
        int forcedNo = forcedStep == null ? 0 : renames.TryGetValue(forcedStep.No, out int fk) ? fk : forcedStep.No;   // ★[v103.1.14 · 코드 검토 낮음] 물려받은 번호로
        // ★[v103.1.13 · 코드 검토 중간 2] 그 옹벽의 예산(판 전체 값에는 정지면 먼저 뒤집기가 들 수 있다 — 쌍 곁 탓으로 적지 않는다)
        string overNote = forcedStep != null
            ? string.Format(System.Globalization.CultureInfo.InvariantCulture, " · ⚠높이 오차 합 {0:F1}mm(1cm 넘음 · 옹벽 {1}) · 옹벽 높이 {2:F1}mm @{3}(Civil이 반드시 합칠 쌍 곁)",
                forcedStep.R.HeightBudget * 1000, forcedNo, forcedStep.R.Clean!.WallDz * 1000, forcedStep.R.Clean.WallAt) : "";
        try { cTin.Description = $"DH 옹벽 합성 — 옹벽 {useOk.Count}개({nos}) {System.DateTime.Now:yyyy-MM-dd HH:mm} · {GradingSettings.Version} · 판정 {res.Tier}({res.TierText}){overNote}"; } catch { }
        // ★★[v103.2 · 계획 4판] 이 합성의 일회용 번호 — 표지 넷째 값 + 넣은 칸마다 같은 값(⑨) · 종단 · 횡단의 옹벽면이 «이 합성의 뚜껑»인지 가른다
        string nonce = System.Guid.NewGuid().ToString("N").Substring(0, 12);
        try { MarkComposite(tr, cTin, $"{System.DateTime.Now:yyyy-MM-dd HH:mm:ss} · {GradingSettings.Version} · 판정 {res.Tier}{overNote}", pTin.ObjectId.Handle.ToString(), nos, nonce); }
        catch (System.Exception mx) { log.AppendLine("합성 표지 못 붙임: " + mx); throw new SwapFailed($"합성 표지를 못 붙였다({mx.GetType().Name})"); }
        int oldN = 0;
        try { var (o, _) = Candidates(civilDoc, tr, OldCompName, groundId); oldN = o.Count; if (oldN > 0) GradingBuilder.EraseSurfacesByBaseName(tr, OldCompName, groundId); } catch { }

        // ⑨ 목록 — 대기 → 넣음 · 바꾼 옹벽은 결과물 지우고 칸 지움 · 못 넣은 넣음 옹벽은 칸 지움(⚠)
        var news = useOk.Where(w => w.State == 0 || WallList.IsDeferred(w)).Select(FinalNo).ToList();   // ★[v103.1.12] 미룬 옹벽도 «새 옹벽»
        foreach (var w in useOk)
        {
            var st = res.Steps.FirstOrDefault(s => s.No == w.No);
            w.State = 1;
            w.P0Handle = p0h; w.Hold = "";   // ★[v103.1] 지금 P0 위에 합성했다
            // ★[v103.1.12 · 4판 낮음 5] 빈 값이면 «미룬 옹벽»(합성에 든 적 없음)으로 읽힌다 — 단계가 없을 때도 고정 표지
            w.LastNote = st != null ? $"판정 {st.R.Tier}({st.R.TierText})" : "판정 ?";
            w.Stamp = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            // ★★[v103.2 · 계획 2-2 · 4판] 뚜껑 · 일회용 번호 — 물려받기(⑨-b) 전 번호 M 칸에 적는다(물려받기가 칸째 K로 옮긴다 · 4판 검토 물음 1 확인)
            w.CompNonce = nonce;
            w.Cap = st?.R.CapTris != null ? DH.Grading.Core.PureWithWalls.CapOf(st.R.CapTris) : null;
            if (w.Cap != null) log.AppendLine($"⑨ 뚜껑 — 옹벽 {w.No}: {w.Cap.Count}면 · 테두리 변 {w.Cap.Edges.Count} · {w.Cap.Area:F3}㎡ · 번호 {nonce}");
        }
        string repNote = "";
        foreach (int rno in replaced)
        {
            var er = WallTags.Erase(db, tr, (n, r) => n == rno, false, groundId);
            walls.RemoveAll(x => x.No == rno);
            repNote += renames.ContainsValue(rno) ? $" · 옛 옹벽 {rno}를 이어 지은 옹벽으로 바꿨다(지움 {er.Text})" : $" · 옛 옹벽 {rno}를 바꿨다(지움 {er.Text})";
        }
        // ★★★[v103.1.14 · JACK 1014 ② «번호 그대로»] 이어 지은 새 옹벽이 옛 번호를 물려받는다 — 옛 옹벽을 지운 <b>뒤</b>(같은 트랜잭션에서 지운 표면 이름은 다시 줄 수 있다 — 1019 실측 E1)
        //   표면 이름 · 표면 표지 · 선 꼬리표 · 목록 칸. 합성 표지 · 설명은 위에서 이미 물려받은 번호로 적었다. 도중에 못 하면 바꿔 끼우기째 물린다(SwapFailed — 합성도 안 들어감)
        foreach (var (mNo, kNo) in renames)
        {
            if (!useOk.Any(w => w.No == mNo)) continue;   // 이번 합성에 못 든 새 옹벽 — 물려받기 없음(위 표지에도 안 들었다)
            try
            {
                if (walls.Any(x => x.No == kNo)) throw new SwapFailed($"옛 옹벽 {kNo}이 목록에 남아 새 옹벽 {mNo}이 번호를 못 물려받았다");
                var (rs, rl) = WallTags.Renumber(db, tr, mNo, kNo, groundId);
                if (!WallList.Renumber(walls, mNo, kNo)) throw new SwapFailed($"목록에서 옹벽 {mNo} → {kNo}를 못 했다");
                repNote += $" · 새 옹벽 {mNo}이 옹벽 {kNo}에 이어져 번호 {kNo}를 물려받았다(표면 {rs} · 선 {rl})";
                log.AppendLine($"⑨-b 번호 물려받기 — 새 옹벽 {mNo} → 옹벽 {kNo}(표면 {rs} · 선 {rl} · 목록 칸)");
            }
            catch (SwapFailed) { throw; }
            catch (System.Exception rx) { throw new SwapFailed($"새 옹벽 {mNo}이 옹벽 {kNo} 번호를 못 물려받았다({rx.GetType().Name}: {rx.Message})"); }
        }
        // ★[v103.1.14 · 계획 검토 낮음 6] 합성에 든 옹벽의 이어 짓기 칸은 비운다 — 물려받기를 못 한 칸에 남으면 목록이 판 3으로 남고 다음 이어 짓기가 없는 번호를 물려받으려 한다
        foreach (var w in useOk)
        {
            if (w.KeepNo > 0) { repNote += $" · ⚠새 옹벽 {w.No}은 옹벽 {w.KeepNo} 번호를 못 물려받아 번호 {w.No} 그대로"; log.AppendLine($"   ⚠옹벽 {w.No}은 옹벽 {w.KeepNo} 번호를 못 물려받았다 — 번호 {w.No} 그대로(②-b 줄)"); }
            w.KeepNo = 0; w.ReplaceNo2 = 0;
        }
        string dropNote = "";
        if (dropped.Count > 0)
        {
            // ★[v103.0 · 코드 검토 중간 3] 뺀 넣음 옹벽은 결과물도 지운다(합성에 없는 옹벽이 남아 보이지 않게) — 요약 맨 앞 ⚠ + 알림창(Run)
            // ★[v103.1.12 · 4판 H3] 면이 없어진 넣음 옹벽(dropped)만 — 이번 회에 안 넣은 보류 옹벽을 지우지 않게
            var dropNos = new System.Collections.Generic.HashSet<int>(droppedNos);
            var erD = WallTags.Erase(db, tr, (n, r) => dropNos.Contains(n), false, groundId);
            walls.RemoveAll(x => dropNos.Contains(x.No));
            _lostNote += (_lostNote.Length > 0 ? "\n" : "") + $"옹벽 {string.Join(",", dropNos)} — 면이 없어져 정지면_DH에서 뺌({string.Join(" · ", dropped)})";
            dropNote = $"⚠옹벽 {string.Join(",", dropNos)}을 정지면_DH에서 뺐다({string.Join(" · ", dropped)}) — 그 옹벽은 다시 지정하세요(결과물 지움 {erD.Text}) · ";
        }
        WallListStore.Save(db, tr, head, walls);

        // ⑩ 보이기 — 계획부지 생성이 끝날 때와 같게(JACK «옹벽 전체수정할때처럼» · 인터뷰 ④ 정지면_DH만)
        string vis;
        if (GradingSettings.ShowOnlyResultSurface)
        {
            GradingBuilder.IsolateSurfaces(tr, PlanName);
            string rb = ""; try { rb = GradingBuilder.RebuildSurfacesByBaseName(tr, BaseName); } catch (System.Exception rx) { rb = "실패 " + rx.GetType().Name; }
            // ★[v103.1.7 · 검토 낮음 3③] 스냅샷을 못 다뤘으면 바꿔 끼우기째 되돌린다(SwapFailed — 트랜잭션을 통째로 물린다)
            if (GradingBuilder.MustAbort(rb)) throw new SwapFailed($"합성 전 면을 다시 짓다 스냅샷을 못 다뤘다({SelfCheck.WarnBits(rb)})");
            vis = $"결과만 보기 — 정지면_DH만 켬 · 합성 전 면 다시 지음({rb})";
        }
        else
        {
            int a = 0, b = 0, c = 0;
            try { a = GradingBuilder.SetSurfaceVisible(tr, PlanName, true); } catch { }
            try { b = GradingBuilder.SetSurfaceVisible(tr, BaseName, false); } catch { }
            try { c = WallTags.SetWallSurfacesVisible(tr, false); } catch { }
            vis = $"정지면_DH 켬 {a} · 숨김 합성 전 {b} · 옹벽 표면 {c}";
        }
        try { GradingBuilder.SetSurfaceVisible(tr, SectionCommand.PurePadSurfaceBase, false); } catch { }
        try { GradingBuilder.SetSurfaceVisible(tr, PureWithWallsBuilder.Name, false); } catch { }   // ★[v103.2] 옹벽면도 순수면 — 늘 숨김
        try { GradingBuilder.SetSurfaceVisible(tr, WallExcavBuilder.Name, false); } catch { }       // ★[v103.3] 옹벽 터파기면도 늘 숨김
        // ★[v103.1.12 · JACK 1015 ③ · 4판 중간 7] 합성에 안 든 «넣음» 옹벽(보류 · 미룸)은 가상옹벽 면을 다시 켠다 — 정지면_DH에도 없고 제 면도 숨으면 아무 데도 안 보인다
        try
        {
            var inC = new System.Collections.Generic.HashSet<int>(useOk.Select(w => w.No));
            var outW = walls.Where(w => w.State == 1 && !inC.Contains(w.No)).ToList();
            if (outW.Count > 0) vis += $" · 합성 밖 옹벽의 옹벽 면 {string.Join(" ", outW.Select(w => WallTags.ShowOutsideWall(tr, w, p0h)))}";
        }
        catch (System.Exception vx) { vis += " · (합성 밖 옹벽 면 켜기 실패 " + vx.GetType().Name + ")"; }
        log.AppendLine($"⑥ 바꿔 끼움: {(inS1 ? "지난 합성을 지우고 새 합성을" : $"정지면_DH를 '{BaseName}'로 숨기고 합성을")} 정지면_DH로 · 옛 판 합성지표면_DH {oldN}개 치움 · 보이기: {vis}");
        log.AppendLine($"⑦ 목록 — 넣음 {string.Join(",", walls.Where(w => w.State == 1).Select(w => w.No))}{repNote}{(dropped.Count > 0 ? " · ⚠뺀 옹벽 " + string.Join(" · ", dropped) : "")}");

        string excNote = "";
        try
        {
            var (xa, _) = Candidates(civilDoc, tr, ViewSurfaceCommand.AllName, groundId);
            var (xb, _) = Candidates(civilDoc, tr, ViewSurfaceCommand.ExcavAllName, groundId);
            if (xa.Count + xb.Count > 0) excNote = " · 터파기 보기 합성면(전체면_DH·터파기전체_DH)은 옛 정지면을 붙여 넣은 것이라 터파기를 다시 하면 새 정지면으로 바뀐다";
        }
        catch { }
        string why2 = string.Join(" · ", res.Steps.Select(s => string.Join(" · ", new[] { s.R.Clean?.Tier2Why ?? "", s.R.Fail }.Where(x => x.Length > 0))).Where(x => x.Length > 0));
        // ★[v103.1.13] 판정 2가 «정지면 먼저 뒤집기» 탓일 수 있다(옹벽마다는 판정 1) — 까닭에 넣는다
        var ofx = res.Outside;
        if (ofx.Flipped > 0 && ofx.MaxDz > 1e-3)
            why2 = (why2.Length > 0 ? why2 + " · " : "") + string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "옹벽과 떨어진 정지면_DH에서 Civil이 가져오며 고칠 면 {0}쌍을 Civil처럼 먼저 뒤집음 — 그 자리 높이 {1:F1}mm @{2}", ofx.Flipped, ofx.MaxDz * 1000, ofx.At);
        string outNote = ofx.Flipped > 0 ? $"(Civil이 고칠 면 {ofx.Flipped}쌍은 먼저 뒤집음)" : "";
        string tierNote = res.Tier == 1 ? "" : $" · ⚠<b>거의</b>({why2})";
        string overHead = forcedStep != null
            ? string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "<b>⚠1cm 넘은 자리</b>(옹벽 {0}): 높이 오차 합 {1:F1}mm(한도 {2:F0}mm) — 옹벽 높이 {3:F1}mm @{4} · Civil이 0.1mm 안 두 점을 반드시 합치는 자리 곁 · ",
                forcedNo, forcedStep.R.HeightBudget * 1000, res.HeightLimit * 1000, forcedStep.R.Clean!.WallDz * 1000, forcedStep.R.Clean.WallAt)
            : "";
        _lastOk = true;
        if (at != null) at.Ok = true;   // ★[v103.1.12] 정식 회도 «됨»(빠뜨리면 부른 쪽이 성공을 «전체 못 함»으로 읽는다 — 화면 없는 시험 D1 1015 첫 판에서 잡음)
        _compId = cTin.ObjectId; _compGround = groundId;
        return _heldNote + dropNote + overHead + $"<b>정지면_DH에 옹벽 {useOk.Count}개({nos})를 합성했다</b>{(news.Count > 0 ? $" — 새 옹벽 {string.Join(",", news)}" : " — 다시 합성")}{(_rebuiltN > 0 ? $" · 새 정지면에 다시 지은 옹벽 {_rebuiltN}개" : "")}{repNote}"
             + $" · 판정 {res.Tier}({res.TierText}){tierNote} · 정지면 {planAll.Count}면 중 옹벽 둘레 {res.Steps.Sum(s => s.R.Touched)}면을 다시 짓고 나머지는 그대로{outNote}"
             + $" · 점 {np} · 삼각형 {nf} Civil이 그대로 받음 · 높이 오차 합 {res.HeightBudget * 1000:F2}mm"
             + $" · 합성 전 정지면은 '{BaseName}'로 숨겨 둠{excNote}{frzNote}";
    }

    /// <summary>★★[v101.1 · JACK 0929 «합성을 자동으로 풀기»] 옹벽 합성을 푼다 — 정지면_DH(합성)를 지우고 <see cref="BaseName"/>을 정지면_DH로 되돌린다.
    /// <para>합성 전 면이 없으면 아무것도 안 한다. v101.0이 따로 만든 합성지표면_DH도 치운다(켜져 있었으면 그 판이 숨긴 정지면_DH를 켠다).
    /// 계획부지 생성 «마지막 구역 다시»(옹벽·사면 변환)가 먼저 부르고, 옹벽 변환 시작(<see cref="WallDaylightBuilder.EraseOld"/>)이 안전망으로 한 번 더 부른다.</para>
    /// <para>★[코드 검토 v101.1 · 중간 1] <b>짝이 맞을 때만</b> 푼다 — 합성 전 면이 하나이고, 지금 정지면_DH가 전부 그 면의 합성(표지 + 핸들)일 때.
    /// 표지 없는 정지면_DH는 새 계획면이다 — 그것을 지우고 낡은 합성 전 면을 되살리면 새 계획면을 말없이 잃는다.
    /// 바꾸기 전에 다 쓰기로 열어 보고, 하나라도 못 열면(잠긴 레이어) 아무것도 안 바꾼다.</para></summary>
    /// <returns>한 일(명령줄·로그용 — 없으면 빈 문자열)과 됐나. 거짓이면 부른 쪽이 트랜잭션을 되돌려야 한다(계획부지 생성은 멈춘다).</returns>
    internal static (string Note, bool Ok) UndoComposite(Database db, Transaction tr, ObjectId groundId)
    {
        var civilDoc = Autodesk.Civil.ApplicationServices.CivilApplication.ActiveDocument;
        var notes = new System.Collections.Generic.List<string>();
        (string, bool) Done(bool ok, string? add = null) { if (add != null) notes.Add(add); return (string.Join(" · ", notes), ok); }
        var (oIds, _) = Candidates(civilDoc, tr, OldCompName, groundId);
        if (oIds.Count > 0)
        {
            bool wasOn = false;
            foreach (var id in oIds) try { if (tr.GetObject(id, OpenMode.ForRead) is Autodesk.AutoCAD.DatabaseServices.Entity e && e.Visible) wasOn = true; } catch { }
            GradingBuilder.EraseSurfacesByBaseName(tr, OldCompName, groundId);
            if (wasOn) GradingBuilder.SetSurfaceVisible(tr, PlanName, true);
            notes.Add($"옛 판 합성지표면_DH {oIds.Count}개 치움{(wasOn ? "(정지면_DH를 다시 켬)" : "")}");
        }
        var (bIds, bNames) = Candidates(civilDoc, tr, BaseName, groundId);
        var (pIds, pNames) = Candidates(civilDoc, tr, PlanName, groundId);
        if (bIds.Count == 0)
        {
            // 합성 전 면 없이 합성 표지가 붙은 정지면_DH — 풀 수 없다(손으로 지웠거나 예상 못 한 실패). 멈추지는 않는다 —
            //   정지면을 통째로 다시 짓는 길이면 새 면으로 바뀌어 빠져나간다. 폴리곤만 만드는 옹벽 변환은 순수옹벽 관문이 같은 표지를 보고 멈춘다
            for (int i = 0; i < pIds.Count; i++)
                if (IsComposite(tr, tr.GetObject(pIds[i], OpenMode.ForRead)))
                    return Done(true, $"⚠'{pNames[i]}'는 합성 전 면 없이 남은 옹벽 합성이라 풀 수 없다 — 정지면을 다시 지으면 새 면으로 바뀐다"
                                    + $"(폴리곤만 만드는 옹벽 변환은 이 면으로 순수옹벽·합성을 안 짓는다 · {B1Help})");
            return Done(true);
        }
        // ★[코드 검토 v101.1 · 낮음 5] 여럿이면 어느 것을 되살릴지 모른다 — 고르지 않는다
        if (bIds.Count > 1)
            return Done(false, $"⚠합성 전 면이 {bIds.Count}개({string.Join(", ", bNames)}) — 어느 것을 되살릴지 몰라 풀지 않는다(하나만 남기거나 {PairHelp})");
        string bh = bIds[0].Handle.ToString();
        for (int i = 0; i < pIds.Count; i++)
        {
            var o = tr.GetObject(pIds[i], OpenMode.ForRead);
            if (!IsComposite(tr, o) || CompositeBaseHandle(tr, o) != bh)
                return Done(false, $"⚠'{pNames[i]}'가 합성 전 면('{bNames[0]}')의 합성이 아니다(짝이 어긋남) — 풀지 않는다 · {PairHelp}");
        }
        Autodesk.Civil.DatabaseServices.Surface b0;
        var olds = new System.Collections.Generic.List<Autodesk.AutoCAD.DatabaseServices.DBObject>();
        try
        {
            b0 = (Autodesk.Civil.DatabaseServices.Surface)tr.GetObject(bIds[0], OpenMode.ForWrite);
            foreach (var id in pIds) olds.Add(tr.GetObject(id, OpenMode.ForWrite));
        }
        catch (System.Exception ox)
        { return Done(false, $"⚠합성 전 면이나 지난 합성을 못 열었다({ox.GetType().Name} · 레이어 잠김?) — 풀지 않는다"); }
        foreach (var o in olds) o.Erase();                                   // 지난 합성(연 것만)
        var (left, leftNames) = Candidates(civilDoc, tr, PlanName, groundId);
        if (left.Count > 0)
            return Done(false, $"⚠지난 옹벽 합성({string.Join(", ", leftNames)})이 남아 합성을 못 풀었다");
        string nx = "";
        try { b0.Name = PlanName; } catch (System.Exception ex) { nx = ex.GetType().Name; }
        if (nx.Length > 0 || b0.Name != PlanName)
            return Done(false, $"⚠합성 전 면 이름을 {PlanName}로 못 바꿨다({(nx.Length > 0 ? nx : $"'{b0.Name}'")})");
        GradingBuilder.SetSurfaceVisible(tr, PlanName, true);
        // ★[v103.1 · v103.0.1 검토 낮음 1] v103.0.1 전에 지은 합성 전 면은 원본 스타일이다 — 되살린 정지면_DH도 등고선 +1cm 사본으로(재생성이 실패해도 톱니가 안 돌아오게)
        try { var lid = GradingBuilder.LiftedContourStyle(tr, b0.StyleId, out _); if (!lid.IsNull && lid != b0.StyleId) b0.StyleId = lid; } catch { }
        // 숨겨 둔 사이 붙은 ⚠(정의 구식) 풀기 — 구식일 때만(25만 면 재작성 + 스냅샷은 수 초 · 곧 새로 지을 면이면 헛일 — 계획 검토 v101.1 · 낮음 5)
        string rbU = "";
        try { if (b0.IsOutOfDate) rbU = GradingBuilder.RebuildSurfacesByBaseName(tr, PlanName); } catch { }
        // ★[v103.1.7 · 검토 낮음 3③] 그 재작성이 스냅샷을 못 다뤘으면 «못 풀었다» — 부른 쪽이 트랜잭션을 되돌린다(그대로 커밋하면 다음 재작성에서 죽거나 바뀐 모양이 굳는다)
        if (GradingBuilder.MustAbort(rbU)) return Done(false, $"⚠되살린 합성 전 면을 다시 짓다 스냅샷을 못 다뤘다({SelfCheck.WarnBits(rbU)}) — 풀지 않는다");
        return Done(true, "지난 옹벽 합성을 풀었다 — 정지면_DH를 합성 전으로 되돌렸다(옹벽 변환이 옹벽을 지으면 이어서 다시 합성한다)");
    }

    /// <summary>못 한 길 — 바꿔 끼우기 전이거나 되돌렸으므로 정지면_DH·보이기는 명령 전 그대로다.</summary>
    private static string Fail(string why) => "⚠" + why + " · 정지면_DH·보이기는 명령 전 그대로다";

    /// <summary>이름이 baseName 또는 baseName_N인 지표면(원지반 제외) — 가장 큰 번호가 먼저.</summary>
    internal static (System.Collections.Generic.List<ObjectId> Ids, System.Collections.Generic.List<string> Names) Candidates(
        Autodesk.Civil.ApplicationServices.CivilDocument civilDoc, Transaction tr, string baseName, ObjectId groundId)
    {
        var hits = new System.Collections.Generic.List<(ObjectId Id, string Name, int N)>();
        foreach (ObjectId sid in civilDoc.GetSurfaceIds())
        {
            if (sid == groundId) continue;
            if (tr.GetObject(sid, OpenMode.ForRead) is not Autodesk.Civil.DatabaseServices.Surface s) continue;
            if (s.Name == baseName) hits.Add((sid, s.Name, 0));
            else if (s.Name.StartsWith(baseName + "_") && int.TryParse(s.Name.Substring(baseName.Length + 1), out int nn)) hits.Add((sid, s.Name, nn));
        }
        hits.Sort((a, b) => b.N.CompareTo(a.N));
        return (hits.ConvertAll(h => h.Id), hits.ConvertAll(h => h.Name));
    }

    /// <summary>임시 이름(지금 것·옛 것)인 지표면을 지운다(지난 실행이 터져 남긴 것).</summary>
    private static int EraseTemp(Transaction tr)
    {
        var civilDoc = Autodesk.Civil.ApplicationServices.CivilApplication.ActiveDocument;
        var victims = new System.Collections.Generic.List<ObjectId>();
        foreach (ObjectId sid in civilDoc.GetSurfaceIds())
            if (tr.GetObject(sid, OpenMode.ForRead) is Autodesk.Civil.DatabaseServices.Surface s && (s.Name == TempName || s.Name == OldTempName)) victims.Add(sid);
        int n = 0;
        foreach (var sid in victims) { try { tr.GetObject(sid, OpenMode.ForWrite).Erase(); n++; } catch { } }
        return n;
    }
}
