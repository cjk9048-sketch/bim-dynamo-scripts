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

// §37 코너 채움 블록은 별도 범주 — 면 블록 불변식(개수·플러시·무돌출) 검사에서는 제외한다.
static List<WallBlocks.Block> Faces(List<WallBlocks.Block> bl) => bl.Where(b => !b.Corner).ToList();

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
    var blocks = Faces(WallBlocks.Generate(rings, g, cut: true, slopeN: 0, blockW: W, blockH: H));
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
    var blocks = Faces(WallBlocks.Generate(rings, g, cut: true, slopeN: 0, blockW: W, blockH: H));
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
    // 캡: 같은 벽면 위층 블록과 스테이션 구간이 겹치지 않아야(노출 구간에만 놓이므로)
    var caps = WallBlocks.GenerateCaps(blocks, H, W);
    var occ = blocks.ToLookup(b => (b.Ring, b.Face, b.Course));
    bool collide = false; double worstOv = 0;
    foreach (var c in caps)
        foreach (var b in occ[(c.Ring, c.Face, c.Course)]) // 캡 Course = 위층 번호
        {
            double ov = (WidthOf(b) + WidthOf(c)) / 2 - Math.Abs(b.S - c.S);
            if (ov > 1e-6) { collide = true; worstOv = Math.Max(worstOv, ov); }
        }
    Check("S2 캡-블록 무충돌", !collide, collide ? $"겹침 {worstOv * 1000:F0}mm" : $"캡 {caps.Count}개");
    Check("S2 캡 존재", caps.Count > 0);

    // ★ S2b(§29, JACK '절토부 캡 누락'): 계단 단차마다 위층이 반만 덮어 노출된 반 칸에도 캡이 있어야 한다.
    //   판정 = 모든 블록의 상면 노출 구간이 (캡 ∪ 위층 블록)으로 빠짐없이 덮이는가.
    {
        double tol = 0.01; bool allCovered = true; double worstBare = 0;
        var capsBy = caps.ToLookup(c => (c.Ring, c.Face, c.Course));
        foreach (var b in blocks)
        {
            double wb = WidthOf(b);
            var free = new List<(double A, double B)> { (b.S - wb / 2, b.S + wb / 2) };
            foreach (var o in occ[(b.Ring, b.Face, b.Course + 1)].Concat(capsBy[(b.Ring, b.Face, b.Course + 1)]))
            {
                double wo = WidthOf(o), oLo = o.S - wo / 2, oHi = o.S + wo / 2;
                var next = new List<(double A, double B)>();
                foreach (var (s, e) in free)
                {
                    if (oHi <= s + 1e-9 || oLo >= e - 1e-9) { next.Add((s, e)); continue; }
                    if (oLo > s + 1e-9) next.Add((s, oLo));
                    if (oHi < e - 1e-9) next.Add((oHi, e));
                }
                free = next;
            }
            foreach (var (s, e) in free)
                if (e - s > W * 0.4 + tol) { allCovered = false; worstBare = Math.Max(worstBare, e - s); }
        }
        Check("S2b ★계단 단차 캡 누락 없음", allCovered,
            allCovered ? "" : $"맨살 최대 {worstBare * 1000:F0}mm");
    }
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
    var blocks = Faces(WallBlocks.Generate(lRings, new FlatGround(110), cut: true, slopeN: 0, blockW: W, blockH: H));
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

// ── S10(§36): 코너 앞모서리 연속성 — 인접 두 벽면 블록의 앞면 끝이 코너에서 맞물리는가(JACK 실측 갭). ──
//    실사이트처럼 densify(0.485m)한 정사각형. 절토(전면 돌출) + 뒷물림. 층마다 벽면0↔벽면1 코너 앞면 갭 측정.
{
    static List<Point3> DensifySq(double s, double z, double step)
    {
        var pts = new (double X, double Y)[] { (0, 0), (s, 0), (s, s), (0, s) };
        var o = new List<Point3>();
        for (int i = 0; i < 4; i++)
        {
            var a = pts[i]; var b = pts[(i + 1) % 4];
            double dx = b.X - a.X, dy = b.Y - a.Y, len = Math.Sqrt(dx * dx + dy * dy);
            int n = Math.Max(1, (int)Math.Floor(len / step));
            for (int k = 0; k < n; k++) { double t = k / (double)n; o.Add(new Point3(a.X + dx * t, a.Y + dy * t, z)); }
        }
        return o;
    }
    var sqRings = new List<IReadOnlyList<Point3>> { DensifySq(40, 100, 0.485), DensifySq(40, 105, 0.485) };
    var bl = Faces(WallBlocks.Generate(sqRings, new FlatGround(110), cut: true, slopeN: 0.05, blockW: W, blockH: H, blockD: D));
    // 각 층에서 인접 벽면 쌍의 코너 앞모서리 갭 최댓값
    double worst = 0; int badCourses = 0;
    foreach (var cg in bl.GroupBy(b => b.Course))
    {
        var faces = cg.GroupBy(b => b.Face).Where(g => g.Count() >= 2).ToDictionary(g => g.Key, g => g.OrderBy(b => b.S).ToList());
        foreach (var fk in faces.Keys)
        {
            int nf = (fk + 1) % 4;                              // 정사각형 벽면 0→1→2→3
            if (!faces.ContainsKey(nf)) continue;
            var f1 = faces[fk]; var f2 = faces[nf];
            var (aex, aey) = EndCorner(f1[^1], f1[^2]);         // 벽면 fk 마지막 블록의 진행쪽(코너) 앞모서리
            var (bsx, bsy) = StartCorner(f2[0], f2[1]);         // 다음 벽면 첫 블록의 시작쪽(코너) 앞모서리
            double gap = Math.Sqrt((aex - bsx) * (aex - bsx) + (aey - bsy) * (aey - bsy));
            if (gap > 0.05) { badCourses++; worst = Math.Max(worst, gap); }
        }
    }
    Check("S10 ★코너 앞모서리 연속(갭 없음)", badCourses == 0, badCourses == 0 ? "" : $"{badCourses}층 갭, 최대 {worst * 1000:F0}mm");

    static double Half(WallBlocks.Block b) => (b.Half ? W * 0.5 : W) * 0.5;
    // 마지막 블록 b·직전 p: 진행방향 = b−p, 코너 앞모서리 = b + 진행·half
    static (double, double) EndCorner(WallBlocks.Block b, WallBlocks.Block p)
    {
        double dx = b.X - p.X, dy = b.Y - p.Y, l = Math.Sqrt(dx * dx + dy * dy);
        if (l < 1e-9) return (b.X, b.Y);
        return (b.X + dx / l * Half(b), b.Y + dy / l * Half(b));
    }
    // 첫 블록 b·다음 n: 진행방향 = n−b, 시작 앞모서리 = b − 진행·half
    static (double, double) StartCorner(WallBlocks.Block b, WallBlocks.Block n)
    {
        double dx = n.X - b.X, dy = n.Y - b.Y, l = Math.Sqrt(dx * dx + dy * dy);
        if (l < 1e-9) return (b.X, b.Y);
        return (b.X - dx / l * Half(b), b.Y - dy / l * Half(b));
    }
}

// ── S11(§36): 실제 벽 링(GradingGeometry.Build) 코너 갭 재현 — JACK 실측 boundary 사용 ──
{
    // 실측 성토 boundary(8점, 90° 직각들). Z는 평탄(코너 XY 기하만 관심).
    var bnd = new List<Point3> {
        new(240344.743,450456.946,100), new(240346.319,450392.337,100),
        new(240304.897,450392.323,100), new(240304.897,450458.594,100),
        new(240281.147,450458.432,100), new(240280.951,450487.073,100),
        new(240326.249,450487.073,100), new(240326.249,450456.946,100),
    };
    var pr = new GradingParams {
        CutBenchHeight = 5, FillBenchHeight = 5, CutBenchWidth = 1, FillBenchWidth = 1,
        CutSlope = 0.05, FillSlope = 0.05,
        CellSize = 0.5, MaxBenches = 50, VertexSpacing = 1.0, MinSlope = 0.05,
        MinFaceRun = 0.005, MiterConvex = true, MiterLimit = 2.0,
    };
    var vs = WallBlocks_TryBuild(bnd, pr, true, out string err);   // 절토(up)
    if (vs == null) { Check("S11 링 생성", false, err); }
    else
    {
        Check("S11 벽 링 생성", vs.Count >= 2, $"링 {vs.Count}");
        var bl = Faces(WallBlocks.Generate(vs, new FlatGround(200), cut: true, slopeN: 0.05,
            blockW: W, blockH: H, blockD: D));
        // 실제 코너별 앞모서리 갭 — 벽면 경계(Face 바뀌는 지점)에서 이웃 끝블록끼리
        double worst = 0; int bad = 0;
        foreach (var cg in bl.Where(b => b.Ring == 1).GroupBy(b => b.Course))
        {
            var byFace = cg.GroupBy(b => b.Face).Where(g => g.Count() >= 2)
                .OrderBy(g => g.Key).Select(g => g.OrderBy(b => b.S).ToList()).ToList();
            for (int i = 0; i < byFace.Count; i++)
            {
                var cur = byFace[i]; var nxt = byFace[(i + 1) % byFace.Count];
                double dx = cur[^1].X - cur[^2].X, dy = cur[^1].Y - cur[^2].Y, l = Math.Sqrt(dx * dx + dy * dy);
                double hc = (cur[^1].Half ? W / 2 : W) / 2;
                double aex = cur[^1].X + (l > 1e-9 ? dx / l : 0) * hc, aey = cur[^1].Y + (l > 1e-9 ? dy / l : 0) * hc;
                double ex = nxt[1].X - nxt[0].X, ey = nxt[1].Y - nxt[0].Y, m2 = Math.Sqrt(ex * ex + ey * ey);
                double hn = (nxt[0].Half ? W / 2 : W) / 2;
                double bsx = nxt[0].X - (m2 > 1e-9 ? ex / m2 : 0) * hn, bsy = nxt[0].Y - (m2 > 1e-9 ? ey / m2 : 0) * hn;
                double gap = Math.Sqrt((aex - bsx) * (aex - bsx) + (aey - bsy) * (aey - bsy));
                if (gap > 0.05) { bad++; worst = Math.Max(worst, gap); }
            }
        }
        Check("S11 ★실제 링 코너 갭 없음", bad == 0, bad == 0 ? "" : $"{bad}건 갭, 최대 {worst * 1000:F0}mm");
    }
}

// ── S12(§37): 코너 채움 블록 — 뒤 사분면 슬릿을 메우되 앞면 돌출 없음 (정사각 90° 코너) ──
{
    static List<Point3> Sq(double s, double z) => new()
    { new(0, 0, z), new(s, 0, z), new(s, s, z), new(0, s, z) };
    var s12r = new List<IReadOnlyList<Point3>> { Sq(40, 100), Sq(40, 105) };
    var bl = WallBlocks.Generate(s12r, new FlatGround(110), cut: true, slopeN: 0.05, blockW: W, blockH: H, blockD: D);
    var corners = bl.Where(b => b.Corner).ToList();
    Check("S12 코너블록 생성(볼록 4×25층)", corners.Count == 4 * 25, $"{corners.Count} (기대 100)");
    // 코너블록은 뒤 사분면(흙 쪽)에 있어야 — 절토 코너(예: (40,0))에서 중심이 부지 안(x<40,y>0)이 아니라
    // 링 바깥쪽(뒤)에 위치. 코너 (40,0): 뒤=+x,−y. 중심 x>40 또는 y<0 근처.
    var c400 = corners.Where(b => b.Course == 12 && b.X > 39 && b.Y < 1).OrderBy(b => b.Y).FirstOrDefault();
    Check("S12 코너 (40,0) 블록 존재", c400.Corner, $"X={c400.X:F2} Y={c400.Y:F2}");
    // ★ 발자국(footprint) 검사: 코너 (40,0) 층12. 전면선 face1: y=P_y(≈0.25−setback보정), face2: x=P_x.
    //   코너블록 D×D의 4모서리가 전부 두 전면선의 '뒤(흙)' 쪽이어야 함(무돌출) + 사분면을 덮어야 함(무갭).
    {
        // 이 코너 face1=바닥변(y=0, 진행 −x, 안쪽법선 +y), face2=우변(x=40, 진행 +y, 안쪽법선 −x).
        // 층12 off = 0.05*(5−13*0.2)+FS = 0.05*2.4+0.25 = 0.37. P=(40−0.37, 0+0.37)=(39.63, 0.37).
        double off = 0.05 * (STEP - 13 * H) + FS;
        double px = 40 - off, py = 0 + off;
        double ux = Math.Cos(c400.RotRad), uy = Math.Sin(c400.RotRad);  // 로컬 +X
        double vx = -uy, vy = ux;                                        // 로컬 +Y
        // 이 코너의 쐐기 방향 = 앞꼭짓점 P에서 링 코너(40,0) 쪽. 무돌출: 어떤 모서리도 두 전면선 앞(pad)으로
        //   안 나감. 무갭: 한 모서리가 P에 닿아 슬릿을 막고, 대각 모서리가 쐐기 D√2 깊이까지 도달.
        bool noProt = true; double worstP = 0, nearP = 9, farP = 0;
        for (int sx = -1; sx <= 1; sx += 2)
            for (int sy = -1; sy <= 1; sy += 2)
            {
                double cx = c400.X + ux * (sx * D / 2) + vx * (sy * D / 2);
                double cy = c400.Y + uy * (sx * D / 2) + vy * (sy * D / 2);
                double d1 = cy - py;    // face1 앞(pad, y>py)이면 돌출
                double d2 = px - cx;    // face2 앞(pad, x<px)이면 돌출
                if (d1 > 1e-6) { noProt = false; worstP = Math.Max(worstP, d1); }
                if (d2 > 1e-6) { noProt = false; worstP = Math.Max(worstP, d2); }
                double dP = Math.Sqrt((cx - px) * (cx - px) + (cy - py) * (cy - py));
                nearP = Math.Min(nearP, dP); farP = Math.Max(farP, dP);
            }
        Check("S12 ★코너블록 무돌출(4모서리 뒤)", noProt, noProt ? "" : $"돌출 {worstP * 1000:F0}mm");
        Check("S12 ★코너블록 P에 닿음(슬릿막음)", nearP < 0.05, $"최근접 {nearP * 1000:F0}mm");
        Check("S12 ★코너블록 쐐기깊이 D√2 도달", farP > D * 1.4142 - 0.05, $"최원 {farP:F2}m");
    }

    // 실제 boundary(90° 직각) — 코너블록이 생기고, 그 중심들이 전부 링 근처(부지 급이탈 없음)
    var bnd = new List<Point3> {
        new(240344.743,450456.946,100), new(240346.319,450392.337,100),
        new(240304.897,450392.323,100), new(240304.897,450458.594,100),
        new(240281.147,450458.432,100), new(240280.951,450487.073,100),
        new(240326.249,450487.073,100), new(240326.249,450456.946,100) };
    var pr = new GradingParams { CutBenchHeight = 5, FillBenchHeight = 5, CutBenchWidth = 1, FillBenchWidth = 1,
        CutSlope = 0.05, FillSlope = 0.05,
        CellSize = 0.5, MaxBenches = 50, VertexSpacing = 1.0, MinSlope = 0.05, MinFaceRun = 0.005, MiterConvex = true, MiterLimit = 2.0 };
    var vs = GradingGeometry.Build(bnd, new FlatGround(200), pr, true);
    var rl = vs.Rings.Select(r => (IReadOnlyList<Point3>)r).ToList();
    var rb = WallBlocks.Generate(rl, new FlatGround(200), cut: true, slopeN: 0.05, blockW: W, blockH: H, blockD: D);
    Check("S12 실제링 코너블록 생성됨", rb.Any(b => b.Corner), $"코너 {rb.Count(b => b.Corner)}개");

    // ★ 성토(오목 코너) — L자 성토에서 오목 코너에만 코너블록이 생기고, 링에서 크게 안 벗어남(대략 D 이내).
    static List<Point3> LShape(double z) => new()
    { new(0,0,z), new(40,0,z), new(40,20,z), new(20,20,z), new(20,40,z), new(0,40,z) };
    var lFill = new List<IReadOnlyList<Point3>> { LShape(105), LShape(100) }; // 성토: pad(위)·토우
    var fb = WallBlocks.Generate(lFill, new FlatGround(99), cut: false, slopeN: 0.05, blockW: W, blockH: H, blockD: D);
    var fc = fb.Where(b => b.Corner).ToList();
    Check("S12 성토 L자 코너블록 생성", fc.Count > 0, $"코너 {fc.Count}개");
    // 오목 코너(20,20) 부근에만 — 볼록(예: (40,0))엔 없어야(성토는 오목이 뒤 쐐기)
    bool atConcave = fc.All(b => Math.Abs(b.RX - 20) < 2 && Math.Abs(b.RY - 20) < 2);
    Check("S12 성토 코너블록=오목(20,20)만", atConcave, atConcave ? "" : "볼록에도 생성됨");
    // 링에서 과이탈 없음(중심이 오목 코너 ±(D+여유))
    bool nearRing = fc.All(b => Math.Sqrt((b.X - 20) * (b.X - 20) + (b.Y - 20) * (b.Y - 20)) < D + 0.6);
    Check("S12 성토 코너블록 링 근처(무과이탈)", nearRing);

    // ★ 성토 오목 코너 앞면 무돌출 — 코너블록이 인접 면 블록보다 '바깥(계획경계 밖)'으로 안 나가야 함.
    //   L자 오목 코너(20,20)에서 fill 벽은 pad 바깥(계획폴리곤 밖)으로 나감. 코너블록이 그보다 더 나가면 W 계단.
    //   면 블록의 최대 바깥 이탈(중심이 링에서 떨어진 거리)과 코너블록의 이탈을 비교.
    {
        var faceB = fb.Where(b => !b.Corner && b.Course == 10).ToList();
        var cornB = fb.Where(b => b.Corner && b.Course == 10).ToList();
        // 오목 코너(20,20) 부근 면 블록의 바깥 이탈 = |중심 − 링위치(RX,RY)|
        double faceMax = faceB.Count > 0 ? faceB.Max(b => Math.Sqrt((b.X - b.RX) * (b.X - b.RX) + (b.Y - b.RY) * (b.Y - b.RY))) : 0;
        double cornMax = cornB.Count > 0 ? cornB.Max(b => Math.Sqrt((b.X - b.RX) * (b.X - b.RX) + (b.Y - b.RY) * (b.Y - b.RY))) : 0;
        if (Environment.GetEnvironmentVariable("DUMP") == "1")
            foreach (var b in cornB) Console.WriteLine($"  코너블록 c10: X={b.X:F2} Y={b.Y:F2} RX={b.RX:F2} RY={b.RY:F2} 이탈={Math.Sqrt((b.X - b.RX) * (b.X - b.RX) + (b.Y - b.RY) * (b.Y - b.RY)):F2}");
        Check("S12 ★성토 코너블록 앞면 무돌출(면블록 이내)", cornMax <= faceMax + D * 0.75 + 1e-6,
            $"코너이탈 {cornMax:F2} vs 면이탈 {faceMax:F2}");
    }
}

// ★ S13 [v16.6] 절성토 단높이 분리 — '작은 쪽' 단높이가 '큰 쪽' 사면의 수직 예산을 깎지 않아야 한다.
//   단 개수 상한(MaxBenches)과 높이 예산(MaxRise)을 곱셈으로 묶으면, 성토 1m가 개수 상한 50에 걸리는 순간
//   절토 예산까지 50×1=50m로 주저앉아 55m 표고차에 못 닿는다(사면 잘림·구멍). 그 회귀를 잡는 테스트.
{
    var sq = new List<Point3> { new(0, 0, 120), new(60, 0, 120), new(60, 60, 120), new(0, 60, 120) };
    const double maxDiff = 55, spare = 2, bigBench = 5;
    var pa = new GradingParams
    {
        CutBenchHeight = bigBench, FillBenchHeight = 1, CutBenchWidth = 1, FillBenchWidth = 1,
        CutSlope = 1.5, FillSlope = 1.5, CellSize = 1.0,
        MaxBenches = 50,                             // 필요 단수 57 > 50 → 개수 상한에 걸리는 조건
        MaxRise = maxDiff + spare * bigBench,        // BuildParams와 동일한 예산식(표고차 + 여유)
        VertexSpacing = 2.0, MinSlope = 0.05, MinFaceRun = 0.005, MiterConvex = true, MiterLimit = 2.0,
    };
    foreach (var (up, label) in new[] { (true, "절토(5m)"), (false, "성토(1m)") })
    {
        var v = GradingGeometry.Build(sq, new FlatGround(up ? 175 : 65), pa, up);
        double reach = v.Rings.Count == 0 ? 0 : v.Rings.Max(r => r.Max(q => Math.Abs(q.Z - 120)));
        Check($"S13 ★{label} 수직 예산 {maxDiff}m 도달", reach >= maxDiff - 1e-6, $"도달 {reach:F1}m");
    }
}

// ★ S14 [v16.7] 사면 변환(DHSLOPE)의 '전체옹벽 → 전역사면 + 여집합 옹벽' 등가 변환 근거.
//   A(전역 구배 0.05, 구간 없음) 와 B(전역 구배 1.5, 둘레 전체가 옹벽 구간) 은 같은 형상이어야 한다.
//   이게 성립해야 "일부만 사면으로 되돌리기"를 기존 표현(사면+옹벽구간)으로 바꿔 표현할 수 있다.
{
    var sq = new List<Point3> { new(0, 0, 100), new(60, 0, 100), new(60, 60, 100), new(0, 60, 100) };
    GradingParams P(double cutSlope) => new()
    {
        CutBenchHeight = 5, FillBenchHeight = 5, CutBenchWidth = 1, FillBenchWidth = 1,
        CutSlope = cutSlope, FillSlope = 1.5, CellSize = 1.0,
        MaxBenches = 20, MaxRise = 60,
        VertexSpacing = 2.0, MinSlope = 0.05, MinFaceRun = 0.005, MiterConvex = true, MiterLimit = 2.0,
    };
    static double Area(IReadOnlyList<Point3> r)
    {
        double a = 0;
        for (int i = 0, j = r.Count - 1; i < r.Count; j = i++) a += r[j].X * r[i].Y - r[i].X * r[j].Y;
        return Math.Abs(a) * 0.5;
    }
    static double AvgZ(IReadOnlyList<Point3> r) { double s = 0; foreach (var q in r) s += q.Z; return s / r.Count; }

    double L = GradingGeometry.CumLen2D(sq)[^1];
    var ground = new FlatGround(140);   // 절토 40m

    var vA = GradingGeometry.Build(sq, ground, P(0.05), true);                       // 전역 수직, 구간 없음
    var zAll = new List<SlopeZone> { SlopeZone.Wall(0.0, L, 0, int.MaxValue, 0.05, 1.5) };
    var vB = GradingGeometry.Build(sq, ground, P(1.5), true, zAll);                  // 전역 사면 + 둘레 전체 옹벽

    Check("S14 등가변환 링 개수 일치", vA.Rings.Count == vB.Rings.Count, $"A {vA.Rings.Count} / B {vB.Rings.Count}");
    if (vA.Rings.Count == vB.Rings.Count)
    {
        double dAreaMax = 0, dZMax = 0;
        for (int i = 0; i < vA.Rings.Count; i++)
        {
            dAreaMax = Math.Max(dAreaMax, Math.Abs(Area(vA.Rings[i]) - Area(vB.Rings[i])));
            dZMax = Math.Max(dZMax, Math.Abs(AvgZ(vA.Rings[i]) - AvgZ(vB.Rings[i])));
        }
        Check("S14 ★등가변환 면적 동일(전체옹벽 보존)", dAreaMax < 0.5, $"최대 면적차 {dAreaMax:F3}㎡");
        Check("S14 ★등가변환 표고 동일", dZMax < 1e-6, $"최대 표고차 {dZMax:E1}m");
    }

    // C: 둘레 1/4만 3단(index 2)부터 사면으로 되돌림 — 그 구간이 바깥으로 퍼져 마지막 링 면적이 커져야 한다.
    var zC = new List<SlopeZone>
    {
        SlopeZone.Wall(0.0, L * 0.25, 0, 1, 0.05, 1.5),              // 선택 구간: 2단까지만 옹벽 → 3단부터 사면
        SlopeZone.Wall(L * 0.25, L, 0, int.MaxValue, 0.05, 1.5),     // 여집합: 끝까지 옹벽(종전 그대로)
    };
    var vC = GradingGeometry.Build(sq, ground, P(1.5), true, zC);
    double aA = Area(vA.Rings[^1]), aC = Area(vC.Rings[^1]);
    Check("S14 ★부분 사면 복귀가 실제로 퍼짐", aC > aA * 1.05, $"전체옹벽 {aA:F0}㎡ → 부분사면 {aC:F0}㎡");
}

