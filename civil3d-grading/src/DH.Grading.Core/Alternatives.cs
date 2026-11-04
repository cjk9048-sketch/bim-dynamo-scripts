using System.Text.Json;

namespace DH.Grading.Core;

/// <summary>★★[v112.1 · JACK 1104 인터뷰 «대안 저장»] <b>대안 = 도면 사본</b>(계획지표면 + 진입도로 함께 · 수량표 나란히).
/// <para>폴더 = «도면이름_대안»(원본 옆) · 안에 대안마다 «이름.dwg» + 목록 «대안목록.json»(이름 · 파일 · 저장 시각 · 수량 · 메모).
/// 대안 사본을 연 채 저장하면 같은 폴더(부모 이름이 «_대안»으로 끝나면 그 폴더).</para>
/// <para>수량 = 참고 수치: 토공(최종지표면 − 원지반 · 도면화 토공량표와 같은 셈) · 옹벽 길이(앞면 선) · 사면 겉넓이(땅을 바꾼 자리 중 경사가 문턱보다 가파른 세모) · 도로 길이(노선).</para></summary>
public static class Alternatives
{
    public const string Suffix = "_대안";
    public const string ListFile = "대안목록.json";
    public const int Ver = 1;

    /// <summary>한 줄 — 수량은 못 쟀으면 NaN(JSON엔 null).</summary>
    public sealed class Row
    {
        public string Name { get; set; } = "";
        public string File { get; set; } = "";
        public DateTime Saved { get; set; }
        public double? Cut { get; set; }
        public double? Fill { get; set; }
        public double? WallLen { get; set; }
        public double? SlopeArea { get; set; }
        public double? RoadLen { get; set; }
        public string Memo { get; set; } = "";
    }

    public sealed class Book
    {
        public int Ver { get; set; } = Alternatives.Ver;
        /// <summary>원본 도면 파일 이름(확장자 포함 · 사본에서 열었을 때 보여 줌).</summary>
        public string Base { get; set; } = "";
        public List<Row> Rows { get; set; } = new();
    }

    static readonly JsonSerializerOptions Opt = new() { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>도면 경로 → 대안 폴더. 도면이 이미 대안 폴더 안이면 그 폴더.</summary>
    public static string FolderFor(string dwgPath)
    {
        string dir = Path.GetDirectoryName(Path.GetFullPath(dwgPath)) ?? "";
        if (Path.GetFileName(dir).EndsWith(Suffix, StringComparison.Ordinal)) return dir;
        return Path.Combine(dir, Path.GetFileNameWithoutExtension(dwgPath) + Suffix);
    }

    /// <summary>이 도면이 대안 사본인가(대안 폴더 안).</summary>
    public static bool IsCopy(string dwgPath)
        => Path.GetFileName(Path.GetDirectoryName(Path.GetFullPath(dwgPath)) ?? "").EndsWith(Suffix, StringComparison.Ordinal);

    public static Book Load(string folder)
    {
        string p = Path.Combine(folder, ListFile);
        if (!System.IO.File.Exists(p)) return new Book();
        var b = JsonSerializer.Deserialize<Book>(System.IO.File.ReadAllText(p), Opt) ?? new Book();
        // 손댄 목록 — 빈 줄 · null 글은 빈 글로(이름 · 파일 없는 줄은 [지우기]로 치울 수 있게 남김)
        b.Rows = (b.Rows ?? new()).Where(r => r != null).ToList();
        foreach (var r in b.Rows) { r.Name ??= ""; r.File ??= ""; r.Memo ??= ""; }
        b.Base ??= "";
        return b;
    }

    /// <summary>쓰기 — 임시 파일에 쓴 뒤 바꿔 끼움(반쯤 쓴 목록이 남지 않게).</summary>
    public static void Save(string folder, Book b)
    {
        Directory.CreateDirectory(folder);
        string p = Path.Combine(folder, ListFile), t = p + ".tmp";
        System.IO.File.WriteAllText(t, JsonSerializer.Serialize(b, Opt));
        if (System.IO.File.Exists(p)) System.IO.File.Replace(t, p, null); else System.IO.File.Move(t, p);
    }

    /// <summary>다음 기본 이름 «대안N»(있는 것 중 가장 큰 N + 1).</summary>
    public static string NextName(IEnumerable<string> names)
    {
        int n = 0;
        foreach (var s in names)
            if (s != null && s.StartsWith("대안", StringComparison.Ordinal) && int.TryParse(s.AsSpan(2), out int k)) n = Math.Max(n, k);
        return "대안" + (n + 1);
    }

    /// <summary>이름 확인 — 빈 이름 · 파일 이름에 못 쓰는 글자 · 같은 이름(대소문자 무시)이면 까닭(괜찮으면 빈 글).</summary>
    public static string CheckName(string name, IEnumerable<string> taken)
    {
        name = (name ?? "").Trim();
        if (name.Length == 0) return "이름이 비었습니다";
        if (name.Length > 60) return "이름이 너무 깁니다(60자까지)";
        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.EndsWith('.')) return "파일 이름에 못 쓰는 글자가 있습니다";
        string stem = name.Split('.')[0].Trim().ToUpperInvariant();
        if (stem is "CON" or "PRN" or "AUX" or "NUL" || (stem.Length == 4 && (stem.StartsWith("COM") || stem.StartsWith("LPT")) && char.IsDigit(stem[3])))
            return "Windows가 쓰는 이름이라 파일 이름으로 못 씁니다";
        if (taken.Any(t => string.Equals(t, name, StringComparison.OrdinalIgnoreCase))) return "같은 이름의 대안이 있습니다";
        return "";
    }

