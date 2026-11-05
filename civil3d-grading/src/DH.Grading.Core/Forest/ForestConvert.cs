using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.Json;
using NetTopologySuite.Geometries;
using NetTopologySuite.Geometries.Utilities;
using NetTopologySuite.Operation.Overlay;
using NetTopologySuite.Operation.OverlayNG;
using NetTopologySuite.Precision;
using NetTopologySuite.Simplify;

namespace DH.Grading.Core.Forest;

/// <summary>★★[v115 · JACK 1105 임상도] <b>임상도 SHP → 2 km 타일</b>(계획 2판 3절) — 한 번 변환(개발 PC · <c>tools\ForestTiles</c>) · 원본마다 이어 하기 · 여러 스레드 · 검증 보고서.
/// <para>원본 하나: SHX 위치로 SHP 레코드 → DBF 코드 칸 → 고리 나누기(바깥 = 시계 · 구멍 = 반시계 · 구멍은 «담는 가장 작은 바깥»에) → 25 ㎡ 미만 부분 버림 →
/// 무효면 <see cref="GeometryFixer"/> → Douglas-Peucker 1 m → 칸 자르기(0.1 m 격자 스냅 라운딩 · OverlayNG) → 0.1 m 정수 좌표.
/// 위상 예외 → Fix → 한 번 더 → 안 되면 그 레코드 빼고 보고서에.</para>
/// <para>원자료 폴더에는 쓰지 않는다. 출력 = <c>{out}\_work\{원본}\</c>(조각 · done.json) → <c>{out}\tiles\</c> → <c>index.json</c>(맨 마지막).</para></summary>
public static class ForestConvert
{
    /// <summary>알고리즘 판 — 바뀌면 done.json 해시가 달라져 다시 한다.</summary>
    public const string Algo = "DHF1-conv-6";

    public sealed class Options
    {
        public string Src = "", Out = "";
        public int Threads = 16;
        public HashSet<string>? Only;
        public bool Force;
        public double Simplify = 1.0, SmallPart = 25.0;
        public int Batch = 5000;
        public Action<string> Log = Console.WriteLine;
        /// <summary>실패 비율 한도(0.01 %) — 넘으면 멈춤.</summary>
        public double FailStop = 1e-4;
    }

    public sealed record SourceFile(string Name, string Folder, string Shp, int Id);

    /// <summary>원본 목록 — <paramref name="srcRoot"/> 아래 폴더마다 *.shp · 이름 차례(같은 입력 → 같은 번호).</summary>
    public static List<SourceFile> FindSources(string srcRoot)
    {
        var files = Directory.GetDirectories(srcRoot).SelectMany(d => Directory.GetFiles(d, "*.shp").Select(f => (Folder: Path.GetFileName(d), Shp: f)))
            .OrderBy(f => Path.GetFileNameWithoutExtension(f.Shp), StringComparer.Ordinal).ToList();
        return files.Select((f, i) => new SourceFile(Path.GetFileNameWithoutExtension(f.Shp), f.Folder, f.Shp, i)).ToList();
    }

    static bool Picked(SourceFile s, HashSet<string>? only) => only == null || only.Contains(s.Name) || only.Contains(s.Folder);

    // ── 원본 하나의 결과(done.json) ──

    public sealed class SourceStats
    {
        public string Name = "", Hash = "", Modified = "";
        public int Id;
        public long Bytes;
        public int Records, Kept, Frtp0, Small, Failed, SmallParts, OrphanHoles, Fixed, UnknownFrtp, Pieces, Tiles, FlatRings;
        public long PointsIn, PointsOut;
        /// <summary>넓이(FRTP마다) — 원래(고친 뒤 · 솎기 전) · 솎은 뒤(0.1 m 격자 전) · 칸(0.1 m 정수).</summary>
        public double[] AreaIn = new double[5], AreaSimp = new double[5], AreaOut = new double[5];
        public double AreaSmall;
        public int YearMin = int.MaxValue, YearMax = int.MinValue;
        /// <summary>③ 칸 경계 레코드 — 수 · 조각 합(자른 직후 실수) ↔ 솎은 넓이 상대 차 최대 · 0.01 % 넘은 수.</summary>
        public int Boundary, BoundaryOver; public double BoundaryMax;
        /// <summary>③ 가장 큰 차 레코드(앞 10) — [레코드, 통 넓이, 조각 합(실수 자르기), 상대 차] · 실수 자르기 못 한 수 · 0.1 m 마디 맞춤 영향(최대 ㎡ · 최대 비율).</summary>
        public List<double[]> BoundaryWorst = new();
        public int BoundaryUnchecked; public double SnapAbs, SnapRel;
        /// <summary>M1 넓이 검사 — |Δ| &gt; 1 % 이고 &gt; 100 ㎡인 레코드(앞 1000개) · 수.</summary>
        public int FlagCount; public List<double[]> Flags = new();
        /// <summary>DBF Shape_Area ↔ NTS 원래 넓이(경고만) — 0.1 % · 1 % 넘은 수 · 최대 상대 차.</summary>
        public int DbfOver01, DbfOver1; public double DbfMax;
        public List<string> FailList = new();
        public Dictionary<string, Dictionary<string, string>> Codes = new();
        public double Seconds;
    }

    sealed class TileBuf
    {
        public readonly List<(long Order, int Piece, byte[] Bytes)> Items = new();
    }

    // ── 레코드 하나 ──

    internal sealed class RecOut
    {
        public int Status;   // 0 남김 · 1 FRTP0 · 2 작은 부분만 · 3 실패
        public string Why = "";
        public List<(int Tx, int Ty, int Piece, ForestPoly P, double Area)> Pieces = new();
        public double AreaIn, AreaSimp, AreaOut, AreaSmall;
        public int SmallParts, Orphans, Fixed, Flat;
        public long PtsOut;
        public bool Boundary, BoundaryUnchecked; public double BoundaryRel, BoundaryA, BoundaryB, SnapAbs, SnapRel;
    }

    static readonly PrecisionModel Pm10 = new(10.0);
    static readonly PrecisionModel PmFloat = new();
    static readonly GeometryFactory GfF = new();
    static readonly GeometryFactory Gf10 = new(Pm10);

    static double Shoelace(double[] r)
    {
        double a = 0;
        int n = r.Length / 2;
        for (int i = 0, j = n - 1; i < n; j = i++) a += r[2 * j] * r[2 * i + 1] - r[2 * i] * r[2 * j + 1];
        return a / 2;   // 반시계 양수
    }

    static Coordinate[] Coords(double[] r)
    {
        int n = r.Length / 2;
        bool closed = n > 1 && r[0] == r[2 * n - 2] && r[1] == r[2 * n - 1];
        var cs = new Coordinate[closed ? n : n + 1];
        for (int i = 0; i < n; i++) cs[i] = new Coordinate(r[2 * i], r[2 * i + 1]);
        if (!closed) cs[n] = cs[0].Copy();
        return cs;
    }

    /// <summary>SHP 고리들 → 부분(바깥 + 담긴 구멍) — M1: 구멍마다 «그 구멍을 담는 가장 작은 바깥 고리» · 없으면 고아(버림).</summary>
    internal static List<Polygon> Parts(List<double[]> rings, out int orphans) => Parts(rings, out orphans, out _);

