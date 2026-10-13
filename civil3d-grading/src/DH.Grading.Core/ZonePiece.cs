using System;
using System.Collections.Generic;

namespace DH.Grading.Core;

/// <summary>★★★[v103.1.1 · JACK 1006 «이미 사면이나 옹벽인 구간을 다시 똑같은 변환옵션으로 클릭하면 "이미 OO입니다" 뜨고 아예 안 되게 ·
/// 역변환할 때는 구간변환은 없게 · 순수 처음 최초 계획지표면에서만 구간변환»] 클릭선 조각이 <b>지금 무엇인지</b> — 끊기 · 가르기 · 정하기.
/// <para>종전 클릭선은 수직↔사면 경계(<c>SlopeHatchGenerator.SplitByZone</c>)와 목록 옹벽 자리(<see cref="WallPlace.SplitAtWalls"/>)에서만 갈렸다 —
/// 구배 · 소단만 바꾼 구간의 경계에서는 안 갈려, 조각 하나에 «바꾼 자리»와 «처음 그대로»가 섞였다(코드 검토 1006 높음).</para></summary>
public static class ZonePiece
{
    /// <summary>이 점 · 이 단의 상태 — 구배 · 소단(<see cref="SlopeZone.ResolveAt"/>과 같은 값) · 그 값을 정한 규칙의 시작단(없으면 −1) ·
    /// 그 규칙이 앞 규칙(없으면 기본값)과 다른 값을 내는가.</summary>
    public readonly record struct State(double Slope, double BenchW, int RuleFrom, bool RuleActs)
    {
        public bool Same(State o)
            => Math.Abs(Slope - o.Slope) < 1e-9 && Math.Abs(BenchW - o.BenchW) < 1e-9 && RuleFrom == o.RuleFrom && RuleActs == o.RuleActs;
    }

    /// <summary><see cref="SlopeZone.ResolveAt"/>과 <b>같은 겹치기</b>(만들어진 순서 · 나중 구간이 자기 시작단부터 대체) + 값을 정한 규칙.</summary>
    public static State StateAt(IReadOnlyList<SlopeZone>? zones, double x, double y, int bench,
        double baseSlope, double baseW, IReadOnlyList<Point3> planB, double[] planCum)
    {
        if (zones == null || zones.Count == 0) return new State(baseSlope, baseW, -1, false);
        List<(int F, double S, double W)>? acc = null;
        foreach (var z in zones)
        {
            if (z == null || z.Rules.Count == 0) continue;
            if (!z.ContainsAt(x, y, planB, planCum)) continue;
            acc ??= new List<(int, double, double)>();
            int zf = z.FirstBench;
            acc.RemoveAll(r => r.F >= zf);
            foreach (var r in z.Rules) acc.Add((r.FromBench, r.Slope, r.BenchW));
        }
        if (acc == null) return new State(baseSlope, baseW, -1, false);
        acc.Sort((a, b) => a.F.CompareTo(b.F));
        double s = baseSlope, w = baseW; int from = -1; bool acts = false;
        foreach (var r in acc)
        {
            if (bench < r.F) break;
            double ps = s, pw = w;
            s = r.S;
            if (r.W >= 0) w = r.W;
            from = r.F;
            acts = Math.Abs(s - ps) > 1e-9 || Math.Abs(w - pw) > 1e-9;
        }
        return new State(s, w, from, acts);
    }

    /// <summary>조각의 종류 — 처음 그대로(아랫단 변환에 딸려 바뀐 윗단 포함 · JACK 1006) / 옹벽(수직) / 이 단을 직접 사면 변환한 자리.</summary>
    public enum Kind { Untouched, Wall, SlopeDirect }

    public static Kind Classify(State st, int bench, double baseSlope, double baseW, double gateSlope)
    {
        bool changed = Math.Abs(st.Slope - baseSlope) > 1e-9 || Math.Abs(st.BenchW - baseW) > 1e-9;
        bool vertical = st.Slope <= gateSlope + 1e-9;
        if (vertical) return changed ? Kind.Wall : Kind.Untouched;      // 기본 구배가 수직인 도면의 손 안 댄 자리는 처음 그대로
        return st.RuleFrom == bench && st.RuleActs ? Kind.SlopeDirect : Kind.Untouched;
    }

    /// <summary>고른 조각에 할 일 — 묻기(전체구간/구간지정) / 조각 전체만(이미 바꾼 자리) / 막기(옹벽을 옹벽 변환으로 다시).</summary>
    public enum Act { Ask, WholeOnly, Block }