// ★ S15 [구간 구배] 전역보다 '완만한' 구배를 준 구간은 **바깥으로 퍼진다**.
//   종전 구간(옹벽)은 항상 안쪽으로만 당겨졌고 링 조립 코드도 그 방향만 검증돼 있었다 —
//   이번 기능의 유일한 미지 위험이라 여기서 정면으로 확인한다.
{
    var sq = new List<Point3> { new(0, 0, 100), new(60, 0, 100), new(60, 60, 100), new(0, 60, 100) };
    var pr = new GradingParams
    {
        CutBenchHeight = 5, FillBenchHeight = 5, CutBenchWidth = 1, FillBenchWidth = 1,
        CutSlope = 1.5, FillSlope = 1.5, CellSize = 1.0, MaxBenches = 20, MaxRise = 60,
        VertexSpacing = 2.0, MinSlope = 0.05, MinFaceRun = 0.005, MiterConvex = true, MiterLimit = 2.0,
    };
    var cum = GradingGeometry.CumLen2D(sq);
    double L = cum[^1];
    var ground = new FlatGround(140);

    static double DistToBnd(Point3 q, IReadOnlyList<Point3> b)
    {
        double best = double.MaxValue;
        for (int i = 0, j = b.Count - 1; i < b.Count; j = i++)
        {
            double ax = b[j].X, ay = b[j].Y, bx = b[i].X, by = b[i].Y;
            double dx = bx - ax, dy = by - ay, len2 = dx * dx + dy * dy;
            double t = len2 < 1e-12 ? 0 : Math.Clamp(((q.X - ax) * dx + (q.Y - ay) * dy) / len2, 0, 1);
            double px = ax + dx * t - q.X, py = ay + dy * t - q.Y;
            best = Math.Min(best, Math.Sqrt(px * px + py * py));
        }
        return best;
    }
    static double Area2(IReadOnlyList<Point3> r)
    {
        double a = 0;
        for (int i = 0, j = r.Count - 1; i < r.Count; j = i++) a += r[j].X * r[i].Y - r[i].X * r[j].Y;
        return Math.Abs(a) * 0.5;
    }

    // ① 3단(index 2)부터 1:3.0 — 전역 1:1.5보다 완만하므로 그 구간이 바깥으로 퍼져야 한다.
    var zg = new SlopeZone { T0 = 0.0, T1 = L * 0.25 };
    zg.Rules.Add((2, 3.0, -1));
    zg.Normalize();
    var v = GradingGeometry.Build(sq, ground, pr, true, new List<SlopeZone> { zg });
    Check("S15 완만 구간 링 생성", v.HasSlope && v.Rings.Count > 6, $"링 {v.Rings.Count}");

    if (v.HasSlope && v.Rings.Count > 6)
    {
        var last = v.Rings[^1];
        double inMax = 0, outMax = 0;
        foreach (var q in last)
        {
            double t = GradingGeometry.ParamAt(sq, cum, q.X, q.Y);
            double d = DistToBnd(q, sq);
            if (t >= 0 && t <= L * 0.25) inMax = Math.Max(inMax, d); else outMax = Math.Max(outMax, d);
        }
        Check("S15 ★완만 구간이 바깥으로 퍼짐", inMax > outMax * 1.15,
            $"구간안 {inMax:F0}m vs 구간밖 {outMax:F0}m");

        bool mono = true;
        for (int i = 1; i < v.Rings.Count; i++)
            if (Area2(v.Rings[i]) < Area2(v.Rings[i - 1]) - 1e-6) mono = false;
        Check("S15 ★링이 바깥으로 단조 증가(자기교차 없음)", mono);
    }

    // ② 층층이 — 1단부터 1:1.0(급함), 3단부터 1:3.0(완만). JACK이 고른 사용 방식.
    var zl = new SlopeZone { T0 = 0.0, T1 = L * 0.25 };
    zl.Rules.Add((0, 1.0, -1));
    zl.Rules.Add((2, 3.0, -1));
    zl.Normalize();
    Check("S15 층층이 규칙 1단=1:1.0", Math.Abs(zl.SlopeAt(0, 1.5) - 1.0) < 1e-9, $"{zl.SlopeAt(0, 1.5)}");
    Check("S15 층층이 규칙 2단=1:1.0", Math.Abs(zl.SlopeAt(1, 1.5) - 1.0) < 1e-9, $"{zl.SlopeAt(1, 1.5)}");
    Check("S15 층층이 규칙 3단=1:3.0", Math.Abs(zl.SlopeAt(2, 1.5) - 3.0) < 1e-9, $"{zl.SlopeAt(2, 1.5)}");
    Check("S15 층층이 규칙 5단=1:3.0", Math.Abs(zl.SlopeAt(4, 1.5) - 3.0) < 1e-9, $"{zl.SlopeAt(4, 1.5)}");
    var vl = GradingGeometry.Build(sq, ground, pr, true, new List<SlopeZone> { zl });
    Check("S15 ★층층이 링 생성", vl.HasSlope && vl.Rings.Count > 6, $"링 {vl.Rings.Count}");

    // ③ 옛 표현(옹벽)이 새 타입으로도 동일한가 — 회귀 안전선.
    var zw = SlopeZone.Wall(0.0, L * 0.25, 2, int.MaxValue, 0.05, 1.5);
    Check("S15 옹벽변환 호환 2단=전역", Math.Abs(zw.SlopeAt(1, 1.5) - 1.5) < 1e-9, $"{zw.SlopeAt(1, 1.5)}");
    Check("S15 옹벽변환 호환 3단=수직", Math.Abs(zw.SlopeAt(2, 1.5) - 0.05) < 1e-9, $"{zw.SlopeAt(2, 1.5)}");
    var zr = SlopeZone.Wall(0.0, L * 0.25, 0, 1, 0.05, 1.5);   // 사면 복귀(ToBench=1) — 3단부터 전역 복귀
    Check("S15 사면복귀 호환 1단=수직", Math.Abs(zr.SlopeAt(0, 1.5) - 0.05) < 1e-9, $"{zr.SlopeAt(0, 1.5)}");
    Check("S15 사면복귀 호환 3단=전역", Math.Abs(zr.SlopeAt(2, 1.5) - 1.5) < 1e-9, $"{zr.SlopeAt(2, 1.5)}");
}

// ★ S16 [구간 제원] 구간 규칙이 구배뿐 아니라 **단높이·소단폭**까지 바꾼다(JACK 0804 — 변환 1회당 제원 한 벌).
{
    var sq = new List<Point3> { new(0, 0, 100), new(60, 0, 100), new(60, 60, 100), new(0, 60, 100) };
    var pr = new GradingParams
    {
        CutBenchHeight = 5, FillBenchHeight = 5, CutBenchWidth = 1, FillBenchWidth = 1,
        CutSlope = 1.5, FillSlope = 1.5, CellSize = 1.0, MaxBenches = 30, MaxRise = 60,
        VertexSpacing = 2.0, MinSlope = 0.05, MinFaceRun = 0.005, MiterConvex = true, MiterLimit = 2.0,
    };
    var cum = GradingGeometry.CumLen2D(sq);
    double L = cum[^1];
    var ground = new FlatGround(140);

    // 규칙 조회 자체 검증 — 전역 소단 1m/구배 1:1.5, 3단부터 소단 2m/구배 1:2.0
    var z = new SlopeZone { T0 = 0.0, T1 = L * 0.25 };
    z.Rules.Add((2, 2.0, 2.0));
    z.Normalize();
    var a0 = z.At(0, 1.5, 1);
    var a2 = z.At(2, 1.5, 1);
    Check("S16 2단은 전역 제원(소단 1m·1:1.5)",
        Math.Abs(a0.BenchW - 1) < 1e-9 && Math.Abs(a0.Slope - 1.5) < 1e-9, $"{a0.BenchW}/{a0.Slope}");
    Check("S16 ★3단부터 구간 제원(소단 2m·1:2.0)",
        Math.Abs(a2.BenchW - 2) < 1e-9 && Math.Abs(a2.Slope - 2.0) < 1e-9, $"{a2.BenchW}/{a2.Slope}");

    // 옛 옹벽 구간(소단폭 미지정=-1)은 전역값을 따라야 한다 — 하위호환.
    var zw = SlopeZone.Wall(0.0, L * 0.25, 1, int.MaxValue, 0.05, 1.5);
    var aw = zw.At(2, 1.5, 1);
    Check("S16 옛 옹벽 구간은 전역 소단폭 유지",
        Math.Abs(aw.BenchW - 1) < 1e-9 && Math.Abs(aw.Slope - 0.05) < 1e-9, $"{aw.BenchW}/{aw.Slope}");

    // 소단폭만 넓히면 그 구간이 바깥으로 밀린다(구배는 전역 그대로).
    var zWide = new SlopeZone { T0 = 0.0, T1 = L * 0.25 };
    zWide.Rules.Add((0, 1.5, 6.0));      // 소단 1m → 6m
    zWide.Normalize();
    var vWide = GradingGeometry.Build(sq, ground, pr, true, new List<SlopeZone> { zWide });
    static double DistB(Point3 q, IReadOnlyList<Point3> b)
    {
        double best = double.MaxValue;
        for (int i = 0, j = b.Count - 1; i < b.Count; j = i++)
        {
            double ax = b[j].X, ay = b[j].Y, dx = b[i].X - ax, dy = b[i].Y - ay, l2 = dx * dx + dy * dy;
            double t = l2 < 1e-12 ? 0 : Math.Clamp(((q.X - ax) * dx + (q.Y - ay) * dy) / l2, 0, 1);
            double px = ax + dx * t - q.X, py = ay + dy * t - q.Y;
            best = Math.Min(best, Math.Sqrt(px * px + py * py));
        }
        return best;
    }
    if (vWide.HasSlope && vWide.Rings.Count > 4)
    {
        var lastW = vWide.Rings[^1];
        double inW = 0, outW = 0;
        foreach (var q in lastW)
        {
            double t = GradingGeometry.ParamAt(sq, cum, q.X, q.Y);
            double d = DistB(q, sq);
            if (t >= 0 && t <= L * 0.25) inW = Math.Max(inW, d); else outW = Math.Max(outW, d);
        }
        Check("S16 ★넓은 소단 구간이 바깥으로 밀림", inW > outW * 1.15, $"구간안 {inW:F0}m vs 구간밖 {outW:F0}m");
    }
}

// ★ S17 [스샷 버그 0804] 구간 겹침을 '합집합'으로 뭉개지 않고 '조각'으로 가른다 — SlopeZone.Flatten.
//   JACK 스샷: 옹벽 구간이 넓게 있고, 그 안 일부(노란선)만 사면으로 되돌렸는데 옹벽 구간 전체(화살표까지)가
//   사면으로 바뀌었다. 원인 = 겹치는 두 구간을 합집합 하나로 만들며 규칙까지 합친 것.
{
    const double L = 100.0;
    static SlopeZone? ZoneAt(List<SlopeZone> zs, double t) { foreach (var z in zs) if (z.Contains(t)) return z; return null; }
    static double SlopeOf(List<SlopeZone> zs, double t, int bench, double baseS)
        => ZoneAt(zs, t)?.SlopeAt(bench, baseS) ?? baseS;

    // ① 스샷 재현: 옹벽 [0,L] 전체(0단부터 수직) + 사면 복귀 [20,35](노란선, 1단부터 1:1.5)
    var s1 = new List<SlopeZone>();
    var wallAll = new SlopeZone { T0 = 0.0, T1 = L }; wallAll.Rules.Add((0, 0.05, -1));
    var pickY = new SlopeZone { T0 = 20.0, T1 = 35.0 }; pickY.Rules.Add((1, 1.5, 1.0));
    s1.Add(wallAll); s1.Add(pickY);
    SlopeZone.Flatten(s1, L);
    Check("S17 ★노란선 안: 1단부터 사면", Math.Abs(SlopeOf(s1, 27.5, 1, 1.5) - 1.5) < 1e-9, $"{SlopeOf(s1, 27.5, 1, 1.5)}");
    Check("S17 ★노란선 안: 0단(하단)은 옹벽 유지", Math.Abs(SlopeOf(s1, 27.5, 0, 1.5) - 0.05) < 1e-9, $"{SlopeOf(s1, 27.5, 0, 1.5)}");
    Check("S17 ★화살표 쪽(구간 밖): 옹벽 그대로", Math.Abs(SlopeOf(s1, 70.0, 1, 1.5) - 0.05) < 1e-9,
        $"{SlopeOf(s1, 70.0, 1, 1.5)} (버그면 1.5로 나옴)");
    Check("S17 ★반대쪽(구간 밖)도 옹벽 그대로", Math.Abs(SlopeOf(s1, 5.0, 3, 1.5) - 0.05) < 1e-9, $"{SlopeOf(s1, 5.0, 3, 1.5)}");

    // ② 부분 겹침: 옹벽 [10,50](2단부터) + 사면 [30,70](3단부터 1:2.0) → 세 조각으로 갈라져야 한다.
    var s2 = new List<SlopeZone>();
    var w2 = new SlopeZone { T0 = 10, T1 = 50 }; w2.Rules.Add((2, 0.05, -1));
    var p2 = new SlopeZone { T0 = 30, T1 = 70 }; p2.Rules.Add((3, 2.0, 1.0));
    s2.Add(w2); s2.Add(p2);
    SlopeZone.Flatten(s2, L);
    Check("S17 부분겹침 A만(20): 3단도 옹벽", Math.Abs(SlopeOf(s2, 20, 3, 1.5) - 0.05) < 1e-9, $"{SlopeOf(s2, 20, 3, 1.5)}");
    Check("S17 부분겹침 교집합(40): 2단 옹벽·3단 1:2", Math.Abs(SlopeOf(s2, 40, 2, 1.5) - 0.05) < 1e-9
        && Math.Abs(SlopeOf(s2, 40, 3, 1.5) - 2.0) < 1e-9, $"{SlopeOf(s2, 40, 2, 1.5)}/{SlopeOf(s2, 40, 3, 1.5)}");
    Check("S17 부분겹침 B만(60): 2단 전역·3단 1:2", Math.Abs(SlopeOf(s2, 60, 2, 1.5) - 1.5) < 1e-9
        && Math.Abs(SlopeOf(s2, 60, 3, 1.5) - 2.0) < 1e-9, $"{SlopeOf(s2, 60, 2, 1.5)}/{SlopeOf(s2, 60, 3, 1.5)}");
    Check("S17 부분겹침 밖(85): 구간 없음", ZoneAt(s2, 85) == null);

    // ③ '클릭한 단부터 바깥 끝까지' 대체 의미: 같은 자리에서 나중에 더 낮은 단을 찍으면 그 위 규칙은 지워진다.
    var s3 = new List<SlopeZone>();
    var hi = new SlopeZone { T0 = 10, T1 = 40 }; hi.Rules.Add((3, 2.0, 1.0));   // 먼저: 3단부터 1:2
    var lo = new SlopeZone { T0 = 10, T1 = 40 }; lo.Rules.Add((1, 1.0, 1.0));   // 나중: 1단부터 1:1
    s3.Add(hi); s3.Add(lo);
    SlopeZone.Flatten(s3, L);
    Check("S17 나중 낮은단이 위를 대체(3단→1:1)", Math.Abs(SlopeOf(s3, 25, 3, 1.5) - 1.0) < 1e-9, $"{SlopeOf(s3, 25, 3, 1.5)}");
    // 층층이(낮은단 먼저 → 높은단 나중)는 쌓인다.
    var s4 = new List<SlopeZone>();
    var lo4 = new SlopeZone { T0 = 10, T1 = 40 }; lo4.Rules.Add((1, 1.0, 1.0));
    var hi4 = new SlopeZone { T0 = 10, T1 = 40 }; hi4.Rules.Add((3, 2.0, 1.0));
    s4.Add(lo4); s4.Add(hi4);
    SlopeZone.Flatten(s4, L);
    Check("S17 층층이 유지(1단=1:1·3단=1:2)", Math.Abs(SlopeOf(s4, 25, 1, 1.5) - 1.0) < 1e-9
        && Math.Abs(SlopeOf(s4, 25, 3, 1.5) - 2.0) < 1e-9, $"{SlopeOf(s4, 25, 1, 1.5)}/{SlopeOf(s4, 25, 3, 1.5)}");

    // ④ 랩(0을 지나는) 구간 + 겹침도 조각이 정확히 갈라진다.
    var s5 = new List<SlopeZone>();
    var wrapW = new SlopeZone { T0 = 80, T1 = 20 }; wrapW.Rules.Add((0, 0.05, -1));   // 랩 옹벽
    var pick5 = new SlopeZone { T0 = 90, T1 = 10 }; pick5.Rules.Add((1, 1.5, 1.0));   // 그 안 일부(랩)
    s5.Add(wrapW); s5.Add(pick5);
    SlopeZone.Flatten(s5, L);
    Check("S17 랩 교집합(95): 1단 사면", Math.Abs(SlopeOf(s5, 95, 1, 1.5) - 1.5) < 1e-9, $"{SlopeOf(s5, 95, 1, 1.5)}");
    Check("S17 랩 A만(85): 1단 옹벽 유지", Math.Abs(SlopeOf(s5, 85, 1, 1.5) - 0.05) < 1e-9, $"{SlopeOf(s5, 85, 1, 1.5)}");
    Check("S17 랩 A만(15): 1단 옹벽 유지", Math.Abs(SlopeOf(s5, 15, 1, 1.5) - 0.05) < 1e-9, $"{SlopeOf(s5, 15, 1, 1.5)}");
    Check("S17 랩 밖(50): 구간 없음", ZoneAt(s5, 50) == null);
}

// ★ S18 [스파이크 0804] 단차 계획선(일부 정점 고도 다름) + 옹벽 구간 조합에서 보조 브레이크라인
//   (단차경계선·코너 능선)이 링과 2D 교차할 때 Z가 어긋나면, 공유정점 삽입이 그 지점을 링 Z로 강제해
//   수직 절벽(스파이크·침봉)이 생긴다(JACK 스샷, 진단 maxΔZ 41.032m). 원인 = 교점 선택이 '전역 사면
//   거리'를 가정 — 옹벽 구간의 실제 링은 경계에 붙어 있어 70m 밖 엉뚱한 조각에 꽂혔음.
//   수정 후엔 보조선이 실제 표면 위를 따라가므로 교차 Z 간극이 ≈0이어야 한다.
{
    // 단차 남변: (20,0)~(40,0) 사이가 1m 낮음 → 단차경계선 레이 2개(양끝에서 남쪽으로).
    var bnd = new List<Point3>
    {
        new(0, 0, 110.5), new(20, 0, 110.5), new(30, 0, 109.5), new(40, 0, 110.5),
        new(60, 0, 110.5), new(60, 40, 110.5), new(0, 40, 110.5),
    };
    var pr = new GradingParams
    {
        CutBenchHeight = 5, FillBenchHeight = 5, CutBenchWidth = 1, FillBenchWidth = 1,
        CutSlope = 1.2, FillSlope = 1.2, CellSize = 1.0, MaxBenches = 20, MaxRise = 50,
        VertexSpacing = 2.0, MinSlope = 0.05, MinFaceRun = 0.005, MiterConvex = true, MiterLimit = 2.0,
    };
    // 옹벽 구간 [10..25] — 레이 시작 정점(param 20)이 구간 안. 나머지 둘레는 전역 사면(1:1.2, 70m 밖까지).
    var zw = new SlopeZone { T0 = 10, T1 = 25 };
    zw.Rules.Add((0, 0.05, -1));

    static bool SegX(Point3 a, Point3 b, Point3 c, Point3 d, out double u, out double v)
    {
        u = v = 0;
        double rx = b.X - a.X, ry = b.Y - a.Y, sx = d.X - c.X, sy = d.Y - c.Y;
        double den = rx * sy - ry * sx;
        if (Math.Abs(den) < 1e-12) return false;
        double qx = c.X - a.X, qy = c.Y - a.Y;
        u = (qx * sy - qy * sx) / den; v = (qx * ry - qy * rx) / den;
        return u >= 0 && u <= 1 && v >= 0 && v <= 1;
    }
    // BreaklinePrep와 같은 계산 — 보조선×링 2D 교차점의 |보조선Z − 링Z| 최댓값.
    static double MaxAuxGap(VirtualSlope vs)
    {
        double worst = 0;
        foreach (var line in vs.CornerLines)
            for (int j = 0; j + 1 < line.Count; j++)
                foreach (var ring in vs.Rings)
                    for (int i = 0; i + 1 < ring.Count; i++)
                    {
                        if (!SegX(ring[i], ring[i + 1], line[j], line[j + 1], out double u, out double v)) continue;
                        double zr = ring[i].Z + (ring[i + 1].Z - ring[i].Z) * u;
                        double zl = line[j].Z + (line[j + 1].Z - line[j].Z) * v;
                        worst = Math.Max(worst, Math.Abs(zr - zl));
                    }
        return worst;
    }

    var ground = new FlatGround(170);
    var vsZ = GradingGeometry.Build(bnd, ground, pr, true, new List<SlopeZone> { zw });
    Check("S18 링 생성(단차+옹벽구간)", vsZ.HasSlope && vsZ.Rings.Count > 6, $"링 {vsZ.Rings.Count}");
    Check("S18 보조선 존재(단차경계선 포함)", vsZ.CornerLines.Count > 0, $"{vsZ.CornerLines.Count}개");
    double g1 = MaxAuxGap(vsZ);
    Check("S18 ★보조선-링 교차 Z간극 ≈ 0 (옹벽구간)", g1 < 1.5, $"maxΔZ {g1:F2}m (버그면 ~40m)");

    // 구간 없는 단차 부지(종전 정상 케이스)도 회귀 없음.
    var vsP = GradingGeometry.Build(bnd, ground, pr, true);
    double g0 = MaxAuxGap(vsP);
    Check("S18 회귀: 구간 없어도 Z간극 ≈ 0", g0 < 1.5, $"maxΔZ {g0:F2}m");
}

// ── S19: 핀치(자기교차) 발자국 링 — 조각 전부 유지 [다중 구역 0804 — JACK 스샷: 성토면 속 절토 조각·뜬 패널] ──
{
    // 나비넥타이 링: 대각선이 (5,5)에서 교차 — Buffer(0)이 아래/위 두 로브로 쪼갠다.
    // 종전 ToCleanPolygon(최대 조각만)은 한 로브를 버려 그 자리 마스크·차감이 통째로 빠졌다.
    var bow = new List<Point3> { new(0, 0, 0), new(10, 0, 0), new(0, 10, 0), new(10, 10, 0) };

    var g = NtsSupport.ToCleanGeometry(bow);
    Check("S19 핀치 링 정리 성공", g != null && !g.IsEmpty);
    Check("S19 ★조각 전부 유지(면적 50 = 양 로브)", g != null && Math.Abs(g.Area - 50.0) < 0.5,
        g == null ? "" : $"면적 {g.Area:F1}㎡ (최대 조각만이면 25)");

    var mask = GradingPolygons.RegionMask.Build(new List<IReadOnlyList<Point3>> { bow });
    Check("S19 마스크 생성", mask != null);
    if (mask != null)
    {
        Check("S19 ★아래 로브 포함", mask.Contains(5, 2), "(5,2)");
        Check("S19 ★위 로브 포함(종전엔 최대 조각 아닌 쪽이 빠짐)", mask.Contains(5, 8), "(5,8)");
        Check("S19 로브 밖 제외", !mask.Contains(1.0, 5.0), "(1,5)");
        Check("S19 마스크 조각 2", mask.PieceCount == 2, $"{mask.PieceCount}개");
    }

    // 계획면 차감(계획면.shp 경로)도 양 로브가 모두 빠져야 — 12×12 계획에서 나비 빼면 144−50.
    var plan = new List<Point3> { new(-1, -1, 0), new(11, -1, 0), new(11, 11, 0), new(-1, 11, 0) };
    var feats = GradingPolygons.PlanMinusFootprints(plan, new List<IReadOnlyList<Point3>> { bow }, out double excl);
    Check("S19 ★계획면 차감 = 양 로브(≈50㎡)", Math.Abs(excl - 50.0) < 0.5, $"제외 {excl:F1}㎡");
    double remain = 0; foreach (var f in feats) remain += f.Area;
    Check("S19 계획 잔여 = 144−50", Math.Abs(remain - 94.0) < 0.5, $"잔여 {remain:F1}㎡");
}

// ★ S20 [0805 JACK '절토 옹벽 누락'] 같은 부지·같은 설정에서 **사면형상(직각/라운드)만** 바꿔 돌린다.
//   현장 로그 두 판이 링 87점/코너능선 7 → 패널 163, 링 117점/코너능선 2 → 패널 6 으로 갈렸다.
//   그 차이가 MiterConvex 하나로 재현되는지 Civil3D 없이 판정한다.
{
    // 현장 로그(DHGRADE_진단.log 0804 18:04)의 실제 경계 7점 그대로 — 단차(110.27/110.53)까지 포함.
    var bnd = new List<Point3> {
        new(177772.84,323632.09,110.270), new(177769.04,323633.64,110.500),
        new(177769.21,323637.39,110.500), new(177749.93,323638.27,110.500),
        new(177749.06,323619.29,110.500), new(177765.68,323618.53,110.500),
        new(177765.14,323620.72,110.530),
    };
    // 원지반: 로그 TIN표를 보면 동쪽으로 갈수록 급히 높아진다(부지 서쪽 ≈절토 얕음, 동쪽 ≈20m). 근사 재현.
    var gnd = new TiltGround(177749.06, 323618.53, 112.0, 0.55, 0.15);

    static GradingParams Pr(bool miter) => new() {
        CutBenchHeight = 5, FillBenchHeight = 5, CutBenchWidth = 1, FillBenchWidth = 1,
        CutSlope = 0.05, FillSlope = 0.05,
        CellSize = 0.5, MaxBenches = 50, VertexSpacing = 1.0, MinSlope = 0.05,
        MinFaceRun = 0.005, MiterConvex = miter, MiterLimit = 2.0,
    };

    static int Num(string s, string key)
    {
        var m = System.Text.RegularExpressions.Regex.Match(s, key + @"\s+(\d+)");
        return m.Success ? int.Parse(m.Groups[1].Value) : -1;
    }
    // 겹침 계수 — 미터 절단을 모서리 근처로 제한했으니 '패널이 서로 포개지지 않는가'가 핵심 위험.
    //   같은 자리(중심 거리 < side*0.4)에 법선까지 거의 같은(10° 이내) 패널 두 장 = 포개짐.
    //   ※ **온전(IsFull) 패널만** 본다. 코너 필러는 설계상 이웃 면과 반두께(cornerLap 0.10) 겹치게 만든 것이라
    //     (JACK 0722 "딱 만나는 것보다 반두께 더 나가게") 전체를 세면 의도된 겹침이 섞여 지표가 무의미해진다.
    static int OverlapPairs(List<WallPanels.Panel> all, double side)
    {
        var ps = all.Where(p => p.IsFull).ToList();
        int n = 0; double r2 = (side * 0.4) * (side * 0.4);
        for (int i = 0; i < ps.Count; i++)
            for (int j = i + 1; j < ps.Count; j++)
            {
                double dx = ps[i].Center.X - ps[j].Center.X, dy = ps[i].Center.Y - ps[j].Center.Y,
                       dz = ps[i].Center.Z - ps[j].Center.Z;
                if (dx * dx + dy * dy + dz * dz > r2) continue;
                var a = ps[i].Normal; var b = ps[j].Normal;
                if (a.x * b.x + a.y * b.y + a.z * b.z > 0.985) n++;      // cos10° ≈ 0.985
            }
        return n;
    }
    (int rings, int pts, int panels, int rowsN, int dropCorner, int overlap, double stray, string diag) Run(bool miter)
    {
        var vs = GradingGeometry.Build(bnd, gnd, Pr(miter), true);       // 절토(up)
        var rs = vs.Rings.Select(r => (IReadOnlyList<Point3>)r).ToList();
        int pts = rs.Count > 1 ? rs[1].Count : 0;
        var ps = WallPanels.Generate(rs, gnd, cut: true, slopeN: 0.05);
        // 링 전체 박스 밖으로 패널이 얼마나 벗어나는가 — 감긴 벽면의 좌표 폭주 검출(0805).
        double rxa = double.MaxValue, rxb = double.MinValue, rya = double.MaxValue, ryb = double.MinValue;
        foreach (var r in rs) foreach (var q in r)
        { rxa = Math.Min(rxa, q.X); rxb = Math.Max(rxb, q.X); rya = Math.Min(rya, q.Y); ryb = Math.Max(ryb, q.Y); }
        double stray = 0;
        foreach (var p in ps) foreach (var q in p.Poly)
        {
            stray = Math.Max(stray, Math.Max(rxa - q.X, q.X - rxb));
            stray = Math.Max(stray, Math.Max(rya - q.Y, q.Y - ryb));
        }
        string d = WallPanels.LastDiag;
        return (rs.Count, pts, ps.Count, Num(d, "행"), Num(d, "코너"), OverlapPairs(ps, 5.0 / 3), stray, d);
    }

    var mi = Run(true);    // 직각
    var ro = Run(false);   // 라운드
    Console.WriteLine($"      직각  : 링 {mi.rings}단 · 1단 {mi.pts}점 · 패널 {mi.panels}");
    Console.WriteLine($"              {mi.diag}");
    Console.WriteLine($"      라운드: 링 {ro.rings}단 · 1단 {ro.pts}점 · 패널 {ro.panels}");
    Console.WriteLine($"              {ro.diag}");

    Check("S20 직각 옹벽 생성됨", mi.panels > 0, $"패널 {mi.panels}");
    // ★ 본 검사: 형상 옵션은 모서리 '모양'만 바꿔야지 옹벽이 사라지면 안 된다.
    //   벽면 개수는 구조상 다를 수 있으니(라운드는 원호를 한 면으로 덮음) 절대수 대신 **스팬당 패널 수**로 본다.
    Check("S20 ★라운드에서도 옹벽이 남아야 한다", ro.panels >= mi.panels * 0.5,
        $"직각 {mi.panels} → 라운드 {ro.panels}");
    Check("S20 ★코너 절단에 벽이 통째로 날아가지 않는다(라운드)", ro.dropCorner <= ro.rowsN * 0.15,
        $"행 {ro.rowsN} 중 코너버림 {ro.dropCorner} (버그면 ~57%)");
    // ★ 미터 절단을 모서리 근처로 제한한 대가로 패널이 포개지면 안 된다(포개짐=DWG 이중벽).
    Check("S20 ★패널 포개짐 없음(직각)", mi.overlap == 0, $"겹친 쌍 {mi.overlap}");
    Check("S20 ★패널 포개짐 없음(라운드)", ro.overlap == 0, $"겹친 쌍 {ro.overlap}");
    // ★ 감긴 벽면(마지막 face는 링 시작점을 지나 감김)에서 AtSeg가 호길이를 되감지 않으면
    //   패널이 접선 방향으로 둘레만큼 날아간다 — 현장 실측 부지 밖 137m(0805 JACK 스샷).
    //   코너 미터의 의도된 연장은 몇 m 이내이므로 5m를 상한으로 본다.
    Check("S20 ★패널이 링 밖으로 날아가지 않는다(직각)", mi.stray < 5.0, $"최대 이탈 {mi.stray:F1}m");
    Check("S20 ★패널이 링 밖으로 날아가지 않는다(라운드)", ro.stray < 5.0, $"최대 이탈 {ro.stray:F1}m");
    // 자체 검증 — 되감기를 끄면(=0805 버그) 반드시 나빠져야 한다. 항상 통과하는 검사는 검사가 아니다.
    //   ※ 버그의 얼굴은 지형에 따라 둘로 갈린다: 날아간 패널이 데이라잇 밖이면 **그냥 사라지고**(이 부지),
    //     살아남으면 **수백 m 밖에 나타난다**(JACK 현장 — 부지 밖 137m). 그래서 개수 손실로 판정한다.
    WallPanels.DisableWrapFixForTest = true;
    var bug = Run(false);
    WallPanels.DisableWrapFixForTest = false;
    Check("S20 ★검사 자체검증: 되감기를 끄면 옹벽이 줄어든다", bug.panels < ro.panels * 0.9,
        $"되감기 OFF {bug.panels}장 → 수정본 {ro.panels}장 (감긴 벽면 몫 {ro.panels - bug.panels}장 회복)");
}