    /// <summary>앞면 선 길이 — <paramref name="on"/>이 점 수와 같으면 두 끝이 다 옹벽인 토막만(아니면 전부).</summary>
    public static double LineLength(IReadOnlyList<Point3> pts, IReadOnlyList<bool>? on)
    {
        bool mask = on != null && on.Count == pts.Count;
        double L = 0;
        for (int i = 0; i + 1 < pts.Count; i++)
        {
            if (mask && !(on![i] && on[i + 1])) continue;
            L += Math.Sqrt((pts[i + 1].X - pts[i].X) * (pts[i + 1].X - pts[i].X) + (pts[i + 1].Y - pts[i].Y) * (pts[i + 1].Y - pts[i].Y));
        }
        return L;
    }

    /// <summary>사면 겉넓이(㎡ · 3D) — 세모 기울기(높이/수평)가 <paramref name="minGrade"/> 이상 · <paramref name="maxGrade"/> 미만이고
    /// 무게중심에서 원지반과 <paramref name="dz"/> 넘게 다른(땅을 바꾼) 세모만. 원지반이 없는 자리는 뺌.
    /// 기본 문턱 = 1:3(33 %) ~ 1:0.3(옹벽 앞면은 뺌).</summary>
    public static double SlopeArea(IEnumerable<(Point3 A, Point3 B, Point3 C)> design, Func<double, double, double?> ground,
                                   double minGrade = 1 / 3.0, double maxGrade = 1 / 0.3, double dz = 0.05)
    {
        double sum = 0;
        foreach (var (a, b, c) in design)
        {
            double ux = b.X - a.X, uy = b.Y - a.Y, uz = b.Z - a.Z, vx = c.X - a.X, vy = c.Y - a.Y, vz = c.Z - a.Z;
            double nx = uy * vz - uz * vy, ny = uz * vx - ux * vz, nz = ux * vy - uy * vx;
            double h = Math.Sqrt(nx * nx + ny * ny), area3 = 0.5 * Math.Sqrt(nx * nx + ny * ny + nz * nz);
            if (area3 <= 1e-12 || Math.Abs(nz) <= 1e-12) continue;
            double grade = h / Math.Abs(nz);
            if (grade < minGrade || grade >= maxGrade) continue;
            double cx = (a.X + b.X + c.X) / 3, cy = (a.Y + b.Y + c.Y) / 3, cz = (a.Z + b.Z + c.Z) / 3;
            var g = ground(cx, cy);
            if (g == null || !double.IsFinite(g.Value) || Math.Abs(cz - g.Value) <= dz) continue;
            sum += area3;
        }
        return sum;
    }
}
