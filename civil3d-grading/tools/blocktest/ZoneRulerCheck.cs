// ── S202 ★★[v112.7 · JACK 1104 «직각 · 사면변환 짜글짜글»] 구간 자(Ref) 위 최근접 호길이(SlopeZone.RulerParamAt · 격자 색인) = 전수 셈(GradingGeometry.ParamAt) ──
//   원인: 격자 찾기가 «다음 반경 칸을 아직 안 봤는데» 그 칸까지 본 것처럼 멈췄다(멈춤 하한이 한 칸 넓었음) → 자 위 매개가 몇 m 붙박였다가 튐 →
//   전이 띠(u = 자 위 거리 / L)의 링 거리가 계단 → 1단 · 2단 링에 0.1~0.2 m 턱 → 등고선 짜글짜글(1104 현장 재현: 링 1 턱 6곳 0.188 m · 링 2 턱 5곳).
//   만든 꼴(현장 값 없음 — 공개 저장소) · 좌표는 현장 크기(X 2e5 · Y 5.5e5)로 띄움 · 출하 함수 그대로.
//   ①자 셋(돌린 네모 고리 · 1 m 촘촘 · 바깥 단 링) × 둘레 밖 · 안 점 수천 개 — 격자 답의 거리 = 전수 답의 거리(1e-9 m)
//   ②구간 소속(ContainsAt)도 전수 매개로 가른 것과 같음
//   ③직각 · 1:1.5 → 1:0.8 · 둘레 82% · 83단(가파름 지킴이 띠를 늘려야 하는 판) — 모든 링에 턱(앞뒤는 눕고 한 변만 바깥으로 5 cm 넘게 뜀) 0
//   ★[v112.7 · 단마다 띠 — JACK 승인 1104] ④아랫단 묶음 10 m · 창 잇달아 · 구간 절반 안 ⑤곧음(구간 한가운데 변에서 링 1~20 = 구간 값 거리 · 1e-6 m)
//   ⑥모든 링 단순 · 이웃 링 안 건넘 ⑦구간 끝 이음 0 ⑧사면 띠가 옹벽으로 안 잡힘 ⑨늘릴 일 없는 구간(8단)은 종전 띠 하나
//   ⑩(설치 전 검토 중간) 자리가 need와 묶음 바닥 합 사이 — ⚠ 없음 · 한도보다 안 가파름 · 바닥 푼 판을 지어 단순 · 안 건넘
using DH.Grading.Core;

static class ZoneRulerCheck
{
    const double OX = 200000, OY = 550000;

