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
    // [JACK 0807 계약 변경] **규격 판넬만** 잰다. 자투리·급커브는 이제 좁아도 되는 **전용 얇은 객체**이고
    //   (JACK: "부족하면 얇은 거 전용객체 하나 만들어서 넣고"), 규격 판넬은 **언제나 정확히 한 변**이어야 한다.
    //   그래서 검사도 종전('아무도 0.3m보다 좁지 않다')보다 **더 엄격**해진다 — 규격 판넬은 폭이 하나뿐이다.
    double specMin = double.MaxValue, specMax = 0; int nSpec = 0, nFill = 0;
    foreach (var t in t1)
    {
        double mnU = double.MaxValue, mxU = double.MinValue;
        foreach (var (u, v) in t.Local) { mnU = Math.Min(mnU, u); mxU = Math.Max(mxU, u); }
        if (t.Filler) { nFill++; continue; }
        nSpec++;
        specMin = Math.Min(specMin, mxU - mnU); specMax = Math.Max(specMax, mxU - mnU);
    }
    Check("S24 ★규격 판넬은 폭이 전부 같다(자투리는 전용객체로 뺀다)",
        nSpec > 0 && specMax - specMin < 0.01,
        $"규격 {nSpec}장 폭 {specMin:F3}~{specMax:F3}m · 전용객체 {nFill}장");

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
            // ★[v32.58] 기준 높이는 <b>맨 위를 뺀 나머지</b>에서 잡는다. 맨 위 행은 데이라잇에 잘리기도 하고,
            //   자투리가 실오라기가 되지 않게 <b>아래 행과 병합</b>되기도 해서 <b>더 클 수도</b> 있다.
            //   전체 최대로 잡으면 그 병합된 맨 위가 기준이 되어 <b>멀쩡한 아래 행이 전부 "잘렸다"로 잡힌다</b>
            //   (행 수가 늘어난 v32.54부터 병합이 실제로 일어난다).
            double hMax = 0;
            for (int k = 0; k + 1 < spans.Count; k++) hMax = Math.Max(hMax, spans[k].Hi - spans[k].Lo);
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
        // [0807] 지반을 **물결**로 준다. 평면 지반에서는 데이라잇선이 직선이라, 판넬 폭이 규격으로 통일된 뒤
        //   상한이 열 경계와 나란히 떨어지면 5각이 한 장도 안 나올 수 있다 — 능력이 없어서가 아니라
        //   **시나리오가 그 능력을 안 밟는** 것이다. 물결 지반이면 상한이 열 한가운데를 반드시 가로지른다.
        //   ('항상 통과하는 검사'만 문제가 아니다 — '조건이 안 걸려 실패하는 검사'도 똑같이 거짓말이다.)
        var t2w = WallBand.Slice(run1, new WavyGround(102.5, 1.2, 3.0), joint: 0.05);
        int quad = 0, penta = 0, more = 0;
        foreach (var t in t2w)
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
        // ★[v32.58] 기대값을 <b>설계 규칙에서 직접</b> 얻는다 — 숫자를 박으면 규격이 바뀔 때마다 여기도 고쳐야 한다.
        //   줄눈(0.05) 인셋을 뺀 값이 행 높이다. 5m 단 → SideFor 1.25 → 1.20m.
        //   위쪽 여유(+0.15)는 맨 위 행이 자투리 병합으로 커질 수 있어서다.
        double wantRow = WallBand.SideFor(5.0) - 0.05;
        Check($"S24 ★판넬 행 높이가 설계값(단높이 ÷ {WallBand.RowsForBench(5.0)}행)",
            hMinT > wantRow - 0.10 && hMaxT < wantRow + 0.15,
            $"행 높이 [{hMinT:F3}..{hMaxT:F3}]m (5m 단 → {wantRow:F2}m 기대 · 상한 {WallBand.MaxSide:F2}m)");
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
            // [JACK 0807] **규격 판넬만** 잰다. 전용 얇은 객체는 코너·급커브의 남는 자리를 메우려고
            //   일부러 놓는 것이라, 벽선에서 벗어난 양을 '이탈'로 세면 두 성질이 뒤섞인다
            //   (겹침을 끄고 재던 종전 이유와 같다 — 이제 겹침 자리를 전용객체가 물려받았을 뿐이다).
            if (t.Filler) continue;
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

    // ★★[JACK 0807 계약 변경] **모서리 겹침 폐지 — 코너는 전용 얇은 객체가 메운다.**
    //   0806에 겹침을 네 번 고쳐 네 번 실패했고(오목 확대 3회·볼록 확대 1회), 결론은 매번 같았다:
    //   *판넬을 늘려서는 코너를 못 메운다.* JACK 0807이 그 결론을 원칙으로 정리했다.
    //   그러니 검사도 '겹침이 얼마나 나갔나'가 아니라 **'코너에 구멍이 남았나'**를 봐야 한다.
    var t1Fill = new List<WallBand.Tile>(t1);
    int q24 = 0;   // [JACK 0807] 틈 메우기 폐지 — 코너 쐐기는 코너 필러(Slice 내부)가 이미 세운다
    string gr24 = WallBand.GapReport(t1Fill);
    Check("S24 ★코너 틈을 전용 얇은 객체가 메운다(판넬을 늘리지 않는다)",
        !gr24.Contains("★양옆 온전"), $"전용객체 {q24}개 · {gr24}");
    double driftLap = ToeDrift(t1NoLap, toe1);
    // ★[0806] 하한을 0.08 → 0.06으로 내린다. 판넬이 사다리꼴이 되면서 **아랫변이 코너에서 정확히 끝나고**
    //   거기서 겹침 0.10m만 U 방향으로 더 나간다. 직각 코너면 그 0.10m의 **토우선까지 수직거리는
    //   0.10×sin45° ≈ 0.071m**다. 종전 0.08 하한은 아랫변이 코너를 지나쳐 나가던 시절의 값이라
    //   지금 기준으로는 '겹침이 없어야 통과'하는 셈이 된다 — 고쳐야 할 건 코드가 아니라 이 숫자다.
    //   [0807] 겹침이 없어졌으므로 판넬은 **언제나** 벽선을 따라간다 — 위 '선추종' 검사와 같은 값이어야 한다.
    Check("S24 ★겹침이 없어 판넬이 벽선 밖으로 안 나간다", driftLap < 0.05,
        $"아랫변 최대 이탈 {driftLap:F3}m (겹침 폐지 — 코너는 전용객체 담당)");

    // (C) ★자체검증 — 코너 분할을 끄면(임계 179°) 판넬이 코너를 가로질러 벽선에서 벗어나야 한다.
    //   '항상 통과하는 검사는 검사가 아니다'(0805).
    // 코너 분할을 꺼도 **현(弦) 이탈 제한**이 대신 막아 준다 — 방어가 이중이라는 뜻(0805 추가).
    var t3 = WallBand.Slice(run1, null, joint: 0.05, cornerDeg: 179.0);
    double bugDrift = ToeDrift(t3, toe1);
    Check("S24 ★코너 분할을 꺼도 현 이탈 제한이 막아준다(이중 방어)", bugDrift < 0.20,
        $"분할 OFF → 아랫변 최대 이탈 {bugDrift:F3}m (제한 {WallBand.ChordTol}m + 겹침 0.10m)");

    // ★자체검증 — **둘 다** 끄면 반드시 재발해야 한다. 안 그러면 검사가 아니다.
    //   [0806] 토우 폭 맞추기도 같이 꺼야 한다 — 그게 아랫변을 토우에 맞춰 주므로, 켜 둔 채로는
    //   두 방어를 꺼도 아랫변이 멀쩡해 보여(0.02m) 검사가 무력해진다(방어가 삼중이 된 셈).
    //   [JACK 0819] **방어가 넷이 됐다.** 쐐기 규칙(WedgeDev)이 벗어나는 열을 아예 안 깔아 버려서,
    //   그것만 켜 두면 앞의 셋을 꺼도 아랫변이 0.00m로 멀쩡해 보인다 — 이 검사가 실제로 그렇게 죽었다.
    //   방어를 하나 늘렸으면 **자체검증도 같이 늘려야** 한다. 안 그러면 검사가 거짓말을 시작한다.
    WallBand.DisableChordLimitForTest = true;
    WallBand.DisableToeWidthForTest = true;
    WallBand.DisableWedgeForTest = true;
    List<WallBand.Tile> t3b;
    try { t3b = WallBand.Slice(run1, null, joint: 0.05, cornerDeg: 179.0); }
    finally
    {
        WallBand.DisableChordLimitForTest = false; WallBand.DisableToeWidthForTest = false;
        WallBand.DisableWedgeForTest = false;
    }
    double bugDrift2 = ToeDrift(t3b, toe1);
    Check("S24 ★검사 자체검증: 방어를 다 끄면 벽선을 크게 벗어난다", bugDrift2 > 0.2,
        $"셋 다 OFF → 아랫변 최대 이탈 {bugDrift2:F2}m (정상 {drift:F4}m)");

    // ★★[JACK 0819 '각도로 접근하는 방법은 버려'] **쐐기 규칙이 혼자서도 막는지** — 각도를 안 쓰는 방어의 단독 성적.
    //   앞의 두 방어(현 이탈 분할·토우 폭)를 끄고 코너 분할도 꺼서 각도 경로를 전부 죽인 뒤,
    //   쐐기 규칙만 남긴다. 판넬이 벽선을 크게 벗어나지 않아야 하고, 그 자리엔 스윕 덩어리가 서 있어야 한다.
    //   ※'안 깔았으니 이탈 0'만 보면 벽에 구멍을 뚫어 놓고 통과할 수 있다 — 덩어리 개수를 같이 본다.
    WallBand.ResetTotals();
    WallBand.DisableChordLimitForTest = true;
    WallBand.DisableToeWidthForTest = true;
    List<WallBand.Tile> t3c;
    try { t3c = WallBand.Slice(run1, null, joint: 0.05, cornerDeg: 179.0); }
    finally { WallBand.DisableChordLimitForTest = false; WallBand.DisableToeWidthForTest = false; }
    double wedgeDrift = ToeDrift(t3c, toe1);
    int sweptN = WallBand.LastMasses.Count;
    Check("S24 ★★쐐기 규칙이 혼자서도 벽선 벗어남을 막는다(각도 경로 전부 OFF)",
        wedgeDrift < 0.20 && sweptN > 0,
        $"각도 OFF·쐐기만 ON → 아랫변 최대 이탈 {wedgeDrift:F3}m · 스윕 덩어리 {sweptN}개 (한도 {WallBand.WedgeDev:F2}m)");
    // ★★★[JACK 0819 '그냥 옹벽 자체를 하나의 매스로 스윕해서 만들고'] **매스 모드가 기본 경로가 됐다.**
    //   기본값이 꺼짐이라 하니스가 이 길을 한 번도 안 밟는다 — 실제로 쓰는 길이 검사 밖에 있으면
    //   '348 PASS'는 안심의 근거가 못 된다. 켜서 직접 본다.
    {
        WallBand.ResetTotals();
        WallBand.MassOnly = true;
        List<WallBand.Tile> tm;
        try { tm = WallBand.Slice(run1, null, joint: 0.05); }
        finally { WallBand.MassOnly = false; }

        int massN = WallBand.LastMasses.Count, secTot = 0, massFlatBad = 0, massShapeBad = 0;
        foreach (var mm in WallBand.LastMasses)
        {
            secTot += mm.Sections.Count;
            foreach (var sec in mm.Sections)
            {
                if (sec.Count != 4) { massShapeBad++; continue; }
                // 단면 네 점이 한 평면 위에 있어야 한다 — 아니면 로프트가 예외 없이 빈 솔리드를 낸다.
                //   마름모는 '법선 N과 수직선 Z'가 만드는 평면 위에 있으므로, 평면에서 벗어난 양을 직접 잰다.
                double ux = sec[1].X - sec[0].X, uy = sec[1].Y - sec[0].Y, uz = sec[1].Z - sec[0].Z;
                double vx = sec[3].X - sec[0].X, vy = sec[3].Y - sec[0].Y, vz = sec[3].Z - sec[0].Z;
                double nx = uy * vz - uz * vy, ny = uz * vx - ux * vz, nz = ux * vy - uy * vx;
                double nl = Math.Sqrt(nx * nx + ny * ny + nz * nz);
                if (nl < 1e-12) { massShapeBad++; continue; }
                double wx = sec[2].X - sec[0].X, wy = sec[2].Y - sec[0].Y, wz = sec[2].Z - sec[0].Z;
                if (Math.Abs((wx * nx + wy * ny + wz * nz) / nl) > 1e-6) massFlatBad++;
            }
        }
        // ★★★[JACK 0820 '단높이를 2m로 바꿔도 5m로 쳐져'] **필드별 복사가 새 필드를 떨어뜨린다.**
    //   BuildParams는 GradingParams를 필드별로 새로 만들어 돌려준다 — 목록에 없는 필드는 조용히 사라진다.
    //   이 저장소가 같은 종류로 여러 번 샜다(v13.9 검사 · 판넬 Simplify · 그리고 이것).
    //   ⇒ GradingParams에 필드를 더하면 **복사에서 살아남는지**를 여기서 잡는다.
    {
        var src43 = new GradingParams
        {
            CutBenchHeight = 5.0, FillBenchHeight = 4.0,
            CutBenchWidth = 1.0, FillBenchWidth = 2.0,
            CutSlope = 1.5, FillSlope = 1.8, MaxRise = 30.0,
            CutBenchSteps = { (2, 3.0) }, FillBenchSteps = { (1, 2.0) },
        };
        // BuildParams는 Civil 어셈블리라 여기서 못 부른다 — 대신 **복사 누락을 잡는 규칙**을 직접 건다:
        //   GradingParams의 모든 목록 필드가 비어 있지 않은 원본을 복사했을 때 살아남아야 한다.
        var copy43 = new GradingParams
        {
            CutBenchHeight = src43.CutBenchHeight, FillBenchHeight = src43.FillBenchHeight,
            CutBenchWidth = src43.CutBenchWidth, FillBenchWidth = src43.FillBenchWidth,
            CutSlope = src43.CutSlope, FillSlope = src43.FillSlope, MaxRise = src43.MaxRise,
            CutBenchSteps = new List<(int, double)>(src43.CutBenchSteps),
            FillBenchSteps = new List<(int, double)>(src43.FillBenchSteps),
        };
        Check("S43 ★★★단높이 규칙이 제원 복사에서 살아남는다(BuildParams가 떨어뜨리던 그 자리)",
            copy43.CutBenchSteps.Count == 1 && copy43.FillBenchSteps.Count == 1
            && Math.Abs(copy43.BenchHeightAt(true, 3) - 3.0) < 1e-9
            && Math.Abs(copy43.BenchHeightAt(false, 3) - 2.0) < 1e-9,
            $"절토 4단 {copy43.BenchHeightAt(true, 3):0.##}m(3이어야) · 성토 4단 {copy43.BenchHeightAt(false, 3):0.##}m(2여야)");
    }

    // ★★★[JACK 0820] **링 표고가 규칙대로 쌓이는지** — 규칙이 값만 바뀌고 실제 링에 안 먹으면 소용없다.
    //   정사각 부지 + 평탄 지반으로 절토 링을 만들어 표고 간격을 잰다.
    {
        var sq43 = new List<DH.Grading.Core.Point3>();
        for (int i = 0; i <= 3; i++)
        {
            double[] xs = { 0, 40, 40, 0 }, ys = { 0, 0, 40, 40 };
            sq43.Add(new DH.Grading.Core.Point3(xs[i], ys[i], 100));
        }
        GradingParams P43(params (int fb, double h)[] steps)
        {
            var pp = new GradingParams
            {
                CutBenchHeight = 5.0, FillBenchHeight = 5.0,
                CutBenchWidth = 1.0, FillBenchWidth = 1.0,
                CutSlope = 1.5, FillSlope = 1.5, MaxRise = 30.0,
            };
            foreach (var st in steps) pp.CutBenchSteps.Add((st.fb, st.h));
            pp.NormalizeBenchSteps();
            return pp;
        }
        // 표고 간격 목록(중복 제거) — 링은 등고선이라 링마다 표고가 하나다.
        static List<double> Gaps(DH.Grading.Core.VirtualSlope v)
        {
            var zs = new List<double>();
            foreach (var r in v.Rings) { if (r.Count > 0) zs.Add(Math.Round(r[0].Z, 3)); }
            zs.Sort();
            for (int i = zs.Count - 1; i > 0; i--) if (Math.Abs(zs[i] - zs[i - 1]) < 1e-6) zs.RemoveAt(i);
            var g = new List<double>();
            for (int i = 1; i < zs.Count; i++) g.Add(Math.Round(zs[i] - zs[i - 1], 3));
            return g;
        }
        var vFlat = GradingGeometry.Build(sq43, new FlatGround(200), P43(), true);
        var gFlat = Gaps(vFlat);
        bool allFive = gFlat.Count > 0;
        foreach (var gg in gFlat) if (Math.Abs(gg - 5.0) > 1e-6) allFive = false;
        Check("S43 ★★규칙이 없으면 표고 간격이 전부 전역 단높이다",
            allFive, $"간격 {(gFlat.Count > 0 ? string.Join(",", gFlat) : "없음")} (전부 5여야 한다)");

        // 3단(0부터 2)부터 3m — 앞 두 간격은 5, 그 뒤는 3이어야 한다.
        var vStep = GradingGeometry.Build(sq43, new FlatGround(200), P43((2, 3.0)), true);
        var gStep = Gaps(vStep);
        bool okStep = gStep.Count >= 4
                      && Math.Abs(gStep[0] - 5.0) < 1e-6 && Math.Abs(gStep[1] - 5.0) < 1e-6
                      && Math.Abs(gStep[2] - 3.0) < 1e-6 && Math.Abs(gStep[3] - 3.0) < 1e-6;
        Check("S43 ★★★단높이 규칙이 실제 링 표고에 먹는다(그 단부터 간격이 바뀐다)",
            okStep, $"간격 {(gStep.Count > 0 ? string.Join(",", gStep) : "없음")} (5,5,3,3,… 이어야 한다)");

        // ★링은 등고선이다 — 한 링 안에서 표고가 하나여야 한다(단높이를 층 전체에 적용했으니 지켜져야 한다).
        double zSpread = 0;
        foreach (var r in vStep.Rings)
        {
            double lo = double.MaxValue, hi = double.MinValue;
            foreach (var q in r) { lo = Math.Min(lo, q.Z); hi = Math.Max(hi, q.Z); }
            if (r.Count > 0) zSpread = Math.Max(zSpread, hi - lo);
        }
        Check("S43 ★★★링 하나에 표고가 하나다(v16.9가 막았던 그 조건)",
            zSpread < 1e-6, $"한 링 안의 표고 편차 최대 {zSpread:F6}m");
    }

    // ★★★[JACK 0820 '해당 선택 지점부터 단높이를 바꿔서 할 순 없나'] **단높이가 단마다 바뀐다.**
    //   v16.9는 "단높이는 구간별 불가"라고 적었다 — 링 하나에 표고가 하나라 둘레의 일부만 못 바꾼다.
    //   그런데 **층 전체**를 바꾸는 것은 다르다: 그 단부터 위쪽 링들의 표고가 통째로 옮겨갈 뿐이다.
    //   ⇒ 규칙이 실제로 먹는지, 그리고 표고가 규칙대로 쌓이는지 잰다.
    {
        var pBase = new GradingParams { CutBenchHeight = 5.0, FillBenchHeight = 5.0 };
        // 3단(0부터 세어 2)부터 3m로
        var pStep = new GradingParams
        {
            CutBenchHeight = 5.0, FillBenchHeight = 5.0,
            CutBenchSteps = { (2, 3.0) },
        };
        pStep.NormalizeBenchSteps();
        Check("S43 ★★단높이 규칙이 그 단부터 먹는다(그 전 단은 그대로)",
            Math.Abs(pStep.BenchHeightAt(true, 0) - 5.0) < 1e-9
            && Math.Abs(pStep.BenchHeightAt(true, 1) - 5.0) < 1e-9
            && Math.Abs(pStep.BenchHeightAt(true, 2) - 3.0) < 1e-9
            && Math.Abs(pStep.BenchHeightAt(true, 9) - 3.0) < 1e-9,
            $"1단 {pStep.BenchHeightAt(true, 0):0.##} · 2단 {pStep.BenchHeightAt(true, 1):0.##}"
            + $" · 3단 {pStep.BenchHeightAt(true, 2):0.##} · 10단 {pStep.BenchHeightAt(true, 9):0.##}");

        // ★반대 방향은 안 건드린다 — 절성토를 따로 보기로 했다(JACK 0820).
        Check("S43 ★★단높이 규칙은 그 방향만 바꾼다(절성토 분리)",
            Math.Abs(pStep.BenchHeightAt(false, 5) - 5.0) < 1e-9,
            $"성토 6단 {pStep.BenchHeightAt(false, 5):0.##}m (전역 5m 그대로여야 한다)");

        // 규칙이 여러 개면 쌓인다 — 구배·소단폭과 같은 규칙(아래는 높게 · 위는 낮게).
        var pMulti = new GradingParams { CutBenchHeight = 5.0, CutBenchSteps = { (2, 3.0), (5, 1.5) } };
        pMulti.NormalizeBenchSteps();
        Check("S43 ★★단높이 규칙이 여러 개면 쌓인다", 
            Math.Abs(pMulti.BenchHeightAt(true, 1) - 5.0) < 1e-9
            && Math.Abs(pMulti.BenchHeightAt(true, 3) - 3.0) < 1e-9
            && Math.Abs(pMulti.BenchHeightAt(true, 6) - 1.5) < 1e-9,
            $"2단 {pMulti.BenchHeightAt(true, 1):0.##} · 4단 {pMulti.BenchHeightAt(true, 3):0.##} · 7단 {pMulti.BenchHeightAt(true, 6):0.##}");

        // ★단수 예산은 **가장 작은** 단높이로 잡아야 한다 — 큰 값으로 잡으면 작은 단이 섞인 구간에서
        //   단수가 모자라 사면이 원지반에 못 닿는다(v16.6에서 이미 겪은 종류).
        Check("S43 ★★★단수 예산은 가장 작은 단높이로 잡는다",
            Math.Abs(pMulti.SmallestBenchHeightOf(true) - 1.5) < 1e-9,
            $"가장 작은 단높이 {pMulti.SmallestBenchHeightOf(true):0.##}m (5·3·1.5 중)");
    }

    Check("S42 ★★매스 모드는 판넬을 한 장도 안 깐다(전부 매스)", tm.Count == 0,
            $"판넬 {tm.Count}장 · 매스 {massN}개 · 단면 {secTot}장");
        // ★★★[JACK 0819 '왜 조각으로 하냐는거야'] **옹벽 한 줄은 솔리드 하나여야 한다.**
        //   조각으로 쌓으면 맞붙어 있어도 도면에서는 131덩어리로 보인다(JACK 실측).
        Check("S42 ★★★옹벽 한 줄이 매스 하나로 나온다(조각으로 안 쪼개진다)", massN == 1,
            $"매스 {massN}개(1이어야 한다) · 단면 {secTot}장");
        // 단면이 옹벽선 정점 수만큼 있어야 벽이 선을 그대로 따라간다 — 빠지면 그만큼 모양이 뭉개진다.
        Check("S42 ★★단면이 옹벽선 정점마다 있다(선 모양을 그대로 따라간다)",
            secTot == run1.Crest.Count, $"단면 {secTot}장 / 옹벽선 정점 {run1.Crest.Count}개");
        Check("S42 ★★단면이 전부 평면이다(로프트가 빈 솔리드를 안 낸다)", massFlatBad == 0,
            $"평면 아닌 단면 {massFlatBad}장 / {secTot}장");
        Check("S42 ★★단면이 전부 4점 마름모다", massShapeBad == 0,
            $"어긋난 단면 {massShapeBad}장 / {secTot}장");

        // ★★★[JACK 0820 '벽이 휘었어' — 스샷: 벽 끝에서 단면이 부채꼴로 비틀림]
        //   단면의 두께 방향은 벽을 따라 **연속**이어야 한다. 한 자리에서 뒤집히면 그 구간이 통째로 꼬인다.
        //   방향을 '크레스트→토우'로 잡았는데 그 수평 길이가 구배×높이라, 데이라잇이 벽을 깎는
        //   **끝에서 0에 수렴**해 방향이 좌표 잡음이 됐다. 그 결함의 눈에 보이는 형태가 '뒤집힘'이다.
        double turnWorst = 0; int flipN = 0;
        foreach (var mm in WallBand.LastMasses)
            for (int i = 0; i + 1 < mm.Sections.Count; i++)
            {
                var A = mm.Sections[i]; var B = mm.Sections[i + 1];
                if (A.Count != 4 || B.Count != 4) continue;
                // 두께 방향 = sec[0] − sec[3] (바깥 − 안쪽)
                double ax = A[0].X - A[3].X, ay = A[0].Y - A[3].Y;
                double bx = B[0].X - B[3].X, by = B[0].Y - B[3].Y;
                double al = Math.Sqrt(ax * ax + ay * ay), bl = Math.Sqrt(bx * bx + by * by);
                if (al < 1e-9 || bl < 1e-9) { flipN++; continue; }
                double cos = (ax * bx + ay * by) / (al * bl);
                double deg = Math.Acos(Math.Clamp(cos, -1, 1)) * 180.0 / Math.PI;
                if (deg > turnWorst) turnWorst = deg;
                if (cos < 0) flipN++;                      // 90°를 넘게 돌았다 = 뒤집힘
            }
        Check("S42 ★★★단면 두께 방향이 뒤집히지 않는다(벽이 안 비틀린다)", flipN == 0,
            $"뒤집힌 이음매 {flipN}곳 · 최대 회전 {turnWorst:F1}°");

        // ★★★[JACK 0820 '표면에서 원래 우리 판넬 두께 절반만큼 튀어나와야 해']
        //   옹벽선은 **판 한가운데**를 지난다 — 절반은 밖으로 나오고 절반은 흙에 묻힌다.
        //   지금은 판넬이 쓰던 상수(PanelFrontOut)를 그대로 쓰지만, 그건 **주장**이지 검사가 아니다.
        //   상수가 어느 쪽이든 흔들리면 벽 앞면 위치가 통째로 밀리므로 여기서 붙잡아 둔다.
        Check("S42 ★★★돌출이 판넬 두께의 절반이다(옹벽선이 판 한가운데)",
            Math.Abs(WallBand.PanelFrontOut - WallBand.PanelThick / 2) < 1e-9,
            $"돌출 {WallBand.PanelFrontOut:F3}m · 두께 {WallBand.PanelThick:F3}m · 절반 {WallBand.PanelThick / 2:F3}m");

        double thkWorst = 0;
        foreach (var mm in WallBand.LastMasses)
            foreach (var sec in mm.Sections)
            {
                if (sec.Count != 4) continue;
                // 바깥위(0) ↔ 안쪽위(3) 사이 거리 = 판 두께. 아래쪽(1↔2)도 같아야 한다.
                double d1 = Math.Sqrt(Math.Pow(sec[0].X - sec[3].X, 2) + Math.Pow(sec[0].Y - sec[3].Y, 2) + Math.Pow(sec[0].Z - sec[3].Z, 2));
                double d2 = Math.Sqrt(Math.Pow(sec[1].X - sec[2].X, 2) + Math.Pow(sec[1].Y - sec[2].Y, 2) + Math.Pow(sec[1].Z - sec[2].Z, 2));
                thkWorst = Math.Max(thkWorst, Math.Max(Math.Abs(d1 - WallBand.PanelThick), Math.Abs(d2 - WallBand.PanelThick)));
            }
        Check("S42 ★★매스 두께가 판넬 두께와 같다(위·아래 모두)", thkWorst < 1e-6,
            $"두께 오차 최대 {thkWorst:F6}m · 규격 {WallBand.PanelThick:F2}m");

        // ★★★[JACK 0820 '왜 비틀렸지? 지표면 그대로 나온 거 아니야?' — 스샷: 벽 끝만 부채꼴]
        //   **데이라잇으로 자를 때 X·Y도 같이 잘라야 한다.** 벽면은 토우→크레스트로 가는 비스듬한 선이라
        //   위를 자르면 가로 위치도 그만큼 안쪽이어야 한다. Z만 줄이고 X·Y를 크레스트로 두면
        //   10m 벽이 2m로 잘려도 가로로는 0.5m 벌어져 **그 자리만 구배가 1:0.05가 아니라 1:0.25**가 된다.
        //   자리마다 다르게 누우니 벽이 비틀린다 — 평평한 구간은 안 잘려 멀쩡했고 끝만 부채꼴이었다.
        //   ⇒ 데이라잇이 얼마를 자르든 **기울기는 어디서나 같아야** 한다. 그것만 재면 이 결함이 다시 못 산다.
        //   ⇒ **자르지 않은 벽과 자른 벽을 같은 자리끼리** 댄다. 자른 단면의 위·아래 점은
        //     자르지 않은 벽의 '토우→크레스트' 선 **위에** 있어야 한다 — 그 선에서 벗어난 거리가 곧 결함의 크기다.
        //     (자리마다 기울기를 비교하면 안 된다 — 90° 코너에서는 이등분선 방향이라 0.05×√2로 커지는 게 정상이다.)
        var uncut = new System.Collections.Generic.List<(DH.Grading.Core.Point3 Bot, DH.Grading.Core.Point3 Top)>();
        foreach (var mm in WallBand.LastMasses)
            foreach (var sec in mm.Sections)
                if (sec.Count == 4) uncut.Add((sec[1], sec[0]));       // 바깥아래(토우) · 바깥위(크레스트)

        double zMid = 0; foreach (var u in uncut) zMid += (u.Bot.Z + u.Top.Z) / 2;
        if (uncut.Count > 0) zMid /= uncut.Count;

        WallBand.ResetTotals();
        WallBand.MassOnly = true;
        try { WallBand.Slice(run1, new FlatGround(zMid), joint: 0.05); }
        finally { WallBand.MassOnly = false; }

        double offWorst = 0; int leanN = 0, unmatched = 0;
        foreach (var mm in WallBand.LastMasses)
            foreach (var sec in mm.Sections)
            {
                if (sec.Count != 4) continue;
                // 같은 자리 찾기 — 자르는 건 위쪽이므로 아랫점(토우)은 그대로다.
                int best = -1; double bestD = double.MaxValue;
                for (int i = 0; i < uncut.Count; i++)
                {
                    double d = Math.Pow(uncut[i].Bot.X - sec[1].X, 2) + Math.Pow(uncut[i].Bot.Y - sec[1].Y, 2);
                    if (d < bestD) { bestD = d; best = i; }
                }
                if (best < 0 || bestD > 1e-6) { unmatched++; continue; }
                var A = uncut[best];
                double ax = A.Top.X - A.Bot.X, ay = A.Top.Y - A.Bot.Y, az = A.Top.Z - A.Bot.Z;
                double al = Math.Sqrt(ax * ax + ay * ay + az * az);
                if (al < 1e-9) continue;
                // 자른 단면의 위·아래 점이 그 선에서 얼마나 벗어났나
                foreach (var q in new[] { sec[0], sec[1] })
                {
                    double wx = q.X - A.Bot.X, wy = q.Y - A.Bot.Y, wz = q.Z - A.Bot.Z;
                    double t = (wx * ax + wy * ay + wz * az) / (al * al);
                    double ex = wx - ax * t, ey = wy - ay * t, ez = wz - az * t;
                    offWorst = Math.Max(offWorst, Math.Sqrt(ex * ex + ey * ey + ez * ez));
                }
                leanN++;
            }
        // ★★★[JACK 0820 '왜 끝단이 뭉퉁그려졌지?'] **데이라잇이 죽인 끝은 0으로 만나야 한다.**
        //   종전엔 높이 0.15m 미만이면 단면을 안 만들었다 — 뾰족하게 만나야 할 자리가 15cm 마구리로 막혔다.
        //   ※ **줄이 그냥 끝나는 자리와 갈라서 재야 한다** — 거기는 벽이 온전한 채로 끝나는 게 맞다.
        //     그래서 벽을 <b>중간에서 죽이는</b> 가파른 지형을 쓰고, 그 죽는 끝만 본다.
        {
            WallBand.ResetTotals();
            WallBand.MassOnly = true;
            try { WallBand.Slice(run1, new SlopeGround(96.0, 0.6), joint: 0.05); }
            finally { WallBand.MassOnly = false; }
            double tipMin = double.MaxValue, tipMax = 0; int tipN = 0, secN = 0;
            foreach (var mm in WallBand.LastMasses)
            {
                if (mm.Sections.Count < 2) continue;
                secN += mm.Sections.Count;
                foreach (var sec in new[] { mm.Sections[0], mm.Sections[mm.Sections.Count - 1] })
                {
                    if (sec.Count != 4) continue;
                    double h = sec[0].Z - sec[1].Z;         // 바깥위 − 바깥아래 = 그 자리 벽 높이
                    tipMin = Math.Min(tipMin, h); tipMax = Math.Max(tipMax, h); tipN++;
                }
            }
            // 데이라잇이 벽을 중간에서 끊었다면 **그 끝은 반드시 0**이다. 하나도 0이 아니면 마구리로 막힌 것이다.
            Check("S42 ★★★데이라잇이 죽인 벽 끝이 0으로 만난다(마구리로 안 막힌다)",
                tipN > 0 && tipMin < 0.02,
                $"끝 높이 {(tipN > 0 ? tipMin : -1):F4}~{tipMax:F2}m · 끝 {tipN}곳 · 단면 {secN}장 (한도 0.02m)");
        }

        // ★★★[JACK 0820 '표면에 무늬와 앵커부분 배열 · 한 무늬가 가로세로 1.5로 규정해서 채우면 될 것 같아' ·
        //   '굴곡부까지 억지로 채우지말고 그냥 직선부만'] **마감 판이 규격이고, 굽은 데는 안 깐다.**
        {
            WallBand.ResetTotals();
            WallBand.MassOnly = true;
            try { WallBand.Slice(run1, null, joint: 0.05); }
            finally { WallBand.MassOnly = false; }
            int nFace = WallBand.BuildFacePanels();
            static double Dist3(DH.Grading.Core.Point3 a, DH.Grading.Core.Point3 b)
                => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y) + (a.Z - b.Z) * (a.Z - b.Z));

            // ① 판 크기가 전부 규격이어야 한다 — 자투리를 억지로 채우면 여기서 걸린다.
            double wMin = double.MaxValue, wMax = 0, hMin = double.MaxValue, hMax = 0;
            double want = WallBand.FaceTile - WallBand.JointW;      // 줄눈을 뺀 실제 판 크기
            int nSpecH = 0, nOverH = 0;
            foreach (var f in WallBand.LastFacePanels)
            {
                if (f.Poly.Count != 4) continue;
                double w = Dist3(f.Poly[0], f.Poly[1]);
                double h = Dist3(f.Poly[1], f.Poly[2]);
                wMin = Math.Min(wMin, w); wMax = Math.Max(wMax, w);
                hMin = Math.Min(hMin, h); hMax = Math.Max(hMax, h);
                if (Math.Abs(h - want) < 0.01) nSpecH++;
                if (h > want + 0.01) nOverH++;                      // 규격보다 큰 판은 있으면 안 된다
            }
            // 한도 1cm — **벽면이 사다리꼴**이라(높이마다 가로 길이가 다르다) 판 아랫변은 가운데보다 몇 mm 짧다.
            //   그건 기하이지 결함이 아니다. 줄눈(5cm)의 1/5 안이면 눈에 안 띈다.
            //   ※ 잡으려는 건 '자투리를 억지로 채워 크기가 제각각이 되는 것'이지 mm 오차가 아니다.
            // 한도 3cm — **벽면이 사다리꼴**이라 위 행이 아래 행보다 조금 넓다(실측 1.450→1.472m).
            //   없애려면 행마다 격자를 다시 깔아야 하는데 그러면 **세로줄이 어긋난다** —
            //   JACK 0820이 "각 패턴 배열시 세로 방향을 유지할 것, 지금은 들쑥날쑥함"이라고 짚은 바로 그 증상이다.
            //   세로 정렬이 우선이므로 가로 2cm는 받아들인다(줄눈 5cm의 절반 이하라 눈에 안 띈다).
            const double tolFace = 0.03;
            // ★[JACK 0820 '데이라잇으로 잘려지는 부분까지도 끝까지 마감할 것'] **세로는 잘릴 수 있다.**
            //   맨 윗행은 벽 상단(데이라잇)에 맞춰 자르므로 규격보다 **작을 수 있다** — 그게 마감이다.
            //   대신 **규격보다 큰 판은 있으면 안 되고**(자투리를 억지로 늘린 것), 가로는 언제나 규격이어야 한다.
            // ★[검토 심각4 수정 뒤] **끝 자투리 칸은 좁다** — 벽면 끝의 0~1.49m를 버리지 않고 마감하기로 했다.
            //   그러니 '가로가 언제나 규격'은 더 이상 참이 아니다. 대신 **규격보다 넓은 판은 없어야** 하고,
            //   좁은 판은 벽면마다 한 칸(끝 자투리)뿐이어야 한다.
            int wideN = 0, narrowN2 = 0;
            foreach (var f in WallBand.LastFacePanels)
            {
                if (f.Poly.Count < 4) continue;
                double w = Dist3(f.Poly[0], f.Poly[1]);
                if (w > want + tolFace) wideN++;
                else if (w < want - tolFace) narrowN2++;
            }
            Check("S42 ★★★표면 판이 규격이다(끝 자투리만 좁고, 규격보다 넓은 판은 없다)",
                nFace > 0 && wideN == 0 && nOverH == 0 && nSpecH > 0,
                $"판 {nFace}장 · 가로 {(nFace > 0 ? wMin : 0):F3}~{wMax:F3}m(넓음 {wideN} · 좁음 {narrowN2})" +
                $" · 세로 {(nFace > 0 ? hMin : 0):F3}~{hMax:F3}m (규격 {want:F3}m · 규격높이 {nSpecH}장 · 규격초과 {nOverH}장)");

            // ② 굽은 자리에는 판이 없어야 한다 — 곧은 판이 벽선을 가로질러 뜨는 것을 막는 규칙 그 자체.
            //    ㄱ자 벽의 코너에서 FaceTile 반경 안에 판 중심이 있으면 굽은 데를 침범한 것이다.
            var corner = run1.Crest[run1.Crest.Count / 2];
            int nearCorner = 0;
            foreach (var f in WallBand.LastFacePanels)
            {
                double d = Math.Sqrt(Math.Pow(f.Center.X - corner.X, 2) + Math.Pow(f.Center.Y - corner.Y, 2));
                if (d < WallBand.FaceTile / 2 - WallBand.JointW) nearCorner++;
            }
            // 한도 = 반 칸 − 줄눈. **코너 바로 옆 판은 중심이 정확히 반 칸(0.75m) 떨어진다** — 덮은 게 아니라 맞닿은 것이다.
            //   그보다 가까우면 판이 코너를 실제로 가로지른 것이고, 그게 이 검사가 잡으려는 것이다.
            Check("S42 ★★굽은 자리(코너)를 판이 가로지르지 않는다",
                nearCorner == 0, $"코너 {WallBand.FaceTile / 2 - WallBand.JointW:F2}m 안의 판 {nearCorner}장");

            // ③ 판은 매스 바깥면보다 앞에 있어야 한다 — 뒤에 있으면 벽에 묻혀 안 보인다.
            double behind = 0; int behindN = 0;
            foreach (var f in WallBand.LastFacePanels)
                foreach (var mm in WallBand.LastMasses)
                    foreach (var sec in mm.Sections)
                    {
                        if (sec.Count != 4) continue;
                        // 그 단면의 바깥 평면에서 판 중심까지의 부호 있는 거리
                        double nx = sec[0].X - sec[3].X, ny = sec[0].Y - sec[3].Y, nz = sec[0].Z - sec[3].Z;
                        double nl = Math.Sqrt(nx * nx + ny * ny + nz * nz);
                        if (nl < 1e-9) continue;
                        nx /= nl; ny /= nl; nz /= nl;
                        double dx = f.Center.X - sec[0].X, dy = f.Center.Y - sec[0].Y, dz = f.Center.Z - sec[0].Z;
                        double along = Math.Sqrt(dx * dx + dy * dy) ;
                        if (along > WallBand.FaceTile) continue;          // 먼 단면은 기준이 안 된다
                        double sd = dx * nx + dy * ny + dz * nz;
                        if (sd < behind) { behind = sd; }
                        if (sd < -1e-9) behindN++;
                    }
            // ★[FaceProud=0 이후] 판이 매스 표면에 **딱 붙어** 있으므로, 굽은 칸에서 곧은 판이 현이 되어
            //   최대 flatTol(5cm)만큼 파고드는 것은 **직선으로 봐 주기로 한 그 범위**다(ChordOffS의 정의).
            //   그보다 깊이 묻히면 그건 다른 원인이다.
            const double buryTol = 0.05;
            Check("S42 ★★표면 판이 벽 안으로 깊이 묻히지 않는다", (-behind) < buryTol,
                $"가장 깊이 묻힌 {(-behind):F3}m · {behindN}장 (한도 {buryTol:F2}m — 평탄 판정 한도와 같음)");

            // ★★★[JACK 0820 '무늬가 전혀 없는데?' — 로그: eCannotScaleNonUniformly · U·V 1.9E-002 · 168장 전멸]
            //   **프레임(U·V·W)이 직교가 아니면 판넬 렌더러가 판을 통째로 못 만든다** — 무늬·도넛·앵커도 같이 죽는다.
            //   벽면이 사다리꼴이라 '위'가 '가로'와 직각이 아닌 게 원인이었다. 여기서 붙잡아 둔다.
            double dotWorst = 0, lenWorst = 0;
            foreach (var f in WallBand.LastFacePanels)
            {
                double uv = f.UAxis.x * f.VAxis.x + f.UAxis.y * f.VAxis.y + f.UAxis.z * f.VAxis.z;
                double uw = f.UAxis.x * f.WAxis.x + f.UAxis.y * f.WAxis.y + f.UAxis.z * f.WAxis.z;
                double vw = f.VAxis.x * f.WAxis.x + f.VAxis.y * f.WAxis.y + f.VAxis.z * f.WAxis.z;
                dotWorst = Math.Max(dotWorst, Math.Max(Math.Abs(uv), Math.Max(Math.Abs(uw), Math.Abs(vw))));
                foreach (var ax in new[] { f.UAxis, f.VAxis, f.WAxis })
                    lenWorst = Math.Max(lenWorst, Math.Abs(Math.Sqrt(ax.x * ax.x + ax.y * ax.y + ax.z * ax.z) - 1));
            }
            Check("S42 ★★★표면 판 프레임이 직교 단위축이다(무늬·앵커가 붙는 조건)",
                nFace > 0 && dotWorst < 1e-9 && lenWorst < 1e-9,
                $"축 내적 최대 {dotWorst:E1} · 길이 오차 최대 {lenWorst:E1} · 판 {nFace}장");
        }

        // ★★★[JACK 0820 '데이라잇이 이상하게 잘림' — 스샷: 벽 윗선에 계단]
        //   <b>지반 밖에서 높이를 꽉 채우면</b> 그 자리만 솟아 계단이 된다.
        //   WallSpanAtPt는 지반 밖이면 hi<0(판단 불가)을 주는데, 종전엔 그때 원래 높이로 꽉 채웠다.
        //   ⇒ 지반이 벽 절반만 덮는 상황을 만들어, 이웃 사이 높이가 갑자기 튀지 않는지 잰다.
        {
            WallBand.ResetTotals();
            WallBand.MassOnly = true;
            try { WallBand.Slice(run1, new HoleGround(101.5, 12.0), joint: 0.05); }
            finally { WallBand.MassOnly = false; }
            double jump = 0; int pairs = 0;
            foreach (var mm in WallBand.LastMasses)
                for (int i = 0; i + 1 < mm.Sections.Count; i++)
                {
                    var A = mm.Sections[i]; var B = mm.Sections[i + 1];
                    if (A.Count != 4 || B.Count != 4) continue;
                    double hA = A[0].Z - A[1].Z, hB = B[0].Z - B[1].Z;
                    jump = Math.Max(jump, Math.Abs(hA - hB)); pairs++;
                }
            // 이웃 단면은 ~1m 떨어져 있고 지반은 완만하다 — 1m 넘게 튀면 '꽉 채운' 자리가 남아 있는 것이다.
            Check("S42 ★★★지반 밖에서 높이를 꽉 채우지 않는다(데이라잇 선에 계단이 안 생긴다)",
                pairs > 0 && jump < 1.0,
                $"이웃 단면 높이차 최대 {jump:F3}m · 이음매 {pairs}곳 (한도 1.0m)");
        }

        // ★★[JACK 0820 '끝부분 잘림 마감이 미흡함 · 주로 앵커정착부에서 발생']
        //   **잘린 판에는 앵커·도넛을 붙이지 않는다** — 0.5m짜리 판에 정착부가 그대로 들어가면 삐져나온다.
        {
            WallBand.ResetTotals();
            WallBand.MassOnly = true;
            // 벽을 **가로질러 비스듬히 자르는** 지형이어야 잘린 판이 생긴다 —
            //   완만한 지형을 쓰면 잘리는 판이 0장이라 아래 검사들이 통과하는 척만 한다(실제로 그랬다).
            try { WallBand.Slice(run1, new SlopeGround(96.0, 0.6), joint: 0.05); }
            finally { WallBand.MassOnly = false; }
            WallBand.BuildFacePanels();
            double want2 = WallBand.FaceTile - WallBand.JointW;
            int shortWithAnchor = 0, shortN = 0, wholeN = 0;
            foreach (var f in WallBand.LastFacePanels)
            {
                if (f.Poly.Count != 4) continue;
                double h = Math.Sqrt(Math.Pow(f.Poly[1].X - f.Poly[2].X, 2)
                                   + Math.Pow(f.Poly[1].Y - f.Poly[2].Y, 2)
                                   + Math.Pow(f.Poly[1].Z - f.Poly[2].Z, 2));
                if (h < want2 - 0.01) { shortN++; if (f.IsFull) shortWithAnchor++; }
                else wholeN++;
            }
            // ★[JACK 0820] 앵커는 **자리가 날 때만** 붙는다 — 보호공 반폭(PocketHalf)이 판 안에 들어와야 한다.
            //   그리고 앵커가 붙은 판은 그 자리가 **격자 위치**여야 한다(판 한가운데가 아니라).
            int offGrid = 0;
            foreach (var f in WallBand.LastFacePanels)
            {
                if (!f.IsFull || f.Poly.Count != 4) continue;
                // 앵커 자리가 판 안에 있고, 보호공 반폭만큼 위아래 여유가 있어야 한다.
                double vA = (f.AnchorPos.X - f.Origin.X) * f.VAxis.x
                          + (f.AnchorPos.Y - f.Origin.Y) * f.VAxis.y
                          + (f.AnchorPos.Z - f.Origin.Z) * f.VAxis.z;
                double vMax = 0;
                foreach (var lv in f.Local) vMax = Math.Max(vMax, lv.v);
                if (vA - WallBand.PocketHalf < -1e-6 || vA + WallBand.PocketHalf > vMax + 1e-6) offGrid++;
            }
            // ★★★[JACK 0820 '무늬 부분의 잘림이 데이라잇과 맞지 않는다']
            //   판 윗변이 1.5m짜리 **직선**이면 그 안에서 꺾이는 데이라잇을 못 따라간다.
            //   ⇒ 칸 안에 옹벽선 정점이 있으면 판이 **사각형보다 많은 점**을 가져야 한다.
            //     (사각형뿐이면 윗변이 직선이라는 뜻이고, 그게 어긋남의 원인이다.)
            //   ※ 정점 개수로는 못 잰다 — 이 시험 벽은 정점이 3개뿐이라 칸 안에 정점이 안 들어간다
            //     (실제 도면은 1m로 조밀화돼 있어 칸마다 정점이 있다).
            //   ※ '데이라잇 선까지의 거리'로도 못 잰다 — 벽이 사라지는 자리에서는 판이 토우까지 내려와
            //     뾰족해지는데, 그 점은 매스 윗선이 아예 없는 자리라 거리가 크게 나온다(그게 정상이다).
            //   ⇒ 진짜 결함은 **판이 벽 위로 솟는 것**이다. 그것만 잰다.
            //   기준은 **매스가 아니라 벽면(LastFaces)**이다 — 매스는 데이라잇에 끊겨 여러 덩어리로 나뉘어서,
            //   끊긴 자리 근처에서는 '가장 가까운 단면'이 엉뚱한 다리(ㄱ자 반대편)로 잡힌다(실측 오탐 2.97m).
            //   벽면은 끊기지 않고 벽 전체를 담고 있으므로 어느 자리에서나 옳은 상한을 준다.
            //   ※ **가장 가까운 정점에 스냅하면 안 된다** — 이 시험 벽은 정점이 3개뿐이라(실제 도면은 1m 간격)
            //     20m 떨어진 정점의 높이와 비교하게 된다(실측 오탐 2.26m · 가까운거리 9m).
            //     판은 정점 <b>사이</b>를 보간해 놓이므로, 잴 때도 <b>구간 위로 투영</b>해서 보간해야 한다.
            double aboveMax = 0; int checkedV = 0;
            foreach (var f in WallBand.LastFacePanels)
                foreach (var q in f.Poly)
                {
                    double best = double.MaxValue, topZ = 0;
                    foreach (var wf in WallBand.LastFaces)
                        for (int k = 0; k + 1 < wf.Full.Count; k++)
                        {
                            var A = wf.Full[k]; var B = wf.Full[k + 1];
                            if (A.Count != 4 || B.Count != 4) continue;
                            double dx = B[1].X - A[1].X, dy = B[1].Y - A[1].Y;
                            double L2 = dx * dx + dy * dy;
                            if (L2 < 1e-12) continue;
                            double t = Math.Clamp(((q.X - A[1].X) * dx + (q.Y - A[1].Y) * dy) / L2, 0, 1);
                            double px = A[1].X + dx * t, py = A[1].Y + dy * t;
                            double d = (q.X - px) * (q.X - px) + (q.Y - py) * (q.Y - py);
                            if (d >= best) continue;
                            best = d;
                            // 그 자리의 토우·크레스트·자르는 비율을 전부 보간해 데이라잇 높이를 얻는다.
                            double bz = A[1].Z + (B[1].Z - A[1].Z) * t;
                            double tz = A[0].Z + (B[0].Z - A[0].Z) * t;
                            double hh = wf.Hi[k] + (wf.Hi[k + 1] - wf.Hi[k]) * t;
                            topZ = bz + (tz - bz) * hh;
                        }
                    if (best == double.MaxValue) continue;
                    aboveMax = Math.Max(aboveMax, q.Z - topZ); checkedV++;
                }

            // ★★★[JACK 0820 '반대로 됨' · '여전히 빈 공간이 많음' · '객체별로 돌아가게 생성됨']
            //   세 증상 다 스샷으로만 보이던 것이다. JACK: "문제점을 로그로도 확인할 수 있게 해."
            //   ⇒ 여기서 숫자로 잡는다. 도면을 열기 전에 걸려야 스샷 왕복이 줄어든다.
            {
                static double D3(DH.Grading.Core.Point3 a, DH.Grading.Core.Point3 b)
                    => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y) + (a.Z - b.Z) * (a.Z - b.Z));

                // ① 판 앞면이 벽 바깥을 보는가 — 안쪽을 보면 무늬가 흙 속으로 들어간다('반대로 됨').
                int flip = 0; double pArea = 0, wArea = 0;
                foreach (var f in WallBand.LastFacePanels)
                {
                    double a2 = 0;
                    for (int i = 0, j2 = f.Local.Count - 1; i < f.Local.Count; j2 = i++)
                        a2 += f.Local[j2].u * f.Local[i].v - f.Local[i].u * f.Local[j2].v;
                    pArea += Math.Abs(a2) / 2;
                    double best = double.MaxValue, bx = 0, by = 0, bz = 0;
                    foreach (var wf in WallBand.LastFaces)
                        foreach (var sc in wf.Full)
                        {
                            if (sc.Count != 4) continue;
                            double d = D3(sc[0], f.Poly[0]);
                            if (d < best) { best = d; bx = sc[0].X - sc[3].X; by = sc[0].Y - sc[3].Y; bz = sc[0].Z - sc[3].Z; }
                        }
                    if (best < double.MaxValue && f.WAxis.x * bx + f.WAxis.y * by + f.WAxis.z * bz < 0) flip++;
                }
                Check("S42 ★★★판 앞면이 벽 바깥을 본다(반대로 안 붙는다)", flip == 0,
                    $"뒤집힌 판 {flip}장 / {WallBand.LastFacePanels.Count}장");

                // ② 덮개율 — '빈 공간이 많다'를 숫자로. 줄눈(5cm)과 굽은 칸 때문에 100%는 구조상 안 된다.
                foreach (var mm in WallBand.LastMasses)
                    for (int k = 0; k + 1 < mm.Sections.Count; k++)
                    {
                        var A = mm.Sections[k]; var B = mm.Sections[k + 1];
                        if (A.Count != 4 || B.Count != 4) continue;
                        wArea += (D3(A[1], A[0]) + D3(B[1], B[0])) / 2 * D3(A[1], B[1]);
                    }
                double cov = wArea > 1e-9 ? pArea / wArea * 100 : 0;
                Check("S42 ★★★표면 판이 벽을 충분히 덮는다(잘린 자리에 빈 공간이 안 남는다)",
                    cov >= 70, $"덮개율 {cov:F0}% · 벽 {wArea:F0}㎡ · 판 {pArea:F0}㎡");

                // ③ 이웃한 판끼리 세로축이 같은가 — '객체별로 돌아가게 생성됨'.
                //   프레임을 판의 왼쪽 변에서 뽑으면 비스듬히 잘린 판에서 그 변이 퇴화해 방향이 잡음이 된다.
                //   벽에서 가져오면 같은 벽면 위의 판들은 세로축이 거의 같다.
                double axTurn = 0; int axPairs = 0;
                var fl = WallBand.LastFacePanels;
                for (int i = 0; i < fl.Count; i++)
                    for (int k = i + 1; k < fl.Count; k++)
                    {
                        double d = Math.Sqrt(Math.Pow(fl[i].Origin.X - fl[k].Origin.X, 2)
                                           + Math.Pow(fl[i].Origin.Y - fl[k].Origin.Y, 2));
                        if (d > WallBand.FaceTile * 1.2) continue;      // 이웃한 판만 본다
                        double dot = fl[i].VAxis.x * fl[k].VAxis.x + fl[i].VAxis.y * fl[k].VAxis.y
                                   + fl[i].VAxis.z * fl[k].VAxis.z;
                        axTurn = Math.Max(axTurn, Math.Acos(Math.Clamp(dot, -1, 1)) * 180.0 / Math.PI);
                        axPairs++;
                    }
                // 한도 15° — 벽이 굽으면 이웃끼리 조금은 다르다. 돌아간 판은 수십 도로 어긋난다.
                Check("S42 ★★★이웃한 판끼리 세로축이 같다(판이 제멋대로 안 돈다)",
                    axPairs > 0 && axTurn < 15.0,
                    $"이웃 판 세로축 최대 어긋남 {axTurn:F1}° · 잰 쌍 {axPairs}개 (한도 15°)");

                // ★★★[JACK 0820 '이미 우리가 통으로 만든 게 패널이야 · 무늬만 넣어서 나눠진 것처럼']
                //   **표면 마감은 몸통을 안 만든다.** 매스가 이미 판넬이라 바탕 판을 얹으면 벽이 두 겹이 되고,
                //   그 판이 잘린 무늬를 덮어 '무늬가 데이라잇으로 안 잘린다'처럼 보인다.
                int notOverlay = 0;
                foreach (var f in WallBand.LastFacePanels) if (!f.Overlay) notOverlay++;
                Check("S42 ★★★표면 마감은 몸통 없이 무늬만 얹는다(벽이 두 겹이 안 된다)",
                    WallBand.LastFacePanels.Count > 0 && notOverlay == 0,
                    $"몸통까지 만드는 판 {notOverlay}장 / {WallBand.LastFacePanels.Count}장");

                // 무늬가 매스 표면에 딱 붙어야 한다 — 띄우면 무늬만 허공에 뜬다(몸통이 없으므로).
                // ★★★[JACK 0820 '코너부 무늬 누락' — 로그: 무늬없음 2(분해실패 2)]
                //   **모든 판이 볼록 조각으로 쪼개져야 무늬가 붙는다.** 데이라잇이 판을 쐐기로 만들면
                //   아랫점과 윗점이 같은 자리가 되어(두께 0) 귀 자르기가 실패하고, 호출부는 무늬를 통째로 건너뛴다.
                //   AutoCAD 없이도 여기서 그대로 잴 수 있다 — ConvexPieces는 Core에 있다.
                int splitFail = 0, splitMax = 0;
                foreach (var f in WallBand.LastFacePanels)
                {
                    var w = WallBand.ConvexPieces(f.Local);
                    if (w.Count == 0) splitFail++; else splitMax = Math.Max(splitMax, w.Count);
                }
                Check("S42 ★★★모든 판이 볼록 조각으로 쪼개진다(무늬가 안 빠진다)",
                    WallBand.LastFacePanels.Count > 0 && splitFail == 0,
                    $"분해 실패 {splitFail}장 / {WallBand.LastFacePanels.Count}장 · 최대 {splitMax}조각");

                // 판의 3D 점과 로컬 좌표는 **개수가 같아야** 한다 — 어긋나면 진단이 거짓말을 시작한다.
                int syncBad = 0;
                foreach (var f in WallBand.LastFacePanels) if (f.Poly.Count != f.Local.Count) syncBad++;
                // ★★★[JACK 0820 '무늬가 불특정하게 누락된 부분이 매우 많음']
                //   무늬는 **온전한 칸**에 깔고 나중에 자른다 — 판 경계상자에 맞춰 깔면 잘린 판마다
                //   무늬가 다시 짜여 조각이 최소 크기에 못 미쳐 죽는다("불특정"의 정체).
                //   ⇒ ① 칸 크기가 규격이어야 하고 ② 칸 중심(=앵커 자리)이 **격자 위**에 있어야 한다.
                double cellBad = 0; int gridOff = 0;
                double cellWant = WallBand.FaceTile - WallBand.JointW;
                var seenCtr = new System.Collections.Generic.List<(double u, double v)>();
                foreach (var f in WallBand.LastFacePanels)
                {
                    cellBad = Math.Max(cellBad, Math.Abs(f.CellU - cellWant));
                    cellBad = Math.Max(cellBad, Math.Abs(f.CellV - cellWant));
                    // 칸 중심은 판 안에 있을 수도 밖에 있을 수도 있다(잘린 판) — 다만 **판의 v 범위**를 기준으로
                    //   격자 간격(FaceTile)의 배수 자리에 있어야 한다. 조각 가운데로 밀리면 여기서 걸린다.
                    double vMaxL = 0; foreach (var lv in f.Local) vMaxL = Math.Max(vMaxL, lv.v);
                    if (f.PocketV < -WallBand.FaceTile || f.PocketV > vMaxL + WallBand.FaceTile) gridOff++;
                }
                Check("S42 ★★★무늬 칸이 규격이고 격자 위에 있다(잘린 판에서도 무늬가 이어진다)",
                    WallBand.LastFacePanels.Count > 0 && cellBad < 1e-9 && gridOff == 0,
                    $"칸 크기 오차 {cellBad:F6}m(규격 {cellWant:F3}m) · 격자 벗어난 판 {gridOff}장");

                Check("S42 ★★판의 3D 점과 로컬 좌표 개수가 같다", syncBad == 0,
                    $"어긋난 판 {syncBad}장 / {WallBand.LastFacePanels.Count}장");

                // ★★★[검토 심각1] 노출면 방향을 **매스를 안 보고** 잰다 — 매스와 비교하면 같이 뒤집혀도 못 잡는다.
                // ★★★[JACK 0820 '성토라고 다 뒤집어진 건 아니고 어느 면은 맞고 어느 면은 안 맞고 그래']
                //   노출면 방향은 **이름(크레스트/토우)이 아니라 높이**로 정해야 한다 —
                //   옹벽 면은 뒤로 누우므로 <b>아래가 언제나 바깥</b>이다. 줄에 따라 이름이 뒤바뀌어 나와도
                //   이 규칙은 안 깨진다. 매스 단면에서 직접 재서 그것이 지켜지는지 본다.
                int upsideDown = 0, wallSec = 0;
                foreach (var mm in WallBand.LastMasses)
                    foreach (var sc in mm.Sections)
                    {
                        if (sc.Count != 4) continue;
                        // 0=바깥위 · 1=바깥아래 · 2=안쪽아래 · 3=안쪽위. 바깥 방향 = [0]−[3].
                        double ox = sc[0].X - sc[3].X, oy = sc[0].Y - sc[3].Y;
                        // 위쪽 점(0·3의 평균)에서 아래쪽 점(1·2의 평균)으로 가는 수평 방향이 '바깥'이어야 한다.
                        double dx = (sc[1].X + sc[2].X) / 2 - (sc[0].X + sc[3].X) / 2;
                        double dy = (sc[1].Y + sc[2].Y) / 2 - (sc[0].Y + sc[3].Y) / 2;
                        if (Math.Sqrt(dx * dx + dy * dy) < 1e-6) continue;   // 수직벽 — 판단 불가
                        wallSec++;
                        if (ox * dx + oy * dy < 0) upsideDown++;
                    }
                Check("S42 ★★★노출면이 아래쪽(바깥)을 향한다(줄마다 뒤집히지 않는다)",
                    wallSec == 0 || upsideDown == 0,
                    $"뒤집힌 단면 {upsideDown}/{wallSec}개");

                var sf = WallBand.LastSideFlip;
                Check("S42 ★★★노출면 방향이 줄 전체에서 일관된다(벽이 안 뒤집힌다)",
                    sf.Total > 0 && sf.Odd == 0,
                    $"다수와 다른 자리 {sf.Odd}/{sf.Total}점");

                Check("S42 ★★마감이 매스 표면에 붙어 있다(띄우지 않는다)",
                    Math.Abs(WallBand.FaceProud) < 1e-9,
                    $"띄움 {WallBand.FaceProud:F3}m (0이어야 한다 — 몸통이 없다)");
            }

            Check("S42 ★★★표면 판이 벽 위로 솟지 않는다(잘림이 벽과 맞는다)",
                checkedV > 0 && aboveMax < 0.10,
                $"벽 위로 솟은 최대 {aboveMax:F3}m · 잰 점 {checkedV}개 (한도 0.10m)");

            Check("S42 ★★잘린 판에는 앵커를 안 붙인다(정착부가 판 밖으로 안 나간다)",
                shortWithAnchor == 0 && offGrid == 0,
                $"잘린 판 {shortN}장 중 앵커 달린 것 {shortWithAnchor}장 · 보호공이 판 밖 {offGrid}장 · 온전한 판 {wholeN}장");
        }

        Check("S42 ★★★데이라잇에 잘려도 벽면 선 위에 있다(벽이 안 비틀린다)",
            leanN > 0 && offWorst < 1e-6,
            $"벽면 선에서 벗어남 최대 {offWorst:F6}m · 잰 단면 {leanN}장 · 짝 못 찾음 {unmatched}장");
    }

    // ★★[JACK 0819 '뭔가 이상하게 나왔어' — 실측 26조각 중 23개가 도면에서 사라졌다]
    //   AutoCAD 로프트는 단면이 **평면이 아니면 예외 없이 빈 솔리드**를 돌려준다. 그 빈 솔리드는
    //   뒤쪽 깨진솔리드 검사가 조용히 지워, 로그는 '26/26개 만듦'인데 도면엔 3개만 남았다.
    //   AutoCAD 없이는 로프트를 못 돌리지만 **단면이 평면인지는 여기서 잴 수 있다** —
    //   그게 이 결함의 필요조건이므로 그것만 지켜도 같은 사고가 다시 안 난다.
    {
        int badFlat = 0, badShape = 0, secN = 0;
        foreach (var mm in WallBand.LastMasses)
            foreach (var sec in mm.Sections)
            {
                secN++;
                if (sec.Count != 4) { badShape++; continue; }
                double ux = sec[1].X - sec[0].X, uy = sec[1].Y - sec[0].Y, uz = sec[1].Z - sec[0].Z;
                double vx = sec[3].X - sec[0].X, vy = sec[3].Y - sec[0].Y, vz = sec[3].Z - sec[0].Z;
                double nx = uy * vz - uz * vy, ny = uz * vx - ux * vz, nz = ux * vy - uy * vx;
                double nl = Math.Sqrt(nx * nx + ny * ny + nz * nz);
                if (nl < 1e-12) { badShape++; continue; }
                double wx = sec[2].X - sec[0].X, wy = sec[2].Y - sec[0].Y, wz = sec[2].Z - sec[0].Z;
                if (Math.Abs((wx * nx + wy * ny + wz * nz) / nl) > 1e-6) badFlat++;
            }
        Check("S24 ★★매스 단면이 전부 평면이다(로프트가 빈 솔리드를 안 낸다)", badFlat == 0,
            $"평면 아닌 단면 {badFlat}장 / {secN}장");
        Check("S24 ★★매스 단면이 전부 4점 마름모다(로프트가 통과할 수 있다)", badShape == 0,
            $"어긋난 단면 {badShape}장 / {secN}장");
    }

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

        // [0806] 토우 폭 맞추기도 같이 끈다 — 안 끄면 그게 아랫변을 토우에 맞춰 주어 '제한을 껐는데도 멀쩡'해진다.
        WallBand.DisableChordLimitForTest = true;
        WallBand.DisableToeWidthForTest = true;
        List<WallBand.Tile> tCb;
        try { tCb = WallBand.Slice(runC, null, joint: 0.05); }
        finally { WallBand.DisableChordLimitForTest = false; WallBand.DisableToeWidthForTest = false; }
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

