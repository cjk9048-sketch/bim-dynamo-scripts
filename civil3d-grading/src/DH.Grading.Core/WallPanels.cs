namespace DH.Grading.Core;

/// <summary>PSM(프리스트레스 패널식) 옹벽 — 프리캐스트 패널(1480×1480×200) 격자 + 어스앵커 (JACK 0721).
/// [단 링 기반 — §42] WallBlocks와 동일하게 GradingGeometry 단 링(vs.Rings)을 입력받아 **단별로** 사면 패널을
/// 붙인다(여러 단이 하나로 합쳐지지 않고 계단대로). 각 단 면을 슬로프길이로 격자화, daylight(원지반 컷)에서
/// 삼각형/사다리꼴로 클립, **온전 패널만** 중심 200×200 홈 + 앵커(원통 70mm, 20° 하향, **지반 속=벽 뒤**로).
///
/// 좌표: 단 크레스트 링을 호길이 u로, 사면 위로 슬로프길이 s로. 사면 1:n → s당 수직 s·vUp, 안쪽수평 s·hIn.
///  · 절토(cut): 토우=크레스트−(수직 step, 안쪽 −n·step), 위로 갈수록 안쪽·높이↑, 앵커=바깥(−안쪽법선)+하향.
///  · 성토(fill): 토우=크레스트(정렬선=토우 링), 아래로… 여기선 대칭 처리(면이 바깥·아래로), 앵커=안쪽+하향.</summary>
public static class WallPanels
{
    /// <summary>패널 1장. Poly=사면 위 클립 3D 폴리곤. IsFull=안 잘림(앵커·홈). AnchorPos/AnchorDir=앵커 헤드·방향.
    /// [DWG] Origin/UAxis/VAxis/WAxis=로컬 프레임, Local=로컬 2D 폴리곤(u,v).</summary>
    public readonly record struct Panel(
        IReadOnlyList<Point3> Poly, bool IsFull,
        Point3 Center, (double x, double y, double z) Normal,
        Point3 AnchorPos, (double x, double y, double z) AnchorDir,
        Point3 Origin,
        (double x, double y, double z) UAxis, (double x, double y, double z) VAxis, (double x, double y, double z) WAxis,
        IReadOnlyList<(double u, double v)> Local,
        double PocketU = 0, double PocketV = 0);

    public static string LastDiag { get; private set; } = "";

    /// <summary>코너 필러(quoin) — 미터로도 안 닫히는 코너 틈(절토 볼록·성토 오목)에만 세우는 얇은 수직 채움 기둥.
    /// Toe=코너 토우, Top=코너 데이라잇(사면 위), WidthAxis=틈을 가로지르는 수평축, W=부지쪽(두께 방향), Width=폭.
    /// (예전 허공 포스트와 달리 AtSeg로 토우·데이라잇을 정확히 계산 — 뜨지 않음.)</summary>
    public readonly record struct Quoin(
        Point3 Toe, Point3 Top,
        (double x, double y, double z) WidthAxis, (double x, double y, double z) W, double Width);

    /// <summary>직전 Generate가 만든 코너 필러들(방향별로 덮어씀 — 호출부에서 방향마다 수집).</summary>
    public static List<Quoin> LastQuoins { get; } = new();

