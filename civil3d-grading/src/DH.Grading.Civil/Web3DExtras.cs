using Autodesk.AutoCAD.DatabaseServices;
using DH.Grading.Core;

namespace DH.Grading.Civil;

/// <summary>★★[v108 · 계획_v108.md 2.6 · 2.7] [3D 보기] 도면에서 읽어 오는 덧 자료 — 지적(도면 레이어) · 종단 측점(Civil 선형 · 샘플 선) · 측점 → 횡단 뷰 찾기.
/// <para>모두 <b>읽기만</b>(트랜잭션은 열어 읽고 닫음 — 도면 «바뀜» 0 · v107 실측 법).</para></summary>
internal static class Web3DExtras
{
    /// <summary>지적 — <c>DH-지적도</c> 선(2 m마다 쪼개 높이 + 0.15 m) · <c>DH-지번</c> 글자(+ 1 m). 범위 [minX, maxX] × [minY, maxY] 안만. 높이는 <paramref name="z"/>(없으면 그 점을 버림).</summary>
    internal static (float[] Seg, List<(string T, double X, double Y, double Z)> Labels, int Lines) Cadastral(Database db, Func<double, double, double?> z,
        double minX, double minY, double maxX, double maxY, double ox, double oy)
    {
        var seg = new List<float>(); var labels = new List<(string, double, double, double)>(); int lines = 0;
        bool In(double x, double y) => x >= minX && x <= maxX && y >= minY && y <= maxY;
        void Add(Autodesk.AutoCAD.Geometry.Point3d a, Autodesk.AutoCAD.Geometry.Point3d b)
        {
            double len = System.Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));
            int n = System.Math.Max(1, (int)System.Math.Ceiling(len / 2.0));
            (double X, double Y, double? Z) P(int i) { double t = (double)i / n, x = a.X + (b.X - a.X) * t, y = a.Y + (b.Y - a.Y) * t; return (x, y, In(x, y) ? z(x, y) : null); }
            var p = P(0);
            for (int i = 1; i <= n; i++)
            {
                var q = P(i);
                if (p.Z.HasValue && q.Z.HasValue)
                {
                    seg.Add((float)(p.X - ox)); seg.Add((float)(p.Y - oy)); seg.Add((float)(p.Z.Value + 0.15));
                    seg.Add((float)(q.X - ox)); seg.Add((float)(q.Y - oy)); seg.Add((float)(q.Z.Value + 0.15));
                }
                p = q;
            }
        }
        using var tr = db.TransactionManager.StartTransaction();
        var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
        foreach (ObjectId id in ms)
        {
            if (tr.GetObject(id, OpenMode.ForRead) is not Entity e) continue;
            if (string.Equals(e.Layer, Commands.ImportGisCommand.LayerParcel, System.StringComparison.OrdinalIgnoreCase) && e is Curve cv)
            {
                try
                {
                    var ext = cv.GeometricExtents;
                    if (ext.MaxPoint.X < minX || ext.MinPoint.X > maxX || ext.MaxPoint.Y < minY || ext.MinPoint.Y > maxY) continue;
                    if (cv is Line ln) Add(ln.StartPoint, ln.EndPoint);
                    else if (cv is Polyline pl && Enumerable.Range(0, pl.NumberOfVertices).All(i => System.Math.Abs(pl.GetBulgeAt(i)) < 1e-9))
                    {
                        int n = (int)System.Math.Round(cv.EndParam - cv.StartParam);
                        // 곧은 폴리선 — 닫힌 것은 끝 매개변수가 닫는 변까지 든다(정수 매개변수 = 꼭짓점)
                        for (int i = 0; i < n; i++) Add(cv.GetPointAtParameter(cv.StartParam + i), cv.GetPointAtParameter(cv.StartParam + i + 1));
                    }
                    else
                    {
                        // ★[설치 전 검토 열린 물음 3] 호 · 원 · 스플라인 · 볼록 폴리선 — 길이를 따라 2 m마다(정수 매개변수로 뛰면 원이 여섯 줄 · 스플라인이 한 줄)
                        double len = cv.GetDistanceAtParameter(cv.EndParam);
                        int n = System.Math.Max(1, (int)System.Math.Ceiling(len / 2.0));
                        var prev = cv.GetPointAtDist(0);
                        for (int i = 1; i <= n; i++) { var q = cv.GetPointAtDist(System.Math.Min(len, len * i / n)); Add(prev, q); prev = q; }
                    }
                    lines++;
                }
                catch { }
            }
            else if (string.Equals(e.Layer, Commands.ImportGisCommand.LayerJibun, System.StringComparison.OrdinalIgnoreCase))
            {
                string t = ""; Autodesk.AutoCAD.Geometry.Point3d at = default;
                if (e is DBText dt) { t = dt.TextString; at = dt.Position; }
                else if (e is MText mt) { t = mt.Text; at = mt.Location; }
                else continue;
                if (t.Length == 0 || !In(at.X, at.Y)) continue;
                var zz = z(at.X, at.Y);
                if (zz.HasValue) labels.Add((t.Trim(), at.X - ox, at.Y - oy, zz.Value + 1.0));
            }
        }
        tr.Commit();
        return (seg.ToArray(), labels, lines);
    }

    internal sealed class Station
    {
        public double S; public string Label = ""; public bool Major; public int Views;
        public double[] L = new double[3], R = new double[3], C = new double[3];
        /// <summary>왼 끝 → 오른 끝 1 m마다(x, y, z — 땅 + 0.3 m · 없으면 NaN) — 측점 가로선이 땅을 따라가게(곧은 L–R 선은 둔덕을 뚫는다).</summary>
        public float[] P = System.Array.Empty<float>();
    }

    /// <summary>종단 측점 — <c>DH선형*</c> 선형(마지막 것 — 다른 명령과 같은 법) · 측점 샘플 선 묶음(횡단 뷰 묶음이 아닌 것 · 없으면 횡단 뷰 묶음)의 측점 · 좌우 끝 · 중심선(2 m마다).
    /// 높이 = <paramref name="z"/> + 0.3 m. 측점마다 이름이 맞는 횡단 뷰 수. 선형이 없으면 null.</summary>
    internal static (string Name, float[] Line, double S0, double S1, double Index, List<Station> St, int Views, int Skipped)? Stations(Database db, Func<double, double, double?> z, double ox, double oy)
    {
        var cdoc = Autodesk.Civil.ApplicationServices.CivilApplication.ActiveDocument;
        using var tr = db.TransactionManager.StartTransaction();
        Autodesk.Civil.DatabaseServices.Alignment? al = null;
        foreach (ObjectId aid in cdoc.GetAlignmentIds())
            if (tr.GetObject(aid, OpenMode.ForRead) is Autodesk.Civil.DatabaseServices.Alignment a && (a.Name ?? "").StartsWith(Commands.SectionCommand.AlignBase, System.StringComparison.Ordinal)) al = a;
        if (al == null) { tr.Commit(); return null; }
        double index = al.StationIndexIncrement > 1e-6 ? al.StationIndexIncrement : 20.0;
        double wl = System.Math.Max(5, GradingSettings.XsecLeft), wr = System.Math.Max(5, GradingSettings.XsecRight);
        double? Z(double x, double y) => z(x, y) is double v ? v + 0.3 : null;
        // 측점 · 뷰 이름
        var stations = new SortedDictionary<long, Station>();
        var viewNames = new List<string>(); int views = 0, skipped = 0;
        var groups = new List<(bool Xsec, ObjectId Id)>();
        foreach (ObjectId gid in al.GetSampleLineGroupIds())
            if (tr.GetObject(gid, OpenMode.ForRead) is Autodesk.Civil.DatabaseServices.SampleLineGroup g)
                groups.Add((Commands.XsecViewCommand.IsXsecGroupName(g.Name), gid));
        bool anyPlain = groups.Exists(g => !g.Xsec);
        foreach (var (xsec, gid) in groups)
        {
            var g = (Autodesk.Civil.DatabaseServices.SampleLineGroup)tr.GetObject(gid, OpenMode.ForRead);
            foreach (ObjectId sid in g.GetSampleLineIds())
            {
                if (tr.GetObject(sid, OpenMode.ForRead) is not Autodesk.Civil.DatabaseServices.SampleLine sl) continue;
                foreach (ObjectId vid in sl.GetSectionViewIds())
                    if (tr.GetObject(vid, OpenMode.ForRead) is Autodesk.Civil.DatabaseServices.SectionView sv) { views++; viewNames.Add(sv.Name ?? ""); }
                if (xsec && anyPlain) continue;                   // 측점은 측점 묶음에서(횡단 뷰 묶음의 (전)/(후)는 밀린 자리)
                double st = sl.Station;
                long key = (long)System.Math.Round(st * 100);
                if (stations.ContainsKey(key)) continue;
                // ★[설치 전 검토 낮음] 측점 하나가 선형 밖(선형을 고친 뒤 남은 샘플 선 · 곡선 반지름보다 먼 끝)이면 그 측점만 뺀다 — 층 전체를 잃지 않게
                try
                {
                    var s = new Station { S = st, Label = StationNaming.Fmt(st, index), Major = StationNaming.IsMajor(st, index, out _) };
                    double e = 0, n = 0;
                    al.PointLocation(st, 0, ref e, ref n); s.C = new[] { e - ox, n - oy, Z(e, n) ?? double.NaN };
                    al.PointLocation(st, -wl, ref e, ref n); s.L = new[] { e - ox, n - oy, Z(e, n) ?? double.NaN };
                    al.PointLocation(st, wr, ref e, ref n); s.R = new[] { e - ox, n - oy, Z(e, n) ?? double.NaN };
                    int np = (int)System.Math.Ceiling(wl + wr);
                    var pp = new float[(np + 1) * 3];
                    for (int i = 0; i <= np; i++)
                    {
                        al.PointLocation(st, -wl + (wl + wr) * i / np, ref e, ref n);
                        pp[i * 3] = (float)(e - ox); pp[i * 3 + 1] = (float)(n - oy); pp[i * 3 + 2] = (float)(Z(e, n) ?? double.NaN);
                    }
                    s.P = pp;
                    stations[key] = s;
                }
                catch { skipped++; }
            }
        }
        foreach (var s in stations.Values) s.Views = viewNames.Count(v => v == s.Label || v == s.Label + "(전)" || v == s.Label + "(후)");
        // 중심선 2 m마다 + 끝 측점(높이 없는 자리는 끊김 — NaN)
        var line = new List<float>();
        double s0 = al.StartingStation, s1 = al.EndingStation;
        void At(double st) { double e = 0, n = 0; al.PointLocation(st, 0, ref e, ref n); line.Add((float)(e - ox)); line.Add((float)(n - oy)); line.Add((float)(Z(e, n) ?? double.NaN)); }
        double last = s0;
        for (double st = s0; st <= s1 + 1e-6; st += 2.0) { At(System.Math.Min(st, s1)); last = System.Math.Min(st, s1); }
        // ★[설치 전 검토 낮음] 마지막 2 m 안 토막도 — 끝 측점이 빠지면 빨간 선 · 자르기 막대가 끝에 못 닿는다
        if (s1 - last > 1e-6) At(s1);
        string name = al.Name ?? "";
        tr.Commit();
        return (name, line.ToArray(), s0, s1, index, stations.Values.ToList(), views, skipped);
    }

    /// <summary>[도면 횡단 보기] — 그 측점 이름(+ (전)/(후))의 횡단 뷰들 범위. 이름이 안 맞으면(옛 DHSECTION 뷰 — 이름이 «묶음_횡단_i») 샘플 선 측점이 <b>같은</b>(5 cm 안) 뷰 —
    /// 가까운 다른 측점으로는 가지 않는다(계획 검토 중간 9). 선형은 <see cref="Stations"/>와 같은 것(마지막 <c>DH선형*</c>). 뷰가 하나도 없으면 total = 0.</summary>
    internal static (Autodesk.AutoCAD.DatabaseServices.Extents3d? Ext, int Matched, int Total) FindViews(Database db, string label, double station)
    {
        var cdoc = Autodesk.Civil.ApplicationServices.CivilApplication.ActiveDocument;
        using var tr = db.TransactionManager.StartTransaction();
        Extents3d? ext = null; int matched = 0, total = 0;
        var near = new List<Extents3d>();
        Autodesk.Civil.DatabaseServices.Alignment? last = null;
        foreach (ObjectId aid in cdoc.GetAlignmentIds())
            if (tr.GetObject(aid, OpenMode.ForRead) is Autodesk.Civil.DatabaseServices.Alignment a && (a.Name ?? "").StartsWith(Commands.SectionCommand.AlignBase, System.StringComparison.Ordinal)) last = a;
        foreach (var al in last == null ? System.Array.Empty<Autodesk.Civil.DatabaseServices.Alignment>() : new[] { last })
        {
            foreach (ObjectId gid in al.GetSampleLineGroupIds())
            {
                if (tr.GetObject(gid, OpenMode.ForRead) is not Autodesk.Civil.DatabaseServices.SampleLineGroup g) continue;
                foreach (ObjectId sid in g.GetSampleLineIds())
                {
                    if (tr.GetObject(sid, OpenMode.ForRead) is not Autodesk.Civil.DatabaseServices.SampleLine sl) continue;
                    foreach (ObjectId vid in sl.GetSectionViewIds())
                    {
                        if (tr.GetObject(vid, OpenMode.ForRead) is not Autodesk.Civil.DatabaseServices.SectionView sv) continue;
                        total++;
                        Extents3d x;
                        try { x = ((Entity)sv).GeometricExtents; } catch { continue; }
                        string nm = sv.Name ?? "";
                        if (nm == label || nm == label + "(전)" || nm == label + "(후)")
                        {
                            matched++;
                            if (ext == null) ext = x; else { var t = ext.Value; t.AddExtents(x); ext = t; }
                        }
                        else if (System.Math.Abs(sl.Station - station) <= 0.05) near.Add(x);
                    }
                }
            }
        }
        if (matched == 0 && near.Count > 0)
        {
            foreach (var x in near) { if (ext == null) ext = x; else { var t = ext.Value; t.AddExtents(x); ext = t; } }
            matched = near.Count;
        }
        tr.Commit();
        return (ext, matched, total);
    }
}