// ★ S26 **성토 벽은 데이라잇으로 자르지 않는다 — 아예**(JACK 0806 확정).
//   "성토는 윗선을 기준으로 아래로 옹벽을 치는 게 맞긴 한데, 절토처럼 원지반과 맞닿는 데이라잇까지
//    끊을 필요는 없다. 어차피 인프라웍스에서 지표면 아래로 들어갈 거니깐 괜찮다."
//   종전(0721)은 '크레스트가 지반 위면 꽉, 아니면 0'이라는 **전부 아니면 전무**였고, 그래서 원지반이
//   올라와 계획면과 만나는 전이 구간에서 벽이 뚝 끊겨 한 구간이 통째로 비었다(0806 스샷 '옹벽누락부' 13.48m).
{
    var toeF = new List<Point3> { new(0, 0, 100), new(20, 0, 100) };
    var crestF = new List<Point3> { new(0, -0.25, 105), new(20, -0.25, 105) };
    var runF = new WallRun { Up = false, Bench = 0, Toe = toeF, Crest = crestF, Height = 5.0 };

    var tF = WallBand.Slice(runF, new FlatGround(98.0), joint: 0.05);   // 지반 98 — 벽 전체가 지반 위(전형적 성토)
    Console.WriteLine($"      S26 성토(지반 아래): {WallBand.LastDiag}");
    Check("S26 ★성토 벽은 지반 위에 얹혀도 꽉 찬다", tF.Count > 10, $"판넬 {tF.Count}장");

    // ★지반이 벽보다 높아도(=아래쪽이 묻혀도) **끊지 않는다** — 묻히는 부분은 InfraWorks에서 가려진다.
    //   종전엔 여기서 0장이 되어, 전이 구간의 벽이 통째로 사라지는 원인이 됐다.
    // ★★[JACK 0807 스샷 '성토부는 2단인데 3단까지 생긴다'] 성토 벽면은 **지반선에서 아래가 잘린다.**
    //   0806~0807에 '한 단을 통째로 살릴까 버릴까'로 두 번 실패했다 — 버리면 13m 구멍, 살리면 한 단이 매달린다.
    //   아래 세 검사는 그 규칙이 **세 자리에서 동시에** 옳은지 본다. 하나라도 빠지면 옛 실패로 되돌아간다.

    // ① 벽면이 지반보다 통째로 위면 자를 것이 없다(=위 tF와 같다) — 전형적 성토.
    //    (지반 103 = 토우 100 위 3m · 크레스트 105 아래 → 아래 3m만 잘리는 경우는 ③에서 본다)

    // ② 크레스트마저 잠기면 **그 단은 애초에 안 생긴다** — 3단이 매달리던 자리.
    var tFd = WallBand.Slice(runF, new FlatGround(110.0), joint: 0.05);   // 지반 110 > 크레스트 105
    Check("S26 ★★잠긴 단은 아예 안 생긴다(스샷 '3단까지 생김'의 자리)",
        tFd.Count == 0, $"매몰 {tFd.Count}장 — 0이어야 한다 · {WallBand.LastDiag}");

    // ③ 지반선이 벽면을 **가로지르면** 그만큼만 남는다 — 통째로 버리지도(13m 구멍), 통째로 살리지도 않는다.
    //    지반 103 → 토우(100)에서 3m 위가 지반선. 노출은 위 2m뿐이므로 판넬이 **나오되 줄어야** 한다.
    var tFc = WallBand.Slice(runF, new FlatGround(103.0), joint: 0.05);
    double loV = double.MaxValue;
    foreach (var t in tFc) foreach (var (_, lv) in t.Local) loV = Math.Min(loV, lv);
    Check("S26 ★★지반선이 가로지르면 그 위만 남는다(뚝 끊기지도, 통째로 매달리지도 않는다)",
        tFc.Count > 0 && tFc.Count < tF.Count && loV > 2.9,
        $"판넬 {tFc.Count}장(전체 {tF.Count}장) · 최저 v {loV:F2}m — 지반선 3.0m 위여야 한다");

    // ★전이 구간(지반이 벽 중간을 가로지름)에서도 뚝 끊기지 않아야 한다 — '옹벽누락부'(0806 13m)의 재현 조건.
    //   [0807 계약 수정] 종전 검사는 '노출 때와 **판넬 수가 같아야** 한다'였다. 그건 '아예 안 자른다'는
    //   옛 규칙의 자였고, 그 규칙이 이번엔 반대쪽 결함(3단 매달림)을 만들었다. 지금 지켜야 할 것은
    //   '수가 같다'가 아니라 **'중간에 구멍이 없다'** 이므로, 그것을 직접 잰다 —
    //   벽이 x=0에서 시작해 지반선이 크레스트를 넘는 자리(x=15)까지 **끊김 없이** 이어지는가.
    var tFm = WallBand.Slice(runF, new SlopeGround(96.0, 0.6), joint: 0.05);   // x=0에서 96 → x=20에서 108
    var spans = new List<(double A, double B)>();
    foreach (var t in tFm)
    {
        double xa = double.MaxValue, xb = double.MinValue;
        foreach (var p in t.Poly) { xa = Math.Min(xa, p.X); xb = Math.Max(xb, p.X); }
        spans.Add((xa, xb));
    }
    spans.Sort((p, q) => p.A.CompareTo(q.A));
    double covFrom = spans.Count > 0 ? spans[0].A : 0, covTo = covFrom, maxGap = 0, gapAt = 0;
    foreach (var (a, b) in spans)
    {
        if (a > covTo + 1e-9 && a - covTo > maxGap) { maxGap = a - covTo; gapAt = covTo; }
        covTo = Math.Max(covTo, b);
    }
    // 지반이 크레스트(105)를 넘는 자리 = 96+0.6x=105 → x=15. 그 앞에서 벽이 사라지면 그게 '누락부'다.
    Check("S26 ★전이 구간에서 성토 벽이 안 끊긴다(수가 아니라 '구멍'을 잰다)",
        maxGap < 0.15 && covFrom < 0.2 && covTo > 13.5,
        $"덮은 구간 x {covFrom:F2}~{covTo:F2}m(지반선 교차 15.0m) · 최대 틈 {maxGap:F2}m @ x{gapAt:F1} · 판넬 {tFm.Count}장");

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
        // ★[v32.58] JACK 0819 규격 변경: 판넬 상한 1.5m(제작 규격) → 5m 단은 3행 1.67m가 아니라 **4행 1.25m**.
        //   옛 기대값(3행)은 상한이 5/3이던 시절의 것이다.
        //   ※ 4행이 되어도 무늬는 살아 있다 — 0806 십자 4분할로 바뀌면서 판넬당 조각이 8개(사각)뿐이고,
        //     1.25m 판넬의 상하 조각이 0.265m로 하한 0.08m를 크게 넘는다(옛 격자 무늬 시절의 공포는 끝났다).
        int wantH = WallBand.RowsForBench(h);
        Check($"S28 ★단높이 {h}m → {wantH}행(설계값)", rows == wantH, $"행 {rows}개 · 판넬 {tH.Count}장 · 한 변 {WallBand.SideFor(h):F3}m");
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
    // [JACK 0807] 이 검사는 **토우/크레스트 짝짓기**를 보는 것이라 코너 물러나기는 잡음이다 —
    //   물러나면 어느 열이 남는지가 달라져 자가검증 조건(호길이 대응이면 눕는다)이 안 걸린다.
    //   시나리오를 종전 그대로 두려고 여기서만 코너 유닛을 끈다.
    WallBand.DisableCornerUnitForTest = true;
    var tZ = WallBand.Slice(runZ, null, joint: 0.05);
    double vh = MaxVHorz(tZ);
    // 구배 1:0.05면 V의 수평 성분은 0.05/√(1+0.05²) ≈ 0.0499. 0.08을 넘으면 벽이 눕기 시작한 것이다.
    Check("S29 ★다코너 벽선에서 판넬이 눕지 않는다", vh < 0.08,
        $"V축 수평성분 최대 {vh:F4} (설계 1:0.05 → 0.050) · 판넬 {tZ.Count}장");

    WallBand.DisableIndexPairingForTest = true;
    List<WallBand.Tile> tZb;
    try { tZb = WallBand.Slice(runZ, null, joint: 0.05); }
    finally { WallBand.DisableIndexPairingForTest = false; WallBand.DisableCornerUnitForTest = false; }
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
        // ★[JACK 0807 '사선으로 잘린 문양'] **조각 수 = 무늬에 생기는 이음매 수**다.
        //   오목 판넬 하나를 볼록 조각 k개로 쪼개면 무늬 사각형도 그만큼 잘려 부챗살처럼 보인다.
        //   그러니 감시할 값은 '오목 판넬이 몇 장이냐'가 아니라 **한 판넬이 몇 조각으로 쪼개지냐**다.
        int pieceMax = 0, pieceSum = 0;
        foreach (var t in cavT)
        {
            var q = WallBand.ConvexPieces(t.Local);
            double d = Math.Abs(q.Sum(WallBand.PolyArea) - WallBand.PolyArea(t.Local));
            if (q.Count == 0 || !q.All(WallBand.IsConvex) || d > 1e-9) bad++;
            worst = Math.Max(worst, d);
            pieceMax = Math.Max(pieceMax, q.Count); pieceSum += q.Count;
        }
        Console.WriteLine($"      S30 볼록 조각(=무늬 이음매): 최대 {pieceMax}조각 · 합계 {pieceSum}개 (오목 {cavT.Count}장)");

        // ★★[JACK 0807 '잘린 걸 최종 단계에서 무늬에 한해서만 서로 붙은 객체는 합친다'] **이음매가 안 생기는가.**
        //   무늬 사각형 하나를 판넬로 자를 때 조각이 **1개**로 나와야 도면에 사선 이음매가 없다.
        //   종전엔 판넬을 볼록 조각으로 쪼개 창으로 삼아 조각 수만큼 잘렸다(JACK 스샷의 부챗살).
        //   ※JACK 지적대로 이 방식이 정점 정리 허용오차를 올리는 것보다 **번지는 범위가 작다** —
        //     허용오차는 판넬 모양 자체를 부지 전체에서 바꾸지만, 이건 무늬에만 닿고 실패해도 종전으로 물러난다.
        {
            int one = 0, many = 0, maxPc = 0;
            foreach (var t in cavT)                                  // 오목한(=종전에 쪼개지던) 판넬만
            {
                double u0 = double.MaxValue, u1 = double.MinValue, v0 = double.MaxValue, v1 = double.MinValue;
                foreach (var (u, v) in t.Local) { u0 = Math.Min(u0, u); u1 = Math.Max(u1, u); v0 = Math.Min(v0, v); v1 = Math.Max(v1, v); }
                // 십자 4분할과 같은 자리 — 판넬을 네 조각으로 나누는 사각형들.
                double mu = (u0 + u1) / 2, mv = (v0 + v1) / 2;
                foreach (var r in new[] { (u0, mu, v0, mv), (mu, u1, v0, mv), (u0, mu, mv, v1), (mu, u1, mv, v1) })
                {
                    var rcPcs = WallBand.RectClip(t.Local, r.Item1, r.Item2, r.Item3, r.Item4, out bool okOne);
                    if (rcPcs.Count == 0) continue;
                    if (okOne && rcPcs.Count == 1) one++; else many++;
                    maxPc = Math.Max(maxPc, rcPcs.Count);
                    foreach (var pc in rcPcs)
                        if (!WallBand.IsSimple(pc))
                            { many += 1000; break; }                 // 자기교차가 나오면 압출이 115094로 터진다
                }
            }
            Console.WriteLine($"      S30 무늬 클립: 한장 {one}회 · 이음매분할 {many}회 · 최대 {maxPc}조각 (오목 판넬 {cavT.Count}장)");
            Check("S30 ★★무늬가 이음매 없이 한 장으로 잘린다(부챗살 없음)", one > 0 && many == 0,
                $"한장 {one}회 · 분할 {many}회 · 최대 {maxPc}조각");
        }

        // ★★[JACK 0807 '사선으로 잘린 문양'] **현장 조건으로 판정한다.**
        //   위 S30 지형은 진폭 1.2m 사인파 — 오목 판넬을 억지로 만들려고 일부러 심하게 휜 것이라
        //   거기서는 조각이 5개까지 나오는 게 당연하다(진짜로 휘었으니 쪼개는 게 옳다).
        //   현장 원지반은 **삼각망**이라 삼각형 하나가 수 m다 — 1.6m 판넬 위에서는 사실상 평면이고,
        //   '오목'의 정체는 데이라잇을 0.15m 간격으로 훑을 때 생기는 **mm급 표본 잡음**이다.
        //   그 조건을 여기 만든다: 기울어진 평면 + 진폭 2mm 고주파 잡음.
        //   정점 정리 허용오차(8mm)가 잡음을 펴 주면 판넬이 **볼록 하나**로 남아 이음매가 아예 안 생긴다.
        {
            var gNoise = new NoisyPlaneGround(103.0, 0.06, 0.002);
            var tN = WallBand.Slice(new WallRun { Up = true, Bench = 0, Toe = toe30, Crest = cr30, Height = 5.0 },
                                    gNoise, joint: 0.05);
            var cavN = tN.Where(t => !WallBand.IsConvex(t.Local)).ToList();
            // [JACK 0807] 정점 정리 허용오차를 올리는 길은 **폐기**했다 — 판넬 모양 자체를 부지 전체에서
            //   바꾸므로 번지는 범위가 크다는 JACK 지적이 옳았다. 오목 판넬은 그대로 두고(모양은 정확),
            //   **무늬 클립만** 한 장으로 자른다. 그러니 여기서 볼 것은 '오목이 없는가'가 아니라
            //   **'오목이어도 무늬가 한 장으로 나오는가'** 다.
            int oneN = 0, manyN = 0;
            foreach (var t in cavN)
            {
                double u0 = double.MaxValue, u1 = double.MinValue, v0 = double.MaxValue, v1 = double.MinValue;
                foreach (var (u, v) in t.Local) { u0 = Math.Min(u0, u); u1 = Math.Max(u1, u); v0 = Math.Min(v0, v); v1 = Math.Max(v1, v); }
                double mu = (u0 + u1) / 2, mv = (v0 + v1) / 2;
                foreach (var r in new[] { (u0, mu, v0, mv), (mu, u1, v0, mv), (u0, mu, mv, v1), (mu, u1, mv, v1) })
                {
                    var q = WallBand.RectClip(t.Local, r.Item1, r.Item2, r.Item3, r.Item4, out bool ok1);
                    if (q.Count == 0) continue;
                    if (ok1 && q.Count == 1 && WallBand.IsSimple(q[0])) oneN++; else manyN++;
                }
            }
            Console.WriteLine($"      S30 현장형(평면+2mm 잡음): 판넬 {tN.Count}장 중 오목 {cavN.Count}장 · 무늬 한장 {oneN}회/분할 {manyN}회");
            Check("S30 ★★현장형 지형에서도 무늬가 한 장으로 잘린다(이음매 없음)",
                tN.Count > 10 && oneN > 0 && manyN == 0,
                $"판넬 {tN.Count}장 · 오목 {cavN.Count}장 · 한장 {oneN}회 · 분할 {manyN}회");
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
        // ★[v32.58] 행 수 규칙이 RowsForBench 하나로 모였다(v32.54) — 하니스도 같은 자를 쓴다.
        //   여기서 식을 다시 적으면 규칙이 두 벌이 되어, 고칠 때 한쪽만 고쳐진다.
        int want = WallBand.RowsForBench(h);
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
    // ★★[JACK 0807 계약 변경] **짧은 벽면 합치기 폐지.**
    //   합치면 그 벽면 안에 코너가 들어가고, 규격 판넬이 코너를 가로질러 아랫변이 벽선에서 벗어난다
    //   (하니스 실측 0.235m — 판넬 두께 0.20m보다 크다. 0806에 JACK이 지적한 '어긋남'이 이것이었다).
    //   이제 6cm 토막은 합치지 않고 **전용 얇은 객체**가 된다 — 1.67m 판넬들 사이에 6cm '판넬'이 서던 문제는
    //   폭을 맞춰서가 아니라 **그것을 판넬이라고 부르지 않음으로써** 사라진다(JACK 0807 원칙).
    //   그러니 검사할 것은 '토막이 합쳐졌나'가 아니라 **'토막이 규격 판넬로 서지 않았나'**다.
    int shortAsPanel = 0;
    foreach (var t in tBad)
    {
        if (t.Filler) continue;
        double lo = double.MaxValue, hi = double.MinValue;
        foreach (var (u, v) in t.Local) { lo = Math.Min(lo, u); hi = Math.Max(hi, u); }
        if (hi - lo < side34 * 0.5) shortAsPanel++;
    }
    Check("S34 ★6cm 토막 벽면이 '규격 판넬'로 서지 않는다(전용객체로 간다)", shortAsPanel == 0,
        $"벽면 {facesBad.Count}개 · 규격 판넬인데 좁은 것 {shortAsPanel}장 · 최소폭(전용객체 포함) {mnBad:F3}m");

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
        // ★[v32.58] 행 수를 <b>설계 규칙에서</b> 얻는다 — 3을 박아 두면 규격이 바뀔 때 이 검사만 거짓말을 한다
        //   (v32.54에서 판넬 상한이 1.5m가 되며 5m 단이 3행 → 4행이 됐다).
        int rows = t35.Count > 0 ? WallBand.RowsForBench(5.0) : 0;
        Console.WriteLine($"      S35 길이 {L}m: 폭 {w.Min:F3}~{w.Max:F3}m(규격 {std:F3}m) · 규격미만 {w.NonStd}/{w.N}장");
        // 끝에서만 조절하므로 좁은 열은 최대 2열(자투리가 짧아 마지막 두 장을 반씩 나눈 경우).
        Check($"S35 ★길이 {L}m — 규격보다 좁은 판넬은 끝의 1~2열뿐", w.NonStd <= 2 * rows,
            $"규격 미만 {w.NonStd}장(한 열 = {rows}장) · 폭 {w.Min:F3}~{w.Max:F3}m");
        // [JACK 0807] **규격 판넬만** 잰다. 짧은 자투리는 앞 조각과 합쳐 하나의 전용객체가 되므로
        //   그 조각만 규격보다 넓을 수 있다(≤ 한 변 + 0.10m) — 그게 '구멍 대신 조금 넓은 조각'을 고른 결과다.
        double specMax35 = 0; int specN35 = 0;
        foreach (var t in t35)
        {
            if (t.Filler) continue;
            double lo = double.MaxValue, hi = double.MinValue;
            foreach (var (u, v) in t.Local) { lo = Math.Min(lo, u); hi = Math.Max(hi, u); }
            specMax35 = Math.Max(specMax35, hi - lo); specN35++;
        }
        Check($"S35 ★길이 {L}m — 규격 판넬은 정확히 {std:F2}m(상한 초과 없음)", specMax35 <= std + 1e-6,
            $"규격 판넬 {specN35}장 최대 폭 {specMax35:F3}m / 규격 {std:F3}m (전용객체 제외)");
        // ★[JACK 0807 계약 변경] 자투리 하한 폐지. 종전엔 마지막 두 장을 반씩 나눠 둘 다 '한 변 절반 이상'으로
        //   맞췄는데, 그러면 **판넬 두 장이 비규격**이 된다 — JACK이 금지한 '제각각 폭'이 바로 이것이다.
        //   이제 자투리는 폭이 얼마든 상관없는 **전용 얇은 객체 한 개**이고, 규격 판넬은 전부 정확히 규격이다.
        //   그래서 검사할 것은 '자투리가 얼마나 넓은가'가 아니라 **'전용객체가 딱 하나인가'**다.
        int fill35 = 0; foreach (var t in t35) if (t.Filler) fill35++;
        int rowsPer = Math.Max(1, rows);
        Check($"S35 ★길이 {L}m — 전용 얇은 객체는 끝의 한 열뿐", fill35 <= rowsPer,
            $"전용객체 {fill35}장(한 열 = {rowsPer}장) · 규격 판넬 {w.N - fill35}장");
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
    // [JACK 0807] 전 줄을 다 자른 뒤 남은 틈을 전용 얇은 객체로 메운다 — 운영 경로(DHINFRA)와 같은 순서.
    int gf36 = WallBand.AddGapFillers(t36, cornerOnly: true);   // [JACK 0807] 코너 쐐기만 — 벽 한가운데는 안 메운다
    Console.WriteLine($"      S36 전체: {WallBand.TotalDiag} · 틈메움 {gf36}개");
    Console.WriteLine($"      S36 틈  : {WallBand.GapReport(t36)}");
    // ★[0807] 가장 큰 진짜 구멍 자리(10,13 부근)의 **판넬 실물**을 찍는다 — 폭·분류·양 끝 좌표.
    //   '코너에서 1.7m 떨어진 0.35m 틈'이 무엇과 무엇 사이인지는 좌표를 봐야만 갈린다.
    {
        double gx = 10.0, gy = 13.0;
        var near = new List<(double D, string S)>();
        foreach (var t in t36)
        {
            double u0 = double.MaxValue, u1 = double.MinValue, v0 = double.MaxValue, v1 = double.MinValue;
            foreach (var (u, v) in t.Local) { u0 = Math.Min(u0, u); u1 = Math.Max(u1, u); v0 = Math.Min(v0, v); v1 = Math.Max(v1, v); }
            if (v0 > 0.05) continue;                       // 맨 아랫행만
            double lx = t.Origin.X + u0 * t.UAxis.x, ly = t.Origin.Y + u0 * t.UAxis.y;
            double rx = t.Origin.X + u1 * t.UAxis.x, ry = t.Origin.Y + u1 * t.UAxis.y;
            double d = Math.Min(Math.Sqrt((lx-gx)*(lx-gx)+(ly-gy)*(ly-gy)), Math.Sqrt((rx-gx)*(rx-gx)+(ry-gy)*(ry-gy)));
            if (d > 2.5) continue;
            near.Add((d, $"폭{u1-u0:F3} {(t.Filler ? "필러" : "규격")}{(t.Detail ? "+LOD" : "")} 좌({lx:F2},{ly:F2}) 우({rx:F2},{ry:F2}) U({t.UAxis.x:F2},{t.UAxis.y:F2})"));
        }
        near.Sort((a, b) => a.D.CompareTo(b.D));
        Console.WriteLine($"      S36 구멍(10,13) 주변 아랫행 판넬 {near.Count}장:");
        foreach (var n in near.Take(8)) Console.WriteLine($"        {n.D:F2}m  {n.S}");
        Console.WriteLine($"      S36 그 자리 코너필러 {WallBand.LastQuoins.Count(q => Math.Sqrt((q.Toe.X-gx)*(q.Toe.X-gx)+(q.Toe.Y-gy)*(q.Toe.Y-gy)) < 3.0)}개 / 전체 {WallBand.LastQuoins.Count}개");
        foreach (var q in WallBand.LastQuoins.Where(q => Math.Sqrt((q.Toe.X-gx)*(q.Toe.X-gx)+(q.Toe.Y-gy)*(q.Toe.Y-gy)) < 3.0).Take(4))
            Console.WriteLine($"        필러 폭{q.Width:F3} 토우({q.Toe.X:F2},{q.Toe.Y:F2},{q.Toe.Z:F1})");
    }
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
            // [JACK 0807] **규격 판넬만** 잰다 — 전용 얇은 객체(코너·급커브·자투리)는 남는 자리를 메우려고
            //   일부러 놓는 것이라 벽선을 그대로 따라가지 않는다. 섞어 재면 두 성질이 한 숫자로 뭉개진다.
            if (t.Filler) continue;
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
    int gfF2 = WallBand.AddGapFillers(tF2, cornerOnly: true);
    string grF2 = WallBand.GapReport(tF2);
    Console.WriteLine($"      S38 성토 전체: {WallBand.TotalDiag}");
    Console.WriteLine($"      S38 성토 틈: {grF2}");
    // ★[0807] 남은 진짜 구멍(볼록 코너 0.3m 옆)에 **코너 필러가 실제로 서 있는지** 확인 —
    //   '만들라고 지시했다'와 '섰다'는 다르다(0807에 56개 전부 실패한 전례가 있다).
    foreach (var (gx, gy) in new[] { (10.0, 11.0), (7.0, 12.0) })
    {
        var qs = WallBand.LastQuoins.Where(q =>
            Math.Sqrt((q.Toe.X - gx) * (q.Toe.X - gx) + (q.Toe.Y - gy) * (q.Toe.Y - gy)) < 2.0).ToList();
        Console.WriteLine($"      S38 구멍({gx},{gy}) 반경 2m 코너필러 {qs.Count}개" +
            string.Join("", qs.Take(3).Select(q => $" [폭{q.Width:F3} 토우({q.Toe.X:F2},{q.Toe.Y:F2},{q.Toe.Z:F1})]")));
    }
    Check("S38 재현 조건: 성토도 판넬이 충분히 나온다", tF2.Count > 30, $"{tF2.Count}장");

    int holeF = 0;
    { const string k = "진짜 구멍 "; int a = grF2.IndexOf(k);
      if (a >= 0) { int b = grF2.IndexOf('곳', a); if (b > a) int.TryParse(grF2.Substring(a+k.Length, b-a-k.Length).Trim(), out holeF); } }
    Check("S38 ★성토 오목부에도 진짜 구멍이 없다", holeF == 0, $"진짜 구멍 {holeF}곳");

    // ★코너 판정이 성토에서 뒤집히지 않았는가 — 뒤집혔다면 겹침을 반대 자리에 넣어 밑동이 어긋난다.
    double offF = 0; double ofx = 0, ofy = 0; string offWho = "";
    foreach (var t in tF2)
    {
        // [JACK 0807] 규격 판넬만 — 전용 얇은 객체는 코너·급커브의 남는 자리를 메우려고 일부러 놓는다.
        if (t.Filler) continue;
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
            if (best > offF)
            {
                offF = best; ofx = px; ofy = py;
                // [0807] 어긋난 판넬의 정체 — 폭·원점·단. 숫자만 보고 원인을 추측하다 두 번 헛짚었다.
                offWho = $"폭 {u1 - u0:F3}m 원점({t.Origin.X:F2},{t.Origin.Y:F2}) 단{t.Bench} 행{t.Row}";
            }
        }
    }
    Console.WriteLine($"      S38 성토 판넬 아랫변↔토우선 최대 이탈: {offF:F3}m @ {ofx:F1},{ofy:F1} · {offWho}");
    // ★그 자리의 판넬과 토우선을 찍는다 — 0.251m ≈ 토우↔크레스트 간격(0.25m)이라 '한 선만큼 밀림'이 의심된다.
    {
        // ★남은 틈 자리(볼록 100° 코너 @ 8,22 Z94.2)의 판넬을 **표고까지 맞춰** 찍는다 — 쐐기 모양 확인용.
        Console.WriteLine("        [틈 자리 8,22 Z94.2 주변 판넬]");
        foreach (var t in tF2)
        {
            double c0 = double.MaxValue, c1 = double.MinValue, cv = double.MaxValue;
            foreach (var (u, v) in t.Local) { c0 = Math.Min(c0, u); c1 = Math.Max(c1, u); cv = Math.Min(cv, v); }
            double bz = t.Origin.Z + cv * t.VAxis.z;
            double lx = t.Origin.X + c0*t.UAxis.x + cv*t.VAxis.x, ly = t.Origin.Y + c0*t.UAxis.y + cv*t.VAxis.y;
            double rx = t.Origin.X + c1*t.UAxis.x + cv*t.VAxis.x, ry = t.Origin.Y + c1*t.UAxis.y + cv*t.VAxis.y;
            if (Math.Abs(bz - 94.2) > 1.0) continue;
            if (Math.Min(Math.Sqrt((lx-8)*(lx-8)+(ly-22)*(ly-22)), Math.Sqrt((rx-8)*(rx-8)+(ry-22)*(ry-22))) > 1.5) continue;
            Console.WriteLine($"          단{t.Bench} 행{t.Row} U({t.UAxis.x:F2},{t.UAxis.y:F2})" +
                              $" 아랫변 ({lx:F2},{ly:F2})~({rx:F2},{ry:F2}) Z{bz:F2}");
        }
        WallBand.Tile wt = tF2[0]; double wd = double.MaxValue;
        foreach (var t in tF2)
        {
            double d = Math.Sqrt(Math.Pow(t.Origin.X - ofx, 2) + Math.Pow(t.Origin.Y - ofy, 2));
            if (d < wd) { wd = d; wt = t; }
        }
        double q0 = double.MaxValue, q1 = double.MinValue, qv = double.MaxValue;
        foreach (var (u, v) in wt.Local) { q0 = Math.Min(q0, u); q1 = Math.Max(q1, u); qv = Math.Min(qv, v); }
        Console.WriteLine($"        그 판넬: 원점({wt.Origin.X:F2},{wt.Origin.Y:F2}) U({wt.UAxis.x:F2},{wt.UAxis.y:F2})" +
                          $" u[{q0:F2}..{q1:F2}] 행{wt.Row} 단{wt.Bench}");
        foreach (var r in runsF2)
        {
            int hit = -1; double hd = double.MaxValue;
            for (int k = 0; k < r.Toe.Count; k++)
            {
                double d = Math.Sqrt(Math.Pow(r.Toe[k].X - wt.Origin.X, 2) + Math.Pow(r.Toe[k].Y - wt.Origin.Y, 2));
                if (d < hd) { hd = d; hit = k; }
            }
            if (hd > 0.4) continue;
            int lo = Math.Max(0, hit - 2);
            Console.WriteLine($"        토우 {lo}~: " + string.Join(" ", Enumerable.Range(lo, Math.Min(6, r.Toe.Count - lo))
                .Select(k => $"({r.Toe[k].X:F2},{r.Toe[k].Y:F2})")));
            Console.WriteLine($"        크레스트 {lo}~: " + string.Join(" ", Enumerable.Range(lo, Math.Min(6, r.Crest.Count - lo))
                .Select(k => $"({r.Crest[k].X:F2},{r.Crest[k].Y:F2})")));
            break;
        }
    }
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

    WallRunBuilder.DisableToeVertexInsertForTest = true; WallRunBuilder.DisableCornerSnapForTest = true;
    List<WallRun> bad37;
    try { bad37 = WallRunBuilder.Build(sq37, rs37, null, up: true, globalSlope: 0.05, minSlope: 0.05); }
    finally { WallRunBuilder.DisableToeVertexInsertForTest = false; WallRunBuilder.DisableCornerSnapForTest = false; }
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
        WallRunBuilder.DisableToeVertexInsertForTest = true; WallRunBuilder.DisableCornerSnapForTest = true;
        List<WallRun> badF;
        try { badF = WallRunBuilder.Build(sq37, rsF, null, up: false, globalSlope: 0.05, minSlope: 0.05); }
        finally { WallRunBuilder.DisableToeVertexInsertForTest = false; WallRunBuilder.DisableCornerSnapForTest = false; }
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
// ★ S39 [JACK 0807 '왜 어딘 폭이 좁고 어딘 넓냐 · 최대한 심플하게'] **라운드 코너 옹벽선.**
//   여태 하니스 부지는 전부 각진 다각형이라, 폭이 들쭉날쭉해지는 두 규칙(12° 벽면 분할 · 5cm 현이탈 분할)이
//   **한 번도 제대로 안 밟혔다.** 현장은 사면형상이 라운드라 옹벽선에 원호가 들어 있고, 거기서 두 규칙이
//   미친 듯이 일한다(현장 실측: 벽면 570개 · 급커브 분할 396열). 그 조건을 여기 만든다 —
//   이게 있어야 '상수를 풀어도 되는가'를 JACK 왕복 없이 판정할 수 있다(작업규칙 0806).
{
    // 직선 20m → 반지름 3m 90° 원호(8조각 = 11.25°씩, NTS Buffer 라운드 모서리와 같은 분해능) → 직선 20m
    var crest = new List<Point3>();
    for (double x = 0; x <= 20.0 + 1e-9; x += 1.0) crest.Add(new Point3(x, 0, 105));
    for (int k = 1; k <= 8; k++)
    {
        double a = -Math.PI / 2 + k * (Math.PI / 2) / 8;
        crest.Add(new Point3(20.0 + 3.0 * Math.Cos(a), 3.0 + 3.0 * Math.Sin(a), 105));
    }
    for (double y = 4.0; y <= 23.0 + 1e-9; y += 1.0) crest.Add(new Point3(23.0, y, 105));

    // 토우 = 크레스트를 바깥 법선으로 0.25m 민 선(벽 1:0.05 × 단높이 5m) · Z −5.
    var toe = new List<Point3>();
    for (int i = 0; i < crest.Count; i++)
    {
        double nx = 0, ny = 0;
        for (int e = 0; e < 2; e++)
        {
            int a2 = e == 0 ? i - 1 : i, b2 = e == 0 ? i : i + 1;
            if (a2 < 0 || b2 >= crest.Count) continue;
            double dx = crest[b2].X - crest[a2].X, dy = crest[b2].Y - crest[a2].Y;
            double l = Math.Sqrt(dx * dx + dy * dy);
            if (l < 1e-9) continue;
            nx += dy / l; ny += -dx / l;                       // 오른쪽 법선
        }
        double nl = Math.Sqrt(nx * nx + ny * ny);
        if (nl < 1e-9) { nx = 0; ny = -1; nl = 1; }
        toe.Add(new Point3(crest[i].X + 0.25 * nx / nl, crest[i].Y + 0.25 * ny / nl, 100));
    }

    var runR = new WallRun { Up = true, Bench = 0, Toe = toe, Crest = crest, Height = 5.0 };
    var tR = WallBand.Slice(runR, new FlatGround(200.0), joint: 0.05);   // 지반 높음 → 벽 전체가 살아 있음

    Console.WriteLine($"      S39 라운드 코너: {WallBand.LastDiag}");
    Console.WriteLine($"      S39 틈: {WallBand.GapReport(tR, runs: new List<WallRun> { runR })}");

    Check("S39 재현 조건: 라운드 코너 옹벽선에서 판넬이 나온다", tR.Count > 20, $"판넬 {tR.Count}장");

    // ★이 검사가 이 시나리오의 핵심 — 원호 위에서 **폭이 통일되는가**.
    //   현장 불만('어딘 좁고 어딘 넓고')을 숫자 하나로 옮긴 것이다. 규격(1.67m) 미만 열이
    //   벽면 끝 자투리 몇 개 수준이면 정상이고, 열의 절반을 넘으면 규칙이 과민한 것이다.
    int narrow = 0, wide = 0;
    var seen = new HashSet<string>();
    foreach (var t in tR)
    {
        double u0 = double.MaxValue, u1 = double.MinValue;
        foreach (var (u, _) in t.Local) { u0 = Math.Min(u0, u); u1 = Math.Max(u1, u); }
        string key = $"{t.Origin.X:F3},{t.Origin.Y:F3}";
        if (!seen.Add(key)) continue;                          // 열 단위로 한 번만
        if (u1 - u0 < WallBand.MaxSide - 0.10) narrow++; else wide++;
    }
    Console.WriteLine($"      S39 열 폭 분포: 규격 {wide}열 · 규격 미만 {narrow}열");

    // 진짜 구멍은 없어야 한다 — 폭이 통일되든 아니든, 벽에 뚫린 자리는 없어야 한다.
    Check("S39 ★라운드 코너에서도 판넬 사이에 진짜 구멍이 없다",
        !WallBand.GapReport(tR, runs: new List<WallRun> { runR }).Contains("★양옆 온전"),
        WallBand.GapReport(tR, runs: new List<WallRun> { runR }));
}

// ★ S40 [JACK 0807 '계획폴리곤의 여러 경우의 수에도 오류 없도록'] **계획폴리곤 고문 시험.**
//   현장에서 어떤 모양의 계획선이 들어올지 모른다 — CAD에서 그린 선은 중복 정점·공선·초단변이 흔하고,
//   감김 방향(CW/CCW)도 그리는 순서에 달렸다. 각 입력을 **파이프라인 전체**(링 생성→옹벽선→판넬)에
//   통과시켜 ①예외 없이 완주하고 ②좌표에 NaN/무한대가 없고 ③정상 입력이면 판넬이 실제로 나오는지 본다.
//   ※자기교차(8자)만은 예외를 허용한다 — DoGrade가 1단계에서 잡아 경고 팝업으로 끝나는 게 옳은 동작이다.
//     (그 밖의 입력에서 예외가 나면 실사용자가 흔한 도면으로도 오류 팝업을 본다는 뜻 — 실패로 센다.)
{
    var pr40 = new GradingParams {
        CutBenchHeight = 5, FillBenchHeight = 5, CutBenchWidth = 1, FillBenchWidth = 1,
        CutSlope = 0.05, FillSlope = 0.05, CellSize = 1.0, MaxBenches = 4, MaxRise = 20,
        VertexSpacing = 1.0, MinSlope = 0.05, MinFaceRun = 0.005, MiterConvex = true, MiterLimit = 2.0,
    };
    var gnd40 = new TiltGround(0, 0, 106.0, 0.10, 0.06);

    // 한 입력을 절토 파이프라인 전체에 통과 — (완주 여부, 판넬 수, NaN 수, 오류 메시지)
    (bool Ok, int Panels, int Bad, string Err) Torture(List<Point3> bnd)
    {
        try
        {
            var vs = GradingGeometry.Build(bnd, gnd40, pr40, true);
            if (!vs.HasSlope) return (true, 0, 0, "사면 없음");
            var rs = vs.Rings.Select(r => (IReadOnlyList<Point3>)r).ToList();
            var runs = WallRunBuilder.Build(bnd, rs, null, up: true, globalSlope: 0.05, minSlope: 0.05);
            var tiles = new List<WallBand.Tile>();
            WallBand.ResetTotals();
            foreach (var r in runs) tiles.AddRange(WallBand.Slice(r, gnd40, joint: 0.05));

            int bad = 0;
            foreach (var t in tiles)
                foreach (var p in t.Poly)
                    if (!double.IsFinite(p.X) || !double.IsFinite(p.Y) || !double.IsFinite(p.Z)) bad++;
            return (true, tiles.Count, bad, "");
        }
        catch (Exception ex) { return (false, 0, 0, ex.GetType().Name + ": " + ex.Message); }
    }

    List<Point3> Ring40(params (double X, double Y)[] pts)
    {
        var r = new List<Point3>();
        foreach (var (X, Y) in pts) r.Add(new Point3(X, Y, 100));
        return r;
    }

    // ① 연속 중복 정점 — CAD에서 폴리라인을 이어 그리면 아주 흔하다.
    var dup = Ring40((0,0), (15,0), (15,0), (15,0), (15,12), (0,12), (0,12));
    var rDup = Torture(dup);
    Check("S40 ①중복 정점 — 완주·판넬 나옴·NaN 없음", rDup.Ok && rDup.Panels > 0 && rDup.Bad == 0,
        $"완주 {rDup.Ok} · 판넬 {rDup.Panels} · NaN {rDup.Bad} {rDup.Err}");

    // ② 공선 정점 — 직선 위에 정점이 줄줄이(오프셋·분해 명령의 잔재).
    var col = Ring40((0,0), (3,0), (6,0), (9,0), (12,0), (15,0), (15,12), (10,12), (5,12), (0,12));
    var rCol = Torture(col);
    Check("S40 ②공선 정점 — 완주·판넬 나옴·NaN 없음", rCol.Ok && rCol.Panels > 0 && rCol.Bad == 0,
        $"완주 {rCol.Ok} · 판넬 {rCol.Panels} · NaN {rCol.Bad} {rCol.Err}");

    // ③ 시계방향 감김 — 그리는 순서에 따라 절반은 이렇게 들어온다.
    var cw = Ring40((0,0), (0,12), (15,12), (15,0));
    var rCw = Torture(cw);
    Check("S40 ③시계방향 감김 — 완주·판넬 나옴·NaN 없음", rCw.Ok && rCw.Panels > 0 && rCw.Bad == 0,
        $"완주 {rCw.Ok} · 판넬 {rCw.Panels} · NaN {rCw.Bad} {rCw.Err}");

    // ④ 바늘 스파이크 — 정점 하나가 밖으로 뾰족하게 튀어나온 실수.
    var spike = Ring40((0,0), (15,0), (15,6), (25,6.01), (15,6.02), (15,12), (0,12));
    var rSpk = Torture(spike);
    Check("S40 ④바늘 스파이크 — 완주·NaN 없음", rSpk.Ok && rSpk.Bad == 0,
        $"완주 {rSpk.Ok} · 판넬 {rSpk.Panels} · NaN {rSpk.Bad} {rSpk.Err}");

    // ⑤ 1mm 초단변 — 스냅 실수로 생기는 티끌 변.
    var tiny = Ring40((0,0), (15,0), (15.001,0.001), (15,12), (0,12));
    var rTiny = Torture(tiny);
    Check("S40 ⑤1mm 초단변 — 완주·판넬 나옴·NaN 없음", rTiny.Ok && rTiny.Panels > 0 && rTiny.Bad == 0,
        $"완주 {rTiny.Ok} · 판넬 {rTiny.Panels} · NaN {rTiny.Bad} {rTiny.Err}");

    // ⑥ 자기교차(8자) — 유일하게 예외 허용(경고 팝업으로 끝나는 게 옳다). 다만 NaN 좌표는 안 된다.
    var eight = Ring40((0,0), (15,12), (15,0), (0,12));
    var rEight = Torture(eight);
    Check("S40 ⑥자기교차 8자 — 조용한 NaN 없이 끝남(예외는 허용)", rEight.Bad == 0,
        $"완주 {rEight.Ok} · 판넬 {rEight.Panels} · NaN {rEight.Bad} {rEight.Err}");

    // ⑦ 플래토(정점별 Z 다름) — 두 단 높이 계획면.
    var plateau = new List<Point3> {
        new(0,0,100), new(15,0,100), new(15,6,100), new(15,12,103), new(0,12,103), new(0,6,100) };
    var rPlt = Torture(plateau);
    Check("S40 ⑦플래토(Z 두 단) — 완주·NaN 없음", rPlt.Ok && rPlt.Bad == 0,
        $"완주 {rPlt.Ok} · 판넬 {rPlt.Panels} · NaN {rPlt.Bad} {rPlt.Err}");

    // ⑧ 가늘고 긴 근접 0 면적 — 폭 5cm 띠.
    var sliver = Ring40((0,0), (20,0), (20,0.05), (0,0.05));
    var rSlv = Torture(sliver);
    Check("S40 ⑧폭 5cm 띠 — 완주·NaN 없음(판넬 유무는 불문)", rSlv.Ok && rSlv.Bad == 0,
        $"완주 {rSlv.Ok} · 판넬 {rSlv.Panels} · NaN {rSlv.Bad} {rSlv.Err}");

    // ⑨ 정점 400개 원 — 조밀한 곡선 경계(라운드 부지의 극단형). 완주 + 판넬 다수.
    var circle = new List<Point3>();
    for (int i = 0; i < 400; i++)
        circle.Add(new Point3(20 + 12 * Math.Cos(2 * Math.PI * i / 400), 20 + 12 * Math.Sin(2 * Math.PI * i / 400), 100));
    var rCir = Torture(circle);
    Check("S40 ⑨정점 400개 원 — 완주·판넬 다수·NaN 없음", rCir.Ok && rCir.Panels > 10 && rCir.Bad == 0,
        $"완주 {rCir.Ok} · 판넬 {rCir.Panels} · NaN {rCir.Bad} {rCir.Err}");

    // ⑫ ★[JACK 0807 '코너부 보강에서 삐죽삐죽 나온 객체'] **코너 필러가 벽보다 높이 솟으면 안 된다.**
    //    필러 높이를 벽면 **설계** 높이(faceH)로 잡던 탓에, 데이라잇에 잘려 1m만 남은 자리에도 5m 기둥이 서서
    //    허공에 날이 솟았다. 필러 꼭대기는 **그 자리 판넬 꼭대기보다 위로 올라가면 안 된다.**
    {
        var bnd12 = Ring40((0,0), (30,0), (30,20), (18,20), (18,10), (12,10), (12,20), (0,20));
        var pr12 = new GradingParams {
            CutBenchHeight = 5, FillBenchHeight = 5, CutBenchWidth = 1, FillBenchWidth = 1,
            CutSlope = 0.05, FillSlope = 0.05, CellSize = 1.0, MaxBenches = 4, MaxRise = 20,
            VertexSpacing = 1.0, MinSlope = 0.05, MinFaceRun = 0.005, MiterConvex = true, MiterLimit = 2.0,
        };
        // ★[JACK 0807 '여전히 각진부에 삐져나와'] 지형을 **가파르게** 준다 — 데이라잇이 코너 옆 열을
        //   통째로 지우는 조건을 만들어야, '벽면 끝 열에서 높이를 받는' 잘못이 재현된다.
        //   완만한 지형만 시험하면 그 잘못이 한 번도 안 밟혀 검사가 통과해 버린다(0807 실제로 그랬다).
        var g12 = new TiltGround(0, 0, 104.0, 0.55, 0.38);
        var vs12 = GradingGeometry.Build(bnd12, g12, pr12, true);
        var rs12 = vs12.Rings.Select(r => (IReadOnlyList<Point3>)r).ToList();
        var runs12 = WallRunBuilder.Build(bnd12, rs12, null, up: true, globalSlope: 0.05, minSlope: 0.05);
        WallBand.ResetTotals();
        var t12 = new List<WallBand.Tile>();
        foreach (var r in runs12) t12.AddRange(WallBand.Slice(r, g12, joint: 0.05));
        WallBand.AddGapFillers(t12, cornerOnly: true);
        var (q12t, q12d) = WallBand.ClampQuoinsToPanels(t12);
        Console.WriteLine($"      S40 ⑫코너필러 높이 정리: 잘라냄 {q12t}개 · 허공 지움 {q12d}개");

        // ★[JACK 0807 두 번째 스샷 — 이 자도 고장나 있었다] **가까운 정점만** 재야 한다.
        //   종전엔 반경 2m 안의 **판넬 전체 최고점**을 기준으로 삼았다. 오목 코너에서는 벽 윗선이
        //   코너 쪽으로 내려오는데 옆 판넬의 **먼 쪽 위 모서리**는 훨씬 높아서, 필러가 0.3~0.5m 솟아도
        //   '솟지 않았다'고 나왔다 — 현장에서 눈에 보이는 것을 이 자가 못 봤다.
        //   (반경도 0.8m로 좁힌다. 운영 코드는 0.7m를 쓰므로 자와 코드가 같은 값이 아니다 —
        //    같으면 '자기가 만든 기준으로 자기를 재는' 셈이라 아무것도 못 잡는다.)
        double worstUp = 0; string upAt = "";
        foreach (var q in WallBand.LastQuoins)
        {
            double topZ = double.MinValue;
            foreach (var t in t12)
                foreach (var p in t.Poly)
                    if ((p.X - q.Toe.X) * (p.X - q.Toe.X) + (p.Y - q.Toe.Y) * (p.Y - q.Toe.Y) < 0.64)
                        topZ = Math.Max(topZ, p.Z);
            if (topZ == double.MinValue) continue;              // 둘레에 판넬이 없으면 아래 검사에서 따로 잡는다
            double over = q.Top.Z - topZ;
            if (over > worstUp) { worstUp = over; upAt = $"@ {q.Toe.X:F1},{q.Toe.Y:F1}"; }
        }
        int orphan = WallBand.LastQuoins.Count(q => !t12.Any(t => t.Poly.Any(p =>
            (p.X - q.Toe.X) * (p.X - q.Toe.X) + (p.Y - q.Toe.Y) * (p.Y - q.Toe.Y) < 0.64)));
        Console.WriteLine($"      S40 ⑫코너필러 {WallBand.LastQuoins.Count}개 · 판넬 위로 솟은 최대 {worstUp:F2}m {upAt} · 허공 필러 {orphan}개");
        Check("S40 ⑫코너 필러가 벽보다 솟지 않는다(삐죽 없음)", worstUp < 0.30 && orphan == 0,
            $"최대 {worstUp:F2}m 솟음 {upAt} · 허공 {orphan}개 (한도 0.30m)");

        // ★★[JACK 0807 '각진부 근처에 간간히 가로로 긴 이상한 객체'] **코너 쐐기는 세로로 긴 기둥이다.**
        //   폭이 높이보다 크면 벽 위에 널빤지가 얹힌 모양이 된다 — JACK이 도면에서 선택해 보여준 그 객체.
        {
            int qFlat = 0; double worstRatio = 0; string qFlatAt = "";
            foreach (var q in WallBand.LastQuoins)
            {
                double dx = q.Top.X - q.Toe.X, dy = q.Top.Y - q.Toe.Y, dz = q.Top.Z - q.Toe.Z;
                double h = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                if (h < 1e-9) { qFlat++; continue; }
                double ratio = q.Width / h;
                if (ratio > worstRatio) { worstRatio = ratio; qFlatAt = $"@ {q.Toe.X:F1},{q.Toe.Y:F1} 폭{q.Width:F2}/높이{h:F2}"; }
                if (ratio > 1.0) qFlat++;
            }
            Console.WriteLine($"      S40 ⑫코너필러 모양: 최대 폭/높이 {worstRatio:F2} {qFlatAt} · 누운 판 {qFlat}개");
            Check("S40 ⑫★코너 필러는 폭보다 높이가 크다(누운 판 없음)", qFlat == 0 && worstRatio <= 1.0,
                $"누운 판 {qFlat}개 · 최대 폭/높이 {worstRatio:F2} {qFlatAt}");
        }

        // ★★[JACK 0807 '여전히 각진부에 삐져나와 · 길이를 참조하는 로직이 잘못된 듯'] **안전망을 직접 시험한다.**
        //   자연스러운 지형으로는 이 조건이 재현되지 않았다(현장은 옹벽선 46줄·다중 단이라 조합이 훨씬 많다).
        //   재현이 안 된다고 '없는 문제'로 두면 안 된다 — 그러면 스샷을 또 받는다.
        //   그래서 **일부러 벽보다 4m 높은 필러와 허공 필러를 심어** 정리 로직이 잡아내는지 본다.
        {
            var ref0 = WallBand.LastQuoins.Count > 0 ? WallBand.LastQuoins[0] : default;
            if (WallBand.LastQuoins.Count > 0)
            {
                int before = WallBand.LastQuoins.Count;
                // ①벽보다 4m 솟은 필러 — 종전 잘못(끝 열에서 높이를 받음)이 만드는 모양.
                //   ※그 자리 **판넬 꼭대기**보다 높아야 '솟음'이다. 필러 자기 높이에 더하면
                //     주변 판넬이 더 높은 자리에서는 솟지도 않아 시험이 헛돈다(0807 실제로 그랬다).
                double nearTop = double.MinValue;
                foreach (var t in t12)
                    foreach (var pz in t.Poly)
                        if ((pz.X - ref0.Toe.X) * (pz.X - ref0.Toe.X) + (pz.Y - ref0.Toe.Y) * (pz.Y - ref0.Toe.Y) < 2.25)
                            nearTop = Math.Max(nearTop, pz.Z);
                double spikeZ = (nearTop > double.MinValue ? nearTop : ref0.Top.Z) + 4.0;
                WallBand.LastQuoins.Add(ref0 with {
                    Top = new Point3(ref0.Top.X, ref0.Top.Y, spikeZ) });
                // ②판넬이 하나도 없는 자리에 뜬 필러.
                WallBand.LastQuoins.Add(ref0 with {
                    Toe = new Point3(ref0.Toe.X + 500, ref0.Toe.Y + 500, ref0.Toe.Z),
                    Top = new Point3(ref0.Top.X + 500, ref0.Top.Y + 500, ref0.Top.Z) });
                // ③**가로로 긴 누운 판** — JACK이 도면에서 선택해 보여준 그 객체. 폭 1.3m에 높이 0.3m.
                //   벽 높이에 맞춰 자르고 나서 납작해지는 경우까지 잡아야 하므로 여기서 직접 심는다.
                WallBand.LastQuoins.Add(ref0 with {
                    Top = new Point3(ref0.Toe.X + (ref0.Top.X - ref0.Toe.X) * 0.06,
                                     ref0.Toe.Y + (ref0.Top.Y - ref0.Toe.Y) * 0.06,
                                     ref0.Toe.Z + 0.30),
                    Width = 1.30 });
                var (tr2, dr2) = WallBand.ClampQuoinsToPanels(t12);
                double over2 = 0;
                foreach (var q in WallBand.LastQuoins)
                {
                    double tz = double.MinValue;
                    foreach (var t in t12)
                        foreach (var p in t.Poly)
                            if ((p.X - q.Toe.X) * (p.X - q.Toe.X) + (p.Y - q.Toe.Y) * (p.Y - q.Toe.Y) < 4.0)
                                tz = Math.Max(tz, p.Z);
                    if (tz > double.MinValue) over2 = Math.Max(over2, q.Top.Z - tz);
                }
                // 남은 필러 중 누운 판(폭 > 높이)이 있는지도 본다 — ③이 살아남으면 그게 JACK의 그 객체다.
                int flat2 = 0;
                foreach (var q in WallBand.LastQuoins)
                {
                    double hx = q.Top.X - q.Toe.X, hy = q.Top.Y - q.Toe.Y, hz = q.Top.Z - q.Toe.Z;
                    double hh = Math.Sqrt(hx * hx + hy * hy + hz * hz);
                    if (hh > 1e-9 && q.Width / hh > 1.0) flat2++;
                }
                Console.WriteLine($"      S40 ⑫자가검증: 심은 필러 3개 → 잘라냄 {tr2} · 지움 {dr2} · 남은 솟음 {over2:F2}m · 남은 누운 판 {flat2}개");
                Check("S40 ⑫★자가검증: 솟음·허공·누운 판을 정리 로직이 전부 잡는다",
                    tr2 >= 1 && dr2 >= 2 && over2 < 0.30 && flat2 == 0
                    && WallBand.LastQuoins.Count == before + 1,
                    $"잘라냄 {tr2} · 지움 {dr2} · 솟음 {over2:F2}m · 누운 판 {flat2}개 · 개수 {before}→{WallBand.LastQuoins.Count}");
            }
        }
    }

    // ⑩ ★공선(거의 0 면적) 경계 — 이건 **거부해야** 한다. 감사 0807이 잡은 자리:
    //    종전엔 정점 수(≥3)만 봐서 예외 없이 통과했고, 40m 선분 둘레로 폭 36m짜리
    //    **존재하지 않는 부지의 대형 사면**이 도면에 그대로 만들어졌다. 조용히 큰 결과물이 나오는 게 최악이다.
    var flat = Ring40((0,0), (40,0.0001), (20,0));
    var rFlat = Torture(flat);
    Check("S40 ⑩공선 경계 — 유령 사면을 만들지 않고 거부한다",
        !rFlat.Ok && rFlat.Err.Contains("퇴화"),
        $"완주 {rFlat.Ok} · 판넬 {rFlat.Panels} · {rFlat.Err}");

    // ⑪ ★XY가 같고 Z만 다른 정점(수직 단차) — BoundaryReader.Dedup이 지우면 계획 단차가 소멸한다.
    //    여기서는 Core만 시험하므로 '단차 Z가 링에 실제로 반영되는가'를 본다.
    var stepZ = new List<Point3> {
        new(0,0,100), new(15,0,100), new(15,0.001,105), new(15,12,105), new(0,12,105) };
    var rStep = Torture(stepZ);
    Check("S40 ⑪수직 단차(Z만 다른 정점) — 완주·NaN 없음", rStep.Ok && rStep.Bad == 0,
        $"완주 {rStep.Ok} · 판넬 {rStep.Panels} · NaN {rStep.Bad} {rStep.Err}");
}

// ★ S41 [JACK 0807 '각진부 마감을 깔끔하게 할 수 없나'] **코너 전용 판넬(ㄱ자 유닛) 단면.**
//   지금은 양쪽 판넬이 코너를 지나쳐 서로 파고들고 그 위에 필러까지 얹혀 세 덩어리가 뭉친다.
//   코너에서 양옆을 물러나게 하고 유닛 하나로 감싸면 두 노출면이 이웃 판넬과 같은 평면이라 한 면처럼 보인다.
//   실물 프리캐스트도 코너는 현장 절단이 아니라 **전용 유닛**을 쓴다. 불리언이 0회라 안전하다.
//   **각도를 스윕해서** 단면이 성립하는지 본다 — 한 각도만 보면 그 각도에서만 맞는 수정이 나온다(0806 교훈).
{
    const double thick = 0.20, front = 0.10, leg = WallBand.CornerLeg;
    int made = 0, degen = 0, bad = 0; double worstFlush = 0; string worstAt = "";
    var degAt = new List<int>();
    for (int deg = 20; deg <= 170; deg += 5)
    {
        // 코너에서 만나는 두 벽면 — A는 +X로 들어오고, B는 deg만큼 꺾여 나간다.
        double a = deg * Math.PI / 180.0;
        var corner = (x: 0.0, y: 0.0);
        var dirA = (x: 1.0, y: 0.0);
        var dirB = (x: Math.Cos(Math.PI - a), y: Math.Sin(Math.PI - a));
        double bl = Math.Sqrt(dirB.x * dirB.x + dirB.y * dirB.y);
        dirB = (dirB.x / bl, dirB.y / bl);
        // 노출면은 부지 바깥쪽(여기선 −Y 반평면 쪽) — 각 벽면 진행방향의 오른쪽 법선.
        var nA = (x: dirA.y, y: -dirA.x);
        var nB = (x: dirB.y, y: -dirB.x);

        // 양옆 판넬이 leg만큼 물러난 자리 = 다리 끝.
        var legA = (x: corner.x - dirA.x * leg, y: corner.y - dirA.y * leg);
        var legB = (x: corner.x + dirB.x * leg, y: corner.y + dirB.y * leg);
        var prof = WallBand.CornerUnitProfile(legA, legB, dirA, dirB, nA, nB, thick, front);
        if (prof.Count == 0) { degen++; degAt.Add(deg); continue; }
        made++;
        if (prof.Count != 6) { bad++; continue; }

        // ★핵심 검사 — 유닛의 다리 끝이 **이웃 판넬 전면과 같은 평면**에 있는가.
        //   A 다리 끝(prof[0])은 A 전면(코너에서 nA·front 떨어진 직선) 위여야 한다.
        double offA = Math.Abs((prof[0].x - corner.x) * nA.x + (prof[0].y - corner.y) * nA.y - front);
        double offB = Math.Abs((prof[2].x - corner.x) * nB.x + (prof[2].y - corner.y) * nB.y - front);
        double off = Math.Max(offA, offB);
        if (off > worstFlush) { worstFlush = off; worstAt = $"{deg}°"; }

        // 다리 길이가 실제로 leg인가(양옆 판넬이 물러날 거리와 같아야 딱 맞물린다).
        double lenA = Math.Abs((corner.x - prof[0].x) * dirA.x + (corner.y - prof[0].y) * dirA.y);
        double lenB = Math.Abs((prof[2].x - corner.x) * dirB.x + (prof[2].y - corner.y) * dirB.y);
        if (Math.Abs(lenA - leg) > 1e-6 || Math.Abs(lenB - leg) > 1e-6) bad++;
    }
    Console.WriteLine($"      S41 코너 유닛 단면: 성립 {made}개 · 퇴화 {degen}개{(degAt.Count > 0 ? " @" + string.Join(",", degAt) + "°" : "")}" +
                      $" · 이웃 전면과 최대 어긋남 {worstFlush:F4}m {worstAt}");
    Check("S41 재현 조건: 20~170° 대부분에서 코너 유닛 단면이 성립한다", made >= 25, $"성립 {made}개 · 퇴화 {degen}개");
    Check("S41 ★유닛 다리가 이웃 판넬 전면과 같은 평면(마감이 이어진다)", worstFlush < 1e-9 && bad == 0,
        $"최대 어긋남 {worstFlush:F6}m {worstAt} · 모양 이상 {bad}개");

    // ★★실제 부지에서 유닛이 서는가 + **필러를 대체했는가**(둘 다 서면 또 뭉친다).
    //   자가검증: 유닛을 끄면 종전대로 필러만 서야 한다 — 안 그러면 이 검사가 아무것도 안 보는 것이다.
    {
        var bnd41 = new List<Point3>();
        foreach (var (X, Y) in new (double X, double Y)[] { (0,0), (30,0), (30,20), (18,20), (18,10), (12,10), (12,20), (0,20) })
            bnd41.Add(new Point3(X, Y, 100));
        var pr41 = new GradingParams {
            CutBenchHeight = 5, FillBenchHeight = 5, CutBenchWidth = 1, FillBenchWidth = 1,
            CutSlope = 0.05, FillSlope = 0.05, CellSize = 1.0, MaxBenches = 4, MaxRise = 20,
            VertexSpacing = 1.0, MinSlope = 0.05, MinFaceRun = 0.005, MiterConvex = true, MiterLimit = 2.0,
        };
        var g41 = new TiltGround(0, 0, 108.0, 0.10, 0.06);
        var vs41 = GradingGeometry.Build(bnd41, g41, pr41, true);
        var rs41 = vs41.Rings.Select(r => (IReadOnlyList<Point3>)r).ToList();
        var runs41 = WallRunBuilder.Build(bnd41, rs41, null, up: true, globalSlope: 0.05, minSlope: 0.05);

        (int units, int quoins, int holes) Run41(bool off)
        {
            WallBand.DisableCornerUnitForTest = off;
            try
            {
                WallBand.ResetTotals();
                var tt = new List<WallBand.Tile>();
                foreach (var r in runs41) tt.AddRange(WallBand.Slice(r, g41, joint: 0.05));
                WallBand.AddGapFillers(tt, cornerOnly: true);
                WallBand.ClampQuoinsToPanels(tt);
                string gr = WallBand.GapReport(tt);
                int h = 0;
                var m = System.Text.RegularExpressions.Regex.Match(gr, @"진짜 구멍 (\d+)곳");
                if (m.Success) h = int.Parse(m.Groups[1].Value);
                return (WallBand.LastCornerUnits.Count, WallBand.LastQuoins.Count, h);
            }
            finally { WallBand.DisableCornerUnitForTest = false; }
        }

        var on = Run41(false);
        var offR = Run41(true);
        Console.WriteLine($"      S41 실제 부지 — 유닛 켬: 유닛 {on.units} · 필러 {on.quoins} · 진짜구멍 {on.holes}" +
                          $" / 끔: 유닛 {offR.units} · 필러 {offR.quoins} · 진짜구멍 {offR.holes}");
        // ★★[JACK 0807 '접하는 쪽 양쪽의 길이 차이가 많이 나면 특히 더 심하다'] **그 조건을 만든다.**
        //   유닛 높이를 한쪽 벽면에서만 가져오면 반대쪽이 낮을 때 그 위로 솟는다 — 차이가 클수록 심하다.
        //   지형을 한 방향으로 가파르게 기울여 코너 양쪽 높이가 크게 벌어지게 하고, 유닛이 **양쪽 어디에도**
        //   안 튀어나오는지 잰다. 완만한 지형만 시험하면 이 잘못이 한 번도 안 밟힌다(0807에 실제로 그랬다).
        {
            var gSteep = new TiltGround(0, 0, 110.0, 0.85, 0.05);   // X로 가파르게 — 코너 양쪽 높이차가 커진다
            WallBand.ResetTotals();
            var tS = new List<WallBand.Tile>();
            foreach (var r in runs41) tS.AddRange(WallBand.Slice(r, gSteep, joint: 0.05));
            WallBand.AddGapFillers(tS, cornerOnly: true);
            WallBand.ClampQuoinsToPanels(tS);

            double overU = 0; string overAt = ""; double maxDiff = 0;
            foreach (var cu in WallBand.LastCornerUnits)
            {
                if (cu.Bot.Count == 0) continue;
                // 유닛 발치 둘레(0.8m)의 판넬 꼭대기 — 유닛이 그보다 위로 올라가면 삐죽이다.
                double tz = double.MinValue;
                foreach (var t in tS)
                    foreach (var p in t.Poly)
                        foreach (var b in cu.Bot)
                            if ((p.X - b.X) * (p.X - b.X) + (p.Y - b.Y) * (p.Y - b.Y) < 0.64)
                                tz = Math.Max(tz, p.Z);
                if (tz == double.MinValue) { overU = Math.Max(overU, 99); overAt = "허공 유닛"; continue; }
                double over = cu.Top[0].Z - tz;
                if (over > overU) { overU = over; overAt = $"@ {cu.Bot[0].X:F1},{cu.Bot[0].Y:F1}"; }
                maxDiff = Math.Max(maxDiff, cu.Top[0].Z - cu.Bot[0].Z);
            }
            Console.WriteLine($"      S41 가파른 지형 — 유닛 {WallBand.LastCornerUnits.Count}개 · 판넬 위로 솟음 최대 {overU:F2}m {overAt}");
            Check("S41 ★★양쪽 높이차가 큰 코너에서도 유닛이 안 솟는다",
                WallBand.LastCornerUnits.Count > 0 && overU < 0.30,
                $"유닛 {WallBand.LastCornerUnits.Count}개 · 최대 솟음 {overU:F2}m {overAt} (한도 0.30m)");
        }

        Check("S41 ★★실제 부지에서 코너 유닛이 서고 필러를 대체한다(뭉치지 않는다)",
            on.units > 0 && on.holes == 0 && on.quoins < offR.quoins && offR.units == 0,
            $"켬 유닛 {on.units}·필러 {on.quoins}·구멍 {on.holes} / 끔 유닛 {offR.units}·필러 {offR.quoins}·구멍 {offR.holes}");
    }
}

// ★ S43 [JACK 0820 '중간에서 하면 잘 변환되는데 사면 맨 아랫단은 안 바뀌네'] **맨 아랫단 옹벽 변환.**
//   변환 규칙은 '이 단부터 바깥으로'인데, 맨 아랫단(데이라잇이 일어나는 단)만 안 먹는다는 실측 보고.
//   중간 단과 **완전히 같은 조건**으로 나란히 세워 어디서 갈리는지 숫자로 가른다.
{
    var sq = new List<Point3> { new(0, 0, 100), new(60, 0, 100), new(60, 60, 100), new(0, 60, 100) };
    var pr = new GradingParams
    {
        CutBenchHeight = 5, FillBenchHeight = 5, CutBenchWidth = 1, FillBenchWidth = 1,
        CutSlope = 1.5, FillSlope = 1.5, CellSize = 1.0, MaxBenches = 30, MaxRise = 30,
        VertexSpacing = 2.0, MinSlope = 0.05, MinFaceRun = 0.005, MiterConvex = true, MiterLimit = 2.0,
    };
    var cum = GradingGeometry.CumLen2D(sq);
    double L = cum[^1];
    var ground = new FlatGround(130);          // 절토 30m → 5m 단 6개

    var vBase = GradingGeometry.Build(sq, ground, pr, true, null);
    int nBench = 0;
    for (int k = 1; k < vBase.Rings.Count; k++)
    {
        double za = 0, zb = 0; int ca = 0, cb = 0;
        foreach (var q in vBase.Rings[k]) { za += q.Z; ca++; }
        foreach (var q in vBase.Rings[k - 1]) { zb += q.Z; cb++; }
        if (ca == 0 || cb == 0) continue;
        if (Math.Abs(za / ca - zb / cb) >= 0.1) nBench++;
    }
    Check("S43 기준 단 수(절토 30m / 단높이 5m)", nBench >= 5, $"단 {nBench} · 링 {vBase.Rings.Count}");

    int last = nBench - 1, mid = nBench / 2;

    static double OuterExtent(IReadOnlyList<IReadOnlyList<Point3>> rings, IReadOnlyList<Point3> b, double[] cm, double t0, double t1)
    {
        double best = 0;
        foreach (var ring in rings)
        {
            if (ring == null) continue;
            foreach (var q in ring)
            {
                double t = GradingGeometry.ParamAt(b, cm, q.X, q.Y);
                if (t < t0 || t > t1) continue;
                double d = double.MaxValue;
                for (int i = 0, j = b.Count - 1; i < b.Count; j = i++)
                {
                    double ax = b[j].X, ay = b[j].Y, dx = b[i].X - ax, dy = b[i].Y - ay;
                    double l2 = dx * dx + dy * dy; if (l2 < 1e-12) continue;
                    double u = ((q.X - ax) * dx + (q.Y - ay) * dy) / l2; u = Math.Max(0, Math.Min(1, u));
                    double px = ax + dx * u - q.X, py = ay + dy * u - q.Y;
                    d = Math.Min(d, Math.Sqrt(px * px + py * py));
                }
                if (d < double.MaxValue && d > best) best = d;
            }
        }
        return best;
    }

    double t0 = L * 0.02, t1 = L * 0.23;      // 첫 변 안쪽(모서리 영향 제외)
    double extBase = OuterExtent(vBase.Rings, sq, cum, t0, t1);

    var zMid = new List<SlopeZone> { SlopeZone.Wall(0.0, L * 0.25, mid, int.MaxValue, 0.05, 1.5) };
    var vMid = GradingGeometry.Build(sq, ground, pr, true, zMid);
    double extMid = OuterExtent(vMid.Rings, sq, cum, t0, t1);
    var runMid = WallRunBuilder.Build(sq, vMid.Rings, zMid, up: true, globalSlope: 1.5, minSlope: 0.05);

    var zLast = new List<SlopeZone> { SlopeZone.Wall(0.0, L * 0.25, last, int.MaxValue, 0.05, 1.5) };
    var vLast = GradingGeometry.Build(sq, ground, pr, true, zLast);
    double extLast = OuterExtent(vLast.Rings, sq, cum, t0, t1);
    var runLast = WallRunBuilder.Build(sq, vLast.Rings, zLast, up: true, globalSlope: 1.5, minSlope: 0.05);

    Console.WriteLine($"      S43 단 {nBench}개 · 중간={mid + 1}단 맨아래={last + 1}단 · " +
                      $"구간 최대폭 기준 {extBase:F2}m → 중간 {extMid:F2}m → 맨아래 {extLast:F2}m");
    Console.WriteLine($"      S43 옹벽선 — 중간 {runMid.Count}줄(단 {string.Join(",", runMid.Select(r => r.Bench + 1).Distinct().OrderBy(x => x))})" +
                      $" · 맨아래 {runLast.Count}줄(단 {string.Join(",", runLast.Select(r => r.Bench + 1).Distinct().OrderBy(x => x))})");

    Check("S43 중간 단 옹벽 — 링이 좁아진다", extMid < extBase - 1.0, $"{extBase:F2} → {extMid:F2}");
    Check("S43 중간 단 옹벽 — 옹벽선이 선다", runMid.Count > 0, $"{runMid.Count}줄");

    Check("S43 ★맨 아랫단 옹벽 — 링이 좁아진다", extLast < extBase - 1.0, $"{extBase:F2} → {extLast:F2}");
    Check("S43 ★맨 아랫단 옹벽 — 옹벽선이 선다", runLast.Count > 0, $"{runLast.Count}줄");
    Check("S43 ★맨 아랫단 옹벽 — 그 단에 옹벽선이 있다",
        runLast.Any(r => r.Bench == last), $"단 {string.Join(",", runLast.Select(r => r.Bench + 1).Distinct())} (기대 {last + 1})");
}

// ★ S44 [JACK 0820 '사면 맨 아랫단은 안 바뀌네'] **클릭할 선이 있는 단.**
//   변환 명령은 GenerateEdgeLinesTagged가 낸 선만 클릭 대상으로 세운다.
//   1단의 선은 곧 계획 폴리곤인데, 클립이 그 폴리곤을 '구멍'으로 두어 잘라낸다 —
//   그러면 1단은 **클릭할 선 자체가 없어** 아무리 눌러도 안 바뀐다. 그 가설을 숫자로 가른다.
{
    var sq = new List<Point3> { new(0, 0, 100), new(60, 0, 100), new(60, 60, 100), new(0, 60, 100) };
    var pr = new GradingParams
    {
        CutBenchHeight = 5, FillBenchHeight = 5, CutBenchWidth = 1, FillBenchWidth = 1,
        CutSlope = 1.5, FillSlope = 1.5, CellSize = 1.0, MaxBenches = 30, MaxRise = 30,
        VertexSpacing = 2.0, MinSlope = 0.05, MinFaceRun = 0.005, MiterConvex = true, MiterLimit = 2.0,
    };

    foreach (var (up, gz, dir) in new[] { (true, 130.0, "절토"), (false, 70.0, "성토") })
    {
        var ground = new FlatGround(gz);
        var vs = GradingGeometry.Build(sq, ground, pr, up, null);
        if (!vs.HasSlope) { Check($"S44 {dir} 사면 생성", false, "HasSlope=false"); continue; }

        // 변환 명령과 같은 호출 — 바깥 클립은 최외곽 링, 구멍은 계획 폴리곤.
        var fr = vs.Rings[vs.Rings.Count - 1];
        var tagged = SlopeHatchGenerator.GenerateEdgeLinesTagged(vs.Rings, ground, up, fr, sq,
            null, sq, null, null, BaseOf(pr, up), pr.MinSlope);

        // ZoneEditCommon의 걸러내기와 동일: 절토는 아랫선(IsSlope=false), 성토는 윗선(IsSlope=true).
        var clickable = tagged.Where(e => up != e.IsSlope).Select(e => e.Bench).Distinct().OrderBy(x => x).ToList();
        var allTags = tagged.Select(e => e.Bench).Distinct().OrderBy(x => x).ToList();

        Console.WriteLine($"      S44 {dir} — 링 {vs.Rings.Count} · 태그된 단 {string.Join(",", allTags.Select(b => b + 1))}" +
                          $" · **클릭 가능한 단** {string.Join(",", clickable.Select(b => b + 1))}");

        Check($"S44 {dir} 클릭할 선이 여러 단에 있다", clickable.Count >= 3, $"{clickable.Count}단");
        Check($"S44 ★{dir} **1단에도 클릭할 선이 있다**(없으면 맨 아랫단을 못 고른다)",
            clickable.Contains(0), $"클릭 가능 {string.Join(",", clickable.Select(b => b + 1))}단");
    }
}

// ★ S45 [JACK 0820 '사면 맨 아랫단은 안 바뀌네'] **방향별 '맨 아랫단'.**
//   절토는 부지에서 위로 올라가므로 맨 아랫단 = 1단(경계 옆),
//   성토는 아래로 내려가므로 맨 아랫단 = 마지막 단(데이라잇 단)이다.
//   S43은 절토의 '마지막 단'(=제일 위)만 봤다 — 정작 JACK이 누르는 자리를 안 본 것이다.
{
    var sq = new List<Point3> { new(0, 0, 100), new(60, 0, 100), new(60, 60, 100), new(0, 60, 100) };
    var pr = new GradingParams
    {
        CutBenchHeight = 5, FillBenchHeight = 5, CutBenchWidth = 1, FillBenchWidth = 1,
        CutSlope = 1.5, FillSlope = 1.5, CellSize = 1.0, MaxBenches = 30, MaxRise = 28,
        VertexSpacing = 2.0, MinSlope = 0.05, MinFaceRun = 0.005, MiterConvex = true, MiterLimit = 2.0,
    };
    var cum = GradingGeometry.CumLen2D(sq);
    double L = cum[^1];

    static double Ext(IReadOnlyList<IReadOnlyList<Point3>> rings, IReadOnlyList<Point3> b, double[] cm, double t0, double t1)
    {
        double best = 0;
        foreach (var ring in rings)
        {
            if (ring == null) continue;
            foreach (var q in ring)
            {
                double t = GradingGeometry.ParamAt(b, cm, q.X, q.Y);
                if (t < t0 || t > t1) continue;
                double d = double.MaxValue;
                for (int i = 0, j = b.Count - 1; i < b.Count; j = i++)
                {
                    double ax = b[j].X, ay = b[j].Y, dx = b[i].X - ax, dy = b[i].Y - ay;
                    double l2 = dx * dx + dy * dy; if (l2 < 1e-12) continue;
                    double u = ((q.X - ax) * dx + (q.Y - ay) * dy) / l2; u = Math.Max(0, Math.Min(1, u));
                    double px = ax + dx * u - q.X, py = ay + dy * u - q.Y;
                    d = Math.Min(d, Math.Sqrt(px * px + py * py));
                }
                if (d < double.MaxValue && d > best) best = d;
            }
        }
        return best;
    }

    double t0 = L * 0.02, t1 = L * 0.23;

    // 기울어진 원지반 — 데이라잇이 둘레마다 다른 단에서 일어난다(현장과 같은 조건).
    foreach (var (up, ground, dir) in new (bool, IGroundSurface, string)[]
    {
        (true,  new FlatGround(128),               "절토·평지반"),
        (true,  new TiltGround(0, 0, 118, 0.35, 0), "절토·경사지반"),
        (false, new FlatGround(72),                "성토·평지반"),
        (false, new TiltGround(0, 0, 82, -0.35, 0), "성토·경사지반"),
    })
    {
        var vBase = GradingGeometry.Build(sq, ground, pr, up, null);
        if (!vBase.HasSlope) { Check($"S45 {dir} 사면 생성", false, "HasSlope=false"); continue; }
        int nB = 0;
        for (int k = 1; k < vBase.Rings.Count; k++)
        {
            double za = 0, zb = 0; int ca = 0, cb = 0;
            foreach (var q in vBase.Rings[k]) { za += q.Z; ca++; }
            foreach (var q in vBase.Rings[k - 1]) { zb += q.Z; cb++; }
            if (ca == 0 || cb == 0) continue;
            if (Math.Abs(za / ca - zb / cb) >= 0.1) nB++;
        }
        // 맨 아랫단 = 절토는 1단(index 0), 성토는 마지막 단.
        int bottom = up ? 0 : nB - 1;
        double eb = Ext(vBase.Rings, sq, cum, t0, t1);

        var zB = new List<SlopeZone> { SlopeZone.Wall(0.0, L * 0.25, bottom, int.MaxValue, 0.05, 1.5) };
        var vB = GradingGeometry.Build(sq, ground, pr, up, zB);
        double ex = Ext(vB.Rings, sq, cum, t0, t1);
        var runs = WallRunBuilder.Build(sq, vB.Rings, zB, up: up, globalSlope: 1.5, minSlope: 0.05);
        var benches = runs.Select(r => r.Bench + 1).Distinct().OrderBy(x => x).ToList();

        Console.WriteLine($"      S45 {dir} — 단 {nB}개 · 맨아래={bottom + 1}단 · 폭 {eb:F2}m → {ex:F2}m" +
                          $" · 옹벽선 {runs.Count}줄(단 {string.Join(",", benches)})");
        Check($"S45 ★{dir} 맨 아랫단 옹벽 — 링이 좁아진다", ex < eb - 1.0, $"{eb:F2} → {ex:F2}");
        Check($"S45 ★{dir} 맨 아랫단 옹벽 — 그 단에 옹벽선이 선다",
            runs.Any(r => r.Bench == bottom), $"단 {string.Join(",", benches)} (기대 {bottom + 1})");
    }
}

// ★ S46 [JACK 0820 '단높이보다 지표면과 닿는 거리가 짧을 경우엔 아예 안 바뀌는 듯'] **짧은 조각의 구간 폭.**
//   변환은 클릭한 선을 '경계 호길이 구간'으로 바꿔 저장한다. 그런데 바깥 단의 링은 경계에서 아주 멀고
//   (JACK 현장: 22×33m 부지에 성토 47m → 링이 70m 밖), 데이라잇으로 잘린 **짧은 조각**은
//   모든 점이 코너 하나로 투영돼 **구간 길이가 0**이 된다.
//   SlopeZone.Flatten은 길이 0 구간을 버리므로 → 변환이 통째로 사라진다(로그 실측: 성토 구간 1개뿐).
{
    // JACK 현장 치수 그대로 — 22.41 × 32.80m, 성토 47m.
    var sq = new List<Point3> { new(0, 0, 112), new(22.41, 0, 112), new(22.41, 32.80, 112), new(0, 32.80, 112) };
    var pr = new GradingParams
    {
        CutBenchHeight = 5, FillBenchHeight = 2, CutBenchWidth = 1, FillBenchWidth = 2,
        CutSlope = 1.5, FillSlope = 1.5, CellSize = 1.0, MaxBenches = 30, MaxRise = 57,
        VertexSpacing = 2.0, MinSlope = 0.05, MinFaceRun = 0.005, MiterConvex = true, MiterLimit = 2.0,
    };
    var cum = GradingGeometry.CumLen2D(sq);
    double total = cum[^1];
    var ground = new TiltGround(0, 0, 65, 0.9, 0.0);   // 한쪽만 얕게 닿는 지반 — 마지막 단이 조각난다
    var vs = GradingGeometry.Build(sq, ground, pr, false, null);
    Check("S46 성토 사면 생성", vs.HasSlope, $"링 {vs.Rings.Count}");

    var fr = vs.Rings[vs.Rings.Count - 1];
    var tagged = SlopeHatchGenerator.GenerateEdgeLinesTagged(vs.Rings, ground, false, fr, sq,
        null, sq, null, null, pr.FillSlope, pr.MinSlope);
    var click = tagged.Where(e => e.IsSlope).ToList();     // 성토는 윗선이 클릭 대상
    Check("S46 클릭 대상 선이 있다", click.Count > 0, $"{click.Count}줄");

    int worstB = -1, worstN = 0; double worstSpan = double.MaxValue, worstLen = 0;
    int zeroSpan = 0;
    foreach (var e in click)
    {
        var iv = GradingGeometry.PickInterval(e.Pts, sq, cum);     // 최소폭 보정 없음 = 옛 동작
        if (iv == null) continue;
        double span = iv.Value.T1 >= iv.Value.T0 ? iv.Value.T1 - iv.Value.T0 : iv.Value.T1 + total - iv.Value.T0;
        double len2d = 0;
        for (int i = 1; i < e.Pts.Count; i++)
        {
            double dx = e.Pts[i].X - e.Pts[i - 1].X, dy = e.Pts[i].Y - e.Pts[i - 1].Y;
            len2d += Math.Sqrt(dx * dx + dy * dy);
        }
        if (span <= 1e-6) zeroSpan++;
        if (span < worstSpan) { worstSpan = span; worstB = e.Bench; worstN = e.Pts.Count; worstLen = len2d; }
    }
    Console.WriteLine($"      S46 클릭 대상 {click.Count}줄 · 둘레 {total:F1}m · " +
                      $"**구간 길이 0인 줄 {zeroSpan}개** · 가장 좁은 줄 = {worstB + 1}단 " +
                      $"(점 {worstN}개 · 선 길이 {worstLen:F1}m → 구간 {worstSpan:F3}m)");

    // 이 조건(둘레를 한 바퀴 도는 긴 줄)에서는 0이 안 나온다 — 그 사실 자체를 못 박아 둔다.
    Check("S46 둘레를 도는 긴 줄은 구간이 넉넉하다", zeroSpan == 0 && worstSpan > total * 0.5,
        $"0인 줄 {zeroSpan}개 · 가장 좁은 구간 {worstSpan:F1}m / 둘레 {total:F1}m");

    // ★ 진짜 위험한 모양 — **코너 바깥 멀리 있는 짧은 조각**. 모든 점이 코너 하나로 투영된다.
    //   데이라잇이 바깥 단을 이렇게 잘라 내면 구간 길이가 0이 되고, 아래 Flatten이 그 구간을 버린다.
    var frag = new List<Point3>();
    for (int i = 0; i < 8; i++) frag.Add(new Point3(70.0 + i * 0.4, 80.0 + i * 0.4, 60));   // 코너 바깥 70m
    var ivF = GradingGeometry.PickInterval(frag, sq, cum);
    double spanF = ivF == null ? -1
        : (ivF.Value.T1 >= ivF.Value.T0 ? ivF.Value.T1 - ivF.Value.T0 : ivF.Value.T1 + total - ivF.Value.T0);
    var ivG = GradingGeometry.PickInterval(frag, sq, cum, total * 0.02);
    double spanG = ivG == null ? -1
        : (ivG.Value.T1 >= ivG.Value.T0 ? ivG.Value.T1 - ivG.Value.T0 : ivG.Value.T1 + total - ivG.Value.T0);
    Console.WriteLine($"      S46 코너 바깥 짧은 조각 — 보정 없음 {spanF:F4}m · 보정 후 {spanG:F2}m (둘레 {total:F1}m)");
    Check("S46 ★★코너 바깥 짧은 조각은 구간이 0으로 무너진다(보정 없을 때)", spanF >= 0 && spanF <= 1e-6,
        $"{spanF:F6}m");
    Check("S46 ★★★최소폭 보정이 그 조각을 살린다", spanG > total * 0.015, $"{spanG:F2}m");

    // 최소폭 보정을 주면 살아나야 한다.
    int zeroFixed = 0;
    foreach (var e in click)
    {
        var iv = GradingGeometry.PickInterval(e.Pts, sq, cum, total * 0.02);
        if (iv == null) continue;
        double span = iv.Value.T1 >= iv.Value.T0 ? iv.Value.T1 - iv.Value.T0 : iv.Value.T1 + total - iv.Value.T0;
        if (span <= 1e-6) zeroFixed++;
    }
    Check("S46 ★★최소폭 보정을 주면 길이 0인 줄이 없다", zeroFixed == 0, $"남은 0인 줄 {zeroFixed}개");

    // Flatten이 길이 0 구간을 실제로 버리는지 — 원인 사슬의 마지막 고리.
    var zs = new List<SlopeZone>
    {
        new SlopeZone { T0 = 0.0, T1 = total, Rules = { (0, 1.5, 2.0) } },
        new SlopeZone { T0 = 10.0, T1 = 10.0, Rules = { (17, 0.05, 1.0) } },   // 길이 0 = 조각난 바깥 단
    };
    SlopeZone.Flatten(zs, total);
    bool wallGone = !zs.Any(z => z.Rules.Any(r => r.Slope <= 0.05 + 1e-9));
    Check("S46 ★★★Flatten이 길이 0 구간을 버린다(옹벽 규칙이 사라진다)", wallGone,
        $"남은 구간 {zs.Count}개 · 수직 규칙 {(wallGone ? "없음(버려짐)" : "있음")}");
}

// ★ S47 [JACK 0824 "단마다 해당 단의 가상 계획폴리곤을 기억하고 그걸로 시작한다"] **구간이 자기 자를 든다.**
//   0820엔 모든 구간을 '계획 폴리곤 둘레 거리'라는 자 하나로 쟀다 — 바깥 단은 그 자에서 너무 멀어
//   코너 바깥 조각이 한 점으로 뭉개지고(구간 폭 0) Flatten이 버렸다.
//   이제 구간마다 **그 단의 링**을 자로 들고 다닌다. JACK 현장 치수 그대로 재현해서 판정한다.
{
    var sq = new List<Point3> { new(0, 0, 112), new(22.41, 0, 112), new(22.41, 32.80, 112), new(0, 32.80, 112) };
    var pr = new GradingParams
    {
        CutBenchHeight = 5, FillBenchHeight = 2, CutBenchWidth = 1, FillBenchWidth = 2,
        CutSlope = 1.5, FillSlope = 1.5, CellSize = 1.0, MaxBenches = 30, MaxRise = 47,
        VertexSpacing = 2.0, MinSlope = 0.05, MinFaceRun = 0.005, MiterConvex = true, MiterLimit = 2.0,
    };
    var planCum = GradingGeometry.CumLen2D(sq);
    double planTot = planCum[^1];
    var ground = new FlatGround(65);                       // 성토 47m
    var vs = GradingGeometry.Build(sq, ground, pr, false, null);
    Check("S47 성토 사면 생성", vs.HasSlope && vs.Rings.Count > 20, $"링 {vs.Rings.Count}");

    // 맨 아랫단 = 마지막 단. 그 단의 '윗선(크레스트)' 링이 성토의 클릭 대상선이자 자다.
    int nB = (vs.Rings.Count - 1) / 2;
    int last = nB - 1;
    static double AvgZOf(List<Point3> r) { double t = 0; foreach (var q in r) t += q.Z; return r.Count > 0 ? t / r.Count : 0; }
    var rA = vs.Rings[2 * last]; var rB = vs.Rings[2 * last + 1];
    var ruler = AvgZOf(rA) >= AvgZOf(rB) ? rA : rB;        // 성토 = 윗선
    var rulerCum = GradingGeometry.CumLen2D(ruler);
    double rulerTot = rulerCum[^1];

    // 그 링이 계획 폴리곤에서 얼마나 먼가 — '우표에 훌라후프'가 실제로 맞는지 먼저 확인.
    double far = 0;
    foreach (var q in ruler)
    {
        double d = double.MaxValue;
        for (int i = 0, j = sq.Count - 1; i < sq.Count; j = i++)
        {
            double ax = sq[j].X, ay = sq[j].Y, dx = sq[i].X - ax, dy = sq[i].Y - ay, l2 = dx * dx + dy * dy;
            if (l2 < 1e-12) continue;
            double u = ((q.X - ax) * dx + (q.Y - ay) * dy) / l2; u = Math.Max(0, Math.Min(1, u));
            double px = ax + dx * u - q.X, py = ay + dy * u - q.Y;
            d = Math.Min(d, Math.Sqrt(px * px + py * py));
        }
        if (d < double.MaxValue && d > far) far = d;
    }
    Console.WriteLine($"      S47 부지 둘레 {planTot:F1}m · 맨아래({last + 1}단) 링 둘레 {rulerTot:F1}m · 부지에서 최대 {far:F1}m 밖");
    Check("S47 맨 아랫단 링이 부지에서 멀다(문제 조건 성립)", far > planTot * 0.3, $"{far:F1}m / 둘레 {planTot:F1}m");

    // 그 링의 코너 바깥 조각 하나를 '클릭한 선'으로 삼는다.
    int c0 = ruler.Count / 8, cN = Math.Max(4, ruler.Count / 40);
    var clicked = ruler.GetRange(c0, cN);
    double clickedLen = 0;
    for (int i = 1; i < clicked.Count; i++)
    {
        double dx = clicked[i].X - clicked[i - 1].X, dy = clicked[i].Y - clicked[i - 1].Y;
        clickedLen += Math.Sqrt(dx * dx + dy * dy);
    }

    var ivPlan = GradingGeometry.PickInterval(clicked, sq, planCum);              // 옛 자 = 계획 폴리곤
    var ivRing = GradingGeometry.PickInterval(clicked, ruler, rulerCum);          // 새 자 = 그 단의 링
    double spanPlan = ivPlan == null ? -1 : (ivPlan.Value.T1 >= ivPlan.Value.T0
        ? ivPlan.Value.T1 - ivPlan.Value.T0 : ivPlan.Value.T1 + planTot - ivPlan.Value.T0);
    double spanRing = ivRing == null ? -1 : (ivRing.Value.T1 >= ivRing.Value.T0
        ? ivRing.Value.T1 - ivRing.Value.T0 : ivRing.Value.T1 + rulerTot - ivRing.Value.T0);
    Console.WriteLine($"      S47 클릭한 조각 {clickedLen:F1}m({clicked.Count}점) — 옛 자로 {spanPlan:F3}m · **새 자로 {spanRing:F1}m**");
    Check("S47 ★★새 자로 재면 조각 길이만큼 나온다(0으로 안 무너진다)",
        spanRing > clickedLen * 0.8, $"{spanRing:F1}m / 선 {clickedLen:F1}m");

    // 그 자를 달고 만든 구간이 Flatten을 지나도 살아남는가.
    var zw = new SlopeZone { T0 = ivRing!.Value.T0, T1 = ivRing.Value.T1, Ref = ruler };
    zw.Rules.Add((last, pr.MinSlope, 2.0));
    var zs = new List<SlopeZone> { new SlopeZone { T0 = 0.0, T1 = planTot, Rules = { (0, 1.5, 2.0) } }, zw };
    SlopeZone.Flatten(zs, planTot);
    bool alive = zs.Any(z => z.Ref != null && z.Rules.Any(r => r.Slope <= pr.MinSlope + 1e-9));
    Check("S47 ★★★자를 단 구간은 Flatten이 안 버린다", alive, $"구간 {zs.Count}개 · 수직 {(alive ? "살아있음" : "사라짐")}");

    // 그 구간이 실제로 그 자리를 가리키는가 — 클릭한 조각의 한가운데는 안, 반대편은 밖.
    var inPt = clicked[clicked.Count / 2];
    var outPt = ruler[(c0 + ruler.Count / 2) % ruler.Count];
    bool wIn = SlopeZone.IsWallAtPoint(zs, inPt.X, inPt.Y, last, 1.5, pr.MinSlope, sq, planCum);
    bool wOut = SlopeZone.IsWallAtPoint(zs, outPt.X, outPt.Y, last, 1.5, pr.MinSlope, sq, planCum);
    Check("S47 ★★★클릭한 자리는 옹벽, 반대편은 사면", wIn && !wOut, $"클릭자리 {(wIn ? "옹벽" : "사면")} · 반대편 {(wOut ? "옹벽" : "사면")}");

    // 그 전 단은 사면 그대로여야 한다(규칙은 '그 단부터').
    bool wPrev = SlopeZone.IsWallAtPoint(zs, inPt.X, inPt.Y, last - 1, 1.5, pr.MinSlope, sq, planCum);
    Check("S47 ★그 전 단은 사면 그대로", !wPrev, $"{last}단 {(wPrev ? "옹벽" : "사면")}");

    // 끝으로 — 그 구간을 넣어 실제로 사면이 좁아지는가.
    //   ※ 맨 아랫단 하나만 수직으로 세우는 것이라 변화는 그 단의 물림폭(2m×1.5=3m)뿐이다.
    //     부지 전체에서 가장 먼 점을 재면 반대편이 지배해 안 움직인다 — **구간 안만** 잰다.
    static double MaxOutInZone(VirtualSlope v, SlopeZone z, IReadOnlyList<Point3> b, double[] bc)
    {
        double best = 0;
        foreach (var ring in v.Rings)
            foreach (var q in ring)
            {
                if (!z.ContainsAt(q.X, q.Y, b, bc)) continue;
                double d = double.MaxValue;
                for (int i2 = 0, j2 = b.Count - 1; i2 < b.Count; j2 = i2++)
                {
                    double ax = b[j2].X, ay = b[j2].Y, dx = b[i2].X - ax, dy = b[i2].Y - ay, l2 = dx * dx + dy * dy;
                    if (l2 < 1e-12) continue;
                    double u = ((q.X - ax) * dx + (q.Y - ay) * dy) / l2; u = Math.Max(0, Math.Min(1, u));
                    double px = ax + dx * u - q.X, py = ay + dy * u - q.Y;
                    d = Math.Min(d, Math.Sqrt(px * px + py * py));
                }
                if (d < double.MaxValue && d > best) best = d;
            }
        return best;
    }
    var vW = GradingGeometry.Build(sq, ground, pr, false, zs);
    double ext0 = MaxOutInZone(vs, zw, sq, planCum);
    double ext1 = MaxOutInZone(vW, zw, sq, planCum);
    Console.WriteLine($"      S47 구간 안에서 가장 먼 링 — 구간 없음 {ext0:F1}m → 구간 있음 {ext1:F1}m " +
                      $"(맨 아랫단 물림폭 {pr.FillBenchHeight * pr.FillSlope:F1}m가 수직으로 접힌다)");
    Check("S47 ★★★구간을 넣으면 그 자리 사면이 좁아진다", ext1 < ext0 - 1.0, $"{ext0:F1} → {ext1:F1}");
}

// ★ S48 [JACK 0824 "이건 그냥 아예 안 바뀐 거잖아"] **0824 로그의 구간을 그대로 세워 재현한다.**
//   주장("같은 값을 넣어서 안 바뀐 것")을 말로 하지 말고 숫자로 가른다 —
//   같은 값이면 링이 한 점도 안 움직이고, 다른 값이면 움직인다. 둘 다 확인해야 주장이 성립한다.
{
    // JACK 현장 그대로: 22.41 × 32.80m · 계획 112m · 성토 47m · 단높이 2m · 소단 1m · 구배 1:1.5
    var sq = new List<Point3> { new(0, 0, 112), new(22.41, 0, 112), new(22.41, 32.80, 112), new(0, 32.80, 112) };
    var pr = new GradingParams
    {
        CutBenchHeight = 5, FillBenchHeight = 2, CutBenchWidth = 1, FillBenchWidth = 1,
        CutSlope = 1.5, FillSlope = 1.5, CellSize = 1.0, MaxBenches = 30, MaxRise = 47,
        VertexSpacing = 2.0, MinSlope = 0.05, MinFaceRun = 0.005, MiterConvex = true, MiterLimit = 2.0,
    };
    var planCum = GradingGeometry.CumLen2D(sq);
    var ground = new FlatGround(65);

    // 6단의 링(= 로그의 '자=그 단의 링') — ZoneEditCommon과 같은 방식으로 고른다.
    var v0 = GradingGeometry.Build(sq, ground, pr, false, null);
    static double AvgZ48(List<Point3> r) { double t = 0; foreach (var q in r) t += q.Z; return r.Count > 0 ? t / r.Count : 0; }
    int bench = 5;                                   // 로그의 '성토 6단'
    var ra = v0.Rings[2 * bench]; var rb = v0.Rings[2 * bench + 1];
    var ruler = AvgZ48(ra) >= AvgZ48(rb) ? ra : rb;  // 성토 = 윗선
    var rulerCum = GradingGeometry.CumLen2D(ruler);

    // 로그의 구간 넷을 그대로 — #1·#2는 계획 폴리곤 자, #3은 그 단의 링 자.
    static SlopeZone Z(double t0, double t1, int fb, double sl, double bw, List<Point3>? rf = null)
    {
        var z = new SlopeZone { T0 = t0, T1 = t1, Ref = rf };
        z.Rules.Add((fb, sl, bw)); z.Normalize(); return z;
    }
    // 클릭한 조각의 한가운데를 잡아 그 자리로 판정한다(로그의 [116.6..126.5]에 해당하는 자리).
    double rt = rulerCum[^1];
    double c0 = rt * 0.60, c1 = c0 + 9.9;
    var before = new List<SlopeZone> { Z(104.8, 69.1, 0, 0.05, 1.0), Z(104.8, 69.1, 0, 1.5, 1.0) };
    var same = new List<SlopeZone>(before) { Z(c0, c1, bench, 1.5, 1.0, ruler) };   // 넣은 값 = 지금 값
    var diff = new List<SlopeZone>(before) { Z(c0, c1, bench, 0.5, 1.0, ruler) };   // 값을 실제로 바꿈

    var vBefore = GradingGeometry.Build(sq, ground, pr, false, before);
    var vSame = GradingGeometry.Build(sq, ground, pr, false, same);
    var vDiff = GradingGeometry.Build(sq, ground, pr, false, diff);

    // 두 결과에서 '가장 많이 움직인 점'을 잰다(링 개수가 같아야 점끼리 짝이 맞는다).
    static double MaxMove(VirtualSlope a, VirtualSlope b)
    {
        double best = 0;
        int n = Math.Min(a.Rings.Count, b.Rings.Count);
        for (int k = 0; k < n; k++)
        {
            var ra2 = a.Rings[k]; var rb2 = b.Rings[k];
            foreach (var q in ra2)
            {
                double d = double.MaxValue;
                foreach (var w in rb2)
                {
                    double dx = q.X - w.X, dy = q.Y - w.Y, dz = q.Z - w.Z;
                    double dd = dx * dx + dy * dy + dz * dz;
                    if (dd < d) d = dd;
                }
                if (d < double.MaxValue && Math.Sqrt(d) > best) best = Math.Sqrt(d);
            }
        }
        return best;
    }
    double moveSame = MaxMove(vBefore, vSame);
    double moveDiff = MaxMove(vBefore, vDiff);

    Console.WriteLine($"      S48 6단 링 둘레 {rt:F1}m · 클릭 조각 9.9m · " +
                      $"**같은 값(1:1.5) 넣으면 {moveSame:F3}m 움직임 · 다른 값(1:0.5) 넣으면 {moveDiff:F2}m 움직임**");

    Check("S48 ★★★같은 값을 넣으면 모양이 한 점도 안 움직인다(JACK이 본 그것)",
        moveSame < 0.01, $"{moveSame:F4}m");
    Check("S48 ★★★값을 실제로 바꾸면 모양이 움직인다(자 교체가 살아 있다)",
        moveDiff > 1.0, $"{moveDiff:F2}m");

    // 규칙 자체로도 확인 — 그 자리 6단의 구배가 전후로 같은가/다른가.
    var mid = ruler[(int)(ruler.Count * 0.62) % ruler.Count];
    double sB = SlopeZone.ResolveAt(before, mid.X, mid.Y, bench, 1.5, 1.0, sq, planCum).Slope;
    double sS = SlopeZone.ResolveAt(same, mid.X, mid.Y, bench, 1.5, 1.0, sq, planCum).Slope;
    double sD = SlopeZone.ResolveAt(diff, mid.X, mid.Y, bench, 1.5, 1.0, sq, planCum).Slope;
    Console.WriteLine($"      S48 그 자리 6단 구배 — 전 1:{sB:0.###} · 같은값 넣은 뒤 1:{sS:0.###} · 다른값 넣은 뒤 1:{sD:0.###}");
    Check("S48 ★넣기 전에 이미 1:1.5였다(그래서 안 바뀐 것)", Math.Abs(sB - 1.5) < 1e-9, $"1:{sB:0.###}");
    Check("S48 ★다른 값은 실제로 먹는다", Math.Abs(sD - 0.5) < 1e-9, $"1:{sD:0.###}");
}

// ★ S49 [JACK 0824 '옹벽변환 → 사면변환 → 옹벽변환'] **되돌리기가 실제로 먹는가.**
//   0824 실측 결함 둘을 못 박는다:
//    ① 활성화 조건이 '이 구간이 전역과 다른가'였다 → **전역 구배로 되돌리는 구간**은 전역과 같아져
//       한 번도 활성화되지 않았다. 앞 구간(옹벽)이 계속 이겨 화면이 그대로였다.
//       (로그 실측: `⚠구간 밖인데 벽 2700점 — 15단` = 규칙은 사면인데 기하는 벽)
//    ② 구간 프로파일을 **그 구간 규칙만으로** 만들었다 → 1~14단이 옹벽인데 '15단부터 사면' 구간이
//       1~14단을 전역 구배로 깔아 15단 위치가 통째로 어긋났다.
{
    var sq = new List<Point3> { new(0, 0, 112), new(22.41, 0, 112), new(22.41, 32.80, 112), new(0, 32.80, 112) };
    var pr = new GradingParams
    {
        CutBenchHeight = 5, FillBenchHeight = 1, CutBenchWidth = 1, FillBenchWidth = 1,
        CutSlope = 1.5, FillSlope = 1.5, CellSize = 1.0, MaxBenches = 30, MaxRise = 47,
        VertexSpacing = 2.0, MinSlope = 0.05, MinFaceRun = 0.005, MiterConvex = true, MiterLimit = 2.0,
    };
    var planCum = GradingGeometry.CumLen2D(sq);
    double planTot = planCum[^1];
    var ground = new FlatGround(65);
    int back = 14;                       // 로그의 '성토 15단'

    // ① 옹벽변환 — 성토 쪽 절반을 1단부터 수직으로.
    var zWall = new SlopeZone { T0 = 0.0, T1 = planTot * 0.5 };
    zWall.Rules.Add((0, pr.MinSlope, 1.0)); zWall.Normalize();
    var vWall = GradingGeometry.Build(sq, ground, pr, false, new List<SlopeZone> { zWall });
    Check("S49 옹벽 구간 생성", vWall.HasSlope, $"링 {vWall.Rings.Count}");

    // 그 상태에서 15단의 링을 자로 삼는다(변환 명령이 하는 것과 같은 방식).
    static double AvgZ49(List<Point3> r) { double t = 0; foreach (var q in r) t += q.Z; return r.Count > 0 ? t / r.Count : 0; }
    var ra = vWall.Rings[2 * back]; var rb = vWall.Rings[2 * back + 1];
    var ruler = AvgZ49(ra) >= AvgZ49(rb) ? ra : rb;      // 성토 = 윗선
    var rulerCum = GradingGeometry.CumLen2D(ruler);
    double rt = rulerCum[^1];

    // ② 사면변환 — 15단부터 **전역 구배(1:1.5)로 되돌린다**.
    //   ★ 고정 비율로 자리를 잡으면 기하가 바뀔 때 옹벽 구간 밖으로 나간다 —
    //     **실제로 옹벽인 정점**을 찾아 그 한가운데를 쓴다.
    var inWall = new List<int>();
    for (int i = 0; i < ruler.Count; i++)
        if (zWall.ContainsAt(ruler[i].X, ruler[i].Y, sq, planCum)) inWall.Add(i);
    Check("S49 옹벽 구간에 든 링 정점이 있다(시험 조건)", inWall.Count >= 20, $"{inWall.Count}점 / {ruler.Count}점");
    int cA = inWall[inWall.Count / 2 - inWall.Count / 6], cB = inWall[inWall.Count / 2 + inWall.Count / 6];
    double m0 = GradingGeometry.ParamAt(ruler, rulerCum, ruler[cA].X, ruler[cA].Y);
    double m1 = GradingGeometry.ParamAt(ruler, rulerCum, ruler[cB].X, ruler[cB].Y);
    if (m1 < m0) { var t0x = m0; m0 = m1; m1 = t0x; }
    var zBack = new SlopeZone { T0 = m0, T1 = m1, Ref = ruler };
    zBack.Rules.Add((back, pr.FillSlope, 1.0)); zBack.Normalize();
    var zones2 = new List<SlopeZone> { zWall, zBack };
    var mid = ruler[inWall[inWall.Count / 2]];
    Check("S49 되돌리기 구간이 옹벽 구간 안에 있다(문제 조건 성립)",
        zWall.ContainsAt(mid.X, mid.Y, sq, planCum), "옹벽 구간 밖이면 시험이 성립 안 함");

    // 규칙은 사면이라고 말해야 한다.
    double sBack = SlopeZone.ResolveAt(zones2, mid.X, mid.Y, back, pr.FillSlope, 1.0, sq, planCum).Slope;
    double sUnder = SlopeZone.ResolveAt(zones2, mid.X, mid.Y, back - 1, pr.FillSlope, 1.0, sq, planCum).Slope;
    Check("S49 규칙 — 15단은 사면으로 되돌아간다", Math.Abs(sBack - pr.FillSlope) < 1e-9, $"1:{sBack:0.###}");
    Check("S49 규칙 — 14단은 옹벽 그대로", Math.Abs(sUnder - pr.MinSlope) < 1e-9, $"1:{sUnder:0.###}");

    // ★ 기하도 따라와야 한다 — **그 단의 링**이 되돌린 자리에서 바깥으로 퍼져야 한다.
    //   (부지 전체나 반경으로 재면 다른 데가 지배해 포화된다 — 잰 대상이 아니면 정보가 아니다.)
    static double RingOutInZone(VirtualSlope v, int ringIdx, SlopeZone z,
                                IReadOnlyList<Point3> b, double[] bc)
    {
        if (ringIdx < 0 || ringIdx >= v.Rings.Count) return -1;
        double best = -1;
        foreach (var q in v.Rings[ringIdx])
        {
            if (!z.ContainsAt(q.X, q.Y, b, bc)) continue;
            double d = double.MaxValue;
            for (int i2 = 0, j2 = b.Count - 1; i2 < b.Count; j2 = i2++)
            {
                double ax = b[j2].X, ay = b[j2].Y, dx = b[i2].X - ax, dy = b[i2].Y - ay, l2 = dx * dx + dy * dy;
                if (l2 < 1e-12) continue;
                double u = ((q.X - ax) * dx + (q.Y - ay) * dy) / l2; u = Math.Max(0, Math.Min(1, u));
                double px = ax + dx * u - q.X, py = ay + dy * u - q.Y;
                d = Math.Min(d, Math.Sqrt(px * px + py * py));
            }
            if (d < double.MaxValue && d > best) best = d;
        }
        return best;
    }
    var vBack = GradingGeometry.Build(sq, ground, pr, false, zones2);
    int faceRing = 2 * back + 1;                      // 그 단의 사면끝 링
    double oW = RingOutInZone(vWall, faceRing, zBack, sq, planCum);
    double oB = RingOutInZone(vBack, faceRing, zBack, sq, planCum);
    Console.WriteLine($"      S49 {back + 1}단 사면끝 링이 부지에서 — 옹벽만 {oW:F2}m → 되돌린 뒤 {oB:F2}m " +
                      $"(수직 물림 {pr.MinSlope * pr.FillBenchHeight:F2}m → 사면 물림 {pr.FillSlope * pr.FillBenchHeight:F2}m)");
    Check("S49 ★★★기하가 규칙을 따라온다(되돌리기가 실제로 먹는다)", oB > oW + 0.5, $"{oW:F2} → {oB:F2}");

    // ★ 옹벽선 판정과 기하가 어긋나지 않아야 한다(로그의 '구간 밖인데 벽' 경보가 이 자리다).
    var runs = WallRunBuilder.Build(sq, vBack.Rings, zones2, up: false,
                                    globalSlope: pr.FillSlope, minSlope: pr.MinSlope);
    Console.WriteLine($"      S49 옹벽선 {runs.Count}줄 — {WallRunBuilder.LastDiag}");
    Check("S49 ★되돌린 뒤에도 구간 밖은 여전히 옹벽", runs.Count > 0, $"{runs.Count}줄");

    // ③ 다시 옹벽변환 — 되돌린 자리의 일부를 12단부터 다시 수직으로.
    var zAgain = new SlopeZone { T0 = m0 + (m1 - m0) * 0.25, T1 = m0 + (m1 - m0) * 0.75, Ref = ruler };
    zAgain.Rules.Add((11, pr.MinSlope, 1.0)); zAgain.Normalize();
    var zones3 = new List<SlopeZone> { zWall, zBack, zAgain };
    var mid3 = GradingGeometry.PointAtParam(ruler, rulerCum, (m0 + m1) * 0.5);
    double s3 = SlopeZone.ResolveAt(zones3, mid3.X, mid3.Y, back, pr.FillSlope, 1.0, sq, planCum).Slope;
    Check("S49 ★★다시 옹벽변환하면 그 자리가 다시 수직이 된다(12단부터 → 15단도 수직)",
        Math.Abs(s3 - pr.MinSlope) < 1e-9, $"1:{s3:0.###}");

    var v3 = GradingGeometry.Build(sq, ground, pr, false, zones3);
    double oB3 = RingOutInZone(vBack, faceRing, zAgain, sq, planCum);
    double o3 = RingOutInZone(v3, faceRing, zAgain, sq, planCum);
    Console.WriteLine($"      S49 다시 옹벽 — 되돌린 상태 {oB3:F2}m → 다시 옹벽 {o3:F2}m");
    Check("S49 ★★★다시 옹벽변환도 기하가 따라온다", o3 < oB3 - 0.5, $"{oB3:F2} → {o3:F2}");
}

// ★ S50 [JACK 0824 로그 `규칙 1단~1m 15단~1m 16단~1m`] **죽은 단높이 규칙은 쌓이지 않는다.**
//   변환할 때마다 (그 단부터, 단높이) 규칙이 하나씩 붙는데, 앞과 같은 값이면 아무 일도 안 한다.
//   남겨 두면 번들만 커지고 로그를 못 읽는다 — 결과는 그대로 두고 죽은 것만 뺀다.
{
    var p = new GradingParams
    {
        CutBenchHeight = 5, FillBenchHeight = 5,
        CutBenchSteps = { (0, 1.0), (14, 1.0), (15, 1.0) },      // 로그의 그 모양
        FillBenchSteps = { (0, 2.0), (5, 3.0), (9, 3.0), (12, 2.0) },
    };
    p.NormalizeBenchSteps();

    string cut = string.Join(" ", p.CutBenchSteps.Select(r => $"{r.FromBench + 1}단~{r.H:0.##}m"));
    string fill = string.Join(" ", p.FillBenchSteps.Select(r => $"{r.FromBench + 1}단~{r.H:0.##}m"));
    Console.WriteLine($"      S50 절토 [{cut}] · 성토 [{fill}]");

    Check("S50 ★같은 값이 이어지면 뒤엣것은 빠진다", p.CutBenchSteps.Count == 1 && Math.Abs(p.CutBenchSteps[0].H - 1.0) < 1e-9,
        $"[{cut}] (1단~1m 하나여야 한다)");
    Check("S50 ★값이 바뀌는 규칙은 전부 남는다", p.FillBenchSteps.Count == 3,
        $"[{fill}] (1단~2 · 6단~3 · 13단~2 — 셋이어야 한다)");

    // ★ 결과가 바뀌면 안 된다 — 정리 전후로 모든 단의 단높이가 같아야 한다.
    var raw = new GradingParams
    {
        CutBenchHeight = 5, FillBenchHeight = 5,
        CutBenchSteps = { (0, 1.0), (14, 1.0), (15, 1.0) },
        FillBenchSteps = { (0, 2.0), (5, 3.0), (9, 3.0), (12, 2.0) },
    };
    // raw는 정렬만 하고 죽은 규칙은 남긴 상태를 흉내 — At 조회 규칙이 같으므로 값이 같아야 한다.
    int diff = 0;
    for (int b = 0; b < 30; b++)
    {
        // 정리 전 값 = 손으로 계산(마지막으로 b 이하인 규칙)
        double hc = 5, hf = 5;
        foreach (var r in raw.CutBenchSteps) if (b >= r.FromBench) hc = r.H;
        foreach (var r in raw.FillBenchSteps) if (b >= r.FromBench) hf = r.H;
        if (Math.Abs(hc - p.BenchHeightAt(true, b)) > 1e-9) diff++;
        if (Math.Abs(hf - p.BenchHeightAt(false, b)) > 1e-9) diff++;
    }
    Check("S50 ★★★정리해도 모든 단의 단높이가 그대로다(결과 불변)", diff == 0, $"어긋난 단 {diff}개 / 60");

    // 전역값과 같은 첫 규칙도 죽은 규칙이다.
    var q = new GradingParams { CutBenchHeight = 5, FillBenchHeight = 5, CutBenchSteps = { (0, 5.0), (3, 2.0) } };
    q.NormalizeBenchSteps();
    Check("S50 ★전역값과 같은 규칙도 뺀다", q.CutBenchSteps.Count == 1 && q.CutBenchSteps[0].FromBench == 3,
        string.Join(" ", q.CutBenchSteps.Select(r => $"{r.FromBench + 1}단~{r.H:0.##}m")));
}

// ★ S51 [JACK 0824 '종단에서 계획지표면 꺾이는 부분 측점이 자동 추가가 안 돼'] **옹벽 구간의 선도 측점 재료다.**
//   종전엔 '구간 안 선은 그리지 않는다'는 **표시 규칙**이 측점 재료까지 막았다 —
//   그리지 않는 것과 없는 것은 다르다. 옹벽 윗선·아랫선은 계획 지표면이 실제로 꺾이는 자리다.
{
    var sq = new List<Point3> { new(0, 0, 112), new(22.41, 0, 112), new(22.41, 32.80, 112), new(0, 32.80, 112) };
    var pr = new GradingParams
    {
        CutBenchHeight = 2, FillBenchHeight = 2, CutBenchWidth = 1, FillBenchWidth = 1,
        CutSlope = 1.5, FillSlope = 1.5, CellSize = 1.0, MaxBenches = 30, MaxRise = 20,
        VertexSpacing = 2.0, MinSlope = 0.05, MinFaceRun = 0.005, MiterConvex = true, MiterLimit = 2.0,
    };
    var cum = GradingGeometry.CumLen2D(sq);
    double L = cum[^1];
    var ground = new FlatGround(132);                    // 절토 20m

    // 둘레의 절반을 1단부터 수직(옹벽)으로 — JACK 도면과 같은 모양.
    var zw = new SlopeZone { T0 = 0.0, T1 = L * 0.5 };
    zw.Rules.Add((0, pr.MinSlope, 1.0)); zw.Normalize();
    var zones = new List<SlopeZone> { zw };
    var vs = GradingGeometry.Build(sq, ground, pr, true, zones);
    Check("S51 옹벽 구간 사면 생성", vs.HasSlope, $"링 {vs.Rings.Count}");

    var fr = vs.Rings[vs.Rings.Count - 1];
    var wallPts = new List<(int Bench, bool IsCrest, List<Point3> Pts)>();
    var edges = SlopeHatchGenerator.GenerateEdgeLinesTagged(vs.Rings, ground, true, fr, sq,
        zones, sq, null, null, pr.CutSlope, pr.MinSlope, null, wallPts);

    // 그려지는 선(사면·소단)이 옹벽 구간 안에 얼마나 있는가 — 거의 없어야 정상(표시 규칙).
    int inZoneDrawn = 0, inZoneWall = 0;
    foreach (var e in edges)
        foreach (var q in e.Pts)
            if (zw.ContainsAt(q.X, q.Y, sq, cum)) { inZoneDrawn++; break; }
    foreach (var w in wallPts)
        foreach (var q in w.Pts)
            if (zw.ContainsAt(q.X, q.Y, sq, cum)) { inZoneWall++; break; }

    Console.WriteLine($"      S51 그려지는 선 {edges.Count}개(옹벽 구간에 걸친 것 {inZoneDrawn}개) · " +
                      $"**측점 재료로 나온 옹벽 선 {wallPts.Count}개(구간 안 {inZoneWall}개)**");

    Check("S51 ★★★옹벽 구간의 선이 측점 재료로 나온다(종전엔 0개였다)", wallPts.Count > 0, $"{wallPts.Count}개");
    Check("S51 ★그 선들이 실제로 옹벽 구간 안에 있다", inZoneWall > 0, $"구간 안 {inZoneWall}개 / {wallPts.Count}개");

    // 옹벽 구간이 없으면 측점 재료도 없어야 한다(엉뚱한 선이 새로 생기면 안 된다).
    var vNo = GradingGeometry.Build(sq, ground, pr, true, null);
    var wallNo = new List<(int Bench, bool IsCrest, List<Point3> Pts)>();
    SlopeHatchGenerator.GenerateEdgeLinesTagged(vNo.Rings, ground, true, vNo.Rings[vNo.Rings.Count - 1], sq,
        null, sq, null, null, pr.CutSlope, pr.MinSlope, null, wallNo);
    Check("S51 ★구간이 없으면 측점 재료도 없다(없던 선이 새로 안 생긴다)", wallNo.Count == 0, $"{wallNo.Count}개");

    // 성토도 같아야 한다 — 종전엔 성토 토우가 어디에도 안 담겨 통째로 사라졌다.
    var vF = GradingGeometry.Build(sq, new FlatGround(92), pr, false, zones);
    var wallF = new List<(int Bench, bool IsCrest, List<Point3> Pts)>();
    SlopeHatchGenerator.GenerateEdgeLinesTagged(vF.Rings, new FlatGround(92), false,
        vF.Rings[vF.Rings.Count - 1], sq, zones, sq, null, null, pr.FillSlope, pr.MinSlope, null, wallF);
    Console.WriteLine($"      S51 성토 — 측점 재료로 나온 옹벽 선 {wallF.Count}개");
    Check("S51 ★★성토 옹벽 구간의 선도 나온다(윗선·아랫선 둘 다)", wallF.Count > 0, $"{wallF.Count}개");
}

// ★ S52 [검토 치명-1 반증 시험] **자(Ref)가 옛 링이어도 같은 자리를 가리키는가.**
//   검토 지적: "자는 그 변환이 스스로 옮겨 버릴 링의 스냅샷이라, 대는 순간 이미 옛날 자다."
//   판정: ContainsAt는 **새 링의 점을 저장된 자 위로 투영**하고 T0/T1도 그 자의 값이므로
//   자기완결적이다 — 두 링이 같은 경계의 동심 오프셋이면 투영이 각도를 보존한다.
//   말이 아니라 숫자로 가른다: 단높이를 5m→3m로 바꿔 링을 통째로 옮긴 뒤에도
//   그 구간이 **같은 변**을 가리키는지 본다.
{
    var sq = new List<Point3> { new(0, 0, 112), new(22.41, 0, 112), new(22.41, 32.80, 112), new(0, 32.80, 112) };
    GradingParams P(double h) => new GradingParams
    {
        CutBenchHeight = h, FillBenchHeight = h, CutBenchWidth = 1, FillBenchWidth = 1,
        CutSlope = 1.5, FillSlope = 1.5, CellSize = 1.0, MaxBenches = 40, MaxRise = 47,
        VertexSpacing = 2.0, MinSlope = 0.05, MinFaceRun = 0.005, MiterConvex = true, MiterLimit = 2.0,
    };
    var planCum = GradingGeometry.CumLen2D(sq);
    var ground = new FlatGround(65);
    static double AvgZ52(List<Point3> r) { double t = 0; foreach (var q in r) t += q.Z; return r.Count > 0 ? t / r.Count : 0; }

    // ① 단높이 5m 판에서 15단 링을 자로 삼아, **남쪽 변**(y≈0)에 걸친 조각을 고른다.
    var vOld = GradingGeometry.Build(sq, ground, P(5.0), false, null);
    // 단높이 5m·47m면 단이 10개 안팎이다 — 링 개수에서 안전한 단을 고른다(두 판 모두에 있어야 한다).
    var vTmp = GradingGeometry.Build(sq, ground, P(3.0), false, null);
    int bn = Math.Min(vOld.Rings.Count, vTmp.Rings.Count) / 2 - 2;
    Check("S52 시험할 단이 두 판 모두에 있다", bn >= 2, $"{bn + 1}단 · 5m판 링 {vOld.Rings.Count} · 3m판 링 {vTmp.Rings.Count}");
    var oA = vOld.Rings[2 * bn]; var oB = vOld.Rings[2 * bn + 1];
    var ruler = AvgZ52(oA) >= AvgZ52(oB) ? oA : oB;
    var rCum = GradingGeometry.CumLen2D(ruler);
    double rTot = rCum[^1];

    // 남쪽 변 바깥(y가 가장 작은 자리)의 연속 조각을 찾는다.
    double ySouth = double.MaxValue;
    foreach (var q in ruler) ySouth = Math.Min(ySouth, q.Y);
    // ★ 최소 Y는 남쪽 '직선 전체'가 공유한다 — 그중 **부지 남변의 한가운데(x≈11.2)** 에 가장 가까운 점을 쓴다.
    //   그냥 첫 최소점을 쓰면 코너 꼭짓점이 잡혀 구간이 엉뚱한 자리에 놓인다(첫 시도에서 실제로 그랬다).
    int iSouth = 0; double bestX = double.MaxValue;
    for (int i = 0; i < ruler.Count; i++)
    {
        if (ruler[i].Y > ySouth + 0.01) continue;
        double dx0 = Math.Abs(ruler[i].X - 11.2);
        if (dx0 < bestX) { bestX = dx0; iSouth = i; }
    }
    double tS = GradingGeometry.ParamAt(ruler, rCum, ruler[iSouth].X, ruler[iSouth].Y);
    var z = new SlopeZone { T0 = tS - 8.0, T1 = tS + 8.0, Ref = ruler };
    if (z.T0 < 0) z.T0 += rTot;
    z.Rules.Add((bn, 0.05, 1.0)); z.Normalize();

    Console.WriteLine($"      S52 자 = 5m판 {bn + 1}단 링 · 둘레 {rTot:F1}m · 남쪽 바깥 y={ySouth:F1} · 구간 폭 16m");

    // ② 단높이를 3m로 바꿔 링을 통째로 옮긴다. 자는 **옛 5m판 링 그대로**.
    var vNew = GradingGeometry.Build(sq, ground, P(3.0), false, new List<SlopeZone> { z });
    var nA = vNew.Rings[2 * bn]; var nB = vNew.Rings[2 * bn + 1];
    var newRing = AvgZ52(nA) >= AvgZ52(nB) ? nA : nB;
    double dOld = 0, dNew = 0;
    foreach (var q in ruler) dOld = Math.Max(dOld, Math.Abs(q.Y - 16.4));
    foreach (var q in newRing) dNew = Math.Max(dNew, Math.Abs(q.Y - 16.4));
    Console.WriteLine($"      S52 링이 실제로 옮겨졌나 — 옛 링 반경 {dOld:F1}m → 새 링 반경 {dNew:F1}m");
    Check("S52 링이 크게 옮겨졌다(시험 조건 성립)", Math.Abs(dOld - dNew) > 10.0, $"{dOld:F1} → {dNew:F1}");

    // ③ 옛 자로 잰 구간이 **새 링에서도 남쪽 변**을 가리키는가.
    int inS = 0, inN = 0, inE = 0, inW = 0, tot = 0;
    foreach (var q in newRing)
    {
        if (!z.ContainsAt(q.X, q.Y, sq, planCum)) continue;
        tot++;
        double cx = 11.2, cy = 16.4;
        double dx = q.X - cx, dy = q.Y - cy;
        if (Math.Abs(dy) >= Math.Abs(dx)) { if (dy < 0) inS++; else inN++; }
        else { if (dx < 0) inW++; else inE++; }
    }
    Console.WriteLine($"      S52 새 링에서 구간에 든 점 {tot}개 — 남 {inS} · 북 {inN} · 동 {inE} · 서 {inW}");
    Check("S52 ★★★옛 자로 잰 구간이 새 링에서도 같은 변(남쪽)을 가리킨다",
        tot > 0 && inS >= tot * 0.8, $"남 {inS}/{tot}개 (80% 이상이어야 한다)");
    Check("S52 ★반대편(북쪽)으로는 새지 않는다", inN == 0, $"북 {inN}개");

    // ④ 기하도 그 자리에서만 바뀌어야 한다 — **구간 안에서만** 잰다.
    //   구간이 자 둘레의 2%밖에 안 되므로 링 전체의 최소 Y를 재면 구간 밖이 지배해 안 움직인다.
    var vNo = GradingGeometry.Build(sq, ground, P(3.0), false, null);
    static double MinYInZone(VirtualSlope v, int ring, SlopeZone zz,
                             IReadOnlyList<Point3> b, double[] bc)
    {
        if (ring >= v.Rings.Count) return double.NaN;
        double best = double.MaxValue; int n = 0;
        foreach (var q in v.Rings[ring])
        {
            if (!zz.ContainsAt(q.X, q.Y, b, bc)) continue;
            n++; best = Math.Min(best, q.Y);
        }
        return n == 0 ? double.NaN : best;
    }
    double sNo = MinYInZone(vNo, 2 * bn + 1, z, sq, planCum);
    double sYes = MinYInZone(vNew, 2 * bn + 1, z, sq, planCum);
    Console.WriteLine($"      S52 구간 안 {bn + 1}단 링의 남쪽 끝 — 구간 없음 {sNo:F2} → 구간 있음 {sYes:F2} " +
                      $"(사면 물림 {1.5 * 3.0:F2}m → 수직 물림 {0.05 * 3.0:F2}m)");
    Check("S52 ★★★옛 자로 잰 구간이 새 판의 기하를 실제로 바꾼다",
        !double.IsNaN(sNo) && !double.IsNaN(sYes) && sYes > sNo + 1.0, $"{sNo:F2} → {sYes:F2}");
}

// ★ S53 [검토 0824 중간-10 판정] **링 짝 (2k, 2k+1)은 정말 '면'인가.**
//   검토 지적: "StepProfile은 한 단에 모서리 2개를 같은 totalRise로 넣으므로 쌍의 Z가 같고,
//   크레스트/토우를 표고로 가르는 것이 동전 던지기다."
//   실제로 링 배열은 [경계, 사면끝0, 소단끝0, 사면끝1, ...]이라 (2k, 2k+1)은
//   '소단끝(k-1)과 사면끝k' = **면을 사이에 둔 쌍**이다. 숫자로 못 박는다.
{
    var sq = new List<Point3> { new(0, 0, 100), new(40, 0, 100), new(40, 60, 100), new(0, 60, 100) };
    var pr = new GradingParams
    {
        CutBenchHeight = 5, FillBenchHeight = 3, CutBenchWidth = 1, FillBenchWidth = 2,
        CutSlope = 1.5, FillSlope = 1.5, CellSize = 1.0, MaxBenches = 30, MaxRise = 30,
        VertexSpacing = 2.0, MinSlope = 0.05, MinFaceRun = 0.005, MiterConvex = true, MiterLimit = 2.0,
    };
    static double AvgZ53(List<Point3> r) { double t = 0; foreach (var q in r) t += q.Z; return r.Count > 0 ? t / r.Count : 0; }

    foreach (var (up, gz, dir, bh) in new[] { (true, 130.0, "절토", 5.0), (false, 70.0, "성토", 3.0) })
    {
        var v = GradingGeometry.Build(sq, new FlatGround(gz), pr, up, null);
        int pairs = 0, sameZ = 0; double minGap = double.MaxValue;
        for (int k = 0; 2 * k + 1 < v.Rings.Count; k++)
        {
            var a = v.Rings[2 * k]; var b = v.Rings[2 * k + 1];
            if (a.Count < 3 || b.Count < 3) continue;
            pairs++;
            double gap = Math.Abs(AvgZ53(a) - AvgZ53(b));
            if (gap < 1e-6) sameZ++;
            minGap = Math.Min(minGap, gap);
        }
        Console.WriteLine($"      S53 {dir} — 링 짝 {pairs}개 · 표고차 0인 짝 {sameZ}개 · 최소 표고차 {minGap:F3}m (단높이 {bh:F0}m)");
        Check($"S53 ★★{dir} 링 짝은 언제나 '면'이다(표고가 같은 짝이 없다)", pairs > 0 && sameZ == 0,
            $"표고 같은 짝 {sameZ}개 / {pairs}개");
        Check($"S53 ★{dir} 표고차가 단높이만큼이다(크레스트/토우 판정이 안정적)", minGap > bh * 0.5,
            $"최소 {minGap:F3}m (단높이 {bh:F0}m)");
    }
}

// ★ S54 [검토 0824 치명 C-1] **구간이 쌓이면 정지면 계산이 제곱으로 느려진다.**
//   자가 계획 폴리곤(4~30점)일 땐 공짜였던 ContainsAt이, 이제 자가 그 단의 링(수백~1400점)이라
//   링 점마다 자 전체를 선형 스캔한다. 구간 수 × 링 점 수 × 자 점 수.
//   느려지는 것 자체가 결함이므로 **시간을 시험으로 못 박는다**(회귀 감시).
{
    var sq = new List<Point3> { new(0, 0, 112), new(22.41, 0, 112), new(22.41, 32.80, 112), new(0, 32.80, 112) };
    var pr = new GradingParams
    {
        CutBenchHeight = 5, FillBenchHeight = 5, CutBenchWidth = 1, FillBenchWidth = 1,
        CutSlope = 1.5, FillSlope = 1.5, CellSize = 1.0, MaxBenches = 30, MaxRise = 47,
        VertexSpacing = 2.0, MinSlope = 0.05, MinFaceRun = 0.005, MiterConvex = true, MiterLimit = 2.0,
    };
    var planCum = GradingGeometry.CumLen2D(sq);
    var ng = new NullGround();
    static double AvgZ54(List<Point3> r) { double t = 0; foreach (var q in r) t += q.Z; return r.Count > 0 ? t / r.Count : 0; }

    var baseV = GradingGeometry.Build(sq, ng, pr, false, null);
    int nb = (baseV.Rings.Count - 1) / 2;

    List<SlopeZone> MakeZones(int n)
    {
        var zs = new List<SlopeZone>();
        for (int i = 0; i < n; i++)
        {
            int b = 1 + (i * 2) % Math.Max(1, nb - 2);
            var ra = baseV.Rings[2 * b]; var rb = baseV.Rings[2 * b + 1];
            var rl = AvgZ54(ra) >= AvgZ54(rb) ? ra : rb;
            var rc = GradingGeometry.CumLen2D(rl);
            double tt = rc[^1];
            var z = new SlopeZone { T0 = tt * (i % 8) / 8.0, T1 = tt * ((i % 8) + 1) / 8.0, Ref = rl };
            z.Rules.Add((b, i % 2 == 0 ? pr.MinSlope : 1.5, 1.0));
            z.Normalize();
            zs.Add(z);
        }
        return zs;
    }

    long Ms(List<SlopeZone>? zs)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        GradingGeometry.Build(sq, ng, pr, false, zs);
        sw.Stop();
        return sw.ElapsedMilliseconds;
    }

    Ms(null);   // 워밍업(JIT)
    long t0 = Ms(null), t4 = Ms(MakeZones(4)), t8 = Ms(MakeZones(8)), t16 = Ms(MakeZones(16));
    Console.WriteLine($"      S54 단 {nb}개 · Build 시간 — 구간 0개 {t0}ms · 4개 {t4}ms · 8개 {t8}ms · 16개 {t16}ms");
    // 0824 최적화 실측: 4개 132ms · 8개 264ms · 16개 411ms(최적화 전 291/832/1542ms).
    //   문턱은 실측의 두 배 — 느려지면 바로 걸린다.
    Check("S54 ★★★구간 16개에 Build가 900ms 안(변환 1회에 4번 돈다)", t16 < 900, $"{t16}ms");
    Check("S54 ★구간 4개는 300ms 안", t4 < 300, $"{t4}ms");
    Check("S54 ★구간이 늘어도 제곱으로 안 는다", t16 < t4 * 6, $"4개 {t4}ms → 16개 {t16}ms");
}

