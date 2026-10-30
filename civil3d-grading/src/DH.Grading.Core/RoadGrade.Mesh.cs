using NetTopologySuite.Geometries;

namespace DH.Grading.Core;

/// <summary>★★★[v111 · 계획_v111.md 2.5] 횡단 → 세모 — 이웃 횡단 사이 띠 다각형(도로 면 · 양쪽 사면) · 끝 마구리 사슬 · 맞닿는 둘레 점 맞춤 · 검사.
/// <para>같은 꼴 이웃 광선은 네모마다 짧은 대각선, 꼴이 다르면(단 수 바뀜 · 기록 경계 끝면 · 절성 바뀜) NTS 제약 들로네(WallDaylight와 같은 길).
/// 둘레에 남의 꼭짓점이 놓이면(끝 선 위 끝 마구리 광선 뿌리 등) 그 점을 둘레에 끼운 뒤 가른다 — T자 없음.</para></summary>
public static partial class RoadGrade
{
    internal sealed class MeshReport
    {
        public int Polys, Quads, Cdt, CdtFail, Inserted, Tris, FlatDropped;
        public int NonManifold, Flipped, TJunctions, OpenEnds, OverlapHits, Holes, Thin; public double Area;
        public string ByPart = "";
        public bool Ok => CdtFail == 0 && NonManifold == 0 && Flipped == 0 && TJunctions == 0 && OverlapHits == 0 && Thin == 0 && OpenEnds == 0;
        public string Text => $"세모 {Tris:N0}(네모 {Quads:N0} · 들로네 {Cdt:N0}" + (CdtFail > 0 ? $" · ⚠못 가름 {CdtFail}" : "") + (Inserted > 0 ? $" · 둘레 끼움 {Inserted}" : "") + (FlatDropped > 0 ? $" · 넓이 0 뺌 {FlatDropped}" : "") + (Holes > 0 ? $" · 속 구멍 메움 {Holes}" : "") + (Thin > 0 ? $" · ⚠바늘 {Thin}" : "") + ")"
                              + $" · 넓이 {Area:N0} ㎡" + (Ok ? " · 검사 통과" : $" · ⚠검사 — 세 세모 이상 변 {NonManifold} · 뒤집힘 {Flipped} · T자 {TJunctions} · 겹침 {OverlapHits} · 주름 틈 {OpenEnds}");
    }

    /// <summary>꼭짓점 표 — 평면 0.5 mm 안 점은 하나로(Civil이 0.1 mm 안 점을 합치므로 · 이분 정밀도로 생긴 곁 점 · 이웃 광선이 나눠 가진 점).</summary>
    /// <summary>레일에 이웃 거리를 끼우지 않는 간격(m) — 그 안이면 내 점이 짝.</summary>
    const double RailGap = 0.02;

    /// <summary>하네스 전용 계측(출하 DLL 밖에서 못 건드리게 internal).</summary>
    internal static Action<string>? DebugMesh;

    sealed class VertTable
    {
        public readonly List<double> X = new(), Y = new(), Z = new();
        const double Merge = 5e-4, Cell = 1e-3;
        readonly Dictionary<(long, long), List<int>> _g = new();
        readonly Dictionary<(long, long), int> _exact = new();
        public int Id(double x, double y, double z)
        {
            long gx = (long)Math.Floor(x / Cell), gy = (long)Math.Floor(y / Cell);
            for (long a = gx - 1; a <= gx + 1; a++)
                for (long b = gy - 1; b <= gy + 1; b++)
                    if (_g.TryGetValue((a, b), out var l))
                        foreach (int v in l) if (Sq(X[v] - x) + Sq(Y[v] - y) <= Merge * Merge) return v;
            int id = X.Count; X.Add(x); Y.Add(y); Z.Add(z);
            if (!_g.TryGetValue((gx, gy), out var c)) _g[(gx, gy)] = c = new();
            c.Add(id);
            _exact[(BitConverter.DoubleToInt64Bits(x), BitConverter.DoubleToInt64Bits(y))] = id;
            return id;
        }
        /// <summary>NTS가 돌려준 좌표(넣은 꼭짓점 그대로) → 번호.</summary>
        public bool TryId(double x, double y, out int id) => _exact.TryGetValue((BitConverter.DoubleToInt64Bits(x), BitConverter.DoubleToInt64Bits(y)), out id);
    }

    /// <summary>★★[v111 · 계획 2.4] 선 기반 계단선(사면선 · 소단선 · 옹벽선) — 결과의 데이라잇 · 가장자리도 횡단 것으로 바꾼다. 못 하면(예외) 옛 칸 등치선.</summary>
    public static List<TierLine> ExactLines(Input inp, Result r, out string note)
    {
        if (r.Error.Length > 0 || r.Eval == null) { note = ""; return r.Error.Length > 0 ? new() : TierLines(r); }
        try
        {
            var ctx = new RayCtx(inp, r);
            var set = BuildSections(ctx);
            var lines = SectionLines(ctx, set, out var dl, out var eL, out var eR);
            r.Sections = set;
            r.Daylight = dl.Select(l => l.Select(p => (p.X, p.Y)).ToList()).ToList();
            r.DaylightZ = dl;
            r.EdgeL = eL; r.EdgeR = eR;
            note = $"횡단 {set.X.Count}(세분 {set.Refined} · 뺌 {set.Removed}) · {set.Ms:F0} ms" + CapNote(set);
            return lines;
        }
        catch (Exception ex)
        {
            note = "⚠선 계산 실패 → 칸 등치선 — " + Why(ex);
            return TierLines(r);
        }
    }

