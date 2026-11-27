using DH.Pipe.Core.Geom;
using DH.Pipe.Core.Model;

namespace DH.Pipe.Core.Section;

/// <summary>
/// 한 측점의 터파기 단면을 만든다.
/// 공법(바닥폭 · 벽) → 바닥선 L(x) → 지반 · 포장 · 지층 · 수위 · 깊이 선으로 조각을 나눠 넓이와 그림을 낸다.
/// 모든 조각은 «아래 선들의 최댓값 ~ 위 선들의 최솟값» 사이(함수 띠)라서 정확하고 깨지지 않는다.
/// </summary>
public static class SectionBuilder
{
    public static SectionResult Build(SectionInput s)
    {
        var r = new SectionResult { Station = s.Station, Label = s.Label, Method = s.Method.Kind };
        r.Messages.AddRange(s.Notes);
        var U = s.Ground;
        r.DomainX0 = U.X0;
        r.DomainX1 = U.X1;
        r.Lines.Add(new SectionLine { Key = "GROUND", Pts = U.Points().Select(p => new P2(p.X, p.Y)).ToList() });
        if (s.Pipes.Count == 0)
        {
            r.Ok = false;
            r.Messages.Add("관 없음");
            return r;
        }

        var std = s.Std;
        var m = s.Method;
        var main = s.Pipes[0];
        r.GroundCl = U.At(main.Dx);
        r.Invert = main.Invert;
        foreach (var p in s.Pipes) r.Circles.Add(new SectionCircle { Key = "PIPE", X = p.Dx, Z = p.CenterZ, R = p.Od * 0.5 });

        if (m.Kind == MethodKind.Jacking) return BuildJacking(s, r);

        double cover = r.GroundCl - main.OuterTop;
        r.SetLen(Q.Cover, cover);
        double minCover = std.MinCover(new PipeSpec { Dn = main.Dn, Od = main.Od, Wall = main.Wall });
        if (cover < -1e-6) r.Messages.Add("관이 지반 위");
        else if (cover < minCover - 0.005) r.Messages.Add($"토피 부족 {cover:0.00} < {minCover:0.00}");

        // ---------------------------------------------------------------- 높이 · 폭
        double zLow = s.Pipes.Min(p => p.OuterBottom);
        double zBot = zLow - std.Bedding;                       // 터파기 바닥
        double zb = zLow;                                       // 관 기초 윗면
        double zt = s.Pipes.Max(p => p.OuterTop) + std.ZoneTop; // 관주위 윗면
        double pl = s.Pipes.Min(p => p.Dx - p.Od * 0.5), pr = s.Pipes.Max(p => p.Dx + p.Od * 0.5);
        double mid = 0.5 * (pl + pr), span = pr - pl;
        double B = m.Kind switch
        {
            MethodKind.ShoreSk => s.Pipes.Count == 1 && main.Dn <= std.SkSmallDn ? std.SkSmallWidth : span + std.SkExtra,
            MethodKind.ShoreTs => span + std.TsExtra,
            MethodKind.HPile => span + std.HPileExtra,
            MethodKind.SheetPile => span + std.SheetExtra,
            _ => span + 2 * (m.Clearance ?? std.Clearance),
        };
        if (m.Width is > 0) B = m.Width.Value;
        if (B < span - 1e-9) r.Messages.Add($"바닥폭 {B:0.00} < 관 폭 {span:0.00}");
        double xbL = mid - B * 0.5, xbR = mid + B * 0.5;
        r.Bottom = zBot;
        r.SetLen(Q.BotWidth, B);
        r.SetLen(Q.Depth, Math.Max(0, r.GroundCl - zBot));
        if (xbL <= U.X0 + 0.01 || xbR >= U.X1 - 0.01)
        {
            r.Ok = false;
            r.Messages.Add("지반 범위 부족");
            return r;
        }

        // ---------------------------------------------------------------- 포장 층 · 지층 · 수위 · 깊이 선
        var zones = FillZones(s, U.X0, U.X1);
        var (tb, tt) = Thickness(zones, std, U.X0, U.X1);
        Pl P1 = tb == null ? U : U.Minus(tb);                   // 묶인 포장 밑
        Pl P2 = tt == null ? U : U.Minus(tt);                   // 보조기층 밑
        // S[k] = 암 분류 k 의 윗면(k = 1 풍화암 … 4 경암). null = 그 분류와 그보다 단단한 것이 없음.
        var S = new Pl?[Q.RockCount];
        if (s.Layers.Count > 0)
        {
            // 지층면에서 뜬 선: 분류 k 의 윗면 = 첫 «k 이상» 층의 윗면(풍화암 없이 바로 연암이면 둘이 같은 선 = 풍화암 두께 0).
            // 지반 위로 솟은 곳은 지반에 붙이고(그 층이 드러난 것), 아래 경계가 위 경계보다 높을 수 없게 한다.
            Pl? above = null;
            for (int k = 1; k < Q.RockCount; k++)
            {
                var lk = s.Layers.FirstOrDefault(l => l.Rock >= k);
                if (lk == null) break;
                var top = Pl.Min(U, OnDomain(lk.Top, U));
                S[k] = above == null ? top : Pl.Min(above, top);
                above = S[k];
            }
        }
        else
        {
            S[1] = s.SoilThk.HasValue ? U.Shift(-Math.Max(0, s.SoilThk.Value)) : null;                 // 토사 밑 = 풍화암 위
            S[2] = S[1] != null && s.WrThk.HasValue ? S[1]!.Shift(-Math.Max(0, s.WrThk.Value)) : null;  // 풍화암 밑 = 연암 위
        }
        Pl? W = s.WaterLine != null ? OnDomain(s.WaterLine, U) : s.WaterElev.HasValue ? Pl.Const(U.X0, U.X1, s.WaterElev.Value) : null;
        Pl? D = std.DepthLimit > 0 ? U.Shift(-std.DepthLimit) : null;

        // ---------------------------------------------------------------- 벽
        bool vertical = m.IsVertical;
        bool paveVert = vertical || (m.PaveVertical ?? std.PaveVertical);
        double nSoil = m.SlopeSoil ?? (m.Kind == MethodKind.RiverOpen ? std.RiverSlope : std.SlopeSoil);
        double nWr = m.SlopeWr ?? std.SlopeWr, nRock = m.SlopeRock ?? std.SlopeRock;
        // 분류마다의 기울기: 토사 · 풍화암 · 연암 · 보통암(연암과 같게) · 경암
        var slopes = new[] { nSoil, nWr, nRock, nRock, Math.Min(nRock, std.SlopeHard) };
        Pl stop = paveVert ? P1 : U;
        var wl = Wall(xbL, zBot, -1, vertical, U, stop, S, slopes);
        var wr = Wall(xbR, zBot, +1, vertical, U, stop, S, slopes);
        if (wl == null || wr == null || wl[^1].X < U.X0 + 1e-6 || wr[^1].X > U.X1 - 1e-6)
        {
            r.Ok = false;
            r.Messages.Add("지반 범위 부족");
            return r;
        }
        double e = Math.Max(0, std.PaveCutExtra);
        double xTopL = wl[^1].X, xTopR = wr[^1].X;
        bool pavedL = paveVert && tb != null && tb.At(xTopL - 1e-4) > 1e-6 && wl.Count > 1;
        bool pavedR = paveVert && tb != null && tb.At(xTopR + 1e-4) > 1e-6 && wr.Count > 1;
        double xOutL = pavedL ? Math.Max(U.X0 + 1e-3, xTopL - e) : xTopL;
        double xOutR = pavedR ? Math.Min(U.X1 - 1e-3, xTopR + e) : xTopR;

        // ---------------------------------------------------------------- 바닥선 L(x) = 지반 … 왼 벽 … 바닥 … 오른 벽 … 지반
        var lp = new List<(double X, double Y)>();
        foreach (var p in U.Points()) if (p.X < xOutL - 1e-9) lp.Add(p);
        lp.Add((xOutL, U.At(xOutL)));
        if (pavedL)
        {
            lp.Add((xOutL, P1.At(xOutL)));
            foreach (var p in P1.Points()) if (p.X > xOutL + 1e-9 && p.X < xTopL - 1e-9) lp.Add(p);
            if (xTopL - xOutL > 1e-9) lp.Add((xTopL, P1.At(xTopL)));
        }
        for (int i = wl.Count - 1; i >= 0; i--) AddPt(lp, wl[i]);
        for (int i = 0; i < wr.Count; i++) AddPt(lp, wr[i]);
        if (pavedR)
        {
            foreach (var p in P1.Points()) if (p.X > xTopR + 1e-9 && p.X < xOutR - 1e-9) lp.Add(p);
            if (xOutR - xTopR > 1e-9) lp.Add((xOutR, P1.At(xOutR)));
        }
        lp.Add((xOutR, U.At(xOutR)));
        foreach (var p in U.Points()) if (p.X > xOutR + 1e-9) lp.Add(p);
        for (int i = 1; i < lp.Count; i++)
        {
            if (lp[i].X < lp[i - 1].X - 1e-6)                    // 왼 → 오 순서가 깨짐 = 단면 꼴이 안 섬(짐작으로 고치지 않음)
            {
                r.Ok = false;
                r.Messages.Add("단면 꼴 오류(바닥선 순서)");
                return r;
            }
        }
        var L = Pl.From(lp);

        r.XLeft = xOutL;
        r.XRight = xOutR;
        r.SetLen(Q.TopWidth, xTopR - xTopL);
        if (vertical)
        {
            r.SetLen(Q.WallL, Math.Max(0, U.At(xbL) - zBot));
            r.SetLen(Q.WallR, Math.Max(0, U.At(xbR) - zBot));
        }

        // ---------------------------------------------------------------- 터파기 조각(토질 × 육상/용수 × 깊이)
        for (int rock = 0; rock < Q.RockCount; rock++)
        {
            Pl? lowK = rock + 1 < Q.RockCount ? S[rock + 1] : null;
            Pl? upK = rock == 0 ? null : S[rock];
            if (rock > 0 && upK == null) continue;              // 그 층이 없음
            for (int wet = 0; wet < 2; wet++)
            {
                if (wet == 1 && W == null) continue;
                for (int deep = 0; deep < 2; deep++)
                {
                    if (deep == 1 && D == null) continue;
                    var lower = Pl.Max(L, lowK, wet == 0 ? W : null, deep == 0 ? D : null);
                    var upper = Pl.Min(P2, upK, wet == 1 ? W : null, deep == 1 ? D : null);
                    r.AddArea(Q.Exc(wet == 1, deep == 1, rock), Pl.AreaBetween(lower, upper));
                }
            }
        }

        // ---------------------------------------------------------------- 포장 깨기 · 보조기층
        var boundLow = Pl.Max(L, P1);
        int cutLines = 0;
        if (tb != null)
        {
            foreach (var z in zones)
            {
                if (std.Pavement(z.Kind).Bound <= 1e-9) continue;
                double a0 = Math.Max(z.X0, xOutL), a1 = Math.Min(z.X1, xOutR);
                if (a1 - a0 < 1e-6) continue;
                double area = Pl.AreaBetween(boundLow, U, a0, a1);
                if (area < 1e-9) continue;
                r.AddArea(Q.Pav(z.Kind), area);
                r.SetLen(Q.Cut(z.Kind), r.L(Q.Cut(z.Kind)) + Pl.WidthWhere(boundLow, U, a0, a1));
                var fill = new SectionFill { Key = Q.Pav(z.Kind), Polys = Pl.Regions(boundLow.Clip(a0, a1), U.Clip(a0, a1)) };
                if (fill.Polys.Count > 0) r.Fills.Add(fill);
            }
            if (pavedL) cutLines++;
            if (pavedR) cutLines++;
            r.AddArea(Q.Subbase, Pl.AreaBetween(Pl.Max(L, P2), P1));
            AddFill(r, Q.Subbase, Pl.Max(L, P2), P1, xOutL, xOutR);
        }
        if (cutLines > 0) r.SetLen(Q.CutLines, cutLines);
        int curbs = s.Curbs.Count(c => c > xOutL + 1e-6 && c < xOutR - 1e-6);
        if (curbs > 0) r.SetLen(Q.Curb, curbs);

        // ---------------------------------------------------------------- 되메우기
        var cZb = Pl.Const(U.X0, U.X1, zb);
        var cZt = Pl.Const(U.X0, U.X1, zt);
        Pl bedLo = L, bedUp = Pl.Min(P2, cZb);
        Pl zoneLo = Pl.Max(L, cZb), zoneUp = Pl.Min(P2, cZt);
        Pl upLo = Pl.Max(L, cZt), upUp = P2;
        double aBed = Pl.AreaBetween(bedLo, bedUp);
        double aZone = Pl.AreaBetween(zoneLo, zoneUp);
        double aUp = Pl.AreaBetween(upLo, upUp);
        // 관 단면은 터파기 안(보조기층 밑 아래)에 든 부분만 공제한다 — 얕은 관은 원 전체가 아니다(검토 낮음)
        double pipeArea = s.Pipes.Sum(p => CircleBelow(p.CenterZ, p.Od * 0.5, P2.At(p.Dx)));
        if (m.Kind == MethodKind.RiverOpen && m.Encase)
        {
            double t = std.EncaseT;
            double ex0 = pl - t, ex1 = pr + t, ez0 = zLow - t, ez1 = s.Pipes.Max(p => p.OuterTop) + t;
            var c0 = Pl.Const(U.X0, U.X1, ez0);
            var c1 = Pl.Const(U.X0, U.X1, ez1);
            aBed -= Pl.AreaBetween(Pl.Max(bedLo, c0), Pl.Min(bedUp, c1), ex0, ex1);
            aZone -= Pl.AreaBetween(Pl.Max(zoneLo, c0), Pl.Min(zoneUp, c1), ex0, ex1);
            aUp -= Pl.AreaBetween(Pl.Max(upLo, c0), Pl.Min(upUp, c1), ex0, ex1);
            // 보호공도 터파기 안에 든 부분만(상자가 터파기 밖으로 나가면 그만큼은 못 친다 — 검토 낮음)
            Pl encLo = Pl.Max(L, c0), encUp = Pl.Min(P2, c1);
            r.AddArea(Q.Encase, Math.Max(0, Pl.AreaBetween(encLo, encUp, ex0, ex1) - pipeArea));
            var encPolys = Pl.Regions(encLo.Clip(ex0, ex1), encUp.Clip(ex0, ex1));
            if (encPolys.Count > 0) r.Fills.Add(new SectionFill { Key = Q.Encase, Polys = encPolys });
        }
        else
        {
            aZone -= Math.Min(pipeArea, aZone);                 // 관 단면 공제
        }
        r.AddArea(Q.Bed, Math.Max(0, aBed));
        r.AddArea(Q.Zone, Math.Max(0, aZone));
        r.AddArea(Q.Upper, Math.Max(0, aUp));
        AddFill(r, Q.Upper, upLo, upUp, xOutL, xOutR);
        AddFill(r, Q.Zone, zoneLo, zoneUp, xOutL, xOutR);
        AddFill(r, Q.Bed, bedLo, bedUp, xOutL, xOutR);

        // ---------------------------------------------------------------- 선
        var trench = new SectionLine { Key = "TRENCH" };
        foreach (var p in L.Points()) if (p.X >= xOutL - 1e-9 && p.X <= xOutR + 3 * Pl.EpsX) trench.Pts.Add(new P2(p.X, p.Y));
        if (trench.Pts.Count > 1) r.Lines.Add(trench);
        if (vertical)
        {
            double emb = m.Kind is MethodKind.HPile ? std.HPileEmbed : m.Kind is MethodKind.SheetPile ? std.SheetEmbed : 0.3;
            r.Lines.Add(new SectionLine { Key = "SHORE", Pts = { new P2(xbL, U.At(xbL) + 0.2), new P2(xbL, zBot - emb) } });
            r.Lines.Add(new SectionLine { Key = "SHORE", Pts = { new P2(xbR, U.At(xbR) + 0.2), new P2(xbR, zBot - emb) } });
        }
        double lx0 = Math.Max(U.X0, xOutL - 1.0), lx1 = Math.Min(U.X1, xOutR + 1.0);
        if (W != null && W.At(mid) > zBot) r.Lines.Add(LineOf("WATER", W, lx0, lx1));
        if (D != null && D.At(mid) > zBot) r.Lines.Add(LineOf("DEPTH", D, lx0, lx1));
        if (s.Layers.Count > 0)
        {
            // 층마다 윗면 선과 이름(부지정지 지층면 그대로). 지반과 겹치는 맨 위 층 · 터파기 바닥보다 아래 층은 뺀다.
            foreach (var l in s.Layers)
            {
                if (!l.Show) continue;
                var top = Pl.Min(U, OnDomain(l.Top, U));
                if (top.At(mid) > zBot && U.At(mid) - top.At(mid) > 0.02) r.Lines.Add(LineOf("LAYER", top, lx0, lx1, l.Name));
            }
        }
        else
        {
            if (S[1] != null && S[1]!.At(mid) > zBot) r.Lines.Add(LineOf("STRATA1", S[1]!, lx0, lx1, "풍화암"));
            if (S[2] != null && S[2]!.At(mid) > zBot) r.Lines.Add(LineOf("STRATA2", S[2]!, lx0, lx1, "연암"));
        }
        return r;
    }

