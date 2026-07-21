namespace DH.Grading.Core;

/// <summary>옹벽 3D 보강토 블록 배치 (옹벽3D_기획.md §2 — 그리드 필터링) — WallLines v2와 같은 제1원리 위에서:
///  · 각 단의 벽 정렬선(링)을 따라 수평 조적조(엇갈림) 가상 그리드를 만들고,
///  · 블록 상면 Z ≤ 상단 커팅라인 Z(=clamp(원지반, 토우, 크레스트), WallLines와 동일)일 때만 블록 채택.
/// 블록은 항상 수평(기울이지 않음) → 사선 구간은 자연히 계단식, 빈틈 없음.
///
/// [우각부 딱 맞춤 — JACK 0720 확정, 반블록 도입]
///  · 링을 모서리(꺾임 ≥30°)에서 벽면(face)으로 분할, 벽면마다 양끝 모서리에 플러시(딱 붙여) 배치.
///  · 홀수층은 반블록(폭 W/2)으로 시작해 엇갈림 230mm 유지 — 실제 보강토 코너 시공과 동일.
///  · 자투리(&lt;W/2)는 그 층 줄눈에 균등 분산(블록당 수 mm — 육안 불가) → 양끝 모두 정확히 플러시.
///  · 뒷물림 시 층별 전면선 교점으로 모서리 시작/끝 스테이션 보정 → 물러난 층도 모서리 칼각.
///  · 인접 두 벽면의 끝블록은 모서리 뒤편(흙 쪽)에서 겹침 — 겉면 칼각, LOD 허용(실시공은 코너 절단).
///
/// 뒷물림(setback)은 정지 구배 n을 따름: 코스 전면이 링에서 안쪽으로 n×|코스상면Z−링Z| (WallLines 경사보정과 동일식).
/// 반환 좌표는 '블록 전면 하단 중앙'(삽입점) + Z축 회전각 — Civil 쪽에서 BlockReference로 삽입.</summary>
public static class WallBlocks
{
    /// <summary>블록 1개 배치. X/Y/Z=전면 하단 중앙(삽입점), RotRad=Z축 회전(블록 로컬 +X=벽 진행방향,
    /// +Y=깊이(배면 흙 방향)), Course=단 내 층 번호(0=최하), Column=벽면·층 내 열 번호, Level=단 크레스트 평균Z,
    /// Ring=링 번호, Face=벽면 번호, S=링 호길이 스테이션(블록 중심), Half=반블록(폭 W/2) 여부,
    /// RX/RY=**링 위 위치**(전면 돌출·뒷물림 적용 전) — 영역 판정은 반드시 이 값으로(아래 FilterByRegions 참조).</summary>
    public readonly record struct Block(
        double X, double Y, double Z, double RotRad, int Course, int Column, double Level, int Ring,
        int Face, double S, bool Half, double RX, double RY, bool Corner = false);

    /// <summary>직전 실행 진단(단별 블록 수) — 로그 표기용.</summary>
    public static string LastDiag { get; private set; } = "";