    /// <summary>★[v111.2 · 전체 검토 L] 병렬 계산의 예외는 AggregateException으로 와서 «One or more errors occurred»만 남음 — 안의 첫 까닭.</summary>
    static string Why(Exception ex) => ex is AggregateException ae && ae.Flatten().InnerExceptions.Count > 0 ? ae.Flatten().InnerExceptions[0].Message : ex.Message;

    /// <summary>★[v111.2 · 전체 검토 L] 세분 한도(횡단 4만 · 마구리 광선 2천)에 걸렸으면 알림 — 꼴 바뀜이 덜 나뉜 자리가 남음.</summary>
    static string CapNote(SectionSet set) => set.Capped > 0 ? " · ⚠세분 한도에 걸림(아주 길거나 복잡한 도로 — 몇 군데 덜 나뉨)" : "";

    /// <summary>★★★[v111 · 계획 2.5] 선 기반 도로 면 — 횡단 띠 세모 · 검사 통과 못 하면(또는 예외) 옛 칸 세모(<see cref="SurfaceTris"/>)로 물러섬 + 알림.</summary>
    public static List<WallDaylight.Tri> ExactSurface(Input inp, Result r, out string note, out List<TierLine> lines)
    {
        lines = new();
        try
        {
            if (r.Eval == null) throw new InvalidOperationException("점 평가기 없음");
            var ctx = new RayCtx(inp, r);
            var set = BuildSections(ctx);
            lines = SectionLines(ctx, set, out var dl, out var eL, out var eR);
            r.Sections = set; r.DaylightZ = dl; r.Daylight = dl.Select(l => l.Select(p => (p.X, p.Y)).ToList()).ToList(); r.EdgeL = eL; r.EdgeR = eR;
            var tris = MeshFromSections(set, inp.Base, out var rep);
            if (rep.Ok && tris.Count > 0)
            {
                note = $"선 면 — 횡단 {set.X.Count}(세분 {set.Refined} · 뺌 {set.Removed}) · {rep.Text} · {set.Ms:F0} ms" + CapNote(set);
                return tris;
            }
            note = $"⚠선 면 검사 못 넘음({rep.Text}) → 칸 면";
        }
        catch (Exception ex) { note = "⚠선 면 실패 → 칸 면 — " + Why(ex); }
        var old = SurfaceTris(r, out string n2);
        if (lines.Count == 0) lines = TierLines(r);
        note += " · " + n2;
        return old;
    }

