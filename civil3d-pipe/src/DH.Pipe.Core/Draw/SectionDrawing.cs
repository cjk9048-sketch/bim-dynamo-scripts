using System.Globalization;
using DH.Pipe.Core.Geom;
using DH.Pipe.Core.Model;
using DH.Pipe.Core.Section;

namespace DH.Pipe.Core.Draw;

/// <summary>단면 한 칸의 그림(왼쪽 아래 = (0, 0)).</summary>
public sealed class XsecCell
{
    public DrawList Draw { get; } = new();
    public double W { get; set; }
    public double H { get; set; }
}

/// <summary>횡단도 여러 장.</summary>
public sealed class SheetSet
{
    public DrawList Draw { get; } = new();
    public int Sheets { get; set; }
    public int Cells { get; set; }
    public int PerSheet { get; set; }
    public double SheetW { get; set; }
    public double SheetH { get; set; }
    /// <summary>축척의 분모(1:N 의 N).</summary>
    public double Scale { get; set; }
    /// <summary>장마다의 왼쪽 아래(모형 m — 그린 자리 기준 · 도면에 넣을 때 옮긴 만큼 더한다).</summary>
    public List<(double X, double Y)> Origins { get; } = new();
    /// <summary>종단: 장마다의 측점 구간.</summary>
    public List<(double A, double B)> Ranges { get; } = new();
    public List<string> Notes { get; } = new();
}

/// <summary>
/// 횡단도 그리기 — 현장 샘플 A 토공 횡단면도 꼴: 왼쪽 폴대 · 측점 글 · 단면 · 오른쪽 토적표.
/// 미리보기 창과 도면이 이 그림 목록을 함께 쓴다.
/// </summary>
public static class SectionDrawing
{
    private static string F2(double v) => v.ToString("0.00", CultureInfo.InvariantCulture);

    /// <summary>토적표에 넣을 행(이 노선에 값이 있는 것만 · 순서 고정).</summary>
    public static List<string> TableKeys(IEnumerable<SectionResult> secs)
    {
        var list = secs.ToList();
        var keys = new List<string>();
        foreach (string k in Quantity.EarthTable.AreaOrder) if (list.Any(r => r.A(k) > 1e-9)) keys.Add(k);
        foreach (string k in new[] { Q.CutAsp, Q.CutCon, Q.CutBlk, Q.WallL, Q.WallR })
            if (list.Any(r => r.L(k) > 1e-9)) keys.Add(k);
        return keys;
    }