    // ---------------------------------------------------------------- 추진: 터파기 없음 · 케이싱 원
    private static SectionResult BuildJacking(SectionInput s, SectionResult r)
    {
        var main = s.Pipes[0];
        double cas = s.Method.CasingOd is > 0 ? s.Method.CasingOd.Value : main.Od + s.Std.CasingExtra;
        if (cas < main.Od) cas = main.Od;
        r.Circles.Insert(0, new SectionCircle { Key = "CASING", X = main.Dx, Z = main.CenterZ, R = cas * 0.5 });
        r.AddArea(Q.Bore, Math.PI * cas * cas * 0.25);
        double cover = r.GroundCl - (main.CenterZ + cas * 0.5);
        r.SetLen(Q.Cover, cover);
        r.Bottom = main.CenterZ - cas * 0.5;
        if (cover < 0) r.Messages.Add("케이싱이 지반 위");
        return r;
    }

    // ---------------------------------------------------------------- 도우미
    /// <summary>원(가운데 높이 cz · 반지름 r)에서 높이 y 아래에 든 넓이.</summary>
    internal static double CircleBelow(double cz, double r, double y)
    {
        if (r <= 0) return 0;
        if (y >= cz + r) return Math.PI * r * r;
        if (y <= cz - r) return 0;
        double h = y - (cz - r);                    // 밑에서 잰 높이
        return r * r * Math.Acos((r - h) / r) - (r - h) * Math.Sqrt(Math.Max(0, 2 * r * h - h * h));
    }

