using System.Text;

namespace DH.Grading.Core;

/// <summary>★★[JACK 1123 «닫힌 폴리선 → 출구에서 두 갈래» · «합류 · 교차 자리에 집수정 자동»] U형 측구 — 셈만(Civil 없음).
/// <list type="bullet">
/// <item>닫힌 선 → 두 갈래(<see cref="SplitLoop"/>) — 둘레에서 가장 낮은 곳 = 출구 · 가장 높은 곳 = 시작 · 시작 → 양쪽 → 출구.
/// 평평(높이차 &lt; <see cref="Settings.LoopFlat"/>) · 지표 없음 = 출구는 그린 첫 점 · 시작은 둘레 반대편.</item>
/// <item>합류 · 교차 · 출구 집수정(<see cref="Link"/>) — 측구 끝점이 다른 측구 선에서 <see cref="Settings.JoinTol"/> 안 = 합류 · 두 선이 가로지름 = 교차 · 끝끼리만 = 출구.
/// 시작끼리만 만난 곳(분수령)은 집수정 없음. 집수정은 단차 집수정과 같은 꼴(<see cref="Basin"/> · <see cref="BasinCup"/>) · 측구 몸체는 선이 집수정 바깥 네모를 나가는 곳에서 끊김(<see cref="Ditch.Cuts"/>).</item>
/// </list></summary>
public static partial class UDitch
{
    /// <summary>측구 선의 집수정 자리 [A, B](측점 m) — 이 자리엔 U 없음 · 이름(J1) · 종류 · 알림.</summary>
    public sealed class Cut
    {
        public double A, B;
        public string Name = "", Kind = "";
        public List<string> Warn = new();
    }

    /// <summary>집수정에 이어진 측구 하나 — 닿은 측점 · 역할(start · end · mid · cross) · 들어오는(In) · 나가는(Out) 바닥고 · 그 자리 지표.</summary>
    public sealed class JMember
    {
        public Ditch D = null!;
        public int Index;
        public double S, Px, Py;
        public string Role = "";
        public double InV = double.NaN, OutV = double.NaN, Ground = double.NaN;
        public Cut Cut = new();
        public bool In => Role is "end" or "mid" or "cross";
        public bool Out => Role is "start" or "mid" or "cross";
        public string RoleText => Role switch { "end" => "끝", "start" => "시작", "mid" => "중간", _ => "교차" };
    }

    /// <summary>합류 · 교차 · 출구 집수정 — 이름(J1) · 종류 · 꼴(<see cref="Basin"/> — 측점 S0 · S1은 안 씀) · 이어진 측구 · 알림 · 3D 머리를 붙일 측구.</summary>
    public sealed class Junction
    {
        public string Name = "", Kind = "";
        public Basin B = new();
        public readonly List<JMember> M = new();
        public readonly List<string> Warn = new(), Log = new();
        /// <summary>★[검토 L10] 보고서에만 적는 알림(출구 — 내보낼 곳 없음 등 · 창 ⚠ · 측구 알림엔 안 감).</summary>
        public readonly List<string> Notes = new();
        public string Owner = "";
        public bool Ok => double.IsFinite(B.Floor) && double.IsFinite(B.ZTop) && B.ZTop > B.ZBottom;
        /// <summary>콘크리트(㎥) = 바깥 상자 − 안 빈 곳(단차 집수정과 같은 셈).</summary>
        public double Volume => Ok ? B.Outer * B.Outer * (B.ZTop - B.ZBottom) - B.Inner * B.Inner * (B.ZTop - B.Floor) : 0;
        /// <summary>«D-001-1 + D-001-2» · «D-002 → D-001» · «D-001 × D-003».</summary>
        public string Members
        {
            get
            {
                if (Kind == "교차")
                {
                    string x = string.Join(" × ", M.Where(m => m.Role == "cross").Select(m => m.D.Id));
                    var o = M.Where(m => m.Role != "cross").Select(m => m.D.Id).ToList();
                    return o.Count > 0 ? x + " + " + string.Join(" + ", o) : x;
                }
                var ins = M.Where(m => m.Role == "end").Select(m => m.D.Id).ToList();
                var thr = M.Where(m => m.Role is "mid" or "start").Select(m => m.D.Id).ToList();
                return thr.Count == 0 ? string.Join(" + ", ins) : (ins.Count > 0 ? string.Join(" + ", ins) + " → " : "") + string.Join(" + ", thr);
            }
        }
        public string Label => $"{Name}({Kind} · {Members})";
    }

    // ══════════════════════════════ 닫힌 선 → 두 갈래 ══════════════════════════════

    /// <summary>닫힌 선 나눔 — 갈래 1(시작 → 둘레 그린 차례 → 출구) · 갈래 2(시작 → 거꾸로 → 출구) [x, y, z] · 둘레 · 출구 · 시작 측점(첫 점 0) · 높이 · 까닭 글.</summary>
    public sealed class LoopSplit
    {
        public List<double[]> A = new(), B = new();
        public double Perim, SOut, SStart, ZOut = double.NaN, ZStart = double.NaN;
        public bool Flat;
        public string Note = "";
    }

