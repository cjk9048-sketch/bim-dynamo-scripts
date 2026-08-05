namespace DH.Grading.Core;

/// <summary>3D 점 (도면 좌표계, 단위 m). XY는 평면, Z는 표고.</summary>
public readonly record struct Point3(double X, double Y, double Z);

/// <summary>
/// 원지반(原地盤) 표면 조회 인터페이스 — Core는 AutoCAD/Civil3D에 의존하지 않으므로
/// 표고 조회를 이 인터페이스로 추상화한다. Civil3D 측에서 TinSurface를 감싸 구현한다.
/// </summary>
public interface IGroundSurface
{
    /// <summary>(x,y)에서의 원지반 표고. 표면 범위를 벗어나면 false.</summary>
    bool TryGetElevation(double x, double y, out double z);
}

/// <summary>
/// [구간 구배 0804 — JACK] 계획경계 둘레의 한 구간에서 '이 단부터는 이 구배' 규칙 묶음.
/// 옹벽·사면 복귀·구배 변경이 전부 이 하나로 표현된다:
///   · 옹벽      = (시작단, 최소구배 0.05)   — 그 단부터 수직
///   · 사면 복귀 = (되돌릴 단, 전역 구배)     — 그 단부터 원래 사면(옛 ToBench가 하던 일)
///   · 구배 변경 = (시작단, 새 구배)          — 그 단부터 다른 구배
/// 규칙은 시작단 오름차순. 어떤 단의 구배 = 시작단이 그 단 이하인 규칙 중 '마지막' 것(없으면 전역 구배).
/// 여러 번 클릭하면 규칙이 쌓여 '아래는 급하게 · 위는 완만하게'가 된다(JACK 0804).
/// </summary>
public sealed class SlopeZone
{
    /// <summary>계획경계 호길이 구간 [T0,T1] — T0 &gt; T1이면 0을 지나 이어지는(랩) 구간.</summary>
    public double T0 { get; set; }
    public double T1 { get; set; }

    /// <summary>(시작단, 구배 n, 소단폭 m) 목록 — <see cref="Normalize"/>로 시작단 오름차순 정렬해 둔다.
    /// 소단폭이 음수면 '전역값 따름'(옛 번들에서 올라온 옹벽 구간이 그렇다).
    /// ※ 단높이는 구간별로 둘 수 없다 — 링은 '같은 표고의 등고선'이라 링 하나에 표고가 하나뿐인데,
    ///   구간마다 단높이가 다르면 같은 링에서 구간 안/밖의 표고가 달라져야 해서 표현이 안 된다.
    ///   구배·소단폭은 표고 순서를 바꾸지 않고 가로 방향만 바꾸므로 구간별로 안전하다(JACK 0804 — B안).</summary>
    public List<(int FromBench, double Slope, double BenchW)> Rules { get; set; } = new();

    /// <summary>이 단에 적용될 (구배, 소단폭) — 해당 규칙이 없는 항목은 전역값. Rules는 정렬 전제.</summary>
    public (double Slope, double BenchW) At(int bench, double baseSlope, double baseW)
    {
        double s = baseSlope, w = baseW;
        foreach (var r in Rules)
        {
            if (bench < r.FromBench) break;
            s = r.Slope;
            if (r.BenchW >= 0) w = r.BenchW;   // 소단 0(소단 없음)은 유효한 값
        }
        return (s, w);
    }

    /// <summary>이 단에 적용될 구배 — 해당 규칙이 없으면 baseSlope(전역 구배).</summary>
    public double SlopeAt(int bench, double baseSlope) => At(bench, baseSlope, 1).Slope;

    /// <summary>가장 낮은 규칙 시작단 — 이 단 미만은 전역 구배 그대로라 구간이 영향을 주지 않는다. 규칙이 없으면 무한대.</summary>
    public int FirstBench => Rules.Count > 0 ? Rules[0].FromBench : int.MaxValue;

