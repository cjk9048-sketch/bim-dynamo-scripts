namespace DH.Grading.Core;

/// <summary>★★[JACK 1111 «진입도로 사면→옹벽 · 옹벽→사면 고를 선 — 소단 앞선 · 뒷선이 다 나옴 → 부지 때처럼 · 같은 단이면 쭉 이어서»] [사면→옹벽] · [옹벽→사면] 고를 선.
/// <para>① 단마다 한 줄 — 부지(<see cref="SlopeHatchGenerator.GenerateEdgeLinesTagged"/> 클릭 대상 «각 단의 시작선»)와 같은 뜻: 절토 = 아랫선(Toe) · 성토 = 윗선(Crest).
/// 1단 시작선은 도로 가장자리(성토 = 이미 있는 1단 윗선 · 절토 = <see cref="EdgeStartLines"/>). 소단 앞선(사면 끝 = 절토 윗선 · 성토 아랫선)은 뺀다 — 고른 선의 단 번호(K)는 그대로라 기록 뜻은 같다.</para>
/// <para>② 같은 단 조각 잇기 — 열쇠(절/성 · 쪽 · 단 · 윗/아랫/옹벽선 · 단면 · 선 옹벽 번호)가 같고 끝이 맞닿으면(1 mm) 잇고, 1 m 안 틈은 같은 다리(측점 2 m 안) · 같은 높이 · 같은 방향일 때만 곧게 잇는다.
/// 측점이 2 m 넘게 뛰는 자리(다른 다리 — 헤어핀 안쪽이 한 선이 되어 범위가 헤어핀 전체가 되지 않게 · 고리 엔진 [M5b]와 같은 끊기) · 1 m 넘는 틈(땅 · 옹벽 · 갈림) ·
/// 단면이 바뀌는 자리(옹벽 기록 경계 — 열쇠가 다름)는 그대로 두고 셈(<see cref="PickStats"/>).</para>
/// 그림 선(DH도로-사면선 · 소단선) · 면 · 노리선은 그대로 — 고를 선 사본만.</summary>
public static partial class RoadGrade
{
    /// <summary>고를 선 셈 — 받은 선 · 뺀 앞선 · 더한 가장자리 선 · 이은 곳(맞닿음 · 짧은 틈) · 남은 끊김(같은 단이 둘 이상 줄로 남은 수) · 그중 1 m 안 끝을 안 이은 까닭(다른 다리 · 높이 · 방향) · 낸 선.</summary>
    public sealed class PickStats
    {
        public int In, Dropped, EdgeAdded, Joined, Bridged, Splits, KeptJump, KeptZ, KeptDir, Out;
        public string Text => $"고를 선 {In} → {Out}(앞선 뺌 {Dropped} · 1단 가장자리 선 더함 {EdgeAdded} · 이음 맞닿음 {Joined} · 1 m 안 틈 {Bridged} · 남은 끊김 {Splits}"
                            + (KeptJump + KeptZ + KeptDir > 0 ? $" — 1 m 안 끝을 안 이음: 다른 다리(측점 {PickJump:0} m 넘게 뜀) {KeptJump} · 높이 {KeptZ} · 방향 {KeptDir}" : "") + ")";
    }

    public const double PickTouch = 1e-3, PickBridge = 1.0, PickJump = 2.0;

    /// <summary>고를 선(위 설명). <paramref name="edgeStart"/> = 절토 1단 시작선(도로 가장자리 — <see cref="EdgeStartLines"/>) · [사면→옹벽]일 때만 더함.</summary>
    public static List<TierLine> PickLines(IReadOnlyList<TierLine> lines, bool wallMode, IReadOnlyList<TierLine>? edgeStart, out PickStats st)
    {
        st = new PickStats();
        var src = new List<TierLine>();
        foreach (var l in lines)
        {
            if (l.Side == 0 || l.Cross || l.DisplayOnly || l.Pts.Count < 2 || (l.Role == LineRole.Wall) == wallMode) continue;
            st.In++;
            if (wallMode && l.Role != (l.Cut ? LineRole.Toe : LineRole.Crest)) { st.Dropped++; continue; }
            src.Add(l);
        }
        if (wallMode && edgeStart != null)
            foreach (var l in edgeStart)
                if (l.Side != 0 && l.Pts.Count >= 2) { src.Add(l); st.EdgeAdded++; }
        var o = new List<TierLine>();
        foreach (var g in src.GroupBy(l => (l.Cut, l.Side, l.K, l.Role, l.Prof, l.WallNo)))
            o.AddRange(JoinPieces(g.ToList(), st));
        st.Out = o.Count;
        return o;
    }

