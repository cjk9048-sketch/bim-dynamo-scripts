// ── S188 CR ★★★[v111.9 · 계획_원지형자르기.md Step 1 · JACK 1103 «어느 단계에서나 · 모든 지표면»] 원지형 자르기 순수 셈 ──
//   만든 꼴(현장 값 없음 — 공개 저장소) · 좌표는 현장 크기(X 2e5 · Y 5.5e5)로 띄움
using DH.Grading.Core;
using Box = DH.Grading.Core.GroundCrop.Box;
using Tri = DH.Grading.Core.WallDaylight.Tri;

static class CropCheck
{
    const double OX = 200000, OY = 550000;

    static List<(double X, double Y)> RectPts(Box r) => new() { (r.X0, r.Y0), (r.X1, r.Y0), (r.X1, r.Y1), (r.X0, r.Y1) };
    static double Area(Box r) => r.IsNone ? 0 : r.W * r.H;

    /// <summary>두 네모의 겹친 넓이.</summary>
    static double Overlap(Box a, Box b)
    {
        double w = Math.Min(a.X1, b.X1) - Math.Max(a.X0, b.X0), h = Math.Min(a.Y1, b.Y1) - Math.Max(a.Y0, b.Y0);
        return w > 0 && h > 0 ? w * h : 0;
    }

    /// <summary>격자 세모(칸 <paramref name="c"/> m) — 높이는 좌표의 간단한 식.</summary>
    static List<Tri> Grid(double x0, double y0, double x1, double y1, double c)
    {
        var t = new List<Tri>();
        Point3 P(double x, double y) => new(x, y, 50 + 0.01 * (x - OX) + 0.02 * (y - OY));
        for (double x = x0; x < x1 - 1e-9; x += c)
            for (double y = y0; y < y1 - 1e-9; y += c)
            {
                t.Add(new Tri(P(x, y), P(x + c, y), P(x + c, y + c)));
                t.Add(new Tri(P(x, y), P(x + c, y + c), P(x, y + c)));
            }
        return t;
    }

