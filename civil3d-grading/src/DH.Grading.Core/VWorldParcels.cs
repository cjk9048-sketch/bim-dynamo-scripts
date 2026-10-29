using System.Globalization;
using System.Text.Json;
using NetTopologySuite.Geometries;
using NetTopologySuite.Geometries.Utilities;

namespace DH.Grading.Core;

/// <summary>★★[v109.1 · 계획_v109.md 3 · JACK 1028 «지적은 VWorld로 · 지번은 속성에 · 국유지/사유지 구분이 중요»]
/// VWorld 지적(2D 데이터 API <c>LP_PA_CBND_BUBUN</c>) · 토지소유정보(국가중점 WFS <c>getPossessionWFS</c> · 필지별 <c>getPossessionAttr</c>) — <b>받은 글을 읽고 합치고 자르는 일만</b>(인터넷 · 도면은 Civil 쪽).
/// <para>실측(1029 · 서울시청 둘레 250×200 m): 데이터 API · WFS 둘 다 EPSG 5185 · 5186 · 5187 · 5188 · 5174를 받고 같은 필지 꼭짓점이 pyproj 변환과 0 mm(5174만 2~4 cm) ·
/// 필지 94 = 소유 94(PNU 다 맞음) · WFS는 <c>startIndex</c>를 안 들음(같은 40개가 다시 옴 — 1,000개가 차면 상자를 나눠야) ·
/// 소유 구분이 빈칸인 7개 중 6개는 공유자 2~27명(필지별 조회로는 소유자마다 구분이 옴 — 14명 모두 «개인»), 하나는 공유인수도 없음.</para></summary>
public static class VWorldParcels
{
    /// <summary>데이터 API 한 번의 상자 한도 — 문서 2 km²(조금 덜 · 실측으로는 2.25 km²도 받았지만 문서를 따름).</summary>
    public const double MaxBoxArea = 1.9e6;

    public readonly record struct Box(double X0, double Y0, double X1, double Y1)
    {
        public double W => X1 - X0;
        public double H => Y1 - Y0;
        public double Area => W * H;
        public string Text(int d = 3) => string.Format(CultureInfo.InvariantCulture, "{0},{1},{2},{3}",
            Math.Round(X0, d), Math.Round(Y0, d), Math.Round(X1, d), Math.Round(Y1, d));
    }

    /// <summary>같은 크기 격자로 — 한 장 ≤ <paramref name="maxArea"/> · 빈틈 · 겹침 없음(경계는 같은 수를 나눠 씀).</summary>
    public static List<Box> SplitBox(Box b, double maxArea = MaxBoxArea)
    {
        var res = new List<Box>();
        if (!(b.W > 0) || !(b.H > 0)) return res;
        int nx = 1, ny = 1;
        while (b.W / nx * (b.H / ny) > maxArea)
        {
            if (b.W / nx >= b.H / ny) nx++; else ny++;
        }
        for (int j = 0; j < ny; j++)
            for (int i = 0; i < nx; i++)
            {
                double x0 = i == 0 ? b.X0 : b.X0 + b.W * i / nx, x1 = i == nx - 1 ? b.X1 : b.X0 + b.W * (i + 1) / nx;
                double y0 = j == 0 ? b.Y0 : b.Y0 + b.H * j / ny, y1 = j == ny - 1 ? b.Y1 : b.Y0 + b.H * (j + 1) / ny;
                res.Add(new Box(x0, y0, x1, y1));
            }
        return res;
    }

    /// <summary>넷으로(소유 WFS가 1,000개를 채웠을 때).</summary>
    public static Box[] Quarter(Box b)
    {
        double mx = (b.X0 + b.X1) / 2, my = (b.Y0 + b.Y1) / 2;
        return new[] { new Box(b.X0, b.Y0, mx, my), new Box(mx, b.Y0, b.X1, my), new Box(b.X0, my, mx, b.Y1), new Box(mx, my, b.X1, b.Y1) };
    }

    // ── 지번 · 지목 ──────────────────────────────────────────────────────────

