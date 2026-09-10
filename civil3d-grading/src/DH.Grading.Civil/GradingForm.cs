using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace DH.Grading.Civil;

/// <summary>★★★[계획 5단계] <b>정지 값의 검사와 저장 — 팝업과 도킹창이 함께 쓴다.</b>
///
/// <para><b>왜 떼어냈나.</b> 이 60줄은 <see cref="GradingDialog"/>의 <c>OnOk</c> 안에 있었다.
/// 도킹창도 같은 값을 받으므로 그대로 두면 <b>베껴 쓸 수밖에 없고</b>,
/// 그러면 하한 하나를 고칠 때 <b>한쪽만 고쳐진다</b> —
/// 이 저장소가 §20·§26에서 되풀이해 배운 실패다.</para>
///
/// <para>★<b>컨트롤을 받는다.</b> 값(double)만 받게 하지 않은 이유는,
/// 검사가 실패했을 때 <b>그 칸에 커서를 놓고 글자를 잡아 줘야</b> 하기 때문이다.
/// 어느 칸이 틀렸는지 말만 하고 못 찾아 주면 사람이 헤맨다.</para>
///
/// <para>★<b>주인 창은 없어도 된다</b>(<c>owner</c>가 <c>null</c>). 도킹창은 WPF <c>Window</c>가
/// 아니라 팔레트에 얹힌 <c>UserControl</c>이라 <see cref="Window.GetWindow"/>가 <c>null</c>이다 —
/// 그것을 그대로 넘기면 경고창이 안 뜨고 <b>아무 일도 안 일어난 것처럼</b> 보인다.</para></summary>
internal static class GradingForm
{
    /// <summary>단높이 하한(m) — 예시 그림의 클램프와 <b>같은 값</b>이라야 화면과 실제가 안 어긋난다.</summary>
    internal const double BenchMin = 0.2;

    /// <summary>단높이 상한(m) — ★[JACK 0819] 그 위로는 사면이라 부르기 어렵고 대소단(법정 15m)과도 어긋난다.</summary>
    internal const double BenchMax = 15.0;

    /// <summary>★★★[검토 0910] <b>구배 상한 1:30 — 여기가 그 값을 정하는 한 곳이다.</b>
    ///
    /// <para><b>없어서 생긴 일.</b> 단높이는 하한·상한을 둘 다 검사하는데 <b>구배는 하한만</b> 있었다
    /// (아래 <c>slopeFloor</c>). 그래서 <c>1:99</c>를 넣으면 <b>경고 없이 통과</b>해 저장됐는데,
    /// 예시 그림은 30에서 잘라 그린다 — <b>그림은 1:30인데 값은 1:99</b>인 상태가 된다.
    /// 이 저장소가 단높이 하한을 넣으며 적어 둔 말이 그대로 적용된다:
    /// <i>"예시 그림의 클램프와 같은 값이라야 화면과 실제가 안 어긋난다."</i></para>
    ///
    /// <para>터파기 창은 이미 0~30을 검사하고 있었다 — <b>두 창이 어긋나 있었던 것</b>이라
    /// 같은 상수를 쓰게 한다. 1:30이면 수평 30m에 수직 1m다(약 1.9°) — 그보다 완만하면
    /// 사면이 아니라 <b>평지</b>고, 데이라잇이 부지 밖 수백 m로 뻗는다.</para></summary>
    internal const double SlopeMax = 30.0;

    /// <summary>정지 창이 다루는 칸 한 벌 — 팝업이든 도킹창이든 이 모양으로 넘긴다.</summary>
    internal sealed class Controls
    {
        internal TextBox CutBenchHeight, CutBenchWidth, CutSlope;
        internal TextBox FillBenchHeight, FillBenchWidth, FillSlope;
        internal RadioButton ShapeMiter;
        internal CheckBox MountainTerrace;
        internal TextBox TerraceInterval, TerraceWidth;
        internal ComboBox CutWallStyle, FillWallStyle;
        /// <summary>기타 설정으로 옮겨 간 것들 — 도킹창에는 <c>null</c>이다.</summary>
        internal CheckBox ShowOnlyResult;
        internal ComboBox CoordSys;
    }