    /// <param name="rings">GradingGeometry 단 링(rings[0]=pad, 이후 단). WallBlocks와 동일 입력.</param>
    /// <param name="ground">원지반.</param>
    /// <param name="cut">true=절토, false=성토.</param>
    /// <param name="slopeN">사면 구배 n(1:n). PSM 절토 0.3.</param>
    public static List<Panel> Generate(
        IReadOnlyList<IReadOnlyList<Point3>> rings, IGroundSurface ground, bool cut,
        double slopeN = 0.3, double panel = 1.48, double joint = 0.02, double anchorDeg = 20.0,
        double eps = 0.02)
    {
        var result = new List<Panel>();
        LastQuoins.Clear();
        if (rings == null || rings.Count < 2 || ground == null) { LastDiag = "링/지반 없음"; return result; }

        static double MeanZ(IReadOnlyList<Point3> r) { double s = 0; foreach (var p in r) s += p.Z; return s / System.Math.Max(r.Count, 1); }
        double jm = System.Math.Max(0, joint) / 2;                    // 줄눈 반폭 — 패널을 각 변에서 이만큼 인셋(이웃과 joint 틈)
        double denom = System.Math.Sqrt(1 + slopeN * slopeN);
        double vUp = 1.0 / denom, hIn = slopeN / denom;               // 슬로프길이 1당 (수직, 안쪽수평)
        double aRad = anchorDeg * System.Math.PI / 180.0;
        int full = 0, part = 0;

        for (int k = 1; k < rings.Count; k++)
        {
            var ring = rings[k];
            if (ring == null || ring.Count < 3) continue;
            double step = System.Math.Abs(MeanZ(ring) - MeanZ(rings[k - 1])); // 이 단 수직높이
            if (step < 0.1) continue;
            // [패널 크기 규칙 — JACK 0721] 단 수직높이 기준 정사각 한 변: ≤1m→높이, ≤3m→높이/2, ≤5m→높이/3.
            //   딱 안 나눠지면 소수점 둘째자리 반올림. 두께는 항상 200. (단높이는 팝업에서 5m 상한.)
            int nRow = step <= 1.0 + 1e-9 ? 1 : step <= 3.0 + 1e-9 ? 2 : 3;
            double side = System.Math.Round(step / nRow, 2);
            if (side < 0.05) continue;
            var walk = new Walk(ring);
            if (walk.Length < side) continue;
            double slopeLen = step / vUp;                              // 이 단 사면 길이
            // 짜투리 문턱 — 클립이 정확해진 뒤(v4.0)로는 데이라잇 작은 삼각형도 사면에 딱 붙으므로 살린다(JACK 0721
            //   "작은 삼각형 누락"). 익스트루드 실패/제로 솔리드 방지용 절대 하한만 둔다.
            const double sliverMin = 0.02;                            // ㎡ 절대 하한(사실상 거의 다 유지)
            const double edgeMin = 0.03;                              // m — 한 변이 이보다 짧은 선형 degenerate만 제거

            // 면점 — s(0=토우, slopeLen=크레스트)로 갈수록 위로. 수평 방향은 절토/성토가 반대:
            //  · 절토(cut): 크레스트(윗단)가 **바깥(산 쪽)** → 위로 갈수록 바깥(−n). 토우=크레스트에서 안쪽(+n)·아래.
            //  · 성토(fill): 크레스트(윗단)가 **안쪽(pad)** → 위로 갈수록 안쪽(+n). 토우=링 그대로.
            //  (기존: 절토를 안쪽으로 기울여 지형을 파고들어 파묻힘 — JACK 0721 지적. 방향 반전 수정.)
            double hs = cut ? -1.0 : 1.0;                              // 위로 갈 때 수평 방향(절토=바깥/성토=안쪽)
            // 면점 — 지정 세그먼트(seg)의 직선을 uAbs(호길이)에서 평가. 코너 넘어까지 연장 가능(미터용).
            //   각 패널은 한 세그먼트 평면 위에만 놓여 항상 평면 유지.
            Point3 FacePt(int seg, double u, double s)
            {
                var (cx, cy, cz, nx, ny) = walk.AtSeg(seg, u);
                double toeX, toeY, toeZ;
                if (cut) { toeX = cx + nx * (slopeN * step); toeY = cy + ny * (slopeN * step); toeZ = cz - step; }
                else { toeX = cx; toeY = cy; toeZ = cz; }
                return new Point3(toeX + nx * hIn * s * hs, toeY + ny * hIn * s * hs, toeZ + vUp * s);
            }
            // 상한 s(이 station에서 패널이 존재하는 최대 슬로프길이):
            //  · 절토 = daylight(면 Z가 원지반 만나는 s) — 그 위는 흙 밖이라 삼각형 클립.
            //  · 성토 = 보강토와 동일하게 **daylight 클립 안 함**(JACK): 크레스트가 지반 위면 slopeLen(꽉),
            //    아니면 0(벽 없음). 성토 벽 하단(토우)의 묻힘/허공은 정지면·영역이 담당.
            double DayS(int seg, double u)
            {
                if (!cut)
                {
                    var c = FacePt(seg, u, slopeLen);                  // 크레스트
                    if (!ground.TryGetElevation(c.X, c.Y, out double gc)) return slopeLen;
                    return c.Z > gc + eps ? slopeLen : 0;              // 크레스트가 지반 위면 꽉, 아니면 없음
                }
                for (double s = 0; s <= slopeLen + 1e-9; s += 0.25)     // 절토 daylight
                {
                    var p = FacePt(seg, u, s);
                    if (!ground.TryGetElevation(p.X, p.Y, out double g)) return s;
                    if (p.Z >= g - eps)                                 // 위로 뚫음 = 벽 끝
                    {
                        double lo = System.Math.Max(0, s - 0.25), hi = s;
                        for (int it = 0; it < 18; it++)
                        {
                            double m = (lo + hi) / 2; var pm = FacePt(seg, u, m);
                            ground.TryGetElevation(pm.X, pm.Y, out double gm);
                            if (pm.Z < gm - eps) lo = m; else hi = m;
                        }
                        return (lo + hi) / 2;
                    }
                }
                return slopeLen;
            }
            // 세그먼트 면의 바깥법선 W(평면 법선) — 코너에서 인접 면 평면 절단(미터)에 사용.
            (double x, double y, double z) SegW(int seg)
            {
                var t = walk.Tangent(seg);
                var r = walk.AtSeg(seg, walk.SegStart(seg));           // 그 변의 안쪽법선(nx,ny)
                double ux = t.x, uy = t.y, uz = 0;
                double vx = r.nx * hIn * hs, vy = r.ny * hIn * hs, vz = vUp;
                double wx = uy * vz - uz * vy, wy = uz * vx - ux * vz, wz = ux * vy - uy * vx;
                double wl = System.Math.Sqrt(wx * wx + wy * wy + wz * wz);
                return wl < 1e-9 ? (0, 0, 0) : (wx / wl, wy / wl, wz / wl);
            }

            // [코너에서 벽면 분할 — JACK 0721] 패널이 코너를 가로질러 휘지 않게 모서리(≥25°)에서 끊어 벽면별로 타일링.
            //   각 face는 시작·끝 코너에서 '각도 이등분선(미터)'까지만 남긴다 → 볼록부 겹침 제거·오목부 빈틈 채움.
            var cidx = walk.FindCornerIdx(25.0);
            var faces = new List<(double fs, double fe, int firstSeg, int lastSeg, bool corner)>();
            if (cidx.Count < 2) faces.Add((0, walk.Length, 0, walk.SegN - 1, false));
            else
            {
                cidx.Sort();
                for (int i = 0; i < cidx.Count; i++)
                {
                    int ci = cidx[i], cj = cidx[(i + 1) % cidx.Count];
                    double fs0 = walk.SegStart(ci), fe0 = walk.SegStart(cj); if (fe0 <= fs0) fe0 += walk.Length;
                    faces.Add((fs0, fe0, ci, (cj - 1 + walk.SegN) % walk.SegN, true));
                }
            }

            foreach (var (fs, fe, firstSeg, lastSeg, corner) in faces)
            {
                double faceLen = fe - fs;
                // [코너 미터 — 상대 면 평면 절단, JACK 0722] 각 face를 '인접 면의 평면'에서 자른다 → 두 면이 그
                //   평면 교선에서 **딱 만난다**(액자 모서리). 이등분선 절단은 서로 다르게 기운 두 면의 실제 교선이
                //   아니라 틈/덧방을 유발했음(v4.2~4.7). 상대 평면 절단은 볼록(틈)·오목(겹침) 모두 필러 없이 해결.
                (double x, double y, double z) wPrev = default, wNext = default;
                Point3 Pprev = default, Pnext = default;
                double signS = 0, signE = 0; bool clipS = false, clipE = false; double ext = 0;
                if (corner)
                {
                    int prevSeg = (firstSeg - 1 + walk.SegN) % walk.SegN;
                    int nextSeg = (lastSeg + 1) % walk.SegN;
                    wPrev = SegW(prevSeg); Pprev = FacePt(prevSeg, fs, 0);
                    wNext = SegW(nextSeg); Pnext = FacePt(nextSeg, fe, 0);
                    var refP = FacePt(firstSeg, (fs + fe) / 2, 0);      // 이 face 안쪽 기준점 → keep 부호
                    signS = System.Math.Sign((refP.X - Pprev.X) * wPrev.x + (refP.Y - Pprev.Y) * wPrev.y + (refP.Z - Pprev.Z) * wPrev.z);
                    signE = System.Math.Sign((refP.X - Pnext.X) * wNext.x + (refP.Y - Pnext.Y) * wNext.y + (refP.Z - Pnext.Z) * wNext.z);
                    clipS = signS != 0; clipE = signE != 0;
                    // 코너 채움에 필요한 만큼만 연장(코너 미터점 오프셋 slopeN·step + 반두께 여유). 과다 연장은 완만한 코너에서
                    //   길고 얇은 조각이 멀리 떠버림(JACK 0722 떠있는 객체) → 상한을 둔다.
                    ext = System.Math.Max(0.6, slopeN * step + 0.4);
                }
                int fcols = (int)System.Math.Floor(faceLen / side + 1e-9);
                if (fcols < 1 && faceLen < side * 0.3) continue;
                // 스팬 = (좌 코너필러) + 온전폭 열들 + (우 자투리+코너필러 병합). cf=코너필러(한 조각·줄눈없음·앵커없음).
                //   [JACK 0722] 코너를 얇은 조각(자투리+오버슛+줄눈)으로 쪼개지 말고 면당 **한 조각**으로.
                var spans = new List<(double a, double b, int seg, bool cf)>();
                if (clipS) spans.Add((fs - ext, fs, firstSeg, true));  // 좌측 코너필러(한 조각)
                for (int j = 0; j < fcols; j++)
                {
                    double a = fs + j * side, b = fs + (j + 1) * side;
                    spans.Add((a, b, walk.SegOf((a + b) / 2), false));
                }
                // 우측: 자투리와 우 오버슛을 한 스팬으로 병합(코너면 fe+ext까지, 아니면 fe까지=일반 자투리).
                double rStart = fs + fcols * side, rEnd = clipE ? fe + ext : fe;
                if (rEnd - rStart > 0.05) spans.Add((rStart, rEnd, lastSeg, clipE));
                foreach (var (u0, u1, seg, cf) in spans)
                {
                    double wCol = u1 - u0, uMid = (u0 + u1) / 2;
                    // [데이라잇 다각형 클립 — JACK 0721] 열 폭에 걸쳐 상한 s를 촘촘히 샘플. 한 행 안에서
                    //   실제 데이라잇 선을 따라 삼/사/오/육각형 무엇이든으로 자른다(직선 하나로 가로질러 찢는 문제 해결).
                    int NS = System.Math.Max(2, (int)System.Math.Ceiling(wCol / 0.12));
                    var us = new double[NS + 1];
                    var cap = new double[NS + 1];
                    bool anyCap = false;
                    double fillCap = 0;
                    if (!cut) fillCap = DayS(seg, uMid) > 0 ? slopeLen : 0;     // 성토=열 통째로 꽉 or 없음
                    for (int t = 0; t <= NS; t++)
                    {
                        double uu = u0 + wCol * t / NS;
                        us[t] = uu;
                        cap[t] = cut ? System.Math.Min(slopeLen, DayS(seg, uu)) : fillCap;
                        if (cap[t] > 1e-6) anyCap = true;
                    }
                    if (!anyCap) continue;

                    for (int i = 0; ; i++)
                    {
                        double s0 = i * side, s1 = System.Math.Min(s0 + side, slopeLen);
                        if (s1 <= s0 + 1e-6) break;
                        var localPolys = ClipCurtain(us, cap, u0, s0, s1);
                        if (localPolys.Count == 0) continue;
                        // [정착구 기준 앵커 — JACK 0721] 가운데 200×200 정착구가 데이라잇/단상한에 안 잘리면 앵커·홈.
                        //   정착구 = 셀 중심(uMid, s0+side/2) ±100mm. 상단(s0+side/2+0.1)이 전폭·양끝 모두 데이라잇 아래면 온전.
                        double pocketTop = s0 + side / 2 + 0.1;
                        bool rowFull = wCol >= side - 1e-6 && s1 >= pocketTop - 1e-6
                                       && CapAt(us, cap, uMid) >= pocketTop - 1e-6
                                       && CapAt(us, cap, uMid - 0.1) >= pocketTop - 1e-6
                                       && CapAt(us, cap, uMid + 0.1) >= pocketTop - 1e-6;

                        foreach (var lp0 in localPolys)
                        {
                            // 프레임(seg 평면, flip 전) — 미터 반평면 계수 계산에 필요하므로 먼저 구한다.
                            var org = FacePt(seg, u0, s0);
                            var a1 = FacePt(seg, u1, s0);
                            double ux = a1.X - org.X, uy = a1.Y - org.Y, uz = a1.Z - org.Z;
                            double ull = System.Math.Sqrt(ux * ux + uy * uy + uz * uz); if (ull < 1e-9) continue;
                            ux /= ull; uy /= ull; uz /= ull;
                            var (_, _, _, nx, ny) = walk.AtSeg(seg, uMid);
                            double vx = nx * hIn * hs, vy = ny * hIn * hs, vz = vUp;   // 사면 상방(절토=바깥/성토=안쪽)

                            // ★코너 미터 클립 — 각 face를 '인접 면 평면'에서 자르되 cornerLap(=두께/2)만큼 더 지나서 자른다.
                            //   → 두 면이 코너에서 두께/2씩 겹쳐 **두께가 꽉 찬 모서리**(JACK 0722: 딱 만나는 것보다 반두께 더 나가게).
                            const double cornerLap = 0.10;             // 두께(0.20)의 절반
                            var lp = lp0;
                            if (clipS)
                                lp = ClipHalf(lp,
                                    signS * ((org.X - Pprev.X) * wPrev.x + (org.Y - Pprev.Y) * wPrev.y + (org.Z - Pprev.Z) * wPrev.z) + cornerLap,
                                    signS * (ux * wPrev.x + uy * wPrev.y + uz * wPrev.z),
                                    signS * (vx * wPrev.x + vy * wPrev.y + vz * wPrev.z));
                            if (clipE && lp.Count >= 3)
                                lp = ClipHalf(lp,
                                    signE * ((org.X - Pnext.X) * wNext.x + (org.Y - Pnext.Y) * wNext.y + (org.Z - Pnext.Z) * wNext.z) + cornerLap,
                                    signE * (ux * wNext.x + uy * wNext.y + uz * wNext.z),
                                    signE * (vx * wNext.x + vy * wNext.y + vz * wNext.z));
                            if (lp.Count < 3) continue;
                            // [줄눈 — JACK 0721/0722] 패널 변에서 jm씩 인셋 → 이웃과 joint 폭 틈(InfraWorks 가시성).
                            //   ※코너필러(cf): **세로 줄눈(좌우)은 없애 가로 조각 방지**, **상하 줄눈은 유지**해 블록 높이가 벽과 일치.
                            //     데이라잇 경사변(상단 s1 아래)은 이 클립에 안 걸려 실루엣 유지.
                            if (jm > 1e-4)
                            {
                                double rowH = s1 - s0;
                                if (!cf)
                                {
                                    lp = ClipHalf(lp, -jm, 1, 0);                             // u ≥ jm (좌우 줄눈)
                                    if (lp.Count >= 3) lp = ClipHalf(lp, wCol - jm, -1, 0);   // u ≤ wCol−jm
                                }
                                if (lp.Count >= 3) lp = ClipHalf(lp, -jm, 0, 1);          // v ≥ jm (상하 줄눈 — cf도 적용)
                                if (lp.Count >= 3) lp = ClipHalf(lp, rowH - jm, 0, -1);   // v ≤ rowH−jm
                                if (lp.Count < 3) continue;
                            }

                            double area = PolyArea(lp);
                            double minV = double.MaxValue, maxV = double.MinValue, minU = double.MaxValue, maxU = double.MinValue;
                            foreach (var q in lp) { minV = System.Math.Min(minV, q.v); maxV = System.Math.Max(maxV, q.v); minU = System.Math.Min(minU, q.u); maxU = System.Math.Max(maxU, q.u); }
                            bool isFull = !cf && rowFull && PointInPoly(wCol / 2, side / 2, lp);   // 코너필러엔 앵커/홈 없음
                            if (!isFull)
                            {
                                // ★작은 삼각형도 유지(JACK 0721) — 익스트루드 실패할 만큼 얇은/작은 것만 제거.
                                if (area < sliverMin) continue;
                                if (maxV - minV < edgeMin || maxU - minU < edgeMin) continue;
                            }

                            double wx = uy * vz - uz * vy, wy = uz * vx - ux * vz, wz = ux * vy - uy * vx;
                            double wl = System.Math.Sqrt(wx * wx + wy * wy + wz * wz); if (wl < 1e-9) continue;
                            wx /= wl; wy /= wl; wz /= wl;
                            // ★W(바깥법선)가 '보이는 면=부지 쪽'을 향하게 정렬. 뒤집을 때 U·local.u도 함께 뒤집어 월드점 불변.
                            double padx = cut ? nx : -nx, pady = cut ? ny : -ny;
                            bool flip = wx * padx + wy * pady < 0;
                            var local = new List<(double u, double v)>(lp);
                            if (flip)
                            {
                                for (int t = 0; t < local.Count; t++) local[t] = (-local[t].u, local[t].v);
                                ux = -ux; uy = -uy; uz = -uz; wx = -wx; wy = -wy; wz = -wz;
                            }
                            var poly = new List<Point3>(local.Count);
                            foreach (var (lu, lv) in local)
                                poly.Add(new Point3(org.X + lu * ux + lv * vx, org.Y + lu * uy + lv * vy, org.Z + lu * uz + lv * vz));

                            Point3 center = default, aPos = default;
                            (double x, double y, double z) aDir = default;
                            double pocketU = 0, pocketV = 0;
                            if (isFull)
                            {
                                pocketU = (flip ? -1 : 1) * (wCol / 2); pocketV = side / 2;
                                aPos = new Point3(org.X + pocketU * ux + pocketV * vx,
                                                  org.Y + pocketU * uy + pocketV * vy,
                                                  org.Z + pocketU * uz + pocketV * vz);
                                center = aPos;
                                double ox = cut ? -nx : nx, oy = cut ? -ny : ny;
                                aDir = (ox * System.Math.Cos(aRad), oy * System.Math.Cos(aRad), -System.Math.Sin(aRad));
                                full++;
                            }
                            else part++;
                            result.Add(new Panel(poly, isFull, center, (wx, wy, wz), aPos, aDir,
                                org, (ux, uy, uz), (vx, vy, vz), (wx, wy, wz), local, pocketU, pocketV));
                        }
                    }
                }
            }

        }
        LastDiag = $"패널 {result.Count}(온전 {full}·잘림 {part}) · 앵커 {full} · {(cut ? "절토" : "성토")} 1:{slopeN}";
        return result;
    }

