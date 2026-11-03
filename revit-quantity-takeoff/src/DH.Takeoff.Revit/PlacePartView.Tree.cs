using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents; // Run
using System.Windows.Input;
using System.Windows.Markup; // XamlReader
using System.Windows.Media;
using DH.Takeoff.Core;
using DH.Takeoff.Core.Formwork;
using DH.Takeoff.Core.View3D;

namespace DH.Takeoff.Revit;

// R73a — ② 보기 먼저(JACK 1030 «거푸집면 설정 자체가 너무 난해 … 목록에서 눌러도 그 부위가 정확히 어딘지도 모르겠고 가시성도 떨어지고» · 계획 §3.1 · §3.4 · §3.6):
//  · 왼쪽 = 위치 나무(층 → 중분류 → 부재 → 면 — 면 줄 = 색 점 · 이름 · 종류 · ㎡ · 고친 면 ● · 식 없음) + 거름 [전체 | 확인할 자리 n | 안 셈 n](판 목록을 걷고 그 자리에).
//    펼친 줄만 짓는다(가상화 목록 — 녹촌 면 1,214). 면 줄 = 그 면을 고르고 날아감 · 부재 줄 = 그 부재 면 모두 · 층 · 중분류 줄 = 펼치기.
//  · 3D 찍기 = 면(세모 → 조각 → 면) · 나무가 펼쳐져 그 줄로. 고른 면 = 맨 위에 제 색 + 분홍 테두리 · 그 부재 = 연회색(맨 위 층 — 옅은 것에 가려지지 않게) ·
//    나머지 = 옅게(Faded — ② 만 · ③ 은 윤곽선 그대로).
//    카드의 단추는 지금처럼 그 면의 판 전체에(검토 M3) — 카드 머리 «판 전체: …»(3D 의 판 전체 둘째 테두리는 R73a2 에서 걷음). 면 하나만 고치기는 R73b.
//  · R73a2 «부재만 보기»(JACK 1030 «목록에서 선택하면 전체가 보이는 게 아니고 그 부재만 보이게 하되 그 부재에 조금이라도 닿은 부재도 함께 보여줘 · 나머지는 전부 투명하고
//    윤곽만 흐리게» · «선택한 부재와 조금이라도 닿은 부재는 서로 색을 달리해줘»): 나무의 면 · 부재 줄 → 그 부재 + 닿은 부재(FormworkBoard.TouchingOf — 분석의 맞닿음 · 깎인 자리 ·
//    무시한 자리 두 쪽 + 한 판 동무)만 보통으로(고른 부재 = 연회색 몸 + 판은 제 색 · 닿은 부재 = 옅은 청록 몸 + 판은 씻은 색) · 나머지 = 흐린 윤곽(Faint — 찍히지 않음) ·
//    그 무리로 날아감 · 3D 오른쪽 위(보기 단추 아래) «부재만 보기 · ■ 고른 부재 · ■ 닿은 부재 n» + [모두 보기](Esc = 고른 것도 풂). 3D 찍기는 무리 안에서 고르기만(다시 거르지 않음).
//  · R73a3 «닿은 근처만 잘라 보기»(JACK 1030 «보를 누르면 천장하고 닿을 텐데 … 천장 슬래브도 다 보이니깐 가려서 안 보이네 … 목록에서 누르면 해당 객체만 뜨면 좋긴 한데
//    그렇게 되면 어디가 겹치는 부분인지 모르니깐»): 닿은 부재 · 그 판은 고른 부재 상자 + 0.3 m(MeshClip.Near — 여럿이면 상자마다 · 합집합) 안의 몫만 진하게(청록 몸 · 씻은 판 ·
//    그림 차례 맨 뒤 «잘라 그린 몫» — 보이니 가리기는 하되 찍히지 · 마우스 밑으로 잡히지 않음) · 그 밖은 흐린 윤곽. 잘라 그린 몫은 고른 부재가 같으면 다시 쓴다(닿은 것만 자름).
//    날아갈 상자도 닿은 부재는 잘라 그린 자리만.
//  · R73a3 Del = 잠깐 숨기기(JACK 1030 — 천장을 치우려 Del 을 눌렀다가 보 판 4.80 ㎡ 가 말없이 빠졌다): 고른 면의 부재(면이 없으면 고른 판 · 문제 딱지의 부재)를 3D 에서만
//    숨긴다(① 숨기기와 같은 묶음 — 값 · 수량 · 기록 · 저장 그대로) · 3D 오른쪽 위 «숨긴 부재 n» + [모두 보기] · 나무 줄을 누르면 그 부재는 다시 보인다. «세지 않음»은 카드 단추로만.
//  · R73a4 «이 부재만 보기»(JACK 1102 «너무 헷갈려 · 일단 닿는 객체만 보이게 하는 건 없던 걸로 하자» · «제일 중요한 건 그냥 목록에서 찍었을 때 심플하게 해당 객체만 보이고
//    그 객체에서 거푸집이 어디에 생기는지 눈으로 직관적으로 보이되, 다른 객체와 닿는 부분만 확인할 수 있으면 돼»): 나무의 면 · 부재 줄 = 그 부재만(Ctrl + 부재 줄 = 고른 부재들) —
//    다른 부재 · 그 부재가 안 든 판은 아예 그리지 않는다(흐린 윤곽 · 잘라 그린 이웃 없음 · 찍히지 않음 · 화면 맞춤 밖 · 종류 · 층 거름도 넘음). 여러 부재를 이은 판은 그 부재 몫만
//    (숨기기의 MeshWithout 그대로). 그 부재 = 연회색 몸 + 거푸집 면은 제 종류 색 · 면 줄 = 그 면 분홍 테두리 · 부재 줄 = 테두리 · 흰 막 없음.
//    닿은 자리 = 그 부재의 모든 면(판에 든 면)에서 이웃이 빠뜨린 자리 빨간 빗금 + 빼지 않은 자리 청록 빗금 + 면 · 이웃마다 작은 이름 딱지(0.05 ㎡ 아래는 딱지 없음 ·
//    뒤를 보는 면의 빗금 · 딱지는 그리지 않음) · 카드 줄도 그 부재 면 모두(단추가 바꾸는 범위 = 빗금). 오른쪽 위 «이 부재만 보기 · 이름 ▨ 빠진 자리 ▨ 빼지 않은 자리» + [모두 보기] · Esc.
//    R73a2 · R73a3 의 닿은 부재 보기는 IsolateOnTreeClick 뒤에 그대로 둔다(«일단»).
//  · R73a5(JACK 1102 «저 부분이 계단인데 계단이라는 글씨가 안 보여» · «목록에서 선택할 때는 3D 뷰 … 2분할로 … 한쪽은 윤곽선에 그 객체만 … 지금 있는 위치 인덱스 부분은 없어도»):
//    닿은 자리 이름 딱지는 넓이 문턱 없이 빗금마다(빠진 · 넣은 자리 — 겹치면 비켜 놓고 치수가 먼저 빠짐) · «이 부재만 보기» 동안 3D 를 둘로 나눠 왼쪽 «위치»(Locator.cs) ·
//    3D 오른쪽 아래 평면 지도(R73a)는 걷음.
//  · R73a6(JACK 1102 «뷰 순서를 바꿔 줘 — 전체 뷰가 오른쪽으로 · 면을 어느 뷰든 선택하면 두 뷰 모두» · «표현을 좀 전문적이게» · «안 헷갈리게»): 나눔 = 왼쪽 부재 단독 보기 |
//    오른쪽 «전체 모델»(Locator.cs — 고르기마다 고른 부재로 맞춤 · 찍기 · 마우스 밑) · 말 = 부재 단독 보기 · 공제 위치(닿는 부위) · 전체 보기 · 검토 필요 · 산출 제외 ·
//    고른 면 · 마우스 밑 테두리 = 조각을 합친 모양(Core FaceMesh — 받침 높이로 나눈 조각 사이 이음매 없음).
//  · 고른 면 위에 이름 + 치수(면 평면의 가로 · 세로 — 둘째 자리 · 반 넘게 못 채우면 «≈»).
//  · 마우스 밑 면 = 굵은 테두리(면이 바뀔 때만 지음) + 아래 이름표(부재 · 면 · ㎡ · 지금 종류).
// 기록 · 수량은 바꾸지 않는다(보기만).
public sealed partial class PlacePartView
{
    // ── 고른 면 ──
    private readonly List<FacePatch> _fwFaces = new();                                    // 고른 면(면의 대표 조각 — 덩이를 다시 지어도 조각은 그대로) · 마지막이 첫 면
    private List<(DropRecord record, int solid, int face)> _fwFaceKeys = new();          // 형상을 다시 잰 뒤(R67) 고른 면을 다시 잡을 열쇠
    private readonly List<(MeshData mesh, BoardFace face)> _fwFaceOverlay = new();        // 고른 면만의 겉면(그림 차례 맨 뒤 · 맨 위 층)
    private readonly List<FacePatch> _fwFaceOverlayFor = new();                           // 위 겉면을 지은 고른 면
    private bool _fwFaceOverlayWhole;                                                      // 위 겉면을 «이 부재만 보기» 부재 통째로 지었다(R73a4 — 면 겉면 없음)
    private readonly Dictionary<FacePatch, (List<FacePatch> patches, MeshData mesh)> _fwFaceMeshes = new(ReferenceEqualityComparer.Instance);
    private BoardFace? _fwHoverFace;
    private bool _fwFocusLook;                                                             // 고른 면을 보이는 중(부재는 찍지 않는다 — 그 위의 옅은 판을 찍게)
    private List<(IReadOnlyList<Vec3> points, bool closed, int kind)> _fwCheckGuides = new();   // 판 모양 검사의 빨간 선(R68 — 고른 면의 치수 선과 함께 건다)
    private string? _fwMarksFor;                                                           // 면 위 딱지를 지은 열쇠(같으면 다시 놓지 않는다)
    private int _fwFlights;                                                                // 날기 수(일지 — 처음 한 번만)

