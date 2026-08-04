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

                    // [JACK 0724] 이 방향이 옹벽으로 작성되면(스타일≠사면 + 경사 n≤0.05) 노리선/사면선/소단선 생략 — 옹벽엔 노리선 없음.
                    double slopeN = up ? bundle.Params.CutSlope : bundle.Params.FillSlope;
                    WallStyle style = up ? GradingSettings.CutWallStyle : GradingSettings.FillWallStyle;
                    if (style != WallStyle.없음_사면 && slopeN <= 0.05 + 1e-9)
                    {
                        detail += $"\n{rTag}{label}: 옹벽({style}) — 노리선 생략";
                        continue;
                    }

                    int slN = 0, blN = 0, tN = 0;
                    foreach (var finalRing in ringList)
                    {
                        if (finalRing == null || finalRing.Count < 3) continue;
                        // [구간 구배 0804] 구간 안이라도 그 단 구배가 수직이 아니면 사면 — 노리선·사면선을 정상 생성해야 한다.
                        double bs = System.Math.Max(slopeN, bundle.Params.MinSlope), ms = bundle.Params.MinSlope;
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

            // 팝업은 성패만 — 개수·레이어 등 상세는 명령창과 로그로(공용 배포용, JACK 0720).
            AcadApp.ShowAlertDialog("노리선 생성 완료");
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
        // 게이트 ② 구역별 계획선 fingerprint(정지 후 계획선 변경 감지)
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
                        Refuse(ed, cmdLabel, $"정지 이후 {rTag}계획선이 변경되었습니다.\n" +
                                   $"[정지면 생성](DHGRADE)을 다시 실행한 뒤 {cmdLabel}을(를) 실행하세요.");
                        return null;
                    }
                }
                catch { note += $"\n({rTag}계획선 비교 불가 — 번들 기준으로 진행)"; }
            }
            else note += $"\n({rTag}원본 계획선을 도면에서 찾지 못함 — 번들 기준으로 진행)";
        }
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
