using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Civil.DatabaseServices;
using DH.Grading.Core;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace DH.Grading.Civil.Commands;

/// <summary>
/// "정지면 생성"(DHGRADE) — [통합 파이프라인, JACK 설계]
/// ① 계획폴리곤+원지반 → 오버사이즈 가상 절토/성토 TIN 생성(기존 로직 그대로)
/// ② 성토: 가상성토↔원지반+계획폴리곤 교선(DHXSEC 엔진 그대로) → 가상성토의 Outer 경계로 주입
/// ③ 절토: 같은 방식
/// ④ 교선 초록선은 '마지막에 한 번만' 그림 — 그리기 단계의 레이어 청소가 성토 결과를 지우지 않게(JACK).
/// </summary>
/// <summary>[다중 구역 0729 — 방식A] 정지 실행 모드 — Fresh=새로시작, Append=이어서(누적, 새 구역 추가),
/// RerunLast=마지막 구역만 재실행(DHWALL 옹벽 적용·설정 변경 재실행).</summary>
internal enum GradeMode { Fresh, Append, RerunLast }

/// <summary>★★[v32.15] <b>붙여넣기 줄 제거 스위치 — 켠다.</b>
///
/// <para><b>다른 축이 전부 닫혔다.</b> 호출 순서·횟수(v32.5~v32.10) · 대상 범위(v32.7~v32.8) ·
/// 트랜잭션 경계(v32.12) · 스냅샷 생애주기 재생성(v32.14, <c>RemoveSnapshot→Rebuild→CreateSnapshot</c>).
/// 게다가 JACK이 <c>-REBUILDSURFACE</c> 명령을 직접 실행해도 ⚠가 안 사라졌다 —
/// <b>'지표면 재작성' 경로로는 애초에 못 지운다</b>는 실험 A의 결론이 명령 수준에서도 확인된 셈이다.</para>
///
/// <para>그래서 <b>지우는 것을 포기하고, 붙어 있을 줄을 없앤다.</b> 스냅샷이 정의 맨 끝에 있으면
/// 형상은 스냅샷이 통째로 물고 있고 앞의 붙여넣기는 <b>빌드에서 무시된다</b>(공식 문서).
/// 즉 그 줄들이 하는 일은 <b>소스에 매달려 ⚠를 다는 것뿐</b>이다.</para>
///
/// <para><b>되돌리는 법</b>: 이 값을 <c>false</c>로 바꾸면 끝. 안전판(삼각형 수 검사 → 미달 시 커밋 안 함)도
/// 그대로 살아 있어, 형상이 조금이라도 줄면 <b>도면은 손대기 전과 같아진다</b>.</para>
///
/// <para><b>대가</b>(자문2 §8 지적): 정의에 붙여넣기 이력이 남지 않아 <b>재현성이 준다</b>.
/// 다만 이 저장소는 정지면을 매번 <b>처음부터 다시 만든다</b>(<c>Composite</c>가 같은 이름 표면을 지우고 새로 생성)
/// — 정의 이력에 기대는 경로가 없다. 확인할 것은 <b>'이어서 하기'에서 소스로 쓸 때</b>뿐이다.</para></summary>
/// <para>★★[v32.18 · 실측으로 기각] <b>켜 봤고, 되돌렸다.</b>
/// 붙여넣기 3줄을 지우니 형상은 온전했지만(삼각형 64978 → 64978) <b>⚠는 그대로였고</b>,
/// 게다가 <c>스냅샷구식=True</c>가 <b>처음으로</b> 떴다 — 정의에 스냅샷을 다시 구울 재료가 없어졌기 때문이다.
/// <b>증상은 그대로인데 상태만 나빠졌다.</b> 되돌린다.</para>
internal static class GradeFlags { public const bool StripPasteOps = false; }

public sealed class CreateGradingCommand
{
    [CommandMethod("DHGRADE")]
    public void Run()
    {
        Document doc = AcadApp.DocumentManager.MdiActiveDocument;
        if (doc == null) return;
        GradingSettings.SyncToDocument(doc);   // [도면 전환 0803] 도면이 바뀌었으면 그 도면 기준으로 설정·기억 재정렬
        Editor ed = doc.Editor;

        bool isWall = GradingSettings.CutSlope <= 1e-6 || GradingSettings.FillSlope <= 1e-6;
        ed.WriteMessage(
            $"\n[정지면 생성] 절토 단높이 {GradingSettings.CutBenchHeight}m·소단 {GradingSettings.CutBenchWidth}m · " +
            $"성토 단높이 {GradingSettings.FillBenchHeight}m·소단 {GradingSettings.FillBenchWidth}m · " +
            $"절토 1:{GradingSettings.CutSlope} · 성토 1:{GradingSettings.FillSlope}{(isWall ? " (수직 옹벽)" : "")}" +
            "  — 값 변경은 [정지 설정]");

        // [§75 — JACK 0728] 이전 실행이 정지면_DH만 남기고 숨겼을 수 있음 → 원지반을 클릭 선택해야 하므로 전부 복원.
        try
        {
            using var trV = doc.Database.TransactionManager.StartTransaction();
            GradingBuilder.IsolateSurfaces(trV, null);
            trV.Commit();
        }
        catch { }

        // ★★★[검토 0908] <b>지난번 자국을 치우는 장치가 통째로 없어졌다.</b>
        //   첫 판은 객체 색을 진짜로 바꾸고 원래 색을 도면에 적어 두었다가
        //   다음 실행이 되돌리게 했는데, 그 되돌리기가 <b>세 군데에서 샜다</b>(검토 0908).
        //   지금은 <see cref="PickMark"/>가 <b>임시 그래픽</b>으로 덧그리므로
        //   도면에 아무것도 안 남는다 — <b>치울 자국 자체가 없다</b>.
        // 1) 계획 폴리곤 선택
        var peoPoly = new PromptEntityOptions("\n계획 경계(닫힌 폴리라인/3D폴리라인/피처라인)를 선택: ");
        peoPoly.SetRejectMessage("\n폴리라인 또는 피처라인이어야 합니다.");
        peoPoly.AddAllowedClass(typeof(Polyline), false);
        peoPoly.AddAllowedClass(typeof(Polyline3d), false);
        peoPoly.AddAllowedClass(typeof(FeatureLine), false);
        var rPoly = ed.GetEntity(peoPoly);
        if (rPoly.Status != PromptStatus.OK) return;

        // ★★★[JACK 0908 "선택한 걸 알 수 있도록 알아보기 쉽게 빨간색으로 색 바뀌고,
        //   정지가 끝나면 다시 원래색으로 복귀"] <b>고른 것을 빨갛게.</b>
        //   <c>using</c>이라 <b>정상 끝·예외·Esc</b> 어느 쪽으로 빠져나가도 되돌아온다 —
        //   이 메서드에는 <c>return</c>이 여럿이라(설정 취소·지반 선택 취소 등) 그것이 중요하다.
        using var pickMark = PickMark.Paint(doc.Database, rPoly.ObjectId);

        // [다중 구역 0729 — 방식A] 기존 정지면·번들이 있으면 '이어서(누적)/새로시작' 선택.
        //   이어서 = 기존 정지면_DH를 새 원지반 삼아 이 계획선 구역을 추가(1번 구역 유지).
        //   같은 계획선을 다시 고르면 = 마지막 구역 재실행(설정 바꿔 다시). 중간 구역 수정은 미지원.
        // ★★★[검토 0909 · H6] <b>판정을 <see cref="GradeStart.Decide"/> 하나로.</b>
        //
        //   이 45줄을 도킹창이 쓸 수 없어 <see cref="GradeStart"/>로 떼어냈는데,
        //   <b>정작 여기는 옛 사본을 그대로 쓰고 있었다</b> — 함정을 피하려고 만든 파일이
        //   그 자리에서 <b>함정을 하나 더 만든 셈</b>이다(§20·§26).
        //   여섯 갈래를 대조해 보니 오늘은 답이 같았다 — 그래서 <b>내일 갈라진다</b>.
        //   ★검사기가 이것을 못 잡는다(§7: 5b는 "불가"). 그러니 사본을 안 두는 것이 유일한 방어다.
        var mode = GradeMode.Fresh;
        ObjectId groundSel = ObjectId.Null;
        {
            // 기존 결과가 있으면 <b>먼저 묻는다</b> — 옛 흐름 그대로(창은 라디오로 답을 받는다).
            bool append = true;
            if (GradeStart.HasPrevious(doc.Database, out int nRegion))
            {
                var pko = new PromptKeywordOptions(
                    $"\n기존 정지면_DH(구역 {nRegion}개)가 있습니다 — 이어서 추가할까요, 새로 시작할까요?");
                pko.Keywords.Add("이어서");
                pko.Keywords.Add("새로시작");
                pko.Keywords.Default = "이어서";
                pko.AllowNone = true;
                var kr = ed.GetKeywords(pko);
                if (kr.Status == PromptStatus.Cancel) return;
                string kw = kr.Status == PromptStatus.Keyword || kr.Status == PromptStatus.OK
                          ? kr.StringResult : "이어서";
                append = kw == "이어서";
            }

            var plan = GradeStart.Decide(doc, rPoly.ObjectId, append);
            if (plan.Blocked)
            {
                ed.WriteMessage("\n[DHGRADE] " + plan.Note.Replace("\n", "\n  "));
                AcadApp.ShowAlertDialog(plan.Note);
                return;
            }
            mode = plan.Mode;
            groundSel = plan.GroundAuto;
            if (!string.IsNullOrEmpty(plan.Note)) ed.WriteMessage("\n" + plan.Note);
        }


        // 2) 원지반 TinSurface 선택 — 이어서(누적)는 기준이 자동(현재 정지면)이라 생략.
        if (groundSel.IsNull)
        {
            string gp = mode == GradeMode.RerunLast ? "\n기준 지반 표면(TIN Surface)을 선택: "
                                                    : "\n원지반 표면(TIN Surface)을 선택: ";
            var peoSurf = new PromptEntityOptions(gp);
            peoSurf.SetRejectMessage("\nTIN Surface여야 합니다.");
            peoSurf.AddAllowedClass(typeof(TinSurface), true);
            var rSurf = ed.GetEntity(peoSurf);
            if (rSurf.Status != PromptStatus.OK) return;
            groundSel = rSurf.ObjectId;
        }
        else if (mode == GradeMode.Append)
            ed.WriteMessage("\n[이어서] 기준 지반 = 현재 정지면_DH (기존 구역 유지, 새 구역 추가)");

        DoGrade(doc, rPoly.ObjectId, groundSel, mode);
    }

    /// <summary>[§75] 정지면 생성 파이프라인 본체 — DHGRADE(프롬프트 후)와 DHWALL(Enter 시 재선택 없이 즉시 재생성)이 공용.
    /// 옹벽 전환 선택(WallPicks)이 있으면 그 단부터 수직 옹벽으로 만든다(1차: 방향 전체).
    /// [다중 구역 0729 — 방식A] mode: Fresh=처음부터(구역 1개), Append=현재 정지면을 기준 지반 삼아 구역 추가
    /// (기존 정지면은 '정지면_DH이전'으로 이름 변경·숨김 보존), RerunLast=마지막 구역만 다시(DHWALL·설정 변경).</summary>
    internal static void DoGrade(Document doc, ObjectId planPolyId, ObjectId groundId, GradeMode mode = GradeMode.Fresh)
    {
        // [JACK 0731] 정지면 생성 중 이벤트 뷰어 알림(팝업)만 끄기 — 기록은 남음. 어떤 경로로 끝나든 원복.
        var evPrev = EventViewerMute.Begin();
        try { DoGradeInner(doc, planPolyId, groundId, mode); }
        finally { EventViewerMute.End(evPrev); }
    }

