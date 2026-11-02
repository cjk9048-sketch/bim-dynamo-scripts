using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using DH.Grading.Core;
using CivilApp = Autodesk.Civil.ApplicationServices;
using CivilDb = Autodesk.Civil.DatabaseServices;

namespace DH.Grading.Civil.Commands;

/// <summary>★★★[v111.5 · 계획_v111_4.md 3 · JACK 1030 «진입도로 횡단 = 계획지표면 [횡단도]와 똑같이»] 진입도로 횡단 — 부지 [횡단도]와 <b>같은 Build</b>를 <see cref="XsecTarget"/>로.
/// <para>여기는 진입도로만의 몫: 스타일 복사본(축척 짝 이름 · 부지 스타일을 안 덮음) · 옛 횡단 지우기(이 도로 표식 · 기록만) · 그린 것에 표식(«XSEC») · 부지 고르기에서 복사본 빼기.</para></summary>
public sealed partial class XsecViewCommand
{
    /// <summary>단면 종류 이름 — 진입도로 면(계획선 · FGL 밴드) · 부지 정지면(정지면_DH — 가는 점선 · 수량엔 안 씀).</summary>
    internal const string RoadKind = "진입도로", RoadPadKind = "정지면(부지)";
    /// <summary>진입도로 횡단의 부지 정지면 선 스타일.</summary>
    internal const string RoadPadStyleName = "DH_횡단면_정지면(진입도로)";
    /// <summary>진입도로 횡단 검토선 숨김 레이어 — «DH도로-»(부지 «DH-» 지우기 · 초기화 «DH-» 훑기에 안 걸리고 진입도로 초기화가 지움).</summary>
    internal const string RoadXsecHiddenLayer = "DH도로-횡단검토선(숨김)";

    /// <summary>★★[v111.5 · 계획 3] 진입도로 횡단 짓기 — 그리는 동안 모형 공간에 새로 붙은 평범한 객체(이름 · 표 · 도곽 · 축 숫자 · 측점 글씨 · 지층 이름)를 모아 끝에 표식(도로 핸들 · 가까운 뷰 · «XSEC»).
    /// 중간에 실패해도 그린 것에는 표식(안 붙이면 다음 [횡단도 그리기]가 못 지우고 · 부지 [횡단도]가 부지 것으로 지움 — 종단도와 같은 법).</summary>
    internal static bool BuildRoad(Autodesk.AutoCAD.ApplicationServices.Document doc, XsecTarget t, Point3d at)
    {
        var db = doc.Database;
        var appended = new List<ObjectId>();
        void OnApp(object o, ObjectEventArgs e)
        {
            try
            {
                // ★[v111.5 · 설치 전 검토 L4] 우리가 그리는 종류만(이름 · 측점 글씨 · 표 · 도곽 · 축 숫자 · 지층 이름) — Civil · 래퍼 없는 객체를 잡아 지우지 않게
                if (e.DBObject is DBText or MText or Line or Polyline or Polyline2d or Polyline3d or Circle or Arc or Table or BlockReference or Hatch or Solid)
                    appended.Add(e.DBObject.ObjectId);
            }
            catch { }
        }
        bool ok = false;
        db.ObjectAppended += OnApp;
        try { ok = Build(doc, at, t); }
        finally
        {
            db.ObjectAppended -= OnApp;
            try { t.Tagged = TagRoadXsec(db, t, appended); } catch { }
        }
        return ok;
    }

