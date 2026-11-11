using System.IO;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using DH.Grading.Core;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;
using CivilDb = Autodesk.Civil.DatabaseServices;

namespace DH.Grading.Civil.Commands;

/// <summary>★★★[v115 · JACK 1105 Revit 구조물 · 계획 5.4] [기타] → [구조물] — 불러오기(DHSTRUCT) · 좌표계 내보내기(DHSTRUCTPTS · v116.9) · 지우기(DHSTRUCTOFF).
/// [다시 반영](DHSTRUCTUPD)은 v116.9에서 없앰(JACK 1110) — 원본이 바뀌면 같은 파일을 다시 불러옴(바꿔치기).
/// 창 없음 · 끝 알림 한 줄 · 자세한 건 진단 로그. 보기 전용(지표면 · 토공량은 안 바꿈 — UndoGuard 대상 아님).
/// ★[v117.1] 좌표계 내보내기(DHSTRUCTPTS)는 팝업 + 3D 영역 — <c>StructCommand.Export.cs</c>.</summary>
public sealed partial class StructCommand
{
    static void Done(Editor ed, System.Text.StringBuilder log, string msg)
    {
        ed.WriteMessage("\n[구조물] " + msg);
        try { DiagLog.Append(log.ToString() + "  → " + msg + "\n"); } catch { }
    }

