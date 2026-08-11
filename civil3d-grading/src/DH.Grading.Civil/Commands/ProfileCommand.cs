using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;
using CivilApp = Autodesk.Civil.ApplicationServices;
using CivilDb = Autodesk.Civil.DatabaseServices;

namespace DH.Grading.Civil.Commands;

/// <summary>
/// ★[종단도 — JACK 0807] <b>버튼을 누르면 노선을 직접 그리고</b>, 그 노선을 따라 종단면도를 만든다(DHPROFILE).
/// <para>
/// 종전 <see cref="SectionCommand"/>(DHSECTION)는 <b>이미 그려진 선을 골라야</b> 했다 —
/// 종단을 뽑으려면 먼저 다른 명령으로 선을 그려 두어야 해서 손이 두 번 갔다.
/// JACK 0807: "버튼을 누르면 선을 직접 그리게 하고 그 노선을 따라 만들어지는 걸로 바꿀 거야. 선은 노란색으로."
/// </para>
/// 흐름: 버튼 → 점을 연달아 찍고 Enter(노란 꺾은 선) → 선형 → 종단(원지반·정지면) → 종단도 놓을 자리 클릭.
/// <para>
/// 그린 노란 선은 <b>도면에 남긴다</b>(JACK 확정) — 어느 선으로 만들었는지 나중에 확인하고,
/// 그 선을 고쳐 다시 돌릴 수 있다. 선형 생성 API가 원본을 지워버리므로 <b>사본</b>을 만들어 그것으로 선형을 만든다.
/// </para>
/// 횡단도는 아직 DHSECTION에 있다 — 종단도가 말끔해진 뒤 같은 방식으로 옮긴다(JACK 0807 확정: 버튼을 나눈다).
/// </summary>
public sealed class ProfileCommand
{
    /// <summary>사용자가 그린 노선이 놓이는 레이어 — <b>노란색</b>(JACK 지정).</summary>
    internal const string LayerRoute = "DH-종단노선";
    /// <summary>부지정지가 그려 두는 <b>데이라잇</b>(계획면이 원지반과 만나는 선) 레이어 —
    /// <c>GradingBuilder.DrawDaylight</c>가 이 이름으로 그린다. 굴곡부 판정의 출처다.</summary>
    private const string LayerDaylight = "DH-정지경계";
    private const string LayerClip = "DH-클립경계";
    /// <summary>측점 라벨 자리 전용 <b>체인 종단</b> — 값은 쓰지 않는다(<see cref="BuildLabelChain"/>).</summary>
    /// <summary>이번 실행에서 만든 측점 라벨용 체인 — 밴드 배선이 이걸 종단1로 쓴다.
    /// <para>★★[v29.0 점검 반영 · 치명] <b>실행 시작 때 반드시 비운다.</b> 종전엔 안 비워서,
    /// 이번 판이 일찍 실패하면 <b>지난 판(또는 다른 도면)의 ID</b>가 그대로 남았다.
    /// 그 선형은 이미 지워졌으니 죽은 번호인데 <c>IsNull</c> 검사는 통과한다 —
    /// 그러면 측점 행 배선이 통째로 실패하고, 최악에는 <b>다른 선형의 종단</b>이 꽂힌다.
    /// 쓸 때는 <see cref="AliveChain"/>로 <b>살아 있는지·이 선형 것인지</b>까지 확인한다.</para></summary>
    private static ObjectId LastLabelChainId = ObjectId.Null;

    /// <summary>체인이 <b>이번 도면에 살아 있고 이 선형에 딸린 것</b>인지 확인한다.
    /// 하나라도 아니면 Null을 돌려준다 — 죽은 번호를 꽂느니 안 꽂는 게 낫다.</summary>
    private static ObjectId AliveChain(Database db, ObjectId alignId)
    {
        if (LastLabelChainId.IsNull) return ObjectId.Null;
        try
        {
            if (LastLabelChainId.Database != db) return ObjectId.Null;
            using var tr = db.TransactionManager.StartTransaction();
            var o = tr.GetObject(LastLabelChainId, OpenMode.ForRead, false);
            bool ok = o is CivilDb.Profile p && !o.IsErased && p.AlignmentId == alignId;
            tr.Commit();
            return ok ? LastLabelChainId : ObjectId.Null;
        }
        catch { return ObjectId.Null; }
    }
    private const string ChainProfileName = "DH_측점체인";
    private const string ChainStyleName = "DH_측점체인(숨김)";
    private const string LayerChain = "DH-측점체인(숨김)";
    private const short YellowIndex = 2;          // AutoCAD 색인 2 = 노랑
    /// <summary>★[JACK 0807] DHT.dwt(회사 표준)에서 심어 오는 종단도 스타일 이름 — 템플릿의 실제 이름 그대로.</summary>
    private const string ViewStyleName = "DH_종단 뷰";
    private const string BandStyleName = "DH_종단 뷰_횡단 데이터_누가거리";

    [CommandMethod("DHPROFILE")]
    public void Run()
    {
        Document doc = AcadApp.DocumentManager.MdiActiveDocument;
        if (doc == null) return;
        GradingSettings.SyncToDocument(doc);
        Editor ed = doc.Editor;
        Database db = doc.Database;
        try { Body(db, ed); }
        catch (System.Exception ex)
        {
            ed.WriteMessage("\n[종단도 오류] " + ex.Message);
            AcadApp.ShowAlertDialog("종단도 생성 중 오류:\n" + ex.Message);
            try { DiagLog.Append($"\n■ DHPROFILE 예외 — {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}\n"); } catch { }
        }
    }

