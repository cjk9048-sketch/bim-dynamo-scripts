using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.Civil.DatabaseServices;
using DH.Grading.Core;
using AcadEntity = Autodesk.AutoCAD.DatabaseServices.Entity;

namespace DH.Grading.Civil;

/// <summary>
/// Civil3D TIN 빌더 — 오버사이즈 가상 사면 TIN 생성(계단 링 = 브레이크라인)과
/// 시각화(daylight 초록선·노리선/소단선). 순수 기하는 Core.GradingGeometry가 담당.
/// </summary>
public static class GradingBuilder
{
    /// <summary>직전 BuildVirtualSlope의 TIN 실측 검증 결과 — 의도한 링 Z와 실제 TIN 표고 대조(진단로그용).</summary>
    public static string LastVerify { get; private set; } = "";

    /// <summary>오버사이즈 가상 사면 TIN — 계단 링을 Standard 브레이크라인으로(동심 비교차 → 톱니 0).
    /// cornerLines(코너 능선)를 주면 열린 브레이크라인으로 추가 — 코너 모따기(사선) 방지(직각 모드).</summary>
    public static ObjectId BuildVirtualSlope(Database db, Transaction tr, IReadOnlyList<List<Point3>> rings, string name,
        IReadOnlyList<List<Point3>>? cornerLines = null, ObjectId protect = default)
    {
        // [재실행 정리] 같은 이름(및 _2, _3… 번호 변형)의 옛 DH 가상면을 먼저 삭제 — 실행마다 쌓여
        // 옛 표면을 보고 "안 생겼다"고 오인하는 혼란 방지(JACK). 항상 최신 하나만 남는다. 원지반(protect)은 제외.
        EraseSurfacesByBaseName(tr, name, protect);
        // [0729 — JACK] 보조선(코너 능선·플래토 직선·단차 레이)이 링과 평면 교차하면 Civil3D가 교차마다
        //   이벤트 뷰어 경고를 남김(단차 부지에서 수십 개) → 교차점을 양쪽에 공유 정점으로 삽입해 접점화.
        int sharedPts = 0;
        if (cornerLines != null && cornerLines.Count > 0)
            sharedPts = BreaklinePrep.SplitLineRingCrossings(rings, cornerLines);
        ObjectId id = TinSurface.Create(db, UniqueName(db, tr, name));
        var tin = (TinSurface)tr.GetObject(id, OpenMode.ForWrite);
        foreach (var ring in rings) AddRingBreakline(tin, ring);
        int intended = rings.Count;
        if (cornerLines != null)
        {
            foreach (var cl in cornerLines) AddOpenBreakline(tin, cl);
            intended += cornerLines.Count;
        }
        tin.Rebuild();

        // [TIN 실측 검증] 의도한 링 점 Z vs 실제 TIN 표고 — 불일치가 어느 방향에 몰렸는지 기록(비대칭 원인 추적).
        var vb = new System.Text.StringBuilder();
        try
        {
            int defCount = -1;
            try { defCount = tin.BreaklinesDefinition.Count; } catch { }
            vb.AppendLine($"  브레이크라인 의도 {intended} / 정의됨 {defCount}" +
                          (sharedPts > 0 ? $" · 보조선-링 공유정점 {sharedPts}개 삽입(교차 경고 제거, maxΔZ {BreaklinePrep.LastMaxZGap:F3}m)" : "") +
                          (BreaklinePrep.LastMaxZGap > 2.0 ? " · ΔZ>2m 교차는 스냅 생략(안전판 — 형상 무해, Civil3D 경고만 남음)" : ""));
            // 부지 중심(첫 링 평균)
            double cx = 0, cy = 0; int cn = 0;
            foreach (var pt in rings[0]) { cx += pt.X; cy += pt.Y; cn++; }
            cx /= Math.Max(cn, 1); cy /= Math.Max(cn, 1);
            for (int r = 0; r < rings.Count; r++)
            {
                var ring = rings[r];
                int sample = 0, bad = 0;
                int e = 0, w = 0, n2 = 0, s2 = 0;
                double maxErr = 0;
                for (int i = 0; i < ring.Count; i += 5) // 5점 간격 표본
                {
                    var pt = ring[i];
                    double zTin;
                    try { zTin = tin.FindElevationAtXY(pt.X, pt.Y); } catch { continue; }
                    sample++;
                    double err = Math.Abs(zTin - pt.Z);
                    if (err > 0.05)
                    {
                        bad++;
                        if (err > maxErr) maxErr = err;
                        double dx = pt.X - cx, dy = pt.Y - cy;
                        if (Math.Abs(dx) >= Math.Abs(dy)) { if (dx > 0) e++; else w++; }
                        else { if (dy > 0) n2++; else s2++; }
                    }
                }
                if (bad > 0)
                    vb.AppendLine($"  링{r}: 표본 {sample} 중 불일치 {bad} (동{e}/서{w}/북{n2}/남{s2}) 최대오차 {maxErr:F2}m");
            }
            // [격자 탐침] ①부지 내부 6×6 ②계단 전체(최외곽 링 bbox) 16×16 — TIN 실측 Z 숫자 지도.
            // '어느 쪽이 안 생겼나'를 스샷 없이 수치로 직접 포착(비대칭 원인 추적).
            void Grid(string title, IReadOnlyList<Point3> extent, int nDiv)
            {
                double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
                foreach (var pt in extent)
                { if (pt.X < minX) minX = pt.X; if (pt.X > maxX) maxX = pt.X; if (pt.Y < minY) minY = pt.Y; if (pt.Y > maxY) maxY = pt.Y; }
                vb.AppendLine($"  [{title} {nDiv}×{nDiv}] X {minX:F1}~{maxX:F1} / Y {minY:F1}~{maxY:F1} (위=북)");
                for (int gy = nDiv - 1; gy >= 0; gy--)
                {
                    var row = new System.Text.StringBuilder("    ");
                    for (int gx = 0; gx < nDiv; gx++)
                    {
                        double x = minX + (maxX - minX) * (gx + 0.5) / nDiv;
                        double y = minY + (maxY - minY) * (gy + 0.5) / nDiv;
                        string cell;
                        try { cell = tin.FindElevationAtXY(x, y).ToString("F1"); }
                        catch { cell = "----"; }
                        row.Append(cell.PadLeft(7));
                    }
                    vb.AppendLine(row.ToString());
                }
            }
            Grid("부지 내부", rings[0], 6);
            // ★[0806 JACK '로그가 너무 길다'] 계단 전체 16×16 숫자 지도는 **16줄**을 차지하는데,
            //   '어느 쪽이 안 생겼나'를 찾던 시절(v13~v15 비대칭 추적)에 만든 것이고 그 문제는 닫혔다.
            //   구멍(빈 셀)이 실제로 있을 때만 지도를 펼치고, 없으면 한 줄 요약으로 끝낸다.
            GridOrSummary("계단 전체", rings[rings.Count - 1], 16);

            void GridOrSummary(string title, IReadOnlyList<Point3> extent, int nDiv)
            {
                double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
                foreach (var pt in extent)
                { if (pt.X < minX) minX = pt.X; if (pt.X > maxX) maxX = pt.X; if (pt.Y < minY) minY = pt.Y; if (pt.Y > maxY) maxY = pt.Y; }
                int hole = 0, hit = 0; double lo = double.MaxValue, hi = double.MinValue;
                for (int gy = 0; gy < nDiv; gy++)
                    for (int gx = 0; gx < nDiv; gx++)
                    {
                        double x = minX + (maxX - minX) * (gx + 0.5) / nDiv;
                        double y = minY + (maxY - minY) * (gy + 0.5) / nDiv;
                        try { double z = tin.FindElevationAtXY(x, y); hit++; if (z < lo) lo = z; if (z > hi) hi = z; }
                        catch { hole++; }
                    }
                // 바깥 모서리는 원래 표면 밖이라 비는 게 정상 — 전체의 45%까지는 정상으로 본다(원뿔형 계단면).
                if (hole > nDiv * nDiv * 0.45) Grid(title, extent, nDiv);      // 이상하게 많이 비었다 → 지도를 펼친다
                else vb.AppendLine($"  [{title}] X {minX:F1}~{maxX:F1} / Y {minY:F1}~{maxY:F1} · 표본 {hit}/{nDiv * nDiv}개 Z {lo:F1}~{hi:F1}m(빈칸 {hole} — 정상 범위)");
            }
        }
        catch (System.Exception ex) { vb.AppendLine("  검증 실패: " + ex.Message); }
        LastVerify = vb.ToString();
        return id;
    }

    /// <summary>교선(폐합 루프)을 가상면의 Outer 경계로 주입(비파괴 = 경계선에서 삼각형 정밀 절단) 후 Rebuild.
    /// 경계는 표면 정의에 저장되므로 이후 다른 표면 작업/재그리기에 영향받지 않는다.</summary>
    public static void AddOuterBoundary(TinSurface tin, IReadOnlyList<Point3> ring)
    {
        int n = ring.Count;
        if (n >= 2)
        {
            var f = ring[0]; var l = ring[n - 1];
            if ((f.X - l.X) * (f.X - l.X) + (f.Y - l.Y) * (f.Y - l.Y) < 1e-12) n--; // 중복 닫음점 제거(길이 0 변 방지, 리뷰 M-1)
        }
        if (n < 3) return;
        var pc = new Point3dCollection();
        for (int i = 0; i < n; i++) pc.Add(new Point3d(ring[i].X, ring[i].Y, ring[i].Z));
        // nonDestructive=true: 경계에 걸친 삼각형을 경계선에서 '정밀 절단'(정점 삽입).
        // ※false로 A/B 실험 결과 절토까지 톱니(걸친 삼각형 통째 제거) — true가 올바른 의미로 확정(2026-07-03).
        //   성토가 경계 밖으로 튀어나오던 문제는 별개 원인 → VerifyBoundaryClip 실측으로 추적.
        tin.BoundariesDefinition.AddBoundaries(pc, 1.0, Autodesk.Civil.SurfaceBoundaryType.Outer, true);
        tin.Rebuild();
    }

    /// <summary>[검증] 경계 주입 후 표면이 경계선대로 잘렸는지 실측 — 링 표본의 안(25cm)·밖(25cm~8m) 표고 유무.
    /// 밖에 표면이 남아 있으면(outHit) 경계가 안 먹은 것, 안이 비면(inMiss) 과도 절단.</summary>
    public static string VerifyBoundaryClip(TinSurface tin, IReadOnlyList<Point3> ring)
    {
        bool TryElev(double x, double y) { try { tin.FindElevationAtXY(x, y); return true; } catch { return false; } }
        int n = ring.Count;
        if (n >= 2)
        {
            var f0 = ring[0]; var l0 = ring[n - 1];
            if ((f0.X - l0.X) * (f0.X - l0.X) + (f0.Y - l0.Y) * (f0.Y - l0.Y) < 1e-12) n--; // 닫음 중복 제외
        }
        if (n < 3) return "  [경계 정합 검증] 링 정점 부족\n";
        double area = 0;
        for (int i = 0; i < n; i++)
        { var a = ring[i]; var b = ring[(i + 1) % n]; area += a.X * b.Y - b.X * a.Y; }
        double s = area > 0 ? 1.0 : -1.0; // CCW면 내부는 진행방향 왼쪽
        int samples = 0, outHit = 0, inMiss = 0; double maxSpill = 0; string worst = "";
        int step = System.Math.Max(1, n / 200);
        for (int i = 0; i < n; i += step)
        {
            var a = ring[i]; var b = ring[(i + 1) % n];
            double ex = b.X - a.X, ey = b.Y - a.Y;
            double el = System.Math.Sqrt(ex * ex + ey * ey); if (el < 1e-9) continue;
            double mx = (a.X + b.X) * 0.5, my = (a.Y + b.Y) * 0.5;
            double nx = s * (-ey / el), ny = s * (ex / el); // 내부 방향 법선
            samples++;
            if (!TryElev(mx + nx * 0.25, my + ny * 0.25)) inMiss++;
            if (TryElev(mx - nx * 0.25, my - ny * 0.25))
            {
                outHit++;
                double spill = 0.25;
                foreach (var dOut in new[] { 0.5, 1.0, 2.0, 4.0, 8.0 })
                { if (TryElev(mx - nx * dOut, my - ny * dOut)) spill = dOut; else break; }
                if (spill > maxSpill) { maxSpill = spill; worst = $"({(mx - nx * spill):F1},{(my - ny * spill):F1})"; }
            }
        }
        return $"  [경계 정합 검증] 표본 {samples} · 경계밖 표면존재 {outHit}(최대 {maxSpill:F1}m 이탈{(worst == "" ? "" : " 예 " + worst)}) · 경계안 비어있음 {inMiss}\n";
    }

    /// <summary>기존 경계 정의를 모두 제거하고 새 Outer(+선택 Hide)로 교체 — paste 거부 시 정규화 링 재주입용.</summary>
    public static void ReplaceOuterBoundary(TinSurface tin, IReadOnlyList<Point3> ring, IReadOnlyList<Point3>? hideRing = null)
    {
        try { var bd = tin.BoundariesDefinition; while (bd.Count > 0) bd.RemoveAt(0); } catch { }
        AddOuterBoundary(tin, ring);
        if (hideRing != null) AddHideBoundary(tin, hideRing);
        try { tin.Rebuild(); } catch { }
    }