    /// <summary>이 단이 '수직(옹벽)'인가 — 적용 구배가 최소구배 이하면 벽으로 본다.</summary>
    public bool IsWallAt(int bench, double baseSlope, double minSlope)
        => SlopeAt(bench, baseSlope) <= minSlope + 1e-9;

    /// <summary>(x,y)의 호길이 param t가 이 구간 안인가 — 랩 대응.</summary>
    public bool Contains(double t) => T0 <= T1 ? (t >= T0 && t <= T1) : (t >= T0 || t <= T1);

    /// <summary>규칙 정렬(시작단 오름차순) + 같은 시작단 중복 제거(나중 것이 이김).</summary>
    public void Normalize()
    {
        Rules.Sort((a, b) => a.FromBench.CompareTo(b.FromBench));
        for (int i = Rules.Count - 1; i > 0; i--)
            if (Rules[i].FromBench == Rules[i - 1].FromBench) Rules.RemoveAt(i - 1);
    }

    /// <summary>
    /// [스샷 버그 0804 — JACK] 겹치는 구간들을 '서로 겹치지 않는 조각'으로 재배열한다.
    /// ※ 종전 병합은 겹치는 두 구간을 **합집합 하나**로 뭉개고 규칙을 합쳤다 — 그래서 옹벽 구간의 일부만
    ///   사면으로 되돌려도 새 규칙이 노란선 범위가 아니라 **옹벽 구간 전체**에 퍼졌다(그 단 전부가 사면으로).
    /// 방법: 모든 구간의 경계 호길이로 둘레를 기본 조각으로 자르고, 조각마다 '그 조각을 덮는 구간들'의
    ///   규칙을 목록 순서(=시간 순서)로 합성한다. 나중 구간의 규칙은 자기 시작단부터 바깥의 기존 규칙을
    ///   지우고 대체한다 — "클릭한 단부터 바깥 끝까지"라는 명령 의미 그대로.
    ///   (낮은 단을 먼저 찍고 높은 단을 나중에 찍으면 층층이로 쌓이고, 반대로 나중에 더 낮은 단을 찍으면
    ///    그 단부터 바깥 전체가 새 값으로 바뀐다.)
    /// 마지막에 규칙이 같은 이웃 조각을 도로 합쳐 조각 수를 최소화한다(랩 포함). zones는 제자리 교체.
    /// </summary>
    public static void Flatten(List<SlopeZone> zones, double total)
    {
        if (zones == null || zones.Count <= 1 || total <= 1e-6) return;
        const double eps = 1e-6;
        double Mod(double t) { t %= total; if (t < 0) t += total; return t; }
        double LenOf(SlopeZone z) { double l = z.T1 - z.T0; if (z.T0 > z.T1) l += total; return l; }

        var src = new List<SlopeZone>();
        foreach (var z in zones)
            if (z != null && z.Rules.Count > 0 && LenOf(z) > eps) src.Add(z);
        if (src.Count <= 1) { zones.Clear(); zones.AddRange(src); return; }

        // ① 경계점 수집(0..total 정규화 → 정렬 → eps 병합, 0≈total 랩 중복 제거).
        //    전체 둘레 구간(길이 total)은 경계점 0 하나로 수렴 — 조각 [0,total) 하나가 된다.
        var cuts = new List<double>();
        foreach (var z in src)
        {
            if (LenOf(z) >= total - eps) { cuts.Add(0.0); continue; }
            cuts.Add(Mod(z.T0)); cuts.Add(Mod(z.T1));
        }
        cuts.Sort();
        var uq = new List<double>();
        foreach (var c in cuts)
            if (uq.Count == 0 || c - uq[uq.Count - 1] > eps) uq.Add(c);
        if (uq.Count >= 2 && uq[0] + total - uq[uq.Count - 1] <= eps) uq.RemoveAt(uq.Count - 1);
        if (uq.Count == 0) uq.Add(0.0);

        // ② 기본 조각마다 중점으로 덮는 구간을 찾아 규칙 합성(시간 순서, 시작단부터 바깥 대체).
        var pieces = new List<(double A, double B, List<(int FromBench, double Slope, double BenchW)> R)>();
        for (int i = 0; i < uq.Count; i++)
        {
            double a = uq[i];
            double b = i + 1 < uq.Count ? uq[i + 1] : uq[0] + total;
            if (b - a <= eps) continue;
            double mid = Mod((a + b) * 0.5);
            var rules = new List<(int, double, double)>();
            foreach (var z in src)
            {
                if (!z.Contains(mid)) continue;
                int zFirst = z.FirstBench;
                rules.RemoveAll(r => r.Item1 >= zFirst);
                rules.AddRange(z.Rules);
            }
            if (rules.Count == 0) continue;
            rules.Sort((x, y) => x.Item1.CompareTo(y.Item1));
            pieces.Add((a, b, rules));
        }

        // ③ 규칙이 같은 이웃 조각 병합(비랩 이웃 → 마지막·첫 조각의 랩 이웃 순).
        static bool SameRules(List<(int, double, double)> x, List<(int, double, double)> y)
        {
            if (x.Count != y.Count) return false;
            for (int i = 0; i < x.Count; i++)
                if (x[i].Item1 != y[i].Item1 || Math.Abs(x[i].Item2 - y[i].Item2) > 1e-12
                    || Math.Abs(x[i].Item3 - y[i].Item3) > 1e-12) return false;
            return true;
        }
        for (int i = pieces.Count - 1; i > 0; i--)
            if (pieces[i].A - pieces[i - 1].B <= eps && SameRules(pieces[i].R, pieces[i - 1].R))
            { pieces[i - 1] = (pieces[i - 1].A, pieces[i].B, pieces[i - 1].R); pieces.RemoveAt(i); }
        if (pieces.Count >= 2 && SameRules(pieces[0].R, pieces[pieces.Count - 1].R)
            && Math.Abs(Mod(pieces[pieces.Count - 1].B) - pieces[0].A) <= eps)
        {
            var last = pieces[pieces.Count - 1];
            pieces[pieces.Count - 1] = (last.A, last.B + (pieces[0].B - pieces[0].A), last.R);
            pieces.RemoveAt(0);
        }

        zones.Clear();
        foreach (var (a, b, r) in pieces)
        {
            var z = b - a >= total - eps
                ? new SlopeZone { T0 = 0.0, T1 = total }
                : new SlopeZone { T0 = Mod(a), T1 = Mod(b) };
            z.Rules.AddRange(r);
            zones.Add(z);
        }
    }

