using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Windows;
using DH.Grading.Core;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;
using CivilApp = Autodesk.Civil.ApplicationServices;
using CivilDb = Autodesk.Civil.DatabaseServices;

namespace DH.Grading.Civil;

/// <summary>★★★[JACK 0828] <b>지층 구성 — 우측 도킹바.</b>
///
/// <para>JACK: <i>"UI에서 지층구성 단추를 누르면 우측에 도킹창이 뜨고, 거기서 평면도에 마우스로
/// 클릭하면 도킹바에 표 형태로 순서대로 찍은 위치의 속성으로 BH1이라고 뜨고,
/// 사용자가 별도의 시추주상도를 보고 각 층의 깊이를 쳐 넣게 하는 로직이야."</i></para>
///
/// <para><b>왜 팝업이 아니라 도킹바인가.</b> 평면도를 보면서 여러 번 찍어야 한다 —
/// 모달 팝업이면 찍을 때마다 창을 닫았다 열어야 한다. 도킹바는 <b>열어 둔 채로</b> 쓴다.</para>
///
/// <para><b>사용자는 두께만 친다</b>(JACK 확정). 지반고는 <b>원지반 지표면에서 읽는다</b> —
/// 자리를 옮기면 <b>그 자리에서 다시 읽는다</b>.</para></summary>
public static class StrataPalette
{
    private static PaletteSet _ps;
    private static StrataPanel _panel;

    /// <summary>보링공 표식이 사는 레이어 — 지우고 다시 그릴 때 여기만 보면 된다.</summary>
    internal const string MarkLayer = "DH-시추공";

    /// <summary>표식 블록 이름 — 동그라미 안에 이름을 넣은 것(JACK 요구).</summary>
    internal const string MarkBlock = "DH_시추공";

    /// <summary>사용자가 <b>정말로</b> 열라고 했는가 — AutoCAD가 되살린 것과 가르는 표시.</summary>
    private static bool _userAsked;

    /// <summary>★★★[JACK 0831 "civil3d를 키면 바로 지층구성 도킹바가 떠 있는데
    /// 지층구성을 눌러야만 뜨게 해줘"]
    ///
    /// <para><b>원인: 팔레트에 붙박이 GUID를 달아 뒀다.</b> AutoCAD는 GUID가 있는 팔레트만
    /// <b>상태(열림·자리·크기)를 저장했다가 다음에 켤 때 되살린다</b> —
    /// 지난번에 열어 둔 채 끄면 다음 시작에 저절로 뜬다.
    /// 우리 코드에는 시작할 때 이 창을 여는 자리가 <b>한 곳도 없고</b>,
    /// 레지스트리에도 이 창의 상태가 <b>없다</b> — 둘 다 확인하고 남긴다.</para>
    ///
    /// <para>→ <b>GUID를 뗀다.</b> 되살릴 근거가 사라지므로 이 창은 <b>여기를 지나야만</b> 생긴다.
    /// 자리와 크기는 어차피 코드가 <c>Dock = Right</c>로 못 박고 있어 잃는 것이 없다.</para>
    ///
    /// <para>덤으로 옛 <c>Substring(0, 36)</c> 조립도 없앴다 — 문자열을 한 글자만 고쳐도
    /// 그 자리에서 던져 <b>창이 아예 안 열리는</b> 방식이었다(검토 지적).</para>
    ///
    /// <para>그래도 되살아나는 길이 남아 있을 수 있어 <see cref="CloseIfNotAsked"/>가
    /// 시작 직후 한 번 더 본다 — <b>관문을 둘로</b> 둔다.</para></summary>
    [Autodesk.AutoCAD.Runtime.CommandMethod("DHSTRATA", Autodesk.AutoCAD.Runtime.CommandFlags.Session)]
    public static void Show()
    {
        _userAsked = true;
        try
        {
            if (_ps == null)
            {
                _panel = new StrataPanel();
                // ★GUID를 주지 않는다 — 주면 AutoCAD가 상태를 저장했다가 다음 시작에 되살린다.
                _ps = new PaletteSet("지층 구성")
                {
                    Style = PaletteSetStyles.ShowPropertiesMenu
                          | PaletteSetStyles.ShowAutoHideButton
                          | PaletteSetStyles.ShowCloseButton,
                    // ★[JACK] <b>우측</b>에 붙인다.
                    DockEnabled = DockSides.Left | DockSides.Right,
                    // ★★[JACK 0901 "처음 켜질 때 2번 보링공 표가 안 잘릴 만큼 길이로 열어 줄 수 있어?"]
                    //   너비는 <b>표가 정한다</b>(<see cref="StrataPanel.WidestWidth"/>) —
                    //   여기 숫자를 박아 두면 칸을 넓힐 때 창이 안 따라와 또 잘린다(§50).
                    MinimumSize = new System.Drawing.Size(StrataPanel.WidestWidth, 420),
                };
                _ps.AddVisual("지층 구성", _panel);
                // ★뜰 때의 크기도 같은 값으로 — <c>MinimumSize</c>만 주면 도킹 폭이 안 따라오는 판이 있다.
                try { _ps.Size = new System.Drawing.Size(StrataPanel.WidestWidth, 700); } catch { }
            }
            // ★★★[JACK 0831 "팝업 형태 말고 바로 우측 창에 도킹되게 안 되?"]
            //
            //   <b>원인: 도킹을 창이 보이기 전에 걸었다.</b> AutoCAD는 아직 안 뜬 팔레트에는
            //   <c>Dock</c>을 무시한다 — 만들 때 걸어 둔 <c>DockSides.Right</c>가 통째로 버려졌다.
            //   게다가 <b>GUID를 떼면서</b>(시작할 때 저절로 뜨는 것을 막느라) AutoCAD가 기억해 두던
            //   도킹 상태도 사라져, 기본값인 <b>떠 있는 창</b>으로 돌아갔다.
            //   두 고침이 서로 부딪힌 자리다.
            //
            //   → <b>보인 뒤에 건다.</b> 그리고 <b>부를 때마다</b> 확인한다 —
            //     사용자가 떼어 놓았다가 다시 누르면 그때도 오른쪽으로 붙는 편이 낫다.
            _ps.Visible = true;
            try { if (_ps.Dock != DockSides.Right) _ps.Dock = DockSides.Right; } catch { }
            // ★되읽어 남긴다 — 안 붙었으면 그 사실이 로그에 있어야 다음에 헤매지 않는다.
            try
            {
                var got = _ps.Dock;
                DiagLog.Append($"\n[지층구성] 창 열기 — 너비 {StrataPanel.WidestWidth}px(표가 정한 값)"
                             + $" · 실제 {_ps.Size.Width}px · 도킹 {got}"
                             + (got == DockSides.Right ? " (우측)" : " ⚠<b>우측에 안 붙었다</b>"));
            }
            catch { }
            _panel?.Refresh();
        }
        catch (System.Exception ex)
        {
            var ed = AcadApp.DocumentManager.MdiActiveDocument?.Editor;
            ed?.WriteMessage("\n[지층구성] 창을 못 열었습니다 — " + ex.Message);
        }
    }

