using System.Security.Cryptography;
using System.Text;

namespace DH.CivilCoord.Core;

/// <summary>IFC GlobalId(22자 · buildingSMART 64진) ↔ Guid.</summary>
public static class IfcGuid
{
    private const string Digits = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz_$";

    /// <summary>Guid → 22자. 128비트를 큰 자리부터: 첫 글자 = 맨 위 2비트 · 나머지 21자 = 6비트씩.</summary>
    public static string Compress(Guid g)
    {
        UInt128 v = ToUInt128(g);
        var c = new char[22];
        c[0] = Digits[(int)(v >> 126)];
        for (int i = 1; i < 22; i++) c[i] = Digits[(int)((v >> (126 - 6 * i)) & 63)];
        return new string(c);
    }

    /// <summary>22자 → Guid(시험용 · 잘못된 글이면 예외).</summary>
    public static Guid Expand(string s)
    {
        if (s.Length != 22) throw new FormatException("IFC GUID는 22자");
        UInt128 v = 0;
        for (int i = 0; i < 22; i++)
        {
            int d = Digits.IndexOf(s[i]);
            if (d < 0 || (i == 0 && d > 3)) throw new FormatException("IFC GUID 글자 아님: " + s[i]);
            v = (v << 6) | (UInt128)(uint)d;
        }
        var be = new byte[16];
        for (int i = 15; i >= 0; i--) { be[i] = (byte)(v & 0xFF); v >>= 8; }
        return FromBigEndian(be);
    }

    /// <summary>글에서 늘 같은 Guid(MD5 · 판 3) — 대지 · 건물 · 관계처럼 Revit 요소가 없는 것.</summary>
    public static Guid FromName(string name)
    {
        byte[] h = MD5.HashData(Encoding.UTF8.GetBytes(name));
        h[6] = (byte)((h[6] & 0x0F) | 0x30);
        h[8] = (byte)((h[8] & 0x3F) | 0x80);
        return FromBigEndian(h);
    }

    private static UInt128 ToUInt128(Guid g)
    {
        byte[] b = g.ToByteArray();   // 앞 세 칸(4 · 2 · 2바이트)은 작은 자리부터
        byte[] be = { b[3], b[2], b[1], b[0], b[5], b[4], b[7], b[6], b[8], b[9], b[10], b[11], b[12], b[13], b[14], b[15] };
        UInt128 v = 0;
        foreach (byte x in be) v = (v << 8) | x;
        return v;
    }

    private static Guid FromBigEndian(byte[] be)
    {
        byte[] b = { be[3], be[2], be[1], be[0], be[5], be[4], be[7], be[6], be[8], be[9], be[10], be[11], be[12], be[13], be[14], be[15] };
        return new Guid(b);
    }
}