    /// <summary>지목 부호(지적도 글자) — 28종(공간정보관리법 시행령 58조 · 부호 = 지적도에 적는 한 글자).</summary>
    static readonly (string Sym, string Name)[] Jimoks =
    {
        ("전", "전"), ("답", "답"), ("과", "과수원"), ("목", "목장용지"), ("임", "임야"), ("광", "광천지"), ("염", "염전"), ("대", "대"),
        ("장", "공장용지"), ("학", "학교용지"), ("차", "주차장"), ("주", "주유소용지"), ("창", "창고용지"), ("도", "도로"), ("철", "철도용지"), ("제", "제방"),
        ("천", "하천"), ("구", "구거"), ("유", "유지"), ("양", "양어장"), ("수", "수도용지"), ("공", "공원"), ("체", "체육용지"), ("원", "유원지"),
        ("종", "종교용지"), ("사", "사적지"), ("묘", "묘지"), ("잡", "잡종지"),
    };

    /// <summary>부호 → 이름(«과» → «과수원») · 모르면 "".</summary>
    public static string JimokName(string sym)
    {
        foreach (var (s, n) in Jimoks) if (s == sym) return n;
        return "";
    }

    /// <summary>소유 WFS의 지목 코드(«08») → 부호(«대») — 부호 차례가 코드 차례(01 전 … 28 잡). 모르면 "".</summary>
    public static string JimokSymOfCode(string? code)
        => int.TryParse(code, NumberStyles.Integer, CultureInfo.InvariantCulture, out int k) && k >= 1 && k <= Jimoks.Length ? Jimoks[k - 1].Sym : "";

    /// <summary>«13-2 대» · «25-5대» · «산12-1임» · «3종» → (지번, 지목 부호). 끝 한 글자가 지목 부호일 때만 뗀다(공백 섞임 — 실측).</summary>
    public static (string Jibun, string Jimok) SplitJibun(string? raw)
    {
        string s = (raw ?? "").Trim();
        if (s.Length >= 2)
        {
            string last = s.Substring(s.Length - 1);
            if (JimokName(last).Length > 0) return (s.Substring(0, s.Length - 1).Trim(), last);
        }
        return (s, "");
    }

    /// <summary>PNU(19자리 = 법정동 10 · 대장 1(1 토지 · 2 임야) · 본번 4 · 부번 4) → 지번(«산12-1»). 꼴이 아니면 "".</summary>
    public static string JibunFromPnu(string? pnu)
    {
        if (pnu == null || pnu.Length != 19 || !pnu.All(char.IsAsciiDigit)) return "";
        char reg = pnu[10];
        if (reg != '1' && reg != '2') return "";
        int bon = int.Parse(pnu.Substring(11, 4), CultureInfo.InvariantCulture), bu = int.Parse(pnu.Substring(15, 4), CultureInfo.InvariantCulture);
        if (bon <= 0) return "";
        return (reg == '2' ? "산" : "") + bon.ToString(CultureInfo.InvariantCulture) + (bu > 0 ? "-" + bu.ToString(CultureInfo.InvariantCulture) : "");
    }

    // ── 소유 구분 ────────────────────────────────────────────────────────────

    public enum OwnKind { Unknown, Private, Public, Mixed }

    /// <summary>소유구분 코드(posesn_se_code) 이름. ★WFS의 <c>lbl</c>은 안 씀 — 시·도유지 · 군유지도 «국유지»로 묶는다(1028 실측).</summary>
    public static string CodeName(string? code) => code switch
    {
        "01" => "개인", "02" => "국유지", "03" => "외국인·외국공공기관", "04" => "시·도유지", "05" => "군유지",
        "06" => "법인", "07" => "종중", "08" => "종교단체", "09" => "기타단체",
        null or "" => "", _ => "코드 " + code,
    };

    /// <summary>국공유지 = 02 · 04 · 05 · 사유지 = 01 · 03 · 06~09 · 그 밖(빈칸 · 모르는 코드) = 모름.</summary>
    public static OwnKind KindOfCode(string? code) => code switch
    {
        "02" or "04" or "05" => OwnKind.Public,
        "01" or "03" or "06" or "07" or "08" or "09" => OwnKind.Private,
        _ => OwnKind.Unknown,
    };