    /// <summary>닫힌 고리(<paramref name="ring"/> = [x, y, z(지표 · 3D Z · NaN)] · 첫 점 = 끝 점이어도 됨)를 출구 · 시작으로 둘로 나눔.
    /// 둘레를 <see cref="Settings.SampleStep"/>마다 읽음(<paramref name="ground"/> → 없으면 마디 z 보간).
    /// 출구가 꼭짓점에서 <paramref name="snap"/>(집수정 바깥폭) 안이면 꼭짓점으로(집수정이 모서리를 반만 덮지 않게) · 시작은 출구에서 둘레로 <paramref name="snap"/> 넘게 떨어진 곳 중 가장 높은 곳(두 갈래 모두 집수정 밖으로 나가게).
    /// 점이 3개 미만이면 null.</summary>
    public static LoopSplit? SplitLoop(IReadOnlyList<double[]> ring, Func<double, double, double>? ground, Settings s, double snap)
    {
        var p = CleanPts(ring);
        if (p.Count >= 2 && Math.Abs(p[0][0] - p[^1][0]) < 1e-3 && Math.Abs(p[0][1] - p[^1][1]) < 1e-3) p.RemoveAt(p.Count - 1);
        if (p.Count < 3) return null;
        int n = p.Count;
        var g = new Path2(p.Concat(new[] { p[0] }).ToList());
        double P = g.L;
        if (!(P > 0.1)) return null;
        var o = new LoopSplit { Perim = P };
        double Z(double sta)
        {
            sta = Mod(sta, P);
            var (x, y) = g.At(sta);
            double z = double.NaN;
            if (ground != null) try { z = ground(x, y); } catch { }
            if (double.IsFinite(z)) return z;
            int e = g.Edge(sta);
            double za = g.P[e][2], zb = g.P[e + 1][2], l = g.Sta[e + 1] - g.Sta[e];
            return double.IsFinite(za) && double.IsFinite(zb) && l > 1e-12 ? za + (zb - za) * (sta - g.Sta[e]) / l : double.IsFinite(za) ? za : zb;
        }
        var st = new List<double>();
        for (int i = 0; i < n; i++)
        {
            double a = g.Sta[i], b = g.Sta[i + 1];
            int k = Math.Max(1, (int)Math.Ceiling((b - a) / s.SampleStep - 1e-9));
            for (int j = 0; j < k; j++) st.Add(a + (b - a) * j / k);
        }
        var zs = st.Select(Z).ToArray();
        var fin = Enumerable.Range(0, st.Count).Where(i => double.IsFinite(zs[i])).ToList();
        double zmin = fin.Count > 0 ? fin.Min(i => zs[i]) : double.NaN, zmax = fin.Count > 0 ? fin.Max(i => zs[i]) : double.NaN;
        double RingDist(double a, double b) { double d = Math.Abs(Mod(a - b, P)); return Math.Min(d, P - d); }
        if (fin.Count < 2 || !(zmax - zmin >= s.LoopFlat))
        {
            o.Flat = true;
            o.SOut = 0; o.SStart = P / 2;
            o.Note = (fin.Count < 2 ? "지표 없음 → 출구 = 그린 첫 점 · 시작 = 둘레 반대편"
                                    : string.Format(Ic, "평평(높이차 {0:0.000} m < {1:0.00}) → 출구 = 그린 첫 점 · 시작 = 둘레 반대편", zmax - zmin, s.LoopFlat))
                     + "(출구를 옮기려면 선의 첫 점을 옮기기)";
        }
        else
        {
            int io = fin.OrderBy(i => zs[i]).ThenBy(i => st[i]).First();
            o.SOut = st[io];
            // 꼭짓점 맞춤 — 가장 가까운 꼭짓점이 snap 안이면
            int best = -1; double bd = double.MaxValue;
            for (int v = 0; v < n; v++) { double dd = RingDist(o.SOut, g.Sta[v]); if (dd < bd) { bd = dd; best = v; } }
            string snapNote = "";
            if (best >= 0 && bd > 1e-6 && bd <= snap) { snapNote = string.Format(Ic, "(꼭짓점으로 {0:0.00} m 옮김)", bd); o.SOut = g.Sta[best]; }
            var far = fin.Where(i => RingDist(st[i], o.SOut) > snap + 1e-9).ToList();
            if (far.Count == 0) { o.SStart = Mod(o.SOut + P / 2, P); o.Note = "둘레가 짧음 → 시작 = 출구 반대편"; }
            else
            {
                int ih = far.OrderByDescending(i => zs[i]).ThenBy(i => st[i]).First();
                o.SStart = st[ih];
            }
            o.ZOut = Z(o.SOut); o.ZStart = Z(o.SStart);
            if (o.Note.Length == 0) o.Note = string.Format(Ic, "출구 = 가장 낮은 곳 {0:0.000}{1} · 시작 = 가장 높은 곳 {2:0.000}", o.ZOut, snapNote, o.ZStart);
        }
        if (o.Flat) { o.ZOut = Z(o.SOut); o.ZStart = Z(o.SStart); }
        // 갈래 — 시작에서 앞으로(그린 차례) · 뒤로, 출구까지
        double fwd = Mod(o.SOut - o.SStart, P);
        if (fwd < 1e-6) { o.SStart = Mod(o.SOut + P / 2, P); fwd = P / 2; }
        double[] Pt(double sta) { var (x, y) = g.At(Mod(sta, P)); return new[] { x, y, Z(sta) }; }
        o.A.Add(Pt(o.SStart));
        foreach (var v in Enumerable.Range(0, n).Select(v => (V: v, F: Mod(g.Sta[v] - o.SStart, P))).Where(q => q.F > 1e-6 && q.F < fwd - 1e-6).OrderBy(q => q.F))
            o.A.Add(new[] { p[v.V][0], p[v.V][1], Z(g.Sta[v.V]) });
        o.A.Add(Pt(o.SOut));
        double back = P - fwd;
        o.B.Add(Pt(o.SStart));
        foreach (var v in Enumerable.Range(0, n).Select(v => (V: v, F: Mod(o.SStart - g.Sta[v], P))).Where(q => q.F > 1e-6 && q.F < back - 1e-6).OrderBy(q => q.F))
            o.B.Add(new[] { p[v.V][0], p[v.V][1], Z(g.Sta[v.V]) });
        o.B.Add(Pt(o.SOut));
        o.A = CleanPts(o.A); o.B = CleanPts(o.B);
        return o;
    }

    static double Mod(double a, double m) { double r = a % m; return r < 0 ? r + m : r; }

