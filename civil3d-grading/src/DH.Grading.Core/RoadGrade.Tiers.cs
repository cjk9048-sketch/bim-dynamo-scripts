namespace DH.Grading.Core;

/// <summary>★★[v110.3 · 계획_v110_3.md 2 · 3 · 검토 H2 · H3 · L5 · L6 · JACK 1029 «평면뷰에서 사면 모양이 2D 설계도처럼 소단선과 같은 방향» · «계획부지처럼 소단선을 변환»]
/// 진입도로 평면 표현 — 단마다 사면선(윗선) · 소단선(아랫선) · 옹벽선 · 노리선 + 계단선 찍기.
/// <para>선 = 칸마다 «도로 띠까지 수평거리» <see cref="Result.D"/>의 등치선(꺾임 거리마다 · 칸 사이 보간) → 선분마다 그 자리 단면(쪽 · 측점 → 기록 → 기본/변형)에 그 꺾임이 있고
/// 바뀐 자리(F &lt; 0) · 종류(절/성)가 맞는 부분만 남김. 단 좌표 하나로 그리면 소단에서 이긴 원뿔이 동률이고 사면 · 소단 기울기가 달라 선이 수십 cm 흔들린다(검토 H2).</para>
/// <para>윗선 · 아랫선은 부지(SlopeHatchGenerator)와 같은 뜻 — 사면선 = 윗선(높은 쪽) · 소단선 = 아랫선 · 노리선은 윗선에서 아랫선 쪽으로.
/// 절토 = 윗선이 바깥(B) · 성토 = 윗선이 안(A) — 성토 1단 윗선 = 도로 가장자리(표본에서).</para></summary>
public static partial class RoadGrade
{
    public enum LineRole { Crest, Toe, Wall }

    /// <summary>계단선 하나 — 절토/성토 · 쪽 · 단 k · 윗선/아랫선/옹벽선 · 단면 번호 · 점(x, y, z = 설계 높이, s = 측점).</summary>
    public sealed class TierLine
    {
        public bool Cut; public int Side; public int K; public LineRole Role; public int Prof;
        public List<(double X, double Y, double Z, double S)> Pts = new();
    }

    /// <summary>칸 가운데 격자 겹선형 보간(어느 꼭짓점이든 NaN이면 NaN · 격자 밖 NaN).</summary>
    static double Bil(Result r, float[] g, double x, double y)
    {
        double gi = (x - r.X0) / r.Cell - 0.5, gj = (y - r.Y0) / r.Cell - 0.5;
        int i = (int)Math.Floor(gi), j = (int)Math.Floor(gj);
        if (i < 0 || j < 0 || i + 1 >= r.W || j + 1 >= r.H) return double.NaN;
        double u = gi - i, v = gj - j;
        float a = g[j * r.W + i], b = g[j * r.W + i + 1], c = g[(j + 1) * r.W + i], d = g[(j + 1) * r.W + i + 1];
        if (!float.IsFinite(a) || !float.IsFinite(b) || !float.IsFinite(c) || !float.IsFinite(d)) return double.NaN;
        return (1 - u) * (1 - v) * a + u * (1 - v) * b + (1 - u) * v * c + u * v * d;
    }

    static int Cell(Result r, double x, double y)
    {
        int i = (int)Math.Floor((x - r.X0) / r.Cell), j = (int)Math.Floor((y - r.Y0) / r.Cell);
        return i < 0 || j < 0 || i >= r.W || j >= r.H ? -1 : j * r.W + i;
    }

    /// <summary>그 자리의 단면 번호(쪽 · 측점 → 나중 기록이 이김 · 끝 너머 = 기본).</summary>
    static int ProfAt(Result r, int side, double s)
    {
        if (side == 0) return 0;
        for (int i = r.Recs.Count - 1; i >= 0; i--)
            if (r.Recs[i].Side == side && s >= r.Recs[i].S0 - 1e-9 && s <= r.Recs[i].S1 + 1e-9) return Math.Max(0, r.RecProf[i]);   // 0단 · 소단 0(−1) = 가장자리 옹벽 — 사면 없음(기본으로 둬도 F ≥ 0이라 선 안 남음)
        return 0;
    }