    /// <summary>한 행(row) 사각형 [u0..u1]×[s0..s1]을 데이라잇 상한선(cap(u)) 아래로 클립.
    /// cap을 u에 걸쳐 촘촘히 샘플한 (us,cap)을 받아, 벽 영역(s0≤v≤min(s1,cap), cap≥s0)을 실제 다각형으로 만든다.
    /// 데이라잇이 셀을 대각으로 지나면 오각형·육각형이, 끝만 스치면 삼각형이 자연히 나온다. 로컬 (u−u0, v−s0) 반환.
    /// cap이 s0 아래로 내려가는 구간에서 끊겨 여러 조각이 될 수 있으므로 리스트 반환.</summary>
    private static List<List<(double u, double v)>> ClipCurtain(double[] us, double[] cap, double u0, double s0, double s1)
    {
        var polys = new List<List<(double u, double v)>>();
        int N = us.Length;
        // 샘플 사이에서 cap이 s0을 지나면(교차) 그 지점을 경계점으로 삽입 → 삼각형 꼭지가 깔끔.
        var pts = new List<(double u, double c)>();
        for (int i = 0; i < N; i++)
        {
            if (i > 0)
            {
                double ca = cap[i - 1], cb = cap[i];
                if ((ca - s0) * (cb - s0) < -1e-18)
                {
                    double tt = (s0 - ca) / (cb - ca);
                    pts.Add((us[i - 1] + (us[i] - us[i - 1]) * tt, s0));
                }
            }
            pts.Add((us[i], cap[i]));
        }
        var cur = new List<(double u, double c)>();
        void Flush()
        {
            bool has = false; foreach (var q in cur) if (q.c > s0 + 1e-9) { has = true; break; }
            if (has && cur.Count >= 2)
            {
                double uL = cur[0].u, uR = cur[cur.Count - 1].u;
                var poly = new List<(double u, double v)> { (uL - u0, 0), (uR - u0, 0) };
                for (int j = cur.Count - 1; j >= 0; j--)
                {
                    double v = System.Math.Min(s1, cur[j].c) - s0; if (v < 0) v = 0;
                    poly.Add((cur[j].u - u0, v));
                }
                var cl = Cleanup(poly);
                if (cl.Count >= 3) polys.Add(cl);
            }
            cur.Clear();
        }
        foreach (var p in pts)
        {
            if (p.c <= s0 + 1e-9) { cur.Add(p); Flush(); cur.Add(p); }   // 경계점은 양쪽 런이 공유
            else cur.Add(p);
        }
        Flush();
        return polys;
    }

