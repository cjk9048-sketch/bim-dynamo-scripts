// 옹벽 표면 시험 페이지(지어낸 좌표 — 현장 값 없음) — 형식 다섯 줄 → WallScene.Build → «DH3D» 페이지(Web3DBuilder와 같은 머리 꼴)
//   dotnet run --project tools/wallpage -- <web3d 폴더> <나갈 html>   →   node web3d/tools/shot.mjs <html> <폴더> [보기 JSON]
using DH.Grading.Core;

var C = new WallSection.Common(0.10, 0.10, 0.02, 0.01);
WallExcav.SecLine Line(WallSection.Kind k, IEnumerable<(double X, double Y, double Toe, double H)> pts, bool cut)
{
    var t = WallSection.DefaultTable(k);
    var ln = new WallExcav.SecLine { Kind = k, Soil = cut ? 1 : -1, Up = cut, SlopeW = 0.01, Src = "시험 " + WallSection.Of(k).Name };
    double s = 0; (double X, double Y)? prev = null;
    foreach (var (x, y, toe, h) in pts)
    {
        if (prev != null) s += Math.Sqrt((x - prev.Value.X) * (x - prev.Value.X) + (y - prev.Value.Y) * (y - prev.Value.Y));
        prev = (x, y);
        var row = WallSection.Lookup(t, h, out _, out _, out _, out _);
        ln.Pts.Add(new WallExcav.SecPoint { X = x, Y = y, S = s, Toe = toe, H = h, Row = t.Rows.IndexOf(row), Shape = WallSection.Build(t, h, C, cut) });
    }
    return ln;
}

var kinds = new[] { WallSection.Kind.역T형, WallSection.Kind.보강토, WallSection.Kind.패널식, WallSection.Kind.찰쌓기, WallSection.Kind.중력식 };
var lines = new List<WallExcav.SecLine>();
for (int k = 0; k < kinds.Length; k++)
{
    double y0 = 1000 + k * 25;
    var pts = new List<(double, double, double, double)>();
    for (int i = 0; i <= 30; i++) pts.Add((1000 + i, y0, 50.0, 3.0 + 2.0 * i / 30));      // 곧게 30 m · 높이 3 → 5 m
    for (int i = 1; i <= 10; i++) pts.Add((1030 + i * 0.8, y0 + i * 0.6, 50.0, 5.0));      // 꺾여 10 m
    lines.Add(Line(kinds[k], pts, kinds[k] != WallSection.Kind.보강토));
}
double ox = 1000, oy = 1000;
var r = WallScene.Build(lines, ox, oy, true);
Console.WriteLine(r.Text);

var sc = new Web3DScene();
var H = sc.Head;
int n = 24; double gx = -40, gy = -40, gw = 120, gh = 160;
var pos = new List<float>(); var idx = new List<int>();
for (int j = 0; j <= n; j++) for (int i = 0; i <= n; i++) { pos.Add((float)(gx + gw * i / n)); pos.Add((float)(gy + gh * j / n)); pos.Add(50f); }
for (int j = 0; j < n; j++) for (int i = 0; i < n; i++) { int a = j * (n + 1) + i, b = a + 1, c = a + n + 1, d = c + 1; idx.AddRange(new[] { a, b, d, a, d, c }); }
H["dwg"] = "wall-face-test"; H["origin"] = new[] { ox, oy };
H["bbox"] = new[] { gx, gy, 45.0, gx + gw, gy + gh, 60.0 };
H["focus"] = new[] { -5.0, -10.0, 49.0, 45.0, 120.0, 56.0 };
H["geo"] = new Dictionary<string, object> { ["lat"] = 37.5, ["lon"] = 127.0 };
H["terrain"] = new Dictionary<string, object?>
{
    ["pos"] = sc.F32(pos.ToArray()), ["idx"] = sc.U32(idx.ToArray()), ["ranges"] = new[] { new[] { 0, 0, idx.Count / 3 } },
    ["classes"] = new[] { new Dictionary<string, object> { ["key"] = "plan", ["name"] = "계획면" } },
};
H["walls"] = r.Walls.Select(w => (object)new Dictionary<string, object>
{
    ["id"] = w.Id, ["name"] = w.Name, ["kind"] = w.Kind, ["up"] = w.Up, ["src"] = w.Src,
    ["len"] = Web3DScene.Num(w.Len, 2), ["hmin"] = Web3DScene.Num(w.HMin, 2), ["hmax"] = Web3DScene.Num(w.HMax, 2),
    ["box"] = w.Box.Select(v => Web3DScene.Num(v, 2)).ToArray(), ["sta"] = sc.F32(w.Sta),
    ["parts"] = w.Parts.Select(p => (object)new Dictionary<string, object> { ["mat"] = p.Mat, ["pos"] = sc.F32(p.Pos), ["idx"] = sc.U32(p.Idx) }).ToList(),
}).ToList();
H["patterns"] = r.Pats.Select(p =>
{
    var d = new Dictionary<string, object> { ["wall"] = p.Wall, ["layer"] = p.Layer, ["color"] = new[] { (int)p.Rgb.R, p.Rgb.G, p.Rgb.B }, ["pos"] = sc.F32(p.Pos), ["idx"] = sc.U32(p.Idx) };
    if (p.Fuv.Length == p.Pos.Length) d["fuv"] = sc.F32(p.Fuv);
    return (object)d;
}).ToList();
string web = args[0], outp = args[1];
using (var wr = new StreamWriter(outp))
    Web3DScene.WritePage(wr, File.ReadAllText(Path.Combine(web, "page.html")), File.ReadAllText(Path.Combine(web, "dist", "viewer.js")), "옹벽 표면 시험", sc.PackGzip());
Console.WriteLine($"{outp} · 무늬 메시 {r.Pats.Count} · fuv {r.Pats.Count(p => p.Fuv.Length == p.Pos.Length)} · 세모 {r.Tris:N0}");
