namespace DH.Takeoff.Revit;

/// <summary>
/// 위치 색 — 3D 팝업과 확인용 Revit 뷰가 같은 색을 쓴다(위치 목록의 차례대로). Revit API 를 쓰지 않는다.
/// JACK 2026-10-13: 회색 = 아직 · 위치별 색 = 끝 · 하나만 넣은 부재는 옅은 색.
/// </summary>
public static class ZonePalette
{
    public static readonly (string name, byte r, byte g, byte b)[] Zones =
    {
        ("파랑", 66, 133, 244), ("초록", 52, 168, 83), ("주황", 251, 140, 0), ("보라", 142, 68, 173),
        ("청록", 0, 172, 193), ("노랑", 241, 196, 15), ("갈색", 141, 110, 99), ("연두", 139, 195, 74),
        ("남색", 57, 73, 171), ("살구", 255, 171, 145), ("올리브", 130, 119, 23), ("하늘", 129, 212, 250),
    };

    /// <summary>위치가 없는 부재(확인용 Revit 뷰에서 쓴다).</summary>
    public static readonly (string name, byte r, byte g, byte b) None = ("회색", 158, 158, 158);

    /// <summary>아직 아무 값도 없는 부재(3D 팝업) — 콘크리트 같은 옅은 회색.</summary>
    public static readonly (string name, byte r, byte g, byte b) Todo = ("옅은 회색", 208, 208, 208);

    /// <summary>부위만 넣은 부재(3D 팝업) — 위치가 없어 위치 색을 쓸 수 없으니 크림색.</summary>
    public static readonly (string name, byte r, byte g, byte b) PartOnly = ("크림색", 240, 226, 192);

    /// <summary>'위치 없음'으로 정한 부재(3D 팝업) — 위치 색에 없는 자주색.</summary>
    public static readonly (string name, byte r, byte g, byte b) NoPlace = ("자주", 156, 39, 106);

    /// <summary>고칠 수 없는 부재(3D 팝업) — 짙은 회색.</summary>
    public static readonly (string name, byte r, byte g, byte b) Locked = ("짙은 회색", 112, 116, 122);

    /// <summary>
    /// 고른 부재의 테두리(3D 팝업) — 위치 색에 없는 분홍. 처음에는 파랑으로 덮었는데 첫째 위치의 파랑과 가려 볼 수 없었다
    /// (2026-10-13 그림으로 확인) — 그래서 색을 덮지 않고 흰 막을 씌우고 굵은 분홍 테두리를 두른다.
    /// </summary>
    public static readonly (string name, byte r, byte g, byte b) Picked = ("분홍", 236, 0, 140);

    public static (string name, byte r, byte g, byte b) Of(int index) => Zones[((index % Zones.Length) + Zones.Length) % Zones.Length];

    /// <summary>옅게 만든 색(흰색을 섞는다) — 덜 끝난 부재.</summary>
    public static (byte r, byte g, byte b) Pale((string name, byte r, byte g, byte b) c, double white = 0.58) =>
        ((byte)Math.Round(c.r + (255 - c.r) * white), (byte)Math.Round(c.g + (255 - c.g) * white), (byte)Math.Round(c.b + (255 - c.b) * white));
}
