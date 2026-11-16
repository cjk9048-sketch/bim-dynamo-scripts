using Autodesk.AutoCAD.DatabaseServices;
using CivilDb = Autodesk.Civil.DatabaseServices;
using DH.Grading.Civil.Commands;

namespace DH.Grading.Civil;

/// <summary>★★[v117.19 · JACK 1116 «서버 지표면이 하나밖에 안 가져와져» · «창마다 [원지반 선택]»] 원지반이 여러 장일 때의 <b>한 벌</b> 규칙(§50).
/// <para><b>주인 표시(태그).</b> 원지반을 만들 때 그 재료(등고선 · 계곡선 · 표고점 · 브레이크라인)마다 XData «DH_GROUND» = 그 원지반 지표면의 <b>핸들</b>을 붙인다.
/// 레이어 이름은 그대로 — 레이어로 켜고 끄는 길(ViewSurfaceCommand · 시트 현황 등고선) · 원지형 자르기가 레이어만 옮기는 길(범위 밖 레이어)이 안 깨진다. 이름 대신 핸들 — 이름을 바꿔도 따라온다.</para>
/// <para><b>옛 도면(표시 없음).</b> 주인 «모름» — <b>원지반을 전부 지울 때만</b> 같이 지운다. «가장 오래된 원지반 것»으로 보면 단순하지만, 옛 도면에서 지운 쪽이 틀리면 남의 재료를 지우는 것이라
/// (되돌릴 수 없음) 못 지우고 남는 쪽(숨은 레이어의 선 몇 개)이 더 싼 실수다.</para></summary>
internal static class GroundSet
{
    internal const string App = "DH_GROUND";

    internal readonly record struct Item(ObjectId Id, string Name, string Handle, int Tris);

    // ── 주인 표시 ───────────────────────────────────────────────────────────
    /// <summary>재료에 주인(원지반 핸들)을 붙인다 — 새 트랜잭션 · 잠긴 레이어도 연다. 반환 = 붙인 수.</summary>
    internal static int Tag(Database db, System.Collections.Generic.IEnumerable<ObjectId> ids, string surfHandle)
    {
        if (string.IsNullOrEmpty(surfHandle)) return 0;
        int n = 0;
        using var tr = db.TransactionManager.StartTransaction();
        GradingBuilder.EnsureRegApp(db, tr, App);
        foreach (var id in ids)
            try
            {
                if (id.IsNull || id.IsErased || tr.GetObject(id, OpenMode.ForWrite, false, true) is not Entity e) continue;
                e.XData = new ResultBuffer(new TypedValue((int)DxfCode.ExtendedDataRegAppName, App),
                                           new TypedValue((int)DxfCode.ExtendedDataHandle, surfHandle));   // 1005 — 복사 · 내보내기 때 AutoCAD가 새 핸들로 옮겨 줌
                n++;
            }
            catch { }
        tr.Commit();
        return n;
    }

    /// <summary>재료의 주인 핸들 — 표시가 없으면 빈 글(옛 도면).</summary>
    internal static string OwnerOf(Entity e)
    {
        try
        {
            using var rb = e.GetXDataForApplication(App);
            if (rb == null) return "";
            foreach (var tv in rb) if (tv.TypeCode == (int)DxfCode.ExtendedDataHandle || tv.TypeCode == (int)DxfCode.ExtendedDataAsciiString) return tv.Value as string ?? "";   // 옛 키트 문자열(1000)도 읽음
        }
        catch { }
        return "";
    }

    // ── 원지반 목록 ─────────────────────────────────────────────────────────
    /// <summary>원지반 후보 — 이름이 «원지반» · «원지반_N»인 TIN 지표면(만든 순서 = 핸들 순). 하나도 없으면(LandXML · 사람이 만든 이름) <see cref="ImportGisCommand.FindGroundSurface"/>가 고르는 면 하나.</summary>
    internal static System.Collections.Generic.List<Item> List(Database db, bool withTris = false, bool legacy = true)
    {
        var r = new System.Collections.Generic.List<Item>();
        try
        {
            var civ = Autodesk.Civil.ApplicationServices.CivilApplication.ActiveDocument;
            using var tr = db.TransactionManager.StartTransaction();
            foreach (ObjectId sid in civ.GetSurfaceIds())
                try
                {
                    if (sid.IsErased || tr.GetObject(sid, OpenMode.ForRead) is not CivilDb.TinSurface ts) continue;
                    string nm = ts.Name ?? "";
                    if (!SectionCommand.IsBase(nm, ImportGisCommand.GroundSurfaceName)) continue;
                    int n = 0; if (withTris) try { n = ts.Triangles.Count; } catch { }
                    r.Add(new Item(sid, nm, sid.Handle.ToString(), n));
                }
                catch { }
            tr.Commit();
        }
        catch { }
        r.Sort((a, b) => HandleVal(a.Handle).CompareTo(HandleVal(b.Handle)));
        if (r.Count == 0 && legacy)
        {
            var o = LegacyLargest(db, out string nm, out int t);
            if (!o.IsNull) r.Add(new Item(o, nm, o.Handle.ToString(), t));
        }
        return r;
    }

