using System.Globalization;
using NetTopologySuite.Geometries;
using NetTopologySuite.Operation.Buffer;
using NetTopologySuite.Operation.Linemerge;
using Tri = DH.Grading.Core.WallDaylight.Tri;

namespace DH.Grading.Core;

/// <summary>★★★[v103.3 · 계획 2-2] 옹벽 앞면 아랫선 → 최종면에서 재기 → 바닥 띠.</summary>
public static partial class WallExcav
{
    /// <summary>옹벽 한 단의 앞면 아랫선(열린 선 · XY) — Z = 기록이 말하는 그 자리 벽 밑(모르면 NaN).</summary>
    public sealed class FaceLine
    {
        public List<Point3> Pts = new();
        /// <summary>흙(뒤) 쪽 — +1 = 선 진행 방향 왼쪽 · −1 = 오른쪽.</summary>
        public int Soil;
        /// <summary>앞면의 평면 너비(뒤쪽 표본을 앞면 너머에 두려고) · 앞면 구배(1:SlopeW — 선이 앞면 위에 얹혔나 가를 때).</summary>
        public double FaceRun, SlopeW;
        public string Src = "";
    }

    /// <summary>재어 낸 바닥 한 줄 — Z = 바닥 높이(벽 밑 − 기초 깊이).</summary>
    public sealed class Bottom
    {
        public List<Point3> Pts = new();
        public int Soil;
        public string Src = "";
        /// <summary>그 줄에서 잰 벽 높이 최대(흙 쪽 − 앞쪽) — 로그 · «보통 거리» 셈.</summary>
        public double WallHMax;
        /// <summary>★[v104] 점마다(Pts와 같은 차례) — 벽 밑(앞 땅 · toe) · 보이는 벽 높이 h · 앞 기울기 rf · 뒤 기울기 rb(흙 쪽으로 오르는 +).</summary>
        public List<double> Toe = new(), H = new(), Rf = new(), Rb = new();
        /// <summary>★[v104] 절토 옹벽인가(앞면 선의 up — 부르는 쪽이 넣는다) · 그 앞면 선의 앞면 구배(정지면 벽 앞면 1:n — 쐐기 공제 · 패널식 깎기면 · <see cref="Measure"/>가 넣는다).</summary>
        public bool Up = true;
        public double SlopeW = 0.01;
        /// <summary>★[v104 B] 부르는 쪽이 넣는다 — 옹벽 묶음 이름 · 단 번호 · 그 옹벽의 맨 위 단인가(배면사면 대상).</summary>
        public string Group = "";
        public int Step;
        public bool Top;
    }

    /// <summary>재기 셈 — 까닭별 길이(m).</summary>
    public sealed class MeasureNote
    {
        public double Len, Kept, NoWall, NoData, Reversed, Misplaced, OnFace;
        public string MisAt = "", OnAt = "", RevAt = "";
        public string Text => string.Format(CultureInfo.InvariantCulture,
            "선 {0:F1}m · 남김 {1:F1}m · 벽 없음(0.1m 미만) {2:F1}m{3}{4}{5}{6}",
            Len, Kept, NoWall, NoData > 0 ? $" · 면을 못 읽음 {NoData:F1}m" : "",
            Reversed > 0 ? $" · ⚠흙 쪽이 기록과 반대 {Reversed:F1}m @{RevAt}" : "",
            Misplaced > 0 ? $" · ⚠벽 밑이 기록보다 5cm 넘게 낮음 {Misplaced:F1}m @{MisAt}" : "",
            OnFace > 0 ? $" · ⚠선이 앞면 위에 얹힘 {OnFace:F1}m @{OnAt}" : "");
        public bool Warn => Reversed > 0 || Misplaced > 0 || OnFace > 0;
    }