    /// <summary>★<b>검사하고 저장한다.</b> 하나라도 틀리면 그 칸을 잡아 주고 <c>false</c>.
    ///
    /// <para><paramref name="miterAtOpen"/>는 <b>이 화면을 열었을 때(또는 도면에 맞췄을 때)의 사면형상</b>이다.
    /// 사용자가 <b>실제로 바꿨을 때만</b> 다음 세션 기본값으로 굳히기 위한 기준값 —
    /// 무조건 굳히면 <i>옛 도면(번들이 직각)을 열고 값 하나만 고쳐 저장</i>해도
    /// <b>라운드 취향이 직각으로 둔갑</b>한다(v17.6 "같은 부지인데 옹벽 6장↔163장"의 뿌리).</para></summary>
    internal static bool Apply(Window? owner, Controls c, bool miterAtOpen)
    {
        if (!TryParse(owner, c.CutBenchHeight, "절토 단높이", out double cbh, positive: true) ||
            !TryParse(owner, c.CutBenchWidth, "절토 소단폭", out double cbw, positive: false) ||
            !TryParse(owner, c.FillBenchHeight, "성토 단높이", out double fbh, positive: true) ||
            !TryParse(owner, c.FillBenchWidth, "성토 소단폭", out double fbw, positive: false) ||
            !TryParse(owner, c.CutSlope, "절토구배", out double cs, positive: false) ||
            !TryParse(owner, c.FillSlope, "성토구배", out double fs, positive: false) ||
            !TryParse(owner, c.TerraceInterval, "대소단 간격", out double ti, positive: true) ||
            !TryParse(owner, c.TerraceWidth, "대소단 폭", out double tw, positive: false))
            return false;

        // [단높이 하한 0803] 예시 그림의 클램프(0.2m)와 같은 값을 검증에도 건다 — 안 걸면 0.01 같은 값이
        //   경고 없이 저장되고(그림은 0.2m로 그려져 화면과 실제가 어긋남), 필요한 단수가 폭증해 사면이 잘린다.
        if (cbh < BenchMin - 1e-9 || fbh < BenchMin - 1e-9)
        {
            bool cutBad = cbh < BenchMin - 1e-9;
            Warn(owner, $"{(cutBad ? "절토" : "성토")} 단높이는 {BenchMin}m 이상이어야 합니다.");
            Focus(cutBad ? c.CutBenchHeight : c.FillBenchHeight);
            return false;
        }

        // [구배 하한 0.05 — JACK] 사용자가 0.05 이하(거의 수직 옹벽)를 넣어도 무조건 0.05로 처리.
        // 그 아래는 Civil3D TIN이 예기치 못한 오류를 내는 사례가 있어 미연 방지. (0 입력=옹벽 의도 → 0.05)
        // ★★[JACK 0825] 하드코딩 0.05였다. <b>여기가 진짜 하한</b>이라, 엔진 상수만 바꾸면
        //   이 줄이 다시 0.05로 끌어올려 <b>낮춘 것이 아무 효과가 없었다</b>. 한 곳에서만 정하게 바꾼다.
        double slopeFloor = GradingSettings.MinSlope;
        if (cs > 0 && cs < slopeFloor) cs = slopeFloor; else if (cs == 0) cs = slopeFloor;
        if (fs > 0 && fs < slopeFloor) fs = slopeFloor; else if (fs == 0) fs = slopeFloor;

        // ★★★[검토 0910] <b>구배 상한.</b> 하한만 있고 상한이 없어 1:99가 조용히 통과했다 —
        //   그림은 30에서 잘라 그리므로 <b>화면과 실제가 갈렸다</b>. 자세한 근거는 <see cref="SlopeMax"/>.
        if (cs > SlopeMax + 1e-9 || fs > SlopeMax + 1e-9)
        {
            bool cutBad = cs > SlopeMax + 1e-9;
            Warn(owner, $"구배는 1:{SlopeMax:0.#} 이하여야 합니다({(cutBad ? "절토" : "성토")} 1:{(cutBad ? cs : fs):0.##}).\n\n"
                      + $"1:{SlopeMax:0.#}보다 완만하면 사면이 아니라 평지에 가깝고, 사면이 닿는 자리가 부지 밖 멀리까지 뻗습니다.");
            Focus(cutBad ? c.CutSlope : c.FillSlope);
            return false;
        }

        // ★★[JACK 0819] <b>단높이 상한 15m</b>.
        //   JACK: <i>"맥시멈은 15미터로 하고, 대소단은 자투리 생겨도 돼 — 10M로 설정하면 10M, 5M(자투리)가 생기는 게 맞어."</i>
        //   <b>자투리는 막지 않는다</b> — 그것이 실제 시공 모습이다.
        if (cbh > BenchMax + 1e-9 || fbh > BenchMax + 1e-9)
        {
            Warn(owner,
                $"단높이는 {BenchMax:0.#}m 이하여야 합니다(절토 {cbh:0.##}m · 성토 {fbh:0.##}m).\n\n" +
                "그보다 높은 사면은 한 단으로 세우지 않습니다 — 산지전용허가법의 대소단 간격도 15m입니다.");
            Focus(cbh > BenchMax ? c.CutBenchHeight : c.FillBenchHeight);
            return false;
        }

        GradingSettings.CutBenchHeight = cbh;
        GradingSettings.CutBenchWidth = cbw;
        GradingSettings.FillBenchHeight = fbh;
        GradingSettings.FillBenchWidth = fbw;
        GradingSettings.CutSlope = cs;
        GradingSettings.FillSlope = fs;
        GradingSettings.MiterConvex = c.ShapeMiter?.IsChecked == true;
        GradingSettings.MountainTerrace = c.MountainTerrace?.IsChecked == true;
        GradingSettings.TerraceInterval = ti;
        GradingSettings.TerraceWidth = tw;
        // ★★★[JACK 0910 · 옮기면서 생길 뻔한 구멍] <b>없는 칸은 안 건드린다.</b>
        //   종전엔 <c>?? 0</c>이라 <c>null</c>이 곧 <b>0 = "없음(사면만)"</b>이었다.
        //   옹벽 형태가 [기타 설정]으로 간 지금, 정지 창에서 [값 저장]을 누를 때마다
        //   <b>옹벽 형태가 말없이 "없음"으로 초기화</b>됐을 것이다 —
        //   아래 두 줄이 <c>ShowOnlyResult</c>·<c>CoordSys</c>와 같은 규칙을 따라야 하는 이유다.
        if (c.CutWallStyle != null)
            GradingSettings.CutWallStyle = (WallStyle)System.Math.Max(0, c.CutWallStyle.SelectedIndex);
        if (c.FillWallStyle != null)
            GradingSettings.FillWallStyle = (WallStyle)System.Math.Max(0, c.FillWallStyle.SelectedIndex);

        // 기타 설정으로 옮겨 갈 것들 — 도킹창에는 없다(null이면 안 건드린다).
        if (c.ShowOnlyResult != null) GradingSettings.ShowOnlyResultSurface = c.ShowOnlyResult.IsChecked == true;
        if (c.CoordSys != null)
            GradingSettings.ExportEpsg = GradingDialog.EpsgCodes[
                System.Math.Clamp(c.CoordSys.SelectedIndex, 0, GradingDialog.EpsgCodes.Length - 1)];

        // [재시작 보존 0805] 사용자가 <b>실제로 바꿨을 때만</b> 다음 세션 기본값으로 기록.
        // ★[검토 0908] 통짜 <c>SaveUserPrefs()</c>는 <c>MiterConvex</c>를 <b>무조건</b> 함께 쓴다 —
        //   여기서는 그것이 <b>바로 우리가 저장하려는 값</b>이므로 통짜를 써도 맞다.
        //   (다른 화면에서 부르면 안 된다 — 그때는 <c>SaveUserPrefInt</c>다.)
        if (GradingSettings.MiterConvex != miterAtOpen) GradingSettings.SaveUserPrefs();
        return true;
    }

