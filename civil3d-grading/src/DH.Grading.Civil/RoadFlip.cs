using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using DH.Grading.Core;
using CivilDb = Autodesk.Civil.DatabaseServices;

namespace DH.Grading.Civil;

/// <summary>★★★[v113.1 · 계획_종단방향_축척.md 1.2 · 1.4 · JACK 1104 «종단 방향 변경 — 시점↔종점 바꾸기»] 진입도로 시점 ↔ 종점.
/// <para>P1 실측(1104 · 도로 둘): <c>Alignment.Reverse()</c> = 제자리 · 시점 측점 0 그대로(기준점이 새 시점으로) · 자리(옛 s ↔ 새 B − s) 0 mm · 자유 곡선 구속 그대로 ·
/// 면에서 딴 «-바탕» 종단은 저절로 · <b>계획 종단 PVI는 그대로 남음</b>(우리가 다시 씀) · 두 번이면 표본 자리 비트 같음.</para>
/// <para>한 트랜잭션: 옛 XY 적어 둠 → Reverse → 시점 측점 고정 → PVI(차례 반대 · 같은 높이) → 옹벽 · 측구 기록(쪽 반대 · 차례 그대로) · 좌우 폭 맞바꿈 → 수동 · 지운 측점 → 되돌림 기록 → 1 mm 확인(어긋나면 물림).
/// 지표면 · 종단도 · 횡단도는 [업데이트](JACK 1102 «무거운 재작성은 단추로»).</para></summary>
internal static class RoadFlip
{
    const string SnapKey = "DH_ROADFLIP";

    /// <summary>화면 없는 시험 칸 — 참이면 노선을 뒤집은 직후 물림(트랜잭션 되돌림이 노선 · 딸린 종단 · 검토선까지 되돌리는지 잼). 평소 거짓.</summary>
    internal static bool TestFailAfterReverse;

    static (double X, double Y)? P(CivilDb.Alignment al, double s)
    {
        try { double x = 0, y = 0; al.PointLocation(s, 0, ref x, ref y); return (x, y); } catch { return null; }
    }

    static ProfileFlip.Snapshot? ReadSnap(Transaction tr, ObjectId aid)
    {
        try
        {
            var o = tr.GetObject(aid, OpenMode.ForRead);
            if (o.ExtensionDictionary.IsNull) return null;
            var d = (DBDictionary)tr.GetObject(o.ExtensionDictionary, OpenMode.ForRead);
            if (!d.Contains(SnapKey) || tr.GetObject(d.GetAt(SnapKey), OpenMode.ForRead) is not Xrecord xr || xr.Data == null) return null;
            var sb = new System.Text.StringBuilder();
            foreach (TypedValue tv in xr.Data) if (tv.TypeCode == (int)DxfCode.Text) sb.Append((string)tv.Value);
            return ProfileFlip.FromJson(sb.ToString());
        }
        catch { return null; }
    }

    /// <summary>되돌림 기록 쓰기 — 있으면 그 Xrecord 값만 바꿈(실행 취소가 옛 값을 되돌리게 · StationMarks와 같은 법).</summary>
    internal static void WriteJson(Transaction tr, ObjectId ownerId, string key, string json)
    {
        var o = tr.GetObject(ownerId, OpenMode.ForRead);
        if (o.ExtensionDictionary.IsNull) { o.UpgradeOpen(); o.CreateExtensionDictionary(); }
        var dict = (DBDictionary)tr.GetObject(o.ExtensionDictionary, OpenMode.ForWrite);
        var rb = new ResultBuffer();
        for (int i = 0; i < json.Length; i += 200) rb.Add(new TypedValue((int)DxfCode.Text, json.Substring(i, Math.Min(200, json.Length - i))));
        if (dict.Contains(key) && tr.GetObject(dict.GetAt(key), OpenMode.ForWrite) is Xrecord old) { old.Data = rb; return; }
        var xr = new Xrecord { Data = rb };
        dict.SetAt(key, xr); tr.AddNewlyCreatedDBObject(xr, true);
    }

