using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DH.Takeoff.Core.Formwork;
using DH.Takeoff.Core.View3D;

namespace DH.Takeoff.Revit;

// R73a2 — 빠진 자리(JACK 1030: 트렌치 안쪽 옆면을 계단이 깎은 자리 «계단 0.03㎡ · 공제» 칸이 «너무 어려워» → 고름 «빠진 자리를 3D에 빗금으로»):
//  · 고른 면(없으면 고른 판의 면)이 다른 부재 때문에 빠진 자리를 3D 에 빨간 빗금(ShowFaceMarks — 점선 갈래 4) · 카드에 이웃 이름마다 한 줄. 전의 «겹침» 칸(R67) · «이 짝 전부에»(R71)를 대신.
//  · [같은 곳 모두] = 짝 예외 한 줄 «면 주인 ← 이웃 = 자리만 거푸집»(옆면이면 «옆면만» — FormworkBoard.SamePlaces · ChangeRules) · n = 그 줄로 바뀌는 면 수(0 이면 단추 없음).
//  · 수량은 누를 때만 바뀐다(문턱 없음) · 되돌리기 한 줄씩.
//  · R73a4 «이 부재만 보기»: 빗금 · 카드 줄 = 그 부재의 모든 면(판에 든 면) · 넣은 자리도 청록 빗금(점선 갈래 5) · 면 · 이웃마다 이름 딱지 ·
//    빗금 · 딱지는 면마다 그 면의 법선을 달아 뒤를 보는 면의 것은 그리지 않는다(앞뒤 면의 빗금이 겹쳐 보이지 않게).
//  · R73a5(JACK 1102 «저런 부분 빼기 다시 넣기든 기능을 좀 개선해 줘 · 위에서 고르는 것보다는 빼기 버튼을 누르고 직접 선택하는 방식으로»): 카드 줄은 상태만
//    «계단 0.03㎡ 빠짐» · «계단 0.03㎡ 넣음»(줄마다의 [빼지 않기] · [다시 빼기]는 걷고 3D 왼쪽 위 [빼기] · [넣기] 도구로 — CutTools.cs) · [같은 곳 모두]는 빠진 줄의 작은 글 단추로 그대로.
//    말은 «빼지 않음» 대신 «넣음 · 넣기»(JACK «빼기 / 다시 넣기»).
public sealed partial class PlacePartView
{
    private readonly StackPanel _fwCuts = new() { Margin = new Thickness(0, 0, 0, 4) };
    private List<FormworkBoard.FaceCut> _fwCutList = new();
    private List<FormworkBoard.FaceCuts> _fwCutByFace = new();     // 면마다(R73a4 — 빗금의 법선 · 이름 딱지)
    private List<BoardFace> _fwCutFaceList = new();                 // 빗금을 건 면(R73a5 — 도구가 누를 자리를 이 면에서 찾는다)
    private int _fwCutFaces;                                        // 빗금 · 카드 줄을 본 면 수(일지)
    private string _fwCutKey = "";
    private const int CutLinesShown = 4;                       // 줄은 넓은 차례로 넷까지(나머지는 «외 n곳» 한 줄 · 풍선 글)

    // 빠진 자리를 볼 면: 고른 면 · 없으면 고른 판의 면(끌기 · 딱지로 판을 고른 때). 보이는 면만(거름 · 숨기기로 가린 면은 빗금도 줄도 없음).
    // R73a4 «이 부재만 보기»: 그 부재(들)의 판에 든 면 모두(같은 평면 면은 이미 다 든다).
    private List<BoardFace> CutFaces()
    {
        if (_board == null) return new List<BoardFace>();
        if (SoloOn) return _fwIso!.Order().SelectMany(id => _board.FacesOfElement(id)).Where(f => f.Piece >= 0 && FaceShown(f)).ToList();
        var faces = FocusFaces();
        if (faces.Count == 0) faces = SelectedPieceList().SelectMany(p => _board.FacesOfPiece(_board.IndexOf(p.Lead))).ToList();
        // 판에 안 든 면은 카드 줄 · 단추가 없다(판 띠가 숨음) — 빗금도 그리지 않는다(검토 낮음 L2). 같은 부재 · 같은 평면 면까지(단추가 바꾸는 범위 = 빗금 · 넓이 — 검토 M2).
        faces = faces.Where(f => f.Piece >= 0 && FaceShown(f)).ToList();
        return faces.Count == 0 ? faces : _board.SamePlaneFaces(faces).Where(f => f.Piece >= 0 && FaceShown(f)).ToList();
    }

