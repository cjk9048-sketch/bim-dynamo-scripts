using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using DH.Grading.Core;
using CivilApp = Autodesk.Civil.ApplicationServices;
using CivilDb = Autodesk.Civil.DatabaseServices;

namespace DH.Grading.Civil;

/// <summary>★★[v110 · 계획_v110.md 3.4] 진입도로 노선 · 종단 만들기 — 폴리선(또는 Civil 노선) → `DH도로_N`(곡선) · 바탕 종단 · <b>계획 종단 자동 초안</b>(레이아웃 — 사람이 PVI를 옮김) · 종단뷰.
/// <para>★이름 · 레이어는 부지와 따로(`DH도로` · `DH도로-…`) — 부지 종단 «지우고 새로»(`DH선형*`) · 초기화(«DH-» 레이어)에 안 걸린다(계획 검토 M8).</para></summary>
internal static class RoadBuilder
{
    internal const string AlignBase = "DH도로";
    internal const string LayAlign = "DH도로-노선", LayProf = "DH도로-종단", LayView = "DH도로-종단뷰";

    /// <summary>바탕면 — 정지면_DH(부지 정지가 있으면 도로가 그 위에 얹힘) · 없으면 원지반.
    /// <para>★[설치 전 검토 · 중간] 정지면은 종단 · 횡단과 같은 규칙(`정지면_DH` 또는 `정지면_DH_N` · 마지막 것) — 정확히 같은 이름만 보면 `_N`일 때 원지반으로 떨어진다.</para></summary>
    internal static (ObjectId Id, string Name) PickBase(Database db, CivilApp.CivilDocument cdoc)
    {
        ObjectId pad = ObjectId.Null, ground = ObjectId.Null; string padNm = "", groundNm = "";
        using var tr = db.TransactionManager.StartTransaction();
        foreach (ObjectId sid in cdoc.GetSurfaceIds())
        {
            try
            {
                if (tr.GetObject(sid, OpenMode.ForRead) is not CivilDb.TinSurface s) continue;
                if (Commands.SectionCommand.IsBase(s.Name, Commands.SectionCommand.PadSurfaceBase)) { pad = sid; padNm = s.Name; }
                else if (ground.IsNull && (s.Name == "원지반" || s.Name.StartsWith("원지반", StringComparison.Ordinal))) { ground = sid; groundNm = s.Name; }
            }
            catch { }
        }
        tr.Commit();
        return !pad.IsNull ? (pad, padNm) : (ground, groundNm);
    }