    internal static List<WallDaylight.Tri> MeshFromSections(SectionSet set, IGroundSurface ground, out MeshReport rep)
    {
        var rp = new MeshReport(); rep = rp;
        var vt = new VertTable();
        List<int> Ids(IEnumerable<RayPt> pts) => pts.Select(p => vt.Id(p.X, p.Y, p.Z)).ToList();
        // ── 광선 이웃(같은 쪽 횡단 차례 · 끝 마구리 사슬) ──
        var nb = new Dictionary<Ray, List<Ray>>(ReferenceEqualityComparer.Instance);
        var links = new List<(Ray A, Ray B)>();
        List<Ray> Nb(Ray r) { if (!nb.TryGetValue(r, out var l)) nb[r] = l = new(); return l; }
        void Link(Ray a, Ray b) { if (ReferenceEquals(a, b)) return; links.Add((a, b)); Nb(a).Add(b); Nb(b).Add(a); }
        for (int i = 0; i + 1 < set.X.Count; i++) { Link(set.X[i].L, set.X[i + 1].L); Link(set.X[i].R, set.X[i + 1].R); }
        foreach (var cap in set.Caps) for (int i = 0; i + 1 < cap.Count; i++) Link(cap[i], cap[i + 1]);
        // ── 레일 — 내 점 + 이웃의 꺾임 거리(내 꺾은선 위 보간 · 이웃 띠와 같은 점 = 맞닿음) + 데이라잇 너머 땅(이웃이 더 길면 그 거리마다 · 바늘 세모 막음 · 계획 검토 M8) ──
        var rails = new Dictionary<Ray, List<RayPt>>(ReferenceEqualityComparer.Instance);
        foreach (var kv in nb) rails[kv.Key] = Rail(kv.Key, kv.Value, ground);
        // ★[설치 전 검토 M2] 주인이 바뀌어 끊긴 광선 끝(주름 · 충돌) — 메운 뒤에도 둘레에 남으면 틈(칸 면으로 물러섬) · 속 구멍은 이 점이 낀 것만 메움(진짜 땅 섬은 안 덮음)
        var creaseV = new HashSet<int>();
        foreach (var r0 in nb.Keys) if (r0.End == RayEnd.Crease && r0.Pts.Count > 0) { var p = r0.Pts[^1]; creaseV.Add(vt.Id(p.X, p.Y, p.Z)); }
        // ★ 땅 따라 늘인 점은 가장자리가 붙은(15 cm 안 — 세분 · 기록 경계 짝) 이웃 띠에서만 — 먼 이웃까지 늘이면 면이 데이라잇 밖 땅을 덮는다
        // 붙은 두 광선 = 함께 가는 길이 내내 옆 간격 15 cm 안(세분 무리 · 기록 경계 짝) — 한 점에서 퍼지는 부채는 끝에서 벌어지므로 아님
        static bool Close(Ray a, Ray b)
        {
            if (Sq(a.Ex - b.Ex) + Sq(a.Ey - b.Ey) > 0.15 * 0.15) return false;
            double d = Math.Min(End(a), End(b));
            double ax = a.Ex + a.Nx * d, ay = a.Ey + a.Ny * d, bx = b.Ex + b.Nx * d, by = b.Ey + b.Ny * d;
            return Sq(ax - bx) + Sq(ay - by) <= 0.15 * 0.15;
        }
        // ★ 붙은 광선끼리는 서로의 레일 거리를 다 나눠 갖는다(내 길이 안 · 보간) — 세분 무리에서 건너온 꺾임이 다음 광선에서 짝 없이 바늘이 되지 않게
        for (int pass = 0; pass < 64; pass++)
        {
            bool changed = false;
            foreach (var (ra, rb) in links)
            {
                if (!Close(ra, rb)) continue;
                foreach (var (dst, src) in new[] { (rails[ra], rails[rb]), (rails[rb], rails[ra]) })
                {
                    double own = dst.Where(p => p.K != -2).Select(p => p.D).DefaultIfEmpty(0).Max();
                    foreach (var q in src.ToList())
                    {
                        if (q.D <= 1e-4 || q.D >= own - 1e-4 || dst.Any(p => Math.Abs(p.D - q.D) < RailGap)) continue;
                        int k = dst.FindIndex(p => p.D > q.D);
                        if (k <= 0) continue;
                        var a = dst[k - 1]; var b = dst[k]; double t = (q.D - a.D) / (b.D - a.D);
                        dst.Insert(k, new RayPt(q.D, a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t, a.Z + (b.Z - a.Z) * t, -3));
                        changed = true;
                    }
                }
            }
            if (!changed) break;
        }
        static double End(Ray r) => r.Pts.Count == 0 ? 0 : r.Pts[^1].D;
        var triIds = new List<(int, int, int)>();
        var tags = new List<string>(); string curTag = "";
        void AddT((int, int, int) t) { triIds.Add(t); tags.Add(curTag); }
        // 두 꺾은선(매개 오름차순)을 지퍼처럼 — 매개가 덜 나간 쪽을 한 칸씩
        void Zip(List<int> a, List<double> ta, List<int> b, List<double> tb, bool openEnd = false)
        {
            int i = 0, j = 0;
            // 같은 점에서 나온 두 꺾은선(모서리 부채 · 끝 선 모서리) — 첫 세모를 (뿌리, a1, b1)로 · 그래야 한 꺾은선 위 점들이 뿌리와 세모(넓이 0 · 바늘 · T자)를 안 만든다
            if (a.Count > 1 && b.Count > 1 && a[0] == b[0]) { AddT((a[0], a[1], b[1])); i = 1; j = 1; }
            while (i < a.Count - 1 || j < b.Count - 1)
            {
                // ★ 한쪽이 먼저 끝났고 두 끝 사이가 바깥 둘레(데이라잇)면 — 남은 점을 그 끝에 부채로 잇지 않고 두 끝을 이은 선분 위 점(같은 비율)과 지퍼로(바늘 막음)
                if (openEnd && (i >= a.Count - 1 || j >= b.Count - 1) && (a.Count - 1 - i) + (b.Count - 1 - j) >= 2)
                {
                    bool aDone = i >= a.Count - 1;
                    var rest = aDone ? b : a; var tr = aDone ? tb : ta; int k0 = aDone ? j : i; int stop = aDone ? a[^1] : b[^1];
                    double t0 = tr[k0], t1 = tr[^1];
                    var chord = new List<int>();
                    for (int k = k0; k < rest.Count - 1; k++)
                    {
                        double w = t1 > t0 ? (tr[k] - t0) / (t1 - t0) : 0;
                        double x = vt.X[stop] + (vt.X[rest[^1]] - vt.X[stop]) * w, y = vt.Y[stop] + (vt.Y[rest[^1]] - vt.Y[stop]) * w, z = vt.Z[stop] + (vt.Z[rest[^1]] - vt.Z[stop]) * w;
                        chord.Add(k == k0 ? stop : vt.Id(x, y, z));
                    }
                    chord.Add(rest[^1]);
                    for (int k = 0; k + 1 < chord.Count; k++)
                    {
                        int r0 = rest[k0 + k], r1 = rest[k0 + k + 1], c0 = chord[k], c1 = chord[k + 1];
                        AddT((r0, c0, r1));
                        if (c1 != r1) AddT((r1, c0, c1));
                    }
                    return;
                }
                bool stepA;
                if (i >= a.Count - 1) stepA = false;
                else if (j >= b.Count - 1) stepA = true;
                else stepA = ta[i + 1] < tb[j + 1] - 1e-12 || (Math.Abs(ta[i + 1] - tb[j + 1]) <= 1e-12 && Sq(vt.X[a[i + 1]] - vt.X[b[j]]) + Sq(vt.Y[a[i + 1]] - vt.Y[b[j]]) <= Sq(vt.X[b[j + 1]] - vt.X[a[i]]) + Sq(vt.Y[b[j + 1]] - vt.Y[a[i]]));
                if (stepA) { AddT((a[i], b[j], a[i + 1])); i++; }
                else { AddT((a[i], b[j], b[j + 1])); j++; }
            }
        }
        foreach (var (ra, rb) in links)
        {
            double dmax = (Close(ra, rb) ? Math.Max(End(ra), End(rb)) : 0) + 1e-9;
            var pa = rails[ra].Where(p => p.K != -2 ? true : p.D <= dmax).ToList(); var pb = rails[rb].Where(p => p.K != -2 ? true : p.D <= dmax).ToList();
            if (pa.Count == 0 || pb.Count == 0) continue;
            static bool Outer(Ray r) => r.End is RayEnd.Daylight or RayEnd.EdgeWall or RayEnd.Reach or RayEnd.NoGround;
            curTag = $"띠 쪽{ra.Side}/{rb.Side} s {ra.S:F3}/{rb.S:F3} 끝{ra.EndCap}/{rb.EndCap} 끝종류 {ra.End}/{rb.End} 점 {pa.Count}/{pb.Count}";
            Zip(Ids(pa), pa.Select(p => p.D).ToList(), Ids(pb), pb.Select(p => p.D).ToList(), Outer(ra) && Outer(rb) && !Close(ra, rb));
            rp.Polys++;
        }
        // 도로 면 — 횡단마다 옆 자리(좌 − · 우 +) 차례 · 끝 횡단은 끝 마구리 광선 뿌리까지 · 이웃 횡단의 옆 자리를 내 꺾은선 위에 보간(짝 맞춤 · 바늘 막음)
        List<(double Lat, double X, double Y, double Z)> DeckOwn(int i)
        {
            var x = set.X[i]; var d = x.Deck;
            var sec = set.Sec;
            // ★[설치 전 검토 M1] MakeX가 점을 넣는 조건 그대로(한쪽 길어깨만 있어도)
            var lats = new List<double> { -sec.OuterL };
            if (sec.ShoulderL > 1e-6) lats.Add(-sec.HalfL);
            lats.Add(0);
            if (sec.ShoulderR > 1e-6) lats.Add(sec.HalfR);
            lats.Add(sec.OuterR);
            var o = new List<(double, double, double, double)>();
            for (int k = 0; k < d.Count && k < lats.Count; k++) o.Add((lats[k], d[k].X, d[k].Y, d[k].Z));
            if (i == 0 || i == set.X.Count - 1)
                foreach (var q in set.Caps[i == 0 ? 0 : 1])
                    if (q.Side == 0 && q.EndCap >= 0 && !o.Any(p => Math.Abs(p.Item1 - q.Lat) < 1e-6)) o.Add((q.Lat, q.Ex, q.Ey, q.Ez));
            return o.OrderBy(p => p.Item1).ToList();
        }
        var deckOwn = Enumerable.Range(0, set.X.Count).Select(DeckOwn).ToList();
        List<(double Lat, double X, double Y, double Z)> DeckRail(int i)
        {
            var o = new List<(double Lat, double X, double Y, double Z)>(deckOwn[i]);
            foreach (int j in new[] { i - 1, i + 1 })
            {
                if (j < 0 || j >= set.X.Count) continue;
                foreach (var q in deckOwn[j])
                {
                    if (o.Any(p => Math.Abs(p.Lat - q.Lat) < RailGap) || q.Lat <= o[0].Lat || q.Lat >= o[^1].Lat) continue;
                    int k = o.FindIndex(p => p.Lat > q.Lat);
                    var a = o[k - 1]; var b = o[k]; double t = (q.Lat - a.Lat) / (b.Lat - a.Lat);
                    o.Insert(k, (q.Lat, a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t, a.Z + (b.Z - a.Z) * t));
                }
            }
            return o;
        }
        var deckRail = Enumerable.Range(0, set.X.Count).Select(DeckRail).ToList();
        // ★ 붙은 횡단(15 cm 안 — 세분 무리)끼리는 서로의 옆 자리를 다 나눠 갖는다(한 번 건너온 옆 자리가 다음 붙은 횡단에서 짝 없이 바늘이 되지 않게)
        for (int pass = 0; pass < 64; pass++)
        {
            bool changed = false;
            for (int i = 0; i + 1 < set.X.Count; i++)
            {
                if (Sq(set.X[i].L.Ex - set.X[i + 1].L.Ex) + Sq(set.X[i].L.Ey - set.X[i + 1].L.Ey) > 0.15 * 0.15) continue;
                foreach (var (dst, src) in new[] { (deckRail[i], deckRail[i + 1]), (deckRail[i + 1], deckRail[i]) })
                    foreach (var q in src)
                    {
                        if (dst.Any(p => Math.Abs(p.Lat - q.Lat) < RailGap) || q.Lat <= dst[0].Lat || q.Lat >= dst[^1].Lat) continue;
                        int k = dst.FindIndex(p => p.Lat > q.Lat);
                        var a = dst[k - 1]; var b = dst[k]; double t = (q.Lat - a.Lat) / (b.Lat - a.Lat);
                        dst.Insert(k, (q.Lat, a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t, a.Z + (b.Z - a.Z) * t));
                        changed = true;
                    }
            }
            if (!changed) break;
        }
        for (int i = 0; i + 1 < set.X.Count; i++)
        {
            var ra = deckRail[i]; var rb = deckRail[i + 1];
            curTag = $"도로 면 {i}";
            Zip(ra.Select(p => vt.Id(p.X, p.Y, p.Z)).ToList(), ra.Select(p => p.Lat).ToList(), rb.Select(p => vt.Id(p.X, p.Y, p.Z)).ToList(), rb.Select(p => p.Lat).ToList());
            rp.Polys++;
        }
        // ── 가름 도우미 — 속 구멍(둘레 꼭짓점만으로 NTS 제약 들로네 · 넓이 대조) ──
        var tris = new List<WallDaylight.Tri>();
        var gf = new GeometryFactory();
        WallDaylight.Tri T(int a, int b, int c) => new(new Point3(vt.X[a], vt.Y[a], vt.Z[a]), new Point3(vt.X[b], vt.Y[b], vt.Z[b]), new Point3(vt.X[c], vt.Y[c], vt.Z[c]));
        double Cross(int o, int a, int b) => (vt.X[a] - vt.X[o]) * (vt.Y[b] - vt.Y[o]) - (vt.Y[a] - vt.Y[o]) * (vt.X[b] - vt.X[o]);
        // ★[v111.2 · 전체 검토 M] 넓이는 첫 꼭짓점 기준 상대 좌표로 — 현장 좌표(2e5 · 5e5)를 그대로 곱하면 항마다 1e-5 ㎡ 오차가 쌓여 멀쩡한 고리를 «못 가름»으로 칸 면에 보냄
        double RingArea(List<int> r) { if (r.Count < 3) return 0; double x0 = vt.X[r[0]], y0 = vt.Y[r[0]], a2 = 0; for (int i = 0; i < r.Count; i++) { int p = r[i], q = r[(i + 1) % r.Count]; a2 += (vt.X[p] - x0) * (vt.Y[q] - y0) - (vt.X[q] - x0) * (vt.Y[p] - y0); } return Math.Abs(a2) / 2; }
        void TriangulateRing(List<int> r)
        {
            if (r.Count == 3) { AddT((r[0], r[1], r[2])); return; }
            try
            {
                var cs = r.Select(v => new Coordinate(vt.X[v], vt.Y[v])).ToList(); cs.Add(cs[0]);
                var trs = NetTopologySuite.Triangulate.Polygon.ConstrainedDelaunayTriangulator.Triangulate(gf.CreatePolygon(cs.ToArray()));
                var got = new List<(int, int, int)>(); double sum = 0;
                for (int g = 0; g < trs.NumGeometries; g++)
                {
                    var c3 = trs.GetGeometryN(g).Coordinates;
                    if (c3.Length < 3) continue;
                    if (!vt.TryId(c3[0].X, c3[0].Y, out int a) || !vt.TryId(c3[1].X, c3[1].Y, out int b) || !vt.TryId(c3[2].X, c3[2].Y, out int c)) { got.Clear(); break; }
                    got.Add((a, b, c)); sum += Math.Abs(Cross(a, b, c)) / 2;
                }
                double ra = RingArea(r);
                if (got.Count == 0 || Math.Abs(sum - ra) > 1e-4 + 1e-6 * ra) { rp.CdtFail++; return; }
                foreach (var g3 in got) AddT(g3); rp.Cdt++;
            }
            catch { rp.CdtFail++; }
        }
        // ── 방향 맞춤(반시계) · 넓이 0 뺌 ──
        var tl = new List<(int A, int B, int C)>(); var tlTag = new List<string>();
        for (int q = 0; q < triIds.Count; q++)
        {
            var (a, b, c) = triIds[q];
            double cr = Cross(a, b, c);
            if (Math.Abs(cr) < 1e-10) { rp.FlatDropped++; continue; }
            tl.Add(cr > 0 ? (a, b, c) : (a, c, b)); tlTag.Add(tags[q]);
        }
        // ── 속 구멍 메움(주름 틈 · 충돌 자리 — 계획 검토 M7 · M10) — 둘레 변(세모 하나만 씀)을 방향 따라 이은 고리 중 시계 방향(속 구멍)을 그 꼭짓점만으로 가름 ──
        for (int pass = 0; pass < 2; pass++)
        {
            var holes = Holes(vt, tl);
            if (holes.Count == 0) break;
            foreach (var h in holes)
            {
                double ha = 0, hx0 = vt.X[h[0]], hy0 = vt.Y[h[0]]; for (int q = 0; q < h.Count; q++) { int p0 = h[q], p1 = h[(q + 1) % h.Count]; ha += (vt.X[p0] - hx0) * (vt.Y[p1] - hy0) - (vt.X[p1] - hx0) * (vt.Y[p0] - hy0); }
                if (!h.Any(creaseV.Contains) && Math.Abs(ha) / 2 > 1.0) continue;
                if (DebugMesh != null) DebugMesh($"속 구멍 — 꼭짓점 {h.Count} · 첫 점 ({vt.X[h[0]]:F3}, {vt.Y[h[0]]:F3})");
                int before = triIds.Count; curTag = "속 구멍";
                var hr = new List<int>(h); hr.Reverse();
                TriangulateRing(hr);
                for (int k = before; k < triIds.Count; k++)
                {
                    var (a, b, c) = triIds[k];
                    double cr = Cross(a, b, c);
                    if (Math.Abs(cr) < 1e-10) { rp.FlatDropped++; continue; }
                    tl.Add(cr > 0 ? (a, b, c) : (a, c, b)); tlTag.Add("속 구멍");
                }
                rp.Holes++;
            }
        }
        for (int q = 0; q < tl.Count; q++)
        {
            var (a, b, c) = tl[q];
            tris.Add(T(a, b, c)); rp.Area += Cross(a, b, c) / 2;
            // ★[계획 검토 H1] Civil 바늘(가장 긴 변에 대한 높이 < ThinH)은 Civil이 뒤집는다
            double l1 = Math.Sqrt(Sq(vt.X[a] - vt.X[b]) + Sq(vt.Y[a] - vt.Y[b])), l2 = Math.Sqrt(Sq(vt.X[b] - vt.X[c]) + Sq(vt.Y[b] - vt.Y[c])), l3 = Math.Sqrt(Sq(vt.X[c] - vt.X[a]) + Sq(vt.Y[c] - vt.Y[a]));
            if (Cross(a, b, c) / Math.Max(l1, Math.Max(l2, l3)) < CivilSafeMesh.ThinH)
            {
                rp.Thin++;
                if (DebugMesh != null && rp.Thin < 6) DebugMesh($"바늘 — ({vt.X[a]:F4}, {vt.Y[a]:F4}) ({vt.X[b]:F4}, {vt.Y[b]:F4}) ({vt.X[c]:F4}, {vt.Y[c]:F4}) · 높이 {Cross(a, b, c) / Math.Max(l1, Math.Max(l2, l3)) * 1e6:F0} µm · {tlTag[q]}");
            }
        }
        rp.Tris = tris.Count;
        {
            var und = new Dictionary<(int, int), int>();
            foreach (var (a, b, c) in tl) foreach (var (u, v) in new[] { (a, b), (b, c), (c, a) }) { var k = u < v ? (u, v) : (v, u); und[k] = und.TryGetValue(k, out int m) ? m + 1 : 1; }
            var open = new HashSet<int>();
            foreach (var kv in und) if (kv.Value == 1) { if (creaseV.Contains(kv.Key.Item1)) open.Add(kv.Key.Item1); if (creaseV.Contains(kv.Key.Item2)) open.Add(kv.Key.Item2); }
            rp.OpenEnds = open.Count;
        }
        rp.ByPart = string.Join(" · ", tlTag.GroupBy(t => t.StartsWith("띠") ? (t.Contains("끝-1/-1") ? "사면 띠" : "끝 사슬") : t.StartsWith("도로 면") ? "도로 면" : t).Select(g => $"{g.Key} {g.Count()}"));
        Check(vt, tl, rp);
        return tris;
    }

