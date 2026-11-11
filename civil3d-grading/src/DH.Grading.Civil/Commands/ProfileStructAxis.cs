using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using DH.Grading.Core;

namespace DH.Grading.Civil.Commands;

/// <summary>★[JACK 1111 «종단선을 구조물 축에 맞춤»] [종단도] 시작점 자리의 [구조물 축(T)] — 고른 구조물 평면 외곽의 주축(긴 축 · S = 짧은 축)을 따라
/// 부지 경계(DH-정지경계)까지 + 5 m 종단선(<see cref="StructAxis"/>). 노선이 구조물을 비스듬히(두 축 어느 쪽과도 5° 넘게) 지나면 알림 한 줄(막지 않음).</summary>
internal static class ProfileStructAxis
{
    const string LayerDaylight = "DH-정지경계";
    /// <summary>평면 외곽 껍질 — 모형(캐시된 것)마다 한 번만 셈(다시 그리기마다 합집합을 다시 하지 않게).</summary>
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<StructModel, List<IReadOnlyList<(double X, double Y)>>> Shells = new();

    static List<IReadOnlyList<(double X, double Y)>> ShellsOf(StructModel m)
        => Shells.GetValue(m, mm => StructPoly.PlanOutline(mm, out _).Select(p => (IReadOnlyList<(double X, double Y)>)p.Shell).ToList());

