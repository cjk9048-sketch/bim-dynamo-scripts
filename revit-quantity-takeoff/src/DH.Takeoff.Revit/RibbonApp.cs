using System.Reflection;
using System.Windows.Media.Imaging;
using Autodesk.Revit.UI;
using DH.Takeoff.Revit.Commands;

namespace DH.Takeoff.Revit;

/// <summary>
/// 애드인 진입점 — 리본 탭 "DH 수량산출" + 아이콘 버튼 구성.
/// </summary>
public sealed class RibbonApp : IExternalApplication
{
    private const string TabName = "DH 수량산출";

    public Result OnStartup(UIControlledApplication app)
    {
        try { app.CreateRibbonTab(TabName); } catch { /* 이미 존재하면 무시 */ }

        var panel = app.CreateRibbonPanel(TabName, "수량산출");
        var asm = Assembly.GetExecutingAssembly().Location;

        // 버튼은 산출하는 차례대로 왼쪽부터 놓는다(JACK 2026-10-13):
        //   겹침 검사 → 산출식 자동입력 → 위치·부위 정하기 → 거푸집 면 확인 → 산출근거 엑셀
        //  · 겹침 검사가 맨 앞: 형상 결합이 부재의 모양을 바꾸므로 산출식보다 먼저 한다.
        //  · 위치·부위는 거푸집 앞: 거푸집의 종류를 '부위 × 면'으로 정하게 될 것이라(다음 단계) 부위가 먼저 있어야 한다.
        //  · 산출근거 엑셀이 맨 끝: 앞의 것들을 모아 내보낸다.
        // 6월 판의 버튼 셋(매개변수 세팅 · 치수 자동입력 · 산출·내보내기)은 리본에서 뺐다: 새 버튼이 필요한 속성 칸을 스스로 만들고,
        // 「치수 자동입력」의 옛 창에서 저장하면 [L1]*… 꼴의 식이 DH_Formula 를 덮어쓸 수 있었다. 명령의 코드는 그대로 둔다.

        var overlap = new PushButtonData(
            "DH_Overlap", "겹침\n검사", asm, typeof(CheckOverlapCommand).FullName)
        {
            ToolTip = "[1] 선택 부재(없으면 전체)의 콘크리트 부재끼리 형상 결합 없이 겹쳐 두 번 세는 자리를 찾아 보여 주고, " +
                      "원하면 우선순위대로(슬래브 우선) 형상 결합. 산출식 자동입력 전에 실행",
            LargeImage = LoadIcon("Setup32.png"),
            Image = LoadIcon("Setup16.png"),
        };
        panel.AddItem(overlap);

        var formula = new PushButtonData(
            "DH_Formula", "산출식\n자동입력", asm, typeof(FillFormulaCommand).FullName)
        {
            ToolTip = "[2] 선택 부재(없으면 전체) 가운데 콘크리트 부재의 산출식을 형상에서 만들어 DH_Formula에 기록. " +
                      "모델 체적과 ±1% 안에서 맞는 것만 적고, 못 만든 부재는 하나씩 띄워 직접 입력",
            LargeImage = LoadIcon("Measure32.png"),
            Image = LoadIcon("Measure16.png"),
        };
        panel.AddItem(formula);

        var zone = new PushButtonData(
            "DH_Zone", "위치·부위\n정하기", asm, typeof(AssignZoneCommand).FullName)
        {
            ToolTip = "[3] 팝업의 3D 그림에서 부재를 찍어(또는 끌어) 고르고, 왼쪽에서 위치(배수지 · 배관실 …)와 부위(바닥슬래브 · 벽체 · 턱 …)를 누르면 " +
                      "바로 부재의 DH_Zone · DH_Part 에 적음. 값이 들어간 부재만 색이 바뀌고, 색이 안 바뀐(회색) 부재가 없을 때까지 함. " +
                      "산출서에는 '배관실 바닥슬래브'처럼 위치 + 부위로 적힘",
            LargeImage = LoadIcon("Setup32.png"),
            Image = LoadIcon("Setup16.png"),
        };
        panel.AddItem(zone);

        var formwork = new PushButtonData(
            "DH_Formwork", "거푸집\n면 확인", asm, typeof(FormworkCheckCommand).FullName)
        {
            ToolTip = "[4] 선택 부재(없으면 전체)의 콘크리트 면을 갈라 거푸집 넓이를 종류별로 내고 3D 뷰에 색으로 보여 줌(1단계 — 아직 엑셀에는 싣지 않음). " +
                      "윗면과 다른 콘크리트가 맞닿은 자리는 빼고, 옆면은 안팎과 높이 구간으로, 밑면은 아래에 공간이 있을 때만. " +
                      "종류는 '유형 × 면' 대응표로 고침. 모델은 바꾸지 않음",
            LargeImage = LoadIcon("Measure32.png"),
            Image = LoadIcon("Measure16.png"),
        };
        panel.AddItem(formwork);

        var sheet = new PushButtonData(
            "DH_CalcSheet", "산출근거\n엑셀", asm, typeof(ExportCalcSheetCommand).FullName)
        {
            ToolTip = "[5] 콘크리트 부재의 산출식(DH_Formula)과 위치(DH_Zone)·부위(DH_Part)를 모아 수량산출서의 산출근거를 엑셀로 내보냄. " +
                      "숫자·기호를 칸마다 적고 값은 =ROUND(…,2). 지금 모델 체적과 맞는 식만 싣고 나머지는 「미산출 부재」 시트에 적음",
            LargeImage = LoadIcon("Export32.png"),
            Image = LoadIcon("Export16.png"),
        };
        panel.AddItem(sheet);

        // 산출 과정에 드는 버튼이 아니라 따로 둔다: 모델의 형상을 파일로 받아, 문제가 생긴 자리를 Revit 없이 다시 재 보는 데 쓴다.
        var tools = app.CreateRibbonPanel(TabName, "도구");
        var dump = new PushButtonData(
            "DH_Dump", "형상\n내보내기", asm, typeof(DumpGeometryCommand).FullName)
        {
            ToolTip = "선택 부재(없으면 전체)의 실제 입체·결합·개구부를 JSON으로 내보냄(모델은 바꾸지 않음). " +
                      "산출 과정에는 쓰지 않음 — 새 모델에서 결과가 이상할 때 형상을 받아 원인을 찾는 데 씀",
            LargeImage = LoadIcon("Export32.png"),
            Image = LoadIcon("Export16.png"),
        };
        tools.AddItem(dump);

        return Result.Succeeded;
    }

    public Result OnShutdown(UIControlledApplication app) => Result.Succeeded;

    /// <summary>DLL에 포함된 PNG 아이콘을 BitmapImage로 로드.</summary>
    private static BitmapImage? LoadIcon(string fileName)
    {
        var asm = Assembly.GetExecutingAssembly();
        var resName = $"DH.Takeoff.Revit.Resources.{fileName}";
        using var stream = asm.GetManifestResourceStream(resName);
        if (stream == null) return null;

        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.StreamSource = stream;
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.EndInit();
        bmp.Freeze();
        return bmp;
    }
}