    /// <summary>★[v110.3 · V60 그림] 두 칸 사이에 옹벽 면(평면폭 단높이 × MinSlope = 5 cm)이 끼어 있으면 F의 0 자리를 그 면에 — 칸 a → b 비율(0~1) · 없으면 null.
    /// <para>옹벽 면에서 F가 몇 m를 뛰어 선형 보간이 0 자리를 칸 안 아무 데나 놓는다(데이라잇 · 면 둘레가 옹벽을 따라 다시 톱니) → 거리장 D로 면 자리를 찾는다(D는 매끈).</para></summary>
    internal static double? WallCrossT(Result r, int a, int b)
    {
        if (r.D.Length != r.F.Length || r.CutProfs.Count == 0) return null;
        float fa = r.F[a], fb = r.F[b], da = r.D[a], db = r.D[b];
        if (!float.IsFinite(fa) || !float.IsFinite(fb) || !float.IsFinite(da) || !float.IsFinite(db) || (fa < 0) == (fb < 0) || Math.Abs(db - da) < 1e-9) return null;
        int inside = fa < 0 ? a : b, outside = inside == a ? b : a;
        byte kind = r.Kind[inside];
        // ★[v110.3.1] 0단 옹벽에 땅이 닿으면 안쪽이 도로 칸 — 바깥 땅이 도로보다 높으면 절토
        if (kind == 1 && float.IsFinite(r.Base[outside]) && float.IsFinite(r.Z[inside])) kind = r.Base[outside] > r.Z[inside] ? (byte)2 : (byte)3;
        if (kind != 2 && kind != 3) return null;
        int prof = ProfAt(r, r.FSide[inside], r.FS[inside]);
        // ★[V61 계측] 도로 칸이 옹벽 구간 끝 밖 측점이면 바깥 칸(벽 쪽) 측점으로(면 세모 EdgeProf와 같은 규칙)
        if (prof == 0 && r.Kind[inside] == 1 && float.IsFinite(r.FS[outside]) && r.FSide[outside] != 0) prof = ProfAt(r, r.FSide[outside], r.FS[outside]);
        if (prof == 0) return null;
        // ★[설치 전 검토 L8] 사이에 벽이 여럿이면(소단 < 칸) 선형 0 자리에 가장 가까운 벽
        double t0 = fa / (fa - fb), bestT = double.NaN, bestGap = double.MaxValue;
        double dmn = Math.Min(da, db), dmx = Math.Max(da, db);
        foreach (var f in (kind == 2 ? r.CutProfs : r.FillProfs)[prof].Faces)
        {
            if (!f.Wall) continue;
            // ★[v110.3.1 · ⑳n 계측] 면과 두 칸 사이가 겹치면 — 한 끝이 면 위(5 cm 안)에 있어도(바깥 칸이 면 위면 선형 보간이 0 자리를 면 앞 소단에 놓아 소단이 땅까지 기울었다)
            double x0 = Math.Max(f.A, dmn), x1 = Math.Min(f.B, dmx);
            if (!(x1 > x0)) continue;
            double x = (x0 + x1) / 2;
            double t = (x - da) / (db - da);
            if (Math.Abs(t - t0) < bestGap) { bestGap = Math.Abs(t - t0); bestT = t; }
        }
        return double.IsNaN(bestT) ? null : bestT;
    }

