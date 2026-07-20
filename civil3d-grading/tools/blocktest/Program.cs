// WallBlocks 오프라인 하네스 — 옹벽 3D 보강토 블록 그리드 필터링 + 우각부 반블록 플러시 검증
// (walltest와 같은 PASS/FAIL 방식)
using DH.Grading.Core;

int fails = 0;
void Check(string name, bool ok, string detail = "")
{
    Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {name} {detail}");
    if (!ok) fails++;
}

const double W = 0.46, H = 0.2, STEP = 5.0, HW = W / 2;
const double D = 0.5, FS = D / 2; // 깊이·전면 돌출(벽 중심=링, JACK 0720 Z-파이팅 해소): 절토 +FS(안쪽), 성토 −FS

// 40×40 정사각 크레스트 링(z=105), 아래 단(pad) 링 z=100 — 절토 1개 단.
static List<Point3> Square(double z) => new()
{
    new Point3(0, 0, z), new Point3(40, 0, z), new Point3(40, 40, z), new Point3(0, 40, z),
};
var rings = new List<IReadOnlyList<Point3>> { Square(100), Square(105) };

// 벽면별·층별 기대 유닛 수(플러시 조성 재현): 배치길이 len = 40 − 2×(층별 전면선 이동량).
static int UnitsForLen(double len, bool odd)
{
    const double W = 0.46, HW = W / 2;
    double rem = len - (odd ? HW : 0);
    if (rem < -1e-9) return 0;
    int nFull = (int)Math.Floor(rem / W + 1e-9);
    rem -= nFull * W;
    return (odd ? 1 : 0) + nFull + (rem >= HW - 1e-9 ? 1 : 0);
}
// S1(절토 slopeN=0): 전층 len = 40−2FS = 39.5 → 짝수층 85F+1H=86, 홀수층 1H+85F=86.
int unitsPerFace = UnitsForLen(40 - 2 * FS, false), unitsPerCourse = 4 * unitsPerFace;
int courses = (int)Math.Floor(STEP / H + 1e-6); // 25

// 블록 전면 양끝 X (y=0 벽면: 진행 +x, 폭 방향 = ±x)
static (double A, double B) EndsX(WallBlocks.Block b, double w) => (b.X - w / 2, b.X + w / 2);
double WidthOf(WallBlocks.Block b) => b.Half ? HW : W;

