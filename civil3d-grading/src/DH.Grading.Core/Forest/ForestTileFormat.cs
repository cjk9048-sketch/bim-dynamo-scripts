using System.Buffers.Binary;
using System.IO.Compression;
using System.Text.Json;

namespace DH.Grading.Core.Forest;

/// <summary>숲 폴리곤 하나(칸 안 조각) — 속성은 타일 바이트(<see cref="ForestCodes"/>) · 고리는 칸 남서 모서리부터 0.1 m 단위(0~20000) ·
/// 고리 0 = 바깥(반시계) · 나머지 = 구멍(시계) · 끝점 되풀이 없음.</summary>
public sealed class ForestPoly
{
    public uint Src;          // 원본 번호 ≪ 24 | 레코드
    public ushort Part;       // 레코드 안 부분 번호
    public byte Frtp, Koftr, Dnst, Hgt, Dm, Ag, Fror;
    public List<ushort[]> Rings = new();   // 고리마다 [x0, y0, x1, y1, …]

    public int Source => (int)(Src >> 24);
    public int Record => (int)(Src & 0xFFFFFF);
    public long Order => ((long)Src << 16) | Part;
}

public sealed class ForestTile
{
    public int Tx, Ty;
    public List<ForestPoly> Polys = new();
    public double X0 => Tx * (double)ForestTileFormat.TileSize;
    public double Y0 => Ty * (double)ForestTileFormat.TileSize;
}

/// <summary>★[v115 · JACK 1105 임상도] 타일 꼴 <c>DHF1</c>(계획 2판 2절) — 쓰기 · 읽기 한 파일. 칸 = 2 km(EPSG 5179) · <c>tiles\{tx}\{ty}.dhf</c>(gzip).
/// <para>머리 «DHF1» · u16 판 1 · u16 0 · i32 tx · i32 ty · u32 폴리곤 수 · 폴리곤마다 u32 src · u16 part · u8 frtp · koftr · dnst · hgt · dm · ag · fror ·
/// u16 고리 수 · 고리마다 u32 점 수 + 점(u16 x, u16 y). 차례 = (src, part, 조각 차례). 같은 입력 = 같은 바이트(gzip 머리에 시각 없음).</para></summary>
public static class ForestTileFormat
{
    public const int TileSize = 2000;
    public const double Quant = 0.1;
    public const int QMax = 20000;
    public const string Magic = "DHF1";
    public const int Version = 1;

    public static int TileOf(double v) => (int)Math.Floor(v / TileSize);