    static int TagRoadXsec(Database db, XsecTarget t, List<ObjectId> appended)
    {
        int n = 0;
        using var tr = db.TransactionManager.StartTransaction();
        var msId = SymbolUtilityServices.GetBlockModelSpaceId(db);
        var boxes = new List<(double X0, double Y0, double X1, double Y1, string H)>();
        foreach (var vid in t.Views)
            try { var ex = ((Entity)tr.GetObject(vid, OpenMode.ForRead)).GeometricExtents; boxes.Add((ex.MinPoint.X, ex.MinPoint.Y, ex.MaxPoint.X, ex.MaxPoint.Y, vid.Handle.ToString())); } catch { }
        var seen = new HashSet<ObjectId>();
        foreach (var id in appended)
        {
            try
            {
                if (id.IsNull || id.IsErased || !seen.Add(id)) continue;
                // ★[v111.5 · 설치 전 검토 L2] 읽기로 열어 모형 공간 것만 쓰기로(표 속 숨은 블록 안 객체 수천 개를 쓰기로 열지 않게)
                if (tr.GetObject(id, OpenMode.ForRead, false, true) is not Entity e || e.OwnerId != msId) continue;
                e.UpgradeOpen();
                string vh = "";
                try
                {
                    var ex = e.GeometricExtents;
                    double cx = (ex.MinPoint.X + ex.MaxPoint.X) / 2, cy = (ex.MinPoint.Y + ex.MaxPoint.Y) / 2, best = double.MaxValue;
                    foreach (var b in boxes)
                    {
                        double dx = cx < b.X0 ? b.X0 - cx : cx > b.X1 ? cx - b.X1 : 0, dy = cy < b.Y0 ? b.Y0 - cy : cy > b.Y1 ? cy - b.Y1 : 0;
                        double dd = dx * dx + dy * dy;
                        if (dd < best) { best = dd; vh = b.H; }
                    }
                }
                catch { }
                SheetTag.Put(db, tr, e, t.RoadHandle, vh, SheetTag.KindXsec);
                n++;
            }
            catch { }
        }
        tr.Commit();
        return n;
    }

