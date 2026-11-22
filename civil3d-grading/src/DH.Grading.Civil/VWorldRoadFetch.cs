using System.Net.Http;
using System.Text;
using DH.Grading.Core;

namespace DH.Grading.Civil;

/// <summary>★★[JACK 1122 «회사 DB가 아니어도 브이월드에서»] VWorld 도로 받기 — 도로중심선(<c>LT_L_N3A0020000</c> · 도로폭)과 표준노드링크(<c>LT_L_MOCTLINK</c> · 터널 · 교량 구분).
/// 읽기 · 테두리 만들기는 Core <see cref="VWorldRoads"/>. 좌표계는 도면 EPSG 그대로 보낸다(지적과 같음). 하나라도 실패면 예외(일부만 쓰면 빠진 도로가 울퉁불퉁하게 남는다).</summary>
internal static class VWorldRoadFetch
{
    const int PageSize = 1000, CallSec = 15, MaxLines = 60000;
    static readonly HttpClient Http = new() { Timeout = System.TimeSpan.FromSeconds(CallSec) };

    internal sealed class Got
    {
        public List<VWorldRoads.Line> Centers = new(), Links = new();
        public int Calls;
        public long Ms;
    }

    internal static Got Fetch(VWorldParcels.Box box, int epsg, string key, System.Action<string>? progress)
    {
        try { return System.Threading.Tasks.Task.Run(() => Run(box, epsg, key, progress)).GetAwaiter().GetResult(); }
        catch (System.AggregateException ae) when (ae.InnerException != null) { throw ae.InnerException; }
    }

    static Got Run(VWorldParcels.Box box, int epsg, string key, System.Action<string>? progress)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var got = new Got();
        string k = System.Uri.EscapeDataString(key);
        string crs = "EPSG:" + epsg.ToString(System.Globalization.CultureInfo.InvariantCulture);
        foreach (var (layer, into, what) in new[] { (VWorldRoads.CenterLayer, got.Centers, "도로중심선"), (VWorldRoads.LinkLayer, got.Links, "교통링크") })
            foreach (var b in VWorldParcels.SplitBox(box))
                for (int page = 1; ; page++)
                {
                    progress?.Invoke($"VWorld {what} 받는 중… ({into.Count:N0})");
                    string url = "https://api.vworld.kr/req/data?service=data&version=2.0&request=GetFeature&format=json"
                        + $"&size={PageSize}&page={page}&data={layer}&crs={crs}&geomFilter=BOX({b.Text()})&geometry=true&attribute=true&key={k}";
                    string body = Get(url, what);
                    got.Calls++;
                    var p = VWorldRoads.ParsePage(body);
                    if (p.Status == "NOT_FOUND") break;
                    if (p.Status != "OK")
                    {
                        if (p.Error.Contains("INVALID_KEY", System.StringComparison.OrdinalIgnoreCase)) throw new VWorldCadastral.FetchException(VWorldKey.KeyHelp, keyError: true);
                        throw new VWorldCadastral.FetchException($"{what} 오류 — " + (p.Error.Length > 0 ? p.Error : p.Status));
                    }
                    into.AddRange(p.Items);
                    if (into.Count > MaxLines) throw new VWorldCadastral.FetchException($"{what}이 너무 많습니다({into.Count:N0}) — 원지반 범위가 넓습니다.");
                    if (page >= p.Pages) break;
                }
        got.Ms = sw.ElapsedMilliseconds;
        return got;
    }

    static string Get(string url, string what)
    {
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                using var resp = Http.Send(new HttpRequestMessage(HttpMethod.Get, url));
                using var st = resp.Content.ReadAsStream();
                using var rd = new System.IO.StreamReader(st, Encoding.UTF8);
                string body = rd.ReadToEnd();
                if (VWorldKey.IsKeyError(body)) throw new VWorldCadastral.FetchException(VWorldKey.KeyHelp, keyError: true);
                if ((int)resp.StatusCode >= 500 && attempt == 0) continue;
                if (!resp.IsSuccessStatusCode) throw new VWorldCadastral.FetchException($"{what} — HTTP {(int)resp.StatusCode}");
                return body;
            }
            catch (System.Exception ex) when ((ex is TaskCanceledException || ex is HttpRequestException) && attempt == 0) { }
            catch (TaskCanceledException) { throw new VWorldCadastral.FetchException($"{what} — 응답 없음({CallSec}초 · 두 번)"); }
            catch (HttpRequestException hx) { throw new VWorldCadastral.FetchException($"{what} — 인터넷 연결 확인({hx.Message})"); }
        }
    }
}