    /// <summary><paramref name="flat"/> = 넓이 0 · 점 셋 미만 고리 수(원자료에 실제로 있음 — 1105 전국 변환: 점 넷 · 넓이 0 레코드 2개).</summary>
    internal static List<Polygon> Parts(List<double[]> rings, out int orphans, out int flat)
    {
        orphans = 0; flat = 0;
        var outs = new List<(Coordinate[] C, double A, Envelope E, List<LinearRing> H)>();
        var holes = new List<(Coordinate[] C, Envelope E)>();
        foreach (var r in rings)
        {
            if (r.Length < 8) { flat++; continue; }   // 점 셋 + 닫는 점 미만
            double a = Shoelace(r);
            if (a == 0) { flat++; continue; }
            var cs = Coords(r);
            if (cs.Length < 4) continue;
            var env = new Envelope();
            foreach (var c in cs) env.ExpandToInclude(c);
            if (a < 0) outs.Add((cs, -a, env, new List<LinearRing>()));   // 시계 = 바깥(SHP)
            else holes.Add((cs, env));
        }
        // 바깥이 하나도 없는데 고리가 있으면(방향을 거꾸로 적은 자료) — 반시계도 바깥으로 본다(그 고리들은 구멍이 아님)
        if (outs.Count == 0 && holes.Count > 0)
        {
            foreach (var h in holes) outs.Add((h.C, Math.Abs(Shoelace(Flat(h.C))), h.E, new List<LinearRing>()));
            holes.Clear();
        }
        var order = Enumerable.Range(0, outs.Count).OrderBy(i => outs[i].A).ThenBy(i => i).ToList();
        foreach (var h in holes)
        {
            int best = -1;
            foreach (int i in order)
            {
                if (!outs[i].E.Covers(h.E)) continue;
                if (Inside(h.C, outs[i].C)) { best = i; break; }
            }
            if (best < 0) { orphans++; continue; }
            outs[best].H.Add(GfF.CreateLinearRing(h.C));
        }
        return outs.Select(o => GfF.CreatePolygon(GfF.CreateLinearRing(o.C), o.H.ToArray())).ToList();
    }

    static double[] Flat(Coordinate[] cs) { var r = new double[cs.Length * 2]; for (int i = 0; i < cs.Length; i++) { r[2 * i] = cs[i].X; r[2 * i + 1] = cs[i].Y; } return r; }

    // 구멍 점 가운데 경계 위가 아닌 첫 점으로 안팎을 가린다
    static bool Inside(Coordinate[] hole, Coordinate[] shell)
    {
        for (int i = 0; i < hole.Length - 1; i++)
        {
            var loc = NetTopologySuite.Algorithm.PointLocation.LocateInRing(hole[i], shell);
            if (loc == Location.Interior) return true;
            if (loc == Location.Exterior) return false;
        }
        return true;   // 모든 점이 경계 위 — 같은 고리(담긴 것으로)
    }

    static IEnumerable<Polygon> Polys(Geometry g)
    {
        if (g is Polygon p) { if (!p.IsEmpty) yield return p; }
        else if (g is GeometryCollection gc)
            for (int i = 0; i < gc.NumGeometries; i++)
                foreach (var q in Polys(gc.GetGeometryN(i))) yield return q;
    }

    /// <summary>0.1 m 격자 위 다각형(칸 안) → 타일 고리(칸 남서부터 정수) · 바깥 반시계 · 구멍 시계 · 겹친 점 · 닫는 점 뺌.</summary>
    internal static List<ushort[]>? Quantize(Polygon p, int tx, int ty)
    {
        double x0 = tx * (double)ForestTileFormat.TileSize, y0 = ty * (double)ForestTileFormat.TileSize;
        ushort[]? Q(LineString ring, bool ccw)
        {
            var o = new List<ushort>(ring.NumPoints * 2);
            for (int i = 0; i < ring.NumPoints; i++)
            {
                var c = ring.GetCoordinateN(i);
                int qx = (int)Math.Round((c.X - x0) / ForestTileFormat.Quant), qy = (int)Math.Round((c.Y - y0) / ForestTileFormat.Quant);
                qx = Math.Clamp(qx, 0, ForestTileFormat.QMax); qy = Math.Clamp(qy, 0, ForestTileFormat.QMax);
                if (o.Count >= 2 && o[^2] == qx && o[^1] == qy) continue;
                o.Add((ushort)qx); o.Add((ushort)qy);
            }
            while (o.Count >= 4 && o[0] == o[^2] && o[1] == o[^1]) { o.RemoveAt(o.Count - 1); o.RemoveAt(o.Count - 1); }
            if (o.Count < 6) return null;
            var r = o.ToArray();
            double a = ForestTileFormat.RingArea(r);
            if (a == 0) return null;
            if ((a > 0) != ccw)
            {
                // 뒤집기 — 첫 점 그대로
                for (int i = 1, j = r.Length / 2 - 1; i < j; i++, j--)
                {
                    (r[2 * i], r[2 * j]) = (r[2 * j], r[2 * i]);
                    (r[2 * i + 1], r[2 * j + 1]) = (r[2 * j + 1], r[2 * i + 1]);
                }
            }
            return r;
        }
        var shell = Q(p.ExteriorRing, true);
        if (shell == null) return null;
        var rings = new List<ushort[]> { shell };
        for (int k = 0; k < p.NumInteriorRings; k++) if (Q(p.GetInteriorRingN(k), false) is { } h) rings.Add(h);
        return rings;
    }

    static Polygon TileRect(int tx, int ty)
    {
        double x0 = tx * (double)ForestTileFormat.TileSize, y0 = ty * (double)ForestTileFormat.TileSize, s = ForestTileFormat.TileSize;
        return Gf10.CreatePolygon(new[] { new Coordinate(x0, y0), new Coordinate(x0 + s, y0), new Coordinate(x0 + s, y0 + s), new Coordinate(x0, y0 + s), new Coordinate(x0, y0) });
    }

