namespace DH.Grading.Core;

/// <summary>★★[v111.1 · 계획_v111_1.md 2.5 · JACK 1030 «노리선도 봐줘»] 노리선 = 광선 띠 — 면 · 선과 같은 광선에서.
/// <para>옛 길(칸 D/F 걷기)은 끝이 광선 선과 2~10 cm 어긋나고(현장 사본 171개) · 깎기 맨 윗단(윗선이 데이라잇 너머)에 노리선이 하나도 없었다.
/// 부지(<see cref="SlopeHatchGenerator"/>)처럼 윗선이 땅 밖이면 데이라잇에서 시작한다(0724 «노리선 오류»와 같은 규칙).</para>
/// <para>이웃 광선 a · b 사이 자리 u: 가장자리 · 방향을 보간(방향은 정규화 안 함 — 같은 거리 D 점이 두 광선 D 점을 잇는 현 위 = 면 세모 변 · 계단선 꼭짓점 현과 같은 자리).
/// 데이라잇 쪽 끝은 두 광선 끝점을 잇는 현(면 둘레)과 만나는 자리.</para></summary>
public static partial class RoadGrade
{
    /// <summary>광선 위 거리 d의 설계 높이(점 사이 보간) · 끝 너머는 단면 높이(가장자리 ± 원뿔 — 계획 검토 L4: 끝 값을 끌면 짧은 광선 쪽 노리선 높이가 틀림).</summary>
    static double ZAt(Ray q, double d, Profile prof)
    {
        var p = q.Pts;
        if (p.Count == 0) return q.Ez;
        if (d <= p[0].D) return p[0].Z;
        for (int i = 1; i < p.Count; i++)
            if (d <= p[i].D) { double w = p[i].D - p[i - 1].D; return w > 1e-12 ? p[i - 1].Z + (p[i].Z - p[i - 1].Z) * (d - p[i - 1].D) / w : p[i].Z; }
        return q.Ez + (q.Regime > 0 ? 1 : -1) * prof.FAt(d);
    }

    static double EndD(Ray q) => q.Pts.Count > 0 ? q.Pts[^1].D : 0;

