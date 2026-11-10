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
/// 창 없음 · 끝 알림 한 줄 · 자세한 건 진단 로그. 보기 전용(지표면 · 토공량은 안 바꿈 — UndoGuard 대상 아님).</summary>
public sealed class StructCommand
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

    // ── ★★[v116.9 · JACK 1110 «구조물 좌표계 내보내기»] Civil → Revit 애드인 기준점 파일(계획_Civil구조물기준점_내보내기.md) ──
    //   인터뷰 1110: 도면 표시 안 남김 · 이름 기본값 = 이 도면에서 마지막에 쓴 이름(처음엔 도면 이름) · 원지반 + 최종 지표면 표고 둘 다
    static readonly Dictionary<string, string> LastPtsName = new(System.StringComparer.OrdinalIgnoreCase);
    static string LastPtsDir = "";

    static double? ElevAt(Transaction tr, ObjectId sid, double x, double y)
    {
        if (sid.IsNull || sid.IsErased) return null;
        try { return tr.GetObject(sid, OpenMode.ForRead) is CivilDb.TinSurface ts ? ts.FindElevationAtXY(x, y) : null; }
        catch { return null; }   // 면 밖
    }

    [CommandMethod("DHSTRUCTPTS")]
    public void ExportPoints()
    {
        var doc = AcadApp.DocumentManager.MdiActiveDocument; if (doc == null) return;
        var ed = doc.Editor; var db = doc.Database;
        GradingSettings.SyncToDocument(doc);
        var log = new System.Text.StringBuilder("\n■ DHSTRUCTPTS(구조물 좌표계 내보내기)\n");
        // ★[설치 전 검토 중간] 배치(종이) 공간에서 찍으면 종이 좌표가 그대로 적힌다(뷰포트 너머 스냅이어도) — 진입도로 명령과 같은 문
        try
        {
            if (System.Convert.ToInt32(AcadApp.GetSystemVariable("TILEMODE")) == 0 && System.Convert.ToInt32(AcadApp.GetSystemVariable("CVPORT")) == 1)
            { Done(ed, log, "⚠원인: 배치(종이) 공간에서는 도면 좌표를 못 찍음 — 모형 공간(또는 배치의 모형 뷰포트 안)에서 다시"); return; }
        }
        catch { }
        int ins = (int)db.Insunits;
        if (!StructPoints.TryToMeter(ins, out double f, out bool assumed))
        { Done(ed, log, $"⚠원인: 도면 단위({StructPoints.UnitName(ins)})를 m로 바꾸는 표가 없음 — UNITS로 m · mm · cm 중 하나로 정한 뒤 다시"); return; }
        string docName = doc.Name ?? "";
        bool named = false; try { named = doc.IsNamedDrawing; } catch { }

        // ① 이름
        string def = LastPtsName.TryGetValue(docName, out var ln) && ln.Length > 0 ? ln : Path.GetFileNameWithoutExtension(docName);
        if (string.IsNullOrWhiteSpace(def)) def = "구조물";
        var nr = ed.GetString(new PromptStringOptions("\n[구조물] 이름") { AllowSpaces = true, DefaultValue = def, UseDefaultValue = true });
        if (nr.Status != PromptStatus.OK) { ed.WriteMessage("\n[구조물] 취소"); return; }
        string name = (nr.StringResult ?? "").Trim(); if (name.Length == 0) name = def;

        // ② 점 1 → 점 2(UCS로 받은 값을 WCS로 · 1 m 미만이면 다시)
        var ucs = ed.CurrentUserCoordinateSystem;
        bool ucsWorld = ucs.IsEqualTo(Matrix3d.Identity);
        var r1 = ed.GetPoint(new PromptPointOptions("\n[구조물] 점 1 — 구조물 모서리(끝점 · 교차점 스냅): "));
        if (r1.Status != PromptStatus.OK) { ed.WriteMessage("\n[구조물] 취소"); return; }
        Point3d w1 = r1.Value.TransformBy(ucs), w2;
        while (true)
        {
            var r2 = ed.GetPoint(new PromptPointOptions("\n[구조물] 점 2 — 다른 모서리(Revit에서도 이 순서로 찍습니다): ") { UseBasePoint = true, BasePoint = r1.Value, UseDashedLine = true });
            if (r2.Status != PromptStatus.OK) { ed.WriteMessage("\n[구조물] 취소"); return; }
            w2 = r2.Value.TransformBy(ucs);
            double dm = System.Math.Sqrt((w2.X - w1.X) * (w2.X - w1.X) + (w2.Y - w1.Y) * (w2.Y - w1.Y)) * f;
            if (dm >= StructPoints.MinDist) break;
            ed.WriteMessage($"\n[구조물] 두 점 거리 {dm:F3} m — {StructPoints.MinDist:F0} m 넘게 떨어진 모서리를 고르세요");
        }

        // ③ 표고(원지반 · 최종 지표면) · 좌표계
        StructPoints.Pt p1, p2;
        string gName = "", fName = "";
        {
            var gid = ImportGisCommand.FindGroundSurface(db, out gName, out int gTris);
            if (gTris <= 0) gid = ObjectId.Null;
            using var tr = db.TransactionManager.StartTransaction();
            ObjectId fid = ObjectId.Null;
            try
            {
                foreach (ObjectId sid in Autodesk.Civil.ApplicationServices.CivilDocument.GetCivilDocument(db).GetSurfaceIds())
                    if (!sid.IsErased && tr.GetObject(sid, OpenMode.ForRead) is CivilDb.TinSurface s && s.Name == FinalSurfaceBuilder.Name) { fid = sid; fName = s.Name; break; }
            }
            catch { }
            StructPoints.Pt Mk(Point3d w)
            {
                double? g = ElevAt(tr, gid, w.X, w.Y), fz = ElevAt(tr, fid, w.X, w.Y);
                return new StructPoints.Pt(w.X * f, w.Y * f, w.Z * f, gName ?? "", g * f, fName, fz * f);
            }
            p1 = Mk(w1); p2 = Mk(w2);
            tr.Commit();
        }
        string code = KoreaCs.Read(db);
        int? det = KoreaCs.ResolveEpsgFromCode(code);
        // ★[설치 전 검토 낮음] SyncToDocument가 이미 도면 값으로 맞춘 설정 값을 쓴다 — 도면 코드로 못 나타내는 원점(구 좌표계 · UTM-K)을 고른 판은 그 고름을 지킨다
        //   (ResolveEpsgFromCode는 신/구를 못 가려 늘 신을 준다). 출처는 실제로 도면 값을 따랐을 때만 «drawing».
        int epsg = GradingSettings.ExportEpsg;
        bool fromDrawing = det.HasValue && det.Value == epsg && KoreaCs.CodeForEpsg(epsg) != null;
        var info = new StructPoints.Info(name, epsg, code, fromDrawing ? "drawing" : "settings", Path.GetFileName(docName), named ? docName : "",
            ins, ucsWorld, "Civil 3D · AutoCAD " + AcadApp.Version, "DH.Grading " + GradingSettings.Version, System.DateTimeOffset.Now);
        // ③-b ★[v116.13 · JACK 1110 저녁 · Revit 터미널 약속 §3 «sketch»] 미리보기 선 — 두 점 가운데 반지름(80 m · 거리 3배 중 큰 값) 안의 우리 평면 선
        StructPoints.Sketch? sketch = null; string sketchNote = "";
        try { sketch = BuildSketch(db, w1, w2, f, log, out sketchNote); }
        catch (System.Exception sx) { sketchNote = "미리보기 선 못 모음 — " + sx.Message; log.AppendLine("  ⚠" + sketchNote); }
        string json = StructPoints.Json(info, p1, p2, sketch);

        // ④ 저장(기본 = 도면 폴더 · 없으면 마지막 폴더)
        string defDir = named ? (Path.GetDirectoryName(docName) ?? "") : LastPtsDir;
        if (defDir.Length == 0 || !Directory.Exists(defDir)) defDir = System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments);
        string defFile = StructPoints.SafeFileName(name) + "_Civil기준점.json";
        string? path;
        short fd = 1; try { fd = System.Convert.ToInt16(AcadApp.GetSystemVariable("FILEDIA")); } catch { }
        if (fd == 0 || SelfCheckLog.Headless)
        {
            var pr = ed.GetString(new PromptStringOptions("\n[구조물] 저장할 파일") { AllowSpaces = true, DefaultValue = Path.Combine(defDir, defFile), UseDefaultValue = true });
            path = pr.Status == PromptStatus.OK ? (pr.StringResult ?? "").Trim().Trim('"') : null;
            if (path != null && path.Length == 0) path = Path.Combine(defDir, defFile);
        }
        else
        {
            var dlg = new Microsoft.Win32.SaveFileDialog { Title = "구조물 기준점 파일(Revit 애드인용)", Filter = "구조물 기준점 (*.json)|*.json", FileName = defFile, InitialDirectory = defDir, DefaultExt = ".json", AddExtension = true, OverwritePrompt = true };
            path = dlg.ShowDialog() == true ? dlg.FileName : null;
        }
        if (string.IsNullOrEmpty(path)) { ed.WriteMessage("\n[구조물] 취소"); return; }
        try
        {
            path = Path.GetFullPath(path);
            File.WriteAllText(path, json, new System.Text.UTF8Encoding(false));
            // 되읽기 — 쓴 x · y가 그대로 읽히나(비트 그대로)
            using var jd = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path, System.Text.Encoding.UTF8));
            var pa = jd.RootElement.GetProperty("points");
            bool same = pa.GetArrayLength() == 2 && pa[0].GetProperty("x").GetDouble() == p1.X && pa[0].GetProperty("y").GetDouble() == p1.Y && pa[1].GetProperty("x").GetDouble() == p2.X && pa[1].GetProperty("y").GetDouble() == p2.Y;
            if (!same) { Done(ed, log, "⚠원인: 저장한 파일을 다시 읽으니 좌표가 다름 — " + path); return; }
        }
        catch (System.Exception ex) { Done(ed, log, "⚠원인: 저장 실패 — " + ex.Message); return; }
        LastPtsName[docName] = name;
        try { LastPtsDir = Path.GetDirectoryName(path) ?? ""; } catch { }
        var (dist, brg) = StructPoints.Check(p1, p2);
        var ic = System.Globalization.CultureInfo.InvariantCulture;
        string El(StructPoints.Pt p) => (p.Ground is double g ? string.Format(ic, " 원지반 {0:F3}", g) : "") + (p.Final is double q ? string.Format(ic, " 최종 {0:F3}", q) : "");
        var notes = new List<string>();
        if (ins != 6) notes.Add(assumed ? "도면 단위 없음(INSUNITS 0) → m로 봄" : $"도면 단위 {StructPoints.UnitName(ins)} → m로 바꿔 적음");
        if (!ucsWorld) notes.Add("UCS가 WCS가 아니어서 WCS 값으로 바꿔 적음");
        if (!det.HasValue) notes.Add($"도면 좌표계 {(code.Length > 0 ? "«" + code + "» 자동인식 밖" : "없음")} — 설정값 EPSG {epsg}");
        else if (!fromDrawing) notes.Add($"좌표계 = 설정에서 고른 EPSG {epsg}(도면 코드 «{code}»로는 원점을 못 가림)");
        log.AppendLine(sketch == null ? json : StructPoints.Json(info, p1, p2));   // 진단 로그엔 미리보기 선 빼고(수만 점) — 선 수는 아래 한 줄
        if (sketchNote.Length > 0) log.AppendLine("  미리보기 선: " + sketchNote);
        Done(ed, log, string.Format(ic, "기준점 저장 — {0} · 점1 ({1:F3}, {2:F3}){3} · 점2 ({4:F3}, {5:F3}){6} · 거리 {7:F3} m · 방향 {8:F2}° · 단위 m · EPSG {9}",
            path, p1.X, p1.Y, El(p1), p2.X, p2.Y, El(p2), dist, brg, epsg)
            + (sketch != null ? $" · 미리보기 선 {sketch.Lines.Count}개" : " · ⚠미리보기 선 없음")
            + (notes.Count > 0 ? " · " + string.Join(" · ", notes) : ""));
    }

    /// <summary>★[v116.13 · 약속 §2-6 · §3 «sketch»] 미리보기 선 모으기 — 모형 공간의 우리 평면 선(옹벽선 · 소단선 · 사면선 · 정지 경계 · 구조물 평면) +
    /// 계획 부지 경계(정지 기록의 계획 폴리선) + 진입도로 포장 윤곽(3D 보기와 같은 셈 — <see cref="RoadLanes.Pavement"/>).
    /// 범위 = 두 점 가운데에서 반지름(<see cref="StructPoints.SketchRadius"/>) 상자에 걸치는 선을 통째로(Revit이 잘라 그림) · 곡선은 0.5 m 꺾은선 · XY만 · m.
    /// 꼭짓점 합계가 5만을 넘으면 덜 중요한 종류부터 뺀다(<see cref="StructPoints.Trim"/>). 도면은 안 바꾼다(읽기만).</summary>
    static StructPoints.Sketch BuildSketch(Database db, Point3d w1, Point3d w2, double f, System.Text.StringBuilder log, out string note)
    {
        double cx = (w1.X + w2.X) / 2, cy = (w1.Y + w2.Y) / 2;
        double distM = System.Math.Sqrt((w2.X - w1.X) * (w2.X - w1.X) + (w2.Y - w1.Y) * (w2.Y - w1.Y)) * f;
        double rM = StructPoints.SketchRadius(distM), rU = rM / f;           // m · 도면 단위
        double step = 0.5 / f;                                              // 곡선 꺾은선 간격 0.5 m(도면 단위로)
        string? KindOf(string layer)
        {
            if (layer == "DH-옹벽선" || layer == "DH도로-옹벽선") return "wall";
            if (layer == "DH-소단선" || layer == "DH도로-소단선") return "bench";
            if (layer == "DH-정지경계") return "site";
            if (layer == StructImport.LayPlan) return "struct";
            if (layer.StartsWith("DH-사면선", System.StringComparison.Ordinal) || layer == "DH도로-사면선") return "other";
            return null;
        }
        bool Near(Extents3d e) => e.MaxPoint.X >= cx - rU && e.MinPoint.X <= cx + rU && e.MaxPoint.Y >= cy - rU && e.MinPoint.Y <= cy + rU;
        List<(double X, double Y)> Meters(IEnumerable<(double X, double Y)> pts) => pts.Select(p => (p.X * f, p.Y * f)).ToList();
        var lines = new List<StructPoints.SketchLine>();
        var seen = new HashSet<ObjectId>();
        var cnt = new Dictionary<string, int>();
        void Add(string kind, string layer, bool closed, List<(double X, double Y)> pts)
        {
            if (pts.Count < 2) return;
            lines.Add(new StructPoints.SketchLine(kind, layer, closed, pts));
            cnt[kind] = (cnt.TryGetValue(kind, out int c) ? c : 0) + 1;
        }
        using (var tr = db.TransactionManager.StartTransaction())
        {
            // ① 모형 공간 우리 레이어 선
            var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
            foreach (ObjectId id in ms)
            {
                try
                {
                    if (tr.GetObject(id, OpenMode.ForRead) is not Curve c || c.IsErased) continue;
                    string? k = KindOf(c.Layer ?? ""); if (k == null) continue;
                    Extents3d ex; try { ex = c.GeometricExtents; } catch { continue; }
                    if (!Near(ex)) continue;
                    var pts = SampleXY(c, step); if (pts.Count < 2) continue;
                    bool closed = c.Closed || (pts.Count > 2 && pts[0] == pts[^1]);
                    Add(k, c.Layer ?? "", closed, Meters(pts)); seen.Add(id);
                }
                catch { }
            }
            // ② 계획 부지 경계 — 정지 기록 구역마다 계획 폴리선(어느 레이어든)
            try
            {
                var regs = GradingBundleStore.TryLoadAll(db, tr, out _);
                if (regs != null)
                    foreach (var rg in regs)
                    {
                        try
                        {
                            string ph = rg.PlanHandle ?? ""; if (ph.Length == 0) continue;
                            if (!db.TryGetObjectId(new Handle(System.Convert.ToInt64(ph, 16)), out var pid) || pid.IsErased || seen.Contains(pid)) continue;
                            if (tr.GetObject(pid, OpenMode.ForRead) is not Curve pc) continue;
                            if (!Near(pc.GeometricExtents)) continue;
                            var pts = SampleXY(pc, step);
                            Add("site", pc.Layer ?? "", true, Meters(pts)); seen.Add(pid);
                        }
                        catch { }
                    }
            }
            catch (System.Exception ex) { log.AppendLine("  미리보기 선 — 계획 경계 못 읽음: " + ex.Message); }
            // ③ 진입도로 포장 윤곽(설계에서 — 도면에 선으로 없다)
            try
            {
                var gf = new NetTopologySuite.Geometries.GeometryFactory();
                foreach (var aid in RoadRanges.Roads(tr, db))
                {
                    try
                    {
                        var d = RoadDesign.Read(tr, aid); if (d == null) continue;
                        string nm = ((Autodesk.Civil.DatabaseServices.Alignment)tr.GetObject(aid, OpenMode.ForRead)).Name ?? "";
                        var rl = RoadLanes.Of(tr, aid, d, nm, null); if (rl == null) continue;
                        var (pave, _, _) = RoadLanes.Pavement(tr, rl, gf);
                        if (pave == null || pave.IsEmpty) continue;
                        var env = pave.EnvelopeInternal;
                        if (env.MaxX < cx - rU || env.MinX > cx + rU || env.MaxY < cy - rU || env.MinY > cy + rU) continue;
                        for (int gi = 0; gi < pave.NumGeometries; gi++)
                            if (pave.GetGeometryN(gi) is NetTopologySuite.Geometries.Polygon pg)
                            {
                                Add("road", "진입도로 포장(" + nm + ")", true, Meters(pg.ExteriorRing.Coordinates.Select(q => (q.X, q.Y))));
                                for (int h = 0; h < pg.NumInteriorRings; h++) Add("road", "진입도로 포장(" + nm + ")", true, Meters(pg.GetInteriorRingN(h).Coordinates.Select(q => (q.X, q.Y))));
                            }
                    }
                    catch { }
                }
            }
            catch (System.Exception ex) { log.AppendLine("  미리보기 선 — 진입도로 못 읽음: " + ex.Message); }
            tr.Commit();
        }
        var (kept, dropped) = StructPoints.Trim(lines);
        int nPts = kept.Sum(l => l.Pts.Count);
        var ic = System.Globalization.CultureInfo.InvariantCulture;
        note = string.Format(ic, "반지름 {0:F1} m · 선 {1}({2}) · 꼭짓점 {3}{4}", rM, kept.Count,
            string.Join(" · ", cnt.OrderBy(kv => kv.Key).Select(kv => kv.Key + " " + kv.Value)), nPts, dropped > 0 ? $" · 한도({StructPoints.SketchMaxPts})로 뺀 선 {dropped}" : "");
        log.AppendLine("  미리보기 선: " + note);
        return new StructPoints.Sketch(cx * f, cy * f, rM, kept);
    }

    /// <summary>곡선 → XY 점열(도면 단위) — 꼭짓점 그대로 · 굽은 토막(호 · 스플라인 · 원)은 <paramref name="step"/> 간격.</summary>
    static List<(double X, double Y)> SampleXY(Curve c, double step)
    {
        var r = new List<(double X, double Y)>();
        void Push(Point3d q) { if (r.Count == 0 || System.Math.Abs(r[^1].X - q.X) > 1e-9 || System.Math.Abs(r[^1].Y - q.Y) > 1e-9) r.Add((q.X, q.Y)); }
        if (c is Line ln) { Push(ln.StartPoint); Push(ln.EndPoint); return r; }   // 선의 매개변수는 길이라 꼭짓점 셈에 안 맞음
        double p0 = c.StartParam, p1 = c.EndParam;
        bool vertexed = c is Polyline || c is Polyline2d || c is Polyline3d;
        if (vertexed)
        {
            int nSeg = System.Math.Max(1, (int)System.Math.Round(p1 - p0));
            for (int i = 0; i < nSeg; i++)
            {
                var a = c.GetPointAtParameter(p0 + i); var b = c.GetPointAtParameter(System.Math.Min(p1, p0 + i + 1)); var m = c.GetPointAtParameter(System.Math.Min(p1, p0 + i + 0.5));
                Push(a);
                double mx = (a.X + b.X) / 2, my = (a.Y + b.Y) / 2;
                if ((m.X - mx) * (m.X - mx) + (m.Y - my) * (m.Y - my) > step * step * 1e-4)   // 굽은 토막(호 bulge)
                {
                    double d0 = c.GetDistanceAtParameter(p0 + i), d1 = c.GetDistanceAtParameter(System.Math.Min(p1, p0 + i + 1));
                    int k = System.Math.Max(2, (int)System.Math.Ceiling((d1 - d0) / step));
                    for (int q = 1; q < k; q++) Push(c.GetPointAtDist(d0 + (d1 - d0) * q / k));
                }
            }
            Push(c.GetPointAtParameter(p1));
        }
        else
        {
            double L = c.GetDistanceAtParameter(p1);
            int k = System.Math.Max(2, (int)System.Math.Ceiling(L / step));
            for (int q = 0; q <= k; q++) Push(c.GetPointAtDist(L * q / k));
        }
        return r;
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