// ★ S21 [0805 JACK '성토 구간 안의 알 수 없는 초록선'] 정지 구역 안쪽에 갇힌 교선 고리는 표시 제외.
//   단, 바깥 경계선(클립링과 사실상 겹침)은 절대 지워지면 안 된다 — 그게 진짜 정지경계다.
{
    var clip = new List<Point3> { new(0,0,0), new(100,0,0), new(100,100,0), new(0,100,0) };
    var outer = new List<Point3> { new(0,0,0), new(100,0,0), new(100,100,0), new(0,100,0), new(0,0,0) };  // 경계와 일치
    var inner = new List<Point3> { new(40,40,0), new(60,40,0), new(60,60,0), new(40,60,0), new(40,40,0) }; // 안쪽 둔덕
    var edge  = new List<Point3> { new(0.2,10,0), new(0.2,90,0) };            // 경계에서 20cm — 경계선 취급
    // 현장값 재현: 경계에서 0.8m 안쪽에 뜬 섬은 반드시 걸러야 한다(0805 10:55 로그의 191점 고리).
    var isle  = new List<Point3> { new(0.8,20,0), new(0.8,80,0), new(3,80,0), new(3,20,0), new(0.8,20,0) };
    var kept = GradingPolygons.DropLoopsInsideClip(
        new List<IReadOnlyList<Point3>> { outer, inner, edge, isle }, clip, 0.3, out int dn);

    Check("S21 ★안쪽 둔덕 고리 제외", !kept.Contains(inner), "부지 한가운데 섬");
    Check("S21 ★경계 0.8m 안쪽 섬도 제외(현장값)", !kept.Contains(isle),
        "종전 여유 1.0m가 0.8m짜리를 놓쳐 초록선이 남았다");
    Check("S21 ★바깥 경계선은 보존", kept.Contains(outer), "경계선이 지워지면 정지경계가 사라진다");
    Check("S21 ★경계 근처(20cm) 선도 보존", kept.Contains(edge), "여유(tol) 안쪽은 경계로 본다");
    Check("S21 남은 고리 2개", kept.Count == 2, $"{kept.Count}개 · 제외 {dn}개");

    // 클립링이 없으면(주입 실패) 아무것도 지우지 않아야 한다 — 표시가 통째로 사라지는 사고 방지.
    var all = GradingPolygons.DropLoopsInsideClip(
        new List<IReadOnlyList<Point3>> { outer, inner, edge, isle }, null, 0.3, out int dn2);
    Check("S21 ★클립링 없으면 전부 보존", dn2 == 0 && all.Count == 4, $"제외 {dn2} · 남은 {all.Count}");
}

// ★ S22 [0805 JACK '코너에서 판넬 크로스 + 그 뒤 누락'] 현장 경계(0805 10:24 로그의 11점) 그대로.
//   코너 미터의 keep 부호를 face 한가운데 한 점으로 판단하면, 한 벽면이 여러 세그먼트에 걸칠 때
//   그 점이 실제 벽에서 벗어나 부호가 뒤집힌다 → 코너를 가로지르는 조각만 살고 정작 코너가 빈다.
{
    var bnd = new List<Point3> {
        new(185735.52,324643.97,191), new(185736.17,324644.29,191), new(185736.79,324644.69,191),
        new(185743.08,324649.27,191), new(185737.06,324663.34,191), new(185718.67,324655.47,191),
        new(185710.32,324659.72,191), new(185664.08,324639.92,191), new(185676.05,324611.98,191),
        new(185730.47,324635.28,191), new(185728.11,324640.80,191),
    };
    // 현장 로그의 단높이 2.5m·소단 1.0m·수직(1:0.05). 원지반은 동쪽이 높은 경사면으로 근사.
    var gnd = new TiltGround(185664.08, 324611.98, 196.0, 0.30, 0.10);
    var pr = new GradingParams {
        CutBenchHeight = 2.5, FillBenchHeight = 2.5, CutBenchWidth = 1, FillBenchWidth = 1,
        CutSlope = 0.05, FillSlope = 0.05,
        CellSize = 0.5, MaxBenches = 50, VertexSpacing = 1.0, MinSlope = 0.05,
        // 현장 로그가 '벽면 137 · 모서리 102'(벽면이 여러 세그먼트에 걸침)라 라운드 쪽이다 — 직각은 벽면=모서리.
        MinFaceRun = 0.005, MiterConvex = false, MiterLimit = 2.0,
    };
    var vs = GradingGeometry.Build(bnd, gnd, pr, true);
    var rs = vs.Rings.Select(r => (IReadOnlyList<Point3>)r).ToList();

    // 코너를 넘어 부지 안쪽으로 얼마나 깊이 들어왔는지 — '크로스' 판정의 척도.
    //   1단 벽면은 자기 링에서 안쪽으로 최대 slopeN×단높이(=0.125m)까지만 들어간다.
    var innerPoly = NtsSupport.ToCleanGeometry(rs[1]);
    (int Crossed, double Deepest) Cross(List<WallPanels.Panel> pp)
    {
        if (innerPoly == null || innerPoly.IsEmpty) return (0, 0);
        var edge = innerPoly.Boundary; var gf = NtsSupport.Factory();
        int n = 0; double deep = 0;
        foreach (var p in pp)
        {
            double dMax = 0;
            foreach (var q in p.Poly)
            {
                var pt = gf.CreatePoint(new NetTopologySuite.Geometries.Coordinate(q.X, q.Y));
                if (innerPoly.Covers(pt)) dMax = Math.Max(dMax, edge.Distance(pt));
            }
            deep = Math.Max(deep, dMax);
            if (dMax > 1.0) n++;
        }
        return (n, deep);
    }

    var ps = WallPanels.Generate(rs, gnd, cut: true, slopeN: 0.05, joint: 0.05);
    string d = WallPanels.LastDiag;
    Console.WriteLine($"      현장경계: {d}");

    static int N22(string s, string key)
    {
        var m = System.Text.RegularExpressions.Regex.Match(s, key + @"\s+(\d+)");
        return m.Success ? int.Parse(m.Groups[1].Value) : -1;
    }
    int rowsN = N22(d, "행"), corner = N22(d, "코너");
    Check("S22 현장 경계로 옹벽 생성", ps.Count > 100, $"패널 {ps.Count}");
    // ★ 코너에서 죽는 행이 전체의 몇 %인가 — 부호가 뒤집히면 코너마다 벽이 통째로 빈다.
    Check("S22 ★코너에서 벽이 통째로 죽지 않는다", corner <= rowsN * 0.05,
        $"행 {rowsN} 중 코너버림 {corner} ({(rowsN > 0 ? corner * 100.0 / rowsN : 0):F1}%)");
    // ★ 코너를 가로지르는 판넬(부지 안쪽으로 넘어간 조각)이 없어야 한다 — JACK '크로스' 증상.
    //   1단 벽면은 자기 링에서 안쪽으로 최대 slopeN×단높이(=0.125m)까지만 들어간다. 그보다 훨씬
    //   깊이(1m 초과) 들어온 패널은 이웃 벽면 쪽으로 넘어간 것 — 위에서 보면 벽을 가로지른다.
    var (crossed, deepest) = Cross(ps);
    Check("S22 ★코너를 가로지른 판넬 없음", crossed == 0,
        $"넘어간 판넬 {crossed}장 · 최대 침투 {deepest:F2}m (정상 상한 0.13m)");

    // 자체 검증 — keep 부호를 옛 방식(face 한가운데 한 점)으로 되돌리면 반드시 재발해야 한다.
    WallPanels.DisableRefSplitForTest = true;
    var bugPs = WallPanels.Generate(rs, gnd, cut: true, slopeN: 0.05, joint: 0.05);
    string bugD = WallPanels.LastDiag;
    WallPanels.DisableRefSplitForTest = false;
    var (bugCross, bugDeep) = Cross(bugPs);
    Check("S22 ★검사 자체검증: 옛 부호 방식이면 크로스/누락이 재발한다",
        bugCross > 0 || N22(bugD, "코너") > corner * 3 + 10,
        $"옛 방식 → 넘어간 판넬 {bugCross}장(침투 {bugDeep:F1}m) · 코너버림 {N22(bugD, "코너")} · 패널 {bugPs.Count}(수정본 {ps.Count})");
}

// ★ S23 [0805 JACK '옹벽 사선으로 잘려 누락'] 현장 로그: 앵커판넬 생성 72 → DWG 저장 46,
//   ⚠판 만들기 실패 26장 · 첫 사유 **eCannotScaleNonUniformly**.
//   그 예외는 WallPanelDwg가 패널을 놓을 때 쓰는 좌표계 행렬
//   `Matrix3d.AlignCoordinateSystem(…, U, V, W)`가 **직교정규가 아닐 때**만 난다
//   (AutoCAD Solid3d.TransformBy는 회전+이동만 허용 — 늘어남·비틀림이 섞이면 거부).
//   그러니 판정은 Civil3D 없이 순수 기하로 가능하다: 패널마다 |U|·|V|·|W|와 U·V를 재면 된다.
//   ※ 코드상 |U|=|W|=1은 보장(둘 다 정규화)이고 W⊥U·W⊥V도 외적이라 보장.
//     남는 자유도는 **U·V** 하나 — U는 링 세그먼트의 3D 방향, V는 사면 상방이라
//     U·V = (세그먼트 Z경사)×vUp. 즉 **링 세그먼트가 수평이 아니면 프레임이 비틀린다.**
{
    static (double MaxUV, double MinLen, double MaxLen, int Bad) Frame(List<WallPanels.Panel> pp)
    {
        double maxUV = 0, minL = double.MaxValue, maxL = 0; int bad = 0;
        foreach (var p in pp)
        {
            double ul = Math.Sqrt(p.UAxis.x * p.UAxis.x + p.UAxis.y * p.UAxis.y + p.UAxis.z * p.UAxis.z);
            double vl = Math.Sqrt(p.VAxis.x * p.VAxis.x + p.VAxis.y * p.VAxis.y + p.VAxis.z * p.VAxis.z);
            double wl = Math.Sqrt(p.WAxis.x * p.WAxis.x + p.WAxis.y * p.WAxis.y + p.WAxis.z * p.WAxis.z);
            double uv = Math.Abs(p.UAxis.x * p.VAxis.x + p.UAxis.y * p.VAxis.y + p.UAxis.z * p.VAxis.z);
            minL = Math.Min(minL, Math.Min(ul, Math.Min(vl, wl)));
            maxL = Math.Max(maxL, Math.Max(ul, Math.Max(vl, wl)));
            maxUV = Math.Max(maxUV, uv);
            if (uv > 1e-6 || Math.Abs(ul - 1) > 1e-6 || Math.Abs(vl - 1) > 1e-6 || Math.Abs(wl - 1) > 1e-6) bad++;
        }
        return (maxUV, minL == double.MaxValue ? 1 : minL, maxL, bad);
    }

    // (A) 평면 계획선 — 링이 전부 수평이라 U·V=0이어야 한다(현장에서도 이 부지는 성공했다).
    var bndA = new List<Point3> {
        new(185735.52,324643.97,191), new(185736.17,324644.29,191), new(185736.79,324644.69,191),
        new(185743.08,324649.27,191), new(185737.06,324663.34,191), new(185718.67,324655.47,191),
        new(185710.32,324659.72,191), new(185664.08,324639.92,191), new(185676.05,324611.98,191),
        new(185730.47,324635.28,191), new(185728.11,324640.80,191),
    };
    var gndA = new TiltGround(185664.08, 324611.98, 196.0, 0.30, 0.10);
    var prA = new GradingParams {
        CutBenchHeight = 2.5, FillBenchHeight = 2.5, CutBenchWidth = 1, FillBenchWidth = 1,
        CutSlope = 0.05, FillSlope = 0.05, CellSize = 0.5, MaxBenches = 50, VertexSpacing = 1.0,
        MinSlope = 0.05, MinFaceRun = 0.005, MiterConvex = true, MiterLimit = 2.0,
    };
    var vsA = GradingGeometry.Build(bndA, gndA, prA, true);
    var rsA = vsA.Rings.Select(r => (IReadOnlyList<Point3>)r).ToList();
    var psA = WallPanels.Generate(rsA, gndA, cut: true, slopeN: 0.05, joint: 0.05);
    var fA = Frame(psA);
    Console.WriteLine($"      (A)평면계획선: 패널 {psA.Count} · max|U·V| {fA.MaxUV:E2} · 축길이 [{fA.MinLen:F6}..{fA.MaxLen:F6}] · 비직교 {fA.Bad}장");
    Check("S23 (A) 평면 계획선은 프레임이 직교정규", fA.Bad == 0,
        $"max|U·V| {fA.MaxUV:E2} (허용 1e-6)");

    // (B) ★단차 계획선(3D 폴리선 — 한쪽이 3m 높다). 현장 구역1이 이 모양이다:
    //   계획선 정점 Z가 다르면 그 아래 사면 링도 **기울어진 세그먼트**를 갖는다 →
    //   U(세그먼트 3D 방향)가 더는 수평이 아니어서 V(사면 상방)와 직각이 아니게 된다.
    var bndB = new List<Point3> {
        new(185676.05,324611.98,191), new(185730.47,324635.28,191),
        new(185710.32,324659.72,194), new(185664.08,324639.92,194),
    };
    var gndB = new TiltGround(185664.08, 324611.98, 199.0, 0.30, 0.10);
    var vsB = GradingGeometry.Build(bndB, gndB, prA, true);
    var rsB = vsB.Rings.Select(r => (IReadOnlyList<Point3>)r).ToList();
    double ringDz = 0;
    foreach (var r in rsB)
    {
        double zmin = double.MaxValue, zmax = double.MinValue;
        foreach (var q in r) { zmin = Math.Min(zmin, q.Z); zmax = Math.Max(zmax, q.Z); }
        ringDz = Math.Max(ringDz, zmax - zmin);
    }
    var psB = WallPanels.Generate(rsB, gndB, cut: true, slopeN: 0.05, joint: 0.05);
    var fB = Frame(psB);
    Console.WriteLine($"      (B)단차계획선: 패널 {psB.Count} · 링 내 Z폭 최대 {ringDz:F2}m · max|U·V| {fB.MaxUV:E2}" +
                      $" · 축길이 [{fB.MinLen:F6}..{fB.MaxLen:F6}] · 비직교 {fB.Bad}장");
    Check("S23 ★단차 계획선에서 링이 기울어진다(재현 조건 성립)", ringDz > 0.01,
        $"링 내 Z폭 {ringDz:F2}m — 0이면 이 시나리오로는 재현 불가(다른 원인)");
    // ★ 내보내는 판넬은 **한 장도 비틀리지 않아야 한다** — 비틀린 프레임은 AutoCAD가 거부하고(옛 증상),
    //   억지로 그리면 부챗살처럼 벌어진다(0805 12:15 스샷). 기울면 그리지 않고 로그로 올리는 것이 현재 계약.
    Check("S23 ★내보낸 판넬은 전부 직교정규 프레임", fB.Bad == 0,
        $"비직교 {fB.Bad}장 / {psB.Count}장 · max|U·V| {fB.MaxUV:E2}");
    Check("S23 ★기울어진 링 위 판넬은 생략된다(부챗살 방지)",
        WallPanels.LastDiag.Contains("기울어진 판넬"),
        $"진단: {(WallPanels.LastDiag.Contains("기울어진 판넬") ? "생략 기록됨" : "★생략 기록 없음 — 벌어진 판넬이 나갔을 수 있음")}");

    // 자체검증 — 남은(=기울기 0.02 이하) 판넬은 직교화 전후로 **월드 좌표가 한 점도 안 움직여야** 한다.
    //   (u·U + v·V = (u+c·v)·U + (s·v)·V' 항등식). 어긋나면 벽 위치가 바뀐 것이다.
    WallPanels.DisableFrameFixForTest = true;
    List<WallPanels.Panel> psRaw;
    try { psRaw = WallPanels.Generate(rsB, gndB, cut: true, slopeN: 0.05, joint: 0.05); }
    finally { WallPanels.DisableFrameFixForTest = false; }
    double moved = 0;
    if (psB.Count == psRaw.Count)
        for (int i = 0; i < psB.Count; i++)
        {
            var a = psB[i].Poly; var b = psRaw[i].Poly;
            if (a.Count != b.Count) { moved = double.MaxValue; break; }
            for (int j = 0; j < a.Count; j++)
                moved = Math.Max(moved, Math.Max(Math.Abs(a[j].X - b[j].X),
                        Math.Max(Math.Abs(a[j].Y - b[j].Y), Math.Abs(a[j].Z - b[j].Z))));
        }
    else moved = double.MaxValue;
    Check("S23 ★기하 무변경(직교화는 표현만 바꾼다)", moved < 1e-9,
        $"패널 {psB.Count} vs {psRaw.Count} · 최대 좌표차 {(moved == double.MaxValue ? "패널 수/정점 수 불일치" : $"{moved:E2}m")}");

    // (C) 평면 계획선은 생략이 0장이어야 한다 — 안전장치가 정상 부지를 갉아먹지 않는지.
    Check("S23 ★평면 계획선은 한 장도 생략되지 않는다", !WallPanels.LastDiag.Contains("기울어진 판넬") || psA.Count > 0, "");
    var psA2 = WallPanels.Generate(rsA, gndA, cut: true, slopeN: 0.05, joint: 0.05);
    Check("S23 ★안전장치가 정상 부지를 건드리지 않는다",
        !WallPanels.LastDiag.Contains("기울어진 판넬") && psA2.Count == psA.Count,
        $"평면 계획선 패널 {psA2.Count}장 · 생략 없음");
}

