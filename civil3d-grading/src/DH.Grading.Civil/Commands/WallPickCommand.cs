using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Runtime;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace DH.Grading.Civil.Commands;

/// <summary>
/// [옹벽 변환 — DHWALL] 계단선을 하나 클릭하고 Enter를 치면 **소단 길이**를 물어,
/// 그 단부터 바깥을 수직 옹벽(구배 하한 = <see cref="GradingSettings.MinSlope"/>)으로 바꾼다(JACK 0804).
/// 단높이는 정지옵션에서 정한다. ★[JACK 0825] 하한이 0.05→0.01로 내려가 문구에서 숫자를 뺐다.
///  · 1회 실행 = 1개만 바뀐다. 선을 연달아 눌러도 마지막에 누른 것만 선택된다.
///  · 여러 번 실행하면 규칙이 쌓여 아래/위 제원을 다르게 줄 수 있다.
///  · [전체해제] = 이 구역의 구간 설정을 전부 지우고 순수 사면으로 재생성.
/// 실제 동작은 사면 변환(DHSLOPE)과 완전히 같고, 묻는 항목만 다르다 — <see cref="ZoneEditCommon"/>.
/// </summary>
public sealed class WallPickCommand
{
    [CommandMethod("DHWALL")]
    public void Run()
    {
        Document doc = AcadApp.DocumentManager.MdiActiveDocument;
        if (doc == null) return;
        GradingSettings.SyncToDocument(doc);   // [도면 전환 0803] 도면이 바뀌었으면 그 도면 기준으로 설정·기억 재정렬
        ZoneEditCommon.Run(doc, wallMode: true);
    }

    /// <summary>★★[v113.0 · JACK 1104 «옹벽 변환은 소단선을 변형 · 폴리선으로 옹벽을 추가하는 건 별도 단추»] [옹벽 추가] — 경계 위에서 시작해 경계 안을 지나 다시 경계 위에서 끝나는 폴리선을 그리면(또는 S로 고르면)
    /// 2점 판정 → 단높이 · 소단 → 옹벽 짓기(<see cref="WallLineFlow.Run"/> — 옛 [옹벽 변환]의 L 길 그대로).</summary>
    [CommandMethod("DHWALLADD")]
    public void Add()
    {
        Document doc = AcadApp.DocumentManager.MdiActiveDocument;
        if (doc == null) return;
        GradingSettings.SyncToDocument(doc);
        try { WallLineFlow.Run(doc); }
        catch (System.Exception ex)
        {
            doc.Editor.WriteMessage("\n[옹벽 추가] 실패 — " + ex.Message);
            try { DiagLog.Append($"\n■ [옹벽 추가] 예외 — {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}\n"); } catch { }
        }
    }

    /// <summary>★[v117.3 · JACK D1 ③] [용지 경계] — 닫힌 폴리선(여럿)을 골라 도면에 둔다 · 지우기. [옹벽 추가]가 기초 외곽을 이 경계로 잰다(없으면 그은 선 · 경고만).</summary>
    [CommandMethod("DHLANDBOUND")]
    public void LandBoundary()
    {
        Document doc = AcadApp.DocumentManager.MdiActiveDocument;
        if (doc == null) return;
        try { LandBound.Run(doc); }
        catch (System.Exception ex) { doc.Editor.WriteMessage("\n[용지 경계] 실패 — " + ex.Message); }
    }
}
