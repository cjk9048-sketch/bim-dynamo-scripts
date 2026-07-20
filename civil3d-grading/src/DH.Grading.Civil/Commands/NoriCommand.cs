using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using DH.Grading.Core;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace DH.Grading.Civil.Commands;

/// <summary>
/// "노리선" 버튼(DHNORI) — DHGRADE가 저장한 번들을 읽어 재선택 없이 한 번에 작도(ralplan Phase A):
///   · 사면선(3D폴리선): DH-사면선-절토(250)/-성토(8) — 사면 상단(crest) 모서리
///   · 소단선(3D폴리선): DH-소단선-절토(1)/-성토(30) — 사면 하단(toe) 모서리
///   · 노리선 틱: DH-노리선(노랑) — 5m 긴선(사면 전폭)/1m 짧은선(절반)
/// 전부 최종 경계(finalRing) − 계획폴리곤 도넛으로 클립 — 정지면_DH와 일치, 경계에서 정확 절단.
/// 실행 게이트(유령선 차단): ①번들 존재 ②계획선 fingerprint 일치 ③정지면 존재 — 실패 시 작도 없이 안내.
/// </summary>
public sealed class NoriCommand
{
    [CommandMethod("DHNORI")]
    public void Run()
    {
        Document doc = AcadApp.DocumentManager.MdiActiveDocument;
        if (doc == null) return;
        Editor ed = doc.Editor;
        Database db = doc.Database;

        try
        {
            using Transaction tr = db.TransactionManager.StartTransaction();

            // ── 실행 게이트 3중(유령선 차단) — DHINFRA와 공용 ──
            var bundle = PassGates(db, tr, ed, "노리선", out string note);
            if (bundle == null) return;

            // ── 링 복원(결정적 재계산 — ground 불필요, NullGround 주입) + 작도 ──
            var ng = new NullGround();
            var ticks = new System.Collections.Generic.List<(Point3 A, Point3 B)>();
            var cutSlope = new System.Collections.Generic.List<System.Collections.Generic.List<Point3>>();
            var cutBerm = new System.Collections.Generic.List<System.Collections.Generic.List<Point3>>();
            var fillSlope = new System.Collections.Generic.List<System.Collections.Generic.List<Point3>>();
            var fillBerm = new System.Collections.Generic.List<System.Collections.Generic.List<Point3>>();
            System.Collections.Generic.List<(System.Collections.Generic.List<Point3> Crest, System.Collections.Generic.List<Point3> Toe)>? transFaces = null;
            string detail = "";

            // [번들 v2 — 다중 절/성토 영역] 링 '리스트' 전체를 순회 — 2개+ 영역에서 작은 영역 누락되던 버그 수정(JACK).
            static System.Collections.Generic.List<System.Collections.Generic.List<Point3>>? RingsOf(
                System.Collections.Generic.List<System.Collections.Generic.List<Point3>>? many,
                System.Collections.Generic.List<Point3>? one)
                => many ?? (one != null ? new() { one } : null);

            foreach (var (up, label, hasSlope, ringList) in new[]
            {
                (true, "절토", bundle.CutHasSlope, RingsOf(bundle.CutFinalRings, bundle.CutFinalRing)),
                (false, "성토", bundle.FillHasSlope, RingsOf(bundle.FillFinalRings, bundle.FillFinalRing)),
            })
            {
                if (!hasSlope) { detail += $"\n{label}: 사면 없음"; continue; }
                if (ringList == null || ringList.Count == 0)
                {
                    detail += $"\n{label}: 최종 경계 없음 — 생략(DHGRADE에서 경계 주입이 실패했는지 확인)";
                    continue;
                }
                var vs = GradingGeometry.Build(bundle.Boundary, ng, bundle.Params, up);
                transFaces ??= vs.TransitionFaces; // 전환사면은 경계에서만 유도 — 절/성 동일, 한 번만
                if (!vs.HasSlope) { detail += $"\n{label}: 링 복원 결과 사면 없음"; continue; }

                int slN = 0, blN = 0, tN = 0;
                foreach (var finalRing in ringList)
                {
                    if (finalRing == null || finalRing.Count < 3) continue;
                    var (t, _) = SlopeHatchGenerator.Generate(vs.Rings, ng, up,
                        GradingSettings.HatchShort, GradingSettings.HatchLong, finalRing, bundle.Boundary);
                    var (sl, bl) = SlopeHatchGenerator.GenerateEdgeLines(vs.Rings, ng, up, finalRing, bundle.Boundary);
                    ticks.AddRange(t);
                    if (up) { cutSlope.AddRange(sl); cutBerm.AddRange(bl); }
                    else { fillSlope.AddRange(sl); fillBerm.AddRange(bl); }
                    slN += sl.Count; blN += bl.Count; tN += t.Count;
                }
                detail += $"\n{label}: 영역 {ringList.Count} · 사면선 {slN} · 소단선 {blN} · 노리선 {tN}";
            }

            // 내부 단차 전환사면(Phase F) — 클립 = 계획폴리곤 자체(부지 안 띠)
            var transCrest = new System.Collections.Generic.List<System.Collections.Generic.List<Point3>>();
            var transToe = new System.Collections.Generic.List<System.Collections.Generic.List<Point3>>();
            if (transFaces != null && transFaces.Count > 0)
            {
                var (tt, tc, tto) = SlopeHatchGenerator.GenerateTransitionHatch(
                    transFaces, GradingSettings.HatchShort, GradingSettings.HatchLong, bundle.Boundary);
                ticks.AddRange(tt);
                transCrest.AddRange(tc);
                transToe.AddRange(tto);
                detail += $"\n전환사면(내부 단차): 면 {transFaces.Count} · 노리선 {tt.Count}";
            }

            GradingBuilder.DrawSlopeEdges(db, tr, cutSlope, cutBerm, fillSlope, fillBerm);
            GradingBuilder.DrawTransitionEdges(db, tr, transCrest, transToe);
            // 틱은 기존 노랑 레이어 재사용. 구 흰색 'DH-소단' 잔재는 빈 목록으로 청소(사면선/소단선 레이어로 대체).
            GradingBuilder.DrawSlopeHatch(db, tr, ticks,
                System.Array.Empty<System.Collections.Generic.IReadOnlyList<Point3>>());
            tr.Commit();

            // 팝업은 성패만 — 개수·레이어 등 상세는 명령창과 로그로(공용 배포용, JACK 0720).
            AcadApp.ShowAlertDialog("노리선 생성 완료");
            ed.WriteMessage("\n" + ("노리선 생성 완료" + note + detail +
                $"\n레이어: DH-사면선-절토/성토 · DH-소단선-절토/성토 · DH-노리선(노랑)" +
                $"\n긴선 {GradingSettings.HatchLong}m마다 · 짧은선 {GradingSettings.HatchShort}m마다(절반)").Replace("\n\n", "\n"));
            try
            {
                System.IO.File.AppendAllText(@"C:\Users\user\Desktop\AI\civil3d-grading\DHGRADE_진단.log",
                    "\n■ DHNORI(노리선 버튼)" + note + detail + "\n");
            }
            catch { }
        }
        catch (System.Exception ex)
        {
            ed.WriteMessage("\n[DHNORI 오류] " + ex.Message);
            AcadApp.ShowAlertDialog("노리선 생성 중 오류:\n" + ex.Message);
        }
    }

