using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace DH.Grading.Civil.Commands;

/// <summary>★★★[v117.19 · JACK 1121] [구조물] → [박스 구조물 배치](DHSTRUCTBOX) — 팝업(이름 · 높이 · 바닥고/깊이 · [평면에서 그리기] · 목록 · 고치기 · 지우기).
/// 명령줄 판(화면 없는 시험 · 매크로): DHSTRUCTBOXADD(새 박스) · DHSTRUCTBOXEDIT(이름으로 고치기) · 지우기는 [구조물 지우기](DHSTRUCTOFF)도 됨.
/// 형상 · 기록은 <see cref="StructBox"/> — Revit 구조물과 같은 기록 · 같은 그리기 · 같은 수량 길.</summary>
public sealed class StructBoxCommand
{
    static readonly System.Globalization.CultureInfo IC = System.Globalization.CultureInfo.InvariantCulture;

    static void Done(Editor ed, System.Text.StringBuilder log, string msg)
    {
        ed.WriteMessage("\n[박스 구조물] " + msg);
        try { DiagLog.Append(log.ToString() + "  → " + msg + "\n"); } catch { }
    }

    /// <summary>평면에서 외곽 — 점 찍기(Enter로 닫음) 또는 «S» = 닫힌 폴리선 고르기. Esc = null.</summary>
    internal static List<(double X, double Y, double B)>? PickRing(Editor ed)
    {
        var o = new PromptPointOptions("\n[박스 구조물] 외곽 첫 점 또는 [폴리선 고르기(S)]: ") { AllowNone = false };
        o.Keywords.Add("S", "S", "폴리선 고르기(S)");
        var r0 = ed.GetPoint(o);
        if (r0.Status == PromptStatus.Keyword)
        {
            var eo = new PromptEntityOptions("\n[박스 구조물] 닫힌 폴리선 고르기: ");
            eo.SetRejectMessage("\n  폴리선만 됩니다.");
            eo.AddAllowedClass(typeof(Polyline), true);
            var er = ed.GetEntity(eo);
            if (er.Status != PromptStatus.OK) return null;
            using var tr = ed.Document.Database.TransactionManager.StartTransaction();
            var pl = (Polyline)tr.GetObject(er.ObjectId, OpenMode.ForRead);
            var ring = StructBox.RingOf(pl);
            bool closed = pl.Closed || (pl.NumberOfVertices > 2 && pl.GetPoint2dAt(0).GetDistanceTo(pl.GetPoint2dAt(pl.NumberOfVertices - 1)) < 1e-6);
            tr.Commit();
            if (!closed) { ed.WriteMessage("\n[박스 구조물] ⚠닫힌 폴리선이 아닙니다"); return null; }
            return ring;
        }
        if (r0.Status != PromptStatus.OK) return null;
        var pts = new List<(double X, double Y, double B)> { (r0.Value.X, r0.Value.Y, 0) };
        // 찍는 동안 지나온 선 — 화면에 임시로(PickMark · 끝나면 걷음)
        PickMark? mark = null;
        try
        {
            while (true)
            {
                var po = new PromptPointOptions(pts.Count < 3 ? "\n[박스 구조물] 다음 점: " : "\n[박스 구조물] 다음 점 <Enter = 닫기>: ")
                { UseBasePoint = true, BasePoint = new Point3d(pts[^1].X, pts[^1].Y, r0.Value.Z), UseDashedLine = true, AllowNone = pts.Count >= 3 };
                var r = ed.GetPoint(po);
                if (r.Status == PromptStatus.None) break;
                if (r.Status != PromptStatus.OK) return null;
                if (System.Math.Abs(r.Value.X - pts[^1].X) < 1e-6 && System.Math.Abs(r.Value.Y - pts[^1].Y) < 1e-6) continue;
                if (pts.Count >= 3 && System.Math.Abs(r.Value.X - pts[0].X) < 1e-6 && System.Math.Abs(r.Value.Y - pts[0].Y) < 1e-6) break;   // 첫 점 = 닫기
                pts.Add((r.Value.X, r.Value.Y, 0));
                try { mark?.Dispose(); } catch { }
                try { mark = PickMark.PaintPath(ed.Document, pts.Select(p => new DH.Grading.Core.Point3(p.X, p.Y, r0.Value.Z)).ToList(), false); } catch { }
            }
        }
        finally { try { mark?.Dispose(); } catch { } }
        return pts.Count >= 3 ? pts : null;
    }

