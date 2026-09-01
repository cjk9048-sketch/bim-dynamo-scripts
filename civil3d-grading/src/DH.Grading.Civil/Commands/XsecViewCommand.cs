using QT = DH.Grading.Core.QuantityTable;
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

    /// <summary>★★★[JACK 0828 "측점 글씨색이 노란색인데 검정으로 바꿔"]
    /// <b>측점 밴드 글씨는 제목과 레이어를 나눈다.</b>
    /// <para>종전엔 <see cref="XsecTitleLayer"/>(노랑)를 <b>제목과 같이 썼다</b> —
    /// 그래서 밴드 안의 측점만 검정으로 바꿀 방법이 없었다. 한 레이어가 두 가지 뜻을 겸하면
    /// 하나를 바꿀 때 다른 하나가 딸려 온다(§52가 변수에서 겪은 것과 같은 함정이다).</para>
    /// <para>색은 <b>7(흰색/검정)</b> — JACK 선택. 화면에선 하얗고 <b>인쇄하면 검정</b>이라
    /// 한국 도면의 표준 '검정'이다. 밴드의 GL·FGL 글씨와 같은 색이라 세 줄이 한 덩이로 보인다.
    /// (배경이 검정이라 <b>진짜 검정(250)으로 하면 안 보인다</b> — 그래서 7이다.)</para></summary>
    internal const string XsecStationLayer = "DH-횡단-측점";

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
        // ★★★[JACK 0831 검증] <b>지우기가 묻기보다 앞에 있었다 — "취소"가 취소가 아니었다.</b>
        //   <c>WipeOld</c>·<c>WipeOldGroups</c>는 각자 트랜잭션을 <b>커밋까지</b> 끝낸다.
        //   그 뒤에 자리를 묻고 Esc면 아무것도 안 그리고 돌아간다 —
        //   즉 지난번 횡단면도·도곽·수량표·검토선그룹이 <b>이미 다 지워진 빈 도면</b>이 되고
        //   화면에는 "취소"라고만 뜬다. 되돌릴 길이 없다.
        //   이 함수의 다른 이탈 경로는 전부 지우기 <b>앞</b>에 있는데 이 하나만 뒤에 있었다.
        //   → <b>자리를 먼저 받는다.</b> 지우기는 다시 그릴 것이 확정된 뒤에만.
        Point3d at;
        if (at0 != null) { at = at0.Value; log.AppendLine("  자리는 지난번 그대로(측점을 고쳐 다시 그린다)"); }
        else
        {
            var pr = ed.GetPoint("\n[횡단도] 횡단면도를 놓을 왼쪽 아래 자리를 클릭 (Esc=취소): ");
            if (pr.Status != PromptStatus.OK) { ed.WriteMessage("\n[횡단도] 취소."); Flush(log); return; }
            at = pr.Value.TransformBy(ed.CurrentUserCoordinateSystem);
        }
        LastAt = at;   // ★다음에 측점을 고치면 이 자리에 다시 그린다

        // ★[JACK 0826] <b>지난번 것을 지운다</b> — 안 지우면 유령이 겹친다.
        //   ★자리를 받은 <b>뒤</b>라야 한다(위 주석) — 다시 그릴 것이 확정된 다음에만 지운다.
        WipeOld(db, log);
        WipeOldGroups(db, cdoc, alignId, log);   // ★Civil이 만든 그룹·뷰도 함께

        // ── ④ 횡단용 검토선 그룹 — 벽 자리는 (전)(후) 둘.
        // ★★[JACK 0826 "지금 종단뷰도 도곽 사이즈에 맞춰서 최적 축척으로 들어가는 거잖아,
        //   그 원리랑 같은 거야"] — <b>맞다. 폭을 줄이는 게 아니라 축척을 고른다.</b>
        //
        //   종이 규격(A1 841×594 · 안쪽 796×484mm[횡단])이 <b>고정</b>이고,
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
                    // ★★[JACK 0831] <b>도면에 안 보일 층은 단면을 안 뜬다.</b>
                    //   수량은 지표면을 <b>직접 훑어</b> 재므로(CachedGroundSurface) 단면이 없어도 멀쩡하다 —
                    //   즉 토사를 꺼도 토적표의 토사 물량은 그대로다.
                    bool ours = surfs.Exists(x => x.SurfId == src.SourceId && x.Show);
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
                        // ★[검토] 자리를 정하는 자는 <b>Core에 하나</b>다 — 여기서 다시 계산하지 않는다.
                        // ★★★[JACK 0828] <b>벽과 수동 (전)(후)는 자가 다르다.</b>
                        //   벽은 두께가 얇아 밀어내야 하고, 사람이 찍은 것은 <b>그 자리가 곧 답</b>이다
                        //   (좌우 5cm — 주로 구조물 투영한 자리에 찍는다).
                        bool fixedSpan = StationMarks.IsFixedSpan(sp.Kind);
                        var (fSt, bSt, outw) = DH.Grading.Core.XsecSpan.Place(sp.Front, sp.Back, fixedSpan);
                        jobs = new[] { (fSt, "(전)", 0), (bSt, "(후)", 2) };
                        nPair++;
                        log.AppendLine($"    {(fixedSpan ? "수동" : "벽")} {StationMarks.Fmt(sp.Mid, ProfileCommand.LastStationInterval)}" +
                                       (fixedSpan
                                        ? $" — 사람이 정한 자리 그대로 → (전){fSt:F2} / (후){bSt:F2} (좌우 {outw:F2}m)"
                                        : $" — 두께 {sp.Back - sp.Front:F3}m → (전){fSt:F2} / (후){bSt:F2} (밖으로 {outw:F2}m)"));
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

        // ★★★[JACK 0831 "표 높이가 달라지는 걸 파악해서 그래프 부분 축척도 조절되어야 해"]
        //   <b>수량을 여기서 먼저 잰다.</b> 표 줄 수가 축척을 정하고, 축척이 자리를 정하기 때문이다 —
        //   종전처럼 자리를 다 잡은 뒤에 재면 <b>줄 수를 알기 전에 축척이 정해져</b> 있다.
        //   (계산은 검토선과 지표면만 있으면 되므로 이 자리에서 돌 수 있다.)
        var qty = CollectQty(db, slIds, alignId, wl, wr, surfs, log);

        var mv = MeasureViews(db, viewIds, log);

        // ── ★★축척 고르기 — 종단도 <c>FitSheet</c>과 같은 셈이다.
        //   <c>종이 mm = 모형 m × 1000 ÷ 축척</c>이므로 뒤집으면 <c>필요 축척 = 모형 m × 1000 ÷ 종이 mm</c>.
        //   가로·세로 중 <b>엄한 쪽</b>이 이기고, 사다리에서 그 값 이상인 첫 값을 고른다.
        // ★[검토] 여기서 <b>실제로 만들어진 뷰 수</b>로 다시 센다 —
        //   뷰가 몇 개 실패하면 빈 도곽이 한 장 더 그려진다.
        nPages = (System.Math.Max(viewIds.Count, 1) + perSheet - 1) / perSheet;
        double cellWmm = SheetCommand.InnerW / cols;      // 칸 폭(종이 mm) — 거터 없이 그냥 나눈다
        double cellHmm = XsecInnerH / rows;              // 칸 높이(종이 mm)
        // ★★★[JACK 0831 "표를 어떻게 하면 빈 셀이 없게"] <b>접을지 여기서 정한다.</b>
        //   폭과 높이가 둘 다 접기에 달려 있고, 그 둘이 축척을 정한다.
        var fold = DH.Grading.Core.QtyTableFold.Make(qty.Spec);
        double tableWmm = QtWidthMmOf(fold);
        // ★[검토 §50] 표 높이를 <b>두 곳에서 다르게</b> 세고 있었다 —
        //   자리 잡는 쪽은 19.0줄, 그리는 쪽은 머리줄 1.4배를 반영해 19.4줄. 2.3mm 어긋났다.
        double tableHmm = QtTableHmmOf(fold.BodyRows + 1);

        // ★★[검토] 표를 <b>오른쪽</b>에 둘 때와 <b>아래</b>에 둘 때를 <b>둘 다 계산</b>해
        //   축척이 작은 쪽(=그림이 큰 쪽)을 고른다. 3×2처럼 칸이 좁은 배치에서는
        //   표를 아래로 내리는 것만으로 <b>1:500 → 1:300</b>으로 두 단계가 살아난다.
        // ★[JACK 0827] 주석 축척을 <b>여기서</b> 읽는다 — 밴드의 모형 높이를 계산해야 하기 때문이다.
        double annoScale = SheetCommand.CurrentDrawingScale(db);

        // ★★★[검토 0827 · CRITICAL] <b>경계상자에 밴드는 안 들어 있다 — 실측으로 확정.</b>
        //   밴드가 2.0m일 때도 3.0m일 때도 잰 뷰 높이가 <b>둘 다 30.0m</b>였다(로그 15987·16346).
        //   폭도 60.0m 딱 떨어진다 — 검토선 좌30+우30, 즉 <b>그래프 네모 하나</b>다.
        //   그래서 <c>mv.H</c>에서 밴드를 빼던 종전 계산은 <b>없는 것을 뺀 것</b>이었다.
        //   그림이 필요 이상 작아졌고, 무엇보다 <b>직전 실행이 남긴 스타일 값</b>을 읽으므로
        //   같은 버튼인데 <b>전에 뭘 눌렀냐에 따라 축척이 달라졌다</b>(1:150 → 1:500 → 1:120…).
        //   → <b>칸 수만 센다.</b> 종이 높이는 칸수 × <see cref="BandHeightMm"/>이고 <b>축척과 무관</b>하다.
        int bandRows = 0;
        try
        {
            using var trB = db.TransactionManager.StartTransaction();
            if (viewIds.Count > 0 &&
                trB.GetObject(viewIds[0].Id, OpenMode.ForRead) is CivilDb.SectionView sv0)
            {
                using var bi0 = sv0.Bands.GetBottomBandItems();
                bandRows = bi0.Count;
            }
            trB.Commit();
        }
        catch (System.Exception ex) { log.AppendLine("  밴드 칸 수를 못 셌다 — " + ex.Message); }
        if (bandRows <= 0) { bandRows = BandRows; log.AppendLine($"  ⚠밴드 칸을 못 세어 {BandRows}칸으로 가정한다"); }

        // 밴드가 먹는 <b>종이</b> 높이 — 이 하나를 예산·배치·표 자리가 <b>함께</b> 쓴다.
        //   ★[검토 · HIGH-1] 종전엔 예산은 30mm를 빼고 배치는 밴드를 아예 몰랐다 —
        //   그래서 칸 위아래로 32mm씩 비어 있는데도 표가 그래프에 붙어 겹쳤다(§50 그 함정).
        //   ★[JACK 0827 "너무 여유를 둬서 횡단뷰가 작아지지 않게"] <b>실제 칸 수</b>로 잰다 —
        //   3칸을 미리 예약하면 쓰지도 않는 자리가 그림을 깎는다(칸 높이만큼).
        double bandPaperMm = bandRows * BandHeightMm;

        double padW = 2 * CellPadMm, padH = 2 * CellPadMm + NameRoomMm + bandPaperMm;
        double gwRight = System.Math.Max(10.0, cellWmm - padW - TableGapMm - tableWmm);
        // ★★★[JACK 0831 · 검토 MED-4] <b>오른쪽 배치도 표 높이를 봐야 한다.</b>
        //   종전엔 <c>ghRight</c>에 표가 안 들어 있었다 — 표가 그래프보다 길어도
        //   "칸에 들어간다"고 판정하고 실제로는 칸을 넘었다.
        //   표를 옆에 두면 <b>덩어리 높이 = max(그래프+밴드, 표)</b>이므로,
        //   표가 더 길면 그만큼 그림이 쓸 수 있는 높이가 줄어든다.
        //   ★<c>Math.Max(10.0, …)</c>가 음수 자리를 10mm로 바꿔 <b>말도 안 되는 축척</b>을
        //   되돌려 주던 것도 여기서 갈린다(검토 MED-3) → 자리가 모자라면 <b>0</b>을 준다.
        double roomH = cellHmm - padH;
        // 표가 남는 자리보다 길면 그림 자리는 <b>없다</b> — 10mm로 눙치지 않는다.
        double ghRight = tableHmm > roomH ? 0.0 : roomH;
        double gwBelow = System.Math.Max(10.0, cellWmm - padW);
        // ★[검토 MED-3] 아래 배치도 마찬가지 — 자리가 모자라면 <b>0</b>을 줘서
        //   <c>PickScale</c>이 "맞는 값이 없다"고 답하게 한다. 10mm로 눙치면
        //   <c>1:3000</c> 같은 값이 <b>유효한 답처럼</b> 돌아온다.
        double roomBelow = cellHmm - padH - TableGapMm - tableHmm;
        double ghBelow = roomBelow > 0 ? roomBelow : 0.0;
        double sRight = PickScale(mv.W, mv.H, gwRight, ghRight);
        double sBelow = PickScale(mv.W, mv.H, gwBelow, ghBelow);
        bool tableRight = sRight > 0 && (sBelow <= 0 || sRight <= sBelow);   // 같으면 오른쪽(참고 도면)
        double autoScale = tableRight ? sRight : sBelow;

        // ★★[JACK 0826 "도면설정에 종단도 축척이 자동·지정이 있잖아? 횡단도 똑같은 로직으로"]
        //   <b>고정을 골랐으면 그 값을 그대로 쓴다.</b> 안 들어가도 <b>바꾸지 않는다</b> —
        //   사용자가 1:200을 콕 집었는데 우리가 1:250으로 올리면 <b>도면에 적힌 축척과 실제가 어긋난다.</b>
        //   현장에서 자로 재는 값이라 그게 넘치는 것보다 나쁘다. 넘친 채로 그리고 로그로 알린다.
        // ★★[JACK 0827 "표 크기 변화에 따른 횡단도 배치별 축척 잘 고려해야 해"]
        //   <b>새 표는 훨씬 넓고 낮다</b>(비율 합 25.5→70, 줄 19→13).
        //   그래서 <b>오른쪽에 두면</b> 그림 폭을 크게 잡아먹고 <b>아래에 두면</b> 덜 먹는다 —
        //   어느 쪽이 나은지는 <b>배치(1×1 ~ 3×2)마다 뒤집힌다</b>. 그 판단을 로그로 남긴다.
        log.AppendLine($"  밴드 자리 — {bandRows}칸 × {BandHeightMm:0.#}mm = 종이 {bandPaperMm:F0}mm"
                     + $" (경계상자에는 안 들어가므로 <b>잰 높이 {mv.H:F1}m는 그대로</b> 쓴다)");
        log.AppendLine($"  축척 고르기 — 칸 {cellWmm:F0}×{cellHmm:F0}mm · 표 {tableWmm:F0}×{tableHmm:F0}mm"
                     + $" · 오른쪽에 두면 자리 {gwRight:F0}×{ghRight:F0}→"
                     + (sRight > 0 ? $"1:{sRight:F0}" : "안 들어감")
                     + $" · 아래에 두면 자리 {gwBelow:F0}×{ghBelow:F0}→"
                     + (sBelow > 0 ? $"1:{sBelow:F0}" : "안 들어감")
                     + $" · <b>택함: {(tableRight ? "오른쪽" : "아래")}</b>");

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
        log.AppendLine($"  ★축척 1:{scale:F0}({(fixedScale ? "도면설정에서 고정" : "자동 — 칸에 맞춤")})"
                     + $" · 표는 {(tableRight ? "그림 오른쪽" : "그림 아래")}"
                     + $" (필요 가로 1:{mv.W * 1000.0 / graphWmm:F0} · 세로 1:{mv.H * 1000.0 / graphHmm:F0}"
                     + $" · 오른쪽 1:{sRight:F0} vs 아래 1:{sBelow:F0})");
        log.AppendLine($"  배치 {cols}×{rows}(한 장 {perSheet}개) · A1 {sheetW:F1}×{sheetH:F1}m"
                     + $" · 안쪽 {innerW:F1}×{innerH:F1}m(종이 {SheetCommand.InnerW:F0}×{XsecInnerH:F0}mm)"
                     + $" · 칸 {cellW:F1}×{cellH:F1}m(종이 {cellWmm:F0}×{cellHmm:F0}mm) · {nPages}장");
        // ★★★[JACK 0827 "1:150만 되어도 상당히 작아서 잘 안 보여"]
        //   <b>이제야 축척을 안다.</b> 눈금값·밴드 글자를 <b>종이에서 바라는 크기</b>로 되돌려 넣는다.
        SetTextSizes(db, XsecStyleId(db, viewIds), scale, annoScale, log);
        ProbeBandText(db, viewIds, scale, annoScale, log);

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
                        // ★★★[검토 0827 · HIGH-1] <b>덩어리 높이에 밴드가 빠져 있었다.</b>
                        //   예산(<c>padH</c>)은 밴드를 뺐는데 여기는 몰랐다 — 그래서 칸 위아래로
                        //   32mm씩 비어 있는데도 표가 그래프에 붙어 겹쳤다. <b>같은 것을 두 곳에서 따로</b> 잰 것이다.
                        //   밴드는 그래프 <b>아래</b>로 뻗으므로 세로 덩어리에만 더한다.
                        double bandM2 = bandPaperMm * sc;
                        double bundleH = tableRight
                            ? System.Math.Max(mv.H + bandM2, tableHmm * sc)
                            : mv.H + bandM2 + gapM2 + tableHmm * sc;
                        double leftX = cellAt[i].X + System.Math.Max(padM, (cellW - bundleW) / 2.0);
                        double botY = cellAt[i].Y + nameM
                                    + System.Math.Max(padM, (cellH - nameM - bundleH) / 2.0);
                        double wantCx = leftX + mv.W / 2.0;
                        // ★★★[JACK 0827 스샷 "넘어가는 배열이 있어"]
                        //   <b>밴드가 앉을 자리를 비워 두고 그림을 올린다.</b>
                        //   덩어리 높이(<c>bundleH</c>)에는 밴드를 더했는데 <b>그림 위치를 안 올려서</b>
                        //   밴드가 <c>botY</c> 아래, 즉 <b>칸 밖으로</b> 뻗었다 —
                        //   경계상자에 밴드가 없으니 화면으로만 보이고 계산에는 안 잡힌다.
                        //   표가 아래면 그림은 그 위에 앉고, 밴드는 그림과 표 <b>사이</b>에 들어간다.
                        double wantCy = tableRight
                            ? botY + bandM2 + mv.H / 2.0
                            : botY + tableHmm * sc + gapM2 + bandM2 + mv.H / 2.0;
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
        // ★★[검토 MED-4] 넘쳤는지 볼 때 <b>표까지 포함한 덩어리</b>를 견준다 —
        //   그래프만 보면 표가 칸을 넘어도 "들어간다"고 말한다.
        double bundleHmm = tableRight
            ? System.Math.Max(mv.H / sc + bandPaperMm, tableHmm)
            : mv.H / sc + bandPaperMm + TableGapMm + tableHmm;
        double roomHmm = cellHmm - padH + bandPaperMm;   // 밴드는 padH에서 이미 뺐으므로 되돌린다
        if (bundleHmm > roomHmm + 1e-6)
            log.AppendLine($"  ⚠<b>표까지 합치면 칸을 넘는다</b> — 덩어리 {bundleHmm:F1}mm > 자리 {roomHmm:F1}mm"
                         + $" (표 {tableHmm:F1}mm · {(tableRight ? "오른쪽" : "아래")} 배치)"
                         + " — 배치를 줄이거나(도면설정) 축척을 낮추세요");
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
        // ★★★[JACK 0828 "가로 가자"] 회사 스타일이 축을 그려 줄 때는 <b>표고 숫자만</b> 가져온다.
        //   Civil 래퍼 버그로 중심축 띄우기를 코드로 못 바꾸기 때문이다(<see cref="TickDiag"/>가 확정).
        //   <b>축선·눈금 자국은 Civil이 그대로 그린다</b> — 안 되는 것 하나만 가져오는 것이 규칙이다.
        //   자리는 뷰를 옮긴 <b>뒤</b>라야 맞으므로 <see cref="DrawCenterAxis"/>와 같은 자리에 둔다.
        else DrawCenterTickLabels(db, viewIds, XsecStyleId(db, viewIds), scale, annoScale, log);
        DrawXsecFrames(db, at, nPages, sc, cols, rows, PageGap, scale, log);
        DrawQtyTables(db, viewIds, bandPaperMm, sc, tableRight, TableGapMm * sc, qty, fold, log);
        // ★[JACK 0826] 선 색·눈금은 <b>숨기기 전</b>에 — 숨긴 뒤에도 되지만 로그 차례가 헷갈린다.
        ApplySectionStyles(db, cdoc, slIds, kindOf, log);
        DrawStrataNames(db, viewIds, kindOf, alignId, wl, wr, scale, log);   // ★[JACK 0828] 지층·지하수위 이름
        BindBandSections(db, viewIds, kindOf, scale, annoScale, log);
        DrawStationBand(db, viewIds, scale, annoScale, log);   // ★[JACK 0827] 측점 칸에 우리 이름
        HideSampleLines(db, cdoc, slIds, groupId, log);   // ★뷰를 다 만든 뒤에 숨긴다

        log.AppendLine($"  횡단면도 이름 {nTxt}개 직접 씀(레이어 '{XsecTitleLayer}') — " +
                       (titleOff ? "Civil 기본 제목은 껐다 — 화면 이름은 이것뿐이다"
                                 : "⚠Civil 기본 제목이 살아 있다 — 이름이 두 개로 보인다"));
        log.AppendLine($"  횡단면도 {nView}/{slIds.Count}장 배치 · 배치 {cols}×{rows} · 칸 {cellW:F1}×{cellH:F1}m" +
                       (firstErr != null ? $"\n  ⚠첫 실패: {firstErr}" : ""));
        // ★★★[JACK 0831 · 검토] 수량이 조용히 틀어진 것은 <b>명령창까지</b> 올린다 —
        //   로그 파일은 사람이 열어 봐야 알고, 표는 숫자가 차 있어 멀쩡해 보인다.
        ed.WriteMessage((string.IsNullOrEmpty(qty.Warn) ? "" : qty.Warn) +
                        $"\n[횡단도] 횡단면도 {nView}장 · 검토선 {slIds.Count}개" +
                        $"\n  자세한 내용: {DiagLog.FilePath}");
        Flush(log);
    }

    /// <summary>★[JACK 0831] 점선 <b>한 무늬</b>가 도면에서 차지할 길이(m).
    /// <para>이 값이 곧 "촘촘한 정도"다. 작을수록 촘촘하다.
    /// 부지가 수백 m라 0.5m면 눈에는 거의 실선에 가까운 촘촘한 점선으로 보인다.</para></summary>
    /// <summary>★★[JACK 0831 · 검토] 표에 <b>숫자로 보이는 가장 작은 값</b>(㎡).
    /// <para>줄을 세울지 정하는 자리와 값을 찍는 자리가 <b>같은 값</b>을 봐야 한다 —
    /// 다르면 "줄은 있는데 모든 측점에서 –"인 유령 줄이 생기고, 그 줄이 축척을 흔든다.</para>
    /// <para><c>0.00</c> 두 자리로 찍으므로 <c>0.005</c> 미만은 반올림해도 <c>0.00</c>이다.</para></summary>
    internal const double QtyShowMin = 5e-3;

    private const double DashPatternM = 0.5;

    /// <summary>지층 색 — 원지반(초록3)·계획(흰7)·터파기(마젠타6)·지하수위(파랑5)와 <b>겹치지 않는</b> 것만.
    /// <para>층이 여덟을 넘으면 처음부터 돌려 쓴다(그만한 층은 실무에 거의 없다).</para></summary>
    private static readonly short[] StrataAci = { 30, 42, 2, 224, 190, 22, 94, 214 };

    /// <summary>★★[JACK 0831] 층 번호 → 색. <b>색표를 아는 곳은 여기 하나다</b> —
    /// 종단(<c>ProfileCommand</c>)도 이것을 빌려 쓴다. 두 곳이 따로 색을 정하면
    /// <b>같은 지층이 종단과 횡단에서 다른 색</b>이 되어 더 헷갈린다(§50 그 함정).</summary>
    internal static short StrataAciOf(int ord) =>
        StrataAci[(System.Math.Max(1, ord) - 1) % StrataAci.Length];

    private static double SafeLts(Database db)
    { try { return db.Ltscale; } catch { return 1.0; } }

    /// <summary>★[JACK 0831 "지층에 나오는 문자가 너무 커 … 좀 작게"] 2.5 → <b>1.8mm</b>.
    /// <para>종단(<c>ProfileCommand.ProfStrataNameMm</c>)과 <b>같은 값</b>이라야 두 도면이 같아 보인다.</para></summary>
    private const double StrataNameMm = 1.8;   // 종이 글자 높이 — 지형선보다 작게(여러 줄이라 빽빽하다)

    /// <summary>★★★[JACK 0828 "종단이나 횡단에서 각 지층과 지하수위의 각층의 좌측 선 위에 해당 층이름을 적어줘"]
    ///
    /// <para><b>Civil 라벨을 쓰지 않는다.</b> 단면 라벨 세트는 스타일 관문이 넷이라(§0827)
    /// 지층 수만큼 스타일을 만들어야 하고, 그러고도 <b>솎아내기</b>에 걸려 안 나올 수 있다.
    /// 우리가 직접 쓰면 관문이 없다 — 눈금 숫자에서 이미 걸어 본 길이다.</para>
    ///
    /// <para><b>자리는 단면 점이 알려 준다.</b> <c>Section.SectionPoints</c>의 <c>Location</c>은
    /// 표고가 아니라 <b>뷰 안의 도면 좌표</b>다(§0826에서 세 번 헛짚고 알아낸 것).
    /// 수량 계산에는 못 쓰는 성질이지만, <b>글씨를 놓는 데는 바로 그것이 필요하다.</b></para>
    ///
    /// <para><b>뷰 밖은 버린다.</b> 지층면이 절단선보다 좁으면 좌표가 격자 밖으로 나갈 수 있어
    /// 옆 칸 도면을 침범한다 → 뷰 외곽선 안에 드는 점만 쓰고, 버린 수를 로그에 남긴다.</para></summary>
    private static int DrawStrataNames(Database db,
        System.Collections.Generic.List<(ObjectId Id, double St, string Name)> views,
        System.Collections.Generic.Dictionary<ObjectId, string> kindOf,
        ObjectId alignId, double wl, double wr,
        double scale, System.Text.StringBuilder log)
    {
        if (views == null || views.Count == 0 || kindOf == null) return 0;
        double txtH = StrataNameMm / 1000.0 * scale;
        double gap = txtH * 0.4;
        int n = 0, noZ = 0, noXY = 0;
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            if (tr.GetObject(alignId, OpenMode.ForRead) is not CivilDb.Alignment al)
            { tr.Commit(); log?.AppendLine("  횡단 지층이름 — 선형을 못 열었다"); return 0; }

            var ms = (BlockTableRecord)tr.GetObject(
                SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
            var layS = SectionCommand.EnsureLayer(db, tr, XsecStrataNameLayer, 8);
            var layW = SectionCommand.EnsureLayer(db, tr, XsecWaterNameLayer, 5);
            var kst = ImportGisCommand.EnsureKoreanTextStyle(db, tr);

            const int Steps = 80;                       // 왼쪽 끝에서 안쪽으로 훑는 횟수(상한)
            double span = System.Math.Max(1e-6, wl + wr);
            double step = span / Steps;

            foreach (var (vid, _, _) in views)
            {
                try
                {
                    if (tr.GetObject(vid, OpenMode.ForRead) is not CivilDb.SectionView sv) continue;
                    if (tr.GetObject(sv.SampleLineId, OpenMode.ForRead) is not CivilDb.SampleLine ln) continue;
                    double st = ln.Station;

                    foreach (ObjectId secId in ln.GetSectionIds())
                    {
                        try
                        {
                            if (tr.GetObject(secId, OpenMode.ForRead) is not CivilDb.Section sec) continue;
                            if (!kindOf.TryGetValue(sec.SourceId, out string kind)) continue;
                            bool water = kind == "지하수위";
                            if (kind != "지층" && !water) continue;
                            if (tr.GetObject(sec.SourceId, OpenMode.ForRead) is not CivilDb.TinSurface ts) continue;
                            string nm = StrataDraw.ShortName(ts.Name);
                            if (string.IsNullOrEmpty(nm)) continue;

                            // ★왼쪽 끝에서 안쪽으로 훑어 <b>지표면이 처음 답하는 자리</b>를 찾는다.
                            //   지층면은 시추공을 둘러싼 사각형이라 절단선 왼쪽 끝이 그 밖일 수 있다.
                            double useOff = double.NaN, useZ = double.NaN;
                            for (int k = 0; k <= Steps; k++)
                            {
                                double off = -wl + step * k;
                                double e = 0, nn = 0;
                                try { al.PointLocation(st, off, ref e, ref nn); } catch { continue; }
                                // ★[기억] 지표면 밖에서는 예외가 난다 — 그것이 곧 "밖"이라는 답이다.
                                try { useZ = ts.FindElevationAtXY(e, nn); }
                                catch { continue; }
                                if (double.IsNaN(useZ)) continue;
                                useOff = off; break;
                            }
                            if (double.IsNaN(useOff)) { noZ++; continue; }

                            // ★★★[JACK 0831 검토] <b>자리는 뷰에게 묻는다.</b>
                            //   앞 판은 <c>Section.SectionPoints[].Location</c>을 도면 좌표로 알고 썼는데,
                            //   되읽기가 <b>격자 밖이라 버린 것 162개</b>라고 말해 주었다 — <b>전부</b>였다.
                            //   즉 그 값은 내가 생각한 좌표계가 아니었다.
                            //   <c>FindXYAtOffsetAndElevation</c>은 이 파일이 중심축 숫자에 이미 쓰고 있는
                            //   <b>검증된 길</b>이다 — 뷰가 어디 놓였든 축척이 얼마든 맞는다.
                            double tx = 0, ty = 0;
                            if (!sv.FindXYAtOffsetAndElevation(useOff, useZ, ref tx, ref ty)) { noXY++; continue; }

                            var t = new DBText
                            {
                                TextString = nm,
                                Height = txtH,
                                Justify = AttachmentPoint.BottomLeft,   // 선 <b>위에</b> 얹는다
                            };
                            t.SetDatabaseDefaults(db);
                            var lay = water ? layW : layS;
                            if (!lay.IsNull) t.LayerId = lay;
                            if (!kst.IsNull) t.TextStyleId = kst;
                            // ★색은 그 층 선과 같게 — 글자와 선이 짝이라는 것이 한눈에 보여야 한다.
                            t.Color = Autodesk.AutoCAD.Colors.Color.FromColorIndex(
                                Autodesk.AutoCAD.Colors.ColorMethod.ByAci, water ? (short)5 : AciOfName(ts.Name));
                            var p = new Point3d(tx + gap, ty + gap, 0);
                            t.Position = p; t.AlignmentPoint = p;
                            ms.AppendEntity(t); tr.AddNewlyCreatedDBObject(t, true);
                            n++;
                        }
                        catch { }
                    }
                }
                catch { }
            }
            tr.Commit();
        }
        catch (System.Exception ex) { log?.AppendLine("  횡단 지층이름 실패 — " + ex.Message); return 0; }

        log?.AppendLine($"  횡단 지층이름 {n}개 — 각 선 <b>왼쪽 끝 위</b>에 직접 씀(종이 {StrataNameMm:0.#}mm × 축척 {scale:0.#} = 모형 {txtH:F2}m · 선과 같은 색)"
                      + (noZ > 0 ? $" · ⚠절단선 어디서도 지표면을 못 만난 것 {noZ}개" : "")
                      + (noXY > 0 ? $" · ⚠뷰가 자리를 못 준 것 {noXY}개(표고 범위 밖)" : ""));
        return n;
    }

    /// <summary>지표면 이름으로 그 층의 색을 고른다 — 선과 글자가 <b>같은 색</b>이어야 짝으로 읽힌다.</summary>
    private static short AciOfName(string surfName)
    {
        try
        {
            string nm = surfName ?? "";
            if (!nm.StartsWith(StrataDraw.SurfPrefix, System.StringComparison.Ordinal)) return 8;
            string rest = nm.Substring(StrataDraw.SurfPrefix.Length);
            int us = rest.IndexOf('_');
            if (!int.TryParse(us > 0 ? rest.Substring(0, us) : rest, out int o)) return 8;
            return StrataAciOf(o);
        }
        catch { return 8; }
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

    /// <summary>★★★[JACK 0828 "가로 가자"] <b>중심축 표고 숫자를 우리가 쓴다.</b>
    /// <para><b>왜 이 길인가</b>: Civil의 <c>CenterAxis.MajorTickStyle.OffsetX</c>는
    /// <b>Autodesk 래퍼 버그</b>로 왼쪽축과 같은 칸을 쓴다(<see cref="TickDiag"/>가 JACK 도면에서 확정:
    /// 왼쪽에 77mm를 쓰니 중심도 77mm가 됐고, 대조군인 크기는 안 따라왔다).
    /// 그 칸은 <b>화면이 중심축을 그릴 때 안 보는 칸</b>이라(90mm를 넣어도 숫자가 안 움직였다)
    /// <b>공개 API로 가는 길이 없다</b>.</para>
    /// <para>→ <b>Civil의 눈금 <c>글자만</c> 끄고 우리가 쓴다.</b> 축선과 눈금 자국은 Civil이 그대로 그린다 —
    /// 그건 잘 되고 있으므로 건드릴 이유가 없다. <b>안 되는 것 하나만</b> 가져온다.</para>
    /// <para><b>자리는 재서 얻는다.</b> <c>FindXYAtOffsetAndElevation(0, 표고)</c>가
    /// 오프셋 0(=노선 중심)의 도면 좌표를 알려 준다 — 가운데를 계산으로 짐작하지 않는다
    /// (좌우 폭이 다르면 기하 중심과 노선 중심이 어긋난다).</para>
    /// <para><b>표고 범위도 재서 얻는다.</b> <c>ElevationMin/Max</c>는 <b>자료 범위</b>라
    /// Civil이 그린 격자(90~120)와 다르다 — 이 함정은 v23.10 표고바에서 한 번 데었다.
    /// 경계상자의 위·아래 Y를 <c>FindOffsetAndElevationAtXY</c>로 <b>표고로 되돌려</b> 읽는다.</para></summary>
    private static int DrawCenterTickLabels(Database db,
                                            List<(ObjectId Id, double St, string Name)> views,
                                            ObjectId styleId, double scale, double annoScale,
                                            System.Text.StringBuilder log)
    {
        if (views == null || views.Count == 0) return 0;

        // ── ① Civil의 중심축 눈금 <b>글자</b>를 끈다. 축선·눈금 자국(35·36)은 그대로 둔다.
        //   ★[기억] 색을 바꾸며 <c>Visible</c>을 같이 켜서 회사가 꺼 둔 축이 전부 켜진 적이 있다.
        //   그래서 <b>여기서는 끄기만</b> 하고, 끈 것과 이미 꺼져 있던 것을 갈라 적는다.
        string offNote = "";
        if (!styleId.IsNull)
            try
            {
                using var trS = db.TransactionManager.StartTransaction();
                if (trS.GetObject(styleId, OpenMode.ForWrite) is CivilDb.Styles.SectionViewStyle st)
                    foreach (var (nm, t) in new (string, CivilDb.Styles.SectionViewDisplayStyleType)[]
                             { ("주", CivilDb.Styles.SectionViewDisplayStyleType.CenterAxisAnnotationMajor),
                               ("보조", CivilDb.Styles.SectionViewDisplayStyleType.CenterAxisAnnotationMinor) })
                    {
                        try
                        {
                            using var ds = st.GetDisplayStylePlan(t);
                            if (ds == null) { offNote += $" {nm}=없음"; continue; }
                            bool was = ds.Visible;
                            if (was) ds.Visible = false;
                            offNote += $" {nm}={(was ? "켜져 있던 것을 껐다" : "이미 꺼져 있었다")}";
                        }
                        catch (System.Exception e) { offNote += $" {nm}=X({e.GetType().Name})"; }
                    }
                trS.Commit();
            }
            catch (System.Exception ex) { offNote = " 끄기 실패 — " + ex.Message; }

        // 커밋 뒤 되읽기 — 켜진 채로 남으면 <b>Civil 숫자와 우리 숫자가 겹쳐</b> 두 벌이 된다.
        string backNote = "?";
        if (!styleId.IsNull)
            try
            {
                using var trB = db.TransactionManager.StartTransaction();
                if (trB.GetObject(styleId, OpenMode.ForWrite) is CivilDb.Styles.SectionViewStyle st2)
                {
                    using var ds2 = st2.GetDisplayStylePlan(
                        CivilDb.Styles.SectionViewDisplayStyleType.CenterAxisAnnotationMajor);
                    backNote = ds2 == null ? "성분 없음"
                             : ds2.Visible ? "⚠<b>아직 켜져 있다 — 숫자가 두 벌로 보인다</b>" : "꺼짐 확인";
                }
                trB.Commit();
            }
            catch { backNote = "되읽기 실패"; }

        // ── ② 우리가 쓴다.
        double txtH = TickTextMm / 1000.0 * scale;       // 종이 3mm → 모형
        double offX = TickOffsetMm / 1000.0 * scale;     // 종이 12mm → 모형 (축척만 곱한다)
        int n = 0, nView = 0, noRange = 0;
        double major = double.NaN;
        try
        {
            // 큰 눈금 간격은 <b>스타일에게 묻는다</b> — 이 속성은 번호가 제대로 갈라져 있다(버그 아님).
            if (!styleId.IsNull)
            {
                using var trI = db.TransactionManager.StartTransaction();
                if (trI.GetObject(styleId, OpenMode.ForWrite) is CivilDb.Styles.SectionViewStyle st3)
                {
                    using var cx = st3.CenterAxis;
                    try { major = cx.MajorTickStyle.Interval; } catch { }
                }
                trI.Commit();
            }
        }
        catch { }

        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            var ms = (BlockTableRecord)tr.GetObject(
                SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
            var layTx = SectionCommand.EnsureLayer(db, tr, XsecTextLayer, 7);
            var kst = ImportGisCommand.EnsureKoreanTextStyle(db, tr);
            var white = Autodesk.AutoCAD.Colors.Color.FromColorIndex(
                Autodesk.AutoCAD.Colors.ColorMethod.ByAci, 7);

            foreach (var (vid, _, _) in views)
            {
                try
                {
                    if (tr.GetObject(vid, OpenMode.ForRead) is not CivilDb.SectionView sv) continue;
                    nView++;

                    // <b>격자가 실제로 덮는 표고</b>를 되읽는다 — 자료 범위가 아니다.
                    var ext = ((Entity)sv).GeometricExtents;
                    double cx0 = (ext.MinPoint.X + ext.MaxPoint.X) / 2.0;
                    double o1 = 0, eLo = 0, o2 = 0, eHi = 0;
                    if (!sv.FindOffsetAndElevationAtXY(cx0, ext.MinPoint.Y, ref o1, ref eLo)
                     || !sv.FindOffsetAndElevationAtXY(cx0, ext.MaxPoint.Y, ref o2, ref eHi)
                     || !(eHi > eLo)) { noRange++; continue; }

                    double maj = (major > 1e-9) ? major : ((eHi - eLo) > 40.0 ? 10.0 : 5.0);
                    for (double e = System.Math.Ceiling(eLo / maj - 1e-9) * maj; e <= eHi + 1e-9; e += maj)
                    {
                        double tx = 0, ty = 0;
                        if (!sv.FindXYAtOffsetAndElevation(0.0, e, ref tx, ref ty)) continue;
                        var t = new DBText
                        {
                            TextString = e.ToString("0.##"),
                            Height = txtH,
                            Justify = AttachmentPoint.MiddleLeft,
                        };
                        t.SetDatabaseDefaults(db);
                        if (!layTx.IsNull) t.LayerId = layTx;
                        t.Color = white;      // ByLayer면 옛 도면에서 넘어온 레이어 색이 이긴다
                        if (!kst.IsNull) t.TextStyleId = kst;
                        var p = new Point3d(tx + offX, ty, 0);
                        t.Position = p; t.AlignmentPoint = p;
                        ms.AppendEntity(t); tr.AddNewlyCreatedDBObject(t, true);
                        n++;
                    }
                }
                catch { }
            }
            tr.Commit();
        }
        catch (System.Exception ex) { log?.AppendLine("  중심축 표고 숫자 실패 — " + ex.Message); return 0; }

        log?.AppendLine($"  중심축 표고 숫자 {n}개(뷰 {nView}개) — <b>우리가 직접 쓴다</b>"
                      + $" · 큰눈금 {(major > 1e-9 ? $"{major:0.##}m(스타일에서 읽음)" : "스타일을 못 읽어 범위로 정함")}"
                      + $" · 글자 종이 {TickTextMm:0.#}mm · <b>띄우기 종이 {TickOffsetMm:0.#}mm</b>(모형 {offX:F2}m)"
                      + $" · 레이어 '{XsecTextLayer}'"
                      + $"\n      Civil 눈금 글자 끄기:{offNote} → [커밋 뒤 확인: {backNote}]"
                      + (noRange > 0 ? $" · ⚠표고 범위를 못 읽은 뷰 {noRange}개" : "")
                      + "\n      ※Civil 래퍼 버그로 중심축 띄우기를 코드로 못 바꾼다 — 그래서 숫자만 가져왔다(축선·눈금은 Civil이 그린다)");
        return n;
    }

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
        double mL = SheetCommand.MarginLeft * sc;
        double mB = SheetCommand.MarginBottom * sc;
        // ★★[검토에서 잡힌 버그] 여기가 <b>524mm</b>로 계산하고 있었다 — 뷰를 놓는 쪽은
        //   <c>ViewH</c>(당시 419.2mm)를 쓰는데 도곽만 여백 뺀 전부를 썼다. <b>같은 네모를 두 크기로</b>
        //   재고 있었으니 칸선이 뷰와 맞을 리가 없다. 제목 자리를 도곽 쪽만 몰랐다.
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
                      + $" · 여백 좌 {SheetCommand.MarginLeft:F0}·우 {SheetCommand.MarginRight:F0}·상 {SheetCommand.MarginTop:F0}·하 {SheetCommand.MarginBottom:F0}mm"
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
    /// <para>종단도는 이 자리가 <b>120mm</b>(제목 40 + 여유 80)다 — 축척 막대·배너가 들어가서다.
    /// 그런데 횡단도 제목은 <c>토공 횡단면도(1/15)</c>와 <c>S=1:200</c> <b>두 줄</b>뿐이라 그만큼이 필요 없다.
    /// 남는 54.8mm를 내부 네모에 돌리면 칸이 그만큼 커지고, <b>축척이 한 단계 살아난다</b>
    /// (1×2 기준 필요 축척 153 → 134.5 = 1:200에서 1:150).</para>
    ///
    /// <para>★종단도의 <c>ViewH</c>는 <b>건드리지 않는다</b> — 그쪽은 그 배분이 맞다.</para></summary>
    // ★★[JACK 0827] 횡단은 <b>제목 40만</b> 쓴다 — 종단의 여유 80mm는 없다.
    //   자를 하나로: 제목 칸 치수는 <c>SheetCommand.TitleMm</c>가 정한다.
    private const double XsecTitleMm = SheetCommand.TitleMm;

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
    //   ★★★[JACK 0827 새 토적표] <b>열 비율은 이제 표가 들고 있다.</b>
    //   <see cref="DH.Grading.Core.QuantityTable.ColRatio"/> 하나만 고치면 폭·축척이 함께 따라온다 —
    //   여기 따로 적어 두면 표 모양을 바꿀 때 <b>한쪽만 고쳐 칸선과 글자가 어긋난다</b>.
    private static double[] QtColRatio => DH.Grading.Core.QuantityTable.ColRatio;

    /// <summary>표 글자 높이(종이 mm). <b>A3로 줄여 찍어도 1.8mm</b>가 되도록 잡았다 —
    /// 제본 도서는 보통 A3이고(A1의 정확히 절반), 감리가 자로 재는 것도 종이다.</summary>
    private const double QtTextMm = 3.6;

    /// <summary>표 한 줄 높이(종이 mm) — 글자가 줄 안에서 숨 쉴 만큼.</summary>
    /// <summary>줄 높이(종이 mm). ★[JACK 0827 "표가 너무 넓어"]
    /// <para>종전 5.81mm는 <b>글자 3.6mm에 여유가 거의 없는</b> 값이었다. 새 표는 칸이 일곱이라
    /// 폭이 크게 늘었는데 줄 높이는 그대로여서 <b>가로:세로가 3.4:1</b>로 납작해졌다
    /// (JACK 원본은 <b>1.4:1</b>). 글자의 두 배쯤을 주면 원본에 가까워진다.</para></summary>
    private const double QtRowH = 7.4;

    /// <summary>표 전체 높이(종이 mm) — <b>머리줄이 1.4배</b>인 것까지 센다.
    /// ★한 곳에서만 정한다: 자리 잡는 쪽과 그리는 쪽이 다르게 세면 표가 어긋난 자리에 앉는다.</summary>
    /// <summary>★★★[JACK 0831] 표 높이(종이 mm)는 <b>실제 줄 수</b>가 정한다.
    /// <para>종전엔 못 박은 13줄이었다. 이제 현장이 무엇이냐에 따라 줄이 늘고 주므로
    /// <b>축척이 그 값을 읽어야</b> 표가 커진 만큼 그림이 작아진다.</para>
    /// <para><c>+0.4</c>는 머리줄이 본문보다 1.4배 높기 때문이다 — 자리 잡는 쪽과 그리는 쪽이
    /// 이 값을 <b>같이</b> 써야 어긋나지 않는다(§50에서 2.3mm 어긋난 적이 있다).</para></summary>
    private static double QtTableHmmOf(int totalRows) => QtRowH * (totalRows + 0.4);

    /// <summary>표 전체 폭(종이 mm) — 열 너비의 합이다. 축척 계산이 이 값을 쓴다.</summary>
    /// <summary>표 폭(종이 mm). ★[JACK 0827] 새 표는 <b>일곱 칸 두 단</b>이라 종전보다
    /// <b>훨씬 넓고 낮다</b>(비율 합 25.5 → 70, 줄 19 → 13).
    /// <para>그래서 <b>오른쪽에 두면</b> 그림 폭을 크게 잡아먹고, <b>아래에 두면</b> 덜 먹는다 —
    /// 축척 고르기가 그 둘을 재서 나은 쪽을 택하므로(<c>tableRight</c>) 값만 맞으면 배치는 따라온다.</para></summary>
    private static double QtWidthMm
    {
        get { double s = 0; foreach (double r in QtColRatio) s += r; return s * QtTextMm; }
    }

    /// <summary>★★[JACK 0831] 접은 표의 폭 — <b>칸마다 제 몫의 폭</b>을 더한다.
    /// <para>단을 늘리면 폭이 늘어난다. 축척이 이 값을 읽으므로 <b>여기 하나만</b> 맞으면 된다.</para></summary>
    private static double QtWidthMmOf(DH.Grading.Core.QtyTableFold fold)
    {
        double s = 0;
        foreach (int ix in fold.ColRatioIndex) s += QtColRatio[ix];
        return s * QtTextMm;
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
                                     double bandPaperMm,
                                     double sc, bool onRight, double gapM,
                                     QtyResult qty, DH.Grading.Core.QtyTableFold fold,
                                     System.Text.StringBuilder log)
    {
        // ★[검토 0828 · LOW-1] <b>안 쓰는 계수기를 지웠다.</b> 대입만 하고 로그에 안 써서
        //   C# 경고도 안 났다 — "경고 0개"가 못 잡는 종류다.
        int tbIn = 0;
        string firstTbErr = null;
        if (views == null || views.Count == 0) return 0;
        // ★★★[JACK 0831] 줄 수는 <b>현장이 정한다</b> — 못 박지 않는다.
        var spec = qty.Spec ?? DH.Grading.Core.QtyTableSpec.BuildFromKeys(
            System.Array.Empty<DH.Grading.Core.QtyKey>(), null, DH.Grading.Core.QuantityTable.DeepLimitM);
        // ★★★[JACK 0831] 줄 수·칸 수는 <b>접기</b>가 정한다.
        int nRow = fold.BodyRows + 1;                  // 머리 1줄 + 내용 N줄
        int nCol = fold.Cols;                          // 7(안 접음) · 10 · 11
        double txtH = QtTextMm * sc;                   // 글자 높이(모형)
        double rowH = QtRowH * sc;                     // 줄 높이
        // ★[JACK 0827] 열 비율은 <b>표가 들고 있다</b> — 표 모양이 바뀌면 폭도 같이 바뀐다.
        //   여기 따로 적어 두면 표를 고칠 때 한쪽만 고쳐 <b>칸선과 글자가 어긋난다</b>.
        var colW = new double[nCol];
        for (int c = 0; c < nCol; c++) colW[c] = QT.ColRatio[fold.ColRatioIndex[c]] * txtH;
        // ★★[JACK 0828 · 검토] 종전 꼬리말은 <c>(값은 아직 '–')</c>를 <b>조건 없이</b> 찍었다 —
        //   값이 실제로 들어가고 있는데도 로그만 "아직 비었다"고 말했다.
        //   낡은 문구가 남아 <b>고친 뒤에도 안 고쳐진 것처럼</b> 보이게 만든다. → <b>세어서 말한다.</b>
        int n = 0, nQty = 0, nCells = 0;
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
                    // ★[JACK 0827] 칸 수도 <b>표가 정한다</b> — 여기 숫자를 박아 두면
                    //   표를 넓힐 때 이 한 줄 때문에 <b>표가 통째로 안 만들어진다</b>(실측: 0개).
                    tb.SetSize(nRow, nCol);
                    if (!layE.IsNull) tb.LayerId = layE;
                    for (int c = 0; c < nCol; c++) tb.Columns[c].Width = colW[c];
                    // ★[JACK 0826 스샷 "제목 셀은 … 셀 높이 조금 높일 것"]
                    //   머리줄만 <b>1.4배</b>로 — 표의 얼굴이라 다른 줄과 같으면 묻힌다.
                    for (int r = 0; r < nRow; r++) tb.Rows[r].Height = r == 0 ? rowH * 1.4 : rowH;

                    // 글자 모양은 셀 단위로 — 표 전체에 한 번에 건다.
                    for (int r = 0; r < nRow; r++)
                        // ★[JACK 0827 "글씨 색상도 좌우측이 달라"] <b>칸 수를 또 4로 박아 뒀다.</b>
                        //   오른쪽 세 칸이 글자 높이·글꼴·색을 <b>하나도 못 받고</b> 있었다.
                        for (int c = 0; c < nCol; c++)
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

    
                    // ── 머리줄: '측 점'이 세 칸을 먹고, 오른쪽 칸에 측점 이름
                    // ★[JACK 0826] 머리줄을 <b>한 칸</b>으로 — <c>측 점(No.1+10.00)</c> 꼴.
                    //   측점명을 괄호 안에 넣으면 제목과 이름이 <b>한눈에 한 덩이</b>로 읽힌다.
                    try { tb.MergeCells(CellRange.Create(tb, 0, 0, 0, nCol - 1)); } catch { }
                    tb.Cells[0, 0].TextString =
                        QT.HeaderLeft
                        + (string.IsNullOrEmpty(vname) ? "" : $"({vname})");
                    // ★[JACK 0826 스샷 "제목 셀은 음각으로 표현(셀 채우기)"]
                    //   바탕을 칠하고 글자를 검게 — 인쇄하면 흰 바탕에 검은 글씨가 뒤집혀 <b>음각</b>이 된다.
                    for (int c = 0; c < nCol; c++)
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
                    static string Fmt(double v) => double.IsNaN(v) || System.Math.Abs(v) < QtyShowMin
                        ? QT.Blank : v.ToString("0.00");
                    DH.Grading.Core.QtyLedger led = null;
                    if (vname != null && qty.Ledgers != null) qty.Ledgers.TryGetValue(vname, out led);
                    // ★★★[검토 0828 · HIGH-C] <b>실제로 숫자가 들어간 칸을 센다.</b>
                    //   사전에 담겼느냐로 세면 표 28장이 전부 <c>–</c>여도 "값이 들어갔다"고 찍힌다.
                    int filled = 0;

                    // ── ★★★[JACK 0831 "표를 어떻게 하면 빈 셀이 없게" · "셀 합치기가 이상하게 됐어"]
                    //   <b>단(段)마다 제 구간만 그린다.</b> 접기(<c>QtyTableFold</c>)가
                    //   "어느 목록의 어디부터 몇 줄을, 어느 칸에" 놓을지 이미 정해 두었다.
                    //   몇 줄을 먹느냐도 얼개가 안다 — <b>단 끝을 넘지 않게</b> 한계를 같이 넘긴다.
                    //   이 셈이 도면 쪽에만 있었을 땐 하니스가 못 잡아 표가 찌그러졌다(S85·S87).
                    foreach (var seg in fold.Segs)
                    {
                        int segEnd = seg.From + seg.Count;
                        for (int i = 0; i < seg.Count; i++)
                        {
                            int src = seg.From + i;
                            int row = i + 1;
                            int c0 = seg.Col;

                            void Put(int col, string text, int rowSpan, int colSpan)
                            {
                                if (rowSpan <= 0) return;               // 위 칸이 먹은 자리
                                if (rowSpan > 1 || colSpan > 1)
                                    try
                                    {
                                        tb.MergeCells(CellRange.Create(tb, row, col,
                                                                       row + rowSpan - 1, col + colSpan - 1));
                                    }
                                    catch { }
                                // <c>|</c>는 얼개가 쓰는 <b>줄바꿈 표시</b>다.
                                if (text != null) tb.Cells[row, col].TextString = text.Replace("|", "\\P");
                            }

                            if (seg.Left)
                            {
                                var Lr = spec.Left[src];
                                Put(c0, Lr.Group, spec.SpanGroup(src, segEnd), spec.GroupTakesTwo(src) ? 2 : 1);
                                Put(c0 + 1, Lr.Sub, spec.SpanSub(src, segEnd), 1);
                                if (Lr.Item != null) tb.Cells[row, c0 + 2].TextString = Lr.Item;
                                // ★[검토 MED-6] 채움 줄에는 아무것도 안 쓴다 —
                                //   <c>–</c>는 "해당 없음"인데 그 줄엔 해당할 항목 자체가 없다.
                                if (!spec.IsFillerLeft(src))
                                {
                                    double vL = (led != null && Lr.Key is DH.Grading.Core.QtyKey kk)
                                                ? led.Get(kk) : double.NaN;
                                    string tL = Fmt(vL);
                                    if (tL != QT.Blank) filled++;
                                    tb.Cells[row, c0 + 3].TextString = tL;
                                }
                            }
                            else
                            {
                                var Rr = spec.Right[src];
                                Put(c0, Rr.Item, spec.SpanRight(src, segEnd), spec.RightTakesTwo(src) ? 2 : 1);
                                if (Rr.Sub != null) tb.Cells[row, c0 + 1].TextString = Rr.Sub;
                                // ★[검토 MED-7] 오른쪽 값은 아직 통로가 없다(공종 수량은 STEP 4).
                                if (!spec.IsFillerRight(src)) tb.Cells[row, c0 + 2].TextString = QT.Blank;
                            }
                        }
                    }
                    // 한 칸이라도 숫자가 든 표를 <b>값이 든 표</b>로 친다. 칸 수도 함께 센다.
                    if (filled > 0) nQty++;
                    nCells += filled;

                    // ★★★[JACK 0827 "색상을 보면 알겠지만 적용이 안 됐어"] <b>표에 직접 쓴다.</b>
                    //   <c>tb.Cells[r,c].Borders…</c>로 세 번 시도했지만(값 쓰기 → <c>Overrides</c> 켜기 →
                    //   병합 뒤로 옮기기) 하나도 안 먹었다. <c>Cell</c>을 거치는 길이 막힌 것이다.
                    //   <c>Table</c>에 <b>직접 쓰는 메서드</b>가 따로 있다:
                    //   <c>SetGridColor(줄, 칸, 격자종류, 색)</c> · <c>SetContentColor(줄, 칸, 순번, 색)</c>.
                    //   <c>GridLineType</c>은 <c>OuterGridLines</c>·<c>InnerGridLines</c>처럼
                    //   <b>바깥/안쪽을 이미 갈라 놓았다</b> — 우리가 네 변을 따로 셀 필요가 없다.
                    //   <b>병합·내용을 다 채운 뒤</b>라야 남는다(병합이 테두리를 다시 계산한다).
                    try
                    {
                        var green = Autodesk.AutoCAD.Colors.Color.FromColorIndex(
                            Autodesk.AutoCAD.Colors.ColorMethod.ByAci, 3);
                        var red = Autodesk.AutoCAD.Colors.Color.FromColorIndex(
                            Autodesk.AutoCAD.Colors.ColorMethod.ByAci, 1);
                        var white = Autodesk.AutoCAD.Colors.Color.FromColorIndex(
                            Autodesk.AutoCAD.Colors.ColorMethod.ByAci, 7);

                        for (int r = 0; r < nRow; r++)
                            for (int c = 0; c < nCol; c++)
                            {
                                // 안쪽 격자는 빨강 가늘게. 바깥도 일단 빨강으로 칠하고 아래에서 가장자리만 덮는다.
                                try { tb.SetGridColor(r, c, GridLineType.InnerGridLines, red); tbIn++; } catch { }
                                try { tb.SetGridColor(r, c, GridLineType.OuterGridLines, red); } catch { }
                                try { tb.SetGridLineWeight(r, c, GridLineType.AllGridLines, LineWeight.LineWeight013); } catch { }
                                // 글자는 흰색 — 머리줄(0)은 초록 바탕에 어두운 글자라 건드리지 않는다.
                                if (r > 0)
                                    try { tb.SetContentColor(r, c, 0, white); } catch { }
                            }

                        // 표의 가장자리만 초록 굵게. 안쪽 칸의 바깥선이 아니라 <b>표 둘레</b>다.
                        for (int c = 0; c < nCol; c++)
                        {
                            try { tb.SetGridColor(0, c, GridLineType.HorizontalTop, green); } catch { }
                            try { tb.SetGridColor(nRow - 1, c, GridLineType.HorizontalBottom, green); } catch { }
                            try { tb.SetGridLineWeight(0, c, GridLineType.HorizontalTop, LineWeight.LineWeight050); } catch { }
                            try { tb.SetGridLineWeight(nRow - 1, c, GridLineType.HorizontalBottom, LineWeight.LineWeight050); } catch { }
                        }
                        for (int r = 0; r < nRow; r++)
                        {
                            try { tb.SetGridColor(r, 0, GridLineType.VerticalLeft, green); } catch { }
                            try { tb.SetGridColor(r, nCol - 1, GridLineType.VerticalRight, green); } catch { }
                            try { tb.SetGridLineWeight(r, 0, GridLineType.VerticalLeft, LineWeight.LineWeight050); } catch { }
                            try { tb.SetGridLineWeight(r, nCol - 1, GridLineType.VerticalRight, LineWeight.LineWeight050); } catch { }
                        }
                    }
                    catch (System.Exception ex) { firstTbErr ??= "색: " + ex.Message; }

                    double px = onRight ? ext.MaxPoint.X + gapM : ext.MinPoint.X;
                    double midY = (ext.MinPoint.Y + ext.MaxPoint.Y) / 2.0;
                    // ★★★[검토 0827 · CRITICAL] <b>표는 그래프가 아니라 <u>밴드</u> 밑에 붙는다.</b>
                    //   <c>ext.MinPoint.Y</c>는 <b>그래프 바닥</b>이다 — 경계상자에 밴드가 없기 때문이다(실측).
                    //   ★[검토 0828 · LOW-2] 이 숫자들은 <b>밴드가 10mm이던 시절</b>의 것이다.
                    //   지금은 칸이 4mm(3칸 12mm)라 TableGapMm(12mm)과 <b>더는 안 겹친다</b> —
                    //   <b>코드는 맞고 근거만 낡았다</b>. 낡은 근거를 남겨 두면 다음 사람이
                    //   "왜 이렇게 했지"에 잘못된 답을 얻는다.
                    //   → 밴드 높이만큼 <b>더 내린다</b>. 예산(<c>padH</c>)도 같은 숫자를 쓰므로 갈라지지 않는다.
                    double bandM = bandPaperMm * sc;   // 종이 mm → 모형 m
                    // ★★★[JACK 0831 · 검토 MED-5] <b>예산과 실제 자리가 서로 다른 기준을 썼다.</b>
                    //   자리를 잡는 쪽은 표가 <b>덩어리 아랫변에서 위로</b> 자란다고 보고 칸을 예약하는데,
                    //   그리는 쪽은 표를 <b>그래프 한가운데</b>에 맞췄다.
                    //   표가 그래프+밴드보다 길어지면 그 차이만큼 <b>아랫칸을 침범한다</b> —
                    //   종전엔 표가 13줄 고정이라 드물었지만, 이제 줄이 늘고 주므로 <b>상시</b>다.
                    //   (검토 실측: 밴드 12mm·그래프 100mm·표 217.6mm → 46.8mm 침범)
                    //   → <b>예산이 쓰는 기준</b>으로 맞춘다: 표 아랫변 = 덩어리 아랫변.
                    double tableH = rowH * (nRow + 0.4);
                    double botY2 = ext.MinPoint.Y - bandM;          // 밴드까지 포함한 덩어리 아랫변
                    double py = onRight
                        ? botY2 + tableH                            // 아랫변을 맞추고 위로 자란다
                        : ext.MinPoint.Y - bandM - gapM;
                    tb.Position = new Point3d(px, py, 0);
                    // ★[JACK 0826] <c>GenerateLayout()</c>을 <b>부르지 않는다</b> — 그것이 행 높이를
                    //   글자와 여백에서 <b>다시 계산</b>해, 우리가 지정한 높이를 덮어쓴다.
                    ms.AppendEntity(tb); tr.AddNewlyCreatedDBObject(tb, true);
                    n++;
                }
                catch (System.Exception ex) { firstTbErr ??= ex.GetType().Name + ": " + ex.Message; }
            }
            tr.Commit();
        }
        catch (System.Exception ex) { log?.AppendLine("  수량표 그리기 실패 — " + ex.Message); }
        double tw = 0; foreach (double w in colW) tw += w;
        if (n == 0 && firstTbErr != null) log?.AppendLine("  ⚠수량표를 하나도 못 만들었다 — " + firstTbErr);
        log?.AppendLine($"  수량표 {n}개 · {nRow}줄 · AutoCAD Table 객체(셀 병합·열 너비를 표가 관리한다)"
                      + $" · 글자 {QtTextMm:0.##}mm · 줄 {QtRowH:0.##}mm · 폭 {tw / sc:F0}mm"
                      + $" · <b>숫자가 든 표 {nQty}/{n}개</b> · 채워진 칸 {nCells}개 · 안쪽 칸선 색 {tbIn}곳"
                      + (nQty < n ? $" (나머지 {n - nQty}장은 잰 것이 없어 전부 '{QT.Blank}')" : ""));
        // ★★[JACK 0828] <b>표의 두 규칙을 매번 물어보고 남긴다.</b>
        //   <c>SpansValid</c>는 만들어 두고 <b>아무도 안 불러</b> 죽어 있었다 —
        //   검사는 <b>돌아야</b> 검사다. 로그 한 줄이면 다음 사람이 표를 고칠 때 바로 걸린다.
        // ★★★[JACK 0831 · 검토 HIGH-1] <b>검사가 그리지 않는 표를 재고 있었다.</b>
        //   <c>QT.SpansValid()</c>는 <b>죽은 상수 배열</b>(12줄 고정)을 본다 —
        //   우리가 그리는 것은 이제 <c>spec</c>인데, 그것이 아무리 찌그러져도
        //   "세로 병합 맞음"이 <b>늘</b> 찍혔다. §53에서 겪은 <b>"검사가 엉뚱한 것을 재고 있었다"</b>
        //   그대로다 — 이번엔 하니스가 아니라 로그 쪽에서 되풀이됐다.
        //   → <b>그린 얼개를 묻는다</b>(<c>QtyTableSpecRules.Holds</c>).
        bool paired = QT.WidthsPaired(out string wnote);
        bool rules = DH.Grading.Core.QtyTableSpecRules.Holds(spec, out string rnote);
        bool rect = spec.Left.Count == spec.Right.Count;
        // ★★★[JACK 0831] <b>병합이 겹치는지 매번 묻는다.</b> 겹치면 AutoCAD가 뒤 병합을
        //   조용히 버려 표가 찌그러진다 — 화면을 봐야만 알던 것을 로그가 먼저 말하게 한다.
        // ★접은 표를 묻는다 — 안 접은 판을 물으면 §53의 "엉뚱한 것을 재는 검사"가 된다.
        bool merges = spec.MergesValid(fold, out string mnote);
        log?.AppendLine($"    표 규칙 — 좌우 짝 폭 {(paired ? "맞음" : "⚠<b>어긋남</b>")}({wnote})"
                      + $" · 늘 서는 줄 {(rules ? "맞음" : $"⚠<b>어긋남 — {rnote}</b>")}"
                      + $" · 두 단 길이 {(rect ? "같음" : $"⚠<b>{spec.Left.Count}/{spec.Right.Count}</b>")}"
                      + $" · 셀 합치기 {(merges ? "안 겹침" : $"⚠<b>겹침 — {mnote}</b>")}"
                      + $" · <b>이번 도면 {nRow}줄 × {nCol}칸</b> — {fold.Note}");
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


    /// <summary>★★★[검토 0828 · HIGH-A] <b>시험은 화면이 쓰는 그 스타일에 해야 한다.</b>
    /// <para><see cref="SectionCommand.PickStyle"/>은 이름을 못 찾으면 <b>컬렉션의 첫 스타일을 그냥 돌려준다</b>
    /// (<c>return first;</c>). 컬렉션이 비었을 때만 <c>Null</c>이라,
    /// <c>if (sid.IsNull) "못 찾았습니다"</c> 같은 방어는 <b>그 이유로는 영영 안 걸린다</b> —
    /// 이름이 안 맞으면 <b>조용히 남의 스타일에 쓴다</b>.</para>
    /// <para>진단 명령에서 이건 치명적이다. 엉뚱한 스타일에 100mm를 써 놓고 대화상자에서
    /// "중심이 그대로네"를 보면 <b>"경우 C 확정"이라는 틀린 결론</b>으로 간다 —
    /// 이 프로젝트가 오늘만 두 번 당한 <b>"화면이 쓰는 것과 다른 물건을 쟀다"</b> 그 함정이다.</para>
    /// <para>→ <b>도면에 놓인 횡단면도에게 직접 묻는다.</b> 뷰가 달고 있는 스타일이 곧 화면을 정하는 것이다.
    /// 뷰가 없으면 <b>이름이 정확히 맞는</b> 스타일만 받고, 그것도 없으면 <b>Null을 돌려준다</b> —
    /// 아무거나 집어 주느니 <b>못 찾았다고 말하는 편</b>이 낫다.</para></summary>
    private static ObjectId DiagStyleId(Database db, out string how)
    {
        how = "못 찾음";
        // ① 도면에 놓인 횡단면도가 쓰는 스타일 — 이것이 화면을 정한다.
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            var ms = (BlockTableRecord)tr.GetObject(
                SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
            foreach (ObjectId id in ms)
            {
                try
                {
                    if (tr.GetObject(id, OpenMode.ForRead) is not CivilDb.SectionView sv) continue;
                    ObjectId st = sv.StyleId;
                    if (st.IsNull) continue;
                    string nm = "?";
                    try { if (tr.GetObject(st, OpenMode.ForRead) is CivilDb.Styles.StyleBase sb) nm = sb.Name ?? "?"; }
                    catch { }
                    tr.Commit();
                    how = $"도면의 횡단면도가 쓰는 스타일 '{nm}'";
                    return st;
                }
                catch { }
            }
            tr.Commit();
        }
        catch { }
        // ② 뷰가 없으면 <b>이름이 정확히 맞는</b> 것만. 비슷한 것을 집어 주지 않는다.
        try
        {
            var cdoc = CivilApp.CivilApplication.ActiveDocument;
            using var tr = db.TransactionManager.StartTransaction();
            foreach (ObjectId id in cdoc.Styles.SectionViewStyles)
            {
                try
                {
                    if (tr.GetObject(id, OpenMode.ForRead) is CivilDb.Styles.StyleBase sb
                        && string.Equals(sb.Name, XsecViewStyleName, System.StringComparison.Ordinal))
                    {
                        tr.Commit();
                        how = $"횡단면도가 없어 이름으로 찾은 스타일 '{XsecViewStyleName}'";
                        return id;
                    }
                }
                catch { }
            }
            tr.Commit();
        }
        catch { }
        return ObjectId.Null;
    }

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

    /// <summary>눈금값 글자 크기 — <b>종이에서 보이길 바라는 크기</b>(mm).
    /// <para>★★[JACK 0827 "1:150만 되어도 상당히 작아서 잘 안 보여. 1:100으로 볼 때가 적당했다"]
    /// 종전 2.5mm는 <b>스타일에 넣는 값</b>이었지 종이에 나오는 크기가 아니었다.</para>
    /// <para>Civil 스타일 글자는 <b>주석 축척으로 모형에 그려지고</b>, 우리는 횡단을 <b>다른 축척</b>으로
    /// 배치하므로 종이에서는 <c>스타일값 × 주석축척 ÷ 횡단축척</c>이 된다 —
    /// 주석 1:120에 횡단 1:150이면 <b>2.0mm</b>로 줄어든다. 1:100이면 3.0mm였고 그것이 적당했다.</para>
    /// <para>→ <b>목표를 종이 크기로 적고</b> <see cref="PaperToStyle"/>이 되돌려 계산한다.
    /// 그러면 축척이 무엇이든 종이에서 같아진다.</para></summary>
    private const double TickTextMm = 3.0;

    /// <summary>★★[JACK 0827] 밴드 <b>칸 높이</b>(종이 mm).
    /// <para>★★★[JACK 0828 "측점·GL·FGL 밴드 간격이 너무 넓어. 글씨끼리 거의 딱 붙게 바꿔.
    /// 너무 넓어서 그래프가 작아지는 현상이야"] <b>여유가 곧 손해다.</b>
    /// 밴드 칸은 <b>칸 수 × 이 값</b>만큼 축척 예산에서 통째로 빠지므로,
    /// 한 칸에서 1mm를 아끼면 세 칸짜리 그림에 3mm가 돌아온다.</para>
    /// <para>글자가 <see cref="BandTextMm"/>(3mm) <b>한 줄</b>이니 위아래 0.5mm씩만 남기면 된다 —
    /// <b>4mm</b>. 종전 10mm는 "두 줄이 들어갈 만큼"이라고 잡아 둔 것인데, 실제로 들어가는 것은
    /// 한 줄뿐이라 <b>쓰지도 않는 18mm가 그림을 깎고 있었다</b>(3칸 × 6mm).</para>
    /// <para>이것도 <b>종이 기준</b>이라 축척 보정을 받는다 — 글자만 키우고 칸을 그대로 두면 눌린다.</para></summary>
    private const double BandHeightMm = 4.0;

    /// <summary>★★★[JACK 0827 "먼저 밴드 3칸 길이만큼을 고려해서 전체 축척부터 맞추고 시작하는 게 좋겠어"]
    /// <b>밴드 칸 수를 미리 잡는다 — 측점·GL·FGL 셋.</b>
    /// <para>지금 도면에는 두 칸(GL·FGL)뿐이지만, 측점 칸을 나중에 더할 때
    /// <b>축척이 다시 바뀌면 도면을 새로 뽑아야 한다</b>. 처음부터 셋 자리를 비워 두면
    /// 칸을 더해도 그림이 그대로다.</para></summary>
    private const int BandRows = 3;

    // ★[검토 0828] <c>BandTotalMm</c>을 지웠다 — <b>아무도 안 쓰는 죽은 값</b>이었다.
    //   실제 예산은 <b>도면에서 센 칸 수</b>로 그때그때 잡는다(<c>bandRows × BandHeightMm</c>) —
    //   3칸을 미리 박아 둔 이 값을 쓰면 두 칸짜리 도면에서 <b>쓰지도 않는 자리를 깎는다</b>.
    //   죽은 값을 남겨 두면 다음 사람이 <b>그게 진짜 예산인 줄 알고</b> 고친다.

    /// <summary>★★★[JACK 0828 "측점 밴드 부분이 너무 수직축 마지막하고 붙어서 겹쳐 보여"]
    /// <b>그래프 바닥과 첫 밴드 칸 사이의 틈</b>(종이 mm).
    /// <para>수직축의 맨 아래 눈금값은 <b>축 끝보다 조금 더 내려와</b> 찍힌다 — 글자의 절반쯤이
    /// 그래프 밖으로 나온다. 밴드가 바닥에 딱 붙으면 그 글자와 첫 칸의 글자가 <b>겹친다</b>.
    /// 눈금 글자 <see cref="TickTextMm"/>(3mm)의 절반보다 조금 넉넉한 <b>2mm</b>면 떨어진다.</para>
    /// <para>이 값은 밴드 <b>칸 높이가 아니라 칸 앞의 빈틈</b>이라 축척 예산에는 안 들어간다 —
    /// 그림을 깎지 않으면서 겹침만 푼다.</para></summary>
    private const double BandGapMm = 2.0;

    /// <summary>GL·FGL 밴드 값 글자 — 눈금값과 <b>같은 크기</b>로 맞춘다.
    /// <para>한 그림 안에서 축 눈금과 밴드 값이 다른 크기면 눈에 거슬린다.</para></summary>
    private const double BandTextMm = 3.0;

    /// <summary><b>종이에서 바라는 크기 → 스타일에 넣을 값.</b>
    /// <para>Civil 스타일 값은 <b>종이 미터</b>이고 주석 축척으로 모형에 커진다.
    /// 우리는 주석 축척을 <b>안 건드리므로</b>(종단 1:120과 횡단 1:200이 한 도면에 공존해야 한다)
    /// 그 어긋남을 <b>글자 값에서 되돌린다</b>.</para>
    /// <para>주석 축척을 못 읽으면 보정 없이 종이 크기 그대로 — 적어도 종전과 같아진다.</para></summary>
    private static double PaperToStyle(double paperMm, double scale, double annoScale)
        => annoScale > 1e-9 ? paperMm / 1000.0 * scale / annoScale : paperMm / 1000.0;

    /// <summary>눈금값을 축에서 띄우는 거리(종이 mm). ★<b>배치·축척과 무관한 고정값</b>이다 —
    /// 종이 기준이라 어떤 축척에서도 종이에서 같은 거리로 보인다.
    /// <para>★★★[JACK 0828 "떨어져 있어. 그런데 너무 떨어져 있어"] <b>12 → 3mm.</b>
    /// 12mm는 JACK이 대화상자에 넣어 두셨던 값인데, 그때는 <b>중심축에 안 먹던 값</b>이라
    /// 화면으로 확인된 적이 없었다 — <see cref="DrawCenterTickLabels"/>가 실제로 그리기 시작하니
    /// <b>비로소 크기가 보였고, 너무 멀었다</b>. "설정돼 있던 값"이 곧 "확인된 값"은 아니다.</para>
    /// <para>★[JACK 0828 "눈금 바로 옆에서 숫자가 시작되어야 해"] <b>숫자를 못 박지 않고
    /// <see cref="TickMajorMm"/>에 묶는다.</b> 눈금 자국은 축에서 <b>오른쪽으로</b> 뻗고
    /// 숫자도 오른쪽에 앉으므로, 띄우기가 <b>자국 길이와 같으면</b> 숫자가 자국이 끝나는 바로 그 자리에서 시작한다.
    /// 눈금 크기를 나중에 키우면 띄우기가 <b>저절로 따라간다</b> —
    /// 숫자를 따로 적어 두면 눈금만 키웠을 때 글자가 자국에 파묻힌다(0827에 이미 겪었다).</para></summary>
    private const double TickOffsetMm = TickMajorMm;

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
    /// <summary>★★★[JACK 0827] <b>눈금값·밴드 글자를 종이 기준으로 맞춘다.</b>
    /// <para>축척을 알아야 하므로 <see cref="TuneAxisTicks"/>와 <b>따로</b> 돈다 —
    /// 스타일을 입혀야 뷰를 재고, 재야 축척이 정해지기 때문이다.</para></summary>
    /// <summary>★★★[JACK 0827 "밴드 라벨 스타일이 잘 작동하는지 로그 심어"]
    /// <b>GL·FGL 글자에 닿을 수 있는지 먼저 잰다.</b>
    /// <para>경로가 세 겹이다 — <b>밴드 → 라벨 스타일 → 글자 칸(Text 컴포넌트)</b>.
    /// 어느 라벨 스타일이 값을 찍는지도 아직 모른다(주 눈금? 보조 눈금? 다른 것?).</para>
    /// <para>그래서 <b>여섯 종류를 다 훑어</b> 이름·현재 크기·쓰기 성공 여부를 남긴다.
    /// 한 판 돌리면 <b>어디에 손대야 하는지</b>가 확정된다 — 추측으로 고르지 않는다.</para>
    /// <para>쓰기도 <b>실제로 해 본다</b>. 되읽어 값이 남았는지가 곧 답이다 —
    /// 이 프로젝트는 "썼다"와 "남았다"가 다른 경우를 여러 번 겪었다.</para></summary>
    private static void ProbeBandText(Database db, List<(ObjectId Id, double St, string Name)> views,
                                      double scale, double annoScale, System.Text.StringBuilder log)
    {
        if (views == null || views.Count == 0) return;
        double want = PaperToStyle(BandTextMm, scale, annoScale);
        var sb = new System.Text.StringBuilder();
        int nBand = 0, nStyle = 0, nRead = 0, nWrote = 0, nStuck = 0, nHt = 0;
        string firstErr = null;
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            if (tr.GetObject(views[0].Id, OpenMode.ForRead) is not CivilDb.SectionView sv)
            { tr.Commit(); log?.AppendLine("  밴드 글자 조사: 뷰를 못 열었다"); return; }

            // ★★★[JACK 0827 "GL 위에 측점도 넣어야 해 · 칸만 비워 두고 우리가 글씨를 그린다"]
            //   칸을 더하려면 <b>어떤 밴드 스타일이 도면에 있는지</b> 알아야 한다.
            //   <c>Add(Database, BandType, 스타일이름)</c>이라 이름을 정확히 대야 하기 때문이다.
            //   ※지금은 <b>목록만 남긴다</b> — 고르는 것은 그 목록을 보고 정한다.
            try
            {
                var cd0 = CivilApp.CivilApplication.ActiveDocument;
                var col0 = cd0.Styles.BandStyles.SectionViewSectionDataBandStyles;
                var nm0 = new System.Text.StringBuilder();
                for (int q = 0; q < col0.Count; q++)
                    try { if (tr.GetObject(col0[q], OpenMode.ForRead) is CivilDb.Styles.StyleBase b0) nm0.Append($" · {b0.Name}"); }
                    catch { }
                log?.AppendLine($"  도면의 횡단 밴드 스타일 {col0.Count}개:{nm0}");
            }
            catch (System.Exception ex) { log?.AppendLine("  횡단 밴드 스타일 목록 실패 — " + ex.Message); }

            using var items = sv.Bands.GetBottomBandItems();
            for (int i = 0; i < items.Count; i++)
            {
                nBand++;
                string bn = "?";
                ObjectId bsId = ObjectId.Null;
                try { bsId = items[i].BandStyleId; } catch { }
                try
                {
                    if (tr.GetObject(bsId, OpenMode.ForWrite) is CivilDb.Styles.StyleBase sb0) bn = sb0.Name ?? "?";
                }
                catch { }
                sb.Append($"\n      [{i}] {bn}");
                // ★★★[JACK 0827 "밴드 높이는 테스트 안 해봐?"]
                //   <b>GL·FGL이 겹치는 직접 원인이 칸 높이다.</b> 글자는 축척 보정을 받아 커졌는데
                //   칸은 그대로라 두 줄이 한 칸에 눌린다. <c>BandHeight</c>도 <b>종이 미터</b>이므로
                //   같은 보정을 받아야 한다 — 글자만 고치고 자리를 안 고친 것이 화근이었다.
                try
                {
                    if (tr.GetObject(bsId, OpenMode.ForWrite) is CivilDb.Styles.BandStyle bst)
                    {
                        double h0 = bst.BandHeight;
                        double hWant = PaperToStyle(BandHeightMm, scale, annoScale);
                        bst.BandHeight = hWant;
                        double h1 = bst.BandHeight;
                        sb.Append($" · 칸높이 {h0 * 1000:F1}→{h1 * 1000:F1}mm"
                                + (System.Math.Abs(h1 - hWant) < 1e-9 ? "" : "⚠안 붙음"));
                        if (System.Math.Abs(h1 - hWant) < 1e-9) nHt++;
                    }
                    else sb.Append(" · 칸높이=밴드스타일아님");
                }
                catch (System.Exception ex) { sb.Append(" · 칸높이 실패(" + ex.GetType().Name + ")"); }
                if (bsId.IsNull) { sb.Append("  (밴드 스타일 없음)"); continue; }

                // 어느 라벨 스타일이 값을 찍는지 모르므로 <b>여섯 종류를 다</b> 본다.
                foreach (string prop in new[] { "MajorIncrementLabelStyleId", "MinorIncrementLabelStyleId",
                                                "CenterlineLabelStyleId", "GradeBreaksLabelStyleId",
                                                "SampleLineVerticesLabelStyleId", "IncrementalDistanceLabelStyleId" })
                {
                    ObjectId lsId = ObjectId.Null;
                    try
                    {
                        var o = tr.GetObject(bsId, OpenMode.ForRead);
                        var pi = o.GetType().GetProperty(prop);
                        if (pi != null) lsId = (ObjectId)pi.GetValue(o);
                    }
                    catch { }
                    if (lsId.IsNull) continue;
                    nStyle++;

                    try
                    {
                        if (tr.GetObject(lsId, OpenMode.ForWrite) is not CivilDb.Styles.LabelStyle ls) continue;
                        var comps = ls.GetComponents(CivilDb.Styles.LabelStyleComponentType.Text);
                        if (comps == null || comps.Count == 0) { sb.Append($" · {Short(prop)}=글자칸없음"); continue; }
                        foreach (ObjectId cid in comps)
                        {
                            try
                            {
                                if (tr.GetObject(cid, OpenMode.ForWrite) is not CivilDb.Styles.LabelStyleTextComponent tc) continue;
                                double now = tc.Text.Height.Value;
                                nRead++;
                                // ★쓰고 <b>되읽는다</b> — 썼다고 남는 것이 아니다(마젠타·눈금 띄우기에서 겪었다).
                                tc.Text.Height.Value = want;
                                double back = tc.Text.Height.Value;
                                bool ok = System.Math.Abs(back - want) < 1e-9;
                                if (ok) nWrote++; else nStuck++;
                                sb.Append($" · {Short(prop)} {now * 1000:F2}→{back * 1000:F2}mm{(ok ? "" : "⚠안 붙음")}");
                            }
                            catch (System.Exception ex) { firstErr ??= ex.Message; }
                        }
                    }
                    catch (System.Exception ex) { firstErr ??= ex.Message; }
                }
            }
            tr.Commit();
        }
        catch (System.Exception ex) { log?.AppendLine("  밴드 글자 조사 실패 — " + ex.Message); return; }

        log?.AppendLine($"  밴드 글자 — 종이 {BandTextMm:0.#}mm 목표 → 스타일 {want * 1000:F2}mm"
                      + $" · 밴드 {nBand}칸 · 라벨스타일 {nStyle}개 · 읽은 글자칸 {nRead}개"
                      + $" · 바뀐 것 {nWrote}개 · 칸높이 {nHt}칸" + (nStuck > 0 ? $" · ⚠안 붙은 것 {nStuck}개" : "")
                      + (firstErr != null ? $"\n      첫 오류: {firstErr}" : "")
                      + sb.ToString());
    }

    private static string Short(string prop)
        => prop.Replace("LabelStyleId", "").Replace("Increment", "눈금").Replace("Major", "주").Replace("Minor", "보조");

    /// <summary>★★★[JACK 0828 "수직축에는 왼쪽·중심·오른쪽이 있어. DHTICKCHK는 왼쪽만 읽는 것 같아 — 다시 짜"]
    /// <b>대화상자에서 바꾼 값이 어느 자리로 들어가는지, 눈으로 안 세고 기계가 찾아 준다.</b>
    /// <para>종전 판도 다섯 축을 다 읽고는 있었다. 그런데 <b>숫자 60개를 늘어놓기만</b> 했으니
    /// 값이 다 같아 보여 "한 축만 읽나" 싶은 게 당연했다 — <b>비교를 사람에게 시킨 것이 잘못</b>이다.
    /// → 이제 <b>지난 판과 달라진 것만</b> 맨 위에 따로 찍는다.</para>
    /// <para>더한 것 넷:
    /// ① 도면의 <b>모든</b> 횡단 뷰 스타일을 읽는다(다른 스타일을 고치셨을 수 있다) ·
    /// ② <b>mm와 원시값을 나란히</b> 찍는다(단위가 미터인지 mm인지 여기서 갈린다) ·
    /// ③ 세 번째 눈금 <c>HorizontalGeometryTickStyle</c>도 시도한다(못 쓰면 못 쓴다고 적는다) ·
    /// ④ 다섯 축 값이 <b>전부 같으면</b> 그렇다고 적는다 — "한 축만 읽나"가 로그로 갈린다.</para>
    /// <para><b>쓰는 법</b>: ① 이 명령 한 번 → ② 대화상자에서 값을 바꾸고 <b>확인</b> →
    /// ③ 이 명령 다시. <b>[횡단도]는 중간에 돌리지 마세요</b> —
    /// 그리기가 이 값을 <b>되덮어서</b> 바꾼 것이 지워집니다.</para></summary>
    [CommandMethod("DHTICKCHK")]
    public static void TickCheck()
    {
        var doc = AcadApp.DocumentManager.MdiActiveDocument;
        if (doc == null) return;
        var db = doc.Database; var ed = doc.Editor;
        var sb = new System.Text.StringBuilder();
        var now = new System.Collections.Generic.Dictionary<string, string>();
        // ★[검토 0828 · M2] <b>"읽기만 한다"는 사실이 아니었다.</b>
        //   Civil 래퍼가 <c>ForRead</c>를 거부해(실측 0827) <b>부득이 <c>ForWrite</c></b>로 열고 커밋한다.
        //   값을 안 바꿔도 <b>도면은 손대진 것으로 표시</b>되는데 문구는 아무 흔적도 안 남는다고 말했다.
        //   <b>로그가 사실과 다르면 그것도 결함이다</b> — 오늘만 세 번 그것에 당했다.
        sb.AppendLine($"■ 횡단 눈금 속성 실측 {System.DateTime.Now:yyyy-MM-dd HH:mm:ss}"
                    + " (값은 안 바꾸되 스타일을 쓰기로 연다 — Civil이 읽기 모드를 거부한다)");
        try
        {
            var cdoc = CivilApp.CivilApplication.ActiveDocument;
            var ids = new System.Collections.Generic.List<ObjectId>();
            try { foreach (ObjectId id in cdoc.Styles.SectionViewStyles) ids.Add(id); }
            catch (System.Exception ex) { sb.AppendLine("  스타일 목록 실패 — " + ex.Message); }
            if (ids.Count == 0) sb.AppendLine("  횡단 뷰 스타일이 하나도 없다");

            using var tr = db.TransactionManager.StartTransaction();
            foreach (ObjectId sid in ids)
            {
                // ★[실측 0827] <b>읽기 모드에서는 축이 안 열린다.</b> <c>ForRead</c>로 열면
                //   Civil 래퍼가 "Operation is not valid"를 던진다. 값은 안 바꾸고 커밋만 한다.
                if (tr.GetObject(sid, OpenMode.ForWrite) is not CivilDb.Styles.SectionViewStyle st) continue;
                string sname = "?";
                try { sname = st.Name; } catch { }
                sb.AppendLine($"  스타일 '{sname}'");
                var axes = new (string Nm, System.Func<CivilDb.Styles.AxisStyle> Get)[]
                {
                    ("중심", () => st.CenterAxis), ("왼쪽", () => st.LeftAxis),
                    ("오른쪽", () => st.RightAxis), ("위", () => st.TopAxis), ("아래", () => st.BottomAxis),
                };
                var majorX = new System.Collections.Generic.List<string>();
                foreach (var (nm, get) in axes)
                {
                    try
                    {
                        using var ax = get();
                        if (ax == null) { sb.AppendLine($"  [{nm}] 없음"); continue; }
                        sb.AppendLine($"  [{nm}]");
                        // ★[JACK 0828] 세 번째 눈금도 <b>넣어서 시도한다</b> — 못 쓰면 그 사실 자체가 정보다.
                        var ticks = new (string Tn, System.Func<CivilDb.Styles.AxisTickStyle> Get)[]
                        {
                            ("주", () => ax.MajorTickStyle),
                            ("보조", () => ax.MinorTickStyle),
                            ("수평기하", () => ax.HorizontalGeometryTickStyle),
                        };
                        foreach (var (tn, tget) in ticks)
                        {
                            CivilDb.Styles.AxisTickStyle tk = null;
                            try { tk = tget(); }
                            catch (System.Exception e) { sb.AppendLine($"      {tn,-6} 못 씀 — {e.GetType().Name}"); continue; }
                            if (tk == null) { sb.AppendLine($"      {tn,-6} 없음"); continue; }

                            // ★★★[JACK 0828 · 자문 글] <b>원시값을 함께 찍는다.</b>
                            //   자문 글은 <c>OffsetX = 10.0</c>을 "10mm"라 쓰는데 우리는 <c>0.010</c>을 쓴다.
                            //   둘 중 하나는 틀렸고, <b>원시값과 대화상자 숫자를 나란히 놓으면 바로 갈린다</b> —
                            //   대화상자 눈금 크기가 4.50mm인데 원시값이 0.0045면 <b>미터가 맞다</b>.
                            string key(string prop) => $"{sname}|{nm}|{tn}|{prop}";
                            string One(string prop, System.Func<double> f)
                            {
                                double v;
                                try { v = f(); } catch { return $" {prop}=X"; }
                                now[key(prop)] = v.ToString("R");
                                return $" {prop}={v * 1000:F2}mm(원시 {v:0.######})";
                            }
                            var line = new System.Text.StringBuilder();
                            line.Append(One("X", () => tk.OffsetX));
                            line.Append(One("Y", () => tk.OffsetY));
                            line.Append(One("크기", () => tk.Size));
                            line.Append(One("글자", () => tk.TextHeight));
                            try { double iv = tk.Interval; now[key("간격")] = iv.ToString("R"); line.Append($" 간격={iv:0.###}"); } catch { }
                            try { double rt = tk.Rotation; now[key("회전")] = rt.ToString("R"); line.Append($" 회전={rt:0.###}"); } catch { }
                            try
                            {
                                var j = tk.Justification;
                                bool named = System.Enum.IsDefined(typeof(CivilDb.Styles.AxisTickJustificationType), j);
                                now[key("정렬")] = ((int)j).ToString();
                                line.Append($" 정렬={(named ? j.ToString() : $"{(int)j}⚠이름없는값")}");
                            }
                            catch { line.Append(" 정렬=X"); }
                            try { line.Append($" 글자체={tk.TextStyle}"); } catch { }
                            try { line.Append($" 딱지='{tk.LabelText}'"); } catch { }
                            sb.AppendLine($"      {tn,-6}{line}");
                            if (tn == "주" && now.TryGetValue(key("X"), out string mx)) majorX.Add(mx);
                        }
                    }
                    catch (System.Exception ex) { sb.AppendLine($"  [{nm}] 실패 — {ex.Message}"); }
                }
                // ★[JACK 0828 "왼쪽만 읽는 것 같아"] <b>정말 한 축만 읽는지 기계가 답한다.</b>
                if (majorX.Count >= 2)
                {
                    bool allSame = majorX.TrueForAll(x => x == majorX[0]);
                    // ★★★[검토 0828 · HIGH-B] <b>관찰만 적는다. 결론은 이 줄이 낼 수 없다.</b>
                    //   종전엔 "값이 같은 것이지 한 축만 읽는 것이 아니다"라고 <b>단정</b>했는데,
                    //   다섯 값이 같은 것은 두 가지와 모두 들어맞는다 —
                    //   ⓐ 축은 다섯인데 값이 같다  ⓑ <b>다섯 번 다 같은 것을 읽는다</b>.
                    //   그런데 ⓑ가 바로 JACK이 의심한 그것이다. <b>내가 증거 없이 그 가능성을 부인했다.</b>
                    //   갈라내는 것은 <c>DHTICKSET</c>(축마다 다른 값을 쓰고 되읽기)이지 이 줄이 아니다.
                    sb.AppendLine(allSame
                        ? $"    ※축 {majorX.Count}개의 주 눈금 X가 <b>전부 같다</b>(원시 {majorX[0]})"
                          + " — 값이 같아서인지 <b>같은 것을 다섯 번 읽어서인지는 이 줄로 못 가른다</b>."
                          + " 가르려면 <b>DHTICKSET</b>(축마다 다른 값을 쓴다)을 치세요"
                        : "    ※축마다 주 눈금 X가 <b>다르다</b> — 축은 각각 따로 읽히고 있다");
                }
            }
            tr.Commit();
        }
        catch (System.Exception ex) { sb.AppendLine("  실패 — " + ex.Message); }

        // ★★★[JACK 0828] <b>지난 판과 달라진 것을 기계가 찾는다.</b>
        //   두 판을 대 보는 것이 이 테스트의 전부인데 그걸 사람에게 시켰다 —
        //   숫자가 60개라 <b>안 움직인 것을 움직였다고, 움직인 것을 안 움직였다고</b> 보기 쉽다.
        try
        {
            string snapPath = System.IO.Path.Combine(
                System.IO.Path.GetDirectoryName(DiagLog.FilePath) ?? ".", "DHXSEC_눈금스냅샷.txt");
            var prev = new System.Collections.Generic.Dictionary<string, string>();
            if (System.IO.File.Exists(snapPath))
                foreach (string ln in System.IO.File.ReadAllLines(snapPath))
                {
                    int t = ln.LastIndexOf('\t');
                    if (t > 0) prev[ln.Substring(0, t)] = ln.Substring(t + 1);
                }
            var moved = new System.Text.StringBuilder();
            int nMoved = 0, nNew = 0;
            foreach (var kv in now)
            {
                if (!prev.TryGetValue(kv.Key, out string old)) { nNew++; continue; }
                if (old != kv.Value) { moved.AppendLine($"      {kv.Key} : {old} → <b>{kv.Value}</b>"); nMoved++; }
            }
            // ★[검토 0828 · M4] <b>사라진 자리도 센다.</b>
            //   종전엔 <c>now</c>만 돌아 <c>prev</c>에만 있는 열쇠를 못 봤다 —
            //   읽기가 절반 죽어도 <c>nMoved == 0</c>이라 <b>"하나도 안 달라졌다"</b>로 찍혔다.
            int nGone = 0;
            foreach (var kv in prev) if (!now.ContainsKey(kv.Key)) nGone++;
            string head = prev.Count == 0
                ? "  [비교] 지난 판이 없다 — 지금 것을 기준으로 저장했다. 대화상자에서 값을 바꾸고 다시 치세요."
                : nMoved == 0
                    ? $"  [비교] 지난 판과 <b>하나도 안 달라졌다</b>(잰 자리 {now.Count}개)"
                      + " ⚠값을 바꾸셨다면 <b>[횡단도]를 중간에 돌리지 않았는지</b> 보세요 — 그리기가 되덮습니다."
                    : $"  [비교] <b>움직인 값 {nMoved}개 — 이것이 대화상자가 쓰는 자리다</b>:\n" + moved.ToString().TrimEnd();
            if (nNew > 0) head += $"\n  (지난 판에 없던 자리 {nNew}개)";
            if (nGone > 0) head += $"\n  ⚠<b>지난 판에는 있었는데 이번엔 못 잰 자리 {nGone}개</b>"
                                 + " — 읽기가 새로 실패했다는 뜻이다";
            int nl = sb.ToString().IndexOf('\n');
            if (nl >= 0) sb.Insert(nl + 1, head + "\n"); else sb.AppendLine(head);
            // ★★[검토 0828 · M3] <b>반쪽짜리 결과로 지난 판을 지우지 않는다.</b>
            //   읽기가 터져 <c>now</c>가 비거나 반쪽이어도 종전엔 그대로 덮어썼다 —
            //   그러면 <b>이 명령의 값어치인 전후 대조가 통째로 날아간다</b>.
            //   → <b>지난 판보다 많이 쟀을 때만</b> 갈아끼운다.
            if (now.Count > 0 && now.Count >= prev.Count)
            {
                var outp = new System.Text.StringBuilder();
                foreach (var kv in now) outp.AppendLine($"{kv.Key}\t{kv.Value}");
                System.IO.File.WriteAllText(snapPath, outp.ToString());
            }
            else if (prev.Count > 0)
                sb.AppendLine($"  ⚠이번에 잰 자리({now.Count}개)가 지난 판({prev.Count}개)보다 적어"
                            + " <b>지난 판을 그대로 둔다</b> — 전후 대조를 잃지 않기 위해서다");
        }
        catch (System.Exception ex) { sb.AppendLine("  비교 실패 — " + ex.Message); }

        try { DiagLog.Append($"\n{sb}"); } catch { }
        ed.WriteMessage($"\n[눈금확인] {now.Count}자리를 읽었습니다 — 자세한 내용: {DiagLog.FilePath}");
        ed.WriteMessage("\n  순서: ①DHTICKCHK → ②대화상자에서 값 바꾸고 확인 → ③DHTICKCHK");
        ed.WriteMessage("\n  ※중간에 [횡단도]를 돌리면 그리기가 값을 되덮어 테스트가 헛돕니다.");
    }

    /// <summary>★★★[JACK 0828 "왼쪽 버그를 역이용한다"] <b>공유된 칸이 화면에 닿는지 한 판에 본다.</b>
    /// <para><see cref="TickDiag"/>가 확정한 것: 중심축과 왼쪽축이 <b>띄우기 칸을 공유</b>한다.
    /// 그렇다면 그 칸에 쓰면 중심축 눈금값도 따라 움직여야 <b>역이용</b>이 성립한다.</para>
    /// <para><b>그런데 아직 모르는 것이 하나 남았다.</b> 그 칸은 원래 <b>왼쪽축의 집</b>이다 —
    /// 화면이 중심축을 그릴 때 <b>그 칸을 보는지</b>는 확인된 바 없다.
    /// 안 본다면 아무리 써도 안 움직인다(우리가 18mm로 겪은 그것이다).</para>
    /// <para>→ <b>눈에 안 띌 수 없는 값</b>을 한 번 쓰고 되그린다.
    /// 지금 종이 12mm짜리를 <b>60mm</b>로 만드니 <b>다섯 배</b>다. 움직였다면 못 볼 수가 없다.
    /// <b>사람의 눈이 판정한다</b> — 되읽기로는 이 물음에 답할 수 없다(공유 칸이라 늘 "남았다"가 나온다).</para>
    /// <para><b>되돌리기</b>: <c>[횡단도]</c>를 한 번 돌리면 그리기가 제 값으로 되덮는다.</para></summary>
    [CommandMethod("DHTICKSET")]
    public static void TickSet()
    {
        var doc = AcadApp.DocumentManager.MdiActiveDocument;
        if (doc == null) return;
        var db = doc.Database; var ed = doc.Editor;
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"■ 공유 칸 <b>역이용 시험</b> {System.DateTime.Now:yyyy-MM-dd HH:mm:ss}"
                    + " — 공유된 띄우기 칸에 크게 쓰고, 중심축 눈금값이 움직이는지 <b>눈으로</b> 본다");
        // 종이에서 다섯 배가 되도록 <b>스타일값</b>을 잡는다.
        //   지금 스타일값 18mm(=종이 12mm @1:150)의 다섯 배 = 90mm.
        const double TryStyleMm = 90.0;
        double before = double.NaN, after = double.NaN, center = double.NaN;
        try
        {
            ObjectId sid = DiagStyleId(db, out string how);
            if (sid.IsNull)
            {
                ed.WriteMessage("\n[역이용시험] 쓸 스타일을 못 찾았습니다 — [횡단도]를 먼저 한 번 찍어 주세요.");
                return;
            }
            sb.AppendLine($"  대상 — {how}");

            using (var tr = db.TransactionManager.StartTransaction())
            {
                if (tr.GetObject(sid, OpenMode.ForWrite) is CivilDb.Styles.SectionViewStyle st)
                {
                    // ★<b>왼쪽축에 쓴다.</b> 중심축에 써도 같은 칸이지만,
                    //   <b>어느 이름으로 썼는지</b>가 로그에 분명해야 나중에 헷갈리지 않는다.
                    using var lx = st.LeftAxis;
                    try { before = lx.MajorTickStyle.OffsetX; } catch { }
                    try { lx.MajorTickStyle.OffsetX = TryStyleMm / 1000.0; } catch (System.Exception e) { sb.AppendLine("  쓰기 실패 — " + e.GetType().Name); }
                    try { lx.MinorTickStyle.OffsetX = TryStyleMm / 1000.0; } catch { }
                }
                tr.Commit();
            }
            using (var tr2 = db.TransactionManager.StartTransaction())
            {
                if (tr2.GetObject(sid, OpenMode.ForWrite) is CivilDb.Styles.SectionViewStyle st2)
                {
                    using var lx = st2.LeftAxis; using var cx = st2.CenterAxis;
                    try { after = lx.MajorTickStyle.OffsetX; } catch { }
                    try { center = cx.MajorTickStyle.OffsetX; } catch { }
                }
                tr2.Commit();
            }
            sb.AppendLine($"  왼쪽축 띄우기 {before * 1000:F2} → {after * 1000:F2}mm · 중심축으로 읽으면 {center * 1000:F2}mm"
                        + "  (공유 칸이라 둘이 같게 나오는 것은 <b>이미 아는 사실</b>이다 — 판정 근거가 아니다)");
            sb.AppendLine("  ★<b>판정은 화면이 한다</b> — 중심축의 표고 숫자가 오른쪽으로 확 밀렸는가?");
        }
        catch (System.Exception ex) { sb.AppendLine("  실패 — " + ex.Message); }

        try { ed.Regen(); } catch { }
        try { DiagLog.Append($"\n{sb}"); } catch { }
        ed.WriteMessage($"\n[역이용시험] 공유 칸에 {TryStyleMm:F0}mm를 썼습니다(종이로 지금의 다섯 배).");
        ed.WriteMessage("\n  ★도면을 보세요 — <중심축의 표고 숫자>가 오른쪽으로 확 밀렸습니까?"
                        .Replace("<", "").Replace(">", ""));
        ed.WriteMessage("\n   밀렸으면 → 역이용이 됩니다. 안 밀렸으면 → 그 칸은 화면에 안 닿습니다.");
        ed.WriteMessage("\n   되돌리려면 [횡단도]를 한 번 돌리시면 됩니다.");
    }

    /// <summary>★★★[JACK 0828] <b>중심축과 왼쪽축이 같은 칸을 쓰는지 — 공개 API만으로 확정한다.</b>
    /// <para>디컴파일이 말하는 것: <c>GetOffsetAttributeId()</c>에서 <c>Center</c>가 <c>Left</c>와
    /// <b>같은 번호</b>(151085334)를 쓴다. 같은 번호면 <b>같은 칸</b>이다.</para>
    /// <para>그렇다면 <b>왼쪽축에만 쓰고 중심축을 읽었을 때 따라 움직여야 한다.</b>
    /// 이건 다른 설명이 없다 — 두 이름이 한 칸을 가리킬 때만 일어나는 일이다.
    /// <b>생포인터도, 비공식 호출도 필요 없다.</b></para>
    /// <para><b>대조군을 같이 돌린다.</b> 눈금 <c>크기</c>는 번호가 제대로 갈라져 있으니
    /// 왼쪽에 써도 중심이 안 움직여야 한다. 띄우기는 움직이고 크기는 안 움직이면
    /// <b>버그가 띄우기 하나에 있다</b>는 것까지 함께 확정된다.
    /// (대조군이 없으면 "원래 다 같이 움직이는 것"일 가능성을 못 지운다.)</para>
    /// <para><b>쓴 값은 되돌린다.</b> 진단이 도면을 바꿔 놓으면 안 된다.</para></summary>
    [CommandMethod("DHTICKDIAG")]
    public static void TickDiag()
    {
        var doc = AcadApp.DocumentManager.MdiActiveDocument;
        if (doc == null) return;
        var db = doc.Database; var ed = doc.Editor;
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"■ 중심축·왼쪽축 <b>같은 칸 시험</b> {System.DateTime.Now:yyyy-MM-dd HH:mm:ss}"
                    + " — 왼쪽에만 쓰고 중심을 읽는다(공개 API만 쓴다)");
        string verdict = "판정 못 함";
        try
        {
            // ★★★[검토 0828 · HIGH-A] 진단이 <b>남의 스타일</b>을 재면 결론이 통째로 뒤집힌다.
            ObjectId sid = DiagStyleId(db, out string how);
            if (sid.IsNull)
            {
                ed.WriteMessage("\n[같은칸시험] 쓸 스타일을 못 찾았습니다 — [횡단도]를 먼저 한 번 찍어 주세요.");
                return;
            }
            sb.AppendLine($"  대상 — {how}");

            // ── 0단계: 원래 값을 적어 둔다(끝나면 되돌린다)
            double oL = double.NaN, oC = double.NaN, oR = double.NaN, sL = double.NaN, sC = double.NaN;
            double Read(System.Func<double> f) { try { return f(); } catch { return double.NaN; } }
            void Step(string title, System.Action<CivilDb.Styles.SectionViewStyle> act)
            {
                using var t = db.TransactionManager.StartTransaction();
                if (t.GetObject(sid, OpenMode.ForWrite) is CivilDb.Styles.SectionViewStyle s) act(s);
                t.Commit();
                if (title != null) sb.AppendLine(title);
            }

            Step(null, s =>
            {
                using var l = s.LeftAxis; using var c = s.CenterAxis; using var r = s.RightAxis;
                oL = Read(() => l.MajorTickStyle.OffsetX);
                oC = Read(() => c.MajorTickStyle.OffsetX);
                oR = Read(() => r.MajorTickStyle.OffsetX);
                sL = Read(() => l.MajorTickStyle.Size);
                sC = Read(() => c.MajorTickStyle.Size);
            });
            sb.AppendLine($"  ① 시작값 — 띄우기 왼쪽 {oL * 1000:F2} · 중심 {oC * 1000:F2} · 오른쪽 {oR * 1000:F2}mm"
                        + $" · 크기 왼쪽 {sL * 1000:F2} · 중심 {sC * 1000:F2}mm");

            // ── 1단계: <b>왼쪽축에만</b> 띄우기 77mm를 쓴다. 중심은 손도 안 댄다.
            const double ProbeOff = 0.077, ProbeSize = 0.0077;
            Step(null, s => { using var l = s.LeftAxis; try { l.MajorTickStyle.OffsetX = ProbeOff; } catch { } });
            double aL = double.NaN, aC = double.NaN, aR = double.NaN;
            Step(null, s =>
            {
                using var l = s.LeftAxis; using var c = s.CenterAxis; using var r = s.RightAxis;
                aL = Read(() => l.MajorTickStyle.OffsetX);
                aC = Read(() => c.MajorTickStyle.OffsetX);
                aR = Read(() => r.MajorTickStyle.OffsetX);
            });
            bool shared = System.Math.Abs(aC - ProbeOff) < 1e-9;
            sb.AppendLine($"  ② 왼쪽 띄우기에만 {ProbeOff * 1000:F0}mm를 썼다 → 왼쪽 {aL * 1000:F2} · "
                        + $"<b>중심 {aC * 1000:F2}</b> · 오른쪽 {aR * 1000:F2}mm"
                        + (shared ? "  ★<b>중심이 따라 움직였다 — 같은 칸이다</b>"
                                  : "  중심은 안 움직였다 — 각자 칸을 쓴다"));

            // ── 2단계 대조군: <b>크기</b>도 같은 시험을 한다. 이쪽은 번호가 갈라져 있어야 한다.
            Step(null, s => { using var l = s.LeftAxis; try { l.MajorTickStyle.Size = ProbeSize; } catch { } });
            double bL = double.NaN, bC = double.NaN;
            Step(null, s =>
            {
                using var l = s.LeftAxis; using var c = s.CenterAxis;
                bL = Read(() => l.MajorTickStyle.Size);
                bC = Read(() => c.MajorTickStyle.Size);
            });
            bool sizeShared = System.Math.Abs(bC - ProbeSize) < 1e-9;
            sb.AppendLine($"  ③ 대조군 — 왼쪽 <b>크기</b>에만 {ProbeSize * 1000:F1}mm를 썼다 → 왼쪽 {bL * 1000:F2} · "
                        + $"중심 {bC * 1000:F2}mm"
                        + (sizeShared ? "  ⚠중심도 따라 움직였다 — 크기까지 같은 칸이다"
                                      : "  ★<b>중심은 그대로다 — 크기는 각자 칸을 쓴다(정상)</b>"));

            // ── 3단계: 되돌린다. 진단이 도면을 바꿔 놓으면 안 된다.
            Step(null, s =>
            {
                using var l = s.LeftAxis;
                try { if (!double.IsNaN(oL)) l.MajorTickStyle.OffsetX = oL; } catch { }
                try { if (!double.IsNaN(sL)) l.MajorTickStyle.Size = sL; } catch { }
            });
            double rL = double.NaN, rC = double.NaN;
            Step(null, s =>
            {
                using var l = s.LeftAxis; using var c = s.CenterAxis;
                rL = Read(() => l.MajorTickStyle.OffsetX);
                rC = Read(() => c.MajorTickStyle.OffsetX);
            });
            sb.AppendLine($"  ④ 되돌림 — 왼쪽 {rL * 1000:F2} · 중심 {rC * 1000:F2}mm (시작값 {oL * 1000:F2} · {oC * 1000:F2})");

            verdict = shared && !sizeShared
                ? "★확정 — <b>중심축과 왼쪽축이 띄우기 칸을 공유한다. 크기는 안 그렇다.</b> Autodesk 래퍼 버그가 맞다"
                : shared && sizeShared
                    ? "중심축이 왼쪽축을 따라간다 — 다만 <b>크기까지</b> 그러니 띄우기만의 문제가 아니다. 다시 봐야 한다"
                    : "<b>안 나뉘었다 — 두 축은 각자 칸을 쓴다.</b> 디컴파일 해석이 틀렸거나 다른 원인이다";
            sb.AppendLine("  " + verdict);
        }
        catch (System.Exception ex) { sb.AppendLine("  실패 — " + ex.Message); verdict = "시험 도중 실패: " + ex.Message; }

        try { DiagLog.Append($"\n{sb}"); } catch { }
        ed.WriteMessage("\n[같은칸시험] " + verdict.Replace("<b>", "").Replace("</b>", "").Replace("★", ""));
        ed.WriteMessage($"\n  자세한 내용: {DiagLog.FilePath}");
    }

    private static void SetTextSizes(Database db, ObjectId styleId, double scale, double annoScale,
                                     System.Text.StringBuilder log)
    {
        if (styleId.IsNull) return;
        double hTick = PaperToStyle(TickTextMm, scale, annoScale);
        var axName = new[] { "중심", "왼쪽", "오른쪽", "위", "아래" };
        var offNote = new System.Text.StringBuilder();
        int nAx = 0;
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            if (tr.GetObject(styleId, OpenMode.ForWrite) is CivilDb.Styles.SectionViewStyle st)
                foreach (var get in new System.Func<CivilDb.Styles.AxisStyle>[]
                         { () => st.CenterAxis, () => st.LeftAxis, () => st.RightAxis,
                           () => st.TopAxis, () => st.BottomAxis })
                {
                    try
                    {
                        // ★★★[검토 0828 · HIGH-D] <b>이름을 먼저 집고 순번을 바로 올린다.</b>
                        //   종전엔 <c>nAx++</c>가 <c>try</c> 맨 끝에 있었다 — 축 하나가 없거나 예외를 던지면
                        //   순번이 안 올라가 <b>다음 축이 앞 축의 이름으로 찍혔다</b>.
                        //   하필 <b>중심축이 실패하면 왼쪽축이 "중심"으로 기록된다</b> —
                        //   JACK의 미해결 과제가 중심축인데, 진단 경로 한복판에서 이름이 밀리는 것이다.
                        //   (바로 아래 되읽기는 무조건 증가라 이름이 맞으므로,
                        //    <b>같은 로그 줄 안에서 앞뒤 목록의 축 이름이 서로 어긋났다</b>.)
                        string axNm = nAx < axName.Length ? axName[nAx] : $"축{nAx}";
                        nAx++;
                        using var ax = get();
                        if (ax == null) { offNote.Append($"\n      {axNm} 없음"); continue; }
                        // ★★★[JACK 0827 "눈금값이 눈금 안으로 들어가버려"]
                        //   <b>글자만 키우면 자리가 안 따라온다.</b> 눈금 크기와 띄우기도
                        //   같은 축척 보정을 받아야 종이에서 한 덩이로 보인다 —
                        //   글자가 1:300에서 세 배 커지는데 띄우기가 그대로면 눈금을 파고든다.
                        ax.MajorTickStyle.TextHeight = hTick;
                        ax.MajorTickStyle.Size = PaperToStyle(TickMajorMm, scale, annoScale);
                        ax.MinorTickStyle.Size = PaperToStyle(TickMinorMm, scale, annoScale);
                        // ★띄우기는 <b>JACK이 템플릿에 넣은 12mm</b>가 기준이다.
                        //   그 값도 주석 축척 기준이라 축척이 다르면 종이에서 어긋난다 —
                        //   <b>사람이 정한 종이 크기를 축척으로 되돌려</b> 넣는다.
                        // ★★★[자문 0827] <b>눈금 스타일이 셋이다 — 우리는 하나만 봤다.</b>
                        //   <c>AxisStyle</c>에는 <c>MajorTickStyle</c>·<c>MinorTickStyle</c>·
                        //   <c>HorizontalGeometryTickStyle</c>이 있는데, 중심축이 <b>다른 것을 쓸 수</b> 있다.
                        //   (왼쪽축은 주 눈금이 맞았다 — 그래서 그쪽만 먹은 것으로 보인다.)
                        //   → <b>셋에 다 넣고 전후를 남긴다.</b> 어느 것이 화면을 바꾸는지 한 판에 갈린다.
                        //   ※진단 단계다. 갈리면 맞는 하나만 남기고 나머지는 뺀다.
                        double wOff = PaperToStyle(TickOffsetMm, scale, annoScale);
                        offNote.Append($"\n      {axNm}");
                        foreach (var (tickNm, tk) in new (string, CivilDb.Styles.AxisTickStyle)[]
                                 { ("주", ax.MajorTickStyle), ("보조", ax.MinorTickStyle) })
                        {
                            if (tk == null) { offNote.Append($" · {tickNm}=없음"); continue; }
                            double b0 = double.NaN, a0 = double.NaN;
                            try { b0 = tk.OffsetX; } catch { }
                            try { tk.OffsetX = wOff; } catch { }
                            try { a0 = tk.OffsetX; } catch { }
                            offNote.Append($" · {tickNm} {b0 * 1000:F1}→{a0 * 1000:F1}");
                        }
                    }
                    catch { }
                }
            tr.Commit();
        }
        catch (System.Exception ex) { log?.AppendLine("  글자 크기 맞추기 실패 — " + ex.Message); return; }

        // ★★★[JACK 0828 "수직축 눈금값 해결이 아직 안 된 것 같아"] <b>JACK이 맞았다 — 로그가 거짓 합격을 냈다.</b>
        //   <see cref="TuneAxisTicks"/>가 <c>[커밋 뒤 확인: 남았다]</c>를 찍고 있었는데,
        //   그것은 <b>자기가 쓴 12mm</b>를 확인한 것이다. 그 뒤 <b>여기서</b> 축척 보정값(24mm)으로
        //   덮어쓰는데 <b>그 마지막 쓰기에는 확인이 없었다</b>. 화면을 정하는 것은 마지막 값인데,
        //   확인은 그 앞 값에 걸려 있었다 — <b>검사한 것과 화면에 나가는 것이 다른 물건이었다</b>.
        //   → 마지막 쓰기 뒤에 <b>새 트랜잭션으로</b> 되읽는다. 비교 기준도 <b>실제로 쓴 값</b>이다.
        //
        //   ★<b>정렬 값도 함께 남긴다.</b> <c>AxisTickJustificationType</c>은 어셈블리 실측 결과
        //   <b>0=TopOrLeft · 1=BottomOrRight · 2=Center</b> 셋뿐인데,
        //   JACK 도면의 <b>중심축만 5</b>였다(왼쪽·오른쪽·위·아래는 전부 BottomOrRight).
        //   범위 밖 값이 들어 있다는 것은 <b>우리가 쥔 중심축 눈금 객체가 UI가 쓰는 그것이 아닐</b> 수 있다는 뜻이다.
        //   (<c>AxisStyle.MajorTickStyle</c>은 <b>읽기 전용</b>이라 통째로 갈아 끼우는 길도 없다.)
        {
            double wOffChk = PaperToStyle(TickOffsetMm, scale, annoScale);
            var chk = new System.Text.StringBuilder();
            int nStuck = 0, nLost = 0;
            try
            {
                using var trC = db.TransactionManager.StartTransaction();
                if (trC.GetObject(styleId, OpenMode.ForRead) is CivilDb.Styles.SectionViewStyle stC)
                {
                    int k = 0;
                    foreach (var get in new System.Func<CivilDb.Styles.AxisStyle>[]
                             { () => stC.CenterAxis, () => stC.LeftAxis, () => stC.RightAxis,
                               () => stC.TopAxis, () => stC.BottomAxis })
                    {
                        string nm = axName[k++];
                        try
                        {
                            using var ax = get();
                            if (ax == null) { chk.Append($"\n      {nm} 없음"); continue; }
                            double got = double.NaN; string just = "?";
                            try { got = ax.MajorTickStyle.OffsetX; } catch { }
                            try
                            {
                                var j = ax.MajorTickStyle.Justification;
                                just = System.Enum.IsDefined(typeof(CivilDb.Styles.AxisTickJustificationType), j)
                                    ? j.ToString()
                                    : $"<b>{(int)j} — 이름 없는 값</b>";
                            }
                            catch { just = "못 읽음"; }
                            bool ok = System.Math.Abs(got - wOffChk) < 1e-9;
                            if (ok) nStuck++; else nLost++;
                            chk.Append($"\n      {nm} 띄우기 {got * 1000:F1}mm"
                                     + (ok ? " 남음" : $" ⚠<b>쓴 값 {wOffChk * 1000:F1}mm이 아니다</b>")
                                     + $" · 정렬 {just}");
                        }
                        catch (System.Exception e) { chk.Append($"\n      {nm} 되읽기 실패 — {e.GetType().Name}"); }
                    }
                }
                trC.Commit();
            }
            catch (System.Exception e) { chk.Append("\n      커밋 뒤 되읽기 실패 — " + e.Message); }
            offNote.Append($"\n    [커밋 뒤 확인] 남은 축 {nStuck}개 · 어긋난 축 {nLost}개"
                         + "  ※값이 남아도 화면이 안 바뀌면 <b>스타일 대화상자에서 바꿔 보고 DHTICKCHK</b>로 어느 속성이 움직이는지 본다"
                         + chk.ToString());
        }

        log?.AppendLine($"  글자·눈금 크기 — 종이 목표 글자{TickTextMm:0.#}·눈금{TickMajorMm:0.#}/{TickMinorMm:0.#}·띄우기{TickOffsetMm:0.#}mm"
                      + $" → 스타일 글자{hTick * 1000:F2}·띄우기{PaperToStyle(TickOffsetMm, scale, annoScale) * 1000:F1}mm"
                      + $" (축척 1:{scale:F0} · 주석 1:{annoScale:F0} · {scale / annoScale:F2}배 보정)"
                      + $" · 축 {nAx}개" + offNote.ToString());
    }

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
                            // ★[JACK 0827] 글자 크기는 <b>축척이 정해진 뒤</b> 따로 건다
                            //   (<see cref="SetTextSizes"/>) — 여기선 아직 축척을 모른다.
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
                // ★★★[JACK 0828] <b>여기서 "남았다"고 말하면 안 된다.</b> 이 값은 축척을 모르는 채로 쓴
                //   <b>임시값</b>이고, 뒤에 <see cref="SetTextSizes"/>가 축척 보정값으로 <b>덮어쓴다</b>.
                //   종전 문구는 그걸 <b>최종 합격</b>처럼 읽히게 만들었다 — JACK이 화면을 보고
                //   "아직 안 됐다"고 하기 전까지 나는 로그만 믿고 됐다고 했다.
                //   <b>최종 판정은 '글자·눈금 크기' 줄의 [커밋 뒤 확인]에 있다.</b>
                axNote.Append(double.IsNaN(got) ? "  [임시값 · 커밋 뒤 중심축을 못 읽었다]"
                    : System.Math.Abs(got - want) < 1e-9
                        ? $"  [임시값 {got * 1000:F1}mm 들어감 — <b>최종 판정은 '글자·눈금 크기' 줄</b>]"
                        : $"  ⚠[임시값조차 안 남았다: 중심 {got * 1000:F1}mm ≠ 쓴 값 {TickOffsetMm:F0}mm]");
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

    /// <summary>★★★[JACK 0827 "GL은 밴드의 지반고, FGL은 밴드의 계획고 값이 나와야 해"]
    /// <b>밴드에 어느 단면을 읽을지 물려 준다.</b>
    /// <para>회사 스타일을 입히면 밴드가 <b>붙기는</b> 하는데 <b>어느 단면을 볼지는 비어 있다</b> —
    /// 그래서 값이 안 나온다. <c>Section1Id</c>·<c>Section2Id</c>에 단면을 꽂아야 한다.</para>
    ///
    /// <para>★★<b>한 번에 읽고 · 다 고치고 · 한 번에 저장한다.</b>
    /// <c>GetBottomBandItems</c>는 <b>스냅샷</b>이라 거기 아무리 써도 <c>SetBottomBandItems</c>로
    /// 돌려주지 않으면 <b>도면은 그대로다</b>(그런데 로그는 성공으로 찍힌다 — 가장 나쁜 모양).
    /// 종단 쪽이 v25.8에 저장을 빼먹어 눈금까지 사라졌고, v25.9엔 칸마다 저장해 마지막 칸만 남았다.
    /// <see cref="SheetCommand"/>가 쓰는 그 형태를 그대로 따른다.</para>
    ///
    /// <para>★★<b>배선은 단면1=원지반 · 단면2=정지면으로 통일한다.</b>
    /// 종단 밴드에서 실측으로 확정된 규칙이다 — 지반고=종단1, 계획고=종단2,
    /// 절토고=종단1−종단2, 성토고=종단2−종단1로 <b>네 식이 한 방향으로 일치</b>했다.
    /// 그러니 밴드마다 다르게 꽂을 이유가 없다. 줄 순번으로 짐작하면 회사가 표를 한 줄만
    /// 손봐도 조용히 무너진다(§23.7에서 계획고와 지반고가 같은 값으로 나온 적이 있다).</para>
    ///
    /// <para><b>이름으로 지표면을 고르지 않는다.</b> 원지반 지표면 이름이 <c>Surface1</c>인 도면이 있어
    /// "원지반"이라는 글자가 없다. <c>FindSurfaces</c>가 갈라 놓은 것을 <b>ObjectId로</b> 맞춘다.</para>
    /// <para>첫 뷰의 <b>밴드 종류와 스타일 이름</b>을 로그에 남긴다 — 어느 줄이 지반고·계획고인지
    /// 한 판에 확정된다. 그리고 <b>커밋 뒤 새 트랜잭션에서 되읽어</b> 실제로 남았는지 확인한다
    /// (같은 스냅샷을 다시 읽는 것은 확인이 아니다).</para></summary>
    /// <summary>★★★[JACK 0827 "GL 위에 측점도 넣어야 해 · 칸만 비워 두고 우리가 글씨를 그린다"]
    /// <b>측점 밴드 칸에 우리 형식으로 이름을 쓴다.</b>
    /// <para>Civil 밴드는 측점을 <b>자기 형식</b>으로 찍는다 — 종단 제목에서 겪었듯
    /// <c>No.</c> 표기가 없고 간격도 다르다. 그래서 그 칸은 <b>비워 두고</b>(단면을 안 물린다)
    /// 토적표 제목과 <b>같은 이름</b>을 우리가 그린다.</para>
    /// <para><b>칸이 없으면 조용히 건너뛴다.</b> 템플릿에 측점 밴드를 넣기 전에도 안전하다.</para></summary>
    private static int DrawStationBand(Database db,
                                       List<(ObjectId Id, double St, string Name)> views,
                                       double scale, double annoScale, System.Text.StringBuilder log)
    {
        if (views == null || views.Count == 0) return 0;
        int n = 0, noSlot = 0, nGapRead = 0;
        double firstGapM = 0.0;
        double h = PaperToStyle(BandTextMm, scale, annoScale) * (annoScale > 1e-9 ? annoScale : 1.0);
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            var ms = (BlockTableRecord)tr.GetObject(
                SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
            var lay = SectionCommand.EnsureLayer(db, tr, XsecStationLayer, 7);
            var kst = ImportGisCommand.EnsureKoreanTextStyle(db, tr);
            // ★★[JACK 0828] 색을 <b>객체에 직접</b> 못 박는다.
            //   <c>EnsureLayer</c>는 <b>이미 있는 레이어의 색은 안 고친다</b>(있으면 그냥 돌려준다) —
            //   그래서 옛 도면에서 넘어온 레이어가 노란색이면 <c>ByLayer</c>로는 계속 노랗다.
            //   이 프로젝트가 터파기 종단에서 이미 겪은 함정이다: <b>ByLayer면 레이어 색이 이긴다</b>.
            var black = Autodesk.AutoCAD.Colors.Color.FromColorIndex(
                Autodesk.AutoCAD.Colors.ColorMethod.ByAci, 7);   // 7 = 흰색/검정(인쇄하면 검정)

            foreach (var (vid, _, vname) in views)
            {
                try
                {
                    if (tr.GetObject(vid, OpenMode.ForRead) is not CivilDb.SectionView sv) continue;

                    // 이름에 <b>측점</b>이 든 밴드가 몇 번째인가. 없으면 이 뷰는 건너뛴다.
                    //   순번을 알아야 그 칸의 세로 자리를 계산할 수 있다.
                    // ★[검토 교훈] <b>자를 하나로</b> — 배선 쪽과 같은 함수로 가린다.
                    int idx = StationBandIndex(tr, sv);
                    if (idx < 0) { noSlot++; continue; }

                    // 밴드는 그래프 바닥에서 <b>아래로</b> 칸마다 쌓인다.
                    //   <c>idx</c>번째 칸의 <b>가운데</b> 높이를 잡는다.
                    var ext = ((Entity)sv).GeometricExtents;
                    double cx = (ext.MinPoint.X + ext.MaxPoint.X) / 2.0;
                    double bandM = BandHeightMm / 1000.0 * scale;
                    // ★★★[JACK 0828 · 검토] <b>틈을 주면 칸이 통째로 내려간다 — 글씨도 따라가야 한다.</b>
                    //   그래프 바닥과 첫 칸 사이에 <see cref="BandGapMm"/>만큼 틈을 넣었는데,
                    //   이 자리 계산은 그 틈을 몰랐다. 그대로 뒀으면 글씨만 <b>칸 위로 반 칸</b> 떠서
                    //   칸선에 걸쳤을 것이다 — <b>겹침 하나 고치다 다른 겹침을 만드는</b> 꼴이다.
                    //
                    //   ★<b>계산하지 않고 밴드에게 물어본다.</b> 우리가 쓴 값을 여기서 다시 계산하면
                    //   §50의 함정("같은 것을 두 곳에서 따로 계산")에 그대로 빠진다.
                    //   실제로 <b>들어간 값</b>을 읽어야 Civil이 안 받았을 때도 자리가 맞는다.
                    //   밴드 값은 <b>종이 미터</b>라 주석 축척을 곱해야 모형 길이가 된다.
                    double gapM = 0.0;
                    try
                    {
                        using var bi = sv.Bands.GetBottomBandItems();
                        double aScale = annoScale > 1e-9 ? annoScale : 1.0;
                        for (int b = 0; b <= idx && b < bi.Count; b++)
                            try { gapM += bi[b].Gap * aScale; } catch { }
                    }
                    catch { }
                    // ★[검토] <b>첫 뷰 것을 그대로 적는다.</b> 종전엔 <c>gapM &gt; 0</c>일 때만 담았는데,
                    //   그러면 <b>틈이 0인 것</b>과 <b>아직 못 읽은 것</b>이 로그에서 같아 보인다 —
                    //   "틈이 0이다"라는 말이 진짜인지 아닌지를 구별할 수 없게 된다.
                    if (nGapRead == 0) firstGapM = gapM;
                    nGapRead++;
                    double cy = ext.MinPoint.Y - gapM - bandM * (idx + 0.5);

                    var t = new DBText
                    {
                        TextString = vname ?? "",
                        Height = h,
                        Justify = AttachmentPoint.MiddleCenter,
                    };
                    t.SetDatabaseDefaults(db);
                    t.LayerId = lay;
                    t.Color = black;
                    if (!kst.IsNull) t.TextStyleId = kst;
                    var pt = new Point3d(cx, cy, 0);
                    t.Position = pt; t.AlignmentPoint = pt;
                    ms.AppendEntity(t);
                    tr.AddNewlyCreatedDBObject(t, true);
                    n++;
                }
                catch { }
            }
            tr.Commit();
        }
        catch (System.Exception ex) { log?.AppendLine("  측점 밴드 글씨 실패 — " + ex.Message); return 0; }

        // ★★★[검토 0828 · HIGH-E] <b>내가 넣은 단위 검사는 발화할 수 없는 검사였다.</b>
        //   <c>firstGapM &gt; bandMLog</c>로 경고하려 했는데, 쓸 때 <c>PaperToStyle</c>이 <c>÷주석축척</c>하고
        //   읽을 때 <c>×주석축척</c>해서 <b>둘이 상쇄된다</b> — <c>gapM</c>은 언제나 <c>bandMLog</c>의
        //   <b>정확히 절반</b>(2mm÷4mm)으로 나온다. 단위를 잘못 봤어도 그대로 절반이다.
        //   <b>자기가 쓴 값을 자기가 되읽는 왕복은 단위를 검증하지 못한다.</b>
        //   오늘 아침에 당한 그것과 <b>같은 종류의 착각</b>이다 — 검사가 다른 물건을 재고 있었다.
        //
        //   → <b>원시값 둘을 나란히 놓는다.</b> 밴드 <b>스타일</b>의 칸 높이(종이값이 확실한 것)와
        //   밴드 <b>항목</b>의 틈을 <b>보정 없이</b> 찍는다. 같은 단위면 비가 <b>0.5</b>여야 한다
        //   (종이 2mm ÷ 4mm). 어긋나면 그 배수가 곧 단위 차이다 — <b>스크린샷 없이 갈린다.</b>
        double bandMLog = BandHeightMm / 1000.0 * scale;
        double gapRawLog = double.NaN, hRawLog = double.NaN;
        try
        {
            using var trR = db.TransactionManager.StartTransaction();
            foreach (var (vid, _, _) in views)
            {
                if (trR.GetObject(vid, OpenMode.ForRead) is not CivilDb.SectionView sv2) continue;
                using var bi2 = sv2.Bands.GetBottomBandItems();
                if (bi2 == null || bi2.Count == 0) break;
                try { gapRawLog = bi2[0].Gap; } catch { }
                try
                {
                    if (trR.GetObject(bi2[0].BandStyleId, OpenMode.ForRead)
                        is CivilDb.Styles.SectionDataBandStyle bs2) hRawLog = bs2.BandHeight;
                }
                catch { }
                break;
            }
            trR.Commit();
        }
        catch { }
        double ratio = (hRawLog > 1e-12) ? gapRawLog / hRawLog : double.NaN;
        log?.AppendLine($"  측점 밴드 글씨 {n}개"
                      + (noSlot > 0 ? $" · 측점 칸이 없어 건너뛴 뷰 {noSlot}개(템플릿에 밴드를 넣으면 나온다)" : "")
                      + $" · 글자 종이 {h * 1000 / (annoScale > 1e-9 ? annoScale : 1.0):F1}mm"
                      + $" · 레이어 '{XsecStationLayer}' · 색 7(흰색/검정) 객체에 직접 지정"
                      + (nGapRead > 0
                         ? $" · 자리 = 그래프바닥 − 틈 {firstGapM:F3}m − 칸 {bandMLog:F3}m×(순번+0.5)"
                         : " · ※틈을 한 번도 못 읽었다"));
        log?.AppendLine($"    단위 확인(원시값 그대로) — 틈 {gapRawLog:0.######} ÷ 칸높이 {hRawLog:0.######} = "
                      + (double.IsNaN(ratio) ? "<b>못 잼</b>" : $"{ratio:0.####}")
                      + $"  (같은 단위면 {BandGapMm / BandHeightMm:0.####}이어야 한다"
                      + (double.IsNaN(ratio) ? ")"
                         : System.Math.Abs(ratio - BandGapMm / BandHeightMm) < 0.02
                            ? " — <b>맞다</b>)"
                            : $" — ⚠<b>어긋난다. 이 비만큼 단위가 다르다</b>)"));
        return n;
    }

    /// <summary>★★★[JACK 0827] <b>측점 칸을 가리는 자 — 한 곳에서만 판단한다.</b>
    /// <para>이름에 <c>측점</c>이 들었으면 그 칸이다. 그런데 도면에 <b>측점용 밴드 스타일이 없어</b>
    /// JACK이 <c>계획고</c> 스타일로 칸을 만드셨다 — <b>어차피 비울 자리라 스타일은 상관없다</b>.</para>
    /// <para>그래서 이름으로 못 찾으면 <b>맨 위 칸</b>을 측점 자리로 본다.
    /// 단 <b>칸이 셋 이상일 때만</b> — 둘뿐이면 지반고·계획고라 맨 위를 비우면 안 된다.</para></summary>
    private static int StationBandIndex(Transaction tr, CivilDb.SectionView sv)
    {
        try
        {
            using var bi = sv.Bands.GetBottomBandItems();
            for (int i = 0; i < bi.Count; i++)
                try
                {
                    if (tr.GetObject(bi[i].BandStyleId, OpenMode.ForRead) is CivilDb.Styles.StyleBase sb
                        && (sb.Name ?? "").IndexOf("측점", System.StringComparison.Ordinal) >= 0) return i;
                }
                catch { }
            return bi.Count >= 3 ? 0 : -1;   // 이름이 없으면 맨 위 — 칸이 셋 이상일 때만
        }
        catch { return -1; }
    }

    private static int BindBandSections(Database db,
                                        List<(ObjectId Id, double St, string Name)> views,
                                        System.Collections.Generic.Dictionary<ObjectId, string> kindOf,
                                        double scale, double annoScale,
                                        System.Text.StringBuilder log)
    {
        if (views == null || views.Count == 0) return 0;
        int nView = 0, nSet = 0, nRow = 0, nPlan = 0, nStn = 0;
        int noSl = 0, noG = 0, noP = 0, nHid = 0, nGap = 0;
        // ★★★[JACK 0828 "측점 밴드 부분이 너무 수직축 마지막하고 붙어서 겹쳐 보여 — 조금 띄워야 해"]
        //   맨 아래 눈금값(<c>90</c>)과 첫 밴드 칸이 <b>맞닿아</b> 글자가 포개졌다.
        //   밴드 칸 높이를 10mm→4mm로 줄이면서 <b>둘 사이의 빈틈까지 같이 사라진 것</b>이다.
        //   → <c>SectionViewBandItem.Gap</c>이 바로 그 틈이다(스타일 대화상자의 <b>간격</b> 칸,
        //   JACK 도면에서 <c>0.00mm</c>였다). <b>첫 칸에만</b> 주면 아래 칸들이 통째로 따라 내려간다.
        double gapWant = PaperToStyle(BandGapMm, scale, annoScale);
        var gapNote = new System.Text.StringBuilder();
        string firstErr = null;
        var shape = new System.Text.StringBuilder();
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            foreach (var (vid, _, vname) in views)
            {
                try
                {
                    if (tr.GetObject(vid, OpenMode.ForWrite) is not CivilDb.SectionView sv) continue;
                    nView++;

                    // 이 뷰의 검토선에서 원지반·계획 단면을 찾는다 — 뷰마다 한 번만.
                    ObjectId g = ObjectId.Null, pl = ObjectId.Null;
                    try
                    {
                        if (tr.GetObject(sv.SampleLineId, OpenMode.ForRead) is CivilDb.SampleLine sl)
                            foreach (ObjectId secId in sl.GetSectionIds())
                            {
                                try
                                {
                                    if (tr.GetObject(secId, OpenMode.ForRead) is not CivilDb.Section sec) continue;
                                    string kind = kindOf.TryGetValue(sec.SourceId, out var kk) ? kk : "";
                                    if (kind == "원지반" && g.IsNull) g = secId;
                                    else if (kind == "정지면" && pl.IsNull) pl = secId;
                                }
                                catch { }
                            }
                        else noSl++;
                    }
                    catch (System.Exception ex) { noSl++; firstErr ??= ex.Message; }
                    if (g.IsNull) noG++;
                    if (pl.IsNull) noP++;
                    if (g.IsNull && pl.IsNull) continue;

                    // 아래 밴드와 위 밴드 <b>둘 다</b> 본다 — 회사 세트가 위에 얹는 구조일 수 있다.
                    // ★★★[검토 0828 · M1] <b>측점 칸 순번은 아래 밴드의 것이다.</b>
                    //   <see cref="StationBandIndex"/>는 <c>GetBottomBandItems</c>만 본다.
                    //   그 순번을 <b>위 밴드에도</b> 그대로 쓰면 위 밴드의 엉뚱한 칸이 조용히 비워진다 —
                    //   되읽기는 아래만 보므로 <b>로그에도 안 잡힌다</b>.
                    //   (바로 아래 주석이 <i>"회사 세트가 위에 얹는 구조일 수 있다"</i>고 적어 둔 자리라
                    //    위 밴드가 없다는 전제를 믿을 수 없다.)
                    //   → <b>아래 밴드에만 적용</b>한다. 위 밴드는 측점 칸을 안 가진다.
                    int stnIdxBottom = StationBandIndex(tr, sv);
                    foreach (bool bottom in new[] { true, false })
                    {
                        int stnIdx = bottom ? stnIdxBottom : -1;
                        try
                        {
                            using var items = bottom ? sv.Bands.GetBottomBandItems() : sv.Bands.GetTopBandItems();
                            if (items == null || items.Count == 0) continue;
                            for (int i = 0; i < items.Count; i++)
                            {
                                try
                                {
                                    var it = items[i];
                                    // ★[JACK 0828] <b>그래프와 첫 칸 사이만 띄운다.</b>
                                    //   칸마다 주면 밴드가 통째로 성겨져 다시 넓어진다 —
                                    //   JACK이 고치라 한 것은 <b>수직축 눈금값과 첫 칸</b>이 겹치는 것뿐이다.
                                    if (bottom && i == 0)
                                    {
                                        double gb = double.NaN;
                                        try { gb = it.Gap; } catch { }
                                        try { it.Gap = gapWant; nGap++; } catch { }
                                        if (nView == 1)
                                            gapNote.Append($"\n      틈 {gb * 1000:F1}→{gapWant * 1000:F1}mm"
                                                         + $"(종이 {BandGapMm:0.#}mm · 축척 {scale / (annoScale > 1e-9 ? annoScale : 1.0):F2}배 보정)");
                                    }
                                    // ★★★[JACK 0827 "GL FGL 둘 다 같은 값, 둘 다 지반고로만 나온다"]
                                    //   <b>§23.7이 종단에서 겪은 그것이다.</b> 회사 표현식이 두 밴드 모두
                                    //   <c>&lt;[단면1 표고]&gt;</c>라서, 모든 줄에 1=원지반을 꽂으면
                                    //   <b>계획고 자리에 지반고가 찍힌다</b>(값이 한 자리도 안 틀리게 같다).
                                    //
                                    //   <b>여기서만은 이름으로 고른다.</b> 지반고와 계획고는 <b>종류도 표현식 구조도
                                    //   같아서</b> 이름 말고 구분할 근거가 없다 — §23.7이 종단에서 내린 결론 그대로다.
                                    //   (절토 <c>1-2</c>·성토 <c>2-1</c>가 생기면 1=원지반이라야 부호가 맞으므로
                                    //    그런 줄은 기본 배선을 그대로 둔다.)
                                    string bn = "?";
                                    try
                                    {
                                        if (tr.GetObject(it.BandStyleId, OpenMode.ForRead) is CivilDb.Styles.StyleBase sb)
                                            bn = sb.Name ?? "?";
                                    }
                                    catch { }
                                    // ★★[JACK 0827] <b>측점 칸에는 단면을 안 물린다.</b>
                            //   물리면 표고가 찍히는데, 그 자리에는 <b>우리가 측점 이름을 쓴다</b>.
                            //   칸을 비워 두는 것이 곧 자리를 만드는 일이다.
                            // ★★★[JACK 0828 "기존 계획고가 안 지워져서 측점하고 겹쳐져서 보여"]
                            //   <b>건너뛰는 것은 비우는 것이 아니다.</b> 이 칸은 <c>계획고</c> 스타일로
                            //   만들어져 <b>이미 단면이 물려 있다</b> — 우리가 손을 안 대면 그 표고가 그대로
                            //   찍히고, 그 위에 우리가 측점 이름을 쓰니 <b>둘이 겹쳐 보였다</b>.
                            //   되읽기가 <c>3/3줄에 남음</c>이라고 말해 준 것이 바로 이것이다 —
                            //   비웠다면 <b>2/3</b>여야 했다. 로그가 답을 들고 있었는데 내가 안 읽었다.
                            //   → <b>물린 것을 끊는다.</b> 칸을 비우는 것이 곧 자리를 만드는 일이다.
                            if (i == stnIdx)
                            {
                                // ★★★[JACK 0828 · 2판 "여전히 측점하고 FGL하고 겹쳐져"]
                                //   <b>단면을 끊는 것으로는 글자가 안 사라진다.</b> 1판에서
                                //   <c>Section1Id = ObjectId.Null</c>을 넣어 봤지만 내 새 되읽기가
                                //   <c>⚠측점칸이 아직 물려 있다</c>로 <b>바로 잡아냈다</b> — Civil이 안 받는다.
                                //   (그 되읽기를 안 넣었으면 또 "됐다"고 했을 것이다.)
                                //
                                //   → <b>어셈블리를 뜯어 답을 찾았다: <c>ShowLabels</c>.</b>
                                //   <c>SectionViewBandItem</c>에 <b>쓰기 가능</b>으로 있다.
                                //   이 칸은 <c>계획고</c> 스타일을 <b>세 번째 칸과 함께 쓰므로</b>
                                //   스타일에서 글자를 끄면 <b>진짜 계획고 칸까지 비어 버린다</b> —
                                //   그래서 <b>스타일이 아니라 이 칸 하나</b>에만 끄는 이 길이라야 한다.
                                // ★[검토 0828 · M7] <b>쓴 횟수는 증거가 아니다.</b>
                                //   스냅샷 속성 쓰기는 거의 안 던지므로 <c>nHid == nStn</c>이 <b>항상 성립</b>한다 —
                                //   로그의 <c>(글자 뀴 것 N개)</c>는 사실상 항등식이었다.
                                //   → <b>바로 되읽어</b> 정말 꺼졌는지 센다(스냅샷 안이라도 쓰기 자체는 확인된다).
                                try { it.ShowLabels = false; if (!it.ShowLabels) nHid++; } catch { }
                                try { it.Section1Id = ObjectId.Null; } catch { }
                                try { it.Section2Id = ObjectId.Null; } catch { }
                                nStn++; continue;
                            }
                            bool isPlan = bn.IndexOf("계획", System.StringComparison.Ordinal) >= 0;
                                    if (isPlan) { if (!pl.IsNull) it.Section1Id = pl; if (!g.IsNull) it.Section2Id = g; nPlan++; }
                                    else        { if (!g.IsNull) it.Section1Id = g;  if (!pl.IsNull) it.Section2Id = pl; }
                                    nRow++;
                                    if (nView == 1)
                                        shape.Append($"\n      [{(bottom ? "아래" : "위")}{i}] {it.BandType} '{bn}'"
                                                   + (isPlan ? " → 단면1=<b>정지면</b>(뒤집음)" : " → 단면1=원지반"));
                                }
                                catch (System.Exception ex) { firstErr ??= ex.Message; }
                            }
                            // ★스냅샷을 <b>통째로</b> 돌려준다. 줄마다 저장하면 앞 줄이 지워진다(v25.9 실측).
                            if (bottom) sv.Bands.SetBottomBandItems(items);
                            else sv.Bands.SetTopBandItems(items);
                            nSet++;
                        }
                        catch (System.Exception ex) { firstErr ??= ex.Message; }
                    }
                }
                catch (System.Exception ex) { firstErr ??= ex.Message; }
            }
            tr.Commit();
        }
        catch (System.Exception ex) { log?.AppendLine("  밴드 단면 물리기 실패 — " + ex.Message); return 0; }

        // 커밋 뒤 <b>새 트랜잭션</b>에서 되읽는다 — 스냅샷을 다시 읽는 건 확인이 아니다.
        string back = "?";
        try
        {
            using var tr2 = db.TransactionManager.StartTransaction();
            foreach (var (vid, _, _) in views)
            {
                if (tr2.GetObject(vid, OpenMode.ForRead) is not CivilDb.SectionView sv2) continue;
                using var it2 = sv2.Bands.GetBottomBandItems();
                if (it2 == null || it2.Count == 0) { back = "밴드 없음"; break; }
                int okS = 0;
                for (int i = 0; i < it2.Count; i++)
                    if (!it2[i].Section1Id.IsNull || !it2[i].Section2Id.IsNull) okS++;
                // ★★[JACK 0828] <b>측점 칸이 정말 비었는지</b>를 따로 말한다.
                //   종전엔 "3/3줄에 남음"이 <b>다 잘 됐다</b>로 읽혔는데, 실은 <b>비워야 할 칸까지</b>
                //   남아 있다는 뜻이었다. 세는 것과 바라는 것이 다르면 로그가 거짓말을 한다.
                int si = StationBandIndex(tr2, sv2);
                // ★★[JACK 0828] <b>화면을 정하는 것을 재야 한다.</b> 1판에서는 <c>Section1Id</c>가
                //   비었는지만 봤는데, 글자를 그리게 하는 것은 <c>ShowLabels</c>였다 —
                //   <b>엉뚱한 것을 재면 고쳐도 고쳐진 줄 모르고, 안 고쳐도 모른다.</b>
                string stnBack;
                if (si < 0) stnBack = "측점칸 없음";
                else
                {
                    bool shown = true;
                    // ★[검토 0828 · LOW-3] <b>순번은 다른 스냅샷에서 얻은 것이다.</b>
                    //   앞 줄만 <c>try</c>로 감싸 놓아, 범위를 벗어나면 뒷줄이 바깥으로 튀어
                    //   <b>되읽기가 통째로 "실패"</b>가 됐다 — 정작 알고 싶은 것은 못 알아낸 채로.
                    //   → 범위를 <b>먼저 본다</b>. 두 스냅샷의 칸 수가 다를 일은 없지만, 없다고 믿지 않는다.
                    bool bound = false, ranged = si < it2.Count;
                    if (ranged)
                    {
                        try { shown = it2[si].ShowLabels; } catch { }
                        try { bound = !it2[si].Section1Id.IsNull || !it2[si].Section2Id.IsNull; } catch { }
                    }
                    stnBack = !ranged
                        ? $"⚠<b>측점칸 순번({si})이 되읽은 칸 수({it2.Count})를 벗어난다</b>"
                        : shown
                            ? "⚠<b>측점칸 글자가 아직 켜져 있다</b>(계획고와 겹친다)"
                            : "측점칸 글자 껐다" + (bound ? "(단면은 물린 채 — 글자를 껐으니 안 보인다)" : "");
                }
                double g2 = double.NaN;
                try { g2 = it2[0].Gap; } catch { }
                // ★[검토 0828 · M8] <b>첫 뷰만 본다고 밝힌다.</b>
                //   종전 문구는 <b>모든 뷰가 그렇다</b>로 읽혔다 — 실제로는 하나만 재고 <c>break</c>한다.
                back = $"첫 뷰 기준 {okS}/{it2.Count}줄에 남음 · {stnBack} · 첫 칸 틈 {g2 * 1000:F1}mm";
                break;
            }
            tr2.Commit();
        }
        catch { back = "되읽기 실패"; }

        log?.AppendLine($"  밴드 단면 물리기 — 뷰 {nView}개 · 물린 줄 {nRow}개 · 저장 {nSet}번 · 계획고 뒤집기 {nPlan}줄"
                      + (nStn > 0 ? $" · 측점칸 {nStn}개(글자 끈 것 {nHid}개)" : "")
                      + (nGap > 0 ? $" · 틈 준 칸 {nGap}개" : "")
                      + $" [커밋 뒤 확인: {back}]"
                      + gapNote.ToString()
                      + (noSl > 0 ? $" · ⚠검토선을 못 연 뷰 {noSl}개" : "")
                      + (noG > 0 ? $" · ⚠원지반 단면이 없던 뷰 {noG}개" : "")
                      + (noP > 0 ? $" · ⚠계획 단면이 없던 뷰 {noP}개" : "")
                      + (firstErr != null ? $"\n      첫 오류: {firstErr}" : "")
                      + shape.ToString());
        return nRow;
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

        // ── 점선 확보 ★★★[JACK 0828] <b>세 가지를 갈라 쓴다.</b>
        //   JACK: <i>"점선이 터파기 지표면 점선하고 헷갈리지 않게 점선 형태를 좀 다른 걸로 해."</i>
        //   같은 점선을 셋이 나눠 쓰면 도면에서 <b>무엇이 무엇인지 알 수 없다</b> —
        //   터파기는 <c>DASHED</c>(긴 파선) 그대로 두고,
        //   지층은 <c>HIDDEN</c>(짧은 점선), 지하수위는 <c>DASHDOT</c>(일점쇄선)으로 갈랐다.
        //   ※일점쇄선은 도면에서 <b>수위·중심선</b>에 쓰는 관례라 뜻도 맞는다.
        string Load(string nm)
        {
            try
            {
                using var trT = db.TransactionManager.StartTransaction();
                var lt = (LinetypeTable)trT.GetObject(db.LinetypeTableId, OpenMode.ForRead);
                if (lt.Has(nm)) { trT.Commit(); return nm; }
                trT.Commit();
            }
            catch { }
            try { db.LoadLineTypeFile(nm, "acadiso.lin"); } catch { }
            try { db.LoadLineTypeFile(nm, "acad.lin"); } catch { }
            try
            {
                using var trT2 = db.TransactionManager.StartTransaction();
                var lt2 = (LinetypeTable)trT2.GetObject(db.LinetypeTableId, OpenMode.ForRead);
                bool has = lt2.Has(nm);
                trT2.Commit();
                return has ? nm : null;
            }
            catch { return null; }
        }
        string dash = Load("DASHED") ?? Load("HIDDEN") ?? Load("CENTER");   // 터파기
        // ★★[JACK 0828] <b>절반 간격 변형을 먼저 쓴다</b> — 부지가 넓으면
        //   기본 무늬은 끊긴 간격이 눈에 밟힌다(JACK 스샷).
        string dashStrata = Load("HIDDEN2") ?? Load("HIDDEN") ?? dash;                       // 지층
        string dashWater = Load("DASHDOT2") ?? Load("DIVIDE2") ?? Load("DASHDOT") ?? dash;   // 지하수위

        // ★★★[JACK 0831 "횡단에 지층들의 점선이 너무 듬성듬성있어 좀 촘촘히 바꿔"]
        //
        //   <b>원인: 선종류 이름만 골랐지 무늬 크기를 안 정했다.</b>
        //   로그가 <c>HIDDEN2</c>·<c>DASHDOT2</c>가 <b>제대로 실렸다</b>고 말하고 있었으므로
        //   이름은 무죄다 — 남는 것은 <b>배율</b>이고, 저장소 어디에도 <c>LTSCALE</c>을
        //   정하는 코드가 없었다(검토 지적). 즉 <b>남이 걸어 둔 값</b>에 끌려다니고 있었다.
        //
        //   → 무늬의 <b>실제 길이를 재서</b>(<c>LinetypeTableRecord.PatternLength</c>)
        //     도면 단위로 <see cref="DashPatternM"/>이 되도록 배율을 <b>계산</b>한다.
        //     도면의 <c>LTSCALE</c>이 얼마든 결과가 같아진다 — 기계마다 다르게 보이지 않는다.
        double LtScaleFor(string lt)
        {
            if (lt == null) return 1.0;
            double pat = 0;
            try
            {
                using var trP = db.TransactionManager.StartTransaction();
                var ltt = (LinetypeTable)trP.GetObject(db.LinetypeTableId, OpenMode.ForRead);
                if (ltt.Has(lt) && trP.GetObject(ltt[lt], OpenMode.ForRead) is LinetypeTableRecord r)
                    pat = System.Math.Abs(r.PatternLength);
                trP.Commit();
            }
            catch { }
            if (pat < 1e-9) return 1.0;
            double gl = 1.0;
            try { gl = db.Ltscale; } catch { }
            if (gl < 1e-9) gl = 1.0;
            double v = DashPatternM / (pat * gl);
            return v > 1e-6 && v < 1e6 ? v : 1.0;
        }

        // ── 스타일 셋 — 이미 있으면 색만 다시 맞춘다(도면에 스타일이 쌓이지 않게).
        ObjectId Ensure(string nm, short aci, string lt, double ltScale = 1.0)
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
                            if (lt != null)
                            {
                                try { ds.Linetype = lt; } catch { }
                                // ★무늬 크기 — 이것이 "듬성듬성"을 정한다.
                                try { ds.LinetypeScale = ltScale; } catch { }
                            }
                        }
                        catch { }
                    }
                tr.Commit();
                return id;
            }
            catch { return ObjectId.Null; }
        }

        double ltsE = LtScaleFor(dash), ltsS = LtScaleFor(dashStrata), ltsW = LtScaleFor(dashWater);
        var stGround = Ensure("DH_횡단면_원지반", 3, null);      // 3 = 초록, 실선
        var stPlan   = Ensure("DH_횡단면_계획", 7, null);        // 7 = 흰색, 실선
        // ★★★[JACK 0831 "터파기 선은 실선으로 — 점선이다 보니 연암하고 경암하고 헷갈려"]
        //   도면에 점선이 일곱 줄이 되면서 계획선(터파기)과 현황선(지층)이 안 갈렸다.
        //   ★<c>null</c>이 아니라 <b>"Continuous"</b>를 준다 — <c>null</c>은 "건드리지 않는다"라
        //   옛 판이 심어 둔 <c>DASHED</c>가 그대로 남는다.
        var stExcav  = Ensure("DH_횡단면_터파기", SectionCommand.ExcavAci, "Continuous");   // 6 = 마젠타, 실선
        var stWater  = Ensure("DH_횡단면_지하수위", 5, dashWater, ltsW);   // 5 = 파랑(JACK 지시)

        // ★★★[JACK 0831 "각 지층별로 색상을 줘"] <b>층마다 스타일을 따로 만든다.</b>
        //   0828에는 지층을 전부 혼탁(8)으로 뒀다 — 여러 줄이 진한 색이면 그것만 보이는 도면이 될까 봐였다.
        //   그런데 실제로 그려 보니 <b>어느 선이 어느 층인지 알 수가 없었다</b>(JACK 스샷).
        //   층을 가르는 것이 이 기능의 전부이므로 <b>가려 보이는 것이 먼저</b>다.
        //   색은 <see cref="StrataAci"/>에서 차례로 가져온다 — 초록(원지반)·흰색(계획)·
        //   마젠타(터파기)·파랑(지하수위)과 <b>겹치지 않는</b> 것만 골라 두었다.
        var stStrataBy = new System.Collections.Generic.Dictionary<int, ObjectId>();
        ObjectId StrataStyleOf(int ord)
        {
            if (stStrataBy.TryGetValue(ord, out var got)) return got;
            short aci = StrataAciOf(ord);
            var id = Ensure($"DH_횡단면_지층{ord}", aci, dashStrata, ltsS);
            stStrataBy[ord] = id;
            return id;
        }

        // 지표면 이름에서 층 번호를 뽑는다 — 못 뽑으면 1번 색을 쓴다(색이 없는 것보다 낫다).
        int StrataOrd(Transaction tr0, ObjectId surfId)
        {
            try
            {
                if (tr0.GetObject(surfId, OpenMode.ForRead) is CivilDb.Surface su)
                {
                    string nm2 = su.Name ?? "";
                    if (nm2.StartsWith(StrataDraw.SurfPrefix, System.StringComparison.Ordinal))
                    {
                        string rest = nm2.Substring(StrataDraw.SurfPrefix.Length);
                        int us = rest.IndexOf('_');
                        if (int.TryParse(us > 0 ? rest.Substring(0, us) : rest, out int o)) return o;
                    }
                }
            }
            catch { }
            return 1;
        }

        int nG = 0, nP = 0, nE = 0, nS = 0, nW = 0, nSkip = 0;
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
                            else if (kind == "지층")
                            {
                                // 이름 <c>DH_지층_3_풍화암</c>에서 번호를 뽑아 그 층의 색을 고른다.
                                int ord = StrataOrd(tr, sec.SourceId);
                                var sid2 = StrataStyleOf(ord);
                                if (!sid2.IsNull) { sec.StyleId = sid2; nS++; }
                            }
                            else if (kind == "지하수위") { if (!stWater.IsNull) { sec.StyleId = stWater; nW++; } }
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
                      + "·실선)"
                      + (nS > 0 ? $" · <b>지층 {nS}개</b>({stStrataBy.Count}가지 색·{dashStrata ?? "점선 없음"}·무늬배율 {ltsS:0.###})" : "")
                      + (nW > 0 ? $" · <b>지하수위 {nW}개</b>(파랑·{dashWater ?? "점선 없음"}·무늬배율 {ltsW:0.###})" : "")
                      + $" · 무늬 목표 {DashPatternM:0.##}m(도면 LTSCALE {(SafeLts(db)):0.###})"
                      + (nSkip > 0 ? $" · 이름으로 못 가른 것 {nSkip}개" : ""));
        return nG + nP + nE + nS + nW;
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
    /// <param name="strata">지층 경계 캐시 — <b>위에서 아래 차례</b>. <c>null</c>이면 지층 계산을 건너뛴다.</param>
    /// <param name="csW">지하수위 캐시. <c>null</c>이면 물 구분 없이 전부 육상.</param>
    /// <param name="led">여기에 지층별 수량을 담는다. <c>null</c>이면 안 담는다.</param>
    ///
    /// <remarks>★★★[JACK 0831] <b>지층 수량도 여기서 잰다 — 표본기를 따로 만들지 않는다.</b>
    /// <para>절토·터파기 면적을 내는 <c>xs</c>·<c>gy</c>·<c>py</c>·<c>ey</c>가 이미 여기 있다.
    /// 지층을 딴 함수에서 다시 표본하면 <b>경계 이분법·격자 간격</b>이 미세하게 달라져
    /// <b>지층 합계가 전체 절토와 안 맞는</b> 일이 생긴다 — 그때는 어느 쪽이 맞는지도 알 수 없다.
    /// <para>★[검토 MED-5] 다만 <b>같은 배열을 넘기는 것만으로는 모자랐다</b> —
    /// <c>Accumulate</c>가 안에서 축을 <b>다시 지으면서</b> 1mm 안쪽 점을 뭉갰다.
    /// 우리가 지표면 가장자리를 이분법으로 0.006mm까지 좁혀 놓은 점 쌍이 바로 그 크기다.
    /// 그래서 <c>axis</c> 인자로 <b>이 배열을 그대로</b> 넘긴다 — 그것까지 해야 축이 같아진다.</para>
    /// <para>★그리고 <b>축이 같아도 합이 저절로 맞지는 않는다</b> — 지층면이 부지를 못 덮으면
    /// 그 칸이 조용히 빠진다. 그래서 <c>CollectQty</c>가 <b>측점마다 합을 견준다</b>.</para></remarks>
    private static DH.Grading.Core.XsecQty QtyAt(Transaction tr, CivilDb.Alignment al, double station,
        double wl, double wr,
        CachedGroundSurface csG, CachedGroundSurface csP, CachedGroundSurface csE,
        System.Text.StringBuilder dbg,
        System.Collections.Generic.IReadOnlyList<(DH.Grading.Core.RockClass Rock, CachedGroundSurface Cs)> strata = null,
        CachedGroundSurface csW = null,
        DH.Grading.Core.QtyLedger led = null,
        double deepLimit = 5.0)
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
        // ★★[JACK 0831 · 검토 LOW-9] <b>지층·지하수위 가장자리도 훑는다.</b>
        //   종전엔 원지반·계획·터파기 셋만 봤다 — 지층면이 끝나는 자리가 표본 사이에 있으면
        //   그 칸(0.1m × 깊이)이 통째로 빠진다. 지층은 층마다 있으니 <b>층 수만큼</b> 빠진다.
        var edgeSurfs = new System.Collections.Generic.List<CachedGroundSurface> { csG, csP, csE, csW };
        if (strata != null) foreach (var (_, cs2) in strata) edgeSurfs.Add(cs2);
        foreach (var cs in edgeSurfs)
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

        // ★★★[JACK 0831] 지층별 수량 — <b>바로 위에서 만든 그 배열</b>로 잰다.
        if (led != null)
        {
            try
            {
                var bands = new System.Collections.Generic.List<DH.Grading.Core.StrataQuantity.Band>();
                int nSkip2 = 0;
                if (strata != null)
                    foreach (var (rock, cs) in strata)
                    {
                        var z = Read(cs);
                        // ★★★[스스로 잡음] <b>중간 층을 건너뛰면 아래층이 그 몫을 먹는다.</b>
                        //   ★[JACK 0831] 선이 <b>상단</b>이 된 뒤로 방향이 <b>반대</b>다:
                        //   <c>StrataQuantity</c>는 "아래 경계 = 다음 층의 상단"으로 띠를 만드므로,
                        //   가운데 한 층이 빠지면 그 자리를 <b>위층이 흘러내려</b> 먹는다 —
                        //   풍화암이어야 할 부피가 토사로 잡히는 식이다. 예외도 안 난다.
                        //   ★지층면들은 <b>같은 격자</b>로 만들어지므로 보통은 전부 되거나 전부 안 된다.
                        //     그래서 여기서 하나만 빠지면 <b>지표면이 하나 없어진 것</b>이고,
                        //     그것은 조용히 넘길 일이 아니다 → 센다.
                        if (z == null) { nSkip2++; continue; }
                        bands.Add(new DH.Grading.Core.StrataQuantity.Band(rock, xs, z));
                    }
                if (nSkip2 > 0)
                    dbg?.Append($" ⚠<b>지층 {nSkip2}장이 이 절단선을 못 만났다 — 위층이 그 몫을 먹는다</b>");
                double[] wz = Read(csW);
                string note = DH.Grading.Core.StrataQuantity.Accumulate(
                    // ★[JACK 0831 검토] <b>축을 정말 넘긴다.</b> 주석은 넘긴다고 적혀 있었는데
                    //   코드는 <c>deepLimit</c>에서 끝나 <c>axis</c>가 기본 <c>null</c>이었다 —
                    //   오늘만 세 번째인 "주석이 코드보다 앞선" 자리다.
                    led, xs, gy, xs, py2, xs, ey, bands, xs, wz, deepLimit, xs);
                dbg?.Append(" | 지층 " + note);
            }
            catch (System.Exception exS) { dbg?.Append(" | ⚠지층 수량 실패 — " + exS.Message); }
        }

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

    /// <summary>한 도면치 수량 — 측점별 값, 측점별 <b>지층 원장</b>, 그리고 도면 전체의 <b>표 얼개</b>.</summary>
    /// <param name="Warn">수량이 조용히 틀어졌을 때 <b>명령창에 올릴</b> 말. 없으면 빈 문자열.</param>
    internal readonly record struct QtyResult(
        System.Collections.Generic.Dictionary<string, DH.Grading.Core.XsecQty> Map,
        System.Collections.Generic.Dictionary<string, DH.Grading.Core.QtyLedger> Ledgers,
        DH.Grading.Core.QtyTableSpec Spec,
        string Warn);

    /// <summary>측점 이름 → 그 측점의 수량. 표를 그릴 때 이 표를 찾아 값을 채운다.
    /// <para>★★★[JACK 0831] <b>표 얼개도 여기서 나온다</b> — 어느 한 측점에서라도 나온 조합을
    /// 모아(합집합) 도면 전체에서 <b>하나뿐인</b> 표 모양을 짓는다. 측점마다 줄 수가 다르면
    /// 횡단면도마다 축척이 제각각이 되어 도면을 못 쓴다.</para></summary>
    private static QtyResult
        CollectQty(Database db, List<(ObjectId Id, string Name, double St, double Mother, int Ord)> sl,
                   ObjectId alignId, double wl, double wr,
                   System.Collections.Generic.List<SectionCommand.SurfPick> surfs,
                   System.Text.StringBuilder log)
    {
        var map = new System.Collections.Generic.Dictionary<string, DH.Grading.Core.XsecQty>();
        var ledgers = new System.Collections.Generic.Dictionary<string, DH.Grading.Core.QtyLedger>();
        // ★경고 계수기는 <c>Done()</c>보다 <b>먼저</b> 선언한다 — 지역 함수가 이것들을 읽는다.
        int nMismatch = 0; string firstMismatch = null;
        int nRockUnknown = 0;
        int nOldVer = 0;          // ★옛 방식(층 하단)으로 만들어진 지층면 수
        QtyResult Done()
        {
            // ★합집합 — <b>실제로 값이 나온</b> 열쇠만 모은다(JACK 인터뷰 확정).
            //   0이나 NaN은 "나오지 않았다"로 본다 — 0인 줄을 세우면 표만 길어진다.
            var present = new System.Collections.Generic.HashSet<DH.Grading.Core.QtyKey>();
            foreach (var kv in ledgers)
                foreach (var k in kv.Value.Keys)
                {
                    double v = kv.Value.Get(k);
                    // ★★[JACK 0831 · 검토 MED-8] <b>줄을 세우는 문턱과 값을 찍는 문턱이 같아야 한다.</b>
                    //   종전엔 여기가 <c>1e-9</c>, 찍는 쪽이 <c>5e-3</c>이라 <b>6자리</b> 벌어져 있었다 —
                    //   0.005㎡짜리가 한 측점에서 한 번 나오면 <b>줄은 서고 모든 측점에서 –</b>가 된다.
                    //   줄 수가 축척을 정하므로 안 보이는 값 하나가 <b>도면 전체를 흔든다</b>.
                    if (!double.IsNaN(v) && System.Math.Abs(v) >= QtyShowMin) present.Add(k);
                }
            var spec = DH.Grading.Core.QtyTableSpec.BuildFromKeys(
                present, null, DH.Grading.Core.QuantityTable.DeepLimitM);
            log?.AppendLine($"  ★토적표 얼개 — 나온 조합 {present.Count}가지 → <b>{spec.TotalRows}줄</b>"
                          + $"(머리 1 + 내용 {spec.BodyRows})"
                          + " · <b>도면 전체에서 하나</b>다(측점마다 다르면 축척이 제각각이 된다)");
            // ★★★[JACK 0831 · 검토 MED-6·7] <b>조용히 토사로 세는 것은 로그만으로 부족하다.</b>
            //   폴백 토사와 진짜 토사는 <b>같은 줄</b>로 합쳐지므로 줄 수도 안 변하고 표도 멀쩡해 보인다.
            //   암 수량이 통째로 사라졌는데 화면에 아무 자국이 없다 → <b>명령창까지 올린다</b>.
            var warn = new System.Text.StringBuilder();
            if (nRockUnknown > 0)
                warn.Append($"\n  ⚠지층 {nRockUnknown}장의 암종을 못 알아내 [토사]로 셌습니다"
                          + " — 도킹바에서 [확인]을 다시 누르면 암종이 도면에 저장됩니다.");
            // ★★★[JACK 0831 검토] <b>옛 도면의 지층면은 하단 기준이다.</b>
            //   합계는 그대로 맞아 <c>Recon</c> 대조로도 안 잡힌다 — 오직 이 표시만이 갈라 준다.
            if (nOldVer > 0)
                warn.Append($"\n  ⚠⚠지층면 {nOldVer}장이 [옛 방식 = 층 하단]으로 만들어져 있습니다"
                          + " — 지금은 [층 상단] 기준이라 암종이 한 층씩 밀립니다."
                          + " 도킹바에서 [확인]을 다시 눌러 주세요.");
            if (nMismatch > 0)
                warn.Append($"\n  ⚠측점 {nMismatch}개에서 지층별 합이 전체와 다릅니다"
                          + " — 지층면이 부지를 다 못 덮었습니다.");
            return new QtyResult(map, ledgers, spec, warn.ToString());
        }
        if (sl == null || sl.Count == 0) return Done();
        int nOk = 0, nNo = 0, nThrow = 0; string firstThrow = null;
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
                return Done();
            }

            // ★[검토] 지표면 캐시를 <b>한 번만</b> 만들어 모든 측점이 나눠 쓴다 —
            //   측점마다 다시 만들면 삼각형을 수십 번 읽는다.
            CachedGroundSurface csG = null, csP = null, csE = null, csW = null;
            var strataCs = new List<(int Ord, DH.Grading.Core.RockClass Rock, CachedGroundSurface Cs, string Nm, string How)>();
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
                    // ★★★[JACK 0831] 지층·지하수위 캐시를 <b>버리지 않는다</b> —
                    //   종전엔 여섯 장을 만들어 놓고 바로 버렸다(검토 지적).
                    else if (sp.Label == "지층")
                    {
                        var rock = StrataDraw.RockOf(ts, out string how);
                        // ★★★[JACK 0831 검증] <b>세는 코드가 없었다.</b> 선언만 해 두고 읽기만 해서
                        //   <c>if (nRockUnknown > 0)</c>이 <b>늘 거짓</b>이었다 —
                        //   "명령창까지 올린다"고 적은 주석이 코드보다 앞서 나가 있었다.
                        //   암종을 못 알아내면 암 수량이 통째로 사라지는데 화면에 아무 자국이 없다.
                        if (how == "모름") nRockUnknown++;
                        // ★★★[JACK 0831 검토] <b>옛 도면의 지층면은 하단 기준이다.</b>
                        //   0831 오후부터 면을 <b>상단</b>으로 만드는데 그 전 것은 <b>하단</b>이다.
                        //   다시 [확인]을 안 누르고 횡단도를 돌리면 <b>암종이 한 층씩 밀린다</b>
                        //   (토사가 풍화암 몫을 먹는다). 값은 안 줄어들어 <b>합계 대조로도 안 잡힌다</b> —
                        //   S83이 바로 그것을 증명한다. 그래서 <b>만든 방식을 도면에서 읽어</b> 알린다.
                        if (StrataDraw.VerOf(ts) < StrataDraw.SurfVer) nOldVer++;
                        int ord = ProfileCommand.StrataOrdOf(sp.SurfName);
                        strataCs.Add((ord, rock, cs, StrataDraw.ShortName(sp.SurfName), how));
                    }
                    else if (sp.Label == "지하수위") csW = cs;
                }
                catch (System.Exception exC) { log?.AppendLine($"  ⚠지표면 '{sp.Label}' 캐시 실패 — {exC.Message}"); }
            }
            strataCs.Sort((a, b) => a.Ord.CompareTo(b.Ord));     // 위에서 아래 차례
            var strata = new List<(DH.Grading.Core.RockClass Rock, CachedGroundSurface Cs)>();
            foreach (var t in strataCs) strata.Add((t.Rock, t.Cs));
            log?.AppendLine($"  지표면 캐시 {nTri}장 — 절단선 표고를 예외 없이 읽는다"
                          + (strataCs.Count > 0 ? $" · 지층 {strataCs.Count}장" : " · 지층 없음")
                          + (csW != null ? " · 지하수위 있음" : " · 지하수위 없음"));
            // ★★[JACK 0831] <b>암종을 어디서 알았는지</b> 반드시 남긴다.
            //   못 읽으면 조용히 <b>토사</b>로 세는데, 그러면 암 수량이 통째로 사라지고
            //   도면에는 아무 자국도 안 남는다 — 수량에서 가장 위험한 종류다.
            foreach (var t in strataCs)
                log?.AppendLine($"    지층 {t.Ord}. {t.Nm} → {DH.Grading.Core.QtyTableSpec.NameOf(t.Rock)}"
                              + (t.How == "설명란" ? " (도면에 적혀 있음)"
                               : t.How == "도킹바" ? " (도킹바에서 읽음 — 도면에는 없다)"
                               : t.How == "이름" ? " (층 이름이 표준 이름과 같아 그렇게 봤다)"
                               : " ⚠<b>못 알아내 토사로 셌다 — 수량이 틀릴 수 있다</b>"));
            foreach (var s in sl)
            {
                try
                {
                    // ★[JACK 0826] 처음 세 개만 찍으면 하필 <b>부지 밖 측점</b>이 걸려
                    //   값이 나온 측점을 못 본다. <b>값이 나온 것</b>도 세 개까지 따로 남긴다.
                    // ★[JACK 0827] <b>모든 측점을 남긴다.</b> 셋만 찍으니 하필 부지 밖 측점이 걸려
                    //   "왜 전부 빈칸인지"를 볼 수가 없었다. 측점이 30개 남짓이라 길지도 않다.
                    var dbg = new System.Text.StringBuilder();
                    // ★측점마다 <b>새 원장</b>이다 — 돌려 쓰면 값이 겹쳐 쌓인다.
                    var led = new DH.Grading.Core.QtyLedger();
                    var q = QtyAt(tr, al0, s.St, wl, wr, csG, csP, csE, dbg,
                                  strata, csW, led, DH.Grading.Core.QuantityTable.DeepLimitM);
                    // ★★★[JACK 0831 · 검토 HIGH-1] <b>성토·되메우기를 원장에 넣는다.</b>
                    //   표가 이제 원장만 읽는데 <c>StrataQuantity.Accumulate</c>는
                    //   <b>절토·터파기만</b> 담는다 — 성토와 되메우기를 넣는 코드가 아예 없었다.
                    //   옛 표는 <c>XsecQty</c>에서 바로 꺼내 썼으므로 값이 있었는데,
                    //   얼개로 바꾸면서 <b>줄은 서고 값만 사라졌다</b> — 사용자는 "해당 없음"으로 읽는다.
                    //   ★두 값은 암종을 안 가른다(쌓는 흙·되메우는 흙) → 열쇠도 하나씩뿐이다.
                    led.Add(DH.Grading.Core.QtyKey.OfFill(), q.Fill);
                    led.Add(DH.Grading.Core.QtyKey.OfBackfill(), q.Backfill);
                    ledgers[s.Name] = led;

                    // ★★★[JACK 0831 · 스스로 잡음] <b>지층별 합이 전체와 맞는지 매번 대조한다.</b>
                    //
                    //   <c>CrossSectionArea.Above</c>는 <c>NaN</c> 칸을 <b>조용히 건너뛰고</b> 나머지를 더한다.
                    //   지층면이 부지보다 좁으면 그 밖 구간이 통째로 빠지는데 <b>예외도 경고도 없다</b> —
                    //   토적표만 보면 숫자가 다 차 있어 <b>멀쩡해 보인다</b>. 수량에서 가장 무서운 종류다.
                    //   → 전체 절토·터파기와 지층별 합을 <b>측점마다</b> 견주고, 어긋나면 그 자리에서 말한다.
                    double sCut = 0, sExc = 0;
                    bool anyCut = false, anyExc = false;
                    foreach (var k in led.Keys)
                    {
                        double v = led.Get(k);
                        if (double.IsNaN(v)) continue;
                        if (k.Kind == DH.Grading.Core.QtyKeyKind.Cut) { sCut += v; anyCut = true; }
                        else if (k.Kind == DH.Grading.Core.QtyKeyKind.Exc) { sExc += v; anyExc = true; }
                    }
                    void Recon(string nm2, double whole, double part, bool any)
                    {
                        if (double.IsNaN(whole) || !any) return;
                        double tol = System.Math.Max(0.01, System.Math.Abs(whole) * 0.001);   // 0.1% 또는 0.01㎡
                        if (System.Math.Abs(whole - part) <= tol) return;
                        nMismatch++;
                        firstMismatch ??= $"{s.Name} {nm2} 전체 {whole:F2}㎡ ≠ 지층합 {part:F2}㎡"
                                        + $"(차이 {whole - part:F2}㎡)";
                        dbg?.Append($" ⚠<b>{nm2} 합 불일치</b> 전체 {whole:F2} ≠ 지층합 {part:F2}");
                    }
                    Recon("절토", q.Cut, sCut, anyCut);
                    Recon("터파기", q.ExcTotal, sExc, anyExc);
                    if (dbg != null && dbg.Length > 0) log?.AppendLine($"    [{s.Name}]{dbg}");
                    map[s.Name] = q;
                    if (double.IsNaN(q.Cut) && double.IsNaN(q.ExcShallow)) nNo++;
                    if (q.NoPlanCells > 0) nNoPlan++;
                    if (q.MissG) nNoG++;
                    if (q.MissP) nNoP++;
                    if (q.MissE) nNoE++;
                    // ★★★[검토 0828] <b><c>else</c>가 터파기 <c>if</c>에만 붙어 있었다.</b>
                    //   그래서 합계가 <b>터파기가 잡힌 측점만</b> 더했다 —
                    //   28개 측점 중 6개. 절토 <b>3677.7㎡가 1405.8㎡</b>로 찍혔다(2.6배 축소).
                    //   도면의 수량표는 멀쩡했다(<c>map</c>은 이 위에서 담는다) —
                    //   <b>JACK이 BO와 대조할 때 보는 그 요약 한 줄만</b> 틀렸다. 그래서 더 나빴다.
                    //   → 합계에 넣을 조건은 <b>그 값이 있느냐</b>이지 터파기가 있느냐가 아니다.
                    if (!double.IsNaN(q.Cut) || !double.IsNaN(q.Fill) || !double.IsNaN(q.ExcTotal))
                    {
                        nOk++;
                        if (!double.IsNaN(q.Cut)) sumCut += q.Cut;
                        if (!double.IsNaN(q.Fill)) sumFill += q.Fill;
                        if (!double.IsNaN(q.ExcTotal)) sumExc += q.ExcTotal;
                    }
                }
                // ★[검토 0828 · M10] <b>조용히 사라지는 측점을 센다.</b>
                //   종전엔 <c>catch { }</c>였다 — <c>QtyAt</c>이 던지면 <c>nOk</c>도 <c>nNo</c>도 안 늘고
                //   지도에도 안 들어가 <b>표는 –인데 로그엔 자취가 없었다</b>.
                catch (System.Exception exQ) { nThrow++; firstThrow ??= $"{s.Name} — {exQ.Message}"; }
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
                      + (nThrow > 0 ? $" · ⚠<b>쟰다가 터진 측점 {nThrow}개</b>(첫 사례: {firstThrow})" : "")
                      + (nMismatch > 0
                         ? $" · ⚠⚠<b>지층합이 전체와 안 맞는 측점 {nMismatch}개</b>({firstMismatch})"
                           + " — <b>지층면이 부지를 다 못 덮었다는 뜻이다</b>"
                         : " · 지층합 = 전체 (대조 통과)")
                      + $" · 합계 절토 {sumCut:F1}㎡ · 성토 {sumFill:F1}㎡ · 터파기 {sumExc:F1}㎡"
                      + "  ※단면 면적이다(체적은 측점 간격을 곱해야 한다)");
        return Done();
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
        string[] mine = MyLayers;
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
    /// <summary>★★★[JACK 0828 "횡단도를 다시 눌러 리셋될 때 측점 밴드값이 안 없어지고 남아 있어"]
    /// <b>우리가 횡단에 그리는 레이어 — 목록은 여기 하나뿐이다.</b>
    /// <para><b>원인이 정확히 §50이었다.</b> 같은 개념(우리가 그린 횡단 산출물)을 <b>두 곳이 따로</b> 들고 있었다 —
    /// <see cref="WipeOld"/>는 배열로, <see cref="SheetCommand"/>의 종단도 청소는 <b>이름을 하나씩 손으로 적어</b>.
    /// 오늘 <c>DH-횡단-측점</c>을 새로 만들면서 <b>앞쪽에만</b> 넣었더니,
    /// <b>종단도가 돌 때만</b> 측점 글씨가 유령으로 남았다(횡단도를 다시 누르면 지워지니 안 보였다).</para>
    /// <para>→ <b>목록을 하나로 모은다.</b> 레이어를 더하는 사람은 이제 여기만 고치면 된다 —
    /// 두 곳을 기억해야 하는 구조는 <b>언젠가 반드시</b> 한쪽을 빠뜨린다.</para></summary>
    internal const string XsecStrataNameLayer = "DH-지층이름";
    internal const string XsecWaterNameLayer = "DH-지하수위이름";

    // ★★[JACK 0828 검토] <b>새 글씨 레이어는 반드시 이 목록에.</b>
    //   여기 안 넣으면 다시 그릴 때마다 이름이 <b>겹쳐 쌓인다</b> —
    //   같은 자리에 같은 글자라 눈으로는 굵어진 것처럼만 보여 늦게 알아차리게 된다.
    internal static readonly string[] MyLayers =
    {
        XsecTitleLayer, XsecStationLayer, XsecAxisLayer, XsecTextLayer, XsecCellLayer,
        QtLayerEdge, QtLayerLine, QtLayerText, XsecFrameLayer,
        XsecStrataNameLayer, XsecWaterNameLayer,
    };

    internal static string TitleLayer => XsecTitleLayer;
    internal static string AxisLayer => XsecAxisLayer;
    internal static string TextLayer => XsecTextLayer;
    internal static string CellLayer => XsecCellLayer;
    internal static string FrameLayer => XsecFrameLayer;
    internal static string QtEdgeLayer => QtLayerEdge;
    internal static string QtLineLayer => QtLayerLine;
    internal static string QtTextLayer => QtLayerText;
}
