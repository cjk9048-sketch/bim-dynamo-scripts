using System.Buffers.Binary;
using System.Text;

namespace DH.Grading.Core.Forest;

/// <summary>★[v115 · JACK 1105 임상도] SHP(다각형 형 5) + SHX + DBF(고정 폭 · cp949) 읽기 — 임상도 변환기(<c>tools\ForestTiles</c>)와 하네스만 쓴다(Civil은 타일만 읽음).
/// <para>위치는 모두 <b>long</b>(SHP · DBF가 1 GB를 넘는다 — 47_2.shp 1.36 GB). SHX 오프셋(16비트 단어 · 큰 끝)으로 SHP 레코드를 찾는다.
/// 원자료 폴더에는 쓰지 않는다(읽기 전용 열기 · <see cref="FileShare.Read"/>).</para></summary>
public sealed class ShpDbfReader : IDisposable
{
    public sealed class Field
    {
        public string Name = "";
        public char Type;
        public int Offset, Length;
    }

    /// <summary>레코드 하나 — 고리들(점은 SHP 차례 그대로 · 닫는 점 포함 · 고리 방향은 SHP 규칙: 바깥 시계 · 구멍 반시계).</summary>
    public sealed class Record
    {
        public int Index;
        public int ShapeType;
        public List<double[]> Rings = new();   // 고리마다 [x0, y0, x1, y1, …]
        public byte[] Dbf = Array.Empty<byte>();
        public long Points;
    }

