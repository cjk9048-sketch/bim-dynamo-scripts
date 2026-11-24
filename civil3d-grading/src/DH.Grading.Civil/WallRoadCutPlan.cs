using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using DH.Grading.Core;
using AcadEntity = Autodesk.AutoCAD.DatabaseServices.Entity;

namespace DH.Grading.Civil;

/// <summary>★★[JACK 1124 «진입도로로 인해 옹벽부가 깎일 경우 옹벽 객체도 수정되어야 해» · 답 «도로 자리만 잘라냄»] 부지 평면 옹벽선(DH-옹벽선*)을 진입도로 자리 밖만 보이게.
/// <list type="bullet">
/// <item>원래 선은 지우지 않고 숨긴다 — 얼린 전용 레이어 <see cref="HidLayer"/>로 옮김(표지 <see cref="App"/> «숨김» + 원래 레이어 이름 · ★[검토 중간 3] Visible 끔은 UNISOLATEOBJECTS가 되살렸다).
/// 옹벽 기록 · 찍기 꼬리표는 그대로 — 원선이 필요한 곳(옹벽 변환의 되돌릴 밑천 · 레이어 통째 지우기)은 표지로 찾는다(<see cref="HiddenOf"/> · <see cref="HiddenSource"/>).
/// 보이는 것은 자리 밖 조각(표지 «조각» · 원래 XData 복사 · 찍기 uid 새 번호).</item>
/// <item>부를 때마다 먼저 되돌린다(조각 지움 · 숨긴 선을 원래 레이어로) → 지금 도로 자리(<see cref="FinalSurfaceBuilder.RoadCut"/> — 최종면 합치기와 같은 발자국)로 다시 자른다. 도로 없음 = 온전한 옹벽선.</item>
/// <item>부르는 곳: 최종지표면 짓기 뒤(도로 [생성] · [업데이트] · 부지 다시 → 도로 자동 갱신) · 노리선 그리기 뒤(번들 · 선 옹벽 옹벽선을 다시 그림) · 진입도로 초기화 뒤 · 옹벽 변환 끝(되돌리기 포함) · 기반 지우기 뒤.</item>
/// </list>
/// 도면화 평면은 모형 공간 레이어를 그대로 보이므로 따라온다(숨김 레이어는 «DH-옹벽선»으로 시작하지 않아 안 그려짐).</summary>
internal static class WallRoadCutPlan
{
    internal const string App = "DH_WALLROADCUT";
    /// <summary>숨긴 원선이 가는 레이어 — 얼림 · 출력 안 함 · «DH-옹벽선»으로 시작하지 않음(옹벽선을 레이어로 읽는 곳이 안 집는다).</summary>
    internal const string HidLayer = "DH-숨김-옹벽선원본";
    const string Hid = "숨김", Piece = "조각", DefaultLayer = "DH-옹벽선";

    static bool SiteWallLayer(string n) => n.StartsWith("DH-옹벽선", System.StringComparison.Ordinal);

    static (string Kind, string Arg) MarkOf(AcadEntity e)
    {
        try
        {
            using var rb = e.GetXDataForApplication(App);
            var v = rb?.AsArray();
            return v != null && v.Length >= 2 ? (v[1].Value as string ?? "", v.Length >= 3 ? v[2].Value as string ?? "" : "") : ("", "");
        }
        catch { return ("", ""); }
    }

    /// <summary>진입도로 자리로 잘라 새로 그린 조각인가(원선이 진짜 — 읽는 쪽은 건너뛴다).</summary>
    internal static bool IsPiece(AcadEntity e) => MarkOf(e).Kind == Piece;

    /// <summary>숨긴 원선이면 원래 레이어 이름(옛 표지면 «DH-옹벽선») · 아니면 null.</summary>
    internal static string? HiddenSource(AcadEntity e)
    {
        var (k, a) = MarkOf(e);
        return k == Hid ? (a.Length > 0 ? a : DefaultLayer) : null;
    }