    public static string RelPath(int tx, int ty) => Path.Combine("tiles", tx.ToString(System.Globalization.CultureInfo.InvariantCulture), ty.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".dhf");
    public static string Key(int tx, int ty) => tx.ToString(System.Globalization.CultureInfo.InvariantCulture) + "_" + ty.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>폴리곤 하나의 바이트(머리 없이) — 변환기가 조각마다 미리 만들어 둔다.</summary>
    public static byte[] EncodePoly(ForestPoly p)
    {
        int n = 4 + 2 + 7 + 2 + p.Rings.Sum(r => 4 + 2 * r.Length);
        var b = new byte[n];
        int o = 0;
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(o), p.Src); o += 4;
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(o), p.Part); o += 2;
        b[o++] = p.Frtp; b[o++] = p.Koftr; b[o++] = p.Dnst; b[o++] = p.Hgt; b[o++] = p.Dm; b[o++] = p.Ag; b[o++] = p.Fror;
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(o), (ushort)p.Rings.Count); o += 2;
        foreach (var r in p.Rings)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(o), (uint)(r.Length / 2)); o += 4;
            foreach (ushort v in r) { BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(o), v); o += 2; }
        }
        return b;
    }

    /// <summary>칸 파일 바이트(gzip) — 폴리곤 바이트들을 주어진 차례 그대로.</summary>
    public static byte[] EncodeTile(int tx, int ty, IReadOnlyList<byte[]> polys)
    {
        using var ms = new MemoryStream();
        using (var gz = new GZipStream(ms, CompressionLevel.Optimal, leaveOpen: true))
        {
            var h = new byte[20];
            System.Text.Encoding.ASCII.GetBytes(Magic).CopyTo(h, 0);
            BinaryPrimitives.WriteUInt16LittleEndian(h.AsSpan(4), Version);
            BinaryPrimitives.WriteUInt16LittleEndian(h.AsSpan(6), 0);
            BinaryPrimitives.WriteInt32LittleEndian(h.AsSpan(8), tx);
            BinaryPrimitives.WriteInt32LittleEndian(h.AsSpan(12), ty);
            BinaryPrimitives.WriteUInt32LittleEndian(h.AsSpan(16), (uint)polys.Count);
            gz.Write(h);
            foreach (var p in polys) gz.Write(p);
        }
        return ms.ToArray();
    }

    public static byte[] EncodeTile(ForestTile t) => EncodeTile(t.Tx, t.Ty, t.Polys.Select(EncodePoly).ToList());

    public static ForestTile DecodeTile(byte[] gzBytes)
    {
        using var ms = new MemoryStream(gzBytes);
        using var gz = new GZipStream(ms, CompressionMode.Decompress);
        using var o = new MemoryStream();
        gz.CopyTo(o);
        return Decode(o.GetBuffer().AsSpan(0, (int)o.Length));
    }

    public static ForestTile Decode(ReadOnlySpan<byte> b)
    {
        if (b.Length < 20 || b[0] != 'D' || b[1] != 'H' || b[2] != 'F' || b[3] != '1') throw new InvalidDataException("DHF1 아님");
        if (BinaryPrimitives.ReadUInt16LittleEndian(b[4..]) != Version) throw new InvalidDataException("DHF 판 다름");
        var t = new ForestTile { Tx = BinaryPrimitives.ReadInt32LittleEndian(b[8..]), Ty = BinaryPrimitives.ReadInt32LittleEndian(b[12..]) };
        uint n = BinaryPrimitives.ReadUInt32LittleEndian(b[16..]);
        // ★[설치 전 검토 L6] 수를 바이트 길이와 먼저 견준다(깨진 칸이 큰 배열을 잡게 하지 않음) — 폴리곤마다 최소 15바이트 · 고리마다 4바이트 · 점마다 4바이트
        if (n > (uint)((b.Length - 20) / 15)) throw new InvalidDataException("DHF 폴리곤 수가 길이를 넘음");
        int o = 20;
        for (uint i = 0; i < n; i++)
        {
            if (o + 15 > b.Length) throw new InvalidDataException("DHF 폴리곤 머리가 잘림");
            var p = new ForestPoly
            {
                Src = BinaryPrimitives.ReadUInt32LittleEndian(b[o..]), Part = BinaryPrimitives.ReadUInt16LittleEndian(b[(o + 4)..]),
                Frtp = b[o + 6], Koftr = b[o + 7], Dnst = b[o + 8], Hgt = b[o + 9], Dm = b[o + 10], Ag = b[o + 11], Fror = b[o + 12],
            };
            o += 13;
            int nr = BinaryPrimitives.ReadUInt16LittleEndian(b[o..]); o += 2;
            if ((long)o + 4L * nr > b.Length) throw new InvalidDataException("DHF 고리 수가 길이를 넘음");
            for (int k = 0; k < nr; k++)
            {
                if (o + 4 > b.Length) throw new InvalidDataException("DHF 고리 머리가 잘림");
                long npl = BinaryPrimitives.ReadUInt32LittleEndian(b[o..]); o += 4;
                if (o + 4 * npl > b.Length) throw new InvalidDataException("DHF 점 수가 길이를 넘음");
                int np = (int)npl;
                var r = new ushort[2 * np];
                for (int q = 0; q < r.Length; q++) { r[q] = BinaryPrimitives.ReadUInt16LittleEndian(b[o..]); o += 2; }
                p.Rings.Add(r);
            }
            t.Polys.Add(p);
        }
        if (o != b.Length) throw new InvalidDataException($"DHF 길이 다름({o} ≠ {b.Length})");
        return t;
    }

    /// <summary>고리 → 5179 좌표 배열(닫는 점 붙임 — NTS 고리용).</summary>
    public static NetTopologySuite.Geometries.Coordinate[] World(ForestTile t, ushort[] r)
    {
        var cs = new NetTopologySuite.Geometries.Coordinate[r.Length / 2 + 1];
        for (int i = 0; i < r.Length / 2; i++) cs[i] = new NetTopologySuite.Geometries.Coordinate(t.X0 + r[2 * i] * Quant, t.Y0 + r[2 * i + 1] * Quant);
        cs[^1] = cs[0].Copy();
        return cs;
    }

    /// <summary>폴리곤 → NTS 다각형(5179) · 고리 셋 미만은 뺀다.</summary>
    public static NetTopologySuite.Geometries.Polygon? ToPolygon(ForestTile t, ForestPoly p, NetTopologySuite.Geometries.GeometryFactory gf)
    {
        if (p.Rings.Count == 0 || p.Rings[0].Length < 6) return null;
        var shell = gf.CreateLinearRing(World(t, p.Rings[0]));
        var holes = p.Rings.Skip(1).Where(r => r.Length >= 6).Select(r => gf.CreateLinearRing(World(t, r))).ToArray();
        return gf.CreatePolygon(shell, holes);
    }

    /// <summary>고리 넓이(㎡ · 부호 = 반시계 양수) — 0.1 m 정수 좌표로 정확히.</summary>
    public static double RingArea(ushort[] r)
    {
        long a2 = 0;
        int n = r.Length / 2;
        for (int i = 0, j = n - 1; i < n; j = i++) a2 += (long)r[2 * j] * r[2 * i + 1] - (long)r[2 * i] * r[2 * j + 1];
        return a2 * 0.5 * Quant * Quant;
    }

    public static double PolyArea(ForestPoly p) => p.Rings.Count == 0 ? 0 : RingArea(p.Rings[0]) + p.Rings.Skip(1).Sum(RingArea);
}

