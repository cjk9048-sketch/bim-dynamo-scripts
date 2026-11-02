using System.Globalization;
using System.Text.Json;

namespace DH.Grading.Core;

/// <summary>★★[v111.7 · JACK 1102 «3D 보기에 건물 · 층수로 높이 · 켜고 끄기»] <b>[3D 보기] 건물</b> — VWorld 건물정보(2D 데이터 API <c>LT_C_BLDGINFO</c> · GIS건물통합정보) 받은 글 읽기 ·
/// 높이 · 바닥 높이 · 바뀌는 자리 닿음 · 장면 배열. 인터넷 · 도면은 Civil 쪽(<c>VWorldBuildings</c>) — 여기는 받은 글과 높이 함수만.
/// <para>규칙(JACK 1102): 높이 = 지상층수 × <see cref="FloorHeight"/> m · 지상층수 0 · 빈칸 · 못 읽음 = 안 그림(셈만) ·
/// 바닥 = 발자국 아래 원지반 <b>가장 낮은 곳</b>(꼭짓점 + 둘레 2 m마다 + 안쪽 격자 — 높이가 하나도 없으면 안 그림) ·
/// 바뀌는 자리(계획부지 · 진입도로)에 닿으면 «닿음»(뷰어 = 빨강 반투명).</para>
/// <para>응답 꼴(1102 실측 · 공개 장소): 피처 <c>id</c> «LT_C_BLDGINFO.n» · 도형 MultiPolygon · 속성 <c>grnd_flr</c>(글) · <c>ugrnd_flr</c> · <c>height</c> · <c>bld_nm</c> …
/// 상자에 걸친 건물은 상자 밖까지 통째로 온다 · 지상층수 «0»이 절반 가까이.</para></summary>
public static class Web3DBuildings
{
    /// <summary>층 높이(m) — JACK 1102.</summary>
    public const double FloorHeight = 3.5;
    /// <summary>둘레 · 안쪽 표본 간격(m) — 바뀐 칸(2 m)과 같은 굵기.</summary>
    public const double Step = 2.0;
    /// <summary>안쪽 격자 한도(건물 하나) — 큰 공장도 표본이 이보다 많지 않게 격자를 넓힌다.</summary>
    const int MaxInner = 2500;

    /// <summary>데이터 API 한 줄(건물 한 동).</summary>
    public sealed class Raw
    {
        public string Id = "", Floors = "";
        public List<VWorldParcels.Poly> Polys = new();
    }

    public sealed class DataPage
    {
        public string Status = "";             // OK · NOT_FOUND · ERROR
        public string Error = "";
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

    static string Head(string s)
    {
        s = System.Text.RegularExpressions.Regex.Replace(s ?? "", @"\s+", " ").Trim();
        return s.Length > 120 ? s.Substring(0, 120) + "…" : s;
    }