    // ── 부재만 보기(R73a2) ──
    private HashSet<long>? _fwIso;                                                         // 나무 줄로 고른 부재(null = 모두 보기)
    private readonly HashSet<long> _fwIsoNear = new();                                     // 닿은 부재(그릴 때마다 다시 셈 — 판을 다시 지어도 맞게)
    private readonly HashSet<int> _fwIsoOwn = new(), _fwIsoNearPieces = new();              // 고른 부재가 든 판 · 닿은 부재만 든 판(덩이 차례)
    private FrameworkElement? _fwIsoBar;
    private readonly TextBlock _fwIsoCount = new() { FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center };
    // R73a3: 닿은 부재 · 판의 잘라 그린 몫(그림 차례 맨 뒤 · source = 그 몫을 낸 그림 차례 — 부재 또는 MemberCount + 덩이) · 그림에 넘긴 몫(바뀌면 겉면을 갈아 끼운다) ·
    // 고른 부재가 같은 동안 다시 쓰는 몫(겉면 → 잘라 그린 몫) · 띠의 이름 · 숨김 수 · 부재만 보기를 끝낸 까닭(일지).
    private readonly List<(MeshData mesh, int source)> _fwIsoClips = new(), _fwClipsDrawn = new();
    private readonly Dictionary<MeshData, MeshData> _fwIsoClipCache = new(ReferenceEqualityComparer.Instance);
    private string? _fwIsoClipKey;
    private readonly TextBlock _fwIsoTitle = new() { FontSize = 12.5, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _fwIsoHidden = new() { FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
    private FrameworkElement? _fwIsoLegend;
    private bool _fwIsoLogged;
    private string? _fwIsoEndWhy;
    // R73a4 «이 부재만 보기»: _fwIso 를 그대로 쓴다(끝내는 길 — Esc · [모두 보기] · 숨기기 · 무리 밖을 고름 · ① 로 · 새 판 — 이 같다) · 닿은 부재는 세지 않는다.
    // 부재 줄로 골랐을 때의 고른 면(그대로인 동안 = 부재 통째 — 면 테두리 · 면 딱지 없음) · 띠의 빗금 풀이.
    private bool _fwIsoSolo;
    private List<FacePatch>? _fwSoloWhole;
    private FrameworkElement? _fwSoloLegend, _fwIsoChip;
    private const double SoloMarkMin = 1e-6;                                               // 빗금마다 이름 딱지(R73a5 — 전의 0.05 ㎡ 문턱이 녹촌 트렌치 «계단» 0.03 ㎡ 를 숨겼다)
    // 닿은 부재의 몸(옅은 청록 — 판 종류 색 · 콘크리트 회색 어디와도 갈리게) · 그 판은 이 쪽으로 씻은 색(«맥락»으로 읽히게 — 고른 부재의 판은 제 색 그대로).
    internal static readonly (byte r, byte g, byte b) NearTint = (0x8F, 0xC1, 0xC6);
    private static readonly (byte r, byte g, byte b) WashTo = (0xC6, 0xDE, 0xE1);
    private static (byte r, byte g, byte b) Washed((byte r, byte g, byte b) c) =>
        ((byte)(c.r + (WashTo.r - c.r) * 0.62), (byte)(c.g + (WashTo.g - c.g) * 0.62), (byte)(c.b + (WashTo.b - c.b) * 0.62));

    // ── 나무 ──
    private TreeList? _fwTreeList;
    private LocationTree? _fwTree;
    private IReadOnlyList<BoardFace>? _fwTreeFaces;                                        // 나무를 지은 면 목록(보드가 다시 세면 새 목록 — 다시 짓는다)
    private int _fwTreeFilter;                                                             // 0 전체 · 1 확인할 자리 · 2 안 셈
    private readonly HashSet<string>[] _fwTreeToggled = { new(StringComparer.Ordinal), new(StringComparer.Ordinal), new(StringComparer.Ordinal) };   // 거름마다 처음과 다르게 펼침 · 접음
    private List<LocationTree.Node> _fwTreeRows = new();
    private readonly RadioButton?[] _fwFilters = new RadioButton?[3];
    private const double TreeIndent = 14;

    // 고른 면(지금 면 — 없어진 것은 뺌 · 차례 = 고른 차례).
    private List<BoardFace> FocusFaces()
    {
        var found = new List<BoardFace>();
        if (_board == null) return found;
        foreach (var anchor in _fwFaces)
            if (_board.FaceOf(anchor) is { } face && !found.Contains(face)) found.Add(face);
        return found;
    }

    private BoardFace? PrimaryFace => _board != null && _fwFaces.Count > 0 ? _board.FaceOf(_fwFaces[^1]) : null;

    // 고른 면을 지금 면으로 정리한다 — 없어진 것 · 판이 고른 것에서 빠진 것(띠의 일 · 다른 고르기가 판을 풀었다) · 같은 면을 가리키게 된 것은 뺀다.
    private void PruneFaces()
    {
        if (_board == null) { _fwFaces.Clear(); return; }
        var plates = _fwSel.Select(p => _board.IndexOf(p)).Where(i => i >= 0).ToHashSet();
        var kept = new List<FacePatch>();
        // R73b(설치 전 검토 중간): 무리째 — 무리의 판 가운데 고른 판이 하나라도 남았거나 무리에 판이 없으면 무리를 다 남긴다(무리 일부만 남지 않게).
        bool Keep(BoardFace face) => face.Siblings.All(s => s.Piece < 0) || face.Siblings.Any(s => s.Piece >= 0 && plates.Contains(s.Piece));
        foreach (var anchor in _fwFaces)
            if (_board.FaceOf(anchor) is { } face && Keep(face) && !kept.Any(p => ReferenceEquals(_board.FaceOf(p), face))) kept.Add(anchor);
        _fwFaces.Clear();
        _fwFaces.AddRange(kept);
    }

    // 그림 차례: … → 줄 몫 → 고른 면(R73a).
    private int FaceOverlayStart => OverlayStart + _fwOverlay.Count;

    // 면만의 겉면(덩이와 같은 띄움 — 고른 면 · 마우스 밑 테두리) · 조각이 같으면 다시 쓴다.
    // R73a7: 한 면처럼 볼 면들(Siblings — 단마다 따로인 계단 옆면)은 함께 · 테두리 하나(대표의 조각을 열쇠로).
    private MeshData FaceMesh(BoardFace face)
    {
        var lead = face.Siblings[0];
        var patches = face.Siblings.SelectMany(f => f.Patches).ToList();
        if (_fwFaceMeshes.TryGetValue(lead.Lead, out var made) && made.patches.SequenceEqual(patches, ReferenceEqualityComparer.Instance)) return made.mesh;
        var mesh = _board!.FaceMesh(lead);         // 테두리 = 조각을 합친 모양(R73a6 — 받침 높이로 나눈 조각 사이 이음매 없이 · JACK «안 헷갈리게»)
        _fwFaceMeshes[lead.Lead] = (patches, mesh);
        return mesh;
    }

    // «이 부재만 보기»(R73a4)인가 · 부재 줄로 고른 그대로인가(부재 통째 — 면 하나를 밝히지 않는다).
    private bool SoloOn => _fwIso != null && _fwIsoSolo && Step == 1;
    private bool SoloWhole => SoloOn && _fwSoloWhole is { } whole && whole.SequenceEqual(_fwFaces, ReferenceEqualityComparer.Instance);

    // 면을 고른다(3D 찍기 · 나무 줄): 고른 면의 판도 고른다(카드의 단추가 판 전체에 — 검토 M3 · 첫 면의 판이 띠의 첫 판) · 나무를 그 줄까지 펼친다 · fly = 날아간다.
    // isolate = 나무 줄로 골랐다: R73a4 «이 부재만 보기»(그 부재만) — IsolateOnTreeClick 이면 R73a2 «부재만 보기»(고른 면의 부재 + 닿은 부재). whole = 부재 줄(R73a4 — 면 테두리 없음).
    private void SelectFaces(IReadOnlyList<BoardFace> faces, SelectMode mode, bool fly, bool isolate = false, bool whole = false)
    {
        if (_board == null) return;
        var watch = Stopwatch.StartNew();
        // R73a7(JACK 1102 «계단 … 옆면이 여러 개로 조각나 있어서 면을 선택하기가 힘들어»): 한 면처럼 볼 면들(Siblings)은 함께 고르고 · 함께 푼다.
        int picks = faces.Select(f => f.Siblings[0]).Distinct().Count();
        // R73b(설치 전 검토 높음): 무리가 판 여럿에 걸쳐도(벽 내측면 + 개구부 옆 띠 · 슬래브 띠) 판 띠 · 판 단추는 누른 면의 판 하나만 — 무리마다 대표 = 누른 면
        // (나무 줄이면 무리째 오므로 판이 있는 가장 넓은 세는 면). 대표는 맨 뒤(첫 면 · 이름표 · 나무 펼치기 — 다 덮인 면 · 나무 밖 면이 되지 않게: 검토 중간).
        var leads = faces.GroupBy(f => f.Siblings[0]).Select(g => g.Count() == 1 && !g.First().Fixed ? g.First()
            : g.Where(f => f.Piece >= 0).OrderByDescending(f => f.CountedArea).FirstOrDefault() ?? g.FirstOrDefault(f => !f.Fixed) ?? g.First()).ToList();
        faces = faces.SelectMany(f => f.Siblings).Distinct().ToList();
        // Ctrl(더하기 · 빼기)이면 판으로 고른 것(끌기 · 딱지)도 그대로 둔다 — 뺀 면의 판은 그 판에 고른 면이 더 없을 때만 뺀다.
        var plates = mode == SelectMode.Replace ? new List<int>() : _fwSel.Select(p => _board.IndexOf(p)).Where(i => i >= 0).Distinct().ToList();
        var dropped = new List<int>();
        if (mode == SelectMode.Replace) _fwFaces.Clear();
        // Ctrl 은 무리마다(설치 전 검토 중간 — 무리의 일부만 골라져 있으면 하나하나 뒤집혀 3D 는 그대로 고른 것처럼 보였다): 하나라도 골라져 있으면 모두 풂 · 아니면 모두 고름.
        foreach (var group in faces.GroupBy(f => f.Siblings[0]))
        {
            bool held = mode == SelectMode.Toggle && group.Any(f => _fwFaces.Any(p => ReferenceEquals(_board.FaceOf(p), f)));
            var lead = leads.FirstOrDefault(l => ReferenceEquals(l.Siblings[0], group.Key)) ?? group.First();
            foreach (var face in group.OrderBy(f => ReferenceEquals(f, lead) ? 1 : 0))   // 대표가 맨 뒤
            {
                int at = _fwFaces.FindIndex(p => ReferenceEquals(_board.FaceOf(p), face));
                if (at >= 0) _fwFaces.RemoveAt(at);
                if (!held) _fwFaces.Add(face.Lead);   // 다시 고른 면은 맨 뒤(첫 면)
                else if (face.Piece >= 0) dropped.Add(face.Piece);
            }
        }
        var chosen = FocusFaces();
        if (isolate)
        {
            _fwIso = chosen.Count > 0 ? chosen.Select(f => f.ElementId).ToHashSet() : null;   // 그 부재들만 보기(R73a2 · R73a4)
            _fwIsoSolo = !IsolateOnTreeClick;
        }
        _fwSoloWhole = isolate && whole && !IsolateOnTreeClick ? _fwFaces.ToList() : null;
        // 판 = 무리마다 대표 면의 판만(R73b — 같은 평면의 다른 판까지 판 단추가 바꾸지 않게) · 첫 면의 판이 맨 뒤.
        var leadSet = chosen.Select(f => f.Siblings[0]).Distinct().Select(k => chosen.Last(f => ReferenceEquals(f.Siblings[0], k))).ToHashSet();
        var facePlates = chosen.Where(f => f.Piece >= 0 && leadSet.Contains(f)).Reverse().Select(f => f.Piece).Distinct().Reverse().ToList();
        plates.RemoveAll(i => facePlates.Contains(i) || dropped.Contains(i));
        _fwSel.Clear();
        foreach (int piece in plates.Concat(facePlates)) _fwSel.Add(_board.Pieces[piece].Lead);
        _fwPicked = null;                          // 면을 고르면 접은 표의 줄 고르기 · 자리 칸 · 문제 딱지로 고른 부재는 푼다
        _fwPickedSub = null;
        _fwPlaceOpen = false;
        _rvMembers.Clear();
        RefreshAll();
        if (chosen.Count == 0) { CancelFollow(); return; }
        // 일지(R73a3 — 다음 실제 실행에서 부재만 보기가 켜졌는지 · 잘라 그린 양 · 걸린 시간을 본다): 고르기 + 닿은 부재 + 자르기 + 겉면 갈아 끼우기까지.
        // R73a4: «이 부재만 보기» — 부재 · 빗금을 건 면 · 빠진 자리(갈래 · ㎡) · 빼지 않은 자리 · 고르기 + 겉면 갈아 끼우기 + 빗금까지.
        if (isolate && _fwIso != null && SoloOn)
        {
            _fwIsoLogged = true;
            Log?.Invoke($"② 부재 단독 보기\t부재 {string.Join(",", _fwIso.Order().Take(4))}{(_fwIso.Count > 4 ? " …" : "")}{(SoloWhole ? "(부재 줄)" : "")} · 면 {_fwCutFaces}" +
                        $" · 공제 위치 {_fwCutList.Count(c => c.Area >= FormworkBoard.JointChipMin)}({M2(_fwCutList.Sum(c => c.Area))}㎡) · 거푸집 추가 위치 {_fwCutList.Count(c => c.Kept >= FormworkBoard.JointChipMin)}" +
                        $" · 빗금 {_view3d.GuideCount(4) + _view3d.GuideCount(5)} · 딱지 {_view3d.LabelCount} · 판 {_fwIsoOwn.Count} · {watch.ElapsedMilliseconds} ms");
        }
        else if (isolate && _fwIso != null)
        {
            _fwIsoLogged = true;
            Log?.Invoke($"② 부재만 보기\t고른 부재 {_fwIso.Count}({string.Join(",", _fwIso.Take(4))}) · 닿은 {_fwIsoNear.Count(id => IndexOf(id) >= 0)} · 잘라 그린 세모 {_fwIsoClips.Sum(c => c.mesh.Triangles)}(조각 {_fwIsoClips.Count}) · {watch.ElapsedMilliseconds} ms");
        }
        SettleSplit();                             // 막 나눴으면(R73a5) 배치를 마쳐 왼쪽 3D 의 새 폭으로 날아갈 자리를 센다
        SelectMilliseconds = watch.ElapsedMilliseconds;
        RevealFace(chosen[^1]);
        bool alone = isolate && _fwIso != null;
        // 면 하나 = 한 면처럼 볼 면들 하나(R73a7) — 넘긴 면 · 고른 면.
        bool one = !whole && chosen.Select(f => f.Siblings[0]).Distinct().Count() == 1;
        var box = alone ? IsolateBox(!whole && picks == 1 ? chosen[^1] : null) : FacesBox(chosen.Where(f => FaceShown(f)));
        (double yaw, double pitch)? turn = null;    // 왼쪽이 날아간 방향(R73a6 — 부재 줄이면 오른쪽 «전체 모델»도 이 방향)
        if (fly && !box.IsEmpty)
        {
            // 부재만 보기(R73a2)는 그 무리를 화면에 맞춰(여백 조금) · 면 하나면 그 면을 보는 방향으로(R73a 와 같게).
            // R73a4 이 부재만 보기: 부재 줄은 거푸집 · 빗금이 가장 많이 보이는 쪽(SoloAngles) · 오른쪽 위 띠 · 오른쪽 아래 지도를 비운 안쪽에 맞춘다.
            // R73a6: 면 하나면 그 면이 보이는 첫 방향(녹촌 벽 개구부 둘레 56 · 계단 6곳은 FacingAngles 에서 그 부재 몸에 가려 안 보였다 — 전수 검사 2).
            var (yaw, pitch) = one ? SeeingAngles(chosen.OrderByDescending(f => f.Area).First(), SoloSees, _view3d.Camera.Yaw, _view3d.Camera.Pitch) : (_view3d.Camera.Yaw, _view3d.Camera.Pitch);
            if (SoloOn && whole) (yaw, pitch) = SoloAngles(yaw, pitch);
            if (SoloOn) _view3d.FlyTo(box, yaw, pitch, IsolateZoomMargin, RowZoomMinWidth, new Thickness(8, SoloInsetTop, 8, 8));
            else _view3d.FlyTo(box, yaw, pitch, alone ? IsolateZoomMargin : FaceZoomMargin, RowZoomMinWidth);
            turn = (yaw, pitch);
        }
        FrameWhole(turn);                          // 오른쪽 «전체 모델»을 고른 부재에 꽉 차게(R73a6 — 면이면 그 면 쪽으로 · 고른 것이 그대로면 그대로)
        FollowLater(chosen.Select(f => f.ElementId).Distinct(), box);   // Revit 에서도 그 부재(R61a)
    }

    // 날아가 볼 여백(한쪽 비율 — 면 크기 1.5 배를 둘레에 · 계획 §3.6) · 4 m 아래로는 좁히지 않는다(RowZoomMinWidth).
    private const double FaceZoomMargin = 0.75;
    private const double IsolateZoomMargin = 0.12;             // 부재만 보기(R73a2): 무리가 화면을 채우게 — 여백 조금
    private const double SoloInsetTop = 112;                   // 이 부재만 보기(R73a4): 위쪽 보기 단추 · 띠 아래부터 맞춘다(픽셀 — R73a6 띠가 두 줄)

    // 부재 줄로 고른 «이 부재만 보기»의 보는 방향(R73a4 · 내 판단 — 녹촌 천장 밑 보를 등각에서 누르면 밑면 거푸집 · 기둥 빗금이 뒤를 보아 안 보였다): 지금 방향 ·
    // 반대편 · 양옆 × 위 · 아래 가운데 거푸집 넓이 + 빗금 넓이 × 3 이 가장 많이 마주 보이는 쪽(면이 시선을 마주한 만큼 · 빗금은 면마다 한 번) — 지금 방향이 그 9할을 넘으면 그대로.
    private (double yaw, double pitch) SoloAngles(double yaw, double pitch)
    {
        if (_board == null || _fwIso == null) return (yaw, pitch);
        const double deg = Math.PI / 180;
        double tilt = Math.Abs(pitch) >= 15 * deg ? Math.Min(Math.Abs(pitch), 60 * deg) : 30 * deg;
        var faces = _fwIso.SelectMany(id => _board.FacesOfElement(id)).Where(f => f.Piece >= 0 && f.Face.Normal is { Length: 3 }).ToList();
        double Score(double y, double p)
        {
            var forward = new Vec3(-Math.Cos(p) * Math.Cos(y), -Math.Cos(p) * Math.Sin(y), -Math.Sin(p));
            double s = 0;
            foreach (var f in faces) s += Math.Max(0, -new Vec3(f.Face.Normal[0], f.Face.Normal[1], f.Face.Normal[2]).Dot(forward)) * f.CountedArea;
            foreach (var c in _fwCutByFace) s += 3 * Math.Max(0, -c.Normal.Dot(forward)) * c.Cuts.Sum(x => x.Area + x.Kept);
            return s;
        }
        var best = (yaw, pitch);
        double now = Score(yaw, pitch), most = now;
        for (int k = 0; k < 4; k++)
            foreach (double p in new[] { tilt, -tilt })
                if (Score(yaw + k * 90 * deg, p) is var s && s > most) (best, most) = ((yaw + k * 90 * deg, p), s);
        return now >= 0.9 * most ? (yaw, pitch) : best;
    }

    // 그 면이 보이는 첫 방향(R73a6): OrbitCamera.ViewCandidates 차례로 그 면 대표 점에 시선을 쏘아 맨 앞이 그 면이면 그 방향 · 없으면 FacingAngles.
    // sees = (그 면, 대표 점(장면 좌표), 시선 방향) → 그 그림에서 그 시선의 맨 앞이 그 면인가.
    private (double yaw, double pitch) SeeingAngles(BoardFace face, Func<BoardFace, Vec3, Vec3, bool> sees, double yaw, double pitch)
    {
        var first = OrbitCamera.FacingAngles(face.Face.Normal, yaw, pitch);
        if (_board == null || face.Patches.Where(p => p.Triangles.Count >= 3).OrderByDescending(p => p.Area).FirstOrDefault() is not { } patch) return first;
        var anchor = FormworkBoard.AnchorOf(patch) - _board.Origin;
        var n = face.Face.Normal is { Length: 3 } nn ? new Vec3(nn[0], nn[1], nn[2]) : Vec3.Zero;
        foreach (var (y, p) in OrbitCamera.ViewCandidates(face.Face.Normal, yaw, pitch))
        {
            var dir = new Vec3(-Math.Cos(p) * Math.Cos(y), -Math.Cos(p) * Math.Sin(y), -Math.Sin(p));
            if (n.Length > 0.5 && n.Dot(dir) > -0.05) continue;
            if (sees(face, anchor, dir)) return (y, p);
        }
        return first;
    }

    // 왼쪽 그림에서 그 시선의 맨 앞이 그 면인가(판 · 고른 면 겉면 · 부재 몸 위의 그 면 자리).
    private bool SoloSees(BoardFace face, Vec3 anchor, Vec3 dir)
    {
        var (index, triangle, distance) = _view3d.HitRay(anchor - dir * 1000, dir);
        return ReferenceEquals(SoloFaceOf(index, triangle, anchor - dir * 1000 + dir.Unit() * distance, dir), face);
    }

    // 왼쪽 그림의 그 차례 · 세모 · 닿은 점의 면(판 → 그 세모의 면 · 고른 면 겉면 → 그 면 · 부재 몸 → 그 점의 면).
    private BoardFace? SoloFaceOf(int index, int triangle, Vec3 point, Vec3 dir)
    {
        if (_board == null || index < 0) return null;
        if (index >= MemberCount && index < OverlayStart) return _board.FaceAt(index - MemberCount, triangle) ?? _board.FaceOf(_board.Pieces[index - MemberCount].Lead);
        if (index >= FaceOverlayStart && index - FaceOverlayStart < _fwFaceOverlay.Count) return _fwFaceOverlay[index - FaceOverlayStart].face;
        if (index < MemberCount) return _board.FaceAtPoint(_members[index].Id, point + _board.Origin, dir) is { } f && (f.Covered || !f.Fixed) ? f : null;
        return null;
    }

    // 그림의 화면 자리에서 부재 몸 위의 면(R73a6 — 다 덮인 면 · 판의 구멍 자리 · 형상이 정해 세지 않는 면(윗면)은 없음).
    private BoardFace? BodyFace(Model3DView view, int member, Point at)
    {
        if (_board == null || member < 0 || member >= MemberCount || member >= view.MeshCount) return null;
        var (origin, direction) = view.Camera.Ray(at.X, at.Y);
        var hit = Picker.PickHit(new[] { view.MeshAt(member) }, _ => true, origin, direction);
        if (hit.index < 0) return null;
        var found = _board.FaceAtPoint(_members[member].Id, origin + direction.Unit() * hit.distance + _board.Origin, direction);
        return found is { } f && (f.Covered || !f.Fixed) ? f : null;
    }

    // 면들을 싼 상자(장면 좌표).
    private Box3 FacesBox(IEnumerable<BoardFace> faces)
    {
        var box = Box3.Empty;
        if (_board == null) return box;
        foreach (var face in faces)
            foreach (var p in face.Patches)
            {
                foreach (var q in p.Triangles) box = box.With(new Vec3(q[0], q[1], q[2]) - _board.Origin);
                foreach (var q in p.Outline.SelectMany(r => r).Where(q => q.Length >= 3)) box = box.With(new Vec3(q[0], q[1], q[2]) - _board.Origin);
            }
        return box;
    }

    // 그 면이 지금 보이는가(종류 · 층 거름 · 숨긴 부재 — 판이 아니면 부재로). «이 부재만 보기»(R73a4)는 고른 부재의 면만(이은 판의 다른 부재 면은 그 몫을 빼고 그린다).
    private bool FaceShown(BoardFace face) =>
        SoloOn ? SoloShows(face.ElementId)
        : face.Piece >= 0 ? FormworkShown(MemberCount + face.Piece) : _editor.Find(face.ElementId) is not { } m || _editor.InView(m);

    // 3D 에서 판을 찍었다: 그 세모의 면(찍은 자리를 모르면 그 판의 대표 면).
    private BoardFace? FaceAtClick(int piece, int triangle)
    {
        if (_board == null || piece < 0 || piece >= _board.Pieces.Count) return null;
        return _board.FaceAt(piece, triangle) ?? _board.FaceOf(_board.Pieces[piece].Lead);
    }

    // 고른 면을 다시 잡을 열쇠(UpdateFormworkLook 이 고른 면을 정리할 때마다).
    private void KeepFaceKeys()
    {
        _fwFaceKeys = new List<(DropRecord, int, int)>();
        if (_board == null) return;
        foreach (var anchor in _fwFaces)
            if (_board.FaceOf(anchor) is { } face) _fwFaceKeys.Add((DropRecord.Of(face.Face, face.Lead), face.Face.Solid, face.Face.Face));
    }

    // 형상을 다시 잰 뒤: 면 · 대표 점으로 고른 면을 다시 잡는다(SyncMeasured).
    private void SyncMeasuredFaces()
    {
        if (_board == null) return;
        bool whole = SoloWhole;                    // 부재 줄로 고른 «이 부재만 보기»는 다시 잰 뒤에도 부재 통째(R73a5 — [넣기] · [빼기]가 형상을 다시 잴 때마다 면 테두리가 생겼다)
        _fwFaces.Clear();
        foreach (var (record, solid, face) in _fwFaceKeys)
            if (_board.FaceNear(record, solid, face) is { } found)
            {
                // 한 면처럼 볼 면들(R73a7)은 무리 모두(다시 잰 뒤 무리가 늘었을 수 있다 — 설치 전 검토 중간) · 찾은 면은 그 무리의 맨 뒤(첫 면 그대로).
                foreach (var s in found.Siblings.Where(s => !ReferenceEquals(s, found)))
                    if (!_fwFaces.Contains(s.Lead)) _fwFaces.Add(s.Lead);
                _fwFaces.Remove(found.Lead);
                _fwFaces.Add(found.Lead);
            }
        if (whole) _fwSoloWhole = _fwFaces.ToList();
        _fwFaceMeshes.Clear();
        _fwHoverFace = null;
        _view3d.SetHoverOutline(null);
    }

    // ── 마우스 밑(R73a) ──

    // 마우스가 움직였다(② ): 그 밑의 면이 바뀌었으면 굵은 테두리를 다시 짓고 이름표를 고친다. 부재 · 줄 몫이면 전의 글.
    private void HoverFormwork(int index, int triangle)
    {
        if (_board == null) return;
        if (_fwTool != CutTool.None) { HoverTool(); return; }   // [빼기] · [넣기]를 든 동안은 닿은 자리(R73a5) · 붓(R73b)
        var face = HitFace(index, triangle, _view3d.HoverPoint);
        if (!ReferenceEquals(face, _fwHoverFace))
        {
            _fwHoverFace = face;
            _view3d.SetHoverOutline(face != null ? FaceMesh(face) : null);
        }
        _hover.Text = index < 0 ? " " : face != null ? FaceText(face) : DescribeFormwork(index);
        _hoverPill.Visibility = index < 0 || _hover.Text.Trim().Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    // 왼쪽 그림의 찍힌 차례 · 세모의 면(판 → 그 세모의 면 · 고른 면 겉면 → 그 면 · 부재 몸 → 그 자리의 면 — R73a6 다 덮인 면 · 판의 구멍 자리). 마우스 밑 · 붓이 같은 길.
    private BoardFace? HitFace(int index, int triangle, Point? at)
    {
        if (_board == null) return null;
        if (index >= MemberCount && index < OverlayStart) return _board.FaceAt(index - MemberCount, triangle) ?? _board.FaceOf(_board.Pieces[index - MemberCount].Lead);
        if (index >= FaceOverlayStart && index - FaceOverlayStart < _fwFaceOverlay.Count) return _fwFaceOverlay[index - FaceOverlayStart].face;
        if (index >= 0 && index < MemberCount && at is { } p) return BodyFace(_view3d, index, p);
        return null;
    }

    /// <summary>면 한 줄(마우스 밑 이름표 · 시험): «부위 마크 · 면 이름 · ㎡ · 지금: 종류».</summary>
    public string FaceText(BoardFace face) =>
        face.Siblings.All(f => f.Covered) ? $"{MemberLabel(face.Result.Element)} · {face.Name} · 접합 {LocationTree.AreaText(face.Siblings.Sum(f => f.CoveredArea))} ㎡ · 현재: 전부 공제"   // R73a6 다 덮인 면(거푸집 없음)
        : $"{MemberLabel(face.Result.Element)} · {face.Name} · {LocationTree.AreaText(ShownArea(face))} ㎡ · 현재: {FaceKind(face)}";

    // 면 이름표의 넓이(R73b — 설치 전 검토 중간): 세는 조각이 있으면 세는 넓이(나무 줄과 같음) · 없으면 면 넓이.
    private static double ShownArea(BoardFace face) => face.Siblings.Any(f => f.Counted) ? face.Siblings.Sum(f => f.CountedArea) : face.JoinedArea;   // R73a6 «지금» → «현재» · 0.005 ㎡ 아래는 셋째 자리 · R73a7 한 면처럼 볼 면들의 넓이

    // 부재 이름(나무 · 카드 머리와 같은 말 — LocationTree.MemberName).
    private string MemberLabel(FormworkElement el) => _board != null ? LocationTree.MemberName(_board, el) : el.Type;

    // 면의 지금 종류(나무 · 이름표): 지운 판 · 확인 필요 · 종류(유로폼은 높이 구간) · 세지 않음.
    // R73b: 한 면처럼 볼 면들(Siblings — 판이 갈린 한 평면) 모두 — 종류가 여럿이면 넓은 것 «외 n»(«유로폼 외 1»).
    private string FaceKind(BoardFace face)
    {
        var group = face.Siblings;
        if (group.All(f => f.Covered)) return "전부 공제";
        var patches = group.SelectMany(f => f.Patches).ToList();
        var kinds = patches.Where(p => p.Counted).GroupBy(KindText).OrderByDescending(g => g.Sum(p => p.Area)).Select(g => g.Key).ToList();
        if (patches.Any(p => p.DropLead.Length > 0)) kinds.Add("지운 판");
        if (kinds.Count == 0) return KindText(patches.OrderByDescending(p => p.Area).FirstOrDefault() ?? face.Lead);
        return kinds.Count == 1 ? kinds[0] : $"{kinds[0]} 외 {kinds.Count - 1}";
    }

    // 면의 색(나무의 점): 3D 의 판 색과 같다(R73b — 한 면처럼 볼 면들 가운데 가장 넓은 세는 조각).
    private (byte r, byte g, byte b) FaceColor(BoardFace face)
    {
        var patches = face.Siblings.SelectMany(f => f.Patches).ToList();
        var lead = patches.Where(p => p.Counted).OrderByDescending(p => p.Area).FirstOrDefault();
        if (lead == null && patches.Any(p => p.DropLead.Length > 0)) return FormworkPalette.Dropped;
        lead ??= patches.OrderByDescending(p => p.Area).FirstOrDefault() ?? face.Lead;
        return lead.Counted || lead.NeedsCheck ? FormworkPalette.Of(lead) : (ZonePalette.Todo.r, ZonePalette.Todo.g, ZonePalette.Todo.b);
    }

    // ── 고른 면 위의 이름 + 치수 · 평면 지도(R73a) ──

    // 첫 면 위에 «부위 · 면 · ㎡» + 가로 · 세로(면 평면 — 둘째 자리 · 반 넘게 못 채우면 «≈») · 그 네모를 회색 점선으로 · 판 모양 검사의 빨간 선과 함께. 지도도.
    // R73a4 «이 부재만 보기»: 빗금은 그 부재의 모든 면 + 면 · 이웃마다 이름 딱지(뒤를 보는 면은 화면이 숨김) · 부재 줄이면 면 하나의 이름 · 치수는 없고 지도의 점은 부재 가운데.
    private void ShowFaceMarks()
    {
        if (Step != 1 || _board == null) return;
        bool whole = SoloWhole;
        var face = PrimaryFace;
        if (face != null && !FaceShown(face)) face = null;   // 종류 · 층 거름이나 숨기기로 가린 면은 딱지 · 지도도 걷는다(R73a 검토 낮음 3)
        var named = whole ? null : face;
        string key = (whole ? "whole|" + string.Join(",", _fwIso!.Order()) : "") + (face == null ? "" : $"{face.Index}|{face.Name}|{M2(face.Area)}|{_board.MeasureVersion}") + "#" + _fwCheckKey + "#" + _fwCutKey;
        bool wholeStale = _locatorShown && _locator != null && _wholeMarksFor != key;   // 오른쪽 «전체 모델»을 막 켰다(R73a6)
        if (key == _fwMarksFor && !wholeStale) return;
        _fwMarksFor = key;
        var guides = new List<(IReadOnlyList<Vec3>, bool, int)>(_fwCheckGuides);
        var guideFacing = guides.Select(_ => Vec3.Zero).ToList();
        foreach (var (guide, facing) in CutGuides(_board.Origin))   // 빠진 자리 빨간 빗금(R73a2 — 고른 면 · 없으면 고른 판의 면) · 넣은 자리 청록(R73a4)
        {
            guides.Add(guide);
            guideFacing.Add(facing);
        }
        var labels = new List<(Vec3, string, Vec3, Vec3)>();
        var dims = new List<(Vec3, string, Vec3, Vec3)>();
        var labelFacing = new List<Vec3>();
        int above = 0;
        if (named != null)
        {
            face = named;
            var origin = _board.Origin;
            var group = face.Siblings;                // 한 면처럼 볼 면들(R73a7 — 단마다 따로인 계단 옆면: 이름 · 치수 · ㎡ 하나)
            var box = FormworkBoard.BoxOf(group);
            var n = face.Face.Normal is { Length: 3 } nn ? new Vec3(nn[0], nn[1], nn[2]).Unit() : Vec3.Zero;
            var lift = n * 0.02;
            var center = face.Center - origin + lift;
            string part = _board.PlaceOf(face.ElementId).part;
            double coveredArea = group.Sum(f => f.CoveredArea);
            bool allCovered = group.All(f => f.Covered);   // R73b: 무리 모두가 다 덮였을 때만 «접합»
            string name = $"{(part.Length > 0 ? part + " · " : "")}{face.Name} · {(allCovered ? "접합 " + LocationTree.AreaText(coveredArea) : LocationTree.AreaText(ShownArea(face)))}㎡";
            if (box == null) labels.Add((center, name, center, center));
            else
            {
                string near = (allCovered ? coveredArea / Math.Max(1e-9, box.Width * box.Height) : box.Fill) < 0.5 ? "≈" : "";   // 다 덮인 면은 닿은 넓이로(R73a6)
                var c = box.Corners.Select(q => q - origin + lift).ToList();
                // 이름은 윗변 바로 위(R73a2 — 가운데 두면 작은 면 · 빠진 자리 빗금을 덮었다: 녹촌 트렌치 0.17 ㎡ 면). 면 하나를 골랐고 빗금이 윗변 위로 나가면 빗금 위에
                // (이 부재만 보기는 그 면의 빗금만 — 부재의 다른 면 빗금까지 보면 이름이 멀리 뜬다).
                var top = (c[2] + c[3]) * 0.5;
                var rings = SoloOn ? _fwCutByFace.Where(f => group.Any(g => ReferenceEquals(f.Face, g.Face))).SelectMany(f => f.Cuts) : _fwCutList;
                if (FocusFaces().Select(f => f.Siblings[0]).Distinct().Count() == 1)
                    top += box.V * Math.Max(0, rings.SelectMany(x => x.Rings.Concat(x.KeptRings)).SelectMany(r => r).Select(q => (q - origin + lift - top).Dot(box.V)).DefaultIfEmpty(0).Max());
                labels.Add((top, name, top, top));
                // 가로 = 아랫변 · 세로 = 오른쪽 변(딱지는 변의 가운데 — 마우스를 올리면 그 변이 주황) — 빗금 이름 딱지 뒤에 단다(아래).
                dims.Add(((c[0] + c[1]) * 0.5 - box.V * 0.12, near + FormulaDecomposer.SheetText(box.Width), c[0], c[1]));
                dims.Add(((c[1] + c[2]) * 0.5 + box.U * 0.12, near + FormulaDecomposer.SheetText(box.Height), c[1], c[2]));
                guides.Add((c, true, 2));
                guideFacing.Add(Vec3.Zero);
                above = 1;
            }
            labelFacing.AddRange(labels.Select(_ => Vec3.Zero));
        }
        // 빗금 이름 딱지(R73a4 — 면 이름 뒤 · 치수 앞: 겹치면 치수가 숨는다 — 닿은 이웃이 «이 부재만 보기»의 요점). 이름처럼 그 자리 바로 위에 띄운다
        // (가운데에 두면 녹촌 트렌치의 «계단 0.03㎡» 빗금을 딱지가 덮었다).
        int cuts = 0, cutStart = labels.Count;
        var marks = new List<(FormworkBoard.CutMark mark, Vec3 normal)>();
        foreach (var (label, facing, mark) in CutLabels(_board.Origin))
        {
            labels.Add(label);
            labelFacing.Add(facing);
            marks.Add((mark, facing));
            cuts++;
        }
        if (cuts > 0) above = labels.Count;
        labels.AddRange(dims);
        labelFacing.AddRange(dims.Select(_ => Vec3.Zero));
        // R73a7: 빗금 딱지를 누르면 그 이웃의 맞닿은 면으로(ClickCutMark) — 왼쪽 · 오른쪽 같은 길.
        _fwMarkLabels = (cutStart, marks);
        bool Clickable(int i) => i >= cutStart && i - cutStart < marks.Count && marks[i - cutStart].mark.Others is { Count: > 0 };
        _view3d.SetLabels(labels, above, labelFacing, nudge: true, clickable: Clickable);   // 닿은 자리 이름이 겹치면 비켜 놓는다(R73a5 — 치수가 먼저 빠진다)
        _view3d.SetGuides(guides, guideFacing);
        // R73a6: 오른쪽 «전체 모델»에도 같은 공제 위치 빗금 + 이웃 이름 딱지(덩이마다) — 면 이름 · 치수 · 판 모양 검사 선은 왼쪽만.
        if (_locatorShown && _locator != null)
        {
            _wholeMarksFor = key;
            var cutGuides = CutGuides(_board.Origin).ToList();
            _locator.SetGuides(cutGuides.Select(g => g.guide).ToList(), cutGuides.Select(g => g.facing).ToList());
            var cutLabels = CutLabels(_board.Origin).ToList();
            _locator.SetLabels(cutLabels.Select(l => l.label).ToList(), cutLabels.Count, cutLabels.Select(l => l.facing).ToList(), nudge: true,
                               clickable: i => i < cutLabels.Count && cutLabels[i].mark.Others is { Count: > 0 });
        }
    }

    // 지금 왼쪽 그림의 빗금 딱지(R73a7 — 딱지 차례의 시작 · 덩이와 그 면의 법선). 오른쪽 «전체 모델»은 같은 덩이를 0 부터.
    private (int start, List<(FormworkBoard.CutMark mark, Vec3 normal)> marks) _fwMarkLabels = (0, new());

    // 그림의 딱지를 눌렀다(R73a7 — Model3DView.LabelClick): 빗금 딱지면 그 이웃으로(ClickCutMark) · 아니면 받지 않음(그 밑 찍기).
    private bool ClickMarkLabel(int index, bool ctrl, bool whole)
    {
        int at = whole ? index : index - _fwMarkLabels.start;
        if (at < 0 || at >= _fwMarkLabels.marks.Count) return false;
        var (mark, normal) = _fwMarkLabels.marks[at];
        return ClickCutMark(mark, normal, ctrl, whole ? "전체 화면" : "단독 보기");
    }

    // ── 나무 ──

    // 왼쪽 판(R73a): [자동 | 사용자] · 거름 · 나무 · 맨 아래 접은 «유형 · 부위별로 한꺼번에»(사용자 모드).
    private FrameworkElement BuildTreePane()
    {
        var pane = new DockPanel();
        var filter = BuildTreeFilter();
        DockPanel.SetDock(filter, Dock.Top);
        pane.Children.Add(filter);
        var fold = BuildRuleFold();
        DockPanel.SetDock(fold, Dock.Bottom);
        pane.Children.Add(fold);
        _fwTreeList = new TreeList { Build = BuildTreeRow, Focusable = false };
        _ui.Mark(_fwTreeList, "", "fw.tree");
        _fwTreeEmpty.Foreground = _sub;
        var host = new Grid();
        host.Children.Add(_fwTreeList);
        host.Children.Add(_fwTreeEmpty);           // 거른 나무가 비었을 때 한 줄
        host.Children.Add(BuildTreeKindLayer());   // 줄 끝 종류 글을 누르면 뜨는 종류 다섯(R73b)
        pane.Children.Add(host);
        return pane;
    }

    private readonly TextBlock _fwTreeEmpty = new() { FontSize = 12, Margin = new Thickness(6, 6, 0, 0), Visibility = Visibility.Collapsed, IsHitTestVisible = false };

    // [전체 | 확인할 자리 n | 안 셈 n](R73a — 판 목록의 «확인할 자리» · «세지 않는 면» · «지운 판» 묶음 대신).
    private FrameworkElement BuildTreeFilter()
    {
        var tips = new[]
        {
            "거푸집 산출 대상 면 전체(상부면 · 지면 위 하부면 제외).",
            "검토 필요 — 하부가 빈 하부면 · 내외측을 정하지 못한 면 · 하부 콘크리트와 너무 가까운 하부면.",
            "산출 제외 — 지운 판 · 산출 제외로 정한 면.",
        };
        var names = new[] { "전체", "검토 필요", "산출 제외" };   // R73a6 전문 용어(전 «확인할 자리 · 안 셈»)
        for (int k = 0; k < 3; k++)
        {
            int which = k;
            var segment = _ui.Segment(names[k], tips[k], "fw.filter." + k);
            segment.IsChecked = k == _fwTreeFilter;
            segment.Checked += (_, _) => SetTreeFilter(which);
            _fwFilters[k] = segment;
        }
        var bar = _ui.Segments(_fwFilters!);
        bar.HorizontalAlignment = HorizontalAlignment.Left;
        bar.Margin = new Thickness(2, 0, 0, 6);
        return bar;
    }

    private void SetTreeFilter(int filter)
    {
        if (filter == _fwTreeFilter) return;
        _fwTreeFilter = filter;
        _fwTreeFaces = null;                       // 거른 나무를 새로
        RefreshTree();
        if (PrimaryFace is { } face) RevealFace(face);
    }

    // 거름에 드는 면.
    private bool InFilter(BoardFace face) => _fwTreeFilter switch
    {
        1 => face.NeedsCheck,
        2 => !face.NeedsCheck && !face.Counted,
        _ => true,
    };

    // 나무를 지금 면 목록으로(면 목록이 바뀌었을 때만 다시 짓는다) · 고른 면의 줄 바탕.
    private void RefreshTree()
    {
        if (_board == null || _fwTreeList == null) return;
        var faces = _board.Faces;
        if (!ReferenceEquals(faces, _fwTreeFaces))
        {
            var watch = Stopwatch.StartNew();
            bool first = _fwTreeFaces == null && _fwTree == null;
            _fwTreeFaces = faces;
            _fwTree = LocationTree.Build(_board, _editor.Floors, InFilter, ZoneRank);
            int checks = faces.Count(f => !f.Fixed && f.NeedsCheck), uncounted = faces.Count(f => !f.Fixed && !f.NeedsCheck && !f.Counted);
            if (_fwFilters[1] != null) _fwFilters[1]!.Content = $"검토 필요 {checks}";
            if (_fwFilters[2] != null) _fwFilters[2]!.Content = $"산출 제외 {uncounted}";
            FlattenTree();
            TreeMilliseconds = watch.ElapsedMilliseconds;
            if (first) Log?.Invoke($"② 위치 나무\t면 {faces.Count}(나무 {faces.Count(f => !f.Fixed)}) · 검토 필요 {checks} · 산출 제외 {uncounted} · 면 목록 {_board.FacesMilliseconds} ms · 나무 {_fwTree.Milliseconds} ms · 줄 {_fwTreeRows.Count} · 짓기 {TreeMilliseconds} ms");
            return;
        }
        MarkTreeRows();
    }

    /// <summary>시험 · 일지용: 마지막으로 나무를 지은 시간(ms — 면 목록 · 나무 · 펼친 줄).</summary>
    public long TreeMilliseconds { get; private set; }

    private int ZoneRank(string zone)
    {
        int at = _editor.Places.FindIndex(p => p.Name == zone);
        return at < 0 ? int.MaxValue - 1 : at;
    }

    // 처음 펼침: 층 · 중분류는 펼침 · 부재는 접음(거른 나무 — 확인할 자리 · 안 셈 — 는 부재도 펼침). 사람이 바꾼 줄은 거름마다 기억한다.
    private bool IsOpen(LocationTree.Node node)
    {
        bool open = node.Level != LocationTree.Level.Member || _fwTreeFilter != 0;
        return _fwTreeToggled[_fwTreeFilter].Contains(node.Key) ? !open : open;
    }

    private void SetOpen(LocationTree.Node node, bool open)
    {
        if (IsOpen(node) == open) return;
        var toggled = _fwTreeToggled[_fwTreeFilter];
        if (!toggled.Remove(node.Key)) toggled.Add(node.Key);
    }

    // 펼친 줄만 목록에(가상화 목록이 보이는 줄만 짓는다). 굴린 자리는 그대로.
    private void FlattenTree()
    {
        if (_fwTreeList == null) return;
        var rows = new List<LocationTree.Node>();
        void Walk(LocationTree.Node node)
        {
            rows.Add(node);
            if (node.Level != LocationTree.Level.Face && IsOpen(node))
                foreach (var child in node.Children) Walk(child);
        }
        foreach (var root in _fwTree?.Roots ?? Array.Empty<LocationTree.Node>()) Walk(root);
        double offset = _fwTreeList.Offset;
        _fwTreeRows = rows;
        _fwTreeList.ItemsSource = rows;
        _fwTreeList.Offset = offset;
        _fwTreeEmpty.Text = _fwTreeFilter == 0 ? "거푸집 산출 대상 면이 없습니다." : _fwTreeFilter == 1 ? "검토 필요 면이 없습니다." : "산출 제외 면이 없습니다.";
        _fwTreeEmpty.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    // 고른 면의 줄 · 마우스로 펼친 줄의 꺾쇠(지어진 줄만).
    private void MarkTreeRows()
    {
        if (_fwTreeList == null) return;
        var chosen = FocusFaces().ToHashSet();
        foreach (var (node, row) in _fwTreeList.Realized)
            row.Background = node.Face is { } f && chosen.Contains(f) ? _ui["AccentSoft"] : Brushes.Transparent;
    }

    // 줄 하나: [꺾쇠][색 점][이름][종류 ●][㎡] — 들여쓰기 = 깊이.
    private FrameworkElement BuildTreeRow(LocationTree.Node node)
    {
        bool face = node.Level == LocationTree.Level.Face;
        var grid = new Grid();
        for (int c = 0; c < 5; c++)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = c == 2 ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });
        var chevron = new TextBlock
        {
            Text = face ? "" : IsOpen(node) ? "▾" : "▸", Width = 14, FontSize = 11, Foreground = _sub, Background = Brushes.Transparent,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(node.Depth * TreeIndent, 0, 2, 0),
        };
        if (!face) chevron.MouseLeftButtonUp += (_, e) => { e.Handled = true; ToggleTree(node); };
        grid.Children.Add(chevron);
        if (node.Face is { } f)
        {
            var c = FaceColor(f);
            var dot = Swatch(c.r, c.g, c.b, 6);
            dot.Width = dot.Height = 10;
            Grid.SetColumn(dot, 1);
            grid.Children.Add(dot);
        }
        var name = new TextBlock
        {
            Text = node.Name, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
            FontSize = node.Level switch { LocationTree.Level.Floor => 12.5, LocationTree.Level.Face => 12.5, _ => 13 },
            FontWeight = node.Level is LocationTree.Level.Floor or LocationTree.Level.Zone ? FontWeights.SemiBold : FontWeights.Normal,
            Foreground = node.Level == LocationTree.Level.Floor ? _sub : _text,
        };
        Grid.SetColumn(name, 2);
        grid.Children.Add(name);
        if (node.Face is { } g)
        {
            var kind = new TextBlock { FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0), Background = Brushes.Transparent };
            // 종류 글을 누르면 종류 다섯 작은 판(R73b — 계획 §3.4 · 줄 이름은 고르기 · 날기만): 칠할 수 있는 면만.
            if (FormworkBoard.Paintable(g))
            {
                kind.Cursor = Cursors.Hand;
                _ui.Mark(kind, "누르면 이 면의 거푸집 종류를 고릅니다", "fw.tree.kind." + node.Key);
                kind.MouseLeftButtonUp += (_, e) => { e.Handled = true; OpenKindPanel(node, kind); };
            }
            kind.Inlines.Add(new Run(FaceKind(g)) { Foreground = g.Siblings.Any(x => x.NeedsCheck) ? _warn : _sub });
            if (g.Siblings.Any(x => x.Direct)) kind.Inlines.Add(new Run(" ●") { Foreground = _ui["AccentText"] });
            Grid.SetColumn(kind, 3);
            grid.Children.Add(kind);
        }
        // 세지 않는 면은 0.00 대신 면 넓이를 옅게 · 가운데 줄(JACK 1030 첫 확인 전 — «0.00»은 무엇을 뺐는지 안 보임).
        bool off = node.Face != null && node.Faces.All(x => !x.Counted);   // R73b: 줄의 면 모두(한 면처럼 볼 면들)
        var area = new TextBlock
        {
            Text = LocationTree.AreaText(off ? node.Faces.Sum(x => x.Area) : node.Area), FontSize = 12, Foreground = face && !off ? _text : _sub, MinWidth = 58, TextAlignment = TextAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 2, 0), TextDecorations = off ? TextDecorations.Strikethrough : null,
        };
        Grid.SetColumn(area, 4);
        grid.Children.Add(area);
        bool picked = node.Face is { } h && FocusFaces().Contains(h);
        var row = new Border
        {
            Child = grid, CornerRadius = new CornerRadius(6), Padding = new Thickness(4, 2, 4, 2), Margin = new Thickness(0, 0, 0, 1), MinHeight = 24,
            Background = picked ? _ui["AccentSoft"] : Brushes.Transparent, Cursor = Cursors.Hand,
        };
        string tip = node.Tip;
        if (node.Face is { } t) tip = string.Join(" · ", new[] { FaceText(t), node.Tip }.Where(s => s.Length > 0));
        _ui.Mark(row, tip, "fw.tree." + node.Key);
        row.MouseLeftButtonUp += (_, e) => { e.Handled = true; ClickTreeRow(node, (Keyboard.Modifiers & ModifierKeys.Control) != 0); };
        return row;
    }

