namespace DH.Grading.Core;

/// <summary>★★★[로드맵 F «선 옹벽 3D 몸통 · 터파기» · `2026-11-11_옹벽추가_자문기반_로드맵.md` 4절 · D6 ①] <b>선 옹벽 → 앞면 아랫선</b>.
/// <para>선 옹벽(종류 2 · 모양 2 수직 · 3 다단)의 면 토막(<see cref="WallByLine.FaceSeg"/> — 짓기가 적은 머리 · 발 점)에서 앞면 아랫선(<see cref="FaceLine"/>)을 만든다.
/// 그 뒤는 구간 옹벽과 <b>같은 길</b>(<see cref="Measure"/> → <see cref="Sectionize"/> → 터파기 띠 · 면 기록 · 3D 몸통) — 지형 TIN의 1:0.01 띠는 그대로 둔다(구조물만 따로).</para>
/// <list type="bullet">
/// <item>앞면 아랫선 = 발 줄(절토 L′ 위 P0 · 성토 L 위 G · 다단 안쪽 단은 발 줄 위 P0′) · Z = 발 높이(기록 벽 밑 — <see cref="Measure"/>의 «어긋남» 판정).</item>
/// <item>흙 쪽 = 머리가 발 줄의 어느 쪽인가(띠 폭 w = max(s·h, 0.25 mm) &gt; 0 — 점마다 부호를 세어 다수 · <see cref="RunFace"/>와 같은 법). 절토 = 선 바깥(R_out · 남은 땅) · 성토 = 안쪽(쌓은 흙).</item>
/// <item>앞면 너비 = 머리 ↔ 발 수직 거리 최대(뒤 표본이 벽 머리 너머에 서게) · 앞면 구배 = 그 옹벽을 지은 구배(1:0.01 — 3D 앞면 맞춤 전단 · 쐐기 공제 기준).</item>
/// <item>단마다 따로 옹벽(JACK — 다단 = 단마다 그 단 높이): 묶음 = 옹벽 번호 · 단 번호 = <see cref="WallByLine.FaceSeg.Tier"/>(1 = 선 L에 선 바깥 단) ·
/// 배면사면(맨 위 단)은 절토 바닥 줄이 남은 가장 바깥 단 · 아래 소단 폭(⚠ 재료)은 다단만 — 절토 바깥쪽 단(안쪽 단이 있을 때) · 성토 2단부터.</item>
/// </list></summary>
public static partial class WallExcav
{
    /// <summary>면 토막 하나 → 앞면 아랫선. 마구리 · 점 모자람 · 흙 쪽을 못 가리면 null + 까닭.</summary>
    public static FaceLine? LineFace(WallByLine.FaceSeg fs, double slopeW, string src, out string why)
    {
        why = "";
        if (fs == null || fs.Kind != 0) { why = $"{src}: 앞면 토막이 아님(마구리)"; return null; }
        int n = Math.Min(fs.Top.Count, fs.Foot.Count);
        if (n < 2) { why = $"{src}: 면 점 {n}"; return null; }
        double side = 0, run = 0; int votes = 0;
        for (int i = 0; i < n; i++)
        {
            var a = fs.Foot[Math.Max(0, i - 1)]; var b = fs.Foot[Math.Min(n - 1, i + 1)];
            double tx = b.X - a.X, ty = b.Y - a.Y, tl = Math.Sqrt(tx * tx + ty * ty);
            if (tl < 1e-12) continue;
            double vx = fs.Top[i].X - fs.Foot[i].X, vy = fs.Top[i].Y - fs.Foot[i].Y;
            double cr = (tx * vy - ty * vx) / tl;   // 머리의 발 줄 왼쪽 거리(+ 왼쪽)
            if (Math.Abs(cr) < 1e-9) continue;
            side += Math.Sign(cr); votes++;
            run = Math.Max(run, Math.Abs(cr));
        }
        if (votes == 0 || Math.Abs(side) < 0.5 * votes) { why = $"{src}: 흙 쪽을 못 가렸다(머리가 발 줄 양쪽에 섞임 {side:F0}/{votes})"; return null; }
        var f = new FaceLine { Soil = side > 0 ? +1 : -1, FaceRun = Math.Max(1e-4, run), SlopeW = slopeW, Src = src };
        foreach (var q in fs.Foot.Take(n))
            if (f.Pts.Count == 0 || Math.Abs(f.Pts[^1].X - q.X) > 1e-9 || Math.Abs(f.Pts[^1].Y - q.Y) > 1e-9) f.Pts.Add(q);
        if (f.Pts.Count < 2) { why = $"{src}: 발 줄이 점 하나"; return null; }
        return f;
    }