    /// <summary>중복·공선점 제거해 다각형 정리.</summary>
    private static List<(double u, double v)> Cleanup(List<(double u, double v)> poly)
    {
        var r = new List<(double u, double v)>();
        foreach (var p in poly)
        {
            if (r.Count > 0)
            {
                var last = r[r.Count - 1];
                if (System.Math.Abs(p.u - last.u) < 1e-7 && System.Math.Abs(p.v - last.v) < 1e-7) continue;
            }
            r.Add(p);
        }
        if (r.Count >= 2)
        {
            var f = r[0]; var l = r[r.Count - 1];
            if (System.Math.Abs(f.u - l.u) < 1e-7 && System.Math.Abs(f.v - l.v) < 1e-7) r.RemoveAt(r.Count - 1);
        }
        if (r.Count >= 3)
        {
            var s = new List<(double u, double v)>(); int n = r.Count;
            for (int i = 0; i < n; i++)
            {
                var a = r[(i - 1 + n) % n]; var b = r[i]; var c = r[(i + 1) % n];
                double cx = (b.u - a.u) * (c.v - a.v) - (b.v - a.v) * (c.u - a.u);
                double la = System.Math.Sqrt((c.u - a.u) * (c.u - a.u) + (c.v - a.v) * (c.v - a.v));
                if (la > 1e-9 && System.Math.Abs(cx) / la < 1e-4) continue;   // 공선점 제거
                s.Add(b);
            }
            r = s;
        }
        return r;
    }

