using System.Globalization;

namespace DH.Grading.Core;

/// <summary>★★[v117.x A · 로드맵 D2 ③ · JACK 1111] 다단 <b>구간 나눔</b> — 한 구간이 안 맞을 때(바깥 단이 선 가운데에서 끊김) 구간안을 셈(순수 셈 · 기하 안 바꿈).
/// <para>1차 수직 가로대(L 위 원지반 g · 벽 높이 h — 가로대 사이 1차라 정확)로 구간마다 첫 소단 Z1(설계자 모드 B의 자동값 = 그 구간 «바깥 벽 ≤ H»를 지키는 가장 낮은 소단 —
/// 한 구간 자동 Z1과 같은 셈)을 정하고, 바깥 단이 «안쪽에서» 높이 0이 되는 곳(g ≤ Z1)이 없도록 가장 적은 구간으로 나눈다(앞에서부터 가장 길게 — 구간 수 최소).
/// 경계는 높은 쪽 구간의 바깥 단 높이가 0이 되는 자리(g = 높은 Z1 · 가로대 사이 1차라 정확)에 둔다 — 높은 단은 경계에서 높이 0으로 끝나고(단별 종료) 낮은 쪽은 1:s 옆벽(옆 계단)으로 이어진다.
/// 최소 길이보다 짧은 구간은 앞(없으면 뒤) 구간에 합친다(남는 끊김은 세어 알린다).</para></summary>
public static partial class WallByLine
{
    /// <summary>★[v117.x A] 구간 최소 길이(m) 기본값.</summary>
    public const double TierSecMinLen = 10;

    /// <summary>가로대 — 호 길이 S · 원지반 G(절토 꼴 · L 위) · 바깥 벽 높이 H(1차).</summary>
    internal readonly record struct TRung(double S, double G, double H);

    /// <summary>1차 수직(옛 사면 그대로)의 바깥 벽 가로대 → (S, G 절토 꼴, h) · S 오름차순(같은 S = 가름점은 높은 것 하나).</summary>
    static List<TRung> TierRungs(Built b1, bool up, double sg, PL pl, double ox, double oy)
    {
        var o = new List<TRung>();
        foreach (var f in b1.Faces.Where(f => f.Up == up && f.Kind == 0))
            for (int i = 0; i < f.Top.Count && i < f.Foot.Count; i++)
            {
                var lp = up ? f.Top[i] : f.Foot[i];
                o.Add(new TRung(pl.Near(lp.X - ox, lp.Y - oy).S, sg * lp.Z, Math.Abs(f.Top[i].Z - f.Foot[i].Z)));
            }
        o.Sort((a, b) => a.S.CompareTo(b.S));
        var r = new List<TRung>();
        foreach (var q in o) { if (r.Count > 0 && Math.Abs(r[^1].S - q.S) <= 1e-9) { if (q.H > r[^1].H) r[^1] = q; continue; } r.Add(q); }
        return r;
    }

    /// <summary>호 길이 s의 (G, h) — 이웃 가로대 사이 1차(끝 밖은 끝 값).</summary>
    static TRung RungAt(List<TRung> r, double s)
    {
        if (r.Count == 0) return new TRung(s, double.NaN, 0);
        if (s <= r[0].S) return r[0] with { S = s };
        if (s >= r[^1].S) return r[^1] with { S = s };
        int lo = 0, hi = r.Count - 1;
        while (hi - lo > 1) { int m = (lo + hi) / 2; if (r[m].S <= s) lo = m; else hi = m; }
        double t = r[hi].S - r[lo].S > 0 ? (s - r[lo].S) / (r[hi].S - r[lo].S) : 0;
        return new TRung(s, r[lo].G + (r[hi].G - r[lo].G) * t, r[lo].H + (r[hi].H - r[lo].H) * t);
    }

    /// <summary>[s0, s1] 안 점(양 끝 사이값 + 안 가로대).</summary>
    static List<TRung> RungsIn(List<TRung> r, double s0, double s1)
    {
        var o = new List<TRung> { RungAt(r, s0) };
        foreach (var q in r) if (q.S > s0 + 1e-9 && q.S < s1 - 1e-9) o.Add(q);
        o.Add(RungAt(r, s1));
        return o;
    }

