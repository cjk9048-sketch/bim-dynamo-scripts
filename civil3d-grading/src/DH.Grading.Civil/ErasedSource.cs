using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using CivilDb = Autodesk.Civil.DatabaseServices;

namespace DH.Grading.Civil;

/// <summary>★★[1116 밤 · 지운 면을 원천으로 문 종단 · 횡단] 부지(정지면_DH · 정지순수_DH)를 다시 짓거나 진입도로 면을 다시 지으면 옛 면은 지워지는데,
/// 그 면에서 딴 «-바탕» 종단 · 표본선 묶음(«-검토» · «횡단»)은 그대로 남아 <b>지워진 면을 원천으로 문다</b>(Civil이 읽거나 다시 만들다 죽는 길 — 1013 · 정지면 바꾼 뒤 UNDO).
/// <para>Civil API로는 원천을 바꿔 끼울 수 없다(단면 원천 읽기 전용 · 표면 종단 DataSourceId 읽기 전용) → 다시 그리거나(<see cref="RoadSession"/>이 «바탕면이 바뀌었습니다»로 알림 → [업데이트]가 같은 자리에 다시)
/// 다시 못 그리면 <b>지운다</b>(<see cref="Sweep"/>) — 어느 쪽이든 지워진 원천을 문 채 끝나지 않는다.</para>
/// 원천은 묶음 전체(<c>GetSectionSources</c> — 지워진 원천에서 죽음)가 아니라 <b>단면마다</b> 읽는다(<see cref="SurfaceRetire.HumanKeep"/>과 같은 읽기). 우리 노선(DH선형 · DH도로)만 — 사람 노선은 안 건드린다.</summary>
internal static class ErasedSource
{
    /// <summary>우리 면(정지면 · 도로 면)이 이번 명령에서 지워졌나 — 켜졌을 때만 <see cref="Sweep"/>이 훑는다(<c>GradingBuilder.EraseSurfacesByBaseName</c> · 도로 면 다시 짓기가 켠다). 전체 정리가 끝나면 끈다.</summary>
    internal static bool Pending;
    /// <summary>부지 갱신 한 번(<c>SiteSheetsRefresh.Run</c>) 안 — 도로 하나의 정리가 <see cref="Pending"/>을 끄지 않는다.</summary>
    internal static bool InBatch;
    /// <summary>★[1116 밤] 알림만 하고 손대지 않기로 한 노선 — <see cref="Sweep"/>이 건너뛴다(사람이 그린 것을 말없이 지우지 않음). <see cref="Protect"/>가 채우고 부지 갱신 끝에서 비운다.</summary>
    internal static readonly HashSet<ObjectId> NoSweep = new();

    /// <summary>알림만 하는 노선(<paramref name="only"/> 없음 = 부지 노선 모두)이 지운 면을 물고 있으면 <see cref="NoSweep"/>에 넣고 이름들을 돌려준다(없으면 빈 목록).</summary>
    internal static List<string> Protect(Document doc, ObjectId only = default)
    {
        var names = new List<string>();
        try
        {
            var db = doc.Database;
            using var tr = db.TransactionManager.StartTransaction();
            var civ = Autodesk.Civil.ApplicationServices.CivilDocument.GetCivilDocument(db);
            foreach (ObjectId aid in civ.GetAlignmentIds())
            {
                try
                {
                    if (aid.IsNull || aid.IsErased || (!only.IsNull && aid != only) || tr.GetObject(aid, OpenMode.ForRead) is not CivilDb.Alignment al) continue;
                    string an = al.Name ?? "";
                    if (only.IsNull && !an.StartsWith(Commands.SectionCommand.AlignBase, StringComparison.Ordinal)) continue;
                    if (!al.GetProfileIds().Cast<ObjectId>().Any(p => ProfileDead(tr, p)) && !al.GetSampleLineGroupIds().Cast<ObjectId>().Any(g => GroupDead(tr, g))) continue;
                    NoSweep.Add(aid); names.Add(an);
                }
                catch { }
            }
            tr.Commit();
        }
        catch { }
        return names;
    }

