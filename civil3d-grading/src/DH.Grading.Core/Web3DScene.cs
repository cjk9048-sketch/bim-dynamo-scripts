using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace DH.Grading.Core;

/// <summary>★★★[v107 · 계획_v107.md 2 · JACK 1027 «3D 웹뷰»] <b>[3D 보기] 장면 묶음</b> — 머리(JSON) + 덩어리(점 · 면 · 그림)를 한 바이트열로 · gzip · base64로 페이지 하나에 넣는다.
/// <para>꼴(뷰어 <c>web3d/src/data.js</c>와 짝): "DH3D" · u32 판(1) · u32 머리 길이 · 머리 UTF-8 · 4바이트 맞춤 = 덩어리 시작 ·
/// 덩어리마다 4바이트 맞춤. 머리 안 덩어리 표시 <c>{o: 덩어리 시작부터 · n: 바이트 · t: f32|u32|u8|img · m: 그림 꼴}</c>.</para>
/// <para>머리 숫자는 모두 유한해야 한다(JSON에 NaN 없음 — <see cref="Num"/>). 점은 원점을 뺀 float32(현장 2 km 안에서 0.1 mm 아래).</para></summary>
public sealed class Web3DScene
{
    public const int Version = 1;
    public readonly Dictionary<string, object?> Head = new();
    readonly List<byte[]> _blobs = new();
    long _len;

    Dictionary<string, object> Ref(byte[] b, string t, string? mime = null)
    {
        var r = new Dictionary<string, object> { ["o"] = _len, ["n"] = b.Length, ["t"] = t };
        if (mime != null) r["m"] = mime;
        _blobs.Add(b);
        _len += (b.Length + 3) & ~3;
        return r;
    }

    public object F32(float[] a) => Ref(MemoryMarshal.AsBytes(a.AsSpan()).ToArray(), "f32");
    public object U32(int[] a) => Ref(MemoryMarshal.AsBytes(a.AsSpan()).ToArray(), "u32");
    public object U8(byte[] a) => Ref(a, "u8");
    public object Img(byte[] data, string mime) => Ref(data, "img", mime);

    /// <summary>JSON에 넣을 수 — NaN · 무한은 0(머리에 NaN이 들어가면 JSON이 깨진다).</summary>
    public static double Num(double v, int digits = 4) => double.IsFinite(v) ? Math.Round(v, digits) : 0;

    public long BlobBytes => _len;

    /// <summary>묶음(풀린 바이트열).</summary>
    public byte[] Pack()
    {
        var head = JsonSerializer.SerializeToUtf8Bytes(Head, new JsonSerializerOptions { WriteIndented = false });
        int baseOff = (12 + head.Length + 3) & ~3;
        var buf = new byte[baseOff + _len];
        Encoding.ASCII.GetBytes("DH3D").CopyTo(buf, 0);
        BitConverter.GetBytes((uint)Version).CopyTo(buf, 4);
        BitConverter.GetBytes((uint)head.Length).CopyTo(buf, 8);
        head.CopyTo(buf, 12);
        long o = baseOff;
        foreach (var b in _blobs) { Buffer.BlockCopy(b, 0, buf, (int)o, b.Length); o += (b.Length + 3) & ~3; }
        return buf;
    }

    public byte[] PackGzip(CompressionLevel level = CompressionLevel.Fastest)
    {
        var raw = Pack();
        using var ms = new MemoryStream();
        using (var gz = new GZipStream(ms, level, leaveOpen: true)) gz.Write(raw, 0, raw.Length);
        return ms.ToArray();
    }

    /// <summary>되읽기(시험) — 머리 JSON · 풀린 바이트열 · 덩어리 시작.</summary>
    public static (JsonDocument Head, byte[] Buf, int Base) Unpack(byte[] gz)
    {
        using var ms = new MemoryStream(gz);
        using var z = new GZipStream(ms, CompressionMode.Decompress);
        using var o = new MemoryStream();
        z.CopyTo(o);
        var buf = o.ToArray();
        if (Encoding.ASCII.GetString(buf, 0, 4) != "DH3D") throw new InvalidDataException("DH3D 아님");
        if (BitConverter.ToUInt32(buf, 4) != Version) throw new InvalidDataException("판 다름");
        int hl = (int)BitConverter.ToUInt32(buf, 8);
        var doc = JsonDocument.Parse(new ReadOnlyMemory<byte>(buf, 12, hl));
        return (doc, buf, (12 + hl + 3) & ~3);
    }

    /// <summary>되읽기(시험) — 덩어리 표시 하나 → float 배열.</summary>
    public static float[] ReadF32(JsonElement r, byte[] buf, int baseOff)
    {
        int o = baseOff + r.GetProperty("o").GetInt32(), n = r.GetProperty("n").GetInt32();
        return MemoryMarshal.Cast<byte, float>(buf.AsSpan(o, n)).ToArray();
    }

    public static int[] ReadU32(JsonElement r, byte[] buf, int baseOff)
    {
        int o = baseOff + r.GetProperty("o").GetInt32(), n = r.GetProperty("n").GetInt32();
        return MemoryMarshal.Cast<byte, int>(buf.AsSpan(o, n)).ToArray();
    }

    /// <summary>페이지 쓰기 — 틀의 자리(<c>__TITLE__</c> · <c>__DATA__</c> · <c>/*__VIEWER__*/</c>)를 <b>차례로 한 번씩</b> 채운다(Replace 사슬 안 씀 — 뷰어 · 자료 안의 같은 글자를 건드리지 않게).
    /// 자료는 base64를 조각으로 흘려 쓴다(큰 문자열 하나를 안 만든다).</summary>
    public static void WritePage(TextWriter w, string template, string viewerJs, string title, byte[] gz)
    {
        const string T = "__TITLE__", D = "__DATA__", V = "/*__VIEWER__*/";
        int iT = template.IndexOf(T, StringComparison.Ordinal);
        int iD = template.IndexOf(D, StringComparison.Ordinal);
        int iV = template.IndexOf(V, StringComparison.Ordinal);
        if (iT < 0 || iD < iT || iV < iD) throw new InvalidDataException("페이지 틀 자리가 없거나 차례가 다름");
        if (viewerJs.Contains("</script", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("뷰어에 </script 글자가 있음");
        w.Write(template.AsSpan(0, iT));
        w.Write(System.Net.WebUtility.HtmlEncode(title));
        w.Write(template.AsSpan(iT + T.Length, iD - iT - T.Length));
        const int Chunk = 3 * 1024 * 1024;                                      // 3의 배수 — 조각 base64를 이어 붙여도 같다
        for (int o = 0; o < gz.Length; o += Chunk)
            w.Write(Convert.ToBase64String(gz, o, Math.Min(Chunk, gz.Length - o)));
        w.Write(template.AsSpan(iD + D.Length, iV - iD - D.Length));
        w.Write(viewerJs);
        w.Write(template.AsSpan(iV + V.Length));
    }
}