    /// <summary>같은 열쇠 조각 잇기 — 끝점 짝을 틈 작은 차례로 · 두 끝이 아직 비었고 다른 줄이면 이음(고리를 닫지 않음).</summary>
    static List<TierLine> JoinPieces(List<TierLine> L, PickStats st)
    {
        int n = L.Count;
        if (n < 2) return L;
        // 끝점 e = 2i(첫 점) · 2i + 1(끝 점)
        (double X, double Y, double Z, double S) End(int e) => (e & 1) == 0 ? L[e >> 1].Pts[0] : L[e >> 1].Pts[^1];
        // 끝에서 바깥으로 향한 방향(끝 점 ← 안쪽 점 · 1 cm 넘게 떨어진 첫 점)
        (double X, double Y) Out(int e)
        {
            var p = L[e >> 1].Pts; var a = End(e);
            for (int k = 1; k < p.Count; k++)
            {
                var q = (e & 1) == 0 ? p[k] : p[p.Count - 1 - k];
                double dx = a.X - q.X, dy = a.Y - q.Y, dl = Math.Sqrt(dx * dx + dy * dy);
                if (dl > 0.01) return (dx / dl, dy / dl);
            }
            return (double.NaN, double.NaN);
        }
        var pairs = new List<(double Gap, int A, int B)>();
        var refused = new List<(int A, int B, int Why)>();   // 1 m 안인데 안 이은 짝 — 0 다른 다리 · 1 높이 · 2 방향(끝이 끝내 비면 셈)
        for (int ea = 0; ea < 2 * n; ea++)
            for (int eb = ea + 1; eb < 2 * n; eb++)
            {
                if ((ea >> 1) == (eb >> 1)) continue;
                var a = End(ea); var b = End(eb);
                double gap = Math.Sqrt(Sq(a.X - b.X) + Sq(a.Y - b.Y));
                if (gap > PickBridge) continue;
                // 같은 다리 — 측점이 2 m 넘게 뛰면 안 이음(둘 다 있을 때)
                if (double.IsFinite(a.S) && double.IsFinite(b.S) && Math.Abs(a.S - b.S) > PickJump) { refused.Add((ea, eb, 0)); continue; }
                if (gap > PickTouch)
                {
                    if (double.IsFinite(a.Z) && double.IsFinite(b.Z) && Math.Abs(a.Z - b.Z) > 0.05 + 0.25 * gap) { refused.Add((ea, eb, 1)); continue; }
                    if (gap > 0.1)
                    {
                        // 곧게 잇는 줄이 두 조각의 끝 방향을 이어 감(60° 안) — 나란히 붙은 조각 · 되돌아가는 조각은 안 이음
                        var ua = Out(ea); var ub = Out(eb);
                        double gx = (b.X - a.X) / gap, gy = (b.Y - a.Y) / gap;
                        if (!double.IsFinite(ua.X) || !double.IsFinite(ub.X) || ua.X * gx + ua.Y * gy < 0.5 || -(ub.X * gx + ub.Y * gy) < 0.5) { refused.Add((ea, eb, 2)); continue; }
                    }
                }
                pairs.Add((gap, ea, eb));
            }
        var link = Enumerable.Repeat(-1, 2 * n).ToArray();
        var root = Enumerable.Range(0, n).ToArray();
        int Find(int i) { while (root[i] != i) i = root[i] = root[root[i]]; return i; }
        foreach (var (gap, ea, eb) in pairs.OrderBy(p => p.Gap))
        {
            if (link[ea] >= 0 || link[eb] >= 0) continue;
            int ra = Find(ea >> 1), rb = Find(eb >> 1);
            if (ra == rb) continue;
            link[ea] = eb; link[eb] = ea; root[ra] = rb;
            if (gap <= PickTouch) st.Joined++; else st.Bridged++;
        }
        // 줄 모으기 — 빈 끝(이음 없는 끝)에서 출발해 이음을 따라감
        var o = new List<TierLine>();
        var done = new bool[n];
        void Walk(int e0)
        {
            var first = L[e0 >> 1];
            var t = new TierLine { Cut = first.Cut, Side = first.Side, K = first.K, Role = first.Role, Prof = first.Prof, WallNo = first.WallNo };
            int e = e0;
            while (true)
            {
                int i = e >> 1; done[i] = true;
                var p = L[i].Pts;
                IEnumerable<(double X, double Y, double Z, double S)> seq = (e & 1) == 0 ? p : Enumerable.Reverse(p);
                foreach (var q in seq)
                {
                    if (t.Pts.Count > 0 && Sq(t.Pts[^1].X - q.X) + Sq(t.Pts[^1].Y - q.Y) <= PickTouch * PickTouch) continue;   // 맞닿은 이음 점 하나만
                    t.Pts.Add(q);
                }
                int far = e ^ 1, nx = link[far];
                if (nx < 0 || done[nx >> 1]) break;
                e = nx;
            }
            if (t.Pts.Count >= 2) o.Add(t);
        }
        for (int e = 0; e < 2 * n; e++) if (!done[e >> 1] && link[e] < 0) Walk(e);
        for (int i = 0; i < n; i++) if (!done[i]) Walk(2 * i);   // (고리는 위에서 막혔으나 남은 것 대비)
        // 남은 끊김 — 같은 단이 둘 이상 줄로 남음(1 m 넘는 틈 = 땅 · 옹벽 · 갈림 · 또는 아래 까닭) · 1 m 안 끝을 끝내 안 이은 까닭(두 끝이 다 빈 짝만 · 짝마다 한 번)
        if (o.Count > 1) st.Splits += o.Count - 1;
        foreach (var (ea, eb, why) in refused)
        {
            if (link[ea] >= 0 || link[eb] >= 0) continue;
            if (why == 0) st.KeptJump++; else if (why == 1) st.KeptZ++; else st.KeptDir++;
        }
        return o;
    }

