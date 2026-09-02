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
    private readonly System.Collections.Generic.List<(string Name, double Sec, long Alloc, int G2)> _laps = new();
    private string _stage = "";

    // ★★★[JACK 0901 "간혹 느려지다가 리소스가 부족한지 튕기는 경우가 있어"]
    //   <b>어디서 메모리를 먹는지 재고 나서 고친다.</b> 짐작으로 처방을 얹는 것이
    //   이 저장소가 여러 번 데인 방식이다(§58 · §59).
    //
    //   <c>GetTotalAllocatedBytes(false)</c>는 <b>지금까지 새로 잡은 총량</b>이다(살아 있는 양이 아니다).
    //   이 값이 큰 단계가 곧 <b>쓰고 버리기를 반복하는</b> 자리다 — 큰 덩어리를 그렇게 쓰면
    //   큰물건창고(LOH)가 구멍투성이가 되고, 총량은 남았는데 연속 자리가 없어 터진다.
    //   <c>CollectionCount(2)</c>는 <b>큰 청소</b> 횟수 — 이게 늘면 그 단계가 압박을 준 것이다.
    //   ★두 값 모두 <b>세어 둔 것을 읽기만</b> 한다 — 재는 값 자체가 비용이 되면 안 된다(0805 교훈).
    private long _alloc0 = System.GC.GetTotalAllocatedBytes(false);
    private int _g20 = System.GC.CollectionCount(2);
    private readonly long _allocStart = System.GC.GetTotalAllocatedBytes(false);
    private readonly long _memStart = System.GC.GetTotalMemory(false);

    /// <summary>다음 단계로 — 직전 단계의 소요시간을 접어 둔다.</summary>
    public void Stage(string name)
    {
        long a = System.GC.GetTotalAllocatedBytes(false);
        int g2 = System.GC.CollectionCount(2);
        if (_stage.Length > 0) _laps.Add((_stage, _cur.Elapsed.TotalSeconds, a - _alloc0, g2 - _g20));
        _alloc0 = a; _g20 = g2;
        _stage = name;
        _cur.Restart();
    }

    /// <summary>전체 소요시간(초).</summary>
    public double TotalSeconds => _all.Elapsed.TotalSeconds;

    /// <summary>단계별 표 — 0.1초 미만은 묶어서 접는다(읽기 좋게).</summary>
    public string Report()
    {
        if (_stage.Length > 0)
        {
            long aEnd = System.GC.GetTotalAllocatedBytes(false);
            _laps.Add((_stage, _cur.Elapsed.TotalSeconds, aEnd - _alloc0,
                       System.GC.CollectionCount(2) - _g20));
            _stage = "";
        }
        _all.Stop();
        var sb = new System.Text.StringBuilder();
        sb.Append($"소요시간 총 {ExportProgress.Human(_all.Elapsed.TotalSeconds)}");
        double small = 0; int smallN = 0;
        foreach (var (n, s, al, g2) in _laps)
        {
            if (s < 0.1 && al < 64L * 1024 * 1024) { small += s; smallN++; continue; }
            sb.Append($" · {n} {s:F1}s");
            if (al > 0) sb.Append($"/{Mb(al)}");
            if (g2 > 0) sb.Append($"/큰청소{g2}");
        }
        if (smallN > 0) sb.Append($" · 기타 {smallN}단계 {small:F1}s");

        // ★<b>총량과 남은 양을 갈라 적는다.</b> 새로 잡은 총량이 크고 남은 양이 작으면
        //   "쓰고 버리기"(LOH 조각남 위험), 남은 양까지 크면 "붙잡고 있음"(누수)이다.
        long allocAll = System.GC.GetTotalAllocatedBytes(false) - _allocStart;
        long live = System.GC.GetTotalMemory(false) - _memStart;
        sb.Append($"\n  ★메모리 — 새로 잡은 총량 {Mb(allocAll)} · 끝나고 남은 양 {Mb(live)}"
                + $" · 큰청소 {System.GC.CollectionCount(2) - (_g20 - 0)}회");
        return sb.ToString();
    }

    /// <summary>★한 줄짜리 메모리 자국 — <b>단계를 나누기 어려운 명령</b>에 붙인다.
    /// <para>명령 앞에서 <see cref="Mem"/>을 한 번 재 두고 끝에서 <see cref="MemSince"/>로 견준다.</para></summary>
    public static (long Alloc, long Live) Mem()
        => (System.GC.GetTotalAllocatedBytes(false), System.GC.GetTotalMemory(false));

    /// <summary>재 둔 자리에서 얼마나 늘었나 — 사람이 읽는 한 줄로.</summary>
    public static string MemSince((long Alloc, long Live) at)
    {
        var now = Mem();
        return $"메모리 — 새로 잡은 총량 {Mb(now.Alloc - at.Alloc)}"
             + $" · 남은 양 {Mb(now.Live - at.Live)}";
    }

    /// <summary>사람이 읽는 크기 — 바이트는 자릿수가 많아 눈에 안 들어온다.</summary>
    private static string Mb(long b) =>
        b >= 1L << 30 ? $"{b / (double)(1L << 30):F1}GB"
      : b >= 1L << 20 ? $"{b / (double)(1L << 20):F0}MB"
      : $"{b / 1024.0:F0}KB";
}