    /// <summary>닫힌 선 갈래를 하나 더 만들 때 — 규격 · 덮개 · 집수 · 자동 바닥고 · 지표 따라감을 같게(점 · 표본 · 바닥고는 따로 읽음).</summary>
    public static Ditch LoopSibling(Ditch d, int branch) => new()
    {
        Id = LoopId(d.Id, branch), LineHandle = d.LineHandle, Size = d.Size.Copy(), InvertAuto = d.InvertAuto, AreaHa = d.AreaHa, C = d.C,
        Follow = d.Follow, Grating = d.Grating, Loop = branch, StartInvert = d.StartInvert, EndInvert = d.EndInvert,
    };

    /// <summary>창에서 한 갈래를 고치면 짝 갈래도 같게(규격 · 덮개 · 집수면적 · C · 지표 따라감 — 손 바닥고는 안 건드림). 바꾼 짝 수.</summary>
    public static int SyncLoop(Record rec, Ditch d, Settings s)
    {
        if (d.Loop == 0 || d.LineHandle.Length == 0) return 0;
        int k = 0;
        foreach (var o in rec.Ditches)
        {
            if (ReferenceEquals(o, d) || o.Loop == 0 || o.LineHandle != d.LineHandle) continue;
            o.Size = d.Size.Copy(); o.Grating = d.Grating; o.AreaHa = d.AreaHa; o.C = d.C; o.Follow = d.Follow;
            ApplyAuto(o, s);   // ★[검토 M1] 손 바닥고(InvertAuto 거짓)는 짝 맞춤이 건드리지 않음 — 자동일 때만 규격 따라 다시 셈
            k++;
        }
        return k;
    }

    // ══════════════════════════════ 합류 · 교차 · 출구 집수정 ══════════════════════════════

