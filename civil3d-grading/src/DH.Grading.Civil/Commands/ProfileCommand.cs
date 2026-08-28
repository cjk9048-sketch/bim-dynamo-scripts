using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using DH.Grading.Core;
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

    /// <summary>★[v32.50] 단면검토선에 우리가 그리는 것들의 레이어 — <b>그리는 쪽과 지우는 쪽이 같은 이름을 본다.</b>
    /// <para>JACK 0819: 축척을 바꿔 다시 그렸더니 <b>옛 글씨가 안 지워져 겹쳤다</b> —
    /// <see cref="SheetCommand.EraseAll"/>이 지우는 레이어 목록에 이것들이 없었기 때문이다.
    /// 이름을 상수로 올려 두면 한쪽만 고쳐질 여지가 사라진다(이 저장소의 단골 실패).</para>
    /// <para><c>DH-검토선측점</c>은 <b>v32.44까지 글씨를 담던 레이어</b>다. 지금은 안 쓰지만
    /// <b>옛 도면에 남아 있으므로</b> 청소 목록에는 남긴다.</para></summary>
    internal const string LayerSlMajor = "DH-검토선(정측점)";
    internal const string LayerSlMinor = "DH-검토선(보조)";
    internal const string LayerSlTextOld = "DH-검토선측점";
    /// <summary>부지정지가 그려 두는 <b>데이라잇</b>(계획면이 원지반과 만나는 선) 레이어 —
    /// <c>GradingBuilder.DrawDaylight</c>가 이 이름으로 그린다. 굴곡부 판정의 출처다.</summary>
    private const string LayerDaylight = "DH-정지경계";
    private const string LayerClip = "DH-클립경계";
    /// <summary>사면·소단의 <b>최종 형상</b> 선 — <c>DHNORI</c>가 구역 전체를 다시 그려 넣는다.
    /// 가상 지표면의 굴곡선과 달리 오버사이즈가 아니고, 누적 구역이 전부 들어 있다.</summary>
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
    internal const string LayerChain = "DH-측점체인(숨김)";   // [v32.27] '지우고 새로'가 정리 대상으로 본다
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

    /// <summary>★★[v32.29 · JACK 0813] <b>이미 만든 종단도를 그 자리에 다시 그린다</b> — 도면설정이 부른다.
    ///
    /// <para>JACK: <i>"도면설정에서 원지반 표현을 바꾸고 저장해도 업데이트가 되지 않아."</i>
    /// 맞다. 정밀도를 바꾸면 <b>측점이 바뀌고</b>, 측점이 바뀌면 단면검토선·밴드·종단뷰·도곽이
    /// 전부 딸려 가므로 부분 갱신이 아니라 <b>다시 그리는 것</b>이 정답이다.</para>
    ///
    /// <para>그런데 그냥 다시 그리면 <b>노선을 또 찍어야</b> 한다 — 설정 하나 바꿀 때마다 그건 무리다.
    /// 그래서 <b>노선 좌표와 종단도 놓은 자리를 그대로 재사용</b>한다.
    /// 노선 좌표는 도면의 노란 선에서 읽고, 놓은 자리는 그 선에 <b>붙여 둔 XData</b>에서 읽는다
    /// (Civil의 <c>ProfileView</c>에는 '어디에 놓였는지'를 돌려주는 속성이 없다 — 메타데이터로 확인).</para>
    ///
    /// <para>둘 중 하나라도 없으면 <b>아무것도 하지 않는다</b>. 자리를 모르는 채 다시 그리면
    /// 종단도가 엉뚱한 데로 옮겨 가는데, 그건 '업데이트'가 아니다.</para></summary>
    /// <summary>★★[v32.35 · 검토 반영] <b>지금 조용한 재작성 중인가</b> — 팝업을 띄우는 쪽이 이것을 본다.
    /// <para>재작성은 <b>사용자가 시작한 일이 아니라 곁따라 일어나는 일</b>이라, 그 도중의 알림은
    /// 명령창으로 충분하다. 측점을 찍을 때마다 확인 버튼을 누르게 하면 '자동'이 아니다.</para>
    /// <para>⚠ <b>반드시 <c>finally</c>로 되돌린다</b> — 예외로 켜진 채 남으면 그 뒤의 진짜 오류까지
    /// 조용히 삼킨다(이 저장소가 §25에서 배운 '스타일은 도면에 남는다'의 다른 얼굴).</para></summary>
    internal static bool QuietRebuild { get; private set; }

    internal static bool Rebuild(Document doc)
    {
        Database db = doc.Database;
        Editor ed = doc.Editor;
        bool wasQuiet = QuietRebuild;
        QuietRebuild = true;
        try
        {
            if (!ReadExistingRoute(db, out var pts, out var viewPt))
            {
                ed.WriteMessage("\n[도면 설정] 다시 그릴 종단도가 없습니다 — [종단도] 버튼으로 먼저 만드세요."
                                + "\n  ※v32.29 이전에 만든 종단도라면 '어디에 놓았는지'가 기록돼 있지 않습니다."
                                + " 한 번만 [종단도]로 새로 만들면 그 뒤로는 저장할 때마다 자동으로 갱신됩니다.");
                return false;
            }
            ed.WriteMessage($"\n[도면 설정] 종단도를 그 자리에 다시 그립니다(노선 {pts.Count}점 재사용)...");
            // ★[검토 반영] <see cref="Body"/>가 <b>중간에 포기했는지</b>를 그대로 넘긴다 —
            //   종전엔 무조건 참이라 부른 쪽이 성공과 실패를 구분할 수 없었다.
            return Body(db, ed, pts, viewPt);
        }
        catch (System.Exception ex)
        {
            ed.WriteMessage("\n[도면 설정] 종단도 다시 그리기 실패 — " + ex.Message);
            try { DiagLog.Append($"\n■ 종단도 재생성 예외 — {ex}\n"); } catch { }
            return false;
        }
        finally { QuietRebuild = wasQuiet; }
    }

    /// <summary>도면에 남아 있는 노선(노란 선)과 <b>종단도를 놓았던 자리</b>를 읽는다.
    /// 둘 다 있어야 참이다 — 노선이 여럿이면 <b>마지막 것</b>(가장 나중에 그린 것)을 쓴다.</summary>
    private static bool ReadExistingRoute(Database db, out System.Collections.Generic.List<Point3d> pts,
                                          out Point3d viewPt)
    {
        pts = new System.Collections.Generic.List<Point3d>();
        viewPt = Point3d.Origin;
        bool ok = false;
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            if (!lt.Has(LayerRoute)) { tr.Commit(); return false; }
            ObjectId lid = lt[LayerRoute];
            var ms = (BlockTableRecord)tr.GetObject(
                SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
            foreach (ObjectId id in ms)
            {
                if (tr.GetObject(id, OpenMode.ForRead) is not Polyline pl || pl.LayerId != lid) continue;
                var got = new System.Collections.Generic.List<Point3d>(pl.NumberOfVertices);
                for (int i = 0; i < pl.NumberOfVertices; i++)
                {
                    var p = pl.GetPoint2dAt(i);
                    got.Add(new Point3d(p.X, p.Y, 0));
                }
                if (got.Count < 2) continue;
                // 놓았던 자리 — 없으면 이 노선은 쓸 수 없다(자리를 모르면 다시 그릴 수 없다).
                using var xd = pl.GetXDataForApplication(ViewPtAppName);
                if (xd == null) continue;
                foreach (TypedValue tv in xd)
                    if (tv.TypeCode == (short)DxfCode.ExtendedDataXCoordinate && tv.Value is Point3d vp)
                    { viewPt = vp; pts = got; ok = true; }     // 마지막 것이 이긴다
            }
            tr.Commit();
        }
        catch { return false; }
        return ok && pts.Count >= 2;
    }

    /// <summary>종단도를 놓은 자리를 노선에 적어 둔다 — 다음에 '그 자리에 다시 그리기'가 읽는다.</summary>
    private static void SaveViewPoint(Database db, ObjectId routeId, Point3d p,
                                      System.Text.StringBuilder log)
    {
        if (routeId.IsNull) return;
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            var rat = (RegAppTable)tr.GetObject(db.RegAppTableId, OpenMode.ForWrite);
            if (!rat.Has(ViewPtAppName))
            {
                var rec = new RegAppTableRecord { Name = ViewPtAppName };
                rat.Add(rec); tr.AddNewlyCreatedDBObject(rec, true);
            }
            var e = (Entity)tr.GetObject(routeId, OpenMode.ForWrite);
            e.XData = new ResultBuffer(
                new TypedValue((int)DxfCode.ExtendedDataRegAppName, ViewPtAppName),
                new TypedValue((int)DxfCode.ExtendedDataXCoordinate, p));
            tr.Commit();
        }
        catch (System.Exception ex) { log.AppendLine("  종단도 자리 기록 실패(다시그리기가 안 될 수 있다) — " + ex.Message); }
    }

    /// <summary>노선에 붙이는 XData 앱 이름 — '종단도를 어디에 놓았는지'를 담는다.</summary>
    private const string ViewPtAppName = "DHGRADE_PROFVIEWPT";

    /// <summary>저장해 둔 좌표로 노선(노란 선)을 다시 그린다 — <see cref="DrawRoute"/>가 남기는 것과 같은 물건.
    /// <para>좌표는 이미 도면 좌표계(WCS)다 — 읽을 때 폴리선 정점에서 왔으므로 변환하지 않는다.
    /// 여기서 UCS 변환을 한 번 더 걸면 UCS를 돌려 쓰는 도면에서 노선이 매번 조금씩 돌아간다.</para></summary>
    private static ObjectId MakeRoutePolyline(Database db,
        System.Collections.Generic.IReadOnlyList<Point3d> pts, out int nPts, out double len)
    {
        nPts = pts?.Count ?? 0; len = 0;
        if (pts == null || pts.Count < 2) return ObjectId.Null;
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            ObjectId layerId = SectionCommand.EnsureLayer(db, tr, LayerRoute, YellowIndex);
            var ms = (BlockTableRecord)tr.GetObject(
                SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
            var pl = new Polyline(pts.Count) { LayerId = layerId };
            for (int i = 0; i < pts.Count; i++)
                pl.AddVertexAt(i, new Point2d(pts[i].X, pts[i].Y), 0, 0, 0);
            pl.Closed = false;
            ms.AppendEntity(pl); tr.AddNewlyCreatedDBObject(pl, true);
            ObjectId id = pl.ObjectId;
            len = pl.Length;
            tr.Commit();
            return id;
        }
        catch { return ObjectId.Null; }
    }

    /// <returns>★[v32.35 · 검토 반영] <b>끝까지 갔으면 참</b>. 중간에 포기하면 거짓 —
    /// 부르는 쪽(<see cref="Rebuild"/>)이 성공과 실패를 구분해야 측점 찍기가 헛돌지 않는다.</returns>
    private static bool Body(Database db, Editor ed,
                             System.Collections.Generic.List<Point3d> presetRoute = null,
                             Point3d presetViewPt = default)
    {
        var cdoc = CivilApp.CivilApplication.ActiveDocument;
        var log = new System.Text.StringBuilder();
        bool rebuild = presetRoute is { Count: >= 2 };
        if (rebuild) log.AppendLine($"※다시 그리기 — 노선 {presetRoute.Count}점과 놓은 자리를 재사용한다");
        // ★★[v29.0 점검 반영 · 치명] 지난 판의 체인 ID가 넘어오지 않게 <b>맨 먼저 비운다</b>.
        LastLabelChainId = ObjectId.Null;

        // ── ① 대상 지표면 ────────────────────────────────────────────────────
        var surfs = SectionCommand.FindSurfaces(db, cdoc);
        if (surfs.Count == 0)
        {
            SectionCommand.Refuse(ed, "종단도를 만들 지표면이 없습니다.\n\n" +
                                      "먼저 [서버지표면]으로 원지반을 만들거나 [부지정지]를 실행하세요.");
            return false;
        }
        log.AppendLine("대상 지표면: " + string.Join(" · ", surfs.ConvertAll(s => s.Label + "=" + s.SurfName)));

        // ── ② 이전 종단도 정리 여부 ──────────────────────────────────────────
        //   [JACK 0807] 무조건 지우지 않는다 — 여러 노선을 놓고 비교하고 싶을 수 있다. 물어본다.
        int prev = CountExisting(db, cdoc);

        // ★★★[v32.35 · 측점 기능이 드러낸 오래된 구멍] <b>수동 측점은 선형과 함께 죽는다.</b>
        //
        //   측점 목록은 <b>선형의 확장사전</b>에 저장된다(<see cref="StationMarks.Save"/>).
        //   그런데 <see cref="EraseExisting"/>은 <b>선형을 지운다</b> — 확장사전도 같이 사라진다.
        //   즉 '지우고 새로'를 고르거나 다시 그릴 때마다 <b>밸브실 측점이 조용히 날아갔다.</b>
        //   종전에는 다시 그릴 일이 드물어 눈에 안 띄었는데, 측점을 찍을 때마다 다시 그리게 되면
        //   <b>방금 찍은 측점이 그 자리에서 사라진다</b> — 기능 자체가 성립하지 않는다.
        //
        //   → <b>지우기 전에 건져 두고, 새 선형에 다시 심는다.</b>
        //   ※ '남겨두고추가'일 때는 건지지 않는다 — 옛 선형이 그대로 살아 있으므로
        //     새 선형에 같은 측점을 또 넣으면 <b>두 벌</b>이 된다.
        var carry = new System.Collections.Generic.List<StationMarks.Mark>();

        // ★★[검토 반영 · 치명] <b>건진 뒤 중간에 포기하면 그 사본이 유일본이라 통째로 사라진다.</b>
        //   원본(선형의 확장사전)은 이미 지워졌고 새 선형은 아직 없다 —
        //   이삿짐을 싸고 옛집을 헌 뒤 새집 계약이 깨진 꼴이다.
        //   되살릴 곳이 없으므로 <b>최소한 말은 한다</b>. 조용히 사라지는 것이 가장 나쁘다.
        //   (근본 해결은 지우기를 노선 확보 뒤로 미루는 것인데, 그러면 방금 그린 노선까지
        //    같은 레이어라 함께 지워진다 — 그 정리는 별도 판으로 미룬다.)
        void LoseCarry()
        {
            if (carry.Count == 0) return;
            log.AppendLine($"  ⚠수동 측점 {carry.Count}개가 갈 곳을 잃었다 — 선형을 만들지 못하고 중단했다");
            ed.WriteMessage($"\n  · ⚠수동 측점 {carry.Count}개가 사라졌습니다(선형을 만들지 못했습니다) — 다시 찍어 주세요.");
            try { DiagLog.Append($"\n■ 종단도 중단 — 수동 측점 {carry.Count}개 유실\n"); } catch { }
        }

        // ★[v32.29] 다시 그리기는 <b>묻지 않는다</b> — 같은 자리에 새로 그리는 것이 목적이므로
        //   옛것을 남기면 두 벌이 겹친다. 사용자가 '다시 그린다'는 것을 이미 고른 상태다.
        if (rebuild)
        {
            log.AppendLine("이전 종단도 정리(다시 그리기 — 묻지 않음):");
            carry = HarvestMarks(db, cdoc, log);
            int wiped = EraseExisting(db, cdoc, log);
            ed.WriteMessage($"\n  · 이전 종단도를 지웠습니다(객체 {wiped}개).");
        }
        else if (prev > 0)
        {
            var kw = new PromptKeywordOptions($"\n이미 만든 종단도가 {prev}개 있습니다. 지우고 새로 만들까요? ");
            kw.Keywords.Add("지우고새로", "Y", "지우고새로(Y)");
            kw.Keywords.Add("남겨두고추가", "N", "남겨두고추가(N)");
            kw.Keywords.Default = "지우고새로";
            kw.AllowNone = true;
            var kr = ed.GetKeywords(kw);
            if (kr.Status != PromptStatus.OK && kr.Status != PromptStatus.None) return false;
            if (kr.Status == PromptStatus.None || kr.StringResult == "지우고새로")
            {
                log.AppendLine("이전 종단도 정리:");
                carry = HarvestMarks(db, cdoc, log);      // ★[v32.35] 선형과 함께 죽기 전에 건진다
                int erased = EraseExisting(db, cdoc, log);
                ed.WriteMessage($"\n  · 이전 종단도를 지웠습니다(객체 {erased}개 — 노선·도곽범위·표고바·제목부·배치 포함).");
            }
            else log.AppendLine($"이전 종단도 {prev}개 유지(추가 생성)");
        }

        // ── ③ 노선 — 새로 그리거나(기본), 저장해 둔 좌표로 되살리거나(다시 그리기)
        ObjectId routeId; int nPts; double routeLen;
        if (rebuild)
        {
            routeId = MakeRoutePolyline(db, presetRoute, out nPts, out routeLen);
            if (routeId.IsNull)
            { ed.WriteMessage("\n[종단도] 노선을 되살리지 못했습니다."); LoseCarry(); return false; }
        }
        else
        {
            routeId = DrawRoute(db, ed, out nPts, out routeLen);
            if (routeId.IsNull) { LoseCarry(); return false; }        // 취소
        }
        if (routeLen < 1.0)
        {
            SectionCommand.EraseQuiet(db, routeId);
            SectionCommand.Refuse(ed, $"노선이 너무 짧습니다({routeLen:F2}m). 1m 이상으로 그려 주세요.");
            LoseCarry();
            return false;
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
            LoseCarry();
            return false;
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
            LoseCarry();
            return false;
        }
        log.AppendLine($"선형 '{alignName}' 생성");

        // ★★[v32.35] 건져 둔 수동 측점을 새 선형에 다시 심는다 — 위 ②의 설명 참조.
        //   <b>선형이 만들어진 직후</b>여야 한다: 아래 <see cref="BuildSampleLines"/>가 이 목록을 읽어
        //   단면검토선을 놓으므로, 그보다 늦으면 <b>이번 판에는 반영되지 않는다.</b>
        if (carry.Count > 0)
        {
            bool ok = false;
            try
            {
                using var trM = db.TransactionManager.StartTransaction();
                ok = StationMarks.Save(trM, alignId, carry);
                if (ok) trM.Commit(); else trM.Abort();
            }
            catch (System.Exception ex) { log.AppendLine("  수동 측점 이월 실패 — " + ex.Message); }
            log.AppendLine(ok
                ? $"  수동 측점 {carry.Count}개를 새 선형으로 이월했다"
                : $"  ⚠수동 측점 {carry.Count}개를 이월하지 못했다 — 이번 판에서 사라진다");
        }

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
        // ★★[v31.3] 축척 배너 같은 <b>블록</b>은 스타일 들여오기로 안 온다 — 따로 복제해 온다.
        string blk = ProfileStyleTemplate.ImportBlocks(db, n => n.Contains("배너") || n.Contains("축척"));
        log.AppendLine(blk);
        // ★[JACK 0807] 로그파일에만 남기면 확인이 안 된다("명령창에 네가 이야기한 것들은 뜨지 않았어") — 명령창에도 찍는다.
        ed.WriteMessage("\n  · " + ProfileStyleTemplate.LastReport);
        if (ProfileStyleTemplate.LastProbe.Length > 0)
        {
            log.AppendLine(ProfileStyleTemplate.LastProbe);
            ed.WriteMessage("\n  · (계측 상세는 로그 파일에 기록됨)");
        }

        ObjectId profStyle = SectionCommand.PickStyle(db, cdoc.Styles.ProfileStyles, "기본", "Standard", "Basic");
        ObjectId excStyle = SectionCommand.EnsureExcavProfileStyle(db, cdoc);   // ★[0824] 터파기 = 마젠타
        // ★★★[JACK 0828] 지층·지하수위 종단선 — <b>점선을 갈라 쓴다</b>.
        //   JACK: <i>"모든 지층은 점선으로. 지하수위는 파란색 점선.
        //   점선이 터파기 지표면 점선하고 헷갈리지 않게 형태를 다른 걸로."</i>
        //   터파기 <c>DASHED</c>(긴 파선) · 지층 <c>HIDDEN</c>(짧은 점선) · 지하수위 <c>DASHDOT</c>(일점쇄선).
        // ★★[JACK 0828 "점선 표시가 축척 때문인지 너무 끊어진 부분이 커서 이상해"]
        //   AutoCAD 기본 선종류는 <b>무늬 길이가 도면 단위로 고정</b>이라,
        //   부지가 수백 m이면 끊긴 간격이 눈에 밟힌다.
        //   → <b>절반 간격 변형</b>(<c>…2</c>)을 먼저 쓴다 — 같은 모양에 무늬만 춌춌하다.
        //   없는 도면이면 원래 것으로 물러난다.
        string ltStrata = SectionCommand.LoadLinetype(db, "HIDDEN2")
                       ?? SectionCommand.LoadLinetype(db, "HIDDEN")
                       ?? SectionCommand.LoadLinetype(db, "DASHED");
        string ltWater = SectionCommand.LoadLinetype(db, "DASHDOT2")
                      ?? SectionCommand.LoadLinetype(db, "DIVIDE2")
                      ?? SectionCommand.LoadLinetype(db, "DASHDOT")
                      ?? ltStrata;
        ObjectId stStrata = SectionCommand.EnsureProfileStyle(db, cdoc, SectionCommand.StrataStyleName, 8, ltStrata);
        ObjectId stWater = SectionCommand.EnsureProfileStyle(db, cdoc, SectionCommand.WaterStyleName, 5, ltWater);
        // ★★★[JACK 0828 "종단에서 지층색이 반영이 안 됐어 다 초록색으로 나와"]
        //   <b>추측하지 않는다 — 무엇을 쓰는지 적어 둔다.</b>
        //   색이 안 먹는 길은 둘이다: ① 스타일을 못 만들어 <b>기본 스타일로 물러났거나</b>
        //   ② 레이어를 못 만들어 <b>선형 레이어(초록)에 그려졌거나</b>.
        //   둘은 고치는 자리가 서로 다르므로 <b>로그가 먼저 갈라줘야</b> 한다.
        log.AppendLine($"  지층 선 준비 — 지층 스타일 {(stStrata.IsNull ? "⚠<b>못 만들었다(기본으로 물러난다)</b>" : "OK")}"
                     + $" · 지하수위 스타일 {(stWater.IsNull ? "⚠<b>못 만들었다</b>" : "OK")}"
                     + $" · 선종류 지층={ltStrata ?? "⚠없음"} · 지하수위={ltWater ?? "⚠없음"}");
        ObjectId profLabels = SectionCommand.PickStyle(db, cdoc.Styles.LabelSetStyles.ProfileLabelSetStyles,
                                                      "_없음", "None", "표준", "Standard");
        int nProf = 0;
        // ★[JACK 0807] 밴드에 **원지반/계획지반을 자동 지정**하려면 만든 종단의 ObjectId를 들고 있어야 한다.
        ObjectId pidGround = ObjectId.Null, pidPad = ObjectId.Null, pidExcav = ObjectId.Null;
        // ★[JACK 0828] 이름을 적으려면 만든 지층 종단을 들고 있어야 한다.
        var strataProfs = new System.Collections.Generic.List<(ObjectId Pid, string Nm, bool Water)>();
        foreach (var s in surfs)
        {
            try
            {
                // ★[JACK 0824] 터파기 종단선만 **마젠타** 스타일로.
                var styleFor = s.Label == "터파기" && !excStyle.IsNull ? excStyle
                             : s.Label == "지층" && !stStrata.IsNull ? stStrata
                             : s.Label == "지하수위" && !stWater.IsNull ? stWater
                             : profStyle;
                // ★★[JACK 0826 '여전히 원지반과 같은 레이어라서 같은 스타일이 먹여짐'] <b>만들 때 레이어를 가른다.</b>
                //   만든 뒤 <c>Entity.LayerId</c>로 옮기는 것은 안 먹었다 — Civil 객체는 생성 시 받은 레이어를 쥔다.
                //   종단은 선형 레이어(CR-GRND=원지반, 초록)에 만들어지므로 터파기도 초록이 됐다.
                var layerFor = alignLayer;
                if (s.Label == "터파기")
                {
                    var lx = SectionCommand.EnsureLayerStandalone(db, SectionCommand.ExcavProfileLayer, SectionCommand.ExcavAci);
                    if (!lx.IsNull) layerFor = lx;
                }
                // ★[JACK 0826 교훈] 색을 스타일에만 두면 <b>ByLayer가 이긴다</b> — 레이어도 갈라 둔다.
                else if (s.Label == "지층")
                {
                    var lx = SectionCommand.EnsureLayerStandalone(db, SectionCommand.StrataProfLayer, SectionCommand.StrataAci);
                    if (!lx.IsNull) layerFor = lx;
                }
                else if (s.Label == "지하수위")
                {
                    var lx = SectionCommand.EnsureLayerStandalone(db, SectionCommand.WaterProfLayer, SectionCommand.WaterAci);
                    if (!lx.IsNull) layerFor = lx;
                }
                var pid = CivilDb.Profile.CreateFromSurface(s.ProfileName, alignId, s.SurfId, layerFor, styleFor, profLabels);
                // ★[JACK 0828] 지층·지하수위는 <b>어느 레이어에 놓였는지</b> 되읽어 남긴다 —
                //   §0826의 교훈: <c>@0</c>은 "그려진 레이어를 따른다"라 <b>레이어가 색을 준다</b>.
                if (s.Label == "지층" || s.Label == "지하수위")
                {
                    // ★★[JACK 0828] 만들 때 레이어를 줘도 안 먹는 경우가 있다 —
                    //   터파기가 겉던 길(<c>PaintExcavProfile</c>)을 그대로 태워 다시 옮긴다.
                    SectionCommand.PaintStrataProfile(db, pid, s.Label == "지하수위");
                    strataProfs.Add((pid, StrataDraw.ShortName(s.SurfName), s.Label == "지하수위"));
                    string lnm = "?", snm = "?";
                    try
                    {
                        using var trL = db.TransactionManager.StartTransaction();
                        if (trL.GetObject(pid, OpenMode.ForRead) is Autodesk.AutoCAD.DatabaseServices.Entity pe2)
                        {
                            lnm = pe2.Layer;
                            try { if (trL.GetObject(((CivilDb.Profile)pe2).StyleId, OpenMode.ForRead) is CivilDb.Styles.StyleBase sb2) snm = sb2.Name; }
                            catch { }
                        }
                        trL.Commit();
                    }
                    catch { }
                    log.AppendLine($"    {s.ProfileName} → 레이어 '{lnm}' · 스타일 '{snm}'");
                }
                // ★[JACK 0824] 라벨로 **정확히** 가른다 — 종전엔 `else pidPad`라
                //   터파기 종단이 생기는 순간 계획면 자리를 덮어써 밴드 값이 통째로 밀렸다.
                if (s.Label == "원지반") pidGround = pid;
                else if (s.Label == "정지면") pidPad = pid;
                else if (s.Label == "터파기")
                {
                    pidExcav = pid;                              // ★[JACK 0825] 가시설 막대의 아래끝
                    SectionCommand.PaintExcavProfile(db, pid);   // ★[JACK 0825] 객체 색을 직접 마젠타로
                }
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
            return false;   // carry는 이미 새 선형에 심었다 — 잃은 것이 없다
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
        LastPidGround = pidGround; LastPidPad = pidPad;   // ★[검토 0827] 뒤에서 이름으로 찾지 않게
        ObjectId slGroupId = BuildSampleLines(db, ed, alignId, pidGround, pidPad, surfs, bandIv,
                                              out var allMarks, log);

        // ── ⑤-c ★★[v32.24 · JACK 0812 스샷] <b>측점을 정한 뒤에 원지반선을 긋는다 — 순서가 중요하다.</b>
        //   JACK: <i>"원지형과 계획지표면이 만나는 부분이야 … 저부분은 딱맞아야해."</i>
        //   v32.23은 꺾은선을 <b>먼저</b> 만들고 측점을 나중에 잡아서, 데이라잇 자리에 정점이 없었다 —
        //   그 자리를 직선이 가로질러 계획선이 원지반선을 뚫고 내려갔다(스샷).
        //   이제 <b>확정된 측점 전부</b>를 정점으로 삼는다: 데이라잇·절성경계 자리에 실측 표고가 박히므로
        //   두 선이 정확히 만난다.
        pidGround = RebuildGroundAsPolyline(db, alignId, pidGround, alignLayer, profStyle, profLabels,
                                            allMarks, log);

        // ── ⑥ 종단도 배치 ───────────────────────────────────────────────────
        //   ★[v32.29] 다시 그리기면 <b>놓았던 자리</b>를 그대로 쓴다(묻지 않는다).
        Point3d placeAt;
        if (rebuild) placeAt = presetViewPt;
        else
        {
            var pvPt = ed.GetPoint("\n[종단도] 종단면도를 놓을 위치 클릭 (Esc=종단만 만들고 끝): ");
            if (pvPt.Status != PromptStatus.OK)
            {
                log.AppendLine("종단도 배치 건너뜀(사용자 취소)");
                Finish(ed, log, $"선형 '{alignName}' · 종단 {nProf}개 생성(종단도 배치는 건너뜀)", quiet: rebuild);
                return true;    // 종단·측점은 만들어졌다 — 도곽만 건너뛴 것이라 성공이다
            }
            placeAt = pvPt.Value.TransformBy(ed.CurrentUserCoordinateSystem);
        }
        // 다음 '다시 그리기'가 이 자리를 찾을 수 있게 노선에 적어 둔다.
        SaveViewPoint(db, routeId, placeAt, log);
        try
        {
            var pvId = CivilDb.ProfileView.Create(alignId, placeAt);
            log.AppendLine("종단면도 배치 완료");
            string sty = ApplyViewStyle(db, cdoc, pvId, pidGround, pidPad, slGroupId, surfs, ed, log);
            log.AppendLine(sty);
            ed.WriteMessage("\n  · " + sty);   // ★[JACK 0807] 명령창에서 바로 확인되게

            // ★[JACK 0810] "도곽 버튼이 왜 필요하지? 그냥 종단도 누르면 모형탭하고 배치까지 자동으로 되야 되."
            //   버튼을 늘리지 않고 여기서 끝까지 간다 — 모형 도곽 범위 + 배치 한 장까지.
            string sheet = SheetCommand.Build(db, ed, pvId, log);

            // ★[v32.45] 축척이 정해진 <b>뒤에</b> 검토선을 꾸민다 — 글씨가 종단 밴드와 같은 크기가 되려면
            //   도면 축척을 알아야 한다(설명은 DecorateSampleLines).
            DecorateSampleLines(db, cdoc, alignId, bandIv, GradingSettings.XsecLeft, GradingSettings.XsecRight, log);
            log.AppendLine("도곽: " + sheet);
            ed.WriteMessage("\n  · 도곽: " + sheet);

            // ★[JACK 0825] 옹벽·가시설을 굵은 수직 막대로 — 도면 관행대로 직각 한 줄로 보이게.

            // ★[JACK 0825] 스타일을 고쳐도 <b>화면이 옛 그림을 들고 있으면</b> 색이 안 바뀐 것처럼 보인다.
            //   되읽기는 DB 값이라 항상 맞게 나오므로 "로그는 맞는데 화면은 틀림"과 모순되지 않는다.
            // ★★[JACK 0825 '터파기 선이 여전히 초록'] <b>종단뷰마다 스타일 재정의가 따로 있다.</b>
            //
            //   Civil 문서: <i>"Override Style — 이 종단뷰에서만 쓰이는 재정의 스타일.
            //   다른 종단뷰는 Style 열의 값을 쓴다."</i>
            //   그래서 <b>객체의 스타일은 마젠타인데 화면은 초록</b>일 수 있다 —
            //   특성창엔 진짜 스타일이 찍히지만 그리는 것은 재정의 쪽이기 때문이다.
            //   (객체 색을 직접 박아도 소용없다. Civil 객체는 자기 Entity.Color를 무시하고
            //    스타일의 DisplayStyle이 화면을 전담한다.)
            //
            //   → 재정의가 걸려 있든 없든 <b>우리 스타일로 덮는다.</b> 걸려 있지 않으면 목록이 비어 no-op다.
            try
            {
                using var trOv = db.TransactionManager.StartTransaction();
                if (trOv.GetObject(pvId, OpenMode.ForWrite) is CivilDb.ProfileView pvOv)
                {
                    var sbOv = new System.Text.StringBuilder();
                    int nOv = 0, nFix = 0;
                    foreach (CivilDb.ProfileOverride ov in pvOv.GraphOverrides)
                    {
                        nOv++;
                        string pn = "?";
                        try { pn = ov.ProfileName ?? "(빈값)"; } catch { }
                        sbOv.Append(' ').Append(pn);
                        // 터파기 종단이면 마젠타 스타일로 못 박는다.
                        if (pn.IndexOf("터파기", System.StringComparison.Ordinal) >= 0 && !excStyle.IsNull)
                        {
                            try { ov.OverrideStyleId = excStyle; nFix++; sbOv.Append("→마젠타"); }
                            catch (System.Exception exO) { sbOv.Append("→실패(").Append(exO.GetType().Name).Append(')'); }
                        }
                    }
                    log.AppendLine($"  종단뷰 스타일 재정의 {nOv}건{(nOv > 0 ? " —" + sbOv : " (없음 — 재정의는 범인이 아니다)")}" +
                                   (nFix > 0 ? $" · 터파기 {nFix}건을 마젠타로 덮었다" : ""));
                }
                trOv.Commit();
            }
            catch (System.Exception exOv) { log.AppendLine("  종단뷰 재정의 확인 실패 — " + exOv.Message); }

            // ★★[JACK 0826 "자꾸 똑같은 오류 보이지 말고 로그를 붙이든 해서 시행착오를 줄여줘"] 맞는 말이다.
            //   ★자리가 중요하다★ — 여기는 <b>재정의까지 끝난 뒤</b>다. 처음엔 도곽(SheetCommand) 직후에
            //   뒀는데, 검토에서 <b>20줄 이르다</b>고 잡혔다. 그 뒤에 GraphOverrides가 한 번 더 돌아
            //   화면을 최종적으로 정하기 때문이다 — 재기 전에 값이 또 바뀌면 잰 값은 화면이 아니다.
            //
            //   ★[civil-object-display-layers] 색을 정하는 층이 <b>셋</b>이고, 셋 다 봐야 한다:
            //     ① 스타일의 선 색 — 명시된 색이면 <b>레이어를 이긴다</b>
            //     ② 그 스타일의 표시 레이어 — ①이 ByLayer일 때<b>만</b> 참조된다("0"=객체가 놓인 레이어)
            //     ③ 뷰별 재정의 — 걸려 있으면 <b>이것이 그린다</b>(객체 스타일은 특성창에만 남는다)
            //   ①만 보던 옛 계측은 "레이어만 맞으면 합격"이라 <b>스타일이 초록이어도 도장을 찍었다.</b>
            {
                string fLay = "(터파기 종단 없음)", fSty = "-", fDsLay = "-", fVerd, fNote = "";
                bool fRead = false, fMag = false, fByL = false, fOk = false;
                string fColTxt = "-", fOv = "";
                if (!pidExcav.IsNull)
                {
                    try
                    {
                        using var trF = db.TransactionManager.StartTransaction();
                        if (trF.GetObject(pidExcav, OpenMode.ForRead) is CivilDb.Profile pf)
                        {
                            fLay = "(레이어 못 읽음)"; fSty = "(스타일 못 읽음)";
                            try { fLay = ((LayerTableRecord)trF.GetObject(pf.LayerId, OpenMode.ForRead)).Name; } catch { }
                            try { fSty = pf.StyleName ?? "(빈값)"; } catch { }

                            // ③ 재정의가 걸려 있으면 <b>그것이 화면을 그린다</b> — 판정 대상을 그쪽으로 옮긴다.
                            //   ★[검토 N-3] 이름 <b>조각</b>이 아니라 <b>정확히 이 종단</b>이어야 한다.
                            //   이름이 겹치면 Civil이 '-1','-2'를 붙이는데, 조각으로 찾으면 옛 실행이 남긴
                            //   'DH_터파기'의 재정의를 골라 <b>엉뚱한 종단으로 판정</b>하게 된다.
                            string pfName = null;
                            try { pfName = pf.Name; } catch { }
                            ObjectId judgeStyle = pf.StyleId;
                            try
                            {
                                if (pfName != null && trF.GetObject(pvId, OpenMode.ForRead) is CivilDb.ProfileView pvJ)
                                    foreach (CivilDb.ProfileOverride ovJ in pvJ.GraphOverrides)
                                    {
                                        string on = null;
                                        try { on = ovJ.ProfileName; } catch { }
                                        if (on == pfName && !ovJ.OverrideStyleId.IsNull)
                                        { judgeStyle = ovJ.OverrideStyleId; fOv = " · 재정의가 그린다"; break; }
                                    }
                            }
                            catch { fOv = " · 재정의 확인 실패"; }

                            // ①② ★[검토 N-1] <b>판정은 Line 하나로.</b> 이 종단은 지표면을 따라 딴 꺾은선이라
                            //   곡선·포물선은 화면에 <b>한 픽셀도 안 그린다</b>. 안 쓰는 칸이 어긋났다고 불합격을
                            //   주면 화면은 멀쩡한데 로그만 초록이라 우는 <b>거짓 경보</b>가 된다(방향만 반대인 헛짚기).
                            //   나머지 다섯은 <b>참고 문구</b>로만 붙인다 — 곡선 어긋남도 눈에는 보이게.
                            try
                            {
                                if (trF.GetObject(judgeStyle, OpenMode.ForRead) is CivilDb.Styles.ProfileStyle fSt)
                                {
                                    int nOther = 0, nDiff = 0;
                                    foreach (var ty in new[]
                                    {
                                        CivilDb.Styles.ProfileDisplayStyleProfileType.Line,
                                        CivilDb.Styles.ProfileDisplayStyleProfileType.Curve,
                                        CivilDb.Styles.ProfileDisplayStyleProfileType.LineExtension,
                                        CivilDb.Styles.ProfileDisplayStyleProfileType.SymmetricalParabola,
                                        CivilDb.Styles.ProfileDisplayStyleProfileType.AsymmetricalParabola,
                                        CivilDb.Styles.ProfileDisplayStyleProfileType.ParabolicCurveExtension,
                                    })
                                    {
                                        // ★[검토 N-2] <b>타입마다</b> 감싼다 — 포물선 하나가 던지면 루프가 끊겨
                                        //   "색을 못 읽었다"가 되던 자리다. 스타일을 칠하는 쪽도 이미 타입마다 감싼다.
                                        try
                                        {
                                            using var fDs = fSt.GetDisplayStyleProfile(ty);
                                            if (fDs == null) continue;
                                            bool isMag = fDs.Color.ColorMethod == Autodesk.AutoCAD.Colors.ColorMethod.ByAci
                                                         && fDs.Color.ColorIndex == SectionCommand.ExcavAci;
                                            bool isByL = fDs.Color.ColorMethod == Autodesk.AutoCAD.Colors.ColorMethod.ByLayer;
                                            if (ty == CivilDb.Styles.ProfileDisplayStyleProfileType.Line)
                                            {
                                                fRead = true; fMag = isMag; fByL = isByL;
                                                fColTxt = isByL ? "ByLayer" : isMag ? "ACI" + SectionCommand.ExcavAci
                                                        : fDs.Color.ColorMethod.ToString() + fDs.Color.ColorIndex;
                                                try { fDsLay = fDs.Layer ?? "(빈값)"; } catch { }
                                            }
                                            else { nOther++; if (!isMag && !isByL) nDiff++; }
                                        }
                                        catch { }
                                    }
                                    if (nDiff > 0) fNote = $" · 참고: 나머지 {nOther}종 중 {nDiff}종이 다른 색(화면엔 안 그려진다)";
                                }
                            }
                            catch { }
                        }
                        else fLay = "(종단으로 못 열림)";
                        trF.Commit();
                    }
                    catch (System.Exception exF) { fLay = "(확인 실패: " + exF.Message + ")"; }
                }

                // ★판정 — 바깥은 OR(둘 중 하나만 마젠타면 마젠타), 안쪽은 AND(레이어에 맡겼을 때의 단서).
                //   왼쪽 가지에 "ByLayer일 때만"과 "표시 레이어가 0일 때만"을 붙이지 않으면
                //   <b>스타일이 초록으로 명시돼 있어도 합격</b>이 나온다(옛 계측의 거짓 도장).
                if (!fRead) fVerd = pidExcav.IsNull ? "터파기 종단이 없다(정지만 돌린 경우)" : "⚠색을 못 읽었다 — 합격도 불합격도 아니다";
                else
                {
                    fOk = fMag || (fByL && fLay == SectionCommand.ExcavProfileLayer && fDsLay == "0");
                    // ★[검토 N-5] 떨어진 <b>진짜 이유</b>를 짚는다 — 종전 문구는 표시 레이어가 딴 데를
                    //   가리켜 떨어졌을 때도 "레이어가 터파기가 아니다"라고 말해, 같은 줄 앞머리와 모순됐다.
                    fVerd = fOk
                        ? (fMag ? "→ 마젠타로 나온다(스타일이 직접 마젠타)" : "→ 마젠타로 나온다(레이어를 따라간다)")
                        : "⚠초록으로 나온다 — " + (!fByL ? "스타일 선 색이 마젠타도 ByLayer도 아니다"
                            : fLay != SectionCommand.ExcavProfileLayer ? $"레이어가 '{SectionCommand.ExcavProfileLayer}'가 아니다"
                            : $"스타일의 표시 레이어가 '0'이 아니라 '{fDsLay}'다(딴 레이어 색을 물어온다)");
                }
                log.AppendLine($"  ★터파기 종단 최종 — 레이어 '{fLay}' · 스타일 '{fSty}'"
                             + $" · 선 색 {fColTxt}(표시레이어 '{fDsLay}'){fOv}{fNote}  {fVerd}");
            }

            // ★★★[JACK 0828 검토] <b>화면을 정하는 값을 마지막에 잰다.</b>
            //   오늘 세 번째 같은 함정이다: 종단을 만들 때 재고 "됐다"고 적었는데
            //   <c>SheetCommand.PolishView</c>가 <b>나중에</b> 레이어와 스타일 색을 통째로 덮었다.
            //   그래서 되읽기는 <b>도곽까지 다 끝난 여기</b>에서, 화면이 실제로 쓰는 두 값
            //   (객체 레이어 · 스타일 선 색)을 재고 <b>합격/불합격을 말로</b> 남긴다.
            if (strataProfs.Count > 0)
            {
                int okN = 0, badN = 0; string firstBad = null;
                try
                {
                    using var trS = db.TransactionManager.StartTransaction();
                    foreach (var it in strataProfs)
                    {
                        string lay = "?", col = "?"; bool good = false;
                        try
                        {
                            if (trS.GetObject(it.Pid, OpenMode.ForRead) is CivilDb.Profile pS)
                            {
                                lay = ((Entity)pS).Layer;
                                string want = it.Water ? SectionCommand.WaterProfLayer : SectionCommand.StrataProfLayer;
                                short wantAci = it.Water ? SectionCommand.WaterAci : SectionCommand.StrataAci;
                                if (trS.GetObject(pS.StyleId, OpenMode.ForRead) is CivilDb.Styles.ProfileStyle sS)
                                {
                                    using var dsS = sS.GetDisplayStyleProfile(CivilDb.Styles.ProfileDisplayStyleProfileType.Line);
                                    bool byL = dsS.Color.ColorMethod == Autodesk.AutoCAD.Colors.ColorMethod.ByLayer;
                                    bool mine = dsS.Color.ColorMethod == Autodesk.AutoCAD.Colors.ColorMethod.ByAci
                                                && dsS.Color.ColorIndex == wantAci;
                                    col = byL ? "ByLayer" : mine ? "ACI" + wantAci
                                        : dsS.Color.ColorMethod.ToString() + dsS.Color.ColorIndex;
                                    // 레이어가 제자리면 ByLayer여도 그 레이어 색이 나온다 — 둘 중 하나면 합격.
                                    good = (lay == want && byL) || mine;
                                }
                            }
                        }
                        catch { }
                        if (good) okN++;
                        else { badN++; firstBad ??= $"{it.Nm}: 레이어 '{lay}' · 선 색 {col}"; }
                    }
                    trS.Commit();
                }
                catch { }
                log.AppendLine($"  ★지층 종단 최종 — 제 색으로 나오는 것 {okN}개"
                             + (badN > 0 ? $" · ⚠<b>초록으로 나올 것 {badN}개</b> (첫째 {firstBad})"
                                         : " · 전부 합격(회색 점선·지하수위 파랑)"));
            }

            try { ed.Regen(); } catch { }
            DrawProfStrataNames(db, pvId, strataProfs, log);   // ★[JACK 0828] 지층·지하수위 이름
            string bars = DrawVertBars(db, pvId, alignId, pidGround, pidPad, pidExcav, LastWallSpans, log);
            log.AppendLine(bars);
            ed.WriteMessage("\n  · " + bars);
        }
        catch (System.Exception ex)
        {
            log.AppendLine("⚠종단면도 실패 — " + ex.Message);
            AcadApp.ShowAlertDialog("종단면도를 만들지 못했습니다.\n" + ex.Message +
                                    "\n\n선형과 종단은 만들어졌으니 Civil3D 기본 기능으로도 배치할 수 있습니다.");
        }

        // ★★[v32.35 · JACK 0813] <b>다시 그리기일 때는 완료 팝업을 띄우지 않는다.</b>
        //   JACK: <i>"재작성될 때 팝업 좀 없애. 팝업이 있으니깐 자동으로 업데이트되는 것처럼 느껴지지가 않아."</i>
        //   맞는 지적이다 — <b>사용자가 시작한 일이 아니라 곁따라 일어나는 일</b>이라 알림이 필요 없다.
        //   측점을 찍을 때마다 확인 버튼을 눌러야 하면 '자동'이 아니다.
        //   처음 만들 때([종단도] 버튼)는 그대로 알린다 — 그건 사용자가 <b>기다리고 있는</b> 결과다.
        // ★★[JACK 0827] 종단도를 다시 그리면 <b>횡단은 지우기만 한다</b>(다시 그리지 않는다).
        //   JACK: <i>"업데이트되는 게 아니라 그냥 전에 있던 횡단 내용은 다 사라지는 걸로."</i>
        //   종단만 손보고 싶을 때가 있고, 그때마다 횡단이 다시 그려지면 느리고 성가시다.
        //   지우는 일은 <c>SheetCommand.EraseAll</c>이 이미 한다(DH-횡단-* 레이어 포함).
        Finish(ed, log, $"노선 {routeLen:F0}m · 선형 '{alignName}' · 종단 {nProf}개 · 종단도 배치 완료", quiet: rebuild);
        return true;
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
    private static ObjectId BuildLabelChain(Database db, ObjectId alignId, ObjectId padId, ObjectId groundId,
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

            // ★★[v32.21 · JACK 0812] <b>"부체인은 원지반쪽은 안 나오는 문제"의 원인이 여기였다.</b>
            //
            //   종전엔 표고를 <c>pad.ElevationAt(s)</c> <b>하나</b>로만 구했다. 그런데 §27(v32.2)부터
            //   그 <c>pad</c>는 <b>순수 정지면</b>이라 <b>정지 구간에만</b> 존재한다 —
            //   정지 밖(=원지반만 있는 구간)을 물으면 실패하고, 그 <c>catch</c>가 조용히 삼켜
            //   <b>PVI가 아예 안 생겼다.</b> PVI가 없으면 측점 행에 <b>번호가 안 찍힌다.</b>
            //
            //   <b>값은 쓰지도 않는데 값 때문에 자리가 사라진 것이다</b>(이 종단은 라벨 자리 전용이다).
            //   → 계획면에서 못 구하면 <b>원지반 표고</b>로 놓는다. 0을 넣으면 안 된다 —
            //     안 보이는 종단이라도 종단 뷰의 <b>표고 범위 계산에는 들어갈 수 있어</b>
            //     Y축이 0까지 늘어나면 도면이 통째로 납작해진다. 원지반이면 늘 그 그림 안이다.
            static bool ElevAt(CivilDb.Profile pr, double s, out double z)
            {
                z = 0;
                if (pr == null) return false;
                try
                {
                    // 묻기 <b>전에</b> 범위를 본다 — 밖에서 <c>ElevationAt</c>이 어떻게 구는지는 문서에 없다(§27과 같은 처방).
                    if (s < pr.StartingStation - 1e-6 || s > pr.EndingStation + 1e-6) return false;
                    z = pr.ElevationAt(s);
                    if (double.IsNaN(z) || double.IsInfinity(z)) return false;
                    // ★[검토 지적 · 높음] 측점 범위 검사는 <b>종단 중간이 빈 구간</b>을 못 막는다
                    //   (누적 구역이 떨어져 있으면 정상적으로 생긴다 — §27이 바로 그 상황이다).
                    //   거기서 <c>0.0</c>이 나오면 그대로 PVI가 되어 종단 뷰 Y축이 0까지 늘어나
                    //   <b>도면이 통째로 납작해진다</b>. 있을 수 있는 표고의 테두리로 되재면
                    //   무엇을 돌려주든 걸린다 — 막아서 잃는 것이 없고, 안 막으면 도면이 깨진다.
                    try { if (z < pr.ElevationMin - 1e-6 || z > pr.ElevationMax + 1e-6) return false; } catch { }
                    return true;
                }
                catch { return false; }
            }

            int made = 0, bad = 0, fell = 0;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var chain = (CivilDb.Profile)tr.GetObject(chainId, OpenMode.ForWrite);
                var pad = (CivilDb.Profile)tr.GetObject(padId, OpenMode.ForRead);
                CivilDb.Profile grd = null;
                if (!groundId.IsNull)
                    try { grd = tr.GetObject(groundId, OpenMode.ForRead) as CivilDb.Profile; } catch { }

                foreach (double s in pts)
                {
                    if (!ElevAt(pad, s, out double z))
                    {
                        if (grd == null || !ElevAt(grd, s, out z)) { bad++; continue; }
                        fell++;                                  // 정지 밖 — 원지반 표고로 놓았다
                    }
                    try { chain.PVIs.AddPVI(s, z); made++; }
                    catch { bad++; }
                }
                tr.Commit();
            }
            log.AppendLine($"측점 라벨용 체인 '{nm}' — PVI {made}개(20m 배수 제외)"
                         + (fell > 0 ? $" · 그중 {fell}개는 정지 밖이라 원지반 표고로 놓았다" : "")
                         + (bad > 0 ? $" · 실패 {bad}개" : "")
                         + "  ※값은 안 쓰고 라벨 자리로만 쓴다");
            return chainId;
        }
        catch (System.Exception ex) { log.AppendLine("측점 라벨용 체인 실패 — " + ex.Message); return ObjectId.Null; }
    }

    /// <summary>실측 원지반 종단을 감출 때 쓰는 이름 — <b>'원지반'이 들어가면 안 된다.</b>
    /// 이름으로 종단을 고르는 코드가 여럿이라(밴드 배선·세로줄 자르기), 같은 말이 둘이면
    /// <b>마지막에 잡힌 것</b>이 쓰여 결과가 실행 순서에 매인다.</summary>
    private const string GroundRawName = "DH_지반실측(숨김)";

    /// <summary>꺾은선을 만드는 동안 쓰는 임시 이름 — 성공이 확정된 뒤에야 <c>DH_원지반</c>이 된다.
    /// 여기에도 <b>'원지반'을 넣지 않는다</b>(중간 상태가 이름으로 잡히면 안 된다).</summary>
    private const string GroundTempName = "DH_지반작업중";

    /// <summary>★★[v32.23 · JACK 0812] <b>원지반을 2D 설계처럼 꺾은선으로 다시 그린다.</b>
    ///
    /// <para>JACK: <i>"2d설계에서 원지반은 사실 직선을 이용해서 쭉쭉 긋다보니깐(일종의 버퍼) 굴곡부가 보이고
    /// 조금이라도 각도가있으면 그부분에 측점을 두는데, civil3d는 굴곡부가 실제와 유사하게 부드러운 선으로
    /// 나오다보니 측점 추가하는게 힘든것같은데 이걸 어떻게 2d설계하듯히 할수없을까?"</i></para>
    ///
    /// <para><b>정확한 진단이었다.</b> v32.21은 속으로는 이미 2D 설계처럼 하고 있었다 —
    /// 부드러운 선을 직선 몇 개로 근사해 그 꺾임을 측점으로 뽑았다. 그런데 <b>그 직선을 도면에 안 그렸다.</b>
    /// 계산에만 쓰고 버리니 도면에는 부드러운 선 위에 측점만 찍혀,
    /// <b>왜 거기가 굴곡부인지 눈에 보이지 않았다.</b></para>
    ///
    /// <para>→ 그 근사선을 <b>원지반선으로 그린다.</b> 그러면 도면이 2D 설계도와 같아지고,
    /// 덤으로 <b>토공 계산과 도면이 일치</b>한다 — 평균단면법은 단면 사이를 직선으로 보는데
    /// 도면의 원지반선도 바로 그 직선이 된다(종전엔 도면=곡선, 계산=직선으로 미세하게 달랐다).</para>
    ///
    /// <para><b>정점은 확정된 측점 전부다</b>(v32.24). 꺾임점만 쓰면 데이라잇 자리에 정점이 없어
    /// 직선이 그 자리를 가로지르고 <b>계획선이 원지반선을 뚫는다</b>(JACK 0812 스샷).
    /// 측점마다 <b>실측 표고</b>를 박으므로 그 자리 지반고는 전부 실제값이고,
    /// 두 선이 만나야 할 자리에서 정확히 만난다. 정점 사이만 직선 근사이고,
    /// 그 구간의 오차는 <b>실행마다 재서 로그에 적는다</b>(⑤ 자가검증) —
    /// <see cref="GradingSettings.GroundBreakTolZ"/>는 꺾임점을 <b>고르는</b> 기준이지
    /// 이 선의 오차를 보장하는 값이 아니다(측점이 곡선 구간을 기울일 수 있다).</para>
    ///
    /// <para><b>치르는 값.</b> 지표면을 고쳐도 <b>자동으로 안 따라온다</b>(종단도를 다시 돌리면 된다).
    /// 실측 종단은 지우지 않고 <see cref="GroundRawName"/>으로 이름을 바꿔 감춰 둔다 —
    /// 나중에 견주어 볼 때 스타일만 되돌리면 보인다.</para>
    /// 반환=앞으로 쓸 원지반 종단(꺾은선). 실패하면 <b>넘겨받은 것을 그대로</b> 돌려준다.</summary>
    private static ObjectId RebuildGroundAsPolyline(Database db, ObjectId alignId, ObjectId srcId,
        ObjectId layer, ObjectId styleId, ObjectId labelSet,
        System.Collections.Generic.IReadOnlyList<StationMarks.Mark> marks,
        System.Text.StringBuilder log)
    {
        if (srcId.IsNull) { log.AppendLine("  원지반 꺾은선: 원지반 종단이 없어 건너뜀"); return srcId; }
        if (marks == null || marks.Count < 2)
        { log.AppendLine("  원지반 꺾은선: 측점이 2개 미만이라 지표면 종단을 그대로 쓴다"); return srcId; }
        try
        {
            // ── ① <b>측점 자리마다</b> 실측 원지반 표고를 읽어 정점을 만든다.
            //
            //   ★★[v32.24 · JACK 0812 스샷] <b>여기가 v32.23의 실패 지점이다.</b>
            //   종전엔 꺾임점(Douglas-Peucker 결과)<b>만</b> 정점으로 삼았다. 그러면 데이라잇 자리에
            //   정점이 없어 직선이 그 자리를 가로지르고, 원지반선이 실제보다 위로 지나
            //   <b>계획선이 원지반선을 뚫고 내려간다</b>(JACK: "저부분은 딱맞아야해").
            //
            //   → <b>확정된 측점 전부</b>를 정점으로 쓴다. 데이라잇·절성경계·사면·소단 자리에
            //   <b>실측 표고</b>가 박히므로 두 선이 정확히 만난다. 그 사이는 여전히 직선이라
            //   2D 설계 도면 모양은 그대로다. 꺾임점도 이미 측점에 들어 있다(같은 목록이다).
            var outPts = new System.Collections.Generic.List<StationMarks.GroundPt>();
            var raw = new System.Collections.Generic.List<StationMarks.GroundPt>();   // 원본 표본(자가검증 기준)
            int nNoElev = 0;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                if (tr.GetObject(srcId, OpenMode.ForRead) is not CivilDb.Profile src)
                { log.AppendLine("  원지반 꺾은선: 종단을 못 열어 건너뜀"); tr.Commit(); return srcId; }

                // ★[검토 지적 · 높음] <b>표고의 테두리를 먼저 읽는다.</b>
                //   <c>ElevationAt</c>이 종단 <b>중간이 빈 구간</b>(누적 구역이 떨어져 있을 때)에서
                //   어떻게 구는지는 문서에 없다 — 예외를 던지면 아래 <c>catch</c>가 받지만,
                //   <b>0.0 같은 값을 돌려주면 그대로 정점이 되어</b> 원지반선이 0까지 곤두박질친다.
                //   있을 수 있는 값의 테두리로 되재면 <b>무엇을 돌려주든 걸린다</b>.
                double zLo = double.NaN, zHi = double.NaN;
                try { zLo = src.ElevationMin; zHi = src.ElevationMax; } catch { }
                bool ZOk(double z) => !double.IsNaN(z) && !double.IsInfinity(z)
                                      && (double.IsNaN(zLo) || double.IsNaN(zHi)
                                          || (z >= zLo - 1e-6 && z <= zHi + 1e-6));

                // 자가검증의 기준이 될 <b>원본 표본</b>을 같이 모은다(아래 ⑤).
                try
                {
                    foreach (CivilDb.ProfilePVI q in src.PVIs)
                    { try { raw.Add(new StationMarks.GroundPt(q.RawStation, q.Elevation)); } catch { } }
                }
                catch { }
                raw.Sort((a, b) => a.Station.CompareTo(b.Station));

                // 양 끝을 반드시 넣는다 — 측점이 종단 끝에 정확히 안 닿아도 선이 끊기지 않게.
                var sts = new System.Collections.Generic.List<double> { src.StartingStation };
                foreach (var m in marks) sts.Add(m.Station);
                sts.Add(src.EndingStation);
                sts.Sort();

                foreach (double s0v in sts)
                {
                    // 같은 자리(1cm)는 하나만 — 정점이 겹치면 길이 0 구간이 생긴다.
                    if (outPts.Count > 0 && s0v - outPts[outPts.Count - 1].Station <= 0.01) continue;
                    double sc = System.Math.Min(System.Math.Max(s0v, src.StartingStation), src.EndingStation);
                    double z;
                    try { z = src.ElevationAt(sc); }
                    catch { nNoElev++; continue; }
                    if (!ZOk(z)) { nNoElev++; continue; }
                    outPts.Add(new StationMarks.GroundPt(sc, z));
                }
                tr.Commit();
            }
            if (outPts.Count < 2)
            { log.AppendLine($"  원지반 꺾은선: 정점이 {outPts.Count}개뿐이라 지표면 종단을 그대로 쓴다"); return srcId; }

            // ── ② 꺾은선을 <b>임시 이름으로 먼저</b> 만든다.
            //
            //   ★★[검토 지적 · 치명] 종전엔 <b>실측 종단을 먼저 감추고</b> 새 종단을 만들었다.
            //   그 사이에 생성이 예외를 던지면 <b>되돌릴 길이 없어 원지반선이 도면에서 통째로 사라진다</b> —
            //   게다가 감춘 이름에는 '원지반'이 없으니(일부러 뺐다) 다른 코드도 그것을 못 찾아
            //   <b>"값은 맞는데 선이 없다"</b>는 가장 찾기 어려운 증상이 된다.
            //   → <b>성공이 확정되기 전에는 실측 종단에 손대지 않는다.</b> 관문을 한 칸 앞으로 옮긴다.
            ObjectId newId = ObjectId.Null; string cerr = null;
            for (int i = 0; i < 20 && newId.IsNull; i++)
            {
                string tmp = i == 0 ? GroundTempName : $"{GroundTempName}-{i}";
                try { newId = CivilDb.Profile.CreateByLayout(tmp, alignId, layer, styleId, labelSet); }
                catch (System.Exception ex) { cerr = ex.Message; }
            }
            if (newId.IsNull)
            {
                log.AppendLine("  원지반 꺾은선: 종단을 못 만들어 지표면 종단을 그대로 쓴다 — " + cerr);
                return srcId;      // 실측은 손도 안 댔다 — 도면은 종전 그대로다
            }

            int made = 0, bad = 0;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var pr = (CivilDb.Profile)tr.GetObject(newId, OpenMode.ForWrite);
                // ★[검토 지적] <c>PVIs</c>는 접근할 때마다 새 래퍼를 만든다 — 루프 밖에서 한 번만 잡는다.
                using var pvis = pr.PVIs;
                foreach (var g in outPts)
                {
                    try { pvis.AddPVI(g.Station, g.Elev); made++; }
                    catch { bad++; }
                }
                tr.Commit();
            }

            // ── ③ 못 만들었으면 <b>새것만 지우면 끝이다</b>(실측은 안 건드렸다).
            if (made < 2)
            {
                log.AppendLine($"  ⚠원지반 꺾은선: PVI를 {made}개밖에 못 심어 되돌린다(실패 {bad}개)"
                               + " — 실측 종단은 그대로라 도면은 종전과 같다");
                try
                {
                    using var tr = db.TransactionManager.StartTransaction();
                    if (tr.GetObject(newId, OpenMode.ForWrite) is CivilDb.Profile badPr) badPr.Erase();
                    tr.Commit();
                }
                catch (System.Exception ex) { log.AppendLine("     빈 종단 지우기 실패 — " + ex.Message); }
                return srcId;
            }

            // ── ④ 이제야 실측을 감추고 꺾은선에 정식 이름을 준다. <b>한 트랜잭션</b>이라
            //   하나라도 실패하면 통째로 물러난다 — '원지반'이 둘이거나 없는 중간 상태가 안 생긴다.
            ObjectId hideStyle = EnsureHiddenProfileStyle(db, log);
            bool swapped = false;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                try
                {
                    if (tr.GetObject(srcId, OpenMode.ForWrite) is CivilDb.Profile src2)
                    {
                        src2.Name = GroundRawName;
                        if (!hideStyle.IsNull) try { src2.StyleId = hideStyle; } catch { }
                    }
                    if (tr.GetObject(newId, OpenMode.ForWrite) is CivilDb.Profile np)
                        np.Name = SectionCommand.ProfGroundName;
                    tr.Commit();
                    swapped = true;
                }
                catch (System.Exception ex) { log.AppendLine("  이름 정리 실패 — " + ex.Message); }
            }
            if (!swapped)
            {
                log.AppendLine("  원지반 꺾은선: 이름 정리가 물러나 되돌린다(실측 종단은 그대로다)");
                try
                {
                    using var tr = db.TransactionManager.StartTransaction();
                    if (tr.GetObject(newId, OpenMode.ForWrite) is CivilDb.Profile bp) bp.Erase();
                    tr.Commit();
                }
                catch { }
                return srcId;
            }

            // ── ⑤ [자가검증] <b>실제로 그려질 이 선</b>이 원본 표본에서 얼마나 벗어나는지 잰다.
            //
            //   ★★[검토 지적 · 높음] 종전 자가검증은 <c>SimplifyGround</c> 안에서
            //   <b>DP가 남긴 점</b>으로 이은 선을 쟀다. 그런데 도면에 그려지는 것은
            //   <b>측점 전부</b>로 다시 이은 이 선이고, <b>점을 더 넣는다고 편차가 반드시 줄지 않는다.</b>
            //   (반례: 곡선 위 한 점을 더 집으면 그 구간이 기울어 반대편 편차가 커진다.)
            //   자를 만들어 놓고 다른 물건을 재고 있었다 — 재는 대상을 실제 선으로 바꾼다.
            double maxDev = 0, maxAt = 0;
            if (raw.Count > 0)
            {
                int k = 0;
                foreach (var r in raw)
                {
                    while (k + 2 < outPts.Count && outPts[k + 1].Station < r.Station) k++;
                    var a = outPts[k]; var b = outPts[k + 1];
                    double ds = b.Station - a.Station;
                    double zl = ds > 1e-9 ? a.Elev + (b.Elev - a.Elev) * (r.Station - a.Station) / ds : a.Elev;
                    double d = System.Math.Abs(r.Elev - zl);
                    if (d > maxDev) { maxDev = d; maxAt = r.Station; }
                }
            }
            double tol = System.Math.Max(0.01, GradingSettings.GroundBreakTolZ);

            log.AppendLine($"  원지반 꺾은선 '{SectionCommand.ProfGroundName}' — 정점 {made}개로 다시 그렸다"
                           + $"(측점 {marks.Count}개 + 양 끝 → 겹침 정리 후 {outPts.Count}개)"
                           + (bad > 0 ? $" · 심기 실패 {bad}개" : "")
                           + (nNoElev > 0 ? $" · 표고가 이상해 뺀 자리 {nNoElev}개" : "")
                           + $" · 실측 종단은 '{GroundRawName}'으로 감췄다"
                           + $"\n     자가검증(실제 그린 선 ↔ 원본 표본 {raw.Count}개): 최대 높이오차 {maxDev:0.###}m"
                           + $" @ {(maxDev > 1e-9 ? maxAt.ToString("0.00") + "m" : "-")}"
                           + (maxDev <= tol + 1e-6 ? $" → 허용치({tol:0.###}m) 안"
                                                   : $"  ⚠허용치({tol:0.###}m)를 넘는다 — 측점이 곡선 구간을 기울여 놓은 자리다")
                           + "\n     ※측점마다 실측 표고를 박았다 — 데이라잇에서 계획선과 정확히 만난다."
                           + "\n     ※종단도와 측점이 이 선을 본다(횡단면도는 지표면에서 직접 뜬다)."
                           + " 지표면을 고치면 종단도를 다시 돌려야 한다.");
            return newId;
        }
        catch (System.Exception ex)
        {
            log.AppendLine("  원지반 꺾은선 실패 — " + ex.Message + "(지표면 종단을 그대로 쓴다)");
            return srcId;
        }
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

    /// <summary>★[v30.2] 한 구역의 데이라잇 링(절토·성토) — 여러 조각이 정본, 옛 번들은 단수로 폴백.</summary>
    private static System.Collections.Generic.IEnumerable<System.Collections.Generic.List<Point3>>
        DaylightRingsOfBundle(GradingBundle b)
    {
        if (b == null) yield break;
        if (b.CutFinalRings != null) { foreach (var r in b.CutFinalRings) if (r is { Count: >= 2 }) yield return r; }
        else if (b.CutFinalRing is { Count: >= 2 }) yield return b.CutFinalRing;
        if (b.FillFinalRings != null) { foreach (var r in b.FillFinalRings) if (r is { Count: >= 2 }) yield return r; }
        else if (b.FillFinalRing is { Count: >= 2 }) yield return b.FillFinalRing;
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
                                             double interval,
                                             out System.Collections.Generic.List<StationMarks.Mark> allMarks,
                                             System.Text.StringBuilder log)
    {
        allMarks = new System.Collections.Generic.List<StationMarks.Mark>();
        try
        {
            double wl = System.Math.Max(1.0, GradingSettings.XsecLeft);
            double wr = System.Math.Max(1.0, GradingSettings.XsecRight);
            var marks = new System.Collections.Generic.List<StationMarks.Mark>();
            var vbars = new System.Collections.Generic.List<StationMarks.VertBar>();   // 벽의 자리·두께
            var wspans = new System.Collections.Generic.List<StationMarks.WallSpan>(); // 벽의 앞·뒤(횡단 (전)(후))
            var cuts = new System.Collections.Generic.List<SectionCommand.Cut>();
            var cutNames = new System.Collections.Generic.List<string>();   // 표시용 이름
            // ★★[JACK 0825] <b>검토선을 두 벌 만든다.</b>
            //   JACK: <i>"단면검토선이 눈으로 안 보인다고 해도 두껍게 보인다든지 측점명이 떡져서
            //   보인다든지 하지 않을까? 아니면 별도의 횡단용 단면검토선을 별도로 복제한다든지."</i>
            //   맞는 걱정이다 — 벽 두께가 2~5cm라 1:100 도면에서 선이 0.2~0.5mm 벌어져 두꺼워 보이고,
            //   <b>측점명 라벨은 확실히 떡진다</b>. 그래서 역할을 나눈다:
            //     표시용 = 측점마다 하나(평면이 보는 것) · 횡단용 = 벽 자리는 (전)(후) 둘(숨긴다)
            var cutsX = new System.Collections.Generic.List<SectionCommand.Cut>();
            var cutNamesX = new System.Collections.Generic.List<string>();
            int nSplit = 0;
            var splitNote = new System.Text.StringBuilder();
            double s0, s1;

            // ── ① 측점 목록을 만든다(읽기만 — 아직 도면에 아무것도 안 만든다).
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var al = (CivilDb.Alignment)tr.GetObject(alignId, OpenMode.ForRead);
                s0 = al.StartingStation; s1 = al.EndingStation;

                // ★★[v30.3 · JACK 0812] <b>가상 사면 지표면의 굴곡선은 더 이상 쓰지 않는다.</b>
                //
                //   JACK: <i>"사면 안에 여러 개 측점이 생기는 이유는 뭐지? 사면 기울기 안에서
                //   측점이 생길 이유가 있나?"</i> — 없다. 사면 한 단은 <b>구배가 일정</b>하므로
                //   그 안에는 꺾임이 없다. 측점이 생기면 그건 잘못 잡은 것이다.
                //
                //   <b>원인.</b> 로그의 사유가 그대로 말해 줬다:
                //   <code>
                //    7.70m 데이라잇(복원)      ← 사면 바깥 끝
                //    8.90m 굴곡부·가상절토_DH  ← 사면 한가운데(엉뚱)
                //    9.89m 굴곡부·가상절토_DH  ← 사면 한가운데(엉뚱)
                //   10.98m 사면·소단(복원)     ← 진짜 소단
                //   11.98m 사면·소단(복원)     ← 진짜 소단
                //   </code>
                //   <c>가상절토_DH</c>는 <b>오버사이즈</b>로 두른 면이라 링이 실제보다 바깥에 있다.
                //   그래서 진짜 소단과 <b>별개의 자리</b>에 한 벌이 더 생겼다 — 그게 사면 한가운데다.
                //   "정지구간 밖은 버린다"는 걸러내기는 <b>데이라잇 안쪽</b>의 헛링은 못 거른다.
                //
                //   → 이제 사면·소단은 <b>번들에서 복원</b>해 쓴다(아래). 그게 최종 형상이고 누적 구역도
                //     전부 들어온다. 오버사이즈 링을 볼 이유가 사라졌으므로 이 출처를 통째로 걷어낸다.

                // ★★[v30.4 · JACK 0812] <b>절성 경계 — 절토와 성토가 바뀌는 자리.</b>
                //   평면의 어떤 선과도 겹치지 않아 교차로는 안 잡힌다. 두 종단에서 직접 잰다.
                CivilDb.Profile prPad = null, prGrd = null;
                try { prPad = tr.GetObject(pidPad, OpenMode.ForRead) as CivilDb.Profile; } catch { }
                try { prGrd = tr.GetObject(pidGround, OpenMode.ForRead) as CivilDb.Profile; } catch { }
                if (prPad != null && prGrd != null)
                    marks.AddRange(StationMarks.FromCutFillLine(prPad, prGrd, s0, s1, 0.5, log));
                else log.AppendLine("  ⚠종단 둘을 못 열어 절성 경계를 못 잡는다");

                // ★★[v32.23~24 · JACK 0812] <b>원지반이 꺾이는 자리도 측점이다.</b>
                //   JACK: <i>"꺾은선으로 바꿨으면 조금이라도 종단상에서 각진 부분은 측점으로 추가해야 해."</i>
                //   여기서 고른 꺾임점이 그대로 측점이 되고, <b>이 목록 전체가 곧 원지반선의 정점</b>이 된다
                //   (<see cref="RebuildGroundAsPolyline"/>이 이 결과를 받아 선을 긋는다).
                //   ※ 실측 종단에서 고른다 — 아직 꺾은선을 만들기 전이라 여기가 유일한 실제 지형 자료다.
                if (prGrd != null)
                    marks.AddRange(StationMarks.MarksFromGround(
                        StationMarks.SimplifyGround(prGrd, s0, s1, GradingSettings.GroundBreakTolZ, log)));
                else log.AppendLine("  ⚠원지반 종단을 못 열어 원지반 굴곡부를 못 잡는다");

                // ★[v30.3] 도면의 <c>DH-정지경계</c>는 <b>번들이 없을 때만</b> 쓰는 보조 근거다.
                //   정본은 아래의 번들 복원이다 — 도면 선은 그릴 때 레이어를 지우므로
                //   누적 구역에서 마지막 것만 남아 있을 수 있다. 같은 자리는 합쳐지므로 겹쳐도 해가 없다.
                marks.AddRange(StationMarks.FromLayerLines(tr, db, al,
                                   new[] { LayerDaylight, LayerClip }, "데이라잇(도면)", null, log));

                // ★★[v30.0 · JACK 0812] <b>사면선·소단선도 도면에서 읽는다 — 그게 최종 형상이다.</b>
                //
                //   JACK: <i>"정지면을 이어서 작성한 경우 … 그 모든 과정에 대한 종단이 나와야 해."</i>
                //   가상 사면 지표면(<c>가상절토_DH</c>·<c>가상성토_DH</c>)은 두 가지 한계가 있다:
                //   ① <b>오버사이즈</b>라 잘려나갈 소단까지 들어 있고,
                //   ② 실행할 때마다 다시 만들어져 <b>마지막 구역 것만</b> 남는다.
                //   반면 <c>DH-사면선-*</c>·<c>DH-소단선-*</c> 레이어는 <b>최종 형상</b>이고,
                //   <c>DHNORI</c>가 <b>구역 전체를 순회하며</b> 다시 그린다(뒤 구역에 덮인 부분은 빼면서).
                //   그러니 이쪽이 더 정확하고 더 완전하다.
                //   ※ 여기도 정지구간 판정을 걸지 않는다 — 사면선 자체가 정지의 가장자리라 자기가 걸러진다.
                // ★★[v30.3] 사면·소단은 <b>복원 하나만</b> 쓴다(아래). 도면에 그려진 선을 함께 읽으면
                //   같은 링을 두 번 잡는데, 두 값이 몇 cm만 어긋나도 중복 제거(1cm)를 빠져나가
                //   <b>사면 한가운데에 짝지어진 측점</b>이 생긴다 — 지금 고치는 증상과 같은 모양이다.
                //   출처는 하나여야 한다.

                // ★★[v30.2 · JACK 0812] <b>번들에서 직접 복원한다 — 노리선을 먼저 돌릴 필요가 없다.</b>
                //
                //   JACK: <i>"우리 애드인의 핵심은 편의성이야. 어느 순간엔 뭐 해야 하고 하는 식이면
                //   제약이 생기고 범용성이 떨어져."</i>
                //   도면에 그려진 사면선은 <b>마지막 구역 것만</b> 남아 있을 수 있다(그릴 때 레이어를 지우므로).
                //   그렇다고 "[노리선]을 먼저 돌리세요"라고 하면 그게 곧 제약이다.
                //   → 노리선이 하는 <b>복원 계산을 여기서 직접</b> 한다. 누적 구역이 전부 들어오고,
                //     뒤 구역이 덮은 자리는 빠지므로 지금 정지면과 맞는다. <b>순서에 매이지 않는다.</b>
                try
                {
                    var regions = GradingBundleStore.TryLoadAll(db, tr, out _);
                    if (regions != null && regions.Count > 0)
                    {
                        // ★[JACK 0825] 옹벽선은 따로 받아 <b>윗선·아랫선을 가운데 한 자리로</b> 접는다.
                        //   측점 명령과 <b>같은 자를 써야</b> 종단도와 [측점 목록]이 같은 말을 한다.
                        var walls = new System.Collections.Generic.List<((int Region, bool Up, int Ring, int Bench) Key,
                                        bool IsCrest, System.Collections.Generic.List<DH.Grading.Core.Point3> Pts, double Slope)>();
                        var rebuilt = NoriCommand.RebuildEdgeLines(regions, out string rdiag, walls);
                        log.AppendLine("  " + rdiag);
                        var asPts = new System.Collections.Generic.List<System.Collections.Generic.List<Point3d>>(rebuilt.Count);
                        foreach (var line in rebuilt)
                        {
                            var q = new System.Collections.Generic.List<Point3d>(line.Count);
                            foreach (var p in line) q.Add(new Point3d(p.X, p.Y, p.Z));
                            asPts.Add(q);
                        }
                        marks.AddRange(StationMarks.FromLines(al, asPts, "사면·소단(복원)", null, log));

                        var wpts = new System.Collections.Generic.List<((int Region, bool Up, int Ring, int Bench) Key,
                                       bool IsCrest, System.Collections.Generic.List<Point3d> Pts, double Slope)>(walls.Count);
                        foreach (var w in walls)
                        {
                            var q = new System.Collections.Generic.List<Point3d>(w.Pts.Count);
                            foreach (var p in w.Pts) q.Add(new Point3d(p.X, p.Y, p.Z));
                            wpts.Add((w.Key, w.IsCrest, q, w.Slope));
                        }
                        StationMarks.FromWallPairs(al, wpts, "옹벽(복원)", null, log, 3.0, vbars, wspans).ForEach(marks.Add);

                        // ★★[v30.2] <b>데이라잇도 번들에서 복원한다.</b> 도면의 <c>DH-정지경계</c>는
                        //   그릴 때 레이어를 지우므로 <b>마지막 구역 것만</b> 남아 있을 수 있다.
                        //   번들엔 구역이 전부 있으니 여기서 바로 꺼내 쓴다 — 도면 상태에 매이지 않는다.
                        //   (뒤 구역이 덮은 자리는 빼야 지금 지표면과 맞는다.)
                        var dl = new System.Collections.Generic.List<System.Collections.Generic.List<Point3d>>();
                        for (int ri = 0; ri < regions.Count; ri++)
                        {
                            var later = GradingBundle.LaterFootprints(regions, ri);
                            var mask = GradingPolygons.RegionMask.Build(later);
                            foreach (var r in DaylightRingsOfBundle(regions[ri]))
                            {
                                var q = new System.Collections.Generic.List<Point3d>(r.Count);
                                foreach (var p in r)
                                {
                                    if (mask != null && mask.Contains(p.X, p.Y))
                                    { if (q.Count >= 2) dl.Add(q); q = new System.Collections.Generic.List<Point3d>(); continue; }
                                    q.Add(new Point3d(p.X, p.Y, p.Z));
                                }
                                if (q.Count >= 2) dl.Add(q);
                            }
                        }
                        marks.AddRange(StationMarks.FromLines(al, dl, "데이라잇(복원)", null, log));
                    }
                    else log.AppendLine("  번들이 없어 사면·소단 복원 생략(도면에 그려진 선만 쓴다)");
                }
                catch (System.Exception ex) { log.AppendLine("  사면·소단 복원 실패 — " + ex.Message); }

                // ★[JACK 0825] 터파기 — 정지 번들과 <b>다른 칸</b>(EXCAV)이라 따로 연다.
                //   가시설(수직 굴착)은 상단·바닥을 가운데 한 자리로 접는다 — 측점 명령과 같은 자.
                try { marks.AddRange(StationMarks.FromExcavation(al, db, tr, null, log, vbars, wspans)); }
                catch (System.Exception ex) { log.AppendLine("  터파기 측점 실패 — " + ex.Message); }

                // ★[JACK 0825] 벽 두께 안 데이라잇을 벽 자리로 — 옹벽 옆 10cm에 측점이 또 서던 것.
                try { StationMarks.PullDaylightToWalls(marks, vbars, log, wspans); }
                catch (System.Exception ex) { log.AppendLine("  벽 측점 정리 실패 — " + ex.Message); }

                // ⓒ 수동 — 선형에 적어 둔 것(DHSTATION).
                var man = StationMarks.Load(tr, alignId);
                marks.AddRange(man);
                if (man.Count > 0) log.AppendLine($"  수동 측점 {man.Count}개");

                // ★★★[JACK 0828 "전/후 측점"] <b>수동으로 찍은 (전)(후)도 벽과 같은 길로 태운다.</b>
                //   JACK: <i>"종단 전용 검토선엔 찍은 그 위치에 측점 하나, 횡단 전용 검토선엔
                //   미세하게 벌려진 두 개."</i>
                //   → <b>측점 목록은 안 건드린다</b>(위 <c>marks.AddRange</c>가 하나만 넣는다).
                //     갈림은 <see cref="StationMarks.WallSpan"/>에만 얹는다 —
                //     그 목록을 보는 것은 <b>횡단뿐</b>이라 종단은 저절로 하나로 남는다.
                //   <b>새 갈래를 만들지 않는 것이 요점이다.</b> 벽이 이미 지나는 길에 태우면
                //   (전)(후) 규칙이 <b>한 곳</b>에만 있고, 벌어지는 거리도 벽과 같은 자를 쓴다.
                int nFb = 0;
                foreach (var m in man)
                {
                    if (!StationMarks.IsFrontBack(m.Why)) continue;
                    // 이미 벽이 잡은 자리면 겹쳐 넣지 않는다 — 두 번 갈리면 검토선 이름이 부딪힌다.
                    if (wspans.Exists(w => System.Math.Abs(w.Mid - m.Station) <= StationMarks.MergeTol)) continue;
                    wspans.Add(new StationMarks.WallSpan(
                        m.Station,
                        m.Station - StationMarks.FrontBackHalf,
                        m.Station + StationMarks.FrontBackHalf,
                        StationMarks.FrontBackKind));
                    nFb++;
                }
                if (nFb > 0)
                    log.AppendLine($"  수동 (전)(후) 측점 {nFb}개 — 종단은 <b>측점 하나</b>, 횡단만 두 장으로 갈린다");
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
            allMarks = all;      // ★[v32.24] 원지반 꺾은선이 이 목록을 그대로 정점으로 쓴다
            LastStationInterval = interval;   // ★[JACK 0826] 횡단도가 같은 이름을 만들 수 있게
            try { LastDbFinger = db.FingerprintGuid.ToString(); } catch { LastDbFinger = ""; }
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
                    // ★★[JACK 0825] <b>벽 자리는 (전)(후) 두 장으로 뜬다.</b>
                    //
                    //   JACK: <i>"보통 옹벽과 가시설은 같은 측점의 (전)(후)로 횡단면도를 생성해.
                    //   측점명은 같지만 (전)(후)라는 이름으로 두 개의 횡단면이 나와야 하고
                    //   한쪽엔 옹벽이 있고 한쪽엔 없는 게 만들어져야 해."</i>
                    //
                    //   선형이 벽을 가로지르면 그 자리에서 지표면이 벽 높이만큼 뚝 떨어진다 —
                    //   단면 하나로는 낮은 쪽·높은 쪽 중 하나만 담긴다.
                    //   Civil은 <b>단면검토선 하나당 횡단면도 한 장</b>이라, 두 장을 얻으려면 검토선이 둘이어야 한다.
                    //   실제 간격은 벽 두께(2~5cm)뿐이라 <b>평면에서는 한 줄로 보인다</b>(1:500에서 0.04mm).
                    //   종단 측점은 접어 둔 <b>가운데 하나</b> 그대로다 — 세 곳이 각자 필요한 것을 본다.
                    string mid = StationMarks.Fmt(m.Station, interval);
                    double st = System.Math.Min(System.Math.Max(m.Station, s0 + eps), s1 - eps);

                    // ── 표시용: 언제나 가운데 하나. 평면 도면이 보는 것이다.
                    if (SectionCommand.TryCut(al, st, wl, wr, out var c)) { cuts.Add(c); cutNames.Add(mid); }
                    else log.AppendLine($"  ⚠{m.Station:F2}m — 법선을 못 구해 단면검토선을 못 놓는다({m.Why})");

                    // ── 횡단용: 벽 자리면 (전)(후) 둘, 아니면 같은 자리 하나.
                    var span = wspans.Find(w => System.Math.Abs(w.Mid - m.Station) <= StationMarks.MergeTol);
                    if (span.Back > span.Front)
                    {
                        // ★★[검토] 여기가 <b>안 고쳐진 자</b>였다 — 벽면 생짜(2cm 간격)를 써서
                        //   두 장이 만들어지긴 해도 <b>같은 그림</b>이 나왔다(JACK: "전후가 안 생겨").
                        //   지금은 스위치로 잠들어 있지만, 켜는 순간 옛 버그가 살아난다. 같은 자로 맞춘다.
                        // ★★★[JACK 0828 · 검토] <b>여기가 또 안 고쳐진 자였다.</b>
                        //   <see cref="XsecViewCommand"/>는 <c>Place</c>로 바꿨는데 이쪽만 <c>PushOut</c>이라,
                        //   수동 (전)(후)의 5cm가 <b>여기서만 0.20m로 부푼다</b> —
                        //   바로 위 주석이 <i>"안 고쳐진 자"</i>를 경고하고 있는데 <b>같은 실수를 반복했다</b>.
                        var (pxF, pxB, _) = DH.Grading.Core.XsecSpan.Place(
                            span.Front, span.Back, StationMarks.IsFixedSpan(span.Kind));
                        foreach (var (stw, tag) in new[] { (pxF, "(전)"), (pxB, "(후)") })
                        {
                            double st2 = System.Math.Min(System.Math.Max(stw, s0 + eps), s1 - eps);
                            if (SectionCommand.TryCut(al, st2, wl, wr, out var cw))
                            { cutsX.Add(cw); cutNamesX.Add(mid + tag); }
                            else log.AppendLine($"  ⚠{stw:F2}m — 법선을 못 구해 횡단용 검토선을 못 놓는다({m.Why}{tag})");
                        }
                        nSplit++;
                        splitNote.Append($" [{mid} {span.Front:F2}/{span.Back:F2}]");
                    }
                    else if (cuts.Count > 0 && cutNames.Count == cuts.Count)
                    { cutsX.Add(cuts[^1]); cutNamesX.Add(mid); }
                }
                if (nSplit > 0)
                    log.AppendLine($"  벽 자리 {nSplit}곳을 (전)(후) 두 장으로 갈랐다{splitNote}");
                tr.Commit();
            }
            if (cuts.Count == 0) { log.AppendLine("단면검토선: 놓을 자리가 없어 건너뜀"); return ObjectId.Null; }

            // ★★[v32.25 · 검토 지적 · 높음] <b>개수 상한을 여기에도 건다.</b>
            //   <see cref="SectionCommand.MaxSections"/> 관문은 종전에 <c>DHSECTION</c>에만 있었다.
            //   원지반 굴곡부(v32.21)가 측점의 <b>새 공급원</b>을 열었고 — 기복이 심한 지형에
            //   기준을 촘촘히 주면 수백 개가 나온다 — 그 하나하나가 나중에 <b>횡단면도 한 장</b>이 된다.
            //   <b>막지는 않는다</b>(종단도는 나와야 한다). 대신 <b>손잡이를 알려 준다</b> —
            //   그 손잡이가 도면설정의 '원지반 굴곡'이다.
            if (cuts.Count > SectionCommand.MaxSections)
            {
                string warn = $"측점이 {cuts.Count}개다(권장 상한 {SectionCommand.MaxSections}개)"
                            + " — 도면이 무거워지고 횡단면도도 그만큼 생긴다."
                            + $" 도면설정의 '원지반 굴곡'을 더 단순한 쪽으로 옮기면 줄어든다(지금 {GradingSettings.GroundBreakTolZ:0.###}m).";
                log.AppendLine("  ⚠" + warn);
                ed.WriteMessage("\n  ⚠" + warn);
            }

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
            // ★[v32.41] 만든 선을 기억한다 — 좌우 끝점이 여기 있으므로 나중에 다시 잴 필요가 없다
            //   (SampleLine에는 끝점을 돌려주는 속성이 없다).
            var made = new List<(ObjectId Id, double St, Point2d L, Point2d R)>();
            for (int i = 0; i < cuts.Count; i++)
            {
                try
                {
                    var pts = new Point2dCollection { cuts[i].Left, cuts[i].Right };
                    string nm = i < cutNames.Count ? cutNames[i] : StationMarks.Fmt(cuts[i].Station, interval);
                    var id = CivilDb.SampleLine.Create($"{groupName}_{nm}", groupId, pts);
                    if (!id.IsNull) { nSl++; made.Add((id, cuts[i].Station, cuts[i].Left, cuts[i].Right)); }
                }
                catch (System.Exception ex) { firstErr ??= $"{cuts[i].Station:F2}m {ex.Message}"; }
            }
            log.AppendLine($"단면검토선 '{groupName}' — {nSl}/{cuts.Count}개 생성 · 좌{wl:0.#}m/우{wr:0.#}m · 표본 지표면 {nSrc}개[{srcNames.ToString().Trim()}]" +
                           (firstErr != null ? $"\n  ⚠첫 실패: {firstErr}" : ""));

            // ★★★[JACK 0828 "전후측점 기능을 쓰면 숫자가 두 개로 표현돼"]
            //   <b>만든 수는 든 수가 아니다.</b> 위 줄은 <c>Create</c>를 몇 번 불렀는지 셀 뿐이라,
            //   그룹에 <b>옛 검토선이 남아 있어도</b> 알 길이 없었다 — 밴드는 그룹을 보므로
            //   남은 선이 곧 <b>같은 자리에 두 번 찍히는 숫자</b>가 된다.
            //   → <b>그룹에게 직접 묻는다.</b> 몇 개가 들어 있고, 가까이 붙은 쌍이 있는지.
            try
            {
                using var trC = db.TransactionManager.StartTransaction();
                if (trC.GetObject(groupId, OpenMode.ForRead) is CivilDb.SampleLineGroup grp)
                {
                    var sts = new List<double>();
                    foreach (ObjectId sid in grp.GetSampleLineIds())
                        try { if (trC.GetObject(sid, OpenMode.ForRead) is CivilDb.SampleLine sl2) sts.Add(sl2.Station); }
                        catch { }
                    sts.Sort();
                    var near = new System.Text.StringBuilder();
                    int nNear = 0;
                    for (int i = 1; i < sts.Count; i++)
                        if (sts[i] - sts[i - 1] < 0.5)
                        { nNear++; if (nNear <= 6) near.Append($" [{sts[i - 1]:F2}↔{sts[i]:F2} {sts[i] - sts[i - 1]:F3}m]"); }
                    log.AppendLine($"  되읽기 — 그룹에 실제로 든 검토선 {sts.Count}개(측점 {cuts.Count}개)"
                                 + (sts.Count != cuts.Count ? "  ⚠<b>수가 다르다 — 옛 선이 남았거나 못 만든 것이 있다</b>" : "")
                                 + (nNear > 0 ? $"\n    ⚠<b>0.5m 안에 붙은 쌍 {nNear}개</b>{near} — 밴드 숫자가 겹쳐 보인다" : ""));
                }
                trC.Commit();
            }
            catch (System.Exception ex) { log.AppendLine("  검토선 되읽기 실패 — " + ex.Message); }
            ed.WriteMessage($"\n  · 단면검토선 {nSl}개 (정측점 {interval:0.#}m + 굴곡부 + 수동)");

            // ★[v32.45] 꾸미기는 여기서 하지 않는다 — 글씨 크기가 <b>도면 축척</b>을 따라야 하는데
            //   축척은 <see cref="SheetCommand.Build"/>가 <b>나중에</b> 정한다(JACK: "측점 문자가 축척이 안 먹음").
            //   만든 것만 넘겨 두고, 축척이 확정된 뒤 <see cref="DecorateSampleLines"/>가 꾸민다.
            LastSampleLines = made;

            // ── ★★[JACK 0825] <b>횡단용 그룹</b> — 벽 자리는 (전)(후) 둘. 평면에서는 숨긴다.
            //   Civil은 <b>단면검토선 하나당 횡단면도 한 장</b>이라, 두 장을 얻으려면 검토선이 둘이어야 한다.
            //   그런데 그 둘을 평면에 두면 선이 두꺼워 보이고 측점명이 떡진다(JACK 지적) —
            //   벽 두께가 2~5cm라 1:100에서 0.2~0.5mm 벌어진다.
            //   → 그룹을 갈라, 평면은 표시용만 보고 횡단면도는 이쪽에서 뽑는다.
            LastXsecGroupId = ObjectId.Null;
            LastWallSpans = wspans;   // ★ 자리 계산은 언제나 남긴다 — 횡단면도를 만들 때 여기서 뽑는다
            LastVertBars = vbars;     // ★ 중심 보정을 받은 막대 목록 — DrawVertBars가 이것을 쓴다
            if (!GradingSettings.BuildXsecSampleLines && nSplit > 0)
                log.AppendLine($"  횡단용 검토선은 만들지 않았다(벽 {nSplit}곳의 (전)(후) 자리는 기억해 둔다) — " +
                               "미리 만들면 그 선이 종단도에도 나타난다");
            if (cutsX.Count > 0 && GradingSettings.BuildXsecSampleLines)
            {
                try
                {
                    string xName = SectionCommand.UniqueName(db, cdoc, SectionCommand.GroupBase + "_단면");
                    var xGroup = CivilDb.SampleLineGroup.Create(xName, alignId);
                    if (!xGroup.IsNull)
                    {
                        ObjectId hideLayer = ObjectId.Null;
                        ObjectId hideStyleX = SectionCommand.EnsureHiddenSampleLineStyle(db, cdoc);
                        try
                        {
                            using var trL = db.TransactionManager.StartTransaction();
                            hideLayer = SectionCommand.EnsureLayer(db, trL, XsecHiddenLayer, 8);
                            // 표본 지표면은 표시용과 같게 — 횡단면도가 값을 뽑을 수 있어야 한다.
                            var gX = (CivilDb.SampleLineGroup)trL.GetObject(xGroup, OpenMode.ForWrite);
                            foreach (CivilDb.SectionSource srcX in gX.GetSectionSources())
                            {
                                bool oursX = surfs.Exists(sx => sx.SurfId == srcX.SourceId);
                                try { srcX.IsSampled = oursX; } catch { }
                            }
                            trL.Commit();
                        }
                        catch (System.Exception ex) { log.AppendLine("  횡단용 준비 경고 — " + ex.Message); }

                        int nX = 0;
                        for (int i = 0; i < cutsX.Count; i++)
                        {
                            try
                            {
                                var ptsX = new Point2dCollection { cutsX[i].Left, cutsX[i].Right };
                                string nmX = i < cutNamesX.Count ? cutNamesX[i] : StationMarks.Fmt(cutsX[i].Station, interval);
                                var idX = CivilDb.SampleLine.Create($"{xName}_{nmX}", xGroup, ptsX);
                                if (idX.IsNull) continue;
                                nX++;
                                // ★★[JACK 0825] <b>스타일로 끈다 — 객체 속성으로는 안 숨는다.</b>
                                //   레이어를 옮겨도, <c>Visible=false</c>로 해도 그대로 보였다.
                                //   Civil 객체는 <b>스타일이 화면을 전담</b>하고 자기 표시 속성을 안 쓴다 —
                                //   터파기 종단선이 초록으로 나오던 것과 같은 구조다.
                                //   → 선·정점이 모두 꺼진 전용 스타일을 붙인다. 횡단면도 생성에는 지장이 없다
                                //     (기하 데이터를 읽는 별개 경로다).
                                try
                                {
                                    using var trE = db.TransactionManager.StartTransaction();
                                    if (trE.GetObject(idX, OpenMode.ForWrite) is CivilDb.SampleLine slX)
                                    {
                                        if (!hideStyleX.IsNull) slX.StyleId = hideStyleX;
                                        if (!hideLayer.IsNull) slX.LayerId = hideLayer;
                                        slX.Visible = false;          // 보조 — 먹으면 좋고 아니어도 스타일이 잡는다
                                    }
                                    trE.Commit();
                                }
                                catch { }
                            }
                            catch { }
                        }

                        // 그 레이어는 꺼 둔다 — 평면에 안 보여야 한다.
                        try
                        {
                            using var trO = db.TransactionManager.StartTransaction();
                            var lt = (LayerTable)trO.GetObject(db.LayerTableId, OpenMode.ForRead);
                            if (lt.Has(XsecHiddenLayer))
                            {
                                var lr = (LayerTableRecord)trO.GetObject(lt[XsecHiddenLayer], OpenMode.ForWrite);
                                if (!lr.IsOff) lr.IsOff = true;
                            }
                            trO.Commit();
                        }
                        catch { }

                        LastXsecGroupId = xGroup;
                        log.AppendLine($"횡단용 검토선 '{xName}' — {nX}/{cutsX.Count}개 " +
                                       $"(벽 {nSplit}곳은 (전)(후) 두 장) · " +
                                       (hideStyleX.IsNull ? "⚠숨김 스타일을 못 만들어 보일 수 있다"
                                                          : $"스타일 '{SectionCommand.HiddenSampleLineStyleName}'로 숨김"));
                    }
                }
                catch (System.Exception ex) { log.AppendLine("  횡단용 검토선 실패 — " + ex.Message); }
            }

            // ── ④ 측점 행이 쓸 <b>라벨 자리 전용 체인</b>(값은 안 쓴다).
            LastLabelChainId = BuildLabelChain(db, alignId, pidPad, pidGround, all, interval, log);
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

        // ★★[v32.29 · JACK 0813] <b>더 이상 묻지 않는다 — 이 애드인은 토공 전용이다.</b>
        //   JACK: <i>"종단도 정보표시표는 없애. 관로는 이 애드인에서 안 할 거야, 새로운 애드인을 별도로 만들 거야.
        //   선택에서도 안 떠도 돼. 무조건 토공이야 이 애드인은."</i>
        //   0810에 '실행할 때 고른다'로 정했던 것을 거둔다 — 고를 것이 하나뿐이면 묻는 것 자체가 손해다.
        string want = GradingSettings.BandSet;   // 항상 "토공"(상수)

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

    /// <summary>★★[JACK 0825] <b>종단의 옹벽·가시설 — 굵은 수직 막대.</b>
    ///
    /// <para>JACK: <i>"옹벽 부분은 종단에서 선을 두껍게 처리해서 직각으로 보이게 하자."</i>
    /// 2D 설계 종단도가 그렇게 그린다 — <b>옹벽은 시안 굵은 막대</b>, <b>가시설은 마젠타 굵은 막대</b>.
    /// 실제 형상은 1:0.05라 살짝 기울어 있지만, 도면에서는 직각 한 줄이 관행이다.</para>
    ///
    /// <para>막대의 위·아래 표고는 <b>측점과 같은 자</b>에서 나온다
    /// (<see cref="StationMarks.CollectVertBars"/>) — 그래야 막대 자리에 측점이 정확히 선다.</para>
    ///
    /// <para><b>폭은 15cm 고정이다.</b> JACK: <i>"어차피 옹벽 최대높이는 15m 이하일 건데
    /// 이때 0.01이면 15cm잖아? 이걸로 하면 어때?"</i> — <b>가장 두꺼운 경우의 두께</b>를 모두에게 쓴다.</para>
    ///
    /// <para>처음엔 <c>구배 × 벽 높이</c>로 실제 두께를 그렸다(JACK: <i>"막대 굵기는 정하지 말고
    /// 단높이를 가지고 폭을 계산하면 되잖아"</i>). 그때는 구배가 0.05라 5m 벽이 25cm였다.
    /// 그런데 <b>하한이 0.01로 내려가면서 그 두께가 의미를 잃었다</b> — 애초에 실제 구조물 치수가 아니라
    /// TIN이 안 무너지게 눕혀 둔 인공 기울기이고, 이제는 5cm까지 얇아져 도면에서 사라진다
    /// (실측: 0.42m 잔여단 → 폭 2cm).</para>
    ///
    /// <para>도면 관행도 이쪽이 맞다 — 옹벽은 <b>굵은 직각 선 하나</b>로 그리지 높이에 따라
    /// 굵기를 달리하지 않는다. 15cm면 1:100에서 1.5mm, 1:500에서 0.3mm다.</para></summary>
    private const double BarWidth = 0.15;   // 단높이 상한 15m × 구배 하한 0.01

    /// <summary>★[JACK 0825] 벽 앞·뒤 자리에서 종단을 읽어 막대의 위·아래를 정한다.
    /// <para>가운데는 벽면 한복판이라 중간 표고가 나오고, 선 값은 자르기 전 오버사이즈라 한 단을 꽉 채운다.
    /// 앞·뒤는 <b>둘 다 벽면 밖</b>이라 실제 지표면 값이다.</para></summary>
    private static bool TryWallFromProfile(StationMarks.VertBar b,
                                           System.Collections.Generic.List<StationMarks.WallSpan> spans,
                                           System.Func<ObjectId, double, double> elevOf,
                                           ObjectId pidPad, ObjectId pidGround,
                                           out double zTop, out double zBot)
    {
        zTop = zBot = double.NaN;
        if (spans == null) return false;
        var sp = spans.Find(w => System.Math.Abs(w.Mid - b.Station) <= StationMarks.MergeTol);
        if (!(sp.Back > sp.Front)) return false;

        // 앞·뒤 각각에서 계획면·원지반을 읽어 넷 중 최대·최소를 쓴다.
        double[] zs =
        {
            elevOf(pidPad, sp.Front), elevOf(pidGround, sp.Front),
            elevOf(pidPad, sp.Back),  elevOf(pidGround, sp.Back),
        };
        double hi = double.MinValue, lo = double.MaxValue;
        foreach (var z in zs)
        {
            if (double.IsNaN(z)) continue;
            if (z > hi) hi = z;
            if (z < lo) lo = z;
        }
        if (hi == double.MinValue || lo == double.MaxValue || hi - lo < 1e-6) return false;
        zTop = hi; zBot = lo;
        return true;
    }

    /// <summary>★★★[JACK 0828 "종단이나 횡단에서 각 지층과 지하수위의 각층의 좌측 선 위에 해당 층이름을 적어줘"]
    ///
    /// <para><b>왼쪽 끝을 찾는 것이 일의 전부다.</b> 지층면은 시추공을 둘러싼 사각형으로 만들어지므로
    /// 노선 시작 측점에서는 <b>지표면 밖</b>일 수 있다 — 그러면 <c>ElevationAt</c>이 값을 못 준다.
    /// 그래서 시작부터 조금씩 나아가며 <b>처음으로 값이 나오는 자리</b>를 쓴다.
    /// 첫 자리에서 실패했다고 포기하면 이름이 통째로 안 나온다.</para>
    ///
    /// <para>글자 높이는 <b>종단뷰가 실제로 차지한 높이</b>에서 뽑는다 — 종단은 세로를 부풀려
    /// 그리는 일이 많아(수직 과장) 평면 축척을 그대로 쓰면 글씨가 화면을 덮는다.</para></summary>
    private static int DrawProfStrataNames(Database db, ObjectId pvId,
        System.Collections.Generic.List<(ObjectId Pid, string Nm, bool Water)> items,
        System.Text.StringBuilder log)
    {
        if (pvId.IsNull || items == null || items.Count == 0) return 0;
        int n = 0, miss = 0, nWiped = 0;
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            if (tr.GetObject(pvId, OpenMode.ForRead) is not CivilDb.ProfileView pv) { tr.Commit(); return 0; }
            var ext = ((Entity)pv).GeometricExtents;
            double drawH = ext.MaxPoint.Y - ext.MinPoint.Y;
            double txtH = System.Math.Max(0.3, drawH * 0.025);
            double gap = txtH * 0.4;
            double st0 = pv.StationStart, st1 = pv.StationEnd;
            double step = System.Math.Max(0.5, (st1 - st0) / 200.0);

            var ms = (BlockTableRecord)tr.GetObject(
                SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
            var layS = SectionCommand.EnsureLayer(db, tr, ProfStrataNameLayer, SectionCommand.StrataAci);
            var layW = SectionCommand.EnsureLayer(db, tr, ProfWaterNameLayer, SectionCommand.WaterAci);
            var kst = ImportGisCommand.EnsureKoreanTextStyle(db, tr);

            // ★★[JACK 0828 검토] <b>먼저 지운다.</b> 종단도는 측점을 찍을 때마다 다시 그려지므로,
            //   안 지우면 같은 자리에 같은 글자가 <b>겹쳐 쌓여</b> 굵어진 것처럼만 보인다.
            //   우리가 만든 레이어라 남의 것을 건드릴 일이 없다(막대가 걸어 둔 것과 같은 방식).
            
            foreach (ObjectId id in ms)
            {
                try
                {
                    if (tr.GetObject(id, OpenMode.ForRead) is not Entity e) continue;
                    if (e.LayerId != layS && e.LayerId != layW) continue;
                    tr.GetObject(id, OpenMode.ForWrite).Erase(); nWiped++;
                }
                catch { }
            }

            foreach (var it in items)
            {
                try
                {
                    if (tr.GetObject(it.Pid, OpenMode.ForRead) is not CivilDb.Profile pr) { miss++; continue; }
                    double tx = 0, ty = 0; bool got = false;
                    for (double st = st0; st <= st1 + 1e-9 && !got; st += step)
                    {
                        double z;
                        try { z = pr.ElevationAt(st); } catch { continue; }
                        if (double.IsNaN(z) || double.IsInfinity(z)) continue;
                        if (pv.FindXYAtStationAndElevation(st, z, ref tx, ref ty)) got = true;
                    }
                    if (!got) { miss++; continue; }

                    var t = new DBText
                    {
                        TextString = it.Nm,
                        Height = txtH,
                        Justify = AttachmentPoint.BottomLeft,   // 선 <b>위에</b> 얹는다
                    };
                    t.SetDatabaseDefaults(db);
                    var lay = it.Water ? layW : layS;
                    if (!lay.IsNull) t.LayerId = lay;
                    if (!kst.IsNull) t.TextStyleId = kst;
                    var p = new Point3d(tx + gap, ty + gap, 0);
                    t.Position = p; t.AlignmentPoint = p;
                    ms.AppendEntity(t); tr.AddNewlyCreatedDBObject(t, true);
                    n++;
                }
                catch { miss++; }
            }
            tr.Commit();
        }
        catch (System.Exception ex) { log?.AppendLine("  종단 지층이름 실패 — " + ex.Message); return 0; }

        log?.AppendLine($"  종단 지층이름 {n}개 — 각 선 <b>왼쪽 끝 위</b>에 직접 씀"
                      + (nWiped > 0 ? $" · 옛것 {nWiped}개 지움" : "")
                      + (miss > 0 ? $" · ⚠자리를 못 잡은 것 {miss}개(종단뷰 범위 밖)" : ""));
        return n;
    }

    private static string DrawVertBars(Database db, ObjectId pvId, ObjectId alignId,
                                       ObjectId pidGround, ObjectId pidPad, ObjectId pidExcav,
                                       System.Collections.Generic.List<StationMarks.WallSpan> wspans,
                                       System.Text.StringBuilder log)
    {
        const short AciCyan = 4, AciMagenta = 6;

        int nWall = 0, nShore = 0, nMiss = 0, wiped = 0;
        int nProfile = 0, nLine = 0;
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            if (tr.GetObject(pvId, OpenMode.ForRead) is not CivilDb.ProfileView pv ||
                tr.GetObject(alignId, OpenMode.ForRead) is not CivilDb.Alignment al)
            { tr.Commit(); return "종단 막대: 종단뷰나 선형을 못 찾았다"; }

            // ★★[JACK 0826] <b>이미 보정된 목록을 쓴다.</b> 새로 계산하면 중심 보정을 잃는다.
            //   측점 수집이 안 돌았을 때만 물러서서 새로 계산한다.
            bool haveBars = LastVertBars != null && LastVertBars.Count > 0;
            var bars = haveBars ? LastVertBars : StationMarks.CollectVertBars(al, db, tr, log);
            log?.AppendLine($"     막대 재료 — {(haveBars ? "측점 수집 때 만든 것(중심 보정됨)" : "여기서 새로 계산(보정 없음)")} {bars.Count}개");
            var lw = SectionCommand.EnsureLayer(db, tr, LayerVBarWall, AciCyan);
            var ls = SectionCommand.EnsureLayer(db, tr, LayerVBarShore, AciMagenta);

            var ms = (BlockTableRecord)tr.GetObject(
                SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);

            // 다시 그릴 때 겹치지 않게 먼저 지운다 — 우리가 만든 레이어라 남의 것을 건드릴 일이 없다.
            foreach (ObjectId id in ms)
            {
                try
                {
                    if (tr.GetObject(id, OpenMode.ForRead) is not Entity e) continue;
                    if (e.LayerId != lw && e.LayerId != ls) continue;
                    tr.GetObject(id, OpenMode.ForWrite).Erase(); wiped++;
                }
                catch { }
            }

            // ★★[JACK 0825] <b>막대의 위·아래는 종단에서 읽는다.</b>
            //
            //   JACK: <i>"여전히 데이라잇에 잘리는 부분의 옹벽은 생성이 안 됐어."</i>
            //
            //   <b>원인.</b> 종전엔 <b>옹벽선 조각의 표고</b>로 막대를 세웠다. 그런데 데이라잇이
            //   한쪽 선을 <b>통째로</b> 자르면 그 벽엔 조각이 하나도 안 남는다(실측 47.20m:
            //   아랫선만 남고 윗선은 재료 자체가 없었다). 조각에서 캐는 한 이 자리는 영영 안 나온다.
            //
            //   → <b>선은 자리만 정하고, 높이는 종단이 준다.</b> 스샷이 그대로 말해 준다 —
            //     옹벽 막대는 <b>계획면↔원지반</b>, 가시설 막대는 <b>계획면↔터파기 바닥</b>이다.
            //     종단은 노선 전 구간에 있으므로 잘릴 일이 없다.
            System.Func<ObjectId, double, double> ElevOf = (pid, st) =>
            {
                if (pid.IsNull) return double.NaN;
                try
                {
                    return tr.GetObject(pid, OpenMode.ForRead) is CivilDb.Profile pr
                         ? pr.ElevationAt(st) : double.NaN;
                }
                catch { return double.NaN; }        // 종단 범위 밖 — 예외를 던진다
            };

            int nFromProfile = 0, nFromLine = 0;
            foreach (var b in bars)
            {
                bool shore = b.Kind != null && b.Kind.Contains("가시설");
                double zTop, zBot;
                if (shore)
                {
                    // ★★[JACK 0825 '가시설이 끝까지 안 생기고 중간에 생기다 말았다 ·
                    //   성토 계획지표면까지 막대가 생겼다'] <b>가시설은 선 값이 정본이다.</b>
                    //
                    //   종전엔 위끝을 <b>계획면 종단</b>으로, 아래끝을 <b>터파기면 종단</b>으로 읽었다. 둘 다 틀렸다:
                    //   ① 위끝 — 성토부에서는 목표면이 <b>원지반</b>이다(§45: 둘 중 낮은 쪽).
                    //      계획면을 쓰면 실제로 파지 않는 성토 몫까지 막대가 올라간다(실측 106.6 지반에 110까지).
                    //   ② 아래끝 — 그 측점 자리의 터파기면은 <b>굴착 법면 위</b>라 바닥이 아니다.
                    //      그래서 막대가 바닥에 못 닿고 중간에서 끊겼다(실측 바닥 105.0인데 106.64에서 멈춤).
                    //
                    //   굴착 상단선과 구조물 바닥선의 <b>교차 표고</b>가 바로 그 두 끝이다 — 종단을 거칠 이유가 없다.
                    zTop = b.ZTop; zBot = b.ZBottom; nFromLine++;
                }
                else if (!double.IsNaN(b.ZTop) && !double.IsNaN(b.ZBottom)
                         && System.Math.Abs(b.ZTop - b.ZBottom) > 1e-6)
                {
                    // ★★[JACK 0826] <b>선 값이 곧 그 단의 높이다 — 종단보다 먼저 본다.</b>
                    //   종단(계획면·원지반)에서 읽으면 언제나 <b>부지 전체 높이</b>가 나온다.
                    //   다단 옹벽에서는 모든 단의 막대가 맨 아래 원지반까지 늘어난다(JACK 스샷).
                    //   크레스트·토우 Z(또는 데이라잇에서 얻은 반대쪽 표고)가 <b>그 단만큼</b>이다.
                    zTop = b.ZTop; zBot = b.ZBottom; nFromLine++;
                }
                else if (TryWallFromProfile(b, wspans, ElevOf, pidPad, pidGround, out zTop, out zBot))
                {
                    // ★★[JACK 0825 '옹벽 막대가 원지반 아래까지 파먹는다'] <b>벽 앞·뒤에서 읽는다.</b>
                    //
                    //   선 값(크레스트·토우 Z)은 <b>데이라잇으로 자르기 전 오버사이즈</b>라 한 단을 꽉 채운다 —
                    //   실측 높이 5.00m가 정확히 단높이였다. 벽이 실제로는 원지반에서 끝나는데도 그 아래까지 그렸다.
                    //   그렇다고 가운데에서 종단을 읽으면 <b>벽면 한복판</b>이라 중간 표고가 나온다(앞서 겪은 것).
                    //   → 벽의 <b>앞 자리에서 낮은 표고</b>, <b>뒤 자리에서 높은 표고</b>를 읽는다.
                    //     둘 다 벽면 밖이라 실제 지표면 값이고, 그 사이가 곧 벽이다.
                    nFromProfile++;
                }
                else if (!double.IsNaN(b.ZTop) && !double.IsNaN(b.ZBottom)
                         && System.Math.Abs(b.ZTop - b.ZBottom) > 1e-6)
                {
                    // ★★[JACK 0825 '옹벽 막대가 생기다 말았다'] <b>옹벽도 선 값이 정본이다.</b>
                    //
                    //   측점을 <b>벽 가운데</b>로 옮긴 뒤로 종단을 읽으면 그 자리가 <b>벽면 한복판</b>이다.
                    //   벽이 거의 수직이라 종단은 위아래를 잇는 중간 표고를 준다 —
                    //   실측 106.44~<b>109.56</b>m(계획면은 110.00)로 막대가 0.44m 짧아졌다.
                    //   크레스트 Z와 토우 Z가 곧 벽의 위아래이고, 짝이 없어도 반대편 최근접 점에서
                    //   표고를 구하므로 <b>항상 값이 있다.</b>
                    zTop = b.ZTop; zBot = b.ZBottom; nFromLine++;
                }
                else
                {
                    double zA = ElevOf(pidPad, b.Station);
                    double zB = ElevOf(pidGround, b.Station);
                    if (!double.IsNaN(zA) && !double.IsNaN(zB) && System.Math.Abs(zA - zB) > 1e-6)
                    { zTop = System.Math.Max(zA, zB); zBot = System.Math.Min(zA, zB); nFromProfile++; }
                    else { zTop = double.NaN; zBot = double.NaN; }
                }

                if (double.IsNaN(zTop) || double.IsNaN(zBot)) { nMiss++; continue; }
                double xT = 0, yT = 0, xB = 0, yB = 0;
                if (!pv.FindXYAtStationAndElevation(b.Station, zTop, ref xT, ref yT) ||
                    !pv.FindXYAtStationAndElevation(b.Station, zBot, ref xB, ref yB))
                { nMiss++; continue; }          // 종단뷰 표고 범위 밖 — 격자가 안 품는 자리다
                if (System.Math.Abs(yT - yB) < 1e-9) { nMiss++; continue; }   // 높이 0이면 벽이 아니다

                // ★★[JACK 0825] <b>적어도 판정 문턱만큼은 굵게.</b>
                //   구배 하한이 0.01로 내려가면 5m 벽 막대가 <b>50mm</b>가 되어 1:200 도면에서 0.25mm —
                //   보통선으로 내려앉고 1:500부터는 사실상 사라진다.
                //   그런데 이 "두께"는 애초에 구조물의 실제 두께가 아니라 <b>TIN이 안 무너지게 눕혀 둔
                //   인공 기울기</b>다(보강토 블록 실제 깊이는 0.50m). 도면 관행도 옹벽은 직각 한 줄이다.
                //   → 실제 구배와 게이트 중 <b>큰 쪽</b>으로 그린다. 높이 비례는 그대로 유지되므로
                //     15m 벽과 3m 벽이 같은 굵기가 되는 일은 없다.
                double width = BarWidth;   // ★[JACK 0825] 고정 — 아래 주석 참조
                log?.AppendLine($"     막대 {b.Kind} {b.Station:F2}m — {zBot:F2}~{zTop:F2}m" +
                                $"(높이 {zTop - zBot:F2}m · 폭 {width:F2}m)" +
                                (shore ? " ※굴착 상단↔구조물 바닥(선 값)" : ""));
                var pl = new Polyline();
                pl.AddVertexAt(0, new Point2d(xB, yB), 0, 0, 0);
                pl.AddVertexAt(1, new Point2d(xT, yT), 0, 0, 0);
                pl.ConstantWidth = System.Math.Max(0.0, width);   // 구배 × 벽 높이 — 그 벽의 실제 두께
                pl.LayerId = shore ? ls : lw;
                ms.AppendEntity(pl);
                tr.AddNewlyCreatedDBObject(pl, true);
                if (shore) nShore++; else nWall++;
            }
            nProfile = nFromProfile; nLine = nFromLine;
            tr.Commit();
        }
        catch (System.Exception ex) { return "종단 막대 실패 — " + ex.Message; }

        return $"종단 막대 — 옹벽 {nWall}개(시안) · 가시설 {nShore}개(마젠타)" +
               (nWall + nShore > 0 ? $" · 폭 {BarWidth}m 고정(최대 벽 15m × 구배 0.01)" : "") +
               (wiped > 0 ? $" · 옛것 {wiped}개 지움" : "") +
               (nMiss > 0 ? $" · 못 세운 것 {nMiss}개(종단뷰 표고 범위 밖이거나 높이 0)" : "") +
               $" · 높이 출처: 종단 {nProfile}개 · 선 {nLine}개";
    }

    /// <summary>★★[v32.35] <b>지워질 선형들에서 수동 측점을 건진다</b> — 선형이 죽으면 확장사전도 죽는다.
    /// <para>선형이 여럿이면 <b>전부 모아</b> 합친다(같은 측점은 <see cref="StationMarks.MergeTol"/> 안에서 하나로).
    /// 어느 하나만 고르면 나머지에 적어 둔 밸브실이 조용히 사라진다.</para></summary>
    private static System.Collections.Generic.List<StationMarks.Mark> HarvestMarks(
        Database db, CivilApp.CivilDocument cdoc, System.Text.StringBuilder log)
    {
        var all = new System.Collections.Generic.List<StationMarks.Mark>();
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            foreach (ObjectId aid in cdoc.GetAlignmentIds())
            {
                if (tr.GetObject(aid, OpenMode.ForRead) is not CivilDb.Alignment al ||
                    !al.Name.StartsWith(SectionCommand.AlignBase)) continue;
                foreach (var m in StationMarks.Load(tr, aid))
                    if (!all.Exists(x => System.Math.Abs(x.Station - m.Station) <= StationMarks.MergeTol))
                        all.Add(m);
            }
            tr.Commit();
        }
        catch (System.Exception ex) { log.AppendLine("  수동 측점 건지기 실패 — " + ex.Message); }
        if (all.Count > 0) log.AppendLine($"  수동 측점 {all.Count}개를 건졌다(선형과 함께 지워지기 전에)");
        return all;
    }

    /// <summary>이 명령이 만든 것을 <b>전부</b> 지운다 — 선형·종단·종단뷰 + 우리가 그린 도면 객체 + 배치.
    ///
    /// <para>★★[v32.27 · JACK 0813] <b>종전엔 선형만 지웠다.</b> 선형을 지우면 딸린 종단·종단뷰가
    /// 따라 사라지므로 그것으로 충분해 보였는데, <b>도곽범위(주황)·노선(노랑)·표고바·제목부·배너는
    /// Civil 객체가 아니라 우리가 직접 그린 평범한 객체</b>라 선형에 매달려 있지 않다.
    /// 아무도 안 지우니 '지우고 새로'를 골라도 겹겹이 쌓였다(JACK 스샷).</para>
    ///
    /// <para><b>노란 노선도 이제 지운다.</b> 종전 방침은 "어느 선으로 만들었는지 남겨 둔다"였는데,
    /// JACK이 0813에 <b>같이 지우라고 확정</b>했다 — 새로 만들면 어차피 새 노선이 그려진다.</para>
    ///
    /// <para>⚠ <b>선형과 함께 수동 측점도 죽는다</b> — 부르기 전에 <see cref="HarvestMarks"/>로 건져야 한다.</para></summary>
    private static int EraseExisting(Database db, CivilApp.CivilDocument cdoc, System.Text.StringBuilder log)
    {
        int n = 0;
        // ① 선형 — 지우면 딸린 종단·종단뷰가 같이 사라진다.
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
            log.AppendLine($"  선형 {n}개 삭제(딸린 종단·종단뷰 포함)");
        }
        catch (System.Exception ex) { log.AppendLine("  선형 삭제 실패 — " + ex.Message); }

        // ② 우리가 직접 그린 도면 객체와 배치 — 소유 레이어를 아는 쪽이 지운다.
        n += SheetCommand.EraseAll(db, log);
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
    /// <summary>★[v32.45] 방금 만든 단면검토선 — <b>꾸미기는 축척이 정해진 뒤</b>라야 하므로 여기 담아 둔다.
    /// <para>글씨 크기를 도면 축척으로 정하는데, 검토선은 <see cref="SheetCommand.Build"/>(축척을 정하는 곳)보다
    /// <b>먼저</b> 만들어진다. 그래서 만들 때 꾸미면 <b>언제나 축척을 모른 채</b> 그리게 된다
    /// (JACK: "측점 문자가 축척이 안 먹음").</para></summary>
    /// <summary>★[JACK 0825] 횡단면도를 뽑을 때 쓸 <b>횡단용</b> 검토선 그룹((전)(후) 포함).</summary>
    /// <summary>★★[JACK 0827 "종단 새로 그리기할 때 기존 종단의 수직 막대가 안 없어져"]
    /// 막대 레이어를 <b>클래스 상수로 올린다</b> — <see cref="SheetCommand.EraseAll"/>이 봐야 하기 때문이다.
    /// <para><b>왜 남았나.</b> 막대는 <b>자기가 다시 그릴 때만</b> 옛것을 지웠다. 그런데 옹벽·가시설이
    /// 사라지면 그리기 경로를 아예 안 타므로 <b>아무도 안 지운다</b>. 지우는 일은 그리는 쪽이 아니라
    /// <b>레이어를 소유한 쪽</b>이 해야 한다 — 그것이 이 프로젝트가 반복해 배운 것이다.</para></summary>
    internal const string ProfStrataNameLayer = "DH-종단-지층이름";
    internal const string ProfWaterNameLayer = "DH-종단-지하수위이름";
    internal const string LayerVBarWall = "DH-종단-옹벽";
    internal const string LayerVBarShore = "DH-종단-가시설";

    /// <summary>★★[검토 0827 · H3] <b>종단을 이름으로 다시 찾지 않는다.</b>
    /// <para>이 파일 851줄이 이미 경고한다 — <i>"이름으로 종단을 고르는 코드가 여럿이라,
    /// 같은 말이 둘이면 <b>마지막에 잡힌 것</b>이 쓰여 결과가 실행 순서에 매인다."</i>
    /// 밴드는 ObjectId로 배선하는데 나중에 쓰는 쪽만 이름으로 찾으면 갈라진다.</para></summary>
    internal static ObjectId LastPidGround = ObjectId.Null, LastPidPad = ObjectId.Null;

    internal static ObjectId LastXsecGroupId = ObjectId.Null;

    /// <summary>★[JACK 0825] 벽의 앞·뒤 자리 — 종단 막대가 지표면을 읽을 때 쓴다.</summary>
    internal static System.Collections.Generic.List<StationMarks.WallSpan> LastWallSpans = new();

    /// <summary>★★[JACK 0826] 측점 수집 때 만든 <b>막대 목록</b> — 이미 중심 보정을 받은 것이다.
    /// <para>JACK: <i>"아직도 절토 옹벽은 시점부에 측점이 만들어져."</i> 실측:
    /// 측점은 <c>44.132</c>(중심)로 갔는데 <b>막대는 44.12</b>(시점)에 남았다.
    /// <see cref="DrawVertBars"/>가 <c>CollectVertBars</c>로 <b>새로 계산</b>해서
    /// <see cref="StationMarks.PullDaylightToWalls"/>의 보정을 못 받았기 때문이다 —
    /// 오늘만 세 번째로 겪는 "두 경로가 다른 자를 쓴다"이다.</para></summary>
    internal static System.Collections.Generic.List<StationMarks.VertBar> LastVertBars = new();

    /// <summary>횡단용 검토선을 담아 두는 레이어 — 평면에 안 보이게 꺼 둔다.</summary>
    internal const string XsecHiddenLayer = "DH-횡단검토선(숨김)";

    private static List<(ObjectId Id, double St, Point2d L, Point2d R)> LastSampleLines = new();

    /// <summary>★[JACK 0826] [횡단도]가 이 목록을 그대로 쓴다 — 종단과 횡단이 <b>같은 측점</b>을 보게.</summary>
    internal static List<(ObjectId Id, double St, Point2d L, Point2d R)> LastSampleLinesPublic => LastSampleLines;

    /// <summary>★[JACK 0826] 측점명을 만들 때 쓴 <b>정측점 간격</b>.
    /// <para>JACK: <i>"횡단은 종단의 측점명하고 맞지가 않아."</i> — 맞다. 횡단이 <c>XsecInterval</c>을
    /// 쓰고 있었는데, 종단은 <b>밴드 간격</b>으로 <c>No.N+xx.xx</c>를 만든다. 같은 자를 써야 이름이 같다.</para></summary>
    internal static double LastStationInterval = 20.0;
    /// <summary>이 값들을 만든 <b>도면</b>. ★[JACK 0826 검토] static이라 AutoCAD를 켜 둔 채
    /// 다른 도면을 열면 <b>옛 도면 측점이 조용히 쓰인다</b> — 측점은 그냥 숫자라 예외도 안 난다.
    /// 도면 지문을 같이 들고 다니며 다르면 안 쓴다.</summary>
    internal static string LastDbFinger = "";

    /// <summary>지금 도면이 그 값들을 만든 도면인가.</summary>
    internal static bool SameDrawing(Database db)
    {
        try { return LastDbFinger.Length > 0 && LastDbFinger == db.FingerprintGuid.ToString(); }
        catch { return false; }
    }

    /// <summary>★★★[v32.41~45 · JACK 0819] <b>단면검토선을 도면답게 — 색·선종류·측점·지시선.</b>
    ///
    /// <para>JACK 요구를 차례로: <i>"선 우측 끝선에 측점"</i> · <i>"정측점의 선은 초록색 그 외는 빨간색"</i> ·
    /// <i>"떡진 부분은 측점을 안 겹치게 띄우고 꺾인 지시선"</i> · <i>"지시선 때문에 측점들이 들쑥날쑥하게 나오는데
    /// 그렇게 나오지 않게"</i> · <i>"지시선이 필요없는 객체도 직선 지시선을"</i> ·
    /// <i>"모든 지시선은 원래 단면검토선하고 살짝 띄워줘"</i> · <i>"글씨 크기는 종단밴드와 항상 동일하게,
    /// 도면축척이 먹게"</i> · <i>"색상은 측선 색이랑 동일하게"</i> · <i>"측점선을 직선으로 나오는데 점선으로"</i></para>
    ///
    /// <para><b>지시선을 모두에게 그린다.</b> 겹친 것만 달면 <b>글씨 시작 자리가 제각각</b>이 된다("들쑥날쑥").
    /// 지시선의 <b>선 방향 길이를 모두 같게</b> 두고 옆으로 밀 일이 있으면 그 안에서 사선으로 처리한다 —
    /// 그러면 안 밀린 것은 저절로 <b>직선</b>이 되고 <b>글씨는 전부 한 줄</b>로 선다. 규칙 하나가 두 요구를 함께 푼다.</para>
    ///
    /// <para><b>레이어로는 검토선 색이 안 바뀐다</b>(v32.41 실측) — Civil 객체는 <b>스타일이 표시를 지배</b>한다.
    /// 검토선은 스타일 둘로 색·선종류를 박고, <b>우리가 그린 지시선·글씨는 레이어</b>로 같은 색을 준다.</para>
    ///
    /// <para>⚠ <b>부르는 자리가 중요하다</b> — <see cref="SheetCommand.Build"/>가 축척을 건 <b>뒤</b>라야
    /// 글씨가 종단 밴드와 같은 크기(종이 2.5mm)로 나온다.</para></summary>
    private static void DecorateSampleLines(Database db, CivilApp.CivilDocument cdoc, ObjectId alignId,
                                            double interval, double wl, double wr,
                                            System.Text.StringBuilder log)
    {
        var made = LastSampleLines;
        if (made == null || made.Count == 0) { log.AppendLine("  검토선 꾸미기: 대상이 없다"); return; }
        try
        {
            int nHid = 0;

            // ── ① 선 스타일 둘 — 색과 <b>점선</b>을 스타일에 박는다(이미 있으면 다시 쓴다).
            //   JACK: <i>"측점선을 직선으로 나오는데 점선으로 바꿔줘."</i>
            //   선종류는 <b>도면에 실려 있어야</b> 쓸 수 있다 — 없으면 표준 파일에서 불러온다.
            string ltName = null;   // DisplayStyle.Linetype은 이름(문자열)을 받는다
            try
            {
                using var trT = db.TransactionManager.StartTransaction();
                var lt = (LinetypeTable)trT.GetObject(db.LinetypeTableId, OpenMode.ForRead);
                foreach (string nm in new[] { "DASHED", "HIDDEN", "CENTER" })
                {
                    if (lt.Has(nm)) { ltName = nm; break; }
                    try { db.LoadLineTypeFile(nm, "acadiso.lin"); } catch { }
                    try { db.LoadLineTypeFile(nm, "acad.lin"); } catch { }
                    if (lt.Has(nm)) { ltName = nm; break; }
                }
                trT.Commit();
            }
            catch (System.Exception ex) { log.AppendLine("  점선 선종류 준비 실패 — " + ex.Message); }

            ObjectId stMajor = ObjectId.Null, stMinor = ObjectId.Null;
            try
            {
                var col = cdoc.Styles.SampleLineStyles;
                ObjectId Ensure(string nm, short aci)
                {
                    ObjectId sid = ObjectId.Null;
                    try { sid = col[nm]; } catch { }
                    if (sid.IsNull) { try { sid = col.Add(nm); } catch { return ObjectId.Null; } }
                    try
                    {
                        using var trX = db.TransactionManager.StartTransaction();
                        if (trX.GetObject(sid, OpenMode.ForWrite) is CivilDb.Styles.SampleLineStyle ss)
                        {
                            using var ds = ss.GetDisplayStylePlan(CivilDb.Styles.SampleLineDisplayStyleType.Lines);
                            ds.Visible = true;
                            ds.Color = Autodesk.AutoCAD.Colors.Color.FromColorIndex(
                                Autodesk.AutoCAD.Colors.ColorMethod.ByAci, aci);
                            if (ltName != null) { try { ds.Linetype = ltName; } catch { } }
                        }
                        trX.Commit();
                    }
                    catch { }
                    return sid;
                }
                stMajor = Ensure("DH_검토선_정측점(초록)", 3);
                stMinor = Ensure("DH_검토선_보조(빨강)", 1);
            }
            catch (System.Exception ex) { log.AppendLine("  검토선 스타일 준비 실패 — " + ex.Message); }

            // ── ①-b <b>선형에 딸린 측점 라벨을 숨긴다</b>(JACK: "기존에 있던 살구색 측점문자는 숨겨줘").
            //   스샷의 <c>BP: 0+000.00</c>이 그것이다 — <b>단면검토선 라벨이 아니라 선형 라벨</b>이었다.
            //   선형을 만들 때 라벨셋 '_없음'을 골랐는데 그 이름이 없어 '표준'이 걸린 결과다.
            //   <c>GetLabelGroupIds</c>로 <b>이 선형 것만</b> 집으므로 남의 선형은 건드리지 않는다.
            try
            {
                using var trA = db.TransactionManager.StartTransaction();
                if (trA.GetObject(alignId, OpenMode.ForRead) is CivilDb.Alignment al)
                    foreach (ObjectId gid in al.GetLabelGroupIds())
                    {
                        try
                        {
                            if (trA.GetObject(gid, OpenMode.ForWrite) is not CivilDb.LabelGroup lg) continue;
                            uint n = lg.SubEntityCount;
                            for (uint k = 0; k < n; k++)
                            {
                                try
                                {
                                    var se = lg.GetAt(k);
                                    if (se != null && se.Visibility) { se.Visibility = false; nHid++; }
                                }
                                catch { }
                            }
                        }
                        catch { }
                    }
                trA.Commit();
            }
            catch (System.Exception ex) { log.AppendLine("  선형 측점 라벨 숨기기 실패 — " + ex.Message); }

            // ── ② 글씨 크기 = <b>종단 밴드와 같은 2.5mm</b> × 도면 축척(JACK 요구).
            //   ※ <c>SetDrawingScale</c>이 <c>PaperUnits=1000</c> 규약으로 걸어 둔 것만 믿는다 —
            //     아니면 남이 걸어 둔 값이므로 검토선 폭에 비례해 물러난다.
            const double slTextMm = 2.5;
            double dwgScale = 0;
            try
            {
                if (db.Cannoscale is Autodesk.AutoCAD.DatabaseServices.AnnotationScale asc &&
                    System.Math.Abs(asc.PaperUnits - 1000.0) < 1e-6) dwgScale = asc.DrawingUnits;
            }
            catch { }
            // ★★[v32.49 · JACK 0819] <b>도면설정에서 고른 검토선 축척이 먼저다.</b>
            //   JACK: <i>"주석 축척 연동하지 말고 도면설정에 단면검토선 주석 축척 선택박스를 넣고
            //   저장을 누를 때 업데이트되게."</i> · <i>"측점 기능으로 추가할 때 생기는 것도 그 시점의 도면설정을 따라가야 함."</i>
            //   → 이 함수는 <b>다시 그릴 때마다</b> 설정을 새로 읽으므로, 측점을 찍어 재작성돼도 그때 값이 쓰인다.
            //   0(자동)이면 도면에 걸린 축척을, 그것도 없으면 검토선 폭에 비례해 물러난다.
            double useScale = GradingSettings.SectionLineScale > 0 ? GradingSettings.SectionLineScale : dwgScale;
            double slH = useScale > 0 ? slTextMm / 1000.0 * useScale
                                      : System.Math.Max(0.20, (wl + wr) * 0.015);
            double slGap0 = slH * 0.6;                 // 검토선 끝에서 지시선 시작까지(살짝 띄움)
            double La = slH * 0.8, Lb = slH * 1.4, Lc = slH * 0.6;   // 지시선 세 도막(합=선 방향 길이·모두 같다)
            double slGapT = slH * 0.5;                 // 지시선 끝에서 글씨까지

            ObjectId layMajor, layMinor;
            using (var trL = db.TransactionManager.StartTransaction())
            {
                layMajor = SectionCommand.EnsureLayer(db, trL, LayerSlMajor, 3);
                layMinor = SectionCommand.EnsureLayer(db, trL, LayerSlMinor, 1);
                trL.Commit();
            }

            // ── ③ <b>떡진 곳을 미리 푼다.</b> 글씨는 선을 따라 세로로 서므로 밀어내는 방향은
            //   <b>선에 수직</b>(=노선을 따라가는 방향)이고, 측점 값이 곧 그 방향의 거리다.
            double minGap = slH * 1.25;
            var off = new double[made.Count];
            double prevS = double.NegativeInfinity;
            for (int i = 0; i < made.Count; i++)
            {
                double want = System.Math.Max(made[i].St, prevS + minGap);
                off[i] = want - made[i].St;
                prevS = want;
            }

            int nMajor = 0, nText = 0, nStyled = 0, nBent = 0;
            using (var trS = db.TransactionManager.StartTransaction())
            {
                var btS = (BlockTable)trS.GetObject(db.BlockTableId, OpenMode.ForRead);
                var msS = (BlockTableRecord)trS.GetObject(btS[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                for (int i = 0; i < made.Count; i++)
                {
                    var m = made[i];
                    double st = m.St;
                    // ★★★[v32.48 · JACK 0819 "여전히 +00.00으로 나와"] <b>정측점 판정이 너무 빡빡했다.</b>
                    //
                    //   종전은 <c>plus < 1e-4</c>(0.1mm)였다. 측점 값에 <b>0.001m 정도의 오차</b>만 있어도
                    //   정측점으로 안 잡히고, 그러면서 표시는 반올림돼 <c>+00.00</c>이 된다 —
                    //   <b>"No.0이어야 할 자리가 +00.00 빨간색"</b>이 정확히 그 모습이었다.
                    //   숫자가 0으로 보이는데 0이 아니라고 판정하니 눈으로는 원인을 찾을 수가 없다.
                    //
                    //   → <b>가장 가까운 정측점과의 거리</b>로 재고 허용오차를 <b>5mm</b>로 둔다.
                    //     반올림을 쓰므로 <b>위에서 접근하는 경우</b>(19.998 → 20.0)도 함께 잡힌다 —
                    //     내림만 쓰면 그 자리는 <c>+19.998</c>로 적히고 정측점을 놓친다.
                    //   ★[JACK 0826 검토] 자를 <b>StationMarks로 옮겼다</b> — 같은 판단을 네 곳에서
                    //   따로 하고 있었고, 그중 횡단 이름 쪽만 옛 0.1mm 자를 들고 있어 v32.48 사고가 되살아났다.
                    bool major = StationMarks.IsMajor(st, interval, out int no);
                    double plus = st - no * interval;
                    if (!major)
                    {
                        no = (int)System.Math.Floor(st / interval + 1e-9);
                        plus = st - no * interval;
                    }
                    if (major) nMajor++;
                    ObjectId lay = major ? layMajor : layMinor;

                    try
                    {
                        if (trS.GetObject(m.Id, OpenMode.ForWrite) is CivilDb.SampleLine sl)
                        {
                            ObjectId want = major ? stMajor : stMinor;
                            if (!want.IsNull) { sl.StyleId = want; nStyled++; }
                        }
                    }
                    catch { }

                    var a = new Point3d(m.L.X, m.L.Y, 0);
                    var b = new Point3d(m.R.X, m.R.Y, 0);
                    var dir = b - a;
                    if (dir.Length < 1e-9) continue;
                    dir = dir.GetNormal();

                    // 밀어내는 방향 = 선에 수직. <b>측점이 커지는 쪽</b>으로 부호를 맞춘다.
                    var perp = new Vector3d(-dir.Y, dir.X, 0);
                    int probe = i + 1 < made.Count ? i + 1 : i - 1;
                    if (probe >= 0 && probe < made.Count)
                    {
                        var nb = new Point3d(made[probe].R.X, made[probe].R.Y, 0);
                        double sign = (nb - b).DotProduct(perp);
                        if (probe < i) sign = -sign;
                        if (sign < 0) perp = -perp;
                    }

                    // 글씨가 <b>왼쪽 반평면</b>을 향하면 거꾸로 읽힌다 → 180도 접고 <b>오른쪽 정렬</b>로.
                    double ang = System.Math.Atan2(dir.Y, dir.X);
                    bool flip = ang > System.Math.PI / 2 - 1e-9 || ang <= -System.Math.PI / 2 - 1e-9;
                    if (flip) ang -= System.Math.PI;

                    // ── 지시선 — <b>모두에게</b>. 선 방향 길이가 늘 같으므로 글씨가 한 줄로 선다.
                    var p0 = b + dir * slGap0;
                    var p1 = p0 + dir * La;
                    var p2 = p1 + dir * Lb + perp * off[i];
                    var p3 = p2 + dir * Lc;
                    var pl = new Polyline();
                    pl.AddVertexAt(0, new Point2d(p0.X, p0.Y), 0, 0, 0);
                    pl.AddVertexAt(1, new Point2d(p1.X, p1.Y), 0, 0, 0);
                    pl.AddVertexAt(2, new Point2d(p2.X, p2.Y), 0, 0, 0);
                    pl.AddVertexAt(3, new Point2d(p3.X, p3.Y), 0, 0, 0);
                    pl.LayerId = lay;
                    msS.AppendEntity(pl); trS.AddNewlyCreatedDBObject(pl, true);
                    if (off[i] > 1e-6) nBent++;

                    var pos = p3 + dir * slGapT;
                    var t = new DBText
                    {
                        TextString = major ? $"No.{no}" : $"+{plus:00.00}",
                        Height = slH,
                        Rotation = ang,
                        LayerId = lay,                                // 글씨도 선과 같은 색(JACK 요구)
                        HorizontalMode = flip ? TextHorizontalMode.TextRight : TextHorizontalMode.TextLeft,
                        VerticalMode = TextVerticalMode.TextVerticalMid,
                    };
                    // ★[JACK 0819 정렬] <b>Position은 건드리지 않는다.</b> 정렬(Mid·Right)을 쓰면 기준점은
                    //   AlignmentPoint 하나이고, Position을 함께 대입하면 그것이 기준을 되돌려 세로가 어긋난다.
                    t.AlignmentPoint = pos;
                    msS.AppendEntity(t); trS.AddNewlyCreatedDBObject(t, true);
                    nText++;
                }
                trS.Commit();
            }
            log.AppendLine($"  검토선 꾸미기: 정측점 {nMajor}개(초록) · 보조 {made.Count - nMajor}개(빨강)"
                         + $" · 스타일 {nStyled}개{(ltName == null ? "(점선 없음)" : $"(점선 {ltName})")}"
                         + $" · 선형 측점 라벨 숨김 {nHid}개"
                         + $" · 측점 글씨 {nText}개(높이 {slH:F2}m = 종이 {slTextMm:F1}mm"
                         + (useScale > 0 ? $" × 1:{useScale:F0}{(GradingSettings.SectionLineScale > 0 ? " 고정" : " 도면축척")})" : " 상당 — 축척이 없어 폭 비례로 물러남)")
                         + $" · 그중 {nBent}개는 꺾어서 띄움");
        }
        catch (System.Exception ex) { log.AppendLine("  검토선 꾸미기 실패 — " + ex.Message); }
    }
}