    /// <summary>«EL» 또는 «D깊이»(원지반에서) — 바닥 한 칸 읽기.</summary>
    static bool ParseBottom(string s, StructBox.Spec sp)
    {
        s = s.Trim();
        if (s.StartsWith("D", System.StringComparison.OrdinalIgnoreCase))
        {
            if (!double.TryParse(s[1..], System.Globalization.NumberStyles.Float, IC, out double d)) return false;
            sp.ByDepth = true; sp.Depth = d; return true;
        }
        if (!double.TryParse(s, System.Globalization.NumberStyles.Float, IC, out double z)) return false;
        sp.ByDepth = false; sp.Bottom = z; return true;
    }

    /// <summary>«벽,상판,바닥판» 두께 한 칸 — 빈 값 = 그대로(true).</summary>
    static bool ParseThick(string s, StructBox.Spec sp)
    {
        s = (s ?? "").Trim();
        if (s.Length == 0) return true;
        var p = s.Split(',');
        if (p.Length != 3) return false;
        var v = new double[3];
        for (int i = 0; i < 3; i++) if (!double.TryParse(p[i].Trim(), System.Globalization.NumberStyles.Float, IC, out v[i])) return false;
        sp.Wall = v[0]; sp.Top = v[1]; sp.Bot = v[2];
        return true;
    }

    static string ThickText(StructBox.Spec sp) => $"{sp.Wall.ToString("0.###", IC)},{sp.Top.ToString("0.###", IC)},{sp.Bot.ToString("0.###", IC)}";

    /// <summary>명령줄 박스 배치 — 이름 · 높이 · 바닥(EL 또는 D깊이) · 두께(벽,상판,바닥판) · 외곽.</summary>
    [CommandMethod("DHSTRUCTBOXADD")]
    public void Add()
    {
        var doc = AcadApp.DocumentManager.MdiActiveDocument; if (doc == null) return;
        var ed = doc.Editor; var db = doc.Database;
        GradingSettings.SyncToDocument(doc);
        var log = new System.Text.StringBuilder("\n■ DHSTRUCTBOXADD(박스 구조물 배치)\n");
        var sp = new StructBox.Spec();
        string def;
        using (var tr = db.TransactionManager.StartTransaction()) { StructImport.TryLoad(db, tr, out _, out var recs, out _); def = StructBox.NextName(recs); tr.Commit(); }
        var nr = ed.GetString(new PromptStringOptions("\n[박스 구조물] 이름") { AllowSpaces = true, DefaultValue = def, UseDefaultValue = true });
        if (nr.Status != PromptStatus.OK) { ed.WriteMessage("\n[박스 구조물] 취소"); return; }
        sp.Name = (nr.StringResult ?? "").Trim(); if (sp.Name.Length == 0) sp.Name = def;
        var hr = ed.GetDouble(new PromptDoubleOptions("\n[박스 구조물] 높이(m)") { AllowNegative = false, AllowZero = false, DefaultValue = 3, UseDefaultValue = true });
        if (hr.Status != PromptStatus.OK) { ed.WriteMessage("\n[박스 구조물] 취소"); return; }
        sp.Height = hr.Value;
        var zr = ed.GetString(new PromptStringOptions("\n[박스 구조물] 바닥 — 바닥고 EL(예 45.2) 또는 원지반에서 깊이 D(예 D3): ") { AllowSpaces = false });
        if (zr.Status != PromptStatus.OK || !ParseBottom(zr.StringResult ?? "", sp)) { ed.WriteMessage("\n[박스 구조물] 취소(바닥 값 없음)"); return; }
        var tr2 = ed.GetString(new PromptStringOptions($"\n[박스 구조물] 두께 벽,상판,바닥판(m) <{ThickText(sp)}>: ") { AllowSpaces = false });
        if (tr2.Status != PromptStatus.OK || !ParseThick(tr2.StringResult ?? "", sp)) { ed.WriteMessage("\n[박스 구조물] 취소(두께 값을 못 읽음)"); return; }
        var ring = PickRing(ed);
        if (ring == null) { ed.WriteMessage("\n[박스 구조물] 취소"); return; }
        sp.Ring = ring;
        int id = StructBox.Place(db, sp, log, out string msg);
        string hv = "";
        try { hv = StructBox.HollowText(StructBox.Eff(sp), sp.Height) + " · "; } catch { }
        Done(ed, log, (id > 0 ? $"«{sp.Name}» 배치 — 바닥고 {sp.Bottom.ToString("F3", IC)} · 넓이 {StructBox.Area(sp.Ring).ToString("F2", IC)} ㎡ · 부피 {(StructBox.Area(sp.Ring) * sp.Height).ToString("F2", IC)} ㎥ · {hv}" : "") + msg);
    }

