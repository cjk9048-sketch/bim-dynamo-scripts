using Autodesk.AutoCAD.DatabaseServices;
using DH.Grading.Core;
using AcadEntity = Autodesk.AutoCAD.DatabaseServices.Entity;

namespace DH.Grading.Civil;

/// <summary>★★★[v103.0 · JACK 0930 «옹벽이 남아야»] 옹벽 결과물의 <b>번호 표지</b> — 옹벽마다 따로 지우고 숨기려고.
/// <para>v102까지는 결과물이 한 벌이라 레이어를 통째로 비우고(DrawLinesOnLayer · WallDaylightBuilder.EraseOld) 같은 이름을 지워(BuildVirtualSlope),
/// 두 번째 옹벽 변환이 첫 옹벽을 지웠다. 이제 선은 XData <see cref="AppName"/> = (번호, 구역 계획선 핸들) ·
/// 표면은 이름 <c>가상옹벽{번호}_DH</c> · <c>순수옹벽{번호}_DH</c> + 확장 사전 <see cref="SurfKey"/>로 가린다.</para>
/// <para>구역 핸들 — «이어서»가 목록을 비워도 앞 구역 옹벽 결과물은 숨긴 채 남는다(정지면_DH이전에 굳은 옹벽의 기록). 새 구역의 정리가 그것을 안 건드리게 한다
/// (v102.2 코드 검토 낮음 9).</para></summary>
public static class WallTags
{
    public const string AppName = "DHGRADE_WALL";
    public const string SurfKey = "DH_WALLNO";
    /// <summary>옹벽 선이 사는 레이어 — 폴리곤 · 옹벽 줄 · 데이라잇(원지반 · 계획 · 띠 — 각 확인용 포함).</summary>
    public static readonly string[] Layers =
    {
        "DH-가상폴리곤", "DH-가상옹벽선",
        WallDaylightBuilder.Ground.Layer, WallDaylightBuilder.Ground.LayerSuspect,
        WallDaylightBuilder.Plan.Layer, WallDaylightBuilder.Plan.LayerSuspect,
        WallDaylightBuilder.BandLayer, WallDaylightBuilder.BandLayerSuspect,
    };
    /// <summary>v102 한 벌 이름(표지 없음) — 옮길 때 지운다.</summary>
    public const string LegacyWall = "가상옹벽_DH";
    public static string LegacyPure => WallDaylightBuilder.PureName;

    static readonly System.Text.RegularExpressions.Regex WallName = new(@"^(가상|순수)옹벽(\d+)_DH(_\d+)?$");
    /// <summary>번호 붙은 옹벽 표면 이름인가(보기 · 초기화 · 정리가 같이 쓴다 — 계획 검토 v103 · M9) — 번호를 준다.</summary>
    public static bool IsWallSurface(string name, out int no)
    {
        no = 0;
        var m = WallName.Match(name ?? "");
        return m.Success && int.TryParse(m.Groups[2].Value, out no) && no > 0;
    }
    /// <summary>v102 한 벌 이름(가상옹벽_DH · 순수옹벽_DH · 그 _N)인가.</summary>
    public static bool IsLegacySurface(string name)
    {
        static bool Base(string nm, string b) => nm == b || (nm.StartsWith(b + "_") && int.TryParse(nm.Substring(b.Length + 1), out _));
        return Base(name ?? "", LegacyWall) || Base(name ?? "", LegacyPure);
    }

    public static void Tag(Database db, Transaction tr, AcadEntity e, int no, string region)
    {
        GradingBuilder.EnsureRegApp(db, tr, AppName);
        if (!e.IsWriteEnabled) e.UpgradeOpen();
        using var rb = new ResultBuffer(
            new TypedValue((int)DxfCode.ExtendedDataRegAppName, AppName),
            new TypedValue((int)DxfCode.ExtendedDataInteger32, no),
            new TypedValue((int)DxfCode.ExtendedDataAsciiString, region ?? ""));
        e.XData = rb;
    }