    private static void AddPt(List<(double X, double Y)> list, P2 p)
    {
        if (list.Count > 0 && Math.Abs(list[^1].X - p.X) < 1e-12 && Math.Abs(list[^1].Y - p.Y) < 1e-12) return;
        list.Add((p.X, p.Y));
    }

    private static SectionLine LineOf(string key, Pl f, double a, double b, string label = "")
        => new() { Key = key, Label = label, Pts = f.Clip(a, b).Points().Select(p => new P2(p.X, p.Y)).ToList() };

    /// <summary>f 를 U 의 범위에 맞춘다 — 범위 밖은 끝값으로 이어 준다(지층면이 단면 폭보다 좁을 때).</summary>
    internal static Pl OnDomain(Pl f, Pl U)
    {
        var pts = new List<(double, double)> { (U.X0, f.At(U.X0)) };
        foreach (var (x, y) in f.Points()) if (x > U.X0 + Pl.EpsX && x < U.X1 - Pl.EpsX) pts.Add((x, y));
        pts.Add((U.X1, f.At(U.X1)));
        return Pl.From(pts);
    }

    private static void AddFill(SectionResult r, string key, Pl lower, Pl upper, double a, double b)
    {
        double x0 = Math.Max(a - 0.01, Math.Max(lower.X0, upper.X0)), x1 = Math.Min(b + 0.01, Math.Min(lower.X1, upper.X1));
        if (x1 - x0 < 1e-6) return;
        var polys = Pl.Regions(lower.Clip(x0, x1), upper.Clip(x0, x1));
        if (polys.Count > 0) r.Fills.Add(new SectionFill { Key = key, Polys = polys });
    }