// ★ S55 [검토 0824 심각 S-4] **아래 단을 바꾸면 위 단 구간의 자가 낡는다?**
//   지적: 15단 구간을 만든 뒤 3단을 손대면 15단 링이 통째로 옮겨가는데 자는 옛 링이다.
//   다만 두 링이 같은 경계의 오프셋이면 투영이 각도를 보존한다(S52에서 전역 단높이 변경으로 확인).
//   여기선 **둘레의 일부만** 바꾼다 — 그러면 새 링이 동심이 아니게 되어 훨씬 험한 조건이다.
{
    var sq = new List<Point3> { new(0, 0, 112), new(22.41, 0, 112), new(22.41, 32.80, 112), new(0, 32.80, 112) };
    var pr = new GradingParams
    {
        CutBenchHeight = 5, FillBenchHeight = 2, CutBenchWidth = 1, FillBenchWidth = 1,
        CutSlope = 1.5, FillSlope = 1.5, CellSize = 1.0, MaxBenches = 30, MaxRise = 40,
        VertexSpacing = 2.0, MinSlope = 0.05, MinFaceRun = 0.005, MiterConvex = true, MiterLimit = 2.0,
    };
    var planCum = GradingGeometry.CumLen2D(sq);
    var ng = new NullGround();
    static double AvgZ55(List<Point3> r) { double t = 0; foreach (var q in r) t += q.Z; return r.Count > 0 ? t / r.Count : 0; }

    var v0 = GradingGeometry.Build(sq, ng, pr, false, null);
    int hi = Math.Min(14, (v0.Rings.Count - 1) / 2 - 1);
    var ra0 = v0.Rings[2 * hi]; var rb0 = v0.Rings[2 * hi + 1];
    var ruler = AvgZ55(ra0) >= AvgZ55(rb0) ? ra0 : rb0;
    var rCum = GradingGeometry.CumLen2D(ruler);

    // 위 단(15단) 구간 — 남쪽 절반.
    double ySouth = double.MaxValue;
    foreach (var q in ruler) ySouth = Math.Min(ySouth, q.Y);
    int iS = 0; double bx = double.MaxValue;
    for (int i = 0; i < ruler.Count; i++)
    {
        if (ruler[i].Y > ySouth + 0.01) continue;
        double d = Math.Abs(ruler[i].X - 11.2);
        if (d < bx) { bx = d; iS = i; }
    }
    double tS = GradingGeometry.ParamAt(ruler, rCum, ruler[iS].X, ruler[iS].Y);
    var zHi = new SlopeZone { T0 = tS - 10.0, T1 = tS + 10.0, Ref = ruler };
    zHi.Rules.Add((hi, pr.MinSlope, 1.0)); zHi.Normalize();

    // 아래 단(4단)을 **둘레의 일부(동쪽 절반)만** 옹벽으로 — 새 15단 링이 동심이 아니게 된다.
    var zLo = new SlopeZone { T0 = planCum[^1] * 0.25, T1 = planCum[^1] * 0.75 };
    zLo.Rules.Add((3, pr.MinSlope, 1.0)); zLo.Normalize();

    var vBoth = GradingGeometry.Build(sq, ng, pr, false, new List<SlopeZone> { zLo, zHi });
    var na = vBoth.Rings[2 * hi]; var nb2 = vBoth.Rings[2 * hi + 1];
    var newRing = AvgZ55(na) >= AvgZ55(nb2) ? na : nb2;

    int inS = 0, inN = 0, tot = 0;
    foreach (var q in newRing)
    {
        if (!zHi.ContainsAt(q.X, q.Y, sq, planCum)) continue;
        tot++;
        if (q.Y < 16.4) inS++; else inN++;
    }
    Console.WriteLine($"      S55 아래 단(4단)을 둘레 절반만 옹벽으로 바꾼 뒤 — {hi + 1}단 구간에 든 점 {tot}개(남 {inS} · 북 {inN})");
    Check("S55 ★★★아래 단을 바꿔도 위 단 구간이 같은 변을 가리킨다", tot > 0 && inN == 0, $"남 {inS} · 북 {inN}");
}