    /// <summary>
    /// 나무 줄로 고르면 «부재만 보기»(R73a2 · R73a3 — 그 부재 + 닿은 부재의 근처 · 나머지 흐린 윤곽)를 켜는가. R73a4: 끔 — JACK 1102 «일단 닿는 객체만 보이게 하는 건
    /// 없던 걸로 하자 · 이게 더 헷갈린다»(스샷 1102 09:40 — 큰 벽 판 · 빗금 · 흐린 윤곽이 겹쳐 읽히지 않음). «일단»이라 장치는 두고 끈다(하네스는 켜서 장치만 따로 본다).
    /// </summary>
    public static bool IsolateOnTreeClick { get; set; }

    // 줄을 눌렀다(붓을 들고 있어도 칠하지 않는다 — JACK 1030 정함 · R73b): 면 = 고르고 날아감 · 부재 = 그 부재 면 모두(펼침) · 층 · 중분류 = 펼치기.
    // 면 · 부재 줄 = «이 부재만 보기»(R73a4 — IsolateOnTreeClick 이면 R73a2 · R73a3 의 닿은 부재 보기).
    private void ClickTreeRow(LocationTree.Node node, bool ctrl)
    {
        if (_board == null || Busy) return;
        CloseKindPanel();
        // 숨긴(Del · ① 숨기기) 부재의 줄을 누르면 그 부재는 다시 보인다(R73a3 — 보이지 않는 것으로 날아가 «아무 일 없음»이 되지 않게).
        if (node.Level is LocationTree.Level.Face or LocationTree.Level.Member && _editor.Hidden.Count > 0)
            _editor.Hidden.ExceptWith(node.Level == LocationTree.Level.Face ? new[] { node.Face!.ElementId } : node.Faces.Select(f => f.ElementId));
        switch (node.Level)
        {
            case LocationTree.Level.Face:
                SelectFaces(new[] { node.Face! }, ctrl ? SelectMode.Toggle : SelectMode.Replace, fly: true, isolate: true);
                break;
            case LocationTree.Level.Member:
                if (!IsOpen(node)) { SetOpen(node, true); FlattenTree(); }
                SelectFaces(node.Faces, ctrl ? SelectMode.Add : SelectMode.Replace, fly: true, isolate: true, whole: true);
                break;
            default:
                ToggleTree(node);
                break;
        }
    }

