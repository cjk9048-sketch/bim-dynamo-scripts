// WallBlocks 오프라인 하네스 — 옹벽 3D 보강토 블록 그리드 필터링 검증 (walltest와 같은 PASS/FAIL 방식)
using DH.Grading.Core;

int fails = 0;
void Check(string name, bool ok, string detail = "")
{
    Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {name} {detail}");
    if (!ok) fails++;
}

const double W = 0.46, H = 0.2, STEP = 5.0;

// 40×40 정사각 크레스트 링(z=105), 아래 단(pad) 링 z=100 — 절토 1개 단.
static List<Point3> Square(double z) => new()
{
    new Point3(0, 0, z), new Point3(40, 0, z), new Point3(40, 40, z), new Point3(0, 40, z),
};
var rings = new List<IReadOnlyList<Point3>> { Square(100), Square(105) };

// ── S1: 평탄 고지반(전절토, 지반 110 ≥ 크레스트) — 전 층·전 열 블록, 캡=최상층 전체 ──
{
    var g = new FlatGround(110);
    var blocks = WallBlocks.Generate(rings, g, cut: true, slopeN: 0, blockW: W, blockH: H);
    int cols = (int)Math.Floor(160.0 / W + 1e-9); // 347
    int courses = (int)Math.Floor(STEP / H + 1e-6); // 25
    Check("S1 블록수 = 층×열", blocks.Count == cols * courses, $"{blocks.Count} (기대 {cols * courses})");
    Check("S1 층수 25", blocks.Max(b => b.Course) == courses - 1, $"max층 {blocks.Max(b => b.Course)}");
    // 모든 블록 수평(z가 0.2 배수 격자) + 커팅라인 아래
    bool zGrid = blocks.All(b => Math.Abs((b.Z - 100.0) / H - Math.Round((b.Z - 100.0) / H)) < 1e-9);
    Check("S1 블록 z=수평 격자", zGrid);
    Check("S1 상면 ≤ 크레스트", blocks.All(b => b.Z + H <= 105 + 1e-9));
    // 엇갈림: 0층·1층 같은 열의 station 차 = W/2 → 바닥변(y=0) 블록 X 좌표로 확인
    var c0 = blocks.Where(b => b.Course == 0 && Math.Abs(b.Y) < 0.3 && b.X > 1 && b.X < 39).OrderBy(b => b.X).First();
    var c1 = blocks.Where(b => b.Course == 1 && Math.Abs(b.Y) < 0.3 && b.X > c0.X - 0.01).OrderBy(b => b.X).First();
    Check("S1 엇갈림 반블록", Math.Abs(c1.X - c0.X - W / 2) < 1e-6 || Math.Abs(c1.X - c0.X + W / 2) < 1e-6,
        $"ΔX {c1.X - c0.X:F3}");
    var caps = WallBlocks.GenerateCaps(blocks, H);
    Check("S1 캡 = 최상층 전체", caps.Count == cols, $"{caps.Count} (기대 {cols})");
    Check("S1 캡 z = 크레스트", caps.All(c => Math.Abs(c.Z - 105) < 1e-9));
}

// ── S2: 사선 지반(x 방향 상승 102→112) — 계단식, 커팅라인 준수, 캡-블록 무충돌 ──
{
    var g = new SlopeGround(102, 0.25); // g = 102 + 0.25x → x=0: 102(중간), x=40: 112(전고)
    var blocks = WallBlocks.Generate(rings, g, cut: true, slopeN: 0, blockW: W, blockH: H);
    Check("S2 블록 존재", blocks.Count > 0, $"{blocks.Count}개");
    // 커팅라인: 상면 ≤ clamp(지반, 토우, 크레스트) + zTol
    bool under = true;
    foreach (var b in blocks)
    {
        g.TryGetElevation(b.X, b.Y, out double gz);
        double top = Math.Min(105, Math.Max(100, gz));
        if (b.Z + H > top + 0.02 + 1e-9) { under = false; break; }
    }
    Check("S2 상면 ≤ 커팅라인", under);
    // 계단식: 바닥변(y=0)에서 열별 최고층이 x 증가에 따라 단조증가(지반 상승 방향)
    var bottom = blocks.Where(b => Math.Abs(b.Y) < 0.3).GroupBy(b => b.Column)
        .Select(gr => (Col: gr.Key, TopC: gr.Max(b => b.Course), X: gr.First().X)).OrderBy(t => t.X).ToList();
    bool mono = true;
    for (int i = 1; i < bottom.Count; i++) if (bottom[i].TopC < bottom[i - 1].TopC - 0) { mono = false; break; }
    Check("S2 계단 단조증가(y=0변)", mono, $"열 {bottom.Count}개");
    // 캡: 위층 블록과 수평 겹침 없음(같은 링·위층에서 station 겹치는 블록 존재 금지)
    var caps = WallBlocks.GenerateCaps(blocks, H);
    var occ = blocks.ToLookup(b => (b.Ring, b.Course));
    bool collide = false;
    foreach (var c in caps)
        foreach (var b in occ[(c.Ring, c.Course)]) // 캡 Course = 위층 번호
            if (Math.Abs(b.X - c.X) < W * 0.99 && Math.Abs(b.Y - c.Y) < W * 0.99) { collide = true; break; }
    Check("S2 캡-블록 무충돌", !collide, $"캡 {caps.Count}개");
    Check("S2 캡 존재", caps.Count > 0);
}

