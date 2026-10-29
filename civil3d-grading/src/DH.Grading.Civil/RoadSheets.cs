using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using DH.Grading.Civil.Commands;
using DH.Grading.Core;
using CivilApp = Autodesk.Civil.ApplicationServices;
using CivilDb = Autodesk.Civil.DatabaseServices;

namespace DH.Grading.Civil;

/// <summary>★★★[v110.4 · 계획_v110_4.md 3.1 · JACK 1029 «종단의 경우 부지정지 종단처럼 도각사이즈와 밴드등 표현방식 모두 똑같이»]
/// 진입도로 종단도 — 부지 [종단도]와 <b>같은 코드</b>(검토선 → 지반 꺾은선 → 장 루프 · 도곽 · 밴드 6칸 → 검토선 꾸미기)를 도로 대상(<see cref="ProfileTarget"/> · <see cref="SheetContext"/>)으로.
/// <para>늘 고정 축척 + 장 나누기(JACK 결정). 축척 = 부지 종단도가 있으면 부지 축척(한 도면 한 축척 — 검토 M1) · 없으면 도면설정 고정 · 없으면 1:500.</para>
/// <para>도로가 그린 평범한 도면 객체(도곽 · 장식 · 세로선 · 검토선 글씨)에는 모두 표식(<see cref="SheetTag"/>) — 부지 지우기는 건너뛰고 [종단도 다시]는 이 도로 것만 지운다.
/// 막대 · 지층 · 터파기 재정의 · 옹벽 두 값은 안 그린다(계획 6). 부지 static · 횡단 폭 · 도면 주석 축척(부지가 있을 때)은 안 건드린다.</para></summary>
internal static class RoadSheets
{
    internal const string LayReview = "DH도로-검토선", LayChain = "DH도로-측점체인(숨김)";

    /// <summary>부지 종단뷰(도로가 아닌 «DH선형» 노선의 종단뷰)가 있나 · 부지 도곽 폭(표식 없는 모형 도곽 사각형 중 가장 넓은 것 · 모형 m).</summary>
    internal static (bool Site, double FrameW) SiteInfo(Database db, Transaction tr)
    {
        bool site = false; double fw = 0;
        var cdoc = CivilApp.CivilDocument.GetCivilDocument(db);
        foreach (ObjectId aid in cdoc.GetAlignmentIds())
        {
            try
            {
                if (tr.GetObject(aid, OpenMode.ForRead) is not CivilDb.Alignment al) continue;
                if (!al.Name.StartsWith(SectionCommand.AlignBase, StringComparison.Ordinal) || RoadDesign.Has(tr, aid)) continue;
                if (al.GetProfileViewIds().Count > 0) site = true;
            }
            catch { }
        }
        if (!site) return (false, 0);
        var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
        foreach (ObjectId id in ms)
        {
            try
            {
                if (tr.GetObject(id, OpenMode.ForRead) is not Polyline pl || !pl.Closed) continue;
                if (!string.Equals(pl.Layer, SheetCommand.LayFrameModel, StringComparison.OrdinalIgnoreCase) || SheetTag.IsRoad(pl)) continue;
                var ex = pl.GeometricExtents;
                fw = Math.Max(fw, ex.MaxPoint.X - ex.MinPoint.X);
            }
            catch { }
        }
        return (true, fw);
    }

    /// <summary>★★[계획 검토 M1] 도로 종단도 축척 · 까닭 한 줄 · 부지 종단뷰가 있나(있으면 도면 주석 축척을 다시 걸지 않는다).</summary>
    internal static (double Scale, string Note, bool Site) ScaleFor(Database db)
    {
        bool site; double fw;
        using (var tr = db.TransactionManager.StartTransaction()) { (site, fw) = SiteInfo(db, tr); tr.Commit(); }
        var r = ProfileSheetRules.RoadScale(site, SheetCommand.CurrentDrawingScale(db), fw, GradingSettings.ProfileScale, SheetCommand.Scales);
        return (r.Scale, r.Note, site);
    }

