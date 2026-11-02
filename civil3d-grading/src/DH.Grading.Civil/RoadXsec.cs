using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using DH.Grading.Civil.Commands;
using DH.Grading.Core;
using CivilApp = Autodesk.Civil.ApplicationServices;
using CivilDb = Autodesk.Civil.DatabaseServices;

namespace DH.Grading.Civil;

/// <summary>★★★[v111.5 · 계획_v111_4.md 3 · JACK 1030 «진입도로 횡단 = 계획지표면 [횡단도]와 똑같이 · 선 = 원지반 + 정지면 + 진입도로 · 토량표도 똑같이»]
/// 진입도로 횡단도 — 부지 [횡단도]와 <b>같은 코드</b>(<see cref="XsecViewCommand"/> Build)를 도로 대상(<see cref="XsecTarget"/>)으로.
/// <para>측점 = 진입도로 종단도 검토선(«{노선}-검토») 그대로 · 면 = 원지반 · 정지면_DH(선만) · 진입도로_DH · 지층 · 지하수위 · 계획면 = FirstOf(진입도로, 바탕면) ·
/// 폭 = 진입도로 면이 닿는 옆거리 + 여유 · 축척 = 도면설정 «진입도로 횡단도 축척»(0 = 자동) · 스타일 = 복사본(축척 짝 이름) · 그린 것 = 표식 «XSEC».</para></summary>
internal static class RoadXsec
{
    /// <summary>진입도로 종단도 검토선 그룹(설계 기록 핸들) — 살아 있고 검토선이 있으면 그 ID · 이름.</summary>
    internal static (ObjectId Id, string Name) SheetGroup(Transaction tr, Database db, RoadDesign d)
    {
        try
        {
            if (d.SheetGroup.Length > 0 && db.TryGetObjectId(new Handle(Convert.ToInt64(d.SheetGroup, 16)), out var gid) && !gid.IsErased
                && tr.GetObject(gid, OpenMode.ForRead) is CivilDb.SampleLineGroup g && g.GetSampleLineIds().Count > 0)
                return (gid, g.Name ?? "");
        }
        catch { }
        return (ObjectId.Null, "");
    }

    /// <summary>그 그룹의 측점 · 이름 꼬리(측점 차례) — 종단도에 찍힌 측점 이름이 곧 횡단 이름.</summary>
    internal static List<(double St, string Label)> Stations(Transaction tr, ObjectId gid, string gname, double interval)
    {
        var list = new List<(double St, string Label)>();
        if (gid.IsNull) return list;
        try
        {
            var g = (CivilDb.SampleLineGroup)tr.GetObject(gid, OpenMode.ForRead);
            foreach (ObjectId sid in g.GetSampleLineIds())
            {
                try
                {
                    if (tr.GetObject(sid, OpenMode.ForRead) is not CivilDb.SampleLine sl) continue;
                    list.Add((sl.Station, RoadXsecRules.LabelOf(sl.Name, gname, StationMarks.Fmt(sl.Station, interval))));
                }
                catch { }
            }
        }
        catch { }
        list.Sort((a, b) => a.St.CompareTo(b.St));
        return list;
    }

    /// <summary>지금 측점 지문(도킹창 «종단이 수정되었습니다» 판정) — 그룹이 없으면 빈 글.</summary>
    internal static string StationsKeyNow(Transaction tr, Database db, RoadDesign d)
    {
        var (gid, gname) = SheetGroup(tr, db, d);
        return gid.IsNull ? "" : RoadXsecRules.StationsKey(Stations(tr, gid, gname, Math.Max(1.0, GradingSettings.XsecInterval)));
    }