    /// <summary>데이터 API 응답 한 쪽(<see cref="VWorldParcels.ParseDataPage"/>와 같은 짜임). 꼴이 아니면 Status = "" · Error = 글 앞부분.
    /// 도형을 못 읽은 건물은 다각형 없이 넣는다(셈은 <see cref="Place"/>의 «모양 나쁨»).</summary>
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
                var raw = new Raw { Id = Str(f, "id"), Floors = Str(pr, "grnd_flr") };
                try { if (f.TryGetProperty("geometry", out var g)) raw.Polys = VWorldParcels.Polys(g); }
                catch (Exception ex) when (ex is InvalidOperationException or FormatException or IndexOutOfRangeException) { raw.Polys = new(); }
                dp.Items.Add(raw);
            }
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException or ArgumentException or KeyNotFoundException)
        { dp.Status = ""; dp.Error = Head(json); }
        return dp;
    }

    /// <summary>지상층수 글 → 층(1 이상 정수) · 0 · 빈칸 · 음수 · 소수 · 글자 = null(안 그림).</summary>
    public static int? Floors(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        if (!double.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double v) || !double.IsFinite(v)) return null;
        double n = Math.Round(v);
        return n >= 1 && n <= 1000 && Math.Abs(v - n) < 1e-6 ? (int)n : null;
    }

    /// <summary>그릴 건물 한 동.</summary>
    public sealed class Bldg
    {
        public string Id = "";
        public int Floors;
        /// <summary>바닥 높이(발자국 아래 원지반 가장 낮은 곳) · 높이(층 × 3.5 m).</summary>
        public double Base, Height;
        /// <summary>바뀌는 자리에 닿음.</summary>
        public bool Touch;
        /// <summary>다각형들 — 바깥 고리 반시계 · 구멍 시계(벽 법선이 바깥 · 지붕이 위) · 닫는 점 없음 · 겹친 점 없음.</summary>
        public List<VWorldParcels.Poly> Polys = new();
    }

    public sealed class Result
    {
        public List<Bldg> Items = new();
        /// <summary>받은 줄 · 같은 건물 두 번(상자 경계) · 층수 없음(0 · 빈칸 · 못 읽음) · 땅 높이 없음 · 모양 나쁨(다각형 없음) · 원지반이 없어 보이는 지형으로 바닥을 잰 것.</summary>
        public int Raws, Dup, NoFloor, NoGround, BadShape, Fallback;
        public int Touch => Items.Count(b => b.Touch);
        /// <summary>뺀 까닭 셈 — 페이지에도 나가는 짧은 글(숫자만).</summary>
        public string DropText => $"층수 없음 {NoFloor:N0} · 땅 높이 없음 {NoGround:N0} · 모양 이상 {BadShape:N0}";
        public string Text => $"{Items.Count:N0}동(바뀌는 자리에 닿음 {Touch:N0}) · {DropText}" + (Fallback > 0 ? $" · 원지반 없어 보이는 지형으로 바닥 {Fallback:N0}" : "")
            + $" · 받음 {Raws:N0}" + (Dup > 0 ? $"(겹쳐 받음 {Dup:N0})" : "");
    }

    /// <summary>같은 건물인가를 가르는 열쇠 — VWorld 피처 <c>id</c>(1102 실측: 쪽끼리 겹침 0 · 다시 받아도 같음 · 맞닿은 상자 둘에 온 건물은 id도 도형도 같음).
    /// id가 비었을 때만 도형 전체(1 cm 반올림) — 점 수 + 첫 점만 보면 서로 다른 건물이 겹친다(같은 실측: 1 km² 안 8쌍).</summary>
    internal static string Key(Raw r)
    {
        if (r.Id.Length > 0) return r.Id;
        var sb = new System.Text.StringBuilder("g:");
        foreach (var p in r.Polys)
            foreach (var ring in p.Holes.Prepend(p.Shell))
            {
                foreach (var (x, y) in ring) sb.Append(Math.Round(x, 2).ToString(CultureInfo.InvariantCulture)).Append(',').Append(Math.Round(y, 2).ToString(CultureInfo.InvariantCulture)).Append(';');
                sb.Append('|');
            }
        return sb.ToString();
    }

    /// <summary>받은 줄 → 그릴 건물. <paramref name="ground"/> = 원지반 높이(없으면 null) · <paramref name="changed"/> = 바뀌는 자리인가(없으면 닿음 없음) ·
    /// <paramref name="fallback"/> = 그 건물 표본 어디에도 원지반이 없을 때 다시 재는 높이(보이는 지형 — 없으면 «땅 높이 없음»).</summary>
    public static Result Place(IEnumerable<Raw> raws, Func<double, double, double?> ground, Func<double, double, bool>? changed,
                               Func<double, double, double?>? fallback = null, double step = Step)
    {
        var res = new Result();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var r in raws)
        {
            res.Raws++;
            if (!seen.Add(Key(r))) { res.Dup++; continue; }
            int? fl = Floors(r.Floors);
            if (fl == null) { res.NoFloor++; continue; }
            var polys = new List<VWorldParcels.Poly>();
            foreach (var p in r.Polys)
            {
                var shell = Clean(p.Shell, ccw: true);
                if (shell == null) continue;
                var q = new VWorldParcels.Poly { Shell = shell };
                foreach (var h in p.Holes) if (Clean(h, ccw: false) is { } hh) q.Holes.Add(hh);
                polys.Add(q);
            }
            if (polys.Count == 0) { res.BadShape++; continue; }
            double zMin = double.PositiveInfinity; bool touch = false;
            var samples = Samples(polys, step).ToList();
            foreach (var (x, y) in samples)
            {
                if (ground(x, y) is double z && double.IsFinite(z) && z < zMin) zMin = z;
                if (!touch && changed != null && changed(x, y)) touch = true;
            }
            // 원지반 밖 건물(서버 원지반 끝 · 원지반이 좁음) — 보이는 지형으로 한 번 더(JACK 1102 «원지반 없으면 보이는 지형»을 건물마다)
            if (!double.IsFinite(zMin) && fallback != null)
            {
                foreach (var (x, y) in samples)
                    if (fallback(x, y) is double z && double.IsFinite(z) && z < zMin) zMin = z;
                if (double.IsFinite(zMin)) res.Fallback++;
            }
            if (!double.IsFinite(zMin)) { res.NoGround++; continue; }
            res.Items.Add(new Bldg { Id = r.Id, Floors = fl.Value, Base = zMin, Height = fl.Value * FloorHeight, Touch = touch, Polys = polys });
        }
        return res;
    }

    // ── 받을 범위 — ★[v111.7.1 · JACK 1102 «주변 아파트가 빠짐»] 처음은 3D 지형 전체(위성 바탕과 같은 범위) · 건물이 한도보다 많으면
    //    바뀐 자리 + 500 → + 200 → + 50 → + 0 m(지형 안으로 자름) · 그다음에 가운데로(넓이 ∝ 그 범위 건물 수) ──

    /// <summary>★[v111.7.1] 바뀐 자리 둘레 여유 차례(m) — 지형 전체 다음.</summary>
    public static readonly double[] SiteMargins = { 500.0, 200.0, 50.0, 0.0 };

    /// <summary>★[v111.7.1] 받을 범위 차례 — 지형 전체 → 바뀐 자리 + 500 · 200 · 50 · 0 m(지형 안으로 자름 · 0 = «바뀐 자리»).
    /// 앞 범위보다 넓이가 줄지 않는 것은 뺀다(지형이 작으면 지형 전체 하나). 바뀐 자리가 없으면 지형 전체만. 그다음 줄이기는 <see cref="ShrinkAround"/>(가운데).</summary>
    public static List<(VWorldParcels.Box Box, string Label)> Ranges(VWorldParcels.Box terrain, VWorldParcels.Box? site)
    {
        var o = new List<(VWorldParcels.Box Box, string Label)> { (terrain, "지형 전체") };
        if (site is not VWorldParcels.Box s) return o;
        foreach (double m in SiteMargins)
        {
            var e = Expand(s, m);
            var c = new VWorldParcels.Box(Math.Max(e.X0, terrain.X0), Math.Max(e.Y0, terrain.Y0), Math.Min(e.X1, terrain.X1), Math.Min(e.Y1, terrain.Y1));
            if (!(c.W > 0 && c.H > 0)) c = e;                       // 지형 밖(있을 수 없음) — 자르지 않음
            if (c.Area < o[^1].Box.Area * (1 - 1e-9)) o.Add((c, m > 0 ? $"바뀐 자리 + {m:0} m" : "바뀐 자리"));
        }
        return o;
    }

    public static VWorldParcels.Box Expand(VWorldParcels.Box b, double m) => new(b.X0 - m, b.Y0 - m, b.X1 + m, b.Y1 + m);

    /// <summary>가운데로 줄임 — 넓이가 한도/건물 수의 0.81배(0.9²)가 되게 · 늘 줄어든다.</summary>
    public static VWorldParcels.Box ShrinkAround(VWorldParcels.Box b, long count, int max)
    {
        double f = Math.Min(0.9, Math.Sqrt((double)max / Math.Max(1, count)) * 0.9), cx = (b.X0 + b.X1) / 2, cy = (b.Y0 + b.Y1) / 2;
        return new VWorldParcels.Box(cx - b.W / 2 * f, cy - b.H / 2 * f, cx + b.W / 2 * f, cy + b.H / 2 * f);
    }

    static double Area2(List<(double X, double Y)> r)
    {
        double a = 0;
        for (int i = 0, j = r.Count - 1; i < r.Count; j = i++) a += (r[j].X - r[i].X) * (r[j].Y + r[i].Y);
        return a;   // 반시계면 양수(두 배 넓이)
    }

    /// <summary>닫는 점 · 겹친 점(1 mm 안) 빼고 방향 맞춤 — 점 셋 미만이거나 넓이 0.01 ㎡ 미만이면 null.</summary>
    internal static List<(double X, double Y)>? Clean(List<(double X, double Y)> ring, bool ccw)
    {
        var o = new List<(double X, double Y)>(ring.Count);
        foreach (var p in ring)
        {
            if (!double.IsFinite(p.X) || !double.IsFinite(p.Y)) continue;
            if (o.Count > 0 && Math.Abs(o[^1].X - p.X) < 1e-3 && Math.Abs(o[^1].Y - p.Y) < 1e-3) continue;
            o.Add(p);
        }
        while (o.Count > 1 && Math.Abs(o[0].X - o[^1].X) < 1e-3 && Math.Abs(o[0].Y - o[^1].Y) < 1e-3) o.RemoveAt(o.Count - 1);
        if (o.Count < 3) return null;
        double a2 = Area2(o);
        if (Math.Abs(a2) < 0.02) return null;
        if (a2 > 0 != ccw) o.Reverse();
        return o;
    }

    static bool InRing(List<(double X, double Y)> r, double x, double y)
    {
        bool inside = false;
        for (int i = 0, j = r.Count - 1; i < r.Count; j = i++)
            if ((r[i].Y > y) != (r[j].Y > y) && x < (r[j].X - r[i].X) * (y - r[i].Y) / (r[j].Y - r[i].Y) + r[i].X) inside = !inside;
        return inside;
    }

    /// <summary>표본 — 모든 고리(구멍 포함)의 꼭짓점 · 변을 따라 <paramref name="step"/>마다 · 안쪽 격자(칸 가운데 · 구멍 밖 · 큰 건물은 칸을 넓힘).</summary>
    internal static IEnumerable<(double X, double Y)> Samples(List<VWorldParcels.Poly> polys, double step)
    {
        foreach (var p in polys)
        {
            foreach (var r in p.Holes.Prepend(p.Shell))
                for (int i = 0; i < r.Count; i++)
                {
                    var a = r[i]; var b = r[(i + 1) % r.Count];
                    double len = Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));
                    int n = Math.Max(1, (int)Math.Ceiling(len / step));
                    for (int k = 0; k < n; k++) yield return (a.X + (b.X - a.X) * k / n, a.Y + (b.Y - a.Y) * k / n);
                }
            double x0 = p.Shell.Min(q => q.X), x1 = p.Shell.Max(q => q.X), y0 = p.Shell.Min(q => q.Y), y1 = p.Shell.Max(q => q.Y);
            double s = Math.Max(step, Math.Sqrt((x1 - x0) * (y1 - y0) / MaxInner));
            for (double y = y0 + s / 2; y < y1; y += s)
                for (double x = x0 + s / 2; x < x1; x += s)
                    if (InRing(p.Shell, x, y) && !p.Holes.Any(h => InRing(h, x, y))) yield return (x, y);
        }
    }

    /// <summary>장면 배열(뷰어 <c>web3d/src/extras.js buildBldg</c>와 짝) — 점은 원점을 뺀 float32 (x, y) 차례 ·
    /// <c>Ring[i]</c> = 고리 i의 첫 점 순번 · <c>Poly[j]</c> = 다각형 j의 첫 고리(첫 고리 = 바깥 · 나머지 = 구멍) · <c>Bldg[k]</c> = 건물 k의 첫 다각형 —
    /// 셋 다 끝에 «다음 첫 것»(전체 수)을 하나 더 붙인다 · 건물마다 바닥 높이 · 높이 · 닿음(0/1).</summary>
    public sealed class Arrays
    {
        public float[] Xy = Array.Empty<float>(), Z0 = Array.Empty<float>(), H = Array.Empty<float>();
        public int[] Ring = Array.Empty<int>(), Poly = Array.Empty<int>(), Bldg = Array.Empty<int>();
        public byte[] Hit = Array.Empty<byte>();
    }

    public static Arrays Pack(IReadOnlyList<Bldg> items, double ox, double oy)
    {
        var xy = new List<float>(); var ring = new List<int>(); var poly = new List<int>(); var bl = new List<int>();
        var z0 = new float[items.Count]; var h = new float[items.Count]; var hit = new byte[items.Count];
        for (int k = 0; k < items.Count; k++)
        {
            var b = items[k];
            bl.Add(poly.Count);
            foreach (var p in b.Polys)
            {
                poly.Add(ring.Count);
                foreach (var r in p.Holes.Prepend(p.Shell))
                {
                    ring.Add(xy.Count / 2);
                    foreach (var (x, y) in r) { xy.Add((float)(x - ox)); xy.Add((float)(y - oy)); }
                }
            }
            z0[k] = (float)b.Base; h[k] = (float)b.Height; hit[k] = b.Touch ? (byte)1 : (byte)0;
        }
        ring.Add(xy.Count / 2); poly.Add(ring.Count - 1); bl.Add(poly.Count - 1);
        return new Arrays { Xy = xy.ToArray(), Ring = ring.ToArray(), Poly = poly.ToArray(), Bldg = bl.ToArray(), Z0 = z0, H = h, Hit = hit };
    }
}