    /// <summary>폴리곤 표에서 옹벽이 서는 열린 선(측선 → 선택구간 → 측선) — 벽 짓기(CreateGradingCommand)와 같은 뽑기.</summary>
    public static List<Point3> WallLineOf(IReadOnlyList<Point3> poly, IReadOnlyList<bool> isWall)
    {
        var wline = new List<Point3>();
        int n0 = poly.Count;
        if (isWall == null || isWall.Count != n0) return wline;
        int st = -1;
        for (int i = 0; i < n0; i++)
            if (isWall[i] && !isWall[(i - 1 + n0) % n0]) { st = i; break; }
        if (st < 0 && n0 > 0 && isWall[0]) st = 0;
        if (st >= 0)
            for (int k = 0; k < n0; k++)
            {
                int i = (st + k) % n0;
                if (!isWall[i]) break;
                wline.Add(poly[i]);
            }
        return wline;
    }

    /// <summary>★구간 옹벽 — 단마다 앞면 아랫선(계획 2-2 · 재검토 중간 4).
    /// <list type="bullet">
    /// <item>절토 k단: 옹벽선에서 <c>step(k−1)</c> · 흙 = 폴리곤 안쪽(멀어지는 쪽) · 기록 벽 밑 = HeadZ + H(k−1). 1단은 옹벽선 그대로(거리 0 버퍼는 빈 선).</item>
    /// <item>성토 k단: <c>step(k−1) + face</c> · 흙 = 옹벽선 쪽(가까워지는 쪽 — 부지의 성토체) · 기록 벽 밑 = HeadZ − H·k.</item>
    /// </list>
    /// 선 = <see cref="WallInPoly.ExtendEnds"/>로 늘인 옹벽선의 거리-d 버퍼 경계 ∩ 폴리곤(벽 짓기 <see cref="WallInPoly.RowsByBuffer"/>와 같은 늘임 · 같은 버퍼 인자).</summary>
    public static List<FaceLine> ZoneFaces(IReadOnlyList<Point3> poly, IReadOnlyList<bool> isWall, bool up, double headZ, int benches,
        double benchH, double benchW, double slopeW, double minFaceRun, string src, out string note, int fromBench = 1)
    {
        var res = new List<FaceLine>();
        note = "";
        var wline = WallLineOf(poly, isWall);
        if (poly == null || poly.Count < 3 || wline.Count < 2) { note = $"{src}: 옹벽선을 못 뽑았다(옹벽 점 {wline.Count})"; return res; }
        double face = Math.Max(benchH * Math.Max(0, slopeW), Math.Max(1e-4, minFaceRun));
        double step = face + Math.Max(0, benchW);
        double maxOff = step * Math.Max(0, benches - 1) + face;
        var wlExt = WallInPoly.ExtendEnds(wline, maxOff + 5.0);
        var gf = new GeometryFactory();
        double ox = poly[0].X, oy = poly[0].Y;
        Coordinate C(Point3 q) => new(q.X - ox, q.Y - oy);
        Polygon pg;
        try
        {
            pg = gf.CreatePolygon(poly.Select(C).Append(C(poly[0])).ToArray());
            if (!pg.IsValid) { note = $"{src}: 폴리곤이 제 몸을 지른다"; return res; }
        }
        catch (Exception ex) { note = $"{src}: 폴리곤을 못 만들었다 {ex.GetType().Name}"; return res; }
        LineString? Ls(IReadOnlyList<Point3> l)
        {
            var cs = new List<Coordinate>();
            foreach (var q in l) { var c = C(q); if (cs.Count == 0 || !cs[^1].Equals2D(c)) cs.Add(c); }
            return cs.Count >= 2 ? gf.CreateLineString(cs.ToArray()) : null;
        }
        var wlG = Ls(wlExt);
        if (wlG == null) { note = $"{src}: 옹벽선이 점 하나"; return res; }
        var notes = new List<string>();
        for (int k = Math.Max(1, fromBench); k <= benches; k++)
        {
            double d = up ? step * (k - 1) : step * (k - 1) + face;
            double rec = up ? headZ + benchH * (k - 1) : headZ - benchH * k;
            var lines = new List<LineString>();
            if (d < 1e-9)
            {
                var l0 = Ls(wline);
                if (l0 != null) lines.Add(l0);
            }
            else
            {
                Geometry inter;
                try
                {
                    var buf = wlG.Buffer(d, new BufferParameters { EndCapStyle = EndCapStyle.Flat });
                    inter = buf.Boundary.Intersection(pg);
                }
                catch (Exception ex) { notes.Add($"{k}단 선을 못 뽑았다 {ex.GetType().Name}"); continue; }
                var lm = new LineMerger();
                foreach (var g in NetTopologySuite.Geometries.Utilities.LinearComponentExtracter.GetLines(inter))
                    if (g is LineString l && l.Length > 1e-6) lm.Add(l);
                foreach (var g in lm.GetMergedLineStrings()) if (g is LineString l && l.Length > 1e-6) lines.Add(l);
            }
            if (lines.Count == 0) { notes.Add($"{k}단 앞면 선 없음(거리 {d:F2}m가 폴리곤 밖)"); continue; }
            int part = 0;
            foreach (var l in lines)
            {
                part++;
                var cs = l.Coordinates;
                // 흙 쪽 — 가운데 점에서 왼쪽 1cm가 옹벽선에서 멀어지면(절토) · 가까워지면(성토) 왼쪽
                int mi = Math.Max(0, cs.Length / 2 - 1);
                var a = cs[mi]; var b = cs[mi + 1];
                double tx = b.X - a.X, ty = b.Y - a.Y, tl = Math.Sqrt(tx * tx + ty * ty);
                if (tl < 1e-12) continue;
                double nx = -ty / tl, ny = tx / tl;
                var m = new Coordinate((a.X + b.X) / 2, (a.Y + b.Y) / 2);
                int soil;
                if (d < 1e-9)
                {
                    bool leftIn = pg.Contains(gf.CreatePoint(new Coordinate(m.X + nx * 0.01, m.Y + ny * 0.01)));
                    bool rightIn = pg.Contains(gf.CreatePoint(new Coordinate(m.X - nx * 0.01, m.Y - ny * 0.01)));
                    if (leftIn == rightIn) { notes.Add($"{k}단 조각{part}: 흙 쪽을 못 가렸다(폴리곤 안팎이 같다)"); continue; }
                    soil = leftIn ? +1 : -1;                           // 절토 1단 — 흙 = 폴리곤 안쪽
                }
                else
                {
                    double dl = wlG.Distance(gf.CreatePoint(new Coordinate(m.X + nx * 0.01, m.Y + ny * 0.01)));
                    double dr = wlG.Distance(gf.CreatePoint(new Coordinate(m.X - nx * 0.01, m.Y - ny * 0.01)));
                    if (Math.Abs(dl - dr) < 1e-4) { notes.Add($"{k}단 조각{part}: 흙 쪽을 못 가렸다(옹벽선 거리가 같다)"); continue; }
                    bool leftFar = dl > dr;
                    soil = up == leftFar ? +1 : -1;                    // 절토 = 멀어지는 쪽 · 성토 = 가까워지는 쪽
                }
                var f = new FaceLine { Soil = soil, FaceRun = face, SlopeW = slopeW, Src = $"{src} {k}단" + (lines.Count > 1 ? $"-{part}" : "") };
                foreach (var c in cs) f.Pts.Add(new Point3(c.X + ox, c.Y + oy, rec));
                res.Add(f);
            }
        }
        note = notes.Count > 0 ? $"{src}: " + string.Join(" · ", notes) : "";
        return res;
    }