    public static void Run(Action<string, bool, string> check)
    {
        Console.WriteLine("\n== S202 구간 자 위 최근접 호길이 — 격자 색인 = 전수 셈 · 전이 띠 링 턱 0 ==");
        // 돌린 네모 부지(34 × 47 m · 116.5° — 현장과 같은 종류의 모양 · 값은 만든 것)
        double th = 116.5 * Math.PI / 180;
        double e1x = Math.Cos(th), e1y = Math.Sin(th), e2x = -Math.Sin(th), e2y = Math.Cos(th);
        double a = 34, b = 47;
        var p0 = new Point3(OX + 120, OY + 70, 80);
        var pad = new List<Point3>
        {
            p0,
            new(p0.X + a * e1x, p0.Y + a * e1y, 80),
            new(p0.X + a * e1x + b * e2x, p0.Y + a * e1y + b * e2y, 80),
            new(p0.X + b * e2x, p0.Y + b * e2y, 80),
        };
        GradingParams P(double rise) => new()
        {
            CutBenchHeight = 5, FillBenchHeight = 5, CutBenchWidth = 1, FillBenchWidth = 1, CutSlope = 1.5, FillSlope = 1.5,
            CellSize = 0.5, MaxBenches = 50, MaxRise = rise, MaxRiseCut = rise, MaxRiseFill = 10, VertexSpacing = 2.0, MinSlope = 0.01,
            WallGateSlope = 0.05, MinFaceRun = 0.005, MiterConvex = true, MiterLimit = 2.0, ZoneBlendLength = 10,
        };
        var g = new FlatGround(1000);
        var v0 = GradingGeometry.Build(pad, g, P(400), true, null);

        // ① 자 셋 — 부지 고리(그 단 링 = 1단 클릭선의 자) · 6단 링 · 30단 링
        var rulers = new List<(string Name, List<Point3> R)> { ("1단 자(부지 링)", v0.Rings[0]) };
        if (v0.Rings.Count > 12) rulers.Add(("6단 링", v0.Rings[12]));
        if (v0.Rings.Count > 60) rulers.Add(("30단 링", v0.Rings[60]));
        var rnd = new Random(1104);
        foreach (var (name, rr) in rulers)
        {
            var z = new SlopeZone { T0 = 1, T1 = 2, Ref = rr };
            var cum = GradingGeometry.CumLen2D(rr);
            double minX = rr.Min(q => q.X), maxX = rr.Max(q => q.X), minY = rr.Min(q => q.Y), maxY = rr.Max(q => q.Y);
            double pad2 = Math.Max(maxX - minX, maxY - minY);
            int bad = 0, n = 0; double worst = 0; string at = "";
            for (int i = 0; i < 4000; i++)
            {
                double x = minX - pad2 + rnd.NextDouble() * (maxX - minX + 2 * pad2);
                double y = minY - pad2 + rnd.NextDouble() * (maxY - minY + 2 * pad2);
                double tg = z.RulerParamAt(x, y, pad, GradingGeometry.CumLen2D(pad));
                double te = GradingGeometry.ParamAt(rr, cum, x, y);
                var qg = GradingGeometry.PointAtParam(rr, cum, tg); var qe = GradingGeometry.PointAtParam(rr, cum, te);
                double dg = Math.Sqrt((qg.X - x) * (qg.X - x) + (qg.Y - y) * (qg.Y - y));
                double de = Math.Sqrt((qe.X - x) * (qe.X - x) + (qe.Y - y) * (qe.Y - y));
                n++;
                if (dg - de > 1e-9) { bad++; if (dg - de > worst) { worst = dg - de; at = $"({x - OX:F2},{y - OY:F2}) 격자 {tg:F3} · 전수 {te:F3}"; } }
            }
            check($"S202 ①{name}({rr.Count}점) — 격자 답의 거리 = 전수 답의 거리", bad == 0, $"어긋남 {bad}/{n} · 최대 {worst:F3} m {at}");
        }

        // ② 구간 소속 — 둘레 82% 구간(T0 = 모서리 지나 0.9 m · T1 = 10.4 m)을 전수 매개로 가른 것과 같나
        var ruler = v0.Rings[0];
        var rcum = GradingGeometry.CumLen2D(ruler);
        double tot = rcum[^1];
        var zone = new SlopeZone { T0 = a + 0.9, T1 = 10.4, Ref = ruler };
        zone.Rules.Add((0, 0.8, 1.0)); zone.Normalize();
        {
            // 짧은 구간(6 m)을 자 위로 3 m씩 옮기며 — 구간 끝이 어디에 떨어져도
            int diff = 0, n = 0;
            var cumP = GradingGeometry.CumLen2D(pad);
            for (double t0 = 0; t0 < tot; t0 += 3)
            {
                var zz = new SlopeZone { T0 = t0, T1 = (t0 + 6) % tot, Ref = ruler };
                zz.Rules.Add((0, 0.8, 1.0)); zz.Normalize();
                for (int k = 1; k < Math.Min(v0.Rings.Count, 40); k += 2)
                    foreach (var q in v0.Rings[k])
                    {
                        n++;
                        if (zz.ContainsAt(q.X, q.Y, pad, cumP) != zz.Contains(GradingGeometry.ParamAt(ruler, rcum, q.X, q.Y))) diff++;
                    }
            }
            check("S202 ②구간 소속(ContainsAt) = 전수 매개로 가른 소속(6 m 구간 × 3 m씩 · 링 1~39)", diff == 0, $"다름 {diff}/{n}");
        }

        // ③ 현장 종류 재현 — 직각 · 1:1.5 → 1:0.8 · 둘레 82% · 83단
        var zs = new List<SlopeZone> { zone };
        var v = GradingGeometry.Build(pad, g, P(400), true, zs);
        string diag = GradingGeometry.LastDiag;
        foreach (var l in diag.Split('\n')) if (l.Contains("전이 띠 —")) Console.WriteLine("      " + l.Trim());
        // ★[v112.7 · 단마다 띠] 같은 입력의 띠(기하가 쓰는 것과 같은 생성자) — 끝마다 묶음 · 창
        var prof = StepProfile.Build(P(400), 1.5, 5, 1, true);
        var bl = new ZoneBlend(zs, pad, GradingGeometry.CumLen2D(pad), P(400), 1.5, 5, 1, true, prof);
        var ends = bl.Ends.Where(x => x.Any).ToList();
        check("S202 ③준비 — 가파름 지킴이 띠를 늘려야 하는 판(현장과 같은 갈래) · 두 끝 다 묶음 띠 · ⚠ 없음", v.BlendBands == 2 && ends.Count == 2 && ends.All(x => x.Staged && !x.Warn) && diag.Contains("단마다 띠"),
              $"띠 {v.BlendBands} · 묶음 {string.Join("/", ends.Select(x => x.Staged))} · ⚠ {string.Join("/", ends.Select(x => x.Warn))} · 링 {v.Rings.Count}");
        double DPad(double x, double y)
        {
            double best = double.MaxValue;
            for (int i = 0; i < pad.Count; i++)
            {
                var s = pad[i]; var t = pad[(i + 1) % pad.Count];
                double ex = t.X - s.X, ey = t.Y - s.Y, l2 = ex * ex + ey * ey;
                double u = Math.Clamp(((x - s.X) * ex + (y - s.Y) * ey) / l2, 0, 1);
                double px = s.X + ex * u - x, py = s.Y + ey * u - y;
                best = Math.Min(best, Math.Sqrt(px * px + py * py));
            }
            return best;
        }
        // 턱 = 앞뒤는 눕고(부지 거리 변화 / 변 길이 < 0.5) 그 한 변만 거의 바깥(법선)으로 5 cm 넘게 뛴 자리 — 붙박였다 튄 계단.
        //   띠 안의 가파른 비탈 링(아랫단 묶음이 10 m 안에 몇 m씩 섞여 연이어 비스듬함)은 앞뒤도 같이 가팔라 턱이 아니다 · 같은 자리 겹친 점은 빼고 잰다.
        int steps = 0; double stepMax = 0; string stepAt = "";
        for (int k = 1; k < v.Rings.Count; k++)
        {
            var r = new List<Point3>();
            foreach (var q in v.Rings[k]) if (r.Count == 0 || Math.Abs(r[^1].X - q.X) + Math.Abs(r[^1].Y - q.Y) > 1e-6) r.Add(q);
            int n = r.Count;
            var ds = r.Select(q => DPad(q.X, q.Y)).ToArray();
            var rat = new double[n];
            for (int i = 0; i < n; i++)
            {
                var q = r[i]; var q2 = r[(i + 1) % n];
                double seg = Math.Sqrt((q2.X - q.X) * (q2.X - q.X) + (q2.Y - q.Y) * (q2.Y - q.Y));
                rat[i] = seg > 1e-9 ? Math.Abs(ds[(i + 1) % n] - ds[i]) / seg : 0;
            }
            for (int i = 0; i < n; i++)
            {
                double dd = Math.Abs(ds[(i + 1) % n] - ds[i]);
                if (dd > 0.05 && rat[i] > 0.9 && rat[(i - 1 + n) % n] < 0.5 && rat[(i + 1) % n] < 0.5)
                {
                    steps++;
                    if (dd > stepMax) { stepMax = dd; stepAt = $"링 {k} ({r[i].X - OX:F2},{r[i].Y - OY:F2})"; }
                }
            }
        }
        check("S202 ③모든 링에 턱 0(전이 띠 거리가 자 위 매개를 따라 이어짐)", steps == 0, $"턱 {steps}곳 · 최대 {stepMax:F3} m {stepAt}");

        // ★[v112.7 · JACK 1104 17:51] 자의 볼록 모퉁이 자리(E에서 안쪽 거리) — 창 끝 맞춤 검사(④ · ⑪)가 쓴다(ZoneBlend와 같은 꺾임 문턱 0.5°)
        List<double> CornersIn(ZoneBlend.End en, SlopeZone zz)
        {
            var (rp, rcm) = zz.Ruler(pad, GradingGeometry.CumLen2D(pad));
            int m = rcm.Length - 1; double sa = 0; var res = new List<double>();
            for (int i = 0; i < m; i++) sa += rp[i].X * rp[(i + 1) % m].Y - rp[(i + 1) % m].X * rp[i].Y;
            for (int i = 0; i < m; i++)
            {
                var pv = rp[(i - 1 + m) % m]; var pc = rp[i]; var nx = rp[(i + 1) % m];
                double ax = pc.X - pv.X, ay = pc.Y - pv.Y, bx = nx.X - pc.X, by = nx.Y - pc.Y;
                double lA = Math.Sqrt(ax * ax + ay * ay), lB = Math.Sqrt(bx * bx + by * by);
                if (lA < 1e-9 || lB < 1e-9 || (ax * bx + ay * by) / (lA * lB) >= Math.Cos(0.5 * Math.PI / 180)) continue;
                double cr = ax * by - ay * bx;
                if (!(sa > 0 ? cr > 0 : cr < 0)) continue;
                double d = en.AtT0 ? rcm[i] - en.E : en.E - rcm[i];
                d %= en.Total; if (d < 0) d += en.Total;
                res.Add(d);
            }
            res.Sort();
            return res;
        }

        // ④ 단마다 띠 길이 — 아랫단 묶음 = 10 m(설정 그대로 — 다음 묶음이 모퉁이 앞에 한 단도 못 들면 그 모퉁이까지 늘 수 있다) · 창은 잇달아 · 모든 창이 구간 절반 안
        {
            bool ok = ends.Count == 2;
            string txt = "";
            foreach (var en in ends)
            {
                if (!en.Staged) { ok = false; continue; }
                double half = en.Avail / 2;
                var eb = Enumerable.Range(0, en.Elig.Length).Where(b => en.Elig[b]).ToList();
                double end0 = en.Ob[eb[0]] + en.Lb[eb[0]];
                bool firstOk = Math.Abs(en.Lb[eb[0]] - 10) < 1e-12 || (en.Lb[eb[0]] > 10 && CornersIn(en, zone).Exists(c => Math.Abs(c - end0) < 1e-9));
                ok &= !en.Warn && firstOk && Math.Abs(en.Ob[eb[0]] - en.O) < 1e-12;
                for (int q = 1; q < eb.Count; q++)
                    ok &= en.Ob[eb[q]] >= en.Ob[eb[q - 1]] - 1e-12 && en.Ob[eb[q]] + en.Lb[eb[q]] >= en.Ob[eb[q - 1]] + en.Lb[eb[q - 1]] - 1e-12;
                ok &= en.Ob[eb[^1]] + en.Lb[eb[^1]] <= half + 1e-9 && Math.Abs(en.O + en.L - (en.Ob[eb[^1]] + en.Lb[eb[^1]])) < 1e-9;
                int first10 = eb.Count(b => en.Ob[b] + en.Lb[b] <= 10 + 1e-9);
                txt += $" [{(en.AtT0 ? "T0" : "T1")} 10 m 안에 끝나는 단 {first10}/{eb.Count} · 1단 {en.Lb[eb[0]]:0.##} m · 끝 단 {en.Ob[eb[^1]]:0.##}~{en.Ob[eb[^1]] + en.Lb[eb[^1]]:0.##} m · 절반 {half:0.##} m]";
            }
            check("S202 ④단마다 띠 — 아랫단 10 m(모퉁이에 맞추면 그 모퉁이까지) · 창 잇달아 · 구간 절반 안", ok, txt);
        }

        // ⑤ 곧음 — 바뀐 구간 한가운데 변(p2→p3 · 두 끝에서 46 m 넘게 안)에서 아랫단 링 1~20(1~10단)은 «전부 1:0.8로 지은 판»의 같은 링과 같은 선 위
        //   (버퍼 자체가 이 좌표에서 0.4 mm쯤 안쪽 · 0.1 mm 기운다 — 그 판도 똑같다 · 그래서 바라는 거리 대신 그 판의 선과 견준다)
        {
            var s0 = pad[2]; var s1 = pad[3];
            double sx = s1.X - s0.X, sy = s1.Y - s0.Y, sl = Math.Sqrt(sx * sx + sy * sy);
            var pRef = new GradingParams
            {
                CutBenchHeight = 5, FillBenchHeight = 5, CutBenchWidth = 1, FillBenchWidth = 1, CutSlope = 0.8, FillSlope = 1.5,
                CellSize = 0.5, MaxBenches = 50, MaxRise = 400, MaxRiseCut = 400, MaxRiseFill = 10, VertexSpacing = 2.0, MinSlope = 0.01,
                WallGateSlope = 0.05, MinFaceRun = 0.005, MiterConvex = true, MiterLimit = 2.0, ZoneBlendLength = 10,
            };
            var vRef = GradingGeometry.Build(pad, g, pRef, true, null);
            List<(double T, double Off)> Side(List<Point3> r, double lim)
            {
                var res = new List<(double, double)>();
                foreach (var q in r)
                {
                    double t = ((q.X - s0.X) * sx + (q.Y - s0.Y) * sy) / sl;
                    if (t < 2 || t > sl - 2) continue;
                    double off = ((q.X - s0.X) * sy - (q.Y - s0.Y) * sx) / sl;   // 반시계 부지 — 바깥 = 진행의 오른쪽
                    if (off < 0 || off > lim) continue;
                    res.Add((t, off));
                }
                return res;
            }
            double maxOff = 0, maxTilt = 0; int cnt = 0; string at = "";
            for (int k = 1; k <= 20 && k < v.Rings.Count && k < vRef.Rings.Count; k++)
            {
                var rf = Side(vRef.Rings[k], 200);
                if (rf.Count < 2) { cnt = -1; break; }
                // 그 판의 선(최소제곱 직선) — off = α + β·t
                double mt = rf.Average(x => x.T), mo = rf.Average(x => x.Off);
                double beta = rf.Sum(x => (x.T - mt) * (x.Off - mo)) / rf.Sum(x => (x.T - mt) * (x.T - mt)), alpha = mo - beta * mt;
                foreach (var (t, off) in Side(v.Rings[k], mo + 30))
                {
                    cnt++;
                    double dv = Math.Abs(off - (alpha + beta * t));
                    if (dv > maxOff) { maxOff = dv; at = $"링 {k} 그 판 {alpha + beta * t:0.####} m · 실제 {off:0.####} m"; }
                }
                // 이 판 링 자신의 기울기(선 맞춤)
                var mine = Side(v.Rings[k], mo + 30);
                if (mine.Count >= 2)
                {
                    double mt2 = mine.Average(x => x.T), mo2 = mine.Average(x => x.Off);
                    double b2 = mine.Sum(x => (x.T - mt2) * (x.Off - mo2)) / mine.Sum(x => (x.T - mt2) * (x.T - mt2));
                    maxTilt = Math.Max(maxTilt, Math.Abs(Math.Atan(b2) - Math.Atan(beta)) * 180 / Math.PI);
                }
            }
            check("S202 ⑤곧음 — 구간 한가운데 변에서 링 1~20 = 전부 1:0.8로 지은 판의 선(어긋남 1e-6 m · 기울기 차 0.001° 안)", cnt > 0 && maxOff < 1e-6 && maxTilt < 1e-3,
                  $"점 {cnt} · 최대 어긋남 {maxOff:0.#########} m · 기울기 차 최대 {maxTilt:0.#####}° {at}");
        }

        // ⑥ 안 건넘 · 안 접힘 — 모든 링 단순 · 이웃 링끼리 안 만남
        {
            var gf = new NetTopologySuite.Geometries.GeometryFactory();
            NetTopologySuite.Geometries.LineString Line(List<Point3> r)
            {
                var cs = new List<NetTopologySuite.Geometries.Coordinate>();
                foreach (var q in r)
                    if (cs.Count == 0 || Math.Abs(cs[^1].X - q.X) > 1e-9 || Math.Abs(cs[^1].Y - q.Y) > 1e-9) cs.Add(new NetTopologySuite.Geometries.Coordinate(q.X, q.Y));
                if (cs.Count > 1 && (Math.Abs(cs[0].X - cs[^1].X) > 1e-9 || Math.Abs(cs[0].Y - cs[^1].Y) > 1e-9)) cs.Add(cs[0].Copy());
                return gf.CreateLineString(cs.ToArray());
            }
            var lines = v.Rings.Skip(1).Select(Line).ToList();
            int notSimple = lines.Count(l => !l.IsSimple), cross = 0;
            for (int k = 0; k + 1 < lines.Count; k++) if (lines[k].Intersects(lines[k + 1])) cross++;
            check("S202 ⑥링 단순 · 이웃 링끼리 안 건넘(모든 링)", notSimple == 0 && cross == 0, $"안 단순 {notSimple} · 건넘 {cross} / 링 {lines.Count}");
        }

        // ⑦ 구간 끝 이음 0 · ⑧ 사면 띠가 옹벽으로 안 잡힘
        check("S202 ⑦구간 끝 이음 0(그 밖 · 옹벽 단 탓)", v.JumpsOther == 0 && v.JumpsWall == 0, $"그 밖 {v.JumpsOther} · 옹벽 단 탓 {v.JumpsWall}");
        {
            WallRunBuilder.Build(pad, v.Rings, zs, true, 1.5, 0.01);
            int extra = WallRunBuilder.LastExtraWall;
            check("S202 ⑧사면 띠가 옹벽으로 안 잡힘(LastExtraWall 0)", extra == 0, $"구간 밖인데 벽 {extra}");
        }

        // ⑨ 늘릴 일이 없는 구간(8단 · 띠 10 m로 가파름 지킴)은 묶음 띠를 안 쓴다(종전 길 그대로)
        {
            var bl8 = new ZoneBlend(zs, pad, GradingGeometry.CumLen2D(pad), P(40), 1.5, 5, 1, true, StepProfile.Build(P(40), 1.5, 5, 1, true));
            var e8 = bl8.Ends.Where(x => x.Any).ToList();
            check("S202 ⑨늘릴 일 없는 구간(8단)은 종전 띠 하나(묶음 아님 · 10 m)", e8.Count == 2 && e8.All(x => !x.Staged && Math.Abs(x.L - 10) < 1e-12),
                  string.Join(" ", e8.Select(x => $"[{(x.AtT0 ? "T0" : "T1")} 묶음 {x.Staged} · 띠 {x.L:0.##} m]")));
        }

        // ⑩ ★[설치 전 검토 중간 · 1104] 구간 절반(자리)이 «띠 하나 늘림 길이(need ≈ 57 m)»와 «묶음마다 10 m 바닥 합(70 m)» 사이인 판 —
        //   종전 늘림은 ⚠ 없이 한도를 지킨다 → 묶음 띠도 ⚠ 없음 · 가장 가파른 면이 한도(1:0.8 면 · 옹벽 판정 한도 3배 = 1:0.1605) 아래로 안 내려감
        //   (바닥을 윗 묶음부터 풀어 맞추거나, 못 맞추면 종전 늘림으로) · 바닥을 푼 묶음 판이 실제로 있었나 · 그 판을 지어 링 단순 · 안 건넘
        {
            double lim3 = 3 * (0.05 * 5 * 1.05 + 5e-3) / 5;      // 1:0.8 면(폭 4 m)의 한도 = min(3·lim, 0.8·fr)/h
            int nCase = 0, nWarn = 0, nSteep = 0, nRelax = 0, nOld = 0; string worst = ""; double worstR = double.MaxValue;
            double relaxSpan = double.NaN;
            // 자리(구간/2) 58~69 m — need(57.3 m)보다 넓고 바닥 합(13단씩 10 m × 7 = 70 m)보다 좁다(그보다 짧은 구간은 종전 늘림도 ⚠ — 코드가 «더 가파르지 않음»을 다짐)
            for (double span = 116; span <= 138 + 1e-9; span += 2)
            {
                var zq = new SlopeZone { T0 = a + 0.9, T1 = (a + 0.9 + span) % tot, Ref = ruler };
                zq.Rules.Add((0, 0.8, 1.0)); zq.Normalize();
                var blq = new ZoneBlend(new List<SlopeZone> { zq }, pad, GradingGeometry.CumLen2D(pad), P(400), 1.5, 5, 1, true, prof);
                foreach (var en in blq.Ends.Where(x => x.Any))
                {
                    nCase++;
                    if (en.Warn) nWarn++;
                    if (en.Steepest < lim3 - 1e-9) nSteep++;
                    if (en.Steepest < worstR) { worstR = en.Steepest; worst = $"구간 {span} m {(en.AtT0 ? "T0" : "T1")} 1:{en.Steepest:0.####}({en.SteepBench + 1}단 · 묶음 {en.Staged})"; }
                    if (!en.Staged) nOld++;
                    else
                    {
                        var eb = Enumerable.Range(0, en.Elig.Length).Where(b => en.Elig[b]).ToList();
                        bool relaxed = eb.Take(eb.Count - 1).Any(b => en.Lb[b] < 10 - 1e-9);   // 마지막 묶음 말고도 10 m보다 짧은 창
                        if (relaxed) { nRelax++; if (double.IsNaN(relaxSpan)) relaxSpan = span; }
                    }
                }
            }
            check("S202 ⑩자리가 need와 바닥 합 사이(구간 116~138 m) — ⚠ 없음 · 한도보다 안 가파름 · 바닥 푼 묶음 판 있음",
                  nCase > 0 && nWarn == 0 && nSteep == 0 && nRelax > 0,
                  $"끝 {nCase} · ⚠ {nWarn} · 한도 넘음 {nSteep} · 바닥 푼 묶음 {nRelax} · 종전 늘림 {nOld} · 가장 가파름 {worst}");
            if (!double.IsNaN(relaxSpan))
            {
                var zq = new SlopeZone { T0 = a + 0.9, T1 = (a + 0.9 + relaxSpan) % tot, Ref = ruler };
                zq.Rules.Add((0, 0.8, 1.0)); zq.Normalize();
                var vq = GradingGeometry.Build(pad, g, P(400), true, new List<SlopeZone> { zq });
                var gf2 = new NetTopologySuite.Geometries.GeometryFactory();
                NetTopologySuite.Geometries.LineString L2(List<Point3> r)
                {
                    var cs = new List<NetTopologySuite.Geometries.Coordinate>();
                    foreach (var q in r)
                        if (cs.Count == 0 || Math.Abs(cs[^1].X - q.X) > 1e-9 || Math.Abs(cs[^1].Y - q.Y) > 1e-9) cs.Add(new NetTopologySuite.Geometries.Coordinate(q.X, q.Y));
                    if (cs.Count > 1 && (Math.Abs(cs[0].X - cs[^1].X) > 1e-9 || Math.Abs(cs[0].Y - cs[^1].Y) > 1e-9)) cs.Add(cs[0].Copy());
                    return gf2.CreateLineString(cs.ToArray());
                }
                var ls = vq.Rings.Skip(1).Select(L2).ToList();
                int ns = ls.Count(l => !l.IsSimple), cr = 0;
                for (int k = 0; k + 1 < ls.Count; k++) if (ls[k].Intersects(ls[k + 1])) cr++;
                WallRunBuilder.Build(pad, vq.Rings, new List<SlopeZone> { zq }, true, 1.5, 0.01);
                check($"S202 ⑩' 바닥 푼 묶음 판(구간 {relaxSpan} m)을 지어 — 링 단순 · 안 건넘 · 이음 0 · 옹벽 오판 0",
                      ns == 0 && cr == 0 && vq.JumpsOther == 0 && vq.JumpsWall == 0 && WallRunBuilder.LastExtraWall == 0,
                      $"안 단순 {ns} · 건넘 {cr} · 이음 {vq.JumpsOther}/{vq.JumpsWall} · 옹벽 오판 {WallRunBuilder.LastExtraWall}");
            }
        }

        // ⑪ ★[v112.7 · JACK 1104 17:51 «아직도 미세하게 짜글거려»] 띠 안에 자의 볼록 모퉁이가 있는 판 — 창이 모퉁이를 걸치지 않고(모퉁이에 창 끝)
        //   링에 «Z» 턱(1.5 m 길 안에서 10° 넘게 반대로 두 번 꺾임) 0. 재현: T0에서 9.6 m 안에 모퉁이 p2(τ 81) — 1묶음 10 m 창이 0.4 m 넘어 걸치던 자리.
        {
            var z11 = new SlopeZone { T0 = 81 - 9.6, T1 = (81 - 9.6 + 125) % tot, Ref = ruler };
            z11.Rules.Add((0, 0.8, 1.0)); z11.Normalize();
            var zs11 = new List<SlopeZone> { z11 };
            var bl11 = new ZoneBlend(zs11, pad, GradingGeometry.CumLen2D(pad), P(400), 1.5, 5, 1, true, prof);
            int straddle = 0, staged11 = 0; string stTxt = "";
            foreach (var en in bl11.Ends.Where(x => x.Any))
            {
                if (!en.Staged) continue;
                staged11++;
                var cs = CornersIn(en, z11);
                var eb = Enumerable.Range(0, en.Elig.Length).Where(b => en.Elig[b]).ToList();
                foreach (int bb in eb)
                    foreach (double c in cs)
                        if (c > en.Ob[bb] + 1e-9 && c < en.Ob[bb] + en.Lb[bb] - 1e-9) { straddle++; if (stTxt.Length == 0) stTxt = $"{(en.AtT0 ? "T0" : "T1")} {bb + 1}단 창 {en.Ob[bb]:0.##}~{en.Ob[bb] + en.Lb[bb]:0.##} m · 모퉁이 {c:0.##} m"; }
            }
            var v11 = GradingGeometry.Build(pad, g, P(400), true, zs11);
            int zjog = 0; string zAt = "";
            for (int k = 1; k <= 40 && k < v11.Rings.Count; k++)
            {
                var r = new List<Point3>();
                foreach (var q in v11.Rings[k]) if (r.Count == 0 || Math.Abs(r[^1].X - q.X) + Math.Abs(r[^1].Y - q.Y) > 0.05) r.Add(q);
                while (r.Count > 3 && Math.Abs(r[^1].X - r[0].X) + Math.Abs(r[^1].Y - r[0].Y) <= 0.05) r.RemoveAt(r.Count - 1);   // 닫는 점(처음과 겹침)
                int n = r.Count;
                var turn = new double[n]; var segL = new double[n];
                for (int i = 0; i < n; i++)
                {
                    var q0 = r[(i - 1 + n) % n]; var q1 = r[i]; var q2 = r[(i + 1) % n];
                    turn[i] = Math.Atan2((q1.X - q0.X) * (q2.Y - q1.Y) - (q1.Y - q0.Y) * (q2.X - q1.X), (q1.X - q0.X) * (q2.X - q1.X) + (q1.Y - q0.Y) * (q2.Y - q1.Y)) * 180 / Math.PI;
                    segL[i] = Math.Sqrt((q2.X - q1.X) * (q2.X - q1.X) + (q2.Y - q1.Y) * (q2.Y - q1.Y));
                }
                for (int i = 0; i < n; i++)
                {
                    if (Math.Abs(turn[i]) <= 10) continue;
                    double walk = 0;
                    for (int s = 1; s < n && walk < 1.5; s++)
                    {
                        walk += segL[(i + s - 1) % n];
                        int j = (i + s) % n;
                        if (walk >= 1.5) break;
                        if (Math.Abs(turn[j]) > 10 && Math.Sign(turn[j]) != Math.Sign(turn[i])) { zjog++; if (zAt.Length == 0) zAt = $"링 {k} ({r[i].X - OX:F1},{r[i].Y - OY:F1}) {turn[i]:0}° → {turn[j]:0}°"; break; }
                    }
                }
            }
            check("S202 ⑪띠 안 볼록 모퉁이 — 창이 모퉁이를 안 걸침 · 링 1~40에 «Z» 턱 0", staged11 > 0 && straddle == 0 && zjog == 0,
                  $"묶음 끝 {staged11} · 걸친 창 {straddle} {stTxt} · Z 턱 {zjog} {zAt}");
        }
    }
}