    /// <summary>
    /// 벽 한쪽: 바닥 모서리에서 위로. 수직이면 곧장, 아니면 지층마다 경사를 바꿔 가며 멈출 선(지반 또는 묶인 포장 밑)까지.
    /// 돌려주는 점 = 모서리 … 꼭대기. 못 만나면 null.
    /// </summary>
    /// <param name="S">S[k] = 암 분류 k 의 윗면(없으면 null).</param>
    /// <param name="slopes">분류마다의 기울기 1:n.</param>
    private static List<P2>? Wall(double xb, double zBot, int dir, bool vertical, Pl U, Pl stop, Pl?[] S, double[] slopes)
    {
        var pts = new List<P2> { new(xb, zBot) };
        if (U.At(xb) <= zBot + 1e-9) return pts;                // 바닥이 지반 위 — 벽 없음
        if (vertical)
        {
            double zTop = stop.At(xb);
            if (zTop > zBot + 1e-9) pts.Add(new P2(xb, zTop));
            return pts;
        }
        double x = xb, z = zBot;
        if (stop.At(x) <= z + 1e-9) return pts;                 // 포장 밑이 이미 바닥 아래
        for (int guard = 0; guard < 32; guard++)
        {
            // 지금 높이가 든 분류 = 그 윗면보다 아래에 있는 가장 단단한 분류
            int layer = 0;
            for (int k = S.Length - 1; k >= 1; k--)
                if (S[k] != null && z < S[k]!.At(x) - 1e-9) { layer = k; break; }
            double n = Math.Max(0, slopes[Math.Min(layer, slopes.Length - 1)]);
            Pl? top = layer > 0 ? S[layer] : null;
            P2? hitStop = stop.RayHit(x, z, dir * n, 1);
            P2? hitTop = top?.RayHit(x, z, dir * n, 1);
            if (hitTop.HasValue && (!hitStop.HasValue || hitTop.Value.Y < hitStop.Value.Y - 1e-9))
            {
                pts.Add(hitTop.Value);
                x = hitTop.Value.X;
                z = hitTop.Value.Y;
                continue;
            }
            if (hitStop.HasValue)
            {
                pts.Add(hitStop.Value);
                return pts;
            }
            return null;
        }
        return null;
    }

