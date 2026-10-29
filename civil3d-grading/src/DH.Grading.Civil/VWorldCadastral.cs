using System.Net.Http;
using System.Text;
using DH.Grading.Core;

namespace DH.Grading.Civil;

/// <summary>★★[v109.1 · 계획_v109.md 3 · 검토 M1 · M2 · L5] VWorld에서 지적(2D 데이터 API)과 토지소유정보(국가중점 WFS · 필지별 조회)를 <b>받기만</b> 한다 — 읽기 · 합치기는 Core <see cref="VWorldParcels"/>, 그리기는 <see cref="Commands.ImportGisCommand"/>.
/// <para>한도: 호출 하나 10초(한 번 다시) · 전체 60초 · 4개 동시. <b>하나라도 실패면 예외</b> — 부르는 쪽은 옛 지적을 그대로 둔다(M2).
/// 필지별 조회(공유 필지 — WFS가 구분을 빈칸으로 줌)만은 실패해도 그 필지를 «소유 자료 없음»으로 두고 셈.</para>
/// <para>좌표계는 도면 EPSG 그대로 보낸다(1029 실측: 5185~5188 · 5174 모두 받음 · 같은 필지 0 mm). WFS 상자 축 = x, y 차례(같은 실측 — 데이터 API와 PNU 94개 다 맞음).</para>
/// <para>★기다림은 모두 ConfigureAwait(false) — 명령 스레드가 막고 기다려도 묶이지 않게(1028 실측: 묶여 10분 멈춤).</para></summary>
internal static class VWorldCadastral
{
    // 공유 필지 조회 한도 — 개수보다 전체 60초가 먼저 묶는다(1029 실측: 도심 2.24 km² 공유 필지 1,114개 · 조회 600개 15초 · 4개 동시 초당 약 40개)
    internal const int CallSec = 10, TotalSec = 60, Concurrency = 4, WfsMax = 1000, WfsDepth = 3, MaxLookups = 3000, PageSize = 1000;

    static readonly HttpClient Http = new() { Timeout = System.Threading.Timeout.InfiniteTimeSpan };   // 시간은 호출마다 따로(CancelAfter)

    internal sealed class FetchException : System.Exception
    {
        internal readonly bool KeyError;
        internal FetchException(string msg, bool keyError = false) : base(msg) { KeyError = keyError; }
    }

    internal sealed class Got
    {
        public List<VWorldParcels.Raw> Raws = new();
        public Dictionary<string, VWorldParcels.OwnInfo> Own = new(System.StringComparer.Ordinal);
        public int Boxes, Pages, WfsCalls, WfsCut, Lookups, LookupFail, LookupSkipped, LookupPartial, ByPlace, Unmatched;
        public long Ms;
        public string Diag => $"상자 {Boxes} · 쪽 {Pages} · 소유 WFS {WfsCalls}" + (WfsCut > 0 ? $"(⚠ 1,000 넘는 상자 {WfsCut})" : "")
            + (ByPlace > 0 ? $" · PNU 다른 필지(법정동 코드 바뀜) 자리로 맞춤 {ByPlace}" : "") + (Unmatched > 0 ? $" · 소유 정보 못 맞춤 {Unmatched}" : "")
            + (Lookups > 0 ? $" · 공유 필지 조회 {Lookups}" + (LookupFail > 0 ? $"(실패 {LookupFail})" : "") : "")
            + (LookupSkipped > 0 ? $" · 조회 한도로 건너뜀 {LookupSkipped}" : "") + $" · {Ms:N0} ms";
    }

    /// <summary>범위(도면 좌표 · <paramref name="epsg"/>) 안 필지와 소유. 필지가 <paramref name="maxParcels"/>보다 많으면 받기 전에 멈춘다(일부만 그리면 빠진 필지가 고장처럼 보인다).</summary>
    internal static Got Fetch(VWorldParcels.Box box, int epsg, string key, int maxParcels, System.Action<string>? progress)
    {
        try { return System.Threading.Tasks.Task.Run(() => FetchAsync(box, epsg, key, maxParcels, progress)).GetAwaiter().GetResult(); }
        catch (System.AggregateException ae) when (ae.InnerException != null) { throw ae.InnerException; }
    }

    static async System.Threading.Tasks.Task<Got> FetchAsync(VWorldParcels.Box box, int epsg, string key, int maxParcels, System.Action<string>? progress)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var got = new Got();
        using var all = new System.Threading.CancellationTokenSource(System.TimeSpan.FromSeconds(TotalSec));
        using var gate = new System.Threading.SemaphoreSlim(Concurrency);
        string k = System.Uri.EscapeDataString(key);
        string crs = "EPSG:" + epsg.ToString(System.Globalization.CultureInfo.InvariantCulture);

