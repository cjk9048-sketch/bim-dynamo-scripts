using DH.Pipe.Core.Geom;
using DH.Pipe.Core.Model;

namespace DH.Pipe.Core.Section;

/// <summary>단면 안의 관 하나.</summary>
public sealed class SectionPipe
{
    /// <summary>중심선에서의 거리(오른쪽 +).</summary>
    public double Dx { get; set; }
    /// <summary>관저고(관 안쪽 바닥 표고).</summary>
    public double Invert { get; set; }
    public double Od { get; set; }
    public double Wall { get; set; }
    public int Dn { get; set; }

    public double OuterBottom => Invert - Wall;
    public double OuterTop => OuterBottom + Od;
    public double CenterZ => OuterBottom + Od * 0.5;
    public double Area => Math.PI * Od * Od * 0.25;

    public static SectionPipe Of(PipeSpec p, double invert, double dx = 0)
        => new() { Dx = dx, Invert = invert, Od = p.Od, Wall = p.Wall, Dn = p.Dn };
}

/// <summary>한 측점 단면의 입력.</summary>
public sealed class SectionInput
{
    public double Station { get; set; }
    public string Label { get; set; } = "";
    /// <summary>지반선 U(x).</summary>
    public Pl Ground { get; set; } = Pl.Const(-6, 6, 0);
    /// <summary>관들. [0] = 본관(종단선의 관).</summary>
    public List<SectionPipe> Pipes { get; set; } = new();
    public MethodSpan Method { get; set; } = new();
    public Standards Std { get; set; } = new();
    /// <summary>지표 구역. 안 덮인 곳 = 기준값의 기본 지표.</summary>
    public List<ZoneSpan> Zones { get; set; } = new();
    /// <summary>토사 두께 · 풍화암 두께(지반 아래 m). null = 그 층이 끝없이.</summary>
    public double? SoilThk { get; set; }
    public double? WrThk { get; set; }
    /// <summary>지하수위 표고. null = 없음.</summary>
    public double? WaterElev { get; set; }
    /// <summary>연석 선의 x 들.</summary>
    public List<double> Curbs { get; set; } = new();
    /// <summary>터파기 폭 안을 가로지르는 지장물 수(보호공).</summary>
    public int Crossings { get; set; }
}

/// <summary>조각 · 길이 이름표(열쇠 글).</summary>
public static class Q
{
    /// <summary>터파기 조각 열쇠. 예 «EXC.DRY.LE.SOIL».</summary>
    public static string Exc(bool wet, bool deep, int rock)
        => $"EXC.{(wet ? "WET" : "DRY")}.{(deep ? "GT" : "LE")}.{RockCode[rock]}";

    public static readonly string[] RockCode = { "SOIL", "WR", "SR" };
    public static readonly string[] RockName = { "토사", "풍화암", "연암" };

    public const string PavAsp = "PAV.ASP";       // 포장 깨기(㎡ 단면 → ㎥)
    public const string PavCon = "PAV.CON";
    public const string PavBlk = "PAV.BLK";
    public const string Subbase = "SUBBASE";      // 보조기층(걷어냄 = 복구)
    public const string Bed = "BF.BED";           // 관 기초(모래)
    public const string Zone = "BF.ZONE";         // 관주위 되메우기
    public const string Upper = "BF.UPPER";       // 관상단 되메우기
    public const string Encase = "ENCASE";        // 콘크리트 보호공
    public const string Bore = "JACK.BORE";       // 추진 굴착(케이싱 단면)

    // 길이(m) — 평균 × 거리 = ㎡ 또는 그대로 평균
    public const string TopWidth = "W.TOP";
    public const string BotWidth = "W.BOT";
    public const string WallL = "H.WALL.L";       // 수직 벽 높이(가시설)
    public const string WallR = "H.WALL.R";
    public const string CutAsp = "CUT.ASP";       // 복구 폭
    public const string CutCon = "CUT.CON";
    public const string CutBlk = "CUT.BLK";
    public const string CutLines = "CUT.LINES";   // 절단 줄 수
    public const string Curb = "CURB";            // 걸리는 연석 줄 수
    public const string Depth = "DEPTH";          // 터파기고(중심)
    public const string Cover = "COVER";          // 토피(본관)

    /// <summary>길이 · 개수 열쇠인지(아니면 넓이 열쇠).</summary>
    public static bool IsLen(string key)
        => key.StartsWith("W.") || key.StartsWith("H.") || key.StartsWith("CUT.") || key is Curb or Depth or Cover;