    /// <summary>실행 게이트 3중(ralplan) — ①번들 존재 ②계획선 fingerprint 일치 ③정지 표면 존재.
    /// 하나라도 실패하면 안내 팝업 후 null(작도/내보내기 금지 — 유령선 차단). DHNORI/DHINFRA 공용.</summary>
    internal static GradingBundle? PassGates(Database db, Transaction tr, Editor ed, string cmdLabel, out string note)
    {
        note = "";
        // 게이트 ① 번들
        var bundle = GradingBundleStore.TryLoad(db, tr, out string reason);
        if (bundle == null)
        {
            Refuse(ed, cmdLabel, $"{cmdLabel}을(를) 실행할 수 없습니다.\n{reason}\n\n[정지면 생성](DHGRADE)을 먼저 실행하세요.");
            return null;
        }
        // 게이트 ② 계획선 fingerprint(정지 후 계획선 변경 감지)
        var planId = FindByHandle(db, bundle.PlanHandle);
        if (!planId.IsNull)
        {
            try
            {
                var cur = BoundaryReader.Read(tr, planId);
                if (cur.Count >= 3 && !bundle.FingerprintMatches(cur))
                {
                    Refuse(ed, cmdLabel, "정지 이후 계획선이 변경되었습니다.\n" +
                               $"[정지면 생성](DHGRADE)을 다시 실행한 뒤 {cmdLabel}을(를) 실행하세요.");
                    return null;
                }
            }
            catch { note += "\n(계획선 비교 불가 — 번들 기준으로 진행)"; }
        }
        else note += "\n(원본 계획선을 도면에서 찾지 못함 — 번들 기준으로 진행)";
        // 게이트 ③ 정지 표면 존재(표면이 지워졌으면 유령선 방지 위해 중단)
        bool surfOk = GradingBuilder.SurfaceExistsByBaseName(tr, "정지면_DH")
                   || GradingBuilder.SurfaceExistsByBaseName(tr, "가상절토_DH")
                   || GradingBuilder.SurfaceExistsByBaseName(tr, "가상성토_DH");
        if (!surfOk)
        {
            Refuse(ed, cmdLabel, "정지 표면(정지면_DH)이 도면에 없습니다.\n" +
                       "[정지면 생성](DHGRADE)을 먼저 실행하세요.");
            return null;
        }
        return bundle;
    }

    private static void Refuse(Editor ed, string label, string msg)
    {
        ed.WriteMessage($"\n[{label}] " + msg.Replace("\n", " "));
        AcadApp.ShowAlertDialog(msg);
    }

    /// <summary>저장된 핸들 문자열로 ObjectId 찾기 — 없거나 지워졌으면 Null.</summary>
    internal static ObjectId FindByHandle(Database db, string handleHex)
    {
        if (string.IsNullOrEmpty(handleHex)) return ObjectId.Null;
        try
        {
            long v = System.Convert.ToInt64(handleHex, 16);
            if (db.TryGetObjectId(new Handle(v), out ObjectId id) && !id.IsErased) return id;
        }
        catch { }
        return ObjectId.Null;
    }
}