        string late = $"시간 한도({TotalSec}초)를 넘었습니다 — VWorld가 느리거나 범위가 넓습니다. 범위를 좁혀 다시 받으세요.";
        async System.Threading.Tasks.Task<string> Get(string url, string what)
        {
            for (int attempt = 0; ; attempt++)
            {
                try { await gate.WaitAsync(all.Token).ConfigureAwait(false); }
                catch (System.OperationCanceledException) { throw new FetchException(late); }
                try
                {
                    using var one = System.Threading.CancellationTokenSource.CreateLinkedTokenSource(all.Token);
                    one.CancelAfter(System.TimeSpan.FromSeconds(CallSec));
                    using var resp = await Http.GetAsync(url, one.Token).ConfigureAwait(false);
                    byte[] b = await resp.Content.ReadAsByteArrayAsync(one.Token).ConfigureAwait(false);
                    string body = Encoding.UTF8.GetString(b);   // ★[L5] 응답은 UTF-8(1029 실측 — 주소 · 지번 한글)
                    if (VWorldKey.IsKeyError(body)) throw new FetchException(VWorldKey.KeyHelp, keyError: true);
                    if ((int)resp.StatusCode >= 500 && attempt == 0) continue;
                    if (!resp.IsSuccessStatusCode) throw new FetchException($"{what} — HTTP {(int)resp.StatusCode}");
                    return body;
                }
                catch (System.OperationCanceledException) when (all.IsCancellationRequested) { throw new FetchException(late); }
                catch (System.Exception ex) when ((ex is System.OperationCanceledException || ex is HttpRequestException) && attempt == 0) { }
                catch (System.OperationCanceledException) { throw new FetchException($"{what} — 응답 없음({CallSec}초 · 두 번)"); }
                catch (HttpRequestException hx) { throw new FetchException($"{what} — 인터넷 연결 확인({hx.Message})"); }
                finally { gate.Release(); }
            }
        }

        string DataUrl(VWorldParcels.Box b, int page) =>
            "https://api.vworld.kr/req/data?service=data&version=2.0&request=GetFeature&format=json"
            + $"&size={PageSize}&page={page}&data=LP_PA_CBND_BUBUN&crs={crs}&geomFilter=BOX({b.Text()})&geometry=true&attribute=true&key={k}";

        async System.Threading.Tasks.Task<VWorldParcels.DataPage> Page(VWorldParcels.Box b, int page)
        {
            var dp = VWorldParcels.ParseDataPage(await Get(DataUrl(b, page), "지적 자료").ConfigureAwait(false));
            if (dp.Status == "OK" || dp.Status == "NOT_FOUND") return dp;
            if (dp.Error.Contains("INVALID_KEY", System.StringComparison.OrdinalIgnoreCase)) throw new FetchException(VWorldKey.KeyHelp, keyError: true);
            throw new FetchException("지적 자료 오류 — " + (dp.Error.Length > 0 ? dp.Error : dp.Status));
        }

        // ① 지적 — 상자마다 첫 쪽(필지 수를 먼저 안다) → 넘치면 받기 전에 멈춤 → 남은 쪽
        var boxes = VWorldParcels.SplitBox(box);
        got.Boxes = boxes.Count;
        progress?.Invoke("지적 받는 중…");
        var firsts = await System.Threading.Tasks.Task.WhenAll(boxes.Select(b => Page(b, 1))).ConfigureAwait(false);
        long sum = firsts.Sum(p => (long)p.Total);
        if (sum > maxParcels) throw new FetchException($"이 범위에는 필지가 {sum:N0}개라 너무 많습니다(한도 {maxParcels:N0}) — 범위를 좁혀 다시 받으세요.");
        var rest = new List<(int Box, System.Threading.Tasks.Task<VWorldParcels.DataPage> T)>();
        for (int i = 0; i < boxes.Count; i++)
            for (int pg = 2; pg <= firsts[i].Pages; pg++) rest.Add((i, Page(boxes[i], pg)));
        await System.Threading.Tasks.Task.WhenAll(rest.Select(r => r.T)).ConfigureAwait(false);
        got.Pages = boxes.Count + rest.Count;
        var perBox = new int[boxes.Count];
        for (int i = 0; i < boxes.Count; i++) { got.Raws.AddRange(firsts[i].Items); perBox[i] += firsts[i].Items.Count; }
        foreach (var (bi, t) in rest) { got.Raws.AddRange(t.Result.Items); perBox[bi] += t.Result.Items.Count; }
        // ★[M2] 받은 수가 알린 수보다 적으면(쪽 하나가 비어 옴) 일부만 그리지 않는다
        for (int i = 0; i < boxes.Count; i++)
            if (perBox[i] < firsts[i].Total) throw new FetchException($"지적 자료 일부를 못 받았습니다(상자 {i + 1}: {perBox[i]:N0}/{firsts[i].Total:N0}) — 다시 받아 주세요.");
        progress?.Invoke($"소유 정보 받는 중… (필지 {got.Raws.Count:N0})");