    /// <summary>단면 한 칸. scale = 축척 분모(100 → 1:100) · 글자 크기는 종이 mm 로 정해 축척을 곱한다.</summary>
    public static XsecCell Cell(SectionResult r, IReadOnlyList<string> tableKeys, double depthLimit, double scale, bool table = true)
    {
        double f = scale / 1000.0;                  // 종이 1 mm = 도면 f m
        var d = new DrawList();
        var ground = r.Lines.FirstOrDefault(l => l.Key == "GROUND");
        double pad = Math.Max(1.0, 12 * f);
        double xa, xb;
        if (!double.IsNaN(r.XLeft) && !double.IsNaN(r.XRight)) { xa = r.XLeft - pad; xb = r.XRight + pad; }
        else { double w = Math.Max(3.0, 30 * f); xa = -w; xb = w; }
        xa = Math.Max(xa, r.DomainX0);
        xb = Math.Min(xb, r.DomainX1);
        if (xb - xa < 1e-3) { xa = -3; xb = 3; }

        double zmin = double.MaxValue, zmax = double.MinValue;
        Pl? gpl = null;
        if (ground != null && ground.Pts.Count >= 2)
        {
            gpl = Pl.From(ground.Pts.Select(p => (p.X, p.Y))).Clip(xa, xb);
            d.Poly(Lay.Ground, gpl.Points().Select(p => new P2(p.X, p.Y)));
            zmin = gpl.MinY(); zmax = gpl.MaxY();
        }
        foreach (var c in r.Circles) { zmin = Math.Min(zmin, c.Z - c.R); zmax = Math.Max(zmax, c.Z + c.R); }
        foreach (var l in r.Lines) if (l.Key is "TRENCH" or "SHORE") foreach (var p in l.Pts) { zmin = Math.Min(zmin, p.Y); zmax = Math.Max(zmax, p.Y); }
        if (zmin == double.MaxValue) { zmin = 0; zmax = 1; }
        zmin -= 0.5; zmax += 0.5;
        double span = zmax - zmin;
        double step = span <= 6 ? 1 : span <= 14 ? 2 : 5;
        double poleB = Math.Floor(zmin / step) * step, poleT = Math.Ceiling(zmax / step) * step;

        // ---- 채움
        foreach (var fill in r.Fills)
        {
            string pat = fill.Key switch
            {
                Q.PavAsp or Q.PavCon or Q.PavBlk => "SOLID",
                Q.Subbase => "GRAVEL",
                Q.Bed or Q.Zone => "SAND",
                Q.Encase => "CONC",
                _ => "",
            };
            if (pat == "") continue;
            string layer = pat == "SOLID" ? Lay.Pave : Lay.Hatch;
            foreach (var poly in fill.Polys)
            {
                var it = d.Fill(layer, poly, pat);
                if (fill.Key is Q.Zone or Q.Encase or Q.Bed)
                    foreach (var c in r.Circles) if (c.Key == "PIPE") it.Holes.Add((c.X, c.Z, c.R));
            }
        }
        // 층 경계선(되메우기 조각 테두리)
        foreach (var fill in r.Fills)
        {
            if (fill.Key is not (Q.Zone or Q.Bed or Q.Upper or Q.Subbase)) continue;
            foreach (var poly in fill.Polys) d.Poly(Lay.Hatch, poly, true);
        }

        // ---- 선
        foreach (var l in r.Lines)
        {
            switch (l.Key)
            {
                case "TRENCH": d.Poly(Lay.Trench, l.Pts); break;
                case "SHORE": d.Poly(Lay.Shore, l.Pts); break;
                case "WATER":
                    d.Poly(Lay.Water, l.Pts);
                    d.Text(Lay.Water, l.Pts[^1].X, l.Pts[^1].Y + 0.8 * f, 2.0 * f, "지하수위", 2);
                    break;
                case "DEPTH":
                    d.Poly(Lay.Depth, l.Pts);
                    d.Text(Lay.Depth, l.Pts[0].X, l.Pts[0].Y + 0.8 * f, 2.0 * f, $"{depthLimit:0.#}m 구분선");
                    break;
                case "STRATA1":
                case "STRATA2":
                case "LAYER":
                    d.Poly(Lay.Strata, l.Pts);
                    d.Text(Lay.Strata, l.Pts[0].X - 1 * f, l.Pts[0].Y - 3 * f, 2.0 * f, l.Label.Length > 0 ? l.Label : l.Key == "STRATA2" ? "연암" : "풍화암", 2);
                    break;
            }
        }
        foreach (var c in r.Circles) d.Circle(c.Key == "CASING" ? Lay.Shore : Lay.Pipe, c.X, c.Z, c.R);

        // ---- 중심 표시 · 공법 · 바닥폭
        double gcl = gpl?.At(0) ?? r.GroundCl;
        d.Line(Lay.Dim, 0, gcl + 1 * f, 0, gcl + 7 * f);
        d.Text(Lay.Dim, 0, gcl + 8 * f, 2.2 * f, "C/L", 1);

        if (r.L(Q.BotWidth) > 0 && r.Method != MethodKind.Jacking)
            d.Text(Lay.Dim, 0, r.Bottom - 4 * f, 2.0 * f, "B=" + F2(r.L(Q.BotWidth)), 1);
        double wy = zmax + 12 * f;
        foreach (string msg in r.Messages)
        {
            d.Text(Lay.Warn, xa, wy, 2.5 * f, "※ " + msg);
            wy += 4 * f;
        }

        // ---- 폴대
        double xp = xa - 8 * f;
        d.Line(Lay.Pole, xp, poleB, xp, poleT);
        for (double z = poleB; z <= poleT + 1e-9; z += step)
        {
            d.Line(Lay.Pole, xp - 1.5 * f, z, xp, z);
            d.Text(Lay.Pole, xp - 2.5 * f, z - 1.0 * f, 2.2 * f, F2(z), 2);
        }
        double tx = xp - 14 * f, ty = poleB - 6 * f;
        d.Text(Lay.Text, tx, poleT + 3 * f, 2.5 * f, MethodSpan.NameOf(r.Method));     // 공법 이름 = 폴대 위(C/L 표시와 안 겹치게)
        d.Text(Lay.Text, tx, ty, 3.0 * f, "NO. " + r.Label);
        d.Text(Lay.Text, tx, ty - 4.5 * f, 2.5 * f, "GH " + F2(r.GroundCl));
        d.Text(Lay.Text, tx, ty - 8.5 * f, 2.5 * f, "IL " + F2(r.Invert));
        if (r.Len.ContainsKey(Q.Cover)) d.Text(Lay.Text, tx, ty - 12.5 * f, 2.5 * f, "토피 " + F2(r.L(Q.Cover)));

        // ---- 토적표(미리보기 창은 표 없이 단면만 크게)
        double x0 = xb + 8 * f, w1 = 42 * f, w2 = 17 * f, rh = 5 * f;
        if (!table) tableKeys = Array.Empty<string>();
        double top = poleT;
        int n = table ? tableKeys.Count + 1 : 0;
        if (table)
        {
        d.Rect(Lay.Table, x0, top - n * rh, x0 + w1 + w2, top);
        d.Line(Lay.Table, x0 + w1, top - n * rh, x0 + w1, top);
        for (int i = 1; i < n; i++) d.Line(Lay.Table, x0, top - i * rh, x0 + w1 + w2, top - i * rh);
        d.Text(Lay.Text, x0 + w1 * 0.5, top - rh + 1.3 * f, 2.5 * f, "측 점", 1);
        d.Text(Lay.Text, x0 + w1 + w2 - 1.2 * f, top - rh + 1.3 * f, 2.3 * f, r.Label, 2);
        }
        for (int i = 0; i < tableKeys.Count; i++)
        {
            string k = tableKeys[i];
            double v = Q.IsLen(k) ? r.L(k) : r.A(k);
            double y = top - (i + 2) * rh + 1.3 * f;
            d.Text(Lay.Text, x0 + 1.5 * f, y, 2.3 * f, Q.NameOf(k, depthLimit));
            d.Text(Lay.Text, x0 + w1 + w2 - 1.2 * f, y, 2.3 * f, v > 0.004 ? F2(v) : "-", 2);
        }

        // ---- 칸 크기에 맞춰 옮김
        var (bx0, by0, bx1, by1) = d.Bounds();
        double m = 4 * f;
        var cell = new XsecCell { W = bx1 - bx0 + 2 * m, H = by1 - by0 + 2 * m };
        cell.Draw.Add(d, -bx0 + m, -by0 + m);
        return cell;
    }