    /// <summary>레일 — 광선 점 + 이웃 꺾임 거리(내 꺾은선 위 보간) + 데이라잇(· 가장자리 옹벽)으로 끝났으면 이웃이 더 긴 만큼 땅 위 점(이웃 거리마다).</summary>
    static List<RayPt> Rail(Ray r, List<Ray> nbrs, IGroundSurface ground)
    {
        var res = new List<RayPt>(r.Pts);
        if (r.Pts.Count == 0) return res;
        double end = r.Pts[^1].D;
        var ds = new SortedSet<double>();
        var ext = new SortedSet<double>();
        foreach (var n in nbrs)
            foreach (var p in n.Pts)
            {
                if (p.D > 1e-4 && p.D < end - 1e-4) ds.Add(p.D);
                else if (p.D > end + 1e-3) ext.Add(p.D);
            }
        foreach (double d in ds)
        {
            // ★ 내 점에서 2 cm 안이면 안 넣음 — 이웃끼리 몇 mm 다른 거리(데이라잇 · 꺾임)가 몰려 바늘 세모가 되지 않게(가까운 점이 짝 노릇)
            if (res.Any(q => Math.Abs(q.D - d) < RailGap)) continue;
            int k = res.FindIndex(q => q.D >= d - 1e-4);
            if (k < 0 || Math.Abs(res[k].D - d) <= 1e-4) continue;
            var a = res[k - 1]; var b = res[k];
            double t = (d - a.D) / (b.D - a.D);
            res.Insert(k, new RayPt(d, a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t, a.Z + (b.Z - a.Z) * t, -3));
        }
        if (r.End == RayEnd.Daylight || r.End == RayEnd.EdgeWall)
        {
            var last = r.Pts[^1];
            double ux = r.Pts.Count > 1 ? (last.X - r.Pts[0].X) / Math.Max(1e-12, last.D) : r.Nx, uy = r.Pts.Count > 1 ? (last.Y - r.Pts[0].Y) / Math.Max(1e-12, last.D) : r.Ny;
            double lastD = end;
            foreach (double d in ext)
            {
                if (d - lastD < RailGap) continue;
                lastD = d;
                double x = r.Pts[0].X + ux * d, y = r.Pts[0].Y + uy * d;
                if (!ground.TryGetElevation(x, y, out double g)) break;
                res.Add(new RayPt(d, x, y, g, -2));
            }
        }
        return res;
    }

