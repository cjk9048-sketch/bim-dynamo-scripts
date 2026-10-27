using System.Diagnostics;
using System.Windows.Input;
using DH.Takeoff.Core;
using DH.Takeoff.Core.Formwork;
using DH.Takeoff.Core.View3D;

namespace DH.Takeoff.Revit;

// R61b — ③ 콘크리트 줄의 3D 길이 숫자(JACK 2026-10-26 «3D 뷰에 길이 숫자가 안 나와» · 인터뷰 «정확하게» · «공제는 빨간 점선»):
// 고른 콘크리트 줄의 부재마다 식의 숫자를 그 숫자가 재는 선분 가운데에 · 항의 도형은 점선(더함 파랑 · 뺌 빨강 · 잰 선 회색).
public sealed partial class PlacePartView
{
    private const int MaxConcreteMembers = 12;       // 한 줄에서 숫자를 띄우는 부재(보이는 · 큰 부재부터)
    private const int MaxConcreteLabels = 200;
    private const int ConcreteBudgetMs = 1500;       // 한 번 고를 때 다시 푸는 시간 — 넘는 부재는 가운데 식(계획 검토 M8)
    private const int CenterTextMax = 40;            // 가운데 식 딱지의 글 길이(긴 높이장 식은 앞부분 …)
    private const int MaxGuidePoints = 20000;        // 점선 꼭짓점(소프트웨어로만 그리는 Revit 에서 돌릴 때 끊기지 않게 — 설치 전 검토 중간)
    private static readonly string ConcreteCapped = $"길이 숫자는 큰 부재 {MaxConcreteMembers}개 · 숫자 {MaxConcreteLabels}개까지";
    private readonly Dictionary<long, FormulaGeometry?> _rvConcrete = new();   // 부재마다 다시 푼 도형(③ 에 들어올 때 비움)
    private string? _rvConcreteShown;                                          // 지금 숫자를 띄운 부재들(같으면 다시 놓지 않는다)
    private bool _rvConcreteAgain;                                             // 줄을 골랐다 — 같은 부재여도 한 번 다시 놓는다(미룬 부재를 푼다)