    public static string Pav(SurfaceKind k) => k switch { SurfaceKind.Asphalt => PavAsp, SurfaceKind.Concrete => PavCon, SurfaceKind.Block => PavBlk, _ => "" };
    public static string Cut(SurfaceKind k) => k switch { SurfaceKind.Asphalt => CutAsp, SurfaceKind.Concrete => CutCon, SurfaceKind.Block => CutBlk, _ => "" };

    /// <summary>열쇠의 한글 이름(표 머리).</summary>
    public static string NameOf(string key, double depthLimit = 5.0)
    {
        if (key.StartsWith("EXC."))
        {
            var p = key.Split('.');
            string wet = p[1] == "WET" ? "용수" : "육상";
            string deep = depthLimit > 0 ? (p[2] == "GT" ? $"{depthLimit:0.#}m초과" : $"{depthLimit:0.#}m이하") : "";
            string rock = p[3] switch { "SOIL" => "토사", "WR" => "풍화암", _ => "연암" };
            return $"터파기 {wet} {deep} {rock}".Replace("  ", " ");
        }
        return key switch
        {
            PavAsp => "아스콘 깨기", PavCon => "콘크리트 깨기", PavBlk => "보도블록 철거",
            Subbase => "보조기층", Bed => "관 기초(모래)", Zone => "관주위 되메우기", Upper => "관상단 되메우기",
            Encase => "콘크리트 보호공", Bore => "추진 굴착",
            TopWidth => "터파기 윗폭", BotWidth => "터파기 바닥폭", WallL => "흙막이 높이(좌)", WallR => "흙막이 높이(우)",
            CutAsp => "아스콘 복구 폭", CutCon => "콘크리트 복구 폭", CutBlk => "보도블록 복구 폭",
            CutLines => "포장 절단 줄", Curb => "연석", Depth => "터파기고", Cover => "토피",
            _ => key,
        };
    }
}

/// <summary>채워 그릴 조각(해치).</summary>
public sealed class SectionFill
{
    public string Key { get; set; } = "";
    public List<List<P2>> Polys { get; set; } = new();
}

/// <summary>선(그리기용).</summary>
public sealed class SectionLine
{
    /// <summary>GROUND · TRENCH · SHORE · WATER · DEPTH · STRATA1 · STRATA2 · LAYER</summary>
    public string Key { get; set; } = "";
    public List<P2> Pts { get; set; } = new();
}

public sealed class SectionCircle
{
    /// <summary>PIPE · CASING</summary>
    public string Key { get; set; } = "";
    public double X { get; set; }
    public double Z { get; set; }
    public double R { get; set; }
}

/// <summary>한 측점 단면의 결과.</summary>
public sealed class SectionResult
{
    public double Station { get; set; }
    public string Label { get; set; } = "";
    public MethodKind Method { get; set; }
    public bool Ok { get; set; } = true;
    public List<string> Messages { get; } = new();

    /// <summary>조각 넓이(㎡).</summary>
    public Dictionary<string, double> Area { get; } = new();
    /// <summary>길이 · 개수.</summary>
    public Dictionary<string, double> Len { get; } = new();

    public List<SectionFill> Fills { get; } = new();
    public List<SectionLine> Lines { get; } = new();
    public List<SectionCircle> Circles { get; } = new();

    public double GroundCl { get; set; }
    public double Invert { get; set; }
    public double Bottom { get; set; }
    /// <summary>터파기가 지반과 닿는 양 끝(없으면 NaN).</summary>
    public double XLeft { get; set; } = double.NaN;
    public double XRight { get; set; } = double.NaN;
    public double DomainX0 { get; set; }
    public double DomainX1 { get; set; }

    public double A(string key) => Area.TryGetValue(key, out double v) ? v : 0;
    public double L(string key) => Len.TryGetValue(key, out double v) ? v : 0;

    /// <summary>터파기 합(포장 깨기 · 보조기층 빼고 흙 · 암만).</summary>
    public double ExcTotal => Area.Where(kv => kv.Key.StartsWith("EXC.")).Sum(kv => kv.Value);
    public double BackfillTotal => A(Q.Bed) + A(Q.Zone) + A(Q.Upper);

    internal void AddArea(string key, double v)
    {
        if (string.IsNullOrEmpty(key) || !(v > 1e-9)) return;
        Area[key] = A(key) + v;
    }

    internal void SetLen(string key, double v)
    {
        if (string.IsNullOrEmpty(key)) return;
        Len[key] = v;
    }
}
