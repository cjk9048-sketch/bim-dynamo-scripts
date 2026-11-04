using System.Diagnostics;
using System.Globalization;
using NetTopologySuite.Algorithm;
using NetTopologySuite.Algorithm.Locate;
using NetTopologySuite.Geometries;
using NetTopologySuite.Index.Strtree;
using NetTopologySuite.Operation.Buffer;
using NetTopologySuite.Operation.Polygonize;
using NetTopologySuite.Triangulate.Polygon;
using Tri = DH.Grading.Core.WallDaylight.Tri;

namespace DH.Grading.Core;

/// <summary>★★★[v111.8 · 계획_v111_8.md 2장 · JACK 1102 «최종지표면 = 정지면_DH 위에 진입도로»] 최종지표면 합치기 — <b>ε 이음 띠</b>(비평 H1).
/// <list type="bullet">
/// <item>층(도로 하나)을 아래부터 얹는다(가지 = Rank 큼 → 뒤 · 같으면 노선 핸들 작은 것 먼저 — L8). 나중 층이 이긴다.</item>
/// <item>도로 세모 · 넓힌 발자국 B 밖 바탕 세모는 <b>비트 그대로</b>. B와 닿는 바탕 세모는 ∂B에서 잘라 그 세모 평면 조각으로.</item>
/// <item>B − F(이음 띠)는 ∂F 점 = 도로 꼭짓점 높이 · ∂B 점 = 그 자리 바탕 높이 — Δv ≠ 0이어도 한 점 한 높이 · 틈 0 · T자 0.</item>
/// <item>손댄 구역은 <see cref="CivilSafeMesh.MakeZone"/>(고정 점 = 손 안 댄 세모와 나누는 점) · 전체는 <see cref="CivilSafeMesh.FixOutside"/>(손 안 댄 바탕 세모끼리만).</item>
/// <item>발자국 null · 다듬기 못 함 · 검사 어긋남이면 null(조용히 틀린 면을 안 만든다 — M7). 가파른 띠는 실패가 아니라 보고(<see cref="Report.Steep"/>).</item>
/// </list></summary>
public static class FinalSurface
{
    /// <summary>합치기 판 — 도장 [0]에 넣는다(규칙이 바뀌면 올림).</summary>
    public const string Version = "1";
    const int TagBase = -1, TagZone = -2;
    /// <summary>가까운 쌍 고치기 돌림 수 — 고칠 것이 없거나 B가 무효가 되면 먼저 멈춘다.</summary>
    const int MaxRounds = 4;
    /// <summary>가파른 띠를 찾을 때 ∂F를 걷는 간격(m).</summary>
    const double RunStep = 0.25;

    /// <summary>합치기 설정. <paramref name="Eps"/> = 이음 띠 폭(m · 기본 5 cm).</summary>
    public sealed record Options(double Eps = 0.05)
    {
        /// <summary>∂B 꼭짓점 · 선분을 바탕 꼭짓점에 붙이는 거리 · 새 점끼리 이보다 가까우면 고친다(1 mm — ReadMesh가 mm로 점을 합침).</summary>
        public double Snap { get; init; } = 1e-3;
        /// <summary>가까운 교차 쌍(쐐기 세모)을 그 꼭짓점으로 붙일 때 ∂B를 옮겨도 되는 거리(1 cm — 띠는 4 cm 넘게 남음).</summary>
        public double WedgeSnap { get; init; } = 1e-2;
        /// <summary>가파른 띠 — |Δv|가 이보다 큰 구간이(5 cm)</summary>
        public double SteepDv { get; init; } = 0.05;
        /// <summary>둘레를 따라 이보다 길게 이어지면 보고(0.5 m).</summary>
        public double SteepLen { get; init; } = 0.5;
    }

    /// <summary>층 = 도로 하나 — 세모(도로 면 그대로) · 이름 · 노선 핸들(차례) · 의존 깊이(본 도로 0 · 그 위 가지 1 …).</summary>
    public sealed record Layer(IReadOnlyList<Tri> Tris, string Name, long Handle, int Rank = 0);

    /// <summary>가파른 띠 구간 — ∂F를 따라 |Δv| &gt; SteepDv가 SteepLen 넘게 이어진 곳(시작 · 끝 XY · 길이 · 최대 Δv = 도로 − 바탕, 부호 그대로).</summary>
    public sealed record SteepRun(string Layer, long Handle, double X0, double Y0, double X1, double Y1, double Length, double MaxDv)
    {
        /// <summary>★[v111.8 · A1 실제 자료 1103] 구간 전체가 ∂F 변 하나의 안쪽(꼭짓점을 안 품음) — 그 변 길이(아니면 0) · 두 끝 꼭짓점 |Δv| 중 큰 것.</summary>
        public double EdgeLen { get; init; }
        public double EdgeEndDv { get; init; } = double.NaN;
        /// <summary>★[v111.8] 도로 면 끝 선(변 하나)의 두 끝은 땅에 닿는데(|Δv| ≤ 1 mm) 가운데가 어긋남 = 그 변 아래 정지면이 꺾임(바탕이 그 변을 따라 곧지 않음) — «사면이 땅에 못 닿음»이 아니다.</summary>
        public bool Crease => EdgeLen > 0 && Math.Abs(EdgeEndDv) <= 1e-3;
    }

    /// <summary>층 하나의 보고.</summary>
    public sealed class LayerReport
    {
        public string Name = ""; public long Handle; public int Rank;
        public int RoadTris, RoadTwoZ, BaseTwoZ;
        public string FootNote = "";
        /// <summary>발자국 F · 넓힌 발자국 B(F ⊕ ε · 다듬은 것) — 3D «진입도로 자리»(RoadArea)가 쓴다.</summary>
        public Geometry? Footprint, Area;
        /// <summary>걸친 세모(바뀐 것) · 그중 정지면 그대로였던 것 · 아래 층 도로 세모였던 것 · 손댄 구역이었던 것.</summary>
        public int Touched, TouchedBase, TouchedRoad, TouchedZone;
        public int Pieces, BandTris, BandDropNoBase;
        public int FacesRoad, FacesBand, FacesPiece, FacesOff, PolyBad, CdtFail; public string CdtWhy = "";
        /// <summary>∂B 다듬기 — 꼭짓점 붙임 · 선분에 바탕 꼭짓점 끼움 · 바탕 변에서 비킴 · 쐐기 꼭짓점 끼움 · 겹 꼭짓점 뺌 · 돌림 · 무효라 물린 돌림 · 처음 붙이기를 물림.</summary>
        public int VertSnap, SegInsert, Push, Wedge, Merge, Rounds, FixRejected, SnapReverted;
        /// <summary>끝에 남은 1 mm 안 쌍(까닭별) · 그중 가장 가까운 거리.</summary>
        public int NearCross, NearCrossBase, NearRing, NearOther; public double NearMin = double.PositiveInfinity; public string NearAt = "";
        /// <summary>Δv — ∂F 점 수 · 바탕 없는 점 · 1 mm 넘는 점 · 최대(부호 그대로) · 자리.</summary>
        public int DvN, DvNoBase, DvOver1mm; public double DvMax; public string DvAt = "";
        /// <summary>1 mm 넘는 점 중 도로 면 «열린 끝 줄» 점(두 이웃 ≤ 1 mm를 이은 곧은 줄 위 — RoadGrade.Mesh가 두 데이라잇 사이를 보간한 점 · 땅 위 아님).</summary>
        public int DvOver1mmChord;
        public readonly List<SteepRun> Steep = new();
        /// <summary>넓이 — 걸친 세모 합 · 새 세모 합(조각 + 띠 + 도로) · 기대(걸친 세모 ∪ F ∪ 남긴 띠).</summary>
        public double AreaTouched, AreaNew, AreaExpect;
        public long MsFoot, MsBuffer, MsSelect, MsFaces, MsTri, MsCheck;
    }

    public sealed class Report
    {
        public bool Ok; public string Fail = "";
        public double Eps;
        public int BaseTris, Kept, Removed, Pieces, BandTris, RoadTris, ZoneIn, ZoneOut, FinalTris;
        public double DvMax; public string DvAt = "";
        public readonly List<SteepRun> Steep = new();
        public readonly List<LayerReport> Layers = new();
        public CivilSafeMesh.Report? Zone;
        public CivilSafeMesh.OutsideFix? Outside;
        /// <summary>손댄 자리 검사(손댄 세모 + 도로 세모 + 그 곁 세모) — RoadGrade.Mesh 면 검사와 같은 것 + 넓이 0 · 0.1 mm 쌍 · 1 mm 쌍(새 점이 낀 것) · 바늘(손댄 구역).</summary>
        public int NonManifold, Flipped, TJunctions, Overlap, ZeroArea, Pairs01, Pairs1mm, Thin, ThinRoad;
        /// <summary>최종 전체 — 같은 XY 두 높이(LandXML 쓰기가 1e-7로 합침).</summary>
        public int TwoZ;
        public double AreaFinal, AreaExpect;
        /// <summary>도로마다 B 합(3D «진입도로 자리»).</summary>
        public Geometry? RoadArea;
        public long MsZone, MsOutside, MsCheck, MsTotal;

        public string Text
        {
            get
            {
                var ci = CultureInfo.InvariantCulture;
                string s = string.Format(ci, "최종면 {0} · 정지면 {1:N0} → 그대로 {2:N0} · 바꿈 {3:N0} · 조각 {4:N0} · 띠 {5:N0} · 도로 {6:N0} · 최종 {7:N0} · Δv 최대 {8:F1} mm{9}",
                    Ok ? "만듦" : "⚠못 만듦", BaseTris, Kept, Removed, Pieces, BandTris, RoadTris, FinalTris, DvMax * 1000, DvAt.Length > 0 ? " @" + DvAt : "");
                if (Steep.Count > 0) s += string.Format(ci, " · ⚠가파른 띠 {0}곳(가장 긴 {1:F1} m)", Steep.Count, Steep.Max(r => r.Length));
                s += string.Format(ci, " · 검사 세 세모 변 {0} · 뒤집힘 {1} · T자 {2} · 겹침 {3} · 넓이 0 {4} · 0.1 mm 쌍 {5} · 같은 XY 두 높이 {6} · 1 mm 쌍 {7} · 바늘 {8}{9}",
                    NonManifold, Flipped, TJunctions, Overlap, ZeroArea, Pairs01, TwoZ, Pairs1mm, Thin, ThinRoad > 0 ? $"(도로 세모 {ThinRoad})" : "");
                s += string.Format(ci, " · 넓이 {0:F3}/{1:F3} ㎡", AreaFinal, AreaExpect);
                if (Zone != null) s += " · 다듬기 판정 " + Zone.Tier + "(" + Zone.TierText + ")";
                if (Outside != null && Outside.Text.Length > 0) s += " · " + Outside.Text;
                s += string.Format(ci, " · {0} ms(다듬기 {1} · 바깥 {2} · 검사 {3})", MsTotal, MsZone, MsOutside, MsCheck);
                foreach (var l in Layers)
                    s += string.Format(ci, "\n  층 «{0}»(핸들 {1:X} · 차례 {2}) 도로 {3:N0} · 걸친 {4:N0}(정지면 {5:N0} · 아래 도로 {6:N0} · 구역 {7:N0}) · 조각 {8:N0} · 띠 {9:N0}{10} · 면 도로 {11}/띠 {12}/조각 {13}/밖 {14}"
                                         + " · ∂B 붙임 {15} · 끼움 {16} · 비킴 {17} · 쐐기 {18} · 뺌 {19} · 돌림 {20}{21}{22}"
                                         + " · Δv 점 {23:N0}(바탕 없음 {24} · 1 mm 넘음 {25}{38}) 최대 {26:F1} mm{27} · 가파른 띠 {28}"
                                         + " · 넓이 {29:F3}/{30:F3} ㎡ · {31}/{32}/{33}/{34}/{35}/{36} ms{37}",
                        l.Name, l.Handle, l.Rank, l.RoadTris, l.Touched, l.TouchedBase, l.TouchedRoad, l.TouchedZone, l.Pieces, l.BandTris,
                        l.BandDropNoBase > 0 ? $"(바탕 없어 버림 {l.BandDropNoBase})" : "", l.FacesRoad, l.FacesBand, l.FacesPiece, l.FacesOff,
                        l.VertSnap, l.SegInsert, l.Push, l.Wedge, l.Merge, l.Rounds,
                        l.FixRejected > 0 ? $" · 무효라 물림 {l.FixRejected}" : "", l.SnapReverted > 0 ? " · ⚠처음 붙이기 물림" : "",
                        l.DvN, l.DvNoBase, l.DvOver1mm, l.DvMax * 1000, l.DvAt.Length > 0 ? " @" + l.DvAt : "", l.Steep.Count,
                        l.AreaNew, l.AreaExpect, l.MsFoot, l.MsBuffer, l.MsSelect, l.MsFaces, l.MsTri, l.MsCheck,
                        (l.NearCross + l.NearCrossBase + l.NearRing + l.NearOther > 0 ? string.Format(ci, " · 남은 1 mm 안 쌍 — 교차끼리 {0} · 교차-바탕 {1} · 고리 {2} · 그 밖 {3}(가장 가까운 {4:F3} mm @{5})",
                            l.NearCross, l.NearCrossBase, l.NearRing, l.NearOther, l.NearMin * 1000, l.NearAt) : "")
                        + (l.FootNote.Length > 0 ? " · " + l.FootNote : "") + (l.CdtFail > 0 ? $" · ⚠못 가른 면 {l.CdtFail}({l.CdtWhy})" : "") + (l.PolyBad > 0 ? $" · ⚠면 묶기 버린 선 {l.PolyBad}" : ""),
                        l.DvOver1mmChord > 0 ? $" — 그중 도로 면 열린 끝 줄 점 {l.DvOver1mmChord}" : "");
                if (Fail.Length > 0) s += "\n  ⚠" + Fail;
                return s;
            }
        }
    }