    public static void Run(Action<string, bool, string> check)
    {
        Console.WriteLine("\n== S188 CR 원지형 자르기(네 띠 · 우리 띠 알아보기 · 설계 자리 검사 · 나간 조각 · 안쪽 지문 · 다시 키우기) ==");
        var box = new Box(OX + 100, OY + 200, OX + 570, OY + 423);       // 470 × 223 m
        var surf = new Box(OX - 900, OY - 1200, OX + 1300, OY + 1600);  // 2.2 × 2.8 km

        // ① 네 띠 — 상자와 틈 · 겹침 0 · 합집합 = E − 상자 · 면 범위가 상자보다 작은 쪽은 띠 없음
        {
            var f = GroundCrop.Frame(box, surf);
            var e = surf.Grow(GroundCrop.Pad);
            double sum = f.Sum(Area), ov = 0;
            for (int i = 0; i < f.Count; i++) { ov += Overlap(f[i], box); for (int j = i + 1; j < f.Count; j++) ov += Overlap(f[i], f[j]); }
            double want = Area(e) - Area(box);
            // 맞닿음: 왼 띠 오른쪽 변 = x0 · 오른 띠 왼쪽 변 = x1 · 아래 띠 위 변 = y0 · 위 띠 아래 변 = y1(비트 그대로)
            bool touch = f.Count == 4 && f[0].X1 == box.X0 && f[1].X0 == box.X1 && f[2].Y1 == box.Y0 && f[3].Y0 == box.Y1
                         && f[2].X0 == box.X0 && f[2].X1 == box.X1 && f[3].X0 == box.X0 && f[3].X1 == box.X1;
            check("S188 CR① 네 띠 — 넷 · 상자와 맞닿음(비트) · 서로 · 상자와 겹침 0 · 넓이 합 = E − 상자", touch && ov == 0 && Math.Abs(sum - want) <= 1e-6 * want,
                  $"띠 {f.Count} · 겹침 {ov} · 넓이 {sum:F3} / {want:F3}");
            // 면이 상자 왼쪽 · 아래쪽보다 안쪽에서 시작(그 쪽 E도 상자 안) → 띠 둘(오른 · 위)만
            var small = new Box(box.X0 + 50 + GroundCrop.Pad, box.Y0 + 30 + GroundCrop.Pad, box.X1 + 400, box.Y1 + 300);
            var f2 = GroundCrop.Frame(box, small);
            bool two = f2.Count == 2 && f2[0].X0 == box.X1 && f2[1].Y0 == box.Y1 && f2.All(b => b.W > 0 && b.H > 0);
            check("S188 CR① 면이 상자보다 작은 쪽은 띠 넓이 0 → 그 띠 안 넣음(오른 · 위만)", two, $"띠 {f2.Count}: {string.Join(" ; ", f2.Select(b => b.Text))}");
            var inside = new Box(box.X0 + 10, box.Y0 + 10, box.X1 - 10, box.Y1 - 10);
            check("S188 CR① 자를 면 판정 — 다 안(경계 위 포함) = 아님 · 한 변 1 cm 넘음 = 자름",
                  !GroundCrop.NeedsCrop(inside, box) && !GroundCrop.NeedsCrop(box, box) && GroundCrop.NeedsCrop(new Box(box.X0, box.Y0 - 0.01, box.X1, box.Y1), box) && GroundCrop.NeedsCrop(surf, box), "");
        }

        // ② 우리 띠 알아보기 — 기록 · 꼴 · 사람 Hide 섞임 · 옛 Outer
        {
            var f = GroundCrop.Frame(box, surf);
            var bnd = new List<GroundCrop.Bnd>();
            bnd.Add(new("Outer", RectPts(surf)));                                // 사람(또는 가상 면 자기) Outer — 네모지만 원지반이 아니면 안 건드림
            var human = new Box(OX + 300, OY + 300, OX + 320, OY + 310);
            bnd.Add(new("Hide", RectPts(human)));                                // 사람 Hide 하나
            foreach (var b in f) bnd.Add(new("Hide", RectPts(b).Append(RectPts(b)[0]).ToList()));   // 닫는 점까지 되읽힌 꼴
            bnd.Add(new("Show", RectPts(human)));
            var rec = GroundCrop.OurBoundaries(bnd, f, false);
            check("S188 CR② 기록 있음 — 우리 띠 넷(2 ~ 5)만 · 사람 Outer · Hide · Show 안 건드림", rec.Select(r => r.Index).SequenceEqual(new[] { 2, 3, 4, 5 }) && rec.All(r => r.Why == GroundCrop.Why.Record),
                  string.Join(",", rec.Select(r => $"{r.Index}:{r.Why}")));
            var shp = GroundCrop.OurBoundaries(bnd, null, false);
            check("S188 CR② 기록 없음 — 꼴(L R B T 네 띠 잇달아)로 같은 넷", shp.Select(r => r.Index).SequenceEqual(new[] { 2, 3, 4, 5 }) && shp.All(r => r.Why == GroundCrop.Why.Shape),
                  string.Join(",", shp.Select(r => $"{r.Index}:{r.Why}")));
            // 기록 없음 · 사람 Hide 네모 하나 · 둘 · 셋(띠 꼴이 아님) → 아무것도 안 알아봄
            var lone = new List<GroundCrop.Bnd> { new("Hide", RectPts(human)), new("Hide", RectPts(f[0])), new("Hide", RectPts(f[1])), new("Hide", RectPts(f[2])) };
            var l0 = GroundCrop.OurBoundaries(lone, null, false);
            check("S188 CR② 기록 없음 · 사람 Hide 네모 하나 + 띠 셋(넷이 안 됨) → 안 건드림", l0.Count == 0, string.Join(",", l0.Select(r => r.Index)));
            // 둘레 위 덧점이 든 되읽기(Civil이 꼭짓점을 더 줄 때) — 같은 네모로 봄
            var withMid = RectPts(f[0]); withMid.Insert(1, ((f[0].X0 + f[0].X1) / 2, f[0].Y0));
            var mid = GroundCrop.OurBoundaries(new List<GroundCrop.Bnd> { new("Hide", withMid) }, f, false);
            check("S188 CR② 둘레 위 덧점이 든 꼭짓점도 같은 띠", mid.Count == 1, "");
            // 옛 DHCROP Outer(원지반만) — 네모 Outer 하나 · 다른 면이면 안 건드림
            var old = new List<GroundCrop.Bnd> { new("Outer", RectPts(box)) };
            var o1 = GroundCrop.OurBoundaries(old, null, true);
            var o2 = GroundCrop.OurBoundaries(old, null, false);
            var bent = new List<GroundCrop.Bnd> { new("Outer", new List<(double, double)> { (box.X0, box.Y0), (box.X1, box.Y0), (box.X1, box.Y1), (box.X0 + 30, box.Y1), (box.X0, box.Y1 - 30) }) };
            var o3 = GroundCrop.OurBoundaries(bent, null, true);
            check("S188 CR② 옛 DHCROP Outer — 원지반의 네모 Outer = 우리 것 · 다른 면 = 아님 · 네모가 아닌 Outer = 아님", o1.Count == 1 && o1[0].Why == GroundCrop.Why.OldOuter && o2.Count == 0 && o3.Count == 0, "");
            // 기록이 어긋남(사람이 띠 하나를 고침) — 맞는 셋만 우리 것 · 고친 것은 남김
            var edited = new List<GroundCrop.Bnd>(f.Select(b => new GroundCrop.Bnd("Hide", RectPts(b))));
            edited[1] = new("Hide", RectPts(new Box(f[1].X0 + 5, f[1].Y0, f[1].X1, f[1].Y1)));
            var ed = GroundCrop.OurBoundaries(edited, f, false);
            check("S188 CR② 기록과 어긋난 띠(사람이 고침) — 맞는 셋만 · 고친 것은 안 건드림", ed.Select(r => r.Index).SequenceEqual(new[] { 0, 2, 3 }), string.Join(",", ed.Select(r => r.Index)));
        }

        // ③ 설계 자리 검사
        {
            var site = new GroundCrop.Item("계획부지 1", RectPts(new Box(box.X0 + 40, box.Y0 + 40, box.X0 + 200, box.Y0 + 150)), true);
            var road = new GroundCrop.Item("진입도로 DH도로_1", new List<(double, double)> { (box.X0 + 200, box.Y0 + 100), (box.X1 - 40, box.Y0 + 120) }, false);
            var c0 = GroundCrop.Covers(box, new[] { site, road });
            check("S188 CR③ 다 안 → 통과 · 나간 항목 0 · 필요 상자 = D + 5 m", c0.Ok && c0.Outs.Count == 0 && c0.Need == new Box(box.X0 + 35, box.Y0 + 35, box.X1 - 35, box.Y0 + 155), c0.Need.Text);
            // 한 변 1 cm 밖(여유 5 m 기준) — 계획부지 위 변이 «상자 − 5 m»를 1 cm 넘음
            var site1 = new GroundCrop.Item("계획부지 1", RectPts(new Box(box.X0 + 40, box.Y0 + 40, box.X0 + 200, box.Y1 - 5 + 0.01)), true);
            var c1 = GroundCrop.Covers(box, new[] { site1, road });
            check("S188 CR③ 한 변 1 cm 밖 → 거절 · 나간 항목 = 계획부지만 · 나간 길이 = 160.02 m(그 변 + 양 옆 1 cm씩) · 멀리 1 cm",
                  !c1.Ok && c1.Outs.Count == 1 && c1.Outs[0].Label == "계획부지 1" && Math.Abs(c1.Outs[0].Length - 160.02) <= 1e-6 && Math.Abs(c1.Outs[0].Far - 0.01) <= 1e-6,
                  string.Join(" ; ", c1.Outs.Select(o => $"{o.Label} {o.Length:F6} m · 멀리 {o.Far:F6}")));
            // 표본선 끝만 밖(1 m)
            var sl = new GroundCrop.Item("횡단 표본선 0+020", new List<(double, double)> { (box.X0 + 250, box.Y0 + 100), (box.X0 + 250, box.Y0 - 1 + 5) }, false);   // 끝이 «상자 − 5 m»보다 1 m 아래
            var c2 = GroundCrop.Covers(box, new[] { site, road, sl });
            check("S188 CR③ 표본선 끝만 1 m 밖 → 거절 · 나간 항목 = 표본선만 · 나간 길이 1 m",
                  !c2.Ok && c2.Outs.Count == 1 && c2.Outs[0].Label.StartsWith("횡단 표본선") && Math.Abs(c2.Outs[0].Length - 1) <= 1e-6, string.Join(" ; ", c2.Outs.Select(o => $"{o.Label} {o.Length:F6}")));
            // 여유 경계값 — 꼭짓점이 «상자 − 5 m» 위 = 통과 · 1 µm 넘으면 거절
            var on = new GroundCrop.Item("표본선 끝 경계 위", new List<(double, double)> { (box.X0 + 250, box.Y0 + 100), (box.X0 + 250, box.Y0 + 5) }, false);
            var over = new GroundCrop.Item("표본선 끝 경계 너머", new List<(double, double)> { (box.X0 + 250, box.Y0 + 100), (box.X0 + 250, box.Y0 + 5 - 1e-6) }, false);
            check("S188 CR③ 여유 5 m 경계값 — 경계 위 통과 · 1 µm 너머 거절", GroundCrop.Covers(box, new[] { on }).Ok && !GroundCrop.Covers(box, new[] { over }).Ok, "");
            check("S188 CR③ 항목 없음 → 통과(설계 전 단계)", GroundCrop.Covers(box, Array.Empty<GroundCrop.Item>()).Ok, "");
            // 상자가 여유 두 배보다 좁음 → 무엇이든 거절
            var tiny = new Box(box.X0, box.Y0, box.X0 + 8, box.Y0 + 8);
            var c3 = GroundCrop.Covers(tiny, new[] { new GroundCrop.Item("점", new List<(double, double)> { (box.X0 + 4, box.Y0 + 4) }, false) });
            check("S188 CR③ 상자가 10 m보다 좁음 → 거절(그 안의 점도)", !c3.Ok && c3.Outs.Count == 1, "");
        }

        // ④ 나간 조각 길이(1e-6)
        {
            var inner = new Box(OX, OY, OX + 100, OY + 100);
            // 대각선이 네모를 가로지름: (−50, 50) → (150, 50) — 밖 50 + 50
            var p1 = GroundCrop.OutsideParts(new List<(double, double)> { (OX - 50, OY + 50), (OX + 150, OY + 50) }, false, inner);
            // 닫힌 네모(−20 ~ 120) — 둘레 560 · 안 0 → 밖 560(한 조각)
            var ring = RectPts(new Box(OX - 20, OY - 20, OX + 120, OY + 120));
            var p2 = GroundCrop.OutsideParts(ring, true, inner);
            // 비스듬한 선 (−30,−40) → (130,180): 길이 = 200·√(1.6²+2.2²)/2 … 직접 셈과 견줌
            var a = (X: OX - 30.0, Y: OY - 40.0); var b = (X: OX + 130.0, Y: OY + 180.0);
            double L = Math.Sqrt(160 * 160 + 220 * 220);
            double tIn = Math.Max((0 - -30) / 160.0, (0 - -40) / 220.0), tOut = Math.Min((100 + 30) / 160.0, (100 + 40) / 220.0);
            var p3 = GroundCrop.OutsideParts(new List<(double, double)> { a, b }, false, inner);
            double w3 = L * (tIn + (1 - tOut));
            // 꺾은선 밖 → 안 → 밖 → 안: 조각 둘
            var p4 = GroundCrop.OutsideParts(new List<(double, double)> { (OX - 10, OY + 10), (OX + 10, OY + 10), (OX + 10, OY - 10), (OX + 50, OY - 10), (OX + 50, OY + 50) }, false, inner);
            double w4 = 10 + 10 + 40 + 10;
            check("S188 CR④ 나간 조각 길이 = 기대(1e-6) — 가로지름 100 · 닫힌 둘레 560 · 비스듬 · 꺾은선 70(조각 2)",
                  Math.Abs(p1.Sum(GroundCrop.Len) - 100) <= 1e-6 && p1.Count == 2 && Math.Abs(p2.Sum(GroundCrop.Len) - 560) <= 1e-6 && p2.Count == 1
                  && Math.Abs(p3.Sum(GroundCrop.Len) - w3) <= 1e-6 && Math.Abs(p4.Sum(GroundCrop.Len) - w4) <= 1e-6 && p4.Count == 2,
                  $"{p1.Sum(GroundCrop.Len):F9}({p1.Count}) · {p2.Sum(GroundCrop.Len):F9}({p2.Count}) · {p3.Sum(GroundCrop.Len):F9}/{w3:F9} · {p4.Sum(GroundCrop.Len):F9}/{w4}({p4.Count})");
        }

        // ⑤ 안쪽 지문 — 세모 차례 · 꼭짓점 차례를 섞어도 같음 · 안쪽 세모 하나 높이 1 µm = 다름 · 바깥 세모가 바뀌어도 같음
        {
            var t = Grid(box.X0 - 60, box.Y0 - 60, box.X1 + 60, box.Y1 + 60, 7.5);
            string h0 = GroundCrop.InsideHash(t, box);
            var rnd = new Random(188);
            var sh = t.OrderBy(_ => rnd.Next()).Select((q, i) => (i % 3) switch { 0 => q, 1 => new Tri(q.B, q.C, q.A), _ => new Tri(q.C, q.B, q.A) }).ToList();
            string h1 = GroundCrop.InsideHash(sh, box);
            int k = t.FindIndex(q => q.A.X > box.X0 + 100 && q.A.Y > box.Y0 + 100);
            var ch = t.ToList(); ch[k] = new Tri(ch[k].A with { Z = ch[k].A.Z + 1e-6 }, ch[k].B, ch[k].C);
            string h2 = GroundCrop.InsideHash(ch, box);
            var outside = t.Where(q => !(new Box(box.X0 + 5, box.Y0 + 5, box.X1 - 5, box.Y1 - 5)).Contains(q.A.X, q.A.Y)).Take(50).ToHashSet();
            var cut = t.Where(q => !outside.Contains(q)).ToList();
            string h3 = GroundCrop.InsideHash(cut, box);
            check("S188 CR⑤ 안쪽 지문 — 섞어도 같음 · 안쪽 1 µm = 다름 · 바깥 세모가 빠져도 같음", h0 == h1 && h0 != h2 && h0 == h3 && !h0.StartsWith("0:"), $"{h0} · {h1} · {h2} · {h3}");
        }

        // ⑥ 옛 길이면 실패 — Outer가 숨은 세모를 드러냄(상자 안 세모가 늘어남) → 지문 «다름»을 잡음 · 여유 0이면 경계 위 표본선 끝이 «통과»로 샘
        {
            var vis = Grid(box.X0 + 40, box.Y0 + 40, box.X0 + 160, box.Y0 + 160, 6);
            var hidden = Grid(box.X0 + 10, box.Y0 + 10, box.X0 + 40, box.Y0 + 190, 6);   // 붙여넣기가 남긴 숨은 세모(상자 안) — Outer로 바꾸면 보임
            string hv = GroundCrop.InsideHash(vis, box), ho = GroundCrop.InsideHash(vis.Concat(hidden), box);
            check("S188 CR⑥ 옛 길(Outer) 흉내 — 숨은 세모가 드러나면 안쪽 지문이 다름(되돌림으로 감)", hv != ho, $"{hv} → {ho}");
            var edge = new GroundCrop.Item("횡단 표본선 끝 상자 위", new List<(double, double)> { (box.X0 + 250, box.Y0 + 100), (box.X0 + 250, box.Y0) }, false);
            bool leak0 = GroundCrop.Covers(box, new[] { edge }, 0).Ok, ok5 = GroundCrop.Covers(box, new[] { edge }).Ok;
            check("S188 CR⑥ 여유 0이면 상자 위 표본선 끝이 «통과»로 샘 · 여유 5 m는 거절", leak0 && !ok5, $"여유 0 {leak0} · 5 m {ok5}");
        }

        // ⑦ 다시 키우기 · 기록 글
        {
            var small = new Box(box.X0 + 50, box.Y0 + 50, box.X1 - 50, box.Y1 - 50);
            var g = GroundCrop.Grew(small, box);
            var ext0 = surf;
            bool notGrownPaste = GroundCrop.NotGrown(small, box, ext0, small);          // 스냅샷이 잘린 꼴로 굳음 — 못 커짐
            bool grown = GroundCrop.NotGrown(small, box, ext0, box);                     // 원지반 — 새 상자까지 커짐
            bool noData = GroundCrop.NotGrown(small, box, small, small);                 // 처음부터 그쪽에 자료 없음 — 못 커짐 아님
            bool shrink = GroundCrop.NotGrown(box, small, ext0, small);                  // 줄임 — 못 커짐 아님
            check("S188 CR⑦ 다시 키우기 — 넓어진 쪽 넷 · 굳은 면 = 못 커짐 · 커진 원지반 · 자료 없던 면 · 줄임 = 아님",
                  g == (true, true, true, true) && notGrownPaste && !grown && !noData && !shrink, $"{g} · {notGrownPaste} {grown} {noData} {shrink}");
            // 다시 자를 때 안쪽 지문 네모 = 새 상자 ∩ 지난 상자(키우면 지난 상자 · 줄이면 새 상자 · 옆으로 옮기면 겹친 곳 · 안 겹치면 없음)
            var side = new Box(box.X0 + 300, box.Y0, box.X1 + 300, box.Y1);
            var far = new Box(box.X1 + 10, box.Y0, box.X1 + 50, box.Y1);
            check("S188 CR⑦ 안쪽 지문 네모 = 새 상자 ∩ 지난 상자", box.Intersect(small) == small && small.Intersect(box) == small
                  && box.Intersect(side) == new Box(box.X0 + 300, box.Y0, box.X1, box.Y1) && box.Intersect(far).IsNone, box.Intersect(side).Text);
            var f = GroundCrop.Frame(box, surf);
            var back = GroundCrop.ParseBands(GroundCrop.BandsText(f));
            check("S188 CR⑦ 기록 글 — 띠 16수 «R» 그대로 오감 · 상자", back.SequenceEqual(f) && GroundCrop.TryBox(GroundCrop.BoxText(box), out var bb) && bb == box && !GroundCrop.TryBox("1,2,3", out _), GroundCrop.BandsText(f).Length + "자");
        }
    }
}
