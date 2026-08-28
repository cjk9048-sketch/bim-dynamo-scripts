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
/// 클릭하면 도킹바에 표 형태로 순서대로 찍은 위치의 속성으로 GP1이라고 뜨고,
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

    [Autodesk.AutoCAD.Runtime.CommandMethod("DHSTRATA", Autodesk.AutoCAD.Runtime.CommandFlags.Session)]
    public static void Show()
    {
        try
        {
            if (_ps == null)
            {
                _panel = new StrataPanel();
                _ps = new PaletteSet("지층 구성", "DHSTRATA",
                                     new Guid("7A2C9E10-4B3D-4A2E-9C11-DH0828STRATA".Substring(0, 36).Replace("DH0828STRATA", "5F6A7B8C9D0E")))
                {
                    Style = PaletteSetStyles.ShowPropertiesMenu
                          | PaletteSetStyles.ShowAutoHideButton
                          | PaletteSetStyles.ShowCloseButton,
                    // ★[JACK] <b>우측</b>에 붙인다.
                    DockEnabled = DockSides.Left | DockSides.Right,
                    MinimumSize = new System.Drawing.Size(420, 400),
                };
                _ps.AddVisual("지층 구성", _panel);
                _ps.Dock = DockSides.Right;
            }
            _ps.Visible = true;
            _panel?.Refresh();
        }
        catch (System.Exception ex)
        {
            var ed = AcadApp.DocumentManager.MdiActiveDocument?.Editor;
            ed?.WriteMessage("\n[지층구성] 창을 못 열었습니다 — " + ex.Message);
        }
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

    /// <summary>수량으로는 무엇인가 — 다섯 중 하나(JACK 확정).</summary>
    public RockClass Rock { get => _rock; set { _rock = value; Raise(nameof(Rock)); Raise(nameof(RockText)); } }

    /// <summary>경계면을 두께로 만들까 표고로 만들까.</summary>
    public InterpMode Mode { get => _mode; set { _mode = value; Raise(nameof(Mode)); Raise(nameof(ModeText)); } }

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
        get => QtyTableSpec.NameOf(_rock);
        set
        {
            foreach (RockClass r in Enum.GetValues(typeof(RockClass)))
                if (QtyTableSpec.NameOf(r) == value) { Rock = r; return; }
        }
    }

    internal const string ModeThickness = "두께";
    internal const string ModeElevation = "GL";

    /// <summary>화면 목록 — 표의 드롭다운이 이걸 쓴다.</summary>
    public static string[] ModeChoices { get; } = { ModeThickness, ModeElevation };
    public static string[] RockChoices { get; } =
        Enum.GetValues(typeof(RockClass)).Cast<RockClass>().Select(QtyTableSpec.NameOf).ToArray();

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