    /// <summary>이 축척에서 장 수(부지와 같은 장 나누기 — 측점 간격의 배수로 끊음).</summary>
    internal static int SheetCount(Database db, ObjectId aid, double scale)
    {
        double a = 0, b = 0;
        using (var tr = db.TransactionManager.StartTransaction())
        {
            var al = (CivilDb.Alignment)tr.GetObject(aid, OpenMode.ForRead);
            a = al.StartingStation; b = al.EndingStation;
            tr.Commit();
        }
        return SheetCommand.SplitPlan(a, b, scale, GradingSettings.XsecInterval, new System.Text.StringBuilder()).Count;
    }

    /// <summary>★[계획 검토 H1] 놓을 자리가 다른 종단도(부지 · 다른 도로)의 도곽 · 종단뷰 가로 범위와 겹치면 그 이름 — 안 겹치면 빈 글.
    /// <para>같은 X(위 · 아래)도 겹침으로 본다 — 부지 장식 지우기 · 밴드 글씨 벌리기가 가로 범위로 주인을 가린다. 이 도로의 옛 종단도(지울 것)는 뺀다.</para></summary>
    internal static string Overlap(Database db, ObjectId aid, Point3d at, double scale, int sheets)
    {
        var (x0, x1) = ProfileSheetRules.RoadSheetsSpan(at.X, scale, sheets);
        string me = aid.Handle.ToString();
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
            foreach (ObjectId id in ms)
            {
                try
                {
                    var o = tr.GetObject(id, OpenMode.ForRead);
                    if (o is Polyline pl && pl.Closed && string.Equals(pl.Layer, SheetCommand.LayFrameModel, StringComparison.OrdinalIgnoreCase))
                    {
                        if (SheetTag.Of(pl, out string r, out _, out _) && string.Equals(r, me, StringComparison.OrdinalIgnoreCase)) continue;
                        var ex = pl.GeometricExtents;
                        if (ProfileSheetRules.SpansOverlap(x0, x1, ex.MinPoint.X, ex.MaxPoint.X)) return "다른 종단도의 도곽";
                    }
                    else if (o is CivilDb.ProfileView pv && pv.AlignmentId != aid)
                    {
                        var ex = pv.GeometricExtents;
                        if (ProfileSheetRules.SpansOverlap(x0, x1, ex.MinPoint.X, ex.MaxPoint.X)) return $"종단도 «{pv.Name}»";
                    }
                }
                catch { }
            }
            tr.Commit();
        }
        catch { }
        return "";
    }

    /// <summary>계획 종단 도장(<see cref="ProfileSheetRules.Stamp"/>) — PVI · 노선 시 · 종점.</summary>
    internal static string StampOf(Transaction tr, ObjectId aid, ObjectId planId)
    {
        var al = (CivilDb.Alignment)tr.GetObject(aid, OpenMode.ForRead);
        var pr = (CivilDb.Profile)tr.GetObject(planId, OpenMode.ForRead);
        var pv = new List<(double, double)>();
        foreach (CivilDb.ProfilePVI q in pr.PVIs) pv.Add((q.RawStation, q.Elevation));
        return ProfileSheetRules.Stamp(pv, al.StartingStation, al.EndingStation);
    }

    /// <summary>★[계획 검토 M7] 부지 [종단도] 끝 — 진입도로 종단도 축척이 <paramref name="siteScale"/>과 다르면 한 줄(같으면 빈 글).</summary>
    internal static string ScaleMismatch(Database db, double siteScale)
    {
        if (!(siteScale > 0)) return "";
        var bad = new List<string>();
        using (var tr = db.TransactionManager.StartTransaction())
        {
            var cdoc = CivilApp.CivilDocument.GetCivilDocument(db);
            foreach (ObjectId aid in cdoc.GetAlignmentIds())
            {
                try
                {
                    var d = RoadDesign.Read(tr, aid);
                    if (d == null || d.SheetViews.Count == 0 || !(d.SheetScale > 0) || Math.Abs(d.SheetScale - siteScale) < 1e-6) continue;
                    bad.Add($"{((CivilDb.Alignment)tr.GetObject(aid, OpenMode.ForRead)).Name} 1:{d.SheetScale:0}");
                }
                catch { }
            }
            tr.Commit();
        }
        return bad.Count == 0 ? "" : $"진입도로 종단도 축척 다름({string.Join(", ", bad)} ≠ 부지 1:{siteScale:0}) — 도킹창 [종단도 다시]";
    }

    /// <summary>★★[계획 3.1] 진입도로 종단도 짓기 — 부르는 쪽(<see cref="RoadSession.MakeSheets"/>)이 문서 잠금 · 바뀜 표시 막음(Suppress)을 건다.
    /// 돌려줌 = 결과 한 줄(자세한 것은 <paramref name="log"/>).</summary>
    internal static string Build(RoadSession s, Point3d at, System.Text.StringBuilder log)
    {
        var doc = s.Doc; var db = doc.Database; var ed = doc.Editor;
        var cdoc = CivilApp.CivilDocument.GetCivilDocument(db);
        GradingSettings.SyncToDocument(doc);                                   // ① 도면설정(축척 고정 · 측점 간격)
        var d = s.Design;
        ObjectId aid = s.AlignId;
        string an = s.Name, me = aid.Handle.ToString();
        var (scale, scNote, site) = ScaleFor(db);                              // ② 축척(검토 M1)
        log.AppendLine($"[진입도로 종단도] {an} — 축척 1:{scale:0} · {scNote}");

        // ③ 옛것 지우기 — 이 도로 노선 안에서만(검토 M3): 표식 → 종단뷰(적어 둔 핸들 · «DH도로-종단뷰» 레이어) → «{노선}-검토*» 그룹 → «-지반» · 측점 체인(«-바탕» · «-계획»은 남김)
        int nTag = 0, nView = 0, nGrp = 0, nProf = 0;
        using (var tr = db.TransactionManager.StartTransaction())
        {
            nTag = SheetTag.EraseRoad(db, tr, me);
            var al = (CivilDb.Alignment)tr.GetObject(aid, OpenMode.ForRead);
            var hv = new HashSet<string>(d.SheetViews, StringComparer.OrdinalIgnoreCase);
            foreach (ObjectId vid in al.GetProfileViewIds())
            {
                try
                {
                    var v = (Entity)tr.GetObject(vid, OpenMode.ForRead);
                    if (!hv.Contains(vid.Handle.ToString()) && !string.Equals(v.Layer, RoadBuilder.LayView, StringComparison.OrdinalIgnoreCase)) continue;
                    tr.GetObject(vid, OpenMode.ForWrite, false, true).Erase(); nView++;
                }
                catch { }
            }
            foreach (ObjectId gid in al.GetSampleLineGroupIds())
            {
                try
                {
                    var g = (CivilDb.SampleLineGroup)tr.GetObject(gid, OpenMode.ForRead);
                    if (gid.Handle.ToString() != d.SheetGroup && !(g.Name ?? "").StartsWith(an + "-검토", StringComparison.Ordinal)) continue;
                    tr.GetObject(gid, OpenMode.ForWrite, false, true).Erase(); nGrp++;
                }
                catch { }
            }
            foreach (ObjectId pid in al.GetProfileIds())
            {
                try
                {
                    if (pid == s.PlanId || pid == s.BaseProfId) continue;
                    var p = (CivilDb.Profile)tr.GetObject(pid, OpenMode.ForRead);
                    string pn = p.Name ?? "", h = pid.Handle.ToString();
                    bool mine = h == d.SheetGround || h == d.SheetChain || pn.StartsWith(an + "-지반", StringComparison.Ordinal)
                                || pn.StartsWith("DH_측점체인", StringComparison.Ordinal);
                    if (!mine) continue;
                    tr.GetObject(pid, OpenMode.ForWrite, false, true).Erase(); nProf++;
                }
                catch { }
            }
            tr.Commit();
        }
        log.AppendLine($"  옛 종단도 지움 — 표식 {nTag} · 종단뷰 {nView} · 검토선 그룹 {nGrp} · 종단 {nProf}");

        ObjectId layProf = SectionCommand.EnsureLayerStandalone(db, RoadBuilder.LayProf, 6);
        ObjectId labels = SectionCommand.PickStyle(db, cdoc.Styles.LabelSetStyles.ProfileLabelSetStyles, "_없음", "None", "표준", "Standard");

        // ③-b ★[검토 M6] «-바탕»이 지금 바탕면을 안 따르면(면이 지워졌거나 바뀜) 지금 바탕면으로 다시 — 노선 25 · 50 · 75 % 자리에서 면 높이와 견줌
        string baseName = "";
        try
        {
            bool stale = s.BaseProfId.IsNull || s.BaseProfId.IsErased;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var surf = (CivilDb.TinSurface)tr.GetObject(s.BaseSurfId, OpenMode.ForRead);
                baseName = surf.Name;
                if (!stale)
                {
                    var al = (CivilDb.Alignment)tr.GetObject(aid, OpenMode.ForRead);
                    var bp = (CivilDb.Profile)tr.GetObject(s.BaseProfId, OpenMode.ForRead);

                    foreach (double f in new[] { 0.25, 0.5, 0.75 })
                    {
                        double st = al.StartingStation + (al.EndingStation - al.StartingStation) * f, x = 0, y = 0, zs;
                        try { al.PointLocation(st, 0, ref x, ref y); zs = surf.FindElevationAtXY(x, y); } catch { continue; }
                        double zp;
                        try { zp = bp.ElevationAt(st); } catch { stale = true; break; }
                        if (!double.IsFinite(zp) || Math.Abs(zp - zs) > 0.05) { stale = true; break; }

                    }
                    // ★[설치 전 검토 · 낮음] 세 자리 다 면 밖이라 못 재면 낡았다고 보지 않는다(그때마다 다시 만들지 않게)
                }
                tr.Commit();
            }
            if (stale)
            {
                // ★[설치 전 검토 · 낮음] 새것을 임시 이름으로 먼저 만들고 → 옛것 지우고 → 이름 바꿈(만들기가 실패해도 옛 «-바탕»은 남음)
                string bpName = an + "-바탕";
                var gStyle = SectionCommand.PickStyle(db, cdoc.Styles.ProfileStyles, "기존", "Existing", "지반", "기본", "Standard");
                ObjectId nb = CivilDb.Profile.CreateFromSurface(bpName + "-새", aid, s.BaseSurfId, layProf, gStyle, labels);
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    if (!s.BaseProfId.IsNull && !s.BaseProfId.IsErased) tr.GetObject(s.BaseProfId, OpenMode.ForWrite, false, true).Erase();
                    ((CivilDb.Profile)tr.GetObject(nb, OpenMode.ForWrite)).Name = bpName;
                    tr.Commit();
                }
                s.BaseProfId = nb;
                d.BaseProfile = s.BaseProfId.Handle.ToString();
                log.AppendLine($"  ⚠«{bpName}»이 지금 바탕면 '{baseName}'을 안 따라 다시 만들었다");
            }
        }
        catch (System.Exception ex) { log.AppendLine("  «-바탕» 확인 실패 — " + ex.Message); }
        ObjectId baseProf = s.BaseProfId;

        // ④ 스타일 심기(부지 P:598-607과 같음) · 측점 색인 증분(No.가 측점 간격마다)
        ProfileStyleTemplate.Import(db, cdoc);
        log.AppendLine("  " + ProfileStyleTemplate.LastReport);
        log.AppendLine("  " + ProfileStyleTemplate.EnsureProfileSectionalBandStyles(db, cdoc));
        log.AppendLine("  " + ProfileStyleTemplate.ImportBlocks(db, n => n.Contains("배너") || n.Contains("축척")));
        double iv = Math.Max(1.0, GradingSettings.XsecInterval);
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            if (tr.GetObject(aid, OpenMode.ForWrite) is CivilDb.Alignment alIdx) alIdx.StationIndexIncrement = iv;
            tr.Commit();
        }
        catch (System.Exception ex) { log.AppendLine("  측점 색인 증분 실패 — " + ex.Message); }

        // ⑤ «-계획» 스타일 «기본»(부지 계획선과 같은 꼴)
        ObjectId profStyle = SectionCommand.PickStyle(db, cdoc.Styles.ProfileStyles, "기본", "Standard", "Basic");
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            if (!profStyle.IsNull && tr.GetObject(s.PlanId, OpenMode.ForWrite) is CivilDb.Profile pp) pp.StyleId = profStyle;
            tr.Commit();
        }
        catch (System.Exception ex) { log.AppendLine("  «-계획» 스타일 실패 — " + ex.Message); }

        // ⑥ 대상 — 부지 출처 · static · 횡단 그룹 없음 · 이름 · 레이어 · 폭(가장자리 + 가장 넓은 사면 + 5 m)
        var sec = d.Section();
        double sl = 0, sr = 0;
        if (s.Last != null && s.Last.Error.Length == 0)
            foreach (var q in s.Last.Slopes) { if (q.Side < 0) sl = Math.Max(sl, q.Width); else if (q.Side > 0) sr = Math.Max(sr, q.Width); }
        var w = ProfileSheetRules.RoadWidth(sec.OuterL, sec.OuterR, sl, sr);
        var t = new ProfileTarget
        {
            SiteSources = false, WriteStatics = false, XsecGroup = false,
            GroupBase = an + "-검토", ChainLayer = LayChain, SampleLineLayer = LayReview, Width = w,
            GroundName = an + "-지반", GroundTempName = an + "-지반작업중", RenameRaw = false,
        };
        var surfs = new List<SectionCommand.SurfPick> { new(s.BaseSurfId, baseName, an + "-바탕", "원지반") };
        log.AppendLine($"  검토선 폭 좌 {w.L:0.#} m / 우 {w.R:0.#} m(가장자리 {sec.OuterL:0.#}/{sec.OuterR:0.#} + 사면 {sl:0.#}/{sr:0.#} + 5)");

        // ⑦ 검토선(측점) → ⑧ 지반 꺾은선(«-지반»)
        ObjectId grp = ProfileCommand.BuildSampleLines(db, ed, aid, baseProf, s.PlanId, surfs, iv, out var allMarks, log, t);
        ObjectId groundLine = ProfileCommand.RebuildGroundAsPolyline(db, aid, baseProf, layProf, profStyle, labels, allMarks, log,
                                                                     t.GroundName, t.GroundTempName, renameRaw: false);

        // ⑨ 장 루프 · 도곽 · 밴드(부지와 같은 함수) — 새로 생긴 평범한 도면 객체를 모아 끝에 표식
        var ctx = new SheetContext
        {
            AlignId = aid, PidGround = baseProf, PidPad = s.PlanId, PidGroundLine = groundLine, ChainId = t.ChainId, GroupId = grp,
            Scale = scale, SampleLines = t.Made, Marks = t.Marks, RoadHandle = me,
            // ★[설치 전 검토 · 중간] 도면 축척을 안 거는 것은 부지가 건 주석 축척과 같을 때만 — 축척을 도곽 폭 · 기본값에서 얻었으면(사용자가 상태줄에서 바꿈 등) 걸어야 밴드 글씨 · 칸이 축척을 따른다
            KeepDrawingScale = site && SheetCommand.CurrentDrawingScale(db) > 0 && Math.Abs(SheetCommand.CurrentDrawingScale(db) - scale) < 1e-6,
        };
        var appended = new List<ObjectId>();
        void OnApp(object? o, ObjectEventArgs e)
        {
            try
            {
                if (e.DBObject is Entity en && !(en.GetType().Namespace ?? "").StartsWith("Autodesk.Civil", StringComparison.Ordinal)) appended.Add(en.ObjectId);
            }
            catch { }
        }
        (ObjectId PvId, List<ObjectId> PvIds, string SheetAll, int Count) r;
        bool hooked = true;
        db.ObjectAppended += OnApp;
        try
        {
            r = ProfileCommand.PlaceSheets(db, ed, cdoc, aid, at, scale, groundLine, s.PlanId, grp, surfs, log, ctx, t.ChainId);
            ProfileCommand.DecorateSampleLines(db, cdoc, aid, iv, w.L, w.R, log, t.Made);   // ⑩ 검토선 꾸미기(부지와 같게)
        }
        catch
        {
            // ★[설치 전 검토 · 중간] 중간에 실패해도 이미 그린 것에 표식 — 안 붙이면 다음 [종단도 다시]가 못 지워 겹쳐 쌓이고 · 같은 자리를 «다른 종단도»로 거절하고 · 부지 «지우고 새로»가 부지 것으로 지운다
            db.ObjectAppended -= OnApp; hooked = false;
            try { TagAppended(new List<ObjectId>()); } catch { }
            throw;
        }
        finally { if (hooked) db.ObjectAppended -= OnApp; }

        // ⑪ 표식 · «-바탕» 숨김(뷰마다 재정의 — 검토 M5) — 막대 · 지층 · 터파기 재정의는 안 부름
        int nTagged = TagAppended(r.PvIds), nHid = 0;
        ObjectId hid = ProfileCommand.EnsureHiddenProfileStyle(db, log);
        using (var tr = db.TransactionManager.StartTransaction())
        {
            if (!hid.IsNull && groundLine != baseProf && !baseProf.IsNull)
            {
                string bn = "";
                try { bn = ((CivilDb.Profile)tr.GetObject(baseProf, OpenMode.ForRead)).Name; } catch { }
                foreach (var vid in r.PvIds)
                {
                    try
                    {
                        if (tr.GetObject(vid, OpenMode.ForWrite) is not CivilDb.ProfileView pv) continue;
                        foreach (CivilDb.ProfileOverride ov in pv.GraphOverrides)
                        {
                            string pn = "";
                            try { pn = ov.ProfileName ?? ""; } catch { }
                            if (pn.Length > 0 && pn == bn) { ov.OverrideStyleId = hid; nHid++; }
                        }
                    }
                    catch { }
                }
            }
            tr.Commit();
        }

        // 짓는 동안 모형 공간에 새로 붙은 평범한 객체에 표식(가장 가까운 종단뷰 핸들 · 뷰를 모르면 빈칸)
        int TagAppended(List<ObjectId> pvIds)
        {
            int nT = 0;
            using var tr = db.TransactionManager.StartTransaction();
            var msId = SymbolUtilityServices.GetBlockModelSpaceId(db);
            var spans = new List<(double X0, double X1, string H)>();
            foreach (var vid in pvIds)
                try { var ex = ((Entity)tr.GetObject(vid, OpenMode.ForRead)).GeometricExtents; spans.Add((ex.MinPoint.X, ex.MaxPoint.X, vid.Handle.ToString())); } catch { }
            foreach (var id in appended.Distinct())
            {
                try
                {
                    if (id.IsNull || id.IsErased) continue;
                    if (tr.GetObject(id, OpenMode.ForWrite, false, true) is not Entity e || e.OwnerId != msId) continue;
                    string vh = "";
                    try
                    {
                        var ex = e.GeometricExtents; double cx = (ex.MinPoint.X + ex.MaxPoint.X) / 2;
                        double best = double.MaxValue;
                        foreach (var sp in spans)
                        {
                            double dd = cx < sp.X0 ? sp.X0 - cx : cx > sp.X1 ? cx - sp.X1 : 0;
                            if (dd < best) { best = dd; vh = sp.H; }
                        }
                    }
                    catch { }
                    SheetTag.Put(db, tr, e, me, vh, e.Layer);
                    nT++;
                }
                catch { }
            }
            tr.Commit();
            return nT;
        }

        // ⑫ 설계에 적고 저장 → 다시 그림
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            d.SheetStamp = StampOf(tr, aid, s.PlanId);
            tr.Commit();
        }
        catch { d.SheetStamp = ""; }
        d.SheetAt = new[] { at.X, at.Y };
        d.SheetScale = scale;
        d.SheetViews = r.PvIds.Select(v => v.Handle.ToString()).ToList();
        d.SheetGroup = grp.IsNull ? "" : grp.Handle.ToString();
        d.SheetGround = groundLine.IsNull || groundLine == baseProf ? "" : groundLine.Handle.ToString();
        d.SheetChain = t.ChainId.IsNull ? "" : t.ChainId.Handle.ToString();
        s.Design = d;
        s.SaveDesign();
        try { ed.Regen(); } catch { }
        string msg = $"종단도 {r.Count}장(1:{scale:0} · A1 도곽 · 밴드) — 측점 {t.Marks.Count}개 · 검토선 {t.Made.Count}개 · 표식 {nTagged}개"
                   + (nHid > 0 ? "" : " · ⚠«-바탕» 숨김 못 함")
                   + (scNote.Contains("⚠") ? " · " + scNote : "")
                   + (grp.IsNull ? " · ⚠검토선 그룹 없음(밴드 값이 빈다)" : "");
        log.AppendLine("  " + msg + $" · «-바탕» 숨김 {nHid}뷰");
        return msg;
    }
}