    /// <param name="rings">GradingGeometry가 재계산한 단 링들(rings[0]=계획 pad, 이후 단 순서) — WallLines와 동일 입력.</param>
    /// <param name="ground">원지반 표고 조회.</param>
    /// <param name="cut">true=절토(정렬선=크레스트 링), false=성토(정렬선=토우 링, 상단=토우+단높이).</param>
    /// <param name="slopeN">벽 구배 n — 뒷물림. 코스 전면을 안쪽으로 n×|코스상면Z−링Z| 이동.</param>
    /// <param name="blockW">블록 전면 폭(m). 원스톤 0.46.</param>
    /// <param name="blockH">블록 높이(m). 원스톤 0.2.</param>
    /// <param name="blockD">블록 깊이(m). 원스톤 0.5 — 벽 중심이 링(지표면 끝선)에 오도록 전면을 D/2 앞으로
    /// 내밈(JACK 0720: 정지면 TIN 벽면과 블록 전면이 같은 평면이면 InfraWorks Z-파이팅 → 앞 절반 돌출로 해소).</param>
    /// <param name="eps">벽 존재 판정 여유(m) — WallLines와 동일(0.02).</param>
    /// <param name="zTol">블록 채택 여유(m) — 코스 상면이 커팅라인을 이만큼 넘어도 허용(수치 노이즈 흡수).</param>
    public static List<Block> Generate(
        IReadOnlyList<IReadOnlyList<Point3>> rings, IGroundSurface ground, bool cut,
        double slopeN = 0.0, double blockW = 0.46, double blockH = 0.2, double blockD = 0.5,
        double eps = 0.02, double zTol = 0.02)
    {
        var result = new List<Block>();
        var diag = new System.Text.StringBuilder();
        if (rings == null || rings.Count < 2 || ground == null || blockW < 1e-3 || blockH < 1e-3)
        { LastDiag = "링/지반/규격 없음"; return result; }

        static double MeanZ(IReadOnlyList<Point3> r)
        { double s = 0; foreach (var p in r) s += p.Z; return s / System.Math.Max(r.Count, 1); }

        double halfW = blockW * 0.5;
        // 전면 돌출: 벽 중심(D/2)이 링에 오도록 전면선을 '전면 방향'으로 D/2 이동.
        // 절토 전면=안쪽(+안쪽법선) → +D/2, 성토 전면=바깥(−안쪽법선) → −D/2. 뒷물림 off와 같은 축이라 합산.
        double frontShift = blockD * 0.5 * (cut ? 1.0 : -1.0);

        for (int k = 1; k < rings.Count; k++)
        {
            var ring = rings[k];
            if (ring == null || ring.Count < 3) continue;
            double step = System.Math.Abs(MeanZ(ring) - MeanZ(rings[k - 1])); // 이 벽의 단높이(마지막 잔여단 포함)
            if (step < blockH - 1e-9) continue;                               // 블록 한 층도 못 들어가는 잔여단
            int courses = (int)System.Math.Floor(step / blockH + 1e-6);       // 정수 층수(잔여는 캡 콘크리트가 흡수)

            var walk = new RingWalk(ring);
            if (walk.Length < blockW) continue;
            double level = MeanZ(ring);
            int count0 = result.Count, half0 = 0;

            // ── 벽면 분할: 모서리(꺾임 ≥30°, 0.3m 룩어헤드)에서 링을 끊음 — 곡선(완만)은 벽면 내부로 연속. ──
            var cornerSta = walk.FindCorners(30.0, 0.3, 0.8);
            var faces = BuildFaces(walk, cornerSta);

            for (int f = 0; f < faces.Count; f++)
            {
                var face = faces[f];
                for (int c = 0; c < courses; c++)
                {
                    // 뒷물림량(층별, 위치 무관): 절토 |상면−크레스트| = step−(c+1)H, 성토 = (c+1)H.
                    // + 전면 돌출(frontShift, 부호 포함) — 층별 전면선의 안쪽법선 방향 총 이동량.
                    double off = (slopeN > 1e-9
                        ? slopeN * (cut ? (step - (c + 1) * blockH) : (c + 1) * blockH)
                        : 0.0) + frontShift;

                    // 이 층의 배치 구간: 모서리 전면선 교점 보정(플러시). 코너 없는 닫힌 링은 보정 0.
                    double s0 = face.Start + face.StartDeltaUnit * off;
                    double s1 = face.End + face.EndDeltaUnit * off;
                    double len = s1 - s0;
                    if (len < halfW - 1e-9) continue;

                    // ── 조적조 구성: [홀수층 선두 반블록] + 통블록 n + [자투리 ≥ W/2면 꼬리 반블록] ──
                    //    잔여(<W/2)는 줄눈에 균등 분산 → 양끝 모두 정확히 플러시(모서리 칼각).
                    var widths = new List<double>();
                    if (face.Flush && c % 2 == 1) widths.Add(halfW);          // 엇갈림 230 유지(코너 반블록)
                    double rem = len - (widths.Count > 0 ? halfW : 0);
                    int nFull = (int)System.Math.Floor(rem / blockW + 1e-9);
                    for (int i = 0; i < nFull; i++) widths.Add(blockW);
                    rem -= nFull * blockW;
                    if (face.Flush && rem >= halfW - 1e-9) { widths.Add(halfW); rem -= halfW; }
                    if (widths.Count == 0) continue;

                    // 코너 없는 닫힌 링(원형 등): 플러시 개념 없음 — 기존처럼 통블록만, 잔여는 이음새 틈.
                    double gap = face.Flush && widths.Count > 1 ? rem / (widths.Count - 1) : 0.0;
                    double cursor = s0 + (face.Flush ? (widths.Count == 1 ? rem * 0.5 : 0.0)
                                                     : (c % 2 == 1 ? halfW : 0.0));

                    int col = 0;
                    foreach (double w in widths)
                    {
                        double station = cursor + w * 0.5;
                        cursor += w + gap;
                        if (!face.Flush && station + w * 0.5 > face.End + 1e-9) break; // 닫힌 링 이음새 걸침 제외
                        // 벽면 범위 밖(오목 코너 보정으로 교점이 넘어갈 때)은 링을 따라 꺾지 말고 벽면 직선
                        // 연장선으로 — At()은 링을 랩해 다음 벽면으로 돌아가므로 이웃 벽 블록과 겹침(치명).
                        var (x, y, ringZ, nx, ny) = SampleFace(walk, face, station);
                        if (!ground.TryGetElevation(x, y, out double g)) { col++; continue; }

                        double toe, crest;
                        if (cut) { crest = ringZ; toe = ringZ - step; }
                        else { toe = ringZ; crest = ringZ + step; }
                        // 벽 존재(WallLines와 동일 판정): 절토=지반이 토우 위, 성토=크레스트가 지반 위.
                        if (cut ? (g - toe <= eps) : (crest - g <= eps)) { col++; continue; }

                        double zBottom = toe + c * blockH;
                        double zTopC = zBottom + blockH;
                        // 그리드 필터 핵심: 코스 상면 ≤ 상단 커팅라인(절토=clamp(지반,토우,크레스트), 성토=크레스트).
                        double topLine = cut ? System.Math.Min(crest, System.Math.Max(toe, g)) : crest;
                        if (zTopC > topLine + zTol) { col++; continue; }

                        // 뒷물림(링Z 국소값으로 재계산해 기존 동작 보존) + 전면 돌출 — 안쪽법선 방향 합산 이동.
                        double offB = (slopeN > 1e-9 ? slopeN * System.Math.Abs(zTopC - ringZ) : 0.0) + frontShift;
                        double bx = x + nx * offB, by = y + ny * offB;
                        // 깊이(배면 흙) 방향: 절토=바깥(−안쪽법선), 성토=안쪽(+안쪽법선). 로컬 +Y=깊이가 되는 회전각.
                        double dxDepth = cut ? -nx : nx, dyDepth = cut ? -ny : ny;
                        double rot = System.Math.Atan2(-dxDepth, dyDepth); // Xaxis=(dyDepth,−dxDepth) → atan2(Xy, Xx)
                        bool isHalf = w < blockW * 0.75;
                        if (isHalf) half0++;
                        // 영역 판정용 링 위치 — 스테이션을 벽면 구간으로 클램프해 **항상 링 위의 점**이 되게 한다.
                        // (코너 보정으로 구간을 넘어간 블록은 그 코너점으로 대표 — 블록 규격과 무관해야 하므로.)
                        var rp = walk.At(face.Flush ? System.Math.Clamp(station, face.Start, face.End) : station);
                        result.Add(new Block(bx, by, zBottom, rot, c, col++, level, k, f, station, isHalf, rp.x, rp.y));
                    }
                }
            }

            // ── 코너 채움 블록(§37, JACK 0721 위에서 본 슬릿) — 두 벽면이 앞 꼭짓점 P에서만 만나 뒤 사분면이
            //    비는 '뒤 쐐기' 코너(절토=볼록/성토=오목)에, P 뒤쪽 흙 사분면에만 D×D 코너블록을 세운다.
            //    앞면(=두 벽면 전면선)과 정확히 플러시라 앞으로 안 튀어나온다(오목/L자엔 생성 안 함). ──
            int cornerN = 0;
            foreach (double cs in cornerSta)
            {
                var (u1x, u1y, n1x, n1y) = walk.ChordDir(cs - 0.3, cs - 0.01);
                var (u2x, u2y, n2x, n2y) = walk.ChordDir(cs + 0.01, cs + 0.3);
                double cross = u1x * u2y - u1y * u2x;                 // 진행방향 외적 → 볼록/오목
                bool convex = (walk.Ccw ? cross : -cross) > 0;
                if (cut ? !convex : convex) continue;                 // 뒤 쐐기 코너만(절토=볼록·성토=오목)
                var cp = walk.At(cs);
                double bx0 = n1x + n2x, by0 = n1y + n2y;              // 안쪽 이등분(정규화 전)
                if (System.Math.Sqrt(bx0 * bx0 + by0 * by0) < 1e-6) continue; // 180° 반전 코너 — 건너뜀

                for (int c = 0; c < courses; c++)
                {
                    double off = (slopeN > 1e-9 ? slopeN * (cut ? (step - (c + 1) * blockH) : (c + 1) * blockH) : 0.0) + frontShift;
                    // 두 전면선(cp+n·off, 방향 u)의 교점 P.
                    double a1x = cp.x + n1x * off, a1y = cp.y + n1y * off;
                    double a2x = cp.x + n2x * off, a2y = cp.y + n2y * off;
                    double det = u1x * (-u2y) - u1y * (-u2x);
                    if (System.Math.Abs(det) < 1e-9) continue;
                    double tt = ((a2x - a1x) * (-u2y) - (a2y - a1y) * (-u2x)) / det;
                    double px = a1x + u1x * tt, py = a1y + u1y * tt; // 앞 꼭짓점 P

                    // 벽 존재·높이(면 블록과 동일 판정, 코너점 기준).
                    if (!ground.TryGetElevation(cp.x, cp.y, out double g)) continue;
                    double toe, crest;
                    if (cut) { crest = cp.z; toe = cp.z - step; } else { toe = cp.z; crest = cp.z + step; }
                    if (cut ? (g - toe <= eps) : (crest - g <= eps)) continue;
                    double zTopC = toe + (c + 1) * blockH;
                    double topLine = cut ? System.Math.Min(crest, System.Math.Max(toe, g)) : crest;
                    if (zTopC > topLine + zTol) continue;

                    // 뒤 사분면 중심 = P에서 '링 코너(cp) 쪽'으로 D/√2(=흙 쪽 대각). 방향을 링 코너로 잡아야
                    // 절토(전면 안쪽=cp가 바깥)·성토(전면 바깥=cp가 안쪽) 모두 흙 쪽으로 간다(고정 −이등분이면
                    // 성토에서 반대로 바깥 돌출=W 계단, JACK 0721). 정사각 D×D가 두 전면선 직각 사분면을 덮고,
                    // 회전=면 방향(u1)에 정렬 → 두 변이 전면선과 평행(플러시·무돌출). 90° 완전 채움.
                    double wdx = cp.x - px, wdy = cp.y - py, wl = System.Math.Sqrt(wdx * wdx + wdy * wdy);
                    if (wl < 1e-9) continue;                          // P≈cp(오프셋 0) — 쐐기 없음
                    double back = blockD / System.Math.Sqrt(2.0);
                    double ccx = px + wdx / wl * back, ccy = py + wdy / wl * back;
                    double rot = System.Math.Atan2(u1y, u1x);        // 로컬 +X = 면1 진행방향
                    result.Add(new Block(ccx, ccy, toe + c * blockH, rot, c, -1, level, k, -1,
                        cs, false, cp.x, cp.y, Corner: true));
                    cornerN++;
                }
            }

            diag.Append($"링{k}(≈{level:F1}, {courses}층·벽면{faces.Count}): {result.Count - count0}개(반 {half0}·코너 {cornerN}) · ");
        }
        LastDiag = diag.Length > 0 ? diag.ToString() : "블록 없음";
        return result;
    }

