using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;
using AcadEntity = Autodesk.AutoCAD.DatabaseServices.Entity;
using CivilApp = Autodesk.Civil.ApplicationServices;
using CivilDb = Autodesk.Civil.DatabaseServices;
using CivilStyles = Autodesk.Civil.DatabaseServices.Styles;

namespace DH.Grading.Civil.Commands;

/// <summary>
/// [종단·횡단 — JACK 0731] 정지면 위에 선을 하나 그으면 그 선을 따라 <b>종단면도 + 횡단면도</b>를 만든다(DHSECTION).
///
/// 직접 그림을 그리는 게 아니라 <b>Civil3D 정식 객체</b>(선형·종단·측점선·단면뷰)로 만들기 때문에
///  · 정지면을 고치면 종단·횡단이 자동으로 따라 갱신되고
///  · Civil3D 기본 라벨·토공량·도면출력 기능을 그대로 쓸 수 있으며
///  · 원지반과 정지면이 한 그림에 겹쳐 나와 절토/성토가 한눈에 보인다.
///
/// 흐름: 선 클릭 → (선형 생성) → 종단 2개 → 종단도 놓을 위치 클릭 → 측점선 → 횡단도 놓을 위치 클릭 → 격자 배치.
/// 간격·좌우 폭·열 수는 <b>도면설정</b> 값을 쓴다.
/// </summary>
public sealed class SectionCommand
{
    internal const string LayerAlign = "DH-종단선";
    internal const string AlignBase = "DH선형";      // 선형 이름 앞머리(초기화 판정 공용)
    internal const string GroupBase = "DH횡단";      // 측점선그룹·횡단도 이름 앞머리
    internal const string ProfGroundName = "DH_원지반";
    internal const string ProfPadName = "DH_정지면";
    private const string PadSurfaceBase = "정지면_DH";

    /// <summary>★★[v32.2 · JACK 0812] <b>순수 정지면 — 원지반을 안 깔고 '정지된 면만' 담은 지표면.</b>
    ///
    /// <para>JACK: <i>"인프라웍스 내보내기 때문에 계획정지면하고 원지반하고 합성한 걸 최종결과물로 만드는데,
    /// 오히려 종단에서는 더 불리한 것 같더라고. 일반적으로 종단은 원지반에 계획지반선만 보이는데
    /// 우리는 원지반 부분이 겹치니까."</i></para>
    ///
    /// <para><b>맞는 지적이다.</b> <c>정지면_DH</c>는 원지반을 깔고 그 위에 절·성토를 얹은 합성면이라
    /// <b>정지 바깥에서도 값이 나온다</b> — 그 값은 원지반과 <b>똑같다</b>.
    /// 그래서 종단을 뜨면 정지 밖 구간에서 계획선이 원지반선 위에 <b>포개져</b> 두 줄이 겹쳐 보인다.
    /// 설계도서의 종단면도는 원지반선 하나에 계획선을 <b>정지 구간에만</b> 얹는다.</para>
    ///
    /// <para><b>그렇다고 합성면을 없앨 수는 없다</b> — InfraWorks 지형·토공량·'이어서 하기'의 기준 지반이
    /// 전부 그것이다(원지반이 안 깔리면 다음 구역 사면이 만날 지반이 없어 데이라잇이 안 나온다).
    /// → 합성면은 그대로 두고 <b>종단·횡단만 이 순수면을 본다.</b> 기존 기능은 하나도 안 건드린다.</para>
    ///
    /// <para>옛 도면에는 이 표면이 없다 — 그때는 <b>합성면으로 물러난다</b>(종전 동작 그대로).</para></summary>
    internal const string PurePadSurfaceBase = "정지순수_DH";

    /// <summary>측점선(=횡단도) 개수 상한 — 넘으면 안내하고 중단(도면이 감당 못 할 양 방지).
    /// <para>★[v32.25 · 검토 지적] <c>DHPROFILE</c>도 같은 상한을 본다 — 종전엔 이 관문이
    /// <b>이 문에만</b> 달려 있었는데, 원지반 굴곡부(v32.21)가 측점의 새 공급원을 열면서
    /// 다른 문으로 수백 개가 들어올 수 있게 됐다.</para></summary>
    internal const int MaxSections = 200;

    [CommandMethod("DHSECTION")]
    public void Run()
    {
        Document doc = AcadApp.DocumentManager.MdiActiveDocument;
        if (doc == null) return;
        GradingSettings.SyncToDocument(doc);   // [도면 전환 0803] 도면이 바뀌었으면 그 도면 기준으로 설정·기억 재정렬
        Editor ed = doc.Editor;
        try { RunCore(doc, ed, doc.Database); }
        catch (System.Exception ex)
        {
            ed.WriteMessage("\n[종단/횡단 오류] " + ex.Message);
            AcadApp.ShowAlertDialog("종단·횡단 생성 중 오류:\n" + ex.Message);
            try { DiagLog.Append($"\n■ DHSECTION 오류 — {ex}\n"); } catch { }
        }
    }

