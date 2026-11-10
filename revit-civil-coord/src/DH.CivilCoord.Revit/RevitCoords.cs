using Autodesk.Revit.DB;
using DH.CivilCoord.Core;

namespace DH.CivilCoord.Revit;

/// <summary>
/// «레벨 높이 그대로»(JACK 1110): Revit 레벨에 보이는 높이(EL 50.00 → 50.00 m)가 Civil 높이가 되도록, 내부 좌표 Z 에 더할 값(m)을 잰다.
/// <para>보이는 높이 = Level.Elevation(레벨 유형의 «높이 기준» — 프로젝트 기준점 · 측량점 — 을 따른다) ·
/// 내부 Z = 그 레벨로 만든 작업 기준면의 Z(되돌리는 트랜잭션 안에서 재고 버림 — 짐작 말고 잼). 재기가 안 되면 Level.ProjectElevation.</para>
/// </summary>
internal sealed class LevelHeights
{
    public const double Ft = 0.3048;

    /// <summary>보이는 레벨 높이(m) = 내부 Z(m) + OffsetM.</summary>
    public double OffsetM { get; private set; }
    /// <summary>창에 보일 한 줄: «EL 50.00» → 50.000 m.</summary>
    public string Label { get; private set; } = "";
    /// <summary>레벨마다 기준이 다를 때 한 줄(아니면 빈 글).</summary>
    public string Note { get; private set; } = "";
    /// <summary>레벨 Id → (이름, 보이는 높이 m).</summary>
    public Dictionary<long, (string Name, double ShownM)> Levels { get; } = new();
    /// <summary>레벨 Id → 내부 Z(m · 잰 값). 공유 좌표를 바꾼 뒤 보이는 높이가 따라 움직였는지 볼 때 씀.</summary>
    private readonly Dictionary<long, double> _internalM = new();

    /// <summary>
    /// 지금(같은 트랜잭션 안에서 공유 좌표를 바꾼 뒤도) 레벨에 보이는 높이 − 내부 Z 의 가장 많은 값(m).
    /// 높이 기준이 «측량점»인 레벨은 공유 좌표 높이를 바꾸면 보이는 높이가 따라 움직인다(설치 전 검토 중간 — 더할 값 두 번).
    /// </summary>
    public double OffsetNow(Document doc)
    {
        var offs = new List<double>();
        foreach (var (id, zi) in _internalM)
            if (doc.GetElement(new ElementId(id)) is Level lv) offs.Add(lv.Elevation * Ft - zi);
        if (offs.Count == 0) return OffsetM;
        return offs.GroupBy(o => Math.Round(o, 4)).OrderByDescending(g => g.Count()).First().First();
    }

    public static LevelHeights Read(Document doc)
    {
        var res = new LevelHeights();
        var levels = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().ToList();
        if (levels.Count == 0) { res.Label = "레벨 없음 — Revit 높이 그대로"; return res; }

        var measured = MeasureInternalZ(doc, levels);
        var offs = new List<(Level L, double Off)>();
        foreach (var lv in levels)
        {
            bool got = measured.TryGetValue(lv.Id.Value, out double zi);
            double z = got ? zi : lv.ProjectElevation;
            offs.Add((lv, (lv.Elevation - z) * Ft));
            res.Levels[lv.Id.Value] = (lv.Name, lv.Elevation * Ft);
            res._internalM[lv.Id.Value] = z * Ft;
            Log.Line($"레벨\t{lv.Name}\t보이는 {lv.Elevation * Ft:0.0000} m\t내부 Z {(got ? "잰" : "ProjectElevation")} {z * Ft:0.0000} m\tProjectElevation {lv.ProjectElevation * Ft:0.0000} m");
        }
        var groups = offs.GroupBy(o => Math.Round(o.Off, 4)).OrderByDescending(g => g.Count()).ToList();
        res.OffsetM = groups[0].First().Off;
        if (groups.Count > 1) res.Note = $"레벨마다 높이 기준이 다름 — {groups[0].Count()}/{levels.Count}개 레벨 기준";
        var rep = groups[0].Select(o => o.L)
            .OrderBy(l => l.Name.Contains("EL", StringComparison.OrdinalIgnoreCase) ? 0 : 1).ThenBy(l => l.Elevation).First();
        res.Label = $"«{rep.Name}» → {rep.Elevation * Ft:0.000} m";
        Log.Line($"레벨 높이\t더할 값 {res.OffsetM:0.0000} m\t{res.Label}\t{res.Note}");
        return res;
    }

    private static Dictionary<long, double> MeasureInternalZ(Document doc, List<Level> levels)
    {
        var z = new Dictionary<long, double>();
        try
        {
            using var t = new Transaction(doc, "DH 레벨 높이 재기");
            if (t.Start() != TransactionStatus.Started) return z;
            try
            {
                foreach (var lv in levels)
                {
                    try { z[lv.Id.Value] = SketchPlane.Create(doc, lv.Id).GetPlane().Origin.Z; }
                    catch { }
                }
            }
            finally { t.RollBack(); }   // 재기만 — 모델에 남기지 않음
        }
        catch (Exception ex) { Log.Line("레벨 내부 Z 재기 못 함\t" + ex.Message); }
        return z;
    }
}

/// <summary>Revit 공유 좌표 보기 · 쓰기.</summary>
internal static class SharedCoords
{
    private const double Ft = 0.3048;

    /// <summary>창에 보일 지금 공유 좌표(프로젝트 기준점).</summary>
    public sealed record Info(string Text, string Pill, string PillKind);