    /// <summary>거절 까닭(빈 글 = 할 수 있음).</summary>
    static string Refusal(Transaction tr, RoadSession s, CivilDb.Alignment al, RoadDesign d, CivilDb.Profile pr)
    {
        if (!RoadBuilder.IsOurs(al)) return "사람이 만든 Civil 노선으로 연 도로는 방향을 바꾸지 않습니다(그 노선의 다른 종단 · 코리더를 모름)";
        try { if (al.StationEquations.Count > 0) return "측점 방정식이 있는 노선은 방향을 바꾸지 않습니다"; } catch { }
        foreach (CivilDb.ProfilePVI q in pr.PVIs)
            try { if (q.VerticalCurve != null) return "계획 종단에 종단곡선이 든 PVI가 있어 방향을 바꾸지 않습니다(우리 도로는 곡선을 안 넣음 — 사람이 넣은 곡선을 말없이 바꾸지 않음)"; } catch { }
        // 우리 것이 아닌 계획(레이아웃) 종단 — 그 PVI는 우리가 다시 쓰지 않으므로 뒤집으면 엉뚱한 자리에 남는다
        var ours = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { d.SheetGround, d.SheetChain };
        foreach (ObjectId pid in al.GetProfileIds())
        {
            if (pid == s.PlanId || pid == s.BaseProfId) continue;
            try
            {
                var p = (CivilDb.Profile)tr.GetObject(pid, OpenMode.ForRead);
                if (p.ProfileType != CivilDb.ProfileType.FG) continue;
                string pn = p.Name ?? "";
                if (ours.Contains(pid.Handle.ToString()) || pn.StartsWith(al.Name + "-지반", StringComparison.Ordinal) || pn.StartsWith("DH_측점체인", StringComparison.Ordinal)) continue;
                return $"이 노선에 진입도로 것이 아닌 계획 종단 «{pn}»이 있어 방향을 바꾸지 않습니다";
            }
            catch { }
        }
        return "";
    }