    /// <summary>기록 전체 — 측구마다 집수정 자리(<see cref="Ditch.Cuts"/>) · 닫힌 선 갈래 시작 맞댐(<see cref="Ditch.StartPrev"/>) · <see cref="Record.Junctions"/>를 새로 셈.
    /// 점(<see cref="Ditch.Pts"/>) · 표본이 바뀐 뒤(다시 읽기 · 창 고침) 부른다. 여러 번 불러도 같음.</summary>
    public static void Link(Record rec, Settings s)
    {
        rec.Junctions = new();
        var ds = rec.Ditches;
        foreach (var d in ds) { d.Cuts = new(); d.StartPrev = null; }
        // ① 닫힌 선 갈래 — 시작이 같은 점이면 마구리를 둘레 이등분 면으로
        foreach (var grp in ds.Where(d => d.Loop > 0 && d.LineHandle.Length > 0).GroupBy(d => d.LineHandle))
        {
            var a = grp.FirstOrDefault(x => x.Loop == 1); var b = grp.FirstOrDefault(x => x.Loop == 2);
            if (a == null || b == null) continue;
            var pa = CleanPts(a.Pts); var pb = CleanPts(b.Pts);
            if (pa.Count < 2 || pb.Count < 2 || Math.Abs(pa[0][0] - pb[0][0]) > 1e-3 || Math.Abs(pa[0][1] - pb[0][1]) > 1e-3) continue;
            a.StartPrev = new[] { pb[1][0], pb[1][1] };
            b.StartPrev = new[] { pa[1][0], pa[1][1] };
        }
        double tol = s.JoinTol;
        var gs = ds.Select(d => { var p = CleanPts(d.Pts); if (p.Count < 2) return null; var g = new Path2(p); return g.L >= 0.1 ? g : null; }).ToList();
        // ② 닿은 곳 — 끝점 ↔ 다른 선(끝 · 중간) · 두 선이 가로지름(끝 밖)
        var cand = new List<(double X, double Y, List<(int I, double S, string Role)> E)>();
        for (int i = 0; i < ds.Count; i++)
        {
            var gi = gs[i];
            if (gi == null) continue;
            foreach (var (si, ri) in new[] { (0.0, "start"), (gi.L, "end") })
            {
                var (x, y) = gi.At(si);
                for (int j = 0; j < ds.Count; j++)
                {
                    var gj = gs[j];
                    if (j == i || gj == null) continue;
                    double sj = gj.Nearest(x, y, out double dist);
                    if (dist > tol) continue;
                    string rj = sj <= tol ? "start" : sj >= gj.L - tol ? "end" : "mid";
                    if (rj == "start") sj = 0; else if (rj == "end") sj = gj.L;
                    var (px, py) = rj == "mid" ? gj.At(sj) : (x, y);
                    cand.Add((px, py, new() { (i, si, ri), (j, sj, rj) }));
                }
            }
        }
        for (int i = 0; i < ds.Count; i++)
            for (int j = i + 1; j < ds.Count; j++)
            {
                var a = gs[i]; var b = gs[j];
                if (a == null || b == null) continue;
                for (int e = 0; e + 1 < a.P.Count; e++)
                {
                    double ax = a.P[e][0], ay = a.P[e][1], rx = a.P[e + 1][0] - ax, ry = a.P[e + 1][1] - ay;
                    for (int f = 0; f + 1 < b.P.Count; f++)
                    {
                        double bx = b.P[f][0], by = b.P[f][1], qx = b.P[f + 1][0] - bx, qy = b.P[f + 1][1] - by;
                        if (Math.Max(ax, ax + rx) < Math.Min(bx, bx + qx) || Math.Min(ax, ax + rx) > Math.Max(bx, bx + qx)
                            || Math.Max(ay, ay + ry) < Math.Min(by, by + qy) || Math.Min(ay, ay + ry) > Math.Max(by, by + qy)) continue;
                        double den = rx * qy - ry * qx;
                        if (Math.Abs(den) < 1e-12) continue;
                        double t = ((bx - ax) * qy - (by - ay) * qx) / den, u = ((bx - ax) * ry - (by - ay) * rx) / den;
                        if (t < 0 || t > 1 || u < 0 || u > 1) continue;
                        double si = a.Sta[e] + (a.Sta[e + 1] - a.Sta[e]) * t, sj = b.Sta[f] + (b.Sta[f + 1] - b.Sta[f]) * u;
                        if (si <= tol || si >= a.L - tol || sj <= tol || sj >= b.L - tol) continue;   // 끝 근처는 ②의 끝점 규칙이 잡음
                        cand.Add((ax + rx * t, ay + ry * t, new() { (i, si, "cross"), (j, sj, "cross") }));
                    }
                }
            }
        // ③ 가까운 곳(JoinTol 안)은 한 집수정으로
        var par = Enumerable.Range(0, cand.Count).ToArray();
        int Find(int x) { while (par[x] != x) x = par[x] = par[par[x]]; return x; }
        for (int i = 0; i < cand.Count; i++)
            for (int j = i + 1; j < cand.Count; j++)
                if (Math.Sqrt(Sq(cand[i].X - cand[j].X) + Sq(cand[i].Y - cand[j].Y)) <= tol) par[Find(j)] = Find(i);
        var groups = Enumerable.Range(0, cand.Count).GroupBy(Find).Select(gp => gp.SelectMany(c => cand[c].E).ToList()).ToList();
        // ④ 꼴(가운데 · 방향 · 크기) — ★[검토 M2] 바깥 네모가 겹치는 집수정은 하나로 묶음(겹쳐 짓고 콘크리트를 두 번 세지 않게) · 묶은 뒤 다시 셈
        var shapes = groups.Select(gp => Shape(gp, ds, gs, s)).ToList();
        for (int guard = 0; guard < 1000; guard++)
        {
            int hi = -1, hj = -1;
            for (int i = 0; i < shapes.Count && hi < 0; i++)
                for (int j = i + 1; j < shapes.Count; j++)
                    if (shapes[i] != null && shapes[j] != null && Overlap(shapes[i]!.B, shapes[j]!.B)) { hi = i; hj = j; break; }
            if (hi < 0) break;
            groups[hi].AddRange(groups[hj]); groups.RemoveAt(hj); shapes.RemoveAt(hj);
            shapes[hi] = Shape(groups[hi], ds, gs, s);
            shapes[hi]?.Log.Add("겹친 집수정을 하나로 묶음");
        }
        // 이름 · 측구 끊음
        var nodes = shapes.Where(j => j != null).Select(j => j!).OrderBy(j => j.M.Min(m => m.Index)).ThenBy(j => j.M.OrderBy(m => m.Index).First().S).ToList();
        for (int k = 0; k < nodes.Count; k++)
        {
            var j = nodes[k];
            j.Name = "J" + (k + 1).ToString(Ic);
            for (int i = 0; i < j.Log.Count; i++) j.Log[i] = j.Name + " " + j.Log[i];
            var bx = j.B;
            foreach (var m in j.M)
            {
                var g = gs[m.Index]!;
                double a = m.Role == "start" ? 0 : Exit(g, m.S, -1, bx.X, bx.Y, bx.Ex, bx.Ey, bx.Outer / 2);
                double b = m.Role == "end" ? g.L : Exit(g, m.S, +1, bx.X, bx.Y, bx.Ex, bx.Ey, bx.Outer / 2);
                m.Cut = new Cut { A = a, B = b, Name = j.Name, Kind = j.Kind };
                m.D.Cuts.Add(m.Cut);
            }
        }
        // ⑤ 높이 — 측구 짜임(집수정 자리 뺀) 뒤: 안바닥 = 이어진 측구 바닥 최저 − 침전 · 윗면 = max(지표 − 덮개, 측구 윗면 최고)
        var lays = new Dictionary<Ditch, Layout>();
        Layout LayOf(Ditch d) => lays.TryGetValue(d, out var l) ? l : lays[d] = LayoutOf(d, s);
        foreach (var j in nodes)
        {
            double wetTop = double.NegativeInfinity, gmax = double.NegativeInfinity;
            foreach (var m in j.M)
            {
                var lay = LayOf(m.D);
                m.Ground = GroundAtSta(m.D, gs[m.Index]!, m.S);
                if (double.IsFinite(m.Ground)) gmax = Math.Max(gmax, m.Ground);
                if (lay.Error.Length > 0) { j.Warn.Add($"{j.Name}: {m.D.Id} — {lay.Error}"); continue; }
                // ★[검토 M2] 단차 집수정 · 관 자리와 겹침
                if (lay.Basins.Any(q => q.S0 < m.Cut.B - 1e-6 && q.S1 > m.Cut.A + 1e-6) || lay.Pipes.Any(q => q.S0 < m.Cut.B - 1e-6 && q.S1 > m.Cut.A + 1e-6))
                    j.Warn.Add($"{j.Name}: {m.D.Id} 단차 집수정 · 관 자리와 겹침 → 선 확인");
                if (m.In) m.InV = lay.Segs.Where(q => q.S1 <= m.Cut.A + 1e-6).Select(q => q.V1).DefaultIfEmpty(double.NaN).Last();
                if (m.Out) m.OutV = lay.Segs.Where(q => q.S0 >= m.Cut.B - 1e-6).Select(q => q.V0).DefaultIfEmpty(double.NaN).First();
                foreach (double v in new[] { m.InV, m.OutV }) if (double.IsFinite(v)) wetTop = Math.Max(wetTop, v + m.D.Size.H);
            }
            var inv = j.M.SelectMany(m => new[] { m.InV, m.OutV }).Where(double.IsFinite).ToList();
            // ★[JACK 1124 «못 지음으로 빼지 말 것»] 이어진 측구 짜임이 모두 없음(선이 집수정보다 짧음 등) → 측구마다 바닥 추정:
            //   시작 · 끝 바닥고 보간 → 없으면 지표 − 덮개 − 안높이(자동 바닥고 규칙) · 안바닥 = 그 최저 − 침전(아래 같은 규칙) · 물 거꾸로 판정에는 안 씀
            if (inv.Count == 0)
            {
                var how = new List<string>();
                foreach (var m in j.M)
                {
                    var g = gs[m.Index]!;
                    double v = double.NaN;
                    if (double.IsFinite(m.D.StartInvert) && double.IsFinite(m.D.EndInvert) && g.L > 1e-9) { v = m.D.StartInvert + (m.D.EndInvert - m.D.StartInvert) * Math.Clamp(m.S / g.L, 0, 1); how.Add(m.D.Id + " 시작 · 끝 바닥고 보간"); }
                    else if (double.IsFinite(m.Ground)) { v = m.Ground - s.Cover - m.D.Size.H; how.Add(m.D.Id + " 지표 − 덮개 − 안높이"); }
                    if (!double.IsFinite(v)) continue;
                    inv.Add(v);
                    wetTop = Math.Max(wetTop, v + m.D.Size.H);
                }
                if (inv.Count > 0) j.Notes.Add(string.Format(Ic, "{0} 바닥고 추정 — 이어진 측구 구간 없음 → {1} · 최저 {2:0.000} − 침전 {3:0.00} m", j.Name, string.Join(" · ", how), inv.Min(), s.BasinSump));
            }
            if (inv.Count == 0) { j.Warn.Add($"{j.Name}: 이어진 측구 바닥고 · 지표를 못 셈 → 깊이 미정(평면 · 개소만) → 측구 선 · 바닥고 확인"); j.B.Floor = double.NaN; }
            else
            {
                j.B.Floor = inv.Min() - s.BasinSump;
                j.B.ZBottom = j.B.Floor - s.BasinFloor;
                j.B.ZTop = Math.Max(double.IsFinite(gmax) ? gmax - s.Cover : double.NegativeInfinity, wetTop);
                var ins = j.M.Where(m => m.In && double.IsFinite(m.InV)).ToList();
                j.B.InV = ins.Count > 0 ? ins.Min(m => m.InV) : double.NaN;
                var outs = j.M.Where(m => m.Out && double.IsFinite(m.OutV)).ToList();
                j.B.OutV = outs.Count > 0 ? outs.Min(m => m.OutV) : double.NaN;
                // 물이 거꾸로 — 나가는 측구 바닥이 들어오는 측구 바닥(최저)보다 높음
                if (ins.Count > 0)
                    foreach (var m in outs)
                        if (m.OutV > j.B.InV + 1e-3)
                            j.Warn.Add(string.Format(Ic, "{0}: {1} 나가는 바닥 {2:0.000} > 들어오는 바닥 {3:0.000} — 물이 거꾸로 → 방향/높이 확인", j.Name, m.D.Id, m.OutV, j.B.InV));
            }
            // ★[조정자 1123 · 검토 L10] 출구 — 나가는 측구가 없음(관은 아직 안 그림) → 보고서에만 «알림» 한 줄(창 ⚠에 늘 뜨면 안 보게 됨)
            if (!j.M.Any(m => m.Out)) j.Notes.Add($"{j.Name} 출구 — 내보낼 곳 없음 → 관 · 측구 연결 확인");
            foreach (var m in j.M) m.Cut.Warn = j.Warn;
            j.Log.Add(string.Format(Ic, "{0}({1}) 가운데 ({2:0.000}, {3:0.000}) · 안 {4:0.00} · 바깥 {5:0.00} · 윗면 {6:0.000} · 안바닥 {7:0.000} · {8}",
                                    j.Name, j.Kind, j.B.X, j.B.Y, j.B.Inner, j.B.Outer, j.B.ZTop, j.B.Floor,
                                    string.Join(" / ", j.M.Select(m => string.Format(Ic, "{0} {1} 측점 {2:0.000} 끊음 {3:0.000}~{4:0.000} 들어옴 {5} 나감 {6}",
                                        m.D.Id, m.RoleText, m.S, m.Cut.A, m.Cut.B, F(m.InV, "0.000"), F(m.OutV, "0.000"))))));
            rec.Junctions.Add(j);
        }
    }