    /// <summary>표면 종단(EG)의 원천 면이 지워졌나(읽기만).</summary>
    internal static bool ProfileDead(Transaction tr, ObjectId pid)
    {
        try
        {
            if (pid.IsNull || pid.IsErased || tr.GetObject(pid, OpenMode.ForRead) is not CivilDb.Profile pf || pf.ProfileType != CivilDb.ProfileType.EG) return false;
            return pf.GetType().GetProperty("DataSourceId")?.GetValue(pf) is ObjectId src && !src.IsNull && src.IsErased;
        }
        catch { return false; }
    }

    /// <summary>표본선 묶음의 단면 중 하나라도 원천 면이 지워졌나(단면마다 읽기만).</summary>
    internal static bool GroupDead(Transaction tr, ObjectId gid)
    {
        try
        {
            if (gid.IsNull || gid.IsErased || tr.GetObject(gid, OpenMode.ForRead) is not CivilDb.SampleLineGroup g) return false;
            foreach (ObjectId slid in g.GetSampleLineIds())   // ★[1116 밤] 원천은 묶음마다 같다 — 단면이 있는 첫 표본선만 읽는다
            {
                if (slid.IsNull || slid.IsErased || tr.GetObject(slid, OpenMode.ForRead) is not CivilDb.SampleLine sl) continue;
                var secs = sl.GetSectionIds();
                if (secs.Count == 0) continue;
                foreach (ObjectId secId in secs)
                    if (!secId.IsNull && !secId.IsErased && tr.GetObject(secId, OpenMode.ForRead) is CivilDb.Section sec && sec.SourceId.IsErased) return true;
                return false;
            }
        }
        catch { }
        return false;
    }

    static bool Find(Database db, string h, out ObjectId id)
    {
        id = ObjectId.Null;
        try { return h.Length > 0 && db.TryGetObjectId(new Handle(System.Convert.ToInt64(h, 16)), out id) && !id.IsErased; } catch { return false; }
    }

    /// <summary>이 진입도로의 «-바탕» 종단이 지워진 면을 물고 있나(알림용 · 읽기만).</summary>
    internal static bool RoadSheetDead(Transaction tr, Database db, RoadDesign d)
        => (Find(db, d.BaseProfile, out var bp) && ProfileDead(tr, bp)) || (Find(db, d.SheetGroup, out var sg) && GroupDead(tr, sg));

    /// <summary>이 진입도로의 횡단 묶음이 지워진 면을 물고 있나(알림용 · 읽기만).</summary>
    internal static bool RoadXsecDead(Transaction tr, Database db, RoadDesign d)
        => Find(db, d.XsecGroup, out var xg) && GroupDead(tr, xg);