    private void ToggleTree(LocationTree.Node node)
    {
        SetOpen(node, !IsOpen(node));
        FlattenTree();
    }

    // 나무를 그 면의 줄까지 펼치고 굴린다(3D 에서 고른 면 — 거름에 들지 않으면 그대로).
    private void RevealFace(BoardFace face)
    {
        if (_fwTree?.NodeOf(face) is not { } node || _fwTreeList == null) return;
        bool changed = false;
        foreach (var up in _fwTree.PathTo(node))
            if (!IsOpen(up)) { SetOpen(up, true); changed = true; }
        if (changed) FlattenTree();
        int at = _fwTreeRows.IndexOf(node);
        if (at >= 0) _fwTreeList.Show(at);
        MarkTreeRows();
    }

    // ── 부재만 보기(R73a2) ──

    // 닿은 부재 · 판을 지금 판으로 다시 센다(UpdateFormworkLook — 판을 다시 지으면 덩이 차례가 바뀐다). 고른 것이 무리 밖으로 나가면(딱지 · 표 줄 · 끌기 ·
    // 문제 딱지 · ③) 모두 보기로 — 보이지 않는 것을 고른 채 두지 않게.
    private void IsolateSets()
    {
        _fwIsoNear.Clear();
        _fwIsoOwn.Clear();
        _fwIsoNearPieces.Clear();
        if (_fwIso == null) return;
        if (_board == null || Step != 1 || _rvMembers.Count > 0 || _fwFocus != null || _fwPicked != null) { _fwIso = null; return; }
        if (!_fwIsoSolo)                                   // «이 부재만 보기»(R73a4)는 닿은 부재를 세지 않는다
        {
            foreach (long id in _fwIso) _fwIsoNear.UnionWith(_board.TouchingOf(id));
            _fwIsoNear.ExceptWith(_fwIso);
        }
        for (int i = 0; i < _board.Pieces.Count; i++)
        {
            var members = _board.Pieces[i].Members;
            if (members.Any(m => _fwIso.Contains(m.Result.Element.Id))) _fwIsoOwn.Add(i);
            else if (members.Any(m => _fwIsoNear.Contains(m.Result.Element.Id))) _fwIsoNearPieces.Add(i);
        }
        bool outside = _fwPickedSet.Any(i => !_fwIsoOwn.Contains(i) && !_fwIsoNearPieces.Contains(i))
                       || FocusFaces().Any(f => !_fwIso.Contains(f.ElementId) && !_fwIsoNear.Contains(f.ElementId));
        if (!outside) return;
        _fwIso = null;
        _fwIsoNear.Clear();
        _fwIsoOwn.Clear();
        _fwIsoNearPieces.Clear();
    }

