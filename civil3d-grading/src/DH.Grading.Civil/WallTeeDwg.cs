using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using DH.Grading.Core;

namespace DH.Grading.Civil;

/// <summary>[역T형 옹벽 — JACK 0730] 역T 단면(벽체+저판)을 정렬선(계획경계 서브아크) 세그먼트별로 압출.
/// 벽 상단은 지반고를 따라 세그먼트 단위로 계단식 추종. 저판 치수는 런 전체 최대 벽높이 기준(연속 기초).
/// 치수(표준 개략): 벽체 두께 0.35, 저판 두께 0.4, 저판 폭 B=max(0.6H,1.2), 앞굽 max(0.15H,0.3).
/// [JACK 0731] ①양끝은 데이라잇 교차점까지 테이퍼 마감(h≈0 표본만 트림 후 한 칸 확장 — 끝단 수렴)
///   ②안쪽 낮은 구간은 최소 벽고로 브리지(중간 누락 금지 — 연속 벽) ③전체 ZSink 하강+전면 FrontOut 돌출
///   (지표면과 z-fighting 방지) ④전면 노출면에 자연석 무늬(앵커판넬과 동일 질감).</summary>
public static class WallTeeDwg
{
    private const double StemT = 0.35;    // 벽체 두께
    private const double SlabT = 0.40;    // 저판 두께
    private const double ZSink = 0.01;    // [JACK 0731] 전체 1cm 아래 — 지표면과 겹침 깜빡임 방지
    private const double FrontOut = 0.05; // [JACK 0731] 전면을 공기쪽으로 살짝 — 표면 수직면과 분리
    // [JACK 0731 끝단 마감] 상단이 지형 추종 연속 경사(v14.8)가 된 뒤로는 끝단을 높이에서 자를 필요가 없다 —
    //   벽이 데이라잇 교차점까지 얇아지며 수렴(테이퍼)하는 게 요구 형상. 기하 퇴화만 막는 최소 높이 2cm.
    private const double HMin = 0.02;     // 기하 최소 벽고(퇴화 방지) — 트림·정점 클램프 공용

    // ★[JACK 0807] **역T는 '1단만 생기는 구간'에서만 쓰이는 옵션**이다 — 정지옵션에서 역T를 골라도
    //   2단 이상 구간은 자동으로 앵커판넬(절토)/보강토(성토)로 대체된다(InfraworksCommand의 자동 대체).
    //   그래서 여기 무늬는 **한 단 높이(보통 ≤5m)의 연속 벽체 전면**만 다루면 된다 —
    //   판넬처럼 격자로 쪼개진 면이 아니라 세그먼트 하나가 통짜 면이고, 상단은 지형을 따라 경사진다.
    //   방어를 옮길 때도 이 전제를 지킨다: 오목 판넬 볼록 분해(앵커판넬용)는 **여기선 필요 없다** —
    //   클립이 상단 경사선 **반평면 하나**뿐이라 결과가 항상 볼록이고 자기교차가 원천적으로 안 생긴다.
    // 전면 자연석 무늬 — **역T는 옛 자연석 질감을 그대로 쓴다**(JACK 0807 확정).
    //   앵커판넬은 0806에 십자 4분할로 바꿨지만(실물 PSM 판넬의 줄눈) 역T는 판넬이 아니라 연속 벽체라
    //   4분할이 맞지 않는다. 두 벽 종류가 서로 다른 질감을 쓰는 것은 **의도된 것**이니 통일하지 말 것.
    //   ※다만 이 경로는 돌을 낱개로 리전→압출하므로 ACIS 오류(115094)에 취약하다 —
    //     역T를 실제로 쓰기 시작하면 v19.23~25에서 앵커판넬에 넣은 방어(개별 리전·빈 목록·볼록 분해)를
    //     여기에도 옮겨야 한다. 지금 현장은 역T가 0세그라 미룬 것뿐이다.
    private const double StoneSize = 0.40;
    private const double GrooveW = 0.05;
    private const double Relief = 0.035;
    private static double Hash(int i, int j) { double s = System.Math.Sin(i * 12.9898 + j * 78.233) * 43758.5453; return (s - System.Math.Floor(s)) * 2 - 1; }

    /// <summary>[진단 0731] 직전 Populate 상세 — 세그/스킵/브리지/무늬 수(DHINFRA 로그 표기, 누락·무늬 소실 추적).</summary>
    public static string LastDiag { get; private set; } = "";

