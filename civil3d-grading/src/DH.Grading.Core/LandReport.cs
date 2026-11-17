using NetTopologySuite.Geometries;
using NetTopologySuite.Geometries.Utilities;

namespace DH.Grading.Core;

/// <summary>★[용지조서 · JACK 1117] 필지 하나(도면 [지적도]의 DH_PARCEL 값 + 면). 소유자 이름 · 주소는 없다(공개 API에 없음).</summary>
public sealed class LandParcel
{
    public string Pnu = "", Jibun = "", Jimok = "", Addr = "", Kind = "", CodeText = "";
    /// <summary>대장면적(㎡ · 소유 WFS lndpcl_ar) — 모르면 -1.</summary>
    public double Ledger = -1;
    public Geometry? Shape;
}

/// <summary>소유 구분 — 국유(02) · 공유(04 시·도유지 · 05 군유지) · 사유(01 · 03 · 06~09) · 섞임 · 모름.</summary>
public enum LandOwn { State, Public, Private, Mixed, Unknown }

public sealed class LandRow
{
    public int No;
    public string Place = "", Jibun = "", Jimok = "", Pnu = "";
    /// <summary>지적면적(㎡ · 0.1) — 대장면적 · 없으면 도면 면적.</summary>
    public double Area;
    /// <summary>영구점용(편입) 면적(㎡ · 0.1) = 필지 ∩ 공사 범위.</summary>
    public double Occupied;
    public bool FromLedger;
    public LandOwn Own;
}

public sealed class LandReportModel
{
    public string Project = "";
    public List<LandRow> Rows = new();
    /// <summary>받은 필지 · 면 없는 필지 · 대장면적 없음(도면 면적 씀) · 편입이 지적보다 커서 지적면적으로 줄인 수.</summary>
    public int Parcels, NoShape, NoLedger, Clamped;
    /// <summary>소재지(리) — 줄 차례대로 한 번씩.</summary>
    public List<string> Places => Rows.Select(r => r.Place).Distinct().ToList();
}

/// <summary>★[용지조서 · JACK 1117] 필지 ∩ 공사 범위 → 편입용지조서 줄 · 집계 기준(엑셀 쓰기는 <see cref="LandReportXlsx"/>).</summary>
public static class LandReport
{
    /// <summary>집계표 줄 이름 — 지목 코드 01~28 차례(양식 «리별집계» · «소유자별집계» A6:A33) + 마지막 «미지번»(모르는 지목).</summary>
    public static readonly string[] JimokRows =
    {
        "전", "답", "과수원", "목장", "임야", "광천지", "염전", "대지", "공장", "학교", "주차장", "주유소", "창고", "도로",
        "철도", "제방", "하천", "구거", "유지", "양어장", "수도", "공원", "체육", "유원지", "종교", "사적지", "묘지", "잡종지",
    };
    public const string NoJimok = "미지번";

    /// <summary>지목 부호(«과») → 집계표 줄 이름(«과수원») · 모르면 «미지번».</summary>
    public static string RowOf(string sym)
    {
        for (int i = 1; i <= JimokRows.Length; i++)
            if (sym.Length > 0 && VWorldParcels.JimokSymOfCode(i.ToString("00", System.Globalization.CultureInfo.InvariantCulture)) == sym) return JimokRows[i - 1];
        return NoJimok;
    }

    /// <summary>도면에 적힌 소유 구분(DH_PARCEL kind · 구분 이름) → 국유 · 공유 · 사유. 국공유 중 «국유지»로 시작하면 국유 · 그 밖(시·도유지 · 군유지)은 공유.</summary>
    public static LandOwn OwnOf(string kind, string codeText) => kind switch
    {
        "pub" => (codeText ?? "").StartsWith("국유지", StringComparison.Ordinal) ? LandOwn.State : LandOwn.Public,
        "pri" => LandOwn.Private,
        "mix" => LandOwn.Mixed,
        _ => LandOwn.Unknown,
    };

    public static string OwnText(LandOwn o) => o switch
    {
        LandOwn.State => "국유", LandOwn.Public => "공유", LandOwn.Private => "사유", LandOwn.Mixed => "혼합", _ => "모름",
    };