    /// <summary>원래 레이어가 <paramref name="layer"/>인 숨긴 원선 — 그 레이어를 통째로 지울 때 함께 지운다(<see cref="GradingBuilder.EraseOnLayer"/> — 안 지우면 다음 되돌리기에 옛 선이 살아난다).</summary>
    internal static List<ObjectId> HiddenOf(Database db, Transaction tr, string layer)
    {
        var r = new List<ObjectId>();
        var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
        if (!lt.Has(HidLayer)) return r;
        var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
        foreach (ObjectId id in ms)
        {
            if (id.IsErased || tr.GetObject(id, OpenMode.ForRead) is not AcadEntity e || e.Layer != HidLayer) continue;
            if (string.Equals(HiddenSource(e), layer, System.StringComparison.OrdinalIgnoreCase)) r.Add(id);
        }
        return r;
    }

    static List<Point3>? PointsOf(Transaction tr, AcadEntity e)
    {
        var r = new List<Point3>();
        switch (e)
        {
            case Polyline p: for (int i = 0; i < p.NumberOfVertices; i++) { var q = p.GetPoint3dAt(i); r.Add(new Point3(q.X, q.Y, q.Z)); } if (p.Closed && r.Count > 2) r.Add(r[0]); break;
            case Polyline3d p3:
                foreach (ObjectId vid in p3) if (tr.GetObject(vid, OpenMode.ForRead) is PolylineVertex3d v) r.Add(new Point3(v.Position.X, v.Position.Y, v.Position.Z));
                if (p3.Closed && r.Count > 2) r.Add(r[0]);
                break;
            case Line l: r.Add(new Point3(l.StartPoint.X, l.StartPoint.Y, l.StartPoint.Z)); r.Add(new Point3(l.EndPoint.X, l.EndPoint.Y, l.EndPoint.Z)); break;
            default: return null;
        }
        return r.Count >= 2 ? r : null;
    }

    /// <summary>자리 밖 조각 — 안 잘렸으면 null. ★[검토 낮음 4] 잘린 조각 중 <see cref="WallRoadCut.MinPiece"/> 미만은 버림(바닥 줄과 같은 값).</summary>
    static List<List<Point3>>? Runs(RoadFootprint.Cutter cut, List<Point3> pts)
    {
        var runs = cut.Line(pts);
        bool same = runs.Count == 1 && runs[0].Count == pts.Count && runs[0][0].Equals(pts[0]) && runs[0][^1].Equals(pts[^1]);
        if (same) return null;
        runs.RemoveAll(r => WallRoadCut.Len(r) < WallRoadCut.MinPiece);
        return runs;
    }