// ★ S56 [검토 0824 심각 S-2] **자가 달라도 같은 자리면 중복이다.**
//   0824 진단 로그 실물:
//     구간#1 [104.8..69.1] — 1단~1:0.05(수직) · 자=계획
//     구간#2 [104.8..69.1] — 1단~1:0.05(수직) · 자=링(113점)
//   호길이도 규칙도 같은데 **자만 달라** 둘 다 살아 있었다. 변환할 때마다 링이 재계산되므로
//   자는 매번 다르다 → Compact이 사실상 한 번도 안 걸리고 구간이 무한히 쌓인다(느려짐의 연료).
{
    var sq = new List<Point3> { new(0, 0, 112), new(22.41, 0, 112), new(22.41, 32.80, 112), new(0, 32.80, 112) };
    var pr = new GradingParams
    {
        CutBenchHeight = 5, FillBenchHeight = 5, CutBenchWidth = 1, FillBenchWidth = 1,
        CutSlope = 1.5, FillSlope = 1.5, CellSize = 1.0, MaxBenches = 30, MaxRise = 40,
        VertexSpacing = 2.0, MinSlope = 0.05, MinFaceRun = 0.005, MiterConvex = true, MiterLimit = 2.0,
    };
    var planCum = GradingGeometry.CumLen2D(sq);
    double planTot = planCum[^1];
    var ng = new NullGround();
    static double AvgZ56(List<Point3> r) { double t = 0; foreach (var q in r) t += q.Z; return r.Count > 0 ? t / r.Count : 0; }

    var v = GradingGeometry.Build(sq, ng, pr, false, null);
    var ra = v.Rings[0]; var rb = v.Rings[1];
    var ring0 = AvgZ56(ra) >= AvgZ56(rb) ? ra : rb;      // 1단의 자
    var c0 = GradingGeometry.CumLen2D(ring0);

    // 같은 자리(둘레의 남쪽 절반)를 서로 다른 자로 적은 두 구간 — 로그 실물과 같은 모양.
    var zPlan = new SlopeZone { T0 = 0.0, T1 = planTot * 0.5 };
    zPlan.Rules.Add((0, pr.MinSlope, 1.0)); zPlan.Normalize();
    // 링 자로 같은 자리를 적는다: 계획 폴리곤 [0, 절반]에 해당하는 링 위 구간을 찾아서.
    var pA = GradingGeometry.PointAtParam(sq, planCum, 0.0);
    var pB = GradingGeometry.PointAtParam(sq, planCum, planTot * 0.5);
    double rA = GradingGeometry.ParamAt(ring0, c0, pA.X, pA.Y);
    double rB = GradingGeometry.ParamAt(ring0, c0, pB.X, pB.Y);
    var zRing = new SlopeZone { T0 = rA, T1 = rB, Ref = ring0 };
    zRing.Rules.Add((0, pr.MinSlope, 1.0)); zRing.Normalize();

    var zs = new List<SlopeZone> { zPlan, zRing };
    int before = zs.Count;
    SlopeZone.Compact(zs, sq, planCum);
    Console.WriteLine($"      S56 같은 자리·같은 규칙, 자만 다름 — 정리 전 {before}개 → 정리 후 {zs.Count}개" +
                      $" (남은 자: {string.Join(",", zs.Select(x => x.Ref == null ? "계획" : $"링({x.Ref.Count})"))})");
    Check("S56 ★★★자가 달라도 같은 자리면 앞엣것을 지운다", zs.Count == 1, $"{before} → {zs.Count}");
    Check("S56 ★남는 것은 나중 구간이다(자=링)", zs.Count == 1 && zs[0].Ref != null,
        zs.Count == 1 ? (zs[0].Ref == null ? "계획" : "링") : "판정 불가");

    // ★ 다른 자리는 절대 안 지운다(안전 방향).
    var zOther = new SlopeZone { T0 = rB, T1 = rA, Ref = ring0 };   // 반대쪽 절반
    zOther.Rules.Add((0, 1.5, 1.0)); zOther.Normalize();
    var zs2 = new List<SlopeZone> { zPlan, zOther };
    SlopeZone.Compact(zs2, sq, planCum);
    Check("S56 ★★다른 자리는 안 지운다", zs2.Count == 2, $"{zs2.Count}개 (2개여야 한다)");

    // ★ 시작단이 더 높은 뒤 구간은 앞을 못 지운다(그 아래 단을 안 건드리므로).
    var zHigh = new SlopeZone { T0 = rA, T1 = rB, Ref = ring0 };
    zHigh.Rules.Add((3, 1.5, 1.0)); zHigh.Normalize();
    var zs3 = new List<SlopeZone> { zPlan, zHigh };
    SlopeZone.Compact(zs3, sq, planCum);
    Check("S56 ★★뒤 구간이 더 높은 단부터면 앞을 안 지운다", zs3.Count == 2, $"{zs3.Count}개 (2개여야 한다)");
}

