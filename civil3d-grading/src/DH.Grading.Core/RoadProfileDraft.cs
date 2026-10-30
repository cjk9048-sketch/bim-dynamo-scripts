namespace DH.Grading.Core;

/// <summary>★★[v110 · 계획_v110.md 3.4 · 인터뷰 «원지반 따라 자동 초안»] 진입도로 계획 종단 초안 — 바탕 종단을 따르되
/// 최대 종단경사 · 최소 구간 길이를 지키고 PVI는 적게. 시점 = 원지반(기존 도로) · 종점 = 계획고(부지).
/// <para>① 바탕을 구간 길이만큼 고르게(이동평균) ② 시점에서 · 종점에서 최대 경사로 갈 수 있는 띠(둘의 겹침) 안으로 자름
/// ③ 이웃 표본 사이 경사를 앞 · 뒤로 두 번 깎음 ④ 더글라스-포이커로 PVI 줄임(허용 tol) ⑤ 최소 구간 길이보다 가까운 PVI는 덜 중요한 쪽을 뺌.
/// 경사 한도를 지킨 곡선 위의 두 점을 잇는 직선은 한도를 넘지 않는다(평균값 정리) — 줄여도 한도 그대로.</para></summary>
public static class RoadProfileDraft
{
    public sealed class Result
    {
        public List<(double S, double Z)> Pvi = new();
        public double MaxGrade;            // 실제 최대 경사(절댓값)
        public bool EndUnreachable;        // 종점 높이를 최대 경사로 못 맞춤(그래도 곧은 선으로 잇고 알림)
        public int StationAdded;           // ★[v111.2] 주측점마다 더한 PVI 수(모양 그대로 · 잡아끌기용)
        public string Text => $"PVI {Pvi.Count}개" + (StationAdded > 0 ? $"(주측점 {StationAdded}개 포함)" : "") + $" · 최대 종단경사 {MaxGrade * 100:F1} %" + (EndUnreachable ? " · ⚠종점 높이를 경사 한도로 못 맞춤" : "");
    }

    public static Result Make(IReadOnlyList<double> s, IReadOnlyList<double> g, double startZ, double endZ,
                              double maxGrade = 0.12, double minLen = 20, double tol = 0.3)
    {
        var res = new Result();
        int n = Math.Min(s.Count, g.Count);
        if (n < 2) return res;
        double s0 = s[0], sN = s[n - 1];
        if (!double.IsFinite(startZ)) startZ = g[0];
        if (!double.IsFinite(endZ)) endZ = g[n - 1];
        maxGrade = Math.Max(1e-4, maxGrade);
        minLen = double.IsFinite(minLen) ? Math.Max(0, minLen) : 20;      // ★[설치 전 검토 L4] 음수면 아래 a++가 배열 밖으로
        // ① 고르게(창 = minLen)
        var sm = new double[n];
        for (int i = 0, a = 0, b = 0; i < n; i++)
        {
            while (a < i && s[a] < s[i] - minLen / 2) a++;
            while (b + 1 < n && s[b + 1] <= s[i] + minLen / 2) b++;
            double sum = 0; int c = 0;
            for (int k = a; k <= b; k++) if (double.IsFinite(g[k])) { sum += g[k]; c++; }
            sm[i] = c > 0 ? sum / c : double.NaN;
        }
        for (int i = 0; i < n; i++) if (!double.IsFinite(sm[i])) sm[i] = startZ + (endZ - startZ) * (s[i] - s0) / Math.Max(1e-9, sN - s0);
        // ② 갈 수 있는 띠
        res.EndUnreachable = Math.Abs(endZ - startZ) > maxGrade * (sN - s0) + 1e-9;
        var z = new double[n];
        for (int i = 0; i < n; i++)
        {
            if (res.EndUnreachable) { z[i] = startZ + (endZ - startZ) * (s[i] - s0) / Math.Max(1e-9, sN - s0); continue; }
            double lo = Math.Max(startZ - maxGrade * (s[i] - s0), endZ - maxGrade * (sN - s[i]));
            double hi = Math.Min(startZ + maxGrade * (s[i] - s0), endZ + maxGrade * (sN - s[i]));
            z[i] = Math.Clamp(sm[i], lo, hi);
        }
        z[0] = startZ; z[n - 1] = endZ;
        // ③ 이웃 경사 깎기(앞 · 뒤)
        if (!res.EndUnreachable)
            for (int pass = 0; pass < 2; pass++)
            {
                for (int i = 1; i < n; i++) { double ds = s[i] - s[i - 1]; z[i] = Math.Clamp(z[i], z[i - 1] - maxGrade * ds, z[i - 1] + maxGrade * ds); }
                z[n - 1] = endZ;
                for (int i = n - 2; i >= 0; i--) { double ds = s[i + 1] - s[i]; z[i] = Math.Clamp(z[i], z[i + 1] - maxGrade * ds, z[i + 1] + maxGrade * ds); }
                z[0] = startZ;
            }
        // ④ 더글라스-포이커
        var keep = new bool[n]; keep[0] = keep[n - 1] = true;
        void Dp(int a, int b)
        {
            if (b <= a + 1) return;
            double best = -1; int bi = -1;
            for (int i = a + 1; i < b; i++)
            {
                double zl = z[a] + (z[b] - z[a]) * (s[i] - s[a]) / Math.Max(1e-9, s[b] - s[a]);
                double d = Math.Abs(z[i] - zl);
                if (d > best) { best = d; bi = i; }
            }
            if (best > tol) { keep[bi] = true; Dp(a, bi); Dp(bi, b); }
        }
        Dp(0, n - 1);
        var idx = Enumerable.Range(0, n).Where(i => keep[i]).ToList();
        // ⑤ 최소 구간 길이 — 너무 가까운 안쪽 PVI는 앞뒤를 이은 직선에서 덜 벗어난 쪽부터 뺀다
        //   ★[설치 전 검토 L5] 종전엔 처음 걸린 것을 뺐다(꼭대기 PVI가 빠질 수 있음) — 걸린 것 중 가장 덜 벗어난 것 하나씩
        while (idx.Count > 2)
        {
            int pick = -1; double least = double.MaxValue;
            for (int k = 1; k < idx.Count - 1; k++)
            {
                if (s[idx[k]] - s[idx[k - 1]] >= minLen - 1e-9 && s[idx[k + 1]] - s[idx[k]] >= minLen - 1e-9) continue;
                int a = idx[k - 1], b = idx[k + 1], i = idx[k];
                double dev = Math.Abs(z[i] - (z[a] + (z[b] - z[a]) * (s[i] - s[a]) / Math.Max(1e-9, s[b] - s[a])));
                if (dev < least) { least = dev; pick = k; }
            }
            if (pick < 0) break;
            idx.RemoveAt(pick);
        }
        foreach (int i in idx) res.Pvi.Add((s[i], z[i]));
        for (int k = 1; k < res.Pvi.Count; k++)
            res.MaxGrade = Math.Max(res.MaxGrade, Math.Abs(res.Pvi[k].Z - res.Pvi[k - 1].Z) / Math.Max(1e-9, res.Pvi[k].S - res.Pvi[k - 1].S));
        return res;
    }