    /// <summary>옛 표현(시작단부터 끝단까지 수직)에서 만들기 — 번들 v6 이하 하위호환·옹벽 변환용.
    /// toBench가 끝이 아니면 그 다음 단부터 전역 구배로 되돌리는 규칙을 함께 넣는다(옛 ToBench와 동일 의미).</summary>
    public static SlopeZone Wall(double t0, double t1, int fromBench, int toBench, double minSlope, double baseSlope)
    {
        var z = new SlopeZone { T0 = t0, T1 = t1 };
        z.Rules.Add((Math.Max(fromBench, 0), minSlope, -1));                // 소단폭은 전역값 따름
        if (toBench < int.MaxValue - 1) z.Rules.Add((toBench + 1, baseSlope, -1));
        z.Normalize();
        return z;
    }
}

/// <summary>정지(절성토) 파라미터. 모든 길이 단위는 m.</summary>
public sealed class GradingParams
{
    // ── [절성토 분리 0803 — JACK] 단높이·소단폭을 구배(CutSlope/FillSlope)와 같은 방식으로 방향별 분리. ──
    //   대소단(TerraceInterval/TerraceWidth)은 공용 유지(산지전용허가법 15m 규정 — JACK 확정).

    /// <summary>절토 한 단(段)의 수직 높이 (기본 5m).</summary>
    public double CutBenchHeight { get; init; } = 5.0;

    /// <summary>성토 한 단(段)의 수직 높이 (기본 5m).</summary>
    public double FillBenchHeight { get; init; } = 5.0;

