using System.Globalization;
using System.Text;
using NetTopologySuite.Geometries;
using NetTopologySuite.Geometries.Prepared;
using NetTopologySuite.Index.Strtree;
using NetTopologySuite.Operation.Distance;
using NetTopologySuite.Operation.OverlayNG;
using NetTopologySuite.Operation.Polygonize;

namespace DH.Grading.Core;

/// <summary>★★★[JACK 0918] <b>가상옹벽과 원지반이 닿는 선(데이라잇)</b> — 닫힌 링 하나.
///
/// <para>JACK: <i>"가상옹벽과 원지반이 닿는 데이라잇을 만드는 빌드를 바로 시작해.
/// 데이라잇은 3d폴리선으로 <b>닫힘이 '예'</b>인 상태로 만들어져야 해."</i></para>
///
/// <para>인터뷰로 정한 것: ①옹벽이 땅보다 <b>높은</b> 곳(나중에 잘려 나갈 섬)의 테두리 —
/// 앞·측선 계단이 땅에 닿는 선 + 폐합면(수직)이 땅에 닿는 선이라 <b>저절로 닫힌다</b>
/// ②상대는 <b>원지반만</b> ③여러 고리면 <b>가장 큰 것 하나</b>(버린 것은 적는다)
/// ④끊기면 <b>폴리곤 테두리로 이어</b> 닫되 그 길이를 밝힌다 ⑤겹친 점·1cm 안 일직선 점만 뺀다.</para>
///
/// <para><b>왜 <c>GetExactDaylight</c>를 안 쓰나.</b> 정지면용 땜질이 들어 있다 —
/// 25m 안의 끝점을 <b>검증 없이</b> 잇고(<c>MAXGAP</c>), 두 면 높이를 <b>평균</b> 낸다.
/// 옹벽 수직면(평면 폭 5mm~5cm)에서 그 평균이 튄다. 그 함수는 정지 경로라 <b>건드리지 않고</b> 새로 짠다.</para>
///
/// <para><b>길(검토 0918이 고친 판).</b>
/// ①삼각형 쌍마다 겹친 자리에서 높이차 D = 옹벽 − 원지반이 0이 되는 선분을 <b>부동소수로</b> 구한다
/// ②교점을 1mm로 군집 → 폴리곤 테두리·측량 경계와 함께 <b>1mm 격자 스냅라운딩</b>으로 잇는다
/// ③폴리고나이즈 → 조각마다 <b>테두리에서 2mm 넘게 떨어진</b> 표본으로 D의 부호를 잰다 —
/// 부호가 섞이면 선이 빠진 것이므로 <b>⚠오류</b>(다수결로 덮지 않는다)
/// ④D&gt;0 조각을 합쳐 가장 큰 덩이의 바깥 링 ⑤선의 <b>출처</b>로 닿는 선/테두리/측량경계를 가른다
/// ⑥0.5m 격자에서 부호와 「섬 안인가」를 <b>따로</b> 대조한다(투표에 쓴 표본을 다시 세지 않는다).</para></summary>
public static partial class WallDaylight
{
    public readonly record struct Tri(Point3 A, Point3 B, Point3 C);

    // ───────────── ★★[v102.0 · JACK 0929 «성토 — 절토의 거울»] 높이 뒤집기(−z) ─────────────
    //   성토 결과 = −(절토 셈(−입력)). 부호 바꾸기는 비트까지 정확하고, 셈은 z의 절댓값을 안 쓴다(부호 판정은 D = W − S ·
    //   비김·±ZeroD·⑦-b 양옆·가지치기·CivilSafeMesh |dz| 모두 거울 — 계획 검토 v102 Q1). ★절댓값이 들어가는 입력은 <b>폴리곤 z</b>
    //   (KeepBand zBase = 옹벽 밑선 높이)뿐이라, 들머리에서 z를 가진 입력을 <b>형마다 전부</b> 뒤집고(목록으로 적지 않는다 — H1),
    //   날머리에서 결과 클래스의 z 필드를 <b>전부</b> 되뒤집는다(각 클래스의 NegateZ — M1).
    //   Simplify3D · CivilSafeMesh.MakeZone · ⑦-b · ⑦-c는 뒤집은 셈 <b>안에서</b> 돈다(XY와 |dz|만 본다 — 거울이 정확하다 · 계획 검토 L5).

    /// <summary>하네스 전용 카나리아 — 이 이름의 뒤집기 하나를 <b>일부러 빼</b> S141(들머리·날머리 완전성)이 반드시 실패하는지 잰다
    /// ("poly" · "wall" · "other" · "ground" · "plan" · "ring" · "holes" · "ringsnap" · "holessnap" · "zone" · "untouched"). 출하 경로는 "".</summary>
    internal static string DebugSkipNeg = "";          // ★[코드 검토 낮음 9] 비어 있지 않으면 성토 결과가 조용히 틀린다 — 하네스만(InternalsVisibleTo blocktest)

    static List<Point3> NegZ(IReadOnlyList<Point3> ps, string what)
    {
        var r = new List<Point3>(ps?.Count ?? 0);
        if (ps == null) return r;
        bool skip = DebugSkipNeg == what;
        foreach (var q in ps) r.Add(skip ? q : new Point3(q.X, q.Y, -q.Z));
        return r;
    }

    static List<Tri> NegZ(IReadOnlyList<Tri> ts, string what)
    {
        var r = new List<Tri>(ts?.Count ?? 0);
        if (ts == null) return r;
        bool skip = DebugSkipNeg == what;
        static Point3 N(Point3 q) => new(q.X, q.Y, -q.Z);
        foreach (var t in ts) r.Add(skip ? t : new Tri(N(t.A), N(t.B), N(t.C)));
        return r;
    }

    static List<List<Point3>> NegZ(List<List<Point3>> rs, string what)
    {
        var r = new List<List<Point3>>(rs?.Count ?? 0);
        if (rs != null) foreach (var x in rs) r.Add(NegZ(x, what));
        return r;
    }

    /// <summary>뒤집은 셈의 추적표 첫 줄 뒤에 붙이는 말 — 표 속 높이 숫자는 부호가 반대다(명령줄 요약·⚠의 높이는 되뒤집어 찍는다).</summary>
    const string DownNote = "  ※<b>성토</b> — 높이를 뒤집어(−z) 절토와 같은 셈을 했다: 이 표 속 높이·표고·D는 부호가 반대다(선·면·요약은 되뒤집었다)\n";

    static string AfterFirstLine(string trace, string add)
    {
        int i = trace.IndexOf('\n');
        return i < 0 ? trace + "\n" + add : trace.Substring(0, i + 1) + add + trace.Substring(i + 1);
    }

    /// <summary>스냅라운딩 격자(1mm). 이 저장소의 NTS 규약(<c>NtsSupport.Factory</c>)과 같다.</summary>
    public const double SnapGrid = 0.001;
    /// <summary>교점 군집 반경. 이웃 쌍이 따로 구한 같은 교점은 1e-10쯤 어긋난다 — <b>그것만</b> 녹인다.
    /// <para>★★[검토 0918 v99.9 · 재현] 종전 값은 <b>1mm</b>였다. 주석은 «1e-10만 녹인다»였는데 반경은 1mm라
    /// 폐합면(1:0.001) 꼭짓점에서 <b>서로 다른 두 교점</b>이 먼저 온 점으로 끌려가 짝이 어긋났다 —
    /// 얕은 절토(원지반 = 계획고 + 0.5 + 0.3v)의 평범한 모양이 <b>끊긴 끝 566</b>으로 노랑이 됐다.
    /// 1mm 안의 진짜 구조는 뒤의 스냅라운딩 노딩이 맡는다(그것은 순서에 안 흔들린다).</para></summary>
    public const double ClusterTol = 1e-6;
    /// <summary>표본이 조각 테두리에서 떨어져야 할 거리. 스냅 이동(≤0.71mm)보다 커야
    /// 그 표본의 부호가 <b>반드시</b> 조각의 부호다.</summary>
    public const double SampleClear = 0.002;
    /// <summary>|D|가 이 이하면 「일치」(옹벽 = 땅). 섬(옹벽이 <b>더 높은</b> 곳)에 넣지 않는다.</summary>
    public const double ZeroD = 1e-6;
    /// <summary>선의 출처를 가르는 거리 — 스냅 이동 두 번분.</summary>
    public const double ProvTol = 0.0015;
    public const double DupTol = 0.001;
    public const double LineTol = 0.01;
    public const double GridStep = 0.5;
    /// <summary>★★[v100.2 · JACK «수직방향이 톱니처럼»] 1mm 스냅라운딩이 점을 옮기는 최대 거리(√2/2 mm)에 여유를 더한 <b>통로 폭</b> —
    /// 띠 링을 정확한 교선 위로 되돌릴 때 이 안의 선만 본다.</summary>
    public const double UnsnapTube = 0.00075;
    /// <summary>★[v100.2 · 검토 중간 5] Civil에 넘기는 링의 가장 짧은 변(10µm) — 더 가까운 점은 하나로 친다
    /// (µm 변은 Civil 비파괴 경계가 어떻게 자를지 모른다. 1:0.01 면에서 높이 1mm 이하).</summary>
    public const double MinRingEdge = 1e-5;

    public sealed class Result
    {
        /// <summary>닫힌 링(세계 좌표, 첫점 반복 없음). Z = 상대면(원지반·계획지표면). 못 만들면 null.</summary>
        public List<Point3>? Ring;
        public bool Warn;
        public string Summary = "";
        public string Trace = "";
        public int DegenWall, DegenGround, WallTris, GroundTris, HullEdges;
        public int Pairs, ZSkip, OverlayFail, Segments, Clustered;
        public int Dangles, CutEdges, InvalidRings;
        /// <summary>★[JACK 0918 현장 · 인터뷰] 끊긴 끝 중 <b>섬 경계에서 1cm 안</b>에만 있는 짧은 곁가지 —
        /// 링은 온전하다(선 정확도가 1cm이므로 그 안의 부스러기는 링을 못 바꾼다). <see cref="Dangles"/>에서 뺀다.</summary>
        public int Spurs;
        public double SpurMaxLen;
        public int Faces, FacesIsland, FacesOut, Mixed, FacesZero, FacesUnknown, FacesThin;
        /// <summary>모름 조각 중 <b>원지반</b>을 못 읽은 것 / <b>옹벽</b>을 못 읽은 것(검토 0918 · 낮음 4 — 까닭을 가른다).</summary>
        public int FacesUnknownGround, FacesUnknownWall;
        public double ZeroArea, UnknownArea, MixedArea;
        public int Islands, HolesInChosen;
        public double ChosenArea, DroppedArea, HoleArea;
        public double LenContact, LenPolyClose, LenHullClose, LenUnknown;
        public int UnknownProv;
        public int RawPts, FinalPts, Dups, Collinear, Inserted, GroundZMiss, ZFromNeighbor;
        public double MaxDev;
        public bool Simple, SimplifyFallback;
        public int OutsidePoly;
        public int GridChecked, GridMismatch;
        public long Ms;

        /// <summary>★★[검토 0918 · 중간 1] <b>선이 틀린 줄 아는가</b> — 참이면 정식 레이어에 그리면 안 된다
        /// (다음 단계가 이 선을 칼로 쓴다). 판정은 <b>여기 한 곳</b>에만 둔다 — Civil과 하네스가 같은 잣대를 쓰게.
        /// <para>측량 범위 밖·원지반 높이 못 읽음·버린 섬처럼 <b>선은 맞는데 알려야 하는</b> 것은 여기 안 든다(⚠만).</para></summary>
        public bool Broken => Ring == null || !Simple || Dangles > 0 || Mixed > 0 || GridMismatch > 0
                           || UnknownProv > 0 || InvalidRings > 0 || CutEdges > 0 || OutsidePoly > 0 || OverlayFail > 0;

        /// <summary>★[v102.0 · 성토] 뒤집은 셈의 결과를 되뒤집는다 — <b>z를 가진 필드는 여기 전부</b>(나머지는 수·넓이·길이·|차|).
        /// 필드를 더하면 여기에도 더할 것(하네스 S141이 비트로 대조한다).</summary>
        internal void NegateZ() { if (Ring != null) Ring = NegZ(Ring, "ring"); }
    }

    // ───────────────────────────── 삼각형 모음(지역 좌표) ─────────────────────────────
    private sealed class TriSet
    {
        public readonly int N;
        public readonly double[] X1, Y1, Z1, X2, Y2, Z2, X3, Y3, Z3, A, B, MinZ, MaxZ;
        public readonly bool[] Ok;
        public readonly STRtree<int> Tree = new();
        public int Degenerate;
        public string DegenAt = "";

        public TriSet(IReadOnlyList<Tri> tris, double ox, double oy)
        {
            N = tris.Count;
            X1 = new double[N]; Y1 = new double[N]; Z1 = new double[N];
            X2 = new double[N]; Y2 = new double[N]; Z2 = new double[N];
            X3 = new double[N]; Y3 = new double[N]; Z3 = new double[N];
            A = new double[N]; B = new double[N]; MinZ = new double[N]; MaxZ = new double[N];
            Ok = new bool[N];
            for (int i = 0; i < N; i++)
            {
                var t = tris[i];
                X1[i] = t.A.X - ox; Y1[i] = t.A.Y - oy; Z1[i] = t.A.Z;
                X2[i] = t.B.X - ox; Y2[i] = t.B.Y - oy; Z2[i] = t.B.Z;
                X3[i] = t.C.X - ox; Y3[i] = t.C.Y - oy; Z3[i] = t.C.Z;
                double ux = X2[i] - X1[i], uy = Y2[i] - Y1[i], uz = Z2[i] - Z1[i];
                double vx = X3[i] - X1[i], vy = Y3[i] - Y1[i], vz = Z3[i] - Z1[i];
                double nz = ux * vy - uy * vx;                       // 평면 넓이 × 2(부호 있음)
                MinZ[i] = Math.Min(Z1[i], Math.Min(Z2[i], Z3[i]));
                MaxZ[i] = Math.Max(Z1[i], Math.Max(Z2[i], Z3[i]));
                // ★평면에서 넓이가 없는 삼각형은 표고를 못 준다 — <b>세고 자리를 적는다</b>(검토 0918 · 낮음 10)
                if (Math.Abs(nz) < 1e-10)
                {
                    Degenerate++;
                    if (DegenAt.Length == 0) DegenAt = $"{t.A.X:F2},{t.A.Y:F2}";
                    continue;
                }
                A[i] = (uz * vy - uy * vz) / nz;
                B[i] = (ux * vz - uz * vx) / nz;
                Ok[i] = true;
                Tree.Insert(Env(i), i);
            }
            if (N > 0) Tree.Build();
        }

        public Envelope Env(int i) => new(
            Math.Min(X1[i], Math.Min(X2[i], X3[i])), Math.Max(X1[i], Math.Max(X2[i], X3[i])),
            Math.Min(Y1[i], Math.Min(Y2[i], Y3[i])), Math.Max(Y1[i], Math.Max(Y2[i], Y3[i])));

        public double Z(int i, double x, double y) => Z1[i] + A[i] * (x - X1[i]) + B[i] * (y - Y1[i]);

        private bool Inside(int i, double x, double y)
        {
            double d = (Y2[i] - Y3[i]) * (X1[i] - X3[i]) + (X3[i] - X2[i]) * (Y1[i] - Y3[i]);
            double u = ((Y2[i] - Y3[i]) * (x - X3[i]) + (X3[i] - X2[i]) * (y - Y3[i])) / d;
            double v = ((Y3[i] - Y1[i]) * (x - X3[i]) + (X1[i] - X3[i]) * (y - Y3[i])) / d;
            double w = 1.0 - u - v;
            return u >= -1e-9 && v >= -1e-9 && w >= -1e-9;
        }

        public bool TryZ(double x, double y, out double z)
        {
            z = 0;
            if (N == 0) return false;
            foreach (int i in Tree.Query(new Envelope(x, x, y, y)))
                if (Inside(i, x, y)) { z = Z(i, x, y); return true; }
            return false;
        }

        /// <summary>점을 품은 삼각형 번호(없으면 −1).</summary>
        public int At(double x, double y)
        {
            if (N == 0) return -1;
            foreach (int i in Tree.Query(new Envelope(x, x, y, y)))
                if (Inside(i, x, y)) return i;
            return -1;
        }

        /// <summary>안에 없으면 <paramref name="maxD"/> 안의 가장 가까운 삼각형 평면으로 늘여 읽는다 —
        /// 스냅으로 테두리 밖 1mm 안에 놓인 점을 위한 것이다.</summary>
        public bool TryZNear(double x, double y, double maxD, out double z)
        {
            if (TryZ(x, y, out z)) return true;
            if (N == 0) return false;
            double best = double.MaxValue; int bi = -1;
            foreach (int i in Tree.Query(new Envelope(x - maxD, x + maxD, y - maxD, y + maxD)))
            {
                double d = Math.Min(SegD(x, y, X1[i], Y1[i], X2[i], Y2[i]),
                           Math.Min(SegD(x, y, X2[i], Y2[i], X3[i], Y3[i]), SegD(x, y, X3[i], Y3[i], X1[i], Y1[i])));
                if (d < best) { best = d; bi = i; }
            }
            if (bi < 0 || best > maxD) return false;
            z = Z(bi, x, y);
            return true;
        }

        public Polygon Poly(int i, GeometryFactory gf) => gf.CreatePolygon(new[]
        {
            new Coordinate(X1[i], Y1[i]), new Coordinate(X2[i], Y2[i]),
            new Coordinate(X3[i], Y3[i]), new Coordinate(X1[i], Y1[i]),
        });
    }

    private static double SegD(double px, double py, double ax, double ay, double bx, double by)
    {
        double ex = bx - ax, ey = by - ay, L2 = ex * ex + ey * ey;
        double t = L2 < 1e-24 ? 0 : Math.Max(0, Math.Min(1, ((px - ax) * ex + (py - ay) * ey) / L2));
        double dx = px - (ax + ex * t), dy = py - (ay + ey * t);
        return Math.Sqrt(dx * dx + dy * dy);
    }

    /// <summary>점에서 선분 위 가장 가까운 점과 그 거리.</summary>
    private static (double X, double Y, double D) OnSeg((double X1, double Y1, double X2, double Y2) s, double x, double y)
    {
        double ex = s.X2 - s.X1, ey = s.Y2 - s.Y1, L2 = ex * ex + ey * ey;
        double t = L2 < 1e-24 ? 0 : Math.Max(0, Math.Min(1, ((x - s.X1) * ex + (y - s.Y1) * ey) / L2));
        double px = s.X1 + ex * t, py = s.Y1 + ey * t;
        return (px, py, Math.Sqrt((px - x) * (px - x) + (py - y) * (py - y)));
    }