    /// <summary>샘플 (us,cap)에서 u 위치의 상한선 값 선형보간.</summary>
    private static double CapAt(double[] us, double[] cap, double u)
    {
        int N = us.Length;
        if (u <= us[0]) return cap[0];
        if (u >= us[N - 1]) return cap[N - 1];
        for (int i = 1; i < N; i++)
            if (u <= us[i])
            {
                double t = (u - us[i - 1]) / (us[i] - us[i - 1]);
                return cap[i - 1] + (cap[i] - cap[i - 1]) * t;
            }
        return cap[N - 1];
    }

    /// <summary>로컬 2D 다각형 면적(㎡).</summary>
    private static double PolyArea(List<(double u, double v)> p)
    {
        double a = 0; for (int i = 0; i < p.Count; i++) { var u = p[i]; var v = p[(i + 1) % p.Count]; a += u.u * v.v - v.u * u.v; }
        return System.Math.Abs(a) / 2;
    }

    /// <summary>다각형을 반평면 A+u·Bu+v·Bv ≥ 0 으로 클립(Sutherland–Hodgman 한 변). 코너 미터에 사용.</summary>
    private static List<(double u, double v)> ClipHalf(List<(double u, double v)> poly, double A, double Bu, double Bv)
    {
        int n = poly.Count;
        if (n == 0) return poly;
        var outp = new List<(double u, double v)>();
        for (int i = 0; i < n; i++)
        {
            var cur = poly[i]; var prv = poly[(i - 1 + n) % n];
            double fc = A + cur.u * Bu + cur.v * Bv;
            double fp = A + prv.u * Bu + prv.v * Bv;
            bool inC = fc >= -1e-9, inP = fp >= -1e-9;
            if (inC)
            {
                if (!inP) { double t = fp / (fp - fc); outp.Add((prv.u + (cur.u - prv.u) * t, prv.v + (cur.v - prv.v) * t)); }
                outp.Add(cur);
            }
            else if (inP) { double t = fp / (fp - fc); outp.Add((prv.u + (cur.u - prv.u) * t, prv.v + (cur.v - prv.v) * t)); }
        }
        return outp;
    }

