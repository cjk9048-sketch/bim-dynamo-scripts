using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection; // ObjectType, PickObject

namespace DH.Takeoff.Revit;

/// <summary>
/// 모델리스 마법사가 Revit API를 안전하게 호출하기 위한 ExternalEvent 핸들러.
/// 한 번의 Raise로 (1) 현재 부재에 값 기록 → (2) 다음 부재만 단독 격리·확대 를 처리한다.
/// </summary>
public sealed class WizardHandler : IExternalEventHandler
{
    // 기록할 대상/값 (없으면 기록 생략). 동일 형상 그룹 전원에 같은 값을 기록.
    public IList<ElementId>? WriteTargets { get; set; }
    public Dictionary<string, double>? Values { get; set; } // 미터
    public string? Code { get; set; }                       // DH_ElementCode
    public string? Formula { get; set; }                    // DH_Formula (null=기록 안 함, "" 포함=기록)

    // 화면 처리
    public bool Restore { get; set; }                       // true면 격리 해제만
    public ElementId? IsolateTarget { get; set; }           // 단독으로 보여줄 부재

    // 3D 측정: 이 값이 채워지면 두 점을 찍어 거리(m)를 OnPicked로 돌려준다
    public string? PickField { get; set; }
    public Action<string, double>? OnPicked { get; set; }
    public bool Measuring { get; set; } // 현재 모서리 측정 진행 중 여부

    // 사용자가 텍스트박스 값을 직접 지웠을 때, 해당 칸의 임시 라벨만 삭제
    public string? ClearLabelField { get; set; }

    // 마법사가 닫힐 때(복원 시) 겹침 공제를 자동 실행
    public bool RunDeductionOnClose { get; set; }

    // 측정한 매개변수의 임시 라벨(파라미터명 → 모서리 중점) + 생성된 TextNote들
    private readonly Dictionary<string, XYZ> _labelPos = new();
    private readonly List<ElementId> _labelIds = new();

    private void ClearLabels(Document doc)
    {
        _labelPos.Clear();
        if (_labelIds.Count == 0) return;
        try
        {
            using var tx = new Transaction(doc, "DH 라벨 삭제");
            tx.Start();
            foreach (var id in _labelIds) { try { doc.Delete(id); } catch { } }
            tx.Commit();
        }
        catch { }
        _labelIds.Clear();
    }

    private void RefreshLabels(UIDocument uidoc)
    {
        var doc = uidoc.Document;
        var view = uidoc.ActiveView;
        try
        {
            using var tx = new Transaction(doc, "DH 라벨");
            tx.Start();
            foreach (var id in _labelIds) { try { doc.Delete(id); } catch { } }
            _labelIds.Clear();

            if (_labelPos.Count > 0)
            {
                if (view is View3D v3 && !v3.IsLocked) { try { v3.SaveOrientationAndLock(); } catch { } }
                var typeId = doc.GetDefaultElementTypeId(ElementTypeGroup.TextNoteType);
                if (typeId != null && typeId != ElementId.InvalidElementId)
                    foreach (var kv in _labelPos)
                        try { _labelIds.Add(TextNote.Create(doc, view.Id, kv.Value, kv.Key, typeId).Id); }
                        catch { }
            }
            tx.Commit();
        }
        catch { }
    }