    public static Info Read(Document doc, Fit2D? fit, double zOffM)
    {
        try
        {
            var sp = BasePoint.GetProjectBasePoint(doc).SharedPosition;   // 피트 · X = 동 · Y = 북 · Z = 높이
            var pos0 = doc.ActiveProjectLocation.GetProjectPosition(XYZ.Zero);
            double e = sp.X * Ft, n = sp.Y * Ft, deg = pos0.Angle * 180 / Math.PI;
            string text = $"동 {e:#,0.000} · 북 {n:#,0.000} m · 진북 {deg:0.00}°";
            if (fit != null && fit.Problem == null && Mismatch(doc.ActiveProjectLocation, fit, zOffM) < 0.001)
                return new Info(text, "맞춤과 같음", "Good");
            if (Math.Abs(e) < 5000 && Math.Abs(n) < 5000) return new Info(text, "Civil 좌표 아님", "Warn");
            return new Info(text, "", "");
        }
        catch (Exception ex) { return new Info("못 읽음 — " + ex.Message, "", ""); }
    }

    /// <summary>
    /// 공유 좌표 = Civil 좌표(같은 두 점 값). 쓴 뒤 두 점을 Revit 에 다시 물어(GetProjectPosition) Civil 좌표로 오는지 잰다.
    /// ProjectPosition.Angle 의 돌림 방향은 문서에 «진북과 프로젝트 북의 차»로만 적혀 있어 + 로 써 보고, 안 맞으면 − 로 · 둘 다 안 맞으면 되돌린다.
    /// <paramref name="alsoInside"/> = 같은 트랜잭션 안에서 할 일(맞춤 값 저장).
    /// </summary>
    public static (bool Ok, string Message) Apply(Document doc, Fit2D fit, double zOffM, Action alsoInside)
    {
        var loc = doc.ActiveProjectLocation;
        var before = loc.GetProjectPosition(XYZ.Zero);
        var rm = P2.Mid(fit.R1, fit.R2); var cm = P2.Mid(fit.C1, fit.C2);
        var at = new XYZ(rm.X / Ft, rm.Y / Ft, 0);
        using var t = new Transaction(doc, "DH 공유 좌표 = Civil 좌표");
        var fo = t.GetFailureHandlingOptions();
        fo.SetFailuresPreprocessor(new WarningSwallower());
        t.SetFailureHandlingOptions(fo);
        if (t.Start() != TransactionStatus.Started) return (false, "원인: 트랜잭션을 못 엶");
        double best = double.MaxValue; int used = 0;
        try
        {
            foreach (int sign in new[] { 1, -1 })
            {
                loc.SetProjectPosition(at, new ProjectPosition(cm.X / Ft, cm.Y / Ft, zOffM / Ft, Fit2D.Normalize(sign * fit.Angle)));
                doc.Regenerate();
                double err = Mismatch(loc, fit, zOffM);
                Log.Line($"공유 좌표 쓰기\t돌림 부호 {sign:+0;-0}\t두 점 어긋남 {err * 1000:0.000} mm");
                best = Math.Min(best, err);
                if (err < 1e-4) { used = sign; break; }
            }
            if (used == 0)
            {
                t.RollBack();
                return (false, $"원인: 공유 좌표를 넣어도 두 점이 Civil 좌표로 안 옴(어긋남 {best * 1000:0.0} mm) — 바꾸지 않음");
            }
            alsoInside();
            var st = t.Commit();
            if (st != TransactionStatus.Committed) return (false, "원인: Revit이 바꾸기를 받지 않음(" + st + ")");
        }
        catch (Exception ex)
        {
            if (t.HasStarted() && !t.HasEnded()) t.RollBack();
            Log.Line("공유 좌표 쓰기 오류\t" + ex);
            return (false, "원인: " + ex.Message);
        }
        var after = loc.GetProjectPosition(XYZ.Zero);
        Log.Line($"공유 좌표\t전 동 {before.EastWest * Ft:0.000} 북 {before.NorthSouth * Ft:0.000} 높이 {before.Elevation * Ft:0.000} 각 {before.Angle * 180 / Math.PI:0.0000}°" +
                 $"\t후 동 {after.EastWest * Ft:0.000} 북 {after.NorthSouth * Ft:0.000} 높이 {after.Elevation * Ft:0.000} 각 {after.Angle * 180 / Math.PI:0.0000}°");
        return (true, "공유 좌표를 Civil 좌표로 바꿈 — Revit IFC · DWG · NWC를 «공유 좌표»로 내보내면 맞습니다");
    }

    /// <summary>두 점을 Revit 에 물은 공유 좌표와 맞춤 값의 가장 큰 차(m · 높이 포함).</summary>
    private static double Mismatch(ProjectLocation loc, Fit2D fit, double zOffM)
    {
        double worst = 0;
        foreach (var r in new[] { fit.R1, fit.R2 })
        {
            var pp = loc.GetProjectPosition(new XYZ(r.X / Ft, r.Y / Ft, 0));
            var w = fit.Apply(r);
            double dxy = Math.Sqrt(Math.Pow(pp.EastWest * Ft - w.X, 2) + Math.Pow(pp.NorthSouth * Ft - w.Y, 2));
            worst = Math.Max(worst, Math.Max(dxy, Math.Abs(pp.Elevation * Ft - zOffM)));
        }
        return worst;
    }

    /// <summary>경고만 지움(오류는 그대로 — Revit 이 되돌린다).</summary>
    private sealed class WarningSwallower : IFailuresPreprocessor
    {
        public FailureProcessingResult PreprocessFailures(FailuresAccessor a)
        {
            foreach (var f in a.GetFailureMessages())
                if (f.GetSeverity() == FailureSeverity.Warning) { Log.Line("경고 지움\t" + f.GetDescriptionText()); a.DeleteWarning(f); }
            return FailureProcessingResult.Continue;
        }
    }
}
