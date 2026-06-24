using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Civil.DatabaseServices;
using DH.Grading.Core;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace DH.Grading.Civil.Commands;

/// <summary>
/// "정지면 생성"(DHGRADE) — 계획 폴리곤 + 원지반 TinSurface를 선택하면
/// 계단식 절성토 비탈면을 distance-field로 계산해 새 TinSurface로 만들고 토공량을 보고한다.
/// </summary>
public sealed class CreateGradingCommand
{
    [CommandMethod("DHGRADE")]
    public void Run()
    {
        Document doc = AcadApp.DocumentManager.MdiActiveDocument;
        if (doc == null) return;
        Editor ed = doc.Editor;
        Database db = doc.Database;

        // 설정은 [정지 설정](DHGRADESET)에서 미리 지정 → 여기선 저장된 값을 그대로 사용(설정창 재표시 안 함).
        bool isWall = GradingSettings.CutSlope <= 1e-6 || GradingSettings.FillSlope <= 1e-6;
        ed.WriteMessage(
            $"\n[정지면 생성] 단높이 {GradingSettings.BenchHeight}m · 소단 {GradingSettings.BenchWidth}m · " +
            $"절토 1:{GradingSettings.CutSlope} · 성토 1:{GradingSettings.FillSlope}{(isWall ? " (수직 옹벽)" : "")}" +
            "  — 값 변경은 [정지 설정]");

        // 1) 계획 폴리곤 선택
        var peoPoly = new PromptEntityOptions("\n계획 경계(닫힌 폴리라인/3D폴리라인/피처라인)를 선택: ");
        peoPoly.SetRejectMessage("\n폴리라인 또는 피처라인이어야 합니다.");
        peoPoly.AddAllowedClass(typeof(Polyline), false);
        peoPoly.AddAllowedClass(typeof(Polyline3d), false);
        peoPoly.AddAllowedClass(typeof(FeatureLine), false);
        var rPoly = ed.GetEntity(peoPoly);
        if (rPoly.Status != PromptStatus.OK) return;

        // 2) 원지반 TinSurface 선택
        var peoSurf = new PromptEntityOptions("\n원지반 표면(TIN Surface)을 선택: ");
        peoSurf.SetRejectMessage("\nTIN Surface여야 합니다.");
        peoSurf.AddAllowedClass(typeof(TinSurface), true);
        var rSurf = ed.GetEntity(peoSurf);
        if (rSurf.Status != PromptStatus.OK) return;

        try
        {
            GradingParams p;
            ObjectId gradeId;
            int lines, dayN;
            System.Collections.Generic.List<Point3> boundary, day;

            // ── Hybrid(NTS Buffer 단 + Ray-casting daylight) 통합 정지면 — 자문답변 #2 채택.
            //    단 모서리=Buffer 오프셋∩daylight 클립 → 브레이크라인(crisp). daylight=ray-cast→Buffer(0).
            //    전환부에서 단이 daylight에 끊겨 벽 없이 0점 수렴. ──
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                boundary = BoundaryReader.Read(tr, rPoly.ObjectId);
                if (boundary.Count < 3)
                {
                    ed.WriteMessage("\n경계 정점이 3개 미만입니다. 닫힌 폴리곤인지 확인하세요.");
                    return;
                }

                var tin = (TinSurface)tr.GetObject(rSurf.ObjectId, OpenMode.ForRead);
                var ground = new Civil3dGroundSurface(tin);
                p = BuildParams(boundary, ground);

                var h = NtsGrading.BuildHybrid(boundary, ground, p);
                if (h.BenchRings.Count == 0)
                {
                    ed.WriteMessage("\n단 모서리가 생성되지 않았습니다. 계획고·원지반/설정을 확인하세요.");
                    return;
                }
                day = h.Daylight;
                dayN = day.Count;

                // [0624-ah: af 복원] 단 모서리 + daylight를 브레이크라인으로, daylight는 Outer 경계로도 사용.
                (gradeId, lines) = SurfaceBuilder.BuildSurfaceFromRings(db, tr, h.BenchRings, day, "정지면_DHGrade");
                tr.Commit();
            }

            // ── 정지경계선(초록) = daylight 외곽선 그림 ──
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                var ms = DrawUtil.ModelSpace(db, tr);
                ObjectId dayLayer = DrawUtil.EnsureLayer(db, tr, "DH-정지경계", 3); // 초록
                DrawUtil.EraseOnLayer(db, tr, "DH-정지경계");
                if (day.Count >= 2)
                    DrawUtil.DrawPolylines(tr, ms, dayLayer, new System.Collections.Generic.List<System.Collections.Generic.List<Point3>> { day });
                tr.Commit();
            }