    /// <summary>등치선 선분(칸 사이 보간 · 마칭 스퀘어) — 여러 값을 <b>격자 한 번 훑기</b>로(★[v110.3 설치 전 검토 M2] 꺾임 거리마다 격자 전체를 훑으면 PVI를 끌 때마다 수백 ms).
    /// 네모마다 네 모서리 값의 범위에 드는 값만 처리 · 값 NaN · 격자 밖은 «위»(그 값보다 큼)로. <paramref name="levels"/>는 오름차순.</summary>
    static List<((double X, double Y) A, (double X, double Y) B)>[] ContourMany(Result r, float[] g, double[] levels, float[]? legS = null, double legJump = double.PositiveInfinity)
    {
        int W = r.W, H = r.H, nl = levels.Length;
        var res = new List<((double, double), (double, double))>[nl];
        for (int k = 0; k < nl; k++) res[k] = new();
        if (nl == 0) return res;
        double dJump = Math.Sqrt(2) * r.Cell + 0.05;
        for (int j = 0; j + 1 < H; j++)
            for (int i = 0; i + 1 < W; i++)
            {
                int ia = j * W + i, ib = ia + 1, ic = ia + W + 1, id = ia + W;
                float ga = g[ia], gb = g[ib], gc = g[ic], gd = g[id];
                double mn = double.PositiveInfinity, mx = double.NegativeInfinity; bool nan = false;
                foreach (float v in new[] { ga, gb, gc, gd }) { if (!float.IsFinite(v)) { nan = true; continue; } mn = Math.Min(mn, v); mx = Math.Max(mx, v); }
                if (!double.IsFinite(mn)) continue;
                if (nan) mx = double.PositiveInfinity;
                // ★[v110.3 재검토 · 중간 · 설치 전 검토 M3] 두 다리 주름 = 측점이 뛰고 «동시에» 거리도 뜀 — 곡선 안쪽은 한 다리여도 측점이 빨리 변해 측점만 보면 바깥 단 선이 빠진다
                if (legS != null)
                {
                    float s0 = legS[ia], s1 = legS[ib], s2 = legS[ic], s3 = legS[id];
                    double sJ = Math.Max(Math.Max(s0, s1), Math.Max(s2, s3)) - Math.Min(Math.Min(s0, s1), Math.Min(s2, s3));
                    if (sJ > legJump && !nan && mx - mn > dJump) continue;
                }
                // 이 네모에 걸린 값들 — mn < level ≤ mx
                int lo = 0, hi = nl;
                while (lo < hi) { int m = (lo + hi) / 2; if (levels[m] <= mn) lo = m + 1; else hi = m; }
                for (int k = lo; k < nl && levels[k] <= mx; k++)
                {
                    double level = levels[k];
                    double V(float f) => !float.IsFinite(f) ? 1 : (f - level == 0 ? 1e-9 : f - level);
                    double a = V(ga), b = V(gb), c = V(gc), d = V(gd);
                    int code = (a < 0 ? 1 : 0) | (b < 0 ? 2 : 0) | (c < 0 ? 4 : 0) | (d < 0 ? 8 : 0);
                    if (code == 0 || code == 15) continue;
                    // 변 위 0 자리 — 늘 작은 번호 쪽에서 셈(이웃 네모와 같은 수)
                    (double, double) X(int i0, int j0, int i1, int j1)
                    {
                        if (j1 < j0 || (j1 == j0 && i1 < i0)) { (i0, i1) = (i1, i0); (j0, j1) = (j1, j0); }
                        double f0 = V(g[j0 * W + i0]), f1 = V(g[j1 * W + i1]), t = Math.Clamp(f0 / (f0 - f1), 0, 1);
                        return (r.X0 + (i0 + 0.5 + (i1 - i0) * t) * r.Cell, r.Y0 + (j0 + 0.5 + (j1 - j0) * t) * r.Cell);
                    }
                    var segs = res[k];
                    int ci = i, cj = j;
                    (double, double) bot() => X(ci, cj, ci + 1, cj);
                    (double, double) rig() => X(ci + 1, cj, ci + 1, cj + 1);
                    (double, double) top() => X(ci, cj + 1, ci + 1, cj + 1);
                    (double, double) lef() => X(ci, cj, ci, cj + 1);
                    bool mid = (a + b + c + d) / 4 < 0;
                    switch (code)
                    {
                        case 1: case 14: segs.Add((lef(), bot())); break;
                        case 2: case 13: segs.Add((bot(), rig())); break;
                        case 3: case 12: segs.Add((lef(), rig())); break;
                        case 4: case 11: segs.Add((rig(), top())); break;
                        case 6: case 9: segs.Add((bot(), top())); break;
                        case 7: case 8: segs.Add((lef(), top())); break;
                        case 5: if (mid) { segs.Add((lef(), top())); segs.Add((bot(), rig())); } else { segs.Add((lef(), bot())); segs.Add((rig(), top())); } break;
                        case 10: if (mid) { segs.Add((lef(), bot())); segs.Add((rig(), top())); } else { segs.Add((lef(), top())); segs.Add((bot(), rig())); } break;
                    }
                }
            }
        return res;
    }

