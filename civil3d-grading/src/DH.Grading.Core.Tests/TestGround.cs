using DH.Grading.Core;

namespace DH.Grading.Core.Tests;

/// <summary>평탄 원지반 — 어디서나 동일 표고.</summary>
public sealed class FlatGround(double z) : IGroundSurface
{
    public bool TryGetElevation(double x, double y, out double elev) { elev = z; return true; }
}

/// <summary>경사 원지반 평면 z = a·x + b·y + c.</summary>
public sealed class TiltedGround(double a, double b, double c) : IGroundSurface
{
    public bool TryGetElevation(double x, double y, out double z) { z = a * x + b * y + c; return true; }
}

/// <summary>울퉁불퉁(파상) 원지반 — 평탄 baseZ에 진폭 amp의 부드러운 굴곡(실 TIN의 미세 떨림 모사).</summary>
public sealed class UndulatingGround(double baseZ, double amp) : IGroundSurface
{
    public bool TryGetElevation(double x, double y, out double z)
    {
        z = baseZ + amp * (System.Math.Sin(x * 0.7) * System.Math.Cos(y * 0.55) + 0.5 * System.Math.Sin(x * 1.9 + y * 1.3));
        return true;
    }
}