    private static void RunCore(Document doc, Editor ed, Database db)
    {
        var cdoc = CivilApp.CivilApplication.ActiveDocument;

        // ── ① 대상 지표면(원지반·정지면) ─────────────────────────────────────
        var surfs = FindSurfaces(db, cdoc);
        if (surfs.Count == 0)
        {
            Refuse(ed, "종단·횡단을 만들 지표면이 없습니다.\n\n" +
                       "먼저 [서버지표면]으로 원지반을 만들거나 [부지정지]를 실행하세요.");
            return;
        }
        ed.WriteMessage("\n[종단/횡단] 대상 지표면: " +
            string.Join(" · ", surfs.ConvertAll(s => s.Label + "=" + s.SurfName)));

        // ── ② 노선으로 쓸 선 클릭 ────────────────────────────────────────────
        var peo = new PromptEntityOptions("\n[종단/횡단] 노선으로 쓸 선을 클릭 (Esc=취소): ");
        peo.SetRejectMessage("\n선(폴리선·2D/3D 폴리선·선분)만 선택할 수 있습니다.");
        peo.AddAllowedClass(typeof(Polyline), false);
        peo.AddAllowedClass(typeof(Polyline2d), false);
        peo.AddAllowedClass(typeof(Polyline3d), false);
        peo.AddAllowedClass(typeof(Line), false);
        var per = ed.GetEntity(peo);
        if (per.Status != PromptStatus.OK) return;

        // ── ③ 선형 생성 ─────────────────────────────────────────────────────
        //   사용자가 그린 선은 **그대로 두고**, 평면에 눕힌 사본을 하나 만들어 그것으로 선형을 만든다.
        //   (선형 생성 API가 원본을 지워버리는 옵션밖에 없어서 — 사본을 지우게 해 원본을 지킨다.
        //    또한 3D 폴리선·선분도 이 경로로 한 번에 처리된다.)
        ObjectId layerId;
        using (var tr = db.TransactionManager.StartTransaction())
        { layerId = EnsureLayer(db, tr, LayerAlign, 4); tr.Commit(); }

        ObjectId flatId = MakeFlatCopy(db, per.ObjectId, layerId, out int nv, out double lineLen);
        if (flatId.IsNull)
        {
            Refuse(ed, "선의 점이 2개 미만이라 노선을 만들 수 없습니다.");
            return;
        }
        if (lineLen < 1.0)
        {
            EraseQuiet(db, flatId);
            Refuse(ed, $"선이 너무 짧습니다({lineLen:F2}m). 1m 이상인 선을 그려 주세요.");
            return;
        }

        string alignName = UniqueName(db, cdoc, AlignBase);
        ObjectId alignId;
        try
        {
            var plo = new CivilDb.PolylineOptions
            {
                PlineId = flatId,
                EraseExistingEntities = true,    // 지워지는 건 **사본** — 사용자 선은 남는다
                AddCurvesBetweenTangents = false,
            };
            alignId = CivilDb.Alignment.Create(
                cdoc, plo, alignName, ObjectId.Null, layerId,
                PickStyle(db, cdoc.Styles.AlignmentStyles, "기본", "Standard", "Basic"),
                PickStyle(db, cdoc.Styles.LabelSetStyles.AlignmentLabelSetStyles, "_없음", "None", "표준", "Standard"));
        }
        catch (System.Exception ex)
        {
            EraseQuiet(db, flatId);
            Refuse(ed, "노선(선형)을 만들지 못했습니다.\n" + ex.Message);
            return;
        }
        ed.WriteMessage($"\n[종단/횡단] 선형 '{alignName}' 생성 (길이 {lineLen:F1}m · 점 {nv}개)");

        // ── ④ 종단 2개(원지반·정지면) ────────────────────────────────────────
        ObjectId profStyle = PickStyle(db, cdoc.Styles.ProfileStyles, "기본", "Standard", "Basic");
        ObjectId excStyle = EnsureExcavProfileStyle(db, cdoc);   // ★[0824] 터파기 = 마젠타
        ObjectId profLabels = PickStyle(db, cdoc.Styles.LabelSetStyles.ProfileLabelSetStyles, "_없음", "None", "표준", "Standard");
        int nProf = 0;
        foreach (var s in surfs)
        {
            try
            {
                // ★[JACK 0824] 터파기 종단선만 **마젠타** 스타일로.
                var styleFor = s.Label == "터파기" && !excStyle.IsNull ? excStyle : profStyle;
                var pid0 = CivilDb.Profile.CreateFromSurface(s.ProfileName, alignId, s.SurfId, layerId, styleFor, profLabels);
                if (s.Label == "터파기") PaintExcavProfile(db, pid0);   // ★[JACK 0825] 스타일만으론 안 된다(ByLayer가 이긴다)
                nProf++;
            }
            catch (System.Exception ex)
            { ed.WriteMessage($"\n  · 종단 '{s.ProfileName}' 생성 실패 — {ex.Message}"); }
        }
        if (nProf == 0)
        {
            Refuse(ed, "종단을 하나도 만들지 못했습니다.\n선이 지표면 범위 밖일 수 있습니다.");
            return;
        }

        // ── ⑤ 종단도 배치 ───────────────────────────────────────────────────
        var pvPt = ed.GetPoint("\n[종단/횡단] 종단면도를 놓을 위치 클릭 (Esc=종단만 만들고 끝): ");
        if (pvPt.Status != PromptStatus.OK)
        {
            Done(ed, $"선형 '{alignName}' · 종단 {nProf}개 생성(종단도 배치는 건너뜀)");
            return;
        }
        try { CivilDb.ProfileView.Create(alignId, pvPt.Value.TransformBy(ed.CurrentUserCoordinateSystem)); }
        catch (System.Exception ex)
        {
            ed.WriteMessage("\n  · 종단면도 생성 실패 — " + ex.Message);
            AcadApp.ShowAlertDialog("종단면도를 만들지 못했습니다.\n" + ex.Message +
                                    "\n\n선형과 종단은 만들어졌으니 Civil3D 기본 기능으로도 배치할 수 있습니다.");
        }

        // ── ⑥ 측점선(횡단 위치) ─────────────────────────────────────────────
        double interval = System.Math.Max(0.5, GradingSettings.XsecInterval);
        double wl = System.Math.Max(0.0, GradingSettings.XsecLeft);
        double wr = System.Math.Max(0.0, GradingSettings.XsecRight);
        if (wl + wr < 0.5) { wl = wr = 30.0; }

        var cuts = PlanSampleLines(db, alignId, interval, wl, wr, out double stStart, out double stEnd);
        if (cuts.Count == 0)
        {
            Done(ed, $"선형 '{alignName}' · 종단 {nProf}개 · 종단면도 완료 (횡단 위치를 잡지 못해 횡단은 생략)");
            return;
        }
        if (cuts.Count > MaxSections)
        {
            AcadApp.ShowAlertDialog(
                $"횡단이 {cuts.Count}개나 됩니다(간격 {interval:0.#}m · 노선 {lineLen:F0}m).\n" +
                $"도면이 감당하기 어려워 횡단은 만들지 않았습니다.\n\n" +
                $"도면설정에서 '횡단 간격'을 늘린 뒤 다시 실행하세요(예 {System.Math.Ceiling(lineLen / 50)}m 이상).");
            Done(ed, $"선형 '{alignName}' · 종단 {nProf}개 · 종단면도 완료 (횡단 {cuts.Count}개 → 상한 초과로 생략)");
            return;
        }

        string groupName = UniqueName(db, cdoc, GroupBase);
        ObjectId groupId;
        try { groupId = CivilDb.SampleLineGroup.Create(groupName, alignId); }
        catch (System.Exception ex)
        {
            ed.WriteMessage("\n  · 측점선 그룹 생성 실패 — " + ex.Message);
            Done(ed, $"선형 '{alignName}' · 종단 {nProf}개 · 종단면도 완료 (횡단 실패: {ex.Message})");
            return;
        }

        // 이 그룹이 표본으로 삼을 지표면 = 우리 두 지표면만 켠다.
        int nSrc = 0;
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            var g = (CivilDb.SampleLineGroup)tr.GetObject(groupId, OpenMode.ForWrite);
            foreach (CivilDb.SectionSource src in g.GetSectionSources())
            {
                bool ours = surfs.Exists(s => s.SurfId == src.SourceId);
                try { src.IsSampled = ours; if (ours) nSrc++; } catch { }
            }
            tr.Commit();
        }
        catch (System.Exception ex) { ed.WriteMessage("\n  · 표본 지표면 지정 경고 — " + ex.Message); }

