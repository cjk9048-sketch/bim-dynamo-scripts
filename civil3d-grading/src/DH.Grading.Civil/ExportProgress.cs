using System.Collections.Generic;
using System.Diagnostics;
using Autodesk.AutoCAD.Runtime;

namespace DH.Grading.Civil;

/// <summary>
/// [JACK 0805] 내보내기 진행 표시 + 단계별 소요시간.
/// <para>
/// 내보내기는 부지 규모에 따라 수십 초~수 분이 걸리는데, 그동안 화면이 멈춘 것처럼 보여
/// "죽은 건지 도는 건지" 알 수 없었다. AutoCAD 상태막대 진행표시기(<see cref="ProgressMeter"/>)로
/// 지금 어느 단계인지 보여주고, 끝나면 단계별로 몇 초 걸렸는지 로그와 완료 팝업에 남긴다.
/// </para>
/// 진행표시기는 실패해도 내보내기 자체를 막으면 안 되므로 모든 호출을 삼킨다(표시는 부가 기능).
/// </summary>
public sealed class ExportProgress : System.IDisposable
{
    private readonly ProgressMeter? _pm;
    private readonly Stopwatch _all = Stopwatch.StartNew();
    private readonly Stopwatch _cur = new();
    private readonly List<(string Name, double Sec)> _laps = new();
    private string _stage = "";
    private readonly int _total;
    private int _done;

    /// <param name="totalStages">전체 단계 수 — 막대가 얼마나 남았는지 보여주는 용도.</param>
    public ExportProgress(int totalStages)
    {
        _total = System.Math.Max(1, totalStages);
        try
        {
            _pm = new ProgressMeter();
            _pm.Start("내보내기 준비 중");
            _pm.SetLimit(_total);
        }
        catch { _pm = null; }
    }

    /// <summary>다음 단계로. 직전 단계의 소요시간을 기록한다.</summary>
    public void Stage(string name)
    {
        Lap();
        _stage = name;
        _cur.Restart();
        _done++;
        try
        {
            _pm?.Stop();
            _pm?.Start($"내보내기 — {name} ({_done}/{_total})");
            _pm?.SetLimit(_total);
            for (int i = 0; i < _done; i++) _pm?.MeterProgress();
        }
        catch { }
    }

    /// <summary>같은 단계 안에서 진행을 알린다(구역 루프 등) — 화면이 멈춘 것처럼 보이지 않게.</summary>
    public void Tick()
    {
        try { _pm?.MeterProgress(); } catch { }
    }

    private void Lap()
    {
        if (_stage.Length == 0) return;
        _laps.Add((_stage, _cur.Elapsed.TotalSeconds));
        _cur.Reset();
    }

    /// <summary>전체 소요시간(초).</summary>
    public double TotalSeconds => _all.Elapsed.TotalSeconds;

    /// <summary>사람이 읽는 소요시간 — 1분 미만은 초, 그 이상은 분·초.</summary>
    public static string Human(double sec) =>
        sec < 60 ? $"{sec:F1}초" : $"{(int)(sec / 60)}분 {sec % 60:F0}초";

    /// <summary>단계별 소요시간 표 — 로그용. 0.1초 미만 단계는 묶어서 생략한다(읽기 좋게).</summary>
    public string Report()
    {
        Lap();
        _all.Stop();   // 보고 시점에 멈춘다 — 팝업에 찍히는 총시간과 로그의 총시간이 같아야 한다
        var sb = new System.Text.StringBuilder();
        sb.Append($"소요시간 총 {Human(_all.Elapsed.TotalSeconds)}");
        double small = 0; int smallN = 0;
        foreach (var (n, s) in _laps)
        {
            if (s < 0.1) { small += s; smallN++; continue; }
            sb.Append($" · {n} {s:F1}s");
        }
        if (smallN > 0) sb.Append($" · 기타 {smallN}단계 {small:F1}s");
        return sb.ToString();
    }

    public void Dispose()
    {
        try { _pm?.Stop(); _pm?.Dispose(); } catch { }
    }
}