    /// <summary>[횡단도 그리기]를 못 하는 까닭(빈 글 = 됨) — 종단도 검토선 · 진입도로 지표면이 있어야 한다.
    /// ★[v111.7.1] <paramref name="surfaceComing"/> = 저절로 다시 만들기가 이 앞에서 지표면을 만든다(지표면은 안 봄).</summary>
    internal static string Refusal(RoadSession s, bool surfaceComing = false)
    {
        var db = s.Doc.Database;
        var d = s.Fresh();
        using (var tr = db.TransactionManager.StartTransaction())
        {
            var (gid, _) = SheetGroup(tr, db, d);
            tr.Commit();
            if (gid.IsNull) return "진입도로 종단도가 없습니다 — 진입도로 [종단도]로 종단도부터 그리세요(횡단 측점 = 종단도 측점).";
        }
        if (!surfaceComing && !s.HasSurface()) return "진입도로 지표면이 없습니다 — 진입도로 창 ■ 결과 [다시 시도]로 만드세요(횡단의 도로 선 · 수량 = 그 지표면).";
        return "";
    }

    /// <summary>★[계획 3 · 검토 M6] 횡단 폭 — 진입도로 면 바깥 경계 꼭짓점의 노선 옆거리 최대(좌 · 우) + 여유 5 m를 5 m 단위로 올림(종단도 검토선 폭과 같은 규칙 — <see cref="ProfileSheetRules.RoadWidth"/>).
    /// 노선 시점 앞 · 종점 뒤 꼭짓점은 뺀다(그 자리는 횡단이 안 지남). 못 재면 (0, 0).</summary>
    internal static (double L, double R, int N) RoadSideReach(Transaction tr, ObjectId aid, ObjectId roadSurf)
    {
        double l = 0, r = 0; int n = 0;
        try
        {
            var al = (CivilDb.Alignment)tr.GetObject(aid, OpenMode.ForRead);
            var cs = new CachedGroundSurface((CivilDb.TinSurface)tr.GetObject(roadSurf, OpenMode.ForRead));
            double s0 = al.StartingStation, s1 = al.EndingStation;
            foreach (var loop in cs.BoundaryLoops())
                foreach (var p in loop)
                {
                    double st = 0, off = 0; bool outside = false;
                    try { al.StationOffsetAcceptOutOfRange(p.X, p.Y, ref st, ref off, ref outside); } catch { continue; }
                    if (outside || st < s0 - 1e-6 || st > s1 + 1e-6) continue;
                    if (off < 0) l = Math.Max(l, -off); else r = Math.Max(r, off);
                    n++;
                }
        }
        catch { }
        return (l, r, n);
    }