// ★ S24 [옹벽 재설계 0805 — 옹벽선_재설계.md] 띠 분할 방식(WallBand)의 불변식.
//   종전 방식의 버그(v17.6 벽면 소실 · v17.7 감긴 호길이 · v17.8 keep 부호 · v18.2 프레임 비틀림)는
//   전부 '판넬이 모서리를 가로지르는 것'을 이웃 평면 절단으로 수습하려다 나왔다.
//   새 방식은 모서리에서 띠를 먼저 끊으므로 그 기계장치가 통째로 없다 — 그 사실을 여기서 못박는다.
{
    // 5m 높이 벽, ㄱ자 코너 하나. 토우/크레스트는 1:0.05 오프셋(수평 0.25m).
    static (List<Point3> Toe, List<Point3> Crest) LShape(double h, double n)
    {
        double off = n * h;   // 벽면 수평 물림
        var toe = new List<Point3> {
            new(0, 0, 100), new(20, 0, 100), new(20, 15, 100),
        };
        var crest = new List<Point3> {
            new(0, -off, 100 + h), new(20 + off, -off, 100 + h), new(20 + off, 15, 100 + h),
        };
        return (toe, crest);
    }

    var (toe1, crest1) = LShape(5.0, 0.05);
    var run1 = new WallRun { Up = true, Bench = 0, Toe = toe1, Crest = crest1, Height = 5.0 };

    // (A) 지반 없음(클립 없음) — 벽이 통째로 서야 한다.
    var t1 = WallBand.Slice(run1, null, joint: 0.05);
    Console.WriteLine($"      (A)ㄱ자벽: {WallBand.LastDiag}");
    Check("S24 판넬 생성됨", t1.Count > 0, $"판넬 {t1.Count}장");
    // ★ 앵커가 달릴 '온전' 판넬이 나와야 한다 — 균등 분배 때문에 열 폭이 상한보다 늘 조금 작은데,
    //   판정을 '열 폭 == 상한'으로 하면 온전이 0장이 되어 **앵커가 통째로 사라진다**(첫 구현의 실제 결함).
    int fullN = t1.FindAll(t => t.IsFull).Count;
    Check("S24 ★온전 판넬(앵커 대상)이 나온다", fullN > t1.Count / 2, $"온전 {fullN}/{t1.Count}장");

    // ★ 프레임이 전부 직교정규 — 이게 깨지면 AutoCAD가 거부하거나 부챗살이 된다(v18.2/18.3).
    double maxUV = 0, maxLenErr = 0;
    foreach (var t in t1)
    {
        double ul = Math.Sqrt(t.UAxis.x * t.UAxis.x + t.UAxis.y * t.UAxis.y + t.UAxis.z * t.UAxis.z);
        double vl = Math.Sqrt(t.VAxis.x * t.VAxis.x + t.VAxis.y * t.VAxis.y + t.VAxis.z * t.VAxis.z);
        double wl = Math.Sqrt(t.WAxis.x * t.WAxis.x + t.WAxis.y * t.WAxis.y + t.WAxis.z * t.WAxis.z);
        maxUV = Math.Max(maxUV, Math.Abs(t.UAxis.x * t.VAxis.x + t.UAxis.y * t.VAxis.y + t.UAxis.z * t.VAxis.z));
        maxLenErr = Math.Max(maxLenErr, Math.Max(Math.Abs(ul - 1), Math.Max(Math.Abs(vl - 1), Math.Abs(wl - 1))));
    }
    Check("S24 ★프레임이 전부 직교정규(비틀림 원천 소멸)", maxUV < 1e-9 && maxLenErr < 1e-9,
        $"max|U·V| {maxUV:E2} · 축길이 오차 {maxLenErr:E2}");

    // ★ 판넬 한 변이 설계 상한(1.67m)을 넘지 않는다 — v18.0 '거대 쐐기'가 다시 안 나오게.
    double maxSide = 0;
    foreach (var t in t1)
    {
        double mnU = double.MaxValue, mxU = double.MinValue, mnV = double.MaxValue, mxV = double.MinValue;
        foreach (var (u, v) in t.Local) { mnU = Math.Min(mnU, u); mxU = Math.Max(mxU, u); mnV = Math.Min(mnV, v); mxV = Math.Max(mxV, v); }
        maxSide = Math.Max(maxSide, Math.Max(mxU - mnU, mxV - mnV));
    }
    // ※모서리 겹침(cornerLap 0.10m)은 판넬을 이웃 벽 뒤로 **일부러** 더 내보내는 살이라 상한에 더해 준다.
    //   폭을 규격으로 통일한 뒤(v19.31)로는 열 폭이 정확히 1.667m라, 겹침이 붙은 끝 열이 1.717m가 된다.
    //   이 검사의 목적은 v18.0의 '거대 쐐기'(수 m짜리 판넬)를 막는 것이지 5cm 겹침을 잡는 게 아니다.
    Check("S24 ★판넬 한 변 ≤ 설계 상한(+모서리 겹침)", maxSide <= WallBand.MaxSide + 0.10 + 1e-6,
        $"최대 한 변 {maxSide:F3}m (상한 {WallBand.MaxSide:F3}m + 겹침 0.10m)");

    // ★ 모서리에서 벽면이 끊긴다 — 판넬이 코너를 가로지르지 않는다는 뜻.
    var segs = WallBand.SplitAtCorners(crest1);
    Check("S24 ★ㄱ자 코너에서 벽면이 끊긴다", segs.Count == 2, $"벽면 {segs.Count}조각 (기대 2)");

    // ★ 판넬이 코너를 가로지르지 않는다 — 각 판넬의 모든 정점이 한 직선 벽면 위(평면성).
    double maxPlaneErr = 0;
    foreach (var t in t1)
    {
        var nrm = t.WAxis; var o = t.Origin;
        foreach (var q in t.Poly)
            maxPlaneErr = Math.Max(maxPlaneErr, Math.Abs((q.X - o.X) * nrm.x + (q.Y - o.Y) * nrm.y + (q.Z - o.Z) * nrm.z));
    }
    Check("S24 ★판넬이 전부 평면(코너 가로지름 없음)", maxPlaneErr < 1e-9, $"최대 평면이탈 {maxPlaneErr:E2}m");

    // ★ 실오라기 없음 — 균등 분배라 수 mm짜리 자투리 열/행이 생기면 안 된다(v17.8 '줄눈 1690'의 정체).
    double minSide = double.MaxValue;
    foreach (var t in t1)
    {
        double mnU = double.MaxValue, mxU = double.MinValue;
        foreach (var (u, v) in t.Local) { mnU = Math.Min(mnU, u); mxU = Math.Max(mxU, u); }
        minSide = Math.Min(minSide, mxU - mnU);
    }
    Check("S24 ★자투리 실오라기 열 없음(균등 분배)", minSide > 0.3, $"최소 열 폭 {minSide:F3}m");

    // (B) 경사 지반 — 데이라잇 위로는 벽이 없어야 한다.
    var gnd24 = new SlopeGround(100.0, 0.15);   // z = 100 + 0.15x → x=20에서 103
    var t2 = WallBand.Slice(run1, gnd24, joint: 0.05);
    Console.WriteLine($"      (B)데이라잇: {WallBand.LastDiag}");
    double maxAbove = 0;
    foreach (var t in t2)
        foreach (var q in t.Poly)
        {
            gnd24.TryGetElevation(q.X, q.Y, out double gz);
            maxAbove = Math.Max(maxAbove, q.Z - gz);
        }
    Check("S24 ★데이라잇 위로 벽이 안 올라간다", maxAbove < 0.20, $"지반 위 최대 {maxAbove:F3}m");
    Check("S24 ★데이라잇 클립으로 판넬이 줄어든다", t2.Count > 0 && t2.Count < t1.Count,
        $"클립 없음 {t1.Count}장 → 클립 {t2.Count}장");

    // ★★ [v13.9 회귀 방지] 앵커·도넛이 달리는 '온전' 판넬은 **도넛(0.56m)이 통째로 판넬 안**에 있어야 한다.
    //   v13.9에서 이미 고쳤던 검사인데(도넛 네 모서리가 판넬 안인지) v19.0 재작성 때 빠뜨려
    //   데이라잇에 비스듬히 잘린 판넬에 앵커가 달려 지반 밖으로 삐져나왔다(JACK 0805 15:45 스샷).
    //   같은 일이 또 없도록 여기서 못박는다.
    {
        const double half = 0.30;   // v13.9 실측값(도넛 반폭 0.28 + 여유 0.02)
        int outN = 0;
        foreach (var t in t2)      // 데이라잇에 잘린 케이스로 검사해야 의미가 있다
        {
            if (!t.IsFull) continue;
            double cu = t.PocketU, cv = t.PocketV;
            foreach (var (du2, dv2) in new[] { (0.0, 0.0), (-half, -half), (half, -half), (-half, half), (half, half) })
                if (!PointInPolyLocal(cu + du2, cv + dv2, t.Local)) { outN++; break; }
        }
        Check("S24 ★온전 판넬은 도넛이 통째로 안에 들어온다(v13.9 회귀 방지)", outN == 0,
            $"도넛이 삐져나온 온전 판넬 {outN}장 / 온전 {t2.FindAll(x => x.IsFull).Count}장");
    }

    // ★★ [JACK 0805 '위에 패널이 있는데도 아래패널이 비스듬히 잘려버림'] 한 열(column) 안의 불변식:
    //   데이라잇 상한은 열마다 하나뿐이므로, **아래부터 연속으로 꽉 차고 잘린 것은 맨 위 하나뿐**이어야 한다.
    //   '위에 판넬이 있는데 아래가 잘렸다'는 이 불변식이 깨졌다는 뜻이다.
    //   한 열의 판넬들은 로컬 원점(Origin)이 같으므로 그것으로 묶는다.
    {
        var byCol = new Dictionary<string, List<WallBand.Tile>>();
        foreach (var t in t2)
        {
            string key = $"{t.Origin.X:F4}|{t.Origin.Y:F4}|{t.Origin.Z:F4}";
            if (!byCol.TryGetValue(key, out var lst)) byCol[key] = lst = new List<WallBand.Tile>();
            lst.Add(t);
        }
        int badCol = 0; string firstBad = "";
        foreach (var kv in byCol)
        {
            var col = kv.Value;
            // 각 판넬의 v 구간
            var spans = col.ConvertAll(t =>
            {
                double mn = double.MaxValue, mx = double.MinValue, mnU = double.MaxValue, mxU = double.MinValue;
                foreach (var (u, v) in t.Local) { mn = Math.Min(mn, v); mx = Math.Max(mx, v); mnU = Math.Min(mnU, u); mxU = Math.Max(mxU, u); }
                return (Lo: mn, Hi: mx, W: mxU - mnU, Full: t.IsFull);
            });
            spans.Sort((a, b) => a.Lo.CompareTo(b.Lo));
            // 맨 위(마지막) 판넬을 뺀 나머지는 전부 '온전'해야 한다.
            //   ★[0806] 판정 기준을 **폭 → 높이**로 바꾼다. 종전엔 '폭이 최대폭과 같은가'로 봤는데,
            //     판넬이 사다리꼴이 되면서(아랫변=토우·윗변=크레스트) **한 열 안에서도 행마다 폭이 다르다** —
            //     설계상 그런 것이지 잘린 게 아니다. 반면 데이라잇은 **위를 자르므로 높이가 줄어든다**.
            //     '잘렸는가'를 보려면 높이를 봐야 한다 — 원래 이 검사가 잡으려던 것도 그것이다.
            double hMax = 0; foreach (var s in spans) hMax = Math.Max(hMax, s.Hi - s.Lo);
            for (int i = 0; i + 1 < spans.Count; i++)
                if (spans[i].Hi - spans[i].Lo < hMax - 1e-6)
                {
                    badCol++;
                    if (firstBad.Length == 0)
                        firstBad = $"열 v[{spans[i].Lo:F2}..{spans[i].Hi:F2}] 높이 {spans[i].Hi - spans[i].Lo:F2} < 최대 {hMax:F2}" +
                                   $" (위에 판넬 {spans.Count - 1 - i}장 더 있음)";
                    break;
                }
        }
        Check("S24 ★한 열은 아래부터 연속으로 차고 잘린 건 맨 위 하나뿐", badCol == 0,
            $"불변식 깨진 열 {badCol}/{byCol.Count}개" + (firstBad.Length > 0 ? $" — {firstBad}" : ""));

        // ★★ 벽 꼭대기가 **지반을 따라가야** 한다 — 맨 위 조각이 버려지면 그 열만 한 행(1.6m)씩 뚝 낮아져
        //   옆 열과 어긋나고 화면엔 삼각형 구멍처럼 보인다(JACK 0805 '위에 패널이 있는데 아래가 잘림').
        //   각 열에서 가장 높은 판넬 정점과 그 자리 지반의 높이차를 잰다.
        double worstGap = 0; string gapAt = "";
        foreach (var kv in byCol)
        {
            double bestZ = double.MinValue; double bx = 0, by = 0;
            foreach (var t in kv.Value)
                foreach (var q in t.Poly)
                    if (q.Z > bestZ) { bestZ = q.Z; bx = q.X; by = q.Y; }
            if (bestZ == double.MinValue) continue;
            gnd24.TryGetElevation(bx, by, out double gz);
            double gap = gz - bestZ;                       // 지반이 위에 있으면 벽이 덜 올라온 것
            if (gap > worstGap) { worstGap = gap; gapAt = $"@{bx:F1},{by:F1} 지반 {gz:F2} vs 벽 {bestZ:F2}"; }
        }
        // 줄눈(0.025)과 사다리꼴 근사 오차만 남아야 한다 — 한 행(1.6m)씩 벌어지면 조각이 버려진 것이다.
        Check("S24 ★벽 꼭대기가 지반을 따라간다(한 행씩 주저앉지 않음)", worstGap < 0.30,
            $"최대 미달 {worstGap:F3}m {gapAt} (한 행 = {(5.0 / 3):F2}m)");
    }

    // ★★ [JACK 0805] 데이라잇은 판넬의 **귀퉁이만** 잘라야 한다 — 그러면 5각·6각이 나온다.
    //   종전엔 볼록성을 지키려고 윗변을 직선 하나로 퉁쳐서 '잘리는 지점부터 다음 꼭지점까지' 통째로 날렸고,
    //   결과가 **항상 사각형**이었다(JACK: '귀퉁이만 잘려야 되는데 항상 4각형으로만 만들어지네').
    {
        // ※'온전(IsFull)'로 세면 안 된다 — 귀퉁이만 잘린 5각형도 도넛이 안에 들어가면 온전이다.
        //   정점 수로만 센다.
        int quad = 0, penta = 0, more = 0;
        foreach (var t in t2)
        {
            int n = t.Local.Count;
            if (n <= 4) quad++; else if (n == 5) penta++; else more++;
        }
        Console.WriteLine($"      (B)잘린 판넬 모양: 4각 {quad} · 5각 {penta} · 6각+ {more}");
        Check("S24 ★잘린 판넬이 5각 이상으로도 나온다(귀퉁이만 잘림)", penta + more > 0,
            $"5각 {penta} · 6각+ {more} (전부 4각이면 윗변을 통째로 날린 것)");
        // 볼록성은 유지돼야 한다 — 무늬 클립이 볼록한 창에서만 옳다(115094).
        Check("S24 ★5각/6각이어도 볼록하다(무늬 클립 안전)", NonConvex(t2) == 0, $"오목 {NonConvex(t2)}장");
    }

    // ★ 데이라잇에 잘린 조각이 '삐죽한 바늘'이면 안 된다(JACK 0805 14:06 지적).
    //   벽이 사면으로 사그라드는 끝단에서 면적 0에 가까운 조각이 나오던 것.
    double minArea = double.MaxValue, minEdge = double.MaxValue;
    foreach (var t in t2)
    {
        double a2 = 0;
        for (int i = 0; i < t.Local.Count; i++)
        {
            var p = t.Local[i]; var q = t.Local[(i + 1) % t.Local.Count];
            a2 += p.u * q.v - q.u * p.v;
        }
        minArea = Math.Min(minArea, Math.Abs(a2) / 2);
        double mnU = double.MaxValue, mxU = double.MinValue, mnV2 = double.MaxValue, mxV2 = double.MinValue;
        foreach (var (u, v) in t.Local) { mnU = Math.Min(mnU, u); mxU = Math.Max(mxU, u); mnV2 = Math.Min(mnV2, v); mxV2 = Math.Max(mxV2, v); }
        minEdge = Math.Min(minEdge, Math.Min(mxU - mnU, mxV2 - mnV2));
    }
    Check("S24 ★삐죽한 실오라기 조각이 없다", minArea >= WallBand.SliverArea - 1e-9 && minEdge >= WallBand.SliverEdge - 1e-9,
        $"최소 면적 {minArea:F3}㎡(하한 {WallBand.SliverArea}) · 최소 변 {minEdge:F3}m(하한 {WallBand.SliverEdge})");

    // ★ 다각형에 중복·공선 정점이 없어야 한다 — 있으면 ACIS가 압출에 실패하고 명령창에
    //   '모델링 작업 오류 115094'를 대량으로 뿜는다(JACK 0805 14:31 실측).
    static (int Dup, int Col, int MaxN) PolyHealth(List<WallBand.Tile> tt)
    {
        int dup = 0, col = 0, maxN = 0;
        foreach (var t in tt)
        {
            var L = t.Local; int n = L.Count; maxN = Math.Max(maxN, n);
            for (int i = 0; i < n; i++)
            {
                var a = L[(i - 1 + n) % n]; var b = L[i]; var c = L[(i + 1) % n];
                if (Math.Abs(b.u - c.u) < 1e-6 && Math.Abs(b.v - c.v) < 1e-6) { dup++; continue; }
                double ax = c.u - a.u, ay = c.v - a.v, len = Math.Sqrt(ax * ax + ay * ay);
                if (len < 1e-9) continue;
                if (Math.Abs((b.u - a.u) * ay - (b.v - a.v) * ax) / len < 1e-4) col++;
            }
        }
        return (dup, col, maxN);
    }
    var h1 = PolyHealth(t1); var h2 = PolyHealth(t2);
    Check("S24 ★판넬 다각형에 중복·공선 정점 없음(ACIS 115094 방지)",
        h1.Dup == 0 && h1.Col == 0 && h2.Dup == 0 && h2.Col == 0,
        $"클립없음 중복{h1.Dup}·공선{h1.Col}(정점 최대 {h1.MaxN}) / 클립 중복{h2.Dup}·공선{h2.Col}(정점 최대 {h2.MaxN})");

    // ★★ 판넬은 **반드시 볼록**해야 한다 — 자연석 무늬는 돌을 판넬 모양에 맞춰 잘라내는데
    //   그 클립은 볼록한 창에서만 옳게 동작한다. 오목하면 자기교차 폴리라인이 나오고
    //   Region·Extrude에서 '모델링 작업 오류 115094'가 쏟아진다(JACK 0805 14:31·14:4x 실측 2회).
    static int NonConvex(List<WallBand.Tile> tt)
    {
        int bad = 0;
        foreach (var t in tt)
        {
            var L = t.Local; int n = L.Count;
            if (n < 3) { bad++; continue; }
            int sign = 0; bool ok = true;
            for (int i = 0; i < n; i++)
            {
                var a = L[i]; var b = L[(i + 1) % n]; var c = L[(i + 2) % n];
                double cr = (b.u - a.u) * (c.v - b.v) - (b.v - a.v) * (c.u - b.u);
                if (Math.Abs(cr) < 1e-9) continue;
                int s = cr > 0 ? 1 : -1;
                if (sign == 0) sign = s; else if (s != sign) { ok = false; break; }
            }
            if (!ok) bad++;
        }
        return bad;
    }
    Check("S24 ★판넬이 전부 볼록(무늬 클립·ACIS 안전)", NonConvex(t1) == 0 && NonConvex(t2) == 0,
        $"오목 판넬 클립없음 {NonConvex(t1)}장 / 클립 {NonConvex(t2)}장");

    // ★★ 판넬은 설계대로 **정사각**이어야 한다(5m 단 → 3행 × 1.67m).
    //   경사길이(4.996)를 한 변(1.663)으로 나눠 올림하면 3.004 → 4행이 되어 행 높이가 1.25m로 낮아진다.
    //   그러면 정착구 보호구역(0.66m)이 판넬 높이의 절반을 넘어 **가운데 세로줄 자연석이 통째로 사라진다**
    //   (JACK 0805 '돌무늬가 생기다 말았다'의 원인). 행 수는 설계 규칙에서 직접 정해야 한다.
    {
        double hMinT = double.MaxValue, hMaxT = 0, wMaxT = 0;
        foreach (var t in t1)
        {
            double mnV3 = double.MaxValue, mxV3 = double.MinValue, mnU3 = double.MaxValue, mxU3 = double.MinValue;
            foreach (var (u, v) in t.Local) { mnV3 = Math.Min(mnV3, v); mxV3 = Math.Max(mxV3, v); mnU3 = Math.Min(mnU3, u); mxU3 = Math.Max(mxU3, u); }
            hMinT = Math.Min(hMinT, mxV3 - mnV3); hMaxT = Math.Max(hMaxT, mxV3 - mnV3);
            wMaxT = Math.Max(wMaxT, mxU3 - mnU3);
        }
        // 줄눈(0.05) 인셋을 빼면 5m 단은 1.667−0.05 = 1.617 이어야 한다.
        Check("S24 ★판넬 행 높이가 설계값(단높이÷3)", hMinT > 1.5 && hMaxT < 1.7,
            $"행 높이 [{hMinT:F3}..{hMaxT:F3}]m (5m 단 → 1.62m 기대 · 1.25m면 4행으로 갈린 것)");
        // ★ 정착구 보호구역(0.66m)이 판넬 높이의 절반을 넘으면 안 된다 — 넘으면 무늬 가운데가 통째로 빈다.
        Check("S24 ★정착구 보호구역이 판넬 높이를 잡아먹지 않는다", hMinT > 0.66 * 1.4,
            $"판넬 높이 {hMinT:F3}m vs 보호구역 0.66m (여유 {hMinT / 0.66:F2}배)");
        Check("S24 ★판넬이 대체로 정사각", wMaxT / hMaxT < 1.4 && hMaxT / wMaxT < 1.4,
            $"최대 폭 {wMaxT:F3}m / 최대 높이 {hMaxT:F3}m");
    }

    // ★ 판넬이 **벽선을 따라간다** — 판넬 윗변이 크레스트 선에서 벗어나면 벽이 코너를 질러간 것이다.
    //   (평면성은 생성 방식상 항상 참이라 검사가 되지 않는다 — 실제로 깨지는 건 '선 추종'이다.)
    static double DistToPolyline2D(double x, double y, IReadOnlyList<Point3> line)
    {
        double best = double.MaxValue;
        for (int i = 0; i + 1 < line.Count; i++)
        {
            double ax = line[i].X, ay = line[i].Y, bx = line[i + 1].X, by = line[i + 1].Y;
            double dx = bx - ax, dy = by - ay, L2 = dx * dx + dy * dy;
            double t = L2 > 1e-12 ? ((x - ax) * dx + (y - ay) * dy) / L2 : 0;
            t = Math.Clamp(t, 0, 1);
            double px = ax + dx * t, py = ay + dy * t;
            best = Math.Min(best, Math.Sqrt((x - px) * (x - px) + (y - py) * (y - py)));
        }
        return best;
    }
    // 맨 아랫행만 본다 — 벽이 1:0.05로 기울어 윗행은 원래 토우선에서 떨어져 있으므로(높이×0.05)
    //   그 오프셋과 '코너 질러감'이 섞이면 지표가 무뎌진다. 아랫행은 토우선 위에 정확히 얹힌다.
    static double ToeDrift(List<WallBand.Tile> tt, IReadOnlyList<Point3> toe)
    {
        double worst = 0;
        foreach (var t in tt)
        {
            double mnV = double.MaxValue;
            foreach (var (u, v) in t.Local) mnV = Math.Min(mnV, v);
            if (mnV > 0.05) continue;                       // 맨 아랫행이 아니면 건너뜀
            for (int i = 0; i < t.Local.Count; i++)
            {
                if (t.Local[i].v > mnV + 1e-6) continue;    // 아랫변 정점만
                var q = t.Poly[i];
                worst = Math.Max(worst, DistToPolyline2D(q.X, q.Y, toe));
            }
        }
        return worst;
    }
    // 선추종은 **모서리 겹침을 끄고** 재야 한다 — 겹침(0.10m)은 코너를 메우려고 일부러 내보내는 양이라
    //   그걸 이탈로 세면 두 성질이 뒤섞여 어느 쪽이 깨졌는지 못 가린다.
    var t1NoLap = WallBand.Slice(run1, null, joint: 0.05, cornerLap: 0.0);
    double drift = ToeDrift(t1NoLap, toe1);
    Check("S24 ★판넬이 벽선을 따라간다(코너 질러감 없음)", drift < 0.05, $"겹침 OFF · 아랫변 최대 이탈 {drift:F4}m");

    // ★ 모서리 겹침이 실제로 코너를 메운다 — 벽면 끝 열이 겹침 길이만큼 더 나가야 한다(JACK '각진부 마감').
    double driftLap = ToeDrift(t1, toe1);
    // ★[0806] 하한을 0.08 → 0.06으로 내린다. 판넬이 사다리꼴이 되면서 **아랫변이 코너에서 정확히 끝나고**
    //   거기서 겹침 0.10m만 U 방향으로 더 나간다. 직각 코너면 그 0.10m의 **토우선까지 수직거리는
    //   0.10×sin45° ≈ 0.071m**다. 종전 0.08 하한은 아랫변이 코너를 지나쳐 나가던 시절의 값이라
    //   지금 기준으로는 '겹침이 없어야 통과'하는 셈이 된다 — 고쳐야 할 건 코드가 아니라 이 숫자다.
    Check("S24 ★모서리 겹침이 코너를 메운다", driftLap > 0.06 && driftLap < 0.18,
        $"겹침 ON · 아랫변 최대 이탈 {driftLap:F3}m (겹침 0.10m · 직각이면 수직거리 0.071m · 선추종 {drift:F3}m)");

    // (C) ★자체검증 — 코너 분할을 끄면(임계 179°) 판넬이 코너를 가로질러 벽선에서 벗어나야 한다.
    //   '항상 통과하는 검사는 검사가 아니다'(0805).
    // 코너 분할을 꺼도 **현(弦) 이탈 제한**이 대신 막아 준다 — 방어가 이중이라는 뜻(0805 추가).
    var t3 = WallBand.Slice(run1, null, joint: 0.05, cornerDeg: 179.0);
    double bugDrift = ToeDrift(t3, toe1);
    Check("S24 ★코너 분할을 꺼도 현 이탈 제한이 막아준다(이중 방어)", bugDrift < 0.20,
        $"분할 OFF → 아랫변 최대 이탈 {bugDrift:F3}m (제한 {WallBand.ChordTol}m + 겹침 0.10m)");

    // ★자체검증 — **둘 다** 끄면 반드시 재발해야 한다. 안 그러면 검사가 아니다.
    //   [0806] 사다리꼴도 같이 꺼야 한다 — 그게 아랫변을 토우에 맞춰 주므로, 켜 둔 채로는
    //   두 방어를 꺼도 아랫변이 멀쩡해 보여(0.02m) 검사가 무력해진다(방어가 삼중이 된 셈).
    WallBand.DisableChordLimitForTest = true;
    WallBand.DisableTrapezoidForTest = true;
    List<WallBand.Tile> t3b;
    try { t3b = WallBand.Slice(run1, null, joint: 0.05, cornerDeg: 179.0); }
    finally { WallBand.DisableChordLimitForTest = false; WallBand.DisableTrapezoidForTest = false; }
    double bugDrift2 = ToeDrift(t3b, toe1);
    Check("S24 ★검사 자체검증: 두 방어를 다 끄면 벽선을 크게 벗어난다", bugDrift2 > 0.2,
        $"둘 다 OFF → 아랫변 최대 이탈 {bugDrift2:F2}m (정상 {drift:F4}m)");

    // ★★ [JACK 0805 실측 0.285m] 판넬보다 좁은 커브에서 판넬이 현이 되어 안쪽으로 파고들면 안 된다.
    //   현장 역산 곡률 반경 ≈ 1m — 판넬 폭(1.66m)보다 좁다. 그 조건을 그대로 만든다.
    {
        var toeC = new List<Point3>(); var crC = new List<Point3>();
        const double R = 1.0, hC = 5.0, nC = 0.05;
        for (int i = 0; i <= 24; i++)   // 반경 1m 반원을 24조각(7.5°/조각 — NTS 라운드 11.25°보다 촘촘)
        {
            double a = Math.PI * i / 24;
            toeC.Add(new Point3(R * Math.Cos(a), R * Math.Sin(a), 100));
            double R2 = R + nC * hC;
            crC.Add(new Point3(R2 * Math.Cos(a), R2 * Math.Sin(a), 100 + hC));
        }
        var runC = new WallRun { Up = true, Bench = 0, Toe = toeC, Crest = crC, Height = hC };
        var tC = WallBand.Slice(runC, null, joint: 0.05);
        double devC = ToeDrift(tC, toeC);
        Console.WriteLine($"      (E)좁은커브(R=1m): 판넬 {tC.Count}장 · 아랫변 최대 이탈 {devC:F3}m · {WallBand.LastDiag}");
        Check("S24 (E) ★판넬보다 좁은 커브에서도 안쪽으로 안 파고든다", devC < 0.20,
            $"이탈 {devC:F3}m (현장 실측 0.285m가 증상 · 제한 {WallBand.ChordTol}m + 겹침 0.10m)");

        // [0806] 사다리꼴도 같이 끈다 — 안 끄면 그게 아랫변을 토우에 맞춰 주어 '제한을 껐는데도 멀쩡'해진다.
        WallBand.DisableChordLimitForTest = true;
        WallBand.DisableTrapezoidForTest = true;
        List<WallBand.Tile> tCb;
        try { tCb = WallBand.Slice(runC, null, joint: 0.05); }
        finally { WallBand.DisableChordLimitForTest = false; WallBand.DisableTrapezoidForTest = false; }
        double devCb = ToeDrift(tCb, toeC);
        Check("S24 (E) ★자체검증: 현 제한을 끄면 커브에서 파고든다", devCb > devC + 0.05,
            $"제한 OFF → 이탈 {devCb:F3}m (제한 ON {devC:F3}m)");
    }

    // ★ 어댑터 — 새 Tile이 기존 DWG 작성기의 Panel 계약을 만족해야 한다(작성기를 재사용하기 위함).
    {
        int badDir = 0, badPocket = 0;
        foreach (var t in t1)
        {
            var p = WallBand.ToPanel(t);
            // 앵커는 벽 뒤(흙 속)로 들어가고 아래로 기울어야 한다 — 앞으로 나오면 허공에 뜬다.
            if (p.IsFull)
            {
                double intoEarth = -(p.AnchorDir.x * t.WAxis.x + p.AnchorDir.y * t.WAxis.y + p.AnchorDir.z * t.WAxis.z);
                if (intoEarth < 0.5 || p.AnchorDir.z > -0.05) badDir++;
                // 정착구 중심은 판넬 폴리곤 안이어야 한다.
                double mnU = double.MaxValue, mxU = double.MinValue, mnV = double.MaxValue, mxV = double.MinValue;
                foreach (var (u, v) in t.Local) { mnU = Math.Min(mnU, u); mxU = Math.Max(mxU, u); mnV = Math.Min(mnV, v); mxV = Math.Max(mxV, v); }
                if (p.PocketU < mnU || p.PocketU > mxU || p.PocketV < mnV || p.PocketV > mxV) badPocket++;
            }
        }
        Check("S24 ★어댑터: 앵커가 벽 뒤로 비스듬히 박힌다", badDir == 0, $"방향 이상 {badDir}장");
        Check("S24 ★어댑터: 정착구가 판넬 안에 있다", badPocket == 0, $"이탈 {badPocket}장");
    }

    // (D) ★현장 조건 — 잘게 꺾인 벽선. 0805 12:37 단독 로그가 '벽면 62 · 모서리 62'였다:
    //   벽면 하나가 평균 1m도 안 돼 **판넬 한 변(1.67m)보다 짧다** → 옛 방식에선 거의 모든 판넬이
    //   '코너 판넬'로 양쪽 이웃 평면 절단을 받았고, 그게 조각·마감 불량의 조건이었다.
    //   새 방식이 이 조건에서 온전한지 못박는다(호를 1m 패싯으로 근사 — 라운드 사면형상과 같은 모양).
    {
        var toeR = new List<Point3>(); var crestR = new List<Point3>();
        const double R = 12.0, hR = 5.0, nR = 0.05;
        int nf = 40;                                  // 90°를 40조각 → 조각당 2.25°·길이 0.47m
        for (int i = 0; i <= nf; i++)
        {
            double a = Math.PI / 2 * i / nf;
            toeR.Add(new Point3(R * Math.Cos(a), R * Math.Sin(a), 100));
            double R2 = R + nR * hR;                  // 크레스트는 바깥으로 0.25m
            crestR.Add(new Point3(R2 * Math.Cos(a), R2 * Math.Sin(a), 100 + hR));
        }
        var runR = new WallRun { Up = true, Bench = 0, Toe = toeR, Crest = crestR, Height = hR };
        var tR = WallBand.Slice(runR, null, joint: 0.05);
        Console.WriteLine($"      (D)잘게꺾인벽: {WallBand.LastDiag}");

        Check("S24 (D) 잘게 꺾인 벽선에서도 판넬 생성", tR.Count > 0, $"판넬 {tR.Count}장");

        // ★ 실오라기 판넬이 없어야 한다 — 조각의 정체는 대개 폭 수 mm짜리 판넬이다.
        double minW = double.MaxValue, maxW = 0;
        foreach (var t in tR)
        {
            double mnU = double.MaxValue, mxU = double.MinValue;
            foreach (var (u, v) in t.Local) { mnU = Math.Min(mnU, u); mxU = Math.Max(mxU, u); }
            minW = Math.Min(minW, mxU - mnU); maxW = Math.Max(maxW, mxU - mnU);
        }
        Check("S24 (D) ★실오라기 판넬 없음", minW > 0.1, $"판넬 폭 [{minW:F3}..{maxW:F3}]m");

        // ★ 벽선을 따라간다 — 잘게 꺾여도 질러가면 안 된다(겹침은 끄고 순수 추종만 본다).
        var tRNoLap = WallBand.Slice(runR, null, joint: 0.05, cornerLap: 0.0);
        double driftR = ToeDrift(tRNoLap, toeR);
        Check("S24 (D) ★잘게 꺾여도 벽선을 따라간다", driftR < 0.05, $"겹침 OFF · 아랫변 최대 이탈 {driftR:F4}m");

        // ★ 프레임 직교정규 — 이 조건에서도 깨지면 안 된다.
        double uvR = 0;
        foreach (var t in tR)
            uvR = Math.Max(uvR, Math.Abs(t.UAxis.x * t.VAxis.x + t.UAxis.y * t.VAxis.y + t.UAxis.z * t.VAxis.z));
        Check("S24 (D) ★프레임 직교정규 유지", uvR < 1e-9, $"max|U·V| {uvR:E2}");

        // ★ 빈틈 없이 덮는다 — 판넬 면적 합 ≈ 벽면 전체 면적(줄눈 손실만큼만 작아야 한다).
        double area = 0;
        foreach (var t in tR)
        {
            double a2 = 0;
            for (int i = 0; i < t.Local.Count; i++)
            {
                var p = t.Local[i]; var q = t.Local[(i + 1) % t.Local.Count];
                a2 += p.u * q.v - q.u * p.v;
            }
            area += Math.Abs(a2) / 2;
        }
        double lenR = 0;
        for (int i = 0; i + 1 < crestR.Count; i++)
            lenR += Math.Sqrt(Math.Pow(crestR[i + 1].X - crestR[i].X, 2) + Math.Pow(crestR[i + 1].Y - crestR[i].Y, 2));
        double faceLen = Math.Sqrt(hR * hR + (nR * hR) * (nR * hR));
        double want = lenR * faceLen;
        Check("S24 (D) ★빈틈 없이 덮는다(면적 ≈ 벽면 전체)", area > want * 0.85 && area <= want + 1e-6,
            $"판넬 면적 {area:F1}㎡ / 벽면 {want:F1}㎡ ({area / want * 100:F1}% — 줄눈 손실만큼만 작아야)");
    }
}

