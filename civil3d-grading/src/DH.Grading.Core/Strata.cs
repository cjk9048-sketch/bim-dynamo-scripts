using System;
using System.Collections.Generic;

namespace DH.Grading.Core;

/// <summary>★★★[JACK 0828] <b>지층 모델 — 시추 자료로 층 경계면을 만든다.</b>
///
/// <para><b>왜 만드는가.</b> 토적표의 <c>풍화암·연암</c> 칸이 비어 있다. 지금 채워지는 것은 <c>토사</c>뿐이다.
/// 지층 모델이 있어야 "이 단면의 이 구간은 풍화암"을 알 수 있고, 그래야 그 칸이 채워진다.
/// <b>이 기능의 성패는 그 빈칸이 채워지느냐 하나로 판정한다.</b></para>
///
/// <para><b>왜 Core인가.</b> 순수 산수라 도면이 없어도 잴 수 있다 — 하니스가 직접 검증한다.
/// 화면에서만 확인되는 규칙은 언젠가 조용히 어긋난다(이 저장소의 규율).</para>
///
/// <para><b>사용자는 두께만 친다</b>(JACK 확정). 표고도, 층별 지반고도 안 친다 —
/// 지반고는 원지반 지표면에서 자동으로 읽는다. 그래서 이 파일이 받는 것은
/// <see cref="BoreLog.Thickness"/> 하나뿐이고, 표고가 필요한 층은 <c>GL − 두께누적</c>으로 만든다.</para></summary>
public static class Strata
{
    /// <summary>같은 자리로 볼 거리(m) — 이보다 가까우면 <b>그 보링공 값을 그대로</b> 쓴다.
    /// <para>완료기준 1번(<i>"보링공 자리에서 친 두께가 그대로 나온다"</i>)을 <b>수치로 보장</b>하는 값이다.
    /// 역거리 가중은 거리가 0이면 무한대로 갈라지므로, 그 앞에서 <b>정확일치로 빠져나간다</b>.</para></summary>
    public const double SamePointTol = 1e-6;

    /// <summary>역거리 가중의 거듭제곱. 2면 <b>거리 제곱</b>에 반비례한다 —
    /// 가까운 공이 확실히 이기면서도 먼 공이 아주 사라지지는 않는 무난한 값이다.</summary>
    public const double IdwPower = 2.0;
}

/// <summary>지층 경계를 <b>무엇으로 만들지</b> — 층마다 고른다(JACK 확정).</summary>
public enum InterpMode
{
    /// <summary><b>두께</b>를 이어 만들고 원지반에서 빼 내려간다.
    /// <para>지층이 지형을 따라간다. 두께는 음수가 못 되므로 <b>역전이 원천 불가</b>다.
    /// 표토·풍화토처럼 지형을 따라 덮이는 층에 맞다.</para></summary>
    Thickness,

    /// <summary><b>표고</b>를 이어 만든다.
    /// <para>암반이 제 모양대로 눕는다 — 지형이 올라가도 경계는 평평할 수 있다.
    /// 대신 위층을 뚫고 올라올 수 있어 <b>역전 검사가 필요</b>하다.</para></summary>
    Elevation,
}

/// <summary>이 층이 토적표의 <b>어느 칸</b>으로 가는가 — 층을 만들 때 사용자가 같이 고른다(JACK 확정).
/// <para>순서나 이름으로 <b>추측하지 않는다</b>. 규칙이 눈에 보여야 하고 현장마다 다를 수 있어야 한다.</para></summary>
public enum QtyBucket
{
    /// <summary>표에 안 쓴다 — 모델에만 남는다(경암처럼 표에 줄이 없는 층).</summary>
    None,
    Soil,        // 토  사
    Weathered,   // 풍화암
    Soft,        // 연  암
}

/// <summary>층 하나의 정의. 이름은 <b>사용자가 정한다</b>(현장마다 쓰는 말이 다르다).</summary>
/// <param name="Name">사용자가 붙인 이름 — 표토·풍화토·풍화암·연암·경암 등.</param>
/// <param name="Bucket">토적표에서 갈 칸.</param>
/// <param name="Mode">경계면을 무엇으로 만들지.</param>
public readonly record struct StratumDef(string Name, QtyBucket Bucket, InterpMode Mode);