    /// <summary>[업데이트]가 종단도를 다시 그리기 <b>앞</b> — 지워진 면을 문 «-바탕»(과 그 종단뷰)을 먼저 지운다. 종단도 다시 그리기가 «-바탕»을 읽기 전에(지워진 원천을 읽다 죽지 않게)
    /// 새 면으로 다시 만들게 한다(<see cref="RoadSheets"/>가 지워졌으면 지금 바탕면에서 새로). 지웠으면 true.</summary>
    internal static bool EraseDeadBase(Database db, ObjectId aid, RoadSession s, System.Text.StringBuilder log)
    {
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            var d = RoadDesign.Read(tr, aid);
            if (d == null || !Find(db, d.BaseProfile, out var bp) || !ProfileDead(tr, bp)) { tr.Commit(); return false; }
            var al = (CivilDb.Alignment)tr.GetObject(aid, OpenMode.ForRead);
            int nv = EraseRoadViews(tr, al, d);
            tr.GetObject(bp, OpenMode.ForWrite, false, true).Erase();
            tr.Commit();
            s.BaseProfId = ObjectId.Null;
            log.AppendLine($"  지워진 면을 문 «-바탕» 종단 지움(종단뷰 {nv}) — 새 면으로 다시 만듦");
            return true;
        }
        catch (System.Exception ex) { log.AppendLine("  ⚠지워진 면을 문 «-바탕»을 못 지움 — " + ex.Message); return false; }
    }

    static int EraseRoadViews(Transaction tr, CivilDb.Alignment al, RoadDesign d)
    {
        int n = 0;
        var hv = new HashSet<string>(d.SheetViews, StringComparer.OrdinalIgnoreCase);
        foreach (ObjectId vid in al.GetProfileViewIds())
        {
            try
            {
                if (vid.IsErased) continue;
                var v = (Entity)tr.GetObject(vid, OpenMode.ForRead);
                if (!hv.Contains(vid.Handle.ToString()) && !string.Equals(v.Layer, RoadBuilder.LayView, StringComparison.OrdinalIgnoreCase)
                    && !(v is CivilDb.ProfileView pv && RoadSheets.IsSheetTwin(pv))) continue;   // ★[v117.37.1] 배치탭 쌍둥이도 함께
                tr.GetObject(vid, OpenMode.ForWrite, false, true).Erase(); n++;
            }
            catch { }
        }
        try { n += RoadSheets.EraseTwinNotes(al.Database, tr, al.ObjectId.Handle.ToString()); } catch { }
        return n;
    }

    static int EraseGroup(Transaction tr, ObjectId gid)
    {
        int n = 0;
        try
        {
            var g = (CivilDb.SampleLineGroup)tr.GetObject(gid, OpenMode.ForRead);
            foreach (ObjectId slid in g.GetSampleLineIds())   // 단면뷰(횡단)를 먼저 — 지운 묶음에 매달린 뷰가 남지 않게
            {
                try
                {
                    if (slid.IsErased || tr.GetObject(slid, OpenMode.ForRead) is not CivilDb.SampleLine sl) continue;
                    foreach (ObjectId svid in sl.GetSectionViewIds()) if (!svid.IsErased) { tr.GetObject(svid, OpenMode.ForWrite, false, true).Erase(); n++; }
                }
                catch { }
            }
            tr.GetObject(gid, OpenMode.ForWrite, false, true).Erase(); n++;
        }
        catch { }
        return n;
    }

    /// <summary>한 번 치운 결과(명령창 · 로그).</summary>
    internal sealed class Result
    {
        public int SiteProf, SiteXsec, RoadProf, RoadXsec;
        public readonly List<string> Names = new();
        public bool Any => SiteProf + SiteXsec + RoadProf + RoadXsec > 0;
        public string Text => string.Join(" · ", new[]
        {
            SiteProf > 0 ? $"부지 종단 {SiteProf}" : "", SiteXsec > 0 ? $"부지 횡단 묶음 {SiteXsec}" : "",
            RoadProf > 0 ? $"진입도로 종단도 {RoadProf}" : "", RoadXsec > 0 ? $"진입도로 횡단도 {RoadXsec}" : "",
        }.Where(t => t.Length > 0));
    }

    /// <summary>명령 끝에서 — 지워진 면을 원천으로 문 우리 종단 · 표본선 묶음(과 그 뷰)을 지운다. 다시 그릴 수 있는 것은 부른 쪽이 먼저 다시 그려 여기 안 온다.
    /// 진입도로는 설계 기록에 «다시 그리지 못함»(<see cref="RoadDesign.SheetLost"/> · <see cref="RoadDesign.XsecLost"/>)을 켜 도킹창 상태 줄 · [업데이트] 깜박이 알린다(다시 그리면 저절로 꺼짐).
    /// 문서 잠금은 부르는 쪽.</summary>
    internal static Result Sweep(Document doc, string who, ObjectId only = default)
    {
        var R = new Result();
        if (!Pending) return R;   // 우리 면이 지워진 적 없으면 훑지 않는다
        bool siteSwept = false;
        var db = doc.Database;
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            var civ = Autodesk.Civil.ApplicationServices.CivilDocument.GetCivilDocument(db);
            foreach (ObjectId aid in civ.GetAlignmentIds())
            {
                try
                {
                    if (aid.IsNull || aid.IsErased || tr.GetObject(aid, OpenMode.ForRead) is not CivilDb.Alignment al) continue;
                    string an = al.Name ?? "";
                    bool road = RoadBuilder.IsOurs(al) || an.StartsWith(RoadBuilder.AlignBase, StringComparison.Ordinal);
                    bool site = !road && an.StartsWith(Commands.SectionCommand.AlignBase, StringComparison.Ordinal);
                    if ((!road && !site) || (!only.IsNull && aid != only) || NoSweep.Contains(aid)) continue;
                    var d = road ? RoadDesign.Read(tr, aid) : null;
                    var deadP = al.GetProfileIds().Cast<ObjectId>().Where(p => ProfileDead(tr, p)).ToList();
                    var deadG = al.GetSampleLineGroupIds().Cast<ObjectId>().Where(g => GroupDead(tr, g)).ToList();
                    if (deadP.Count == 0 && deadG.Count == 0) continue;
                    string me = aid.Handle.ToString();
                    bool sheetPart = false, xsecPart = false;
                    if (deadP.Count > 0)
                    {
                        // 이 노선의 종단뷰가 그 종단을 띠로 그린다 — 뷰를 먼저(진입도로 = 종단도 레이어 · 기록한 뷰만 · 부지 = 이 노선의 뷰 모두)
                        int nv = 0;
                        if (road && d != null) nv = EraseRoadViews(tr, al, d);
                        else foreach (ObjectId vid in al.GetProfileViewIds()) { try { if (!vid.IsErased) { tr.GetObject(vid, OpenMode.ForWrite, false, true).Erase(); nv++; } } catch { } }
                        foreach (var p in deadP) { try { tr.GetObject(p, OpenMode.ForWrite, false, true).Erase(); } catch { } }
                        if (road) R.RoadProf++; else { R.SiteProf++; siteSwept = true; }
                        R.Names.Add($"{an} 종단 {deadP.Count}(뷰 {nv})");
                        sheetPart = true;
                    }
                    foreach (var g in deadG)
                    {
                        string gn = ""; try { gn = ((CivilDb.SampleLineGroup)tr.GetObject(g, OpenMode.ForRead)).Name ?? ""; } catch { }
                        bool isXsec = road && d != null && g.Handle.ToString().Equals(d.XsecGroup, StringComparison.OrdinalIgnoreCase);
                        EraseGroup(tr, g);
                        if (road) { if (isXsec) { R.RoadXsec++; xsecPart = true; } else { sheetPart = true; if (deadP.Count == 0) R.RoadProf++; } }
                        else { R.SiteXsec++; siteSwept = true; }
                        R.Names.Add($"{an}/{gn}");
                    }
                    if (road && d != null)
                    {
                        if (sheetPart && deadP.Count == 0) EraseRoadViews(tr, al, d);   // 묶음만 죽었어도 종단뷰는 그 묶음을 띠 원천으로 문다
                        if (sheetPart) { try { Commands.SheetTag.EraseRoad(db, tr, me); } catch { } d.SheetLost = true; d.SheetLostSwept = true; }
                        if (xsecPart) { try { Commands.SheetTag.EraseRoad(db, tr, me, true); } catch { } d.XsecLost = true; d.XsecLostSwept = true; }
                        d.Write(tr, aid);
                    }
                }
                catch { }
            }
            tr.Commit();
        }
        catch (System.Exception ex) { try { DiagLog.Append($"\n■ 지운 면을 문 종단 · 횡단 정리 — {who} ⚠예외 {ex.GetType().Name}: {ex.Message}\n"); } catch { } return R; }
        // 부지는 도곽 · 장식(표식 없는 우리 레이어)을 함께 — 진입도로 것(표식)은 EraseAll이 건너뛴다
        if (siteSwept) { try { Commands.SheetCommand.EraseAll(db, null); } catch { } }
        if (only.IsNull || !InBatch) Pending = false;
        if (R.Any)
        {
            string msg = $"지운 면을 원천으로 문 {R.Text} 지움 — 다시: 진입도로 [지표면 업데이트] · 계획지표면 [종단도] · [횡단도]";
            try { DiagLog.Append($"\n■ 지운 면을 문 종단 · 횡단 정리 — {who} 뒤 · {msg}\n  {string.Join(" · ", R.Names)}\n"); } catch { }
            try { doc.Editor.WriteMessage("\n[정리] " + msg); } catch { }
        }
        return R;
    }
}