    /// <summary>측점 <paramref name="s0"/>에서 <paramref name="dir"/>(+ 하류 · − 상류) 쪽으로 선을 따라가 집수정 바깥 네모(가운데 · 방향 · 반폭 h)를 나가는 측점.
    /// 시작 점이 이미 밖이면 그 측점 · 끝까지 안이면 선 끝.</summary>
    static double Exit(Path2 g, double s0, int dir, double cx, double cy, double ex, double ey, double h)
    {
        double U(double x, double y) => (x - cx) * ex + (y - cy) * ey;
        double V(double x, double y) => -(x - cx) * ey + (y - cy) * ex;
        var (x0, y0) = g.At(s0);
        if (Math.Max(Math.Abs(U(x0, y0)), Math.Abs(V(x0, y0))) > h + 1e-9) return s0;
        var stops = dir > 0 ? g.Sta.Where(t => t > s0 + 1e-9).ToList() : g.Sta.Where(t => t < s0 - 1e-9).Reverse().ToList();
        double cur = s0;
        foreach (double nxt in stops)
        {
            var (xa, ya) = g.At(cur); var (xb, yb) = g.At(nxt);
            double ua = U(xa, ya), va = V(xa, ya), du = U(xb, yb) - ua, dv = V(xb, yb) - va, te = double.PositiveInfinity;
            foreach (var (p0, dp) in new[] { (ua, du), (va, dv) })
            {
                if (dp > 1e-15) te = Math.Min(te, (h - p0) / dp);
                else if (dp < -1e-15) te = Math.Min(te, (-h - p0) / dp);
            }
            if (te <= 1) return cur + (nxt - cur) * Math.Max(0, te);
            cur = nxt;
        }
        return dir > 0 ? g.L : 0;
    }

    /// <summary>측구 선 측점의 지표(표본 → 마디 z 보간 · 못 읽으면 NaN).</summary>
    static double GroundAtSta(Ditch d, Path2 g, double sta)
    {
        var sm = SamplesOf(d, g).Where(q => double.IsFinite(q.Z)).ToList();
        if (sm.Count == 0) return double.NaN;
        if (sm.Count == 1) return sm[0].Z;
        return Interp(sm.Select(q => q.S).ToArray(), sm.Select(q => q.Z).ToArray(), sta);
    }