    // 진하게 그리는 그림 차례인가(부재 · 덩이 — 부재만 보기가 아니면 모두). R73a3: 고른 부재 · 그 판만 — 닿은 부재 · 닿은 부재만의 판은 흐린 윤곽 + 잘라 그린 몫(_fwIsoClips).
    private bool InIsolate(int index) =>
        _fwIso == null || (index < MemberCount ? _fwIso.Contains(_members[index].Id)
                           : index < OverlayStart ? _fwIsoOwn.Contains(index - MemberCount)
                           : true);

    // 닿은 부재를 잘라 볼 상자(R73a3): 고른 부재 상자마다 MeshClip.Near(0.3 m)만큼 키운 것.
    private List<Box3> IsolateClipBoxes()
    {
        var boxes = new List<Box3>();
        foreach (long id in (_fwIso ?? new HashSet<long>()).Order())
            if (IndexOf(id) is int i && i >= 0) boxes.Add(_memberMeshes[i].Box);
        return MeshClip.NearBoxes(boxes);
    }

    // 부재만 보기로 날아갈 상자(내 판단 — 큰 이웃 · 긴 부재 때문에 고른 것이 점이 되지 않게): 부재 줄 = 고른 부재 + 그 둘레(가장 긴 변의 반 · 3 m 아래로는 3 m)의 닿은 부재 ·
    // 면 줄(around) = 그 면 + 그 둘레(면의 가장 긴 변 · 2 m 아래로는 2 m)의 고른 부재 · 닿은 부재(녹촌 트렌치 0.17 ㎡ 면 — 긴 트렌치 전체로 맞추면 면이 안 보였다).
    // R73a3: 닿은 부재는 잘라 그린 자리(고른 부재 상자 + 0.3 m)만 — 흐린 윤곽으로 남은 쪽까지 맞추면 고른 것이 작아진다.
    private Box3 IsolateBox(BoardFace? around)
    {
        var own = Box3.Empty;
        foreach (long id in _fwIso ?? new HashSet<long>())
            if (IndexOf(id) is int i && i >= 0) own = own.With(_memberMeshes[i].Box);
        var center = around != null ? FacesBox(around.Siblings) : own;
        if (center.IsEmpty) return own;
        var size = center.Size;
        double extent = Math.Max(size.X, Math.Max(size.Y, size.Z));
        double grow = around != null ? Math.Max(2, extent) : Math.Max(3, 0.5 * extent);
        var reach = new Box3(center.Min - new Vec3(grow, grow, grow), center.Max + new Vec3(grow, grow, grow));
        var near = IsolateClipBoxes();
        var box = center;
        foreach (long id in (_fwIso ?? new HashSet<long>()).Concat(_fwIsoNear))
        {
            if (IndexOf(id) is not (int i and >= 0)) continue;
            var b = MeshClip.Intersect(_memberMeshes[i].Box, reach);
            if (_fwIso?.Contains(id) == true) { if (!b.IsEmpty) box = box.With(b); continue; }
            foreach (var clip in near)
                if (MeshClip.Intersect(b, clip) is { IsEmpty: false } part) box = box.With(part);
        }
        return box;
    }