// ── S1: 평탄 고지반(전절토, 지반 110 ≥ 크레스트) — 전 층·전 열 블록, 캡=최상층 전체 ──
{
    var g = new FlatGround(110);
    var blocks = WallBlocks.Generate(rings, g, cut: true, slopeN: 0, blockW: W, blockH: H);
    Check("S1 블록수 = 층×유닛", blocks.Count == courses * unitsPerCourse,
        $"{blocks.Count} (기대 {courses * unitsPerCourse})");
    Check("S1 층수 25", blocks.Max(b => b.Course) == courses - 1, $"max층 {blocks.Max(b => b.Course)}");
    bool zGrid = blocks.All(b => Math.Abs((b.Z - 100.0) / H - Math.Round((b.Z - 100.0) / H)) < 1e-9);
    Check("S1 블록 z=수평 격자", zGrid);
    Check("S1 상면 ≤ 크레스트", blocks.All(b => b.Z + H <= 105 + 1e-9));
    // 반블록 수: 짝수층 4면×1(꼬리) + 홀수층 4면×1(선두) = 층당 4 → 100
    Check("S1 반블록 층당 4", blocks.Count(b => b.Half) == courses * 4,
        $"{blocks.Count(b => b.Half)} (기대 {courses * 4})");
    // ★ 전면 돌출(절토, slopeN=0): y=0 벽면 블록 삽입점 y = FS(0.25) — 벽 중심이 링 위
    Check("S1 ★전면 돌출 = D/2", blocks.Any(b => Math.Abs(b.Y - FS) < 1e-9 && b.X > 1 && b.X < 39));
    // 엇갈림: y=FS 벽면 중앙부 통블록 줄눈이 위아래 층에서 ≈W/2 어긋남(줄눈 분산 ±0.02 허용)
    var c0 = blocks.Where(b => b.Course == 0 && !b.Half && Math.Abs(b.Y - FS) < 0.3 && b.X > 1 && b.X < 39).OrderBy(b => b.X).First();
    var c1 = blocks.Where(b => b.Course == 1 && !b.Half && Math.Abs(b.Y - FS) < 0.3 && b.X > c0.X - 0.01).OrderBy(b => b.X).First();
    Check("S1 엇갈림 ≈반블록", Math.Abs(Math.Abs(c1.X - c0.X) - HW) < 0.02, $"ΔX {c1.X - c0.X:F3}");
    var caps = WallBlocks.GenerateCaps(blocks, H, W);
    Check("S1 캡 = 최상층 전체", caps.Count == unitsPerCourse, $"{caps.Count} (기대 {unitsPerCourse})");
    Check("S1 캡 z = 크레스트", caps.All(c => Math.Abs(c.Z - 105) < 1e-9));
    Check("S1 반캡 = 최상층 반블록 4", caps.Count(c => c.Half) == 4, $"{caps.Count(c => c.Half)}");

    // ★ 우각부 플러시: 전면선이 FS만큼 안쪽 → 전 층 y=FS 벽면 전면 양끝이 [FS, 40−FS]에 딱 닿음
    bool noProtrude = true, flushEvery = true;
    for (int c = 0; c < courses; c++)
    {
        var face = blocks.Where(b => b.Course == c && Math.Abs(b.Y - FS) < 0.01).ToList();
        double lo = face.Min(b => EndsX(b, WidthOf(b)).A), hi = face.Max(b => EndsX(b, WidthOf(b)).B);
        if (lo < FS - 1e-6 || hi > 40 - FS + 1e-6) noProtrude = false;
        if (Math.Abs(lo - FS) > 1e-6 || Math.Abs(hi - (40 - FS)) > 1e-6) flushEvery = false;
    }
    Check("S1 ★모서리 무돌출(전층)", noProtrude);
    Check("S1 ★모서리 플러시(전층 FS·40−FS 정확)", flushEvery);
    // 홀수층 선두 = 반블록(엇갈림이 코너 반블록에서 시작)
    var oddFirst = blocks.Where(b => b.Course == 1 && Math.Abs(b.Y - FS) < 0.01).OrderBy(b => b.X).First();
    Check("S1 홀수층 선두 반블록", oddFirst.Half, $"X {oddFirst.X:F3}");
    // 줄눈 분산 gap ≤ 3mm(87유닛, 잔여 0.21/86)
    var evenFace = blocks.Where(b => b.Course == 0 && Math.Abs(b.Y - FS) < 0.01).OrderBy(b => b.X).ToList();
    double maxGap = 0;
    for (int i = 1; i < evenFace.Count; i++)
        maxGap = Math.Max(maxGap, EndsX(evenFace[i], WidthOf(evenFace[i])).A - EndsX(evenFace[i - 1], WidthOf(evenFace[i - 1])).B);
    Check("S1 줄눈 분산 ≤ 3mm", maxGap <= 0.003 + 1e-9, $"max {maxGap * 1000:F1}mm");
}

// ── S2: 사선 지반(x 방향 상승 102→112) — 계단식, 커팅라인 준수, 캡-블록 무충돌 ──
{
    var g = new SlopeGround(102, 0.25); // g = 102 + 0.25x → x=0: 102(중간), x=40: 112(전고)
    var blocks = WallBlocks.Generate(rings, g, cut: true, slopeN: 0, blockW: W, blockH: H);
    Check("S2 블록 존재", blocks.Count > 0, $"{blocks.Count}개");
    bool under = true;
    foreach (var b in blocks)
    {
        g.TryGetElevation(b.X, b.Y, out double gz);
        double top = Math.Min(105, Math.Max(100, gz));
        // 여유 0.1 = 필터 zTol(0.02) + 전면 돌출 FS×경사(0.25×0.25=0.0625, 지반은 링 위치에서 샘플됨)
        if (b.Z + H > top + 0.1 + 1e-9) { under = false; break; }
    }
    Check("S2 상면 ≤ 커팅라인", under);
    // 계단식: 바닥변(y=0)에서 열별 최고층이 x 증가에 따라 단조증가(지반 상승 방향)
    var bottom = blocks.Where(b => Math.Abs(b.Y) < 0.3).GroupBy(b => (b.Face, b.Column))
        .Select(gr => (TopC: gr.Max(b => b.Course), X: gr.First().X)).OrderBy(t => t.X).ToList();
    bool mono = true;
    for (int i = 1; i < bottom.Count; i++) if (bottom[i].TopC < bottom[i - 1].TopC) { mono = false; break; }
    Check("S2 계단 단조증가(y=0변)", mono, $"열 {bottom.Count}개");
    // 캡: 같은 벽면 위층 블록과 스테이션 겹침 없음
    var caps = WallBlocks.GenerateCaps(blocks, H, W);
    var occ = blocks.ToLookup(b => (b.Ring, b.Face, b.Course));
    bool collide = false;
    foreach (var c in caps)
        foreach (var b in occ[(c.Ring, c.Face, c.Course)]) // 캡 Course = 위층 번호
            if (Math.Abs(b.S - c.S) < (WidthOf(b) + WidthOf(c)) / 2 - 0.021) { collide = true; break; }
    Check("S2 캡-블록 무충돌", !collide, $"캡 {caps.Count}개");
    Check("S2 캡 존재", caps.Count > 0);
}

