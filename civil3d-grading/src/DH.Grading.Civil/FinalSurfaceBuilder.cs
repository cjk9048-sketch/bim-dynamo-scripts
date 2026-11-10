using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using DH.Grading.Core;
using NetTopologySuite.Geometries;
using CivilDb = Autodesk.Civil.DatabaseServices;
using Tri = DH.Grading.Core.WallDaylight.Tri;

namespace DH.Grading.Civil;

/// <summary>★★★[v111.8 · 계획_v111_8.md 2 ~ 5장 · JACK 1102 «최종지표면 = 정지면_DH 위에 진입도로»] <b>최종지표면_DH</b> — 정지면_DH 세모 위에 진입도로 면들을 얹은 한 장(Core <see cref="FinalSurface.Compose"/>).
/// <list type="bullet">
/// <item>LandXML(늘 새 파일) → Civil 면 → 되읽기 관문 → 바로 굳힘 — 소스가 없는 면(붙여넣기 · RebuildSnapshot 안 부름 · 1013 · 1012 실측 메모).</item>
/// <item>짓는 자리는 <see cref="RoadSession.MakeSurface"/> 뒤 한 곳(+ [업데이트]의 «최종면만»). [3D 보기] · DHINFRA는 도장이 맞으면 그 면 · 아니면 메모리에서 같은 합치기(도면 안 바꿈).</item>
/// <item>도장(면 확장 사전 «DH_FINAL») [0] 입력 지문(정지면_DH 핸들 + 지문 · 도로마다 노선 · 도로 면 핸들 + 지문 · ε · 판) [1] 시각 · 판 · 세모 · Δv · 가파른 띠 [2] 실제로 쓴 세모 해시(진단) [3] ⚠ 글.</item>
/// </list></summary>
internal static class FinalSurfaceBuilder
{
    internal const string Name = "최종지표면_DH";
    internal const string TempName = "최종지표면_짓는중_DH";
    /// <summary>남이 붙여넣기 소스 · 체적면으로 쓰는 옛 최종면 — 지우지 않고 이 이름(_N)으로 바꿔 숨김(L10).</summary>
    internal const string OldName = "최종지표면_DH(옛)";
    const string StampKey = "DH_FINAL";
    const string XmlKind = "최종지표면", XmlSurf = "FINAL";
    /// <summary>화면 없는 Civil 시험 고리 — 참이면 Civil 관문을 일부러 실패(V85 fail).</summary>
#pragma warning disable CS0649   // 시험(반사)만 씀
    internal static bool FailGateForTest;
#pragma warning restore CS0649

    internal enum State { NoRoads, NoBase, Fresh, Stale, Missing }

    /// <summary>도로 하나 — 노선 · 도로 면(우리 것) · 설계.</summary>
    internal sealed class Road
    {
        public ObjectId Aid, SurfId;
        public string Name = "";
        public RoadDesign D = null!;
        public string P = "";
    }

    /// <summary>입력 — 정지면_DH(하나일 때만) · 도로들(노선 핸들 차례 — L8) · 입력 지문 [0].</summary>
    sealed class Inputs
    {
        public ObjectId BaseId = ObjectId.Null;
        public string BaseWhy = "";
        public string BaseP = "";
        public readonly List<Road> Roads = new();
        public string Stamp0 = "";
    }

    /// <summary>우리 진입도로 면인가(레이어 DH도로-지표면 · 설명 «DH 진입도로 지표면» · 작업 면 이름).</summary>
    internal static bool IsRoadSurface(CivilDb.Surface s)
    {
        string desc = ""; try { desc = s.Description ?? ""; } catch { }
        return string.Equals(s.Layer, RoadBuilder.LaySurf, StringComparison.OrdinalIgnoreCase) || desc.StartsWith("DH 진입도로 지표면", StringComparison.Ordinal)
            || s.Name == RoadSession.TempSurf;
    }

    /// <summary>★[M5] 원지반으로 고르면 안 되는 우리 면 — 최종지표면 · 짓는중 · 옛 최종면 · 진입도로 면.</summary>
    internal static bool IsOurs(CivilDb.Surface s)
    {
        string nm = s.Name ?? "";
        return Commands.SectionCommand.IsBase(nm, Name) || Commands.SectionCommand.IsBase(nm, TempName) || SurfaceRetire.IsRetired(s) || IsRoadSurface(s);   // ★[v111.10] 물러난 면은 표식으로(이름 꼴 말고)
    }