    /// <summary>구조물을 골라 축 따라 종단선 두 점(WCS) — 취소 · 구조물 없음 = null. 축 한 줄을 명령줄 · <paramref name="log"/>에.</summary>
    internal static List<Point3d>? Pick(Database db, Editor ed, System.Text.StringBuilder log)
    {
        var all = StructImport.LoadAll(db, log, out var warn);
        foreach (var w in warn) log.AppendLine("  [구조물] " + w);
        if (all.Count == 0) { ed.WriteMessage("\n[종단도] 쓸 수 있는 구조물이 없습니다 — [구조물 불러오기] 먼저(원본을 못 찾은 구조물은 못 씁니다)."); return null; }
        bool shortAx = false;
        StructImport.Loaded? L = null;
        while (L == null)
        {
            var po = new PromptEntityOptions($"\n[종단도] 구조물 선택 — {(shortAx ? "짧은" : "긴")} 축 따라" + (all.Count == 1 ? $" (Enter={all[0].Rec.Name})" : ""));
            po.Keywords.Add("L", "L", "긴 축(L)");
            po.Keywords.Add("S", "S", "짧은 축(S)");
            po.AllowNone = all.Count == 1;
            var pr = ed.GetEntity(po);
            if (pr.Status == PromptStatus.Keyword) { shortAx = pr.StringResult == "S"; continue; }
            if (pr.Status == PromptStatus.None && all.Count == 1) { L = all[0]; break; }
            if (pr.Status != PromptStatus.OK) return null;
            int id = -1;
            try
            {
                using var tr = db.TransactionManager.StartTransaction();
                if (tr.GetObject(pr.ObjectId, OpenMode.ForRead) is Entity e && StructDraw.TagOf(e, out _, out _, out int sid)) id = sid;
                tr.Commit();
            }
            catch { }
            L = all.FirstOrDefault(x => x.Rec.Id == id || x.Insert == pr.ObjectId);
            if (L == null) ed.WriteMessage("\n  · 구조물이 아닙니다 — 평면 외곽(DH-구조물) · 솔리드를 고르세요.");
        }

        var shells = ShellsOf(L.Model);
        var ax = StructAxis.Of(shells);
        if (ax == null) { ed.WriteMessage($"\n[종단도] 구조물 {L.Rec.Name}의 평면 외곽을 못 얻어 축을 못 정했습니다."); return null; }
        var site = new List<(IReadOnlyList<(double X, double Y)> Pts, bool Closed)>();
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
            foreach (ObjectId id in ms)
            {
                try
                {
                    if (tr.GetObject(id, OpenMode.ForRead) is not Curve c || !string.Equals(c.Layer, LayerDaylight, System.StringComparison.OrdinalIgnoreCase)) continue;
                    var pts = new List<(double X, double Y)>();
                    if (c is Polyline || c is Polyline2d || c is Polyline3d)
                        for (int i = (int)System.Math.Round(c.StartParam); i <= (int)System.Math.Round(c.EndParam); i++) { var q = c.GetPointAtParameter(i); pts.Add((q.X, q.Y)); }
                    else
                    {
                        double len = c.GetDistanceAtParameter(c.EndParam);
                        int n = (int)System.Math.Clamp(System.Math.Ceiling(len / 0.5), 8, 20000);
                        for (int i = 0; i <= n; i++) { var q = c.GetPointAtDist(len * i / n); pts.Add((q.X, q.Y)); }
                    }
                    if (pts.Count >= 2) site.Add((pts, c.Closed));
                }
                catch { }
            }
            tr.Commit();
        }
        catch { }
        var (a, b) = StructAxis.Line(ax, shortAx, shells, site, StructAxis.Margin, out bool hit);
        double deg = shortAx ? ax.ShortDeg : ax.LongDeg, lineLen = System.Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));
        string line = $"구조물 축 — {L.Rec.Name} {(shortAx ? "짧은" : "긴")} 축 {deg:F1}° · 평면 {ax.LongLen:F1} × {ax.ShortLen:F1} m · 중심 ({ax.Cx:F1}, {ax.Cy:F1}) · 종단선 {lineLen:F1} m"
                    + (hit ? $"(부지 경계 + {StructAxis.Margin:0} m)" : $"(⚠부지 경계(DH-정지경계)와 안 만나 구조물 + {StructAxis.Margin:0} m)");
        ed.WriteMessage("\n[종단도] " + line);
        log.AppendLine(line);
        if (ax.Tie)
        {
            string tie = $"구조물 {L.Rec.Name} 거의 정사각 — 긴 축을 {ax.LongDeg:F1}° 쪽으로 골랐습니다(다른 쪽은 [짧은 축(S)])";
            ed.WriteMessage("\n  · " + tie);
            log.AppendLine("  " + tie);
        }
        return new List<Point3d> { new(a.X, a.Y, 0), new(b.X, b.Y, 0) };
    }

    /// <summary>노선이 지나는 구조물마다 축과 벌어진 각을 재어 <see cref="StructAxis.WarnDeg"/>를 넘으면 알림 줄(로그에도) — 막지 않음. 구조물 없으면 빈 목록.</summary>
    internal static List<string> Oblique(Database db, ObjectId routeId, System.Text.StringBuilder log)
    {
        var res = new List<string>();
        if (routeId.IsNull || !StructImport.Any(db)) return res;
        try
        {
            var route = new List<(double X, double Y)>();
            using (var tr = db.TransactionManager.StartTransaction())
            {
                if (tr.GetObject(routeId, OpenMode.ForRead) is Polyline pl)
                    for (int i = 0; i < pl.NumberOfVertices; i++) { var p = pl.GetPoint2dAt(i); route.Add((p.X, p.Y)); }
                tr.Commit();
            }
            if (route.Count < 2) return res;
            foreach (var L in StructImport.LoadAll(db, log, out _))
            {
                var shells = ShellsOf(L.Model);
                var ax = StructAxis.Of(shells);
                if (ax == null) continue;
                double? th = StructAxis.CrossDeg(route, shells, ax.LongDeg);
                if (th == null) continue;
                log.AppendLine($"  [구조물] {L.Rec.Name} — 노선이 축과 {th:0.#}° 벌어짐(긴 축 {ax.LongDeg:F1}°)");
                if (th > StructAxis.WarnDeg)
                    res.Add($"구조물 {L.Rec.Name}과 {th:0.#}° 비스듬 — 횡단에서 벽이 실제보다 두껍게 보입니다 · [구조물 축(T)]로 다시 그을 수 있습니다");
            }
        }
        catch (System.Exception ex) { log.AppendLine("  [구조물] 비스듬 검사 실패 — " + ex.Message); }
        foreach (var r in res) log.AppendLine("  ⚠" + r);
        return res;
    }
}