    /// <summary>★[JACK 0831] <b>두 번째 관문</b> — 시작이 끝난 뒤 한 번만 본다.
    /// <para>사용자가 [지층 구성]을 누른 적이 없는데 창이 떠 있으면 <b>AutoCAD가 되살린 것</b>이다 → 닫는다.
    /// 이미 눌렀다면 <b>건드리지 않는다</b> — 시작 도중에 눌렀을 수도 있다.</para>
    /// <para>GUID를 뗀 것만으로 될 일이지만, 이 창이 <b>제멋대로 뜨지 않는다</b>는 것은
    /// JACK이 직접 말한 요건이라 <b>확인하는 자리를 하나 더</b> 둔다.</para></summary>
    internal static void CloseIfNotAsked()
    {
        try
        {
            if (_userAsked || _ps == null || !_ps.Visible) return;
            _ps.Visible = false;
            try { DiagLog.Append("\n[지층구성] 시작할 때 떠 있던 창을 닫았다 — 단추를 눌러야 열린다"); } catch { }
        }
        catch { }
    }
}

/// <summary>층 하나 — 도킹바의 위쪽 표 한 줄.</summary>
public sealed class LayerRow : INotifyPropertyChanged
{
    private string _name = "";
    private RockClass _rock = RockClass.Soil;
    private InterpMode _mode = InterpMode.Thickness;

    /// <summary>사용자가 붙인 이름 — 표토·매립토·퇴적층 등 조사보고서의 말 그대로.</summary>
    public string Name { get => _name; set { _name = value; Raise(nameof(Name)); } }

    /// <summary>수량으로는 무엇인가 — 다섯 중 하나(JACK 확정).
    /// <para>★★[JACK 0831 "사실상 암선을 빼곤 표시할 필요가 없어 … 토사는 디폴트로 체크 해제해"]
    /// <b>분류를 바꾸면 표시여부가 따라간다.</b> 토사면 끄고 암이면 켠다.
    /// 종전엔 [＋ 층 추가]로 만든 줄이 분류는 토사인데 <b>체크는 켜진 채</b>였다 —
    /// 기본값을 씨앗 층에만 걸어 두고 <b>새로 만드는 길에는 안 걸었기</b> 때문이다.</para>
    /// <para>손으로 껐다 켠 것은 그대로 두되, <b>분류를 바꾸면 다시 기본값</b>으로 간다 —
    /// 연암으로 고르면 선이 저절로 켜지는 편이 손이 덜 간다.</para></summary>
    public RockClass Rock
    {
        get => _rock;
        set
        {
            if (_rock == value) return;          // 같은 값이면 표시여부를 건드리지 않는다
            _rock = value;
            Raise(nameof(Rock)); Raise(nameof(RockText));
        }
    }