// ── S3: 지반이 토우 아래(99) — 벽 없음 → 블록 0 ──
{
    var blocks = WallBlocks.Generate(rings, new FlatGround(99), cut: true, slopeN: 0, blockW: W, blockH: H);
    Check("S3 벽 없음 → 블록 0", blocks.Count == 0, $"{blocks.Count}개");
}

// ── S4: 뒷물림(slopeN=0.05) — 아래층일수록 안쪽 + 모서리도 층별 플러시 ──
{
    var g = new FlatGround(110);
    var blocks = WallBlocks.Generate(rings, g, cut: true, slopeN: 0.05, blockW: W, blockH: H);
    // 바닥변(안쪽법선=+y): 0층 y = n×(step−H)+FS = 0.49, 최상층(24) y = 0+FS = 0.25
    var y0 = blocks.Where(b => b.Course == 0 && b.Y > -0.1 && b.Y < 0.8 && b.X > 5 && b.X < 35).Select(b => b.Y).FirstOrDefault(-1);
    var yTop = blocks.Where(b => b.Course == 24 && b.Y > -0.1 && b.Y < 0.8 && b.X > 5 && b.X < 35).Select(b => b.Y).FirstOrDefault(-1);
    Check("S4 0층 안쪽 0.24+FS", Math.Abs(y0 - (0.05 * (STEP - H) + FS)) < 1e-6, $"y0 {y0:F3}");
    Check("S4 최상층 링+FS", Math.Abs(yTop - FS) < 1e-6, $"yTop {yTop:F3}");
    // ★ 뒷물림 모서리 정합: 층 c 전면선은 y=off(c)+FS, 전면 양끝은 [off, 40−off] — 층별 오프셋 사각형에 플러시
    bool flushOff = true;
    for (int c = 0; c < courses; c++)
    {
        double off = 0.05 * (STEP - (c + 1) * H) + FS;
        var face = blocks.Where(b => b.Course == c && Math.Abs(b.Y - off) < 0.01).ToList();
        if (face.Count == 0) { flushOff = false; break; }
        double lo = face.Min(b => EndsX(b, WidthOf(b)).A), hi = face.Max(b => EndsX(b, WidthOf(b)).B);
        if (Math.Abs(lo - off) > 1e-6 || Math.Abs(hi - (40 - off)) > 1e-6) { flushOff = false; break; }
    }
    Check("S4 ★뒷물림 모서리 플러시(전층)", flushOff);
}

