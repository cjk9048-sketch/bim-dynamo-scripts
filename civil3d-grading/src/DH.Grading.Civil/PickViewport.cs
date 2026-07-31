using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace DH.Grading.Civil;

/// <summary>[JACK 0731] 옹벽/사면 변환 중 화면을 좌우 2분할(왼쪽=평면·오른쪽=3D)로 만들어 옹벽선·소단선을
/// 평면과 3D를 함께 보며 직관적으로 고르게 한다. 변환이 끝나면 원래 단일 평면 뷰로 복원.
/// 전 과정 방어적(try/catch) — 뷰포트 제어가 실패해도 변환 명령 자체는 정상 동작해야 한다.
///
/// 동작: TILEMODE=1(모델공간 타일 뷰포트) → 단일로 정리 → 세로 2분할 → 화면 위치(LowerLeftCorner.X)로
///   왼쪽/오른쪽 뷰포트 번호(CVPORT)를 판별 → 오른쪽=남서 아이소+음영, 왼쪽=평면(TOP)+2D → 왼쪽을 활성.
/// 복원: 단일로 정리 → 저장해 둔 원래 뷰(줌·팬 포함) 복구 + 2D 와이어프레임 → 원래 TILEMODE 복원.
///
/// [리뷰 0731] '분할 성공' 플래그(_splitDone)는 분할 직후에 세팅한다 — 이후 스타일링이 실패해도
///   Restore가 반드시 단일로 걷어내 화면이 2분할로 고착되지 않게(중간1). 원래 TILEMODE도 저장·복원(중간2).
/// </summary>
internal static class PickViewport
{
    private static ViewTableRecord? _savedView;
    private static object? _savedTilemode;
    private static bool _splitDone;   // 화면이 실제로 분할됨 → 복원(단일화) 필요

    /// <summary>좌(평면)·우(3D) 2분할 진입. 실패해도 조용히 넘어간다(변환 명령은 계속).</summary>
    public static void Enter(Document doc)
    {
        _splitDone = false; _savedView = null; _savedTilemode = null;
        if (doc == null) return;
        Editor ed = doc.Editor;
        Database db = doc.Database;
        try
        {
            try { _savedView = ed.GetCurrentView(); } catch { _savedView = null; }
            try { _savedTilemode = AcadApp.GetSystemVariable("TILEMODE"); } catch { _savedTilemode = null; }
            AcadApp.SetSystemVariable("TILEMODE", 1);
            ed.Command("_.-VPORTS", "_SI");              // 알려진 단일 상태에서 시작
            ed.Command("_.-VPORTS", "_2", "_V");         // 세로 2분할(왼|오)
            _splitDone = true;                           // ★분할 성공 즉시 — 스타일링 실패해도 Restore가 걷어냄

            // 화면 위치로 왼쪽/오른쪽 CVPORT 번호 판별(번호는 환경마다 다를 수 있음).
            int leftNum = 0, rightNum = 0; double leftX = double.MaxValue, rightX = double.MinValue;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var vt = (ViewportTable)tr.GetObject(db.ViewportTableId, OpenMode.ForRead);
                foreach (ObjectId id in vt)
                {
                    if (tr.GetObject(id, OpenMode.ForRead) is not ViewportTableRecord r) continue;
                    if (r.Name != "*Active" || r.Number < 2) continue;   // 활성 타일 뷰포트만
                    double x = r.LowerLeftCorner.X;
                    if (x < leftX) { leftX = x; leftNum = r.Number; }
                    if (x > rightX) { rightX = x; rightNum = r.Number; }
                }
                tr.Commit();
            }

            if (rightNum >= 2 && leftNum >= 2 && rightNum != leftNum)
            {
                // 오른쪽 = 3D(남서 아이소 + 음영/개념)
                AcadApp.SetSystemVariable("CVPORT", rightNum);
                ed.Command("_.-VIEW", "_SWISO");
                ed.Command("_.VSCURRENT", "_Conceptual");
                ed.Command("_.ZOOM", "_E");
                // 왼쪽 = 평면(TOP + 2D 와이어프레임) — 활성으로 두어 여기서 시작.
                AcadApp.SetSystemVariable("CVPORT", leftNum);
                ed.Command("_.-VIEW", "_TOP");
                ed.Command("_.VSCURRENT", "_2dwireframe");
                ed.Command("_.ZOOM", "_E");
            }
            else
            {
                // 좌/우 판별 실패 — 최소한 현재 뷰포트만 3D로(그래도 평면과 병행 검토 가능).
                ed.Command("_.-VIEW", "_SWISO");
                ed.Command("_.VSCURRENT", "_Conceptual");
                ed.Command("_.ZOOM", "_E");
            }
        }
        catch
        {
            // _splitDone은 그대로 둔다 — 이미 분할됐다면 Restore가 반드시 단일로 되돌려야 하므로.
        }
    }

    /// <summary>단일 평면 뷰로 복원. 분할했으면 단일화하고, Enter가 TILEMODE를 바꿨으면(분할 성공 여부와 무관) 원복.</summary>
    public static void Restore(Document doc)
    {
        // 상태를 먼저 지역으로 옮기고 리셋 — 분할 실패로 _splitDone=false여도 TILEMODE 복원은 시도(리뷰 사소1).
        bool split = _splitDone;
        var savedView = _savedView;
        var savedTile = _savedTilemode;
        _splitDone = false; _savedView = null; _savedTilemode = null;
        if (doc == null) return;
        Editor ed = doc.Editor;
        try
        {
            if (split)
            {
                AcadApp.SetSystemVariable("TILEMODE", 1);
                ed.Command("_.-VPORTS", "_SI");              // 단일로 합침
                ed.Command("_.VSCURRENT", "_2dwireframe");   // 평면은 2D 와이어프레임
                if (savedView != null)
                {
                    try { ed.SetCurrentView(savedView); }    // 원래 줌·팬 복구
                    catch { ed.Command("_.-VIEW", "_TOP"); ed.Command("_.ZOOM", "_E"); }
                }
                else { ed.Command("_.-VIEW", "_TOP"); ed.Command("_.ZOOM", "_E"); }
            }
            // 원래 TILEMODE 복원 — 배치(도면공간)에서 시작했다면 배치로 되돌아간다(분할 실패 시에도).
            if (savedTile != null) { try { AcadApp.SetSystemVariable("TILEMODE", savedTile); } catch { } }
        }
        catch { }
    }
}
