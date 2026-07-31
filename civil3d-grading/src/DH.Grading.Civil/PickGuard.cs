using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace DH.Grading.Civil;

/// <summary>[JACK 0731] 옹벽/사면 변환 선택 중 '우리 3D 선만' 집히게 하는 방어.
/// 스샷 문제: 계획폴리곤·등고선·TIN 지표면 등 설계 중 생기는 어떤 선이든 우리 선과 겹치면
/// 클릭을 가로채거나 선택 순환 팝업이 떠서 초보자가 헷갈림.
///  ① SELECTIONCYCLING을 명령 동안만 0으로 — 팝업 자체가 안 뜸(종료 시 원복).
///  ② 우리 선(지정 레이어)을 그리기 순서 맨 위로 — 겹쳐도 우리 선이 우선.
///  ③ [근본 해결] 클릭이 다른 객체에 먹혀도, 클릭 지점 주변(픽박스 크기)을 '우리 레이어의 POLYLINE'
///    필터로 재검색해 우리 선으로 스냅(<see cref="SnapToLayerLine"/>) — 어떤 선이 겹쳐 있어도 동작.
/// 전 과정 방어적 try/catch — 실패해도 명령은 계속.</summary>
internal static class PickGuard
{
    private static object? _savedCycling;

    /// <summary>선택 순환 끄기 + topLayers 레이어의 모델공간 객체를 그리기 순서 맨 위로.</summary>
    public static void Enter(Document doc, params string[] topLayers)
    {
        _savedCycling = null;
        if (doc == null) return;
        try
        {
            try { _savedCycling = AcadApp.GetSystemVariable("SELECTIONCYCLING"); } catch { _savedCycling = null; }
            // [리뷰 0731 중간1] SELECTIONCYCLING은 레지스트리(사용자 전역) 변수 — 원래 값을 못 읽었으면
            //   아예 건드리지 않는다(0으로 바꿔놓고 원복 못 하면 다음 세션까지 꺼진 채 고착).
            if (_savedCycling != null)
                try { AcadApp.SetSystemVariable("SELECTIONCYCLING", 0); } catch { }

            if (topLayers == null || topLayers.Length == 0) return;
            var want = new System.Collections.Generic.HashSet<string>(topLayers, System.StringComparer.OrdinalIgnoreCase);
            Database db = doc.Database;
            using var tr = db.TransactionManager.StartTransaction();
            var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
            var ids = new ObjectIdCollection();
            foreach (ObjectId id in ms)
            {
                // [리뷰 0731 사소2] 손상/프록시 객체 하나가 던져도 나머지 수집은 계속.
                try
                {
                    if (tr.GetObject(id, OpenMode.ForRead) is Entity e && want.Contains(e.Layer))
                        ids.Add(id);
                }
                catch { }
            }
            if (ids.Count > 0)
            {
                var dot = (DrawOrderTable)tr.GetObject(ms.DrawOrderTableId, OpenMode.ForWrite);
                dot.MoveToTop(ids);   // 이후에도 맨 위 유지 — 우리 오버레이 선이라 부작용 없음(오히려 잘 보임)
            }
            tr.Commit();
            try { DiagLog.Append($"\n  [픽가드] 진입 OK — 순환 저장={_savedCycling != null} · 맨위 {ids.Count}개"); } catch { }
        }
        catch (System.Exception ex)
        {
            try { DiagLog.Append("\n  [픽가드] 예외: " + ex.GetType().Name + " — " + ex.Message); } catch { }
        }
    }

    /// <summary>선택 순환 원복(그리기 순서는 되돌리지 않음 — 의도적).</summary>
    public static void Exit()
    {
        if (_savedCycling != null)
        {
            try { AcadApp.SetSystemVariable("SELECTIONCYCLING", _savedCycling); } catch { }
        }
        _savedCycling = null;
    }

