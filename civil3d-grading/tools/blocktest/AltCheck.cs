// ── S193 AL ★★[v112.1 · JACK 1104 «대안 저장» — 도면 사본 · 수량표 나란히] 대안 폴더 · 목록 · 이름 · 옹벽 길이 · 사면 겉넓이 ──
using DH.Grading.Core;

static class AltCheck
{
    public static void Run(Action<string, bool, string> check)
    {
        Console.WriteLine("\n== S193 AL 대안(폴더 · 목록 왕복 · 이름 · 옹벽 앞면 선 길이 · 사면 겉넓이) ==");
        // ① 폴더 — 원본 옆 «이름_대안» · 사본 안에서는 그 폴더
        {
            string a = Alternatives.FolderFor(@"C:\p\현장\부지.dwg"), b = Alternatives.FolderFor(@"C:\p\현장\부지_대안\대안1.dwg");
            check("S193 AL① 폴더 — 원본 옆 «부지_대안» · 사본 안이면 같은 폴더 · 사본 판정",
                  a == @"C:\p\현장\부지_대안" && b == @"C:\p\현장\부지_대안" && Alternatives.IsCopy(@"C:\p\현장\부지_대안\대안1.dwg") && !Alternatives.IsCopy(@"C:\p\현장\부지.dwg"), a + " | " + b);
        }
        // ② 목록 왕복 — 한글 이름 · 메모 · 못 잰 값(null) · 시각 그대로 · 덮어쓰기(Replace)
        {
            string dir = Path.Combine(Path.GetTempPath(), "dh_alt_" + Guid.NewGuid().ToString("N"));
            try
            {
                var t = new DateTime(2026, 11, 4, 9, 30, 0);
                var bk = new Alternatives.Book { Base = "부지.dwg" };
                bk.Rows.Add(new Alternatives.Row { Name = "대안1", File = "대안1.dwg", Saved = t, Cut = 1234.5, Fill = 678.9, WallLen = 45.6, SlopeArea = 789.0, RoadLen = 120.0, Memo = "옹벽 많이 · \"따옴표\"" });
                bk.Rows.Add(new Alternatives.Row { Name = "대안2", File = "대안2.dwg", Saved = t.AddMinutes(5), Cut = null, Fill = null });
                Alternatives.Save(dir, bk);
                bk.Rows[1].Memo = "고침";
                Alternatives.Save(dir, bk);   // 두 번째 = Replace 길
                var r = Alternatives.Load(dir);
                bool ok = r.Ver == Alternatives.Ver && r.Base == "부지.dwg" && r.Rows.Count == 2 && r.Rows[0].Memo == "옹벽 많이 · \"따옴표\"" && r.Rows[0].Cut == 1234.5 && r.Rows[0].Saved == t
                          && r.Rows[1].Cut == null && r.Rows[1].Memo == "고침" && !File.Exists(Path.Combine(dir, Alternatives.ListFile + ".tmp"));
                var empty = Alternatives.Load(Path.Combine(dir, "없음"));
                // 손댄 목록 — null 줄 · null 글
                string d2 = Path.Combine(dir, "손댐"); Directory.CreateDirectory(d2);
                File.WriteAllText(Path.Combine(d2, Alternatives.ListFile), "{\"Ver\":1,\"Base\":null,\"Rows\":[null,{\"Name\":null,\"File\":null,\"Saved\":\"2026-11-04T09:00:00\"}]}");
                var hand = Alternatives.Load(d2);
                ok = ok && hand.Rows.Count == 1 && hand.Rows[0].Name == "" && hand.Rows[0].File == "" && hand.Rows[0].Memo == "" && hand.Base == "";
                check("S193 AL② 목록 왕복 — 한글 · 메모 따옴표 · null 값 · 시각 · 두 번 쓰기(임시 파일 안 남음) · 없는 폴더 = 빈 목록", ok && empty.Rows.Count == 0,
                      File.ReadAllText(Path.Combine(dir, Alternatives.ListFile)).Replace("\r", "").Replace("\n", " ").Substring(0, 80));
            }
            finally { try { Directory.Delete(dir, true); } catch { } }
        }
        // ③ 이름 — 다음 번호 · 거절(빈 · 못 쓰는 글자 · 같은 이름 대소문자 무시 · 점으로 끝남)
        {
            string n1 = Alternatives.NextName(Array.Empty<string>()), n2 = Alternatives.NextName(new[] { "대안1", "대안7", "옹벽안", "대안x" });
            bool ok = n1 == "대안1" && n2 == "대안8"
                      && Alternatives.CheckName("", new string[0]).Length > 0 && Alternatives.CheckName("a/b", new string[0]).Length > 0
                      && Alternatives.CheckName("Plan", new[] { "plan" }).Length > 0 && Alternatives.CheckName("안.", new string[0]).Length > 0
                      && Alternatives.CheckName(" 옹벽 줄인 안 ", new[] { "대안1" }) == ""
                      && Alternatives.CheckName("con", new string[0]).Length > 0 && Alternatives.CheckName("COM1", new string[0]).Length > 0 && Alternatives.CheckName("comma", new string[0]) == ""
                      && Alternatives.NextName(new string[] { null!, "대안2" }) == "대안3";
            check("S193 AL③ 이름 — 대안1 · 대안8(가장 큰 번호 + 1) · 빈/못 쓰는 글자/같은 이름/점 끝/CON · COM1 거절 · 앞뒤 빈칸 · comma 괜찮음 · null 이름 건너뜀", ok, n1 + " " + n2);
        }
        // ④ 옹벽 앞면 선 길이 — 표 없으면 전부 · 표 있으면 두 끝 다 옹벽인 토막만
        {
            var p = new List<Point3> { new(0, 0, 0), new(3, 4, 0), new(3, 10, 0), new(13, 10, 0) };
            double all = Alternatives.LineLength(p, null), part = Alternatives.LineLength(p, new[] { true, true, false, true }), bad = Alternatives.LineLength(p, new[] { true });
            check("S193 AL④ 옹벽 앞면 선 — 전부 21 · 옹벽 토막만 5 · 표 길이 다르면 전부", Math.Abs(all - 21) < 1e-12 && Math.Abs(part - 5) < 1e-12 && Math.Abs(bad - 21) < 1e-12, $"{all} {part} {bad}");
        }
        // ⑤ 사면 겉넓이 — 1:1.5 사면(바뀐 자리) 셈 · 평평한 바닥 · 옹벽 앞면(1:0.1) · 안 바뀐 가파른 원지반 · 원지반 없는 자리는 뺌
        {
            var tris = new List<(Point3, Point3, Point3)>();
            void Quad(Point3 a, Point3 b, Point3 c, Point3 d) { tris.Add((a, b, c)); tris.Add((a, c, d)); }
            Quad(new(0, 0, 0), new(10, 0, 0), new(10, 3, 2), new(0, 3, 2));          // 1:1.5 오르막 사면 10 × 3(수평) — 겉넓이 10 × √13
            Quad(new(0, 3, 2), new(10, 3, 2), new(10, 13, 2), new(0, 13, 2));        // 평평한 바닥
            Quad(new(20, 0, 0), new(30, 0, 0), new(30, 0.5, 5), new(20, 0.5, 5));    // 옹벽 앞면(1:0.1)
            Quad(new(40, 0, 0), new(50, 0, 0), new(50, 3, 2), new(40, 3, 2));        // 안 바뀐 사면(원지반 = 그대로)
            Quad(new(60, 0, 0), new(70, 0, 0), new(70, 3, 2), new(60, 3, 2));        // 원지반 없는 자리
            double? G(double x, double y) => x >= 60 ? null : x >= 40 ? (y / 3) * 2 : -1.0;
            double s = Alternatives.SlopeArea(tris, G), want = 10 * Math.Sqrt(13);
            check("S193 AL⑤ 사면 겉넓이 — 1:1.5 바뀐 사면만(10×√13) · 바닥 · 옹벽 앞면 · 안 바뀐 사면 · 원지반 없는 자리 뺌", Math.Abs(s - want) < 1e-9, $"{s:F6} 기대 {want:F6}");
        }
    }
}