    /// <summary>★번들 옹벽 한 줄 — 앞면 아랫선 = <see cref="WallRun.Toe"/>(절토 · 성토 모두) · 흙 = Toe → Crest 쪽(WallBand.cs:2313-2318) ·
    /// 기록 벽 밑 = Toe 점 z. 흙 쪽은 Crest 점들이 Toe의 어느 쪽에 있는지로 가른다(가르지 못하면 null + 까닭).</summary>
    public static FaceLine? RunFace(WallRun r, double slopeW, double minFaceRun, string src, out string why)
    {
        why = "";
        if (r == null || r.Toe.Count < 2) { why = $"{src}: 아랫선 점 {r?.Toe.Count ?? 0}"; return null; }
        double side = 0;
        foreach (var c in r.Crest)
        {
            // 가장 가까운 Toe 변에 대한 부호(왼쪽 +)
            double best = double.MaxValue, sgn = 0;
            for (int i = 0; i + 1 < r.Toe.Count; i++)
            {
                var a = r.Toe[i]; var b = r.Toe[i + 1];
                double ex = b.X - a.X, ey = b.Y - a.Y, L2 = ex * ex + ey * ey;
                if (L2 < 1e-18) continue;
                double t = Math.Max(0, Math.Min(1, ((c.X - a.X) * ex + (c.Y - a.Y) * ey) / L2));
                double px = a.X + ex * t - c.X, py = a.Y + ey * t - c.Y, dd = px * px + py * py;
                if (dd < best) { best = dd; sgn = Math.Sign(ex * (c.Y - a.Y) - ey * (c.X - a.X)); }
            }
            side += sgn;
        }
        if (Math.Abs(side) < 0.5 * Math.Max(1, r.Crest.Count)) { why = $"{src}: 흙 쪽을 못 가렸다(윗선이 아랫선 양쪽에 섞임 {side:F0}/{r.Crest.Count})"; return null; }
        double h = r.Height > 0 ? r.Height : 0;
        return new FaceLine
        {
            Pts = r.Toe.Select(q => new Point3(q.X, q.Y, q.Z)).ToList(),
            Soil = side > 0 ? +1 : -1,
            FaceRun = Math.Max(h * Math.Max(0, slopeW), Math.Max(1e-4, minFaceRun)),
            SlopeW = slopeW,
            Src = src,
        };
    }

