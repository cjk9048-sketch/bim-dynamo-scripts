using System.Net.Http;
using System.Text.Json;

namespace DH.Grading.Civil.Commands;

/// <summary>서버 지표면 지도의 주소 검색 — VWorld 검색 API 2.0(address 도로명 · 지번, 없으면 place).
/// 키는 <see cref="VWorldKey.Current"/> 그대로(페이지에 새로 내보내지 않음 · 로그에 안 적음).</summary>
internal static class MapSearch
{
    internal sealed record Hit(string Label, string Cat, double Lon, double Lat);

    static readonly HttpClient Http = new() { Timeout = System.TimeSpan.FromSeconds(10) };

    internal static string BuildUrl(string key, string query, string type, string category) =>
        "https://api.vworld.kr/req/search?service=search&request=search&version=2.0&crs=EPSG:4326&size=10&page=1&format=json&errorformat=json"
        + "&type=" + type + (category.Length > 0 ? "&category=" + category : "")
        + "&query=" + System.Uri.EscapeDataString(query)
        + "&key=" + System.Uri.EscapeDataString(key);

    /// <summary>응답 JSON → 결과 목록. NOT_FOUND면 빈 목록, 그 밖의 오류 상태면 null.</summary>
    internal static List<Hit>? Parse(string json, string cat)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("response", out var r)) return null;
        string st = r.TryGetProperty("status", out var s) ? s.GetString() ?? "" : "";
        if (st == "NOT_FOUND") return new List<Hit>();
        if (st != "OK" || !r.TryGetProperty("result", out var res) || !res.TryGetProperty("items", out var items)) return null;
        var list = new List<Hit>();
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        foreach (var it in items.EnumerateArray())
        {
            if (!it.TryGetProperty("point", out var p)) continue;
            if (!double.TryParse(p.GetProperty("x").GetString(), System.Globalization.NumberStyles.Float, inv, out double x)) continue;
            if (!double.TryParse(p.GetProperty("y").GetString(), System.Globalization.NumberStyles.Float, inv, out double y)) continue;
            string label = "";
            if (it.TryGetProperty("address", out var a) && a.ValueKind == JsonValueKind.Object)
            {
                string road = a.TryGetProperty("road", out var rd) ? rd.GetString() ?? "" : "";
                string parcel = a.TryGetProperty("parcel", out var pc) ? pc.GetString() ?? "" : "";
                label = road.Length > 0 ? road : parcel;
            }
            if (it.TryGetProperty("title", out var t) && (t.GetString() ?? "").Length > 0)
                label = t.GetString() + (label.Length > 0 ? " · " + label : "");
            if (label.Length == 0) continue;
            list.Add(new Hit(label, cat, x, y));
        }
        return list;
    }

    /// <summary>null = 통신 · 키 실패. 빈 목록 = 못 찾음.</summary>
    internal static async Task<List<Hit>?> SearchAsync(string query)
    {
        string key = VWorldKey.Current;
        if (key.Length == 0 || query.Length == 0) return null;
        var all = new List<Hit>();
        bool anyOk = false;
        foreach (var (type, category, cat) in new[] { ("address", "road", "도로명"), ("address", "parcel", "지번"), ("place", "", "장소") })
        {
            if (all.Count > 0) break;   // 도로명 → 지번 → 장소 순, 나오면 거기서 멈춤
            try
            {
                string json = await Http.GetStringAsync(BuildUrl(key, query, type, category)).ConfigureAwait(false);
                var h = Parse(json, cat);
                if (h == null) { DiagLog.Append($"\n[지도검색] {cat} 응답 상태 이상"); continue; }
                anyOk = true;
                foreach (var x in h) if (!all.Exists(o => System.Math.Abs(o.Lon - x.Lon) < 1e-6 && System.Math.Abs(o.Lat - x.Lat) < 1e-6)) all.Add(x);
            }
            catch (System.Exception ex) { DiagLog.Append($"\n[지도검색] {cat} 실패 — {ex.GetType().Name}"); }   // ★주소(키 포함)는 안 적는다
        }
        return anyOk ? all : null;
    }
}