    /// <summary>사면선 · 소단선 · 옹벽선 — 절토 · 성토 · 쪽 · 단마다 꺾은선(점마다 설계 높이 · 측점).</summary>
    public static List<TierLine> TierLines(Result r)
    {
        var res = new List<TierLine>();
        if (r.W < 2 || r.H < 2 || r.D.Length != r.W * r.H || r.CutProfs.Count == 0) return res;
        // 바뀐 자리 중 가장 먼 거리 — 그 밖 꺾임은 안 그림
        double dMax = 0;
        for (int k = 0; k < r.D.Length; k++) if ((r.Kind[k] == 2 || r.Kind[k] == 3) && float.IsFinite(r.D[k])) dMax = Math.Max(dMax, r.D[k]);
        foreach (bool cut in new[] { true, false })
        {
            var profs = cut ? r.CutProfs : r.FillProfs;
            byte kindWant = (byte)(cut ? 2 : 3);
            // 꺾임 거리 → (단면, 단, 역할) — 거리 0(가장자리)은 표본에서(성토 1단 윗선 · 0단 벽)
            var at = new SortedDictionary<double, List<(int P, int K, LineRole Role)>>();
            void Put(double d, int p, int k, LineRole role)
            {
                if (d <= 1e-6 || d > dMax + 1e-6) return;
                double key = Math.Round(d, 4);
                if (!at.TryGetValue(key, out var l)) at[key] = l = new();
                l.Add((p, k, role));
            }
            for (int p = 0; p < profs.Count; p++)
            {
                double lastWall = double.NegativeInfinity;
                foreach (var f in profs[p].Faces)
                {
                    // 소단 없이 이어 쌓인 옹벽(5 cm 간격)은 한 줄 — 앞 옹벽 면과 0.3 m 안이면 건너뜀
                    if (f.Wall) { if (f.A - lastWall >= 0.3) Put(f.A, p, f.K, LineRole.Wall); lastWall = f.B; }
                    else
                    {
                        Put(f.A, p, f.K, cut ? LineRole.Toe : LineRole.Crest);
                        Put(f.B, p, f.K, cut ? LineRole.Crest : LineRole.Toe);
                    }
                }
            }
            var lvArr = at.Keys.ToArray();
            var segsAt = ContourMany(r, r.D, lvArr, r.FS, 4.0 + 2 * r.Cell);   // ★[설치 전 검토 M2] 한 번 훑기
            for (int li = 0; li < lvArr.Length; li++)
            {
                double level = lvArr[li]; var owners = at[level];
                var keep = new Dictionary<(int P, int K, LineRole Role, int Side), List<((double, double), (double, double))>>();
                bool anyWall = owners.Any(o => o.Role == LineRole.Wall), anyFace = owners.Any(o => o.Role != LineRole.Wall);
                foreach (var (a, b) in segsAt[li])
                {
                    // ★[v110.3 · V60 그림] 옹벽선 — 옹벽 위 끝은 데이라잇과 거의 겹쳐 F로 자르면 토막 난다 → 도로 쪽으로 0.3 m 들어간 자리가 바뀐 자리면 선분 통째로
                    if (anyWall)
                    {
                        // ★[설치 전 검토 L1] 선분 양 끝 모두 «도로 쪽 0.3 m 점이 바뀐 자리»일 때만 — 한 점만 보면 벽이 없는 소단까지 몇 m 그려졌다
                        (bool Ok, int Cell) Inner(double px, double py)
                        {
                            double gx = Bil(r, r.D, px + 0.25, py) - Bil(r, r.D, px - 0.25, py), gy = Bil(r, r.D, px, py + 0.25) - Bil(r, r.D, px, py - 0.25), gl = Math.Sqrt(gx * gx + gy * gy);
                            if (!double.IsFinite(gl) || gl < 1e-9) return (false, -1);
                            double qx = px - gx / gl * 0.3, qy = py - gy / gl * 0.3, fq = Bil(r, r.F, qx, qy);
                            int cq = Cell(r, qx, qy);
                            return (double.IsFinite(fq) && fq < 0 && cq >= 0 && r.Kind[cq] == kindWant, cq);
                        }
                        var ia = Inner(a.Item1, a.Item2); var ib = Inner(b.Item1, b.Item2);
                        if (ia.Ok && ib.Ok)
                        {
                            int sideW = r.FSide[ia.Cell], profW = ProfAt(r, sideW, r.FS[ia.Cell]);
                            foreach (var (p, k, role) in owners)
                                if (role == LineRole.Wall && p == profW)
                                {
                                    var key = (p, k, role, sideW);
                                    if (!keep.TryGetValue(key, out var l)) keep[key] = l = new();
                                    l.Add((a, b));
                                }
                        }
                    }
                    if (!anyFace) continue;
                    // 바뀐 자리(F < 0)에서 자름 — 선분 양 끝 F를 보간해 0 자리에서 나눔
                    double fa = Bil(r, r.F, a.Item1, a.Item2), fb = Bil(r, r.F, b.Item1, b.Item2);
                    if (!double.IsFinite(fa) || !double.IsFinite(fb)) continue;
                    var (pa, pb) = (a, b);
                    if (fa >= 0 && fb >= 0) continue;
                    if (fa >= 0 || fb >= 0)
                    {
                        double t = fa / (fa - fb);
                        var m = (a.Item1 + (b.Item1 - a.Item1) * t, a.Item2 + (b.Item2 - a.Item2) * t);
                        if (fa >= 0) pa = m; else pb = m;
                    }
                    double mx = (pa.Item1 + pb.Item1) / 2, my = (pa.Item2 + pb.Item2) / 2;
                    int c = Cell(r, mx, my);
                    if (c < 0 || r.Kind[c] != kindWant) continue;
                    int side = r.FSide[c];
                    int prof = ProfAt(r, side, r.FS[c]);
                    foreach (var (p, k, role) in owners)
                        if (p == prof && role != LineRole.Wall)
                        {
                            var key = (p, k, role, side);
                            if (!keep.TryGetValue(key, out var l)) keep[key] = l = new();
                            l.Add((pa, pb));
                        }
                }
                foreach (var kv in keep)
                    foreach (var line in Join(kv.Value.Select(q => ((Math.Round(q.Item1.Item1, 6), Math.Round(q.Item1.Item2, 6)), (Math.Round(q.Item2.Item1, 6), Math.Round(q.Item2.Item2, 6)))).ToList()))
                    {
                        if (line.Count < 2) continue;
                        var tl = new TierLine { Cut = cut, Side = kv.Key.Side, K = kv.Key.K, Role = kv.Key.Role, Prof = kv.Key.P };
                        foreach (var (x, y) in line)
                        {
                            int c = Cell(r, x, y);
                            double z = Bil(r, r.Z, x, y);
                            if (!double.IsFinite(z) && c >= 0) z = r.Z[c];
                            tl.Pts.Add((x, y, z, c >= 0 ? r.FS[c] : double.NaN));
                        }
                        res.Add(tl);
                    }
            }
        }
        // 가장자리(거리 0) — 성토 1단 윗선 · 0단 옹벽선(변형 Tier 0 · 옛 가장자리 옹벽) — 표본 차례로 이어진 것끼리
        foreach (int side in new[] { -1, 1 })
        {
            TierLine? cur = null; double lastS = double.NaN;
            foreach (var q in r.Slopes.Where(q => q.Side == side))
            {
                bool wall0 = q.Wall || (q.Rec >= 0 && r.Recs[q.Rec].Tier == 0);
                LineRole? role = wall0 && (q.Cut || q.Fill) ? LineRole.Wall : q.Fill ? LineRole.Crest : null;
                bool cont = cur != null && role == cur.Role && (wall0 ? true : !cur.Cut) && Math.Abs(q.S - lastS) <= 1.5;
                if (role == null) { cur = null; continue; }
                if (!cont)
                {
                    cur = new TierLine { Cut = !q.Fill, Side = side, K = 0, Role = role.Value, Prof = q.Rec >= 0 ? r.RecProf[q.Rec] : 0 };
                    res.Add(cur);
                }
                cur!.Pts.Add((q.X, q.Y, q.EdgeZ, q.S));
                lastS = q.S;
            }
        }
        res.RemoveAll(l => l.Pts.Count < 2);
        return res;
    }

