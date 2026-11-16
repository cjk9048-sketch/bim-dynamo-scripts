using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using DH.Grading.Core;
using CivilDb = Autodesk.Civil.DatabaseServices;

namespace DH.Grading.Civil;

/// <summary>★★[v117.19 · JACK 1116 «굽이 평탄화»] ■ 종단수정 [굽이 평탄화] 체크 — 계획 종단 PVI에 급한 굽이 구간을 0 %로 펴 넣고 · 끄면 펴기 전 PVI로 되돌림(<see cref="RoadBendFlat"/> 순수 셈 위).
/// 한 트랜잭션 · 처음 · 끝 PVI 그대로 · 수가 안 맞으면 커밋 안 함(<see cref="RoadBuilder.Redraft"/>와 같은 법).</summary>
internal static class RoadBendFlatCivil
{
    /// <summary>중심선에서 그 쪽 안쪽 첫 단(소단) 거리 — 반폭 + 길어깨 + 첫 사면(구배 × 단높이 · 절 · 성 중 큰 쪽 — 땅 높이를 몰라 넓은 쪽) + 소단 폭 절반.</summary>
    internal static double TierDist(RoadDesign d, bool left)
        => (left ? d.HalfL + d.ShoulderL : d.HalfR + d.ShoulderR)
           + Math.Max(d.CutSlope * d.CutBenchH + d.CutBenchW / 2, d.FillSlope * d.FillBenchH + d.FillBenchW / 2);

    /// <summary>노선을 1 m마다 읽는다(X · Y · 측점).</summary>
    static (double[] X, double[] Y, double[] S) SampleAlign(CivilDb.Alignment al, double s0, double s1)
    {
        var X = new List<double>(); var Y = new List<double>(); var S = new List<double>();
        for (double st = s0; ; st += 1.0)
        {
            double sc = Math.Min(st, s1), x = 0, y = 0;
            try { al.PointLocation(sc, 0, ref x, ref y); if (S.Count == 0 || sc - S[^1] > 1e-3) { X.Add(x); Y.Add(y); S.Add(sc); } } catch { }
            if (sc >= s1) break;
        }
        return (X.ToArray(), Y.ToArray(), S.ToArray());
    }

    /// <summary>[굽이 평탄화] 켜짐/꺼짐을 계획 종단에 맞춘다 — 켜짐: 지금 PVI(손으로 안 고친 편 PVI면 편 기록의 «펴기 전»)에서 다시 펴기 · 꺼짐: 안 고쳤으면 펴기 전으로.
    /// d의 기록(BendBackup · BendApplied)을 바꾼다 — 저장은 부른 쪽(<see cref="RoadSession.SaveDesign"/>). 돌려줌 = 줄글(명령창 · 위에 ⚠ 줄 포함).</summary>
    internal static string Sync(Document doc, RoadSession s, RoadDesign d)
    {
        using var dl = doc.LockDocument();
        using var tr = doc.Database.TransactionManager.StartTransaction();
        var al = (CivilDb.Alignment)tr.GetObject(s.AlignId, OpenMode.ForRead);
        var pr = (CivilDb.Profile)tr.GetObject(s.PlanId, OpenMode.ForRead);
        var cur = new List<(double S, double Z)>();
        for (int i = 0; i < pr.PVIs.Count; i++) cur.Add((pr.PVIs[i].RawStation, pr.PVIs[i].Elevation));
        if (cur.Count < 2) return "굽이 평탄화 — 계획 종단이 비었음";
        var applied = RoadBendFlat.Unpack(d.BendApplied); var backup = RoadBendFlat.Unpack(d.BendBackup);
        bool have = applied.Count >= 2 && backup.Count >= 2;
        bool untouched = have && RoadBendFlat.Same(cur, applied);
        var sb = new System.Text.StringBuilder();
        List<(double S, double Z)> target;
        if (!d.BendFlat)
        {
            if (!have) return "굽이 평탄화 꺼짐 — 펴 둔 것 없음";
            if (!untouched)
            {
                d.BendApplied = Array.Empty<double>(); d.BendBackup = Array.Empty<double>();
                return "⚠ 굽이 평탄화 끔 — 종단을 손으로 고쳐 둠 → 지금 종단 그대로(펴기 전으로 안 되돌림)";
            }
            target = backup; sb.Append("굽이 평탄화 끔 — 펴기 전 종단으로 되돌림");
            d.BendApplied = Array.Empty<double>(); d.BendBackup = Array.Empty<double>();
        }
        else
        {
            List<(double S, double Z)> baseP;
            if (have && untouched) baseP = backup;
            else { baseP = cur; if (have) sb.AppendLine("⚠ 편 뒤 종단을 손으로 고쳐 둠 → 지금 종단 위에 다시 평탄화"); }
            double s0 = Math.Max(al.StartingStation, baseP[0].S), s1 = Math.Min(al.EndingStation, baseP[^1].S);
            var (x, y, st) = SampleAlign(al, s0, s1);
            var zones = RoadBendFlat.Detect(x, y, st, TierDist(d, true), TierDist(d, false));
            var r = RoadBendFlat.Apply(baseP, zones, d.MaxGrade);
            foreach (var w in r.Warnings) sb.AppendLine(w);
            if (r.Zones.Count == 0)
            {
                if (have && untouched) { target = backup; sb.Append("굽이 평탄화 — 급한 굽이 없음 → 펴기 전 종단으로 되돌림"); }
                else { sb.Append("굽이 평탄화 — 급한 굽이 없음 (반경이 안쪽 소단 거리보다 작은 굽이만)"); return sb.ToString(); }
                d.BendApplied = Array.Empty<double>(); d.BendBackup = Array.Empty<double>();
            }
            else
            {
                target = r.Pvi;
                d.BendBackup = RoadBendFlat.Pack(baseP); d.BendApplied = RoadBendFlat.Pack(r.Pvi);
                sb.Append($"굽이 평탄화 — 굽이 {r.Zones.Count}곳 · " + string.Join(" · ", r.Zones.Select(z => $"측점 {z.A:F0}~{z.B:F0} m 0 %(높이 {z.Z:F2})")) + $" · 최대 종단경사 {r.MaxGrade * 100:F1} %");
            }
        }
        if (RoadBendFlat.Same(cur, target)) return sb.ToString();
        // 쓰기 — 가운데 PVI만 지우고 안쪽을 더함(처음 · 끝은 그대로 · 수 안 맞으면 커밋 안 함)
        if (Math.Abs(target[0].S - cur[0].S) > 1e-6 || Math.Abs(target[^1].S - cur[^1].S) > 1e-6 || Math.Abs(target[0].Z - cur[0].Z) > 1e-6 || Math.Abs(target[^1].Z - cur[^1].Z) > 1e-6)
            throw new InvalidOperationException("굽이 평탄화 — 처음 · 끝 PVI가 달라짐 → 바꾸지 않음");
        pr.UpgradeOpen();
        for (int i = pr.PVIs.Count - 2; i >= 1; i--) pr.PVIs.RemoveAt(i);
        for (int k = 1; k + 1 < target.Count; k++) pr.PVIs.AddPVI(target[k].S, target[k].Z);
        if (pr.PVIs.Count != target.Count) throw new InvalidOperationException($"굽이 평탄화 — PVI 수가 다름({pr.PVIs.Count}/{target.Count}) → 바꾸지 않음");
        tr.Commit();
        return sb.ToString();
    }
}