    /// <summary>절토 소단(小段) 폭 — 단 사이 평탄부 (기본 1m).</summary>
    public double CutBenchWidth { get; init; } = 1.0;

    /// <summary>성토 소단(小段) 폭 — 단 사이 평탄부 (기본 1m).</summary>
    public double FillBenchWidth { get; init; } = 1.0;

    /// <summary>방향별 단높이 — up=true 절토 / false 성토.</summary>
    public double BenchHeightOf(bool up) => up ? CutBenchHeight : FillBenchHeight;

    /// <summary>방향별 소단폭 — up=true 절토 / false 성토.</summary>
    public double BenchWidthOf(bool up) => up ? CutBenchWidth : FillBenchWidth;

    /// <summary>
    /// [절성토 분리] 단수 산정 기준 = 둘 중 '작은' 단높이. 작은 쪽이 같은 표고차를 오르는 데 더 많은 단을
    /// 쓰므로, 이 기준으로 단수를 잡아야 양쪽 다 원지반(데이라잇)에 닿는다. 큰 쪽 기준으로 잡으면 작은 쪽 사면이
    /// 도중에 끊겨 정지면이 잘리거나 구멍이 난다. (절토=성토면 종전과 완전히 동일 — 회귀 없음.)
    /// </summary>
    public double SmallerBenchHeight => Math.Min(CutBenchHeight, FillBenchHeight);

    /// <summary>[절성토 분리] 여유 마진용 — 둘 중 '큰' 단높이.</summary>
    public double LargerBenchHeight => Math.Max(CutBenchHeight, FillBenchHeight);

    /// <summary>절토 구배 n. 표기 1:n = 수직 1 : 수평 n (예 1:1.0).</summary>
    public double CutSlope { get; init; } = 1.0;

    /// <summary>성토 구배 n. 표기 1:n = 수직 1 : 수평 n (예 1:1.5).</summary>
    public double FillSlope { get; init; } = 1.5;

    /// <summary>격자 해상도 (기본 1m). 작을수록 정밀·느림.</summary>
    public double CellSize { get; init; } = 1.0;

    /// <summary>안전 최대 단수 — daylight를 못 만나도 이 단수에서 멈춰 무한 확장 방지.
    /// [0803] <see cref="MaxRise"/>가 지정되면 높이 예산은 그쪽이 맡고, 이 값은 옛 번들 폴백에만 쓰인다.</summary>
    public int MaxBenches { get; init; } = 50;

    /// <summary>
    /// [절성토 분리 0803] 사면 링을 만들 '수직 예산' (m). 0=미지정 → 종전대로 MaxBenches×단높이로 계산(옛 번들).
    /// ※ 단 '개수' 상한(MaxBenches)에 단높이를 곱해 높이 예산을 만들면, 단높이가 작은 쪽이 개수 상한에 걸리는
    ///   순간 예산이 함께 주저앉아 사면이 원지반에 닿기 전에 끊긴다(표고차 &gt; 48×작은단높이에서 발동).
    ///   그래서 '개수 상한'과 '높이 예산'을 분리한다 — 높이 예산은 단높이와 무관한 실제 표고차에서 나와야 한다.
    /// CreateGradingCommand.BuildParams가 (표고차 + 여유)로 채운다.
    /// </summary>
    public double MaxRise { get; init; } = 0;

    /// <summary>경계 둘레 샘플 간격 (m) — 정점 밀도. 작을수록 곡선 추종 좋고 폴리라인 많음.</summary>
    public double VertexSpacing { get; init; } = 2.0;

    /// <summary>
    /// 비탈 최소 구배 n (1:n). 구배 0(수직 옹벽) 입력 시 이 비율로 살짝 눕혀 TIN 붕괴를 막는다.
    /// 기본 0.05(JACK) — 0.05 미만은 Civil3D TIN이 예기치 못한 오류를 내는 사례가 있어 이 값을 하한으로 고정.
    /// (단높이 5m면 수평 0.25m 폭 — 사실상 수직 옹벽.)
    /// </summary>
    public double MinSlope { get; init; } = 0.05;

