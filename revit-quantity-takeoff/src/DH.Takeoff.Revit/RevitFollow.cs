using System.Diagnostics;
using System.Globalization;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using DH.Takeoff.Core.View3D;

namespace DH.Takeoff.Revit;

/// <summary>
/// R61a — 「수량산출」 창(모달)에서 고른 것을 Revit 화면에서도(JACK 2026-10-26 «3D 뷰에서 클릭한 객체로 줌인되게 실제 Revit 3D 뷰에서도 보고 싶어»):
/// 창을 열기 전에 3D 뷰로 바꾸고(인터뷰 «창을 열 때») · ② · ③ 에서 고를 때마다 선택 + 확대(«선택 + 확대») · 창이 닫히면 열 때의 선택으로 되돌린다.
/// 창이 떠 있는 동안 Revit 을 직접 만질 수는 없다(인터뷰 «팝업 그대로»).
/// </summary>
internal sealed class RevitFollow
{
    private const double Feet = 0.3048;
    private const double Margin = 0.2;        // 확대 네모의 여유(한쪽 10% 씩)
    private const double MinWidth = 4.0;      // 작은 부재도 둘레가 보이게 확대 네모는 이(m)보다 좁히지 않는다(팝업의 RowZoomMinWidth 와 같이)

    private readonly UIDocument _uidoc;
    private readonly Vec3 _origin;
    private readonly Action<string> _log;
    private readonly List<ElementId> _before;
    private readonly bool _switched;
    private bool _closed, _moved, _hiddenNoted, _zoomNoted;
    private HashSet<long> _lastSet = new();     // 따라가기가 마지막으로 넣은 선택(R62 — 닫을 때 이것 그대로일 때만 되돌린다)

    /// <summary>처음 한 번 창에 띄우는 짧은 알림(숨겨진 부재 · 확대 못 함) — 부르는 쪽이 띠가 비었을 때만 띄운다(설치 전 검토 L6).</summary>
    public Action<string>? Notice { get; set; }

    /// <param name="origin">팝업 장면의 원점(m — 모델 안쪽 좌표 · ZoneScene.Data.Origin).</param>
    /// <param name="before">창을 열 때(뷰를 바꾸기 전)의 Revit 선택.</param>
    /// <param name="switched">창을 열 때 3D 뷰로 바꿨다(바꾸며 선택이 풀렸을 수 있어 닫을 때 되돌린다).</param>
    public RevitFollow(UIDocument uidoc, Vec3 origin, Action<string> log, List<ElementId> before, bool switched)
    {
        _uidoc = uidoc;
        _origin = origin;
        _log = log;
        _before = before;
        _switched = switched;
    }

    /// <summary>
    /// 창을 열기 전(트랜잭션 그룹 밖 — 모달 창 밑에서 새 뷰 창을 열지 않게): 지금 뷰가 3D(투시 아님 · 템플릿 아님)가 아니면 3D 뷰로 바꾼다.
    /// 차례 = 열려 있는 3D 뷰 → «{3D…»(작업공유는 «{3D - 사용자}») → 이름 차례 첫 3D 뷰. 하나도 없으면 바꾸지 않는다(새로 만들지 않음).
    /// </summary>
    public static (string note, bool switched) Use3DView(UIDocument uidoc)
    {
        try
        {
            var doc = uidoc.Document;
            var active = uidoc.ActiveView;
            // 투시 3D 뷰도 바꾼다(그 뷰에서는 확대가 안 된다 — 내 판단 · JACK 에게 알림).
            if (Usable(active)) return ($"그대로\t{active.Name}", false);
            View3D? pick = null;
            foreach (var ui in uidoc.GetOpenUIViews())
                if (doc.GetElement(ui.ViewId) is View3D v && Usable(v)) { pick = v; break; }
            // 열린 3D 뷰가 없으면: 내 «{3D - 사용자}» → «{3D}» → 이름 차례(작업공유에서 다른 사람의 뷰를 먼저 열지 않게 — 설치 전 검토 L4).
            string mine = "{3D - " + doc.Application.Username + "}";
            pick ??= new FilteredElementCollector(doc).OfClass(typeof(View3D)).Cast<View3D>().Where(Usable)
                        .OrderBy(v => v.Name == mine ? 0 : v.Name == "{3D}" ? 1 : v.Name.StartsWith("{3D", StringComparison.Ordinal) ? 3 : 2)
                        .ThenBy(v => v.Name, StringComparer.Ordinal).FirstOrDefault();
            if (pick == null) return ($"바꾸지 않음(3D 뷰 없음)\t{active.Name}", false);
            uidoc.ActiveView = pick;
            return ($"바꿈\t{active.Name} → {pick.Name}", true);
        }
        catch (Exception ex) { return ($"바꾸지 못함\t{ex.Message}", false); }
    }