    /// <summary>경계면을 두께로 만들까 표고로 만들까.</summary>
    public InterpMode Mode { get => _mode; set { _mode = value; Raise(nameof(Mode)); Raise(nameof(ModeText)); } }

    // ★★[JACK 0901] <b>도면표시 스위치를 없앴다.</b>
    //   값이 하나뿐인 스위치(암층만 그린다)는 사람을 헷갈리게만 한다 —
    //   이제 <c>Rock != Soil</c>이 곧 "그린다"이고, 그 판정은 <c>Confirm</c>이 한 곳에서 한다.
    //   덤으로 어제 씨름한 <b>"첫 층은 원지반과 같은 선"</b> 문제도 통째로 사라졌다 —
    //   첫 층은 토사(두께 모드)이거나 저절로 생기는 토사(GL 모드)라 어차피 안 그린다.

    // ★★[JACK 0828] <b>화면에는 우리말로 보인다.</b>
    //   JACK: <i>"영문 thickness는 두께, elevation은 GL로 바꿔 줘."</i>
    //   <b>속은 그대로 두고 껍데기만 바꾼다</b> — 열거형 이름을 우리말로 바꾸면
    //   코드가 읽기 어려워지고, 저장된 도면과도 어긋난다.
    //   덤으로 수량 분류(<c>Soil</c>·<c>Weathered</c>…)도 우리말로 보이게 했다 —
    //   한글 화면에 영문만 남아 있으면 <b>같은 표 안에서 두 나라 말</b>이 된다.

    /// <summary>화면에 보이는 '적용값' — <c>두께</c> / <c>GL</c>.</summary>
    public string ModeText
    {
        get => _mode == InterpMode.Thickness ? ModeThickness : ModeElevation;
        set { Mode = value == ModeElevation ? InterpMode.Elevation : InterpMode.Thickness; }
    }

    /// <summary>화면에 보이는 수량 분류 — 토  사 / 풍화암 / 연  암 / 보통암 / 경  암.</summary>
    public string RockText
    {
        // ★[JACK 0901] 화면은 <b>붙여 쓴 이름</b>이다 — 도면 표만 벌려 쓴다(가로 맞춤 관례).
        get => QtyTableSpec.TightNameOf(_rock);
        set
        {
            foreach (RockClass r in Enum.GetValues(typeof(RockClass)))
                if (QtyTableSpec.TightNameOf(r) == value) { Rock = r; return; }
        }
    }

    internal const string ModeThickness = "두께";
    internal const string ModeElevation = "GL";

    /// <summary>화면 목록 — 표의 드롭다운이 이걸 쓴다.</summary>
    public static string[] ModeChoices { get; } = { ModeThickness, ModeElevation };
    public static string[] RockChoices { get; } =
        Enum.GetValues(typeof(RockClass)).Cast<RockClass>().Select(QtyTableSpec.TightNameOf).ToArray();

    public event PropertyChangedEventHandler PropertyChanged;
    private void Raise(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

/// <summary>보링공 하나 — 도킹바의 아래쪽 표 한 줄.
/// <para>★<b>두께는 층 수에 따라 칸이 늘고 준다</b>. WPF 표는 고정 속성만 묶을 수 있으므로
/// 두께는 <see cref="Th"/> 목록에 담고 열을 <b>코드로 만든다</b>.</para></summary>
public sealed class BoreRow : INotifyPropertyChanged
{
    private double _x, _y, _gl = double.NaN, _water = double.NaN;

    public string Name { get; set; } = "";

    /// <summary>★[JACK] 표에서 좌표를 고치면 <b>평면의 표식이 그 자리로 옮겨가고</b>
    /// <b>지반고도 그 자리에서 다시 읽는다</b>.</summary>
    public double X { get => _x; set { if (Same(_x, value)) return; _x = value; Raise(nameof(X)); Moved?.Invoke(this); } }
    public double Y { get => _y; set { if (Same(_y, value)) return; _y = value; Raise(nameof(Y)); Moved?.Invoke(this); } }

    /// <summary>지반고 — <b>사람이 안 친다</b>. 원지반에서 읽는다.</summary>
    public double Gl { get => _gl; set { _gl = value; Raise(nameof(Gl)); } }

    /// <summary>지하수위 심도(지반고에서 아래로 m). 없으면 비워 둔다.</summary>
    public double Water { get => _water; set { _water = value; Raise(nameof(Water)); } }

    /// <summary>층별 두께 — 층 수만큼.</summary>
    public List<double> Th { get; } = new();

    /// <summary>표식 블록의 도면 안 자리 — 옮길 때 쓴다.</summary>
    public ObjectId MarkId { get; set; } = ObjectId.Null;

    /// <summary>자리가 바뀌었다 — 화면 표식과 지반고를 다시 맞추라는 신호.</summary>
    public event Action<BoreRow> Moved;

    public event PropertyChangedEventHandler PropertyChanged;
    private void Raise(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    private static bool Same(double a, double b) => Math.Abs(a - b) < 1e-9 || (double.IsNaN(a) && double.IsNaN(b));
}