    /// <summary>비탈 최소 수평폭 절대 바닥 (m) — 단높이가 매우 작을 때만 작동하는 안전장치.</summary>
    public double MinFaceRun { get; init; } = 0.005;

    /// <summary>
    /// 사면형상 — 볼록(튀어나온) 모서리 처리. true=직각(마이터, 기본), false=라운드(원호).
    /// 직각 모드는 예각에서 <see cref="MiterLimit"/> 비율을 넘으면 자동으로 라운드로 폴백한다.
    /// [0805] 기본값은 GradingSettings.MiterConvex(직각)와 반드시 일치해야 한다 — 어긋나 있으면
    /// 명시 대입을 빠뜨린 코드가 '설정은 직각인데 결과는 라운드'를 조용히 만든다(직각/라운드는
    /// 옹벽 벽면 분할이 완전히 달라져 결과 차이가 극단적 — v17.6 옹벽 6장↔163장).
    /// </summary>
    public bool MiterConvex { get; init; } = true;

    /// <summary>직각(마이터) 모서리 최대 연장 비율 — 모서리 길이 ÷ 단거리. 이보다 뾰족하면 라운드로 폴백.</summary>
    public double MiterLimit { get; init; } = 2.0;

    /// <summary>
    /// 계단식 산지 적용 (산지전용허가법). true면 사면 수직 누적이 <see cref="TerraceInterval"/>(기본 15m)에
    /// 닿는 단마다 일반 소단 대신 폭 <see cref="TerraceWidth"/>(기본 15m)의 대소단(큰 평탄)을 넣고 누적을 리셋한다.
    /// 단높이로 간격이 딱 안 떨어지면 마지막 사면을 '간격−누적'만큼 자투리로 올려 정확히 간격에 맞춘다.
    /// </summary>
    public bool MountainTerrace { get; init; } = false;

    /// <summary>대소단 수직 간격 (m, 기본 15) — 누적 사면높이가 이 값에 닿으면 대소단 삽입.</summary>
    public double TerraceInterval { get; init; } = 15.0;

    /// <summary>대소단(큰 평탄) 폭 (m, 기본 15).</summary>
    public double TerraceWidth { get; init; } = 15.0;

    public void Validate()
    {
        if (CutBenchHeight <= 0) throw new ArgumentException("절토 단높이(CutBenchHeight)는 0보다 커야 합니다.");
        if (FillBenchHeight <= 0) throw new ArgumentException("성토 단높이(FillBenchHeight)는 0보다 커야 합니다.");
        if (CutBenchWidth < 0) throw new ArgumentException("절토 소단폭(CutBenchWidth)은 0 이상이어야 합니다.");
        if (FillBenchWidth < 0) throw new ArgumentException("성토 소단폭(FillBenchWidth)은 0 이상이어야 합니다.");
        if (CutSlope < 0 || FillSlope < 0) throw new ArgumentException("구배는 0 이상이어야 합니다.");
        if (CellSize <= 0) throw new ArgumentException("격자(CellSize)는 0보다 커야 합니다.");
        if (MaxBenches <= 0) throw new ArgumentException("최대 단수(MaxBenches)는 1 이상이어야 합니다.");
        if (VertexSpacing <= 0) throw new ArgumentException("정점 간격(VertexSpacing)은 0보다 커야 합니다.");
        if (MinSlope < 0) throw new ArgumentException("최소 구배(MinSlope)는 0 이상이어야 합니다.");
        if (MinFaceRun <= 0) throw new ArgumentException("최소 비탈폭(MinFaceRun)은 0보다 커야 합니다.");
        if (MountainTerrace)
        {
            if (TerraceInterval <= 0) throw new ArgumentException("대소단 수직 간격(TerraceInterval)은 0보다 커야 합니다.");
            if (TerraceWidth < 0) throw new ArgumentException("대소단 폭(TerraceWidth)은 0 이상이어야 합니다.");
        }
    }
}