    /// <summary>시점 ↔ 종점 한 번(같은 트랜잭션 · 실패 = 물림). 돌려줌 = 한 줄(⚠로 시작 = 안 바꿈).</summary>
    internal static string Run(RoadSession s)
    {
        var doc = s.Doc; var db = doc.Database;
        bool was = s.Suppress; s.Suppress = true;
        try
        {
            using var dl = doc.LockDocument();
            using var tr = db.TransactionManager.StartTransaction();
            var al = (CivilDb.Alignment)tr.GetObject(s.AlignId, OpenMode.ForWrite);
            var pr = (CivilDb.Profile)tr.GetObject(s.PlanId, OpenMode.ForWrite);
            var d = RoadDesign.Read(tr, s.AlignId) ?? throw new InvalidOperationException("설계 기록이 없습니다");
            string no = Refusal(tr, s, al, d, pr);
            if (no.Length > 0) { tr.Abort(); return "⚠" + no; }
            double a0 = al.StartingStation, b0 = al.EndingStation;
            // ① 지금 값
            var marks = StationMarks.Load(tr, s.AlignId); var drops = StationMarks.LoadDropped(tr, s.AlignId);
            var now = new ProfileFlip.State
            {
                TierWalls = d.TierWalls.Select(w => (double[])w.Clone()).ToList(),
                Ditches = d.Ditches.Select(w => (double[])w.Clone()).ToList(),
                Marks = marks.Select(m => new ProfileFlip.Mark(m.Station, m.Why ?? "")).ToList(),
                Drops = drops.ToList(),
                Lr = new[] { d.HalfL, d.HalfR, d.ShoulderL, d.ShoulderR },
                A0 = a0, B0 = b0,   // ★[설치 전 검토 H1] 노선 길이가 사이에 바뀌면 되돌림 기록을 안 씀
            };
            foreach (CivilDb.ProfilePVI q in pr.PVIs) now.Pvis.Add(new ProfileFlip.Pvi(q.RawStation, q.Elevation));
            var (next, snap, exact) = ProfileFlip.Toggle(now, b0, a0, ReadSnap(tr, s.AlignId));
            // ② 옛 자리(XY) — 1 mm 확인용(구간 끝 · 측점 · PVI)
            var before = new List<(string What, double S, double X, double Y)>();
            void Mark(string w, double st) { if (double.IsFinite(st) && P(al, st) is { } p) before.Add((w, st, p.X, p.Y)); }
            foreach (var w in now.TierWalls) if (w.Length >= 2) { Mark("옹벽", w[0]); Mark("옹벽", w[1]); }
            foreach (var w in now.Ditches) if (w.Length >= 2) { Mark("측구", w[0]); Mark("측구", w[1]); }
            foreach (var m in now.Marks) Mark("수동 측점", m.S);
            foreach (double st in now.Drops) Mark("지운 측점", st);
            foreach (var q in now.Pvis) Mark("PVI", q.S);
            // ③ 노선 뒤집기 · 시점 측점 그대로(P1: Civil이 기준점을 새 시점으로 옮기고 측점 0 유지 — 다르면 우리가)
            al.Reverse();
            if (Math.Abs(al.StartingStation - a0) > 1e-9 && P(al, al.StartingStation) is { } sp) { al.ReferencePoint = new Point2d(sp.X, sp.Y); al.ReferencePointStation = a0; }
            if (Math.Abs(al.StartingStation - a0) > 1e-6 || Math.Abs(al.EndingStation - b0) > 1e-3)
                throw new InvalidOperationException($"뒤집은 노선 측점 {al.StartingStation:F3}~{al.EndingStation:F3}이 처음({a0:F3}~{b0:F3})과 다름");
            if (TestFailAfterReverse) throw new InvalidOperationException("시험 — 뒤집은 뒤 물림");
            // ④ 계획 종단 PVI(RoadEndFix와 같은 법 — 가운데 지우고 처음 · 끝 고쳐 넣기)
            var want = next.Pvis;
            if (want.Count < 2) throw new InvalidOperationException("계획 종단 PVI가 두 개보다 적음");
            for (int i = pr.PVIs.Count - 2; i >= 1; i--) pr.PVIs.RemoveAt(i);
            var first = pr.PVIs[0]; var last = pr.PVIs[pr.PVIs.Count - 1];
            double ns0 = want[0].S, nz0 = want[0].Z, ns1 = want[^1].S, nz1 = want[^1].Z;
            if (ns0 >= last.RawStation || (ns1 < last.RawStation && ns1 > first.RawStation)) { last.RawStation = ns1; first.RawStation = ns0; }
            else { first.RawStation = ns0; last.RawStation = ns1; }
            first.Elevation = nz0; last.Elevation = nz1;
            for (int k = 1; k + 1 < want.Count; k++) pr.PVIs.AddPVI(want[k].S, want[k].Z);
            if (pr.PVIs.Count != want.Count) throw new InvalidOperationException($"계획 종단 PVI 수가 다름({pr.PVIs.Count}/{want.Count})");
            // ⑤ 설계 기록(옹벽 · 측구 쪽 반대 · 차례 그대로 · 좌우 맞바꿈) · 측점
            // 바뀐 것이 없으면(기록 없음 · 좌우 같음) 설계 기록을 안 씀 — 옛 판 기록의 바이트를 그대로 둔다(쓰면 새 판 칸이 붙어 «두 번 = 처음»이 값만 같고 글자는 달라짐 · V113 RB 실측)
            bool designSame = now.TierWalls.Count == 0 && now.Ditches.Count == 0 && now.Lr.SequenceEqual(next.Lr);
            d.TierWalls = next.TierWalls; d.Ditches = next.Ditches;
            if (next.Lr.Length >= 4) { d.HalfL = next.Lr[0]; d.HalfR = next.Lr[1]; d.ShoulderL = next.Lr[2]; d.ShoulderR = next.Lr[3]; }
            if (!designSame) d.Write(tr, s.AlignId);
            if (marks.Count > 0 && !StationMarks.Save(tr, s.AlignId, next.Marks.Select(m => new StationMarks.Mark(m.S, m.Why)))) throw new InvalidOperationException("수동 측점을 못 옮김");
            if (drops.Count > 0 && !StationMarks.SaveDropped(tr, s.AlignId, next.Drops)) throw new InvalidOperationException("지운 측점을 못 옮김");
            WriteJson(tr, s.AlignId, SnapKey, ProfileFlip.ToJson(snap));
            // ⑥ 1 mm 확인 — 옛 자리 = 새 측점(B − s)의 자리 · PVI 높이(되읽기)
            double worst = 0;
            foreach (var b in before)
            {
                var p = P(al, ProfileFlip.Map(b.S, b0, a0));
                if (p == null) continue;
                double e = Math.Sqrt(Math.Pow(p.Value.X - b.X, 2) + Math.Pow(p.Value.Y - b.Y, 2));
                worst = Math.Max(worst, e);
                if (e > 1e-3) throw new InvalidOperationException($"{b.What} 측점 {b.S:F3}의 자리가 뒤집은 뒤 {e * 1000:F1} mm 어긋남 — 물림");
            }
            var back = new List<ProfileFlip.Pvi>(); foreach (CivilDb.ProfilePVI q in pr.PVIs) back.Add(new ProfileFlip.Pvi(q.RawStation, q.Elevation));
            for (int k = 0; k < want.Count; k++)
                if (Math.Abs(back[k].S - want[k].S) > 1e-6 || Math.Abs(back[k].Z - want[k].Z) > 1e-6) throw new InvalidOperationException($"PVI {k + 1} 되읽기 {back[k].S:F3}/{back[k].Z:F3} ≠ {want[k].S:F3}/{want[k].Z:F3} — 물림");
            tr.Commit();
            s.Design = d;
            string txt = $"시점 ↔ 종점 바꿈 — 측점 0이 반대쪽 끝(노선 {b0 - a0:F2} m) · PVI {want.Count} · 옹벽 기록 {next.TierWalls.Count} · 측구 기록 {next.Ditches.Count} · 측점 {next.Marks.Count} · 지운 측점 {next.Drops.Count}"
                       + $" · 자리 확인 {before.Count}곳 최대 {worst * 1000:F3} mm" + (exact ? " · 처음 값 그대로(되돌림 기록)" : "");
            try { DiagLog.Append($"\n[진입도로 방향] {s.Name} — {txt}"); } catch { }
            return txt;
        }
        catch (Exception ex)
        {
            try { DiagLog.Append($"\n[진입도로 방향] {s.Name} — 물림 — {ex}"); } catch { }
            return "⚠방향을 못 바꿈(아무것도 안 바뀜) — " + ex.Message;
        }
        finally { s.Suppress = was; }
    }
}