    /// <summary>레코드 하나 → 칸 조각(시험이 직접 부른다).</summary>
    internal static void ProcessRecord(List<double[]> rings, ForestPoly attrs, double simplify, double smallPart, RecOut ro)
    {
        var parts = Parts(rings, out int orph, out int flat);
        ro.Orphans = orph; ro.Flat = flat;
        // 넓이 0 고리만 있는 레코드 = 넓이 0 < 25 ㎡ → «작은 부분만»(고칠 수 없는 원자료 · 실패와 따로 셈)
        if (parts.Count == 0 && flat > 0) { ro.Status = 2; ro.SmallParts += flat; return; }
        if (parts.Count == 0) { ro.Status = 3; ro.Why = "고리 없음"; return; }
        int piece = 0;
        bool any = false;
        for (int pi = 0; pi < parts.Count; pi++)
        {
            Geometry g = parts[pi];
            double aRaw = g.Area;
            // ★[L5] 25 ㎡ 규칙 — 자르기 전 레코드 전체의 부분에만
            if (aRaw < smallPart) { ro.SmallParts++; ro.AreaSmall += aRaw; continue; }
            Geometry? simp = null; double a0 = 0;
            List<(int Tx, int Ty, Polygon P, double A)>? cut = null;
            for (int attempt = 0; attempt < 2 && cut == null; attempt++)
            {
                try
                {
                    if (attempt == 1 || !g.IsValid) { g = GeometryFixer.Fix(g); ro.Fixed++; }
                    a0 = g.Area;
                    simp = DouglasPeuckerSimplifier.Simplify(g, simplify);
                    if (simp.IsEmpty) { cut = new(); break; }
                    // 0.1 m 격자로 먼저 한 번(스냅 라운딩 — 격자 위에서 유효) → 칸 자르기는 그 격자 다각형을 자른다(조각끼리 같은 마디를 나눠 가짐)
                    var snapped = GeometryPrecisionReducer.Reduce(simp, Pm10);
                    if (snapped.IsEmpty) { cut = new(); break; }
                    var env = snapped.EnvelopeInternal;
                    int tx0 = ForestTileFormat.TileOf(env.MinX), tx1 = ForestTileFormat.TileOf(env.MaxX), ty0 = ForestTileFormat.TileOf(env.MinY), ty1 = ForestTileFormat.TileOf(env.MaxY);
                    // 칸 경계에 꼭 닿은 상자(최대 = 칸 경계)는 그 칸까지만
                    if (tx1 > tx0 && env.MaxX == tx1 * (double)ForestTileFormat.TileSize) tx1--;
                    if (ty1 > ty0 && env.MaxY == ty1 * (double)ForestTileFormat.TileSize) ty1--;
                    var list = new List<(int, int, Polygon, double)>();
                    double sumF = 0;
                    if (tx0 == tx1 && ty0 == ty1)
                    {
                        foreach (var q in Polys(snapped)) list.Add((tx0, ty0, q, q.Area));
                    }
                    else
                    {
                        ro.Boundary = true;
                        for (int tx = tx0; tx <= tx1; tx++)
                            for (int ty = ty0; ty <= ty1; ty++)
                            {
                                var r = OverlayNG.Overlay(snapped, TileRect(tx, ty), SpatialFunction.Intersection, Pm10);
                                foreach (var q in Polys(r)) { list.Add((tx, ty, q, q.Area)); sumF += q.Area; }
                            }
                        // ③ 자르기만 재기 — 같은 격자 다각형을 실수 정밀도로 칸마다 잘라 조각 합 = 통 넓이인가(나눠 넣기에서 잃거나 겹친 것이 없는가).
                        //   저장 조각은 자른 마디를 0.1 m로 맞춘다(꼴이 0.1 m 정수) — 그 영향(조각 합 − 통)은 따로 잰다(1105 시범: 1 ㎡ 안팎 · 작은 레코드일수록 비율이 큼)
                        double sa = snapped.Area, sumX = 0;
                        bool xOk = true;
                        try
                        {
                            for (int tx = tx0; tx <= tx1; tx++)
                                for (int ty = ty0; ty <= ty1; ty++)
                                    sumX += OverlayNG.Overlay(snapped, TileRect(tx, ty), SpatialFunction.Intersection, PmFloat).Area;
                        }
                        catch (TopologyException) { xOk = false; ro.BoundaryUnchecked = true; }
                        double rel = xOk && sa > 0 ? Math.Abs(sumX - sa) / sa : 0;
                        if (rel >= ro.BoundaryRel) { ro.BoundaryRel = rel; ro.BoundaryA = sa; ro.BoundaryB = sumX; }
                        ro.SnapAbs = Math.Max(ro.SnapAbs, Math.Abs(sumF - sa));
                        ro.SnapRel = Math.Max(ro.SnapRel, sa > 0 ? Math.Abs(sumF - sa) / sa : 0);
                    }
                    cut = list;
                }
                catch (Exception ex) when (ex is TopologyException or ArgumentException or InvalidOperationException)
                {
                    if (attempt == 1) { ro.Status = 3; ro.Why = ex.GetType().Name + " — " + ex.Message; return; }
                }
            }
            if (cut == null) continue;
            ro.AreaIn += a0;
            if (simp != null) ro.AreaSimp += simp.Area;
            foreach (var (tx, ty, q, _) in cut)
            {
                var qr = Quantize(q, tx, ty);
                if (qr == null) continue;
                var fp = new ForestPoly
                {
                    Src = attrs.Src, Part = (ushort)Math.Min(pi, ushort.MaxValue), Frtp = attrs.Frtp, Koftr = attrs.Koftr, Dnst = attrs.Dnst,
                    Hgt = attrs.Hgt, Dm = attrs.Dm, Ag = attrs.Ag, Fror = attrs.Fror, Rings = qr,
                };
                double qa = ForestTileFormat.PolyArea(fp);
                ro.AreaOut += qa;
                ro.PtsOut += qr.Sum(r => r.Length / 2);
                ro.Pieces.Add((tx, ty, piece++, fp, qa));
                any = true;
            }
        }
        if (ro.Status == 3) return;
        if (!any && ro.SmallParts > 0 && ro.AreaIn == 0) { ro.Status = 2; return; }
        ro.Status = attrs.Frtp == 0 ? 1 : 0;
    }

    // ── 원본 하나 ──

