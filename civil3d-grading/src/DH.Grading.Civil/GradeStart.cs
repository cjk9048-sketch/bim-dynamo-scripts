using Autodesk.AutoCAD.DatabaseServices;
using DH.Grading.Core;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;
using AcDoc = Autodesk.AutoCAD.ApplicationServices.Document;
using CivilDb = Autodesk.Civil.DatabaseServices;

namespace DH.Grading.Civil;

/// <summary>★★★[계획 5b단계] <b>정지를 시작하기 전에 정해야 하는 것 — 명령과 도킹창이 함께 쓴다.</b>
///
/// <para><b>왜 떼어냈나.</b> "기존 구역이 있으면 이어서 할까 새로 시작할까"를 가리는 45줄이
/// <see cref="Commands.CreateGradingCommand"/>의 명령 흐름 <b>한복판</b>에 있었다.
/// 도킹창은 그 답을 <b>라디오 단추</b>로 받으므로 그 자리를 그대로 못 쓴다 —
/// 베끼면 <b>한쪽만 고쳐진다</b>(§20·§26).</para>
///
/// <para>★<b>판정은 [생성]을 누르는 순간</b> 한다. 찍은 때와 만드는 때 사이에
/// 사용자가 초기화를 하거나 사면수정을 돌리면 <b>답이 뒤집힌다</b>(계획 §5.1).</para></summary>
internal static class GradeStart
{
    /// <summary>판정 결과 — 무엇으로 만들지와, 왜 그렇게 정했는지.</summary>
    internal readonly record struct Plan(
        /// <summary>정지 방식.</summary>
        Commands.GradeMode Mode,
        /// <summary>기준 지반 — <b>자동으로 정해진</b> 경우에만 값이 있다(이어서·마지막구역 다시).
        /// <c>Null</c>이면 사용자가 고른 것을 써야 한다.</summary>
        ObjectId GroundAuto,
        /// <summary>사람에게 보여 줄 한 줄.</summary>
        string Note,
        /// <summary>막아야 하는가 — 그러면 <see cref="Note"/>를 띄우고 <b>진행하지 않는다</b>.</summary>
        bool Blocked);

