using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using DH.Grading.Core;

namespace DH.Grading.Civil;

/// <summary>★★[v103.1.8 · JACK 1014 «최대한 현장확인말고 로그로»] <b>v103.2 첫 계측(m0)에 쓸 현장 지표면을 떠 둔다</b> — 도면 사본을 부탁하는 대신.
/// <para>m0(계획_v103_2.md 3절): 합성 전 면에서 붙여넣은 면의 테두리가 삼각형 변으로 남는가 · 순수면에 두 조각을 잇는 삼각형이 없는가 — 현장 면들이 있어야 잰다.
/// 옹벽 합성이 <b>성공한 뒤</b>, <b>개발 PC에서만</b>(진단 로그가 저장소 폴더일 때 — 배포 PC에서는 아무것도 안 한다), <b>도면마다 판마다 한 번</b> 진단이력\m0\에 압축해 둔다.</para>
/// <para>뜨는 것: m0에 쓰는 면만(정지면_DH · 정지면_DH합성전 · 정지면_DH이전 · 정지순수_DH · 정지순수_DH이전 · 가상옹벽N_DH · 순수옹벽N_DH)의 보이는 삼각형 · 원지반은 정지면_DH 둘레 ±30m만 · 옹벽 목록(번호 · 방향 · 폴리곤 · 안쪽 변).
/// 줄 하나 = 삼각형 하나(x1 y1 z1 x2 y2 z2 x3 y3 z3 · 왕복 가능한 자릿수). 읽기만 한다 — 도면은 안 바뀐다.</para>
/// <para>★[v103.1.9 · v103.1.8 검토 낮음 3-1~3-4] 삼각형마다 try + 버림(Dispose) · 실패해도 표지(합성마다 다시 뜨지 않게) · 열쇠 = 도면 고유값(FingerprintGuid — 이름 없는 도면은 세션마다 «Drawing1»이라 이름으로는 첫 도면만 떴다) ·
/// m0에 쓰는 면만(종전엔 우리 이름 꼴 면 전부 — 가상성토 · 터파기 · 옹벽뚜껑까지) · 덤프 폴더는 최근 <see cref="Keep"/>개만 남긴다.</para></summary>
internal static class M0Dump
{
    const string DevDir = @"C:\Users\user\Desktop\AI\civil3d-grading";
    /// <summary>남길 덤프 폴더 수(새것부터) — 이름 꼴 <c>yyyyMMdd_HHmmss_&lt;8자&gt;_&lt;판&gt;</c>인 폴더만 센다 · 지운다.</summary>
    internal const int Keep = 6;

    /// <summary>m0에 쓰는 면 — 이름 뒤 _숫자(같은 이름 비킴)는 허용.</summary>
    static readonly System.Text.RegularExpressions.Regex Wanted = new(@"^(정지면_DH(합성전|이전)?|정지순수_DH(이전)?|(가상|순수)옹벽\d+_DH)(_\d+)?$");
    internal static bool IsWanted(string name) => Wanted.IsMatch(name ?? "");
    static readonly System.Text.RegularExpressions.Regex DumpDirName = new(@"^\d{8}_\d{6}_[0-9a-f]{8}_");

    /// <summary>시험 칸 — 화면 없는 Civil 시험이 저장소 진단이력 대신 쓸 폴더 · 덤프 중간에 터뜨리기.</summary>
    internal static string? _rootForTest;
    internal static bool _failForTest;

