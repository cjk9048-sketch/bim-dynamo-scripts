namespace DH.Grading.Civil;

/// <summary>★[1125 · Civil 3D 2027] 번들 공유 자료의 뿌리(<c>Contents\</c>).
/// <para>2026 DLL은 <c>Contents\</c> 바로 밑, 2027 DLL은 <c>Contents\2027\</c>에 있다. 공유 자료(coordsys · Tutorial · Frames · RevitAddin ·
/// dh.keys.json · share3d.default.json · UDitch.settings.json)는 <c>Contents\</c>에 한 벌만 둔다 —
/// 그래서 DLL 폴더가 «Contents 밑 판 폴더(숫자 네 자리)»면 한 칸 위를 쓴다. 그 밖(개발 bin · 키트 build\…\bin)은 지금처럼 DLL 폴더.</para></summary>
internal static class BundleContents
{
    internal static string Root { get; } = Find();

    static string Find()
    {
        try
        {
            string dir = System.IO.Path.GetDirectoryName(typeof(BundleContents).Assembly.Location) ?? "";
            string? parent = System.IO.Path.GetDirectoryName(dir);
            string name = System.IO.Path.GetFileName(dir);
            if (parent != null && name.Length == 4 && name.All(char.IsAsciiDigit)
                && string.Equals(System.IO.Path.GetFileName(parent), "Contents", System.StringComparison.OrdinalIgnoreCase))
                return parent;
            return dir;
        }
        catch { return ""; }
    }
}