    /// <summary>이 도면에 이미 정지 결과가 있는가 — 창이 <c>이어서/새로시작</c> 칸을 보일지 정하는 데 쓴다.</summary>
    internal static bool HasPrevious(Database db, out int nRegion)
    {
        nRegion = 0;
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            var regions = GradingBundleStore.TryLoadAll(db, tr, out _);
            bool has = regions != null && regions.Count > 0
                    && GradingBuilder.SurfaceExistsByBaseName(tr, "정지면_DH");
            nRegion = has ? regions.Count : 0;
            tr.Commit();
            return has;
        }
        catch { return false; }
    }

    /// <summary>★<b>이어서/새로시작을 가린다.</b> 명령은 키워드로, 창은 라디오로 답을 얻어 여기 넘긴다.
    ///
    /// <para>명령판(<c>DHGRADE</c>)의 판정 규칙을 <b>그대로</b> 옮겼다 —
    /// 같은 계획선을 다시 고르면 <b>마지막 구역 재실행</b>, 처음 보는 계획선이면 <b>구역 추가</b>,
    /// <b>중간 구역</b>이면 막는다(아직 지원 안 함).</para></summary>
    /// <param name="append">사용자가 <b>이어서</b>를 골랐는가. 거짓이면 무조건 새로 시작.</param>
    internal static Plan Decide(AcDoc doc, ObjectId planPolyId, bool append)
    {
        var db = doc.Database;
        var fresh = new Plan(Commands.GradeMode.Fresh, ObjectId.Null, "", false);
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            var regions0 = GradingBundleStore.TryLoadAll(db, tr, out _);
            bool hasPrev = regions0 != null && regions0.Count > 0
                        && GradingBuilder.SurfaceExistsByBaseName(tr, "정지면_DH");
            if (!hasPrev || !append) { tr.Commit(); return fresh; }

            // 선택한 계획선이 기존 구역과 같은가 — 핸들 또는 fingerprint로 판정.
            string ph = planPolyId.Handle.ToString();
            System.Collections.Generic.List<Point3>? curB = null;
            try { curB = BoundaryReader.Read(tr, planPolyId); } catch { }
            int matchIdx = -1;
            for (int k = 0; k < regions0!.Count; k++)
                if (regions0[k].PlanHandle == ph ||
                    (curB != null && curB.Count >= 3 && regions0[k].FingerprintMatches(curB)))
                { matchIdx = k; break; }

            Plan r;
            if (matchIdx < 0)
            {
                r = new Plan(Commands.GradeMode.Append,
                             GradingBuilder.FindSurfaceByBaseName(tr, "정지면_DH"),   // 기준=현재 누적면(자동)
                             $"[이어서] 기준 지반 = 현재 정지면_DH (기존 구역 {regions0.Count}개 유지, 새 구역 추가)",
                             false);
            }
            else if (matchIdx == regions0.Count - 1)
            {
                var g = Commands.NoriCommand.FindByHandle(db, regions0[^1].GroundHandle);
                // ★★[검토 0909 · 높음] <b>못 찾으면 막는다 — 말없이 다른 면으로 갈아타지 않는다.</b>
                //   옛 명령은 여기서 <i>"직접 선택합니다"</i>를 찍고 <b>일부러 다시 물었다</b>.
                //   창 흐름에서는 사용자가 이미 아무 TIN이나 골라 뒀을 수 있어,
                //   그대로 흘리면 <b>마지막 구역이 엉뚱한 기준면 위에 다시 만들어진다</b> —
                //   형상과 수량이 조용히 틀리는 부류다.
                r = g.IsNull
                    ? new Plan(Commands.GradeMode.Fresh, ObjectId.Null,
                        "마지막 구역을 만들 때 쓴 기준 지반을 찾을 수 없습니다(지웠거나 이름이 바뀌었습니다).\n\n"
                      + "다른 면으로 대신 만들면 그 구역의 형상과 수량이 달라집니다 —\n"
                      + "[새로시작]으로 처음부터 다시 만들거나, 그 지표면을 되살린 뒤 다시 시도하세요.", true)
                    : new Plan(Commands.GradeMode.RerunLast, g,
                        "[다시] 마지막 구역을 새 값으로 다시 만듭니다", false);
            }
            else
            {
                r = new Plan(Commands.GradeMode.Fresh, ObjectId.Null,
                             $"이 계획선은 이미 구역{matchIdx + 1}로 정지되어 있습니다.\n" +
                             "중간 구역 수정은 아직 지원하지 않습니다 — [새로시작]으로 처음부터 다시 만들어 주세요.",
                             true);
            }
            tr.Commit();
            return r;
        }
        catch (System.Exception ex)
        {
            // [안전] 구역 판정 중 예외 — 조용히 '새로시작'으로 흘러 기존 구역을 날리면 안 됨 → 막는다.
            return new Plan(Commands.GradeMode.Fresh, ObjectId.Null,
                            "기존 정지 구역 확인 중 오류가 나 중단합니다(기존 결과 보호):\n" + ex.Message, true);
        }
    }

    /// <summary>고른 것이 <b>정말 쓸 수 있는가</b> — 지워졌거나 종류가 다르면 이유를 돌려준다.</summary>
    internal static bool CheckGround(AcDoc doc, ObjectId id, out string why)
    {
        why = "";
        if (id.IsNull) { why = "원지반을 고르지 않았습니다."; return false; }
        try
        {
            using var tr = doc.Database.TransactionManager.StartTransaction();
            bool ok = tr.GetObject(id, OpenMode.ForRead) is CivilDb.TinSurface;
            tr.Commit();
            if (!ok) { why = "고른 것이 TIN 지표면이 아닙니다."; return false; }
            return true;
        }
        catch (System.Exception ex) { why = "원지반을 읽지 못했습니다 — " + ex.Message; return false; }
    }
}
