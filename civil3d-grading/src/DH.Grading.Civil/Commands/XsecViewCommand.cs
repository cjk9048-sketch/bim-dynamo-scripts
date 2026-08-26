using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;
using CivilApp = Autodesk.Civil.ApplicationServices;
using CivilDb = Autodesk.Civil.DatabaseServices;

namespace DH.Grading.Civil.Commands;

/// <summary>★★[JACK 0826] <b>[횡단도] — 종단도가 정한 측점대로 횡단면도를 늘어놓는다.</b>
///
/// <para>JACK: <i>"단면검토선 버튼은 이제 사용성이 없으니까 없애고 종단·횡단 버튼도 없애.
/// 대신 횡단도라고 만들고, 횡단도를 누르면 종단도처럼 원하는 곳 클릭하면 그려지게 만들어.
/// 레이아웃하고 축척은 차후 고민하는 걸로 하고, <b>횡단용 단면검토선 전후가 잘 작동하는지
/// 확인하기 위해</b> 횡단도 기능 초안을 만들어."</i></para>
///
/// <para><b>왜 여기서 (전)(후) 검토선을 만드는가.</b> 종단도가 미리 만들어 두면 그 선이
/// <b>종단도에도 세로선으로 나타난다</b>(JACK 스샷: 벽 하나에 선 셋). 레이어·<c>Visible</c>·스타일
/// 셋 다 숨기기가 안 먹었다. 그래서 <b>쓸 때 만든다</b> — 종단도는 중심 하나만 보고,
/// 횡단은 여기서 (전)(후)를 얻는다.</para>
///
/// <para><b>초안이다.</b> 배치는 가로로 늘어놓고 줄바꿈만 한다 — 축척·도곽·레이아웃은 아직이다.
/// 지금 목적은 <b>(전)(후)가 실제로 두 장을 만들어 내는지</b> 눈으로 보는 것이다.</para></summary>
public sealed class XsecViewCommand
{
    /// <summary>한 줄에 몇 장. 초안이라 고정 — 배치는 나중에 도곽과 함께 정한다.</summary>
    private const int PerRow = 5;

    /// <summary>★★[JACK 0826] (전)(후)를 <b>벽 밖으로</b> 얼마나 더 미는가.
    ///
    /// <para>JACK: <i>"횡단에서 전후 단면이 안 생겨. 가시설을 봤는데 전후가 안 생겨."</i>
    /// 실제로는 두 장이 만들어졌는데 <b>내용이 같았다</b> — (전)(후) 간격이 <b>벽 두께뿐</b>이라
    /// (실측 3~5cm) 그 사이 지표면이 사실상 같기 때문이다. 구배를 0.01로 낮추며 벽이 다섯 배
    /// 얇아진 것이 여기서 드러났다.</para>
    ///
    /// <para>JACK 선택: <b>법면 밖까지</b>. 벽 두께의 몇 배쯤 나가야 한쪽은 벽이 온전히 보이고
    /// 한쪽은 안 보이는 그림이 된다. 벽이 아주 얇을 때를 대비해 <b>최소값</b>도 둔다.</para></summary>
    private const double OutFactor = 3.0;    // 벽 두께의 몇 배
    private const double OutMin = 0.20;      // 최소 20cm — 얇은 벽에서도 두 장이 달라지게

    [CommandMethod("DHXVIEW")]
    public void Run()
    {
        var doc = AcadApp.DocumentManager.MdiActiveDocument;
        if (doc == null) return;
        Editor ed = doc.Editor;
        Database db = doc.Database;
        var cdoc = CivilApp.CivilApplication.ActiveDocument;
        var log = new System.Text.StringBuilder();
        log.AppendLine($"\n[횡단도] {System.DateTime.Now:yyyy-MM-dd HH:mm:ss}  [DH.Grading {GradingSettings.Version}]");

        // ── ① 선형 — 종단도가 만든 것.
        ObjectId alignId = ObjectId.Null; string alignName = "";
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            foreach (ObjectId aid in cdoc.GetAlignmentIds())
            {
                if (tr.GetObject(aid, OpenMode.ForRead) is not CivilDb.Alignment al) continue;
                if (!al.Name.StartsWith(SectionCommand.AlignBase)) continue;
                alignId = aid; alignName = al.Name;   // 여럿이면 마지막 것(종단도와 같은 규칙)
            }
            tr.Commit();
        }
        catch (System.Exception ex) { log.AppendLine("  선형 찾기 실패 — " + ex.Message); }