    private static bool Usable(View? v) => v is View3D v3 && !v3.IsTemplate && !v3.IsPerspective;

    /// <summary>고른 부재를 Revit 에서 선택하고 지금 뷰를 그 자리로 확대한다. box = 팝업 장면의 상자(비었으면 부재 상자).</summary>
    public void Follow(IReadOnlyList<long> ids, Box3 box)
    {
        if (_closed || _following) return;
        _following = true;
        try { FollowNow(ids, box); }
        finally { _following = false; }
    }

    private bool _following;   // Revit 이 다시 그리는 동안 메시지가 돌아 틱이 겹쳐 들어와도 한 번만(설치 전 검토 L8)

    private void FollowNow(IReadOnlyList<long> ids, Box3 box)
    {
        var doc = _uidoc.Document;
        // 트랜잭션 한가운데(묻는 창 안에서 틱이 온 때 등)에서는 화면을 건드리지 않는다(계획 검토 H1).
        if (doc.IsModifiable) { _log("Revit 따라가기\t건너뜀(트랜잭션 중)"); return; }
        var watch = Stopwatch.StartNew();
        var list = ids.Select(i => new ElementId(i)).Where(id => doc.GetElement(id) != null).ToList();
        if (list.Count == 0) return;
        _uidoc.Selection.SetElementIds(list);
        _moved = true;
        _lastSet = list.Select(id => id.Value).ToHashSet();
        var view = _uidoc.ActiveView;
        string zoom = Zoom(view, list, box);
        try { _uidoc.RefreshActiveView(); } catch (Exception ex) { zoom += $"\t새로 그리기 오류 {ex.Message}"; }
        BoundingBoxXYZ? section = null;
        try { if (view is View3D { IsSectionBoxActive: true } v3) section = v3.GetSectionBox(); } catch { }
        int hidden = list.Count(id => Hidden(view, doc.GetElement(id), section));
        _log($"Revit 따라가기\t부재 {list.Count}/{ids.Count}\t뷰 {view.Name}\t{zoom}\t뷰에서 안 보임 {hidden}\t{watch.ElapsedMilliseconds} ms");
        if (hidden > 0 && !_hiddenNoted)
        {
            _hiddenNoted = true;
            Notice?.Invoke($"Revit 뷰에서 숨겨진 부재 {hidden}개는 선택만 됩니다(숨기기 · 단면 상자 · 범주 끔)");
        }
    }

    // 확대: 상자(장면 + 원점 → 모델 m → ft)의 꼭짓점 8개를 뷰의 오른쪽 · 위 방향에 비춰 범위 + 여유 → 상자 가운데를 지나는 면 위의 두 모서리
    // (계획 검토 M5 — 두 꼭짓점 그대로면 돌린 3D 뷰에서 상자보다 작은 네모가 된다). 확대한 뒤의 모서리를 일지에(모달 밑에서 정말 확대됐는가).
    private string Zoom(View view, List<ElementId> ids, Box3 box)
    {
        try
        {
            XYZ min, max;
            if (!box.IsEmpty)
            {
                min = Ft(box.Min + _origin);
                max = Ft(box.Max + _origin);
            }
            else
            {
                var boxes = ids.Select(id => _uidoc.Document.GetElement(id)?.get_BoundingBox(null)).Where(b => b != null).ToList();
                if (boxes.Count == 0) return "확대 X(상자 없음)";
                min = new XYZ(boxes.Min(b => b!.Min.X), boxes.Min(b => b!.Min.Y), boxes.Min(b => b!.Min.Z));
                max = new XYZ(boxes.Max(b => b!.Max.X), boxes.Max(b => b!.Max.Y), boxes.Max(b => b!.Max.Z));
            }
            if (view is View3D { IsPerspective: true }) return ZoomFailed("투시 뷰");
            if (view is not (View3D or ViewPlan or ViewSection)) return ZoomFailed("3D 뷰 없음");   // 시트 · 제도 · 범례는 모델 자리로 확대하지 않는다(설치 전 검토 L2)
            var ui = _uidoc.GetOpenUIViews().FirstOrDefault(u => u.ViewId == view.Id);
            if (ui == null) return ZoomFailed("열린 뷰 없음");
            XYZ right = view.RightDirection, up = view.UpDirection, center = (min + max) / 2;
            double r0 = double.MaxValue, r1 = double.MinValue, u0 = double.MaxValue, u1 = double.MinValue;
            foreach (double x in new[] { min.X, max.X })
                foreach (double y in new[] { min.Y, max.Y })
                    foreach (double z in new[] { min.Z, max.Z })
                    {
                        var d = new XYZ(x, y, z) - center;
                        double r = d.DotProduct(right), u = d.DotProduct(up);
                        r0 = Math.Min(r0, r); r1 = Math.Max(r1, r);
                        u0 = Math.Min(u0, u); u1 = Math.Max(u1, u);
                    }
            // 네모 = 비춘 범위 + 여유. 긴 쪽이 MinWidth 보다 짧으면 모양 그대로 키운다(점 · 선 같은 상자는 MinWidth 정사각형).
            double w = (r1 - r0) * (1 + Margin), h = (u1 - u0) * (1 + Margin), least = MinWidth / Feet;
            double longer = Math.Max(w, h);
            if (longer < least)
            {
                if (longer < 1e-6) w = h = least;
                else { w *= least / longer; h *= least / longer; }
            }
            double rc = (r0 + r1) / 2, uc = (u0 + u1) / 2;
            var a = center + right * (rc - w / 2) + up * (uc - h / 2);
            var b = center + right * (rc + w / 2) + up * (uc + h / 2);
            ui.ZoomAndCenterRectangle(a, b);
            var got = ui.GetZoomCorners();
            return $"확대 O\t요청 {P(a)}~{P(b)}\t결과 {(got.Count >= 2 ? P(got[0]) + "~" + P(got[1]) : "?")}";
        }
        catch (Exception ex) { return ZoomFailed(ex.Message); }
    }

