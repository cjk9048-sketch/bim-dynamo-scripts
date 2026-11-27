using DH.Pipe.Core.Geom;

namespace DH.Pipe.Core.Quantity;

/// <summary>시험용 가짜 지형 · 노선(미리보기 도구와 자가 시험 명령이 같이 쓴다).</summary>
public static class DemoTerrain
{
    /// <summary>높이: 완만한 오르막 + 굴곡 + x = 320 근처 하천 골.</summary>
    public static double Z(double x, double y)
    {
        double z = 20 + 0.012 * x + 1.4 * Math.Sin(x / 45.0) + 0.5 * Math.Cos(y / 30.0) + 0.12 * Math.Sin(x / 4.0);
        double river = Math.Exp(-Math.Pow((x - 320) / 12.0, 2));
        return z - 3.2 * river;
    }

    public const double X0 = -40, X1 = 660, Y0 = -80, Y1 = 160;

    /// <summary>노선 꼭짓점(직선 넷 · 꺾인 점 셋).</summary>
    public static readonly P2[] Plan = { new(0, 0), new(200, 0), new(260, 60), new(420, 60), new(600, 20) };
}