    static long HandleVal(string h) { try { return System.Convert.ToInt64(h, 16); } catch { return long.MaxValue; } }

    /// <summary>★<b>명시적으로 고른</b> 지표면(창에서 클릭 · 기록의 핸들) — 살아 있는 TIN이고 우리 면(_DH · DH_)이 아니면 이름과 무관하게 인정(현황측량 같은 사람 면 포함).
    /// 지워졌거나 아니면 Null(= «고르지 않음»). <see cref="List"/> 소속은 <b>자동</b> 고르기에만 적용.</summary>
    internal static ObjectId Resolve(Database db, string handle) => Alive(db, handle);

    /// <summary>고르기 없이 정해지는 원지반 — 후보가 <b>딱 하나</b>면 그것 · 아니면 Null(0개 또는 2개 이상). <paramref name="count"/> = 후보 수.</summary>
    internal static ObjectId Auto(Database db, out int count)
    {
        var l = List(db);
        count = l.Count;
        return l.Count == 1 ? l[0].Id : ObjectId.Null;
    }

    /// <summary>이 창의 고른 값(핸들) 기준으로 쓸 원지반. 고른 것이 살아 있으면 그것 · 아니면 후보 하나면 자동 · 둘 이상이면 Null + <paramref name="needPick"/>.
    /// 고른 것이 지워졌으면 «고르지 않음»으로 돌아간다(옛 값은 안 씀).</summary>
    internal static ObjectId ForWindow(Database db, string chosenHandle, out bool needPick, out int count)
    {
        needPick = false;
        var id = Resolve(db, chosenHandle);
        var l = List(db);
        count = l.Count;
        if (!id.IsNull) return id;
        if (l.Count == 1) return l[0].Id;
        needPick = l.Count >= 2;
        return ObjectId.Null;
    }

    /// <summary>★터파기 창이 쓸 원지반 — ①이 창에서 고른 것 ②이 터파기(<paramref name="polyHandle"/>)의 기록에 적힌 것 ③원지반이 하나(이하)면 종전대로(마지막 정지의 면 → 그 하나).
    /// 둘 이상인데 고른 것이 없으면 Null + <paramref name="needPick"/>(만들기를 막고 [원지반 선택]을 안내). 고른 것 · 기록의 면이 지워졌으면 «고르지 않음»으로 돌아간다.</summary>
    internal static ObjectId ForExcav(Autodesk.AutoCAD.ApplicationServices.Document doc, string polyHandle, out bool needPick, out bool chosen)
        => ForExcavCore(doc, polyHandle, out needPick, out chosen, peek: false);

    /// <summary>화면 갱신용 — 같은 규칙인데 창에서 고른 값이 지워졌어도 <b>잊지 않는다</b>(잊으면 Raise → 창 갱신이 다시 불리는 되먹임). 명령 쪽은 <see cref="ForExcav"/>.</summary>
    internal static ObjectId ForExcavPeek(Autodesk.AutoCAD.ApplicationServices.Document doc, string polyHandle, out bool needPick, out bool chosen)
        => ForExcavCore(doc, polyHandle, out needPick, out chosen, peek: true);