    /// <summary>구역 목록을 [x0, x1] 전체를 빈틈없이 덮게 다듬는다(빈 곳 = 기본 지표 · 겹치면 뒤에 적힌 것).</summary>
    public static List<ZoneSpan> FillZones(SectionInput s, double x0, double x1)
    {
        var cuts = new SortedSet<double> { x0, x1 };
        foreach (var z in s.Zones)
        {
            double a = Math.Clamp(Math.Min(z.X0, z.X1), x0, x1), b = Math.Clamp(Math.Max(z.X0, z.X1), x0, x1);
            if (b - a < 1e-6) continue;
            cuts.Add(a);
            cuts.Add(b);
        }
        var xs = cuts.ToList();
        var res = new List<ZoneSpan>();
        for (int i = 0; i + 1 < xs.Count; i++)
        {
            if (xs[i + 1] - xs[i] < 1e-6) continue;
            double xm = 0.5 * (xs[i] + xs[i + 1]);
            var kind = s.Std.DefaultSurface;
            foreach (var z in s.Zones)
                if (xm >= Math.Min(z.X0, z.X1) && xm <= Math.Max(z.X0, z.X1)) kind = z.Kind;
            if (res.Count > 0 && res[^1].Kind == kind) res[^1].X1 = xs[i + 1];
            else res.Add(new ZoneSpan { X0 = xs[i], X1 = xs[i + 1], Kind = kind });
        }
        return res;
    }

    /// <summary>구역별 포장 두께를 계단 함수로(묶인 층 · 전체). 포장이 하나도 없으면 null.</summary>
    private static (Pl? Bound, Pl? Total) Thickness(List<ZoneSpan> zones, Standards std, double x0, double x1)
    {
        if (zones.All(z => std.Pavement(z.Kind).Bound + std.Pavement(z.Kind).Subbase <= 1e-9)) return (null, null);
        var bx = new List<double>(); var by = new List<double>(); var ty = new List<double>();
        foreach (var z in zones)
        {
            var p = std.Pavement(z.Kind);
            bx.Add(z.X0); by.Add(p.Bound); ty.Add(p.Bound + p.Subbase);
            bx.Add(z.X1); by.Add(p.Bound); ty.Add(p.Bound + p.Subbase);
        }
        return (new Pl(bx, by), new Pl(bx, ty));
    }
}
