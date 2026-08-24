using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using DH.Grading.Core;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;
using CivilApp = Autodesk.Civil.ApplicationServices;
using CivilDb = Autodesk.Civil.DatabaseServices;

namespace DH.Grading.Civil.Commands;

/// <summary>★[JACK 0810] <b>[측점추가] — 밸브실처럼 원하는 자리에 측점을 더한다.</b>
///
/// <para>JACK 요구②: "수동으로 밸브실이나 밸브같이 내가 원하는 위치의 체인을 추가할 수 있기를 원해."
/// 그리고 방식은 <b>"별도 명령으로 아무 때나 추가·삭제"</b>를 고르셨다 — 종단도를 만든 뒤에
/// 밸브실 위치가 바뀌어도 노선을 다시 그릴 필요가 없어야 하기 때문이다.</para>
///
/// <para><b>측점은 눈에 보이지 않는다</b>(JACK: "숨겨줘"). 도면에 마커를 그리면 남의 도면을
/// 어지럽히고 실수로 지워진다. 대신 선형에 딸린 보이지 않는 자리에 적어 둔다. 그 대가로
/// <b>목록을 반드시 찍어 준다</b> — 보이지 않는 것을 관리하려면 목록이 유일한 창이다.</para>
///
/// <para>자동으로 잡히는 것(노선 꺾임점·계획면 구배변화점)은 여기서 건드리지 않는다.
/// 그건 노선과 종단에서 <b>매번 다시 계산</b>되므로 저장할 필요가 없고, 저장하면 오히려
/// 노선을 고쳤을 때 옛 값이 남는다. 이 명령이 다루는 것은 <b>수동으로 더한 것만</b>이다.</para></summary>
public static class StationCommand
{
    [CommandMethod("DHSTATION", CommandFlags.Modal)]
    public static void Run()
    {
        var doc = AcadApp.DocumentManager.MdiActiveDocument;
        if (doc == null) return;
        var db = doc.Database;
        Editor ed = doc.Editor;

        ObjectId alignId = PickAlignment(db, ed);
        if (alignId.IsNull) return;

        // ★★★[v32.35 · JACK 0813] <b>버튼을 누르면 곧바로 찍을 수 있어야 한다.</b>
        //
        //   JACK: <i>"측점 버튼을 누르고 종단의 그래프 내에서 클릭하면 해당 위치의 단면검토선이 추가되고
        //   자동으로 종단뷰가 재작성되어야 해. 그래서 측점이 추가되게 해야 해."</i>
        //
        //   <b>종전엔 메뉴가 한 겹 앞에 있었다</b> — 버튼 → 목록 → "추가" 고르기 → 그제야 클릭.
        //   가장 자주 하는 일(찍기)에 매번 한 번을 더 물었다.
        //   → <b>점 찍기를 기본으로 두고, 나머지는 키워드로 받는다.</b>
        //     <see cref="PromptPointOptions"/>는 점과 키워드를 <b>동시에</b> 받으므로 메뉴를 없앨 수 있다.
        //
        //   ※ 목록은 처음 한 번만 보여 준다 — 찍을 때마다 쏟아지면 명령창이 목록으로 덮여
        //     정작 방금 무엇이 추가됐는지가 안 보인다.
        ShowList(db, ed, alignId);
        ed.WriteMessage("\n  ※ 측점을 찍으면 단면검토선이 생기고 종단도가 그 자리에서 다시 그려집니다.");

        while (true)
        {
            var ppo = new PromptPointOptions(
                "\n[측점] 종단도(또는 노선)에서 추가할 자리를 클릭 [목록(L)/삭제(D)/전체삭제(A)/끝(X)]: ")
            { AllowNone = true };
            ppo.Keywords.Add("목록", "L", "목록(L)", true, true);
            ppo.Keywords.Add("삭제", "D", "삭제(D)", true, true);
            ppo.Keywords.Add("전체삭제", "A", "전체삭제(A)", true, true);
            ppo.Keywords.Add("끝", "X", "끝(X)", true, true);

            var pp = ed.GetPoint(ppo);

            // Esc·Enter는 끝낸다 — 찍기가 기본이므로 '그만 찍겠다'는 뜻이다.
            if (pp.Status == PromptStatus.Cancel || pp.Status == PromptStatus.None) return;

            bool changed = false;
            if (pp.Status == PromptStatus.Keyword)
            {
                switch (pp.StringResult)
                {
                    case "끝": return;
                    case "목록": ShowList(db, ed, alignId); continue;
                    case "삭제": changed = DeleteOne(db, ed, alignId); break;
                    case "전체삭제": changed = DeleteAll(db, ed, alignId); break;
                    default: continue;
                }
            }
            else if (pp.Status == PromptStatus.OK)
            {
                changed = AddAt(db, ed, alignId, pp.Value.TransformBy(ed.CurrentUserCoordinateSystem));
            }
            // ★[검토 반영] <b>남은 상태(Error 등)는 끝낸다 — <c>continue</c>면 무한 루프다.</b>
            //   <c>GetPoint</c>가 <c>Error</c>를 돌려주는 상황(도면이 닫히는 중·스크립트 입력 고갈)에서는
            //   <b>사람 입력을 기다리지 않고</b> 즉시 돌아오므로 CPU를 물고 돌아 AutoCAD가 얼어붙는다.
            //   종전 코드는 non-OK면 무조건 빠져나갔다 — 이번 판이 만든 회귀다.
            else return;

            // ── 바뀌었으면 <b>그 자리에서</b> 종단도를 다시 그린다.
            //   단면검토선은 종단도가 측점 목록대로 만들므로(<c>ProfileCommand.BuildSampleLines</c>),
            //   <b>따로 부를 것이 없다</b> — 다시 그리는 것이 곧 단면검토선 갱신이다.
            if (!changed) continue;

            RedrawProfile(doc, ed);

            // ★★★[v32.35] <b>다시 그리면 선형이 새것으로 바뀐다 — 들고 있던 ID는 죽는다.</b>
            //   <see cref="ProfileCommand"/>는 옛 선형을 지우고 같은 좌표로 <b>새로 만든다</b>
            //   (선형 생성 API가 폴리선을 소모하는 구조라 '고쳐 쓰기'가 안 된다).
            //   그래서 다음 클릭에서 <b>지워진 선형</b>에 측점을 저장하려 들면 조용히 실패한다 —
            //   찍었는데 아무 일도 안 일어나는, 가장 알아채기 어려운 증상이다.
            alignId = Reacquire(db, alignId);
            if (alignId.IsNull)
            {
                ed.WriteMessage("\n  · 노선을 다시 잡지 못했습니다 — [측점]을 다시 실행해 주세요.");
                return;
            }
        }
    }