    public static (int No, string Region)? Read(AcadEntity e)
    {
        try
        {
            using var rb = e.GetXDataForApplication(AppName);
            if (rb == null) return null;
            int no = 0; string reg = "";
            foreach (TypedValue tv in rb.AsArray())
            {
                if (tv.TypeCode == (short)DxfCode.ExtendedDataInteger32) no = System.Convert.ToInt32(tv.Value);
                else if (tv.TypeCode == (short)DxfCode.ExtendedDataAsciiString) reg = tv.Value as string ?? "";
            }
            return no > 0 ? (no, reg) : null;
        }
        catch { return null; }
    }

    public static void TagSurface(Transaction tr, ObjectId sid, int no, string region)
    {
        var o = tr.GetObject(sid, OpenMode.ForWrite);
        if (o.ExtensionDictionary.IsNull) o.CreateExtensionDictionary();
        var d = (DBDictionary)tr.GetObject(o.ExtensionDictionary, OpenMode.ForWrite);
        if (d.Contains(SurfKey)) d.Remove(SurfKey);
        var xr = new Xrecord { Data = new ResultBuffer(new TypedValue((int)DxfCode.Int32, no), new TypedValue((int)DxfCode.Text, region ?? "")) };
        d.SetAt(SurfKey, xr);
        tr.AddNewlyCreatedDBObject(xr, true);
    }

    public static (int No, string Region)? ReadSurface(Transaction tr, DBObject o)
    {
        try
        {
            if (o.ExtensionDictionary.IsNull) return null;
            if (tr.GetObject(o.ExtensionDictionary, OpenMode.ForRead) is not DBDictionary d || !d.Contains(SurfKey)) return null;
            if (tr.GetObject(d.GetAt(SurfKey), OpenMode.ForRead) is not Xrecord xr || xr.Data == null) return null;
            var a = xr.Data.AsArray();
            int no = a.Length >= 1 ? System.Convert.ToInt32(a[0].Value) : 0;
            string reg = a.Length >= 2 ? a[1].Value as string ?? "" : "";
            return no > 0 ? (no, reg) : null;
        }
        catch { return null; }
    }

    /// <summary>지운 것 · 못 지운 것 · 번호들.</summary>
    public sealed class EraseReport
    {
        public int Surfaces, Lines, Stuck, LegacySurfaces, LegacyLines;
        public SortedSet<int> Nos = new();
        public string Text => $"표면 {Surfaces}{(LegacySurfaces > 0 ? $"(v102 한 벌 {LegacySurfaces})" : "")} · 선 {Lines}{(LegacyLines > 0 ? $"(v102 한 벌 {LegacyLines})" : "")}"
                            + (Nos.Count > 0 ? $" · 번호 {string.Join(",", Nos)}" : "") + (Stuck > 0 ? $" · ⚠못 지운 것 {Stuck}(레이어가 잠겼는지 보세요)" : "");
    }