        if (alignId.IsNull)
        {
            ed.WriteMessage("\n[횡단도] 노선이 없습니다 — [종단도]를 먼저 돌리세요.");
            Flush(log); return;
        }

        // ── ② 측점 — 종단도와 <b>같은 자</b>를 쓴다. 여기서 다시 계산하지 않는다.
        //   종단도가 방금 돌았으면 그 목록과 벽 자리가 그대로 남아 있다.
        var spans = ProfileCommand.LastWallSpans ?? new List<StationMarks.WallSpan>();
        var made = ProfileCommand.LastSampleLinesPublic;
        if (made == null || made.Count == 0)
        {
            ed.WriteMessage("\n[횡단도] 이 세션에서 [종단도]를 먼저 돌려 주세요 — 측점 목록이 필요합니다.");
            Flush(log); return;
        }
        log.AppendLine($"  노선 '{alignName}' · 측점 {made.Count}개 · 벽 자리 {spans.Count}곳");

        // ── ③ 놓을 자리.
        var pr = ed.GetPoint("\n[횡단도] 횡단면도를 놓을 왼쪽 아래 자리를 클릭 (Esc=취소): ");
        if (pr.Status != PromptStatus.OK) { ed.WriteMessage("\n[횡단도] 취소."); Flush(log); return; }
        var at = pr.Value.TransformBy(ed.CurrentUserCoordinateSystem);

        // ── ④ 횡단용 검토선 그룹 — 벽 자리는 (전)(후) 둘.
        double wl = System.Math.Max(1.0, GradingSettings.XsecLeft);
        double wr = System.Math.Max(1.0, GradingSettings.XsecRight);
        var surfs = SectionCommand.FindSurfaces(db, cdoc);

