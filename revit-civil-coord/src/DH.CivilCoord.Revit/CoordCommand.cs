using System.IO;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using DH.CivilCoord.Core;

namespace DH.CivilCoord.Revit;

/// <summary>
/// «Civil 좌표 맞춤»: 창 → (점 찍기 · IFC 내보내기 · 공유 좌표에 적용) → 다시 창 … 닫으면 끝.
/// 창에 넣은 값은 rvt 안에 둔다(<see cref="FitStore"/> — 바뀌었을 때만 · 닫을 때 한 번).
/// 늘 Succeeded 를 돌려준다 — Failed 는 이미 커밋한 것(공유 좌표 · 맞춤 값)까지 되돌린다.
/// </summary>
[Transaction(TransactionMode.Manual)]
public sealed class CoordCommand : IExternalCommand
{
    private const double Ft = 0.3048;

    public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
    {
        try { Run(data.Application); }
        catch (Exception ex)
        {
            Log.Line("오류\t" + ex);
            try { Dialogs.Show("원인: " + ex.Message, "자세한 것: %APPDATA%\\DH.CivilCoord\\log.txt"); } catch { }
        }
        return Result.Succeeded;
    }

    private static void Run(UIApplication app)
    {
        var uidoc = app.ActiveUIDocument;
        if (uidoc == null) return;
        var doc = uidoc.Document;
        if (doc.IsFamilyDocument) { Dialogs.Show("프로젝트(rvt)에서 쓰는 명령입니다"); return; }

        var saved = FitStore.Load(doc);
        var state = saved?.Clone() ?? new FitState();
        // 비교 기준도 같은 규칙으로(모드 정하기) — 아니면 열고 닫기만 해도 «저장» 트랜잭션이 생김(검토 중간)
        var baseline = (saved ?? new FitState()).Clone();
        baseline.ResolveMode();
        string savedJson = Cmp(baseline);
        var heights = LevelHeights.Read(doc);
        bool dark = false;
        try { dark = UIThemeManager.CurrentTheme == UITheme.Dark; } catch { }
        Log.Line($"열기\t{doc.Title}\t맞춤 값 {(saved != null ? "있음" : "없음")}");

        state.ResolveMode();
        // 미리보기: Revit 부재 윤곽(명령 한 번에 한 번) · Civil 파일(바뀐 때만 다시 읽음 — 선 · 바뀜 검사)
        var model = Exporter.Preview(uidoc, heights);
        CivilPoints? civil = null; string civilKey = "";

        string status = "", kind = "";
        try
        {
            while (true)
            {
                var fit0 = state.Fit();
                double zOff = heights.OffsetM + state.DzValue;
                var shared = SharedCoords.Read(doc, fit0?.Problem == null ? fit0 : null, zOff);
                bool civilChanged = false, civilExists = false;
                string civilError = "";
                if (state.CivilFile.Length > 0 && File.Exists(state.CivilFile))
                {
                    civilExists = true;
                    string key = state.CivilFile + "|" + File.GetLastWriteTimeUtc(state.CivilFile).Ticks;
                    if (key != civilKey)
                    {
                        var cp = CivilPoints.Read(state.CivilFile, out string? cerr);
                        civil = cerr == null ? cp : null;
                        // 못 읽었으면 시각을 기억하지 않음(잠깐 잠긴 파일 · NAS 복사 중 — 다음에 다시 읽게) · 까닭은 창에(검토 낮음)
                        civilKey = cerr == null ? key : "";
                        if (cerr != null) { civilError = "원인: " + cerr; Log.Line($"Civil 파일 다시 읽기 못 함\t{state.CivilFile}\t{cerr}"); }
                    }
                    civilChanged = civil != null && state.FileDiffers(civil);
                }
                else { civil = null; civilKey = ""; }
                // Civil 3D 영역(JACK 1111): 기준점 파일의 region3d → 전에 쓰던 파일 · 파일 시각이 바뀌면 다시 읽음(RegionCache)
                string prevRegion = state.RegionFile;
                state.RegionFile = RegionCache.Resolve(state.CivilFile, civil, prevRegion, state.RegionDirect);
                state.RegionDirect = state.RegionDirect && state.RegionFile == prevRegion;
                var region = RegionCache.Get(state.RegionFile, out string regionError);
                if (region == null && regionError.Length == 0 && civil is { Region3DFile.Length: > 0 }) regionError = $"짝 파일 {civil.Region3DFile} 없음";
                var win = new CoordWindow(state, heights, shared, status, kind, DefaultPath(doc), app.MainWindowHandle, dark, model, civil, civilChanged, civilExists, civilError,
                                          region, regionError, CivilSurface.CountMade(doc));
                win.ShowDialog();
                state = win.State;
                status = ""; kind = "";

                if (win.Action is CoordWindow.Act.Pick1 or CoordWindow.Act.Pick2)
                {
                    int n = win.Action == CoordWindow.Act.Pick1 ? 1 : 2;
                    var p = Pick(uidoc, n, out string err);
                    if (p != null)
                    {
                        var xy = new[] { p.X * Ft, p.Y * Ft };
                        if (n == 1) state.R1 = xy; else state.R2 = xy;
                        Log.Line($"점 {n}\t내부 {xy[0]:0.0000}, {xy[1]:0.0000} m\t뷰 «{uidoc.ActiveView?.Name}»");
                        // 두 점이 바뀌면 그 위에서 만든 손 맞춤은 뜻이 없어짐 → 지우고 알림(같은 자리를 다시 찍었으면 그대로)
                        if (state.ClearStaleHand()) { status = "Revit 점이 바뀌어 손 맞춤을 지웠습니다"; kind = "Warn"; }
                    }
                    else if (err.Length > 0) { status = err; kind = "Bad"; }
                    continue;
                }

                var fit = state.Fit();
                if (fit == null || fit.Problem != null) break;   // 창이 막지만 한 번 더
                zOff = heights.OffsetM + state.DzValue;

                // 두 선분 길이가 달라도 묻지 않음(JACK 1110 인터뷰 «묻지 않고 늘 비율대로») — IFC 는 평면 비율 적용 · 공유 좌표는 옮김 · 돌림만 + 알림.
                // 비율 · 거리 차는 창의 «확인» 줄과 미리보기(윤곽이 Civil 선에서 벗어나는지)로 본다.
                if (win.Action == CoordWindow.Act.Export)
                {
                    (status, kind) = Exporter.Run(uidoc, state, fit, heights, win.ExportPath);
                    continue;
                }
                if (win.Action == CoordWindow.Act.Insert)
                {
                    // [모델에 넣기](JACK 1111): 미리보기와 같은 자리 — 구조물 맞춤의 거꾸로(비율 · 손 맞춤 · 높이)
                    if (win.InsertOpts is { } io && win.Region is { } rg)
                    {
                        (kind, status) = CivilSurface.Insert(doc, rg, fit, zOff, io);
                    }
                    continue;
                }
                if (win.Action == CoordWindow.Act.Apply)
                {
                    if (!Dialogs.YesNo("Revit 공유 좌표를 Civil 좌표로 바꿀까요?", "프로젝트 기준점 · 측량점 값이 바뀝니다. Ctrl+Z로 되돌릴 수 있습니다."))
                        continue;
                    var snapshot = state.Clone();
                    double dz = state.DzValue, offBefore = heights.OffsetM;
                    bool dzIntoLevels = false;
                    var h0 = heights;
                    var (ok, msg) = SharedCoords.Apply(doc, fit, zOff, () =>
                    {
                        // 높이 기준이 «측량점»인 레벨은 바뀐 공유 높이를 따라 dz 만큼 올라감 → 더할 값을 0 으로(다음에 두 번 더하지 않게 — 검토 중간)
                        if (dz != 0 && Math.Abs(h0.OffsetNow(doc) - (offBefore + dz)) < 1e-4) { snapshot.Dz = "0"; dzIntoLevels = true; }
                        FitStore.WriteInside(doc, snapshot);
                    });
                    if (ok)
                    {
                        state = snapshot;
                        savedJson = Cmp(snapshot);
                        heights = LevelHeights.Read(doc);
                        if (dzIntoLevels) msg += $"\n레벨 높이 기준이 측량점이라 더할 값 {dz:0.000} m가 레벨 높이에 들어감 → 더할 값 0";
                    }
                    status = msg; kind = ok ? "Good" : "Bad";
                    continue;
                }
                break;   // 닫기
            }
        }
        finally
        {
            state.ClearStaleHand();   // 닫을 때 쓰지 않는(두 점이 바뀐) 손 맞춤은 남기지 않음
            if (Cmp(state) != savedJson)
            {
                string? err = FitStore.Save(doc, state);
                Log.Line(err == null ? "맞춤 값 저장" : "맞춤 값 저장 못 함\t" + err);
                // 작업 공유에서 프로젝트 정보를 다른 사람이 빌려 갔거나 읽기 전용이면 실패 — 넣은 값이 조용히 사라지지 않게 알림(검토 낮음)
                if (err != null) try { Dialogs.Show("맞춘 값을 rvt에 저장하지 못했습니다", "원인: " + err + "\n다음에 열면 다시 넣어야 합니다."); } catch { }
            }
        }
    }