/// <summary>창고의 <c>index.json</c>(맨 마지막에 쓴다 — 여기 있는 칸만 믿는다).</summary>
public sealed class ForestIndex
{
    public sealed class Source
    {
        public string Name = "";
        public int Id;
        public long Bytes;
        public string Modified = "";
        public int Records, Kept, Frtp0, Small, Failed;
        public double AreaIn, AreaOut;
        public int YearMin, YearMax;
    }

    public string Format = ForestTileFormat.Magic;
    public int Ver = ForestTileFormat.Version;
    public int Crs = 5179, Tile = ForestTileFormat.TileSize;
    public double QuantM = ForestTileFormat.Quant, Simplify = 1.0;
    public string Made = "";
    public List<Source> Sources = new();
    public Dictionary<string, Dictionary<string, string>> Codes = new();
    public SortedDictionary<string, long[]> Tiles = new(StringComparer.Ordinal);
    /// <summary>★[설치 전 검토 M2] 도면 폴더 사본만 — 그 도면이 달라고 했으나 창고에도 숲이 없던 칸(«비어 있음»이 확실한 칸).
    /// 사본에서 읽을 때 Tiles에도 Empty에도 없는 칸 = 사본을 만들 때 안 달라고 한 칸(숲이 있는지 모름 → 경고).</summary>
    public SortedSet<string> Empty = new(StringComparer.Ordinal);

    /// <summary>이 칸을 아는가(숲 칸이거나 «비어 있음» 칸).</summary>
    public bool Knows(string key) => Tiles.ContainsKey(key) || Empty.Contains(key);

    public string Hash = "";

