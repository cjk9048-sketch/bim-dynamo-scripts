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
            catch (Exception ex) when (Bad(ex))
            { why = "창고 index.json을 못 읽음 — " + ex.GetType().Name; }
        }
        else why = "창고 없음";
        // 도면 폴더 사본
        if (OpenCopy(dwgDir, out var copy, out string cwhy)) return new Pick(copy, "", false, false, why + " → 도면 폴더 사본", true);
        if (cwhy.Length > 0) why += " · " + cwhy;
        if (why.StartsWith("웹 주소", StringComparison.Ordinal)) return new Pick(null, "웹 주소 타일은 아직 지원하지 않습니다(폴더만)", false, true, why, false);
        if (isDefault && !seen) return new Pick(null, "임상도 타일 없음", true, false, why, false);
        return new Pick(null, "임상도 타일 폴더가 없습니다", false, true, why, false);
    }

    /// <summary>읽기 · 풀기에서 «그 파일이 나쁨»으로 보는 예외(그 밖 예외는 위로 — 진단 로그).</summary>
    static bool Bad(Exception ex) => ex is IOException or UnauthorizedAccessException or InvalidDataException or System.Text.Json.JsonException or KeyNotFoundException
        or InvalidOperationException or FormatException or ArgumentOutOfRangeException or OverflowException;

    /// <summary>도면 폴더 사본 열기(없으면 false · 못 읽으면 false + 까닭).</summary>
    public static bool OpenCopy(string? dwgDir, out IForestTileSource? copy, out string why)
    {
        copy = null; why = "";
        if (string.IsNullOrEmpty(dwgDir)) return false;
        string cp = Path.Combine(dwgDir, CopyDir);
        if (!File.Exists(Path.Combine(cp, "index.json"))) return false;
        try { copy = new FolderForestSource(cp); return true; }
        catch (Exception ex) when (Bad(ex)) { why = "사본도 못 읽음 — " + ex.GetType().Name; return false; }
    }

    /// <summary>칸들을 읽어 푼다 — 없는 칸(숲 없음)은 건너뜀.</summary>
    public static List<ForestTile> Load(IForestTileSource src, IEnumerable<(int Tx, int Ty)> keys, out List<(int Tx, int Ty)> used)
        => Load(src, keys, null, out used, out _, out _);

    /// <summary>★[설치 전 검토 M3] 칸마다 따로 — 못 읽는 칸(없음 · 잠김 · 깨짐)은 <paramref name="fallback"/>(도면 폴더 사본)에서 한 번 더 · 그래도 안 되면 그 칸만 빼고 셈(층 전체를 죽이지 않음).</summary>
    public static List<ForestTile> Load(IForestTileSource src, IEnumerable<(int Tx, int Ty)> keys, Func<IForestTileSource?>? fallback,
                                        out List<(int Tx, int Ty)> used, out int failed, out int fromFallback)
    {
        var o = new List<ForestTile>();
        used = new(); failed = 0; fromFallback = 0;
        IForestTileSource? fb = null; bool fbTried = false;
        foreach (var (tx, ty) in keys)
        {
            ForestTile? t = null;
            try
            {
                var b = src.Tile(tx, ty);
                if (b == null) continue;   // 숲 없는 칸
                t = Checked(ForestTileFormat.DecodeTile(b), tx, ty);
            }
            catch (Exception ex) when (Bad(ex))
            {
                if (!fbTried) { fbTried = true; try { fb = fallback?.Invoke(); } catch (Exception fex) when (Bad(fex)) { fb = null; } }
                try { if (fb?.Tile(tx, ty) is { } b2) { t = Checked(ForestTileFormat.DecodeTile(b2), tx, ty); fromFallback++; } }
                catch (Exception ex2) when (Bad(ex2)) { t = null; }
                if (t == null) { failed++; continue; }
            }
            o.Add(t);
            used.Add((tx, ty));
        }
        return o;
    }

    static ForestTile Checked(ForestTile t, int tx, int ty) => t.Tx == tx && t.Ty == ty ? t : throw new InvalidDataException("칸 번호가 다름");

    /// <summary>★[M5] 쓴 칸만 도면 폴더 <c>DH_임상도\</c>에 사본 — 칸 파일은 바이트가 같으면 다시 안 씀 · index.json은 사본 칸만(원본 index의 출처 · 이름표는 그대로).
    /// ★[설치 전 검토 M2] 달라고 한 칸 가운데 창고에도 숲이 없는 칸은 index «empty»에 적는다 — 사본만으로 읽을 때 «모르는 칸»(사본을 만든 범위 밖)을 가려 경고한다.
    /// 돌려준 값 = 새로 쓴 칸 수(-1 = 사본 자체가 원천이라 안 씀).</summary>
    public static int CopyUsed(IForestTileSource src, IEnumerable<(int Tx, int Ty)> requested, IEnumerable<(int Tx, int Ty)> used, string dwgDir)
    {
        string dst = Path.Combine(dwgDir, CopyDir);
        if (src is FolderForestSource fs && string.Equals(Path.GetFullPath(fs.Root).TrimEnd('\\'), Path.GetFullPath(dst).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) return -1;
        ForestIndex? have = null;
        try { if (File.Exists(Path.Combine(dst, "index.json"))) have = ForestIndex.Read(Path.Combine(dst, "index.json")); } catch (Exception ex) when (ex is IOException or InvalidDataException or System.Text.Json.JsonException or KeyNotFoundException or InvalidOperationException) { have = null; }
        var ix = new ForestIndex { Made = src.Index.Made, Simplify = src.Index.Simplify, Sources = src.Index.Sources, Codes = src.Index.Codes };
        // 사본에 이미 있던 칸(같은 창고 판) 은 남긴다 — 다른 범위로 [3D 보기]해도 쌓인다
        if (have != null && have.Made == src.Index.Made)
        {
            foreach (var (k, v) in have.Tiles) if (File.Exists(Path.Combine(dst, ForestTileFormat.RelPath(int.Parse(k.Split('_')[0], System.Globalization.CultureInfo.InvariantCulture), int.Parse(k.Split('_')[1], System.Globalization.CultureInfo.InvariantCulture))))) ix.Tiles[k] = v;
            foreach (var k in have.Empty) ix.Empty.Add(k);
        }
        foreach (var (tx, ty) in requested)
        {
            string key = ForestTileFormat.Key(tx, ty);
            if (!src.Index.Tiles.ContainsKey(key)) ix.Empty.Add(key);
        }
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
        ix.Empty.ExceptWith(ix.Tiles.Keys);
        bool sameIndex = have != null && have.Made == ix.Made && have.Tiles.Count == ix.Tiles.Count && have.Tiles.All(kv => ix.Tiles.TryGetValue(kv.Key, out var v) && v.SequenceEqual(kv.Value))
                         && have.Empty.SetEquals(ix.Empty);
        if (!sameIndex) { Directory.CreateDirectory(dst); ix.Write(Path.Combine(dst, "index.json")); }
        return wrote;
    }
}