        ObjectId groupId = ObjectId.Null;
        var slIds = new List<(ObjectId Id, string Name)>();
        try
        {
            string gname = SectionCommand.UniqueName(db, cdoc, SectionCommand.GroupBase + "_횡단");
            groupId = CivilDb.SampleLineGroup.Create(gname, alignId);
            if (groupId.IsNull) { ed.WriteMessage("\n[횡단도] 검토선 그룹을 못 만들었습니다."); Flush(log); return; }

            // 표본 지표면 — 우리 것만.
            try
            {
                using var trS = db.TransactionManager.StartTransaction();
                var g = (CivilDb.SampleLineGroup)trS.GetObject(groupId, OpenMode.ForWrite);
                foreach (CivilDb.SectionSource src in g.GetSectionSources())
                {
                    bool ours = surfs.Exists(x => x.SurfId == src.SourceId);
                    try { src.IsSampled = ours; } catch { }
                }
                trS.Commit();
            }
            catch { }

            int nPair = 0;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var al = (CivilDb.Alignment)tr.GetObject(alignId, OpenMode.ForRead);
                double s0 = al.StartingStation, s1 = al.EndingStation;
                const double eps = 0.001;

                foreach (var m in made)
                {
                    // ★[JACK 0826] 종단이 쓴 <b>같은 간격</b>으로 이름을 만든다 — 안 그러면 두 도면의 측점명이 어긋난다.
                    string label = StationMarks.Fmt(m.St, ProfileCommand.LastStationInterval);
                    var sp = spans.Find(w => System.Math.Abs(w.Mid - m.St) <= StationMarks.MergeTol);

                    // ★ 벽 자리면 (전)(후) 두 장. 아니면 한 장.
                    //   ★[JACK 0826] 벽 두께만큼만 띄우면 두 단면이 같아 보인다 — <b>법면 밖까지</b> 민다.
                    (double, string)[] jobs;
                    if (sp.Back > sp.Front)
                    {
                        double c = (sp.Front + sp.Back) / 2.0;
                        double outw = System.Math.Max((sp.Back - sp.Front) / 2.0 * OutFactor, OutMin);
                        jobs = new[] { (c - outw, "(전)"), (c + outw, "(후)") };
                        nPair++;
                        log.AppendLine($"    벽 {StationMarks.Fmt(c, ProfileCommand.LastStationInterval)}" +
                                       $" — 두께 {sp.Back - sp.Front:F3}m → (전){c - outw:F2} / (후){c + outw:F2} (밖으로 {outw:F2}m)");
                    }
                    else jobs = new[] { (m.St, "") };

                    foreach (var (st, tag) in jobs)
                    {
                        double stc = System.Math.Min(System.Math.Max(st, s0 + eps), s1 - eps);
                        if (!SectionCommand.TryCut(al, stc, wl, wr, out var cut)) continue;
                        try
                        {
                            var pts = new Point2dCollection { cut.Left, cut.Right };
                            var id = CivilDb.SampleLine.Create($"{gname}_{label}{tag}", groupId, pts);
                            if (!id.IsNull) slIds.Add((id, label + tag));
                        }
                        catch { }
                    }
                }
                tr.Commit();
            }
            log.AppendLine($"  횡단용 검토선 '{gname}' — {slIds.Count}개 (벽 {nPair}곳은 (전)(후) 두 장)");
        }
        catch (System.Exception ex)
        {
            log.AppendLine("  검토선 실패 — " + ex.Message);
            ed.WriteMessage("\n[횡단도] 검토선을 못 만들었습니다 — " + ex.Message);
            Flush(log); return;
        }

        // ── ⑤ 횡단면도 배치 — 초안: 가로로 늘어놓고 줄바꿈.
        //   간격은 검토선 폭에서 잡는다(좌우폭 + 여유). 축척·도곽은 나중에.
        // ★[JACK 0826 '횡단면도는 너무 겹쳐져서 보기가 힘들어'] 간격을 실제 크기에서 잡는다.
        //   가로는 좌우폭이 정하지만, <b>세로는 표고 범위</b>가 정한다 — 40m 고정이라 겹쳤다.
        //   원지반·계획면의 표고 폭에 여유를 더해 잡고, 못 재면 넉넉한 기본값으로 물러선다.
        double dx = (wl + wr) * 1.6 + 20.0;
        double dy = 120.0;
        try
        {
            using var trZ = db.TransactionManager.StartTransaction();
            double zLo = double.MaxValue, zHi = double.MinValue;
            foreach (var sf in surfs)
            {
                if (trZ.GetObject(sf.SurfId, OpenMode.ForRead) is not CivilDb.TinSurface ts) continue;
                try
                {
                    var mn = ts.GetGeneralProperties().MinimumElevation;
                    var mx = ts.GetGeneralProperties().MaximumElevation;
                    if (mn < zLo) zLo = mn;
                    if (mx > zHi) zHi = mx;
                }
                catch { }
            }
            trZ.Commit();
            if (zHi > zLo) dy = (zHi - zLo) * 2.2 + 40.0;   // 격자 + 밴드 + 여백 몫
        }
        catch { }
        int nView = 0; string firstErr = null;
        for (int i = 0; i < slIds.Count; i++)
        {
            double x = at.X + (i % PerRow) * dx;
            double y = at.Y + (i / PerRow) * dy;
            try
            {
                // 이름을 주는 오버로드를 쓴다 — 검토선 이름을 그대로 넘겨 (전)(후)가 제목에 남게.
                var vid = CivilDb.SectionView.Create(slIds[i].Name, slIds[i].Id, new Point3d(x, y, 0.0));
                if (!vid.IsNull) nView++;
            }
            catch (System.Exception ex) { firstErr ??= $"{slIds[i].Name} — {ex.Message}"; }
        }

        log.AppendLine($"  횡단면도 {nView}/{slIds.Count}장 배치 · 간격 가로 {dx:F1}m · 세로 {dy:F1}m · 한 줄 {PerRow}장" +
                       (firstErr != null ? $"\n  ⚠첫 실패: {firstErr}" : ""));
        ed.WriteMessage($"\n[횡단도] 횡단면도 {nView}장 · 검토선 {slIds.Count}개" +
                        $"\n  자세한 내용: {DiagLog.FilePath}");
        Flush(log);
    }

    private static void Flush(System.Text.StringBuilder log)
    {
        try { DiagLog.Append("\n" + log.ToString()); } catch { }
    }
}