    /// <summary>벽면 위 station 표본 — 벽면 내부는 링을 따르되, 관통/보정으로 [Start,End] 밖이면
    /// 벽면 끝점에서 그 방향으로 직선 연장(링 따라 꺾이면 이웃 벽 블록과 겹침 → 치명). 법선은 그 끝점 값 유지.</summary>
    private static (double x, double y, double z, double nx, double ny) SampleFace(
        RingWalk walk, FaceRun face, double station)
    {
        if (station >= face.Start - 1e-9 && station <= face.End + 1e-9)
            return walk.At(station);
        if (station < face.Start)
        {
            var p = walk.At(face.Start);
            var (ux, uy, _, _) = walk.ChordDir(face.Start + 0.005, face.Start + 0.3);
            double e = station - face.Start;                          // 음수 — 시작 이전으로 역연장
            return (p.x + ux * e, p.y + uy * e, p.z, p.nx, p.ny);
        }
        else
        {   // face.End는 다음 벽면이 시작하는 스테이션 — At()이 다음 변의 법선을 주므로 살짝 앞에서 표본.
            const double back = 1e-4;
            var p = walk.At(face.End - back);
            var (ux, uy, _, _) = walk.ChordDir(face.End - 0.3, face.End - 0.005);
            double e = station - (face.End - back);                  // 양수 — 끝 이후로 연장
            return (p.x + ux * e, p.y + uy * e, p.z, p.nx, p.ny);
        }
    }

