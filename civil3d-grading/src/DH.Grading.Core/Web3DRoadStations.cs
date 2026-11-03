namespace DH.Grading.Core;

/// <summary>★★[JACK 1103 «3D 뷰에서 진입도로 측점이 없어 · 좌측 상단에 부지 측점, 진입도로 측점이 별도로»] <b>[3D 보기] 진입도로 측점</b> — 도로 하나의 머리.
/// <para>꼴은 부지 측점(머리 «align»)과 같다 — <c>{name, line, s0, s1, step, index, views, st: [{s, label, major, views, c, l, r, pts}]}</c>(뷰어 <c>extras.js</c>가 같은 코드로 그린다).
/// 중심선 = s0부터 <see cref="Step"/> m마다 + 끝 측점 · 가로선 = 왼 끝 → 오른 끝 1 m마다 · 높이 = 지형 + <see cref="Lift"/> m(없으면 NaN → 머리 −99999 · 뷰어가 끊음) · 점은 원점 뺌.</para>
/// <para>노선 자리는 <paramref name="at"/>(측점, 옆거리) → 도면 좌표(못 얻으면 null) — Civil 노선 <c>PointLocation</c>을 부르는 쪽이 넘긴다(여기는 도면 없이 잰다).</para>
/// <para>부르는 쪽이 도로가 있을 때만 머리 «roadAlign»(도로 목록)을 넣는다 — 도로 없는 장면은 바이트 그대로.</para></summary>
public static class Web3DRoadStations
{
    /// <summary>측점 선 높이 — 지형 위(부지 측점과 같음).</summary>
    public const double Lift = 0.3;

    /// <summary>중심선 점 간격(m) — 부지 측점과 같음(뷰어 alignAt이 이 간격으로 측점 값을 짚는다).</summary>
    public const double Step = 2.0;

    /// <summary>측점 하나(입력) — 측점 값 · 이름(종단도 검토선 이름 꼬리) · 그 검토선의 횡단 뷰 수.</summary>
    public readonly record struct Mark(double S, string Label, int Views);

    /// <summary>도로 하나 머리 — 자리를 하나도 못 얻으면 null. <paramref name="skipped"/> = 노선 밖(자리 못 얻음)으로 뺀 측점 수.
    /// 측점은 값 차례 · 1 cm 안 겹침은 첫 것만(부지와 같은 법).</summary>
    public static Dictionary<string, object>? Head(Web3DScene sc, string name, double s0, double s1, double index, double wl, double wr,
        IEnumerable<Mark> marks, Func<double, double, (double X, double Y)?> at, Func<double, double, double?> z, double ox, double oy, out int skipped)
    {
        skipped = 0;
        if (!(double.IsFinite(s0) && double.IsFinite(s1)) || s1 <= s0) return null;
        wl = Math.Max(0, double.IsFinite(wl) ? wl : 0); wr = Math.Max(0, double.IsFinite(wr) ? wr : 0);
        if (wl + wr < 1e-6) return null;
        double ZL(double x, double y) => z(x, y) is double v && double.IsFinite(v) ? v + Lift : double.NaN;
        double H(double v) => double.IsFinite(v) ? Web3DScene.Num(v, 3) : -99999;

        // 측점 — 값 차례 · 1 cm 겹침 뺌 · 자리 못 얻은 것은 그 측점만 뺌
        var seen = new HashSet<long>();
        var rows = new List<(Mark M, double[] C, double[] L, double[] R, float[] P)>();
        foreach (var m in marks.Where(m => double.IsFinite(m.S)).OrderBy(m => m.S))
        {
            if (!seen.Add((long)Math.Round(m.S * 100))) continue;
            var c = at(m.S, 0); var l = at(m.S, -wl); var r = at(m.S, wr);
            if (c == null || l == null || r == null) { skipped++; continue; }
            int np = (int)Math.Ceiling(wl + wr);
            var pp = new float[(np + 1) * 3];
            bool ok = true;
            for (int i = 0; i <= np; i++)
            {
                var q = at(m.S, -wl + (wl + wr) * i / np);
                if (q == null) { ok = false; break; }
                pp[i * 3] = (float)(q.Value.X - ox); pp[i * 3 + 1] = (float)(q.Value.Y - oy); pp[i * 3 + 2] = (float)ZL(q.Value.X, q.Value.Y);
            }
            if (!ok) { skipped++; continue; }
            double[] V((double X, double Y) p) => new[] { p.X - ox, p.Y - oy, ZL(p.X, p.Y) };
            rows.Add((m, V(c.Value), V(l.Value), V(r.Value), pp));
        }
        if (rows.Count == 0) return null;

        // 중심선 — s0부터 Step마다 + 끝(마지막 토막은 Step보다 짧을 수 있음) · 자리 못 얻은 점 = NaN(뷰어가 끊음)
        var line = new List<float>();
        void At(double st)
        {
            var p = at(st, 0);
            if (p == null) { line.Add(float.NaN); line.Add(float.NaN); line.Add(float.NaN); return; }
            line.Add((float)(p.Value.X - ox)); line.Add((float)(p.Value.Y - oy)); line.Add((float)ZL(p.Value.X, p.Value.Y));
        }
        double last = s0;
        for (int k = 0; s0 + k * Step <= s1 + 1e-6; k++) { last = Math.Min(s0 + k * Step, s1); At(last); }
        if (s1 - last > 1e-6) At(s1);

        return new Dictionary<string, object>
        {
            ["name"] = name, ["line"] = sc.F32(line.ToArray()), ["s0"] = Web3DScene.Num(s0, 3), ["s1"] = Web3DScene.Num(s1, 3), ["step"] = Step, ["index"] = Web3DScene.Num(index, 3),
            ["views"] = rows.Sum(q => q.M.Views),
            ["st"] = rows.Select(q => (object)new Dictionary<string, object>
            {
                ["s"] = Web3DScene.Num(q.M.S, 3), ["label"] = q.M.Label, ["major"] = StationNaming.IsMajor(q.M.S, index, out _), ["views"] = q.M.Views,
                ["c"] = q.C.Select(H).ToArray(), ["l"] = q.L.Select(H).ToArray(), ["r"] = q.R.Select(H).ToArray(), ["pts"] = sc.F32(q.P),
            }).ToList(),
        };
    }

    /// <summary>검토선 꼭짓점 옆거리들 → (왼 폭, 오른 폭) — 왼 = −(가장 작은 옆거리) · 오른 = 가장 큰 옆거리(0 아래로는 안 감). 꼭짓점이 없으면 (0, 0).</summary>
    public static (double L, double R) Reach(IEnumerable<double> offsets)
    {
        double l = 0, r = 0;
        foreach (double o in offsets)
        {
            if (!double.IsFinite(o)) continue;
            if (o < 0) l = Math.Max(l, -o); else r = Math.Max(r, o);
        }
        return (l, r);
    }
}
