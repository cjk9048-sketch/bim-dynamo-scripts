using DH.Pipe.Core.Geom;
using DH.Pipe.Core.Quantity;

namespace DH.Pipe.Core.Draw;

/// <summary>
/// 도면 한 장의 자리(종이 mm) — 부지정지 도면화와 같은 약속: A1(841 × 594) · 그림 자리 = 왼쪽 25 · 아래 50 에서 796 × 524 ·
/// 그 맨 위 40 은 제목 띠. 모형 공간의 장(횡단도 · 종단도면)을 이 자리에 맞춰 그려 두면, 배치탭에서는 도곽을 놓고
/// «그림 자리 − 제목 띠»만 뷰포트로 보면 된다(모형 장의 테두리 · 제목은 뷰포트 밖).
/// </summary>
public static class SheetGeom
{
    public const double PaperW = 841, PaperH = 594;
    public const double DrawL = 25, DrawB = 50, DrawR = 821, DrawT = 574;
    /// <summary>그림 자리 위쪽의 제목 띠(mm).</summary>
    public const double TitleBand = 40;
    /// <summary>뷰포트가 보는 자리의 윗선(mm).</summary>
    public const double ContentT = DrawT - TitleBand;
    public const double ContentW = DrawR - DrawL;
    public const double ContentH = ContentT - DrawB;
    /// <summary>모형 공간에서 장과 장 사이(mm).</summary>
    public const double ModelGap = 60;
    /// <summary>그림과 그림 자리 테두리 사이(mm).</summary>
    public const double Pad = 3;

    /// <summary>모형 공간의 i번 장 왼쪽 아래(m). f = 종이 1 mm 의 모형 길이(축척 ÷ 1000).</summary>
    public static double OriginX(int i, double f) => i * (PaperW + ModelGap) * f;

    /// <summary>모형 장의 꾸밈: 바깥 · 테두리 · 아래 띠 · 위 제목 · 축척 · «i / n». 배치탭 뷰포트에는 안 보인다.</summary>
    public static void Decorate(DrawList d, double ox, double oy, double f, string title, string scaleText, int index, int count)
    {
        d.Rect(Lay.Frame, ox, oy, ox + PaperW * f, oy + PaperH * f);
        d.Rect(Lay.Frame, ox + DrawL * f, oy + 20 * f, ox + DrawR * f, oy + DrawT * f);
        d.Line(Lay.Frame, ox + DrawL * f, oy + DrawB * f, ox + DrawR * f, oy + DrawB * f);
        double cx = ox + 0.5 * (DrawL + DrawR) * f, ty = oy + (ContentT + 0.5 * TitleBand) * f;
        string name = count > 1 ? $"{title}({index + 1})" : title;
        d.Text(Lay.Title, cx, ty - 1 * f, 8 * f, name, 1);
        d.Text(Lay.Title, cx + (DrawList.TextWidth(name, 8 * f) * 0.5 + 8 * f), ty - 9 * f, 4 * f, scaleText, 0);
        d.Text(Lay.Title, ox + (DrawR - 6) * f, oy + 36 * f, 5 * f, name, 2);
        d.Text(Lay.Title, ox + (DrawR - 6) * f, oy + 28 * f, 3 * f, $"{index + 1} / {count}", 2);
    }
}

/// <summary>평면도 한 장이 보는 곳.</summary>
/// <param name="StaFrom">이 장의 시작 측점.</param>
/// <param name="StaTo">끝 측점.</param>
/// <param name="Cx">보는 곳 가운데(세계 좌표).</param>
/// <param name="Cy">보는 곳 가운데(세계 좌표).</param>
/// <param name="RotRad">종이에서 반시계로 돌릴 각(라디안) — 이 구간의 시작 → 끝이 왼쪽 → 오른쪽이 되게.</param>
/// <param name="WidthM">돌린 뒤 노선이 차지하는 가로(m).</param>
/// <param name="HeightM">돌린 뒤 노선이 차지하는 세로(m).</param>
public sealed record PlanWindow(double StaFrom, double StaTo, double Cx, double Cy, double RotRad, double WidthM, double HeightM);