    /// <summary>벽면 1개 — 링 호길이 [Start, End] 구간(End는 Start보다 크며 링 길이를 넘어 랩 가능).
    /// Flush=양끝이 모서리(플러시 배치 대상). *DeltaUnit=뒷물림 단위량(off=1)당 시작/끝 스테이션 보정
    /// (전면 오프셋 선 교점 — 층마다 off를 곱해 사용).</summary>
    private readonly record struct FaceRun(
        double Start, double End, bool Flush, double StartDeltaUnit, double EndDeltaUnit);

    private static List<FaceRun> BuildFaces(RingWalk walk, List<double> corners)
    {
        var faces = new List<FaceRun>();
        if (corners.Count == 0)
        {   // 모서리 없는 닫힌 링(원형/타원 파드) — 기존 연속 배치 유지(이음새 1곳 허용).
            faces.Add(new FaceRun(0, walk.Length, false, 0, 0));
            return faces;
        }
        // 각 모서리의 전면선 교점 단위보정(a=앞 벽면 끝 보정, b=뒷 벽면 시작 보정) 사전 계산.
        var deltas = new (double A, double B)[corners.Count];
        for (int i = 0; i < corners.Count; i++) deltas[i] = CornerDeltas(walk, corners[i]);

        for (int i = 0; i < corners.Count; i++)
        {
            int j = (i + 1) % corners.Count;
            double s = corners[i];
            double e = corners[j] > corners[i] + 1e-9 ? corners[j] : corners[j] + walk.Length;
            faces.Add(new FaceRun(s, e, true, deltas[i].B, deltas[j].A));
        }
        return faces;
    }

