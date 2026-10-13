// ── S154 ★★★[v103.1.4 · JACK 1008 «재생성이 좀 오래 걸리는데 시간을 줄일 방법 없어?» → «옹벽을 값 그대로 되돌릴 때 재생성 생략»] ──
//   «다시 지어도 똑같은 정지면이 나오나»를 가리는 견주기(GradeSame — CreateGradingCommand.SameAsLastGrade가 부르는 것과 같은 함수).
//   틀리게 «같다»고 하면 낡은 정지면이 말없이 남는다 — 제원은 <b>칸 하나하나</b>를 바꿔 보고 전부 잡히는지 잰다(칸이 늘어도 이 검사가 따라간다).
using System.Collections;
using System.Reflection;
using DH.Grading.Core;

static class GradeSameCheck
{
    public static void Run(Action<string, bool, string> check0)
    {
        void check(string name, bool ok, string detail = "") => check0(name, ok, detail);
        Console.WriteLine("\n== S154 다시 지어도 같은가 — 제원 · 구간 견주기(GradeSame) ==");

        static GradingParams Make() => new()
        {
            CutBenchHeight = 5, FillBenchHeight = 4, CutBenchWidth = 1, FillBenchWidth = 1.5,
            CutSlope = 1.5, FillSlope = 1.8, CellSize = 1.0, MaxBenches = 18, MaxRise = 55.2, MaxRiseCut = 55.2, MaxRiseFill = 88.0,
            VertexSpacing = 2.0, MinSlope = 0.01, MinFaceRun = 0.005, MiterConvex = true, MiterLimit = 2.0,
            CutBenchSteps = { (2, 3.0) }, FillBenchSteps = { (1, 2.5), (4, 6.0) },
        };

        // ① 값이 같은 두 벌(객체 · 목록은 따로) → 같다
        string same = GradeSame.ParamsDiff(Make(), Make());
        check("S154 제원 — 값이 같은 두 벌(객체 · 목록은 따로 만든 것)은 «같다»", same.Length == 0, same);

        // ② 쓸 수 있는 칸을 하나씩 바꿔 본다 — 전부 «다르다»로 잡혀야 한다(그 칸 이름으로)
        int tried = 0, caught = 0; var missed = new List<string>(); var skipped = new List<string>();
        foreach (var pi in typeof(GradingParams).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!pi.CanWrite || pi.GetIndexParameters().Length > 0) continue;
            var b = Make();
            object? cur = pi.GetValue(b); object? nv;
            var t = pi.PropertyType;
            if (t == typeof(double)) nv = (double)cur! + 0.125;
            else if (t == typeof(int)) nv = (int)cur! + 1;
            else if (t == typeof(bool)) nv = !(bool)cur!;
            else if (t == typeof(string)) nv = (string?)cur + "x";
            else if (t.IsEnum) { var vals = Enum.GetValues(t); nv = vals.Length > 1 ? vals.Cast<object>().First(v => !v.Equals(cur)) : null; if (nv == null) { skipped.Add(pi.Name); continue; } }
            else if (typeof(IList).IsAssignableFrom(t) && t.IsGenericType)
            {
                var l = (IList)Activator.CreateInstance(t)!;
                if (cur is IEnumerable e) foreach (var o in e) l.Add(o);
                l.Add(Activator.CreateInstance(t.GetGenericArguments()[0]));
                nv = l;
            }
            else { skipped.Add(pi.Name + "(" + t.Name + ")"); continue; }
            try { pi.SetValue(b, nv); } catch { skipped.Add(pi.Name + "(못 씀)"); continue; }
            tried++;
            string d = GradeSame.ParamsDiff(Make(), b);
            if (d.Length > 0) caught++; else missed.Add(pi.Name);
        }
        check("S154 제원 — 쓸 수 있는 칸을 하나씩 바꾸면 전부 «다르다»(칸 20개 넘게 · 놓친 칸 0 · 못 바꿔 본 칸 0)",
              tried >= 20 && caught == tried && missed.Count == 0 && skipped.Count == 0,
              $"바꿔 본 칸 {tried} · 잡은 칸 {caught}" + (missed.Count > 0 ? " · 놓침: " + string.Join(",", missed) : "") + (skipped.Count > 0 ? " · 못 바꿔 봄: " + string.Join(",", skipped) : ""));

