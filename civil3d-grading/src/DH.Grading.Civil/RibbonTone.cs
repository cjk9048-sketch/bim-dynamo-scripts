using System;
using System.Collections.Generic;
using System.Windows.Media;

namespace DH.Grading.Civil;

/// <summary>★[v117.12 · JACK 1112 «UI 단추 — 계획지표면 · 진입도로 · 공통처럼 카테고리별로 모양은 유지하되 색상을 공통된 느낌으로 구분감»]
/// 리본 칸(패널)마다 색 계열 하나 — 단추 그림(<see cref="RibbonIcons"/>)의 모양은 그대로, 색만 그 칸 색상(色相)으로 옮긴다(밝기 · 채도는 그대로 → 입체감 유지).
/// <para>그대로 두는 색: 무채색(흰 · 회색 · 검정 — 채도 0.12 아래) · 빨간 «지우기 · 끄기» 표시(색상 350°~12° · 채도 0.45 넘음 — 뜻이 있는 색).</para></summary>
internal static class RibbonTone
{
    /// <summary>칸 제목 → 색상(°). 없는 칸은 null(그대로).</summary>
    internal static double? HueOf(string? panel) => panel switch
    {
        "계획지표면" => 145,   // 초록 — 계획
        "진입도로" => 210,     // 파랑 — 도로
        "공통" => 180,         // 청록 — 회사 색 곁
        "원지반 작성" => 28,   // 갈색 — 땅
        "기타" => 270,         // 보라
        "내보내기" => 330,     // 자홍 — 빨강(지우기)과 떨어지게
        _ => null,
    };

    /// <summary>그림(벡터)의 색을 그 색상으로 옮긴 새 그림(얼림) — 못 하면 null.</summary>
    internal static ImageSource? Apply(ImageSource? img, double hue)
    {
        if (img is not DrawingImage di) return null;
        try
        {
            var copy = di.Clone();
            Walk(copy.Drawing, hue);
            copy.Freeze();
            return copy;
        }
        catch { return null; }
    }

    static void Walk(Drawing? d, double hue)
    {
        switch (d)
        {
            case DrawingGroup g: foreach (var c in g.Children) Walk(c, hue); break;
            case GeometryDrawing gd:
                gd.Brush = Tone(gd.Brush, hue);
                if (gd.Pen != null) gd.Pen.Brush = Tone(gd.Pen.Brush, hue);
                break;
        }
    }

    static Brush? Tone(Brush? b, double hue)
    {
        switch (b)
        {
            case SolidColorBrush s: return new SolidColorBrush(Shift(s.Color, hue)) { Opacity = s.Opacity };
            case GradientBrush gb:
                var c = gb.Clone();
                foreach (var st in c.GradientStops) st.Color = Shift(st.Color, hue);
                return c;
            default: return b;
        }
    }

    static Color Shift(Color c, double hue)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        double mx = Math.Max(r, Math.Max(g, b)), mn = Math.Min(r, Math.Min(g, b)), l = (mx + mn) / 2, dl = mx - mn;
        if (dl < 1e-6) return c;
        double s = l > 0.5 ? dl / (2 - mx - mn) : dl / (mx + mn);
        double h = mx == r ? (g - b) / dl + (g < b ? 6 : 0) : mx == g ? (b - r) / dl + 2 : (r - g) / dl + 4; h *= 60;
        if (s < 0.12) return c;                                   // 무채색 그대로
        if ((h >= 350 || h <= 12) && s > 0.45) return c;          // 빨간 지우기 · 끄기 표시 그대로
        double q = l < 0.5 ? l * (1 + s) : l + s - l * s, p = 2 * l - q, hk = hue / 360.0;
        double Hue(double t) { if (t < 0) t += 1; if (t > 1) t -= 1; return t < 1.0 / 6 ? p + (q - p) * 6 * t : t < 0.5 ? q : t < 2.0 / 3 ? p + (q - p) * (2.0 / 3 - t) * 6 : p; }
        return Color.FromArgb(c.A, (byte)Math.Round(Hue(hk + 1.0 / 3) * 255), (byte)Math.Round(Hue(hk) * 255), (byte)Math.Round(Hue(hk - 1.0 / 3) * 255));
    }
}