    /// <summary>내부 숨김(Hide) 경계 — 링 안쪽을 도넛처럼 뚫는다(절토면에서 pad 제거 → 성토와 겹침 제거).</summary>
    public static void AddHideBoundary(TinSurface tin, IReadOnlyList<Point3> ring)
    {
        int n = ring.Count;
        if (n >= 2)
        {
            var f = ring[0]; var l = ring[n - 1];
            if ((f.X - l.X) * (f.X - l.X) + (f.Y - l.Y) * (f.Y - l.Y) < 1e-12) n--; // 중복 닫음점 제거
        }
        if (n < 3) return;
        var pc = new Point3dCollection();
        for (int i = 0; i < n; i++) pc.Add(new Point3d(ring[i].X, ring[i].Y, ring[i].Z));
        tin.BoundariesDefinition.AddBoundaries(pc, 1.0, Autodesk.Civil.SurfaceBoundaryType.Hide, true);
        tin.Rebuild();
    }

    /// <summary>최종 합성 — 빈 TIN에 pasteOrder 순서로 PasteSurface.
    /// paste별 성공/실패와 Civil 예외 메시지를 log로 반환(병합 느낌표 원인 특정용, JACK 검증 지시).
    ///
    /// <para>★★[v32.9 · JACK 0812 스샷] <b><paramref name="freezeEach"/>는 이제 쓰지 않는다 — 스냅샷은 <u>맨 끝에 한 번</u>.</b>
    ///
    /// <para>JACK이 <c>정지면_DH</c> 특성 → 정의 탭을 열어 줬다. 작업 목록이 이랬다:</para>
    /// <code>
    /// ⚠ 붙여넣기   Surface1 지표면 추가
    /// ⚠ 스냅샷 작성 사용자 작성        ← 두 번째! 맨 끝이 아니다
    /// ⚠ 붙여넣기   가상성토_DH 지표면 추가
    /// ⚠ 붙여넣기   가상절토_DH 지표면 추가
    /// </code>
    ///
    /// <para><b>스냅샷이 중간에 눌러앉아 있었다.</b> 붙여넣기마다 <see cref="Freeze"/>를 부르니
    /// <b>첫 붙여넣기 직후에 스냅샷이 만들어지고</b>, 그 뒤로는 <c>RebuildSnapshot</c>이 <b>그 자리에서</b> 갱신만 한다.
    /// 스냅샷은 <b>자기 위치로 이동하지 않는다</b>.</para>
    ///
    /// <para><b>그래서 무엇이 어긋났나.</b> 공식 문서: <i>"지표면을 지을 때 이전 작업은 무시되고
    /// <b>스냅샷 작업에서부터</b> 시작한다."</i> 즉 <b>원지반만</b> 스냅샷에 구워졌고,
    /// <b>성토·절토 붙여넣기는 아직 살아 있어</b> 소스 표면에 계속 매달려 있었다.
    /// 소스를 건드릴 때마다(숨김·재작성) 그 항목들이 구식이 되고, 그게 JACK이 본 느낌표다.</para>
    ///
    /// <para><b>덤으로 드러난 것</b>: <c>CreateGradingCommand</c>가 기대던
    /// <i>"소스가 지워져도 형상 유지"</i>는 <b>원지반에만 참이었다</b>. 성토·절토는 소스가 사라지면 무너진다.
    /// 스냅샷을 맨 끝으로 옮기면 <b>세 개 다</b> 구워져 그 기대가 비로소 사실이 된다.</para>
    ///
    /// <para>→ 붙여넣는 동안은 <c>Rebuild</c>만 하고, <b>다 붙인 뒤 한 번만</b> 굳힌다.</para></summary>
    public static ObjectId Composite(Database db, Transaction tr, string name,
        IReadOnlyList<(ObjectId id, string label)> pasteOrder, out string log, bool freezeEach = false,
        ObjectId protect = default)
    {
        var sb = new System.Text.StringBuilder();
        EraseSurfacesByBaseName(tr, name, protect); // 재실행 스택 방지 — 원지반(protect)은 이름이 겹쳐도 보호(JACK 0715)
        ObjectId id = TinSurface.Create(db, UniqueName(db, tr, name));
        var final = (TinSurface)tr.GetObject(id, OpenMode.ForWrite);
        foreach (var (sid, label) in pasteOrder)
        {
            if (sid.IsNull) { sb.Append($"{label}:없음  "); continue; }
            // ★[v32.14 · 자문2 §9] <b>소스가 불안정하면 붙여넣기 작업의 상태에도 옮는다.</b>
            //   붙이기 전에 소스 상태를 재 둔다 — 나중에 '소스 탓인가'를 로그만으로 가릴 수 있게.
            try
            {
                if (tr.GetObject(sid, OpenMode.ForRead) is Autodesk.Civil.DatabaseServices.Surface ss)
                    sb.Append($"[{label}소스 구식={ss.IsOutOfDate}/스냅샷={ss.HasSnapshot}] ");
            }
            catch { }
            try
            {
                final.PasteSurface(sid);
                // ★[v32.9] 붙여넣는 동안은 <b>짓기만</b> 한다 — 여기서 굳히면 스냅샷이 중간에 박힌다(위 설명).
                if (freezeEach) Freeze(final);
                else { try { final.Rebuild(); } catch { } }
                sb.Append($"{label}:OK  ");
            }
            catch (System.Exception ex) { sb.Append($"{label}:실패[{ex.GetType().Name}] {ex.Message}  "); }
        }
        try { Freeze(final); } catch { }
        // ★[v32.14 · 자문2 §12] 굳히기가 실제로 무엇을 했는지 함께 남긴다 — 조용한 실패를 없앤다.
        sb.Append(" · 굳히기: " + LastFreezeDiag);
        log = sb.ToString().Trim();
        return id;
    }

    private static void Freeze(TinSurface s)
    {
        // ★[JACK 0807 'DH정지면에 스냅샷 재작성 느낌표가 뜬 상태로 작성됨'] 순서가 문제였다.
        //   종전엔 `CreateSnapshot()`을 부르고 **예외가 났을 때만** `RebuildSnapshot()`으로 갔다.
        //   그런데 스냅샷이 이미 있을 때 CreateSnapshot이 예외를 안 던지면(조용히 무시) 갱신이 영영 안 된다 —
        //   그러면 스냅샷은 **첫 붙여넣기 시점**에 머물고, 뒤에 쌓인 붙여넣기가 스냅샷보다 새것이 되어
        //   Prospector가 '스냅샷 재작성 필요(!)'를 띄운다. 예외에 기대지 말고 **둘 다 순서대로** 부른다.
        //
        // ★★[v32.2 · JACK 0812 '정지생성에서 자꾸 스냅샷 재작성 느낌표가 뜬다'] <b>순서가 아직도 뒤집혀 있었다.</b>
        //   스냅샷은 <b>지금 굳은 모양을 찍어 두는 것</b>이다. 그런데 종전엔 찍어 둔 <b>뒤에</b> `Rebuild()`를 불렀다.
        //   재작성은 정의를 처음부터 다시 훑으므로 표면이 스냅샷보다 <b>새것</b>이 되고,
        //   Prospector는 그 즉시 다시 (!)를 띄운다. 0807에 고친 것은 '갱신을 아예 안 하던' 문제였고,
        //   <b>이건 갱신한 것을 도로 무르는</b> 문제다 — 그래서 그때 고쳤는데도 느낌표가 남아 있었다.
        //   → <b>먼저 짓고, 마지막에 찍는다.</b>
        //   ★[v32.4 · 검토 반영] 공식 문서 두 줄이 순서를 확정해 준다:
        //   ① <i>"CreateSnapshot and RebuildSnapshot can also cause errors <b>if the surface is out-of-date</b>."</i>
        //      → 붙여넣기 직후는 <b>반드시</b> 구식이다. 먼저 <c>Rebuild()</c>로 풀지 않으면 매번 오류 조건이다.
        //   ② <i>"<b>Both</b> CreateSnapshot and RebuildSnapshot <b>will overwrite</b> an existing snapshot.
        //      RebuildSnapshot will cause an error <b>if the snapshot does not exist</b>."</i>
        //      → 둘을 잇달아 부를 이유가 없다(같은 일을 두 번). <b>있으면 갱신, 없으면 생성</b> 한쪽만 부른다.
        //   ★★[v32.5 · JACK 0812 계측] <b>세 단계다. 둘 중 하나만으로는 안 된다.</b>
        //   실측 로그가 갈랐다: <c>구식=True · 스냅샷구식=False</c> —
        //   <b>스냅샷은 멀쩡한데 표면이 구식</b>이었다. 그게 Prospector의 느낌표다.
        //   그리고 <c>Rebuild()</c>를 부른 <b>직후에도</b> 구식이었다 —
        //   <b>스냅샷을 찍는 행위 자체가 정의를 바꿔 표면을 다시 구식으로 만들기 때문</b>이다.
        //   두 제약이 동시에 참이다: 스냅샷 <b>전</b>에 지어야 오류가 안 나고(문서),
        //   스냅샷 <b>뒤</b>에도 지어야 구식이 안 남는다(계측). → <b>짓고 · 찍고 · 다시 짓는다.</b>
        //   ★★★[v32.14 · 자문2] <b>갱신하지 말고 <u>지우고 새로 만든다</u> — 아직 안 해 본 축이다.</b>
        //
        //   여태 우리는 스냅샷이 있으면 <c>RebuildSnapshot()</c>으로 <b>갱신</b>만 했다.
        //   그런데 이번 문제의 정체가 <b>기존 스냅샷과 정의 작업의 내부 상태가 서로 어긋난 것</b>이라면,
        //   있는 것을 다시 굽는 것보다 <b>없애고 처음부터 만드는 것</b>이 훨씬 강한 초기화다.
        //   <c>RemoveSnapshot</c> → <c>Rebuild</c>(정의 전체를 처음부터 다시 밟는다) → <c>CreateSnapshot</c>.
        //
        //   ※ 자문1은 '붙여넣기 줄 삭제'를 권했고 자문2는 '재현성이 사라지니 권하지 않는다'고 했다.
        //     <b>되돌릴 수 있는 쪽을 먼저</b> 시험한다 — 이쪽은 실패해도 스냅샷만 다시 만들면 그만이다.
        //
        //   ★★ 그리고 <b>예외를 삼키지 않는다</b>(자문2 §12). 종전 <c>catch { }</c>는
        //     "예외 없음"과 "조용히 실패"를 구분할 수 없게 만들었다 — 이 문제에서 가장 값비쌌던 눈가림이다.
        //
        //   ── 옛 기록 ──
        //   [v32.10] 트레일링 <c>Rebuild</c>를 뺐다 — 찍고 나서 또 지으면 스냅샷이 구식이 된다.
        //   v32.5에서 ③을 넣은 근거는 <c>구식=True</c>였는데, 그건 <b>스냅샷이 정의 중간에 있을 때의 증상</b>이었다.
        //   중간에 있으면 스냅샷을 찍어도 <b>그 뒤의 붙여넣기가 미처리로 남아</b> 표면이 구식이었던 것이다.
        //   v32.9에서 스냅샷을 <b>맨 끝</b>으로 옮긴 뒤로는 그 조건이 사라졌다 —
        //   이제 ③은 필요 없을 뿐 아니라 <b>해롭다</b>(JACK 실측: 붙여넣기 느낌표는 사라지고 스냅샷 느낌표만 남았다).
        //   <b>같은 증상을 두 원인이 만들었고, 원인을 고치자 처방이 병이 됐다.</b>
        // ★★★[v32.16 · JACK 0812] <b>0807판으로 되돌린다 — 마지막으로 '고쳐졌다'고 확인된 상태다.</b>
        //
        //   JACK: <i>"아주 초반에 우리 이 문제 스냅샷 느낌표 해결하지 않았어?"</i> — <b>맞다.</b>
        //   오늘 나는 이 세 줄을 <b>세 번</b> 바꿨고(v32.5·v32.10·v32.14) 세 번 다 증상은 그대로였다.
        //   <b>고쳐지지 않는데 계속 바꾸는 것은 고치는 게 아니라 흔드는 것</b>이다.
        //   알려진 정상 상태로 되돌리고, 다른 축에서 원인을 찾는다.
        //
        //   ※ 이 순서의 원래 근거(0807): <c>CreateSnapshot</c>이 이미 있을 때 조용히 무시될 수 있으므로
        //     <b>예외에 기대지 말고 둘 다 순서대로</b> 부른다. 그 판단은 지금도 유효하다.
        var fd = new System.Text.StringBuilder();
        try { s.CreateSnapshot(); fd.Append("스냅샷생성 "); }
        catch (System.Exception ex) { fd.Append($"스냅샷생성건너뜀[{ex.GetType().Name}] "); }
        try { s.RebuildSnapshot(); fd.Append("스냅샷갱신 "); }
        catch (System.Exception ex) { fd.Append($"⚠스냅샷갱신실패[{ex.GetType().Name}:{ex.Message}] "); }
        try { s.Rebuild(); fd.Append("재작성 "); }
        catch (System.Exception ex) { fd.Append($"⚠재작성실패[{ex.GetType().Name}:{ex.Message}] "); }
        LastFreezeDiag = fd.ToString().Trim();
    }