    /// <summary>폴리선 → 노선. 호(bulge)는 그대로 · 직선끼리 꺾인 곳은 반경 R 곡선(접선이 짧으면 반경을 0.7배씩 줄여 3 m까지 · 그래도 안 되면 꺾인 채 두고 알림).</summary>
    internal static ObjectId AlignmentFromPolyline(Document doc, ObjectId plId, double radius, System.Text.StringBuilder log)
    {
        var db = doc.Database;
        var cdoc = CivilApp.CivilDocument.GetCivilDocument(db);
        ObjectId lay = Commands.SectionCommand.EnsureLayerStandalone(db, LayAlign, 1);
        ObjectId flat = Commands.SectionCommand.MakeFlatCopy(db, plId, lay, out int nv, out double len);
        if (flat.IsNull) throw new InvalidOperationException("폴리선을 읽지 못했습니다(점이 두 개보다 적거나 길이 0)");
        string name = Commands.SectionCommand.UniqueName(db, cdoc, AlignBase);
        ObjectId aid;
        try
        {
            aid = CivilDb.Alignment.Create(cdoc, new CivilDb.PolylineOptions { PlineId = flat, EraseExistingEntities = true, AddCurvesBetweenTangents = false },
                name, ObjectId.Null, lay,
                Commands.SectionCommand.PickStyle(db, cdoc.Styles.AlignmentStyles, "기본", "Standard", "Basic"),
                Commands.SectionCommand.PickStyle(db, cdoc.Styles.LabelSetStyles.AlignmentLabelSetStyles, "_없음", "None", "표준", "Standard"));
        }
        catch { Commands.SectionCommand.EraseQuiet(db, flat); throw; }
        log.AppendLine($"[진입도로] 노선 '{name}' · 폴리선 점 {nv} · {len:F1} m");
        // 직선 ↔ 직선 이음매에 곡선
        int curves = 0, reduced = 0, failed = 0;
        using (var tr = db.TransactionManager.StartTransaction())
        {
            var al = (CivilDb.Alignment)tr.GetObject(aid, OpenMode.ForWrite);
            var pairs = new List<(int A, int B)>();
            int n = al.Entities.Count;
            for (int i = 0; i + 1 < n; i++)
            {
                var a = al.Entities.GetEntityByOrder(i); var b = al.Entities.GetEntityByOrder(i + 1);
                if (a.EntityType == CivilDb.AlignmentEntityType.Line && b.EntityType == CivilDb.AlignmentEntityType.Line) pairs.Add((a.EntityId, b.EntityId));
            }
            foreach (var (pa, pb) in pairs)
            {
                double r = Math.Max(3, radius);
                bool ok = false;
                while (!ok && r >= 3 - 1e-9)
                {
                    try { al.Entities.AddFreeCurve(pa, pb, r, CivilDb.CurveParamType.Radius, false, CivilDb.CurveType.Compound); ok = true; }
                    catch { r *= 0.7; }
                }
                if (ok) { curves++; if (r < radius - 1e-6) reduced++; } else failed++;
            }
            tr.Commit();
        }
        log.AppendLine($"  곡선 {curves}개(R {radius:F0} m" + (reduced > 0 ? $" · 접선이 짧아 반경 줄임 {reduced}" : "") + ")" + (failed > 0 ? $" · ⚠곡선 못 넣은 꺾임 {failed}" : ""));
        return aid;
    }

    /// <summary>바탕 종단(면에서) + 계획 종단(레이아웃 · 자동 초안 PVI). 돌려줌 = (바탕, 계획, 초안 결과).</summary>
    internal static (ObjectId Base, ObjectId Plan, RoadProfileDraft.Result Draft) MakeProfiles(Document doc, ObjectId aid, ObjectId baseSurf, RoadDesign d, System.Text.StringBuilder log)
    {
        var db = doc.Database;
        var cdoc = CivilApp.CivilDocument.GetCivilDocument(db);
        ObjectId lay = Commands.SectionCommand.EnsureLayerStandalone(db, LayProf, 6);
        string an;
        using (var tr = db.TransactionManager.StartTransaction()) { an = ((CivilDb.Alignment)tr.GetObject(aid, OpenMode.ForRead)).Name; tr.Commit(); }
        var labels = Commands.SectionCommand.PickStyle(db, cdoc.Styles.LabelSetStyles.ProfileLabelSetStyles, "_없음", "None", "표준", "Standard");
        var gStyle = Commands.SectionCommand.PickStyle(db, cdoc.Styles.ProfileStyles, "기존", "Existing", "지반", "기본", "Standard");
        var pStyle = Commands.SectionCommand.PickStyle(db, cdoc.Styles.ProfileStyles, "설계", "Design", "계획", "기본", "Standard");
        ObjectId bp = CivilDb.Profile.CreateFromSurface(an + "-바탕", aid, baseSurf, lay, gStyle, labels);
        // 바탕 종단을 1 m마다 읽어 초안
        var s = new List<double>(); var g = new List<double>();
        using (var tr = db.TransactionManager.StartTransaction())
        {
            var pr = (CivilDb.Profile)tr.GetObject(bp, OpenMode.ForRead);
            var al = (CivilDb.Alignment)tr.GetObject(aid, OpenMode.ForRead);
            double s0 = Math.Max(al.StartingStation, pr.StartingStation), s1 = Math.Min(al.EndingStation, pr.EndingStation);
            for (double st = s0; st <= s1 + 1e-9; st += 1.0)
            {
                double z;
                try { z = pr.ElevationAt(Math.Min(st, s1)); } catch { continue; }
                if (double.IsFinite(z)) { s.Add(Math.Min(st, s1)); g.Add(z); }
            }
            if (s.Count > 0 && s[^1] < s1 - 1e-6) { try { double z = pr.ElevationAt(s1); if (double.IsFinite(z)) { s.Add(s1); g.Add(z); } } catch { } }
            tr.Commit();
        }
        if (s.Count < 2) throw new InvalidOperationException("바탕면에서 종단을 읽지 못했습니다(노선이 면 밖?)");
        var draft = RoadProfileDraft.Make(s, g, g[0], g[^1], d.MaxGrade, d.MinPviLen, 0.3);
        ObjectId pp = CivilDb.Profile.CreateByLayout(an + "-계획", aid, lay, pStyle, labels);
        using (var tr = db.TransactionManager.StartTransaction())
        {
            var pr = (CivilDb.Profile)tr.GetObject(pp, OpenMode.ForWrite);
            foreach (var (st, z) in draft.Pvi) pr.PVIs.AddPVI(st, z);
            tr.Commit();
        }
        log.AppendLine($"  바탕 '{an}-바탕' · 계획 '{an}-계획' — {draft.Text}" + (draft.EndUnreachable
            ? $" · 종점까지 {Math.Abs(g[^1] - g[0]) / Math.Max(1e-6, d.MaxGrade) - (s[^1] - s[0]):F0} m 더 길어야 함" : ""));
        return (bp, pp, draft);
    }