    /// <summary>runs를 모델공간에 채움. 반환=생성 솔리드 수(세그먼트 단위).</summary>
    public static int Populate(Database db, Transaction tr, IReadOnlyList<WallTee.Run> runs)
    {
        LastDiag = "";
        if (runs == null || runs.Count == 0) return 0;
        var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
        ObjectId layId = EnsureLayer(db, tr, "옹벽-역T", 253);   // 밝은 회색(콘크리트)
        int made = 0;
        int segLenSkip = 0, segFail = 0, bridged = 0, padsMade = 0, padsMiss = 0, runDrop = 0;
        int stoneOk = 0, stoneFail = 0;
        string firstErr = "", firstPadErr = "", segFailInfo = "";

        foreach (var run in runs)
        {
            var P = run.PathBottom; var T = run.TopZ;
            if (P == null || T == null || P.Count < 2) continue;
            int n = System.Math.Min(P.Count, T.Count);

            // [JACK 0731 끝단 마감] 데이라잇 트림 — 벽높이 HMin(2cm) 미만(사실상 0) 표본만 걷어낸 뒤,
            //   경계 바깥 표본 1개씩 되살린다 → 마지막 세그 상단이 데이라잇 교차점까지 얇아지며 수렴(테이퍼).
            int i0 = 0, i1 = n - 1;
            while (i0 < i1 && T[i0] - P[i0].Z < HMin) i0++;
            while (i1 > i0 && T[i1] - P[i1].Z < HMin) i1--;
            if (i1 - i0 < 1) { runDrop++; continue; }
            if (i0 > 0) i0--;             // 끝단 테이퍼용 — h≈0 정점까지 한 칸 확장
            if (i1 < n - 1) i1++;

            // 저판 치수는 트림 구간의 최대 벽높이 기준(연속 확대기초).
            double Hd = 0;
            for (int i = i0; i <= i1; i++) Hd = System.Math.Max(Hd, T[i] - P[i].Z);
            if (Hd < 0.3) continue;
            double slabB = System.Math.Max(0.6 * Hd, 1.2);
            double toe = System.Math.Max(0.15 * Hd, 0.3);
            double heel = System.Math.Max(slabB - toe - StemT, 0.2);
            double B = toe + StemT + heel;

            for (int i = i0; i + 1 <= i1; i++)
            {
                var a = P[i]; var b = P[i + 1];
                double dx = b.X - a.X, dy = b.Y - a.Y, dz = b.Z - a.Z;
                double len = System.Math.Sqrt(dx * dx + dy * dy + dz * dz);
                if (len < 0.05) { segLenSkip++; continue; }
                // [JACK 0731 계단 제거] 벽 상단은 표본 지반고를 정점별로 따라가는 '연속 경사'(hA→hB) —
                //   이웃 세그와 정점 높이를 공유하므로 상단선이 끊김 없이 데이라잇을 추종한다(계단·단차 없음).
                //   끝단·지형 골에서는 HMin(2cm)까지 수렴 — 벽이 데이라잇 교차점에서 자연 마감(끝단 마감).
                double hAraw = T[i] - a.Z, hBraw = T[i + 1] - b.Z;
                if (hAraw < HMin || hBraw < HMin) bridged++;
                double hA = System.Math.Max(hAraw, HMin), hB = System.Math.Max(hBraw, HMin);
                var left = Vector3d.ZAxis.CrossProduct(new Vector3d(dx, dy, 0));
                if (left.Length < 1e-9) continue;
                left = left.GetNormal();
                var soil = left * run.SoilLeft;
                var ext = new Vector3d(dx, dy, dz);
                var U2 = ext * (1.0 / len);

                // [JACK 0731] 원점을 ZSink 내리고 공기쪽(−soil)으로 FrontOut 내밀어 지표면과 분리.
                // [대좌표 대응 0731] 좌표 45만m대에서 ACIS 리전/압출이 간헐적 eInvalidInput(진단: i20·i30, 정상 치수)
                //   → 정석대로 '원점 근처에서 생성 후 제자리로 이동'(oShift). 벽체·저판·무늬 모두 동일.
                var o = new Point3d(a.X - soil.X * FrontOut, a.Y - soil.Y * FrontOut, a.Z - ZSink);
                var oShift = Matrix3d.Displacement(o.GetAsVector());

                // ── 벽체: '전면 다각형'(하단, 상단 hA→hB 경사)을 흙쪽으로 StemT 두께 압출 — 상단이 지형 추종.
                //   하단은 저판 속으로 5cm 물림(별도 솔리드 접촉면 깜빡임 방지 — 저판 두께 0.4 안이라 안 보임).
                const double stemEmbed = 0.05;
                try
                {
                    var facePts = new Point3dCollection
                    {
                        Point3d.Origin - Vector3d.ZAxis * stemEmbed,
                        Point3d.Origin + U2 * len - Vector3d.ZAxis * stemEmbed,
                        Point3d.Origin + U2 * len + Vector3d.ZAxis * hB,
                        Point3d.Origin + Vector3d.ZAxis * hA,
                    };
                    using var pl = new Polyline3d(Poly3dType.SimplePoly, facePts, true);
                    using var curves = new DBObjectCollection { pl };
                    using var regions = Region.CreateFromCurves(curves);
                    if (regions.Count == 0) throw new System.InvalidOperationException("전면 리전 0");
                    using var region = (Region)regions[0];
                    var stem = new Solid3d();
                    stem.CreateExtrudedSolid(region, soil * StemT, new SweepOptions());   // 전면(0)→흙쪽 두께
                    stem.TransformBy(oShift);
                    stem.LayerId = layId;
                    stem.Color = Color.FromColorIndex(ColorMethod.ByAci, 253);
                    ms.AppendEntity(stem); tr.AddNewlyCreatedDBObject(stem, true);
                    made++;
                }
                catch (System.Exception ex)
                {
                    segFail++;
                    if (segFailInfo.Length < 200)
                        segFailInfo += $"[벽체 i{i} len{len:F2} h{hA:F2}~{hB:F2}: {ex.Message}] ";
                    if (firstErr.Length == 0) firstErr = ex.Message;
                    continue;
                }

                // ── 저판: 단면(soil-Z 평면, 원점 기준)을 경로 방향으로 압출(묻히는 부분 — 세그 평탄 유지).
                try
                {
                    Point3d Sp(double x, double y) => Point3d.Origin + soil * x + Vector3d.ZAxis * y;
                    var slabPts = new Point3dCollection
                    { Sp(-toe, 0), Sp(-toe, -SlabT), Sp(B - toe, -SlabT), Sp(B - toe, 0) };
                    using var pl2 = new Polyline3d(Poly3dType.SimplePoly, slabPts, true);
                    using var curves2 = new DBObjectCollection { pl2 };
                    using var regions2 = Region.CreateFromCurves(curves2);
                    if (regions2.Count > 0)
                    {
                        using var region2 = (Region)regions2[0];
                        var slab = new Solid3d();
                        slab.CreateExtrudedSolid(region2, ext, new SweepOptions());
                        slab.TransformBy(oShift);
                        slab.LayerId = layId;
                        slab.Color = Color.FromColorIndex(ColorMethod.ByAci, 253);
                        ms.AppendEntity(slab); tr.AddNewlyCreatedDBObject(slab, true);
                    }
                }
                catch (System.Exception ex)
                {
                    if (segFailInfo.Length < 200) segFailInfo += $"[저판 i{i}: {ex.Message}] ";
                }

                // [JACK 0731] 전면 노출면(벽체 앞면)에만 자연석 무늬 — 돌 개별 압출(검증된 경로), 상단 경사선으로 클립.
                // ★[JACK 0806 '무늬도 다 없애'] 앵커판넬과 **같은 스위치**로 끈다 — 한쪽만 끄면 도면에서 질감이 어긋난다.
                if (GradingSettings.StonePattern)
                {
                    var (stOk, stFail, stErr) = AppendTeePads(ms, tr, layId, oShift, U2, len, hA, hB, soil, i);
                    stoneOk += stOk; stoneFail += stFail;
                    if (stOk > 0) padsMade++; else padsMiss++;
                    if (stErr.Length > 0 && firstPadErr.Length == 0) firstPadErr = stErr;
                }
            }
        }
        LastDiag = $"세그 {made}·길이스킵 {segLenSkip}·생성실패 {segFail}·브리지 {bridged}" +
                   $"·무늬세그 {padsMade}/{padsMade + padsMiss}·무늬돌 {stoneOk}(실패 {stoneFail})" +
                   (runDrop > 0 ? $"·런제외 {runDrop}" : "") +
                   (segFailInfo.Length > 0 ? $" · 세그오류 {segFailInfo.TrimEnd()}" : "") +
                   (firstPadErr.Length > 0 ? $" · 무늬 첫오류: {firstPadErr}" : "");
        return made;
    }