    /// <summary>정지면 세모 위에 층들을 얹은 최종 세모(손 안 댄 세모는 비트 그대로 · 좌표 차례로 정렬). 못 하면 null — 까닭은 <paramref name="rep"/>.Fail.</summary>
    public static List<Tri>? Compose(IReadOnlyList<Tri> baseTris, IReadOnlyList<Layer> layers, Options? opt, out Report rep)
    {
        opt ??= new Options();
        rep = new Report { Eps = opt.Eps };
        var sw = Stopwatch.StartNew();
        List<Tri>? res;
        try { res = Core(baseTris, layers ?? Array.Empty<Layer>(), opt, rep); }
        catch (Exception ex) { rep.Fail = "합치기 예외 — " + ex.Message; res = null; }
        rep.MsTotal = sw.ElapsedMilliseconds;
        if (res == null && rep.Fail.Length == 0) rep.Fail = "까닭 모름";
        rep.Ok = res != null && rep.Fail.Length == 0;
        return rep.Ok ? res : null;
    }

    // ══════════════════════════════ 전체 ══════════════════════════════

    static List<Tri>? Core(IReadOnlyList<Tri> baseTris, IReadOnlyList<Layer> layers, Options opt, Report R)
    {
        if (baseTris == null || baseTris.Count == 0) { R.Fail = "정지면 세모가 없다"; return null; }
        if (!(opt.Eps > 4 * opt.Snap + 2 * opt.WedgeSnap)) { R.Fail = "띠 폭이 붙이기 거리에 비해 좁다"; return null; }
        R.BaseTris = baseTris.Count;
        double baseArea = 0;
        foreach (var t in baseTris) baseArea += Area(t);
        R.AreaExpect = baseArea;
        var cur = new List<Tri>(baseTris);
        var tag = new List<int>(baseTris.Count);
        for (int i = 0; i < baseTris.Count; i++) tag.Add(TagBase);
        // ★[L8] 차례 = 의존 깊이(가지는 본 도로 뒤) → 노선 핸들 작은 것(먼저 만든 노선) → 이름 — 부르는 쪽 차례와 상관없이 같은 결과
        var order = layers.Where(l => l != null).OrderBy(l => l.Rank).ThenBy(l => l.Handle).ThenBy(l => l.Name, StringComparer.Ordinal).ToList();
        var roadPts = new HashSet<(double, double)>();
        var areas = new List<Geometry>();
        for (int k = 0; k < order.Count; k++)
        {
            var lr = new LayerReport { Name = order[k].Name, Handle = order[k].Handle, Rank = order[k].Rank };
            R.Layers.Add(lr);
            if (!Apply(cur, tag, order[k], k, opt, lr, R, out var next, out var ntag)) return null;
            cur = next; tag = ntag;
            foreach (var t in order[k].Tris) { roadPts.Add((t.A.X, t.A.Y)); roadPts.Add((t.B.X, t.B.Y)); roadPts.Add((t.C.X, t.C.Y)); }
            R.AreaExpect += lr.AreaExpect - lr.AreaTouched;
            R.Removed += lr.TouchedBase;
            if (lr.Area != null) areas.Add(lr.Area);
            R.Steep.AddRange(lr.Steep);
            if (Math.Abs(lr.DvMax) > Math.Abs(R.DvMax)) { R.DvMax = lr.DvMax; R.DvAt = lr.DvAt; }
        }
        if (areas.Count > 0) R.RoadArea = areas.Count == 1 ? areas[0] : NetTopologySuite.Operation.OverlayNG.OverlayNGRobust.Union(areas);

        // ── 손댄 구역 다듬기(고정 점 = 손 안 댄 세모와 나누는 점 — L12) ──
        var sw = Stopwatch.StartNew();
        var zone = new List<Tri>(); var keep = new List<Tri>(cur.Count); var keepTag = new List<int>(cur.Count);
        for (int i = 0; i < cur.Count; i++) { if (tag[i] == TagZone) zone.Add(cur[i]); else { keep.Add(cur[i]); keepTag.Add(tag[i]); } }
        R.Kept = keepTag.Count(t => t == TagBase);
        R.RoadTris = keepTag.Count(t => t >= 0);
        R.ZoneIn = zone.Count;
        List<Tri> safe = zone;
        if (zone.Count > 0)
        {
            zone.Sort(CompareTri);
            var zk = new HashSet<(double, double)>();
            foreach (var t in zone) { zk.Add((t.A.X, t.A.Y)); zk.Add((t.B.X, t.B.Y)); zk.Add((t.C.X, t.C.Y)); }
            var fixedP = new Dictionary<(double, double), Point3>();
            foreach (var t in keep)
                foreach (var p in V3(t))
                    if (zk.Contains((p.X, p.Y))) fixedP[(p.X, p.Y)] = p;
            // ★[실제 자료 1103 계측] 띠 · 조각 바늘(높이 107~122µm — ∂B 1.2 mm 짧은 변에 먼 꼭짓점 · ∂B 세 점 거의 한 줄)은 Civil이 뒤집는 문턱(122µm) 안 →
            //   옹벽 터파기면과 같이 125µm 아래 면도 접는다(ThinFlat). 움직일 수 있는 점은 ∂B 쪽(고정 점 아님)뿐이다
            var made = CivilSafeMesh.MakeZone(zone, fixedP.Values.OrderBy(p => p.X).ThenBy(p => p.Y).ToList(), out var zr, flatH: CivilSafeMesh.ThinFlat);
            R.Zone = zr;
            if (made == null) { R.Fail = "손댄 구역 다듬기 못 함 — " + (zr.Fail.Length > 0 ? zr.Fail : zr.Summary); R.MsZone = sw.ElapsedMilliseconds; return null; }
            safe = made;
        }
        R.ZoneOut = safe.Count;
        R.MsZone = sw.ElapsedMilliseconds;

        // ── 전체 «Civil이 고칠 면» — 손 안 댄 정지면 세모끼리만 먼저 뒤집기(도로 세모 · 띠는 금지) ──
        sw.Restart();
        var all = new List<Tri>(keep.Count + safe.Count);
        all.AddRange(keep); all.AddRange(safe);
        all.Sort(CompareTri);   // ★[11] 같은 입력 = 같은 결과(Civil이 세모 차례를 바꿔 줘도)
        var untouched = new HashSet<Tri>();
        for (int i = 0; i < keep.Count; i++) if (keepTag[i] == TagBase) untouched.Add(keep[i]);
        var safeSet = new HashSet<Tri>(safe);
        var fin = CivilSafeMesh.FixOutside(all, t => untouched.Contains(t) && !safeSet.Contains(t), out var ofx);
        R.Outside = ofx;
        R.MsOutside = sw.ElapsedMilliseconds;

        // ── 검사 ──
        sw.Restart();
        R.FinalTris = fin.Count;
        CheckFinal(fin, untouched, safeSet, roadPts, R);
        R.MsCheck = sw.ElapsedMilliseconds;
        var bad = new List<string>();
        if (R.NonManifold > 0) bad.Add($"세 세모 변 {R.NonManifold}");
        if (R.Flipped > 0) bad.Add($"뒤집힘 {R.Flipped}");
        if (R.TJunctions > 0) bad.Add($"T자 {R.TJunctions}");
        if (R.Overlap > 0) bad.Add($"겹침 {R.Overlap}");
        if (R.ZeroArea > 0) bad.Add($"넓이 0 {R.ZeroArea}");
        if (R.TwoZ > 0) bad.Add($"같은 XY 두 높이 {R.TwoZ}");
        if (R.Pairs01 > 0) bad.Add($"0.1 mm 쌍 {R.Pairs01}");
        if (Math.Abs(R.AreaFinal - R.AreaExpect) > 1e-6 * R.AreaExpect + 1e-6)
            bad.Add(string.Format(CultureInfo.InvariantCulture, "넓이 {0:F4}/{1:F4} ㎡", R.AreaFinal, R.AreaExpect));
        if (bad.Count > 0) { R.Fail = "검사 어긋남 — " + string.Join(" · ", bad); return null; }
        return fin;
    }

    // ══════════════════════════════ 층 하나 ══════════════════════════════

    /// <summary>∂B 고리 — 폴리곤 번호(첫 고리가 껍질) · 점(닫지 않음).</summary>
    sealed class Ring
    {
        public int Poly; public List<Coordinate> P = new();
        public Ring Copy() => new() { Poly = Poly, P = P.Select(c => new Coordinate(c.X, c.Y)).ToList() };
    }

    /// <summary>고리 선분 위 노드 — 선분 매개 · 점 · 그 점이 바탕 꼭짓점인가 · 바탕 변(교차 · 없으면 −1).</summary>
    readonly record struct RNode(double T, Coordinate C, bool BaseV, int Edge);

    /// <summary>한 판의 배치 — B · 걸친 세모 · 그 변 · 노드.</summary>
    sealed class Arr
    {
        public Geometry B = null!; public IndexedPointInAreaLocator Loc = null!;
        public STRtree<(int R, int J)> Segs = null!;
        public List<int> T = new();
        public List<int> E = new();
        public List<List<List<RNode>>> RN = new();
        public Dictionary<int, List<(double T, Coordinate C)>> EN = new();
    }