    // 닿은 부재 · 닿은 부재만의 판을 고른 부재 상자 + 0.3 m 로 자른다(R73a3 — UpdateFormworkLook 이 IsolateSets 뒤에 부른다). 고른 부재가 같으면 전에 자른 몫을 다시 쓴다
    // (겉면 MeshData 가 같은 것 — 판을 다시 지은 덩이만 새로 자른다). 그림에 넘긴 몫과 다르면 부르는 쪽이 겉면을 갈아 끼운다(ClipsChanged).
    private void BuildIsoClips()
    {
        var next = new List<(MeshData mesh, int source)>();
        if (_fwIso != null && _board != null && Step == 1 && (_fwIsoNear.Count > 0 || _fwIsoNearPieces.Count > 0))
        {
            var boxes = IsolateClipBoxes();
            string key = string.Join(",", _fwIso.Order());
            if (key != _fwIsoClipKey) { _fwIsoClipCache.Clear(); _fwIsoClipKey = key; }
            var used = new HashSet<MeshData>(ReferenceEqualityComparer.Instance);
            MeshData Clip(MeshData source)
            {
                used.Add(source);
                if (!_fwIsoClipCache.TryGetValue(source, out var clip)) _fwIsoClipCache[source] = clip = MeshClip.Clip(source, boxes);
                return clip;
            }
            var bodies = _fwMemberMeshes.Count == MemberCount ? _fwMemberMeshes : _memberMeshes;
            for (int i = 0; i < MemberCount; i++)
                if (_fwIsoNear.Contains(_members[i].Id) && Clip(bodies[i]) is { Triangles: > 0 } body) next.Add((body, i));
            foreach (int piece in _fwIsoNearPieces.Order())
                if (piece < _board.Pieces.Count && Clip(PieceMesh(piece)) is { Triangles: > 0 } plate) next.Add((plate, MemberCount + piece));
            foreach (var stale in _fwIsoClipCache.Keys.Where(k => !used.Contains(k)).ToList()) _fwIsoClipCache.Remove(stale);
        }
        else
        {
            _fwIsoClipKey = null;
            _fwIsoClipCache.Clear();
        }
        _fwIsoClips.Clear();
        _fwIsoClips.AddRange(next);
    }