    /// <summary>점(u,v)이 다각형 내부인지(정착구 온전 판정).</summary>
    private static bool PointInPoly(double u, double v, List<(double u, double v)> poly)
    {
        bool inside = false; int n = poly.Count;
        for (int i = 0, j = n - 1; i < n; j = i++)
        {
            var a = poly[i]; var b = poly[j];
            if (((a.v > v) != (b.v > v)) &&
                (u < (b.u - a.u) * (v - a.v) / (b.v - a.v) + a.u)) inside = !inside;
        }
        return inside;
    }

    /// <summary>닫힌 링 호길이 파라미터화(안쪽 단위법선) — 축소판.</summary>
    private sealed class Walk
    {
        private readonly double[] _cum;
        private readonly (double x, double y, double z)[] _a, _b;
        private readonly (double nx, double ny)[] _n;
        public double Length { get; }
        public bool Ccw { get; }
        public Walk(IReadOnlyList<Point3> ring)
        {
            int n = ring.Count;
            bool dup = n >= 2 &&
                (ring[0].X - ring[n - 1].X) * (ring[0].X - ring[n - 1].X) +
                (ring[0].Y - ring[n - 1].Y) * (ring[0].Y - ring[n - 1].Y) < 1e-12;
            int m = dup ? n - 1 : n;
            double area = 0;
            for (int i = 0; i < m; i++) { var p = ring[i]; var q = ring[(i + 1) % m]; area += p.X * q.Y - q.X * p.Y; }
            bool ccw = area > 0; Ccw = ccw;
            _a = new (double, double, double)[m]; _b = new (double, double, double)[m];
            _n = new (double, double)[m]; _cum = new double[m];
            double acc = 0;
            for (int i = 0; i < m; i++)
            {
                var p = ring[i]; var q = ring[(i + 1) % m];
                _a[i] = (p.X, p.Y, p.Z); _b[i] = (q.X, q.Y, q.Z);
                double dx = q.X - p.X, dy = q.Y - p.Y, len = System.Math.Sqrt(dx * dx + dy * dy);
                _n[i] = len > 1e-12 ? ((ccw ? -dy : dy) / len, (ccw ? dx : -dx) / len) : (0, 0);
                _cum[i] = acc; acc += len;
            }
            Length = acc;
        }
        public (double x, double y, double z, double nx, double ny) At(double u)
        {
            if (Length <= 0) return (_a.Length > 0 ? _a[0].x : 0, _a.Length > 0 ? _a[0].y : 0, 0, 0, 0);
            u %= Length; if (u < 0) u += Length;
            int lo = 0, hi = _cum.Length - 1;
            while (lo < hi) { int mid = (lo + hi + 1) / 2; if (_cum[mid] <= u) lo = mid; else hi = mid - 1; }
            var a = _a[lo]; var b = _b[lo];
            double dx = b.x - a.x, dy = b.y - a.y, len = System.Math.Sqrt(dx * dx + dy * dy);
            double t = len > 1e-12 ? (u - _cum[lo]) / len : 0; if (t > 1) t = 1;
            return (a.x + dx * t, a.y + dy * t, a.z + (b.z - a.z) * t, _n[lo].nx, _n[lo].ny);
        }