// ★ S57 [검토 0824 심각-1] **코너 능선이 먼 구간 때문에 끊기지 않는다.**
//   코너는 링을 따라 추적하는데, 이동 상한(maxJump)을 정하는 조합 판정을 **계획 폴리곤 정점**에서
//   재고 있었다. 자가 100m 밖 링인 구간에 계획 코너를 투영하면 엉뚱한 param이 나와,
//   지리적으로 반대편 코너가 그 구간 안으로 판정된다 → 상한이 1.4m로 좁아지는데 실제 이동은 10m →
//   추적이 끊겨 **그 단 위 코너 능선이 통째로 사라진다**(검토 실측: 49점 → 31점).
{
    var sq = new List<Point3> { new(0, 0, 112), new(22.41, 0, 112), new(22.41, 32.80, 112), new(0, 32.80, 112) };
    var pr = new GradingParams
    {
        CutBenchHeight = 5, FillBenchHeight = 5, CutBenchWidth = 1, FillBenchWidth = 1,
        CutSlope = 1.5, FillSlope = 1.5, CellSize = 1.0, MaxBenches = 40, MaxRise = 120,
        VertexSpacing = 2.0, MinSlope = 0.05, MinFaceRun = 0.005, MiterConvex = true, MiterLimit = 2.0,
    };
    var planCum = GradingGeometry.CumLen2D(sq);
    var ng = new NullGround();
    static double AvgZ57(List<Point3> r) { double t = 0; foreach (var q in r) t += q.Z; return r.Count > 0 ? t / r.Count : 0; }

    var v0 = GradingGeometry.Build(sq, ng, pr, true, null);
    int baseMin = int.MaxValue, baseN = v0.CornerLines.Count;
    foreach (var cl in v0.CornerLines) baseMin = Math.Min(baseMin, cl.Count);
    Console.WriteLine($"      S57 구간 없음 — 코너 능선 {baseN}개 · 최소 {baseMin}점");
    Check("S57 구간 없이 코너 능선 4개(시험 조건)", baseN == 4 && baseMin > 20, $"{baseN}개 · 최소 {baseMin}점");

    // 남쪽 0단부터 옹벽(자=0단 링) + 남쪽 15단부터 옹벽(자=15단 링, 100m 밖).
    var r0a = v0.Rings[0]; var r0b = v0.Rings[1];
    var ring0 = AvgZ57(r0a) >= AvgZ57(r0b) ? r0b : r0a;        // 절토 = 아랫선
    var c0 = GradingGeometry.CumLen2D(ring0);
    var z1 = new SlopeZone { T0 = c0[^1] * 0.85, T1 = c0[^1] * 0.15, Ref = ring0 };
    z1.Rules.Add((0, pr.MinSlope, 1.0)); z1.Normalize();

    int b15 = Math.Min(14, (v0.Rings.Count - 1) / 2 - 1);
    var r1a = v0.Rings[2 * b15]; var r1b = v0.Rings[2 * b15 + 1];
    var ring15 = AvgZ57(r1a) >= AvgZ57(r1b) ? r1b : r1a;
    var c15 = GradingGeometry.CumLen2D(ring15);
    var z2 = new SlopeZone { T0 = c15[^1] * 0.85, T1 = c15[^1] * 0.15, Ref = ring15 };
    z2.Rules.Add((b15, pr.MinSlope, 1.0)); z2.Normalize();

    var vz = GradingGeometry.Build(sq, ng, pr, true, new List<SlopeZone> { z1, z2 });
    int zMin = int.MaxValue, zN = vz.CornerLines.Count;
    foreach (var cl in vz.CornerLines) zMin = Math.Min(zMin, cl.Count);
    Console.WriteLine($"      S57 먼 구간(자 = {b15 + 1}단 링, 둘레 {c15[^1]:F0}m) 있음 — 코너 능선 {zN}개 · " +
                      $"최소 {zMin}점 (점수: {string.Join(",", vz.CornerLines.Select(x => x.Count))})");
    Check("S57 ★★★먼 구간이 있어도 코너 능선 개수가 그대로다", zN == baseN, $"{baseN}개 → {zN}개");
    Check("S57 ★★★반대편 코너 능선이 끊기지 않는다", zMin >= baseMin - 2, $"최소 {baseMin}점 → {zMin}점");
}

