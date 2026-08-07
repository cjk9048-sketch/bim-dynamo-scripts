namespace DH.Grading.Civil;

/// <summary>[JACK 0807 '옹벽변환이 여전히 오래 걸린다'] 단계별 소요시간만 재는 최소 계측기.
/// <para>
/// <see cref="ExportProgress"/>는 AutoCAD 상태막대(ProgressMeter)에 묶여 있어 내보내기 전용이다.
/// 정지면 생성(DoGrade)처럼 진행막대가 이미 다른 방식으로 도는 자리에는 **시간만** 필요하다.
/// </para>
/// 규칙(0805에서 값비싸게 배움): <b>계측 자신이 비용이 되면 안 된다.</b>
/// 이 클래스는 Stopwatch 하나와 리스트 하나뿐이라 단계당 비용이 사실상 0이다.
/// </summary>
public sealed class StageTimer
{
    private readonly System.Diagnostics.Stopwatch _all = System.Diagnostics.Stopwatch.StartNew();
    private readonly System.Diagnostics.Stopwatch _cur = new();
    private readonly System.Collections.Generic.List<(string Name, double Sec)> _laps = new();
    private string _stage = "";

    /// <summary>다음 단계로 — 직전 단계의 소요시간을 접어 둔다.</summary>
    public void Stage(string name)
    {
        if (_stage.Length > 0) _laps.Add((_stage, _cur.Elapsed.TotalSeconds));
        _stage = name;
        _cur.Restart();
    }

    /// <summary>전체 소요시간(초).</summary>
    public double TotalSeconds => _all.Elapsed.TotalSeconds;

    /// <summary>단계별 표 — 0.1초 미만은 묶어서 접는다(읽기 좋게).</summary>
    public string Report()
    {
        if (_stage.Length > 0) { _laps.Add((_stage, _cur.Elapsed.TotalSeconds)); _stage = ""; }
        _all.Stop();
        var sb = new System.Text.StringBuilder();
        sb.Append($"소요시간 총 {ExportProgress.Human(_all.Elapsed.TotalSeconds)}");
        double small = 0; int smallN = 0;
        foreach (var (n, s) in _laps)
        {
            if (s < 0.1) { small += s; smallN++; continue; }
            sb.Append($" · {n} {s:F1}s");
        }
        if (smallN > 0) sb.Append($" · 기타 {smallN}단계 {small:F1}s");
        return sb.ToString();
    }
}
