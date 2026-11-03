using System.Globalization;
using Autodesk.AutoCAD.DatabaseServices;
using CivilDb = Autodesk.Civil.DatabaseServices;
using CivilStyles = Autodesk.Civil.DatabaseServices.Styles;

namespace DH.Grading.Civil;

/// <summary>★★[v111.11 · JACK 1103 «원지형 및 모든 지형의 등고선 간격을 조정할 수 있는 기능을 도면설정 팝업 안에 · 3. 공통»]
/// 등고선(주곡선) · 계곡선 간격 — <b>도면에 저장</b>(도면 사전 XRecord «DH_CONTOURINT» · 진입도로 축척 · 횡단도 배치와 같은 길).
/// <para>기록이 없으면 지금까지 그대로다 — 원지반 2 · 10 m(<c>ImportGisCommand.EnsureContourStyle</c>) · 정지면은 고른 스타일의 간격.
/// 바꾼 적 없는 도면은 한 줄도 안 바뀐다.</para>
/// <para>바꾸면 <b>우리 지표면이 쓰는 스타일</b>의 간격을 고친다(<see cref="Apply"/>) — 스타일을 고치므로 그 스타일을 쓰는 지표면이 다 따라간다.
/// 뒤에 새로 만드는 스타일(원지반 다시 받기 · 정지면 +1cm 사본)도 이 값을 쓴다.</para>
/// <para>가져온 수치지도 등고선(선 · «DH-등고선» · «DH-등고선-계곡선» 도면층)은 원본 자료라 간격을 못 바꾼다 — 그대로 둔다(그 도면층은 가져올 때 꺼 둔다).</para></summary>
internal static class ContourInterval
{
    /// <summary>지금까지의 값(JACK 0901 «10m에 2m») — 기록이 없는 도면의 창에 보이는 값.</summary>
    internal const double DefaultMinor = 2.0, DefaultMajor = 10.0;
    /// <summary>하한 — 0.01 m 같은 값이면 Civil이 등고선 수만 가닥을 그리다 멈춘다.</summary>
    internal const double MinValue = 0.1;
    const string Key = "DH_CONTOURINT";