    /// <summary>★[v32.14] 직전 <see cref="Freeze"/>가 실제로 무엇을 했는지 — <b>예외를 삼키지 않고</b> 남긴다.
    /// "예외 없음"과 "조용히 실패"를 구분 못 한 것이 이 문제에서 가장 값비쌌던 눈가림이었다(자문2 §12).</summary>
    public static string LastFreezeDiag { get; private set; } = "";

    /// <summary>열린 브레이크라인(코너 능선 등) — 링과 달리 닫지 않는다.</summary>
    private static void AddOpenBreakline(TinSurface tin, IReadOnlyList<Point3> pts)
    {
        if (pts.Count < 2) return;
        var pc = new Point3dCollection();
        foreach (var pt in pts) pc.Add(new Point3d(pt.X, pt.Y, pt.Z));
        try { tin.BreaklinesDefinition.AddStandardBreaklines(pc, 1.0, 0.0, 0.0, 0.0); } catch { }
    }

    /// <summary>★★[v30.4 · JACK 0812] <b>끝점이 맞닿는 조각끼리 이어 붙인다 — 데이라잇을 폐합시킨다.</b>
    ///
    /// <para>JACK: <i>"데이라잇은 폐합이 안 됐어. 절토부 성토부가 각각 선으로만 있는 것 같아."</i> — 맞다.
    /// 데이라잇은 <b>절토 쪽 교선</b>과 <b>성토 쪽 교선</b>이 <b>따로</b> 만들어진다. 둘이 이어져야
    /// 부지를 감싸는 한 바퀴가 되는데, 각각 열린 선으로 남아 <b>고리로 안 보인다</b>.</para>
    ///
    /// <para>억지로 첫점과 끝점을 잇는 건 안 된다 — 그러면 부지를 가로지르는 <b>허공 지름길</b>이 생긴다
    /// (그래서 종전 코드는 열린 선을 열린 채로 뒀다). 대신 <b>다른 조각의 끝과 맞닿는지</b> 보고
    /// 맞닿을 때만 잇는다. 실제로 절토·성토 교선은 <b>절성 경계점에서 만난다</b>.</para>
    ///
    /// <para>그러고도 안 닫히면 그 자리는 <b>진짜로 끊긴 것</b>이라 열어 둔다 — 도면이 사실을 말해야 한다.</para></summary>
    /// <summary>★★[v31.8 · JACK 0812] <b>되돌아오는 '가시'를 걷어낸다 — 부지를 가로지르는 선의 정체.</b>
    ///
    /// <para>JACK: <i>"중간에 겹쳐져서 가로지르는 선은 없어야 해."</i> 스샷의 그 선은
    /// <b>경계선이 안쪽으로 쭉 들어갔다가 같은 길로 되돌아온 것</b>이다. 두 겹이 겹쳐 보이니
    /// 한 줄로 보이지만 실은 왕복이다. 폐합(잇기)을 넣기 <b>전 스샷에도 같은 자리에 있었으므로</b>
    /// 잇기 때문에 생긴 게 아니라 <b>교선 계산이 남긴 가시</b>다(핀치 링에서 나온다).</para>
    ///
    /// <para>지형 경계선에 <b>180°로 되돌아오는 점</b>은 뜻이 없다 — 면적도 0이고 경계도 아니다.
    /// 그래서 꺾임각이 거의 완전히 뒤집히는 정점을 지운다. 지우면 가시가 한 칸 짧아지고,
    /// 그 자리가 다시 뒤집힌 점이 되므로 <b>안정될 때까지 되풀이</b>하면 가시가 통째로 사라진다.</para>
    ///
    /// <para>진짜 모서리는 안 건드린다 — 170°보다 더 뒤집힌 것만 본다.
    /// 그보다 완만한 각은 실제 지형 경계에 얼마든지 있다.</para></summary>
    private static List<Point3> DropSpurs(IReadOnlyList<Point3> src, double dupTol, double cosLimit)
    {
        static double D2(Point3 a, Point3 b) { double dx = a.X - b.X, dy = a.Y - b.Y; return dx * dx + dy * dy; }
        var p = new List<Point3>(src);
        double d2 = dupTol * dupTol;
        for (int i = p.Count - 1; i > 0; i--) if (D2(p[i], p[i - 1]) < d2) p.RemoveAt(i);

        bool changed = true;
        int guard = 0;
        while (changed && p.Count >= 3 && guard++ < 1000)
        {
            changed = false;
            for (int i = p.Count - 2; i >= 1; i--)
            {
                double ax = p[i].X - p[i - 1].X, ay = p[i].Y - p[i - 1].Y;
                double bx = p[i + 1].X - p[i].X, by = p[i + 1].Y - p[i].Y;
                double la = System.Math.Sqrt(ax * ax + ay * ay), lb = System.Math.Sqrt(bx * bx + by * by);
                if (la < 1e-9 || lb < 1e-9) { p.RemoveAt(i); changed = true; continue; }
                if ((ax * bx + ay * by) / (la * lb) < cosLimit) { p.RemoveAt(i); changed = true; }
            }
            for (int i = p.Count - 1; i > 0; i--) if (D2(p[i], p[i - 1]) < d2) { p.RemoveAt(i); changed = true; }
        }
        return p;
    }