    /// <summary>★최종면에서 재기(계획 2-2 · 재검토 중간 5) — 0.5 m 간격(+ 꺾임점)으로 점마다 앞쪽 5 · 15 cm와 흙 쪽(5 cm + 앞면 너비)에서 <paramref name="surf"/>를 읽어
    /// <list type="bullet">
    /// <item>벽 높이(흙 쪽 − 앞 5 cm) 0.1 m 미만 = 그 자리엔 벽이 없다(판정 규칙 — 끊는다).</item>
    /// <item>−0.1 m 아래 = 흙 쪽이 기록과 반대(⚠ · 끊는다).</item>
    /// <item>앞 5 cm가 기록 벽 밑보다 5 cm 넘게 낮다 = 선 자리 어긋남(⚠ · 끊는다).</item>
    /// <item>앞 5 · 15 cm 기울기가 앞면 기울기(1:SlopeW)의 절반보다 가파르다 = 선이 앞면 위에 얹혔다(⚠ · 끊는다).</item>
    /// </list>
    /// 남은 이어진 구간이 바닥 한 줄 — Z = 앞 5 cm − <paramref name="foot"/>. 끊기는 자리는 1 cm까지 반으로 좁혀 찾는다.</summary>
    public static List<Bottom> Measure(FaceLine f, Func<double, double, double?> surf, double foot, MeasureNote mn, double spacing = 0.5)
    {
        var res = new List<Bottom>();
        if (f.Pts.Count < 2) return res;
        // 호길이
        var cum = new double[f.Pts.Count];
        for (int i = 1; i < f.Pts.Count; i++) cum[i] = cum[i - 1] + Math.Sqrt(Sq(f.Pts[i].X - f.Pts[i - 1].X) + Sq(f.Pts[i].Y - f.Pts[i - 1].Y));
        double total = cum[^1];
        mn.Len += total;
        if (total < 1e-6) return res;
        (double X, double Y, double Rec, double Nx, double Ny) At(double s)
        {
            s = Math.Max(0, Math.Min(total, s));
            int i = 0;
            while (i < cum.Length - 2 && cum[i + 1] < s) i++;
            var a = f.Pts[i]; var b = f.Pts[i + 1];
            double L = cum[i + 1] - cum[i], t = L < 1e-12 ? 0 : (s - cum[i]) / L;
            double tx = (b.X - a.X) / Math.Max(L, 1e-12), ty = (b.Y - a.Y) / Math.Max(L, 1e-12);
            // 꺾임점에서는 두 변 방향의 평균(앞뒤 변 모두 수직에 가깝게)
            if (t < 1e-9 && i > 0) { var p = f.Pts[i - 1]; double L0 = cum[i] - cum[i - 1]; if (L0 > 1e-12) { tx += (a.X - p.X) / L0; ty += (a.Y - p.Y) / L0; } }
            else if (t > 1 - 1e-9 && i + 2 < f.Pts.Count) { var q = f.Pts[i + 2]; double L2 = cum[i + 2] - cum[i + 1]; if (L2 > 1e-12) { tx += (q.X - b.X) / L2; ty += (q.Y - b.Y) / L2; } }
            double tl = Math.Sqrt(tx * tx + ty * ty);
            if (tl < 1e-12) { tx = (b.X - a.X); ty = (b.Y - a.Y); tl = Math.Max(1e-12, Math.Sqrt(tx * tx + ty * ty)); }
            tx /= tl; ty /= tl;
            double nx = -ty * f.Soil, ny = tx * f.Soil;             // 흙 쪽 단위 법선
            double rec = a.Z + (b.Z - a.Z) * t;
            return (a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t, rec, nx, ny);
        }
        double back = 0.05 + Math.Max(0, f.FaceRun);
        double steep = 0.5 / Math.Max(1e-3, f.SlopeW > 0 ? f.SlopeW : 0.05);
        // 0 = 남김 · 1 = 벽 없음 · 2 = 못 읽음 · 3 = 반대 · 4 = 어긋남 · 5 = 앞면 위
        // ★★[v103.3 설치 전 검토 · 높음 1] 벽 높이는 <b>순 단차</b> — 흙 쪽 − 앞 5 cm에서 «땅 기울기를 그 거리만큼 늘인 몫»을 뺀다.
        //   그냥 차이로 재면 1:2 비탈(벽 없음)도 폭 0.35 m 안에서 0.175 m 올라 «벽»이 됐다(가짜 단 · 가짜 터파기가 ⚠ 없이 수량에).
        //   땅 기울기 = <b>뒤</b>(back · back + 10 cm) 기울기 — 벽이 없으면 선 뒤 땅은 그 기울기로 이어지므로 앞 5 cm부터 그만큼이 «벽 없음» 몫이다.
        //   벽이 있으면 남는 몫 ≈ H·(1 − 뒤 기울기·앞면 구배) − 0.05·뒤 기울기 &gt; H/2(뒤 기울기 &lt; steep이면) — 진짜 벽은 안 빠진다.
        //   ★[재검토 낮음 1] 앞 · 뒤 중 완만한 쪽을 쓰면 «앞 평평 + 선에서 바로 비탈»(벽 끝 · 벽 없는 자리)이 0이 돼 1:2 비탈이 0.15 m «벽»으로 남았다.
        //   앞쪽만 쓰면 벽 앞이 가파른 자리(앞면 위에 놓인 선 · 벼랑 위 벽)에서 너무 많이 빼 진짜 벽을 «반대»로 갈랐다(시험 «앞면 위»).
        //   뒤 기울기가 steep을 넘으면(뒤 표본이 앞면 위 — 기록보다 높은 벽) 빼지 않는다.
        //   ★[재검토 낮음 2] 벽 밑 = 선 자리의 땅(앞 5 cm를 앞 기울기로 5 cm 늘인 값) — 어긋남 판정과 바닥(벽 밑 − 기초)이 같은 값을 쓴다.
        // ★[v104 · 계획_v104.md 5.1 · 2판 검토 중간 6] 앞 기울기 rf · 뒤 기울기 rb도 돌려준다(단면 고르기 — 보강토 근입 · 경사 배면 ⚠)
        (int K, double Z, double H, double Rf, double Rb) Judge(double s)
        {
            var p = At(s);
            var f5 = surf(p.X - p.Nx * 0.05, p.Y - p.Ny * 0.05);
            var f15 = surf(p.X - p.Nx * 0.15, p.Y - p.Ny * 0.15);
            var bk = surf(p.X + p.Nx * back, p.Y + p.Ny * back);
            var bk2 = surf(p.X + p.Nx * (back + 0.10), p.Y + p.Ny * (back + 0.10));
            if (f5 == null || f15 == null || bk == null || bk2 == null) return (2, double.NaN, double.NaN, 0, 0);
            double rf = (f5.Value - f15.Value) / 0.10, rb = (bk2.Value - bk.Value) / 0.10;   // 흙 쪽으로 가며 오르는 기울기
            double rise = Math.Abs(rb) < steep ? rb : 0;
            double h = bk.Value - f5.Value - rise * (back + 0.05);
            if (h < -0.1) return (3, f5.Value, h, rf, rb);
            if (h < 0.1) return (1, f5.Value, h, rf, rb);
            if (Math.Abs(rf) > steep) return (5, f5.Value, h, rf, rb);            // 앞면 위에 얹힌 선 — 늘인 벽 밑을 믿을 수 없다
            double toe = f5.Value + rf * 0.05;
            if (!double.IsNaN(p.Rec) && toe < p.Rec - 0.05) return (4, toe, h, rf, rb);
            return (0, toe, h, rf, rb);
        }
        // 표본 자리 — 0.5 m 간격 + 꺾임점
        var ss = new SortedSet<double>();
        int n = Math.Max(1, (int)Math.Ceiling(total / spacing));
        for (int k = 0; k <= n; k++) ss.Add(Math.Min(total, total * k / n));
        foreach (var c in cum) ss.Add(c);
        var sl = ss.ToList();
        var js = sl.Select(Judge).ToList();
        string At3(double s) { var p = At(s); return string.Format(CultureInfo.InvariantCulture, "({0:F2},{1:F2})", p.X, p.Y); }
        // 까닭별 길이 — 표본 사이 반씩
        for (int i = 0; i < sl.Count; i++)
        {
            double w = (i > 0 ? (sl[i] - sl[i - 1]) / 2 : 0) + (i + 1 < sl.Count ? (sl[i + 1] - sl[i]) / 2 : 0);
            switch (js[i].K)
            {
                case 0: mn.Kept += w; break;
                case 1: mn.NoWall += w; break;
                case 2: mn.NoData += w; break;
                case 3: mn.Reversed += w; if (mn.RevAt.Length == 0) mn.RevAt = $"{f.Src} {At3(sl[i])}"; break;
                case 4: mn.Misplaced += w; if (mn.MisAt.Length == 0) mn.MisAt = $"{f.Src} {At3(sl[i])}"; break;
                case 5: mn.OnFace += w; if (mn.OnAt.Length == 0) mn.OnAt = $"{f.Src} {At3(sl[i])}"; break;
            }
        }
        // 이어진 남김 구간 — 양 끝은 반으로 좁혀 1 cm까지
        double Edge(double sOk, double sBad)
        {
            for (int it = 0; it < 12 && Math.Abs(sBad - sOk) > 0.01; it++)
            {
                double mid = (sOk + sBad) / 2;
                if (Judge(mid).K == 0) sOk = mid; else sBad = mid;
            }
            return sOk;
        }
        int j = 0;
        while (j < sl.Count)
        {
            if (js[j].K != 0) { j++; continue; }
            int j0 = j;
            while (j + 1 < sl.Count && js[j + 1].K == 0) j++;
            double s0 = j0 > 0 ? Edge(sl[j0], sl[j0 - 1]) : sl[j0];
            double s1 = j + 1 < sl.Count ? Edge(sl[j], sl[j + 1]) : sl[j];
            if (s1 - s0 > 0.05)
            {
                var b = new Bottom { Soil = f.Soil, Src = f.Src, SlopeW = f.SlopeW };
                var pts = new List<double> { s0 };
                for (int q = j0; q <= j; q++) if (sl[q] > s0 + 1e-6 && sl[q] < s1 - 1e-6) pts.Add(sl[q]);
                pts.Add(s1);
                foreach (double s in pts)
                {
                    var jj = Judge(s);
                    var p = At(s);
                    double z = jj.K == 0 ? jj.Z : double.NaN;
                    if (double.IsNaN(z)) continue;                    // 끝을 좁히다 판정이 흔들린 점 — 뺀다(남은 점으로 잇는다)
                    b.Pts.Add(new Point3(p.X, p.Y, z - foot));
                    b.Toe.Add(z); b.H.Add(jj.H); b.Rf.Add(jj.Rf); b.Rb.Add(jj.Rb);
                    b.WallHMax = Math.Max(b.WallHMax, jj.H);
                }
                if (b.Pts.Count >= 2) res.Add(b);
            }
            j++;
        }
        return res;
    }

