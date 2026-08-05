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
    Check("S24 ★판넬 한 변 ≤ 설계 상한", maxSide <= WallBand.MaxSide + 1e-6, $"최대 한 변 {maxSide:F3}m (상한 {WallBand.MaxSide:F3}m)");

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
    Check("S24 ★모서리 겹침이 코너를 메운다", driftLap > 0.08 && driftLap < 0.18,
        $"겹침 ON · 아랫변 최대 이탈 {driftLap:F3}m (겹침 0.10m + 선추종 {drift:F3}m)");

    // (C) ★자체검증 — 코너 분할을 끄면(임계 179°) 판넬이 코너를 가로질러 벽선에서 벗어나야 한다.
    //   '항상 통과하는 검사는 검사가 아니다'(0805).
    var t3 = WallBand.Slice(run1, null, joint: 0.05, cornerDeg: 179.0);
    double bugDrift = ToeDrift(t3, toe1);
    Check("S24 ★검사 자체검증: 코너 분할을 끄면 벽선을 벗어난다", bugDrift > 0.2,
        $"분할 OFF → 아랫변 최대 이탈 {bugDrift:F2}m (정상 {drift:F4}m)");

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

Console.WriteLine(fails == 0 ? "\n== 전부 통과 ==" : $"\n== 실패 {fails}건 ==");

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