    static readonly System.Text.RegularExpressions.Regex JibunTok = new(@"^산?\d+(-\d+)?\D?$");

    /// <summary>소재지(읍면 + 리 · 동) — VWorld 주소 «○○도 ○○군 ○○면 ○○리 123-4»에서 끝 지번을 떼고 마지막 시 · 군 · 구 뒤만.
    /// 주소가 없으면 «법정동 PNU 앞 10자리».</summary>
    public static string PlaceOf(string? addr, string? pnu)
    {
        var t = (addr ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        if (t.Count > 0 && JibunTok.IsMatch(t[^1])) t.RemoveAt(t.Count - 1);
        if (t.Count > 0 && t[^1] == "산") t.RemoveAt(t.Count - 1);
        if (t.Count == 0)
            return pnu != null && pnu.Length >= 10 && pnu.Take(10).All(char.IsAsciiDigit) ? "법정동 " + pnu.Substring(0, 10) : "소재지 모름";
        int k = -1;
        for (int i = 0; i < t.Count - 1; i++)
            if (t[i].EndsWith('시') || t[i].EndsWith('군') || t[i].EndsWith('구')) k = i;
        return string.Join(" ", t.Skip(k + 1));
    }

    /// <summary>지번 차례 열쇠 — 일반 먼저 · 산 뒤 · 본번 · 부번.</summary>
    static (int San, int Bon, int Bu, string Raw) JibunKey(string j)
    {
        string s = j.Trim();
        int san = s.StartsWith('산') ? 1 : 0;
        if (san == 1) s = s.Substring(1);
        var parts = s.Split('-');
        int bon = int.TryParse(parts[0], out int b) ? b : int.MaxValue;
        int bu = parts.Length > 1 && int.TryParse(parts[1], out int u) ? u : 0;
        return (san, bon, bu, j);
    }

    public static double R1(double v) => Math.Round(v, 1, MidpointRounding.AwayFromZero);

    /// <summary>필지마다 공사 범위와 겹친 넓이(0.1 ㎡) — 0이면 뺌. 지적면적 = 대장면적(있으면) · 없으면 도면 면 넓이. 편입 &gt; 지적이면 지적면적까지.</summary>
    public static LandReportModel Build(IEnumerable<LandParcel> parcels, Geometry footprint, string project)
    {
        var m = new LandReportModel { Project = project };
        var fp = footprint.IsValid ? footprint : GeometryFixer.Fix(footprint);
        var prep = NetTopologySuite.Geometries.Prepared.PreparedGeometryFactory.Prepare(fp);
        var rows = new List<LandRow>();
        foreach (var p in parcels)
        {
            m.Parcels++;
            if (p.Shape == null || p.Shape.IsEmpty) { m.NoShape++; continue; }
            var g = p.Shape.IsValid ? p.Shape : GeometryFixer.Fix(p.Shape);
            if (!prep.Intersects(g)) continue;
            double occ;
            try { occ = fp.Intersection(g).Area; }
            catch { try { occ = fp.Buffer(0).Intersection(g.Buffer(0)).Area; } catch { m.NoShape++; continue; } }
            occ = R1(occ);
            if (occ <= 0) continue;
            bool led = p.Ledger > 0 && double.IsFinite(p.Ledger);
            double area = R1(led ? p.Ledger : g.Area);
            if (!led) m.NoLedger++;
            if (occ > area) { occ = area; m.Clamped++; }
            rows.Add(new LandRow
            {
                Place = PlaceOf(p.Addr, p.Pnu), Jibun = p.Jibun.Length > 0 ? p.Jibun : VWorldParcels.JibunFromPnu(p.Pnu), Jimok = p.Jimok, Pnu = p.Pnu,
                Area = area, Occupied = occ, FromLedger = led, Own = OwnOf(p.Kind, p.CodeText),
            });
        }
        m.Rows = rows.OrderBy(r => r.Place, StringComparer.Ordinal).ThenBy(r => JibunKey(r.Jibun)).ToList();
        for (int i = 0; i < m.Rows.Count; i++) m.Rows[i].No = i + 1;
        return m;
    }
}