    internal static bool IsDevPc()
    {
        if (_rootForTest != null) return true;
        try { return string.Equals(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(DiagLog.FilePath))?.TrimEnd('\\'), DevDir, System.StringComparison.OrdinalIgnoreCase); }
        catch { return false; }
    }

    static string Hash8(string s)
    {
        uint h = 2166136261;
        unchecked { foreach (char c in (s ?? "").ToUpperInvariant()) { h ^= c; h *= 16777619; } }
        return h.ToString("x8");
    }

    /// <summary>도면 열쇠 — 도면 고유값(FingerprintGuid · 새 도면마다 새 값) + 도면 이름. ★[v103.1.9 · 계획 검토 낮음 10] 고유값은 다른 이름으로 저장 · 파일 복사에도 그대로라고 보여(짐작 — 안 잼)
    /// 이름도 함께 섞는다 — 현장 도면을 복사해 쓴 둘째 도면이 «이미 떴다»로 건너뛰지 않게. 이름 없는 도면은 세션마다 «Drawing1»이어도 고유값이 달라 따로 뜬다.</summary>
    internal static string KeyOf(Document doc)
    {
        string fp = "";
        try { fp = doc.Database.FingerprintGuid ?? ""; } catch { }
        return Hash8(fp + "|" + doc.Name);
    }

    /// <summary>개발 PC · 이 도면 · 이 판에서 아직 안 떴으면 뜬다. 한 줄 요약(안 했으면 빈 문자열).</summary>
    internal static string MaybeDump(Document doc, ObjectId groundId)
    {
        if (!IsDevPc()) return "";
        string root = _rootForTest ?? System.IO.Path.Combine(DevDir, "진단이력", "m0");
        string key = $"{KeyOf(doc)}_{SelfCheckLog.Ver}";
        string mark = System.IO.Path.Combine(root, $"done_{key}.txt");
        try { if (System.IO.File.Exists(mark)) return ""; } catch { return ""; }
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var parts = new System.Collections.Generic.List<string>();
        // 같은 초 · 같은 열쇠의 폴더가 이미 있으면 _2 · _3 — 실패 때 지우는 폴더가 앞서 성공한 덤프일 수 없게(화면 없는 Civil 시험 W5가 잡음: 표지를 지우고 같은 초에 다시 뜨면 겹쳤다)
        string dir0 = System.IO.Path.Combine(root, $"{System.DateTime.Now:yyyyMMdd_HHmmss}_{key}"), dir = dir0;
        for (int i = 2; System.IO.Directory.Exists(dir) && i < 100; i++) dir = $"{dir0}_{i}";
        int lost = 0;
        try
        {
            System.IO.Directory.CreateDirectory(dir);
            try { doc.Editor.WriteMessage("\n[옹벽 합성] (개발 PC · 한 번만) 다음 판 계측용으로 지표면을 진단이력에 떠 둡니다…"); } catch { }
            var db = doc.Database;
            double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
            using (var tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                var cd = Autodesk.Civil.ApplicationServices.CivilDocument.GetCivilDocument(db);
                foreach (ObjectId sid in cd.GetSurfaceIds())
                {
                    if (sid.IsNull || sid.IsErased) continue;
                    if (tr.GetObject(sid, OpenMode.ForRead) is not Autodesk.Civil.DatabaseServices.TinSurface s) continue;
                    string nm = s.Name ?? "";
                    if (!IsWanted(nm)) continue;
                    var (n, bb, ls) = WriteTris(s, System.IO.Path.Combine(dir, Safe(nm) + ".tri.txt.gz"), null);
                    lost += ls;
                    parts.Add($"{nm} {n}면" + (ls > 0 ? $"(⚠읽다 버린 삼각형 {ls})" : ""));
                    if (nm == Commands.WallCompositeCommand.PlanName && n > 0) { minX = bb.MinX; minY = bb.MinY; maxX = bb.MaxX; maxY = bb.MaxY; }
                    if (_failForTest) throw new System.InvalidOperationException("시험 — 덤프 중간에 터뜨림");
                }
                if (!groundId.IsNull && minX < maxX && tr.GetObject(groundId, OpenMode.ForRead) is Autodesk.Civil.DatabaseServices.TinSurface g)
                {
                    var clip = (minX - 30, minY - 30, maxX + 30, maxY + 30);
                    var (ng, _, lg) = WriteTris(g, System.IO.Path.Combine(dir, "원지반_둘레30m.tri.txt.gz"), clip);
                    lost += lg;
                    parts.Add($"원지반(±30m) {ng}면" + (lg > 0 ? $"(⚠읽다 버린 삼각형 {lg})" : ""));
                }
                // 옹벽 목록
                try
                {
                    using var trL = db.TransactionManager.StartTransaction();
                    if (WallListStore.TryLoad(db, trL, out var head, out var walls, out _))
                    {
                        var sb = new System.Text.StringBuilder();
                        var ci = System.Globalization.CultureInfo.InvariantCulture;
                        sb.AppendLine($"# 옹벽 목록 · 확인된 정지면 {head.VerifiedP0} · 다음 번호 {head.NextNo}");
                        foreach (var w in walls)
                        {
                            sb.AppendLine($"WALL {w.No} {(w.Up ? "절토" : "성토")} 상태 {w.State} 보류 «{w.Hold}» P0 {w.P0Handle}");
                            sb.AppendLine("POLY " + string.Join(" ", (w.Poly ?? new System.Collections.Generic.List<Point3>()).Select(p => string.Format(ci, "{0:R},{1:R},{2:R}", p.X, p.Y, p.Z))));
                            sb.AppendLine("SEG " + string.Join(" ", (w.Seg ?? new System.Collections.Generic.List<Point3>()).Select(p => string.Format(ci, "{0:R},{1:R},{2:R}", p.X, p.Y, p.Z))));
                        }
                        System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "옹벽목록.txt"), sb.ToString());
                        parts.Add($"옹벽 {walls.Count}개");
                    }
                    trL.Commit();
                }
                catch (System.Exception lx) { parts.Add("옹벽 목록 못 뜸(" + lx.GetType().Name + ")"); }
                tr.Commit();
            }
            System.IO.File.WriteAllText(mark, $"{System.DateTime.Now:yyyy-MM-dd HH:mm:ss} · {doc.Name} · {dir}\n");
            long bytes = new System.IO.DirectoryInfo(dir).GetFiles().Sum(f => f.Length);
            string tidy = TidyOld(root);
            return $"m0 지표면 덤프(한 번만) — {string.Join(" · ", parts)} · {bytes / 1048576.0:F1}MB · {sw.ElapsedMilliseconds / 1000.0:F1}초 · {dir}" + (tidy.Length > 0 ? " · " + tidy : "");
        }
        catch (System.Exception ex)
        {
            // ★[v103.1.9 · 검토 낮음 3-2] 실패해도 표지를 남긴다 — 늘 실패하는 도면이면 합성마다 다시 떠(수 초) 시각 붙은 폴더가 쌓이고 ⚠ 줄이 남았다. 반쯤 쓴 폴더는 지운다
            try { System.IO.File.WriteAllText(mark, $"{System.DateTime.Now:yyyy-MM-dd HH:mm:ss} · {doc.Name} · 실패 {ex.GetType().Name}: {ex.Message}\n"); } catch { }
            try { if (System.IO.Directory.Exists(dir)) System.IO.Directory.Delete(dir, true); } catch { }
            return $"⚠m0 지표면 덤프 실패({ex.GetType().Name}: {ex.Message}) — 이 도면 · 이 판에서는 다시 안 뜬다 · {string.Join(" · ", parts)}";
        }
    }

    /// <summary>★[v103.1.9 · 검토 낮음 3-4] 덤프 폴더를 최근 <see cref="Keep"/>개만 남긴다(이름 꼴이 맞는 폴더만 · 표지 파일은 둔다 — 작다). 지운 수 한 줄(없으면 빈 문자열).</summary>
    internal static string TidyOld(string root)
    {
        try
        {
            var dirs = new System.IO.DirectoryInfo(root).GetDirectories()
                .Where(d => DumpDirName.IsMatch(d.Name))
                .OrderByDescending(d => d.Name, System.StringComparer.Ordinal)   // 이름 앞이 시각 — 새것부터
                .Skip(Keep).ToList();
            int n = 0;
            foreach (var d in dirs) { try { d.Delete(true); n++; } catch { } }
            return n > 0 ? $"옛 덤프 {n}개 지움(최근 {Keep}개만 남김)" : "";
        }
        catch { return ""; }
    }

    static string Safe(string nm)
    {
        foreach (char c in System.IO.Path.GetInvalidFileNameChars()) nm = nm.Replace(c, '_');
        return nm;
    }

    /// <summary>보이는 삼각형을 줄마다 쓴다(gzip) — <paramref name="clip"/>이 있으면 그 네모와 겹치는 삼각형만. (삼각형 수, 둘레 네모, 읽다 버린 수).
    /// ★[v103.1.9 · 검토 낮음 3-1] 삼각형마다 try · 끝나면 버린다(CachedGroundSurface와 같다 — 수십만 개 래퍼를 종료자에 넘기지 않고 · 하나의 예외가 덤프 전체를 안 망친다).</summary>
    static (int N, (double MinX, double MinY, double MaxX, double MaxY) Bb, int Lost) WriteTris(Autodesk.Civil.DatabaseServices.TinSurface s, string path,
        (double X0, double Y0, double X1, double Y1)? clip)
    {
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        int n = 0, lost = 0; double x0 = double.MaxValue, y0 = double.MaxValue, x1 = double.MinValue, y1 = double.MinValue;
        using var fs = System.IO.File.Create(path);
        using var gz = new System.IO.Compression.GZipStream(fs, System.IO.Compression.CompressionLevel.Fastest);
        using var w = new System.IO.StreamWriter(gz, new System.Text.UTF8Encoding(false));
        using var tris = s.GetTriangles(false);
        foreach (var t in tris)
        {
            try
            {
                var a = t.Vertex1.Location; var b = t.Vertex2.Location; var c = t.Vertex3.Location;
                double tx0 = System.Math.Min(a.X, System.Math.Min(b.X, c.X)), tx1 = System.Math.Max(a.X, System.Math.Max(b.X, c.X));
                double ty0 = System.Math.Min(a.Y, System.Math.Min(b.Y, c.Y)), ty1 = System.Math.Max(a.Y, System.Math.Max(b.Y, c.Y));
                if (clip != null && (tx1 < clip.Value.X0 || tx0 > clip.Value.X1 || ty1 < clip.Value.Y0 || ty0 > clip.Value.Y1)) continue;
                w.Write(string.Format(ci, "{0:R} {1:R} {2:R} {3:R} {4:R} {5:R} {6:R} {7:R} {8:R}\n", a.X, a.Y, a.Z, b.X, b.Y, b.Z, c.X, c.Y, c.Z));
                n++;
                if (tx0 < x0) x0 = tx0; if (tx1 > x1) x1 = tx1; if (ty0 < y0) y0 = ty0; if (ty1 > y1) y1 = ty1;
            }
            catch { lost++; }
            finally { t.Dispose(); }
        }
        return (n, (x0, y0, x1, y1), lost);
    }
}