    static bool Apply(List<Tri> cur, List<int> tag, Layer L, int li, Options opt, LayerReport lr, Report R, out List<Tri> next, out List<int> ntag)
    {
        next = cur; ntag = tag;
        var sw = Stopwatch.StartNew();
        string who = $"«{L.Name}»";
        if (L.Tris == null || L.Tris.Count == 0) { R.Fail = who + " 도로 세모가 없다"; return false; }
        lr.RoadTris = L.Tris.Count;
        var gf = new GeometryFactory();

        // ① 발자국 F — 둘레 = 도로 면 둘레 변 그대로 · 구멍 = 도로 안 땅 섬 · null이면 실패(M7)
        var F0 = RoadFootprint.FromTris(L.Tris, out string fnote);
        lr.FootNote = fnote;
        if (F0 == null || F0.IsEmpty) { R.Fail = $"{who} 발자국 못 만듦(도로를 말없이 빼지 않음){(fnote.Length > 0 ? " — " + fnote : "")}"; return false; }
        var F = F0.Copy(); F.Normalize();
        lr.Footprint = F;
        var roadZ = new Dictionary<(double, double), double>(L.Tris.Count * 2);
        foreach (var t in L.Tris)
            foreach (var p in V3(t))
                if (!roadZ.TryAdd((p.X, p.Y), p.Z) && roadZ[(p.X, p.Y)] != p.Z) lr.RoadTwoZ++;
        lr.MsFoot = sw.ElapsedMilliseconds;

        // ② 넓힌 발자국 B = F ⊕ ε(모서리 Mitre · 한도 2 → 넘으면 Bevel)
        sw.Restart();
        var bp = new BufferParameters { JoinStyle = JoinStyle.Mitre, MitreLimit = 2.0, QuadrantSegments = 8 };
        var B0 = BufferOp.Buffer(F, opt.Eps, bp);
        if (B0 == null || B0.IsEmpty || B0 is not IPolygonal) { R.Fail = $"{who} 넓힌 발자국 못 만듦"; return false; }
        B0.Normalize();
        var rings0 = new List<Ring>();
        int nPoly = 0;
        for (int g = 0; g < B0.NumGeometries; g++)
        {
            if (B0.GetGeometryN(g) is not Polygon pg || pg.IsEmpty) continue;
            rings0.Add(OpenRing(pg.ExteriorRing, nPoly));
            for (int h = 0; h < pg.NumInteriorRings; h++) rings0.Add(OpenRing(pg.GetInteriorRingN(h), nPoly));
            nPoly++;
        }

        // ③ 후보 세모 — ∂B 곁(다듬기가 옮길 수 있는 거리 전부 + 여유: 쐐기 끼움 · 돌림마다 비킴) 또는 B 안
        double margin = 2 * opt.WedgeSnap + (MaxRounds + 2) * 2 * opt.Snap;
        var segs0 = SegTree(rings0);
        var benv = B0.EnvelopeInternal.Copy(); benv.ExpandBy(margin);
        var loc0 = new IndexedPointInAreaLocator(B0);
        var cand = new List<int>();
        for (int i = 0; i < cur.Count; i++)
        {
            var t = cur[i];
            var e = TriEnv(t);
            if (!e.Intersects(benv)) continue;
            var em = e.Copy(); em.ExpandBy(margin);
            if (segs0.Query(em).Count > 0 || loc0.Locate(Centroid(t)) == Location.Interior) cand.Add(i);
        }
        // 후보 꼭짓점(높이) · 변(좌표 차례로)
        var bz = new Dictionary<(double, double), double>();
        foreach (int i in cand)
            foreach (var p in V3(cur[i]))
                if (!bz.TryAdd((p.X, p.Y), p.Z) && bz[(p.X, p.Y)] != p.Z) lr.BaseTwoZ++;
        var ekeys = new SortedSet<((double, double), (double, double))>();
        foreach (int i in cand) { var t = cur[i]; ekeys.Add(EK(t.A, t.B)); ekeys.Add(EK(t.B, t.C)); ekeys.Add(EK(t.C, t.A)); }
        var edges = new List<(Coordinate A, Coordinate B)>(ekeys.Count);
        var eid = new Dictionary<((double, double), (double, double)), int>(ekeys.Count);
        foreach (var k in ekeys) { eid[k] = edges.Count; edges.Add((new Coordinate(k.Item1.Item1, k.Item1.Item2), new Coordinate(k.Item2.Item1, k.Item2.Item2))); }
        var triE = new Dictionary<int, (int, int, int)>(cand.Count);
        foreach (int i in cand) { var t = cur[i]; triE[i] = (eid[EK(t.A, t.B)], eid[EK(t.B, t.C)], eid[EK(t.C, t.A)]); }
        var candEdgeTree = new STRtree<int>();
        for (int e = 0; e < edges.Count; e++) candEdgeTree.Insert(new Envelope(edges[e].A, edges[e].B), e);
        candEdgeTree.Build();
        var vgrid = new Dictionary<(long, long), List<Coordinate>>();
        foreach (var k in bz.Keys)
        {
            var g = Cell(k.Item1, k.Item2, opt.Snap);
            if (!vgrid.TryGetValue(g, out var l)) vgrid[g] = l = new();
            l.Add(new Coordinate(k.Item1, k.Item2));
        }

        // ④ ∂B 다듬기 — 1 mm 안 바탕 꼭짓점에 붙임(꼭짓점 · 선분) · 바탕 변 1 mm 안 꼭짓점은 비킴
        var rings = rings0.Select(r => r.Copy()).ToList();
        SnapRings(rings, vgrid, bz, opt, lr);
        PushOffEdges(rings, bz, edges, candEdgeTree, opt, lr);
        foreach (var r in rings) Dedupe(r.P);
        var Bt = BuildB(rings, nPoly, gf);
        if (Bt == null || !Bt.IsValid)
        {
            // 붙이기가 B를 무효로 만들면 붙이기 전 고리로(가까운 쌍은 아래 돌림 · 다듬기가 본다)
            lr.SnapReverted = 1;
            rings = rings0.Select(r => r.Copy()).ToList();
        }
        lr.MsBuffer = sw.ElapsedMilliseconds;

        // ⑤ 배치 · 가까운 쌍 고치기(돌림)
        sw.Restart();
        var li0 = new RobustLineIntersector();
        Arr A = Arrange(rings, nPoly, gf, cur, cand, triE, edges, bz, li0);
        for (int round = 0; round < MaxRounds; round++)
        {
            var fixes = NearFixes(A, rings, edges, bz, opt, lr, count: false);
            if (fixes.Count == 0) break;
            var trial = ApplyFixes(rings, fixes, lr);
            var Bn = BuildB(trial, nPoly, gf);
            if (Bn == null || !Bn.IsValid) { lr.FixRejected++; break; }
            rings = trial; lr.Rounds++;
            A = Arrange(rings, nPoly, gf, cur, cand, triE, edges, bz, li0);
        }
        NearFixes(A, rings, edges, bz, opt, lr, count: true);
        lr.Area = A.B;
        foreach (int i in A.T)
        {
            lr.Touched++;
            if (tag[i] == TagBase) lr.TouchedBase++; else if (tag[i] == TagZone) lr.TouchedZone++; else lr.TouchedRoad++;
            lr.AreaTouched += Area(cur[i]);
        }
        lr.MsSelect = sw.ElapsedMilliseconds;

        // ⑥ 선 모음 → 면: (T의 변 − B) ∪ ∂B ∪ ∂F — 띠 안에는 바탕 변을 넣지 않는다(∂F 위 남의 점 0)
        sw.Restart();
        var lineKeys = new SortedSet<((double, double), (double, double))>();
        void AddLine(Coordinate u, Coordinate v) { if (u.X == v.X && u.Y == v.Y) return; lineKeys.Add(EK(u, v)); }
        foreach (int e in A.E)
        {
            var (a, b) = edges[e];
            var pts = new List<Coordinate> { a };
            if (A.EN.TryGetValue(e, out var en)) pts.AddRange(en.OrderBy(q => q.T).Select(q => q.C));
            pts.Add(b);
            for (int k = 0; k + 1 < pts.Count; k++)
            {
                var u = pts[k]; var v = pts[k + 1];
                if (u.X == v.X && u.Y == v.Y) continue;
                if (A.Loc.Locate(new Coordinate((u.X + v.X) / 2, (u.Y + v.Y) / 2)) == Location.Exterior) AddLine(u, v);
            }
        }
        for (int r = 0; r < rings.Count; r++)
        {
            var P = rings[r].P;
            for (int j = 0; j < P.Count; j++)
            {
                var pts = new List<Coordinate> { P[j] };
                pts.AddRange(A.RN[r][j].Select(q => q.C));
                pts.Add(P[(j + 1) % P.Count]);
                for (int k = 0; k + 1 < pts.Count; k++) AddLine(pts[k], pts[k + 1]);
            }
        }
        var fRings = new List<Coordinate[]>();
        for (int g = 0; g < F.NumGeometries; g++)
        {
            if (F.GetGeometryN(g) is not Polygon pg) continue;
            fRings.Add(pg.ExteriorRing.Coordinates);
            for (int h = 0; h < pg.NumInteriorRings; h++) fRings.Add(pg.GetInteriorRingN(h).Coordinates);
        }
        foreach (var cs in fRings) for (int k = 0; k + 1 < cs.Length; k++) AddLine(cs[k], cs[k + 1]);
        var poly = new Polygonizer();
        foreach (var k in lineKeys) poly.Add(gf.CreateLineString(new[] { new Coordinate(k.Item1.Item1, k.Item1.Item2), new Coordinate(k.Item2.Item1, k.Item2.Item2) }));
        var faces = poly.GetPolygons().OfType<Polygon>().Where(p => !p.IsEmpty).Select(p => { var q = (Polygon)p.Copy(); q.Normalize(); return q; }).ToList();
        lr.PolyBad = poly.GetDangles().Count + poly.GetCutEdges().Count + poly.GetInvalidRingLines().Count;
        faces.Sort((p, q) =>
        {
            var a = p.EnvelopeInternal; var b = q.EnvelopeInternal;
            int c = a.MinX.CompareTo(b.MinX); if (c != 0) return c;
            c = a.MinY.CompareTo(b.MinY); if (c != 0) return c;
            c = a.MaxX.CompareTo(b.MaxX); if (c != 0) return c;
            c = a.MaxY.CompareTo(b.MaxY); if (c != 0) return c;
            return p.NumPoints.CompareTo(q.NumPoints);
        });
        lr.MsFaces = sw.ElapsedMilliseconds;
        if (lr.PolyBad > 0) { R.Fail = $"{who} 이음 띠 면 묶기에서 버린 선 {lr.PolyBad}(매달린 변 · 잘린 변 · 무효 고리)"; return false; }

        // ⑦ 면 가르기 · 세모 나누기 · 높이
        sw.Restart();
        var tIdx = new STRtree<int>();
        foreach (int i in A.T) tIdx.Insert(TriEnv(cur[i]), i);
        tIdx.Build();
        var floc = new IndexedPointInAreaLocator(F);
        var zOne = new Dictionary<(double, double), double>();
        double Z(Coordinate c)
        {
            var k = (c.X, c.Y);
            if (zOne.TryGetValue(k, out double z)) return z;
            if (roadZ.TryGetValue(k, out z)) { }
            else if (bz.TryGetValue(k, out z)) { }
            else z = PlaneZ(tIdx, cur, c.X, c.Y, out _) ?? double.NaN;
            zOne[k] = z;
            return z;
        }
        var pieces = new List<Tri>(); var band = new List<Tri>();
        double bandKeptArea = 0;
        var bandKeptPolys = new List<Geometry>();
        foreach (var pg in faces)
        {
            var ip = pg.InteriorPoint.Coordinate;
            if (floc.Locate(ip) != Location.Exterior) { lr.FacesRoad++; continue; }
            var lb = A.Loc.Locate(ip);
            bool isBand;
            if (lb == Location.Interior) isBand = true;
            else if (lb == Location.Exterior)
            {
                // 바탕 조각 — 걸친 세모 하나 안(그 세모 평면) · 걸친 세모 밖이면 손 안 댄 세모 자리 · 바탕 없는 자리(버림)
                if (PlaneZ(tIdx, cur, ip.X, ip.Y, out _) == null) { lr.FacesOff++; continue; }
                isBand = false;
            }
            else { R.Fail = $"{who} 면 안쪽 점이 ∂B 위({ip.X:F4},{ip.Y:F4})"; return false; }
            var trs = Triangulate(pg, out string why);
            if (trs == null) { lr.CdtFail++; if (lr.CdtWhy.Length < 160) lr.CdtWhy += (lr.CdtWhy.Length > 0 ? " · " : "") + why; continue; }
            if (isBand) lr.FacesBand++; else lr.FacesPiece++;
            foreach (var (a, b, c) in trs)
            {
                double za = Z(a), zb = Z(b), zc = Z(c);
                if (!isBand && (double.IsNaN(za) || double.IsNaN(zb) || double.IsNaN(zc))) { R.Fail = $"{who} 바탕 조각 높이 없음({a.X:F4},{a.Y:F4})"; return false; }
                if (isBand && (double.IsNaN(za) || double.IsNaN(zb) || double.IsNaN(zc))) { lr.BandDropNoBase++; continue; }   // ★[10] 바탕 범위 밖 ∂B 점을 품은 띠 세모는 버림(열린 끝)
                var t = Ccw(new Tri(new Point3(a.X, a.Y, za), new Point3(b.X, b.Y, zb), new Point3(c.X, c.Y, zc)));
                if (isBand) { band.Add(t); bandKeptArea += Area(t); bandKeptPolys.Add(TriPoly(t, gf)); }
                else pieces.Add(t);
            }
        }
        if (lr.CdtFail > 0) { R.Fail = $"{who} 면 {lr.CdtFail}개를 세모로 못 가름 — {lr.CdtWhy}"; return false; }
        lr.Pieces = pieces.Count; lr.BandTris = band.Count;
        R.Pieces += pieces.Count; R.BandTris += band.Count;
        lr.MsTri = sw.ElapsedMilliseconds;

        // ⑧ Δv(∂F 점마다 도로 − 같은 XY 바탕) · 가파른 띠
        sw.Restart();
        foreach (var cs in fRings)
        {
            var dense = new List<(double X, double Y, double Dv, int E)>(cs.Length * 4);   // E = 변 번호(꼭짓점 사이 표본) · −1 = 꼭짓점
            var vdv = new double[cs.Length - 1];
            for (int k = 0; k + 1 < cs.Length; k++)
            {
                var c = cs[k];
                if (!roadZ.TryGetValue((c.X, c.Y), out double zr)) { R.Fail = $"{who} ∂F 점이 도로 꼭짓점이 아님({c.X:F4},{c.Y:F4})"; return false; }
                double? zb = PlaneZ(tIdx, cur, c.X, c.Y, out _);
                double dv = zb.HasValue ? zr - zb.Value : double.NaN;
                lr.DvN++;
                if (double.IsNaN(dv)) lr.DvNoBase++;
                else
                {
                    if (Math.Abs(dv) > 1e-3) lr.DvOver1mm++;
                    if (Math.Abs(dv) > Math.Abs(lr.DvMax)) { lr.DvMax = dv; lr.DvAt = At(c.X, c.Y); }
                }
                vdv[k] = dv;
                dense.Add((c.X, c.Y, dv, -1));
                // ★[SF③ 계측] 가파른 띠 끝은 꼭짓점 사이(2 m까지)에서 바탕이 꺾여도 맞게 — 둘레 변을 0.25 m 안으로 걸으며 잰다(도로 높이는 도로 세모 변이라 직선)
                var c2 = cs[k + 1];
                double len = Math.Sqrt((c2.X - c.X) * (c2.X - c.X) + (c2.Y - c.Y) * (c2.Y - c.Y));
                int nSub = (int)Math.Ceiling(len / RunStep);
                if (nSub > 1 && roadZ.TryGetValue((c2.X, c2.Y), out double zr2))
                    for (int q = 1; q < nSub; q++)
                    {
                        double f = (double)q / nSub, x = c.X + (c2.X - c.X) * f, y = c.Y + (c2.Y - c.Y) * f;
                        double? zq = PlaneZ(tIdx, cur, x, y, out _);
                        dense.Add((x, y, zq.HasValue ? zr + (zr2 - zr) * f - zq.Value : double.NaN, k));
                    }
            }
            Runs(dense, opt, lr, cs, vdv);
            // ★[실제 자료 · SF② 1103 계측] 1 mm 넘는 점 중 «두 이웃(≤ 1 mm)을 이은 곧은 줄 위 점» — RoadGrade.Mesh 열린 끝 줄(두 데이라잇 사이 보간 · 땅 위 아님)이라 도로 면 몫(띠가 잇는다)
            int m = vdv.Length;
            for (int k = 0; k < m; k++)
            {
                if (!(Math.Abs(vdv[k]) > 1e-3)) continue;
                int a = (k + m - 1) % m, b = (k + 1) % m;
                if (!(Math.Abs(vdv[a]) <= 1e-3 && Math.Abs(vdv[b]) <= 1e-3)) continue;
                var pa = cs[a]; var pb = cs[b]; var pc = cs[k];
                double cr = (pb.X - pa.X) * (pc.Y - pa.Y) - (pb.Y - pa.Y) * (pc.X - pa.X), lab = Math.Sqrt((pb.X - pa.X) * (pb.X - pa.X) + (pb.Y - pa.Y) * (pb.Y - pa.Y));
                if (lab > 0 && Math.Abs(cr) / lab < 1e-6) lr.DvOver1mmChord++;
            }
        }

        // ⑨ 넓이 — 새 세모 합 = (걸친 세모 ∪ F ∪ 남긴 띠) 넓이(겹침 · 틈 0)
        double roadArea = 0; foreach (var t in L.Tris) roadArea += Area(t);
        double pieceArea = 0; foreach (var t in pieces) pieceArea += Area(t);
        lr.AreaNew = pieceArea + bandKeptArea + roadArea;
        var ug = new List<Geometry>(A.T.Count + 1 + bandKeptPolys.Count);
        foreach (int i in A.T) ug.Add(TriPoly(cur[i], gf));
        ug.Add(F);
        ug.AddRange(bandKeptPolys);
        lr.AreaExpect = UnionArea(ug);
        if (Math.Abs(lr.AreaNew - lr.AreaExpect) > 1e-6 * lr.AreaExpect + 1e-6)
        { R.Fail = string.Format(CultureInfo.InvariantCulture, "{0} 넓이 어긋남 — 새 세모 {1:F4} ㎡ · 기대 {2:F4} ㎡(겹침 또는 틈)", who, lr.AreaNew, lr.AreaExpect); return false; }
        lr.MsCheck = sw.ElapsedMilliseconds;

        // ⑩ 다음 판 — 손 안 댄 세모(비트 그대로) + 조각 + 띠 + 도로 세모(비트 그대로)
        var tset = new HashSet<int>(A.T);
        next = new List<Tri>(cur.Count - tset.Count + pieces.Count + band.Count + L.Tris.Count);
        ntag = new List<int>(next.Capacity);
        for (int i = 0; i < cur.Count; i++) { if (tset.Contains(i)) continue; next.Add(cur[i]); ntag.Add(tag[i]); }
        foreach (var t in pieces) { next.Add(t); ntag.Add(TagZone); }
        foreach (var t in band) { next.Add(t); ntag.Add(TagZone); }
        foreach (var t in L.Tris) { next.Add(t); ntag.Add(li); }
        return true;
    }