    public void Execute(UIApplication app)
    {
        try
        {
            var uidoc = app.ActiveUIDocument;
            if (uidoc == null) return;
            var doc = uidoc.Document;
            var view = uidoc.ActiveView;

            // 0-A) 칸 값 지움 → 그 칸의 임시 라벨만 삭제
            if (!string.IsNullOrEmpty(ClearLabelField))
            {
                string f = ClearLabelField!;
                ClearLabelField = null;
                if (_labelPos.Remove(f)) RefreshLabels(uidoc);
                return;
            }

            // 0) 모서리 측정 모드 — 모서리를 하나씩 클릭(선택색으로 진하게 표시) + 합계 실시간 표시.
            //    다 고르면 Esc로 완료(합계 입력). 한 개도 안 고르고 Esc면 취소.
            //    (꺾인 벽체 등은 펼친 전체 길이를 단면에 적용하므로 분절 모서리들을 합산)
            if (!string.IsNullOrEmpty(PickField))
            {
                string field = PickField!;
                PickField = null;
                Measuring = true;
                var picked = new List<Reference>();
                var keys = new List<string>();   // 중복(더블클릭) 판별용 안정 식별자
                var lens = new List<double>();    // 각 모서리 길이(ft) — 토글 해제 시 차감
                double feet = 0;
                while (true)
                {
                    double mNow = Math.Round(feet * 0.3048, 2);
                    Reference r;
                    try
                    {
                        r = uidoc.Selection.PickObject(ObjectType.Edge,
                            $"{field}: 모서리 클릭(다시 누르면 해제) — 선택 {picked.Count}개, 합계 {mNow} m, 다 되면 Esc");
                    }
                    catch (Autodesk.Revit.Exceptions.OperationCanceledException) { break; } // Esc = 완료/취소
                    catch { break; }

                    var el = doc.GetElement(r);
                    if (el?.GetGeometryObjectFromReference(r) is not Edge edge) continue;

                    string key = r.ConvertToStableRepresentation(doc);
                    int idx = keys.IndexOf(key);
                    if (idx >= 0) // 이미 선택된 모서리 → 토글 해제(차감)
                    {
                        feet -= lens[idx];
                        picked.RemoveAt(idx); keys.RemoveAt(idx); lens.RemoveAt(idx);
                    }
                    else // 새 모서리 → 추가
                    {
                        double L = edge.AsCurve()?.Length ?? edge.ApproximateLength;
                        feet += L; picked.Add(r); keys.Add(key); lens.Add(L);
                    }

                    try { uidoc.Selection.SetReferences(picked); } catch { } // 선택색으로 강조
                    OnPicked?.Invoke(field, Math.Round(feet * 0.3048, 4));     // 클릭 즉시 칸에 합계 표시
                }
                Measuring = false;
                // 측정한 칸의 임시 라벨(첫 모서리 중점에 파라미터명) 갱신
                if (picked.Count > 0 &&
                    doc.GetElement(picked[0])?.GetGeometryObjectFromReference(picked[0]) is Edge ed0 &&
                    ed0.AsCurve() is Curve c0)
                    _labelPos[field] = c0.Evaluate(0.5, true);
                else
                    _labelPos.Remove(field);
                RefreshLabels(uidoc);
                return;
            }

            // 1) 값 기록 (트랜잭션) — 동일 형상 그룹 전원에 동일 적용
            if (WriteTargets != null && WriteTargets.Count > 0 &&
                (Values != null || !string.IsNullOrWhiteSpace(Code) || Formula != null))
            {
                using var tx = new Transaction(doc, "DH 비정형 값 입력");
                tx.Start();
                foreach (var tid in WriteTargets)
                {
                    var el = doc.GetElement(tid);
                    if (el == null) continue;
                    if (Values != null)
                        foreach (var kv in Values) DimensionExtractor.WriteMeters(el, kv.Key, kv.Value);
                    if (!string.IsNullOrWhiteSpace(Code))
                        DimensionExtractor.WriteString(el, "DH_ElementCode", Code);
                    if (Formula != null)
                        DimensionExtractor.WriteString(el, "DH_Formula", Formula);
                }
                tx.Commit();
                WriteTargets = null; Values = null; Code = null; Formula = null;
            }

            // 2) 닫기/복원: 라벨 삭제 + 임시 격리 해제 + 뷰 잠금 해제 (★ 트랜잭션 필요)
            if (Restore || IsolateTarget == null)
            {
                Restore = false;
                ClearLabels(doc); // 임시 라벨 모두 삭제
                var v3all = new FilteredElementCollector(doc).OfClass(typeof(View3D)).Cast<View3D>()
                    .Where(v => !v.IsTemplate && (v.IsTemporaryHideIsolateActive() || v.IsLocked)).ToList();
                if (v3all.Count > 0)
                {
                    using var txr = new Transaction(doc, "DH 격리·잠금 해제");
                    txr.Start();
                    foreach (var v in v3all)
                    {
                        if (v.IsTemporaryHideIsolateActive())
                            v.DisableTemporaryViewMode(TemporaryViewMode.TemporaryHideIsolate);
                        if (v.IsLocked) { try { v.Unlock(); } catch { } }
                    }
                    txr.Commit();
                }

                // 마법사 종료 → 겹침 공제 자동 실행
                if (RunDeductionOnClose)
                {
                    RunDeductionOnClose = false;
                    try { TaskDialog.Show("DH 수량산출 — 겹침 공제(자동)", OverlapResolver.Resolve(doc)); }
                    catch (Exception ex) { TaskDialog.Show("DH 수량산출 — 오류", "겹침 공제 중 오류:\n" + ex.Message); }
                }
                return;
            }

            // 3) 3D 뷰 확보 (+ 이전 객체의 임시 라벨 삭제)
            View3D? v3d = Get3DView(doc, view);
            if (v3d == null) return;
            ClearLabels(doc);

            var ids = new List<ElementId> { IsolateTarget };
            string? isoError = null;
            try
            {
                // ★ 임시 격리도 Revit 2026에선 트랜잭션 안에서 호출해야 한다.
                //   활성화보다 '먼저' 격리를 적용해야 전환 후에도 격리가 유지된다.
                using var txi = new Transaction(doc, "DH 부재 격리");
                txi.Start();
                if (v3d.IsTemporaryHideIsolateActive())
                    v3d.DisableTemporaryViewMode(TemporaryViewMode.TemporaryHideIsolate);
                v3d.IsolateElementsTemporary(ids);
                txi.Commit();
            }
            catch (Exception ex) { isoError = $"{ex.GetType().Name}: {ex.Message}"; }

            // 뷰 활성화 + 선택 + 새로고침
            if (uidoc.ActiveView.Id != v3d.Id) uidoc.ActiveView = v3d;
            uidoc.Selection.SetElementIds(ids);
            uidoc.RefreshActiveView();

            // 4) 대상으로 확대
            var tgt = doc.GetElement(IsolateTarget);
            var bb = tgt?.get_BoundingBox(v3d) ?? tgt?.get_BoundingBox(null);
            if (bb != null)
            {
                try // 투시(perspective) 뷰에선 ZoomAndCenterRectangle 불가 → 격리는 유지하고 줌만 생략
                {
                    foreach (UIView uv in uidoc.GetOpenUIViews())
                        if (uv.ViewId == v3d.Id) { uv.ZoomAndCenterRectangle(bb.Min, bb.Max); break; }
                }
                catch { }
            }

            // 5) 격리가 실제로 적용됐는지 점검 — 안 됐으면 '한 번만' 원인 안내
            if (!_diagShown && (isoError != null || !v3d.IsTemporaryHideIsolateActive()))
            {
                _diagShown = true;
                var tcat = tgt?.Category?.Name ?? "(없음)";
                bool linked = tgt is RevitLinkInstance;
                TaskDialog.Show("DH 격리 디버그(최초 1회)",
                    $"임시 격리가 화면에 적용되지 않았습니다.\n\n" +
                    $"• 뷰: {v3d.Name}\n• 부재: {tcat} (Id {IsolateTarget.Value})\n" +
                    $"• 격리 활성: {v3d.IsTemporaryHideIsolateActive()}\n" +
                    $"• 링크부재 여부: {linked}\n" +
                    $"• 오류: {isoError ?? "없음"}\n\n" +
                    "이 내용을 그대로 캡처해 전달해 주세요.");
            }
        }
        catch { /* 뷰 종류 등으로 실패 시 무시 */ }
    }

    private static bool _diagShown;

    /// <summary>활성 뷰가 3D면 그대로, 아니면 기존 3D 뷰를 찾고, 없으면 새로 만든다.</summary>
    private static View3D? Get3DView(Document doc, View active)
    {
        if (active is View3D av && !av.IsTemplate) return av;

        var existing = new FilteredElementCollector(doc).OfClass(typeof(View3D))
            .Cast<View3D>().FirstOrDefault(v => !v.IsTemplate);
        if (existing != null) return existing;

        var vft = new FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType))
            .Cast<ViewFamilyType>().FirstOrDefault(t => t.ViewFamily == ViewFamily.ThreeDimensional);
        if (vft == null) return null;

        using var tx = new Transaction(doc, "DH 3D 뷰 생성");
        tx.Start();
        var v = View3D.CreateIsometric(doc, vft.Id);
        tx.Commit();
        return v;
    }

    public string GetName() => "DH Review Wizard Handler";
}