/// <summary>보링공 하나. <b>사용자가 치는 것은 두께와 수위 심도뿐</b>이다.</summary>
/// <param name="Name">공 이름 — <c>GP1</c> 식.</param>
/// <param name="X">평면 좌표.</param>
/// <param name="Y">평면 좌표.</param>
/// <param name="Gl">지반고 — <b>원지반 지표면에서 자동으로 읽는다</b>(사람이 안 친다).</param>
/// <param name="Thickness">층별 두께(m). <see cref="StratumDef"/> 목록과 <b>같은 순서·같은 길이</b>.
/// 모르는 층은 <c>NaN</c>(그 공은 그 층을 안 만난 것으로 본다).</param>
/// <param name="WaterDepth">지하수위 심도 — 지반고에서 아래로 몇 m. 없으면 <c>NaN</c>.</param>
public readonly record struct BoreLog(
    string Name, double X, double Y, double Gl, double[] Thickness, double WaterDepth);

/// <summary>한 자리에서 파 내려간 결과 — 층 경계 표고들과 지하수위.</summary>
/// <param name="Ground">그 자리 원지반 표고(들어온 값 그대로).</param>
/// <param name="Bottom">층별 <b>하단</b> 표고. <c>Bottom[i]</c>는 <c>i</c>번 층의 바닥이고,
/// <c>i</c>번 층의 상단은 <c>i==0</c>이면 <see cref="Ground"/>, 아니면 <c>Bottom[i-1]</c>이다.</param>
/// <param name="Water">지하수위 표고. 없으면 <c>NaN</c>.</param>
/// <param name="Fixed">역전이라 <b>눌러 내린</b> 층 번호와 누른 폭(m). 비어 있으면 손 안 댄 것이다.</param>
public readonly record struct StrataColumn(
    double Ground, double[] Bottom, double Water, IReadOnlyList<(int Layer, double Drop)> Fixed);

/// <summary>★★★ <b>지층 모델</b> — 보링공 목록으로 만들고, 아무 자리나 물어보면 층 경계를 돌려준다.
///
/// <para><b>왜 TIN이 아니라 역거리 가중(IDW)인가.</b>
/// TIN은 점이 <b>셋 이상</b>이어야 하고 <b>삼각망 안쪽만</b> 덮는다. 그런데 보링공은 서너 개인데
/// 부지는 그보다 넓은 것이 보통이라 <b>부지 가장자리가 통째로 빈다</b>.
/// IDW는 <b>한 공만 있어도</b> 답을 내고(그 값이 온 부지에 퍼진다), 공이 늘수록 저절로 촘촘해진다.
/// 그리고 <b>보링공 자리에서는 친 값이 정확히 나온다</b> — 완료기준 1번이 이 성질에 걸려 있다.</para>
///
/// <para><b>역전은 마지막에 한 번만 다룬다.</b> 층마다 따로 보간한 뒤,
/// 위에서 아래로 훑으며 위층 밑으로 붙인다. <b>고친 자리는 반드시 남긴다</b>(JACK 확정) —
/// 조용히 고치는 것도, 빈칸으로 두는 것도 아니다.</para>
///
/// <para><b>지하수위는 역전 제약에서 뺀다</b>(JACK 확정). 지하수위는 <b>지층이 아니다</b> —
/// 풍화토를 가로지르든 풍화암 속에 있든 자연스럽다.
/// 층간 제약에 끼워 넣으면 <b>없는 규칙을 강요해 자료를 망친다</b>.</para></summary>
public sealed class StrataModel
{
    private readonly StratumDef[] _defs;
    private readonly BoreLog[] _logs;

    /// <summary>층 정의(순서가 곧 위에서 아래 차례다).</summary>
    public IReadOnlyList<StratumDef> Defs => _defs;

    /// <summary>쓰인 보링공.</summary>
    public IReadOnlyList<BoreLog> Logs => _logs;

    private StrataModel(StratumDef[] defs, BoreLog[] logs) { _defs = defs; _logs = logs; }

