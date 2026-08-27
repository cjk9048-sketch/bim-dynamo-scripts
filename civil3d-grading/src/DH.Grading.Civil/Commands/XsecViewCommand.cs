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
    // ★[JACK 0826] 배치를 명령행에서 묻던 코드(Layout·Layouts·AskLayout·_lastLayout)는
    //   <b>걷어냈다</b> — JACK이 <i>"도면설정 팝업에 넣고, 횡단도 단추는 누르고 찍으면 바로"</i>로
    //   바꾸셨다. 목록은 이제 <see cref="GradingSettings.XsecLayoutLabels"/>가 들고 있다.

    /// <summary>★★[JACK 0826] (전)(후)를 <b>벽 밖으로</b> 얼마나 더 미는가.
    ///
    /// <para>JACK: <i>"횡단에서 전후 단면이 안 생겨. 가시설을 봤는데 전후가 안 생겨."</i>
    /// 실제로는 두 장이 만들어졌는데 <b>내용이 같았다</b> — (전)(후) 간격이 <b>벽 두께뿐</b>이라
    /// (실측 3~5cm) 그 사이 지표면이 사실상 같기 때문이다. 구배를 0.01로 낮추며 벽이 다섯 배
    /// 얇아진 것이 여기서 드러났다.</para>
    ///
    /// <para>JACK 선택: <b>법면 밖까지</b>. 벽 두께의 몇 배쯤 나가야 한쪽은 벽이 온전히 보이고
    /// 한쪽은 안 보이는 그림이 된다. 벽이 아주 얇을 때를 대비해 <b>최소값</b>도 둔다.</para></summary>

    /// <summary>횡단면도 이름을 쓰는 레이어 — 우리가 직접 그리므로 지울 때도 여기만 보면 된다.</summary>
    internal const string XsecTitleLayer = "DH-횡단-이름";

    /// <summary>★★[JACK 0826 "측점 넣기 기능을 쓰니까 횡단뷰만 사라져 버렸어 —
    /// 전체적으로 업데이트가 돼야 해"] <b>마지막으로 그린 자리를 기억한다.</b>
    /// <para>측점을 고치면 종단도가 다시 그려지고, 그때 검토선 그룹이 새로 생기면서
    /// <b>거기 매달린 횡단면도가 Civil에 의해 지워진다</b>. 자리를 알고 있으면
    /// 사용자에게 다시 묻지 않고 <b>같은 자리에 다시 그릴 수 있다</b>.</para></summary>
    internal static Point3d? LastAt;

    /// <summary>기억해 둔 자리에 <b>다시 그린다</b> — 측점을 고친 뒤 종단도가 부른다.
    /// 그린 적이 없으면(<see cref="LastAt"/>가 비었으면) 아무 일도 하지 않는다.</summary>
    internal static bool Refresh(Autodesk.AutoCAD.ApplicationServices.Document doc)
    {
        if (LastAt == null || doc == null) return false;
        try { Build(doc, LastAt); return true; }
        catch { return false; }
    }

    [CommandMethod("DHXVIEW")]
    public void Run() => Build(AcadApp.DocumentManager.MdiActiveDocument, null);

    /// <summary>본체 — <paramref name="at"/>가 있으면 자리를 <b>묻지 않는다</b>(다시 그리기).</summary>
    private static void Build(Autodesk.AutoCAD.ApplicationServices.Document doc, Point3d? at0)
    {
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
        // ★[JACK 0826 검토] 도면이 바뀌었으면 <b>옛 측점을 쓰지 않는다.</b>
        if (made != null && made.Count > 0 && !ProfileCommand.SameDrawing(db))
        {
            ed.WriteMessage("\n[횡단도] 이 측점 목록은 다른 도면의 것입니다 — 이 도면에서 [종단도]를 먼저 돌려 주세요.");
            log.AppendLine("  ⚠측점 목록이 다른 도면 것이라 쓰지 않았다 — 이 도면에서 [종단도]를 먼저 돌려야 한다");
            Flush(log); return;
        }
        if (made == null || made.Count == 0)
        {
            ed.WriteMessage("\n[횡단도] 이 세션에서 [종단도]를 먼저 돌려 주세요 — 측점 목록이 필요합니다.");
            Flush(log); return;
        }
        log.AppendLine($"  노선 '{alignName}' · 측점 {made.Count}개 · 벽 자리 {spans.Count}곳" +
                       $" · 측점명 간격 {ProfileCommand.LastStationInterval:0.#}m(종단과 같아야 한다)");

        // ── ③ 놓을 자리.
        // ★★[JACK 0826] 배치는 <b>도면 설정</b>에서 고른다 — 여기서 묻지 않는다.
        //   JACK: <i>"횡단도 단추는 누르고 찍으면 바로 생기게 해 줘."</i>
        // ★[JACK 0826] <b>지난번 것을 먼저 지운다</b> — 안 지우면 유령이 겹친다.
        WipeOld(db, log);
        WipeOldGroups(db, cdoc, alignId, log);   // ★Civil이 만든 그룹·뷰도 함께
        Point3d at;
        if (at0 != null) { at = at0.Value; log.AppendLine("  자리는 지난번 그대로(측점을 고쳐 다시 그린다)"); }
        else
        {
            var pr = ed.GetPoint("\n[횡단도] 횡단면도를 놓을 왼쪽 아래 자리를 클릭 (Esc=취소): ");
            if (pr.Status != PromptStatus.OK) { ed.WriteMessage("\n[횡단도] 취소."); Flush(log); return; }
            at = pr.Value.TransformBy(ed.CurrentUserCoordinateSystem);
        }
        LastAt = at;   // ★다음에 측점을 고치면 이 자리에 다시 그린다

        // ── ④ 횡단용 검토선 그룹 — 벽 자리는 (전)(후) 둘.
        // ★★[JACK 0826 "지금 종단뷰도 도곽 사이즈에 맞춰서 최적 축척으로 들어가는 거잖아,
        //   그 원리랑 같은 거야"] — <b>맞다. 폭을 줄이는 게 아니라 축척을 고른다.</b>
        //
        //   종이 규격(A1 841×594 · 안쪽 791×419.2mm)이 <b>고정</b>이고,
        //   그림이 그 안에 들어가도록 <b>축척을 사다리에서 골라</b> 도곽을 모형에 그린다.
        //   좌우 폭은 사용자가 정한 대로 <b>온전히</b> 쓴다 — 지형이 잘리면 안 된다.
        double wl = System.Math.Max(1.0, GradingSettings.XsecLeft);
        double wr = System.Math.Max(1.0, GradingSettings.XsecRight);
        var surfs = SectionCommand.FindSurfaces(db, cdoc);
        // ★[검토] 지표면 <b>ID → 종류</b> 표를 만들어 넘긴다 — 이름은 사용자가 지은 것이라 못 믿는다.
        var kindOf = new System.Collections.Generic.Dictionary<ObjectId, string>();
        foreach (var sp in surfs) if (!kindOf.ContainsKey(sp.SurfId)) kindOf[sp.SurfId] = sp.Label;
        // ★[JACK 0826] 지표면의 <b>표고 범위</b>를 남긴다 — 값이 이상할 때 계산을 뒤지기 전에
        //   <b>지표면 자체가 엉뚱한 자리에 있는지</b>부터 갈린다.
        try
        {
            using var trS0 = db.TransactionManager.StartTransaction();
            var sbS = new System.Text.StringBuilder();
            foreach (var sp in surfs)
            {
                try
                {
                    if (trS0.GetObject(sp.SurfId, OpenMode.ForRead) is not CivilDb.TinSurface ts0) continue;
                    var gp = ts0.GetGeneralProperties();
                    sbS.Append($"  [{sp.Label}] {ts0.Name} z{gp.MinimumElevation:F2}~{gp.MaximumElevation:F2}");
                }
                catch { }
            }
            trS0.Commit();
            log.AppendLine("  지표면 —" + sbS);
        }
        catch { }

        ObjectId groupId = ObjectId.Null;
        // ★[JACK 0826] 측점(St)도 담는다 — <b>배치 전에 측점 순으로 정렬</b>해야 한다.
        //   (전)이 앞 측점보다 작아질 수 있다: 10.00 보조측점 다음에 벽 10.16의 (전)9.96이 만들어진다.
        // ★[JACK 0826 검토] <b>Mother</b>=버퍼 주기 전 모측점, <b>Ord</b>=(전)0·본1·(후)2.
        //   정렬을 자른 자리(St)로만 하면 <b>이름 순서가 뒤집힌다</b> — 벽이 정측점 20cm 안에
        //   있으면 (전)이 앞 측점보다 작아져 No.1+00.15(전) → No.1 → No.1+00.15(후)로 늘어선다.
        var slIds = new List<(ObjectId Id, string Name, double St, double Mother, int Ord)>();
        int nSlFail = 0; string slFail = null;
        try
        {
            string gname = SectionCommand.UniqueName(db, cdoc, SectionCommand.GroupBase + "_횡단");
            groupId = CivilDb.SampleLineGroup.Create(gname, alignId);
            if (groupId.IsNull) { ed.WriteMessage("\n[횡단도] 검토선 그룹을 못 만들었습니다."); Flush(log); return; }
            // ★[검토 지적] <b>만든 사람이 등록한다.</b> 종단 세로줄이 이 그룹을 빼려면 알아야 하는데,
            //   종전엔 옛 경로(ProfileCommand)만 등록해서 <b>[횡단도]가 만든 그룹은 아무도 몰랐다</b>.
            ProfileCommand.LastXsecGroupId = groupId;

            // 표본 지표면 — 우리 것만.
            int nSampled = 0; string sampErr = null;
            try
            {
                using var trS = db.TransactionManager.StartTransaction();
                var g = (CivilDb.SampleLineGroup)trS.GetObject(groupId, OpenMode.ForWrite);
                foreach (CivilDb.SectionSource src in g.GetSectionSources())
                {
                    bool ours = surfs.Exists(x => x.SurfId == src.SourceId);
                    // ★[검토] 여기가 조용히 실패하면 단면이 안 잡혀 <b>선 색도 수량도</b> 못 낸다.
                    try { src.IsSampled = ours; if (ours) nSampled++; }
                    catch (System.Exception exS) { sampErr ??= exS.Message; }
                }
                trS.Commit();
            }
            catch (System.Exception exS2) { sampErr ??= exS2.Message; }
            log.AppendLine($"  표본 지표면 {nSampled}장 지정"
                         + (sampErr != null ? $"  ⚠일부 실패 — {sampErr}(단면이 안 잡히면 선 색·수량이 다 빈다)" : ""));

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
                    (double St, string Tag, int Ord)[] jobs;
                    if (sp.Back > sp.Front)
                    {
                        // ★[검토] 밀어내는 자는 <b>Core에 하나</b>다 — 여기서 다시 계산하지 않는다.
                        var (fSt, bSt, outw) = DH.Grading.Core.XsecSpan.PushOut(sp.Front, sp.Back);
                        jobs = new[] { (fSt, "(전)", 0), (bSt, "(후)", 2) };
                        nPair++;
                        log.AppendLine($"    벽 {StationMarks.Fmt(sp.Mid, ProfileCommand.LastStationInterval)}" +
                                       $" — 두께 {sp.Back - sp.Front:F3}m → (전){fSt:F2} / (후){bSt:F2} (밖으로 {outw:F2}m)");
                    }
                    else jobs = new[] { (m.St, "", 1) };

                    foreach (var (st, tag, ord) in jobs)
                    {
                        double stc = System.Math.Min(System.Math.Max(st, s0 + eps), s1 - eps);
                        // ★[JACK 0826 검토] 조용히 사라지던 두 길에 <b>이유를 남긴다</b> —
                        //   횡단면도가 몇 장 빈 채로 나오면 원인을 찾을 길이 없었다.
                        if (!SectionCommand.TryCut(al, stc, wl, wr, out var cut))
                        { nSlFail++; slFail ??= $"{label}{tag} — 노선을 못 잘랐다(측점 {stc:F2})"; continue; }
                        try
                        {
                            var pts = new Point2dCollection { cut.Left, cut.Right };
                            var id = CivilDb.SampleLine.Create($"{gname}_{label}{tag}", groupId, pts);
                            if (!id.IsNull) slIds.Add((id, label + tag, stc, m.St, ord));
                        }
                        catch (System.Exception exSl) { nSlFail++; slFail ??= $"{label}{tag} — {exSl.Message}"; }
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

        // ★[JACK 0826 '순서가 측점 순서대로 나오는 것 같지가 않다'] <b>측점 순으로 정렬한다.</b>
        if (nSlFail > 0)
            log.AppendLine($"  ⚠검토선 {nSlFail}개를 못 만들었다(첫 실패: {slFail})"
                         + " — 그만큼 횡단면도가 빈다. 이름이 겹치면 Civil이 거부한다.");
        //   벽의 (전)은 법면 밖으로 밀려 <b>앞 측점보다 작아질 수 있다</b>(10.00 다음에 9.96).
        //   만든 순서 그대로 놓으면 도면이 측점 순이 아니게 된다.
        //   → <b>모측점 먼저, 같으면 (전)→본→(후)</b> 두 단계로 정렬한다.
        slIds.Sort((a, b) =>
        {
            int c = a.Mother.CompareTo(b.Mother);
            return c != 0 ? c : a.Ord.CompareTo(b.Ord);
        });

        // ── ⑤ 횡단면도 배치 — 초안: 가로로 늘어놓고 줄바꿈.
        //   간격은 검토선 폭에서 잡는다(좌우폭 + 여유). 축척·도곽은 나중에.
        // ★[JACK 0826 '횡단면도는 너무 겹쳐져서 보기가 힘들어'] 간격을 실제 크기에서 잡는다.
        //   가로는 좌우폭이 정하지만, <b>세로는 표고 범위</b>가 정한다 — 40m 고정이라 겹쳤다.
        //   원지반·계획면의 표고 폭에 여유를 더해 잡고, 못 재면 넉넉한 기본값으로 물러선다.
        // ★★[JACK 0826 검토] 여기 있던 <c>dx=(좌우폭)×1.6</c>, <c>dy=(지표면 전체 표고범위)×2.2</c>
        //   <b>짐작값을 걷어냈다.</b> 원지반은 부지 전체를 덮으니 표고 범위가 52m나 나오는데,
        //   횡단 한 장이 잡는 것은 그 측점 좌우 60m 안뿐이다 — <b>세로를 일곱 배</b> 부풀리고 있었다.
        //   이제 <see cref="MeasureViews"/>가 만들어진 뷰를 <b>직접 잰다</b>.
        // ── ⑤ ★★[JACK 0826] <b>종단도와 같은 원리</b>: 종이 규격을 고정하고 <b>축척을 그림에 맞춘다.</b>
        //   그림이 얼마나 큰지는 <b>만들어 봐야</b> 아므로 순서가 이렇게 된다:
        //     뷰를 임시 자리에 만든다 → 재다 → 축척을 고른다 → 칸을 계산한다 → 옮긴다 → 도곽을 그린다
        int cols = GradingSettings.XsecLayoutC, rows = GradingSettings.XsecLayoutR;
        int perSheet = System.Math.Max(1, cols * rows);
        int nPages = (slIds.Count + perSheet - 1) / perSheet;   // 임시 — 뷰를 만든 뒤 다시 센다
        // 임시 간격 — 뷰끼리 겹치지만 않으면 된다. 잰 뒤 제자리로 옮긴다.
        double tempGap = (wl + wr) * 3.0 + 100.0;

        int nView = 0; string firstErr = null; ObjectId firstView = ObjectId.Null;
        var nameAt = new List<(string Name, double X, double Y)>();
        // ★[검토] <b>이름도 같이 들고 간다.</b> 축 아래 측점을 여기서 다시 지으면
        //   밀어낸 자리(stc)로 지어져 뷰 이름(모측점)과 <b>20cm 어긋난 두 이름</b>이 한 장에 찍힌다.
        //   측점명 짓는 자는 하나로 모아 뒀는데 <b>넣는 값</b>이 두 갈래가 됐던 것이다.
        //   (표고 GH·FH는 <b>밀어낸 자리</b>에서 읽어야 (전)·(후)가 실제로 달라진다 — 그건 그대로 둔다.)
        var viewIds = new List<(ObjectId Id, double St, string Name)>();
        var cellAt = new List<(double X, double Y)>();
        for (int i = 0; i < slIds.Count; i++)
        {
            // ★★[JACK 0826 "그냥 위로 쫙 생겨"] <b>원인: 장(張) 개념이 없었다.</b>
            //   종전엔 <c>i % 열수</c>로만 나눠, 1열 배치를 고르면 <b>세로로 끝없이 쌓였다</b>.
            //   한 장에 열×행 개를 채우고 <b>넘으면 다음 장</b>으로 가야 도곽을 씌울 수 있다.
            // ★★[JACK 0826 "배치가 이상해. 도곽에 맞지가 않아"] <b>추정으로 자리를 잡으면 안 된다.</b>
            //   종전엔 좌우폭×1.6, 표고범위×2.2로 <b>크기를 짐작해</b> 자리를 정했는데,
            //   실제 뷰 크기는 <b>축척과 스타일</b>이 정한다 — 짐작이 맞을 이유가 없다.
            //   → 일단 만들고 <b>실제 크기를 재서 칸 가운데로 옮긴다</b>(아래 ⑤-c).
            double x = at.X + i * tempGap, y = at.Y;   // 임시 자리 — ⑤-c에서 칸으로 옮긴다
            try
            {
                // 이름을 주는 오버로드를 쓴다 — 뷰 객체의 이름이 된다.
                var vid = CivilDb.SectionView.Create(slIds[i].Name, slIds[i].Id, new Point3d(x, y, 0.0));
                if (!vid.IsNull)
                {
                    nView++;   // 이름 자리는 옮긴 뒤에 정한다(임시 자리로 쓰면 엉뚱한 데 찍힌다)
                    viewIds.Add((vid, slIds[i].St, slIds[i].Name));
                    if (firstView.IsNull) firstView = vid;
                }
            }
            catch (System.Exception ex) { firstErr ??= $"{slIds[i].Name} — {ex.Message}"; }
        }

        // ── ⑥ ★★[JACK 0826] <b>이름을 직접 쓴다.</b>
        //   Civil의 횡단면도 제목은 <c>SectionViewStyle</c>의 Title Annotation이 정하는데,
        //   .NET API에 <b>제목 관련 멤버가 하나도 없다</b>(축·격자·그래프 스타일뿐) — Autodesk가
        //   인정하는 API 격차다. 스타일은 UI에서만 고칠 수 있어, 코드로는 제목에 (전)(후)를 못 넣는다.
        //   → 뷰 자리를 아는 우리가 <b>그 아래에 이름을 그린다.</b> 확실하고 코드로 된다.
        // ── ⑤-c ★★[JACK 0826] <b>재서 칸 가운데로 옮긴다.</b>
        //   여기가 종전에 <b>주석만 있고 코드가 없던</b> 자리다(검토에서 잡혔다) —
        //   그래서 뷰가 칸 왼쪽 아래 모서리에 붙은 채, 축 글자는 그보다 더 왼쪽으로 삐져나갔다.
        //   옮기는 것은 <c>Location</c> 한 줄로 한다 — <c>TransformBy</c>로 확대·축소하면
        //   Civil이 다시 그릴 때 배율이 풀린다(배율을 저장할 자리가 객체에 없다).
        //   ★<b>반드시 우리가 글자를 그리기 전에</b> 옮긴다: 이름·표·축은 생 DBText/Line이라 안 따라온다.
        // ★★[검토·자체확인] <b>재기 전에 스타일을 입힌다.</b> 회사 스타일은 아래에 <b>밴드</b>를 붙이는데,
        //   재고 나서 입히면 잰 값이 실제보다 작아 축척이 헐거워지고 <b>밴드만큼 칸을 넘친다.</b>
        //   순서가 곧 정확도다 — 종단도도 스타일을 걸고 격자를 좁힌 <b>뒤에</b> 잰다.
        // ★★[JACK 0826] <b>회사 스타일이 있으면 그것이 그린다.</b>
        //   가운데 표고축도 축 아래 GH·FH도 <c>DH_횡단 뷰 스타일</c>과 밴드 세트의 몫이다 —
        //   두 벌이 겹쳐 보이는 것이 없는 것보다 나쁘므로, 스타일이 먹으면 우리는 손을 뗀다.

        int nStyled = ApplyCompanyStyle(db, cdoc, viewIds, log);
        TuneAxisTicks(db, XsecStyleId(db, viewIds), log);
        // ★[검토] 제목 끄기는 <b>회사 스타일을 입힌 뒤</b>여야 한다 — 스타일을 갈아끼우면
        //   껐던 제목이 <b>새 스타일 것으로 되살아난다</b>. 그리고 <b>재기 전</b>이어야 한다:
        //   제목이 사라지면 뷰가 그만큼 작아지므로, 끄고 나서 재야 실제 크기가 나온다.
        // ── ⑤-b ★★[JACK 0826 "막 40m 간격으로 나와서 측점번호가 다르고 NO.1 표시도 없어"]
        //   <b>원인: 제목이 두 개였다.</b> 우리가 붙인 이름(No.1+13.82) 위에 Civil이 자기 제목을
        //   따로 그리고 있었고, 화면에 보이던 건 그쪽이다 — Civil 제목은 노선 측점을 자기 형식으로
        //   찍어서 "No." 표기도 없고 간격도 달라 보인다. 우리 이름은 로그대로 <b>줄곧 맞았다.</b>
        //   지난번엔 "SectionViewStyle에 제목 멤버가 없다"고 결론냈는데 <b>틀렸다</b> —
        //   내용은 못 바꿔도 <c>GraphTitle</c>을 <b>끌 수는 있다</b>. 끄면 우리 이름만 남는다.
        //   (스타일은 뷰들이 공유하므로 한 번만 끄면 전부 적용된다.)
        bool titleOff = false;
        if (!firstView.IsNull)
        {
            try
            {
                using var trS = db.TransactionManager.StartTransaction();
                string sn = "?";
                if (trS.GetObject(firstView, OpenMode.ForRead) is CivilDb.SectionView sv0
                    && trS.GetObject(sv0.StyleId, OpenMode.ForWrite) is CivilDb.Styles.SectionViewStyle svs)
                {
                    sn = svs.Name;
                    using var dsT = svs.GetDisplayStylePlan(CivilDb.Styles.SectionViewDisplayStyleType.GraphTitle);
                    if (dsT != null && dsT.Visible) { dsT.Visible = false; titleOff = true; }
                    else if (dsT != null) titleOff = true;   // 이미 꺼져 있다
                    // ★[JACK 0826 회사 스타일 도입] <b>바깥 축을 끄지 않는다.</b>
                    //   종전엔 우리가 가운데 축을 그리니 중복이라 껐는데, 이제 축을 정하는 것은
                    //   <c>DH_횡단 뷰 스타일</c>이다. 그 위에서 또 끄면 <b>회사가 정한 모양을 우리가 망친다</b>.
                    //   게다가 스타일은 도면 공용이라 다른 명령이 만든 뷰까지 축을 잃는다(검토 지적).
                }
                trS.Commit();
                log.AppendLine(titleOff
                    ? $"  Civil 기본 제목 껐다 — 스타일 '{sn}'의 GraphTitle → 우리가 쓴 이름만 보인다"
                    : $"  ⚠Civil 기본 제목을 못 껐다 — 스타일 '{sn}' · 제목이 두 개로 보인다");
            }
            catch (System.Exception ex) { log.AppendLine("  Civil 기본 제목 끄기 실패 — " + ex.Message); }
        }

        var mv = MeasureViews(db, viewIds, log);

        // ── ★★축척 고르기 — 종단도 <c>FitSheet</c>과 같은 셈이다.
        //   <c>종이 mm = 모형 m × 1000 ÷ 축척</c>이므로 뒤집으면 <c>필요 축척 = 모형 m × 1000 ÷ 종이 mm</c>.
        //   가로·세로 중 <b>엄한 쪽</b>이 이기고, 사다리에서 그 값 이상인 첫 값을 고른다.
        // ★[검토] 여기서 <b>실제로 만들어진 뷰 수</b>로 다시 센다 —
        //   뷰가 몇 개 실패하면 빈 도곽이 한 장 더 그려진다.
        nPages = (System.Math.Max(viewIds.Count, 1) + perSheet - 1) / perSheet;
        double cellWmm = SheetCommand.InnerW / cols;      // 칸 폭(종이 mm) — 거터 없이 그냥 나눈다
        double cellHmm = XsecInnerH / rows;              // 칸 높이(종이 mm)
        double tableWmm = QtWidthMm;
        // ★[검토 §50] 표 높이를 <b>두 곳에서 다르게</b> 세고 있었다 —
        //   자리 잡는 쪽은 19.0줄, 그리는 쪽은 머리줄 1.4배를 반영해 19.4줄. 2.3mm 어긋났다.
        double tableHmm = QtTableHmm;

        // ★★[검토] 표를 <b>오른쪽</b>에 둘 때와 <b>아래</b>에 둘 때를 <b>둘 다 계산</b>해
        //   축척이 작은 쪽(=그림이 큰 쪽)을 고른다. 3×2처럼 칸이 좁은 배치에서는
        //   표를 아래로 내리는 것만으로 <b>1:500 → 1:300</b>으로 두 단계가 살아난다.
        double padW = 2 * CellPadMm, padH = 2 * CellPadMm + NameRoomMm;
        double gwRight = System.Math.Max(10.0, cellWmm - padW - TableGapMm - tableWmm);
        double ghRight = System.Math.Max(10.0, cellHmm - padH);
        double gwBelow = System.Math.Max(10.0, cellWmm - padW);
        double ghBelow = System.Math.Max(10.0, cellHmm - padH - TableGapMm - tableHmm);
        double sRight = PickScale(mv.W, mv.H, gwRight, ghRight);
        double sBelow = PickScale(mv.W, mv.H, gwBelow, ghBelow);
        bool tableRight = sRight > 0 && (sBelow <= 0 || sRight <= sBelow);   // 같으면 오른쪽(참고 도면)
        double autoScale = tableRight ? sRight : sBelow;

        // ★★[JACK 0826 "도면설정에 종단도 축척이 자동·지정이 있잖아? 횡단도 똑같은 로직으로"]
        //   <b>고정을 골랐으면 그 값을 그대로 쓴다.</b> 안 들어가도 <b>바꾸지 않는다</b> —
        //   사용자가 1:200을 콕 집었는데 우리가 1:250으로 올리면 <b>도면에 적힌 축척과 실제가 어긋난다.</b>
        //   현장에서 자로 재는 값이라 그게 넘치는 것보다 나쁘다. 넘친 채로 그리고 로그로 알린다.
        bool fixedScale = GradingSettings.XsecScale > 0;
        double scale = fixedScale ? GradingSettings.XsecScale : autoScale;
        double graphWmm = tableRight ? gwRight : gwBelow;
        double graphHmm = tableRight ? ghRight : ghBelow;
        if (scale <= 0)
        {
            scale = SheetCommand.Scales[SheetCommand.Scales.Length - 1];
            log.AppendLine($"  ⚠사다리 끝까지 맞는 축척이 없다 — 가장 큰 1:{scale:F0}으로 둔다"
                         + $" (그림 {mv.W:F1}×{mv.H:F1}m · 자리 {graphWmm:F0}×{graphHmm:F0}mm)");
        }
        double sc = scale / 1000.0;                       // 종이 1mm = 모형 sc m
        const double PageGapMm = 0.0;                     // 장과 장을 맞붙인다(JACK)
        double PageGap = PageGapMm * sc;
        double sheetW = SheetCommand.SheetW * sc, sheetH = SheetCommand.SheetH * sc;
        double innerW = SheetCommand.InnerW * sc, innerH = XsecInnerH * sc;
        double cellW = cellWmm * sc, cellH = cellHmm * sc;
        double tableRoom0 = tableRight ? (tableWmm + TableGapMm) * sc : 0.0;
        double tableBelow0 = tableRight ? 0.0 : (tableHmm + TableGapMm) * sc;
        double annoScale = SheetCommand.CurrentDrawingScale(db);
        log.AppendLine($"  ★축척 1:{scale:F0}({(fixedScale ? "도면설정에서 고정" : "자동 — 칸에 맞춤")})"
                     + $" · 표는 {(tableRight ? "그림 오른쪽" : "그림 아래")}"
                     + $" (필요 가로 1:{mv.W * 1000.0 / graphWmm:F0} · 세로 1:{mv.H * 1000.0 / graphHmm:F0}"
                     + $" · 오른쪽 1:{sRight:F0} vs 아래 1:{sBelow:F0})");
        log.AppendLine($"  배치 {cols}×{rows}(한 장 {perSheet}개) · A1 {sheetW:F1}×{sheetH:F1}m"
                     + $" · 안쪽 {innerW:F1}×{innerH:F1}m(종이 {SheetCommand.InnerW:F0}×{XsecInnerH:F0}mm)"
                     + $" · 칸 {cellW:F1}×{cellH:F1}m(종이 {cellWmm:F0}×{cellHmm:F0}mm) · {nPages}장");
        if (annoScale > 0 && System.Math.Abs(annoScale - scale) > 1e-6)
            log.AppendLine($"  ※도면 주석 축척은 1:{annoScale:F0}(종단이 걸어 둔 것)이라 그대로 둔다 —"
                         + $" 횡단 글자·밴드가 <b>{scale / annoScale:F2}배</b>로 인쇄된다."
                         + " 차이가 크면 횡단 전용 스타일의 종이값을 그 비율로 보정해야 한다.");

        // 칸 자리를 지금 계산한다 — 축척이 정해져야 칸 크기를 안다.
        cellAt.Clear();
        for (int i = 0; i < viewIds.Count; i++)
        {
            int page = i / perSheet, idx = i % perSheet;
            cellAt.Add((at.X + page * (sheetW + PageGap) + (idx % cols) * cellW,
                        at.Y + innerH - (idx / cols + 1) * cellH));
        }
        int nMoved = 0;
        if (mv.N > 0 && mv.W > 0 && mv.H > 0)
        {
            try
            {
                using var trM = db.TransactionManager.StartTransaction();
                for (int i = 0; i < viewIds.Count && i < cellAt.Count; i++)
                {
                    try
                    {
                        if (trM.GetObject(viewIds[i].Id, OpenMode.ForWrite) is not CivilDb.SectionView sv) continue;
                        var ex = ((Entity)sv).GeometricExtents;
                        // ★★[JACK 0826 스샷] <b>그림과 표를 한 덩어리로 묶어 칸 가운데에 놓는다.</b>
                        //   종전엔 '칸에서 표 폭을 뗀 자리'의 가운데에 그림만 놓았다. 그런데 축척은
                        //   <b>세로가 정하는 경우가 많아</b> 가로가 남는데(실측 42%만 씀), 그 남은 폭이
                        //   전부 오른쪽에 몰려 그림이 왼쪽으로 치우쳐 보였다.
                        //   ★기준은 <b>가장 큰 뷰</b>다 — 뷰마다 제 크기로 가운데를 잡으면 장마다
                        //   그림 자리가 들쭉날쭉해 도면이 흐트러진다.
                        double padM = CellPadMm * sc, nameM = NameRoomMm * sc;
                        double gapM2 = TableGapMm * sc, tblW = tableWmm * sc;
                        double bundleW = tableRight ? mv.W + gapM2 + tblW : mv.W;
                        // ★[검토] 표를 오른쪽에 둘 때도 <b>표 높이를 센다</b> — 표가 그림보다 길면
                        //   가운데 맞춤이라 위아래 <b>양쪽으로</b> 삐져나간다(전엔 아래로만 나갔다).
                        double bundleH = tableRight
                            ? System.Math.Max(mv.H, QtTableHmm * sc)
                            : mv.H + gapM2 + QtTableHmm * sc;
                        double leftX = cellAt[i].X + System.Math.Max(padM, (cellW - bundleW) / 2.0);
                        double botY = cellAt[i].Y + nameM
                                    + System.Math.Max(padM, (cellH - nameM - bundleH) / 2.0);
                        double wantCx = leftX + mv.W / 2.0;
                        // 표가 아래면 그림은 그 위에 앉는다.
                        double wantCy = tableRight
                            ? botY + mv.H / 2.0
                            : botY + tableHmm * sc + gapM2 + mv.H / 2.0;
                        double curCx = (ex.MinPoint.X + ex.MaxPoint.X) / 2.0;
                        double curCy = (ex.MinPoint.Y + ex.MaxPoint.Y) / 2.0;
                        var loc = sv.Location;
                        sv.Location = new Point3d(loc.X + (wantCx - curCx), loc.Y + (wantCy - curCy), loc.Z);
                        nMoved++;
                    }
                    catch { }
                }
                trM.Commit();
            }
            catch (System.Exception exM) { log.AppendLine("  뷰 옮기기 실패 — " + exM.Message); }
        }
        double fitW = graphWmm * sc, fitH = graphHmm * sc;
        log.AppendLine($"  뷰 실측 — 가장 큰 것 {mv.W:F1}×{mv.H:F1}m ({mv.N}개 잼) · 칸 그림 자리 {fitW:F1}×{fitH:F1}m"
                     + $" · 가운데로 옮긴 것 {nMoved}개"
                     + (mv.W > fitW || mv.H > fitH
                        ? $"  ⚠넘친다(가로 {mv.W / System.Math.Max(fitW, 1e-9):F2}배 · 세로 {mv.H / System.Math.Max(fitH, 1e-9):F2}배)"
                          + (fixedScale
                             ? $" — 축척을 1:{scale:F0}으로 <b>고정</b>하셨기 때문이다. 자동이면 1:{autoScale:F0}이 된다."
                             : " — 사다리 끝까지 맞는 값이 없다")
                        : mv.W < fitW * WarnFill && mv.H < fitH * WarnFill
                          ? $"  → 칸에 들어가지만 <b>헐겁다</b>(가로 {mv.W / fitW * 100:F0}% · 세로 {mv.H / fitH * 100:F0}%만 씀)"
                            + " — 배치를 더 조밀하게 하면 축척이 커진다"
                          : "  → 칸에 들어간다"));

        // ★[검토] 이름은 <b>옮긴 뒤</b>에 그린다 — 생 DBText라 뷰를 따라오지 않는다.
        //   자리는 뷰의 실제 아래쪽에서 잡는다(칸 모서리가 아니라).
        nameAt.Clear();
        try
        {
            using var trN2 = db.TransactionManager.StartTransaction();
            foreach (var (vid, _, vn) in viewIds)
            {
                try
                {
                    if (trN2.GetObject(vid, OpenMode.ForRead) is not Entity ev) continue;
                    var ex2 = ev.GeometricExtents;
                    nameAt.Add((vn, (ex2.MinPoint.X + ex2.MaxPoint.X) / 2.0, ex2.MinPoint.Y));
                }
                catch { }
            }
            trN2.Commit();
        }
        catch { }
        int nTxt = DrawViewNames(db, nameAt, NameTextMm * sc, log);
        // 회사 스타일이 축을 안 그려 줄 때만 우리가 그린다 — 옮긴 뒤라야 자리가 맞다.
        if (nStyled == 0) DrawCenterAxis(db, viewIds, alignId, log);
        DrawXsecFrames(db, at, nPages, sc, cols, rows, PageGap, scale, log);
        var qtyMap = CollectQty(db, slIds, alignId, wl, wr, surfs, log);
        DrawQtyTables(db, viewIds, sc, tableRight, TableGapMm * sc, qtyMap, log);
        // ★[JACK 0826] 선 색·눈금은 <b>숨기기 전</b>에 — 숨긴 뒤에도 되지만 로그 차례가 헷갈린다.
        ApplySectionStyles(db, cdoc, slIds, kindOf, log);
        HideSampleLines(db, cdoc, slIds, groupId, log);   // ★뷰를 다 만든 뒤에 숨긴다

        log.AppendLine($"  횡단면도 이름 {nTxt}개 직접 씀(레이어 '{XsecTitleLayer}') — " +
                       (titleOff ? "Civil 기본 제목은 껐다 — 화면 이름은 이것뿐이다"
                                 : "⚠Civil 기본 제목이 살아 있다 — 이름이 두 개로 보인다"));
        log.AppendLine($"  횡단면도 {nView}/{slIds.Count}장 배치 · 배치 {cols}×{rows} · 칸 {cellW:F1}×{cellH:F1}m" +
                       (firstErr != null ? $"\n  ⚠첫 실패: {firstErr}" : ""));
        ed.WriteMessage($"\n[횡단도] 횡단면도 {nView}장 · 검토선 {slIds.Count}개" +
                        $"\n  자세한 내용: {DiagLog.FilePath}");
        Flush(log);
    }

    private static void Flush(System.Text.StringBuilder log)
    {
        try { DiagLog.Append("\n" + log.ToString()); } catch { }
    }

    /// <summary>횡단면도 <b>이름을 직접 쓴다.</b> ★[JACK 0826] 두 명령이 같이 쓴다 —
    /// Civil 기본 제목을 끄면(<c>GraphTitle</c>) 이름을 안 그리는 쪽 뷰가 <b>무제</b>가 되기 때문이다.
    /// 제목 스위치는 스타일에 달려 있고 스타일은 도면 공용이라, 한 명령만 고치면 다른 명령이 다친다.</summary>
    internal static int DrawViewNames(Database db,
        System.Collections.Generic.List<(string Name, double X, double Y)> at,
        double height, System.Text.StringBuilder log)
    {
        int n = 0;
        if (at == null || at.Count == 0) return 0;
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            var ms = (BlockTableRecord)tr.GetObject(
                SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
            var lay = SectionCommand.EnsureLayer(db, tr, XsecTitleLayer, 2);
            // ★★[JACK 0826 "전후 글씨가 물음표로 나왔어"] <b>원인: 글꼴을 안 줬다.</b>
            //   AutoCAD 기본 스타일은 <c>txt.shx</c>라 한글이 없다 — 없는 글자는 <b>?</b>로 찍힌다.
            //   JACK이 0731에 지번의 '산'이 깨질 때 이미 고쳐 둔 자리가 있는데(EnsureKoreanTextStyle)
            //   새로 만든 이 함수가 그걸 안 썼다 — <b>같은 것을 두 곳에서</b> 또 갈라진 것이다.
            var kst = ImportGisCommand.EnsureKoreanTextStyle(db, tr);
            // ★[검토] 글꼴을 못 얻으면 <b>조용히 물음표로 되돌아간다</b> — 한 줄이라도 남긴다.
            //   같은 증상을 또 겪을 때 원인을 처음부터 다시 찾지 않으려는 것이다.
            if (kst.IsNull) log?.AppendLine("  ⚠한글 글꼴 스타일을 못 얻었다 — 한글이 ?로 찍힌다");
            double h = System.Math.Max(0.5, height);
            foreach (var (nm, x, y) in at)
            {
                try
                {
                    var t = new DBText
                    {
                        TextString = nm,
                        Height = h,
                        Position = new Point3d(x, y - h * 2.0, 0.0),   // 뷰 아래에 붙인다
                        // ★[검토 §50 재발] 호출부가 <b>뷰의 가운데 X</b>를 주는데 왼쪽 정렬이면
                        //   글자가 오른쪽으로 삐져나간다. 같은 파일의 DrawCenterAxis는 가운데 정렬을 쓴다.
                        HorizontalMode = TextHorizontalMode.TextCenter,
                    };
                    if (!lay.IsNull) t.LayerId = lay;
                    if (!kst.IsNull) t.TextStyleId = kst;   // 한글 글꼴
                    ms.AppendEntity(t); tr.AddNewlyCreatedDBObject(t, true);
                    n++;
                }
                catch { }
            }
            tr.Commit();
        }
        catch (System.Exception ex) { log?.AppendLine("  이름 쓰기 실패 — " + ex.Message); }
        return n;
    }

    internal const string XsecAxisLayer = "DH-횡단-축";      // 표고축·눈금 — 빨강
    internal const string XsecTextLayer = "DH-횡단-글씨";    // 표고 숫자·측점·GH·FH — 흰색

    /// <summary>★★[JACK 0826 "횡단뷰는 중앙에 스케일을 넣고 스케일 아래 측점 GH, FH가 들어가야 해"]
    ///
    /// <para><b>Civil 기본 횡단면도는 축이 바깥에만 선다.</b> 우리 도면 관례는 <b>노선 중심</b>에
    /// 표고축을 세우고 그 아래에 측점·지반고·계획고를 적는 것이라, 이건 직접 그려야 한다.</para>
    ///
    /// <para>자리는 <c>FindXYAtOffsetAndElevation</c>이 알려 준다 —
    /// <b>오프셋 0이 곧 노선 중심</b>이므로 그 선이 도면의 가운데다.</para>
    ///
    /// <para><b>GH·FH는 도면에서 읽는다.</b> 종단이 남긴 static을 쓰지 않는다 —
    /// 다른 도면을 열면 옛 값이 조용히 쓰이는 사고를 이미 겪었다. 노선에 붙은 종단을
    /// 이름으로 찾아 그 측점의 표고를 그때그때 묻는다.</para></summary>
    internal static int DrawCenterAxis(Database db, List<(ObjectId Id, double St, string Name)> views,
                                       ObjectId alignId, System.Text.StringBuilder log)
    {
        if (views == null || views.Count == 0) return 0;
        int n = 0, noGh = 0;
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            var ms = (BlockTableRecord)tr.GetObject(
                SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
            var layAx = SectionCommand.EnsureLayer(db, tr, XsecAxisLayer, 1);
            var layTx = SectionCommand.EnsureLayer(db, tr, XsecTextLayer, 7);
            var kst = ImportGisCommand.EnsureKoreanTextStyle(db, tr);
            // ★[검토] 글꼴을 못 얻으면 <b>조용히 물음표로 되돌아간다</b> — 한 줄이라도 남긴다.
            //   같은 증상을 또 겪을 때 원인을 처음부터 다시 찾지 않으려는 것이다.
            if (kst.IsNull) log?.AppendLine("  ⚠한글 글꼴 스타일을 못 얻었다 — 한글이 ?로 찍힌다");

            // ── 원지반(GH)·계획면(FH) 종단 찾기 — 도면이 진실의 원천이다.
            ObjectId pidG = ObjectId.Null, pidF = ObjectId.Null;
            try
            {
                if (tr.GetObject(alignId, OpenMode.ForRead) is CivilDb.Alignment al)
                    foreach (ObjectId pid in al.GetProfileIds())
                    {
                        try
                        {
                            if (tr.GetObject(pid, OpenMode.ForRead) is not CivilDb.Profile pp) continue;
                            string pn = pp.Name ?? "";
                            if (pn.IndexOf("터파기", System.StringComparison.Ordinal) >= 0) continue;
                            if (pidG.IsNull && pn.IndexOf("원지반", System.StringComparison.Ordinal) >= 0) pidG = pid;
                            else if (pidF.IsNull && (pn.IndexOf("정지", System.StringComparison.Ordinal) >= 0
                                                  || pn.IndexOf("계획", System.StringComparison.Ordinal) >= 0)) pidF = pid;
                        }
                        catch { }
                    }
            }
            catch { }

            foreach (var (vid, st, vname) in views)
            {
                try
                {
                    if (tr.GetObject(vid, OpenMode.ForRead) is not CivilDb.SectionView sv) continue;
                    double eLo = sv.ElevationMin, eHi = sv.ElevationMax;
                    if (!(eHi > eLo)) continue;

                    // 축 위·아래 끝의 도면 좌표 — 이 둘이 도면상 높이를 알려 준다.
                    double x0 = 0, y0 = 0, x1 = 0, y1 = 0;
                    if (!sv.FindXYAtOffsetAndElevation(0.0, eLo, ref x0, ref y0)) continue;
                    if (!sv.FindXYAtOffsetAndElevation(0.0, eHi, ref x1, ref y1)) continue;
                    double drawH = System.Math.Abs(y1 - y0);
                    if (drawH < 1e-6) continue;
                    double txtH = System.Math.Max(0.4, drawH * 0.035);

                    // ── 세로 축선
                    var ax = new Line(new Point3d(x0, y0, 0), new Point3d(x1, y1, 0));
                    if (!layAx.IsNull) ax.LayerId = layAx;
                    ms.AppendEntity(ax); tr.AddNewlyCreatedDBObject(ax, true);

                    // ── 눈금 — 큰 눈금 5m(숫자), 작은 눈금 1m. 범위가 넓으면 10m/2m로 벌린다.
                    double major = (eHi - eLo) > 40.0 ? 10.0 : 5.0;
                    double minor = major / 5.0;
                    double tickMaj = txtH * 0.9, tickMin = txtH * 0.45;
                    for (double e = System.Math.Ceiling(eLo / minor) * minor; e <= eHi + 1e-9; e += minor)
                    {
                        double tx = 0, ty = 0;
                        if (!sv.FindXYAtOffsetAndElevation(0.0, e, ref tx, ref ty)) continue;
                        bool isMaj = System.Math.Abs(e / major - System.Math.Round(e / major)) < 1e-6;
                        double len = isMaj ? tickMaj : tickMin;
                        var tk = new Line(new Point3d(tx - len, ty, 0), new Point3d(tx, ty, 0));
                        if (!layAx.IsNull) tk.LayerId = layAx;
                        ms.AppendEntity(tk); tr.AddNewlyCreatedDBObject(tk, true);
                        if (!isMaj) continue;
                        var el = new DBText
                        {
                            TextString = e.ToString("0.00"),
                            Height = txtH,
                            Position = new Point3d(tx + tickMaj * 0.5, ty - txtH * 0.5, 0),
                        };
                        if (!layTx.IsNull) el.LayerId = layTx;
                        if (!kst.IsNull) el.TextStyleId = kst;
                        ms.AppendEntity(el); tr.AddNewlyCreatedDBObject(el, true);
                    }

                    // ── 축 아래 세 줄 — 측점 · GH · FH
                    double gh = double.NaN, fh = double.NaN;
                    try { if (!pidG.IsNull && tr.GetObject(pidG, OpenMode.ForRead) is CivilDb.Profile pg) gh = pg.ElevationAt(st); } catch { }
                    try { if (!pidF.IsNull && tr.GetObject(pidF, OpenMode.ForRead) is CivilDb.Profile pf) fh = pf.ElevationAt(st); } catch { }
                    if (double.IsNaN(gh)) noGh++;

                    // ★[검토] 뷰 이름을 <b>그대로</b> 쓴다 — 다시 짓지 않는다.
                    string stName = vname ?? StationMarks.Fmt(st, ProfileCommand.LastStationInterval);
                    var lines = new[]
                    {
                        stName,
                        "GH(+) " + (double.IsNaN(gh) ? "-" : gh.ToString("0.00")),
                        "FH(+) " + (double.IsNaN(fh) ? "-" : fh.ToString("0.00")),
                    };
                    double ly = y0 - txtH * 1.2;
                    foreach (var s in lines)
                    {
                        var tx2 = new DBText
                        {
                            TextString = s,
                            Height = txtH,
                            Position = new Point3d(x0, ly, 0),
                            HorizontalMode = TextHorizontalMode.TextCenter,
                            AlignmentPoint = new Point3d(x0, ly, 0),
                        };
                        if (!layTx.IsNull) tx2.LayerId = layTx;
                        if (!kst.IsNull) tx2.TextStyleId = kst;
                        ms.AppendEntity(tx2); tr.AddNewlyCreatedDBObject(tx2, true);
                        ly -= txtH * 1.35;
                    }
                    n++;
                }
                catch { }
            }
            tr.Commit();
        }
        catch (System.Exception ex) { log?.AppendLine("  가운데 축 그리기 실패 — " + ex.Message); }
        log?.AppendLine($"  가운데 표고축 {n}개 · 측점/GH/FH 적음"
                      + (noGh > 0 ? $" · ⚠지반고를 못 읽은 것 {noGh}개(원지반 종단을 못 찾음)" : ""));
        return n;
    }

    /// <summary>회사 표준 횡단 스타일 이름 — 템플릿(DHT.dwt)에서 <b>이미 들여오고 있다</b>.
    /// <para>★★[JACK 0826 "회사에서 사용하는 DH_횡단 뷰 스타일 … 이걸 이용해서 해도 돼.
    /// 다만 이 애드인을 설치할 때 이 스타일도 자동으로 생성되어야 해"] — <b>확인해 보니 이미 된다.</b>
    /// <c>ProfileStyleTemplate.Import</c>가 'DH'로 시작하는 스타일을 서랍째 훑어 가져오고,
    /// 로그에 <c>DH_횡단 뷰 스타일 [AeccDbGraphStyleCrossSection]</c>으로 찍힌다.
    /// <b>없던 것은 그 스타일을 실제로 쓰는 코드</b>였다 — 뷰를 기본 스타일로 만들고 있었다.</para></summary>
    private const string XsecViewStyleName = "DH_횡단 뷰 스타일";

    /// <summary>축 아래 지반고·계획고·터파기바닥고를 채우는 밴드 세트.</summary>
    private const string XsecBandSetName = "DH_횡단 뷰_정보표시 테이블";

    /// <summary>★[JACK 0826] 만들어진 횡단면도에 <b>회사 스타일과 밴드</b>를 입힌다.
    ///
    /// <para>이걸 입히면 <b>가운데 표고축도, 축 아래 GH·FH도 스타일이 그린다</b> —
    /// 우리가 직접 그릴 필요가 없어진다. 그래서 이 함수가 성공하면
    /// <see cref="DrawCenterAxis"/>는 건너뛴다(두 벌이 겹쳐 보이면 더 나쁘다).</para>
    ///
    /// <para><b>못 찾으면 조용히 물러난다.</b> 회사 템플릿이 없는 도면에서도 기능은 돌아야 한다 —
    /// 그때는 종전대로 우리가 축을 그린다.</para></summary>
    /// <returns>스타일을 입힌 뷰 수. 0이면 스타일을 못 찾은 것이다.</returns>
    internal static int ApplyCompanyStyle(Database db, CivilApp.CivilDocument cdoc,
                                          List<(ObjectId Id, double St, string Name)> views,
                                          System.Text.StringBuilder log)
    {
        if (views == null || views.Count == 0) return 0;
        ObjectId vs = ObjectId.Null, bs = ObjectId.Null;
        string vsName = "(없음)", bsName = "(없음)";
        try
        {
            vs = SectionCommand.PickStyle(db, cdoc.Styles.SectionViewStyles, XsecViewStyleName);
            if (!vs.IsNull) using (var t0 = db.TransactionManager.StartTransaction())
            {
                if (t0.GetObject(vs, OpenMode.ForRead) is CivilDb.Styles.StyleBase sb0) vsName = sb0.Name;
                t0.Commit();
                // 이름이 전혀 다른 것을 첫 번째라고 집어 온 경우는 쓰지 않는다 — 기본 스타일보다 나쁠 수 있다.
                if (vsName.IndexOf("DH", System.StringComparison.Ordinal) < 0) { vs = ObjectId.Null; vsName = "(회사 것 없음)"; }
            }
        }
        catch { }
        try
        {
            bs = SectionCommand.PickStyle(db, cdoc.Styles.SectionViewBandSetStyles, XsecBandSetName);
            if (!bs.IsNull) using (var t1 = db.TransactionManager.StartTransaction())
            {
                if (t1.GetObject(bs, OpenMode.ForRead) is CivilDb.Styles.StyleBase sb1) bsName = sb1.Name;
                t1.Commit();
                if (bsName.IndexOf("DH", System.StringComparison.Ordinal) < 0) { bs = ObjectId.Null; bsName = "(회사 것 없음)"; }
            }
        }
        catch { }

        if (vs.IsNull && bs.IsNull)
        {
            log?.AppendLine("  회사 횡단 스타일을 못 찾았다 — 가운데 축을 우리가 그린다"
                          + $" (찾은 이름: 뷰 {vsName} · 밴드 {bsName})");
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
                    if (!vs.IsNull) { sv.StyleId = vs; n++; }
                    if (!bs.IsNull) { try { sv.Bands.ImportBandSetStyle(bs); nb++; } catch { } }
                }
                catch { }
            }
            tr.Commit();
        }
        catch (System.Exception ex) { log?.AppendLine("  회사 스타일 적용 실패 — " + ex.Message); }

        log?.AppendLine($"  회사 스타일 — 뷰 '{vsName}' {n}개 · 밴드 '{bsName}' {nb}개"
                      + (n > 0 ? "  → 가운데 축·GH·FH는 스타일이 그린다" : ""));
        return n;
    }

    /// <summary>★★[JACK 0826 "도곽이 안 생겨. 도곽은 종단에서 사용한 그 크기 그대로 사용해"]
    ///
    /// <para><b>종단도와 같은 규격·같은 레이어</b>로 그린다 — 숫자를 여기 다시 적지 않고
    /// <see cref="SheetCommand"/>의 상수를 그대로 쓴다. 두 곳에 적으면 언젠가 갈라진다.</para>
    ///
    /// <para>바깥 네모는 <b>종이 전체</b>(A1 841×594mm × 축척), 안쪽 네모는 <b>여백을 뺀 자리</b>다.
    /// 종단도의 안쪽 네모는 '뷰포트가 볼 자리'였는데, 횡단은 그 안을 칸으로 나눠 쓰므로
    /// <b>칸 경계선</b>도 함께 그어 준다 — 한 칸에 하나씩 들어갔는지 눈으로 대볼 수 있다.</para></summary>
    private static int DrawXsecFrames(Database db, Point3d at, int nPages, double sc,
                                      int cols, int rows, double pageGap, double scaleForTitle,
                                      System.Text.StringBuilder log)
    {
        if (nPages <= 0) return 0;
        double sheetW = SheetCommand.SheetW * sc, sheetH = SheetCommand.SheetH * sc;
        double mL = SheetCommand.MarginLR * sc;
        double mT = SheetCommand.MarginTop * sc, mB = SheetCommand.MarginBottom * sc;
        // ★★[검토에서 잡힌 버그] 여기가 <b>524mm</b>로 계산하고 있었다 — 뷰를 놓는 쪽은
        //   <c>ViewH</c>(419.2mm)를 쓰는데 도곽만 여백 뺀 전부를 썼다. <b>같은 네모를 두 크기로</b>
        //   재고 있었으니 칸선이 뷰와 맞을 리가 없다. 제목 자리 104.8mm를 도곽 쪽만 몰랐다.
        double innerW = SheetCommand.InnerW * sc, innerH = XsecInnerH * sc;
        int n = 0;
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
            // ★[JACK 0826] <b>횡단 전용 도곽 레이어</b> — 종단과 같이 쓰면 횡단을 지울 때
            //   종단 도곽까지 함께 지워진다. 색은 종단과 같은 주황으로 맞춘다.
            var lay = SectionCommand.EnsureLayer(db, tr, XsecFrameLayer, 30);   // 30 = 주황
            var layCell = SectionCommand.EnsureLayer(db, tr, XsecCellLayer, 8);             // 8 = 회색
            var layT = SectionCommand.EnsureLayer(db, tr, XsecTitleLayer, 7);
            var kst = ImportGisCommand.EnsureKoreanTextStyle(db, tr);
            for (int p = 0; p < nPages; p++)
            {
                // 배치의 원점(at)은 <b>안쪽 왼쪽 아래</b>다 — 뷰를 거기 기준으로 놓았으므로 도곽도 거기서 되짚는다.
                double ix = at.X + p * (sheetW + pageGap);
                double iy = at.Y;
                SheetCommand.AddRect(tr, ms, lay, ix - mL, iy - mB, ix - mL + sheetW, iy - mB + sheetH);  // 종이
                SheetCommand.AddRect(tr, ms, lay, ix, iy, ix + innerW, iy + innerH);                       // 안쪽
                n++;
                // ★[JACK 도면] 제목은 <b>안쪽 테두리 바로 위</b> 가운데, 아래에 축척.
                //   장 번호 <c>(8/11)</c>가 붙어야 도면철에서 순서를 안다.
                // ★[JACK 0826] 제목은 <b>제목 자리 50mm 안</b>에 든다 — 두 줄(이름 + 축척)이라
                //   글자 6mm·4mm면 줄간까지 20mm 남짓이고, 위쪽 여백이 넉넉하다.
                double tcx = ix + innerW / 2.0, tcy = iy + innerH + XsecTitleMm * sc * 0.42;
                double th1 = 6.0 * sc, th2 = 4.0 * sc;   // 종이 6mm·4mm
                var t1 = new DBText
                {
                    TextString = $"토공 횡단면도({p + 1}/{nPages})",
                    Height = th1,
                    HorizontalMode = TextHorizontalMode.TextCenter,
                    Position = new Point3d(tcx, tcy, 0),
                    AlignmentPoint = new Point3d(tcx, tcy, 0),
                };
                if (!layT.IsNull) t1.LayerId = layT;
                if (!kst.IsNull) t1.TextStyleId = kst;
                ms.AppendEntity(t1); tr.AddNewlyCreatedDBObject(t1, true);
                var t2 = new DBText
                {
                    TextString = $"S=1:{scaleForTitle:F0}",
                    Height = th2,
                    HorizontalMode = TextHorizontalMode.TextCenter,
                    Position = new Point3d(tcx, tcy - th1 * 1.4, 0),
                    AlignmentPoint = new Point3d(tcx, tcy - th1 * 1.4, 0),
                };
                if (!layT.IsNull) t2.LayerId = layT;
                if (!kst.IsNull) t2.TextStyleId = kst;
                ms.AppendEntity(t2); tr.AddNewlyCreatedDBObject(t2, true);
                // 칸 경계 — 세로줄
                for (int c = 1; c < cols; c++)
                {
                    var l1 = new Line(new Point3d(ix + innerW / cols * c, iy, 0),
                                      new Point3d(ix + innerW / cols * c, iy + innerH, 0));
                    if (!layCell.IsNull) l1.LayerId = layCell;
                    ms.AppendEntity(l1); tr.AddNewlyCreatedDBObject(l1, true);
                }
                // 칸 경계 — 가로줄
                for (int r = 1; r < rows; r++)
                {
                    var l2 = new Line(new Point3d(ix, iy + innerH / rows * r, 0),
                                      new Point3d(ix + innerW, iy + innerH / rows * r, 0));
                    if (!layCell.IsNull) l2.LayerId = layCell;
                    ms.AppendEntity(l2); tr.AddNewlyCreatedDBObject(l2, true);
                }
            }
            tr.Commit();
        }
        catch (System.Exception ex) { log?.AppendLine("  도곽 그리기 실패 — " + ex.Message); }
        log?.AppendLine($"  도곽 {n}장 · 모형 {sheetW:F1}×{sheetH:F1}m ="
                      + $" <b>종이 A1 {SheetCommand.SheetW:F0}×{SheetCommand.SheetH:F0}mm</b>(축척 1:{scaleForTitle:F0})"
                      + $" — 인쇄하면 종단도와 <b>같은 크기</b>다. 모형에서만 축척만큼 다르게 보인다."
                      + $" · 여백 좌우 {SheetCommand.MarginLR:F0}·상 {SheetCommand.MarginTop:F0}·하 {SheetCommand.MarginBottom:F0}mm"
                      + $" · 레이어 '{XsecFrameLayer}' · 칸선 '{XsecCellLayer}'");
        return n;
    }

    /// <summary>횡단 도곽 전용 레이어 — ★종단도의 <c>DH-도곽범위(모형)</c>와 <b>따로 둔다</b>:
    /// 같이 쓰면 횡단을 지울 때 종단 도곽까지 지워진다.</summary>
    internal const string XsecFrameLayer = "DH-횡단-도곽";

    /// <summary>칸 경계선 레이어 — 한 칸에 하나씩 들어갔는지 대보는 자다. 인쇄에서는 끄면 된다.</summary>
    internal const string XsecCellLayer = "DH-횡단-칸";

    /// <summary>★★[JACK 0826 "상단도 50만 남기는 걸로 하자"] <b>횡단도 전용 제목 자리(종이 mm).</b>
    ///
    /// <para>종단도는 이 자리가 <b>104.8mm</b>다 — 축척 막대·배너가 들어가서다.
    /// 그런데 횡단도 제목은 <c>토공 횡단면도(1/15)</c>와 <c>S=1:200</c> <b>두 줄</b>뿐이라 그만큼이 필요 없다.
    /// 남는 54.8mm를 내부 네모에 돌리면 칸이 그만큼 커지고, <b>축척이 한 단계 살아난다</b>
    /// (1×2 기준 필요 축척 153 → 134.5 = 1:200에서 1:150).</para>
    ///
    /// <para>★종단도의 <c>ViewH</c>는 <b>건드리지 않는다</b> — 그쪽은 그 배분이 맞다.</para></summary>
    private const double XsecTitleMm = 50.0;

    /// <summary>횡단도 내부 네모 높이(종이 mm) — 검산: 하 50 + 474 + 제목 50 + 상 20 = 594 = A1 세로.</summary>
    private static double XsecInnerH =>
        SheetCommand.SheetH - SheetCommand.MarginTop - SheetCommand.MarginBottom - XsecTitleMm;

    /// <summary>칸선에서 사방으로 띄우는 여백(종이 mm). ★거터(칸 사이 틈)를 두는 대신 <b>칸 안쪽</b>에 둔다 —
    /// 칸 사이를 벌리면 그림 자리가 줄어 축척이 한 단계 밀린다(2mm 때문에 그림이 20% 작아지는 일이 생긴다).</summary>
    private const double CellPadMm = 4.0;

    /// <summary>측점 이름이 앉을 자리(뷰 아래, 종이 mm).</summary>
    private const double NameRoomMm = 6.0;

    /// <summary>그림과 수량표 사이 틈(종이 mm).</summary>
    private const double TableGapMm = 12.0;   // ★[JACK 0826] "그래프와의 간격도 조금만 더" — 5 → 12mm

    /// <summary>횡단면도 이름 글자 크기(종이 mm) — 도면 제목 관례는 3~5mm다.</summary>
    private const double NameTextMm = 3.5;

    /// <summary>수량표와 그림 사이 틈(종이 mm).</summary>
    private const double QtGapMm = 4.0;

    // ── 수량표 규격 ★★[JACK 0826] <b>열 너비를 글자에서 계산한다.</b>
    //   고정 mm로 두면 글자를 키울 때마다 어긋난다 — 실제로 글자가 칸을 뚫고 나왔다(JACK 스샷).
    //   한글은 글자 높이만큼, 영문·숫자는 절반쯤 차지하므로 <b>각 열에서 가장 긴 글자</b>로 배수를 잡는다.
    //     1열 '터 파 기(5.0m이하)' 5.6배 · 2열 '성토면' 3.6배 · 3열 '풍화암' 3.6배 · 4열 'NO. 0+5.000' 5.5배
    //   ★[JACK 0826 두 번째] 배수를 <b>넉넉히</b> 잡는다. 종전엔 글자 폭만 세고
    //   <b>셀 여백(좌우 각 15%)을 빼는 것을 잊어</b> 글자가 두 줄로 접혔다(육/상, 풍화/암).
    //   접히면 행이 높아지고 표가 세로로 길어져 칸을 넘본다 — 폭 부족이 높이 문제로 번진다.
    //     1열 '(5.0m이하)' = 기호·숫자 6 × 0.5 + 한글 2 × 1.0 = 5.0 → 여백까지 6.5
    //     2열 '성토면' = 3.0 → 4.2 · 3열 '풍화암' = 3.0 → 4.2
    //     4열 머리줄 'No.1+10.00' = 10자 × 0.5 = 5.0 → 6.0
    //   ★[JACK 0826 세 번째] 1열을 더 넓힌다 — <c>(5.0m이하)</c>가 아직 두 줄로 접힌다.
    //   괄호와 소수점까지 세면 실제로는 계산보다 넓게 잡아야 한다.
    //   ★[JACK 0826 스샷 "폭 넓일 것, 풍화암 한 줄로 표현할 것"] 2·3열을 더 넓힌다 —
    //   계산으로는 4.2배면 3글자가 들어가야 하는데 실제로는 접혔다. 글꼴 자간과 Table의
    //   자체 여백이 계산 밖에 있어서다 — <b>재 보고 넉넉히</b> 잡는 편이 낫다.
    private static readonly double[] QtColRatio = { 8.5, 5.5, 5.5, 6.0 };

    /// <summary>표 글자 높이(종이 mm). <b>A3로 줄여 찍어도 1.8mm</b>가 되도록 잡았다 —
    /// 제본 도서는 보통 A3이고(A1의 정확히 절반), 감리가 자로 재는 것도 종이다.</summary>
    private const double QtTextMm = 3.6;

    /// <summary>표 한 줄 높이(종이 mm) — 글자가 줄 안에서 숨 쉴 만큼.</summary>
    private const double QtRowH = 5.81;

    /// <summary>표 전체 높이(종이 mm) — <b>머리줄이 1.4배</b>인 것까지 센다.
    /// ★한 곳에서만 정한다: 자리 잡는 쪽과 그리는 쪽이 다르게 세면 표가 어긋난 자리에 앉는다.</summary>
    private static double QtTableHmm => QtRowH * (DH.Grading.Core.QuantityTable.TotalRows + 0.4);

    /// <summary>표 전체 폭(종이 mm) — 열 너비의 합이다. 축척 계산이 이 값을 쓴다.</summary>
    private static double QtWidthMm
    {
        get { double s = 0; foreach (double r in QtColRatio) s += r; return s * QtTextMm; }
    }
    internal const string QtLayerEdge = "DH-횡단-표(테두리)";   // 초록
    internal const string QtLayerLine = "DH-횡단-표(줄)";       // 빨강
    internal const string QtLayerText = "DH-횡단-표(글씨)";     // 흰색

    /// <summary>★★[JACK 0826 "표 크기와 글씨도 너무 잘 안 맞어 … 참고자료 설계검토를 참고해봐"]
    ///
    /// <para><b>AutoCAD <c>Table</c> 객체로 그린다.</b> 종전엔 선과 글자를 <b>따로</b> 그렸는데,
    /// 그러면 글자를 키워도 열 너비는 그대로라 <b>글자가 칸을 뚫고 나온다</b>(JACK 스샷: 육 상↔토 사 겹침).
    /// <c>Table</c>은 셀 병합·행 높이·열 너비·글자 정렬을 <b>한 몸으로</b> 관리하므로 그 일이 생기지 않는다.</para>
    ///
    /// <para>JACK의 설계검토 문서가 권한 구조 그대로다:
    /// <i>"QTO는 계산 엔진으로 쓰고, 최종 도면 표는 애드인이 AutoCAD Table로 직접 생성한다."</i>
    /// 지금은 값이 비어 있고(<c>–</c>), 나중에 수량이 계산되면 <b>셀 값만</b> 채우면 된다.</para>
    ///
    /// <para><b>열 너비는 글자에서 계산한다.</b> 고정 mm로 두면 글자 크기를 바꿀 때마다 어긋난다 —
    /// 한글은 글자 높이만큼, 영문·숫자는 절반쯤 차지하므로 가장 긴 글자로 폭을 잡는다.</para></summary>
    private static int DrawQtyTables(Database db, List<(ObjectId Id, double St, string Name)> views,
                                     double sc, bool onRight, double gapM,
                                     System.Collections.Generic.Dictionary<string, DH.Grading.Core.XsecQty> qty,
                                     System.Text.StringBuilder log)
    {
        if (views == null || views.Count == 0) return 0;
        var Q = DH.Grading.Core.QuantityTable.Rows;
        int nRow = Q.Length + 1;                       // 머리 1줄 + 내용
        double txtH = QtTextMm * sc;                   // 글자 높이(모형)
        double rowH = QtRowH * sc;                     // 줄 높이
        // 열 너비 — '터 파 기(5.0m이하)'·'풍화암'·'NO. 0+5.000'이 각 열의 가장 긴 글자다.
        var colW = new double[QtColRatio.Length];
        for (int c = 0; c < colW.Length; c++) colW[c] = QtColRatio[c] * txtH;
        int n = 0;
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
            var layE = SectionCommand.EnsureLayer(db, tr, QtLayerEdge, 3);
            var kst = ImportGisCommand.EnsureKoreanTextStyle(db, tr);
            if (kst.IsNull) log?.AppendLine("  ⚠한글 글꼴을 못 얻었다 — 표 글자가 ?로 찍힌다");
            var qtStyle = EnsureQtyTableStyle(db, txtH, kst);
            if (qtStyle.IsNull) log?.AppendLine("  ⚠수량표 전용 스타일을 못 만들었다 — 표가 여백만큼 커진다");

            foreach (var (vid, _, vname) in views)
            {
                try
                {
                    if (tr.GetObject(vid, OpenMode.ForRead) is not CivilDb.SectionView sv) continue;
                    var ext = ((Entity)sv).GeometricExtents;

                    var tb = new Table();
                    try { if (!qtStyle.IsNull) tb.TableStyle = qtStyle; } catch { }
                    tb.SetSize(nRow, 4);
                    if (!layE.IsNull) tb.LayerId = layE;
                    for (int c = 0; c < 4; c++) tb.Columns[c].Width = colW[c];
                    // ★[JACK 0826 스샷 "제목 셀은 … 셀 높이 조금 높일 것"]
                    //   머리줄만 <b>1.4배</b>로 — 표의 얼굴이라 다른 줄과 같으면 묻힌다.
                    for (int r = 0; r < nRow; r++) tb.Rows[r].Height = r == 0 ? rowH * 1.4 : rowH;

                    // 글자 모양은 셀 단위로 — 표 전체에 한 번에 건다.
                    for (int r = 0; r < nRow; r++)
                        for (int c = 0; c < 4; c++)
                        {
                            var cell = tb.Cells[r, c];
                            try { cell.TextHeight = txtH; } catch { }
                            try { if (!kst.IsNull) cell.TextStyleId = kst; } catch { }
                            try { cell.Alignment = CellAlignment.MiddleCenter; } catch { }
                            // ★[JACK 0826 "표 안 글씨는 흰색으로, 표는 초록색으로 두고"]
                            //   표(선)는 초록 레이어를 따르고, <b>글자만</b> 흰색으로 못 박는다.
                            try
                            {
                                cell.ContentColor = Autodesk.AutoCAD.Colors.Color.FromColorIndex(
                                    Autodesk.AutoCAD.Colors.ColorMethod.ByAci, 7);   // 7 = 흰색
                            }
                            catch { }
                        }

                    // ★★[JACK 0826 "토적표의 외곽선은 살짝 두껍게(초록색), 내부선은 모두 빨간색(얇은 선)"]
                    //   <c>Table</c>은 <b>바깥 테두리</b>(Top/Bottom/Left/Right)와
                    //   <b>안쪽 격자</b>(Horizontal/Vertical)를 따로 다룬다.
                    try
                    {
                        var green = Autodesk.AutoCAD.Colors.Color.FromColorIndex(
                            Autodesk.AutoCAD.Colors.ColorMethod.ByAci, 3);
                        var red = Autodesk.AutoCAD.Colors.Color.FromColorIndex(
                            Autodesk.AutoCAD.Colors.ColorMethod.ByAci, 1);
                        var all = tb.Cells;
                        foreach (var b in new[] { all.Borders.Top, all.Borders.Bottom,
                                                  all.Borders.Left, all.Borders.Right })
                        {
                            try { b.Color = green; b.LineWeight = LineWeight.LineWeight050; } catch { }
                        }
                        foreach (var b in new[] { all.Borders.Horizontal, all.Borders.Vertical })
                        {
                            try { b.Color = red; b.LineWeight = LineWeight.LineWeight013; } catch { }
                        }
                    }
                    catch { }

                    // ── 머리줄: '측 점'이 세 칸을 먹고, 오른쪽 칸에 측점 이름
                    // ★[JACK 0826] 머리줄을 <b>한 칸</b>으로 — <c>측 점(No.1+10.00)</c> 꼴.
                    //   측점명을 괄호 안에 넣으면 제목과 이름이 <b>한눈에 한 덩이</b>로 읽힌다.
                    try { tb.MergeCells(CellRange.Create(tb, 0, 0, 0, 3)); } catch { }
                    tb.Cells[0, 0].TextString =
                        DH.Grading.Core.QuantityTable.HeaderLeft
                        + (string.IsNullOrEmpty(vname) ? "" : $"({vname})");
                    // ★[JACK 0826 스샷 "제목 셀은 음각으로 표현(셀 채우기)"]
                    //   바탕을 칠하고 글자를 검게 — 인쇄하면 흰 바탕에 검은 글씨가 뒤집혀 <b>음각</b>이 된다.
                    for (int c = 0; c < 4; c++)
                    {
                        try
                        {
                            var h0 = tb.Cells[0, c];
                            h0.BackgroundColor = Autodesk.AutoCAD.Colors.Color.FromColorIndex(
                                Autodesk.AutoCAD.Colors.ColorMethod.ByAci, 3);    // 표와 같은 초록 바탕
                            h0.ContentColor = Autodesk.AutoCAD.Colors.Color.FromColorIndex(
                                Autodesk.AutoCAD.Colors.ColorMethod.ByAci, 250);  // 어두운 글자
                        }
                        catch { }
                    }
                    DH.Grading.Core.XsecQty q0 = default;
                    bool hasQty = vname != null && qty != null && qty.TryGetValue(vname, out q0);

                    // ── 내용 — 병합은 "이 칸이 몇 줄을 먹느냐"로 적혀 있다.
                    for (int r = 0; r < Q.Length; r++)
                    {
                        var q = Q[r];
                        int row = r + 1;
                        if (q.NameRows > 0)
                        {
                            int last = row + q.NameRows - 1;
                            // Sub이 null이면 항목 칸이 2열까지 가로로 넓어진다.
                            int colTo = q.Sub == null ? 1 : 0;
                            if (q.NameRows > 1 || colTo > 0)
                                try { tb.MergeCells(CellRange.Create(tb, row, 0, last, colTo)); } catch { }
                            tb.Cells[row, 0].TextString = (q.Name ?? "").Replace("|", "\\P");
                        }
                        if (q.Sub != null && q.SubRows > 0)
                        {
                            if (q.SubRows > 1)
                                try { tb.MergeCells(CellRange.Create(tb, row, 1, row + q.SubRows - 1, 1)); } catch { }
                            tb.Cells[row, 1].TextString = q.Sub;
                        }
                        tb.Cells[row, 2].TextString = q.Material ?? "";
                        // ★[JACK 0826] <b>값이 있으면 넣고 없으면 빈칸.</b>
                        //   0으로 채우면 "재 봤더니 없다"로 읽혀 잘못이다 — 아직 안 재는 공종은 비워 둔다.
                        double v = hasQty ? DH.Grading.Core.QuantityTable.Pick(q0, r) : double.NaN;
                        // ★[JACK 0826 "0.00으로 나오는 건 – 나오게"] 도면에서는 0도 빈칸으로 본다.
                        //   토공이 없는 자리에 0.00이 줄줄이 적히면 <b>읽는 사람이 훑기 어렵다</b>.
                        tb.Cells[row, 3].TextString = double.IsNaN(v) || System.Math.Abs(v) < 5e-3
                            ? DH.Grading.Core.QuantityTable.Blank
                            : v.ToString("0.00");
                    }

                    // 자리 — 표는 <b>왼쪽 위</b>가 기준이다.
                    // ★[JACK 0826 "표가 배치의 좀 상단에 위치해 있는데 중간쯤으로"]
                    //   표 <b>가운데</b>를 그림 <b>가운데</b>에 맞춘다 — 종전엔 표 위쪽을 그림 위쪽에 맞춰
                    //   표가 그림보다 짧으면 위로 몰려 보였다.
                    double px = onRight ? ext.MaxPoint.X + gapM : ext.MinPoint.X;
                    double midY = (ext.MinPoint.Y + ext.MaxPoint.Y) / 2.0;
                    double py = onRight ? midY + rowH * (nRow + 0.4) / 2.0 : ext.MinPoint.Y - gapM;
                    tb.Position = new Point3d(px, py, 0);
                    // ★[JACK 0826] <c>GenerateLayout()</c>을 <b>부르지 않는다</b> — 그것이 행 높이를
                    //   글자와 여백에서 <b>다시 계산</b>해, 우리가 지정한 높이를 덮어쓴다.
                    ms.AppendEntity(tb); tr.AddNewlyCreatedDBObject(tb, true);
                    n++;
                }
                catch { }
            }
            tr.Commit();
        }
        catch (System.Exception ex) { log?.AppendLine("  수량표 그리기 실패 — " + ex.Message); }
        double tw = 0; foreach (double w in colW) tw += w;
        log?.AppendLine($"  수량표 {n}개 · {nRow}줄 · AutoCAD Table 객체(셀 병합·열 너비를 표가 관리한다)"
                      + $" · 글자 {QtTextMm:0.##}mm · 줄 {QtRowH:0.##}mm · 폭 {tw / sc:F0}mm"
                      + $" (값은 아직 '{DH.Grading.Core.QuantityTable.Blank}')");
        return n;
    }

    /// <summary>★★[JACK 0826 "횡단뷰용 단면검토선은 <b>어느 순간에도</b> 안 보였으면 좋겠어"]
    ///
    /// <para><b>왜 어려웠나.</b> 이 선은 세 곳에서 그려진다 — 객체 자신, <c>SampleLineStyle</c>(자기 레이어에 그린다),
    /// 그리고 딸린 <b>측점 라벨</b>. 레이어만 옮기거나 스타일만 끄면 나머지 하나가 남아
    /// 종전엔 <i>"레이어·Visible·스타일 셋 다 안 먹었다"</i>는 결론이 났었다 —
    /// 사실은 <b>셋을 다 껐어야</b> 했다.</para>
    ///
    /// <para><b>뷰를 다 만든 뒤에</b> 부른다. 검토선은 횡단면도의 뿌리라, 만들기 전에 숨기면
    /// 뷰 생성이 걸릴 수 있다. 다 만든 뒤에는 뷰가 기하 데이터를 따로 들고 있어 지장이 없다.</para>
    ///
    /// <para>평면도를 <b>계획평면도로 그대로 쓸 것</b>이므로(JACK), 이 선이 한 겹이라도 남으면 안 된다.</para></summary>
    private static int HideSampleLines(Database db, CivilApp.CivilDocument cdoc,
                                       List<(ObjectId Id, string Name, double St, double Mother, int Ord)> sl,
                                       ObjectId groupId, System.Text.StringBuilder log)
    {
        if (sl == null || sl.Count == 0) return 0;
        ObjectId hideStyle = ObjectId.Null;
        try { hideStyle = SectionCommand.EnsureHiddenSampleLineStyle(db, cdoc); } catch { }

        // ★★★[JACK 0827 "횡단 객체를 격리했다 복귀하면 종단에 빨간 측점선이 엄청 생겨"]
        //   <b>원인: AutoCAD의 객체 격리가 쓰는 스위치가 바로 <c>Entity.Visible</c>이다.</b>
        //   그래서 "복귀"는 격리했던 것만이 아니라 <b>우리가 숨겨 둔 것까지 전부 켠다</b> —
        //   우리가 잠가 둔 문을 남이 같은 열쇠로 열어젖히는 셈이다.
        //
        //   <b>레이어를 끄면 견딘다.</b> 레이어 상태는 격리·복귀가 건드리지 않는 별개의 층이다.
        //   <see cref="ProfileCommand"/>는 <b>이미 그렇게 하고 있었다</b>(스타일+레이어끄기+Visible 세 겹).
        //   그런데 여기는 <b>두 겹</b>뿐이라 갈라졌다 — 또 "같은 일을 두 곳에서 따로" 한 대가다.
        //   → 같은 레이어(<c>ProfileCommand.XsecHiddenLayer</c>)를 쓴다. 자를 하나로 만든다.
        ObjectId hideLayer = ObjectId.Null;
        try
        {
            using var trL = db.TransactionManager.StartTransaction();
            hideLayer = SectionCommand.EnsureLayer(db, trL, ProfileCommand.XsecHiddenLayer, 8);
            trL.Commit();
        }
        catch { }

        int nStyle = 0, nVis = 0, nLbl = 0, nLay = 0;
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            foreach (var s in sl)
            {
                try
                {
                    if (tr.GetObject(s.Id, OpenMode.ForWrite) is not CivilDb.SampleLine ln) continue;

                    // ① 스타일 — 선·꼭짓점을 안 그리는 전용 스타일로 바꾼다.
                    if (!hideStyle.IsNull) { try { ln.StyleId = hideStyle; nStyle++; } catch { } }

                    // ② 레이어 — <b>격리 복귀가 못 건드리는 겹</b>. 이것이 진짜 자물쇠다.
                    if (!hideLayer.IsNull && ln.LayerId != hideLayer)
                    { try { ln.LayerId = hideLayer; nLay++; } catch { } }

                    // ③ 객체 자신 — 보조. 격리 복귀에 되살아나므로 <b>여기에만 기대면 안 된다</b>.
                    if (ln.Visible) { ln.Visible = false; nVis++; }
                }
                catch { }
            }
            // ② 라벨 — 측점 글씨는 검토선과 <b>따로</b> 산다(그룹이 들고 있다).
            //   이걸 안 끄면 선은 사라져도 <b>글씨가 남는다</b> — 종전에 "안 먹었다"고 본 것이 이 겹이다.
            try
            {
                if (tr.GetObject(groupId, OpenMode.ForRead) is CivilDb.SampleLineGroup grp)
                    foreach (ObjectId lg in grp.GetAvailableSampleLineLabelGroupIds())
                    {
                        try
                        {
                            if (tr.GetObject(lg, OpenMode.ForWrite) is not Entity le) continue;
                            // 라벨도 같은 레이어로 — 측점 글씨와 지시선이 여기 산다.
                            if (!hideLayer.IsNull && le.LayerId != hideLayer)
                            { try { le.LayerId = hideLayer; nLay++; } catch { } }
                            if (le.Visible) { le.Visible = false; nLbl++; }
                        }
                        catch { }
                    }
            }
            catch { }
            tr.Commit();
        }
        catch (System.Exception ex) { log?.AppendLine("  검토선 숨기기 실패 — " + ex.Message); }

        // ④ 그 레이어를 <b>끄고 동결한다</b>.
        //
        //   ★★★[JACK 0827 "격리 후 복귀하면 종단에 세로 측점선이 엄청 많이 생겨"]
        //   <b>원인: 우리가 켜 둔 '단면검토선 자리 격자선'이 횡단용 검토선까지 본다.</b>
        //   종단뷰는 <b>그 선형에 달린 검토선을 전부</b> 보고 자리마다 세로줄을 긋는데,
        //   횡단용은 측점마다 본체·(전)·(후) 셋이라 <b>세 배</b>로 늘어난다.
        //   평소엔 검토선이 숨어 있어 Civil이 줄을 안 긋다가, 격리 복귀로 보이게 되면 전부 긋는다.
        //
        //   <b>끄기(Off)로는 모자랐다.</b> 끈 레이어는 평면에서 안 보일 뿐 <b>여전히 살아 있어</b>
        //   격자선이 그 자리를 찾아낸다. <b>동결(Freeze)</b>은 다르다 — 동결된 레이어의 객체는
        //   화면 재생성 자체에서 빠지므로 격자선도 자리를 못 찾는다. 격리·복귀도 동결은 안 건드린다.
        //   (검토선은 이미 다 쓴 뒤다 — 횡단면도는 만들어졌고, 기하 데이터를 읽는 경로는 별개다.)
        bool layOff = false, layFrozen = false;
        try
        {
            using var trO = db.TransactionManager.StartTransaction();
            var lt = (LayerTable)trO.GetObject(db.LayerTableId, OpenMode.ForRead);
            if (lt.Has(ProfileCommand.XsecHiddenLayer))
            {
                ObjectId lid = lt[ProfileCommand.XsecHiddenLayer];
                var lr = (LayerTableRecord)trO.GetObject(lid, OpenMode.ForWrite);
                if (!lr.IsOff) lr.IsOff = true;
                layOff = true;
                // ★현재 레이어는 동결할 수 없다 — 그 경우 조용히 넘어간다(우리 전용이라 그럴 일은 없다).
                try { if (lid != db.Clayer && !lr.IsFrozen) lr.IsFrozen = true; layFrozen = lr.IsFrozen; }
                catch { layFrozen = false; }
            }
            trO.Commit();
        }
        catch { }

        log?.AppendLine($"  횡단용 검토선 숨김 — 스타일 {nStyle}개 · 라벨 {nLbl}개 · 객체 {nVis}개"
                      + $" · 레이어 옮김 {nLay}개 · 끄기 {(layOff ? "O" : "X")} · <b>동결 {(layFrozen ? "O" : "X")}</b>"
                      + (layFrozen ? "" : "  ⚠동결이 안 됐다 — 격리 복귀 뒤 종단에 세로줄이 쏟아질 수 있다")
                      + (hideStyle.IsNull ? "  ⚠숨김 스타일을 못 만들었다(객체만 숨겼다)" : "")
                      + $"  (평면도를 계획평면도로 쓰므로 한 겹도 남으면 안 된다)");
        return nVis;
    }

    /// <summary>★★[JACK 0826] <b>만들어진 뷰들의 실제 크기</b>를 잰다 — 짐작하지 않는다.
    ///
    /// <para>종전엔 <c>좌우폭×1.6</c>, <c>지표면 전체 표고범위×2.2</c>로 <b>짐작</b>했다.
    /// 그런데 원지반은 부지 전체를 덮으니 표고 범위가 52m나 나오는 반면,
    /// 횡단 한 장이 잡는 것은 <b>그 측점 좌우 60m 안</b>뿐이다 — 세로를 <b>일곱 배</b> 부풀려 잡고 있었다.
    /// 엉뚱한 자로 재고 있었던 것이지 API가 모자란 것이 아니다.</para>
    ///
    /// <para><b>경계상자를 그대로 써도 안전하다.</b> 종단도는 이걸로 크게 데었지만(축척을 걸고 다시 재니
    /// 여분이 축척 배수로 부풀어 8배 어긋났다), 그건 <b>축척 걸기와 재기가 한 고리 안에</b> 있었기 때문이다.
    /// 우리는 <b>주석 축척을 건드리지 않으므로</b> 그 고리가 없다 — 뷰 크기는 재는 동안 변하지 않는다.</para></summary>
    /// <returns>가장 큰 뷰의 가로·세로(모형 m)와, 잰 뷰 수.</returns>
    private static (double W, double H, int N) MeasureViews(Database db,
        List<(ObjectId Id, double St, string Name)> views, System.Text.StringBuilder log)
    {
        double w = 0, h = 0; int n = 0;
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            foreach (var (vid, _, _) in views)
            {
                try
                {
                    if (tr.GetObject(vid, OpenMode.ForRead) is not Entity e) continue;
                    var ext = e.GeometricExtents;
                    double ew = ext.MaxPoint.X - ext.MinPoint.X;
                    double eh = ext.MaxPoint.Y - ext.MinPoint.Y;
                    if (ew > w) w = ew;
                    if (eh > h) h = eh;
                    n++;
                }
                catch { }
            }
            tr.Commit();
        }
        catch (System.Exception ex) { log?.AppendLine("  뷰 크기 재기 실패 — " + ex.Message); }
        return (w, h, n);
    }

    /// <summary>그림이 자리에 들어가는 <b>표준 축척</b>을 고른다 — 종단도와 같은 사다리를 쓴다.
    ///
    /// <para><c>종이 mm = 모형 m × 1000 ÷ 축척</c>이므로, 뒤집으면
    /// <c>필요 축척 = 모형 m × 1000 ÷ 종이 mm</c>다. 가로·세로 중 <b>엄한 쪽</b>이 이긴다.</para>
    ///
    /// <para>사다리에서 <b>그 값 이상인 첫 값</b>을 고른다 — 작은 쪽을 고르면 넘친다.
    /// <para>★<b>여유 비율을 곱하지 않는다.</b> 곱하면 사다리 올림과 겹쳐 <b>두 번 깎인다</b> —
    /// 종단도 v23.5가 <c>143 → 155 → 200</c>으로 겪은 사고다(8% 여백을 사려고 28%를 잃었다).
    /// 여백은 <b>올림이 남기는 몫</b>으로 저절로 생긴다.</para></summary>
    private static double PickScale(double wM, double hM, double wMm, double hMm)
    {
        if (wM <= 0 || hM <= 0 || wMm <= 1 || hMm <= 1) return 0;
        // ★★[검토] <b>여기에 여백 비율을 곱하지 않는다.</b> 곱하면 그 뒤 사다리 <b>올림</b>이
        //   한 번 더 들어가 두 번 깎인다 — 종단도가 v23.5에 겪은 사고가 정확히 이것이다:
        //   <c>143 → (버퍼)155 → (올림)200</c>. 8% 여백을 사려고 <b>28%를 잃었다.</b>
        //   여백은 <b>사다리 올림이 남기는 몫</b>으로 저절로 생긴다(실측 76~91% 사용).
        double need = System.Math.Max(wM * 1000.0 / wMm, hM * 1000.0 / hMm);
        foreach (double s in SheetCommand.Scales) if (s >= need - 1e-9) return s;
        return 0;   // 사다리 끝까지 없으면 호출부가 판단한다
    }

    /// <summary>이만큼도 못 채우면 <b>로그로 알리는</b> 경고선 — 축척 계산에 <b>곱하지 않는다</b>.
    /// 곱하면 사다리 올림과 겹쳐 두 번 깎인다(종단도 v23.5 사고).</summary>
    private const double WarnFill = 0.55;

    /// <summary>수량표 전용 표 스타일 — <b>셀 여백을 우리가 정한다.</b>
    ///
    /// <para>★★[JACK 0826 "표의 크기가 각 횡단뷰의 범위를 넘어가고"] <b>원인은 여백이었다.</b>
    /// 도면 기본 표 스타일은 셀마다 넉넉한 여백을 붙이는데, 그게 <b>19줄에 곱해져</b>
    /// 표가 지정한 높이의 두 배 가까이 커졌다. 행 높이를 아무리 지정해도 여백은 그 위에 더해진다.</para>
    ///
    /// <para>여백을 글자 높이의 <b>15%</b>로 잡는다 — 0으로 두면 글자가 선에 닿아 읽기 나쁘다.</para></summary>
    private static ObjectId EnsureQtyTableStyle(Database db, double txtH, ObjectId textStyle)
    {
        const string styleName = "DH-수량표";
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            var dict = (DBDictionary)tr.GetObject(db.TableStyleDictionaryId, OpenMode.ForRead);
            ObjectId id;
            if (dict.Contains(styleName))
            {
                id = dict.GetAt(styleName);
                if (tr.GetObject(id, OpenMode.ForWrite) is TableStyle old) Apply(old);
            }
            else
            {
                var ts = new TableStyle();
                Apply(ts);
                dict.UpgradeOpen();
                id = dict.SetAt(styleName, ts);
                tr.AddNewlyCreatedDBObject(ts, true);
            }
            tr.Commit();
            return id;

            void Apply(TableStyle ts)
            {
                try { ts.HorizontalCellMargin = txtH * 0.15; } catch { }
                try { ts.VerticalCellMargin = txtH * 0.15; } catch { }
                foreach (var rt in new[] { RowType.TitleRow, RowType.HeaderRow, RowType.DataRow })
                {
                    try { ts.SetTextHeight(txtH, (int)rt); } catch { }
                    try { if (!textStyle.IsNull) ts.SetTextStyle(textStyle, (int)rt); } catch { }
                    try { ts.SetAlignment(CellAlignment.MiddleCenter, (int)rt); } catch { }
                    // ★★[JACK 0826 "표 테두리도 안 됐어"] <c>Cells.Borders</c>가 안 먹어서
                    //   <b>표 스타일의 격자선</b>으로 옮긴다 — 바깥과 안쪽을 따로 잡을 수 있다.
                    var gGreen = Autodesk.AutoCAD.Colors.Color.FromColorIndex(
                        Autodesk.AutoCAD.Colors.ColorMethod.ByAci, 3);
                    var gRed = Autodesk.AutoCAD.Colors.Color.FromColorIndex(
                        Autodesk.AutoCAD.Colors.ColorMethod.ByAci, 1);
                    foreach (var gl in new[] { GridLineType.HorizontalTop, GridLineType.HorizontalBottom,
                                               GridLineType.VerticalLeft, GridLineType.VerticalRight })
                    {
                        try { ts.SetGridColor(gGreen, (int)gl, (int)rt); } catch { }
                        try { ts.SetGridLineWeight(LineWeight.LineWeight050, (int)gl, (int)rt); } catch { }
                        try { ts.SetGridVisibility(true, (int)gl, (int)rt); } catch { }
                    }
                    foreach (var gl in new[] { GridLineType.HorizontalInside, GridLineType.VerticalInside })
                    {
                        try { ts.SetGridColor(gRed, (int)gl, (int)rt); } catch { }
                        try { ts.SetGridLineWeight(LineWeight.LineWeight013, (int)gl, (int)rt); } catch { }
                        try { ts.SetGridVisibility(true, (int)gl, (int)rt); } catch { }
                    }
                }
            }
        }
        catch { return ObjectId.Null; }
    }

    /// <summary>지금 뷰들이 쓰는 스타일 — 눈금을 손보려면 <b>실제로 붙은 스타일</b>을 알아야 한다.</summary>
    /// <summary>★★★[JACK 0827 · 검토 지적] <b>"횡단용 검토선 그룹인가"를 재는 자는 하나뿐이다.</b>
    ///
    /// <para>종전엔 <b>두 곳이 서로 다른 자</b>를 들고 있었다 — 지우는 쪽(<c>WipeOldGroups</c>)은
    /// <c>DH횡단_횡단</c>으로, 종단 세로줄에서 빼는 쪽은 <c>_단면</c>으로 쟀다.
    /// 그런데 [횡단도]가 실제로 만드는 이름은 <c>DH횡단_횡단_1</c>이라
    /// <b>빼는 쪽 자에는 아예 안 걸렸다</b> — 이 프로젝트가 반복해 데인 그 함정이다.</para>
    ///
    /// <para><c>_단면</c>도 함께 본다 — <see cref="ProfileCommand"/>가 옛 경로에서 그 이름으로 만든다.
    /// 둘 다 "횡단면도용"이라는 점에서는 같으므로 <b>빼는 자</b>는 둘을 다 잡아야 한다.</para></summary>
    internal static bool IsXsecGroupName(string name)
        => name != null
        && (name.StartsWith(SectionCommand.GroupBase + "_횡단", System.StringComparison.Ordinal)
         || name.StartsWith(SectionCommand.GroupBase + "_단면", System.StringComparison.Ordinal));

    private static ObjectId XsecStyleId(Database db, List<(ObjectId Id, double St, string Name)> views)
    {
        if (views == null || views.Count == 0) return ObjectId.Null;
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            ObjectId r = ObjectId.Null;
            foreach (var (vid, _, _) in views)
            {
                try { if (tr.GetObject(vid, OpenMode.ForRead) is CivilDb.SectionView sv) { r = sv.StyleId; break; } }
                catch { }
            }
            tr.Commit();
            return r;
        }
        catch { return ObjectId.Null; }
    }

    /// <summary>눈금 크기(종이 mm) — ★[JACK 0826 "주눈금과 보조눈금도 좀 키워, 너무 작아"].</summary>
    private const double TickMajorMm = 3.0;
    private const double TickMinorMm = 1.5;

    /// <summary>눈금값 글자 크기(종이 mm) — 도면 글자 관례 2.5mm.</summary>
    private const double TickTextMm = 2.5;

    /// <summary>눈금값을 축에서 띄우는 거리(종이 mm). ★<b>배치·축척과 무관한 고정값</b>이다 —
    /// 종이 기준이라 어떤 축척에서도 종이에서 같은 거리로 보인다.</summary>
    private const double TickOffsetMm = 12.0;

    /// <summary>★★[JACK 0826] <b>표고축 눈금을 키우고 축과 같은 색으로 맞춘다.</b>
    ///
    /// <para>JACK: <i>"주눈금과 보조눈금도 좀 키워, 너무 작아. 눈금들은 색도 축색하고 통일하고(지금은 하얀색임)."</i></para>
    ///
    /// <para>축은 다섯 자리에 설 수 있다(왼쪽·오른쪽·위·아래·<b>가운데</b>). 회사 스타일은
    /// <b>가운데 축</b>을 쓰지만, 어느 것이 켜져 있는지 모르므로 <b>다섯 곳 모두</b> 같은 값으로 맞춘다 —
    /// 켜지지 않은 축을 건드려도 화면에 아무 일이 없다.</para>
    ///
    /// <para><b>색은 축에서 읽어 눈금에 옮긴다.</b> 우리가 색을 정하지 않는다 —
    /// 회사가 축을 무슨 색으로 정했든 눈금이 그것을 따라가야 한 몸으로 보인다.</para></summary>
    private static void TuneAxisTicks(Database db, ObjectId styleId, System.Text.StringBuilder log)
    {
        if (styleId.IsNull) return;
        int nSize = 0, nColor = 0; string axisName = "?"; double vexag = double.NaN;
        var axNote = new System.Text.StringBuilder();
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            if (tr.GetObject(styleId, OpenMode.ForWrite) is CivilDb.Styles.SectionViewStyle st)
            {
                axisName = st.Name;
                try
                {
                    using var gs = st.GraphStyle;
                    if (gs != null) vexag = gs.VerticalExaggeration;
                }
                catch { }
                // ① 크기 — 다섯 축 모두
                // ★[JACK 0826 "중심이 먹어야 하는데 왼쪽 오른쪽만 먹었어"]
                //   축마다 <b>이름과 결과</b>를 남긴다 — 어느 축이 안 먹는지 로그로 갈린다.
                // ★★★[JACK 0827 "x간격띄우기는 아직도 해결 안 됨"] <b>세 시점을 각각 잰다.</b>
                //   ① 쓴 직후 <b>같은 객체</b>에서 되읽기 ② <b>스타일에서 새로 꺼내</b> 읽기
                //   ③ 커밋 뒤 새 트랜잭션에서 읽기.
                //   ①만 맞고 ②가 틀리면 <b>우리가 쥔 것이 스타일이 아니라 복사본</b>이라는 뜻이다 —
                //   그러면 아무리 써도 화면에 안 간다. 어느 시점에 값이 새는지 여기서 갈린다.
                //   <b>델리게이트로 꺼낸다</b> — 매번 새로 꺼내야 ②를 잴 수 있기 때문이다.
                //   <b><c>using</c>을 붙인다</b> — 이 계열 스타일은 <c>GraphStyle</c>처럼
                //   Dispose할 때 값이 실제로 반영되는 것들이 있다(같은 파일에서 이미 그렇게 쓰고 있다).
                double wantOff = TickOffsetMm / 1000.0;
                var axes = new (string Nm, System.Func<CivilDb.Styles.AxisStyle> Get)[]
                {
                    ("중심", () => st.CenterAxis), ("왼쪽", () => st.LeftAxis), ("오른쪽", () => st.RightAxis),
                    ("위", () => st.TopAxis), ("아래", () => st.BottomAxis),
                };
                foreach (var (axNm, get) in axes)
                {
                    string r1 = "?", r2 = "?";
                    double b1 = double.NaN;
                    try
                    {
                        using (var ax = get())
                        {
                            if (ax == null) { axNote.Append($" {axNm}=없음"); continue; }
                            try { ax.MajorTickStyle.Size = TickMajorMm / 1000.0; nSize++; r1 = "크기O"; }
                            catch (System.Exception e1) { r1 = "크기X(" + e1.GetType().Name + ")"; }
                            try { ax.MinorTickStyle.Size = TickMinorMm / 1000.0; } catch { }
                            try { ax.MajorTickStyle.TextHeight = TickTextMm / 1000.0; } catch { }
                            try { ax.MajorTickStyle.Justification = CivilDb.Styles.AxisTickJustificationType.BottomOrRight; } catch { }
                            try { ax.MajorTickStyle.OffsetX = wantOff; b1 = ax.MajorTickStyle.OffsetX; }
                            catch (System.Exception e2) { r2 = "띄우기X(" + e2.GetType().Name + ")"; }
                        }   // ← 여기서 Dispose된다. 반영이 여기 걸려 있다면 이 뒤라야 보인다.

                        if (r2 == "?")
                        {
                            double b2 = double.NaN;
                            try { using (var ax2 = get()) b2 = ax2.MajorTickStyle.OffsetX; } catch { }
                            bool ok1 = System.Math.Abs(b1 - wantOff) < 1e-9;
                            bool ok2 = System.Math.Abs(b2 - wantOff) < 1e-9;
                            r2 = ok1 && ok2 ? "띄우기O"
                               : ok1 ? $"띄우기X(쓴 직후는 {b1 * 1000:F1}인데 <b>새로 꺼내니 {b2 * 1000:F1}mm</b> — 복사본을 쥐고 있었다)"
                               : $"띄우기X(쓴 직후부터 {b1 * 1000:F1}mm)";
                        }
                    }
                    catch (System.Exception e3) { r2 = "띄우기X(" + e3.GetType().Name + ")"; }
                    axNote.Append($" {axNm}:{r1}·{r2}");
                }

                // ② 색 — ★[JACK 0826 "축선과 눈금은 빨간색으로 해 줘"] 축에서 읽지 않고
                //   <b>우리가 빨강으로 못 박는다</b>. 종전엔 축 색을 읽어 눈금에 옮겼는데,
                //   회사 스타일 축이 흰색이라 눈금도 흰색이 됐다.
                var red = Autodesk.AutoCAD.Colors.Color.FromColorIndex(
                    Autodesk.AutoCAD.Colors.ColorMethod.ByAci, 1);   // 1 = 빨강
                var pairs = new[]
                {
                    (CivilDb.Styles.SectionViewDisplayStyleType.CenterAxis,
                     CivilDb.Styles.SectionViewDisplayStyleType.CenterAxisTicksMajor,
                     CivilDb.Styles.SectionViewDisplayStyleType.CenterAxisTicksMinor),
                    (CivilDb.Styles.SectionViewDisplayStyleType.LeftAxis,
                     CivilDb.Styles.SectionViewDisplayStyleType.LeftAxisTicksMajor,
                     CivilDb.Styles.SectionViewDisplayStyleType.LeftAxisTicksMinor),
                    (CivilDb.Styles.SectionViewDisplayStyleType.RightAxis,
                     CivilDb.Styles.SectionViewDisplayStyleType.RightAxisTicksMajor,
                     CivilDb.Styles.SectionViewDisplayStyleType.RightAxisTicksMinor),
                };
                foreach (var (axT, majT, minT) in pairs)
                {
                    try
                    {
                        using var dsAx = st.GetDisplayStylePlan(axT);
                        // ★★[JACK 0826 "저 가로로 그어진 빨간 줄 좀 어떻게 해봐"]
                        //   <b>원인: 내가 켰다.</b> 색을 바꾸면서 <c>Visible = true</c>를 같이 줬는데,
                        //   회사 스타일이 <b>꺼 놓은 축과 눈금</b>이 그 바람에 전부 켜졌다.
                        //   → <b>색만 바꾼다.</b> 무엇을 보일지는 회사 스타일이 정한 대로 둔다.
                        if (dsAx != null) dsAx.Color = red;
                        foreach (var tk in new[] { majT, minT })
                        {
                            try
                            {
                                using var dsTk = st.GetDisplayStylePlan(tk);
                                if (dsTk == null) continue;
                                dsTk.Color = red;
                                nColor++;
                            }
                            catch { }
                        }
                    }
                    catch { }
                }
            }
            tr.Commit();
        }
        catch (System.Exception ex) { log?.AppendLine("  눈금 손질 실패 — " + ex.Message); }
        // ★[JACK 0826 "이 면적이 축척하고도 관련이 있나?"] <b>수직 과장</b>을 남긴다 —
        //   과장이 1배가 아니면 도면에서 자로 잰 면적(BO 등)이 <b>그 배수만큼 부푼다</b>.
        //   우리 계산은 지표면의 실제 표고를 쓰므로 과장과 무관하다 — 어느 쪽이 맞는지 이 줄로 갈린다.
        // ★★[JACK 0827 "X 간격 띄우기도 중심이 아직도 여전히 7mm야"]
        //   <b>같은 트랜잭션 안에서 되읽으면 성공으로 보인다</b> — 쓴 값이 아직 메모리에 있어서다.
        //   커밋한 <b>뒤에 새 트랜잭션</b>으로 다시 읽어야 진짜 남았는지 알 수 있다.
        try
        {
            using var trV = db.TransactionManager.StartTransaction();
            if (trV.GetObject(styleId, OpenMode.ForRead) is CivilDb.Styles.SectionViewStyle st2)
            {
                double got = double.NaN;
                try { got = st2.CenterAxis.MajorTickStyle.OffsetX; } catch { }
                double want = TickOffsetMm / 1000.0;
                axNote.Append(double.IsNaN(got) ? "  [커밋 뒤 중심축을 못 읽었다]"
                    : System.Math.Abs(got - want) < 1e-9
                        ? $"  [커밋 뒤 확인: 중심 {got * 1000:F1}mm — 남았다]"
                        : $"  ⚠[커밋 뒤 확인: 중심 {got * 1000:F1}mm — 쓴 값 {TickOffsetMm:F0}mm이 <b>안 남았다</b>]");
            }
            trV.Commit();
        }
        catch { }
        log?.AppendLine($"  눈금 — 스타일 '{axisName}' · 크기 주 {TickMajorMm:0.#}mm·보조 {TickMinorMm:0.#}mm({nSize}축)"
                      + $" · 색 {nColor}곳 ·{axNote}"
                      + (double.IsNaN(vexag) ? "" : $" · 수직과장 {vexag:0.##}배"
                        + (System.Math.Abs(vexag - 1.0) > 0.01
                           ? "  ⚠도면에서 자로 잰 면적은 이 배수만큼 부푼다(우리 계산은 실제 표고라 무관)"
                           : "  (1배 — 도면에서 잰 면적과 계산이 같아야 한다)")));
    }

    /// <summary>★★[JACK 0826] <b>횡단뷰의 선 색을 정한다.</b>
    ///
    /// <para>JACK: <i>"원지반은 초록색, 계획지표면은 흰색, 터파기는 마젠타로. 이 중 터파기만 점선으로
    /// 가고 나머지는 실선으로."</i></para>
    ///
    /// <para>횡단면의 선은 <c>Section</c> 객체가 그리고, 그 모양은 <c>SectionStyle</c>이 정한다.
    /// 어느 지표면에서 온 단면인지는 <c>SourceName</c>이 알려 주므로 <b>이름으로 골라</b> 스타일을 건다.</para>
    ///
    /// <para><b>점선은 도면에 실려 있어야 쓸 수 있다</b> — 없으면 표준 파일에서 불러온다(종단도와 같은 수법).</para></summary>
    private static int ApplySectionStyles(Database db, CivilApp.CivilDocument cdoc,
                                          List<(ObjectId Id, string Name, double St, double Mother, int Ord)> sl,
                                          System.Collections.Generic.Dictionary<ObjectId, string> kindOf,
                                          System.Text.StringBuilder log)
    {
        if (sl == null || sl.Count == 0) return 0;

        // ── 점선 확보
        string dash = null;
        try
        {
            using var trT = db.TransactionManager.StartTransaction();
            var lt = (LinetypeTable)trT.GetObject(db.LinetypeTableId, OpenMode.ForRead);
            foreach (string nm in new[] { "DASHED", "HIDDEN", "CENTER" })
            {
                if (lt.Has(nm)) { dash = nm; break; }
                try { db.LoadLineTypeFile(nm, "acadiso.lin"); } catch { }
                try { db.LoadLineTypeFile(nm, "acad.lin"); } catch { }
                if (lt.Has(nm)) { dash = nm; break; }
            }
            trT.Commit();
        }
        catch { }

        // ── 스타일 셋 — 이미 있으면 색만 다시 맞춘다(도면에 스타일이 쌓이지 않게).
        ObjectId Ensure(string nm, short aci, bool dashed)
        {
            try
            {
                var coll = cdoc.Styles.SectionStyles;
                ObjectId id = ObjectId.Null;
                foreach (ObjectId sid in coll)
                {
                    using var t0 = db.TransactionManager.StartTransaction();
                    try { if (t0.GetObject(sid, OpenMode.ForRead) is CivilDb.Styles.SectionStyle s0 && s0.Name == nm) id = sid; }
                    catch { }
                    t0.Commit();
                    if (!id.IsNull) break;
                }
                if (id.IsNull) id = coll.Add(nm);
                using var tr = db.TransactionManager.StartTransaction();
                if (tr.GetObject(id, OpenMode.ForWrite) is CivilDb.Styles.SectionStyle st)
                    foreach (var ty in new[]
                    {
                        CivilDb.Styles.SectionDisplayStyleSectionType.Segments,
                        CivilDb.Styles.SectionDisplayStyleSectionType.Points,
                    })
                    {
                        try
                        {
                            using var ds = st.GetDisplayStyleSection(ty);
                            if (ds == null) continue;
                            ds.Color = Autodesk.AutoCAD.Colors.Color.FromColorIndex(
                                Autodesk.AutoCAD.Colors.ColorMethod.ByAci, aci);
                            ds.Visible = ty == CivilDb.Styles.SectionDisplayStyleSectionType.Segments;
                            if (dashed && dash != null) { try { ds.Linetype = dash; } catch { } }
                        }
                        catch { }
                    }
                tr.Commit();
                return id;
            }
            catch { return ObjectId.Null; }
        }

        var stGround = Ensure("DH_횡단면_원지반", 3, false);      // 3 = 초록
        var stPlan   = Ensure("DH_횡단면_계획", 7, false);        // 7 = 흰색
        var stExcav  = Ensure("DH_횡단면_터파기", SectionCommand.ExcavAci, true);   // 6 = 마젠타, 점선

        int nG = 0, nP = 0, nE = 0, nSkip = 0;
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            foreach (var s in sl)
            {
                try
                {
                    if (tr.GetObject(s.Id, OpenMode.ForRead) is not CivilDb.SampleLine ln) continue;
                    foreach (ObjectId secId in ln.GetSectionIds())
                    {
                        try
                        {
                            if (tr.GetObject(secId, OpenMode.ForWrite) is not CivilDb.Section sec) continue;
                            // ★★[JACK 0826 "원지반도 파란색에 점선이야"] <b>원인: 이름으로 골랐다.</b>
                            //   원지반 지표면 이름이 <c>Surface1</c>이라 "원지반"이라는 글자가 없었다 —
                            //   못 가른 28개가 아무 색도 못 받아 기본 파란 점선으로 남았고,
                            //   같은 이유로 <b>절토·성토가 계산 불가</b>라 표가 전부 빈칸이 됐다.
                            //   → <c>FindSurfaces</c>가 이미 원지반·정지면·터파기로 <b>갈라 놓았으니</b>
                            //     그 <b>ObjectId로 맞춘다.</b> 이름은 사용자가 지은 것이라 믿을 수 없다.
                            string kind = kindOf.TryGetValue(sec.SourceId, out var kk) ? kk : "";
                            // 이름으로 고른다 — 터파기를 먼저 봐야 한다('터파기면_DH'가 '정지'를 안 품지만 순서를 못 박아 둔다).
                            if (kind == "터파기") { if (!stExcav.IsNull) { sec.StyleId = stExcav; nE++; } }
                            else if (kind == "원지반") { if (!stGround.IsNull) { sec.StyleId = stGround; nG++; } }
                            else if (kind == "정지면") { if (!stPlan.IsNull) { sec.StyleId = stPlan; nP++; } }
                            else nSkip++;
                        }
                        catch { }
                    }
                }
                catch { }
            }
            tr.Commit();
        }
        catch (System.Exception ex) { log?.AppendLine("  단면 선 색 적용 실패 — " + ex.Message); }

        log?.AppendLine($"  단면 선 — 원지반 {nG}개(초록) · 계획 {nP}개(흰색) · 터파기 {nE}개(마젠타"
                      + (dash != null ? $"·{dash} 점선" : "·점선 못 실음") + ")"
                      + (nSkip > 0 ? $" · 이름으로 못 가른 것 {nSkip}개" : ""));
        return nG + nP + nE;
    }

    /// <summary>★★★[JACK 0826] <b>지표면에서 직접 읽는다.</b>
    ///
    /// <para><b>세 번 헛짚고 알아낸 것.</b> <c>Section.SectionPoints</c>의 <c>Location</c>은
    /// 표고가 아니라 <b>뷰 안의 도면 좌표</b>였다. 실측이 그것을 증명했다:
    /// 지표면 <c>정지순수_DH</c>는 <b>z100~115.3</b>인데 단면에서 읽은 값은 <b>z110~145</b>였고,
    /// <c>터파기면_DH</c>는 <b>z95~100</b>인데 <b>z106.9~107.5</b>가 나왔다 — 지표면 범위를 벗어난다.</para>
    ///
    /// <para>→ <b>절단선을 우리가 훑고 지표면에 표고를 묻는다.</b> 뷰가 어디에 놓였든,
    /// 어떤 축척이든 상관없다. 지표면이 곧 진실이다.</para>
    ///
    /// <para><b>지표면 밖은 <c>NaN</c>이다.</b> <c>FindElevationAtXY</c>가 예외를 던지는데,
    /// 그것을 0으로 바꾸면 <b>있지도 않은 흙</b>을 세게 된다 — 계획면이 부지 안에만 있으므로
    /// 절단선 양 끝은 대개 지표면 밖이다.</para></summary>
    private static DH.Grading.Core.XsecQty QtyAt(Transaction tr, CivilDb.Alignment al, double station,
        double wl, double wr,
        CachedGroundSurface csG, CachedGroundSurface csP, CachedGroundSurface csE,
        System.Text.StringBuilder dbg)
    {
        // 절단선의 좌우 끝을 구한다 — 종단도·횡단도가 쓰는 것과 <b>같은 함수</b>다.
        if (!SectionCommand.TryCut(al, station, wl, wr, out var cut))
        {
            dbg?.Append(" ⚠절단선을 못 구했다");
            return new DH.Grading.Core.XsecQty(double.NaN, double.NaN, double.NaN, double.NaN, double.NaN);
        }

        int n = (int)System.Math.Max(2, System.Math.Round((wl + wr) / SampleStepM) + 1);
        var xs = new double[n];
        double lx = cut.Left.X, ly = cut.Left.Y, rx = cut.Right.X, ry = cut.Right.Y;
        for (int i = 0; i < n; i++) xs[i] = -wl + (wl + wr) * i / (n - 1.0);

        // ★★★[검토] <b>못 잰 것과 0을 갈라야 한다.</b> 유효한 점이 두 개도 안 되면
        //   <c>null</c>을 돌려준다 — 그래야 아래 계산이 <c>NaN</c>(잴 수 없었다)을 내고,
        //   표에 <b>빈칸</b>으로 간다. 종전엔 전부 NaN인 배열을 넘겨 면적이 <b>0.00</b>으로 찍혔고,
        //   로그도 "뺐다"로 세어 <b>성공처럼 보였다</b>.
        //
        // ★[검토] <b>예외 대신 캐시를 쓴다.</b> <c>FindElevationAtXY</c>는 지표면 밖에서 예외를 던지는데,
        //   계획면·터파기면은 부지 안에만 있어 절단선 대부분이 밖이다 — 측점당 수백 번 예외가 난다.
        //   <c>CachedGroundSurface</c>는 삼각형을 한 번 읽어 격자로 색인해 두고 <b>bool로</b> 답한다.
        //   같은 물건을 정지 명령이 이미 조밀 표본에 쓰고 있다.
        double[] Read(CachedGroundSurface cs)
        {
            if (cs == null) return null;
            var z = new double[n];
            int okN = 0;
            for (int i2 = 0; i2 < n; i2++)
            {
                // ★[중요] 격자가 경계점 때문에 <b>불균등</b>해졌다 — 순번으로 위치를 셈하면 어긋난다.
                //   <c>xs</c>에 적힌 실제 오프셋에서 비율을 낸다.
                double t2 = (xs[i2] + wl) / (wl + wr);
                double px2 = lx + (rx - lx) * t2, py3 = ly + (ry - ly) * t2;
                if (cs.TryGetElevation(px2, py3, out double zz)) { z[i2] = zz; okN++; }
                else z[i2] = double.NaN;
            }
            return okN >= 2 ? z : null;   // ★두 점도 못 얻으면 "못 쟀다"
        }

        // ★★★[JACK 0826 "BO로 재니 2.253인데 왜 절토가 2.11이야?"]
        //   <b>원인: 지표면 가장자리를 0.25m 단위로만 알았다.</b>
        //   계획면은 부지 안에만 있어 절단선 어딘가에서 끊기는데, 그 끊기는 자리가
        //   표본 사이에 있으면 <b>그 칸이 통째로 빠진다</b>(0.25m × 그 자리 깊이).
        //   가장자리 두 곳이면 실측 차이 0.14㎡가 그대로 설명된다.
        //
        //   → <b>경계를 이분법으로 찾아 그 점을 격자에 넣는다.</b> 여덟 번 반씩 쪼개면
        //   0.25m가 1mm 아래로 좁혀진다. 간격을 0.01m로 줄이는 것(6001점)보다 훨씬 싸다.
        var edges = new System.Collections.Generic.List<double>();
        int nEdge = 0;
        foreach (var cs in new[] { csG, csP, csE })
        {
            if (cs == null) continue;
            bool Ok(double xx)
            {
                double tt = (xx + wl) / (wl + wr);
                return cs.TryGetElevation(lx + (rx - lx) * tt, ly + (ry - ly) * tt, out _);
            }
            for (int k = 0; k + 1 < n; k++)
            {
                bool a = Ok(xs[k]), b = Ok(xs[k + 1]);
                if (a == b) continue;                       // 경계가 아니다
                double lo2 = xs[k], hi2 = xs[k + 1];
                for (int it = 0; it < 14; it++)             // ★0.1m → 0.006mm 아래
                {
                    double mid = (lo2 + hi2) / 2.0;
                    if (Ok(mid) == a) lo2 = mid; else hi2 = mid;
                }
                // 경계 <b>양쪽</b>에 점을 둔다 — 안쪽 점이 있어야 그 칸을 셀 수 있다.
                // ★[JACK 0827] 경계 <b>양쪽</b>에 점을 둔다 — 안쪽 점이 있어야 그 칸을 셀 수 있다.
                //   그리고 경계 <b>안쪽 0.2m를 1cm마다</b> 훑는다: 지표면이 끝나는 자리는 대개
                //   비탈이 만나는 곳이라 <b>형상이 급하게 바뀐다</b>. 0.1m 간격으로는 그 곡률을 놓친다.
                edges.Add(a ? lo2 : hi2);
                edges.Add(a ? hi2 : lo2);
                double inner = a ? lo2 : hi2;          // 지표면이 있는 쪽
                double dir = a ? -1.0 : +1.0;          // 안쪽으로 가는 방향
                for (int q = 1; q <= 20; q++)          // 안쪽 0.2m를 <b>1cm</b>마다 — 기본 간격의 10분의 1
                {
                    double xe = inner + dir * (q * 0.01);
                    if (xe > -wl && xe < wr) edges.Add(xe);
                }
            }
        }
        if (edges.Count > 0)
        {
            var merged = new System.Collections.Generic.List<double>(xs);
            merged.AddRange(edges);
            merged.Sort();
            var keep = new System.Collections.Generic.List<double>(merged.Count) { merged[0] };
            for (int k = 1; k < merged.Count; k++)
                if (merged[k] - keep[keep.Count - 1] > 1e-6) keep.Add(merged[k]);
            nEdge = keep.Count - n;
            xs = keep.ToArray();
            n = xs.Length;
        }

        double[] gy = Read(csG), py2 = Read(csP), ey = Read(csE);

        if (dbg != null)
        {
            void Rng(string nm, double[] zz)
            {
                if (zz == null) { dbg.Append($" {nm}=없음"); return; }
                double lo = double.MaxValue, hi = double.MinValue; int cnt = 0;
                foreach (double v in zz) if (!double.IsNaN(v)) { cnt++; if (v < lo) lo = v; if (v > hi) hi = v; }
                dbg.Append(cnt == 0 ? $" {nm}=범위밖" : $" {nm}={cnt}/{n}점 z{lo:F2}~{hi:F2}");
            }
            // ★[JACK 0826] 경계를 몇 개 찾았는지 남긴다 — 터파기 가장자리가 표본 사이에 있으면
            //   그 칸이 통째로 빠진다(0.25m × 깊이). 이분법이 실제로 도는지 이 숫자로 갈린다.
            dbg.Append($" 폭{wl:F0}+{wr:F0}m {n}점(경계 {nEdge}개 추가)");
            Rng("원지반", gy); Rng("계획", py2); Rng("터파기", ey);
        }

        var qq = DH.Grading.Core.XsecQuantity.Compute(xs, gy, xs, py2, xs, ey);
        if (dbg != null)
        // ★[JACK 0827 "토적표 정확도 향상"] <b>어느 구간을 쟀는지</b> 남긴다 —
        //   BO로 잡은 영역과 맞대 보려면 우리가 센 자리를 알아야 한다.
        if (dbg != null && gy != null && py2 != null)
        {
            double x0 = double.NaN, x1 = double.NaN, hMax = 0; int nCell = 0;
            for (int j = 0; j < n && j < gy.Length && j < py2.Length; j++)
            {
                double d = gy[j] - py2[j];
                if (double.IsNaN(d) || d <= 0) continue;
                if (double.IsNaN(x0)) x0 = xs[j];
                x1 = xs[j]; nCell++;
                if (d > hMax) hMax = d;
            }
            if (nCell > 0)
                dbg.Append($" [절토구간 x{x0:F2}~{x1:F2}({x1 - x0:F2}m) 최대높이 {hMax:F3}m {nCell}점]");
        }
            dbg.Append($" → 절토 {qq.Cut:F2} 성토 {qq.Fill:F2} 터파기 {qq.ExcShallow:F2}+{qq.ExcDeep:F2} 되메 {qq.Backfill:F2}"
                       + (qq.NoPlanCells > 0
                          ? $"  ⚠계획면이 없는 칸 {qq.NoPlanCells}개는 <b>원지반 기준</b>으로 셌다(터파기가 부풀 수 있다)"
                          : ""));
        return qq;
    }

    /// <summary>노선을 연다 — 못 열면 <c>null</c>.</summary>
    private static object trGetAlign(Transaction tr, ObjectId id)
    {
        try { return tr.GetObject(id, OpenMode.ForRead); } catch { return null; }
    }

    /// <summary>절단선을 훑는 간격(m) — 촘촘할수록 정확하지만 느리다. 0.25m면 60m 폭에 241점이다.</summary>
    /// <summary>절단선을 훑는 간격(m). ★[JACK 0826 "정확도 향상을 위해 절단 간격을 더 낮춰야"]
    /// <para>오차가 <b>두 종류</b>다:
    /// <list type="number">
    /// <item><b>꺾임을 놓치는 오차</b> — 간격²에 비례한다. 0.25m에서 단면당 0.05㎡ 남짓으로
    /// 원래도 작았다(100~500㎡ 단면의 0.05% 이하).</item>
    /// <item><b>지표면 가장자리 오차</b> — <c>간격 × 그 자리 깊이</c>. 터파기 가장자리에서
    /// 깊이 5m면 1.25㎡였다. <b>이게 컸고</b>, 경계를 이분법으로 1mm까지 찾아 없앴다.</item>
    /// </list></para>
    /// <para>그래도 <b>0.1m로 낮춘다</b>: 가시설 벽면은 구배 0.01·높이 5m면 <b>폭이 5cm</b>라
    /// 25cm 간격에서는 표본 사이에 통째로 들어가 안 보인다. 이건 이분법으로 안 잡힌다 —
    /// 지표면이 끊기는 게 아니라 그 안에서 급하게 꺾이는 것이라서다.</para>
    /// <para>비용은 2.4배지만 <see cref="CachedGroundSurface"/>로 예외를 없앤 뒤라 감당된다.</para></summary>
    private const double SampleStepM = 0.1;

    /// <summary>측점 이름 → 그 측점의 수량. 표를 그릴 때 이 표를 찾아 값을 채운다.</summary>
    private static System.Collections.Generic.Dictionary<string, DH.Grading.Core.XsecQty>
        CollectQty(Database db, List<(ObjectId Id, string Name, double St, double Mother, int Ord)> sl,
                   ObjectId alignId, double wl, double wr,
                   System.Collections.Generic.List<SectionCommand.SurfPick> surfs,
                   System.Text.StringBuilder log)
    {
        var map = new System.Collections.Generic.Dictionary<string, DH.Grading.Core.XsecQty>();
        if (sl == null || sl.Count == 0) return map;
        int nOk = 0, nNo = 0;
        double sumCut = 0, sumFill = 0, sumExc = 0;
        int nNoPlan = 0;
        int nNoG = 0, nNoP = 0, nNoE = 0;
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            if (trGetAlign(tr, alignId) is not CivilDb.Alignment al0)
            {
                tr.Commit();
                // ★[검토] 조용히 빈 표를 돌려주면 "표가 왜 비었나"에 단서가 없다.
                log?.AppendLine("  ⚠수량 — 노선을 못 열어 하나도 못 뺐다");
                return map;
            }

            // ★[검토] 지표면 캐시를 <b>한 번만</b> 만들어 모든 측점이 나눠 쓴다 —
            //   측점마다 다시 만들면 삼각형을 수십 번 읽는다.
            CachedGroundSurface csG = null, csP = null, csE = null;
            int nTri = 0;
            foreach (var sp in surfs)
            {
                try
                {
                    if (tr.GetObject(sp.SurfId, OpenMode.ForRead) is not CivilDb.TinSurface ts) continue;
                    var cs = new CachedGroundSurface(ts);
                    nTri++;
                    if (sp.Label == "원지반") csG = cs;
                    else if (sp.Label == "정지면") csP = cs;
                    else if (sp.Label == "터파기") csE = cs;
                }
                catch (System.Exception exC) { log?.AppendLine($"  ⚠지표면 '{sp.Label}' 캐시 실패 — {exC.Message}"); }
            }
            log?.AppendLine($"  지표면 캐시 {nTri}장 — 절단선 표고를 예외 없이 읽는다");
            foreach (var s in sl)
            {
                try
                {
                    // ★[JACK 0826] 처음 세 개만 찍으면 하필 <b>부지 밖 측점</b>이 걸려
                    //   값이 나온 측점을 못 본다. <b>값이 나온 것</b>도 세 개까지 따로 남긴다.
                    // ★[JACK 0827] <b>모든 측점을 남긴다.</b> 셋만 찍으니 하필 부지 밖 측점이 걸려
                    //   "왜 전부 빈칸인지"를 볼 수가 없었다. 측점이 30개 남짓이라 길지도 않다.
                    var dbg = new System.Text.StringBuilder();
                    var q = QtyAt(tr, al0, s.St, wl, wr, csG, csP, csE, dbg);
                    if (dbg != null && dbg.Length > 0) log?.AppendLine($"    [{s.Name}]{dbg}");
                    map[s.Name] = q;
                    if (double.IsNaN(q.Cut) && double.IsNaN(q.ExcShallow)) nNo++;
                    if (q.NoPlanCells > 0) nNoPlan++;
                    if (q.MissG) nNoG++;
                    if (q.MissP) nNoP++;
                    if (q.MissE) nNoE++;
                    else
                    {
                        nOk++;
                        if (!double.IsNaN(q.Cut)) sumCut += q.Cut;
                        if (!double.IsNaN(q.Fill)) sumFill += q.Fill;
                        if (!double.IsNaN(q.ExcTotal)) sumExc += q.ExcTotal;
                    }
                }
                catch { }
            }
            tr.Commit();
        }
        catch (System.Exception ex) { log?.AppendLine("  수량 계산 실패 — " + ex.Message); }
        // ★[JACK 0827 "토적표에 갑자기 값이 안 뜸 — 모든 측점이 -"]
        //   <b>왜 못 뺐는지</b>가 안 찍혀 있었다. 지표면별로 몇 개가 범위 밖이었는지 센다.
        log?.AppendLine($"  수량 — {nOk}개 측점에서 뺐다 · 못 뺀 것 {nNo}개"
                      + (nNoG > 0 ? $" · ⚠원지반이 없던 측점 {nNoG}개" : "")
                      + (nNoP > 0 ? $" · ⚠계획면이 없던 측점 {nNoP}개(부지 밖이면 정상)" : "")
                      + (nNoE > 0 ? $" · 터파기가 없던 측점 {nNoE}개" : "")
                      + (nNoPlan > 0 ? $" · ⚠계획면이 터파기를 못 덮은 측점 {nNoPlan}개" : "")
                      + $" · 합계 절토 {sumCut:F1}㎡ · 성토 {sumFill:F1}㎡ · 터파기 {sumExc:F1}㎡"
                      + "  ※단면 면적이다(체적은 측점 간격을 곱해야 한다)");
        return map;
    }

    /// <summary>★★[JACK 0826 "측점 기능으로 측점을 추가했을 때 횡단뷰가 그냥 날아가 버려.
    /// 제목하고 표만 덩그러니 남아 있어"]
    ///
    /// <para><b>원인.</b> 측점을 고치면 종단도가 검토선 그룹을 새로 만드는데, 그러면 그 그룹에 매달린
    /// <b>횡단면도가 함께 사라진다</b>(Civil이 지운다). 그런데 <b>우리가 그린 것</b>(제목·이름·수량표·도곽·축)은
    /// 생 <c>DBText</c>·<c>Line</c>·<c>Table</c>이라 Civil이 모른다 — 그대로 남아 <b>유령</b>이 된다.</para>
    ///
    /// <para>그래서 <c>[횡단도]</c>는 시작할 때 <b>제가 지난번에 그린 것을 먼저 지운다.</b>
    /// 레이어로 갈라 두었기에 정확히 우리 것만 지울 수 있다 —
    /// ★<c>DH-도곽범위(모형)</c>는 <b>종단도와 함께 쓰는 레이어</b>라 손대지 않는다.</para></summary>
    private static int WipeOld(Database db, System.Text.StringBuilder log)
    {
        string[] mine = { XsecTitleLayer, XsecAxisLayer, XsecTextLayer, XsecCellLayer,
                          QtLayerEdge, QtLayerLine, QtLayerText, XsecFrameLayer };
        int n = 0;
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);
            var want = new System.Collections.Generic.HashSet<string>(mine, System.StringComparer.OrdinalIgnoreCase);
            foreach (ObjectId id in ms)
            {
                try
                {
                    if (tr.GetObject(id, OpenMode.ForRead) is not Entity e) continue;
                    if (!want.Contains(e.Layer)) continue;
                    e.UpgradeOpen();
                    e.Erase();
                    n++;
                }
                catch { }
            }
            tr.Commit();
        }
        catch (System.Exception ex) { log?.AppendLine("  옛 횡단 지우기 실패 — " + ex.Message); }
        if (n > 0) log?.AppendLine($"  지난번 횡단 산출물 {n}개를 지웠다(제목·이름·표·칸선·축)"
                                 + " — 측점을 고치면 뷰만 사라지고 이것들이 유령으로 남는다");
        return n;
    }

    /// <summary>★★★[검토] <b>지난번 검토선 그룹과 뷰를 지운다.</b>
    ///
    /// <para><c>WipeOld</c>는 <b>우리가 그린 글자·선·표</b>만 지운다. Civil이 만든
    /// <c>SampleLineGroup</c>·<c>SampleLine</c>·<c>SectionView</c>는 아무도 안 지웠다.</para>
    ///
    /// <para><b>그래서 [횡단도]를 두 번 누르면</b> 같은 이름의 검토선을 Civil이 거부하고
    /// 뷰가 안 만들어진다 — <b>제목과 표만 덩그러니 남는</b> 그 도면이 된다.
    /// (여태 안 터진 것은 [종단도]를 먼저 돌리면 그것이 선형을 지우면서 딸린 그룹까지 죽였기 때문이다.
    /// <b>횡단도만 두 번</b> 누르면 드러난다.)</para>
    ///
    /// <para>우리가 만든 그룹만 지운다 — 이름이 <c>DH횡단_횡단</c>으로 시작하는 것.
    /// 사용자가 손으로 만든 그룹은 건드리지 않는다.</para></summary>
    private static int WipeOldGroups(Database db, CivilApp.CivilDocument cdoc, ObjectId alignId,
                                     System.Text.StringBuilder log)
    {
        int nG = 0, nV = 0;
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            if (tr.GetObject(alignId, OpenMode.ForRead) is CivilDb.Alignment al)
            {
                var kill = new System.Collections.Generic.List<ObjectId>();
                foreach (ObjectId gid in al.GetSampleLineGroupIds())
                {
                    try
                    {
                        if (tr.GetObject(gid, OpenMode.ForRead) is not CivilDb.SampleLineGroup g) continue;
                        string gn = g.Name ?? "";
                        if (!IsXsecGroupName(gn)) continue;
                        // 뷰부터 세어 둔다 — 그룹을 지우면 딸린 뷰도 같이 사라진다.
                        foreach (ObjectId sid in g.GetSampleLineIds())
                        {
                            try
                            {
                                if (tr.GetObject(sid, OpenMode.ForRead) is CivilDb.SampleLine sl)
                                    nV += sl.GetSectionViewIds().Count;
                            }
                            catch { }
                        }
                        kill.Add(gid);
                    }
                    catch { }
                }
                foreach (ObjectId gid in kill)
                {
                    try
                    {
                        if (tr.GetObject(gid, OpenMode.ForWrite) is Entity e) { e.Erase(); nG++; }
                    }
                    catch (System.Exception exG) { log?.AppendLine("  옛 검토선 그룹 지우기 실패 — " + exG.Message); }
                }
            }
            tr.Commit();
        }
        catch (System.Exception ex) { log?.AppendLine("  옛 그룹 정리 실패 — " + ex.Message); }
        if (nG > 0) log?.AppendLine($"  지난번 검토선 그룹 {nG}개(딸린 횡단면도 {nV}장)를 지웠다"
                                  + " — 안 지우면 같은 이름을 Civil이 거부해 그림 없는 도면이 된다");
        return nG;
    }

    /// <summary>지우기 목록에서 쓰는 별칭 — <see cref="SheetCommand.EraseAll"/>이 이 레이어들을 함께 지운다.</summary>
    internal static string TitleLayer => XsecTitleLayer;
    internal static string AxisLayer => XsecAxisLayer;
    internal static string TextLayer => XsecTextLayer;
    internal static string CellLayer => XsecCellLayer;
    internal static string FrameLayer => XsecFrameLayer;
    internal static string QtEdgeLayer => QtLayerEdge;
    internal static string QtLineLayer => QtLayerLine;
    internal static string QtTextLayer => QtLayerText;
}