    // 카드의 빠진 자리 줄을 지금 고른 것으로(다시 잴 수 없으면 줄 없음 — 바꿀 수 없다).
    private void RefreshCutLines()
    {
        _fwCuts.Children.Clear();
        var faces = CutFaces();
        bool on = _board is { Remeasure: not null } && Step == 1 && faces.Count > 0;
        _fwCutList = on ? _board!.CutsOf(faces) : new List<FormworkBoard.FaceCut>();
        // R73a5: 빗금마다 이름 딱지(넓이 문턱 없음 — 전의 0.05 ㎡ 가 녹촌 트렌치 «계단» 0.03 ㎡ 를 숨겼다 · 고른 면만 작은 것까지 보이던 R73a4 의 덧댐은 걷음).
        _fwCutByFace = on && SoloOn ? _board!.CutsByFace(faces, SoloMarkMin) : new List<FormworkBoard.FaceCuts>();
        _fwCutFaceList = on ? faces : new List<BoardFace>();
        _fwCutFaces = on ? faces.Count : 0;
        _fwCutKey = (SoloOn ? "solo#" : "") + string.Join("|", faces.Select(f => f.Index)) + "#" + string.Join("|", _fwCutList.Select(c => $"{c.Label}:{M2(c.Area)}:{M2(c.Kept)}:{c.Rings.Count}:{c.KeptRings.Count}"));
        for (int i = 0; i < Math.Min(_fwCutList.Count, CutLinesShown); i++) _fwCuts.Children.Add(CutLine(i, _fwCutList[i], faces));
        if (_fwCutList.Count > CutLinesShown)
        {
            var rest = _fwCutList.Skip(CutLinesShown).ToList();
            // 넣은 갈래만 남으면 «0.00㎡ 빠짐»이 아니라 «넣음 n»(R73a4 — 부재 통째 줄은 갈래가 많다).
            double restArea = rest.Sum(c => c.Area);
            int restKept = rest.Count(c => c.Area < FormworkBoard.JointChipMin);
            string restText = $"외 {rest.Count}곳" + (restArea >= FormworkBoard.JointChipMin ? $" · {M2(restArea)}㎡ 빠짐" : "") + (restKept > 0 ? $" · 넣음 {restKept}" : "");
            var more = new TextBlock { Text = restText, Foreground = _sub, FontSize = 12, Margin = new Thickness(21, 0, 0, 2) };
            _ui.Mark(more, string.Join("\n", rest.Select(CutText)), "fw.cut.more");
            _fwCuts.Children.Add(more);
        }
        _fwCuts.Visibility = _fwCuts.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        RefreshCutTools();                         // [빼기] · [넣기] 도구(R73a5 — 빗금이 없으면 꺼짐)
    }

    // 줄 글(R73a5): «계단 0.03㎡ 빠짐» · «계단 0.03㎡ 넣음» · 둘 다면 «계단 0.60㎡ 빠짐 · 0.03㎡ 넣음».
    private static string CutText(FormworkBoard.FaceCut cut)
    {
        bool cutting = cut.Area >= FormworkBoard.JointChipMin, kept = cut.Kept >= FormworkBoard.JointChipMin;
        return cutting ? $"{cut.Label} {M2(cut.Area)}㎡ 빠짐" + (kept ? $" · {M2(cut.Kept)}㎡ 넣음" : "") : $"{cut.Label} {M2(cut.Kept)}㎡ 넣음";
    }

    // 줄 하나: [빗금 네모] «계단 0.03㎡ 빠짐» [같은 곳 모두 (트렌치 ← 계단 n곳)] · 넣은 것은 [청록 네모] «계단 0.03㎡ 넣음».
    private FrameworkElement CutLine(int i, FormworkBoard.FaceCut cut, List<BoardFace> faces)
    {
        bool cutting = cut.Area >= FormworkBoard.JointChipMin;
        var line = new WrapPanel { Margin = new Thickness(0, 1, 0, 3), MinHeight = 24 };   // 줄 높이를 같게(R73a5 — 넣고 빼도 카드 높이가 그대로라 3D 가 들썩이지 않게)
        var mark = new Border
        {
            Width = 14, Height = 14, CornerRadius = new CornerRadius(2), Margin = new Thickness(0, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center,
            // 넣은 줄은 3D 의 청록 빗금과 같은 네모(R73a4 — 전에는 빈 네모).
            Background = Model3DView.HatchBrush(_ui.IsDark, kept: !cutting), BorderBrush = new SolidColorBrush(Model3DView.HatchColor(_ui.IsDark, kept: !cutting)), BorderThickness = new Thickness(1),
        };
        line.Children.Add(mark);
        var text = new TextBlock { Text = CutText(cut), FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0), Foreground = _text };
        // 이 부재만 보기(R73a4)는 뒤를 보는 면의 빗금을 그리지 않는다 — 안 보이면 돌려 보라고 풍선 글에.
        string turn = SoloOn ? " 반대쪽 면의 빗금은 3D 를 돌리면 보입니다." : "";
        _ui.Mark(text, cutting ? $"{cut.Label}(부재 {cut.Others.Count})가 닿아 거푸집에서 뺀 자리 — 3D 의 빨간 빗금. 3D 왼쪽 위 [넣기]를 누르고 그 빗금을 누르면 거푸집으로 셉니다." + turn
                              : $"{cut.Label} 자리도 거푸집으로 셉니다 — 3D 의 청록 빗금. [빼기]를 누르고 그 빗금을 누르면 다시 뺍니다." + turn, "fw.cut." + i);
        line.Children.Add(text);
        if (!cutting) return line;
        var (pair, n) = _board!.SamePlaces(faces, cut);
        if (pair == null || n <= 0) return line;
        // [같은 곳 모두](R73a2 — 범위가 다름: 모델 전체의 비슷한 자리) · 작은 글 단추(R73a5 — 줄의 상태 글보다 앞에 나서지 않게).
        string owner = PourOrder.LabelOf(pair.Owner), other = PourOrder.LabelOf(pair.Other);
        var all = _ui.Button($"같은 곳 모두 ({owner} ← {other} {n}곳)", "BtnGhost",
            $"모델 전체의 {owner}{(pair.Scope == PourOrder.Scope.Side ? " 옆면" : "")} ← {other} 자리를 거푸집으로 넣습니다 — 면 {n}곳(규칙 탭의 짝 예외 «{owner} ← {other} : 자리만 거푸집»).",
            "btn.fw.cut.all." + i);
        all.Focusable = false;
        all.MinHeight = 22;
        all.FontSize = 12;
        all.Padding = new Thickness(6, 0, 6, 1);
        all.VerticalAlignment = VerticalAlignment.Center;
        all.Foreground = _ui["AccentText"];
        all.Click += (_, _) => KeepEverywhere(pair);
        line.Children.Add(all);
        return line;
    }