    private static void Body(Database db, Editor ed)
    {
        var cdoc = CivilApp.CivilApplication.ActiveDocument;
        var log = new System.Text.StringBuilder();
        // ★★[v29.0 점검 반영 · 치명] 지난 판의 체인 ID가 넘어오지 않게 <b>맨 먼저 비운다</b>.
        LastLabelChainId = ObjectId.Null;

        // ── ① 대상 지표면 ────────────────────────────────────────────────────
        var surfs = SectionCommand.FindSurfaces(db, cdoc);
        if (surfs.Count == 0)
        {
            SectionCommand.Refuse(ed, "종단도를 만들 지표면이 없습니다.\n\n" +
                                      "먼저 [서버지표면]으로 원지반을 만들거나 [부지정지]를 실행하세요.");
            return;
        }
        log.AppendLine("대상 지표면: " + string.Join(" · ", surfs.ConvertAll(s => s.Label + "=" + s.SurfName)));

        // ── ② 이전 종단도 정리 여부 ──────────────────────────────────────────
        //   [JACK 0807] 무조건 지우지 않는다 — 여러 노선을 놓고 비교하고 싶을 수 있다. 물어본다.
        int prev = CountExisting(db, cdoc);
        if (prev > 0)
        {
            var kw = new PromptKeywordOptions($"\n이미 만든 종단도가 {prev}개 있습니다. 지우고 새로 만들까요? ");
            kw.Keywords.Add("지우고새로", "Y", "지우고새로(Y)");
            kw.Keywords.Add("남겨두고추가", "N", "남겨두고추가(N)");
            kw.Keywords.Default = "지우고새로";
            kw.AllowNone = true;
            var kr = ed.GetKeywords(kw);
            if (kr.Status != PromptStatus.OK && kr.Status != PromptStatus.None) return;
            if (kr.Status == PromptStatus.None || kr.StringResult == "지우고새로")
            {
                int erased = EraseExisting(db, cdoc);
                log.AppendLine($"이전 종단도 정리: {erased}개 지움");
                ed.WriteMessage($"\n  · 이전 종단도 {erased}개를 지웠습니다.");
            }
            else log.AppendLine($"이전 종단도 {prev}개 유지(추가 생성)");
        }

        // ── ③ 노선 직접 그리기 ───────────────────────────────────────────────
        ObjectId routeId = DrawRoute(db, ed, out int nPts, out double routeLen);
        if (routeId.IsNull) return;                    // 취소
        if (routeLen < 1.0)
        {
            SectionCommand.EraseQuiet(db, routeId);
            SectionCommand.Refuse(ed, $"노선이 너무 짧습니다({routeLen:F2}m). 1m 이상으로 그려 주세요.");
            return;
        }
        log.AppendLine($"노선 직접 그리기: 점 {nPts}개 · 길이 {routeLen:F1}m (레이어 {LayerRoute}, 노랑)");
        ed.WriteMessage($"\n[종단도] 노선 {routeLen:F1}m · 점 {nPts}개");

        // ── ④ 선형 ───────────────────────────────────────────────────────────
        //   선형 생성 API는 원본 폴리선을 지워버린다 → **사본**을 만들어 그것을 소모시키고
        //   JACK이 그린 노란 선은 도면에 남긴다(JACK 0807 확정).
        ObjectId alignLayer;
        using (var tr = db.TransactionManager.StartTransaction())
        { alignLayer = SectionCommand.EnsureLayer(db, tr, SectionCommand.LayerAlign, 4); tr.Commit(); }

        ObjectId flatId = SectionCommand.MakeFlatCopy(db, routeId, alignLayer, out int nv, out double flatLen);
        if (flatId.IsNull)
        {
            SectionCommand.Refuse(ed, "노선 사본을 만들지 못했습니다.");
            return;
        }

        string alignName = SectionCommand.UniqueName(db, cdoc, SectionCommand.AlignBase);
        ObjectId alignId;
        try
        {
            var plo = new CivilDb.PolylineOptions
            {
                PlineId = flatId,
                EraseExistingEntities = true,          // 지워지는 건 사본 — 노란 선은 남는다
                AddCurvesBetweenTangents = false,
            };
            alignId = CivilDb.Alignment.Create(
                cdoc, plo, alignName, ObjectId.Null, alignLayer,
                SectionCommand.PickStyle(db, cdoc.Styles.AlignmentStyles, "기본", "Standard", "Basic"),
                SectionCommand.PickStyle(db, cdoc.Styles.LabelSetStyles.AlignmentLabelSetStyles,
                                         "_없음", "None", "표준", "Standard"));
        }
        catch (System.Exception ex)
        {
            SectionCommand.EraseQuiet(db, flatId);
            SectionCommand.Refuse(ed, "노선(선형)을 만들지 못했습니다.\n" + ex.Message);
            return;
        }
        log.AppendLine($"선형 '{alignName}' 생성");

        // ── ④-b ★[JACK 0811] <b>"측점은 20m 간격으로 하고, 주측점은 No.1 같이, 보조는 +00.00 형태로."</b>
        //   <c>No.</c>가 몇 m마다 하나씩 올라가는지는 <b>선형의 측점 색인 증분</b>이 정한다.
        //   지금은 그 값이 커서 노선 전체가 <c>No.0</c> 하나로 묶여 있었다 —
        //   그래서 굴곡부마다 'No.0'만 찍혔다(JACK: "측점값이 0이야").
        //   횡단 간격과 같은 값으로 맞춘다: 20m면 20m에서 No.1, 40m에서 No.2가 된다.
        try
        {
            using var trIdx = db.TransactionManager.StartTransaction();
            if (trIdx.GetObject(alignId, OpenMode.ForWrite) is CivilDb.Alignment alIdx)
            {
                double before = alIdx.StationIndexIncrement;
                double want = System.Math.Max(1.0, GradingSettings.XsecInterval);
                alIdx.StationIndexIncrement = want;
                double after = alIdx.StationIndexIncrement;
                log.AppendLine($"측점 색인 증분: {before:0.##}m → {after:0.##}m (No.가 {after:0.##}m마다 하나씩)" +
                               (System.Math.Abs(after - want) > 1e-6 ? "  ⚠넣은 값과 다르다" : ""));
            }
            trIdx.Commit();
        }
        catch (System.Exception ex) { log.AppendLine("측점 색인 증분 설정 실패 — " + ex.Message); }

        // ── ⑤ 종단(원지반·정지면) ────────────────────────────────────────────
        // ★[JACK 0807 2단계] 회사 표준 스타일을 **먼저** 도면에 심는다 — 종단·종단뷰·밴드가 모두 이걸 쓴다.
        //   심는 게 늦으면 종단이 기본 스타일로 만들어져 나중에 다시 바꿔 줘야 한다.
        ProfileStyleTemplate.Import(db, cdoc);
        log.AppendLine(ProfileStyleTemplate.LastReport);
        // ★★[v27.0] 들여오기가 <b>횡단 데이터 밴드 스타일만은 엉뚱한 서랍</b>에 넣는다(실측).
        //   맞는 서랍(종단 뷰▸밴드▸횡단 데이터)에 같은 이름으로 만들어 속을 옮긴다.
        string sect = ProfileStyleTemplate.EnsureProfileSectionalBandStyles(db, cdoc);
        log.AppendLine(sect);
        ed.WriteMessage("\n  · " + sect);
        // ★[JACK 0807] 로그파일에만 남기면 확인이 안 된다("명령창에 네가 이야기한 것들은 뜨지 않았어") — 명령창에도 찍는다.
        ed.WriteMessage("\n  · " + ProfileStyleTemplate.LastReport);
        if (ProfileStyleTemplate.LastProbe.Length > 0)
        {
            log.AppendLine(ProfileStyleTemplate.LastProbe);
            ed.WriteMessage("\n  · (계측 상세는 로그 파일에 기록됨)");
        }

        ObjectId profStyle = SectionCommand.PickStyle(db, cdoc.Styles.ProfileStyles, "기본", "Standard", "Basic");
        ObjectId profLabels = SectionCommand.PickStyle(db, cdoc.Styles.LabelSetStyles.ProfileLabelSetStyles,
                                                      "_없음", "None", "표준", "Standard");
        int nProf = 0;
        // ★[JACK 0807] 밴드에 **원지반/계획지반을 자동 지정**하려면 만든 종단의 ObjectId를 들고 있어야 한다.
        ObjectId pidGround = ObjectId.Null, pidPad = ObjectId.Null;
        foreach (var s in surfs)
        {
            try
            {
                var pid = CivilDb.Profile.CreateFromSurface(s.ProfileName, alignId, s.SurfId, alignLayer, profStyle, profLabels);
                if (s.Label == "원지반") pidGround = pid; else pidPad = pid;
                nProf++;
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n  · 종단 '{s.ProfileName}' 생성 실패 — {ex.Message}");
                log.AppendLine($"  ⚠종단 '{s.ProfileName}' 실패 — {ex.Message}");
            }
        }
        if (nProf == 0)
        {
            // ★[JACK 0807] 실패로 빠질 때 **로그를 남기고** 나간다 — v21.6에서 실패했는데 로그에 아무 기록이
            //   없어 원인을 도면 밖에서 찾을 수 없었다. 실패한 판이야말로 기록이 필요하다.
            Finish(ed, log, "종단 생성 실패 — 위 사유 참조", quiet: true);
            SectionCommand.Refuse(ed, "종단을 하나도 만들지 못했습니다.\n노선이 지표면 범위 밖일 수 있습니다.");
            return;
        }
        log.AppendLine($"종단 {nProf}개 생성");

        // ── ⑤-b ★★[v25.0 · JACK 0811 확정] <b>측점을 정하고 단면검토선으로 심는다.</b>
        //
        //   <b>왜 단면검토선인가.</b> 그동안 측점이 계속 어긋난 근본 이유는, 밴드마다 측점을 찍는
        //   원천(증분·굴곡부·시작끝)이 <b>제각각</b>이었기 때문이다. 규칙을 아무리 다듬어도 원천이
        //   여럿인 한 열이 안 맞는다. 그런데 Civil에는 <b>'횡단 데이터' 밴드</b>가 있고, 그건
        //   <b>단면검토선이 있는 자리에만</b> 눈금과 값을 찍는다 —
        //   <b>여섯 칸이 한 목록을 보므로 열이 어긋날 수가 없다.</b>
        //
        //   ※ DHT 템플릿의 토공 세트는 <b>원래 6칸 전부 '횡단 데이터'</b>였다(0810 실측).
        //     단면검토선이 없어서 우리가 '종단 데이터'로 바꿔 끼웠고, 거기서부터 어긋나기 시작했다.
        //     이제 원래 설계대로 되돌린다.
        //
        //   덤: 측점이 <b>눈에 보이는 객체</b>가 된다. 프로그램이 잘못 잡으면 도면에서 지우거나
        //   옮기면 되고, 종단도와 횡단면도가 <b>같은 그룹</b>이라 저절로 함께 따라온다
        //   (JACK 0810: "종단에 있는 체인은 다 횡단면도가 그려져야 해").
        double bandIv = System.Math.Max(1.0, GradingSettings.XsecInterval);
        ObjectId slGroupId = BuildSampleLines(db, ed, alignId, pidGround, pidPad, surfs, bandIv, log);

        // ── ⑥ 종단도 배치 ───────────────────────────────────────────────────
        var pvPt = ed.GetPoint("\n[종단도] 종단면도를 놓을 위치 클릭 (Esc=종단만 만들고 끝): ");
        if (pvPt.Status != PromptStatus.OK)
        {
            log.AppendLine("종단도 배치 건너뜀(사용자 취소)");
            Finish(ed, log, $"선형 '{alignName}' · 종단 {nProf}개 생성(종단도 배치는 건너뜀)");
            return;
        }
        try
        {
            var pvId = CivilDb.ProfileView.Create(alignId, pvPt.Value.TransformBy(ed.CurrentUserCoordinateSystem));
            log.AppendLine("종단면도 배치 완료");
            string sty = ApplyViewStyle(db, cdoc, pvId, pidGround, pidPad, slGroupId, surfs, ed, log);
            log.AppendLine(sty);
            ed.WriteMessage("\n  · " + sty);   // ★[JACK 0807] 명령창에서 바로 확인되게

            // ★[JACK 0810] "도곽 버튼이 왜 필요하지? 그냥 종단도 누르면 모형탭하고 배치까지 자동으로 되야 되."
            //   버튼을 늘리지 않고 여기서 끝까지 간다 — 모형 도곽 범위 + 배치 한 장까지.
            string sheet = SheetCommand.Build(db, ed, pvId, log);
            log.AppendLine("도곽: " + sheet);
            ed.WriteMessage("\n  · 도곽: " + sheet);
        }
        catch (System.Exception ex)
        {
            log.AppendLine("⚠종단면도 실패 — " + ex.Message);
            AcadApp.ShowAlertDialog("종단면도를 만들지 못했습니다.\n" + ex.Message +
                                    "\n\n선형과 종단은 만들어졌으니 Civil3D 기본 기능으로도 배치할 수 있습니다.");
        }

        Finish(ed, log, $"노선 {routeLen:F0}m · 선형 '{alignName}' · 종단 {nProf}개 · 종단도 배치 완료");
    }