    // ══════════════════════════════ ∂B 다듬기 ══════════════════════════════

    static Ring OpenRing(LineString ls, int poly)
    {
        var r = new Ring { Poly = poly };
        var cs = ls.Coordinates;
        for (int i = 0; i + 1 < cs.Length; i++) r.P.Add(new Coordinate(cs[i].X, cs[i].Y));
        return r;
    }

    static Geometry? BuildB(List<Ring> rings, int nPoly, GeometryFactory gf)
    {
        var polys = new List<Polygon>();
        for (int p = 0; p < nPoly; p++)
        {
            LinearRing? shell = null; var holes = new List<LinearRing>();
            foreach (var r in rings)
            {
                if (r.Poly != p) continue;
                if (r.P.Count < 3) return null;
                var cs = r.P.Select(c => new Coordinate(c.X, c.Y)).ToList(); cs.Add(new Coordinate(r.P[0].X, r.P[0].Y));
                LinearRing lrg;
                try { lrg = gf.CreateLinearRing(cs.ToArray()); } catch { return null; }
                if (shell == null) shell = lrg; else holes.Add(lrg);
            }
            if (shell != null) polys.Add(gf.CreatePolygon(shell, holes.ToArray()));
        }
        if (polys.Count == 0) return null;
        return polys.Count == 1 ? polys[0] : gf.CreateMultiPolygon(polys.ToArray());
    }

    static STRtree<(int R, int J)> SegTree(List<Ring> rings)
    {
        var t = new STRtree<(int, int)>();
        for (int r = 0; r < rings.Count; r++)
        {
            var P = rings[r].P;
            for (int j = 0; j < P.Count; j++) t.Insert(new Envelope(P[j], P[(j + 1) % P.Count]), (r, j));
        }
        t.Build();
        return t;
    }

    /// <summary>1 mm 안 바탕 꼭짓점에 붙임 — 고리 꼭짓점은 가장 가까운 바탕 꼭짓점으로 · 고리 선분 1 mm 안 바탕 꼭짓점은 선분에 끼움(GeometrySnapper와 같은 뜻 · 좌표는 바탕 것 그대로).</summary>
    static void SnapRings(List<Ring> rings, Dictionary<(long, long), List<Coordinate>> vgrid, Dictionary<(double, double), double> bz, Options opt, LayerReport lr)
    {
        foreach (var r in rings)
            for (int k = 0; k < r.P.Count; k++)
            {
                var c = r.P[k];
                var v = NearestVertex(vgrid, c, opt.Snap);
                if (v != null && !(v.X == c.X && v.Y == c.Y)) { r.P[k] = new Coordinate(v.X, v.Y); lr.VertSnap++; }
            }
        var segs = SegTree(rings);
        var ins = new Dictionary<(int, int), List<(double T, Coordinate C)>>();
        foreach (var k in bz.Keys.OrderBy(q => q))
        {
            var v = new Coordinate(k.Item1, k.Item2);
            double best = opt.Snap; (int, int)? at = null; double bt = 0;
            foreach (var (ri, j) in segs.Query(new Envelope(v.X - opt.Snap, v.X + opt.Snap, v.Y - opt.Snap, v.Y + opt.Snap)))
            {
                var P = rings[ri].P; var p = P[j]; var q = P[(j + 1) % P.Count];
                if ((p.X == v.X && p.Y == v.Y) || (q.X == v.X && q.Y == v.Y)) { at = null; best = -1; break; }
                double d = SegDist(v, p, q, out double t);
                if (t <= 0 || t >= 1) continue;
                if (d < best) { best = d; at = (ri, j); bt = t; }
            }
            if (at == null) continue;
            if (!ins.TryGetValue(at.Value, out var l)) ins[at.Value] = l = new();
            l.Add((bt, v)); lr.SegInsert++;
        }
        if (ins.Count == 0) return;
        for (int ri = 0; ri < rings.Count; ri++)
        {
            var P = rings[ri].P; var np = new List<Coordinate>(P.Count + 8);
            for (int j = 0; j < P.Count; j++)
            {
                np.Add(P[j]);
                if (ins.TryGetValue((ri, j), out var l)) np.AddRange(l.OrderBy(q => q.T).Select(q => q.C));
            }
            rings[ri].P = np;
        }
    }