    /// <summary>계획 종단 초안을 다시(지금 PVI를 모두 지우고 새로) — 노선이 바뀌어 끝 측점이 달라졌을 때 · [종단 초안 다시].</summary>
    /// <para>★[재검토 열린 질문] 바탕은 «지금» 바탕면(세션이 이름으로 다시 찾은 것)에서 직접 읽는다 — 처음 만든 «-바탕» 종단은 재정지 · 초기화 뒤 옛 면을 따라갈 수 있다.</para>
    internal static RoadProfileDraft.Result Redraft(Document doc, ObjectId aid, IGroundSurface baseSurf, ObjectId planProf, RoadDesign d)
    {
        var db = doc.Database;
        var s = new List<double>(); var g = new List<double>();
        using var tr = db.TransactionManager.StartTransaction();
        var al = (CivilDb.Alignment)tr.GetObject(aid, OpenMode.ForRead);
        double s0 = al.StartingStation, s1 = al.EndingStation;
        for (double st = s0; ; st += 1.0)
        {
            double sc = Math.Min(st, s1), x = 0, y = 0;
            try { al.PointLocation(sc, 0, ref x, ref y); if (baseSurf.TryGetElevation(x, y, out double z) && double.IsFinite(z)) { s.Add(sc); g.Add(z); } } catch { }
            if (sc >= s1) break;
        }
        if (s.Count < 2) throw new InvalidOperationException("바탕면에서 노선을 따라 높이를 읽지 못했습니다(노선이 면 밖?)");
        var draft = RoadProfileDraft.Make(s, g, g[0], g[^1], d.MaxGrade, d.MinPviLen, 0.3);
        var pr = (CivilDb.Profile)tr.GetObject(planProf, OpenMode.ForWrite);
        // ★★[설치 전 검토 · 중간 → 화면 없는 Civil 실측 1028] 처음 · 끝 PVI는 지울 수 없고(«Could not remove the first or the last PVI»),
        //   요소를 다 비운 종단에는 AddPVI가 안 들어간다(커밋 뒤에도 «Value does not fall within the expected range»).
        //   → 가운데 PVI만 지우고 · 양 끝을 새 측점 · 높이로 옮기고(RawStation · Elevation 쓰기 됨) · 안쪽을 더한다. 수가 안 맞으면 되돌림(커밋 안 함).
        if (pr.PVIs.Count < 2) throw new InvalidOperationException("계획 종단이 비었습니다 — [진입도로]로 폴리선부터 다시");
        if (draft.Pvi.Count < 2) throw new InvalidOperationException("초안 PVI가 두 개보다 적습니다");
        for (int i = pr.PVIs.Count - 2; i >= 1; i--) pr.PVIs.RemoveAt(i);
        var (ns0, nz0) = draft.Pvi[0]; var (ns1, nz1) = draft.Pvi[^1];
        var first = pr.PVIs[0]; var last = pr.PVIs[pr.PVIs.Count - 1];
        // 옮기는 차례 — 서로 넘지 않게: 새 시점이 옛 끝 뒤거나, 끝이 앞으로 오면(옛 시점 뒤) 끝 먼저(★[재검토 낮음 5] 범위가 안 겹칠 때도)
        if (ns0 >= last.RawStation || (ns1 < last.RawStation && ns1 > first.RawStation)) { last.RawStation = ns1; first.RawStation = ns0; }
        else { first.RawStation = ns0; last.RawStation = ns1; }
        first.Elevation = nz0; last.Elevation = nz1;
        for (int k = 1; k + 1 < draft.Pvi.Count; k++) pr.PVIs.AddPVI(draft.Pvi[k].S, draft.Pvi[k].Z);
        if (pr.PVIs.Count != draft.Pvi.Count) throw new InvalidOperationException($"계획 종단 PVI 수가 초안과 다릅니다({pr.PVIs.Count}/{draft.Pvi.Count}) — 바꾸지 않음");
        tr.Commit();
        return draft;
    }

