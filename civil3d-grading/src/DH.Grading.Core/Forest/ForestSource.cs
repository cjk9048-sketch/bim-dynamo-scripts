namespace DH.Grading.Core.Forest;

/// <summary>★[v115 · JACK 1105 임상도] 타일 창고 읽기 — «폴더 또는 http» 꼴로 짜되 이번 판은 폴더만(계획 2판 0-4 · 5절). NAS(<c>HttpForestSource</c>)는 다음 판.</summary>
public interface IForestTileSource
{
    /// <summary>로그 · 시험용 이름(경로는 페이지에 안 나감).</summary>
    string Describe { get; }
    ForestIndex Index { get; }
    /// <summary>칸 파일 바이트(gzip) — index에 없는 칸이면 null(숲 없음).</summary>
    byte[]? Tile(int tx, int ty);
}

/// <summary>폴더 창고 — <c>{root}\index.json</c> · <c>{root}\tiles\{tx}\{ty}.dhf</c>. index에 있는 칸만 믿는다(덜 끝난 변환은 안 보임).</summary>
public sealed class FolderForestSource : IForestTileSource
{
    public readonly string Root;
    public ForestIndex Index { get; }
    public string Describe => "폴더 " + Root;

    public FolderForestSource(string root)
    {
        Root = root;
        Index = ForestIndex.Read(Path.Combine(root, "index.json"));
    }

    public byte[]? Tile(int tx, int ty)
    {
        if (!Index.Tiles.ContainsKey(ForestTileFormat.Key(tx, ty))) return null;
        return File.ReadAllBytes(Path.Combine(Root, ForestTileFormat.RelPath(tx, ty)));
    }
}

public static class ForestSource
{
    /// <summary>도면 폴더 사본 폴더 이름(쓴 칸만 · 도면과 같은 폴더).</summary>
    public const string CopyDir = "DH_임상도";

    /// <summary>창고 고르기 결과 — Source가 null이면 Missing(층 회색) · Quiet면 ⚠ 없이 · Warn이면 경고 한 줄.</summary>
    public sealed record Pick(IForestTileSource? Source, string Missing, bool Quiet, bool Warn, string Log, bool FromCopy);

    /// <summary>계획 5절 표 — 창고 → (실패) 도면 폴더 사본 → (실패) missing.
    /// <paramref name="isDefault"/> = 설정이 기본 경로 · <paramref name="seen"/> = 이 PC에서 창고를 한 번이라도 찾은 적이 있음(L8 — 처음 쓰는 사람에게 ⚠를 안 띄움).</summary>
    public static Pick Open(string tiles, bool isDefault, bool seen, string? dwgDir)
    {
        string why = "";
        if (tiles.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || tiles.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            why = "웹 주소 타일은 아직";
        else if (tiles.Length > 0 && File.Exists(Path.Combine(tiles, "index.json")))
        {
            try { return new Pick(new FolderForestSource(tiles), "", false, false, "창고", false); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or System.Text.Json.JsonException or KeyNotFoundException or InvalidOperationException)
            { why = "창고 index.json을 못 읽음 — " + ex.GetType().Name; }
        }
        else why = "창고 없음";
        // 도면 폴더 사본
        if (!string.IsNullOrEmpty(dwgDir))
        {
            string cp = Path.Combine(dwgDir, CopyDir);
            if (File.Exists(Path.Combine(cp, "index.json")))
                try { return new Pick(new FolderForestSource(cp), "", false, false, why + " → 도면 폴더 사본", true); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or System.Text.Json.JsonException or KeyNotFoundException or InvalidOperationException)
                { why += " · 사본도 못 읽음 — " + ex.GetType().Name; }
        }
        if (why.StartsWith("웹 주소", StringComparison.Ordinal)) return new Pick(null, "웹 주소 타일은 아직 지원하지 않습니다(폴더만)", false, true, why, false);
        if (isDefault && !seen) return new Pick(null, "임상도 타일 없음", true, false, why, false);
        return new Pick(null, "임상도 타일 폴더가 없습니다", false, true, why, false);
    }

    /// <summary>칸들을 읽어 푼다 — 없는 칸(숲 없음)은 건너뜀.</summary>
    public static List<ForestTile> Load(IForestTileSource src, IEnumerable<(int Tx, int Ty)> keys, out List<(int Tx, int Ty)> used)
    {
        var o = new List<ForestTile>();
        used = new();
        foreach (var (tx, ty) in keys)
        {
            var b = src.Tile(tx, ty);
            if (b == null) continue;
            o.Add(ForestTileFormat.DecodeTile(b));
            used.Add((tx, ty));
        }
        return o;
    }

    /// <summary>★[M5] 쓴 칸만 도면 폴더 <c>DH_임상도\</c>에 사본 — 칸 파일은 바이트가 같으면 다시 안 씀 · index.json은 사본 칸만(원본 index의 출처 · 이름표는 그대로).
    /// 돌려준 값 = 새로 쓴 칸 수(-1 = 사본 자체가 원천이라 안 씀).</summary>
    public static int CopyUsed(IForestTileSource src, IEnumerable<(int Tx, int Ty)> used, string dwgDir)
    {
        string dst = Path.Combine(dwgDir, CopyDir);
        if (src is FolderForestSource fs && string.Equals(Path.GetFullPath(fs.Root).TrimEnd('\\'), Path.GetFullPath(dst).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) return -1;
        ForestIndex? have = null;
        try { if (File.Exists(Path.Combine(dst, "index.json"))) have = ForestIndex.Read(Path.Combine(dst, "index.json")); } catch (Exception ex) when (ex is IOException or InvalidDataException or System.Text.Json.JsonException or KeyNotFoundException or InvalidOperationException) { have = null; }
        var ix = new ForestIndex { Made = src.Index.Made, Simplify = src.Index.Simplify, Sources = src.Index.Sources, Codes = src.Index.Codes };
        // 사본에 이미 있던 칸(같은 창고 판) 은 남긴다 — 다른 범위로 [3D 보기]해도 쌓인다
        if (have != null && have.Made == src.Index.Made)
            foreach (var (k, v) in have.Tiles) if (File.Exists(Path.Combine(dst, ForestTileFormat.RelPath(int.Parse(k.Split('_')[0], System.Globalization.CultureInfo.InvariantCulture), int.Parse(k.Split('_')[1], System.Globalization.CultureInfo.InvariantCulture))))) ix.Tiles[k] = v;
        int wrote = 0;
        foreach (var (tx, ty) in used)
        {
            string key = ForestTileFormat.Key(tx, ty);
            if (!src.Index.Tiles.TryGetValue(key, out var meta)) continue;
            string path = Path.Combine(dst, ForestTileFormat.RelPath(tx, ty));
            var b = src.Tile(tx, ty);
            if (b == null) continue;
            bool same = File.Exists(path) && new FileInfo(path).Length == b.LongLength && File.ReadAllBytes(path).AsSpan().SequenceEqual(b);
            if (!same)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllBytes(path + ".tmp", b);
                File.Move(path + ".tmp", path, overwrite: true);
                wrote++;
            }
            ix.Tiles[key] = meta;
        }
        bool sameIndex = have != null && have.Made == ix.Made && have.Tiles.Count == ix.Tiles.Count && have.Tiles.All(kv => ix.Tiles.TryGetValue(kv.Key, out var v) && v.SequenceEqual(kv.Value));
        if (!sameIndex) { Directory.CreateDirectory(dst); ix.Write(Path.Combine(dst, "index.json")); }
        return wrote;
    }
}
