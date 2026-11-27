using DH.Pipe.Core.Geom;

namespace DH.Pipe.Core.Draw;

public enum DrawKind { Line, Poly, Circle, Text, Fill }

/// <summary>그림 하나(선 · 꺾은선 · 원 · 글자 · 채움). 화면 미리보기와 도면이 같은 목록을 그린다.</summary>
public sealed class DrawItem
{
    public DrawKind Kind { get; set; }
    /// <summary>레이어 열쇠(<see cref="Lay"/>).</summary>
    public string Layer { get; set; } = Lay.Text;
    public List<P2> Pts { get; set; } = new();
    public bool Closed { get; set; }
    public double R { get; set; }
    public string Text { get; set; } = "";
    /// <summary>글자 높이(도면 단위).</summary>
    public double H { get; set; }
    /// <summary>글자 돌림(도).</summary>
    public double Rot { get; set; }
    /// <summary>가로 맞춤: 0 왼쪽 · 1 가운데 · 2 오른쪽.</summary>
    public int Align { get; set; }
    /// <summary>세로 맞춤: 0 밑줄 · 1 가운데.</summary>
    public int VAlign { get; set; }
    /// <summary>채움 무늬: SOLID · SAND · GRAVEL · CONC.</summary>
    public string Pattern { get; set; } = "";
    /// <summary>채움 안의 구멍(관) — 원 목록 (x, y, r).</summary>
    public List<(double X, double Y, double R)> Holes { get; set; } = new();
}

/// <summary>레이어 열쇠와 도면 레이어 이름 · 색 · 선종류. 색 번호는 현장 샘플 A 횡단도 · 종단도와 맞춰 같은 CTB 로 찍히게 한다.</summary>
public static class Lay
{
    public const string Ground = "지반선";
    public const string Trench = "터파기";
    public const string Shore = "가시설";
    public const string Water = "지하수위추정선";
    public const string Depth = "깊이구분선";
    public const string Strata = "지층선";
    public const string Pipe = "관";
    public const string Pole = "폴대";
    public const string Text = "문자";
    public const string Table = "토적표";
    public const string Hatch = "해치";
    public const string Pave = "포장";
    public const string Frame = "도곽";
    public const string Title = "제목";
    public const string Leader = "지시선";
    public const string Band = "밴드";
    public const string BandText = "밴드문자";
    public const string Obst = "지장물";
    public const string Appt = "부속";
    public const string Dim = "치수";
    public const string Warn = "경고";

    /// <summary>(열쇠, 색 번호, 선종류).</summary>
    public static readonly (string Key, short Aci, string Linetype)[] Table0 =
    {
        (Ground, 1, "Continuous"), (Trench, 3, "Continuous"), (Shore, 6, "Continuous"), (Water, 5, "HIDDEN"),
        (Depth, 8, "HIDDEN"), (Strata, 8, "DASHDOT"), (Pipe, 4, "Continuous"), (Pole, 1, "Continuous"),
        (Text, 7, "Continuous"), (Table, 3, "Continuous"), (Hatch, 9, "Continuous"), (Pave, 8, "Continuous"),
        (Frame, 7, "Continuous"), (Title, 7, "Continuous"), (Leader, 1, "Continuous"), (Band, 7, "Continuous"),
        (BandText, 2, "Continuous"), (Obst, 6, "Continuous"), (Appt, 2, "Continuous"), (Dim, 1, "Continuous"),
        (Warn, 1, "Continuous"),
    };

    /// <summary>도면 레이어 이름. group = «횡단» · «종단».</summary>
    public static string Name(string group, string key) => $"DHP-{group}-{key}";
}

/// <summary>그림 목록.</summary>
public sealed class DrawList
{
    public List<DrawItem> Items { get; } = new();

    public void Line(string layer, double x1, double y1, double x2, double y2)
        => Items.Add(new DrawItem { Kind = DrawKind.Line, Layer = layer, Pts = { new P2(x1, y1), new P2(x2, y2) } });