    /// <summary>★★[계획 3] 진입도로 횡단도 짓기 — 부르는 쪽(<see cref="RoadSession.MakeXsec"/>)이 문서 잠금 · 바뀜 표시 막음을 건다. 돌려줌 = 결과 한 줄(자세한 것은 <paramref name="log"/> · 진단 로그).</summary>
    internal static string Build(RoadSession s, Point3d at, System.Text.StringBuilder log)
    {
        var doc = s.Doc; var db = doc.Database;
        var cdoc = CivilApp.CivilDocument.GetCivilDocument(db);
        GradingSettings.SyncToDocument(doc);                                   // 배치(열 × 행)는 부지와 같은 도면설정
        var d = s.Design;
        ObjectId aid = s.AlignId;
        string an = s.Name, me = aid.Handle.ToString();
        double pin = RoadDrawingSettings.Read(db).Xsec;
        log.AppendLine($"[진입도로 횡단도] {an} — 축척 {(pin > 0 ? $"고정 1:{pin:0}" : "자동")}");

        // ① 측점 · 면 · 폭
        List<(double St, string Label)> stations; string gname;
        ObjectId roadSurf = ObjectId.Null; string roadName = "";
        bool ours;
        (double L, double R, int N) reach;
        using (var tr = db.TransactionManager.StartTransaction())
        {
            var (gid, gn) = SheetGroup(tr, db, d);
            gname = gn;
            stations = Stations(tr, gid, gn, Math.Max(1.0, GradingSettings.XsecInterval));
            try
            {
                // ★[v111.5 · 설치 전 검토 L8] 우리 진입도로 면일 때만(레이어 · 설명 — RoadSession.HasSurface와 같은 판정)
                if (d.SurfaceHandle.Length > 0 && db.TryGetObjectId(new Handle(Convert.ToInt64(d.SurfaceHandle, 16)), out var sid) && !sid.IsErased
                    && tr.GetObject(sid, OpenMode.ForRead) is CivilDb.TinSurface ts
                    && (string.Equals(ts.Layer, RoadBuilder.LaySurf, StringComparison.OrdinalIgnoreCase) || (ts.Description ?? "").StartsWith("DH 진입도로 지표면"))) { roadSurf = sid; roadName = ts.Name; }
            }
            catch { }
            ours = tr.GetObject(aid, OpenMode.ForRead) is CivilDb.Alignment al0 && RoadBuilder.IsOurs(al0);
            reach = roadSurf.IsNull ? (0, 0, 0) : RoadSideReach(tr, aid, roadSurf);
            tr.Commit();
        }
        if (stations.Count == 0) throw new InvalidOperationException("진입도로 종단도 측점이 없습니다 — 진입도로 [종단도]부터");
        if (roadSurf.IsNull) throw new InvalidOperationException("진입도로 지표면이 없습니다 — 진입도로 창 ■ 결과 [다시 시도]로 만드세요");

        var all = SectionCommand.FindSurfaces(db, cdoc);
        var surfs = new List<SectionCommand.SurfPick>();
        foreach (var x in all) if (x.Label == "원지반") { surfs.Add(x); break; }
        // 부지 정지면 선 — 선만(수량은 계획면 = 진입도로 + 바탕면)
        //   ★[v111.5.1 · JACK 1102 스샷 «점선이 계획지표면이 없는 측점에서도 초록색 원지반과 겹쳐 보여»] 정지면_DH는 정지 밖에서 원지반 높이를 그대로 담아 도면 전체를 덮는 면 →
        //   부지 종단 · 횡단(JACK 0812)처럼 «정지한 자리만 담은 면»으로 그림: 정지순수옹벽_DH(옹벽 든 것) · 없으면 정지순수_DH · 둘 다 없으면 정지면_DH(알림)
        ObjectId pad = ObjectId.Null; string padName = "";
        bool baseIsPad = false; string baseName = "";
        using (var tr = db.TransactionManager.StartTransaction())
        {
            try { baseName = ((CivilDb.TinSurface)tr.GetObject(s.BaseSurfId, OpenMode.ForRead)).Name ?? ""; } catch { }
            baseIsPad = SectionCommand.IsBase(baseName, SectionCommand.PadSurfaceBase);
            ObjectId pureW = ObjectId.Null, pure = ObjectId.Null, full = ObjectId.Null; string nW = "", nP = "", nF = "";
            foreach (ObjectId sid in cdoc.GetSurfaceIds())
                try
                {
                    if (tr.GetObject(sid, OpenMode.ForRead) is not CivilDb.TinSurface ts) continue;
                    if (SectionCommand.IsBase(ts.Name, PureWithWallsBuilder.Name)) { pureW = sid; nW = ts.Name; }
                    else if (SectionCommand.IsBase(ts.Name, SectionCommand.PurePadSurfaceBase)) { pure = sid; nP = ts.Name; }
                    else if (SectionCommand.IsBase(ts.Name, SectionCommand.PadSurfaceBase)) { full = sid; nF = ts.Name; }
                }
                catch { }
            if (!pureW.IsNull) { pad = pureW; padName = nW; }
            else if (!pure.IsNull) { pad = pure; padName = nP; }
            else if (baseIsPad) { pad = s.BaseSurfId; padName = baseName; }
            else if (!full.IsNull) { pad = full; padName = nF; }
            tr.Commit();
        }
        if (!pad.IsNull && !SectionCommand.IsBase(padName, PureWithWallsBuilder.Name) && !SectionCommand.IsBase(padName, SectionCommand.PurePadSurfaceBase))
            log.AppendLine($"  ⚠정지한 자리만 담은 면(정지순수_DH)이 없어 '{padName}'으로 정지면 선을 그림 — 정지 밖 측점에서 원지반선과 겹쳐 보임");
        if (!pad.IsNull && !surfs.Exists(x => x.SurfId == pad)) surfs.Add(new SectionCommand.SurfPick(pad, padName, padName, XsecViewCommand.RoadPadKind));
        surfs.Add(new SectionCommand.SurfPick(roadSurf, roadName, roadName, XsecViewCommand.RoadKind));
        foreach (var x in all) if (x.Label == "지층" || x.Label == "지하수위") surfs.Add(x);
        if (!surfs.Exists(x => x.Label == "원지반")) log.AppendLine("  ⚠원지반을 못 찾았다 — 절토 · 성토가 빈다");

        // 폭 — 진입도로 면이 닿는 옆거리 + 여유(못 재면 표준횡단 가장자리 + 지난 미리보기 사면)
        var w = ProfileSheetRules.RoadWidth(reach.L, reach.R, 0, 0);
        if (reach.N == 0)
        {
            var sec = d.Section(); double sl = 0, sr = 0;
            if (s.Last != null && s.Last.Error.Length == 0)
                foreach (var q in s.Last.Slopes) { if (q.Side < 0) sl = Math.Max(sl, q.Width); else if (q.Side > 0) sr = Math.Max(sr, q.Width); }
            w = ProfileSheetRules.RoadWidth(sec.OuterL, sec.OuterR, sl, sr);
            log.AppendLine($"  ⚠진입도로 면 옆거리를 못 재 표준횡단 + 사면으로 폭을 잡음 — 좌 {w.L:0.#} / 우 {w.R:0.#} m");
        }
        else log.AppendLine($"  횡단 폭 좌 {w.L:0.#} / 우 {w.R:0.#} m(진입도로 면 옆거리 {reach.L:0.##} / {reach.R:0.##} m + 5 · 경계 꼭짓점 {reach.N})");
        log.AppendLine($"  측점 {stations.Count}개(«{gname}») · 면 {string.Join(" · ", surfs.Select(x => $"{x.Label}={x.SurfName}"))} · 바탕면 '{baseName}'");

        // ② 짓기(같은 Build) — 옛것 지우기 · 표식은 그 안
        var t = new XsecTarget
        {
            AlignId = aid, AlignName = an, RoadHandle = me, Stations = stations, Surfs = surfs,
            RoadSurfId = roadSurf, BaseSurfId = s.BaseSurfId, BaseIsPad = baseIsPad, Width = (w.L, w.R),
            GroupBase = an + "-횡단", Pin = pin, OldGroup = d.XsecGroup, OldViews = new List<string>(d.XsecViews), OwnAlign = ours,
        };
        bool ok = false;
        try { ok = XsecViewCommand.BuildRoad(doc, t, at); }
        finally
        {
            // ③ 설계에 적고 저장(실패해도 · 예외여도 — 옛것은 이미 지웠으니 지금 상태를 적는다 · ★[v111.5 · 설치 전 검토 M1] 예외 때 안 적으면 사람 노선에 그룹 · 뷰가 주인 없이 남음)
            d.XsecAt = new[] { at.X, at.Y };
            d.XsecScale = t.Scale; d.XsecAnno = t.Anno; d.XsecPin = pin;
            d.XsecGroup = t.GroupId.IsNull ? "" : t.GroupId.Handle.ToString();
            d.XsecViews = t.Views.Select(v => v.Handle.ToString()).ToList();
            d.XsecViewAt = ok ? RoadSession.ViewAt(db, t.Views) : Array.Empty<double>();   // ★[v111.7] 첫 · 끝 뷰 자리(옮겼나 — 저절로 다시 그리기 · 못 그렸으면 없음)
            if (ok) d.XsecLost = false;                                                       // ★[v111.7] 그려졌으면 «못 그림» 알림 끔(못 그렸으면 RoadSession.MakeXsec가 켬)
            d.XsecStamp = ok ? RoadXsecRules.Stamp(RoadXsecRules.StationsKey(stations), roadSurf.Handle.ToString(), d.SurfaceSig) : "";
            s.Design = d;
            try { s.SaveDesign(); } catch (System.Exception exS) { log.AppendLine("  ⚠횡단 기록 저장 실패 — " + exS.Message); }
        }
        try { doc.Editor.Regen(); } catch { }
        string msg = ok ? $"횡단도 — {t.Msg} · 표식 {t.Tagged}개" : "⚠횡단도를 못 그렸습니다 — 진단 로그를 보세요";
        log.AppendLine("  " + msg);
        return msg;
    }
}
