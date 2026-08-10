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

        // ── ⑤ 종단(원지반·정지면) ────────────────────────────────────────────
        // ★[JACK 0807 2단계] 회사 표준 스타일을 **먼저** 도면에 심는다 — 종단·종단뷰·밴드가 모두 이걸 쓴다.
        //   심는 게 늦으면 종단이 기본 스타일로 만들어져 나중에 다시 바꿔 줘야 한다.
        ProfileStyleTemplate.Import(db, cdoc);
        log.AppendLine(ProfileStyleTemplate.LastReport);
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
            string sty = ApplyViewStyle(db, cdoc, pvId, pidGround, pidPad, ed, log);
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

    /// <summary>종단도에 회사 표준 뷰 스타일을 씌우고, 아래 밴드에 <b>원지반·계획지반을 자동 지정</b>한다.
    /// <para>★[JACK 0807] "스타일 심는 것도 중요하지만 심고 나서 스타일 설정에서 원지반하고 계획지반하고
    /// 지정까지 자동으로 되어야 해." — 밴드 스타일만 붙이면 표고·누가거리 칸이 <b>빈 채로</b> 나온다.
    /// 밴드는 "1번 종단/2번 종단이 무엇인지"를 따로 지정해 줘야 값을 채우는데, Civil 3D는 그걸
    /// 사람이 종단뷰 특성창에서 손으로 고르게 되어 있다 — 매번 두 번 클릭해야 하고 빼먹기 쉽다.
    /// 여기서 만든 종단 ObjectId를 그대로 1번=원지반, 2번=정지면으로 꽂아 준다.</para></summary>
    private static string ApplyViewStyle(Database db, CivilApp.CivilDocument cdoc, ObjectId pvId,
                                         ObjectId pidGround, ObjectId pidPad,
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
                var pdBands = ProfileStyleTemplate.Collect(db, cdoc,
                                  x => x.Cls == ProfileStyleTemplate.ClsProfileDataBand);

                foreach (bool bottom in new[] { true, false })
                {
                    // ⓐ 지금 붙어 있는 칸의 (종류, 스타일이름)을 순서대로 읽는다
                    var cur = new System.Collections.Generic.List<(Autodesk.Civil.BandType T, string N)>();
                    using (var items0 = bottom ? pv.Bands.GetBottomBandItems() : pv.Bands.GetTopBandItems())
                    {
                        for (int i = 0; i < items0.Count; i++)
                        {
                            string nm = ""; var bt = Autodesk.Civil.BandType.ProfileData;
                            try
                            {
                                bt = items0[i].BandType;
                                if (tr.GetObject(items0[i].BandStyleId, OpenMode.ForRead) is
                                    Autodesk.Civil.DatabaseServices.Styles.StyleBase sb) nm = sb.Name;
                            }
                            catch { }
                            cur.Add((bt, nm));
                        }
                    }
                    if (cur.Count == 0) continue;

                    // ⓑ 횡단 데이터 칸은 같은 뜻의 종단 데이터 칸으로 바꿔 끼운다
                    var plan = new System.Collections.Generic.List<(Autodesk.Civil.BandType T, string N, string Note)>();
                    foreach (var (bt, nm) in cur)
                    {
                        if (bt == Autodesk.Civil.BandType.SectionalData && nm.Length > 0)
                        {
                            string key = nm.Substring(nm.LastIndexOf('_') + 1);   // 지반고·계획고·측점 …
                            var twin = pdBands.FirstOrDefault(b => b.Name.EndsWith("_" + key, System.StringComparison.Ordinal));
                            if (twin.Id.IsNull)
                                twin = pdBands.FirstOrDefault(b => b.Name.Contains(key, System.StringComparison.Ordinal));
                            if (!twin.Id.IsNull)
                            { plan.Add((Autodesk.Civil.BandType.ProfileData, twin.Name, $"횡단→종단 '{twin.Name}'")); swapN++; continue; }
                            plan.Add((bt, nm, "짝 없음 — 그대로(단면검토선 필요)"));
                            continue;
                        }
                        plan.Add((bt, nm, ""));
                    }

                    // ⓒ 새 목록으로 통째 교체(기존 항목의 종류는 만든 뒤에 바꿀 수 없다)
                    using var fresh = new CivilDb.ProfileViewBandItemCollection(
                        pvId, bottom ? Autodesk.Civil.BandLocationType.Bottom : Autodesk.Civil.BandLocationType.Top);
                    for (int i = 0; i < plan.Count; i++)
                    {
                        var (bt, nm, note) = plan[i];
                        if (nm.Length == 0) continue;
                        try { fresh.Add(bt, nm); }
                        catch (System.Exception ex) { detail.AppendLine($"    [{(bottom ? "하단" : "상단")} {i}] {bt} '{nm}' → 붙이기 실패:{ex.Message}"); badN++; continue; }

                        int k = fresh.Count - 1;
                        string act = note;
                        switch (bt)
                        {
                            case Autodesk.Civil.BandType.ProfileData:
                                try
                                {
                                    if (!pidGround.IsNull) fresh[k].Profile1Id = pidGround;
                                    if (!pidPad.IsNull) fresh[k].Profile2Id = pidPad;
                                    // ★ 간격이 0이면 라벨이 하나도 안 찍힌다 — JACK 스샷의 '주 간격' 칸이 비어 있었다.
                                    fresh[k].MajorInterval = band;
                                    fresh[k].MinorInterval = band;
                                    act += $" · 1=원지반 2=정지면 · 간격 {band:0.#}m"; okN++;
                                }
                                catch (System.Exception ex) { act += " · 배선실패:" + ex.Message; badN++; }
                                break;
                            case Autodesk.Civil.BandType.VerticalGeometry:
                                // 구배 밴드는 **계획 종단**의 종단선형 기하를 읽는다(원지반엔 그 기하가 없다).
                                try { if (!pidPad.IsNull) fresh[k].Profile1Id = pidPad; act += " · 1=정지면"; okN++; }
                                catch (System.Exception ex) { act += " · 배선실패:" + ex.Message; badN++; }
                                break;
                            default:
                                act += " · 대상아님"; naN++;
                                break;
                        }
                        detail.AppendLine($"    [{(bottom ? "하단" : "상단")} {i}] {bt} '{nm}' → {act.TrimStart(' ', '·')}");
                    }
                    if (bottom) pv.Bands.SetBottomBandItems(fresh); else pv.Bands.SetTopBandItems(fresh);
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