    /// <summary>★[v111.5 · 계획 3 · 검토 H4] 이 도로의 옛 횡단 — 표식(«XSEC») · 기록의 뷰 핸들 · 기록의 그룹(우리 노선이면 «{노선}-횡단…» 이름도). 부지 · 다른 도로 것은 안 건드림.</summary>
    static void WipeRoad(Database db, CivilApp.CivilDocument cdoc, XsecTarget t, System.Text.StringBuilder log)
    {
        int nTag = 0, nView = 0, nGrp = 0;
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            nTag = SheetTag.EraseRoad(db, tr, t.RoadHandle, xsec: true);
            foreach (string h in t.OldViews)
            {
                try
                {
                    if (h.Length == 0 || !db.TryGetObjectId(new Handle(System.Convert.ToInt64(h, 16)), out var vid) || vid.IsErased) continue;
                    if (tr.GetObject(vid, OpenMode.ForRead) is not CivilDb.SectionView) continue;
                    tr.GetObject(vid, OpenMode.ForWrite, false, true).Erase(); nView++;
                }
                catch { }
            }
            if (tr.GetObject(t.AlignId, OpenMode.ForRead) is CivilDb.Alignment al)
            {
                var kill = new List<ObjectId>();
                foreach (ObjectId gid in al.GetSampleLineGroupIds())
                {
                    try
                    {
                        if (tr.GetObject(gid, OpenMode.ForRead) is not CivilDb.SampleLineGroup g) continue;
                        bool mine = gid.Handle.ToString() == t.OldGroup
                                    || (t.OwnAlign && t.GroupBase.Length > 0 && (g.Name ?? "").StartsWith(t.GroupBase, System.StringComparison.Ordinal));
                        if (mine) kill.Add(gid);
                    }
                    catch { }
                }
                foreach (var gid in kill) { try { tr.GetObject(gid, OpenMode.ForWrite, false, true).Erase(); nGrp++; } catch { } }
            }
            tr.Commit();
        }
        catch (System.Exception ex) { log?.AppendLine("  옛 진입도로 횡단 지우기 실패 — " + ex.Message); }
        log?.AppendLine($"  옛 진입도로 횡단 지움 — 표식 {nTag} · 뷰 {nView} · 검토선 그룹 {nGrp}(부지 횡단 · 다른 도로는 그대로)");
    }

    /// <summary>★[v111.5 · 검토 H1] 부지 스타일 고르기 — <see cref="SectionCommand.PickStyle"/>과 같은 규칙(이름에 든 첫 것 · 없으면 첫 것)인데 진입도로 복사본은 뺀다.
    /// 복사본이 없는 도면에서는 PickStyle과 같은 답이다.</summary>
    internal static ObjectId PickSiteStyle(Database db, System.Collections.IEnumerable styleIds, params string[] prefer)
    {
        ObjectId first = ObjectId.Null;
        var all = new List<(ObjectId Id, string Name)>();
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            foreach (ObjectId id in styleIds)
            {
                string nm = "";
                try { if (tr.GetObject(id, OpenMode.ForRead) is CivilDb.Styles.StyleBase sb) nm = sb.Name ?? ""; } catch { }
                if (RoadXsecRules.IsRoadClone(nm)) continue;
                if (first.IsNull) first = id;
                all.Add((id, nm));
            }
            tr.Commit();
        }
        catch { }
        foreach (var want in prefer)
            foreach (var (id, nm) in all)
                if (nm.IndexOf(want, System.StringComparison.OrdinalIgnoreCase) >= 0) return id;
        return first;
    }

    static string StyleName(Database db, ObjectId id)
    {
        if (id.IsNull) return "";
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            string nm = tr.GetObject(id, OpenMode.ForRead) is CivilDb.Styles.StyleBase sb ? sb.Name ?? "" : "";
            tr.Commit();
            return nm;
        }
        catch { return ""; }
    }

    /// <summary>이름이 정확히 같은 스타일(여러 모음에서) — 없으면 Null.</summary>
    static ObjectId FindStyle(Transaction tr, string name, params System.Collections.IEnumerable[] colls)
    {
        foreach (var coll in colls)
        {
            if (coll == null) continue;
            foreach (ObjectId id in coll)
                try { if (tr.GetObject(id, OpenMode.ForRead) is CivilDb.Styles.StyleBase sb && sb.Name == name) return id; } catch { }
        }
        return ObjectId.Null;
    }

    /// <summary>원본의 복사본(이름 = 원본 바탕 이름 + 꼬리) — 이미 있으면 그것. 원본이 이미 다른 꼬리의 복사본이면 바탕 이름으로.</summary>
    static ObjectId CloneStyle(Transaction tr, ObjectId src, string suffix, out bool made, params System.Collections.IEnumerable[] colls)
    {
        made = false;
        if (src.IsNull) return ObjectId.Null;
        var s0 = (CivilDb.Styles.StyleBase)tr.GetObject(src, OpenMode.ForRead);
        string want = RoadXsecRules.BaseName(s0.Name ?? "") + suffix;
        if ((s0.Name ?? "") == want) return src;
        var got = FindStyle(tr, want, colls);
        if (!got.IsNull) return got;
        made = true;
        return s0.CopyAsSibling(want);
    }

    static readonly string[] BandLabelProps =
    {
        "MajorIncrementLabelStyleId", "MinorIncrementLabelStyleId", "CenterlineLabelStyleId",
        "GradeBreaksLabelStyleId", "SampleLineVerticesLabelStyleId", "IncrementalDistanceLabelStyleId",
    };

    static ObjectId PropId(DBObject o, string prop)
    {
        try { var pi = o.GetType().GetProperty(prop); return pi != null ? (ObjectId)pi.GetValue(o)! : ObjectId.Null; } catch { return ObjectId.Null; }
    }

    /// <summary>★★[v111.5 · 계획 3 · 검토 H1 · M7] 진입도로 횡단 스타일 — 회사 스타일(부지와 같은 판정 · 복사본 뺌)의 <b>복사본</b>을 뷰에 건다.
    /// <list type="bullet">
    /// <item><paramref name="measure"/> = 축척을 아직 모름(자동) — 뷰 스타일만 «측정» 복사본(제목 · 눈금은 Build가 끔/맞춤) · 밴드는 원본 세트(칸 수만 셈 — 원본에 안 씀).</item>
    /// <item>최종 — 이름 = 축척 짝 «…(진입도로 1:200@500)» · 뷰 스타일 · 밴드 세트 · 세트 안 밴드 스타일(라벨 스타일까지 — 같은 객체면 글자를 안 씀) · 수량표 스타일 이름 · 제목 끔 · 눈금 맞춤 · «측정» 복사본 지움.</item>
    /// </list>
    /// 돌려줌 = 스타일을 건 뷰 수(0 = 회사 스타일이 없어 우리가 축을 그림 — 부지와 같은 갈래).</summary>
    static int ApplyRoadStyles(Database db, CivilApp.CivilDocument cdoc, List<(ObjectId Id, double St, string Name)> views,
                               XsecTarget t, double scale, double anno, System.Text.StringBuilder log, bool measure)
    {
        if (views == null || views.Count == 0) return 0;
        // ① 원본 — 부지 ApplyCompanyStyle과 같은 판정(이름에 «DH»가 없으면 회사 것 아님)
        ObjectId vsSrc = PickSiteStyle(db, cdoc.Styles.SectionViewStyles, XsecViewStyleName);
        if (StyleName(db, vsSrc).IndexOf("DH", System.StringComparison.Ordinal) < 0) vsSrc = ObjectId.Null;
        ObjectId bsSrc = PickSiteStyle(db, cdoc.Styles.SectionViewBandSetStyles, XsecBandSetName);
        if (StyleName(db, bsSrc).IndexOf("DH", System.StringComparison.Ordinal) < 0) bsSrc = ObjectId.Null;
        bool company = !vsSrc.IsNull || !bsSrc.IsNull;
        // 회사 뷰 스타일이 없으면 뷰가 지금 단 스타일을 복사(그 스타일은 부지 뷰도 쓸 수 있다 — 축척 값을 거기 쓰지 않게)
        if (vsSrc.IsNull) vsSrc = XsecStyleId(db, views);
        string suffix = measure ? RoadXsecRules.MeasureSuffix : RoadXsecRules.CloneSuffix(scale, anno);
        ObjectId vs = ObjectId.Null, bs = ObjectId.Null;
        int nBandMade = 0, nBandReuse = 0, nShared = 0; bool vsMade = false, bsMade = false;
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            vs = CloneStyle(tr, vsSrc, suffix, out vsMade, cdoc.Styles.SectionViewStyles);
            if (!measure && !bsSrc.IsNull)
            {
                bs = CloneStyle(tr, bsSrc, suffix, out bsMade, cdoc.Styles.SectionViewBandSetStyles);
                // ★[v111.5 · 설치 전 검토 M2] 있던 복사본이 회사 세트와 칸 구성이 다르면(템플릿을 고침) 옛 복사본은 이름만 바꿔 두고(다른 도로 뷰가 쥘 수 있음) 새로 복사 —
                //   자동 축척은 원본 세트로 칸 수를 재므로, 옛 복사본을 그대로 쓰면 밴드가 칸을 넘친다
                if (!bsMade && bs != bsSrc && !SameBandSet(tr, bsSrc, bs))
                {
                    var old = (CivilDb.Styles.StyleBase)tr.GetObject(bs, OpenMode.ForWrite);
                    string want = old.Name ?? "";
                    old.Name = want + $" 옛{System.DateTime.Now:MMddHHmmss}";
                    bs = ((CivilDb.Styles.StyleBase)tr.GetObject(bsSrc, OpenMode.ForRead)).CopyAsSibling(want);
                    bsMade = true;
                    log?.AppendLine($"  진입도로 밴드 세트 복사본 «{want}»이 회사 세트와 칸 구성이 달라 새로 복사(옛것은 이름을 바꿔 둠)");
                }
                var set = (CivilDb.Styles.SectionViewBandSetStyle)tr.GetObject(bs, OpenMode.ForWrite);
                var bandColls = new System.Collections.IEnumerable[] { cdoc.Styles.BandStyles.SectionViewSectionDataBandStyles, cdoc.Styles.BandStyles.SectionViewSegmentsBandStyles };
                foreach (bool bottom in new[] { true, false })
                {
                    using var items = bottom ? set.GetBottomBandSetItems() : set.GetTopBandSetItems();
                    bool changed = false;
                    for (int i = 0; i < items.Count; i++)
                    {
                        var it = items[i];
                        ObjectId sid = it.BandStyleId;
                        if (sid.IsNull) continue;
                        var c = CloneStyle(tr, sid, suffix, out bool mk, bandColls);
                        if (mk) nBandMade++; else nBandReuse++;
                        // 라벨 스타일 — 복사본이 원본과 같은 라벨 객체를 쥐면(얕은 복사) 그 라벨 글자는 안 씀(부지 밴드 글자가 바뀌지 않게) · 있던 복사본도 원본(바탕 이름)과 견줌
                        string sName = ((CivilDb.Styles.StyleBase)tr.GetObject(sid, OpenMode.ForRead)).Name ?? "";
                        ObjectId orig = RoadXsecRules.IsRoadClone(sName) ? FindStyle(tr, RoadXsecRules.BaseName(sName), bandColls) : sid;
                        if (!orig.IsNull && orig != c)
                        {
                            var src0 = tr.GetObject(orig, OpenMode.ForRead);
                            var dst0 = tr.GetObject(c, OpenMode.ForRead);
                            foreach (var prop in BandLabelProps)
                            {
                                ObjectId a = PropId(src0, prop), b = PropId(dst0, prop);
                                if (!a.IsNull && a == b) { t.SharedLabels.Add(b); nShared++; }
                            }
                        }
                        if (c != sid) { it.BandStyleId = c; changed = true; }
                    }
                    if (changed) { if (bottom) set.SetBottomBandSetItems(items); else set.SetTopBandSetItems(items); }
                }
            }
            tr.Commit();
        }
        catch (System.Exception ex)
        {
            log?.AppendLine("  ⚠진입도로 스타일 복사 실패 — " + ex.Message + "(원본에 축척 값을 안 쓰도록 축척 쓰기를 건너뜀)");
            vs = ObjectId.Null; bs = ObjectId.Null;
        }
        if (vs.IsNull)
        {
            // 복사본을 못 만들면 원본에 쓰지 않는다 — 뷰는 지금 스타일 그대로 · 축척 쓰기 대상에서 그 스타일 · 밴드를 뺌
            foreach (var x in BandStyleIdsOf(db, views)) t.SharedLabels.Add(x);
            log?.AppendLine("  ⚠진입도로 뷰 스타일 복사본 없음 — 글자 · 밴드 칸을 안 씀");
            return 0;
        }

        int n = 0, nb = 0;
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            foreach (var (vid, _, _) in views)
            {
                try
                {
                    if (tr.GetObject(vid, OpenMode.ForWrite) is not CivilDb.SectionView sv) continue;
                    sv.StyleId = vs; n++;
                    var bset = measure ? bsSrc : bs;
                    if (!bset.IsNull) { try { sv.Bands.ImportBandSetStyle(bset); nb++; } catch { } }
                }
                catch { }
            }
            tr.Commit();
        }
        catch (System.Exception ex) { log?.AppendLine("  진입도로 스타일 적용 실패 — " + ex.Message); }

        if (!measure)
        {
            t.TableStyle = RoadXsecRules.DictKey("DH-수량표" + suffix);   // 표 스타일 이름은 «:»를 못 받음 → «DH-수량표(진입도로 1-200@150)»
            // 밴드 스타일 중 복사본이 아닌 것(세트를 못 복사했거나 회사 세트가 없음)은 글자 · 칸 높이를 안 씀
            int nOrig = 0;
            foreach (var x in BandStyleIdsOf(db, views))
                if (!RoadXsecRules.IsRoadClone(StyleName(db, x)) && t.SharedLabels.Add(x)) nOrig++;
            TitleOffStyle(db, vs);
            TuneAxisTicks(db, vs, log);
            // «측정» 복사본 — 이제 아무 뷰도 안 씀 → 지움(못 지우면 남겨 둠 · 부지 고르기에서는 빠짐)
            //   ★[v111.5 · 설치 전 검토 L5] 모든 뷰가 최종 복사본으로 바뀌었을 때만(못 바뀐 뷰가 지운 스타일을 쥐지 않게)
            string mName = RoadXsecRules.BaseName(StyleName(db, vs)) + RoadXsecRules.MeasureSuffix;
            if (n == views.Count)
                try
                {
                    using var tr = db.TransactionManager.StartTransaction();
                    var mId = FindStyle(tr, mName, cdoc.Styles.SectionViewStyles);
                    if (!mId.IsNull && mId != vs) tr.GetObject(mId, OpenMode.ForWrite).Erase();
                    tr.Commit();
                }
                catch { }
            else log?.AppendLine($"  ⚠뷰 {views.Count - n}개가 최종 스타일로 안 바뀌어 «측정» 복사본을 남김");
            log?.AppendLine($"  진입도로 스타일 복사본 «{StyleName(db, vs)}»{(vsMade ? " 새로" : " 있던 것")} · 밴드 세트 «{StyleName(db, bs)}»{(bs.IsNull ? "(회사 세트 없음)" : bsMade ? " 새로" : " 있던 것")}"
                          + $" · 밴드 스타일 새로 {nBandMade} · 있던 것 {nBandReuse}"
                          + (nShared > 0 ? $" · ⚠원본과 같은 라벨 {nShared}개(그 글자는 안 씀)" : " · 라벨 스타일 모두 따로")
                          + (nOrig > 0 ? $" · ⚠복사본 아닌 밴드 {nOrig}칸(칸 높이 · 글자 안 씀)" : "")
                          + $" · 수량표 «{t.TableStyle}» · 뷰 {n}개 · 밴드 {nb}개{(company ? "" : " · ⚠회사 스타일 없음")}");
        }
        else log?.AppendLine($"  진입도로 — 축척을 고르기 전 «{StyleName(db, vs)}»로 잼(제목 끔 · 밴드는 원본 세트로 칸 수만) · 뷰 {n}개");
        return company ? n : 0;
    }

    /// <summary>★[v111.5 · 설치 전 검토 M2] 복사본 밴드 세트가 원본과 칸 구성이 같은가 — 위 · 아래 칸 수 · 칸마다 종류 · 밴드 스타일 바탕 이름.</summary>
    static bool SameBandSet(Transaction tr, ObjectId src, ObjectId clone)
    {
        try
        {
            var a = (CivilDb.Styles.SectionViewBandSetStyle)tr.GetObject(src, OpenMode.ForRead);
            var b = (CivilDb.Styles.SectionViewBandSetStyle)tr.GetObject(clone, OpenMode.ForRead);
            foreach (bool bottom in new[] { true, false })
            {
                using var ia = bottom ? a.GetBottomBandSetItems() : a.GetTopBandSetItems();
                using var ib = bottom ? b.GetBottomBandSetItems() : b.GetTopBandSetItems();
                if (ia.Count != ib.Count) return false;
                for (int i = 0; i < ia.Count; i++)
                {
                    if (ia[i].BandType != ib[i].BandType) return false;
                    string na = ia[i].BandStyleId.IsNull ? "" : ((CivilDb.Styles.StyleBase)tr.GetObject(ia[i].BandStyleId, OpenMode.ForRead)).Name ?? "";
                    string nb = ib[i].BandStyleId.IsNull ? "" : ((CivilDb.Styles.StyleBase)tr.GetObject(ib[i].BandStyleId, OpenMode.ForRead)).Name ?? "";
                    if (RoadXsecRules.BaseName(na) != RoadXsecRules.BaseName(nb)) return false;
                }
            }
            return true;
        }
        catch { return true; }   // 못 견주면 지금처럼(있던 복사본)
    }

    static List<ObjectId> BandStyleIdsOf(Database db, List<(ObjectId Id, double St, string Name)> views)
    {
        var ids = new List<ObjectId>();
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            foreach (var (vid, _, _) in views)
            {
                if (tr.GetObject(vid, OpenMode.ForRead) is not CivilDb.SectionView sv) continue;
                foreach (bool bottom in new[] { true, false })
                    try
                    {
                        using var bi = bottom ? sv.Bands.GetBottomBandItems() : sv.Bands.GetTopBandItems();
                        for (int i = 0; i < bi.Count; i++) { var id = bi[i].BandStyleId; if (!id.IsNull && !ids.Contains(id)) ids.Add(id); }
                    }
                    catch { }
                break;   // 뷰마다 같은 세트 — 첫 뷰만
            }
            tr.Commit();
        }
        catch { }
        return ids;
    }

    /// <summary>뷰 스타일 제목(GraphTitle) 끔 — Build의 같은 단계와 같은 일(최종 복사본에 다시 걸 때).</summary>
    static void TitleOffStyle(Database db, ObjectId styleId)
    {
        if (styleId.IsNull) return;
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            if (tr.GetObject(styleId, OpenMode.ForWrite) is CivilDb.Styles.SectionViewStyle svs)
            {
                using var ds = svs.GetDisplayStylePlan(CivilDb.Styles.SectionViewDisplayStyleType.GraphTitle);
                if (ds != null && ds.Visible) ds.Visible = false;
            }
            tr.Commit();
        }
        catch { }
    }
}