    /// <summary>둘레 변을 방향 따라 이은 고리 중 시계 방향(속 구멍) — 갈림 꼭짓점(나가는 둘레 변 둘 이상)이 낀 고리는 건너뜀.</summary>
    static List<List<int>> Holes(VertTable vt, List<(int A, int B, int C)> tl)
    {
        var und = new Dictionary<(int, int), int>();
        foreach (var (a, b, c) in tl)
            foreach (var (u, v) in new[] { (a, b), (b, c), (c, a) }) { var k = u < v ? (u, v) : (v, u); und[k] = und.TryGetValue(k, out int m) ? m + 1 : 1; }
        var next = new Dictionary<int, List<int>>();
        foreach (var (a, b, c) in tl)
            foreach (var (u, v) in new[] { (a, b), (b, c), (c, a) })
                if (und[u < v ? (u, v) : (v, u)] == 1) { if (!next.TryGetValue(u, out var l)) next[u] = l = new(); l.Add(v); }
        var res = new List<List<int>>();
        var done = new HashSet<int>();
        foreach (int s0 in next.Keys)
        {
            if (done.Contains(s0)) continue;
            var loop = new List<int>(); int cur = s0; bool ok = true;
            while (true)
            {
                if (!next.TryGetValue(cur, out var l) || l.Count != 1) { ok = false; break; }
                if (!done.Add(cur)) { ok = cur == s0; break; }
                loop.Add(cur); cur = l[0];
                if (loop.Count > 200000) { ok = false; break; }
            }
            if (!ok || loop.Count < 3) continue;
            double a2 = 0, lx0 = vt.X[loop[0]], ly0 = vt.Y[loop[0]]; for (int i = 0; i < loop.Count; i++) { int p = loop[i], q = loop[(i + 1) % loop.Count]; a2 += (vt.X[p] - lx0) * (vt.Y[q] - ly0) - (vt.X[q] - lx0) * (vt.Y[p] - ly0); }
            if (a2 < 0) res.Add(loop);   // 세모가 반시계 → 바깥 둘레도 반시계 · 속 구멍은 시계
        }
        return res;
    }