        int nSl = 0;
        var slIds = new System.Collections.Generic.List<ObjectId>();
        for (int i = 0; i < cuts.Count; i++)
        {
            try
            {
                var pts = new Point2dCollection { cuts[i].Left, cuts[i].Right };
                ObjectId slId = CivilDb.SampleLine.Create($"{groupName}_{i + 1}", groupId, pts);
                if (!slId.IsNull) { slIds.Add(slId); nSl++; }
            }
            catch (System.Exception ex)
            { if (nSl == 0) ed.WriteMessage($"\n  · 측점선 생성 실패(첫 실패 St.{cuts[i].Station:F1}) — {ex.Message}"); }
        }
        ed.WriteMessage($"\n[종단/횡단] 측점선 {nSl}개 (간격 {interval:0.#}m · 좌 {wl:0.#}m / 우 {wr:0.#}m · 표본 지표면 {nSrc}개)");
        if (nSl == 0)
        {
            Done(ed, $"선형 '{alignName}' · 종단 {nProf}개 · 종단면도 완료 (측점선 생성 실패)");
            return;
        }

        // ── ⑦ 횡단도 격자 배치 ──────────────────────────────────────────────
        var svPt = ed.GetPoint("\n[종단/횡단] 횡단면도를 놓을 위치(왼쪽 위) 클릭 (Esc=측점선까지만): ");
        if (svPt.Status != PromptStatus.OK)
        {
            Done(ed, $"선형 '{alignName}' · 종단 {nProf}개 · 종단면도 · 측점선 {nSl}개 (횡단도 배치는 건너뜀)");
            return;
        }
        int nSv = PlaceSectionViews(db, ed, slIds, groupName,
                                    svPt.Value.TransformBy(ed.CurrentUserCoordinateSystem),
                                    System.Math.Max(1, GradingSettings.XsecCols), wl + wr);