    /// <summary>노리선 — 윗선(Crest)을 따라 <paramref name="shortSp"/>마다 · 윗선에 직각 · 아랫선 쪽으로 · 길이 = 단 평면폭(<paramref name="longSp"/>마다 전부 · 나머지 절반) ·
    /// 바뀐 자리 밖(F ≥ 0)에서 멈춤 · 부지와 같은 겹침 걷기(<see cref="SlopeHatchGenerator.RemoveOverlaps(IReadOnlyList{ValueTuple{Point3, Point3}})"/>).</summary>
    public static List<(Point3 A, Point3 B)> Ticks(Result r, List<TierLine> lines, double shortSp = 1.0, double longSp = 5.0, bool longOnly = false)
    {
        var ticks = new List<(Point3, Point3)>();
        if (shortSp <= 0) shortSp = 1.0;
        if (longSp <= 0) longSp = 5.0;
        int ratio = Math.Max(1, (int)Math.Round(longSp / shortSp));
        foreach (var l in lines)
        {
            if (l.Role != LineRole.Crest) continue;
            var profs = l.Cut ? r.CutProfs : r.FillProfs;
            if (l.Prof < 0 || l.Prof >= profs.Count) continue;
            var face = profs[l.Prof].Faces.FirstOrDefault(f => f.K == l.K);
            double w = face.B - face.A;
            if (!(w > 0.3)) continue;
            double crestD = l.Cut ? face.B : face.A, toeD = l.Cut ? face.A : face.B;
            double run = 0, next = 0; int count = 0;
            for (int i = 0; i + 1 < l.Pts.Count; i++)
            {
                var p0 = l.Pts[i]; var p1 = l.Pts[i + 1];
                double sx = p1.X - p0.X, sy = p1.Y - p0.Y, sl = Math.Sqrt(sx * sx + sy * sy);
                if (sl < 1e-9) continue;
                double tx = sx / sl, ty = sy / sl;
                while (next <= run + sl + 1e-9)
                {
                    double u = (next - run) / sl;
                    double px = p0.X + sx * u, py = p0.Y + sy * u, pz = p0.Z + (p1.Z - p0.Z) * u;
                    bool isLong = count % ratio == 0;
                    count++; next += shortSp;
                    if (longOnly && !isLong) continue;
                    // 아랫선 쪽 = D가 toeD 쪽으로 가는 방향
                    double nx = -ty, ny = tx;
                    double dp = Bil(r, r.D, px + nx * 0.3, py + ny * 0.3), dm = Bil(r, r.D, px - nx * 0.3, py - ny * 0.3);
                    if (!double.IsFinite(dp) || !double.IsFinite(dm)) continue;
                    if (Math.Abs(dp - toeD) > Math.Abs(dm - toeD)) { nx = -nx; ny = -ny; }
                    double len = isLong ? w : w / 2, go = 0, step = Math.Min(0.1, len / 4);
                    double ex = px, ey = py;
                    while (go + step <= len + 1e-9)
                    {
                        double qx = px + nx * (go + step), qy = py + ny * (go + step);
                        double f = Bil(r, r.F, qx, qy), dq = Bil(r, r.D, qx, qy);
                        if (!double.IsFinite(f) || f >= 0) break;
                        if (double.IsFinite(dq) && (crestD > toeD ? dq < toeD - 1e-3 : dq > toeD + 1e-3)) break;   // 아랫선 너머
                        go += step; ex = qx; ey = qy;
                    }
                    if (go < 0.2) continue;
                    double ez = Bil(r, r.Z, ex, ey);
                    ticks.Add((new Point3(px, py, pz), new Point3(ex, ey, double.IsFinite(ez) ? ez : pz)));
                }
                run += sl;
            }
        }
        return SlopeHatchGenerator.RemoveOverlaps(ticks);
    }