// ── S5: 성토(정렬선=토우 링 z=100, 크레스트 105, 지반 99 — 전면 노출) ──
{
    var fillRings = new List<IReadOnlyList<Point3>> { Square(105), Square(100) }; // rings[0]=pad(위), rings[1]=토우 링
    var blocks = WallBlocks.Generate(fillRings, new FlatGround(99), cut: false, slopeN: 0.05, blockW: W, blockH: H);
    // 성토 전면 이동 = 뒷물림(+안쪽) − FS(전면은 바깥) → off(c) = n×(c+1)H − FS. 층별 배치길이 40−2off.
    int expected = 0;
    for (int c = 0; c < courses; c++)
        expected += 4 * UnitsForLen(40 - 2 * (0.05 * (c + 1) * H - FS), c % 2 == 1);
    Check("S5 성토 블록수 = 층별 유닛합", blocks.Count == expected, $"{blocks.Count} (기대 {expected})");
    Check("S5 바닥 z = 토우(100)", Math.Abs(blocks.Min(b => b.Z) - 100) < 1e-9);
    // 뒷물림−FS: 바닥변에서 0층 y = n×H − FS = −0.24(링 밖 돌출), 최상층 y = n×step − FS = 0
    var y0 = blocks.Where(b => b.Course == 0 && b.Y > -0.5 && b.Y < 0.5 && b.X > 5 && b.X < 35).Select(b => b.Y).FirstOrDefault(-1);
    var yTop = blocks.Where(b => b.Course == courses - 1 && b.Y > -0.5 && b.Y < 0.5 && b.X > 5 && b.X < 35).Select(b => b.Y).FirstOrDefault(-1);
    Check("S5 성토 0층 y=n×H−FS", Math.Abs(y0 - (0.05 * H - FS)) < 1e-6, $"y0 {y0:F3}");
    Check("S5 성토 최상층 y=n×step−FS", Math.Abs(yTop - (0.05 * STEP - FS)) < 1e-6, $"yTop {yTop:F3}");
    // ★ S5b(§27, JACK 실측 모서리 반블록 빈틈 — 성토 코너 플러시는 그동안 무검증이었음):
    //   벽면 f·층 c의 배치 구간은 링 스테이션 [40f+off, 40(f+1)−off] (성토 off<0 = 링 밖으로 연장).
    //   ※좌표(Y) 대신 Face/S로 판정 — 성토는 코너에서 이웃 벽면이 서로 넘어와 좌표 필터가 섞임.
    bool fillFlush = true; double worstF = 0;
    foreach (var grp in blocks.GroupBy(b => (b.Face, b.Course)))
    {
        double off = 0.05 * (grp.Key.Course + 1) * H - FS;
        double s0 = 40.0 * grp.Key.Face + off, s1 = 40.0 * (grp.Key.Face + 1) - off;
        double lo = grp.Min(b => b.S - WidthOf(b) / 2), hi = grp.Max(b => b.S + WidthOf(b) / 2);
        worstF = Math.Max(worstF, Math.Max(Math.Abs(lo - s0), Math.Abs(hi - s1)));
        if (Math.Abs(lo - s0) > 1e-6 || Math.Abs(hi - s1) > 1e-6) fillFlush = false;
    }
    Check("S5b ★성토 모서리 플러시(전 벽면·전층)", fillFlush, fillFlush ? "" : $"최대 어긋남 {worstF * 1000:F0}mm");
    var buried = WallBlocks.Generate(fillRings, new FlatGround(106), cut: false, slopeN: 0, blockW: W, blockH: H);
    Check("S5 매몰 → 블록 0", buried.Count == 0, $"{buried.Count}개");
}