    /// <summary>다시 그린 뒤의 노선을 잡는다 — 옛 ID가 아직 살아 있으면 그대로 쓴다.
    /// <para>여럿이면 <b>마지막 것</b>을 쓴다(<see cref="ProfileCommand"/>가 방금 만든 것이 마지막이다).</para></summary>
    private static ObjectId Reacquire(Database db, ObjectId old)
    {
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            if (!old.IsNull && !old.IsErased &&
                tr.GetObject(old, OpenMode.ForRead, false, true) is CivilDb.Alignment { IsErased: false })
            { tr.Commit(); return old; }

            ObjectId last = ObjectId.Null;
            var cdoc = CivilApp.CivilApplication.ActiveDocument;
            foreach (ObjectId id in cdoc.GetAlignmentIds())
                if (tr.GetObject(id, OpenMode.ForRead) is CivilDb.Alignment al && !al.IsErased &&
                    al.Name.StartsWith(SectionCommand.AlignBase)) last = id;
            tr.Commit();
            return last;
        }
        catch { return ObjectId.Null; }
    }

    /// <summary>측점이 바뀐 뒤 종단도를 그 자리에서 다시 그린다 — <b>팝업 없이</b>.
    /// <para>종단도가 아직 없으면 조용히 넘어간다(측점만 쌓아 두고 나중에 [종단도]를 눌러도 된다).</para></summary>
    private static void RedrawProfile(Autodesk.AutoCAD.ApplicationServices.Document doc, Editor ed)
    {
        try
        {
            if (!ProfileCommand.Rebuild(doc))
                ed.WriteMessage("\n  · (종단도가 아직 없어 목록에만 담았습니다 — [종단도]를 누르면 반영됩니다)");
        }
        catch (System.Exception ex) { ed.WriteMessage("\n  · ⚠종단도 갱신 실패 — " + ex.Message); }
    }

    /// <summary>노선(선형)을 정한다 — 하나뿐이면 묻지 않는다.</summary>
    private static ObjectId PickAlignment(Database db, Editor ed)
    {
        var found = new List<(ObjectId Id, string Name)>();
        using (var tr = db.TransactionManager.StartTransaction())
        {
            try
            {
                var cdoc = CivilApp.CivilApplication.ActiveDocument;
                foreach (ObjectId id in cdoc.GetAlignmentIds())
                {
                    // ★★[v32.35 · 검토 반영] <b>이 애드인이 만든 노선만 고르게 한다.</b>
                    //   종전엔 도면의 모든 선형을 후보에 넣었는데, 뒤따르는 일들
                    //   (<c>HarvestMarks</c>·<c>EraseExisting</c>·<see cref="Reacquire"/>)은 전부
                    //   <c>DH</c> 접두사만 본다. 남의 노선을 고르면 측점은 그쪽에 저장되고
                    //   종단도는 DH 노선으로 다시 그려져 — <b>찍어도 아무 일도 안 일어난다.</b>
                    //   고를 수 없게 막는 것이 "왜 안 되지"를 없애는 가장 싼 방법이다.
                    if (tr.GetObject(id, OpenMode.ForRead) is CivilDb.Alignment al &&
                        al.Name.StartsWith(SectionCommand.AlignBase)) found.Add((id, al.Name));
                }
            }
            catch { }
            tr.Commit();
        }
        if (found.Count == 0)
        {
            SectionCommand.Refuse(ed, "도면에 노선(선형)이 없습니다.\n먼저 [종단도]로 노선을 그리세요.");
            return ObjectId.Null;
        }
        if (found.Count == 1)
        {
            ed.WriteMessage($"\n[측점] 노선 '{found[0].Name}'");
            return found[0].Id;
        }
        ed.WriteMessage($"\n[측점] 노선이 {found.Count}개입니다 — 화면에서 고르세요.");
        var peo = new PromptEntityOptions("\n[측점] 노선을 클릭 (Esc=취소): ");
        peo.SetRejectMessage("\n노선(선형)이 아닙니다.");
        peo.AddAllowedClass(typeof(CivilDb.Alignment), true);
        var per = ed.GetEntity(peo);
        return per.Status == PromptStatus.OK ? per.ObjectId : ObjectId.Null;
    }

    /// <summary>지금 측점이 어떻게 잡히는지 통째로 보여 준다 — 보이지 않는 것을 보는 유일한 창.</summary>
    private static void ShowList(Database db, Editor ed, ObjectId alignId)
    {
        using var tr = db.TransactionManager.StartTransaction();
        var manual = StationMarks.Load(tr, alignId);
        var auto = AutoMarks(tr, alignId, out string autoNote);
        double idx = System.Math.Max(1.0, GradingSettings.XsecInterval);
        ed.WriteMessage($"\n─ 수동 측점 {manual.Count}개 · 자동 {auto.Count}개({autoNote}) ─");
        foreach (var m in manual.OrderBy(m => m.Station))
            ed.WriteMessage($"\n   [수동] {StationMarks.Fmt(m.Station, idx),-14} {m.Why}");
        foreach (var m in auto.OrderBy(m => m.Station))
            ed.WriteMessage($"\n   [자동] {StationMarks.Fmt(m.Station, idx),-14} {m.Why}");
        if (manual.Count + auto.Count == 0) ed.WriteMessage("\n   (아직 없음)");
        tr.Commit();
    }

    /// <summary>자동으로 잡히는 측점 — 노선 꺾임점 + 계획 종단의 구배변화점.
    /// 저장하지 않고 <b>볼 때마다 다시 계산</b>한다. 노선이나 종단을 고치면 자동으로 따라간다.</summary>
    internal static List<StationMarks.Mark> AutoMarks(Transaction tr, ObjectId alignId, out string note)
    {
        var list = new List<StationMarks.Mark>();
        note = "";
        try
        {
            if (tr.GetObject(alignId, OpenMode.ForRead) is not CivilDb.Alignment al) return list;

            // ① 수평 꺾임점 — 선형의 접선-접선 교점. 노선을 직접 그렸다면 곧 이형관 자리다.
            int nPi = 0;
            try
            {
                foreach (CivilDb.AlignmentEntity e in al.Entities)
                {
                    if (e is not CivilDb.AlignmentLine ln) continue;
                    double st = 0, off = 0;
                    al.StationOffset(ln.EndPoint.X, ln.EndPoint.Y, ref st, ref off);
                    if (st > al.StartingStation + 1e-6 && st < al.EndingStation - 1e-6)
                    { list.Add(new StationMarks.Mark(st, "꺾임점")); nPi++; }
                }
            }
            catch { }

            // ── ② ★★[v30.3 · JACK 0812] <b>'굴곡부'는 버린 개념이다 — 여기도 같은 자를 쓴다.</b>
            //
            //   JACK: <i>"우리 굴곡부라는 개념은 아예 버리기로 하지 않았어?"</i> — 맞다.
            //   종단의 PVI를 훑어 '많이 꺾인 것'을 고르던 방식(<c>FromProfileGradeBreaks</c>)은
            //   <b>지표면 표본점과 설계 변화를 구분하지 못해</b> 폐기했다(62m 노선에 78개가 잡혔다).
            //   그런데 이 명령만 옛 방식을 계속 쓰고 있었다 —
            //   같은 노선인데 <b>[측점 목록]과 실제 도면의 단면검토선이 달랐다.</b>
            //
            //   → 종단도와 <b>똑같이</b> 번들에서 복원한 선(데이라잇·소단·사면)과의 교차로 잡는다.
            //     자가 하나가 되어야 두 화면이 같은 말을 한다.
            int nEdge = 0, nDl = 0;
            try
            {
                var regions = GradingBundleStore.TryLoadAll(alignId.Database, tr, out _);
                if (regions != null && regions.Count > 0)
                {
                    var edges = NoriCommand.RebuildEdgeLines(regions, out string rdiag);
                    // ★[JACK 0824] 측점이 왜 그만큼인지 로그로 남긴다 — 종전엔 log에 null을 넘겨
                    //   '측점이 안 잡힌다'는 보고가 와도 재료가 없는 건지 교차가 없는 건지 못 갈랐다.
                    var slog = new System.Text.StringBuilder();
                    slog.AppendLine($"■ 측점 재료 — {rdiag}");
                    var pts = new System.Collections.Generic.List<System.Collections.Generic.List<Point3d>>(edges.Count);
                    foreach (var e in edges)
                    {
                        var q = new System.Collections.Generic.List<Point3d>(e.Count);
                        foreach (var p in e) q.Add(new Point3d(p.X, p.Y, p.Z));
                        pts.Add(q);
                    }
                    var em = StationMarks.FromLines(al, pts, "사면·소단·옹벽", null, slog);
                    list.AddRange(em); nEdge = em.Count;

                    var dl = new System.Collections.Generic.List<System.Collections.Generic.List<Point3d>>();
                    for (int ri = 0; ri < regions.Count; ri++)
                    {
                        var later = GradingBundle.LaterFootprints(regions, ri);
                        var mask = GradingPolygons.RegionMask.Build(later);
                        var b = regions[ri];
                        foreach (var r in new[] { b.CutFinalRings, b.FillFinalRings }
                                          .Where(x => x != null).SelectMany(x => x)
                                          .Concat(new[] { b.CutFinalRing, b.FillFinalRing }.Where(x => x != null)))
                        {
                            var q = new System.Collections.Generic.List<Point3d>();
                            foreach (var p in r)
                            {
                                if (mask != null && mask.Contains(p.X, p.Y))
                                { if (q.Count >= 2) dl.Add(q); q = new System.Collections.Generic.List<Point3d>(); continue; }
                                q.Add(new Point3d(p.X, p.Y, p.Z));
                            }
                            if (q.Count >= 2) dl.Add(q);
                        }
                    }
                    var dm = StationMarks.FromLines(al, dl, "데이라잇", null, slog);
                    try { DiagLog.Append("\n" + slog.ToString()); } catch { }
                    list.AddRange(dm); nDl = dm.Count;
                }
            }
            catch (System.Exception ex)
            {
                // ★[JACK 0824] 종전엔 **조용히 삼켰다** — 여기서 터지면 측점이 0개인데 이유를 알 길이 없었다.
                try { DiagLog.Append($"\n■ 측점 재료 예외 — {ex.GetType().Name}: {ex.Message}"); } catch { }
            }
            note = $"꺾임 {nPi} · 사면·소단·옹벽 {nEdge} · 데이라잇 {nDl}"
                 + (nEdge + nDl == 0 ? " (번들이 없어 정지 경계는 못 잡음 — [부지정지]를 먼저)" : "");
        }
        catch { }
        return list;
    }

    /// <summary>★[JACK 0810] <b>종단도 위를 클릭해도 측점이 잡히게 한다.</b>
    ///
    /// <para>JACK: "종단상에서 내가 클릭을 하면 그 부분에 자동으로 종단 체인이 추가되고 밴드가 업데이트되며,
    /// 나중에 그 부분의 횡단도를 만들 때 추가되어야 하는 로직이야."
    /// 그리고 그 앞 단계로 <b>Revit 구조물을 종단도에 투영</b>해 놓고 그 끝선을 보고 찍는다 —
    /// 일반적인 2D 설계법이다. 그러니 <b>클릭 대상은 평면 노선이 아니라 종단도</b>여야 한다.</para>
    ///
    /// <para>클릭한 점이 어느 종단도 안이면 <c>FindStationAndElevationAtXY</c>로 측점을 읽고,
    /// 아니면 평면 노선에서 읽는다.</para>
    ///
    /// <para>★[v23.17 검토 반영] <b>조용한 폴백이 가장 위험하다.</b> 종단도는 모형공간 아무 데나 놓이는데
    /// 그 좌표를 평면 노선 좌표로 그대로 해석하면 <b>완전히 엉뚱한 측점</b>이 나오고,
    /// 그런데도 "노선에서 읽었습니다"라고 보고된다. 사용자가 종단도의 격자 <b>바깥</b>(밴드 표·여백)을
    /// 찍는 일은 흔하다. → <b>종단도 근처를 찍었으면 평면으로 내려가지 않고 거절한다.</b></para>
    ///
    /// <para>모형공간 전수 순회도 걷어냈다 — <c>Alignment.GetProfileViewIds()</c>가 있다.
    /// 이 노선의 종단도만 열게 되어 빠르고, 남의 종단도를 잘못 집을 일도 없다.</para>
    /// 반환=측점(못 잡으면 null).</summary>
    private static double? StationFromPick(Transaction tr, Editor ed, Point3d wcs,
                                            CivilDb.Alignment al, out string via)
    {
        via = "?";
        int nView = 0, nNear = 0;
        try
        {
            foreach (ObjectId id in al.GetProfileViewIds())
            {
                if (tr.GetObject(id, OpenMode.ForRead) is not CivilDb.ProfileView pv) continue;
                nView++;
                // 이 종단도의 상자 안을 찍었는가 — 안인데 격자 밖이면 '평면'으로 내려가면 안 된다.
                bool inside = false;
                try
                {
                    var e = pv.GeometricExtents;
                    inside = wcs.X >= e.MinPoint.X - 1e-6 && wcs.X <= e.MaxPoint.X + 1e-6 &&
                             wcs.Y >= e.MinPoint.Y - 1e-6 && wcs.Y <= e.MaxPoint.Y + 1e-6;
                }
                catch (System.Exception ex)
                { ed.WriteMessage($"\n  · 종단도 '{pv.Name}' 상자 읽기 실패 — {ex.Message}"); }
                if (inside) nNear++;

                double st = 0, el = 0;
                bool ok;
                try { ok = pv.FindStationAndElevationAtXY(wcs.X, wcs.Y, ref st, ref el); }
                catch (System.Exception ex)
                { ed.WriteMessage($"\n  · 종단도 '{pv.Name}' 좌표 변환 실패 — {ex.Message}"); continue; }
                if (!ok) continue;
                if (st < al.StartingStation - 1e-6 || st > al.EndingStation + 1e-6)
                {
                    ed.WriteMessage($"\n  · 종단도 '{pv.Name}': 측점 {st:F2}m가 노선 범위" +
                                    $"({al.StartingStation:F2}~{al.EndingStation:F2}m) 밖입니다.");
                    continue;
                }
                via = $"종단도 '{pv.Name}'(표고 {el:F2}m)";
                return st;
            }
        }
        catch (System.Exception ex)
        { ed.WriteMessage($"\n  · 종단도 훑기 실패 — {ex.Message}"); }

        if (nNear > 0)
        {
            // 종단도 안을 찍었는데 못 읽었다 → 격자 밖이다. 평면으로 내려가면 엉뚱한 측점이 나온다.
            ed.WriteMessage($"\n  · 종단도 안({nNear}개 중)을 찍었지만 <b>격자 밖</b>입니다 — 그래프 안을 찍어 주세요."
                            .Replace("<b>", "").Replace("</b>", ""));
            via = "거절(격자 밖)";
            return null;
        }
        var plane = StationMarks.StationOf(al, wcs);
        via = plane.HasValue ? $"평면 노선(종단도 {nView}개는 안 걸림)" : "실패";
        return plane;
    }

    /// <summary>★[JACK 0810] <b>측점 자리에 PVI를 심어 밴드가 저절로 갱신되게 한다.</b>
    /// <para>밴드에 값을 억지로 밀어 넣는 게 아니라, <b>종단에 "여기 체인이 있다"고 등록</b>하면
    /// Civil이 굴곡부로 인식해 알아서 찍는다. 심는 표고는 <b>그 자리의 현재 표고</b>라
    /// 기존 선 위의 점이 된다 — <b>종단 모양은 바뀌지 않는다</b>('꺾임 없는 꺾임점').</para>
    ///
    /// <para>★[v23.17 검토 반영] <b>세 가지가 조용히 실패할 수 있다.</b></para>
    /// <list type="number">
    /// <item><b>동적 종단</b> — 정지면은 <c>CreateFromSurface</c>로 만든 <b>지표면의 거울</b>이다.
    ///   거울에 그림을 그려도 원본은 안 바뀌고, 원본이 움직이면 그림이 사라진다.
    ///   <c>UpdateMode</c>가 <c>Dynamic</c>이면 <b>심기 전에 막고 사유를 남긴다</b> —
    ///   Static으로 돌리면 지표면 연동이 <b>영구히</b> 끊기므로 조용히 할 결정이 아니다.</item>
    /// <item><b>중복</b> — 측점 목록에는 중복 검사가 있는데 여기엔 없어 같은 자리에 PVI가 쌓였다.</item>
    /// <item><b>되읽기 없음</b> — 넣었다고 세면 로그가 거짓말을 한다. 이 저장소의 규율대로
    ///   <b>개수를 전후로 재서</b> 실제로 늘었는지 확인한다.</item>
    /// </list>
    /// 반환=심었으면 true.</summary>
    private static bool PlantPvi(Transaction tr, CivilDb.Alignment al, double station, Editor ed)
    {
        // ★ 계획 종단 고르는 규칙을 AutoMarks와 <b>같게</b> 맞춘다(마지막 일치).
        //   서로 다르면 '구배변화를 읽는 종단'과 'PVI를 심는 종단'이 갈려 이 기능의 대전제가 깨진다.
        ObjectId padId = ObjectId.Null; int nPad = 0;
        try
        {
            foreach (ObjectId pid in al.GetProfileIds())
                if (tr.GetObject(pid, OpenMode.ForRead) is CivilDb.Profile p &&
                    (p.Name.Contains("정지") || p.Name.Contains("계획")))
                { padId = pid; nPad++; }
        }
        catch (System.Exception ex) { ed.WriteMessage($"\n  · 계획 종단 찾기 실패 — {ex.Message}"); return false; }

        if (padId.IsNull)
        { ed.WriteMessage("\n  · 계획 종단을 찾지 못해 PVI를 심지 못했습니다(측점 목록에는 남았습니다)."); return false; }
        if (nPad > 1) ed.WriteMessage($"\n  · ⚠계획 종단이 {nPad}개입니다 — 마지막 것을 씁니다.");

        try
        {
            if (tr.GetObject(padId, OpenMode.ForWrite) is not CivilDb.Profile pr) return false;

            // ── 게이트 ①: 동적 종단이면 심지 않는다.
            string mode = "?", ptype = "?";
            try { mode = pr.UpdateMode.ToString(); } catch { }
            try { ptype = pr.ProfileType.ToString(); } catch { }
            if (mode == nameof(CivilDb.ProfileUpdateType.Dynamic))
            {
                ed.WriteMessage(
                    $"\n  · ⚠'{pr.Name}'은 지표면에 연동된 <동적> 종단({ptype}/{mode})이라 PVI를 심을 수 없습니다."
                        .Replace("<", "").Replace(">", "") +
                    "\n    측점 목록에는 남았으니 횡단에는 반영됩니다. 밴드에 띄우려면 이 종단을 정적으로" +
                    "\n    바꿔야 하는데, 그러면 정지면 지표면과의 연결이 영구히 끊깁니다 — 지시 없이 하지 않습니다.");
                return false;
            }

            // ── 게이트 ②: 범위. 시·종점에 '딱 걸린' 것은 범위 안이다.
            if (station < pr.StartingStation - 1e-6 || station > pr.EndingStation + 1e-6)
            {
                ed.WriteMessage($"\n  · 측점 {station:F2}m가 계획 종단 범위" +
                                $"({pr.StartingStation:F2}~{pr.EndingStation:F2}m) 밖이라 PVI를 심지 못했습니다.");
                return false;
            }

            double el = pr.ElevationAt(station);

            // ── 게이트 ③: 이미 있으면 또 심지 않는다.
            try
            {
                if (pr.PVIs.GetPVIAt(station, el) != null)
                { ed.WriteMessage($"\n  · 그 자리에 이미 PVI가 있어 새로 심지 않았습니다 (표고 {el:F3}m)."); return true; }
            }
            catch { }   // 없으면 예외를 던지는 구현일 수 있다 — 없는 것으로 보고 진행

            int before = 0, after = 0;
            try { before = pr.PVIs.Count; } catch { }
            pr.PVIs.AddPVI(station, el);
            try { after = pr.PVIs.Count; } catch { }

            // ★ 넣었다고 세지 않는다 — 개수가 실제로 늘었는지 확인한다.
            if (after > before)
            {
                ed.WriteMessage($"\n  · '{pr.Name}'({ptype}/{mode})에 PVI 심음 — 표고 {el:F3}m · PVI {before}→{after}개");
                return true;
            }
            ed.WriteMessage($"\n  · ⚠PVI가 늘지 않았습니다 (PVI {before}→{after}개, {ptype}/{mode})." +
                            "\n    호출은 성공했는데 도면이 안 바뀌었습니다 — 측점 목록에만 남습니다.");
            return false;
        }
        catch (System.Exception ex)
        {
            ed.WriteMessage($"\n  · ⚠PVI를 심지 못했습니다 — {ex.GetType().Name}: {ex.Message}"
                          + "\n    (측점 목록에는 남았으니 횡단에는 반영됩니다. 밴드에는 안 뜹니다.)");
        }
        return false;
    }

    /// <summary>찍은 자리에 측점을 더한다. <b>참=목록이 바뀌었다</b>(부른 쪽이 종단도를 다시 그린다).
    /// <para>★[v32.35] 점을 <b>인자로 받는다</b> — 부르는 쪽이 이미 물어봤기 때문이다.
    /// 종전처럼 여기서 또 물으면 클릭을 두 번 하게 된다.</para></summary>
    private static bool AddAt(Database db, Editor ed, ObjectId alignId, Point3d wcs)
    {
        // ── ① 측점 읽기 (읽기 전용). ★[v23.17] 사람에게 이름을 묻기 <b>전에</b> 닫는다 —
        //   묻는 동안 트랜잭션을 열어 두면 그 사이 도면 전체가 잡혀 있다.
        double st; string via;
        using (var tr0 = db.TransactionManager.StartTransaction())
        {
            if (tr0.GetObject(alignId, OpenMode.ForRead) is not CivilDb.Alignment al0) { tr0.Commit(); return false; }
            var got = StationFromPick(tr0, ed, wcs, al0, out via);
            tr0.Commit();
            if (!got.HasValue)
            { ed.WriteMessage("\n  · 측점을 잡지 못했습니다 — 위 사유를 보세요."); return false; }
            st = got.Value;
        }
        ed.WriteMessage($"\n  · 측점을 {via}에서 읽었습니다.");
        double idx = System.Math.Max(1.0, GradingSettings.XsecInterval);

        // ★★★[v32.36 · JACK 0813] <b>이름을 묻지 않는다 — 찍는 순간 들어간다.</b>
        //   JACK: <i>"측점을 누르고 위치를 찍으면 무슨 밸브실이라고 뜨는데,
        //   이런 거 없이 측점 누르는 순간 바로 추가되게 해줘."</i>
        //
        //   <b>왜 묻고 있었나.</b> v23 때 이 명령은 <b>밸브실 자리를 적어 두는 것</b>이 목적이라
        //   이름이 곧 그 측점의 뜻이었다. 그런데 지금은 <b>단면검토선을 놓는 손</b>으로 쓰인다 —
        //   토공 종단도에서 측점을 더 촘촘히 두려는 것이지, 그 자리에 무엇이 있다는 뜻이 아니다.
        //   <b>쓰임이 바뀌었으면 묻는 것도 바뀌어야 한다.</b>
        //
        //   ※ 이름이 다시 필요해지면 <b>목록에서 고쳐 넣는 길</b>을 여는 편이 낫다 —
        //     찍는 손을 멈춰 세우지 않으면서도 이름을 남길 수 있다.
        const string why = "직접 찍음";

        // ── ② 측점 목록 저장 — 이건 <b>반드시 남아야 하는 것</b>이라 따로 커밋한다.
        bool saved;
        using (var tr1 = db.TransactionManager.StartTransaction())
        {
            var marks = StationMarks.Load(tr1, alignId);
            // 같은 자리에 이미 있으면 이름만 바꾼다 — 중복이 쌓이면 라벨이 겹친다.
            int hit = marks.FindIndex(m => System.Math.Abs(m.Station - st) <= StationMarks.MergeTol);
            if (hit >= 0) { marks[hit] = new StationMarks.Mark(st, why); ed.WriteMessage("\n  · 같은 자리에 있어 이름만 바꿨습니다."); }
            else marks.Add(new StationMarks.Mark(st, why));
            saved = StationMarks.Save(tr1, alignId, marks);
            if (saved) { tr1.Commit(); ed.WriteMessage($"\n  · 추가: {StationMarks.Fmt(st, idx)} '{why}'"); }
            else { tr1.Abort(); ed.WriteMessage("\n  · ⚠저장하지 못했습니다 — PVI도 심지 않습니다(도면과 목록이 어긋나면 안 됩니다)."); }
        }
        if (!saved) return false;

        // ── ③ PVI 심기 — <b>실패 가능성이 높은 쓰기</b>라 따로 연다.
        //   ★[v23.17] 종전엔 ②와 한 트랜잭션이라, 이게 반쯤 실패해도 되돌릴 수가 없었다
        //   (Abort하면 애써 저장한 측점 목록까지 날아간다).
        using (var tr2 = db.TransactionManager.StartTransaction())
        {
            if (tr2.GetObject(alignId, OpenMode.ForRead) is not CivilDb.Alignment al2) { tr2.Abort(); return true; }
            if (PlantPvi(tr2, al2, st, ed)) tr2.Commit();
            else tr2.Abort();          // 반쯤 바뀐 상태를 도면에 남기지 않는다
        }
        // 목록은 이미 저장됐다 — PVI가 실패해도 <b>참</b>이다. 다시 그리면 목록대로 다시 심는다.
        return true;
    }

    /// <summary>참=목록이 바뀌었다(부른 쪽이 종단도를 다시 그린다).</summary>
    private static bool DeleteOne(Database db, Editor ed, ObjectId alignId)
    {
        // ★[v32.35] 지울 때도 <b>종단도에서 찍을 수 있다</b> — 추가를 종단도에서 하니
        //   지우기만 평면 노선으로 가라고 하면 화면을 오갈 일이 생긴다.
        var pp = ed.GetPoint("\n[측점] 지울 측점 근처를 종단도(또는 노선)에서 클릭 (Esc=취소): ");
        if (pp.Status != PromptStatus.OK) return false;
        var wcs = pp.Value.TransformBy(ed.CurrentUserCoordinateSystem);

        using var tr = db.TransactionManager.StartTransaction();
        if (tr.GetObject(alignId, OpenMode.ForRead) is not CivilDb.Alignment al) { tr.Commit(); return false; }
        var st = StationFromPick(tr, ed, wcs, al, out _);
        if (!st.HasValue) { ed.WriteMessage("\n  · 측점을 잡지 못했습니다."); tr.Commit(); return false; }

        var marks = StationMarks.Load(tr, alignId);
        if (marks.Count == 0) { ed.WriteMessage("\n  · 수동 측점이 없습니다(자동 측점은 지울 수 없습니다)."); tr.Commit(); return false; }
        double idx = System.Math.Max(1.0, GradingSettings.XsecInterval);
        int best = 0;
        for (int i = 1; i < marks.Count; i++)
            if (System.Math.Abs(marks[i].Station - st.Value) < System.Math.Abs(marks[best].Station - st.Value)) best = i;
        var gone = marks[best];
        marks.RemoveAt(best);
        bool ok = StationMarks.Save(tr, alignId, marks);
        if (ok) ed.WriteMessage($"\n  · 지움: {StationMarks.Fmt(gone.Station, idx)} '{gone.Why}'");
        else ed.WriteMessage("\n  · ⚠저장하지 못했습니다.");
        tr.Commit();
        return ok;
    }

    /// <summary>참=목록이 바뀌었다.</summary>
    private static bool DeleteAll(Database db, Editor ed, ObjectId alignId)
    {
        // ★[v32.35 · 검토 반영] <b>묻기 전에 트랜잭션을 닫는다.</b> 같은 파일이 <see cref="AddAt"/>에서
        //   이미 배운 규칙인데(v23.17) 여기만 어기고 있었다 — 사람이 답할 때까지 도면 전체가 잡혀 있다.
        int n;
        using (var tr0 = db.TransactionManager.StartTransaction())
        { n = StationMarks.Load(tr0, alignId).Count; tr0.Commit(); }
        if (n == 0) { ed.WriteMessage("\n  · 지울 수동 측점이 없습니다."); return false; }

        var pko = new PromptKeywordOptions($"\n수동 측점 {n}개를 모두 지웁니다. 진행할까요")
        { AllowNone = true };
        pko.Keywords.Add("예"); pko.Keywords.Add("아니오");
        pko.Keywords.Default = "아니오";
        var pr = ed.GetKeywords(pko);
        if (pr.Status != PromptStatus.OK || pr.StringResult != "예") return false;

        // ★[검토 반영] <b>저장 결과를 그대로 돌려준다.</b> 종전엔 성공으로 단정해,
        //   저장이 실패해도 "지웠습니다"라고 말하고 전면 재작성까지 돌았다.
        bool ok;
        using (var tr = db.TransactionManager.StartTransaction())
        {
            ok = StationMarks.Save(tr, alignId, new List<StationMarks.Mark>());
            if (ok) tr.Commit(); else tr.Abort();
        }
        ed.WriteMessage(ok ? $"\n  · 수동 측점 {n}개를 지웠습니다."
                           : "\n  · ⚠지우지 못했습니다(저장 실패) — 목록은 그대로입니다.");
        return ok;
    }
}