    static double Sq(double v) => v * v;

    /// <summary>★[설치 전 검토 높음 1] 구간 옹벽의 단 수 — 벽 짓기와 같은 셈(<see cref="WallInPoly.BenchCount"/> · <see cref="WallInPoly.BenchCountDown"/>)을
    /// 폴리곤 안 <paramref name="surf"/> 최고(절토) · 최저(성토)로 한다. 못 재면 0(부르는 쪽이 ⚠).</summary>
    public static int ZoneBenches(IReadOnlyList<Point3> poly, bool up, double headZ, double benchH, Func<double, double, double?> surf, double grid)
    {
        double? g = up ? WallInPoly.MaxGroundIn(poly, surf, grid, out _, out _) : WallInPoly.MinGroundIn(poly, surf, grid, out _, out _);
        if (g == null) return 0;
        return up ? WallInPoly.BenchCount(headZ, g.Value, benchH) : WallInPoly.BenchCountDown(headZ, g.Value, benchH);
    }

    /// <summary>★바닥 띠(링) — 바닥 줄을 흙 쪽으로 <paramref name="back"/> · 앞쪽으로 <paramref name="front"/>만큼 한쪽 버퍼 둘의 합(NTS · 끝 평평).
    /// 링 꼭짓점 z = 바닥 줄 위 가장 가까운 점의 z(줄 따라 선형 보간). 반시계 · 첫점 반복 없음. 못 만들면 null.
    /// <para>★[1021 화면 없는 Civil 시험 ③ — 바닥이 1:3으로 기울었다] 테두리 변을 <paramref name="maxEdge"/>마다 나눠 점마다 z를 준다 — 꼭짓점이 모서리에만 있으면
    /// ㄷ자 옹벽선(측선 → 벽 → 측선)의 모서리 점이 측선 쪽 높은 바닥 z를 받아 그 사이 바닥을 Civil이 비스듬히 잇는다. 부르는 쪽은 바닥 줄도 브레이크라인으로 넣는다.</para></summary>
    public static List<Point3>? StripRing(Bottom b, double back, double front, out string why, double maxEdge = 0.5)
    {
        why = "";
        if (b.Pts.Count < 2) { why = "바닥 줄 점이 모자람"; return null; }
        var gf = new GeometryFactory();
        double ox = b.Pts[0].X, oy = b.Pts[0].Y;
        var cs = new List<Coordinate>();
        var zs = new List<double>();        // ★[설치 전 검토 낮음 7] 같은 점을 뺀 목록과 같은 차례의 z(순번이 어긋나지 않게)
        foreach (var q in b.Pts) { var c = new Coordinate(q.X - ox, q.Y - oy); if (cs.Count == 0 || !cs[^1].Equals2D(c)) { cs.Add(c); zs.Add(q.Z); } }
        if (cs.Count < 2) { why = "바닥 줄이 점 하나"; return null; }
        var ls = gf.CreateLineString(cs.ToArray());
        var bp = new BufferParameters { EndCapStyle = EndCapStyle.Flat, IsSingleSided = true, JoinStyle = JoinStyle.Mitre, MitreLimit = 4.0 };
        Geometry? g = null;
        try
        {
            // NTS 한쪽 버퍼 — 거리 + = 왼쪽 · − = 오른쪽
            double sBack = b.Soil > 0 ? back : -back, sFront = b.Soil > 0 ? -front : front;
            var parts = new List<Geometry>();
            if (back > 1e-9) parts.Add(ls.Buffer(sBack, bp));
            if (front > 1e-9) parts.Add(ls.Buffer(sFront, bp));
            if (parts.Count == 0) { why = "뒤 · 앞이 모두 0"; return null; }
            g = parts.Count == 1 ? parts[0] : NetTopologySuite.Operation.Union.CascadedPolygonUnion.Union(parts);
        }
        catch (Exception ex) { why = "띠 버퍼 실패 " + ex.GetType().Name; return null; }
        Polygon? big = null;
        for (int i = 0; i < g.NumGeometries; i++) if (g.GetGeometryN(i) is Polygon p && (big == null || p.Area > big.Area)) big = p;
        if (big == null || big.Area < 1e-4) { why = "띠가 비었다"; return null; }
        if (g.NumGeometries > 1) why = $"띠가 {g.NumGeometries}조각 — 가장 큰 것만";
        var ring0 = big.ExteriorRing.Coordinates.ToList();
        if (ring0.Count > 1 && ring0[0].Equals2D(ring0[^1])) ring0.RemoveAt(ring0.Count - 1);
        if (!NetTopologySuite.Algorithm.Orientation.IsCCW(big.ExteriorRing.Coordinates)) ring0.Reverse();
        // 변을 maxEdge 이하로 나눈다(원래 꼭짓점은 그대로 · 나눈 점은 변 위 — 넓이 · 모양 그대로)
        var ring = new List<Coordinate>(ring0.Count * 2);
        for (int i = 0; i < ring0.Count; i++)
        {
            var p = ring0[i]; var q = ring0[(i + 1) % ring0.Count];
            ring.Add(p);
            double L = Math.Sqrt(Sq(q.X - p.X) + Sq(q.Y - p.Y));
            int k = maxEdge > 0 ? (int)Math.Ceiling(L / maxEdge) : 1;
            for (int j = 1; j < k; j++) ring.Add(new Coordinate(p.X + (q.X - p.X) * j / k, p.Y + (q.Y - p.Y) * j / k));
        }
        // z — 바닥 줄 위 가장 가까운 점
        var res = new List<Point3>(ring.Count);
        foreach (var c in ring)
        {
            double best = double.MaxValue, z = zs[0];
            for (int i = 0; i + 1 < cs.Count; i++)
            {
                var a = cs[i]; var e = cs[i + 1];
                double ex = e.X - a.X, ey = e.Y - a.Y, L2 = ex * ex + ey * ey;
                double t = L2 < 1e-18 ? 0 : Math.Max(0, Math.Min(1, ((c.X - a.X) * ex + (c.Y - a.Y) * ey) / L2));
                double dx = a.X + ex * t - c.X, dy = a.Y + ey * t - c.Y, dd = dx * dx + dy * dy;
                if (dd < best) { best = dd; z = zs[i] + (zs[i + 1] - zs[i]) * t; }
            }
            res.Add(new Point3(c.X + ox, c.Y + oy, z));
        }
        return res;
    }

    /// <summary>★[재검토 중간 2] Civil이 링 브레이크라인만으로 지은 조각은 볼록 껍질을 메운 삼각형이 섞인다 — 무게중심이 바깥 링 밖인 것을 버린다.</summary>
    public static List<Tri> DropOutside(IReadOnlyList<Tri> tris, IReadOnlyList<Point3> outer, out int dropped)
    {
        dropped = 0;
        var keep = new List<Tri>(tris.Count);
        foreach (var t in tris)
        {
            double cx = (t.A.X + t.B.X + t.C.X) / 3, cy = (t.A.Y + t.B.Y + t.C.Y) / 3;
            if (GradingGeometry.PointInRing(outer, cx, cy)) keep.Add(t); else dropped++;
        }
        return keep;
    }
}