    static string P(Point3 q) => string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0:F2},{1:F2};", q.X, q.Y);

    /// <summary>★[검토 중간 1] 지금 진입도로 자리가 부지 평면 옹벽선에 하는 일의 지문(남는 조각 끝 · 다 빠진 선 — cm · 원선 기준 · 평면을 아직 안 맞췄어도 같은 값).
    /// 자리가 옹벽을 안 건드리면 빈 글. 옹벽 터파기 도장 머리(<see cref="WallExcavBuilder"/>)에 들어가 도로를 만들거나 바꾸거나 지우면 단면 기록 · 터파기면 · 횡단 «바뀜»이 따라온다
    /// (옹벽에서 먼 도로만 바뀌면 그대로 — 되씀).</summary>
    internal static string Print(Database db, Transaction tr)
    {
        RoadFootprint.Cutter? cut;
        try { cut = FinalSurfaceBuilder.RoadCut(db, tr, out _, out _); } catch { return "?"; }
        if (cut == null) return "";
        var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
        var keys = new List<string>();
        foreach (ObjectId id in ms)
        {
            if (id.IsErased || tr.GetObject(id, OpenMode.ForRead) is not AcadEntity e) continue;
            string ly = e.Layer ?? "";
            if (ly == HidLayer) { if (HiddenSource(e) == null) continue; }
            else if (!SiteWallLayer(ly) || !e.Visible || IsPiece(e)) continue;
            var pts = PointsOf(tr, e);
            if (pts == null) continue;
            var runs = Runs(cut, pts);
            if (runs == null) continue;
            keys.Add(runs.Count == 0 ? "X" + P(pts[0]) + P(pts[^1]) : string.Concat(runs.Select(r => P(r[0]) + P(r[^1]))));
        }
        if (keys.Count == 0) return "";
        keys.Sort(System.StringComparer.Ordinal);
        using var sha = System.Security.Cryptography.SHA256.Create();
        return System.Convert.ToHexString(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(string.Join("|", keys)))).Substring(0, 12);
    }

    static readonly HashSet<System.IntPtr> _changed = new();
    static readonly Dictionary<System.IntPtr, string> _sig = new();

    /// <summary>되돌리고 다시 자른다(자기 트랜잭션 — 실패하면 평면은 그대로). 돌려줌 = 로그 한 줄(한 일 없으면 빈 글).</summary>
    internal static string Refresh(Database db) => Refresh(db, out _);

    /// <param name="changed">자른 결과(조각 끝 · 숨긴 수)가 지난번과 다름 — 부지 종단 · 횡단을 다시 그릴 까닭(<see cref="ResyncSiteViews"/>).</param>
    internal static string Refresh(Database db, out bool changed)
    {
        changed = false;
        using var tr = db.TransactionManager.StartTransaction();
        var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
        int erased = 0, restored = 0, hidden = 0, pieces = 0, uidMax = 0;
        var lines = new List<ObjectId>();
        var before = new List<string>(); var after = new List<string>();
        foreach (ObjectId id in ms)
        {
            if (id.IsErased || tr.GetObject(id, OpenMode.ForRead) is not AcadEntity e) continue;
            string ly = e.Layer ?? "";
            bool hidLy = ly == HidLayer;
            if (!hidLy && !SiteWallLayer(ly)) { if (e is Curve) uidMax = System.Math.Max(uidMax, PickUid(e)); continue; }   // 찍기 uid는 사면 · 소단선과 한 번호 공간
            var (mk, arg) = MarkOf(e);
            if (mk == Piece)
            {
                var pp = PointsOf(tr, e);
                if (pp != null) before.Add(P(pp[0]) + P(pp[^1]));
                e.UpgradeOpen(); e.Erase(); erased++; continue;
            }
            if (mk == Hid)
            {
                e.UpgradeOpen();
                if (hidLy) e.Layer = RestoreLayer(db, tr, arg);
                e.Visible = true;   // 옛 표지(Visible 끔 · 레이어 그대로)도 함께 풂
                e.XData = new ResultBuffer(new TypedValue((int)DxfCode.ExtendedDataRegAppName, App));   // 표지만 지움
                restored++;
            }
            else if (hidLy) continue;   // 숨김 레이어의 표지 없는 것은 안 건드림
            if (e.Visible && e is Polyline or Polyline3d or Line) lines.Add(id);
            uidMax = System.Math.Max(uidMax, PickUid(e));
        }
        var cut = FinalSurfaceBuilder.RoadCut(db, tr, out _, out string note);
        double lenCut = 0;
        if (cut != null && lines.Count > 0)
        {
            GradingBuilder.EnsureRegApp(db, tr, App);
            ObjectId hidId = ObjectId.Null;
            foreach (var id in lines)
            {
                var e = (AcadEntity)tr.GetObject(id, OpenMode.ForRead);
                var pts = PointsOf(tr, e);
                if (pts == null) continue;
                var runs = Runs(cut, pts);
                if (runs == null) continue;
                if (!ms.IsWriteEnabled) ms.UpgradeOpen();
                foreach (var run in runs)
                {
                    var pl = new Polyline3d();
                    pl.SetPropertiesFrom(e);
                    ms.AppendEntity(pl); tr.AddNewlyCreatedDBObject(pl, true);
                    foreach (var q in run) { var v = new PolylineVertex3d(new Point3d(q.X, q.Y, q.Z)); pl.AppendVertex(v); tr.AddNewlyCreatedDBObject(v, true); }
                    var xd = CopyXData(e, ref uidMax);
                    if (xd != null) pl.XData = xd;
                    pl.XData = Mark(Piece, e.Handle.ToString());
                    after.Add(P(run[0]) + P(run[^1]));
                    pieces++;
                }
                lenCut += WallRoadCut.Len(pts) - runs.Sum(r => WallRoadCut.Len(r));
                if (hidId.IsNull) hidId = EnsureHidLayer(db, tr);
                e.UpgradeOpen();
                e.XData = Mark(Hid, e.Layer);
                e.LayerId = hidId;
                hidden++;
            }
        }
        before.Sort(System.StringComparer.Ordinal); after.Sort(System.StringComparer.Ordinal);
        string sigAfter = string.Concat(after) + "H" + hidden;
        tr.Commit();
        // 지난 결과와 견줌 — 이 세션에서 맞춘 적이 있으면 그 기억(노리선 · 옹벽 변환이 레이어를 통째로 지우고 다시 그린 뒤에도 헛된 «바뀜» · 놓친 «바뀜»이 없게), 처음이면 도면에 남은 조각
        lock (_changed)
        {
            string sigBefore = _sig.TryGetValue(db.UnmanagedObject, out var s0) ? s0 : string.Concat(before) + "H" + restored;
            _sig[db.UnmanagedObject] = sigAfter;
            changed = sigBefore != sigAfter;
            if (changed) _changed.Add(db.UnmanagedObject);
        }
        if (erased + restored + hidden == 0 && note.Length == 0) return "";
        return string.Format(System.Globalization.CultureInfo.InvariantCulture,
            "옹벽선 진입도로 자리 — 되돌림 {0}(조각 지움 {1}) · 숨김 {2} · 조각 {3} · 뺀 길이 {4:F1} m{5}{6}",
            restored, erased, hidden, pieces, lenCut, changed ? " · 바뀜" : "", note.Length > 0 ? " · ⚠" + note : "");
    }

    /// <summary>숨긴 선을 돌려놓을 레이어 — 기록된 이름이 지금 있으면 그것 · 아니면 «DH-옹벽선».</summary>
    static string RestoreLayer(Database db, Transaction tr, string name)
    {
        var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
        if (name.Length > 0 && lt.Has(name)) return name;
        GradingBuilder.EnsureLayer(db, tr, DefaultLayer, 1);
        return DefaultLayer;
    }

    static ObjectId EnsureHidLayer(Database db, Transaction tr)
    {
        var id = GradingBuilder.EnsureLayer(db, tr, HidLayer, 8);
        var ltr = (LayerTableRecord)tr.GetObject(id, OpenMode.ForRead);
        bool freeze = !ltr.IsFrozen && db.Clayer != id;
        if (freeze || ltr.IsPlottable)
        {
            ltr.UpgradeOpen();
            if (freeze) ltr.IsFrozen = true;
            ltr.IsPlottable = false;
        }
        return id;
    }

    /// <summary>★[검토 GAP] 진입도로 [생성] · [업데이트] · 초기화로 자른 결과가 바뀌었으면(<see cref="Refresh(Database, out bool)"/>) 그려 둔 부지 종단 · 횡단을 같은 자리에 다시 —
    /// 옹벽 설정 [적용]과 같은 길(<see cref="Commands.ProfileCommand.Rebuild(Document, out bool, string)"/> → <see cref="Commands.XsecViewCommand.Refresh"/>).
    /// 부지 고침 뒤 한꺼번에 하는 길(<see cref="Commands.NoriCommand.AutoHold"/> &gt; 0 — <see cref="Commands.SiteSheetsRefresh"/>)에서는 안 함(그 끝에서 다시 부름). 돌려줌 = 한 줄(안 했으면 빈 글).</summary>
    internal static string ResyncSiteViews(Document doc, string who)
    {
        if (doc == null || Commands.NoriCommand.AutoHold > 0) return "";
        var db = doc.Database;
        lock (_changed) if (!_changed.Remove(db.UnmanagedObject)) return "";
        if (!Commands.ProfileCommand.ReadExistingRoute(db, out _, out _, out _)) return "";
        // ★[1124 마무리 — 도우미 남김 «부지 노선 여럿이면 [적용]처럼 다시 그려 마지막 노선만 남음»] 종단 갱신(SiteSheetsRefresh)과 같은 막음 — 손대지 않고 알림만
        int nAlign = 0;
        try { nAlign = Commands.SiteSheetsRefresh.SiteAlignCount(db); } catch { }
        if (nAlign > 1)
        {
            string why = $"부지 종단 노선 {nAlign}개 → 저절로 다시 그리지 않음 · 노선마다 계획지표면 [종단도]";
            try { doc.Editor.WriteMessage($"\n[{who}] 옹벽이 진입도로 자리로 바뀜 — {why}"); } catch { }
            try { DiagLog.Append($"\n■ 옹벽 진입도로 자리 바뀜 — {who} 뒤 {why}\n"); } catch { }
            return why;
        }
        bool hasX = Commands.SheetSettingsCommand.HasSiteXsec(db);
        bool pOk = Commands.ProfileCommand.Rebuild(doc, out bool started, who);
        bool xOk = true;
        if (hasX && started)
        {
            BusyPopup.Show(Commands.ProfileCommand.BusyText);
            try { xOk = Commands.XsecViewCommand.Refresh(doc); }
            catch (System.Exception ex) { xOk = false; try { DiagLog.Append("\n■ 옹벽 진입도로 자리 — 부지 횡단 예외 " + ex.Message + "\n"); } catch { } }
            finally { BusyPopup.Close(); }
        }
        string res = $"부지 종단 {(pOk ? "다시 그림" : started ? "⚠다시 그리다 멈춤" : "⚠못 그림")}"
                   + (hasX ? $" · 횡단 {(!started ? "그대로" : xOk ? "다시 그림" : "⚠못 그림 — 계획지표면 [횡단도]")}" : "");
        try { doc.Editor.WriteMessage($"\n[{who}] 옹벽이 진입도로 자리로 바뀌어 {res}"); } catch { }
        try { DiagLog.Append($"\n■ 옹벽 진입도로 자리 바뀜 — {who} 뒤 {res}\n"); } catch { }
        try { SelfCheckLog.Line("옹벽 진입도로 자리 — 부지 종단 · 횡단", pOk && xOk, $"{who} 뒤 — {res}", doc.Name); } catch { }
        return res;
    }

    /// <summary>부지 종단을 새로 지으면(지금 자리로 옹벽 터파기 · 단면 기록을 세움) 밀린 다시 그리기는 필요 없다.</summary>
    internal static void ClearChanged(Database db) { lock (_changed) _changed.Remove(db.UnmanagedObject); }

    static ResultBuffer Mark(string kind, string src) => new(
        new TypedValue((int)DxfCode.ExtendedDataRegAppName, App),
        new TypedValue((int)DxfCode.ExtendedDataAsciiString, kind),
        new TypedValue((int)DxfCode.ExtendedDataAsciiString, src));

    static int PickUid(AcadEntity e)
    {
        try
        {
            using var rb = e.GetXDataForApplication(GradingSettings.WallPickAppName);
            var v = rb?.AsArray();
            return v != null && v.Length >= 5 ? System.Convert.ToInt32(v[4].Value) : 0;
        }
        catch { return 0; }
    }

    /// <summary>원래 선의 XData 전부(우리 표지 빼고) — 찍기 꼬리표 uid만 새 번호(같은 uid 둘이면 [사면→옹벽] 두 번째 클릭이 첫 선택을 푼다 — WallLinePlan과 같은 법).</summary>
    static ResultBuffer? CopyXData(AcadEntity e, ref int uid)
    {
        using var all = e.XData;
        if (all == null) return null;
        var o = new List<TypedValue>();
        string app = ""; int k = 0;
        foreach (TypedValue tv in all.AsArray())
        {
            if (tv.TypeCode == (short)DxfCode.ExtendedDataRegAppName) { app = tv.Value as string ?? ""; k = 0; if (app != App) o.Add(tv); continue; }
            if (app == App) continue;
            k++;
            if (app == GradingSettings.WallPickAppName && k == 4) { uid = System.Math.Min(uid + 1, short.MaxValue); o.Add(new TypedValue((int)DxfCode.ExtendedDataInteger16, (short)uid)); }
            else o.Add(tv);
        }
        return o.Count > 0 ? new ResultBuffer(o.ToArray()) : null;
    }
}
