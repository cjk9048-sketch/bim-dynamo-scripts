using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Runtime;
using DH.Grading.Core;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;
using CivilDb = Autodesk.Civil.DatabaseServices;

namespace DH.Grading.Civil.Commands;

/// <summary>★★[v112.1 · JACK 1103 «이렇게도 저렇게도 해 보기» · 1104 인터뷰] <b>[대안](DHALT)</b> — 지금 설계를 도면 사본으로 저장 · 대안끼리 수량표로 견줌 · 열기 · 지우기.
/// <para>저장 = 도면을 저장(바뀐 것이 있을 때만 — QSAVE와 같은 <c>SaveAs(같은 이름)</c>)한 뒤 파일 통째 복사 → «도면이름_대안\이름.dwg» + 목록(<see cref="Alternatives"/>).
/// 사본 = 면 · 선 · 도로 · 도면화가 모두 그대로(다시 계산 없음). 열기 = 그 사본을 새 창으로(이미 열려 있으면 그 창으로).</para>
/// <para>도면 열기 · 창 바꾸기를 해야 해서 세션 명령(<see cref="CommandFlags.Session"/>) — 도면 일은 문서 잠금 안에서만.</para></summary>
public sealed class AltCommand
{
    [CommandMethod("DHALT", CommandFlags.Session)]
    public void Run()
    {
        var doc = AcadApp.DocumentManager.MdiActiveDocument;
        if (doc == null) return;
        var ed = doc.Editor;
        Alternatives.Row? now = null;
        for (int guard = 0; guard < 50; guard++)
        {
            string path = doc.Name ?? "";
            bool titled = false;
            try { titled = System.Convert.ToInt32(AcadApp.GetSystemVariable("DWGTITLED")) == 1 && System.IO.Path.IsPathRooted(path) && System.IO.File.Exists(path); } catch { }
            // ★[v112.1 검토 M1] 읽기 전용 · DWG 아닌 파일(DXF · DWT)은 사본 저장 안 함(SaveAs가 그 파일을 덮거나 DWG로 바꿔 씀)
            string noSave = !titled ? "도면을 먼저 파일로 저장하세요"
                          : !path.EndsWith(".dwg", System.StringComparison.OrdinalIgnoreCase) ? "DWG 도면만 대안으로 저장합니다"
                          : doc.IsReadOnly ? "읽기 전용으로 연 도면입니다" : "";
            string folder = titled ? Alternatives.FolderFor(path) : "";
            Alternatives.Book book;
            try { book = titled ? Alternatives.Load(folder) : new Alternatives.Book(); }
            catch (System.Exception ex)
            {
                // ★[검토 L1] 깨진 목록 — 옆으로 치우고 새 목록(사본 파일은 그대로)
                string bad = System.IO.Path.Combine(folder, Alternatives.ListFile), moved = bad + "." + System.DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".bad";
                try { System.IO.File.Move(bad, moved); } catch { ed.WriteMessage($"\n[대안] 목록을 못 읽었습니다 — {ex.Message}"); return; }
                ed.WriteMessage($"\n[대안] ⚠목록 파일이 깨져 옆으로 치우고 새 목록으로 엽니다 — {moved}");
                book = new Alternatives.Book();
            }
            if (now == null)
            {
                ed.WriteMessage("\n[대안] 지금 도면 수량을 재는 중...");
                now = Measure(doc, out string mlog);
                try { DiagLog.Append("\n[대안] 지금 도면 수량 — " + mlog); } catch { }
            }
            now.Name = titled ? System.IO.Path.GetFileNameWithoutExtension(path) : "(저장 안 된 도면)";
            var missing = new HashSet<string>(book.Rows.Where(r => FileOf(folder, r) is not string f || !System.IO.File.Exists(f)).Select(r => r.File), System.StringComparer.OrdinalIgnoreCase);
            string? openFile = titled && Alternatives.IsCopy(path) ? System.IO.Path.GetFileName(path) : null;

            var dlg = new AltDialog(book, now, noSave, folder, openFile, missing);
            AcadApp.ShowModalWindow(dlg);
            if (dlg.MemosChanged && titled)
                try { Alternatives.Save(folder, book); } catch (System.Exception ex) { ed.WriteMessage($"\n[대안] 메모를 못 적었습니다 — {ex.Message}"); }
            if (dlg.DialogResult != true || dlg.Action == AltDialog.Act.None) return;

            try
            {
                switch (dlg.Action)
                {
                    case AltDialog.Act.Save:
                        SaveCopy(doc, path, folder, book, now, dlg.NewName, dlg.NewMemo);
                        continue;   // 창 다시(그만둬도)
                    case AltDialog.Act.Delete:
                        Delete(doc, folder, book, dlg.Target!);
                        continue;
                    case AltDialog.Act.Open:
                        Open(doc, folder, dlg.Target!);
                        return;
                }
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n[대안] 실패 — {ex.Message}");
                try { DiagLog.Append($"\n[대안] {dlg.Action} 실패 — {ex}"); } catch { }
                System.Windows.MessageBox.Show(ex.Message, "대안", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            }
        }
    }

    /// <summary>시험용 — 연 대안을 저장해도 되나 묻기 대신(null = 창으로 물음).</summary>
    internal static System.Func<string, bool>? AskForTest;

    /// <summary>도면 저장(바뀌었을 때만) → 사본 복사 → 목록에 한 줄. 돌려줌 = 거짓이면 사용자가 그만둠.</summary>
    static bool SaveCopy(Document doc, string path, string folder, Alternatives.Book book, Alternatives.Row now, string name, string memo)
    {
        string why = Alternatives.CheckName(name, book.Rows.Select(r => r.Name));
        if (why.Length > 0) throw new System.InvalidOperationException(why);
        string file = name.Trim() + ".dwg", dst = System.IO.Path.Combine(folder, file);
        if (System.IO.File.Exists(dst)) throw new System.InvalidOperationException($"«{file}» 파일이 이미 있습니다 — 다른 이름으로");
        var db = doc.Database;
        bool dirty = false;
        try { dirty = System.Convert.ToInt32(AcadApp.GetSystemVariable("DBMOD")) != 0; } catch { dirty = true; }
        // ★[v112.1 검토 H1] 연 도면이 대안 사본이면 — 저장하면 그 대안이 바뀐다 → 묻고, 그 줄의 수량 · 시각도 지금 것으로
        Alternatives.Row? own = Alternatives.IsCopy(path)
            ? book.Rows.FirstOrDefault(r => string.Equals(r.File, System.IO.Path.GetFileName(path), System.StringComparison.OrdinalIgnoreCase)) : null;
        if (dirty && Alternatives.IsCopy(path))
        {
            string ownName = own?.Name ?? System.IO.Path.GetFileNameWithoutExtension(path);
            string q = $"열린 대안 «{ownName}»에 바뀐 것이 있습니다.\n새 대안을 만들려면 이 도면을 먼저 저장해야 해서 «{ownName}»도 지금 모양으로 바뀝니다.\n\n계속할까요?";
            bool yes = AskForTest != null ? AskForTest(q)
                     : System.Windows.MessageBox.Show(q, "대안 저장", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning) == System.Windows.MessageBoxResult.Yes;
            if (!yes) return false;
        }
        if (dirty)
        {
            doc.Editor.WriteMessage("\n[대안] 도면을 저장합니다...");
            using (doc.LockDocument()) db.SaveAs(path, true, DwgVersion.Current, db.SecurityParameters);
        }
        System.IO.Directory.CreateDirectory(folder);
        System.IO.File.Copy(path, dst, false);
        // ★[v112.1 검토 H2] 사본의 면이 무는 XML 원본을 «이 사본이 문다»로 적어 둠 — 원본 도면을 고치고 저장해도 그 원본 정리가 안 지우게(탐색기 복사와 달리)
        string claim = "";
        try { claim = XmlStore.ClaimSaved(dst, GradingBuilder.XmlFilesInDb(db, out _)); } catch (System.Exception exC) { claim = "⚠XML 원본 물림 못 적음 — " + exC.Message; }
        var row = new Alternatives.Row
        {
            Name = name.Trim(), File = file, Saved = System.DateTime.Now, Memo = memo,
            Cut = now.Cut, Fill = now.Fill, WallLen = now.WallLen, SlopeArea = now.SlopeArea, RoadLen = now.RoadLen,
        };
        if (dirty && own != null) { own.Saved = row.Saved; own.Cut = now.Cut; own.Fill = now.Fill; own.WallLen = now.WallLen; own.SlopeArea = now.SlopeArea; own.RoadLen = now.RoadLen; }
        if (book.Base.Length == 0 && !Alternatives.IsCopy(path)) book.Base = System.IO.Path.GetFileName(path);
        book.Rows.Add(row);
        try { Alternatives.Save(folder, book); }
        catch { try { System.IO.File.Delete(dst); } catch { } throw; }   // 목록에 못 적으면 사본도 거둠(주인 없는 파일 안 남김)
        doc.Editor.WriteMessage($"\n[대안] «{row.Name}» 저장 — {dst}" + (dirty ? "(도면도 저장함)" : ""));
        try { DiagLog.Append($"\n[대안] 저장 «{row.Name}» → {dst} · 도면 저장 {(dirty ? "함" : "안 함(바뀐 것 없음)")}" + (claim.Length > 0 ? " · " + claim : "")); } catch { }
        return true;
    }

    /// <summary>★[v112.1 검토 M4] 줄의 파일 — 이름만(폴더 · «..» 없음) · .dwg · 대안 폴더 안일 때만 전체 경로(아니면 null — 지우기 · 열기 안 함).</summary>
    static string? FileOf(string folder, Alternatives.Row r)
    {
        string f = r.File ?? "";
        if (f.Length == 0 || System.IO.Path.GetFileName(f) != f || !f.EndsWith(".dwg", System.StringComparison.OrdinalIgnoreCase)) return null;
        try
        {
            string root = System.IO.Path.GetFullPath(folder).TrimEnd('\\') + "\\", full = System.IO.Path.GetFullPath(System.IO.Path.Combine(folder, f));
            return full.StartsWith(root, System.StringComparison.OrdinalIgnoreCase) ? full : null;
        }
        catch { return null; }
    }

    static Document? OpenDoc(string full)
    {
        foreach (Document d in AcadApp.DocumentManager)
            try { if (string.Equals(System.IO.Path.GetFullPath(d.Name), full, System.StringComparison.OrdinalIgnoreCase)) return d; } catch { }
        return null;
    }

    static void Delete(Document doc, string folder, Alternatives.Book book, Alternatives.Row r)
    {
        string? full = FileOf(folder, r);
        if (full != null && OpenDoc(full) != null) throw new System.InvalidOperationException($"«{r.Name}»이(가) 열려 있어 못 지웁니다 — 그 도면을 닫고 다시");
        if (full != null && System.IO.File.Exists(full)) System.IO.File.Delete(full);   // 이상한 파일 이름이면 목록 줄만 지움
        book.Rows.Remove(r);
        Alternatives.Save(folder, book);
        doc.Editor.WriteMessage($"\n[대안] «{r.Name}» 지움");
        try { DiagLog.Append($"\n[대안] 지움 «{r.Name}» {full}"); } catch { }
    }

    static void Open(Document doc, string folder, Alternatives.Row r)
    {
        string full = FileOf(folder, r) ?? throw new System.InvalidOperationException($"«{r.File}» — 대안 폴더 안 DWG 파일 이름이 아닙니다");
        var d = OpenDoc(full);
        if (d == null)
        {
            if (!System.IO.File.Exists(full)) throw new System.InvalidOperationException($"«{r.File}» 파일이 없습니다");
            d = AcadApp.DocumentManager.Open(full, false);
        }
        if (d != null) AcadApp.DocumentManager.MdiActiveDocument = d;
        try { DiagLog.Append($"\n[대안] 열기 «{r.Name}» {full}"); } catch { }
    }

    /// <summary>지금 도면 수량 — 토공(도면화 토공량표 «계»와 같은 셈) · 옹벽 앞면 선(부지 + 진입도로 구간) · 사면 겉넓이 · 도로 길이. 못 잰 칸 = null.</summary>
    internal static Alternatives.Row Measure(Document doc, out string log)
    {
        var db = doc.Database;
        var sb = new System.Text.StringBuilder();
        var row = new Alternatives.Row();
        using (doc.LockDocument())
        {
            // 토공 = 도면화 토공량표 «계»와 같은 셈(최종지표면_DH · 없으면 정지면_DH − 원지반) — 칸 나누기(도로 안 · 밖) 없이 한 번만(1104 실측: 토공량표 셈 16.7초)
            string g = ImportGisCommand.GroundSurfaceName;
            ObjectId gid = ObjectId.Null, did = ObjectId.Null;
            try
            {
                (gid, did) = EarthworkVolume.Find(db, g, FinalSurfaceBuilder.Name);
                if (did.IsNull) (gid, did) = EarthworkVolume.Find(db, g, WallDaylightBuilder.PlanSurfaceBase);
                if (!gid.IsNull && !did.IsNull)
                {
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    var v = EarthworkVolume.Compute(db, did, gid);
                    if (double.IsFinite(v.Cut) && double.IsFinite(v.Fill)) { row.Cut = v.Cut; row.Fill = v.Fill; }
                    sb.Append($"토공 {v.Cut:F1}/{v.Fill:F1} ㎥ · {sw.ElapsedMilliseconds} ms");
                }
                else sb.Append("⚠토공 — 원지반 · 계획면 없음");
            }
            catch (System.Exception ex) { sb.Append(" · ⚠토공 " + ex.Message); }
            try
            {
                if (!gid.IsNull && !did.IsNull)
                {
                    using var tr = db.TransactionManager.StartTransaction();
                    var gs = new CachedGroundSurface((CivilDb.TinSurface)tr.GetObject(gid, OpenMode.ForRead));
                    var ds = new CachedGroundSurface((CivilDb.TinSurface)tr.GetObject(did, OpenMode.ForRead));
                    row.SlopeArea = Alternatives.SlopeArea(ds.AllTriangles().Select(t => (t.A, t.B, t.C)),
                                                           (x, y) => gs.TryGetElevation(x, y, out double z) ? z : null);
                    tr.Commit();
                }
            }
            catch (System.Exception ex) { sb.Append(" · ⚠사면 " + ex.Message); }

            try
            {
                using var tr = db.TransactionManager.StartTransaction();
                double wall = 0, road = 0; int nw = 0, nr = 0;
                if (!gid.IsNull)
                    foreach (var w in PureWithWallsBuilder.CurrentWalls(db, tr, gid, out _)) { wall += w.Kind == 1 ? w.LineLen : Alternatives.LineLength(w.Poly, w.IsWall); nw++; }   // ★[v113.0 · JACK 1104 «옹벽 폴리선»] 경계 폴리선 옹벽 = 폴리선 길이 × 단 수
                foreach (var id in RoadRanges.Roads(tr, db))
                {
                    try { road += ((CivilDb.Alignment)tr.GetObject(id, OpenMode.ForRead)).Length; nr++; } catch { }
                    var d = RoadDesign.Read(tr, id);
                    if (d == null) continue;
                    foreach (var x in d.Walls) if (x.Length >= 2 && double.IsFinite(x[0]) && double.IsFinite(x[1])) wall += System.Math.Abs(x[1] - x[0]);
                    foreach (var x in d.TierWalls) if (x.Length >= 2 && double.IsFinite(x[0]) && double.IsFinite(x[1])) wall += System.Math.Abs(x[1] - x[0]);
                }
                row.WallLen = wall; row.RoadLen = road;
                sb.Append($" · 부지 옹벽 {nw}개 · 도로 {nr}개");
                tr.Commit();
            }
            catch (System.Exception ex) { sb.Append(" · ⚠옹벽 · 도로 " + ex.Message); }
        }
        log = sb.ToString().Replace("\r", "").Replace("\n", " ").Trim();
        return row;
    }
}
