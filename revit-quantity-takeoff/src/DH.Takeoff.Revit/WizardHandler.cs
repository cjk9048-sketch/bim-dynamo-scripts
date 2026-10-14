using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection; // ObjectType, PickObject

namespace DH.Takeoff.Revit;

/// <summary>
/// 모델리스 마법사가 Revit API를 안전하게 호출하기 위한 ExternalEvent 핸들러.
/// Raise 가 여러 번 겹쳐도 한 번의 Execute 가 순서대로 다 처리한다:
/// (1) 쌓인 값 기록 → (2) 보여줄 부재 단독 격리·확대 → (3) 라벨 정리 → (4) 모서리 측정 → (5) 닫기 정리.
/// </summary>
public sealed class WizardHandler : IExternalEventHandler
{
    /// <summary>
    /// 마법사를 띄운 문서. 값 기록과 정리는 항상 이 문서에 한다(다른 문서가 화면에 떠 있어도 같은 Id의 다른 부재에 적지 않는다).
    /// 화면을 다루는 일(측정·격리·확대)은 이 문서가 활성일 때만 한다.
    /// </summary>
    public Document? TargetDoc { get; set; }

    // 기록할 것들(대상 + 값). [저장]을 누를 때마다 하나씩 쌓이고 다음 Execute 에서 한꺼번에 적는다.
    // 한 칸에 덮어쓰지 않고 쌓는다 — Revit이 앞의 것을 처리하기 전에 [저장·다음]을 또 누르면 앞의 값이 사라졌다.
    private sealed record WriteJob(List<ElementId> Targets, Dictionary<string, double>? Values, string? Code, string? Formula);
    private readonly Queue<WriteJob> _writes = new();

    /// <summary>
    /// 기록할 것을 쌓는다. 동일 형상 그룹 전원에 같은 값을 적는다.
    /// values=null 이면 치수 칸은 건드리지 않고, formula=null 이면 식 칸은 건드리지 않는다("" 는 식 칸을 비운다).
    /// </summary>
    public void Stage(IList<ElementId> targets, Dictionary<string, double>? values, string? code, string? formula)
    {
        if (targets == null || targets.Count == 0) return;
        if (values == null && string.IsNullOrWhiteSpace(code) && formula == null) return;
        _writes.Enqueue(new WriteJob(targets.ToList(),
            values == null ? null : new Dictionary<string, double>(values), code, formula));
    }

    // 화면 처리
    public bool Restore { get; set; }                       // true면 닫기 정리(격리·잠금 해제, 라벨 삭제)
    public ElementId? IsolateTarget { get; set; }           // 단독으로 보여줄 부재

    // 3D 측정: 이 값이 채워지면 모서리를 찍어 길이 합(m)을 OnPicked로 돌려준다
    public string? PickField { get; set; }
    public Action<string, double>? OnPicked { get; set; }
    public bool Measuring { get; set; } // 현재 모서리 측정 진행 중 여부

    // 사용자가 텍스트박스 값을 직접 지웠을 때, 해당 칸의 임시 라벨만 삭제
    public string? ClearLabelField { get; set; }

    // 마법사가 닫힐 때(복원 시) 겹침 공제를 자동 실행
    public bool RunDeductionOnClose { get; set; }

    /// <summary>문서가 닫혀 더 할 수 있는 일이 없다 — 마법사를 닫으라는 신호.</summary>
    public Action? OnDocumentGone { get; set; }

    /// <summary>닫기 정리가 끝났다 — 마법사와 이벤트를 놓아도 된다는 신호.</summary>
    public Action? OnRestored { get; set; }

    // 측정한 매개변수의 임시 라벨(파라미터명 → 모서리 중점) + 생성된 TextNote들
    private readonly Dictionary<string, XYZ> _labelPos = new();
    private readonly List<ElementId> _labelIds = new();

