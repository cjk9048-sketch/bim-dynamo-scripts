// ★[v115 · JACK 1105 임상도] 임상도 SHP → 2 km 타일(DHF1) 변환기 — 계획 2판 3절.
//   dotnet run -c Release --project tools\ForestTiles -- build --src <임상도 폴더> --out <창고> [--threads 16] [--only 11,36] [--force]
//   dotnet run -c Release --project tools\ForestTiles -- verify --out <창고> [--threads 16]
//   dotnet run -c Release --project tools\ForestTiles -- at --out <창고> --x <5179 X> --y <5179 Y>
//   원자료 폴더에는 쓰지 않는다 · 창고 = 저장소 밖(기본 바탕화면\임상도_타일).
using System.Globalization;
using DH.Grading.Core.Forest;

Console.OutputEncoding = System.Text.Encoding.UTF8;
string? Arg(string k) { int i = Array.IndexOf(args, k); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }
string cmd = args.Length > 0 ? args[0] : "";
string desk = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
string outDir = Arg("--out") ?? Path.Combine(desk, "임상도_타일");
int threads = int.TryParse(Arg("--threads"), out int th) ? th : Math.Min(16, Environment.ProcessorCount);
try
{
    switch (cmd)
    {
        case "build":
        {
            string src = Arg("--src") ?? Path.Combine(desk, "임상도");
            string fo = Path.GetFullPath(outDir).TrimEnd('\\'), fs = Path.GetFullPath(src).TrimEnd('\\');
            if (string.Equals(fo, fs, StringComparison.OrdinalIgnoreCase) || fo.StartsWith(fs + "\\", StringComparison.OrdinalIgnoreCase))
            { Console.Error.WriteLine("창고가 원자료 폴더 안입니다 — 원자료 폴더에는 쓰지 않습니다"); return 2; }
            var o = new ForestConvert.Options
            {
                Src = src, Out = outDir, Threads = threads, Force = args.Contains("--force"),
                Simplify = double.TryParse(Arg("--simplify"), NumberStyles.Float, CultureInfo.InvariantCulture, out double sm) ? sm : 1.0,
                Only = Arg("--only") is { } only ? new HashSet<string>(only.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries), StringComparer.Ordinal) : null,
            };
            Directory.CreateDirectory(outDir);
            using var log = new StreamWriter(Path.Combine(outDir, "build.log"), append: true) { AutoFlush = true };
            o.Log = s => { string l = $"{DateTime.Now:HH:mm:ss} {s}"; Console.WriteLine(l); lock (log) log.WriteLine(l); };
            o.Log($"── build {string.Join(' ', args)}");
            var ix = ForestConvert.Build(o);
            if (ix == null) return 1;
            var v = ForestConvert.Verify(outDir, threads, o.Log);
            return v.Ok ? 0 : 1;
        }
        case "verify":
        {
            var v = ForestConvert.Verify(outDir, threads);
            return v.Ok ? 0 : 1;
        }
        case "at":
        {
            double x = double.Parse(Arg("--x") ?? "0", CultureInfo.InvariantCulture), y = double.Parse(Arg("--y") ?? "0", CultureInfo.InvariantCulture);
            foreach (var l in ForestConvert.At(outDir, x, y)) Console.WriteLine(l);
            return 0;
        }
        default:
            Console.WriteLine("쓰는 법: build --src <임상도> --out <창고> [--threads 16] [--only 11,36] [--force] | verify --out <창고> | at --out <창고> --x <X> --y <Y>");
            return 2;
    }
}
catch (Exception ex)
{
    Console.Error.WriteLine("실패 — " + ex);
    return 1;
}
