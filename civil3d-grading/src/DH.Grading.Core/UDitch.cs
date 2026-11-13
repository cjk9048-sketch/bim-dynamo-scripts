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
        // ★[v117.16 · JACK 1112 «애초에 폴리선이 단차 있게 만든 부지거든?»] 지표 따라감 · 낙차 · 단차(집수정 + 관로) · 덮개 — 기준값은 모두 설정(지어낸 고정값 없음)
        /// <summary>지표를 읽는 간격(m · 선 따라). 급한 곳은 0.1 m까지 더 잘게.</summary>
        public double SampleStep { get; set; } = 1.0;
        /// <summary>★[v117.16 검토 HIGH] 간격 가운데 지표가 두 끝 직선에서 이만큼(m) 넘게 벗어나면 반씩 더 읽음(꺾인 곳 — 법면 끝 등).</summary>
        public double SampleTol { get; set; } = 0.02;
        /// <summary>이보다 급한 지표 내리막(%) 중 높이가 <see cref="DropMax"/>를 넘는 곳 = 단차(집수정 + 관로).</summary>
        public double StepGradePct { get; set; } = 50.0;
        /// <summary>낙차 하나의 최대 높이(m) — 넘는 급경사는 단차로 봄.</summary>
        public double DropMax { get; set; } = 0.5;
        /// <summary>낙차 하나의 최소 높이(m).</summary>
        public double DropMin { get; set; } = 0.05;
        /// <summary>단차 위 · 아래 끝에서 집수정 · 관 끝을 띄우는 거리(m).</summary>
        public double StepSetback { get; set; } = 0.5;
        /// <summary>집수정 안치수(정사각 · m) · 벽 · 바닥 두께 · 유입 바닥고 아래 깊이(침전).</summary>
        public double BasinInner { get; set; } = 0.6;
        public double BasinWall { get; set; } = 0.15;
        public double BasinFloor { get; set; } = 0.15;
        public double BasinSump { get; set; } = 0.15;
        /// <summary>그레이팅 두께(m) — 집수정 덮개 · 측구 스틸 그레이팅.</summary>
        public double GrateT { get; set; } = 0.05;
        /// <summary>관로 지름(호칭 · m) · 관 두께(m · 모델용).</summary>
        public double PipeD { get; set; } = 0.3;
        public double PipeWall { get; set; } = 0.05;
        /// <summary>★[v117.16 검토 HIGH] 관로 최소 흙덮개(m · 관 윗면 ~ 지표) — 실제 = max(Cover, PipeCover).</summary>
        public double PipeCover { get; set; } = 0.3;
        /// <summary>관로 추천에 쓰는 지름 표(m).</summary>
        public List<double> PipeSizes { get; set; } = new() { 0.3, 0.4, 0.5, 0.6, 0.8, 1.0 };
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
        // ★[v117.16 · JACK 1112] 단차 · 낙차 · 집수정 · 관로
        if (!Pos(s.SampleStep) || s.SampleStep < 0.1 || s.SampleStep > 20) { bad.Add("SampleStep"); s.SampleStep = 1.0; }
        if (!Pos(s.SampleTol) || s.SampleTol > 1) { bad.Add("SampleTol"); s.SampleTol = 0.02; }
        if (!Pos(s.StepGradePct) || s.StepGradePct <= s.MaxSlopePct) { bad.Add("StepGradePct"); s.StepGradePct = Math.Max(50, s.MaxSlopePct * 2); }
        if (!Pos(s.DropMax) || s.DropMax > 5) { bad.Add("DropMax"); s.DropMax = 0.5; }
        if (!Pos(s.DropMin) || s.DropMin > s.DropMax) { bad.Add("DropMin"); s.DropMin = Math.Min(0.05, s.DropMax); }
        if (!double.IsFinite(s.StepSetback) || s.StepSetback < 0 || s.StepSetback > 10) { bad.Add("StepSetback"); s.StepSetback = 0.5; }
        if (!Pos(s.BasinInner) || s.BasinInner > 5) { bad.Add("BasinInner"); s.BasinInner = 0.6; }
        if (!Pos(s.BasinWall) || s.BasinWall > 2) { bad.Add("BasinWall"); s.BasinWall = 0.15; }
        if (!Pos(s.BasinFloor) || s.BasinFloor > 2) { bad.Add("BasinFloor"); s.BasinFloor = 0.15; }
        if (!double.IsFinite(s.BasinSump) || s.BasinSump < 0 || s.BasinSump > 3) { bad.Add("BasinSump"); s.BasinSump = 0.15; }
        if (!Pos(s.GrateT) || s.GrateT > 0.5) { bad.Add("GrateT"); s.GrateT = 0.05; }
        if (!Pos(s.PipeD) || s.PipeD > 5) { bad.Add("PipeD"); s.PipeD = 0.3; }
        if (!Pos(s.PipeWall) || s.PipeWall > 1) { bad.Add("PipeWall"); s.PipeWall = 0.05; }
        if (!double.IsFinite(s.PipeCover) || s.PipeCover < 0 || s.PipeCover > 5) { bad.Add("PipeCover"); s.PipeCover = 0.3; }
        var ps = (s.PipeSizes ?? new List<double>()).Where(v => Pos(v) && v <= 5).Distinct().OrderBy(v => v).ToList();
        if (ps.Count == 0) { bad.Add("PipeSizes"); ps = new Settings().PipeSizes; }
        s.PipeSizes = ps;
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
        /// <summary>참 = [방향 바꾸기] — 원 선(그린 차례)을 다시 읽어도 뒤집어 씀(<see cref="SetLinePts"/>). 옛 기록 = 거짓(옛 판은 모르는 칸을 버림 · 판 번호 그대로).</summary>
        public bool Reversed { get; set; }
        public double AreaHa { get; set; }
        public double C { get; set; } = 0.9;
        /// <summary>★[v117.16 · JACK 1112] 참 = 바닥이 지표를 따라감(최대 경사 넘으면 낙차 · 단차는 집수정 + 관로 · <see cref="LayoutOf"/>). 새 측구 = 참 · 옛 기록(칸 없음) = 거짓 = 한 경사 그대로. [지표면 맞춤]이 켬.</summary>
        public bool Follow { get; set; }
        /// <summary>★[v117.16] 선 따라 읽은 지표 [측점 m, z(NaN = 못 읽음)] — <see cref="FillSamples"/>. 옛 기록 = 비었음(마디 지표만 씀). 기록에는 한 줄로(검토 LOW — 도면 기록이 부풀지 않게).</summary>
        [JsonConverter(typeof(CompactPairs))]
        public List<double[]> Samp { get; set; } = new();
        /// <summary>★[v117.16 · JACK 1112 «측구에 스틸그라우팅도 선택되게»] 덮개 — 거짓 = 없음(열린 U · 옛 기록) · 참 = 스틸 그레이팅.</summary>
        public bool Grating { get; set; }
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

    /// <summary>★[v117.16 검토 LOW] [[a, b], …]를 줄바꿈 없이 한 값으로(들여쓰기 기록 안에서도) · NaN = "NaN" · 읽기는 숫자 · "NaN" 모두.</summary>
    internal sealed class CompactPairs : JsonConverter<List<double[]>>
    {
        public override List<double[]> Read(ref Utf8JsonReader r, Type t, JsonSerializerOptions o)
        {
            var x = new List<double[]>();
            if (r.TokenType == JsonTokenType.Null) return x;
            if (r.TokenType != JsonTokenType.StartArray) throw new JsonException("Samp — 배열이 아님");
            while (r.Read() && r.TokenType != JsonTokenType.EndArray)
            {
                if (r.TokenType != JsonTokenType.StartArray) { r.Skip(); continue; }
                var row = new List<double>();
                while (r.Read() && r.TokenType != JsonTokenType.EndArray)
                    row.Add(r.TokenType == JsonTokenType.String ? (double.TryParse(r.GetString(), NumberStyles.Float, Ic, out double v) ? v : double.NaN)
                          : r.TokenType == JsonTokenType.Number ? r.GetDouble() : double.NaN);
                x.Add(row.ToArray());
            }
            return x;
        }

        public override void Write(Utf8JsonWriter w, List<double[]> v, JsonSerializerOptions o)
        {
            var sb = new StringBuilder("[");
            for (int i = 0; i < v.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append('[');
                var row = v[i] ?? Array.Empty<double>();
                for (int j = 0; j < row.Length; j++)
                {
                    if (j > 0) sb.Append(',');
                    double d = row[j];
                    sb.Append(double.IsNaN(d) ? "\"NaN\"" : double.IsPositiveInfinity(d) ? "\"Infinity\"" : double.IsNegativeInfinity(d) ? "\"-Infinity\"" : d.ToString("R", Ic));
                }
                sb.Append(']');
            }
            sb.Append(']');
            w.WriteRawValue(sb.ToString());
        }
    }

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
                d.Samp = (d.Samp ?? new()).Where(p => p != null && p.Length >= 2 && double.IsFinite(p[0])).ToList();   // ★[v117.16]
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
        /// <summary>★[v117.16] 지표 따라감이면 구간 · 낙차 · 집수정 · 관로(<see cref="LayoutOf"/>) — 한 경사면 null.</summary>
        public Layout? Lay;
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

    /// <summary>[방향 바꾸기] — 점 · 시작/끝 바닥고를 뒤집고 <see cref="Ditch.Reversed"/>를 바꿔 기억(원 선을 다시 읽어도 유지).</summary>
    public static void Reverse(Ditch d)
    {
        d.Reversed = !d.Reversed;
        d.Pts.Reverse();
        (d.StartInvert, d.EndInvert) = (d.EndInvert, d.StartInvert);
        // ★[v117.16] 지표 표본도 뒤집음(측점 = 길이 − 측점)
        if (d.Samp.Count > 0)
        {
            double L = d.Samp[^1][0];
            d.Samp = d.Samp.AsEnumerable().Reverse().Select(q => new[] { Math.Round(L - q[0], 3), q.Length > 1 ? q[1] : double.NaN }).ToList();
        }
    }

    /// <summary>원 선에서 다시 읽은 점(그린 차례) → 측구 점(상류 → 하류) — <see cref="Ditch.Reversed"/>면 뒤집음.</summary>
    public static void SetLinePts(Ditch d, List<double[]> drawn)
    {
        if (d.Reversed) drawn.Reverse();
        d.Pts = drawn;
    }

    /// <summary>바닥고가 «자동»이면 지표면 · 규격을 따라 다시 정함.</summary>
    public static void ApplyAuto(Ditch d, Settings s)
    {
        if (!d.InvertAuto) return;
        // ★[v117.16 · JACK 1112] 지표 따라감 — 시점 · 종점 바닥고 = 구간 계산의 처음 · 끝(보이기용)
        if (d.Follow)
        {
            var lay = LayoutOf(d, s);
            if (lay.Follow && lay.Segs.Count > 0) { d.StartInvert = Math.Round(lay.StartInv, 3); d.EndInvert = Math.Round(lay.EndInv, 3); return; }
        }
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
        double H = d.Size.H;
        // ★[v117.16 · JACK 1112 «애초에 폴리선이 단차 있게 만든 부지거든?»] 지표 따라감 — 구간 · 낙차 · 집수정 · 관로
        if (d.Follow && d.InvertAuto)
        {
            var lay = LayoutOf(d, s);
            if (lay.Follow)
            {
                pr.Lay = lay;
                if (lay.Error.Length > 0) { pr.Error = lay.Error; return pr; }
                for (int i = 0; i < n; i++) { pr.Inv[i] = lay.InvAt(pr.Sta[i]); pr.Top[i] = pr.Inv[i] + H; pr.Ground[i] = p[i][2]; }
                pr.SlopePct = (lay.StartInv - lay.EndInv) / pr.Length * 100;
                pr.MaxAbove = lay.MaxAbove; pr.MaxBelow = lay.MaxBelow;
                pr.Warn.AddRange(lay.Warn);
                if (pr.MaxAbove > 0.05) pr.Warn.Add(string.Format(Ic, "측구 윗면이 지표면 위로 최대 {0:0.00} m", pr.MaxAbove));
                if (pr.MaxBelow > s.DropMax + 0.05) pr.Warn.Add(string.Format(Ic, "측구가 최대 {0:0.00} m 묻힘 — 지표가 솟은 곳 → [방향 바꾸기] 또는 선 확인", pr.MaxBelow));
                if (lay.GroundMiss > 0) pr.Warn.Add("지표면 밖 점 있음 — 이웃 값으로 메움");
                return pr;
            }
        }
        if (!double.IsFinite(d.StartInvert) || !double.IsFinite(d.EndInvert)) { pr.Error = "시작 · 끝 바닥고를 넣으세요(지표면을 못 읽음)"; return pr; }
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
        /// <summary>★[v117.16] 지표 따라감 — 구간마다 · 관로마다(한 경사면 비었음).</summary>
        public readonly List<SegCheck> Segs = new();
        public readonly List<PipeCheck> Pipes = new();
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
        if (pr.Lay is { Follow: true } lay) return CheckFollow(d, r, s, pr, lay);   // ★[v117.16] 구간마다
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
        var q = Qty(d, s);
        var lay = pr.Lay is { Follow: true } l0 && l0.Error.Length == 0 ? l0 : null;   // ★[v117.16] 지표 따라감
        var ex = Extras(d, s);
        rows.Add(("규격", "U형 " + d.Size.Label + string.Format(Ic, " (벽 {0:0} · 바닥 {1:0} mm)", d.Size.Tw * 1000, d.Size.Tb * 1000)));
        if (lay == null)
        {
            rows.Add(("길이", F(q.Length, "0.00") + " m"));
            rows.Add(("경사", F(pr.SlopePct, "0.###") + " %"));
        }
        else
        {
            rows.Add(("길이", F(lay.Length, "0.00") + " m" + (lay.Basins.Count > 0 ? " · U형 " + F(q.Length, "0.00") + " m" : "")));
            var parts = new List<string> { $"{lay.Segs.Count}" };
            if (lay.Drops.Count > 0) parts.Add(string.Format(Ic, "낙차 {0}(합 {1:0.00} m)", lay.Drops.Count, ex.DropSum));
            if (lay.Basins.Count > 0) parts.Add($"집수정 {lay.Basins.Count}");
            if (lay.Pipes.Count > 0) parts.Add(string.Format(Ic, "관 {0:0.0} m", ex.PipeLen));
            rows.Add(("구간", string.Join(" · ", parts)));
            double lo = lay.Segs.Min(g => g.SMinPct), hi = lay.Segs.Max(g => g.SMaxPct);
            rows.Add(("경사", Math.Abs(hi - lo) < 0.0005 ? F(lo, "0.###") + " %" : F(lo, "0.###") + "~" + F(hi, "0.###") + " %"));
        }
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
        if (d.Grating) rows.Add(("덮개", "스틸 그레이팅 " + F(ex.GrateLen, "0.00") + " m"));   // ★[v117.16 · JACK 1112 «스틸그라우팅»]
        rows.Add(("콘크리트", F(q.BodyVol + q.FootVol + ex.Concrete, "0.00") + " ㎥"));
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
        if (rec.Ditches.Any(d => d.Follow && d.InvertAuto))   // ★[v117.16 · JACK 1112]
            sb.AppendLine(string.Format(Ic, "  지표 따라감: 표본 {0:0.0#} m · 단차 = 지표 경사 > {1:0.#} % 이고 높이 > {2:0.00} m · 낙차 {3:0.00}~{2:0.00} m · 단차마다 집수정 둘(위 · 아래 · 안 {4:0.00} m · 벽 {5:0.00} · 바닥 {6:0.00} · 침전 {7:0.00}) · 띄움 {8:0.00} m · 관 D{9:0} · 흙덮개 ≥ {10:0.00} m(땅 속으로)",
                          s.SampleStep, s.StepGradePct, s.DropMax, s.DropMin, s.BasinInner, s.BasinWall, s.BasinFloor, s.BasinSump, s.StepSetback, s.PipeD * 1000, Math.Max(s.Cover, s.PipeCover)));
        double tb = 0, tf = 0, tl = 0, tx = 0, tpipe = 0, tgr = 0;
        int tbas = 0, tdrop = 0;
        foreach (var d in rec.Ditches)
        {
            var pr = ProfileOf(d, s); var h = Check(d, r, s); var q = Qty(d, s); var ex = Extras(d, s);
            var lay = pr.Lay is { Follow: true } l0 && l0.Error.Length == 0 ? l0 : null;   // ★[v117.16 · JACK 1112] 지표 따라감
            sb.AppendLine();
            sb.AppendLine($"■ {d.Id} — U형 {d.Size.Label}");
            sb.AppendLine(string.Format(Ic, "  단면: 안폭 {0:0.000} · 안높이 {1:0.000} · 벽 {2:0.000} · 바닥 {3:0.000} · 기초 {4:0.000}(내밈 {5:0.000}) m", d.Size.B, d.Size.H, d.Size.Tw, d.Size.Tb, d.Size.Tf, d.Size.Pf));
            sb.AppendLine($"  선: 점 {CleanPts(d.Pts).Count} · 길이 {F(pr.Length, "0.000")} m · 지표면 «{d.Surface}» · 방향 = 시점(상류) → 종점(하류)");
            if (lay == null)
                sb.AppendLine($"  종단: 바닥고 {F(d.StartInvert, "0.000")} → {F(d.EndInvert, "0.000")} m · 경사 {F(pr.SlopePct, "0.###")} % ({(d.InvertAuto ? "지표면 따라 자동" : "손 입력")})");
            else
            {
                sb.AppendLine(string.Format(Ic, "  종단: 지표 따라감(바닥 = 지표 − 덮개 − 안높이 · 경사 {0:0.###}~{1:0.###} % · 급하면 낙차 · 단차 = 집수정 + 관로) · 바닥고 {2} → {3} m",
                              s.MinSlopePct, s.MaxSlopePct, F(d.StartInvert, "0.000"), F(d.EndInvert, "0.000")));
                sb.AppendLine(string.Format(Ic, "  짜임: 구간 {0} · 낙차 {1}(합 {2:0.000} m) · 집수정 {3} · 관로 {4} · 묻힘 최대 {5:0.00} m", lay.Segs.Count, lay.Drops.Count, ex.DropSum, lay.Basins.Count, lay.Pipes.Count, lay.MaxBelow));
                for (int i = 0; i < lay.Segs.Count; i++)
                {
                    var g = lay.Segs[i];
                    sb.AppendLine(string.Format(Ic, "    구간 {0}: 측점 {1:0.000} → {2:0.000} m · 길이 {3:0.000} m · 경사 {4} · 바닥고 {5:0.000} → {6:0.000} m",
                                  i + 1, g.S0, g.S1, g.Length, Math.Abs(g.SMaxPct - g.SMinPct) < 0.0005 ? F(g.SMinPct, "0.###") + " %" : F(g.SMinPct, "0.###") + "~" + F(g.SMaxPct, "0.###") + " %", g.V0, g.V1));
                }
                for (int i = 0; i < lay.Drops.Count; i++)
                {
                    var x = lay.Drops[i];
                    sb.AppendLine(string.Format(Ic, "    낙차 {0}: 측점 {1:0.000} m · 높이 {2:0.000} m ({3:0.000} → {4:0.000})", i + 1, x.S, x.H, x.V1, x.V2));
                }
                for (int i = 0; i < lay.Basins.Count; i++)
                {
                    var b = lay.Basins[i];
                    sb.AppendLine(string.Format(Ic, "    집수정 {0}({1}): 측점 {2:0.000}~{3:0.000} m · 안 {4:0.00}×{4:0.00} · 바깥 {5:0.00}×{5:0.00} m · 윗면 {6:0.000} · 유입 바닥 {7:0.000}{8} · 안바닥 {9:0.000} · 깊이 {10:0.000} m · 그레이팅 덮개",
                                  i + 1, b.Lower ? "아래" : "위", b.S0, b.S1, b.Inner, b.Outer, b.ZTop, b.InV, double.IsFinite(b.OutV) ? string.Format(Ic, " · 유출 바닥 {0:0.000}", b.OutV) : "", b.Floor, b.Depth));
                }
                for (int i = 0; i < lay.Pipes.Count; i++)
                {
                    var p = lay.Pipes[i];
                    sb.AppendLine(string.Format(Ic, "    관로 {0}: 측점 {1:0.000} → {2:0.000} m · 원형 D{3:0}(바깥 {4:0.00}) · 다리 {5}개 · 길이 {6:0.000} m · 관 바닥 {7:0.000} → {8:0.000} m · 흙덮개 ≥ {9:0.00} m(관 윗면 + 흙덮개 − 지표 최대 {10:0.000} m)",
                                  i + 1, p.S0, p.S1, p.D * 1000, p.Dout, p.Legs.Count, p.Length, p.InvIn, p.InvOut, p.CoverMin, p.CrownExcess));
                    for (int j = 0; j < p.Legs.Count; j++)
                    {
                        var l = p.Legs[j];
                        sb.AppendLine(string.Format(Ic, "      다리 {0}-{1}: 측점 {2:0.000} → {3:0.000} m · 길이 {4:0.000} m · 경사 {5} %{6} · 관 바닥 {7:0.000} → {8:0.000}",
                                      i + 1, j + 1, l.S0, l.S1, l.Length, F(l.SlopePct, "0.##"), l.SlopePct > FallPct ? "(낙하)" : "", l.Z1 - p.D / 2, l.Z2 - p.D / 2));
                    }
                }
            }
            if (pr.Error.Length > 0) sb.AppendLine("  ⚠ " + pr.Error);
            foreach (var w in pr.Warn) sb.AppendLine("  ⚠ " + w);
            sb.AppendLine("  수리(모델과 따로):");
            if (h.Verdict == Verdict.NoRain || h.Verdict == Verdict.NoArea || h.Verdict == Verdict.NoLine) sb.AppendLine("    " + h.Status + " — " + string.Join(" · ", h.Notes));
            else
            {
                sb.AppendLine(string.Format(Ic, "    Q = C·I·A/360 = {0:0.00} × {1:0.0} × {2:0.0000} / 360 = {3:0.0000} m³/s", d.C, r.IntensityMmh, d.AreaHa, h.Q));
                if (lay == null)
                {
                    if (double.IsFinite(h.Qcap))
                    {
                        double y = d.Size.H * s.DepthRatio, A = d.Size.B * y, P = d.Size.B + 2 * y;
                        sb.AppendLine(string.Format(Ic, "    Qcap = (1/n)·A·R^(2/3)·S^(1/2) — y = {0:0.000} m · A = {1:0.0000} ㎡ · P = {2:0.000} m · R = {3:0.0000} m · S = {4:0.00000} → {5:0.0000} m³/s",
                                      y, A, P, A / P, h.SlopePct / 100, h.Qcap));
                    }
                    if (double.IsFinite(h.Vel)) sb.AppendLine(string.Format(Ic, "    등류 수심 {0:0.000} m ({1:0}%) · 유속 {2:0.00} m/s", h.Yn, h.YRatio * 100, h.Vel));
                }
                else
                {
                    double y = d.Size.H * s.DepthRatio, A = d.Size.B * y, P = d.Size.B + 2 * y;
                    sb.AppendLine(string.Format(Ic, "    Qcap = (1/n)·A·R^(2/3)·S^(1/2) — y = {0:0.000} m · A = {1:0.0000} ㎡ · P = {2:0.000} m · R = {3:0.0000} m · S = 구간 최소 경사", y, A, P, A / P));
                    foreach (var c in h.Segs)
                        sb.AppendLine(string.Format(Ic, "    구간 {0}: S {1:0.00000}~{2:0.00000} · Qcap {3} m³/s · 수심 {4} % · 유속 {5}~{6} m/s · {7}{8}",
                                      c.No, c.Seg.SMinPct / 100, c.Seg.SMaxPct / 100, F(c.Qcap, "0.0000"), F(c.YRatio * 100, "0"), F(c.VelMin, "0.00"), F(c.VelMax, "0.00"), c.Status,
                                      c.Notes.Count > 0 ? " — " + string.Join(" · ", c.Notes) : ""));
                    foreach (var c in h.Pipes)
                        sb.AppendLine(string.Format(Ic, "    관로 {0}: 가득 흐름 Qfull = (1/n)·(πD²/4)·(D/4)^(2/3)·S^(1/2) — D {1:0.000} m · 가장 완만한 다리 S {2} → {3} m³/s · 가장 급한 흐름 다리 S {4} 유속 {5} m/s · 낙하 다리 {6} · {7}{8}{9}",
                                      c.No, c.Pipe.D, F(c.SFlat, "0.00000"), F(c.Qfull, "0.0000"), F(c.SSteep, "0.00000"), F(c.VelMax, "0.00"), c.FallLegs, c.Status,
                                      c.Recommend.Length > 0 ? " → " + c.Recommend : "", c.Notes.Count > 0 ? " — " + string.Join(" · ", c.Notes) : ""));
                }
                sb.AppendLine("    판정: " + h.Status + (h.Notes.Count > 0 ? " — " + string.Join(" · ", h.Notes) : ""));
                if (h.Recommend.Length > 0) sb.AppendLine("    표에서 기준을 다 맞는 가장 작은 규격(참고 · 확정은 설계자): " + h.Recommend);
            }
            // ★[v117.16 · JACK 1112 «측구에 스틸그라우팅도 선택되게»] 덮개
            if (d.Grating)
            {
                string per = lay != null && lay.Segs.Count > 1 ? " (" + string.Join(" · ", lay.Segs.Select((g, i) => string.Format(Ic, "구간 {0} {1:0.00} m", i + 1, g.Length))) + ")" : "";
                sb.AppendLine(string.Format(Ic, "  덮개: 스틸 그레이팅 · 구간 {0}개 · 길이 {1:0.000} m{2}", lay?.Segs.Count ?? 1, ex.GrateLen, per));
            }
            else sb.AppendLine("  덮개: 없음(열린 U)");
            sb.AppendLine(string.Format(Ic, "  수량: 본체 {0:0.0000} ㎡ × {1:0.000} m = {2:0.000} ㎥ · 기초 {3:0.0000} ㎡ × {1:0.000} m = {4:0.000} ㎥ · 합 {5:0.000} ㎥",
                          q.BodyArea, q.Length, q.BodyVol, q.FootArea, q.FootVol, q.BodyVol + q.FootVol));
            if (ex.Drops + ex.Basins + ex.Pipes > 0)
                sb.AppendLine(string.Format(Ic, "        낙차벽 {0}개 {1:0.000} ㎥ · 집수정 {2}개 {3:0.000} ㎥(바깥 상자 − 안 빈 곳) · 관로 D{4:0} {5}개(다리 {7}) {6:0.000} m",
                              ex.Drops, ex.DropVol, ex.Basins, ex.BasinVol, s.PipeD * 1000, ex.Pipes, ex.PipeLen, ex.Legs));
            tb += q.BodyVol; tf += q.FootVol; tl += q.Length; tx += ex.Concrete; tpipe += ex.PipeLen; tgr += ex.GrateLen; tbas += ex.Basins; tdrop += ex.Drops;
        }
        sb.AppendLine();
        sb.AppendLine(string.Format(Ic, "■ 합계: 측구 {0}개 · 길이 {1:0.00} m · 콘크리트 본체 {2:0.000} ㎥ · 기초 {3:0.000} ㎥", rec.Ditches.Count, tl, tb, tf)
                      + (tx > 0 ? string.Format(Ic, " · 낙차벽 · 집수정 {0:0.000} ㎥", tx) : "") + string.Format(Ic, " · 합 {0:0.000} ㎥", tb + tf + tx)
                      + (tbas > 0 || tdrop > 0 ? string.Format(Ic, " · 낙차 {0} · 집수정 {1} · 관로 {2:0.00} m", tdrop, tbas, tpipe) : "")
                      + (tgr > 0 ? string.Format(Ic, " · 스틸 그레이팅 {0:0.00} m", tgr) : ""));
        return sb.ToString();
    }
}