        // ③ 단높이 규칙 — 같은 값 다른 목록 = 같다 · 값 하나 · 차례 · 개수가 다르면 다르다
        var s1 = Make(); var s2 = Make();
        typeof(GradingParams).GetProperty("FillBenchSteps")!.SetValue(s2, new List<(int, double)> { (1, 2.5), (4, 6.5) });
        var s3 = Make();
        typeof(GradingParams).GetProperty("FillBenchSteps")!.SetValue(s3, new List<(int, double)> { (4, 6.0), (1, 2.5) });
        var s4 = Make();
        typeof(GradingParams).GetProperty("CutBenchSteps")!.SetValue(s4, new List<(int, double)>());
        check("S154 제원 — 단높이 규칙: 값 하나(6.0 → 6.5) · 차례 · 개수가 다르면 «다르다»",
              GradeSame.ParamsDiff(s1, s2).Contains("FillBenchSteps") && GradeSame.ParamsDiff(s1, s3).Contains("FillBenchSteps") && GradeSame.ParamsDiff(s1, s4).Contains("CutBenchSteps"),
              $"{GradeSame.ParamsDiff(s1, s2)} / {GradeSame.ParamsDiff(s1, s3)} / {GradeSame.ParamsDiff(s1, s4)}");

        // ④ 값 견주기 — ★[검토 1012 낮음] 실수는 <b>비트까지</b> 같아야 같다(옮겨 적은 값끼리라 어긋날 까닭이 없다 · 허용 폭을 두면 그만큼은 모르고 넘긴다)
        double oneUlp = BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(1.5) + 1);
        check("S154 값 — 실수는 비트까지: 마지막 자리 하나(2.2e-16)만 달라도 다르다 · 같은 값은 같다 · 값 없음(NaN)끼리는 같다 · 묶음 속 실수도 마찬가지 · 정수 5와 실수 5.0은 다르다 · 없음끼리는 같다",
              !GradeSame.ValueSame(1.5, oneUlp) && !GradeSame.ValueSame(1.5, 1.5 + 1e-13) && GradeSame.ValueSame(1.5, 3.0 / 2.0) && GradeSame.ValueSame(double.NaN, double.NaN)
              && !GradeSame.ValueSame((2, 1.5), (2, oneUlp)) && GradeSame.ValueSame((2, 1.5), (2, 1.5)) && !GradeSame.ValueSame((2, 1.5), (3, 1.5))
              && !GradeSame.ValueSame(new List<(int, double)> { (1, 1.5) }, new List<(int, double)> { (1, oneUlp) })
              && !GradeSame.ValueSame(5, 5.0) && GradeSame.ValueSame(null, null) && !GradeSame.ValueSame(null, 1.0),
              $"1.5의 다음 실수 = 1.5 + {oneUlp - 1.5:E2}");

        // ⑤ 구간 — 현장 크기 좌표의 링을 자로
        var ringA = new List<Point3>(); var ringB = new List<Point3>(); var ringC = new List<Point3>();
        for (int i = 0; i < 280; i++)
        {
            double a = i * Math.PI * 2 / 280, x = 210289.2 + 45 * Math.Cos(a), y = 509835.6 + 60 * Math.Sin(a);
            ringA.Add(new Point3(x, y, 105)); ringB.Add(new Point3(x, y, 105)); ringC.Add(new Point3(x + (i == 137 ? 0.001 : 0), y, 105));
        }
        SlopeZone Z(double t0, double t1, List<Point3>? r, params (int, double, double)[] rules)
        { var z = new SlopeZone { T0 = t0, T1 = t1, Ref = r }; foreach (var q in rules) z.Rules.Add(q); z.Normalize(); return z; }
        List<SlopeZone> L1(List<Point3>? r) => new() { Z(270.74, 44.54, r, (0, 1.2, 1.0)), Z(70.04, 122.24, r, (0, 1.0, 1.0), (2, 1.5, 1.0)) };
        var baseL = L1(ringA);
        check("S154 구간 — 값이 같은 두 벌(자는 좌표가 같은 다른 객체)은 «같다» · 규칙 없는 구간은 안 센다",
              GradeSame.ZonesDiff(baseL, L1(ringB)).Length == 0
              && GradeSame.ZonesDiff(baseL, new List<SlopeZone>(L1(ringB)) { new SlopeZone { T0 = 1, T1 = 2 } }).Length == 0
              && GradeSame.ZonesDiff(null, new List<SlopeZone>()).Length == 0, GradeSame.ZonesDiff(baseL, L1(ringB)));
        var dT = L1(ringA); dT[0].T1 += 1e-6;
        var dS = L1(ringA); dS[0].Rules[0] = (0, 1.25, 1.0);
        var dW = L1(ringA); dW[1].Rules[1] = (2, 1.5, 1.2);
        var dF = L1(ringA); dF[1].Rules[1] = (3, 1.5, 1.0);
        var dN = L1(ringA); dN.RemoveAt(1);
        var dO = L1(ringA); dO.Reverse();
        var dR0 = L1(null);
        var dR1 = L1(ringC);
        var dR2 = L1(ringA.Take(279).ToList());
        // ★[검토 1012 낮음] 규칙 · 자는 비트까지 — 구배 1e-13 · 소단 1e-13 · 자 한 점 1nm도 «다르다»
        var dS2 = L1(ringA); dS2[0].Rules[0] = (0, 1.2 + 1e-13, 1.0);
        var dW2 = L1(ringA); dW2[1].Rules[1] = (2, 1.5, 1.0 + 1e-13);
        var ringD = ringA.Select((q, i) => i == 41 ? new Point3(q.X, q.Y + 1e-9, q.Z) : q).ToList();
        var dR3 = L1(ringD);
        var dT2 = L1(ringA); dT2[1].T0 += 1e-8;
        var diffs = new (string Name, List<SlopeZone> L)[] { ("자리 1µm", dT), ("자리 10nm", dT2), ("구배", dS), ("구배 1e-13", dS2), ("소단", dW), ("소단 1e-13", dW2), ("시작단", dF),
            ("구간 수", dN), ("차례", dO), ("자 없음", dR0), ("자 한 점 1mm", dR1), ("자 한 점 1nm", dR3), ("자 점 수", dR2) };
        var notCaught = diffs.Where(d => GradeSame.ZonesDiff(baseL, d.L).Length == 0).Select(d => d.Name).ToList();
        check("S154 구간 — 자리(1µm · 10nm) · 구배 · 소단(1e-13까지) · 시작단 · 구간 수 · 차례 · 자(없음 · 한 점 1mm · 1nm · 점 수) 어느 하나만 달라도 «다르다»",
              notCaught.Count == 0, notCaught.Count == 0 ? string.Join(" / ", diffs.Take(3).Select(d => GradeSame.ZonesDiff(baseL, d.L))) : "놓침: " + string.Join(",", notCaught));

        // ⑥ ★[검토 1012 낮음] 부분 지정 표시 — 수직 구간에서만 뜻이 있다(정지면 만들기가 «부분 지정 + 수직»만 정지면에서 뗀다)
        {
            List<SlopeZone> LP(bool partSlope, bool partWall, double wallSlope = 0.01)
            {
                var l = new List<SlopeZone> { Z(270.74, 44.54, ringA, (0, 1.2, 1.0)), Z(70.04, 122.24, ringA, (1, wallSlope, 1.0)) };
                l[0].Partial = partSlope; l[1].Partial = partWall; return l;
            }
            string pSlope = GradeSame.ZonesDiff(LP(true, false), LP(false, false));          // 사면 구간의 표시만 다르다 — 변환 창이 옛 구간을 옮겨 적으며 떨군다
            string pWall = GradeSame.ZonesDiff(LP(false, true), LP(false, false));           // 수직 구간의 표시가 다르다 — 전체구간 옹벽 ↔ 부분 지정
            string pGate = GradeSame.ZonesDiff(LP(false, true, 0.05), LP(false, false, 0.05), 0.05);   // 문턱에 딱 걸린 구배(1:0.05)도 수직
            string pOver = GradeSame.ZonesDiff(LP(false, true, 0.06), LP(false, false, 0.06), 0.05);   // 문턱을 넘으면 사면
            check("S154 구간 — 부분 지정 표시: 사면 구간은 안 본다(같다) · 수직 구간은 본다(다르다) · 문턱 1:0.05는 수직 · 1:0.06은 사면",
                  pSlope.Length == 0 && pWall.Contains("부분 지정") && pGate.Contains("부분 지정") && pOver.Length == 0, $"사면 «{pSlope}» · 수직 «{pWall}» · 1:0.05 «{pGate}» · 1:0.06 «{pOver}»");
        }

        // ⑦ ★[검토 1012 — «같다»가 현장 흐름에서 실제로 나오나] 변환 창(ZoneEditCommon)이 하는 차례 그대로: 번들의 옛 구간을 옮겨 적고(부분 지정 표시는 떨군다) → 방금 고른 자리에
        //   기본값 구간을 넣고 → 겹침 가르기(Flatten) → 죽은 구간 정리(Compact) → 아무 일도 안 하는 구간 빼기(DropNoOps). 옹벽을 기본값 그대로 되돌린 것이면 결과가 넣기 전 목록과 <b>같아야</b> 건너뛴다.
        //   현장 크기 좌표(20만 · 50만대) · 단 링 자 · 랩 구간 포함. 값이 다른 구간을 넣으면 «다르다»여야 한다
        {
            const double OX = 210261.38, OY = 509793.47, bS = 1.5, bW = 1.0;
            var bdF = new List<Point3> { new(OX, OY, 100), new(OX + 60, OY, 100), new(OX + 60, OY + 40, 100), new(OX, OY + 40, 100) };
            var bcF = GradingGeometry.CumLen2D(bdF);
            var ppF = new GradingParams
            {
                CutBenchHeight = 5, FillBenchHeight = 5, CutBenchWidth = 1, FillBenchWidth = 1, CutSlope = 1.5, FillSlope = 1.5, CellSize = 1.0, MaxBenches = 8, MaxRise = 30,
                VertexSpacing = 2.0, MinSlope = 0.05, MinFaceRun = 0.005, MiterConvex = true, MiterLimit = 2.0,
            };
            var RF = WallPlace.BenchRings(bdF, ppF, true, null);
            bool haveF = RF.TryGetValue(1, out var rg) && rg!.Count >= 3;
            check("S154 준비 — 현장 크기 좌표의 절토 1단 자 링", haveF, $"단 {RF.Count}개");
            if (haveF)
            {
                var cg = GradingGeometry.CumLen2D(rg!); double tg = cg[^1];
                static SlopeZone CopyAsEdit(SlopeZone z) => new() { T0 = z.T0, T1 = z.T1, Rules = new(z.Rules), Ref = z.Ref };   // ZoneEditCommon의 옮겨 적기와 같은 꼴(부분 지정 표시 없음)
                int nTry = 0, nSame = 0, sameBits = 0, diffCaught = 0, wraps = 0; string firstBad = "";
                for (int i = 1; i <= 100; i++)
                {
                    double f0 = i * 0.6180339887498949 % 1.0, f1 = i * 0.7548776662466927 % 1.0, f2 = i * 0.5698402909980532 % 1.0;
                    // 옛 구간 둘 — 구간지정 사면 변환(부분 지정 표시 있음) 하나 · 랩일 수도 있는 사면 하나. 서로 안 겹치게 둘레를 셋으로 나눠 쓴다
                    double a0 = f0 * tg, a1 = (a0 + 2 + f1 * 0.25 * tg) % tg;
                    double b0 = (a1 + 3 + f2 * 0.1 * tg) % tg, b1 = (b0 + 2 + f1 * 0.2 * tg) % tg;
                    double c0 = (b1 + 4) % tg, c1 = (c0 + 2 + f0 * 0.1 * tg) % tg;          // 방금 고른 자리(옹벽이 서 있던 처음 그대로인 자리)
                    double used = ((a1 - a0 + tg) % tg) + ((b0 - a1 + tg) % tg) + ((b1 - b0 + tg) % tg) + ((c0 - b1 + tg) % tg) + ((c1 - c0 + tg) % tg);
                    if (used >= tg - 5) continue;                                          // 한 바퀴를 넘어 서로 겹치면 이 검사의 전제가 아니다
                    if (a1 < a0 || b1 < b0) wraps++;
                    var zA = new SlopeZone { T0 = a0, T1 = a1, Ref = rg, Partial = true }; zA.Rules.Add((1, 1.2, bW)); zA.Normalize();
                    var zB = new SlopeZone { T0 = b0, T1 = b1, Ref = rg }; zB.Rules.Add((1, 1.0, bW)); zB.Rules.Add((3, bS, bW)); zB.Normalize();
                    var snap = new List<SlopeZone> { zA, zB };                             // 끝까지 성공한 마지막 정지면 생성이 쓴 구간(= 번들에 적힌 것)

                    List<SlopeZone> Edit(double slope)
                    {
                        var target = snap.Select(CopyAsEdit).ToList();
                        var nz = new SlopeZone { T0 = c0, T1 = c1, Ref = rg }; nz.Rules.Add((1, slope, bW)); nz.Normalize();
                        target.Add(nz);
                        SlopeZone.Flatten(target, bcF[^1]);
                        SlopeZone.Compact(target, bdF, bcF);
                        SlopeZone.DropNoOps(target, bS, bW, bdF, bcF, null, nz);
                        return target;
                    }
                    nTry++;
                    var back = Edit(bS);                                                   // 기본값 그대로 되돌림
                    string d = GradeSame.ZonesDiff(snap, back, 0.05);
                    if (d.Length == 0) nSame++; else if (firstBad.Length == 0) firstBad = d;
                    if (back.Count == snap.Count && Enumerable.Range(0, snap.Count).All(k =>
                            BitConverter.DoubleToInt64Bits(back[k].T0) == BitConverter.DoubleToInt64Bits(snap[k].T0) && BitConverter.DoubleToInt64Bits(back[k].T1) == BitConverter.DoubleToInt64Bits(snap[k].T1)))
                        sameBits++;
                    if (GradeSame.ZonesDiff(snap, Edit(1.3), 0.05).Length > 0) diffCaught++;   // 값을 바꿔 되돌림(1:1.3) — 다시 지어야 한다
                }
                check("S154 현장 흐름 — 옹벽을 기본값 그대로 되돌린 구간 목록은 넣기 전과 «같다»(자리까지 비트 그대로) · 값을 바꿔 되돌리면(1:1.3) «다르다» (단 링 자 · 임의 배치 · 랩 포함)",
                      nTry >= 50 && nSame == nTry && sameBits == nTry && diffCaught == nTry && wraps > 0,
                      $"시도 {nTry} · 같다 {nSame} · 자리 비트까지 같다 {sameBits} · 값 바꾼 것 다르다 {diffCaught} · 랩 {wraps}" + (firstBad.Length > 0 ? " · 첫 어긋남: " + firstBad : ""));

                // ⑧ 자리(T0 · T1)만 1e-9로 견주는 까닭을 잰다 — 계획선 자(자 없음) 구간이 둘 이상이면 구간 정리(Flatten)가 목록을 다시 짜고, 랩 구간의 끝은 (T + 둘레) % 둘레로 다시 나온다.
                //   그 흔들림이 얼마인가 · 한 번 정리된 목록(번들에 든 꼴)은 다시 정리해도 비트까지 그대로인가
                {
                    double tot = bcF[^1], worst = 0; int n = 0, moved = 0, stable = 0, sameTol = 0;
                    for (int i = 1; i <= 200; i++)
                    {
                        double f0 = i * 0.6180339887498949 % 1.0, f1 = i * 0.7548776662466927 % 1.0;
                        double t0 = (0.80 + 0.19 * f0) * tot, t1 = (0.01 + 0.10 * f1) * tot;                  // 랩: t0 > t1
                        var zW = new SlopeZone { T0 = t0, T1 = t1 }; zW.Rules.Add((0, 1.2, bW)); zW.Normalize();
                        var zM = new SlopeZone { T0 = 0.30 * tot, T1 = 0.40 * tot }; zM.Rules.Add((0, 1.0, bW)); zM.Normalize();
                        var raw = new List<SlopeZone> { zM, zW };                                               // 둘레 차례대로(정리해도 차례는 그대로)
                        var l1 = raw.Select(CopyAsEdit).ToList();
                        SlopeZone.Flatten(l1, tot);
                        n++;
                        if (l1.Count != 2) continue;
                        double dMove = Math.Max(Math.Max(Math.Abs(l1[0].T0 - zM.T0), Math.Abs(l1[0].T1 - zM.T1)), Math.Max(Math.Abs(l1[1].T0 - t0), Math.Abs(l1[1].T1 - t1)));
                        if (dMove > 0) moved++; worst = Math.Max(worst, dMove);
                        if (GradeSame.ZonesDiff(raw, l1, 0.05).Length == 0) sameTol++;                          // 흔들림은 «같다»로 넘긴다
                        var l2 = l1.Select(CopyAsEdit).ToList();
                        var nz = new SlopeZone { T0 = 0.50 * tot, T1 = 0.55 * tot }; nz.Rules.Add((0, bS, bW)); nz.Normalize();
                        l2.Add(nz);
                        SlopeZone.Flatten(l2, tot); SlopeZone.Compact(l2, bdF, bcF); SlopeZone.DropNoOps(l2, bS, bW, bdF, bcF, null, nz);
                        if (l2.Count == 2 && Enumerable.Range(0, 2).All(k =>
                                BitConverter.DoubleToInt64Bits(l1[k].T0) == BitConverter.DoubleToInt64Bits(l2[k].T0) && BitConverter.DoubleToInt64Bits(l1[k].T1) == BitConverter.DoubleToInt64Bits(l2[k].T1)))
                            stable++;
                    }
                    check("S154 자리 잡음 — 계획선 자의 랩 구간을 구간 정리가 다시 낼 때 끝의 흔들림은 1e-12m 아래 · 견주기(1e-9)는 그것을 «같다»로 넘긴다 · 한 번 정리된 목록은 다시 정리해도 비트까지 그대로",
                          n == 200 && worst <= 1e-12 && sameTol == n && stable == n, $"랩 구간 {n}개 · 끝이 움직인 것 {moved}개 · 가장 큰 흔들림 {worst:E2}m · «같다» {sameTol} · 다시 정리해 비트 그대로 {stable}");
                }
            }
        }
    }
}
