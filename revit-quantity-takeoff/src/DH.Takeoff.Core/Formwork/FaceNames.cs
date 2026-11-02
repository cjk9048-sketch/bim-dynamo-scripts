namespace DH.Takeoff.Core.Formwork;

/// <summary>
/// 면의 쉬운 이름(R73a — 계획 §3.5 · 한 곳에서만 짓는다): 위치 나무 · 마우스 밑 이름표 · 3D 면 위 딱지가 같은 이름을 쓴다.
///  · 벽 · 보 · 그 밖의 옆면: 넓은 쪽 = «바깥 면» · «안쪽 면(수조부)»(안쪽이면 구역 이름 · 개구부 · 안팎 확인), 짧은 쪽 = «끝면».
///    넓은 쪽 = 그 부재의 가장 넓은 옆면과 나란한 면(평면에서 45° 안). 거의 네모난 부재(기둥 · 넓은 옆면과 꺾인 옆면이 85 % 넘게 비슷)는 방위 «북쪽 옆면»(지도와 같은 북 = +Y).
///  · 슬래브 · 기초의 옆면 = «옆면(가장자리)»(안쪽이면 «안쪽 옆면(…)») · 밑면 = «밑면» · 경사면 = «경사면» · 구멍 둘레 = «구멍 둘레»(벽 · 기둥 · 보는 «개구부 둘레») ·
///    곡면 = «곡면» · 윗면 = «윗면» · Revit 계단 = «계단 밑면 · 옆면 · 경사면».
///  · 한 부재에 같은 이름이 둘 넘으면 «끝면 1 · 끝면 2»(면 차례).
/// 이름은 보기만 한다 — 기록 · 엑셀 줄에는 쓰지 않는다.
/// </summary>
public static class FaceNames
{
    /// <summary>나무에서 면 줄의 차례(이름의 머리말 → 차례 — 넓은 옆면 먼저 · 밑면 · 경사 · 둘레 · 곡면 · 윗면).</summary>
    public static int Order(string name) =>
        name.StartsWith("바깥", StringComparison.Ordinal) ? 0
        : name.StartsWith("안쪽", StringComparison.Ordinal) ? 1
        : name.Contains("쪽 옆면", StringComparison.Ordinal) ? 2
        : name.StartsWith("옆면", StringComparison.Ordinal) || name.StartsWith("계단 옆면", StringComparison.Ordinal) ? 3
        : name.StartsWith("끝면", StringComparison.Ordinal) ? 4
        : name.StartsWith("개구부 옆면", StringComparison.Ordinal) ? 5
        : name.Contains("밑면", StringComparison.Ordinal) ? 6
        : name.Contains("경사면", StringComparison.Ordinal) ? 7
        : name.Contains("둘레", StringComparison.Ordinal) ? 8
        : name.StartsWith("곡면", StringComparison.Ordinal) ? 9
        : 10;

    /// <summary>한 부재의 면들에 이름을 붙인다(EnsureFaces 가 부재마다 부른다).</summary>
    internal static void Assign(ElementFormwork result, IReadOnlyList<BoardFace> faces)
    {
        if (faces.Count == 0) return;
        var el = result.Element;
        // 넓은 쪽: 가장 넓은 옆면의 평면 법선 · 거의 네모(꺾인 쪽 가장 넓은 옆면이 85 % 넘음)면 방위로.
        (double x, double y)? wide = null;
        double wideArea = 0, crossArea = 0;
        foreach (var f in result.Faces)
            if (f.Class == FaceClass.Side && Flat(f.Normal) is { } h && f.Polygon > wideArea) { wideArea = f.Polygon; wide = h; }
        if (wide is { } w)
            foreach (var f in result.Faces)
                if (f.Class == FaceClass.Side && Flat(f.Normal) is { } h && Math.Abs(h.x * w.x + h.y * w.y) < 0.7071) crossArea = Math.Max(crossArea, f.Polygon);
        bool compass = el.Kind == MemberKind.Column || (wide != null && crossArea > 0 && crossArea >= 0.85 * wideArea);
        var names = faces.Select(f => Base(f, el, wide, compass)).ToList();
        // 같은 이름이 둘 넘으면 차례 번호(면 차례 — 이웃 판을 칠해도 바뀌지 않는 형상 차례).
        foreach (var g in names.Select((n, i) => (n, i)).GroupBy(x => x.n, StringComparer.Ordinal).Where(g => g.Count() > 1))
        {
            int k = 1;
            foreach (var (_, i) in g) names[i] = $"{g.Key} {k++}";
        }
        for (int i = 0; i < faces.Count; i++) faces[i].Name = names[i];
    }

    // 면 하나의 이름(번호 붙이기 전).
    private static string Base(BoardFace face, FormworkElement el, (double x, double y)? wide, bool compass)
    {
        var f = face.Face;
        string side = face.Lead.Side;
        bool stair = el.Stair || el.Kind == MemberKind.Stair;
        switch (f.Class)
        {
            case FaceClass.Top: return stair ? "계단 윗면" : "윗면";
            case FaceClass.Bottom: return stair ? "계단 밑면" : "밑면";
            case FaceClass.SlopeUp or FaceClass.SlopeDown: return stair ? "계단 경사면" : "경사면";
            case FaceClass.Curved: return "곡면";
            case FaceClass.HoleEdge: return el.Kind is MemberKind.Wall or MemberKind.Column or MemberKind.Beam ? "개구부 둘레" : "구멍 둘레";
        }
        if (stair) return "계단 옆면";
        if (side == FormworkAnalyzer.Opening) return "개구부 옆면";
        if (el.Kind is MemberKind.Slab or MemberKind.Foundation)
            return side.StartsWith("내측", StringComparison.Ordinal) ? "안쪽 옆면" + Zone(side) : "옆면(가장자리)";
        var h = Flat(f.Normal);
        if (compass || h == null || wide == null) return h is { } d ? Compass(d) + " 옆면" : "옆면";
        bool along = Math.Abs(h.Value.x * wide.Value.x + h.Value.y * wide.Value.y) >= 0.7071;
        if (!along) return "끝면";
        return side switch
        {
            "외측" => "바깥 면",
            "확인 필요" => "옆면(안팎 확인)",
            _ when side.StartsWith("내측", StringComparison.Ordinal) => "안쪽 면" + Zone(side),
            _ => "옆면",
        };
    }

    // «내측(수조부)» → «(수조부)» · «내측» → "".
    private static string Zone(string side) => side.Length > 2 && side[2] == '(' ? side[2..] : "";

    // 평면 법선(수평 성분이 반 넘는 면만 · 단위).
    private static (double x, double y)? Flat(double[]? n)
    {
        if (n is not { Length: 3 }) return null;
        double len = Math.Sqrt(n[0] * n[0] + n[1] * n[1]);
        return len > 0.5 ? (n[0] / len, n[1] / len) : null;
    }

    /// <summary>평면 법선의 방위(북 = +Y · 8 방위).</summary>
    public static string Compass((double x, double y) d)
    {
        double deg = Math.Atan2(d.x, d.y) * 180 / Math.PI;   // 북에서 시계 방향
        if (deg < 0) deg += 360;
        string[] names = { "북쪽", "북동쪽", "동쪽", "남동쪽", "남쪽", "남서쪽", "서쪽", "북서쪽" };
        return names[(int)Math.Round(deg / 45) % 8];
    }
}