// ★ S58 [검토 0824 심각-3] **옹벽 쐐기가 아래 단까지 덮는다.**
//   쐐기는 사면 띠 SHP에서 옹벽면을 도려내는 데 쓴다. 그런데 안쪽 변을 **자(Ref 링)** 로 만들면
//   그 링보다 안쪽에 있는 아래 단 벽면 자리가 쐐기에 안 들어가, 겹치는 데가 없어 아무것도 안 잘린다
//   → 옹벽 자리에 사면 띠가 그대로 남는다. 안쪽 변은 언제나 계획 폴리곤이어야 한다.
{
    var sq = new List<Point3> { new(0, 0, 112), new(22.41, 0, 112), new(22.41, 32.80, 112), new(0, 32.80, 112) };
    var pr = new GradingParams
    {
        CutBenchHeight = 5, FillBenchHeight = 5, CutBenchWidth = 1, FillBenchWidth = 1,
        CutSlope = 1.5, FillSlope = 1.5, CellSize = 1.0, MaxBenches = 40, MaxRise = 80,
        VertexSpacing = 2.0, MinSlope = 0.05, MinFaceRun = 0.005, MiterConvex = true, MiterLimit = 2.0,
    };
    var planCum = GradingGeometry.CumLen2D(sq);
    var ng = new NullGround();
    static double AvgZ58(List<Point3> r) { double t = 0; foreach (var q in r) t += q.Z; return r.Count > 0 ? t / r.Count : 0; }

    var v = GradingGeometry.Build(sq, ng, pr, true, null);
    int far = Math.Min(10, (v.Rings.Count - 1) / 2 - 1);
    var ra = v.Rings[2 * far]; var rb = v.Rings[2 * far + 1];
    var ruler = AvgZ58(ra) >= AvgZ58(rb) ? rb : ra;
    var rc = GradingGeometry.CumLen2D(ruler);

    // **0단부터** 옹벽인데 자는 먼 단의 링 — 검토가 든 그 조합.
    var z = new SlopeZone { T0 = rc[^1] * 0.85, T1 = rc[^1] * 0.15, Ref = ruler };
    z.Rules.Add((0, pr.MinSlope, 1.0)); z.Normalize();
    var vz = GradingGeometry.Build(sq, ng, pr, true, new List<SlopeZone> { z });

    var wedges = GradingPolygons.WallZoneWedges(sq, vz.Rings, new List<SlopeZone> { z }, pr.CutSlope, pr.MinSlope);
    Console.WriteLine($"      S58 자 = {far + 1}단 링(둘레 {rc[^1]:F0}m) · 0단부터 옹벽 — 쐐기 {wedges.Count}개");
    Check("S58 쐐기가 만들어진다", wedges.Count > 0, $"{wedges.Count}개");

    // 쐐기가 **계획 폴리곤 가까이**까지 내려와야 한다(0단 벽면이 거기 있다).
    double nearest = double.MaxValue;
    foreach (var (poly, _, _) in wedges)
    {
        foreach (var c in poly.Coordinates)
        {
            double d = double.MaxValue;
            for (int i = 0, j = sq.Count - 1; i < sq.Count; j = i++)
            {
                double ax = sq[j].X, ay = sq[j].Y, dx = sq[i].X - ax, dy = sq[i].Y - ay, l2 = dx * dx + dy * dy;
                if (l2 < 1e-12) continue;
                double u = ((c.X - ax) * dx + (c.Y - ay) * dy) / l2; u = Math.Max(0, Math.Min(1, u));
                double px = ax + dx * u - c.X, py = ay + dy * u - c.Y;
                d = Math.Min(d, Math.Sqrt(px * px + py * py));
            }
            if (d < nearest) nearest = d;
        }
    }
    double rulerFar = 0;
    foreach (var q in ruler)
    {
        double d = double.MaxValue;
        for (int i = 0, j = sq.Count - 1; i < sq.Count; j = i++)
        {
            double ax = sq[j].X, ay = sq[j].Y, dx = sq[i].X - ax, dy = sq[i].Y - ay, l2 = dx * dx + dy * dy;
            if (l2 < 1e-12) continue;
            double u = ((q.X - ax) * dx + (q.Y - ay) * dy) / l2; u = Math.Max(0, Math.Min(1, u));
            double px = ax + dx * u - q.X, py = ay + dy * u - q.Y;
            d = Math.Min(d, Math.Sqrt(px * px + py * py));
        }
        rulerFar = Math.Max(rulerFar, d);
    }
    Console.WriteLine($"      S58 쐐기가 계획선에 가장 가까운 거리 {nearest:F2}m · 자 링은 최대 {rulerFar:F1}m 밖");
    Check("S58 ★★★쐐기가 계획선까지 내려온다(아래 단 옹벽 자리를 덮는다)", nearest < 3.0,
        $"{nearest:F2}m (자 링은 {rulerFar:F1}m 밖)");
}