    public void Write(string path)
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            w.WriteStartObject();
            w.WriteString("format", Format); w.WriteNumber("ver", Ver); w.WriteNumber("crs", Crs); w.WriteNumber("tile", Tile);
            w.WriteNumber("quant", QuantM); w.WriteNumber("simplify", Simplify); w.WriteString("made", Made);
            w.WriteStartArray("sources");
            foreach (var s in Sources)
            {
                w.WriteStartObject();
                w.WriteString("name", s.Name); w.WriteNumber("id", s.Id); w.WriteNumber("bytes", s.Bytes); w.WriteString("modified", s.Modified);
                w.WriteNumber("records", s.Records); w.WriteNumber("kept", s.Kept); w.WriteNumber("frtp0", s.Frtp0); w.WriteNumber("small", s.Small); w.WriteNumber("failed", s.Failed);
                w.WriteNumber("areaIn", Math.Round(s.AreaIn, 1)); w.WriteNumber("areaOut", Math.Round(s.AreaOut, 1));
                w.WriteStartArray("years"); w.WriteNumberValue(s.YearMin); w.WriteNumberValue(s.YearMax); w.WriteEndArray();
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteStartObject("codes");
            foreach (var (k, m) in Codes.OrderBy(k => k.Key, StringComparer.Ordinal))
            {
                w.WriteStartObject(k);
                foreach (var (c, n) in m.OrderBy(q => q.Key, StringComparer.Ordinal)) w.WriteString(c, n);
                w.WriteEndObject();
            }
            w.WriteEndObject();
            w.WriteStartObject("tiles");
            foreach (var (k, v) in Tiles) { w.WriteStartArray(k); foreach (long x in v) w.WriteNumberValue(x); w.WriteEndArray(); }
            w.WriteEndObject();
            if (Empty.Count > 0) { w.WriteStartArray("empty"); foreach (var k in Empty) w.WriteStringValue(k); w.WriteEndArray(); }
            w.WriteEndObject();
        }
        string tmp = path + ".tmp";
        File.WriteAllBytes(tmp, ms.ToArray());
        File.Move(tmp, path, overwrite: true);
    }

    public static ForestIndex Read(string path)
    {
        byte[] raw = File.ReadAllBytes(path);
        using var doc = JsonDocument.Parse(raw);
        var r = doc.RootElement;
        var ix = new ForestIndex
        {
            Format = r.GetProperty("format").GetString() ?? "", Ver = r.GetProperty("ver").GetInt32(), Crs = r.GetProperty("crs").GetInt32(), Tile = r.GetProperty("tile").GetInt32(),
            QuantM = r.GetProperty("quant").GetDouble(), Simplify = r.GetProperty("simplify").GetDouble(),
            Made = r.TryGetProperty("made", out var md) ? md.GetString() ?? "" : "",
            Hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(raw))[..16],
        };
        if (ix.Format != ForestTileFormat.Magic || ix.Ver != ForestTileFormat.Version || ix.Crs != 5179 || ix.Tile != ForestTileFormat.TileSize)
            throw new InvalidDataException("임상도 타일 꼴이 다릅니다");
        if (r.TryGetProperty("sources", out var ss))
            foreach (var s in ss.EnumerateArray())
            {
                var src = new Source
                {
                    Name = s.GetProperty("name").GetString() ?? "", Id = s.GetProperty("id").GetInt32(), Bytes = s.GetProperty("bytes").GetInt64(), Modified = s.GetProperty("modified").GetString() ?? "",
                    Records = s.GetProperty("records").GetInt32(), Kept = s.GetProperty("kept").GetInt32(), Frtp0 = s.GetProperty("frtp0").GetInt32(), Small = s.GetProperty("small").GetInt32(), Failed = s.GetProperty("failed").GetInt32(),
                    AreaIn = s.GetProperty("areaIn").GetDouble(), AreaOut = s.GetProperty("areaOut").GetDouble(),
                };
                if (s.TryGetProperty("years", out var y) && y.GetArrayLength() == 2) { src.YearMin = y[0].GetInt32(); src.YearMax = y[1].GetInt32(); }
                ix.Sources.Add(src);
            }
        if (r.TryGetProperty("codes", out var cs))
            foreach (var k in cs.EnumerateObject())
                ix.Codes[k.Name] = k.Value.EnumerateObject().ToDictionary(q => q.Name, q => q.Value.GetString() ?? "");
        if (r.TryGetProperty("tiles", out var ts))
            foreach (var k in ts.EnumerateObject())
                ix.Tiles[k.Name] = k.Value.EnumerateArray().Select(v => v.GetInt64()).ToArray();
        if (r.TryGetProperty("empty", out var es) && es.ValueKind == JsonValueKind.Array)
            foreach (var e in es.EnumerateArray()) if (e.GetString() is { Length: > 0 } k) ix.Empty.Add(k);
        return ix;
    }
}
