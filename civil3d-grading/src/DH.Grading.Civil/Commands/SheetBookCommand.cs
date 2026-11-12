using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Runtime;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace DH.Grading.Civil.Commands;

/// <summary>★★★[v112.0 · 계획_도면화.md 4.10 · JACK 1103 «[도면화] 단추 → 도킹창 → [작성하기]»]
/// <c>DHSHEETBOOK</c> = 도킹창 «도면화» 열기(리본 [도면화]) · <c>DHSHEETMAKE</c> = [작성하기](배치탭 짓기 — 명령 하나 = U 한 번).
/// <para>창 값은 <see cref="Pending"/>로 건네고(없으면 도면에 저장된 값) · 명령 안에서 다른 명령을 부르지 않는다(SendStringToExecute 금지 — 4.11).</para></summary>
public class SheetBookCommand
{
    /// <summary>도킹창 [작성하기]가 넣고 명령이 꺼내 쓰는 값(문서마다).</summary>
    internal static readonly Dictionary<Document, SheetBookInput> Pending = new();
    /// <summary>마지막 결과(문서마다) — 도킹창 결과 줄.</summary>
    internal static readonly Dictionary<Document, SheetBookBuilder.Result> Last = new();

    [CommandMethod("DHSHEETBOOK", CommandFlags.Session)]
    public void Open() => SheetBookPalette.Show();

