using Autodesk.AutoCAD.DatabaseServices;
using DH.Grading.Civil.Commands;
using DH.Grading.Core;
using CivilApp = Autodesk.Civil.ApplicationServices;
using CivilDb = Autodesk.Civil.DatabaseServices;

namespace DH.Grading.Civil;

/// <summary>★★[v117.6.1 · JACK 1111 «진입도로 횡단도도 주석 축척을 따른다» — 5a97d38 진입도로 종단 KeepLook의 횡단 짝]
/// 부지 [종단도]가 도면 주석 축척을 바꾸면(1:500 → 1:100) 그려 둔 진입도로 횡단뷰의 밴드 칸 · 밴드 글자 · 축 눈금 글자(스타일 종이 값 × CANNOSCALE)가 따라 변한다.
/// 그린 때 주석 축척 ÷ 지금을 <b>이 도로 복사본</b> 스타일(뷰 · 밴드 · 라벨)에 구워 꼴을 그대로 둔다(다시 안 그림 · 스타일만).
/// <para>종단과 다른 점: 횡단 복사본 이름은 도로 구분이 없어(«…(진입도로 1:200@500)») 같은 축척의 다른 도로와 같이 쓴다 → 굽기 전에 늘 «#도로핸들»이 붙은 이 도로 몫을 따로 만든다.</para></summary>
internal static class XsecScaleStyles
{
    static bool Mine(string? name, string tag) => name != null && RoadXsecRules.IsRoadClone(name) && name.Contains(" #" + tag + ")", StringComparison.Ordinal);

    /// <summary>복사본 이름 꼬리 — «(진입도로 1:S@A #도로)».</summary>
    static string Suffix(double scale, double anno, string tag)
    {
        string s = RoadXsecRules.CloneSuffix(scale, anno);
        return s.Substring(0, s.Length - 1) + " #" + tag + ")";
    }