        ed.Regen();
        string msg = $"선형 '{alignName}' · 종단 {nProf}개 · 종단면도 1개 · 측점선 {nSl}개 · 횡단면도 {nSv}개";
        Done(ed, msg);
        AcadApp.ShowAlertDialog("종단·횡단 생성 완료\n\n" + msg +
            $"\n\n· 횡단 간격 {interval:0.#}m · 좌 {wl:0.#}m / 우 {wr:0.#}m · 가로 {GradingSettings.XsecCols}개씩" +
            "\n· 간격·폭·열 수는 [도면설정]에서 바꿉니다." +
            "\n· 정지면을 고치면 종단·횡단은 Civil3D가 자동으로 갱신합니다.");
        try { DiagLog.Append($"\n■ DHSECTION — {msg} · 간격{interval} 좌{wl} 우{wr} St.{stStart:F1}~{stEnd:F1}\n"); } catch { }
    }

    // ── 지표면 찾기 ──────────────────────────────────────────────────────────

    /// <summary>종단에 쓸 지표면(원지반·정지면) — 있는 것만 모은다.</summary>
    internal readonly record struct SurfPick(ObjectId SurfId, string SurfName, string ProfileName, string Label);

    /// <summary>★[JACK 0824] 터파기 종단 이름 — 원지반·정지면과 나란히 놓인다.</summary>
    internal const string ProfExcavName = "DH_터파기";

    /// <summary>터파기 종단선 스타일 이름 — 마젠타(JACK 0824).</summary>
    internal const string ExcavStyleName = "DH_터파기(마젠타)";

    /// <summary>★★[JACK 0825] <b>터파기 종단선을 마젠타로 — 객체 색을 직접 박는다.</b>
    ///
    /// <para>스타일(<c>DH_터파기(마젠타)</c>)만으로는 안 됐다. JACK 특성창 실측:</para>
    /// <code>
    /// 스타일    : DH_터파기(마젠타)   ← 스타일은 맞다
    /// 트루 컬러 : ByLayer            ← 그런데 객체 색이 ByLayer이고
    /// 도면층    : CR-GRND            ← 그 레이어가 원지반(초록)이다
    /// </code>
    /// <para>종단 객체가 <b>ByLayer</b>면 레이어 색이 스타일 색을 이긴다. 종단은 선형 레이어에
    /// 만들어지는데 그게 원지반 레이어라, 스타일을 아무리 마젠타로 해도 초록으로 나왔다.</para>
    ///
    /// <para>→ <b>객체 색을 명시</b>한다. ByLayer가 아니게 되면 레이어와 무관하게 그 색으로 그려진다.</para></summary>
    internal static void PaintExcavProfile(Database db, ObjectId profileId)
    {
        if (profileId.IsNull) return;
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            if (tr.GetObject(profileId, OpenMode.ForWrite) is Entity pe)
            {
                pe.Color = Autodesk.AutoCAD.Colors.Color.FromColorIndex(
                    Autodesk.AutoCAD.Colors.ColorMethod.ByAci, 6);   // ACI 6 = 마젠타
                try { DiagLog.Append("  터파기 종단 객체 색 = 마젠타(ACI6) 직접 지정 — ByLayer면 레이어 색이 이긴다"); } catch { }
            }
            tr.Commit();
        }
        catch (System.Exception ex) { try { DiagLog.Append("  터파기 종단 색 지정 실패 — " + ex.Message); } catch { } }
    }

    /// <summary>★[JACK 0824] <b>터파기 종단선은 마젠타.</b> 그 색의 종단 스타일을 만들어 두고 그 ObjectId를 준다.
    /// <para>이미 있으면 그대로 쓴다(매번 만들면 도면에 스타일이 쌓인다). 만들지 못하면
    /// <c>ObjectId.Null</c>을 돌려주고, 호출부는 기본 스타일로 물러난다 — 색 하나 때문에 종단이 안 생기면 안 된다.</para></summary>
    internal static ObjectId EnsureExcavProfileStyle(Database db, CivilApp.CivilDocument cdoc)
    {
        const short Magenta = 6;   // ACI 6 = 마젠타
        try
        {
            var coll = cdoc.Styles.ProfileStyles;
            ObjectId id = ObjectId.Null;
            foreach (ObjectId sid in coll)
            {
                using var tr0 = db.TransactionManager.StartTransaction();
                try
                {
                    if (tr0.GetObject(sid, OpenMode.ForRead) is CivilStyles.ProfileStyle st0 && st0.Name == ExcavStyleName)
                        id = sid;
                }
                catch { }
                tr0.Commit();
                if (!id.IsNull) break;
            }
            if (id.IsNull) id = coll.Add(ExcavStyleName);

            using var tr = db.TransactionManager.StartTransaction();
            if (tr.GetObject(id, OpenMode.ForWrite) is CivilStyles.ProfileStyle st)
            {
                // 선·곡선·연장선까지 같은 색으로 — 하나만 바꾸면 곡선 구간이 다른 색으로 남는다.
                foreach (var t in new[]
                {
                    CivilStyles.ProfileDisplayStyleProfileType.Line,
                    CivilStyles.ProfileDisplayStyleProfileType.Curve,
                    CivilStyles.ProfileDisplayStyleProfileType.LineExtension,
                    CivilStyles.ProfileDisplayStyleProfileType.SymmetricalParabola,
                    CivilStyles.ProfileDisplayStyleProfileType.AsymmetricalParabola,
                    CivilStyles.ProfileDisplayStyleProfileType.ParabolicCurveExtension,
                })
                {
                    try
                    {
                        var ds = st.GetDisplayStyleProfile(t);
                        if (ds == null) continue;
                        // ★ 이미 같으면 쓰지 않는다 — 값이 같아도 쓰는 행위 자체가 Civil에게는 '수정'이다.
                        if (ds.Color.ColorIndex != Magenta)
                            ds.Color = Autodesk.AutoCAD.Colors.Color.FromColorIndex(
                                Autodesk.AutoCAD.Colors.ColorMethod.ByAci, Magenta);
                        ds.Visible = true;
                        // ★[JACK 0825] 표시 레이어를 <b>0</b>으로 못 박는다.
                        //   Civil 문서: 컴포넌트의 Layer는 "값이 ByLayer일 때 참조된다"이고
                        //   0은 "그려진 레이어와 같다"는 뜻이다. 색을 명시했으니 원칙상 무관하지만,
                        //   새 스타일이 어느 프로토타입에서 복제됐는지 알 수 없어 우연히 특정 레이어를
                        //   물고 있을 여지를 막는다.
                        try { if (ds.Layer != "0") ds.Layer = "0"; } catch { }
                    }
                    catch { }
                }
            }
            tr.Commit();

            // ★★[JACK 0825 '터파기 종단이 여전히 마젠타로 안 나온다'] <b>썼으면 되읽어 확인한다.</b>
            //   색을 쓰는 코드가 try/catch로 감싸여 있어 <b>실패해도 아무 말이 없었다</b> —
            //   로그엔 "스타일 'DH_터파기(마젠타)'"라고 찍히는데 정작 색은 안 바뀐 채일 수 있다.
            //   <b>스타일 이름이 붙은 것과 그 스타일이 마젠타인 것은 다른 일이다.</b>
            try
            {
                using var trV = db.TransactionManager.StartTransaction();
                if (trV.GetObject(id, OpenMode.ForRead) is CivilStyles.ProfileStyle stV)
                {
                    var sbV = new System.Text.StringBuilder();
                    int okN = 0, badN = 0;
                    foreach (var t in new[]
                    {
                        CivilStyles.ProfileDisplayStyleProfileType.Line,
                        CivilStyles.ProfileDisplayStyleProfileType.Curve,
                        CivilStyles.ProfileDisplayStyleProfileType.LineExtension,
                    })
                    {
                        try
                        {
                            var dsV = stV.GetDisplayStyleProfile(t);
                            if (dsV == null) { sbV.Append($" {t}=없음"); badN++; continue; }
                            short ci = dsV.Color.ColorIndex;
                            bool vis = dsV.Visible;
                            if (ci == Magenta && vis) okN++; else badN++;
                            // ★[JACK 0825] <b>레이어까지 본다.</b> 색이 ACI6인데도 초록으로 나온다면
                            //   스타일 컴포넌트가 <b>자기 레이어</b>에 그리고 그 레이어 색이 이기는 것이다.
                            string lay = "?";
                            try { lay = dsV.Layer ?? "(빈값)"; } catch { }
                            sbV.Append($" {t}=ACI{ci}@{lay}{(vis ? "" : "(숨김)")}");
                        }
                        catch (System.Exception exV) { sbV.Append($" {t}=읽기실패({exV.GetType().Name})"); badN++; }
                    }
                    trV.Commit();
                    try { DiagLog.Append($"\n  터파기 종단 스타일 되읽기 — 마젠타 {okN}개 · 어긋남 {badN}개 ·{sbV}" +
                                         (badN > 0 ? "  ⚠색이 안 먹었다" : "")); } catch { }
                }
                else trV.Commit();
            }
            catch { }

            return id;
        }
        catch { return ObjectId.Null; }
    }

    internal static System.Collections.Generic.List<SurfPick> FindSurfaces(Database db, CivilApp.CivilDocument cdoc)
    {
        var list = new System.Collections.Generic.List<SurfPick>();
        ObjectId ground = ObjectId.Null, pad = ObjectId.Null, pure = ObjectId.Null, exc = ObjectId.Null;
        string groundNm = "", padNm = "", pureNm = "", excNm = "";

        using var tr = db.TransactionManager.StartTransaction();
        foreach (ObjectId sid in cdoc.GetSurfaceIds())
        {
            string nm;
            try
            {
                if (tr.GetObject(sid, OpenMode.ForRead) is not CivilDb.Surface s) continue;
                nm = s.Name;
            }
            catch { continue; }

            // ★[JACK 0824] 터파기 지표면 — 있으면 종단에 선 하나가 더 그려진다.
            //   굴착 형상만이라(바닥+법면) 구조물 위에만 나온다 — JACK: "순수하게 터파기선만 나오면 돼".
            if (IsBase(nm, Commands.ExcavCommand.SurfName)) { exc = sid; excNm = nm; continue; }
            // 터파기 작업용 중간 산물은 종단 대상이 아니다(목표면·복원 절토부).
            if (IsBase(nm, Commands.ExcavCommand.BaseName) || nm.StartsWith("터파기_절토복원")) continue;
            // ★[v32.2] 순수 정지면이 있으면 그것이 종단·횡단의 정지면이다(위 설명).
            if (IsBase(nm, PurePadSurfaceBase)) { pure = sid; pureNm = nm; continue; }
            // 정지면_DH(또는 정지면_DH_N) — 가장 마지막 것을 쓴다.
            if (IsBase(nm, PadSurfaceBase)) { pad = sid; padNm = nm; continue; }
            // 원지반(서버지표면이 만든 것) 우선
            if (IsBase(nm, ImportGisCommand.GroundSurfaceName)) { ground = sid; groundNm = nm; }
        }

        // 원지반이 없으면 — 우리 산출물이 아닌 지표면 중 삼각형이 가장 많은 것을 원지반으로 본다(DHINFRA와 같은 규칙).
        if (ground.IsNull)
        {
            int best = -1;
            foreach (ObjectId sid in cdoc.GetSurfaceIds())
            {
                try
                {
                    if (tr.GetObject(sid, OpenMode.ForRead) is not CivilDb.TinSurface ts) continue;
                    string nm = ts.Name;
                    if (nm.Contains("_DH") || IsBase(nm, PadSurfaceBase)) continue;   // DH 산출물 제외
                    int n = 0; try { n = ts.Triangles.Count; } catch { }
                    if (n > best) { best = n; ground = sid; groundNm = nm; }
                }
                catch { }
            }
        }
        tr.Commit();

        if (!ground.IsNull) list.Add(new SurfPick(ground, groundNm, ProfGroundName, "원지반"));
        // ★[v32.2] 순수면이 있으면 그것을, 없으면 합성면으로 물러난다(옛 도면 호환).
        if (!pure.IsNull) list.Add(new SurfPick(pure, pureNm, ProfPadName, "정지면"));
        else if (!pad.IsNull) list.Add(new SurfPick(pad, padNm, ProfPadName, "정지면"));
        // ★[JACK 0824] 터파기는 맨 뒤에 — 밴드가 종단1(원지반)·종단2(정지면)를 이름 순서가 아니라
        //   이 목록 순서로 잡으므로, 앞에 끼워 넣으면 밴드 값이 통째로 밀린다.
        if (!exc.IsNull) list.Add(new SurfPick(exc, excNm, ProfExcavName, "터파기"));
        return list;
    }

    /// <summary>이름이 baseName 또는 baseName_숫자 인가.</summary>
    private static bool IsBase(string nm, string baseName) =>
        nm == baseName ||
        (nm.StartsWith(baseName + "_") && int.TryParse(nm.Substring(baseName.Length + 1), out _));

    // ── 선 → 평면 사본 ───────────────────────────────────────────────────────

    /// <summary>선택한 선을 평면(Z=0)에 눕힌 폴리선 사본으로 만든다. 원본은 건드리지 않는다.
    ///
    /// 주의 두 가지:
    ///  · 옛날식 폴리선(2D/3D)에 스플라인이 걸려 있으면 화면에 보이지 않는 <b>조종점</b>이 정점 목록에 섞여 있다.
    ///    그대로 주워 담으면 노선이 그린 모양과 다르게 휘는데 오류도 안 나서 알아채기 어렵다 → 조종점은 뺀다.
    ///  · 호(둥근 구간)는 <b>bulge</b> 값에 들어 있다. 안 옮기면 곡선 노선이 직선으로 펴져버린다 → 같이 옮긴다.</summary>
    internal static ObjectId MakeFlatCopy(Database db, ObjectId srcId, ObjectId layerId, out int nv, out double len)
    {
        nv = 0; len = 0;
        var pts = new System.Collections.Generic.List<(Point2d P, double B)>();   // B=bulge(호 정도, 0=직선)
        using var tr = db.TransactionManager.StartTransaction();
        try
        {
            switch (tr.GetObject(srcId, OpenMode.ForRead))
            {
                case Polyline lw:   // 요즘 폴리선(LWPOLYLINE) — 호는 bulge로 들고 있다
                    for (int i = 0; i < lw.NumberOfVertices; i++)
                        pts.Add((lw.GetPoint2dAt(i), SafeBulge(lw, i)));
                    if (lw.Closed && pts.Count > 1) pts.Add((pts[0].P, 0.0));
                    break;
                case Polyline3d p3:   // 3D 폴리선 — 구간은 항상 직선(bulge 없음)
                    foreach (ObjectId vid in p3)
                        if (tr.GetObject(vid, OpenMode.ForRead) is PolylineVertex3d v &&
                            v.VertexType != Vertex3dType.ControlVertex)          // 조종점 제외
                            pts.Add((new Point2d(v.Position.X, v.Position.Y), 0.0));
                    if (p3.Closed && pts.Count > 1) pts.Add((pts[0].P, 0.0));
                    break;
                case Polyline2d p2:   // 옛날식 2D 폴리선
                    foreach (ObjectId vid in p2)
                        if (tr.GetObject(vid, OpenMode.ForRead) is Vertex2d v &&
                            v.VertexType != Vertex2dType.SplineControlVertex)     // 조종점 제외
                            pts.Add((new Point2d(v.Position.X, v.Position.Y), v.Bulge));
                    if (p2.Closed && pts.Count > 1) pts.Add((pts[0].P, 0.0));
                    break;
                case Line ln:
                    pts.Add((new Point2d(ln.StartPoint.X, ln.StartPoint.Y), 0.0));
                    pts.Add((new Point2d(ln.EndPoint.X, ln.EndPoint.Y), 0.0));
                    break;
            }
        }
        catch { }

        // 겹친 점 제거(선형 생성이 0길이 구간을 싫어한다). 겹친 점을 버릴 때 그 점의 호 정보도 같이 버린다.
        var clean = new System.Collections.Generic.List<(Point2d P, double B)>();
        foreach (var v in pts)
            if (clean.Count == 0 || clean[clean.Count - 1].P.GetDistanceTo(v.P) > 1e-6) clean.Add(v);
        nv = clean.Count;
        if (clean.Count < 2) { tr.Commit(); return ObjectId.Null; }
        for (int i = 1; i < clean.Count; i++) len += clean[i - 1].P.GetDistanceTo(clean[i].P);

        var pl = new Polyline(clean.Count);
        for (int i = 0; i < clean.Count; i++) pl.AddVertexAt(i, clean[i].P, clean[i].B, 0, 0);
        pl.Elevation = 0;
        pl.LayerId = layerId;
        var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
        ms.AppendEntity(pl);
        tr.AddNewlyCreatedDBObject(pl, true);
        ObjectId id = pl.ObjectId;
        tr.Commit();
        return id;
    }

    /// <summary>LWPOLYLINE의 호(bulge) 읽기 — 마지막 정점 등에서 예외가 날 수 있어 감싼다.</summary>
    private static double SafeBulge(Polyline lw, int i)
    { try { return lw.GetBulgeAt(i); } catch { return 0.0; } }

    // ── 측점선 위치 계산 ─────────────────────────────────────────────────────

    internal readonly record struct Cut(double Station, Point2d Left, Point2d Right);

    /// <summary>선형을 따라 간격마다 좌/우 폭 지점 2개를 뽑는다(폭을 확실히 제어하려고 좌표 방식 사용).</summary>
    private static System.Collections.Generic.List<Cut> PlanSampleLines(
        Database db, ObjectId alignId, double interval, double wl, double wr,
        out double stStart, out double stEnd)
    {
        var list = new System.Collections.Generic.List<Cut>();
        stStart = stEnd = 0;
        using var tr = db.TransactionManager.StartTransaction();
        try
        {
            var al = (CivilDb.Alignment)tr.GetObject(alignId, OpenMode.ForRead);
            stStart = al.StartingStation;
            stEnd = al.EndingStation;
            // 시작/끝은 선형 끄트머리에서 아주 살짝 안쪽으로(끝점 정확히에서 법선 계산이 실패하는 경우 방지)
            const double eps = 0.001;
            for (double s = stStart; s <= stEnd + 1e-9; s += interval)
            {
                double st = System.Math.Min(System.Math.Max(s, stStart + eps), stEnd - eps);
                if (TryCut(al, st, wl, wr, out var c)) list.Add(c);
            }
            // 마지막 측점이 끝에서 멀면 끝단도 하나 추가
            if (list.Count > 0 && stEnd - list[list.Count - 1].Station > interval * 0.25)
                if (TryCut(al, stEnd - eps, wl, wr, out var cEnd)) list.Add(cEnd);
        }
        catch { }
        tr.Commit();
        return list;
    }

    internal static bool TryCut(CivilDb.Alignment al, double st, double wl, double wr, out Cut cut)
    {
        cut = default;
        try
        {
            // offset 부호: 양수 = 진행방향 오른쪽 (Civil3D 규약). 이 API는 out이 아니라 ref라 미리 초기화한다.
            double le = 0, ln = 0, re = 0, rn = 0;
            al.PointLocation(st, -wl, ref le, ref ln);
            al.PointLocation(st, wr, ref re, ref rn);
            var L = new Point2d(le, ln);
            var R = new Point2d(re, rn);
            if (L.GetDistanceTo(R) < 0.1) return false;
            cut = new Cut(st, L, R);
            return true;
        }
        catch { return false; }
    }

    // ── 횡단도 격자 배치 ─────────────────────────────────────────────────────

    /// <summary>측점선마다 횡단도를 만들어 가로 cols개씩 격자로 놓는다. 칸 크기는 만들어진 뷰의 실제 크기로 잰다.</summary>
    private static int PlaceSectionViews(Database db, Editor ed,
        System.Collections.Generic.List<ObjectId> slIds, string groupName,
        Point3d origin, int cols, double totalWidth)
    {
        double gap = System.Math.Max(5.0, totalWidth * 0.15);   // 칸 사이 여백
        double cellW = totalWidth + gap;                        // 첫 뷰를 재기 전 임시값
        double rowH = 0, maxRowH = 0;
        int made = 0, col = 0;
        double x = origin.X, y = origin.Y;

        for (int i = 0; i < slIds.Count; i++)
        {
            ObjectId svId;
            try { svId = CivilDb.SectionView.Create($"{groupName}_횡단_{i + 1}", slIds[i], new Point3d(x, y, 0)); }
            catch (System.Exception ex)
            {
                if (made == 0) ed.WriteMessage($"\n  · 횡단면도 생성 실패(첫 실패 {i + 1}번) — {ex.Message}");
                continue;
            }
            made++;

            // 실제 크기 측정 → 다음 칸 위치에 반영(스타일·축척이 도면마다 달라 고정값을 쓰면 겹치거나 벌어진다)
            double w = cellW, h = rowH;
            try
            {
                using var tr = db.TransactionManager.StartTransaction();
                var ext = ((AcadEntity)tr.GetObject(svId, OpenMode.ForRead)).GeometricExtents;
                w = ext.MaxPoint.X - ext.MinPoint.X;
                h = ext.MaxPoint.Y - ext.MinPoint.Y;
                tr.Commit();
            }
            catch { }
            if (w > 0.1) cellW = w + gap;
            if (h > maxRowH) maxRowH = h;

            col++;
            if (col >= cols)
            {
                col = 0;
                x = origin.X;
                y -= (maxRowH > 0.1 ? maxRowH : 40.0) + gap;   // 다음 줄은 아래로
                maxRowH = 0;
            }
            else x += cellW;
        }
        return made;
    }

    // ── 공통 ────────────────────────────────────────────────────────────────

    /// <summary>스타일 고르기 — 이름에 후보 문자열이 든 것 우선, 없으면 첫 번째. 하나도 없으면 Null.</summary>
    internal static ObjectId PickStyle(Database db, System.Collections.IEnumerable styleIds, params string[] prefer)
    {
        ObjectId first = ObjectId.Null;
        var all = new System.Collections.Generic.List<(ObjectId Id, string Name)>();
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            foreach (ObjectId id in styleIds)
            {
                if (first.IsNull) first = id;
                try
                {
                    if (tr.GetObject(id, OpenMode.ForRead) is CivilStyles.StyleBase sb)
                        all.Add((id, sb.Name ?? ""));
                }
                catch { }
            }
            tr.Commit();
        }
        catch { }
        foreach (var want in prefer)
            foreach (var (id, nm) in all)
                if (nm.IndexOf(want, System.StringComparison.OrdinalIgnoreCase) >= 0) return id;
        return first;
    }

    /// <summary>선형/그룹 이름 중복 회피 — DH선형_1, DH선형_2 …</summary>
    internal static string UniqueName(Database db, CivilApp.CivilDocument cdoc, string baseName)
    {
        var used = new System.Collections.Generic.HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            foreach (ObjectId aid in cdoc.GetAlignmentIds())
            {
                try
                {
                    if (tr.GetObject(aid, OpenMode.ForRead) is not CivilDb.Alignment al) continue;
                    used.Add(al.Name);
                    foreach (ObjectId gid in al.GetSampleLineGroupIds())
                        try
                        {
                            if (tr.GetObject(gid, OpenMode.ForRead) is CivilDb.SampleLineGroup g) used.Add(g.Name);
                        }
                        catch { }
                }
                catch { }
            }
            tr.Commit();
        }
        catch { }
        for (int i = 1; i < 10000; i++)
        {
            string nm = $"{baseName}_{i}";
            if (!used.Contains(nm)) return nm;
        }
        return baseName + "_X";
    }

    internal static ObjectId EnsureLayer(Database db, Transaction tr, string name, short aci)
    {
        var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
        if (lt.Has(name)) return lt[name];
        lt.UpgradeOpen();
        var ltr = new LayerTableRecord { Name = name, Color = Color.FromColorIndex(ColorMethod.ByAci, aci) };
        ObjectId id = lt.Add(ltr);
        tr.AddNewlyCreatedDBObject(ltr, true);
        return id;
    }

    internal static void EraseQuiet(Database db, ObjectId id)
    {
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            (tr.GetObject(id, OpenMode.ForWrite) as AcadEntity)?.Erase();
            tr.Commit();
        }
        catch { }
    }

    /// <summary>거절 사유를 알린다 — 명령창에 늘 쓰고, 팝업은 <b>조용한 재작성 중이 아닐 때만</b>.
    ///
    /// <para>★★[v32.35 · 검토 반영] JACK: <i>"재작성될 때 팝업 좀 없애."</i>
    /// <see cref="ProfileCommand.Finish"/>만 막아서는 부족했다 — 실패 경로는 <b>여기</b>로 오는데
    /// 여기가 팝업을 띄우면 측점을 찍을 때마다 확인 버튼을 눌러야 한다(요구와 정반대).</para>
    ///
    /// <para><b>그래도 침묵하지는 않는다.</b> 명령창 줄은 언제나 남는다 —
    /// 조용히 실패하면 "버튼이 고장 났다"가 되고, 그건 팝업보다 나쁘다.</para></summary>
    internal static void Refuse(Editor ed, string msg)
    {
        ed.WriteMessage("\n[종단/횡단] " + msg.Replace("\n", " "));
        if (!ProfileCommand.QuietRebuild) AcadApp.ShowAlertDialog(msg);
    }

    private static void Done(Editor ed, string msg)
    {
        ed.WriteMessage("\n[종단/횡단] " + msg);
        try { DiagLog.Append($"\n■ DHSECTION — {msg}\n"); } catch { }
    }
}