    // 이 마법사가 격리하거나 잠근 3D 뷰. 닫을 때 이것만 되돌린다(사용자가 잠가 둔 다른 뷰는 건드리지 않는다).
    private readonly HashSet<ElementId> _isolatedByUs = new();
    private readonly HashSet<ElementId> _lockedByUs = new();

    private ElementId? _shown;      // 지금 화면에 단독으로 띄워 둔 부재
    private bool _wrongDocShown, _goneShown, _errorShown, _showFailShown;

    private void ClearLabels(Document doc)
    {
        _labelPos.Clear();
        if (_labelIds.Count == 0) return;
        try
        {
            using var tx = new Transaction(doc, "DH 라벨 삭제");
            tx.Start();
            foreach (var id in _labelIds) { try { doc.Delete(id); } catch { } }
            if (tx.Commit() == TransactionStatus.Committed) _labelIds.Clear(); // 못 지웠으면 다음 정리 때 다시 지운다
        }
        catch { }
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
                if (view is View3D v3 && !v3.IsLocked)
                {
                    try { v3.SaveOrientationAndLock(); _lockedByUs.Add(v3.Id); } catch { }
                }
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

    /// <summary>
    /// 쌓인 값을 한 트랜잭션에 적는다. 못 적은 것이 있으면 조용히 넘기지 않고 알린다.
    /// 식 칸을 건드린 부재는 검산 칸(BIM물량·수식물량·물량차·판정)도 '지금 칸에 든 식'으로 다시 적는다 —
    /// 칸 이름([L1] 등)이 든 식이나 빈 식으로 바꿨으면 '수식 없음'이 되어, 앞서 적힌 '일치'가 남지 않는다.
    /// </summary>
    private void FlushWrites(Document doc)
    {
        if (_writes.Count == 0) return;
        var jobs = _writes.ToList();
        _writes.Clear();
        var failed = new List<string>();
        var wrote = new Dictionary<ElementId, string>(); // 되읽기용: 부재 → 마지막으로 적은 식
        try
        {
            // 적어도 되는 부재인지는 트랜잭션을 열기 전에 본다(작업공유 상태는 트랜잭션 중에는 믿기 어렵다).
            var blocked = new Dictionary<ElementId, string>();
            foreach (var tid in jobs.SelectMany(j => j.Targets).Distinct())
            {
                var el = doc.GetElement(tid);
                string? why = el == null ? null : FormulaAutoFill.WhyNotEditable(doc, el);
                if (why != null) blocked[tid] = why;
            }

            TransactionStatus status;
            using (var tx = new Transaction(doc, "DH 비정형 값 입력"))
            {
                tx.Start();
                foreach (var job in jobs)
                    foreach (var tid in job.Targets)
                    {
                        var el = doc.GetElement(tid);
                        if (el == null) { failed.Add($"Id {tid.Value}: 부재를 찾을 수 없음"); continue; }
                        if (blocked.TryGetValue(tid, out var why)) { failed.Add($"Id {tid.Value}: {why}"); continue; }
                        try
                        {
                            if (job.Values != null)
                                foreach (var kv in job.Values) DimensionExtractor.WriteMeters(el, kv.Key, kv.Value);
                            if (!string.IsNullOrWhiteSpace(job.Code))
                                DimensionExtractor.WriteString(el, "DH_ElementCode", job.Code);
                            if (job.Formula != null)
                            {
                                if (DimensionExtractor.WriteString(el, FormulaAutoFill.ParamName, job.Formula))
                                    wrote[tid] = job.Formula;
                                else if (job.Formula.Length > 0)
                                    failed.Add($"Id {tid.Value}: 식 칸(DH_Formula)이 없거나 읽기 전용이라 식을 적지 못함");
                            }
                        }
                        catch (Exception ex) { failed.Add($"Id {tid.Value}: {ex.Message}"); continue; }

                        if (job.Formula != null)
                        {
                            try { FormulaAutoFill.WriteVerdict(el, FormulaAutoFill.CurrentFormula(el)); }
                            catch (Exception ex)
                            {
                                failed.Add($"Id {tid.Value}: 식은 적었지만 판정 칸을 고치지 못함 — {ex.Message}");
                                FormulaAutoFill.MarkVerdictUnknown(el); // 앞서 적힌 판정이 새 식 옆에 남지 않게
                            }
                        }
                    }
                status = tx.Commit();
            }
            if (status != TransactionStatus.Committed)
            {
                Dialogs.Show("DH 수량산출 — 저장 안 됨",
                    $"입력한 값이 저장되지 않았습니다(Revit이 변경을 되돌림: {status}).\n다시 입력해 주세요.");
                return;
            }
            // 되읽기: 커밋 뒤에 새로 읽은 식이 적은 식과 같은가
            foreach (var kv in wrote)
            {
                var el = doc.GetElement(kv.Key);
                if (el == null || FormulaAutoFill.CurrentFormula(el) != (string.IsNullOrWhiteSpace(kv.Value) ? "" : kv.Value))
                    failed.Add($"Id {kv.Key.Value}: 저장한 식이 다시 읽은 값과 다름");
            }
            if (failed.Count > 0)
                Dialogs.Show("DH 수량산출 — 일부 저장 안 됨",
                    $"{failed.Count}건을 저장하지 못했습니다.\n\n" + string.Join("\n", failed.Take(8)));
        }
        catch (Exception ex)
        {
            Dialogs.Show("DH 수량산출 — 저장 안 됨", "입력한 값을 저장하지 못했습니다:\n" + ex.Message + "\n다시 입력해 주세요.");
        }
    }

    /// <summary>이 마법사가 격리·잠금한 3D 뷰만 되돌린다. (★ 트랜잭션 필요)</summary>
    private void RestoreViews(Document doc)
    {
        try
        {
            var views = _isolatedByUs.Union(_lockedByUs)
                .Select(id => doc.GetElement(id) as View3D)
                .Where(v => v != null && v.IsValidObject && !v.IsTemplate)
                .Select(v => v!)
                .Where(v => (_isolatedByUs.Contains(v.Id) && v.IsTemporaryHideIsolateActive()) ||
                            (_lockedByUs.Contains(v.Id) && v.IsLocked))
                .ToList();
            if (views.Count > 0)
            {
                using var txr = new Transaction(doc, "DH 격리·잠금 해제");
                txr.Start();
                foreach (var v in views)
                {
                    if (_isolatedByUs.Contains(v.Id) && v.IsTemporaryHideIsolateActive())
                        v.DisableTemporaryViewMode(TemporaryViewMode.TemporaryHideIsolate);
                    if (_lockedByUs.Contains(v.Id) && v.IsLocked) { try { v.Unlock(); } catch { } }
                }
                if (txr.Commit() != TransactionStatus.Committed) { TellRestoreFailed(); return; }
            }
            _isolatedByUs.Clear();
            _lockedByUs.Clear();
        }
        catch { TellRestoreFailed(); }
    }

    // 닫을 때 화면을 되돌리지 못했다 — 다시 시도할 기회가 없으니 직접 푸는 법을 알린다.
    private static void TellRestoreFailed()
    {
        try
        {
            Dialogs.Show("DH 수량산출",
                "마법사가 걸어 둔 부재 격리(또는 3D 뷰 잠금)를 풀지 못했습니다.\n" +
                "뷰 조절 막대의 [임시 숨기기/분리 재설정]과 [3D 뷰 잠금 해제]로 직접 풀어 주세요.");
        }
        catch { }
    }

    private void NotifyWrongDocOnce()
    {
        if (_wrongDocShown) return;
        _wrongDocShown = true;
        Dialogs.Show("DH 수량산출",
            "마법사를 띄운 문서가 지금 화면에 없습니다.\n그 문서로 돌아가면 측정과 부재 확대를 이어서 할 수 있습니다.\n" +
            "(입력한 값은 원래 문서에 저장됩니다.)");
    }

    public void Execute(UIApplication app)
    {
        try
        {
            var uidoc = app.ActiveUIDocument;
            var doc = TargetDoc ?? uidoc?.Document;
            if (doc == null || !doc.IsValidObject)
            {
                // 문서가 닫혔다 — 할 수 있는 일이 없다. 한 번 알리고 마법사를 닫게 한다.
                int lost = _writes.Count;
                _writes.Clear();
                PickField = null; ClearLabelField = null; Restore = false; RunDeductionOnClose = false;
                if (!_goneShown)
                {
                    _goneShown = true;
                    Dialogs.Show("DH 수량산출",
                        "마법사를 띄운 문서가 닫혔습니다." +
                        (lost > 0 ? $"\n방금 입력한 값 {lost}건은 저장하지 못했습니다." : "") + "\n마법사를 닫습니다.");
                    try { OnDocumentGone?.Invoke(); } catch { }
                }
                try { OnRestored?.Invoke(); } catch { }
                return;
            }
            // 화면을 다루는 일은 그 문서가 활성일 때만. 값 기록과 정리는 문서만 열려 있으면 한다.
            bool active = uidoc != null && uidoc.Document.Equals(doc);
            bool closing = Restore;

            // 1) 쌓인 값 기록 (트랜잭션) — 동일 형상 그룹 전원에 동일 적용
            FlushWrites(doc);

            // 2) 보여줄 부재가 바뀌었으면 먼저 그 부재를 단독으로 띄운다(측정은 화면에 뜬 부재에서 하게 된다).
            if (!closing && IsolateTarget != null && (_shown == null || IsolateTarget.Value != _shown.Value))
            {
                if (!active) NotifyWrongDocOnce();
                else if (ShowTarget(uidoc!, doc, IsolateTarget)) _shown = IsolateTarget;
            }

            // 3) 칸 값 지움 → 그 칸의 임시 라벨만 삭제
            if (!string.IsNullOrEmpty(ClearLabelField))
            {
                string f = ClearLabelField!;
                ClearLabelField = null;
                if (_labelPos.Remove(f) && active && !closing) RefreshLabels(uidoc!);
            }

            // 4) 모서리 측정 (닫는 중이면 시작하지 않는다)
            if (!string.IsNullOrEmpty(PickField))
            {
                string field = PickField!;
                PickField = null;
                if (!closing && !Restore) // 저장 실패 창이 떠 있는 사이에 닫혔을 수도 있다
                {
                    if (!active) NotifyWrongDocOnce();
                    else PickEdges(uidoc!, doc, field);
                }
            }

            // 5) 닫기 정리: 라벨 삭제 + (이 마법사가 건) 임시 격리 해제 + 뷰 잠금 해제
            //    측정하는 동안 마법사가 닫혔을 수도 있으므로 Restore 를 여기서 다시 본다.
            if (closing || Restore)
            {
                Restore = false;
                FlushWrites(doc); // 측정 중에 닫으며 쌓인 마지막 값을 겹침 공제보다 먼저 적는다
                ClearLabels(doc);
                RestoreViews(doc);
                _shown = null;

                // 마법사 종료 → 겹침 공제 자동 실행
                if (RunDeductionOnClose)
                {
                    RunDeductionOnClose = false;
                    try { Dialogs.Show("DH 수량산출 — 겹침 공제(자동)", OverlapResolver.Resolve(doc)); }
                    catch (Exception ex) { Dialogs.Show("DH 수량산출 — 오류", "겹침 공제 중 오류:\n" + ex.Message); }
                }
                try { OnRestored?.Invoke(); } catch { }
            }
        }
        catch (Exception ex)
        {
            // 값 기록은 FlushWrites 가 따로 알린다. 여기까지 온 것은 화면 처리 쪽 오류다 — 한 번은 알린다.
            if (!_errorShown)
            {
                _errorShown = true;
                try { Dialogs.Show("DH 수량산출", "마법사 화면 처리 중 오류가 났습니다:\n" + ex.Message); } catch { }
            }
        }
    }

    /// <summary>모서리를 하나씩 클릭해 길이 합을 잰다(선택색으로 표시, 합계 실시간). 다 고르면 Esc.</summary>
    private void PickEdges(UIDocument uidoc, Document doc, string field)
    {
        // (꺾인 벽체 등은 펼친 전체 길이를 단면에 적용하므로 분절 모서리들을 합산)
        var picked = new List<Reference>();
        var keys = new List<string>();   // 중복(더블클릭) 판별용 안정 식별자
        var lens = new List<double>();    // 각 모서리 길이(ft) — 토글 해제 시 차감
        double feet = 0;
        Measuring = true;
        try
        {
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
        }
        finally { Measuring = false; } // 중간에 오류가 나도 '측정 중'으로 남지 않게

        if (Restore) return; // 측정하는 동안 마법사가 닫혔다 — 라벨을 새로 붙이지 않는다

        // 측정한 칸의 임시 라벨(첫 모서리 중점에 파라미터명) 갱신
        if (picked.Count > 0 &&
            doc.GetElement(picked[0])?.GetGeometryObjectFromReference(picked[0]) is Edge ed0 &&
            ed0.AsCurve() is Curve c0)
            _labelPos[field] = c0.Evaluate(0.5, true);
        else
            _labelPos.Remove(field);
        RefreshLabels(uidoc);
    }

    /// <summary>부재 하나만 3D 뷰에 단독으로 띄우고 확대한다. 띄웠으면 true.</summary>
    private bool ShowTarget(UIDocument uidoc, Document doc, ElementId target)
    {
        try
        {
            // 3D 뷰 확보 (+ 이전 객체의 임시 라벨 삭제)
            View3D? v3d = Get3DView(doc, uidoc.ActiveView);
            if (v3d == null)
            {
                if (!_showFailShown)
                {
                    _showFailShown = true;
                    Dialogs.Show("DH 수량산출", "3D 뷰를 만들 수 없어 부재를 화면에 띄우지 못했습니다.\n(식 입력과 저장은 그대로 됩니다.)");
                }
                return false;
            }
            ClearLabels(doc);

            var ids = new List<ElementId> { target };
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
                _isolatedByUs.Add(v3d.Id);
                txi.Commit();
            }
            catch (Exception ex) { isoError = $"{ex.GetType().Name}: {ex.Message}"; }

            // 뷰 활성화 + 선택 + 새로고침
            if (uidoc.ActiveView.Id != v3d.Id) uidoc.ActiveView = v3d;
            uidoc.Selection.SetElementIds(ids);
            uidoc.RefreshActiveView();

            // 대상으로 확대
            var tgt = doc.GetElement(target);
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

            // 격리가 실제로 적용됐는지 점검 — 안 됐으면 '한 번만' 원인 안내
            if (!_diagShown && (isoError != null || !v3d.IsTemporaryHideIsolateActive()))
            {
                _diagShown = true;
                var tcat = tgt?.Category?.Name ?? "(없음)";
                bool linked = tgt is RevitLinkInstance;
                Dialogs.Show("DH 수량산출 — 격리 디버그(최초 1회)",
                    $"임시 격리가 화면에 적용되지 않았습니다.\n\n" +
                    $"• 뷰: {v3d.Name}\n• 부재: {tcat} (Id {target.Value})\n" +
                    $"• 격리 활성: {v3d.IsTemporaryHideIsolateActive()}\n" +
                    $"• 링크부재 여부: {linked}\n" +
                    $"• 오류: {isoError ?? "없음"}\n\n" +
                    "이 내용을 그대로 캡처해 전달해 주세요.");
            }
            return true;
        }
        catch (Exception ex)
        {
            if (!_showFailShown)
            {
                _showFailShown = true;
                try
                {
                    Dialogs.Show("DH 수량산출",
                        "부재를 화면에 띄우지 못했습니다:\n" + ex.Message + "\n(지금 화면의 부재가 마법사에 표시된 부재와 다를 수 있습니다.)");
                }
                catch { }
            }
            return false;
        }
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
