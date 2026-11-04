using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DH.Grading.Core;

/// <summary>★★[v113.1 · 계획_종단방향_축척.md 1 · JACK 1104 «종단 방향 변경 — 시점↔종점 바꾸기»] 측점에 묶인 기록을 반대 방향 측점으로.
/// <para>옛 노선 측점 범위 [a0, b0] · 새 시작 측점 a1 → 새 측점 = a1 + (b0 − s). 구간 [s0, s1] → [a1 + b0 − s1, a1 + b0 − s0] · 쪽 −1 ↔ +1 · 목록 차례 그대로(나중 기록이 이김).</para>
/// <para>«두 번 누르면 처음»: 부동소수 c − (c − s)는 s와 끝자리가 다를 수 있다 → 되돌림 기록(<see cref="Snapshot"/>) — 바꾼 뒤 값의 지문이 지금 값과 같으면(사이에 고친 것 없음) 바꾸기 전 값을 그대로 되씀.</para></summary>
public static class ProfileFlip
{
    /// <summary>새 측점.</summary>
    public static double Map(double s, double b0, double a1) => a1 + (b0 - s);

    /// <summary>구간 기록 하나 — [s0, s1, 쪽, …] → [Map(s1), Map(s0), −쪽, …]. 짧거나 측점이 숫자가 아니면 그대로(사본).</summary>
    public static double[] Span(double[] w, double b0, double a1)
    {
        var c = (double[])w.Clone();
        if (w.Length < 3 || !double.IsFinite(w[0]) || !double.IsFinite(w[1])) return c;
        c[0] = Map(w[1], b0, a1);
        c[1] = Map(w[0], b0, a1);
        c[2] = w[2] < 0 ? 1 : -1;   // 쪽 판정(&lt; 0 = 좌)과 같은 법 — 0(우)은 좌(−1)로
        return c;
    }

    /// <summary>방향에 묶인 값 한 벌(도로 설계 입력 · 측점 · PVI · 부지 노선 꼭짓점).</summary>
    public sealed class State
    {
        public List<double[]> TierWalls = new(), Ditches = new();
        public List<Mark> Marks = new();
        public List<double> Drops = new();
        public List<Pvi> Pvis = new();
        /// <summary>좌 · 우 값 [HalfL, HalfR, ShoulderL, ShoulderR] — 부지는 빈 배열.</summary>
        public double[] Lr = Array.Empty<double>();
        /// <summary>부지 노선 꼭짓점 [x0, y0, x1, y1, …] — 도로는 빈 배열.</summary>
        public double[] Route = Array.Empty<double>();
        /// <summary>★[설치 전 검토 H1] 이 값들이 걸린 노선 측점 범위 [A0, B0](모르면 NaN) — 지문에 넣는다(사이에 노선 길이를 바꾸면 되돌림 기록을 안 씀).</summary>
        public double A0 = double.NaN, B0 = double.NaN;

        public State Clone() => new()
        {
            TierWalls = TierWalls.Select(w => (double[])w.Clone()).ToList(),
            Ditches = Ditches.Select(w => (double[])w.Clone()).ToList(),
            Marks = Marks.ToList(), Drops = Drops.ToList(), Pvis = Pvis.ToList(),
            Lr = (double[])Lr.Clone(), Route = (double[])Route.Clone(), A0 = A0, B0 = B0,
        };
    }

    public readonly record struct Mark(double S, string Why);
    public readonly record struct Pvi(double S, double Z);

    /// <summary>식으로 바꿈(되돌림 기록 없이).</summary>
    public static State Flip(State st, double b0, double a1)
    {
        var o = new State
        {
            TierWalls = st.TierWalls.Select(w => Span(w, b0, a1)).ToList(),
            Ditches = st.Ditches.Select(w => Span(w, b0, a1)).ToList(),
            Marks = st.Marks.Select(m => double.IsFinite(m.S) ? m with { S = Map(m.S, b0, a1) } : m).ToList(),
            Drops = st.Drops.Select(s => double.IsFinite(s) ? Map(s, b0, a1) : s).ToList(),
            Pvis = st.Pvis.AsEnumerable().Reverse().Select(p => p with { S = Map(p.S, b0, a1) }).ToList(),
            Lr = st.Lr.Length >= 4 ? new[] { st.Lr[1], st.Lr[0], st.Lr[3], st.Lr[2] }.Concat(st.Lr.Skip(4)).ToArray() : (double[])st.Lr.Clone(),
            Route = ReverseRoute(st.Route),
            A0 = double.IsFinite(st.A0) ? a1 : double.NaN,
            B0 = double.IsFinite(st.A0) ? a1 + (b0 - st.A0) : double.NaN,
        };
        return o;
    }

    /// <summary>[x0, y0, x1, y1, …] 꼭짓점 차례 뒤집기(값은 그대로 — 정확).</summary>
    public static double[] ReverseRoute(double[] r)
    {
        int n = r.Length / 2;
        var o = new double[n * 2];
        for (int i = 0; i < n; i++) { o[2 * i] = r[2 * (n - 1 - i)]; o[2 * i + 1] = r[2 * (n - 1 - i) + 1]; }
        return o;
    }

