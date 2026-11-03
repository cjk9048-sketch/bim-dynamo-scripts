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

    [CommandMethod("DHSHEETMAKE")]
    public void Make()
    {
        var doc = AcadApp.DocumentManager.MdiActiveDocument;
        if (doc == null) return;
        RunMake(doc, null);
    }

    /// <summary>몸통 — 명령 · 화면 없는 시험이 부른다. 결과 = 명령창 한 줄 + 진단 로그 표 + 자가점검 한 줄.</summary>
    internal static SheetBookBuilder.Result RunMake(Document doc, SheetBookInput? inp)
    {
        GradingSettings.SyncToDocument(doc);
        if (inp == null && Pending.Remove(doc, out var p)) inp = p;
        inp ??= SheetBookInput.Read(doc.Database);
        var ed = doc.Editor;
        using var job = new SelfCheckLog.Job("도면화", doc.Name);
        SheetBookBuilder.Result r;
        try
        {
            BusyPopup.Show("도면화 — 배치탭 만드는 중");
            r = SheetBookBuilder.Make(doc, inp);
        }
        catch (System.Exception ex)
        {
            r = new SheetBookBuilder.Result { Line = "⚠도면화 실패 — " + ex.Message };
            r.Log.AppendLine("  " + ex);
        }
        finally { try { BusyPopup.Close(); } catch { } }
        string warn = r.Warns.Count > 0 ? "\n  ⚠" + string.Join("\n  ⚠", r.Warns.Distinct()) : "";
        ed.WriteMessage("\n[도면화] " + r.Line + warn);
        try { DiagLog.Append("\n" + r.Log + (r.Warns.Count > 0 ? "  ⚠" + string.Join("\n  ⚠", r.Warns.Distinct()) + "\n" : "")); } catch { }
        job.Done(r.Ok, r.Line + (r.Warns.Count > 0 ? $" · ⚠{r.Warns.Count}" : ""));
        Last[doc] = r;
        // ★[V98 실측] 화면 없는 Civil(accoreconsole)에는 도킹창 어셈블리(acmgd)가 없다 — 그 타입을 건드리는 메서드는 JIT부터 죽는다 → 화면 있을 때만 · 따로 떼어 둠
        if (!SelfCheckLog.Headless) RefreshPalette();
        return r;
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    static void RefreshPalette() => SheetBookPalette.Refresh();
}