    internal static ObjectId MakeView(Document doc, ObjectId aid, Point3d at)
    {
        var db = doc.Database;
        ObjectId vid = CivilDb.ProfileView.Create(aid, at);
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            var lay = Commands.SectionCommand.EnsureLayer(db, tr, LayView, 7);
            ((Entity)tr.GetObject(vid, OpenMode.ForWrite)).LayerId = lay;
            tr.Commit();
        }
        catch { }
        return vid;
    }

    /// <summary>노선 중심선 표본(1 m마다 + 끝) · 계획 종단 높이 → Core 입력. 종단 범위 밖 측점은 뺀다.</summary>
    internal static (double[] X, double[] Y, double[] Z, double S0, double S1, string Note) Sample(Transaction tr, ObjectId aid, ObjectId planProf)
    {
        var al = (CivilDb.Alignment)tr.GetObject(aid, OpenMode.ForRead);
        var pr = (CivilDb.Profile)tr.GetObject(planProf, OpenMode.ForRead);
        double s0 = Math.Max(al.StartingStation, pr.StartingStation), s1 = Math.Min(al.EndingStation, pr.EndingStation);
        var X = new List<double>(); var Y = new List<double>(); var Z = new List<double>();
        string note = "";
        if (pr.StartingStation > al.StartingStation + 0.5 || pr.EndingStation < al.EndingStation - 0.5)
            note = $"계획 종단이 노선 일부만 덮음({pr.StartingStation:F1}~{pr.EndingStation:F1} / 노선 {al.StartingStation:F1}~{al.EndingStation:F1}) — [종단 초안 다시]";
        double first = double.NaN;                     // ★[설치 전 검토 L3] Core 측점 0 = 실제 첫 표본(첫 측점이 실패하면 옹벽 · 제안 측점이 밀림)
        for (double st = s0; ; st += 1.0)
        {
            double sc = Math.Min(st, s1);
            double x = 0, y = 0, z;
            try { al.PointLocation(sc, 0, ref x, ref y); z = pr.ElevationAt(sc); } catch { if (sc >= s1) break; continue; }
            if (double.IsFinite(z)) { X.Add(x); Y.Add(y); Z.Add(z); if (double.IsNaN(first)) first = sc; }
            if (sc >= s1) break;
        }
        return (X.ToArray(), Y.ToArray(), Z.ToArray(), double.IsNaN(first) ? s0 : first, s1, note);
    }

    /// <summary>계획 종단 PVI 사이 최대 경사(절댓값) — 사람이 PVI를 옮긴 뒤 한도를 넘었는지 창에 보인다.</summary>
    internal static double PlanGrade(Transaction tr, ObjectId planProf)
    {
        var pr = (CivilDb.Profile)tr.GetObject(planProf, OpenMode.ForRead);
        double g = 0;
        for (int i = 1; i < pr.PVIs.Count; i++)
        {
            var a = pr.PVIs[i - 1]; var b = pr.PVIs[i];
            double ds = b.RawStation - a.RawStation;
            if (ds > 1e-6) g = Math.Max(g, Math.Abs(b.Elevation - a.Elevation) / ds);
        }
        return g;
    }
}