    /// <summary>★[v103.1.2 · JACK 1006 «구배 수정하려면 무조건 막으면 안 되겠다»] 직접 사면 변환한 자리를 사면 변환으로 다시 고르면 막지 않고
    /// <b>조각 전체</b>로 값을 바꾼다(값이 지금과 같을 때만 부르는 쪽이 «이미 1:N입니다»로 멈춘다). 옹벽 → 옹벽은 그대로 막는다.</summary>
    /// <param name="wallMode">옹벽 변환이면 참 · 사면 변환이면 거짓</param>
    /// <param name="wallPiece">목록 옹벽 자리(주황 조각)인가</param>
    public static Act Decide(bool wallMode, bool wallPiece, Kind kind)
    {
        bool wallNow = wallPiece || kind == Kind.Wall;
        bool slopeConv = !wallNow && kind == Kind.SlopeDirect;
        if (wallMode && wallNow) return Act.Block;
        return wallNow || slopeConv ? Act.WholeOnly : Act.Ask;
    }

    /// <summary>★★★[v103.1.3 · JACK 1007 «되돌린 자리에서 여전히 주황 조각이었던 부분으로 잘려 있는데 이게 정상이야?»] 클릭선을 <b>끊을지</b> 가르는 잣대 —
    /// 구배 · 소단이 같고 종류(처음 그대로 / 옹벽 / 직접 사면 변환)도 같으면 <b>같은 조각</b>이다.
    /// <para>v103.1.1은 «값을 정한 규칙»까지 견줘(<see cref="State.Same"/>), 기본값으로 되돌린 구간(값은 주변과 같고 규칙 기록만 남은 자리)의 경계에서도 끊었다 —
    /// 현장 1007: 성토 클릭선 5줄이 14조각(구배는 전부 1:1.5). 규칙이 어디서 왔는지는 <b>고르는 데 아무 차이도 안 만든다</b>(할 일은 종류가 정한다).</para></summary>
    public static bool SameForPick(State a, State b, int bench, double baseSlope, double baseW, double gateSlope)
        => Math.Abs(a.Slope - b.Slope) < 1e-9 && Math.Abs(a.BenchW - b.BenchW) < 1e-9
           && Classify(a, bench, baseSlope, baseW, gateSlope) == Classify(b, bench, baseSlope, baseW, gateSlope);