// ── S3: 지반이 토우 아래(99) — 벽 없음 → 블록 0 ──
{
    var blocks = WallBlocks.Generate(rings, new FlatGround(99), cut: true, slopeN: 0, blockW: W, blockH: H);
    Check("S3 벽 없음 → 블록 0", blocks.Count == 0, $"{blocks.Count}개");
}

// ── S4: 뒷물림(slopeN=0.05) — 아래층일수록 안쪽, 층당 n×H=10mm ──
{
    var g = new FlatGround(110);
    var blocks = WallBlocks.Generate(rings, g, cut: true, slopeN: 0.05, blockW: W, blockH: H);
    // 바닥변(y=0, 안쪽법선=+y): 0층 y = n×(step−H) = 0.24, 최상층(24) y = n×0 = 0
    var y0 = blocks.Where(b => b.Course == 0 && b.Y > -0.1 && b.Y < 0.5 && b.X > 5 && b.X < 35).Select(b => b.Y).FirstOrDefault(-1);
    var yTop = blocks.Where(b => b.Course == 24 && b.Y > -0.1 && b.Y < 0.5 && b.X > 5 && b.X < 35).Select(b => b.Y).FirstOrDefault(-1);
    Check("S4 0층 안쪽 0.24", Math.Abs(y0 - 0.05 * (STEP - H)) < 1e-6, $"y0 {y0:F3}");
    Check("S4 최상층 링 위(0)", Math.Abs(yTop) < 1e-6, $"yTop {yTop:F3}");
}

// ── S5: 성토(정렬선=토우 링 z=100, 크레스트 105, 지반 99 — 전면 노출) ──
{
    var fillRings = new List<IReadOnlyList<Point3>> { Square(105), Square(100) }; // rings[0]=pad(위), rings[1]=토우 링
    var blocks = WallBlocks.Generate(fillRings, new FlatGround(99), cut: false, slopeN: 0.05, blockW: W, blockH: H);
    int cols = (int)Math.Floor(160.0 / W + 1e-9);
    int courses = (int)Math.Floor(STEP / H + 1e-6);
    Check("S5 성토 블록수 = 층×열", blocks.Count == cols * courses, $"{blocks.Count} (기대 {cols * courses})");
    Check("S5 바닥 z = 토우(100)", Math.Abs(blocks.Min(b => b.Z) - 100) < 1e-9);
    // 뒷물림: 위층일수록 안쪽 — 바닥변에서 0층 y = n×H = 0.01, 최상층 y = n×step = 0.25
    var y0 = blocks.Where(b => b.Course == 0 && b.Y > -0.1 && b.Y < 0.5 && b.X > 5 && b.X < 35).Select(b => b.Y).FirstOrDefault(-1);
    var yTop = blocks.Where(b => b.Course == courses - 1 && b.Y > -0.1 && b.Y < 0.5 && b.X > 5 && b.X < 35).Select(b => b.Y).FirstOrDefault(-1);
    Check("S5 성토 0층 y=n×H", Math.Abs(y0 - 0.05 * H) < 1e-6, $"y0 {y0:F3}");
    Check("S5 성토 최상층 y=n×step", Math.Abs(yTop - 0.05 * STEP) < 1e-6, $"yTop {yTop:F3}");
    // 성토 지반이 크레스트 위(매몰) → 0
    var buried = WallBlocks.Generate(fillRings, new FlatGround(106), cut: false, slopeN: 0, blockW: W, blockH: H);
    Check("S5 매몰 → 블록 0", buried.Count == 0, $"{buried.Count}개");
}

// ── S6: 영역 필터 — 작은 사각 영역 안의 블록만 유지 ──
{
    var g = new FlatGround(110);
    var blocks = WallBlocks.Generate(rings, g, cut: true, slopeN: 0, blockW: W, blockH: H);
    var region = new List<IReadOnlyList<Point3>>
    { new List<Point3> { new(-1, -1, 0), new(10, -1, 0), new(10, 10, 0), new(-1, 10, 0) } };
    var kept = WallBlocks.FilterByRegions(blocks, region, 0.3);
    Check("S6 영역필터 축소", kept.Count > 0 && kept.Count < blocks.Count, $"{kept.Count}/{blocks.Count}");
    Check("S6 유지블록 영역 내", kept.All(b => b.X <= 10.4 && b.Y <= 10.4));
}

// ── S7: 회전각 — 바닥변(y=0, 진행 +x, 절토: 깊이=바깥(−y)) → 로컬 +Y=(0,−1) → rot=180° ──
{
    var g = new FlatGround(110);
    var blocks = WallBlocks.Generate(rings, g, cut: true, slopeN: 0, blockW: W, blockH: H);
    var b0 = blocks.First(b => Math.Abs(b.Y) < 0.01 && b.X > 5 && b.X < 35);
    // 깊이방향 (0,−1): rot = atan2(−dx, dy) = atan2(0, −1) = π
    Check("S7 절토 y=0변 rot=π", Math.Abs(Math.Abs(b0.RotRad) - Math.PI) < 1e-6, $"rot {b0.RotRad:F3}");
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
