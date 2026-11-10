// ── S237 ★★[v116.8 · JACK 1110 진입도로 포장 종류] 포장 층 · 횡단 도장 · 3D 줄눈 · 지형 가르기 ──
//   만든 꼴(현장 값 없음 — 공개 저장소).
using DH.Grading.Core;

static class RoadPaveCheck
{
    public static void Run(Action<string, bool, string> check0)
    {
        void Check(string n, bool ok, string d) => check0("S237 PV" + n, ok, d);
        Console.WriteLine("\n== S237 진입도로 포장 — 층 · 고리 · 도장 · 줄눈 ==");
        // ① 첫 값 · 두께 0 층 뺌 · 이름 차례 · Key
        {
            var la = RoadPave.Layers(RoadPave.Kind.Asphalt, null, null);
            var lc = RoadPave.Layers(RoadPave.Kind.Concrete, null, null);
            Check(" ① 첫 값 — 아스팔트 표층 · 기층 · 보조기층 / 콘크리트 슬래브 · 보조기층",
                la.Select(l => l.Name).SequenceEqual(new[] { "표층", "기층", "보조기층" }) && lc.Select(l => l.Name).SequenceEqual(new[] { "콘크리트 슬래브", "보조기층" }),
                string.Join(",", la.Select(l => l.Name)) + " / " + string.Join(",", lc.Select(l => l.Name)));
            var l0 = RoadPave.Layers(RoadPave.Kind.Asphalt, new[] { 0.05, 0, 0.2 }, null);
            Check(" ① 두께 0 층은 뺌", l0.Count == 2 && l0[1].Name == "보조기층", string.Join(",", l0.Select(l => l.Name)));
            Check(" ① Key 글자 고정", RoadPave.Key(RoadPave.Kind.Asphalt, null, null) == "A:50,150,250" && RoadPave.Key(RoadPave.Kind.Concrete, null, null) == "C:250,200",
                RoadPave.Key(RoadPave.Kind.Asphalt, null, null) + " · " + RoadPave.Key(RoadPave.Kind.Concrete, null, null));
            var bad = RoadPave.Clean(new[] { double.NaN, -1, 9 }, RoadPave.DefaultA);
            Check(" ① 깨진 배열 → 첫 값", bad.SequenceEqual(RoadPave.DefaultA), string.Join(",", bad));
            var shortA = RoadPave.Clean(new[] { 0.04 }, RoadPave.DefaultA);
            Check(" ① 짧은 배열 → 남은 칸 첫 값", shortA.Length == 3 && shortA[0] == 0.04 && shortA[2] == 0.25, string.Join(",", shortA));
        }
        // ② 고리 — HalfL 3 · HalfR 3.5 · 2 %
        {
            var sec = new RoadGrade.Section { HalfL = 3, HalfR = 3.5, CrossSlope = 0.02 };
            var ly = RoadPave.Layers(RoadPave.Kind.Asphalt, null, null);
            var rg = RoadPave.Rings(sec, ly);
            double topErr = 0, tErr = 0, gapErr = 0;
            foreach (var (o, z) in rg[0].Skip(3)) topErr = Math.Max(topErr, Math.Abs(z - sec.Drop(o)));
            for (int i = 0; i < rg.Count; i++)
            {
                for (int k = 0; k < 3; k++) tErr = Math.Max(tErr, Math.Abs((rg[i][5 - k].Z - rg[i][k].Z) - ly[i].T));
                if (i > 0) for (int k = 0; k < 3; k++) gapErr = Math.Max(gapErr, Math.Abs(rg[i][5 - k].Z - rg[i - 1][k].Z));
            }
            bool ends = rg.All(r => Math.Abs(r[0].O + 3) < 1e-12 && Math.Abs(r[2].O - 3.5) < 1e-12);
            Check(" ② 맨 윗선 = Drop · 층 두께 = T · 위 층 밑 = 아래 층 위 · 끝 = ±반폭", topErr < 1e-9 && tErr < 1e-9 && gapErr < 1e-9 && ends, $"윗선 {topErr:E1} · 두께 {tErr:E1} · 틈 {gapErr:E1} · 끝 {ends}");
            double sa = 0; var r0 = rg[0]; for (int i = 0; i < r0.Count; i++) { var a = r0[i]; var b = r0[(i + 1) % r0.Count]; sa += a.O * b.Z - b.O * a.Z; }
            Check(" ② 고리 반시계(O–Z 평면) · 넓이 = 폭 × 두께", sa > 0 && Math.Abs(sa / 2 - 6.5 * ly[0].T) < 1e-9, $"{sa / 2:F6} ㎡");
        }
        // ③ 횡단 도장 판정 — 차례 측점 → 지표면 → 포장 → 축척 → 배치 · 옛 3칸 = 한 번 알림 · 실패 도장(빈 글)은 옛 판처럼
        {
            string k = RoadPave.Key(RoadPave.Kind.Asphalt, null, null), kc = RoadPave.Key(RoadPave.Kind.Concrete, null, null);
            string s4 = RoadXsecRules.Stamp("st", "h", "sig", k), s3 = RoadXsecRules.Stamp("st", "h", "sig");
            string Z(string stamp, string st, string pave) => RoadXsecRules.Stale(stamp, st, "h", "sig", pave, 200, 200, 0, 0, 1, 1);
            Check(" ③ 같은 열쇠 = 최신 · 다른 열쇠 = «포장이 바뀌었습니다» · 옛 3칸 = «포장 층 없이 그린 횡단도» · 측점이 먼저",
                Z(s4, "st", k) == "" && Z(s4, "st", kc) == RoadXsecRules.PaveChanged && Z(s3, "st", k) == RoadXsecRules.PaveOld && Z(s4, "st2", kc) == "종단이 수정되었습니다",
                $"{Z(s4, "st", k)}|{Z(s4, "st", kc)}|{Z(s3, "st", k)}|{Z(s4, "st2", kc)}");
            string zf = RoadXsecRules.Stale("", "st", "h", "sig", k, 200, 100, 0, 0, 1, 1), zf2 = RoadXsecRules.Stale("", "st", "h", "sig", k, 200, 200, 0, 0, 1, 1);
            Check(" ③ 실패 도장(빈 글) = 축척 · 배치만(포장 알림 안 함)", zf == "축척이 바뀌었습니다" && zf2 == "", $"{zf}|{zf2}");
            Check(" ③ 옛 8 · 10인자 판정 그대로(포장 없는 3칸 도장 = 최신)", RoadXsecRules.Stale(s3, "st", "h", "sig", 200, 200, 0, 0, 1, 1) == "", "");
        }
        // ⑤ 지형 가르기 — 콘크리트 차로
        {
            const double OX = 200000, OY = 550000;
            int nx = 60, ny = 40; double d = 0.5;
            var X = new List<double>(); var Y = new List<double>(); var Zs = new List<double>();
            for (int j = 0; j <= ny; j++) for (int i = 0; i <= nx; i++) { X.Add(OX + i * d); Y.Add(OY + j * d); Zs.Add(0); }
            var F = new List<int>();
            for (int j = 0; j < ny; j++) for (int i = 0; i < nx; i++) { int a = j * (nx + 1) + i, b = a + 1, c = a + nx + 1, e = c + 1; F.AddRange(new[] { a, b, e }); F.AddRange(new[] { a, e, c }); }
            var gf = NtsSupport.Factory();
            NetTopologySuite.Geometries.Geometry Box(double x0, double y0, double x1, double y1) => gf.CreatePolygon(new[] { new NetTopologySuite.Geometries.Coordinate(OX + x0, OY + y0), new NetTopologySuite.Geometries.Coordinate(OX + x1, OY + y0), new NetTopologySuite.Geometries.Coordinate(OX + x1, OY + y1), new NetTopologySuite.Geometries.Coordinate(OX + x0, OY + y1), new NetTopologySuite.Geometries.Coordinate(OX + x0, OY + y0) });
            TerrainMesh.Input In(NetTopologySuite.Geometries.Geometry? lane, NetTopologySuite.Geometries.Geometry? laneC) => new()
            {
                X = X.ToArray(), Y = Y.ToArray(), Z = Zs.ToArray(), F = F.ToArray(), GroundZ = (x, y) => 0.0, Plan = Box(-1, -1, 31, 21),
                RoadArea = Box(-1, 5, 31, 15), RoadPave = Box(-1, 8.5, 31, 11.5), RoadLane = lane, RoadLaneConc = laneC, BaseZ = (x, y) => 0.0,
            };
            int nT = F.Count / 3;
            (double X, double Y) Ctr(int t) => ((X[F[t * 3]] + X[F[t * 3 + 1]] + X[F[t * 3 + 2]]) / 3 - OX, (Y[F[t * 3]] + Y[F[t * 3 + 1]] + Y[F[t * 3 + 2]]) / 3 - OY);
            int Bad(TerrainMesh.Output o, Func<double, double, TerrainMesh.Cls?> want) { int n = 0; for (int t = 0; t < nT; t++) { var (cx, cy) = Ctr(t); var w = want(cx, cy); if (w != null && o.TriCls[t] != w) n++; } return n; }
            bool InPave(double y) => y > 8.5 && y < 11.5; bool InLane(double y) => y > 9 && y < 11;
            // (가) 콘크리트 차로만 — 길어깨는 길어깨 그대로
            var oC = TerrainMesh.Build(In(null, Box(-1, 9, 31, 11)), OX, OY);
            int bC = Bad(oC, (x, y) => !InPave(y) ? null : InLane(y) ? TerrainMesh.Cls.RoadConc : TerrainMesh.Cls.Shoulder);
            Check(" ⑤ 콘크리트 차로만 — 차로 = 진입도로(콘크리트) · 길어깨 = 길어깨(차로로 안 바뀜)", bC == 0 && oC.Count[(int)TerrainMesh.Cls.RoadConc] > 0 && oC.Count[(int)TerrainMesh.Cls.Shoulder] > 0, $"틀림 {bC}");
            // (나) 섞임 — 왼쪽 반 아스팔트 · 오른쪽 반 콘크리트 · (다) 겹침 = 콘크리트
            var oM = TerrainMesh.Build(In(Box(-1, 9, 16, 11), Box(14, 9, 31, 11)), OX, OY);
            int bM = Bad(oM, (x, y) => !InPave(y) ? null : !InLane(y) ? TerrainMesh.Cls.Shoulder : x > 14 ? TerrainMesh.Cls.RoadConc : x < 14 ? TerrainMesh.Cls.Road : null);
            Check(" ⑤ 섞임 — 각자 · 겹친 자리 = 콘크리트", bM == 0 && oM.Count[(int)TerrainMesh.Cls.Road] > 0 && oM.Count[(int)TerrainMesh.Cls.RoadConc] > 0, $"틀림 {bM}");
            // (라) 콘크리트 다각형 없음 = 옛 결과와 비트 같음
            var oA = TerrainMesh.Build(In(Box(-1, 9, 31, 11), null), OX, OY);
            var oOld = TerrainMesh.Build(new TerrainMesh.Input { X = X.ToArray(), Y = Y.ToArray(), Z = Zs.ToArray(), F = F.ToArray(), GroundZ = (x, y) => 0.0, Plan = Box(-1, -1, 31, 21), RoadArea = Box(-1, 5, 31, 15), RoadPave = Box(-1, 8.5, 31, 11.5), RoadLane = Box(-1, 9, 31, 11), BaseZ = (x, y) => 0.0 }, OX, OY);
            Check(" ⑤ 콘크리트 없음 = 옛 결과와 같음(구분 · 점 · 색인)", oA.TriCls.SequenceEqual(oOld.TriCls) && oA.Pos.SequenceEqual(oOld.Pos) && oA.Idx.SequenceEqual(oOld.Idx), "");
            Check(" ⑤ 새 열쇠 맨 끝 «roadConc» · 옛 번호 그대로", TerrainMesh.Keys[^1] == "roadConc" && (int)TerrainMesh.Cls.RoadConc == TerrainMesh.Keys.Length - 1 && TerrainMesh.Keys[(int)TerrainMesh.Cls.Shoulder] == "shoulder", "");
        }
        // ④ 줄눈 — 곧은 30 m · 간격 6 → 6 · 12 · 18 · 24 네 줄 · 폭 = w
        {
            (double X, double Y)? Pt(double s, double o) => s < 0 || s > 30 ? null : (s, o);
            var r = RoadPave.Joints(new[] { (0.0, 30.0) }, 6, Pt, _ => (3, 3), (x, y) => 10.0);
            double wMax = 0; for (int i = 0; i + 2 < r.Idx.Count; i += 3) { var a = r.Pos[r.Idx[i]]; foreach (int j in new[] { r.Idx[i + 1], r.Idx[i + 2] }) wMax = Math.Max(wMax, Math.Abs(r.Pos[j].X - a.X)); }
            var xs = r.Pos.Select(p => Math.Round(p.X, 3)).Distinct().OrderBy(v => v).ToList();
            Check(" ④ 곧은 30 m · 간격 6 → 줄눈 넷(6 · 12 · 18 · 24) · 폭 3 cm · 높이 = 지형 + 3 cm", r.Lines == 4 && Math.Abs(wMax - RoadPave.JointWidth) < 1e-9 && r.Pos.All(p => Math.Abs(p.Z - 10.03) < 1e-9) && xs.Count == 8,
                $"{r.Lines}줄 · 폭 {wMax:F4} · x {string.Join(",", xs)}");
            double sg = 0; for (int i = 0; i + 2 < r.Idx.Count; i += 3) { var a = r.Pos[r.Idx[i]]; var b = r.Pos[r.Idx[i + 1]]; var c = r.Pos[r.Idx[i + 2]]; sg += Math.Sign((b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X)); }
            Check(" ④ 세모 감김 한 방향", Math.Abs(sg) == r.Idx.Count / 3, $"{sg}/{r.Idx.Count / 3}");
            var rNo = RoadPave.Joints(new[] { (0.0, 30.0) }, 6, Pt, _ => (3, 3), (x, y) => null);
            Check(" ④ 지형 없는 칸 = 버림(지형 없음)", rNo.Kept == 0 && rNo.DropNoZ > 0, rNo.Text);
            var rGap = RoadPave.Joints(new[] { (0.0, 5.0), (20.0, 30.0) }, 6, Pt, _ => (3, 3), (x, y) => 10.0);
            Check(" ④ 구간마다 시작에서 셈 · 끝 0.5 m 안 없음", rGap.Lines == 1, $"{rGap.Lines}줄");
        }
    }
}