    /// <summary>★★[v111.2 · JACK 1030 «종단상 직선구간이더라도 주측점엔 PVI점 — 사용자가 잡아끌 수 있게»] 주측점(측점 간격의 배수 = 종단도 No. 자리)마다 PVI를 더한다 —
    /// 그 자리 꺾은선 높이라 모양은 그대로. 처음 · 끝 PVI 안쪽만 · 이미 PVI가 <paramref name="gap"/> 안에 있으면 안 넣음. 돌려줌 = 측점 차례로 합친 목록 · 더한 수.</summary>
    public static (List<(double S, double Z)> Pvi, int Added) AddStations(IReadOnlyList<(double S, double Z)> pvi, double interval, double gap = double.NaN)
    {
        var res = pvi.ToList();
        if (pvi.Count < 2 || !(interval > 0)) return (res, 0);
        interval = Math.Max(1.0, interval);   // ★[v111.2 · 전체 검토 L] 아주 작은 간격(0.001 등)이면 수백만 번 — 1 m 아래는 안 받음
        // ★[설치 전 검토 L3] 경사 꺾임 PVI와 너무 붙으면(끌면 극단 경사 · 라벨 겹침) 안 넣음 — 간격의 1/4 · 2.5 m 중 작은 것
        if (!(gap >= 0)) gap = Math.Min(interval / 4, 2.5);
        gap = Math.Max(gap, 1e-6);            // 같은 측점 PVI 두 개 막음
        double s0 = pvi[0].S, s1 = pvi[^1].S;
        int added = 0;
        // ★[설치 전 검토 L2] 노선 시점부터 센다(횡단 측점 StationMarks와 같은 자리 — 시점이 0이 아닌 노선)
        for (long k = 1; s0 + k * interval < s1 - gap; k++)
        {
            double st = s0 + k * interval;
            if (st <= s0 + gap) continue;
            if (pvi.Any(p => Math.Abs(p.S - st) < gap)) continue;
            res.Add((st, At(pvi, st)));
            added++;
        }
        res.Sort((a, b) => a.S.CompareTo(b.S));
        return (res, added);
    }

    /// <summary>PVI 꺾은선의 측점 s 높이(직선 보간 · 범위 밖은 끝값).</summary>
    public static double At(IReadOnlyList<(double S, double Z)> pvi, double s)
    {
        if (pvi.Count == 0) return double.NaN;
        if (s <= pvi[0].S) return pvi[0].Z;
        for (int k = 1; k < pvi.Count; k++)
            if (s <= pvi[k].S) return pvi[k - 1].Z + (pvi[k].Z - pvi[k - 1].Z) * (s - pvi[k - 1].S) / Math.Max(1e-9, pvi[k].S - pvi[k - 1].S);
        return pvi[^1].Z;
    }
}