            string msg =
                $"Hybrid 통합 정지면 생성 완료  [버전 0624-ah·af복원(daylight Breakline+Outer, basePoly합집합 제거)]\n\n" +
                $"· '정지면_DHGrade'(절토·성토 통합): 단 모서리 브레이크라인 {lines:N0}개\n" +
                $"· 정지경계(daylight): {dayN:N0}점 (Breakline+Outer)\n" +
                $"· 단=Buffer∩daylight 클립(crisp·벽없음), 라운드코너 자동, 0.05m 세척\n\n" +
                $"단높이 {p.BenchHeight}m / 소단 {p.BenchWidth}m / 절토 1:{p.CutSlope} / 성토 1:{p.FillSlope} / 단수 N≤{p.MaxBenches}";
            ed.WriteMessage("\n" + msg.Replace("\n\n", "\n"));
            AcadApp.ShowAlertDialog(msg);
        }
        catch (System.Exception ex)
        {
            ed.WriteMessage("\n[DHGRADE 오류] " + ex.Message);
            AcadApp.ShowAlertDialog("정지면 생성 중 오류:\n" + ex.Message);
        }
    }

    /// <summary>여러 daylight 루프 중 면적이 가장 큰 1개만 남긴다(외곽). 내부 봉우리 구멍·잔루프 제외 — 15·17차 방침.</summary>
    private static System.Collections.Generic.List<System.Collections.Generic.List<Point3>> KeepLargestLoop(
        System.Collections.Generic.IReadOnlyList<System.Collections.Generic.List<Point3>> loops)
    {
        var outp = new System.Collections.Generic.List<System.Collections.Generic.List<Point3>>();
        if (loops == null || loops.Count == 0) return outp;
        int best = -1; double bestA = -1;
        for (int i = 0; i < loops.Count; i++)
        {
            var l = loops[i];
            if (l == null || l.Count < 3) continue;
            double a = 0; int n = l.Count;
            for (int j = 0, k = n - 1; j < n; k = j++) a += l[k].X * l[j].Y - l[j].X * l[k].Y;
            a = System.Math.Abs(a) * 0.5;
            if (a > bestA) { bestA = a; best = i; }
        }
        if (best >= 0) outp.Add(loops[best]);
        return outp;
    }

    /// <summary>진단 덤프(개발용) — 입력 경계 좌표와 형상선 통계를 파일로. 실패해도 명령은 계속.</summary>
    internal static void DumpDebug(System.Collections.Generic.List<Point3> boundary, GradingResult result, GradingParams p)
    {
        try
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"# DHGRADE 진단 {System.DateTime.Now:HH:mm:ss}  단높이={p.BenchHeight} 소단={p.BenchWidth} 절토=1:{p.CutSlope} 성토=1:{p.FillSlope} 격자={p.CellSize} 라운드={(!p.MiterConvex)}");
            sb.AppendLine($"## 입력경계 점수={boundary.Count}");
            // 입력경계가 톱니(축정렬 계단)인지: 축정렬 변 비율 + 짧은변 개수
            int axis = 0, shortE = 0; double per = 0;
            for (int i = 0; i < boundary.Count; i++)
            {
                var a = boundary[i]; var b = boundary[(i + 1) % boundary.Count];
                double dx = b.X - a.X, dy = b.Y - a.Y; double L = System.Math.Sqrt(dx * dx + dy * dy); per += L;
                if (L > 1e-9 && (System.Math.Abs(dx) < 0.05 * L || System.Math.Abs(dy) < 0.05 * L)) axis++;
                if (L < 1.0) shortE++;
            }
            sb.AppendLine($"## 둘레={per:F1}m  축정렬변={axis}/{boundary.Count}  1m미만짧은변={shortE}");
            for (int i = 0; i < boundary.Count; i++)
                sb.AppendLine($"V{i}\t{boundary[i].X:F3}\t{boundary[i].Y:F3}\t{boundary[i].Z:F3}");

            sb.AppendLine($"## 형상선 루프수={result.BenchLoops.Count}");
            for (int li = 0; li < result.BenchLoops.Count; li++)
            {
                var loop = result.BenchLoops[li];
                if (loop.Count < 2) { sb.AppendLine($"L{li}\t점={loop.Count}\t(빈약)"); continue; }
                bool closed = System.Math.Abs(loop[0].X - loop[^1].X) < 1e-6 && System.Math.Abs(loop[0].Y - loop[^1].Y) < 1e-6;
                int n = closed ? loop.Count - 1 : loop.Count;
                double maxTurn = 0; int axisSeg = 0; int segc = 0;
                for (int j = 0; j < (closed ? n : n - 1); j++)
                {
                    var b = loop[j]; var c = loop[(j + 1) % loop.Count];
                    double dx = c.X - b.X, dy = c.Y - b.Y; double L = System.Math.Sqrt(dx * dx + dy * dy);
                    if (L < 1e-9) continue; segc++;
                    if (System.Math.Abs(dx) < 0.05 * L || System.Math.Abs(dy) < 0.05 * L) axisSeg++;
                    var a = loop[(j - 1 + loop.Count) % loop.Count];
                    double v1x = b.X - a.X, v1y = b.Y - a.Y, l1 = System.Math.Sqrt(v1x * v1x + v1y * v1y);
                    if (l1 < 1e-9) continue;
                    double cos = (v1x * dx + v1y * dy) / (l1 * L);
                    double turn = System.Math.Acos(System.Math.Clamp(cos, -1, 1)) * 180 / System.Math.PI;
                    if (turn > maxTurn) maxTurn = turn;
                }
                sb.AppendLine($"L{li}\t점={loop.Count}\tz={loop[0].Z:F2}\t닫힘={closed}\t최대꺾임={maxTurn:F0}°\t축정렬세그={axisSeg}/{segc}");
            }
            System.IO.File.WriteAllText(@"C:\Users\user\Desktop\AI\civil3d-grading\dhgrade_debug.txt", sb.ToString());
        }
        catch { /* 진단 실패는 무시 */ }
    }

    /// <summary>설정값을 읽고, 원지반/계획고 표고차로 필요한 최대 단수를 좁혀 마진을 최소화.</summary>
    public static GradingParams BuildParams(System.Collections.Generic.List<Point3> boundary, Civil3dGroundSurface ground)
    {
        double designMin = double.MaxValue, designMax = double.MinValue;
        foreach (var v in boundary) { designMin = System.Math.Min(designMin, v.Z); designMax = System.Math.Max(designMax, v.Z); }

        int maxBenches = GradingSettings.MaxBenches;
        try
        {
            var (gMin, gMax) = ground.ElevationRange();
            double maxDiff = System.Math.Max(System.Math.Abs(gMax - designMin), System.Math.Abs(gMin - designMax));
            // JACK: 원지반 깊이로 단수 N = ceil(Δh/단높이) + 1 ('+1단까지 더한'). 단은 원지반 무시하고 N단까지 침.
            int needed = (int)System.Math.Ceiling(maxDiff / System.Math.Max(GradingSettings.BenchHeight, 1e-6)) + 1;
            maxBenches = System.Math.Min(maxBenches, System.Math.Max(needed, 1));
        }
        catch { /* 표고 범위를 못 얻으면 설정값 그대로 */ }

        return new GradingParams
        {
            BenchHeight = GradingSettings.BenchHeight,
            BenchWidth = GradingSettings.BenchWidth,
            CutSlope = GradingSettings.CutSlope,
            FillSlope = GradingSettings.FillSlope,
            CellSize = GradingSettings.CellSize,
            MaxBenches = maxBenches,
            VertexSpacing = GradingSettings.VertexSpacing,
            MinSlope = GradingSettings.MinSlope,
            MinFaceRun = GradingSettings.MinFaceRun,
            MiterConvex = GradingSettings.MiterConvex,
            MiterLimit = GradingSettings.MiterLimit,
        };
    }
}