    private static void DoGradeInner(Document doc, ObjectId planPolyId, ObjectId groundId, GradeMode mode)
    {
        Editor ed = doc.Editor;
        Database db = doc.Database;

        // [사면생성 0729 — 리뷰] ZoneOverride는 진입 즉시 스냅샷+클리어(1회성 보장) — 조기 return 시
        //   남아서 다음 실행에 잘못 적용되는 누출 방지. 전체해제 플래그도 동일하게 1회성 소비.
        var zoneOverride = GradingSettings.ZoneOverride;
        GradingSettings.ZoneOverride = null;
        // ★[JACK 0914] 전이면 선 통로는 <b>더 쓰지 않는다</b> — 지표면을 만들어 덮는 방식이 버려졌다.
        //   그래도 진입 때 비운다(옛 값이 남아 있으면 혼란을 준다).
        GradingSettings.TransitionLines = null;
        bool zoneReplaceAll = GradingSettings.WallZoneReplaceAll;
        GradingSettings.WallZoneReplaceAll = false;

        // [다중 구역] 기존 구역 목록 + Append의 기준면 개명(실패 시 원복용 핸들).
        System.Collections.Generic.List<GradingBundle>? regionsPrev = null;
        string? baseRestoreHandle = null;
        // ★★[v32.2] 순수 정지면(<see cref="SectionCommand.PurePadSurfaceBase"/>)의 <b>앞 구역 몫</b>.
        //   합성면이 '이전 정지면'을 깔고 누적하듯, 순수면도 '이전 순수면'을 깔고 누적한다 —
        //   안 그러면 이어서 할 때마다 <b>앞 구역이 종단에서 사라진다</b>.
        ObjectId prevPureId = ObjectId.Null;
        const string PureBase = SectionCommand.PurePadSurfaceBase;
        const string PurePrev = PureBase + "이전";
        try
        {
            using var trM = db.TransactionManager.StartTransaction();
            if (mode != GradeMode.Fresh)
                regionsPrev = GradingBundleStore.TryLoadAll(db, trM, out _);
            if (mode == GradeMode.Append)
            {
                // 옛 기준면 정리 — 현재 정지면은 스냅샷으로 굳어 있어(합성 시 Freeze) 소스가 지워져도 형상 유지.
                GradingBuilder.EraseSurfacesByBaseName(trM, "정지면_DH이전");
                var baseSurf = (Autodesk.Civil.DatabaseServices.Surface)trM.GetObject(groundId, OpenMode.ForWrite);
                baseSurf.Name = GradingBuilder.UniqueName(db, trM, "정지면_DH이전");
                baseRestoreHandle = groundId.Handle.ToString();

                // 순수면도 같은 방식으로 물려준다 — <b>이름을 비켜 줘야</b> 새로 합성할 때 안 지워진다
                // (합성은 시작할 때 같은 이름 표면을 지운다).
                try
                {
                    GradingBuilder.EraseSurfacesByBaseName(trM, PurePrev);
                    var cur = GradingBuilder.FindSurfaceByBaseName(trM, PureBase);
                    if (!cur.IsNull)
                    {
                        var ps = (Autodesk.Civil.DatabaseServices.Surface)trM.GetObject(cur, OpenMode.ForWrite);
                        ps.Name = GradingBuilder.UniqueName(db, trM, PurePrev);
                        prevPureId = cur;
                    }
                }
                catch { prevPureId = ObjectId.Null; }   // 못 물려받아도 이번 구역은 나온다(앞 구역만 빠진다)
            }
            else if (mode == GradeMode.Fresh)
            {
                GradingBuilder.EraseSurfacesByBaseName(trM, "정지면_DH이전");   // 새로시작 — 잔재 청소
                GradingBuilder.EraseSurfacesByBaseName(trM, PurePrev);
            }
            else   // RerunLast — 기준면(…이전)은 그대로 두고 마지막 구역만 다시 얹는다.
            {
                try { prevPureId = GradingBuilder.FindSurfaceByBaseName(trM, PurePrev); } catch { }
            }
            trM.Commit();
        }
        catch (System.Exception mx)
        {
            ed.WriteMessage("\n[DHGRADE] 구역 준비 실패 — " + mx.Message);
            AcadApp.ShowAlertDialog("구역 준비 중 오류:\n" + mx.Message);
            return;
        }
        // [리뷰 0729] Append인데 기존 구역을 못 읽었으면 진행 금지 — 번들이 단일 구역으로 접혀
        //   이전 구역 기록이 사라지는 것 방지(도면 표면은 남지만 노리선/내보내기에서 빠짐).
        if (mode == GradeMode.Append && (regionsPrev == null || regionsPrev.Count == 0))
        {
            TryRestoreBase(db, baseRestoreHandle);
            ed.WriteMessage("\n[DHGRADE] 기존 구역 번들을 읽지 못해 '이어서'를 중단합니다.");
            AcadApp.ShowAlertDialog("기존 구역 정보를 읽지 못해 '이어서'를 중단합니다.\n[새로시작]으로 실행하거나 도면을 확인하세요.");
            return;
        }

        // [§75] 다음 DHWALL 즉시 재생성용으로 계획선·기준 지반 기억(세션 메모리 — Append면 기준=이전 누적면).
        GradingSettings.LastPlanHandle = planPolyId.Handle.ToString();
        GradingSettings.LastGroundHandle = groundId.Handle.ToString();
        try
        {
            DiagLog.Append(
                $"\n■ DoGrade 시작 {System.DateTime.Now:HH:mm:ss} — 모드 {mode} · 기존구역 {regionsPrev?.Count ?? 0} · " +
                $"옹벽선택 {GradingSettings.WallPicks.Count}건\n");
        }
        catch { }

    /// <summary>★★★[JACK 0916] <b>구간을 고르면 폴리곤 하나만 그린다.</b>
    ///
    /// <para><b>길이</b>는 고른 구간 그대로. <b>폭</b>은
    /// 「남은 단수 × (단높이 × <b>계획 구배</b> + 소단폭)」 + 여유.
    /// 미는 방향은 구간선의 <b>직각방향</b>이고, 어느 쪽이 사면인지는 <b>땅을 재서</b> 정한다.</para>
    ///
    /// <para>★<b>계획 구배</b>를 쓴다(옹벽 구배가 아니다). 0916 실측: 옹벽 구배 1:0.01로 재니
    /// 폭이 <b>14.2m</b>였는데 데이라잇은 <b>27.83m</b>였다 — 절반에서 끝났다.
    /// 폭은 「벽이 얼마나 가느냐」가 아니라 「<b>사면이었다면</b> 얼마나 갔겠느냐」다.</para></summary>
    /// <summary>★★★[JACK 0918] <b>폴리곤 안에 정형화된 가상 옹벽을 세운다.</b>
    ///
    /// <para>JACK: <i>"안쪽 해당 폴리곤 안에 속하는 <b>원지반 높이보다 높은 단</b>
    /// (단높이는 매개변수에 따름)까지 가상 옹벽을 치는 걸 추가해."</i></para>
    ///
    /// <para>서는 자리는 <b>측선 → 선택구간 → 측선</b> 셋이고, <b>폐합면은 수직</b>이라 빠진다
    /// (JACK: <i>"나머지 폴리곤을 닫기 위한 선분은 설정에 관계없이 수직으로 침"</i>).</para>
    ///
    /// <para>★<b>못 세워도 막지 않는다</b> — 폴리곤은 이미 그렸으므로, 옹벽만 건너뛰고
    /// <b>왜 못 세웠는지</b>를 적는다.</para></summary>
    static string BuildWallInPolygon(Database db, Transaction tr,
        System.Collections.Generic.List<Point3> poly, double zBase, bool wUp,
        GradingParams p, IGroundSurface ground, ObjectId groundId)
    {
        try
        {
            // ★★★[JACK 0918 스샷 <i>"<b>폴리곤대로 만들어지지 않았어</b>, 옹벽면만 만들어졌지"</i>]
            //   <b>폴리곤 전체</b>로 짓는다 — 변마다 규칙이 다르다.
            //   <para>옹벽 변은 단마다 안쪽으로 물러나고, <b>폐합면은 제자리에서 수직</b>으로 오른다.
            //   맨 위 줄이 곧 <b>뚜껑의 테두리</b>라 폴리곤이 다 채워진다
            //   (첫 판은 옹벽이 서는 <b>열린 선</b>만 줄로 만들어 «옹벽면만» 생겼다).</para>
            var flags = GradingSettings.WallPolyIsWall;
            GradingSettings.WallPolyChain = null; GradingSettings.WallPolyIsWall = null;   // ★한 번 쓰고 비운다
            if (flags == null || flags.Count != poly.Count)
                return $"    ⚠<b>옹벽을 못 세웠다</b> — 변 나누기 표가 없다"
                     + $"(폴리곤 {poly.Count}점 · 표 {(flags == null ? "없음" : flags.Count + "개")})\n";

            // ① 폴리곤 안 원지반 최고
            double? top = WallInPoly.MaxGroundIn(poly,
                (x, y) => ground.TryGetElevation(x, y, out double gz) ? gz : (double?)null,
                System.Math.Max(1.0, p.VertexSpacing), out int nHit, out int nMiss);
            if (top == null)
                return $"    ⚠<b>옹벽을 못 세웠다</b> — 폴리곤 안에서 원지반을 <b>한 자리도 못 쟀다</b>"
                     + $"(못 잰 자리 {nMiss}) — 측량 범위를 벗어났는지 보세요\n";

            // ② 원지반을 <b>넘는</b> 단수
            double benchH = p.BenchHeightOf(wUp);
            double slopeW = System.Math.Max(GradingSettings.MinSlope, 0);
            double benchW = p.BenchWidthOf(wUp);
            int nb = WallInPoly.BenchCount(zBase, top.Value, benchH);

            // ③ 옹벽이 서는 <b>열린 선</b>을 표에서 뽑는다 — 이어지는 옹벽 점들
            var wline = new System.Collections.Generic.List<Point3>();
            {
                int n0 = poly.Count, st = -1;
                for (int i = 0; i < n0; i++)
                    if (flags[i] && !flags[(i - 1 + n0) % n0]) { st = i; break; }   // 옹벽이 <b>시작</b>하는 자리
                if (st < 0 && flags.Count > 0 && flags[0]) st = 0;                   // 전부 옹벽이면 아무 데서나
                if (st >= 0)
                    for (int k = 0; k < n0; k++)
                    {
                        int i = (st + k) % n0;
                        if (!flags[i]) break;
                        wline.Add(poly[i]);
                    }
            }
            if (wline.Count < 2)
                return $"    ⚠<b>옹벽을 못 세웠다</b> — 옹벽 변이 이어지지 않는다(옹벽 점 {wline.Count}개)\n";

            // ④ 줄 — <b>「옹벽선에서 d만큼 떨어진 자리」</b>를 NTS에게 묻는다
            //   <para>점마다 미는 방식은 옹벽과 폐합면이 만나는 모서리에서 <b>칼날 같은 삼각형</b>을 남겼다
            //   (JACK 0918 스샷). NTS는 코너 트림도 겹침 정리도 <b>스스로</b> 한다.</para>
            var rows = WallInPoly.RowsByBuffer(poly, wline, zBase, nb, benchH, slopeW, benchW,
                                               p.MinFaceRun, out string rlog);
            if (rows.Count < 2)
                return $"    ⚠<b>옹벽을 못 세웠다</b> — 줄이 {rows.Count}개뿐({rlog})\n";

            // ⑤ 면으로 — 계단 브레이크라인이라 허용오차는 <b>0.001</b>이어야 한다(면 폭이 5cm다)
            var wid = GradingBuilder.BuildVirtualSlope(db, tr,
                new System.Collections.Generic.List<System.Collections.Generic.List<Point3>>(),
                "가상옹벽_DH", rows, groundId, null, midOrd: 0.001);
            string vseen = "";
            try
            {
                var tin = (TinSurface)tr.GetObject(wid, OpenMode.ForWrite);
                // ★제 폴리곤으로 가둔다 — 없으면 볼록껍질이 엉뚱한 자리를 메운다
                try { GradingBuilder.AddOuterBoundary(tin, poly, midOrd: 0.001); }
                catch (System.Exception be) { vseen += $" · ⚠경계 실패 {be.GetType().Name}"; }
                try { using var tc = tin.GetTriangles(false); vseen += $" · <b>삼각형 {tc.Count}개</b>"; }
                catch { }
            }
            catch (System.Exception te) { vseen += $" · ⚠면 손질 실패 {te.GetType().Name}"; }
            try { vseen += " · " + GradingBuilder.MakeSurfaceVisible(db, tr, "가상옹벽_DH",
                                       "DH-가상옹벽면", "DH-가상옹벽", 6); }
            catch { }
            try { DrawLinesOnLayer(db, tr, rows, "DH-가상옹벽선", 6); } catch { }

            return $"    ★<b>가상 옹벽</b> — 폴리곤 안 원지반 최고 <b>{top:F2}m</b>"
                 + $"(잰 자리 {nHit}{(nMiss > 0 ? $" · 못 잰 자리 {nMiss}" : "")})"
                 + $" · 찍은 선 {zBase:F2}m → <b>{zBase + benchH * nb:F2}m</b>"
                 + $" · {rlog}{vseen}\n";
        }
        catch (System.Exception ex)
        { return $"    ⚠<b>옹벽을 못 세웠다</b> — {ex.GetType().Name}: {ex.Message}\n"; }
    }

    static string BuildWallBoxPolygon(Database db, Transaction tr,
        SlopeZone wz, bool wUp, GradingParams p, IGroundSurface ground,
        System.Collections.Generic.List<Point3> boundary, double toD,
        System.Collections.Generic.List<Point3>? manual, string manualWhy, ObjectId groundId)
    {
        var rul = wz.Ref ?? boundary;
        var rcm = wz.RefCum ?? GradingGeometry.CumLen2D(rul);
        double rTot = rcm[rcm.Length - 1];
        double spanW = wz.T1 >= wz.T0 ? wz.T1 - wz.T0 : rTot - wz.T0 + wz.T1;
        double zBase = GradingGeometry.PointAtParam(rul, rcm, wz.T0).Z;

        // ── ★★★[JACK 0917] <b>손으로 그린 것이 있으면 그것을 쓴다.</b> ──
        //   <para>한 번 쓰고 <b>비운다</b> — 안 비우면 다음 실행이 <b>지난번에 그린 것</b>을 제 것인 양 쓴다.</para>
        //   <para>못 쓰게 생겼으면(제 몸을 지르거나 넓이가 없으면) <b>막지 않고</b>
        //   계산한 띠로 물러난다. 다만 <b>왜 안 썼는지</b>는 반드시 적는다.</para>
        string manualNote = manualWhy;
        {
            if (manual != null && manual.Count >= 3)
            {
                var mp = new System.Collections.Generic.List<Point3>();
                foreach (var q in manual) mp.Add(new Point3(q.X, q.Y, zBase));
                bool mOk = GradingGeometry.RingIsSimple(mp);
                double mA = GradingGeometry.RingAreaNts(mp);
                if (mOk && mA > 1.0)
                {
                    var plM = new System.Collections.Generic.List<Point3>(mp) { mp[0] };
                    DrawLinesOnLayer(db, tr,
                        new System.Collections.Generic.List<System.Collections.Generic.List<Point3>> { plM },
                        "DH-가상폴리곤", PolyAci);
                    string wallNote = BuildWallInPolygon(db, tr, mp, zBase, wUp, p, ground, groundId);
                    return $"    ★<b>손으로 그린 폴리곤</b>을 썼다 — 'DH-가상폴리곤' <b>{mp.Count}점 / {mA:F1}㎡</b>"
                         + $" · 표고 {zBase:F2}m · 계산한 띠는 <b>안 썼다</b> · DHRESET이 걷어 간다\n"
                         + wallNote;
                }
                manualNote = $"    ⚠<b>손으로 그린 폴리곤을 못 썼다</b>(점 {mp.Count}개"
                           + $" · 제 몸을 안 지르는가 {(mOk ? "예" : "<b>아니오</b>")}"
                           + $" · 넓이 {mA:F1}㎡) — <b>계산한 띠</b>로 간다\n";
            }
        }
        double benchH = p.BenchHeightOf(wUp);
        double slopePlan = System.Math.Max(wUp ? p.CutSlope : p.FillSlope, p.MinSlope);
        double benchWPlan = p.BenchWidthOf(wUp);
        double runPer = benchH * slopePlan + benchWPlan;      // 한 단이 평면에서 가는 거리
        int nStepT = 64, maxBench = 80;
        int calcN = 0, walkN = 0, nMeasured = 0, nNoGround = 0;
        double worstCut = 0;
        for (int i = 0; i <= nStepT; i++)
        {
            double t = wz.T0 + spanW * i / nStepT;
            double tw = ((t % rTot) + rTot) % rTot;
            var pOn = GradingGeometry.PointAtParam(rul, rcm, tw);
            // ① 남은 단수 — 선 위 <b>지반고</b>와 <b>계획고</b>의 차(JACK이 정한 식)
            if (ground.TryGetElevation(pOn.X, pOn.Y, out double zgOn))
            {
                double cutH = wUp ? zgOn - pOn.Z : pOn.Z - zgOn;
                if (cutH > worstCut) worstCut = cutH;
                int nc = (int)System.Math.Ceiling(System.Math.Max(0, cutH) / System.Math.Max(0.01, benchH));
                if (nc > calcN) calcN = nc;
            }
            else nNoGround++;
            // ② 견줌 — 땅에 대고 한 단씩 나가며 세 본다(쓰지는 않는다)
            double zCur = pOn.Z, dCur = 0; int nb = 0; bool met = false;
            for (; nb < maxBench; nb++)
            {
                dCur += benchH * slopePlan; zCur += wUp ? benchH : -benchH;
                var q = GradingGeometry.OutwardAt(rul, rcm, tw, dCur);
                if (ground.TryGetElevation(q.X, q.Y, out double zg))
                { if (wUp ? zg <= zCur : zg >= zCur) { nb++; met = true; break; } }
                dCur += benchWPlan;
            }
            if (met) nMeasured++;
            if (nb > walkN) walkN = nb;
        }
        if (calcN <= 0) calcN = 1;
        double widthW = calcN * runPer + GradingSettings.WallPolygonMargin;
        double walkD = walkN * runPer;                      // 땅에 대고 센 데이라잇 거리

        // ── ★방향 — <b>재서</b> 정한다. 바깥이 늘 사면 쪽이라고 가정하지 않는다 ──
        double sgn = 1.0; string dirWhy;
        {
            double twm = (((wz.T0 + spanW * 0.5) % rTot) + rTot) % rTot;
            var pm = GradingGeometry.PointAtParam(rul, rcm, twm);
            double probe = System.Math.Max(2.0, widthW * 0.25);
            var qP = GradingGeometry.OutwardAt(rul, rcm, twm, probe);
            var qM = GradingGeometry.OutwardAt(rul, rcm, twm, -probe);
            bool okP = ground.TryGetElevation(qP.X, qP.Y, out double zP);
            bool okM = ground.TryGetElevation(qM.X, qM.Y, out double zM);
            if (okP && okM)
            {
                double sP = wUp ? zP - pm.Z : pm.Z - zP;    // 사면 쪽일수록 큰 값
                double sM = wUp ? zM - pm.Z : pm.Z - zM;
                sgn = sP >= sM ? 1.0 : -1.0;
                dirWhy = $"바깥 {sP:+0.0;-0.0}m / 안쪽 {sM:+0.0;-0.0}m → <b>{(sgn > 0 ? "바깥" : "안쪽")}</b>이 사면 쪽";
            }
            else dirWhy = okP ? "안쪽 땅을 못 재 <b>바깥</b>으로 간다" : "양쪽 다 못 재 <b>바깥</b>으로 간다";
        }

        // ── 구간선(안쪽) + 직각으로 민 선(바깥) → 닫는다 ──
        int nDiv = System.Math.Max(8, (int)System.Math.Ceiling(spanW /
                       System.Math.Max(0.5, System.Math.Min(p.VertexSpacing, 1.0))));
        var inner = new System.Collections.Generic.List<Point3>();
        var outer = new System.Collections.Generic.List<Point3>();
        for (int i = 0; i <= nDiv; i++)
        {
            double tw = (((wz.T0 + spanW * i / nDiv) % rTot) + rTot) % rTot;
            var qi = GradingGeometry.OutwardAt(rul, rcm, tw, 0.0);
            var qo = GradingGeometry.OutwardAt(rul, rcm, tw, widthW * sgn);
            inner.Add(new Point3(qi.X, qi.Y, zBase));
            outer.Add(new Point3(qo.X, qo.Y, zBase));
        }
        var box = new System.Collections.Generic.List<Point3>(inner);
        for (int i = outer.Count - 1; i >= 0; i--) box.Add(outer[i]);

        var sb = new System.Text.StringBuilder();
        sb.Append($"    ★<b>폴리곤 폭</b> — 선 위 최대 {(wUp ? "절토" : "성토")}고 <b>{worstCut:F2}m</b>"
            + $" ÷ 단높이 {benchH:0.##} → <b>남은 단수 {calcN}단</b>"
            + $" × (단높이 {benchH:0.##} × <b>계획 구배 1:{slopePlan:0.###}</b> + 소단 {benchWPlan:0.##} = {runPer:F2}m)"
            + $" = {calcN * runPer:F1}m + 여유 {GradingSettings.WallPolygonMargin:0.#}m"
            + $" → <b>폭 {widthW:F1}m</b>\n");
        sb.Append($"    ★<b>견줌</b> — 땅에 대고 세면 <b>{walkN}단({walkD:F1}m)</b>"
            + $"(구간 {nStepT + 1}자리 중 땅에 닿은 데 {nMeasured}"
            + (nNoGround > 0 ? $" · 선 위 땅을 못 잰 자리 {nNoGround}" : "") + ")"
            + $" → 폭이 그보다 <b>{widthW - walkD:+0.0;-0.0}m</b>"
            + (widthW > walkD ? " <b>넘어선다</b>" : " <b>⚠못 미친다</b>")
            + (toD > 0 ? $" · 옛 사면 데이라잇 {toD:F1}m 대비 {widthW - toD:+0.0;-0.0}m" : "")
            + $" · 방향: {dirWhy}\n");

        bool simpleBox = GradingGeometry.RingIsSimple(box);
        double aBox = GradingGeometry.RingAreaNts(box);
        if (box.Count >= 4 && simpleBox && aBox > 1.0)
        {
            var pl = new System.Collections.Generic.List<Point3>(box) { box[0] };
            DrawLinesOnLayer(db, tr,
                new System.Collections.Generic.List<System.Collections.Generic.List<Point3>> { pl },
                "DH-가상폴리곤", PolyAci);
            sb.Append($"    ★<b>폴리곤만 그렸다</b> — 'DH-가상폴리곤' <b>{box.Count}점 / {aBox:F1}㎡</b>"
                + $" · 길이 {spanW:F1}m(구간 [{wz.T0:F1}..{wz.T1:F1}]) × 폭 {widthW:F1}m"
                + $" · 표고 {zBase:F2}m · <b>경로에 평행</b>(직각방향으로 밀었다)"
                + " · 표면은 <b>안 만든다</b> · DHRESET이 걷어 간다\n");
        }
        else
            sb.Append($"    ⚠<b>폴리곤을 못 만들었다</b> — 점 {box.Count}개"
                + $" · 제 몸을 지르지 않는가 {(simpleBox ? "예" : "<b>아니오</b>")}"
                + $" · 넓이 {aBox:F1}㎡\n");
        return manualNote + sb.ToString();
    }

        // ★[JACK 0807 '옹벽변환이 여전히 오래 걸린다'] 어디서 시간을 쓰는지 **재고 나서** 고친다.
        //   종전엔 DoGrade 전체에 시계가 하나도 없어, 느리다는 체감만 있고 근거가 없었다.
        //   추측으로 후보를 고르면 헛짚는다(0805~0806에서 성능만 두 번 자책골) — 단계별 초를 남긴다.
        var stw = new StageTimer();

        // ══ ★★★[검토 0917 · 높음] <b>꺼내는 자리를 맨 앞으로 옮긴다.</b>
        //
        //   <para><b>주석만 고치고 코드는 안 고쳤다.</b> <c>TakeWallPolyManual</c>을 만들어 놓고
        //   부르는 자리는 <c>BuildWallBoxPolygon</c> 한 곳 그대로였다 —
        //   그 함수는 «<c>WallPolygonOnly</c>이고 옹벽 구간이 있을 때»만 불린다.
        //   그래서 주석이 말한 <b>세 가지 새는 길이 셋 다 그대로</b> 있었다:
        //   ①<c>WallPolygonOnly</c>가 꺼졌을 때 ②그려 놓고 생성을 안 돌렸을 때 ③도중에 터졌을 때.</para>
        //
        //   <para>이건 이 저장소가 스스로 정한 <b>「짐작을 사실처럼 적지 말 것」</b>에 걸리는 자리다 —
        //   주석이 "고쳤다"고 말하는데 코드는 안 고쳐져 있었다.</para>
        //
        //   <para>→ <b>DoGrade가 어느 길로 가든 반드시 지나는 이 자리</b>에서 꺼낸다.
        //   꺼내는 순간 정적은 비고, 값은 아래로 <b>인자로</b> 내려간다.</para>
        var manualPoly = GradingSettings.TakeWallPolyManual(doc.Name, out string manualWhy);

        try
        {
            System.Collections.Generic.List<Point3> boundary;
            GradingParams p;
            VirtualSlope cut, fill;
            ObjectId cutId = ObjectId.Null, fillId = ObjectId.Null;
            // [§75 → 구간 구배 0804] 구간별 구배 규칙 — 3.5단계(태그 작도)·4단계(번들 저장)에서도 쓰므로 밖에 선언.
            var cutZones = new System.Collections.Generic.List<SlopeZone>();
            var fillZones = new System.Collections.Generic.List<SlopeZone>();
            // ★[JACK 0914] 링 셈에 넣을 구간(옹벽 구간을 뺀 것)과, <b>정지면에서 떼어 둔</b> 옹벽 구간.
            //   뒤쪽(전이면·옹벽선)에서도 쓰므로 메서드 범위에 둔다.
            var cutZonesR = cutZones; var fillZonesR = fillZones;
            var wallZoneCut = new System.Collections.Generic.List<SlopeZone>();
            var wallZoneFill = new System.Collections.Generic.List<SlopeZone>();
            // [0729] 경계 표본 기반 필요 방향·계획-지반 최대 표고차(계획고 실수 감지용).
            bool needCut = false, needFill = false;
            double maxPlanGap = 0;

            // ── 1단계: 가상 절토/성토 대지표면 생성(기존 로직 그대로) ──
            stw.Stage("1단계 가상면");
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                boundary = BoundaryReader.Read(tr, planPolyId);
                if (boundary.Count < 3)
                {
                    ed.WriteMessage("\n경계 정점이 3개 미만입니다. 닫힌 폴리곤인지 확인하세요.");
                    return;
                }

                var groundTin = (TinSurface)tr.GetObject(groundId, OpenMode.ForRead);
                var ground = new CachedGroundSurface(groundTin); // 원지반 표고 캐싱(단수 계산용)
                p = BuildParams(boundary, ground);

                // [0729 — JACK 계획고 실수 감지] 경계를 따라 지반고를 표본해 '절토/성토가 필요한가'와
                //   '경계에서 지반자료가 없는가'를 기록 — 뒤에서 데이라잇이 안 나왔을 때 조용히 넘어가지 않고
                //   원인(계획고가 지형과 안 맞음/측량 밖)을 경고하기 위함.
                int nOffGround = 0, nSample = 0;
                for (int bi = 0; bi < boundary.Count; bi++)
                {
                    var a0 = boundary[bi]; var b0 = boundary[(bi + 1) % boundary.Count];
                    int div = System.Math.Max(1, (int)(System.Math.Sqrt((b0.X - a0.X) * (b0.X - a0.X) + (b0.Y - a0.Y) * (b0.Y - a0.Y)) / 5.0));
                    for (int si = 0; si < div; si++)
                    {
                        double t = (double)si / div;
                        double sx = a0.X + (b0.X - a0.X) * t, sy = a0.Y + (b0.Y - a0.Y) * t, sz = a0.Z + (b0.Z - a0.Z) * t;
                        nSample++;
                        if (!ground.TryGetElevation(sx, sy, out double gz)) { nOffGround++; continue; }
                        if (gz > sz + 0.1) needCut = true;
                        if (gz < sz - 0.1) needFill = true;
                        double gap = System.Math.Abs(gz - sz);
                        if (gap > maxPlanGap) maxPlanGap = gap;
                    }
                }

                // 정지 설정에 따라 오버사이즈 가상 절토/성토면(계단 링)을 계산 → TIN 브레이크라인으로 생성.
                // 계획고는 평면 근사가 아니라 '경계 3D 폴리선의 Z'를 그대로 추종 — 단차 계획선도 단차대로 정지(JACK).
                // [§75 구간 옹벽] 선택(WallPicks)을 계획경계 호길이 '구간'으로 변환 — 그 구간·그 단부터만 수직.
                //   같은 방향의 다른 영역(다른 성토 등)은 구간이 달라 영향 없음(JACK).
                // [사면생성 0729] DHSLOPE가 넣어둔 명시 구간(번들 구간 수정본)이 있으면 그것을 사용(진입 시 스냅샷).
                if (zoneOverride != null)
                {
                    cutZones = zoneOverride.Value.Cut;
                    fillZones = zoneOverride.Value.Fill;
                    ed.WriteMessage($"\n[사면생성 적용] 절토 구간 {cutZones.Count} · 성토 구간 {fillZones.Count}");
                }
                else
                {
                    cutZones = GradingSettings.ComputeWallZones(true, boundary);
                    fillZones = GradingSettings.ComputeWallZones(false, boundary);
                    // [옹벽 유지 0729 — JACK] 옹벽생성 재사용·같은 구역 재실행 시 번들의 기존 옹벽 구간과 병합 —
                    //   새 선택과 겹치는 기존 구간은 교체(기존 관례), 안 겹치면 둘 다 유지. '전체해제'는 병합 생략.
                    if (mode == GradeMode.RerunLast && !zoneReplaceAll && regionsPrev != null && regionsPrev.Count > 0)
                    {
                        var lastR = regionsPrev[^1];
                        int addC = cutZones.Count, addF = fillZones.Count;
                        var cumMz = GradingGeometry.CumLen2D(boundary);
                        cutZones = MergeZones(lastR.CutWallZones, cutZones, boundary, cumMz);
                        fillZones = MergeZones(lastR.FillWallZones, fillZones, boundary, cumMz);
                        if (cutZones.Count > addC || fillZones.Count > addF)
                            ed.WriteMessage($"\n[옹벽 유지] 기존 옹벽 구간 절토 {cutZones.Count - addC}·성토 {fillZones.Count - addF}개 유지(새 선택과 병합)");
                    }
                    if (cutZones.Count > 0 || fillZones.Count > 0)
                        ed.WriteMessage($"\n[옹벽 적용] 절토 구간 {cutZones.Count} · 성토 구간 {fillZones.Count} (선택 {GradingSettings.WallPicks.Count}건)");
                }

                // ══ ★★★[JACK 0914] <b>옹벽 구간을 정지면에서 뗀다</b> ═══════════════════════
                //
                //   <para>JACK: <i>"정지면_DH에 <b>옹벽변환이 반영이 안 되게</b> 하라는 말이야.
                //   순수하게 <b>옹벽변환 지표면</b>과 <b>정지면_DH</b>가 나오게 하라고."</i></para>
                //
                //   <para>종전엔 옹벽 구간이 <c>SlopeZone</c>으로 <b>링을 일그러뜨려</b> 정지면에 반영됐다.
                //   그 일그러짐이 끊김의 원인이기도 하다 — 한 링 안에서 옹벽 쪽(1.05m/단)과
                //   사면 쪽(8.5m/단)이 <b>7.45m 튀고</b>, <c>RingSegMaxM</c>(2.5m)이 그 변을 버린다
                //   (현장 로그: "링이 끊긴 자리 24곳이 전부 빈 채로 남는다").</para>
                //
                //   <para>→ <b>링 셈에서만 뺀다.</b> 번들·옹벽선 판정에는 그대로 남겨
                //   다음 실행이 구간을 기억한다. 정지면은 <b>구간 자리도 사면으로</b> 만들어지고,
                //   옹벽은 <c>가상옹벽_DH</c>로 따로 나온다.</para>
                cutZonesR = cutZones; fillZonesR = fillZones;
                if (GradingSettings.TransitionStage >= 1)
                {
                    // ★★★[JACK 0915] <b>부분 지정일 때만 뗀다.</b>
                    //   <para>JACK: <i>"옹벽변환 전체구간으로 했는데 … 구간변환만 손댄 거잖아.
                    //   <b>전체구간변환기능은 이전이랑 동일해야 해.</b>"</i></para>
                    //   <para>0914엔 이 줄에 <c>Partial</c> 검사가 없어 <b>선 전체를 옹벽으로 바꾸는</b>
                    //   원래 기능까지 실험 중인 전이면 길로 갔다 — 벽이 정지면에서 빠지고
                    //   <c>가상옹벽_DH</c>로만 따로 떠서, 쓰던 결과와 <b>다른 그림</b>이 나왔다
                    //   (현장 12:37 로그: <c>적용 구간 = 선이 덮는 구간 전체</c>인데 전이면이 돌았다).</para>
                    static bool IsWall(SlopeZone z) =>
                        z.Partial
                        && z.Rules.Count > 0 && z.Rules[0].Slope <= GradingSettings.WallGateSlope + 1e-9;
                    var cR = new System.Collections.Generic.List<SlopeZone>();
                    var fR = new System.Collections.Generic.List<SlopeZone>();
                    foreach (var z in cutZones) { if (IsWall(z)) wallZoneCut.Add(z); else cR.Add(z); }
                    foreach (var z in fillZones) { if (IsWall(z)) wallZoneFill.Add(z); else fR.Add(z); }
                    if (wallZoneCut.Count > 0 || wallZoneFill.Count > 0)
                    {
                        cutZonesR = cR; fillZonesR = fR;
                        DiagLog.Append($"\n  ★옹벽 구간을 <b>정지면에서 뗐다</b> — 절토 {wallZoneCut.Count}개"
                            + $" · 성토 {wallZoneFill.Count}개(링 셈에서 뺌 · 번들에는 남긴다)."
                            + " 정지면은 그 자리도 <b>사면</b>으로 만들어지고, 옹벽은 '가상옹벽_DH'로 따로 나온다.\n");
                        ed.WriteMessage($"\n[옹벽 분리] <b>부분 지정</b> 옹벽 구간 {wallZoneCut.Count + wallZoneFill.Count}개를"
                            .Replace("<b>", "").Replace("</b>", "")
                            + " 정지면에서 떼고 '가상옹벽_DH'로 따로 만듭니다(전체 구간 변환은 종전대로).");
                    }
                }

                // ══ ★★★[JACK 0916 <i>"폴리곤만 생성하는 건데 왜 이렇게 처리속도가 오래 걸리는 거야?"</i>]
                //   <b>맞는 말이라 재 봤다 — 폴리곤은 공짜였다.</b>
                //
                //   <para><b>실측(그 판 로그)</b>: 총 <b>16.1초</b> 중
                //   2단계 교선·경계주입 <b>10.4초/34.2GB</b> · 토량 산출 2.3초 ·
                //   1단계 가상면 1.7초(삼각형 30,760 + 57,544) · 3단계 합성 1.4초.
                //   <b>폴리곤은 따로 잡히지도 않았다.</b></para>
                //
                //   <para>옹벽 변환을 누르면 <b>정지면을 통째로 다시 만든다</b>. 옹벽이 정지면을 바꾸던
                //   시절엔 당연한 일이었는데, <b>지금은 폴리곤 한 줄만 그리므로 정지면이 바뀔 일이 없다</b> —
                //   16초가 통째로 헛일이다.</para>
                //
                //   <para>→ 여기서 <b>끝낸다</b>. 폴리곤에 필요한 것(자·구간·원지반·설정)은
                //   <b>이미 다 손에 있다</b>. 화면의 <c>정지면_DH</c>는 <b>건드리지 않으므로</b> 그대로 남는다.</para>
                // ★★★[JACK 0917 <i>"계획경계 선택하고 원지반 선택했는데 <b>계획부지 생성하기가 안 돼</b>"</i>]
                //   <b>내가 낸 회귀다.</b> <c>WallPolygonOnly</c>는 <b>전역</b>인데 여기서 그것만 보고 나갔다 —
                //   옹벽 변환뿐 아니라 <b>보통 정지 작업</b>까지 폴리곤만 그리고 끝냈다.
                //   실측(12:38 로그): <i>"⚠옹벽 구간이 없다 … 절토 구간 0 · 성토 구간 0"</i> ·
                //   <i>"걸린 시간 0.4초"</i> — <b>정지면_DH를 아예 안 만들고 나갔다</b>.
                //   → <b>옹벽 구간이 실제로 있을 때만</b> 짧게 끝낸다.
                //     구간이 없으면 여기는 <b>보통 정지 작업</b>이므로 종전 길로 그대로 간다.
                if (GradingSettings.WallPolygonOnly && (wallZoneCut.Count > 0 || wallZoneFill.Count > 0))
                {
                    SlopeZone? wzF = null; bool wUpF = true; string wSideF = ""; int nWZ = 0;
                    foreach (var (zs, upv, nm) in new[] { (wallZoneCut, true, "절토"), (wallZoneFill, false, "성토") })
                        foreach (var z in zs) { nWZ++; if (wzF == null) { wzF = z; wUpF = upv; wSideF = nm; } }
                    var sbP = new System.Text.StringBuilder();
                    sbP.Append("[DHGRADE 진단] " + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                        + "\n■ <b>폴리곤만 만든다</b>(WallPolygonOnly) — 정지면은 <b>안 건드린다</b>"
                        + "(그래서 <b>다시 만들지 않는다</b> — 0916 실측으로 그 재생성이 16.1초 중 16초였다)\n");
                    if (wzF == null)
                        sbP.Append("  ⚠옹벽 구간이 없다(수직 구배 규칙을 가진 <b>부분 지정</b> 구간 0개)"
                            + $" · 절토 구간 {cutZones.Count} · 성토 구간 {fillZones.Count}"
                            + " — 구간을 다시 고르세요.\n");
                    else
                    {
                        sbP.Append($"  ★구간 — {wSideF} · [{wzF.T0:F1}..{wzF.T1:F1}]"
                            + (nWZ > 1 ? $" (옹벽 구간 {nWZ}개 중 <b>첫 구간</b>만)" : "") + "\n");
                        try { sbP.Append(BuildWallBoxPolygon(db, tr, wzF, wUpF, p, ground, boundary, 0, manualPoly, manualWhy, groundId)); }
                        catch (System.Exception bex)
                        { sbP.Append($"  ⚠폴리곤 만들기가 <b>터졌다</b> — {bex.GetType().Name}: {bex.Message}\n"); }
                    }
                    // ★[JACK 0917] 폴리곤만 보는 판이니 <b>태그 선들은 꺼 둔다</b> —
                    //   지우지 않고 <b>표시만</b> 끄므로, 필요하면 레이어를 켜면 그대로 있다.
                    //   (이 선들은 계획부지 생성이 그리는 '옹벽 전환용 태그'다 — 나중에 쓸 것이라 안 지운다.)
                    // ★★★[JACK 0917] 어느 레이어가 <b>있었고 · 켜져 있었고 · 껐는지</b>를 낱낱이 적는다.
                    //   지난 판 로그가 «레이어 0개»만 찍어 <b>없어서 0인지 이미 꺼져서 0인지</b> 알 수가 없었다.
                    //   그 한 줄이 더 있었으면 빨간 선이 <c>DH-옹벽선</c>인 걸 바로 알았다.
                    var offSb = new System.Text.StringBuilder();
                    int offN = 0, onWas = 0, noLayer = 0;
                    foreach (var lyr in new[] { "DH-옹벽선",
                                                "DH-소단선-절토", "DH-사면선-절토",
                                                "DH-소단선-성토", "DH-사면선-성토",
                                                "DH-소단선-전환", "DH-사면선-전환" })
                    {
                        try
                        {
                            var lt0 = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
                            if (!lt0.Has(lyr)) { noLayer++; offSb.Append($" {lyr}=없음"); continue; }
                            var r0 = (LayerTableRecord)tr.GetObject(lt0[lyr], OpenMode.ForRead);
                            if (r0.IsOff) { offSb.Append($" {lyr}=이미꺼짐"); continue; }
                            onWas++;
                            if (GradingBuilder.SetLayerOff(db, tr, lyr)) { offN++; offSb.Append($" <b>{lyr}=껐다</b>"); }
                        }
                        catch (System.Exception lex0) { offSb.Append($" {lyr}=실패({lex0.GetType().Name})"); }
                    }
                    sbP.Append($"  ★<b>선 레이어 손질</b> — 켜져 있던 {onWas}개 중 <b>{offN}개를 껐다</b>"
                        + (noLayer > 0 ? $" · 없는 레이어 {noLayer}개" : "") + " ·" + offSb
                        + "\n    <b>지운 게 아니라 표시만</b> 껐다(레이어를 켜면 그대로 있다)"
                        + " · 폴리곤은 <b>청록('DH-가상폴리곤')</b>이다\n");
                    sbP.Append($"\n■ 걸린 시간 — {stw.Report()}\n");
                    tr.Commit();
                    try { DiagLog.Reset(sbP.ToString()); } catch { }
                    try { ed.WriteMessage("\n[DHGRADE] 폴리곤만 만들었습니다(정지면은 그대로). 진단 로그를 보세요."); } catch { }
                    return;
                }

                cut = GradingGeometry.Build(boundary, ground, p, up: true, cutZonesR);
                string diagCut = GradingGeometry.LastDiag;
                fill = GradingGeometry.Build(boundary, ground, p, up: false, fillZonesR);
                string diagFill = GradingGeometry.LastDiag;
                // [검증로그] 스샷 없이 분석 가능하게 실행마다 기록(JACK) — DHXSEC_진단.log와 같은 방식.
                try
                {
                    // [§75] 옹벽 적용 상태를 로그 첫머리에 — 스샷 없이 "옹벽이 적용됐는지" 바로 판별(JACK 0727).
                    string wallInfo = $"옹벽 적용: 절토 구간 {cutZones.Count} · 성토 구간 {fillZones.Count} · " +
                                      $"선택 {GradingSettings.WallPicks.Count}건";
                    DiagLog.Reset(
                        "[DHGRADE 진단] " + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") +
                        "\n■ " + wallInfo +
                        (LastBudgetNote.Length > 0 ? "\n■ " + LastBudgetNote : "") +
                        "\n\n■ 절토\n" + diagCut + "\n■ 성토\n" + diagFill);
                }
                catch { }

                string verifyCut = "", verifyFill = "";
                if (cut.HasSlope) { cutId = GradingBuilder.BuildVirtualSlope(db, tr, cut.Rings, "가상절토_DH", cut.CornerLines, groundId, cut.SeamLines); verifyCut = GradingBuilder.LastVerify; }
                if (fill.HasSlope) { fillId = GradingBuilder.BuildVirtualSlope(db, tr, fill.Rings, "가상성토_DH", fill.CornerLines, groundId, fill.SeamLines); verifyFill = GradingBuilder.LastVerify; }
                // 검증 로그에 TIN 실측 대조 결과 덧붙임(비대칭/누락 방향 추적)
                try
                {
                    DiagLog.Append(
                        "\n■ TIN 실측검증(절토)\n" + verifyCut + "\n■ TIN 실측검증(성토)\n" + verifyFill);
                }
                catch { }

                tr.Commit();
            }

            // ★★★[검토 0903 — 판별 계측] <b>여기가 두 가설을 가르는 자리다.</b>
            //
            //   <b>왜 하필 여기인가.</b> 1단계 가상면(<c>BuildVirtualSlope</c>)은 브레이크라인만 넣고
            //   <b>경계(Outer/Hide)를 안 넣는다</b>. 그리고 이 저장소 어디에도 삼각형 최대 길이 설정이 없다.
            //   그러면 Civil 3D는 점들을 <b>가장 크게 감싸는 볼록한 껍질까지</b> 삼각형으로 다 채운다.
            //
            //   옹벽을 씌우면 절토 계단이 NW 모서리에서만 120.3m 밖 → 15.7m로 확 줄어, 바깥선이
            //   <b>ㄱ자로 파인 모양</b>이 된다. 파인 자리를 껍질이 메우면 <b>바닥 데이터가 없는 가짜 삼각형</b>이
            //   깔리고, 교선(데이라잇)은 <b>그 가짜 삼각형 위에서</b> 계산된다 — 절토 교선이
            //   3985㎡ → 183㎡로 무너진 것이 이것으로 설명된다.
            //
            //   <b>예측을 미리 적어 둔다(맞히기가 아니라 판별이다).</b>
            //     메운다면 → 가상절토_DH 최장 변이 <b>100m 이상</b>, 좌표는 NW 파인 자리 안
            //     안 메운다면 → 15m 안팎으로 사면 판과 비슷
            //   재기만 하고 도면은 안 건드린다(읽기 전용).
            try
            {
                DiagLog.Append("\n■ 1단계 가상면 검사(경계 주입 전)"
                             + SurfaceEdgeScan(db, cutId, "가상절토_DH")
                             + SurfaceEdgeScan(db, fillId, "가상성토_DH") + "\n");
            }
            catch { }

            // ── 2단계: 교선 생성 → 각 가상면에 Outer 경계 주입 (성토 → 절토 순서, JACK 설계) ──
            stw.Stage("2단계 교선·경계주입");
            // DHXSEC 엔진(RawTriangleIntersectionFinder)을 그대로 호출. 초록선 그리기는 맨 마지막 한 번만 —
            // 그리기의 레이어 청소(EraseOnLayer)가 앞서 그린 성토 교선을 지우는 일이 없도록(JACK 지적).
            var allLoops = new System.Collections.Generic.List<System.Collections.Generic.List<Point3>>();
            var injectedRings = new System.Collections.Generic.Dictionary<string, (ObjectId id, System.Collections.Generic.List<Point3> ring)>();
            // 표면별 '최종' 경계 링(정규화 재주입 시 갱신) — 4단계 노리선 클립 기준(§0-HH 다음 단계)
            // ★★★[JACK 0903 "옹벽 변환했는데 지표면이 이상하게 작성되는 부분이 발생했어"]
            //   <b>계측부터.</b> 실측으로 갈린 것은 여기까지다:
            //     사면 변환 → 절토링 <b>633점</b> · 링 최장변 <b>1.00m</b> · 불일치 0
            //     옹벽 변환 → 절토링 <b>220점</b> · 링 최장변 <b>102.31m</b> · 불일치 18 · 초록선이 톱니
            //   즉 <b>점 413개가 빠지고 그 자리가 102m짜리 직선 한 변</b>이 됐다.
            //   그런데 지금 로그는 <b>끝 숫자만</b> 말한다 — 어느 단계에서 줄었는지는 안 남는다.
            //   → 링이 지나는 <b>네 자리</b>에서 같은 자를 대고 찍는다(점수 · 최장변 · 그 자리 좌표).
            //     추측하지 않고 <b>어디서</b>부터 좁힌다.
            var ringTrace = new System.Text.StringBuilder();
            void TraceRing(string where, string lab, System.Collections.Generic.List<Point3>? r)
            {
                try
                {
                    if (r == null) { ringTrace.Append($"\n    [링추적] {where} · {lab}: 없음"); return; }
                    double mx = 0; double ax = 0, ay = 0;
                    for (int i = 1; i < r.Count; i++)
                    {
                        double dx = r[i].X - r[i - 1].X, dy = r[i].Y - r[i - 1].Y;
                        double d = System.Math.Sqrt(dx * dx + dy * dy);
                        if (d > mx) { mx = d; ax = r[i - 1].X; ay = r[i - 1].Y; }
                    }
                    ringTrace.Append($"\n    [링추적] {where} · {lab}: {r.Count}점 · 최장변 {mx:F2}m @ {ax:F0},{ay:F0}");
                }
                catch { }
            }

            var finalRings = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<Point3>>();
            // [v2 번들 — 리뷰 D] 계획관련 '전체' 순수교선 링(다조각 보존) — 옹벽선 영역필터·작은 정상영역용
            var allRings = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<System.Collections.Generic.List<Point3>>>();
            string bndMsg = "", diagX = "";
            bool anyMissed = false;
            using (Transaction tr2 = db.TransactionManager.StartTransaction())
            {
                var groundTin2 = (TinSurface)tr2.GetObject(groundId, OpenMode.ForRead);

                // ── [JACK 합집합 재설계] 1) 양쪽 표면의 '순수 닫힌 교선'을 먼저 계산 ──
                //   (계획합집합·면조각 없음 — 스텝 검증으로 정확 확인된 경로)
                var groundSampler2 = new CachedGroundSurface(groundTin2);
                var pureLoops = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<System.Collections.Generic.List<Point3>>>();
                var vsIdOf = new System.Collections.Generic.Dictionary<string, ObjectId>();
                // ★★★[JACK 0903 "옹벽 변환했는데 지표면이 이상하게 작성되는 부분이 발생했어"]
                //   <paramref name="outerRing"/> = 이 사면의 <b>진짜 바깥선</b>(마지막 링).
                //   가상면에는 경계가 없어 Civil 3D가 <b>볼록껍질까지</b> 삼각형을 채우는데,
                //   둘레 일부만 옹벽이면 바깥선이 ㄱ자로 파여 그 자리가 <b>가짜 삼각형</b>으로 메워진다.
                //   교선은 그 위에서 계산되므로 절토 교선이 3985㎡ → 183㎡로 무너졌다.
                //   → <b>교선을 구하기 직전에만</b> 걸러 낸다. 도면 객체는 하나도 안 바꾼다.
                void ComputePure(ObjectId vsId, string label, System.Collections.Generic.IReadOnlyList<Point3>? outerRing)
                {
                    if (vsId.IsNull) return;
                    try
                    {
                        var vs = (TinSurface)tr2.GetObject(vsId, OpenMode.ForWrite);
                        var loops = RawTriangleIntersectionFinder.GetExactDaylight(vs, groundTin2, null, outerRing);
                        diagX += $"\n■ 교선({label})\n" + RawTriangleIntersectionFinder.LastDiag + "\n";
                        try // [리뷰 L-1] 상세 진단이 다음 호출에 덮이지 않게 표면별 사본 보존
                        {
                            System.IO.File.Copy(RawTriangleIntersectionFinder.LogPath,
                                System.IO.Path.Combine(
                                    System.IO.Path.GetDirectoryName(DiagLog.FilePath) ?? ".",
                                    $"DHXSEC_진단_{label}.log"), true);
                        }
                        catch { }
                        pureLoops[label] = loops; vsIdOf[label] = vsId;
                    }
                    catch (System.Exception ex)
                    {
                        anyMissed = true;
                        bndMsg += $"\n{label}: 교선 생성 실패 — {ex.Message}";
                    }
                }
                // 마지막 링 = 바깥 링. 이 저장소가 이미 같은 자를 쓴다(GradingBuilder.cs:112 "계단 전체").
                //   면적으로 고르지 않는 이유: 자기교차한 링은 신발끈 면적이 서로 상쇄돼 거의 0이 되어
                //   <b>안쪽 링을 바깥으로 착각</b>할 수 있다.
                // ★★[JACK 0903 "여전히 똑같은 오류가 나"] <b>링을 파일로 뽑는다.</b>
                //   껍질컷이 0.3%만 버렸다 = 자가 파인 자리까지 덮고 있다는 뜻인데,
                //   자가 잘못된 것인지 링이 애초에 안 파인 것인지는 <b>링을 직접 봐야</b> 안다.
                //   도면을 여러 번 돌리는 대신 한 번에 다 뽑아 오프라인에서 재현한다(형상 무변경).
                DumpRingsCsv("절토", cut.Rings);
                DumpRingsCsv("성토", fill.Rings);
                // ★[검토 0903] <b>HasSlope를 함께 본다.</b> 사면이 하나도 안 생기면 Rings에는
                //   <b>패드 하나만</b> 남는다(GradingGeometry가 패드를 Rings[0]으로 먼저 넣는다) —
                //   그러면 rings[^1]이 패드가 되어 <b>사면 삼각형을 전부 버린다</b>.
                ComputePure(fillId, "성토", fill.HasSlope && fill.Rings.Count > 1 ? fill.Rings[fill.Rings.Count - 1] : null);
                ComputePure(cutId, "절토", cut.HasSlope && cut.Rings.Count > 1 ? cut.Rings[cut.Rings.Count - 1] : null);

                // [0728 — JACK] 사면(데이라잇)이 원지반(측량) 경계에 닿을 정도면 경고 후 수행 중단.
                //   경계 밖 지반 정보가 없어 결과(정지면·토량)를 신뢰할 수 없음 — 계획고/구배/측량범위 조정 필요.
                var borderLoops = new System.Collections.Generic.List<System.Collections.Generic.List<Point3>>();
                try
                {
                    var bids = groundTin2.ExtractBorder(Autodesk.Civil.SurfaceExtractionSettingsType.Model);
                    foreach (ObjectId bid in bids)
                    {
                        if (tr2.GetObject(bid, OpenMode.ForWrite) is Polyline3d bp3)
                        {
                            var lp = new System.Collections.Generic.List<Point3>();
                            foreach (ObjectId vId in bp3)
                                if (tr2.GetObject(vId, OpenMode.ForRead) is PolylineVertex3d pv)
                                    lp.Add(new Point3(pv.Position.X, pv.Position.Y, pv.Position.Z));
                            if (lp.Count >= 3) borderLoops.Add(lp);
                            bp3.Erase(); // 검사용 임시 추출물 제거
                        }
                    }
                }
                catch { /* 경계 추출 실패 시 검사 생략(수행은 막지 않음) */ }

                const double BorderMargin = 2.0; // 경계 '닿음' 판정 여유(m)
                string? borderHit = null;
                bool NearBorder(System.Collections.Generic.List<Point3> loop)
                {
                    foreach (var q in loop)
                        foreach (var bl in borderLoops)
                        {
                            int nb = bl.Count;
                            for (int bi = 0; bi < nb; bi++)
                            {
                                var a = bl[bi]; var b2 = bl[(bi + 1) % nb];
                                double ex = b2.X - a.X, ey = b2.Y - a.Y, l2 = ex * ex + ey * ey;
                                double u = l2 < 1e-12 ? 0 : ((q.X - a.X) * ex + (q.Y - a.Y) * ey) / l2;
                                u = u < 0 ? 0 : (u > 1 ? 1 : u);
                                double px = a.X + ex * u, py = a.Y + ey * u;
                                double ddx = q.X - px, ddy = q.Y - py;
                                if (ddx * ddx + ddy * ddy <= BorderMargin * BorderMargin) return true;
                            }
                        }
                    return false;
                }

                // ── 2) [링 2개 분리 — JACK 확정 구조] 같은 링에 두 역할을 시키던 것이 근본 버그였음.
                //   ⓐ finalRing(초록선·번들·옹벽선용) = '순수 닫힌 교선'(전이선 지형대로 정확 — 스텝 검증).
                //   ⓑ 클립용 링(표면 자르기·합성용) = 교선 ∪ 계획 '전체'(기존 검증 방식 — 클립은 2D라 sticking 무해,
                //      pad 덮음·다조각 병합·잡루프 제외 + 자문의 GeometrySnapper·중복정점 제거·Z 역투영 반영). ──
                var clipLoopsDraw = new System.Collections.Generic.List<System.Collections.Generic.List<Point3>>(); // 클립링 시각화(하늘색)
                System.Collections.Generic.List<Point3>? Largest(System.Collections.Generic.IReadOnlyList<System.Collections.Generic.List<Point3>> rs, out double area)
                {
                    System.Collections.Generic.List<Point3>? best = null; area = 0;
                    foreach (var r in rs)
                    {
                        double a = 0;
                        for (int i = 0; i < r.Count - 1; i++) a += r[i].X * r[i + 1].Y - r[i + 1].X * r[i].Y;
                        a = System.Math.Abs(a * 0.5);
                        if (a > area) { area = a; best = r; }
                    }
                    return best;
                }
                foreach (var label in pureLoops.Keys)
                {
                    string oppL = label == "성토" ? "절토" : "성토";
                    // [JACK 목적② + 짜투리 제거] 계획과 무관한 루프·미세 조각(<5㎡)을 순수 루프에서 필터.
                    var own = RawTriangleIntersectionFinder.FilterPlanRelated(pureLoops[label], boundary, 5.0, out string fdiag);
                    diagX += $"\n■ 루프필터({label}) {fdiag}\n";
                    // [진단 0729 — 다중 구역] 순수 루프가 전부 걸러졌으면(생성 실패 직행) 원인 분석용으로
                    //   병합 교선 전체를 CSV로 덤프 — 오프라인 하니스 재현에 사용(형상 미변경, 진단 전용).
                    if (own.Count == 0 && pureLoops[label].Count > 0)
                        DumpLoopsCsv(label, pureLoops[label], boundary);
                    // [0728 — JACK] 사면이 원지반 경계에 닿으면 중단 표식(아래에서 정리 후 반환).
                    if (borderHit == null && borderLoops.Count > 0)
                        foreach (var lp in own)
                            if (NearBorder(lp)) { borderHit = label; break; }
                    if (borderHit != null) break;
                    var opp = pureLoops.TryGetValue(oppL, out var ol) ? ol
                        : new System.Collections.Generic.List<System.Collections.Generic.List<Point3>>();
                    // ⓐ finalRing = 순수 교선 최대 루프(전이선 정확) — 초록선은 필터된 순수 루프 전부 그림.
                    var pureBest = Largest(own, out double pureArea);
                    if (pureBest != null) { finalRings[label] = pureBest; allRings[label] = own; allLoops.AddRange(own); }
                    TraceRing("①교선링 뽑은 직후", label, pureBest);
                    // ★[JACK 0903] 옹벽 판에서만 10점짜리 두 번째 링이 생겼다(최장변 37.63m) — 어디서 오는지 전부 찍는다.
                    for (int oi = 0; oi < own.Count; oi++) TraceRing($"①-조각[{oi}]", label, own[oi]);
                    // ⓑ 클립용 = 교선 ∪ 계획 전체(+스냅·정제) → 표면 Outer 경계 주입.
                    var clipRings = RawTriangleIntersectionFinder.UnionLoopsWithPlan(
                        own, opp, boundary, groundSampler2, out string udiag, subtractOpposite: false);
                    diagX += $"\n■ 클립링({label}) {udiag}\n";
                    var clipBest = Largest(clipRings, out double clipArea);
                    if (clipBest != null && pureBest != null)
                    {
                        // [0728 — 계단식 산지 IllegalBoundary] 자기접촉(핀치) 링은 주입이 거부됨 →
                        //   원본 실패 시 CleanRing(5mm 정규화)으로 1회 재시도(합성 단계의 적응형 복구와 동일 원리).
                        bool injected = false;
                        var vs2 = (TinSurface)tr2.GetObject(vsIdOf[label], OpenMode.ForWrite);
                        foreach (var (ring, tag) in new[] { (clipBest, "원본"), (RawTriangleIntersectionFinder.CleanRing(clipBest), "정규화") })
                        {
                            if (ring == null) continue;
                            try
                            {
                                GradingBuilder.AddOuterBoundary(vs2, ring);
                                injectedRings[label] = (vsIdOf[label], ring);
                                clipLoopsDraw.Add(ring); // 하늘색 참고선으로 표시(JACK: 클립링 눈으로 확인)
                                bndMsg += $"\n{label}: 클립경계 주입[{tag}](∪계획 {clipArea:F0}㎡) · finalRing=순수교선 {pureArea:F0}㎡";
                                diagX += GradingBuilder.VerifyBoundaryClip(vs2, ring);
                                // ★[JACK 0904] 자른 뒤 표면이 <b>어느 둘레 칸에서</b> 경계보다 더 나갔는지 —
                                //   "11곳 이탈"만으론 옹벽 구간인지 사면 구간인지 알 수 없었다.
                                diagX += GradingBuilder.VerifySurfaceExtent(vs2, boundary, ring);
                                // ★[JACK 0904] <b>주입한 클립링 좌표를 남긴다.</b> 이음매에서 안 잘린 혀가
                                //   이 링 <b>안</b>이면 링(교선)이 틀린 것이고, <b>밖</b>이면 Civil이 안 자른 것이다 —
                                //   갈림길이 정반대라 좌표 없이는 못 고른다.
                                DumpLoopsCsv(label, new System.Collections.Generic.List<System.Collections.Generic.List<Point3>> { ring },
                                             boundary, "클립링");
                                injected = true;
                                break;
                            }
                            catch (System.Exception ex) { bndMsg += $"\n{label}: 클립경계 주입[{tag}] 실패 — {ex.Message}"; }
                        }
                        if (!injected) anyMissed = true;
                    }
                    // [팝업 오탐 0804 — JACK] 교선이 아예 없거나 전부 5㎡ 미만 짜투리 = 이 방향은 실질적으로
                    //   사면이 없는 것(예: 전체가 절토인 부지 — 성토는 몇 ㎡ 웅덩이뿐). '실패'가 아니라 '없음'이다.
                    //   종전엔 anyMissed를 켜서 매번 "⚠ 확인 필요 / 토량 산출 안 함" 팝업이 떴다(정지면은 정상 완성인데).
                    else if (own.Count == 0)
                        bndMsg += $"\n{label}: 유효 사면 없음(교선 {pureLoops[label].Count}개 전부 5㎡ 미만 짜투리) — {label} 없음으로 처리";
                    else { anyMissed = true; bndMsg += $"\n{label}: 링 생성 실패(순수 {own.Count}·클립 {clipRings.Count}) — {udiag}"; }
                }

                // [0728 — JACK] 경계 이탈 감지 → 가상면 정리 후 경고 팝업, 수행 중단.
                if (borderHit != null)
                {
                    EraseSurface(tr2, cutId);
                    EraseSurface(tr2, fillId);
                    tr2.Commit();
                    TryRestoreBase(db, baseRestoreHandle);   // [다중 구역] Append 중단 — 기준면 이름 원복
                    string wmsg = $"사면({borderHit})이 원지반(측량) 경계를 벗어납니다.\n" +
                                  "경계 밖 지반 정보가 없어 정지면을 만들 수 없습니다.\n" +
                                  "계획고·구배·측량 범위를 확인하세요.";
                    ed.WriteMessage("\n[DHGRADE 중단] " + wmsg.Replace("\n", " "));
                    try
                    {
                        DiagLog.Append(
                            $"\n■ 수행 중단 — 사면({borderHit}) 원지반 경계 이탈(여유 2m 이내 접근)\n");
                    }
                    catch { }
                    AcadApp.ShowAlertDialog(wmsg);
                    return;
                }

                // [겹침 제거 — 도넛] 성토·절토가 pad(계획 내부)를 둘 다 가지면 최종 합성의 마지막 paste가
                // SurfaceException(Failure)으로 깨짐(실측). 성토가 pad를 담당하고, 절토는 계획 내부를 Hide로
                // 뚫어 바깥 계단 띠만 남긴다 → 두 면이 전혀 안 겹쳐 합성 안정(옛 0-BB '도넛' 검증 해법).
                // [순수 절토/성토 — JACK] 성토가 실제로 있을 때만(finalRing 有) 도넛을 건다. 순수 절토면
                // 성토가 pad를 안 채우므로 절토를 뚫으면 계획부지가 구멍남(스샷). → 둘 다 실제일 때만 Hide.
                if (!cutId.IsNull && !fillId.IsNull && finalRings.ContainsKey("절토") && finalRings.ContainsKey("성토"))
                {
                    try
                    {
                        var cutTin = (TinSurface)tr2.GetObject(cutId, OpenMode.ForWrite);
                        GradingBuilder.AddHideBoundary(cutTin, boundary);
                        bndMsg += "\n절토: 계획 내부 Hide(도넛) 적용 — 성토와 겹침 제거";
                    }
                    catch (System.Exception ex) { bndMsg += $"\n절토 도넛 실패 — {ex.Message}"; }
                }

                // [JACK 0728 재원복] 정지면_DH는 원지반+절/성토 '합성면'이라 지표면 자체 경계(스타일 Boundary)는
                //   측량 전체 외곽선이지 정지경계가 아님(스샷: 부지 근처에 경계 안 보임) → 초록 정지경계선을 다시
                //   보이게 한다. 부지를 가로지르는 전이선·2m 미만 부스러기는 FilterOutsidePlan으로 걸러 표시.
                // [0805 JACK '성토 구간 안의 알 수 없는 초록선'] 정지 구역 **안쪽에 완전히 갇힌** 교선 고리는
                //   최종 지형의 경계가 아니다(그 둔덕은 어차피 깎여 계획면이 된다) → 그리지 않는다.
                //   경계로 실제 쓰이는 건 클립링 1개인데 표시 경로가 걸러진 고리를 전부 그려 온 것이 원인.
                System.Collections.Generic.IReadOnlyList<System.Collections.Generic.IReadOnlyList<Point3>>
                    drawLoops = FilterOutsidePlan(allLoops, boundary, PlanNearM);
                int loopDropped = 0;
                string loopDiag = "";
                // [안전 0805] 표시용 필터가 지표면 트랜잭션을 깨면 안 된다 — 실패하면 원래대로 전부 그린다.
                try
                {
                    foreach (var kv in injectedRings)
                    {
                        // 여유 0.3m — 진짜 경계선은 클립링과 겹쳐 0.0m로 찍히고, 갇힌 섬은 0.8m처럼 뚜렷이
                        //   떨어져 나온다(현장 로그 0805 10:55 실측). 종전 1.0m는 0.8m짜리를 놓쳤다.
                        drawLoops = GradingPolygons.DropLoopsInsideClip(drawLoops, kv.Value.ring, 0.3, out int dn);
                        loopDropped += dn;
                        loopDiag += $"\n  vs {kv.Key} 클립링:{GradingPolygons.LastDropDiag}";
                    }
                    bndMsg += $"\n정지경계 표시: 고리 {drawLoops.Count + loopDropped}개 중 갇힌 것 {loopDropped}개 제외(표시 전용 — 기하·토량 무관)" + loopDiag;
                }
                catch (System.Exception ex)
                {
                    drawLoops = FilterOutsidePlan(allLoops, boundary, PlanNearM);   // 폴백: 종전대로 전부 표시
                    bndMsg += $"\n정지경계 표시: 갇힌 고리 판정 실패 — 전부 표시(표시 전용, 지표면 무관) — {ex.Message}";
                }
                // ★★[v30.0 · JACK 0812] <b>"이어서 작성하면 가장 최근 것의 데이라잇 경계만 나온다 —
                //   최초 시점부터의 경계가 나와야 하고, 그 모든 과정에 대한 종단이 나와야 한다."</b>
                //
                //   <b>원인.</b> <see cref="GradingBuilder.DrawDaylight"/>는 그리기 전에
                //   <c>EraseOnLayer</c>로 <b>레이어를 통째로 지운다</b>. 재실행할 때 겹겹이 쌓이는 것을
                //   막으려던 것인데, '이어서(누적)' 모드에서는 <b>앞 구역의 경계까지 지워 버린다</b>.
                //   정지면 자체는 누적 합성면이라 형상은 다 들어 있는데, <b>경계선만 최신 구역 것만</b> 남았다.
                //
                //   <b>처방.</b> 지우는 것은 그대로 두고, <b>앞 구역의 데이라잇을 함께 넘긴다.</b>
                //   번들에 구역이 전부 누적돼 있으므로 근거는 이미 있다(<c>regionsPrev</c>).
                //   다만 <b>뒤 구역이 덮은 자리는 빼야</b> 최종 지형과 맞는다 —
                //   옹벽선이 이미 쓰는 방식(<see cref="GradingBundle.LaterFootprints"/> + 마스크)을 그대로 쓴다.
                if (regionsPrev != null && regionsPrev.Count > 0)
                {
                    try
                    {
                        // 이번 구역이 덮은 자리(클립링 + 계획 폴리곤) — 앞 구역들은 여기서 잘려야 한다.
                        var mineNow = new System.Collections.Generic.List<System.Collections.Generic.List<Point3>>();
                        foreach (var kv in injectedRings)
                            if (kv.Value.ring is { Count: >= 3 }) mineNow.Add(kv.Value.ring);
                        if (boundary is { Count: >= 3 }) mineNow.Add(boundary);

                        var kept = new System.Collections.Generic.List<System.Collections.Generic.IReadOnlyList<Point3>>();
                        int nPrevRing = 0, nPiece = 0, nNoMask = 0;
                        var maskDiag = new System.Text.StringBuilder();
                        for (int ri = 0; ri < regionsPrev.Count; ri++)
                        {
                            // 이 구역보다 <b>뒤</b>에 온 것 = 앞 구역들 중 나중 것 + 이번 구역
                            var later = GradingBundle.LaterFootprints(regionsPrev, ri);
                            later.AddRange(mineNow);
                            var mask = GradingPolygons.RegionMask.Build(later);

                            // ★★[v32.4 · JACK 0812] <b>마스크가 없으면 앞 구역 선이 통째로 살아난다 — 그게 '파고드는 선'이다.</b>
                            //   실측 로그: `링 3개 → 3조각` — <b>하나도 안 잘렸다</b>. 옛 번들(v8 미만)에는
                            //   클립링이 없어 <c>LaterFootprints</c>가 비고, 마스크가 <c>null</c>이 되어
                            //   <b>지금 정지면 안쪽에 묻힌 옛 경계선까지 그대로</b> 그려진다.
                            //   이제 <b>몇 조각이 어떻게 잘렸는지</b>를 구역별로 남긴다 — 숫자가 같으면 안 잘린 것이다.
                            if (mask == null) nNoMask++;
                            maskDiag.Append($"\n  구역{ri + 1}: 덮개 {later.Count}개 → 마스크 "
                                          + (mask == null ? "없음(⚠앞 구역 선이 안 잘린다 — 옛 번들에 클립링이 없다)"
                                                          : $"조각 {mask.PieceCount}개"));
                            foreach (var r in DaylightRingsOf(regionsPrev[ri]))
                            {
                                nPrevRing++;
                                if (mask == null) { kept.Add(r); nPiece++; continue; }
                                foreach (var piece in TrimOutsideMask(r, mask)) { kept.Add(piece); nPiece++; }
                            }
                        }
                        bndMsg += maskDiag.ToString();
                        if (kept.Count > 0)
                        {
                            kept.AddRange(drawLoops);
                            drawLoops = kept;
                            bndMsg += $"\n앞 구역 데이라잇 복원: 구역 {regionsPrev.Count}개 · 링 {nPrevRing}개 → " +
                                      $"덮인 부분 제외하고 {nPiece}조각 함께 그림(최초 구역부터 경계가 남는다)";
                        }
                        else bndMsg += $"\n앞 구역 데이라잇 복원: 남을 조각이 없다(앞 구역이 전부 덮였거나 링이 비었다)";
                    }
                    catch (System.Exception ex)
                    { bndMsg += "\n앞 구역 데이라잇 복원 실패 — " + ex.Message; }
                }
                GradingBuilder.DrawDaylight(db, tr2, drawLoops, "DH-정지경계", 3, layerOff: false);
                // ★[v32.4] <b>계수기를 달아 놓고 출력을 안 했다.</b> `가시 제거 N점`이 여태 로그에 한 번도
                //   안 찍혀서, 가시가 남았을 때 <b>못 잡은 건지 안 돈 건지</b>를 가릴 수가 없었다.
                bndMsg += "\n정지경계 작도: " + GradingBuilder.LastDaylightDiag;
                GradingBuilder.DrawDaylight(db, tr2, clipLoopsDraw, "DH-클립경계", 4, layerOff: true); // 하늘색=클립링(∪계획)
                bndMsg += "\n클립경계 작도: " + GradingBuilder.LastDaylightDiag;
                // 과거 진단선(빨강/하늘) 잔재 청소 — 오류로 오인 방지(JACK)
                GradingBuilder.DrawDebugSpans(db, tr2, System.Array.Empty<(Point3, Point3)>());
                GradingBuilder.DrawDebugSpans(db, tr2, System.Array.Empty<(Point3, Point3)>(), "DH-틈메움", 4);
                tr2.Commit();
            }
            try
            {
                DiagLog.Append(
                    diagX + "\n■ 경계 주입" + bndMsg + "\n");
            }
            catch { }

            // [링 2개 구조 — 전체 파이프라인 복원] 클립링으로 표면 클립·합성, finalRing(순수교선)은 번들·초록선용.
            // ── 3단계: 최종 합성(원지반 → 성토 → 절토 순 Paste) — 병합 느낌표의 실제 원인을 로그로 특정(JACK) ──
            stw.Stage("3단계 합성(Paste)");
            string pasteLog = "";
            ObjectId finalSurfId = ObjectId.Null;
            // ★★★[검토 0914 · 높음] <b>2단계가 정말 됐는지를 깃발로 들고 나간다.</b>
            //   여기서 예외가 나면 <c>trW2</c>가 되물려지고, 지웠던 <b>지난 실행의 순수옹벽_DH가
            //   되살아난다</b>. 그런데 뒤쪽 가시성 코드는 <b>이름으로</b> 찾아 켜므로,
            //   JACK 화면엔 <b>옛 결과가 새 결과인 양</b> 보인다 — 화면만으론 구별할 길이 없다.
            //   → 됐을 때만 그 이름을 켠다.
            bool pureWallOk = false;
            try
            {
                using Transaction tr3 = db.TransactionManager.StartTransaction();
                // [절토/성토 한쪽만 있는 경우 — JACK] 순수 절토(또는 성토) 부지는 반대쪽 표면이 지반과 안 만나
                // daylight(경계)가 안 생김 → 오버사이즈 표면이 클립 없이 억지로 합성돼 줄무늬 오류(스샷3·4).
                // 유효 경계(finalRing)가 주입된 표면만 합성하고, 없는 쪽 가상표면은 지운다.
                // [0729 — 조용한 실패 방지] 그 방향이 '필요'한 부지(경계 표본에서 지반이 계획고보다 낮/높음)인데
                //   데이라잇이 안 나왔으면 순수 부지로 오판하지 말고 중단+경고 — 대표 원인: 계획 폴리곤 고도(Z)
                //   미입력/지형 불일치, 사면이 측량 밖(JACK 실측 0729: Z=101 vs 지반 65~90 → 조용히 빈 결과).
                string? missNeeded = null;
                if (!fillId.IsNull && !finalRings.ContainsKey("성토") && needFill) missNeeded = "성토";
                else if (!cutId.IsNull && !finalRings.ContainsKey("절토") && needCut) missNeeded = "절토";
                if (missNeeded != null)
                {
                    EraseSurface(tr3, cutId);
                    EraseSurface(tr3, fillId);
                    tr3.Commit();
                    TryRestoreBase(db, baseRestoreHandle);
                    string wmsg2 = $"{missNeeded} 사면이 필요한 부지인데(경계에서 계획고-지반고 차 최대 {maxPlanGap:F1}m) " +
                                   $"{missNeeded} 데이라잇(사면과 지반이 만나는 선)을 찾지 못해 중단합니다.\n\n" +
                                   "① 계획 폴리곤의 고도(Z)가 지형과 맞는지\n" +
                                   "② 사면이 측량(원지반) 범위를 벗어나지 않는지 확인하세요.";
                    ed.WriteMessage("\n[DHGRADE 중단] " + wmsg2.Replace("\n", " "));
                    DiagLog.Append($"\n■ 수행 중단 — {missNeeded} 필요(경계 계획-지반 표고차 최대 {maxPlanGap:F1}m)인데 데이라잇 없음(계획고/측량범위 확인)\n");
                    AcadApp.ShowAlertDialog(wmsg2);
                    return;
                }
                if (!fillId.IsNull && !finalRings.ContainsKey("성토")) { EraseSurface(tr3, fillId); fillId = ObjectId.Null; bndMsg += "\n성토: daylight 없음 — 순수 절토 부지로 판단, 성토 가상면 제거"; }
                if (!cutId.IsNull && !finalRings.ContainsKey("절토")) { EraseSurface(tr3, cutId); cutId = ObjectId.Null; bndMsg += "\n절토: daylight 없음 — 순수 성토 부지로 판단, 절토 가상면 제거"; }

                // [적응형 합성] 실측 확정: 표면마다 paste가 받아주는 링이 다름(성토=원본 OK/정규화 실패,
                // 절토=원본 실패/정규화 OK — NTS 검사로는 구분 불가). → paste 결과로 판단해 실패한 표면만
                // 경계를 5mm 정규화 링으로 교체하고 재시도(표면당 1회).
                var order = new System.Collections.Generic.List<(ObjectId, string)> { (groundId, "원지반") };
                if (!fillId.IsNull) order.Add((fillId, "성토"));
                if (!cutId.IsNull) order.Add((cutId, "절토"));

                // ══ ★★★[JACK 0914] <b>전이면 — 넉넉히 짓고 두 번 잘라, 원지반과 함께 합성</b> ══════
                //
                //   <para>JACK이 준 순서 그대로다: <i>"구간 변환을 고른 직선을 기준으로 해당 높이에서
                //   가상 폴리곤을 생성하고 원지반의 높이를 계산해서 <b>그만큼 넉넉히 일단 옹벽을 만들어</b>.
                //   그다음에 그 지표면과 원지반이 만나는 데이라잇을 만들고 <b>그걸로 잘라</b>.
                //   그리고 사면이 만들어진 정지dh와도 데이라잇을 만들어서 <b>그걸로 또 잘라</b>.
                //   그다음에 정지dh, …원지반에 의해 잘린 지표면객체, 그리고 원지반에서 가상지표면에 의해
                //   잘린 원지반면 이 <b>세 개를 합성</b>해야"</i></para>
                //
                //   <para>★<b>내가 앞서 두 번 틀린 자리.</b> ①계획폴리곤을 정지 엔진에 넘겼더니
                //   사방으로 버퍼돼 <b>부지 쪽으로 자랐다</b>(실측 1196점·29.8m). ②원지반을 <b>다시 떠서</b>
                //   덮는 면을 만들었더니 옆 사면을 건드렸다. JACK: <i>"자꾸 지표면을 만들어내는 방향으로
                //   잡은 거 같은데 이 방법은 버리라고 한 것 같은데? 어떻게든 <b>원지반을 살려서</b>…"</i>
                //   — 원지반은 <b>객체 그대로 잘라</b> 쓰는 것이다.</para>
                //
                //   <para>★이 방식은 <b>새로 발명한 것이 아니다</b> — 이 파일이 사면에 이미 쓰는 그 방식이다
                //   (가상면을 짓고 <c>GetExactDaylight</c>로 교선을 얻어 그 링으로 자른다, 위 <c>ComputePure</c>).
                //   옹벽에도 같은 것을 하고, 원지반 조각을 한 장 더 넣을 뿐이다.</para>
                ObjectId wallSlabId = ObjectId.Null, groundPieceId = ObjectId.Null;
                // ★[JACK 0914] 쐐기·발자국은 <b>합성 뒤</b>에도 쓴다(뚜껑을 자를 칼) — 밖에 둔다.
                System.Collections.Generic.List<Point3>? wedgePolyKeep = null, wallPolyKeep = null;
                // ★★[JACK 0916] <b>절토인지 성토인지</b>도 들고 나간다 — 벽을 어느 쪽에서 잘라야 하는지가
                //   그것으로 갈린다(아래 ④의 <c>inEarth</c>).
                bool wallUpKeep = true;
                // ★★★[JACK 0916] <b>줄(row)</b>을 2단계로 들고 나간다 — 새 로직의 재료다.
                System.Collections.Generic.List<System.Collections.Generic.List<Point3>>? slabKeep = null;
                // ★[JACK 0916] 자·구간도 들고 나간다 — 뚜껑 바깥 변을 직각선으로 찾는 데 쓴다.
                System.Collections.Generic.List<Point3>? rulKeep = null; double[]? rcmKeep = null;
                double wzT0 = 0, wzSpan = 0, wzFar = 0;
                // ★★[JACK 0916 스샷] 정지 <b>데이라잇 링</b>도 들고 나간다 — 쐐기의 바깥 변은
                //   표본으로 흉내 낸 선이라 진짜 곡선과 어긋난다. 마지막에 이 링으로 한 번 더 자른다.
                System.Collections.Generic.List<Point3>? dayRingKeep = null;
                int tStage = GradingSettings.TransitionStage;
                if (tStage <= 0)
                    DiagLog.Append("\n  전이면 — <b>꺼져 있다</b>(TransitionStage=0)\n");
                else
                try
                {
                    var gTin3 = (TinSurface)tr3.GetObject(groundId, OpenMode.ForRead);
                    var gSam3 = new CachedGroundSurface(gTin3);

                    // 옹벽 구간 하나 고른다 — 수직 구배 규칙을 가진 첫 구간.
                    //   ★여러 개는 <b>다음 걸음</b>이다. 지금은 하나만 하고 그 사실을 로그에 적는다.
                    // ★위에서 <b>정지면에서 떼어 둔</b> 바로 그 구간을 쓴다 — 두 번 고르지 않는다.
                    SlopeZone? wz = null; bool wUp = true; string wSide = ""; int wallZoneN = 0;
                    foreach (var (zs, upv, nm2) in new[] { (wallZoneCut, true, "절토"), (wallZoneFill, false, "성토") })
                        foreach (var z in zs)
                        {
                            wallZoneN++;
                            if (wz == null) { wz = z; wUp = upv; wSide = nm2; }
                        }

                    if (wz == null)
                        DiagLog.Append("\n  전이면 — 옹벽 구간이 없다(수직 구배 규칙을 가진 구간 0개)\n");
                    else
                    {
                        var rul = wz.Ref ?? boundary;
                        var rcm = wz.RefCum ?? GradingGeometry.CumLen2D(rul);
                        double rTot = rcm[rcm.Length - 1];
                        int fb = wz.FirstBench == int.MaxValue ? 1 : wz.FirstBench;
                        var (slopeW, benchWW) = wz.At(fb, wUp ? p.CutSlope : p.FillSlope, p.BenchWidthOf(wUp));
                        if (benchWW < 0) benchWW = p.BenchWidthOf(wUp);

                        // ── 옛 사면 데이라잇까지의 거리 — 구간 범위 안에서 가장 먼 자리
                        double toD = 0;
                        if (finalRings.TryGetValue(wSide, out var dayRing) && dayRing != null)
                            foreach (var q in dayRing)
                            {
                                double t = GradingGeometry.ParamAt(rul, rcm, q.X, q.Y);
                                double rel = t - wz.T0;
                                if (rel < -rTot * 0.5) rel += rTot;
                                if (rel > rTot * 0.5) rel -= rTot;
                                double sp = wz.T1 >= wz.T0 ? wz.T1 - wz.T0 : rTot - wz.T0 + wz.T1;
                                if (rel < -1.0 || rel > sp + 1.0) continue;      // 구간 밖 데이라잇은 안 본다
                                var b3 = GradingGeometry.PointAtParam(rul, rcm, t);
                                toD = System.Math.Max(toD,
                                    System.Math.Sqrt((q.X-b3.X)*(q.X-b3.X) + (q.Y-b3.Y)*(q.Y-b3.Y)));
                            }

                        // ── 벽이 원지반에 닿는 거리(안쪽 경계)
                        double fromD = GradingGeometry.WallDaylightDist(rul, rcm, wz.T0, wz.T1,
                                           gSam3, p, wUp, slopeW, benchWW, out string ddiag);

                        double zBase = GradingGeometry.PointAtParam(rul, rcm, wz.T0).Z;

                        DiagLog.Append($"\n  ★전이면 재료 — 옹벽 구간 {wallZoneN}개 중 <b>첫 구간</b>만 처리"
                            + (wallZoneN > 1 ? " (<b>나머지는 다음 걸음</b>)" : "")
                            + $" · {wSide} · 구간 [{wz.T0:F1}..{wz.T1:F1}] · 자 {rul.Count}점(둘레 {rTot:F1}m)"
                            + $" · 구배 1:{slopeW:0.###} · 소단 {benchWW:0.##}m · 찍은 선 표고 {zBase:F2}m"
                            + $"\n    {ddiag}"
                            + $"\n    옛 사면 데이라잇까지 {toD:F2}m\n");

                        // ── ① <b>구간 + 양 끝 직각선 + 데이라잇</b>으로 폴리곤 → 그 <b>안쪽으로</b> 옹벽
                        //   JACK: <i>"구간을 설정하면 그 선에서 <b>직각방향으로 선을 그리고</b>
                        //   <b>데이라잇과 만나는</b> 폴리곤을 만들고, 구간선과 직각방향선 3방향으로
                        //   <b>폴리곤 안쪽으로</b> 옹벽이 해당 폴리곤 구간 안의 최대 원지반높이를
                        //   포함한 높이의 단이 포함되게 치는 거야."</i>
                        //   <para>★<b>좌우로 안 늘린다.</b> 앞 판은 구간을 양옆으로 13.9m씩 늘려
                        //   <b>날개가 생겼다</b> — JACK: <i>"왜 좌우측에 날개가 생겼지 반대로 생겨야지"</i>.</para>
                        //   <para>★폴리곤은 <b>평면 기준</b>이다(JACK) — 직각선이 데이라잇과 만나는 자리를
                        //   위에서 본 거리(<c>toD</c>)로 잡는다.</para>
                        // ★★★[검토 0914 · 치명] <b>데이라잇 거리를 자리마다</b> 넘긴다.
                        //   종전엔 구간 안 <b>최댓값 하나</b>(toD)로 쐐기를 팠다. 실측으로 데이라잇은
                        //   구간 안에서 <b>10m나 오르내려</b>, 최댓값이 난 한 곳에서만 입구가 데이라잇에
                        //   앉고 반대쪽은 <b>8.17m 떠 있었다</b> → 뚜껑 링이 원리적으로 안 닫힌다.
                        System.Func<double, double>? farAt = null;
                        {
                            // 정지면 데이라잇 링을 자 위 둘레값으로 표본해 둔다(빈 칸은 이웃으로 메운다)
                            int NS = 128;
                            var samp = new double[NS + 1];
                            for (int i = 0; i <= NS; i++) samp[i] = double.NaN;
                            double spN = wz.T1 >= wz.T0 ? wz.T1 - wz.T0 : rTot - wz.T0 + wz.T1;
                            if (finalRings.TryGetValue(wSide, out var dr2) && dr2 != null)
                                foreach (var q in dr2)
                                {
                                    double tq = GradingGeometry.ParamAt(rul, rcm, q.X, q.Y);
                                    double rel = tq - wz.T0;
                                    if (rel < -rTot * 0.5) rel += rTot;
                                    if (rel > rTot * 0.5) rel -= rTot;
                                    if (rel < -0.5 || rel > spN + 0.5) continue;
                                    int k = (int)System.Math.Round(System.Math.Max(0, System.Math.Min(1, rel / spN)) * NS);
                                    var b5 = GradingGeometry.PointAtParam(rul, rcm, tq);
                                    double dq = System.Math.Sqrt((q.X-b5.X)*(q.X-b5.X) + (q.Y-b5.Y)*(q.Y-b5.Y));
                                    if (double.IsNaN(samp[k]) || dq > samp[k]) samp[k] = dq;
                                }
                            int got = 0; foreach (var v in samp) if (!double.IsNaN(v)) got++;
                            if (got >= 3)
                            {
                                // 빈 칸 메우기 — 가장 가까운 값
                                for (int i = 0; i <= NS; i++)
                                    if (double.IsNaN(samp[i]))
                                        for (int d2 = 1; d2 <= NS; d2++)
                                        {
                                            if (i - d2 >= 0 && !double.IsNaN(samp[i - d2])) { samp[i] = samp[i - d2]; break; }
                                            if (i + d2 <= NS && !double.IsNaN(samp[i + d2])) { samp[i] = samp[i + d2]; break; }
                                        }
                                double lo2 = double.MaxValue, hi2 = 0;
                                foreach (var v in samp) { lo2 = System.Math.Min(lo2, v); hi2 = System.Math.Max(hi2, v); }
                                DiagLog.Append($"    ①-f 데이라잇 거리 <b>자리마다</b> — 표본 {NS + 1}칸 중 {got}칸에서 잼"
                                    + $" · {lo2:F2} ~ {hi2:F2}m(차 {hi2 - lo2:F2}m · 종전엔 최댓값 {toD:F2}m 하나만 썼다)\n");
                                farAt = tq =>
                                {
                                    double rel = tq - wz.T0;
                                    if (rel < -rTot * 0.5) rel += rTot;
                                    if (rel > rTot * 0.5) rel -= rTot;
                                    double u = System.Math.Max(0, System.Math.Min(1, rel / spN));
                                    return samp[(int)System.Math.Round(u * NS)];
                                };
                            }
                            else DiagLog.Append($"    ①-f 데이라잇 거리 — 표본이 {got}칸뿐이라"
                                + $" <b>한 값 {toD:F2}m</b>로 간다(자리마다 못 쓴다)\n");
                        }
                        var slab = GradingGeometry.WallInWedge(rul, rcm, wz.T0, wz.T1,
                                       farAt: farAt, daylightDist: toD,
                                       ground: gSam3, p: p, up: wUp, slope: slopeW, benchW: benchWW,
                                       polygon: out var wallPoly, wedgePolygon: out var wedgePoly,
                                       log: out string slog);
                        wedgePolyKeep = wedgePoly; wallPolyKeep = wallPoly; wallUpKeep = wUp; slabKeep = slab;
                        rulKeep = new System.Collections.Generic.List<Point3>(rul); rcmKeep = rcm;
                        wzT0 = wz.T0; wzSpan = wz.T1 >= wz.T0 ? wz.T1 - wz.T0 : rTot - wz.T0 + wz.T1;
                        wzFar = toD;
                        if (finalRings.TryGetValue(wSide, out var dayR0) && dayR0 != null && dayR0.Count >= 3)
                            dayRingKeep = new System.Collections.Generic.List<Point3>(dayR0);
                        DiagLog.Append($"    ①옹벽면 — {slog}\n");

                        // ══ ★★★[JACK 0916 <i>"누르면 폴리곤만 생성되는 것까지만"</i>] ═══════════
                        //   표면을 <b>하나도 안 만들고</b> 쐐기 폴리곤 한 줄만 그린다.
                        //   빨간 미리보기(화살표·경로)는 이 위에서 이미 끝난 일이라 그대로 남는다.
                        if (GradingSettings.WallPolygonOnly)
                        {
                            // ══ ★★★[JACK 0916] <b>폴리곤은 이렇게 만든다.</b>
                            //
                            //   <para><b>길이</b>는 사용자가 고른 경로 그대로 — 구간 [T0..T1].
                            //   <b>폭</b>은 계획지표면에서 「그 선에 <b>남은 단수</b> × 구배」로 거리를 구하고
                            //   거기에 <b>여유 10m</b>를 더한다. 그 폭만큼 <b>경로에 평행한 선</b>을 만들되,
                            //   미는 방향은 <b>경로의 직각방향</b>이다.</para>
                            //
                            //   <code>
                            //     한 단이 평면에서 가는 거리 = 단높이 × 구배 + 소단폭
                            //     폭 = 남은 단수 × 그 거리 + 여유 10m
                            //   </code>
                            //
                            //   <para><b>남은 단수</b>는 땅에 대고 센다 — 찍은 선에서 한 단씩 나가며
                            //   원지반을 만나는 데까지. 구간을 따라 여러 자리에서 세어 <b>가장 많은 데</b>를 쓴다
                            //   (한 곳만 재면 나머지가 폴리곤 밖으로 나간다 — 전에 데이라잇이 구간 안에서
                             //   <b>10m나 오르내리는</b> 것을 이미 쟀다).</para>
                            // ── ★<b>폭</b> — JACK이 정한 식 그대로 ────────────────────────────
                            //   <code>
                            //     남은 단수 = |선 위 <b>지반고</b> − 선의 <b>계획고</b>| ÷ 단높이   (올림)
                            //     한 단이 평면에서 가는 거리 = 단높이 × 구배 + 소단폭
                            //     폭 = 남은 단수 × 그 거리 + 여유 10m
                            //   </code>
                            //   <para>구간을 따라 여러 자리에서 재고 <b>가장 큰 값 하나</b>를 쓴다 —
                            //   JACK: <i>"그 값을 <b>양 끝</b> 직각방향 선에 적용"</i>. 자리마다 다른 폭을 주면
                            //   양 끝이 어긋나고, 작은 쪽을 쓰면 그 자리에서 데이라잇이 폴리곤 밖으로 나간다.</para>
                            //
                            //   <para>★<b>땅에 대고 센 값도 나란히 적는다.</b> 계산식은 선 <b>위</b> 한 점만 보므로,
                            //   바깥으로 갈수록 땅이 더 오르면 <b>모자랄 수 있다</b>. 둘을 같이 찍어 두면
                            //   여유 10m로 되는지 안 되는지가 <b>숫자로</b> 보인다.</para>
                            // ★★★[JACK 0916 스샷 <i>"딱 봐도 빨간선이 더 작지? 데이라잇보다도 더 왼쪽으로 10m 더 나가야 되는데"</i>]
                            //   <b>구배를 잘못 썼다 — 옹벽 구배를 넣었다.</b>
                            //
                            //   <para><b>그 판 로그 그대로</b>: <i>"구배 1:<b>0.01</b> · 남은 단수 4단 ×
                            //   (단높이 5 × 0.01 + 소단 1) = <b>4.2m</b> + 여유 10m → 폭 <b>14.2m</b>"</i>.
                            //   그런데 같은 로그에 <i>"옛 사면 데이라잇까지 <b>27.83m</b>"</i>라고 적혀 있다 —
                            //   폴리곤이 데이라잇의 <b>절반</b>에서 끝났다.</para>
                            //
                            //   <para><c>slopeW</c>는 <b>이 구간의 옹벽 구배</b>(1:0.01 = 수직)다.
                            //   JACK이 말한 것은 <b>계획지표면의 구배</b>, 곧 <b>인공사면</b>이 서는 구배(1:1.5)다.
                            //   폭은 「벽이 얼마나 가느냐」가 아니라 「<b>사면이었다면</b> 얼마나 갔겠느냐」이므로
                            //   여기서는 <b>전역 구배</b>를 써야 한다.</para>
                            //
                            //   <code>
                            //     틀린 값: 4단 × (5 × <b>0.01</b> + 1) =  4.2m + 10 = 14.2m   ← 데이라잇 27.83m에 한참 못 미친다
                            //     맞는 값: 4단 × (5 × <b>1.5 </b> + 1) = 34.0m + 10 = 44.0m   ← 데이라잇을 16m 넘어선다
                            //   </code>
                            double benchH = p.BenchHeightOf(wUp);
                            double slopePlan = System.Math.Max(wUp ? p.CutSlope : p.FillSlope, p.MinSlope);
                            double benchWPlan = p.BenchWidthOf(wUp);
                            double runPer = benchH * slopePlan + benchWPlan;  // 한 단이 평면에서 가는 거리
                            int nStepT = 64, maxBench = 80;
                            double spanW = wz.T1 >= wz.T0 ? wz.T1 - wz.T0 : rTot - wz.T0 + wz.T1;
                            int calcN = 0, walkN = 0, nMeasured = 0, nNoGround = 0;
                            double worstCut = 0;
                            for (int i4 = 0; i4 <= nStepT; i4++)
                            {
                                double t4 = wz.T0 + spanW * i4 / nStepT;
                                double tw4 = ((t4 % rTot) + rTot) % rTot;
                                var pOn = GradingGeometry.PointAtParam(rul, rcm, tw4);

                                // ① JACK 식 — 선 위 지반고와 계획고의 차
                                if (gSam3.TryGetElevation(pOn.X, pOn.Y, out double zgOn))
                                {
                                    double cutH = wUp ? zgOn - pOn.Z : pOn.Z - zgOn;  // 절토면 땅이 위, 성토면 아래
                                    if (cutH > worstCut) worstCut = cutH;
                                    int nc = (int)System.Math.Ceiling(System.Math.Max(0, cutH) / System.Math.Max(0.01, benchH));
                                    if (nc > calcN) calcN = nc;
                                }
                                else nNoGround++;

                                // ② 견줌 — 땅에 대고 한 단씩 나가며 세 본다(쓰지는 않는다)
                                double zCur = pOn.Z, dCur = 0; int nb = 0; bool met = false;
                                for (; nb < maxBench; nb++)
                                {
                                    dCur += benchH * slopePlan; zCur += wUp ? benchH : -benchH;
                                    var q4 = GradingGeometry.OutwardAt(rul, rcm, tw4, dCur);
                                    if (gSam3.TryGetElevation(q4.X, q4.Y, out double zg4))
                                    { if (wUp ? zg4 <= zCur : zg4 >= zCur) { nb++; met = true; break; } }
                                    dCur += benchWPlan;
                                }
                                if (met) nMeasured++;
                                if (nb > walkN) walkN = nb;
                            }
                            if (calcN <= 0) calcN = 1;
                            double widthW = calcN * runPer + GradingSettings.WallPolygonMargin;

                            // ── ★<b>방향이 맞나</b> — 인공사면 쪽으로 갔는지 <b>재서</b> 확인한다 ──
                            //   <para>바깥(<c>OutwardAt</c>)이 늘 사면 쪽이라고 <b>가정하지 않는다</b>.
                            //   구간 가운데에서 양쪽으로 나가 보고, <b>절토면 땅이 더 높은 쪽</b>(성토면 더 낮은 쪽)이
                            //   사면 쪽이다. 어긋나면 <b>뒤집고 로그에 적는다</b>.</para>
                            double sgn = 1.0; string dirWhy = "확인 못 함";
                            {
                                double tm = wz.T0 + spanW * 0.5;
                                double twm = ((tm % rTot) + rTot) % rTot;
                                var pm = GradingGeometry.PointAtParam(rul, rcm, twm);
                                double probe = System.Math.Max(2.0, widthW * 0.25);
                                var qP = GradingGeometry.OutwardAt(rul, rcm, twm, probe);
                                var qM = GradingGeometry.OutwardAt(rul, rcm, twm, -probe);
                                bool okP = gSam3.TryGetElevation(qP.X, qP.Y, out double zP);
                                bool okM = gSam3.TryGetElevation(qM.X, qM.Y, out double zM);
                                if (okP && okM)
                                {
                                    double sP = wUp ? zP - pm.Z : pm.Z - zP;   // 사면 쪽일수록 큰 값
                                    double sM = wUp ? zM - pm.Z : pm.Z - zM;
                                    sgn = sP >= sM ? 1.0 : -1.0;
                                    dirWhy = $"바깥 {sP:+0.0;-0.0}m / 안쪽 {sM:+0.0;-0.0}m → <b>{(sgn > 0 ? "바깥" : "안쪽")}</b>이 사면 쪽";
                                }
                                else if (okP) dirWhy = "안쪽 땅을 못 재 <b>바깥</b>으로 간다";
                                else dirWhy = "양쪽 다 못 재 <b>바깥</b>으로 간다";
                            }
                            DiagLog.Append($"    ★<b>폴리곤 폭</b> — 선 위 최대 {(wUp ? "절토" : "성토")}고 <b>{worstCut:F2}m</b>"
                                + $" ÷ 단높이 {benchH:0.##} → <b>남은 단수 {calcN}단</b>"
                                + $" × (단높이 {benchH:0.##} × <b>계획 구배 1:{slopePlan:0.###}</b> + 소단 {benchWPlan:0.##} = {runPer:F2}m)"
                                + $" = {calcN * runPer:F1}m + 여유 {GradingSettings.WallPolygonMargin:0.#}m"
                                + $" → <b>폭 {widthW:F1}m</b>"
                                + $" · 옛 사면 데이라잇까지 {toD:F1}m 이므로 <b>{widthW - toD:+0.0;-0.0}m</b>"
                                + (widthW > toD ? " <b>넘어선다</b>" : " <b>⚠못 미친다</b>") + "\n");
                            DiagLog.Append($"    ★<b>견줌</b> — 땅에 대고 세면 <b>{walkN}단</b>"
                                + $"(구간 {nStepT + 1}자리 중 땅에 닿은 데 {nMeasured}"
                                + (nNoGround > 0 ? $" · 선 위 땅을 못 잰 자리 {nNoGround}" : "") + ")"
                                + (walkN > calcN
                                   ? $" — <b>계산보다 {walkN - calcN}단 많다</b>. 모자란 몫 {(walkN - calcN) * runPer:F1}m를"
                                     + $" 여유 {GradingSettings.WallPolygonMargin:0.#}m가 <b>{((walkN - calcN) * runPer <= GradingSettings.WallPolygonMargin ? "덮는다" : "못 덮는다")}</b>"
                                   : " — 계산이 <b>넉넉하다</b>")
                                + $" · 방향: {dirWhy}\n");

                            // ── 경로에 <b>평행한</b> 선 두 줄 — 안쪽은 경로 그대로, 바깥은 직각으로 widthW ──
                            int nDiv = System.Math.Max(8, (int)System.Math.Ceiling(spanW / System.Math.Max(0.5,
                                          System.Math.Min(p.VertexSpacing, 1.0))));
                            var inner = new System.Collections.Generic.List<Point3>();
                            var outer = new System.Collections.Generic.List<Point3>();
                            for (int i4 = 0; i4 <= nDiv; i4++)
                            {
                                double t4 = wz.T0 + spanW * i4 / nDiv;
                                double tw4 = ((t4 % rTot) + rTot) % rTot;
                                var qi = GradingGeometry.OutwardAt(rul, rcm, tw4, 0.0);
                                var qo = GradingGeometry.OutwardAt(rul, rcm, tw4, widthW * sgn);
                                inner.Add(new Point3(qi.X, qi.Y, zBase));
                                outer.Add(new Point3(qo.X, qo.Y, zBase));
                            }
                            var wallBox = new System.Collections.Generic.List<Point3>(inner);
                            for (int i4 = outer.Count - 1; i4 >= 0; i4--) wallBox.Add(outer[i4]);

                            bool simpleBox = GradingGeometry.RingIsSimple(wallBox);
                            double aBox = GradingGeometry.RingAreaNts(wallBox);
                            if (wallBox.Count >= 4 && simpleBox && aBox > 1.0)
                            {
                                try
                                {
                                    var pl0 = new System.Collections.Generic.List<Point3>(wallBox) { wallBox[0] };
                                    DrawLinesOnLayer(db, tr3,
                                        new System.Collections.Generic.List<System.Collections.Generic.List<Point3>> { pl0 },
                                        "DH-가상폴리곤", PolyAci);
                                    DiagLog.Append($"    ★<b>폴리곤만 그렸다</b> — 'DH-가상폴리곤' <b>{wallBox.Count}점 / {aBox:F1}㎡</b>"
                                        + $" · 길이 {spanW:F1}m(구간 [{wz.T0:F1}..{wz.T1:F1}]) × 폭 {widthW:F1}m"
                                        + $" · 표고 {zBase:F2}m · <b>경로에 평행</b>(직각방향으로 밀었다)"
                                        + " · 표면은 <b>안 만든다</b> · DHRESET이 걷어 간다\n");
                                }
                                catch (System.Exception pex0)
                                { DiagLog.Append($"    ⚠폴리곤 그리기 실패 — {pex0.GetType().Name}: {pex0.Message}\n"); }
                            }
                            else
                                DiagLog.Append($"    ⚠<b>폴리곤을 못 만들었다</b> — 점 {wallBox.Count}개"
                                    + $" · 제 몸을 지르지 않는가 {(simpleBox ? "예" : "<b>아니오</b>")}"
                                    + $" · 넓이 {aBox:F1}㎡\n");
                        }
                        else if (slab.Count < 3)
                            DiagLog.Append("    ⚠전이면 — 옹벽면 줄이 모자라 그만둔다\n");
                        else
                        {
                            //   ★[JACK 0914] 허용오차 <b>0.001m</b> — 기본 1.0m는 옹벽의 0.05m 면보다 커서
                            //   계단을 한 줄로 뭉개고 측면(날개벽)을 지운다(실측: "전면만 나와").
                            wallSlabId = GradingBuilder.BuildVirtualSlope(db, tr3,
                                new System.Collections.Generic.List<System.Collections.Generic.List<Point3>>(),
                                "가상옹벽_DH", slab, groundId, null, midOrd: 0.001);
                            // ★[검토 A-3] <b>자가검증 한 줄을 로그에 싣는다</b> — 종전엔 받지 않아
                            //   "브레이크라인 의도 N / 정의됨 M"이 <b>어디에도 안 남았다</b>.
                            //   지금 가장 알고 싶은 숫자가 그것이다(줄이 정말 TIN에 들어갔나).
                            DiagLog.Append("    ①-v " + GradingBuilder.LastVerify + "\n");
                            var slabTin = (TinSurface)tr3.GetObject(wallSlabId, OpenMode.ForWrite);

                            // ★★★[JACK 0914 "이게 무슨 모양인지 모르겠어"] <b>제 발자국으로 먼저 가둔다.</b>
                            //
                            //   <para>가상면에는 경계가 없어 Civil 3D가 <b>볼록껍질까지</b> 삼각형을 채운다
                            //   (이 파일이 이미 그 함정을 적어 뒀다 — 위 <c>ComputePure</c>의
                            //   <i>"둘레 일부만 옹벽이면 바깥선이 ㄱ자로 파여 그 자리가 가짜 삼각형으로 메워진다"</i>).
                            //   벽은 굽은 띠라 껍질이 그 오목한 쪽을 통째로 메워 <b>엉뚱한 덩어리</b>가 붙는다 —
                            //   그래서 무슨 모양인지 알 수가 없었다.</para>
                            //
                            //   <para>★이것은 ②의 <b>데이라잇 자르기가 아니다</b>. 만든 것의 <b>제 발자국</b>
                            //   (첫 줄 ↔ 마지막 줄)으로만 가두는 것이라 단계 1에서도 정당하다 —
                            //   없는 것을 지우는 것이지 있는 것을 깎는 것이 아니다.</para>
                            if (wallPoly != null && wallPoly.Count >= 4)
                            {
                                try
                                {
                                    GradingBuilder.AddOuterBoundary(slabTin, wallPoly, midOrd: 0.001);
                                    DiagLog.Append($"    ①-b <b>가상 폴리곤 경계</b>로 가둠 — {wallPoly.Count}점"
                                        + "(볼록껍질이 채우던 가짜 삼각형 제거)\n");
                                }
                                catch (System.Exception fex)
                                { DiagLog.Append($"    ⚠①-b 폴리곤 경계 주입 실패 — {fex.Message}\n"); }
                            }
                            else DiagLog.Append("    ⚠①-b 가상 폴리곤이 없어 경계를 못 넣었다"
                                + "(볼록껍질이 채운 가짜 삼각형이 남는다)\n");

                            // ★★★[JACK 0914 "모형뷰어보기 해야만 결과물이 보이는데
                            //   가상옹벽도 만들어질 때 <b>기본적으로 보이게</b> 해줘"]
                            //   <para>새로 만든 표면은 <b>스타일이 없어</b> 평면에서 아무것도 안 그린다.
                            //   정지면에 쓰는 것과 같은 스타일을 붙이고 <b>표시</b>를 켠다.</para>
                            //   ★[JACK 0914] 전에는 <c>SetSurfaceStyle</c>로 <b>남의 스타일</b>(등고선 배경)을
                            //   붙였는데, 그 스타일은 평면에서 거의 안 그린다 — 그래서 모형 뷰어에서만 보였다.
                            //   이제 <b>옹벽 전용 스타일</b>을 만들어 <b>삼각형</b>을 켠다.
                            try
                            {
                                string vlog = GradingBuilder.MakeSurfaceVisible(db, tr3, "가상옹벽_DH",
                                                  "DH-가상옹벽면", "DH-가상옹벽", 6);
                                DiagLog.Append($"    ①-d <b>보이게</b> 했다 — {vlog}\n");
                            }
                            catch (System.Exception vex2)
                            { DiagLog.Append($"    ⚠①-d 보이게 하기 실패 — {vex2.GetType().Name}: {vex2.Message}\n"); }

                            // ★★[JACK 0914] <b>선으로도 그려 준다 — 면만으로는 안 보인다.</b>
                            //   실측: 폭 6.3m인데 높이 30m다(거의 수직). 평면에서는 얇은 띠라 선처럼 보이고
                            //   회색 음영이면 더 안 보인다. 단마다 3D 폴리선을 그려 <b>계단을 눈으로</b> 셀 수 있게 한다.
                            try
                            {
                                DrawLinesOnLayer(db, tr3, slab, "DH-가상옹벽선", 6);
                                // ★[JACK 0914] 그리는 폴리곤은 <b>쐐기</b>(데이라잇까지) — 보고 싶은 것이 그것이다.
                                //   표면을 가두는 것은 <b>발자국</b>(wallPoly)이고 그건 안 그린다(벽 선과 겹친다).
                                if (wedgePoly != null && wedgePoly.Count >= 4)
                                {
                                    var pl = new System.Collections.Generic.List<Point3>(wedgePoly) { wedgePoly[0] };
                                    DrawLinesOnLayer(db, tr3,
                                        new System.Collections.Generic.List<System.Collections.Generic.List<Point3>> { pl },
                                        "DH-가상폴리곤", PolyAci);
                                }
                                DiagLog.Append($"    ①-c 선으로도 그렸다 — 'DH-가상옹벽선' {slab.Count}줄"
                                    + $"(한 단에 둘) · 'DH-가상폴리곤' {(wedgePoly?.Count ?? 0)}점"
                                    + $"(쐐기 — 데이라잇까지) · 발자국 {(wallPoly?.Count ?? 0)}점(경계로만 씀)"
                                    + " · DHRESET이 걷어 간다\n");
                            }
                            catch (System.Exception lex)
                            { DiagLog.Append($"    ⚠①-c 선 그리기 실패 — {lex.Message}\n"); }

                        }
                    }
                }
                catch (System.Exception tex)
                {
                    DiagLog.Append($"\n  ⚠전이면 실패 — {tex.GetType().Name}: {tex.Message}\n{tex.StackTrace}\n");
                    wallSlabId = ObjectId.Null; groundPieceId = ObjectId.Null;
                }


                bool ok = false;
                for (int attempt = 1; attempt <= 3; attempt++)
                {
                    // ★[v32.9] 붙여넣기마다 굳히지 않는다(false) — 그러면 스냅샷이 <b>첫 붙여넣기 뒤에 박혀</b>
                    //   성토·절토 붙여넣기가 소스에 매달린 채 남는다(JACK 0812 정의 탭 스샷). 맨 끝에 한 번만 굳힌다.
                    finalSurfId = GradingBuilder.Composite(db, tr3, "정지면_DH", order, out string lg, true, groundId);
                    pasteLog += $"\n  시도{attempt}: {lg}";
                    if (!lg.Contains("실패")) { ok = true; break; }
                    string? failLabel = lg.Contains("성토:실패") ? "성토" : lg.Contains("절토:실패") ? "절토" : null;
                    if (failLabel == null || !injectedRings.TryGetValue(failLabel, out var info)) break;
                    var cleanedR = RawTriangleIntersectionFinder.CleanRing(info.ring);
                    if (cleanedR == null) { pasteLog += $"\n  → {failLabel} 링 정규화 실패"; break; }
                    var vsT = (TinSurface)tr3.GetObject(info.id, OpenMode.ForWrite);
                    // ★[검토 0903] <b>도넛 조건을 처음 걸 때와 똑같이 맞춘다.</b>
                    //   여기서는 "절토냐"만 보고 다시 뚫었는데, 도넛을 <b>처음</b> 거는 자리는
                    //   "절토와 성토가 <b>둘 다 실제로 있을 때만</b>"이다(위 '순수 절토/성토' 주석).
                    //   순수 절토 부지에서 절토 붙이기가 한 번 실패하면 여기가 <b>없던 구멍을 처음으로 뚫어</b>
                    //   계획부지에 구멍이 난다 — 그 주석이 경고한 바로 그 결과다.
                    bool donut = failLabel == "절토" && !cutId.IsNull && !fillId.IsNull
                              && finalRings.ContainsKey("절토") && finalRings.ContainsKey("성토");
                    GradingBuilder.ReplaceOuterBoundary(vsT, cleanedR, donut ? boundary : null);
                    // [링 2개 구조] finalRings는 순수교선 유지 — 클립링 정규화는 injected(클립)에만 반영.
                    pasteLog += $"\n  → {failLabel} 경계 정규화 재주입(정점 {cleanedR.Count})";
                    TraceRing("②경계 정규화 재주입 뒤", failLabel,
                              finalRings.TryGetValue(failLabel, out var fr2) ? fr2 : null);
                    injectedRings.Remove(failLabel); // 같은 표면 재정규화 무한루프 방지
                }
                pasteLog += ok ? "\n  ★합성 성공 — 정지면_DH 완성" : "\n  ✖합성 실패 — 자문 대기";


                // ══ ★★★[JACK 0914] <b>뚜껑이 원지반으로 덮인, 성토 위 순수 옹벽</b> — 지표면 하나로 ══
                //
                //   <para>JACK: <i>"옹벽지표면과 정지지표면, 원지반 지표면을 이용해서 <b>데이라잇</b>을
                //   만들어서 <b>순수하게 성토 위로 나온 옹벽만</b> 만든다. 이때 원지반도 데이라잇을
                //   이용해서 그 옹벽의 <b>뚜껑</b>을 만들어서 합성한다. 결론. <b>뚜껑이 원지반으로 덮힌
                //   성토 위 순수 옹벽</b>을 만든다(하나의 지표면으로)."</i></para>
                //
                //   <para>JACK이 이어서 물었다: <i>"어쨌든 <b>ㄷ모양 지표면</b>이라 가능할지는 모르겠어"</i>
                //   — <b>타당한 걱정이었다.</b> v93.2를 현장에서 돌려 까닭을 <b>쟀다</b>:</para>
                //   <code>
                //   ②옹벽 ∩ 원지반 — 교선 <b>2개</b> · 가장 넓은 것 300점 · 103.3㎡
                //   ④옹벽에서 A를 Hide        — <b>실패</b> 원본:IllegalBoundary 정규화:IllegalBoundary
                //   ⑤뚜껑A = 쐐기 − 발자국(NTS 면 빼기) 948.3㎡ — <b>성공</b>(기대 947.5㎡)
                //   ⑤뚜껑B = 원지반을 A로 자름  — <b>실패</b> 원본:IllegalBoundary 정규화:IllegalBoundary
                //   </code>
                //
                //   <para>★<b>가른 선이 분명하다.</b> NTS가 낸 링(143점)은 Civil이 <b>그대로 받았고</b>,
                //   <c>GetExactDaylight</c>이 낸 교선 링(300점)은 <b>원본도 정규화도 거부</b>당했다.
                //   까닭은 이 저장소가 이미 적어 뒀다 — <c>NtsSupport.ToCleanGeometry</c> 주석:
                //   <i>"교선(daylight) 링은 <b>자기접촉(핀치)</b>이 흔한데(<b>경계 주입 IllegalBoundary가 그 증거</b>)"</i>.
                //   그리고 <c>CleanRing</c>은 <c>Buffer(0)</c>을 거쳐 <b>감김이 반대인 로브를 소거</b>하고
                //   <b>가장 큰 조각 하나만</b> 남기므로(같은 파일 101줄의 경고) 이걸 못 고친다.</para>
                //
                //   <para>★<b>검토가 까닭을 코드로 못 박았다.</b> <c>GetExactDaylight</c>은 열린 사슬을
                //   <b>옹벽 자신의 바깥 경계를 따라</b> 닫는다(<c>RawTriangleIntersectionFinder.cs:466</c>의
                //   <c>BoundaryLoops()</c> → <c>:950</c> hull 폐합, 실측 <i>"hull 경로 폐합 45.0m"</i>).
                //   그 바깥 경계가 바로 우리가 넣은 <b>발자국 Outer</b>다 —
                //   즉 <b>Hide 링이 Outer 링과 46m 포개져</b> 있었다. Civil이 거부하는 게 당연하다.</para>
                //
                //   <para>→ <b>교선 링을 경계로 쓰지 않는다. <u>칼금(선)</u>으로만 쓴다.</b></para>
                //   <code>
                //   ③ <b>쐐기</b>를 「발자국 + A + B」 칼금으로 폴리고나이즈 → 조각 여러 개
                //        └ 핀치·자기교차는 노딩에서 그냥 <b>교차점</b>이 되어 사라진다.
                //        └ 발자국이 아니라 <b>쐐기</b>를 가른다 — 조각들이 쐐기를 <b>빈 데 없이</b> 덮어야
                //          벽 몫과 뚜껑 몫이 <b>평면에서 안 겹친다</b>.
                //   ④ 조각마다 <b>표본 여러 점</b>에서 표고를 <b>재서</b> 「옹벽이 원지반보다 위」면 벽, 아니면 뚜껑
                //        └ "어느 쪽이 안인가"를 안 묻는다. 닫는 규칙이 만든 <b>가짜 칼금</b>도
                //          무해하다 — 그 양쪽 조각은 같은 판정을 받아 <b>도로 붙는다</b>.
                //   ⑤ 붙일 수 있으면 붙이되 <b>구멍이 생기면 안 붙인다</b>(구멍이 메워지면 벽이 뚜껑을 덮는다)
                //   ⑥ 조각마다 표면 하나 — 뚜껑은 <b>원지반</b>에서, 벽은 <b>옹벽</b>에서 뜬다. Outer 하나씩.
                //   ⑦ 합성 : <b>Hide 경계 0개 · 겹침 0㎡.</b> 순서에 기대지 않는다.
                //   </code>
                //
                //   <para>★<b>겹치면 안 된다</b>는 것은 이 저장소가 이미 쟀다 — 같은 자리를 가진 두 면을 붙이면
                //   마지막 paste가 <c>SurfaceException(Failure)</c>로 깨진다(이 파일 671줄 '도넛' 주석).
                //   그래서 "뚜껑을 통째로 깔고 벽으로 덮는다"는 길은 쓰지 않았다.</para>
                //
                //   <para>★<b>v93.2가 화면을 덮은 까닭도 이것이다.</b> 뚜껑B의 경계 주입이 실패했는데
                //   합성에는 <c>OK</c>로 들어갔다 — <b>안 잘린 원지반이 통째로</b> 순수옹벽_DH에 붙었다.
                //   뚜껑을 <b>하나</b>로 줄이면 그 실패 지점 자체가 사라진다.</para>
                if (tStage >= 2 && !wallSlabId.IsNull && !finalSurfId.IsNull)
                {
                    try
                    {
                        using var trW2 = db.TransactionManager.StartTransaction();
                        // ★[검토 0914 · 보통] <b>ForRead로 연다.</b> 이제 옹벽 표면에 쓰는 데가 없고
                        //   (교선·표고 조회뿐), 같은 id를 아래에서 <c>PasteSurface</c> 소스로 넘긴다.
                        var slabT = (TinSurface)trW2.GetObject(wallSlabId, OpenMode.ForRead);
                        var gndT = (TinSurface)trW2.GetObject(groundId, OpenMode.ForRead);
                        var jiT = (TinSurface)trW2.GetObject(finalSurfId, OpenMode.ForRead);

                        static double RingArea(System.Collections.Generic.IReadOnlyList<Point3> r)
                        {
                            double a = 0;
                            for (int i = 0; i < r.Count; i++)
                            { var u = r[i]; var v = r[(i + 1) % r.Count]; a += u.X * v.Y - v.X * u.Y; }
                            return System.Math.Abs(a) * 0.5;
                        }
                        // ★★[검토 0916 · 치명 재발방지] <b>구멍을 뺀 넓이</b>.
                        //   <c>RingArea</c>는 <b>테두리만</b> 잰다 — 구멍이 든 덩이를 그것으로 재면
                        //   넓이가 부풀고, 겹침과 틈이 서로 상쇄돼 <b>장부가 눈을 감는다</b>
                        //   (이 저장소가 한 번 겪은 일이다 — 64㎡ 겹침을 못 봤다 · 하네스 S126).
                        static double NetArea(System.Collections.Generic.IReadOnlyList<Point3> ring,
                                              System.Collections.Generic.IReadOnlyList<System.Collections.Generic.List<Point3>>? holes)
                        {
                            double a = RingArea(ring);
                            if (holes != null) foreach (var h in holes) a -= RingArea(h);
                            return a;
                        }
                        static bool TryZ(TinSurface s, double x, double y, out double z)
                        { try { z = s.FindElevationAtXY(x, y); return true; } catch { z = 0; return false; } }

                        // ② <b>칼금</b> 모으기 — 조각을 <b>전부</b> 쓴다.
                        //   v93.2는 `Widest()`로 가장 넓은 것 하나만 썼는데 실측이 <b>교선 2개</b>였다 —
                        //   나머지 칼금이 통째로 빠지면 판이 덜 갈린다.
                        var lA = RawTriangleIntersectionFinder.GetExactDaylight(slabT, gndT, null, null);
                        var lB = RawTriangleIntersectionFinder.GetExactDaylight(slabT, jiT, null, null);
                        // ══ ★★★[JACK 0916] <b>딴 것이 진짜 데이라잇인지 잰다.</b>
                        //
                        //   <para>JACK: <i>"지금 우리는 <b>데이라잇과의 싸움</b>이야. 지표면들을 조합해서
                        //   데이라잇을 따고 그걸 원하는 지표면의 경계에 추가해서 자르는 로직이야 —
                        //   <b>최대한 데이라잇을 따는 로직에 집중해.</b>"</i></para>
                        //
                        //   <para>그런데 우리는 <b>딴 것이 데이라잇이 맞는지 한 번도 안 쟀다.</b>
                        //   데이라잇의 뜻은 <b>두 면의 표고가 같은 선</b>이다 — 그러니 그 선 위 점마다
                        //   <c>|벽 − 상대면|</c>을 재면 <b>0이어야 한다</b>. 0이 아닌 구간이 있다면
                        //   그만큼은 데이라잇이 아니라 <b>이어 붙인 가짜 선</b>이다
                        //   (<c>GetExactDaylight</c>은 열린 사슬을 <b>옹벽 자신의 바깥 경계</b>를 따라 닫는다 —
                        //   0915에 실측한 그 46m가 그것이다).</para>
                        //
                        //   <para>이 한 줄이 있으면 <b>따는 로직</b>을 고칠 때마다
                        //   좋아졌는지 나빠졌는지 <b>숫자로</b> 갈린다.</para>
                        string DayQ(System.Collections.Generic.List<System.Collections.Generic.List<Point3>> ls,
                                    TinSurface other, string otherName)
                        {
                            var sb2 = new System.Text.StringBuilder();
                            for (int i = 0; i < ls.Count; i++)
                            {
                                var r = ls[i];
                                double len = 0;
                                for (int k = 0; k < r.Count; k++)
                                {
                                    var u = r[k]; var v = r[(k + 1) % r.Count];
                                    len += System.Math.Sqrt((u.X - v.X) * (u.X - v.X) + (u.Y - v.Y) * (u.Y - v.Y));
                                }
                                // ★점마다 <b>두 면의 표고차</b> — 진짜 데이라잇이면 0이다.
                                int nOn = 0, nOff = 0, nMiss = 0; double worst = 0, offLen = 0;
                                for (int k = 0; k < r.Count; k++)
                                {
                                    bool okW = TryZ(slabT, r[k].X, r[k].Y, out double zw);
                                    bool okO = TryZ(other, r[k].X, r[k].Y, out double zo);
                                    if (!okW || !okO) { nMiss++; continue; }
                                    double d = System.Math.Abs(zw - zo);
                                    if (d > worst) worst = d;
                                    if (d <= 0.05) nOn++;
                                    else
                                    {
                                        nOff++;
                                        var v = r[(k + 1) % r.Count];
                                        offLen += System.Math.Sqrt((r[k].X - v.X) * (r[k].X - v.X) + (r[k].Y - v.Y) * (r[k].Y - v.Y));
                                    }
                                }
                                int tot = nOn + nOff;
                                sb2.Append(i > 0 ? " + " : "")
                                   .Append($"[{i + 1}] {r.Count}점/{RingArea(r):F0}㎡/{len:F0}m")
                                   .Append(tot > 0 ? $" · <b>진짜 {100.0 * nOn / tot:F0}%</b>" : " · <b>못 쟀다</b>")
                                   .Append(nOff > 0 ? $"(가짜 {nOff}점 ≈{offLen:F0}m · 최악 {worst:F2}m)" : "(전부 5cm 안)")
                                   .Append(nMiss > 0 ? $" · 표고 못 잰 점 {nMiss}" : "");
                            }
                            return ls.Count == 0 ? "없음" : sb2.ToString();
                        }
                        DiagLog.Append($"\n  ②칼금 <b>품질</b>(진짜 = 두 면 표고차 5cm 안 · 가짜 = 이어 붙인 선)"
                            + $"\n    옹벽∩<b>원지반</b> {lA.Count}개 — {DayQ(lA, gndT, "원지반")}"
                            + $"\n    옹벽∩<b>정지면</b> {lB.Count}개 — {DayQ(lB, jiT, "정지면")}"
                            + " · <b>경계로 안 쓴다</b>(교선 링은 핀치라 Civil이 IllegalBoundary로 되돌린다 — v93.2 실측)\n");

                        if (wallPolyKeep == null || wedgePolyKeep == null)
                            DiagLog.Append($"  ⚠③~⑦ 못 함 — 발자국 {(wallPolyKeep == null ? "없음" : "있음")}"
                                + $" · 쐐기 {(wedgePolyKeep == null ? "없음" : "있음")}\n");
                        else
                        {
                            double zBase = wedgePolyKeep.Count > 0 ? wedgePolyKeep[0].Z : 0.0;

                            // ══ ★★★[JACK 0916 <i>"뚜껑 바깥변까지 해 왜 그건 자꾸 안 고치는 거야"</i>]
                            //
                            //   <para>맞는 지적이다 — 계속 미뤘다. 쐐기의 바깥 변은 여태 <b>흉내</b>였다:
                            //   둘레를 129칸으로 나눠 데이라잇 거리를 재고 <b>못 잰 칸은 이웃 값으로 메웠다</b>
                            //   (<c>①-f</c> 로그의 <i>"표본 129칸 중 ○칸에서 잼"</i>).
                            //   그러니 진짜 곡선과 어긋나는 것이 당연했다 —
                            //   스샷의 <i>"미세하게 맞지 않음 · 튀어나옴"</i>이 그것이다.</para>
                            //
                            //   <para>★<b>벽과 똑같은 방법</b>으로 딴다. 벽은 「줄을 따라가며 지형과 견주는」
                            //   1차원 문제였고, 뚜껑의 바깥 변은 「<b>직각선을 따라가며 정지면과 원지반을 견주는</b>」
                            //   1차원 문제다 — 둘이 만나는 자리가 곧 정지 데이라잇이고 거기가 뚜껑의 끝이다.
                            //   표본 간격과 무관하다(자리마다 <b>직접 찾는다</b>).</para>
                            // ★[검토 0916 · 높음8] <b>여기서 던져도 벽은 살아야 한다.</b>
                            //   이 블록이 던지면 stage-2의 바깥 catch까지 올라가 <b>③④⑤⑥⑦이 통째로</b> 날아간다 —
                            //   오늘 두 번이나 잃은 ⑤옹벽 가둠이 그 안에 있다.
                            //   뚜껑 변 하나 때문에 벽 가둠이 날아갈 이유는 없다.
                            try
                            {
                            if (rulKeep != null && rcmKeep != null && wzSpan > 1e-6)
                            {
                                // ★[검토 0916 · 높음3] 안쪽 변 간격은 <b>옛 쐐기와 같게</b> 한다 —
                                //   옛 <c>WallInWedge</c>는 «max(0.3, min(VertexSpacing, 1.0))»으로 <b>1m 상한</b>이 있었다.
                                //   내가 «max(0.5, VertexSpacing)»으로 두는 바람에 기본값(2.0)에서 간격이 2배가 됐다
                                //   (검토 실측: 안쪽 변이 자를 벗어나는 최댓값 0.117m → 0.334m).
                                //   <b>미세하게 안 맞는 걸 고치는 판에 되레 성기게 만든 것</b>이다.
                                double sp2 = System.Math.Max(0.3, System.Math.Min(p.VertexSpacing, 1.0));
                                int nT = System.Math.Max(8, (int)System.Math.Ceiling(wzSpan / sp2));
                                // ★[검토 0916 · 보통6] 찾아볼 거리의 바닥값을 <b>20m → 80m</b>로 올린다.
                                //   <c>wzFar</c>는 <b>옛 사면의</b> 데이라잇에서 온 값이라(이 파일이 스스로
                                //   "벽과 아무 상관없는 선"이라 적어 뒀다) 믿을 수 없다. 모자라면 못 찾고,
                                //   못 찾으면 그만큼 이웃으로 메워져 바깥 변이 흐려진다.
                                //   성토 47m·1:1.5면 70m를 넘는다고 이 파일이 이미 적어 뒀다.
                                double far2 = System.Math.Max(wzFar * 1.6, 80.0);
                                var dayOut = WallTrim.DaylightOutward(rulKeep, rcmKeep, wzT0, wzSpan,
                                    (x, y) => { return TryZ(gndT, x, y, out double z) ? z : (double?)null; },
                                    (x, y) => { return TryZ(jiT, x, y, out double z) ? z : (double?)null; },
                                    far2, nT, System.Math.Max(0.25, sp2 * 0.5), zBase,
                                    out int dayMiss, out double dayStep, out int dayMulti, out string dayLog);
                                if (dayOut.Count >= 3)
                                {
                                    // 쐐기 = 찍은 선(구간) + 데이라잇(되짚기)
                                    var ring2 = new System.Collections.Generic.List<Point3>();
                                    for (int i2 = 0; i2 <= nT; i2++)
                                    {
                                        double t2 = wzT0 + wzSpan * i2 / nT;
                                        var q2 = GradingGeometry.OutwardAt(rulKeep, rcmKeep,
                                                     ((t2 % rcmKeep[rcmKeep.Length - 1]) + rcmKeep[rcmKeep.Length - 1]) % rcmKeep[rcmKeep.Length - 1], 0.0);
                                        ring2.Add(new Point3(q2.X, q2.Y, zBase));
                                    }
                                    for (int i2 = dayOut.Count - 1; i2 >= 0; i2--) ring2.Add(dayOut[i2]);
                                    double aOld = RingArea(wedgePolyKeep), aNew = RingArea(ring2);
                                    // ★★★[검토 0916 · 치명1·4] <b>넓이는 모양의 잣대가 아니다.</b>
                                    //   검토 실측: 25자리 중 <b>2곳만</b> 찾은 27점짜리 링이 넓이 관문을 통과했다.
                                    //   그래서 관문을 셋으로 바꾼다 —
                                    //   ①<b>제 몸을 안 지를 것</b>(NTS IsSimple) ②<b>한 걸음이 칸의 6배를 안 넘을 것</b>
                                    //   ③못 찾은 자리가 <b>1/4을 안 넘을 것</b>. 넓이는 마지막 그물로만 둔다.
                                    double nomStep = wzSpan / System.Math.Max(1, nT);
                                    bool simple = GradingGeometry.RingIsSimple(ring2);
                                    bool okStep = dayStep <= nomStep * 6.0 + 1e-9;
                                    bool okMiss = dayMiss * 4 <= nT + 1;
                                    bool okArea = aNew > aOld * 0.3 && aNew < aOld * 3.0;
                                    if (simple && okStep && okMiss && okArea)
                                    {
                                        wedgePolyKeep = ring2;
                                        DiagLog.Append($"  ②-뚜껑 바깥 변 — <b>진짜 데이라잇으로 갈아 끼웠다</b>({dayLog})"
                                            + $" · 쐐기 {aOld:F1} → <b>{aNew:F1}㎡</b>" + "\n");
                                    }
                                    else
                                        DiagLog.Append($"  ⚠②-뚜껑 바깥 변 — <b>안 바꿨다</b>(흉내 낸 쐐기를 그대로 쓴다) — "
                                            + (!simple ? "<b>제 몸을 지른다</b> · " : "")
                                            + (!okStep ? $"<b>한 걸음 {dayStep:F2}m</b>(칸 {nomStep:F2}m의 6배 넘음) · " : "")
                                            + (!okMiss ? $"<b>못 찾음 {dayMiss}/{nT + 1}</b>(1/4 넘음) · " : "")
                                            + (!okArea ? $"<b>넓이 {aOld:F1} → {aNew:F1}㎡</b> · " : "")
                                            + dayLog + "\n");
                                }
                                else
                                    DiagLog.Append($"  ⚠②-뚜껑 바깥 변 — <b>못 땄다</b>({dayLog}) — 흉내 낸 쐐기를 그대로 쓴다" + "\n");
                            }
                            else
                                DiagLog.Append($"  ⚠②-뚜껑 바깥 변 — <b>재료가 없다</b>"
                                    + $"(자 {(rulKeep == null ? "없음" : rulKeep.Count + "점")}"
                                    + $" · 구간 {wzSpan:F1}m) — 흉내 낸 쐐기를 그대로 쓴다" + "\n");
                            }
                            catch (System.Exception dex)
                            {
                                DiagLog.Append($"  ⚠②-뚜껑 바깥 변 — <b>터졌다</b> {dex.GetType().Name}[{dex.Message}]"
                                    + " — 흉내 낸 쐐기를 그대로 쓰고 <b>벽 작업은 계속한다</b>" + "\n");
                            }

                            // ※[JACK 0916] <b>칼금 만들기는 걷었다.</b> 폴리고나이즈로 판을 가르던 길을
                            //   통째로 버렸으므로(아래 ③) 교선 링도, 진짜 구간 끊기도 더는 필요 없다.
                            //   교선은 이제 <b>품질 계측(②)에만</b> 쓴다 — 데이라잇이 얼마나 진짜인지 보는 눈이다.
                            // ══ ★★★[JACK 0916 · 검토 셋] <b>평면으로 자르는 길을 버린다.</b>
                            //
                            //   <para>검토 결론: <i>"평면에서 자르는 접근 자체가 원리적으로 틀렸다.
                            //   줄마다 3D로 자르면 <b>데이라잇은 따로 딸 필요가 없다</b> — 잘린 점이 곧 데이라잇이다."</i></para>
                            //
                            //   <para><b>까닭이 숫자로 있다.</b> 옹벽은 1:0.01이라 <b>5m 높이가 평면에서 5cm</b>다.
                            //   그런데 판정이 필요한 선(데이라잇)이 하필 그 5cm 안에 있어,
                            //   폴리고나이즈·안쪽점·격자표본·표고조회가 전부 <b>5cm 표적</b>을 맞춰야 했다.
                            //   0914~0916에 열 번 넘게 고쳤지만 증상만 바뀌었다 —
                            //   가짜 데이라잇 30% · 표결이 갈리는 조각 · 뚜껑의 8~14m 절벽 · 안 맞는 평면 데이라잇.</para>
                            //
                            //   <para>★그리고 <c>⑥-데이라잇 자르기</c>가 쓰던 링은 <b>옛 사면의 데이라잇</b>이었다
                            //   (<c>finalRings</c> = 가상사면 ∩ 원지반). <b>벽과 아무 상관없는 선</b>으로 자르고 있었다 —
                            //   JACK이 세 판 연속 말한 <i>"평면에서 데이라잇도 안 맞어"</i>가 그것이다.</para>
                            //
                            //   <para>→ <see cref="WallTrim"/>: 줄은 <b>표고가 일정</b>하므로 줄을 따라가며
                            //   원지반·정지면과 견주면 <b>1차원 문제</b>가 된다. 남길 구간의 양 끝이 곧 데이라잇이고,
                            //   <b>끝점 위에서 표고차는 정의상 0</b>이라 자가검증이 공짜다(S127 실측 0.001m).</para>
                            // ★★[검토 0916 · 높음4] 기준값도 <b>NTS 넓이</b>로 잰다 — 아래 셈이 전부 NTS다.
                            //   신발끈은 꼬인 링에서 서로 상쇄된다(실측: 꼬인 발자국 신발끈 0.0 vs NTS 400.0㎡)
                            //   → 그대로 두면 장부가 «−400㎡ 여기가 빈다»는 <b>거짓 경보</b>를 낸다.
                            double areaW = GradingGeometry.RingAreaNts(wedgePolyKeep);
                            double areaF = GradingGeometry.RingAreaNts(wallPolyKeep);
                            var lobes = WallTrim.Trim(slabKeep,
                                (x, y) => { return TryZ(gndT, x, y, out double z) ? z : (double?)null; },
                                (x, y) => { return TryZ(jiT, x, y, out double z) ? z : (double?)null; },
                                System.Math.Max(0.2, p.VertexSpacing * 0.25), 0.02, out string trimLog);
                            DiagLog.Append($"  ③<b>줄마다 3D로 자름</b> — {trimLog}\n");

                            // ══ ★★★[검토 0916 · 치명 · 현장 좌표로 재현] <b>덩이의 테두리는 벽 자리가 아니었다.</b>
                            //
                            //   <para><b>계측.</b> 덩이 링 <b>759.9㎡</b> vs 발자국 <b>404.9㎡</b> —
                            //   발자국보다 6배 깊고 바깥으로 <b>26.3m</b>. 덩이 ∖ 발자국 = <b>485.6㎡</b>.
                            //   옹벽 몫 합이 발자국 <b>전체의 2배</b>(798.8 vs 404.9)인데도 장부는 <b>"맞음"</b>이었다
                            //   (실차 +35.6 &lt; 문턱 41.7). <b>아무것도 안 자르면 0.0㎡다 — 자르기 시작해야 터진다.</b></para>
                            //
                            //   <para><b>까닭.</b> 테두리를 「첫 줄 전부 → 끝점들 → 마지막 줄 되짚기 → 첫점들」로
                            //   둘렀다. 가운데 줄들을 <b>건너뛴 현(弦)</b>이다. 벽은 ㄷ자이고 날개가
                            //   데이라잇까지(23~28m) 뻗으므로, 아래 줄이 26m 밖에서 끝나고 위 줄이 앞에서 끝나면
                            //   그 현이 <b>벽 없는 쐐기를 가로질러</b> 수백 ㎡를 삼킨다.</para>
                            //
                            //   <para>★<b>이제 띠를 쓴다</b> — 이웃한 두 줄 사이를 한 장씩 만들어 합친다
                            //   (<see cref="WallTrim.Lobe.Bands"/>). 어긋나 봐야 <b>줄 한 칸(1.05m)</b>에 갇힌다.</para>
                            var bandsAll = new System.Collections.Generic.List<System.Collections.Generic.IReadOnlyList<Point3>?>();
                            double aRaw = 0;
                            foreach (var lb in lobes)
                                foreach (var bd in lb.Bands) { bandsAll.Add(bd); aRaw += RingArea(bd); }
                            var Pw = GradingGeometry.RingUnion(bandsAll, zBase, out int bandHoles, p.VertexSpacing);
                            double aUni = 0; foreach (var q in Pw) aUni += NetArea(q.Ring, q.Holes);
                            DiagLog.Append($"  ③-<b>띠를 합친다</b> — 띠 {bandsAll.Count}장 {aRaw:F1}㎡(따로 잰 합)"
                                + $" → <b>{Pw.Count}덩이 {aUni:F1}㎡</b>(합집합)"
                                + (bandHoles > 0 ? $" · 구멍 {bandHoles}개" : "")
                                + $" · <b>겹쳐 있던 몫 {aRaw - aUni:F1}㎡</b>" + "\n");

                            // ══ ★★★[JACK 0916 스샷 <i>"깨지고 난리났어"</i>] <b>틈이 생긴 까닭 — 장부가 안 맞았다.</b>
                            //
                            //   <para><b>계측(12:51 로그)</b>: 발자국 307.8㎡ · 벽 덩이 248.3㎡ ·
                            //   「발자국 − 벽」 = <b>87.8㎡</b>. 셋이 못 맞는다 — 307.8 − 248.3 = 59.5인데 87.8이 나왔다.
                            //   차이 <b>28.3㎡</b>가 <b>발자국 바깥에 있는 덩이</b>다.</para>
                            //
                            //   <para><b>까닭 후보는 둘이고, 아직 어느 쪽인지 안 쟀다</b>(둘 다 똑같은 증상을 낸다):
                            //   <b>①부풂</b> — 덩이의 테두리는 「맨 바깥 줄 → 한쪽 끝점들 → 맨 안쪽 줄 되짚기 →
                            //   반대쪽 끝점들」로 짓는다. 가운데 줄들은 테두리에 안 들어가고 <b>곧은 선으로 건너뛴다</b>;
                            //   줄이 단마다 1.05m씩 물러나므로 그 곧은 선이 계단 바깥으로 부풀 수 있다.
                            //   <b>②자기교차</b> — 테두리가 제 몸을 지르면 <b>신발끈 넓이와 NTS 넓이가 어긋난다</b>
                            //   (하네스 S128 실측: 나비 테두리의 신발끈 0.0㎡ / NTS 450.0㎡).</para>
                            //
                            //   <para>★<b>아래 로그가 그것을 가른다</b> — <c>fitLog</c>의 앞 숫자가 NTS가 본 넓이다.
                            //   그것이 신발끈({aRaw})과 <b>같으면 ①부풂</b>, <b>다르면 ②자기교차</b>다.</para>
                            //
                            //   <para>그러면 어떻게 되나 — 그 28.3㎡는
                            //   <b>벽도 못 받고(발자국 Outer 밖이라 잘려 나간다) 뚜껑도 못 받는다(벽 몫이라고 빼 버렸다)</b>.
                            //   <b>빈 자리</b>가 남고, Civil이 그 위를 <b>긴 삼각형으로 건너질러</b> 메운다 —
                            //   되읽기의 <i>"합성면 117.7 · 원래 면 120.00 ✖ −2.3m"</i>이 바로 그 삼각형이다
                            //   (117.7은 어느 줄의 표고도 아니다 — 115와 120 사이를 이은 값이다).</para>
                            //
                            //   <para>★고침: <b>덩이를 발자국 안으로 잘라 놓고</b> 그 다음에 나눈다.
                            //   벽 삼각형은 애초에 발자국 안에만 있으니 <b>잃는 것이 없고</b>,
                            //   이제 <b>벽 몫 ∪ 뚜껑 몫 = 쐐기</b>가 정확히 성립한다 — 틈이 생길 수 없다.</para>
                            var Pfit = GradingGeometry.RingIntersect(Pw, wallPolyKeep, zBase, out string fitLog, p.VertexSpacing);
                            double aFit = 0; int nHole = 0;
                            foreach (var q in Pfit) { aFit += NetArea(q.Ring, q.Holes); nHole += q.Holes.Count; }
                            // ★관문은 <b>양쪽</b>을 본다 — 자기교차 테두리면 신발끈({aRaw})이 0에 가까워져
                            //   «aFit > aRaw*0.5» 하나만으로는 무엇이든 통과한다(하네스 S128 나비 사례).
                            // ★★[검토 0916 · 높음3] <b>되돌아가지 않는다.</b> 종전 관문은
                            //   «많이 잘리면 원본을 쓴다»였는데, 많이 잘렸다는 건 <b>원본이 부풀었다는 증거</b>지
                            //   맞춤이 틀렸다는 증거가 아니다. 검토 실측: 밖으로 50% 넘으면 거절 →
                            //   <b>틈 −160㎡ · ledgerOk=False → ⑦합성이 통째로 멈췄다</b>(고치려는 병이 심할수록 약을 안 줬다).
                            //   <c>RingIntersect</c>의 결과는 <b>언제나 성한 폴리곤이고 발자국 안</b>이다 — 그것을 쓴다.
                            bool fitOk = Pfit.Count > 0 && aFit > 1.0;
                            if (fitOk) Pw = Pfit;
                            double aWall = fitOk ? aFit : aUni;
                            DiagLog.Append($"  ③-<b>발자국 안으로 맞춤</b> — 합집합 {aUni:F1}㎡ → "
                                + (fitOk ? $"<b>{Pw.Count}덩이 {aWall:F1}㎡</b> · 발자국 밖이라 덜어낸 몫 <b>{aUni - aFit:F1}㎡</b> · {fitLog}"
                                           + (nHole > 0 ? $" · <b>⚠구멍 {nHole}개</b>(뚫을 몫 계산이 테두리만 본다)" : "")
                                         : $"<b>⚠안 맞췄다</b>({fitLog}) — 너무 많이 잘려 원본을 쓴다")
                                + "\n");
                            Pw.Sort((x, y) => RingArea(y.Ring).CompareTo(RingArea(x.Ring)));

                            // 뚜껑 몫 = 쐐기 − 벽 몫 · 뚫을 몫 = 발자국 − 벽 몫
                            // ★★[검토 0916 · 보통6] <b>구멍까지</b> 제대로 뺀다 — 종전엔 테두리만 넘겨
                            //   구멍(벽이 <b>없는</b> 자리)이 Hide에서도 뚜껑에서도 빠져 <b>주인이 없어졌다</b>.
                            //   띠를 합치면 ㄷ자 안뜰이 구멍으로 나올 수 있으므로 실제로 일어난다.
                            var Pl = GradingGeometry.RingSubtractRegions(wedgePolyKeep, Pw, zBase, out string lidLog, p.VertexSpacing);
                            var CutRegions = GradingGeometry.RingSubtractRegions(wallPolyKeep, Pw, zBase, out string cutLog, p.VertexSpacing);
                            double aLid = 0; foreach (var q in Pl) aLid += NetArea(q.Ring, q.Holes);
                            double aCut = 0; foreach (var q in CutRegions) aCut += NetArea(q.Ring, q.Holes);
                            // ★<b>틈 장부</b> — 쐐기를 벽과 뚜껑이 남김없이 나눠 가졌는지 <b>직접</b> 잰다.
                            //   (0916 실측: 종전엔 여기서 28.3㎡가 새고 있었는데 넓이 합만 보느라 안 보였다)
                            // ★<b>부호가 뜻이 다르다</b> — 양수면 <b>틈</b>(아무도 안 가진 자리),
                            //   음수면 <b>겹침</b>(둘이 같은 자리를 제 몫이라 한다). 종전엔 둘 다 "여기가 빈다"로 적었다.
                            double gapFoot = areaF - aWall - aCut;      // 발자국 = 벽 + 뚫을 몫 이어야 한다
                            DiagLog.Append($"  ④몫 나누기 — <b>옹벽 {aWall:F1}㎡</b>({Pw.Count}덩이)"
                                + $" · <b>상부 원지반 {aLid:F1}㎡</b>({Pl.Count}덩이 · {lidLog})"
                                + $" · 발자국에서 뚫을 몫 {CutRegions.Count}덩이({cutLog})" + "\n"
                                + $"    <b>틈 장부</b> 발자국 {areaF:F1} − 벽 {aWall:F1} − 뚫을 몫 {aCut:F1} = "
                                + (System.Math.Abs(gapFoot) < 1.0 ? $"<b>{gapFoot:+0.0;-0.0;0.0}㎡ — 틈 없다</b>"
                                   : gapFoot > 0 ? $"<b>⚠+{gapFoot:F1}㎡ — 여기가 빈다</b>(아무도 안 가진 자리 · 긴 삼각형이 건너지른다)"
                                                 : $"<b>⚠{gapFoot:F1}㎡ — 여기가 겹친다</b>(둘이 같은 자리를 제 몫이라 한다)")
                                + "\n");

                            // ══ ★★★[JACK 0916] <b>옹벽을 제 몫으로 가둔다.</b>
                            //
                            //   <para>★<b>이 블록을 오늘 두 번 잃었다.</b> 구조를 갈아엎으면서 스플라이스가
                            //   두 번 다 삼켰고, 그때마다 <b>안 잘린 벽 전체</b>가 화면에 나갔다
                            //   (12:39 로그에 <c>⑥옹벽 가둠</c> 줄이 통째로 없었다).
                            //   그래서 <b>안 돌면 로그가 소리치게</b> 해 둔다 — 조용히 사라지지 않게.</para>
                            //
                            //   <para><b>Outer 하나(발자국) + Hide 여럿(뚫을 몫)</b> — 도넛이다.
                            //   한 면에 Outer를 여럿 걸면 파괴된다(0916 실측: 삼각형 1311 → 137개).</para>
                            //
                            //   <para>★<b>복사본이 아니라 <c>가상옹벽_DH</c> 자신</b>에 건다 —
                            //   복사본을 붙이면 <c>SurfaceException[Failure]</c>가 난다(0915 세 판으로 확인).</para>
                            string wp;
                            if (Pw.Count == 0) wp = "<b>⚠남길 덩이가 없다</b>";
                            else
                            {
                                var wsb = new System.Text.StringBuilder();
                                try
                                {
                                    var slabW = (TinSurface)trW2.GetObject(wallSlabId, OpenMode.ForWrite);
                                    GradingBuilder.ReplaceOuterBoundary(slabW, wallPolyKeep, null, 0.001);
                                    int hOk = 0, hBad = 0, sOk = 0, sBad = 0;
                                    foreach (var q3 in CutRegions)
                                    {
                                        try { GradingBuilder.AddHideBoundary(slabW, q3.Ring, 0.001); hOk++; }
                                        catch (System.Exception he) { hBad++; wsb.Append($" <b>Hide실패</b>{he.GetType().Name}"); }
                                        // ★★★[검토 0916 · 치명] 덩이의 <b>구멍</b>은 남길 벽 섬이다 — 도로 살린다.
                                        //   <b>여기서 Outer를 또 걸고 있었다</b> — 바로 위 주석이 금지한 그것이다
                                        //   (한 면에 Outer 여럿 = 삼각형 1311 → 137개). Civil에는 이 용도의
                                        //   경계형이 따로 있다: <c>Show</c>. 그리고 <b>조용한 catch도 걷었다</b> —
                                        //   부서지든 실패하든 로그에 한 줄도 안 남던 자리다.
                                        foreach (var hh in q3.Holes)
                                        {
                                            try { GradingBuilder.AddShowBoundary(slabW, hh, 0.001); sOk++; }
                                            catch (System.Exception se) { sBad++; wsb.Append($" <b>Show실패</b>{se.GetType().Name}"); }
                                        }
                                    }
                                    int tri = -1;
                                    try { using var tc = slabW.GetTriangles(false); tri = tc.Count; } catch { }
                                    wsb.Insert(0, $"<b>Outer=발자국 {wallPolyKeep.Count}점</b> + <b>Hide {hOk}개</b>"
                                               + (hBad > 0 ? $"(실패 {hBad})" : "")
                                               + (sOk + sBad > 0 ? $" + <b>Show {sOk}개</b>(벽 섬)" + (sBad > 0 ? $"(실패 {sBad})" : "") : "")
                                               + $" → 남는 몫 {Pw.Count}덩이 {aWall:F1}㎡"
                                               + (tri >= 0 ? $" · <b>삼각형 {tri}개</b>" : ""));
                                }
                                catch (System.Exception we)
                                { wsb.Append($"<b>⚠실패</b> {we.GetType().Name}[{we.Message}]"); }
                                wp = wsb.ToString();
                            }
                            DiagLog.Append($"  ⑤<b>옹벽 가둠</b> — {wp}\n");
                            // ★[검토 0916 · 낮음] 종전엔 «Outer…»로 시작하기만 하면 조용했다 —
                            //   Hide가 통째로 실패해도 <b>안 잘린 벽</b>이 조용히 나갔다.
                            if (Pw.Count > 0 && (!wp.StartsWith("<b>Outer") || wp.Contains("실패")))
                                try { ed.WriteMessage("\n[DHGRADE] ⚠옹벽을 제대로 못 가뒀습니다 — 안 잘린 벽이 나갈 수 있습니다. 진단 로그를 보세요."); }
                                catch { }
                            // ★★[검토 0916 · 보통] <b>탐침은 테두리에서 떨어진 자리만</b> 쓴다 —
                            //   경계에 붙은 점은 근수직 계단 옆에서 어느 값이든 주므로 잣대가 안 된다
                            //   (실측: 종전 표본이 경계에서 <b>5.7mm</b>까지 붙었다).
                            var probes = new System.Collections.Generic.List<(double X, double Y, bool Wall, double ZW, double ZG)>();
                            foreach (var q in Pw)
                                foreach (var (px2, py2) in GradingGeometry.InteriorPoints(q.Ring, 60, 0.3))
                                    if (TryZ(slabT, px2, py2, out double zw2)) probes.Add((px2, py2, true, zw2, zw2));
                            foreach (var q in Pl)
                                foreach (var (px2, py2) in GradingGeometry.InteriorPoints(q.Ring, 60, 0.3))
                                    if (TryZ(gndT, px2, py2, out double zg2)) probes.Add((px2, py2, false, zg2, zg2));
                            bool ledgerOk = System.Math.Abs(aWall + aLid - areaW) < areaW * 0.03;
                            DiagLog.Append($"    <b>장부</b> 옹벽 {aWall:F1} + 원지반 {aLid:F1} = {aWall + aLid:F1}㎡"
                                + $" vs 쐐기 {areaW:F1}㎡ — "
                                + (ledgerOk ? "<b>맞음</b>" : $"<b>⚠어긋남 {aWall + aLid - areaW:+0.0;-0.0}㎡</b>") + "\n");
                            // ★[JACK 0916] 합성면 경계 = <b>쐐기 그대로</b>.
                            //   벽 몫과 원지반 몫이 쐐기를 빈 데 없이 나눠 가지므로 따로 합칠 것이 없다.
                            //   ※<c>⑥-데이라잇 자르기</c>는 걷었다 — 자르던 링이 <b>옛 사면의 데이라잇</b>이라
                            //     벽과 아무 상관이 없었다(검토 셋이 같은 결론). 벽의 경계는 이제
                            //     <see cref="WallTrim"/>이 <b>정확히</b> 낸다.
                            var Keep = new System.Collections.Generic.List<(System.Collections.Generic.List<Point3> Ring,
                                System.Collections.Generic.List<System.Collections.Generic.List<Point3>> Holes)>
                                { (new System.Collections.Generic.List<Point3>(wedgePolyKeep),
                                   new System.Collections.Generic.List<System.Collections.Generic.List<Point3>>()) };
                            DiagLog.Append($"  ⑥합성면 경계 — <b>쐐기 그대로</b> {wedgePolyKeep.Count}점/{areaW:F1}㎡" + "\n");

                            // ⑦ <b>한 지표면으로</b> — 붙이는 것은 <b>원지반과 가상옹벽_DH 둘뿐</b>이다.
                            //   ★[검토 0914 · 높음] 장부가 어긋나면 <b>안 붙인다</b> —
                            //   반쪽짜리를 "만들었다"고 켜 놓으면 화면만 보고는 구별할 길이 없다(v93.2가 그랬다).
                            // ★★[검토 0915 · 높음] <b>멈출 때는 옹벽 경계를 되돌린다.</b>
                            //   위에서 <c>가상옹벽_DH</c>의 경계를 「남길 몫」으로 갈아 끼웠는데,
                            //   2단계가 멈추면 JACK에게 <b>보여 주는 것이 그 잘린 면</b>이 된다 —
                            //   그러면 <c>DH-가상옹벽선</c>(안 잘린 선)과 <b>어긋나</b> 보인다.
                            void RestoreWallBnd(string why)
                            {
                                if (wallPolyKeep == null) return;
                                try
                                {
                                    GradingBuilder.ReplaceOuterBoundary(
                                        (TinSurface)trW2.GetObject(wallSlabId, OpenMode.ForWrite),
                                        wallPolyKeep, null, 0.001);
                                    DiagLog.Append($"  ⑦되돌림 — 옹벽 경계를 <b>발자국 {wallPolyKeep.Count}점</b>으로 되돌렸다"
                                        + $"({why} · 재료를 온전히 보여 주려고)\n");
                                }
                                catch (System.Exception rx)
                                { DiagLog.Append($"  ⚠⑦되돌림 실패 — {rx.GetType().Name}[{rx.Message}]\n"); }
                            }
                            // ★[JACK 0916] <b>덩이가 여럿이어도 멈추지 않는다</b>(위 ⑥옹벽 가둠 주석).
                            //   합성면 경계는 여전히 한 덩이여야 한다 — 그건 옹벽+원지반을 합친 것이라
                            //   보통 하나로 이어진다(갈라지면 그때는 진짜로 이상한 판이다).
                            // ★★[검토 0916] <b>틈 장부도 관문이다.</b> 종전엔 로그일 뿐이었다 —
                            //   검토 실측: 겹침 −30.87㎡인데 <c>ledgerOk</c>는 30.9 &lt; 문턱 41.7로 <b>통과</b>시켰다.
                            //   그 장부는 벽과 뚜껑의 오차가 <b>부호만 바꿔 상쇄</b>되므로 사실상 항등식이다.
                            string stopWhy = Pw.Count == 0 ? "남길 <b>옹벽</b> 몫이 하나도 없다"
                                           : System.Math.Abs(gapFoot) > System.Math.Max(2.0, areaF * 0.05)
                                             ? (gapFoot > 0 ? $"발자국에 <b>빈 자리 {gapFoot:F1}㎡</b>가 남는다"
                                                            : $"발자국에서 <b>{-gapFoot:F1}㎡가 겹친다</b>")
                                           : !ledgerOk ? "테두리 장부가 어긋난다(조각이 겹친다)"
                                           : Keep.Count == 0 ? "합성면에 걸 경계를 못 만들었다"
                                           : Keep.Count > 1 ? $"합성면 경계가 <b>{Keep.Count}덩이</b>다(한 면에 Outer를 여럿 걸 수 없다)"
                                           : "";
                            ObjectId one = ObjectId.Null; string oneLog = "";
                            if (stopWhy.Length > 0)
                            {
                                DiagLog.Append($"  ⚠⑦ <b>합성을 멈춘다</b> — {stopWhy}."
                                    + " 반쪽짜리 '순수옹벽_DH'를 켜지 않는다(재료를 온전히 되돌려 둔다)\n");
                                RestoreWallBnd("합성을 멈췄다");
                                try { GradingBuilder.EraseSurfacesByBaseName(trW2, "순수옹벽_DH", groundId); } catch { }
                                try { ed.WriteMessage($"\n[DHGRADE] 순수옹벽 합성을 멈췄습니다 — {stopWhy.Replace("<b>", "").Replace("</b>", "")}. 진단 로그를 보세요."); }
                                catch { }
                            }
                            else
                            {
                                // 원지반을 깔고 그 위에 옹벽을 얹는다 — 옹벽은 이미 제 몫으로 가둬 뒀다.
                                var pieces = new System.Collections.Generic.List<(ObjectId, string)>
                                    { (groundId, "원지반"), (wallSlabId, "옹벽") };
                                one = GradingBuilder.Composite(db, trW2, "순수옹벽_DH", pieces,
                                          out oneLog, false, groundId, freeze: false);
                                // ★[검토 0915 · 치명] <b>붙이기 결과만 본다</b> — 굳히기 진단이 섞이면 멀쩡한 것을 지운다.
                                string pasteOnly = oneLog.Split(new[] { " · 굳히기:" }, System.StringSplitOptions.None)[0];
                                bool madeOk = !one.IsNull && !pasteOnly.Contains("실패");
                                string bndLog = "안 함";
                                if (madeOk)
                                {
                                    // ★경계는 <b>합성면에 한 번</b> 건다 — 원본 → 5mm 정규화 차례로(저장소 선례 830줄).
                                    var oneT = (TinSurface)trW2.GetObject(one, OpenMode.ForWrite);
                                    System.Exception? lastB = null; bool bOk = false; string bTag = "";
                                    foreach (var (rr, tg) in new[] { (Keep[0].Ring, "원본"),
                                             (RawTriangleIntersectionFinder.CleanRing(Keep[0].Ring), "정규화") })
                                    {
                                        if (rr == null || rr.Count < 3) continue;
                                        try { GradingBuilder.ReplaceOuterBoundary(oneT, rr, null, 0.001); bOk = true; bTag = tg; break; }
                                        catch (System.Exception be3) { lastB = be3; }
                                    }
                                    int hOk2 = 0;
                                    if (bOk)
                                        foreach (var h in Keep[0].Holes)
                                        { try { GradingBuilder.AddHideBoundary(oneT, h, 0.001); hOk2++; } catch { } }
                                    int triOne = -1;
                                    try { using var tcO = oneT.GetTriangles(false); triOne = tcO.Count; } catch { }
                                    // ★★★[JACK 0916 <i>"순수옹벽에 정의에 느낌표가 들어오고
                                    //   스냅샷 재정의를 눌러야 사라져"</i>] <b>굳히는 차례가 문제였다.</b>
                                    //   <c>FreezeSurface</c>는 <b>재작성으로 끝난다</b> — 이 저장소의 v32.10 계측이
                                    //   그 증상을 그대로 적어 뒀다: <i>"찍고 나서 또 지으면 <b>스냅샷이 구식</b>
                                    //   (붙여넣기 느낌표는 사라지고 <b>스냅샷 느낌표만 남았다</b>)"</i>.
                                    //   → <b>짓고 나서 찍고, 끝</b>낸다(<c>RebuildSurfacesByBaseName</c>과 같은 차례).
                                    if (bOk)
                                    {
                                        try { oneT.Rebuild(); } catch { }
                                        try { if (oneT.IsOutOfDate) oneT.Rebuild(); } catch { }
                                        try { if (oneT.HasSnapshot) oneT.RebuildSnapshot(); else oneT.CreateSnapshot(); } catch { }
                                    }
                                    bndLog = bOk
                                        ? $"<b>{bTag}</b>({Keep[0].Ring.Count}점/{RingArea(Keep[0].Ring):F1}㎡)로 가둠"
                                          + (hOk2 > 0 ? $" · 구멍 {hOk2}개 뚫음" : "")
                                          + (triOne >= 0 ? $" · 삼각형 {triOne}개" : "")
                                        : $"<b>실패</b> {lastB?.GetType().Name}[{lastB?.Message}]";
                                    if (!bOk) madeOk = false;
                                }
                                DiagLog.Append($"  ⑦합성 — '순수옹벽_DH' {(madeOk ? "만들었다" : "<b>실패</b>")}"
                                    + $"(붙인 것: <b>원지반 + 가상옹벽_DH 둘뿐</b> — 뚜껑 조각 표면은 안 만든다)"
                                    + $"\n    경계 — {bndLog} · {oneLog}\n");
                                if (!madeOk)
                                {
                                    try { GradingBuilder.EraseSurfacesByBaseName(trW2, "순수옹벽_DH", groundId); } catch { }
                                    DiagLog.Append("  ⑦정리 — 반쪽짜리 '순수옹벽_DH'를 <b>지웠다</b>\n");
                                    RestoreWallBnd("붙이기가 실패했다");
                                    try { ed.WriteMessage("\n[DHGRADE] 순수옹벽 합성이 실패했습니다 — 진단 로그를 보세요."); } catch { }
                                    one = ObjectId.Null;
                                }
                                else
                                {
                                    pureWallOk = true;
                                    DiagLog.Append("  ⑦보이게 — " + GradingBuilder.MakeSurfaceVisible(
                                        db, trW2, "순수옹벽_DH", "DH-순수옹벽면", "DH-순수옹벽", 6) + "\n");
                                }
                            }

                            // ⑧ ★★★<b>되읽기</b> — 만들었다고 말하기 전에, <b>그 자리에서 실제로 나오는 값</b>을 잰다.
                            //   (검토 0914 · 보통: <i>"순서가 정말 이기는지는 코드 독해로는 확정 못 한다 — 이 한 줄이면 끝난다"</i>)
                            //   벽 자리에선 <b>벽 표고</b>가, 뚜껑 자리에선 <b>원지반 표고</b>가 나와야 한다.
                            if (!one.IsNull && probes.Count > 0)
                            {
                                var vsb = new System.Text.StringBuilder();
                                int okN = 0, badN = 0, missN = 0;
                                double worstW = 0, worstL = 0; int nW = 0, nL = 0;
                                double wx = 0, wy = 0, lx = 0, ly = 0;
                                var oneT = (TinSurface)trW2.GetObject(one, OpenMode.ForRead);
                                foreach (var (px, py, isWall, zw, zg) in probes)
                                {
                                    double want = isWall ? zw : zg;
                                    if (!TryZ(oneT, px, py, out double got)) { missN++; vsb.Append($"\n      · ({px:F0},{py:F0}) {(isWall ? "벽" : "뚜껑")} — <b>표고가 없다</b>(기대 {want:F2})"); continue; }
                                    double d = System.Math.Abs(got - want);
                                    bool hit = d < 0.01;                       // ★1cm — <b>같은 면이면 딱 맞아야 한다</b>
                                    if (hit) okN++; else badN++;
                                    if (isWall) { nW++; if (d > worstW) { worstW = d; wx = px; wy = py; } }
                                    else { nL++; if (d > worstL) { worstL = d; lx = px; ly = py; } }
                                    if (!hit && vsb.Length < 900)
                                        vsb.Append($"\n      · ({px:F0},{py:F0}) {(isWall ? "벽" : "뚜껑")} — 합성면 {got:F2} · 원래 면 {want:F2}"
                                                 + $" <b>✖ {got - want:+0.00;-0.00}m</b>");
                                }
                                DiagLog.Append($"  ⑧되읽기 — 탐침 {probes.Count}곳(벽 {nW} · 뚜껑 {nL}) 중 <b>맞음 {okN}</b>"
                                    + (badN > 0 ? $" · <b>어긋남 {badN}</b>" : "")
                                    + (missN > 0 ? $" · <b>표고 없음 {missN}</b>(구멍이다)" : "")
                                    + $" · 최악 차이 — 벽 <b>{worstW:F3}m</b>"
                                    + (worstW > 0.01 ? $"@({wx:F0},{wy:F0})" : "")
                                    + $" · 뚜껑 <b>{worstL:F3}m</b>"
                                    + (worstL > 0.01 ? $"@({lx:F0},{ly:F0})" : "")
                                    + (badN == 0 && missN == 0 ? " — <b>합성면이 원래 면과 같다</b>(1cm 안)" : "")
                                    + vsb.ToString() + "\n");
                            }
                            // 재료는 숨긴다 — 결과 하나만 보이게
                            var hideNames = new System.Collections.Generic.List<string> { "가상옹벽_DH", "옹벽뚜껑원본_DH" };
                            for (int i = 1; i <= GradingSettings.WallPartMax; i++)
                            { hideNames.Add($"옹벽조각{i}_DH"); hideNames.Add($"옹벽뚜껑{i}_DH"); }
                            int hidN = 0;
                            foreach (var nmH in hideNames) hidN += GradingBuilder.SetSurfaceVisible(trW2, nmH, false);
                            // ★★★[JACK 0915 스샷 "죄다 느낌표 떴어"] <b>숨긴 뒤에는 반드시 재작성한다.</b>
                            //   이 파일 1436줄이 이미 적어 뒀다 — <i>"소스 숨김(Visible 변경)이 의존 표면에
                            //   '정의 구식(⚠)'을 붙임 → 숨김 후 재작성으로 해소"</i>.
                            //   정지면_DH에는 그 처방을 쓰면서 <b>옹벽 쪽에는 안 썼다</b> — 그래서 죄다 ⚠가 떴다.
                            // ★★★[검토 0915 · 치명] <b>재작성은 여기가 아니다.</b>
                            //   ⚠를 붙이는 진짜 숨김은 한참 뒤 <c>trE</c>의 <c>IsolateSurfaces("정지면_DH")</c>다 —
                            //   그것이 <b>원지반</b>을 숨기고, 원지반을 붙인 뚜껑이 구식이 되고, 그 뚜껑을 붙인
                            //   순수옹벽_DH가 또 구식이 된다. 여기서 아무리 구워 봐야 그 뒤에 도로 구식이 된다.
                            //   → 재작성은 <b>trE의 표시 손질이 다 끝난 뒤</b>에, <b>소스 → 의존</b> 차례로 한다.
                            DiagLog.Append($"  ⑦정리 — 재료 {hidN}개를 숨겼다"
                                + "(재작성은 표시 손질이 끝난 뒤에 한다 — 아래 ★옹벽 표시 다음)" + "\n");
                        }
                        trW2.Commit();
                    }
                    catch (System.Exception pex)
                    {
                        DiagLog.Append($"\n  ⚠순수 옹벽 실패 — {pex.GetType().Name}: {pex.Message}\n{pex.StackTrace}\n");
                        // ★[검토 0914 · 높음] <b>화면에도 알린다.</b> 여기서 트랜잭션이 되물려지면
                        //   지웠던 <b>지난 실행의 순수옹벽_DH가 되살아나고</b>, 뒤에서 이름으로 찾아 켠다 —
                        //   JACK 눈에는 <b>새 결과처럼</b> 보인다. 로그에만 적으면 구별할 길이 없다.
                        try { ed.WriteMessage($"\n[DHGRADE] 순수옹벽을 못 만들었습니다({pex.GetType().Name})."
                            + " 화면의 '순수옹벽_DH'는 지난 실행의 것일 수 있습니다 — 진단 로그를 보세요."); }
                        catch { }
                    }
                }
                else if (tStage >= 1)
                {
                    // ★[JACK 0916 <i>"가상 옹벽만 나오게"</i>] <b>지난 판의 찌꺼기를 걷는다.</b>
                    //   2단계에서 만들던 면들(순수옹벽·옹벽뚜껑N)은 이제 안 만든다 —
                    //   그런데 <b>지우지 않으면 도면에 그대로 남아</b> 새 결과처럼 보인다.
                    int wipeN = 0;
                    try
                    {
                        using var trK = db.TransactionManager.StartTransaction();
                        try { GradingBuilder.EraseSurfacesByBaseName(trK, "순수옹벽_DH", groundId); wipeN++; } catch { }
                        try { GradingBuilder.EraseSurfacesByBaseName(trK, "옹벽뚜껑원본_DH", groundId); wipeN++; } catch { }
                        for (int i3 = 1; i3 <= GradingSettings.WallPartMax; i3++)
                        {
                            try { GradingBuilder.EraseSurfacesByBaseName(trK, $"옹벽조각{i3}_DH", groundId); wipeN++; } catch { }
                            try { GradingBuilder.EraseSurfacesByBaseName(trK, $"옹벽뚜껑{i3}_DH", groundId); wipeN++; } catch { }
                        }
                        trK.Commit();
                    }
                    catch { }
                    DiagLog.Append($"\n  ②~⑦ — <b>안 한다</b>(단계 {tStage}"
                        + $" · 옹벽 {(wallSlabId.IsNull ? "없음" : "있음")}"
                        + $" · 정지면 {(finalSurfId.IsNull ? "없음" : "있음")})."
                        + $" <b>가상옹벽_DH만</b> 만들고 멈춘다"
                        + " · 지난 판 찌꺼기(순수옹벽·옹벽뚜껑)를 걷었다" + ".\n");
                }
                // ★[검토 0903 · JACK "도면 수행 시 무거우면 안 되"] <b>합성면 검사는 뺐다.</b>
                //   합성면은 원지반을 깔고 만드는 면이라 삼각형이 25만 개(314ms)인데 그 대부분이
                //   <b>원지반</b>이다 — 실제로 최장 변 549m가 찍힌 자리도 정지 구역이 아니라
                //   수치지도 서쪽 끝이었다. <b>정지와 무관한 것을 비싸게 잰 것이다.</b>
                //   순수 정지면(1~2ms)만 재도 부채꼴은 똑같이 보인다.

                // ── ★★[v32.2 · JACK 0812] <b>순수 정지면 — 원지반을 빼고 정지된 면만.</b>
                //   위 합성면은 <b>원지반을 깔고</b> 시작하므로 정지 바깥에서도 값이 나오고, 그 값은 원지반과 같다.
                //   그래서 종단을 뜨면 정지 밖에서 계획선이 원지반선과 <b>포개진다</b>(JACK 지적).
                //   여기서는 <b>같은 재료를 원지반 없이</b> 한 번 더 붙여, 정지 밖에는 값이 없는 면을 만든다.
                //   종단·횡단만 이걸 본다(<see cref="SectionCommand.FindSurfaces"/>) — 나머지 기능은 합성면 그대로다.
                //
                //   ※ 실패해도 <b>진행을 막지 않는다.</b> 순수면이 없으면 종단은 합성면으로 물러나
                //     종전과 똑같이 동작한다 — 이것 때문에 정지면 생성이 통째로 실패하면 안 된다.
                try
                {
                    var orderPure = new System.Collections.Generic.List<(ObjectId, string)>();
                    if (!prevPureId.IsNull) orderPure.Add((prevPureId, "앞 구역"));
                    if (!fillId.IsNull) orderPure.Add((fillId, "성토"));
                    if (!cutId.IsNull) orderPure.Add((cutId, "절토"));
                    if (orderPure.Count == 0)
                        pasteLog += $"\n  순수 정지면: 붙일 것이 없어 생략";
                    else
                    {
                        GradingBuilder.Composite(db, tr3, PureBase, orderPure, out string lgPure, true);
                        pasteLog += $"\n  순수 정지면({PureBase}): {lgPure}"
                                  + (prevPureId.IsNull ? "" : " · 앞 구역 물려받음");
                    }
                }
                catch (System.Exception px) { pasteLog += "\n  순수 정지면 실패(종단은 합성면으로 물러난다) — " + px.Message; }

                tr3.Commit();
            }
            catch (System.Exception ex) { pasteLog += $"  합성 자체 실패: {ex.Message}"; }
            try
            {
                DiagLog.Append(
                    "\n■ 합성(Paste) 검증\n  " + pasteLog + "\n");
            }
            catch { }

            // ── ★★[v32.6 · JACK 0812] <b>데이라잇을 '순수 정지면의 외곽선'으로 다시 그린다.</b>
            //
            //   JACK: <i>"정지순수_DH 있는 건 외곽선도 있는 거 아니야? 왜 굳이 다시 그려내는 거지?"</i>
            //
            //   위에서 그린 데이라잇은 <b>절토 교선</b>과 <b>성토 교선</b>을 따로 계산해 이어 붙인 것이라
            //   절성 경계마다 <b>아무도 안 그리는 틈</b>이 남는다(실측 2.84m). 그 틈을 직선으로 메우면
            //   <b>없는 형상을 지어내는</b> 것이라 JACK이 기각했다.
            //   순수 정지면은 절토·성토를 <b>이미 하나로 붙여 놓은</b> 면이고, TIN의 외곽은
            //   <b>정의상 닫혀 있다</b> — 그 선이 곧 정지면이 실제로 끝나는 자리, 원지반과 맞닿는 자리다.
            //
            //   <b>왜 '다시' 그리나.</b> 순수면은 이 시점(합성 뒤)에야 존재한다. 위 작도를 없애지 않고
            //   덮어쓰는 이유는, 순수면을 못 만든 도면에서 <b>종전 결과라도 남아야</b> 하기 때문이다
            //   (<see cref="GradingBuilder.DrawDaylight"/>는 그리기 전에 레이어를 비운다).
            try
            {
                using var trO = db.TransactionManager.StartTransaction();
                var pureId = GradingBuilder.FindSurfaceByBaseName(trO, PureBase);
                string oMsg;
                if (!pureId.IsNull &&
                    trO.GetObject(pureId, OpenMode.ForRead) is Autodesk.Civil.DatabaseServices.TinSurface pureTin)
                {
                    var outline = GradingBuilder.SurfaceOutline(pureTin, trO, out string oDiag);
                    if (outline.Count > 0)
                    {
                        GradingBuilder.DrawDaylight(db, trO, outline, "DH-정지경계", 3, layerOff: false);
                        oMsg = $"정지경계 재작도(순수면 외곽선): {oDiag} · {GradingBuilder.LastDaylightDiag}"
                             + FoldDiag(outline) + SurfaceEdgeScan(db, pureId, PureBase)
                             // ★[JACK 0904] <b>화면에 보이는 그 면</b>을 잰다 — 가상면이 아니라 완성면이
                             //   부지 밖 어디까지 나가 있는지. JACK이 "사면이 계획면보다 넓어진다"고 한 자리다.
                             + "\n" + GradingBuilder.VerifySurfaceExtent(pureTin, boundary, null);
                    }
                    else oMsg = $"정지경계 재작도 건너뜀 — 외곽선을 못 뽑았다({oDiag}) · 종전 교선 작도를 그대로 둔다";
                }
                else oMsg = "정지경계 재작도 건너뜀 — 순수 정지면이 없다 · 종전 교선 작도를 그대로 둔다";
                trO.Commit();
                bndMsg += "\n" + oMsg;
                try { DiagLog.Append("\n■ 데이라잇 재작도\n  " + oMsg + "\n"); } catch { }
            }
            catch (System.Exception ox)
            {
                bndMsg += "\n정지경계 재작도 실패(종전 작도 유지) — " + ox.Message;
                try { DiagLog.Append("\n■ 데이라잇 재작도 실패 — " + ox.Message + "\n"); } catch { }
            }

            // ── 3.5단계 [§75 1-A]: 사면선·소단선을 식별 태그(XData: 방향·단·구간)와 함께 작도 ──
            //   옹벽 전환(DHWALL)이 클릭할 대상. JACK: 지표면 생성 때 함께 생성. 항상 사면 기준(옹벽 미적용).
            //   클립은 DHNORI와 동일(finalRing − 계획경계 도넛). ground는 클립 모드라 미사용(NullGround).
            stw.Stage("3.5단계 사면선 태그");
            string edgeMsg = "";
            try
            {
                var ng = new NullGround();
                var cutEdges = new System.Collections.Generic.List<(bool, int, int, System.Collections.Generic.List<Point3>)>();
                var fillEdges = new System.Collections.Generic.List<(bool, int, int, System.Collections.Generic.List<Point3>)>();
                var wallLines = new System.Collections.Generic.List<System.Collections.Generic.List<Point3>>();
                // [진단 0728] 옹벽선으로 분류돼 버려지는 런 수를 로그로 보기 위한 수거통(그리지는 않음 — 노리선 담당).
                var wallDump = new System.Collections.Generic.List<System.Collections.Generic.List<Point3>>();
                foreach (var (vs, up, label, target, zn) in new[]
                {
                    (cut, true, "절토", cutEdges, cutZones),
                    (fill, false, "성토", fillEdges, fillZones),
                })
                {
                    if (!vs.HasSlope) continue;
                    var ringList = allRings.TryGetValue(label, out var rs) && rs.Count > 0 ? rs
                        : (finalRings.TryGetValue(label, out var fr0)
                            ? new System.Collections.Generic.List<System.Collections.Generic.List<Point3>> { fr0 }
                            : null);
                    if (ringList == null) continue;
                    for (int ri = 0; ri < ringList.Count; ri++) TraceRing($"③옹벽선 확정 직전[{ri}]", label, ringList[ri]);
                    foreach (var fr in ringList)
                    {
                        if (fr == null || fr.Count < 3) continue;
                        // [JACK 0728] 옹벽선은 이 단계에서 그리지 않음(노리선 때만 표시) — wallDump는 개수 진단용.
                        // [구간 구배 0804] 구간이 '수직(옹벽)'인지 판정하려면 그 방향 전역 구배와 최소구배가 필요.
                        target.AddRange(SlopeHatchGenerator.GenerateEdgeLinesTagged(vs.Rings, ng, up, fr, boundary,
                            zn, boundary, wallDump,
                            baseSlope: System.Math.Max(up ? p.CutSlope : p.FillSlope, p.MinSlope), minSlope: p.WallGateSlope));
                    }
                }
                using Transaction trE = db.TransactionManager.StartTransaction();
                // [다중 구역] 이 구역(계획선 핸들) 태그 포함 — DHWALL이 마지막 구역 선만 받도록.
                GradingBuilder.DrawSlopeEdgesTagged(db, trE, cutEdges, fillEdges, planPolyId.Handle.ToString());
                // [JACK 0728] 이전 노리선 실행이 남긴 옹벽선(빨강)은 낡은 정보 → 청소만(재표시는 DHNORI가).
                GradingBuilder.DrawWallLines(db, trE, wallLines);
                // [JACK 0728] '결과지표면만 표시' 옵션 시 정지면_DH만 보이게 — 원지반·가상면 등 전부 숨김.
                if (GradingSettings.ShowOnlyResultSurface)
                {
                    GradingBuilder.IsolateSurfaces(trE, "정지면_DH");
                    // [0728] 소스 숨김(Visible 변경)이 의존 표면에 '정의 구식(⚠)'을 붙임 → 숨김 후 재작성으로 해소.
                    GradingBuilder.RebuildSurfacesByBaseName(trE, "정지면_DH");
                }
                // ★[v32.2] 순수 정지면은 <b>옵션과 무관하게 늘 숨긴다.</b>
                //   종단·횡단은 표면의 <b>정의</b>를 읽으므로 보일 필요가 없고, 보이면 평면도에
                //   합성면과 <b>등고선이 두 겹</b>으로 겹쳐 그려진다(정지 구간에서 정확히 포개진다).
                GradingBuilder.SetSurfaceVisible(trE, PureBase, false);
                // ★★★[JACK 0914 "옹벽은 보이지도 않고" · 검토 A-1] <b>여기서 도로 꺼지고 있었다.</b>
                //   <para>앞에서 <c>MakeSurfaceVisible</c>로 켰는데, 위 <c>IsolateSurfaces("정지면_DH")</c>가
                //   <b>이름이 정지면_DH가 아닌 모든 표면</b>을 끈다(<c>ShowOnlyResultSurface</c> 기본 <c>true</c>).
                //   로그의 "보이게 했다"는 <b>거짓말이 아니라 너무 일찍 끝난 것</b>이었다 —
                //   재는 자리가 켜는 자리였고, 끄는 자리는 그 뒤였다.</para>
                //   <para>→ 가려 놓은 <b>뒤에</b> 옹벽만 다시 켠다. 옹벽은 정지면과 <b>따로</b> 보여야 하는 것이라
                //   '결과지표면만 표시'의 뜻에 어긋나지 않는다(JACK: <i>"순수하게 옹벽변환 지표면과
                //   정지면_DH가 나오게 하라고"</i>).</para>
                //   <para>★★[JACK 0914 2차] <b>2단계에서는 켤 이름이 다르다.</b> 2단계의 결과물은
                //   <c>순수옹벽_DH</c>이고 <c>가상옹벽_DH</c>는 <b>재료</b>다. 여기서 재료만 켜고 있었으니
                //   결과물은 <c>IsolateSurfaces</c>에 꺼진 채로 남았다 — 앞의 실수와 <b>똑같은 실수</b>다.</para>
                //   ★[검토 0914 · 높음] <b>깃발을 본다</b> — 이름만 보고 켜면 <b>지난 실행의 찌꺼기</b>를
                //   새 결과처럼 켤 수 있다(위 <c>pureWallOk</c> 주석). 못 됐으면 재료를 보여 준다.
                // ★[JACK 0916] 폴리곤만 만드는 판에서는 <b>켤 표면이 없다</b> — 억지로 찾으면 못 찾았다고 로그만 시끄럽다.
                string wallShowName = (GradingSettings.TransitionStage >= 2 && pureWallOk) ? "순수옹벽_DH" : "가상옹벽_DH";
                // ★[JACK 0917] 같은 회귀를 여기서도 막는다 — <b>옹벽 구간이 있을 때만</b>
                //   "폴리곤만 만드는 판"이다. 보통 정지 작업에서는 종전 문구가 나가야 한다.
                bool polyOnly = GradingSettings.WallPolygonOnly
                             && (wallZoneCut.Count > 0 || wallZoneFill.Count > 0);
                int wallVis = polyOnly ? 0 : GradingBuilder.SetSurfaceVisible(trE, wallShowName, true);
                if (GradingSettings.TransitionStage >= 2 && !pureWallOk)
                {
                    // 못 만든 '순수옹벽_DH'가 지난 실행에서 남아 있으면 <b>꺼 둔다</b> — 새것으로 오인하지 않게
                    GradingBuilder.SetSurfaceVisible(trE, "순수옹벽_DH", false);
                    DiagLog.Append("\n  ⚠순수옹벽을 <b>못 만들었다</b> — 같은 이름의 옛 표면이 있으면 꺼 두고,"
                        + " 재료('가상옹벽_DH')만 보여 준다\n");
                }
                DiagLog.Append(polyOnly
                    ? "\n  ★옹벽 표시 — <b>켤 표면이 없다</b>(폴리곤만 만드는 판이다). 화면에 남는 것은 <b>정지면_DH</b>와 빨간 <b>'DH-가상폴리곤'</b> 한 줄이다.\n"
                    : $"\n  ★옹벽 표시 — 가려 놓은 뒤 <b>'{wallShowName}'</b>을 다시 켰다 · 켠 표면 {wallVis}개"
                      + $"(단계 {GradingSettings.TransitionStage} · ShowOnlyResultSurface={GradingSettings.ShowOnlyResultSurface})\n");
                // [JACK 0728] 정지면_DH 표시 스타일 = Contours 2m and 10m (Background) (한글 템플릿 이름 폴백 포함).
                // ★★★[검토 0915 · 치명] <b>여기가 재작성 자리다.</b>
                //   위 <c>IsolateSurfaces</c>가 <b>원지반까지</b> 숨겼고, 이 파일 1710줄은 정지면_DH만 다시 굽는다.
                //   옹벽 쪽 사슬(원지반 → 옹벽뚜껑N_DH → 순수옹벽_DH)은 그대로 ⚠를 단 채 남았다 —
                //   JACK 스샷의 <i>"죄다 느낌표 떴어"</i>가 그것이다.
                //   ★<b>소스를 먼저, 의존을 나중에</b> 굽는다. 거꾸로 하면 소스를 굽는 순간 의존이 도로 구식이 된다.
                if (GradingSettings.TransitionStage >= 2)
                {
                    var wsb2 = new System.Text.StringBuilder(); int rebN = 0;
                    for (int i2 = 1; i2 <= GradingSettings.WallPartMax; i2++)
                        try { var r = GradingBuilder.RebuildSurfacesByBaseName(trE, $"옹벽뚜껑{i2}_DH");
                              // ★[JACK 0916] <c>Contains("없음")</c>은 <b>"스냅샷 없음"</b>에도 걸린다 —
                              //   그래서 진짜로 구운 표면이 로그에서 통째로 사라졌다. <b>"표면 0개"</b>로만 거른다.
                              if (!string.IsNullOrWhiteSpace(r) && !r.Contains("표면 0개")) { rebN++; wsb2.Append(" " + r); } }
                        catch { }
                    // ※가상옹벽_DH는 <b>붙여넣기가 없는 면</b>(계단 브레이크라인으로 짓는다)이라
                    //   원지반을 숨겨도 구식이 될 수 없다. 여기서 구우면 <b>없던 스냅샷만 새로 생겨</b> ⚠ 위험만 는다.
                    try { var r = GradingBuilder.RebuildSurfacesByBaseName(trE, "순수옹벽_DH");
                          if (!string.IsNullOrWhiteSpace(r) && !r.Contains("표면 0개")) { rebN++; wsb2.Append(" " + r); } }
                    catch { }
                    DiagLog.Append($"  ★옹벽 사슬 재작성 — <b>{rebN}개</b>(소스 → 의존 차례){wsb2}"
                        + "(숨김이 ⚠를 붙이므로 — 이 파일 1710줄의 처방을 옹벽에도)" + "\n");
                }
                string styleApplied = GradingBuilder.SetSurfaceStyle(trE, "정지면_DH",
                    "Contours 2m and 10m (Background)", "등고선 2m 및 10m (배경)");
                trE.Commit();
                edgeMsg = $"사면선/소단선(옹벽 전환용 태그) 작도: 절토 {cutEdges.Count} · 성토 {fillEdges.Count}" +
                          $" (옹벽선으로 분류·생략 {wallDump.Count} · 구간 절 {cutZones.Count}/성 {fillZones.Count})" +
                          $" · 표시스타일 {(styleApplied == "" ? "미적용(후보 없음)" : styleApplied)}";
            }
            catch (System.Exception ex) { edgeMsg = "사면선/소단선 태그 작도 실패 — " + ex.Message; }
            try
            {
                DiagLog.Append(
                    "\n■ 사면선/소단선 태그 작도(3.5단계)\n  " + edgeMsg + "\n");
            }
            catch { }

            // ── 4단계: 결과 번들 저장(ralplan Phase 0) — 노리선 작도는 DHNORI(노리선 버튼)로 이관 ──
            // 저장 시점 = 3단계의 모든 복구·정규화 종단점 이후(finalRings가 정규화 재주입까지 반영된 상태).
            // 내부 링은 boundary+params에서 결정적 재계산 가능하므로 재현 불가능한 finalRing만 저장.
            bool bundleFailed = false;
            string bundleMsg = "", trimMsg = "";
            // ── [옹벽선 정본화 0805 — 옹벽선_재설계.md P2] 옹벽선을 **여기서 확정**한다 ──
            //   지표면을 만든 그 링에서, 지금 이 자리에서 뽑아 저장한다. 내보내기는 이걸 읽기만 하므로
            //   '내보내기가 링을 다시 계산해 지표면과 어긋나는' 구조적 결함이 사라진다.
            //   실패해도 번들 저장 자체는 계속한다(옛 경로로 폴백 — 정지면은 이미 완성돼 있다).
            System.Collections.Generic.List<WallRun>? cutRuns = null, fillRuns = null;
            string runMsg = "";
            stw.Stage("옹벽선 확정");
            try
            {
                cutRuns = cut.HasSlope
                    ? WallRunBuilder.Build(boundary, cut.Rings, cutZonesR.Count > 0 ? cutZonesR : null,
                                           up: true, globalSlope: p.CutSlope, minSlope: p.MinSlope, gateSlope: p.WallGateSlope)
                    : null;
                string cd = WallRunBuilder.LastDiag;
                fillRuns = fill.HasSlope
                    ? WallRunBuilder.Build(boundary, fill.Rings, fillZonesR.Count > 0 ? fillZonesR : null,
                                           up: false, globalSlope: p.FillSlope, minSlope: p.MinSlope, gateSlope: p.WallGateSlope)
                    : null;
                string fd = WallRunBuilder.LastDiag;
                if (cutRuns != null && cutRuns.Count == 0) cutRuns = null;
                if (fillRuns != null && fillRuns.Count == 0) fillRuns = null;
                runMsg = $"옹벽선 확정 — 절토 {(cutRuns?.Count ?? 0)}줄 · 성토 {(fillRuns?.Count ?? 0)}줄" +
                         (cut.HasSlope ? $"\n  절토: {cd}" : "") + (fill.HasSlope ? $"\n  성토: {fd}" : "");
            }
            catch (System.Exception rex) { runMsg = "옹벽선 확정 실패(내보내기는 옛 경로로 폴백) — " + rex.Message; }
            try { DiagLog.Append("\n■ 옹벽선 확정(4단계 전)\n  " + runMsg.Replace("\n", "\n  ") + "\n"); }
            catch { }

            stw.Stage("4단계 번들 저장");
            try
            {
                var fp = GradingBundle.Fingerprint(boundary);
                var bundle = new GradingBundle
                {
                    PlanHandle = planPolyId.Handle.ToString(),
                    GroundHandle = groundId.Handle.ToString(),   // [v4] 이 구역의 기준 지반(재실행·DHWALL용)
                    VertexCount = fp.N,
                    CentroidX = fp.Cx, CentroidY = fp.Cy,
                    BboxMinX = fp.MinX, BboxMinY = fp.MinY, BboxMaxX = fp.MaxX, BboxMaxY = fp.MaxY,
                    Perimeter = fp.Perim, Diagonal = fp.Diag,
                    Boundary = boundary,
                    Params = p,
                    CutHasSlope = cut.HasSlope,
                    FillHasSlope = fill.HasSlope,
                    CutFinalRing = finalRings.TryGetValue("절토", out var cr) ? cr : null,
                    FillFinalRing = finalRings.TryGetValue("성토", out var fr) ? fr : null,
                    CutFinalRings = allRings.TryGetValue("절토", out var crs) ? crs : null,
                    FillFinalRings = allRings.TryGetValue("성토", out var frs) ? frs : null,
                    // [v8 0804] 실제 주입 클립링 — 다중 구역 발자국 마스크 정본(순수교선과 달리 정규화까지 반영).
                    CutClipRing = injectedRings.TryGetValue("절토", out var icr) ? icr.ring : null,
                    FillClipRing = injectedRings.TryGetValue("성토", out var ifr) ? ifr.ring : null,
                    // [§75 v3] 적용된 옹벽 구간 보존 — DHNORI(노리선 제외+옹벽선)·DHINFRA 소비.
                    CutWallZones = cutZones.Count > 0 ? cutZones : null,
                    FillWallZones = fillZones.Count > 0 ? fillZones : null,
                    // [v9 0805] 옹벽선 정본 — 내보내기는 이것만 읽는다(옹벽선_재설계.md).
                    CutWallRuns = cutRuns,
                    FillWallRuns = fillRuns,
                };
                // [다중 구역 0729] 모드별 구역 목록: Fresh=이 구역 하나 / Append=기존 뒤에 추가 / RerunLast=마지막 교체.
                var save = mode == GradeMode.Append && regionsPrev != null
                    ? new System.Collections.Generic.List<GradingBundle>(regionsPrev) { bundle }
                    : mode == GradeMode.RerunLast && regionsPrev != null && regionsPrev.Count > 0
                        ? new System.Collections.Generic.List<GradingBundle>(regionsPrev)
                        : new System.Collections.Generic.List<GradingBundle> { bundle };
                if (mode == GradeMode.RerunLast && regionsPrev != null && regionsPrev.Count > 0)
                    save[save.Count - 1] = bundle;

                // ★[이어서 하기 0805] 이 구역이 덮은 자리에서 **앞 구역들의 옹벽선을 잘라 갱신**한다.
                //   여기서 갱신해 두면 내보내기 시점엔 이미 최종 상태 — 지우개(마스크)가 필요 없어지고,
                //   지우개 경계에 조각이 남던 종전 결함의 뿌리가 사라진다.
                try
                {
                    var mine = new System.Collections.Generic.List<System.Collections.Generic.List<Point3>>();
                    if (bundle.CutClipRing is { Count: >= 3 }) mine.Add(bundle.CutClipRing);
                    if (bundle.FillClipRing is { Count: >= 3 }) mine.Add(bundle.FillClipRing);
                    if (boundary is { Count: >= 3 }) mine.Add(boundary);
                    var mask = GradingPolygons.RegionMask.Build(mine);
                    if (mask != null && save.Count > 1)
                    {
                        int last = save.Count - 1, trimmed = 0;
                        for (int r = 0; r < last; r++)
                        {
                            var pb = save[r];
                            int before = (pb.CutWallRuns?.Count ?? 0) + (pb.FillWallRuns?.Count ?? 0);
                            if (before == 0) continue;
                            var nc = WallRunBuilder.TrimBy(pb.CutWallRuns, mask.Contains);
                            var nf = WallRunBuilder.TrimBy(pb.FillWallRuns, mask.Contains);
                            pb.CutWallRuns = nc.Count > 0 ? nc : null;
                            pb.FillWallRuns = nf.Count > 0 ? nf : null;
                            int after = nc.Count + nf.Count;
                            if (after != before) trimmed++;
                        }
                        trimMsg = trimmed > 0
                            ? $"\n앞 구역 옹벽선 갱신: {trimmed}개 구역이 이번 구역에 덮여 잘림 — {WallRunBuilder.LastDiag}"
                            : "\n앞 구역 옹벽선 갱신: 덮인 옹벽 없음";
                    }
                }
                catch (System.Exception tex) { trimMsg = "\n앞 구역 옹벽선 갱신 실패 — " + tex.Message; }
                using Transaction tr4 = db.TransactionManager.StartTransaction();
                GradingBundleStore.SaveAll(db, tr4, save);
                tr4.Commit();
                TraceRing("④번들에 담기 직전", "절토", bundle.CutFinalRing);
                TraceRing("④번들에 담기 직전", "성토", bundle.FillFinalRing);
                bundleMsg = $"번들 저장 v{GradingBundleStore.Version} — 구역 {save.Count}개 · 이번 구역 경계 {boundary.Count}점 · " +
                            $"절토링 {(bundle.CutFinalRing?.Count ?? 0)}점 · 성토링 {(bundle.FillFinalRing?.Count ?? 0)}점 · " +
                            $"클립링 절 {(bundle.CutClipRing?.Count ?? 0)}점/성 {(bundle.FillClipRing?.Count ?? 0)}점 · " +
                            $"옹벽선 절 {(bundle.CutWallRuns?.Count ?? 0)}줄/성 {(bundle.FillWallRuns?.Count ?? 0)}줄" +
                            trimMsg +
                            "\n→ [노리선]·[INFRAWORKS] 버튼이 이 번들을 사용합니다";
            }
            catch (System.Exception ex) { bundleMsg = "번들 저장 실패 — " + ex.Message; bundleFailed = true; }
            try
            {
                DiagLog.Append(
                    "\n■ 번들 저장(4단계)\n  " + bundleMsg.Replace("\n", "\n  ") + "\n");
                // ★[검토 0903] <b>계측기가 사고 순간에 꺼지면 안 된다.</b>
                //   종전에는 링추적을 bundleMsg에 실어 보냈는데, 그 블록은 통째로 catch된다 —
                //   번들 저장이 실패하는 판(<b>지금 쫓는 것이 바로 그런 판이다</b>)에서는
                //   "번들 저장 실패"만 남고 어느 단계에서 링이 무너졌는지는 못 본다.
                //   → 성공하든 실패하든 <b>따로</b> 내보낸다.
                if (ringTrace.Length > 0) DiagLog.Append(ringTrace.ToString() + "\n");
            }
            catch { }

            // [§75 1회성 — JACK 0728] 옹벽 선택은 한 번 적용되면 자동 해제(다시 원하면 DHWALL로 새로 선택).
            //   예외로 중단된 경우(catch)는 선택 유지 — 재시도 가능.
            if (GradingSettings.WallPicks.Count > 0)
            {
                ed.WriteMessage($"\n[옹벽] 선택 {GradingSettings.WallPicks.Count}건 적용 완료 — 자동 해제(다음 정지면은 순수 사면 기준)");
                GradingSettings.WallPicks.Clear();
            }

            // 상세 진단은 전부 로그로(위 AppendAllText들). 팝업은 **성패 + 토량**만 — 공용 배포용(JACK 0720).
            // ★★[검토 0824 S-1] **번들 저장 실패를 성패 판정에 넣는다.**
            //   종전엔 저장이 던져도 로그에만 적고 화면엔 "완료"가 떴다. 저장이 트랜잭션째 롤백되면
            //   **옛 번들이 그대로 남는다** — 지표면은 새 모양인데 기록은 옛 구간이라, 다음 변환이
            //   옛 구간을 읽어 방금 한 변환이 사라지거나 두 번 먹힌다. 원인을 알 길이 없다.
            bool gradeOk = pasteLog.Contains("합성 성공") && !anyMissed && !bundleFailed;

            // ── 토량 산출(체적표면: 원지반=기준, 정지면=비교) ──
            // 합성이 실패했으면 정지면이 온전하지 않아 **틀린 물량이 조용히 나온다** → 아예 계산하지 않는다.
            stw.Stage("토량 산출");
            string volMsg = gradeOk
                ? ComputeVolumes(db, groundId, finalSurfId)
                : "토량: 정지면이 완성되지 않아 산출하지 않았습니다";
            // [다중 구역] 이어서(누적)면 기준이 '직전 누적면'이라 이번 구역분 토량 — 전체 누적은 INFRAWORKS가 원지반 기준으로 계산.
            if (gradeOk && mode == GradeMode.Append)
                volMsg += "\n(이번 구역 기준 — 전체 누적 토공량은 [INFRAWORKS] 토공량.csv)";
            string headline = gradeOk ? "정지면 생성 완료" : "⚠ 정지면 생성 — 확인 필요";
            var box = new System.Text.StringBuilder();
            box.AppendLine(headline);
            box.AppendLine();
            box.AppendLine(volMsg);
            if (!gradeOk)
                box.AppendLine("\n자세한 내용은 진단 로그를 확인하세요:\n" + DiagLog.FilePath);
            string msg = box.ToString().TrimEnd();

            // ★★[JACK 0807 'DH정지면에 스냅샷 재작성 느낌표가 뜬 상태로 작성됨'] **맨 마지막에 한 번 더** 재작성한다.
            //   종전엔 3.5단계에서, 그것도 `결과지표면만 표시` 옵션이 켜져 있을 때만 돌았다 —
            //   옵션이 꺼져 있으면 아예 안 돌고, 켜져 있어도 그 뒤에 토량 임시표면 생성·삭제가 이어져
            //   정지면이 다시 '구식'이 된다. 사용자가 보는 시점은 **모든 작업이 끝난 뒤**이므로 그때 맞춰야 한다.
            string snapMsg = "";
            try
            {
                // ★★[v32.12 · JACK 0812 실험 A·C] <b>표면마다 트랜잭션을 끊는다 — 손으로 누르는 것과 같은 모양으로.</b>
                //   JACK 확인: <i>"무조건 마우스 오른쪽 버튼으로 스냅샷 재작성을 눌러야만 없어져."</i>
                //   같은 호출을 우리도 하고 있는데 결과가 다르다. 남은 차이는 <b>커밋 시점</b> 하나다 —
                //   JACK은 클릭마다 끝나고 커밋되는데, 우리는 소스·합성면·스냅샷을 <b>한 트랜잭션에</b> 몰아넣었다.
                //   ①소스 → ②합성면 → ③스냅샷을 <b>표면 하나마다 열고·하고·커밋</b>한다.
                //   ※ 순서를 바꾸는 것이 아니라 <b>언제 확정되는가</b>를 바꾸는 것이다(§30 금지 12번과 다른 축).
                snapMsg = GradingBuilder.RebuildSurfacesStaged(db);

                // ── ★★[v32.13] <b>맨 마지막에</b> 붙여넣기 줄을 정의에서 지운다.
                //   스냅샷이 형상을 통째로 물고 있으므로 붙여넣기 줄은 잉여이고, 남아서 하는 일은 ⚠를 다는 것뿐이다.
                //   <b>반드시 스냅샷 재작성보다 뒤여야 한다</b> — 지운 뒤에 스냅샷을 다시 구우면 텅 빈 정의가 구워진다.
                //   삼각형 수가 줄면 커밋하지 않으므로 실패해도 도면은 손대기 전 그대로다.
                //   ★★[v32.14 · 자문2 §8] <b>지금은 끈다 — 한 번에 하나만 시험한다.</b>
                //     자문1은 '붙여넣기 삭제를 강력 권장', 자문2는 '재현성이 사라지니 권하지 않는다'로 갈렸다.
                //     <b>되돌릴 수 있는 쪽</b>(스냅샷 지우고 새로 만들기)을 먼저 시험한다.
                //     그것으로 안 되면 이 스위치를 켜면 된다 — 코드는 안전판까지 그대로 살아 있다.
                if (GradeFlags.StripPasteOps)
                    foreach (var baseNm in new[] { "정지면_DH", PureBase })
                    {
                        ObjectId sid;
                        using (var trF = db.TransactionManager.StartTransaction())
                        { sid = GradingBuilder.FindSurfaceByBaseName(trF, baseNm); trF.Commit(); }
                        if (sid.IsNull) { snapMsg += $"\n  붙여넣기 정리: '{baseNm}' 없음"; continue; }
                        GradingBuilder.StripPasteOperations(db, sid, out string sd);
                        snapMsg += "\n  붙여넣기 정리: " + sd;
                    }

                // 진단은 <b>읽기만</b> 한다 — 종전엔 진단이 재작성 함수 안에 섞여 있어
                // '진단하려면 표면을 건드려야' 했고, 그 자체가 상태를 바꿨다.
                using Transaction trD = db.TransactionManager.StartTransaction();
                snapMsg += "\n  " + GradingBuilder.Describe(trD, "정지면_DH");
                snapMsg += "\n  " + GradingBuilder.Describe(trD, PureBase);
                trD.Commit();
            }
            catch (System.Exception rex) { snapMsg = "재작성 실패 — " + rex.Message; }

            // ★★[v32.7b · JACK 0812 계측] <b>커밋한 뒤에 다시 읽는다.</b>
            //   트랜잭션 <b>안</b>에서 깨끗해 보여도 커밋하면서 다시 더러워질 수 있다 —
            //   그러면 <b>우리 로그는 깨끗한데 화면엔 느낌표가 뜬다</b>(실제로 그랬다).
            //   <b>사용자가 보는 것과 같은 시점에서 재는 것</b>만이 믿을 만한 계측이다.
            //   여기서 0이 아니면 원인은 '재작성을 안 해서'가 아니라 <b>커밋 뒤에 누가 더럽히는 것</b>이다.
            try
            {
                using Transaction trV = db.TransactionManager.StartTransaction();
                var bad = new System.Collections.Generic.List<string>();
                int nAll = 0;
                foreach (ObjectId sid in Autodesk.Civil.ApplicationServices.CivilApplication.ActiveDocument.GetSurfaceIds())
                {
                    if (trV.GetObject(sid, OpenMode.ForRead) is not Autodesk.Civil.DatabaseServices.Surface s) continue;
                    nAll++;
                    if (s.IsOutOfDate || (s.HasSnapshot && s.IsSnapshotOutOfDate))
                        bad.Add($"{s.Name}(구식={s.IsOutOfDate}/스냅샷구식={s.IsSnapshotOutOfDate})");
                }
                trV.Commit();
                // ★★[v32.11 · 조사 반영] <b>'깨끗함'이라고 쓰지 않는다 — 그 문구가 조사를 다섯 번 오도했다.</b>
                //   여기서 읽는 <c>IsOutOfDate</c>·<c>IsSnapshotOutOfDate</c>는 <b>지표면 한 장 단위</b> 값이고,
                //   화면의 ⚠는 <b>정의 목록의 한 줄(작업) 단위</b> 표시다. <b>자가 다르다.</b>
                //   Civil 3D 2026 어셈블리에는 작업 단위 구식 여부를 읽는 공개 속성이 <b>없다</b>(조사로 확인).
                //   그러니 이 줄이 0이어도 <b>⚠가 없다는 뜻이 아니다</b> — 판정은 특성 대화상자로만 한다.
                snapMsg += $"\n  [커밋 뒤 확인] 지표면 {nAll}개 중 표면단위 플래그 {bad.Count}개"
                         + (bad.Count > 0 ? " ⚠[" + string.Join(" · ", bad) + "]" : "")
                         + "  ※정의 탭 ⚠와는 다른 값 — 이 숫자로 성공을 판정하지 말 것";
            }
            catch (System.Exception vex) { snapMsg += "\n  [커밋 뒤 확인] 실패 — " + vex.Message; }

            try { DiagLog.Append("\n■ 정지면 마무리 재작성\n  " + snapMsg + "\n"); } catch { }

            // ★[JACK 0807] 정지면 생성/옹벽변환이 어디서 오래 걸렸는지 — 로그 한 줄로 남긴다.
            string gradeTime = stw.Report();
            try { DiagLog.Append("\n■ DoGrade 단계별 시간\n  " + gradeTime + "\n"); } catch { }

            // ★[JACK 0807 '글씨가 엄청나게 생긴다'] 명령창에는 **한눈에 읽히는 만큼만** 낸다.
            //   상세(경계 주입·합성 검증·번들 내역)는 전부 진단 로그 파일에 이미 들어 있다.
            //   ※`GradingSettings.Version`은 이제 짧은 버전 문자열이다 — 변경 이력은 Changelog로 옮겼고
            //     **출력하지 않는다**(종전엔 이 자리에서 68,623자가 통째로 찍혔다).
            string terrace = p.MountainTerrace ? $" · 계단식 산지(대소단 {p.TerraceInterval}m/{p.TerraceWidth}m)" : "";
            ed.WriteMessage("\n" + headline + $"  [DH.Grading {GradingSettings.Version}]" +
                $"\n  {volMsg.Replace("\n", " · ")}" +
                $"\n  절토 1:{p.CutSlope} 단높이 {p.CutBenchHeight}m·소단 {p.CutBenchWidth}m" +
                $" / 성토 1:{p.FillSlope} 단높이 {p.FillBenchHeight}m·소단 {p.FillBenchWidth}m{terrace}" +
                $"\n  {gradeTime}" +
                $"\n  자세한 내용: {DiagLog.FilePath}");

            // ★★★[8단계 검토 · 높음2] <b>도킹창이 떠 있으면 다시 채운다.</b>
            //   터파기는 제 창을 챙기는데(<c>ExcavCommand</c> 끝) <b>정지만 그 한 줄이 없었다</b> —
            //   그래서 리본으로 정지면을 만들면 창의 [옹벽 변환]·[사면 변환]과
            //   [이어서/새로시작]이 <b>회색인 채로 굳었다</b>(도면 탭을 왕복해야 풀렸다).
            //   ★<c>GradingPalette.Refresh()</c>는 8단계에서 호출자가 <b>0개</b>가 됐었다 —
            //     "고침이 한 곳에만 들어간다"(§77 ④)의 거울상이다: 여기서는 <b>한 곳에도</b> 안 들어갔다.
            try { GradingPalette.Refresh(); } catch { }

            // ★[검토 0824 S-1] 저장이 실패했으면 **팝업에도** 적는다 — 로그만 보고 알 수는 없다.
            if (bundleFailed)
                AcadApp.ShowAlertDialog(msg +
                    "\n\n⚠ 이 도면에 정지면 기록(번들)을 남기지 못했습니다.\n" +
                    "지표면은 새로 만들어졌지만 기록은 옷 상태로 남아 있어,\n" +
                    "옥벽·사면 변환이 옷 구간을 읽습니다. 도면을 저장하지 말고 다시 실행하세요.\n\n" +
                    bundleMsg);
            else AcadApp.ShowAlertDialog(msg);
        }
        catch (System.Exception ex)
        {
            // [다중 구역] Append 도중 예외로 정지면_DH가 안 만들어졌으면 기준면 이름 원복(도면 상태 보호).
            TryRestoreBase(db, baseRestoreHandle);
            ed.WriteMessage("\n[DHGRADE 오류] " + ex.Message);
            AcadApp.ShowAlertDialog("가상 지표면 생성 중 오류:\n" + ex.Message);
            try
            {
                DiagLog.Append(
                    "\n■ DoGrade 예외 — " + ex.GetType().Name + ": " + ex.Message + "\n" + ex.StackTrace + "\n");
            }
            catch { }
        }
    }

    /// <summary>[옹벽 유지 0729] 기존 구간과 새 선택 구간 병합 — 새 구간과 '겹치는' 기존 구간은 버림(교체 관례),
    /// 안 겹치는 기존 구간은 유지. 결과 = 새 구간 + 유지된 기존 구간.</summary>
    private static System.Collections.Generic.List<SlopeZone> MergeZones(
        System.Collections.Generic.List<SlopeZone>? existing,
        System.Collections.Generic.List<SlopeZone> newZones,
        System.Collections.Generic.IReadOnlyList<Point3> boundary, double[] cum)
    {
        // ★★[검토 0824 치명-1] **기존이 먼저, 새 것이 나중.**
        //   규칙 합성은 목록 뒤쪽이 이긴다(ResolveAt·ProfOf 둘 다). 그런데 종전엔 새 구간을 앞에 두고
        //   기존을 뒤에 붙여 **옛 구간이 새 선택을 덮었다** — "옹벽을 찍었는데 화면에 안 나온다"가 된다.
        var res = new System.Collections.Generic.List<SlopeZone>();
        if (existing != null)
            foreach (var ez in existing)
            {
                // ★ 겹침은 T 숫자로 보면 안 된다 — 자가 다르면 **서로 다른 축의 눈금**이다
                //   (실측: 한쪽은 둘레 910m 링 축의 798.9, 다른 쪽은 둘레 110m 계획 축).
                //   Compact과 같은 방식으로 **좌표 표본**을 떠서 묻는다.
                bool overlapped = false;
                foreach (var nz in newZones)
                    if (SlopeZone.RegionsOverlap(ez, nz, boundary, cum)) { overlapped = true; break; }
                if (!overlapped) res.Add(ez);
            }
        res.AddRange(newZones);
        return res;
    }

    /// <summary>[진단 0729] 병합 교선 루프 전체를 CSV로 — 진단 로그와 같은 폴더에 DHGRADE_교선덤프_{label}.csv.
    /// 형식: loop,idx,x,y,z (loop=-1은 계획경계). 루프 전멸(생성 실패) 시에만 호출 — 오프라인 재현용.</summary>
    /// <summary>★[JACK 0903] 계단 링 전부를 CSV로 — 오프라인 재현용(형상 무변경).
    /// 열: 링번호, 점번호, X, Y, Z. 링번호가 클수록 바깥이다.</summary>
    private static void DumpRingsCsv(string label, System.Collections.Generic.IReadOnlyList<System.Collections.Generic.List<Point3>> rings)
    {
        try
        {
            if (rings == null || rings.Count == 0) return;
            var sb = new System.Text.StringBuilder("ring,i,x,y,z\n");
            for (int r = 0; r < rings.Count; r++)
            {
                var g = rings[r];
                for (int i = 0; i < g.Count; i++)
                    sb.Append(r).Append(',').Append(i).Append(',')
                      .Append(g[i].X.ToString("F4")).Append(',')
                      .Append(g[i].Y.ToString("F4")).Append(',')
                      .Append(g[i].Z.ToString("F4")).Append('\n');
            }
            string dir = System.IO.Path.GetDirectoryName(DiagLog.FilePath) ?? ".";
            System.IO.File.WriteAllText(System.IO.Path.Combine(dir, $"DHGRADE_계단링_{label}.csv"), sb.ToString());
        }
        catch { }
    }

    private static void DumpLoopsCsv(string label,
        System.Collections.Generic.List<System.Collections.Generic.List<Point3>> loops,
        System.Collections.Generic.List<Point3> boundary, string kind = "교선덤프")
    {
        try
        {
            string dir = System.IO.Path.GetDirectoryName(DiagLog.FilePath) ?? ".";
            var sb = new System.Text.StringBuilder("loop,idx,x,y,z\n");
            for (int i = 0; i < boundary.Count; i++)
                sb.AppendLine(string.Create(System.Globalization.CultureInfo.InvariantCulture,
                    $"-1,{i},{boundary[i].X:F3},{boundary[i].Y:F3},{boundary[i].Z:F3}"));
            for (int l = 0; l < loops.Count; l++)
                for (int i = 0; i < loops[l].Count; i++)
                    sb.AppendLine(string.Create(System.Globalization.CultureInfo.InvariantCulture,
                        $"{l},{i},{loops[l][i].X:F3},{loops[l][i].Y:F3},{loops[l][i].Z:F3}"));
            System.IO.File.WriteAllText(System.IO.Path.Combine(dir, $"DHGRADE_{kind}_{label}.csv"), sb.ToString());
        }
        catch { }
    }

    /// <summary>[다중 구역] Append가 개명해 둔 기준면(정지면_DH이전)을 '정지면_DH'로 되돌린다 —
    /// 중단/예외로 새 정지면이 안 만들어진 경우에만(이미 있으면 두 개가 되므로 건드리지 않음).</summary>
    private static void TryRestoreBase(Database db, string? handleHex)
    {
        if (string.IsNullOrEmpty(handleHex)) return;
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            if (GradingBuilder.SurfaceExistsByBaseName(tr, "정지면_DH")) { tr.Commit(); return; }
            var id = NoriCommand.FindByHandle(db, handleHex);
            if (!id.IsNull && tr.GetObject(id, OpenMode.ForWrite) is Autodesk.Civil.DatabaseServices.Surface s)
                s.Name = GradingBuilder.UniqueName(db, tr, "정지면_DH");
            tr.Commit();
        }
        catch { }
    }

    /// <summary>[0728 — JACK] 교선 루프에서 계획폴리곤 '안' 또는 경계 tol 이내 점 구간을 제거하고
    /// 바깥 둘레 구간(열린 폴리선)만 남긴다 — 부지를 가로지르는 전이선 초록 표시 제거(표시 전용, 번들 무관).</summary>
    /// <summary>★[v30.0] 한 구역의 <b>데이라잇 링</b>을 꺼낸다 — 여러 조각(<c>*FinalRings</c>)이 정본,
    /// 옛 번들은 단수(<c>*FinalRing</c>)로 폴백. 절토·성토 둘 다 모은다.</summary>
    private static System.Collections.Generic.IEnumerable<System.Collections.Generic.List<Point3>>
        DaylightRingsOf(GradingBundle b)
    {
        if (b == null) yield break;
        if (b.CutFinalRings != null) { foreach (var r in b.CutFinalRings) if (r is { Count: >= 2 }) yield return r; }
        else if (b.CutFinalRing is { Count: >= 2 }) yield return b.CutFinalRing;
        if (b.FillFinalRings != null) { foreach (var r in b.FillFinalRings) if (r is { Count: >= 2 }) yield return r; }
        else if (b.FillFinalRing is { Count: >= 2 }) yield return b.FillFinalRing;
    }

    /// <summary>★[v30.0] 링에서 <b>마스크 안에 든 점을 빼고</b> 남은 연속 구간만 조각으로 돌려준다.
    /// <para>뒤 구역이 덮은 자리의 경계선은 <b>최종 지형의 경계가 아니다</b> — 거기는 이미 다시 깎였다.
    /// 옹벽선이 쓰는 <c>TrimBy</c>와 같은 성격의 처리를 점렬에 적용한 것이다.</para></summary>
    private static System.Collections.Generic.List<System.Collections.Generic.List<Point3>>
        TrimOutsideMask(System.Collections.Generic.IReadOnlyList<Point3> ring, GradingPolygons.RegionMask mask)
    {
        var res = new System.Collections.Generic.List<System.Collections.Generic.List<Point3>>();
        var cur = new System.Collections.Generic.List<Point3>();
        foreach (var p in ring)
        {
            if (mask.Contains(p.X, p.Y)) { if (cur.Count >= 2) res.Add(cur); cur = new System.Collections.Generic.List<Point3>(); }
            else cur.Add(p);
        }
        if (cur.Count >= 2) res.Add(cur);
        return res;
    }

    /// <summary>★★[JACK 0827 · 추적 결과] <b>계획선에서 이만큼 안쪽이면 "부지를 가로지르는 선"으로 본다.</b>
    /// <para>종전 0.5m는 <b>진짜 데이라잇을 잘라 먹었다</b>. 구배가 수직에 가까우면(이번 정지는 1:0.01)
    /// 데이라잇이 계획선에서 <b>0.12m밖에</b> 안 떨어지는데, 0.5m 자로 재면 그것까지 지운다 —
    /// 실측으로 링 412점 중 <b>41점·16.8m가 삭제</b>되어 고리가 두 조각으로 갈라졌다.</para>
    /// <para>가로지르는 선은 계획면 <b>한참 안쪽</b>을 지나므로 5cm면 충분히 가려진다.</para></summary>
    private const double PlanNearM = 0.05;

    private static System.Collections.Generic.List<System.Collections.Generic.List<Point3>> FilterOutsidePlan(
        System.Collections.Generic.List<System.Collections.Generic.List<Point3>> loops,
        System.Collections.Generic.List<Point3> plan, double tol)
    {
        bool InsideOrNear(double x, double y)
        {
            int n = plan.Count;
            bool inside = false;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                var a = plan[i]; var b = plan[j];
                if ((a.Y > y) != (b.Y > y) && x < (b.X - a.X) * (y - a.Y) / (b.Y - a.Y + 1e-300) + a.X)
                    inside = !inside;
            }
            if (inside) return true;
            for (int i = 0; i < n; i++)
            {
                var a = plan[i]; var b = plan[(i + 1) % n];
                double ex = b.X - a.X, ey = b.Y - a.Y, l2 = ex * ex + ey * ey;
                double u = l2 < 1e-12 ? 0 : ((x - a.X) * ex + (y - a.Y) * ey) / l2;
                u = u < 0 ? 0 : (u > 1 ? 1 : u);
                double px = a.X + ex * u, py = a.Y + ey * u;
                if ((x - px) * (x - px) + (y - py) * (y - py) <= tol * tol) return true;
            }
            return false;
        }
        // [0728 — JACK] 조각 최소 길이: 필터 후 2m 미만 부스러기(경계 근처 스침 잔여물)는 그리지 않음.
        const double MinRunLen = 2.0;
        bool LongEnough(System.Collections.Generic.List<Point3> run)
        {
            double len = 0;
            for (int i = 0; i + 1 < run.Count; i++)
            {
                double dx = run[i + 1].X - run[i].X, dy = run[i + 1].Y - run[i].Y;
                len += System.Math.Sqrt(dx * dx + dy * dy);
                if (len >= MinRunLen) return true;
            }
            return false;
        }
        var outp = new System.Collections.Generic.List<System.Collections.Generic.List<Point3>>();
        foreach (var loop in loops)
        {
            if (loop == null || loop.Count < 2) continue;
            System.Collections.Generic.List<Point3>? run = null;
            foreach (var q in loop)
            {
                if (!InsideOrNear(q.X, q.Y)) { (run ??= new()).Add(q); }
                else if (run != null) { if (run.Count >= 2 && LongEnough(run)) outp.Add(run); run = null; }
            }
            if (run != null && run.Count >= 2 && LongEnough(run)) outp.Add(run);
        }
        return outp;
    }

    /// <summary>가상표면(ObjectId)을 지운다 — daylight 없는 억지 생성 표면 정리용.</summary>
    /// <summary>★★★[JACK 0903 "구배를 0으로 주고 하면 오류 없이 잘 되는데
    /// 구배를 1.5로 주고 만든 걸 옹벽 변환하면 그런 오류가 생겨"]
    /// <b>초록선(정지경계)이 톱니인지 숫자로 잰다.</b>
    ///
    /// <para><b>톱니는 눈으로만 보이고 로그에는 안 남았다.</b> 지금 로그가 말하는 것은 점 수(695개)뿐이라
    /// 그 점들이 <b>매끈하게</b> 놓였는지 <b>지그재그로</b> 놓였는지는 알 수 없다. 그래서 자를 하나 댄다:
    /// 앞 변과 뒤 변이 <b>90°보다 크게 되꺾이면</b> 한 번 센다. 매끈한 곡선은 한 걸음에 조금씩만 돌아서
    /// 거의 안 걸리고, 옹벽 윗선·아랫선 사이를 왔다 갔다 하면 <b>거의 매 점마다</b> 걸린다.</para>
    ///
    /// <para>가장 심한 자리의 <b>좌표</b>도 남긴다 — 도면에서 바로 그 자리를 볼 수 있게.</para></summary>
    private static string FoldDiag(System.Collections.Generic.List<System.Collections.Generic.List<Point3>> rings)
    {
        try
        {
            if (rings == null || rings.Count == 0) return "";
            int fold = 0, pts = 0; double worst = 1.0, wx = 0, wy = 0;
            double minLen = double.MaxValue, maxLen = 0, mx = 0, my = 0;
            foreach (var r in rings)
            {
                if (r == null || r.Count < 3) continue;
                pts += r.Count;
                // ★[검토 0903] <b>이음매 한 칸이 사각지대였다.</b>
                //   닫힌 링은 첫 점을 끝에 한 번 더 넣는다(SurfaceOutline). 종전 루프는 1..n-2라
                //   <b>마지막 변과 이음매 꼭짓점을 한 번도 안 쟀다</b> — 그런데 이음매는 걷기가 끊겼다
                //   다시 시작하는 자리라 <b>긴 변이 가장 잘 생기는 곳</b>이다.
                //   "접힘 2%로 깨끗하다"는 판정이 이 사각지대 탓일 수 있어 링 전체를 감아서 돈다.
                int n = r.Count;
                bool closed = System.Math.Abs(r[0].X - r[n - 1].X) < 1e-6
                           && System.Math.Abs(r[0].Y - r[n - 1].Y) < 1e-6;
                int m = closed ? n - 1 : n;          // 닫힌 링은 겹친 끝점을 빼고 센다
                int lo = closed ? 0 : 1;
                for (int i = lo; i < (closed ? m : n - 1); i++)
                {
                    int im = closed ? (i - 1 + m) % m : i - 1;
                    int ip = closed ? (i + 1) % m : i + 1;
                    double ax = r[i].X - r[im].X, ay = r[i].Y - r[im].Y;
                    double bx = r[ip].X - r[i].X, by = r[ip].Y - r[i].Y;
                    double la = System.Math.Sqrt(ax * ax + ay * ay), lb = System.Math.Sqrt(bx * bx + by * by);
                    if (la > 1e-9)
                    {
                        if (la < minLen) minLen = la;
                        if (la > maxLen) { maxLen = la; mx = r[i - 1].X; my = r[i - 1].Y; }
                    }
                    if (la < 1e-9 || lb < 1e-9) continue;
                    double cos = (ax * bx + ay * by) / (la * lb);   // 1=직진 · -1=완전히 되꺾임
                    if (cos < 0.0) fold++;
                    if (cos < worst) { worst = cos; wx = r[i].X; wy = r[i].Y; }
                }
            }
            double deg = System.Math.Acos(System.Math.Max(-1.0, System.Math.Min(1.0, worst))) * 180.0 / System.Math.PI;
            double rate = pts > 0 ? fold * 100.0 / pts : 0;
            return $" · 접힘 {fold}곳/{pts}점({rate:F0}%) · 가장 심한 곳 {deg:F0}도 @ {wx:F0},{wy:F0}"
                 + $" · 변길이 {(minLen == double.MaxValue ? 0 : minLen):F3}~{maxLen:F2}m(최장 @ {mx:F0},{my:F0})";
        }
        catch { return ""; }
    }

    /// <summary>★★★[JACK 0903 "옹벽 변환했는데 지표면이 이상하게 작성되는 부분이 발생했어"]
    /// <b>완성된 지표면의 삼각형 변을 직접 잰다 — 증상 자체를 재는 자다.</b>
    ///
    /// <para><b>왜 여기까지 왔나.</b> 초록선(정지경계)을 의심해 접힘을 쟀더니 <b>2%로 깨끗했고</b>,
    /// 링도 처음부터 203점으로 <b>정상적으로 태어났다</b>. 두 가설이 다 죽었으니 남은 것은 <b>면 자체</b>다.
    /// 스샷의 부채꼴은 삼각망이 <b>멀리 떨어진 두 점을 이어</b> 생기는 모양이므로,
    /// <b>비정상적으로 긴 변</b>을 세면 있는지 없는지가 곧바로 나온다.</para>
    ///
    /// <para><b>무겁지 않게.</b> JACK: <i>"중요한 건 도면 수행 시 무거우면 안 되"</i> —
    /// 삼각형 수를 <see cref="ScanCap"/>으로 막고 걸린 시간도 함께 남겨 <b>비용을 눈으로 본다</b>.
    /// 재기만 하고 도면은 건드리지 않는다.</para></summary>
    private const int ScanCap = 400000;

    /// <remarks>★[검토 0903] <b>이름이 아니라 그 면을 받는다.</b> 이름으로 찾으면
    /// 옛 면이 안 지워졌을 때(<c>EraseSurfacesByBaseName</c>은 실패를 삼킨다) 새 면은 <c>_2</c>가 되고
    /// 검사는 <b>옛 면</b>을 잰다 — 그러면 이 자가 낸 숫자를 근거로 쓸 수 없다.</remarks>
    private static string SurfaceEdgeScan(Database db, ObjectId id, string label)
    {
        try
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            if (id.IsNull) return $"\n  [면검사] {label}: 없음";
            using var tr = db.TransactionManager.StartTransaction();
            if (tr.GetObject(id, OpenMode.ForRead) is not Autodesk.Civil.DatabaseServices.TinSurface tin)
            { tr.Commit(); return $"\n  [면검사] {label}: TIN이 아니다"; }

            int n = 0, over = 0; double worst = 0, wx = 0, wy = 0; bool capped = false;
            const double Long = 5.0;   // 등고선 간격 1m — 정상 변은 1~3m다. 5m를 넘으면 먼 점끼리 이어진 것.
            // ★[검토 0903] <b>네이티브 메모리를 닫는다.</b> 삼각형 컬렉션과 삼각형은 둘 다 IDisposable이고,
            //   이 저장소의 다른 자리는 이미 전부 닫고 있다(검토 0901). 안 닫으면 25만 삼각형마다 래퍼가
            //   파이널라이저 대기열에 쌓여, JACK이 겪은 <i>"간혹 느려지다가 리소스가 부족한지 튕긴다"</i>와
            //   같은 압박이 된다.
            using (var tris = tin.GetTriangles(false))
            {
                foreach (Autodesk.Civil.DatabaseServices.TinSurfaceTriangle t in tris)
                {
                    try
                    {
                        if (++n > ScanCap) { capped = true; break; }
                        var a = t.Vertex1.Location; var b = t.Vertex2.Location; var c = t.Vertex3.Location;
                        for (int e = 0; e < 3; e++)
                        {
                            var pp = e == 0 ? a : e == 1 ? b : c;
                            var qq = e == 0 ? b : e == 1 ? c : a;
                            double dx = qq.X - pp.X, dy = qq.Y - pp.Y;
                            double d = System.Math.Sqrt(dx * dx + dy * dy);
                            if (d > Long) over++;
                            if (d > worst) { worst = d; wx = (pp.X + qq.X) / 2; wy = (pp.Y + qq.Y) / 2; }
                        }
                    }
                    finally { t.Dispose(); }
                }
            }
            tr.Commit();
            sw.Stop();
            // ★[검토 0903] 삼각형마다 세 변을 다 세므로 <b>안쪽 변은 두 번 세어진다</b>(바깥 변만 한 번).
            //   숫자를 부풀린 채로 두면 다음 판단이 틀어진다 — 무엇을 센 것인지 그대로 적는다.
            return $"\n  [면검사] {label}: 삼각형 {n}개{(capped ? "(상한에서 멈춤)" : "")}"
                 + $" · {Long:F0}m 넘는 변 {over}회 검사(안쪽 변은 2회 계수) · 최장 {worst:F2}m @ {wx:F0},{wy:F0}"
                 + $" · {sw.ElapsedMilliseconds}ms";
        }
        catch (System.Exception ex) { return $"\n  [면검사] {label}: 못 쟀다 — {ex.Message}"; }
    }

    /// <summary>★[JACK 0914] 선 묶음을 한 레이어에 3D 폴리선으로 그린다 — <b>부를 때마다 먼저 비운다</b>.
    /// <para>면만으로는 안 보이는 것(거의 수직인 벽 등)을 눈으로 확인하려고 쓴다.
    /// 레이어 이름이 "DH-"로 시작하므로 <c>DHRESET</c>이 걷어 간다.</para></summary>
    /// <summary>★[JACK 0917 <i>"옹벽 폴리곤 외 소단에 <b>알 수 없는 빨간선</b>들이 생겼어"</i>]
    /// 옹벽 폴리곤 색 — <b>청록(4)</b>.
    ///
    /// <para>여태 <b>빨강(1)</b>이었는데, <c>DH-소단선-절토</c>가 <b>똑같이 빨강</b>이다
    /// (<c>GradingBuilder.cs</c>의 «("DH-소단선-절토", 1, cutBermLines)») —
    /// 둘이 한 화면에 있으면 <b>어느 것이 옹벽 폴리곤인지 알 수가 없다</b>.
    /// 소단선은 계획부지 생성이 3.5단계에서 그리는 <b>옹벽 전환용 태그</b>라 지울 것은 아니고,
    /// <b>색을 갈라 두는 것</b>이 맞다.</para></summary>
    private const short PolyAci = 4;          // 청록 — 빨강(소단선)·초록(데이라잇)과 안 겹친다

    private static void DrawLinesOnLayer(Database db, Transaction tr,
        System.Collections.Generic.IReadOnlyList<System.Collections.Generic.List<Point3>> segs,
        string layer, short aci)
    {
        GradingBuilder.EnsureLayer(db, tr, layer, aci);
        GradingBuilder.EraseOnLayer(db, tr, layer);
        var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
        var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
        var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
        var layerId = lt.Has(layer) ? lt[layer] : ObjectId.Null;
        foreach (var seg in segs)
        {
            if (seg == null || seg.Count < 2) continue;
            var pl = new Polyline3d();
            if (!layerId.IsNull) pl.LayerId = layerId;
            ms.AppendEntity(pl);
            tr.AddNewlyCreatedDBObject(pl, true);
            foreach (var q in seg)
            {
                var v = new PolylineVertex3d(new Autodesk.AutoCAD.Geometry.Point3d(q.X, q.Y, q.Z));
                pl.AppendVertex(v);
                tr.AddNewlyCreatedDBObject(v, true);
            }
        }
    }

    private static void EraseSurface(Transaction tr, ObjectId id)
    {
        try { if (!id.IsNull && tr.GetObject(id, OpenMode.ForWrite) is Autodesk.AutoCAD.DatabaseServices.Entity e) e.Erase(); }
        catch { }
    }

    /// <summary>토량 산출용 임시 체적표면 이름 — 계산 후 즉시 지우며, 남아 있으면 다음 실행이 청소한다.</summary>
    private const string TempVolumeName = "_DH토량임시";

    /// <summary>토량 산출 — Civil3D 체적표면(기준=원지반, 비교=정지면)을 임시로 만들어 절토/성토/순토량을 읽고 지운다.
    /// 부호 규약: 정지면이 원지반보다 낮으면 절토(파냄), 높으면 성토(쌓음). 순토량 = 성토 − 절토
    /// (양수면 흙이 모자라 반입, 음수면 남아 반출). 팝업에 보여줄 유일한 수치라 실패해도 작업은 계속한다.</summary>
    private static string ComputeVolumes(Database db, ObjectId groundId, ObjectId designId)
    {
        if (groundId.IsNull || designId.IsNull) return "토량: 계산 불가 (표면 없음)";
        ObjectId volId = ObjectId.Null;
        try
        {
            double cut, fill;
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                // 이전 실행이 비정상 종료돼 남은 임시 체적표면이 있으면 먼저 청소(도면 오염 방지).
                GradingBuilder.EraseSurfacesByBaseName(tr, TempVolumeName);
                volId = Autodesk.Civil.DatabaseServices.TinVolumeSurface.Create(
                    GradingBuilder.UniqueName(db, tr, TempVolumeName), groundId, designId);
                var vs = (Autodesk.Civil.DatabaseServices.TinVolumeSurface)tr.GetObject(volId, OpenMode.ForRead);
                var vp = vs.GetVolumeProperties();
                cut = vp.UnadjustedCutVolume;
                fill = vp.UnadjustedFillVolume;
                tr.Commit();
            }
            // 임시 체적표면 제거(도면에 남기지 않음) — 실패해도 수치는 이미 확보.
            try
            {
                using Transaction tr2 = db.TransactionManager.StartTransaction();
                EraseSurface(tr2, volId);
                tr2.Commit();
            }
            catch { }

            double net = fill - cut;
            string netWord = net >= 0 ? "부족(반입)" : "여유(반출)";
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            // ★[JACK 0904] <b>토량을 로그에도 남긴다.</b> 종전엔 팝업뿐이라 창을 닫으면 사라졌고,
            //   그래서 0904에 "가짜 가시를 걷어내 토량이 얼마나 정정됐나"를 <b>잴 수가 없었다</b>.
            //   고침마다 "토량이 안 변했나"를 확인하려면 남아 있어야 한다(진단이력에 판별로 보관된다).
            try
            {
                DiagLog.Append(string.Create(ci,
                    $"\n■ 토량\n  절토 {cut:N0}㎥ · 성토 {fill:N0}㎥ · 순토 {System.Math.Abs(net):N0}㎥({netWord})\n"));
            }
            catch { }
            return string.Create(ci, $"절토량 : {cut,12:N0} ㎥\n성토량 : {fill,12:N0} ㎥\n순토량 : {System.Math.Abs(net),12:N0} ㎥  ({netWord})");
        }
        catch (System.Exception ex)
        {
            try
            {
                using Transaction tr3 = db.TransactionManager.StartTransaction();
                EraseSurface(tr3, volId);
                tr3.Commit();
            }
            catch { }
            return "토량: 계산 실패 — " + ex.Message;
        }
    }

    /// <summary>[0807] 직전 <see cref="BuildParams"/>의 수직 예산 실측 — DoGrade가 로그를 새로 시작한 뒤에 찍는다.
    /// (BuildParams 안에서 바로 쓰면 그 뒤 <c>DiagLog.Reset</c>에 지워진다 — 0807 1차 시도의 실패.)</summary>
    internal static string LastBudgetNote = "";

    /// <summary>설정값을 읽고, 원지반/계획고 표고차로 필요한 최대 단수를 좁혀 매개변수를 만든다(+여유단).</summary>
    public static GradingParams BuildParams(System.Collections.Generic.List<Point3> boundary, CachedGroundSurface ground)
    {
        double designMin = double.MaxValue, designMax = double.MinValue;
        foreach (var v in boundary) { designMin = System.Math.Min(designMin, v.Z); designMax = System.Math.Max(designMax, v.Z); }

        var s = GradingSettings.ToParams();
        int maxBenches = GradingSettings.MaxBenches;
        double maxRise = 0;     // 0 = 표고차를 못 얻음 → GradingGeometry가 종전 식(MaxBenches×단높이)으로 폴백
        double maxRiseCut = 0, maxRiseFill = 0;   // 0 = MaxRise로 폴백(옛 번들과 같은 동작)
        try
        {
            var (gMin, gMax) = ground.ElevationRange();
            double maxDiff = System.Math.Max(System.Math.Abs(gMax - designMin), System.Math.Abs(gMin - designMax));

            // [절성토 분리 0803] 여유 단수 — 기본 2단 + 대소단이 사면을 바깥으로 밀어내는 만큼 추가.
            int spare = 2;
            if (GradingSettings.MountainTerrace && GradingSettings.TerraceInterval > 1e-6)
                spare += (int)System.Math.Floor(maxDiff / GradingSettings.TerraceInterval) + 2;

            // 수직 예산 = 표고차 + 여유(큰 쪽 단높이 기준). 단높이와 무관한 실제 지형 값이라
            //   절토·성토 어느 쪽도 상대의 단높이 때문에 잘리지 않는다.
            //   절토=성토면 링 개수가 종전(needed×단높이)과 정확히 같다 — ceil(maxDiff/H)+spare단. 회귀 없음.
            maxRise = maxDiff + spare * System.Math.Max(s.LargerBenchHeight, 1e-6);

            // 단수는 '작은 쪽' 단높이 기준(작은 쪽이 같은 표고차에 더 많은 단을 쓴다) — 무한루프 백스톱용.
            int needed = (int)System.Math.Ceiling(maxDiff / System.Math.Max(s.SmallerBenchHeight, 1e-6)) + spare;
            maxBenches = System.Math.Min(maxBenches, System.Math.Max(needed, 1));

            // ★[JACK 0807 '옹벽변환이 여전히 오래 걸린다'] 이 예산 하나가 **절토·성토 양쪽에 같이** 적용된다.
            //   그런데 실제로 필요한 높이는 방향마다 다르다:
            //     · 절토는 계획고에서 **위로** 원지반 꼭대기까지  → gMax − designMin
            //     · 성토는 계획고에서 **아래로** 원지반 바닥까지  → designMax − gMin
            //   산을 낀 부지처럼 한쪽이 압도적으로 크면, 작은 쪽이 큰 쪽 예산을 그대로 받아 **필요 없는 단**을
            //   수십 개 만든다(0807 현장 로그: 절토 계단 +224m, 성토 계단 −224m로 완전 대칭).
            //   단이 늘면 링·삼각형·옹벽선·판넬이 전부 그만큼 늘어난다 — 정지면 생성 시간의 유력 후보다.
            //   ※다만 MaxRise는 **번들에 저장되는 값**이라 방향별로 쪼개면 저장형식이 바뀐다(v9→v10).
            //     추측으로 형식을 건드리지 않는다 — 먼저 **숫자를 남겨** 실제로 남아도는지 확인하고 고친다.
            //   ※로그에 **바로 쓰지 않는다** — BuildParams는 DiagLog.Reset(진단 로그 새로 시작)보다 먼저 불리므로
            //     여기서 쓰면 그대로 지워진다(0807 1차 시도가 이 이유로 한 줄도 안 남았다). 담아 뒀다 나중에 쓴다.
            double needCut = gMax - designMin, needFill = designMax - gMin;
            // ★★★[JACK 0826] <b>방향별로 예산을 나눈다</b> — 0807에 미뤄 뒀던 그 수정이다.
            //   깎는 쪽은 needCut, 쌓는 쪽은 needFill만 있으면 땅에 닿는다.
            //   한 값을 같이 쓰면 작은 쪽이 큰 쪽 예산만큼 <b>허공에 계단</b>을 쌓고,
            //   그 헛단을 횡단 수량이 계획면으로 읽어 <b>있지도 않은 성토</b>가 잡힌다(실측 2000㎡).
            //   ※<b>번들 저장형식은 안 바뀐다</b> — 이 둘은 담지 않는 파생값이라,
            //     옛 도면을 열면 0이 되어 <c>MaxRise</c>로 물러나 종전과 똑같이 돈다.
            double spareM = spare * System.Math.Max(s.LargerBenchHeight, 1e-6);
            maxRiseCut = System.Math.Max(needCut, 0) + spareM;
            maxRiseFill = System.Math.Max(needFill, 0) + spareM;
            LastBudgetNote =
                $"[수직 예산] 원지반 {gMin:F1}~{gMax:F1}m · 계획 {designMin:F1}~{designMax:F1}m" +
                $" → 필요 절토 {needCut:F1}m / 성토 {needFill:F1}m" +
                $" · 배정 절토 {maxRiseCut:F1}m / 성토 {maxRiseFill:F1}m(★방향별) · 최대 {maxBenches}단" +
                (System.Math.Abs(maxRiseCut - maxRiseFill) > 1e-6
                    ? "  (예산을 방향별로 나눴다 — 헛단 없음)"
                    : "");
        }
        catch (System.Exception ex)
        {
            // 표고 범위를 못 얻으면 설정값 그대로 — 다만 조용히 넘어가지 않는다(사면이 잘려도 단서가 없어짐).
            DiagLog.Append($"\n[BuildParams] 원지반 표고범위 실패 — 수직 예산 미산출, MaxBenches {maxBenches}단 폴백. {ex.Message}\n");
        }

        return new GradingParams
        {
            CutBenchHeight = s.CutBenchHeight,
            FillBenchHeight = s.FillBenchHeight,
            CutBenchWidth = s.CutBenchWidth,
            FillBenchWidth = s.FillBenchWidth,
            CutSlope = s.CutSlope,
            FillSlope = s.FillSlope,
            CellSize = s.CellSize,
            MaxBenches = maxBenches,
            MaxRise = maxRise,
            MaxRiseCut = maxRiseCut,
            MaxRiseFill = maxRiseFill,
            VertexSpacing = s.VertexSpacing,
            MinSlope = s.MinSlope,
            WallGateSlope = GradingSettings.WallGateSlope,   // ★[JACK 0825] 판정 문턱은 동결값(번들에 안 담는다)
            MinFaceRun = s.MinFaceRun,
            MiterConvex = s.MiterConvex,
            MiterLimit = s.MiterLimit,
            MountainTerrace = s.MountainTerrace,
            TerraceInterval = s.TerraceInterval,
            TerraceWidth = s.TerraceWidth,
            NoPlatform = GradingSettings.NoPlatform,   // ★[검토 0911] 종전엔 빠져 있어 Append로는 켤 수 없었다
            // ★★★[JACK 0820 '단높이를 2m로 바꿔도 5m로 쳐져'] **여기서 규칙이 버려지고 있었다.**
            //   BuildParams는 마지막에 GradingParams를 <b>필드별로 새로 만들어</b> 돌려준다.
            //   단높이 규칙을 이 목록에 안 넣으면, 앞에서 아무리 잘 전달해도 <b>여기서 조용히 사라진다</b>
            //   (로그: "규칙 없음"). 값이 안 들어간 게 아니라 <b>중간에서 떨어뜨린</b> 것이었다.
            //   ※필드별 복사는 새 필드가 생길 때마다 이렇게 샌다 — 새 필드를 추가하면 이 목록도 같이 봐야 한다.
            CutBenchSteps = new System.Collections.Generic.List<(int, double)>(s.CutBenchSteps),
            FillBenchSteps = new System.Collections.Generic.List<(int, double)>(s.FillBenchSteps),
        };
    }
}
