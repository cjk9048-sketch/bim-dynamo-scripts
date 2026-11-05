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
    internal const string LayAlign = "DH도로-노선", LayProf = "DH도로-종단", LayView = "DH도로-종단뷰", LaySurf = "DH도로-지표면";

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
        // ★★[도로 마무리 ① · 계획 5.3 «만들 때» · JACK «종단 작성 단계부터 기존 선형 자체를 강제로 끝단에 맞춰서»] 평 사본을 부지 바닥 끝 · 본 도로 포장 끝에 맞춤(넘침 = 자름 · 20 m 안 못 미침 = 늘림)
        try { flat = AdjustEnds(db, flat, radius, log); }
        catch (Exception exE) { log.AppendLine("  ⚠도로 끝을 못 맞춤(그린 그대로) — " + exE.Message); try { DiagLog.Append("\n[진입도로 끝] 만들 때 못 맞춤 — " + exE); } catch { } }
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

    /// <summary>★★[도로 마무리 ① · 계획 5.3 «만들 때»] 노선을 만들기 앞 평 사본(LWPOLYLINE)의 끝 — 넘침 = 그 자리에서 자름(GetSplitCurves) · 못 미침(20 m 안) = 끝 꼭짓점을 옮김(곧게) 또는 꼭짓점을 더함(꺾어 — 곡선은 노선을 만들 때 직선 사이에 저절로).
    /// 돌려줌 = 평 사본 ID(잘랐으면 새 것). 바닥 · 다른 도로가 없으면 그대로.</summary>
    static ObjectId AdjustEnds(Database db, ObjectId flat, double radius, System.Text.StringBuilder log)
    {
        using var tr = db.TransactionManager.StartTransaction();
        if (tr.GetObject(flat, OpenMode.ForRead) is not Polyline pl) { tr.Commit(); return flat; }
        var floors = PadFloors.Load(db, tr, out _);
        var roads = new List<RoadEnds.Road>();
        foreach (var id in RoadRanges.Roads(tr, db)) { var dd = RoadDesign.Read(tr, id); var c = dd == null ? null : RoadRanges.Centre(tr, id, dd); if (c != null) roads.Add(c); }
        if (floors.Count == 0 && roads.Count == 0) { tr.Commit(); return flat; }
        double len = pl.Length;
        var S = new List<double>(); var X = new List<double>(); var Y = new List<double>();
        for (int k = 0; ; k++)
        {
            double s = Math.Min(k * 0.25, len);
            var p = pl.GetPointAtDist(s);
            if (S.Count > 0 && s - S[^1] < 1e-3) { S[^1] = s; X[^1] = p.X; Y[^1] = p.Y; }   // 끝 1 mm 안 토막은 앞 표본을 끝으로(방향이 흔들리지 않게)
            else { S.Add(s); X.Add(p.X); Y.Add(p.Y); }
            if (s >= len) break;
        }
        var sec = new RoadDesign().Section();
        const long Me = long.MaxValue;   // 새 도로 = 핸들이 가장 큼(가지 판정 «핸들 작은 것이 본»과 같은 차례)
        roads.Add(new RoadEnds.Road(Me, S, X, Y, sec.OuterL, sec.OuterR));
        // 바닥 높이는 여기서 안 씀(끝 PVI는 종단 초안이 바탕면에서 읽음 = 바닥 높이)
        var res = RoadEnds.Solve(roads, floors, (_, _) => null, null, new RoadEnds.Options { CurveR = Math.Max(3, radius) }).First(r => r.Handle == Me);
        foreach (var n in res.Notes) log.AppendLine("  [도로 끝] " + n);
        double a = res.Start.Kind is RoadEnds.Kind.Floor or RoadEnds.Kind.Branch && res.Start.Station > 0.01 ? res.Start.Station : 0;
        double b = res.Finish.Kind is RoadEnds.Kind.Floor or RoadEnds.Kind.Branch && res.Finish.Station < len - 0.01 ? res.Finish.Station : len;
        ObjectId outId = flat;
        if (a > 0 || b < len)
        {
            var pars = new DoubleCollection();
            if (a > 0) pars.Add(pl.GetParameterAtDistance(a));
            if (b < len) pars.Add(pl.GetParameterAtDistance(b));
            var pieces = pl.GetSplitCurves(pars);
            int keep = a > 0 ? 1 : 0;
            if (pieces.Count <= keep || pieces[keep] is not Polyline np) throw new InvalidOperationException("폴리선을 못 자름");
            np.LayerId = pl.LayerId;
            var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
            outId = ms.AppendEntity(np); tr.AddNewlyCreatedDBObject(np, true);
            for (int i = 0; i < pieces.Count; i++) if (i != keep) pieces[i].Dispose();
            pl.UpgradeOpen(); pl.Erase();
            pl = np;
            if (a > 0) log.AppendLine($"  [도로 끝] 시점을 {(res.Start.Kind == RoadEnds.Kind.Branch ? "본 도로 포장" : "부지 바닥")} 끝에서 잘랐습니다({a:F1} m)");
            if (b < len) log.AppendLine($"  [도로 끝] 종점을 {(res.Finish.Kind == RoadEnds.Kind.Branch ? "본 도로 포장" : "부지 바닥")} 끝에서 잘랐습니다({len - b:F1} m)");
        }
        else pl.UpgradeOpen();
        // 늘리기 — 끝 꼭짓점 옮김(직선 끝) · 아니면 꼭짓점 더함
        void Ext(RoadEnds.End e, bool atEnd)
        {
            if (e.Kind != RoadEnds.Kind.Extend || e.Ext == null) return;
            int n = pl.NumberOfVertices, i = atEnd ? n - 1 : 0;
            bool straightEnd = Math.Abs(pl.GetBulgeAt(atEnd ? n - 2 : 0)) < 1e-12;
            var q = new Point2d(e.Ext.Qx, e.Ext.Qy);
            if (e.Ext.Straight && straightEnd) pl.SetPointAt(i, q);
            else if (atEnd) pl.AddVertexAt(n, q, 0, 0, 0);
            else pl.AddVertexAt(0, q, 0, 0, 0);
            log.AppendLine($"  [도로 끝] {(atEnd ? "종점" : "시점")}을 부지 바닥 끝까지 {e.Ext.Length:F1} m {(e.Ext.Straight ? "늘렸습니다" : $"꺾어({e.Ext.BendDeg:F0}°) 늘렸습니다")}");
        }
        Ext(res.Finish, true); Ext(res.Start, false);
        tr.Commit();
        return outId;
    }

    /// <summary>★[v110.2] 직선 ↔ 직선 사이 곡선의 반경을 <paramref name="radius"/>로(접선이 짧으면 0.7배씩 3 m까지) — 곡선이 없던 꺾임에는 새로 넣음.
    /// 반경을 바로 못 바꾸는 곡선은 지우고 다시 넣는다. 돌려줌 = 결과 한 줄.</summary>
    internal static string ReCurve(Document doc, ObjectId aid, double radius)
    {
        var db = doc.Database;
        int changed = 0, reduced = 0, failed = 0, added = 0, kept = 0;
        using (var tr = db.TransactionManager.StartTransaction())
        {
            var al = (CivilDb.Alignment)tr.GetObject(aid, OpenMode.ForWrite);
            bool Fit(Func<double, bool> tryR)
            {
                double r = Math.Max(3, radius);
                while (r >= 3 - 1e-9)
                {
                    if (tryR(r)) { if (r < radius - 1e-6) reduced++; return true; }
                    r *= 0.7;
                }
                return false;
            }
            // 곡선(앞뒤가 직선) — 반경 바꾸기
            var arcs = new List<(int Prev, int Arc, int Next)>();
            var gaps = new List<(int A, int B)>();
            int n = al.Entities.Count;
            for (int i = 0; i < n; i++)
            {
                var e = al.Entities.GetEntityByOrder(i);
                if (e.EntityType == CivilDb.AlignmentEntityType.Arc && i > 0 && i + 1 < n)
                {
                    var p = al.Entities.GetEntityByOrder(i - 1); var q = al.Entities.GetEntityByOrder(i + 1);
                    if (p.EntityType == CivilDb.AlignmentEntityType.Line && q.EntityType == CivilDb.AlignmentEntityType.Line) arcs.Add((p.EntityId, e.EntityId, q.EntityId));
                }
                else if (e.EntityType == CivilDb.AlignmentEntityType.Line && i + 1 < n && al.Entities.GetEntityByOrder(i + 1).EntityType == CivilDb.AlignmentEntityType.Line)
                    gaps.Add((e.EntityId, al.Entities.GetEntityByOrder(i + 1).EntityId));
            }
            foreach (var (pv, ar, nx) in arcs)
            {
                var arc = (CivilDb.AlignmentArc)al.Entities.EntityAtId(ar);
                // ★[설치 전 검토 · 중간] 우리가 넣은 자유 곡선(Free)만 — 폴리선 볼록으로 그린 호(Fixed)는 «호는 그대로»
                if (arc.Constraint1 != CivilDb.AlignmentEntityConstraintType.Free) { kept++; continue; }
                double old = arc.Radius;
                if (Math.Abs(old - radius) < 1e-6) continue;
                // 바꾼 뒤 이웃 직선이 남아 있어야 맞는 곡선(반경이 접선보다 길면 예외 없이 겹친 노선이 될 수 있다 — 검토 확인 필요 항목)
                bool Valid(double r)
                {
                    try
                    {
                        var la = (CivilDb.AlignmentLine)al.Entities.EntityAtId(pv); var lb = (CivilDb.AlignmentLine)al.Entities.EntityAtId(nx);
                        return Math.Abs(arc.Radius - r) < 1e-6 && arc.Length > 1e-3 && la.Length > 0.01 && lb.Length > 0.01;
                    }
                    catch { return false; }
                }
                bool ok = Fit(r => { try { arc.Radius = r; return Valid(r); } catch { return false; } });
                // ★[설치 전 검토 · 중간] 못 맞추면 지우지 않고 원래 반경으로 되돌린다(지운 뒤 다시 못 넣으면 곡선이 사라진 채 저장됐다)
                if (!ok) { try { arc.Radius = old; } catch { } failed++; }
                else changed++;
            }
            foreach (var (a, b) in gaps)
            {
                // 거의 한 줄(꺾임 0.5° 안)이면 곡선 자리가 아니다 — 못 바꿈으로 세지 않음
                try
                {
                    var la = (CivilDb.AlignmentLine)al.Entities.EntityAtId(a); var lb = (CivilDb.AlignmentLine)al.Entities.EntityAtId(b);
                    double d1 = Math.Atan2(la.EndPoint.Y - la.StartPoint.Y, la.EndPoint.X - la.StartPoint.X), d2 = Math.Atan2(lb.EndPoint.Y - lb.StartPoint.Y, lb.EndPoint.X - lb.StartPoint.X);
                    double dd = Math.Abs(Math.IEEERemainder(d2 - d1, 2 * Math.PI));
                    if (dd < 0.5 * Math.PI / 180) continue;
                }
                catch { }
                bool ok = Fit(r => { try { al.Entities.AddFreeCurve(a, b, r, CivilDb.CurveParamType.Radius, false, CivilDb.CurveType.Compound); return true; } catch { return false; } });
                if (ok) added++; else failed++;
            }
            tr.Commit();
        }
        return $"곡선 반경 {radius:0.#} m — 바꿈 {changed}" + (added > 0 ? $" · 새로 {added}" : "") + (reduced > 0 ? $" · 접선이 짧아 줄임 {reduced}" : "")
            + (kept > 0 ? $" · 그린 호 그대로 {kept}" : "") + (failed > 0 ? $" · ⚠못 바꿈 {failed}(원래 반경)" : "");
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
        // ★[v113.3 · JACK 1105] 바탕면 높이가 없는 노선 끝 — 종단이 거기서 멈춘다(그 끝은 도로가 안 생김) · 알림만(끝 맞추기 [업데이트]가 종단 끝을 노선 끝으로 잇는다)
        try
        {
            using var trC = db.TransactionManager.StartTransaction();
            var alC = (CivilDb.Alignment)trC.GetObject(aid, OpenMode.ForRead);
            double ha = s[0] - alC.StartingStation, hb = alC.EndingStation - s[^1];
            if (ha > RoadEndFix.CoverTol) log.AppendLine($"  [도로 끝] ⚠시점 쪽 {ha:F1} m는 바탕면 높이가 없어 종단이 못 감 — [업데이트]가 노선 시점까지 잇습니다");
            if (hb > RoadEndFix.CoverTol) log.AppendLine($"  [도로 끝] ⚠종점 쪽 {hb:F1} m는 바탕면 높이가 없어 종단이 못 감 — [업데이트]가 노선 종점까지 잇습니다");
            trC.Commit();
        }
        catch { }
        // ★★[도로 마무리 ① · 계획 5.4 · 5.5 · 5.6] 끝 높이 고정 — 바닥 끝 = 바탕(정지면) 높이(= g 끝 그대로) · 가지 끝 = 본 도로 포장 높이 · 빈 구간 두 자리도 바닥 높이
        double zS = g[0], zE = g[^1]; RoadEnds.Result? ends = null;
        if (d.EndsOn)
        {
            try
            {
                using var tr = db.TransactionManager.StartTransaction();
                var tin = (CivilDb.TinSurface)tr.GetObject(baseSurf, OpenMode.ForRead);
                double? Fz(double x, double y) { try { double z = tin.FindElevationAtXY(x, y); return double.IsFinite(z) ? z : null; } catch { return null; } }
                ends = RoadRanges.Solve(tr, db, aid, PadFloors.Load(db, tr, out _), Fz, true, new RoadEnds.Options { CurveR = d.CurveR > 0 ? d.CurveR : 30 }, d);
                tr.Commit();
                if (ends != null && ends.Start.Kind == RoadEnds.Kind.Branch && double.IsFinite(ends.Start.Z)) zS = ends.Start.Z;
                if (ends != null && ends.Finish.Kind == RoadEnds.Kind.Branch && double.IsFinite(ends.Finish.Z)) zE = ends.Finish.Z;
            }
            catch (Exception exE) { log.AppendLine("  ⚠도로 끝 높이 판정 실패(바탕면 높이로) — " + exE.Message); }
        }
        var draft = WithStations(doc, RoadProfileDraft.Make(s, g, zS, zE, d.MaxGrade, d.MinPviLen, 0.3));
        if (ends != null && ends.Gaps.Count > 0 && draft.Pvi.Count >= 2)
        {
            draft.Pvi = RoadEnds.PinPvis(draft.Pvi, draft.Pvi[0].S, draft.Pvi[0].Z, draft.Pvi[^1].S, draft.Pvi[^1].Z, ends.Gaps);
            log.AppendLine($"  [도로 끝] 부지 바닥을 가로지름 — 빈 구간 {ends.Gaps.Count}(" + string.Join(", ", ends.Gaps.Select(q => $"{q.A:F1}~{q.B:F1} m")) + ") · 두 끝 높이 = 바닥 높이");
        }
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

    /// <summary>★★[v111.2 · JACK 1030 «종단상 직선구간이더라도 주측점엔 PVI점 — 사용자가 잡아끌 수 있게» · 인터뷰: 간격 = 도면 측점 간격] 초안 PVI에 주측점(측점 간격의 배수 = 종단도 No.)마다 PVI를 더함 — 모양 그대로.</summary>
    static RoadProfileDraft.Result WithStations(Document doc, RoadProfileDraft.Result draft)
    {
        GradingSettings.SyncToDocument(doc);   // 측점 간격은 도면 설정
        var (pvi, added) = RoadProfileDraft.AddStations(draft.Pvi, Math.Max(1.0, GradingSettings.XsecInterval));
        draft.Pvi = pvi; draft.StationAdded = added;
        return draft;
    }

    /// <summary>★[v111.2 · 전체 검토 H1] 우리가 만든 진입도로 노선인가(이름 «DH도로…» 또는 레이어 DH도로-노선) — 아니면 사람 노선(진입도로를 얹은 남의 Civil 노선): 이름으로 고르는 지우기 금지.</summary>
    internal static bool IsOurs(CivilDb.Alignment al)
        => (al.Name ?? "").StartsWith(AlignBase, StringComparison.Ordinal) || string.Equals(al.Layer, LayAlign, StringComparison.OrdinalIgnoreCase);

    /// <summary>계획 종단 초안을 다시(지금 PVI를 모두 지우고 새로) — 노선이 바뀌어 끝 측점이 달라졌을 때 · [종단 자동으로 다시](★[v111.7] 옛 [종단 초안 다시]).</summary>
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
        var draft = WithStations(doc, RoadProfileDraft.Make(s, g, g[0], g[^1], d.MaxGrade, d.MinPviLen, 0.3));
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
        => SampleRange(tr, aid, planProf, double.NaN, double.NaN);

    /// <summary>★[도로 마무리 ① · 계획 5.8] <see cref="Sample"/>을 도로 구간 [<paramref name="ra"/>, <paramref name="rb"/>]로(<see cref="RoadRanges"/> — 가상 자르기 · 빈 구간 토막) · NaN = 노선 ∩ 종단 그대로(비트 같음).
    /// 이름을 따로 둠 — 시험 도구가 <see cref="Sample"/>을 세 인자로 부름.</summary>
    internal static (double[] X, double[] Y, double[] Z, double S0, double S1, string Note) SampleRange(Transaction tr, ObjectId aid, ObjectId planProf, double ra, double rb)
    {
        var al = (CivilDb.Alignment)tr.GetObject(aid, OpenMode.ForRead);
        var pr = (CivilDb.Profile)tr.GetObject(planProf, OpenMode.ForRead);
        double s0 = Math.Max(al.StartingStation, pr.StartingStation), s1 = Math.Min(al.EndingStation, pr.EndingStation);
        if (double.IsFinite(ra)) s0 = Math.Max(s0, ra);
        if (double.IsFinite(rb)) s1 = Math.Min(s1, rb);
        var X = new List<double>(); var Y = new List<double>(); var Z = new List<double>();
        string note = "";
        if (pr.StartingStation > al.StartingStation + 0.5 || pr.EndingStation < al.EndingStation - 0.5)
            note = $"계획 종단이 노선 일부만 덮음({pr.StartingStation:F1}~{pr.EndingStation:F1} / 노선 {al.StartingStation:F1}~{al.EndingStation:F1}) — [종단 자동으로 다시]";
        double first = double.NaN, lastSc = double.NaN;   // ★[설치 전 검토 L3] Core 측점 0 = 실제 첫 표본(첫 측점이 실패하면 옹벽 · 제안 측점이 밀림)
        for (int k = 0; ; k++)
        {
            // ★[v111.2 · 전체 검토 L] 측점을 더해 가지 않고 곱으로(누적 반올림) · 끝 측점이 앞 표본과 1 mm 안이면 그 표본을 끝으로 바꿈 —
            //   길이 0 선분이 끼면 선 면이 «꺾임 10° 넘음»으로 도로 전체를 칸 면으로 물림
            double sc = Math.Min(s0 + k, s1);
            double x = 0, y = 0, z;
            try { al.PointLocation(sc, 0, ref x, ref y); z = pr.ElevationAt(sc); } catch { if (sc >= s1) break; continue; }
            if (double.IsFinite(z))
            {
                if (X.Count > 0 && sc - lastSc < 1e-3) { X[^1] = x; Y[^1] = y; Z[^1] = z; }
                else { X.Add(x); Y.Add(y); Z.Add(z); if (double.IsNaN(first)) first = sc; }
                lastSc = sc;
            }
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
