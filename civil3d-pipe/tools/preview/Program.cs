// DH 관로 — 엔진 미리보기(화면 · Civil 없이).
// 가짜 지형 + 노선으로 ① ~ ⑥ 을 끝까지 돌려 횡단도 · 종단도 그림 목록(JSON) · 엑셀 · 3D(OBJ)를 폴더에 쓴다.
// 쓰기: dotnet run --project tools/preview -- <출력 폴더>
using System.Text;
using System.Text.Json;
using DH.Pipe.Core.Draw;
using DH.Pipe.Core.Export;
using DH.Pipe.Core.Geom;
using DH.Pipe.Core.Model;
using DH.Pipe.Core.Model3D;
using DH.Pipe.Core.Profile;
using DH.Pipe.Core.Quantity;
using DH.Pipe.Core.Section;
using DH.Pipe.Preview;

string outDir = args.Length > 0 ? args[0] : Path.Combine(Path.GetTempPath(), "dhpipe-preview");
Directory.CreateDirectory(outDir);
var c = Demo.Context(out Pl ground);
var route = c.Route;

// ③ 처음 종단 → 가이드안(지장물 회피 포함)
var plain = InitialProfile.Make(ground, route);
var init = InitialProfile.Make(ground, route, avoid: true);
c.Profile = init.Line;
route.Appts.Clear();
route.Appts.AddRange(Guide.Make(c.Profile, route));
Console.WriteLine($"지반 점 {ground.Count} · 처음 종단 PVI {plain.Line.Pvi.Count}(회피 뒤 {init.Line.Pvi.Count}) · 토피 평균 {init.AvgCover:0.00} 최소 {init.MinCover:0.00} 최대 {init.MaxCover:0.00}");
foreach (string n in init.Notes) Console.WriteLine("  알림: " + n);

// ④ 단면 → ⑥ 수량
var rows = RouteEngine.BuildRows(c, ground.X0, ground.X1);
var earth = EarthTable.Build(rows, route.Std.DepthLimit);
var sum = Summary.Build(c, rows, ground);
var issues = ProfileCheck.Run(c.Profile, ground, route);
var obst = ProfileCheck.Obstacles(c.Profile, ground, route);
Console.WriteLine($"측점 {rows.Count} · 못 만든 단면 {rows.Count(r => !r.After.Ok || !r.Before.Ok)} · 터파기 {earth.ExcTotal:0.00} ㎥ · 되메우기 {earth.BackfillTotal:0.00} ㎥ · 잔토 {earth.Surplus:0.00} ㎥");
foreach (var r in rows.Where(r => r.After.Messages.Count > 0)) Console.WriteLine($"  No.{r.Name} [{MethodSpan.NameOf(r.After.Method)}] {string.Join(" · ", r.After.Messages)}");
foreach (var q in sum.Spans) Console.WriteLine($"  구간 {q.StaFrom:0.0} ~ {q.StaTo:0.0} {MethodSpan.NameOf(q.Span.Kind)} 평균 깊이 {q.AvgDepth:0.00} {q.SpecText}");
foreach (var i in issues) Console.WriteLine($"  검사 [{i.Kind}] {i.StaFrom:0.0} ~ {i.StaTo:0.0} {i.Text}");
foreach (var a in route.Appts) Console.WriteLine($"  부속 {a.Sta:0.0} {Appurtenance.NameOf(a.Kind)} {a.Spec} — {a.Reason}");

// ⑤ 횡단도 · 종단도 그림 목록
var secs = new List<SectionResult>();
foreach (var r in rows) { secs.Add(r.Before); if (r.IsBreak) secs.Add(r.After); }
var keys = SectionDrawing.TableKeys(secs);
var sheets = SectionDrawing.Sheets(secs, keys, route.Std.DepthLimit, route.Std.XsecScale, route.Name);
Console.WriteLine($"횡단도 {sheets.Sheets}장 · 단면 {sheets.Cells}개 · 장당 {sheets.PerSheet}개 {string.Join(" ", sheets.Notes)}");
var prof = ProfileDrawing.Draw(new ProfileDrawing.Input
{
    Title = route.Name, Ground = ground, Line = c.Profile, Route = route,
    Stations = rows.Select(r => r.Sta).ToList(), Spans = sum.Spans, Pipes = sum.Pipes,
});
var jopt = new JsonSerializerOptions { IncludeFields = true };
File.WriteAllText(Path.Combine(outDir, "xsec.json"), JsonSerializer.Serialize(sheets.Draw.Items, jopt));
File.WriteAllText(Path.Combine(outDir, "profile.json"), JsonSerializer.Serialize(prof.Items, jopt));
// 미리보기 한 칸(도킹 창에 뜰 그림)
for (int k = 0; k < secs.Count; k++)
{
    if (k % Math.Max(1, secs.Count / 6) != 0) continue;
    var cell = SectionDrawing.Cell(secs[k], keys, route.Std.DepthLimit, route.Std.XsecScale);
    File.WriteAllText(Path.Combine(outDir, $"cell_{k:00}.json"), JsonSerializer.Serialize(cell.Draw.Items, jopt));
}

// 엑셀
string xlsx = Path.Combine(outDir, "관로_수량산출서.xlsx");
QtyWorkbook.Write(new QtyWorkbook.Input { Title = route.Name, Version = "미리보기", Route = route, Earth = earth, Sum = sum, Obstacles = obst, Issues = issues, Rows = rows }, xlsx);

// 3D(OBJ)
var meshes = Mesh3D.Build(c, rows, sum, ground);
var sb = new StringBuilder();
int baseIdx = 1;
foreach (var m in meshes)
{
    sb.AppendLine("o " + m.Key);
    foreach (var v in m.V) sb.AppendLine(FormattableString.Invariant($"v {v.X:0.###} {v.Y:0.###} {v.Z:0.###}"));
    foreach (var f in m.F) sb.AppendLine("f " + string.Join(" ", f.Select(i => i + baseIdx)));
    baseIdx += m.V.Count;
}
File.WriteAllText(Path.Combine(outDir, "model.obj"), sb.ToString());
Console.WriteLine($"3D 덩어리 {meshes.Count}개 · 면 {meshes.Sum(m => m.F.Count)}개 → {outDir}");
File.WriteAllText(Path.Combine(outDir, "route.json"), PipeJson.Save(route));