        public int SegN => _cum.Length;
        private int Norm(int seg) => ((seg % _cum.Length) + _cum.Length) % _cum.Length;
        public double SegStart(int seg) => _cum[Norm(seg)];

        /// <summary>호길이 u가 속한 세그먼트 인덱스.</summary>
        public int SegOf(double u)
        {
            if (Length <= 0) return 0;
            u %= Length; if (u < 0) u += Length;
            int lo = 0, hi = _cum.Length - 1;
            while (lo < hi) { int mid = (lo + hi + 1) / 2; if (_cum[mid] <= u) lo = mid; else hi = mid - 1; }
            return lo;
        }

        /// <summary>지정 세그먼트의 직선을 호길이 uAbs에서 평가(코너 넘어 연장 가능) + 그 변의 안쪽법선.</summary>
        public (double x, double y, double z, double nx, double ny) AtSeg(int seg, double uAbs)
        {
            seg = Norm(seg);
            var a = _a[seg]; var b = _b[seg];
            double dx = b.x - a.x, dy = b.y - a.y, len = System.Math.Sqrt(dx * dx + dy * dy);
            double t = len > 1e-12 ? (uAbs - _cum[seg]) / len : 0;
            return (a.x + dx * t, a.y + dy * t, a.z + (b.z - a.z) * t, _n[seg].nx, _n[seg].ny);
        }