    /// <summary>도면 기록(있을 때만 참). 열린 트랜잭션 안에서.</summary>
    internal static bool TryRead(Transaction tr, Database db, out double minor, out double major)
    {
        minor = DefaultMinor; major = DefaultMajor;
        try
        {
            var nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);
            if (!nod.Contains(Key) || tr.GetObject(nod.GetAt(Key), OpenMode.ForRead) is not Xrecord xr || xr.Data == null) return false;
            var v = xr.Data.AsArray();
            if (v.Length < 2 || v[0].Value is not double a || v[1].Value is not double b || Check(a, b) != null) return false;
            minor = a; major = b;
            return true;
        }
        catch { return false; }
    }

    /// <summary>창을 열 때 — (주곡선, 계곡선) · 기록이 없으면 기본값.</summary>
    internal static (double Minor, double Major) Read(Database db)
    {
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            TryRead(tr, db, out double mi, out double ma);
            tr.Commit();
            return (mi, ma);
        }
        catch { return (DefaultMinor, DefaultMajor); }
    }

    /// <summary>명령 안(문서 잠김)에서 부른다.</summary>
    internal static void Write(Database db, double minor, double major)
    {
        using var tr = db.TransactionManager.StartTransaction();
        var nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);
        Xrecord xr;
        if (nod.Contains(Key)) xr = (Xrecord)tr.GetObject(nod.GetAt(Key), OpenMode.ForWrite);
        else
        {
            nod.UpgradeOpen();
            xr = new Xrecord();
            nod.SetAt(Key, xr);
            tr.AddNewlyCreatedDBObject(xr, true);
        }
        xr.Data = new ResultBuffer(new TypedValue((int)DxfCode.Real, minor), new TypedValue((int)DxfCode.Real, major));
        tr.Commit();
    }

    /// <summary>검사 — 틀리면 창에 띄울 짧은 말, 맞으면 null.</summary>
    internal static string? Check(double minor, double major)
    {
        if (!double.IsFinite(minor) || !double.IsFinite(major)) return "숫자를 넣으세요";
        if (minor < MinValue || major < MinValue) return $"{MinValue.ToString("0.0", CultureInfo.InvariantCulture)} m 이상";
        double r = major / minor;
        if (r < 1 - 1e-9 || Math.Abs(r - Math.Round(r)) > 1e-6) return "계곡선 = 등고선 간격의 배수";
        return null;
    }

    static bool Same(double a, double b) => Math.Abs(a - b) <= 1e-9;

    /// <summary>스타일 하나에 간격을 넣고 되읽어 맞는지 — 이미 같으면 안 쓴다.
    /// <para>화면 없는 Civil 계측(1103 · V96): 2026은 보조 · 주를 어느 차례로 넣어도 받는다(2/10에서 보조 3 · 주 7 · 주 1 · 보조 20 다 받음).
    /// 그래도 v103.1 때처럼 중간 상태를 거부하는 판이 있을 수 있어 안 되면 차례를 바꿔 한 번 더 해 본다.</para></summary>
    internal static bool SetOn(CivilStyles.SurfaceStyle st, double minor, double major)
    {
        var cs = st.ContourStyle;
        bool Ok() { try { return Same(cs.MinorContourInterval, minor) && Same(cs.MajorContourInterval, major); } catch { return false; } }
        if (Ok()) return true;
        if (!st.IsWriteEnabled) st.UpgradeOpen();
        try { cs.MinorContourInterval = minor; cs.MajorContourInterval = major; } catch { }
        if (Ok()) return true;
        try { cs.MajorContourInterval = major; cs.MinorContourInterval = minor; } catch { }
        return Ok();
    }

    /// <summary>등고선을 그리는 스타일인가(평면이나 모형에서 주곡선 · 계곡선 하나라도 켜짐).</summary>
    static bool ShowsContours(CivilStyles.SurfaceStyle st)
    {
        foreach (var t in new[] { CivilStyles.SurfaceDisplayStyleType.MajorContour, CivilStyles.SurfaceDisplayStyleType.MinorContour })
        {
            try { if (st.GetDisplayStylePlan(t).Visible) return true; } catch { }
            try { if (st.GetDisplayStyleModel(t).Visible) return true; } catch { }
        }
        return false;
    }

    /// <summary>우리 스타일 이름인가 — 원지반 스타일 · 정지면 +1cm 사본 · 진입도로 면 스타일.</summary>
    static bool IsOurStyleName(string nm) =>
        string.Equals(nm, Commands.ImportGisCommand.GroundStyleName, StringComparison.OrdinalIgnoreCase)
        || nm.EndsWith(GradingBuilder.ContourLiftSuffix, StringComparison.OrdinalIgnoreCase)
        || string.Equals(nm, RoadSession.SurfStyleName, StringComparison.OrdinalIgnoreCase);

    /// <summary>[저장] 뒤 — 고칠 스타일 = 우리 이름의 스타일(원지반 · +1cm 사본 · 진입도로) + 원지반이나 보이는 우리 지표면(이름 끝 «_DH» …)이 쓰는 스타일 중 등고선을 그리는 것.
    /// 지층 숨김 스타일 · 등고선을 안 그리는 스타일 · 숨긴 중간 면만 쓰는 템플릿 스타일 · 우리 지표면이 안 쓰는 남의 스타일은 안 건드린다. 돌려줌 = 로그 한 줄.</summary>
    internal static string Apply(Database db, double minor, double major)
    {
        var cdoc = Autodesk.Civil.ApplicationServices.CivilDocument.GetCivilDocument(db);
        ObjectId groundId = ObjectId.Null;
        try { foreach (var sp in Commands.SectionCommand.FindSurfaces(db, cdoc)) if (sp.Label == "원지반") { groundId = sp.SurfId; break; } } catch { }

        var targets = new Dictionary<ObjectId, string>();   // 스타일 → 이름
        var why = new Dictionary<ObjectId, List<string>>(); // 스타일 → 쓰는 우리 지표면
        int others = 0, ok = 0;
        var bad = new List<string>();
        using var tr = db.TransactionManager.StartTransaction();
        foreach (ObjectId sid in cdoc.Styles.SurfaceStyles)
        {
            try
            {
                if (tr.GetObject(sid, OpenMode.ForRead) is CivilStyles.SurfaceStyle st && IsOurStyleName(st.Name))
                { targets[sid] = st.Name; why[sid] = new List<string>(); }
            }
            catch { }
        }
        var otherUse = new List<ObjectId>();   // 우리 것 아닌 지표면이 쓰는 스타일(겹치면 알림)
        foreach (ObjectId id in cdoc.GetSurfaceIds())
        {
            try
            {
                if (tr.GetObject(id, OpenMode.ForRead) is not CivilDb.Surface s) continue;
                string nm = s.Name;
                bool ground = id == groundId || Commands.SectionCommand.IsBase(nm, Commands.ImportGisCommand.GroundSurfaceName);
                bool ours = ground || SelfCheck.IsOurSurfaceName(nm);
                if (!ours) { otherUse.Add(s.StyleId); continue; }
                if (s.StyleId.IsNull || tr.GetObject(s.StyleId, OpenMode.ForRead) is not CivilStyles.SurfaceStyle st) continue;
                if (!targets.ContainsKey(s.StyleId))
                {
                    if (string.Equals(st.Name, StrataDraw.HideStyleName, StringComparison.OrdinalIgnoreCase) || !ShowsContours(st)) continue;
                    // ★숨긴 중간 면(가상절토_DH · 정지순수_DH …)은 만들 때 도면 기본 스타일(«Contours 5m and 25m (Background)» 같은 템플릿)을 받는다 —
                    //   그것까지 고치면 이름이 거짓말이 되고 남의 지표면도 바뀐다 → 남의 이름 스타일은 원지반이나 보이는 우리 면이 쓸 때만.
                    if (!ground && !s.Visible) continue;
                    targets[s.StyleId] = st.Name; why[s.StyleId] = new List<string>();
                }
                why[s.StyleId].Add(nm);
            }
            catch { }
        }
        foreach (var kv in targets)
        {
            try
            {
                var st = (CivilStyles.SurfaceStyle)tr.GetObject(kv.Key, OpenMode.ForRead);
                if (SetOn(st, minor, major)) ok++; else bad.Add(kv.Value);
            }
            catch (System.Exception ex) { bad.Add($"{kv.Value}({ex.GetType().Name})"); }
        }
        foreach (var o in otherUse) if (targets.ContainsKey(o) && !IsOurStyleName(targets[o])) others++;
        tr.Commit();

        var ci = CultureInfo.InvariantCulture;
        string list = string.Join(" · ", targets.Select(kv => why[kv.Key].Count > 0 ? $"«{kv.Value}»({string.Join(",", why[kv.Key])})" : $"«{kv.Value}»"));
        return $"등고선 {minor.ToString("0.###", ci)} m · 계곡선 {major.ToString("0.###", ci)} m — 스타일 {ok}/{targets.Count}개 고침"
             + (targets.Count > 0 ? $": {list}" : "")
             + (bad.Count > 0 ? $" · ⚠못 고침 {string.Join(", ", bad)}" : "")
             + (others > 0 ? $" · 같은 스타일을 쓰는 다른 지표면 {others}개도 따라 바뀜" : "");
    }
}