    /// <summary>첫 소단 자동(절토 꼴) — 점 중 h &gt; H인 것의 G − H 최대 + 이웃 사이 h = H 건넘의 G* − H(한 구간 자동과 같은 셈). 없으면 −∞(단 필요 없음).</summary>
    static double Z1Of(List<TRung> pts, double H)
    {
        double z = double.NegativeInfinity;
        for (int i = 0; i < pts.Count; i++)
        {
            if (pts[i].H > H) z = Math.Max(z, pts[i].G - H);
            if (i > 0 && (pts[i - 1].H - H) * (pts[i].H - H) < 0)
            {
                double t = (H - pts[i - 1].H) / (pts[i].H - pts[i - 1].H);
                z = Math.Max(z, pts[i - 1].G + t * (pts[i].G - pts[i - 1].G) - H);
            }
        }
        return z;
    }

    /// <summary>바깥 단 높이 0인 점(g ≤ Z1 + 0.1 mm · 또는 h ≤ 0.1 mm)이 이어진 몫 가운데 «안쪽» 것 수 — head/tail 참이면 앞/뒤 끝에 닿은 몫은 꼬리(셈 안 함).</summary>
    static int TierZeroRuns(List<TRung> pts, double z1, bool head, bool tail)
    {
        bool Zero(TRung q) => q.H <= 1e-4 || (!double.IsNegativeInfinity(z1) && q.G - z1 <= 1e-4);
        int n = 0, m = pts.Count;
        for (int i = 0; i < m;)
        {
            if (!Zero(pts[i])) { i++; continue; }
            int j = i; while (j + 1 < m && Zero(pts[j + 1])) j++;
            if (!((i == 0 && head) || (j == m - 1 && tail))) n++;
            i = j + 1;
        }
        return n;
    }

    /// <summary>★[v117.x A] 구간안(절토 꼴 Z1 · NoTier = 단 없음) — 가로대 r(S 오름차순) · 단높이 H · 선 길이 len · 최소 길이 minLen. 남는 끊김(어림)을 돌려준다.
    /// 한 구간으로 맞으면 1개.</summary>
    internal static List<TierSec> ProposeTierSecs(List<TRung> r, double H, double len, double minLen, out int breaks)
    {
        breaks = 0;
        int m = r.Count;
        if (m < 2) return new List<TierSec> { new TierSec { S0 = 0, S1 = len, Z1 = Z1Of(r, H) } };
        // 맞음 = 바깥 단 높이 0 몫이 선 끝(꼬리)에만 — 구간 안쪽 · 안쪽 경계에 닿은 0 몫은 끊김(앞 구간을 늘릴수록 Z1이 오르기만 하므로 한 번 안 맞으면 계속 안 맞음)
        bool Feas(int a, int b) { var pts = r.GetRange(a, b - a + 1); return TierZeroRuns(pts, Z1Of(pts, H), a == 0, b == m - 1) == 0; }
        var idx = new List<(int A, int B)>();
        for (int a = 0; a < m;)
        {
            if (Feas(a, m - 1)) { idx.Add((a, m - 1)); break; }
            int best = -1;
            for (int b = a; b < m - 1; b++) { if (Feas(a, b)) best = b; else if (best >= 0) break; }
            if (best < 0) best = a;
            idx.Add((a, best)); a = best + 1;
        }
        // 경계 — 높은 쪽 구간의 바깥 단이 0이 되는 자리(g = 높은 Z1 · 가로대 사이 1차)
        var secs = new List<TierSec>();
        double prevS = 0;
        for (int k = 0; k < idx.Count; k++)
        {
            double s1 = len;
            if (k + 1 < idx.Count)
            {
                var (a0, b0) = idx[k]; var (a1, b1) = idx[k + 1];
                double zA = Z1Of(r.GetRange(a0, b0 - a0 + 1), H), zB = Z1Of(r.GetRange(a1, b1 - a1 + 1), H);
                double zh = Math.Max(zA, zB);
                var p = r[b0]; var q = r[a1];
                double t = !double.IsNegativeInfinity(zh) && (p.G - zh) * (q.G - zh) < 0 ? (p.G - zh) / (p.G - q.G) : 0.5;
                s1 = p.S + t * (q.S - p.S);
            }
            secs.Add(new TierSec { S0 = prevS, S1 = s1 });
            prevS = s1;
        }
        void Resolve() { foreach (var s in secs) { s.Z1 = Z1Of(RungsIn(r, s.S0, s.S1), H); s.NoTier = double.IsNegativeInfinity(s.Z1); } }
        Resolve();
        // 짧은 구간 합치기(앞 · 없으면 뒤)
        while (secs.Count > 1)
        {
            int k = secs.FindIndex(s => s.S1 - s.S0 < minLen - 1e-9);
            if (k < 0) break;
            int j = k > 0 ? k - 1 : k + 1;
            int lo = Math.Min(j, k);
            secs[lo].S1 = secs[lo + 1].S1; secs.RemoveAt(lo + 1);
            Resolve();
        }
        // 남는 끊김(어림) — 안쪽 경계의 사이값 점은 뺌(높은 쪽 구간은 경계에서 정의상 높이 0)
        for (int k = 0; k < secs.Count; k++)
        {
            var pk = RungsIn(r, secs[k].S0, secs[k].S1);
            if (k + 1 < secs.Count) pk.RemoveAt(pk.Count - 1);
            if (k > 0) pk.RemoveAt(0);
            if (pk.Count > 0) breaks += TierZeroRuns(pk, secs[k].Z1, k == 0, k == secs.Count - 1);
        }
        return secs;
    }