    // 그림에 넘긴 잘라 그린 몫이 지금 몫과 다른가(겉면을 갈아 끼워야 한다).
    private bool ClipsChanged =>
        _fwIsoClips.Count != _fwClipsDrawn.Count || _fwIsoClips.Where((c, k) => !ReferenceEquals(c.mesh, _fwClipsDrawn[k].mesh) || c.source != _fwClipsDrawn[k].source).Any();

    // 그림 차례: … → 고른 면 → 잘라 그린 몫(R73a3).
    private int ClipStart => FaceOverlayStart + _fwFaceOverlay.Count;

    // 잘라 그린 몫의 그림 차례면 그 몫을 낸 차례(부재 · 덩이) · 아니면 −1 — 그림에 넘긴 몫으로 센다(그림이 묻는 차례).
    private int ClipSource(int index) => index >= ClipStart && index - ClipStart < _fwClipsDrawn.Count ? _fwClipsDrawn[index - ClipStart].source : -1;

    // 3D 오른쪽 위(보기 단추 아래): «부재만 보기 · ■ 고른 부재 · ■ 닿은 부재 n»(보기만 — 색 네모는 3D 의 몸 색) + [모두 보기](따로 — 상태와 넣는 단추를 섞지 않음).
    // R73a3: 숨긴 부재(Del · ① 숨기기)가 있으면 « · 숨김 n» — 부재만 보기가 아니면 «숨긴 부재 n» 만.
    private FrameworkElement BuildIsoBar()
    {
        // R73a5: 좁은 3D(나눔)에서는 제목(부재 이름)이 «…»로 줄고 풀이 · [모두 보기]는 그대로 — 오른쪽부터 쌓고 제목이 남는 폭을 채운다.
        var text = new DockPanel { LastChildFill = true };
        _fwIsoTitle.Text = "부재만 보기";
        _fwIsoTitle.TextTrimming = TextTrimming.CharacterEllipsis;
        var legend = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 0, 0, 0) };
        legend.Children.Add(Swatch(ZonePalette.Todo.r, ZonePalette.Todo.g, ZonePalette.Todo.b, 5));
        legend.Children.Add(new TextBlock { Text = "고른 부재", FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) });
        legend.Children.Add(Swatch(NearTint.r, NearTint.g, NearTint.b, 5));
        legend.Children.Add(_fwIsoCount);
        _fwIsoLegend = legend;
        // R73a4 «이 부재만 보기»의 풀이: 빗금 네모 둘(카드 줄의 네모와 같은 것).
        var solo = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 0, 0, 0), Visibility = Visibility.Collapsed };
        foreach (bool kept in new[] { false, true })
        {
            solo.Children.Add(new Border
            {
                Width = 13, Height = 13, CornerRadius = new CornerRadius(2), Margin = new Thickness(kept ? 10 : 0, 0, 5, 0), VerticalAlignment = VerticalAlignment.Center,
                Background = Model3DView.HatchBrush(_ui.IsDark, kept), BorderBrush = new SolidColorBrush(Model3DView.HatchColor(_ui.IsDark, kept)), BorderThickness = new Thickness(1),
            });
            solo.Children.Add(new TextBlock { Text = kept ? "거푸집 추가 위치" : "공제 위치(닿는 부위)", FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center, Foreground = _text });   // R73a6 전문 용어(JACK 1102 «빠진 자리 → 공제 위치(닿는 부위)»)
        }
        _fwSoloLegend = solo;
        _fwIsoHidden.Foreground = _sub;
        foreach (var right in new FrameworkElement[] { _fwIsoHidden, legend })
        {
            DockPanel.SetDock(right, Dock.Right);
            text.Children.Add(right);
        }
        text.Children.Add(_fwIsoTitle);
        // R73a6: 빗금 풀이는 제목 아래 둘째 줄(«공제 위치(닿는 부위)»가 길어 좁은 왼쪽 3D 에서 부재 이름이 «…»로 잘렸다).
        solo.Margin = new Thickness(0, 4, 0, 0);
        var lines = new StackPanel();
        lines.Children.Add(text);
        lines.Children.Add(solo);
        var chip = new Border
        {
            Background = _ui["Glass"], BorderBrush = _line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(11, 5, 11, 6), Child = lines,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _ui.Mark(chip, "", "fw.iso");                       // 풍선 글은 RefreshIsoBar 가 보기마다
        _fwIsoChip = chip;
        var all = _ui.Button("전체 보기", "BtnSecondary", "모든 부재를 다시 표시합니다 — 숨긴 부재 포함(선택한 면은 그대로 · Esc = 선택 해제).", "btn.fw.iso.all");
        all.Focusable = false;
        all.MinHeight = 30;
        all.Padding = new Thickness(12, 0, 12, 1);
        all.Margin = new Thickness(6, 0, 0, 0);
        all.VerticalAlignment = VerticalAlignment.Center;
        all.Click += (_, _) => ShowAllMembers();
        // 보기 단추 줄(오른쪽 위) 바로 아래 오른쪽에 — 좁은 창에서도 보기 단추 · 알림 딱지와 겹치지 않게(R73a5: 알림은 이 띠 아래 — PlacePartView 의 _notes).
        var bar = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 0, 0, 6), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Visibility = Visibility.Collapsed };
        DockPanel.SetDock(all, Dock.Right);
        bar.Children.Add(all);
        bar.Children.Add(chip);
        _fwIsoBar = bar;
        return bar;
    }

    // 띠를 지금 값으로(② 에서 부재만 보기 · 숨긴 부재가 있을 때만). 부재만 보기가 끝나면 일지 한 줄(R73a3 — 까닭: Esc · [모두 보기] · 숨기기 · 무리 밖을 고름 · ① 로).
    private void RefreshIsoBar()
    {
        if (_fwIso == null && _fwIsoLogged)
        {
            _fwIsoLogged = false;
            Log?.Invoke($"② 전체 보기\t{(_fwIsoSolo ? "부재 단독 보기" : "부재만 보기")} 끝 · {_fwIsoEndWhy ?? "무리 밖을 고름"}");
        }
        _fwIsoEndWhy = null;
        if (_fwIsoBar == null) return;
        int hidden = _editor.Hidden.Count;
        bool iso = Step == 1 && _fwIso != null, solo = iso && SoloOn;
        ShowLocator(solo);                         // 이 부재만 보기 동안 왼쪽 «위치» 그림(R73a5)
        _fwIsoBar.Visibility = iso || (Step == 1 && hidden > 0) ? Visibility.Visible : Visibility.Collapsed;
        // R73a4: «이 부재만 보기 · {부재 이름}»(여럿이면 «… 외 n») + 빗금 풀이 — R73a2 의 색 풀이(고른 · 닿은 부재)는 닿은 부재 보기일 때만.
        _fwIsoTitle.Text = solo ? $"부재 단독 보기 · {SoloName()}" : iso ? "부재만 보기" : $"숨긴 부재 {hidden}";   // R73a6 «이 부재만 보기» → «부재 단독 보기»
        if (_fwIsoLegend != null) _fwIsoLegend.Visibility = iso && !solo ? Visibility.Visible : Visibility.Collapsed;
        if (_fwSoloLegend != null) _fwSoloLegend.Visibility = solo ? Visibility.Visible : Visibility.Collapsed;
        if (_fwIsoChip != null)
            _ui.Tip(_fwIsoChip, solo ? "목록에서 선택한 부재만 표시합니다 — 거푸집 면은 종류 색 · 나머지는 회색. 빨간 빗금 = 다른 부재와 닿아 거푸집 면적에서 공제한 위치 · 청록 빗금 = 접합부지만 거푸집 면적에 포함한 위치(왼쪽 위 [공제] · [거푸집 추가]로 변경)."
                                : iso ? "목록에서 고른 부재와, 거기 닿은 부재의 닿은 근처(0.3 m)만 진하게 봅니다 — 고른 부재의 판은 제 색 · 닿은 부재는 청록 몸에 옅은 판 · 나머지는 흐린 윤곽(찍히지 않음). Del 로 숨긴 부재는 3D 에서만 숨깁니다(수량 그대로)."
                                : "Del 로 숨긴 부재(3D 에서만 — 수량 그대로 · [전체 보기]로 다시 표시).");
        _fwIsoCount.Text = $"닿은 부재 {_fwIsoNear.Count(id => IndexOf(id) >= 0)}";
        _fwIsoHidden.Text = $"숨김 {hidden}";
        _fwIsoHidden.Visibility = iso && hidden > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    // 띠의 부재 이름(R73a4): 하나면 그 이름 · 여럿이면 «첫 이름 외 n».
    private string SoloName()
    {
        var ids = (_fwIso ?? new HashSet<long>()).Where(id => IndexOf(id) >= 0).Order().ToList();
        if (ids.Count == 0) return "";
        long first = PrimaryFace is { } f && ids.Contains(f.ElementId) ? f.ElementId : ids[0];
        // 나무의 부재 줄 이름(같은 이름이 여럿이면 번호 — «벽체 WA_T600 4») · 나무에 없으면 부재 이름.
        string name = PrimaryFace is { } g && g.ElementId == first && _fwTree?.NodeOf(g) is { } node && _fwTree.PathTo(node) is { Count: > 0 } path ? path[^1].Name : MemberLabel(first);
        return name + (ids.Count > 1 ? $" 외 {ids.Count - 1}" : "");
    }

    // [모두 보기]: 고른 면 · 판은 그대로 두고 모든 부재를 다시 보인다(카메라는 그대로) — 숨긴 부재(Del · ① 숨기기)도(R73a3).
    private void ShowAllMembers()
    {
        int hidden = _editor.Hidden.Count;
        if (_fwIso == null && hidden == 0) return;
        if (_fwIso == null) Log?.Invoke($"② 전체 보기\t숨김 {hidden} 풂");
        _fwIsoEndWhy = $"[전체 보기] · 숨김 {hidden} 풂";
        _fwIso = null;
        _editor.Hidden.Clear();
        RefreshAll();
    }

    // Del = 잠깐 숨기기(R73a3 — 3D 에서만 · 값 · 수량 · 기록 · 저장은 그대로 · 되돌리기 줄도 없음): 고른 면의 부재를 숨긴다 — 판만 숨기면 그 부재의 몸이 그대로 가린다
    // (내 판단 · JACK «천장 슬래브도 다 보이니깐 가려서»). 면 없이 판으로 고른 것(끌기 · 딱지)은 그 판의 부재 · 문제 딱지로 고른 부재는 그 부재. 숨긴 것은 고른 것에서 풀린다.
    private bool HideSelected()
    {
        if (_board == null || Busy) return false;
        var faces = FocusFaces();
        var plates = SelectedPieceList();
        var ids = (faces.Count > 0 ? faces.Select(f => f.ElementId) : plates.SelectMany(p => p.Members).Select(m => m.Result.Element.Id))
            .Concat(_rvMembers).Where(id => IndexOf(id) >= 0).Distinct().ToList();
        if (ids.Count == 0) { ShowMessage("숨길 면을 3D 나 목록에서 먼저 선택하세요."); return false; }
        int added = _editor.Hide(ids);
        _fwFaces.Clear();
        _fwSel.Clear();
        _rvMembers.Clear();
        _fwPicked = null;
        _fwPickedSub = null;
        _fwPlaceOpen = false;
        if (_fwIso != null && _fwIso.All(id => _editor.Hidden.Contains(id)))
        {
            _fwIso = null;
            _fwIsoEndWhy = "고른 부재를 숨김";
        }
        CancelFollow();
        RefreshAll();
        string what = ids.Count == 1 ? MemberLabel(ids[0]) : $"부재 {ids.Count}개";
        Log?.Invoke($"② 숨기기\t부재 {ids.Count}(새로 {added} · 면 {faces.Count} · 판 {plates.Count}) · Id {string.Join(",", ids.Take(6))}{(ids.Count > 6 ? " …" : "")} · 숨긴 부재 모두 {_editor.Hidden.Count}");
        ShowMessage($"{what} 숨김(수량 그대로) — [전체 보기]로 되돌립니다.");
        return true;
    }

    // 부재 Id → 이름(나무 · 카드와 같은 말).
    private string MemberLabel(long id) =>
        _board?.Results.FirstOrDefault(r => r.Element.Id == id) is { } r ? MemberLabel(r.Element) : _editor.Find(id) is { } m ? $"{m.Category} {m.Type}" : $"Id {id}";

    // ── 시험에서 쓰는 길 ─────────────────────────────────────────────────────────

    /// <summary>부재만 보기(R73a2): 고른 부재 · 닿은 부재(없으면 null · 빈 목록) · 3D 띠 · [모두 보기] 누르기.</summary>
    public IReadOnlyCollection<long>? IsolatedMembers => _fwIso;
    public IReadOnlyCollection<long> TouchingMembers => _fwIsoNear;
    public FrameworkElement? IsolateBar => _fwIsoBar;
    public string IsolateText => _fwIsoCount.Text;
    public void PressShowAll() => ShowAllMembers();
    public static (byte r, byte g, byte b) NearTintColor => NearTint;

    /// <summary>R73a3: 잘라 그린 몫(겉면 · 낸 그림 차례) · 그 그림 차례 · 자른 상자(고른 부재 상자 + 0.3 m) · 띠 제목 · 숨긴 부재(Del · ① 숨기기).</summary>
    public IReadOnlyList<(MeshData mesh, int source)> IsolateClips => _fwIsoClips;
    public int IsolateClipIndex(int k) => k >= 0 && k < _fwIsoClips.Count ? ClipStart + k : -1;
    public IReadOnlyList<Box3> IsolateBoxes => IsolateClipBoxes();
    public string IsolateTitle => _fwIsoTitle.Text;
    public string IsolateHiddenText => _fwIsoHidden.Visibility == Visibility.Visible ? _fwIsoHidden.Text : "";
    public IReadOnlySet<long> HiddenMembers => _editor.Hidden;

    /// <summary>R73a4: «이 부재만 보기»인가 · 부재 줄로 고른 그대로인가(면 테두리 없음) · 띠의 빗금 풀이가 보이는가 · 마지막 고르기(SelectFaces — 겉면 갈아 끼우기 · 빗금까지) ms.</summary>
    public bool IsSolo => SoloOn;
    public bool IsSoloWhole => SoloWhole;
    public bool SoloLegendShown => _fwSoloLegend is { Visibility: Visibility.Visible };
    public long SelectMilliseconds { get; private set; }

    /// <summary>시험용: 그 부재 · 덩이의 3D 색(부재 차례 · 덩이 차례).</summary>
    public (byte r, byte g, byte b) MemberColor(int member) => FormworkColor(member);

    /// <summary>시험용: 창의 색 · 틀(줄 바탕이 «고름» 색인지 견줄 때).</summary>
    public UiTheme Theme => _ui;

    /// <summary>위치 나무(R73a — 없으면 null) · 지금 펼친 줄(위에서부터).</summary>
    public LocationTree? Tree => _fwTree;
    public IReadOnlyList<LocationTree.Node> TreeRows => _fwTreeRows;

    /// <summary>나무 줄을 누른다(화면의 줄과 같은 길) · 꺾쇠를 누른다.</summary>
    public void ClickTreeNode(LocationTree.Node node, bool ctrl = false) => ClickTreeRow(node, ctrl);
    public void ToggleTreeNode(LocationTree.Node node) => ToggleTree(node);

    /// <summary>그 줄의 화면(보이게 굴린 뒤 · 지어지지 않았으면 null).</summary>
    public FrameworkElement? TreeRowView(LocationTree.Node node)
    {
        if (_fwTreeList == null) return null;
        int at = _fwTreeRows.IndexOf(node);
        if (at < 0) return null;
        _fwTreeList.Show(at);
        _fwTreeList.UpdateLayout();
        return _fwTreeList.Realized.TryGetValue(node, out var row) ? row : null;
    }

    /// <summary>지어진 나무 줄 수(가상화 — 보이는 줄만).</summary>
    public int TreeRealized => _fwTreeList?.Realized.Count ?? 0;

    /// <summary>거름 칸(0 전체 · 1 확인할 자리 · 2 안 셈).</summary>
    public RadioButton? TreeFilterButton(int filter) => filter is >= 0 and < 3 ? _fwFilters[filter] : null;
    public int TreeFilter => _fwTreeFilter;

    /// <summary>고른 면들 · 첫 면(R73a).</summary>
    public IReadOnlyList<BoardFace> PickedFaces => FocusFaces();
    public BoardFace? PickedFace => PrimaryFace;

    /// <summary>3D 에서 그 면을 찍었다(찍기와 같은 길 — 면 · Ctrl).</summary>
    public void ClickFace(BoardFace face, bool ctrl = false) => SelectFaces(new[] { face }, ctrl ? SelectMode.Toggle : SelectMode.Replace, fly: false);

    /// <summary>마우스 밑 이름표(왼쪽 아래).</summary>
    public FrameworkElement HoverPill => _hoverPill;

    /// <summary>고른 면의 겉면 · 그 차례(그림 차례 — 없으면 −1).</summary>
    public int FaceOverlayIndex(int k) => k >= 0 && k < _fwFaceOverlay.Count ? FaceOverlayStart + k : -1;
    public int FaceOverlayCount => _fwFaceOverlay.Count;

    // 펼친 줄만 짓는 목록(가상화): 줄 = 나무 줄 · 짓는 일은 Build 가 한다 · 지어진 줄을 들고 있다(고른 면의 바탕을 그 자리에서 고친다).
    private sealed class TreeList : ItemsControl
    {
        private static ControlTemplate? _template;
        private ScrollViewer? _scroll;
        public Func<LocationTree.Node, FrameworkElement>? Build;
        public readonly Dictionary<LocationTree.Node, Border> Realized = new(ReferenceEqualityComparer.Instance);

        public TreeList()
        {
            Template = _template ??= (ControlTemplate)XamlReader.Parse(
                "<ControlTemplate xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" TargetType=\"ItemsControl\">" +
                "<ScrollViewer x:Name=\"PART_Scroll\" CanContentScroll=\"True\" Focusable=\"False\" VerticalScrollBarVisibility=\"Auto\" HorizontalScrollBarVisibility=\"Disabled\" Padding=\"0,0,4,0\">" +
                "<ItemsPresenter/></ScrollViewer></ControlTemplate>");
            ItemsPanel = new ItemsPanelTemplate(new FrameworkElementFactory(typeof(VirtualizingStackPanel)));
            VirtualizingPanel.SetIsVirtualizing(this, true);
            VirtualizingPanel.SetVirtualizationMode(this, VirtualizationMode.Recycling);
            VirtualizingPanel.SetScrollUnit(this, ScrollUnit.Pixel);
        }

        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();
            _scroll = GetTemplateChild("PART_Scroll") as ScrollViewer;
        }

        /// <summary>굴린 자리(픽셀).</summary>
        public double Offset
        {
            get => _scroll?.VerticalOffset ?? 0;
            set { if (_scroll != null && value > 0) _scroll.ScrollToVerticalOffset(value); }
        }

        /// <summary>그 줄이 보이게 굴린다.</summary>
        public void Show(int index)
        {
            if (index < 0 || index >= Items.Count) return;
            UpdateLayout();
            if (Panel() is { } panel) panel.BringIndexIntoViewPublic(index);
        }

        private VirtualizingStackPanel? Panel()
        {
            var stack = new Stack<DependencyObject>();
            stack.Push(this);
            while (stack.Count > 0)
            {
                var at = stack.Pop();
                if (at is VirtualizingStackPanel panel) return panel;
                for (int i = 0; i < VisualTreeHelper.GetChildrenCount(at); i++) stack.Push(VisualTreeHelper.GetChild(at, i));
            }
            return null;
        }

        protected override DependencyObject GetContainerForItemOverride() => new ContentPresenter();
        protected override bool IsItemItsOwnContainerOverride(object item) => item is ContentPresenter;

        protected override void PrepareContainerForItemOverride(DependencyObject element, object item)
        {
            base.PrepareContainerForItemOverride(element, item);
            if (element is not ContentPresenter presenter || item is not LocationTree.Node node || Build == null) return;
            var row = Build(node);
            presenter.Content = row;
            if (row is Border border) Realized[node] = border;
        }

        protected override void ClearContainerForItemOverride(DependencyObject element, object item)
        {
            if (item is LocationTree.Node node) Realized.Remove(node);
            if (element is ContentPresenter presenter) presenter.Content = null;
            base.ClearContainerForItemOverride(element, item);
        }
    }
}
