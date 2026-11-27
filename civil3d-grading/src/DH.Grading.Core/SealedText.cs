using System.Security.Cryptography;
using System.Text;

namespace DH.Grading.Core;

/// <summary>★[v117.44.1 · JACK 1127 «암호화해서 기본으로 넣고»] 설치 키 파일(dh.keys.json)에 넣는 값을 암호문으로 — <c>enc1:</c> + base64(nonce 12 · 암호문 · 태그 16) · AES-256-GCM.
/// <para>용도 이름(예: "kakao")을 덧붙인 자료(AAD)로 묶어 다른 칸으로 옮기면 안 풀린다 · 한 글자라도 바뀌면 안 풀린다(null).</para>
/// <para>★한계: 푸는 열쇠가 DLL 안에 있다 — 파일을 열어 봐도 값이 바로 보이지 않게 하는 것까지. 카카오 JavaScript 키의 진짜 잠금은 카카오에 등록한 도메인이다.</para>
/// <para>만드는 쪽: tools/setup/sealkey.py (같은 열쇠 · 같은 꼴).</para></summary>
public static class SealedText
{
    public const string Prefix = "enc1:";
    const int NonceLen = 12, TagLen = 16;

    // sealkey.py 와 같은 글 — 바꾸면 이미 만든 설치 묶음의 값이 안 풀린다
    static readonly byte[] Key = SHA256.HashData(Encoding.UTF8.GetBytes("DH.Grading.SealedText.v1/builtin"));

    public static bool IsSealed(string? s) => s != null && s.StartsWith(Prefix, StringComparison.Ordinal);

    public static string Seal(string plain, string purpose)
    {
        byte[] p = Encoding.UTF8.GetBytes(plain ?? "");
        byte[] nonce = RandomNumberGenerator.GetBytes(NonceLen);
        byte[] ct = new byte[p.Length], tag = new byte[TagLen];
        using (var g = new AesGcm(Key, TagLen))
            g.Encrypt(nonce, p, ct, tag, Encoding.UTF8.GetBytes(purpose ?? ""));
        byte[] all = new byte[NonceLen + ct.Length + TagLen];
        Buffer.BlockCopy(nonce, 0, all, 0, NonceLen);
        Buffer.BlockCopy(ct, 0, all, NonceLen, ct.Length);
        Buffer.BlockCopy(tag, 0, all, NonceLen + ct.Length, TagLen);
        return Prefix + Convert.ToBase64String(all);
    }

    /// <summary>풀기 — 암호문 꼴이 아니거나 · 깨졌거나 · 용도가 다르면 null.</summary>
    public static string? Open(string? sealedText, string purpose)
    {
        if (!IsSealed(sealedText)) return null;
        try
        {
            byte[] all = Convert.FromBase64String(sealedText!.Substring(Prefix.Length).Trim());
            if (all.Length < NonceLen + TagLen) return null;
            int n = all.Length - NonceLen - TagLen;
            byte[] p = new byte[n];
            using (var g = new AesGcm(Key, TagLen))
                g.Decrypt(all.AsSpan(0, NonceLen), all.AsSpan(NonceLen, n), all.AsSpan(NonceLen + n, TagLen), p, Encoding.UTF8.GetBytes(purpose ?? ""));
            return Encoding.UTF8.GetString(p);
        }
        catch (FormatException) { return null; }
        catch (CryptographicException) { return null; }
    }
}