    /// <summary>선 옹벽 한 칸 → 바닥 줄(구간 옹벽과 같은 <see cref="Measure"/> · foot 0) + 아래 소단 폭(없으면 NaN).
    /// <paramref name="used"/>가 있으면 만든 앞면 아랫선을 차례대로 담는다(부르는 쪽 지문).</summary>
    public static List<(Bottom B, double BenchBelow)> LineBottoms(IReadOnlyList<WallByLine.FaceSeg> faces, int shape, double tierB, double slopeW,
        Func<double, double, double?> surf, string src, MeasureNote mn, List<string> notes, List<FaceLine>? used = null)
    {
        var res = new List<(Bottom B, double BenchBelow)>();
        var fronts = faces.Where(f => f != null && f.Kind == 0).ToList();
        int upMax = fronts.Where(f => f.Up).Select(f => f.Tier).DefaultIfEmpty(0).Max();
        var cnt = fronts.GroupBy(f => (f.Up, f.Tier)).ToDictionary(g => g.Key, g => g.Count());
        var idx = new Dictionary<(bool, int), int>();
        foreach (var fs in fronts)
        {
            var key = (fs.Up, fs.Tier);
            idx[key] = (idx.TryGetValue(key, out int k0) ? k0 : 0) + 1;
            string fsrc = $"{src} {(fs.Up ? "절토" : "성토")} {fs.Tier}단" + (cnt[key] > 1 ? $"-{idx[key]}" : "");
            var f = LineFace(fs, slopeW, fsrc, out string why);
            if (f == null) { notes.Add(why); continue; }
            used?.Add(f);
            double bb = shape == 3 && tierB > 0 && (fs.Up ? fs.Tier < upMax : fs.Tier >= 2) ? tierB : double.NaN;
            foreach (var b0 in Measure(f, surf, 0, mn))
            {
                b0.Up = fs.Up; b0.Group = src; b0.Step = fs.Tier;
                res.Add((b0, bb));
            }
        }
        // 배면사면 대상(맨 위 단 · 절토만) — 바닥 줄이 남은 가장 바깥 단
        var ups = res.Where(x => x.B.Up).ToList();
        if (ups.Count > 0) { int top = ups.Min(x => x.B.Step); foreach (var x in ups) if (x.B.Step == top) x.B.Top = true; }
        return res;
    }

    /// <summary>몸통 수량(평균단면법) — 3D 몸통과 같은 조각(<see cref="WallSweep.Solids"/>) 넓이를 점 사이 사다리꼴로 · 재료별 ㎥(곧은 줄은 3D 메시 부피와 같다).</summary>
    public static SortedDictionary<string, double> BodyVolume(SecLine sl)
    {
        var res = new SortedDictionary<string, double>(StringComparer.Ordinal);
        static double Area(IReadOnlyList<(double X, double Z)> r) { double a = 0; for (int i = 0; i < r.Count; i++) { var p = r[i]; var q = r[(i + 1) % r.Count]; a += p.X * q.Z - q.X * p.Z; } return Math.Abs(a) / 2; }
        for (int i = 0; i + 1 < sl.Pts.Count; i++)
        {
            double ds = sl.Pts[i + 1].S - sl.Pts[i].S;
            if (!(ds > 0)) continue;
            foreach (var sh in new[] { sl.Pts[i].Shape, sl.Pts[i + 1].Shape })
                foreach (var p in WallSweep.Solids(sh))
                    res[p.Material] = (res.TryGetValue(p.Material, out double v) ? v : 0) + Area(p.Ring) * ds / 2;
        }
        return res;
    }

    /// <summary>선 옹벽 몸통 · 수량 글 — 옹벽(묶음 «선 옹벽 N»)마다 단 · 형식 · 줄 · 길이 · 최고 높이 · 몸통 부피(재료별). 선 옹벽이 없으면 빈 글(구간 옹벽만인 도면의 로그는 그대로).</summary>
    public static string LineBodyText(IEnumerable<(Bottom B, SecLine S)> secs)
    {
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        var outs = new List<string>();
        foreach (var g in secs.Where(x => x.B.Group.StartsWith("선 옹벽 ", StringComparison.Ordinal)).GroupBy(x => x.B.Group))
        {
            var vol = new SortedDictionary<string, double>(StringComparer.Ordinal);
            foreach (var (_, s) in g) foreach (var kv in BodyVolume(s)) vol[kv.Key] = (vol.TryGetValue(kv.Key, out double v) ? v : 0) + kv.Value;
            string tiers = string.Join(" · ", g.GroupBy(x => (x.S.Up, x.B.Step, x.S.Kind)).OrderBy(t => t.Key.Up ? 0 : 1).ThenBy(t => t.Key.Step).Select(t => string.Format(ci,
                "{0} {1}단 {2} {3}줄 {4:F2} m · h ≤{5:F2} m", t.Key.Up ? "절토" : "성토", t.Key.Step, WallSection.Of(t.Key.Kind).Name, t.Count(),
                t.Sum(x => x.S.Pts.Count == 0 ? 0 : x.S.Pts[^1].S - x.S.Pts[0].S), t.Max(x => x.S.Pts.Count == 0 ? 0 : x.S.Pts.Max(p => p.H)))));
            outs.Add($"{g.Key} 몸통 — {tiers} · " + (vol.Count == 0 ? "부피 0" : string.Join(" · ", vol.Select(kv => string.Format(ci, "{0} {1:F2}㎥", kv.Key, kv.Value)))));
        }
        return string.Join(" | ", outs);
    }
}