    /// <summary>지문 — 구간 기록 · PVI · 노선은 차례대로 · 측점 · 지운 측점은 측점 차례로 정렬(저장이 측점 순으로 쓰므로).</summary>
    public static string Hash(State st)
    {
        var ci = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        void D(double v) => sb.Append(v.ToString("R", ci)).Append(',');
        sb.Append("T"); foreach (var w in st.TierWalls) { foreach (var v in w) D(v); sb.Append(';'); }
        sb.Append("|D"); foreach (var w in st.Ditches) { foreach (var v in w) D(v); sb.Append(';'); }
        sb.Append("|M"); foreach (var m in st.Marks.OrderBy(m => m.S).ThenBy(m => m.Why, StringComparer.Ordinal)) { D(m.S); sb.Append(m.Why ?? "").Append(';'); }
        sb.Append("|X"); foreach (var s in st.Drops.OrderBy(s => s)) D(s);
        sb.Append("|P"); foreach (var p in st.Pvis) { D(p.S); D(p.Z); sb.Append(';'); }
        sb.Append("|L"); foreach (var v in st.Lr) D(v);
        sb.Append("|R"); foreach (var v in st.Route) D(v);
        // ★[설치 전 검토 H1] 측점 범위 — µm로 반올림(노선을 다시 만들 때 끝자리 잡음은 같은 지문 · 손으로 늘리고 줄인 것은 다른 지문)
        sb.Append("|S").Append(double.IsFinite(st.A0) ? Math.Round(st.A0, 6).ToString("R", ci) : "-").Append(',').Append(double.IsFinite(st.B0) ? Math.Round(st.B0, 6).ToString("R", ci) : "-");
        return Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(Encoding.UTF8.GetBytes(sb.ToString())));
    }

    /// <summary>되돌림 기록 — 바꾼 뒤 값의 지문 · 바꾸기 전 값.</summary>
    public sealed class Snapshot
    {
        public string AfterHash = "";
        public State Prior = new();
    }

    /// <summary>한 번 누름 — 되돌림 기록이 맞으면(지금 값 지문 = 기록의 «바꾼 뒤» 지문) 바꾸기 전 값 그대로(<c>Exact</c>) · 아니면 식. 새 되돌림 기록도 돌려줌.</summary>
    public static (State Next, Snapshot Snap, bool Exact) Toggle(State now, double b0, double a1, Snapshot? snap)
    {
        bool exact = snap != null && snap.AfterHash.Length > 0 && snap.AfterHash == Hash(now);
        var f = Flip(now, b0, a1);
        // ★[설치 전 검토 H1] 되돌림 기록의 값이 지금 값을 식으로 바꾼 것과 1e-6 안일 때만 그대로 씀 — 아니면(노선이 바뀜 · 지문이 우연히 같음) 식
        if (exact && !Close(f, snap!.Prior, 1e-6)) exact = false;
        var next = exact ? snap!.Prior.Clone() : f;
        return (next, new Snapshot { AfterHash = Hash(next), Prior = now.Clone() }, exact);
    }

    /// <summary>두 값 한 벌이 <paramref name="tol"/> 안에서 같은가(측점 · 높이 · 좌표 · 칸 수 · 사유 글 · NaN끼리 같음).</summary>
    public static bool Close(State a, State b, double tol)
    {
        bool D(double x, double y) => (double.IsNaN(x) && double.IsNaN(y)) || Math.Abs(x - y) <= tol;
        bool Arr(double[] x, double[] y) => x.Length == y.Length && x.Zip(y).All(q => D(q.First, q.Second));
        bool Recs(List<double[]> x, List<double[]> y) => x.Count == y.Count && x.Zip(y).All(q => Arr(q.First, q.Second));
        if (!Recs(a.TierWalls, b.TierWalls) || !Recs(a.Ditches, b.Ditches) || !Arr(a.Lr, b.Lr) || !Arr(a.Route, b.Route)) return false;
        if (!D(a.A0, b.A0) || !D(a.B0, b.B0)) return false;
        if (a.Pvis.Count != b.Pvis.Count || !a.Pvis.Zip(b.Pvis).All(q => D(q.First.S, q.Second.S) && D(q.First.Z, q.Second.Z))) return false;
        var ma = a.Marks.OrderBy(m => m.S).ToList(); var mb = b.Marks.OrderBy(m => m.S).ToList();
        if (ma.Count != mb.Count || !ma.Zip(mb).All(q => D(q.First.S, q.Second.S) && string.Equals(q.First.Why ?? "", q.Second.Why ?? "", StringComparison.Ordinal))) return false;
        var da = a.Drops.OrderBy(v => v).ToArray(); var db = b.Drops.OrderBy(v => v).ToArray();
        return Arr(da, db);
    }

    static readonly JsonSerializerOptions Js = new() { IncludeFields = true, NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals };

    public static string ToJson(Snapshot s) => JsonSerializer.Serialize(s, Js);

    public static Snapshot? FromJson(string? t)
    {
        if (string.IsNullOrWhiteSpace(t)) return null;
        try { return JsonSerializer.Deserialize<Snapshot>(t, Js); } catch { return null; }
    }

    /// <summary>바꾼 기록 수(알림 한 줄).</summary>
    public static int Count(State st) => st.TierWalls.Count + st.Ditches.Count + st.Marks.Count + st.Drops.Count + st.Pvis.Count;
}