/// <summary>평면도 · 종단면도의 장 나눔(둘이 같은 측점 구간을 쓴다 — 평면 k번 장 = 종단 k번 장).</summary>
public static class PlanSheets
{
    /// <summary>
    /// 한 장에 담는 노선 길이(m): 그림 자리 폭에서 종단 밴드 제목 칸(24) · 오른쪽 여백(4) · 양쪽 띄움을 뺀 길이를 측점 간격의 배수로 내림.
    /// </summary>
    public static double SheetLength(double hScale, double interval)
    {
        double f = Math.Max(1, hScale) / 1000.0;
        double room = (SheetGeom.ContentW - 24 - 4 - 2 * SheetGeom.Pad) * f;
        if (interval <= 0) return room;
        double n = Math.Floor(room / interval + 1e-9);
        return n >= 1 ? n * interval : room;
    }

    /// <summary>끝에 남은 토막이 이보다 짧으면(m) 앞 장에 붙인다 — 거의 빈 장이 하나 더 생겨 도면번호가 밀리지 않게.</summary>
    public const double TailMin = 0.05;

    /// <summary>[s0, s1] 을 장 길이로 나눈 구간들. 끝에 남는 아주 짧은 토막(<see cref="TailMin"/> 미만)은 앞 장에 붙인다.</summary>
    public static List<(double A, double B)> Ranges(double s0, double s1, double sheetLen)
    {
        var res = new List<(double, double)>();
        if (s1 - s0 < 1e-6 || sheetLen <= 1e-6) return res;
        for (double a = s0; a < s1 - 1e-6; a += sheetLen)
        {
            double b = Math.Min(s1, a + sheetLen);
            if (s1 - b < TailMin) b = s1;
            res.Add((a, b));
            if (b >= s1 - 1e-9) break;
        }
        return res;
    }

    /// <summary>
    /// 장마다 보는 곳: 그 구간의 시작 → 끝 방향이 종이에서 왼쪽 → 오른쪽이 되게 돌리고, 돌린 좌표에서 노선이 차지하는 네모의 가운데를 본다
    /// (굽은 노선도 장 가운데에 오게).
    /// </summary>
    public static List<PlanWindow> Windows(PlanLine plan, IReadOnlyList<(double A, double B)> ranges)
    {
        var res = new List<PlanWindow>();
        foreach (var (a, b) in ranges)
        {
            var pa = plan.PointAt(a); var pb = plan.PointAt(b);
            double th = Math.Atan2(pb.Y - pa.Y, pb.X - pa.X);
            if (Math.Abs(pb.X - pa.X) + Math.Abs(pb.Y - pa.Y) < 1e-9) th = 0;
            double c = Math.Cos(-th), s = Math.Sin(-th);
            double u0 = double.MaxValue, u1 = double.MinValue, v0 = double.MaxValue, v1 = double.MinValue;
            void Take(P2 p)
            {
                double u = p.X * c - p.Y * s, v = p.X * s + p.Y * c;
                u0 = Math.Min(u0, u); u1 = Math.Max(u1, u); v0 = Math.Min(v0, v); v1 = Math.Max(v1, v);
            }
            Take(pa);
            Take(pb);
            double acc = 0;
            for (int i = 0; i < plan.Pts.Count; i++)
            {
                if (i > 0) acc += Math.Sqrt(Math.Pow(plan.Pts[i].X - plan.Pts[i - 1].X, 2) + Math.Pow(plan.Pts[i].Y - plan.Pts[i - 1].Y, 2));
                if (acc > a + 1e-9 && acc < b - 1e-9) Take(plan.Pts[i]);
            }
            double um = 0.5 * (u0 + u1), vm = 0.5 * (v0 + v1);
            // 돌린 좌표의 가운데를 세계 좌표로 되돌림(+th 회전)
            double cx = um * Math.Cos(th) - vm * Math.Sin(th), cy = um * Math.Sin(th) + vm * Math.Cos(th);
            res.Add(new PlanWindow(a, b, cx, cy, -th, u1 - u0, v1 - v0));
        }
        return res;
    }
}