    /// <summary>★★[v28.0 · JACK 0811 확정] <b>측점 라벨 전용 '체인 종단' — 값은 안 쓰고 자리만 쓴다.</b>
    ///
    /// <para><b>왜 필요한가.</b> JACK 요구: <i>정측점은 <c>No.1</c>, 그 외는 <c>+06.41</c>.</i>
    /// 그런데 <b>한 밴드의 라벨 형식은 하나뿐</b>이라 자리에 따라 글자를 바꿀 수 없다.
    /// 횡단 데이터 밴드의 '증분 라벨'로 갈라 보려 했으나, 실측 결과 그 라벨이 쓸 수 있는 항목은
    /// <b>'이전 단면검토선과의 거리'와 토량뿐</b>이라 측점도 표고도 못 찍는다(JACK 확인). 막혔다.</para>
    ///
    /// <para><b>되는 길.</b> <b>측점 행만 '종단 데이터' 밴드로</b> 바꾼다. 그 종류는 원래
    /// <b>주 증분</b>(20m → <c>No.1</c>)과 <b>굴곡부</b>(→ <c>+06.41</c>)를 <b>따로</b> 찍는다 —
    /// 자리가 다르니 형식도 다르게 줄 수 있다. 측점 행은 <b>값이 필요 없으므로</b>
    /// 예전에 문제였던 '표고를 보간해서 읽는다'는 걱정이 아예 없다.</para>
    ///
    /// <para>그래서 이 종단은 <b>보이지 않게</b> 만들고 PVI를 <b>20m 배수가 아닌 측점</b>에만 심는다.
    /// 20m 자리는 주 증분이 맡으므로 넣으면 두 번 찍힌다.
    /// 값 다섯 행은 그대로 <b>단면검토선</b>에서 읽으므로 측점은 여전히 한 줄로 선다.</para>
    /// 반환=만든 체인 종단(실패하면 Null).</summary>
    private static ObjectId BuildLabelChain(Database db, ObjectId alignId, ObjectId padId,
                                            System.Collections.Generic.List<StationMarks.Mark> all,
                                            double major, System.Text.StringBuilder log)
    {
        if (padId.IsNull || all == null || all.Count == 0) return ObjectId.Null;
        try
        {
            var pts = new System.Collections.Generic.List<double>();
            foreach (var m in all)
                if (System.Math.Abs(m.Station - System.Math.Round(m.Station / major) * major) > 1e-6)
                    pts.Add(m.Station);
            if (pts.Count == 0) { log.AppendLine("측점 라벨용 체인: 20m 아닌 측점이 없어 만들지 않음"); return ObjectId.Null; }

            ObjectId styId = EnsureHiddenProfileStyle(db, log);
            var cdoc = CivilApp.CivilApplication.ActiveDocument;
            if (styId.IsNull) styId = SectionCommand.PickStyle(db, cdoc.Styles.ProfileStyles, "기본", "Standard", "Basic");

            ObjectId lay;
            using (var tr = db.TransactionManager.StartTransaction())
            { lay = SectionCommand.EnsureLayer(db, tr, LayerChain, 8); tr.Commit(); }

            // ★[v28.2 실측] <c>labelSetId</c>에 <c>ObjectId.Null</c>을 주면 <b>거절당한다</b> —
            //   "Object id of ProfileLabelSetStyle is expected". 실제 라벨 세트를 골라 준다
            //   ('_없음'이 있으면 그것 — 이 종단은 안 보이는 선이라 라벨이 필요 없다).
            ObjectId labelSet = SectionCommand.PickStyle(db, cdoc.Styles.LabelSetStyles.ProfileLabelSetStyles,
                                                        "_없음", "None", "표준", "Standard");
            ObjectId chainId = ObjectId.Null; string nm = ChainProfileName, err = null;
            for (int n = 0; n < 20 && chainId.IsNull; n++)
            {
                nm = n == 0 ? ChainProfileName : $"{ChainProfileName}-{n}";
                try { chainId = CivilDb.Profile.CreateByLayout(nm, alignId, lay, styId, labelSet); }
                catch (System.Exception ex) { err = ex.Message; }
            }
            if (chainId.IsNull) { log.AppendLine("측점 라벨용 체인 생성 실패 — " + err); return ObjectId.Null; }

            int made = 0, bad = 0;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var chain = (CivilDb.Profile)tr.GetObject(chainId, OpenMode.ForWrite);
                var pad = (CivilDb.Profile)tr.GetObject(padId, OpenMode.ForRead);
                foreach (double s in pts)
                {
                    try { chain.PVIs.AddPVI(s, pad.ElevationAt(s)); made++; }
                    catch { bad++; }
                }
                tr.Commit();
            }
            log.AppendLine($"측점 라벨용 체인 '{nm}' — PVI {made}개(20m 배수 제외){(bad > 0 ? $" · 실패 {bad}개" : "")}"
                         + "  ※값은 안 쓰고 <b>라벨 자리</b>로만 쓴다");
            return chainId;
        }
        catch (System.Exception ex) { log.AppendLine("측점 라벨용 체인 실패 — " + ex.Message); return ObjectId.Null; }
    }

    /// <summary>안 보이는 종단 스타일 — 선·곡선 표시를 전부 끈다(체인은 라벨 자리 용도다).</summary>
    private static ObjectId EnsureHiddenProfileStyle(Database db, System.Text.StringBuilder log)
    {
        try
        {
            var col = CivilApp.CivilApplication.ActiveDocument.Styles.ProfileStyles;
            ObjectId id;
            try { id = col[ChainStyleName]; } catch { id = col.Add(ChainStyleName); }
            using var tr = db.TransactionManager.StartTransaction();
            if (tr.GetObject(id, OpenMode.ForWrite) is CivilDb.Styles.ProfileStyle ps)
                foreach (var t in System.Enum.GetValues(typeof(CivilDb.Styles.ProfileDisplayStyleProfileType)))
                    try { using var ds = ps.GetDisplayStyleProfile((CivilDb.Styles.ProfileDisplayStyleProfileType)t); ds.Visible = false; }
                    catch { }
            tr.Commit();
            return id;
        }
        catch (System.Exception ex) { log.AppendLine("체인 스타일 실패 — " + ex.Message); return ObjectId.Null; }
    }

    /// <summary>★★[v25.2] <b>횡단 데이터 밴드에 '무엇을 읽을지'를 꽂는다 — 그리고 되읽어 확인한다.</b>
    /// <para><c>DataSourceId</c>가 어떤 객체를 받는지 문서에 없다. 그래서 <b>단면검토선 그룹 → 지표면</b>
    /// 순으로 넣어 보고, 붙은 것을 로그에 <b>객체 종류와 이름까지</b> 남긴다. 한 판이면 확정된다.</para>
    /// <para>덤으로 이 밴드가 <b>어떤 표현식</b>을 쓰는지도 찍는다 — 표가 비었을 때
    /// '데이터가 없는 것'인지 '표현식이 딴 걸 가리키는 것'인지 가르는 유일한 단서다.</para></summary>
    private static string WireSectionalBand(Transaction tr, CivilDb.ProfileViewBandItem item, string bandName,
                                            ObjectId slGroupId, ObjectId pidGround, ObjectId pidPad,
                                            System.Text.StringBuilder log, int idx)
    {
        string Who(ObjectId id)
        {
            if (id.IsNull) return "없음";
            try
            {
                var o = tr.GetObject(id, OpenMode.ForRead);
                string n = ""; try { n = (o as CivilDb.Entity)?.Name ?? ""; } catch { }
                return $"{o.GetType().Name}{(n.Length > 0 ? ":" + n : "")}";
            }
            catch (System.Exception ex) { return "읽기실패:" + ex.GetType().Name; }
        }

        var sb = new System.Text.StringBuilder();
        // ── ① 손대기 전 상태
        string was = "?", mat = "?", maxOff = "?";
        try { was = Who(item.DataSourceId); } catch (System.Exception ex) { was = "예외:" + ex.GetType().Name; }
        try { mat = item.MaterialName ?? "(null)"; } catch { }
        try { maxOff = item.MaxOffsetDistance.HasValue ? item.MaxOffsetDistance.Value.ToString("0.###") : "(null)"; } catch { }
        log.AppendLine($"   [{idx}칸] '{bandName}' 전: 출처={was} · 재료={mat} · 최대오프셋={maxOff}");

        // ── ② 표현식을 찍는다 — 이 밴드가 무엇을 읽으려 하는지.
        try
        {
            if (tr.GetObject(item.BandStyleId, OpenMode.ForRead) is CivilDb.Styles.SectionalDataBandStyle sdb)
                foreach (var (pn, sid) in new[] { ("단면검토선라벨", sdb.SampleLineStationLabelStyleId),
                                                  ("증분라벨", sdb.IncrementalSectionDataLabelStyleId) })
                {
                    if (sid.IsNull) { log.AppendLine($"        {pn}: 없음"); continue; }
                    if (tr.GetObject(sid, OpenMode.ForRead) is not CivilDb.Styles.LabelStyle ls) continue;
                    using var comps = ls.GetComponents(CivilDb.Styles.LabelStyleComponentType.Text);
                    int nc = 0;
                    foreach (ObjectId cid in comps)
                    {
                        if (tr.GetObject(cid, OpenMode.ForRead) is not CivilDb.Styles.LabelStyleTextComponent tc) continue;
                        using var txt = tc.Text; using var con = txt.Contents;
                        log.AppendLine($"        {pn}[{nc++}] {con.Value}");
                    }
                    if (nc == 0) log.AppendLine($"        {pn}: 글자 구성요소가 0개");
                }
        }
        catch (System.Exception ex) { log.AppendLine($"        표현식 읽기 실패 — {ex.Message}"); }

        // ── ③ <b>어디에 찍을지</b> = 단면검토선 그룹.
        if (!slGroupId.IsNull)
        {
            try { item.DataSourceId = slGroupId; } catch (System.Exception ex) { sb.Append($"그룹대입실패({ex.GetType().Name}) "); }
            ObjectId back = ObjectId.Null; try { back = item.DataSourceId; } catch { }
            sb.Append(back == slGroupId ? "위치=단면검토선그룹 " : $"위치=안붙음({Who(back)}) ");
        }
        else sb.Append("위치=그룹없음 ");

        // ── ④ <b>무슨 값</b> = 종단1·종단2.
        //
        //   ★★[v25.4 실측 확정] 표현식을 찍어 보고서야 갈렸다. 이 밴드는 <b>둘 다</b> 쓴다 —
        //   자리는 단면검토선에서, 값은 <b>종단</b>에서. 그래서 v25.2까지는 눈금만 생기고 값이 비었다.
        //   <code>
        //   성토고 : 종단2 표고 - 종단1 표고
        //   절토고 : 종단1 표고 - 종단2 표고
        //   계획고 : 종단2 표고
        //   지반고 : 종단1 표고
        //   </code>
        //   네 식이 <b>한 방향으로 일치</b>한다 — <b>종단1=원지반 · 종단2=정지면</b>.
        //   종단 데이터 밴드 때처럼 계획고·지반고가 서로 부딪히는 일이 없다(그쪽은 둘 다 종단1이었다).
        //   그러니 <b>여섯 칸을 같은 배선으로 통일</b>한다 — 밴드마다 다르게 꽂을 이유가 없다.
        int okP = 0;
        if (!pidGround.IsNull)
        {
            try { item.Profile1Id = pidGround; okP++; } catch (System.Exception ex) { sb.Append($"종단1실패({ex.GetType().Name}) "); }
        }
        if (!pidPad.IsNull)
        {
            try { item.Profile2Id = pidPad; okP++; } catch (System.Exception ex) { sb.Append($"종단2실패({ex.GetType().Name}) "); }
        }
        string b1 = "?", b2 = "?";
        try { b1 = Who(item.Profile1Id); } catch { }
        try { b2 = Who(item.Profile2Id); } catch { }
        log.AppendLine($"   [{idx}칸] 후: 출처={Who(item.DataSourceId)} · 종단1={b1} · 종단2={b2}" +
                       (okP < 2 ? "  ⚠종단을 다 못 꽂았다 — 값이 빈다" : ""));
        return (sb + $"1=원지반 2=정지면").Trim();
    }

    /// <summary>★★[v25.0 · JACK 0811 확정] <b>측점 목록 → 단면검토선.</b>
    /// <para>측점의 원천은 셋이고, 셋 다 <b>여기서 한 목록으로 합쳐</b> 단면검토선으로 심는다.
    /// 그 뒤로는 종단도 밴드도, 횡단면도도 이 목록 하나만 본다.</para>
    /// <code>
    /// ⓐ 정측점    20m마다                       → No.0 · No.1 · No.2
    /// ⓑ 굴곡부    선형 × 정지면 굴곡선의 2D 교차  → 데이라잇·소단·사면·옹벽을 넘는 자리
    /// ⓒ 수동      사용자가 종단뷰에서 찍은 자리    → DHSTATION
    /// </code>
    /// <para><b>솎지 않는다</b>(JACK: "최소간격 없어 둘 다 찍어"). 정측점과 굴곡부가 30cm 차이로
    /// 붙어도 둘 다 남긴다 — 라벨이 겹쳐 보이는 것보다 <b>빠지는 것</b>이 나쁘다.</para>
    /// 반환=만든 단면검토선 그룹(실패하면 Null).</summary>
    private static ObjectId BuildSampleLines(Database db, Editor ed, ObjectId alignId,
                                             ObjectId pidGround, ObjectId pidPad,
                                             System.Collections.Generic.List<SectionCommand.SurfPick> surfs,
                                             double interval, System.Text.StringBuilder log)
    {
        try
        {
            double wl = System.Math.Max(1.0, GradingSettings.XsecLeft);
            double wr = System.Math.Max(1.0, GradingSettings.XsecRight);
            var marks = new System.Collections.Generic.List<StationMarks.Mark>();
            var cuts = new System.Collections.Generic.List<SectionCommand.Cut>();
            double s0, s1;

            // ── ① 측점 목록을 만든다(읽기만 — 아직 도면에 아무것도 안 만든다).
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var al = (CivilDb.Alignment)tr.GetObject(alignId, OpenMode.ForRead);
                s0 = al.StartingStation; s1 = al.EndingStation;

                // ⓑ 굴곡부 — 우리가 만든 지표면 <b>전부</b>에서 굴곡선을 읽는다.
                //
                //   ★★[v25.1 실측] 종전엔 정지면 하나만 봤는데 <b>굴곡선이 0개</b>였다.
                //     <c>정지면_DH</c>는 <b>붙여넣기(Paste) 합성면</b>이라 자기 굴곡선이 없다 —
                //     데이라잇·소단·사면 굴곡선은 붙여넣기 <b>원본</b>인 <c>가상절토_DH</c>·<c>가상성토_DH</c>에 있다.
                //     그래서 도면의 <b>DH 산출물 지표면 전부</b>를 훑는다.
                //   원지반은 <b>뺀다</b> — 측량면의 굴곡선은 설계가 아니라 지형이고 수천 개가 나온다.
                var srcIds = new System.Collections.Generic.List<ObjectId>();
                var srcNm = new System.Collections.Generic.List<string>();
                ObjectId groundId = ObjectId.Null;
                foreach (var s in surfs) if (s.Label == "원지반") groundId = s.SurfId;
                foreach (ObjectId sid in CivilApp.CivilApplication.ActiveDocument.GetSurfaceIds())
                {
                    if (sid == groundId) continue;
                    try
                    {
                        if (tr.GetObject(sid, OpenMode.ForRead) is not CivilDb.TinSurface ts) continue;
                        // DH 산출물만 — 남의 지표면을 굴곡선 출처로 삼지 않는다(저장소 공통 규칙).
                        if (!ts.Name.Contains("_DH")) continue;
                        srcIds.Add(sid); srcNm.Add(ts.Name);
                    }
                    catch { }
                }
                // ★★[v25.3] <b>정지구간 밖의 링은 버린다.</b> 가상 사면은 <b>오버사이즈</b>라
                //   잘려나갈 소단까지 두르고 있다. 그 링과의 교차는 <b>도면에 없는 자리</b>다.
                //   판정은 두 종단의 표고차 — 계획면이 원지반과 붙어 있으면 정지한 데가 아니다.
                CivilDb.Profile prPad = null, prGrd = null;
                try { prPad = tr.GetObject(pidPad, OpenMode.ForRead) as CivilDb.Profile; } catch { }
                try { prGrd = tr.GetObject(pidGround, OpenMode.ForRead) as CivilDb.Profile; } catch { }
                System.Func<double, bool> graded = null;
                if (prPad != null && prGrd != null)
                    graded = s =>
                    {
                        try { return System.Math.Abs(prPad.ElevationAt(s) - prGrd.ElevationAt(s)) > StationMarks.PadGroundTol; }
                        catch { return false; }
                    };
                else log.AppendLine("  ⚠종단 둘을 못 열어 '정지구간 밖 버리기'를 못 한다 — 오버사이즈 링이 섞일 수 있다");

                log.AppendLine("굴곡부 수집(선형 × 정지면 굴곡선) — 대상 지표면 " +
                               (srcNm.Count > 0 ? string.Join("·", srcNm) : "없음(정지면을 먼저 만들어야 한다)"));
                marks.AddRange(StationMarks.FromGradingBreaklines(tr, al, srcIds, graded, log));

                // ★★[v25.3 · JACK 0811] <b>데이라잇은 도면선에서 읽는다.</b>
                //   "데이라잇선(계획지표면이 시작되는 지점)은 단면검토선이 안 끊어졌어" —
                //   가상 사면이 오버사이즈라 그 굴곡선에는 데이라잇이 없다. 진짜 데이라잇은
                //   <c>DrawDaylight</c>가 <c>DH-정지경계</c>에 그려 둔다. 여기는 <b>정지구간 판정을 걸지 않는다</b> —
                //   데이라잇은 정의상 정지구간의 <b>가장자리</b>라 표고차가 0에 가깝고, 걸면 자기가 걸러진다.
                marks.AddRange(StationMarks.FromLayerLines(tr, db, al,
                                   new[] { LayerDaylight, LayerClip }, "데이라잇", null, log));

                // ⓒ 수동 — 선형에 적어 둔 것(DHSTATION).
                var man = StationMarks.Load(tr, alignId);
                marks.AddRange(man);
                if (man.Count > 0) log.AppendLine($"  수동 측점 {man.Count}개");
                tr.Commit();
            }

            // ⓐ 정측점(20m)과 보조측점(10m)을 얹는다.
            //   ★★[v25.5 · JACK 0811] <b>"보조측점(10)은 아예 안 보여."</b> —
            //   v24.1에서 20m만 남기고 정리했던 것을 되살린다. 격자를 <b>절반 간격</b>으로 깔면
            //   20m 배수는 그대로 정측점(<c>No.1</c>)이 되고 그 사이가 보조측점이 된다.
            //   측점 형식은 <c>No.&lt;[측점값(FSI)]&gt;</c> 하나라 20m 자리는 <c>No.1</c>,
            //   그 사이는 <c>No.0+10.00</c>으로 <b>저절로 갈린다</b> — 스위치를 따로 둘 필요가 없다.
            //   <b>tol=1cm</b> — 같은 자리만 합치고 그 외엔 전부 남긴다(JACK 확정 "최소간격 없어 둘 다 찍어").
            double sub = interval / 2.0;
            var all = StationMarks.Merge(s0, s1, sub, marks, tol: 0.01);
            //   사유를 갈라 적는다 — 로그를 도면과 대조할 때 '왜 여기 측점이 있나'가 바로 보여야 한다.
            for (int i = 0; i < all.Count; i++)
                if (all[i].Why == "정체인")
                {
                    bool onMain = System.Math.Abs(all[i].Station - System.Math.Round(all[i].Station / interval) * interval) < 1e-6;
                    all[i] = all[i] with { Why = onMain ? $"정측점({interval:0.#}m)" : $"보조측점({sub:0.#}m)" };
                }
            log.AppendLine($"측점 목록 {all.Count}개(정측점 {interval:0.#}m + 굴곡부 + 수동):\n    " +
                           string.Join("\n    ", all.ConvertAll(m => $"{m.Station,9:0.00}m  {StationMarks.Fmt(m.Station, interval),-12} {m.Why}")));

            // ── ② 좌우 폭 지점을 미리 잰다(끄트머리에서 법선 계산이 실패하는 것을 피해 살짝 안쪽으로).
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var al = (CivilDb.Alignment)tr.GetObject(alignId, OpenMode.ForRead);
                const double eps = 0.001;
                foreach (var m in all)
                {
                    double st = System.Math.Min(System.Math.Max(m.Station, s0 + eps), s1 - eps);
                    if (SectionCommand.TryCut(al, st, wl, wr, out var c)) cuts.Add(c);
                    else log.AppendLine($"  ⚠{m.Station:F2}m — 법선을 못 구해 단면검토선을 못 놓는다({m.Why})");
                }
                tr.Commit();
            }
            if (cuts.Count == 0) { log.AppendLine("단면검토선: 놓을 자리가 없어 건너뜀"); return ObjectId.Null; }

            // ── ③ 그룹과 선을 만든다.
            var cdoc = CivilApp.CivilApplication.ActiveDocument;
            string groupName = SectionCommand.UniqueName(db, cdoc, SectionCommand.GroupBase);
            ObjectId groupId;
            try { groupId = CivilDb.SampleLineGroup.Create(groupName, alignId); }
            catch (System.Exception ex)
            { log.AppendLine("단면검토선 그룹 생성 실패 — " + ex.Message); return ObjectId.Null; }

            // 표본으로 삼을 지표면 = 우리 것만. 이게 켜져 있어야 '횡단 데이터' 밴드에 값이 찍힌다.
            int nSrc = 0; var srcNames = new System.Text.StringBuilder();
            try
            {
                using var tr = db.TransactionManager.StartTransaction();
                var g = (CivilDb.SampleLineGroup)tr.GetObject(groupId, OpenMode.ForWrite);
                foreach (CivilDb.SectionSource src in g.GetSectionSources())
                {
                    bool ours = surfs.Exists(s => s.SurfId == src.SourceId);
                    try { src.IsSampled = ours; if (ours) { nSrc++; srcNames.Append(' ').Append(src.SourceName); } } catch { }
                }
                tr.Commit();
            }
            catch (System.Exception ex) { log.AppendLine("  표본 지표면 지정 경고 — " + ex.Message); }

            int nSl = 0; string firstErr = null;
            for (int i = 0; i < cuts.Count; i++)
            {
                try
                {
                    var pts = new Point2dCollection { cuts[i].Left, cuts[i].Right };
                    var id = CivilDb.SampleLine.Create($"{groupName}_{StationMarks.Fmt(cuts[i].Station, interval)}", groupId, pts);
                    if (!id.IsNull) nSl++;
                }
                catch (System.Exception ex) { firstErr ??= $"{cuts[i].Station:F2}m {ex.Message}"; }
            }
            log.AppendLine($"단면검토선 '{groupName}' — {nSl}/{cuts.Count}개 생성 · 좌{wl:0.#}m/우{wr:0.#}m · 표본 지표면 {nSrc}개[{srcNames.ToString().Trim()}]" +
                           (firstErr != null ? $"\n  ⚠첫 실패: {firstErr}" : ""));
            ed.WriteMessage($"\n  · 단면검토선 {nSl}개 (정측점 {interval:0.#}m + 굴곡부 + 수동)");

            // ── ④ 측점 행이 쓸 <b>라벨 자리 전용 체인</b>(값은 안 쓴다).
            LastLabelChainId = BuildLabelChain(db, alignId, pidPad, all, interval, log);
            return groupId;
        }
        catch (System.Exception ex) { log.AppendLine("단면검토선 실패 — " + ex.Message); return ObjectId.Null; }
    }

    private static string ApplyViewStyle(Database db, CivilApp.CivilDocument cdoc, ObjectId pvId,
                                         ObjectId pidGround, ObjectId pidPad, ObjectId slGroupId,
                                         System.Collections.Generic.List<SectionCommand.SurfPick> surfs,
                                         Editor ed, System.Text.StringBuilder log)
    {
        var msg = new System.Text.StringBuilder("스타일 지정: ");

        // ── ⓪ 어느 정보표시 테이블을 씌울지 — 물어보되 **지난 선택이 기본값**이라 Enter면 넘어간다.
        //   JACK 0810 "둘 다 — 실행할 때 고른다". 자동 판정은 불가능하다: 상수도 현장은 한 도면에
        //   원지반·정지면·관로가 같이 있는 게 정상이라, 방금 그린 노선이 무엇인지 알 길이 없다.
        //   잘못 고르면 12칸짜리 표가 통째로 비므로 '틀린 자동'이 '한 번 묻기'보다 훨씬 비싸다.
        // ★[JACK 0810] 안내 라벨에 대괄호를 쓰지 않는다 — AutoCAD는 프롬프트의 [...]를 **선택지 목록**으로
        //   읽어서, '[종단도]'라는 라벨이 통째로 유령 선택지가 됐다("메뉴에 토공은 뭐고 종단도는 뭐야?").
        var pko = new PromptKeywordOptions($"\n정보표시 테이블 <{GradingSettings.BandSet}>") { AllowNone = true };
        // ★[JACK 0810] "도로와 없음은 없애. 우린 토공과 관로만 필요해."
        //   쓰지 않는 선택지는 고민만 늘린다.
        pko.Keywords.Add("토공"); pko.Keywords.Add("관로");
        pko.Keywords.Default = GradingSettings.BandSet;
        var pr = ed.GetKeywords(pko);
        string want = pr.Status == PromptStatus.OK ? pr.StringResult : GradingSettings.BandSet;
        if (want != "없음") { GradingSettings.BandSet = want; GradingSettings.SaveBandSet(); }

        // ── ① 필수 구간 — 뷰 스타일 + 밴드 세트. 여기가 깨지면 되돌린다.
        bool core = false;
        using (var tr = db.TransactionManager.StartTransaction())
        {
            try
            {
                var pv = (CivilDb.ProfileView)tr.GetObject(pvId, OpenMode.ForWrite);
                var vs = ProfileStyleTemplate.PickByClass(db, cdoc, ProfileStyleTemplate.ClsProfileView, ViewStyleName);
                if (vs.HasValue) { pv.StyleId = vs.Value.Id; msg.Append($"뷰='{vs.Value.Name}'"); }
                else msg.Append("뷰=(회사 표준 없음 — 기본값 유지)");

                if (want == "없음") msg.Append(" · 밴드=건너뜀");
                else
                {
                    // ★[JACK 0810] 밴드를 한 장씩 붙이던 것을 **세트 통째 적용**으로 바꿨다.
                    //   종전엔 종단 데이터 밴드 한 장만 붙어 '한 줄짜리 표'가 나왔다 — 회사 표준은
                    //   12칸짜리 정보표시 테이블이고, 템플릿이 세트를 3벌 갖고 있는 이유가 그것이다.
                    //   ImportBandSetStyle은 기존 밴드를 **통째로 교체**하므로 재실행해도 쌓이지 않는다.
                    var set = ProfileStyleTemplate.PickBandSet(db, cdoc, want);
                    if (!set.HasValue) msg.Append($" · 밴드=('{want}' 세트가 도면에 없음)");
                    else
                    {
                        int before = 0;
                        try { using var b0 = pv.Bands.GetBottomBandItems(); before = b0.Count; } catch { }
                        pv.Bands.ImportBandSetStyle(set.Value.Id);
                        int after = 0;
                        try { using var b1 = pv.Bands.GetBottomBandItems(); after = b1.Count; } catch { }
                        msg.Append($" · 세트='{set.Value.Name}'(하단 {before}→{after}칸)");
                    }
                }
                core = true;
            }
            catch (System.Exception ex) { msg.Append(" ⚠세트 적용 실패:" + ex.Message); }
            if (core) tr.Commit();
        }
        if (!core) return msg.ToString();

        // ── ② 최선노력 구간 — 밴드를 '종단 데이터'로 갈아 끼우고 종단·간격을 꽂는다.
        //
        //   ★★[JACK 0810 실측] 토공 세트가 **6칸 전부 횡단 데이터(SectionalData) 밴드**였다.
        //     그 종류는 **단면검토선에서만** 값을 읽으므로 단면검토선이 없으면 표가 통째로 빈다 —
        //     JACK이 본 '밴드칸은 만들어졌는데 데이터와 측점이 없어'가 정확히 이것이다.
        //     게다가 우리가 그 종류를 '대상아님'으로 건너뛰어 종단이 Civil 3D 기본값(원지반)으로
        //     남았고, 그래서 '종단이 전부 원지반'으로 보였다. 세 증상이 한 원인이었다.
        //
        //   → **짝이 되는 '종단 데이터' 밴드로 바꿔 끼운다.** 템플릿에 6개가 이미 다 있고
        //     이름이 1:1로 맞는다(…_횡단 데이터_지반고 ↔ …_종단 데이터_지반고).
        //     종단 데이터 밴드는 단면검토선 없이 종단에서 바로 값을 읽는다.
        //     **JACK이 정한 칸 순서는 그대로 지킨다** — 세트가 가진 설계는 살리고 종류만 바꾼다.
        //     짝을 못 찾은 칸은 원래 것을 그대로 두고 로그에 남긴다(조용히 버리지 않는다).
        int okN = 0, naN = 0, badN = 0, swapN = 0;
        double band = System.Math.Max(1.0, GradingSettings.XsecInterval);
        var detail = new System.Text.StringBuilder();
        using (var tr = db.TransactionManager.StartTransaction())
        {
            try
            {
                var pv = (CivilDb.ProfileView)tr.GetObject(pvId, OpenMode.ForWrite);

                // ★★[v25.0 · JACK 0811] <b>목록을 다시 만들지 않는다 — 있는 칸을 그대로 손본다.</b>
                //
                //   종전엔 '횡단 데이터'를 '종단 데이터'로 <b>바꿔 끼우려고</b> 목록을 통째로 새로 만들었다.
                //   항목의 종류는 만든 뒤에 못 바꾸니 그 방법밖에 없었다. 그런데 v25.0에서 바꿀 이유가
                //   사라졌는데도 다시 만드는 코드가 남아 있었고, <c>Add(종류, 이름)</c>이 횡단 데이터 이름을
                //   못 찾아 <b>6칸이 통째로 날아갔다</b>(실측: "The specified band style name is not found",
                //   그 결과 밴드 0칸). 바꿀 게 없으면 <b>다시 만들 이유도 없다.</b>
                foreach (bool bottom in new[] { true, false })
                {
                    // ★★[v26.0 · 실측으로 확정] <b>한 번에 읽고 · 다 고치고 · 한 번에 저장한다.</b>
                    //
                    //   <c>GetBandItems</c>는 <b>스냅샷</b>이고 <c>SetBandItems</c>는 그 스냅샷을
                    //   <b>통째로 덮어쓴다</b>. 이걸 몰라서 두 판을 헤맸다:
                    //   <list type="bullet">
                    //   <item>v25.8 저장을 아예 안 했더니 — 눈금까지 통째로 사라졌다(아무것도 저장 안 됨).</item>
                    //   <item>v25.9 칸마다 저장했더니 — <b>마지막 칸만</b> 살아남았다(앞 칸이 매번 덮여 나감).
                    //         진단 블록이 숫자로 못박았다: 5번 칸만 <c>레이블표시=켬</c>, 나머지는 전부 꺼짐.</item>
                    //   </list>
                    //   → 스냅샷 하나에 <b>여섯 칸의 수정을 모두 담아</b> 마지막에 한 번 저장한다.
                    // ★★[v27.2 · JACK 0811 실측] <b>있는 항목을 고치지 말고 목록을 새로 만든다.</b>
                    //
                    //   JACK: <i>"정보표시 테이블 가져오기에서 DH 토공을 가져오고 그렇게 세팅하면 잘 나와.
                    //   그런데 우리 것 세팅 상태에서 똑같이 레이블 끝 해도 안 나와."</i>
                    //   → 차이는 <b>설정값이 아니라 '가져오기'라는 행위 자체</b>에 있다.
                    //     그 버튼은 밴드 항목을 <b>새로 만든다</b>. 새로 만들 때 Civil이 밴드마다
                    //     <b>라벨 그룹</b>을 붙이는데, 우리처럼 있는 항목을 고쳐 되돌려 넣으면 그게 날아간다
                    //     (그래서 첫 칸만 살아남았다).
                    //
                    //   v25.0에 이 방식을 걷어냈던 이유는 <c>Add(종류, 이름)</c>이
                    //   <c>band style name is not found</c>로 실패했기 때문인데, 그건 <b>스타일이 남의 서랍</b>
                    //   (횡단 뷰)에 있어서였다. v27.0에서 제자리로 옮겼으니 이제 이름으로 찾힌다.
                    var order = new System.Collections.Generic.List<(Autodesk.Civil.BandType T, string N)>();
                    using (var cur = bottom ? pv.Bands.GetBottomBandItems() : pv.Bands.GetTopBandItems())
                        for (int i = 0; i < cur.Count; i++)
                        {
                            string n0 = ""; var t0 = Autodesk.Civil.BandType.ProfileData;
                            try
                            {
                                t0 = cur[i].BandType;
                                if (tr.GetObject(cur[i].BandStyleId, OpenMode.ForRead) is
                                    Autodesk.Civil.DatabaseServices.Styles.StyleBase s0) n0 = s0.Name;
                            }
                            catch { }
                            if (n0.Length == 0) continue;

                            // ★★[v28.0 · JACK 0811 확정] <b>측점 행만 '종단 데이터' 밴드로 바꾼다.</b>
                            //
                            //   JACK 요구: <i>정측점은 <c>No.1</c>, 그 외는 <c>+06.41</c>.</i>
                            //   그런데 <b>한 밴드의 라벨 형식은 하나뿐</b>이다. 횡단 데이터 밴드의 '증분 라벨'로
                            //   갈라 보려 했으나, 실측 결과 그 라벨이 쓸 수 있는 항목은
                            //   <b>'이전 단면검토선과의 거리'와 토량뿐</b>이라 측점조차 못 찍는다(JACK 확인).
                            //
                            //   반면 <b>종단 데이터 밴드</b>는 <b>주 증분</b>(20m→<c>No.1</c>)과
                            //   <b>굴곡부</b>(→<c>+06.41</c>)를 <b>따로</b> 찍는다 — 자리가 다르니 형식도 다르게 준다.
                            //   측점 행은 <b>값이 필요 없으므로</b> 표고를 보간해 읽던 옛 걱정이 아예 없다.
                            //   값 다섯 행은 그대로 단면검토선에서 읽으니 <b>측점은 여전히 한 줄로 선다</b>.
                            if (t0 == Autodesk.Civil.BandType.SectionalData && n0.Contains("측점"))
                            {
                                var twin = ProfileStyleTemplate.Collect(db, cdoc,
                                               x => x.Cls == ProfileStyleTemplate.ClsProfileDataBand
                                                 && x.Name.Contains("측점", System.StringComparison.Ordinal))
                                           .FirstOrDefault();
                                if (!twin.Id.IsNull)
                                {
                                    detail.AppendLine($"    [측점] 횡단 데이터 → 종단 데이터 '{twin.Name}'로 교체(No.1 / +06.41을 나눠 찍기 위해)");
                                    order.Add((Autodesk.Civil.BandType.ProfileData, twin.Name));
                                    continue;
                                }
                                detail.AppendLine("    [측점] ⚠짝이 되는 '종단 데이터_측점' 스타일이 없어 그대로 둔다");
                            }
                            order.Add((t0, n0));
                        }
                    if (order.Count == 0) continue;

                    using var fresh = new CivilDb.ProfileViewBandItemCollection(
                        pvId, bottom ? Autodesk.Civil.BandLocationType.Bottom : Autodesk.Civil.BandLocationType.Top);
                    // ★★[v29.0 점검 반영 · 높음] <b>붙이기에 성공한 것만 따로 모은다.</b>
                    //   종전엔 실패한 칸은 빠지는데 배선은 <b>원래 목록의 번호</b>를 그대로 썼다.
                    //   6칸 중 2번이 실패하면 <b>3번 내용이 2번 자리에 적힌다</b> — 예외도 안 나고
                    //   로그는 성공한 이름으로 찍혀 <b>조용히 틀린다</b>. 밀림이 생길 수 없게 목록을 다시 만든다.
                    var placed = new System.Collections.Generic.List<(Autodesk.Civil.BandType T, string N)>();
                    foreach (var (t1, n1) in order)
                    {
                        try { fresh.Add(t1, n1); placed.Add((t1, n1)); }
                        catch (System.Exception ex)
                        { detail.AppendLine($"    [{(bottom ? "하단" : "상단")}] {t1} '{n1}' → 붙이기 실패:{ex.Message}"); badN++; }
                    }
                    int cnt = placed.Count;
                    if (cnt != order.Count)
                        log.AppendLine($"    ⚠{(bottom ? "하단" : "상단")} {order.Count}칸 중 {cnt}칸만 붙었다 — 못 붙은 칸은 도면에서 사라진다");
                    if (cnt == 0) { log.AppendLine($"    ⚠{(bottom ? "하단" : "상단")} 밴드를 하나도 못 붙였다 — 옛 목록을 그대로 둔다"); continue; }

                    for (int i = 0; i < cnt; i++)
                    {
                        int k = i;
                        var (bt, nm) = placed[i];
                        string act = "";
                        switch (bt)
                        {
                            case Autodesk.Civil.BandType.ProfileData:
                                try
                                {
                                    // ★★[JACK 0810] <b>계획고 밴드만 1번이 정지면이다.</b>
                                    //   실측 결함: 계획고 행과 지반고 행의 값이 <b>한 자리도 안 틀리게 같았다</b>
                                    //   (103.09/103.09 · 103.20/103.20 …). 원인은 배선이다 —
                                    //   두 밴드의 회사 표현식이 <b>둘 다 <c>&lt;[종단1 표고]&gt;</c></b>인데
                                    //   코드가 모든 밴드에 1=원지반을 꽂았다. 그래서 계획고 자리에 지반고가 찍혔다.
                                    //   (절토 <c>종단1-종단2</c> · 성토 <c>종단2-종단1</c>는 1=원지반이라야 부호가 맞다.)
                                    //
                                    //   ※ 여기서만은 <b>이름으로 고른다.</b> §22.4는 '종류로 고르라'였지만
                                    //     계획고와 지반고는 <b>종류도 표현식 구조도 같다</b> — 이름 말고 구분할 근거가 없다.
                                    //     그래서 '계획'이 들어가면 뒤집는다.
                                    // ★★[JACK 0811] <b>"성토~측점까지 모든 밴드의 측점 분할구간이 같아야 해.
                                    //   그런데 계획고나 누가거리나 다 제각각이야. 그럼 측점이라는 게 의미가 없어."</b>
                                    //
                                    //   계측으로 확정됐다: <b>굴곡부는 종단1을 따라간다</b>
                                    //   (계획고 행과 지반고 행의 값이 서로 다른 자리에 찍혔다 —
                                    //    누가거리 칸만 종단1을 바꿔 둔 실험도 같은 결론).
                                    //   그런데 종단1은 회사 표현식의 부호에 묶여 밴드마다 달랐다.
                                    //   → <b>전부 1=정지면 2=원지반으로 통일</b>하고,
                                    //     표현식의 종단1↔종단2를 역할에 맞게 <see cref="SheetCommand"/>에서 맞춘다.
                                    //     그래야 값은 그대로면서 측점이 한 줄로 선다.
                                    // ★★[v28.0] 이 자리에 남는 종단 데이터 밴드는 <b>측점 행 하나</b>다.
                                    //   종단1을 <b>측점 라벨용 체인</b>으로 꽂는다 — 굴곡부 라벨이 종단1을 따라가므로,
                                    //   체인의 PVI(=20m 아닌 측점)마다 <c>+06.41</c>이 찍힌다.
                                    //   20m 자리는 <b>주 증분</b>이 <c>No.1</c>로 찍는다(체인엔 PVI가 없어 안 겹친다).
                                    //   ★★[v29.0 점검 반영] <b>정지면으로 몰래 바꿔 끼우지 않는다.</b>
                                    //   종전엔 체인이 없으면 조용히 계획 종단을 꽂았다. 그건 지표면 표본이라
                                    //   62m 노선에 PVI가 78개 잡힌 실측이 있다 — 굴곡부 라벨이 수십 개 겹쳐 찍힌다.
                                    //   <b>조용히 틀린 도면</b>보다 <b>빠진 채로 로그에 남는 편</b>이 낫다.
                                    ObjectId p1 = AliveChain(db, pv.AlignmentId);
                                    if (!p1.IsNull) fresh[k].Profile1Id = p1;
                                    else act += " · ⚠측점 체인 없음(굴곡부 측점이 안 찍힌다)";
                                    if (!pidGround.IsNull) fresh[k].Profile2Id = pidGround;
                                    // ★ 간격이 0이면 라벨이 하나도 안 찍힌다 — JACK 스샷의 '주 간격' 칸이 비어 있었다.
                                    // ★★[v24.1] <b>측점은 주 증분 하나만 쓴다.</b> 보조 증분과 굴곡부는
                                    //   <see cref="SheetCommand"/>에서 <b>표시를 꺼</b> 둔다 — 지금은 20m 정측점이
                                    //   제자리에 서는지부터 확인하는 판이다(JACK: "정체인 20미터 간격으로 측점
                                    //   나오게 먼저 만들어봐"). 보조 간격 값 자체는 남겨 둔다 — 나중에 켤 때 쓴다.
                                    fresh[k].MajorInterval = band;
                                    fresh[k].MinorInterval = band / 2.0;
                                    act += $" · 1=정지면 2=원지반 · 주간격 {band:0.#}m";
                                    okN++;
                                }
                                catch (System.Exception ex) { act += " · 배선실패:" + ex.Message; badN++; }
                                break;
                            case Autodesk.Civil.BandType.VerticalGeometry:
                                // 구배 밴드는 **계획 종단**의 종단선형 기하를 읽는다(원지반엔 그 기하가 없다).
                                try { if (!pidPad.IsNull) fresh[k].Profile1Id = pidPad; act += " · 1=정지면"; okN++; }
                                catch (System.Exception ex) { act += " · 배선실패:" + ex.Message; badN++; }
                                break;
                            case Autodesk.Civil.BandType.SectionalData:
                                // ★★[v27.0] <b>맞는 서랍의 스타일로 갈아 끼운다.</b>
                                //   밴드 세트가 들고 온 스타일은 <b>횡단 뷰 서랍</b>에 앉은 것이라,
                                //   종단도 밴드가 이름으로 찾을 때 없는 것과 같다.
                                //   같은 이름으로 종단 뷰 서랍에 만들어 둔 것으로 바꿔 꽂는다.
                                try
                                {
                                    var right = CivilDb.Styles.BandStyle.GetBandStyleId(
                                                    db, Autodesk.Civil.BandType.SectionalData, nm);
                                    if (!right.IsNull && right != fresh[k].BandStyleId)
                                    { fresh[k].BandStyleId = right; act += " · 스타일을 종단뷰 서랍 것으로 교체"; }
                                }
                                catch (System.Exception ex) { act += " · 스타일 교체 실패:" + ex.Message; }
                                // ★★[v25.2 계측] <b>값이 비는 자리를 짐작으로 메우지 않는다.</b>
                                //   실측: 단면검토선 15개가 제대로 만들어졌는데도 표가 통째로 비었다.
                                //   <c>ProfileViewBandItem.DataSourceId</c>가 '무엇을 읽을지'를 정하는데,
                                //   여기에 <b>단면검토선 그룹</b>을 넣는지 <b>지표면</b>을 넣는지 문서가 없다.
                                //   → <b>둘 다 넣어 보고 되읽어</b> 어느 쪽이 붙는지 이 판에서 확정한다.
                                //     짐작으로 한쪽만 넣으면 실패했을 때 '틀린 값'인지 '안 먹은 것'인지 못 가른다.
                                // ★★[v29.0 점검 반영] <b>단면검토선 그룹이 없으면 성공으로 세지 않는다.</b>
                                //   종전엔 그룹이 아예 없어도 값을 안 넣고 카운터만 올려 "꽂음 6칸"으로 요약했다 —
                                //   값 다섯 행이 통째로 빈 도면인데 <b>성공 보고가 나갔다</b>.
                                act += " · " + WireSectionalBand(tr, fresh[k], nm, slGroupId, pidGround, pidPad, log, k);
                                if (slGroupId.IsNull) { act += " · ⚠단면검토선 그룹 없음(값이 안 나온다)"; badN++; }
                                else okN++;
                                break;
                            default:
                                act += " · 대상아님"; naN++;
                                break;
                        }
                        detail.AppendLine($"    [{(bottom ? "하단" : "상단")} {i}] {bt} '{nm}' → {act.TrimStart(' ', '·')}");
                    }
                    if (bottom) pv.Bands.SetBottomBandItems(fresh); else pv.Bands.SetTopBandItems(fresh);
                    log.AppendLine($"    ({(bottom ? "하단" : "상단")} {cnt}칸 — 한 스냅샷에 모아 한 번 저장)");
                }
            }
            catch (System.Exception ex) { msg.Append(" ⚠배선 중단:" + ex.Message); }
            tr.Commit();   // 최선노력 — 일부 실패해도 성공한 것은 남긴다
        }
        if (detail.Length > 0) log.AppendLine("  밴드 배선:\n" + detail.ToString().TrimEnd());
        msg.Append($" · 종단→간격 꽂음 {okN}칸" + (swapN > 0 ? $" · 횡단→종단 교체 {swapN}칸" : "")
                 + (naN > 0 ? $" · 대상아님 {naN}칸" : "") + (badN > 0 ? $" · 실패 {badN}칸" : ""));
        return msg.ToString();
    }

    /// <summary>노선을 화면에 직접 그린다 — 점을 연달아 찍고 Enter로 끝낸다(Esc=취소).
    /// <para>
    /// ★[JACK 0807] <b>찍는 즉시 선이 보여야 한다</b> — "다 찍고 나서 엔터를 쳐야 선이 보이니깐 노선을 잡기가 쉽지 않아."
    /// 종전엔 점만 모아 뒀다가 마지막에 한 번에 그려서, 어디까지 어떻게 그렸는지 보이지 않았다.
    /// 이제 <b>폴리선을 먼저 만들고 점을 찍을 때마다 정점을 붙여 커밋</b>한다 — 커밋할 때마다 화면에 그려지므로
    /// 지금까지 그린 노선이 계속 보인 채로 다음 점을 잡을 수 있다.
    /// </para>
    /// 덤으로 <b>그 폴리선이 곧 결과물</b>이라 마지막에 다시 만들 필요가 없다(취소하면 지운다).
    /// 반환 <see cref="ObjectId.Null"/> = 취소.
    /// </summary>
    private static ObjectId DrawRoute(Database db, Editor ed, out int nPts, out double len)
    {
        nPts = 0; len = 0;
        ObjectId layerId;
        using (var tr = db.TransactionManager.StartTransaction())
        { layerId = SectionCommand.EnsureLayer(db, tr, LayerRoute, YellowIndex); tr.Commit(); }

        var first = ed.GetPoint("\n[종단도] 노선 시작점 클릭 (Esc=취소): ");
        if (first.Status != PromptStatus.OK) return ObjectId.Null;
        // ★[검토단 0807] 클릭 좌표는 **사용자 좌표계(UCS)** 값이고 폴리선 정점은 도면 좌표계(WCS)다.
        //   종전엔 종단도 놓을 자리만 변환하고 노선 점은 변환 없이 썼다 — UCS를 돌려 쓰는 도면에서는
        //   노선이 엉뚱한 자리에 그려진다. 여기서 한 번에 WCS로 맞춰 둔다.
        var ucs = ed.CurrentUserCoordinateSystem;
        var pts = new System.Collections.Generic.List<Point3d> { first.Value.TransformBy(ucs) };
        ObjectId plId = ObjectId.Null;

        // 지금까지 찍은 점으로 폴리선을 다시 그린다 — 커밋되는 순간 화면에 나타난다.
        void Redraw()
        {
            if (pts.Count < 2) return;
            using var tr = db.TransactionManager.StartTransaction();
            Polyline pl;
            if (plId.IsNull)
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForWrite);
                var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
                pl = new Polyline(pts.Count) { LayerId = layerId };
                ms.AppendEntity(pl); tr.AddNewlyCreatedDBObject(pl, true);
                plId = pl.ObjectId;
            }
            else pl = (Polyline)tr.GetObject(plId, OpenMode.ForWrite);
            while (pl.NumberOfVertices > 0) pl.RemoveVertexAt(pl.NumberOfVertices - 1);
            for (int i = 0; i < pts.Count; i++) pl.AddVertexAt(i, new Point2d(pts[i].X, pts[i].Y), 0, 0, 0);
            pl.Closed = false;
            tr.Commit();
        }

        while (true)
        {
            var opt = new PromptPointOptions(
                $"\n[종단도] 다음 점 클릭 [{pts.Count}점] (Enter=끝, U=마지막 점 취소): ")
            {
                AllowNone = true,                       // Enter로 끝내기
                UseBasePoint = true,
                BasePoint = pts[pts.Count - 1],         // 고무줄선 — 지금 놓을 구간이 보인다
            };
            opt.Keywords.Add("U", "U", "취소(U)");
            var pr = ed.GetPoint(opt);

            if (pr.Status == PromptStatus.None) break;                       // Enter — 끝
            if (pr.Status == PromptStatus.Keyword)
            {
                if (pts.Count > 1)
                {
                    pts.RemoveAt(pts.Count - 1);
                    if (pts.Count < 2 && !plId.IsNull) { SectionCommand.EraseQuiet(db, plId); plId = ObjectId.Null; }
                    else Redraw();
                    ed.UpdateScreen();
                    ed.WriteMessage($"\n  · 마지막 점 취소({pts.Count}점 남음)");
                }
                else ed.WriteMessage("\n  · 시작점은 취소할 수 없습니다(Esc로 전체 취소).");
                continue;
            }
            if (pr.Status != PromptStatus.OK)                                // Esc — 전체 취소
            {
                if (!plId.IsNull) SectionCommand.EraseQuiet(db, plId);
                return ObjectId.Null;
            }
            pts.Add(pr.Value.TransformBy(ucs));   // [검토단 0807] UCS→WCS (위 주석 참조)
            Redraw();
            ed.UpdateScreen();                                              // 찍는 즉시 보이게
        }

        if (pts.Count < 2)
        {
            if (!plId.IsNull) SectionCommand.EraseQuiet(db, plId);
            SectionCommand.Refuse(ed, "점을 2개 이상 찍어야 노선이 됩니다.");
            return ObjectId.Null;
        }
        nPts = pts.Count;
        using (var tr = db.TransactionManager.StartTransaction())
        { len = ((Polyline)tr.GetObject(plId, OpenMode.ForRead)).Length; tr.Commit(); }
        return plId;
    }

    /// <summary>이 명령이 만든 종단도·선형이 몇 개 있는지 — 재실행 때 물어보려고 센다.</summary>
    private static int CountExisting(Database db, CivilApp.CivilDocument cdoc)
    {
        int n = 0;
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            foreach (ObjectId aid in cdoc.GetAlignmentIds())
                if (tr.GetObject(aid, OpenMode.ForRead) is CivilDb.Alignment al &&
                    al.Name.StartsWith(SectionCommand.AlignBase)) n++;
            tr.Commit();
        }
        catch { }
        return n;
    }

    /// <summary>이 명령이 만든 종단도·선형을 지운다 — 선형을 지우면 딸린 종단·종단도가 같이 사라진다.
    /// <para>노란 노선은 <b>지우지 않는다</b>(JACK 확정 — 어느 선으로 만들었는지 남겨 둔다).</para></summary>
    private static int EraseExisting(Database db, CivilApp.CivilDocument cdoc)
    {
        int n = 0;
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            var victims = new System.Collections.Generic.List<ObjectId>();
            foreach (ObjectId aid in cdoc.GetAlignmentIds())
                if (tr.GetObject(aid, OpenMode.ForRead) is CivilDb.Alignment al &&
                    al.Name.StartsWith(SectionCommand.AlignBase)) victims.Add(aid);
            foreach (var id in victims)
            {
                try { (tr.GetObject(id, OpenMode.ForWrite) as Entity)?.Erase(); n++; } catch { }
            }
            tr.Commit();
        }
        catch { }
        return n;
    }

    /// <summary>로그를 파일에 남기고 요약을 알린다. <paramref name="quiet"/>=실패 경로 —
    /// 로그는 남기되 '완료' 팝업은 띄우지 않는다(곧 실패 안내가 따로 뜬다).</summary>
    private static void Finish(Editor ed, System.Text.StringBuilder log, string headline, bool quiet = false)
    {
        try { DiagLog.Append("\n■ DHPROFILE(종단도)\n  " + log.ToString().TrimEnd().Replace("\n", "\n  ") + "\n"); }
        catch { }
        // [JACK 0807 명령창 정리] 화면엔 요약만 — 자세한 내용은 로그 파일.
        ed.WriteMessage($"\n[종단도] {headline}\n  자세한 내용: {DiagLog.FilePath}");
        if (!quiet) AcadApp.ShowAlertDialog("종단도 생성 완료\n\n" + headline);
    }
}
