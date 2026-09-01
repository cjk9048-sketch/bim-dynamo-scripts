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
        GradingSettings.SyncToDocument(doc);   // [도면 전환 0803] 도면이 바뀌었으면 그 도면 기준으로 설정·기억 재정렬
        Editor ed = doc.Editor;
        Database db = doc.Database;

        try
        {
            using Transaction tr = db.TransactionManager.StartTransaction();

            // ── 실행 게이트 3중(유령선 차단) — DHINFRA와 공용 ──
            var regions = PassGates(db, tr, ed, "노리선", out string note);
            if (regions == null) return;

            // ── 링 복원(결정적 재계산 — ground 불필요, NullGround 주입) + 작도 ──
            var ng = new NullGround();
            var ticks = new System.Collections.Generic.List<(Point3 A, Point3 B)>();
            var cornerTicks = new System.Collections.Generic.List<(Point3 A, Point3 B)>(); // 볼록 코너 대각선(우선 보존)
            // [§75 0728] 옹벽 구간의 계단 상단선 = 옹벽선(두꺼운 빨강) — 노리선/사면선/소단선은 구간에서 제외.
            var wallLines = new System.Collections.Generic.List<System.Collections.Generic.List<Point3>>();
            var transCrest = new System.Collections.Generic.List<System.Collections.Generic.List<Point3>>();
            var transToe = new System.Collections.Generic.List<System.Collections.Generic.List<Point3>>();
            string detail = "";
            bool firstEdgeDraw = true;
            // [FGL 표기 — JACK 0729] 구역(계획 부지)마다 중앙에 FGL 심볼+계획고 텍스트.
            var fglMarks = new System.Collections.Generic.List<(double X, double Y, double Z)>();

            // [번들 v2 — 다중 절/성토 영역] 링 '리스트' 전체를 순회 — 2개+ 영역에서 작은 영역 누락되던 버그 수정(JACK).
            static System.Collections.Generic.List<System.Collections.Generic.List<Point3>>? RingsOf(
                System.Collections.Generic.List<System.Collections.Generic.List<Point3>>? many,
                System.Collections.Generic.List<Point3>? one)
                => many ?? (one != null ? new() { one } : null);

            // [다중 구역 0729] 구역(누적 정지) 전체 순회 — 구역마다 자기 boundary·params·zones로 재계산.
            for (int ri = 0; ri < regions.Count; ri++)
            {
                var bundle = regions[ri];
                string rTag = regions.Count > 1 ? $"[구역{ri + 1}] " : "";
                // [다중 구역 0804] 뒤 구역이 덮어쓴 영역 — 이 구역의 노리선·사면선은 거기서 빼야 최종 지표면과 맞는다.
                var later = GradingBundle.LaterFootprints(regions, ri);
                if (later.Count > 0) detail += $"\n{rTag}뒤 구역이 덮은 영역 {later.Count}개 제외";
                // [§75 1-A] 사면선/소단선 태그 작도 — 구역별로 호출(첫 구역만 레이어 청소, planHandle로 구역 식별).
                var cutEdges = new System.Collections.Generic.List<(bool, int, int, System.Collections.Generic.List<Point3>)>();
                var fillEdges = new System.Collections.Generic.List<(bool, int, int, System.Collections.Generic.List<Point3>)>();
                System.Collections.Generic.List<(System.Collections.Generic.List<Point3> Crest, System.Collections.Generic.List<Point3> Toe)>? transFaces = null;

                foreach (var (up, label, hasSlope, ringList) in new[]
                {
                    (true, "절토", bundle.CutHasSlope, RingsOf(bundle.CutFinalRings, bundle.CutFinalRing)),
                    (false, "성토", bundle.FillHasSlope, RingsOf(bundle.FillFinalRings, bundle.FillFinalRing)),
                })
                {
                    if (!hasSlope) { detail += $"\n{rTag}{label}: 사면 없음"; continue; }
                    if (ringList == null || ringList.Count == 0)
                    {
                        detail += $"\n{rTag}{label}: 최종 경계 없음 — 생략(DHGRADE에서 경계 주입이 실패했는지 확인)";
                        continue;
                    }
                    // [§75 0728] 적용된 옹벽 구간은 번들(v3+)에서 — 선택(WallPicks)은 1회성이라 여기 의존하면 안 됨.
                    var zones = up ? bundle.CutWallZones : bundle.FillWallZones;
                    var vs = GradingGeometry.Build(bundle.Boundary, ng, bundle.Params, up, zones);
                    transFaces ??= vs.TransitionFaces; // 전환사면은 경계에서만 유도 — 절/성 동일, 구역당 한 번
                    if (!vs.HasSlope) { detail += $"\n{rTag}{label}: 링 복원 결과 사면 없음"; continue; }

                    // ★★★[JACK 0901 "처음 정지설정에서 만들었든 <b>사면변환으로 만들었든</b>
                    //   사면이면 노리선은 나와야지"] — <b>맞다. 여기가 틀렸다.</b>
                    //
                    //   종전엔 <b>정지설정의 전역 스타일</b>만 보고 그 방향을 <b>통째로 건너뛰었다</b>.
                    //   그래서 절토·성토를 옹벽으로 잡아 두고 [사면 변환]으로 일부를 사면으로 돌려 놔도
                    //   <b>노리선이 한 개도 안 나왔다</b> — 화면에는 "완료"만 뜨니 고장으로 보인다.
                    //
                    //   ★<b>구간별 판정은 아래 생성기가 이미 한다.</b> <c>zones</c>(번들에 적힌 실제 옹벽 구간)와
                    //   구배를 넘겨받아 옹벽 자리는 빼고 사면 자리만 그린다 — 바로 아래 0804 주석이 그 얘기다.
                    //   그러니 여기서 미리 자르면 안 된다. <b>세는 것은 생성기에 맡기고, 우리는 적기만 한다.</b>
                    double slopeN = up ? bundle.Params.CutSlope : bundle.Params.FillSlope;
                    WallStyle style = up ? GradingSettings.CutWallStyle : GradingSettings.FillWallStyle;
                    bool wallDefault = style != WallStyle.없음_사면 && slopeN <= GradingSettings.WallGateSlope + 1e-9;
                    if (wallDefault)
                        detail += $"\n{rTag}{label}: 기본 옹벽({style}) — 사면으로 바꾼 구간만 노리선";

                    int slN = 0, blN = 0, tN = 0;
                    foreach (var finalRing in ringList)
                    {
                        if (finalRing == null || finalRing.Count < 3) continue;
                        // [구간 구배 0804] 구간 안이라도 그 단 구배가 수직이 아니면 사면 — 노리선·사면선을 정상 생성해야 한다.
                        double bs = System.Math.Max(slopeN, bundle.Params.MinSlope), ms = bundle.Params.WallGateSlope;
                        var (t, ct, _) = SlopeHatchGenerator.Generate(vs.Rings, ng, up,
                            GradingSettings.HatchShort, GradingSettings.HatchLong, finalRing, bundle.Boundary,
                            zones, bundle.Boundary, bs, ms, later);
                        var edges = SlopeHatchGenerator.GenerateEdgeLinesTagged(vs.Rings, ng, up, finalRing, bundle.Boundary,
                            zones, bundle.Boundary, wallLines, null, bs, ms, later);
                        ticks.AddRange(t);
                        cornerTicks.AddRange(ct);
                        if (up) cutEdges.AddRange(edges); else fillEdges.AddRange(edges);
                        foreach (var e in edges) { if (e.IsSlope) slN++; else blN++; }
                        tN += t.Count;
                    }
                    detail += $"\n{rTag}{label}: 영역 {ringList.Count} · 사면선 {slN} · 소단선 {blN} · 노리선 {tN}";
                }

                // 내부 단차 전환사면(Phase F) — 클립 = 그 구역 계획폴리곤 자체(부지 안 띠)
                if (transFaces != null && transFaces.Count > 0)
                {
                    var (tt, tct, tc, tto) = SlopeHatchGenerator.GenerateTransitionHatch(
                        transFaces, GradingSettings.HatchShort, GradingSettings.HatchLong, bundle.Boundary);
                    ticks.AddRange(tt);
                    cornerTicks.AddRange(tct);
                    transCrest.AddRange(tc);
                    transToe.AddRange(tto);
                    detail += $"\n{rTag}전환사면(내부 단차): 면 {transFaces.Count} · 노리선 {tt.Count}";
                }

                GradingBuilder.DrawSlopeEdgesTagged(db, tr, cutEdges, fillEdges,
                    bundle.PlanHandle, clearFirst: firstEdgeDraw);
                firstEdgeDraw = false;

                // [FGL 표기 — 플래토별(JACK 0729)] 단차 계획선은 같은 계획고 구간(플래토)마다 1개씩,
                //   평지 계획선은 1개. 위치는 구간 중심을 부지 내부점 방향으로 끌어들여 폴리곤 '안'을 보장.
                try
                {
                    var pg = NtsSupport.ToCleanPolygon(bundle.Boundary);
                    double ipx = 0, ipy = 0;
                    if (pg != null) { var ip0 = pg.InteriorPoint; ipx = ip0.X; ipy = ip0.Y; }
                    else
                    {
                        foreach (var bp in bundle.Boundary) { ipx += bp.X; ipy += bp.Y; }
                        ipx /= System.Math.Max(bundle.Boundary.Count, 1);
                        ipy /= System.Math.Max(bundle.Boundary.Count, 1);
                    }
                    var gfp = NtsSupport.Factory();
                    foreach (var (cx, cy, z) in PlateauMarks(bundle.Boundary))
                    {
                        double mx = cx, my = cy;
                        if (pg != null && !pg.Contains(gfp.CreatePoint(new NetTopologySuite.Geometries.Coordinate(cx, cy))))
                        {
                            // 구간 중심이 폴리곤 밖(경계 위/오목형)이면 내부점 방향으로 옮겨 첫 '안' 지점 사용.
                            mx = ipx; my = ipy;
                            for (double t = 0.15; t <= 0.95; t += 0.1)
                            {
                                double qx = cx + (ipx - cx) * t, qy = cy + (ipy - cy) * t;
                                if (pg.Contains(gfp.CreatePoint(new NetTopologySuite.Geometries.Coordinate(qx, qy))))
                                { mx = qx; my = qy; break; }
                            }
                        }
                        fglMarks.Add((mx, my, z));
                    }
                }
                catch { }
            }

            // [겹침 제거 — JACK 0727] 코너·급커브 격자 겹침을 실제 2D 교차 판정으로 정리(생성은 최대, 겹치는 것만 제거).
            //   볼록 코너 대각선(cornerTicks)은 우선 보존 — 겹치면 주변 수직틱이 대신 빠진다.
            int rawTicks = ticks.Count + cornerTicks.Count;
            ticks = SlopeHatchGenerator.RemoveOverlaps(cornerTicks, ticks);
            detail += $"\n겹침 제거: 노리선 {rawTicks} → {ticks.Count} (볼록코너 {cornerTicks.Count})";

            GradingBuilder.DrawWallLines(db, tr, wallLines); // [§75] 옹벽 구간 = 두꺼운 빨간 옹벽선만
            GradingBuilder.DrawFglMarkers(db, tr, fglMarks); // [FGL 표기 — JACK 0729] 구역별 중앙 심볼+계획고
            if (fglMarks.Count > 0) detail += $"\nFGL 표기: {fglMarks.Count}건";
            if (wallLines.Count > 0) detail += $"\n옹벽선(두꺼운 빨강): {wallLines.Count}";
            GradingBuilder.DrawTransitionEdges(db, tr, transCrest, transToe);
            // 틱은 기존 노랑 레이어 재사용. 구 흰색 'DH-소단' 잔재는 빈 목록으로 청소(사면선/소단선 레이어로 대체).
            GradingBuilder.DrawSlopeHatch(db, tr, ticks,
                System.Array.Empty<System.Collections.Generic.IReadOnlyList<Point3>>());
            tr.Commit();

            // ★★★[JACK 0901 "노리선 작성 기능이 안 돼"] — <b>안 된 게 아니라 그릴 것이 없었다.</b>
            //   정지옵션에서 절토·성토가 <b>둘 다 옹벽</b>이면 노리선을 그릴 <b>사면이 없다</b>.
            //   그런데 팝업이 "노리선 생성 완료" 한 줄뿐이라 <b>0개를 그려 놓고 완료</b>라고 했다 —
            //   사용자 눈에는 고장이다. 사유는 로그에만 있었다.
            //   → <b>숫자를 팝업에 싣고</b>, 아무것도 안 그렸으면 왜인지 말한다.
            int drawn = ticks.Count + wallLines.Count;
            if (drawn == 0)
            {
                string why = detail.Contains("기본 옹벽(")
                    ? "절토·성토가 모두 옹벽이고, 사면으로 바꾼 구간이 없습니다."
                      + "\n[사면 변환]으로 일부를 사면으로 돌리면 그 구간에 노리선이 나옵니다."
                    : "그릴 사면을 찾지 못했습니다 — [계획부지 생성]을 먼저 돌렸는지 확인해 주세요.";
                AcadApp.ShowAlertDialog("노리선 — 그린 것이 없습니다\n\n" + why
                                      + "\n\n자세한 사유:" + detail.Replace("\n\n", "\n"));
            }
            else
            {
                AcadApp.ShowAlertDialog($"노리선 생성 완료\n\n노리선 {ticks.Count}개"
                                      + (wallLines.Count > 0 ? $" · 옹벽선 {wallLines.Count}개" : "")
                                      + (fglMarks.Count > 0 ? $" · FGL 표기 {fglMarks.Count}건" : ""));
            }
            ed.WriteMessage("\n" + ("노리선 생성 완료" + note + detail +
                $"\n레이어: DH-사면선-절토/성토 · DH-소단선-절토/성토 · DH-노리선(노랑)" +
                $"\n긴선 {GradingSettings.HatchLong}m마다 · 짧은선 {GradingSettings.HatchShort}m마다(절반)").Replace("\n\n", "\n"));
            try
            {
                DiagLog.Append(
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

    /// <summary>실행 게이트 3중(ralplan) — ①번들 존재 ②계획선 fingerprint 일치(구역별) ③정지 표면 존재.
    /// 하나라도 실패하면 안내 팝업 후 null(작도/내보내기 금지 — 유령선 차단). DHNORI/DHINFRA 공용.
    /// [다중 구역 0729] 구역 목록을 반환(v3 번들은 1개짜리 목록).</summary>
    /// <summary>★★[v30.2 · JACK 0812] <b>번들만 있으면 사면선·소단선·데이라잇을 도면과 무관하게 복원한다.</b>
    ///
    /// <para>JACK: <i>"우리 애드인의 핵심은 편의성이야. 어느 순간엔 뭐 해야 하고 하는 식이면
    /// 제약이 생기고 범용성이 떨어져."</i> — 맞는 말이다.
    /// 그래서 <b>"종단도 전에 노리선을 먼저 돌리세요"는 없앤다.</b></para>
    ///
    /// <para>노리선이 하는 일의 본질은 <b>번들에서 결정적으로 다시 계산</b>하는 것이다
    /// (지반이 필요 없어 <see cref="NullGround"/>를 넣는다). 그리기와 계산을 갈라 놓으면
    /// <b>종단도가 자기가 필요한 선을 직접 복원</b>할 수 있고, 실행 순서에 매이지 않는다.</para>
    ///
    /// <para>누적 구역도 그대로 처리한다 — 구역마다 <b>뒤 구역이 덮은 자리</b>를 빼므로
    /// 결과는 지금 정지면과 맞는다.</para>
    /// 반환=선 목록(사면선·소단선·전환사면 모서리). <paramref name="diag"/>=사람이 읽을 요약.</summary>
    /// <param name="wallOut">★[JACK 0825] 주면 <b>옹벽선을 여기로만</b> 내보낸다(반환 목록에는 안 담는다).
    /// 키는 <b>구역·절성·링·단</b>이라 같은 키의 윗선·아랫선이 <b>한 벽</b>이다 — 측점에서 그 둘을
    /// 가운데 하나로 접는 데 쓴다. 소단은 단 번호가 달라 섞이지 않는다.</param>
    internal static System.Collections.Generic.List<System.Collections.Generic.List<Point3>> RebuildEdgeLines(
        System.Collections.Generic.IReadOnlyList<GradingBundle> regions, out string diag,
        System.Collections.Generic.List<((int Region, bool Up, int Ring, int Bench) Key, bool IsCrest,
                                         System.Collections.Generic.List<Point3> Pts, double Slope)> wallOut = null)
    {
        var res = new System.Collections.Generic.List<System.Collections.Generic.List<Point3>>();
        var sb = new System.Text.StringBuilder();
        if (regions == null || regions.Count == 0) { diag = "번들 없음"; return res; }
        var ng = new NullGround();

        for (int ri = 0; ri < regions.Count; ri++)
        {
            var b = regions[ri];
            if (b == null) continue;
            string rTag = regions.Count > 1 ? $"구역{ri + 1}" : "구역";
            var later = GradingBundle.LaterFootprints(regions, ri);
            int slN = 0, blN = 0, wlN = 0;

            foreach (var (up, label, hasSlope, many, one) in new[]
            {
                (true,  "절토", b.CutHasSlope,  b.CutFinalRings,  b.CutFinalRing),
                (false, "성토", b.FillHasSlope, b.FillFinalRings, b.FillFinalRing),
            })
            {
                if (!hasSlope) continue;
                var ringList = many ?? (one != null
                    ? new System.Collections.Generic.List<System.Collections.Generic.List<Point3>> { one } : null);
                if (ringList == null || ringList.Count == 0) { sb.Append($" {rTag}/{label}:링없음"); continue; }
                var zones = up ? b.CutWallZones : b.FillWallZones;
                try
                {
                    var vs = GradingGeometry.Build(b.Boundary, ng, b.Params, up, zones);
                    if (!vs.HasSlope) { sb.Append($" {rTag}/{label}:복원실패"); continue; }
                    double slopeN = up ? b.Params.CutSlope : b.Params.FillSlope;
                    // ★[JACK 0825] 게이트(ms)와 <b>벽의 실제 구배</b>(realN)를 가른다.
                    //   종전엔 한 변수를 선 분류와 VertBar 양쪽에 썼다 — 게이트로 이관하는 순간
                    //   종단 막대의 tol이 실제 두께가 아니라 판정 문턱(0.05)을 재게 되어,
                    //   벽과 무관한 측점까지 벽 자리로 끌어온다.
                    double bs = System.Math.Max(slopeN, b.Params.MinSlope), ms = b.Params.WallGateSlope;
                    double realN = System.Math.Max(System.Math.Min(slopeN, b.Params.WallGateSlope), b.Params.MinSlope);
                    for (int ringIdx = 0; ringIdx < ringList.Count; ringIdx++)
                    {
                        var finalRing = ringList[ringIdx];
                        if (finalRing == null || finalRing.Count < 3) continue;
                        // ★★[JACK 0824 '계획지표면 꺾이는 부분 측점이 자동 추가가 안 돼'] 옹벽 구간의 선도 받는다.
                        //   이 함수의 결과는 **측점 재료로만** 쓰인다(측점·종단 두 곳뿐 — 도면에 그리지 않는다).
                        //   옹벽의 윗선·아랫선은 계획 지표면이 실제로 꺾이는 자리라 측점이 서야 하는데,
                        //   종전엔 '구간 안이면 그리지 않는다'는 표시 규칙에 걸려 **측점에서도 사라졌다**.
                        var wallPts = new System.Collections.Generic.List<(int Bench, bool IsCrest,
                                          System.Collections.Generic.List<Point3> Pts)>();
                        var edges = SlopeHatchGenerator.GenerateEdgeLinesTagged(
                            vs.Rings, ng, up, finalRing, b.Boundary, zones, b.Boundary,
                            null, null, bs, ms, later, wallPts);
                        foreach (var e in edges)
                        {
                            if (e.Pts == null || e.Pts.Count < 2) continue;
                            res.Add(e.Pts);
                            if (e.IsSlope) slN++; else blN++;
                        }
                        foreach (var w in wallPts)
                        {
                            if (w.Pts == null || w.Pts.Count < 2) continue;
                            // ★[JACK 0825] 옹벽선을 받겠다는 곳이 있으면 <b>그쪽으로만</b> 보낸다.
                            //   양쪽에 다 넣으면 접힌 측점과 안 접힌 측점이 둘 다 서서 도로 두 개가 된다.
                            // 구배(ms)를 함께 싣는다 — 종단 막대의 <b>폭</b>이 여기서 나온다(폭 = 구배 × 벽 높이).
                            if (wallOut != null) wallOut.Add(((ri, up, ringIdx, w.Bench), w.IsCrest, w.Pts, realN));
                            else res.Add(w.Pts);
                            wlN++;
                        }
                    }
                }
                catch (System.Exception ex) { sb.Append($" {rTag}/{label}:예외({ex.GetType().Name})"); }
            }
            sb.Append($" {rTag}:사면{slN}/소단{blN}/옹벽{wlN}");
        }
        diag = $"번들에서 복원 — 구역 {regions.Count}개 · 선 {res.Count}개 ·{sb}";
        return res;
    }

    internal static System.Collections.Generic.List<GradingBundle>? PassGates(
        Database db, Transaction tr, Editor ed, string cmdLabel, out string note)
    {
        note = "";
        // 게이트 ① 번들
        var regions = GradingBundleStore.TryLoadAll(db, tr, out string reason);
        if (regions == null || regions.Count == 0)
        {
            Refuse(ed, cmdLabel, $"{cmdLabel}을(를) 실행할 수 없습니다.\n{reason}\n\n[정지면 생성](DHGRADE)을 먼저 실행하세요.");
            return null;
        }
        // ★★[v30.1 · JACK 0812] <b>게이트 ②는 거부가 아니라 알림이다.</b>
        //
        //   JACK: <i>"노리선 기능은 언제든 누르면 현재 기준 원지반하고 다르다면 해당 구역은 다 나와야 해."</i>
        //
        //   종전엔 구역 <b>하나라도</b> 계획선이 바뀌어 있으면 <b>명령 전체를 거부</b>했다.
        //   멀쩡한 구역까지 하나도 안 그려졌다 — 누적 구역이 많을수록 걸릴 확률이 커진다.
        //
        //   그리고 거부할 이유가 없다. <b>계획선을 나중에 고쳐도 정지면은 안 바뀐다</b>(DHGRADE를
        //   다시 돌려야 바뀐다). 그러니 <b>번들 기준으로 그린 선이 곧 지금 지표면과 맞는 선</b>이다.
        //   계획선은 그때 쓰인 입력일 뿐, 지금 도면의 정본이 아니다.
        //   → <b>바뀐 사실은 분명히 알리되 그린다.</b> 유령선은 게이트 ③(표면 존재)이 막는다.
        int changed = 0;
        for (int ri = 0; ri < regions.Count; ri++)
        {
            var bundle = regions[ri];
            string rTag = regions.Count > 1 ? $"구역{ri + 1} " : "";
            var planId = FindByHandle(db, bundle.PlanHandle);
            if (!planId.IsNull)
            {
                try
                {
                    var cur = BoundaryReader.Read(tr, planId);
                    if (cur.Count >= 3 && !bundle.FingerprintMatches(cur))
                    {
                        changed++;
                        note += $"\n(⚠{rTag}계획선이 정지 이후 바뀌었다 — 정지면은 그대로이므로 <b>번들 기준</b>으로 그린다. " +
                                "고친 계획선을 반영하려면 [정지면 생성]을 다시 실행할 것)";
                    }
                }
                catch { note += $"\n({rTag}계획선 비교 불가 — 번들 기준으로 진행)"; }
            }
            else note += $"\n({rTag}원본 계획선을 도면에서 찾지 못함 — 번들 기준으로 진행)";
        }
        if (changed > 0)
            ed.WriteMessage($"\n  · ⚠계획선이 바뀐 구역 {changed}개 — 번들 기준으로 그립니다(정지면과 일치)");
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
        return regions;
    }

    /// <summary>[FGL 플래토 — JACK 0729] 경계 정점을 같은 Z(±1cm) '원형 연속 구간'으로 묶어
    /// (구간 정점 평균 XY, 그 Z)를 반환. 정점 2개 이상 구간만(단차 전이 단독 정점 제외).
    /// 전부 같은 Z(평지)=1개, 같은 Z 구간이 없으면(경사 계획선) 전체 평균 1개 폴백.</summary>
    internal static System.Collections.Generic.List<(double X, double Y, double Z)> PlateauMarks(
        System.Collections.Generic.List<Point3> b)
    {
        var outp = new System.Collections.Generic.List<(double, double, double)>();
        if (b == null || b.Count < 3) return outp;
        int n = b.Count;
        if (System.Math.Abs(b[0].X - b[n - 1].X) < 1e-9 && System.Math.Abs(b[0].Y - b[n - 1].Y) < 1e-9) n--;
        if (n < 3) return outp;
        const double zTol = 0.01;

        void Flush(System.Collections.Generic.List<Point3> run)
        {
            if (run.Count < 2) return;
            double ax = 0, ay = 0;
            foreach (var q in run) { ax += q.X; ay += q.Y; }
            outp.Add((ax / run.Count, ay / run.Count, run[0].Z));
        }

        int start = -1;
        for (int i = 0; i < n; i++)
            if (System.Math.Abs(b[i].Z - b[(i - 1 + n) % n].Z) > zTol) { start = i; break; }
        if (start < 0)
        {
            // 평지 — 전체가 한 플래토.
            double ax = 0, ay = 0;
            for (int i = 0; i < n; i++) { ax += b[i].X; ay += b[i].Y; }
            outp.Add((ax / n, ay / n, b[0].Z));
            return outp;
        }
        var cur = new System.Collections.Generic.List<Point3> { b[start] };
        for (int s = 1; s < n; s++)
        {
            int i = (start + s) % n;
            if (System.Math.Abs(b[i].Z - cur[cur.Count - 1].Z) <= zTol) cur.Add(b[i]);
            else { Flush(cur); cur = new System.Collections.Generic.List<Point3> { b[i] }; }
        }
        Flush(cur);
        if (outp.Count == 0)
        {
            // 연속 경사 계획선 등 — 평균 1개 폴백.
            double ax = 0, ay = 0, az = 0;
            for (int i = 0; i < n; i++) { ax += b[i].X; ay += b[i].Y; az += b[i].Z; }
            outp.Add((ax / n, ay / n, az / n));
        }
        return outp;
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