    /// <summary>두 선분 사이 거리(서로 지르면 0).</summary>
    private static double SegSegD((double X1, double Y1, double X2, double Y2) s, double ax, double ay, double bx, double by)
    {
        double d1 = (bx - ax) * (s.Y1 - ay) - (by - ay) * (s.X1 - ax), d2 = (bx - ax) * (s.Y2 - ay) - (by - ay) * (s.X2 - ax);
        double d3 = (s.X2 - s.X1) * (ay - s.Y1) - (s.Y2 - s.Y1) * (ax - s.X1), d4 = (s.X2 - s.X1) * (by - s.Y1) - (s.Y2 - s.Y1) * (bx - s.X1);
        if (((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) && ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0))) return 0;
        return Math.Min(Math.Min(SegD(s.X1, s.Y1, ax, ay, bx, by), SegD(s.X2, s.Y2, ax, ay, bx, by)),
                        Math.Min(SegD(ax, ay, s.X1, s.Y1, s.X2, s.Y2), SegD(bx, by, s.X1, s.Y1, s.X2, s.Y2)));
    }

    // ───────────────────────────── 본체 ─────────────────────────────

    /// <summary>★★★ 가상옹벽 삼각형과 원지반 삼각형으로 데이라잇 링을 만든다.</summary>
    /// <param name="poly">옹벽을 세운 폴리곤(세계 좌표, 닫는 점 반복 없어도 됨).</param>
    /// <param name="wallTris">가상옹벽_DH의 <b>보이는</b> 삼각형(경계 적용 뒤).</param>
    /// <param name="groundTris">원지반 삼각형 — 폴리곤 둘레를 덮는 것만 넘겨도 된다.</param>
    /// <param name="dropSegAt">★하네스 전용 — 이 조건에 드는 교선 조각을 <b>일부러 뺀다</b>
    /// (선이 빠졌을 때 경보가 울리는지 재려고). 출하 경로는 null.</param>
    /// <param name="down">★★[v102.0] 성토 — 섬 = 옹벽이 상대면보다 <b>낮은</b> 곳. −z로 뒤집어 같은 셈을 하고 링을 되뒤집는다.</param>
    public static Result Build(IReadOnlyList<Point3> poly, IReadOnlyList<Tri> wallTris,
        IReadOnlyList<Tri> groundTris, string groundName = "원지반",
        Func<double, double, bool>? dropSegAt = null, bool down = false)
    {
        if (!down) return BuildCore(poly, wallTris, groundTris, groundName, dropSegAt, false);
        var r = BuildCore(NegZ(poly, "poly"), NegZ(wallTris, "wall"), NegZ(groundTris, "other"), groundName, dropSegAt, true);
        r.NegateZ();
        r.Trace = AfterFirstLine(r.Trace, DownNote);
        return r;
    }

    /// <param name="down">문구만 고른다(높은/낮은) — 입력은 이미 뒤집혀 왔다.</param>
    static Result BuildCore(IReadOnlyList<Point3> poly, IReadOnlyList<Tri> wallTris,
        IReadOnlyList<Tri> groundTris, string groundName,
        Func<double, double, bool>? dropSegAt, bool down)
    {
        string hi = down ? "낮은" : "높은", lo = down ? "높은" : "낮은";   // 뒤집은 셈의 «높은» = 실제로 낮은
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var R = new Result();
        var tr = new StringBuilder();
        var warns = new List<string>();
        tr.Append($"■ 옹벽 데이라잇 — '가상옹벽_DH' ∩ '{groundName}'\n");
        if (poly == null || poly.Count < 3)
        { R.Summary = "⚠데이라잇 못 만듦 — 폴리곤이 없다"; R.Warn = true; R.Trace = tr + "  " + R.Summary + "\n"; return R; }

        // 지역 좌표 — 세계 좌표(20만대)에서 평면식을 풀면 자릿수가 깎인다
        double ox = poly[0].X, oy = poly[0].Y;
        var gfF = new GeometryFactory();                    // 부동소수(계산용)
        var pm = new PrecisionModel(1.0 / SnapGrid);        // 1mm(잇기용)

        Polygon polyG;
        {
            var cs = new List<Coordinate>();
            foreach (var q in poly)
            {
                var c = new Coordinate(q.X - ox, q.Y - oy);
                if (cs.Count == 0 || cs[^1].Distance(c) > 1e-9) cs.Add(c);
            }
            if (cs[0].Distance(cs[^1]) > 1e-9) cs.Add(cs[0].Copy()); else cs[^1] = cs[0].Copy();
            if (cs.Count < 4)
            { R.Summary = "⚠데이라잇 못 만듦 — 폴리곤 점이 모자란다"; R.Warn = true; R.Trace = tr + "  " + R.Summary + "\n"; return R; }
            polyG = gfF.CreatePolygon(cs.ToArray());
            if (!polyG.IsValid)
            { R.Summary = "⚠데이라잇 못 만듦 — 폴리곤이 제 몸을 지른다"; R.Warn = true; R.Trace = tr + "  " + R.Summary + "\n"; return R; }
        }
        var polyEnv = polyG.EnvelopeInternal;

        // ① 삼각형
        var W = new TriSet(wallTris ?? Array.Empty<Tri>(), ox, oy);
        var G = new TriSet(groundTris ?? Array.Empty<Tri>(), ox, oy);
        R.WallTris = W.N; R.GroundTris = G.N; R.DegenWall = W.Degenerate; R.DegenGround = G.Degenerate;
        var hull = HullEdgesLocal(G, polyEnv);
        R.HullEdges = hull.Count;
        tr.Append($"  ① 삼각형 — 옹벽 {W.N}개"
            + (W.Degenerate > 0 ? $"(평면 넓이 0이라 뺀 것 {W.Degenerate} @{W.DegenAt})" : "")
            + $" · {groundName} {G.N}개" + (G.Degenerate > 0 ? $"(뺀 것 {G.Degenerate} @{G.DegenAt})" : "")
            + $" · 폴리곤 둘레의 측량 경계 변 {hull.Count}개\n");
        if (W.N == 0 || G.N == 0)
        {
            R.Summary = W.N == 0 ? "⚠데이라잇 못 만듦 — 옹벽 삼각형이 0개" : $"⚠데이라잇 못 만듦 — 폴리곤 자리에 {groundName} 삼각형이 0개(범위 밖)";
            R.Warn = true; R.Trace = tr + "  " + R.Summary + "\n"; return R;
        }

        // ② 교선 조각 — 부동소수로 구한다(1mm로 겹침을 구하면 5mm 띠의 교점이 m 단위로 밀린다 — 검토 높음 1)
        //   ★[0918 v100] 순수 옹벽 띠(KeepBand)와 <b>같은 코드</b>를 쓰게 떼어 냈다 — 결과는 종전과 같다(하네스 S138).
        var zs = ZeroLines(W, G, gfF, ox, oy, dropSegAt);
        var segs = zs.Segs;
        var zeroLines = zs.Lines;
        string failAt = zs.FailAt;
        R.Pairs = zs.Pairs; R.ZSkip = zs.ZSkip; R.OverlayFail = zs.OverlayFail; R.Segments = zs.Segments;
        R.Clustered = zeroLines.Count;
        tr.Append($"  ② 교선 조각 — 삼각형 쌍 {R.Pairs}(높이가 안 겹쳐 건너뜀 {R.ZSkip})"
            + $" · 겹침 계산 실패 {R.OverlayFail}{(R.OverlayFail > 0 ? " @" + failAt : "")}"
            + $" · 선분 {R.Segments} → 1mm 군집 뒤 {R.Clustered}"
            + (dropSegAt != null ? " · (하네스: 일부러 뺀 조각 있음)" : "") + "\n");
        if (R.OverlayFail > 0) warns.Add($"겹침 계산 실패 {R.OverlayFail}쌍(그 자리 교선이 빠질 수 있다 @{failAt})");
        if (zeroLines.Count == 0)
        {
            R.Summary = $"⚠데이라잇 못 만듦 — 옹벽이 '{groundName}'에 <b>한 곳도 안 닿는다</b>(옹벽이 통째로 위이거나 아래)";
            R.Warn = true; R.Trace = tr + "  " + R.Summary + "\n"; return R;
        }

        // ③ 잇기 — 교선 + 폴리곤 테두리 + 측량 경계를 <b>한꺼번에</b> 1mm 스냅라운딩으로 노딩
        Geometry hullClipped = HullClipped(hull, polyG, gfF, warns);
        var all = new List<Geometry>(zeroLines) { polyG.ExteriorRing };
        if (!hullClipped.IsEmpty) all.Add(hullClipped);
        Geometry noded;
        try { noded = UnaryUnionNG.Union((Geometry)gfF.CreateGeometryCollection(all.ToArray()), pm); }
        catch (Exception ex)
        {
            R.Summary = $"⚠데이라잇 못 만듦 — 선 잇기(스냅라운딩)가 터졌다 {ex.GetType().Name}";
            R.Warn = true; R.Trace = tr + "  " + R.Summary + "\n"; return R;
        }
        var pz = new Polygonizer();
        pz.Add(noded);
        var faces = new List<Polygon>();
        foreach (var g in pz.GetPolygons()) if (g is Polygon f && !f.IsEmpty && f.Area > 0) faces.Add(f);
        var dangles = pz.GetDangles(); var cuts = pz.GetCutEdges(); var invalid = pz.GetInvalidRingLines();
        R.Dangles = dangles.Count; R.CutEdges = cuts.Count; R.InvalidRings = invalid.Count;
        string At(IEnumerable<Geometry> gs)
        {
            var sb = new StringBuilder(); int n = 0;
            foreach (var g in gs)
            {
                if (n++ >= 4) { sb.Append(" …"); break; }
                var c = g.Coordinate; if (c != null) sb.Append($" ({c.X + ox:F2},{c.Y + oy:F2})");
            }
            return sb.ToString();
        }
        tr.Append($"  ③ 잇기(1mm 스냅라운딩) — 조각 {faces.Count}개 · 끊긴 끝 {R.Dangles}"
            + (R.Dangles > 0 ? At(dangles) : "") + $" · 다리 {R.CutEdges}" + (R.CutEdges > 0 ? At(cuts) : "")
            + $" · 무효 고리 {R.InvalidRings}" + (R.InvalidRings > 0 ? At(invalid) : "") + "\n");
        // ★교선은 폴리곤 안에서 <b>끝날 수 없다</b>(연속인 D의 0선은 닫히거나 테두리에 닿는다) —
        //   끊긴 끝이 있으면 계산이 선을 흘린 것이다.
        if (R.CutEdges > 0) warns.Add($"어느 조각도 안 가르는 선 {R.CutEdges}개{At(cuts)}");
        if (R.InvalidRings > 0) warns.Add($"무효 고리 {R.InvalidRings}개{At(invalid)}");

        // ④ 조각마다 부호 — 다수결이 아니다. 섞이면 오류다(검토 높음 2).
        var polyAround = PreparedGeometryFactory.Prepare(polyG.Buffer(SampleClear));
        var islandFaces = new List<Geometry>();
        var mixedAt = new StringBuilder();
        foreach (var f in faces)
        {
            var ip = f.InteriorPoint;
            if (ip == null || !polyAround.Contains(ip)) continue;
            R.Faces++;
            var S = FaceSamples(f, ip, gfF, out bool thin);
            if (thin) R.FacesThin++;   // 2mm도 안 되는 띠 — 안쪽 점 하나로 잰다(약한 판정, 센다)
            int pos = 0, neg = 0, zer = 0, unkW = 0, unkG = 0;
            foreach (var (x, y) in S)
            {
                if (!W.TryZ(x, y, out double zw)) { unkW++; continue; }
                if (!G.TryZ(x, y, out double zg)) { unkG++; continue; }
                double d = zw - zg;
                if (d > ZeroD) pos++; else if (d < -ZeroD) neg++; else zer++;
            }
            if (pos > 0 && neg > 0)
            {
                R.Mixed++; R.MixedArea += f.Area;
                if (mixedAt.Length < 200) mixedAt.Append($" {f.Area:F1}㎡@({ip.X + ox:F2},{ip.Y + oy:F2}) +{pos}/−{neg}");
                if (pos >= neg) { islandFaces.Add(f); R.FacesIsland++; } else R.FacesOut++;
            }
            else if (pos > 0) { islandFaces.Add(f); R.FacesIsland++; }
            else if (neg > 0) R.FacesOut++;
            else if (zer > 0) { R.FacesZero++; R.ZeroArea += f.Area; }
            else
            {
                R.FacesUnknown++; R.UnknownArea += f.Area;
                if (unkG > 0) R.FacesUnknownGround++; else R.FacesUnknownWall++;
            }
        }
        tr.Append($"  ④ 조각 {R.Faces}개 — 옹벽이 {hi} 곳 {R.FacesIsland} · {lo} 곳 {R.FacesOut}"
            + $" · ⚠섞임 {R.Mixed}{(R.Mixed > 0 ? $"({R.MixedArea:F1}㎡:{mixedAt})" : "")}"
            + $" · 일치(옹벽=땅) {R.FacesZero}{(R.FacesZero > 0 ? $"({R.ZeroArea:F1}㎡ — 섬에 안 넣음)" : "")}"
            + $" · 모름 {R.FacesUnknown}{(R.FacesUnknown > 0 ? $"({R.UnknownArea:F1}㎡ — {groundName} 없음 {R.FacesUnknownGround} · 옹벽 면 없음 {R.FacesUnknownWall})" : "")}"
            + $" · 2mm 안 되는 띠 {R.FacesThin}\n");
        if (R.Mixed > 0) warns.Add($"부호가 섞인 조각 {R.Mixed}개({R.MixedArea:F1}㎡) — 그 자리 교선이 빠졌다:{mixedAt}");
        if (R.FacesUnknownGround > 0) warns.Add($"'{groundName}' 면이 없는 조각 {R.FacesUnknownGround}개 — 그 면의 범위 밖");
        if (R.FacesUnknownWall > 0) warns.Add($"옹벽 면을 못 읽은 조각 {R.FacesUnknownWall}개 — 가상옹벽_DH에 구멍이 있다");

        if (islandFaces.Count == 0)
        {
            R.Summary = $"⚠데이라잇 못 만듦 — 옹벽이 '{groundName}'보다 <b>{hi} 곳이 없다</b>";
            R.Warn = true; R.Trace = tr + "  " + R.Summary + "\n"; return R;
        }

        // ⑤ 섬 — 합쳐서 가장 큰 것 하나(JACK)
        Geometry islandU;
        try { islandU = UnaryUnionNG.Union(islandFaces, pm); }
        catch (Exception ex)
        {
            R.Summary = $"⚠데이라잇 못 만듦 — 섬 합치기가 터졌다 {ex.GetType().Name}";
            R.Warn = true; R.Trace = tr + "  " + R.Summary + "\n"; return R;
        }
        var comps = new List<Polygon>();
        for (int k = 0; k < islandU.NumGeometries; k++)
            if (islandU.GetGeometryN(k) is Polygon pg && !pg.IsEmpty) comps.Add(pg);
        comps.Sort((a, b) => b.Area.CompareTo(a.Area));
        R.Islands = comps.Count;
        var chosen = comps[0];
        R.ChosenArea = chosen.Area; R.HolesInChosen = chosen.NumInteriorRings;
        for (int h = 0; h < chosen.NumInteriorRings; h++)
            R.HoleArea += Math.Abs(gfF.CreatePolygon((LinearRing)chosen.GetInteriorRingN(h)).Area);
        var dropAt = new StringBuilder();
        for (int k = 1; k < comps.Count; k++)
        {
            R.DroppedArea += comps[k].Area;
            if (k <= 3) { var c = comps[k].InteriorPoint; dropAt.Append($" {comps[k].Area:F2}㎡@({c.X + ox:F2},{c.Y + oy:F2})"); }
        }
        tr.Append($"  ⑤ 섬 {R.Islands}개 — <b>가장 큰 것 {R.ChosenArea:F1}㎡</b>를 쓴다"
            + (R.Islands > 1 ? $" · 버린 섬 {R.Islands - 1}개(합 {R.DroppedArea:F2}㎡:{dropAt}{(R.Islands > 4 ? " …" : "")})" : "")
            + (R.HolesInChosen > 0 ? $" · 그 안의 구멍 {R.HolesInChosen}개({R.HoleArea:F2}㎡ — 링에 안 넣음)" : "") + "\n");

        // ⑥ 링 — 변마다 출처를 가린다(검토 높음 3: 절벽에서는 |D|로 못 가른다)
        var zeroIdx = new IndexedFacetDistance(gfF.CreateMultiLineString(
            zeroLines.ConvertAll(g => (LineString)g).ToArray()));
        var polyIdx = new IndexedFacetDistance(polyG.ExteriorRing);
        IndexedFacetDistance? hullIdx = hullClipped.IsEmpty ? null : new IndexedFacetDistance(hullClipped);
        var rc = chosen.ExteriorRing.Coordinates;
        var raw = new List<(double X, double Y)>();
        for (int k = 0; k < rc.Length - 1; k++) raw.Add((rc[k].X, rc[k].Y));
        R.RawPts = raw.Count;
        var kind = new int[raw.Count];                  // 0 닿는 선 · 1 테두리 · 2 측량경계 · 3 모름
        var unkAt = new StringBuilder();
        for (int k = 0; k < raw.Count; k++)
        {
            var a = raw[k]; var b = raw[(k + 1) % raw.Count];
            double L = Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));
            var m = gfF.CreatePoint(new Coordinate((a.X + b.X) * 0.5, (a.Y + b.Y) * 0.5));
            if (zeroIdx.Distance(m) <= ProvTol) { kind[k] = 0; R.LenContact += L; }
            else if (polyIdx.Distance(m) <= ProvTol) { kind[k] = 1; R.LenPolyClose += L; }
            else if (hullIdx != null && hullIdx.Distance(m) <= ProvTol) { kind[k] = 2; R.LenHullClose += L; }
            else
            {
                kind[k] = 3; R.LenUnknown += L; R.UnknownProv++;
                if (unkAt.Length < 120) unkAt.Append($" {L:F2}m@({m.X + ox:F2},{m.Y + oy:F2})");
            }
        }
        if (R.UnknownProv > 0) warns.Add($"어디서 왔는지 모를 변 {R.UnknownProv}개({R.LenUnknown:F2}m):{unkAt}");

        // ⑦ 높이 — 원지반에서. 테두리·측량경계 구간은 원지반 삼각형 변과 만나는 점마다 넣는다(검토 중간 7)
        double GZ(double x, double y)
        {
            if (G.TryZNear(x, y, SampleClear, out double z)) return z;
            R.GroundZMiss++;
            if (W.TryZNear(x, y, SampleClear, out z)) return z;
            return double.NaN;
        }
        var pts = new List<Point3>();
        for (int k = 0; k < raw.Count; k++)
        {
            var a = raw[k]; var b = raw[(k + 1) % raw.Count];
            pts.Add(new Point3(a.X, a.Y, GZ(a.X, a.Y)));
            if (kind[k] == 1 || kind[k] == 2)
                foreach (var (x, y) in GroundEdgeCrossings(G, a.X, a.Y, b.X, b.Y))
                { pts.Add(new Point3(x, y, GZ(x, y))); R.Inserted++; }
        }
        // 못 읽은 높이는 이웃에서 — 빈 채로 두면 0m로 떨어져 수직 가시가 선다
        for (int k = 0; k < pts.Count; k++)
            if (double.IsNaN(pts[k].Z))
            {
                double zn = double.NaN;
                for (int s = 1; s < pts.Count && double.IsNaN(zn); s++)
                {
                    if (!double.IsNaN(pts[(k + s) % pts.Count].Z)) zn = pts[(k + s) % pts.Count].Z;
                    else if (!double.IsNaN(pts[(k - s + pts.Count) % pts.Count].Z)) zn = pts[(k - s + pts.Count) % pts.Count].Z;
                }
                pts[k] = new Point3(pts[k].X, pts[k].Y, double.IsNaN(zn) ? 0 : zn);
                R.ZFromNeighbor++;
            }
        if (R.GroundZMiss > 0)
            warns.Add($"{groundName} 높이를 못 읽은 점 {R.GroundZMiss}개 — 옹벽 높이로 대신"
                    + (R.ZFromNeighbor > 0 ? $"(그것도 못 읽어 <b>이웃 점 높이</b>를 쓴 점 {R.ZFromNeighbor}개)" : ""));

        // ⑧ 정리 — 겹친 점(1mm) · 3D로 1cm 안 일직선 점만
        var dedup = Dedup(pts, out int nd);
        R.Dups = nd;
        var simp = Simplify3D(dedup, LineTol, out double maxDev);
        R.MaxDev = maxDev; R.Collinear = dedup.Count - simp.Count;
        var final = simp;
        if (!RingOk(gfF, simp))
        {
            // 펴다가 제 몸을 지르게 됐으면 펴지 않은 것을 쓴다 — 모양이 우선이다
            R.SimplifyFallback = true; R.Collinear = 0; R.MaxDev = 0; final = dedup;
            warns.Add("일직선 점을 빼니 링이 제 몸을 질러 <b>빼지 않은 것</b>을 썼다");
        }
        R.Simple = RingOk(gfF, final);
        if (!R.Simple) warns.Add("<b>링이 제 몸을 지른다</b>");
        R.FinalPts = final.Count;
        var outer = PreparedGeometryFactory.Prepare(polyG.Buffer(SampleClear));
        foreach (var q in final) if (!outer.Contains(gfF.CreatePoint(new Coordinate(q.X, q.Y)))) R.OutsidePoly++;
        if (R.OutsidePoly > 0) warns.Add($"폴리곤 밖 점 {R.OutsidePoly}개");
        double perim = 0;
        for (int k = 0; k < final.Count; k++)
        {
            var a = final[k]; var b = final[(k + 1) % final.Count];
            perim += Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));
        }
        tr.Append($"  ⑥ 선 — 점 {R.RawPts}(+테두리 구간에 넣은 점 {R.Inserted}) → 겹침 {R.Dups} · 일직선 {R.Collinear} 뺌"
            + $" → <b>{R.FinalPts}점</b> · 최대 이탈 {R.MaxDev * 100:F2}cm · 둘레 {perim:F1}m\n");
        tr.Append($"    닿는 선 {R.LenContact:F2}m · <b>폴리곤 테두리로 닫음 {R.LenPolyClose:F2}m</b>"
            + $" · 측량 경계로 닫음 {R.LenHullClose:F2}m" + (R.UnknownProv > 0 ? $" · ⚠출처 모름 {R.LenUnknown:F2}m" : "")
            + $" · 높이는 {groundName}(못 읽은 점 {R.GroundZMiss})\n");

        // ⑨ 독립 검사 — 0.5m 격자에서 「옹벽이 높은가」와 「섬 안인가」를 대조
        {
            var islPrep = PreparedGeometryFactory.Prepare(islandU);
            var islB = new IndexedFacetDistance(islandU.Boundary);
            var inner = PreparedGeometryFactory.Prepare(polyG.Buffer(-SampleClear));
            double worst = -1; string worstAt = "";
            for (double x = polyEnv.MinX + GridStep * 0.5; x < polyEnv.MaxX; x += GridStep)
                for (double y = polyEnv.MinY + GridStep * 0.5; y < polyEnv.MaxY; y += GridStep)
                {
                    var pt = gfF.CreatePoint(new Coordinate(x, y));
                    if (!inner.Contains(pt)) continue;
                    if (!W.TryZ(x, y, out double zw) || !G.TryZ(x, y, out double zg)) continue;
                    double d = zw - zg;
                    if (Math.Abs(d) <= ZeroD) continue;
                    double db = islB.Distance(pt);
                    if (db < SampleClear) continue;
                    R.GridChecked++;
                    if ((d > 0) != islPrep.Contains(pt))
                    {
                        R.GridMismatch++;
                        if (db > worst) { worst = db; worstAt = $"({x + ox:F2},{y + oy:F2}) D={(down ? -d : d):+0.000;-0.000}m · 섬 경계에서 {db:F3}m"; }
                    }
                }
            tr.Append($"  ⑦ 독립 검사(0.5m 격자) — {R.GridChecked}점 중 부호와 섬이 어긋난 점 <b>{R.GridMismatch}</b>"
                + (R.GridMismatch > 0 ? $" · 최악 {worstAt}" : "") + "\n");
            if (R.GridMismatch > 0) warns.Add($"격자 검사에서 어긋난 점 {R.GridMismatch}개(최악 {worstAt})");
        }

        // ⑦-b ★[JACK 0918 현장 · 끊긴 끝 1곳] <b>끊긴 끝을 하나씩 따라간다</b>(계측 — 판정문 없이 숫자만).
        //   <para>끝 주변 반경 1cm에서 D의 부호가 <b>바뀌면</b> 교선이 거기서 이어져야 하는데 <b>빠진 것</b>이고,
        //   <b>안 바뀌면</b> 교선이 없는 자리에 선분이 <b>잘못 생긴 것</b>이다. 링까지 거리로 곁가지인지도 본다.</para>
        // ★★[JACK 0918 인터뷰 «정식 레이어 + 로그에 적기»] <b>곁가지와 진짜 끊김을 가른다.</b>
        //   <para>현장(v99.8): 끊긴 끝 1곳이 <b>길이 2.0mm</b> · 한 끝이 링 위(0.0mm) · 다른 끝이 링에서 2.0mm —
        //   폐합면(1:0.001) 위라 1mm 격자로 이으면 그만한 부스러기가 남는다. 조각 2 · 격자 어긋남 0이라 링은 온전했다.
        //   일부러 교선을 뺀 하네스 ⑤는 끊긴 끝이 수백 mm · 링에서 26mm였다 — <b>둘이 뚜렷이 갈린다.</b></para>
        //   <para>잣대는 새로 만든 자가 아니라 <b>선 정확도</b>(JACK이 정한 1cm 정리)다 — 그 안의 부스러기는 링을 못 바꾼다.
        //   섬 경계 전부(버린 섬·구멍 포함)에서 재므로, 버린 작은 섬 근처 부스러기도 곁가지로 센다.</para>
        var realDangles = new List<LineString>();
        if (R.Dangles > 0)
        {
            var islBd = new IndexedFacetDistance(islandU.Boundary);
            foreach (var dg in dangles)
            {
                // ★★[검토 0918 v99.9 · 중간 1] <b>길이도 1cm 이하</b>여야 곁가지다 — 길이 제한이 없으면
                //   좁은 띠(폭 1cm 미만)를 가르던 교선이 통째로 빠져도(재현: 364mm) «1cm 안»이라 곁가지로 통과했다.
                //   그리고 거리를 <b>꼭짓점에서만</b> 재면 곧고 긴 조각은 양 끝만 본다 — 1mm 간격으로 촘촘히 잰다.
                double far = 0;
                var dcs = dg.Coordinates;
                for (int q = 0; q + 1 < dcs.Length; q++)
                {
                    double L = dcs[q].Distance(dcs[q + 1]);
                    int ns = Math.Max(1, (int)Math.Ceiling(L / 0.001));
                    for (int t = 0; t <= ns; t++)
                    {
                        double x = dcs[q].X + (dcs[q + 1].X - dcs[q].X) * t / ns, y = dcs[q].Y + (dcs[q + 1].Y - dcs[q].Y) * t / ns;
                        far = Math.Max(far, islBd.Distance(gfF.CreatePoint(new Coordinate(x, y))));
                    }
                }
                if (far <= LineTol && dg.Length <= LineTol) { R.Spurs++; R.SpurMaxLen = Math.Max(R.SpurMaxLen, dg.Length); }
                else realDangles.Add(dg);
            }
            R.Dangles = realDangles.Count;
            if (R.Spurs > 0)
                tr.Append($"  ⑦-a 곁가지 {R.Spurs}개 — 길이 1cm 이하 · 섬 경계에서 1cm 안(최장 {R.SpurMaxLen * 1000:F1}mm) · 선 정확도(1cm) 안이라 <b>정식으로 그린다</b>\n");
            if (R.Dangles > 0) warns.Add($"교선이 끊긴 끝 {R.Dangles}곳(1cm보다 길거나 섬 경계에서 1cm 넘게 벗어남){At(realDangles)}");
        }
        if (R.Dangles + R.Spurs > 0)
        {
            var ringIdx = new IndexedFacetDistance(chosen.ExteriorRing);
            string Around(double x, double y)
            {
                double mn = double.MaxValue, mx = double.MinValue; int n = 0;
                for (int k = -1; k < 8; k++)
                {
                    double px = x + (k < 0 ? 0 : 0.01 * Math.Cos(k * Math.PI / 4));
                    double py = y + (k < 0 ? 0 : 0.01 * Math.Sin(k * Math.PI / 4));
                    if (!W.TryZ(px, py, out double zw) || !G.TryZ(px, py, out double zg)) continue;
                    double d = zw - zg; n++;
                    mn = Math.Min(mn, d); mx = Math.Max(mx, d);
                }
                if (n == 0) return "D 못 읽음";
                bool flip = mn < -ZeroD && mx > ZeroD;
                return $"반경1cm D {(down ? -mx : mn):+0.0000;-0.0000}~{(down ? -mn : mx):+0.0000;-0.0000}m(부호 바뀜 {(flip ? "<b>예</b>" : "아니오")})";
            }
            int shown = 0;
            foreach (var dg in (realDangles.Count > 0 ? (IEnumerable<LineString>)realDangles : dangles))   // 진짜 끊김을 먼저 보인다
            {
                if (shown++ >= 4) break;
                var cs = dg.Coordinates;
                if (cs.Length < 2) continue;
                var a = cs[0]; var b = cs[^1];
                double da = ringIdx.Distance(gfF.CreatePoint(a)), db2 = ringIdx.Distance(gfF.CreatePoint(b));
                int nearA = 0, nearB = 0;
                foreach (var s in segs)
                {
                    if (Math.Min(Math.Sqrt((s.X1 - a.X) * (s.X1 - a.X) + (s.Y1 - a.Y) * (s.Y1 - a.Y)),
                                 Math.Sqrt((s.X2 - a.X) * (s.X2 - a.X) + (s.Y2 - a.Y) * (s.Y2 - a.Y))) < 0.005) nearA++;
                    if (Math.Min(Math.Sqrt((s.X1 - b.X) * (s.X1 - b.X) + (s.Y1 - b.Y) * (s.Y1 - b.Y)),
                                 Math.Sqrt((s.X2 - b.X) * (s.X2 - b.X) + (s.Y2 - b.Y) * (s.Y2 - b.Y))) < 0.005) nearB++;
                }
                var m = dg.InteriorPoint;
                tr.Append($"  ⑦-b 끊긴 끝 #{shown} — 길이 {dg.Length * 1000:F1}mm · 점 {cs.Length}개"
                    + $" · 가운데 {Around(m.X, m.Y)}\n"
                    + $"      끝A ({a.X + ox:F3},{a.Y + oy:F3}) 링까지 {da * 1000:F1}mm · 5mm 안 원선분 끝 {nearA}개 · {Around(a.X, a.Y)}\n"
                    + $"      끝B ({b.X + ox:F3},{b.Y + oy:F3}) 링까지 {db2 * 1000:F1}mm · 5mm 안 원선분 끝 {nearB}개 · {Around(b.X, b.Y)}\n");
            }
        }

        // 세계 좌표로
        var ring = new List<Point3>(final.Count);
        foreach (var q in final) ring.Add(new Point3(q.X + ox, q.Y + oy, q.Z));
        R.Ring = ring;
        R.Warn = warns.Count > 0;
        R.Ms = sw.ElapsedMilliseconds;
        string closeNote = R.LenPolyClose > 0.005 || R.LenHullClose > 0.005
            ? $" · 진짜 닿는 선이 아닌 구간: 폴리곤 테두리 {R.LenPolyClose:F2}m · 측량 경계 {R.LenHullClose:F2}m"
            : " · 전부 진짜 닿는 선";
        R.Summary = $"데이라잇 {(R.Warn ? "⚠" : "✔")} 닫힌 3D폴리선 1개 · {R.FinalPts}점 · 둘레 {perim:F1}m{closeNote}"
                  + (R.Islands > 1 ? $" · 버린 고리 {R.Islands - 1}개({R.DroppedArea:F1}㎡)" : "")
                  + (R.HolesInChosen > 0 ? $" · 안쪽 구멍 {R.HolesInChosen}개(안 그림)" : "");
        tr.Append($"  ⑧ 자가검증 — 닫힘 예 · 단순 {(R.Simple ? "예" : "<b>아니오</b>")} · 폴리곤 밖 점 {R.OutsidePoly}"
            + $" · 끊긴 끝 {R.Dangles}{(R.Spurs > 0 ? $"(+곁가지 {R.Spurs})" : "")} · 섞인 조각 {R.Mixed} · 격자 어긋남 {R.GridMismatch} · {R.Ms}ms"
            + (warns.Count == 0 ? " → ✔\n" : $" → <b>⚠{warns.Count}건</b>\n"));
        foreach (var w in warns) tr.Append($"    ⚠{w}\n");
        R.Trace = tr.ToString();
        return R;
    }

    // ───────────────────────────── 남길 옹벽 띠(순수 옹벽) ─────────────────────────────

    /// <summary>★★★[JACK 0918] <b>남길 옹벽 띠</b> — 정지면보다 높고 원지반보다 높지는 않은 곳.</summary>
    public sealed class BandResult
    {
        /// <summary>띠의 바깥 링(세계 좌표, 첫점 반복 없음). <b>1cm 정리를 안 한다</b> — Civil 경계로 그대로 넘긴다.</summary>
        public List<Point3>? Ring;
        /// <summary>띠 안의 구멍(옹벽이 땅 위로 솟은 작은 섬) — Hide 경계로 뚫는다(JACK 0918).</summary>
        public List<List<Point3>> Holes = new();
        public bool Warn;
        public string Summary = "";
        public string Trace = "";
        public int Faces, FacesKeep, FacesA, FacesOutB, Mixed, FacesUnknown, FacesThin, FacesPlanAboveGround;
        public double AreaPlanAboveGround, MixedArea, UnknownArea;
        public int Pieces, NearPieces;
        /// <summary>떨어진 조각 중 <b>모든 점이 큰 조각에서 1cm 안</b>인 부스러기(띠 끝 바늘에서 스냅이 떼어 낸 것) —
        /// 곁가지와 같은 잣대(선 정확도 1cm)라 틀린 곳으로 안 센다.</summary>
        public int Fragments;
        /// <summary>★[JACK 0918 «자글자글»] 포개진 자리(옹벽 = 원지반 또는 옹벽 = 정지면) 조각 — 맞닿은 길이가 긴 이웃을 따라갔다.</summary>
        public int CoplanarFaces, CoplanarToKeep, CoplanarToDrop, CoplanarIsolated;
        public double CoplanarArea;
        /// <summary>★[검토 v100.1 · 중간] 버린 포개진 조각이 남김 덩이 둘 이상을 잇는 다리라 <b>남김으로 뒤집은</b> 수(띠가 갈라지지 않게).</summary>
        public int CoplanarBridge;
        /// <summary>판정 중 삼킨 예외 수 · 한 조각에 «같음»과 «위/아래»가 함께 나온 조각 수(선이 빠졌을 수 있다 — 정보).</summary>
        public int CoplanarErrors, PartialZero;
        /// <summary>★★[검토 v100.1 · 높음] <b>옹벽 = 원지반</b>이라 버린 자리의 넓이 — 빨강 섬 안에도 띠 안에도 없다.
        /// 합성 때 원지반 자리는 반드시 <b>«파랑 안 − 띠»</b>로 잡을 것(빨강 섬으로 잡으면 이 자리가 정지면으로 채워져 구덩이가 난다).</summary>
        public double GroundEqualDroppedArea;
        /// <summary>★[검토 v100.0 · 중간 1] 1cm 안에 붙었지만 <b>격자보다 얇은</b>(평균 폭 ≤ 2mm) 조각 — 조용히 버리지 않고 길이를 적어 ⚠.</summary>
        public int ThinDropped;
        public double ThinDroppedLen;
        /// <summary>가장 큰 조각의 평균 폭(2×넓이÷둘레). <see cref="TooThinWidth"/> 이하면 1mm 격자로 띠를 표현할 수 없다.</summary>
        public double ChosenMeanWidth;
        public bool TooThin;
        public double Area, DroppedArea, HoleArea;
        public int Dangles, Spurs, CutEdges, InvalidRings, OverlayFail;
        public double SpurMaxLen;
        public double LenZG, LenZP, LenPoly, LenHull, LenUnknown;
        public int UnknownProv;
        public int RawPts, FinalPts, OutsidePoly, GridChecked, GridMismatch;
        public bool Simple;
        public long Ms;
        /// <summary>★★[JACK 0918 «수직방향이 톱니처럼»] <b>1mm 스냅 링</b>(v100.1까지의 링) — Civil이 정확한 링(<see cref="Ring"/>)을
        /// 못 받으면 순수옹벽_DH를 이것으로 한 번 더 짓는다(검토 v100.2 · 중간 5 — 지금보다 나빠지지 않게).</summary>
        public List<Point3>? RingSnap;
        public List<List<Point3>> HolesSnap = new();
        /// <summary>⑦-b 링을 1mm 격자에서 정확한 교선 위로 되돌린 셈 — 옮긴 꼭짓점 · 두 선의 교점 모서리 · 교점이 없어 가까운 선으로 ·
        /// <b>스냅 그대로</b> · 길 없음 · 되살린 꺾임점 · 뺀 가시 · 제 몸 지름 고친 꼭짓점 · 출처를 이웃 조각으로 못 정해 거리로 짐작한 변 ·
        /// 옹벽 높이 변에 넣은 삼각형 변 교점.</summary>
        public int UnsMoved, UnsCorner, UnsCornerNear, UnsKept, UnsNoPath, UnsBends, UnsSpikes, UnsRepaired, UnsFamGuess, UnsWallCross;
        /// <summary>선을 못 찾아 곧게 둔 변 · 못 되돌려 스냅 링으로 둔 링(바깥 또는 구멍) 수.</summary>
        public int UnsStraight, UnsRingsSnapped;
        /// <summary>양옆 높이 판정이 조각 판정과 다른 선을 고른 변 · 어느 선도 양옆이 맞지 않은 변(가려지긴 하는데 다 틀림).</summary>
        public int UnsSideSwitch, UnsSideNone;
        /// <summary>이음선 바깥도 남길 곳인 자리 — 띠가 <b>격자보다 얇아져 닫은</b> 곳(띠 끝 쐐기 · 떼어 낸 얇은 조각)과 그 세계 좌표.</summary>
        public int UnsCuts;
        public List<(double X, double Y)> UnsCutAt = new();
        /// <summary>되돌리기를 포기하고 스냅 링을 그대로 쓴다(제 몸을 지름 · 구멍과 겹침).</summary>
        public bool UnsFallback;
        public double UnsMaxMove, UnsToSnapMax;
        /// <summary>⑦-c 자가검증 — Civil이 테두리에 줄 높이(옹벽 TIN) − 링 높이. 링 꼭짓점 + 링 변 × 옹벽 삼각형 변 교점에서 잰다
        /// (Civil 비파괴 경계가 넣는 점이 바로 그 교점이다 — 현장 스샷 0918 15:53).</summary>
        public double EdgeGapMax, EdgeGapMed, EdgeGap99;
        public int EdgeGapN, EdgeGapOver1cm, EdgeGapNoWall;
        public string EdgeGapAt = "";

        /// <summary>틀린 줄 아는가 — 참이면 순수옹벽_DH를 짓지 않는다. 데이라잇의 <see cref="Result.Broken"/>과 같은 잣대 +
        /// <b>1cm 안에서 갈라진 조각</b>(얕은 절토에서 스냅이 띠를 끊은 것 — 진짜 벽이 버려진다).</summary>
        public bool Broken => Ring == null || !Simple || Dangles > 0 || Mixed > 0 || GridMismatch > 0 || UnknownProv > 0
                           || InvalidRings > 0 || CutEdges > 0 || OutsidePoly > 0 || OverlayFail > 0 || NearPieces > 0 || TooThin;

        /// <summary>★[v102.0 · 성토] 뒤집은 셈의 결과를 되뒤집는다 — <b>z를 가진 필드는 여기 전부</b>(Ring · Holes · RingSnap · HolesSnap —
        /// 계획 검토 v102 · M1: 스냅 링은 Civil이 되돌리기 실패·넷째 시도에서 쓴다). UnsCutAt은 XY · EdgeGap*는 |차|.</summary>
        internal void NegateZ()
        {
            if (Ring != null) Ring = NegZ(Ring, "ring");
            Holes = NegZ(Holes, "holes");
            if (RingSnap != null) RingSnap = NegZ(RingSnap, "ringsnap");
            HolesSnap = NegZ(HolesSnap, "holessnap");
        }
    }

    /// <summary>★[S139 ⑤·⑥] 1mm 격자로 표현 못 하는 폭 — 평균 폭(2×넓이÷둘레)이 이 이하인 조각은 <b>격자보다 얇다</b>.
    /// <para>띠 끝 바늘이 가늘어지다 떨어진 꼬리(⑥)는 이만큼 얇아 버려도 선 정확도 안이다.
    /// 반대로 <b>가장 큰 조각</b>이 이만큼 얇으면(얕은 앞면 ⑤: 폭 0.5mm) 순수 옹벽을 만들 수 없다 → 틀린 곳.</para></summary>
    public const double TooThinWidth = 2 * SnapGrid;

    /// <summary>하네스·계측 전용 — 포개진 조각의 이웃을 로그에 낱낱이 적는다.</summary>
    public static bool DebugCoplanar = false;
    /// <summary>하네스·계측 전용 — 이 자리(세계 좌표) 5mm 안에서 ⑦-b가 한 일(변마다 고른 선 · 꼭짓점 · 판마다 링 점)을 로그에 적는다.</summary>
    public static (double X, double Y)? DebugUnsnapAt = null;
    /// <summary>하네스 전용 — ⑦-b가 링마다 실패한 것처럼 군다(스냅 링으로 돌아가는 길을 잰다). 출하 경로는 거짓.</summary>
    public static bool DebugForceUnsnapFail = false;

    /// <summary>★★★[JACK 0918 <i>"원지반과의 데이라잇과, 정지면과의 데이라잇을 활용해서 순수하게 잘린걸 뽑아내야 하는데
    /// … 경계가 두개가 안 들어갈꺼야"</i>] <b>남길 옹벽 띠 하나</b>를 만든다(절토).
    ///
    /// <para><b>무엇을 남기나.</b> 최종 면은 median(옹벽, 정지면, 원지반)이다 — 옹벽이 원지반보다 높으면 원지반,
    /// 정지면보다 낮으면 정지면, <b>그 사이면 옹벽</b>. 그래서 남길 곳 K = B − A =
    /// {옹벽 &gt; 정지면} − {옹벽 &gt; 원지반}. ★<b>옹벽 = 원지반</b>인 자리(현장: 원지반 120.0 평평 삼각형이 3단 소단과 34.4㎡ 포갬)는
    /// A 밖이라 <b>남긴다</b> — 거기는 옹벽이 곧 땅이다(계획 검토 0918 · 높음 1: 「원지반보다 낮은」으로 짜면 현장에서 면이 안 지어졌다).</para>
    ///
    /// <para><b>어떻게.</b> 그려진 빨강·파랑을 빼지 않는다 — 1cm로 다듬은 두 선을 빼면 폐합면 쪽에서 넓이 0 부스러기가 난다
    /// (현장 실측: 둘레 3.7m·9.2m 두 개). 두 교선을 <b>한 번에</b> 칼금으로 넣어(같은 1mm 스냅라운딩) 조각마다 두 부호를 잰다.</para>
    ///
    /// <para><b>Civil에 넘기는 링은 1cm로 다듬지 않는다</b>(0.1mm만) — 옹벽 면이 1:0.01이라 평면 1cm가 높이 1m다(검토 중간 3).</para></summary>
    /// <param name="dropGroundSegAt">★하네스 전용 — 옹벽∩원지반 교선 조각을 일부러 뺀다. 출하 경로는 null.</param>
    /// <param name="down">★★[v102.0] 성토 — 남길 곳 = 원지반 ≤ 옹벽 &lt; 정지면. 폴리곤 z(옹벽 밑선 = 머리 높이)까지 −z로 뒤집어 같은 셈을 하고
    /// 링·구멍·스냅 링을 되뒤집는다(계획 검토 v102 · H1·M1).</param>
    public static BandResult KeepBand(IReadOnlyList<Point3> poly, IReadOnlyList<Tri> wallTris,
        IReadOnlyList<Tri> groundTris, IReadOnlyList<Tri> planTris,
        string groundName = "원지반", string planName = "정지면_DH",
        Func<double, double, bool>? dropGroundSegAt = null, Func<double, double, bool>? dropPlanSegAt = null,
        bool down = false)
    {
        if (!down) return KeepBandCore(poly, wallTris, groundTris, planTris, groundName, planName, dropGroundSegAt, dropPlanSegAt, false);
        var r = KeepBandCore(NegZ(poly, "poly"), NegZ(wallTris, "wall"), NegZ(groundTris, "ground"), NegZ(planTris, "plan"),
                             groundName, planName, dropGroundSegAt, dropPlanSegAt, true);
        r.NegateZ();
        r.Trace = AfterFirstLine(r.Trace, DownNote);
        return r;
    }

    /// <param name="down">문구만 고른다 — 입력은 이미 뒤집혀 왔다(뒤집은 셈의 «높은» = 실제로 낮은 · «밑선» = 옹벽 머리선).</param>
    static BandResult KeepBandCore(IReadOnlyList<Point3> poly, IReadOnlyList<Tri> wallTris,
        IReadOnlyList<Tri> groundTris, IReadOnlyList<Tri> planTris,
        string groundName, string planName,
        Func<double, double, bool>? dropGroundSegAt, Func<double, double, bool>? dropPlanSegAt, bool down)
    {
        double Hz(double z) => down ? -z : z;                     // 사람에게 보이는 높이는 되뒤집어 적는다
        string side = down ? "성토" : "절토", other = down ? "절토" : "성토", baseLine = down ? "옹벽 머리선" : "옹벽 밑선";
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var R = new BandResult();
        var tr = new StringBuilder();
        var warns = new List<string>();
        tr.Append(down ? $"■ 남길 옹벽 띠(성토) — '{groundName}' ≤ 옹벽 < '{planName}'(파랑 안 − 빨강 안)\n"
                       : $"■ 남길 옹벽 띠 — '{planName}' < 옹벽 ≤ '{groundName}'(파랑 안 − 빨강 안)\n");
        BandResult Fail(string why) { R.Summary = why; R.Warn = true; R.Trace = tr + "  " + why + "\n"; R.Ms = sw.ElapsedMilliseconds; return R; }
        if (poly == null || poly.Count < 3) return Fail("⚠남길 띠 못 만듦 — 폴리곤이 없다");

        double ox = poly[0].X, oy = poly[0].Y, zBase = poly[0].Z;
        var gfF = new GeometryFactory();
        var pm = new PrecisionModel(1.0 / SnapGrid);
        Polygon polyG;
        {
            var cs = new List<Coordinate>();
            foreach (var q in poly)
            {
                var c = new Coordinate(q.X - ox, q.Y - oy);
                if (cs.Count == 0 || cs[^1].Distance(c) > 1e-9) cs.Add(c);
            }
            if (cs[0].Distance(cs[^1]) > 1e-9) cs.Add(cs[0].Copy()); else cs[^1] = cs[0].Copy();
            if (cs.Count < 4) return Fail("⚠남길 띠 못 만듦 — 폴리곤 점이 모자란다");
            polyG = gfF.CreatePolygon(cs.ToArray());
            if (!polyG.IsValid) return Fail("⚠남길 띠 못 만듦 — 폴리곤이 제 몸을 지른다");
        }
        var polyEnv = polyG.EnvelopeInternal;

        // ① 삼각형 세 벌
        var W = new TriSet(wallTris ?? Array.Empty<Tri>(), ox, oy);
        var G = new TriSet(groundTris ?? Array.Empty<Tri>(), ox, oy);
        var P = new TriSet(planTris ?? Array.Empty<Tri>(), ox, oy);
        tr.Append($"  ① 삼각형 — 옹벽 {W.N} · {groundName} {G.N} · {planName} {P.N}"
            + (W.Degenerate + G.Degenerate + P.Degenerate > 0 ? $" · 평면 넓이 0이라 뺀 것 {W.Degenerate}/{G.Degenerate}/{P.Degenerate}" : "") + "\n");
        if (W.N == 0 || G.N == 0 || P.N == 0) return Fail("⚠남길 띠 못 만듦 — 삼각형이 0개인 면이 있다");

        // ② 교선 — 데이라잇과 같은 코드로, 면마다 <b>두 높이</b>(D = +ZeroD · −ZeroD)에서 긋는다.
        //   ★★[JACK 0918 «데이라잇에 맞춰서 깔끔하게 잘리진 못해?»] 옹벽 소단(105+5m 간격)이 원지반 등고선 평삼각형·
        //   정지면 소단(같은 5m 간격)과 <b>정확히 포갠다</b>(현장 110·115·120m). 거기서 «닿는 선»은 선이 아니라 <b>면</b>이고,
        //   한 높이로만 그으면 그 평평한 자리가 옆 조각에 붙어 띠 테두리가 평삼각형의 <b>비스듬한 변</b>을 따라가며
        //   바늘·톱니가 났다(현장 1cm 격자 그림으로 확인). 두 높이로 그으면 보통 자리에선 두 줄이 1mm 격자에서 한 줄로
        //   녹고, <b>포개진 자리만</b> 따로 조각으로 떨어진다 — 그 조각을 아래 ④-b가 정한다.
        var zg = ZeroLines(W, G, gfF, ox, oy, dropGroundSegAt, +ZeroD);
        var zg2 = ZeroLines(W, G, gfF, ox, oy, dropGroundSegAt, -ZeroD);
        var zp = ZeroLines(W, P, gfF, ox, oy, dropPlanSegAt, +ZeroD);
        var zp2 = ZeroLines(W, P, gfF, ox, oy, dropPlanSegAt, -ZeroD);
        zg.Lines.AddRange(zg2.Lines); zg.Segs.AddRange(zg2.Segs); zg.Segments += zg2.Segments; zg.OverlayFail += zg2.OverlayFail; zg.FailAt += zg2.FailAt;
        zp.Lines.AddRange(zp2.Lines); zp.Segs.AddRange(zp2.Segs); zp.Segments += zp2.Segments; zp.OverlayFail += zp2.OverlayFail; zp.FailAt += zp2.FailAt;
        R.OverlayFail = zg.OverlayFail + zp.OverlayFail;
        tr.Append($"  ② 교선(두 높이 ±{ZeroD:0e0}) — 옹벽∩{groundName} 선분 {zg.Segments}→{zg.Lines.Count} · 옹벽∩{planName} 선분 {zp.Segments}→{zp.Lines.Count}"
            + $" · 겹침 계산 실패 {R.OverlayFail}" + (dropGroundSegAt != null || dropPlanSegAt != null ? " · (하네스: 일부러 뺀 조각 있음)" : "") + "\n");
        if (R.OverlayFail > 0) warns.Add($"겹침 계산 실패 {R.OverlayFail}쌍 @{zg.FailAt}{zp.FailAt}");
        if (zg.Lines.Count == 0 && zp.Lines.Count == 0) return Fail("⚠남길 띠 못 만듦 — 옹벽이 두 면 어디에도 안 닿는다");

        // ③ 한 번에 잇기 — 두 교선 + 폴리곤 테두리 + 두 면의 측량 경계
        var hG = HullClipped(HullEdgesLocal(G, polyEnv), polyG, gfF, warns);
        var hP = HullClipped(HullEdgesLocal(P, polyEnv), polyG, gfF, warns);
        var all = new List<Geometry>(zg.Lines);
        all.AddRange(zp.Lines);
        all.Add(polyG.ExteriorRing);
        if (!hG.IsEmpty) all.Add(hG);
        if (!hP.IsEmpty) all.Add(hP);
        Geometry noded;
        try { noded = UnaryUnionNG.Union((Geometry)gfF.CreateGeometryCollection(all.ToArray()), pm); }
        catch (Exception ex) { return Fail($"⚠남길 띠 못 만듦 — 선 잇기가 터졌다 {ex.GetType().Name}"); }
        var pz = new Polygonizer();
        pz.Add(noded);
        var faces = new List<Polygon>();
        foreach (var g in pz.GetPolygons()) if (g is Polygon f && !f.IsEmpty && f.Area > 0) faces.Add(f);
        var dangles = pz.GetDangles(); var cuts = pz.GetCutEdges(); var invalid = pz.GetInvalidRingLines();
        R.CutEdges = cuts.Count; R.InvalidRings = invalid.Count;
        R.Dangles = dangles.Count;   // 아래 ⑥에서 곁가지를 빼고 다시 센다 — 그 전에 멈추면 이 값이 사유다
        string At(IEnumerable<Geometry> gs)
        {
            var sb = new StringBuilder(); int n = 0;
            foreach (var g in gs)
            {
                if (n++ >= 4) { sb.Append(" …"); break; }
                var c = g.Coordinate; if (c != null) sb.Append($" ({c.X + ox:F2},{c.Y + oy:F2})");
            }
            return sb.ToString();
        }
        tr.Append($"  ③ 잇기(1mm 스냅라운딩) — 조각 {faces.Count}개 · 끊긴 끝 {dangles.Count} · 다리 {R.CutEdges} · 무효 고리 {R.InvalidRings}\n");
        if (R.CutEdges > 0) warns.Add($"어느 조각도 안 가르는 선 {R.CutEdges}개{At(cuts)}");
        if (R.InvalidRings > 0) warns.Add($"무효 고리 {R.InvalidRings}개{At(invalid)}");

        // ④ 조각마다 두 부호 — 섞이면 오류(다수결로 덮지 않는다)
        var polyAround = PreparedGeometryFactory.Prepare(polyG.Buffer(SampleClear));
        var keepFaces = new List<Geometry>(); var aFaces = new List<Geometry>(); var bFaces = new List<Geometry>();
        var mixedAt = new StringBuilder();
        // 0 남김 · 1 버림 · 2 포개진 자리(남기든 버리든 최종 높이가 같다 — 아래 ④-b에서 정한다)
        var cls = new List<(Polygon F, int C, int G, int P)>();
        var copFaces = new List<Geometry>();
        foreach (var f in faces)
        {
            var ip = f.InteriorPoint;
            if (ip == null || !polyAround.Contains(ip)) continue;
            R.Faces++;
            var S = FaceSamples(f, ip, gfF, out bool thin);
            if (thin) R.FacesThin++;
            int gP = 0, gN = 0, gZ = 0, pP = 0, pN = 0, pZ = 0, pag = 0, unk = 0;
            foreach (var (x, y) in S)
            {
                if (!W.TryZ(x, y, out double zw) || !G.TryZ(x, y, out double zgv) || !P.TryZ(x, y, out double zpv)) { unk++; continue; }
                double dg = zw - zgv, dp = zw - zpv;
                if (dg > ZeroD) gP++; else if (dg < -ZeroD) gN++; else gZ++;
                if (dp > ZeroD) pP++; else if (dp < -ZeroD) pN++; else pZ++;
                if (zpv - zgv > ZeroD) pag++;
            }
            if (unk == S.Count) { R.FacesUnknown++; R.UnknownArea += f.Area; continue; }
            bool mixG = gP > 0 && gN > 0, mixP = pP > 0 && pN > 0;
            if (mixG || mixP)
            {
                R.Mixed++; R.MixedArea += f.Area;
                if (mixedAt.Length < 200) mixedAt.Append($" {f.Area:F2}㎡@({ip.X + ox:F2},{ip.Y + oy:F2}) 원지반 +{gP}/−{gN} 정지면 +{pP}/−{pN}");
            }
            // 면마다 세 상태 — 위(+) · 아래(−) · 같음(0). 섞이면 많은 쪽(이미 ⚠오류로 셌다)
            int gS = mixG ? (gP >= gN ? 1 : -1) : gP > 0 ? 1 : gN > 0 ? -1 : 0;
            int pS = mixP ? (pP >= pN ? 1 : -1) : pP > 0 ? 1 : pN > 0 ? -1 : 0;
            if ((gZ > 0 && (gP > 0 || gN > 0)) || (pZ > 0 && (pP > 0 || pN > 0))) R.PartialZero++;
            bool inA = gS > 0;                             // 옹벽 > 원지반
            bool inB = pS > 0;                             // 옹벽 > 정지면
            if (inA) { aFaces.Add(f); R.FacesA++; }
            if (inB) bFaces.Add(f); else if (pS < 0) R.FacesOutB++;
            if (pS < 0 || gS > 0) cls.Add((f, 1, gS, pS));                  // 정지면 아래이거나 원지반 위 — 버림
            else if (pS > 0 && gS < 0) { cls.Add((f, 0, gS, pS)); R.FacesKeep++; }   // 정지면 위 · 원지반 아래 — 남김
            else { cls.Add((f, 2, gS, pS)); R.CoplanarFaces++; R.CoplanarArea += f.Area; copFaces.Add(f); }   // 한쪽이 «같음» — 포개진 자리
            if (pag > 0) { R.FacesPlanAboveGround++; R.AreaPlanAboveGround += f.Area; }
        }

        // ④-b ★★[JACK 0918 «자글자글»] <b>포개진 자리</b>(옹벽 = 원지반 또는 옹벽 = 정지면)는 남기든 버리든 최종 지표면 높이가
        //   <b>똑같다</b> — 그러니 테두리가 가장 깔끔한 쪽을 고른다. 허용오차를 늘리는 것이 아니라 <b>높이가 같아 결과가 같은 자리만</b> 정한다.
        //   <para>가시를 만드는 것은 원지반·정지면 <b>삼각형의 비스듬한 변</b>이다(옹벽 계단 모서리는 곧다). 그래서 포개진 조각은
        //   <b>옹벽 모서리가 아닌 변을 더 많이 맞댄 이웃</b>을 따라간다 — 그 비스듬한 변이 띠 <b>안쪽으로 숨고</b> 테두리는 옹벽 모서리나
        //   진짜 닿는 선을 따라간다. (첫 판은 «맞닿은 길이 합»으로 골랐는데 현장 110m 정지면 소단이 양쪽 5.41m로 비겨 가시가 남았다.)
        //   그래도 비기면 맞닿은 길이 합, 그것도 비기면 버린다(높이는 같다).</para>
        int[] faceKeep = Array.Empty<int>();       // 조각마다 최종 판정(0 남김 · 1 버림) — ⑦-b가 링 변의 출처를 이웃 조각으로 정한다
        {
            // 옹벽이 <b>꺾이는 변</b>(계단 모서리) — 두 삼각형의 기울기가 다른 변 + 한 삼각형에만 속한 변
            IndexedFacetDistance? foldIdx = null;
            {
                var em = new Dictionary<((long, long), (long, long)), (double X1, double Y1, double X2, double Y2, int T0, int T1)>();
                static (long, long) Q(double x, double y) => ((long)Math.Round(x * 1e6), (long)Math.Round(y * 1e6));
                void AddE(int t, double x1, double y1, double x2, double y2)
                {
                    var a = Q(x1, y1); var b = Q(x2, y2);
                    if (a.Equals(b)) return;
                    var key = a.CompareTo(b) <= 0 ? (a, b) : (b, a);
                    em[key] = em.TryGetValue(key, out var v) ? (v.X1, v.Y1, v.X2, v.Y2, v.T0, t) : (x1, y1, x2, y2, t, -1);
                }
                for (int t = 0; t < W.N; t++)
                {
                    if (!W.Ok[t]) continue;
                    AddE(t, W.X1[t], W.Y1[t], W.X2[t], W.Y2[t]);
                    AddE(t, W.X2[t], W.Y2[t], W.X3[t], W.Y3[t]);
                    AddE(t, W.X3[t], W.Y3[t], W.X1[t], W.Y1[t]);
                }
                var fl = new List<LineString>();
                foreach (var v in em.Values)
                {
                    bool fold = v.T1 < 0 || Math.Abs(W.A[v.T0] - W.A[v.T1]) + Math.Abs(W.B[v.T0] - W.B[v.T1]) > 1e-6;
                    if (fold) fl.Add(gfF.CreateLineString(new[] { new Coordinate(v.X1, v.Y1), new Coordinate(v.X2, v.Y2) }));
                }
                if (fl.Count > 0) foldIdx = new IndexedFacetDistance(gfF.CreateMultiLineString(fl.ToArray()));
            }
            // 맞댄 선을 «옹벽 모서리 위»와 «그 밖(비스듬한 변)»으로 나눠 길이를 잰다
            (double Wall, double Other) Split(Geometry shared)
            {
                double w = 0, o = 0;
                foreach (var g in NetTopologySuite.Geometries.Utilities.LinearComponentExtracter.GetLines(shared))
                {
                    var cs = g.Coordinates;
                    for (int q = 0; q + 1 < cs.Length; q++)
                    {
                        double L = cs[q].Distance(cs[q + 1]);
                        if (L <= 0) continue;
                        var mid = gfF.CreatePoint(new Coordinate((cs[q].X + cs[q + 1].X) * 0.5, (cs[q].Y + cs[q + 1].Y) * 0.5));
                        if (foldIdx != null && foldIdx.Distance(mid) <= ProvTol) w += L; else o += L;
                    }
                }
                return (w, o);
            }
            var tree = new STRtree<int>();
            for (int k = 0; k < cls.Count; k++) tree.Insert(cls[k].F.EnvelopeInternal, k);
            if (cls.Count > 0) tree.Build();
            var c = new int[cls.Count]; for (int k = 0; k < cls.Count; k++) c[k] = cls[k].C;
            var cpAt = new StringBuilder();
            bool changed = true; int rounds = 0;
            // ★[검토 v100.1 · 낮음] 한 바퀴 안에서 바로 바꿔 넣으면 결과가 조각 순서에 따라 달라진다 —
            //   지난 바퀴의 판정(c)만 보고 이번 바퀴의 판정(nx)을 모은 뒤 한꺼번에 바꾼다
            while (changed && rounds++ < 20)
            {
                changed = false;
                var nx = (int[])c.Clone();
                for (int k = 0; k < cls.Count; k++)
                {
                    if (c[k] != 2) continue;
                    double lk = 0, ln = 0, ok = 0, on = 0;       // 맞닿은 길이 합 · 그중 옹벽 모서리가 아닌 길이
                    foreach (int j in tree.Query(cls[k].F.EnvelopeInternal))
                    {
                        if (j == k || c[j] == 2) continue;
                        Geometry sh;
                        try { sh = cls[k].F.Boundary.Intersection(cls[j].F.Boundary); } catch { R.CoplanarErrors++; continue; }
                        if (sh.IsEmpty || sh.Length <= 0) continue;
                        var (lw, lo) = Split(sh);
                        if (c[j] == 0) { lk += lw + lo; ok += lo; } else { ln += lw + lo; on += lo; }
                        if (DebugCoplanar && cpAt.Length < 3000)
                        {
                            var ipj = cls[j].F.InteriorPoint;
                            double ovA = 0; try { ovA = cls[k].F.Intersection(cls[j].F).Area; } catch { ovA = -1; }
                            cpAt.Append($"\n      [#{k}({cls[k].F.Area:F2}㎡·둘레 {cls[k].F.Length:F2}·구멍 {cls[k].F.NumInteriorRings})의 이웃 #{j}] {(c[j] == 0 ? "남김" : "버림")} {cls[j].F.Area:F2}㎡(구멍 {cls[j].F.NumInteriorRings})@({ipj.X + ox:F2},{ipj.Y + oy:F2}) 맞댐 {lw + lo:F3}(비스듬 {lo:F3})m · 겹친 넓이 {ovA:F4}㎡");
                        }
                    }
                    if (lk + ln <= 0) continue;
                    // ★[검토 v100.1 · 낮음] 비김은 1mm로 가른다(1e-6m로 가르면 폭 1mm 안의 조각들이 들쭉날쭉 정해진다)
                    nx[k] = Math.Abs(ok - on) > 0.001 ? (ok > on ? 0 : 1)     // 비스듬한 변을 더 많이 맞댄 쪽
                          : Math.Abs(lk - ln) > 0.001 ? (lk > ln ? 0 : 1)     // 비기면 맞닿은 길이 합
                          : 1;                                               // 그래도 비기면 버린다(높이는 같다)
                    if (cpAt.Length < 300) { var ipk = cls[k].F.InteriorPoint; cpAt.Append($" {cls[k].F.Area:F2}㎡@({ipk.X + ox:F1},{ipk.Y + oy:F1})→{(nx[k] == 0 ? "남김" : "버림")}(비스듬한 변 남김 {ok:F2}·버림 {on:F2}m / 합 {lk:F2}·{ln:F2}m)"); }
                    changed = true;
                }
                c = nx;
            }
            for (int k = 0; k < cls.Count; k++) if (c[k] == 2) { c[k] = 1; R.CoplanarIsolated++; }   // 이웃이 없다 — 버린다(높이는 같다)

            // ★★[검토 v100.1 · 중간] <b>다리 지킴</b> — 버린 포개진 조각이 서로 다른 남김 덩이 <b>둘 이상</b>에 닿아 있으면
            //   남김으로 뒤집는다. 옹벽 모서리로만 맞댄 다리는 위 규칙에서 «버림»으로 갈 수 있고, 그러면 띠가 둘로 갈라져
            //   «가장 큰 것만» 규칙이 진짜 벽 한 덩이를 버린다(합성 재현: 160 + 160㎡ → 한 덩이 328.5㎡가 맞다).
            for (int pass = 0; pass < 5; pass++)
            {
                var kf = new List<Geometry>();
                for (int k = 0; k < cls.Count; k++) if (c[k] == 0) kf.Add(cls[k].F);
                if (kf.Count < 2) break;
                Geometry ku;
                try { ku = UnaryUnionNG.Union(kf, pm); } catch { R.CoplanarErrors++; break; }
                var kc = new List<Polygon>();
                for (int q = 0; q < ku.NumGeometries; q++) if (ku.GetGeometryN(q) is Polygon pq && !pq.IsEmpty) kc.Add(pq);
                if (kc.Count < 2) break;
                bool flip = false;
                for (int k = 0; k < cls.Count; k++)
                {
                    if (cls[k].C != 2 || c[k] != 1) continue;
                    int touch = 0;
                    foreach (var comp in kc)
                    {
                        if (!comp.EnvelopeInternal.Intersects(cls[k].F.EnvelopeInternal)) continue;
                        double L = 0;
                        try { L = cls[k].F.Boundary.Intersection(comp.Boundary).Length; } catch { R.CoplanarErrors++; }
                        if (L > 0.001) touch++;
                    }
                    if (touch >= 2) { c[k] = 0; R.CoplanarBridge++; flip = true; }
                }
                if (!flip) break;
            }
            for (int k = 0; k < cls.Count; k++)
            {
                if (cls[k].C == 2) { if (c[k] == 0) R.CoplanarToKeep++; else { R.CoplanarToDrop++; if (cls[k].G == 0) R.GroundEqualDroppedArea += cls[k].F.Area; } }
                if (c[k] == 0) keepFaces.Add(cls[k].F);
            }
            faceKeep = c;
            if (R.CoplanarFaces > 0)
                tr.Append($"  ④-b 포개진 자리(옹벽 = 원지반/정지면) {R.CoplanarFaces}조각 {R.CoplanarArea:F2}㎡ — 남김 {R.CoplanarToKeep} · 버림 {R.CoplanarToDrop}"
                    + (R.CoplanarIsolated > 0 ? $" · 이웃 없어 버림 {R.CoplanarIsolated}" : "")
                    + (R.CoplanarBridge > 0 ? $" · <b>다리라 남김으로 뒤집음 {R.CoplanarBridge}</b>" : "")
                    + (R.GroundEqualDroppedArea > 0 ? $" · 옹벽=원지반이라 버린 자리 {R.GroundEqualDroppedArea:F2}㎡(합성 때 원지반 자리 = 파랑 안 − 띠)" : "")
                    + (R.CoplanarErrors > 0 ? $" · ⚠판정 중 예외 {R.CoplanarErrors}" : "")
                    + (R.PartialZero > 0 ? $" · 일부만 «같음»인 조각 {R.PartialZero}" : "") + $" (옹벽 모서리가 아닌 변을 더 많이 맞댄 쪽 · 최종 높이는 어느 쪽이든 같다):{cpAt}\n");
        }
        tr.Append($"  ④ 조각 {R.Faces}개 — <b>남길 곳 {R.FacesKeep}</b> · 옹벽이 원지반 위(버림) {R.FacesA} · 옹벽이 정지면 아래(버림) {R.FacesOutB}"
            + $" · ⚠섞임 {R.Mixed}{(R.Mixed > 0 ? $"({R.MixedArea:F2}㎡:{mixedAt})" : "")}"
            + $" · 모름 {R.FacesUnknown}{(R.FacesUnknown > 0 ? $"({R.UnknownArea:F1}㎡)" : "")} · 2mm 안 되는 띠 {R.FacesThin}\n");
        if (R.Mixed > 0) warns.Add($"부호가 섞인 조각 {R.Mixed}개({R.MixedArea:F2}㎡) — 그 자리 교선이 빠졌다:{mixedAt}");
        if (R.FacesUnknown > 0) warns.Add($"세 면 중 하나가 없는 조각 {R.FacesUnknown}개({R.UnknownArea:F1}㎡) — 범위 밖");
        // ★[검토 0918 · 중간 6] 정지면이 원지반보다 높은 곳(성토) — 이 셈은 절토 전제라 <b>조용히 버리지 않고</b> 알린다
        if (R.FacesPlanAboveGround > 0)
            warns.Add($"정지면이 원지반보다 {(down ? "낮은" : "높은")} 조각 {R.FacesPlanAboveGround}개({R.AreaPlanAboveGround:F2}㎡) — {side} 전제가 안 맞는 자리({other})");
        if (keepFaces.Count == 0)
            return Fail((down ? "⚠남길 띠 못 만듦 — 정지면보다 낮고 원지반보다 낮지 않은 옹벽이 없다"
                             : "⚠남길 띠 못 만듦 — 정지면보다 높고 원지반보다 높지 않은 옹벽이 없다")
                + (R.Dangles > 0 ? $"(★교선이 끊긴 끝 {R.Dangles}곳 — 계산이 선을 흘려 조각이 합쳐졌을 수 있다{At(dangles)})" : ""));

        // ⑤ 조각 합치기 → 가장 큰 것(JACK) · 1cm 안에 딴 조각이 있으면 갈라진 것
        Geometry keepU, aU, bU;
        try
        {
            keepU = UnaryUnionNG.Union(keepFaces, pm);
            aU = aFaces.Count > 0 ? UnaryUnionNG.Union(aFaces, pm) : gfF.CreateGeometryCollection();
            bU = bFaces.Count > 0 ? UnaryUnionNG.Union(bFaces, pm) : gfF.CreateGeometryCollection();
        }
        catch (Exception ex) { return Fail($"⚠남길 띠 못 만듦 — 조각 합치기가 터졌다 {ex.GetType().Name}"); }
        var comps = new List<Polygon>();
        for (int k = 0; k < keepU.NumGeometries; k++) if (keepU.GetGeometryN(k) is Polygon pg && !pg.IsEmpty) comps.Add(pg);
        comps.Sort((a, b) => b.Area.CompareTo(a.Area));
        R.Pieces = comps.Count;
        // ★[검토 v100.0 · 낮음 1] 합친 결과가 1mm 격자에서 무너져 폴리곤이 하나도 없을 수 있다
        if (comps.Count == 0) return Fail("⚠남길 띠 못 만듦 — 남길 조각을 합치니 1mm 격자에서 넓이가 남지 않았다(얇은 띠)");
        var chosen = comps[0];
        var chosenIdx = new IndexedFacetDistance(chosen);
        R.Area = chosen.Area;
        var dropAt = new StringBuilder();
        for (int k = 1; k < comps.Count; k++)
        {
            R.DroppedArea += comps[k].Area;
            string tag = "";
            if (comps[k].Distance(chosen) <= LineTol)
            {
                // ★[S139 ⑥ · 검토 v100.0 · 중간 1] 셋으로 가른다.
                //   ①<b>모든 점</b>이 큰 조각에서 1cm 안 → 부스러기(선 정확도 안 · 조용히 · 로그만)
                //   ②1cm 넘게 뻗는데 <b>격자보다 얇다</b>(평균 폭 ≤ 2mm) → 띠 끝 꼬리나 얕은 앞면 조각 — 버리되 <b>길이를 적어 ⚠</b>
                //     (종전엔 ②도 조용히 버렸다 — 40m 얕은 앞면이 소리 없이 빠질 수 있었다)
                //   ③1cm 넘게 뻗고 격자보다 두껍다 → 스냅이 띠를 끊은 것 → 틀린 곳(Broken)
                double far = 0;
                var cs = comps[k].ExteriorRing.Coordinates;
                for (int q = 0; q + 1 < cs.Length && far <= LineTol; q++)
                {
                    double L = cs[q].Distance(cs[q + 1]);
                    int ns = Math.Max(1, (int)Math.Ceiling(L / 0.001));
                    for (int t = 0; t <= ns && far <= LineTol; t++)
                        far = Math.Max(far, chosenIdx.Distance(gfF.CreatePoint(new Coordinate(
                            cs[q].X + (cs[q + 1].X - cs[q].X) * t / ns, cs[q].Y + (cs[q + 1].Y - cs[q].Y) * t / ns))));
                }
                double perim = comps[k].ExteriorRing.Length;
                double pw = 2 * comps[k].Area / Math.Max(1e-12, perim);
                if (far <= LineTol) { R.Fragments++; tag = $"·부스러기(1cm 안)"; }
                else if (pw <= TooThinWidth) { R.ThinDropped++; R.ThinDroppedLen += perim / 2; tag = $"·<b>격자보다 얇은 조각</b>(폭 {pw * 1000:F2}mm · 길이 {perim / 2:F2}m)"; }
                else { R.NearPieces++; tag = "·<b>1cm 안에서 갈라짐</b>"; }
            }
            if (k <= 4) { var c = comps[k].InteriorPoint; dropAt.Append($" {comps[k].Area:F2}㎡@({c.X + ox:F2},{c.Y + oy:F2}){tag}"); }
        }
        for (int h = 0; h < chosen.NumInteriorRings; h++)
            R.HoleArea += gfF.CreatePolygon((LinearRing)chosen.GetInteriorRingN(h)).Area;
        R.ChosenMeanWidth = 2 * chosen.Area / Math.Max(1e-12, chosen.Boundary.Length);
        if (R.ChosenMeanWidth <= TooThinWidth)
        {
            R.TooThin = true;
            warns.Add($"띠가 <b>1mm 격자보다 얇다</b>(평균 폭 {R.ChosenMeanWidth * 1000:F2}mm) — 얕은 {side}라 이 판은 순수 옹벽을 표현할 수 없다");
        }
        tr.Append($"  ⑤ 띠 {R.Pieces}조각 — <b>가장 큰 것 {R.Area:F2}㎡</b>를 쓴다"
            + (R.Pieces > 1 ? $" · 버린 조각 {R.Pieces - 1}개(합 {R.DroppedArea:F2}㎡:{dropAt})" : "")
            + (chosen.NumInteriorRings > 0 ? $" · 구멍 {chosen.NumInteriorRings}개({R.HoleArea:F2}㎡ — Hide로 뚫는다)" : "") + "\n");
        if (R.Pieces - 1 - R.Fragments > 0)
            warns.Add($"띠가 {R.Pieces}조각 — 가장 큰 것만 쓰고 {R.Pieces - 1 - R.Fragments}개는 버렸다(합 {R.DroppedArea:F2}㎡ · 부스러기 {R.Fragments} 포함)"
                + (R.ThinDropped > 0 ? $" · 그중 격자보다 얇은 조각 {R.ThinDropped}개 길이 합 <b>{R.ThinDroppedLen:F2}m</b>(그 자리는 {side}가 수 cm뿐인 옹벽 — 순수옹벽_DH에 안 들어간다)" : ""));
        if (R.NearPieces > 0) warns.Add($"띠가 <b>1cm 안에서 갈라졌다</b>({R.NearPieces}곳) — 얕은 {side}에서 스냅이 띠를 끊은 것으로 보인다(진짜 벽이 버려진다)");

        // ⑥ 끊긴 끝 — 곁가지(길이 ≤1cm · A·B·띠 경계에서 1cm 안) 규칙은 데이라잇과 같다
        {
            var bd = new List<Geometry> { keepU.Boundary };
            foreach (var cf in copFaces) bd.Add(cf.Boundary);   // ★[검토 v100.1 · 낮음] 버린 포개진 조각 둘레의 곁가지도 곁가지다
            if (!aU.IsEmpty) bd.Add(aU.Boundary);
            if (!bU.IsEmpty) bd.Add(bU.Boundary);
            var bIdx = new IndexedFacetDistance(gfF.CreateGeometryCollection(bd.ToArray()));
            var real = new List<Geometry>();
            foreach (var dg in dangles)
            {
                double far = 0; var dcs = dg.Coordinates;
                for (int q = 0; q + 1 < dcs.Length; q++)
                {
                    double L = dcs[q].Distance(dcs[q + 1]);
                    int ns = Math.Max(1, (int)Math.Ceiling(L / 0.001));
                    for (int t = 0; t <= ns; t++)
                        far = Math.Max(far, bIdx.Distance(gfF.CreatePoint(new Coordinate(
                            dcs[q].X + (dcs[q + 1].X - dcs[q].X) * t / ns, dcs[q].Y + (dcs[q + 1].Y - dcs[q].Y) * t / ns))));
                }
                if (far <= LineTol && dg.Length <= LineTol) { R.Spurs++; R.SpurMaxLen = Math.Max(R.SpurMaxLen, dg.Length); }
                else real.Add(dg);
            }
            R.Dangles = real.Count;
            if (R.Spurs > 0) tr.Append($"  ⑥ 곁가지 {R.Spurs}개(최장 {R.SpurMaxLen * 1000:F1}mm) — 선 정확도 안이라 그대로 간다\n");
            if (R.Dangles > 0) warns.Add($"교선이 끊긴 끝 {R.Dangles}곳{At(real)}");
        }

        // ⑦ 링 — 변마다 출처 · 높이는 짝 면(원지반 선 → 원지반, 정지면 선 → 정지면, 옹벽 밑선 → 옹벽 바닥)
        IndexedFacetDistance? zgIdx = zg.Lines.Count > 0 ? new IndexedFacetDistance(gfF.CreateMultiLineString(zg.Lines.ConvertAll(g => (LineString)g).ToArray())) : null;
        IndexedFacetDistance? zpIdx = zp.Lines.Count > 0 ? new IndexedFacetDistance(gfF.CreateMultiLineString(zp.Lines.ConvertAll(g => (LineString)g).ToArray())) : null;
        var polyIdx = new IndexedFacetDistance(polyG.ExteriorRing);
        var hulls = new List<Geometry>(); if (!hG.IsEmpty) hulls.Add(hG); if (!hP.IsEmpty) hulls.Add(hP);
        IndexedFacetDistance? hullIdx = hulls.Count > 0 ? new IndexedFacetDistance(gfF.CreateGeometryCollection(hulls.ToArray())) : null;
        // 0 교선(원지반) · 1 교선(정지면) · 2 옹벽 밑선(폴리곤 테두리) · 3 측량 경계 · 4 모름
        int KindAt(double x, double y)
        {
            var pt = gfF.CreatePoint(new Coordinate(x, y));
            double dG = zgIdx?.Distance(pt) ?? double.MaxValue, dP = zpIdx?.Distance(pt) ?? double.MaxValue;
            if (Math.Min(dG, dP) <= ProvTol) return dG <= dP ? 0 : 1;      // 가까운 쪽(검토 낮음 — 첫 일치가 아니라)
            if (polyIdx.Distance(pt) <= ProvTol) return 2;
            if (hullIdx != null && hullIdx.Distance(pt) <= ProvTol) return 3;
            return 4;
        }
        var unkAt = new StringBuilder();
        List<Point3> RingPts(LineString ls, bool measure)
        {
            var cs = ls.Coordinates;
            var raw = new List<(double X, double Y)>();
            for (int k = 0; k + 1 < cs.Length; k++) raw.Add((cs[k].X, cs[k].Y));
            if (measure) R.RawPts += raw.Count;
            var pts = new List<Point3>(raw.Count);
            for (int k = 0; k < raw.Count; k++)
            {
                var a = raw[k]; var b = raw[(k + 1) % raw.Count];
                int kv = KindAt(a.X, a.Y);
                double z;
                if (kv == 0 && G.TryZNear(a.X, a.Y, SampleClear, out double z0)) z = z0;
                else if (kv == 1 && P.TryZNear(a.X, a.Y, SampleClear, out double z1)) z = z1;
                else if (kv == 2) z = zBase;                              // 옹벽 밑선 = 0번 줄(검토 중간 5)
                else z = W.TryZNear(a.X, a.Y, SampleClear, out double z3) ? z3 : zBase;
                pts.Add(new Point3(a.X, a.Y, z));
                if (!measure) continue;
                double L = Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));
                int ke = KindAt((a.X + b.X) * 0.5, (a.Y + b.Y) * 0.5);
                switch (ke)
                {
                    case 0: R.LenZG += L; break;
                    case 1: R.LenZP += L; break;
                    case 2: R.LenPoly += L; break;
                    case 3: R.LenHull += L; break;
                    default:
                        R.LenUnknown += L; R.UnknownProv++;
                        if (unkAt.Length < 120) unkAt.Append($" {L:F3}m@({(a.X + b.X) * 0.5 + ox:F2},{(a.Y + b.Y) * 0.5 + oy:F2})");
                        break;
                }
            }
            return pts;
        }
        List<Point3> Clean(List<Point3> pts, out bool ok)
        {
            // 겹친 점(1mm)만 빼고, 0.1mm 안의 일직선만 편다 — 1cm로 펴면 절벽에서 높이 1m가 어긋난다(검토 중간 3)
            var d = Dedup(pts, out _);
            var sm = Simplify3D(d, 1e-4, out _);
            if (RingOk(gfF, sm)) { ok = true; return sm; }
            ok = RingOk(gfF, d);
            return d;
        }
        var ext = Clean(RingPts(chosen.ExteriorRing, true), out bool extOk);
        R.Simple = extOk;
        if (!extOk) warns.Add("<b>띠 링이 제 몸을 지른다</b>");
        var holes = new List<List<Point3>>();
        for (int h = 0; h < chosen.NumInteriorRings; h++)
        {
            var hr = Clean(RingPts(chosen.GetInteriorRingN(h), true), out bool hOk);
            if (!hOk) { R.Simple = false; warns.Add($"구멍 {h + 1}이 제 몸을 지른다"); }
            holes.Add(hr);
        }
        if (R.UnknownProv > 0) warns.Add($"어디서 왔는지 모를 변 {R.UnknownProv}개({R.LenUnknown:F3}m):{unkAt}");
        // ★[검토 v100.0 · 낮음 7] 다듬은 뒤 <b>구멍과 바깥 링이 서로 겹치지 않는가</b> — Civil에 Outer+Hide로 그대로 간다
        if (R.Simple && holes.Count > 0)
        {
            try
            {
                LinearRing LR(List<Point3> r0) { var cs = new Coordinate[r0.Count + 1]; for (int i = 0; i < r0.Count; i++) cs[i] = new Coordinate(r0[i].X, r0[i].Y); cs[r0.Count] = cs[0].Copy(); return gfF.CreateLinearRing(cs); }
                var whole = gfF.CreatePolygon(LR(ext), holes.ConvertAll(LR).ToArray());
                if (!whole.IsValid) { R.Simple = false; warns.Add("구멍과 바깥 링이 겹친다(띠 폴리곤이 무효)"); }
            }
            catch { R.Simple = false; warns.Add("띠 폴리곤을 못 만든다(구멍 포함)"); }
        }
        tr.Append($"  ⑦ 링(1mm 격자) — 점 {R.RawPts} → {ext.Count}점(겹친 점·0.1mm 일직선만 뺌) · 구멍 {holes.Count}"
            + $" · 원지반 닿는 선 {R.LenZG:F2}m · 정지면 닿는 선 {R.LenZP:F2}m · {baseLine} {R.LenPoly:F2}m"
            + (R.LenHull > 0 ? $" · 측량 경계 {R.LenHull:F2}m" : "") + (R.UnknownProv > 0 ? $" · ⚠출처 모름 {R.LenUnknown:F3}m" : "") + "\n");

        // ⑦-b ★★[JACK 0918 «또 수직방향이 톱니처럼 짤렸어 데이라잇에 깔끔하게 안잘려»] 링을 1mm 격자에서 <b>정확한 교선 위로</b> 되돌린다.
        //   ③ 잇기의 1mm 스냅라운딩은 꼭짓점을 평면에서 최대 0.707mm 옮긴다. 옹벽 앞면이 1:0.01이라 높이로 100배(폐합면 1:0.001은 1000배) —
        //   Civil은 테두리 높이를 옹벽 TIN에서 읽으므로 테두리가 초록 선에서 오르내렸다(현장 15:50 실측 최대 69.7mm ≈ 0.707mm × 100).
        //   스냅은 위상(조각 나누기·판정)에만 쓰고, Civil에 넘기는 링은 <b>스냅 전 교선</b>을 따라 다시 긋는다.
        //   ①변마다 <b>어느 선</b>인가를 거리로 짐작하지 않고 <b>양옆 조각의 판정</b>으로 정한다(검토 v100.2 · 높음 2 — 얇은 띠 끝에서
        //     원지반 선과 정지면 선이 1mm 안에 나란하다): 안(남김)과 밖(버림)의 원지반 부호가 다르면 원지반 선, 정지면 부호가 다르면
        //     정지면 선, 밖에 조각이 없으면 폴리곤 테두리(폴리곤 밖) 또는 측량 경계. ±ZeroD <b>두 벌 중 어느 벌</b>인지도 두 부호로 정한다
        //     (검토 높음 1 — 두 벌은 끝점이 따로 군집돼 서로 이어지지 않는다).
        //   ②꼭짓점은 그 선 위 가장 가까운 점, 선이 바뀌는 모서리는 두 선분의 교점(늘이지 않는다).
        //   ③변은 스냅 변 곁 통로(<see cref="UnsnapTube"/>) 안에서 그 선의 선분을 따라간다 — 한 칸(1mm)에 녹은 꺾임점을 되살린다.
        //   ④제 몸을 지르면 그 자리 5mm 안만 스냅 좌표로 되돌려 다시(10번). 그래도 안 되면 스냅 링을 그대로 쓰고 ⚠.
        R.RingSnap = ToWorld(ext);
        foreach (var h in holes) R.HolesSnap.Add(ToWorld(h));
        var extSnapL = ext; var holesSnapL = holes;
        if (R.Simple)
        try
        {
            const double Tau = UnsnapTube;
            // ★길 찾는 통로는 두 배 — 조각을 합칠 때(⑤ UnaryUnion) 1mm 격자에 <b>한 번 더</b> 맞춰, 진짜 꺾임점이 스냅 꼭짓점에서
            //   √2/2 mm를 넘을 수 있다(합성 «원지반 = 소단 + 2e-6»: 0.774mm — 검토 v100.2 · 낮음 8이 짚은 대로)
            const double Tube = 2 * UnsnapTube;
            var uNoPathAt = new StringBuilder(); var uGuessAt = new StringBuilder();
            void Why(StringBuilder sb, string what, double x, double y) { if (sb.Length < 400) sb.Append($" {what}@({x + ox:F3},{y + oy:F3})"); }
            // 선 여섯 벌: 0 원지반(+ZeroD) · 1 원지반(−ZeroD) · 2 정지면(+) · 3 정지면(−) · 4 폴리곤 테두리 · 5 측량 경계
            var sets = new List<(double X1, double Y1, double X2, double Y2)>[6];
            var trees = new STRtree<int>[6];
            for (int s = 0; s < 6; s++) { sets[s] = new(); trees[s] = new STRtree<int>(); }
            void AddSet(int s, Geometry g)
            {
                foreach (var l in NetTopologySuite.Geometries.Utilities.LinearComponentExtracter.GetLines(g))
                {
                    var c = l.Coordinates;
                    for (int q = 0; q + 1 < c.Length; q++)
                    {
                        if (c[q].Equals2D(c[q + 1])) continue;
                        trees[s].Insert(new Envelope(c[q], c[q + 1]), sets[s].Count);
                        sets[s].Add((c[q].X, c[q].Y, c[q + 1].X, c[q + 1].Y));
                    }
                }
            }
            int nGp = zg.Lines.Count - zg2.Lines.Count, nPp = zp.Lines.Count - zp2.Lines.Count;   // ② 에서 +벌 뒤에 −벌을 붙였다
            for (int i = 0; i < zg.Lines.Count; i++) AddSet(i < nGp ? 0 : 1, zg.Lines[i]);
            for (int i = 0; i < zp.Lines.Count; i++) AddSet(i < nPp ? 2 : 3, zp.Lines[i]);
            AddSet(4, polyG.ExteriorRing);
            if (!hG.IsEmpty) AddSet(5, hG);
            if (!hP.IsEmpty) AddSet(5, hP);
            for (int s = 0; s < 6; s++) if (sets[s].Count > 0) trees[s].Build();

            // 높이를 받을 면 — 점을 <b>올려놓은 선</b>의 짝 면(검토 중간 3): 0 원지반 · 1 정지면 · 2 옹벽 바닥(zBase) · 3 옹벽
            static int ZFam(int s) => s <= 1 ? 0 : s <= 3 ? 1 : s == 4 ? 2 : 3;
            int ZFamKind(double x, double y) { int kv = KindAt(x, y); return kv <= 2 ? kv : 3; }
            double ZOf(int zf, double x, double y)
            {
                if (zf == 0 && G.TryZNear(x, y, SampleClear, out double z0)) return z0;
                if (zf == 1 && P.TryZNear(x, y, SampleClear, out double z1)) return z1;
                if (zf == 2) return zBase;
                return W.TryZNear(x, y, SampleClear, out double z3) ? z3 : zBase;
            }
            bool Near(int s, double x, double y, out double px, out double py, out double d, double tol = Tau)
            {
                px = x; py = y; d = double.MaxValue;
                if (s < 0 || sets[s].Count == 0) return false;
                foreach (int i in trees[s].Query(new Envelope(x - tol, x + tol, y - tol, y + tol)))
                {
                    var o = OnSeg(sets[s][i], x, y);
                    if (o.D < d) { d = o.D; px = o.X; py = o.Y; }
                }
                return d <= tol;
            }
            // 먼저 τ, 없으면 통로 폭까지
            bool NearW(int s, double x, double y, out double px, out double py, out double d)
                => Near(s, x, y, out px, out py, out d) || Near(s, x, y, out px, out py, out d, Tube);
            // 두 선이 만나는 점 — 선분이 실제로 지르는 곳만(늘이지 않는다 · 검토 낮음 7) · 스냅 점에서 통로 폭 안
            //   (합성 «웅덩이»: 정지면 선과 원지반 선의 모서리가 스냅 점에서 0.75mm 넘게 떨어져 1.06mm 이음선이 남길 곳 모서리를 깎았다)
            bool Cross(int s1, int s2, double x, double y, out double px, out double py)
            {
                px = x; py = y; double best = double.MaxValue;
                if (sets[s1].Count == 0 || sets[s2].Count == 0) return false;
                var env = new Envelope(x - Tube, x + Tube, y - Tube, y + Tube);
                var c2 = trees[s2].Query(env);
                foreach (int i1 in trees[s1].Query(env))
                {
                    var a = sets[s1][i1];
                    foreach (int i2 in c2)
                    {
                        var b = sets[s2][i2];
                        double rx = a.X2 - a.X1, ry = a.Y2 - a.Y1, sx = b.X2 - b.X1, sy = b.Y2 - b.Y1, den = rx * sy - ry * sx;
                        double La = Math.Sqrt(rx * rx + ry * ry), Lb = Math.Sqrt(sx * sx + sy * sy);
                        if (Math.Abs(den) <= 1e-9 * La * Lb) continue;          // 거의 평행 — 교점이 불안정하다
                        double t = ((b.X1 - a.X1) * sy - (b.Y1 - a.Y1) * sx) / den, u = ((b.X1 - a.X1) * ry - (b.Y1 - a.Y1) * rx) / den;
                        double et = 1e-9 / Math.Max(La, 1e-12), eu = 1e-9 / Math.Max(Lb, 1e-12);
                        if (t < -et || t > 1 + et || u < -eu || u > 1 + eu) continue;
                        double ix = a.X1 + rx * t, iy = a.Y1 + ry * t, d = Math.Sqrt((ix - x) * (ix - x) + (iy - y) * (iy - y));
                        if (d < best) { best = d; px = ix; py = iy; }
                    }
                }
                if (best <= Tube) return true;
                // 지르지 않고 <b>끝이 닿기만</b> 하는 곳(교선이 폴리곤 테두리에서 끝남 등) — 두 선분이 ClusterTol 안이면 그 가장 가까운 점
                foreach (int i1 in trees[s1].Query(env))
                {
                    var a = sets[s1][i1];
                    foreach (int i2 in c2)
                    {
                        var b = sets[s2][i2];
                        foreach (var (qx, qy, onB) in new[] { (a.X1, a.Y1, true), (a.X2, a.Y2, true), (b.X1, b.Y1, false), (b.X2, b.Y2, false) })
                        {
                            var o = OnSeg(onB ? b : a, qx, qy);
                            if (o.D > ClusterTol) continue;
                            double mx = (qx + o.X) * 0.5, my = (qy + o.Y) * 0.5, d = Math.Sqrt((mx - x) * (mx - x) + (my - y) * (my - y));
                            if (d < best) { best = d; px = mx; py = my; }
                        }
                    }
                }
                return best <= Tube;
            }
            // 변 하나 — 선 s의 정확한 선분을 따라 P에서 Q로(스냅 변 a→b 곁 통로 안). 사이 점만 돌려준다
            List<(double X, double Y)> PathMid(int s, Coordinate a, Coordinate b, (double X, double Y) P, (double X, double Y) Q, ref int noPath)
            {
                var res = new List<(double X, double Y)>();
                if (sets[s].Count == 0) { noPath++; Why(uNoPathAt, $"선{s}없음", (a.X + b.X) * 0.5, (a.Y + b.Y) * 0.5); return res; }
                var env = new Envelope(Math.Min(a.X, b.X) - Tube, Math.Max(a.X, b.X) + Tube, Math.Min(a.Y, b.Y) - Tube, Math.Max(a.Y, b.Y) + Tube);
                var cand = new List<int>();
                foreach (int i in trees[s].Query(env)) if (SegSegD(sets[s][i], a.X, a.Y, b.X, b.Y) <= Tube) cand.Add(i);
                int sP = -1, sQ = -1; double dP = double.MaxValue, dQ = double.MaxValue;
                (double X, double Y, double D) oP = default, oQ = default;
                foreach (int i in cand)
                {
                    var o = OnSeg(sets[s][i], P.X, P.Y); if (o.D < dP) { dP = o.D; sP = i; oP = o; }
                    o = OnSeg(sets[s][i], Q.X, Q.Y); if (o.D < dQ) { dQ = o.D; sQ = i; oQ = o; }
                }
                if (sP < 0 || sQ < 0 || dP > Tube || dQ > Tube)
                {
                    noPath++; Why(uNoPathAt, $"선{s} 끝 못 올림(후보 {cand.Count} · 앞 {(dP == double.MaxValue ? -1 : dP * 1000):F3} · 뒤 {(dQ == double.MaxValue ? -1 : dQ * 1000):F3}mm)", (a.X + b.X) * 0.5, (a.Y + b.Y) * 0.5);
                    return res;
                }
                if (dP > 1e-9) res.Add((oP.X, oP.Y));
                if (sP != sQ)
                {
                    // 통로 안 마디만 지나는 최단 경로 — 한 벌 안의 끝점은 ZeroLines 군집·폴리곤 좌표라 정확히 같은 값이다
                    var id = new Dictionary<(double, double), int>(); var xy = new List<(double X, double Y)>();
                    var adj = new List<List<(int To, double W)>>();
                    int Node(double x, double y)
                    {
                        if (id.TryGetValue((x, y), out int k)) return k;
                        id[(x, y)] = k = xy.Count; xy.Add((x, y)); adj.Add(new()); return k;
                    }
                    bool InTube(double x, double y) => SegD(x, y, a.X, a.Y, b.X, b.Y) <= Tube;
                    foreach (int i in cand)
                    {
                        var g = sets[s][i];
                        int u = Node(g.X1, g.Y1), v = Node(g.X2, g.Y2);
                        double w = Math.Sqrt((g.X2 - g.X1) * (g.X2 - g.X1) + (g.Y2 - g.Y1) * (g.Y2 - g.Y1));
                        adj[u].Add((v, w)); adj[v].Add((u, w));
                    }
                    var dist = new double[xy.Count]; var prev = new int[xy.Count];
                    for (int k = 0; k < xy.Count; k++) { dist[k] = double.MaxValue; prev[k] = -1; }
                    var pq = new PriorityQueue<int, double>();
                    var gP = sets[s][sP];
                    foreach (var (ex, ey) in new[] { (gP.X1, gP.Y1), (gP.X2, gP.Y2) })
                    {
                        if (!InTube(ex, ey)) continue;
                        int k = id[(ex, ey)]; double w = Math.Sqrt((ex - oP.X) * (ex - oP.X) + (ey - oP.Y) * (ey - oP.Y));
                        if (w < dist[k]) { dist[k] = w; pq.Enqueue(k, w); }
                    }
                    while (pq.TryDequeue(out int u, out double du))
                    {
                        if (du > dist[u]) continue;
                        foreach (var (v, w) in adj[u])
                        {
                            if (!InTube(xy[v].X, xy[v].Y)) continue;
                            if (du + w < dist[v]) { dist[v] = du + w; prev[v] = u; pq.Enqueue(v, du + w); }
                        }
                    }
                    var gQ = sets[s][sQ]; int end = -1; double bestEnd = double.MaxValue;
                    foreach (var (ex, ey) in new[] { (gQ.X1, gQ.Y1), (gQ.X2, gQ.Y2) })
                    {
                        int k = id[(ex, ey)];
                        if (dist[k] == double.MaxValue) continue;
                        double cst = dist[k] + Math.Sqrt((ex - oQ.X) * (ex - oQ.X) + (ey - oQ.Y) * (ey - oQ.Y));
                        if (cst < bestEnd) { bestEnd = cst; end = k; }
                    }
                    if (end < 0 && DebugUnsnapAt is { } dA && Math.Abs((a.X + b.X) * 0.5 + ox - dA.X) < 0.005 && Math.Abs((a.Y + b.Y) * 0.5 + oy - dA.Y) < 0.005)
                    {
                        tr.Append($"      [dbg] 길 없음 선{s} 스냅변 ({a.X + ox:F6},{a.Y + oy:F6})→({b.X + ox:F6},{b.Y + oy:F6}) P({P.X + ox:F6},{P.Y + oy:F6}) Q({Q.X + ox:F6},{Q.Y + oy:F6}) sP {sP} sQ {sQ}" + (char)10);
                        foreach (int i in cand) { var g = sets[s][i]; tr.Append($"      [dbg]   후보 {i} ({g.X1 + ox:F7},{g.Y1 + oy:F7})→({g.X2 + ox:F7},{g.Y2 + oy:F7}) 통로 {InTube(g.X1, g.Y1)}/{InTube(g.X2, g.Y2)} 거리 {dist[id[(g.X1, g.Y1)]]:G4}/{dist[id[(g.X2, g.Y2)]]:G4}" + (char)10); }
                    }
                    if (end < 0) { noPath++; Why(uNoPathAt, $"선{s} 이어진 길 없음(후보 {cand.Count})", (a.X + b.X) * 0.5, (a.Y + b.Y) * 0.5); }
                    else
                    {
                        var chain = new List<(double X, double Y)>();
                        for (int k = end; k >= 0; k = prev[k]) chain.Add(xy[k]);
                        chain.Reverse();
                        res.AddRange(chain);
                    }
                }
                if (dQ > 1e-9) res.Add((oQ.X, oQ.Y));
                return res;
            }
            // 옹벽 높이를 쓰는 변(측량 경계·출처 모름)은 옹벽 삼각형 변에서 꺾인다 — 그 교점을 넣는다(검토 낮음 10)
            List<(double X, double Y)> WallCross(double ax, double ay, double bx, double by)
            {
                var ts = new List<double>();
                double rx = bx - ax, ry = by - ay;
                foreach (int i in W.Tree.Query(new Envelope(ax, bx, ay, by)))
                {
                    (double X, double Y)[] E = { (W.X1[i], W.Y1[i]), (W.X2[i], W.Y2[i]), (W.X3[i], W.Y3[i]) };
                    for (int e = 0; e < 3; e++)
                    {
                        var m = E[e]; var m2 = E[(e + 1) % 3];
                        double sx = m2.X - m.X, sy = m2.Y - m.Y, den = rx * sy - ry * sx;
                        if (Math.Abs(den) < 1e-18) continue;
                        double t = ((m.X - ax) * sy - (m.Y - ay) * sx) / den, u = ((m.X - ax) * ry - (m.Y - ay) * rx) / den;
                        if (t > 1e-9 && t < 1 - 1e-9 && u >= 0 && u <= 1) ts.Add(t);
                    }
                }
                ts.Sort();
                var res = new List<(double X, double Y)>();
                double last = -1;
                foreach (var t in ts) { if (t - last < 1e-9) continue; res.Add((ax + rx * t, ay + ry * t)); last = t; }
                return res;
            }

            // 조각 찾기 — 링 변의 안(남김)과 밖(버림) 조각
            var fTree = new STRtree<int>();
            for (int k = 0; k < cls.Count; k++) fTree.Insert(cls[k].F.EnvelopeInternal, k);
            if (cls.Count > 0) fTree.Build();
            var fPrep = new Dictionary<int, IPreparedGeometry>();
            int FaceAt(double x, double y)
            {
                if (cls.Count == 0) return -1;
                var pt = gfF.CreatePoint(new Coordinate(x, y));
                foreach (int k in fTree.Query(new Envelope(x, x, y, y)))
                {
                    if (!fPrep.TryGetValue(k, out var pg)) fPrep[k] = pg = PreparedGeometryFactory.Prepare(cls[k].F);
                    if (pg.Contains(pt)) return k;
                }
                return -1;
            }
            var chosenPrep = PreparedGeometryFactory.Prepare(chosen);
            var polyPrep = PreparedGeometryFactory.Prepare(polyG);
            // 변 하나의 후보 선(앞이 먼저) — 양옆 조각의 부호로. 못 정하면 종전처럼 거리로 짐작하고 센다
            List<int> Cands(Coordinate a, Coordinate b, ref int guess)
            {
                var l = new List<int>();
                double mx = (a.X + b.X) * 0.5, my = (a.Y + b.Y) * 0.5, dx = b.X - a.X, dy = b.Y - a.Y, L = Math.Sqrt(dx * dx + dy * dy);
                if (L > 0 && faceKeep.Length == cls.Count)
                {
                    // 안쪽(띠 쪽) 단위 법선 — 링의 돌림 방향은 모르므로 1e-7 옆 점이 띠 안인가로 정한다
                    double ux = -dy / L, uy = dx / L;
                    if (!chosenPrep.Contains(gfF.CreatePoint(new Coordinate(mx + ux * 1e-7, my + uy * 1e-7)))) { ux = -ux; uy = -uy; }
                    // 양옆으로 조금씩 나가며 <b>남김 조각</b>(안)과 <b>버림 조각 또는 조각 없음</b>(밖)을 찾는다. 바로 옆이 아닐 수 있다 —
                    //   조각을 합칠 때 1mm 격자에서 폭 1mm 못 되는 조각이 녹아(합성 «어긋난 격자»: 남김 판정 0.0001㎡ 조각이 링 밖에 남음,
                    //   «웅덩이»: 링 변이 남김 조각 한가운데를 지남) 링 변 바로 옆이 그 조각일 수 있다. 통로 폭까지만 본다
                    int fi = -1, fo = -2; double qx = mx, qy = my;
                    foreach (double st in new[] { 1e-7, 1e-5, 1e-4, 3e-4, 7e-4, Tube })
                    {
                        int f = FaceAt(mx + ux * st, my + uy * st);
                        if (f >= 0 && faceKeep[f] == 0) { fi = f; break; }
                    }
                    foreach (double st in new[] { 1e-7, 1e-5, 1e-4, 3e-4, 7e-4, Tube })
                    {
                        qx = mx - ux * st; qy = my - uy * st;
                        int f = FaceAt(qx, qy);
                        if (f < 0 || faceKeep[f] != 0) { fo = f; break; }
                    }
                    bool inKeep = fi >= 0;
                    if (inKeep && fo == -1)
                    {
                        // 밖에 조각이 없다 — 폴리곤 테두리 또는 측량 경계, 변 가운데에서 가까운 쪽 먼저
                        Near(4, mx, my, out _, out _, out double d4, Tube); Near(5, mx, my, out _, out _, out double d5, Tube);
                        if (d4 <= d5) { l.Add(4); l.Add(5); } else { l.Add(5); l.Add(4); }
                        return l;
                    }
                    if (inKeep && fo >= 0)
                    {
                        // 부호(−1 아래 · 0 같음 · +1 위) 사이 경계: {−1,0} → −ZeroD 선 · {0,+1} → +ZeroD 선 ·
                        //   {−1,+1} → 두 선이 스냅으로 한 줄에 녹은 곳(남김 쪽 벌 먼저). 다른 벌은 뒷순위로 붙인다
                        void Add(int iS, int oS, int plus, int minus)
                        {
                            if (iS == oS) return;
                            int lo = Math.Min(iS, oS), hi = Math.Max(iS, oS);
                            int first = hi <= 0 ? minus : lo >= 0 ? plus : (iS < 0 ? minus : plus);
                            l.Add(first); l.Add(first == plus ? minus : plus);
                        }
                        Add(cls[fi].G, cls[fo].G, 0, 1);
                        Add(cls[fi].P, cls[fo].P, 2, 3);
                        if (l.Count > 0) return l;
                    }
                }
                guess++; Why(uGuessAt, $"짐작(길이 {L * 1000:F3}mm)", mx, my);
                switch (KindAt(mx, my)) { case 0: l.Add(1); l.Add(0); break; case 1: l.Add(2); l.Add(3); break; case 2: l.Add(4); break; case 3: l.Add(5); break; }
                return l;
            }
            // 한 점의 판정 — +1 분명히 남길 곳(정지면 < 옹벽 < 원지반 · 폴리곤 안) · −1 분명히 버릴 곳 · 0 모름/포개짐(|옹벽 − 면| ≤ ZeroD)
            int SideCls(double x, double y)
            {
                if (!W.TryZ(x, y, out double zw) || !G.TryZ(x, y, out double zg0) || !P.TryZ(x, y, out double zp0)) return 0;
                double dg = zw - zg0, dp = zw - zp0;
                if (Math.Abs(dg) <= ZeroD || Math.Abs(dp) <= ZeroD) return 0;
                return dp > 0 && dg < 0 ? 1 : -1;
            }
            // 선 위 점 (x, y)에서 안쪽(inx, iny)으로 δ · 바깥으로 δ: +1 맞는 선 · −1 틀린 선 · 0 못 가림.
            //   완만한 자리(소단)에선 20µm로 높이차가 ZeroD도 안 나 «못 가림»이 된다 — 가려질 때까지 넓힌다(통로 폭 안)
            int SideOk(double x, double y, double inx, double iny)
            {
                foreach (double dl in new[] { 2e-5, 1e-4, 5e-4 })
                {
                    int ci = SideCls(x + inx * dl, y + iny * dl), co = SideCls(x - inx * dl, y - iny * dl);
                    if (ci == 0 && co == 0) continue;
                    return ci >= 0 && co <= 0 ? 1 : -1;
                }
                return 0;
            }
            int sideSwitch = 0, sideNone = 0, uCuts = 0;
            var uCutAt = new List<(double X, double Y)>();

            // 같은 점 — 가장 짧은 변(10µm) 안. ±ZeroD 두 벌이 만나는 곳은 두 벌의 마디가 µm 어긋나 있어 정확히 같지 않다
            //   (현장 15:50 (210254.07, 509779.27): 115m 정지면 소단이 옹벽 소단과 포개 두 벌이 갈라지는 곳 — 1e-9로 재면 가시가 남아 제 몸을 질렀다)
            static bool Same((double X, double Y, int Z, bool Wn) a, (double X, double Y, int Z, bool Wn) b)
                => (a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y) <= MinRingEdge * MinRingEdge;

            int uMoved = 0, uCorner = 0, uCornerNear = 0, uKept = 0, uNoPath = 0, uBends = 0, uSpikes = 0, uRepaired = 0, uGuess = 0, uWall = 0, uStraight = 0;
            double uMaxMove = 0;
            var uRepairAt = new StringBuilder();
            // 점 하나 = (x, y, 높이를 받을 면, 다음 점까지 <b>옹벽을 따라야 하는 변</b>인가 — 두 선을 잇는 짧은 선 · 선을 못 찾은 곧은 변)
            List<Point3>? ExactRing(LineString ls)
            {
                if (DebugForceUnsnapFail) return null;
                var cs = ls.Coordinates; int n = cs.Length - 1;
                if (n < 3) return null;
                int guess = 0;
                var es = new int[n];
                var inN = new (double X, double Y)[n];      // 스냅 변마다 안쪽(띠 쪽) 단위 법선
                for (int k = 0; k < n; k++)
                {
                    es[k] = -1;
                    double mx = (cs[k].X + cs[k + 1].X) * 0.5, my = (cs[k].Y + cs[k + 1].Y) * 0.5;
                    var cands = Cands(cs[k], cs[k + 1], ref guess);
                    // ★★[하네스 «맞는 선 위» 검사가 잡음] 조각 판정만으로는 <b>폭 1mm 못 되는 틈</b>을 못 본다 — 격자에서 녹아 조각이 없다
                    //   (합성 «어긋난 격자»: 옹벽 밑선 안쪽 수십 µm에 정지면 선이 나란히 가는데 링이 폴리곤 테두리에 올라 37변이 어긋남).
                    //   그래서 후보 선마다 <b>그 선 양옆 20µm에서 세 면 높이를 직접 재</b> 안은 버릴 곳이 아니고 밖은 남길 곳이 아닌 선을 고른다.
                    //   조각 판정이 고른 순서가 먼저, 나머지 선은 뒤(포개진 자리처럼 둘 다 맞으면 조각 판정을 따른다)
                    double ex = cs[k + 1].X - cs[k].X, ey = cs[k + 1].Y - cs[k].Y, eL = Math.Sqrt(ex * ex + ey * ey);
                    double inx = 0, iny = 0;
                    if (eL > 0)
                    {
                        inx = -ey / eL; iny = ex / eL;
                        if (!chosenPrep.Contains(gfF.CreatePoint(new Coordinate(mx + inx * 1e-7, my + iny * 1e-7)))) { inx = -inx; iny = -iny; }
                    }
                    inN[k] = (inx, iny);
                    var order = new List<int>(cands);
                    for (int s = 0; s < 6; s++) if (!order.Contains(s)) order.Add(s);
                    bool any = false;
                    foreach (int s in order)
                    {
                        if (eL <= 0 || !Near(s, mx, my, out double px, out double py, out double pd, Tube)) continue;
                        int v = SideOk(px, py, inx, iny);
                        if (DebugUnsnapAt is { } dz && Math.Abs(mx + ox - dz.X) < 0.005 && Math.Abs(my + oy - dz.Y) < 0.005)
                            tr.Append($"      [dbg] 변 #{k} ({cs[k].X + ox:F6},{cs[k].Y + oy:F6})→({cs[k + 1].X + ox:F6},{cs[k + 1].Y + oy:F6}) 조각 순서 [{string.Join(",", cands)}] 선 {s} 거리 {pd * 1000:F3}mm 양옆 {v}" + (char)10);
                        if (v != 0) any = true;
                        if (v > 0) { es[k] = s; if (cands.Count == 0 || s != cands[0]) sideSwitch++; break; }
                    }
                    if (es[k] >= 0) continue;
                    // 어느 선도 양옆이 갈리지 않는다(포개진 자리 · 면이 없는 자리) — 조각 판정대로: 먼저 τ 안, 없으면 통로 폭 안
                    //   (검토 v100.2 · 중간 1 — 꼭짓점을 통로 폭까지 옮기면서 변만 τ로 고르면 곧은 변이 셈 없이 샌다)
                    if (any) sideNone++;
                    foreach (int s in cands) if (Near(s, mx, my, out _, out _, out _)) { es[k] = s; break; }
                    if (es[k] < 0) foreach (int s in cands) if (Near(s, mx, my, out _, out _, out _, Tube)) { es[k] = s; break; }
                }
                var keepSnap = new bool[n];
                bool Dbg(double x, double y) => DebugUnsnapAt is { } da && Math.Abs(x + ox - da.X) < 0.005 && Math.Abs(y + oy - da.Y) < 0.005;
                if (DebugUnsnapAt != null)
                    for (int k = 0; k < n; k++)
                        if (Dbg(cs[k].X, cs[k].Y)) tr.Append($"      [dbg] 스냅 #{k} ({cs[k].X + ox:F6},{cs[k].Y + oy:F6}) 들어오는 변 선 {es[(k - 1 + n) % n]} · 나가는 변 선 {es[k]}" + (char)10);
                for (int pass = 0; pass <= 10; pass++)
                {
                    int moved = 0, corner = 0, cornerNear = 0, kept = 0, noPath = 0, bends = 0, spikes = 0, wall = 0, straight = 0, cuts = 0; double maxMove = 0;
                    var cutAt = new List<(double X, double Y)>();
                    // 꼭짓점마다 <b>들어오는 변의 끝</b>(Vin)과 <b>나가는 변의 시작</b>(Vout). 보통 한 점이고, 두 선이 교점 없이 만나는
                    //   모서리(같은 면의 두 벌이 갈아타는 곳 · 두 선이 거의 나란히 만나는 띠 끝 · 옹벽 밑선을 따라 붙은 정지면 선)에서만
                    //   두 점 — 각 변이 <b>제 선 위에서</b> 끝나고 사이를 짧게(≤ 통로 폭) 잇는다. 한 점으로 우기면 딴 선의 변이 제 선에 못 올라탄다
                    var Vin = new (double X, double Y, int Z, bool Wn)[n]; var Vout = new (double X, double Y, int Z, bool Wn)[n];
                    for (int k = 0; k < n; k++)
                    {
                        int s1 = es[(k - 1 + n) % n], s2 = es[k];
                        double x = cs[k].X, y = cs[k].Y, px, py, d;
                        int zfSnap = s2 >= 0 ? ZFam(s2) : s1 >= 0 ? ZFam(s1) : ZFamKind(x, y);
                        if (keepSnap[k] || (s1 < 0 && s2 < 0)) { Vin[k] = Vout[k] = (x, y, zfSnap, false); kept++; continue; }
                        if (s1 == s2 || s1 < 0 || s2 < 0)
                        {
                            int s = s1 < 0 ? s2 : s1;
                            if (NearW(s, x, y, out px, out py, out d)) { Vin[k] = Vout[k] = (px, py, ZFam(s), false); moved++; maxMove = Math.Max(maxMove, d); }
                            else { Vin[k] = Vout[k] = (x, y, zfSnap, false); kept++; }
                            continue;
                        }
                        if (Cross(s1, s2, x, y, out px, out py))
                        {
                            Vin[k] = Vout[k] = (px, py, Math.Min(ZFam(s1), ZFam(s2)), false); corner++;
                            maxMove = Math.Max(maxMove, Math.Sqrt((px - x) * (px - x) + (py - y) * (py - y)));
                            continue;
                        }
                        bool n1 = NearW(s1, x, y, out double p1x, out double p1y, out double d1), n2 = NearW(s2, x, y, out double p2x, out double p2y, out double d2);
                        // 두 점을 잇는 짧은 선은 옹벽을 따른다(Wn) — 그 사이는 어느 선 위도 아니니 Civil이 읽을 옹벽 높이가 곧 테두리다(검토 낮음 3)
                        if (n1 && n2)
                        {
                            Vin[k] = (p1x, p1y, ZFam(s1), true); Vout[k] = (p2x, p2y, ZFam(s2), false); cornerNear++; maxMove = Math.Max(maxMove, Math.Max(d1, d2));
                            // 이음선 바깥도 <b>남길 곳</b>이면 여기는 모서리가 아니라 띠가 <b>격자보다 얇아져 닫은 자리</b>다 — 원지반 선과 정지면 선이
                            //   1mm 안으로 좁아지며 만나는 띠 끝, 또는 «가장 큰 것만» 규칙이 얇은 조각을 떼어 낸 곳(⑤ 얇은 조각 ⚠).
                            //   그 너머는 폭 1mm 못 되는 쐐기라 1mm 격자 위상으로는 못 담는다(v100.1 스냅 링도 여기서 닫았다). 이음선은 옹벽을 따르므로
                            //   테두리 높이는 맞다. 자리를 적어 하네스가 «맞는 선 위» 어긋남을 이 자리에서만 허용한다
                            var nn = inN[k];
                            if (SideCls((p1x + p2x) * 0.5 - nn.X * 2e-5, (p1y + p2y) * 0.5 - nn.Y * 2e-5) > 0)
                            {
                                cuts++;
                                cutAt.Add(((p1x + p2x) * 0.5 + ox, (p1y + p2y) * 0.5 + oy));
                            }
                        }
                        else if (n1) { Vin[k] = Vout[k] = (p1x, p1y, ZFam(s1), false); cornerNear++; maxMove = Math.Max(maxMove, d1); }
                        else if (n2) { Vin[k] = Vout[k] = (p2x, p2y, ZFam(s2), false); cornerNear++; maxMove = Math.Max(maxMove, d2); }
                        else { Vin[k] = Vout[k] = (x, y, zfSnap, false); kept++; }
                    }
                    // 링을 <b>한 선 위 가운데 꼭짓점</b>에서 시작한다 — 되돌아가는 고리는 선이 바뀌는 모서리에서 나므로 시작점에 안 걸리게
                    int k0 = 0;
                    for (int k = 0; k < n; k++) if (es[k] >= 0 && es[(k - 1 + n) % n] == es[k] && !keepSnap[k]) { k0 = k; break; }
                    var pts = new List<(double X, double Y, int Z, bool Wn)>();
                    for (int kk = 0; kk < n; kk++)
                    {
                        int k = (k0 + kk) % n, k2 = (k + 1) % n, s = es[k];
                        bool twoPts = Vout[k].X != Vin[k].X || Vout[k].Y != Vin[k].Y;
                        pts.Add(Vin[k]);
                        if (twoPts) pts.Add(Vout[k]);
                        int np0 = noPath;
                        var mid = (s < 0 || keepSnap[k] || keepSnap[k2]) ? null
                                : PathMid(s, cs[k], cs[k + 1], (Vout[k].X, Vout[k].Y), (Vin[k2].X, Vin[k2].Y), ref noPath);
                        if (mid == null || noPath > np0)
                        {
                            // 곧은 변(선을 못 찾음 · 스냅 쪽이 낌 · 길 없음) — 옹벽을 따르게 하고 센다
                            if (s < 0) straight++;
                            pts[^1] = (pts[^1].X, pts[^1].Y, pts[^1].Z, true);
                            continue;
                        }
                        foreach (var q in mid) { pts.Add((q.X, q.Y, ZFam(s), false)); bends++; }
                    }
                    // 겹친 점 · <b>제자리로 돌아오는 작은 고리</b>(a, b, …, a → a) — 꼭짓점을 선분 위로 옮기면 꺾임점 앞뒤로 가시가 나고,
                    //   두 벌이 만나는 모서리에서는 꼭짓점이 딴 벌의 가지 위로 가 그 가지로 갔다 돌아온다
                    //   (현장 15:50 (210254.07, 509779.27): 0.1mm 세모 고리가 들어오는 선에 닿아 제 몸을 질렀다).
                    //   한 점 왕복(a, b, a)은 크기와 상관없이 가시다. 여러 점 고리는 모두 통로 폭 안일 때만 — 그보다 크면 진짜 모양이다
                    bool Tiny(List<(double X, double Y, int Z, bool Wn)> l, int from, int to, (double X, double Y, int Z, bool Wn) c)
                    {
                        for (int i = from; i < to; i++) if ((l[i].X - c.X) * (l[i].X - c.X) + (l[i].Y - c.Y) * (l[i].Y - c.Y) > Tube * Tube) return false;
                        return true;
                    }
                    var stk = new List<(double X, double Y, int Z, bool Wn)>();
                    foreach (var p in pts)
                    {
                        if (stk.Count > 0 && Same(stk[^1], p)) { if (p.Wn) stk[^1] = (stk[^1].X, stk[^1].Y, stk[^1].Z, true); continue; }
                        int hit = -1;
                        for (int j = stk.Count - 2; j >= Math.Max(0, stk.Count - 12); j--)
                            if (Same(stk[j], p) && (j == stk.Count - 2 || Tiny(stk, j + 1, stk.Count, p))) { hit = j; break; }
                        if (hit >= 0) { stk.RemoveRange(hit + 1, stk.Count - hit - 1); if (p.Wn) stk[^1] = (stk[^1].X, stk[^1].Y, stk[^1].Z, true); spikes++; continue; }
                        stk.Add(p);
                    }
                    for (bool ch = true; ch && stk.Count > 3;)
                    {
                        ch = false;
                        if (Same(stk[^1], stk[0])) { stk.RemoveAt(stk.Count - 1); ch = true; continue; }
                        for (int m = 2; m <= Math.Min(12, stk.Count - 3) && !ch; m++)
                            if (Same(stk[stk.Count - m], stk[0]) && (m == 2 || Tiny(stk, stk.Count - m, stk.Count, stk[0]))) { stk.RemoveRange(stk.Count - m, m); spikes++; ch = true; }
                        for (int m = 1; m <= Math.Min(11, stk.Count - 3) && !ch; m++)
                            if (Same(stk[m], stk[^1]) && (m == 1 || Tiny(stk, 0, m, stk[^1]))) { stk.RemoveRange(0, m); spikes++; ch = true; }
                    }
                    if (stk.Count < 3) return null;
                    var ring = new List<Point3>(stk.Count);
                    for (int i = 0; i < stk.Count; i++)
                    {
                        var a = stk[i]; var b = stk[(i + 1) % stk.Count];
                        ring.Add(new Point3(a.X, a.Y, ZOf(a.Z, a.X, a.Y)));
                        // 옹벽 높이를 쓰는 변(측량 경계 · 짧은 이음 · 곧은 변)은 옹벽 삼각형 변에서 꺾인다 — 그 교점을 넣는다(검토 낮음 10 · 낮음 3)
                        if ((a.Z == 3 && b.Z == 3) || a.Wn)
                            foreach (var (x, y) in WallCross(a.X, a.Y, b.X, b.Y)) { ring.Add(new Point3(x, y, ZOf(3, x, y))); wall++; }
                    }
                    // 가장 짧은 변 10µm(검토 중간 5) · 3D로 정확히 일직선(1µm)인 점만 뺀다 — 1mm로 정리하면 되살린 꺾임점이 다시 빠진다
                    var dd = new List<Point3>(ring.Count);
                    foreach (var q in ring)
                        if (dd.Count == 0 || Math.Sqrt((dd[^1].X - q.X) * (dd[^1].X - q.X) + (dd[^1].Y - q.Y) * (dd[^1].Y - q.Y)) > MinRingEdge) dd.Add(q);
                    while (dd.Count > 3 && Math.Sqrt((dd[0].X - dd[^1].X) * (dd[0].X - dd[^1].X) + (dd[0].Y - dd[^1].Y) * (dd[0].Y - dd[^1].Y)) <= MinRingEdge) dd.RemoveAt(dd.Count - 1);
                    var sm = Simplify3D(dd, 1e-6, out _);
                    if (DebugUnsnapAt != null)
                    {
                        for (int k = 0; k < n; k++) if (Dbg(cs[k].X, cs[k].Y)) tr.Append($"      [dbg] 판 {pass} 꼭짓점 #{k} → 들어옴 ({Vin[k].X + ox:F6},{Vin[k].Y + oy:F6}) 나감 ({Vout[k].X + ox:F6},{Vout[k].Y + oy:F6})" + (char)10);
                        for (int i = 0; i < sm.Count; i++) if (Dbg(sm[i].X, sm[i].Y)) tr.Append($"      [dbg] 판 {pass} 링 {i} ({sm[i].X + ox:F6},{sm[i].Y + oy:F6},{sm[i].Z:F4})" + (char)10);
                    }
                    if (RingOk(gfF, sm))
                    {
                        uMoved += moved; uCorner += corner; uCornerNear += cornerNear; uKept += kept; uNoPath += noPath; uBends += bends;
                        uCuts += cuts; uCutAt.AddRange(cutAt);
                        uSpikes += spikes; uWall += wall; uGuess += guess; uStraight += straight; uMaxMove = Math.Max(uMaxMove, maxMove);
                        foreach (bool b in keepSnap) if (b) uRepaired++;
                        return sm;
                    }
                    // 제 몸을 지른 자리 <b>모두</b>를 찾아, 그 5mm 안 꼭짓점과 <b>그 자리를 지나는 스냅 변의 양 끝</b>을 스냅 좌표로 두고 다시
                    //   (검토 v100.2 · 중간 2 — 교차가 긴 스냅 변 한가운데면 5mm 안에 꼭짓점이 없어 곧바로 포기했다)
                    var bad = new List<Coordinate>();
                    try
                    {
                        var cc = new Coordinate[sm.Count + 1];
                        for (int i = 0; i < sm.Count; i++) cc[i] = new Coordinate(sm[i].X, sm[i].Y);
                        cc[sm.Count] = cc[0].Copy();
                        var op = new NetTopologySuite.Operation.Valid.IsSimpleOp(gfF.CreateLineString(cc)) { FindAllLocations = true };
                        if (!op.IsSimple()) bad.AddRange(op.NonSimpleLocations);
                        else
                        {
                            var ve = new NetTopologySuite.Operation.Valid.IsValidOp(gfF.CreatePolygon(cc)).ValidationError;
                            if (ve?.Coordinate != null) bad.Add(ve.Coordinate);
                        }
                    }
                    catch { }
                    int marked = 0;
                    foreach (var bc in bad)
                    {
                        if (uRepairAt.Length < 300) uRepairAt.Append($" ({bc.X + ox:F3},{bc.Y + oy:F3})");
                        int kb = -1; double db = double.MaxValue;
                        for (int k = 0; k < n; k++)
                        {
                            if (!keepSnap[k] && cs[k].Distance(bc) <= 0.005) { keepSnap[k] = true; marked++; }
                            double dk = SegD(bc.X, bc.Y, cs[k].X, cs[k].Y, cs[k + 1].X, cs[k + 1].Y);
                            if (dk < db) { db = dk; kb = k; }
                        }
                        if (kb >= 0)
                        {
                            if (!keepSnap[kb]) { keepSnap[kb] = true; marked++; }
                            if (!keepSnap[(kb + 1) % n]) { keepSnap[(kb + 1) % n] = true; marked++; }
                        }
                    }
                    if (marked == 0) return null;
                }
                return null;
            }

            // 링마다 따로 — 못 되돌린 링만 스냅 링으로 둔다(검토 v100.2 · 중간 2 — 구멍 하나 때문에 바깥 링까지 버리지 않게)
            int ringsSnapped = 0;
            var ex2 = ExactRing(chosen.ExteriorRing);
            if (ex2 == null) { ex2 = extSnapL; ringsSnapped++; }
            var ho2 = new List<List<Point3>>();
            for (int h = 0; h < chosen.NumInteriorRings; h++)
            {
                var hr = ExactRing(chosen.GetInteriorRingN(h));
                if (hr == null) { hr = holesSnapL[h]; ringsSnapped++; }
                ho2.Add(hr);
            }
            bool okAll = ringsSnapped < 1 + chosen.NumInteriorRings;
            // 구멍과 바깥 링이 서로 겹치지 않는가(검토 낮음 9) — 겹치면 통째로 스냅 링으로
            if (okAll && ho2.Count > 0)
            {
                try
                {
                    LinearRing LR2(List<Point3> r0) { var cs = new Coordinate[r0.Count + 1]; for (int i = 0; i < r0.Count; i++) cs[i] = new Coordinate(r0[i].X, r0[i].Y); cs[r0.Count] = cs[0].Copy(); return gfF.CreateLinearRing(cs); }
                    okAll = gfF.CreatePolygon(LR2(ex2), ho2.ConvertAll(LR2).ToArray()).IsValid;
                }
                catch { okAll = false; }
            }
            // 정확한 링 ↔ 스냅 링 거리(검토 중간 6 · 낮음 2) — 통로 폭을 넘으면 되돌린 것이 틀린 것이다: 스냅 링으로
            double far = 0;
            if (okAll)
            {
                var sIdx = new IndexedFacetDistance(chosen.Boundary);
                foreach (var r0 in ho2.Prepend(ex2)) foreach (var q in r0) far = Math.Max(far, sIdx.Distance(gfF.CreatePoint(new Coordinate(q.X, q.Y))));
                if (far > Tube) okAll = false;
            }
            R.UnsMoved = uMoved; R.UnsCorner = uCorner; R.UnsCornerNear = uCornerNear; R.UnsKept = uKept; R.UnsNoPath = uNoPath;
            R.UnsBends = uBends; R.UnsSpikes = uSpikes; R.UnsRepaired = uRepaired; R.UnsFamGuess = uGuess; R.UnsWallCross = uWall; R.UnsMaxMove = uMaxMove;
            R.UnsStraight = uStraight; R.UnsRingsSnapped = ringsSnapped; R.UnsToSnapMax = far;
            R.UnsSideSwitch = sideSwitch; R.UnsSideNone = sideNone; R.UnsCuts = uCuts; R.UnsCutAt = uCutAt;
            if (okAll) { ext = ex2; holes = ho2; }
            else
            {
                R.UnsFallback = true;
                warns.Add("링을 정확한 교선 위로 <b>못 되돌렸다</b>(" + (ringsSnapped == 1 + chosen.NumInteriorRings ? "링마다 제 몸을 지름" : far > Tube ? $"스냅 링에서 {far * 1000:F2}mm — 통로 폭을 넘음" : "구멍과 겹침")
                    + ") — 1mm 격자 링을 그대로 쓴다(테두리가 초록 선에서 수 cm 흔들린다)");
            }
            tr.Append($"  ⑦-b 정확한 자리로 — " + (R.UnsFallback ? "<b>⚠못 되돌림(스냅 링 그대로)</b>"
                : $"꼭짓점 옮김 {R.UnsMoved}(최대 {R.UnsMaxMove * 1000:F3}mm) · 두 선 교점 {R.UnsCorner} · 교점 없어 두 선을 짧게 이음 {R.UnsCornerNear}"
                + $" · 되살린 꺾임점 {R.UnsBends} · 뺀 가시 {R.UnsSpikes}"
                + (R.UnsWallCross > 0 ? $" · 옹벽 높이 변의 삼각형 변 교점 {R.UnsWallCross}" : "")
                + $" · <b>스냅 그대로 {R.UnsKept}</b> · 선 못 찾은 곧은 변 {R.UnsStraight} · 길 없음 {R.UnsNoPath}{(R.UnsNoPath > 0 ? $"({uNoPathAt})" : "")}"
                + $" · 이웃 조각으로 못 정해 거리로 짐작 {R.UnsFamGuess}{(R.UnsFamGuess > 0 ? $"({uGuessAt})" : "")}"
                + $" · 양옆 높이로 선을 바꾼 변 {R.UnsSideSwitch}" + (R.UnsSideNone > 0 ? $" · <b>양옆이 맞는 선이 없는 변 {R.UnsSideNone}</b>" : "")
                + (R.UnsCuts > 0 ? $" · 격자보다 얇아 이음선으로 닫은 자리 {R.UnsCuts}({string.Join(" ", R.UnsCutAt.Take(4).Select(c => $"({c.X:F3},{c.Y:F3})"))})" : "")
                + (R.UnsRepaired > 0 ? $" · 제 몸 지름 고치려 스냅으로 둔 꼭짓점 {R.UnsRepaired}(자리{uRepairAt})" : "")
                + (R.UnsRingsSnapped > 0 ? $" · <b>스냅 링으로 둔 링 {R.UnsRingsSnapped}</b>" : "")
                + $" → 스냅 링과 거리 최대 {R.UnsToSnapMax * 1000:F3}mm") + "\n");
            if (!R.UnsFallback && (R.UnsKept > 0 || R.UnsNoPath > 0 || R.UnsStraight > 0 || R.UnsRingsSnapped > 0))
                warns.Add($"정확한 교선을 못 찾은 자리 — 스냅 그대로 {R.UnsKept}점 · 곧은 변 {R.UnsStraight} · 길 없음 {R.UnsNoPath}변 · 스냅 링으로 둔 링 {R.UnsRingsSnapped}(그 자리 테두리가 초록 선에서 흔들릴 수 있다 — ⑦-c가 잰다)");
        }
        catch (Exception uex)
        {
            // ★[검토 v100.2 · 낮음 1] 되돌리다 예상 못 한 예외 — 띠를 잃지 않고 스냅 링으로(v100.1과 같게)
            ext = extSnapL; holes = holesSnapL; R.UnsFallback = true;
            warns.Add($"링을 정확한 자리로 되돌리다 예외 {uex.GetType().Name}: {uex.Message} — 1mm 격자 링을 그대로 쓴다");
            tr.Append($"  ⑦-b 정확한 자리로 — <b>⚠예외 {uex.GetType().Name}</b>(스냅 링 그대로)" + "\n");
        }
        else R.UnsFallback = true;
        R.FinalPts = ext.Count;
        var outer = PreparedGeometryFactory.Prepare(polyG.Buffer(SampleClear));
        foreach (var q in ext) if (!outer.Contains(gfF.CreatePoint(new Coordinate(q.X, q.Y)))) R.OutsidePoly++;
        if (R.OutsidePoly > 0) warns.Add($"폴리곤 밖 점 {R.OutsidePoly}개");

        // ⑦-c ★자가검증 — Civil이 테두리에 줄 높이(옹벽 TIN) − 링 높이. 링 꼭짓점 + 링 변 × 옹벽 삼각형 변 교점(Civil 비파괴 경계가 넣는 점)
        {
            var gaps = new List<double>(); double worst = -1;
            var seenX = new HashSet<(long, long)>();     // 두 삼각형이 나눠 가진 변의 교점을 두 번 세지 않게(검토 낮음 3)
            void AddGap(double x, double y, double zr, int tri)
            {
                double zw = W.Z(tri, x, y), g = Math.Abs(zw - zr);
                gaps.Add(g);
                if (g > 0.01) R.EdgeGapOver1cm++;
                if (g > worst)
                {
                    worst = g; double grad = Math.Sqrt(W.A[tri] * W.A[tri] + W.B[tri] * W.B[tri]);
                    R.EdgeGapAt = $"({x + ox:F3},{y + oy:F3}) 옹벽 {Hz(zw):F3} · 선 {Hz(zr):F3} · 면 {(grad < 1e-9 ? "평평" : $"1:{1 / grad:0.####}")}";
                }
            }
            foreach (var r0 in holes.Prepend(ext))
                for (int k = 0; k < r0.Count; k++)
                {
                    var p = r0[k]; var q = r0[(k + 1) % r0.Count];
                    int tp = W.At(p.X, p.Y);
                    if (tp >= 0) AddGap(p.X, p.Y, p.Z, tp); else R.EdgeGapNoWall++;
                    double rx = q.X - p.X, ry = q.Y - p.Y;
                    foreach (int i in W.Tree.Query(new Envelope(p.X, q.X, p.Y, q.Y)))
                    {
                        (double X, double Y)[] E = { (W.X1[i], W.Y1[i]), (W.X2[i], W.Y2[i]), (W.X3[i], W.Y3[i]) };
                        for (int e = 0; e < 3; e++)
                        {
                            var m = E[e]; var m2 = E[(e + 1) % 3];
                            double sx = m2.X - m.X, sy = m2.Y - m.Y, den = rx * sy - ry * sx;
                            if (Math.Abs(den) < 1e-18) continue;
                            double t = ((m.X - p.X) * sy - (m.Y - p.Y) * sx) / den, u = ((m.X - p.X) * ry - (m.Y - p.Y) * rx) / den;
                            if (t <= 1e-9 || t >= 1 - 1e-9 || u < 0 || u > 1) continue;
                            double cx = p.X + rx * t, cy = p.Y + ry * t;
                            if (!seenX.Add(((long)Math.Round(cx * 1e8), (long)Math.Round(cy * 1e8)))) continue;
                            AddGap(cx, cy, p.Z + (q.Z - p.Z) * t, i);
                        }
                    }
                }
            if (gaps.Count > 0)
            {
                gaps.Sort();
                R.EdgeGapN = gaps.Count; R.EdgeGapMax = gaps[^1]; R.EdgeGapMed = gaps[gaps.Count / 2]; R.EdgeGap99 = gaps[Math.Min(gaps.Count - 1, (int)(gaps.Count * 0.99))];
            }
            tr.Append($"  ⑦-c 자가검증 — 테두리 높이(옹벽 TIN) − 띠 선 높이: {R.EdgeGapN}점 · 중앙 {R.EdgeGapMed * 1000:F3}mm · 99% {R.EdgeGap99 * 1000:F3}mm"
                + $" · <b>최대 {R.EdgeGapMax * 1000:F3}mm</b> @{R.EdgeGapAt}" + (R.EdgeGapNoWall > 0 ? $" · 옹벽 밖 꼭짓점 {R.EdgeGapNoWall}" : "")
                + (R.EdgeGapOver1cm > 0 ? $" · ⚠1cm 넘는 점 {R.EdgeGapOver1cm}" : "") + "\n");
            // ★[검토 v100.2 · 중간 4] 1cm 넘으면 ⚠ — 면은 짓는다(자리는 맞고 테두리 높이만 흔들린다). 어디서·어느 기울기 면인지 적는다
            if (R.EdgeGapMax > 0.01)
                warns.Add($"테두리 높이가 띠 선에서 최대 {R.EdgeGapMax * 100:F1}cm 벗어난다(1cm 넘는 점 {R.EdgeGapOver1cm}) @{R.EdgeGapAt}");
        }

        // ⑧ 독립 검사 — 0.5m 격자에서 「남길 곳인가」(정지면 < 옹벽 < 원지반)와 「띠 안인가」를 대조.
        //   |옹벽−원지반| 또는 |옹벽−정지면|이 ZeroD 이하인 점은 건너뛴다(검토 높음 1 — 같은 평면 자리)
        {
            var kPrep = PreparedGeometryFactory.Prepare(keepU);
            var kB = new IndexedFacetDistance(keepU.Boundary);
            var inner = PreparedGeometryFactory.Prepare(polyG.Buffer(-SampleClear));
            double worst = -1; string worstAt = "";
            for (double x = polyEnv.MinX + GridStep * 0.5; x < polyEnv.MaxX; x += GridStep)
                for (double y = polyEnv.MinY + GridStep * 0.5; y < polyEnv.MaxY; y += GridStep)
                {
                    var pt = gfF.CreatePoint(new Coordinate(x, y));
                    if (!inner.Contains(pt)) continue;
                    if (!W.TryZ(x, y, out double zw) || !G.TryZ(x, y, out double zgv) || !P.TryZ(x, y, out double zpv)) continue;
                    double dg = zw - zgv, dp = zw - zpv;
                    if (Math.Abs(dg) <= ZeroD || Math.Abs(dp) <= ZeroD) continue;
                    double db = kB.Distance(pt);
                    if (db < SampleClear) continue;
                    R.GridChecked++;
                    bool want = dp > 0 && dg < 0;
                    if (want != kPrep.Contains(pt))
                    {
                        R.GridMismatch++;
                        if (db > worst) { worst = db; worstAt = $"({x + ox:F2},{y + oy:F2}) 옹벽−원지반 {dg:+0.000;-0.000} · 옹벽−정지면 {dp:+0.000;-0.000}"; }
                    }
                }
            tr.Append($"  ⑧ 독립 검사(0.5m 격자) — {R.GridChecked}점 중 어긋남 <b>{R.GridMismatch}</b>" + (R.GridMismatch > 0 ? $" · 최악 {worstAt}" : "") + "\n");
            if (R.GridMismatch > 0) warns.Add($"격자 검사에서 어긋난 점 {R.GridMismatch}개(최악 {worstAt})");
        }

        // 세계 좌표로
        List<Point3> ToWorld(List<Point3> r) { var w = new List<Point3>(r.Count); foreach (var q in r) w.Add(new Point3(q.X + ox, q.Y + oy, q.Z)); return w; }
        R.Ring = ToWorld(ext);
        foreach (var h in holes) R.Holes.Add(ToWorld(h));
        R.Warn = warns.Count > 0;
        R.Ms = sw.ElapsedMilliseconds;
        R.Summary = $"남길 띠 {(R.Broken ? "⚠" : R.Warn ? "⚠" : "✔")} 닫힌 3D폴리선 1개 · {R.FinalPts}점 · 넓이 {R.Area:F2}㎡"
                  + (holes.Count > 0 ? $" · 구멍 {holes.Count}개(Hide)" : "")
                  + (R.Pieces > 1 ? $" · 버린 조각 {R.Pieces - 1}개({R.DroppedArea:F2}㎡)" : "")
                  + $" · 테두리 높이 차 최대 {R.EdgeGapMax * 1000:F1}mm" + (R.UnsFallback ? "(⚠1mm 격자 링)" : "");
        tr.Append($"  ⑨ 자가검증 — 닫힘 예 · 단순 {(R.Simple ? "예" : "<b>아니오</b>")} · 폴리곤 밖 점 {R.OutsidePoly}"
            + $" · 끊긴 끝 {R.Dangles}{(R.Spurs > 0 ? $"(+곁가지 {R.Spurs})" : "")} · 섞인 조각 {R.Mixed} · 격자 어긋남 {R.GridMismatch}"
            + $" · 1cm 안 갈라진 조각 {R.NearPieces}{(R.Fragments > 0 ? $"(+부스러기 {R.Fragments})" : "")} · {R.Ms}ms" + (warns.Count == 0 ? " → ✔\n" : $" → <b>⚠{warns.Count}건</b>\n"));
        foreach (var w in warns) tr.Append($"    ⚠{w}\n");
        R.Trace = tr.ToString();
        return R;
    }

    /// <summary>★하네스 전용 — 옹벽과 상대면의 <b>날것</b> 교선 선분(세계 좌표, 군집·스냅 전).
    /// 링 점이 진짜 교선 위인지 <b>따로</b> 재는 데 쓴다(반경 5mm 부호 검사는 절벽에서 높이 50cm라 약하다 — 검토 0918).</summary>
    public static List<(Point3 A, Point3 B)> ZeroSegmentsWorld(IReadOnlyList<Point3> poly, IReadOnlyList<Tri> wallTris, IReadOnlyList<Tri> otherTris,
        double level = ZeroD)
    {
        double ox = poly[0].X, oy = poly[0].Y;
        var zs = ZeroLines(new TriSet(wallTris, ox, oy), new TriSet(otherTris, ox, oy), new GeometryFactory(), ox, oy, null, level);
        var res = new List<(Point3, Point3)>(zs.Segs.Count);
        foreach (var s in zs.Segs) res.Add((new Point3(s.X1 + ox, s.Y1 + oy, 0), new Point3(s.X2 + ox, s.Y2 + oy, 0)));
        return res;
    }

    /// <summary>띠 재생 파일 — 폴리곤·옹벽·원지반·정지면 <b>한 파일</b>(두 파일을 짝지으면 어긋날 수 있다 — 검토 0918).</summary>
    public static void WriteBandInput(string path, IReadOnlyList<Point3> poly, IReadOnlyList<Tri> wall,
        IReadOnlyList<Tri> ground, IReadOnlyList<Tri> plan, string meta)
    {
        WriteInput(path, poly, wall, ground, meta);
        var ci = CultureInfo.InvariantCulture;
        using var w = new StreamWriter(path, true, new UTF8Encoding(false));
        w.WriteLine($"PLAN {plan.Count}");
        foreach (var t in plan)
            w.WriteLine(string.Format(ci, "{0:R} {1:R} {2:R} {3:R} {4:R} {5:R} {6:R} {7:R} {8:R}",
                t.A.X, t.A.Y, t.A.Z, t.B.X, t.B.Y, t.B.Z, t.C.X, t.C.Y, t.C.Z));
    }

    /// <summary>★★[v102.0 · 계획 검토 M4·L6] 입력 파일 <b>끝</b>에 덧붙이는 줄 — 방향(절토|성토) · 옹벽선 · 머리(z0 단높이 소단 단수 버린 줄).
    /// <para>옛 파일엔 없다. 읽는 쪽(<see cref="TryReadInput"/>·<see cref="TryReadBandInput"/>)은 앞 블록만 읽으므로 그대로 받는다.
    /// 재생이 방향을 모르고 성토 덤프를 절토로 돌리던 것(S139 ⑨ — 0929 15:39)을 막는다.</para></summary>
    public sealed record InputTail(string Side, List<Point3> WallLine, double[] Head);

    public static void AppendInputTail(string path, string side, IReadOnlyList<Point3>? wallLine, double[]? head)
    {
        var ci = CultureInfo.InvariantCulture;
        using var w = new StreamWriter(path, true, new UTF8Encoding(false));
        if (!string.IsNullOrEmpty(side)) w.WriteLine("SIDE " + side);
        if (wallLine != null && wallLine.Count > 0)
        {
            w.WriteLine($"WALLLINE {wallLine.Count}");
            foreach (var q in wallLine) w.WriteLine(string.Format(ci, "{0:R} {1:R} {2:R}", q.X, q.Y, q.Z));
        }
        if (head != null && head.Length > 0) w.WriteLine("HEAD " + string.Join(" ", head.Select(v => v.ToString("R", ci))));
    }

    public static InputTail ReadInputTail(string path)
    {
        string side = ""; var wl = new List<Point3>(); double[] head = Array.Empty<double>();
        if (!File.Exists(path)) return new(side, wl, head);
        var ci = CultureInfo.InvariantCulture;
        var lines = File.ReadAllLines(path);
        try
        {
            for (int k = 0; k < lines.Length; k++)
            {
                string l = lines[k];
                if (l.StartsWith("SIDE ")) side = l.Substring(5).Trim();
                else if (l.StartsWith("HEAD ")) head = l.Substring(5).Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(v => double.Parse(v, ci)).ToArray();
                else if (l.StartsWith("WALLLINE "))
                {
                    int n = int.Parse(l.Substring(9), ci);
                    for (int i = 1; i <= n && k + i < lines.Length; i++)
                    {
                        var v = lines[k + i].Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        wl.Add(new Point3(double.Parse(v[0], ci), double.Parse(v[1], ci), double.Parse(v[2], ci)));
                    }
                    k += n;
                }
            }
        }
        catch { }
        return new(side, wl, head);
    }

    public static bool TryReadBandInput(string path, out List<Point3> poly, out List<Tri> wall,
        out List<Tri> ground, out List<Tri> plan, out string meta)
    {
        plan = new();
        if (!TryReadInput(path, out poly, out wall, out ground, out meta)) return false;
        var ci = CultureInfo.InvariantCulture;
        var lines = File.ReadAllLines(path);
        int k = Array.FindIndex(lines, l => l.StartsWith("PLAN "));
        if (k < 0) return false;
        try
        {
            int n = int.Parse(lines[k].Substring(5), ci);
            for (int i = 1; i <= n && k + i < lines.Length; i++)
            {
                var v = Array.ConvertAll(lines[k + i].Split(' ', StringSplitOptions.RemoveEmptyEntries), x => double.Parse(x, ci));
                if (v.Length < 9) return false;
                plan.Add(new Tri(new Point3(v[0], v[1], v[2]), new Point3(v[3], v[4], v[5]), new Point3(v[6], v[7], v[8])));
            }
            return plan.Count > 0 && plan.Count == n;   // ★[검토 v100.0 · 낮음 5] 줄 수가 모자라면(끊긴 파일) 못 읽은 것이다
        }
        catch { return false; }
    }

    // ───────────────────────────── 공용 조각(데이라잇 · 남길 띠) ─────────────────────────────

    private sealed class ZeroSet
    {
        public List<(double X1, double Y1, double X2, double Y2)> Segs = new();
        public List<Geometry> Lines = new();
        public int Pairs, ZSkip, OverlayFail, Segments;
        public string FailAt = "";
    }

    /// <summary>옹벽 W와 상대면 G의 교선 조각 — 부동소수 겹침 → 0선 선분 → 1e-6 군집 → 선.
    /// <para><see cref="Build"/>와 <see cref="KeepBand"/>가 <b>같은 코드</b>를 쓴다.</para></summary>
    /// <param name="level">높이차 D = 옹벽 − 상대면이 이 값이 되는 선을 긋는다. 데이라잇은 +ZeroD 한 벌,
    /// 남길 띠는 <b>+ZeroD와 −ZeroD 두 벌</b>(포개진 평평한 자리를 따로 떼려고 — JACK 0918 «자글자글»).</param>
    private static ZeroSet ZeroLines(TriSet W, TriSet G, GeometryFactory gfF, double ox, double oy,
        Func<double, double, bool>? dropSegAt, double level = ZeroD)
    {
        var z = new ZeroSet();
        var segs = z.Segs;
        for (int i = 0; i < W.N; i++)
        {
            if (!W.Ok[i]) continue;
            Polygon? pa = null;
            foreach (int j in G.Tree.Query(W.Env(i)))
            {
                z.Pairs++;
                // 높이가 안 겹치면 그 겹침 안에서 D의 부호가 하나다 — 0선이 없다
                // D의 범위가 level을 안 품으면 그 겹침 안에 level 선이 없다(종전 «0을 안 품으면»보다 정확 — ZeroD 틈까지 맞춘다)
                if (W.MinZ[i] - G.MaxZ[j] > level || W.MaxZ[i] - G.MinZ[j] < level) { z.ZSkip++; continue; }
                pa ??= W.Poly(i, gfF);
                var pb = G.Poly(j, gfF);
                Geometry ov;
                try { ov = pa.Intersection(pb); }
                catch
                {
                    try { ov = OverlayNGRobust.Overlay(pa, pb, NetTopologySuite.Operation.Overlay.SpatialFunction.Intersection); }
                    catch { z.OverlayFail++; if (z.FailAt.Length == 0) z.FailAt = $"{W.X1[i] + ox:F2},{W.Y1[i] + oy:F2}"; continue; }
                }
                if (ov.IsEmpty) continue;
                CollectCross(ov, W, i, G, j, segs, level);
            }
        }
        z.Segments = segs.Count;
        if (dropSegAt != null)
            segs.RemoveAll(s => dropSegAt((s.X1 + s.X2) * 0.5 + ox, (s.Y1 + s.Y2) * 0.5 + oy));

        // 교점 군집 — 이웃 쌍이 따로 구한 같은 점을 한 점으로(반경은 ClusterTol 주석)
        var snapGrid = new Dictionary<(long, long), List<(double X, double Y)>>();
        (double, double) Snap(double x, double y)
        {
            long kx = (long)Math.Floor(x / ClusterTol), ky = (long)Math.Floor(y / ClusterTol);
            for (long dx = -1; dx <= 1; dx++)
                for (long dy = -1; dy <= 1; dy++)
                    if (snapGrid.TryGetValue((kx + dx, ky + dy), out var lst))
                        foreach (var e in lst)
                            if ((e.X - x) * (e.X - x) + (e.Y - y) * (e.Y - y) <= ClusterTol * ClusterTol) return e;
            if (!snapGrid.TryGetValue((kx, ky), out var own)) snapGrid[(kx, ky)] = own = new List<(double, double)>();
            own.Add((x, y));
            return (x, y);
        }
        foreach (var s in segs)
        {
            var (ax, ay) = Snap(s.X1, s.Y1);
            var (bx, by) = Snap(s.X2, s.Y2);
            if ((ax - bx) * (ax - bx) + (ay - by) * (ay - by) < 1e-18) continue;
            z.Lines.Add(gfF.CreateLineString(new[] { new Coordinate(ax, ay), new Coordinate(bx, by) }));
        }
        return z;
    }

    /// <summary>측량 경계 변들을 폴리곤으로 자른 <b>선</b>(점은 버린다). 없으면 빈 모음.</summary>
    private static Geometry HullClipped(List<(double X1, double Y1, double X2, double Y2)> hull, Polygon polyG,
        GeometryFactory gfF, List<string> warns)
    {
        Geometry hullClipped = gfF.CreateGeometryCollection();
        if (hull.Count == 0) return hullClipped;
        var hl = new LineString[hull.Count];
        for (int k = 0; k < hull.Count; k++)
            hl[k] = gfF.CreateLineString(new[] { new Coordinate(hull[k].X1, hull[k].Y1), new Coordinate(hull[k].X2, hull[k].Y2) });
        try
        {
            // 잘린 결과에 <b>점</b>(변이 테두리에 한 점으로 닿은 자리)이 섞일 수 있다 — 선만 쓴다
            var cut = gfF.CreateMultiLineString(hl).Intersection(polyG);
            var ls = new List<LineString>();
            foreach (var g in NetTopologySuite.Geometries.Utilities.LinearComponentExtracter.GetLines(cut))
                if (g is LineString l && l.Length > 0) ls.Add(l);
            if (ls.Count > 0) hullClipped = gfF.CreateMultiLineString(ls.ToArray());
        }
        catch { warns.Add("측량 경계를 폴리곤으로 못 잘랐다"); }
        return hullClipped;
    }

    /// <summary>조각 안의 표본 — 테두리에서 <see cref="SampleClear"/>(2mm) 넘게 떨어진 점만.
    /// 하나도 없으면(2mm도 안 되는 띠) 안쪽 점 하나로 대신하고 <paramref name="thin"/>을 세운다.</summary>
    private static List<(double X, double Y)> FaceSamples(Polygon f, Point ip, GeometryFactory gfF, out bool thin)
    {
        var fPrep = PreparedGeometryFactory.Prepare(f);
        var fb = new IndexedFacetDistance(f.Boundary);
        var S = new List<(double X, double Y)>();
        void TryAdd(double x, double y)
        {
            var pt = gfF.CreatePoint(new Coordinate(x, y));
            if (!fPrep.Contains(pt) || fb.Distance(pt) < SampleClear) return;
            S.Add((x, y));
        }
        TryAdd(ip.X, ip.Y);
        var env = f.EnvelopeInternal;
        foreach (int ng in new[] { 5, 17, 51 })
        {
            if (S.Count >= 9) break;
            for (int gx = 0; gx < ng; gx++)
                for (int gy = 0; gy < ng; gy++)
                    TryAdd(env.MinX + env.Width * (gx + 0.5) / ng, env.MinY + env.Height * (gy + 0.5) / ng);
        }
        thin = S.Count == 0;
        if (thin) S.Add((ip.X, ip.Y));
        return S;
    }

    // ───────────────────────────── 조각들 ─────────────────────────────

    /// <summary>겹침(볼록) 안에서 D = 옹벽 − 원지반 = <see cref="ZeroD"/>인 선분.
    /// <para>「옹벽이 땅보다 <b>높은</b> 곳」(D &gt; ZeroD)이 섬이므로 |D| ≤ ZeroD(일치)는 늘 섬 밖이다.
    /// ★[검토 0918 · 낮음 1] 표본·격자 검사와 <b>같은 문턱</b>으로 가른다 — 종전엔 여기만 정확히 0으로 갈라
    /// 옹벽 면과 땅이 같은 평면인 자리에서 부동소수 잡음이 가짜 교선을 낼 수 있었다.</para></summary>
    private static void CollectCross(Geometry ov, TriSet w, int i, TriSet g, int j,
        List<(double, double, double, double)> segs, double level = ZeroD)
    {
        if (ov is Polygon p)
        {
            var cs = p.ExteriorRing.Coordinates;
            if (cs.Length < 4) return;
            var cross = new List<(double X, double Y)>(4);
            for (int k = 0; k < cs.Length - 1; k++)
            {
                double xi = cs[k].X, yi = cs[k].Y, xj = cs[k + 1].X, yj = cs[k + 1].Y;
                double di = w.Z(i, xi, yi) - g.Z(j, xi, yi);
                double dj = w.Z(i, xj, yj) - g.Z(j, xj, yj);
                if ((di > level) == (dj > level)) continue;
                double t = (di - level) / (di - dj);
                cross.Add((xi + t * (xj - xi), yi + t * (yj - yi)));
            }
            if (cross.Count < 2) return;
            if (cross.Count > 2)
            {
                // 선형 필드의 0선 ∩ 볼록 = 선분 하나 — 방향으로 줄 세워 양 끝을 쓴다(:1087과 같은 판정)
                double ux = -(w.B[i] - g.B[j]), uy = w.A[i] - g.A[j];
                cross.Sort((a, b) => (a.X * ux + a.Y * uy).CompareTo(b.X * ux + b.Y * uy));
            }
            var c0 = cross[0]; var c1 = cross[^1];
            if ((c0.X - c1.X) * (c0.X - c1.X) + (c0.Y - c1.Y) * (c0.Y - c1.Y) > 1e-18)
                segs.Add((c0.X, c0.Y, c1.X, c1.Y));
        }
        else if (ov is GeometryCollection gc)
            foreach (var sub in gc.Geometries) CollectCross(sub, w, i, g, j, segs, level);
    }

    /// <summary>측량 경계(한 삼각형에만 속한 변) 중 폴리곤 상자에 걸치는 것.
    /// <para>후보는 호출 쪽이 상자+여유로 거른 것이다. 상자에 걸치는 변은 이웃 삼각형도 반드시 후보에 들므로
    /// (그 이웃의 상자도 그 변을 품는다), 「한 번만 나온 변」이 곧 진짜 측량 경계다.</para></summary>
    private static List<(double X1, double Y1, double X2, double Y2)> HullEdgesLocal(TriSet g, Envelope box)
    {
        var cnt = new Dictionary<((long, long), (long, long)), (double X1, double Y1, double X2, double Y2, int N)>();
        static (long, long) K(double x, double y) => ((long)Math.Round(x * 1000.0), (long)Math.Round(y * 1000.0));
        void Add(double x1, double y1, double x2, double y2)
        {
            var a = K(x1, y1); var b = K(x2, y2);
            if (a.Equals(b)) return;
            var key = a.CompareTo(b) <= 0 ? (a, b) : (b, a);
            cnt[key] = cnt.TryGetValue(key, out var v) ? (v.X1, v.Y1, v.X2, v.Y2, v.N + 1) : (x1, y1, x2, y2, 1);
        }
        for (int i = 0; i < g.N; i++)
        {
            Add(g.X1[i], g.Y1[i], g.X2[i], g.Y2[i]);
            Add(g.X2[i], g.Y2[i], g.X3[i], g.Y3[i]);
            Add(g.X3[i], g.Y3[i], g.X1[i], g.Y1[i]);
        }
        var res = new List<(double, double, double, double)>();
        foreach (var v in cnt.Values)
        {
            if (v.N != 1) continue;
            var e = new Envelope(v.X1, v.X2, v.Y1, v.Y2);
            if (e.Intersects(box)) res.Add((v.X1, v.Y1, v.X2, v.Y2));
        }
        return res;
    }

    /// <summary>선분 a→b가 원지반 삼각형 변을 지나는 자리들(a·b 제외, a에서 가까운 순).</summary>
    private static List<(double X, double Y)> GroundEdgeCrossings(TriSet g, double ax, double ay, double bx, double by)
    {
        var ts = new List<double>();
        double L = Math.Sqrt((bx - ax) * (bx - ax) + (by - ay) * (by - ay));
        if (L < DupTol) return new List<(double, double)>();
        var env = new Envelope(ax, bx, ay, by);
        foreach (int i in g.Tree.Query(env))
        {
            void Hit(double px, double py, double qx, double qy)
            {
                double rx = bx - ax, ry = by - ay, sx = qx - px, sy = qy - py;
                double den = rx * sy - ry * sx;
                if (Math.Abs(den) < 1e-15) return;
                double t = ((px - ax) * sy - (py - ay) * sx) / den;
                double u = ((px - ax) * ry - (py - ay) * rx) / den;
                if (t > 1e-9 && t < 1 - 1e-9 && u >= -1e-9 && u <= 1 + 1e-9) ts.Add(t);
            }
            Hit(g.X1[i], g.Y1[i], g.X2[i], g.Y2[i]);
            Hit(g.X2[i], g.Y2[i], g.X3[i], g.Y3[i]);
            Hit(g.X3[i], g.Y3[i], g.X1[i], g.Y1[i]);
        }
        ts.Sort();
        var res = new List<(double, double)>();
        double last = 0;
        foreach (var t in ts)
        {
            if ((t - last) * L < DupTol || (1 - t) * L < DupTol) continue;
            res.Add((ax + (bx - ax) * t, ay + (by - ay) * t));
            last = t;
        }
        return res;
    }

    private static List<Point3> Dedup(List<Point3> pts, out int removed)
    {
        var r = new List<Point3>(pts.Count);
        foreach (var q in pts)
            if (r.Count == 0 || D3(r[^1], q) > DupTol) r.Add(q);
        while (r.Count > 3 && D3(r[0], r[^1]) <= DupTol) r.RemoveAt(r.Count - 1);
        removed = pts.Count - r.Count;
        return r;
    }

    private static double D3(Point3 a, Point3 b)
        => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y) + (a.Z - b.Z) * (a.Z - b.Z));

    /// <summary>점에서 3D 선분까지 거리.</summary>
    private static double PtSeg3(Point3 p, Point3 a, Point3 b)
    {
        double ex = b.X - a.X, ey = b.Y - a.Y, ez = b.Z - a.Z, L2 = ex * ex + ey * ey + ez * ez;
        double t = L2 < 1e-24 ? 0 : Math.Max(0, Math.Min(1, ((p.X - a.X) * ex + (p.Y - a.Y) * ey + (p.Z - a.Z) * ez) / L2));
        double dx = p.X - (a.X + ex * t), dy = p.Y - (a.Y + ey * t), dz = p.Z - (a.Z + ez * t);
        return Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    /// <summary>★닫힌 링에서 <b>3D로</b> 일직선인 점을 뺀다 — 뺀 점은 <b>원래 점 전부</b>가 새 변에서
    /// <paramref name="tol"/> 안이어야 한다(차례로 빼며 오차가 쌓이지 않게).
    /// 가장 많이 꺾인 점에서 시작한다 — 그 점은 어차피 남으므로 시작점이 아무 데나 박히지 않는다.</summary>
    public static List<Point3> Simplify3D(IReadOnlyList<Point3> ring, double tol, out double maxDev)
    {
        maxDev = 0;
        int n = ring.Count;
        if (n <= 4) return new List<Point3>(ring);
        int s0 = 0; double bestTurn = -1;
        for (int i = 0; i < n; i++)
        {
            var a = ring[(i - 1 + n) % n]; var b = ring[i]; var c = ring[(i + 1) % n];
            double d = PtSeg3(b, a, c);
            if (d > bestTurn) { bestTurn = d; s0 = i; }
        }
        var R = new List<Point3>();
        int anchor = 0;                                  // s0에서 센 자리
        R.Add(ring[s0]);
        while (anchor < n)
        {
            int best = anchor + 1;
            double bestDev = 0;
            for (int j = anchor + 2; j <= n; j++)       // j = n 이면 시작점으로 돌아온 것
            {
                var A = ring[(s0 + anchor) % n]; var B = ring[(s0 + j) % n];
                double dev = 0; bool ok = true;
                for (int k = anchor + 1; k < j; k++)
                {
                    double d = PtSeg3(ring[(s0 + k) % n], A, B);
                    if (d > tol) { ok = false; break; }
                    if (d > dev) dev = d;
                }
                if (!ok) break;
                best = j; bestDev = dev;
            }
            if (bestDev > maxDev) maxDev = bestDev;
            anchor = best;
            if (anchor < n) R.Add(ring[(s0 + anchor) % n]);
        }
        return R.Count >= 3 ? R : new List<Point3>(ring);
    }

    /// <summary>★★★[v100.3 · JACK 0918 «초록색선에 맞춰서 정확히 잘리지 않으면 이 기능은 의미가 없어 무조건 성공해야해»]
    /// 옹벽 삼각형을 <b>띠(초록 선 · 구멍 빼고)로 직접 잘라</b> 순수 옹벽 삼각형을 만든다.
    /// <para>Civil 비파괴 경계에 자르기를 맡기면 Civil이 어떻게 자르는지(삼각망 변과 포갠 경계 등)에 달린다 — 17:32엔 관문에 걸려
    /// 1mm 격자 링(톱니 60mm)으로 되돌아갔다. 여기서 자른 삼각형을 LandXML 면(Faces)으로 넘기면 Civil은 <b>자르지 않고 받기만</b> 한다:
    /// 테두리 = 초록 선. 삼각형마다 원래 옹벽 평면 그대로(높이 = 그 평면 — 초록 선 점에서 원지반/정지면 높이와 0.1mm 안).</para></summary>
    /// <param name="note">잘린 삼각형 수 · 통째로 든 수 · 넓이 대조.</param>
    /// <param name="bad">띠 안인데 삼각형으로 못 덮은 조각 수(옹벽 밖 · 삼각분할 실패 · 삼각형 넓이 합 ≠ 조각 넓이) — 0이 아니면 이 길을 쓰지 않는다(검토 v100.3 · 중간 2·5).</param>
    public static List<Tri> ClipToBand(IReadOnlyList<Tri> wallTris, IReadOnlyList<Point3> ring, IReadOnlyList<List<Point3>> holes, out string note, out int bad)
    {
        bad = 0;
        // ★[하네스 ⑮가 잡음] 삼각형마다 따로 자르면 링이 삼각망 변과 포갠 자리에서 0.1µm도 안 되는 가는 조각이 나고, 이웃 조각과 꼭짓점이
        //   어긋나(변 하나를 네 삼각형이 씀) LandXML 면이 맞물리지 않았다. → 옹벽 삼각형 변 전부 + 초록 선을 <b>한 번에</b> 1µm 격자로 잇고
        //   (띠 계산 ③과 같은 방법 — 스냅라운딩은 점마다 같은 격자점으로 가므로 이웃 조각이 꼭짓점을 정확히 나눠 갖는다)
        //   조각을 나눈 뒤, 띠 안 조각만 삼각형으로 쪼개고 높이는 그 자리 옹벽 평면으로. 1µm는 1:0.01 면에서 높이 0.1mm 이하다.
        var res = new List<Tri>();
        note = "";
        if (ring == null || ring.Count < 3 || wallTris == null || wallTris.Count == 0) { note = "링이나 옹벽 삼각형이 없다"; bad = 1; return res; }
        double ox = ring[0].X, oy = ring[0].Y;
        var gf = new GeometryFactory();
        var pm = new PrecisionModel(1e6);                  // 1µm
        LinearRing LR(IReadOnlyList<Point3> r0)
        {
            var cs = new Coordinate[r0.Count + 1];
            for (int i = 0; i < r0.Count; i++) cs[i] = new Coordinate(r0[i].X - ox, r0[i].Y - oy);
            cs[r0.Count] = cs[0].Copy();
            return gf.CreateLinearRing(cs);
        }
        var band = gf.CreatePolygon(LR(ring), holes.Select(h => LR(h)).ToArray());
        var env = band.EnvelopeInternal.Copy(); env.ExpandBy(0.01);
        var W = new TriSet(wallTris, ox, oy);
        // 옹벽 삼각형 변(겹친 것은 하나로) + 초록 선(바깥·구멍)
        var lines = new List<Geometry>();
        var seen = new HashSet<((long, long), (long, long))>();
        static (long, long) Q(double x, double y) => ((long)Math.Round(x * 1e9), (long)Math.Round(y * 1e9));
        int nT = 0;
        for (int t = 0; t < W.N; t++)
        {
            if (!W.Ok[t] || !W.Env(t).Intersects(env)) continue;
            nT++;
            var P3 = new[] { (W.X1[t], W.Y1[t]), (W.X2[t], W.Y2[t]), (W.X3[t], W.Y3[t]) };
            for (int e = 0; e < 3; e++)
            {
                var p = P3[e]; var q = P3[(e + 1) % 3];
                var kp = Q(p.Item1, p.Item2); var kq = Q(q.Item1, q.Item2);
                var key = kp.CompareTo(kq) <= 0 ? (kp, kq) : (kq, kp);
                if (!seen.Add(key)) continue;
                lines.Add(gf.CreateLineString(new[] { new Coordinate(p.Item1, p.Item2), new Coordinate(q.Item1, q.Item2) }));
            }
        }
        lines.Add(band.ExteriorRing);
        for (int h = 0; h < band.NumInteriorRings; h++) lines.Add(band.GetInteriorRingN(h));
        Geometry noded;
        try { noded = UnaryUnionNG.Union((Geometry)gf.CreateGeometryCollection(lines.ToArray()), pm); }
        catch (Exception ex) { note = $"선 잇기가 터졌다 {ex.GetType().Name}"; bad = 1; return res; }
        var pz = new Polygonizer();
        pz.Add(noded);
        // 띠도 같은 격자로 — 조각은 이 띠의 안이거나 밖이다(선 위에 걸친 조각이 없다)
        var bandS = NetTopologySuite.Precision.GeometryPrecisionReducer.Reduce(band, pm);
        var prep = PreparedGeometryFactory.Prepare(bandS);
        int faces = 0, inside = 0, noTri = 0, triFail = 0, areaMis = 0; double area = 0;
        foreach (var g in pz.GetPolygons())
        {
            if (g is not Polygon f || f.IsEmpty || f.Area <= 0) continue;
            faces++;
            var ip = f.InteriorPoint;
            if (ip == null || !prep.Contains(ip)) continue;
            inside++;
            int t = W.At(ip.X, ip.Y);
            if (t < 0 && !W.TryZNear(ip.X, ip.Y, 1e-5, out _)) { noTri++; continue; }
            if (t < 0)
            {
                // 옹벽 테두리 1µm 밖에 걸친 조각(격자 맞춤) — 가장 가까운 삼각형 평면으로
                double best = double.MaxValue;
                foreach (int i in W.Tree.Query(new Envelope(ip.X - 1e-5, ip.X + 1e-5, ip.Y - 1e-5, ip.Y + 1e-5)))
                {
                    double d0 = Math.Min(SegD(ip.X, ip.Y, W.X1[i], W.Y1[i], W.X2[i], W.Y2[i]),
                               Math.Min(SegD(ip.X, ip.Y, W.X2[i], W.Y2[i], W.X3[i], W.Y3[i]), SegD(ip.X, ip.Y, W.X3[i], W.Y3[i], W.X1[i], W.Y1[i])));
                    if (d0 < best) { best = d0; t = i; }
                }
                if (t < 0) { noTri++; continue; }
            }
            // ★[검토 v100.3 · 중간 5] 조각마다 따로 — 한 조각 삼각분할이 터져도 전체가 무너지지 않게, 그리고 넓이를 대조한다
            Geometry trs;
            try { trs = NetTopologySuite.Triangulate.Polygon.ConstrainedDelaunayTriangulator.Triangulate(f); }
            catch { triFail++; continue; }
            double fa = 0;
            var local = new List<Tri>();
            for (int q = 0; q < trs.NumGeometries; q++)
            {
                if (trs.GetGeometryN(q) is not Polygon tq || tq.Area <= 0) continue;
                var c = tq.ExteriorRing.Coordinates;
                int tt = t;
                Point3 P(Coordinate k) => new(k.X + ox, k.Y + oy, W.Z(tt, k.X, k.Y));
                local.Add(new Tri(P(c[0]), P(c[1]), P(c[2])));
                fa += tq.Area;
            }
            if (Math.Abs(fa - f.Area) > 1e-9 + 1e-9 * f.Area) { areaMis++; continue; }
            res.AddRange(local);
            area += fa;
        }
        bad = noTri + triFail + areaMis;
        note = $"옹벽 삼각형 {nT}개 변 + 초록 선을 1µm 격자로 이어 조각 {faces}개 · 띠 안 {inside}개 → 삼각형 {res.Count}개 · 넓이 {area:F4}㎡(띠 {band.Area:F4}㎡)"
             + (noTri > 0 ? $" · ⚠옹벽 밖 조각 {noTri}" : "") + (triFail > 0 ? $" · ⚠삼각분할 실패 {triFail}" : "") + (areaMis > 0 ? $" · ⚠넓이 안 맞는 조각 {areaMis}" : "");
        return res;
    }

    /// <summary>★[v100.3] 삼각형들을 LandXML TIN(Pnts + Faces)으로 쓴다 — Civil <c>TinSurface.CreateFromLandXML</c>가 면을 그대로 받는다.
    /// <para>같은 자리 점(<paramref name="mergeTol"/> 안)은 한 점으로 — 이웃 삼각형이 따로 구한 같은 점이 부동소수로 어긋나 틈이 나지 않게.
    /// 합친 뒤 넓이 0이 된 면은 뺀다(<paramref name="dropped"/>). 좌표는 LandXML 규약대로 «북 동 표고»(Y X Z).</para></summary>
    public static void WriteLandXmlTin(string path, string surfaceName, IReadOnlyList<Tri> tris, double mergeTol,
        out int nPts, out int nFaces, out int dropped)
    {
        var ci = CultureInfo.InvariantCulture;
        var id = new Dictionary<(long, long), int>();
        var pts = new List<Point3>();
        int Id(Point3 q)
        {
            var k = ((long)Math.Round(q.X / mergeTol), (long)Math.Round(q.Y / mergeTol));
            if (id.TryGetValue(k, out int v)) return v;
            pts.Add(q); id[k] = pts.Count;      // LandXML 점 번호는 1부터
            return pts.Count;
        }
        var faces = new List<(int A, int B, int C)>();
        dropped = 0;
        foreach (var t in tris)
        {
            int a = Id(t.A), b = Id(t.B), c = Id(t.C);
            if (a == b || b == c || a == c) { dropped++; continue; }
            var pa = pts[a - 1]; var pb = pts[b - 1]; var pc = pts[c - 1];
            double cr = (pb.X - pa.X) * (pc.Y - pa.Y) - (pb.Y - pa.Y) * (pc.X - pa.X);
            if (Math.Abs(cr) < 1e-14) { dropped++; continue; }
            faces.Add(cr > 0 ? (a, b, c) : (a, c, b));          // 반시계로 맞춘다
        }
        nPts = pts.Count; nFaces = faces.Count;
        using var w = new StreamWriter(path, false, new UTF8Encoding(false));
        w.WriteLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        w.WriteLine("<LandXML xmlns=\"http://www.landxml.org/schema/LandXML-1.2\" version=\"1.2\" date=\"" + DateTime.Now.ToString("yyyy-MM-dd", ci) + "\" time=\"" + DateTime.Now.ToString("HH:mm:ss", ci) + "\">");
        w.WriteLine("  <Units><Metric areaUnit=\"squareMeter\" linearUnit=\"meter\" volumeUnit=\"cubicMeter\" temperatureUnit=\"celsius\" pressureUnit=\"milliBars\" angularUnit=\"decimal degrees\" directionUnit=\"decimal degrees\"/></Units>");
        w.WriteLine("  <Surfaces>");
        w.WriteLine($"    <Surface name=\"{surfaceName}\">");
        w.WriteLine("      <Definition surfType=\"TIN\">");
        w.WriteLine("        <Pnts>");
        for (int i = 0; i < pts.Count; i++)
            w.WriteLine(string.Format(ci, "          <P id=\"{0}\">{1:R} {2:R} {3:R}</P>", i + 1, pts[i].Y, pts[i].X, pts[i].Z));
        w.WriteLine("        </Pnts>");
        w.WriteLine("        <Faces>");
        foreach (var f in faces) w.WriteLine($"          <F>{f.A} {f.B} {f.C}</F>");
        w.WriteLine("        </Faces>");
        w.WriteLine("      </Definition>");
        w.WriteLine("    </Surface>");
        w.WriteLine("  </Surfaces>");
        w.WriteLine("</LandXML>");
    }

    /// <summary>★★[v100.3 · JACK 0918 17:33 «너무 톱니같아»] 링을 <b>옹벽 삼각망의 변·꼭짓점에서 조금 비킨다</b>.
    /// <para>현장 17:32: 정확한 링으로 지은 순수옹벽_DH가 되읽기에서 «최대 거리 41mm»로 걸려 1mm 격자 링으로 되돌아갔다(톱니 60mm).
    /// 정확한 링은 소단 모서리(120·115m)·옹벽 밑선(105m 47m)을 <b>삼각망 변과 겹쳐</b> 따라가고 7점은 삼각망 꼭짓점과 <b>딱 겹쳤다</b>
    /// — 1mm 격자 링엔 없던 모양이다. 비파괴 경계가 제 변과 포갠 경계를 어떻게 자르는지는 모른다(그래서 이것은 <b>두 번째 시도</b>다).</para>
    /// <para>변과 포갠 링 변은 <b>띠 바깥쪽</b>으로 <paramref name="eps"/>만큼 평행이동하고(바깥은 버린 자리 — 포개진 소단이면 높이가 같다),
    /// 꼭짓점과 겹친 점은 바깥쪽 이등분 방향으로 비킨다. 옮긴 점의 높이는 그 자리 옹벽 높이(Civil이 테두리에 줄 값)로 — 1:0.01 면이어도
    /// 10µm는 1mm다. 옮긴 뒤 링이 제 몸을 지르면 원래 링을 그대로 돌려준다(moved = −1).</para></summary>
    /// <param name="isHole">구멍 링이면 참 — 띠 바깥 = 구멍 안쪽.</param>
    /// <param name="maxDz">옮긴 점의 새 높이(옹벽) − 원래 높이 최대 — 명령줄에 «테두리 높이 차»로 그대로 적는다(검토 v100.3 · 중간 2).</param>
    public static List<Point3> OffTinEdges(IReadOnlyList<Point3> ring, IReadOnlyList<Tri> wallTris, bool isHole, double eps,
        out int moved, out string at, out double maxDz)
    {
        moved = 0; at = ""; maxDz = 0;
        int n = ring.Count;
        var res = new List<Point3>(ring);
        if (n < 3 || wallTris == null || wallTris.Count == 0) return res;
        double ox = ring[0].X, oy = ring[0].Y;
        var W = new TriSet(wallTris, ox, oy);
        // 삼각망 변(겹친 것은 하나로)과 꼭짓점
        var edges = new List<(double X1, double Y1, double X2, double Y2)>();
        var eTree = new STRtree<int>();
        var seen = new HashSet<((long, long), (long, long))>();
        var verts = new STRtree<(double X, double Y)>();
        var vSeen = new HashSet<(long, long)>();
        static (long, long) Q(double x, double y) => ((long)Math.Round(x * 1e7), (long)Math.Round(y * 1e7));
        for (int t = 0; t < W.N; t++)
        {
            if (!W.Ok[t]) continue;
            var P3 = new[] { (W.X1[t], W.Y1[t]), (W.X2[t], W.Y2[t]), (W.X3[t], W.Y3[t]) };
            for (int e = 0; e < 3; e++)
            {
                var a = P3[e]; var b = P3[(e + 1) % 3];
                var ka = Q(a.Item1, a.Item2); var kb = Q(b.Item1, b.Item2);
                if (vSeen.Add(ka)) verts.Insert(new Envelope(a.Item1, a.Item1, a.Item2, a.Item2), (a.Item1, a.Item2));
                var key = ka.CompareTo(kb) <= 0 ? (ka, kb) : (kb, ka);
                if (!seen.Add(key)) continue;
                eTree.Insert(new Envelope(a.Item1, b.Item1, a.Item2, b.Item2), edges.Count);
                edges.Add((a.Item1, a.Item2, b.Item1, b.Item2));
            }
        }
        if (edges.Count == 0) return res;
        eTree.Build(); verts.Build();
        var L = new (double X, double Y)[n];
        for (int i = 0; i < n; i++) L[i] = (ring[i].X - ox, ring[i].Y - oy);
        // 돌림 방향 — 띠 바깥 법선: 반시계 링이면 오른쪽. 구멍이면 반대
        double area2 = 0;
        for (int i = 0; i < n; i++) { var a = L[i]; var b = L[(i + 1) % n]; area2 += a.X * b.Y - b.X * a.Y; }
        double side = (area2 > 0 ? 1.0 : -1.0) * (isHole ? -1.0 : 1.0);
        const double Tol = 1e-7;
        // 링 변이 삼각망 변 위에 누웠나 — 가운데 점이 어떤 변 위(1e-7)이고 그 변과 나란하면(여러 변을 곧게 이어 지나가도 잡는다),
        //   또는 삼각망 꼭짓점이 링 변 한가운데(끝점 아닌 곳)에 놓였으면
        var onEdge = new bool[n];
        for (int i = 0; i < n; i++)
        {
            var a = L[i]; var b = L[(i + 1) % n];
            double dx = b.X - a.X, dy = b.Y - a.Y, len = Math.Sqrt(dx * dx + dy * dy);
            if (len < 1e-12) continue;
            double mx = (a.X + b.X) * 0.5, my = (a.Y + b.Y) * 0.5;
            foreach (int k in eTree.Query(new Envelope(mx - Tol, mx + Tol, my - Tol, my + Tol)))
            {
                var g = edges[k];
                double gx = g.X2 - g.X1, gy = g.Y2 - g.Y1, gl = Math.Sqrt(gx * gx + gy * gy);
                if (gl < 1e-12 || SegD(mx, my, g.X1, g.Y1, g.X2, g.Y2) > Tol) continue;
                if (Math.Abs(dx * gy - dy * gx) / (len * gl) < 1e-6) { onEdge[i] = true; break; }
            }
            if (onEdge[i]) continue;
            var env = new Envelope(a.X, b.X, a.Y, b.Y); env.ExpandBy(Tol);
            foreach (var v in verts.Query(env))
            {
                if ((v.X - a.X) * (v.X - a.X) + (v.Y - a.Y) * (v.Y - a.Y) <= Tol * Tol || (v.X - b.X) * (v.X - b.X) + (v.Y - b.Y) * (v.Y - b.Y) <= Tol * Tol) continue;
                if (SegD(v.X, v.Y, a.X, a.Y, b.X, b.Y) <= Tol) { onEdge[i] = true; break; }
            }
        }
        var onVert = new bool[n];
        for (int i = 0; i < n; i++)
            foreach (var v in verts.Query(new Envelope(L[i].X - Tol, L[i].X + Tol, L[i].Y - Tol, L[i].Y + Tol)))
                if ((v.X - L[i].X) * (v.X - L[i].X) + (v.Y - L[i].Y) * (v.Y - L[i].Y) <= Tol * Tol) { onVert[i] = true; break; }
        (double X, double Y) Out(int i)
        {
            var a = L[i]; var b = L[(i + 1) % n];
            double dx = b.X - a.X, dy = b.Y - a.Y, l = Math.Sqrt(dx * dx + dy * dy);
            return l < 1e-15 ? (0, 0) : (side * dy / l, -side * dx / l);
        }
        var sb = new StringBuilder();
        for (int i = 0; i < n; i++)
        {
            int ip = (i - 1 + n) % n;
            bool eIn = onEdge[ip], eOut = onEdge[i];
            if (!eIn && !eOut && !onVert[i]) continue;
            // ★[검토 v100.3 · 낮음 4] 옮기는 점은 모두 <b>양옆 변을 바깥으로 ε 민 두 직선의 교점</b>(마이터)으로 — 늘 바깥으로만 간다.
            //   (한쪽 변의 법선으로만 옮기면 90°보다 많이 꺾인 모서리에서 이웃 변을 넘어 띠 안으로 들어갔다 — 17:32 i=215, 4.3µm.
            //   이웃 직선 위로만 옮기면 그 이웃이 삼각망 변과 포갠 채로 남는 자리가 생겼다 — ⑭ «다시 4».)
            //   거의 곧으면 법선 평균, 아주 뾰족하면 3ε로 묶는다
            (double X, double Y) d;
            {
                var n1 = Out(ip); var n2 = Out(i);
                double c = n1.X * n2.X + n1.Y * n2.Y;
                d = c > 0.99 ? ((n1.X + n2.X) * 0.5, (n1.Y + n2.Y) * 0.5) : c < -0.99 ? n2 : ((n1.X + n2.X) / (1 + c), (n1.Y + n2.Y) / (1 + c));
                double dl = Math.Sqrt(d.X * d.X + d.Y * d.Y);
                if (dl > 3) d = (d.X / dl * 3, d.Y / dl * 3);
            }
            double x = L[i].X + d.X * eps, y = L[i].Y + d.Y * eps;
            double z = W.TryZ(x, y, out double zw) ? zw : ring[i].Z;
            res[i] = new Point3(x + ox, y + oy, z);
            maxDz = Math.Max(maxDz, Math.Abs(z - ring[i].Z));
            moved++;
            if (sb.Length < 200) sb.Append($" ({ring[i].X:F3},{ring[i].Y:F3}){(onVert[i] ? "꼭짓점" : "")}{(eIn || eOut ? "변" : "")}");
        }
        at = sb.ToString();
        if (moved > 0 && !RingOk(new GeometryFactory(), res.ConvertAll(q => new Point3(q.X - ox, q.Y - oy, q.Z)))) { moved = -1; return new List<Point3>(ring); }
        return res;
    }

    private static bool RingOk(GeometryFactory gf, IReadOnlyList<Point3> r)
    {
        if (r == null || r.Count < 3) return false;
        try
        {
            var cs = new Coordinate[r.Count + 1];
            for (int i = 0; i < r.Count; i++) cs[i] = new Coordinate(r[i].X, r[i].Y);
            cs[r.Count] = cs[0].Copy();
            var lr = gf.CreateLinearRing(cs);
            return lr.IsSimple && gf.CreatePolygon(lr).IsValid;
        }
        catch { return false; }
    }

    // ───────────────────────────── 재생 파일(출하 입력을 하네스로) ─────────────────────────────

    /// <summary>★[검토 0918 · 높음 4] <b>검사 입력은 출하 입력이어야 한다.</b>
    /// 실행마다 Civil이 준 삼각형을 그대로 적어 두면 하네스가 같은 입력으로 다시 돌린다.</summary>
    public static void WriteInput(string path, IReadOnlyList<Point3> poly, IReadOnlyList<Tri> wall,
        IReadOnlyList<Tri> ground, string meta)
    {
        var ci = CultureInfo.InvariantCulture;
        using var w = new StreamWriter(path, false, new UTF8Encoding(false));
        w.WriteLine("# DHWALLDL v1 " + meta.Replace('\n', ' '));
        w.WriteLine($"POLY {poly.Count}");
        foreach (var q in poly) w.WriteLine(string.Format(ci, "{0:R} {1:R} {2:R}", q.X, q.Y, q.Z));
        void Tris(string tag, IReadOnlyList<Tri> ts)
        {
            w.WriteLine($"{tag} {ts.Count}");
            foreach (var t in ts)
                w.WriteLine(string.Format(ci, "{0:R} {1:R} {2:R} {3:R} {4:R} {5:R} {6:R} {7:R} {8:R}",
                    t.A.X, t.A.Y, t.A.Z, t.B.X, t.B.Y, t.B.Z, t.C.X, t.C.Y, t.C.Z));
        }
        Tris("WALL", wall);
        Tris("GROUND", ground);
    }

    public static bool TryReadInput(string path, out List<Point3> poly, out List<Tri> wall,
        out List<Tri> ground, out string meta)
    {
        poly = new(); wall = new(); ground = new(); meta = "";
        if (!File.Exists(path)) return false;
        var ci = CultureInfo.InvariantCulture;
        var lines = File.ReadAllLines(path);
        int k = 0;
        if (k < lines.Length && lines[k].StartsWith("#")) meta = lines[k++];
        List<double[]> Block(string tag)
        {
            var res = new List<double[]>();
            if (k >= lines.Length || !lines[k].StartsWith(tag + " ")) return res;
            int n = int.Parse(lines[k++].Substring(tag.Length + 1), ci);
            for (int i = 0; i < n && k < lines.Length; i++, k++)
            {
                var parts = lines[k].Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var v = new double[parts.Length];
                for (int j = 0; j < parts.Length; j++) v[j] = double.Parse(parts[j], ci);
                res.Add(v);
            }
            return res;
        }
        foreach (var v in Block("POLY")) poly.Add(new Point3(v[0], v[1], v[2]));
        foreach (var v in Block("WALL")) wall.Add(new Tri(new Point3(v[0], v[1], v[2]), new Point3(v[3], v[4], v[5]), new Point3(v[6], v[7], v[8])));
        foreach (var v in Block("GROUND")) ground.Add(new Tri(new Point3(v[0], v[1], v[2]), new Point3(v[3], v[4], v[5]), new Point3(v[6], v[7], v[8])));
        return poly.Count >= 3 && wall.Count > 0 && ground.Count > 0;
    }
}