    /// <summary>바탕 변 1 mm 안(끝점 곁 아님)의 고리 꼭짓점은 그 변에서 2 mm로 비킨다 — 그대로 두면 변 교차가 그 꼭짓점 곁에 생겨 1 mm 안 쌍 · 바늘 조각이 된다.</summary>
    static void PushOffEdges(List<Ring> rings, Dictionary<(double, double), double> bz, List<(Coordinate A, Coordinate B)> edges, STRtree<int> etree, Options opt, LayerReport lr)
    {
        foreach (var r in rings)
            for (int k = 0; k < r.P.Count; k++)
                for (int it = 0; it < 3; it++)
                {
                    var c = r.P[k];
                    if (bz.ContainsKey((c.X, c.Y))) break;   // 바탕 꼭짓점에 붙은 점은 그대로
                    int hit = -1; double hd = opt.Snap;
                    foreach (int e in etree.Query(new Envelope(c.X - opt.Snap, c.X + opt.Snap, c.Y - opt.Snap, c.Y + opt.Snap)))
                    {
                        double d = SegDist(c, edges[e].A, edges[e].B, out double t);
                        if (t <= 0 || t >= 1) continue;
                        if (d < hd) { hd = d; hit = e; }
                    }
                    if (hit < 0) break;
                    var nb = r.P[(k + 1) % r.P.Count]; var pb = r.P[(k + r.P.Count - 1) % r.P.Count];
                    r.P[k] = PushFrom(c, edges[hit].A, edges[hit].B, nb, pb, 2 * opt.Snap);
                    lr.Push++;
                }
    }

    /// <summary>점 c를 변 (a,b) 직선에서 dist만큼 떨어진 자리로 — 이미 있는 쪽 · 직선 위면 이웃(다음 · 앞) 쪽 · 그것도 위면 왼쪽.</summary>
    static Coordinate PushFrom(Coordinate c, Coordinate a, Coordinate b, Coordinate next, Coordinate prev, double dist)
    {
        double dx = b.X - a.X, dy = b.Y - a.Y, L = Math.Sqrt(dx * dx + dy * dy);
        if (L == 0) return c;
        double nx = -dy / L, ny = dx / L;
        int s = (int)Orientation.Index(a, b, c);
        if (s == 0) s = (int)Orientation.Index(a, b, next);
        if (s == 0) s = (int)Orientation.Index(a, b, prev);
        if (s == 0) s = 1;
        double t = ((c.X - a.X) * dx + (c.Y - a.Y) * dy) / (L * L);
        double fx = a.X + dx * t, fy = a.Y + dy * t;
        return new Coordinate(fx + nx * dist * s, fy + ny * dist * s);
    }

    static Coordinate? NearestVertex(Dictionary<(long, long), List<Coordinate>> vgrid, Coordinate c, double tol)
    {
        var g = Cell(c.X, c.Y, tol);
        Coordinate? best = null; double bd = tol;
        for (long a = g.Item1 - 1; a <= g.Item1 + 1; a++)
            for (long b = g.Item2 - 1; b <= g.Item2 + 1; b++)
            {
                if (!vgrid.TryGetValue((a, b), out var l)) continue;
                foreach (var v in l)
                {
                    double d = Math.Sqrt((v.X - c.X) * (v.X - c.X) + (v.Y - c.Y) * (v.Y - c.Y));
                    if (d < bd || (d == bd && best != null && Cmp(v, best) < 0)) { bd = d; best = v; }
                }
            }
        return best;
    }

    static void Dedupe(List<Coordinate> P)
    {
        for (int i = P.Count - 1; i > 0; i--) if (P[i].X == P[i - 1].X && P[i].Y == P[i - 1].Y) P.RemoveAt(i);
        while (P.Count > 1 && P[0].X == P[^1].X && P[0].Y == P[^1].Y) P.RemoveAt(P.Count - 1);
    }

    /// <summary>B · 걸친 세모 · 그 변 · ∂B × 바탕 변 노드(같은 좌표 값을 양쪽에 — 비트 그대로 이어짐).</summary>
    static Arr Arrange(List<Ring> rings, int nPoly, GeometryFactory gf, List<Tri> cur, List<int> cand, Dictionary<int, (int, int, int)> triE,
                       List<(Coordinate A, Coordinate B)> edges, Dictionary<(double, double), double> bz, RobustLineIntersector li)
    {
        var A = new Arr();
        A.B = BuildB(rings, nPoly, gf) ?? throw new InvalidOperationException("넓힌 발자국 고리가 무효");
        A.Loc = new IndexedPointInAreaLocator(A.B);
        A.Segs = SegTree(rings);
        var eset = new SortedSet<int>();
        foreach (int i in cand)
        {
            if (!Touches(cur[i], A.Segs, rings, A.Loc, li)) continue;
            A.T.Add(i);
            var (e1, e2, e3) = triE[i]; eset.Add(e1); eset.Add(e2); eset.Add(e3);
        }
        A.E = eset.ToList();
        var etree = new STRtree<int>();
        foreach (int e in A.E) etree.Insert(new Envelope(edges[e].A, edges[e].B), e);
        etree.Build();
        for (int r = 0; r < rings.Count; r++)
        {
            var P = rings[r].P;
            var perSeg = new List<List<RNode>>(P.Count);
            for (int j = 0; j < P.Count; j++)
            {
                var p = P[j]; var q = P[(j + 1) % P.Count];
                var nodes = new List<RNode>();
                double dx = q.X - p.X, dy = q.Y - p.Y, l2 = dx * dx + dy * dy;
                foreach (int e in etree.Query(new Envelope(p, q)))
                {
                    var (a, b) = edges[e];
                    li.ComputeIntersection(p, q, a, b);
                    if (!li.HasIntersection) continue;
                    for (int k = 0; k < li.IntersectionNum; k++)
                    {
                        var g = li.GetIntersection(k);
                        Coordinate c = Same(g, a) ? a : Same(g, b) ? b : Same(g, p) ? p : Same(g, q) ? q : new Coordinate(g.X, g.Y);
                        bool onRingEnd = ReferenceEquals(c, p) || ReferenceEquals(c, q) || Same(c, p) || Same(c, q);
                        bool onEdgeEnd = Same(c, a) || Same(c, b);
                        if (!onRingEnd && l2 > 0) nodes.Add(new RNode(((c.X - p.X) * dx + (c.Y - p.Y) * dy) / l2, c, onEdgeEnd || bz.ContainsKey((c.X, c.Y)), onEdgeEnd ? -1 : e));
                        if (!onEdgeEnd)
                        {
                            double ex = b.X - a.X, ey = b.Y - a.Y, m2 = ex * ex + ey * ey;
                            if (!A.EN.TryGetValue(e, out var en)) A.EN[e] = en = new();
                            if (!en.Any(q2 => Same(q2.C, c))) en.Add((((c.X - a.X) * ex + (c.Y - a.Y) * ey) / m2, c));
                        }
                    }
                }
                // 같은 점은 하나로(바탕 꼭짓점이면 그 표시를 남김) · 매개 차례
                nodes.Sort((u, v) => u.T.CompareTo(v.T));
                var uniq = new List<RNode>(nodes.Count);
                foreach (var n in nodes)
                {
                    int at = uniq.FindIndex(u => Same(u.C, n.C));
                    if (at < 0) uniq.Add(n);
                    else if (n.BaseV && !uniq[at].BaseV) uniq[at] = n;
                }
                perSeg.Add(uniq);
            }
            A.RN.Add(perSeg);
        }
        return A;
    }

    static bool Touches(Tri t, STRtree<(int R, int J)> segs, List<Ring> rings, IPointOnGeometryLocator loc, RobustLineIntersector li)
    {
        var a = new Coordinate(t.A.X, t.A.Y); var b = new Coordinate(t.B.X, t.B.Y); var c = new Coordinate(t.C.X, t.C.Y);
        int o = (int)Orientation.Index(a, b, c);
        foreach (var (r, j) in segs.Query(TriEnv(t)))
        {
            var P = rings[r].P; var p = P[j]; var q = P[(j + 1) % P.Count];
            if (o != 0 && (In(p) || In(q))) return true;
            li.ComputeIntersection(p, q, a, b); if (li.HasIntersection) return true;
            li.ComputeIntersection(p, q, b, c); if (li.HasIntersection) return true;
            li.ComputeIntersection(p, q, c, a); if (li.HasIntersection) return true;
        }
        return loc.Locate(Centroid(t)) == Location.Interior;
        bool In(Coordinate p) => (int)Orientation.Index(a, b, p) != -o && (int)Orientation.Index(b, c, p) != -o && (int)Orientation.Index(c, a, p) != -o;
    }

    /// <summary>고칠 것 — 고리 꼭짓점 옮김(붙임 · 비킴) · 뺌 · 선분에 바탕 꼭짓점 끼움.</summary>
    sealed class Fix { public int Kind; public int R, K; public Coordinate C = null!; public double T; }
    const int FixMove = 0, FixRemove = 1, FixInsert = 2;