        // ② 소유 — 상자마다 WFS(도형 없이) · 1,000개가 차면 넷으로(startIndex를 안 들음 — 1029 실측) · 깊이 3에서도 차면 셈
        var rows = new System.Collections.Concurrent.ConcurrentBag<VWorldParcels.OwnRow>();
        int calls = 0, cut = 0;
        async System.Threading.Tasks.Task Own(VWorldParcels.Box b, int depth)
        {
            string url = $"https://api.vworld.kr/ned/wfs/getPossessionWFS?typename=dt_d160&bbox={b.Text()},{crs}&srsName={crs}&maxFeatures={WfsMax}&output=application/json"
                + $"&propertyname=pnu,posesn_se_code,cnrs_psn_co,ownship_chg_de,lndcgr_code,lndpcl_ar&key={k}";
            var wp = VWorldParcels.ParseOwnershipWfs(await Get(url, "소유 정보").ConfigureAwait(false));
            System.Threading.Interlocked.Increment(ref calls);
            if (!wp.Ok) throw new FetchException("소유 정보 오류 — " + wp.Error);
            if (VWorldParcels.WfsFull(wp, WfsMax))
            {
                if (depth < WfsDepth) { await System.Threading.Tasks.Task.WhenAll(VWorldParcels.Quarter(b).Select(q => Own(q, depth + 1))).ConfigureAwait(false); return; }
                System.Threading.Interlocked.Increment(ref cut);
            }
            foreach (var r in wp.Items) rows.Add(r);
        }
        await System.Threading.Tasks.Task.WhenAll(VWorldParcels.SplitBox(box, 1.0e6).Select(b => Own(b, 0))).ConfigureAwait(false);
        got.WfsCalls = calls; got.WfsCut = cut;
        // ★[v110.2.1] PNU가 같으면 그것 · 다르면(법정동 코드가 바뀐 곳) 본번 · 부번 + 자리로 짝지음
        got.Own = VWorldParcels.MatchOwnership(got.Raws, rows, out got.ByPlace, out got.Unmatched);

        // ③ 공유 필지 — WFS가 구분을 빈칸으로 준 것 · 못 맞춘 것 → 필지별 조회(소유 쪽 PNU로 · 소유자마다 구분 · 이름은 안 받음)
        //   여러 필지가 한 소유 정보를 함께 쓰면(같은 객체) 한 번만 묻는다
        var need = new List<string>();
        var asked = new HashSet<VWorldParcels.OwnInfo>(ReferenceEqualityComparer.Instance);
        foreach (var pnu in got.Raws.Select(r => r.Pnu).Where(p => p.Length > 0).Distinct())
        {
            if (!got.Own.TryGetValue(pnu, out var o)) got.Own[pnu] = o = new VWorldParcels.OwnInfo { OwnPnu = pnu };
            if (VWorldParcels.NeedsLookup(o) && asked.Add(o)) need.Add(pnu);
        }
        got.LookupSkipped = System.Math.Max(0, need.Count - MaxLookups);
        if (need.Count > MaxLookups) need = need.Take(MaxLookups).ToList();
        int done = 0, fail = 0, partial = 0;
        if (need.Count > 0) progress?.Invoke($"공유 필지 소유 조회 중… 0/{need.Count}");
        await System.Threading.Tasks.Task.WhenAll(need.Select(async pnu =>
        {
            var o = got.Own[pnu];
            string ownPnu = o.OwnPnu.Length > 0 ? o.OwnPnu : pnu;
            try
            {
                string body = await Get($"https://api.vworld.kr/ned/data/getPossessionAttr?pnu={ownPnu}&format=json&numOfRows=1000&pageNo=1&key={k}", "공유 필지 소유").ConfigureAwait(false);
                var codes = VWorldParcels.ParseOwnerCodes(body, out int total, out _);
                lock (o)
                {
                    o.Looked = true;
                    if (codes != null) { foreach (var c in codes.Distinct()) if (!o.Codes.Contains(c)) o.Codes.Add(c); if (total > codes.Count) System.Threading.Interlocked.Increment(ref partial); }
                    else System.Threading.Interlocked.Increment(ref fail);
                }
            }
            // 시간 한도에 걸려도 여기서는 실패로만 센다 — 지적 · WFS는 이미 다 받았다(그 필지만 «소유 자료 없음»)
            catch (FetchException fx) when (!fx.KeyError) { System.Threading.Interlocked.Increment(ref fail); lock (o) o.Looked = true; }
            int d = System.Threading.Interlocked.Increment(ref done);
            if (d % 20 == 0) progress?.Invoke($"공유 필지 소유 조회 중… {d}/{need.Count}");
        })).ConfigureAwait(false);
        got.Lookups = need.Count; got.LookupFail = fail; got.LookupPartial = partial;
        got.Ms = sw.ElapsedMilliseconds;
        return got;
    }
}