// ── S6: 영역 필터 — 작은 사각 영역 안의 블록만 유지 ──
{
    var g = new FlatGround(110);
    var blocks = WallBlocks.Generate(rings, g, cut: true, slopeN: 0, blockW: W, blockH: H);
    var region = new List<IReadOnlyList<Point3>>
    { new List<Point3> { new(-1, -1, 0), new(10, -1, 0), new(10, 10, 0), new(-1, 10, 0) } };
    var kept = WallBlocks.FilterByRegions(blocks, region, 0.3, out int dropped);
    Check("S6 영역필터 축소", kept.Count > 0 && kept.Count < blocks.Count, $"{kept.Count}/{blocks.Count}");
    Check("S6 유지블록 영역 내", kept.All(b => b.X <= 10.4 && b.Y <= 10.4));
    Check("S6 제외수 = 생성−유지", dropped == blocks.Count - kept.Count, $"제외 {dropped}");

    // ★ S6b(§28, JACK '중간중간 빠진 블록'의 실제 원인): 영역 판정은 **링 위치** 기준이어야 하며,
    //   블록 제작 오프셋(전면 돌출 D/2·뒷물림)에 좌우되면 안 된다. 현장에서 성토 4010개가 이렇게 탈락했다.
    //   region을 링과 정확히 같게 두고(=성토 daylight가 최외곽인 실제 상황) 깊이를 키워도 탈락 0이어야 함.
    {
        var fillRings = new List<IReadOnlyList<Point3>> { Square(105), Square(100) };
        var self = new List<IReadOnlyList<Point3>> { Square(0) };   // 링과 동일한 평면 영역
        foreach (double dd in new[] { 0.5, 1.0, 2.0 })              // 깊이가 커져도 판정 불변이어야
        {
            var fb = WallBlocks.Generate(fillRings, new FlatGround(99), cut: false, slopeN: 0.05,
                blockW: W, blockH: H, blockD: dd);
            WallBlocks.FilterByRegions(fb, self, 0.3, out int drop);
            Check($"S6b ★성토 영역필터 탈락 0 (깊이 {dd:F1}m)", drop == 0, $"탈락 {drop}");
        }
        // 링 위치는 반드시 링 위(사각형 경계)의 점이어야 — 코너 넘어감이 남으면 영역 밖으로 새어나간다.
        var a2 = WallBlocks.Generate(fillRings, new FlatGround(99), cut: false, slopeN: 0.05,
            blockW: W, blockH: H, blockD: 2.0);
        bool onRing = a2.All(b => b.RX >= -1e-9 && b.RX <= 40 + 1e-9 && b.RY >= -1e-9 && b.RY <= 40 + 1e-9);
        Check("S6b 링 위치는 항상 링 위(깊이 2m에서도)", onRing);
    }
}

// ── S7: 회전각 — 바닥변(y=0, 진행 +x, 절토: 깊이=바깥(−y)) → 로컬 +Y=(0,−1) → rot=180° ──
{
    var g = new FlatGround(110);
    var blocks = WallBlocks.Generate(rings, g, cut: true, slopeN: 0, blockW: W, blockH: H);
    var b0 = blocks.First(b => Math.Abs(b.Y - FS) < 0.01 && b.X > 5 && b.X < 35);
    Check("S7 절토 y=0변 rot=π", Math.Abs(Math.Abs(b0.RotRad) - Math.PI) < 1e-6, $"rot {b0.RotRad:F3}");
}

// ── S8: L자(오목 코너 포함) — 전면 무돌출: 어떤 블록 전면 끝도 링 전면선 밖으로 안 나감 ──
{
    static List<Point3> LShape(double z) => new()
    {
        new Point3(0, 0, z), new Point3(40, 0, z), new Point3(40, 20, z),
        new Point3(20, 20, z), new Point3(20, 40, z), new Point3(0, 40, z),
    };
    var lRings = new List<IReadOnlyList<Point3>> { LShape(100), LShape(105) };
    var blocks = WallBlocks.Generate(lRings, new FlatGround(110), cut: true, slopeN: 0, blockW: W, blockH: H);
    Check("S8 L자 블록 존재", blocks.Count > 0, $"{blocks.Count}개");
    // 전면 끝점이 L 폴리곤 경계 밖(바깥쪽)으로 tol 이상 벗어나지 않는지 — 우각부 삐져나옴 검출.
    // 전면 끝점 = 삽입점 ± (폭/2)·진행방향. 진행방향 = rot로부터 (깊이 = 로컬+Y = 바깥이므로 X축 = 깊이에 수직).
    bool ok = true; double worst = 0;
    foreach (var b in blocks)
    {
        double w = WidthOf(b);
        // 로컬 X축(진행방향) = (cos rot, sin rot)
        double ux = Math.Cos(b.RotRad), uy = Math.Sin(b.RotRad);
        foreach (double s in new[] { -w / 2, w / 2 })
        {
            double px = b.X + ux * s, py = b.Y + uy * s;
            // L 폴리곤 내부(전면선 위 포함)여야 함 — 절토 전면은 링 안쪽. 바깥 돌출 = 오염.
            double d = SignedOut(px, py);
            if (d > 1e-6) { ok = false; worst = Math.Max(worst, d); }
        }
    }
    Check("S8 ★L자 전면 무돌출(오목 포함)", ok, ok ? "" : $"최대 돌출 {worst * 1000:F1}mm");

    static double SignedOut(double x, double y)
    {   // L 영역: [0,40]×[0,20] ∪ [0,20]×[0,40] — 밖이면 경계까지 거리(근사), 안이면 0
        bool inA = x >= -1e-9 && x <= 40 + 1e-9 && y >= -1e-9 && y <= 20 + 1e-9;
        bool inB = x >= -1e-9 && x <= 20 + 1e-9 && y >= -1e-9 && y <= 40 + 1e-9;
        if (inA || inB) return 0;
        double dA = Math.Max(Math.Max(-x, x - 40), Math.Max(-y, y - 20));
        double dB = Math.Max(Math.Max(-x, x - 20), Math.Max(-y, y - 40));
        return Math.Min(dA, dB);
    }
}