    /// <summary>창 · 보고서 — 측구 집수정 자리 알림을 종단 알림에 붙임.</summary>
    static void JunctionWarn(Ditch d, Profile pr)
    {
        foreach (var c in d.Cuts ?? new())
            foreach (var w in c.Warn)
                if (!pr.Warn.Contains(w)) pr.Warn.Add(w);
    }

    /// <summary>합류 · 교차 · 출구 집수정 솔리드 — 단차 집수정과 같은 컵(바깥 상자 − 안 빈 곳) + 그레이팅 덮개. 바닥고를 못 셌으면 빈 것.</summary>
    public static List<Solid> JunctionSolids(Junction j, Settings s)
    {
        if (!j.Ok) return new List<Solid>();
        var cup = BasinCup(j.B); cup.Part = "합류 집수정";
        var lid = Box("합류 집수정 덮개", Square(j.B, j.B.Inner / 2), j.B.ZTop - Math.Min(s.GrateT, j.B.ZTop - j.B.Floor), j.B.ZTop);
        return new List<Solid> { cup, lid };
    }

    /// <summary>[3D 보기] 합류 · 교차 · 출구 집수정 — (붙일 측구 Id, 칸) · V = [x, y, zTop, zBottom, w, l, rot, inner](장면 좌표 — <see cref="SceneParts"/>와 같은 꼴).</summary>
    public static List<(string Owner, ScenePart Part)> JunctionParts(Record rec, Settings s, double ox, double oy)
    {
        var o = new List<(string, ScenePart)>();
        foreach (var j in rec.Junctions)
        {
            if (!j.Ok) continue;
            var b = j.B;
            var sp = new ScenePart { V = new[] { b.X - ox, b.Y - oy, b.ZTop, b.ZBottom, b.Outer, b.Outer, Math.Atan2(b.Ey, b.Ex), b.Inner }, Name = "집수정 " + j.Label };
            sp.Rows.Add(("종류", j.Kind + " · " + j.Members));
            sp.Rows.Add(("안치수", string.Format(Ic, "{0:0.00} × {0:0.00} m (바깥 {1:0.00})", b.Inner, b.Outer)));
            sp.Rows.Add(("윗면 · 안바닥", string.Format(Ic, "{0:0.000} · {1:0.000}", b.ZTop, b.Floor)));
            sp.Rows.Add(("깊이", string.Format(Ic, "{0:0.00} m", b.Depth)));
            sp.Rows.Add(("덮개", "그레이팅"));
            foreach (var w in j.Warn.Take(2)) sp.Rows.Add(("⚠", w));
            o.Add((j.Owner, sp));
        }
        return o;
    }

    /// <summary>보고서 — 합류 · 교차 · 출구 집수정 절.</summary>
    static void JunctionReport(StringBuilder sb, Record rec, Settings s)
    {
        if (rec.Junctions.Count == 0) return;
        sb.AppendLine();
        sb.AppendLine($"■ 집수정(합류 · 교차 · 출구) {rec.Junctions.Count}개 — 측구가 만나는 곳 · 측구 몸체는 집수정 벽에서 끊김");
        foreach (var j in rec.Junctions)
        {
            var b = j.B;
            sb.AppendLine(string.Format(Ic, "  {0}: X {1:0.000} · Y {2:0.000} · 안 {3:0.00}×{3:0.00} · 바깥 {4:0.00}×{4:0.00} m · 윗면 {5} · 안바닥 {6} · 깊이 {7} m · 그레이팅 덮개 · 콘크리트 {8:0.000} ㎥",
                                       j.Label, b.X, b.Y, b.Inner, b.Outer, F(b.ZTop, "0.000"), F(b.Floor, "0.000"), F(b.ZTop - b.Floor, "0.000"), j.Volume));
            foreach (var m in j.M)
                sb.AppendLine(string.Format(Ic, "    {0} {1} 측점 {2:0.000} m · 몸체 끊음 {3:0.000}~{4:0.000} m{5}{6}", m.D.Id, m.RoleText, m.S, m.Cut.A, m.Cut.B,
                                            m.In ? " · 들어오는 바닥 " + F(m.InV, "0.000") : "", m.Out ? " · 나가는 바닥 " + F(m.OutV, "0.000") : ""));
            foreach (var l in j.Log.Where(l => l.Contains("키움"))) sb.AppendLine("    " + l);
            foreach (var w in j.Warn) sb.AppendLine("    ⚠ " + w);
            foreach (var n in j.Notes) sb.AppendLine("    알림: " + n);
        }
    }