    /// <summary>1 mm 안 쌍(새 점이 낀 것 — 고리 꼭짓점 · 교차 / 바탕 꼭짓점)을 찾아 고칠 것을 낸다. <paramref name="count"/>면 고치지 않고 남은 쌍을 까닭별로 센다.</summary>
    static List<Fix> NearFixes(Arr A, List<Ring> rings, List<(Coordinate A, Coordinate B)> edges, Dictionary<(double, double), double> bz, Options opt, LayerReport lr, bool count)
    {
        // 점: 0 = 고리 꼭짓점(바탕 꼭짓점 아님) · 1 = 교차(고리 r 선분 k · 변 e) · 2 = 바탕 꼭짓점(걸친 세모)
        var pts = new List<(Coordinate C, int Kind, int R, int K, int E)>();
        var seen = new Dictionary<(double, double), int>();
        void Add(Coordinate c, int kind, int r, int k, int e)
        {
            if (seen.TryGetValue((c.X, c.Y), out int at)) { if (kind < pts[at].Kind) pts[at] = (c, kind, r, k, e); return; }
            seen[(c.X, c.Y)] = pts.Count; pts.Add((c, kind, r, k, e));
        }
        for (int r = 0; r < rings.Count; r++)
        {
            var P = rings[r].P;
            for (int k = 0; k < P.Count; k++)
            {
                Add(P[k], bz.ContainsKey((P[k].X, P[k].Y)) ? 2 : 0, r, k, -1);
                foreach (var n in A.RN[r][k]) Add(n.C, n.BaseV ? 2 : 1, r, k, n.Edge);
            }
        }
        foreach (int e in A.E) { Add(edges[e].A, 2, -1, -1, -1); Add(edges[e].B, 2, -1, -1, -1); }
        var grid = new Dictionary<(long, long), List<int>>();
        for (int i = 0; i < pts.Count; i++)
        {
            var g = Cell(pts[i].C.X, pts[i].C.Y, opt.Snap);
            if (!grid.TryGetValue(g, out var l)) grid[g] = l = new();
            l.Add(i);
        }
        var pairs = new List<(double D, int I, int J)>();
        for (int i = 0; i < pts.Count; i++)
        {
            if (pts[i].Kind == 2) continue;
            var c = pts[i].C; var g = Cell(c.X, c.Y, opt.Snap);
            for (long a = g.Item1 - 1; a <= g.Item1 + 1; a++)
                for (long b = g.Item2 - 1; b <= g.Item2 + 1; b++)
                {
                    if (!grid.TryGetValue((a, b), out var l)) continue;
                    foreach (int j in l)
                    {
                        if (j == i || (pts[j].Kind != 2 && j < i)) continue;
                        var q = pts[j].C;
                        double d = Math.Sqrt((q.X - c.X) * (q.X - c.X) + (q.Y - c.Y) * (q.Y - c.Y));
                        if (d < opt.Snap) pairs.Add((d, i, j));
                    }
                }
        }
        pairs.Sort((u, v) => u.D != v.D ? u.D.CompareTo(v.D) : Cmp(pts[u.I].C, pts[v.I].C) != 0 ? Cmp(pts[u.I].C, pts[v.I].C) : Cmp(pts[u.J].C, pts[v.J].C));
        var fixes = new List<Fix>();
        if (count)
        {
            foreach (var (d, i, j) in pairs)
            {
                int ki = pts[i].Kind, kj = pts[j].Kind;
                if (ki == 1 && kj == 1) lr.NearCross++;
                else if ((ki == 1 && kj == 2) || (ki == 2 && kj == 1)) lr.NearCrossBase++;
                else if (ki == 0 || kj == 0) lr.NearRing++;
                else lr.NearOther++;
                if (d < lr.NearMin) { lr.NearMin = d; lr.NearAt = At(pts[i].C.X, pts[i].C.Y); }
            }
            return fixes;
        }
        var usedV = new HashSet<(int, int)>(); var usedS = new HashSet<(int, int)>();
        void Move(int r, int k, Coordinate to) { if (!usedV.Add((r, k))) return; fixes.Add(new Fix { Kind = FixMove, R = r, K = k, C = to }); }
        // 바탕 꼭짓점 v를 가장 가까운 고리 선분에 끼움(쐐기 · 교차-바탕 쌍) — WedgeSnap 안일 때만 · 끝점 1 mm 안이면 그 끝점을 v로
        bool InsertV(Coordinate v)
        {
            double best = opt.WedgeSnap; (int R, int J)? at = null; double bt = 0;
            foreach (var (ri, j) in A.Segs.Query(new Envelope(v.X - opt.WedgeSnap, v.X + opt.WedgeSnap, v.Y - opt.WedgeSnap, v.Y + opt.WedgeSnap)))
            {
                var P = rings[ri].P; var p = P[j]; var q = P[(j + 1) % P.Count];
                double d = SegDist(v, p, q, out double t);
                if (d < best || (d == best && at != null && (ri, j).CompareTo(at.Value) < 0)) { best = d; at = (ri, j); bt = t; }
            }
            if (at == null) return false;
            var (r0, j0) = at.Value; var P0 = rings[r0].P;
            var e0 = P0[j0]; var e1 = P0[(j0 + 1) % P0.Count];
            if (Same(e0, v) || Same(e1, v)) return false;
            if (Dist(e0, v) < opt.Snap && !bz.ContainsKey((e0.X, e0.Y))) { Move(r0, j0, new Coordinate(v.X, v.Y)); return true; }
            if (Dist(e1, v) < opt.Snap && !bz.ContainsKey((e1.X, e1.Y))) { Move(r0, (j0 + 1) % P0.Count, new Coordinate(v.X, v.Y)); return true; }
            if (bt <= 0 || bt >= 1 || !usedS.Add((r0, j0))) return false;
            fixes.Add(new Fix { Kind = FixInsert, R = r0, K = j0, C = new Coordinate(v.X, v.Y), T = bt });
            return true;
        }
        foreach (var (d, i0, j0) in pairs)
        {
            var pi = pts[i0]; var pj = pts[j0];
            // 고리 꼭짓점(0)을 앞으로
            if (pj.Kind == 0 && pi.Kind != 0) (pi, pj) = (pj, pi);
            if (pi.Kind == 0 && pj.Kind == 2) { Move(pi.R, pi.K, new Coordinate(pj.C.X, pj.C.Y)); lr.VertSnap++; continue; }
            if (pi.Kind == 0 && pj.Kind == 1)
            {
                var P = rings[pi.R].P;
                Move(pi.R, pi.K, PushFrom(pi.C, edges[pj.E].A, edges[pj.E].B, P[(pi.K + 1) % P.Count], P[(pi.K + P.Count - 1) % P.Count], 2 * opt.Snap));
                lr.Push++; continue;
            }
            if (pi.Kind == 0 && pj.Kind == 0)
            {
                // 같은 고리 이웃 꼭짓점이면 뒤 것을 뺌 · 아니면(고리 두 자리가 1 mm 안) 그대로 둠
                if (pi.R == pj.R && (Math.Abs(pi.K - pj.K) == 1 || Math.Abs(pi.K - pj.K) == rings[pi.R].P.Count - 1) && usedV.Add((pj.R, pj.K)))
                { fixes.Add(new Fix { Kind = FixRemove, R = pj.R, K = pj.K }); lr.Merge++; }
                continue;
            }
            if (pi.Kind == 1 && pj.Kind == 2) { if (InsertV(pj.C)) lr.Wedge++; continue; }
            if (pi.Kind == 1 && pj.Kind == 1)
            {
                var (a1, b1) = edges[pi.E]; var (a2, b2) = edges[pj.E];
                if (pi.E == pj.E)
                {
                    // 한 변을 고리가 1 mm 안에서 두 번 지남 — 그 사이 고리 꼭짓점을 그 변에서 비킴
                    if (pi.R == pj.R)
                    {
                        int n = rings[pi.R].P.Count, k = (Math.Min(pi.K, pj.K) + 1) % n;
                        if (Math.Abs(pi.K - pj.K) == n - 1) k = 0;
                        var P = rings[pi.R].P;
                        if (!bz.ContainsKey((P[k].X, P[k].Y))) { Move(pi.R, k, PushFrom(P[k], a1, b1, P[(k + 1) % n], P[(k + n - 1) % n], 2 * opt.Snap)); lr.Push++; }
                    }
                    continue;
                }
                Coordinate? v = Same(a1, a2) || Same(a1, b2) ? a1 : Same(b1, a2) || Same(b1, b2) ? b1 : null;
                if (v != null && InsertV(v)) lr.Wedge++;
                continue;
            }
        }
        return fixes;
    }

    static List<Ring> ApplyFixes(List<Ring> rings, List<Fix> fixes, LayerReport lr)
    {
        var res = rings.Select(r => r.Copy()).ToList();
        var mv = new Dictionary<(int, int), Coordinate>(); var rm = new HashSet<(int, int)>(); var ins = new Dictionary<(int, int), List<(double T, Coordinate C)>>();
        foreach (var f in fixes)
        {
            if (f.Kind == FixMove) mv[(f.R, f.K)] = f.C;
            else if (f.Kind == FixRemove) rm.Add((f.R, f.K));
            else { if (!ins.TryGetValue((f.R, f.K), out var l)) ins[(f.R, f.K)] = l = new(); l.Add((f.T, f.C)); }
        }
        for (int r = 0; r < res.Count; r++)
        {
            var P = res[r].P; var np = new List<Coordinate>(P.Count + 4);
            for (int k = 0; k < P.Count; k++)
            {
                if (!rm.Contains((r, k))) np.Add(mv.TryGetValue((r, k), out var c) ? c : P[k]);
                if (ins.TryGetValue((r, k), out var l)) np.AddRange(l.OrderBy(q => q.T).Select(q => q.C));
            }
            Dedupe(np);
            res[r].P = np;
        }
        return res;
    }

    // ══════════════════════════════ 면 → 세모 ══════════════════════════════

    /// <summary>면 하나 — 새 점 없이 제약 들로네. 꼭짓점이 다 쓰였나 · 넓이 합 = 면 넓이 · 남의 점이 없나를 잰다(아니면 null).</summary>
    static List<(Coordinate A, Coordinate B, Coordinate C)>? Triangulate(Polygon pg, out string why)
    {
        why = "";
        var verts = new HashSet<(double, double)>();
        void AddRing(LineString ls) { foreach (var c in ls.Coordinates) verts.Add((c.X, c.Y)); }
        AddRing(pg.ExteriorRing);
        for (int h = 0; h < pg.NumInteriorRings; h++) AddRing(pg.GetInteriorRingN(h));
        var res = new List<(Coordinate, Coordinate, Coordinate)>();
        if (pg.NumInteriorRings == 0 && pg.ExteriorRing.NumPoints == 4)
        {
            var cs = pg.ExteriorRing.Coordinates;
            res.Add((cs[0], cs[1], cs[2]));
            return res;
        }
        Geometry g;
        try { g = ConstrainedDelaunayTriangulator.Triangulate(pg); }
        catch (Exception ex) { why = $"들로네 예외({pg.InteriorPoint.X:F3},{pg.InteriorPoint.Y:F3}) {ex.Message}"; return null; }
        var used = new HashSet<(double, double)>();
        double sum = 0;
        for (int i = 0; i < g.NumGeometries; i++)
        {
            var cs = g.GetGeometryN(i).Coordinates;
            if (cs.Length < 3) continue;
            for (int k = 0; k < 3; k++)
            {
                if (!verts.Contains((cs[k].X, cs[k].Y))) { why = $"남의 점({cs[k].X:F4},{cs[k].Y:F4})"; return null; }
                used.Add((cs[k].X, cs[k].Y));
            }
            sum += Math.Abs((cs[1].X - cs[0].X) * (cs[2].Y - cs[0].Y) - (cs[1].Y - cs[0].Y) * (cs[2].X - cs[0].X)) / 2;   // 차이로(현장 좌표 곱셈 오차 없게)
            res.Add((cs[0], cs[1], cs[2]));
        }
        double area = pg.Area;
        if (used.Count != verts.Count) { why = $"꼭짓점 빠짐 {verts.Count - used.Count}({pg.InteriorPoint.X:F3},{pg.InteriorPoint.Y:F3})"; return null; }
        if (Math.Abs(sum - area) > 1e-9 * area + 1e-10) { why = string.Format(CultureInfo.InvariantCulture, "넓이 {0:E3}/{1:E3}({2:F3},{3:F3})", sum, area, pg.InteriorPoint.X, pg.InteriorPoint.Y); return null; }
        return res;
    }