    /// <summary>모서리 C에서 앞 벽면(f, C에서 끝남)·뒷 벽면(g, C에서 시작) 전면선을 안쪽으로 1만큼
    /// 평행이동했을 때 교점의 스테이션 보정 (a=f 끝, b=g 시작). C + a·u_f + n_f = C + b·u_g + n_g 를 풂.
    /// 근평행(모서리 아님)이면 0.</summary>
    private static (double A, double B) CornerDeltas(RingWalk walk, double cornerSta)
    {
        const double look = 0.3;
        var (ufx, ufy, nfx, nfy) = walk.ChordDir(cornerSta - look, cornerSta - 0.01);
        var (ugx, ugy, ngx, ngy) = walk.ChordDir(cornerSta + 0.01, cornerSta + look);
        double rx = ngx - nfx, ry = ngy - nfy;                       // off=1일 때 우변
        double det = -ufx * ugy + ugx * ufy;                          // [u_f  −u_g] 행렬식
        if (System.Math.Abs(det) < 0.1) return (0, 0);                // 꺾임 ≈ 0 — 보정 불필요
        double a = (-rx * ugy + ugx * ry) / det;
        double b = (ufx * ry - ufy * rx) / det;
        return (a, b);
    }

    /// <summary>캡블록 배치(옹벽3D_기획 §4 수정 — JACK 0720: 캡블록 개별, 460×300×100) — 블록 상면 중
    /// **노출된 구간마다** 캡을 얹는다. 온전히 노출되면 통캡, 반 칸만 노출되면 그 자리에 반캡.
    ///
    /// [부분 노출 처리 — JACK 0720 '절토부 캡 누락'] 예전엔 위층 블록과 조금이라도 겹치면 캡을 통째로
    /// 생략했다. 조적조는 반 칸 엇갈리므로 **계단(지표 절단) 단차마다 위층이 정확히 반만 덮어** 노출된
    /// 반 칸이 맨살로 남았다(계단 코마다 캡 1장씩 빠짐). 이제 위층 블록 구간을 빼고 남은 구간을 계산해
    /// 반 칸이면 반캡으로 메운다 — 캡-블록 충돌은 여전히 0(노출 구간에만 놓으므로).
    /// 반환 Block: X/Y=노출 구간 중심(벽 진행방향으로 이동), Z=아래 블록 상면, Course=아래 블록 층+1.</summary>
    public static List<Block> GenerateCaps(List<Block> blocks, double blockH = 0.2, double blockW = 0.46)
    {
        var caps = new List<Block>();
        if (blocks.Count == 0) return caps;
        double halfW = blockW * 0.5;
        var above = blocks.ToLookup(b => (b.Ring, b.Face, b.Course));
        foreach (var b in blocks)
        {
            if (b.Corner) continue;                                   // 코너 채움 블록은 캡 대상 아님(정사각 포스트)
            double wb = b.Half ? halfW : blockW;
            // 이 블록 상면 구간에서 위층 블록이 덮는 부분을 빼 '노출 구간'만 남긴다.
            var free = new List<(double A, double B)> { (b.S - wb * 0.5, b.S + wb * 0.5) };
            foreach (var a in above[(b.Ring, b.Face, b.Course + 1)])
            {
                double wa = a.Half ? halfW : blockW;
                double aLo = a.S - wa * 0.5, aHi = a.S + wa * 0.5;
                var next = new List<(double A, double B)>(free.Count + 1);
                foreach (var (s, e) in free)
                {
                    if (aHi <= s + 1e-9 || aLo >= e - 1e-9) { next.Add((s, e)); continue; } // 안 겹침
                    if (aLo > s + 1e-9) next.Add((s, aLo));
                    if (aHi < e - 1e-9) next.Add((aHi, e));
                }
                free = next;
            }
            double ux = System.Math.Cos(b.RotRad), uy = System.Math.Sin(b.RotRad); // 벽 진행방향(로컬 +X)
            foreach (var (s, e) in free)
            {
                double len = e - s;
                if (len < halfW * 0.8) continue;              // 캡 한 장도 못 놓는 자투리
                bool half = len < blockW * 0.8;               // 반 칸 노출 → 반캡
                double mid = (s + e) * 0.5, d = mid - b.S;
                caps.Add(b with
                {
                    X = b.X + ux * d,
                    Y = b.Y + uy * d,
                    Z = b.Z + blockH,
                    Course = b.Course + 1,
                    S = mid,
                    Half = half,
                    RX = b.RX + ux * d,
                    RY = b.RY + uy * d,
                });
            }
        }
        return caps;
    }