    /// <summary>모델을 만든다. 만들 수 없으면 <paramref name="why"/>에 <b>이유를 적고</b> <c>null</c>을 돌려준다.
    /// <para>★<b>조용히 빈 모델을 돌려주지 않는다</b> — 이 저장소가 여러 번 데인 자리다.
    /// 왜 못 만들었는지가 없으면 도면이 비었을 때 원인을 찾을 길이 없다.</para></summary>
    public static StrataModel Build(IReadOnlyList<StratumDef> defs, IReadOnlyList<BoreLog> logs, out string why)
    {
        why = "";
        if (defs == null || defs.Count == 0) { why = "층이 하나도 정의되지 않았다"; return null; }
        if (logs == null || logs.Count == 0) { why = "보링공이 하나도 없다"; return null; }

        var ok = new List<BoreLog>();
        int badLen = 0, badXy = 0, badGl = 0;
        foreach (var b in logs)
        {
            if (b.Thickness == null || b.Thickness.Length != defs.Count) { badLen++; continue; }
            if (double.IsNaN(b.X) || double.IsNaN(b.Y)) { badXy++; continue; }
            if (double.IsNaN(b.Gl)) { badGl++; continue; }   // 지반고를 못 읽은 공은 두께를 걸 자리가 없다
            ok.Add(b);
        }
        if (ok.Count == 0)
        {
            why = $"쓸 수 있는 보링공이 없다 — 층 수가 안 맞는 것 {badLen}개 · 좌표가 없는 것 {badXy}개 · 지반고를 못 읽은 것 {badGl}개";
            return null;
        }
        if (badLen + badXy + badGl > 0)
            why = $"보링공 {badLen + badXy + badGl}개를 버렸다(층 수 {badLen} · 좌표 {badXy} · 지반고 {badGl}) — 쓴 것 {ok.Count}개";
        return new StrataModel(defs is StratumDef[] a ? (StratumDef[])a.Clone() : ToArray(defs), ok.ToArray());
    }

    private static StratumDef[] ToArray(IReadOnlyList<StratumDef> src)
    {
        var r = new StratumDef[src.Count];
        for (int i = 0; i < src.Count; i++) r[i] = src[i];
        return r;
    }

    /// <summary>★ 한 자리를 파 본다 — <paramref name="groundZ"/>는 <b>그 자리의 원지반 표고</b>다.
    /// <para>원지반은 이 모델이 모른다. 도면 쪽이 지표면에서 읽어 넘겨준다 —
    /// <b>같은 것을 두 곳에서 따로 계산하지 않기 위해서다</b>(§50).</para></summary>
    public StrataColumn At(double x, double y, double groundZ)
    {
        int n = _defs.Length;
        var bottom = new double[n];
        var fixes = new List<(int, double)>();

        // ── ① 층마다 <b>따로</b> 보간한다. 이 단계에서는 역전을 신경 쓰지 않는다.
        //   섞어 놓고 한꺼번에 풀려 하면 어느 층이 왜 그 자리에 왔는지 알 수 없게 된다.
        double top = groundZ;
        var raw = new double[n];
        for (int i = 0; i < n; i++)
        {
            if (_defs[i].Mode == InterpMode.Thickness)
            {
                // 두께를 이어 만들고 <b>바로 위 경계</b>에서 빼 내려간다 — 지형을 따라간다.
                double th = Idw(x, y, i, thickness: true, groundZ);
                raw[i] = double.IsNaN(th) ? double.NaN : top - Math.Max(0.0, th);
            }
            else
            {
                // 표고를 그대로 이어 만든다 — 암반이 제 모양대로 눕는다.
                raw[i] = Idw(x, y, i, thickness: false, groundZ);
            }
            // 다음 층의 '바로 위'는 <b>보정 전 값</b>이 아니라 <b>보정 뒤 값</b>이라야 한다.
            //   그래서 여기서는 top을 안 옮기고 ②에서 한 번에 훑는다.
            top = double.IsNaN(raw[i]) ? top : raw[i];
        }

        // ── ② 역전을 푼다 — <b>위에서 아래로 한 번</b>. 위층 밑으로 붙이고 <b>누른 폭을 남긴다</b>.
        //   ★[JACK 0828] <i>"눌러 내리되 어디를 고쳤는지 다 남긴다."</i>
        //   수량은 나오되 <b>어디를 믿지 말아야 하는지가 눈에 보여야</b> 한다.
        double prev = groundZ;
        for (int i = 0; i < n; i++)
        {
            double v = raw[i];
            if (double.IsNaN(v)) { bottom[i] = double.NaN; continue; }   // 못 잰 것은 0이 아니라 '모른다'
            if (v > prev)
            {
                fixes.Add((i, v - prev));   // 얼마나 눌렀는지 — 이 숫자가 곧 못 믿을 폭이다
                v = prev;
            }
            bottom[i] = v;
            prev = v;
        }

        // ── ③ 지하수위 — <b>지층이 아니므로 위 제약을 안 받는다</b>.
        //   다만 <b>땅 위로는 못 올라간다</b>(그건 침수다). 올라가면 지표에 붙인다.
        double w = IdwWater(x, y, groundZ);
        if (!double.IsNaN(w) && w > groundZ) w = groundZ;

        return new StrataColumn(groundZ, bottom, w, fixes);
    }