// ★ S25 [옹벽 재설계 P2] 단 링 → 옹벽선(WallRun) 확정. 지표면을 만든 그 링에서 뽑는지, 구간을 정확히
//   따르는지, 그리고 그 선으로 판넬을 자르면 온전한지 — 한 줄로 이어 검증한다.
{
    var sq25 = new List<Point3> {
        new(0, 0, 100), new(60, 0, 100), new(60, 60, 100), new(0, 60, 100),
    };
    var gnd25 = new FlatGround(140);          // 절토 40m — 링이 넉넉히 나온다
    var pr25 = new GradingParams {
        CutBenchHeight = 5, FillBenchHeight = 5, CutBenchWidth = 1, FillBenchWidth = 1,
        CutSlope = 1.5, FillSlope = 1.5, CellSize = 1.0, MaxBenches = 30, MaxRise = 60,
        VertexSpacing = 2.0, MinSlope = 0.05, MinFaceRun = 0.005, MiterConvex = true, MiterLimit = 2.0,
    };
    var cum25 = GradingGeometry.CumLen2D(sq25);
    double tot25 = cum25[cum25.Length - 1];

    // 1단만 옹벽인 구간(둘레 앞쪽 1/4) — JACK이 실기한 '1단만 옹벽' 조건과 같다.
    //   ※ Wall(...)의 toBench는 **포함**이다 — '1단만'은 toBench:0 (toBench+1부터 전역 구배로 복귀).
    var z25 = new List<SlopeZone> { SlopeZone.Wall(0.0, tot25 * 0.25, 0, 0, 0.05, 1.5) };
    var vs25 = GradingGeometry.Build(sq25, gnd25, pr25, true, z25);
    var rs25 = vs25.Rings.Select(r => (IReadOnlyList<Point3>)r).ToList();

    var runs = WallRunBuilder.Build(sq25, rs25, z25, up: true, globalSlope: 1.5, minSlope: 0.05);
    Console.WriteLine($"      S25 {WallRunBuilder.LastDiag}");
    Check("S25 옹벽선이 나온다", runs.Count > 0, $"{runs.Count}줄");

    // ★ 1단만 옹벽이므로 Bench는 0만 나와야 한다 — 여기가 틀리면 엉뚱한 단에 벽이 선다.
    bool onlyB0 = runs.TrueForAll(r => r.Bench == 0);
    Check("S25 ★지정한 단(1단)에만 옹벽선", onlyB0, $"단 번호 {string.Join(",", runs.ConvertAll(r => r.Bench).Distinct())}");

    // ★ 옹벽선이 구간 안에만 있어야 한다 — 밖으로 새면 사면 자리에 벽이 선다.
    double outside = 0;
    foreach (var r in runs)
        foreach (var q in r.Crest)
        {
            double t = GradingGeometry.ParamAt(sq25, cum25, q.X, q.Y);
            if (!z25[0].Contains(t)) outside = Math.Max(outside, Math.Min(Math.Abs(t - z25[0].T1), Math.Abs(t - z25[0].T0)));
        }
    Check("S25 ★옹벽선이 지정 구간을 벗어나지 않는다", outside < 3.0, $"구간 밖 최대 {outside:F2}m (정점 간격 2m — 끝 한 칸 여유)");

    // ★ 토우/크레스트 대응 — 개수가 같고 수평 간격이 구배n×높이(0.25m)여야 한다.
    double gapMin = double.MaxValue, gapMax = 0; bool sameN = true;
    foreach (var r in runs)
    {
        if (r.Toe.Count != r.Crest.Count) { sameN = false; continue; }
        for (int i = 0; i < r.Toe.Count; i++)
        {
            double g = Math.Sqrt(Math.Pow(r.Crest[i].X - r.Toe[i].X, 2) + Math.Pow(r.Crest[i].Y - r.Toe[i].Y, 2));
            gapMin = Math.Min(gapMin, g); gapMax = Math.Max(gapMax, g);
        }
    }
    Check("S25 ★토우·크레스트 1:1 대응", sameN, "개수 일치");
    Check("S25 ★두 선 간격이 옹벽 구배와 맞다", gapMax < 0.60, $"수평 간격 [{gapMin:F3}..{gapMax:F3}]m (1:0.05×5m=0.25m 기대)");

    // ★ 높이가 단높이와 맞다.
    double hMin = double.MaxValue, hMax = 0;
    foreach (var r in runs) { hMin = Math.Min(hMin, r.Height); hMax = Math.Max(hMax, r.Height); }
    Check("S25 ★벽 높이 = 단높이", hMax <= 5.05 && hMin >= 4.95, $"높이 [{hMin:F2}..{hMax:F2}]m");

    // ★ 이 선으로 판넬을 자르면 온전해야 한다(P1과 이어붙인 통합 검증).
    int tiles = 0, fullT = 0; double uvMax = 0;
    foreach (var r in runs)
        foreach (var t in WallBand.Slice(r, gnd25, joint: 0.05))
        {
            tiles++; if (t.IsFull) fullT++;
            uvMax = Math.Max(uvMax, Math.Abs(t.UAxis.x * t.VAxis.x + t.UAxis.y * t.VAxis.y + t.UAxis.z * t.VAxis.z));
        }
    Check("S25 ★옹벽선→판넬 통합", tiles > 0 && fullT > 0, $"판넬 {tiles}장(온전 {fullT})");
    Check("S25 ★통합 후에도 프레임 직교정규", uvMax < 1e-9, $"max|U·V| {uvMax:E2}");

    // ★ 전체 옹벽(구간 없음, 전역 구배가 수직)도 잡아야 한다 — 구간 모드에만 의존하면 안 된다.
    var prW = new GradingParams {
        CutBenchHeight = 5, FillBenchHeight = 5, CutBenchWidth = 1, FillBenchWidth = 1,
        CutSlope = 0.05, FillSlope = 0.05, CellSize = 1.0, MaxBenches = 30, MaxRise = 60,
        VertexSpacing = 2.0, MinSlope = 0.05, MinFaceRun = 0.005, MiterConvex = true, MiterLimit = 2.0,
    };
    var vsW = GradingGeometry.Build(sq25, gnd25, prW, true);
    var rsW = vsW.Rings.Select(r => (IReadOnlyList<Point3>)r).ToList();
    var runsW = WallRunBuilder.Build(sq25, rsW, null, up: true, globalSlope: 0.05, minSlope: 0.05);
    Check("S25 ★전체 옹벽(구간 없음)도 옹벽선이 나온다", runsW.Count > 0, $"{runsW.Count}줄 · {WallRunBuilder.LastDiag}");

    // ★ 전역이 사면이고 구간도 없으면 옹벽선이 하나도 없어야 한다(사면 자리에 벽을 세우면 안 된다).
    //   ※ 링도 구간 없이 새로 만들어야 한다 — 구간으로 만든 링에는 벽이 실제로 들어 있으므로
    //     '기하로 판정'하는 새 방식은 (옳게) 그 벽을 찾아낸다.
    var vsNone = GradingGeometry.Build(sq25, gnd25, pr25, true);
    var rsNone = vsNone.Rings.Select(r => (IReadOnlyList<Point3>)r).ToList();
    var runsNone = WallRunBuilder.Build(sq25, rsNone, null, up: true, globalSlope: 1.5, minSlope: 0.05);
    Check("S25 ★사면뿐이면 옹벽선 0줄", runsNone.Count == 0, $"{runsNone.Count}줄");

    // ★ 전체 옹벽이면 벽면쌍마다 **한 줄씩**이어야 한다 — 모서리에서 끊기면 코너에 벽이 없어진다.
    //   (정점으로 판정하면 마이터 모서리 정점 간격이 커서 4토막으로 갈렸다 — 실제 결함이었다.)
    var perFace = new Dictionary<int, int>();
    foreach (var r in runsW) perFace[r.Bench] = perFace.TryGetValue(r.Bench, out var c) ? c + 1 : 1;
    int maxPerFace = 0;
    foreach (var kv in perFace) maxPerFace = Math.Max(maxPerFace, kv.Value);
    Check("S25 ★전체 옹벽은 단마다 한 줄(모서리에서 안 끊김)", maxPerFace == 1,
        $"단당 최대 {maxPerFace}줄 · 총 {runsW.Count}줄 / 단 {perFace.Count}개");

    // ── ★★ 가짜 긴 선분 탐지 — JACK 0805 13:44 '사선으로 존재하지 않는 옹벽' ──
    //   판넬은 균등 분할이라 옹벽선에 없는 긴 선분이 섞이면 그 위에 판넬이 **일정한 사슬**로 깔린다.
    //   원인이 무엇이든, 옹벽선의 어떤 선분도 원본 링의 최대 선분보다 크게 길 수 없다 — 이걸 불변식으로 못박는다.
    {
        static double MaxSeg(IReadOnlyList<Point3> pts)
        {
            double mx = 0;
            for (int i = 0; i + 1 < pts.Count; i++)
                mx = Math.Max(mx, Math.Sqrt(Math.Pow(pts[i + 1].X - pts[i].X, 2) + Math.Pow(pts[i + 1].Y - pts[i].Y, 2)));
            return mx;
        }
        static double RingMaxSeg(IReadOnlyList<IReadOnlyList<Point3>> rr)
        {
            double mx = 0;
            foreach (var r in rr)
            {
                mx = Math.Max(mx, MaxSeg(r));
                if (r.Count >= 2)  // 닫는 변(마지막→처음)도 실제 선분이다
                    mx = Math.Max(mx, Math.Sqrt(Math.Pow(r[0].X - r[r.Count - 1].X, 2) + Math.Pow(r[0].Y - r[r.Count - 1].Y, 2)));
            }
            return mx;
        }
        // 링이 첫 점을 끝에 중복해 닫는 형식인지 실측 — 이 가정이 틀리면 '닫힘' 판정이 어긋난다.
        var r1 = rs25[1];
        bool dupClosed = Math.Abs(r1[0].X - r1[r1.Count - 1].X) < 1e-9 && Math.Abs(r1[0].Y - r1[r1.Count - 1].Y) < 1e-9;
        Console.WriteLine($"      S25 링 형식: 첫점==끝점 {dupClosed} · 링[1] {r1.Count}점 · 최대선분 {MaxSeg(r1):F2}m");

        double ringMax = RingMaxSeg(rs25);
        double runMax = 0; int badRun = -1;
        for (int i = 0; i < runs.Count; i++)
        {
            double s = Math.Max(MaxSeg(runs[i].Crest), MaxSeg(runs[i].Toe));
            if (s > runMax) { runMax = s; badRun = i; }
        }
        Check("S25 ★옹벽선에 가짜 긴 선분이 없다(구간 모드)", runMax <= ringMax + 1e-6,
            $"옹벽선 최대선분 {runMax:F2}m vs 링 최대선분 {ringMax:F2}m (run #{badRun})");

        double ringMaxW = RingMaxSeg(rsW);
        double runMaxW = 0; int badW = -1;
        for (int i = 0; i < runsW.Count; i++)
        {
            double s = Math.Max(MaxSeg(runsW[i].Crest), MaxSeg(runsW[i].Toe));
            if (s > runMaxW) { runMaxW = s; badW = i; }
        }
        Check("S25 ★옹벽선에 가짜 긴 선분이 없다(전체 옹벽)", runMaxW <= ringMaxW + 1e-6,
            $"옹벽선 최대선분 {runMaxW:F2}m vs 링 최대선분 {ringMaxW:F2}m (run #{badW})");

        // ★ 링 시작점을 걸쳐 있는 구간(랩) — 인덱스 0 근처에서 이어져야 하는데 끊기거나 질러가기 쉬운 자리.
        var zWrap = new List<SlopeZone> { SlopeZone.Wall(tot25 * 0.9, tot25 * 0.1, 0, 0, 0.05, 1.5) };
        var vsWrap = GradingGeometry.Build(sq25, gnd25, pr25, true, zWrap);
        var rsWrap = vsWrap.Rings.Select(r => (IReadOnlyList<Point3>)r).ToList();
        var runsWrap = WallRunBuilder.Build(sq25, rsWrap, zWrap, up: true, globalSlope: 1.5, minSlope: 0.05);
        double ringMaxWrap = RingMaxSeg(rsWrap), runMaxWrap = 0;
        foreach (var r in runsWrap) runMaxWrap = Math.Max(runMaxWrap, Math.Max(MaxSeg(r.Crest), MaxSeg(r.Toe)));
        Console.WriteLine($"      S25 랩구간: {WallRunBuilder.LastDiag}");
        Check("S25 ★랩 구간(링 시작점 걸침)에도 가짜 선분 없다", runsWrap.Count > 0 && runMaxWrap <= ringMaxWrap + 1e-6,
            $"{runsWrap.Count}줄 · 옹벽선 최대선분 {runMaxWrap:F2}m vs 링 {ringMaxWrap:F2}m");

        // ★★ 옹벽↔사면 전환부의 긴 방사형 변이 옹벽선에 섞이면 안 된다 — 섞이면 그 변 위에 판넬이
        //   균등하게 깔려 '사선으로 존재하지 않는 옹벽'이 된다(JACK 0805 13:44 스샷).
        //   구간 모드 링은 전환부에서 0.25m ↔ 7.5m로 튀므로 7m급 변이 실제로 존재한다(실측 7.43m).
        //   옹벽선의 최대 변은 **정상 벽 변 수준(정점 간격)**이어야 한다 — 링 최대변이 아니라.
        double normalSpacing = 2.5;   // VertexSpacing 2.0 + 여유
        Check("S25 ★전환부 긴 변이 옹벽선에 안 섞인다(사선 옹벽 차단)", runMax <= normalSpacing,
            $"옹벽선 최대변 {runMax:F2}m (정상 ≤{normalSpacing}m · 링에는 {ringMax:F2}m짜리 전환변이 있다)");
        Check("S25 ★랩 구간도 전환부 변이 안 섞인다", runMaxWrap <= normalSpacing,
            $"옹벽선 최대변 {runMaxWrap:F2}m (링 {ringMaxWrap:F2}m)");

        // ★★ [0805 현장 실측 재현] 링 최대변 51.63m 중 10.29m가 옹벽선까지 들어와 벽이 엉뚱한 방향으로 뻗었다.
        //   그 변은 옹벽↔사면 **전환부의 방사형 변**이고, **토우 링에도 같은 자리에 나란한 방사형 변**이 있어
        //   간격 검사(3점)를 전부 통과했다. 길이 기준(중앙값의 4배)이 없으면 못 거른다.
        //   토우/크레스트 둘 다에 나란한 긴 변을 심어 그 상황을 그대로 만든다.
        {
            var toeX = new List<Point3>(); var crX = new List<Point3>();
            const double hX = 5.0, nX = 0.05, offX = nX * hX;   // 벽 간격 0.25m
            for (int i = 0; i <= 30; i++) { double x = i * 1.0; toeX.Add(new(x, 0, 100)); crX.Add(new(x, -offX, 100 + hX)); }
            // ← 여기서 두 선 모두 같은 방향으로 40m 방사 점프(전환부). 서로 0.25m 나란하다.
            toeX.Add(new Point3(30, 40, 100)); crX.Add(new Point3(30 - offX, 40, 100 + hX));
            for (int i = 1; i <= 10; i++) { toeX.Add(new(30 - i * 1.0, 40, 100)); crX.Add(new(30 - i * 1.0, 40 + offX, 100 + hX)); }
            var runX = new WallRun { Up = true, Bench = 0, Toe = toeX, Crest = crX, Height = hX };

            double MaxSegOf(IReadOnlyList<Point3> pts)
            {
                double mx = 0;
                for (int i = 0; i + 1 < pts.Count; i++)
                    mx = Math.Max(mx, Math.Sqrt(Math.Pow(pts[i + 1].X - pts[i].X, 2) + Math.Pow(pts[i + 1].Y - pts[i].Y, 2)));
                return mx;
            }
            Check("S25 ★재현 조건: 링에 40m급 방사형 변이 있다", MaxSegOf(crX) > 35,
                $"링 최대변 {MaxSegOf(crX):F1}m");

            // 이 옹벽선을 그대로 판넬로 자르면, 긴 변 위에 판넬이 사슬로 깔려 엉뚱한 방향으로 뻗는다.
            var tX = WallBand.Slice(runX, null, joint: 0.05);
            double farX = 0;
            foreach (var t in tX)
                foreach (var q in t.Poly)
                    if (q.Y > 1.0 && q.Y < 39.0) farX = Math.Max(farX, q.Y);   // 방사 변 위에 놓인 판넬
            Console.WriteLine($"      S25 전환변: 판넬 {tX.Count}장 · 방사변 위 판넬 최대 Y {farX:F1}m");

            // WallRunBuilder가 이런 변을 **애초에 옹벽선에 넣지 않는지**가 핵심이다.
            //   (여기서는 이미 만들어진 WallRun을 쓰므로, 소비 시점 관문이 막아주는지 본다.)
            var guarded = WallRunBuilder.SplitLongSegments(new[] { runX }, out string gdiag);
            double gMax = 0;
            foreach (var r in guarded) gMax = Math.Max(gMax, MaxSegOf(r.Crest));
            Check("S25 ★관문이 방사형 변을 끊는다", gMax < 2.5, $"관문 후 최대변 {gMax:F2}m · {gdiag}");
        }
    }

    // ── ★ 성토(아래로 내려가는 단) — 절토만 검사하면 못 잡는 자리 ──
    //   성토는 단이 **아래로** 내려가므로 rings[k]가 오히려 밑이다. 링 인덱스로 토우/크레스트를 정하면
    //   성토에서 두 선이 뒤바뀌어 ①벽면이 반대쪽을 봐서 무늬·앵커가 흙 속으로 향하고 ②데이라잇 판정이 뒤집힌다.
    //   (첫 구현이 실제로 그랬다 — 절토만 검사해 놓쳤다.)
    {
        var gndF = new FlatGround(60);        // 계획고 100 → 아래로 40m 성토
        var vsF = GradingGeometry.Build(sq25, gndF, prW, false);   // prW = 전역 1:0.05(전체 옹벽)
        var rsF = vsF.Rings.Select(r => (IReadOnlyList<Point3>)r).ToList();
        var runsF = WallRunBuilder.Build(sq25, rsF, null, up: false, globalSlope: 0.05, minSlope: 0.05);
        Console.WriteLine($"      S25 성토: {WallRunBuilder.LastDiag}");
        Check("S25 (성토) 옹벽선이 나온다", runsF.Count > 0, $"{runsF.Count}줄");

        // ★ 크레스트가 토우보다 위여야 한다 — 뒤바뀌면 벽이 거꾸로 선다.
        double worstInv = 0;
        foreach (var r in runsF)
            for (int i = 0; i < Math.Min(r.Toe.Count, r.Crest.Count); i++)
                worstInv = Math.Max(worstInv, r.Toe[i].Z - r.Crest[i].Z);
        Check("S25 (성토) ★크레스트가 토우보다 위", worstInv < 1e-6, $"토우가 더 높은 최대량 {worstInv:F3}m");

        // ★ 벽면이 **바깥**(부지 반대편)을 봐야 한다 — 성토 벽의 노출면은 바깥이다.
        //   부지 중심(30,30)에서 판넬 중심으로 가는 방향과 법선 W가 같은 쪽이면 바깥을 보는 것.
        int inward = 0, outward = 0; double vUpMin = 1;
        foreach (var r in runsF)
            foreach (var t in WallBand.Slice(r, gndF, joint: 0.05))
            {
                double cx = 0, cy = 0;
                foreach (var q in t.Poly) { cx += q.X; cy += q.Y; }
                cx /= t.Poly.Count; cy /= t.Poly.Count;
                double ox = cx - 30.0, oy = cy - 30.0;
                if (t.WAxis.x * ox + t.WAxis.y * oy > 0) outward++; else inward++;
                vUpMin = Math.Min(vUpMin, t.VAxis.z);          // V는 위를 향해야 한다
            }
        Check("S25 (성토) ★벽면이 바깥을 본다(무늬·앵커가 흙 속으로 안 감)", inward == 0 && outward > 0,
            $"바깥 {outward}장 · 안쪽 {inward}장");
        Check("S25 (성토) ★V축이 위를 향한다", vUpMin > 0.9, $"V.z 최소 {vUpMin:F3}");

        // ★ 절토도 같은 규칙으로 **안쪽**을 봐야 한다(대칭 확인) — 규칙이 절/성토 공용임을 못박는다.
        int cIn = 0, cOut = 0;
        foreach (var r in runsW)
            foreach (var t in WallBand.Slice(r, gnd25, joint: 0.05))
            {
                double cx = 0, cy = 0;
                foreach (var q in t.Poly) { cx += q.X; cy += q.Y; }
                cx /= t.Poly.Count; cy /= t.Poly.Count;
                if (t.WAxis.x * (cx - 30.0) + t.WAxis.y * (cy - 30.0) > 0) cOut++; else cIn++;
            }
        Check("S25 (절토) ★벽면이 부지 안쪽을 본다", cOut == 0 && cIn > 0, $"안쪽 {cIn}장 · 바깥 {cOut}장");

        // ★자체검증 — 토우/크레스트를 옛 방식(링 인덱스)으로 되돌리면 성토가 반드시 뒤집혀야 한다.
        //   '항상 통과하는 검사는 검사가 아니다'(0805).
        WallRunBuilder.DisableToeCrestOrderForTest = true;
        List<WallRun> bugF;
        try { bugF = WallRunBuilder.Build(sq25, rsF, null, up: false, globalSlope: 0.05, minSlope: 0.05); }
        finally { WallRunBuilder.DisableToeCrestOrderForTest = false; }
        double bugInv = 0; int bugIn = 0, bugOut = 0;
        foreach (var r in bugF)
        {
            for (int i = 0; i < Math.Min(r.Toe.Count, r.Crest.Count); i++)
                bugInv = Math.Max(bugInv, r.Toe[i].Z - r.Crest[i].Z);
            foreach (var t in WallBand.Slice(r, gndF, joint: 0.05))
            {
                double cx = 0, cy = 0;
                foreach (var q in t.Poly) { cx += q.X; cy += q.Y; }
                cx /= t.Poly.Count; cy /= t.Poly.Count;
                if (t.WAxis.x * (cx - 30.0) + t.WAxis.y * (cy - 30.0) > 0) bugOut++; else bugIn++;
            }
        }
        Check("S25 ★검사 자체검증: 링 인덱스로 정하면 성토가 뒤집힌다", bugInv > 1.0 && bugIn > 0,
            $"옛 방식 → 토우가 {bugInv:F1}m 더 높음 · 안쪽을 본 판넬 {bugIn}장(정상 0장)");
    }

    // ── ★ '이어서 하기' — 뒤 구역이 덮은 자리에서 앞 구역 옹벽선을 잘라낸다(이번 재설계의 핵심) ──
    {
        var r0 = runsW[0];                                   // 사각 부지를 한 바퀴 도는 1단 옹벽선
        double lenOf(WallRun r) { double L = 0; for (int i = 0; i + 1 < r.Crest.Count; i++)
            L += Math.Sqrt(Math.Pow(r.Crest[i + 1].X - r.Crest[i].X, 2) + Math.Pow(r.Crest[i + 1].Y - r.Crest[i].Y, 2)); return L; }
        double len0 = lenOf(r0);

        // 뒤 구역이 부지 동쪽 절반을 덮었다고 하자.
        var trimmed = WallRunBuilder.TrimBy(new[] { r0 }, (x, y) => x > 30.0);
        Console.WriteLine($"      S25 이어서: {WallRunBuilder.LastDiag}");
        Check("S25 ★덮인 자리를 잘라내고 남은 옹벽선이 있다", trimmed.Count > 0, $"{trimmed.Count}줄");

        double lenT = 0; foreach (var r in trimmed) lenT += lenOf(r);
        Check("S25 ★잘린 만큼 길이가 준다", lenT < len0 * 0.75 && lenT > len0 * 0.25,
            $"{len0:F1}m → {lenT:F1}m ({lenT / len0 * 100:F0}%)");

        // ★ 남은 옹벽선이 덮인 영역으로 넘어가면 안 된다 — 넘어가면 '최종 지표면에 없는 벽'이 남는다(v17.5 증상).
        double intrude = 0;
        foreach (var r in trimmed)
            foreach (var q in r.Crest) intrude = Math.Max(intrude, q.X - 30.0);
        Check("S25 ★남은 옹벽선이 덮인 영역을 침범하지 않는다", intrude < 2.0,
            $"최대 침범 {intrude:F2}m (세그먼트 단위라 정점 간격만큼 여유)");

        // ★ 전부 덮이면 옹벽선이 0줄이어야 한다 — 남으면 허공에 벽이 뜬다.
        var gone = WallRunBuilder.TrimBy(new[] { r0 }, (x, y) => true);
        Check("S25 ★전부 덮이면 옹벽선 0줄", gone.Count == 0, $"{gone.Count}줄");

        // ★ 아무것도 안 덮이면 그대로여야 한다(정상 부지를 갉아먹지 않는지).
        var same = WallRunBuilder.TrimBy(new[] { r0 }, (x, y) => false);
        Check("S25 ★안 덮이면 옹벽선 무변경", same.Count == 1 && Math.Abs(lenOf(same[0]) - len0) < 1e-9,
            $"{same.Count}줄 · {lenOf(same[0]):F1}m");

        // ★ 잘린 옹벽선으로 판넬을 잘라도 온전해야 한다(끝단이 깨지기 쉬운 자리).
        int tn = 0; double uv2 = 0;
        foreach (var r in trimmed)
            foreach (var t in WallBand.Slice(r, gnd25, joint: 0.05))
            { tn++; uv2 = Math.Max(uv2, Math.Abs(t.UAxis.x * t.VAxis.x + t.UAxis.y * t.VAxis.y + t.UAxis.z * t.VAxis.z)); }
        Check("S25 ★잘린 옹벽선도 판넬이 온전", tn > 0 && uv2 < 1e-9, $"판넬 {tn}장 · max|U·V| {uv2:E2}");
    }
}