    public void Poly(string layer, IEnumerable<P2> pts, bool closed = false)
    {
        var it = new DrawItem { Kind = DrawKind.Poly, Layer = layer, Closed = closed };
        it.Pts.AddRange(pts);
        if (it.Pts.Count >= 2) Items.Add(it);
    }

    public void Rect(string layer, double x0, double y0, double x1, double y1)
        => Poly(layer, new[] { new P2(x0, y0), new P2(x1, y0), new P2(x1, y1), new P2(x0, y1) }, true);

    public void Circle(string layer, double x, double y, double r)
        => Items.Add(new DrawItem { Kind = DrawKind.Circle, Layer = layer, Pts = { new P2(x, y) }, R = r });

    public DrawItem Text(string layer, double x, double y, double h, string text, int align = 0, double rot = 0, int valign = 0)
    {
        var it = new DrawItem { Kind = DrawKind.Text, Layer = layer, Pts = { new P2(x, y) }, H = h, Text = text, Align = align, Rot = rot, VAlign = valign };
        Items.Add(it);
        return it;
    }

    public DrawItem Fill(string layer, IEnumerable<P2> pts, string pattern)
    {
        var it = new DrawItem { Kind = DrawKind.Fill, Layer = layer, Closed = true, Pattern = pattern };
        it.Pts.AddRange(pts);
        Items.Add(it);
        return it;
    }

    /// <summary>다른 목록을 (dx, dy) 옮겨 붙인다.</summary>
    public void Add(DrawList other, double dx, double dy)
    {
        foreach (var o in other.Items)
        {
            var it = new DrawItem
            {
                Kind = o.Kind, Layer = o.Layer, Closed = o.Closed, R = o.R, Text = o.Text, H = o.H, Rot = o.Rot,
                Align = o.Align, VAlign = o.VAlign, Pattern = o.Pattern,
            };
            foreach (var p in o.Pts) it.Pts.Add(new P2(p.X + dx, p.Y + dy));
            foreach (var h in o.Holes) it.Holes.Add((h.X + dx, h.Y + dy, h.R));
            Items.Add(it);
        }
    }

    /// <summary>그림 범위(글자는 대략).</summary>
    public (double X0, double Y0, double X1, double Y1) Bounds()
    {
        double x0 = double.MaxValue, y0 = double.MaxValue, x1 = double.MinValue, y1 = double.MinValue;
        void Put(double x, double y)
        {
            if (x < x0) x0 = x;
            if (y < y0) y0 = y;
            if (x > x1) x1 = x;
            if (y > y1) y1 = y;
        }
        foreach (var it in Items)
        {
            if (it.Kind == DrawKind.Circle) { Put(it.Pts[0].X - it.R, it.Pts[0].Y - it.R); Put(it.Pts[0].X + it.R, it.Pts[0].Y + it.R); continue; }
            if (it.Kind == DrawKind.Text)
            {
                double w = TextWidth(it.Text, it.H);
                double ax = it.Align == 1 ? -w * 0.5 : it.Align == 2 ? -w : 0;
                double c = Math.Cos(it.Rot * Math.PI / 180), s = Math.Sin(it.Rot * Math.PI / 180);
                foreach (var (lx, ly) in new[] { (ax, 0.0), (ax + w, 0.0), (ax, it.H), (ax + w, it.H) })
                    Put(it.Pts[0].X + lx * c - ly * s, it.Pts[0].Y + lx * s + ly * c);
                continue;
            }
            foreach (var p in it.Pts) Put(p.X, p.Y);
        }
        if (x0 == double.MaxValue) return (0, 0, 0, 0);
        return (x0, y0, x1, y1);
    }

    /// <summary>글자 폭 어림(한글 = 높이 · 영숫자 = 높이의 0.6).</summary>
    public static double TextWidth(string s, double h)
    {
        double w = 0;
        foreach (char ch in s) w += ch > 0x2E7F ? 1.0 : 0.62;
        return w * h;
    }
}