    static string? AskPath(Editor ed)
    {
        short fd = 1;
        try { fd = System.Convert.ToInt16(AcadApp.GetSystemVariable("FILEDIA")); } catch { }
        if (fd == 0 || SelfCheckLog.Headless)
        {
            var r = ed.GetString(new PromptStringOptions("\n[구조물] Revit 내보내기 파일(DWG · IFC) 경로: ") { AllowSpaces = true });
            string s = r.Status == PromptStatus.OK ? (r.StringResult ?? "").Trim().Trim('"') : "";
            return s.Length > 0 ? s : null;
        }
        // ★[v116.5 · JACK 1110 «구조물은 Revit에서 — rvt · ifc · fbx»] DWG · IFC를 읽음 · RVT · FBX · Navisworks는 골라도 까닭 · 내보내는 법을 알림
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Revit 내보내기 (*.dwg;*.ifc)|*.dwg;*.ifc|Revit 3D DWG (*.dwg)|*.dwg|IFC (*.ifc)|*.ifc|Revit · FBX · Navisworks (*.rvt;*.rfa;*.fbx;*.nwc;*.nwd) — 못 읽음(까닭 안내)|*.rvt;*.rfa;*.fbx;*.nwc;*.nwd",
            Title = "Revit 구조물 — DWG · IFC",
        };
        return dlg.ShowDialog() == true ? dlg.FileName : null;
    }

    /// <summary>옆 DB로 INSUNITS · 원본 XY 범위(원점 삽입 · 축척 1) — 붙이기 전에 단위 계수를 정한다(D7).</summary>
    static bool Probe(string path, out int insunits, out StructUnits.Box2 raw, out string why)
    {
        insunits = 0; raw = default; why = "";
        try
        {
            using var sdb = new Database(false, true);
            sdb.ReadDwgFile(path, FileShare.Read, true, null);
            sdb.CloseInput(true);
            insunits = (int)sdb.Insunits;
            Point3d a = sdb.Extmin, b = sdb.Extmax;
            if (!(a.X <= b.X && a.Y <= b.Y) || a.X < -1e20 || b.X > 1e20)
            {
                double x0 = double.MaxValue, y0 = double.MaxValue, x1 = double.MinValue, y1 = double.MinValue;
                using var tr = sdb.TransactionManager.StartTransaction();
                foreach (ObjectId id in (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(sdb), OpenMode.ForRead))
                    try { if (tr.GetObject(id, OpenMode.ForRead) is Entity e) { var ex = e.GeometricExtents; x0 = System.Math.Min(x0, ex.MinPoint.X); y0 = System.Math.Min(y0, ex.MinPoint.Y); x1 = System.Math.Max(x1, ex.MaxPoint.X); y1 = System.Math.Max(y1, ex.MaxPoint.Y); } } catch { }
                tr.Commit();
                if (x0 > x1) { why = "원본에 그림이 없다"; return false; }
                raw = new StructUnits.Box2(x0, y0, x1, y1);
            }
            else raw = new StructUnits.Box2(a.X, a.Y, b.X, b.Y);
            return true;
        }
        catch (System.Exception ex) { why = "원본을 못 읽음 — " + ex.Message; return false; }
    }

    /// <summary>구조물 하나를 지운다 — 그린 것(평면 · 종단 · 횡단 · 도로 장 것 포함) · xref · 기록. 원본 파일은 그대로.</summary>
    internal static bool Remove(Database db, int id, System.Text.StringBuilder log)
    {
        ObjectId btr = ObjectId.Null; int n = 0; string name = "";
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            if (!StructImport.TryLoad(db, tr, out var head, out var recs, out _)) { tr.Commit(); return false; }
            var r = recs.FirstOrDefault(x => x.Id == id);
            if (r == null) { tr.Commit(); return false; }
            name = r.Name;
            n = StructDraw.EraseTagged(db, tr, (_, _, sid, _) => sid == id);
            try
            {
                if (db.TryGetObjectId(new Handle(System.Convert.ToInt64(r.XrefHandle, 16)), out var iid) && !iid.IsErased && tr.GetObject(iid, OpenMode.ForWrite) is BlockReference br)
                { btr = br.BlockTableRecord; br.Erase(); }
            }
            catch { }
            recs.Remove(r);
            StructImport.Save(db, tr, head, recs);
            tr.Commit();
        }
        catch (System.Exception ex) { log.AppendLine("  지우기 실패 — " + ex.Message); return false; }
        if (!btr.IsNull) try { db.DetachXref(btr); } catch (System.Exception ex) { log.AppendLine("  xref 떼기 실패 — " + ex.Message); }
        StructImport.Forget(db, id);
        log.AppendLine($"  {name} 지움 — 그림 {n}");
        return true;
    }

    [CommandMethod("DHSTRUCT")]
    public void Import()
    {
        var doc = AcadApp.DocumentManager.MdiActiveDocument; if (doc == null) return;
        var ed = doc.Editor; var db = doc.Database;
        GradingSettings.SyncToDocument(doc);
        var log = new System.Text.StringBuilder("\n■ DHSTRUCT(구조물 불러오기)\n");
        string? path = AskPath(ed);
        if (path == null) { ed.WriteMessage("\n[구조물] 취소"); return; }
        try { path = Path.GetFullPath(path); } catch { }
        if (!File.Exists(path)) { Done(ed, log, "⚠파일 없음: " + path); return; }
        if (StructIfc.NotReadable(path) is { } nr0) { Done(ed, log, "⚠" + nr0); return; }
        string srcFile = path, ifcNote = "";
        if (string.Equals(Path.GetExtension(path), ".ifc", System.StringComparison.OrdinalIgnoreCase))
        {
            // ★[v116.5] IFC → 옆 DWG(«이름.ifc.dh.dwg» · m) → 아래 DWG 흐름 그대로
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var side = StructIfc.ToDwg(path, out ifcNote);
            log.AppendLine($"  IFC {path} → {(side ?? "실패")} · {ifcNote} · {sw.ElapsedMilliseconds} ms");
            if (side == null) { Done(ed, log, "⚠" + ifcNote); return; }
            path = side;
        }
        if (!Probe(path, out int ins, out var raw, out string pw)) { Done(ed, log, "⚠" + pw); return; }
        var ground = StructImport.GroundBox(db, out var gid);
        double f = StructUnits.PickFactor(ins, raw, ground, out string fnote);
        log.AppendLine($"  {path} · INSUNITS {ins} · 범위 {raw.X0:F1},{raw.Y0:F1} ~ {raw.X1:F1},{raw.Y1:F1} · 계수 {f} {fnote}");
        if (double.IsNaN(f))
        {
            // ★[v116.7 · JACK 1110] «프로젝트 기준점»으로 내보낸 IFC면 그 까닭을 앞에(공유 좌표 자체가 원점 근처인 경우는 fnote가 안내)
            string cb = srcFile != path ? StructIfc.CoordBaseHint(srcFile) : "";
            Done(ed, log, "⚠" + (cb.Length > 0 ? cb + " · " : "") + fnote); return;
        }
        string def = StructUnits.NameFromFile(srcFile);
        var nr = ed.GetString(new PromptStringOptions("\n[구조물] 이름") { AllowSpaces = true, DefaultValue = def, UseDefaultValue = true });   // 기본값 <이름>은 AutoCAD가 붙인다
        if (nr.Status != PromptStatus.OK) { ed.WriteMessage("\n[구조물] 취소"); return; }
        string name = (nr.StringResult ?? "").Trim(); if (name.Length == 0) name = def;

        // ★[D9] 같은 경로 = 바꿔치기
        var olds = new List<int>();
        using (var tr0 = db.TransactionManager.StartTransaction())
        {
            if (StructImport.TryLoad(db, tr0, out _, out var recs0, out _))
                olds.AddRange(recs0.Where(r => string.Equals(r.PathAbs, path, System.StringComparison.OrdinalIgnoreCase)).Select(r => r.Id));
            tr0.Commit();
        }
        foreach (int oid in olds) Remove(db, oid, log);
        // ★[D9] 다른 구조물과 상자가 거의 같으면 «같은 구조물?»(폴리메시판 · ACIS판 두 겹)
        var box = raw.Scale(f);
        var warn = new List<string>(fnote.StartsWith("⚠") ? new[] { fnote } : System.Array.Empty<string>());
        foreach (var o in StructImport.LoadAll(db, log, out _))
        {
            var m = o.Model;
            if (StructUnits.IoU(box, new StructUnits.Box2(m.MinX + m.Ox, m.MinY + m.Oy, m.MaxX + m.Ox, m.MaxY + m.Oy)) >= 0.8) warn.Add($"⚠같은 구조물? «{o.Rec.Name}»와 자리가 거의 같음");
        }

        int newId;
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            if (!StructImport.TryLoad(db, tr, out var head, out var recs, out string lw)) { tr.Commit(); Done(ed, log, "⚠" + lw); return; }
            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            newId = head.NextId;
            while (bt.Has(StructImport.BlockBase + newId)) newId++;
            var btrId = db.OverlayXref(path, StructImport.BlockBase + newId);
            var xb = (BlockTableRecord)tr.GetObject(btrId, OpenMode.ForWrite);
            string rel = "";
            try
            {
                string dir = StructImport.HostDir(db);
                if (dir.Length > 0 && Path.IsPathRooted(dir) && string.Equals(Path.GetPathRoot(dir), Path.GetPathRoot(path), System.StringComparison.OrdinalIgnoreCase))
                { rel = Path.GetRelativePath(dir, path); xb.PathName = rel; }
            }
            catch { rel = ""; }
            if (xb.XrefStatus != XrefStatus.Resolved) { tr.Abort(); try { db.DetachXref(btrId); } catch { } Done(ed, log, $"⚠xref를 못 풀었습니다({xb.XrefStatus})"); return; }
            var lay = SectionCommand.EnsureLayer(db, tr, StructImport.LayXref, 8);
            var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
            var br = new BlockReference(Point3d.Origin, btrId) { ScaleFactors = new Scale3d(f) };
            br.SetDatabaseDefaults(db); br.LayerId = lay;
            ms.AppendEntity(br); tr.AddNewlyCreatedDBObject(br, true);
            var fi = new FileInfo(path);
            recs.Add(new StructRec { Id = newId, Name = name, PathRel = rel, PathAbs = path, Size = fi.Length, Mtime = StructImport.MtimeOf(path), Sha16 = StructImport.Sha16(path), Factor = f, XrefHandle = br.Handle.ToString() });
            head.NextId = newId + 1;
            StructImport.Save(db, tr, head, recs);
            tr.Commit();
        }
        catch (System.Exception ex) { Done(ed, log, "⚠불러오기 실패 — " + ex.Message); return; }

        // 형상 읽기 · 기록 계측 · D8
        var all = StructImport.LoadAll(db, log, out var lwarn);
        warn.AddRange(lwarn);
        var me = all.FirstOrDefault(x => x.Rec.Id == newId);
        if (me == null || me.Model.Empty) { Done(ed, log, "⚠형상을 못 읽었습니다" + (me != null ? " — " + me.Note : "")); return; }
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            if (StructImport.TryLoad(db, tr, out var head, out var recs, out _))
            {
                var r = recs.First(x => x.Id == newId);
                r.Parts = me.Model.Parts.Count(p => !p.Excluded); r.Tris = me.Model.TriCount; r.OpenParts = me.Model.OpenParts;
                StructImport.Save(db, tr, head, recs);
            }
            if (!gid.IsNull && tr.GetObject(gid, OpenMode.ForRead) is CivilDb.TinSurface ts)
            {
                var m = me.Model;
                double gz = double.NaN;
                try { gz = ts.FindElevationAtXY((m.MinX + m.MaxX) / 2 + m.Ox, (m.MinY + m.MaxY) / 2 + m.Oy); } catch { }
                if (StructUnits.HeightOff(m.MaxZ + m.Oz, gz)) warn.Add($"⚠윗면 {m.MaxZ + m.Oz:F1} m · 원지반 {gz:F1} m — 높이를 확인하세요");
            }
            tr.Commit();
        }
        catch { }
        if (me.Model.Unused > 0) warn.Add($"안 쓴 개체 {me.Model.Unused}");
        if (ifcNote.Contains('⚠')) warn.Add(ifcNote[ifcNote.IndexOf('⚠')..]);   // ★[v116.5] IFC에서 못 읽은 형상 · 구멍(셈)
        string s = StructDraw.Reflect(db, log);
        Done(ed, log, s + (warn.Count > 0 ? " · " + string.Join(" · ", warn.Distinct()) : ""));
    }

    [CommandMethod("DHSTRUCTOFF")]
    public void Off()
    {
        var doc = AcadApp.DocumentManager.MdiActiveDocument; if (doc == null) return;
        var ed = doc.Editor; var db = doc.Database;
        var log = new System.Text.StringBuilder("\n■ DHSTRUCTOFF(구조물 지우기)\n");
        List<StructRec> recs;
        using (var tr = db.TransactionManager.StartTransaction()) { StructImport.TryLoad(db, tr, out _, out recs, out _); tr.Commit(); }
        if (recs.Count == 0) { ed.WriteMessage("\n[구조물] 없음"); return; }
        var pick = recs;
        if (recs.Count > 1)
        {
            var r = ed.GetString(new PromptStringOptions($"\n[구조물] 지울 이름({string.Join(" · ", recs.Select(x => x.Name))}) <전부>: ") { AllowSpaces = true });
            if (r.Status != PromptStatus.OK) { ed.WriteMessage("\n[구조물] 취소"); return; }
            string nm = (r.StringResult ?? "").Trim();
            if (nm.Length > 0 && nm != "전부")
            {
                pick = recs.Where(x => x.Name == nm).ToList();
                if (pick.Count == 0) { ed.WriteMessage($"\n[구조물] «{nm}» 없음"); return; }
            }
        }
        int n = pick.Count(x => Remove(db, x.Id, log));
        Done(ed, log, $"{n}개 지움({string.Join(", ", pick.Select(x => x.Name))}) — 원본 파일은 그대로");
    }
}