    private static List<IReadOnlyList<Point3>> StitchOpenEnds(
        IEnumerable<IReadOnlyList<Point3>> loops, double tol)
    {
        var open = new List<List<Point3>>();
        var done = new List<IReadOnlyList<Point3>>();
        foreach (var l in loops)
        {
            if (l == null || l.Count < 2) continue;
            var f = l[0]; var e = l[l.Count - 1];
            double gx = f.X - e.X, gy = f.Y - e.Y;
            if (gx * gx + gy * gy < 0.10 * 0.10) done.Add(l);      // 이미 닫힌 고리 — 그대로
            else open.Add(new List<Point3>(l));
        }

        static double D2(Point3 a, Point3 b) { double dx = a.X - b.X, dy = a.Y - b.Y; return dx * dx + dy * dy; }
        double t2 = tol * tol;
        var joinLog = new System.Text.StringBuilder();
        int nJoin = 0;

        // ★★[v32.5 · JACK 0812 실측] <b>가장 가까운 쌍부터 잇는다 — '먼저 만난 쌍'이 아니라.</b>
        //
        //   종전엔 이중 반복문을 돌다 <b>처음 문턱 안에 든 쌍</b>을 그냥 이었다.
        //   문턱이 0.30m처럼 아주 작을 때는 그 안에 후보가 사실상 하나뿐이라 문제가 안 됐다.
        //   그런데 문턱을 실측값에 맞춰 키우면 <b>엉뚱한 짝이 먼저 걸릴</b> 수 있다 —
        //   그게 §17에서 부지를 가로지르는 지름길을 만든 그 병이다.
        //   <b>매번 전체에서 가장 가까운 쌍을 골라</b> 이으면 순서에 안 흔들린다.
        //
        //   그리고 <b>이을 때마다 간격을 로그에 적는다.</b> 잘못 이어지면 그 숫자가 먼저 티가 난다.
        while (true)
        {
            double best = double.MaxValue; int bi = -1, bj = -1, mode = -1;
            for (int i = 0; i < open.Count; i++)
                for (int j = i + 1; j < open.Count; j++)
                {
                    var A = open[i]; var B = open[j];
                    Point3 af = A[0], ae = A[A.Count - 1], bf = B[0], be = B[B.Count - 1];
                    double d;
                    if ((d = D2(ae, bf)) < best) { best = d; bi = i; bj = j; mode = 0; }
                    if ((d = D2(ae, be)) < best) { best = d; bi = i; bj = j; mode = 1; }
                    if ((d = D2(af, be)) < best) { best = d; bi = i; bj = j; mode = 2; }
                    if ((d = D2(af, bf)) < best) { best = d; bi = i; bj = j; mode = 3; }
                }
            if (bi < 0 || best > t2) break;
            var P = open[bi]; var Q = open[bj];
            switch (mode)
            {
                case 0: P.AddRange(Q.GetRange(1, Q.Count - 1)); break;                        // P끝 → Q앞
                case 1: Q.Reverse(); P.AddRange(Q.GetRange(1, Q.Count - 1)); break;            // P끝 → Q뒤
                case 2: Q.AddRange(P.GetRange(1, P.Count - 1)); open[bi] = Q; break;           // Q끝 → P앞
                default: P.Reverse(); P.AddRange(Q.GetRange(1, Q.Count - 1)); break;           // P앞 → Q앞
            }
            open.RemoveAt(bj);
            nJoin++;
            joinLog.Append($"\n      이음{nJoin}: 간격 {System.Math.Sqrt(best):F2}m");
        }
        // ★★[v32.5 · JACK 0812] <b>못 이은 끝이 '얼마나' 떨어졌는지 남긴다 — 문턱을 짐작으로 정하지 않으려고.</b>
        //
        //   JACK: <i>"절토쪽 성토쪽 경계선인 것 같은데 두 선이 닿는 부분은 연결이 안 되고 많이 떨어져 있어."</i>
        //   문턱(0.30m)만 키우는 것은 <b>재시도 금지 목록</b>이다 — §17: 느슨한 조인(1.0/2.5m)이 코너에서
        //   소단 너머로 억지 연결해 <b>16회 반복 실패의 정체</b>가 됐다.
        //   그러니 <b>실제 간격을 재서 그 숫자로 판단한다</b>:
        //   몇 cm면 문턱 문제이고, 수십 m면 <b>애초에 선이 안 만들어진 것</b>이라
        //   문턱을 키워 봐야 <b>없던 선을 지어내는</b> 셈이 된다.
        if (open.Count > 0)
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < open.Count; i++)
            {
                double best = double.MaxValue; int bj = -1;
                var mine = new[] { open[i][0], open[i][open[i].Count - 1] };
                for (int j = 0; j < open.Count; j++)
                {
                    if (j == i) continue;
                    var his = new[] { open[j][0], open[j][open[j].Count - 1] };
                    foreach (var q in his)
                        foreach (var p in mine)
                        { double d = D2(p, q); if (d < best) { best = d; bj = j; } }
                }
                double len = 0;
                for (int k = 1; k < open[i].Count; k++) len += System.Math.Sqrt(D2(open[i][k - 1], open[i][k]));
                sb.Append($"\n      열린조각[{i}] {open[i].Count}점 {len:F0}m → 가장 가까운 남의 끝 "
                        + (bj < 0 ? "없음(혼자)" : $"{System.Math.Sqrt(best):F2}m (조각[{bj}])"));
            }
            LastStitchDiag = $"이음 {nJoin}건(문턱 {tol:F2}m){joinLog} · 못 이은 조각 {open.Count}개" + sb;
        }
        else LastStitchDiag = $"이음 {nJoin}건(문턱 {tol:F2}m){joinLog} · 열린 조각 없음 — 전부 폐합";

        foreach (var o in open) done.Add(o);
        return done;
    }

    /// <summary>★[v32.5] 직전 <see cref="StitchOpenEnds"/>가 <b>이은 간격과 못 이은 끝의 실제 간격</b> —
    /// 문턱을 근거로 정하기 위한 계측. 이 숫자 없이 문턱을 만지면 §17을 반복한다.</summary>
    public static string LastStitchDiag { get; private set; } = "";

    /// <summary>★★[v32.5 · JACK 0812 실측] <b>열린 끝을 잇는 문턱(m) — 짐작이 아니라 <u>잰 값</u>이다.</b>
    ///
    /// <para>JACK이 끊긴 자리를 확대해 두 끝점 좌표를 찍어 줬다:
    /// <c>(240303.243, 450388.196, <b>112.554</b>)</c> · <c>(240306.073, 450388.389, <b>111.574</b>)</c>
    /// → 평면 거리 <b>2.84m</b>. 종전 문턱 <c>0.30m</c>의 <b>아홉 배</b>다.</para>
    ///
    /// <para><b>그 자리가 무엇인지도 좌표가 말해 준다.</b> 계획 폴리곤 남쪽 변은 <c>Y=450389.03</c>,
    /// 계획고는 <c>Z=112.000</c>이다. 두 끝점은 경계 바깥 0.6~0.8m에 있고,
    /// 표고가 <b>+0.554m(절토쪽)</b>과 <b>−0.426m(성토쪽)</b>으로 <b>계획고를 사이에 두고 갈린다</b>.
    /// <b>절성 경계</b>다 — 절토 교선과 성토 교선은 <b>따로</b> 계산되므로,
    /// 그 사이 2.84m는 <b>양쪽 다 자기 몫이 아니라 아무도 안 그린다</b>.
    /// 각 표면 안의 틈은 이미 메우고 있지만(로그 <c>틈메움 12·4</c>), <b>두 표면 사이</b>는 아무도 안 메웠다.</para>
    ///
    /// <para>★★[v32.6 · JACK 0812 기각] <b>3.0m로 키웠다가 되돌렸다 — 잇는 것 자체가 틀렸다.</b>
    /// 문턱을 2.84m 위로 올리니 실제로 이어지긴 했다(로그 `이음2: 간격 2.84m`).
    /// 그런데 JACK: <i>"노란색 경로처럼 <b>원지반과 맞닿는 선</b>으로 이어져야 하는데 그냥 두 선을 붙여버림."</i>
    /// <b>맞는 지적이다.</b> 이 함수가 만드는 것은 두 끝점을 잇는 <b>직선 현</b>이다 —
    /// 그 2.84m 구간의 진짜 데이라잇은 지형을 따라 계획 경계 쪽으로 <b>부풀었다 돌아오는 곡선</b>이다
    /// (JACK이 그려 준 노란 경로가 그 모양이다).
    /// <b>없는 형상을 지어내느니 끊어 두는 편이 낫다</b> — 도면이 사실을 말해야 한다(§30의 원칙).
    /// 그 구간은 <b>지어내는 것이 아니라 계산해야</b> 한다. 여기서는 <b>정말로 맞닿은 끝</b>만 잇는다.</para></summary>
    public const double StitchTolM = 0.30;

    /// <summary>★★[v32.6 · JACK 0812] <b>지표면의 외곽선을 뽑는다 — 삼각형 <u>하나</u>에만 속한 변이 곧 경계다.</b>
    ///
    /// <para>JACK: <i>"정지순수_DH 있는 건 외곽선도 있는 거 아니야? 왜 굳이 다시 그려내는 거지?"</i></para>
    ///
    /// <para><b>맞는 지적이고, 이게 정답이다.</b> 종전 데이라잇은 <b>절토면∩원지반</b>과 <b>성토면∩원지반</b>을
    /// <b>따로</b> 계산해 조각을 이어 붙이는 방식이었다. 그래서 절성 경계마다 <b>아무도 안 그리는 틈</b>이 남고,
    /// 그 틈을 직선으로 메우면 <b>없는 형상을 지어내게</b> 된다(JACK 0812 기각).</para>
    ///
    /// <para>반면 <c>정지순수_DH</c>는 절토·성토를 <b>이미 하나로 붙여 놓은</b> 지표면이다.
    /// TIN의 외곽 경계는 <b>정의상 반드시 닫혀 있다</b> — 삼각형 한 개에만 속한 변을 모으면 그것이 외곽선이고,
    /// 끊길 수가 없다. 그리고 그 선이 곧 <b>정지면이 실제로 끝나는 자리 = 원지반과 맞닿는 자리</b>다.</para>
    ///
    /// <para><b>누적(이어서)에도 그대로 맞는다.</b> 순수면은 앞 구역을 물려받아 쌓이므로(§27),
    /// 그 외곽선은 <b>전 구역이 합쳐진 데이라잇</b>이다 — 번들에서 앞 구역 링을 꺼내 마스크로 자르던
    /// 과정 자체가 필요 없어진다(옛 번들에서 마스크가 <c>null</c>이 되던 문제도 같이 사라진다).</para>
    ///
    /// <para>좌표는 <b>mm로 반올림해</b> 같은 점을 묶는다 — 붙여넣기 경계에서 미세하게 어긋난 정점이
    /// 서로 다른 점으로 잡히면 없는 구멍이 생긴다.</para></summary>
    public static List<List<Point3>> SurfaceOutline(TinSurface tin, out string diag)
    {
        diag = ""; var loops = new List<List<Point3>>();
        try
        {
            static (long, long) K(Point3d p)
                => ((long)System.Math.Round(p.X * 1000.0), (long)System.Math.Round(p.Y * 1000.0));
            static ((long, long), (long, long)) Norm((long, long) a, (long, long) b)
                => Comparer<(long, long)>.Default.Compare(a, b) <= 0 ? (a, b) : (b, a);

            var pos = new Dictionary<(long, long), Point3>();
            var edge = new Dictionary<((long, long), (long, long)), int>();
            int nTri = 0;

            foreach (TinSurfaceTriangle t in tin.GetTriangles(false))
            {
                nTri++;
                Point3d a = t.Vertex1.Location, b = t.Vertex2.Location, c = t.Vertex3.Location;
                foreach (var (p, q) in new[] { (a, b), (b, c), (c, a) })
                {
                    var kp = K(p); var kq = K(q);
                    if (kp == kq) continue;
                    pos[kp] = new Point3(p.X, p.Y, p.Z);
                    pos[kq] = new Point3(q.X, q.Y, q.Z);
                    var key = Norm(kp, kq);
                    edge[key] = edge.TryGetValue(key, out int n) ? n + 1 : 1;
                }
            }

            // 삼각형 하나에만 속한 변 = 외곽. 그것들로 이웃 관계를 만든다.
            var adj = new Dictionary<(long, long), List<(long, long)>>();
            int nB = 0;
            foreach (var kv in edge)
            {
                if (kv.Value != 1) continue;
                nB++;
                var (u, v) = kv.Key;
                if (!adj.TryGetValue(u, out var lu)) adj[u] = lu = new List<(long, long)>();
                if (!adj.TryGetValue(v, out var lv)) adj[v] = lv = new List<(long, long)>();
                lu.Add(v); lv.Add(u);
            }

            var used = new HashSet<((long, long), (long, long))>();
            foreach (var start in new List<(long, long)>(adj.Keys))
                foreach (var first in new List<(long, long)>(adj[start]))
                {
                    if (used.Contains(Norm(start, first))) continue;
                    var loop = new List<Point3> { pos[start] };
                    var cur = start; var nxt = first;
                    int guard = 0;
                    while (guard++ < 1_000_000)
                    {
                        used.Add(Norm(cur, nxt));
                        loop.Add(pos[nxt]);
                        if (nxt.Equals(start)) break;             // 한 바퀴 — 닫혔다
                        (long, long)? step = null;
                        foreach (var w in adj[nxt]) if (!used.Contains(Norm(nxt, w))) { step = w; break; }
                        if (step == null) break;                  // 더 못 간다(열린 끝)
                        cur = nxt; nxt = step.Value;
                    }
                    if (loop.Count >= 4) loops.Add(loop);
                }

            int closed = 0;
            foreach (var l in loops)
            {
                var f = l[0]; var e = l[l.Count - 1];
                double dx = f.X - e.X, dy = f.Y - e.Y;
                if (dx * dx + dy * dy < 1e-6) closed++;
            }
            diag = $"삼각형 {nTri} · 외곽변 {nB} · 고리 {loops.Count}개(닫힘 {closed})";
        }
        catch (System.Exception ex) { diag = "외곽선 추출 실패 — " + ex.Message; }
        return loops;
    }

    /// <summary>직전 <see cref="DrawDaylight"/>의 정리 결과 — 진단 로그용.</summary>
    public static string LastDaylightDiag { get; private set; } = "";

    /// <summary>daylight/교선 외곽선을 초록 폴리라인으로(시각 확인용). 레이어 'DH-정지경계'.</summary>
    public static void DrawDaylight(Database db, Transaction tr, IEnumerable<IReadOnlyList<Point3>> loops,
        string layerName = "DH-정지경계", short colorIndex = 3, bool layerOff = false)
    {
        ObjectId layerId = EnsureLayer(db, tr, layerName, colorIndex);
        // [JACK] 숨김 옵션 — 데이터(선)는 남기되 레이어 on/off로 화면 제어. 기존 도면에서 꺼져 있던 레이어도
        // layerOff=false면 다시 켠다(0728: 정지경계 초록 표시 요청 — 이전 실행이 꺼둔 상태 복구).
        try { var ltr = (LayerTableRecord)tr.GetObject(layerId, OpenMode.ForWrite); ltr.IsOff = layerOff; } catch { }
        EraseOnLayer(db, tr, layerName);
        var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
        // ★★[v31.8] 가시(왕복선)를 먼저 걷어내고 → 끝점이 맞닿는 조각을 잇는다.
        //   순서가 중요하다: 가시가 남아 있으면 그 끝이 남의 끝과 가깝다고 잘못 이어질 수 있다.
        var cleaned = new List<IReadOnlyList<Point3>>();
        int spurPts = 0;
        foreach (var l in loops)
        {
            if (l == null || l.Count < 2) continue;
            var c = DropSpurs(l, 0.01, -0.985);      // 1cm 이내는 같은 점 · 170°보다 뒤집히면 가시
            spurPts += l.Count - c.Count;
            if (c.Count >= 2) cleaned.Add(c);
        }
        loops = StitchOpenEnds(cleaned, StitchTolM);
        LastDaylightDiag = $"가시 제거 {spurPts}점 · {LastStitchDiag}";
        foreach (var loop in loops)
        {
            if (loop == null || loop.Count < 2) continue;
            // 폐합 판정: 첫~끝 간격 ≤10cm면 '닫힘=예' 속성으로 닫는다(중복 정점 X → 속성창에 닫힘=예).
            // 열린 교선을 강제로 닫으면 시작~끝 대각선(허공 지름길)이 그려지므로 열린 선은 열린 채로 둔다.
            var f = loop[0]; var l = loop[loop.Count - 1];
            double gx = f.X - l.X, gy = f.Y - l.Y;
            double gapSq = gx * gx + gy * gy;
            int count = loop.Count;
            if (gapSq < 1e-12) count--; // 끝점=첫점 중복이면 정점 하나 생략(닫힘 속성이 연결 담당)
            if (count < 2) continue;    // 정점 1개짜리 방어(리뷰 L-6)
            bool closed = (gapSq < 0.10 * 0.10) && count >= 3;

            var pl = new Polyline3d { LayerId = layerId };
            ms.AppendEntity(pl); tr.AddNewlyCreatedDBObject(pl, true);
            for (int i = 0; i < count; i++)
            {
                var p = loop[i];
                var v = new PolylineVertex3d(new Point3d(p.X, p.Y, p.Z));
                pl.AppendVertex(v); tr.AddNewlyCreatedDBObject(v, true);
            }
            if (closed) pl.Closed = true;
        }
    }

    /// <summary>[진단] 표시용 선분 그리기 — 기본: 지름길 컷(빨강 'DH-진단'). 틈메움 연결선은 'DH-틈메움'(하늘색 4)로.
    /// 끊긴 자리에 빨간 선이 있으면 '필터가 자른 것', 없으면 '그 구간 교선이 아예 생성 안 된 것'.</summary>
    public static void DrawDebugSpans(Database db, Transaction tr, IEnumerable<(Point3 A, Point3 B)> spans,
        string layer = "DH-진단", short aci = 1)
    {
        ObjectId layerId = EnsureLayer(db, tr, layer, aci);
        EraseOnLayer(db, tr, layer);
        var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
        foreach (var (a, b) in spans)
        {
            var ln = new Line(new Point3d(a.X, a.Y, a.Z), new Point3d(b.X, b.Y, b.Z)) { LayerId = layerId };
            ms.AppendEntity(ln); tr.AddNewlyCreatedDBObject(ln, true);
        }
    }

    /// <summary>노리선(노란 'DH-노리선')+소단선(흰 'DH-소단')을 그린다 — DHGRADE 4단계·DHSLOPELINE 공용.</summary>
    public static void DrawSlopeHatch(Database db, Transaction tr,
        IEnumerable<(Point3 A, Point3 B)> ticks, IEnumerable<IReadOnlyList<Point3>> benchLines)
    {
        ObjectId tickLayer = EnsureLayer(db, tr, "DH-노리선", 2);  // 노란
        ObjectId benchLayer = EnsureLayer(db, tr, "DH-소단", 7);   // 흰
        EraseOnLayer(db, tr, "DH-노리선");
        EraseOnLayer(db, tr, "DH-소단");
        var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);

        foreach (var (a, b) in ticks)
        {
            var ln = new Line(new Point3d(a.X, a.Y, a.Z), new Point3d(b.X, b.Y, b.Z)) { LayerId = tickLayer };
            ms.AppendEntity(ln); tr.AddNewlyCreatedDBObject(ln, true);
        }
        foreach (var loop in benchLines)
        {
            if (loop == null || loop.Count < 2) continue;
            var pl = new Polyline3d { LayerId = benchLayer };
            ms.AppendEntity(pl); tr.AddNewlyCreatedDBObject(pl, true);
            foreach (var p in loop)
            {
                var v = new PolylineVertex3d(new Point3d(p.X, p.Y, p.Z));
                pl.AppendVertex(v); tr.AddNewlyCreatedDBObject(v, true);
            }
        }
    }

    /// <summary>사면선·소단선 3D폴리선(ralplan Phase A) — 절/성토별 레이어 4개, 재실행 시 자기 레이어 청소.
    /// 사면선: 절토=색150(밝은 하늘색)/성토=색210(밝은 자주) · 소단선: 절토=색1(빨강)/성토=색30(주황).
    /// (JACK 0727: 구 250·8 진회색이 검은 배경에서 안 보여 밝은 색으로 교체.)</summary>
    public static void DrawSlopeEdges(Database db, Transaction tr,
        IEnumerable<IReadOnlyList<Point3>> cutSlopeLines, IEnumerable<IReadOnlyList<Point3>> cutBermLines,
        IEnumerable<IReadOnlyList<Point3>> fillSlopeLines, IEnumerable<IReadOnlyList<Point3>> fillBermLines)
    {
        var sets = new (string Layer, short Aci, IEnumerable<IReadOnlyList<Point3>> Lines)[]
        {
            ("DH-사면선-절토", 150, cutSlopeLines),
            ("DH-소단선-절토", 1,   cutBermLines),
            ("DH-사면선-성토", 210, fillSlopeLines),
            ("DH-소단선-성토", 30,  fillBermLines),
        };
        foreach (var (layer, aci, lines) in sets) Draw3dPolys(db, tr, layer, aci, lines);
    }

    /// <summary>부지 내부 단차 전환사면(Phase F) 모서리 — 상단=DH-사면선-전환(색6)/하단=DH-소단선-전환(색4).</summary>
    public static void DrawTransitionEdges(Database db, Transaction tr,
        IEnumerable<IReadOnlyList<Point3>> crestLines, IEnumerable<IReadOnlyList<Point3>> toeLines)
    {
        Draw3dPolys(db, tr, "DH-사면선-전환", 6, crestLines);
        Draw3dPolys(db, tr, "DH-소단선-전환", 4, toeLines);
    }

    /// <summary>레이어 보장+청소 후 3D 폴리선 일괄 작도(사면선/소단선/전환선 공용).</summary>
    private static void Draw3dPolys(Database db, Transaction tr, string layer, short aci,
        IEnumerable<IReadOnlyList<Point3>> lines)
    {
        ObjectId layerId = EnsureLayer(db, tr, layer, aci);
        EraseOnLayer(db, tr, layer);
        var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
        foreach (var loop in lines)
        {
            if (loop == null || loop.Count < 2) continue;
            var pl = new Polyline3d { LayerId = layerId };
            ms.AppendEntity(pl); tr.AddNewlyCreatedDBObject(pl, true);
            foreach (var p in loop)
            {
                var v = new PolylineVertex3d(new Point3d(p.X, p.Y, p.Z));
                pl.AppendVertex(v); tr.AddNewlyCreatedDBObject(v, true);
            }
        }
    }

    /// <summary>[§75 1-A] XData 앱(RegApp) 등록 보장 — 없으면 추가.</summary>
    private static void EnsureRegApp(Database db, Transaction tr, string appName)
    {
        var rat = (RegAppTable)tr.GetObject(db.RegAppTableId, OpenMode.ForRead);
        if (rat.Has(appName)) return;
        rat.UpgradeOpen();
        var r = new RegAppTableRecord { Name = appName };
        rat.Add(r); tr.AddNewlyCreatedDBObject(r, true);
    }

    /// <summary>[§75] 사면선·소단선 4개 레이어 이름(절/성토 × 사면/소단) — DHWALL 색 전환·복원에 공용.</summary>
    public static readonly string[] EdgeLayerNames =
        { "DH-사면선-절토", "DH-소단선-절토", "DH-사면선-성토", "DH-소단선-성토" };
    /// <summary>사면선/소단선 기본색 — 등고선처럼 희미한 회색(ACI 8, JACK 0728). DHWALL 밖에서는 이 색.</summary>
    public const short EdgeGrayAci = 8;
    /// <summary>DHWALL 실행 중 '고를 수 있음' 강조색(ACI 4 = 시안).</summary>
    public const short EdgePickAci = 4;

    /// <summary>[§75] 지정 레이어들의 색을 aci로 설정(있는 것만). DHWALL 진입=강조/종료=회색 복원에 사용.</summary>
    public static void SetLayersColor(Database db, Transaction tr, IEnumerable<string> names, short aci)
    {
        var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
        foreach (var n in names)
        {
            if (!lt.Has(n)) continue;
            var ltr = (LayerTableRecord)tr.GetObject(lt[n], OpenMode.ForWrite);
            ltr.Color = Color.FromColorIndex(ColorMethod.ByAci, aci);
        }
    }

    /// <summary>[§75 1-A] 사면선·소단선을 식별정보(XData: 방향 up·사면/소단·단 index·구간 index)와 함께 작도.
    /// 옹벽 전환(DHWALL)이 클릭할 대상. 4개 레이어(사면선/소단선 × 절/성토, 색은 DrawSlopeEdges와 동일) 청소 후
    /// 태그된 3D 폴리선으로 그린다. up=true 절토/false 성토. XData=[appName, up, isSlope, bench, seg].</summary>
    public static void DrawSlopeEdgesTagged(Database db, Transaction tr,
        IEnumerable<(bool IsSlope, int Bench, int Seg, System.Collections.Generic.List<Point3> Pts)> cutEdges,
        IEnumerable<(bool IsSlope, int Bench, int Seg, System.Collections.Generic.List<Point3> Pts)> fillEdges,
        string planHandle = "", bool clearFirst = true)
    {
        EnsureRegApp(db, tr, GradingSettings.WallPickAppName);
        // [§75 1-A UX] 사면선/소단선은 기본 '회색'(JACK). 옹벽생성(DHWALL) 실행 중에만 색이 바뀌고 종료 시 복원.
        // [다중 구역 0729] clearFirst=false면 기존 선을 지우지 않고 덧그림(구역 루프의 2번째 이후 호출용).
        //   planHandle은 XData 끝에 문자열로 붙어 어느 구역(계획선)의 선인지 식별 — DHWALL이 마지막 구역만 허용.
        var layerId = new Dictionary<string, ObjectId>();
        foreach (var name in EdgeLayerNames)
        {
            layerId[name] = EnsureLayer(db, tr, name, EdgeGrayAci);
            if (clearFirst) EraseOnLayer(db, tr, name);
        }
        SetLayersColor(db, tr, EdgeLayerNames, EdgeGrayAci); // 기존 레이어면 EnsureLayer가 색을 안 바꾸므로 강제 회색

        var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);

        void DrawSet(IEnumerable<(bool IsSlope, int Bench, int Seg, System.Collections.Generic.List<Point3> Pts)> edges, bool up)
        {
            // [0728 버그수정 — JACK] Seg를 '방향 전체 유일 번호'로 재부여. 원래 seg는 영역(finalRing)마다
            //   0부터 다시 시작해, 성토 2곳이면 서로 다른 선이 같은 (단·구간) 신분을 가짐 →
            //   DHWALL 두 번째 클릭이 첫 선택을 토글 해제(둘 중 하나만 적용되던 원인).
            int uid = 0;
            foreach (var (isSlope, bench, _, pts) in edges)
            {
                if (pts == null || pts.Count < 2) continue;
                string layer = (isSlope ? "DH-사면선-" : "DH-소단선-") + (up ? "절토" : "성토");
                var pl = new Polyline3d { LayerId = layerId[layer] };
                ms.AppendEntity(pl); tr.AddNewlyCreatedDBObject(pl, true);
                foreach (var q in pts)
                {
                    var v = new PolylineVertex3d(new Point3d(q.X, q.Y, q.Z));
                    pl.AppendVertex(v); tr.AddNewlyCreatedDBObject(v, true);
                }
                pl.XData = new ResultBuffer(
                    new TypedValue((int)DxfCode.ExtendedDataRegAppName, GradingSettings.WallPickAppName),
                    new TypedValue((int)DxfCode.ExtendedDataInteger16, (short)(up ? 1 : 0)),
                    new TypedValue((int)DxfCode.ExtendedDataInteger16, (short)(isSlope ? 1 : 0)),
                    new TypedValue((int)DxfCode.ExtendedDataInteger16, (short)bench),
                    new TypedValue((int)DxfCode.ExtendedDataInteger16, (short)(uid++)),
                    new TypedValue((int)DxfCode.ExtendedDataAsciiString, planHandle ?? ""));
            }
        }
        DrawSet(cutEdges, true);
        DrawSet(fillEdges, false);
    }

    /// <summary>[FGL 표기 — JACK 0729 샘플 스샷] 계획 부지 중앙에 수준점형 심볼(4분할 원, 북서·남동 빨강 채움)
    /// + 위에 "FGL(+)191.00" 텍스트(색 7 — 배경 따라 흰/검). 레이어 'DH-FGL', 재실행 시 자기 레이어 청소.
    /// 노리선(DHNORI)에서 구역마다 1개씩 호출.</summary>
    public static void DrawFglMarkers(Database db, Transaction tr,
        IEnumerable<(double X, double Y, double Z)> marks,
        double radius = 2.0, double textH = 3.0)
    {
        ObjectId layerId = EnsureLayer(db, tr, "DH-FGL", 7);
        EraseOnLayer(db, tr, "DH-FGL");
        var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
        var red = Color.FromColorIndex(ColorMethod.ByAci, 1);
        const double bulge90 = 0.41421356237;   // tan(22.5°) — 90° 호

        foreach (var m in marks)
        {
            var c = new Point3d(m.X, m.Y, m.Z);
            var circ = new Circle(c, Vector3d.ZAxis, radius) { LayerId = layerId, Color = red };
            ms.AppendEntity(circ); tr.AddNewlyCreatedDBObject(circ, true);
            var lh = new Line(new Point3d(m.X - radius, m.Y, m.Z), new Point3d(m.X + radius, m.Y, m.Z)) { LayerId = layerId, Color = red };
            ms.AppendEntity(lh); tr.AddNewlyCreatedDBObject(lh, true);
            var lv = new Line(new Point3d(m.X, m.Y - radius, m.Z), new Point3d(m.X, m.Y + radius, m.Z)) { LayerId = layerId, Color = red };
            ms.AppendEntity(lv); tr.AddNewlyCreatedDBObject(lv, true);

            // 채움 사분면 2개(체크무늬) — 북서(북→서 호)·남동(남→동 호), 둘 다 반시계(양수 bulge)로 볼록한
            //   진짜 부채꼴이 되게 한다(0730 스샷: 서→북 순서는 호가 반대로 휘어 오목 조각이 됐음).
            foreach (var (ax, ay, bx, by) in new[]
            {
                (m.X, m.Y + radius, m.X - radius, m.Y),   // NW: 북 → 서 (반시계, 11시 사분면)
                (m.X, m.Y - radius, m.X + radius, m.Y),   // SE: 남 → 동 (반시계, 5시 사분면)
            })
            {
                var pie = new Polyline(3) { Elevation = m.Z, LayerId = layerId, Closed = true };
                pie.AddVertexAt(0, new Point2d(m.X, m.Y), 0, 0, 0);
                pie.AddVertexAt(1, new Point2d(ax, ay), bulge90, 0, 0);
                pie.AddVertexAt(2, new Point2d(bx, by), 0, 0, 0);
                ms.AppendEntity(pie); tr.AddNewlyCreatedDBObject(pie, true);
                try
                {
                    // [0730 스샷] 해치 평면 고도(Elevation)를 심볼 고도와 일치 — 안 주면 Z=0에 그려져 테두리와 분리.
                    var h = new Hatch { LayerId = layerId, Color = red, Associative = false, Elevation = m.Z };
                    ms.AppendEntity(h); tr.AddNewlyCreatedDBObject(h, true);
                    h.SetHatchPattern(HatchPatternType.PreDefined, "SOLID");
                    h.AppendLoop(HatchLoopTypes.Default, new ObjectIdCollection { pie.ObjectId });
                    h.EvaluateHatch(true);
                    pie.Erase();   // 윤곽 폴리선은 해치 생성 후 제거(원·십자선이 윤곽 담당)
                }
                catch { }          // 해치 실패 시 부채꼴 윤곽선이라도 남김
            }

            var txt = new DBText
            {
                TextString = $"FGL(+){m.Z:F2}",
                Height = textH,
                LayerId = layerId,
                Color = Color.FromColorIndex(ColorMethod.ByAci, 7),
                HorizontalMode = TextHorizontalMode.TextCenter,
                VerticalMode = TextVerticalMode.TextBottom,
                AlignmentPoint = new Point3d(m.X, m.Y + radius * 1.4, m.Z),
            };
            ms.AppendEntity(txt); tr.AddNewlyCreatedDBObject(txt, true);
            try { txt.AdjustAlignment(db); } catch { }
        }
    }

    /// <summary>[사면생성 DHSLOPE — JACK 0729] 클릭 대상용 '태그된' 옹벽선 작도 — 레이어 DH-옹벽선에
    /// XData [app, up, isSlope=1, bench, seg(방향 전체 유일), planHandle] 부착. 기존 선은 지우지 않고 덧그림
    /// (명령 종료 시 호출부가 반환된 ObjectId들만 지워 원상 복구). 반환=생성한 엔티티들.</summary>
    public static List<ObjectId> DrawWallLinesTagged(Database db, Transaction tr,
        IEnumerable<(bool IsSlope, int Bench, int Seg, System.Collections.Generic.List<Point3> Pts)> cutEdges,
        IEnumerable<(bool IsSlope, int Bench, int Seg, System.Collections.Generic.List<Point3> Pts)> fillEdges,
        string planHandle)
    {
        EnsureRegApp(db, tr, GradingSettings.WallPickAppName);
        ObjectId layerId = EnsureLayer(db, tr, "DH-옹벽선", 1);
        var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
        var made = new List<ObjectId>();

        void DrawSet(IEnumerable<(bool IsSlope, int Bench, int Seg, System.Collections.Generic.List<Point3> Pts)> edges, bool up)
        {
            int uid = 0;   // [DHWALL과 동일 원칙] 방향 전체 유일 번호 — 영역/단마다 0부터 재시작하는 seg 충돌 방지
            foreach (var (_, bench, _, pts) in edges)
            {
                if (pts == null || pts.Count < 2) continue;
                var pl = new Polyline3d { LayerId = layerId };
                ms.AppendEntity(pl); tr.AddNewlyCreatedDBObject(pl, true);
                foreach (var q in pts)
                {
                    var v = new PolylineVertex3d(new Point3d(q.X, q.Y, q.Z));
                    pl.AppendVertex(v); tr.AddNewlyCreatedDBObject(v, true);
                }
                pl.XData = new ResultBuffer(
                    new TypedValue((int)DxfCode.ExtendedDataRegAppName, GradingSettings.WallPickAppName),
                    new TypedValue((int)DxfCode.ExtendedDataInteger16, (short)(up ? 1 : 0)),
                    new TypedValue((int)DxfCode.ExtendedDataInteger16, (short)1),
                    new TypedValue((int)DxfCode.ExtendedDataInteger16, (short)bench),
                    new TypedValue((int)DxfCode.ExtendedDataInteger16, (short)(uid++)),
                    new TypedValue((int)DxfCode.ExtendedDataAsciiString, planHandle ?? ""));
                made.Add(pl.ObjectId);
            }
        }
        DrawSet(cutEdges, true);
        DrawSet(fillEdges, false);
        return made;
    }

    /// <summary>[§75] 옹벽 구간의 옹벽선(계단 상단 모서리) — 두꺼운 빨간 선(JACK 0728: 옹벽 구간은 노리선 대신 이것만).
    /// 레이어 'DH-옹벽선' ACI 1(빨강)·선가중치 0.50mm. 재실행 시 자기 레이어 청소.</summary>
    public static void DrawWallLines(Database db, Transaction tr,
        IEnumerable<IReadOnlyList<Point3>> lines)
    {
        ObjectId layerId = EnsureLayer(db, tr, "DH-옹벽선", 1);
        var ltr = (LayerTableRecord)tr.GetObject(layerId, OpenMode.ForWrite);
        ltr.Color = Color.FromColorIndex(ColorMethod.ByAci, 1);
        ltr.LineWeight = LineWeight.LineWeight050;
        EraseOnLayer(db, tr, "DH-옹벽선");
        var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
        foreach (var loop in lines)
        {
            if (loop == null || loop.Count < 2) continue;
            var pl = new Polyline3d { LayerId = layerId };
            ms.AppendEntity(pl); tr.AddNewlyCreatedDBObject(pl, true);
            foreach (var q in loop)
            {
                var v = new PolylineVertex3d(new Point3d(q.X, q.Y, q.Z));
                pl.AppendVertex(v); tr.AddNewlyCreatedDBObject(v, true);
            }
        }
    }

    /// <summary>[§75 — JACK 0728] 지표면 표시 정리: keepBaseName(정지면_DH)만 보이고 나머지(원지반·가상면 등)는
    /// 전부 숨김. keepBaseName=null이면 모든 지표면 표시 복원(DHGRADE 시작 시 — 원지반을 클릭 선택해야 하므로).</summary>
    public static void IsolateSurfaces(Transaction tr, string? keepBaseName)
    {
        var civilDoc = Autodesk.Civil.ApplicationServices.CivilApplication.ActiveDocument;
        foreach (ObjectId sid in civilDoc.GetSurfaceIds())
        {
            if (tr.GetObject(sid, OpenMode.ForRead) is not Autodesk.Civil.DatabaseServices.Surface s) continue;
            bool keep = keepBaseName == null
                || s.Name == keepBaseName
                || (s.Name.StartsWith(keepBaseName + "_") && int.TryParse(s.Name.Substring(keepBaseName.Length + 1), out _));
            try
            {
                // ★[v32.11 · 조사 반영] <b>이미 같은 값이면 쓰지 않는다.</b>
                //   값이 같아도 <b>쓰는 행위 자체가 Civil에게는 '수정'</b>이라, 그 표면을 붙여넣은
                //   합성면의 작업 항목에 '추가된 뒤 수정됨' 표시가 붙을 수 있다.
                //   먼저 읽어 보고 다를 때만 쓴다 — 호출부 전부(정지생성·설정·초기화)가 같이 이득을 본다.
                var eRead = (AcadEntity)tr.GetObject(sid, OpenMode.ForRead);
                if (eRead.Visible == keep) continue;
                var e = (AcadEntity)tr.GetObject(sid, OpenMode.ForWrite);
                e.Visible = keep;
            }
            catch { }
        }
    }

    /// <summary>[0728 — JACK] baseName 지표면의 표시 스타일을 이름 후보들 중 존재하는 것으로 설정.
    /// 정확 일치 우선, 없으면 '2'와 '10'이 들어간 등고선 스타일 폴백. 적용된 스타일명 반환("" = 미적용).</summary>
    public static string SetSurfaceStyle(Transaction tr, string baseName, params string[] candidates)
    {
        var civilDoc = Autodesk.Civil.ApplicationServices.CivilApplication.ActiveDocument;
        ObjectId styleId = ObjectId.Null; string styleName = "";
        var all = new List<(ObjectId id, string name)>();
        foreach (ObjectId sid in civilDoc.Styles.SurfaceStyles)
        {
            if (tr.GetObject(sid, OpenMode.ForRead) is Autodesk.Civil.DatabaseServices.Styles.SurfaceStyle st)
                all.Add((sid, st.Name));
        }
        foreach (var cand in candidates)
        {
            foreach (var (id2, nm) in all)
                if (string.Equals(nm, cand, StringComparison.OrdinalIgnoreCase)) { styleId = id2; styleName = nm; break; }
            if (!styleId.IsNull) break;
        }
        if (styleId.IsNull)
            foreach (var (id2, nm) in all)
                if (nm.Contains("2") && nm.Contains("10")) { styleId = id2; styleName = nm; break; }
        if (styleId.IsNull) return "";
        // [JACK 0728] 스타일에서 취하는 건 등고선 간격뿐 — '경계' 표시는 켜서 예전처럼 지표면 둘레가 보이고
        //   클릭 시 지표면이 선택되게(별도 초록 객체 불필요).
        try
        {
            var stw = (Autodesk.Civil.DatabaseServices.Styles.SurfaceStyle)tr.GetObject(styleId, OpenMode.ForWrite);
            stw.GetDisplayStylePlan(Autodesk.Civil.DatabaseServices.Styles.SurfaceDisplayStyleType.Boundary).Visible = true;
            stw.GetDisplayStyleModel(Autodesk.Civil.DatabaseServices.Styles.SurfaceDisplayStyleType.Boundary).Visible = true;
        }
        catch { }
        foreach (ObjectId sid in civilDoc.GetSurfaceIds())
        {
            if (tr.GetObject(sid, OpenMode.ForRead) is not Autodesk.Civil.DatabaseServices.Surface s) continue;
            string nm = s.Name;
            if (nm != baseName && !(nm.StartsWith(baseName + "_") && int.TryParse(nm.Substring(baseName.Length + 1), out _)))
                continue;
            try
            {
                var w = (Autodesk.Civil.DatabaseServices.Surface)tr.GetObject(sid, OpenMode.ForWrite);
                w.StyleId = styleId;
            }
            catch { }
        }
        return styleName;
    }

    /// <summary>★★[v32.7 · JACK 0812 '지표면들에 느낌표가 엄청 뜬다'] <b>가시성을 건드렸으면 전부 되살린다.</b>
    ///
    /// <para><b>여태 둘만 고치고 있었다.</b> 마무리 재작성은 <c>정지면_DH</c>와 <c>정지순수_DH</c>만 돌았다.
    /// 그런데 느낌표를 붙이는 주체는 <see cref="IsolateSurfaces"/>이고, 그건 <b>나머지 전부</b>를 숨긴다 —
    /// <c>가상절토_DH</c>·<c>가상성토_DH</c>·원지반·<c>정지면_DH이전</c>.
    /// 숨기면 구식이 되고 <b>다시 켜도 구식으로 남는다</b>(Autodesk 확인 결함).
    /// 실측 로그가 그것을 갈랐다: 우리가 챙긴 둘은 <c>구식=False</c>로 깨끗한데
    /// JACK 화면에는 느낌표가 <b>여럿</b> 떠 있었다 — <b>챙기지 않은 것들이었다.</b></para>
    ///
    /// <para><b>스냅샷은 없는 데 만들지 않는다.</b> 스냅샷은 '소스가 사라져도 형상을 유지'하는 장치라
    /// 원지반처럼 스냅샷이 없는 표면에 함부로 만들면 성격이 바뀐다. <b>있는 것만 갱신</b>한다.</para>
    ///
    /// <para>그러고도 구식으로 남는 표면이 있으면 <b>이름을 로그에 적는다</b> — 개수만으로는 못 좁힌다.</para></summary>
    /// <para>★★[v32.7b · JACK 0812 <i>"지표면 재작성도 누르고 스냅샷 재작성까지 눌러야 없어지는데"</i>]
    /// <b>순서가 문제였다 — 한 바퀴로는 안 된다.</b>
    /// <c>정지면_DH</c>는 원지반·가상절토·가상성토를 <b>붙여서</b> 만든 면이다.
    /// <b>소스가 구식이면 자식을 아무리 재작성해도 소스를 고치는 순간 자식이 다시 구식</b>이 된다.
    /// <c>GetSurfaceIds()</c>의 순서는 정해져 있지 않으므로 자식을 먼저 고칠 때가 있고, 그러면 헛일이다.
    /// JACK이 손으로 하나씩 누르면 없어진 이유가 그것이다 — 누르는 사이에 순서가 맞아떨어진다.
    /// → <b>의존 순서를 알아내려 하지 않는다.</b> 아무것도 구식이 아닐 때까지 <b>되풀이</b>한다
    /// (가시 제거·자기교차 정리에서 이미 쓰는 방식이다). 최대 4바퀴면 어떤 순서라도 가라앉는다.</para></summary>
    /// <para>★★[v32.8 · JACK 0812 <i>"지표면 특성을 보면 <b>붙여넣기</b> 가상성토와 가상절토에 느낌표가 있어"</i>]
    /// <b>느낌표는 표면이 아니라 '정의의 붙여넣기 항목'에 붙는다.</b>
    ///
    /// <para>그래서 <c>IsOutOfDate</c>가 계속 <c>False</c>로 나왔던 것이다 — 그 속성은 <b>표면 단위</b>이고,
    /// Prospector가 띄우는 것은 <b>작업(operation) 단위</b> 표시다. 우리 로그는 내내 '깨끗함'이라고
    /// 참말을 했는데, <b>다른 것을 재고 있었다</b>. JACK이 특성 대화상자를 열어 준 덕에 갈렸다.</para>
    ///
    /// <para><b>원인은 순서다.</b> 소스(<c>가상절토_DH</c>·<c>가상성토_DH</c>)를 재작성하면
    /// 그것을 붙여넣은 <b>합성면의 붙여넣기 항목이 그 순간 구식</b>이 된다.
    /// 종전 루프는 <c>IsOutOfDate</c>가 <c>False</c>라 <b>1바퀴 만에 끝나</b>(로그 `1바퀴 · 처음 구식 0`)
    /// 합성면을 다시 짓지 않았다. 표면 플래그를 수렴 신호로 쓴 것 자체가 틀렸다.</para>
    ///
    /// <para>→ <b>조건을 보지 않고 순서로 푼다.</b> ①소스를 전부 짓고 ②그 다음에 합성면을 짓는다.
    /// 합성면끼리도 <c>…이전</c>(다음 합성면의 소스)을 먼저 짓는다.
    /// 조건 없이 한 번씩 지으므로 <b>어떤 플래그가 진짜인지 몰라도 결과가 맞는다</b>.</para></summary>
    /// <summary>★★[v32.12 · JACK 0812 실험 A·C] <b>단계마다 트랜잭션을 끊는다 — 손으로 누르는 것과 같은 모양으로.</b>
    ///
    /// <para><b>실험이 두 갈래를 다 닫았다.</b>
    /// <b>A</b>: '지표면 재작성'만으로는 ⚠가 <b>하나도</b> 안 사라진다 → 지우는 것은 <c>스냅샷 재작성</c>뿐이다.
    /// (스냅샷이 있으면 빌드가 스냅샷에서 시작해 <b>앞의 붙여넣기를 다시 밟지 않기</b> 때문 — 공식 문서와 맞는다.)
    /// <b>C</b>: 다른 이름으로 저장하고 다시 열어도 ⚠가 그대로다 → <b>화면 갱신이 아니라 도면에 저장된 진짜 상태</b>다.</para>
    ///
    /// <para>그런데 우리 코드도 <c>RebuildSnapshot()</c>을 부르고 로그도 성공이다.
    /// <b>손으로 누른 것과 코드가 부른 것이 같은 일인데 결과가 다르다.</b> 남은 차이는 <b>트랜잭션 경계</b>다 —
    /// JACK은 클릭마다 작업이 끝나고 커밋되는데, 우리는 소스 재작성·합성면 재작성·스냅샷 재작성을
    /// <b>한 트랜잭션에 몰아넣었다</b>. 같은 호출을 같은 순서로 해도
    /// <b>커밋되지 않은 중간 상태 위에서 다음 호출이 도는</b> 것은 다른 일일 수 있다.</para>
    ///
    /// <para>→ <b>표면 하나마다 열고·하고·커밋</b>한다. 순서를 바꾸는 것이 아니라 <b>언제 확정되는가</b>를 바꾸는 것이라
    /// §30의 재시도 금지 12번(호출 순서 바꾸기)과 <b>다른 축</b>이다.</para></summary>
    public static string RebuildSurfacesStaged(Database db)
    {
        static bool IsComposite(string nm)
            => nm.StartsWith("정지면_DH", System.StringComparison.Ordinal)
            || nm.StartsWith("정지순수_DH", System.StringComparison.Ordinal);

        var src = new List<ObjectId>(); var compPrev = new List<ObjectId>(); var comp = new List<ObjectId>();
        try
        {
            var civilDoc = Autodesk.Civil.ApplicationServices.CivilApplication.ActiveDocument;
            using var tr0 = db.TransactionManager.StartTransaction();
            foreach (ObjectId sid in civilDoc.GetSurfaceIds())
            {
                if (tr0.GetObject(sid, OpenMode.ForRead) is not Autodesk.Civil.DatabaseServices.Surface s) continue;
                if (!IsComposite(s.Name)) src.Add(sid);
                else if (s.Name.Contains("이전")) compPrev.Add(sid);
                else comp.Add(sid);
            }
            tr0.Commit();
        }
        catch { }

        int a = StageOne(db, src, false);
        int b = StageOne(db, compPrev, false) + StageOne(db, comp, false);
        int c = StageOne(db, compPrev, true) + StageOne(db, comp, true);
        return $"단계별 재작성(표면마다 트랜잭션 분리) — ①소스 {a}개 ②합성면 {b}개 ③스냅샷 {c}개";
    }

    /// <summary>표면 하나에 <b>자기 트랜잭션</b>을 열어 재작성(또는 스냅샷 재작성)하고 바로 커밋한다.</summary>
    private static int StageOne(Database db, List<ObjectId> ids, bool snapshot)
    {
        int n = 0;
        foreach (var sid in ids)
        {
            try
            {
                using var tr = db.TransactionManager.StartTransaction();
                var w = (Autodesk.Civil.DatabaseServices.Surface)tr.GetObject(sid, OpenMode.ForWrite);
                // ★[v32.16] 여기도 0807판과 같은 방식으로 되돌린다 — 한 곳만 다른 경로면 서로 덮는다.
                if (snapshot) { if (w.HasSnapshot) { w.RebuildSnapshot(); n++; } }
                else { w.Rebuild(); n++; }
                tr.Commit();
            }
            catch { }
        }
        return n;
    }

    /// <summary>★★[v32.13 · JACK 0812] <b>붙여넣기 줄을 정의에서 지운다 — ⚠를 지우는 대신 ⚠가 붙을 줄을 없앤다.</b>
    ///
    /// <para><b>세 축이 모두 닫혔다.</b> 호출 순서(v32.5~v32.10) · 대상 범위(v32.7~v32.8) · 트랜잭션 경계(v32.12).
    /// 실험도 둘 다 음성이었다 — 재작성만으로는 안 지워지고(A), 저장·재오픈해도 남는다(C).
    /// JACK 확인: <i>"무조건 오른쪽 버튼으로 스냅샷 재작성을 눌러야만 없어져."</i></para>
    ///
    /// <para><b>발상을 바꾼다.</b> 스냅샷이 정의 <b>맨 끝</b>에 있으면(v32.9) 빌드는 스냅샷에서 시작하고
    /// <b>그 앞의 붙여넣기는 어차피 무시된다</b>(공식 문서). 즉 형상은 스냅샷이 통째로 물고 있고
    /// <b>붙여넣기 줄은 이미 잉여</b>다 — 남아서 하는 일이라고는 소스 표면에 매달려 ⚠를 다는 것뿐이다.
    /// 그러니 지운다.</para>
    ///
    /// <para><b>안전판이 본체다.</b> 지우고 나서 삼각형 수를 다시 세어, <b>조금이라도 줄면 커밋하지 않는다</b> —
    /// 트랜잭션이 통째로 물러나 도면은 손대기 전과 같아진다. 되돌리기 비용이 0이라야 이런 수를 쓸 수 있다.
    /// 스냅샷이 <b>없으면 아예 손대지 않는다</b>(형상이 사라진다).</para>
    ///
    /// <para><b>지운 뒤 <c>RebuildSnapshot</c>을 절대 부르지 않는다</b> — 텅 빈 정의로 다시 구워질 수 있다.
    /// 이 함수는 표면을 건드리는 <b>맨 마지막</b> 작업이어야 한다.</para></summary>
    public static bool StripPasteOperations(Database db, ObjectId surfId, out string diag)
    {
        diag = ""; bool ok = false;
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            if (tr.GetObject(surfId, OpenMode.ForWrite) is not TinSurface w) { diag = "TIN이 아니다"; return false; }
            string nm = w.Name;

            int before = -1;
            try { before = w.GetTriangles(false).Count; } catch { }
            if (before <= 0) { diag = $"'{nm}' 삼각형을 못 읽어 건너뜀"; return false; }
            if (!w.HasSnapshot) { diag = $"'{nm}' 스냅샷이 없어 건너뜀(지우면 형상이 사라진다)"; return false; }

            int removed = 0;
            var oc = w.Operations;
            for (int i = oc.Count - 1; i >= 0; i--)          // 반드시 뒤에서부터
            {
                string tn = oc[i].GetType().Name;
                if (tn.IndexOf("Paste", System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                try { oc.RemoveAt(i); removed++; } catch { }
            }
            if (removed == 0) { diag = $"'{nm}' 지울 붙여넣기가 없다"; return false; }

            try { w.Rebuild(); } catch { }
            int after = -1;
            try { after = w.GetTriangles(false).Count; } catch { }

            // ★ 안전판 — 형상이 깎이면 커밋하지 않는다(자동으로 무른다).
            if (after < before)
            {
                diag = $"'{nm}' ⚠형상이 줄어 되돌림 — 삼각형 {before} → {after} (붙여넣기 {removed}줄은 그대로 둔다)";
                return false;
            }

            tr.Commit(); ok = true;
            diag = $"'{nm}' 붙여넣기 {removed}줄 제거 · 삼각형 {before} → {after} (스냅샷만 남김)";
        }
        catch (System.Exception ex) { diag = "붙여넣기 제거 실패 — " + ex.Message; }
        return ok;
    }

    /// <summary>★[v32.12] 지표면 상태를 <b>읽기만</b> 해서 한 줄로 만든다 — 재작성은 하지 않는다.
    /// 종전엔 진단이 <see cref="RebuildSurfacesByBaseName"/> 안에 섞여 있어
    /// <b>진단하려면 표면을 건드려야</b> 했다 — 그 자체가 상태를 바꾼다.</summary>
    public static string Describe(Transaction tr, string baseName)
    {
        var civilDoc = Autodesk.Civil.ApplicationServices.CivilApplication.ActiveDocument;
        foreach (ObjectId sid in civilDoc.GetSurfaceIds())
        {
            try
            {
                if (tr.GetObject(sid, OpenMode.ForRead) is not Autodesk.Civil.DatabaseServices.Surface w) continue;
                string nm = w.Name;
                if (nm != baseName && !(nm.StartsWith(baseName + "_") && int.TryParse(nm.Substring(baseName.Length + 1), out _)))
                    continue;

                string tri = "?";
                try { if (w is TinSurface ts) tri = ts.GetTriangles(false).Count.ToString(); } catch { }
                string ops = "?";
                try
                {
                    var oc = w.Operations;
                    int nPaste = 0, idxSnap = -1;
                    for (int k = 0; k < oc.Count; k++)
                    {
                        string tn = oc[k].GetType().Name;
                        if (tn.IndexOf("Paste", System.StringComparison.OrdinalIgnoreCase) >= 0) nPaste++;
                        if (tn.IndexOf("Snapshot", System.StringComparison.OrdinalIgnoreCase) >= 0) idxSnap = k;
                    }
                    ops = $"{oc.Count}줄(붙여넣기 {nPaste} · 스냅샷 {(idxSnap < 0 ? "없음" : $"{idxSnap + 1}번째")})";
                }
                catch (System.Exception oe) { ops = "읽기실패:" + oe.GetType().Name; }

                return $"'{nm}' 삼각형={tri} 보임={((AcadEntity)w).Visible} 정의={ops}"
                     + $" 구식={w.IsOutOfDate} 스냅샷구식={w.IsSnapshotOutOfDate} 스냅샷있음={w.HasSnapshot}";
            }
            catch { }
        }
        return $"'{baseName}' 없음";
    }

    public static string RebuildAllSurfaces(Transaction tr)
    {
        var civilDoc = Autodesk.Civil.ApplicationServices.CivilApplication.ActiveDocument;

        // 우리가 '붙여넣기로 만든' 면 = 합성면. 나머지는 그 소스다.
        static bool IsComposite(string nm)
            => nm.StartsWith("정지면_DH", System.StringComparison.Ordinal)
            || nm.StartsWith("정지순수_DH", System.StringComparison.Ordinal);

        var src = new List<ObjectId>();
        var compPrev = new List<ObjectId>();   // '…이전' — 다른 합성면의 소스라 먼저
        var comp = new List<ObjectId>();
        foreach (ObjectId sid in civilDoc.GetSurfaceIds())
        {
            try
            {
                if (tr.GetObject(sid, OpenMode.ForRead) is not Autodesk.Civil.DatabaseServices.Surface s) continue;
                if (!IsComposite(s.Name)) src.Add(sid);
                else if (s.Name.Contains("이전")) compPrev.Add(sid);
                else comp.Add(sid);
            }
            catch { }
        }

        int nSrc = 0, nComp = 0, snap = 0;
        foreach (var sid in src)
            try { var w = (Autodesk.Civil.DatabaseServices.Surface)tr.GetObject(sid, OpenMode.ForWrite); w.Rebuild(); nSrc++; }
            catch { }

        foreach (var sid in compPrev.Concat(comp))
        {
            try
            {
                var w = (Autodesk.Civil.DatabaseServices.Surface)tr.GetObject(sid, OpenMode.ForWrite);
                w.Rebuild();                                                   // 붙여넣기 항목을 되살리고
                // ★[v32.10] <b>스냅샷으로 끝낸다</b> — 찍은 뒤에 또 지으면 스냅샷이 구식이 된다(위 <see cref="Freeze"/> 설명).
                if (w.HasSnapshot) { try { w.RebuildSnapshot(); snap++; } catch { } }
                nComp++;
            }
            catch { }
        }

        // 되읽어 확인한다 — 넣었다고 세면 로그가 거짓말을 한다(이 저장소의 규율).
        var stuck = new List<string>();
        foreach (ObjectId sid in civilDoc.GetSurfaceIds())
        {
            try
            {
                if (tr.GetObject(sid, OpenMode.ForRead) is not Autodesk.Civil.DatabaseServices.Surface w) continue;
                if (w.IsOutOfDate || (w.HasSnapshot && w.IsSnapshotOutOfDate))
                    stuck.Add($"{w.Name}(구식={w.IsOutOfDate}/스냅샷구식={w.IsSnapshotOutOfDate})");
            }
            catch { }
        }
        return $"지표면 재작성 — 소스 {nSrc}개 먼저 · 합성면 {nComp}개 나중(스냅샷 {snap}) · 표면단위 플래그 잔여 {stuck.Count}"
             + (stuck.Count > 0 ? " ⚠[" + string.Join(" · ", stuck) + "]" : "")
             + "  ※정의 탭 ⚠는 <작업 한 줄> 단위라 이 값으로 안 잡힌다(2026 API에 읽을 속성 없음 — 조사 확인)";
    }

    /// <summary>★[v32.2] 이름(또는 이름_N)인 지표면의 <b>표시 여부만</b> 바꾼다.
    /// <see cref="IsolateSurfaces"/>는 '하나만 남기고 나머지를 끄는' 물건이라 여기엔 못 쓴다 —
    /// 이건 <b>지목한 것만</b> 건드리고 나머지는 그대로 둔다.</summary>
    public static int SetSurfaceVisible(Transaction tr, string baseName, bool visible)
    {
        var civilDoc = Autodesk.Civil.ApplicationServices.CivilApplication.ActiveDocument;
        int n = 0;
        foreach (ObjectId sid in civilDoc.GetSurfaceIds())
        {
            if (tr.GetObject(sid, OpenMode.ForRead) is not Autodesk.Civil.DatabaseServices.Surface s) continue;
            string nm = s.Name;
            if (nm != baseName && !(nm.StartsWith(baseName + "_") && int.TryParse(nm.Substring(baseName.Length + 1), out _)))
                continue;
            try { ((AcadEntity)tr.GetObject(sid, OpenMode.ForWrite)).Visible = visible; n++; }
            catch { }
        }
        return n;
    }

    /// <summary>[0728] 이름이 baseName(또는 _N)인 지표면 재작성 — 소스 숨김(Visible) 등으로 붙는
    /// '정의 구식(⚠)' 표시 해소용. 실패해도 무시.</summary>
    public static string RebuildSurfacesByBaseName(Transaction tr, string baseName)
    {
        var civilDoc = Autodesk.Civil.ApplicationServices.CivilApplication.ActiveDocument;
        int hit = 0, snapOk = 0, snapNo = 0, reOk = 0; string first = "";
        string snapState = "";   // 스냅샷 상태 속성이 있으면 첫 표면 것만 남긴다(계측)
        foreach (ObjectId sid in civilDoc.GetSurfaceIds())
        {
            if (tr.GetObject(sid, OpenMode.ForRead) is not Autodesk.Civil.DatabaseServices.Surface s) continue;
            string nm = s.Name;
            if (nm != baseName && !(nm.StartsWith(baseName + "_") && int.TryParse(nm.Substring(baseName.Length + 1), out _)))
                continue;
            hit++;
            try
            {
                var w = (Autodesk.Civil.DatabaseServices.Surface)tr.GetObject(sid, OpenMode.ForWrite);
                // [0807] 스냅샷 갱신이 **실제로 됐는지** 남긴다 — 종전엔 catch{}로 삼켜서
                //   느낌표가 왜 안 없어지는지 로그로 알 길이 없었다.
                //
                // ★★[v32.2 · JACK 0812] <b>여기도 순서가 뒤집혀 있었다 — 재작성이 스냅샷보다 뒤였다.</b>
                //   이 함수는 정지 생성의 <b>맨 마지막</b>에 불린다(사용자가 보는 시점).
                //   스냅샷을 찍고 나서 `Rebuild()`를 하면 표면이 다시 스냅샷보다 새것이 되어
                //   <b>정확히 이 자리에서</b> 느낌표가 되살아난다. → 짓고 나서 찍는다.
                try { w.Rebuild(); reOk++; }
                catch (System.Exception ex) { if (first.Length == 0) first = ex.Message; }

                // ★★[v32.4 · 검토 반영] <b>스냅샷이 없는데 <c>RebuildSnapshot</c>을 부르면 반드시 실패한다</b>
                //   (공식 문서: <i>"RebuildSnapshot will cause an error if the snapshot does not exist."</i>).
                //   종전엔 <see cref="Freeze"/>와 달리 여기엔 <c>CreateSnapshot</c>이 없어, 스냅샷 없는 표면에서는
                //   <b>매번 확정 실패</b>였다 — 로그의 '스냅샷 실패' 숫자가 그래서 줄지 않았다.
                //   그리고 아직 구식이면 스냅샷 호출 자체가 오류 조건이므로 한 번 더 짓고 간다.
                try
                {
                    if (w.IsOutOfDate) w.Rebuild();
                    if (w.HasSnapshot) w.RebuildSnapshot(); else w.CreateSnapshot();
                    // ★★[v32.10] <b>여기서 또 짓지 않는다.</b> v32.5엔 트레일링 <c>Rebuild</c>가 있었는데,
                    //   그 근거(<c>구식=True</c>)는 <b>스냅샷이 정의 중간에 있을 때의 증상</b>이었다.
                    //   v32.9에서 스냅샷을 맨 끝으로 옮긴 뒤로는 <b>찍고 나서 또 지으면 스냅샷이 구식</b>이 된다
                    //   (JACK 실측: 붙여넣기 느낌표는 사라지고 스냅샷 느낌표만 남았다). <b>지은 뒤 찍고, 끝.</b>
                    snapOk++;
                }
                catch (System.Exception ex) { snapNo++; if (first.Length == 0) first = ex.Message; }

                // ★[계측] 반사를 걷어내고 <b>직접</b> 읽는다 — 이름이 2024·2026에서 같은 것을 확인했다.
                //   반사로는 이름·값을 뭉뚱그려 찍을 뿐이지만, 직접 읽으면
                //   <b>표면이 구식인지</b>(IsOutOfDate)와 <b>스냅샷이 구식인지</b>(IsSnapshotOutOfDate)를 <b>갈라서</b> 본다.
                //   느낌표가 또 뜨면 이 한 줄이 어느 쪽인지 바로 가려 준다.
                if (snapState.Length == 0)
                {
                    try
                    {
                        // ★[v32.6 · JACK 0812] <b>삼각형 수를 같이 남긴다 — "지표면이 아예 없다"를 가리기 위해.</b>
                        //   숨겨 둔 표면은 도면에서 선택이 안 되므로 <b>빈 것과 구분이 안 된다</b>.
                        //   숫자가 0이면 진짜로 빈 것이고, 크면 그냥 안 보이는 것이다.
                        string tri = "?";
                        try { if (w is TinSurface ts) tri = ts.GetTriangles(false).Count.ToString(); } catch { }

                        // ★★[v32.11 · 조사 반영] <b>정의 목록의 실제 모양을 처음으로 로그에 남긴다.</b>
                        //   조사 결론: 정의 탭의 ⚠는 <b>작업(operation) 한 줄 단위</b>인데
                        //   Civil 3D 2026 어셈블리의 <c>SurfaceOperation</c> 공개 멤버는
                        //   <c>Guid·Enabled·Move*·Dispose</c>가 전부다 — <b>구식 여부를 읽는 속성이 없다</b>.
                        //   그러니 ⚠ 자체는 못 읽지만, <b>줄이 몇 개이고 붙여넣기가 몇 개인지</b>는 읽을 수 있다.
                        //   그것만으로도 '스냅샷이 몇 번째인가·붙여넣기가 남아 있는가'를 스샷 없이 확인할 수 있다.
                        string ops = "?";
                        //   ※ 작업의 <b>형식 이름을 코드에 박지 않는다</b> — 한 번 빗맞혀 빌드가 깨졌다.
                        //     <c>GetType().Name</c>을 읽어 판별하면 Civil 버전이 달라도 안 깨진다.
                        try
                        {
                            var oc = w.Operations;
                            int nPaste = 0, idxSnap = -1;
                            for (int k = 0; k < oc.Count; k++)
                            {
                                string tn = oc[k].GetType().Name;
                                if (tn.IndexOf("Paste", System.StringComparison.OrdinalIgnoreCase) >= 0) nPaste++;
                                if (tn.IndexOf("Snapshot", System.StringComparison.OrdinalIgnoreCase) >= 0) idxSnap = k;
                            }
                            ops = $"{oc.Count}줄(붙여넣기 {nPaste} · 스냅샷 {(idxSnap < 0 ? "없음" : $"{idxSnap + 1}번째")})";
                        }
                        catch (System.Exception oe) { ops = "읽기실패:" + oe.GetType().Name; }

                        snapState = $" 삼각형={tri} 보임={((AcadEntity)w).Visible} 정의={ops}"
                                  + $" 구식={w.IsOutOfDate} 스냅샷구식={w.IsSnapshotOutOfDate}"
                                  + $" 스냅샷있음={w.HasSnapshot} 자동재작성={w.AutoRebuild}";
                    }
                    catch { }
                }
            }
            catch (System.Exception ex) { if (first.Length == 0) first = ex.Message; }
        }
        return $"'{baseName}' 표면 {hit}개 — 재작성 {reOk} · 스냅샷 갱신 {snapOk}/실패 {snapNo}" +
               (snapState.Length > 0 ? " ·" + snapState : "") +
               (first.Length > 0 ? $" · 첫 사유: {first}" : "");
    }

    /// <summary>이름(또는 이름_숫자)의 지표면 존재 여부 — DHNORI/DHINFRA 실행 게이트 ③용.</summary>
    public static bool SurfaceExistsByBaseName(Transaction tr, string baseName)
        => !FindSurfaceByBaseName(tr, baseName).IsNull;

    /// <summary>[다중 구역 0729] baseName(또는 _N 번호 변형) 지표면의 ObjectId — 없으면 Null.</summary>
    public static ObjectId FindSurfaceByBaseName(Transaction tr, string baseName)
    {
        var civilDoc = Autodesk.Civil.ApplicationServices.CivilApplication.ActiveDocument;
        foreach (ObjectId sid in civilDoc.GetSurfaceIds())
        {
            if (tr.GetObject(sid, OpenMode.ForRead) is not Autodesk.Civil.DatabaseServices.Surface s) continue;
            string nm = s.Name;
            if (nm == baseName || (nm.StartsWith(baseName + "_") && int.TryParse(nm.Substring(baseName.Length + 1), out _)))
                return sid;
        }
        return ObjectId.Null;
    }

    // ── helpers ──
    private static void AddRingBreakline(TinSurface tin, IReadOnlyList<Point3> loop)
    {
        if (loop.Count < 3) return;
        var seen = new HashSet<(long, long)>(); // 링마다 독립 — 링 간 정점 충돌로 정점이 스킵되어 브레이크라인에 구멍 나는 것 방지
        var pts = new List<Point3d>();
        foreach (var pt in loop)
        {
            var key = ((long)Math.Round(pt.X * 1000), (long)Math.Round(pt.Y * 1000));
            if (!seen.Add(key)) continue;
            pts.Add(new Point3d(pt.X, pt.Y, pt.Z));
        }
        if (pts.Count < 3) return;

        // [§75 — 0728] 옹벽 구간 좌우 끝의 '급하강(다이브)' 긴 선분(>2.5m)은 브레이크라인에서 제외.
        //   깊은 단일수록 다이브가 수십 m라 이웃 링(1m 간격)의 다이브끼리 평면 교차 → 이벤트 뷰어 오류 홍수.
        //   정상 링은 densify로 전 선분 ≤1m라 무영향. 측벽 면은 TIN 삼각화가 채우므로 형상은 유지된다.
        const double MaxSeg = 2.5;
        const double MaxSeg2 = MaxSeg * MaxSeg;
        var runs = new List<List<Point3d>>();
        var cur = new List<Point3d> { pts[0] };
        for (int i = 1; i < pts.Count; i++)
        {
            double dx = pts[i].X - pts[i - 1].X, dy = pts[i].Y - pts[i - 1].Y;
            if (dx * dx + dy * dy > MaxSeg2) { if (cur.Count >= 2) runs.Add(cur); cur = new List<Point3d>(); }
            cur.Add(pts[i]);
        }
        double cdx = pts[0].X - pts[^1].X, cdy = pts[0].Y - pts[^1].Y;
        bool closeOk = cdx * cdx + cdy * cdy <= MaxSeg2;
        if (runs.Count == 0 && closeOk)
        {
            // 다이브 없음 = 정상 링 — 기존과 동일하게 닫아서 등록(이음매 거대 삼각형 방지).
            var pc = new Point3dCollection();
            foreach (var q in cur) pc.Add(q);
            pc.Add(cur[0]);
            try { tin.BreaklinesDefinition.AddStandardBreaklines(pc, 1.0, 0.0, 0.0, 0.0); } catch { }
            return;
        }
        if (closeOk && runs.Count > 0 && cur.Count > 0)
        {
            cur.AddRange(runs[0]); // 꼬리 run이 이음새(마지막→첫)로 머리 run과 이어짐 → 병합
            runs[0] = cur;
        }
        else if (cur.Count >= 2) runs.Add(cur);
        foreach (var run in runs)
        {
            if (run.Count < 2) continue;
            var pc = new Point3dCollection();
            foreach (var q in run) pc.Add(q);
            try { tin.BreaklinesDefinition.AddStandardBreaklines(pc, 1.0, 0.0, 0.0, 0.0); } catch { }
        }
    }

    /// <summary>이름이 baseName 또는 baseName_N 인 지표면을 모두 삭제(잠긴/참조 중이면 그 항목만 건너뜀).</summary>
    internal static void EraseSurfacesByBaseName(Transaction tr, string baseName, ObjectId protect = default)
    {
        var civilDoc = Autodesk.Civil.ApplicationServices.CivilApplication.ActiveDocument;
        var victims = new List<ObjectId>();
        foreach (ObjectId sid in civilDoc.GetSurfaceIds())
        {
            if (sid == protect) continue; // [JACK 0715] 선택된 원지반 보호 — LandXML 지반 이름이 '정지면_DH'여도 삭제 금지
            if (tr.GetObject(sid, OpenMode.ForRead) is not Autodesk.Civil.DatabaseServices.Surface s) continue;
            string nm = s.Name;
            if (nm == baseName || (nm.StartsWith(baseName + "_") && int.TryParse(nm.Substring(baseName.Length + 1), out _)))
                victims.Add(sid);
        }
        foreach (var sid in victims)
        {
            try { (tr.GetObject(sid, OpenMode.ForWrite) as AcadEntity)?.Erase(); } catch { }
        }
    }

    internal static string UniqueName(Database db, Transaction tr, string baseName)
    {
        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var civilDoc = Autodesk.Civil.ApplicationServices.CivilApplication.ActiveDocument;
        foreach (ObjectId id in civilDoc.GetSurfaceIds())
            if (tr.GetObject(id, OpenMode.ForRead) is Autodesk.Civil.DatabaseServices.Surface s) existing.Add(s.Name);
        if (!existing.Contains(baseName)) return baseName;
        for (int i = 2; ; i++) { string c = $"{baseName}_{i}"; if (!existing.Contains(c)) return c; }
    }

    private static ObjectId EnsureLayer(Database db, Transaction tr, string name, short aci)
    {
        var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
        if (lt.Has(name)) return lt[name];
        lt.UpgradeOpen();
        var ltr = new LayerTableRecord { Name = name, Color = Color.FromColorIndex(ColorMethod.ByAci, aci) };
        ObjectId id = lt.Add(ltr); tr.AddNewlyCreatedDBObject(ltr, true);
        return id;
    }

    private static void EraseOnLayer(Database db, Transaction tr, string layerName)
    {
        var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
        if (!lt.Has(layerName)) return;
        var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
        var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);
        var ids = new List<ObjectId>();
        foreach (ObjectId id in ms)
            if (tr.GetObject(id, OpenMode.ForRead) is AcadEntity ent && ent.Layer == layerName) ids.Add(id);
        foreach (ObjectId id in ids)
            if (tr.GetObject(id, OpenMode.ForWrite) is AcadEntity e) e.Erase();
    }
}