    /// <summary>점에서 가장 가까운 계단선(<paramref name="tol"/> 안) — 선 번호 · 그 자리 측점 · 거리. 없으면 null.</summary>
    public static (int Line, double S, double Dist)? NearestLine(List<TierLine> lines, double x, double y, double tol)
    {
        int best = -1; double bd = tol, bs = double.NaN;
        for (int li = 0; li < lines.Count; li++)
        {
            var pts = lines[li].Pts;
            for (int i = 0; i + 1 < pts.Count; i++)
            {
                var (d, s) = SegDist(pts[i], pts[i + 1], x, y);
                if (d < bd) { bd = d; best = li; bs = s; }
            }
        }
        return best < 0 ? null : (best, bs, bd);
    }

    static (double D, double S) SegDist((double X, double Y, double Z, double S) a, (double X, double Y, double Z, double S) b, double x, double y)
    {
        double dx = b.X - a.X, dy = b.Y - a.Y, l2 = dx * dx + dy * dy;
        double t = l2 > 0 ? Math.Clamp(((x - a.X) * dx + (y - a.Y) * dy) / l2, 0, 1) : 0;
        double qx = a.X + dx * t - x, qy = a.Y + dy * t - y;
        double s = double.IsFinite(a.S) && double.IsFinite(b.S) ? a.S + (b.S - a.S) * t : double.IsFinite(a.S) ? a.S : b.S;
        return (Math.Sqrt(qx * qx + qy * qy), s);
    }