    static readonly Encoding Cp949 = MakeCp949();
    static Encoding MakeCp949()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(949);
    }

    readonly FileStream _shp, _shx, _dbf;
    public readonly int Count;
    public readonly int DbfCount, DbfHeaderLen, DbfRecordLen;
    public readonly byte Ldid;
    public readonly List<Field> Fields = new();
    public readonly int ShapeType;
    public readonly double MinX, MinY, MaxX, MaxY;

    public ShpDbfReader(string shpPath)
    {
        string bas = Path.Combine(Path.GetDirectoryName(shpPath) ?? "", Path.GetFileNameWithoutExtension(shpPath));
        _shp = Open(shpPath);
        _shx = Open(bas + ".shx");
        _dbf = Open(bas + ".dbf");
        // SHP 머리 100바이트 — 형(32) · 상자(36~67)
        var h = new byte[100];
        _shp.ReadExactly(h);
        if (BinaryPrimitives.ReadInt32BigEndian(h) != 9994) throw new InvalidDataException("SHP 머리 아님 — " + shpPath);
        ShapeType = BinaryPrimitives.ReadInt32LittleEndian(h.AsSpan(32));
        MinX = BitConverter.ToDouble(h, 36); MinY = BitConverter.ToDouble(h, 44); MaxX = BitConverter.ToDouble(h, 52); MaxY = BitConverter.ToDouble(h, 60);
        Count = (int)((_shx.Length - 100) / 8);
        // DBF 머리
        var d = new byte[32];
        _dbf.ReadExactly(d);
        DbfCount = (int)BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(4));
        DbfHeaderLen = BinaryPrimitives.ReadUInt16LittleEndian(d.AsSpan(8));
        DbfRecordLen = BinaryPrimitives.ReadUInt16LittleEndian(d.AsSpan(10));
        Ldid = d[29];
        var fd = new byte[DbfHeaderLen - 32];
        _dbf.ReadExactly(fd);
        int off = 1;   // 첫 바이트 = 지움 표시
        for (int p = 0; p + 32 <= fd.Length && fd[p] != 0x0D; p += 32)
        {
            int nl = Array.IndexOf(fd, (byte)0, p, 11);
            if (nl < 0) nl = p + 11;
            var f = new Field { Name = Cp949.GetString(fd, p, nl - p).Trim(), Type = (char)fd[p + 11], Length = fd[p + 16], Offset = off };
            off += f.Length;
            Fields.Add(f);
        }
    }

    static FileStream Open(string p) => new(p, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, FileOptions.SequentialScan);

    public Field? FieldOf(string name) => Fields.FirstOrDefault(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>칸 글(앞뒤 빈칸 뺌) — 코드 칸은 ASCII · 이름 칸은 cp949.</summary>
    public static string Text(byte[] rec, Field? f)
    {
        if (f == null || f.Offset + f.Length > rec.Length) return "";
        return Cp949.GetString(rec, f.Offset, f.Length).Trim();
    }

    /// <summary>ASCII 코드 칸(숫자 · 글자) — 빈칸이면 "".</summary>
    public static string Code(byte[] rec, Field? f)
    {
        if (f == null || f.Offset + f.Length > rec.Length) return "";
        int a = f.Offset, b = f.Offset + f.Length;
        while (a < b && (rec[a] == (byte)' ' || rec[a] == 0)) a++;
        while (b > a && (rec[b - 1] == (byte)' ' || rec[b - 1] == 0)) b--;
        return Encoding.ASCII.GetString(rec, a, b - a);
    }

    public static double Number(byte[] rec, Field? f)
    {
        string s = Code(rec, f);
        return double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double v) ? v : double.NaN;
    }

    /// <summary>레코드 [i0, i0 + n)를 차례로 읽는다(SHP · DBF 둘 다 · 한 스레드에서).</summary>
    public List<Record> ReadBatch(int i0, int n)
    {
        var o = new List<Record>(n);
        int end = Math.Min(Count, i0 + n);
        // SHX 덩어리 한 번에
        var shx = new byte[8 * (end - i0)];
        _shx.Position = 100 + 8L * i0;
        _shx.ReadExactly(shx);
        // DBF 덩어리 한 번에
        var dbf = new byte[(long)DbfRecordLen * (end - i0)];
        _dbf.Position = DbfHeaderLen + (long)DbfRecordLen * i0;
        _dbf.ReadExactly(dbf);
        for (int i = i0; i < end; i++)
        {
            long pos = 2L * (uint)BinaryPrimitives.ReadInt32BigEndian(shx.AsSpan(8 * (i - i0)));
            var r = ReadShape(pos);
            r.Index = i;
            r.Dbf = dbf.AsSpan(DbfRecordLen * (i - i0), DbfRecordLen).ToArray();
            o.Add(r);
        }
        return o;
    }

    public Record ReadOne(int i) => ReadBatch(i, 1)[0];

    Record ReadShape(long pos)
    {
        var r = new Record();
        if (_shp.Position != pos) _shp.Position = pos;
        var rh = new byte[8];
        _shp.ReadExactly(rh);
        int words = BinaryPrimitives.ReadInt32BigEndian(rh.AsSpan(4));
        var c = new byte[2L * words];
        _shp.ReadExactly(c);
        r.ShapeType = c.Length >= 4 ? BinaryPrimitives.ReadInt32LittleEndian(c) : 0;
        if (r.ShapeType != 5 && r.ShapeType != 15 && r.ShapeType != 25) return r;   // 빈 모양(0) 등 — 고리 없음
        int nParts = BinaryPrimitives.ReadInt32LittleEndian(c.AsSpan(36));
        int nPts = BinaryPrimitives.ReadInt32LittleEndian(c.AsSpan(40));
        int pBase = 44 + 4 * nParts;
        r.Points = nPts;
        for (int k = 0; k < nParts; k++)
        {
            int a = BinaryPrimitives.ReadInt32LittleEndian(c.AsSpan(44 + 4 * k));
            int b = k + 1 < nParts ? BinaryPrimitives.ReadInt32LittleEndian(c.AsSpan(44 + 4 * (k + 1))) : nPts;
            if (a < 0 || b > nPts || b <= a) continue;
            var ring = new double[2 * (b - a)];
            for (int q = a; q < b; q++)
            {
                ring[2 * (q - a)] = BitConverter.ToDouble(c, pBase + 16 * q);
                ring[2 * (q - a) + 1] = BitConverter.ToDouble(c, pBase + 16 * q + 8);
            }
            r.Rings.Add(ring);
        }
        return r;
    }

    public void Dispose()
    {
        _shp.Dispose(); _shx.Dispose(); _dbf.Dispose();
    }

    // ── 시험용 쓰기(하네스 — 가짜 좌표만) ──

    /// <summary>시험용 SHP · SHX · DBF 쓰기 — 다각형(형 5) · 칸은 모두 C(글) · cp949. <paramref name="rings"/>는 레코드마다 고리들(닫는 점 없이 줘도 닫아서 씀).</summary>
    public static void WriteTest(string shpPath, IReadOnlyList<IReadOnlyList<double[]>> rings, IReadOnlyList<(string Name, int Len)> fields, IReadOnlyList<string[]> values)
    {
        string bas = Path.Combine(Path.GetDirectoryName(shpPath) ?? "", Path.GetFileNameWithoutExtension(shpPath));
        var recs = new List<byte[]>();
        double bx0 = double.MaxValue, by0 = double.MaxValue, bx1 = double.MinValue, by1 = double.MinValue;
        for (int i = 0; i < rings.Count; i++)
        {
            var rr = rings[i].Select(r => r.Length >= 4 && (r[0] != r[^2] || r[1] != r[^1]) ? r.Concat(new[] { r[0], r[1] }).ToArray() : r).ToList();
            int nPts = rr.Sum(r => r.Length / 2);
            var c = new byte[44 + 4 * rr.Count + 16 * nPts];
            BinaryPrimitives.WriteInt32LittleEndian(c, 5);
            double x0 = double.MaxValue, y0 = double.MaxValue, x1 = double.MinValue, y1 = double.MinValue;
            int q = 0;
            for (int k = 0; k < rr.Count; k++)
            {
                BinaryPrimitives.WriteInt32LittleEndian(c.AsSpan(44 + 4 * k), q);
                for (int p = 0; p < rr[k].Length / 2; p++, q++)
                {
                    double x = rr[k][2 * p], y = rr[k][2 * p + 1];
                    BitConverter.GetBytes(x).CopyTo(c, 44 + 4 * rr.Count + 16 * q);
                    BitConverter.GetBytes(y).CopyTo(c, 44 + 4 * rr.Count + 16 * q + 8);
                    x0 = Math.Min(x0, x); y0 = Math.Min(y0, y); x1 = Math.Max(x1, x); y1 = Math.Max(y1, y);
                }
            }
            BitConverter.GetBytes(x0).CopyTo(c, 4); BitConverter.GetBytes(y0).CopyTo(c, 12); BitConverter.GetBytes(x1).CopyTo(c, 20); BitConverter.GetBytes(y1).CopyTo(c, 28);
            BinaryPrimitives.WriteInt32LittleEndian(c.AsSpan(36), rr.Count);
            BinaryPrimitives.WriteInt32LittleEndian(c.AsSpan(40), nPts);
            recs.Add(c);
            bx0 = Math.Min(bx0, x0); by0 = Math.Min(by0, y0); bx1 = Math.Max(bx1, x1); by1 = Math.Max(by1, y1);
        }
        byte[] Header(long totalBytes)
        {
            var h = new byte[100];
            BinaryPrimitives.WriteInt32BigEndian(h, 9994);
            BinaryPrimitives.WriteInt32BigEndian(h.AsSpan(24), (int)(totalBytes / 2));
            BinaryPrimitives.WriteInt32LittleEndian(h.AsSpan(28), 1000);
            BinaryPrimitives.WriteInt32LittleEndian(h.AsSpan(32), 5);
            BitConverter.GetBytes(bx0).CopyTo(h, 36); BitConverter.GetBytes(by0).CopyTo(h, 44); BitConverter.GetBytes(bx1).CopyTo(h, 52); BitConverter.GetBytes(by1).CopyTo(h, 60);
            return h;
        }
        long shpLen = 100 + recs.Sum(r => 8L + r.Length);
        using (var s = File.Create(bas + ".shp"))
        using (var x = File.Create(bas + ".shx"))
        {
            s.Write(Header(shpLen));
            x.Write(Header(100 + 8L * recs.Count));
            long pos = 100;
            for (int i = 0; i < recs.Count; i++)
            {
                var rh = new byte[8];
                BinaryPrimitives.WriteInt32BigEndian(rh, i + 1);
                BinaryPrimitives.WriteInt32BigEndian(rh.AsSpan(4), recs[i].Length / 2);
                s.Write(rh); s.Write(recs[i]);
                var xr = new byte[8];
                BinaryPrimitives.WriteInt32BigEndian(xr, (int)(pos / 2));
                BinaryPrimitives.WriteInt32BigEndian(xr.AsSpan(4), recs[i].Length / 2);
                x.Write(xr);
                pos += 8 + recs[i].Length;
            }
        }
        // DBF — 판 3 · LDID 0x4E(cp949)
        int recLen = 1 + fields.Sum(f => f.Len), hdrLen = 32 + 32 * fields.Count + 1;
        using (var d = File.Create(bas + ".dbf"))
        {
            var h = new byte[32];
            h[0] = 3; h[1] = 126; h[2] = 1; h[3] = 1;
            BinaryPrimitives.WriteUInt32LittleEndian(h.AsSpan(4), (uint)values.Count);
            BinaryPrimitives.WriteUInt16LittleEndian(h.AsSpan(8), (ushort)hdrLen);
            BinaryPrimitives.WriteUInt16LittleEndian(h.AsSpan(10), (ushort)recLen);
            h[29] = 0x4E;
            d.Write(h);
            foreach (var (name, len) in fields)
            {
                var f = new byte[32];
                var nb = Cp949.GetBytes(name);
                Array.Copy(nb, f, Math.Min(10, nb.Length));
                f[11] = (byte)'C'; f[16] = (byte)len;
                d.Write(f);
            }
            d.WriteByte(0x0D);
            foreach (var v in values)
            {
                var r = new byte[recLen];
                Array.Fill(r, (byte)' ');
                int o = 1;
                for (int k = 0; k < fields.Count; k++)
                {
                    var b = Cp949.GetBytes(k < v.Length ? v[k] ?? "" : "");
                    Array.Copy(b, 0, r, o, Math.Min(b.Length, fields[k].Len));
                    o += fields[k].Len;
                }
                d.Write(r);
            }
            d.WriteByte(0x1A);
        }
    }
}