// ★ S26 [치명-1 회귀 방지] **성토 벽은 데이라잇으로 자르지 않는다**(JACK 0721 확정 — 보강토와 동일 규칙):
//   크레스트가 지반 위면 꽉, 아니면 없음. 절토 규칙("설계면이 원지반보다 아래일 때 벽")과 **부호가 정반대**라
//   방향을 안 가르면 성토 벽은 토우가 지반 위여서 전부 '벽 없음'이 되어 **판넬이 한 장도 안 나온다**.
//   옛 구현(WallPanels.DayS)엔 있던 분기가 재작성 때 빠졌다 — 하니스가 성토를 안 봐서 못 잡았다.
{
    var toeF = new List<Point3> { new(0, 0, 100), new(20, 0, 100) };
    var crestF = new List<Point3> { new(0, -0.25, 105), new(20, -0.25, 105) };
    var runF = new WallRun { Up = false, Bench = 0, Toe = toeF, Crest = crestF, Height = 5.0 };

    var tF = WallBand.Slice(runF, new FlatGround(98.0), joint: 0.05);   // 지반 98 — 벽 전체가 지반 위(전형적 성토)
    Console.WriteLine($"      S26 성토(지반 아래): {WallBand.LastDiag}");
    Check("S26 ★성토 벽은 지반 위에 얹혀도 꽉 찬다", tF.Count > 10, $"판넬 {tF.Count}장");

    var tFb = WallBand.Slice(runF, new FlatGround(110.0), joint: 0.05);  // 지반 110 — 벽이 통째로 매몰
    Check("S26 ★매몰된 성토 벽은 판넬 0장", tFb.Count == 0, $"판넬 {tFb.Count}장");

    // 같은 형상을 절토로 주면 절토 규칙이 그대로 적용돼야 한다(성토 분기가 절토를 오염시키지 않는지).
    var runC2 = new WallRun { Up = true, Bench = 0, Toe = toeF, Crest = crestF, Height = 5.0 };
    var tC2 = WallBand.Slice(runC2, new FlatGround(98.0), joint: 0.05);
    Check("S26 ★절토 규칙은 그대로(토우가 지반 위면 벽 없음)", tC2.Count == 0, $"판넬 {tC2.Count}장");
}

// ★ S27 [치명-3 회귀 방지] 링 하나가 퇴화해 빠져도 그 위 옹벽이 살아 있어야 한다.
//   종전엔 `k += 2`로 링이 짝수 개라 가정해, 링 하나가 빠지면 짝이 어긋나 **그 단부터 위쪽 옹벽이 통째로 사라졌다.**
{
    var sq27 = new List<Point3> { new(0, 0, 100), new(50, 0, 100), new(50, 50, 100), new(0, 50, 100) };
    var gnd27 = new FlatGround(130);
    var pr27 = new GradingParams {
        CutBenchHeight = 5, FillBenchHeight = 5, CutBenchWidth = 1, FillBenchWidth = 1,
        CutSlope = 0.05, FillSlope = 0.05, CellSize = 1.0, MaxBenches = 30, MaxRise = 40,
        VertexSpacing = 2.0, MinSlope = 0.05, MinFaceRun = 0.005, MiterConvex = true, MiterLimit = 2.0,
    };
    var vs27 = GradingGeometry.Build(sq27, gnd27, pr27, true);
    var rs27 = vs27.Rings.Select(r => (IReadOnlyList<Point3>)r).ToList();

    var r1 = WallRunBuilder.Build(sq27, rs27, null, up: true, globalSlope: 0.05, minSlope: 0.05);
    int bench1 = r1.Select(x => x.Bench).Distinct().Count();
    Console.WriteLine($"      S27 정상 링: {WallRunBuilder.LastDiag}");
    Check("S27 정상 링 배열에서 단이 여러 개 나온다", bench1 >= 2, $"단 {bench1}개 · {r1.Count}줄");

    // ★소단 링 하나를 빼 인덱스를 한 칸 민다 — 옛 방식이면 여기서 0줄이 된다.
    var rsGap = new List<IReadOnlyList<Point3>>(rs27);
    if (rsGap.Count > 2) rsGap.RemoveAt(2);
    var r2 = WallRunBuilder.Build(sq27, rsGap, null, up: true, globalSlope: 0.05, minSlope: 0.05);
    Console.WriteLine($"      S27 링 하나 빠짐: {WallRunBuilder.LastDiag}");
    Check("S27 ★링 하나가 빠져도 옹벽선이 살아 있다", r2.Count > 0,
        $"{r2.Count}줄(정상 {r1.Count}줄) — 0줄이면 짝/홀 가정이 되살아난 것");
}

// ★ S28 [치명-2 회귀 방지] 링 평균 Z가 설계 단높이보다 수 mm 크게 나와도 행 수가 튀면 안 된다.
//   5.0002m에서 3행이 4행이 되면 행 높이가 1.67→1.25m로 낮아져 정착구 보호구역이 절반을 넘어
//   가운데 자연석이 사라진다(JACK '돌무늬가 생기다 말았다'와 같은 증상).
{
    static int RowsOf(List<WallBand.Tile> tt)
    {
        var s = new HashSet<string>();
        foreach (var t in tt)
        {
            double mn = double.MaxValue;
            foreach (var (u, v) in t.Local) mn = Math.Min(mn, v);
            s.Add(mn.ToString("F2"));
        }
        return s.Count;
    }
    var toeH = new List<Point3> { new(0, 0, 100), new(20, 0, 100) };
    foreach (double h in new[] { 5.0, 5.0002, 5.05 })
    {
        var crestH = new List<Point3> { new(0, -0.05 * h, 100 + h), new(20, -0.05 * h, 100 + h) };
        var runH = new WallRun { Up = true, Bench = 0, Toe = toeH, Crest = crestH, Height = h };
        var tH = WallBand.Slice(runH, null, joint: 0.05);
        int rows = RowsOf(tH);
        Check($"S28 ★단높이 {h}m → 3행(설계값)", rows == 3, $"행 {rows}개 · 판넬 {tH.Count}장");
    }
}

// ★ S29 [치명-4 회귀 방지] 토우↔크레스트 대응이 **인덱스**로 유지돼야 한다.
//   호길이로 맞추면 두 선의 길이가 다를 때(볼록 모서리에서 크레스트가 길다) 토우가 미끄러져
//   그 판넬의 V축 수평 성분이 설계 0.25m가 아니라 수십 cm가 되고 **판넬만 확 눕는다.**
{
    // 볼록 모서리가 여럿인 지그재그 벽선.
    //   ※크레스트를 **평행이동**으로 만들면 두 선 길이가 같아져 호길이 대응과 인덱스 대응이 우연히 일치한다
    //     — 그러면 이 검사가 버그를 못 잡는다. **실제 오프셋(모서리는 마이터)** 으로 만들어야
    //     볼록 모서리에서 크레스트가 길어지고 두 대응이 갈린다.
    static List<Point3> MiterOffset(IReadOnlyList<Point3> line, double d, double dz)
    {
        var o = new List<Point3>(line.Count);
        for (int i = 0; i < line.Count; i++)
        {
            double nx, ny;
            (double X, double Y) Nrm(int a, int b)
            {
                double dx = line[b].X - line[a].X, dy = line[b].Y - line[a].Y;
                double L = Math.Sqrt(dx * dx + dy * dy);
                return L < 1e-9 ? (0, 0) : (dy / L, -dx / L);   // 오른쪽 법선
            }
            if (i == 0) { var n = Nrm(0, 1); nx = n.X; ny = n.Y; }
            else if (i == line.Count - 1) { var n = Nrm(i - 1, i); nx = n.X; ny = n.Y; }
            else
            {
                var a = Nrm(i - 1, i); var b = Nrm(i, i + 1);
                double bx = a.X + b.X, by = a.Y + b.Y;
                double bl = Math.Sqrt(bx * bx + by * by);
                if (bl < 1e-9) { nx = a.X; ny = a.Y; }
                else
                {
                    double cos = (a.X * bx + a.Y * by) / bl;          // 이등분선과 법선 사이 각
                    double m = Math.Min(1 / Math.Max(cos, 1e-6), 4);  // 마이터 연장(상한 4배)
                    nx = bx / bl * m; ny = by / bl * m;
                }
            }
            o.Add(new Point3(line[i].X + nx * d, line[i].Y + ny * d, line[i].Z + dz));
        }
        return o;
    }
    const double hZ = 5.0, offZ = 0.25;
    var toeZ = new List<Point3>();
    foreach (var (X, Y) in new (double X, double Y)[] { (0,0), (10,0), (12,8), (22,8), (24,0), (34,0) })
        toeZ.Add(new Point3(X, Y, 100));
    var crZ = MiterOffset(toeZ, offZ, hZ);
    var runZ = new WallRun { Up = true, Bench = 0, Toe = toeZ, Crest = crZ, Height = hZ };
    {
        // ※총길이가 같아도 **구간별 분포**가 다르면 두 대응은 갈린다 — 그게 실제 조건이다.
        //   정점마다 '정규화 누적 호길이'의 차이를 재고, 그걸 미터로 환산하면 **토우가 미끄러지는 거리**다.
        static double[] CumOf(IReadOnlyList<Point3> p)
        {
            var c = new double[p.Count];
            for (int i = 1; i < p.Count; i++)
                c[i] = c[i-1] + Math.Sqrt(Math.Pow(p[i].X-p[i-1].X,2) + Math.Pow(p[i].Y-p[i-1].Y,2));
            return c;
        }
        var ct = CumOf(toeZ); var cc = CumOf(crZ);
        double Lt = ct[ct.Length-1], Lc = cc[cc.Length-1], slip = 0;
        for (int i = 0; i < ct.Length; i++) slip = Math.Max(slip, Math.Abs(ct[i]/Lt - cc[i]/Lc) * Lc);
        // 0.1m만 어긋나도 대응이 갈린다 — 실측 0.20m에서 자체검증이 실제로 재발했다(V수평 0.0499→0.0688).
        Check("S29 재현 조건: 두 선의 호길이 분포가 어긋난다(대응이 갈린다)", slip > 0.1,
            $"토우 {Lt:F2}m / 크레스트 {Lc:F2}m · 정점별 최대 미끄러짐 {slip:F2}m");
    }

    static double MaxVHorz(List<WallBand.Tile> tt)
    {
        double mx = 0;
        foreach (var t in tt) mx = Math.Max(mx, Math.Sqrt(t.VAxis.x * t.VAxis.x + t.VAxis.y * t.VAxis.y));
        return mx;
    }
    var tZ = WallBand.Slice(runZ, null, joint: 0.05);
    double vh = MaxVHorz(tZ);
    // 구배 1:0.05면 V의 수평 성분은 0.05/√(1+0.05²) ≈ 0.0499. 0.08을 넘으면 벽이 눕기 시작한 것이다.
    Check("S29 ★다코너 벽선에서 판넬이 눕지 않는다", vh < 0.08,
        $"V축 수평성분 최대 {vh:F4} (설계 1:0.05 → 0.050) · 판넬 {tZ.Count}장");

    WallBand.DisableIndexPairingForTest = true;
    List<WallBand.Tile> tZb;
    try { tZb = WallBand.Slice(runZ, null, joint: 0.05); }
    finally { WallBand.DisableIndexPairingForTest = false; }
    double vhb = MaxVHorz(tZb);
    Check("S29 ★자체검증: 호길이 대응으로 되돌리면 판넬이 눕는다", vhb > vh + 0.01,
        $"호길이 대응 → V수평 {vhb:F4} (인덱스 대응 {vh:F4})");
}

// ★ S30 [JACK 0806 '무늬패턴이 누락된 애들이 또 생겼어'] 오목한 판넬도 무늬가 모양대로 꽉 차야 한다.
//   무늬 클립(Sutherland–Hodgman)은 볼록한 창에서만 옳다. v19.20~v19.22는 이 제약을
//   '오목하면 무늬를 통째로 생략'으로 피했고, 현장에서 201장 중 25장이 민판으로 나왔다.
//   대신 창을 볼록 조각으로 쪼갠다 — 그 쪼개기가 **참된 분할**인지(빠짐·겹침 0)를 면적과 래스터로 잰다.
{
    // 실제 데이라잇 실루엣을 닮은 오목 6각형 — 윗변이 안쪽으로 한 번 꺾인다.
    var cav = new List<(double u, double v)> { (0, 0), (1.6, 0), (1.6, 1.2), (0.9, 0.55), (0.4, 1.3), (0, 1.0) };
    Check("S30 재현 조건: 이 판넬은 오목하다", !WallBand.IsConvex(cav), "볼록이면 이 검사는 아무것도 안 잡는다");

    var pcs = WallBand.ConvexPieces(cav);
    Check("S30 ★오목 판넬이 볼록 조각으로 쪼개진다", pcs.Count >= 2, $"{pcs.Count}조각");
    Check("S30 ★조각이 전부 볼록하다(115094 안전)", pcs.All(WallBand.IsConvex),
        $"볼록 {pcs.Count(WallBand.IsConvex)}/{pcs.Count}조각 — 하나라도 오목하면 클립이 자기교차를 만든다");

    double sumA = pcs.Sum(WallBand.PolyArea), whole = WallBand.PolyArea(cav);
    Check("S30 ★조각 면적의 합 = 원본 면적", Math.Abs(sumA - whole) < 1e-9,
        $"합 {sumA:F6}㎡ vs 원본 {whole:F6}㎡ (차 {Math.Abs(sumA - whole):E1})");

    // ★면적이 같아도 '한 곳은 겹치고 다른 곳은 비었다'면 합은 같을 수 있다 — 자리마다 직접 센다.
    //   판넬 안의 점은 조각 **정확히 하나**에, 밖의 점은 **하나도** 안 들어가야 한다.
    int dup = 0, gap = 0, spill = 0, inN = 0;
    for (int gi = 0; gi <= 160; gi++)
        for (int gj = 0; gj <= 130; gj++)
        {
            double u = gi * 0.01 + 0.0037, v = gj * 0.01 + 0.0041;   // 격자선·정점과 안 겹치게 살짝 어긋난 표본
            bool inPoly = PointInPolyLocal(u, v, cav);
            int hit = pcs.Count(pp => PointInPolyLocal(u, v, pp));
            if (inPoly) { inN++; if (hit == 0) gap++; else if (hit > 1) dup++; }
            else if (hit > 0) spill++;
        }
    Check("S30 ★조각들이 판넬을 정확히 한 번씩 덮는다(빠짐·겹침 0)", gap == 0 && dup == 0 && spill == 0,
        $"판넬 안 표본 {inN}개 · 빈 곳 {gap} · 겹친 곳 {dup} · 판넬 밖으로 삐져나온 곳 {spill}");

    // ★무늬가 실제로 얼마나 채워지는가 — 내보내기와 같은 순서(격자 돌 → 창마다 클립)로 재현한다.
    static List<(double u, double v)> ClipHalf(List<(double u, double v)> poly, (double u, double v) a, (double u, double v) b, bool ccw)
    {
        var o = new List<(double u, double v)>(poly.Count + 2);
        double Side((double u, double v) q) { double c = (b.u - a.u) * (q.v - a.v) - (b.v - a.v) * (q.u - a.u); return ccw ? c : -c; }
        for (int i = 0; i < poly.Count; i++)
        {
            var cur = poly[i]; var nxt = poly[(i + 1) % poly.Count];
            double sc = Side(cur), sn = Side(nxt);
            if (sc >= -1e-12) o.Add(cur);
            if ((sc >= -1e-12) != (sn >= -1e-12))
            {
                double t = sc / (sc - sn);
                o.Add((cur.u + (nxt.u - cur.u) * t, cur.v + (nxt.v - cur.v) * t));
            }
        }
        return o;
    }
    static double SignedOf(List<(double u, double v)> p)
    {
        double a = 0;
        for (int i = 0; i < p.Count; i++) { var s = p[i]; var t = p[(i + 1) % p.Count]; a += s.u * t.v - t.u * s.v; }
        return a / 2;
    }
    static double PatternArea(List<(double u, double v)> stone, List<List<(double u, double v)>> wins)
    {
        double a = 0;
        foreach (var w in wins)
        {
            bool ccw = SignedOf(w) > 0;
            var cl = stone;
            for (int e = 0; e < w.Count && cl.Count >= 3; e++) cl = ClipHalf(cl, w[e], w[(e + 1) % w.Count], ccw);
            if (cl.Count >= 3) a += WallBand.PolyArea(cl);
        }
        return a;
    }
    double covered = 0;
    for (int i = 0; i < 4; i++)
        for (int j = 0; j < 3; j++)
        {
            var st = new List<(double u, double v)> { (i * 0.4, j * 0.44), (i * 0.4 + 0.4, j * 0.44),
                                                      (i * 0.4 + 0.4, j * 0.44 + 0.44), (i * 0.4, j * 0.44 + 0.44) };
            covered += PatternArea(st, pcs);
        }
    Check("S30 ★무늬가 판넬 면적의 대부분을 덮는다", covered > whole * 0.95,
        $"무늬 {covered:F3}㎡ / 판넬 {whole:F3}㎡ ({covered / whole * 100:F0}%)");
    // ★자체검증 — v19.22까지의 '오목이면 생략'을 흉내내면 같은 판넬이 0㎡가 된다.
    Check("S30 ★자체검증: 종전 방식(오목이면 생략)이면 무늬가 0㎡", PatternArea(
            new List<(double u, double v)> { (0.2, 0.2), (1.4, 0.2), (1.4, 0.9), (0.2, 0.9) },
            WallBand.IsConvex(cav) ? pcs : new List<List<(double u, double v)>>()) == 0,
        "이 검사가 실패하면 재현이 안 된 것");

    // ★실제 Slice()가 뱉은 오목 판넬로도 되는가 — 손으로 만든 도형만 통과하면 의미가 없다.
    {
        var gnd30 = new WavyGround(103.0, 1.0, 0.5);   // 102~104m — 벽면(100~105m) 한가운데를 물결로 가로지른다
        var toe30 = new List<Point3>(); var cr30 = new List<Point3>();
        for (int i = 0; i <= 24; i++) { toe30.Add(new Point3(i, 0, 100)); cr30.Add(new Point3(i, -0.25, 105)); }
        var t30 = WallBand.Slice(new WallRun { Up = true, Bench = 0, Toe = toe30, Crest = cr30, Height = 5.0 },
                                 gnd30, joint: 0.05);
        var cavT = t30.Where(t => !WallBand.IsConvex(t.Local)).ToList();
        Console.WriteLine($"      S30 실제 Slice: 판넬 {t30.Count}장 중 오목 {cavT.Count}장 · {WallBand.LastDiag}");
        int bad = 0; double worst = 0;
        foreach (var t in cavT)
        {
            var q = WallBand.ConvexPieces(t.Local);
            double d = Math.Abs(q.Sum(WallBand.PolyArea) - WallBand.PolyArea(t.Local));
            if (q.Count == 0 || !q.All(WallBand.IsConvex) || d > 1e-9) bad++;
            worst = Math.Max(worst, d);
        }
        Check("S30 ★실제 데이라잇 오목 판넬도 전부 볼록 분해된다", cavT.Count > 0 && bad == 0,
            $"오목 {cavT.Count}장 · 실패 {bad}장 · 최대 면적차 {worst:E1}㎡");
    }
}

// ★ S31 [JACK 0806 '1단 높이 5m로만 계속 돌려왔다 — 2.5·3m로 바뀌어도 오류 없는지'] 단높이 스윕.
//   지금까지 현장·하니스가 **전부 5m**였다. 설계 규칙에 ≤1m→1행 / ≤3m→2행 / 초과→3행 이라는
//   **경계 두 개**가 있어서, 그 근처에서 행 높이가 뚝 떨어진다(3.0m→1.50m / 3.1m→1.03m).
//   행 높이가 낮아지면 정착구 보호구역(도넛 0.56m + 줄눈 0.05m×2 = 0.66m)이 판넬 높이의 큰 몫을
//   차지해 가운데 자연석이 사라진다 — v18.0에서 실제로 겪은 결함이다(JACK '돌무늬가 생기다 말았다').
{
    const double PocketZone = 0.66;   // 정착구 보호구역(Civil 쪽 Collar1Size 0.56 + GrooveW 0.05×2)
    static int RowsOfT(List<WallBand.Tile> tt)
    {
        var s = new HashSet<string>();
        foreach (var t in tt) { double mn = double.MaxValue; foreach (var (u, v) in t.Local) mn = Math.Min(mn, v); s.Add(mn.ToString("F2")); }
        return s.Count;
    }
    var tight = new List<string>();
    foreach (double h in new[] { 1.0, 1.5, 2.0, 2.5, 3.0, 3.05, 3.5, 4.0, 5.0, 5.0002, 6.0 })
    {
        var toe = new List<Point3>(); var cr = new List<Point3>();
        for (int i = 0; i <= 20; i++) { toe.Add(new Point3(i, 0, 100)); cr.Add(new Point3(i, -0.05 * h, 100 + h)); }
        var t31 = WallBand.Slice(new WallRun { Up = true, Bench = 0, Toe = toe, Crest = cr, Height = h }, null, joint: 0.05);

        Check($"S31 단높이 {h}m — 판넬이 나온다", t31.Count > 0, $"{t31.Count}장 · {WallBand.LastDiag}");

        int rows = RowsOfT(t31);
        int want = Math.Max(WallBand.RowsFor(h), (int)Math.Ceiling((h - 0.5) / WallBand.MaxSide - 1e-9));
        Check($"S31 단높이 {h}m — 행 {want}행(설계 규칙)", rows == want, $"실제 {rows}행 · 한 변 {WallBand.SideFor(h):F3}m");

        Check($"S31 단높이 {h}m — 한 변이 상한을 안 넘는다", WallBand.SideFor(h) <= WallBand.MaxSide + 1e-9,
            $"{WallBand.SideFor(h):F3}m / 상한 {WallBand.MaxSide:F3}m");

        // 퇴화 판넬(면적 0·자기교차)이 섞이면 안 된다 — 행 높이가 낮아질수록 위험하다.
        int degen = 0, cav = 0, splitBad = 0;
        foreach (var t in t31)
        {
            if (t.Local.Count < 3 || WallBand.PolyArea(t.Local) < 1e-6) { degen++; continue; }
            if (WallBand.IsConvex(t.Local)) continue;
            cav++;
            var q = WallBand.ConvexPieces(t.Local);
            if (q.Count == 0 || !q.All(WallBand.IsConvex)
                || Math.Abs(q.Sum(WallBand.PolyArea) - WallBand.PolyArea(t.Local)) > 1e-9) splitBad++;
        }
        Check($"S31 단높이 {h}m — 퇴화 판넬 0장 · 볼록 분해 실패 0장", degen == 0 && splitBad == 0,
            $"퇴화 {degen} · 오목 {cav} · 분해실패 {splitBad}");

        double rowH = h / rows;
        if (rowH < PocketZone * 1.6) tight.Add($"{h}m→행높이 {rowH:F2}m(보호구역이 {PocketZone / rowH * 100:F0}%)");

        // ★온전 판넬이 0장이면 앵커·정착구가 하나도 안 달린다 — 판넬은 멀쩡히 나오므로 숫자를 안 보면 모른다.
        //   v13.9 규칙: 판넬 한 변이 0.80m 미만이면 도넛(0.56m)이 안 들어가 온전 판정이 안 난다.
        int fullN = t31.FindAll(x => x.IsFull).Count;
        bool wantAnchor = WallBand.SideFor(h) - 0.05 >= 0.80 - 1e-9;
        Check($"S31 단높이 {h}m — 앵커 유무가 판넬 크기 규칙과 맞다", (fullN > 0) == wantAnchor,
            $"온전 {fullN}/{t31.Count}장 · 한 변 {WallBand.SideFor(h):F2}m" +
            (wantAnchor ? " (0.80m 이상 → 앵커 있어야 함)" : " (0.80m 미만 → 앵커 없는 게 규칙)"));
        Check($"S31 단높이 {h}m — 진단이 '앵커 없음'을 말해준다", fullN > 0 || WallBand.LastDiag.Contains("온전 판넬 0장"),
            "온전 0장인데 경고가 없으면 앵커 없는 옹벽이 조용히 나간다");
    }
    // ★정착구가 판넬을 잡아먹는 단높이 — 실패가 아니라 **설계 한계**로 기록한다(있는 그대로 남긴다).
    Console.WriteLine(tight.Count == 0
        ? "      S31 정착구 보호구역: 모든 단높이에서 행 높이의 63% 미만 — 여유 있음"
        : $"      S31 ⚠정착구가 빡빡한 단높이: {string.Join(" · ", tight)}");
    Check("S31 ★정착구 보호구역이 판넬 높이를 넘는 단높이는 없다",
        tight.TrueForAll(s => !s.Contains("(보호구역이 1")), $"{string.Join(" · ", tight)}");
}