    /// <summary>검사 — 변마다 세모 수(셋 이상 = 겹침) · 같은 방향 두 번(뒤집힘/겹침) · 둘레 변 위 남의 꼭짓점(T자) · 무작위 점 겹침.</summary>
    static void Check(VertTable vt, List<(int A, int B, int C)> tl, MeshReport rep)
    {
        var dir = new Dictionary<(int, int), int>();
        var und = new Dictionary<(int, int), int>();
        foreach (var (a, b, c) in tl)
            foreach (var (u, v) in new[] { (a, b), (b, c), (c, a) })
            {
                dir[(u, v)] = dir.TryGetValue((u, v), out int n) ? n + 1 : 1;
                var k = u < v ? (u, v) : (v, u);
                und[k] = und.TryGetValue(k, out int m) ? m + 1 : 1;
            }
        foreach (var kv in und) if (kv.Value > 2) rep.NonManifold++;
        foreach (var kv in dir) if (kv.Value > 1) rep.Flipped++;
        // T자 — 둘레 변(한 세모만 씀) 위에 다른 꼭짓점
        var used = new HashSet<int>(); foreach (var (a, b, c) in tl) { used.Add(a); used.Add(b); used.Add(c); }
        var grid = new Dictionary<(int, int), List<int>>();
        foreach (int v in used) { var key = ((int)Math.Floor(vt.X[v]), (int)Math.Floor(vt.Y[v])); if (!grid.TryGetValue(key, out var l)) grid[key] = l = new(); l.Add(v); }
        foreach (var kv in und)
        {
            if (kv.Value != 1) continue;
            var (a, b) = kv.Key;
            double ax = vt.X[a], ay = vt.Y[a], dx = vt.X[b] - ax, dy = vt.Y[b] - ay, l2 = dx * dx + dy * dy;
            if (l2 < 1e-14) continue;
            int gx0 = (int)Math.Floor(Math.Min(ax, ax + dx)), gx1 = (int)Math.Floor(Math.Max(ax, ax + dx)), gy0 = (int)Math.Floor(Math.Min(ay, ay + dy)), gy1 = (int)Math.Floor(Math.Max(ay, ay + dy));
            if ((long)(gx1 - gx0 + 1) * (gy1 - gy0 + 1) > 4000) continue;
            bool hit = false;
            for (int gy = gy0; gy <= gy1 && !hit; gy++)
                for (int gx = gx0; gx <= gx1 && !hit; gx++)
                {
                    if (!grid.TryGetValue((gx, gy), out var cell)) continue;
                    foreach (int v in cell)
                    {
                        if (v == a || v == b) continue;
                        double t = ((vt.X[v] - ax) * dx + (vt.Y[v] - ay) * dy) / l2;
                        if (t <= 1e-6 || t >= 1 - 1e-6) continue;
                        double qx = ax + dx * t - vt.X[v], qy = ay + dy * t - vt.Y[v];
                        if (qx * qx + qy * qy < 1e-10) { hit = true; break; }
                    }
                }
            if (hit) rep.TJunctions++;
        }
        // ★[설치 전 검토 M4] 겹침 — 끝점을 나누지 않는 두 변이 안에서 엇갈리면(평면 세모망이면 이것으로 모든 겹침이 잡힘)
        {
            var eg = new Dictionary<(int, int), List<(int, int)>>();
            foreach (var kv in und)
            {
                var (a, b) = kv.Key;
                int x0 = (int)Math.Floor(Math.Min(vt.X[a], vt.X[b])), x1 = (int)Math.Floor(Math.Max(vt.X[a], vt.X[b])), y0 = (int)Math.Floor(Math.Min(vt.Y[a], vt.Y[b])), y1 = (int)Math.Floor(Math.Max(vt.Y[a], vt.Y[b]));
                if ((long)(x1 - x0 + 1) * (y1 - y0 + 1) > 4000) continue;
                for (int y = y0; y <= y1; y++) for (int x = x0; x <= x1; x++) { if (!eg.TryGetValue((x, y), out var l)) eg[(x, y)] = l = new(); l.Add((a, b)); }
            }
            double O(int p, int q, int r) => (vt.X[q] - vt.X[p]) * (vt.Y[r] - vt.Y[p]) - (vt.Y[q] - vt.Y[p]) * (vt.X[r] - vt.X[p]);
            var seen = new HashSet<((int, int), (int, int))>();
            foreach (var l in eg.Values)
                for (int i = 0; i < l.Count; i++)
                    for (int j = i + 1; j < l.Count; j++)
                    {
                        var (a, b) = l[i]; var (c, d) = l[j];
                        if (a == c || a == d || b == c || b == d) continue;
                        double o1 = O(a, b, c), o2 = O(a, b, d), o3 = O(c, d, a), o4 = O(c, d, b);
                        if (((o1 > 1e-12 && o2 < -1e-12) || (o1 < -1e-12 && o2 > 1e-12)) && ((o3 > 1e-12 && o4 < -1e-12) || (o3 < -1e-12 && o4 > 1e-12)))
                        {
                            var key = l[i].CompareTo(l[j]) < 0 ? (l[i], l[j]) : (l[j], l[i]);
                            if (seen.Add(key)) rep.OverlapHits++;
                        }
                    }
        }
        // 겹침 — 세모 가운데 점마다 그 점을 덮는 세모 수(자기 말고 하나라도 덮으면 겹침)
        var tg = new Dictionary<(int, int), List<int>>();
        for (int i = 0; i < tl.Count; i++)
        {
            var (a, b, c) = tl[i];
            int x0 = (int)Math.Floor(Math.Min(vt.X[a], Math.Min(vt.X[b], vt.X[c]))), x1 = (int)Math.Floor(Math.Max(vt.X[a], Math.Max(vt.X[b], vt.X[c])));
            int y0 = (int)Math.Floor(Math.Min(vt.Y[a], Math.Min(vt.Y[b], vt.Y[c]))), y1 = (int)Math.Floor(Math.Max(vt.Y[a], Math.Max(vt.Y[b], vt.Y[c])));
            if ((long)(x1 - x0 + 1) * (y1 - y0 + 1) > 20000) continue;
            for (int y = y0; y <= y1; y++) for (int x = x0; x <= x1; x++) { if (!tg.TryGetValue((x, y), out var l)) tg[(x, y)] = l = new(); l.Add(i); }
        }
        for (int i = 0; i < tl.Count; i++)
        {
            var (a, b, c) = tl[i];
            double px = (vt.X[a] + vt.X[b] + vt.X[c]) / 3, py = (vt.Y[a] + vt.Y[b] + vt.Y[c]) / 3;
            if (!tg.TryGetValue(((int)Math.Floor(px), (int)Math.Floor(py)), out var l)) continue;
            foreach (int j in l)
            {
                if (j == i) continue;
                var (p, q, r) = tl[j];
                double c1 = (vt.X[q] - vt.X[p]) * (py - vt.Y[p]) - (vt.Y[q] - vt.Y[p]) * (px - vt.X[p]);
                double c2 = (vt.X[r] - vt.X[q]) * (py - vt.Y[q]) - (vt.Y[r] - vt.Y[q]) * (px - vt.X[q]);
                double c3 = (vt.X[p] - vt.X[r]) * (py - vt.Y[r]) - (vt.Y[p] - vt.Y[r]) * (px - vt.X[r]);
                if (c1 > 1e-12 && c2 > 1e-12 && c3 > 1e-12) { rep.OverlapHits++; break; }
            }
        }
    }
}
