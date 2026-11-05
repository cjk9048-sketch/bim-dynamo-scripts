namespace DH.Grading.Core;

/// <summary>★★[v115 · JACK 1105 Revit 구조물 · 계획 4절] 불러온 구조물 한 칸 — NOD <c>DH_GRADING/STRUCTS</c>.
/// 원본은 xref(덧씌우기)로 붙어 있고 형상은 그 xref에서 다시 읽는다 — 여기는 원본 경로 · 바뀜 검사 값 · 계수 · 계측만.</summary>
public sealed class StructRec
{
    public int Id;
    public string Name = "";
    /// <summary>도면 기준 상대경로(도면을 저장하기 전이면 빈 값) · 절대경로.</summary>
    public string PathRel = "", PathAbs = "";
    public double Size;
    /// <summary>원본 파일 고친 시각(UTC · 왕복 형식) · SHA-256 앞 16자.</summary>
    public string Mtime = "", Sha16 = "";
    /// <summary>단위 계수(D7) — xref 삽입 축척으로 직접 들어간다.</summary>
    public double Factor = 1;
    /// <summary>xref 삽입(BlockReference) 핸들.</summary>
    public string XrefHandle = "";
    public int Parts, Tris, OpenParts;
}

/// <summary>목록 머리 — 다음 번호 · 부지 횡단 축척(다시 반영이 해치 간격 · 글자 높이에 쓴다 · 0 = 모름).</summary>
public sealed class StructHead
{
    public int NextId = 1;
    public double SiteXsecScale;
}

public static class StructList
{
    public const string Sign = "DH_STRUCTS";
    public const int Version = 1;

    /// <summary>(종류, 값) — 'S' 글 · 'I' 정수 · 'D' 실수(<see cref="WallList"/>와 같은 꼴).</summary>
    public static List<(char Kind, object Value)> Encode(StructHead head, IReadOnlyList<StructRec> recs)
    {
        var v = new List<(char, object)> { ('S', Sign), ('I', Version), ('I', head.NextId), ('D', head.SiteXsecScale), ('I', recs.Count) };
        foreach (var r in recs)
        {
            v.Add(('I', r.Id)); v.Add(('S', r.Name ?? "")); v.Add(('S', r.PathRel ?? "")); v.Add(('S', r.PathAbs ?? ""));
            v.Add(('D', r.Size)); v.Add(('S', r.Mtime ?? "")); v.Add(('S', r.Sha16 ?? "")); v.Add(('D', r.Factor));
            v.Add(('S', r.XrefHandle ?? "")); v.Add(('I', r.Parts)); v.Add(('I', r.Tris)); v.Add(('I', r.OpenParts));
        }
        return v;
    }

    public static bool TryDecode(IReadOnlyList<(char Kind, object Value)> v, out StructHead head, out List<StructRec> recs, out string why)
    {
        head = new StructHead(); recs = new List<StructRec>(); why = "";
        int k = 0;
        try
        {
            string S() { var x = v[k++]; if (x.Kind != 'S') throw new FormatException($"{k}번째 칸이 글이 아니다"); return (string)x.Value; }
            int I() { var x = v[k++]; if (x.Kind != 'I') throw new FormatException($"{k}번째 칸이 정수가 아니다"); return Convert.ToInt32(x.Value); }
            double D() { var x = v[k++]; if (x.Kind != 'D') throw new FormatException($"{k}번째 칸이 실수가 아니다"); return Convert.ToDouble(x.Value); }
            if (v.Count == 0) return true;
            if (S() != Sign) { why = "구조물 목록 서명이 다르다"; return false; }
            int ver = I();
            if (ver < 1 || ver > Version) { why = $"구조물 목록 판 {ver}(이 애드인은 {Version}) — 더 새 판이 쓴 목록"; return false; }
            head.NextId = I(); head.SiteXsecScale = D();
            int n = I();
            for (int i = 0; i < n; i++)
            {
                var r = new StructRec { Id = I(), Name = S(), PathRel = S(), PathAbs = S(), Size = D(), Mtime = S(), Sha16 = S(), Factor = D(), XrefHandle = S(), Parts = I(), Tris = I(), OpenParts = I() };
                recs.Add(r);
            }
            if (k != v.Count) { why = $"구조물 목록 끝에 남는 칸 {v.Count - k}"; return false; }
            return true;
        }
        catch (Exception ex) { why = "구조물 목록을 못 읽었다 — " + ex.Message; recs.Clear(); return false; }
    }
}
