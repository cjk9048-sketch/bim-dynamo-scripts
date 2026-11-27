using Xunit;

namespace DH.Grading.Core.Tests;

/// <summary>★[v117.44.1] 설치 키 파일 암호문(enc1:) — tools/setup/sealkey.py 와 같은 꼴인가 · 깨지면 null.</summary>
public class SealedTextTests
{
    const string Plain = "0123456789abcdef0123456789abcdef";   // 가짜 값(시험 전용)
    // sealkey.seal(Plain, "kakao") 로 만든 것 — 파이썬이 만든 암호문을 C#이 푼다
    const string FromPython = "enc1:DM2+0MWxW9WlwwD2fRDpqCfGZj24c6Dk4WpcTAAyTCnxCofqbCZxXtpsSo5oznCEOzIyOOCdh3Qp7R/O";

    [Fact]
    public void OpensPythonSealed() => Assert.Equal(Plain, SealedText.Open(FromPython, "kakao"));

    [Fact]
    public void RoundTrip()
    {
        string s = SealedText.Seal(Plain, "kakao");
        Assert.StartsWith(SealedText.Prefix, s);
        Assert.DoesNotContain(Plain, s);
        Assert.Equal(Plain, SealedText.Open(s, "kakao"));
        Assert.NotEqual(s, SealedText.Seal(Plain, "kakao"));   // 매번 다른 nonce
    }

    [Fact]
    public void WrongPurposeIsNull() => Assert.Null(SealedText.Open(FromPython, "vworld"));

    [Fact]
    public void TamperedIsNull()
    {
        char[] c = FromPython.ToCharArray();
        int i = SealedText.Prefix.Length + 20;
        c[i] = c[i] == 'A' ? 'B' : 'A';
        Assert.Null(SealedText.Open(new string(c), "kakao"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(Plain)]
    [InlineData("enc1:")]
    [InlineData("enc1:@@@")]
    [InlineData("enc1:AAAA")]
    public void NotSealedOrBrokenIsNull(string? s) => Assert.Null(SealedText.Open(s, "kakao"));
}