    /// <summary>한 필지의 여러 줄(공유자마다) — 다 국공유 = 국공유 · 다 사유 = 사유 · 섞이면 «일부 국공유» · 아는 코드가 없으면 모름(계획 L2).</summary>
    public static OwnKind Combine(IEnumerable<string?> codes)
    {
        bool pub = false, pri = false;
        foreach (var c in codes)
        {
            var k = KindOfCode(c);
            if (k == OwnKind.Public) pub = true;
            else if (k == OwnKind.Private) pri = true;
        }
        return pub && pri ? OwnKind.Mixed : pub ? OwnKind.Public : pri ? OwnKind.Private : OwnKind.Unknown;
    }

    public static string KindName(OwnKind k) => k switch
    {
        OwnKind.Public => "국공유지", OwnKind.Private => "사유지", OwnKind.Mixed => "일부 국공유", _ => "소유 자료 없음",
    };

    /// <summary>한 필지의 소유 정보(WFS 줄 + 필지별 조회를 합친 것).</summary>
    public sealed class OwnInfo
    {
        public List<string> Codes = new();      // 아는 코드만(빈칸 뺌)
        public int? Cnrs;                        // 공유인수
        public string ChgDate = "";             // 소유권 변동일 «1959-03-03»
        public double? LedgerArea;               // 대장면적 ㎡
        public string JimokCode = "";           // «08»
        public bool Looked;                      // 필지별 조회를 했나
        /// <summary>소유 정보 쪽 PNU — 지적 자료와 다를 수 있다(법정동 코드가 바뀐 곳 · 1029 실측 — 구가 새로 생긴 시: 지적은 새 코드 · 소유는 옛 코드). 필지별 조회는 이것으로.</summary>
        public string OwnPnu = "";
        public OwnKind Kind => Combine(Codes);
        /// <summary>구분 이름 — 코드 하나면 그 이름(«시·도유지») · 여럿이면 «개인 외 2» 꼴.</summary>
        public string CodeText
        {
            get
            {
                var d = Codes.Distinct().ToList();
                if (d.Count == 0) return "";
                if (d.Count == 1) return CodeName(d[0]);
                return CodeName(d[0]) + $" 외 {d.Count - 1}";
            }
        }
    }

    /// <summary>필지별 조회가 필요한가 — 아는 코드가 없을 때(공유 필지는 WFS가 빈칸으로 준다 — 1029 실측).</summary>
    public static bool NeedsLookup(OwnInfo o) => !o.Looked && o.Codes.Count == 0;

    // ── 받은 글 읽기 ─────────────────────────────────────────────────────────

    public sealed class Poly
    {
        public List<(double X, double Y)> Shell = new();
        public List<List<(double X, double Y)>> Holes = new();
    }

    /// <summary>데이터 API 한 줄(필지 한 개 · 잘리기 전).</summary>
    public sealed class Raw
    {
        public string Pnu = "", JibunRaw = "", Bonbun = "", Bubun = "", Addr = "", GosiYm = "";
        public double? Jiga;                      // 개별공시지가 원/㎡
        public List<Poly> Polys = new();
    }

    public sealed class DataPage
    {
        public string Status = "";             // OK · NOT_FOUND · ERROR
        public string Error = "";              // ERROR 글(코드 · 글)
        public int Total, Pages, Page;
        public List<Raw> Items = new();
    }