    /// <summary>[WallLines.FilterByRegions와 동일 취지] 계획무관 고립 포켓의 블록 제외 —
    /// **링 위 위치(RX/RY)**가 '계획관련 순수교선 링(regions)' 중 하나의 내부(±buffer)면 유지.
    ///
    /// [★삽입점(X/Y)으로 판정하면 안 되는 이유 — JACK 0720 '중간중간 빠진 블록'의 실제 원인]
    /// 삽입점은 전면 돌출(D/2)·뒷물림만큼 링에서 떨어져 있다. 성토는 전면이 **바깥**이라 삽입점이 순수교선
    /// (=성토 daylight, 곧 부지 최외곽) 밖으로 나가 buffer를 넘고, 최외곽 단에서 무더기 탈락했다
    /// (현장 로그 실측: 성토 18285개 중 **4010개 제외**, 절토는 0개 — 안쪽으로 물러나므로).
    /// 영역 판정의 질문은 "이 벽이 계획 구역의 벽인가"이므로 **블록 제작 오프셋과 무관한 링 위치**로 물어야 한다.</summary>
    /// <param name="dropped">영역 밖으로 판정해 제외한 블록 수(진단용).</param>
    public static List<Block> FilterByRegions(
        List<Block> blocks, IReadOnlyList<IReadOnlyList<Point3>>? regions, double buffer, out int dropped)
    {
        dropped = 0;
        if (regions == null || regions.Count == 0 || blocks.Count == 0) return blocks;
        var kept = new List<Block>(blocks.Count);
        foreach (var b in blocks)
            if (InsideAny(b.RX, b.RY, regions, buffer)) kept.Add(b); else dropped++;
        return kept;
    }

