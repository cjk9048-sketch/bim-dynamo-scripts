using System.Net.Http;
using System.Text;
using DH.Grading.Core;

namespace DH.Grading.Civil;

/// <summary>★★[v111.7 · JACK 1102 «3D 보기에 건물 · 층수로 높이»] VWorld 건물정보(2D 데이터 API <c>LT_C_BLDGINFO</c> · GIS건물통합정보)를 <b>받기만</b> 한다 —
/// 읽기 · 높이 · 바닥은 Core <see cref="Web3DBuildings"/>, 그리기는 뷰어(<c>web3d/src/extras.js</c>). 도면 · InfraWorks 파일에는 안 넣는다(3D 장면만).
/// <para>지적(<see cref="VWorldCadastral"/>)과 같은 법: 상자 ≤ 1.9 km² · 쪽 1,000 · 호출 하나 10초(한 번 다시) · 전체 60초 · 4개 동시 · 좌표계 = 도면 EPSG 그대로 · 키 오류 따로.
/// <b>하나라도 실패면 <see cref="Fail"/></b> — 부르는 쪽(<see cref="Web3DBuilder"/>)은 건물 없이 3D를 짓는다.</para>
/// <para>건물이 한도보다 많으면 받기 전에 범위를 줄여 다시 센다 — 둘레 여유부터(200 → 50 → 0 m) · 그다음 현장 안쪽으로(넓이 ∝ 건물 수 · 세 번까지).
/// 쪽 일부만 받으면 빠진 건물이 고장처럼 보인다.</para>
/// <para>★[검토 M · 보안] 페이지는 NAS · 웹으로 나간다 — 실패는 <see cref="Fail.Page"/>(정해 둔 짧은 글)만 페이지로, 응답 조각 · 예외 글은 <see cref="System.Exception.Message"/>(키를 지운 것)로 진단 로그에만.</para>
/// <para>★기다림은 모두 ConfigureAwait(false) — 명령 스레드가 막고 기다려도 묶이지 않게(지적과 같은 1028 실측).</para></summary>
internal static class VWorldBuildings
{
    internal const int PageSize = 1000, MaxShrink = 3;

    // 페이지에 나가는 글 — 정해 둔 것만(받은 글 · 예외 글은 안 섞음)
    internal const string PageNet = "건물 자료를 받지 못했습니다(인터넷 · VWorld 확인)";
    internal const string PageKey = "VWorld 키 오류";
    internal const string PageBad = "건물 자료 꼴이 이상합니다";
    internal const string PagePart = "건물 자료 일부를 받지 못했습니다(다시 만들어 보세요)";
    internal const string PageMany = "건물이 너무 많습니다(범위를 줄여도 한도를 넘음)";

    static readonly HttpClient Http = new() { Timeout = System.Threading.Timeout.InfiniteTimeSpan };   // 시간은 호출마다 따로(CancelAfter)

    /// <summary>못 받음 — <see cref="Page"/> = 페이지 글(정해 둔 것) · Message = 진단 로그용 자세한 글(키 지움).</summary>
    internal sealed class Fail : System.Exception
    {
        internal readonly string Page;
        internal readonly bool KeyError;
        internal Fail(string page, string detail, bool keyError = false) : base(detail) { Page = page; KeyError = keyError; }
    }

    /// <summary>로그에 쓰기 전에 키를 지운다(그대로 · 주소에 넣은 꼴 둘 다).</summary>
    internal static string Scrub(string s, string key)
    {
        s ??= "";
        if (string.IsNullOrEmpty(key)) return s;
        s = s.Replace(key, "***", System.StringComparison.OrdinalIgnoreCase);
        string esc = System.Uri.EscapeDataString(key);
        return esc == key ? s : s.Replace(esc, "***", System.StringComparison.OrdinalIgnoreCase);
    }

