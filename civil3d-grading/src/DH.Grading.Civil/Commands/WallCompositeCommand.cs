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
    static void MarkComposite(Transaction tr, Autodesk.AutoCAD.DatabaseServices.DBObject o, string info, string baseHandle)
    {
        if (!o.IsWriteEnabled) o.UpgradeOpen();
        if (o.ExtensionDictionary.IsNull) o.CreateExtensionDictionary();
        var d = (DBDictionary)tr.GetObject(o.ExtensionDictionary, OpenMode.ForWrite);
        if (d.Contains(TagKey)) throw new System.InvalidOperationException("새로 지은 면에 합성 표지가 벌써 있다");
        var xr = new Xrecord { Data = new ResultBuffer(new TypedValue((int)DxfCode.Text, info), new TypedValue((int)DxfCode.Text, baseHandle)) };
        d.SetAt(TagKey, xr);
        tr.AddNewlyCreatedDBObject(xr, true);
    }
    /// <summary>표지에 적힌 합성 전 면 핸들 — 없으면 빈 문자열(짝 확인에서 «어긋남»이 된다).</summary>
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
    /// <summary>합성 전 면 없이 남은 합성에서 빠져나가는 길 — 정지면을 통째로 다시 짓는 것만 늘 된다
    /// (옹벽 구간이 남아 있으면 옹벽·사면 변환은 폴리곤만 만들고 정지면을 다시 짓지 않는다 — CreateGradingCommand «폴리곤만»).</summary>
    internal const string B1Help = "계획부지 생성 «새로 시작»으로 정지면을 다시 지은 뒤 옹벽 변환을 다시 하세요";
    /// <summary>짝이 어긋난 합성 전 면을 치우는 길.</summary>
    internal const string PairHelp = "합성 전 면을 치우려면 계획부지 생성 «새로 시작»";
    /// <summary>바꿔 끼우기를 시작한 뒤의 실패 — 던져서 트랜잭션째 되돌린다(코드 검토 v101.1 · 낮음 3: 돌려주고 커밋하면 반쯤 바꾼 것이 굳는다).</summary>
    sealed class SwapFailed : System.Exception { public SwapFailed(string m) : base(m) { } }
    /// <summary>이번 실행이 Civil에 만든 새 면 — 되돌린 뒤에도 남았으면(가져오기가 트랜잭션 밖에서 만들었을 때) 이름과 상관없이 지운다.</summary>
    static ObjectId _made = ObjectId.Null;
    const string LogName = "DHWALLCOMP_진단.log";
    const string DumpName = "DHWALLCOMP_입력.txt";
    const string XmlName = "DHWALLCOMP_합성.xml";

    [CommandMethod("DHWALLCOMP")]
    public void Run()
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
        _made = ObjectId.Null;
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
            crashed = true;
            summary = $"⚠옹벽 합성 못 함 — {sf.Message} · 바꿔 끼우기를 통째로 되돌렸다(정지면_DH·보이기는 명령 전 그대로)";
            log.AppendLine(sf.ToString());
        }
        catch (System.Exception ex)
        {
            crashed = true;
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
                    tr2.Commit();
                    if (n > 0) summary += $" · 남은 임시 면 {n}개 지움";
                }
            }
            catch (System.Exception ex2) { log.AppendLine("임시 면 치우기 실패: " + ex2); }
        }
        summary += $" · 전체 {sw.ElapsedMilliseconds / 1000.0:F1}초";
        log.AppendLine("■ " + summary);
        try { System.IO.File.WriteAllText(logPath, log.ToString()); WallDaylightBuilder.ArchiveDumpPublic(logPath); } catch { }
        try { ed.WriteMessage($"\n[옹벽 합성] {summary.Replace("<b>", "").Replace("</b>", "")}\n  로그: {logPath}\n"); } catch { }
        try { ed.Regen(); } catch { }
    }

    /// <summary>재료 모으기 → Core 합성 → LandXML(임시 이름) → Civil → 되읽기 관문 → 정지면_DH로 바꿔 끼우기 → 보이기. 못 하면 까닭(⚠) 문자열.</summary>
    private static string Build(Database db, Transaction tr, System.Text.StringBuilder log, string dir)
    {
        var civilDoc = Autodesk.Civil.ApplicationServices.CivilApplication.ActiveDocument;
        int leftover = EraseTemp(tr);
        if (leftover > 0) log.AppendLine($"   지난번에 남은 임시 면 {leftover}개 지움");

        // ① 원지반 — 옹벽변환이 쓴 것 먼저
        ObjectId groundId = NoriCommand.FindByHandle(db, GradingSettings.LastGroundHandle);
        string gName = "";
        if (groundId.IsNull) groundId = ImportGisCommand.FindGroundSurface(db, out gName, out _);
        if (groundId.IsNull || tr.GetObject(groundId, OpenMode.ForRead) is not TinSurface gTin) return Fail("원지반 지표면을 못 찾았다");
        gName = gTin.Name;

        // ② 짝 — ★[검토 0929 v101 · 중간 5] 옹벽 변환이 쓴 폴리곤(점 그대로)·가상옹벽·정지면만 쓴다.
        //   정지면 = 합성 <b>전</b> 면 — 합성한 뒤면 이름이 정지면_DH합성전이다(짝 핸들은 같은 객체를 가리킨다)
        System.Collections.Generic.List<Point3> poly;
        TinSurface wTin, pTin;
        string pairNote;
        bool NameOk(string nm, string b) => nm == b || (nm.StartsWith(b + "_") && int.TryParse(nm.Substring(b.Length + 1), out _));
        if (GradingSettings.LastWallInvalid)
            return Fail("지난 옹벽 변환이 짝을 안 남겼다 — 데이라잇까지 못 갔거나 «폴리곤만» 판이 아니다(합성은 «폴리곤만» 옹벽 변환이 순수옹벽까지 간 뒤에 짓는다 · 옹벽 변환을 다시 돌리세요)");
        if (GradingSettings.LastWallPoly != null)
        {
            if (GradingSettings.LastWallPlanNote.Length > 0)
                return Fail($"옹벽 변환이 순수옹벽_DH를 안 지은 까닭이 그대로다 — {GradingSettings.LastWallPlanNote}");
            ObjectId wId = NoriCommand.FindByHandle(db, GradingSettings.LastWallSurfHandle), pId = NoriCommand.FindByHandle(db, GradingSettings.LastWallPlanHandle);
            if (wId.IsNull || tr.GetObject(wId, OpenMode.ForRead) is not TinSurface w0)
                return Fail("옹벽 변환이 지은 가상옹벽_DH가 없다(지워졌거나 다시 지어짐) — 옹벽 변환을 다시 돌리세요");
            if (pId.IsNull || tr.GetObject(pId, OpenMode.ForRead) is not TinSurface p0)
                return Fail($"옹벽 변환이 쓴 {PlanName}가 없다(계획부지 생성을 다시 돌렸나?) — 옹벽 변환을 다시 돌리세요");
            // ★[2차 검토 N5] 핸들은 도면을 닫았다 다시 열면 다른 면을 가리킬 수 있다 — 이름으로 한 번 더 확인
            if (!NameOk(w0.Name, "가상옹벽_DH") || !(NameOk(p0.Name, PlanName) || p0.Name == BaseName))
                return Fail($"기억한 짝의 면 이름이 다르다('{w0.Name}' · '{p0.Name}') — 옹벽 변환을 다시 돌리세요");
            poly = new System.Collections.Generic.List<Point3>(GradingSettings.LastWallPoly);
            wTin = w0; pTin = p0;
            pairNote = $"옹벽 변환({GradingSettings.LastWallStamp})이 쓴 짝";
        }
        else
        {
            // 기억이 없다(도면 전환·Civil 재시작) — 이름으로 찾되 후보가 하나씩일 때만
            var polys = new System.Collections.Generic.List<System.Collections.Generic.List<Point3>>();
            var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
            foreach (ObjectId id in ms)
            {
                if (tr.GetObject(id, OpenMode.ForRead) is not Polyline3d pl || !string.Equals(pl.Layer, "DH-가상폴리곤", System.StringComparison.OrdinalIgnoreCase)) continue;
                var pts = new System.Collections.Generic.List<Point3>();
                foreach (ObjectId vid in pl)
                    if (tr.GetObject(vid, OpenMode.ForRead) is PolylineVertex3d v) pts.Add(new Point3(v.Position.X, v.Position.Y, v.Position.Z));
                if (pts.Count > 1 && pts[0].X == pts[^1].X && pts[0].Y == pts[^1].Y) pts.RemoveAt(pts.Count - 1);
                if (pts.Count >= 3) polys.Add(pts);
            }
            if (polys.Count == 0) return Fail("옹벽 폴리곤('DH-가상폴리곤')이 없다 — 옹벽 변환(DHWALL)을 먼저 돌리세요");
            if (polys.Count > 1) return Fail($"옹벽 폴리곤('DH-가상폴리곤')이 {polys.Count}개 — 어느 것과 합성할지 모른다(옹벽 변환을 다시 돌리면 짝을 기억한다)");
            var (wIds, wNames) = Candidates(civilDoc, tr, "가상옹벽_DH", groundId);
            if (wIds.Count != 1 || tr.GetObject(wIds[0], OpenMode.ForRead) is not TinSurface w1)
                return Fail(wIds.Count == 0 ? "가상옹벽_DH가 없다 — 옹벽 변환(DHWALL)을 먼저 돌리세요" : $"가상옹벽_DH 후보가 {wIds.Count}개({string.Join(", ", wNames)}) — 어느 것과 합성할지 모른다");
            // 정지면 — 합성 전 면이 있으면 그것(지난 합성이 정지면_DH에 들어가 있다), 없으면 정지면_DH
            var (bIds, bNames) = Candidates(civilDoc, tr, BaseName, groundId);
            TinSurface? p1 = null;
            if (bIds.Count > 1) return Fail($"합성 전 정지면이 {bIds.Count}개({string.Join(", ", bNames)}) — 어느 것에 합성할지 모른다");
            if (bIds.Count == 1) p1 = tr.GetObject(bIds[0], OpenMode.ForRead) as TinSurface;
            else
            {
                var (pIds, pNames) = Candidates(civilDoc, tr, PlanName, groundId);
                if (pIds.Count != 1)
                    return Fail(pIds.Count == 0 ? $"{PlanName}가 없다 — 계획부지 생성을 먼저 돌리세요" : $"{PlanName} 후보가 {pIds.Count}개({string.Join(", ", pNames)}) — 어느 것과 합성할지 모른다");
                p1 = tr.GetObject(pIds[0], OpenMode.ForRead) as TinSurface;
            }
            if (p1 == null) return Fail("정지면이 TIN 지표면이 아니다");
            poly = polys[0]; wTin = w1; pTin = p1;
            pairNote = "옹벽 변환 기억이 없어 이름으로 찾음(폴리곤·가상옹벽·정지면 하나씩)";
        }
        bool inS1 = pTin.Name == BaseName;                 // 합성이 이미 정지면_DH에 들어가 있다(다시 합성)
        // ★[계획 검토 v101.1 · 높음 1] 표지 붙은 면(합성)을 입력으로 받으면 합성 위에 또 합성한다
        if (IsComposite(tr, pTin))
            return Fail($"'{pTin.Name}'는 이미 옹벽 합성인데 합성 전 면이 없다 — 합성 위에 또 합성하지 않는다({B1Help})");
        // ★[계획 검토 v101.1 · 중간 2] 합성 전 면이 있으면 짝의 정지면이 곧 그것이어야 한다 — 아니면 짝이 어긋난 것
        if (!inS1)
        {
            var (bx, bxNames) = Candidates(civilDoc, tr, BaseName, groundId);
            if (bx.Count > 0)
                return Fail($"합성 전 면({string.Join(", ", bxNames)})이 남아 있는데 짝의 정지면은 '{pTin.Name}'다(짝이 어긋남) — {PairHelp}");
        }
        // ★[코드 검토 v101.1 · 중간 1] 다시 합성이면 지금 정지면_DH는 전부 이 합성 전 면의 합성(표지 + 핸들)이어야 한다 —
        //   표지 없는 정지면_DH는 새 계획면이다. 바꿔 끼우기가 그것을 지우면 새 계획면을 말없이 잃는다
        if (inS1)
        {
            string ph = pTin.ObjectId.Handle.ToString();
            var (cur, curNames) = Candidates(civilDoc, tr, PlanName, groundId);
            for (int i = 0; i < cur.Count; i++)
            {
                var o = tr.GetObject(cur[i], OpenMode.ForRead);
                if (!IsComposite(tr, o) || CompositeBaseHandle(tr, o) != ph)
                    return Fail($"'{curNames[i]}'가 합성 전 면('{pTin.Name}')의 합성이 아니다(짝이 어긋남) — {PairHelp}");
            }
        }
        // 붙여넣기 합성면은 원지반을 숨기거나 켤 때마다 ⚠(정의 구식)가 붙는다(보기·결과만 보기) — 한 번 다시 짓고 본다
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
        try { if (wTin.IsOutOfDate) return Fail($"'{wTin.Name}'가 낡음(Out of date)"); } catch { }
        log.AppendLine($"① 짝 — {pairNote} · 폴리곤 {poly.Count}점 · 원지반 '{gName}' · 옹벽 '{wTin.Name}' · 정지면 '{pTin.Name}'{(inS1 ? "(지난 합성의 합성 전 면 — 다시 합성)" : "")}");

        // ③ 삼각형 — 옹벽·원지반은 폴리곤 둘레(±1m), 정지면은 전부(손 안 댄 것은 그대로 옮긴다)
        var sw = System.Diagnostics.Stopwatch.StartNew();
        double mnx = poly.Min(q => q.X) - 1, mny = poly.Min(q => q.Y) - 1, mxx = poly.Max(q => q.X) + 1, mxy = poly.Max(q => q.Y) + 1;
        var wCache = new CachedGroundSurface(wTin); var gCache = new CachedGroundSurface(gTin); var planCache = new CachedGroundSurface(pTin);
        // ★[검토 0929 v101 · 낮음 9] 읽다 버린 삼각형이 있으면 그 면은 빠진 채다 — 되읽기 관문은 우리 목록끼리 견주므로 여기서 막는다
        if (wCache.ReadFailed + gCache.ReadFailed + planCache.ReadFailed > 0)
            return Fail($"삼각형을 다 못 읽었다(옹벽 {wCache.ReadFailed} · 원지반 {gCache.ReadFailed} · 정지면 {planCache.ReadFailed}개 빠짐)");
        var wallTris = wCache.TrianglesIn(mnx, mny, mxx, mxy);
        var groundTris = gCache.TrianglesIn(mnx, mny, mxx, mxy);
        var planAll = planCache.AllTriangles();
        log.AppendLine($"② 삼각형 — 옹벽 {wallTris.Count} · 원지반 {groundTris.Count} · 정지면 전체 {planAll.Count} · {sw.ElapsedMilliseconds}ms");
        // 입력을 떨군다 — 오프라인 재생(먼저 폴리곤 ±5m, 합성 뒤 손댄 상자로 다시)
        string dump = System.IO.Path.Combine(dir, DumpName);
        string head = $"{System.DateTime.Now:yyyy-MM-dd HH:mm:ss} {GradingSettings.Version} 원지반='{gName}' 옹벽='{wTin.Name}' 정지면='{pTin.Name}' · {pairNote}";
        void Dump(System.Collections.Generic.List<WallDaylight.Tri> planPart, string what)
        {
            try
            {
                WallDaylight.WriteBandInput(dump, poly, wallTris, groundTris, planPart, head + $"(전체 {planAll.Count}면 중 {what} {planPart.Count})");
                log.AppendLine($"   입력 파일({what}): {dump}");
            }
            catch (System.Exception de) { log.AppendLine($"   (입력 못 떨굼: {de.GetType().Name})"); }
        }
        Dump(planCache.TrianglesIn(mnx - 4, mny - 4, mxx + 4, mxy + 4), "폴리곤 ±5m");

        // ④ Core 합성
        var r = WallDaylight.BuildComposite(poly, wallTris, groundTris, planAll);
        log.AppendLine("③ 합성: " + r.Summary);
        if (r.TouchMaxX >= r.TouchMinX) Dump(planCache.TrianglesIn(r.TouchMinX, r.TouchMinY, r.TouchMaxX, r.TouchMaxY), "손댄 상자에 걸친");
        try { WallDaylightBuilder.ArchiveDumpPublic(dump); } catch { }
        if (r.Zone == null) return Fail("합성을 못 만들었다 — " + r.Fail);

        // ⑤ LandXML → Civil(임시 이름 — 정지면_DH는 아직 안 건드린다)
        var all = new System.Collections.Generic.List<WallDaylight.Tri>(r.Zone.Count + r.Untouched.Count);
        all.AddRange(r.Zone); all.AddRange(r.Untouched);
        string xml = System.IO.Path.Combine(dir, XmlName);
        WallDaylight.WriteLandXmlTin(xml, "PUREWALL", all, 1e-7, out int np, out int nf, out int nd);
        if (nd > 0) return Fail($"합성을 못 만들었다 — 넓이 0이라 뺀 면 {nd}개");
        sw.Restart();
        ObjectId cid = TinSurface.CreateFromLandXML(db, TempName, xml, "PUREWALL");
        if (cid.IsNull) return Fail("합성을 못 만들었다 — CreateFromLandXML이 빈 ObjectId를 돌려줬다");
        _made = cid;
        var cTin = (TinSurface)tr.GetObject(cid, OpenMode.ForWrite);
        long tCreate = sw.ElapsedMilliseconds;
        void Drop() { try { cTin.Erase(); } catch { } }

        // ⑥ 되읽기 관문 — 점 수 · 삼각형 수 · 넘긴 면과 같은가(다른 면만 높이 · 모양) · 높이 오차 합이 판정 한도 안
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
            double zTol = r.Tier == 1 ? 1e-3 : 1e-2;
            gateOk = theirs.Count == nf && civPts == np && cmp.Exceptions == 0 && cmp.SymArea <= 1e-8 && cmp.Hausdorff <= 1e-6 && r.HeightBudget + cmp.MaxDz <= zTol;
            back = $"Civil 점 {civPts}/{np} · 삼각형 {theirs.Count}/{nf} · {cmp.Summary} · 높이 오차 합 {r.HeightBudget * 1000:F3} + Civil {cmp.MaxDz * 1000:F3}mm(한도 {zTol * 1000:F0}mm) · 가져오기 {tCreate}ms · 대조 {sw.ElapsedMilliseconds}ms";
        }
        catch (System.Exception gx) { Drop(); log.AppendLine(gx.ToString()); return Fail($"새 합성을 되읽다 터졌다({gx.GetType().Name}) — 새 면은 지웠다"); }
        log.AppendLine("④ 되읽기: " + back);
        if (!gateOk) { Drop(); return Fail("Civil이 넘긴 합성을 그대로 안 받았다 — 새 면은 지웠다 · " + back); }

        // ⑦ 정지면_DH로 바꿔 끼운다 — 관문을 지난 뒤에만. 한 단계라도 못 하면 SwapFailed — 트랜잭션째 되돌려 명령 전 상태로
        var pW = (Autodesk.Civil.DatabaseServices.Surface)tr.GetObject(pTin.ObjectId, OpenMode.ForWrite);
        if (inS1)
        {
            // 지금 정지면_DH = 지난 합성 — 지운다(원지반은 보호 · 합성 전 면은 이름이 달라 안 걸린다)
            GradingBuilder.EraseSurfacesByBaseName(tr, PlanName, groundId);
            var (left, leftNames) = Candidates(civilDoc, tr, PlanName, groundId);
            if (left.Count > 0) throw new SwapFailed($"지난 합성({string.Join(", ", leftNames)})을 못 지웠다(레이어 잠김?)");
        }
        else
        {
            // 남은 낡은 합성 전 면(있으면) 지우기 → 입력 정지면 이름을 합성 전으로
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
        // 정지면_DH가 늘 보이던 모양 그대로 — 합성 전 면의 스타일·레이어를 물려받는다
        try { cTin.StyleId = pW.StyleId; } catch (System.Exception sx) { log.AppendLine("스타일 물려받기 실패: " + sx.GetType().Name); }
        try { ((Autodesk.AutoCAD.DatabaseServices.Entity)cTin).LayerId = ((Autodesk.AutoCAD.DatabaseServices.Entity)pW).LayerId; } catch (System.Exception lx) { log.AppendLine("레이어 물려받기 실패: " + lx.GetType().Name); }
        try { cTin.Description = $"DH 옹벽 합성 {System.DateTime.Now:yyyy-MM-dd HH:mm} · {GradingSettings.Version} · 판정 {r.Tier}({r.TierText}) · {pairNote}"; } catch { }
        // ★[코드 검토 v101.1 · 낮음 6] 표지가 없으면 «합성 위에 또 합성» 막이와 풀기 짝 확인이 없다 — 못 붙이면 되돌린다
        try { MarkComposite(tr, cTin, $"{System.DateTime.Now:yyyy-MM-dd HH:mm:ss} · {GradingSettings.Version} · 판정 {r.Tier}", pTin.ObjectId.Handle.ToString()); }
        catch (System.Exception mx) { log.AppendLine("합성 표지 못 붙임: " + mx); throw new SwapFailed($"합성 표지를 못 붙였다({mx.GetType().Name})"); }
        // v101.0이 따로 만든 합성지표면_DH는 치운다
        int oldN = 0;
        try { var (o, _) = Candidates(civilDoc, tr, OldCompName, groundId); oldN = o.Count; if (oldN > 0) GradingBuilder.EraseSurfacesByBaseName(tr, OldCompName, groundId); } catch { }

        // ⑧ 보이기 — 계획부지 생성이 끝날 때와 같게(JACK «옹벽 전체수정할때처럼»)
        string vis;
        if (GradingSettings.ShowOnlyResultSurface)
        {
            GradingBuilder.IsolateSurfaces(tr, PlanName);
            // 원지반을 숨기면 그것을 붙여 넣은 합성 전 면에 ⚠(정의 구식)가 붙는다 — 계획부지 생성이 정지면_DH에 하듯 다시 지어 푼다
            string rb = ""; try { rb = GradingBuilder.RebuildSurfacesByBaseName(tr, BaseName); } catch (System.Exception rx) { rb = "실패 " + rx.GetType().Name; }
            vis = $"결과만 보기 — 정지면_DH만 켬 · 합성 전 면 다시 지음({rb})";
        }
        else
        {
            int a = 0, b = 0, c = 0, d = 0;
            try { a = GradingBuilder.SetSurfaceVisible(tr, PlanName, true); } catch { }
            try { b = GradingBuilder.SetSurfaceVisible(tr, BaseName, false); } catch { }
            try { c = GradingBuilder.SetSurfaceVisible(tr, WallDaylightBuilder.PureName, false); } catch { }
            try { d = GradingBuilder.SetSurfaceVisible(tr, "가상옹벽_DH", false); } catch { }
            vis = $"정지면_DH 켬 {a} · 숨김 합성 전 {b} · 순수옹벽 {c} · 가상옹벽 {d}";
        }
        try { GradingBuilder.SetSurfaceVisible(tr, SectionCommand.PurePadSurfaceBase, false); } catch { }   // 순수 정지면은 늘 숨김(계획부지 생성과 같다)
        log.AppendLine($"⑤ 바꿔 끼움: {(inS1 ? "지난 합성을 지우고 새 합성을" : $"정지면_DH를 '{BaseName}'로 숨기고 합성을")} 정지면_DH로 · 옛 판 합성지표면_DH {oldN}개 치움 · 보이기: {vis}");

        // ★[계획 검토 v101.1 · 낮음 4] 터파기 보기 합성면은 정지면_DH를 붙여 넣은 것 — 옛 정지면을 품고 있다
        string excNote = "";
        try
        {
            var (xa, _) = Candidates(civilDoc, tr, ViewSurfaceCommand.AllName, groundId);
            var (xb, _) = Candidates(civilDoc, tr, ViewSurfaceCommand.ExcavAllName, groundId);
            if (xa.Count + xb.Count > 0) excNote = " · 터파기 보기 합성면(전체면_DH·터파기전체_DH)은 옛 정지면을 붙여 넣은 것이라 터파기를 다시 하면 새 정지면으로 바뀐다";
        }
        catch { }
        // ★[검토 0929 v101 · 중간 2] «거의»는 까닭과 자리를 — 다듬기 까닭 + 합성 까닭(규칙 대조 · 테두리 · 한 점 한 높이 · 높이 오차 합)
        string why2 = string.Join(" · ", new[] { r.Clean?.Tier2Why ?? "", r.Fail }.Where(x => x.Length > 0));
        string tierNote = r.Tier == 1 ? "" : $" · ⚠<b>거의</b>({why2})";
        return $"<b>정지면_DH에 옹벽을 합성했다</b> — 판정 {r.Tier}({r.TierText}){tierNote} · 정지면 {r.PlanTotal}면 중 옹벽 둘레 {r.Touched}면을 다시 짓고 나머지는 그대로"
             + $" · 점 {np} · 삼각형 {nf} Civil이 그대로 받음 · 높이 오차 합 {r.HeightBudget * 1000:F2}mm"
             + $" · 합성 전 정지면은 '{BaseName}'로 숨겨 둠(옹벽 변환을 다시 돌리면 되돌린다){excNote} · {pairNote}";
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
        // 숨겨 둔 사이 붙은 ⚠(정의 구식) 풀기 — 구식일 때만(25만 면 재작성 + 스냅샷은 수 초 · 곧 새로 지을 면이면 헛일 — 계획 검토 v101.1 · 낮음 5)
        try { if (b0.IsOutOfDate) GradingBuilder.RebuildSurfacesByBaseName(tr, PlanName); } catch { }
        return Done(true, "지난 옹벽 합성을 풀었다 — 정지면_DH를 합성 전으로 되돌렸다(«옹벽 합성»을 다시 누르세요)");
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
