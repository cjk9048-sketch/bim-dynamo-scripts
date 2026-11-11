using System.IO;
using DH.CivilCoord.Core;

namespace DH.CivilCoord.Revit;

/// <summary>
/// Civil 3D 영역 파일을 한 번만 읽어 둠(세모 30만이면 읽기에 시간이 걸림 — 창을 다시 열 때마다 읽지 않게). 파일 시각이 바뀌면(Civil 에서 다시 내보냄) 다시 읽음.
/// </summary>
internal static class RegionCache
{
    private static string _key = "";
    private static Region3D? _region;
    private static string _error = "";

    /// <summary>
    /// 쓸 3D 영역 파일 경로(설치 전 검토 낮음 — 다른 구조물 영역이 남거나 직접 연 파일이 말없이 바뀌지 않게):
    /// 전에 쓰던 파일이 «이 기준점 파일의 짝»(같은 폴더 · pointsFile 이름이 같음)이고 사용자가 직접 골랐으면 그것 →
    /// 기준점 파일의 region3d → 전에 쓰던 짝 파일 → 빈 글.
    /// </summary>
    public static string Resolve(string pointsPath, CivilPoints? cp, string previous, bool previousDirect)
    {
        bool prevOk = previous.Length > 0 && pointsPath.Length > 0 && File.Exists(previous) &&
                      string.Equals(Path.GetDirectoryName(previous), Path.GetDirectoryName(pointsPath), StringComparison.OrdinalIgnoreCase) &&
                      string.Equals(Region3D.PeekString(previous, "pointsFile"), Path.GetFileName(pointsPath), StringComparison.OrdinalIgnoreCase);
        if (prevOk && previousDirect) return previous;
        if (cp != null && cp.Region3DFile.Length > 0)
            return CivilPoints.Region3DPath(pointsPath, cp.Region3DFile);
        return prevOk ? previous : "";
    }

    /// <summary>경로의 3D 영역(없거나 못 읽으면 null · error = 까닭 — 파일이 없으면 빈 글).</summary>
    public static Region3D? Get(string path, out string error)
    {
        error = "";
        if (path.Length == 0 || !File.Exists(path)) return null;
        string key;
        try { key = path + "|" + File.GetLastWriteTimeUtc(path).Ticks; }
        catch (Exception ex) { error = ex.Message; return null; }
        if (key == _key) { error = _error; return _region; }
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var r = Region3D.Read(path, out string? err);
        _region = err == null ? r : null;
        _error = err ?? "";
        // 못 읽었으면 기억하지 않음(복사 중인 파일 — 다음에 다시 읽게)
        _key = err == null ? key : "";
        error = _error;
        Log.Line(err == null
            ? $"3D 영역\t{path}\t{r.Summary}\t버림 세모 {r.DroppedTriangles} · 선 {r.DroppedLines}\t{r.Addin}\t{sw.ElapsedMilliseconds} ms"
            : $"3D 영역 못 읽음\t{path}\t{err}");
        return _region;
    }
}
