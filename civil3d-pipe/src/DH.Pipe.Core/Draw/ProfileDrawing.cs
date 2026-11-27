using System.Globalization;
using DH.Pipe.Core.Geom;
using DH.Pipe.Core.Model;
using DH.Pipe.Core.Profile;
using DH.Pipe.Core.Quantity;

namespace DH.Pipe.Core.Draw;

/// <summary>
/// 종단도면 그리기 — 현장 샘플 A 구내배관 종단면도 꼴:
/// 표고 막대 · 지반선 · 관(위 · 아래 두 줄) · 지하수위 · 지장물 · 부속 · 구간 치수선(관 · 공법) · 밴드(구배 · 토피 · 관저고 · 지반고 · 누가거리 · 구간거리 · 측점).
/// 가로 1 도면 단위 = 1 m · 세로는 (가로 축척 / 세로 축척) 배로 키운다.
/// </summary>
public static class ProfileDrawing
{
    private static string F2(double v) => v.ToString("0.00", CultureInfo.InvariantCulture);

    public sealed class Input
    {
        public string Title { get; set; } = "관로";
        public Pl Ground { get; set; } = Pl.Const(0, 1, 0);
        public ProfileLine Line { get; set; } = new();
        public RouteData Route { get; set; } = new();
        /// <summary>밴드에 적을 측점.</summary>
        public List<double> Stations { get; set; } = new();
        public List<SpanQty> Spans { get; set; } = new();
        public List<PipeQty> Pipes { get; set; } = new();
        /// <summary>지층선(노선 따라 뜬 층 윗면 — 층 이름 · 토막들). 없으면 «땅속 값»으로 그린다.</summary>
        public List<(string Name, List<Pl> Runs)> Strata { get; set; } = new();
        /// <summary>지하수위선(지하수위 면에서 뜬 토막들). 없으면 «땅속 값»으로.</summary>
        public List<Pl> Water { get; set; } = new();
        /// <summary>이 측점 구간만 그린다(여러 장으로 나눌 때). null = 전체.</summary>
        public double? From { get; set; }
        public double? To { get; set; }
        /// <summary>위 제목 · 축척 글을 그릴지(장으로 나눌 때는 장의 제목을 따로 쓴다).</summary>
        public bool ShowTitle { get; set; } = true;
    }

    /// <summary>
    /// 종단도면을 A1 장으로 나눠 모형 공간에 왼쪽 → 오른쪽으로 놓는다. 장마다 측점 구간 하나 — 표고 막대 · 밴드 제목 칸이 장마다 있다.
    /// 장의 그림 자리는 배치탭 뷰포트가 보는 곳과 같다(<see cref="SheetGeom"/>).
    /// </summary>
    public static SheetSet Sheets(Input a)
    {
        var std = a.Route.Std;
        double hs = Math.Max(1, std.ProfileHScale), vs = Math.Max(1, std.ProfileVScale), f = hs / 1000.0;
        var set = new SheetSet { SheetW = SheetGeom.PaperW * f, SheetH = SheetGeom.PaperH * f, Scale = hs };
        if (a.Line.IsEmpty) return set;
        double s0 = Math.Max(a.Line.S0, a.Ground.X0), s1 = Math.Min(a.Line.S1, a.Ground.X1);
        var ranges = PlanSheets.Ranges(s0, s1, PlanSheets.SheetLength(hs, std.StationInterval));
        set.Sheets = ranges.Count;
        string scaleText = Math.Abs(hs - vs) < 1e-9 ? $"S = 1:{hs:0}" : $"S ( H = 1:{hs:0}  V = 1:{vs:0} )";
        for (int k = 0; k < ranges.Count; k++)
        {
            var (ra, rb) = ranges[k];
            var strip = Draw(new Input
            {
                Title = a.Title, Ground = a.Ground, Line = a.Line, Route = a.Route, Stations = a.Stations, Spans = a.Spans, Pipes = a.Pipes,
                Strata = a.Strata, Water = a.Water, From = ra, To = rb, ShowTitle = false,
            });
            double ox = SheetGeom.OriginX(k, f);
            set.Origins.Add((ox, 0));
            set.Ranges.Add((ra, rb));
            SheetGeom.Decorate(set.Draw, ox, 0, f, $"{a.Title} 종단면도", scaleText, k, ranges.Count);
            if (strip.Items.Count == 0) continue;
            var (bx0, by0, bx1, by1) = strip.Bounds();
            double roomW = (SheetGeom.ContentW - 2 * SheetGeom.Pad) * f, roomH = (SheetGeom.ContentH - 2 * SheetGeom.Pad) * f;
            if (bx1 - bx0 > roomW + 1e-6) set.Notes.Add($"종단 {k + 1}장: 가로가 용지보다 큼");
            if (by1 - by0 > roomH + 1e-6 && !set.Notes.Any(n => n.StartsWith("종단이 용지보다 높음"))) set.Notes.Add($"종단이 용지보다 높음 → 세로 축척 1:{vs:0} 을 키울 것");
            // 왼쪽 맞춤(밴드 제목 칸이 그림 자리 왼쪽 끝) · 위아래는 가운데
            double dx = ox + (SheetGeom.DrawL + SheetGeom.Pad) * f - bx0;
            double dy = (SheetGeom.DrawB + SheetGeom.Pad) * f + Math.Max(0, (roomH - (by1 - by0)) * 0.5) - by0;
            set.Draw.Add(strip, dx, dy);
        }
        return set;
    }