    /// <summary>층 <paramref name="i"/>의 값을 역거리 가중으로 잰다.
    /// <param name="thickness"><c>true</c>=두께를, <c>false</c>=하단 표고를 잰다.</param>
    /// <para>★<b>보링공 자리에서는 그 공 값을 그대로</b> 돌려준다(<see cref="Strata.SamePointTol"/>).
    /// 완료기준 1번이 여기 걸려 있다 — <b>보간이 자기 자료를 배신하면 안 된다</b>.</para></summary>
    private double Idw(double x, double y, int i, bool thickness, double groundZ)
    {
        double num = 0, den = 0;
        foreach (var b in _logs)
        {
            double v = ValueOf(b, i, thickness);
            if (double.IsNaN(v)) continue;                 // 그 공이 이 층을 모르면 표를 안 던진다
            double dx = x - b.X, dy = y - b.Y;
            double d2 = dx * dx + dy * dy;
            if (d2 <= Strata.SamePointTol * Strata.SamePointTol) return v;   // ★같은 자리 — 그대로
            double w = 1.0 / Math.Pow(d2, Strata.IdwPower / 2.0);
            num += w * v; den += w;
        }
        return den > 0 ? num / den : double.NaN;
    }

    /// <summary>그 공의 <paramref name="i"/>번 층 값 — 두께이거나 하단 표고.
    /// <para>표고는 <c>GL − 두께누적</c>이다. <b>사용자는 두께만 치므로</b> 표고는 언제나 여기서 만들어진다.</para></summary>
    private double ValueOf(BoreLog b, int i, bool thickness)
    {
        double th = b.Thickness[i];
        if (thickness) return th;
        double z = b.Gl;
        for (int k = 0; k <= i; k++)
        {
            double t = b.Thickness[k];
            if (double.IsNaN(t)) return double.NaN;        // 위층을 모르면 이 층 표고도 모른다
            z -= Math.Max(0.0, t);
        }
        return z;
    }

    /// <summary>지하수위 표고를 잰다 — <b>표고로</b> 잇는다(물은 수평을 찾는다).
    /// <para>두께처럼 지형을 따라가게 하면 언덕 위 물이 언덕만큼 올라간다 — 물은 그렇지 않다.</para></summary>
    private double IdwWater(double x, double y, double groundZ)
    {
        double num = 0, den = 0;
        foreach (var b in _logs)
        {
            if (double.IsNaN(b.WaterDepth)) continue;
            double v = b.Gl - b.WaterDepth;
            double dx = x - b.X, dy = y - b.Y;
            double d2 = dx * dx + dy * dy;
            if (d2 <= Strata.SamePointTol * Strata.SamePointTol) return v;
            double w = 1.0 / Math.Pow(d2, Strata.IdwPower / 2.0);
            num += w * v; den += w;
        }
        return den > 0 ? num / den : double.NaN;
    }
}