    /// <summary>
    /// 저장할지 견줄 글: 3D 영역 경로는 뺌 — 기준점 파일의 region3d 로 늘 다시 찾는 값이라, 열고 닫기만 해도 저장 트랜잭션이 생기지 않게(설치 전 검토 낮음).
    /// </summary>
    private static string Cmp(FitState s)
    {
        var c = s.Clone();
        c.RegionFile = ""; c.RegionDirect = false;
        return c.ToJson();
    }

    /// <summary>점 하나 찍기(평면 XY 만 씀). 그만두면 null.</summary>
    private static XYZ? Pick(UIDocument uidoc, int n, out string err)
    {
        err = "";
        // 평면 XY 를 쓰므로 위에서 내려다보는 평면도에서만 — 입면 · 단면 · 3D 는 붙은 점이 보는 방향으로 작업 기준면에 내려앉아 XY 가 조용히 틀린다(검토 중간)
        var view = uidoc.ActiveView;
        if (view is not ViewPlan)
        {
            err = "평면도에서 찍으세요(입면 · 단면 · 3D에서는 찍은 점의 평면 자리가 틀어짐)";
            Log.Line($"점 {n} 못 찍음\t뷰 «{view?.Name}» {view?.ViewType}");
            return null;
        }
        try
        {
            const ObjectSnapTypes snaps = ObjectSnapTypes.Endpoints | ObjectSnapTypes.Intersections | ObjectSnapTypes.Midpoints
                                        | ObjectSnapTypes.Centers | ObjectSnapTypes.Perpendicular | ObjectSnapTypes.Quadrants;
            return uidoc.Selection.PickPoint(snaps, $"점 {n}: Civil {n}번 자리와 같은 구조물 모서리를 찍으세요 (Esc = 그만)");
        }
        catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return null; }
        catch (Autodesk.Revit.Exceptions.InvalidOperationException ex)
        {
            Log.Line($"점 {n} 못 찍음\t{ex.Message}");
            err = "이 뷰에서는 점을 찍을 수 없습니다(작업 기준면 없음) — 평면도에서 찍으세요";
            return null;
        }
    }

    private static string DefaultPath(Document doc)
    {
        string title = Path.GetFileNameWithoutExtension(doc.Title);
        string dir = Log.LastFolder;
        if (dir.Length == 0 || !Directory.Exists(dir))
            dir = doc.PathName.Length > 0 ? Path.GetDirectoryName(doc.PathName) ?? "" : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        return Path.Combine(dir, title + "_Civil.ifc");
    }
}