// ── S9(§27): 실제 링처럼 '촘촘히 샘플된(densify)' 경계 — JACK 실측 모서리 빈틈 재현 시도 ──
//    실사이트 링은 0.485m 간격 점열 + 코너각 88.6°(직각 아님). 이상적 4점 사각형만 검증돼 있었음.
{
    static List<Point3> Densify(IReadOnlyList<(double X, double Y)> poly, double z, double step)
    {
        var outp = new List<Point3>();
        for (int i = 0; i < poly.Count; i++)
        {
            var a = poly[i]; var b = poly[(i + 1) % poly.Count];
            double dx = b.X - a.X, dy = b.Y - a.Y, len = Math.Sqrt(dx * dx + dy * dy);
            int n = Math.Max(1, (int)Math.Floor(len / step));
            for (int k = 0; k < n; k++)
            {
                double t = k / (double)n;
                outp.Add(new Point3(a.X + dx * t, a.Y + dy * t, z));
            }
        }
        return outp;
    }
    // 실사이트와 같은 88.6° 기울기(한 변이 1.4° 기움) 사각형
    var poly = new (double X, double Y)[] { (0, 0), (40, 0), (40.98, 40), (0.98, 40) };
    var dRings = new List<IReadOnlyList<Point3>>
    { Densify(poly, 105, 0.485), Densify(poly, 100, 0.485) };   // 성토: rings[0]=pad(위), [1]=토우
    var bl = WallBlocks.Generate(dRings, new FlatGround(99), cut: false, slopeN: 0.05, blockW: W, blockH: H);
    Check("S9 densify 링 블록 생성", bl.Count > 0, $"{bl.Count}개");
    int faceCount = bl.Select(b => b.Face).Distinct().Count();
    Check("S9 벽면 4개 검출", faceCount == 4, $"검출 {faceCount}");
    // 벽면별·층별로 이웃 벽면과 모서리에서 끊김(빈틈) 없이 이어지는지 — 각 벽면 구간이 링 전체를 덮어야.
    //   성토는 코너에서 서로 넘어오므로 '벽면 끝 ≥ 다음 벽면 시작'(겹침 허용, 틈 금지)으로 판정.
    bool noCornerGap = true; double worstGap = 0;
    foreach (var cg in bl.GroupBy(b => b.Course))
    {
        var byFace = cg.GroupBy(b => b.Face)
            .Select(g2 => (Face: g2.Key, Lo: g2.Min(b => b.S - WidthOf(b) / 2), Hi: g2.Max(b => b.S + WidthOf(b) / 2)))
            .OrderBy(t => t.Lo).ToList();
        for (int i = 1; i < byFace.Count; i++)
        {
            double gapLen = byFace[i].Lo - byFace[i - 1].Hi;   // >0 이면 모서리 빈틈
            if (gapLen > 1e-6) { noCornerGap = false; worstGap = Math.Max(worstGap, gapLen); }
        }
    }
    Check("S9 ★모서리 빈틈 없음(densify)", noCornerGap, noCornerGap ? "" : $"최대 빈틈 {worstGap * 1000:F0}mm");
}

Console.WriteLine(fails == 0 ? "\n== 전부 통과 ==" : $"\n== 실패 {fails}건 ==");
return fails == 0 ? 0 : 1;

sealed class FlatGround(double z) : IGroundSurface
{
    public bool TryGetElevation(double x, double y, out double zz) { zz = z; return true; }
}

sealed class SlopeGround(double z0, double kx) : IGroundSurface
{
    public bool TryGetElevation(double x, double y, out double zz) { zz = z0 + kx * x; return true; }
}