    /// <summary>횡단도 여러 장. A1(841 × 594 mm) 테두리 안에 칸을 위 → 아래 · 왼 → 오로 채운다.</summary>
    public static SheetSet Sheets(IReadOnlyList<SectionResult> secs, IReadOnlyList<string> tableKeys, double depthLimit, double scale, string title)
    {
        double f = scale / 1000.0;
        var set = new SheetSet { SheetW = SheetGeom.PaperW * f, SheetH = SheetGeom.PaperH * f, Scale = scale };
        var cells = secs.Select(s => Cell(s, tableKeys, depthLimit, scale)).ToList();
        set.Cells = cells.Count;
        if (cells.Count == 0) return set;
        double gap = 6 * f;
        // 그림 자리 = 도곽의 그림 자리에서 제목 띠를 뺀 곳(배치탭 뷰포트가 보는 곳과 같다)
        double left = SheetGeom.DrawL * f, topM = (SheetGeom.PaperH - SheetGeom.ContentT) * f;
        double uw = SheetGeom.ContentW * f, uh = SheetGeom.ContentH * f;
        if (cells.Any(c => c.W + gap > uw || c.H + gap > uh)) set.Notes.Add($"단면이 용지보다 큼 → 축척 1:{scale:0} 을 키울 것");

        // 칸을 측점 순서대로 채운다: 한 줄(열)에 위 → 아래로 쌓고, 높이가 차면 오른쪽 열, 폭이 차면 다음 장
        var place = new List<(int Sheet, double X, double YTop)>();
        int sheet = 0;
        double colX = 0, colW = 0, y = 0;
        foreach (var c in cells)
        {
            double w = c.W + gap, h = c.H + gap;
            if (y > 0 && y + h > uh + 1e-9) { colX += colW; colW = 0; y = 0; }            // 다음 열
            if (colX > 0 && colX + w > uw + 1e-9) { sheet++; colX = 0; colW = 0; y = 0; }  // 다음 장
            place.Add((sheet, colX, y));
            y += h;
            colW = Math.Max(colW, w);
        }
        set.Sheets = sheet + 1;
        set.PerSheet = (int)Math.Ceiling(cells.Count / (double)set.Sheets);
        for (int k = 0; k < set.Sheets; k++)
        {
            double ox = SheetGeom.OriginX(k, f);
            set.Origins.Add((ox, 0));
            SheetGeom.Decorate(set.Draw, ox, 0, f, $"{title} 횡단면도", $"S = 1:{scale:0}", k, set.Sheets);
        }
        for (int i = 0; i < cells.Count; i++)
        {
            var (sh, px, py) = place[i];
            double ox = SheetGeom.OriginX(sh, f);
            set.Draw.Add(cells[i].Draw, ox + left + px + gap * 0.5, set.SheetH - topM - py - cells[i].H - gap * 0.5);
        }
        return set;
    }
}