    /// <summary>④ 집수정 꼴 하나 — 닿은 곳 묶음(측구 차례 · 측점 · 역할) → 이어진 측구 · 종류 · 가운데 · 방향 · 크기. 시작끼리만(분수령)이면 null.</summary>
    static Junction? Shape(List<(int I, double S, string Role)> entries, List<Ditch> ds, List<Path2?> gs, Settings s)
    {
        var j = new Junction();
        foreach (var di in entries.GroupBy(e => e.I).OrderBy(x => x.Key))
        {
            string role = di.Any(e => e.Role == "cross") ? "cross" : di.Any(e => e.Role == "mid") ? "mid" : di.Any(e => e.Role == "end") ? "end" : "start";
            double S = di.Where(e => e.Role == role).Average(e => e.S);
            var (px, py) = gs[di.Key]!.At(S);
            j.M.Add(new JMember { D = ds[di.Key], Index = di.Key, S = S, Role = role, Px = px, Py = py });
        }
        if (j.M.Count < 2 || !j.M.Any(m => m.In)) return null;   // 시작끼리만 = 분수령(물이 안 모임) — 집수정 없음
        j.Kind = j.M.Any(m => m.Role == "cross") ? "교차" : j.M.Any(m => m.Role == "mid") || (j.M.Any(m => m.Role == "start") && j.M.Any(m => m.Role == "end")) ? "합류" : "출구";
        j.Owner = j.M.OrderBy(m => m.Index).First().D.Id;
        // 가운데 = 지나가는 측구 위(교차 = 만난 점) · 없으면 첫 유입 끝 / 방향 = 그 측구
        var key = j.M.FirstOrDefault(m => m.Role is "cross" or "mid") ?? j.M.First(m => m.Role == "end");
        double cx = key.Px, cy = key.Py;
        var (ex, ey) = gs[key.Index]!.Dir(key.S);
        if (!j.M.Any(m => m.Role is "cross" or "mid"))
        {
            // 출구 · 끝끼리(지나가는 측구 없음) — 들어오는 측구 모두 벽에 가장 곧게 들어오게: 네 면 대칭이라 각도 4배의 평균 ÷ 4(직각 모서리 = 한 변 방향 그대로 · 예각 = 두 갈래가 같은 만큼 비스듬)
            double sx = 0, sy = 0;
            foreach (var m in j.M) { var (dx, dy) = gs[m.Index]!.Dir(m.S); double a4 = 4 * Math.Atan2(dy, dx); sx += Math.Cos(a4); sy += Math.Sin(a4); }
            if (sx * sx + sy * sy > 1e-12)
            {
                double phi = Math.Atan2(sy, sx) / 4, k0 = Math.Atan2(ey, ex);
                phi += Math.Round((k0 - phi) / (Math.PI / 2)) * (Math.PI / 2);   // 첫 유입 방향에 가까운 쪽(네모는 같음 · 3D 회전값만)
                (ex, ey) = (Math.Cos(phi), Math.Sin(phi));
            }
        }
        double maxB = j.M.Max(m => m.D.Size.B), maxW = j.M.Max(m => m.D.Size.B + 2 * m.D.Size.Tw + (m.D.Size.Tf > 0 ? 2 * m.D.Size.Pf : 0));
        double inner = Math.Max(s.BasinInner, maxB + 2 * s.BasinMargin), outer = inner + 2 * s.BasinWall;
        var why = new List<string>();
        if (maxW > outer + 1e-9) { outer = maxW; why.Add(string.Format(Ic, "측구 바깥폭 {0:0.00} m", maxW)); }
        // 닿은 점이 모두 집수정 바깥 네모 안에(끝이 집수정 밖에 떨어져 몸체와 집수정 사이가 비지 않게)
        double need = j.M.Max(m => Math.Max(Math.Abs((m.Px - cx) * ex + (m.Py - cy) * ey), Math.Abs(-(m.Px - cx) * ey + (m.Py - cy) * ex)));
        if (need * 2 > outer + 1e-9) { outer = need * 2; why.Add(string.Format(Ic, "떨어진 끝 {0:0.00} m", need)); }
        inner = Math.Max(inner, outer - 2 * s.BasinWall);
        j.B = new Basin { S0 = double.NaN, S1 = double.NaN, X = cx, Y = cy, Ex = ex, Ey = ey, Inner = inner, Outer = outer };
        if (why.Count > 0) j.Log.Add("크기 키움 — " + string.Join(" · ", why));
        return j;
    }

    /// <summary>두 집수정 바깥 네모가 겹치나(분리축 — 맞닿기만 하면 안 겹침).</summary>
    static bool Overlap(Basin a, Basin b)
    {
        double dx = b.X - a.X, dy = b.Y - a.Y, ha = a.Outer / 2, hb = b.Outer / 2;
        foreach (var (ax, ay) in new[] { (a.Ex, a.Ey), (-a.Ey, a.Ex), (b.Ex, b.Ey), (-b.Ey, b.Ex) })
        {
            double ra = ha * (Math.Abs(a.Ex * ax + a.Ey * ay) + Math.Abs(-a.Ey * ax + a.Ex * ay));
            double rb = hb * (Math.Abs(b.Ex * ax + b.Ey * ay) + Math.Abs(-b.Ey * ax + b.Ex * ay));
            if (Math.Abs(dx * ax + dy * ay) >= ra + rb - 1e-9) return false;
        }
        return true;
    }

    /// <summary>★[검토 M3] 집수정 합계(단차 집수정 + 합류 · 교차 · 출구) — 개수 · 콘크리트(㎥). 보고서 합계 · 수량산출서가 같은 값을 쓴다. <see cref="Link"/> 뒤에 부른다.</summary>
    public static (int Count, double Volume) BasinTotals(Record rec, Settings s)
    {
        var l = BasinList(rec, s);
        return (l.Count, l.Sum(b => b.Volume));
    }

    /// <summary>★[JACK 1124 «집수정 블록 · 수량»] 집수정 하나 — 꼴 · 종류(단차 · 합류 · 교차 · 출구) · 임자(단차 = 측구 Id · 합류 = J1) · 이어진 측구 «D-001,D-002» · 이름 ·
    /// 깊이(윗면 − 안바닥 · m) · 콘크리트(㎥) · <see cref="DepthKnown"/> 거짓 = 바닥고 · 지표를 못 셈(«깊이 미정» — 블록 · 개소만 · 콘크리트 0 · 3D 없음).</summary>
    public sealed class BasinItem
    {
        public Basin B = new();
        public string Kind = "", Owner = "", Members = "", Label = "";
        public bool Junction, DepthKnown = true;
        public double Volume;
        public double Depth => DepthKnown ? B.ZTop - B.Floor : double.NaN;
    }