    /// <summary>부지 [종단도]가 주석 축척을 건 직후 부른다 — 그린 때와 지금 주석 축척이 다른 진입도로 횡단도마다 스타일에 비율을 굽고 XsecAnno · 장 표식을 지금 값으로.</summary>
    internal static void KeepLook(Database db, System.Text.StringBuilder log)
    {
        double a1 = SheetCommand.CurrentDrawingScale(db);
        if (!(a1 > 0)) return;
        var todo = new List<(ObjectId Aid, string Name, RoadDesign D, List<ObjectId> Views, double A0)>();
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            foreach (ObjectId aid in CivilApp.CivilDocument.GetCivilDocument(db).GetAlignmentIds())
            {
                try
                {
                    var d = RoadDesign.Read(tr, aid);
                    if (d == null || d.XsecViews.Count == 0 || !(d.XsecAnno > 0) || Math.Abs(d.XsecAnno - a1) < 1e-6) continue;
                    var views = new List<ObjectId>();
                    foreach (var h in d.XsecViews)
                        try
                        {
                            if (db.TryGetObjectId(new Handle(Convert.ToInt64(h, 16)), out var vid) && !vid.IsErased
                                && tr.GetObject(vid, OpenMode.ForRead) is CivilDb.SectionView) views.Add(vid);
                        }
                        catch { }
                    if (views.Count > 0) todo.Add((aid, ((CivilDb.Alignment)tr.GetObject(aid, OpenMode.ForRead)).Name, d, views, d.XsecAnno));
                }
                catch { }
            }
            tr.Commit();
        }
        catch (Exception ex) { log.AppendLine("  ⚠진입도로 횡단도 꼴 지키기 — 읽기 실패 " + ex.Message); return; }
        foreach (var (aid, name, d, views, a0) in todo)
        {
            try
            {
                string msg = Rebake(db, views, d.XsecScale, a0, a1, aid.Handle.ToString(), log);
                d.XsecAnno = a1;
                using (var tr = db.TransactionManager.StartTransaction()) { d.Write(tr, aid); tr.Commit(); }
                try { if (Autodesk.AutoCAD.ApplicationServices.Core.Application.DocumentManager.GetDocument(db) is { } doc && RoadSession.Of(doc) is RoadSession s && s.AlignId == aid) s.Design.XsecAnno = a1; } catch { }
                RetagFrames(db, aid.Handle.ToString(), SheetFrameTag.KindXsec, a0, a1);
                log.AppendLine($"  진입도로 횡단도 꼴 그대로 — {name} 1:{d.XsecScale:0} · {msg}");
            }
            catch (Exception ex) { log.AppendLine($"  ⚠진입도로 횡단도 꼴 지키기 실패 — {name}: {ex.Message}"); }
        }
    }

    /// <summary>이 도로 장 표식(도면화 뷰포트가 거는 주석 축척)의 그린 때 값 <paramref name="a0"/> → <paramref name="a1"/>. 스타일을 a1 기준으로 구웠으므로 뷰포트도 a1이어야 같은 꼴.
    /// 종단(<see cref="SheetFrameTag.KindProf"/>)도 같은 까닭으로 부른다.</summary>
    internal static void RetagFrames(Database db, string roadHandle, string kind, double a0, double a1)
    {
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
            foreach (ObjectId id in ms)
            {
                if (id.ObjectClass.DxfName != "LWPOLYLINE") continue;
                try
                {
                    if (tr.GetObject(id, OpenMode.ForRead) is not Polyline pl) continue;
                    var (fk, fs, fa) = SheetFrameTag.Read(pl);
                    if (fk != kind || Math.Abs(fa - a0) > 1e-6) continue;
                    if (!SheetTag.Of(pl, out string road, out _, out _) || road != roadHandle) continue;
                    pl.UpgradeOpen();
                    SheetFrameTag.Put(db, tr, pl, fk, fs, a1);
                }
                catch { }
            }
            tr.Commit();
        }
        catch { }
    }

    /// <summary>횡단뷰들을 주석 축척 a0(그린 때) → a1(지금)에서 같은 꼴로 — 이 도로 복사본 스타일에 a0 ÷ a1을 곱한다. 돌려줌 = 한 줄.</summary>
    internal static string Rebake(Database db, IReadOnlyList<ObjectId> viewIds, double scale, double a0, double a1, string tag, System.Text.StringBuilder log)
    {
        if (!(a0 > 0) || !(a1 > 0) || Math.Abs(a0 - a1) < 1e-6 || viewIds.Count == 0) return "";
        double k = a0 / a1;
        string suffix = Suffix(scale, a1, tag);
        int nView = 0, nBand = 0, nProp = 0, nLabel = 0;
        var doneStyle = new HashSet<ObjectId>(); var doneLabel = new HashSet<ObjectId>();
        var bandMap = new Dictionary<ObjectId, ObjectId>();
        var viewMap = new Dictionary<ObjectId, ObjectId>();
        var replaced = new HashSet<ObjectId>();
        var mine = new Dictionary<string, ObjectId>(StringComparer.Ordinal);   // 이 도로 복사본 이름 → id(같은 이름 비키기)
        var cdoc = CivilApp.CivilDocument.GetCivilDocument(db);
        using var tr = db.TransactionManager.StartTransaction();
        foreach (var coll in new System.Collections.IEnumerable[] { cdoc.Styles.SectionViewStyles, cdoc.Styles.BandStyles.SectionViewSectionDataBandStyles, cdoc.Styles.BandStyles.SectionViewSegmentsBandStyles })
            try { foreach (ObjectId sid in coll) try { if (tr.GetObject(sid, OpenMode.ForRead) is CivilDb.Styles.StyleBase sb && Mine(sb.Name, tag)) mine[sb.Name] = sid; } catch { } } catch { }
        // 새로 지을 이름이 이미 있으면(지난 판 찌꺼기) 비켜 둔다 — CopyAsSibling · 이름 바꾸기가 같은 이름에서 실패하지 않게
        void MakeRoom(string want, ObjectId keep)
        {
            if (!mine.TryGetValue(want, out var old) || old == keep || old.IsErased) return;
            try { ((CivilDb.Styles.StyleBase)tr.GetObject(old, OpenMode.ForWrite)).Name = want.Replace(" #" + tag + ")", $" #{tag} 옛{DateTime.Now:MMddHHmmss})"); } catch { }
            mine.Remove(want);
        }
        foreach (var viewId in viewIds)
        {
            if (viewId.IsNull || viewId.IsErased || tr.GetObject(viewId, OpenMode.ForWrite) is not CivilDb.SectionView sv) continue;
            // 뷰 스타일 — 이 도로 몫이면 그 자리에서 k배 · 아니면(회사 것 · 같은 축척 다른 도로와 같이 쓰던 것) 이 도로 복사본을 만들어 걸고 k배
            var vs0 = (CivilDb.Styles.StyleBase)tr.GetObject(sv.StyleId, OpenMode.ForRead);
            string vsName = vs0.Name ?? "", vWant = RoadXsecRules.BaseName(vsName) + suffix;
            if (Mine(vsName, tag))
            {
                if (doneStyle.Add(sv.StyleId))
                {
                    var vsw = (CivilDb.Styles.SectionViewStyle)tr.GetObject(sv.StyleId, OpenMode.ForWrite);
                    nProp += ScaleView(vsw, k); nView++;
                    if (vsName != vWant) { MakeRoom(vWant, sv.StyleId); try { vsw.Name = vWant; mine[vWant] = sv.StyleId; } catch { } }
                }
            }
            else
            {
                if (!viewMap.TryGetValue(sv.StyleId, out var vc))
                {
                    MakeRoom(vWant, ObjectId.Null);
                    vc = vs0.CopyAsSibling(vWant);
                    if (vc.IsNull) throw new InvalidOperationException($"뷰 스타일 복사본 «{vWant}»을 못 만듦(같은 이름?)");
                    mine[vWant] = vc;
                    nProp += ScaleView((CivilDb.Styles.SectionViewStyle)tr.GetObject(vc, OpenMode.ForWrite), k); nView++;
                    viewMap[sv.StyleId] = vc; doneStyle.Add(vc);
                }
                sv.StyleId = vc;
            }
            foreach (bool bottom in new[] { true, false })
            {
                using var items = bottom ? sv.Bands.GetBottomBandItems() : sv.Bands.GetTopBandItems();
                if (items == null || items.Count == 0) continue;
                for (int i = 0; i < items.Count; i++)
                {
                    var it = items[i];
                    var sid = it.BandStyleId;
                    if (sid.IsNull) continue;
                    if (!bandMap.TryGetValue(sid, out var cid))
                    {
                        var src = (CivilDb.Styles.StyleBase)tr.GetObject(sid, OpenMode.ForRead);
                        string want = RoadXsecRules.BaseName(src.Name) + suffix;
                        if (Mine(src.Name, tag)) replaced.Add(sid);
                        MakeRoom(want, ObjectId.Null);
                        cid = src.CopyAsSibling(want);
                        if (cid.IsNull) throw new InvalidOperationException($"밴드 스타일 복사본 «{want}»을 못 만듦(같은 이름?)");
                        mine[want] = cid;
                        var cp = tr.GetObject(cid, OpenMode.ForWrite);
                        nProp += ProfileScaleStyles.ScaleProps(cp, ProfileScaleStyles.BandProps, k);
                        foreach (var pi in cp.GetType().GetProperties().Where(p => p.PropertyType == typeof(ObjectId) && p.Name.EndsWith("LabelStyleId", StringComparison.Ordinal)))
                        {
                            try
                            {
                                var lid = (ObjectId)pi.GetValue(cp)!;
                                var lsrc = (ObjectId)pi.GetValue(src)!;
                                if (!lid.IsNull && lid == lsrc) { log.AppendLine($"   ⚠밴드 «{src.Name}» {pi.Name} 라벨이 원본과 같은 객체(얕은 복사) — 그 라벨은 안 키움"); continue; }
                                nLabel += ProfileScaleStyles.ScaleLabel(tr, lid, k, doneLabel);
                            }
                            catch { }
                        }
                        bandMap[sid] = cid; nBand++;
                    }
                    it.BandStyleId = cid;
                    try { it.Gap *= k; } catch { }
                }
                if (bottom) sv.Bands.SetBottomBandItems(items); else sv.Bands.SetTopBandItems(items);
            }
        }
        tr.Commit();
        // 갈아 낀 지난 판 이 도로 밴드 복사본 — 이 도로 뷰들만 쓰던 것이라 이제 아무도 안 씀(이름부터 비킨 뒤 지움 — 지운 스타일이 이름을 쥐면 다음 CopyAsSibling이 빈 Id)
        if (replaced.Count > 0)
            try
            {
                using var trE = db.TransactionManager.StartTransaction();
                int nE = 0, q = 0;
                foreach (var id in replaced)
                    try
                    {
                        var sb = (CivilDb.Styles.StyleBase)trE.GetObject(id, OpenMode.ForWrite);
                        try { sb.Name = (sb.Name ?? "").Replace(" #" + tag + ")", $" #{tag} 옛{DateTime.Now:MMddHHmmss}_{++q})"); } catch { }
                        sb.Erase(); nE++;
                    }
                    catch { }
                trE.Commit();
                log.AppendLine($"   갈아 낀 지난 판 밴드 복사본 지움 {nE}/{replaced.Count}");
            }
            catch (Exception ex) { log.AppendLine("   지난 판 밴드 복사본 지우기 실패 — " + ex.Message); }
        return $"주석 1:{a0:0} → 1:{a1:0} · ×{k:0.###} — 뷰 스타일 {nView} · 밴드 복사본 {nBand} · 크기 칸 {nProp} · 라벨 칸 {nLabel}";
    }

    /// <summary>횡단 뷰 스타일의 축 눈금 · 축 제목 · 그래프 제목 · 격자 오프셋(종이 값)을 k배 — 축 · 눈금 객체 이름이 판마다 다를 수 있어 이름으로 읽는다(없으면 건너뜀).</summary>
    static int ScaleView(CivilDb.Styles.SectionViewStyle vs, double k)
    {
        int c = 0;
        foreach (var axName in new[] { "CenterAxis", "LeftAxis", "RightAxis", "TopAxis", "BottomAxis" })
        {
            object? ax = Get(vs, axName);
            if (ax == null) continue;
            foreach (var tn in new[] { "MajorTickStyle", "MinorTickStyle", "HorizontalGeometryTickStyle" })
                c += ScaleChild(ax, tn, ProfileScaleStyles.TickProps, k);
            c += ScaleChild(ax, "TitleStyle", ProfileScaleStyles.TitleProps, k);
            (ax as IDisposable)?.Dispose();
        }
        c += ScaleChild(vs, "GridStyle", ProfileScaleStyles.GridProps, k);
        object? gr = Get(vs, "GraphStyle");
        if (gr != null) { c += ScaleChild(gr, "TitleStyle", ProfileScaleStyles.TitleProps, k); (gr as IDisposable)?.Dispose(); }
        return c;
    }

    static object? Get(object o, string prop)
    {
        try { return o.GetType().GetProperty(prop)?.GetValue(o); } catch { return null; }
    }

    static int ScaleChild(object o, string prop, HashSet<string> names, double k)
    {
        object? ch = Get(o, prop);
        if (ch == null) return 0;
        try { return ProfileScaleStyles.ScaleProps(ch, names, k); }
        finally { (ch as IDisposable)?.Dispose(); }
    }
}