    /// <summary>계단선 위 두 점(가장 가까운 자리로 투영) → 측점 구간 [작은 것, 큰 것]. 점이 없으면(null) 그 선 전체(점 측점의 최소 ~ 최대).</summary>
    public static (double S0, double S1) RangeOnLine(TierLine line, (double X, double Y)? p0, (double X, double Y)? p1)
    {
        var ss = line.Pts.Select(p => p.S).Where(double.IsFinite).ToList();
        if (ss.Count == 0) return (double.NaN, double.NaN);
        if (p0 == null || p1 == null) return (ss.Min(), ss.Max());
        double S(double x, double y)
        {
            double bd = double.MaxValue, bs = double.NaN;
            for (int i = 0; i + 1 < line.Pts.Count; i++) { var (d, s) = SegDist(line.Pts[i], line.Pts[i + 1], x, y); if (d < bd) { bd = d; bs = s; } }
            return bs;
        }
        double a = S(p0.Value.X, p0.Value.Y), b = S(p1.Value.X, p1.Value.Y);
        return (Math.Min(a, b), Math.Max(a, b));
    }

    /// <summary>★[v110.3.1 · JACK 1029 «사용법을 간결하게 하면서 계획지표면때 기능을 답습»] 계단선 따라 재기 — 구간지정 빨간 띠(부지 PickOnRing과 같은 꼴 · 열린 선).
    /// 매개 t = 선 첫 점부터 평면 누적 길이(m).</summary>
    public sealed class LineRuler
    {
        readonly List<(double X, double Y, double Z, double S)> _p = new();
        readonly double[] _c;
        public double Total => _c.Length == 0 ? 0 : _c[^1];
        public LineRuler(TierLine l)
        {
            double lastZ = 0;
            foreach (var q in l.Pts) if (double.IsFinite(q.Z)) { lastZ = q.Z; break; }
            foreach (var q in l.Pts)
            {
                if (!double.IsFinite(q.X) || !double.IsFinite(q.Y)) continue;
                if (double.IsFinite(q.Z)) lastZ = q.Z;
                _p.Add((q.X, q.Y, lastZ, q.S));   // 높이 없는 점은 앞 점 높이(띠만 그림)
            }
            _c = new double[_p.Count];
            for (int i = 1; i < _p.Count; i++) _c[i] = _c[i - 1] + Math.Sqrt(Sq(_p[i].X - _p[i - 1].X) + Sq(_p[i].Y - _p[i - 1].Y));
        }
        /// <summary>가장 가까운 자리의 t.</summary>
        public double ParamAt(double x, double y)
        {
            double bd = double.MaxValue, bt = 0;
            for (int i = 0; i + 1 < _p.Count; i++)
            {
                double dx = _p[i + 1].X - _p[i].X, dy = _p[i + 1].Y - _p[i].Y, l2 = dx * dx + dy * dy;
                double u = l2 > 0 ? Math.Clamp(((x - _p[i].X) * dx + (y - _p[i].Y) * dy) / l2, 0, 1) : 0;
                double d = Sq(_p[i].X + dx * u - x) + Sq(_p[i].Y + dy * u - y);
                if (d < bd) { bd = d; bt = _c[i] + (_c[i + 1] - _c[i]) * u; }
            }
            return bt;
        }
        /// <summary>t 자리 점(측점 S는 이웃 점 사이 보간 · 한쪽이 NaN이면 다른 쪽).</summary>
        public (double X, double Y, double Z, double S) At(double t)
        {
            if (_p.Count == 0) return (double.NaN, double.NaN, double.NaN, double.NaN);
            if (_p.Count == 1 || t <= 0) return _p[0];
            if (t >= Total) return _p[^1];
            int i = Array.BinarySearch(_c, t); if (i < 0) i = ~i - 1;
            i = Math.Clamp(i, 0, _p.Count - 2);
            double L = _c[i + 1] - _c[i], u = L > 0 ? (t - _c[i]) / L : 0;
            var a = _p[i]; var b = _p[i + 1];
            double s = double.IsFinite(a.S) && double.IsFinite(b.S) ? a.S + (b.S - a.S) * u : double.IsFinite(a.S) ? a.S : b.S;
            return (a.X + (b.X - a.X) * u, a.Y + (b.Y - a.Y) * u, a.Z + (b.Z - a.Z) * u, s);
        }
        /// <summary>t0 → t1 사이 길(두 끝 포함 · 어느 쪽이 커도).</summary>
        public List<(double X, double Y, double Z)> Sub(double t0, double t1)
        {
            double a = Math.Min(t0, t1), b = Math.Max(t0, t1);
            var o = new List<(double X, double Y, double Z)>();
            var pa = At(a); o.Add((pa.X, pa.Y, pa.Z));
            for (int i = 0; i < _p.Count; i++) if (_c[i] > a && _c[i] < b) o.Add((_p[i].X, _p[i].Y, _p[i].Z));
            var pb = At(b); o.Add((pb.X, pb.Y, pb.Z));
            return o;
        }
    }