    /// <summary>걸친 세모 중 (x,y)를 품는 것의 평면 높이 — 가장 깊이 품는 것(무게 좌표 최소가 가장 큰 것 · −1e-9까지) · 없으면 null.
    /// <para>★[11 · SF⑧ 계측] 꼭짓점을 좌표 차례로 놓고 셈 · 같으면 세모 좌표 차례로 고름 — 세모 · 꼭짓점 차례가 바뀌어도 마지막 비트까지 같은 높이.</para></summary>
    static double? PlaneZ(STRtree<int> idx, List<Tri> cur, double x, double y, out int which)
    {
        which = -1; double best = -1e-9; double? z = null;
        foreach (int i in idx.Query(new Envelope(x, x, y, y)))
        {
            var t = Canon(cur[i]);
            double x1 = t.B.X - t.A.X, y1 = t.B.Y - t.A.Y, x2 = t.C.X - t.A.X, y2 = t.C.Y - t.A.Y, px = x - t.A.X, py = y - t.A.Y;
            double d = x1 * y2 - y1 * x2;
            if (d == 0) continue;
            double l1 = (px * y2 - py * x2) / d, l2 = (x1 * py - y1 * px) / d, l0 = 1 - l1 - l2;
            double m = Math.Min(l0, Math.Min(l1, l2));
            if (m > best || (m == best && which >= 0 && CompareTri(cur[i], cur[which]) < 0)) { best = m; which = i; z = t.A.Z + l1 * (t.B.Z - t.A.Z) + l2 * (t.C.Z - t.A.Z); }
        }
        return z;
    }

    /// <summary>가파른 띠 — 둘레 차례 점(닫지 않음)에서 |Δv| &gt; SteepDv가 이어진 구간 · 시작 · 끝은 이웃 점과 Δv 선형 보간 · SteepLen 넘으면 기록.
    /// <para>★[v111.8 · 실제 자료 1103] 구간 표본이 모두 한 변(<paramref name="cs"/>의 k → k+1)의 안쪽이면 그 변 길이 · 두 끝 |Δv|(<paramref name="vdv"/>)를 적는다 — <see cref="SteepRun.Crease"/>.</para></summary>
    static void Runs(List<(double X, double Y, double Dv, int E)> ring, Options opt, LayerReport lr, Coordinate[] cs, double[] vdv)
    {
        int nv = vdv.Length;
        SteepRun Edge(SteepRun r, int s0, int s1, int start)
        {
            int e = ring[(start + s0) % ring.Count].E;
            if (e < 0) return r;
            for (int s = s0; s <= s1; s++) if (ring[(start + s) % ring.Count].E != e) return r;
            double L = Math.Sqrt((cs[e + 1].X - cs[e].X) * (cs[e + 1].X - cs[e].X) + (cs[e + 1].Y - cs[e].Y) * (cs[e + 1].Y - cs[e].Y));
            double d0 = vdv[e], d1 = vdv[(e + 1) % nv];
            return r with { EdgeLen = L, EdgeEndDv = double.IsFinite(d0) && double.IsFinite(d1) ? Math.Max(Math.Abs(d0), Math.Abs(d1)) : double.NaN };
        }
        int n = ring.Count;
        if (n < 2) return;
        bool S(int i) => double.IsFinite(ring[i].Dv) && Math.Abs(ring[i].Dv) > opt.SteepDv;
        int start = -1;
        for (int i = 0; i < n; i++) if (!S(i)) { start = i; break; }
        double D(int i, int j) => Math.Sqrt((ring[i].X - ring[j].X) * (ring[i].X - ring[j].X) + (ring[i].Y - ring[j].Y) * (ring[i].Y - ring[j].Y));
        if (start < 0)
        {
            double per = 0, mx = 0;
            for (int i = 0; i < n; i++) { per += D(i, (i + 1) % n); if (Math.Abs(ring[i].Dv) > Math.Abs(mx)) mx = ring[i].Dv; }
            if (per > opt.SteepLen) lr.Steep.Add(new SteepRun(lr.Name, lr.Handle, ring[0].X, ring[0].Y, ring[0].X, ring[0].Y, per, mx));
            return;
        }
        // 차례 seq[s] = (start + s) % n · seq[0] = seq[n] = 가파르지 않은 점
        (double X, double Y, double L) Cut(int u, int w)
        {
            // u = 가파르지 않은 점 · w = 가파른 점 — |Δv| = 문턱인 자리(없으면 w)
            double du = ring[u].Dv, dw = ring[w].Dv;
            if (!double.IsFinite(du) || dw == du) return (ring[w].X, ring[w].Y, 0);
            double s = (Math.Sign(dw) * opt.SteepDv - du) / (dw - du);
            s = Math.Clamp(s, 0, 1);
            double x = ring[u].X + (ring[w].X - ring[u].X) * s, y = ring[u].Y + (ring[w].Y - ring[u].Y) * s;
            return (x, y, D(u, w) * (1 - s));
        }
        int sIdx = 1;
        while (sIdx < n)
        {
            int k = (start + sIdx) % n;
            if (!S(k)) { sIdx++; continue; }
            int s0 = sIdx;
            while (sIdx + 1 < n && S((start + sIdx + 1) % n)) sIdx++;
            int s1 = sIdx;
            int i0 = (start + s0) % n, i1 = (start + s1) % n, before = (start + s0 - 1) % n, after = (start + s1 + 1) % n;
            var a = Cut(before, i0); var b = Cut(after, i1);
            double len = a.L + b.L, mx = 0;
            for (int s = s0; s <= s1; s++)
            {
                int i = (start + s) % n;
                if (Math.Abs(ring[i].Dv) > Math.Abs(mx)) mx = ring[i].Dv;
                if (s < s1) len += D(i, (start + s + 1) % n);
            }
            if (len > opt.SteepLen) lr.Steep.Add(Edge(new SteepRun(lr.Name, lr.Handle, a.X, a.Y, b.X, b.Y, len, mx), s0, s1, start));
            sIdx++;
        }
    }

    // ══════════════════════════════ 검사 ══════════════════════════════

    /// <summary>최종 검사 — 같은 XY 두 높이(전체) · 넓이(전체) · 손댄 자리(손댄 세모 + 도로 세모 + 그 곁 세모)의 면 검사(세 세모 변 · 뒤집힘 · T자 · 겹침 · 넓이 0 · 쌍 · 바늘).</summary>
    static void CheckFinal(List<Tri> fin, HashSet<Tri> untouched, HashSet<Tri> zoneSet, HashSet<(double, double)> roadPts, Report R)
    {
        var z1 = new Dictionary<(long, long), double>(fin.Count * 2);
        double area = 0;
        foreach (var t in fin)
        {
            area += Area(t);
            foreach (var p in V3(t))
            {
                var k = ((long)Math.Round(p.X / 1e-7), (long)Math.Round(p.Y / 1e-7));
                if (z1.TryGetValue(k, out double z)) { if (z != p.Z) R.TwoZ++; }
                else z1[k] = p.Z;
            }
        }
        R.AreaFinal = area;
        // 손댄 자리 = 정지면 그대로가 아닌 세모 + 그 꼭짓점을 나누는 정지면 세모
        var touched = new List<Tri>(); var tv = new HashSet<(double, double)>();
        foreach (var t in fin) if (!untouched.Contains(t)) { touched.Add(t); tv.Add((t.A.X, t.A.Y)); tv.Add((t.B.X, t.B.Y)); tv.Add((t.C.X, t.C.Y)); }
        var local = new List<Tri>(touched);
        foreach (var t in fin) if (untouched.Contains(t) && (tv.Contains((t.A.X, t.A.Y)) || tv.Contains((t.B.X, t.B.Y)) || tv.Contains((t.C.X, t.C.Y)))) local.Add(t);
        // 새 점 = 정지면(그대로 세모) · 도로 꼭짓점이 아닌 점
        var oldPts = new HashSet<(double, double)>(roadPts);
        foreach (var t in local) if (untouched.Contains(t)) { oldPts.Add((t.A.X, t.A.Y)); oldPts.Add((t.B.X, t.B.Y)); oldPts.Add((t.C.X, t.C.Y)); }
        var vid = new Dictionary<(double, double), int>(); var X = new List<double>(); var Y = new List<double>();
        int Id(Point3 p) { var k = (p.X, p.Y); if (vid.TryGetValue(k, out int i)) return i; i = X.Count; vid[k] = i; X.Add(p.X); Y.Add(p.Y); return i; }
        var tl = new List<(int A, int B, int C)>(local.Count);
        var isZone = new List<bool>(local.Count); var isRoad = new List<bool>(local.Count);
        foreach (var t in local)
        {
            int a = Id(t.A), b = Id(t.B), c = Id(t.C);
            double cr = (X[b] - X[a]) * (Y[c] - Y[a]) - (Y[b] - Y[a]) * (X[c] - X[a]);
            bool zn = zoneSet.Contains(t), rd = !zn && !untouched.Contains(t);
            if (zn && (a == b || b == c || c == a || Math.Abs(cr) < 1e-14)) R.ZeroArea++;
            tl.Add(cr >= 0 ? (a, b, c) : (a, c, b)); isZone.Add(zn); isRoad.Add(rd);
            double l1 = Math.Sqrt((X[a] - X[b]) * (X[a] - X[b]) + (Y[a] - Y[b]) * (Y[a] - Y[b])), l2 = Math.Sqrt((X[b] - X[c]) * (X[b] - X[c]) + (Y[b] - Y[c]) * (Y[b] - Y[c])), l3 = Math.Sqrt((X[c] - X[a]) * (X[c] - X[a]) + (Y[c] - Y[a]) * (Y[c] - Y[a]));
            double lm = Math.Max(l1, Math.Max(l2, l3));
            if (lm > 0 && Math.Abs(cr) / lm < CivilSafeMesh.ThinH) { if (zn) R.Thin++; else if (rd) R.ThinRoad++; }
        }
        // 쌍 — 새 점이 낀 것(정지면 · 도로가 원래 가진 쌍은 Civil이 이미 받은 것)
        {
            var grid = new Dictionary<(long, long), List<int>>();
            for (int i = 0; i < X.Count; i++) { var g = Cell(X[i], Y[i], 1e-3); if (!grid.TryGetValue(g, out var l)) grid[g] = l = new(); l.Add(i); }
            for (int i = 0; i < X.Count; i++)
            {
                bool ni = !oldPts.Contains((X[i], Y[i]));
                var g = Cell(X[i], Y[i], 1e-3);
                for (long a = g.Item1 - 1; a <= g.Item1 + 1; a++)
                    for (long b = g.Item2 - 1; b <= g.Item2 + 1; b++)
                    {
                        if (!grid.TryGetValue((a, b), out var l)) continue;
                        foreach (int j in l)
                        {
                            if (j <= i) continue;
                            if (!ni && oldPts.Contains((X[j], Y[j]))) continue;
                            double dx = Math.Abs(X[j] - X[i]), dy = Math.Abs(Y[j] - Y[i]);
                            if (Math.Max(dx, dy) < CivilSafeMesh.MustMerge) R.Pairs01++;
                            if (dx * dx + dy * dy < 1e-6) R.Pairs1mm++;
                        }
                    }
            }
        }
        // 변마다 세모 수 · 같은 방향 두 번
        var dir = new Dictionary<(int, int), int>(); var und = new Dictionary<(int, int), int>();
        foreach (var (a, b, c) in tl)
            foreach (var (u, v) in new[] { (a, b), (b, c), (c, a) })
            {
                dir[(u, v)] = dir.TryGetValue((u, v), out int n) ? n + 1 : 1;
                var k = u < v ? (u, v) : (v, u);
                und[k] = und.TryGetValue(k, out int m) ? m + 1 : 1;
            }
        foreach (var kv in und) if (kv.Value > 2) R.NonManifold++;
        foreach (var kv in dir) if (kv.Value > 1) R.Flipped++;
        // T자 — 둘레 변(한 세모만 씀) 위에 남의 꼭짓점(1 m 칸)
        var used = new HashSet<int>(); foreach (var (a, b, c) in tl) { used.Add(a); used.Add(b); used.Add(c); }
        var vg = new Dictionary<(long, long), List<int>>();
        foreach (int v in used) { var key = Cell(X[v], Y[v], 1.0); if (!vg.TryGetValue(key, out var l)) vg[key] = l = new(); l.Add(v); }
        foreach (var kv in und)
        {
            if (kv.Value != 1) continue;
            var (a, b) = kv.Key;
            double ax = X[a], ay = Y[a], dx = X[b] - ax, dy = Y[b] - ay, l2 = dx * dx + dy * dy;
            if (l2 < 1e-14) continue;
            var g0 = Cell(Math.Min(ax, ax + dx), Math.Min(ay, ay + dy), 1.0); var g1 = Cell(Math.Max(ax, ax + dx), Math.Max(ay, ay + dy), 1.0);
            if ((g1.Item1 - g0.Item1 + 1) * (g1.Item2 - g0.Item2 + 1) > 4000) continue;
            bool hit = false;
            for (long gy = g0.Item2; gy <= g1.Item2 && !hit; gy++)
                for (long gx = g0.Item1; gx <= g1.Item1 && !hit; gx++)
                {
                    if (!vg.TryGetValue((gx, gy), out var cell)) continue;
                    foreach (int v in cell)
                    {
                        if (v == a || v == b) continue;
                        double t = ((X[v] - ax) * dx + (Y[v] - ay) * dy) / l2;
                        if (t <= 1e-9 || t >= 1 - 1e-9) continue;
                        double qx = ax + dx * t - X[v], qy = ay + dy * t - Y[v];
                        if (qx * qx + qy * qy < 1e-12) { hit = true; break; }
                    }
                }
            if (hit) R.TJunctions++;
        }
        // 겹침 — 끝점을 나누지 않는 두 변이 안에서 엇갈림 · 세모 가운데가 남의 세모 안
        {
            var eg = new Dictionary<(long, long), List<(int, int)>>();
            foreach (var kv in und)
            {
                var (a, b) = kv.Key;
                var g0 = Cell(Math.Min(X[a], X[b]), Math.Min(Y[a], Y[b]), 1.0); var g1 = Cell(Math.Max(X[a], X[b]), Math.Max(Y[a], Y[b]), 1.0);
                if ((g1.Item1 - g0.Item1 + 1) * (g1.Item2 - g0.Item2 + 1) > 4000) continue;
                for (long y = g0.Item2; y <= g1.Item2; y++) for (long x = g0.Item1; x <= g1.Item1; x++) { if (!eg.TryGetValue((x, y), out var l)) eg[(x, y)] = l = new(); l.Add((a, b)); }
            }
            var ca = new Coordinate(); var cb = new Coordinate(); var cc = new Coordinate(); var cd = new Coordinate();
            int O(int p, int q, int r) { ca.X = X[p]; ca.Y = Y[p]; cb.X = X[q]; cb.Y = Y[q]; cc.X = X[r]; cc.Y = Y[r]; return (int)Orientation.Index(ca, cb, cc); }
            var seen = new HashSet<((int, int), (int, int))>();
            foreach (var l in eg.Values)
                for (int i = 0; i < l.Count; i++)
                    for (int j = i + 1; j < l.Count; j++)
                    {
                        var (a, b) = l[i]; var (c, d) = l[j];
                        if (a == c || a == d || b == c || b == d) continue;
                        int o1 = O(a, b, c), o2 = O(a, b, d), o3 = O(c, d, a), o4 = O(c, d, b);
                        if (o1 * o2 < 0 && o3 * o4 < 0)
                        {
                            var key = l[i].CompareTo(l[j]) < 0 ? (l[i], l[j]) : (l[j], l[i]);
                            if (seen.Add(key)) R.Overlap++;
                        }
                    }
            var tg = new Dictionary<(long, long), List<int>>();
            for (int i = 0; i < tl.Count; i++)
            {
                var (a, b, c) = tl[i];
                var g0 = Cell(Math.Min(X[a], Math.Min(X[b], X[c])), Math.Min(Y[a], Math.Min(Y[b], Y[c])), 1.0);
                var g1 = Cell(Math.Max(X[a], Math.Max(X[b], X[c])), Math.Max(Y[a], Math.Max(Y[b], Y[c])), 1.0);
                if ((g1.Item1 - g0.Item1 + 1) * (g1.Item2 - g0.Item2 + 1) > 20000) continue;
                for (long y = g0.Item2; y <= g1.Item2; y++) for (long x = g0.Item1; x <= g1.Item1; x++) { if (!tg.TryGetValue((x, y), out var l)) tg[(x, y)] = l = new(); l.Add(i); }
            }
            for (int i = 0; i < tl.Count; i++)
            {
                var (a, b, c) = tl[i];
                double px = (X[a] + X[b] + X[c]) / 3, py = (Y[a] + Y[b] + Y[c]) / 3;
                if (!tg.TryGetValue(Cell(px, py, 1.0), out var l)) continue;
                foreach (int j in l)
                {
                    if (j == i) continue;
                    var (p, q, r) = tl[j];
                    double c1 = (X[q] - X[p]) * (py - Y[p]) - (Y[q] - Y[p]) * (px - X[p]);
                    double c2 = (X[r] - X[q]) * (py - Y[q]) - (Y[r] - Y[q]) * (px - X[q]);
                    double c3 = (X[p] - X[r]) * (py - Y[r]) - (Y[p] - Y[r]) * (px - X[r]);
                    if (c1 > 1e-12 && c2 > 1e-12 && c3 > 1e-12) { R.Overlap++; break; }
                }
            }
        }
    }