// ★ S32 [0806 현장 재교정] '판넬 0장인 줄' 경고가 **정상에서 울리면 안 된다**.
//   현장 v19.27: `0/64(+0.1m)` — 토우가 지반보다 10cm 위인 줄이 0장인데 경고가 떴다. 그건 정상이다
//   (토우가 지반 위면 붙잡을 흙이 없어 벽 높이가 0). 기준은 거리가 아니라 **부호**여야 한다.
//   정상에서 울리는 경고는 진짜가 울릴 때 같이 무시당하므로, 이 눈금을 하니스로 못 박는다.
{
    var gnd32 = new FlatGround(110.0);
    static WallRun Run32(double toeZ, double h)
    {
        var toe = new List<Point3>(); var cr = new List<Point3>();
        for (int i = 0; i <= 20; i++) { toe.Add(new Point3(i, 0, toeZ)); cr.Add(new Point3(i, -0.05 * h, toeZ + h)); }
        return new WallRun { Up = true, Bench = 0, Toe = toe, Crest = cr, Height = h };
    }
    // 현장과 같은 층위: 완전히 묻힘 → 걸침 → 데이라잇 바로 위(+0.1m) → 한참 위(+5m)
    var lines32 = new[] { Run32(104.0, 5), Run32(108.0, 5), Run32(110.1, 5), Run32(115.0, 5) };
    WallBand.ResetTotals();
    var kept32 = new List<int>();
    foreach (var r in lines32) kept32.Add(WallBand.Slice(r, gnd32, joint: 0.05).Count);
    string tot32 = WallBand.TotalDiag;
    Console.WriteLine($"      S32 {tot32}");

    Check("S32 재현 조건: 묻힌 줄은 판넬이 나오고 뜬 줄 2개는 0장", kept32[0] > 0 && kept32[2] == 0 && kept32[3] == 0,
        $"줄별 {string.Join("/", kept32)}");
    Check("S32 ★데이라잇 바로 위(+0.1m)로 0장인 줄에 경고가 안 뜬다", !tot32.Contains("⚠토우가 지반 아래"),
        "정상에서 울리는 경고는 진짜가 울릴 때 같이 무시당한다");
    Check("S32 ★대신 '정상 — 붙잡을 흙 없음'으로 설명된다", tot32.Contains("정상 — 붙잡을 흙 없음"),
        "0장을 설명 없이 두면 다음 사람이 또 버그로 의심한다");
}

// ★ S33 [성토 실기 확인 전] 성토 옹벽이 **바깥을 보고** 서는지 — 지표면 생성부터 판넬까지 전 과정으로.
//   코드는 절/성토에 **같은 규칙**('크레스트→토우가 노출면 방향')을 쓴다. 절토에서 맞는 건 확인됐지만
//   성토는 토우·크레스트가 뒤바뀐 배치라, 규칙이 진짜 공용인지는 성토를 끝까지 돌려봐야 안다.
//   뒤집혀 있으면 벽이 흙 속을 보고 서고 앵커가 허공으로 나간다 — 스샷 한 장 볼 때까지 모른다.
{
    var sq33 = new List<Point3> { new(0, 0, 100), new(50, 0, 100), new(50, 50, 100), new(0, 50, 100) };
    double cx33 = 25, cy33 = 25;                       // 부지 중심 — '안/밖'의 기준
    var pr33 = new GradingParams {
        CutBenchHeight = 5, FillBenchHeight = 5, CutBenchWidth = 1, FillBenchWidth = 1,
        CutSlope = 0.05, FillSlope = 0.05, CellSize = 1.0, MaxBenches = 6, MaxRise = 20,
        VertexSpacing = 2.0, MinSlope = 0.05, MinFaceRun = 0.005, MiterConvex = true, MiterLimit = 2.0,
    };
    foreach (bool up33 in new[] { true, false })
    {
        string nm = up33 ? "절토" : "성토";
        var vs33 = GradingGeometry.Build(sq33, new FlatGround(up33 ? 130 : 70), pr33, up33);
        var rs33 = vs33.Rings.Select(r => (IReadOnlyList<Point3>)r).ToList();
        var runs33 = WallRunBuilder.Build(sq33, rs33, null, up: up33, globalSlope: 0.05, minSlope: 0.05);
        Check($"S33 {nm} — 옹벽선이 나온다", runs33.Count > 0, $"{runs33.Count}줄 · {WallRunBuilder.LastDiag}");

        var tt33 = new List<WallBand.Tile>();
        foreach (var r in runs33) tt33.AddRange(WallBand.Slice(r, null, joint: 0.05));
        Check($"S33 {nm} — 판넬이 나온다", tt33.Count > 0, $"{tt33.Count}장");

        // ★노출면 법선 W가 어느 쪽을 보는가 — 절토는 부지 **안**(파낸 쪽), 성토는 부지 **밖**.
        int wrong = 0; double worst = 0; string at = "";
        foreach (var t in tt33)
        {
            double toCx = cx33 - t.Origin.X, toCy = cy33 - t.Origin.Y;   // 판넬 → 부지 중심
            double L = Math.Sqrt(toCx * toCx + toCy * toCy);
            if (L < 1e-9) continue;
            double dot = (t.WAxis.x * toCx + t.WAxis.y * toCy) / L;      // >0 = 안쪽을 봄
            bool ok = up33 ? dot > 0.05 : dot < -0.05;
            if (!ok) { wrong++; if (Math.Abs(dot) > Math.Abs(worst)) { worst = dot; at = $"{t.Origin.X:F0},{t.Origin.Y:F0}"; } }
        }
        Check($"S33 ★{nm} 판넬이 {(up33 ? "부지 안" : "부지 밖")}을 본다", wrong == 0,
            $"어긋난 판넬 {wrong}/{tt33.Count}장" + (wrong > 0 ? $" · 최악 내적 {worst:F3} @ {at}" : ""));

        // ★앵커는 항상 흙 속(−W)으로 들어가야 한다 — 성토에선 부지 안쪽이다.
        int anc = 0, ancBad = 0;
        foreach (var t in tt33)
        {
            if (!t.IsFull) continue;
            var pn = WallBand.ToPanel(t, 20.0);
            double toCx = cx33 - t.Origin.X, toCy = cy33 - t.Origin.Y;
            double L = Math.Sqrt(toCx * toCx + toCy * toCy);
            if (L < 1e-9) continue;
            anc++;
            double dot = (pn.AnchorDir.x * toCx + pn.AnchorDir.y * toCy) / L;
            if (up33 ? dot > -0.05 : dot < 0.05) ancBad++;   // 절토: 산 쪽(바깥) / 성토: 부지 안쪽
            if (pn.AnchorDir.z > 0) ancBad++;                 // 앵커는 아래로 기운다
        }
        Check($"S33 ★{nm} 앵커가 흙 속으로 들어간다", anc > 0 && ancBad == 0, $"온전 {anc}장 · 어긋남 {ancBad}건");
    }
}

// ★ S34 [JACK 0806 '중간에 판넬 가로 넓이가 달라졌어' — 현장 실측 '벽면길이 0.06m를 1등분']
//   옹벽선을 1m로 조밀화할 때 남는 자투리가 모서리와 겹치면 **6cm짜리 벽면**이 생기고,
//   그 벽면이 자기 몫의 판넬을 한 장 받아 1.67m 판넬들 사이에 6cm 널빤지가 선다.
//   짧은 벽면은 이웃에 합쳐야 한다 — 단, **많이 꺾인 모서리는 가로지르면 안 된다**(평면이 깨진다).
{
    const double h34 = 5.0;
    double side34 = WallBand.SideFor(h34);
    // 곧은 벽 한가운데에 6cm 토막을 만든다 — 살짝(15°) 꺾였다 곧바로 되꺾이는 자투리.
    var pts34 = new List<(double X, double Y)> { (0, 0), (6, 0), (6.06, 0.016), (12, 0.016) };
    var toe34 = new List<Point3>(); var cr34 = new List<Point3>();
    foreach (var (X, Y) in pts34) { toe34.Add(new Point3(X, Y, 100)); cr34.Add(new Point3(X, Y - 0.05 * h34, 100 + h34)); }
    var run34 = new WallRun { Up = true, Bench = 0, Toe = toe34, Crest = cr34, Height = h34 };

    static double MinColW(List<WallBand.Tile> tt)
    {
        double mn = double.MaxValue;
        foreach (var t in tt)
        {
            double lo = double.MaxValue, hi = double.MinValue;
            foreach (var (u, v) in t.Local) { lo = Math.Min(lo, u); hi = Math.Max(hi, u); }
            mn = Math.Min(mn, hi - lo);
        }
        return mn == double.MaxValue ? 0 : mn;
    }

    // 자체검증 — 합치기를 끄면 현장과 같은 토막 벽면이 되살아난다.
    WallBand.DisableShortFaceMergeForTest = true;
    List<WallBand.Tile> tBad;
    var facesBad = WallBand.SplitAtCorners(cr34, 12.0);
    try { tBad = WallBand.Slice(run34, null, joint: 0.05); }
    finally { WallBand.DisableShortFaceMergeForTest = false; }
    double mnBad = MinColW(tBad);
    Check("S34 재현 조건: 합치기를 끄면 토막 벽면이 그대로 판넬이 된다", facesBad.Count >= 3 && mnBad < side34 * 0.5,
        $"벽면 {facesBad.Count}개 · 최소 판넬폭 {mnBad:F3}m (설계 {side34:F2}m)");

    var faces34 = WallBand.SplitAtCorners(cr34, 12.0, WallBand.MinFaceLenFor(side34));
    var t34 = WallBand.Slice(run34, null, joint: 0.05);
    double mn34 = MinColW(t34);
    Console.WriteLine($"      S34 합치기 켬: 벽면 {facesBad.Count}→{faces34.Count}개 · 최소 판넬폭 {mnBad:F3}→{mn34:F3}m · {WallBand.LastDiag}");
    Check("S34 ★토막 벽면이 이웃에 합쳐진다", faces34.Count < facesBad.Count,
        $"벽면 {facesBad.Count}→{faces34.Count}개");
    Check("S34 ★어떤 판넬도 자투리 하한보다 좁지 않다", mn34 >= WallBand.MinTailLen - 1e-6,
        $"최소 판넬폭 {mn34:F3}m / 하한 {WallBand.MinTailLen:F2}m");
    Check("S34 ★벽을 빠짐없이 덮는다(합치면서 구간이 새지 않았다)",
        Math.Abs(faces34[0].F0) < 1e-9 && Math.Abs(faces34[faces34.Count - 1].F1 - 1.0) < 1e-9
        && faces34.Zip(faces34.Skip(1), (a, b) => Math.Abs(a.F1 - b.F0) < 1e-9).All(x => x),
        $"구간 {string.Join(" ", faces34.ConvertAll(f => $"[{f.F0:F3}..{f.F1:F3}]"))}");

    // ★많이 꺾인 모서리(90°)는 가로지르면 안 된다 — 짧아도 합치지 않는다.
    var pts90 = new List<(double X, double Y)> { (0, 0), (6, 0), (6, 0.5), (0.5, 0.5), (0.5, 6) };
    var toe90 = new List<Point3>(); var cr90 = new List<Point3>();
    foreach (var (X, Y) in pts90) { toe90.Add(new Point3(X, Y, 100)); cr90.Add(new Point3(X, Y, 100 + h34)); }
    var f90 = WallBand.SplitAtCorners(cr90, 12.0, WallBand.MinFaceLenFor(side34));
    Check("S34 ★90° 코너 사이의 짧은 벽면(0.5m)은 합치지 않는다", f90.Count >= 4,
        $"벽면 {f90.Count}개 — 합쳐졌으면 판넬이 직각을 가로질러 평면이 깨진다");
}

// ★ S35 [JACK 0806 '가로길이를 높이에 따라 통일하되 맨 마지막에서 잘림으로 조절해'] 규격 폭 + 끝 자투리.
//   종전엔 벽면 길이를 열 수로 n등분해서 벽면마다 폭이 달랐다(현장 0.06~1.67m).
//   이제 **곧은 벽에서는 마지막 한 장만** 규격보다 좁아야 한다.
{
    static (double Min, double Max, int NonStd, int N) Widths(List<WallBand.Tile> tt, double std)
    {
        double mn = double.MaxValue, mx = 0; int ns = 0;
        var seen = new List<double>();
        foreach (var t in tt)
        {
            double lo = double.MaxValue, hi = double.MinValue;
            foreach (var (u, v) in t.Local) { lo = Math.Min(lo, u); hi = Math.Max(hi, u); }
            double w = hi - lo;
            mn = Math.Min(mn, w); mx = Math.Max(mx, w);
            if (w < std - 1e-6) ns++;
            seen.Add(w);
        }
        return (mn == double.MaxValue ? 0 : mn, mx, ns, seen.Count);
    }
    const double h35 = 5.0;
    double side35 = WallBand.SideFor(h35);
    foreach (double L in new[] { 10.0, 12.0, 20.0, 33.4 })
    {
        var toe35 = new List<Point3>(); var cr35 = new List<Point3>();
        for (double x = 0; x <= L + 1e-9; x += 1.0)                       // 현장처럼 1m 간격 조밀화
        { toe35.Add(new Point3(x, 0, 100)); cr35.Add(new Point3(x, -0.05 * h35, 100 + h35)); }
        if (toe35[toe35.Count - 1].X < L - 1e-9)
        { toe35.Add(new Point3(L, 0, 100)); cr35.Add(new Point3(L, -0.05 * h35, 100 + h35)); }
        var t35 = WallBand.Slice(new WallRun { Up = true, Bench = 0, Toe = toe35, Crest = cr35, Height = h35 },
                                 null, joint: 0.05);
        // 판넬 폭 = 열 폭 − 줄눈(0.05). 코너가 없는 곧은 벽이라 모서리 겹침은 안 붙는다.
        double std = side35 - 0.05;
        var w = Widths(t35, std);
        int rows = t35.Count > 0 ? 3 : 0;
        Console.WriteLine($"      S35 길이 {L}m: 폭 {w.Min:F3}~{w.Max:F3}m(규격 {std:F3}m) · 규격미만 {w.NonStd}/{w.N}장");
        // 끝에서만 조절하므로 좁은 열은 최대 2열(자투리가 짧아 마지막 두 장을 반씩 나눈 경우).
        Check($"S35 ★길이 {L}m — 규격보다 좁은 판넬은 끝의 1~2열뿐", w.NonStd <= 2 * rows,
            $"규격 미만 {w.NonStd}장(한 열 = {rows}장) · 폭 {w.Min:F3}~{w.Max:F3}m");
        Check($"S35 ★길이 {L}m — 규격 판넬은 정확히 {std:F2}m(상한 초과 없음)", w.Max <= std + 1e-6,
            $"최대 폭 {w.Max:F3}m / 규격 {std:F3}m");
        Check($"S35 ★길이 {L}m — 자투리도 한 변 절반 이상", w.Min >= side35 * 0.5 - 0.05 - 1e-6,
            $"최소 폭 {w.Min:F3}m / 하한 {side35 * 0.5 - 0.05:F3}m");
    }
}

// ★ S36 [JACK 0806 '오목부에서 빈공간 + 방향 어긋남'] 실험용 도면을 **오프라인으로 재현**한다.
//   JACK이 오목부를 여럿 만든 도면은 지워졌고 똑같이는 못 만든다 — 그런데 다시 만들 필요가 없다.
//   필요한 건 '그 도면'이 아니라 **그 조건**(깊은 오목부가 여럿인 경계)이고, 그건 여기서 만들면 된다.
//   이 저장소 규칙 그대로다: 가설은 현장 왕복이 아니라 하니스로 판정한다.
{
    // 빗 모양 경계 — 깊은 노치 두 개(오목 코너 4 + 볼록 코너 다수). 스샷의 지그재그 벽과 같은 조건.
    //   ★[0806] 노치 벽을 **비스듬히** 만든다. 처음엔 직각(90°)으로 했는데 현장 코너는 **80~82°**였고,
    //     90°에서 통과한 수정이 현장에서는 절반만 들었다(구멍 1.93m→0.64m). 시험 조건을 현장에 맞춘다 —
    //     직각만 시험하면 직각에서만 맞는 수정이 나온다.
    var bnd36 = new List<Point3>();
    foreach (var (X, Y) in new (double X, double Y)[] {
        (0,0), (30,0), (30,20), (22,20), (23.8,10), (17.6,10), (18,20), (10,20), (11.8,10), (5.6,10), (6,20), (0,20) })
        bnd36.Add(new Point3(X, Y, 100));
    var pr36 = new GradingParams {
        CutBenchHeight = 5, FillBenchHeight = 5, CutBenchWidth = 1, FillBenchWidth = 1,
        CutSlope = 0.05, FillSlope = 0.05, CellSize = 1.0, MaxBenches = 4, MaxRise = 20,
        VertexSpacing = 1.0, MinSlope = 0.05, MinFaceRun = 0.005, MiterConvex = true, MiterLimit = 2.0,
    };
    // 원지반을 벽면 중간 높이에 두어 데이라잇이 걸리게 한다(현장과 같은 조건).
    var gnd36 = new TiltGround(0, 0, 108.0, 0.10, 0.06);
    var vs36 = GradingGeometry.Build(bnd36, gnd36, pr36, true);
    var rs36 = vs36.Rings.Select(r => (IReadOnlyList<Point3>)r).ToList();
    var runs36 = WallRunBuilder.Build(bnd36, rs36, null, up: true, globalSlope: 0.05, minSlope: 0.05);
    Check("S36 재현 조건: 빗 모양 부지에서 옹벽선이 나온다", runs36.Count > 0,
        $"{runs36.Count}줄 · 링 {rs36.Count}개");

    WallBand.ResetTotals();
    var t36 = new List<WallBand.Tile>();
    foreach (var r in runs36) t36.AddRange(WallBand.Slice(r, gnd36, joint: 0.05));
    Console.WriteLine($"      S36 전체: {WallBand.TotalDiag}");
    Console.WriteLine($"      S36 틈  : {WallBand.GapReport(t36)}");
    Check("S36 재현 조건: 판넬이 충분히 나온다(코너가 여럿 포함될 만큼)", t36.Count > 30, $"{t36.Count}장");

    // ★핵심 판정 — 양옆이 온전한데 벌어진 자리(=진짜 구멍)가 있으면 재현된 것이다.
    string gr36 = WallBand.GapReport(t36);
    int realHole = 0;
    {
        const string key36 = "진짜 구멍 ";
        int p = gr36.IndexOf(key36);
        if (p >= 0)
        {
            int q = gr36.IndexOf('곳', p);
            if (q > p) int.TryParse(gr36.Substring(p + key36.Length, q - p - key36.Length).Trim(), out realHole);
        }
    }
    // ★구멍 자리의 **실제 판넬 두 장**을 찍는다 — 겹침을 키웠더니 틈이 0.43→0.64m로 더 벌어졌다.
    //   내 기하 모델(오목 코너에서 아래가 2d 벌어진다)이 틀렸다는 뜻이므로, 좌표를 직접 본다.
    {
        // ★구멍 자리의 **옹벽선 자체**(크레스트·토우)를 찍는다 — 판넬이 아니라 입력이 문제일 수 있다.
        //   오목 코너에서는 크레스트(윗선)가 토우(아랫선)보다 **짧다**. 그런데 옹벽선은
        //   `토우[i] = 크레스트[i]의 최근접 토우점`으로 만들어지므로, 크레스트가 짧으면
        //   토우의 코너 부근 구간에 **대응되는 크레스트 정점이 없어** 그 만큼이 통째로 안 깔린다.
        {
            var run = runs36.Find(r => r.Crest.Count > 2);
            if (run != null)
            {
                double bx = 22, by = 20; int bi = -1; double bd = double.MaxValue;
                for (int i = 0; i < run.Crest.Count; i++)
                {
                    double d = Math.Sqrt(Math.Pow(run.Crest[i].X - bx, 2) + Math.Pow(run.Crest[i].Y - by, 2));
                    if (d < bd) { bd = d; bi = i; }
                }
                Console.WriteLine($"      S36 코너 부근 옹벽선(크레스트 {run.Crest.Count}점 · 가장 가까운 정점 {bi}):");
                for (int i = Math.Max(0, bi - 3); i <= Math.Min(run.Crest.Count - 1, bi + 3); i++)
                {
                    var c = run.Crest[i]; var t = run.Toe[i];
                    double stepC = i > 0 ? Math.Sqrt(Math.Pow(c.X - run.Crest[i-1].X, 2) + Math.Pow(c.Y - run.Crest[i-1].Y, 2)) : 0;
                    double stepT = i > 0 ? Math.Sqrt(Math.Pow(t.X - run.Toe[i-1].X, 2) + Math.Pow(t.Y - run.Toe[i-1].Y, 2)) : 0;
                    Console.WriteLine($"        [{i}] 크레스트({c.X:F2},{c.Y:F2}) 토우({t.X:F2},{t.Y:F2})" +
                                      $" · 크레스트간격 {stepC:F3} 토우간격 {stepT:F3}" +
                                      $"{(stepT > stepC * 2 + 0.2 ? "  ★토우가 크레스트보다 훨씬 벌어짐 = 이 사이가 안 깔린다" : "")}");
                }
            }
        }
        double hx = 22, hy = 20;                                  // S36이 짚은 최대 구멍 자리(3.61m)
        Console.WriteLine($"      S36 구멍 자리({hx},{hy}) 주변 판넬:");
        var near = new List<(double D, WallBand.Tile T)>();
        foreach (var t in t36)
        {
            double d = Math.Sqrt((t.Origin.X - hx) * (t.Origin.X - hx) + (t.Origin.Y - hy) * (t.Origin.Y - hy));
            if (d < 3.0) near.Add((d, t));
        }
        // ★코너로 들어오는 면(x=22, U가 세로)의 판넬이 어디까지 깔렸는지 — 행0만 Y순으로.
        Console.WriteLine("      S36 구멍 자리(9,13) 옆면 행0 판넬 Y 분포:");
        var side = t36.FindAll(t => t.Row == 0 && Math.Abs(t.UAxis.x) < 0.3 && Math.Abs(t.Origin.X - 9.0) < 1.2);
        side.Sort((a, b) => a.Origin.Y.CompareTo(b.Origin.Y));
        foreach (var t in side)
        {
            double u0 = double.MaxValue, u1 = double.MinValue;
            foreach (var (u, v) in t.Local) { u0 = Math.Min(u0, u); u1 = Math.Max(u1, u); }
            Console.WriteLine($"        원점({t.Origin.X:F2},{t.Origin.Y:F2}) U({t.UAxis.x:F2},{t.UAxis.y:F2})" +
                              $" → Y {t.Origin.Y + u0 * t.UAxis.y:F2}~{t.Origin.Y + u1 * t.UAxis.y:F2}");
        }
        near.Sort((a, b) => a.D.CompareTo(b.D));
        for (int k = 0; k < near.Count && k < 6; k++)
        {
            var t = near[k].T;
            double u0 = double.MaxValue, u1 = double.MinValue, v0 = double.MaxValue, v1 = double.MinValue;
            foreach (var (u, v) in t.Local) { u0 = Math.Min(u0, u); u1 = Math.Max(u1, u); v0 = Math.Min(v0, v); v1 = Math.Max(v1, v); }
            double lx = t.Origin.X + u0 * t.UAxis.x, ly = t.Origin.Y + u0 * t.UAxis.y;
            double rx = t.Origin.X + u1 * t.UAxis.x, ry = t.Origin.Y + u1 * t.UAxis.y;
            Console.WriteLine($"        행{t.Row} 원점({t.Origin.X:F2},{t.Origin.Y:F2}) U({t.UAxis.x:F2},{t.UAxis.y:F2})" +
                              $" u[{u0:F2}..{u1:F2}] v[{v0:F2}..{v1:F2}] → 좌({lx:F2},{ly:F2}) 우({rx:F2},{ry:F2})");
        }
    }
    Check("S36 ★오목부 다수 부지에 진짜 구멍이 없다", realHole == 0,
        $"진짜 구멍 {realHole}곳");

    // ★자체검증 — 수정 둘을 각각 끄면 그 몫의 구멍이 되살아나야 한다. 안 되살아나면 무관한 수정이다.
    static int Holes36(List<WallBand.Tile> tt)
    {
        string s = WallBand.GapReport(tt);
        const string k = "진짜 구멍 ";
        int a = s.IndexOf(k); if (a < 0) return 0;
        int b = s.IndexOf('곳', a); if (b <= a) return 0;
        int.TryParse(s.Substring(a + k.Length, b - a - k.Length).Trim(), out int n); return n;
    }
    // ★[JACK 0806 '공백은 사라졌는데 어긋남은 여전히 있어'] 틈이 0이어도 **선형**은 따로 재야 한다.
    //   판넬 아랫변이 아랫선(토우)을 실제로 따라가는가 — 코너를 가로지르면 현(弦)이 되어 벗어난다.
    {
        double worstOff = 0; double wx = 0, wy = 0; WallBand.Tile worstT = t36[0];
        foreach (var t in t36)
        {
            // ★아랫변은 **v가 가장 낮은 정점들**로 잡아야 한다. u 범위 전체를 쓰면 윗변이 더 넓은
            //   사다리꼴에서 있지도 않은 아랫변을 재게 된다(내 첫 측정이 그래서 사다리꼴 수정을 못 읽었다).
            double v0 = double.MaxValue;
            foreach (var (u, v) in t.Local) v0 = Math.Min(v0, v);
            // ★[0806] **맨 아랫행만** 본다. 벽이 1:0.05로 기울어 윗행은 원래 토우선에서 떨어져 있으므로
            //   (높이 3.4m면 수평으로 0.17m) 그걸 이탈로 세면 정상적인 기울기가 결함으로 찍힌다.
            //   S24의 ToeDrift는 처음부터 이렇게 걸러 왔는데 내 새 검사에 그 조건을 안 옮겼다 — 오늘 여덟 번째 자 오류.
            if (v0 > 0.05) continue;
            double u0 = double.MaxValue, u1 = double.MinValue;
            foreach (var (u, v) in t.Local) if (v < v0 + 0.02) { u0 = Math.Min(u0, u); u1 = Math.Max(u1, u); }
            if (u1 <= u0) continue;
            // 아랫변을 5등분해 각 점이 토우선에서 얼마나 떨어졌는지.
            for (int s = 0; s <= 5; s++)
            {
                double u = u0 + (u1 - u0) * s / 5.0;
                double px = t.Origin.X + u * t.UAxis.x + v0 * t.VAxis.x;
                double py = t.Origin.Y + u * t.UAxis.y + v0 * t.VAxis.y;
                double best = double.MaxValue;
                foreach (var r in runs36)
                    for (int k = 0; k + 1 < r.Toe.Count; k++)
                    {
                        double dx = r.Toe[k+1].X - r.Toe[k].X, dy = r.Toe[k+1].Y - r.Toe[k].Y, L2 = dx*dx + dy*dy;
                        if (L2 < 1e-12) continue;
                        double tt = Math.Clamp(((px - r.Toe[k].X)*dx + (py - r.Toe[k].Y)*dy) / L2, 0, 1);
                        double qx = r.Toe[k].X + dx*tt, qy = r.Toe[k].Y + dy*tt;
                        double d = Math.Sqrt((px-qx)*(px-qx) + (py-qy)*(py-qy));
                        if (d < best) best = d;
                    }
                if (best > worstOff) { worstOff = best; wx = px; wy = py; worstT = t; }
            }
        }
        Console.WriteLine($"      S36 판넬 아랫변↔토우선 최대 이탈: {worstOff:F3}m @ {wx:F1},{wy:F1}");
        // ★가장 어긋난 판넬의 정체를 찍는다 — 두 번 연속 헛짚었으니 이번엔 좌표부터 본다.
        {
            double a0 = double.MaxValue, a1 = double.MinValue;
            foreach (var (u, v) in worstT.Local) { a0 = Math.Min(a0, u); a1 = Math.Max(a1, u); }
            Console.WriteLine($"        그 판넬: 원점({worstT.Origin.X:F2},{worstT.Origin.Y:F2}) U({worstT.UAxis.x:F2},{worstT.UAxis.y:F2})" +
                              $" u[{a0:F2}..{a1:F2}] 행{worstT.Row} 단{worstT.Bench}" +
                              $" → 아랫변 ({worstT.Origin.X + a0*worstT.UAxis.x:F2},{worstT.Origin.Y + a0*worstT.UAxis.y:F2})" +
                              $" ~ ({worstT.Origin.X + a1*worstT.UAxis.x:F2},{worstT.Origin.Y + a1*worstT.UAxis.y:F2})");
            foreach (var r in runs36)
            {
                int hit = -1; double hd = double.MaxValue;
                for (int k = 0; k < r.Toe.Count; k++)
                {
                    double d = Math.Sqrt(Math.Pow(r.Toe[k].X - worstT.Origin.X, 2) + Math.Pow(r.Toe[k].Y - worstT.Origin.Y, 2));
                    if (d < hd) { hd = d; hit = k; }
                }
                if (hd > 0.3) continue;
                Console.WriteLine($"        그 줄 토우 정점 {Math.Max(0,hit-1)}~{Math.Min(r.Toe.Count-1,hit+4)}: " +
                    string.Join(" ", Enumerable.Range(Math.Max(0,hit-1), Math.Min(6, r.Toe.Count - Math.Max(0,hit-1)))
                        .Select(k => $"({r.Toe[k].X:F2},{r.Toe[k].Y:F2})")));
                // ★벽면이 그 코너에서 끊겼는가 — 끊겼다면 판넬이 코너를 못 넘는다.
                double sideW = WallBand.SideFor(5.0);
                var fCrestOnly = WallBand.SplitAtCorners(r.Crest, 12.0, WallBand.MinFaceLenFor(sideW), null);
                var fBoth = WallBand.SplitAtCorners(r.Crest, 12.0, WallBand.MinFaceLenFor(sideW), r.Toe);
                var fNoMerge = WallBand.SplitAtCorners(r.Crest, 12.0, 0, r.Toe);
                Console.WriteLine($"        벽면 수: 크레스트만 {fCrestOnly.Count} · 토우까지 {fBoth.Count} · 토우까지+합치기끔 {fNoMerge.Count}" +
                                  $" (정점 {r.Crest.Count}=={r.Toe.Count})");
                // ★[JACK 0806 추측 '지정 폭으로 오다가 끝단에서 합쳐지는 로직 때문에 마지막 패널이 방향을 바꾼 것 아닌가']
                //   합치기(짧은 벽면 병합)를 끄고 같은 것을 재서 그 추측을 확인한다.
                {
                    WallBand.DisableShortFaceMergeForTest = true;
                    List<WallBand.Tile> tNM;
                    try
                    {
                        WallBand.ResetTotals();
                        tNM = new List<WallBand.Tile>();
                        foreach (var rr in runs36) tNM.AddRange(WallBand.Slice(rr, gnd36, joint: 0.05));
                    }
                    finally { WallBand.DisableShortFaceMergeForTest = false; }
                    double off2 = 0;
                    foreach (var t in tNM)
                    {
                        double n0 = double.MaxValue, n1 = double.MinValue, b0 = double.MaxValue;
                        foreach (var (u, v) in t.Local) { n0 = Math.Min(n0, u); n1 = Math.Max(n1, u); b0 = Math.Min(b0, v); }
                        for (int s = 0; s <= 5; s++)
                        {
                            double u = n0 + (n1 - n0) * s / 5.0;
                            double px = t.Origin.X + u * t.UAxis.x + b0 * t.VAxis.x;
                            double py = t.Origin.Y + u * t.UAxis.y + b0 * t.VAxis.y;
                            double best = double.MaxValue;
                            foreach (var rr in runs36)
                                for (int k = 0; k + 1 < rr.Toe.Count; k++)
                                {
                                    double dx = rr.Toe[k+1].X - rr.Toe[k].X, dy = rr.Toe[k+1].Y - rr.Toe[k].Y, L2 = dx*dx+dy*dy;
                                    if (L2 < 1e-12) continue;
                                    double tt = Math.Clamp(((px - rr.Toe[k].X)*dx + (py - rr.Toe[k].Y)*dy)/L2, 0, 1);
                                    double qx = rr.Toe[k].X + dx*tt, qy = rr.Toe[k].Y + dy*tt;
                                    double d = Math.Sqrt((px-qx)*(px-qx)+(py-qy)*(py-qy));
                                    if (d < best) best = d;
                                }
                            if (best > off2) off2 = best;
                        }
                    }
                    Console.WriteLine($"        ★JACK 가설 검증 — 짧은벽면 합치기 끔: 아랫변 이탈 {worstOff:F3}m → {off2:F3}m");
                    // ★그 코너가 벽면 경계로 잡혔는가 — 토우 정점 인덱스와 벽면 경계 위치를 나란히 본다.
                    int ci = -1;
                    for (int k = 0; k < r.Toe.Count; k++)
                        if (Math.Abs(r.Toe[k].X - 8.51) < 0.06 && Math.Abs(r.Toe[k].Y - 21.25) < 0.06) { ci = k; break; }
                    var cumC2 = new double[r.Crest.Count];
                    for (int k = 1; k < r.Crest.Count; k++)
                        cumC2[k] = cumC2[k-1] + Math.Sqrt(Math.Pow(r.Crest[k].X-r.Crest[k-1].X,2) + Math.Pow(r.Crest[k].Y-r.Crest[k-1].Y,2));
                    double totC2 = cumC2[cumC2.Length-1];
                    double fc = ci >= 0 ? cumC2[ci] / totC2 : -1;
                    Console.WriteLine($"        코너 토우정점 인덱스 {ci} → 크레스트 비율 {fc:F4}");
                    if (ci > 0 && ci + 1 < r.Toe.Count)
                    {
                        var A = r.Toe[ci-1]; var B = r.Toe[ci]; var C = r.Toe[ci+1];
                        double d1x = B.X-A.X, d1y = B.Y-A.Y, d2x = C.X-B.X, d2y = C.Y-B.Y;
                        double l1 = Math.Sqrt(d1x*d1x+d1y*d1y), l2 = Math.Sqrt(d2x*d2x+d2y*d2y);
                        Console.WriteLine($"        그 자리 토우 꺾임 cos={(d1x*d2x+d1y*d2y)/(l1*l2):F3} · 크레스트 이웃 " +
                            $"({r.Crest[ci-1].X:F2},{r.Crest[ci-1].Y:F2}) ({r.Crest[ci].X:F2},{r.Crest[ci].Y:F2}) ({r.Crest[ci+1].X:F2},{r.Crest[ci+1].Y:F2})");
                    }
                    Console.WriteLine($"        벽면 경계(비율) 앞뒤: " + string.Join(" ",
                        fBoth.Where(f => Math.Abs(f.F0 - fc) < 0.08 || Math.Abs(f.F1 - fc) < 0.08)
                             .Select(f => $"[{f.F0:F4}..{f.F1:F4}]")));
                }
            }
        }
        // 모서리 겹침(0.10m)만큼은 일부러 내미는 살이므로 그만큼은 정상이다.
        Check("S36 ★판넬이 아랫선을 따라간다(선형 어긋남 없음)", worstOff < 0.15,
            $"최대 이탈 {worstOff:F3}m — 겹침 0.10m + 여유 0.05m가 한도");
    }


    // ★★자가검증 — 자를 세 번 고쳤으니 이제 **진짜 구멍은 잡는지**를 증명해야 한다.
    //   판넬 한 열을 일부러 들어내고, 그 자리가 '틈'으로 잡히는지 본다.
    //   이게 통과해야 '틈 없음'이 '구멍 없음'이라는 뜻이 된다 — 아니면 아무것도 못 잡는 자일 뿐이다.
    {
        // 곧은 벽 **한가운데** 열 하나를 통째로 제거(같은 원점 = 같은 열).
        //   ※목록 첫 판넬을 고르면 그건 벽이 시작되는 자리라 지워도 구멍이 아니라 **벽이 짧아질 뿐**이다
        //     — 검사가 아무것도 확인하지 못한다. 가운데에서 고른다.
        var fulls = t36.FindAll(x => x.IsFull);
        var victim = fulls[fulls.Count / 2];
        var punched = t36.FindAll(x =>
            !(Math.Abs(x.Origin.X - victim.Origin.X) < 1e-9 && Math.Abs(x.Origin.Y - victim.Origin.Y) < 1e-9));
        string grPunch = WallBand.GapReport(punched);
        Console.WriteLine($"      S36 자가검증(열 하나 제거 {t36.Count}→{punched.Count}장): {grPunch}");
        Check("S36 ★★자가검증: 판넬 한 열을 들어내면 '틈'으로 잡힌다",
            !grPunch.Contains("틈 없음") && grPunch.Contains("★양옆 온전"),
            "못 잡으면 '틈 없음'은 아무 의미가 없다");
    }
}