    /// <summary>선을 상태가 바뀌는 자리에서 끊는다 — 꼭짓점이 아니라 <b>바뀌는 그 자리</b>(변 위를 <paramref name="step"/> 간격으로 재고 <paramref name="tol"/>까지 좁힘)에
    /// 점을 넣어 양쪽 조각이 나눠 갖는다. 닫힌 선은 이음매에서 갈린 첫 · 끝 조각(같은 상태)을 다시 잇는다.
    /// <paramref name="dropped"/> = 길이 1mm가 안 돼 버린 조각 수(구간 최소 길이 때문에 정상이면 0).
    /// <paramref name="same"/> = 두 상태를 한 조각으로 볼 잣대(없으면 <see cref="State.Same"/> — 규칙까지 같아야 한다). 조각에 들려 보내는 상태는 그 조각 <b>첫 점</b>의 것이다.</summary>
    public static List<(List<Point3> Pts, State St)> Split(IReadOnlyList<Point3> line, Func<double, double, State> at,
        out int dropped, double step = 0.25, double tol = 1e-6, Func<State, State, bool>? same = null)
    {
        same ??= static (x, y) => x.Same(y);
        var res = new List<(List<Point3>, State)>();
        dropped = 0;
        if (line == null || line.Count == 0) return res;
        var p0 = line[0];
        var sPrev = at(p0.X, p0.Y);
        if (line.Count < 2) { res.Add((new List<Point3> { p0 }, sPrev)); return res; }
        var cur = new List<Point3> { p0 };
        var pPrev = p0;
        static double D(Point3 a, Point3 b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
        static Point3 Lerp(Point3 a, Point3 b, double u) => new(a.X + (b.X - a.X) * u, a.Y + (b.Y - a.Y) * u, a.Z + (b.Z - a.Z) * u);
        void Step(Point3 q, bool vertex)
        {
            var sq = at(q.X, q.Y);
            int guard = 0;
            while (!same(sPrev, sq) && guard++ < 64)
            {
                var lo = pPrev; var hi = q;
                while (D(lo, hi) > tol)
                {
                    var mid = Lerp(lo, hi, 0.5);
                    if (same(at(mid.X, mid.Y), sPrev)) lo = mid; else hi = mid;
                }
                var sc = at(hi.X, hi.Y);
                if (same(sc, sPrev)) { sc = sq; hi = q; }     // 좁히다 놓쳤다(잰 값이 흔들림) — 표본 자리에서 끊는다
                if (D(cur[^1], hi) > 1e-12) cur.Add(hi);
                res.Add((cur, sPrev));
                cur = new List<Point3> { hi };
                pPrev = hi; sPrev = sc;
            }
            if (vertex && D(cur[^1], q) > 1e-12) cur.Add(q);
            // ★[v103.1.3] 조각의 상태는 그 조각 <b>첫 점</b>의 것으로 둔다 — 한 조각으로 보는 잣대가 느슨하면(SameForPick) 조각 안에서 규칙만 다른 상태가 이어지는데,
            //   견주는 기준(sPrev)을 그때마다 갈아 끼우면 조각이 끝날 때 들려 보내는 상태가 «마지막 점»의 것이 된다. 값 · 종류는 어느 쪽이든 같다(잣대가 그것을 견준다)
            pPrev = q;
            if (!same(sPrev, sq)) sPrev = sq;                 // 한 걸음에서 64번을 넘겨 못 다 끊었다(있을 수 없는 일) — 기준을 지금 점으로 돌려 다음 걸음의 좁히기 전제(lo는 기준과 같다)를 지킨다
        }
        for (int i = 1; i < line.Count; i++)
        {
            var a = line[i - 1]; var b = line[i];
            double len = D(a, b);
            int n = step > 1e-9 ? (int)Math.Ceiling(len / step) : 1;
            for (int j = 1; j < n; j++) Step(Lerp(a, b, (double)j / n), false);
            Step(b, true);
        }
        res.Add((cur, sPrev));

        static double Len(List<Point3> p) { double s = 0; for (int i = 1; i < p.Count; i++) s += D(p[i - 1], p[i]); return s; }
        bool closed = line.Count >= 3 && Math.Abs(line[0].X - line[^1].X) < 0.05 && Math.Abs(line[0].Y - line[^1].Y) < 0.05;
        if (closed && res.Count >= 2 && same(res[0].Item2, res[^1].Item2))
        {
            var merged = new List<Point3>(res[^1].Item1);
            for (int i = 1; i < res[0].Item1.Count; i++) merged.Add(res[0].Item1[i]);
            res[0] = (merged, res[0].Item2);
            res.RemoveAt(res.Count - 1);
        }
        if (res.Count > 1)
            for (int i = res.Count - 1; i >= 0; i--)
                if (res[i].Item1.Count < 2 || Len(res[i].Item1) < 1e-3) { res.RemoveAt(i); dropped++; }
        return res;
    }

    /// <summary>끝점을 나눠 가진 조각들을 다시 한 줄로 잇는다(앞 조각의 끝 = 뒤 조각의 처음 · 같은 점) — <see cref="Split"/>에 먹이기 전에 쓴다.
    /// <para><c>SlopeHatchGenerator.SplitByZone</c>은 수직↔사면 경계를 <b>꼭짓점</b>에서 끊어, 그대로 <see cref="Split"/>에 먹이면 진짜 경계와 그 꼭짓점 사이가
    /// 1m 안 되는 토막으로 따로 남는다(재검토 1006 중간 1). 이어서 먹이면 경계 한 곳에서만 끊긴다. 조각 방향은 그대로(뒤집어 잇지 않는다).</para></summary>
    public static List<List<Point3>> Join(IReadOnlyList<List<Point3>> lines, double tol = 1e-9)
    {
        var res = new List<List<Point3>>();
        foreach (var l in lines) if (l != null && l.Count >= 2) res.Add(new List<Point3>(l));
        static bool Near(Point3 a, Point3 b, double t) => Math.Abs(a.X - b.X) <= t && Math.Abs(a.Y - b.Y) <= t;
        bool again = true;
        while (again)
        {
            again = false;
            for (int i = 0; i < res.Count && !again; i++)
                for (int j = 0; j < res.Count; j++)
                {
                    if (i == j || !Near(res[i][^1], res[j][0], tol)) continue;
                    for (int k = 1; k < res[j].Count; k++) res[i].Add(res[j][k]);
                    res.RemoveAt(j);
                    again = true;
                    break;
                }
        }
        return res;
    }

    /// <summary>선의 길이 한가운데 점(꼭짓점 번호가 아니라 호길이) — 2점짜리 조각도 가운데를 준다.</summary>
    public static Point3 MidPoint(IReadOnlyList<Point3> pts)
    {
        if (pts.Count == 0) return new Point3(0, 0, 0);
        double tot = 0;
        for (int i = 1; i < pts.Count; i++) tot += Math.Sqrt((pts[i].X - pts[i - 1].X) * (pts[i].X - pts[i - 1].X) + (pts[i].Y - pts[i - 1].Y) * (pts[i].Y - pts[i - 1].Y));
        if (tot < 1e-12) return pts[0];
        double half = tot * 0.5, run = 0;
        for (int i = 1; i < pts.Count; i++)
        {
            double l = Math.Sqrt((pts[i].X - pts[i - 1].X) * (pts[i].X - pts[i - 1].X) + (pts[i].Y - pts[i - 1].Y) * (pts[i].Y - pts[i - 1].Y));
            if (run + l >= half && l > 1e-12)
            {
                double u = (half - run) / l;
                return new Point3(pts[i - 1].X + (pts[i].X - pts[i - 1].X) * u, pts[i - 1].Y + (pts[i].Y - pts[i - 1].Y) * u, pts[i - 1].Z + (pts[i].Z - pts[i - 1].Z) * u);
            }
            run += l;
        }
        return pts[^1];
    }
}
