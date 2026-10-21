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
        //   → 거푸집(켜기 · 끄기 · 삭제 — 삭제는 납품 전 · R44 · 1021 풀다운)
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

        // «거푸집»(JACK 2026-10-21 «거푸집 객체 지우기를 거푸집이라는 단추로 — 누르면 거푸집 켜기 · 끄기 · 삭제 · 수량산출로 만들기 전까지는 비활성»):
        // 누르면 목록이 펼쳐지는 단추(Revit 의 풀다운 — 스플릿 단추는 마지막에 쓴 항목의 이름으로 바뀌어 «거푸집» 이름을 지키려고 이것으로 했다).
        // 켜기 · 끄기 = 지금 뷰(고름) · 삭제 = 전의 [거푸집 객체 지우기](납품 전 — 명령 이름 DH_FormworkObjectsErase 그대로: 단축키가 남게).
        // 누를 수 있는 때 = 열린 모델에 판이 있을 때(고름) — 뷰가 바뀌거나 모델이 바뀌면 다시 본다(RefreshFormwork) · 항목마다 Availability 도.
        string available = typeof(FormworkAvailability).FullName!;
        var show = new PushButtonData("DH_FormworkShow", "거푸집 켜기", asm, typeof(ShowFormworkCommand).FullName)
        {
            ToolTip = "지금 뷰에서 거푸집 객체(판)를 보이게 합니다.", AvailabilityClassName = available,
            LargeImage = LoadIcon("Show32.png"), Image = LoadIcon("Show16.png"),
        };
        var hide = new PushButtonData("DH_FormworkHide", "거푸집 끄기", asm, typeof(HideFormworkCommand).FullName)
        {
            ToolTip = "지금 뷰에서 거푸집 객체(판)를 숨깁니다(다른 뷰는 그대로).", AvailabilityClassName = available,
            LargeImage = LoadIcon("Hide32.png"), Image = LoadIcon("Hide16.png"),
        };
        var erase = new PushButtonData("DH_FormworkObjectsErase", "거푸집 삭제", asm, typeof(DeleteFormworkObjectsCommand).FullName)
        {
            ToolTip = "납품 전에: [수량산출]의 [완료]가 만든 거푸집 객체(일반 모델 판)를 모두 지움. " +
                      "지침은 거푸집을 비객체로 두어 모델에서 뺀다 — 콘크리트 부재의 거푸집 넓이 속성과 엑셀은 그대로. Ctrl+Z 로 되돌림",
            AvailabilityClassName = available,
            LargeImage = LoadIcon("Erase32.png"), Image = LoadIcon("Erase16.png"),
        };
        var formwork = (PulldownButton)panel.AddItem(new PulldownButtonData("DH_Formwork", "거푸집")
        {
            ToolTip = "[3] 거푸집 객체(판) — 켜기 · 끄기(지금 뷰) · 삭제(납품 전). [수량산출]의 [완료]로 판을 만든 뒤에 누를 수 있습니다.",
            LargeImage = LoadIcon("Formwork32.png"), Image = LoadIcon("Formwork16.png"),
        });
        formwork.AddPushButton(show);
        formwork.AddPushButton(hide);
        formwork.AddSeparator();
        formwork.AddPushButton(erase);
        formwork.Enabled = false;
        s_formwork = formwork;
        app.ViewActivated += (_, e) => { s_active = e.Document; RefreshFormwork(e.Document); };
        app.ControlledApplication.DocumentChanged += (_, e) =>
        {
            if (e.GetAddedElementIds().Count + e.GetDeletedElementIds().Count > 0) RefreshFormwork(e.GetDocument());
        };
        // 닫힌 것이 보던 모델일 때만 끈다(다른 모델의 뷰가 먼저 켜졌으면 그 모델의 것 그대로 — 설치 전 검토).
        app.ControlledApplication.DocumentClosed += (_, _) =>
        {
            try
            {
                if (s_active != null && s_active.IsValidObject) return;
                s_active = null;
                if (s_formwork != null) s_formwork.Enabled = false;
            }
            catch { }
        };

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

    private static PulldownButton? s_formwork;
    private static Autodesk.Revit.DB.Document? s_active;   // 마지막으로 뷰를 연 모델(다른 모델이 바뀐 일로 단추를 켜고 끄지 않게)

    // «거푸집» 단추를 그 모델에 판이 있는지로 켜고 끈다. 판을 훑는 것은 판(DirectShape)만 · 처음 하나에서 멈춘다. 던지지 않는다.
    private static void RefreshFormwork(Autodesk.Revit.DB.Document? doc)
    {
        if (s_formwork == null || doc == null) return;
        try
        {
            if (doc.IsLinked) return;
            if (s_active != null && !ReferenceEquals(s_active, doc) && !s_active.IsValidObject) s_active = null;
            if (s_active != null && !ReferenceEquals(s_active, doc) && !doc.Equals(s_active)) return;   // DocumentChanged: 지금 보는 모델이 아니면 그대로
        }
        catch { }
        try { s_formwork.Enabled = FormworkObjects.Exists(doc); }
        catch { }
    }

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
