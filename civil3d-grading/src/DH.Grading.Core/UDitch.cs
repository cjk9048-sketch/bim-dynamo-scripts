using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DH.Grading.Core;

/// <summary>★★[배수지 U형 측구 · 명세서 «배수지_U형측구_애드인_모듈별개발명세서.md» §7 1단계 MVP · JACK 1111 초안] 순수 셈 — Civil 없이 하네스(S260 UD)가 잰다.
/// <list type="bullet">
/// <item>M01 부지 대표점 · 경위도(<see cref="LonLat"/> · <see cref="Centroid"/>)</item>
/// <item>M02 강우자료 — 수동 입력 · CSV IDF 표(<see cref="ParseIdfCsv"/>). <b>강우값을 지어내지 않는다</b>(§9) — 없으면 «자료 미확보» · 표에 없는 지속기간 · 재현기간은 보간하지 않는다(§M02 «임의로 외삽하지 않는다»).</item>
/// <item>M05 종단(시작 · 끝 바닥고 → 한 경사 · <see cref="ProfileOf"/>) · 수리(합리식 Q = C·I·A/360 · Manning 직사각 · <see cref="Check"/>). <b>모델링과 수리는 따로</b>(§9) — 모델을 지어도 수리 OK가 아니다.</item>
/// <item>M06 3D 몸통(마디마다 맞댄 단면 · <see cref="Meshes"/>) · M08 수량 산근 · 보고서(<see cref="Report"/>).</item>
/// </list>
/// 단위 = m · 좌표 = 도면 WCS(m로 가정). 단면 꼴은 진입도로 측구와 같은 <see cref="DitchSection.U"/>(뚜껑 자리 없음 — 1단계).</summary>
public static partial class UDitch
{
    public const int RecordVersion = 1;
    static readonly CultureInfo Ic = CultureInfo.InvariantCulture;

    // ══════════════════════════════ 설정 · 규격 표 ══════════════════════════════

    /// <summary>표준 규격 한 줄 — 안폭 B · 안높이 H · 벽 Tw · 바닥 Tb · 기초 Tf(0 = 없음) · 기초 내민 폭 Pf (m).</summary>
    public sealed class Size
    {
        public string Name { get; set; } = "";
        public double B { get; set; }
        public double H { get; set; }
        public double Tw { get; set; }
        public double Tb { get; set; }
        public double Tf { get; set; } = 0.10;
        public double Pf { get; set; } = 0.10;
        [JsonIgnore] public string Label => Name.Length > 0 ? Name : string.Format(Ic, "{0:0}×{1:0}", B * 1000, H * 1000);
        /// <summary>진입도로 측구와 같은 단면 꼴(뚜껑 자리 없음).</summary>
        [JsonIgnore] public DitchSection.UDims Dims => new(B, H, Tw, Tb, Tf, Pf, 0, 0, false);
        public Size Copy() => (Size)MemberwiseClone();
    }

    /// <summary>프로젝트 기준값(§9 «기본값을 보편 기준처럼 고정하지 않는다» → 번들 «UDitch.settings.json»으로 고침).</summary>
    public sealed class Settings
    {
        public double ManningN { get; set; } = 0.013;
        /// <summary>허용 수심 = 안높이 × 이 값(여유고 = 나머지).</summary>
        public double DepthRatio { get; set; } = 0.8;
        public double MinSlopePct { get; set; } = 0.3;
        public double MaxSlopePct { get; set; } = 10.0;
        public double MinVelocity { get; set; } = 0.6;
        public double MaxVelocity { get; set; } = 3.0;
        /// <summary>바닥고 기본값 = 지표면 − 이 값 − 안높이(0 = 측구 윗면이 지표면).</summary>
        public double Cover { get; set; } = 0.0;
        public double DefaultC { get; set; } = 0.9;
        /// <summary>호(곡선)를 나누는 간격(m).</summary>
        public double ArcStep { get; set; } = 0.5;
        public List<Size> Sizes { get; set; } = DefaultSizes();
    }

    /// <summary>기본 규격 표(현장타설 U형 · 기초 100 · 양쪽 100 내밈). 300×300은 JACK 도면 «24_U형측구 상세도»(벽 · 바닥 150) — 나머지 두께는 초안 값(설정 파일에서 고침).</summary>
    public static List<Size> DefaultSizes() => new()
    {
        new() { Name = "300×300", B = 0.30, H = 0.30, Tw = 0.15, Tb = 0.15 },
        new() { Name = "400×400", B = 0.40, H = 0.40, Tw = 0.15, Tb = 0.15 },
        new() { Name = "500×500", B = 0.50, H = 0.50, Tw = 0.20, Tb = 0.20 },
        new() { Name = "600×600", B = 0.60, H = 0.60, Tw = 0.20, Tb = 0.20 },
        new() { Name = "800×800", B = 0.80, H = 0.80, Tw = 0.25, Tb = 0.25 },
        new() { Name = "1000×1000", B = 1.00, H = 1.00, Tw = 0.30, Tb = 0.30 },
    };

