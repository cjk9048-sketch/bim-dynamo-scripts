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
    static void MarkComposite(Transaction tr, Autodesk.AutoCAD.DatabaseServices.DBObject o, string info, string baseHandle, string nos = "")
    {
        if (!o.IsWriteEnabled) o.UpgradeOpen();
        if (o.ExtensionDictionary.IsNull) o.CreateExtensionDictionary();
        var d = (DBDictionary)tr.GetObject(o.ExtensionDictionary, OpenMode.ForWrite);
        if (d.Contains(TagKey)) throw new System.InvalidOperationException("새로 지은 면에 합성 표지가 벌써 있다");
        var rb = new ResultBuffer(new TypedValue((int)DxfCode.Text, info), new TypedValue((int)DxfCode.Text, baseHandle));
        if (!string.IsNullOrEmpty(nos)) rb.Add(new TypedValue((int)DxfCode.Text, nos));
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
    const string RebuildFailTag = "다시 짓기 실패";
    /// <summary>★[v103.1 · 코드 검토 낮음 4] 이번 실행이 다시 지은 순수옹벽 — 합성이 터져 트랜잭션이 되돌려져도 LandXML로 따로 생긴 면은 남을 수 있어 그때 지운다.</summary>
    static readonly System.Collections.Generic.List<ObjectId> _rebuiltPure = new();
    static ObjectId _compId = ObjectId.Null, _compGround = ObjectId.Null;

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
    const string XmlName = "DHWALLCOMP_합성.xml";

    [CommandMethod("DHWALLCOMP")]
    public void Run()
    {
        // ★[v103.0.1 · JACK 1002 스샷 «저런 정보창»] 계획부지 생성처럼 이벤트 뷰어 <b>자동 알림만</b> 끈다(기록은 남는다 · EventViewerMute) —
        //   합성 가져오기(LandXML)가 남기는 정보 한 줄(«V1.1 이전 알고리즘 … 인접이 없습니다»)에 창이 떴다. 옹벽 변환이 이어 태우는 명령이라 DoGrade의 끄기 밖이었다
        var evPrev = EventViewerMute.Begin();
        try { RunInner(); }
        finally { EventViewerMute.End(evPrev); }
    }

    void RunInner()
    {
        Document doc = AcadApp.DocumentManager.MdiActiveDocument;
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
        _lastOk = false; _rejectedNo = 0;
        _made = ObjectId.Null;
        _rebuiltN = 0; _heldNote = ""; _compId = ObjectId.Null; _compGround = ObjectId.Null; _rebuiltPure.Clear();
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
            summary = $"⚠옹벽 합성 못 함 — {sf.Message} · 바꿔 끼우기를 통째로 되돌렸다(정지면_DH·보이기는 명령 전 그대로)";
            log.AppendLine(sf.ToString());
        }
        catch (System.Exception ex)
        {
            crashed = true; _lastOk = false; _rejectedNo = 0;
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
                    tr2.Commit();
                    if (n > 0) summary += $" · 남은 임시 면 {n}개 지움";
                }
            }
            catch (System.Exception ex2) { log.AppendLine("임시 면 치우기 실패: " + ex2); }
        }
        // ★[v103.1 · 검토 v103.1 중간 7] 정지면을 다시 지은 뒤 옹벽을 다시 합성했으면 토량을 다시 잰다(계획부지 생성 팝업 토량은 옹벽 합성 전)
        if (_lastOk && _rebuiltN > 0 && !_compId.IsNull && !_compGround.IsNull)
        {
            try { summary += " · 옹벽 합성 뒤 " + CreateGradingCommand.ComputeVolumes(db, _compGround, _compId).Replace("\n", " "); }
            catch (System.Exception vx) { log.AppendLine("토량 다시 재기 실패: " + vx.GetType().Name); }
        }
        summary += $" · 전체 {sw.ElapsedMilliseconds / 1000.0:F1}초";
        // ★[v102.2 · 계획 검토 중간 8] 옹벽 변환이 이어서 태운 합성이 못 했으면 알림창 — 버튼이 없어져 명령줄 한 줄로는 묻힌다
        bool auto = GradingSettings.AutoCompDoc.Length > 0 && GradingSettings.AutoCompDoc == doc.Name;
        int autoNo = GradingSettings.AutoCompNo;
        GradingSettings.AutoCompDoc = ""; GradingSettings.AutoCompNo = 0;
        // ★[v103.0] 쌓기 — 못 하면 새 옹벽만 빠지고 지난 옹벽이 든 정지면_DH는 그대로다
        // ★[v103.0 · 코드 검토 중간 3] 합성은 됐는데 넣은 옹벽이 빠졌으면(⚠) 손으로 친 합성이어도 알린다 — 명령줄 한 줄로는 묻힌다
        if ((auto && (crashed || summary.StartsWith("⚠"))) || (_lastOk && summary.StartsWith("⚠")))
            try
            {
                string head0 = _lastOk ? "옹벽 합성은 했지만 빠진 옹벽이 있습니다."
                             : _rejectedNo > 0 ? $"옹벽 {_rejectedNo}를 정지면_DH에 못 넣어 버렸습니다(지난 옹벽과 정지면_DH는 그대로)."
                             : $"옹벽 {(autoNo > 0 ? autoNo + " " : "")}합성을 못 했습니다(지난 옹벽과 정지면_DH는 그대로).";
                string tail0 = _lastOk ? "빠진 옹벽은 옹벽 변환에서 다시 지정하세요."
                             : _rejectedNo > 0 ? "버린 옹벽은 합성으로 돌아오지 않습니다 — 옹벽 변환을 다시 하세요."
                             : "다시 하려면 옹벽 변환을 다시 하거나 명령줄에 DHWALLCOMP를 치세요.";
                AcadApp.ShowAlertDialog(head0 + "\n\n" + System.Text.RegularExpressions.Regex.Replace(summary, "<[^>]+>", "") + "\n\n" + tail0 + " 자세한 것은 " + LogName);
            }
            catch { }
        log.AppendLine("■ " + summary);
        try { System.IO.File.WriteAllText(logPath, log.ToString()); WallDaylightBuilder.ArchiveDumpPublic(logPath); } catch { }
        try { ed.WriteMessage($"\n[옹벽 합성] {summary.Replace("<b>", "").Replace("</b>", "")}\n  로그: {logPath}\n"); } catch { }
        try { ed.Regen(); } catch { }
    }

    /// <summary>재료 모으기 → Core 합성 → LandXML(임시 이름) → Civil → 되읽기 관문 → 정지면_DH로 바꿔 끼우기 → 보이기. 못 하면 까닭(⚠) 문자열.
    /// <para>★★★[v103.0 · JACK 0930 «옹벽이 남아야»] 재료는 <b>옹벽 목록</b> — «넣음» 옹벽(바꿀 것 빼고) + «대기» 옹벽을 번호 차례로 <b>합성 전 면 위에</b> 짓는다
    /// (<see cref="WallDaylight.ComposeWalls"/> — 더하기 = 다시 짓기의 마지막 단계). 세션 «짝»(LastWall*)은 안 쓴다 — Civil을 다시 켜도 같다.
    /// 대기 옹벽이 못 들어가면 그 옹벽만 버린다(결과물 지움 · 칸 지움) — 지난 옹벽과 정지면_DH는 그대로.</para></summary>
    private static string Build(Database db, Transaction tr, System.Text.StringBuilder log, string dir)
    {
        var civilDoc = Autodesk.Civil.ApplicationServices.CivilApplication.ActiveDocument;
        int leftover = EraseTemp(tr);
        if (leftover > 0) log.AppendLine($"   지난번에 남은 임시 면 {leftover}개 지움");

        // ① 옹벽 목록
        if (!WallListStore.TryLoad(db, tr, out var head, out var walls, out string lwhy)) return Fail("옹벽 목록을 못 읽었다 — " + lwhy);
        var replaced = new System.Collections.Generic.HashSet<int>(walls.Where(w => w.State == 0 && w.Block.Length == 0 && w.ReplaceNo > 0).Select(w => w.ReplaceNo));
        var use = walls.Where(w => (w.State == 1 && !replaced.Contains(w.No)) || (w.State == 0 && w.Block.Length == 0)).OrderBy(w => w.No).ToList();
        var blocked = walls.Where(w => w.State == 0 && w.Block.Length > 0).ToList();
        log.AppendLine($"① 옹벽 목록 — 넣음 {walls.Count(w => w.State == 1)}({string.Join(",", walls.Where(w => w.State == 1).Select(w => w.No))}) · 대기 {walls.Count(w => w.State == 0)}"
            + (replaced.Count > 0 ? $" · 바꿀 옹벽 {string.Join(",", replaced)}" : "") + (blocked.Count > 0 ? $" · 합성 막힌 대기 {string.Join(",", blocked.Select(w => w.No))}" : "")
            + $" → 합성할 옹벽 {use.Count}개({string.Join(",", use.Select(w => w.No))}) · 다음 번호 {head.NextNo}");
        if (use.Count == 0)
            return Fail(walls.Count == 0 ? "옹벽 목록이 비었다 — 옹벽 변환(부분 지정)으로 옹벽을 먼저 지으세요"
                                         : $"합성할 옹벽이 없다{(blocked.Count > 0 ? $" — 대기 옹벽 {string.Join(",", blocked.Select(w => w.No))}는 합성이 막혔다({blocked[0].Block})" : "")}");

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
        try
        {
            if (pTin.IsOutOfDate)
            {
                string rb = GradingBuilder.RebuildSurfacesByBaseName(tr, pTin.Name);
                log.AppendLine($"   '{pTin.Name}'가 구식이라 다시 지음: {rb}");
            }
        }
        catch (System.Exception rx) { log.AppendLine($"   다시 짓기 실패 {rx.GetType().Name}"); }
        try { if (pTin.IsOutOfDate) return Fail($"'{pTin.Name}'가 낡음(Out of date) — 다시 지어도 안 풀린다"); } catch { }
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
        var heldNow = use.Where(w => w.Hold.Length > 0 && !w.Hold.StartsWith(RebuildFailTag)).ToList();
        foreach (var w in heldNow) use.Remove(w);
        var failedNow = new System.Collections.Generic.List<WallRec>();
        _rebuiltN = 0;
        var toRebuild = use.Where(w => w.State == 1 && w.P0Handle != p0h).ToList();
        if (toRebuild.Count > 0)
        {
            var gIfc = new CachedGroundSurface(gTin);
            foreach (var w in toRebuild)
            {
                var swR = System.Diagnostics.Stopwatch.StartNew();
                string rb = RebuildWall(db, tr, w, head.PlanHandle, gIfc, groundId, log);
                if (rb.Length > 0) { w.Hold = RebuildFailTag + " — " + rb; use.Remove(w); failedNow.Add(w); log.AppendLine($"   ⚠옹벽 {w.No} 보류 — {rb}"); }
                else { w.P0Handle = p0h; w.Hold = ""; _rebuiltN++; log.AppendLine($"   옹벽 {w.No}를 새 정지면에 다시 지었다 · {swR.ElapsedMilliseconds}ms"); }
            }
            // 다시 지은 것 · 보류는 적어 둔다 — 아래 합성이 못 해도(Fail도 커밋) 옹벽은 새 정지면에 맞게 남는다
            try { WallListStore.Save(db, tr, head, walls); } catch (System.Exception sx) { log.AppendLine("목록 저장 실패: " + sx.GetType().Name); }
        }
        _heldNote = (heldNow.Count > 0 ? $"⚠옹벽 {string.Join(",", heldNow.Select(w => w.No))}은 서 있던 단이 움직여 보류라 합성에서 뺐다({string.Join(" · ", heldNow.Select(w => $"{w.No}: {w.Hold}"))}) — 사면 변환으로 되돌리거나 옹벽 변환으로 다시 지정하세요 · " : "")
                  + (failedNow.Count > 0 ? $"⚠옹벽 {string.Join(",", failedNow.Select(w => w.No))}을 새 정지면에 다시 짓지 못해 이번 합성에서 뺐다({string.Join(" · ", failedNow.Select(w => $"{w.No}: {w.Hold}"))}) — 옹벽은 목록에 그대로 · 명령줄 DHWALLCOMP나 계획부지 생성 «다시»로 다시 시도하세요(사면 변환은 그 옹벽을 지웁니다) · " : "");
        if (use.Count == 0)
            return Fail($"합성할 옹벽이 없다 — {_heldNote}");

        // 대기 옹벽을 버린다 — 결과물 지움 · 칸 지움 · 목록 저장(이 트랜잭션은 Fail이어도 커밋된다 — 바꿔 끼우기 전이라 남는 것은 이것뿐)
        string Reject(WallRec w, string why)
        {
            _rejectedNo = w.No;
            var er = WallTags.Erase(db, tr, (n, r) => n == w.No, false, groundId);
            walls.RemoveAll(x => x.No == w.No);
            try { WallListStore.Save(db, tr, head, walls); } catch (System.Exception sx) { log.AppendLine("목록 저장 실패: " + sx.GetType().Name); }
            log.AppendLine($"   대기 옹벽 {w.No} 버림 — {why} · {er.Text}");
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
                        try { if (wt.IsOutOfDate) { GradingBuilder.RebuildSurfacesByBaseName(tr, wt.Name); log.AppendLine($"   '{wt.Name}'가 낡아 다시 지음"); } } catch { }
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
                if (w.State == 0) return Reject(w, bad);
                if (gone) dropped.Add($"옹벽 {w.No}({bad})");
                else
                {
                    // 넣은 옹벽을 지금 못 읽는다(일시적일 수 있다) — 빼지 않는다. 이번 새 옹벽이 있으면 그것을 버리고 지난 합성은 그대로 · 없으면 멈춘다
                    var nw = use.LastOrDefault(x => x.State == 0);
                    if (nw != null) return Reject(nw, $"넣은 옹벽 {w.No}를 지금 못 읽는다({bad}) — 지난 합성을 그대로 둔다");
                    return Fail($"넣은 옹벽 {w.No}를 지금 못 읽는다 — {bad}");
                }
            }
        }
        if (dropped.Count > 0) log.AppendLine("   ⚠넣음 옹벽을 뺀다 — " + string.Join(" · ", dropped));
        if (inputs.Count == 0) return Fail("합성할 수 있는 옹벽이 없다 — " + string.Join(" · ", dropped));
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
            if (fw != null && fw.State == 0) return Reject(fw, res.Fail);
            // ★[v103.0 · 코드 검토 낮음 6] 넣음 옹벽이 걸렸어도 이번에 새 옹벽이 있으면 그 탓일 수 있다(바꿈으로 차례가 바뀜 등) — 새 옹벽을 버리고 지난 합성은 그대로
            var newest0 = useOk.LastOrDefault(w => w.State == 0);
            if (newest0 != null) return Reject(newest0, $"넣은 옹벽 {failNo}가 이번 옹벽과 함께 안 들어간다 — {res.Fail}");
            return Fail("옹벽 합성을 못 만들었다 — " + res.Fail);
        }

        // ⑥ LandXML → Civil(임시 이름 — 정지면_DH는 아직 안 건드린다)
        var all = res.All;
        string xml = System.IO.Path.Combine(dir, XmlName);
        WallDaylight.WriteLandXmlTin(xml, "PUREWALL", all, 1e-7, out int np, out int nf, out int nd);
        // ★[v103.0 · 코드 검토 낮음 8] 여기부터 못 하면 이번 새 옹벽(대기)을 버린다 — 대기 칸이 남아 다음 옹벽 변환까지 떠돌지 않게
        string FailNew(string why) { var nw = useOk.LastOrDefault(w => w.State == 0); return nw != null ? Reject(nw, why) : Fail(why); }
        if (nd > 0) return FailNew($"합성을 못 만들었다 — 넓이 0이라 뺀 면 {nd}개");
        sw.Restart();
        ObjectId cid = TinSurface.CreateFromLandXML(db, TempName, xml, "PUREWALL");
        if (cid.IsNull) return FailNew("합성을 못 만들었다 — CreateFromLandXML이 빈 ObjectId를 돌려줬다");
        _made = cid;
        var cTin = (TinSurface)tr.GetObject(cid, OpenMode.ForWrite);
        long tCreate = sw.ElapsedMilliseconds;
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
        log.AppendLine("⑤ 되읽기: " + back);
        if (!gateOk)
        {
            Drop();
            var newest = useOk.LastOrDefault(w => w.State == 0);
            if (newest != null) return Reject(newest, "Civil이 넘긴 합성을 그대로 안 받았다 — " + back);
            return Fail("Civil이 넘긴 합성을 그대로 안 받았다 — 새 면은 지웠다 · " + back);
        }

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
        string nos = string.Join(",", useOk.Select(w => w.No));
        var forcedStep = res.Steps.FirstOrDefault(s => s.R.Clean?.OverForced == true);
        string overNote = forcedStep != null
            ? string.Format(System.Globalization.CultureInfo.InvariantCulture, " · ⚠높이 오차 합 {0:F1}mm(1cm 넘음 · 옹벽 {1}) · 옹벽 높이 {2:F1}mm @{3}(Civil이 반드시 합칠 쌍 곁)",
                res.HeightBudget * 1000, forcedStep.No, forcedStep.R.Clean!.WallDz * 1000, forcedStep.R.Clean.WallAt) : "";
        try { cTin.Description = $"DH 옹벽 합성 — 옹벽 {useOk.Count}개({nos}) {System.DateTime.Now:yyyy-MM-dd HH:mm} · {GradingSettings.Version} · 판정 {res.Tier}({res.TierText}){overNote}"; } catch { }
        try { MarkComposite(tr, cTin, $"{System.DateTime.Now:yyyy-MM-dd HH:mm:ss} · {GradingSettings.Version} · 판정 {res.Tier}{overNote}", pTin.ObjectId.Handle.ToString(), nos); }
        catch (System.Exception mx) { log.AppendLine("합성 표지 못 붙임: " + mx); throw new SwapFailed($"합성 표지를 못 붙였다({mx.GetType().Name})"); }
        int oldN = 0;
        try { var (o, _) = Candidates(civilDoc, tr, OldCompName, groundId); oldN = o.Count; if (oldN > 0) GradingBuilder.EraseSurfacesByBaseName(tr, OldCompName, groundId); } catch { }

        // ⑨ 목록 — 대기 → 넣음 · 바꾼 옹벽은 결과물 지우고 칸 지움 · 못 넣은 넣음 옹벽은 칸 지움(⚠)
        var news = useOk.Where(w => w.State == 0).Select(w => w.No).ToList();
        foreach (var w in useOk)
        {
            var st = res.Steps.FirstOrDefault(s => s.No == w.No);
            w.State = 1;
            w.P0Handle = p0h; w.Hold = "";   // ★[v103.1] 지금 P0 위에 합성했다
            w.LastNote = st != null ? $"판정 {st.R.Tier}({st.R.TierText})" : "";
            w.Stamp = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        }
        string repNote = "";
        foreach (int rno in replaced)
        {
            var er = WallTags.Erase(db, tr, (n, r) => n == rno, false, groundId);
            walls.RemoveAll(x => x.No == rno);
            repNote += $" · 옛 옹벽 {rno}를 바꿨다(지움 {er.Text})";
        }
        string dropNote = "";
        if (dropped.Count > 0)
        {
            // ★[v103.0 · 코드 검토 중간 3] 뺀 넣음 옹벽은 결과물도 지운다(합성에 없는 옹벽이 남아 보이지 않게) — 요약 맨 앞 ⚠ + 알림창(Run)
            var dropNos = new System.Collections.Generic.HashSet<int>(use.Where(w => w.State == 1 && !useOk.Contains(w)).Select(w => w.No));
            var erD = WallTags.Erase(db, tr, (n, r) => dropNos.Contains(n), false, groundId);
            walls.RemoveAll(x => dropNos.Contains(x.No));
            dropNote = $"⚠옹벽 {string.Join(",", dropNos)}을 정지면_DH에서 뺐다({string.Join(" · ", dropped)}) — 그 옹벽은 다시 지정하세요(결과물 지움 {erD.Text}) · ";
        }
        WallListStore.Save(db, tr, head, walls);

        // ⑩ 보이기 — 계획부지 생성이 끝날 때와 같게(JACK «옹벽 전체수정할때처럼» · 인터뷰 ④ 정지면_DH만)
        string vis;
        if (GradingSettings.ShowOnlyResultSurface)
        {
            GradingBuilder.IsolateSurfaces(tr, PlanName);
            string rb = ""; try { rb = GradingBuilder.RebuildSurfacesByBaseName(tr, BaseName); } catch (System.Exception rx) { rb = "실패 " + rx.GetType().Name; }
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
        string tierNote = res.Tier == 1 ? "" : $" · ⚠<b>거의</b>({why2})";
        string overHead = forcedStep != null
            ? string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "<b>⚠1cm 넘은 자리</b>(옹벽 {0}): 높이 오차 합 {1:F1}mm(한도 {2:F0}mm) — 옹벽 높이 {3:F1}mm @{4} · Civil이 0.1mm 안 두 점을 반드시 합치는 자리 곁 · ",
                forcedStep.No, res.HeightBudget * 1000, res.HeightLimit * 1000, forcedStep.R.Clean!.WallDz * 1000, forcedStep.R.Clean.WallAt)
            : "";
        _lastOk = true;
        _compId = cTin.ObjectId; _compGround = groundId;
        return _heldNote + dropNote + overHead + $"<b>정지면_DH에 옹벽 {useOk.Count}개({nos})를 합성했다</b>{(news.Count > 0 ? $" — 새 옹벽 {string.Join(",", news)}" : " — 다시 합성")}{(_rebuiltN > 0 ? $" · 새 정지면에 다시 지은 옹벽 {_rebuiltN}개" : "")}{repNote}"
             + $" · 판정 {res.Tier}({res.TierText}){tierNote} · 정지면 {planAll.Count}면 중 옹벽 둘레 {res.Steps.Sum(s => s.R.Touched)}면을 다시 짓고 나머지는 그대로"
             + $" · 점 {np} · 삼각형 {nf} Civil이 그대로 받음 · 높이 오차 합 {res.HeightBudget * 1000:F2}mm"
             + $" · 합성 전 정지면은 '{BaseName}'로 숨겨 둠{excNote}";
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
        try { if (b0.IsOutOfDate) GradingBuilder.RebuildSurfacesByBaseName(tr, PlanName); } catch { }
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