    /// <summary>★[v112.2 · JACK 1104] 도면화 창 도곽 목록 «다른 도곽 추가…» — DWG 고르기 → 표제란 칸 찾기 → 이 PC에 저장 · 지금 도곽으로(도면설정 «4. 도곽» [바꾸기…]와 같은 길).</summary>
    [CommandMethod("DHSHEETFRAMEADD")]
    public void FrameAdd()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "도곽 DWG (*.dwg)|*.dwg", Title = "도곽 DWG 고르기" };
        if (dlg.ShowDialog() == true)
        {
            var (ok, msg) = FrameLibrary.Pick(dlg.FileName);
            if (!ok) System.Windows.MessageBox.Show("도곽으로 쓸 수 없습니다 — " + msg, "도곽", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            else if (msg.Length > 0) AcadApp.DocumentManager.MdiActiveDocument?.Editor.WriteMessage("\n[도곽] ⚠" + msg);
        }
        if (!SelfCheckLog.Headless) RefreshPalette();
    }

    /// <summary>★[v112.5] 평면 꾸미기 창 값의 등고선 간격(주곡선 · 계곡선) — 명령이 도면에 적고 지형 스타일에 걸음.</summary>
    internal static readonly Dictionary<Document, (double Minor, double Major)> PendingContour = new();

    /// <summary>★★[v112.5 · JACK 1104 «등고선 간격 조절 · 등고값 · 측점 껐다 켰다 · 기울기 — 작성하기로 수행해야만»] 평면 꾸미기 — 등고선 간격(바뀌었으면) → 전체 계획평면도 탭만 다시(다른 탭은 그대로).</summary>
    [CommandMethod("DHSHEETPLAN")]
    public void PlanOpts()
    {
        var doc = AcadApp.DocumentManager.MdiActiveDocument;
        if (doc == null) return;
        GradingSettings.SyncToDocument(doc);
        if (PendingContour.Remove(doc, out var cc))
        {
            var (mi0, ma0) = ContourInterval.Read(doc.Database);
            SheetSettingsCommand.SavedContour(doc, mi0, ma0, cc.Minor, cc.Major);
        }
        // ★★[v112.6 · JACK 1104 «누를 때마다 도면화를 재실행 — 실시간으로»] 평면 탭을 다시 만들지 않고 그 자리에서(PlanSheet.Live)
        var inp = Pending.Remove(doc, out var p) ? p : SheetBookInput.Read(doc.Database);
        var log = new System.Text.StringBuilder(); var warns = new List<string>();
        string line;
        try { line = PlanSheet.Live(doc.Database, inp, log, warns); }
        catch (System.Exception ex) { line = "⚠평면 꾸미기 실패 — " + ex.Message; log.AppendLine("  " + ex); }
        doc.Editor.WriteMessage("\n[평면 꾸미기] " + line + (warns.Count > 0 ? "\n  ⚠" + string.Join("\n  ⚠", warns.Distinct()) : ""));
        try { DiagLog.Append("\n" + log + (warns.Count > 0 ? "  ⚠" + string.Join("\n  ⚠", warns.Distinct()) + "\n" : "")); } catch { }
        try { doc.Editor.Regen(); } catch { }
    }

    [CommandMethod("DHSHEETMAKE")]
    public void Make()
    {
        var doc = AcadApp.DocumentManager.MdiActiveDocument;
        if (doc == null) return;
        RunMake(doc, null);
    }

    /// <summary>몸통 — 명령 · 화면 없는 시험이 부른다. 결과 = 명령창 한 줄 + 진단 로그 표 + 자가점검 한 줄.</summary>
    internal static SheetBookBuilder.Result RunMake(Document doc, SheetBookInput? inp, IReadOnlyCollection<DH.Grading.Core.SheetBookRules.Kind>? only = null)
    {
        GradingSettings.SyncToDocument(doc);
        if (inp == null && Pending.Remove(doc, out var p)) inp = p;
        inp ??= SheetBookInput.Read(doc.Database);
        var ed = doc.Editor;
        using var job = new SelfCheckLog.Job("도면화", doc.Name);
        SheetBookBuilder.Result r;
        string nori = "";
        try { if (only?.Contains(DH.Grading.Core.SheetBookRules.Kind.Plan) ?? inp.On[DH.Grading.Core.SheetBookRules.Kind.Plan]) nori = EnsureNori(doc); }   // ★[v112.5 검토 중간] 평면만 다시여도
        catch (System.Exception exN) { nori = "⚠노리선 못 그림 — " + exN.Message; }
        try
        {
            BusyPopup.Show("도면화 — 배치탭 만드는 중");
            r = SheetBookBuilder.Make(doc, inp, only);
        }
        catch (System.Exception ex)
        {
            r = new SheetBookBuilder.Result { Line = "⚠도면화 실패 — " + ex.Message };
            r.Log.AppendLine("  " + ex);
        }
        finally { try { BusyPopup.Close(); } catch { } }
        if (nori.Length > 0) { r.Log.Insert(0, "  " + nori + "\n"); if (nori.StartsWith("⚠")) r.Warns.Add(nori.TrimStart('⚠')); }
        string warn = r.Warns.Count > 0 ? "\n  ⚠" + string.Join("\n  ⚠", r.Warns.Distinct()) : "";
        ed.WriteMessage("\n[도면화] " + r.Line + warn);
        try { DiagLog.Append("\n" + r.Log + (r.Warns.Count > 0 ? "  ⚠" + string.Join("\n  ⚠", r.Warns.Distinct()) + "\n" : "")); } catch { }
        job.Done(r.Ok, r.Line + (r.Warns.Count > 0 ? $" · ⚠{r.Warns.Count}" : ""));
        Last[doc] = r;
        // ★[V98 실측] 화면 없는 Civil(accoreconsole)에는 도킹창 어셈블리(acmgd)가 없다 — 그 타입을 건드리는 메서드는 JIT부터 죽는다 → 화면 있을 때만 · 따로 떼어 둠
        if (!SelfCheckLog.Headless) RefreshPalette();
        return r;
    }

    /// <summary>★★[v112.2 · JACK 1104 «노리선도 보이게»] 평면도를 만들 때 노리선이 하나도 없거나 진입도로 면을 다시 만들며 지웠으면(도킹창 «노리선 지움») [노리선]과 같은 길로 먼저 그림.
    /// 이미 있으면 그대로(사람이 고친 노리선을 안 덮음). 돌려줌 = 로그 한 줄(한 일 없으면 빈 글).</summary>
    static string EnsureNori(Document doc)
    {
        var db = doc.Database;
        int have = 0;
        using (var tr = db.TransactionManager.StartTransaction())
        {
            var ms = (Autodesk.AutoCAD.DatabaseServices.BlockTableRecord)tr.GetObject(Autodesk.AutoCAD.DatabaseServices.SymbolUtilityServices.GetBlockModelSpaceId(db), Autodesk.AutoCAD.DatabaseServices.OpenMode.ForRead);
            foreach (Autodesk.AutoCAD.DatabaseServices.ObjectId id in ms)
            {
                try
                {
                    if (tr.GetObject(id, Autodesk.AutoCAD.DatabaseServices.OpenMode.ForRead) is Autodesk.AutoCAD.DatabaseServices.Entity e
                        && (string.Equals(e.Layer, "DH-노리선", System.StringComparison.OrdinalIgnoreCase) || string.Equals(e.Layer, RoadSession.LayNori, System.StringComparison.OrdinalIgnoreCase)))
                    { have++; break; }
                }
                catch { }
            }
            tr.Commit();
        }
        var rs = RoadSession.Of(doc);
        int erased = rs?.TicksErased ?? 0;
        if ((have > 0 && erased == 0) || !NoriSetting.IsOn(db)) return "";   // ★[JACK 1112] «노리선 그리기» 꺼져 있으면 평면도에도 안 그림
        string res = NoriCommand.Draw(doc, quiet: true, out int drawn);
        rs?.NoteTicksDrawn();
        return $"노리선 먼저 그림({(have == 0 ? "없었음" : $"진입도로 면을 다시 만들며 {erased}개 지움")}) — {drawn}개 · {res}";
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    static void RefreshPalette() => SheetBookPalette.Refresh();
}
