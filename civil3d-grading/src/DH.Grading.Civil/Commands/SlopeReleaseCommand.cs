using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Runtime;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace DH.Grading.Civil.Commands;

/// <summary>
/// [사면 변환 — DHSLOPE] 계단선을 하나 클릭하고 Enter를 치면 **사면 경사 → 소단 길이**를 물어,
/// 그 단부터 바깥을 그 제원의 사면으로 바꾼다(JACK 0804). 단높이는 정지옵션에서 정한다.
///  · 옹벽선·사면선 둘 다 클릭 대상 — 옹벽을 사면으로 되돌리는 것도, 이미 사면인 곳의 구배만 바꾸는 것도 된다.
///  · 1회 실행 = 1개만 바뀐다. 선을 연달아 눌러도 마지막에 누른 것만 선택된다.
///  · 여러 번 실행하면 규칙이 쌓여 '아래는 급하게 · 위는 완만하게'가 된다.
///  · 정지옵션 구배를 0으로 줘서 처음부터 전체가 옹벽인 도면에서도 그대로 동작한다.
/// 실제 동작은 옹벽 변환(DHWALL)과 완전히 같고, 묻는 항목만 다르다 — <see cref="ZoneEditCommon"/>.
/// </summary>
public sealed class SlopeReleaseCommand
{
    [CommandMethod("DHSLOPE")]
    public void Run()
    {
        Document doc = AcadApp.DocumentManager.MdiActiveDocument;
        if (doc == null) return;
        GradingSettings.SyncToDocument(doc);   // [도면 전환 0803] 도면이 바뀌었으면 그 도면 기준으로 설정·기억 재정렬
        ZoneEditCommon.Run(doc, wallMode: false);
    }
}