        /// <summary>세그먼트 단위 접선(진행 방향).</summary>
        public (double x, double y) Tangent(int seg)
        {
            seg = Norm(seg);
            var a = _a[seg]; var b = _b[seg];
            double dx = b.x - a.x, dy = b.y - a.y, len = System.Math.Sqrt(dx * dx + dy * dy);
            return len > 1e-12 ? (dx / len, dy / len) : (0, 0);
        }

        /// <summary>세그먼트 시작 정점(=코너점) 평면 좌표.</summary>
        public (double x, double y) Vertex(int seg) { seg = Norm(seg); return (_a[seg].x, _a[seg].y); }

        /// <summary>모서리(꺾임 ≥ angleDeg) 정점 인덱스 — 여기서 벽면을 끊고 미터한다.</summary>
        public List<int> FindCornerIdx(double angleDeg = 25.0)
        {
            var res = new List<int>();
            double cosT = System.Math.Cos(angleDeg * System.Math.PI / 180.0);
            int m = _cum.Length;
            for (int i = 0; i < m; i++)
            {
                var pa = _a[(i - 1 + m) % m]; var pb = _b[(i - 1 + m) % m];   // 앞 변
                var qa = _a[i]; var qb = _b[i];                                // 뒤 변
                double d1x = pb.x - pa.x, d1y = pb.y - pa.y, l1 = System.Math.Sqrt(d1x * d1x + d1y * d1y);
                double d2x = qb.x - qa.x, d2y = qb.y - qa.y, l2 = System.Math.Sqrt(d2x * d2x + d2y * d2y);
                if (l1 < 1e-9 || l2 < 1e-9) continue;
                double dot = (d1x * d2x + d1y * d2y) / (l1 * l2);
                if (dot < cosT) res.Add(i);                                    // 이 정점이 모서리
            }
            return res;
        }
    }
}