    /// <summary>★[v117.x A] 받은 구간(실제 z · NaN = 자동)을 절토 꼴로 — 이어 붙이고 [0, len]으로 · 자동은 구간 가로대로. 못 쓰면 까닭.</summary>
    static List<TierSec>? ResolveTierSecs(List<TierSec> given, List<TRung> r, double H, double sg, double len, out string why)
    {
        why = "";
        var g = given.Select(q => q.Copy()).OrderBy(q => q.S0).ToList();
        // ★[v117.9 · 설치 전 검토 LOW] 경계를 고쳐 선이 짧아지면 선 밖이 된 구간 경계는 버림(앞 구간에 합침) — ⚠ 한 줄(옹벽이 보류되지 않게)
        int drop = 0;
        for (int k = g.Count - 1; k >= 1; k--) if (!(g[k].S0 < len - 1e-6)) { g.RemoveAt(k); drop++; }
        if (g.Count < 2) { why = string.Format(CultureInfo.InvariantCulture, "구간 경계가 모두 선(0~{0:F2}) 밖입니다 — 한 구간(O) 또는 구간을 다시 나누세요", len); return null; }
        g[0].S0 = 0; g[^1].S1 = len;
        for (int k = 0; k + 1 < g.Count; k++)
        {
            double b = g[k + 1].S0;
            if (!(b > g[k].S0 + 1e-6) || !(b < len - 1e-6)) { why = string.Format(CultureInfo.InvariantCulture, "구간 경계 STA {0:F2}가 선(0~{1:F2}) 안 차례대로가 아닙니다", b, len); return null; }
            g[k].S1 = b;
        }
        if (g[^1].S1 <= g[^1].S0 + 1e-6) { why = "마지막 구간 길이가 0입니다"; return null; }
        foreach (var s in g)
        {
            if (s.NoTier) { s.Z1 = double.NegativeInfinity; continue; }
            if (double.IsNaN(s.Z1)) { s.Z1 = Z1Of(RungsIn(r, s.S0, s.S1), H); s.NoTier = double.IsNegativeInfinity(s.Z1); s.Hand = false; }
            else { s.Z1 = sg * s.Z1; s.Hand = true; }
        }
        if (drop > 0) why = string.Format(CultureInfo.InvariantCulture, "구간 경계 {0}개가 선(0~{1:F2}) 밖이라 버림(앞 구간에 합침)", drop, len);   // 성공 때 why = ⚠ 한 줄
        return g;
    }
}