    /// <summary>★[M5] 원지반 찍기에서 우리 면을 골랐으면 한 줄 알리고 참(다시 묻기) — DHGRADE · DHEXCAV.</summary>
    internal static bool RejectPick(Database db, Autodesk.AutoCAD.EditorInput.Editor ed, ObjectId id)
    {
        bool ours = false;
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            ours = tr.GetObject(id, OpenMode.ForRead) is CivilDb.Surface s && IsOurs(s);
            tr.Commit();
        }
        catch { }
        if (ours) ed.WriteMessage("\n우리가 만든 면입니다(최종지표면 · 진입도로) — 원지반을 고르세요.");
        return ours;
    }

    /// <summary>★[v111.8 · 검토 M3] 면 지문(상태 판정 — 싸게: Civil이 든 통계 한 번 + 정의 줄 수) — 세모 · 점 · 표고 최저/최고/평균 · 세모 변 · 넓이 최소/최대(비트 그대로) · 정의 줄 수.
    /// 사람 편집(변 뒤집기 · 점 옮김 · 더함 · 지움 — 정의에 줄이 붙음)과 수가 같은 다시 짓기를 가른다. <c>RevisionNumber</c>는 늘 0이라 안 씀(1103 계측).
    /// 세모 하나하나까지는 여기서 안 봄(25만 세모 읽기 0.3초) — [3D 보기] · DHINFRA는 세모를 읽는 김에 도장 [2] 해시와 견준다(<see cref="PickTerrain"/>).</summary>
    static string Sig(CivilDb.Surface s)
    {
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        string tin = "-", gen = "-", ops = "-";
        try { if (s is CivilDb.TinSurface ts) { var t = ts.GetTinProperties(); tin = string.Create(ci, $"{t.NumberOfTriangles} {t.MinimumTriangleLength:R}/{t.MaximumTriangleLength:R} {t.MinimumTriangleArea:R}/{t.MaximumTriangleArea:R}"); } } catch { }
        try { var g = s.GetGeneralProperties(); gen = string.Create(ci, $"{g.NumberOfPoints} {g.MinimumElevation:R}/{g.MaximumElevation:R}/{g.MeanElevation:R}"); } catch { }
        try { ops = s.Operations.Count.ToString(ci); } catch { }
        return $"세모 {tin} · 점 {gen} · 정의 {ops}";
    }

    static Inputs Gather(Database db, Transaction tr)
    {
        var I = new Inputs();
        var cdoc = Autodesk.Civil.ApplicationServices.CivilDocument.GetCivilDocument(db);
        var (ids, nms) = Commands.WallCompositeCommand.Candidates(cdoc, tr, Commands.WallCompositeCommand.PlanName, Commands.SectionCommand.GroundOf(db));
        if (ids.Count == 1) { I.BaseId = ids[0]; I.BaseP = Sig((CivilDb.Surface)tr.GetObject(ids[0], OpenMode.ForRead)); }
        else I.BaseWhy = ids.Count == 0 ? "정지면_DH 없음" : $"정지면_DH가 여럿({string.Join(",", nms)})";
        foreach (ObjectId aid in cdoc.GetAlignmentIds())
        {
            try
            {
                if (aid.IsErased || !RoadDesign.Has(tr, aid)) continue;
                var d = RoadDesign.Read(tr, aid);
                if (d == null || d.SurfaceHandle.Length == 0) continue;
                if (!db.TryGetObjectId(new Handle(Convert.ToInt64(d.SurfaceHandle, 16)), out var sid) || sid.IsErased) continue;
                if (tr.GetObject(sid, OpenMode.ForRead) is not CivilDb.TinSurface ts || !IsRoadSurface(ts)) continue;
                I.Roads.Add(new Road { Aid = aid, SurfId = sid, Name = ((CivilDb.Alignment)tr.GetObject(aid, OpenMode.ForRead)).Name ?? "", D = d, P = Sig(ts) + " · 지문 " + d.SurfaceSig });
            }
            catch { }
        }
        I.Roads.Sort((a, b) => a.Aid.Handle.Value.CompareTo(b.Aid.Handle.Value));   // ★[L8] 먼저 만든 노선 먼저(Core도 같은 차례로 다시 줄 세움)
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        I.Stamp0 = string.Join("|", new[] { "판 " + FinalSurface.Version, "ε " + new FinalSurface.Options().Eps.ToString("R", ci), I.BaseId.IsNull ? "-" : I.BaseId.Handle + " " + I.BaseP }
                                    .Concat(I.Roads.Select(r => $"{r.Aid.Handle}:{r.SurfId.Handle} {r.P}")));
        return I;
    }

    /// <summary>최종지표면 후보(이름 = 본 이름 또는 _N).</summary>
    static List<ObjectId> Finals(Database db, Transaction tr, string baseName)
    {
        var res = new List<ObjectId>();
        var cdoc = Autodesk.Civil.ApplicationServices.CivilDocument.GetCivilDocument(db);
        foreach (ObjectId sid in cdoc.GetSurfaceIds())
        {
            if (sid.IsNull || sid.IsErased) continue;
            try { if (tr.GetObject(sid, OpenMode.ForRead) is CivilDb.Surface s && Commands.SectionCommand.IsBase(s.Name ?? "", baseName)) res.Add(sid); } catch { }
        }
        return res;
    }

    static string[] ReadStamp(Transaction tr, DBObject o)
    {
        try
        {
            if (o.ExtensionDictionary.IsNull) return Array.Empty<string>();
            if (tr.GetObject(o.ExtensionDictionary, OpenMode.ForRead) is not DBDictionary d || !d.Contains(StampKey)) return Array.Empty<string>();
            if (tr.GetObject(d.GetAt(StampKey), OpenMode.ForRead) is not Xrecord xr || xr.Data == null) return Array.Empty<string>();
            return xr.Data.AsArray().Select(v => v.Value as string ?? "").ToArray();
        }
        catch { return Array.Empty<string>(); }
    }

    static void WriteStamp(Transaction tr, DBObject o, params string[] vals)
    {
        if (!o.IsWriteEnabled) o.UpgradeOpen();
        if (o.ExtensionDictionary.IsNull) o.CreateExtensionDictionary();
        var d = (DBDictionary)tr.GetObject(o.ExtensionDictionary, OpenMode.ForWrite);
        var xr = new Xrecord { Data = new ResultBuffer(vals.Select(v => new TypedValue((int)DxfCode.Text, v ?? "")).ToArray()) };
        if (d.Contains(StampKey)) tr.GetObject(d.GetAt(StampKey), OpenMode.ForWrite).Erase();
        d.SetAt(StampKey, xr);
        tr.AddNewlyCreatedDBObject(xr, true);
    }

    /// <summary>지금 상태 — 도로 없음 · 정지면_DH 하나 아님 · 맞음(도장 [0] = 지금 입력 · 그 면 <paramref name="fresh"/>) · 옛것 · 없음. 읽기만(핸들 + 지문 — 싸다).</summary>
    internal static State StateOf(Database db, Transaction tr, out ObjectId fresh, out string stamp0)
    {
        fresh = ObjectId.Null;
        var I = Gather(db, tr);
        stamp0 = I.Stamp0;
        if (I.Roads.Count == 0) return State.NoRoads;
        if (I.BaseId.IsNull) return State.NoBase;
        var fs = Finals(db, tr, Name);
        foreach (var id in fs)
        {
            var st = ReadStamp(tr, tr.GetObject(id, OpenMode.ForRead));
            if (st.Length > 0 && st[0] == I.Stamp0) { fresh = id; return State.Fresh; }
        }
        return fs.Count > 0 ? State.Stale : State.Missing;
    }

    internal static State StateOf(Database db, out string stamp0)
    {
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            var s = StateOf(db, tr, out _, out stamp0);
            tr.Commit();
            return s;
        }
        catch { stamp0 = ""; return State.NoRoads; }
    }

    /// <summary>«결과만 보기»에서 켤 계획면 이름 — 맞는 최종면이 있으면 «최종지표면_DH»(그 면의 이름) · 아니면 «정지면_DH».</summary>
    internal static string ResultName(Database db, Transaction tr)
    {
        try
        {
            if (StateOf(db, tr, out var id, out _) == State.Fresh && tr.GetObject(id, OpenMode.ForRead) is CivilDb.Surface s) return s.Name;
        }
        catch { }
        return Commands.WallCompositeCommand.PlanName;
    }

    /// <summary>★[계획 4장] 계획부지 다시 · 옹벽 합성 끝 알림 한 줄 — 우리 진입도로 면이 있을 때만(도로 없는 도면은 빈 글 = 알림 그대로).</summary>
    internal static string SiteChangedNote(Database db)
    {
        return AnyRoads(db) ? "⚠계획부지가 바뀌어 진입도로 지표면 · 최종지표면_DH가 옛것입니다 — 진입도로 창 [업데이트]" : "";
    }

    /// <summary>★[v111.8 · 검토 L1] 도면에 우리 진입도로 면이 살아 있나(설계 기록의 면 핸들 + 레이어 · 설명) — 면 통계는 안 읽음(싸다) · 예외면 거짓.</summary>
    internal static bool AnyRoads(Database db)
    {
        try
        {
            var cdoc = Autodesk.Civil.ApplicationServices.CivilDocument.GetCivilDocument(db);
            using var tr = db.TransactionManager.StartTransaction();
            bool any = false;
            foreach (ObjectId aid in cdoc.GetAlignmentIds())
            {
                try
                {
                    if (aid.IsErased || !RoadDesign.Has(tr, aid)) continue;
                    var d = RoadDesign.Read(tr, aid);
                    if (d == null || d.SurfaceHandle.Length == 0) continue;
                    if (db.TryGetObjectId(new Handle(Convert.ToInt64(d.SurfaceHandle, 16)), out var sid) && !sid.IsErased
                        && tr.GetObject(sid, OpenMode.ForRead) is CivilDb.TinSurface ts && IsRoadSurface(ts)) { any = true; break; }
                }
                catch { }
            }
            tr.Commit();
            return any;
        }
        catch { return false; }
    }

    /// <summary>★[L10] 다른 면 정의가 붙여넣기 소스로 쓰거나 체적면이 쓰나 — 그 면 이름(<paramref name="who"/>).</summary>
    internal static bool ReferencedElsewhere(Database db, Transaction tr, ObjectId id, out string who)
    {
        who = "";
        var cdoc = Autodesk.Civil.ApplicationServices.CivilDocument.GetCivilDocument(db);
        foreach (ObjectId sid in cdoc.GetSurfaceIds())
        {
            if (sid == id || sid.IsNull || sid.IsErased) continue;
            string nm = "?";
            try
            {
                if (tr.GetObject(sid, OpenMode.ForRead) is not CivilDb.Surface s) continue;
                try { nm = s.Name ?? "?"; } catch { }
                // 체적면(TIN · 격자) — 기준 · 비교 면
                CivilDb.VolumeSurfaceProperties? vp = s is CivilDb.TinVolumeSurface tv ? tv.GetVolumeProperties() : s is CivilDb.GridVolumeSurface gv ? gv.GetVolumeProperties() : null;
                if (vp != null)
                {
                    if (vp.BaseSurface == id || vp.ComparisonSurface == id) { who = nm; return true; }
                    continue;
                }
                var oc = s.Operations;
                for (int i = 0; i < oc.Count; i++)
                {
                    using var op = oc[i];
                    if (op is CivilDb.SurfaceOperationPasteSurface ps && ps.SurfaceId == id) { who = nm; return true; }
                }
            }
            // ★[v111.8 · 검토 M2] 못 읽은 면은 «쓴다»로 본다 — 지우면 그 면이 매달린 소스를 물 수 있다(이름만 바꿔 남김)
            catch { who = nm + "(못 읽음)"; return true; }
        }
        return false;
    }

    /// <summary>★[v111.8 · 검토 M1] 이름만 바꿔 남긴 옛 최종면(«최종지표면_DH(옛)» · «…_N»)인가 — XML 면 판정 · 굳은 면 지문이 쓴다.</summary>
    internal static bool IsOldName(string nm) => Commands.SectionCommand.IsBase(nm ?? "", OldName);

    /// <summary>옛 최종면 치우기 — 남이 쓰면 «최종지표면_DH(옛)_N»으로 이름 바꿔 숨김 · 아니면 지움(<paramref name="keep"/>는 뺌). 돌려줌 = 한 줄(빈 글 = 한 일 없음).</summary>
    internal static string RetireOld(Database db, Transaction tr, ObjectId keep) => RetireOld(db, tr, keep, out _);

    internal static string RetireOld(Database db, Transaction tr, ObjectId keep, out int erased)
    {
        // ★[v111.10 · 계획_초기화.md 3.1] 물러남 규칙 한 곳(<see cref="SurfaceRetire.Apply"/>) — 이름 «최종지표면_DH(옛)»(_N) · 표식 · 레이어 «옛면_DH» · 설명 · 숨김
        var R = SurfaceRetire.Apply(db, tr, Finals(db, tr, Name).Concat(Finals(db, tr, TempName)).Where(id => id != keep));
        erased = R.Erased;
        var kept = R.Retired.Concat(R.Failed).ToList();
        return (erased > 0 ? $"옛 최종면 {erased}개 지움" : "") + (kept.Count > 0 ? (erased > 0 ? " · " : "") + "⚠남이 쓰는 옛 최종면은 이름만 바꿔 숨김 — " + string.Join(" · ", kept) : "");
    }

    /// <summary>같은 이름이 없게(지운 면은 건너뜀).</summary>
    static string UniqueName(Database db, Transaction tr, string baseName)
    {
        var have = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var cdoc = Autodesk.Civil.ApplicationServices.CivilDocument.GetCivilDocument(db);
        foreach (ObjectId id in cdoc.GetSurfaceIds())
        {
            if (id.IsNull || id.IsErased) continue;
            try { if (tr.GetObject(id, OpenMode.ForRead) is CivilDb.Surface s) have.Add(s.Name); } catch { }
        }
        if (!have.Contains(baseName)) return baseName;
        for (int i = 2; ; i++) { string c = $"{baseName}_{i}"; if (!have.Contains(c)) return c; }
    }

    /// <summary>★[L14] 진입도로 면 스타일 모형(3D) 표시 — 최종면이 맞으면 «경계만»(최종면 세모와 같은 자리 세모가 겹쳐 깜박이지 않게) · 아니면 바탕면 스타일 그대로(세모 — 도로가 3D 화면에 보이게).</summary>
    static void SetRoadStyleModel(Transaction tr, bool boundaryOnly, ObjectId baseId)
    {
        var id = RoadSession.EnsureRoadSurfStyle(tr, baseId);
        if (id.IsNull || !boundaryOnly) return;
        var st = (CivilDb.Styles.SurfaceStyle)tr.GetObject(id, OpenMode.ForWrite);
        foreach (CivilDb.Styles.SurfaceDisplayStyleType t in Enum.GetValues(typeof(CivilDb.Styles.SurfaceDisplayStyleType)))
            try { var m = st.GetDisplayStyleModel(t); bool want = t == CivilDb.Styles.SurfaceDisplayStyleType.Boundary; if (m.Visible != want) m.Visible = want; } catch { }
    }

    static List<Tri> ReadAll(CivilDb.TinSurface s, string who)
    {
        var t = PureWithWallsBuilder.ReadTris(s, out int lost);
        if (lost > 0) throw new InvalidOperationException($"«{who}» 세모를 읽다 {lost}개를 버림");
        return t;
    }

    /// <summary>차례와 무관한 세모 해시(꼭짓점 비트 그대로) — 도장 [2](진단 — 상태 판정엔 안 씀).</summary>
    static string TriHash(List<Tri> tris)
    {
        ulong sum = 0;
        static ulong Mix(ulong h) { unchecked { h ^= h >> 33; h *= 0xff51afd7ed558ccdUL; h ^= h >> 33; h *= 0xc4ceb9fe1a85ec53UL; h ^= h >> 33; return h; } }
        static ulong V(Point3 p) { unchecked { return Mix((ulong)BitConverter.DoubleToInt64Bits(p.X) * 3UL + Mix((ulong)BitConverter.DoubleToInt64Bits(p.Y)) * 5UL + Mix((ulong)BitConverter.DoubleToInt64Bits(p.Z)) * 7UL); } }
        foreach (var t in tris) unchecked { sum += Mix(V(t.A) + V(t.B) + V(t.C)); }
        return $"{tris.Count}:{sum:x16}";
    }

    /// <summary>★[8장 · 7.7 · A1 실제 자료] 가파른 띠마다 까닭 — 도로 면 끝 선이 정지면 꺾임을 가로지름(<see cref="FinalSurface.SteepRun.Crease"/>) ·
    /// 진입도로 지표면이 지금 계획부지와 안 맞음(그 도로 지문의 바탕 몫이 지금과 다름) · 사면이 땅에 못 닿음. 자리 = 노선 측점 · 좌/우.
    /// 돌려줌 = (경고 — ⚠ 줄들 · 알림 — 꺾임 줄들).</summary>
    static (List<string> Warn, List<string> Info) SteepNotes(Database db, Transaction tr, IReadOnlyList<FinalSurface.SteepRun> runs, IReadOnlyList<Road> roads, ObjectId baseId, Func<CachedGroundSurface> baseC)
    {
        var warn = new List<string>(); var info = new List<string>();
        if (runs.Count == 0) return (warn, info);
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        double iv = Math.Max(1.0, GradingSettings.XsecInterval);
        var match = new Dictionary<long, bool>();
        foreach (var s in runs)
        {
            var r = roads.FirstOrDefault(q => q.Aid.Handle.Value == s.Handle);
            string at = "";
            if (r != null)
            {
                // 둘레 전체(시작 = 끝 점)면 «둘레 전체» · 끝 점이 노선 측점 밖(끝 마구리 등 — StationOffset 예외)이면 좌표
                if (s.X0 == s.X1 && s.Y0 == s.Y1) at = $"«{r.Name}» 둘레 전체 · ";
                else
                    try
                    {
                        var al = (CivilDb.Alignment)tr.GetObject(r.Aid, OpenMode.ForRead);
                        double s0 = 0, o0 = 0, s1 = 0, o1 = 0;
                        al.StationOffset(s.X0, s.Y0, ref s0, ref o0); al.StationOffset(s.X1, s.Y1, ref s1, ref o1);
                        at = $"«{r.Name}» 측점 {StationMarks.Fmt(Math.Min(s0, s1), iv)}" + (Math.Abs(s1 - s0) > 0.05 ? $" ~ {StationMarks.Fmt(Math.Max(s0, s1), iv)}" : "") + $" {(o0 + o1 < 0 ? "좌" : "우")} · ";
                    }
                    catch { at = string.Format(ci, "«{0}» ({1:F1}, {2:F1}) · ", r.Name, (s.X0 + s.X1) / 2, (s.Y0 + s.Y1) / 2); }
            }
            string tail = string.Format(ci, "{0}{1:F1} m · 최대 {2:F0} cm", at, s.Length, s.MaxDv * 100);
            if (s.Crease) { info.Add(string.Format(ci, "도로 면 끝 선이 정지면 꺾임을 가로지름({0} · 끝 선 {1:F2} m)", tail, s.EdgeLen)); continue; }
            bool same = true;
            if (r != null && !match.TryGetValue(s.Handle, out same))
            {
                same = RoadSession.BaseMatches(db, tr, r.Aid, r.D, baseId, baseC());
                match[s.Handle] = same;
            }
            warn.Add(same ? $"⚠사면이 땅에 못 닿음 — 옹벽 변환 필요({tail})" : $"⚠진입도로 지표면이 지금 계획부지와 안 맞음 — [업데이트]({tail})");
        }
        return (warn, info);
    }

    /// <summary>★★★[계획 4장 차례 ③] 짓는다 — 남은 «짓는중» 치움 → 입력(정지면 세모를 <b>늘 새로 읽음</b> — M4) → Compose → XML 정리 → 새 XML → 가져오기 → 되읽기 관문 → 굳힘 →
    /// 옛 최종면 치움(보호) → 이름 · 스타일 · 레이어 = 정지면_DH · 도장 → 보이기(결과만: 최종면 켬 · 정지면_DH 끔 · 도로 면 켬 · 도로 스타일 모형 = 경계만) → 커밋.
    /// <para>예외를 안 던진다 — 돌려줌 = 한 줄: «⚠최종지표면 못 만듦 — 까닭»(실패 — 옛 최종면 치움 · 정지면_DH 켬 · 도로 스타일 모형 = 세모) · «최종지표면 … 건너뜀 — 까닭»(정지면_DH가 하나 아님) · 그 밖 = 만듦(⚠ 경고가 붙을 수 있음).
    /// 문서는 부르는 쪽이 잠금 · 바뀜 막기(<see cref="RoadSession.Suppress"/>)도 부르는 쪽.</para>
    /// <para>★[Step 0 측정 1103] 정지면_DH를 꺼도 어느 면도 구식이 안 됨 → 숨긴 뒤 재작성은 뺐다(3장 규칙). 그래서 숨김은 짓는 트랜잭션 안에서 함께(실패하면 함께 물림).</para></summary>
    internal static string Build(Document doc)
    {
        var db = doc.Database;
        var all = System.Diagnostics.Stopwatch.StartNew();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var ms = new List<string>();
        void Lap(string n) { ms.Add($"{n} {sw.ElapsedMilliseconds}"); sw.Restart(); }
        ObjectId made = ObjectId.Null, baseId = ObjectId.Null;
        string repText = "";
        try
        {
            // ⓪ 지난번에 못 치운 «짓는중»
            using (var tr = db.TransactionManager.StartTransaction())
            {
                foreach (var id in Finals(db, tr, TempName)) try { tr.GetObject(id, OpenMode.ForWrite, false, true).Erase(); } catch { }
                tr.Commit();
            }
            // ① 입력 — 정지면 세모 · 도로 세모(새로 읽음)
            Inputs I; List<Tri> baseT; var layers = new List<FinalSurface.Layer>();
            using (var tr = db.TransactionManager.StartTransaction())
            {
                I = Gather(db, tr);
                baseId = I.BaseId;
                if (I.Roads.Count == 0 || I.BaseId.IsNull) { tr.Commit(); return Skip(db, I.Roads.Count == 0 ? "진입도로 지표면 없음" : I.BaseWhy); }
                baseT = ReadAll((CivilDb.TinSurface)tr.GetObject(I.BaseId, OpenMode.ForRead), Commands.WallCompositeCommand.PlanName);
                var ranks = BranchBase.LayerRanks(tr, db);   // ★[도로 마무리 ① · 계획 5.5 · L8] 가지(끝 맞춤 도로)는 본 도로 뒤(위) — 없으면 모두 0(핸들 차례 · 지금과 같음)
                foreach (var r in I.Roads)
                {
                    var ts = (CivilDb.TinSurface)tr.GetObject(r.SurfId, OpenMode.ForRead);
                    layers.Add(new FinalSurface.Layer(ReadAll(ts, ts.Name), r.Name, r.Aid.Handle.Value, ranks.TryGetValue(r.Aid.Handle.Value, out int rk) ? rk : 0));
                }
                tr.Commit();
            }
            Lap("읽기");
            // ② 합치기
            var fin = FinalSurface.Compose(baseT, layers, null, out var rep);
            repText = rep.Text;
            Lap("합치기");
            if (fin == null) return Fail(db, baseId, "합치기 — " + rep.Fail, repText, all, ms);
            // ③ XML — 쓰기 앞에서 아무 면도 안 무는 옛 원본을 치운다(L11) · 늘 새 파일(한 번 쓴 파일은 다시 안 씀)
            try
            {
                string td;
                using (var trT = db.TransactionManager.StartTransaction()) { td = GradingBuilder.TidyXml(db, trT); trT.Commit(); }
                if (td.Length > 0) DiagLog.Append("\n[최종지표면] XML 정리: " + td);
            }
            catch (Exception exT) { try { DiagLog.Append("\n[최종지표면] XML 정리 못 함 — " + exT.Message); } catch { } }
            string xml = GradingBuilder.NewXmlPath(db, XmlKind);
            WallDaylight.WriteLandXmlTin(xml, XmlSurf, fin, 1e-7, out int np, out int nf, out int nd);
            Lap("XML");
            string msg;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                ObjectId cid = CivilDb.TinSurface.CreateFromLandXML(db, TempName, xml, XmlSurf);
                if (cid.IsNull) throw new InvalidOperationException("CreateFromLandXML이 빈 ObjectId를 돌려줬다");
                made = cid;
                var cTin = (CivilDb.TinSurface)tr.GetObject(cid, OpenMode.ForWrite, false, true);
                // ★[v111.8 · 검토 L5] 새 면이 넘긴 XML 경로를 무는가(어긋나면 이 실행에서는 XML 원본을 안 지움 — 옹벽 합성과 같은 법)
                string refChk; try { refChk = GradingBuilder.XmlRefCheck(cTin, xml); } catch (Exception rx) { refChk = "확인 실패 " + rx.GetType().Name; }
                if (refChk.Length > 0) try { DiagLog.Append("\n[최종지표면] ⚠원본 경로 확인 — " + refChk + " — 이 실행에서는 XML 원본을 지우지 않음"); } catch { }
                int civPts = -1; try { civPts = cTin.GetGeneralProperties().NumberOfPoints; } catch { }
                var theirs = PureWithWallsBuilder.ReadTris(cTin, out int lost);
                Lap("가져오기");
                if (lost != 0 || nd != 0 || theirs.Count != nf || civPts != np)
                    throw new InvalidOperationException($"Civil이 면을 그대로 안 받았습니다 — Civil 점 {civPts}/{np} · 세모 {theirs.Count}/{nf} · 못 읽음 {lost} · 합친 세모 {nd}");
                var cmp = CivilSafeMesh.Compare(fin, theirs);
                double area = 0; foreach (var t in fin) area += Math.Abs((t.B.X - t.A.X) * (t.C.Y - t.A.Y) - (t.B.Y - t.A.Y) * (t.C.X - t.A.X)) / 2;
                bool gate = cmp.Exceptions == 0 && cmp.SymArea <= 1e-8 + 1e-12 * area && cmp.Hausdorff <= 1e-6 && cmp.MaxDz <= 1e-3;
                string back = $"Civil 점 {civPts}/{np} · 세모 {theirs.Count}/{nf} · {cmp.Summary}";
                Lap("대조");
                if (FailGateForTest) throw new InvalidOperationException("Civil이 면을 그대로 안 받았습니다 — 시험 고리 · " + back);
                if (!gate) throw new InvalidOperationException("Civil이 면을 그대로 안 받았습니다 — " + back);
                string frz = GradingBuilder.FreezeXmlBacked(cTin);
                if (frz.Length > 0) throw new InvalidOperationException("굳히지 못했습니다 — " + frz);
                Lap("굳힘");
                string old = RetireOld(db, tr, cid);
                var bs = (CivilDb.TinSurface)tr.GetObject(I.BaseId, OpenMode.ForRead);
                string name = UniqueName(db, tr, Name);
                cTin.Name = name;
                try { cTin.Description = $"DH 최종지표면(정지면_DH + 진입도로 {I.Roads.Count}개) — {DateTime.Now:yyyy-MM-dd HH:mm} · {GradingSettings.Version}"; } catch { }
                try { cTin.StyleId = bs.StyleId; } catch { }
                try { cTin.LayerId = bs.LayerId; } catch { }
                // 가파른 띠 — 까닭 붙여 경고 · 꺾임 가로지름은 알림(로그 · 명령창)
                CachedGroundSurface? bc = null;
                var (warn, info) = SteepNotes(db, tr, rep.Steep, I.Roads, I.BaseId, () => bc ??= new CachedGroundSurface(baseT));
                var ci = System.Globalization.CultureInfo.InvariantCulture;
                WriteStamp(tr, cTin, I.Stamp0,
                           string.Format(ci, "{0:yyyy-MM-dd HH:mm:ss} · {1} · 세모 {2} · Δv 최대 {3:F1} mm · 가파른 띠 {4}", DateTime.Now, GradingSettings.Version, fin.Count, rep.DvMax * 1000, rep.Steep.Count),
                           "정지면 " + TriHash(baseT) + " · " + string.Join(" · ", layers.Select(l => $"{l.Name} {TriHash(l.Tris as List<Tri> ?? l.Tris.ToList())}")),
                           string.Join("\n", warn));
                // ★[3장 · M3 · L14] 보이기 — «결과지표면만 표시»일 때만: 최종면 켬 · 정지면_DH 끔(읽어 보고 다를 때만 씀) · 도로 면은 켬
                string vis = "결과만 보기 꺼짐 — 보이기 그대로";
                if (GradingSettings.ShowOnlyResultSurface)
                {
                    if (!cTin.Visible) cTin.Visible = true;
                    int nb = GradingBuilder.SetSurfaceVisible(tr, Commands.WallCompositeCommand.PlanName, false);
                    int nr = 0;
                    foreach (var r in I.Roads)
                    {
                        var e = (Entity)tr.GetObject(r.SurfId, OpenMode.ForRead);
                        if (!e.Visible) { e.UpgradeOpen(); e.Visible = true; nr++; }
                    }
                    vis = $"최종면 켬 · 정지면_DH 끔({nb}) · 도로 면 켬({nr})";
                }
                SetRoadStyleModel(tr, true, I.BaseId);
                tr.Commit();
                made = ObjectId.Null;
                Lap("커밋");
                msg = $"최종지표면 '{name}' — 세모 {fin.Count:N0} · {all.ElapsedMilliseconds / 1000.0:F1}초"
                      + (warn.Count > 0 ? " · " + string.Join(" · ", warn) : "") + (info.Count > 0 ? " · " + string.Join(" · ", info) : "")
                      + (old.StartsWith("⚠") || old.Contains(" · ⚠") ? " · " + old[old.IndexOf('⚠')..] : "");
                try { DiagLog.Append($"\n[최종지표면] {msg} · {vis} · {old} · {string.Join(" · ", ms)} ms · 되읽기 {back} · XML {System.IO.Path.GetFileName(xml)}\n  {repText.Replace("\n", "\n  ")}"); } catch { }
            }
            return msg;
        }
        catch (Exception ex)
        {
            return Fail(db, baseId, ex.Message, repText, all, ms);
        }
        finally
        {
            if (!made.IsNull)
            {
                try { using var trE = db.TransactionManager.StartTransaction(); if (!made.IsErased) trE.GetObject(made, OpenMode.ForWrite, false, true).Erase(); trE.Commit(); }
                catch (Exception exE) { try { DiagLog.Append($"\n[최종지표면] ⚠짓는 면 «{TempName}»을 못 지움(다음에 다시 치움) — {exE.Message}"); } catch { } }
            }
        }
    }

    /// <summary>못 지음 · 건너뜀 — 옛 최종면은 이제 옛것이라 치움(보호면 이름 바꿈) · 정지면_DH 켬 · 도로 스타일 모형 = 세모(도로가 3D 화면에 보이게).</summary>
    static string Clean(Database db, ObjectId baseId)
    {
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            string old = RetireOld(db, tr, ObjectId.Null);
            GradingBuilder.SetSurfaceVisible(tr, Commands.WallCompositeCommand.PlanName, true);
            SetRoadStyleModel(tr, false, baseId);
            tr.Commit();
            return old;
        }
        catch (Exception ex) { return "⚠정리 못 함 — " + ex.Message; }
    }

    static string Fail(Database db, ObjectId baseId, string why, string repText, System.Diagnostics.Stopwatch all, List<string> ms)
    {
        string old = Clean(db, baseId);
        string msg = "⚠최종지표면 못 만듦 — " + why;
        try { DiagLog.Append($"\n[최종지표면] {msg} · {old} · {string.Join(" · ", ms)} ms · 전체 {all.ElapsedMilliseconds} ms" + (repText.Length > 0 ? "\n  " + repText.Replace("\n", "\n  ") : "")); } catch { }
        return msg;
    }

    static string Skip(Database db, string why)
    {
        string old = Clean(db, ObjectId.Null);
        string msg = "최종지표면 건너뜀 — " + why;
        try { DiagLog.Append($"\n[최종지표면] {msg}" + (old.Length > 0 ? " · " + old : "")); } catch { }
        return msg + (old.Contains('⚠') ? " · " + old[old.IndexOf('⚠')..] : "");
    }

    // ══════════════════════════════ 쓰는 곳 — [3D 보기] · DHINFRA ══════════════════════════════

    /// <summary>지형 고르기 결과.</summary>
    internal sealed class Pick
    {
        /// <summary>도면에 우리 진입도로 면이 있다(«진입도로 제외» 표시) — 거짓이면 부르는 쪽은 지금 코드 그대로.</summary>
        public bool HasRoads;
        /// <summary>지형에 도로가 들었다(최종면 맞음 또는 메모리 합침) — 3D 도로 구분 · 차선.</summary>
        public bool InTerrain;
        /// <summary>쓸 면(맞는 최종면 · 합치기 실패 때 정지면_DH) — 메모리 합침이면 Null.</summary>
        public ObjectId Id = ObjectId.Null;
        public List<Tri>? Tris;
        public string Source = "";
        public Geometry? RoadArea, RoadPave, RoadLane;
        /// <summary>★[v116.8 · JACK 1110 포장 종류] 콘크리트 포장 도로의 차로(지형 «진입도로(콘크리트)») — 없으면 null(옛 결과 그대로).</summary>
        public Geometry? RoadLaneConc;
        /// <summary>★[v111.8.1] 도로 소단 띠(엔진 계단선 짝 · <see cref="RoadBenches.Strips"/>) — 지형에 도로가 들었으면 늘 있음(없으면 빈 것).</summary>
        public Geometry? RoadBench;
        public CachedGroundSurface? Base;
        public readonly List<RoadLanes.Road> Roads = new();
    }

    /// <summary>★[v111.8.1] 모형 공간의 진입도로 계단선(사면선 · 소단선 · 옹벽선 3D 폴리선) — 노선 핸들(XData)마다. 노리선은 안 씀.</summary>
    static Dictionary<string, List<IReadOnlyList<(double X, double Y, double Z)>>> RoadTierLines(Database db, Transaction tr)
    {
        var res = new Dictionary<string, List<IReadOnlyList<(double X, double Y, double Z)>>>(StringComparer.OrdinalIgnoreCase);
        var lays = new HashSet<string>(new[] { RoadSession.LaySlope, RoadSession.LayBench, RoadSession.LayWall }, StringComparer.OrdinalIgnoreCase);
        var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
        foreach (ObjectId id in ms)
        {
            try
            {
                if (!id.ObjectClass.IsDerivedFrom(Autodesk.AutoCAD.Runtime.RXObject.GetClass(typeof(Polyline3d)))) continue;
                var pl = (Polyline3d)tr.GetObject(id, OpenMode.ForRead);
                if (!lays.Contains(pl.Layer)) continue;
                using var rb = pl.GetXDataForApplication(RoadSession.LineApp);
                var v = rb?.AsArray();
                if (v == null || v.Length < 2 || v[1].Value is not string h) continue;
                var pts = new List<(double X, double Y, double Z)>();
                foreach (ObjectId vid in pl) { var pv = (PolylineVertex3d)tr.GetObject(vid, OpenMode.ForRead); pts.Add((pv.Position.X, pv.Position.Y, pv.Position.Z)); }
                if (!res.TryGetValue(h, out var l)) res[h] = l = new();
                l.Add(pts);
            }
            catch { }
        }
        return res;
    }

    /// <summary>★[계획 5장] 지형 고르기 — 도로 없음 → <see cref="Pick.HasRoads"/> 거짓(지금 코드 그대로 · 한 줄도 안 달라짐) · 최종면 맞음 → 그 면 · 없음/옛것 → 메모리 합치기(같은 Compose · 도면 안 바꿈).
    /// 합치기 실패면 정지면_DH + ⚠ «진입도로 빠짐». 읽기만 한다(DBMOD 0 → 0).</summary>
    internal static Pick PickTerrain(Database db, System.Text.StringBuilder log, List<string> warn)
    {
        var P = new Pick();
        using var tr = db.TransactionManager.StartTransaction();
        var I = Gather(db, tr);
        if (I.Roads.Count == 0) { tr.Commit(); return P; }
        P.HasRoads = true;   // 토량은 계획부지만 — 지형에 도로를 못 넣어도 «진입도로 제외»
        if (I.BaseId.IsNull) { warn.Add("진입도로 빠짐 — " + I.BaseWhy); log.AppendLine("최종지표면: 진입도로 빠짐 — " + I.BaseWhy); tr.Commit(); return P; }
        var gf = NtsSupport.Factory();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var st = StateOf(db, tr, out var fresh, out _);
        var baseTin = (CivilDb.TinSurface)tr.GetObject(I.BaseId, OpenMode.ForRead);
        var roadT = new List<(Road R, List<Tri> T, Geometry? F)>();
        foreach (var r in I.Roads)
        {
            var ts = (CivilDb.TinSurface)tr.GetObject(r.SurfId, OpenMode.ForRead);
            var t = ReadAll(ts, ts.Name);
            roadT.Add((r, t, RoadFootprint.FromTris(t)));
        }
        var baseT = ReadAll(baseTin, Commands.WallCompositeCommand.PlanName);
        if (st == State.Fresh)
        {
            // ★[v111.8 · 검토 M3] 세모를 읽는 김에 도장 [2](지을 때 쓴 세모 해시)와 견준다 — 통계 · 정의 줄 수로 못 가른 편집이면 옛것으로(메모리 합침)
            var stp0 = ReadStamp(tr, tr.GetObject(fresh, OpenMode.ForRead));
            string nowH = "정지면 " + TriHash(baseT) + " · " + string.Join(" · ", roadT.Select(q => $"{q.R.Name} {TriHash(q.T)}"));
            if (stp0.Length < 3 || stp0[2] != nowH)
            {
                st = State.Stale;
                log.AppendLine($"최종지표면: 도장 [0]은 맞는데 세모가 지을 때와 다름 → 옛것으로(메모리 합침) — 도장 «{(stp0.Length > 2 ? stp0[2] : "")}» · 지금 «{nowH}»");
            }
        }
        if (st == State.Fresh)
        {
            P.Id = fresh; P.Source = Name; P.InTerrain = true;
            P.Base = new CachedGroundSurface(baseT);
            // RoadArea = 도로마다 F ⊕ ε(Compose와 같은 모서리 법)
            var bp = new NetTopologySuite.Operation.Buffer.BufferParameters { JoinStyle = NetTopologySuite.Operation.Buffer.JoinStyle.Mitre, MitreLimit = 2.0, QuadrantSegments = 8 };
            var areas = roadT.Where(q => q.F != null).Select(q => NetTopologySuite.Operation.Buffer.BufferOp.Buffer(q.F!, new FinalSurface.Options().Eps, bp)).ToList();
            P.RoadArea = areas.Count == 0 ? null : areas.Count == 1 ? areas[0] : NetTopologySuite.Operation.Union.CascadedPolygonUnion.Union(areas);
            var stp = ReadStamp(tr, tr.GetObject(fresh, OpenMode.ForRead));
            if (stp.Length > 3 && stp[3].Length > 0) warn.AddRange(stp[3].Split('\n').Select(w => w.TrimStart('⚠')));
            log.AppendLine($"최종지표면: 맞음 — «{((CivilDb.Surface)tr.GetObject(fresh, OpenMode.ForRead)).Name}» 그대로 씀 · 도로 {I.Roads.Count} · {sw.ElapsedMilliseconds} ms");
        }
        else
        {
            var ranks = BranchBase.LayerRanks(tr, db);   // ★[계획 5.5] 최종면과 같은 차례(가지 위)
            var fin = FinalSurface.Compose(baseT, roadT.Select(q => new FinalSurface.Layer(q.T, q.R.Name, q.R.Aid.Handle.Value, ranks.TryGetValue(q.R.Aid.Handle.Value, out int rk) ? rk : 0)).ToList(), null, out var rep);
            P.Base = new CachedGroundSurface(baseT);
            if (fin == null)
            {
                P.Id = I.BaseId; P.Source = Commands.WallCompositeCommand.PlanName;
                warn.Add("진입도로 빠짐 — " + rep.Fail);
                log.AppendLine($"최종지표면: {(st == State.Stale ? "옛것" : "없음")} · 메모리 합치기 실패 → 정지면_DH(진입도로 빠짐) — {rep.Fail}\n  {rep.Text.Replace("\n", "\n  ")}");
            }
            else
            {
                P.Tris = fin; P.Source = "메모리 합침"; P.InTerrain = true; P.RoadArea = rep.RoadArea;
                var b = P.Base;
                var (w, info) = SteepNotes(db, tr, rep.Steep, I.Roads, I.BaseId, () => b);
                warn.AddRange(w.Select(x => x.TrimStart('⚠')));
                log.AppendLine($"최종지표면_DH가 {(st == State.Stale ? "옛것" : "없어")} 메모리에서 합쳐 씀(도면 안 바꿈) · 세모 {fin.Count:N0} · {sw.ElapsedMilliseconds} ms"
                               + (w.Count + info.Count > 0 ? " · " + string.Join(" · ", w.Concat(info)) : "") + $"\n  {rep.Text.Replace("\n", "\n  ")}");
            }
        }
        // 포장(차로 + 길어깨) · 차로 — 설계에서(노선 + 폭)
        if (P.InTerrain)
        {
            var paves = new List<Geometry>(); var lanes = new List<Geometry>(); var lanesC = new List<Geometry>();
            foreach (var (r, _, f) in roadT)
            {
                try
                {
                    var rl = RoadLanes.Of(tr, r.Aid, r.D, r.Name, f);
                    if (rl == null) { warn.Add($"진입도로 «{r.Name}» 계획 종단을 못 찾아 포장 · 차선을 안 그림"); continue; }
                    P.Roads.Add(rl);
                    var (pv, ln, nd) = RoadLanes.Pavement(tr, rl, gf);
                    if (nd > 0) log.AppendLine($"  포장 «{r.Name}»: 노선 자리를 못 얻은 측점 {nd}개(포장 · 차로 합)는 양쪽에서 뺌");
                    if (pv != null) paves.Add(pv);
                    // ★[v116.8] 콘크리트 도로 차로는 따로 · ★[설치 전 검토 L9] 차로 다각형을 못 얻은 도로는 그 도로 포장 전체를 제 종류 차로로(«둘 다 없으면 포장 = 차로» 전체 규칙에 기대지 않게)
                    var lnK = ln ?? pv;
                    if (lnK != null) (r.D?.PaveKind == 1 ? lanesC : lanes).Add(lnK);
                }
                catch (Exception ex) { warn.Add($"진입도로 «{r.Name}» 포장을 못 셈 — {ex.Message}"); }
            }
            Geometry? U(List<Geometry> g) => g.Count == 0 ? null : g.Count == 1 ? g[0] : NetTopologySuite.Operation.Union.CascadedPolygonUnion.Union(g);
            P.RoadPave = U(paves); P.RoadLane = U(lanes); P.RoadLaneConc = U(lanesC);
            // ★★[v111.8.1 · JACK 1103 «진입도로의 소단이 안 보여»] 소단 띠 — 도로 소단은 종단 경사를 따라가 세모 기울기(1:10)로는 못 가른다(시험 도면 소단 세모 607개 중 0개).
            //   엔진이 그린 계단선 짝 사이 횡단으로 평평한 띠(높이 = 그 도로 면) · 짝 찾는 거리 = 설계 소단폭 · 옹벽 사이 소단 길이 중 큰 것 + 0.3 m(옹벽 면 평면폭 포함)
            var benches = new List<Geometry>();
            bool benchFail = false;
            try
            {
                var tl = RoadTierLines(db, tr);
                foreach (var (r, t, _) in roadT)
                {
                    // ★★[v116.5 · JACK 1110 «소단 표시 누락»] 고리 면 도로 = 면 높이 규칙의 소단 자리(계단선 짝은 두 다리가 만나는 안쪽에서 꺾여 소단을 놓침) · 아니면 아래 옛 계단선 짝
                    {
                        var rzA = new CachedGroundSurface(t);
                        NetTopologySuite.Geometries.Geometry? ga = null; string gNote;
                        var swA = System.Diagnostics.Stopwatch.StartNew();
                        try { ga = RoadSession.RingBenchArea(db, r.Aid, r.D, (x, y) => rzA.TryGetElevation(x, y, out _), out gNote); }
                        catch (Exception exA) { gNote = "예외 — " + exA.Message; }
                        if (ga != null)
                        {
                            log.AppendLine($"  소단 «{r.Name}»: 면 규칙 — {gNote} · {swA.ElapsedMilliseconds} ms");
                            if (!ga.IsEmpty) benches.Add(ga);
                            continue;
                        }
                        log.AppendLine($"  소단 «{r.Name}»: 면 규칙 못 씀({gNote}) → 계단선 짝");
                    }
                    if (!tl.TryGetValue(r.Aid.Handle.ToString(), out var ls) || ls.Count == 0) { log.AppendLine($"  소단 «{r.Name}»: 계단선 없음 — 소단 없음"); continue; }
                    double reach = Math.Max(r.D.CutBenchW, r.D.FillBenchW);
                    foreach (var w in r.D.TierWalls) if (w.Length >= 5 && double.IsFinite(w[4])) reach = Math.Max(reach, w[4]);
                    var rz = new CachedGroundSurface(t);
                    var br = RoadBenches.Strips(ls, (x, y) => rz.TryGetElevation(x, y, out double z) ? z : null, reach + 0.3);
                    log.AppendLine($"  소단 «{r.Name}»: {br.Text}");
                    if (br.Strips != null && !br.Strips.IsEmpty) benches.Add(br.Strips);
                }
            }
            catch (Exception ex) { benchFail = true; warn.Add("진입도로 소단을 못 셈 — " + ex.Message); log.AppendLine("  소단: 실패 — " + ex + " → 옛 1:10 판정으로"); }
            // ★[v111.9 · 설치 전 검토 L2] 못 셌으면 null(옛 1:10 판정) — 빈 것을 넘기면 도로 소단이 말없이 다 빠진다
            P.RoadBench = benchFail ? null : U(benches) ?? gf.CreateGeometryCollection();
        }
        tr.Commit();
        return P;
    }
}