    /// <summary>[근본 해결 — JACK 0731] 클릭 지점(pt, UCS) 주변 픽박스 영역을 '지정 레이어의 POLYLINE(3D 폴리선)'
    /// 필터로 크로싱 재검색해 **클릭 지점에 2D 최근접인** 우리 선을 돌려준다(없으면 Null). 클릭이 계획폴리곤·
    /// 등고선·지표면 등 다른 객체에 먹혔을 때 우리 선으로 스냅하는 용도 — 겹친 선이 무엇이든 동작.
    /// [리뷰 0731] ①검색창은 화면(뷰) 축 기준 사각형 — 3D 아이소 뷰에서도 폭이 안 죽음 ②후보 중 최근접 선택
    /// (첫 결과 아님 — 엉뚱한 단 오선택 방지) ③화면 배율을 못 읽으면 스냅 포기(줌 무관 거대 창 방지).</summary>
    public static ObjectId SnapToLayerLine(Editor ed, Transaction tr, Point3d pt, params string[] layers)
    {
        try
        {
            // 화면 픽셀 → 도면 단위: VIEWSIZE(현재 뷰 높이) / SCREENSIZE.Y(뷰포트 세로 픽셀). 못 읽으면 포기.
            double viewSize; Point2d scr;
            try
            {
                viewSize = System.Convert.ToDouble(AcadApp.GetSystemVariable("VIEWSIZE"));
                scr = (Point2d)AcadApp.GetSystemVariable("SCREENSIZE");
            }
            catch { return ObjectId.Null; }
            if (!(scr.Y > 1) || !(viewSize > 0)) return ObjectId.Null;
            double upp = viewSize / scr.Y;
            double pickPx = 10;
            try { pickPx = System.Math.Max(System.Convert.ToInt32(AcadApp.GetSystemVariable("PICKBOX")) * 2, 10); }
            catch { }
            double ap = pickPx * upp;   // 검색 반폭(픽박스 2배, 최소 10px 상당)

            // 창을 '화면 축' 사각형으로 — X/Y축 고정 사각형은 SW 아이소 뷰에서 화면상 폭 0으로 붕괴(리뷰 중간1).
            var ucs = ed.CurrentUserCoordinateSystem;
            var ptW = pt.TransformBy(ucs);                       // PickedPoint(UCS) → WCS
            Vector3d n;
            try { n = ed.GetCurrentView().ViewDirection.GetNormal(); } catch { n = Vector3d.ZAxis; }
            var refUp = System.Math.Abs(n.Z) > 0.9 ? Vector3d.YAxis : Vector3d.ZAxis;
            var right = refUp.CrossProduct(n);
            right = right.Length > 1e-9 ? right.GetNormal() : Vector3d.XAxis;
            var up = n.CrossProduct(right).GetNormal();
            var inv = ucs.Inverse();
            var c1 = (ptW - right * ap - up * ap).TransformBy(inv);   // Select*Window은 UCS 점을 받음
            var c2 = (ptW + right * ap + up * ap).TransformBy(inv);

            var filter = new SelectionFilter(new[]
            {
                new TypedValue((int)DxfCode.Start, "POLYLINE"),                       // 3D 폴리선(LWPOLYLINE 아님)
                new TypedValue((int)DxfCode.LayerName, string.Join(",", layers)),     // 우리 레이어만
            });
            var res = ed.SelectCrossingWindow(c1, c2, filter);
            if (res.Status != PromptStatus.OK || res.Value == null || res.Value.Count == 0) return ObjectId.Null;

            // [리뷰 중간2] 후보 중 클릭 지점 최근접 선 — 거리는 '화면 좌표'(right/up 성분)로 판정.
            //   평면 뷰에선 월드 XY와 동일하고, 3D 뷰에선 구성평면 투영 오차(단높이×1.4 수준)를 상쇄해
            //   화면상 실제로 가까운 선이 뽑힌다(리뷰 0731 후속 — 창과 판정을 같은 뷰 축으로 통일).
            ObjectId best = ObjectId.Null; double bestD = double.MaxValue;
            foreach (SelectedObject so in res.Value)
            {
                try
                {
                    if (tr.GetObject(so.ObjectId, OpenMode.ForRead) is not Curve cv) continue;
                    var cp = cv.GetClosestPointTo(ptW, false);
                    var v = cp - ptW;
                    double dr = v.DotProduct(right), du = v.DotProduct(up);
                    double d = dr * dr + du * du;
                    if (d < bestD) { bestD = d; best = so.ObjectId; }
                }
                catch { }
            }
            return best;
        }
        catch { }
        return ObjectId.Null;
    }
}
