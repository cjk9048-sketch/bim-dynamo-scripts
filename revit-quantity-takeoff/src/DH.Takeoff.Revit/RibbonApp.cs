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

        // 버튼은 셋(JACK 2026-10-15 «위치·부위 정하기 · 산출식 자동입력 · 산출근거 엑셀은 결국 수량산출이라 한 버튼 안에»):
        //   겹침 검사 → 수량산출(한 창에서 ① 위치·부위 → ② 거푸집면 설정 → [완료] = 산출식 · 거푸집 속성 · 산출근거 엑셀 · 거푸집 객체)
        //   → 거푸집 객체 지우기(R44 — 납품 전)
        //  · 겹침 검사가 맨 앞: 형상 결합이 부재의 모양을 바꾸므로 산출보다 먼저 한다.
        // 전의 네 버튼(산출식 자동입력 · 위치·부위 정하기 · 거푸집 면 확인 · 산출근거 엑셀)은 리본에서 뺐다 — 명령의 코드는 그대로 둔다
        // (겹침 검사 · 수량산출이 그 안의 것을 쓴다). 6월 판의 버튼 셋(매개변수 세팅 · 치수 자동입력 · 산출·내보내기)도 리본에 없다.

        // R54(JACK 2026-10-20): «겹침 검사» → «모델 검증»(같은 자리 · 명령 이름 DH_Overlap 은 그대로 — 사용자가 단축키 · 빠른 실행에 넣어 둔 것이 남게).
        var overlap = new PushButtonData(
            "DH_Overlap", "모델\n검증", asm, typeof(CheckOverlapCommand).FullName)
        {
            ToolTip = "[1] 수량산출 전에 실행 — 선택 부재(없으면 전체)의 콘크리트 부재끼리 겹쳐 두 번 세는 자리와 " +
                      "겹친 자리 순위(기준표)와 반대로 결합된 쌍을 찾아 보여 주고, 원하면 기준대로 결합 · 순서를 바꿉니다.",
            LargeImage = LoadIcon("Setup32.png"),
            Image = LoadIcon("Setup16.png"),
        };
        panel.AddItem(overlap);

        var takeoff = new PushButtonData(
            "DH_Takeoff", "수량\n산출", asm, typeof(QuantityTakeoffCommand).FullName)
        {
            ToolTip = "[2] 한 창에서 ① 부위·중분류: 3D 에서 부재를 골라 부위와 중분류를 넣고 → ② 거푸집면 설정: 대응표로 면마다 거푸집 종류를 보고 고친 뒤 → " +
                      "[완료]: 산출식(DH_Formula) · 거푸집 속성을 채우고 산출근거 엑셀을 저장 · 거푸집 객체(확인용 판)를 새로 만듦",
            LargeImage = LoadIcon("Measure32.png"),
            Image = LoadIcon("Measure16.png"),
        };
        panel.AddItem(takeoff);

        // 납품 전에 거푸집 객체를 한 번에 지운다(R44 — JACK 2026-10-15): 수량산출 창을 열고 모델을 다시 재지 않고 바로.
        var erase = new PushButtonData(
            "DH_FormworkObjectsErase", "거푸집\n객체 지우기", asm, typeof(DeleteFormworkObjectsCommand).FullName)
        {
            ToolTip = "[3] 납품 전에: [수량산출]의 [완료]가 만든 거푸집 객체(일반 모델 판)를 모두 지움. " +
                      "지침은 거푸집을 비객체로 두어 모델에서 뺀다 — 콘크리트 부재의 거푸집 넓이 속성과 엑셀은 그대로. Ctrl+Z 로 되돌림",
            LargeImage = LoadIcon("Erase32.png"),
            Image = LoadIcon("Erase16.png"),
        };
        panel.AddItem(erase);

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
