using Autodesk.Civil.DatabaseServices;
using DH.Grading.Core;

namespace DH.Grading.Civil;

/// <summary>
/// Civil3D TinSurface → Core의 IGroundSurface 어댑터.
/// Core(순수 로직)가 AutoCAD에 의존하지 않도록, 원지반 표고 조회만 이 클래스가 담당한다.
/// </summary>
public sealed class Civil3dGroundSurface : IGroundSurface
{
    private readonly TinSurface _surface;

    public Civil3dGroundSurface(TinSurface surface) => _surface = surface;

    public bool TryGetElevation(double x, double y, out double z)
    {
        try
        {
            // 표면 범위를 벗어나면 예외 → 정지 대상 아님으로 처리
            z = _surface.FindElevationAtXY(x, y);
            return !double.IsNaN(z);
        }
        catch
        {
            z = 0;
            return false;
        }
    }

    /// <summary>표면의 최저/최고 표고 — 마진(필요 단수) 추정에 사용.</summary>
    public (double Min, double Max) ElevationRange()
    {
        var gp = _surface.GetGeneralProperties();
        return (gp.MinimumElevation, gp.MaximumElevation);
    }
}