    internal static List<(Point3 A, Point3 B)> SectionTicks(Result r, SectionSet set, double shortSp, double longSp, bool longOnly)
    {
        var ticks = new List<(Point3, Point3)>();
        var corner = new List<(Point3, Point3)>();
        if (set.X.Count == 0) return ticks;
        if (shortSp <= 0) shortSp = 1.0;
        if (longSp <= 0) longSp = 5.0;
        int ratio = Math.Max(1, (int)Math.Round(longSp / shortSp));
        // 둘레 고리 — SectionLines 데이라잇 고리와 같은 차례(왼쪽 → 종점 마구리 → 오른쪽 뒤로 → 시점 마구리) · 닫힘
        var loop = new List<Ray>();
        loop.AddRange(set.X.Select(x => x.L));
        if (set.Caps[1].Count > 1) loop.AddRange(set.Caps[1].Skip(1)); else loop.Add(set.X[^1].R);
        for (int i = set.X.Count - 2; i >= 0; i--) loop.Add(set.X[i].R);
        var c0 = set.Caps[0];
        for (int i = c0.Count - 2; i >= 1; i--) loop.Add(c0[i]);
        bool closed = c0.Count > 1;
        Profile ProfOf(Ray q) => (q.Regime > 0 ? r.CutProfs : r.FillProfs)[Math.Clamp(q.Prof, 0, r.CutProfs.Count - 1)];
        // 면마다 이어 가는 자리(가상 윗선 길이) · 다음 노리선 자리 · 차례 · 부채(가상 윗선 길이 0 — 성토 1단 윗선 = 모서리 점)
        var runs = new Dictionary<(sbyte, int, int), TickRun>();
        int nStrip = closed ? loop.Count : loop.Count - 1;
        for (int si = 0; si < nStrip; si++)
        {
            var a = loop[si]; var b = loop[(si + 1) % loop.Count];
            // ★[v111.3] 경계 짝 띠(직각 끝면 · 5 cm — 단면이 다른 두 광선)는 건너뜀 — 노리선 차례를 끊지 않음(끝면 띠를 지나도 이웃 사면 · 본 단면 면의 간격이 이어짐)
            if (a.Prof != b.Prof && a.Regime == b.Regime && a.Regime != 0 && !a.EdgeWall && !b.EdgeWall && a.EndCap < 0 && b.EndCap < 0 && Sq(a.Ex - b.Ex) + Sq(a.Ey - b.Ey) < 0.15 * 0.15) continue;
            var live = new HashSet<(sbyte, int, int)>();
            if (a.Regime != 0 && a.Regime == b.Regime && a.Prof == b.Prof && !a.EdgeWall && !b.EdgeWall)
            {
                bool cut = a.Regime > 0;
                var prof = ProfOf(a);
                double ea = EndD(a), eb = EndD(b);
                foreach (var f in prof.Faces)
                {
                    if (f.Wall || !(f.B - f.A > 0.3)) continue;   // 0.3 m 이하 면은 노리선 없음(옛 도로 길과 같음)
                    if (ea <= f.A + 1e-6 && eb <= f.A + 1e-6) continue;   // 이 띠에 면이 없음(땅에 묻힘)
                    var key = (a.Regime, f.Src >= 0 ? f.Src : a.Prof, f.K);   // ★[v111.3] 면 출처 단면(끝면 띠 단면이 받아 온 면은 본 · 이웃과 한 차례)
                    live.Add(key);
                    double dc = cut ? f.B : f.A;   // 가상 윗선(절토 = 바깥 B · 성토 = 안 A)
                    double cax = a.Ex + a.Nx * dc, cay = a.Ey + a.Ny * dc, cbx = b.Ex + b.Nx * dc, cby = b.Ey + b.Ny * dc;
                    double seg = Math.Sqrt(Sq(cbx - cax) + Sq(cby - cay));
                    if (!runs.TryGetValue(key, out var st)) runs[key] = st = new TickRun { Start = si };
                    if (seg < 1e-6)
                    {
                        // 부채 — 윗선이 한 점(모서리)이라 1 m마다 못 놓음 → 다 지난 뒤 이등분선 광선에 노리선 하나(부지 «볼록 코너 대각선»과 같은 뜻)
                        (st.Fan ??= new()).Add((a, b));
                        continue;
                    }
                    if (st.Fan != null) { FanTick(st.Fan, f, cut); st.Fan = null; }
                    // ★[설치 전 검토 L2] 고리 이음매 — 처음 띠에서 시작한 면이 한 바퀴 돌아오면 마지막 띠 끝(= 첫 노리선 자리)은 안 놓음(겹친 두 노리선)
                    double lim = closed && si == nStrip - 1 && st.Start == 0 ? st.Pos + seg - 1e-6 : st.Pos + seg + 1e-9;
                    while (st.Next <= lim)
                    {
                        double u = (st.Next - st.Pos) / seg;
                        bool isLong = st.Count % ratio == 0;
                        st.Count++; st.Next += shortSp;
                        if (longOnly && !isLong) continue;
                        Tick(a, b, u, f, prof, cut, isLong, ticks);
                    }
                    st.Pos += seg;
                }
            }
            foreach (var k in runs.Keys.Where(k => !live.Contains(k)).ToList())
            {
                var st = runs[k];
                if (st.Fan != null)
                {
                    var (fa, _) = st.Fan[0];
                    var fp = ProfOf(fa).Faces.FirstOrDefault(q => q.K == k.Item3);
                    FanTick(st.Fan, fp, fa.Regime > 0);
                }
                runs.Remove(k);
            }
        }
        foreach (var kv in runs)
            if (kv.Value.Fan != null)
            {
                var (fa, _) = kv.Value.Fan[0];
                FanTick(kv.Value.Fan, ProfOf(fa).Faces.FirstOrDefault(q => q.K == kv.Key.Item3), fa.Regime > 0);
            }
        return SlopeHatchGenerator.RemoveOverlaps(corner, ticks);

        // 부채 이등분선에 가장 가까운 광선 하나 — 그 광선 점 그대로(면 세모 변)
        void FanTick(List<(Ray A, Ray B)> fan, Face f, bool cut)
        {
            var r0 = fan[0].A; var r1 = fan[^1].B;
            double ang = Math.Acos(Math.Clamp(r0.Nx * r1.Nx + r0.Ny * r1.Ny, -1, 1)) * 180 / Math.PI;
            if (ang < 15) return;
            double bx = r0.Nx + r1.Nx, by = r0.Ny + r1.Ny, bl = Math.Sqrt(bx * bx + by * by);
            if (bl < 1e-9) return;
            bx /= bl; by /= bl;
            Ray best = r0; double bd = -2;
            foreach (var (x, y) in fan) foreach (var q in new[] { x, y }) { double d = q.Nx * bx + q.Ny * by; if (d > bd) { bd = d; best = q; } }
            Tick(best, best, 0, f, ProfOf(best), cut, true, corner);
        }
    }