    internal sealed class Got
    {
        public List<Web3DBuildings.Raw> Raws = new();
        /// <summary>실제로 받은 범위 · 그 둘레 여유(m · 현장 안쪽까지 줄였으면 0).</summary>
        public VWorldParcels.Box Box;
        public double Margin;
        /// <summary>처음 범위의 건물 수(VWorld가 알린 수 · 상자 경계 건물은 두 번 셈).</summary>
        public long Total0;
        public int Boxes, Pages;
        /// <summary>줄였나 · 현장 안쪽까지 줄였나.</summary>
        public bool Shrunk, IntoSite;
        public long Ms;
        public string Diag => $"상자 {Boxes} · 쪽 {Pages}" + (Shrunk ? $" · ⚠ 많아서 범위 줄임(처음 {Total0:N0}동 → {Box.W:N0}×{Box.H:N0} m · " + (IntoSite ? "현장 안쪽)" : $"둘레 {Margin:N0} m)") : "") + $" · {Ms:N0} ms";
    }

    /// <summary><paramref name="site"/>(도면 좌표 · <paramref name="epsg"/>) + 둘레 <paramref name="margin"/> m에 걸친 건물. <paramref name="maxBuildings"/>보다 많으면 범위를 줄인다.</summary>
    internal static Got Fetch(VWorldParcels.Box site, double margin, int epsg, string key, int maxBuildings, System.Action<string>? progress)
    {
        try { return System.Threading.Tasks.Task.Run(() => FetchAsync(site, margin, epsg, key, maxBuildings, progress)).GetAwaiter().GetResult(); }
        catch (System.AggregateException ae) when (ae.InnerException != null) { throw ae.InnerException; }
    }

    static async System.Threading.Tasks.Task<Got> FetchAsync(VWorldParcels.Box site, double margin, int epsg, string key, int maxBuildings, System.Action<string>? progress)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var got = new Got();
        using var all = new System.Threading.CancellationTokenSource(System.TimeSpan.FromSeconds(VWorldCadastral.TotalSec));
        using var gate = new System.Threading.SemaphoreSlim(VWorldCadastral.Concurrency);
        string k = System.Uri.EscapeDataString(key);
        string crs = "EPSG:" + epsg.ToString(System.Globalization.CultureInfo.InvariantCulture);
        int callSec = VWorldCadastral.CallSec;
        Fail F(string page, string detail, bool keyError = false) => new(page, Scrub(detail, key), keyError);

        string late = $"시간 한도({VWorldCadastral.TotalSec}초)를 넘었습니다 — VWorld가 느리거나 범위가 넓습니다.";
        async System.Threading.Tasks.Task<string> Get(string url, string what)
        {
            for (int attempt = 0; ; attempt++)
            {
                try { await gate.WaitAsync(all.Token).ConfigureAwait(false); }
                catch (System.OperationCanceledException) { throw F(PageNet, late); }
                try
                {
                    using var one = System.Threading.CancellationTokenSource.CreateLinkedTokenSource(all.Token);
                    one.CancelAfter(System.TimeSpan.FromSeconds(callSec));
                    using var resp = await Http.GetAsync(url, one.Token).ConfigureAwait(false);
                    byte[] b = await resp.Content.ReadAsByteArrayAsync(one.Token).ConfigureAwait(false);
                    string body = Encoding.UTF8.GetString(b);
                    if (VWorldKey.IsKeyError(body)) throw F(PageKey, $"{what} — 키 오류 응답", keyError: true);
                    if ((int)resp.StatusCode >= 500 && attempt == 0) continue;
                    if (!resp.IsSuccessStatusCode) throw F(PageNet, $"{what} — HTTP {(int)resp.StatusCode}");
                    return body;
                }
                catch (System.OperationCanceledException) when (all.IsCancellationRequested) { throw F(PageNet, late); }
                catch (System.Exception ex) when ((ex is System.OperationCanceledException || ex is HttpRequestException) && attempt == 0) { }
                catch (System.OperationCanceledException) { throw F(PageNet, $"{what} — 응답 없음({callSec}초 · 두 번)"); }
                catch (HttpRequestException hx) { throw F(PageNet, $"{what} — 인터넷 연결 확인({hx.Message})"); }
                finally { gate.Release(); }
            }
        }