    /// <summary>옹벽 결과물을 지운다.</summary>
    /// <param name="pick">지울 옹벽인가(번호, 구역) — 표지 있는 표면·선에 묻는다.</param>
    /// <param name="legacy">v102 한 벌(표지 없는 가상옹벽_DH · 순수옹벽_DH · 옹벽 레이어의 표지 없는 선)도 지운다.</param>
    /// <param name="protect">원지반 — 이름이 무엇이든 안 지운다.</param>
    public static EraseReport Erase(Database db, Transaction tr, System.Func<int, string, bool> pick, bool legacy, ObjectId protect)
    {
        var rep = new EraseReport();
        var civilDoc = Autodesk.Civil.ApplicationServices.CivilApplication.ActiveDocument;
        var victims = new List<(ObjectId Id, bool Legacy, int No)>();
        foreach (ObjectId sid in civilDoc.GetSurfaceIds())
        {
            if (sid == protect) continue;
            if (tr.GetObject(sid, OpenMode.ForRead) is not Autodesk.Civil.DatabaseServices.Surface s) continue;
            if (IsWallSurface(s.Name, out int nn))
            {
                var t = ReadSurface(tr, s);
                // 표지가 있으면 표지로 · 없으면 이름의 번호로(구역은 모름 — 빈 구역으로 묻는다)
                if (t != null ? pick(t.Value.No, t.Value.Region) : pick(nn, "")) victims.Add((sid, false, t?.No ?? nn));
            }
            else if (legacy && IsLegacySurface(s.Name) && ReadSurface(tr, s) == null) victims.Add((sid, true, 0));
        }
        foreach (var (id, lg, no) in victims)
        {
            try { tr.GetObject(id, OpenMode.ForWrite).Erase(); if (lg) rep.LegacySurfaces++; else { rep.Surfaces++; rep.Nos.Add(no); } }
            catch { rep.Stuck++; }
        }
        var set = new HashSet<string>(Layers, System.StringComparer.OrdinalIgnoreCase);
        var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
        var lines = new List<(ObjectId Id, bool Legacy, int No)>();
        foreach (ObjectId id in ms)
        {
            if (tr.GetObject(id, OpenMode.ForRead) is not AcadEntity e || !set.Contains(e.Layer)) continue;
            var t = Read(e);
            if (t != null) { if (pick(t.Value.No, t.Value.Region)) lines.Add((id, false, t.Value.No)); }
            else if (legacy) lines.Add((id, true, 0));
        }
        foreach (var (id, lg, no) in lines)
        {
            try { tr.GetObject(id, OpenMode.ForWrite).Erase(); if (lg) rep.LegacyLines++; else { rep.Lines++; rep.Nos.Add(no); } }
            catch { rep.Stuck++; }
        }
        return rep;
    }

    /// <summary>v102 한 벌 결과물이 있는가(표지 없는 가상옹벽_DH · 순수옹벽_DH · 옹벽 레이어의 표지 없는 선) — 첫 v103 옹벽 변환이 옮긴다.</summary>
    public static (int Surfaces, int Lines) CountLegacy(Database db, Transaction tr, ObjectId protect)
    {
        int s0 = 0, l0 = 0;
        var civilDoc = Autodesk.Civil.ApplicationServices.CivilApplication.ActiveDocument;
        foreach (ObjectId sid in civilDoc.GetSurfaceIds())
        {
            if (sid == protect) continue;
            if (tr.GetObject(sid, OpenMode.ForRead) is Autodesk.Civil.DatabaseServices.Surface s && IsLegacySurface(s.Name) && ReadSurface(tr, s) == null) s0++;
        }
        var set = new HashSet<string>(Layers, System.StringComparer.OrdinalIgnoreCase);
        var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
        foreach (ObjectId id in ms)
            if (tr.GetObject(id, OpenMode.ForRead) is AcadEntity e && set.Contains(e.Layer) && Read(e) == null) l0++;
        return (s0, l0);
    }

    /// <summary>번호 붙은 옹벽 표면 보이기·숨기기 — 바뀐 수.</summary>
    public static int SetWallSurfacesVisible(Transaction tr, bool visible)
    {
        int n = 0;
        var civilDoc = Autodesk.Civil.ApplicationServices.CivilApplication.ActiveDocument;
        foreach (ObjectId sid in civilDoc.GetSurfaceIds())
        {
            if (tr.GetObject(sid, OpenMode.ForRead) is not Autodesk.Civil.DatabaseServices.Surface s || !IsWallSurface(s.Name, out _)) continue;
            try
            {
                if (((AcadEntity)s).Visible != visible) { ((AcadEntity)tr.GetObject(sid, OpenMode.ForWrite)).Visible = visible; n++; }
            }
            catch { }
        }
        return n;
    }
}