    /// <summary>면 하나를 고리 따라 이어 가는 자리 — 가상 윗선 길이 · 다음 노리선 자리 · 차례 · 시작 띠 · 부채(가상 윗선 길이 0 — 성토 1단 윗선 = 모서리 점).</summary>
    sealed class TickRun { public double Pos, Next; public int Count, Start; public List<(Ray A, Ray B)>? Fan; }

    /// <summary>띠 (a, b) 자리 u의 노리선 하나 — 절토 = 윗선(또는 데이라잇) → 아랫선 A · 성토 = 윗선 A → 아랫선(또는 데이라잇) · 짧은 선 = 보이는 폭 절반.</summary>
    static void Tick(Ray a, Ray b, double u, Face f, Profile prof, bool cut, bool isLong, List<(Point3, Point3)> outList)
    {
        double ex = a.Ex + (b.Ex - a.Ex) * u, ey = a.Ey + (b.Ey - a.Ey) * u;
        double nx = a.Nx + (b.Nx - a.Nx) * u, ny = a.Ny + (b.Ny - a.Ny) * u;
        double ea = EndD(a), eb = EndD(b);
        // 보이는 구간 [A, hi] — 두 광선 끝이 다 B 너머면 B · 아니면 끝점 현과 만나는 자리
        double hi = f.B, hiZ = double.NaN;
        if (ea < f.B - 1e-6 || eb < f.B - 1e-6)
        {
            var qa = a.Pts[^1]; var qb = b.Pts[^1];
            double wx = qb.X - qa.X, wy = qb.Y - qa.Y, den = nx * wy - ny * wx;
            double t, v = 0; bool onChord = false;
            // ★[계획 검토 M2] 한 광선(부채 이등분선) = 그 끝 · 현 길이 0(엇갈려 자른 두 광선이 한 점에서 만남 — Meet) · 줄과 나란한 현 → 짧은 쪽 끝(긴 쪽으로 늘이면 면 밖)
            // ★[설치 전 검토 L1] 현 위 점일 때만 현 높이 — 아니면 그 자리 단면 높이(ZD · u에 따라)
            if (ReferenceEquals(a, b)) { t = ea; onChord = true; }
            else if (Math.Abs(den) < 1e-12) t = Math.Min(ea, eb);
            else
            {
                double rx = qa.X - ex, ry = qa.Y - ey;
                t = (rx * wy - ry * wx) / den; v = (rx * ny - ry * nx) / den;
                if (v < -1e-6 || v > 1 + 1e-6) t = ea + (eb - ea) * u;   // 현이 줄과 안 만남(드묾) — 끝 거리 보간
                else onChord = true;
            }
            if (t < f.B) { hi = t; if (onChord) hiZ = qa.Z + (qb.Z - qa.Z) * Math.Clamp(v, 0, 1); }
        }
        if (!(hi > f.A + 1e-6)) return;
        double ZD(double d) { double za = ZAt(a, d, prof); return za + (ZAt(b, d, prof) - za) * u; }
        var pA = new Point3(ex + nx * f.A, ey + ny * f.A, ZD(f.A));
        var pH = new Point3(ex + nx * hi, ey + ny * hi, double.IsNaN(hiZ) ? ZD(hi) : hiZ);
        var s = cut ? pH : pA; var e = cut ? pA : pH;
        if (!isLong) e = new Point3((s.X + e.X) / 2, (s.Y + e.Y) / 2, (s.Z + e.Z) / 2);
        if (Math.Sqrt(Sq(e.X - s.X) + Sq(e.Y - s.Y)) < 0.2) return;
        outList.Add((s, e));
    }
}