    static string ParamHash(SourceFile s, long bytes, string mtime, Options o)
    {
        string k = string.Join("|", Algo, s.Name, s.Id, bytes, mtime, o.Simplify.ToString("R", CultureInfo.InvariantCulture), o.SmallPart.ToString("R", CultureInfo.InvariantCulture), ForestTileFormat.TileSize, ForestTileFormat.Quant.ToString("R", CultureInfo.InvariantCulture));
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(k)))[..16];
    }

    static string WorkDir(string outDir, string name) => Path.Combine(outDir, "_work", name);

    static (long Bytes, string Mtime) Stamp(string shp)
    {
        string bas = Path.Combine(Path.GetDirectoryName(shp) ?? "", Path.GetFileNameWithoutExtension(shp));
        long b = 0; DateTime m = DateTime.MinValue;
        foreach (var ext in new[] { ".shp", ".shx", ".dbf" })
        {
            var fi = new FileInfo(bas + ext);
            b += fi.Length;
            if (fi.LastWriteTimeUtc > m) m = fi.LastWriteTimeUtc;
        }
        return (b, m.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
    }

    /// <summary>done.json이 있고 해시가 같은가.</summary>
    public static SourceStats? Done(string outDir, SourceFile s, Options o)
    {
        string dj = Path.Combine(WorkDir(outDir, s.Name), "done.json");
        if (!File.Exists(dj)) return null;
        try
        {
            var st = JsonSerializer.Deserialize<SourceStats>(File.ReadAllText(dj), JsonOpt);
            var (b, m) = Stamp(s.Shp);
            return st != null && st.Hash == ParamHash(s, b, m, o) && File.Exists(Path.Combine(WorkDir(outDir, s.Name), "pieces.idx")) ? st : null;
        }
        catch (JsonException) { return null; }
    }

    static readonly JsonSerializerOptions JsonOpt = new() { IncludeFields = true, WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping, NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals };

    /// <summary>원본 하나를 조각으로 — <c>_work\{원본}\pieces.bin · pieces.idx · done.json</c>(done이 맨 마지막).</summary>
    public static SourceStats ConvertSource(SourceFile s, Options o, Func<double, string>? eta = null)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        string wd = WorkDir(o.Out, s.Name);
        if (Directory.Exists(wd)) Directory.Delete(wd, true);
        Directory.CreateDirectory(wd);
        var (bytes, mtime) = Stamp(s.Shp);
        var st = new SourceStats { Name = s.Name, Id = s.Id, Bytes = bytes, Modified = mtime, Hash = ParamHash(s, bytes, mtime, o) };
        var tiles = new ConcurrentDictionary<long, TileBuf>();
        var codeLock = new object();
        using var rd = new ShpDbfReader(s.Shp);
        if (rd.ShapeType != 5) throw new InvalidDataException($"{s.Name}: 다각형(형 5)이 아님 — {rd.ShapeType}");
        if (rd.DbfCount != rd.Count) throw new InvalidDataException($"{s.Name}: SHX {rd.Count} ≠ DBF {rd.DbfCount}");
        var fFrtp = rd.FieldOf("FRTP_CD"); var fKof = rd.FieldOf("KOFTR_GROU"); var fDn = rd.FieldOf("DNST_CD"); var fH = rd.FieldOf("HEIGHT");
        var fDm = rd.FieldOf("DMCLS_CD"); var fAg = rd.FieldOf("AGCLS_CD"); var fFr = rd.FieldOf("FROR_CD"); var fYr = rd.FieldOf("갱신년도"); var fArea = rd.FieldOf("Shape_Area");
        var names = new (string Key, ShpDbfReader.Field? Code, ShpDbfReader.Field? Name)[]
        {
            ("frtp", fFrtp, rd.FieldOf("FRTP_NM")), ("koftr", fKof, rd.FieldOf("KOFTR_NM")), ("dmcls", fDm, rd.FieldOf("DMCLS_NM")),
            ("agcls", fAg, rd.FieldOf("AGCLS_NM")), ("dnst", fDn, rd.FieldOf("DNST_NM")), ("height", fH, rd.FieldOf("HEIGHT_NM")),
        };
        if (fFrtp == null) throw new InvalidDataException($"{s.Name}: FRTP_CD 칸 없음");
        st.Records = rd.Count;
        int done = 0;
        var po = new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, o.Threads) };
        Task<List<ShpDbfReader.Record>>? next = null;
        var lastLog = System.Diagnostics.Stopwatch.StartNew();
        var statLock = new object();
        for (int i0 = 0; i0 < rd.Count; i0 += o.Batch)
        {
            var batch = next != null ? next.Result : rd.ReadBatch(i0, o.Batch);
            int nx = i0 + o.Batch;
            next = nx < rd.Count ? Task.Run(() => rd.ReadBatch(nx, o.Batch)) : null;
            Parallel.ForEach(batch, po, rec =>
            {
                var b = rec.Dbf;
                var attrs = new ForestPoly
                {
                    Src = ((uint)s.Id << 24) | (uint)rec.Index,
                    Frtp = ForestCodes.Frtp(ShpDbfReader.Code(b, fFrtp)), Koftr = ForestCodes.Koftr(ShpDbfReader.Code(b, fKof)), Dnst = ForestCodes.Dnst(ShpDbfReader.Code(b, fDn)),
                    Hgt = ForestCodes.Height(ShpDbfReader.Code(b, fH)), Dm = ForestCodes.Dm(ShpDbfReader.Code(b, fDm)), Ag = ForestCodes.Small(ShpDbfReader.Code(b, fAg)), Fror = ForestCodes.Small(ShpDbfReader.Code(b, fFr)),
                };
                bool unknownFrtp = attrs.Frtp == 255;
                if (unknownFrtp) attrs.Frtp = 0;   // 모르는 FRTP = 나무 안 심는 자리(셈)
                var ro = new RecOut();
                if (rec.ShapeType != 5 || rec.Rings.Count == 0) { ro.Status = 3; ro.Why = "빈 모양 · 형 " + rec.ShapeType; }
                else ProcessRecord(rec.Rings, attrs, o.Simplify, o.SmallPart, ro);
                // 이름표(코드 → 이름) — 처음 본 코드만
                foreach (var (key, cf, nf) in names)
                {
                    if (cf == null || nf == null) continue;
                    string code = ShpDbfReader.Code(b, cf);
                    if (code.Length == 0) continue;
                    lock (codeLock)
                    {
                        if (!st.Codes.TryGetValue(key, out var m)) st.Codes[key] = m = new Dictionary<string, string>();
                        if (!m.ContainsKey(code)) m[code] = ShpDbfReader.Text(b, nf);
                    }
                }
                int.TryParse(ShpDbfReader.Code(b, fYr), out int yr);
                double dbfArea = ShpDbfReader.Number(b, fArea);
                if (ro.Status <= 1)   // 실패한 레코드는 앞 부분 조각도 넣지 않는다(그 레코드 통째로 뺌)
                    foreach (var pc in ro.Pieces)
                    {
                        long key = ((long)pc.Tx << 32) | (uint)pc.Ty;
                        var tb = tiles.GetOrAdd(key, _ => new TileBuf());
                        var enc = ForestTileFormat.EncodePoly(pc.P);
                        lock (tb) tb.Items.Add((pc.P.Order, pc.Piece, enc));
                    }
                lock (statLock)
                {
                    st.PointsIn += rec.Points;
                    if (ro.Status <= 1) st.PointsOut += ro.PtsOut;
                    st.OrphanHoles += ro.Orphans; st.FlatRings += ro.Flat; st.SmallParts += ro.SmallParts; st.Fixed += ro.Fixed; st.AreaSmall += ro.AreaSmall;
                    if (unknownFrtp) st.UnknownFrtp++;
                    if (yr >= 1900 && yr <= 2100) { st.YearMin = Math.Min(st.YearMin, yr); st.YearMax = Math.Max(st.YearMax, yr); }
                    switch (ro.Status)
                    {
                        case 0: st.Kept++; break;
                        case 1: st.Frtp0++; break;
                        case 2: st.Small++; break;
                        default: st.Failed++; if (st.FailList.Count < 200) st.FailList.Add($"{rec.Index}: {ro.Why}"); break;
                    }
                    if (ro.Status <= 1)
                    {
                        int f = Math.Min(4, (int)attrs.Frtp);
                        st.AreaIn[f] += ro.AreaIn; st.AreaSimp[f] += ro.AreaSimp; st.AreaOut[f] += ro.AreaOut;
                        st.Pieces += ro.Pieces.Count;
                        double d = Math.Abs(ro.AreaOut - ro.AreaIn);
                        if (d > 100 && d > 0.01 * ro.AreaIn) { st.FlagCount++; if (st.Flags.Count < 1000) st.Flags.Add(new double[] { rec.Index, Math.Round(ro.AreaIn, 1), Math.Round(ro.AreaOut, 1) }); }
                        if (ro.Boundary)
                        {
                            st.Boundary++; st.BoundaryMax = Math.Max(st.BoundaryMax, ro.BoundaryRel);
                            if (ro.BoundaryUnchecked) st.BoundaryUnchecked++;
                            st.SnapAbs = Math.Max(st.SnapAbs, ro.SnapAbs); st.SnapRel = Math.Max(st.SnapRel, ro.SnapRel);
                            if (ro.BoundaryRel > 1e-4) st.BoundaryOver++;
                            st.BoundaryWorst.Add(new[] { rec.Index, ro.BoundaryA, ro.BoundaryB, ro.BoundaryRel });
                            if (st.BoundaryWorst.Count > 40) { st.BoundaryWorst.Sort((a, b) => b[3].CompareTo(a[3])); st.BoundaryWorst.RemoveRange(10, st.BoundaryWorst.Count - 10); }
                        }
                        if (double.IsFinite(dbfArea) && dbfArea > 0)
                        {
                            double rel = Math.Abs(ro.AreaIn + ro.AreaSmall - dbfArea) / dbfArea;
                            if (rel > 1e-3) st.DbfOver01++;
                            if (rel > 1e-2) st.DbfOver1++;
                            st.DbfMax = Math.Max(st.DbfMax, rel);
                        }
                    }
                }
            });
            done += batch.Count;
            if (lastLog.Elapsed.TotalSeconds >= 10 || done == rd.Count)
            {
                double rps = done / Math.Max(1e-3, sw.Elapsed.TotalSeconds);
                double left = (rd.Count - done) / Math.Max(1e-3, rps);
                o.Log($"  [{s.Name}] {done:N0}/{rd.Count:N0} 레코드 · {rps:N0} 레코드/초 · 이 원본 남은 {Dur(left)}" + (eta != null ? " · " + eta((double)done / rd.Count) : ""));
                lastLog.Restart();
            }
        }
        if (st.YearMin == int.MaxValue) { st.YearMin = 0; st.YearMax = 0; }
        // 조각 쓰기 — 칸 차례(tx, ty) · 칸 안 (src, part, 조각) 차례
        var keys = tiles.Keys.OrderBy(k => (int)(k >> 32)).ThenBy(k => (int)(uint)k).ToList();
        using (var bin = new FileStream(Path.Combine(wd, "pieces.bin"), FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20))
        using (var idx = new StreamWriter(Path.Combine(wd, "pieces.idx"), false, new UTF8Encoding(false)))
        {
            long off = 0;
            foreach (var k in keys)
            {
                var items = tiles[k].Items;
                items.Sort((a, b) => a.Order != b.Order ? a.Order.CompareTo(b.Order) : a.Piece.CompareTo(b.Piece));
                long len = 0;
                foreach (var it in items) { bin.Write(it.Bytes); len += it.Bytes.Length; }
                idx.WriteLine(string.Join(",", (int)(k >> 32), (int)(uint)k, off, len, items.Count));
                off += len;
            }
        }
        st.Tiles = keys.Count;
        st.Seconds = Math.Round(sw.Elapsed.TotalSeconds, 1);
        File.WriteAllText(Path.Combine(wd, "done.json"), JsonSerializer.Serialize(st, JsonOpt), new UTF8Encoding(false));
        return st;
    }

    /// <summary>% 글 — 부호 늘 · 셋째 자리(반올림해 0이면 +0.000).</summary>
    static string Pct(double v)
    {
        double r = Math.Round(v, 3);
        return (r < 0 ? "-" : "+") + Math.Abs(r).ToString("0.000", CultureInfo.InvariantCulture);
    }

    public static string Dur(double sec)
    {
        if (!double.IsFinite(sec) || sec < 0) return "?";
        if (sec < 90) return $"{sec:0}초";
        if (sec < 5400) return $"{sec / 60:0}분";
        return $"{sec / 3600:0.0}시간";
    }

    // ── 합치기 ──

    /// <summary>done인 원본 전부(<c>--only</c>와 상관없이)를 칸 파일로 합치고 index.json을 맨 마지막에.</summary>
    public static ForestIndex Merge(Options o, IReadOnlyList<SourceFile> all, IReadOnlyList<SourceStats> done)
    {
        var map = new SortedDictionary<(int, int), List<(int Id, string Name, long Off, long Len, int N)>>();
        foreach (var st in done.OrderBy(d => d.Id))
        {
            string wd = WorkDir(o.Out, st.Name);
            foreach (var line in File.ReadLines(Path.Combine(wd, "pieces.idx")))
            {
                var p = line.Split(',');
                if (p.Length < 5) continue;
                var key = (int.Parse(p[0], CultureInfo.InvariantCulture), int.Parse(p[1], CultureInfo.InvariantCulture));
                if (!map.TryGetValue(key, out var l)) map[key] = l = new();
                l.Add((st.Id, st.Name, long.Parse(p[2], CultureInfo.InvariantCulture), long.Parse(p[3], CultureInfo.InvariantCulture), int.Parse(p[4], CultureInfo.InvariantCulture)));
            }
        }
        var ix = new ForestIndex { Made = DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture), Simplify = o.Simplify };
        var handles = done.ToDictionary(d => d.Name, d => new FileStream(Path.Combine(WorkDir(o.Out, d.Name), "pieces.bin"), FileMode.Open, FileAccess.Read, FileShare.Read));
        try
        {
            var written = new ConcurrentDictionary<string, long[]>();
            var keys = map.Keys.ToList();
            var hlock = new object();
            Parallel.ForEach(keys, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, o.Threads) }, key =>
            {
                var parts = map[key];
                var chunks = new List<byte[]>();
                int n = 0;
                foreach (var pc in parts)
                {
                    var buf = new byte[pc.Len];
                    lock (hlock)
                    {
                        var fs = handles[pc.Name];
                        fs.Position = pc.Off;
                        fs.ReadExactly(buf);
                    }
                    chunks.Add(buf); n += pc.N;
                }
                var gz = EncodeRaw(key.Item1, key.Item2, chunks, n);
                string path = Path.Combine(o.Out, ForestTileFormat.RelPath(key.Item1, key.Item2));
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                string tmp = path + ".tmp";
                using (var f = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None)) f.Write(gz);
                File.Move(tmp, path, overwrite: true);
                written[ForestTileFormat.Key(key.Item1, key.Item2)] = new long[] { n, gz.Length };
            });
            foreach (var (k, v) in written) ix.Tiles[k] = v;
        }
        finally { foreach (var h in handles.Values) h.Dispose(); }
        // 지난 변환에서 남은 칸(지금 index에 없는 것) 지우기
        string tdir = Path.Combine(o.Out, "tiles");
        if (Directory.Exists(tdir))
            foreach (var f in Directory.GetFiles(tdir, "*.*", SearchOption.AllDirectories))
            {
                string rel = Path.GetRelativePath(tdir, f);
                var seg = rel.Split(Path.DirectorySeparatorChar);
                bool keep = seg.Length == 2 && seg[1].EndsWith(".dhf", StringComparison.Ordinal) && ix.Tiles.ContainsKey(seg[0] + "_" + Path.GetFileNameWithoutExtension(seg[1]));
                if (!keep) File.Delete(f);
            }
        foreach (var st in done.OrderBy(d => d.Id))
        {
            ix.Sources.Add(new ForestIndex.Source
            {
                Name = st.Name, Id = st.Id, Bytes = st.Bytes, Modified = st.Modified, Records = st.Records, Kept = st.Kept, Frtp0 = st.Frtp0, Small = st.Small, Failed = st.Failed,
                AreaIn = st.AreaIn.Sum(), AreaOut = st.AreaOut.Sum(), YearMin = st.YearMin, YearMax = st.YearMax,
            });
            foreach (var (k, m) in st.Codes)
            {
                if (!ix.Codes.TryGetValue(k, out var dst)) ix.Codes[k] = dst = new Dictionary<string, string>();
                foreach (var (c, nm) in m) dst.TryAdd(c, nm);
            }
        }
        ix.Write(Path.Combine(o.Out, "index.json"));
        return ix;
    }

    static byte[] EncodeRaw(int tx, int ty, List<byte[]> chunks, int n)
    {
        using var ms = new MemoryStream();
        using (var gz = new System.IO.Compression.GZipStream(ms, System.IO.Compression.CompressionLevel.Optimal, leaveOpen: true))
        {
            var h = new byte[20];
            Encoding.ASCII.GetBytes(ForestTileFormat.Magic).CopyTo(h, 0);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(h.AsSpan(4), ForestTileFormat.Version);
            System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(h.AsSpan(8), tx);
            System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(h.AsSpan(12), ty);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(h.AsSpan(16), (uint)n);
            gz.Write(h);
            foreach (var c in chunks) gz.Write(c);
        }
        return ms.ToArray();
    }

    // ── 한꺼번에 ──

    /// <summary>build — 고른 원본을 변환(이미 done이고 해시가 같으면 건너뜀 · --force면 다시) → done 전부 합치기 → index.json. 실패가 한도를 넘으면 합치지 않고 멈춤(돌려준 값 null).</summary>
    public static ForestIndex? Build(Options o)
    {
        Directory.CreateDirectory(o.Out);
        var all = FindSources(o.Src);
        if (all.Count == 0) throw new InvalidDataException("원본 SHP가 없습니다 — " + o.Src);
        if (all.Count > 255) throw new InvalidDataException("원본이 255개를 넘습니다");
        var pick = all.Where(s => Picked(s, o.Only)).ToList();
        o.Log($"원본 {all.Count}개 · 이번 {pick.Count}개({string.Join(" ", pick.Select(s => s.Name))}) · 스레드 {o.Threads}");
        var t0 = System.Diagnostics.Stopwatch.StartNew();
        long totalBytes = pick.Sum(s => Stamp(s.Shp).Bytes), doneBytes = 0;
        foreach (var s in pick)
        {
            var have = o.Force ? null : Done(o.Out, s, o);
            if (have != null) { o.Log($"[{s.Name}] 이미 끝남(done.json 해시 같음) — 건너뜀"); doneBytes += have.Bytes; continue; }
            o.Log($"[{s.Name}] 시작 — {s.Shp}");
            long sb = Stamp(s.Shp).Bytes;
            double baseSec = t0.Elapsed.TotalSeconds; long baseBytes = doneBytes;
            var st = ConvertSource(s, o, frac =>
            {
                double gone = baseBytes + frac * sb;
                double el = t0.Elapsed.TotalSeconds;
                return gone > 0 ? $"전체 남은 ≈ {Dur(el / gone * (totalBytes - gone))}" : "";
            });
            doneBytes += sb;
            o.Log($"[{s.Name}] 끝 {Dur(st.Seconds)} — 레코드 {st.Records:N0} = 남김 {st.Kept:N0} + FRTP0 {st.Frtp0:N0} + 작은 부분만 {st.Small:N0} + 실패 {st.Failed:N0} · 고아 구멍 {st.OrphanHoles:N0} · 고침 {st.Fixed:N0} · 점 {st.PointsIn:N0} → {st.PointsOut:N0} · 칸 {st.Tiles:N0}");
            if (st.Failed > o.FailStop * st.Records)
            {
                o.Log($"✗ [{s.Name}] 실패 {st.Failed:N0}건이 한도({o.FailStop:P2})를 넘었습니다 — 멈춤(합치지 않음)");
                foreach (var f in st.FailList.Take(20)) o.Log("    " + f);
                return null;
            }
        }
        var doneAll = all.Select(s => Done(o.Out, s, o)).Where(d => d != null).Cast<SourceStats>().ToList();
        o.Log($"합치기 — done 원본 {doneAll.Count}개({string.Join(" ", doneAll.Select(d => d.Name))})");
        var tm = System.Diagnostics.Stopwatch.StartNew();
        var ix = Merge(o, all, doneAll);
        o.Log($"합치기 끝 {Dur(tm.Elapsed.TotalSeconds)} — 칸 {ix.Tiles.Count:N0} · 전체 {Dur(t0.Elapsed.TotalSeconds)}");
        return ix;
    }

    // ── 검증 보고서(계획 3절 ①~⑤) ──

    public sealed class VerifyResult
    {
        public bool Ok;
        public string Text = "";
        public int Fails, Warns;
    }

    /// <summary>verify — index.json의 칸을 전부 풀어 원본마다 · FRTP마다 넓이를 done.json과 견주고 보고서(<c>report.txt · report.json</c>)를 창고에 쓴다.</summary>
    public static VerifyResult Verify(string outDir, int threads, Action<string>? log = null)
    {
        log ??= Console.WriteLine;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var ix = ForestIndex.Read(Path.Combine(outDir, "index.json"));
        var stats = new Dictionary<int, SourceStats>();
        foreach (var s in ix.Sources)
        {
            string dj = Path.Combine(WorkDir(outDir, s.Name), "done.json");
            if (File.Exists(dj)) stats[s.Id] = JsonSerializer.Deserialize<SourceStats>(File.ReadAllText(dj), JsonOpt)!;
        }
        int nSrc = 256;
        var area = new double[nSrc, 5];
        var polys = new long[nSrc];
        long pts = 0, diskBytes = 0;
        var bad = new ConcurrentBag<string>();
        string biggest = ""; long bigBytes = 0;
        var lk = new object();
        Parallel.ForEach(ix.Tiles, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, threads) }, kv =>
        {
            var p = kv.Key.Split('_');
            int tx = int.Parse(p[0], CultureInfo.InvariantCulture), ty = int.Parse(p[1], CultureInfo.InvariantCulture);
            string path = Path.Combine(outDir, ForestTileFormat.RelPath(tx, ty));
            ForestTile t;
            byte[] gz;
            try { gz = File.ReadAllBytes(path); t = ForestTileFormat.DecodeTile(gz); }
            catch (Exception ex) { bad.Add($"{kv.Key}: 못 읽음 — {ex.GetType().Name}"); return; }
            if (t.Tx != tx || t.Ty != ty) bad.Add($"{kv.Key}: 머리 칸 번호 다름");
            if (t.Polys.Count != kv.Value[0] || gz.Length != kv.Value[1]) bad.Add($"{kv.Key}: index 수 · 바이트 다름");
            var la = new double[nSrc, 5]; var lp = new long[nSrc]; long lpts = 0;
            long prev = long.MinValue;
            foreach (var q in t.Polys)
            {
                if (q.Order < prev) bad.Add($"{kv.Key}: 차례 어긋남");
                prev = q.Order;
                if (q.Rings.Count == 0 || ForestTileFormat.RingArea(q.Rings[0]) <= 0) bad.Add($"{kv.Key}: 바깥 고리 방향 · 넓이");
                for (int k = 1; k < q.Rings.Count; k++) if (ForestTileFormat.RingArea(q.Rings[k]) >= 0) { bad.Add($"{kv.Key}: 구멍 방향"); break; }
                la[q.Source, Math.Min(4, (int)q.Frtp)] += ForestTileFormat.PolyArea(q);
                lp[q.Source]++;
                lpts += q.Rings.Sum(r => r.Length / 2);
            }
            lock (lk)
            {
                for (int s = 0; s < nSrc; s++) { polys[s] += lp[s]; for (int f = 0; f < 5; f++) area[s, f] += la[s, f]; }
                pts += lpts; diskBytes += gz.Length;
                if (gz.Length > bigBytes) { bigBytes = gz.Length; biggest = kv.Key; }
            }
        });
        var sb = new StringBuilder();
        int fails = 0, warns = 0;
        void Line(string s) { sb.AppendLine(s); log(s); }
        void Fail(string s) { fails++; Line("✗ " + s); }
        void Warn(string s) { warns++; Line("⚠ " + s); }
        Line($"임상도 타일 검증 — {ix.Made} · 원본 {ix.Sources.Count} · 칸 {ix.Tiles.Count:N0}");
        foreach (var b in bad.Take(30)) Fail(b);
        if (bad.Count > 30) Fail($"… 칸 이상 모두 {bad.Count}건");
        // ① 레코드 셈
        Line("\n① 원본마다 들어옴 = 남김 + FRTP0 + 작은 부분만 + 실패");
        long recAll = 0, failAll = 0;
        foreach (var s in ix.Sources)
        {
            if (!stats.TryGetValue(s.Id, out var st)) { Fail($"{s.Name}: done.json 없음"); continue; }
            recAll += st.Records; failAll += st.Failed;
            bool sum = st.Records == st.Kept + st.Frtp0 + st.Small + st.Failed;
            string l = $"  {st.Name}: {st.Records:N0} = {st.Kept:N0} + {st.Frtp0:N0} + {st.Small:N0} + {st.Failed:N0} · 작은 부분 {st.SmallParts:N0}({st.AreaSmall / 1e4:N2} ha) · 고아 구멍 {st.OrphanHoles:N0} · 넓이 0 고리 {st.FlatRings:N0} · 고침 {st.Fixed:N0} · 모르는 FRTP {st.UnknownFrtp:N0} · {Dur(st.Seconds)}";
            if (!sum) Fail(l + " — 셈이 안 맞음");
            else if (st.Failed > 1e-4 * st.Records) Fail(l + " — 실패 0.01 % 넘음");
            else if (st.Failed > 0) Warn(l + $" — 실패 {st.Failed}");
            else Line("  ✓" + l[1..]);
            foreach (var f in st.FailList.Take(5)) Line("      " + f);
        }
        // ② 넓이
        Line("\n② 원래 넓이 ↔ 칸 넓이 합(원본마다 · FRTP마다 ±0.5 %) · [솎은 뒤 대비] = 자르기 · 격자 맞춤만(±0.5 %) · 칸 = 조각(done)과 같아야");
        string[] fn = { "무립목", "침엽", "활엽", "혼효", "죽림" };
        var frAll = new double[5, 3];
        foreach (var s in ix.Sources)
        {
            if (!stats.TryGetValue(s.Id, out var st)) continue;
            var cells = new List<string>();
            bool ok = true;
            for (int f = 0; f < 5; f++)
            {
                double a0 = st.AreaIn[f], am = st.AreaSimp[f], a1 = area[s.Id, f];
                frAll[f, 0] += a0; frAll[f, 1] += am; frAll[f, 2] += a1;
                if (a0 <= 0 && a1 <= 0) continue;
                double rel = a0 > 0 ? (a1 - a0) / a0 : 1, relM = am > 0 ? (a1 - am) / am : 1;
                bool bad1 = Math.Abs(rel) > 0.005, bad2 = Math.Abs(relM) > 0.005, bad3 = Math.Abs(a1 - st.AreaOut[f]) > 1e-6 * Math.Max(1, a1);
                if (bad1 || bad2 || bad3) ok = false;
                cells.Add($"{fn[f]} {a0 / 1e4:N1}→{a1 / 1e4:N1} ha({Pct(rel * 100)}%{(bad1 ? "✗" : "")} · 솎은 뒤 대비 {Pct(relM * 100)}%{(bad2 ? "✗" : "")}{(bad3 ? " · 칸≠조각✗" : "")})");
            }
            string l = $"  {st.Name}: " + string.Join(" · ", cells) + $" · M1 넓이 목록 {st.FlagCount:N0}건";
            if (ok) Line(l); else Fail(l + " — ±0.5 % 넘음");
        }
        Line("  전국: " + string.Join(" · ", Enumerable.Range(0, 5).Select(f => $"{fn[f]} {frAll[f, 0] / 1e4:N0}→{frAll[f, 2] / 1e4:N0} ha({Pct(frAll[f, 0] > 0 ? (frAll[f, 2] - frAll[f, 0]) / frAll[f, 0] * 100 : 0)}% · 솎은 뒤 대비 {Pct(frAll[f, 1] > 0 ? (frAll[f, 2] - frAll[f, 1]) / frAll[f, 1] * 100 : 0)}%)")));
        // ③ 칸 경계
        Line("\n③ 칸 경계에 걸친 레코드 — 자른 조각 합 = 솎은 넓이 ±0.01 %");
        int bnd = stats.Values.Sum(s => s.Boundary), bndOver = stats.Values.Sum(s => s.BoundaryOver);
        double bndMax = stats.Values.Select(s => s.BoundaryMax).DefaultIfEmpty(0).Max();
        int bndUn = stats.Values.Sum(s => s.BoundaryUnchecked);
        string bl = $"  걸친 레코드 {bnd:N0}(전수 · 실수 자르기) · 최대 차 {bndMax * 100:0.00000}% · 0.01 % 넘음 {bndOver:N0}" + (bndUn > 0 ? $" · 실수 자르기 위상 예외로 못 잰 것 {bndUn:N0}" : "");
        if (bndOver > 0) Fail(bl); else Line(bl);
        Line($"  (알림) 저장 조각은 자른 마디를 0.1 m 정수로 맞춤 — 그 영향 레코드마다 최대 {stats.Values.Select(s => s.SnapAbs).DefaultIfEmpty(0).Max():N2} ㎡ · 최대 {stats.Values.Select(s => s.SnapRel).DefaultIfEmpty(0).Max() * 100:0.000}%(합계는 ②의 «솎은 뒤 대비»)");
        foreach (var st in stats.Values.OrderBy(s => s.Id))
            foreach (var w in st.BoundaryWorst.OrderByDescending(w => w[3]).Take(3).Where(w => w[3] > 1e-4))
                Line($"      {st.Name} 레코드 {w[0]:0} · 통 {w[1]:N2} ㎡ · 조각 합 {w[2]:N2} ㎡ · {w[3] * 100:0.0000}%");
        // DBF 넓이(경고만 — L10)
        int d01 = stats.Values.Sum(s => s.DbfOver01), d1 = stats.Values.Sum(s => s.DbfOver1);
        Line($"  (알림) DBF Shape_Area ↔ 원래 넓이: 0.1 % 넘음 {d01:N0} · 1 % 넘음 {d1:N0} · 최대 {stats.Values.Select(s => s.DbfMax).DefaultIfEmpty(0).Max() * 100:0.###}%");
        // ④ 크기
        long ptsIn = stats.Values.Sum(s => s.PointsIn), ptsOut = stats.Values.Sum(s => s.PointsOut);
        Line($"\n④ 점 {ptsIn:N0} → {ptsOut:N0}({(ptsIn > 0 ? 100.0 * ptsOut / ptsIn : 0):0.0}%) · 칸 점 {pts:N0} · 칸 {ix.Tiles.Count:N0} · 창고 칸 바이트 {diskBytes / 1048576.0:N1} MB · 가장 큰 칸 {biggest} {bigBytes / 1048576.0:N2} MB · 폴리곤 {polys.Sum():N0}");
        if (pts != ptsOut) Fail($"칸 점 수 {pts:N0} ≠ 조각 점 수 {ptsOut:N0}");
        // ⑤ 겹침 표본
        var keys = ix.Tiles.Keys.ToList();
        int step = Math.Max(1, keys.Count / 200);
        double ovSum = 0, ovUnion = 0; int ovTiles = 0, ovErr = 0;
        var sample = keys.Where((k, i) => i % step == 0).Take(200).ToList();
        var ovLock = new object();
        Parallel.ForEach(sample, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, threads) }, k =>
        {
            var p = k.Split('_');
            var t = ForestTileFormat.DecodeTile(File.ReadAllBytes(Path.Combine(outDir, ForestTileFormat.RelPath(int.Parse(p[0], CultureInfo.InvariantCulture), int.Parse(p[1], CultureInfo.InvariantCulture)))));
            var g = t.Polys.Where(q => q.Frtp >= 1 && q.Frtp <= 4).Select(q => ForestTileFormat.ToPolygon(t, q, Gf10)).Where(q => q != null).Cast<Geometry>().ToList();
            if (g.Count == 0) return;
            double s0 = g.Sum(q => q.Area);
            try
            {
                double u = OverlayNGRobust.Union(g, Gf10).Area;
                lock (ovLock) { ovSum += s0; ovUnion += u; ovTiles++; }
            }
            catch (Exception) { lock (ovLock) ovErr++; }
        });
        Line($"\n⑤ 표본 칸 {ovTiles}개 숲 폴리곤 겹친 넓이 비율(알림) {(ovSum > 0 ? (ovSum - ovUnion) / ovSum * 100 : 0):0.0000}%" + (ovErr > 0 ? $" · 합치기 실패 {ovErr}" : ""));
        Line($"\n판정: {(fails == 0 ? "통과" : $"실패 {fails}건")} · 경고 {warns} · 검증 {Dur(sw.Elapsed.TotalSeconds)}");
        File.WriteAllText(Path.Combine(outDir, "report.txt"), sb.ToString(), new UTF8Encoding(false));
        var json = new Dictionary<string, object>
        {
            ["made"] = ix.Made, ["ok"] = fails == 0, ["fails"] = fails, ["warns"] = warns, ["tiles"] = ix.Tiles.Count, ["diskBytes"] = diskBytes, ["biggest"] = new object[] { biggest, bigBytes },
            ["pointsIn"] = ptsIn, ["pointsOut"] = ptsOut, ["boundary"] = new object[] { bnd, bndMax, bndOver }, ["overlap"] = new object[] { ovTiles, ovSum, ovUnion },
            ["sources"] = stats.Values.OrderBy(s => s.Id).Select(s => (object)new Dictionary<string, object>
            {
                ["name"] = s.Name, ["records"] = s.Records, ["kept"] = s.Kept, ["frtp0"] = s.Frtp0, ["small"] = s.Small, ["failed"] = s.Failed, ["orphans"] = s.OrphanHoles,
                ["areaIn"] = s.AreaIn, ["areaSimp"] = s.AreaSimp, ["areaTiles"] = Enumerable.Range(0, 5).Select(f => area[s.Id, f]).ToArray(), ["flags"] = s.Flags, ["flagCount"] = s.FlagCount, ["fail"] = s.FailList,
                ["dbf"] = new object[] { s.DbfOver01, s.DbfOver1, s.DbfMax }, ["seconds"] = s.Seconds,
            }).ToList(),
            ["bad"] = bad.Take(200).ToList(),
        };
        File.WriteAllText(Path.Combine(outDir, "report.json"), JsonSerializer.Serialize(json, JsonOpt), new UTF8Encoding(false));
        return new VerifyResult { Ok = fails == 0, Text = sb.ToString(), Fails = fails, Warns = warns };
    }

    /// <summary>at — 그 점(5179)을 담는 폴리곤 글.</summary>
    public static List<string> At(string outDir, double x, double y)
    {
        var ix = ForestIndex.Read(Path.Combine(outDir, "index.json"));
        int tx = ForestTileFormat.TileOf(x), ty = ForestTileFormat.TileOf(y);
        var o = new List<string>();
        if (!ix.Tiles.ContainsKey(ForestTileFormat.Key(tx, ty))) { o.Add($"칸 {tx}_{ty} 없음(숲 없음)"); return o; }
        var t = ForestTileFormat.DecodeTile(File.ReadAllBytes(Path.Combine(outDir, ForestTileFormat.RelPath(tx, ty))));
        var pt = GfF.CreatePoint(new Coordinate(x, y));
        string N(string k, string c) => ix.Codes.TryGetValue(k, out var m) && m.TryGetValue(c, out var n) ? n : "";
        foreach (var q in t.Polys)
        {
            var pg = ForestTileFormat.ToPolygon(t, q, GfF);
            if (pg == null || !pg.Covers(pt)) continue;
            string src = ix.Sources.FirstOrDefault(s => s.Id == q.Source)?.Name ?? q.Source.ToString(CultureInfo.InvariantCulture);
            o.Add($"원본 {src} 레코드 {q.Record} 부분 {q.Part} · FRTP {q.Frtp}({N("frtp", q.Frtp.ToString(CultureInfo.InvariantCulture))}) · 수종 {q.Koftr}({N("koftr", q.Koftr.ToString(CultureInfo.InvariantCulture))}) · 밀도 {q.Dnst} · 키 {q.Hgt} · 경급 {q.Dm} · 영급 {q.Ag} · 임종 {q.Fror} · 넓이 {ForestTileFormat.PolyArea(q):N1} ㎡");
        }
        if (o.Count == 0) o.Add($"칸 {tx}_{ty} 안 · 담는 폴리곤 없음");
        return o;
    }
}