    /// <summary>★[JACK 1124] 집수정 목록 — 평면 블록(<c>UDitchStore.Build</c>) · 수량(<see cref="BasinTotals"/> · <see cref="BasinGroups"/>)이 같은 목록을 쓴다(블록 수 = 수량 개소).
    /// 단차 집수정 = 지표 따라감 · 짜임 오류 없는 측구(<see cref="Extras"/>와 같은 조건) · 합류 · 교차 · 출구 = 모두(바닥고를 못 셌으면 «깊이 미정»으로 — 빠뜨리지 않음). <see cref="Link"/> 뒤에 부른다.</summary>
    public static List<BasinItem> BasinList(Record rec, Settings s)
    {
        var o = new List<BasinItem>();
        foreach (var d in rec.Ditches)
        {
            var lay = LayoutOf(d, s);
            if (!lay.Follow || lay.Error.Length > 0) continue;
            for (int k = 0; k < lay.Basins.Count; k++)
            {
                var b = lay.Basins[k];
                o.Add(new BasinItem { B = b, Kind = "단차", Owner = d.Id, Members = d.Id, Label = $"{d.Id} 집수정 {k + 1}", Volume = b.Outer * b.Outer * (b.ZTop - b.ZBottom) - b.Inner * b.Inner * (b.ZTop - b.Floor) });
            }
        }
        foreach (var j in rec.Junctions)
            o.Add(new BasinItem { B = j.B, Kind = j.Kind, Owner = j.Name, Members = string.Join(",", j.M.Select(m => m.D.Id).Distinct()), Label = j.Label, Junction = true, DepthKnown = j.Ok, Volume = j.Volume });
        return o;
    }

    /// <summary>집수정 규격 하나 — 안치수(m · 정사각) × 깊이(0.1 m 올림 · NaN = 깊이 미정) · 바깥치수(가장 큰 것) · 개소 · 콘크리트(㎥ · 실제 꼴 합) · 종류별 개소 글(«합류 2 · 교차 1 · 단차 2»).</summary>
    public sealed class BasinGroup
    {
        public double Inner, Depth = double.NaN, Outer, Volume;
        public int Count;
        public string Kinds = "";
        public bool DepthKnown => double.IsFinite(Depth);
        /// <summary>«안 0.60×0.60 × 깊이 0.9 m» · «안 0.60×0.60 × 깊이 미정».</summary>
        public string Label => string.Format(Ic, "안 {0:0.00}×{0:0.00} × 깊이 ", Inner) + (DepthKnown ? Depth.ToString("0.0", Ic) + " m" : "미정");
        /// <summary>«0.60×0.60×0.9» · «0.60×0.60×미정».</summary>
        public string Short => string.Format(Ic, "{0:0.00}×{0:0.00}×", Inner) + (DepthKnown ? Depth.ToString("0.0", Ic) : "미정");
    }

    /// <summary>깊이 규격(0.1 m 올림 — 깊이 0.857 → 0.9).</summary>
    public static double DepthClass(double depth) => double.IsFinite(depth) ? Math.Ceiling(Math.Round(depth * 10, 6)) / 10 : double.NaN;

    /// <summary>★[JACK 1124 «수량으로 나와야 · 안치수 × 깊이별 개소»] 집수정 규격별(안치수 cm × 깊이 0.1 m 올림 · 깊이 미정은 따로 · 작은 것부터) — 창 · 보고서 · 수량산출서가 같은 값.</summary>
    public static List<BasinGroup> BasinGroups(Record rec, Settings s) => BasinGroups(BasinList(rec, s));

    public static List<BasinGroup> BasinGroups(List<BasinItem> list)
    {
        string[] order = { "합류", "교차", "출구", "단차" };
        return list.GroupBy(b => (Math.Round(b.B.Inner, 2), b.DepthKnown ? DepthClass(b.Depth) : -1.0))
                   .OrderBy(g => g.Key.Item1).ThenBy(g => g.Key.Item2 < 0 ? double.MaxValue : g.Key.Item2)
                   .Select(g => new BasinGroup
                   {
                       Inner = g.Key.Item1, Depth = g.Key.Item2 < 0 ? double.NaN : g.Key.Item2, Outer = g.Max(b => b.B.Outer), Count = g.Count(), Volume = g.Sum(b => b.Volume),
                       Kinds = string.Join(" · ", g.GroupBy(b => b.Kind).OrderBy(x => Array.IndexOf(order, x.Key) is int i && i >= 0 ? i : 9).Select(x => $"{x.Key} {x.Count()}")),
                   }).ToList();
    }

    /// <summary>집수정 규격별 한 줄 — «집수정 6개소(0.60×0.60×0.6 1 · 0.60×0.60×0.9 3 …) · 콘크리트 2.030 ㎥»(<paramref name="vol"/> 거짓 = 콘크리트 뺌 · 깊이 미정이 있으면 «깊이 미정 n» · 없으면 빈 글).</summary>
    public static string BasinSummary(List<BasinItem> list, bool vol = true)
    {
        if (list.Count == 0) return "";
        var gs = BasinGroups(list);
        int un = list.Count(b => !b.DepthKnown);
        return string.Format(Ic, "집수정 {0}개소({1})", list.Count, string.Join(" · ", gs.Select(g => g.Short + " " + g.Count.ToString(Ic))))
             + (vol ? string.Format(Ic, " · 콘크리트 {0:0.000} ㎥", list.Sum(b => b.Volume)) : "")
             + (un > 0 ? $" · ⚠깊이 미정 {un}(콘크리트 빠짐)" : "");
    }

    /// <summary>보고서 — 집수정 수량 절(안치수 × 깊이별 개소 · 종류 · 콘크리트 · 합계).</summary>
    static void BasinQtyReport(StringBuilder sb, Record rec, Settings s)
    {
        var l = BasinList(rec, s);
        if (l.Count == 0) return;
        sb.AppendLine();
        sb.AppendLine(string.Format(Ic, "■ 집수정 수량 — {0}개소 · 콘크리트 {1:0.000} ㎥(평면 블록 «DH_집수정»과 같은 자료 · 깊이 = 윗면 − 안바닥 0.1 m 올림)", l.Count, l.Sum(b => b.Volume)));
        foreach (var g in BasinGroups(l))
            sb.AppendLine(string.Format(Ic, "  {0}: {1}개소({2}) · ", g.Label, g.Count, g.Kinds)
                          + (g.DepthKnown ? string.Format(Ic, "콘크리트 {0:0.000} ㎥", g.Volume) : "콘크리트 미정(바닥고 · 지표 모름 → 측구 확인)"));
    }
}