    /// <summary>세그먼트 전면(벽체 앞면)의 자연석 무늬 — 면을 (u,v)로 파라미터화(u=경로방향 0..len, v=수직,
    /// 점=U2·u+Z·v, 원점 기준 생성 후 oShift로 이동 — 대좌표 ACIS 오류 회피). 지터드 격자 돌을
    /// 홈(GrooveW)만큼 축소하고 **상단 경사선(v ≤ hA+(hB−hA)·u/len)으로 클립** — 돌이 지형 추종 상단 위로
    /// 안 삐져나감. 돌마다 개별 리전→공기쪽(−soil) Relief 압출→개별 배치(검증된 경로).
    /// 반환=(성공 돌 수, 실패 돌 수, 첫 오류).</summary>
    private static (int ok, int fail, string err) AppendTeePads(BlockTableRecord ms, Transaction tr, ObjectId layId,
        Matrix3d oShift, Vector3d U2, double len, double hA, double hB, Vector3d soil, int seed)
    {
        double hMax = System.Math.Max(hA, hB);
        if (len < 0.15 || hMax < 0.12) return (0, 0, "");
        var air = -soil;   // 공기쪽(전면 돌출 방향)
        int nx = System.Math.Max(1, (int)System.Math.Round(len / StoneSize));
        int ny = System.Math.Max(1, (int)System.Math.Round(hMax / StoneSize));
        double du = len / nx, dv = hMax / ny;
        // 지터드 격자점(경계점 고정 — 세그 가장자리 깔끔, 이웃 세그와 이 맞음).
        var pts = new Point2d[nx + 1, ny + 1];
        for (int i = 0; i <= nx; i++)
            for (int j = 0; j <= ny; j++)
            {
                double ju = (i == 0 || i == nx) ? 0 : Hash(i + seed * 13, j) * 0.33 * du;
                double jv = (j == 0 || j == ny) ? 0 : Hash(i + seed * 13 + 7, j + 3) * 0.33 * dv;
                pts[i, j] = new Point2d(i * du + ju, j * dv + jv);
            }
        double scale = System.Math.Max(0.5, 1 - GrooveW / System.Math.Min(du, dv));
        Point3d W(Point2d q) => Point3d.Origin + U2 * q.X + Vector3d.ZAxis * q.Y;

        // 상단 경사선 클립(Sutherland–Hodgman 한 변): f(p)=hA+(hB−hA)·u/len − v ≥ 0 이 '면 안'.
        System.Collections.Generic.List<Point2d> ClipTop(System.Collections.Generic.List<Point2d> poly)
        {
            var outp = new System.Collections.Generic.List<Point2d>();
            int n = poly.Count;
            for (int k = 0; k < n; k++)
            {
                var cur = poly[k]; var prv = poly[(k - 1 + n) % n];
                double fc = hA + (hB - hA) * cur.X / len - cur.Y;
                double fp = hA + (hB - hA) * prv.X / len - prv.Y;
                bool inC = fc >= -1e-9, inP = fp >= -1e-9;
                if (inC)
                {
                    if (!inP) { double t = fp / (fp - fc); outp.Add(new Point2d(prv.X + (cur.X - prv.X) * t, prv.Y + (cur.Y - prv.Y) * t)); }
                    outp.Add(cur);
                }
                else if (inP) { double t = fp / (fp - fc); outp.Add(new Point2d(prv.X + (cur.X - prv.X) * t, prv.Y + (cur.Y - prv.Y) * t)); }
            }
            return outp;
        }

        int ok = 0, fail = 0; string err = "";
        for (int i = 0; i < nx; i++)
            for (int j = 0; j < ny; j++)
            {
                try
                {
                    var a = pts[i, j]; var b = pts[i + 1, j]; var c = pts[i + 1, j + 1]; var d = pts[i, j + 1];
                    double cx = (a.X + b.X + c.X + d.X) / 4, cy = (a.Y + b.Y + c.Y + d.Y) / 4;
                    Point2d Sc(Point2d q) => new Point2d(cx + (q.X - cx) * scale, cy + (q.Y - cy) * scale);
                    var stoneUv = ClipTop(new System.Collections.Generic.List<Point2d> { Sc(a), Sc(b), Sc(c), Sc(d) });
                    // [리뷰 0731 중간1] 클립이 만든 근접중복 정점 정리 — 영길이 변이 Region/압출 eInvalidInput을
                    //   유발하는 계열(115094)의 재입구 차단(앵커판넬 DedupeRing과 동일 보험).
                    for (int k = stoneUv.Count - 1; k >= 1; k--)
                        if (stoneUv[k].GetDistanceTo(stoneUv[k - 1]) < 1e-6) stoneUv.RemoveAt(k);
                    while (stoneUv.Count >= 2 && stoneUv[0].GetDistanceTo(stoneUv[stoneUv.Count - 1]) < 1e-6)
                        stoneUv.RemoveAt(stoneUv.Count - 1);
                    if (stoneUv.Count < 3) continue;   // 상단선 위 돌 — 통째 클립(실패 아님)
                    // 미세 조각 제거(퇴화 리전 방지).
                    double area = 0;
                    for (int k = 0; k < stoneUv.Count; k++)
                    { var u1 = stoneUv[k]; var u2 = stoneUv[(k + 1) % stoneUv.Count]; area += u1.X * u2.Y - u2.X * u1.Y; }
                    if (System.Math.Abs(area) * 0.5 < 1e-3) continue;
                    // ★[JACK 0807 '역T도 쓸 거니깐 앵커판넬 넣었던 방어 넣어'] 앵커판넬에서 값비싸게 얻은
                    //   두 가지를 옮긴다. 나머지(돌마다 개별 압출·근접중복 정점 정리)는 이미 여기 있다.
                    //   ① **실오라기는 면적만으로 못 거른다.** 가느다란 쐐기(0.03m × 0.40m = 120㎠)는
                    //      면적 하한을 통과해 바늘 같은 조각으로 남는다(JACK 0805 '조각이 쪼개졌어').
                    //      실효 두께 = 면적 ÷ 최대 폭 — 길쭉할수록 작아지므로 길이에 안 속는다.
                    double sArea = System.Math.Abs(area) * 0.5;
                    double sx0 = double.MaxValue, sx1 = double.MinValue, sy0 = double.MaxValue, sy1 = double.MinValue;
                    foreach (var q in stoneUv)
                    { sx0 = System.Math.Min(sx0, q.X); sx1 = System.Math.Max(sx1, q.X); sy0 = System.Math.Min(sy0, q.Y); sy1 = System.Math.Max(sy1, q.Y); }
                    double sExt = System.Math.Max(sx1 - sx0, sy1 - sy0);
                    if (sExt < 1e-9 || sArea / sExt < 0.08) continue;    // 두께 8cm 미만 = 돌이 아니라 실오라기
                    var quad = new Point3dCollection();
                    foreach (var q in stoneUv) quad.Add(W(q));
                    using var pl = new Polyline3d(Poly3dType.SimplePoly, quad, true);
                    using var curves = new DBObjectCollection { pl };
                    using var regions = Region.CreateFromCurves(curves);
                    if (regions.Count == 0) { fail++; continue; }
                    for (int rq = 1; rq < regions.Count; rq++)          // 여분 리전 누수 방지
                        try { regions[rq].Dispose(); } catch { }
                    using var region = (Region)regions[0];
                    var stone = new Solid3d();
                    stone.CreateExtrudedSolid(region, air * Relief, new SweepOptions());
                    //   ② **압출이 조용히 깨진 솔리드를 내놓는 경우가 있다.** 그대로 두면 SaveAs가
                    //      'RECOVER 권장' 모달을 띄운다(JACK 0731). 경계상자와 부피로 확실한 증거가 있을 때만 버린다.
                    bool bad = false;
                    try { var _ = stone.GeometricExtents; } catch { bad = true; }
                    if (!bad)
                        try { double vol = stone.MassProperties.Volume; if (!(vol > 1e-9) || double.IsNaN(vol) || double.IsInfinity(vol)) bad = true; }
                        catch { }
                    if (bad) { try { stone.Dispose(); } catch { } fail++; continue; }
                    stone.TransformBy(oShift);
                    stone.LayerId = layId;
                    stone.Color = Color.FromColorIndex(ColorMethod.ByAci, 253);
                    ms.AppendEntity(stone); tr.AddNewlyCreatedDBObject(stone, true);
                    ok++;
                }
                catch (System.Exception ex)
                {
                    fail++;
                    if (err.Length == 0) err = ex.Message;
                }
            }
        return (ok, fail, err);
    }

    private static ObjectId EnsureLayer(Database db, Transaction tr, string name, short aci)
    {
        var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
        if (lt.Has(name)) return lt[name];
        lt.UpgradeOpen();
        var ltr = new LayerTableRecord
        {
            Name = name,
            Color = Color.FromColorIndex(ColorMethod.ByAci, aci),
        };
        var id = lt.Add(ltr);
        tr.AddNewlyCreatedDBObject(ltr, true);
        return id;
    }
}