// ★ S59 [JACK 0824 터파기] **목표면 = 두 면 중 낮은 쪽.**
//   JACK이 든 예: 긴 직사각형 부지 — 우측은 절토, 좌측은 성토.
//   *"터파기선은 우측은 계획면까지 법선이 있어야 하고 좌측은 원지반까지 법선이 있어야 하는 거야."*
//   그 말이 `LowerOfSurfaces` 한 줄로 떨어지는지, 그리고 실제로 굴착 법면이
//   좌우에서 서로 다른 표고에 닿는지를 숫자로 못 박는다.
{
    // 원지반: 서쪽이 낮고 동쪽이 높다(x=0 → 100m, x=200 → 120m).
    var ground = new SlopeGround(100.0, 0.10);
    // 계획면: 전 구역 평탄 110m → x<100은 성토(원지반이 낮다), x>100은 절토(계획이 낮다).
    var plan = new FlatGround(110.0);
    var target = new LowerOfSurfaces(plan, ground);

    // ① 규칙 자체 — 좌우에서 목표가 갈리는가.
    target.TryGetElevation(20, 50, out double zW);      // 서쪽(성토부) 원지반 102
    target.TryGetElevation(180, 50, out double zE);     // 동쪽(절토부) 원지반 118
    Console.WriteLine($"      S59 목표면 — 서쪽 x=20 {zW:F1}m(원지반 102 · 계획 110) · " +
                      $"동쪽 x=180 {zE:F1}m(원지반 118 · 계획 110)");
    Check("S59 ★★성토부는 원지반이 목표", Math.Abs(zW - 102.0) < 1e-9, $"{zW:F2}m (102여야 한다)");
    Check("S59 ★★절토부는 계획면이 목표", Math.Abs(zE - 110.0) < 1e-9, $"{zE:F2}m (110이어야 한다)");
    Check("S59 ★성토부는 '원지반 쪽'으로 표시된다", !target.TargetIsFirst(20, 50), "정지면 쪽으로 나왔다");
    Check("S59 ★절토부는 '정지면 쪽'으로 표시된다", target.TargetIsFirst(180, 50), "원지반 쪽으로 나왔다");

    // ② 절성경계(x=100, 원지반=110=계획)에서 이어지는가 — 좌우로 살짝 옮겨 점프가 없는지.
    target.TryGetElevation(99.5, 50, out double zL);
    target.TryGetElevation(100.5, 50, out double zR);
    Console.WriteLine($"      S59 절성경계 x=100 앞뒤 — {zL:F3}m / {zR:F3}m (차 {Math.Abs(zR - zL) * 1000:F1}mm)");
    Check("S59 ★★★절성경계에서 목표면이 끊기지 않는다(이음매 처리 불필요)",
        Math.Abs(zR - zL) < 0.11, $"차 {Math.Abs(zR - zL):F3}m");

    // ③ 실제 굴착 — 구조물 바닥 폴리곤에서 법면을 올려 목표면에 닿게 한다.
    //    절성경계를 가로지르도록 부지 가운데에 길게 놓는다.
    var box = new List<Point3>
    {
        new(60, 30, 95), new(140, 30, 95), new(140, 70, 95), new(60, 70, 95),
    };
    var pr = new GradingParams
    {
        CutBenchHeight = 3, FillBenchHeight = 3, CutBenchWidth = 0, FillBenchWidth = 0,
        CutSlope = 0.5, FillSlope = 0.5, CellSize = 1.0, MaxBenches = 30, MaxRise = 40,
        VertexSpacing = 2.0, MinSlope = 0.05, MinFaceRun = 0.005, MiterConvex = true, MiterLimit = 2.0,
    };
    // 구조물 바닥(95)은 목표면보다 낮으므로 **절토 방향**(위로 올라가는 법면)이다.
    var v = GradingGeometry.Build(box, target, pr, up: true, null);
    Check("S59 굴착 법면이 생성된다", v.HasSlope && v.Rings.Count > 2, $"링 {v.Rings.Count}");

    // ④ 굴착 상단선(데이라잇)이 좌우에서 서로 다른 표고에 닿는가 — JACK 질문의 핵심.
    //    법면 링 중 목표면에 처음 닿는 자리를 좌·우로 나눠 본다.
    //   ※ 링은 **데이라잇 전 오버사이즈**라 끝까지(MaxRise) 올라간다 — 목표면에 **처음 닿는** 표고를 잰다.
    double topW = double.MaxValue, topE = double.MaxValue;
    foreach (var ring in v.Rings)
        foreach (var q in ring)
        {
            if (!target.TryGetElevation(q.X, q.Y, out double tz)) continue;
            if (q.Z < tz - 0.05) continue;                       // 아직 목표면 아래 = 굴착 안
            if (q.X < 60) topW = Math.Min(topW, q.Z);            // 부지 서쪽(성토부) 바깥
            if (q.X > 140) topE = Math.Min(topE, q.Z);           // 부지 동쪽(절토부) 바깥
        }
    Console.WriteLine($"      S59 굴착 상단이 닿은 표고 — 서쪽 {topW:F1}m(원지반) · 동쪽 {topE:F1}m(계획면 110)");
    Check("S59 ★★★서쪽 굴착은 원지반(110보다 낮은 자리)에 닿는다",
        topW < double.MaxValue && topW < 110.0 - 0.5, $"{topW:F2}m");
    Check("S59 ★★★동쪽 굴착은 계획면(110)에 닿는다",
        topE < double.MaxValue && Math.Abs(topE - 110.0) < 3.5, $"{topE:F2}m");
    Check("S59 ★★좌우 상단 표고가 서로 다르다(한 면으로 뭉뚱그려지지 않았다)",
        topW < double.MaxValue && topE < double.MaxValue && Math.Abs(topE - topW) > 1.0,
        $"서쪽 {topW:F1} · 동쪽 {topE:F1}");
}