    private void ShowConcreteLengths(IReadOnlyDictionary<long, string> formulas)
    {
        var shown = _rvMembers.Where(id => formulas.ContainsKey(id) && IndexOf(id) is int i && i >= 0 && ReviewShown(i))
            .OrderByDescending(id => Volume(_memberMeshes[IndexOf(id)].Box)).ThenBy(id => id).ToList();
        string key = string.Join(",", shown);
        if (_rvConcreteShown == key && !_rvConcreteAgain) return;
        _rvConcreteShown = key;
        _rvConcreteAgain = false;
        _rvLabeled = null;
        var labels = new List<(Vec3, string, Vec3, Vec3)>();
        var tokens = new List<int>();   // 딱지마다 고른 줄 식의 숫자 차례(R65b — 모르면 −1 → 같은 글로)
        var guides = new List<(IReadOnlyList<Vec3>, bool, int)>();
        int exact = 0, center = 0, late = 0, points = 0, linked = 0;
        // 고른 줄에서 부재 식의 첫 숫자 차례(R65b — JACK 2026-10-27 «0.1 에 갖다 대면 모든 0.1 이 아니라 그 수식이 쓰인 부분의 0.1 만»).
        var starts = _rv != null && _rvPicked >= 0 && _rvPicked < _rvViews.Count && _rvViews[_rvPicked].item is int pickedItem && pickedItem >= 0 && pickedItem < _rv.Items.Count
            ? _rv.Items[pickedItem].Starts : null;
        bool capped = shown.Count > MaxConcreteMembers;
        long slowest = 0, slowestId = 0;
        var watch = Stopwatch.StartNew();
        var cursor = Mouse.OverrideCursor;
        try
        {
            if (shown.Take(MaxConcreteMembers).Any(id => !_rvConcrete.ContainsKey(id))) Mouse.OverrideCursor = Cursors.Wait;
            foreach (long id in shown.Take(MaxConcreteMembers))
            {
                string formula = formulas[id];
                if (!_rvConcrete.TryGetValue(id, out var geo))
                {
                    if (watch.ElapsedMilliseconds > ConcreteBudgetMs) late++;   // 이번에는 가운데 식 — 다음에 고를 때 푼다
                    else
                    {
                        long start = watch.ElapsedMilliseconds;
                        try { geo = ConcreteGeometry!(id); }
                        catch (Exception ex) { Log?.Invoke($"③ 콘크리트 숫자\t{id}\t오류\t{ex.Message}"); }
                        _rvConcrete[id] = geo;
                        if (watch.ElapsedMilliseconds - start > slowest) (slowest, slowestId) = (watch.ElapsedMilliseconds - start, id);
                    }
                }
                // 다시 푼 식이 산출근거의 식과 글자까지 같을 때만 그 도형(계획 §3.2 · 계획 검토 L12).
                if (geo != null && geo.Terms.Count > 0 && geo.Formula == formula)
                {
                    var (l, g) = ConcreteLabels.Of(geo, _board!.Origin);
                    int more = g.Sum(x => x.points.Count);
                    // 숫자 · 점선 한도를 넘는 부재는 넣지 않는다(숫자 없이 점선만 남지 않게).
                    if (labels.Count + l.Count > MaxConcreteLabels || points + more > MaxGuidePoints) { capped = true; continue; }
                    labels.AddRange(l);
                    // 그 부재 식의 몇째 숫자(ConcreteLabels.Tokens) + 줄 식에서 그 부재 식이 시작하는 차례 = 줄 식의 그 숫자 하나.
                    var own = ConcreteLabels.Tokens(geo);
                    int start = 0;
                    bool startKnown = starts != null && starts.TryGetValue(id, out start);
                    foreach (int t in own) tokens.Add(startKnown && t >= 0 ? start + t : -1);
                    linked += startKnown ? own.Count(t => t >= 0) : 0;
                    guides.AddRange(g);
                    points += more;
                    exact++;
                }
                else if (formula.Length > 0)
                {
                    var middle = _memberMeshes[IndexOf(id)].Box.Center;
                    labels.Add((middle, Short(FormworkLabels.Pretty(formula)), middle, middle));
                    tokens.Add(-1);
                    center++;
                }
            }
        }
        catch (Exception ex)
        {
            // 숫자는 보기 돕기라 못 놓아도 고른 줄 · 화면 맞추기는 그대로(R60 거푸집 숫자와 같이).
            _view3d.SetLabels(Array.Empty<(Vec3, string)>());
            _view3d.SetGuides(Array.Empty<(IReadOnlyList<Vec3>, bool, int)>());
            ShowMessage("길이 숫자를 놓지 못했습니다: " + ex.Message, true);
            return;
        }
        finally { Mouse.OverrideCursor = cursor; }
        _rvLabelTokens = tokens.Take(MaxConcreteLabels).ToList();   // 이은 숫자는 그 하나 · 못 이은 것(−1)은 같은 글로(R63d 그대로)
        _view3d.SetLabels(labels.Take(MaxConcreteLabels).ToList());
        _view3d.SetGuides(guides);
        Log?.Invoke($"③ 콘크리트 숫자\t부재 {shown.Count}(보임) · 정확 {exact} · 가운데 식 {center} · 시간 넘음 {late} · 숫자 {labels.Count}(식 숫자에 이음 {linked}) · 점선 {guides.Count}(점 {points})" +
                    $" · {watch.ElapsedMilliseconds} ms · 가장 오래 {slowest} ms(Id {slowestId})");
        if (capped) ShowMessage(ConcreteCapped);
        else if (_message.Text == ConcreteCapped || _message.Text == LabelsCapped) ShowMessage("");
    }

    private static double Volume(Box3 b)
    {
        if (b.IsEmpty) return 0;
        var s = b.Size;
        return s.X * s.Y * s.Z;
    }

    private static string Short(string text) => text.Length <= CenterTextMax ? text : text[..CenterTextMax] + " …";

    /// <summary>시험용: ③ 콘크리트 숫자를 다시 푼 부재 수.</summary>
    public int ConcreteSolved => _rvConcrete.Count;
}