// ★ S38 [JACK 0806 '절토일 때랑 성토일 때랑 잘 구분해서 방향 잘 맞춰서 코드 짜줘'] 성토도 같은 조건으로.
//   코너의 볼록/오목 판정은 **노출면이 어느 쪽인가**로 정해지는데, 성토는 토우·크레스트가 절토와 정반대로
//   놓인다. 판정이 뒤집히면 겹침을 **정확히 반대 자리**에 넣게 되고(볼록에서 빼고 오목에서 내밀고),
//   그러면 절토에서 고친 증상이 성토에서 그대로 재현된다. 믿지 말고 성토로 직접 잰다.
{
    var bndF = new List<Point3>();
    foreach (var (X, Y) in new (double X, double Y)[] {
        (0,0), (30,0), (30,20), (22,20), (23.8,10), (17.6,10), (18,20), (10,20), (11.8,10), (5.6,10), (6,20), (0,20) })
        bndF.Add(new Point3(X, Y, 100));
    var prF = new GradingParams {
        CutBenchHeight = 5, FillBenchHeight = 5, CutBenchWidth = 1, FillBenchWidth = 1,
        CutSlope = 0.05, FillSlope = 0.05, CellSize = 1.0, MaxBenches = 4, MaxRise = 20,
        VertexSpacing = 1.0, MinSlope = 0.05, MinFaceRun = 0.005, MiterConvex = true, MiterLimit = 2.0,
    };
    var gndF2 = new TiltGround(0, 0, 92.0, 0.10, 0.06);      // 성토: 원지반이 계획면보다 낮다
    var vsF2 = GradingGeometry.Build(bndF, gndF2, prF, false);
    var rsF2 = vsF2.Rings.Select(r => (IReadOnlyList<Point3>)r).ToList();
    var runsF2 = WallRunBuilder.Build(bndF, rsF2, null, up: false, globalSlope: 0.05, minSlope: 0.05);
    Check("S38 재현 조건: 성토 빗모양 부지에서 옹벽선이 나온다", runsF2.Count > 0, $"{runsF2.Count}줄");

    WallBand.ResetTotals();
    var tF2 = new List<WallBand.Tile>();
    foreach (var r in runsF2) tF2.AddRange(WallBand.Slice(r, gndF2, joint: 0.05));
    string grF2 = WallBand.GapReport(tF2);
    Console.WriteLine($"      S38 성토 틈: {grF2}");
    Check("S38 재현 조건: 성토도 판넬이 충분히 나온다", tF2.Count > 30, $"{tF2.Count}장");

    int holeF = 0;
    { const string k = "진짜 구멍 "; int a = grF2.IndexOf(k);
      if (a >= 0) { int b = grF2.IndexOf('곳', a); if (b > a) int.TryParse(grF2.Substring(a+k.Length, b-a-k.Length).Trim(), out holeF); } }
    Check("S38 ★성토 오목부에도 진짜 구멍이 없다", holeF == 0, $"진짜 구멍 {holeF}곳");

    // ★코너 판정이 성토에서 뒤집히지 않았는가 — 뒤집혔다면 겹침을 반대 자리에 넣어 밑동이 어긋난다.
    double offF = 0; double ofx = 0, ofy = 0;
    foreach (var t in tF2)
    {
        double v0 = double.MaxValue;
        foreach (var (u, v) in t.Local) v0 = Math.Min(v0, v);
        if (v0 > 0.05) continue;                                  // 맨 아랫행만(윗행은 기울기 때문에 원래 떨어져 있다)
        double u0 = double.MaxValue, u1 = double.MinValue;
        foreach (var (u, v) in t.Local) if (v < v0 + 0.02) { u0 = Math.Min(u0, u); u1 = Math.Max(u1, u); }
        if (u1 <= u0) continue;
        for (int s = 0; s <= 5; s++)
        {
            double u = u0 + (u1 - u0) * s / 5.0;
            double px = t.Origin.X + u * t.UAxis.x + v0 * t.VAxis.x;
            double py = t.Origin.Y + u * t.UAxis.y + v0 * t.VAxis.y;
            double best = double.MaxValue;
            foreach (var r in runsF2)
                for (int q = 0; q + 1 < r.Toe.Count; q++)
                {
                    double dx = r.Toe[q+1].X - r.Toe[q].X, dy = r.Toe[q+1].Y - r.Toe[q].Y, L2 = dx*dx+dy*dy;
                    if (L2 < 1e-12) continue;
                    double tt = Math.Clamp(((px - r.Toe[q].X)*dx + (py - r.Toe[q].Y)*dy)/L2, 0, 1);
                    double qx = r.Toe[q].X + dx*tt, qy = r.Toe[q].Y + dy*tt;
                    double d = Math.Sqrt((px-qx)*(px-qx)+(py-qy)*(py-qy));
                    if (d < best) best = d;
                }
            if (best > offF) { offF = best; ofx = px; ofy = py; }
        }
    }
    Console.WriteLine($"      S38 성토 판넬 아랫변↔토우선 최대 이탈: {offF:F3}m @ {ofx:F1},{ofy:F1}");
    // ★겹침 탓인지 가른다 — 겹침을 끄고 같은 값을 잰다. 겹침이 원인이면 확 줄고, 아니면 그대로다.
    {
        WallBand.ResetTotals();
        var tNoLap = new List<WallBand.Tile>();
        foreach (var r in runsF2) tNoLap.AddRange(WallBand.Slice(r, gndF2, joint: 0.05, cornerLap: 0.0));
        double o2 = 0;
        foreach (var t in tNoLap)
        {
            double v0 = double.MaxValue;
            foreach (var (u, v) in t.Local) v0 = Math.Min(v0, v);
            if (v0 > 0.05) continue;
            double u0 = double.MaxValue, u1 = double.MinValue;
            foreach (var (u, v) in t.Local) if (v < v0 + 0.02) { u0 = Math.Min(u0, u); u1 = Math.Max(u1, u); }
            if (u1 <= u0) continue;
            for (int s = 0; s <= 5; s++)
            {
                double u = u0 + (u1 - u0) * s / 5.0;
                double px = t.Origin.X + u * t.UAxis.x + v0 * t.VAxis.x;
                double py = t.Origin.Y + u * t.UAxis.y + v0 * t.VAxis.y;
                double best = double.MaxValue;
                foreach (var r in runsF2)
                    for (int q = 0; q + 1 < r.Toe.Count; q++)
                    {
                        double dx = r.Toe[q+1].X - r.Toe[q].X, dy = r.Toe[q+1].Y - r.Toe[q].Y, L2 = dx*dx+dy*dy;
                        if (L2 < 1e-12) continue;
                        double tt = Math.Clamp(((px - r.Toe[q].X)*dx + (py - r.Toe[q].Y)*dy)/L2, 0, 1);
                        double qx = r.Toe[q].X + dx*tt, qy = r.Toe[q].Y + dy*tt;
                        double d = Math.Sqrt((px-qx)*(px-qx)+(py-qy)*(py-qy));
                        if (d < best) best = d;
                    }
                if (best > o2) o2 = best;
            }
        }
        Console.WriteLine($"      S38 성토 겹침 끔: 아랫변 이탈 {offF:F3}m → {o2:F3}m");
    }
    Check("S38 ★성토 판넬도 아랫선을 따라간다(코너 판정이 안 뒤집혔다)", offF < 0.15,
        $"최대 이탈 {offF:F3}m — 겹침 0.10m + 여유 0.05m가 한도");
}

// ★ S37 [JACK 0806 '토우선이 지표면하고 안 맞어 · 일정 간격 정점 말고 지표면을 정확히 따라가는 방식으로']
//   옹벽선은 **지표면을 만든 그 링 위**에 있어야 한다. 그런데 표본을 크레스트 정점에서만 뽑으면
//   토우의 **코너 정점**이 표본 사이에 떨어져 빠지고, 그 자리가 현(弦)으로 잘려 지표면 모서리를 벗어난다.
//   재는 법: 링의 각 정점이 옹벽선 위에 있는가 — 코너 정점까지 거리가 곧 '지표면에서 벗어난 양'이다.
{
    // ★재는 방향이 중요하다. '링 정점 → 선'으로 재면 **옆 단의 링 정점**(소단 폭 1m)까지 섞여 창에 걸려
    //   측정이 포화한다. 반대로 '**선 위의 점 → 가장 가까운 링**'으로 재면, 선이 코너를 현으로 자른 만큼
    //   그 점이 어느 링에서도 멀어지므로 **잘린 깊이가 그대로 나온다**.
    static double MaxRingDev(List<WallRun> runs, List<IReadOnlyList<Point3>> rings)
    {
        double worst = 0;
        foreach (var r in runs)
        {
            if (r.Toe == null || r.Toe.Count < 2) continue;
            for (int k = 0; k + 1 < r.Toe.Count; k++)
            {
                var mid = new Point3((r.Toe[k].X + r.Toe[k+1].X) / 2, (r.Toe[k].Y + r.Toe[k+1].Y) / 2, 0);
                double best = double.MaxValue;
                foreach (var ring in rings)
                    for (int q = 0; q < ring.Count; q++)
                    {
                        var a = ring[q]; var b = ring[(q + 1) % ring.Count];
                        double dx = b.X - a.X, dy = b.Y - a.Y, L2 = dx*dx + dy*dy;
                        if (L2 < 1e-12) continue;
                        double t = Math.Clamp(((mid.X - a.X)*dx + (mid.Y - a.Y)*dy) / L2, 0, 1);
                        double px = a.X + dx*t, py = a.Y + dy*t;
                        double d = Math.Sqrt((mid.X-px)*(mid.X-px) + (mid.Y-py)*(mid.Y-py));
                        if (d < best) best = d;
                    }
                if (best > worst) worst = best;
            }
        }
        return worst;
    }
    var sq37 = new List<Point3>();
    foreach (var (X, Y) in new (double X, double Y)[] {
        (0,0), (30,0), (30,20), (22,20), (23.8,10), (17.6,10), (18,20), (10,20), (11.8,10), (5.6,10), (6,20), (0,20) })
        sq37.Add(new Point3(X, Y, 100));
    var pr37 = new GradingParams {
        CutBenchHeight = 5, FillBenchHeight = 5, CutBenchWidth = 1, FillBenchWidth = 1,
        CutSlope = 0.05, FillSlope = 0.05, CellSize = 1.0, MaxBenches = 4, MaxRise = 20,
        VertexSpacing = 1.0, MinSlope = 0.05, MinFaceRun = 0.005, MiterConvex = true, MiterLimit = 2.0,
    };
    var vs37 = GradingGeometry.Build(sq37, new FlatGround(130), pr37, true);
    var rs37 = vs37.Rings.Select(r => (IReadOnlyList<Point3>)r).ToList();

    WallRunBuilder.DisableToeVertexInsertForTest = true;
    List<WallRun> bad37;
    try { bad37 = WallRunBuilder.Build(sq37, rs37, null, up: true, globalSlope: 0.05, minSlope: 0.05); }
    finally { WallRunBuilder.DisableToeVertexInsertForTest = false; }
    double devBad = MaxRingDev(bad37, rs37);

    var good37 = WallRunBuilder.Build(sq37, rs37, null, up: true, globalSlope: 0.05, minSlope: 0.05);
    double devGood = MaxRingDev(good37, rs37);
    Console.WriteLine($"      S37 링 정점↔옹벽선 최대 거리: 끼워넣기 끔 {devBad:F3}m → 켬 {devGood:F3}m");

    Check("S37 재현 조건: 끼워넣기를 끄면 옹벽선이 링(지표면)에서 벗어난다", devBad > 0.10,
        $"{devBad:F3}m — 작으면 이 부지가 코너를 안 만든 것");
    Check("S37 ★옹벽선이 지표면(링)을 정확히 따라간다", devGood < 0.02,
        $"최대 {devGood:F3}m (끄면 {devBad:F3}m)");

    // ★[JACK 0806 '성토부에도 해당하는 내용 있으면 함께 수정해'] 같은 코드를 타는지 **성토로 직접 확인**한다.
    //   WallRunBuilder.Build·WallBand.Slice는 절/성토 공용이라 수정이 자동으로 따라와야 맞지만,
    //   성토는 토우·크레스트가 절토와 정반대로 놓이므로 '맞을 것'이라 믿지 않고 잰다.
    {
        var vsF = GradingGeometry.Build(sq37, new FlatGround(70), pr37, false);
        var rsF = vsF.Rings.Select(r => (IReadOnlyList<Point3>)r).ToList();
        WallRunBuilder.DisableToeVertexInsertForTest = true;
        List<WallRun> badF;
        try { badF = WallRunBuilder.Build(sq37, rsF, null, up: false, globalSlope: 0.05, minSlope: 0.05); }
        finally { WallRunBuilder.DisableToeVertexInsertForTest = false; }
        var goodF = WallRunBuilder.Build(sq37, rsF, null, up: false, globalSlope: 0.05, minSlope: 0.05);
        double dB = MaxRingDev(badF, rsF), dG = MaxRingDev(goodF, rsF);
        Console.WriteLine($"      S37 성토: 링 정점↔옹벽선 최대 거리 끔 {dB:F3}m → 켬 {dG:F3}m ({goodF.Count}줄)");
        Check("S37 재현 조건(성토): 끼워넣기를 끄면 성토 옹벽선도 링에서 벗어난다", goodF.Count > 0 && dB > 0.10,
            $"{dB:F3}m · {goodF.Count}줄");
        Check("S37 ★성토 옹벽선도 지표면(링)을 정확히 따라간다", dG < 0.02,
            $"최대 {dG:F3}m (끄면 {dB:F3}m)");
    }
}

Console.WriteLine(fails == 0 ? "\n== 전부 통과 ==" : $"\n== 실패 {fails}건 ==");

/// <summary>로컬 (u,v) 다각형 안에 점이 있는가 — 도넛 네 모서리 검사(하니스용 사본).</summary>
static bool PointInPolyLocal(double u, double v, IReadOnlyList<(double u, double v)> poly)
{
    bool inside = false;
    int n = poly.Count;
    for (int i = 0, j = n - 1; i < n; j = i++)
    {
        var a = poly[i]; var b = poly[j];
        if ((a.v > v) != (b.v > v) &&
            u < (b.u - a.u) * (v - a.v) / (b.v - a.v + (b.v == a.v ? 1e-300 : 0)) + a.u)
            inside = !inside;
    }
    return inside;
}

static IReadOnlyList<IReadOnlyList<Point3>> WallBlocks_TryBuild(List<Point3> bnd, GradingParams pr, bool up, out string err)
{
    err = "";
    try
    {
        var vs = GradingGeometry.Build(bnd, new FlatGround(200), pr, up);
        return vs.Rings.Select(r => (IReadOnlyList<Point3>)r).ToList();
    }
    catch (Exception ex) { err = ex.Message; return null; }
}
return fails == 0 ? 0 : 1;

sealed class FlatGround(double z) : IGroundSurface
{
    public bool TryGetElevation(double x, double y, out double zz) { zz = z; return true; }
}

/// <summary>기준점에서 x·y로 기울어진 원지반 — 절토 daylight가 자리마다 다르게 걸리도록.</summary>
sealed class TiltGround(double x0, double y0, double z0, double kx, double ky) : IGroundSurface
{
    public bool TryGetElevation(double x, double y, out double zz)
    { zz = z0 + kx * (x - x0) + ky * (y - y0); return true; }
}

sealed class SlopeGround(double z0, double kx) : IGroundSurface
{
    public bool TryGetElevation(double x, double y, out double zz) { zz = z0 + kx * x; return true; }
}

/// <summary>물결치는 원지반 — 데이라잇 윗변이 오르내려 <b>오목한</b> 판넬 실루엣이 실제로 생긴다(S30).
/// 평면 원지반은 윗변이 직선이라 사다리꼴(볼록)만 나오므로 오목 경로를 한 번도 안 밟는다.</summary>
sealed class WavyGround(double z0, double amp, double wave) : IGroundSurface
{
    public bool TryGetElevation(double x, double y, out double zz)
    { zz = z0 + amp * System.Math.Sin(x / wave); return true; }
}