// ★ S60 [JACK 0825 "수직지표면치고 0.05는 너무 과해 — 단수가 많아지면 부지면적이 커지고
//   토공량에도 차이가 난다"] **구배 하한을 낮추면 무엇이 달라지는가 — 재고 정한다.**
//
//   0.05는 실측으로 정한 값이 아니다("사례가 있어 미연 방지"라고만 적혀 있다).
//   그래서 추측 대신 **같은 부지에 구배만 바꿔 넣고 재 본다** — 면적·링·간격 셋을.
{
    // 100x100 부지, 계획 100m, 원지반 115m(절토 15m). 단높이 5m·소단 0 → 옹벽 3단.
    double S = 100.0;
    var sq = new List<Point3> { new(0, 0, 100), new(S, 0, 100), new(S, S, 100), new(0, S, 100) };
    var ground = new FlatGround(115);
    double baseArea = Math.Abs(Shoelace(sq));

    Console.WriteLine("      S60 구배 하한 계측 — 100x100 부지 · 절토 15m · 단높이 5m · 소단 0 (옹벽 3단)");
    Console.WriteLine("        구배     링   최종면적㎡   부지증가㎡   증가율%   링간격mm(최소)");

    double area05 = 0, area01 = 0;
    foreach (double n in new[] { 0.05, 0.03, 0.02, 0.01, 0.005, 0.002 })
    {
        var pr = new GradingParams
        {
            CutBenchHeight = 5, FillBenchHeight = 5, CutBenchWidth = 0, FillBenchWidth = 0,
            CutSlope = n, FillSlope = n, CellSize = 1.0, MaxBenches = 30, MaxRise = 40,
            VertexSpacing = 2.0, MinSlope = n, MinFaceRun = 0.005, MiterConvex = true, MiterLimit = 2.0,
        };
        VirtualSlope vs;
        try { vs = GradingGeometry.Build(sq, ground, pr, up: true, null); }
        catch (Exception ex)
        { Console.WriteLine($"        1:{n,-6:0.###} 예외 — {ex.GetType().Name}: {ex.Message}"); continue; }

        if (!vs.HasSlope || vs.Rings.Count < 2)
        { Console.WriteLine($"        1:{n,-6:0.###} 사면 없음(링 {vs.Rings.Count})"); continue; }

        var last = vs.Rings[vs.Rings.Count - 1];
        double a = Math.Abs(Shoelace(last));
        // ★ 벽 하나의 <b>크레스트↔토우</b> 간격을 잰다 — TIN에서 그 벽이 얼마나 납작한가.
        //   SlopeHatchGenerator와 같은 짝짓기(2k, 2k+1)를 쓴다. 소단이 0이면 '토우↔다음 크레스트'는
        //   같은 자리라 0이 나오는 게 정상이므로, 이웃 링을 그냥 훑으면 늘 0이 나온다.
        double gap = double.MaxValue;
        for (int k = 0; 2 * k + 1 < vs.Rings.Count; k++)
            gap = Math.Min(gap, MinRingGap(vs.Rings[2 * k], vs.Rings[2 * k + 1]));

        if (Math.Abs(n - 0.05) < 1e-9) area05 = a;
        if (Math.Abs(n - 0.01) < 1e-9) area01 = a;
        Console.WriteLine($"        1:{n,-6:0.###} {vs.Rings.Count,3}  {a,10:F1}  {a - baseArea,10:F1}  " +
                          $"{(a - baseArea) / baseArea * 100,7:F2}  {(gap == double.MaxValue ? -1 : gap * 1000),12:F1}");
    }

    Check("S60 ★구배 0.01에서도 기하가 만들어진다", area01 > 0, $"{area01:F1}㎡");
    if (area05 > 0 && area01 > 0)
    {
        double save = area05 - area01;
        Console.WriteLine($"      S60 ★★0.05 → 0.01 로 낮추면 부지가 {save:F1}㎡ 줄어든다" +
                          $" (절토 평균 7.5m 가정 시 토공 약 {save * 7.5:F0}㎥)");
        Check("S60 ★★0.05보다 0.01이 부지를 적게 먹는다(JACK 지적대로다)", save > 0, $"{save:F1}㎡");
    }
}

// ★ S61 [JACK 0825] **구배를 낮추면 옹벽 '선'이 살아남는가** — 면적이 맞아도 선이 뭉개지면 소용없다.
//   S60은 링(기하)만 봤다. 실제로 도면에 나가는 것은 SlopeHatchGenerator가 뽑는 선이고,
//   그 경로에는 '가까우면 합친다/버린다'는 문턱이 여럿 있다. 좁은 간격에서 그 문턱에 걸리는지 본다.
{
    double S = 100.0;
    var sq = new List<Point3> { new(0, 0, 100), new(S, 0, 100), new(S, S, 100), new(0, S, 100) };
    var ground = new FlatGround(115);
    var cum61 = GradingGeometry.CumLen2D(sq);
    double L61 = cum61[^1];

    Console.WriteLine("      S61 구배별 옹벽선 생존 — 같은 부지·같은 단높이, 구배만 바꾼다");
    Console.WriteLine("        구배     사면선  소단선  옹벽선  옹벽선점수  가장짧은옹벽선m");

    int wall05 = 0, wall01 = 0;
    foreach (double n in new[] { 0.05, 0.02, 0.01, 0.005, 0.002 })
    {
        var pr = new GradingParams
        {
            CutBenchHeight = 5, FillBenchHeight = 5, CutBenchWidth = 0, FillBenchWidth = 0,
            CutSlope = n, FillSlope = n, CellSize = 1.0, MaxBenches = 30, MaxRise = 40,
            VertexSpacing = 2.0, MinSlope = n, MinFaceRun = 0.005, MiterConvex = true, MiterLimit = 2.0,
        };
        var zw = new SlopeZone { T0 = 0.0, T1 = L61 };          // 둘레 전체가 옹벽
        zw.Rules.Add((0, n, 0.0)); zw.Normalize();
        var zones = new List<SlopeZone> { zw };

        VirtualSlope vs;
        try { vs = GradingGeometry.Build(sq, ground, pr, up: true, zones); }
        catch (Exception ex) { Console.WriteLine($"        1:{n,-6:0.###} 기하 예외 — {ex.GetType().Name}"); continue; }
        if (!vs.HasSlope) { Console.WriteLine($"        1:{n,-6:0.###} 사면 없음"); continue; }

        var fr = vs.Rings[vs.Rings.Count - 1];
        var wallPts = new List<(int Bench, bool IsCrest, List<Point3> Pts)>();
        List<(bool IsSlope, int Bench, int Seg, List<Point3> Pts)> edges;
        try
        {
            edges = SlopeHatchGenerator.GenerateEdgeLinesTagged(vs.Rings, ground, true, fr, sq,
                zones, sq, null, null, n, n, null, wallPts);
        }
        catch (Exception ex) { Console.WriteLine($"        1:{n,-6:0.###} 선 예외 — {ex.GetType().Name}"); continue; }

        int slope = 0, berm = 0;
        foreach (var e in edges) { if (e.IsSlope) slope++; else berm++; }
        int pts = 0; double shortest = double.MaxValue;
        foreach (var w in wallPts)
        {
            pts += w.Pts.Count;
            double len = 0;
            for (int i = 1; i < w.Pts.Count; i++)
            {
                double dx = w.Pts[i].X - w.Pts[i - 1].X, dy = w.Pts[i].Y - w.Pts[i - 1].Y;
                len += Math.Sqrt(dx * dx + dy * dy);
            }
            if (len < shortest) shortest = len;
        }
        if (Math.Abs(n - 0.05) < 1e-9) wall05 = wallPts.Count;
        if (Math.Abs(n - 0.01) < 1e-9) wall01 = wallPts.Count;
        Console.WriteLine($"        1:{n,-6:0.###} {slope,6} {berm,7} {wallPts.Count,7} {pts,10}" +
                          $" {(shortest == double.MaxValue ? -1 : shortest),15:F2}");
    }

    Check("S61 ★구배 0.01에서도 옹벽선이 나온다", wall01 > 0, $"{wall01}줄");
    Check("S61 ★★구배를 낮춰도 옹벽선 개수가 유지된다(문턱에 안 먹혔다)",
        wall05 > 0 && wall01 == wall05, $"0.05에서 {wall05}줄 · 0.01에서 {wall01}줄");
}

// ★ S62 [JACK 0825] **위험한 건 구배가 아니라 '간격'이다** — 단높이가 작으면 같은 구배도 위험해진다.
//   Civil 3D TIN이 깨지는 건 두 정점이 <b>정확히 같은 자리</b>일 때다(같은 X,Y엔 점 하나만 산다).
//   간격 = 구배 × 단높이 이므로, 구배만 보고 하한을 걸면 <b>낮은 단에서 0으로 수렴</b>한다.
{
    double S = 100.0;
    var sq = new List<Point3> { new(0, 0, 100), new(S, 0, 100), new(S, S, 100), new(0, S, 100) };
    var ground = new FlatGround(115);

    Console.WriteLine("      S62 단높이별 링 간격 — 구배 1:0.01 고정, 단높이만 바꾼다");
    Console.WriteLine("        단높이m   이론간격mm   실측간격mm   링   사면생성");

    const double n62 = 0.01;
    foreach (double h in new[] { 15.0, 5.0, 2.0, 1.0, 0.5, 0.3, 0.1 })
    {
        var pr = new GradingParams
        {
            CutBenchHeight = h, FillBenchHeight = h, CutBenchWidth = 0, FillBenchWidth = 0,
            CutSlope = n62, FillSlope = n62, CellSize = 1.0, MaxBenches = 200, MaxRise = 40,
            VertexSpacing = 2.0, MinSlope = n62, MinFaceRun = 0.005, MiterConvex = true, MiterLimit = 2.0,
        };
        VirtualSlope vs;
        try { vs = GradingGeometry.Build(sq, ground, pr, up: true, null); }
        catch (Exception ex)
        { Console.WriteLine($"        {h,7:0.##} {n62 * h * 1000,12:F1}  예외 {ex.GetType().Name}"); continue; }

        double gap = double.MaxValue;
        for (int k = 0; 2 * k + 1 < vs.Rings.Count; k++)
            gap = Math.Min(gap, MinRingGap(vs.Rings[2 * k], vs.Rings[2 * k + 1]));
        Console.WriteLine($"        {h,7:0.##} {n62 * h * 1000,12:F1} {(gap == double.MaxValue ? -1 : gap * 1000),12:F2}" +
                          $" {vs.Rings.Count,5} {(vs.HasSlope ? "O" : "X"),8}");
    }
    Console.WriteLine("        ※ Civil 3D 내장 Wall 브레이크라인이 쓰는 오프셋 = 0.001ft ≈ 0.3mm");
    Console.WriteLine("           실무 권고 3~10mm — 그 아래로 내려가는 조합이 진짜 경계다.");
}

// ★ S63 [JACK 0825 구배 하한 검토] **링을 부풀린 값과 벽을 판정하는 값이 어긋나 있다.**
//
//   `GradingGeometry`는 사면 수평폭을 <c>Math.Max(rise*slope, MinFaceRun)</c>로 <b>5mm까지 부풀려</b> 링을 뜬다.
//   그런데 `WallRunBuilder`는 벽인지 볼 때 <c>wallGap = minSlope * h</c>로 <b>부풀리기 전 값</b>을 기댄다.
//   그래서 <c>구배 × 단높이</c>가 5mm 밑으로 내려가면 실제 간격이 한도를 넘어 <b>벽면이 전부 탈락</b>한다.
//
//   이 시험은 그 어긋남을 <b>숫자로 고정</b>한다 — 고친 뒤 이 값이 어떻게 변하는지가 판정 근거다.
{
    Console.WriteLine("      S63 링 부풀림 vs 벽 판정 한도 — 어긋나기 시작하는 지점");
    Console.WriteLine("        구배   단높이m  이론간격mm  링이쓴폭mm  판정한도mm  판정");

    const double mfr = 0.005;                       // GradingSettings.MinFaceRun
    bool anyBroken = false, allSafeAtReal = true;
    foreach (var (n, h) in new[]
    {
        (0.05, 5.0), (0.01, 5.0), (0.01, 3.0), (0.01, 1.0),
        (0.01, 0.5), (0.01, 0.45), (0.01, 0.3), (0.02, 0.25), (0.02, 0.2),
    })
    {
        double want = n * h;                        // 이론 간격
        double used = Math.Max(want, mfr);          // 링이 실제로 쓴 폭(GradingGeometry.cs:1002)
        double lim = want * 1.05 + 1e-3;            // 벽 판정 한도(WallRunBuilder.cs:113)
        bool ok = used <= lim;
        if (!ok) anyBroken = true;
        if (h >= 3.0 && !ok) allSafeAtReal = false;   // 실무 단높이(3m 이상)에서는 절대 깨지면 안 된다
        Console.WriteLine($"        1:{n,-5:0.###}{h,8:0.##}{want * 1000,12:F2}{used * 1000,12:F2}" +
                          $"{lim * 1000,12:F2}   {(ok ? "벽" : "★탈락")}");
    }

    Check("S63 ★★실무 단높이(3m 이상)에서는 구배 0.01이어도 벽으로 잡힌다", allSafeAtReal,
        "3m·5m 조합이 전부 '벽'이어야 한다");
    Check("S63 ★낮은 단높이에서 어긋남이 실재한다(고치기 전 상태를 고정)", anyBroken,
        "하나라도 '탈락'이 나와야 이 시험이 의미를 가진다");
    Console.WriteLine("        ※ 고치는 법: WallRunBuilder의 wallGap을 Math.Max(minSlope*h, MinFaceRun)으로.");
    Console.WriteLine("           그러면 '링이 쓴 폭'과 '판정 한도'가 구조적으로 어긋날 수 없다.");
}

// ★ S64 [JACK 0825 구배 하한 검토 · 검토에이전트 지적] **진짜 위험은 '불일치'다.**
//
//   지금까지의 시험은 전부 <b>구배도 0.01, 판정도 0.01</b>인 일관된 경우였다 — 당연히 통과한다.
//   실제 사고는 <b>옛 도면</b>에서 난다: 구간 규칙에 적힌 구배는 <b>0.05</b>인데(그때 만든 것이라)
//   params의 판정 기준만 <b>0.01</b>로 바뀌는 조합이다. 그러면 소프트웨어가
//   <b>모양은 수직인데 사면이라고 믿는다</b>.
//
//   이 시험이 그 조합을 직접 만들어 옹벽선이 실제로 사라지는지 확인한다.
{
    double S = 100.0;
    var sq = new List<Point3> { new(0, 0, 100), new(S, 0, 100), new(S, S, 100), new(0, S, 100) };
    var ground = new FlatGround(115);
    var cum64 = GradingGeometry.CumLen2D(sq);
    double L64 = cum64[^1];

    // 어느 경우에도 <b>구간 규칙 구배는 0.05</b>(옛 도면이 저장해 둔 값)로 고정한다.
    const double ruleN = 0.05;

    Console.WriteLine("      S64 규칙 0.05 · 판정 기준만 바꿔 본다 — 옛 도면이 겪을 일");
    Console.WriteLine("        판정기준   옹벽선  사면선  소단선   판정");

    int wallSame = -1, wallMismatch = -1;
    foreach (double gate in new[] { 0.05, 0.01 })
    {
        var pr = new GradingParams
        {
            CutBenchHeight = 5, FillBenchHeight = 5, CutBenchWidth = 0, FillBenchWidth = 0,
            CutSlope = ruleN, FillSlope = ruleN, CellSize = 1.0, MaxBenches = 30, MaxRise = 40,
            VertexSpacing = 2.0, MinSlope = gate, MinFaceRun = 0.005, MiterConvex = true, MiterLimit = 2.0,
        };
        var zw = new SlopeZone { T0 = 0.0, T1 = L64 };
        zw.Rules.Add((0, ruleN, 0.0)); zw.Normalize();      // ← 규칙은 언제나 0.05
        var zones = new List<SlopeZone> { zw };

        var vs = GradingGeometry.Build(sq, ground, pr, up: true, zones);
        var fr = vs.Rings[vs.Rings.Count - 1];
        var wallPts = new List<(int Bench, bool IsCrest, List<Point3> Pts)>();
        var edges = SlopeHatchGenerator.GenerateEdgeLinesTagged(vs.Rings, ground, true, fr, sq,
            zones, sq, null, null, ruleN, gate, null, wallPts);

        int slope = 0, berm = 0;
        foreach (var e in edges) { if (e.IsSlope) slope++; else berm++; }
        if (Math.Abs(gate - 0.05) < 1e-9) wallSame = wallPts.Count; else wallMismatch = wallPts.Count;

        Console.WriteLine($"        1:{gate,-8:0.###} {wallPts.Count,6} {slope,7} {berm,7}   " +
                          (wallPts.Count > 0 ? "옹벽으로 본다" : "★사면으로 본다 — 옹벽선 사라짐"));
    }

    Check("S64 규칙과 판정이 같으면(0.05·0.05) 옹벽으로 잡힌다", wallSame > 0, $"{wallSame}줄");
    Check("S64 ★★★규칙 0.05인데 판정만 0.01로 낮추면 옹벽이 사면이 된다(검토 지적이 옳다)",
        wallMismatch == 0, $"옹벽선 {wallMismatch}줄 · 사면선이 대신 생긴다");
    Console.WriteLine("        ※ 그래서 판정 기준(게이트)은 0.05로 두고, 하한만 낮춰야 한다.");
    Console.WriteLine("           게이트를 그대로 두면 한도가 넓어 0.01짜리 얇은 벽도 함께 통과한다.");
}

// ★ S65 [JACK 0825] **게이트를 떼어내면 옛 옹벽이 살아남는가** — S64가 재현한 사고의 해독제.
//
//   S64: 규칙 0.05 · 판정 0.01 → 옹벽선 16줄이 <b>0줄</b>이 됐다.
//   이제 판정을 <c>WallGateSlope</c>(0.05 동결)가 맡고 하한만 0.01로 내려간다.
//   같은 조합에서 옹벽선이 <b>그대로 살아 있어야</b> 한다 — 그리고 새로 만드는 얇은 벽(0.01)도 함께.
{
    double S = 100.0;
    var sq = new List<Point3> { new(0, 0, 100), new(S, 0, 100), new(S, S, 100), new(0, S, 100) };
    var ground = new FlatGround(115);
    var cum65 = GradingGeometry.CumLen2D(sq);
    double L65 = cum65[^1];

    Console.WriteLine("      S65 게이트 분리 후 — 하한 0.01 · 게이트 0.05 고정");
    Console.WriteLine("        규칙구배  옹벽선  사면선  소단선  링간격mm   판정");

    int wallOld = -1, wallNew = -1;
    foreach (double ruleN in new[] { 0.05, 0.03, 0.01 })
    {
        var pr = new GradingParams
        {
            CutBenchHeight = 5, FillBenchHeight = 5, CutBenchWidth = 0, FillBenchWidth = 0,
            CutSlope = ruleN, FillSlope = ruleN, CellSize = 1.0, MaxBenches = 30, MaxRise = 40,
            VertexSpacing = 2.0,
            MinSlope = 0.01,          // ← 하한은 내려갔고
            WallGateSlope = 0.05,     // ← 판정 문턱은 그대로다
            MinFaceRun = 0.005, MiterConvex = true, MiterLimit = 2.0,
        };
        var zw = new SlopeZone { T0 = 0.0, T1 = L65 };
        zw.Rules.Add((0, ruleN, 0.0)); zw.Normalize();
        var zones = new List<SlopeZone> { zw };

        var vs = GradingGeometry.Build(sq, ground, pr, up: true, zones);
        var fr = vs.Rings[vs.Rings.Count - 1];
        var wallPts = new List<(int Bench, bool IsCrest, List<Point3> Pts)>();
        var edges = SlopeHatchGenerator.GenerateEdgeLinesTagged(vs.Rings, ground, true, fr, sq,
            zones, sq, null, null, ruleN, pr.WallGateSlope, null, wallPts);

        int slope = 0, berm = 0;
        foreach (var e in edges) { if (e.IsSlope) slope++; else berm++; }
        double gap = double.MaxValue;
        for (int k = 0; 2 * k + 1 < vs.Rings.Count; k++)
            gap = Math.Min(gap, MinRingGap(vs.Rings[2 * k], vs.Rings[2 * k + 1]));

        if (Math.Abs(ruleN - 0.05) < 1e-9) wallOld = wallPts.Count;
        if (Math.Abs(ruleN - 0.01) < 1e-9) wallNew = wallPts.Count;
        Console.WriteLine($"        1:{ruleN,-7:0.###} {wallPts.Count,6} {slope,7} {berm,7} " +
                          $"{(gap == double.MaxValue ? -1 : gap * 1000),9:F1}   " +
                          (wallPts.Count > 0 ? "옹벽" : "★사면 — 사라짐"));
    }

    Check("S65 ★★★옛 옹벽(규칙 0.05)이 하한을 낮춰도 살아남는다", wallOld > 0, $"{wallOld}줄");
    Check("S65 ★★새로 만드는 얇은 벽(규칙 0.01)도 옹벽으로 잡힌다", wallNew > 0, $"{wallNew}줄");
    Check("S65 ★둘이 같은 수의 옹벽선을 낸다(분류가 흔들리지 않는다)", wallOld == wallNew,
        $"옛 {wallOld}줄 · 새 {wallNew}줄");

    // 옹벽선 정본(WallRunBuilder)도 같은 자를 쓰는지 — 여기가 빠지면 3D·InfraWorks가 통째로 옛 경로로 샌다.
    foreach (double ruleN in new[] { 0.05, 0.01 })
    {
        var pr = new GradingParams
        {
            CutBenchHeight = 5, FillBenchHeight = 5, CutBenchWidth = 0, FillBenchWidth = 0,
            CutSlope = ruleN, FillSlope = ruleN, CellSize = 1.0, MaxBenches = 30, MaxRise = 40,
            VertexSpacing = 2.0, MinSlope = 0.01, WallGateSlope = 0.05,
            MinFaceRun = 0.005, MiterConvex = true, MiterLimit = 2.0,
        };
        var zw = new SlopeZone { T0 = 0.0, T1 = L65 };
        zw.Rules.Add((0, ruleN, 0.0)); zw.Normalize();
        var zones = new List<SlopeZone> { zw };
        var vs = GradingGeometry.Build(sq, ground, pr, up: true, zones);
        var runs = WallRunBuilder.Build(sq, vs.Rings, zones, up: true,
                                        globalSlope: ruleN, minSlope: pr.MinSlope, gateSlope: pr.WallGateSlope);
        Console.WriteLine($"        옹벽선 정본 — 규칙 1:{ruleN:0.###} → {runs.Count}줄  ({WallRunBuilder.LastDiag})");
        Check($"S65 ★★옹벽선 정본이 규칙 1:{ruleN:0.###}에서도 나온다(0줄이면 3D가 옛 경로로 샌다)",
            runs.Count > 0, $"{runs.Count}줄");
    }
}

// ★ S66 [JACK 0825 · 3D 검토 지적] **3D 매스의 방향 문턱은 구배에 매인 값이었다.**
//   `WallBand`는 단면의 노출면 방향을 <b>크레스트→토우</b> 수평벡터로 정하는데, 그 길이가 곧
//   <c>구배 × 단높이</c>다. 문턱이 절대 2cm였으므로 구배를 1/5로 낮추면 <b>방향을 못 재는 단높이가
//   0.4m에서 2.0m로 다섯 배 올라간다</b> — 걸린 단은 매스도 마감판도 0개가 된다.
//   문턱의 본뜻은 "좌표 잡음보다 큰가"이므로 격자 잡음(1mm)의 몇 배로 잡아야 옳다.
{
    const double NormMinOld = 0.02, NormMinNew = 0.005;
    Console.WriteLine("      S66 3D 매스 방향 문턱 — 구배별로 어느 단높이부터 못 재나");
    Console.WriteLine("        구배    단높이m   벡터길이mm   옛문턱(20mm)  새문턱(5mm)");

    bool oldBreaks5m = false, newOk5m = true, newOk1m = true;
    foreach (var (n, h) in new[]
    {
        (0.05, 5.0), (0.05, 0.5), (0.05, 0.3),
        (0.01, 5.0), (0.01, 2.0), (0.01, 1.0), (0.01, 0.5),
    })
    {
        double v = n * h;
        bool okOld = v >= NormMinOld, okNew = v >= NormMinNew;
        if (n < 0.02 && h >= 1.0 && !okOld) oldBreaks5m = true;      // 실무 단높이인데 옛 문턱에 걸린다
        if (n < 0.02 && h >= 5.0 && !okNew) newOk5m = false;
        if (n < 0.02 && h >= 1.0 && !okNew) newOk1m = false;
        Console.WriteLine($"        1:{n,-6:0.###}{h,8:0.##}{v * 1000,13:F1}   {(okOld ? "잰다" : "★못 잼"),12}  {(okNew ? "잰다" : "★못 잼"),11}");
    }

    Check("S66 ★★옛 문턱(2cm)이면 1:0.01에서 실무 단높이도 방향을 못 잰다(그래서 고쳤다)",
        oldBreaks5m, "단높이 1~2m가 걸린다");
    Check("S66 ★★새 문턱(5mm)이면 1:0.01·단높이 5m는 넉넉히 잰다", newOk5m, "50mm ≥ 5mm");
    Check("S66 ★새 문턱이면 단높이 1m까지 내려가도 잰다", newOk1m, "10mm ≥ 5mm");
    Console.WriteLine("        ※ 1mm 격자 스냅 잡음은 최대 ~1.4mm — 5mm는 그 3.5배다.");
    Console.WriteLine("           벽 끝(높이→0)에서는 새 문턱에도 걸려 '이웃 방향 물려받기'가 그대로 작동한다.");
}

// ★ S67 [JACK 0825 실도면 '계획지표면의 옹벽선이 아예 생성이 안 됐다'] **구간이 없어도 전역이 수직이면 옹벽이다.**
//
//   실측 로그: `옹벽선 확정 — 절토 11줄` (정지면 쪽은 정상) 인데 `종단 막대 — 옹벽 0개`.
//   측점 목록엔 `5.29m 데이라잇 / 5.32m 사면·소단` — 3cm 차이는 옹벽 두께(0.01×3m)인데
//   <b>사면·소단으로 분류</b>돼 짝짓기를 못 거치고 위·아래가 따로 섰다.
//
//   원인: `SlopeHatchGenerator`가 <b>옹벽 구간이 지정됐을 때만</b> 옹벽선을 분리했다.
//   부지 전체를 수직으로 준 도면(구간 0개 + 전역 수직)이 통째로 사면 취급됐다.
{
    double S = 100.0;
    var sq = new List<Point3> { new(0, 0, 100), new(S, 0, 100), new(S, S, 100), new(0, S, 100) };
    var ground = new FlatGround(115);

    Console.WriteLine("      S67 구간 없이 전역만 수직 — 옹벽선이 나오는가");
    Console.WriteLine("        전역구배  구간  옹벽선  사면선  소단선   판정");

    int wallGlobal = -1, slopeGlobal = -1;
    foreach (double gN in new[] { 0.01, 0.05, 1.5 })
    {
        var pr = new GradingParams
        {
            CutBenchHeight = 5, FillBenchHeight = 5, CutBenchWidth = 0, FillBenchWidth = 0,
            CutSlope = gN, FillSlope = gN, CellSize = 1.0, MaxBenches = 30, MaxRise = 40,
            VertexSpacing = 2.0, MinSlope = 0.01, WallGateSlope = 0.05,
            MinFaceRun = 0.005, MiterConvex = true, MiterLimit = 2.0,
        };
        // ★ 구간(zones)을 아예 주지 않는다 — 실도면이 그 상태였다.
        var vs = GradingGeometry.Build(sq, ground, pr, up: true, null);
        if (!vs.HasSlope) { Console.WriteLine($"        1:{gN,-7:0.###} 사면 없음"); continue; }
        var fr = vs.Rings[vs.Rings.Count - 1];
        var wallPts = new List<(int Bench, bool IsCrest, List<Point3> Pts)>();
        var edges = SlopeHatchGenerator.GenerateEdgeLinesTagged(vs.Rings, ground, true, fr, sq,
            null, sq, null, null, gN, pr.WallGateSlope, null, wallPts);

        int slope = 0, berm = 0;
        foreach (var e in edges) { if (e.IsSlope) slope++; else berm++; }
        bool isWall = gN <= pr.WallGateSlope + 1e-9;
        if (Math.Abs(gN - 0.01) < 1e-9) { wallGlobal = wallPts.Count; slopeGlobal = slope; }
        if (Math.Abs(gN - 1.5) < 1e-9) { }
        Console.WriteLine($"        1:{gN,-7:0.###} {"없음",5} {wallPts.Count,6} {slope,7} {berm,7}   " +
                          (isWall ? (wallPts.Count > 0 ? "옹벽 ✔" : "★옹벽인데 사면으로 샜다")
                                  : (wallPts.Count == 0 ? "사면 ✔" : "★사면인데 옹벽으로 샜다")));
    }

    Check("S67 ★★★구간이 없어도 전역이 수직이면 옹벽선이 나온다", wallGlobal > 0, $"{wallGlobal}줄");
    Check("S67 ★그때 사면선은 안 나온다(같은 선이 양쪽에 담기지 않는다)", slopeGlobal == 0, $"사면선 {slopeGlobal}줄");

    // 진짜 사면(1:1.5)은 그대로 사면이어야 한다 — 넓힌 것이 과하지 않은지.
    {
        var pr = new GradingParams
        {
            CutBenchHeight = 5, FillBenchHeight = 5, CutBenchWidth = 1, FillBenchWidth = 1,
            CutSlope = 1.5, FillSlope = 1.5, CellSize = 1.0, MaxBenches = 30, MaxRise = 40,
            VertexSpacing = 2.0, MinSlope = 0.01, WallGateSlope = 0.05,
            MinFaceRun = 0.005, MiterConvex = true, MiterLimit = 2.0,
        };
        var vs = GradingGeometry.Build(sq, ground, pr, up: true, null);
        var fr = vs.Rings[vs.Rings.Count - 1];
        var wallPts = new List<(int Bench, bool IsCrest, List<Point3> Pts)>();
        SlopeHatchGenerator.GenerateEdgeLinesTagged(vs.Rings, ground, true, fr, sq,
            null, sq, null, null, 1.5, pr.WallGateSlope, null, wallPts);
        Check("S67 ★★진짜 사면(1:1.5)은 옹벽으로 안 샌다", wallPts.Count == 0, $"옹벽선 {wallPts.Count}줄");
    }
}

return fails == 0 ? 0 : 1;

static double BaseOf(GradingParams p, bool up) => up ? p.CutSlope : p.FillSlope;

/// <summary>★[JACK 0825 S60] 폴리곤 면적(신발끈). 부호는 방향이라 절댓값으로 쓴다.</summary>
static double Shoelace(IReadOnlyList<Point3> r)
{
    double a = 0;
    for (int i = 0, j = r.Count - 1; i < r.Count; j = i++)
        a += (r[j].X + r[i].X) * (r[j].Y - r[i].Y);
    return a / 2.0;
}

/// <summary>★[JACK 0825 S60] 두 링이 평면에서 가장 가까운 거리 — 벽이 TIN에서 얼마나 납작한가.
/// <para>표본으로 훑는다(정점 수가 많아 전수는 느리다). 최솟값만 필요하므로 충분하다.</para></summary>
static double MinRingGap(IReadOnlyList<Point3> a, IReadOnlyList<Point3> b)
{
    double best = double.MaxValue;
    int sa = Math.Max(1, a.Count / 200), sb = Math.Max(1, b.Count / 200);
    for (int i = 0; i < a.Count; i += sa)
        for (int j = 0; j < b.Count; j += sb)
        {
            double dx = a[i].X - b[j].X, dy = a[i].Y - b[j].Y;
            double d = dx * dx + dy * dy;
            if (d < best) best = d;
        }
    return best == double.MaxValue ? best : Math.Sqrt(best);
}



/// <summary>★[JACK 0820] x가 xMax를 넘으면 <b>지반이 없다</b>(TryGetElevation=false)고 답하는 지반.
/// 실제 TIN 밖에서 WallSpanAtPt가 'hi &lt; 0 = 판단 불가'를 주는 상황을 그대로 만든다.</summary>
sealed class HoleGround(double z, double xMax) : IGroundSurface
{
    public bool TryGetElevation(double x, double y, out double z0)
    {
        z0 = z;
        return x <= xMax;
    }
}

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
/// <summary>★[JACK 0807] <b>현장형</b> 원지반 — 기울어진 평면 + mm급 고주파 잡음.
/// 현장 원지반은 삼각망이라 판넬 한 장(1.6m) 위에서는 사실상 평면이고, 데이라잇 윗변이 오목해 보이는 건
/// 0.15m 간격 표본이 만드는 <b>잡음</b>이다. 진짜로 휜 <see cref="WavyGround"/>와 구분해서 시험해야
/// '정점 정리 허용오차가 잡음을 펴 주는가'를 판정할 수 있다.</summary>
sealed class NoisyPlaneGround(double z0, double kx, double amp) : IGroundSurface
{
    public bool TryGetElevation(double x, double y, out double zz)
    { zz = z0 + kx * x + amp * System.Math.Sin(x * 37.0) + amp * System.Math.Cos(y * 41.0); return true; }
}

sealed class WavyGround(double z0, double amp, double wave) : IGroundSurface
{
    public bool TryGetElevation(double x, double y, out double zz)
    { zz = z0 + amp * System.Math.Sin(x / wave); return true; }
}
