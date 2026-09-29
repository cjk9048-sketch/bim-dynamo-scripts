using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Civil.DatabaseServices;
using DH.Grading.Core;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace DH.Grading.Civil.Commands;

/// <summary>★★★[v101.0 · JACK 0928 «커밋하고 원지반 뚜껑만들어서 합성하는걸로»] <b>옹벽 합성(DHWALLCOMP)</b> — 합성지표면_DH.
/// <para>인터뷰로 정한 것: 새 면 <b>합성지표면_DH</b>(정지면_DH는 그대로) · 뚜껑은 합성에만 · 정지면 <b>전체</b>를 덮는다(옹벽 폴리곤 안만 바뀜) ·
/// <b>따로 명령</b>(옹벽변환을 여러 번 고치는 동안에는 빠르게 — 합성은 15~20초) · 합성 면만 보이고 정지면_DH·순수옹벽_DH는 숨김 ·
/// 1mm 안에 못 맞추는 자리는 1cm 안이면 ⚠와 자리를 밝히고 짓는다 · 횡단·InfraWorks·터파기는 이번엔 정지면_DH 그대로.</para>
/// <para>높이 = max(정지면, min(옹벽, 원지반)) — Core <see cref="WallDaylight.BuildComposite"/>. Civil은 LandXML을 받기만 한다
/// (화면 없는 Civil 실측: 현장 다섯 판 × 템플릿 둘 모두 점·면 전부 그대로 · 정지면 크기 25만 면도 그대로 받는다).</para>
/// <para>★[검토 0929 v101 · 높음 1] 새 면은 <b>임시 이름</b>으로 짓고 되읽기 관문을 지난 뒤에만 지난 합성을 지우고 이름을 바꾼다 —
/// 종전엔 지난 합성을 먼저 지우고 관문에서 떨어지면 «보이는 면이 하나도 없는» 도면이 남았다. 못 지으면 지난 합성은 지우지 않고 숨기고
/// 정지면_DH·순수옹벽_DH를 다시 보인다(지난 합성은 이번 재료와 안 맞을 수 있다) — 명령줄에 실제 상태를 적는다.</para></summary>
public sealed class WallCompositeCommand
{
    public const string CompName = "합성지표면_DH";
    public const string CompLayer = "DH-합성지표면";
    /// <summary>관문 전 이름 — <see cref="CompName"/>(또는 _N)과 겹치지 않아 지난 합성 지우기에 안 걸린다.</summary>
    public const string TempName = "DH합성_짓는중";
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
        try
        {
            using (doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                summary = Build(db, tr, log, dir);
                // 실패 길도 커밋한다 — 지난 합성은 관문을 지난 뒤에만 지우므로, 실패 길에 남는 것은 «새 면 지움 + 보이기 되돌림»뿐이다
                tr.Commit();
            }
        }
        catch (System.Exception ex)
        {
            crashed = true;
            summary = $"⚠옹벽 합성 못 함 — {ex.GetType().Name}: {ex.Message} · 이번 명령이 바꾼 것은 되돌렸다(지난 합성·보이기는 명령 전 그대로)";
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

    /// <summary>재료 모으기 → Core 합성 → LandXML(임시 이름) → Civil → 되읽기 관문 → 지난 합성과 바꾸기 → 보이기. 못 하면 까닭(⚠) 문자열.</summary>
    private static string Build(Database db, Transaction tr, System.Text.StringBuilder log, string dir)
    {
        var civilDoc = Autodesk.Civil.ApplicationServices.CivilApplication.ActiveDocument;
        int leftover = EraseTemp(tr);
        if (leftover > 0) log.AppendLine($"   지난번에 남은 임시 면 {leftover}개 지움");

        // ① 원지반 — 옹벽변환이 쓴 것 먼저
        ObjectId groundId = NoriCommand.FindByHandle(db, GradingSettings.LastGroundHandle);
        string gName = "";
        if (groundId.IsNull) groundId = ImportGisCommand.FindGroundSurface(db, out gName, out _);
        if (groundId.IsNull || tr.GetObject(groundId, OpenMode.ForRead) is not TinSurface gTin) return Fail(tr, "원지반 지표면을 못 찾았다");
        gName = gTin.Name;

        // ② 짝 — ★[검토 0929 v101 · 중간 5] 옹벽 변환이 쓴 폴리곤(점 그대로)·가상옹벽·정지면만 쓴다
        System.Collections.Generic.List<Point3> poly;
        TinSurface wTin, pTin;
        string pairNote;
        if (GradingSettings.LastWallInvalid)
            return Fail(tr, "지난 옹벽 변환이 짝을 안 남겼다 — 데이라잇까지 못 갔거나 «폴리곤만» 판이 아니다(합성은 «폴리곤만» 옹벽 변환이 순수옹벽까지 간 뒤에 짓는다 · 옹벽 변환을 다시 돌리세요)");
        if (GradingSettings.LastWallPoly != null)
        {
            if (GradingSettings.LastWallPlanNote.Length > 0)
                return Fail(tr, $"옹벽 변환이 순수옹벽_DH를 안 지은 까닭이 그대로다 — {GradingSettings.LastWallPlanNote}");
            ObjectId wId = NoriCommand.FindByHandle(db, GradingSettings.LastWallSurfHandle), pId = NoriCommand.FindByHandle(db, GradingSettings.LastWallPlanHandle);
            if (wId.IsNull || tr.GetObject(wId, OpenMode.ForRead) is not TinSurface w0)
                return Fail(tr, "옹벽 변환이 지은 가상옹벽_DH가 없다(지워졌거나 다시 지어짐) — 옹벽 변환을 다시 돌리세요");
            if (pId.IsNull || tr.GetObject(pId, OpenMode.ForRead) is not TinSurface p0)
                return Fail(tr, $"옹벽 변환이 쓴 {WallDaylightBuilder.PlanSurfaceBase}가 없다(계획부지 생성을 다시 돌렸나?) — 옹벽 변환을 다시 돌리세요");
            // ★[2차 검토 N5] 핸들은 도면을 닫았다 다시 열면 다른 면을 가리킬 수 있다 — 이름으로 한 번 더 확인
            bool NameOk(string nm, string b) => nm == b || (nm.StartsWith(b + "_") && int.TryParse(nm.Substring(b.Length + 1), out _));
            if (!NameOk(w0.Name, "가상옹벽_DH") || !NameOk(p0.Name, WallDaylightBuilder.PlanSurfaceBase))
                return Fail(tr, $"기억한 짝의 면 이름이 다르다('{w0.Name}' · '{p0.Name}') — 옹벽 변환을 다시 돌리세요");
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
            if (polys.Count == 0) return Fail(tr, "옹벽 폴리곤('DH-가상폴리곤')이 없다 — 옹벽 변환(DHWALL)을 먼저 돌리세요");
            if (polys.Count > 1) return Fail(tr, $"옹벽 폴리곤('DH-가상폴리곤')이 {polys.Count}개 — 어느 것과 합성할지 모른다(옹벽 변환을 다시 돌리면 짝을 기억한다)");
            var (wIds, wNames) = Candidates(civilDoc, tr, "가상옹벽_DH", groundId);
            if (wIds.Count != 1 || tr.GetObject(wIds[0], OpenMode.ForRead) is not TinSurface w1)
                return Fail(tr, wIds.Count == 0 ? "가상옹벽_DH가 없다 — 옹벽 변환(DHWALL)을 먼저 돌리세요" : $"가상옹벽_DH 후보가 {wIds.Count}개({string.Join(", ", wNames)}) — 어느 것과 합성할지 모른다");
            var (pIds, pNames) = Candidates(civilDoc, tr, WallDaylightBuilder.PlanSurfaceBase, groundId);
            if (pIds.Count != 1 || tr.GetObject(pIds[0], OpenMode.ForRead) is not TinSurface p1)
                return Fail(tr, pIds.Count == 0 ? $"{WallDaylightBuilder.PlanSurfaceBase}가 없다 — 계획부지 생성을 먼저 돌리세요" : $"{WallDaylightBuilder.PlanSurfaceBase} 후보가 {pIds.Count}개({string.Join(", ", pNames)}) — 어느 것과 합성할지 모른다");
            poly = polys[0]; wTin = w1; pTin = p1;
            pairNote = "옹벽 변환 기억이 없어 이름으로 찾음(폴리곤·가상옹벽·정지면 하나씩)";
        }
        try { if (pTin.IsOutOfDate) return Fail(tr, $"'{pTin.Name}'가 낡음(Out of date) — 순수옹벽_DH와 같은 관문"); } catch { }
        try { if (wTin.IsOutOfDate) return Fail(tr, $"'{wTin.Name}'가 낡음(Out of date)"); } catch { }
        log.AppendLine($"① 짝 — {pairNote} · 폴리곤 {poly.Count}점 · 원지반 '{gName}' · 옹벽 '{wTin.Name}' · 정지면 '{pTin.Name}'");

        // ③ 삼각형 — 옹벽·원지반은 폴리곤 둘레(±1m), 정지면은 전부(손 안 댄 것은 그대로 옮긴다)
        var sw = System.Diagnostics.Stopwatch.StartNew();
        double mnx = poly.Min(q => q.X) - 1, mny = poly.Min(q => q.Y) - 1, mxx = poly.Max(q => q.X) + 1, mxy = poly.Max(q => q.Y) + 1;
        var wCache = new CachedGroundSurface(wTin); var gCache = new CachedGroundSurface(gTin); var planCache = new CachedGroundSurface(pTin);
        // ★[검토 0929 v101 · 낮음 9] 읽다 버린 삼각형이 있으면 그 면은 빠진 채다 — 되읽기 관문은 우리 목록끼리 견주므로 여기서 막는다
        if (wCache.ReadFailed + gCache.ReadFailed + planCache.ReadFailed > 0)
            return Fail(tr, $"삼각형을 다 못 읽었다(옹벽 {wCache.ReadFailed} · 원지반 {gCache.ReadFailed} · 정지면 {planCache.ReadFailed}개 빠짐)");
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
        if (r.Zone == null) return Fail(tr, "합성지표면을 못 만들었다 — " + r.Fail);

        // ⑤ LandXML → Civil(임시 이름 — 지난 합성은 아직 안 건드린다)
        var all = new System.Collections.Generic.List<WallDaylight.Tri>(r.Zone.Count + r.Untouched.Count);
        all.AddRange(r.Zone); all.AddRange(r.Untouched);
        string xml = System.IO.Path.Combine(dir, XmlName);
        WallDaylight.WriteLandXmlTin(xml, "PUREWALL", all, 1e-7, out int np, out int nf, out int nd);
        if (nd > 0) return Fail(tr, $"합성지표면을 못 만들었다 — 넓이 0이라 뺀 면 {nd}개");
        sw.Restart();
        ObjectId cid = TinSurface.CreateFromLandXML(db, TempName, xml, "PUREWALL");
        if (cid.IsNull) return Fail(tr, "합성지표면을 못 만들었다 — CreateFromLandXML이 빈 ObjectId를 돌려줬다");
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
        catch (System.Exception gx) { Drop(); log.AppendLine(gx.ToString()); return Fail(tr, $"새 합성을 되읽다 터졌다({gx.GetType().Name}) — 새 면은 지웠다"); }
        log.AppendLine("④ 되읽기: " + back);
        if (!gateOk) { Drop(); return Fail(tr, "Civil이 넘긴 합성지표면을 그대로 안 받았다 — 새 면은 지웠다 · " + back); }

        // ⑦ 지난 합성과 바꾼다 — 관문을 지난 뒤에만 지우고 이름을 바꾼다
        GradingBuilder.EraseSurfacesByBaseName(tr, CompName, groundId);
        // ★[2차 검토 N3] 지우기는 실패를 삼킨다(레이어 잠김 등) — 남았으면 이름이 겹치니 새 면을 지우고 못 한 길로
        var (left, leftNames) = Candidates(civilDoc, tr, CompName, groundId);
        if (left.Count > 0) { Drop(); return Fail(tr, $"지난 {string.Join(", ", leftNames)}를 못 지웠다(레이어 잠김?) — 새 면은 지웠다"); }
        string renameErr = "";
        try { cTin.Name = CompName; } catch (System.Exception nx) { renameErr = nx.GetType().Name; log.AppendLine("이름 바꾸기 실패: " + nx); }
        if (renameErr.Length > 0 || cTin.Name != CompName)
        {
            // 지난 합성은 이미 지웠다 — 새 면도 지우면 보이는 면이 없으니 정지면_DH·순수옹벽_DH를 켠다
            Drop();
            int p = 0, w = 0;
            try { p = GradingBuilder.SetSurfaceVisible(tr, WallDaylightBuilder.PlanSurfaceBase, true); } catch { }
            try { w = GradingBuilder.SetSurfaceVisible(tr, WallDaylightBuilder.PureName, true); } catch { }
            return $"⚠합성은 지었는데 이름을 '{CompName}'로 못 바꿔({(renameErr.Length > 0 ? renameErr : $"'{cTin.Name}'")}) 새 면을 지웠다 · 지난 합성도 지워졌다 · 정지면_DH {p}개·순수옹벽_DH {w}개를 보이게 했다";
        }
        try { cTin.Description = $"DH 옹벽 합성 {System.DateTime.Now:yyyy-MM-dd HH:mm} · {GradingSettings.Version} · 판정 {r.Tier}({r.TierText}) · {pairNote}"; } catch { }

        // ⑧ 보이기 — 합성 면이 켜졌을 때만 나머지를 숨긴다(검토 0929 v101 · 낮음 8)
        string vis = "";
        try { vis = GradingBuilder.MakeSurfaceVisible(db, tr, CompName, "DH-합성지표면면", CompLayer, 30); } catch (System.Exception ve) { vis = $"(보이기 실패 {ve.GetType().Name})"; }
        bool shown = false; try { shown = ((Autodesk.AutoCAD.DatabaseServices.Entity)tr.GetObject(cid, OpenMode.ForRead)).Visible; } catch { }
        int hidP = 0, hidW = 0, hidV = 0;
        if (shown)
        {
            try { hidP = GradingBuilder.SetSurfaceVisible(tr, WallDaylightBuilder.PlanSurfaceBase, false); } catch { }
            try { hidW = GradingBuilder.SetSurfaceVisible(tr, WallDaylightBuilder.PureName, false); } catch { }
            try { hidV = GradingBuilder.SetSurfaceVisible(tr, "가상옹벽_DH", false); } catch { }
        }
        log.AppendLine($"⑤ 보이기: {vis} · 합성 켜짐 {shown} · 숨김 정지면 {hidP} · 순수옹벽 {hidW} · 가상옹벽 {hidV}");

        // ★[검토 0929 v101 · 중간 2] «거의»는 까닭과 자리를 — 다듬기 까닭 + 합성 까닭(규칙 대조 · 테두리 · 한 점 한 높이 · 높이 오차 합)
        string why2 = string.Join(" · ", new[] { r.Clean?.Tier2Why ?? "", r.Fail }.Where(x => x.Length > 0));
        string tierNote = r.Tier == 1 ? "" : $" · ⚠<b>거의</b>({why2})";
        return $"<b>{CompName} 만듦</b> — 판정 {r.Tier}({r.TierText}){tierNote} · 정지면 {r.PlanTotal}면 중 옹벽 둘레 {r.Touched}면을 다시 짓고 나머지는 그대로"
             + $" · 점 {np} · 삼각형 {nf} Civil이 그대로 받음 · 높이 오차 합 {r.HeightBudget * 1000:F2}mm" + (shown ? "" : " · ⚠합성 면을 못 켰다(정지면·순수옹벽은 안 숨김)")
             + $" · {pairNote}";
    }

    /// <summary>못 지은 길 — 지난 합성이 있으면 지우지 않고 <b>숨기고</b>(이번 재료와 안 맞을 수 있다) 정지면_DH·순수옹벽_DH를 다시 보인다.
    /// 지난 합성이 없으면 보이기는 안 건드린다. 실제로 한 일을 문구에 붙인다.</summary>
    private static string Fail(Transaction tr, string why)
    {
        string state;
        try
        {
            var civilDoc = Autodesk.Civil.ApplicationServices.CivilApplication.ActiveDocument;
            var (ids, names) = Candidates(civilDoc, tr, CompName, ObjectId.Null);
            if (ids.Count == 0) state = " · 도면의 지표면·보이기는 그대로다";
            else
            {
                string desc = "";
                try { if (tr.GetObject(ids[0], OpenMode.ForRead) is Autodesk.Civil.DatabaseServices.Surface s0 && !string.IsNullOrEmpty(s0.Description)) desc = $"({s0.Description})"; } catch { }
                int h = GradingBuilder.SetSurfaceVisible(tr, CompName, false);
                int p = GradingBuilder.SetSurfaceVisible(tr, WallDaylightBuilder.PlanSurfaceBase, true);
                int w = GradingBuilder.SetSurfaceVisible(tr, WallDaylightBuilder.PureName, true);
                state = $" · 지난 {string.Join(", ", names)}{desc}는 지우지 않고 " + (h == ids.Count ? "숨겼다" : $"{h}/{ids.Count}개만 숨겼다(레이어 잠김?)")
                      + $"(이번 재료와 안 맞을 수 있다) · 정지면_DH {p}개·순수옹벽_DH {w}개를 다시 보이게 했다";
            }
        }
        catch (System.Exception ex) { state = $" · (보이기 되돌리기 실패 {ex.GetType().Name})"; }
        return "⚠" + why + state;
    }

    /// <summary>이름이 baseName 또는 baseName_N인 지표면(원지반 제외) — 가장 큰 번호가 먼저.</summary>
    private static (System.Collections.Generic.List<ObjectId> Ids, System.Collections.Generic.List<string> Names) Candidates(
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

    /// <summary>이름이 정확히 <see cref="TempName"/>인 지표면을 지운다(지난 실행이 터져 남긴 것).</summary>
    private static int EraseTemp(Transaction tr)
    {
        var civilDoc = Autodesk.Civil.ApplicationServices.CivilApplication.ActiveDocument;
        var victims = new System.Collections.Generic.List<ObjectId>();
        foreach (ObjectId sid in civilDoc.GetSurfaceIds())
            if (tr.GetObject(sid, OpenMode.ForRead) is Autodesk.Civil.DatabaseServices.Surface s && s.Name == TempName) victims.Add(sid);
        int n = 0;
        foreach (var sid in victims) { try { tr.GetObject(sid, OpenMode.ForWrite).Erase(); n++; } catch { } }
        return n;
    }
}