    /// <summary>★[v110.3.1 · JACK 1029 «계획지표면때 기능을 답습»] [사면 변환] — 옹벽 기록 [s0, s1, 쪽, 단, 소단] 중 그 쪽 · [a, b]와 겹친 것을 사면으로 되돌린다.
    /// <paramref name="whole"/> = 겹친 기록을 통째로 지움(전체구간) · 아니면 [a, b]만 잘라 냄(구간지정 — 양끝 남는 토막은 <paramref name="minLen"/> 넘을 때만 남김 · 차례 그대로라 나중 기록이 이기는 뜻 그대로).
    /// 돌려줌 = 손댄 기록 수. 꼴 틀린 줄(5칸 미만)은 그대로 둔다. <paramref name="match"/> = 통째로 지울 기록 고르기(<see cref="WallLineMatch"/>).</summary>
    public static int CutWallRecords(List<double[]> recs, int side, double a, double b, bool whole, double minLen = 0.5, Func<double[], bool>? match = null)
    {
        double lo = Math.Min(a, b), hi = Math.Max(a, b);
        int touched = 0;
        for (int i = recs.Count - 1; i >= 0; i--)
        {
            var w = recs[i];
            if (w.Length < 5 || (int)w[2] != side) continue;
            if (whole && match != null && !match(w)) continue;   // ★[설치 전 검토 M2] 통째로 지울 때는 고른 선의 기록만(옆 단 · 옆 구간 기록은 선 끝이 칸 하나 넘쳐 겹쳐도 그대로)
            double s0 = Math.Min(w[0], w[1]), s1 = Math.Max(w[0], w[1]);
            if (!(s1 > lo + 1e-9 && s0 < hi - 1e-9)) continue;
            touched++;
            recs.RemoveAt(i);
            if (whole) continue;
            var keep = new List<double[]>();
            if (lo - s0 >= minLen) keep.Add(new[] { s0, lo, w[2], w[3], w[4] });
            if (s1 - hi >= minLen) keep.Add(new[] { hi, s1, w[2], w[3], w[4] });
            recs.InsertRange(i, keep);
        }
        return touched;
    }

    /// <summary>★[v110.3.1 · 설치 전 검토 M2] 옹벽선 → 그 선을 낸 기록 고르기 — 단면 번호 0(도로 가장자리 0단 옹벽선)이면 가장자리 옹벽 기록(0단 · 소단 0) ·
    /// 그 밖이면 그 단면의 (단, 소단) 기록(단면 번호 = 엔진이 기록 (단, 소단 1 mm 반올림)마다 하나씩 만든 것).</summary>
    public static Func<double[], bool> WallLineMatch(Result r, TierLine line)
    {
        if (line.Prof <= 0) return w => w.Length >= 5 && w[3] <= 0 && w[4] <= 0;
        for (int i = 0; i < r.Recs.Count && i < r.RecProf.Length; i++)
            if (r.RecProf[i] == line.Prof)
            {
                int tier = r.Recs[i].Tier; double bl = Math.Round(r.Recs[i].BenchL, 3);
                return w => w.Length >= 5 && !(w[3] <= 0 && w[4] <= 0) && (int)Math.Max(0, w[3]) == tier && Math.Round(Math.Max(0, w[4]), 3) == bl;
            }
        return w => w.Length >= 5 && !(w[3] <= 0 && w[4] <= 0);
    }
}