    /// <summary>★[JACK 1111] 절토 1단 시작선 — 도로 가장자리(가장자리 표본 <see cref="Result.Slopes"/>) 중 바깥이 절토이고 가장자리 옹벽이 아닌 줄(K 0 · 아랫선).
    /// 엔진은 거리 0 꺾임을 선으로 안 낸다(성토 1단 윗선만 표본에서) — 고를 선에서만 씀(그림 · 면은 그대로). 측점 1.5 m 넘게 뛰거나 단면이 바뀌면 끊음(옛 가장자리 줄과 같은 끊기).</summary>
    public static List<TierLine> EdgeStartLines(Result r)
    {
        var o = new List<TierLine>();
        foreach (int side in new[] { -1, 1 })
        {
            TierLine? cur = null; double lastS = double.NaN;
            foreach (var q in r.Slopes.Where(q => q.Side == side).OrderBy(q => q.S))
            {
                bool rec = q.Rec >= 0 && q.Rec < r.Recs.Count;
                bool wall0 = q.Wall || (rec && r.Recs[q.Rec].Tier == 0);
                int prof = rec && q.Rec < r.RecProf.Length ? r.RecProf[q.Rec] : 0;
                if (!q.Cut || q.Fill || wall0 || !double.IsFinite(q.X) || !double.IsFinite(q.Y)) { cur = null; continue; }
                if (cur == null || cur.Prof != prof || !(Math.Abs(q.S - lastS) <= 1.5))
                {
                    cur = new TierLine { Cut = true, Side = side, K = 0, Role = LineRole.Toe, Prof = prof };
                    o.Add(cur);
                }
                cur.Pts.Add((q.X, q.Y, q.EdgeZ, q.S));
                lastS = q.S;
            }
        }
        o.RemoveAll(l => l.Pts.Count < 2);
        return o;
    }
}