    private static bool InsideAny(double x, double y, IReadOnlyList<IReadOnlyList<Point3>> regions, double buffer)
    {
        foreach (var r in regions)
        {
            int n = r.Count; if (n < 3) continue;
            bool inside = false; bool nearEdge = false; double bd2 = buffer * buffer;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                double xi = r[i].X, yi = r[i].Y, xj = r[j].X, yj = r[j].Y;
                if ((yi > y) != (yj > y) && x < (xj - xi) * (y - yi) / (yj - yi) + xi) inside = !inside;
                double vx = xj - xi, vy = yj - yi, l2 = vx * vx + vy * vy;
                double t = l2 < 1e-12 ? 0 : ((x - xi) * vx + (y - yi) * vy) / l2;
                t = t < 0 ? 0 : (t > 1 ? 1 : t);
                double qx = xi + t * vx, qy = yi + t * vy;
                if ((x - qx) * (x - qx) + (y - qy) * (y - qy) <= bd2) nearEdge = true;
            }
            if (inside || nearEdge) return true;
        }
        return false;
    }

    /// <summary>닫힌 링의 호길이 파라미터화 — 임의 station의 (x, y, 링Z, 안쪽 단위법선).
    /// 법선은 변 단위(블록이 놓인 변의 법선) — 코너에 걸친 블록은 중심이 속한 변을 따름.
    /// station은 링 길이로 랩(모듈로) — 음수/초과 허용.</summary>
    private sealed class RingWalk
    {
        private readonly double[] _cum;                    // 변 시작 누적길이
        private readonly (double x, double y, double z)[] _a, _b;
        private readonly (double nx, double ny)[] _n;
        private readonly bool _ccw;
        public bool Ccw => _ccw;
        public double Length { get; }

        public RingWalk(IReadOnlyList<Point3> ring)
        {
            int n = ring.Count;
            bool dup = n >= 2 &&
                (ring[0].X - ring[n - 1].X) * (ring[0].X - ring[n - 1].X) +
                (ring[0].Y - ring[n - 1].Y) * (ring[0].Y - ring[n - 1].Y) < 1e-12;
            int m = dup ? n - 1 : n;
            double area = 0;
            for (int i = 0; i < m; i++)
            { var p = ring[i]; var q = ring[(i + 1) % m]; area += p.X * q.Y - q.X * p.Y; }
            _ccw = area > 0;
            _a = new (double, double, double)[m]; _b = new (double, double, double)[m];
            _n = new (double, double)[m]; _cum = new double[m];
            double acc = 0;
            for (int i = 0; i < m; i++)
            {
                var p = ring[i]; var q = ring[(i + 1) % m];
                _a[i] = (p.X, p.Y, p.Z); _b[i] = (q.X, q.Y, q.Z);
                double dx = q.X - p.X, dy = q.Y - p.Y;
                double len = System.Math.Sqrt(dx * dx + dy * dy);
                _n[i] = len > 1e-12
                    ? ((_ccw ? -dy : dy) / len, (_ccw ? dx : -dx) / len)
                    : (0, 0);
                _cum[i] = acc; acc += len;
            }
            Length = acc;
        }

        private double Wrap(double station)
        {
            if (Length <= 0) return 0;
            station %= Length;
            return station < 0 ? station + Length : station;
        }

        public (double x, double y, double z, double nx, double ny) At(double station)
        {
            station = Wrap(station);
            int lo = 0, hi = _cum.Length - 1;
            while (lo < hi)                                // 마지막 _cum ≤ station 변 탐색
            {
                int mid = (lo + hi + 1) / 2;
                if (_cum[mid] <= station) lo = mid; else hi = mid - 1;
            }
            var a = _a[lo]; var b = _b[lo];
            double dx = b.x - a.x, dy = b.y - a.y;
            double len = System.Math.Sqrt(dx * dx + dy * dy);
            double t = len > 1e-12 ? (station - _cum[lo]) / len : 0;
            if (t > 1) t = 1;
            return (a.x + dx * t, a.y + dy * t, a.z + (b.z - a.z) * t, _n[lo].nx, _n[lo].ny);
        }

        /// <summary>[from→to] 현(chord) 방향 단위벡터와 그 안쪽 법선 — 모서리 앞뒤의 대표 방향
        /// (미세 변 노이즈를 룩어헤드 거리로 평균).</summary>
        public (double ux, double uy, double nx, double ny) ChordDir(double from, double to)
        {
            var p = At(from); var q = At(to);
            double dx = q.x - p.x, dy = q.y - p.y;
            double len = System.Math.Sqrt(dx * dx + dy * dy);
            if (len < 1e-12) return (1, 0, 0, _ccw ? 1 : -1);
            double ux = dx / len, uy = dy / len;
            return _ccw ? (ux, uy, -uy, ux) : (ux, uy, uy, -ux);
        }

        /// <summary>모서리 탐지 — 각 꼭짓점에서 룩어헤드(look) 현 방향의 꺾임각 ≥ angleDeg면 모서리.
        /// dedup 거리 내 중복은 꺾임 큰 것 하나만(촘촘한 링에서 한 모서리가 여러 점에 걸릴 때).</summary>
        public List<double> FindCorners(double angleDeg, double look, double dedup)
        {
            var cand = new List<(double Sta, double Ang)>();
            double cosT = System.Math.Cos(angleDeg * System.Math.PI / 180.0);
            for (int i = 0; i < _cum.Length; i++)
            {
                double sv = _cum[i];
                var (u1x, u1y, _, _) = ChordDir(sv - look, sv - 0.005);
                var (u2x, u2y, _, _) = ChordDir(sv + 0.005, sv + look);
                double dot = u1x * u2x + u1y * u2y;
                if (dot < cosT) cand.Add((sv, System.Math.Acos(System.Math.Clamp(dot, -1, 1))));
            }
            if (cand.Count == 0) return new List<double>();
            cand.Sort((p, q) => p.Sta.CompareTo(q.Sta));
            // dedup: 인접(랩 포함) dedup 거리 내 그룹에서 최대 꺾임만.
            var keep = new List<(double Sta, double Ang)>();
            foreach (var c in cand)
            {
                if (keep.Count > 0 && c.Sta - keep[^1].Sta < dedup)
                { if (c.Ang > keep[^1].Ang) keep[^1] = c; }
                else keep.Add(c);
            }
            if (keep.Count > 1 && keep[0].Sta + Length - keep[^1].Sta < dedup)
            {
                if (keep[^1].Ang > keep[0].Ang) keep[0] = keep[^1];
                keep.RemoveAt(keep.Count - 1);
            }
            return keep.Select(kv => kv.Sta).ToList();
        }
    }
}
