// ── S242 ★[v116.9 · JACK 1110 «구조물 좌표계 내보내기»] Civil → Revit 기준점 파일 — 만든 좌표(현장 값 없음)
using System.Text.Json;
using DH.Grading.Core;

static class StructPointsCheck
{
    public static void Run(Action<string, bool, string> check0)
    {
        void Check(string n, bool ok, string d) => check0("S242 SP" + n, ok, d);
        Console.WriteLine("\n== S242 구조물 기준점 파일(Civil → Revit) ==");
        // ① 단위 표
        bool u = StructPoints.TryToMeter(6, out double f6, out bool a6) && f6 == 1 && !a6
              && StructPoints.TryToMeter(4, out double f4, out _) && f4 == 0.001
              && StructPoints.TryToMeter(0, out double f0, out bool a0) && f0 == 1 && a0
              && !StructPoints.TryToMeter(7, out _, out _) && !StructPoints.TryToMeter(3, out _, out _);
        Check(" ① 단위 — m 1 · mm 0.001 · 없음 = m로 봄 · km · 마일은 막음", u, "");
        // ② JSON — 왕복 · 형식 · 한글 · null
        var p1 = new StructPoints.Pt(123456.78912345678, 234567.89123456789, 0, "원지반", 71.85012, "최종지표면_DH", 72.1);
        var p2 = new StructPoints.Pt(123488.6891, 234566.8301, 0.25, "원지반", null, "", 72.2);
        var info = new StructPoints.Info("가상 구조물 «A»", 5186, "KOREA_GRS80_127TM", "drawing", "가상.dwg", @"C:\가상\가상.dwg", 6, true, "Civil 3D · AutoCAD 25.1", "DH.Grading v116.9.0", new DateTimeOffset(2026, 11, 10, 15, 20, 0, TimeSpan.FromHours(9)));
        string js = StructPoints.Json(info, p1, p2);
        using var jd = JsonDocument.Parse(js);
        var r = jd.RootElement;
        var pts = r.GetProperty("points");
        bool rt = pts.GetArrayLength() == 2 && pts[0].GetProperty("no").GetInt32() == 1 && pts[1].GetProperty("no").GetInt32() == 2
                  && pts[0].GetProperty("x").GetDouble() == p1.X && pts[0].GetProperty("y").GetDouble() == p1.Y
                  && pts[1].GetProperty("x").GetDouble() == p2.X && pts[1].GetProperty("y").GetDouble() == p2.Y && pts[1].GetProperty("z").GetDouble() == 0.25;
        Check(" ② 좌표 왕복(비트 그대로) · 점 둘 · 찍은 순서", rt, $"x1 {pts[0].GetProperty("x").GetRawText()}");
        bool fm = r.GetProperty("format").GetString() == StructPoints.Format && r.GetProperty("version").GetInt32() == 1
                  && r.GetProperty("units").GetProperty("length").GetString() == "m" && r.GetProperty("crs").GetProperty("epsg").GetInt32() == 5186
                  && r.GetProperty("crs").GetProperty("name").GetString() == "Korea 2000 / Central Belt 2010"
                  && r.GetProperty("drawing").GetProperty("insunitsName").GetString() == "Meters" && !r.GetProperty("drawing").GetProperty("unitAssumed").GetBoolean()
                  && r.GetProperty("created").GetString() == "2026-11-10T15:20:00+09:00";
        Check(" ③ 형식 · 판 · 단위 m · EPSG 이름 · 만든 때(+09:00)", fm, "");
        bool ko = js.Contains("가상 구조물 «A»") && r.GetProperty("structure").GetProperty("name").GetString() == "가상 구조물 «A»" && !js.Contains("\\u");
        Check(" ④ 한글 그대로(\\u 없음)", ko, "");
        bool nul = pts[0].GetProperty("ground").GetProperty("elevation").GetDouble() == 71.8501 && pts[0].GetProperty("final").GetProperty("surface").GetString() == "최종지표면_DH"
                   && pts[1].GetProperty("ground").ValueKind == JsonValueKind.Null && pts[1].GetProperty("final").ValueKind == JsonValueKind.Null;
        Check(" ⑤ 표고 — 있으면 면 이름 · 값(0.1 mm) · 없으면 null(지어내지 않음)", nul, "");
        // ⑥ check — Revit 쪽 식(x · y로 다시 셈)과 1 mm · 0.01° 안
        double dx = p2.X - p1.X, dy = p2.Y - p1.Y, d = Math.Sqrt(dx * dx + dy * dy), b = Math.Atan2(dy, dx) * 180 / Math.PI;
        var ck = r.GetProperty("check");
        Check(" ⑥ check 거리 · 방향 = x · y로 다시 셈", Math.Abs(ck.GetProperty("distance").GetDouble() - d) < 1e-3 && Math.Abs(ck.GetProperty("bearingDeg").GetDouble() - b) < 1e-2,
            $"거리 {ck.GetProperty("distance").GetDouble()} · 방향 {ck.GetProperty("bearingDeg").GetDouble()}");
        // ⑦ 단위 없음 · 좌표계 모름
        var info0 = info with { Insunits = 0, Epsg = 0, CivilCode = "", CrsSource = "settings" };
        using var j0 = JsonDocument.Parse(StructPoints.Json(info0, p1, p2));
        Check(" ⑦ 단위 없음 = unitAssumed · EPSG 모름 = null", j0.RootElement.GetProperty("drawing").GetProperty("unitAssumed").GetBoolean() && j0.RootElement.GetProperty("crs").GetProperty("epsg").ValueKind == JsonValueKind.Null, "");
        // ⑧ 파일 이름
        Check(" ⑧ 파일 이름 — 못 쓰는 글자 «_» · 빈 이름 = 구조물", StructPoints.SafeFileName("A/B:C*?") == "A_B_C__" && StructPoints.SafeFileName("  ") == "구조물" && StructPoints.SafeFileName("배수 구조물 1") == "배수 구조물 1",
            StructPoints.SafeFileName("A/B:C*?"));
        // ⑨ ★[v116.13 · 약속 §3 «sketch»] 미리보기 선 — 없으면 칸 자체가 없음 · 있으면 center · radius · lines(kind · layer · closed · pts [[x, y]])
        Check(" ⑨ sketch 없음 = 칸 없음(선택 칸)", !r.TryGetProperty("sketch", out _), "");
        var sk = new StructPoints.Sketch(123472.73, 234567.36, StructPoints.SketchRadius(31.9), new[]
        {
            new StructPoints.SketchLine("site", "가상-경계", true, new[] { (123400.0, 234500.0), (123550.0, 234500.0), (123550.0, 234650.0) }),
            new StructPoints.SketchLine("wall", "DH-옹벽선", false, new[] { (123410.1234, 234510.5678), (123420.0, 234520.0) }),
        });
        using var js2 = JsonDocument.Parse(StructPoints.Json(info, p1, p2, sk));
        var s2 = js2.RootElement.GetProperty("sketch");
        bool skOk = s2.GetProperty("radius").GetDouble() == 95.7 && s2.GetProperty("center").GetArrayLength() == 2 && s2.GetProperty("lines").GetArrayLength() == 2
                    && s2.GetProperty("lines")[0].GetProperty("kind").GetString() == "site" && s2.GetProperty("lines")[0].GetProperty("closed").GetBoolean()
                    && s2.GetProperty("lines")[1].GetProperty("pts")[0][0].GetDouble() == 123410.123 && s2.GetProperty("lines")[1].GetProperty("layer").GetString() == "DH-옹벽선"
                    && js2.RootElement.GetProperty("points")[0].GetProperty("x").GetDouble() == p1.X;   // 점 좌표는 그대로(미리보기만 1 mm)
        Check(" ⑨-b sketch 꼴 — center · radius(80 m와 거리 3배 중 큰 값) · lines kind · closed · pts(1 mm) · 점 좌표 그대로", skOk, s2.GetRawText().Length.ToString());
        // ⑩ 5만 점 넘으면 other → contour → bench … 차례로 뺌 · 2점 미만 · 숫자 아닌 선도 뺌
        var many = new List<StructPoints.SketchLine>
        {
            new("other", "a", false, Enumerable.Range(0, 30000).Select(k => ((double)k, 0.0)).ToList()),
            new("bench", "b", false, Enumerable.Range(0, 15000).Select(k => ((double)k, 1.0)).ToList()),
            new("wall", "c", false, Enumerable.Range(0, 10000).Select(k => ((double)k, 2.0)).ToList()),
            new("site", "d", false, new[] { (0.0, 0.0) }),
            new("road", "e", false, new[] { (0.0, 0.0), (double.NaN, 1.0) }),
        };
        var (kept, dropped) = StructPoints.Trim(many);
        Check(" ⑩ 꼭짓점 5만 한도 — other부터 뺌 · 1점 · NaN 선 뺌", kept.Count == 2 && kept.All(l => l.Kind is "bench" or "wall") && dropped == 3 && kept.Sum(l => l.Pts.Count) == 25000,
            $"남김 {kept.Count}({string.Join(",", kept.Select(l => l.Kind))}) · 뺌 {dropped}");
    }
}