    // ══════════════════════════════ 작은 도움 ══════════════════════════════

    /// <summary>꼭짓점을 (X, Y) 차례로 — 같은 세모면 같은 셈(돌린 차례와 상관없이).</summary>
    static Tri Canon(Tri t)
    {
        Point3 a = t.A, b = t.B, c = t.C;
        static bool Gt(Point3 p, Point3 q) => p.X > q.X || (p.X == q.X && p.Y > q.Y);
        if (Gt(a, b)) (a, b) = (b, a);
        if (Gt(b, c)) (b, c) = (c, b);
        if (Gt(a, b)) (a, b) = (b, a);
        return new Tri(a, b, c);
    }
    static IEnumerable<Point3> V3(Tri t) { yield return t.A; yield return t.B; yield return t.C; }
    static double Area(Tri t) => Math.Abs((t.B.X - t.A.X) * (t.C.Y - t.A.Y) - (t.B.Y - t.A.Y) * (t.C.X - t.A.X)) / 2;
    static Tri Ccw(Tri t) => (t.B.X - t.A.X) * (t.C.Y - t.A.Y) - (t.B.Y - t.A.Y) * (t.C.X - t.A.X) >= 0 ? t : new Tri(t.A, t.C, t.B);
    static Envelope TriEnv(Tri t) => new(Math.Min(t.A.X, Math.Min(t.B.X, t.C.X)), Math.Max(t.A.X, Math.Max(t.B.X, t.C.X)), Math.Min(t.A.Y, Math.Min(t.B.Y, t.C.Y)), Math.Max(t.A.Y, Math.Max(t.B.Y, t.C.Y)));
    static Coordinate Centroid(Tri t) => new((t.A.X + t.B.X + t.C.X) / 3, (t.A.Y + t.B.Y + t.C.Y) / 3);
    static Polygon TriPoly(Tri t, GeometryFactory gf) => gf.CreatePolygon(new[] { new Coordinate(t.A.X, t.A.Y), new Coordinate(t.B.X, t.B.Y), new Coordinate(t.C.X, t.C.Y), new Coordinate(t.A.X, t.A.Y) });
    static ((double, double), (double, double)) EK(Point3 a, Point3 b) { var ka = (a.X, a.Y); var kb = (b.X, b.Y); return ka.CompareTo(kb) <= 0 ? (ka, kb) : (kb, ka); }
    static ((double, double), (double, double)) EK(Coordinate a, Coordinate b) { var ka = (a.X, a.Y); var kb = (b.X, b.Y); return ka.CompareTo(kb) <= 0 ? (ka, kb) : (kb, ka); }
    static (long, long) Cell(double x, double y, double s) => ((long)Math.Floor(x / s), (long)Math.Floor(y / s));
    static bool Same(Coordinate a, Coordinate b) => a.X == b.X && a.Y == b.Y;
    static double Dist(Coordinate a, Coordinate b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
    static int Cmp(Coordinate a, Coordinate b) { int c = a.X.CompareTo(b.X); return c != 0 ? c : a.Y.CompareTo(b.Y); }
    static string At(double x, double y) => string.Format(CultureInfo.InvariantCulture, "({0:F3},{1:F3})", x, y);

    static double SegDist(Coordinate p, Coordinate a, Coordinate b, out double t)
    {
        double dx = b.X - a.X, dy = b.Y - a.Y, l2 = dx * dx + dy * dy;
        t = l2 > 0 ? ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / l2 : 0;
        double tc = Math.Clamp(t, 0, 1);
        double qx = a.X + dx * tc - p.X, qy = a.Y + dy * tc - p.Y;
        return Math.Sqrt(qx * qx + qy * qy);
    }

    /// <summary>합집합 넓이 — 넓이 0 세모(Civil 면에 있을 수 있음)는 빼고 OverlayNG 튼튼한 합집합(CivilSafeMesh.Compare와 같은 길).</summary>
    static double UnionArea(List<Geometry> gs)
    {
        var ok = gs.Where(g => g != null && !g.IsEmpty && g.Area > 1e-12).ToList();
        if (ok.Count == 0) return 0;
        try
        {
            var u = NetTopologySuite.Operation.OverlayNG.OverlayNGRobust.Union(ok);
            return u?.Area ?? 0;
        }
        catch (Exception)
        {
            // ★[JACK 1104 실측] 넓이 대조용 합집합 — 부동소수 겹침 연산이 세모 수천 개의 거의 맞닿은 변에서 «Ring edge missing»으로 깨졌다(진입도로 끝 띠 조각 · 같은 자리의 도로 세모는 옛 판과 비트 같음 → 자리 탓 아님, 합치는 차례 탓).
            //   1 µm 격자 스냅 라운딩(늘 위상이 맞는 겹침 연산)으로 다시 셈 — 넓이 차는 둘레 × 1 µm 꼴(수 km 둘레에도 0.001 ㎡ 안 · 대조 한도 1e-6 × 넓이 안)
            var f = ok[0].Factory;
            var u = NetTopologySuite.Operation.OverlayNG.UnaryUnionNG.Union(f.BuildGeometry(ok), new PrecisionModel(1e6));
            return u?.Area ?? 0;
        }
    }

    /// <summary>세모 차례 — 꼭짓점을 (X, Y)로 정렬한 셋을 견줌(같으면 높이).</summary>
    static int CompareTri(Tri a, Tri b)
    {
        var ka = Key(a); var kb = Key(b);
        int c = ka.Item1.CompareTo(kb.Item1); if (c != 0) return c;
        c = ka.Item2.CompareTo(kb.Item2); if (c != 0) return c;
        c = ka.Item3.CompareTo(kb.Item3); if (c != 0) return c;
        c = (a.A.Z + a.B.Z + a.C.Z).CompareTo(b.A.Z + b.B.Z + b.C.Z);
        return c;
        static ((double, double), (double, double), (double, double)) Key(Tri t)
        {
            var p = (t.A.X, t.A.Y); var q = (t.B.X, t.B.Y); var r = (t.C.X, t.C.Y);
            if (p.CompareTo(q) > 0) (p, q) = (q, p);
            if (q.CompareTo(r) > 0) (q, r) = (r, q);
            if (p.CompareTo(q) > 0) (p, q) = (q, p);
            return (p, q, r);
        }
    }
}