    /// <summary>명령줄 박스 고치기 — 이름 · 새 높이(Enter = 그대로) · 새 바닥(Enter = 그대로).</summary>
    [CommandMethod("DHSTRUCTBOXEDIT")]
    public void Edit()
    {
        var doc = AcadApp.DocumentManager.MdiActiveDocument; if (doc == null) return;
        var ed = doc.Editor; var db = doc.Database;
        var log = new System.Text.StringBuilder("\n■ DHSTRUCTBOXEDIT(박스 구조물 고치기)\n");
        var rows = StructBox.List(db);
        if (rows.Count == 0) { ed.WriteMessage("\n[박스 구조물] 없음"); return; }
        var nr = ed.GetString(new PromptStringOptions($"\n[박스 구조물] 고칠 이름({string.Join(" · ", rows.Select(x => x.S.Name))}): ") { AllowSpaces = true });
        if (nr.Status != PromptStatus.OK) return;
        var row = rows.FirstOrDefault(x => x.S.Name == (nr.StringResult ?? "").Trim());
        if (row == null) { ed.WriteMessage("\n[박스 구조물] 그 이름 없음"); return; }
        var sp = row.S.Copy();
        var hr = ed.GetString(new PromptStringOptions($"\n[박스 구조물] 높이 <{sp.Height.ToString("0.###", IC)}>: "));
        if (hr.Status != PromptStatus.OK) return;
        if ((hr.StringResult ?? "").Trim().Length > 0 && double.TryParse(hr.StringResult, System.Globalization.NumberStyles.Float, IC, out double h)) sp.Height = h;
        var zr = ed.GetString(new PromptStringOptions($"\n[박스 구조물] 바닥 <{(sp.ByDepth ? "D" + sp.Depth.ToString("0.###", IC) : sp.Bottom.ToString("0.###", IC))}>: "));
        if (zr.Status != PromptStatus.OK) return;
        if ((zr.StringResult ?? "").Trim().Length > 0 && !ParseBottom(zr.StringResult ?? "", sp)) { ed.WriteMessage("\n[박스 구조물] 바닥 값을 못 읽음"); return; }
        var tk = ed.GetString(new PromptStringOptions($"\n[박스 구조물] 두께 벽,상판,바닥판 <{ThickText(sp)}>: "));
        if (tk.Status != PromptStatus.OK) return;
        if (!ParseThick(tk.StringResult ?? "", sp)) { ed.WriteMessage("\n[박스 구조물] 두께 값을 못 읽음"); return; }
        bool ok = StructBox.Update(db, row.Id, sp, log, out string msg);
        Done(ed, log, (ok ? $"«{sp.Name}» 고침 — " : "") + msg);
    }

    /// <summary>창 위치 기억(이 세션).</summary>
    static double? WinLeft, WinTop;

    [CommandMethod("DHSTRUCTBOX")]
    public void Run()
    {
        var doc = AcadApp.DocumentManager.MdiActiveDocument; if (doc == null) return;
        var ed = doc.Editor; var db = doc.Database;
        GradingSettings.SyncToDocument(doc);
        if (SelfCheckLog.Headless) { ed.WriteMessage("\n[박스 구조물] 화면 없는 실행 — DHSTRUCTBOXADD를 쓰세요"); return; }
        var form = new StructBoxWindow.Form();
        int sel = -1; string status = "";
        PickMark? mark = null;
        void Mark(StructBox.Row? r)
        {
            try { mark?.Dispose(); } catch { }
            mark = null;
            if (r == null) return;
            try { mark = PickMark.PaintPath(doc, r.S.Ring.Select(p => new DH.Grading.Core.Point3(p.X, p.Y, r.S.Bottom)).ToList(), true); } catch { }
        }
        try
        {
            for (int guard = 0; guard < 500; guard++)
            {
                var rows = StructBox.List(db);
                var dlg = new StructBoxWindow(form, rows, sel, status);
                dlg.ZoomTo = r =>
                {
                    try { using (doc.LockDocument()) GroundOffCommand.ZoomToExt(ed, r.Ext); } catch { }
                    Mark(r);
                };
                if (WinLeft is double wl && WinTop is double wt) { dlg.WindowStartupLocation = System.Windows.WindowStartupLocation.Manual; dlg.Left = wl; dlg.Top = wt; }
                AcadApp.ShowModalWindow(dlg);
                try { WinLeft = dlg.Left; WinTop = dlg.Top; } catch { }
                form = dlg.Current;
                sel = dlg.SelectedId;
                status = "";
                var log = new System.Text.StringBuilder();
                switch (dlg.Action)
                {
                    case StructBoxWindow.Act.Draw:
                        {
                            log.Append("\n■ DHSTRUCTBOX(박스 구조물 배치)\n");
                            Mark(null);
                            var ring = PickRing(ed);
                            if (ring == null) { status = "평면에서 그리기를 취소했습니다"; continue; }
                            var sp = form.ToSpec(); sp.Ring = ring;
                            int id = StructBox.Place(db, sp, log, out string msg);
                            Done(ed, log, (id > 0 ? $"«{sp.Name}» 배치 — " : "") + msg);
                            if (id > 0) { sel = id; form.Name = ""; status = $"«{sp.Name}» 배치했습니다"; }
                            else status = msg;
                            continue;
                        }
                    case StructBoxWindow.Act.Update:
                        {
                            log.Append("\n■ DHSTRUCTBOX(박스 구조물 고치기)\n");
                            var row = rows.FirstOrDefault(x => x.Id == sel);
                            if (row == null) { status = "⚠목록에서 박스를 고르세요"; continue; }
                            var sp = form.ToSpec(); sp.Ring = row.S.Ring;
                            bool ok = StructBox.Update(db, row.Id, sp, log, out string msg);
                            Done(ed, log, (ok ? $"«{sp.Name}» 고침 — " : "") + msg);
                            status = ok ? $"«{sp.Name}» 고쳤습니다" : msg;
                            continue;
                        }
                    case StructBoxWindow.Act.Reshape:
                        {
                            log.Append("\n■ DHSTRUCTBOX(박스 구조물 외곽 다시)\n");
                            var row = rows.FirstOrDefault(x => x.Id == sel);
                            if (row == null) { status = "⚠목록에서 박스를 고르세요"; continue; }
                            Mark(null);
                            var ring = PickRing(ed);
                            if (ring == null) { status = "외곽 다시 그리기를 취소했습니다"; continue; }
                            var sp = form.ToSpec(); sp.Ring = ring;
                            bool ok = StructBox.Update(db, row.Id, sp, log, out string msg);
                            Done(ed, log, (ok ? $"«{sp.Name}» 외곽 고침 — " : "") + msg);
                            status = ok ? $"«{sp.Name}» 외곽을 고쳤습니다" : msg;
                            continue;
                        }
                    case StructBoxWindow.Act.Delete:
                        {
                            log.Append("\n■ DHSTRUCTBOX(박스 구조물 지우기)\n");
                            var row = rows.FirstOrDefault(x => x.Id == sel);
                            if (row == null) { status = "⚠목록에서 박스를 고르세요"; continue; }
                            Mark(null);
                            bool ok = StructCommand.Remove(db, row.Id, log);
                            Done(ed, log, ok ? $"«{row.S.Name}» 지움" : "⚠지우지 못했습니다");
                            status = ok ? $"«{row.S.Name}» 지웠습니다" : "⚠지우지 못했습니다";
                            sel = -1;
                            continue;
                        }
                    default:
                        return;
                }
            }
        }
        finally { Mark(null); }
    }
}