    static ObjectId ForExcavCore(Autodesk.AutoCAD.ApplicationServices.Document doc, string polyHandle, out bool needPick, out bool chosen, bool peek)
    {
        var db = doc.Database;
        needPick = false; chosen = false;
        // peek = 화면 갱신용 — 지워진 고름을 잊지 않는다(잊으면 Raise → 창이 다시 갱신되는 되먹임)
        var id = peek ? PickSession.TryResolve(doc, PickSession.KeyExcavGround, out _) : PickSession.ResolveForUse(doc, PickSession.KeyExcavGround);
        if (!id.IsNull && AliveId(db, id)) { chosen = true; return id; }
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            var recs = ExcavBundleStore.TryLoadAll(db, tr, out _, out bool tooNew);
            tr.Commit();
            if (!tooNew && recs != null && !string.IsNullOrEmpty(polyHandle))
            {
                var rec = recs.Find(r => r.PolyHandle == polyHandle);
                if (rec != null) { id = Resolve(db, rec.GroundHandle); if (!id.IsNull) { chosen = true; return id; } }
            }
        }
        catch { }
        // 자동 — 원지반 이름 후보만 센다(없을 때 «가장 큰 면»으로 몰래 고르지 않음 · 종전대로 묻는다)
        var l = List(db, legacy: false);
        if (l.Count >= 2) { needPick = true; return ObjectId.Null; }
        id = NoriCommand.FindByHandle(db, GradingSettings.LastGroundHandle);
        if (id.IsNull && l.Count == 1) id = l[0].Id;
        return id;
    }

    /// <summary>★이름 · 핸들로 «지금 이 원지반» 이름 읽기(창 글자용) — 못 찾으면 빈 글.</summary>
    internal static string NameOf(Database db, ObjectId id)
    {
        if (id.IsNull) return "";
        foreach (var g in List(db)) if (g.Id == id) return g.Name;
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            string s = tr.GetObject(id, OpenMode.ForRead) is CivilDb.Surface sf ? sf.Name ?? "" : "";
            tr.Commit();
            return s;
        }
        catch { return ""; }
    }

    /// <summary>부지(계획부지 정지)가 쓴 원지반 — 마지막 정지가 기억한 핸들 → 도면에 저장된 마지막 구역의 핸들. 살아 있고 우리 면이 아닐 때만.</summary>
    internal static ObjectId SiteGround(Database db)
    {
        try
        {
            var id = Alive(db, GradingSettings.LastGroundHandle);
            if (!id.IsNull) return id;
            using var tr = db.TransactionManager.StartTransaction();
            var regs = GradingBundleStore.TryLoadAll(db, tr, out _);
            tr.Commit();
            if (regs is { Count: > 0 })
                for (int i = regs.Count - 1; i >= 0; i--)
                {
                    id = Alive(db, regs[i].GroundHandle);
                    if (!id.IsNull) return id;
                }
        }
        catch { }
        return ObjectId.Null;
    }

    /// <summary>우리 면 이름인가(_DH 꼴 · DH_ 머리) — 원지반으로 고를 수 없는 면.</summary>
    internal static bool IsOurs(string? name) => SelfCheck.IsOurSurfaceName(name) || (name ?? "").StartsWith("DH_", System.StringComparison.Ordinal);

    internal static ObjectId Alive(Database db, string handle)
    {
        var id = NoriCommand.FindByHandle(db, handle);
        return id.IsNull || !AliveId(db, id) ? ObjectId.Null : id;
    }

    /// <summary>살아 있는 TIN · 우리 면 아님 · 물러난 면 아님.</summary>
    internal static bool AliveId(Database db, ObjectId id)
    {
        if (id.IsNull || id.IsErased) return false;
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            bool ok = tr.GetObject(id, OpenMode.ForRead) is CivilDb.TinSurface ts && !IsOurs(ts.Name) && !SurfaceRetire.IsRetired(ts);
            tr.Commit();
            return ok;
        }
        catch { return false; }
    }

    /// <summary>종전 규칙 — 우리 산출물이 아닌 지표면 중 삼각형이 제일 많은 것.</summary>
    internal static ObjectId LegacyLargest(Database db, out string name, out int tris)
    {
        name = ""; tris = 0;
        ObjectId best = ObjectId.Null;
        try
        {
            var civilDoc = Autodesk.Civil.ApplicationServices.CivilApplication.ActiveDocument;
            using var tr = db.TransactionManager.StartTransaction();
            foreach (ObjectId sid in civilDoc.GetSurfaceIds())
                try
                {
                    if (tr.GetObject(sid, OpenMode.ForRead) is not CivilDb.TinSurface ts) continue;
                    string nm = ts.Name ?? "";
                    if (nm.Contains("_DH") || nm.StartsWith("DH_", System.StringComparison.Ordinal)) continue;
                    int n = 0; try { n = ts.Triangles.Count; } catch { }
                    if (n > tris) { tris = n; best = sid; name = nm; }
                }
                catch { }
            tr.Commit();
        }
        catch { }
        return best;
    }
}