    static readonly JsonSerializerOptions Jo = new()
    {
        WriteIndented = true,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static string SettingsJson(Settings s) => JsonSerializer.Serialize(s, Jo);

    /// <summary>설정 파일 읽기 — 틀리면 기본값 + 까닭(빈 글 = 맞음). 규격 줄이 맞지 않으면 그 줄만 뺀다.</summary>
    public static Settings ParseSettings(string json, out string why)
    {
        why = "";
        Settings? s;
        try { s = JsonSerializer.Deserialize<Settings>(json, Jo); }
        catch (Exception ex) { why = "설정 파일을 못 읽음 — " + ex.Message; return new Settings(); }
        if (s == null) { why = "설정 파일이 비었음"; return new Settings(); }
        var bad = new List<string>();
        static bool Pos(double v) => double.IsFinite(v) && v > 0;
        if (!Pos(s.ManningN) || s.ManningN > 0.1) { bad.Add("ManningN"); s.ManningN = 0.013; }
        if (!Pos(s.DepthRatio) || s.DepthRatio > 1) { bad.Add("DepthRatio"); s.DepthRatio = 0.8; }
        if (!double.IsFinite(s.MinSlopePct) || s.MinSlopePct < 0) { bad.Add("MinSlopePct"); s.MinSlopePct = 0.3; }
        if (!Pos(s.MaxSlopePct) || s.MaxSlopePct <= s.MinSlopePct) { bad.Add("MaxSlopePct"); s.MaxSlopePct = Math.Max(10, s.MinSlopePct * 2); }
        if (!double.IsFinite(s.MinVelocity) || s.MinVelocity < 0) { bad.Add("MinVelocity"); s.MinVelocity = 0.6; }
        if (!Pos(s.MaxVelocity) || s.MaxVelocity <= s.MinVelocity) { bad.Add("MaxVelocity"); s.MaxVelocity = Math.Max(3.0, s.MinVelocity * 2); }
        if (!double.IsFinite(s.Cover) || s.Cover < 0 || s.Cover > 5) { bad.Add("Cover"); s.Cover = 0; }
        if (!Pos(s.DefaultC) || s.DefaultC > 1) { bad.Add("DefaultC"); s.DefaultC = 0.9; }
        if (!Pos(s.ArcStep) || s.ArcStep > 10) { bad.Add("ArcStep"); s.ArcStep = 0.5; }
        var ok = new List<Size>();
        foreach (var z in s.Sizes ?? new List<Size>())
        {
            if (z == null) continue;
            z.Name ??= "";
            if (DitchSection.Check(z.Dims).Length > 0) { bad.Add("규격 " + z.Label); continue; }
            ok.Add(z);
        }
        if (ok.Count == 0) { ok = DefaultSizes(); bad.Add("규격 표 비었음 → 기본 표"); }
        s.Sizes = ok;
        if (bad.Count > 0) why = "설정 값 고침(기본값으로): " + string.Join(" · ", bad);
        return s;
    }

    // ══════════════════════════════ 기록(도면 안 · JSON) ══════════════════════════════

    /// <summary>M01 — 부지 대표점(도면 좌표) · 경위도(WGS84 · 바꿀 수 없으면 NaN) · 좌표계 · 어디서 셌나.</summary>
    public sealed class Site
    {
        public double X { get; set; } = double.NaN;
        public double Y { get; set; } = double.NaN;
        public double Lon { get; set; } = double.NaN;
        public double Lat { get; set; } = double.NaN;
        public int Epsg { get; set; }
        public double AreaM2 { get; set; }
        public string Source { get; set; } = "";
    }

    /// <summary>M02 — 적용 강우(하나 · 도면 전체). <see cref="IntensityMmh"/> ≤ 0 = 자료 미확보(수리검토 안 함).</summary>
    public sealed class Rain
    {
        public double ReturnYears { get; set; }
        public double DurationMin { get; set; }
        public double IntensityMmh { get; set; }
        /// <summary>출처(문서 · 기관 · 관측소/지역).</summary>
        public string Source { get; set; } = "";
        /// <summary>기준연도 · 버전.</summary>
        public string SourceVersion { get; set; } = "";
        /// <summary>«수동» · «CSV».</summary>
        public string Method { get; set; } = "";
        public string File { get; set; } = "";
        public string Entered { get; set; } = "";
        [JsonIgnore] public bool Has => double.IsFinite(IntensityMmh) && IntensityMmh > 0;
        [JsonIgnore] public string Text => Has
            ? string.Format(Ic, "{0:0.#}년 · {1:0.#}분 · I = {2:0.0} mm/h", ReturnYears, DurationMin, IntensityMmh) + (Source.Length > 0 ? " · " + Source : "") + (SourceVersion.Length > 0 ? " (" + SourceVersion + ")" : "")
            : "자료 미확보";
    }

    /// <summary>M04 · M05 — 측구 하나. <see cref="Pts"/> = [x, y, 지표면 z(NaN = 못 읽음)] 상류 → 하류(호는 나눈 점).</summary>
    public sealed class Ditch
    {
        public string Id { get; set; } = "D-001";
        public string LineHandle { get; set; } = "";
        public List<double[]> Pts { get; set; } = new();
        public string Surface { get; set; } = "";
        public Size Size { get; set; } = DefaultSizes()[0];
        public double StartInvert { get; set; } = double.NaN;
        public double EndInvert { get; set; } = double.NaN;
        /// <summary>참 = 바닥고가 지표면 · 규격을 따라감(손으로 친 뒤엔 거짓).</summary>
        public bool InvertAuto { get; set; } = true;
        public double AreaHa { get; set; }
        public double C { get; set; } = 0.9;
        /// <summary>지은 솔리드 핸들(16진) — 다시 지을 때 지움.</summary>
        public List<string> Solids { get; set; } = new();
    }

    public sealed class Record
    {
        public int Version { get; set; } = RecordVersion;
        public Site Site { get; set; } = new();
        public Rain Rain { get; set; } = new();
        public List<Ditch> Ditches { get; set; } = new();
        public string Updated { get; set; } = "";
    }

    public static string ToJson(Record r) => JsonSerializer.Serialize(r, Jo);

    public static bool TryParse(string json, out Record r, out string why)
    {
        r = new Record(); why = "";
        try
        {
            var x = JsonSerializer.Deserialize<Record>(json, Jo);
            if (x == null) { why = "기록이 비었음"; return false; }
            if (x.Version > RecordVersion) { why = $"새 판 기록(판 {x.Version}) — 이 애드인은 판 {RecordVersion}까지"; return false; }
            x.Site ??= new(); x.Rain ??= new(); x.Ditches ??= new();
            foreach (var d in x.Ditches)
            {
                d.Pts ??= new(); d.Solids ??= new(); d.Size ??= DefaultSizes()[0]; d.Id ??= ""; d.LineHandle ??= ""; d.Surface ??= "";
                d.Pts = d.Pts.Where(p => p != null && p.Length >= 2).Select(p => p.Length >= 3 ? p : new[] { p[0], p[1], double.NaN }).ToList();
            }
            r = x;
            return true;
        }
        catch (Exception ex) { why = "U형 측구 기록을 못 읽음 — " + ex.Message; return false; }
    }

    /// <summary>다음 측구 이름 «D-00n»(있는 것 다음 번호).</summary>
    public static string NextId(IEnumerable<Ditch> ds)
    {
        int max = 0;
        foreach (var d in ds)
            if (d.Id.StartsWith("D-", StringComparison.Ordinal) && int.TryParse(d.Id[2..], NumberStyles.Integer, Ic, out int k)) max = Math.Max(max, k);
        return string.Format(Ic, "D-{0:000}", max + 1);
    }

    // ══════════════════════════════ M01 부지 대표점 ══════════════════════════════

    /// <summary>닫힌 고리들의 넓이 무게중심(겹침은 따지지 않음 — 대표점 용) · 넓이 합. 고리가 없거나 넓이 0이면 null.</summary>
    public static (double X, double Y, double Area)? Centroid(IEnumerable<IReadOnlyList<(double X, double Y)>> rings)
    {
        double sa = 0, sx = 0, sy = 0;
        foreach (var r in rings)
        {
            if (r == null || r.Count < 3) continue;
            double x0 = r[0].X, y0 = r[0].Y, a = 0, cx = 0, cy = 0;   // 첫 점 기준(대좌표 지움)
            for (int i = 0; i < r.Count; i++)
            {
                var p = r[i]; var q = r[(i + 1) % r.Count];
                double px = p.X - x0, py = p.Y - y0, qx = q.X - x0, qy = q.Y - y0, cr = px * qy - qx * py;
                a += cr; cx += (px + qx) * cr; cy += (py + qy) * cr;
            }
            if (Math.Abs(a) < 1e-9) continue;
            a /= 2; cx = cx / (6 * a) + x0; cy = cy / (6 * a) + y0;
            double w = Math.Abs(a);
            sa += w; sx += cx * w; sy += cy * w;
        }
        if (sa <= 0) return null;
        return (sx / sa, sy / sa, sa);
    }

    /// <summary>도면 좌표 → WGS84 경위도(도). 한국 TM(5180~5188) · UTM-K(5179)만 — 그 밖은 null(§M01 «변환 실패면 사용자 확인»).</summary>
    public static (double Lon, double Lat)? LonLat(double x, double y, int epsg)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y)) return null;
        if (epsg == 5179) return KoreaTm.UtmkToLonLat(x, y);
        var b = ShapefileWriter.Belt(epsg);
        if (b == null) return null;
        var ll = KoreaTm.ToLonLat(x, y, b.Value.cm, b.Value.fn);
        if (!double.IsFinite(ll.Lon) || !double.IsFinite(ll.Lat) || ll.Lat < 30 || ll.Lat > 45 || ll.Lon < 120 || ll.Lon > 135) return null;   // 한반도 밖 = 좌표계 · 단위가 틀렸을 것
        return ll;
    }

    // ══════════════════════════════ M02 강우 CSV ══════════════════════════════

    public sealed record IdfRow(double DurationMin, double ReturnYears, double IntensityMmh);

    /// <summary>CSV IDF 표 읽기 — 두 꼴:
    /// <para>① 긴 꼴: 머리 «duration_min, return_period, intensity_mmh»(또는 «지속기간, 재현기간, 강우강도») · 머리가 없으면 이 차례.</para>
    /// <para>② 넓은 꼴: 머리 «지속기간, 2년, 5년, 10년, …» · 줄마다 «10, 120.5, 150.2, …».</para>
    /// «# 출처: …» · «# source: …» 주석 줄 = 출처 · 구분자 = 쉼표 · 세미콜론 · 탭. 틀린 줄은 <paramref name="errors"/>에(그 줄만 뺌).</summary>
    public static List<IdfRow> ParseIdfCsv(string text, out string source, out List<string> errors)
    {
        var rows = new List<IdfRow>(); errors = new(); source = "";
        if (string.IsNullOrWhiteSpace(text)) { errors.Add("빈 파일"); return rows; }
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        string[]? head = null;
        int iDur = 0, iRet = 1, iInt = 2;
        double[]? wide = null;
        for (int ln = 0; ln < lines.Length; ln++)
        {
            string raw = lines[ln].Trim().TrimStart('﻿');
            if (raw.Length == 0) continue;
            if (raw.StartsWith('#'))
            {
                string c = raw.TrimStart('#').Trim();
                foreach (var key in new[] { "출처:", "출처 :", "source:", "Source:", "SOURCE:" })
                    if (c.StartsWith(key, StringComparison.Ordinal)) { source = c[key.Length..].Trim(); break; }
                continue;
            }
            char sep = raw.Contains('\t') ? '\t' : raw.Contains(';') ? ';' : ',';
            var f = raw.Split(sep).Select(s => s.Trim().Trim('"')).ToArray();
            if (head == null && rows.Count == 0 && f.Any(s => s.Length > 0 && !double.TryParse(s, NumberStyles.Float, Ic, out _)))
            {
                head = f;
                int Find(params string[] keys) { for (int i = 0; i < f.Length; i++) foreach (var k in keys) if (f[i].Contains(k, StringComparison.OrdinalIgnoreCase)) return i; return -1; }
                int d = Find("duration", "지속"), r = Find("return", "재현", "빈도"), it = Find("intensity", "강도", "mm/h", "mmh");
                if (d >= 0 && r >= 0 && it >= 0) { iDur = d; iRet = r; iInt = it; continue; }
                // 넓은 꼴 — 첫 칸 = 지속기간 · 나머지 칸 = 재현기간(년)
                var w = new double[f.Length];
                bool okWide = f.Length >= 2;
                for (int i = 1; i < f.Length && okWide; i++)
                {
                    string t = f[i].Replace("년", "").Replace("yr", "", StringComparison.OrdinalIgnoreCase).Replace("y", "", StringComparison.OrdinalIgnoreCase).Trim();
                    okWide = double.TryParse(t, NumberStyles.Float, Ic, out w[i]) && w[i] > 0;
                }
                if (okWide) { wide = w; continue; }
                errors.Add($"{ln + 1}줄: 머리를 모름 — «duration_min, return_period, intensity_mmh» 또는 «지속기간, 2년, 5년, …»");
                return rows;
            }
            if (wide != null)
            {
                if (!double.TryParse(f[0], NumberStyles.Float, Ic, out double dur) || dur <= 0) { errors.Add($"{ln + 1}줄: 지속기간 «{f[0]}»"); continue; }
                for (int i = 1; i < wide.Length; i++)
                {
                    if (i >= f.Length || f[i].Length == 0) continue;
                    if (double.TryParse(f[i], NumberStyles.Float, Ic, out double v) && v > 0 && double.IsFinite(v)) rows.Add(new IdfRow(dur, wide[i], v));
                    else errors.Add($"{ln + 1}줄 {i + 1}칸: 강우강도 «{f[i]}»");
                }
                continue;
            }
            int need = Math.Max(iDur, Math.Max(iRet, iInt));
            if (f.Length <= need) { errors.Add($"{ln + 1}줄: 칸이 모자람"); continue; }
            if (double.TryParse(f[iDur], NumberStyles.Float, Ic, out double a) && double.TryParse(f[iRet], NumberStyles.Float, Ic, out double b) && double.TryParse(f[iInt], NumberStyles.Float, Ic, out double c2)
                && a > 0 && b > 0 && c2 > 0 && double.IsFinite(a) && double.IsFinite(b) && double.IsFinite(c2))
                rows.Add(new IdfRow(a, b, c2));
            else errors.Add($"{ln + 1}줄: 숫자가 아니거나 0 이하");
        }
        // 같은 (지속기간, 재현기간)이 둘 — 앞 것만 · 알림
        var seen = new HashSet<(double, double)>();
        var outRows = new List<IdfRow>();
        foreach (var r in rows)
            if (seen.Add((r.DurationMin, r.ReturnYears))) outRows.Add(r);
            else errors.Add(string.Format(Ic, "겹침: {0:0.#}분 · {1:0.#}년 — 앞 줄만 씀", r.DurationMin, r.ReturnYears));
        if (outRows.Count == 0 && errors.Count == 0) errors.Add("강우 줄이 없음");
        return outRows.OrderBy(r => r.ReturnYears).ThenBy(r => r.DurationMin).ToList();
    }

    /// <summary>표에서 정확히 그 줄만(보간 · 외삽 안 함 — §M02). 없으면 null.</summary>
    public static IdfRow? Find(IReadOnlyList<IdfRow> rows, double years, double minutes)
        => rows.FirstOrDefault(r => Math.Abs(r.ReturnYears - years) < 1e-6 && Math.Abs(r.DurationMin - minutes) < 1e-6);

    // ══════════════════════════════ M05 종단 ══════════════════════════════

    public sealed class Profile
    {
        public double Length;
        public double[] Sta = Array.Empty<double>(), Inv = Array.Empty<double>(), Top = Array.Empty<double>(), Ground = Array.Empty<double>();
        /// <summary>(시작 − 끝) / 길이 × 100 — 양수 = 하류로 내려감.</summary>
        public double SlopePct = double.NaN;
        /// <summary>윗면이 지표면 위로 나온 최대(m · 0 = 안 나옴) · 지표면 아래로 묻힌 최대(m).</summary>
        public double MaxAbove, MaxBelow;
        public double MaxDeflectDeg;
        public readonly List<string> Warn = new();
        public string Error = "";
    }

    /// <summary>평면 점(1 mm 안 겹침 뺌) — [x, y, 지표면 z].</summary>
    public static List<double[]> CleanPts(IReadOnlyList<double[]> pts)
    {
        var o = new List<double[]>();
        foreach (var p in pts)
        {
            if (p == null || p.Length < 2 || !double.IsFinite(p[0]) || !double.IsFinite(p[1])) continue;
            if (o.Count > 0 && Math.Abs(p[0] - o[^1][0]) < 1e-3 && Math.Abs(p[1] - o[^1][1]) < 1e-3) continue;
            o.Add(new[] { p[0], p[1], p.Length > 2 ? p[2] : double.NaN });
        }
        return o;
    }

    /// <summary>바닥고 기본값 = 끝 지표면 − 덮개 − 안높이(mm 반올림 · 지표면 없음 = NaN).</summary>
    public static (double Start, double End) DefaultInverts(Ditch d, Settings s)
    {
        var p = CleanPts(d.Pts);
        if (p.Count < 2) return (double.NaN, double.NaN);
        double R(double g) => double.IsFinite(g) ? Math.Round(g - s.Cover - d.Size.H, 3) : double.NaN;
        return (R(p[0][2]), R(p[^1][2]));
    }

    /// <summary>바닥고가 «자동»이면 지표면 · 규격을 따라 다시 정함.</summary>
    public static void ApplyAuto(Ditch d, Settings s)
    {
        if (!d.InvertAuto) return;
        var (a, b) = DefaultInverts(d, s);
        d.StartInvert = a; d.EndInvert = b;
    }

    /// <summary>측점 · 바닥고(시작 → 끝 한 경사) · 윗면 · 지표면 · 검사(역경사 · 최소/최대 경사 · 지표면 위로 나옴 · 급한 꺾임).</summary>
    public static Profile ProfileOf(Ditch d, Settings s)
    {
        var pr = new Profile();
        var p = CleanPts(d.Pts);
        if (p.Count < 2) { pr.Error = "측구 선이 없음(점 2개 이상)"; return pr; }
        int n = p.Count;
        pr.Sta = new double[n]; pr.Inv = new double[n]; pr.Top = new double[n]; pr.Ground = new double[n];
        for (int i = 1; i < n; i++) pr.Sta[i] = pr.Sta[i - 1] + Math.Sqrt(Sq(p[i][0] - p[i - 1][0]) + Sq(p[i][1] - p[i - 1][1]));
        pr.Length = pr.Sta[^1];
        if (pr.Length < 0.1) { pr.Error = "측구 선이 너무 짧음(0.1 m 미만)"; return pr; }
        for (int i = 1; i + 1 < n; i++)
        {
            double a = Math.Atan2(p[i][1] - p[i - 1][1], p[i][0] - p[i - 1][0]), b = Math.Atan2(p[i + 1][1] - p[i][1], p[i + 1][0] - p[i][0]);
            double t = Math.Abs(Math.IEEERemainder(b - a, 2 * Math.PI)) * 180 / Math.PI;
            pr.MaxDeflectDeg = Math.Max(pr.MaxDeflectDeg, t);
        }
        if (pr.MaxDeflectDeg > MaxDeflect) { pr.Error = string.Format(Ic, "꺾임 {0:0}° — {1:0}° 넘게 꺾인 점은 측구를 못 지음(점을 더 찍어 완만하게)", pr.MaxDeflectDeg, MaxDeflect); return pr; }
        if (pr.MaxDeflectDeg > 90.5) pr.Warn.Add(string.Format(Ic, "급한 꺾임 {0:0}°(맞댄 이음이 길어짐)", pr.MaxDeflectDeg));
        if (!double.IsFinite(d.StartInvert) || !double.IsFinite(d.EndInvert)) { pr.Error = "시작 · 끝 바닥고를 넣으세요(지표면을 못 읽음)"; return pr; }
        double H = d.Size.H;
        for (int i = 0; i < n; i++)
        {
            pr.Inv[i] = d.StartInvert + (d.EndInvert - d.StartInvert) * pr.Sta[i] / pr.Length;
            pr.Top[i] = pr.Inv[i] + H;
            pr.Ground[i] = p[i][2];
            if (double.IsFinite(pr.Ground[i]))
            {
                pr.MaxAbove = Math.Max(pr.MaxAbove, pr.Top[i] - pr.Ground[i]);
                pr.MaxBelow = Math.Max(pr.MaxBelow, pr.Ground[i] - pr.Top[i]);
            }
        }
        pr.SlopePct = (d.StartInvert - d.EndInvert) / pr.Length * 100;
        if (pr.SlopePct < -1e-9) pr.Warn.Add(string.Format(Ic, "역경사 {0:0.###} %(끝이 더 높음 — [방향 바꾸기] 또는 바닥고 확인)", pr.SlopePct));
        else if (pr.SlopePct < 1e-9) pr.Warn.Add("경사 0 %(평탄)");
        else if (pr.SlopePct < s.MinSlopePct) pr.Warn.Add(string.Format(Ic, "최소 경사 {0:0.###} % 미달({1:0.###} %)", s.MinSlopePct, pr.SlopePct));
        else if (pr.SlopePct > s.MaxSlopePct) pr.Warn.Add(string.Format(Ic, "최대 경사 {0:0.###} % 넘음({1:0.###} %)", s.MaxSlopePct, pr.SlopePct));
        if (pr.MaxAbove > 0.05) pr.Warn.Add(string.Format(Ic, "측구 윗면이 지표면 위로 최대 {0:0.00} m", pr.MaxAbove));
        if (pr.Ground.Any(g => !double.IsFinite(g))) pr.Warn.Add("지표면 밖 점이 있음(그 점은 깊이 검사 안 함)");
        return pr;
    }

    /// <summary>맞댄 이음으로 지을 수 있는 가장 큰 꺾임(°).</summary>
    public const double MaxDeflect = 120;

    static double Sq(double v) => v * v;

    // ══════════════════════════════ M05 수리 ══════════════════════════════

    public enum Verdict { NoLine, NoRain, NoArea, NoSlope, Ok, Caution, Short }

    public sealed class Hyd
    {
        public Verdict Verdict;
        /// <summary>짧은 판정 글 — «OK» · «주의» · «부족» · «강우자료 없음» · «집수면적 없음» · «경사 없음» · «선 없음».</summary>
        public string Status = "";
        /// <summary>설계유량 · 통수능(허용 수심) · 가득 흐름 통수능 (m³/s) · 유속(설계유량 · 등류 수심) m/s · 등류 수심 m · 수심비.</summary>
        public double Q = double.NaN, Qcap = double.NaN, Qfull = double.NaN, Vel = double.NaN, Yn = double.NaN, YRatio = double.NaN;
        public double SlopePct = double.NaN;
        public readonly List<string> Notes = new();
        /// <summary>표에서 기준을 다 맞는 가장 작은 규격(추천 — 확정은 설계자 · §M05) · 없으면 «자동 확정 불가» · 셀 수 없으면 빈 글.</summary>
        public string Recommend = "";
    }

    /// <summary>직사각 물길(U형 안) Manning 유량 — 폭 B · 수심 y · 경사 S(m/m) · n.</summary>
    public static double ManningQ(double B, double y, double S, double n)
    {
        if (!(B > 0 && y > 0 && S > 0 && n > 0)) return 0;
        double A = B * y, P = B + 2 * y, R = A / P;
        return A * Math.Pow(R, 2.0 / 3.0) * Math.Sqrt(S) / n;
    }

    /// <summary>합리식 Q = C · I · A / 360 (m³/s · I mm/h · A ha · 명세서 §M03 예시식).</summary>
    public static double RationalQ(double C, double I, double Aha) => C * I * Aha / 360.0;

    /// <summary>유량 <paramref name="Q"/>의 등류 수심(이분법 · 가득 차도 모자라면 H 그대로 · 넘침).</summary>
    public static double NormalDepth(double B, double H, double S, double n, double Q)
    {
        if (!(Q > 0)) return 0;
        if (ManningQ(B, H, S, n) < Q) return H;
        double lo = 0, hi = H;
        for (int k = 0; k < 60; k++) { double m = (lo + hi) / 2; if (ManningQ(B, m, S, n) < Q) lo = m; else hi = m; }
        return (lo + hi) / 2;
    }

    /// <summary>수리검토 — 한 측구 = 한 구간(1단계 · 집수면적 하나). 모델링과 따로(§9).</summary>
    public static Hyd Check(Ditch d, Rain r, Settings s)
    {
        var h = new Hyd();
        var pr = ProfileOf(d, s);
        if (pr.Error.Length > 0) { h.Verdict = Verdict.NoLine; h.Status = "선 · 바닥고 확인"; h.Notes.Add(pr.Error); return h; }
        h.SlopePct = pr.SlopePct;
        double S = pr.SlopePct / 100, B = d.Size.B, H = d.Size.H, n = s.ManningN;
        if (S > 0)
        {
            h.Qcap = ManningQ(B, H * s.DepthRatio, S, n);
            h.Qfull = ManningQ(B, H, S, n);
        }
        if (!r.Has) { h.Verdict = Verdict.NoRain; h.Status = "강우자료 없음"; h.Notes.Add("강우강도를 넣거나 CSV에서 고르면 검토합니다(값을 짓지 않음)"); return h; }
        if (!(d.AreaHa > 0) || !(d.C > 0) || d.C > 1) { h.Verdict = Verdict.NoArea; h.Status = "집수면적 없음"; h.Notes.Add("집수면적(ha) · 유출계수 C(0~1)를 넣으세요"); return h; }
        h.Q = RationalQ(d.C, r.IntensityMmh, d.AreaHa);
        if (!(S > 0)) { h.Verdict = Verdict.NoSlope; h.Status = "경사 없음"; h.Notes.Add("역경사 · 평탄 — 통수능을 셀 수 없음"); return h; }
        h.Yn = NormalDepth(B, H, S, n, h.Q);
        h.YRatio = h.Yn / H;
        h.Vel = h.Yn > 0 ? h.Q / (B * h.Yn) : 0;
        h.Recommend = Recommend(h.Q, S, s);
        if (h.Q > h.Qcap + 1e-12)
        {
            h.Verdict = Verdict.Short; h.Status = "부족";
            h.Notes.Add(string.Format(Ic, "설계유량 {0:0.000} > 통수능 {1:0.000} m³/s(수심 {2:0}% 기준)", h.Q, h.Qcap, s.DepthRatio * 100));
            return h;
        }
        h.Verdict = Verdict.Ok; h.Status = "OK";
        if (h.Vel < s.MinVelocity) { h.Verdict = Verdict.Caution; h.Notes.Add(string.Format(Ic, "유속 {0:0.00} m/s < {1:0.0#} (퇴적 우려)", h.Vel, s.MinVelocity)); }
        if (h.Vel > s.MaxVelocity) { h.Verdict = Verdict.Caution; h.Notes.Add(string.Format(Ic, "유속 {0:0.00} m/s > {1:0.0#} (세굴 우려)", h.Vel, s.MaxVelocity)); }
        if (pr.SlopePct < s.MinSlopePct) { h.Verdict = Verdict.Caution; h.Notes.Add(string.Format(Ic, "최소 경사 {0:0.###} % 미달", s.MinSlopePct)); }
        if (pr.SlopePct > s.MaxSlopePct) { h.Verdict = Verdict.Caution; h.Notes.Add(string.Format(Ic, "최대 경사 {0:0.###} % 넘음", s.MaxSlopePct)); }
        if (h.Verdict == Verdict.Caution) h.Status = "주의";
        return h;
    }

    /// <summary>표에서 유량 · 유속 기준을 다 맞는 가장 작은 규격(안 넓이 순) — 없으면 «자동 확정 불가».</summary>
    public static string Recommend(double Q, double S, Settings s)
    {
        if (!(Q > 0) || !(S > 0)) return "";
        foreach (var z in s.Sizes.OrderBy(z => z.B * z.H).ThenBy(z => z.B))
        {
            if (ManningQ(z.B, z.H * s.DepthRatio, S, s.ManningN) < Q) continue;
            double y = NormalDepth(z.B, z.H, S, s.ManningN, Q), v = y > 0 ? Q / (z.B * y) : 0;
            if (v < s.MinVelocity || v > s.MaxVelocity) continue;
            return z.Label;
        }
        return "자동 확정 불가";
    }

    // ══════════════════════════════ M08 수량 · 글 ══════════════════════════════

    /// <summary>콘크리트 — 본체 · 기초 단면적(㎡) × 중심선 길이(맞댄 이음 · 좌우 대칭 단면이라 부피 = 넓이 × 중심선 길이).</summary>
    public static (double BodyArea, double FootArea, double Length, double BodyVol, double FootVol) Qty(Ditch d)
    {
        var sh = DitchSection.U(d.Size.Dims);
        double body = PolyArea(sh.Concrete[0]), foot = sh.Concrete.Count > 1 ? PolyArea(sh.Concrete[1]) : 0;
        var p = CleanPts(d.Pts);
        double L = 0;
        for (int i = 1; i < p.Count; i++) L += Math.Sqrt(Sq(p[i][0] - p[i - 1][0]) + Sq(p[i][1] - p[i - 1][1]));
        return (body, foot, L, body * L, foot * L);
    }

    static double PolyArea(IReadOnlyList<(double O, double Z)> p)
    {
        double a = 0;
        for (int i = 0; i < p.Count; i++) { var q = p[i]; var r = p[(i + 1) % p.Count]; a += q.O * r.Z - r.O * q.Z; }
        return Math.Abs(a) / 2;
    }

    static string F(double v, string fmt) => double.IsFinite(v) ? v.ToString(fmt, Ic) : "—";

    /// <summary>창 · 3D 풍선 · 보고서가 같이 쓰는 줄(이름, 값).</summary>
    public static List<(string K, string V)> InfoRows(Ditch d, Rain r, Settings s)
    {
        var rows = new List<(string, string)>();
        var pr = ProfileOf(d, s);
        var h = Check(d, r, s);
        var q = Qty(d);
        rows.Add(("규격", "U형 " + d.Size.Label + string.Format(Ic, " (벽 {0:0} · 바닥 {1:0} mm)", d.Size.Tw * 1000, d.Size.Tb * 1000)));
        rows.Add(("길이", F(q.Length, "0.00") + " m"));
        rows.Add(("경사", F(pr.SlopePct, "0.###") + " %"));
        rows.Add(("바닥고", F(d.StartInvert, "0.000") + " → " + F(d.EndInvert, "0.000") + " m"));
        if (h.Verdict == Verdict.NoRain || h.Verdict == Verdict.NoArea || h.Verdict == Verdict.NoLine)
            rows.Add(("판정", h.Status));
        else
        {
            rows.Add(("설계유량", F(h.Q, "0.000") + " m³/s"));
            rows.Add(("통수능", F(h.Qcap, "0.000") + string.Format(Ic, " m³/s (수심 {0:0}%)", s.DepthRatio * 100)));
            if (double.IsFinite(h.Vel)) rows.Add(("유속", F(h.Vel, "0.00") + " m/s · " + (h.YRatio >= 0.999 ? "넘침(가득 차도 모자람)" : "수심 " + F(h.YRatio * 100, "0") + " %")));
            rows.Add(("판정", h.Status + (h.Notes.Count > 0 && h.Verdict != Verdict.Ok ? " — " + string.Join(" · ", h.Notes) : "")));
            if (h.Recommend.Length > 0 && h.Recommend != d.Size.Label) rows.Add(("추천 규격", h.Recommend));
        }
        rows.Add(("콘크리트", F(q.BodyVol + q.FootVol, "0.00") + " ㎥"));
        return rows;
    }

    /// <summary>M08 최소 보고서(글) — 강우 출처 · 측구마다 종단 · 수리(식과 값) · 수량 산근. 모델과 수리는 따로 적는다(§9).</summary>
    public static string Report(Record rec, Settings s, string dwg, string app)
    {
        var sb = new StringBuilder();
        sb.AppendLine("U형 측구 설계 결과 (1단계 초안)");
        sb.AppendLine($"도면: {dwg} · {app} · {rec.Updated}");
        sb.AppendLine();
        sb.AppendLine("■ 부지 대표점");
        var st = rec.Site;
        sb.AppendLine($"  X {F(st.X, "0.000")} · Y {F(st.Y, "0.000")} · EPSG {(st.Epsg > 0 ? st.Epsg.ToString(Ic) : "모름")} · " +
                      (double.IsFinite(st.Lat) ? $"위도 {F(st.Lat, "0.000000")} · 경도 {F(st.Lon, "0.000000")}" : "경위도 변환 못 함(좌표계 확인)") + $" · {st.Source}");
        sb.AppendLine();
        sb.AppendLine("■ 설계강우");
        var r = rec.Rain;
        if (!r.Has) sb.AppendLine("  자료 미확보 — 수리검토 안 함");
        else
        {
            sb.AppendLine(string.Format(Ic, "  재현기간 {0:0.#}년 · 지속기간 {1:0.#}분 · 강우강도 I = {2:0.0} mm/h", r.ReturnYears, r.DurationMin, r.IntensityMmh));
            sb.AppendLine($"  출처: {(r.Source.Length > 0 ? r.Source : "(적지 않음)")} · 기준연도/판: {(r.SourceVersion.Length > 0 ? r.SourceVersion : "(적지 않음)")} · 방식: {r.Method}{(r.File.Length > 0 ? " · 파일 " + r.File : "")} · 입력 {r.Entered}");
        }
        sb.AppendLine();
        sb.AppendLine("■ 기준값(설정)");
        sb.AppendLine(string.Format(Ic, "  Manning n {0} · 허용 수심 {1:0}% · 경사 {2:0.###}~{3:0.###} % · 유속 {4:0.0#}~{5:0.0#} m/s · 덮개 {6:0.00} m",
                      s.ManningN, s.DepthRatio * 100, s.MinSlopePct, s.MaxSlopePct, s.MinVelocity, s.MaxVelocity, s.Cover));
        double tb = 0, tf = 0, tl = 0;
        foreach (var d in rec.Ditches)
        {
            var pr = ProfileOf(d, s); var h = Check(d, r, s); var q = Qty(d);
            sb.AppendLine();
            sb.AppendLine($"■ {d.Id} — U형 {d.Size.Label}");
            sb.AppendLine(string.Format(Ic, "  단면: 안폭 {0:0.000} · 안높이 {1:0.000} · 벽 {2:0.000} · 바닥 {3:0.000} · 기초 {4:0.000}(내밈 {5:0.000}) m", d.Size.B, d.Size.H, d.Size.Tw, d.Size.Tb, d.Size.Tf, d.Size.Pf));
            sb.AppendLine($"  선: 점 {CleanPts(d.Pts).Count} · 길이 {F(pr.Length, "0.000")} m · 지표면 «{d.Surface}» · 방향 = 시점(상류) → 종점(하류)");
            sb.AppendLine($"  종단: 바닥고 {F(d.StartInvert, "0.000")} → {F(d.EndInvert, "0.000")} m · 경사 {F(pr.SlopePct, "0.###")} % ({(d.InvertAuto ? "지표면 따라 자동" : "손 입력")})");
            if (pr.Error.Length > 0) sb.AppendLine("  ⚠ " + pr.Error);
            foreach (var w in pr.Warn) sb.AppendLine("  ⚠ " + w);
            sb.AppendLine("  수리(모델과 따로):");
            if (h.Verdict == Verdict.NoRain || h.Verdict == Verdict.NoArea || h.Verdict == Verdict.NoLine) sb.AppendLine("    " + h.Status + " — " + string.Join(" · ", h.Notes));
            else
            {
                sb.AppendLine(string.Format(Ic, "    Q = C·I·A/360 = {0:0.00} × {1:0.0} × {2:0.0000} / 360 = {3:0.0000} m³/s", d.C, r.IntensityMmh, d.AreaHa, h.Q));
                if (double.IsFinite(h.Qcap))
                {
                    double y = d.Size.H * s.DepthRatio, A = d.Size.B * y, P = d.Size.B + 2 * y;
                    sb.AppendLine(string.Format(Ic, "    Qcap = (1/n)·A·R^(2/3)·S^(1/2) — y = {0:0.000} m · A = {1:0.0000} ㎡ · P = {2:0.000} m · R = {3:0.0000} m · S = {4:0.00000} → {5:0.0000} m³/s",
                                  y, A, P, A / P, h.SlopePct / 100, h.Qcap));
                }
                if (double.IsFinite(h.Vel)) sb.AppendLine(string.Format(Ic, "    등류 수심 {0:0.000} m ({1:0}%) · 유속 {2:0.00} m/s", h.Yn, h.YRatio * 100, h.Vel));
                sb.AppendLine("    판정: " + h.Status + (h.Notes.Count > 0 ? " — " + string.Join(" · ", h.Notes) : ""));
                if (h.Recommend.Length > 0) sb.AppendLine("    표에서 기준을 다 맞는 가장 작은 규격(참고 · 확정은 설계자): " + h.Recommend);
            }
            sb.AppendLine(string.Format(Ic, "  수량: 본체 {0:0.0000} ㎡ × {1:0.000} m = {2:0.000} ㎥ · 기초 {3:0.0000} ㎡ × {1:0.000} m = {4:0.000} ㎥ · 합 {5:0.000} ㎥",
                          q.BodyArea, q.Length, q.BodyVol, q.FootArea, q.FootVol, q.BodyVol + q.FootVol));
            tb += q.BodyVol; tf += q.FootVol; tl += q.Length;
        }
        sb.AppendLine();
        sb.AppendLine(string.Format(Ic, "■ 합계: 측구 {0}개 · 길이 {1:0.00} m · 콘크리트 본체 {2:0.000} ㎥ · 기초 {3:0.000} ㎥ · 합 {4:0.000} ㎥", rec.Ditches.Count, tl, tb, tf, tb + tf));
        return sb.ToString();
    }
}