    // [같은 곳 모두]: 짝 예외 한 줄(규칙 탭과 같은 길 — 되돌리기 한 줄 · 위 띠에 바뀐 면 · ㎡).
    private bool KeepEverywhere(PourOrder.Pair pair) =>
        ChangeRules($"같은 곳 모두 {PourOrder.LabelOf(pair.Owner)} ← {PourOrder.LabelOf(pair.Other)} → 넣음", b => b.SetPair(pair), "이미 그렇게 셉니다.");

    // 3D 빗금(ShowFaceMarks 가 고른 면의 치수와 함께 건다): 빠진 자리 고리(4 — 빨강) · 넣은 자리 고리(5 — 청록 · R73a4) · 장면 좌표 + 그 면의 바깥 법선
    // («이 부재만 보기»만 — 면마다 · 뒤를 보는 면은 화면이 숨김 · 그 밖은 영 벡터 = 늘 그림 · 전과 같음).
    private IEnumerable<((IReadOnlyList<Vec3> points, bool closed, int kind) guide, Vec3 facing)> CutGuides(Vec3 origin)
    {
        (IReadOnlyList<Vec3>, bool, int) Ring(IReadOnlyList<Vec3> r, int kind) => (r.Select(q => q - origin).ToList(), true, kind);
        if (_fwCutByFace.Count > 0)
            return _fwCutByFace.SelectMany(f => f.Cuts.SelectMany(c => c.Rings.Select(r => (Ring(r, 4), f.Normal)).Concat(c.KeptRings.Select(r => (Ring(r, 5), f.Normal)))));
        return _fwCutList.SelectMany(c => c.Rings.Select(r => (Ring(r, 4), Vec3.Zero)).Concat(c.KeptRings.Select(r => (Ring(r, 5), Vec3.Zero))));
    }

    // 빗금 이름 딱지(R73a4 — «이 부재만 보기»만): 면 · 이웃마다 하나(빠진 / 넣은 자리마다 · R73a5 부터 넓이 문턱 없음) · 면에서 2 cm 띄움 · 그 면의 법선.
    private IEnumerable<((Vec3 at, string text, Vec3 from, Vec3 to) label, Vec3 facing)> CutLabels(Vec3 origin) =>
        _fwCutByFace.SelectMany(f => f.Marks.Select(m => m.At - origin + f.Normal * 0.02).Zip(f.Marks, (at, m) => ((at, m.Label, at, at), f.Normal)));

    // ── 시험에서 쓰는 길 ──

    /// <summary>카드의 빠진 자리 줄(R73a2 — 글 · 보이는 단추 이름 · R73a5 부터 단추는 [같은 곳 모두]뿐).</summary>
    public IReadOnlyList<(string text, IReadOnlyList<string> buttons)> CutLines =>
        _fwCuts.Visibility != Visibility.Visible ? Array.Empty<(string, IReadOnlyList<string>)>()
        : _fwCuts.Children.OfType<WrapPanel>().Select(l => (l.Children.OfType<TextBlock>().First().Text, (IReadOnlyList<string>)l.Children.OfType<Button>().Select(b => b.Content as string ?? "").ToList())).ToList();

    /// <summary>지금 고른 것의 빠진 자리(R73a2) · 면마다(R73a4 «이 부재만 보기» — 빗금 · 딱지) · 본 면 수.</summary>
    public IReadOnlyList<FormworkBoard.FaceCut> Cuts => _fwCutList;
    public IReadOnlyList<FormworkBoard.FaceCuts> CutsByFace => _fwCutByFace;
    public int CutFaceCount => _fwCutFaces;
}