    private string ZoomFailed(string why)
    {
        if (!_zoomNoted)
        {
            _zoomNoted = true;
            Notice?.Invoke($"Revit 뷰를 확대하지 못했습니다({why}) — 선택은 됩니다");
        }
        return $"확대 X({why})";
    }

    // 이 뷰에서 안 보이는 부재: 숨긴 부재 · 범주 끔 · 임시 숨기기/격리 · 단면 상자와 겹치지 않음(거푸집 덧그리기와 같은 잣대 — 설치 전 검토 M1).
    private static bool Hidden(View view, Element? e, BoundingBoxXYZ? section)
    {
        if (e == null) return false;
        try
        {
            if (e.IsHidden(view)) return true;
            if (e.Category != null && view.GetCategoryHidden(e.Category.Id)) return true;
            if (view.IsTemporaryHideIsolateActive() && !view.IsElementVisibleInTemporaryViewMode(TemporaryViewMode.TemporaryHideIsolate, e.Id)) return true;
            if (section != null && e.get_BoundingBox(null) != null && !FormworkOverlay.Touches(e, section)) return true;
        }
        catch { }
        return false;
    }

    /// <summary>
    /// 창이 닫혔다: 더 부르지 않고, 따라가기 · 뷰 바꾸기로 바뀐 Revit 선택은 창을 열 때의 선택으로 되돌린다
    /// (다음 실행의 ① 이 ② 줄의 부재 수백 개를 골라 둔 채 시작하지 않게 — 계획 검토 H3). 선택은 트랜잭션이 아니라 그룹과 상관없다.
    /// </summary>
    public void Closed()
    {
        _closed = true;
        if (!_moved) return;
        try
        {
            // 창을 띄운 채 Revit 에서 사람이 고른 것은 그대로 둔다(R62 — 지금 선택이 따라가기가 넣은 그대로일 때만 되돌린다).
            var now = _uidoc.Selection.GetElementIds().Select(id => id.Value).ToHashSet();
            if (!now.SetEquals(_lastSet)) { _log("Revit 따라가기\t닫힘 — Revit 에서 고친 선택이라 그대로"); return; }
            var doc = _uidoc.Document;
            var back = _before.Where(id => doc.GetElement(id) != null).ToList();
            _uidoc.Selection.SetElementIds(back);
            _log($"Revit 따라가기\t닫힘 — 선택을 열 때로 되돌림 {back.Count}");
        }
        catch (Exception ex) { _log("Revit 따라가기\t선택 되돌리기 오류\t" + ex.Message); }
    }

    private static XYZ Ft(Vec3 m) => new(m.X / Feet, m.Y / Feet, m.Z / Feet);

    private static string P(XYZ q) =>
        string.Create(CultureInfo.InvariantCulture, $"({q.X * Feet:0.##},{q.Y * Feet:0.##},{q.Z * Feet:0.##})");
}