        string DataUrl(VWorldParcels.Box b, int page) =>
            "https://api.vworld.kr/req/data?service=data&version=2.0&request=GetFeature&format=json"
            + $"&size={PageSize}&page={page}&data=LT_C_BLDGINFO&crs={crs}&geomFilter=BOX({b.Text()})&geometry=true&attribute=true&key={k}";

        async System.Threading.Tasks.Task<Web3DBuildings.DataPage> Page(VWorldParcels.Box b, int page)
        {
            var dp = Web3DBuildings.ParseDataPage(await Get(DataUrl(b, page), "건물 자료").ConfigureAwait(false));
            if (dp.Status == "OK" || dp.Status == "NOT_FOUND") return dp;
            if (dp.Error.Contains("INVALID_KEY", System.StringComparison.OrdinalIgnoreCase)) throw F(PageKey, "건물 자료 — " + dp.Error, keyError: true);
            throw F(PageBad, "건물 자료 오류 — " + (dp.Error.Length > 0 ? dp.Error : dp.Status.Length > 0 ? dp.Status : "꼴 아님"));
        }

        // ① 상자마다 첫 쪽(건물 수를 먼저 안다) → 한도보다 많으면 줄여 다시: 둘레 여유 200 → 50 → 0 m → 현장 안쪽(넓이 ∝ 수 · 0.9배 여유 · 세 번까지)
        var margins = Web3DBuildings.Margins(margin);
        int mi = 0, inner = 0;
        var core = site;
        var cur = Web3DBuildings.Expand(core, margins[0]);
        List<VWorldParcels.Box> boxes;
        Web3DBuildings.DataPage[] firsts;
        for (int round = 0; ; round++)
        {
            progress?.Invoke(round == 0 ? "건물 받는 중…" : "건물이 많아 범위를 줄여 다시 세는 중…");
            boxes = VWorldParcels.SplitBox(cur);
            firsts = await System.Threading.Tasks.Task.WhenAll(boxes.Select(b => Page(b, 1))).ConfigureAwait(false);
            long sum = firsts.Sum(p => (long)p.Total);
            if (round == 0) got.Total0 = sum;
            if (sum <= maxBuildings) break;
            if (mi + 1 < margins.Count) cur = Web3DBuildings.Expand(core, margins[++mi]);
            else if (inner < MaxShrink) { core = Web3DBuildings.ShrinkAround(core, sum, maxBuildings); cur = core; inner++; got.IntoSite = true; }
            else throw F(PageMany, $"범위를 줄여도 {sum:N0}동 · 한도 {maxBuildings:N0}");
            got.Shrunk = true;
        }
        got.Box = cur; got.Margin = got.IntoSite ? 0 : margins[mi]; got.Boxes = boxes.Count;
        var rest = new List<(int Box, System.Threading.Tasks.Task<Web3DBuildings.DataPage> T)>();
        for (int i = 0; i < boxes.Count; i++)
            for (int pg = 2; pg <= firsts[i].Pages; pg++) rest.Add((i, Page(boxes[i], pg)));
        if (rest.Count > 0) progress?.Invoke($"건물 받는 중… (쪽 {boxes.Count + rest.Count})");
        await System.Threading.Tasks.Task.WhenAll(rest.Select(r => r.T)).ConfigureAwait(false);
        got.Pages = boxes.Count + rest.Count;
        var perBox = new int[boxes.Count];
        for (int i = 0; i < boxes.Count; i++) { got.Raws.AddRange(firsts[i].Items); perBox[i] += firsts[i].Items.Count; }
        foreach (var (bi, t) in rest) { got.Raws.AddRange(t.Result.Items); perBox[bi] += t.Result.Items.Count; }
        // 받은 수가 알린 수보다 적으면(쪽 하나가 비어 옴) 일부만 그리지 않는다 — 지적과 같은 법
        for (int i = 0; i < boxes.Count; i++)
            if (perBox[i] < firsts[i].Total) throw F(PagePart, $"상자 {i + 1}: {perBox[i]:N0}/{firsts[i].Total:N0}");
        got.Ms = sw.ElapsedMilliseconds;
        return got;
    }
}