    public static DrawList Draw(Input a)
    {
        var d = new DrawList();
        var route = a.Route; var std = route.Std;
        if (a.Line.IsEmpty) return d;
        double hs = Math.Max(1, std.ProfileHScale), vs = Math.Max(1, std.ProfileVScale);
        double f = hs / 1000.0;             // 종이 1 mm = 가로 f m
        double ev = hs / vs;                // 세로 과장
        double s0 = Math.Max(a.Line.S0, a.Ground.X0), s1 = Math.Min(a.Line.S1, a.Ground.X1);
        double sBase = s0;                  // 누가거리의 기준 = 노선(종단)의 시작 — 장으로 나눠도 이어진다
        if (a.From.HasValue) s0 = Math.Max(s0, a.From.Value);
        if (a.To.HasValue) s1 = Math.Min(s1, a.To.Value);
        if (s1 - s0 < 1e-6) return d;

        // ---- 높이 범위(이 구간 안의 것만)
        double zmin = double.MaxValue, zmax = double.MinValue;
        foreach (var p in a.Ground.Points()) if (p.X >= s0 - 1e-9 && p.X <= s1 + 1e-9) { zmin = Math.Min(zmin, p.Y); zmax = Math.Max(zmax, p.Y); }
        foreach (double se in new[] { s0, s1 })
        {
            zmin = Math.Min(zmin, Math.Min(a.Ground.At(se), a.Line.At(se) - 1.0));
            zmax = Math.Max(zmax, Math.Max(a.Ground.At(se), a.Line.At(se) + 1.0));
        }
        foreach (var p in a.Line.Pvi)
        {
            if (p.S < s0 - 1e-9 || p.S > s1 + 1e-9) continue;
            zmin = Math.Min(zmin, p.Z - 1.0); zmax = Math.Max(zmax, p.Z + 1.0);
        }
        foreach (var o in route.Obstacles)
        {
            if (o.Sta < s0 - 1e-9 || o.Sta > s1 + 1e-9) continue;
            double top = o.TopElev ?? a.Ground.At(o.Sta) - o.Depth;
            zmin = Math.Min(zmin, top - o.Height - 0.5);
        }
        double span = zmax - zmin;
        double step = span <= 8 ? 1 : span <= 20 ? 2 : span <= 50 ? 5 : 10;
        double datum = Math.Floor((zmin - 1.0) / step) * step, ztop = Math.Ceiling((zmax + 1.0) / step) * step;
        double Y(double z) => (z - datum) * ev;

        // ---- 표고 막대
        double xp = s0 - 12 * f;
        d.Line(Lay.Pole, xp, Y(datum), xp, Y(ztop));
        for (double z = datum; z <= ztop + 1e-9; z += step)
        {
            d.Line(Lay.Pole, xp - 2 * f, Y(z), xp, Y(z));
            d.Text(Lay.Pole, xp - 3 * f, Y(z) - 1 * f, 2.5 * f, F2(z), 2);
        }
        d.Line(Lay.Band, s0, Y(datum), s1, Y(datum));

        // ---- 지반선 · 관 · 지하수위
        d.Poly(Lay.Ground, a.Ground.Clip(s0, s1).Points().Select(p => new P2(p.X, Y(p.Y))));
        var cuts = new SortedSet<double> { s0, s1 };
        foreach (var p in a.Line.Pvi) if (p.S > s0 && p.S < s1) cuts.Add(p.S);
        foreach (var r in route.Runs) { if (r.StaFrom > s0 && r.StaFrom < s1) cuts.Add(r.StaFrom); if (r.StaTo > s0 && r.StaTo < s1) cuts.Add(r.StaTo); }
        var cl = cuts.ToList();
        for (int i = 0; i + 1 < cl.Count; i++)
        {
            double sa = cl[i], sb = cl[i + 1];
            var pipe = route.PipeAt(0.5 * (sa + sb));
            double za = a.Line.At(sa), zb = a.Line.At(sb);
            d.Line(Lay.Pipe, sa, Y(PipeGeom.OuterBottom(pipe, za)), sb, Y(PipeGeom.OuterBottom(pipe, zb)));
            d.Line(Lay.Pipe, sa, Y(PipeGeom.OuterTop(pipe, za)), sb, Y(PipeGeom.OuterTop(pipe, zb)));
            d.Line(Lay.Pipe, sa, Y(PipeGeom.OuterBottom(pipe, za)), sa, Y(PipeGeom.OuterTop(pipe, za)));
            if (i + 2 == cl.Count) d.Line(Lay.Pipe, sb, Y(PipeGeom.OuterBottom(pipe, zb)), sb, Y(PipeGeom.OuterTop(pipe, zb)));
        }
        // ---- 지층선: 지층면에서 뜬 것(층 이름마다) → 없으면 «땅속 값»(토사 밑 = 풍화암 · 풍화암 밑 = 연암)
        int strataNo = 0;                   // 글자를 층마다 옆으로 벌려 적는다(층 선이 몇 mm 간격이라 한 자리에 적으면 겹친다)
        void StrataRun(string name, IEnumerable<(double X, double Y)> pts)
        {
            // 표고 틀 아래로 내려간 곳은 틀 바닥에서 자르지 않고 그 점을 뺀다(선이 밴드 위로 지나가지 않게)
            var seg = new List<P2>();
            int k = strataNo++;
            bool named = false;
            void Flush()
            {
                if (seg.Count >= 2)
                {
                    d.Poly(Lay.Strata, seg);
                    if (!named)
                    {
                        double want = seg[0].X + (3 + 22 * k) * f;
                        var at = seg.FirstOrDefault(p => p.X >= want - 1e-9, seg[^1]);
                        d.Text(Lay.Strata, at.X, at.Y + 0.8 * f, 2.2 * f, name);
                        named = true;
                    }
                }
                seg = new List<P2>();
            }
            foreach (var (x, z) in pts)
            {
                // 범위 밖 · 표고 틀 아래 · 지반과 겹치는 곳(맨 위 층 · 두께 0)은 끊는다
                if (x < s0 - 1e-6 || x > s1 + 1e-6 || z < datum + 1e-6 || a.Ground.At(x) - z <= 0.02) { Flush(); continue; }
                seg.Add(new P2(x, Y(z)));
            }
            Flush();
        }
        if (a.Strata.Count > 0)
        {
            foreach (var (name, runs) in a.Strata)
                foreach (var run in runs)
                    StrataRun(name, run.Points());
        }
        else if (route.Strata.Any(p => p.SoilThk.HasValue))
        {
            var top1 = new List<(double, double)>(); var top2 = new List<(double, double)>();
            foreach (double s in a.Stations)
            {
                var st = route.StrataAt(s);
                if (!st.SoilThk.HasValue) continue;
                double z1 = a.Ground.At(s) - Math.Max(0, st.SoilThk.Value);
                top1.Add((s, z1));
                if (st.WrThk.HasValue) top2.Add((s, z1 - Math.Max(0, st.WrThk.Value)));
            }
            StrataRun("풍화암", top1);
            StrataRun("연암", top2);
        }

        // ---- 지하수위선
        if (a.Water.Count > 0)
        {
            bool named = false;
            foreach (var run in a.Water)
            {
                if (run.X1 <= s0 + 1e-6 || run.X0 >= s1 - 1e-6) continue;
                var wpts = run.Clip(Math.Max(s0, run.X0), Math.Min(s1, run.X1)).Points().Where(p => p.Y > datum).Select(p => new P2(p.X, Y(p.Y))).ToList();
                if (wpts.Count < 2) continue;
                d.Poly(Lay.Water, wpts);
                if (!named)
                {
                    double want = wpts[0].X + (3 + 22 * strataNo) * f;
                    var at = wpts.FirstOrDefault(p => p.X >= want - 1e-9, wpts[^1]);
                    d.Text(Lay.Water, at.X, at.Y + 0.8 * f, 2.5 * f, "지하수위추정선");
                }
                named = true;
            }
        }
        else if (route.Strata.Any(p => p.WaterDepth.HasValue))
        {
            var wpts = new List<P2>();
            foreach (double s in a.Stations)
            {
                var st = route.StrataAt(s);
                if (st.WaterDepth.HasValue) wpts.Add(new P2(s, Y(a.Ground.At(s) - st.WaterDepth.Value)));
            }
            if (wpts.Count >= 2)
            {
                d.Poly(Lay.Water, wpts);
                d.Text(Lay.Water, wpts[0].X + 2 * f, wpts[0].Y - 3.5 * f, 2.5 * f, "지하수위추정선");
            }
        }

        // ---- 지장물(세로 과장 때문에 원이 길쭉해진다 — 꼭짓점 24개 타원 · 구조물은 네모)
        foreach (var oc in ProfileCheck.Obstacles(a.Line, a.Ground, route))
        {
            var o = oc.Obstacle;
            if (o.Sta < s0 - 1e-6 || o.Sta > s1 + 1e-6) continue;
            double cz = 0.5 * (oc.Top + oc.Bottom), rx = o.Width * 0.5, ry = o.Height * 0.5;
            if (o.Round)
            {
                var pts = new List<P2>();
                for (int k = 0; k < 24; k++)
                {
                    double t = k * Math.PI / 12;
                    pts.Add(new P2(o.Sta + rx * Math.Cos(t), Y(cz + ry * Math.Sin(t))));
                }
                d.Poly(Lay.Obst, pts, true);
            }
            else d.Rect(Lay.Obst, o.Sta - rx, Y(oc.Bottom), o.Sta + rx, Y(oc.Top));
            double lx = o.Sta + 4 * f, ly = Y(oc.Bottom) - 10 * f;
            d.Line(Lay.Obst, o.Sta, Y(oc.Bottom), lx, ly + 3 * f);
            string size = o.Round ? $"D{o.Width * 1000:0}" : $"{F2(o.Width)}×{F2(o.Height)}";
            d.Text(oc.Ok ? Lay.Obst : Lay.Warn, lx, ly, 2.2 * f, $"{o.Kind} {size} EL {F2(oc.Top)} 이격 {F2(oc.Gap)}{(oc.Ok ? "" : " ※")}");
        }

        // ---- 부속(공기밸브 ▽ · 이토 △ · 제수밸브 ⋈ · 소화전 ◇) — 글자 = AV · DV · GV · FH
        foreach (var ap in route.Appts)
        {
            if (ap.Sta < s0 - 1e-6 || ap.Sta > s1 + 1e-6) continue;
            var pipe = route.PipeAt(ap.Sta);
            double inv = a.Line.At(ap.Sta);
            double yt = Y(PipeGeom.OuterTop(pipe, inv)), yb = Y(PipeGeom.OuterBottom(pipe, inv));
            double u = 2.5 * f;
            string tag;
            switch (ap.Kind)
            {
                case ApptKind.AirValve:
                    d.Poly(Lay.Appt, new[] { new P2(ap.Sta - u, yt + 2 * u), new P2(ap.Sta + u, yt + 2 * u), new P2(ap.Sta, yt) }, true);
                    tag = "AV"; d.Text(Lay.Appt, ap.Sta, yt + 2.4 * u, 2.2 * f, tag, 1);
                    break;
                case ApptKind.Drain:
                    d.Poly(Lay.Appt, new[] { new P2(ap.Sta - u, yb - 2 * u), new P2(ap.Sta + u, yb - 2 * u), new P2(ap.Sta, yb) }, true);
                    tag = "DV"; d.Text(Lay.Appt, ap.Sta, yb - 2.4 * u - 2.2 * f, 2.2 * f, tag, 1);
                    break;
                case ApptKind.GateValve:
                    d.Poly(Lay.Appt, new[] { new P2(ap.Sta - u, yt + u), new P2(ap.Sta + u, yb - u), new P2(ap.Sta + u, yt + u), new P2(ap.Sta - u, yb - u) }, true);
                    tag = "GV"; d.Text(Lay.Appt, ap.Sta, yt + 1.6 * u, 2.2 * f, tag, 1);
                    break;
                default:
                    d.Poly(Lay.Appt, new[] { new P2(ap.Sta, yt), new P2(ap.Sta + u, yt + u), new P2(ap.Sta, yt + 2 * u), new P2(ap.Sta - u, yt + u) }, true);
                    tag = "FH"; d.Text(Lay.Appt, ap.Sta, yt + 2.4 * u, 2.2 * f, tag, 1);
                    break;
            }
        }

        // ---- 구간 치수선(관종 · 관경 / 공법) — 바닥 기준선과 밴드 사이의 띠(관 · 지장물 글자와 안 겹치게)
        double yd1 = Y(datum) - 7 * f, yd2 = Y(datum) - 15 * f;
        foreach (var p in a.Pipes) DimLine(d, f, Math.Max(p.StaFrom, s0), Math.Min(p.StaTo, s1), yd1, $"D{p.Pipe.Dn}({p.Pipe.Material}) L={F2(p.SlopeLen)}M");
        foreach (var sq in a.Spans) DimLine(d, f, Math.Max(sq.StaFrom, s0), Math.Min(sq.StaTo, s1), yd2, $"{MethodSpan.NameOf(sq.Span.Kind)} 구간 L={F2(sq.Length)}M");

        // ---- 밴드
        string[] rowsName = { "구 배", "토 피", "관저고", "지반고", "누가거리", "구간거리", "측 점" };
        double rh = 14 * f, bx0 = s0 - 24 * f, by = Y(datum) - 21 * f;
        double bh = rh * rowsName.Length;
        d.Rect(Lay.Band, bx0, by - bh, s1 + 4 * f, by);
        d.Line(Lay.Band, s0 - 4 * f, by - bh, s0 - 4 * f, by);
        for (int i = 0; i < rowsName.Length; i++)
        {
            if (i > 0) d.Line(Lay.Band, bx0, by - i * rh, s1 + 4 * f, by - i * rh);
            d.Text(Lay.BandText, 0.5 * (bx0 + s0 - 4 * f), by - (i + 0.5) * rh - 1.5 * f, 3 * f, rowsName[i], 1);
        }
        // 구배 줄: 꺾인 점 눈금 + «S=1.08% L=27.71»
        for (int i = 0; i + 1 < a.Line.Pvi.Count; i++)
        {
            double sa = Math.Max(a.Line.Pvi[i].S, s0), sb = Math.Min(a.Line.Pvi[i + 1].S, s1);
            if (sb - sa < 1e-6) continue;
            d.Line(Lay.Band, sa, by - rh, sa, by);
            double g = a.Line.GradePct(i);
            string t = Math.Abs(g) < 0.005 ? $"LEVEL L={F2(sb - sa)}" : $"S={(g > 0 ? "+" : "")}{F2(g)}% L={F2(sb - sa)}";
            if (sb - sa > DrawList.TextWidth(t, 2.2 * f)) d.Text(Lay.BandText, 0.5 * (sa + sb), by - 0.5 * rh - 1.1 * f, 2.2 * f, t, 1);
            // 올라가면 /  내려가면 \  평평하면 ─
            double ya = by - rh + 1.5 * f, yb2 = by - 1.5 * f;
            if (g > 0.005) d.Line(Lay.Band, sa, ya, sb, yb2);
            else if (g < -0.005) d.Line(Lay.Band, sa, yb2, sb, ya);
        }
        // 값 줄: 측점마다 세로 글 · 너무 붙으면 건너뜀
        double lastX = double.NegativeInfinity, prevS = s0;
        foreach (double s in a.Stations.OrderBy(x => x))
        {
            if (s < s0 - 1e-6 || s > s1 + 1e-6) continue;
            d.Line(Lay.Leader, s, by - bh, s, by - bh + 1.5 * f);
            for (int i = 1; i < rowsName.Length; i++) d.Line(Lay.Band, s, by - i * rh - 1.0 * f, s, by - i * rh + 1.0 * f);
            if (s - lastX < 3.4 * f) { prevS = s; continue; }
            var pipe = route.PipeAt(s);
            double g = a.Ground.At(s), inv = a.Line.At(s);
            string[] vals =
            {
                F2(PipeGeom.Cover(pipe, inv, g)), F2(inv), F2(g), F2(s - sBase), F2(s - prevS),
                StationPlan.Name(s, std.StationInterval),
            };
            for (int i = 0; i < vals.Length; i++)
                d.Text(Lay.BandText, s + 1.1 * f, by - (i + 2) * rh + 1.5 * f, 2.2 * f, vals[i], 0, 90);
            d.Line(Lay.Leader, s, by, s, Y(PipeGeom.OuterBottom(pipe, inv)));
            lastX = s; prevS = s;
        }

        // ---- 제목 · 축척
        if (a.ShowTitle)
        {
            double yt2 = Y(ztop) + 16 * f;
            d.Text(Lay.Title, 0.5 * (s0 + s1), yt2, 7 * f, $"{a.Title} 종단면도", 1);
            d.Text(Lay.Title, 0.5 * (s0 + s1), yt2 - 7 * f, 3.5 * f, $"S ( H = 1:{hs:0}  V = 1:{vs:0} )", 1);
        }
        return d;
    }

    private static void DimLine(DrawList d, double f, double a, double b, double y, string text)
    {
        if (b - a < 1e-6) return;
        d.Line(Lay.Dim, a, y, b, y);
        d.Line(Lay.Dim, a, y - 1.5 * f, a, y + 1.5 * f);
        d.Line(Lay.Dim, b, y - 1.5 * f, b, y + 1.5 * f);
        d.Text(Lay.Dim, 0.5 * (a + b), y + 1.0 * f, 2.5 * f, text, 1);
    }
}