    static string Str(JsonElement e, string k)
    {
        if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(k, out var v)) return "";
        return v.ValueKind switch
        {
            JsonValueKind.String => (v.GetString() ?? "").Trim(),
            JsonValueKind.Number => v.GetRawText(),
            _ => "",
        };
    }

    static int Int(JsonElement e, string k) => int.TryParse(Str(e, k), NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : 0;

    static double? Num(JsonElement e, string k) => double.TryParse(Str(e, k), NumberStyles.Float, CultureInfo.InvariantCulture, out double v) && double.IsFinite(v) ? v : null;

    static List<(double, double)> Ring(JsonElement r)
    {
        var pts = new List<(double, double)>();
        foreach (var p in r.EnumerateArray())
        {
            if (p.ValueKind != JsonValueKind.Array || p.GetArrayLength() < 2) continue;
            double x = p[0].GetDouble(), y = p[1].GetDouble();
            if (double.IsFinite(x) && double.IsFinite(y)) pts.Add((x, y));
        }
        return pts;
    }

    static Poly? PolyOf(JsonElement rings)
    {
        if (rings.ValueKind != JsonValueKind.Array) return null;
        var pl = new Poly();
        int i = 0;
        foreach (var r in rings.EnumerateArray())
        {
            var pts = Ring(r);
            if (pts.Count < 3) { i++; continue; }
            if (i == 0) pl.Shell = pts; else pl.Holes.Add(pts);
            i++;
        }
        return pl.Shell.Count >= 3 ? pl : null;
    }

    /// <summary>GeoJSON 도형 → 다각형들(Polygon · MultiPolygon).</summary>
    public static List<Poly> Polys(JsonElement geom)
    {
        var res = new List<Poly>();
        if (geom.ValueKind != JsonValueKind.Object || !geom.TryGetProperty("coordinates", out var c)) return res;
        string t = Str(geom, "type");
        if (t == "Polygon") { if (PolyOf(c) is Poly p) res.Add(p); }
        else if (t == "MultiPolygon") foreach (var pc in c.EnumerateArray()) if (PolyOf(pc) is Poly p) res.Add(p);
        return res;
    }

    /// <summary>데이터 API 응답 한 쪽. 꼴이 아니면 Status = "" · Error = 글 앞부분.</summary>
    public static DataPage ParseDataPage(string json)
    {
        var dp = new DataPage();
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("response", out var r)) { dp.Error = Head(json); return dp; }
            dp.Status = Str(r, "status");
            if (r.TryGetProperty("error", out var er)) dp.Error = (Str(er, "code") + " " + Str(er, "text")).Trim();
            if (r.TryGetProperty("record", out var rec)) dp.Total = Int(rec, "total");
            if (r.TryGetProperty("page", out var pg)) { dp.Pages = Int(pg, "total"); dp.Page = Int(pg, "current"); }
            if (dp.Status != "OK") return dp;
            if (!r.TryGetProperty("result", out var res) || !res.TryGetProperty("featureCollection", out var fc) || !fc.TryGetProperty("features", out var fs)) return dp;
            foreach (var f in fs.EnumerateArray())
            {
                var pr = f.TryGetProperty("properties", out var pp) ? pp : default;
                var raw = new Raw
                {
                    Pnu = Str(pr, "pnu"), JibunRaw = Str(pr, "jibun"), Bonbun = Str(pr, "bonbun"), Bubun = Str(pr, "bubun"), Addr = Str(pr, "addr"),
                    Jiga = Num(pr, "jiga"),
                };
                string gy = Str(pr, "gosi_year"), gm = Str(pr, "gosi_month");
                if (gy.Length > 0) raw.GosiYm = gy + (gm.Length > 0 ? "-" + gm : "");
                if (f.TryGetProperty("geometry", out var g)) raw.Polys = Polys(g);
                dp.Items.Add(raw);
            }
        }
        catch (Exception ex) when (BadJson(ex)) { dp.Status = ""; dp.Error = Head(json); }
        return dp;
    }

    /// <summary>소유 WFS 한 줄.</summary>
    public sealed class OwnRow
    {
        public string Pnu = "", Code = "", ChgDate = "", JimokCode = "";
        public int? Cnrs;
        public double? Area;
        /// <summary>필지 범위(WFS가 도형 없이도 줌) — x0, y0, x1, y1 · 없으면 null.</summary>
        public double[]? Box;
    }

    public sealed class WfsPage
    {
        public bool Ok;
        public string Error = "";
        public int Matched = -1, Returned;
        public List<OwnRow> Items = new();
    }

    /// <summary>«19590303» → «1959-03-03».</summary>
    public static string DateText(string d) => d.Length == 8 && d.All(char.IsAsciiDigit) ? $"{d[..4]}-{d.Substring(4, 2)}-{d.Substring(6, 2)}" : d;

    /// <summary>소유 WFS 응답(GeoJSON · 도형 없이 받아도 됨). 오류 응답(XML 등)은 Ok = 거짓 · Error = 앞부분.</summary>
    public static WfsPage ParseOwnershipWfs(string json)
    {
        var wp = new WfsPage();
        try
        {
            using var doc = JsonDocument.Parse(json);
            var r = doc.RootElement;
            if (!r.TryGetProperty("features", out var fs) || fs.ValueKind != JsonValueKind.Array) { wp.Error = Head(json); return wp; }
            wp.Ok = true;
            if (r.TryGetProperty("numberMatched", out var nm) && nm.ValueKind == JsonValueKind.Number) wp.Matched = nm.GetInt32();
            foreach (var f in fs.EnumerateArray())
            {
                var pr = f.TryGetProperty("properties", out var pp) ? pp : default;
                var row = new OwnRow { Pnu = Str(pr, "pnu"), Code = Str(pr, "posesn_se_code"), ChgDate = DateText(Str(pr, "ownship_chg_de")), JimokCode = Str(pr, "lndcgr_code"), Area = Num(pr, "lndpcl_ar") };
                if (int.TryParse(Str(pr, "cnrs_psn_co"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int cn)) row.Cnrs = cn;
                if (f.TryGetProperty("bbox", out var bb) && bb.ValueKind == JsonValueKind.Array && bb.GetArrayLength() >= 4)
                {
                    var v = new double[4];
                    for (int i = 0; i < 4; i++) v[i] = bb[i].ValueKind == JsonValueKind.Number ? bb[i].GetDouble() : double.NaN;
                    if (v.All(double.IsFinite) && v[2] >= v[0] && v[3] >= v[1]) row.Box = v;
                }
                if (row.Pnu.Length > 0) wp.Items.Add(row);
            }
            wp.Returned = wp.Items.Count;
            if (r.TryGetProperty("numberReturned", out var nr) && nr.ValueKind == JsonValueKind.Number) wp.Returned = Math.Max(wp.Returned, nr.GetInt32());
        }
        catch (Exception ex) when (BadJson(ex)) { wp.Ok = false; wp.Error = Head(json); }
        return wp;
    }

    /// <summary>소유 WFS가 상자를 다 못 담았나(1,000개가 참 · 나눠 다시) — startIndex를 안 들어서(1029 실측).</summary>
    public static bool WfsFull(WfsPage p, int max) => p.Returned >= max || (p.Matched >= 0 && p.Matched > p.Returned);

    /// <summary>필지별 소유(getPossessionAttr) — 소유자마다 한 줄의 구분 코드. <c>field</c>가 배열이 아니라 하나일 때도. 총 줄 수는 <paramref name="total"/>.
    /// 꼴이 아니면 null(오류 글은 <paramref name="error"/>).</summary>
    public static List<string>? ParseOwnerCodes(string json, out int total, out string error)
    {
        total = 0; error = "";
        try
        {
            using var doc = JsonDocument.Parse(json);
            // ★자료가 없는 필지는 «possessions» 없이 «response»(totalCount 0)로 온다(1029 실측 — 공유인수도 없는 필지) — 실패가 아니라 빈 목록
            if (!doc.RootElement.TryGetProperty("possessions", out var p))
            {
                if (doc.RootElement.TryGetProperty("response", out var r0) && Str(r0, "totalCount") == "0") return new List<string>();
                error = Head(json); return null;
            }
            total = Int(p, "totalCount");
            var res = new List<string>();
            if (p.TryGetProperty("field", out var f))
            {
                if (f.ValueKind == JsonValueKind.Array) { foreach (var e in f.EnumerateArray()) Add(e); }
                else if (f.ValueKind == JsonValueKind.Object) Add(f);
            }
            return res;
            void Add(JsonElement e) { string c = Str(e, "posesnSeCode"); if (c.Length > 0) res.Add(c); }
        }
        catch (Exception ex) when (BadJson(ex)) { error = Head(json); return null; }
    }

    /// <summary>WFS 줄들을 PNU로 — 한 PNU 여러 줄이면 코드를 다 모음(계획 L2).</summary>
    public static Dictionary<string, OwnInfo> MergeOwnership(IEnumerable<OwnRow> rows)
    {
        var d = new Dictionary<string, OwnInfo>(StringComparer.Ordinal);
        foreach (var r in rows)
        {
            if (!d.TryGetValue(r.Pnu, out var o)) d[r.Pnu] = o = new OwnInfo();
            if (r.Code.Length > 0 && !o.Codes.Contains(r.Code)) o.Codes.Add(r.Code);
            o.Cnrs ??= r.Cnrs;
            if (o.ChgDate.Length == 0) o.ChgDate = r.ChgDate;
            o.LedgerArea ??= r.Area;
            if (o.JimokCode.Length == 0) o.JimokCode = r.JimokCode;
        }
        return d;
    }

    /// <summary>★★[v110.2.1 · JACK 1029 «국공유지 내용은 없는 것 같아»] 지적 자료 필지마다 소유 정보를 짝짓는다(열쇠 = 지적 자료 PNU).
    /// <para>① PNU가 같으면 그 줄들 ② 다르면 — 대장 구분 · 본번 · 부번(PNU 뒤 9자리)이 같고 필지 안의 한 점이 소유 쪽 필지 범위 안인 것(여럿이면 범위가 가장 작은 것).
    /// 원인(1029 실측): 법정동 코드가 바뀐 곳은 두 자료가 다른 코드를 쓴다 — 구가 새로 생긴 시(2026): 지적 «새 시군구 · 새 읍면동 …» · 소유 «옛 시군구 · 옛 읍면동 …»(본번 · 부번은 같음) → PNU로만 맞추면 현장 44필지가 다 «소유 자료 없음».</para>
    /// <para>못 맞춘 필지는 빈 정보(OwnPnu = 지적 PNU — 필지별 조회를 그것으로 해 봄).</para></summary>
    public static Dictionary<string, OwnInfo> MatchOwnership(IEnumerable<Raw> raws, IEnumerable<OwnRow> rows, out int byPlace, out int unmatched)
    {
        byPlace = 0; unmatched = 0;
        var rowList = rows.ToList();
        var byPnu = MergeOwnership(rowList);
        foreach (var kv in byPnu) kv.Value.OwnPnu = kv.Key;
        // 소유 쪽 PNU마다 범위(여러 줄이면 합침) · 뒤 9자리로 묶음
        var box = new Dictionary<string, double[]>(StringComparer.Ordinal);
        foreach (var r in rowList)
        {
            if (r.Box == null) continue;
            if (!box.TryGetValue(r.Pnu, out var b)) box[r.Pnu] = (double[])r.Box.Clone();
            else { b[0] = Math.Min(b[0], r.Box[0]); b[1] = Math.Min(b[1], r.Box[1]); b[2] = Math.Max(b[2], r.Box[2]); b[3] = Math.Max(b[3], r.Box[3]); }
        }
        static string Tail(string p) => p.Length == 19 ? p.Substring(10) : "";
        var byTail = byPnu.Keys.Where(p => Tail(p).Length > 0).GroupBy(Tail).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
        var gf = NtsSupport.Factory();
        var res = new Dictionary<string, OwnInfo>(StringComparer.Ordinal);
        foreach (var raw in raws)
        {
            if (raw.Pnu.Length == 0 || res.ContainsKey(raw.Pnu)) continue;
            if (byPnu.TryGetValue(raw.Pnu, out var same)) { res[raw.Pnu] = same; continue; }
            OwnInfo? hit = null;
            if (byTail.TryGetValue(Tail(raw.Pnu), out var cands))
            {
                // 필지 안의 한 점(오목해도 안쪽)
                (double X, double Y)? pt = null;
                try
                {
                    Polygon? big = null;
                    foreach (var p in raw.Polys) if (MakePolygon(gf, p) is Polygon pg && (big == null || pg.Area > big.Area)) big = pg;
                    if (big != null) { var g = big.IsValid ? (Geometry)big : GeometryFixer.Fix(big); var ip = g.InteriorPoint; pt = (ip.X, ip.Y); }
                }
                catch { }
                double bestA = double.MaxValue;
                foreach (var c in cands)
                {
                    if (pt == null || !box.TryGetValue(c, out var b)) continue;
                    const double tol = 0.5;
                    if (pt.Value.X < b[0] - tol || pt.Value.X > b[2] + tol || pt.Value.Y < b[1] - tol || pt.Value.Y > b[3] + tol) continue;
                    double a = (b[2] - b[0]) * (b[3] - b[1]);
                    if (a < bestA) { bestA = a; hit = byPnu[c]; }
                }
            }
            if (hit != null) { res[raw.Pnu] = hit; byPlace++; }
            else { res[raw.Pnu] = new OwnInfo { OwnPnu = raw.Pnu }; unmatched++; }
        }
        return res;
    }

    /// <summary>★[설치 전 검토 · 낮음] 꼴이 다른 응답(맨 바깥이 객체가 아님 · 좌표가 숫자가 아님)은 JsonException이 아니라 InvalidOperationException 등이 난다 — 다 «꼴 아님»으로.</summary>
    static bool BadJson(Exception ex) => ex is JsonException or InvalidOperationException or FormatException or ArgumentException or IndexOutOfRangeException or KeyNotFoundException;

    static string Head(string s)
    {
        s = System.Text.RegularExpressions.Regex.Replace(s ?? "", @"\s+", " ").Trim();
        return s.Length > 120 ? s.Substring(0, 120) + "…" : s;
    }

    // ── 합치기 · 자르기 ──────────────────────────────────────────────────────

    public sealed class Piece
    {
        public List<(double X, double Y)> Shell = new();               // 닫는 점 없이
        public List<List<(double X, double Y)>> Holes = new();
        public double Area;
    }

    /// <summary>범위로 자른 필지 하나(PNU 하나).</summary>
    public sealed class Parcel
    {
        public string Pnu = "", Jibun = "", Jimok = "", Addr = "", GosiYm = "";
        public double? Jiga;
        public OwnInfo Own = new();
        public List<Piece> Pieces = new();
        public (double X, double Y) Label;
        public string JimokName => VWorldParcels.JimokName(Jimok);
        /// <summary>지적도 글자 «645-1전».</summary>
        public string LabelText => Jibun + Jimok;
    }

    public sealed class Result
    {
        public List<Parcel> Parcels = new();
        public int DupRows;          // 같은 PNU가 상자 경계에서 두 번 온 것
        public int Fixed;            // 고친 도형(자기교차 등 — GeometryFixer)
        public int Skipped;          // 고쳐도 못 쓴 것 · 범위 밖
        public int JibunMismatch;    // jibun 글자와 PNU가 말하는 지번이 다름(PNU를 씀)
        public int NoOwn;            // 소유 자료 없음
        public int Public, Private, Mixed;
    }

    /// <summary>받은 줄들 → PNU로 합침(상자 경계 중복 · L1) → 범위로 자름(조각마다 · 지번 글자는 가장 큰 조각의 안쪽 점 — 오목해도 안쪽) → 소유 붙임.
    /// 자기교차 등 틀린 도형은 GeometryFixer로 고쳐 쓰고 셈(M4) · 고쳐도 비면 건너뛰고 셈.</summary>
    public static Result Assemble(IEnumerable<Raw> raws, IReadOnlyDictionary<string, OwnInfo> own, Box clip)
    {
        var res = new Result();
        var gf = NtsSupport.Factory();
        var env = gf.ToGeometry(new Envelope(clip.X0, clip.X1, clip.Y0, clip.Y1));
        var byPnu = new Dictionary<string, (Raw First, List<Poly> Polys)>(StringComparer.Ordinal);
        var order = new List<string>();
        int anon = 0;
        foreach (var r in raws)
        {
            string key = r.Pnu.Length > 0 ? r.Pnu : "?" + (anon++).ToString(CultureInfo.InvariantCulture);
            if (byPnu.TryGetValue(key, out var have)) { res.DupRows++; continue; }   // 같은 필지 = 같은 도형(상자마다 통째로 옴)
            byPnu[key] = (r, r.Polys);
            order.Add(key);
        }
        foreach (string key in order)
        {
            var (r, polys) = byPnu[key];
            Geometry g;
            try
            {
                var ps = new List<Polygon>();
                foreach (var p in polys) if (MakePolygon(gf, p) is Polygon pg) ps.Add(pg);
                if (ps.Count == 0) { res.Skipped++; continue; }
                g = ps.Count == 1 ? ps[0] : gf.CreateMultiPolygon(ps.ToArray());
                if (!g.IsValid) { g = GeometryFixer.Fix(g); res.Fixed++; }
                g = g.Intersection(env);
            }
            catch { res.Skipped++; continue; }
            var pc = new Parcel { Pnu = r.Pnu, Addr = r.Addr, Jiga = r.Jiga, GosiYm = r.GosiYm };
            Polygon? big = null;
            for (int i = 0; i < g.NumGeometries; i++)
            {
                if (g.GetGeometryN(i) is not Polygon pg || pg.Area < 1e-4) continue;
                var piece = new Piece { Shell = Coords(pg.ExteriorRing), Area = pg.Area };
                for (int h = 0; h < pg.NumInteriorRings; h++) { var hr = Coords(pg.GetInteriorRingN(h)); if (hr.Count >= 3) piece.Holes.Add(hr); }
                if (piece.Shell.Count < 3) continue;
                pc.Pieces.Add(piece);
                if (big == null || pg.Area > big.Area) big = pg;
            }
            if (big == null) { res.Skipped++; continue; }
            var ip = big.InteriorPoint;
            pc.Label = (ip.X, ip.Y);
            // 지번 — PNU가 말하는 것(꼴이 맞으면) · jibun 글자와 견줌(M5)
            var (jb, jm) = SplitJibun(r.JibunRaw);
            string fromPnu = JibunFromPnu(r.Pnu);
            if (fromPnu.Length > 0)
            {
                if (jb.Length > 0 && jb != fromPnu) res.JibunMismatch++;
                pc.Jibun = fromPnu;
            }
            else pc.Jibun = jb;
            if (own.TryGetValue(r.Pnu, out var o)) pc.Own = o;
            pc.Jimok = jm.Length > 0 ? jm : JimokSymOfCode(pc.Own.JimokCode);
            switch (pc.Own.Kind)
            {
                case OwnKind.Public: res.Public++; break;
                case OwnKind.Private: res.Private++; break;
                case OwnKind.Mixed: res.Mixed++; break;
                default: res.NoOwn++; break;
            }
            res.Parcels.Add(pc);
        }
        return res;
    }

    static Polygon? MakePolygon(GeometryFactory gf, Poly p)
    {
        var shell = RingOf(gf, p.Shell);
        if (shell == null) return null;
        var holes = new List<LinearRing>();
        foreach (var h in p.Holes) if (RingOf(gf, h) is LinearRing lr) holes.Add(lr);
        return gf.CreatePolygon(shell, holes.ToArray());
    }

    static LinearRing? RingOf(GeometryFactory gf, List<(double X, double Y)> pts)
    {
        var cs = new List<Coordinate>(pts.Count + 1);
        foreach (var (x, y) in pts)
        {
            var c = new Coordinate(gf.PrecisionModel.MakePrecise(x), gf.PrecisionModel.MakePrecise(y));
            if (cs.Count == 0 || !cs[^1].Equals2D(c)) cs.Add(c);
        }
        if (cs.Count >= 2 && cs[0].Equals2D(cs[^1])) cs.RemoveAt(cs.Count - 1);
        if (cs.Count < 3) return null;
        cs.Add(cs[0].Copy());
        return gf.CreateLinearRing(cs.ToArray());
    }

    static List<(double X, double Y)> Coords(LineString ring)
    {
        var res = new List<(double, double)>(ring.NumPoints);
        var cs = ring.Coordinates;
        int n = cs.Length;
        if (n >= 2 && cs[0].Equals2D(cs[n - 1])) n--;
        for (int i = 0; i < n; i++) res.Add((cs[i].X, cs[i].Y));
        return res;
    }

    /// <summary>점이 필지 조각 안인가(구멍 빼고) — 3D 풍선 · 시험 공용.</summary>
    public static bool Inside(Piece p, double x, double y)
    {
        if (!InRing(p.Shell, x, y)) return false;
        foreach (var h in p.Holes) if (InRing(h, x, y)) return false;
        return true;
    }

    static bool InRing(List<(double X, double Y)> r, double x, double y)
    {
        bool c = false;
        for (int i = 0, j = r.Count - 1; i < r.Count; j = i++)
            if ((r[i].Y > y) != (r[j].Y > y) && x < (r[j].X - r[i].X) * (y - r[i].Y) / (r[j].Y - r[i].Y) + r[i].X) c = !c;
        return c;
    }
}