    /// <summary>구배는 <b>화면에 보일 때만</b> 하한값을 0으로 되돌려 보여 준다 —
    /// 사용자가 <i>수직</i>을 뜻으로 0을 쳤는데 0.05로 되돌아오면 <b>자기가 친 것이 무시된 줄</b> 안다.
    /// <para>★[검토 0909 · 낮음] 팝업과 도킹창에 <b>두 벌</b>이 있었다 — 지금은 식이 같지만
    /// 한쪽만 고쳐질 자리다. <i>"베끼지 않았다"</i>고 적어 놓고 베낀 것이라 여기로 올린다.</para></summary>
    internal static double SlopeShown(double n) => n <= GradingSettings.MinSlope + 1e-9 ? 0 : n;

    private static void Focus(TextBox b) { try { b?.Focus(); b?.SelectAll(); } catch { } }

    /// <summary>★★[검토 0909 · 보통] <b>주인 없는 경고창을 띄우지 않는다.</b>
    ///
    /// <para>도킹창에는 WPF <c>Window</c>가 없다. 그렇다고 <b>주인 없이</b> 띄우면
    /// ①AutoCAD 창이 안 잠겨 경고가 떠 있는 채로 패널을 계속 만질 수 있고
    /// ②도면을 한 번 클릭하면 경고창이 <b>뒤로 숨어</b> 사라진 것처럼 보인다 —
    /// 그러면 [값 저장]이 <b>아무 일도 안 한 것처럼</b> 보인다.</para>
    ///
    /// <para>→ AutoCAD가 제 주 창을 주인으로 삼아 띄워 주는 문을 쓴다.</para></summary>
    private static void Warn(Window? owner, string msg)
    {
        try
        {
            if (owner != null) { MessageBox.Show(owner, msg, "입력 오류", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
        }
        catch { }
        try { Autodesk.AutoCAD.ApplicationServices.Core.Application.ShowAlertDialog(msg); }
        catch { try { MessageBox.Show(msg, "입력 오류", MessageBoxButton.OK, MessageBoxImage.Warning); } catch { } }
    }

    /// <summary>숫자 한 칸 — '.'과 ',' 둘 다 받는다(한국 사용자 입력 편의).</summary>
    internal static bool TryParse(Window? owner, TextBox box, string name, out double value, bool positive)
    {
        value = 0;
        // ★[검토 0909 · 낮음] <b>조용히 거짓을 돌려주지 않는다.</b> 5b에서 칸 하나가
        //   조건부가 되는 순간, [값 저장]이 <b>아무 말 없이 아무 일도 안 하게</b> 된다.
        if (box == null) { Warn(owner, $"'{name}' 칸이 없습니다 — 내부 오류입니다(개발자에게 알려 주세요)."); return false; }
        string text = box.Text.Trim().Replace(',', '.');
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) ||
            value < 0 || (positive && value <= 0))
        {
            Warn(owner, $"'{name}' 값을 확인하세요. {(positive ? "0보다 큰" : "0 이상의")} 숫자여야 합니다.");
            Focus(box);
            return false;
        }
        return true;
    }
}
