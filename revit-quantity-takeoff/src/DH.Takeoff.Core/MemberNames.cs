using System.Globalization;

namespace DH.Takeoff.Core;

/// <summary>
/// 부재 이름(R53b — JACK 2026-10-19 «수량산출 매개변수를 넣을 때 애드인이 자동으로 이름을 매기고(슬래브 SB01~ · 벽체 W01~) 산출서에도 그 이름을» ·
/// 2026-10-20 «벽은 WA01»). 인터뷰(1019): 부재마다 하나 · 앞 글자 = 부위 묶음 · 한 번 붙인 이름은 그대로(새 부재만 다음 번호 · 지운 번호는 비워 둠 ·
/// 사람이 고친 이름도 지킴).
///  · 앞 글자: 채움 · 기초 → F · 바닥 · 위 슬래브 → SB · 벽 → WA · 헌치 → H · 기둥 C / 보 B · 계단 → ST · 턱 · 받침 · 그 밖 → E.
///  · 번호: 그 앞 글자를 쓰는 모델 안 이름의 가장 큰 번호 + 1 — 두 자리(SB01), 100 부터 세 자리. 한 번에 여럿이면 앞 글자마다
///    위치 차례 → 높이(아래부터) → x → y.
///  · 지키는 이름: 이 부재의 기록(이름 주인 — 기록의 Uid 가 이 부재)이 있는 이름 · 기록 없이 사람이 적은 이름. 같은 이름이 둘 이상이면
///    기록과 같은 이름의 주인 → 이름을 고친 주인 → 기록 없는 부재 → 복사한 뒤 고친 이름 → 복사본, 같은 자리끼리는 작은 Id 가 갖고 나머지는 새 번호.
///  · 복사본(기록의 주인이 다른 부재 — Revit 은 기록도 함께 복사한다): 기록의 이름 그대로면 원래 부재가 그 이름을 쓰는 동안 새 번호
///    (겹침 검사가 나눈 조각도 이것 — 원래 부재는 제 기록이라 이름을 지킨다). 원래 부재가 지워져 아무도 안 쓰면 그 이름을 이어받는다.
///    복사한 뒤 사람이 고친 이름이면 지킨다.
///  · 프로그램이 붙인 이름(Auto)인데 부위를 바꿔 앞 글자가 맞지 않으면(WA05 를 헌치로) 새 앞 글자의 다음 번호 — 사람이 적은 이름은 그대로(내 판단:
///    산출서의 헌치 제목 아래 WA05 가 나오지 않게).
/// 모델은 모른다(Revit 쪽이 읽어 넘기고 결정대로 적는다 — MemberNamer).
/// </summary>
public static class MemberNames
{
    /// <summary>부재 속성 칸 이름(공유 매개변수 — 글).</summary>
    public const string Param = "DH_부재명";

    /// <summary>부위 묶음 · 부위 이름 · 부재 종류 → 앞 글자.</summary>
    public static string PrefixOf(string group, string part, MemberKind kind) => group switch
    {
        PartCatalog.Fill => "F",
        PartCatalog.Floor => part.Contains("기초", StringComparison.Ordinal) ? "F" : "SB",
        PartCatalog.Upper => "SB",
        PartCatalog.Wall => "WA",
        PartCatalog.Haunch => "H",
        PartCatalog.Frame => kind == MemberKind.Column || (kind != MemberKind.Beam && part.Contains("기둥", StringComparison.Ordinal)) ? "C" : "B",
        PartCatalog.Stair => "ST",
        _ => "E",
    };

    /// <summary>앞 글자(영문) + 번호(숫자)로 된 이름이면 둘로 가른다(«SB05» → SB, 5). 앞 글자는 대문자로.</summary>
    public static bool TryParse(string? name, out string prefix, out int number)
    {
        prefix = "";
        number = 0;
        string text = (name ?? "").Trim();
        int i = 0;
        while (i < text.Length && char.IsAsciiLetter(text[i])) i++;
        if (i == 0 || i == text.Length || text.Length - i > 6) return false;
        for (int j = i; j < text.Length; j++) if (!char.IsAsciiDigit(text[j])) return false;
        prefix = text[..i].ToUpperInvariant();
        number = int.Parse(text[i..], NumberStyles.None, CultureInfo.InvariantCulture);
        return true;
    }

    /// <summary>이름 글: 두 자리(SB01) · 100 부터 세 자리.</summary>
    public static string Format(string prefix, int number) => prefix + number.ToString("00", CultureInfo.InvariantCulture);

    /// <summary>
    /// 산출서 줄의 이름 글(R53b 계획 — «W03, W04» · 넷 이상 이어지면 «W03~W06»): 앞 글자가 같고 번호가 하나씩 이어지는 이름은 넷부터 «처음~끝».
    /// 덩이가 maxPieces 보다 많거나 글 폭(한글 2 · 그 밖 1)이 maxWidth 를 넘으면 앞의 덩이만 적고 «외 n개»(n = 적지 않은 이름 수 — 줄의 부호 칸이
    /// 괄호 · «:» 와 겹치지 않게(core-verify R53b L1) · 이름은 «부재 목록»에 다 있다). 덩이 하나는 늘 적는다.
    /// </summary>
    public static string Compact(IEnumerable<string> names, int maxPieces = 6, int maxWidth = 32)
    {
        var list = names.Select(n => n.Trim()).Where(n => n.Length > 0).Distinct(StringComparer.Ordinal).ToList();
        list.Sort(Formwork.FormworkSheet.CompareMarks);
        var pieces = new List<(string text, int count)>();
        for (int i = 0; i < list.Count;)
        {
            int j = i;
            if (TryParse(list[i], out string p, out int n) && Format(p, n) == list[i])
                while (j + 1 < list.Count && TryParse(list[j + 1], out string q, out int m) && q == p && m == n + (j + 1 - i) && Format(q, m) == list[j + 1]) j++;
            int run = j - i + 1;
            if (run >= 4) pieces.Add(($"{list[i]}~{list[j]}", run));
            else for (int k = i; k <= j; k++) pieces.Add((list[k], 1));
            i = j + 1;
        }
        static int Width(string text) => text.Sum(c => c > 0x2E80 ? 2 : 1);
        string all = string.Join(", ", pieces.Select(x => x.text));
        if (pieces.Count <= 1 || (pieces.Count <= maxPieces && Width(all) <= maxWidth)) return all;
        for (int k = Math.Min(pieces.Count - 1, maxPieces - 1); k > 1; k--)
        {
            string text = string.Join(", ", pieces.Take(k).Select(x => x.text)) + $" 외 {pieces.Skip(k).Sum(x => x.count)}개";
            if (Width(text) <= maxWidth) return text;
        }
        return pieces[0].text + $" 외 {pieces.Skip(1).Sum(x => x.count)}개";
    }

    /// <summary>부재에 붙은 이름 기록이 누구의 것인가: 없음 · 이 부재 · 다른 부재(복사본).</summary>
    public enum Stamp { None, Own, Foreign }

    /// <summary>이름을 정할 부재 하나 — Revit 에서 읽은 그대로.</summary>
    public sealed class Member
    {
        public long Id { get; init; }
        public string Name { get; init; } = "";          // DH_부재명 의 지금 값
        public Stamp Stamp { get; init; }
        public string StampName { get; init; } = "";     // 기록의 이름(마지막으로 정한 이름)
        public bool StampAuto { get; init; }             // 기록: 프로그램이 붙인 이름
        public string Origin { get; init; } = "";        // 복사본이면 원래 부재의 UniqueId(나눈 조각 — R53c 기둥 개수) · 아니면 ""
        public string Prefix { get; init; } = "";        // 지금 부위로 정한 앞 글자(PrefixOf)
        public bool Editable { get; init; } = true;      // 적을 수 있다(그룹 안 · 다른 사용자가 편집 중이면 거짓 — 이름은 그대로 두고 번호만 센다)
        public int Zone { get; init; } = int.MaxValue;   // 위치 차례(위치 목록의 자리 · 없으면 맨 뒤)
        public double Z { get; init; }                   // 아래 끝 높이(m)
        public double X { get; init; }                   // 둘레 상자의 작은 x · y(m)
        public double Y { get; init; }
    }

    /// <summary>정한 까닭: 그대로 · 새 부재 · 복사본 · 같은 이름이라 새 번호 · 앞 글자가 바뀜 · 적을 수 없음.</summary>
    public enum Why { Kept, New, Copy, Duplicate, Prefix, Locked }

    /// <summary>정한 이름. Write = 칸이나 기록을 적어야 한다(그대로인 이름은 기록이 맞으면 적지 않는다 — 부재를 건드리지 않게).</summary>
    public sealed record Decision(long Id, string Name, bool Auto, bool Write, Why Why, string Origin, string Before);

    /// <summary>
    /// 이름을 정한다. members = 이름을 붙일 부재(대상 콘크리트 — 적을 수 없는 부재도 넘긴다: 그 이름은 그대로 두고 번호와 겹침을 셀 때 쓴다).
    /// others = 그 밖의 부재에 적힌 이름(가장 큰 번호 · 복사본이 이어받을 수 있는가를 볼 때만). 돌려주는 차례 = members 의 차례.
    /// </summary>
    public static List<Decision> Assign(IReadOnlyList<Member> members, IEnumerable<string>? others = null)
    {
        var decided = new Dictionary<long, Decision>();
        var fresh = new List<(Member m, Why why)>();
        var claims = new List<(Member m, string name, int rank)>();
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);   // 우리가 고칠 수 없는 이름(밖의 부재 · 적을 수 없는 부재)
        var max = new Dictionary<string, int>(StringComparer.Ordinal);

        // 앞 글자(영문 · 대문자 — 비었거나 영문이 아니면 «그 밖» E).
        static string PrefixOfMember(Member m) => TryParse(m.Prefix + "1", out string p, out _) ? p : "E";

        void See(string name)
        {
            if (TryParse(name, out string p, out int n)) max[p] = Math.Max(max.GetValueOrDefault(p), n);
        }

        foreach (string other in others ?? Array.Empty<string>())
        {
            string name = other.Trim();
            if (name.Length == 0) continue;
            See(name);
            taken.Add(name);
        }
        foreach (var m in members)
        {
            string name = m.Name.Trim();
            See(name);
            if (!m.Editable)
            {
                if (name.Length > 0) taken.Add(name);
                decided[m.Id] = new Decision(m.Id, name, m.Stamp == Stamp.Own && m.StampAuto && name == m.StampName, false, Why.Locked, m.Origin, name);
                continue;
            }
            if (name.Length == 0) { fresh.Add((m, Why.New)); continue; }
            // 프로그램이 붙인 이름인데 앞 글자가 부위와 맞지 않음 — 이 부재의 기록이든 복사해 온 기록이든(core-verify R53b M1: 원래 부재가 지워진 복사본이
            // 첫 [완료]에서 옛 앞 글자를 이어받고 다음 [완료]에서야 바뀌지 않게).
            if (m.Stamp != Stamp.None && m.StampAuto && name == m.StampName && TryParse(name, out string p, out _) && p != PrefixOfMember(m))
            {
                fresh.Add((m, Why.Prefix));
                continue;
            }
            // 이름을 지키려는 자리: 0 기록 그대로인 주인 · 1 이름을 고친 주인 · 2 기록 없음(사람이 적음) · 3 복사한 뒤 고친 이름 · 4 복사본.
            int rank = m.Stamp switch
            {
                Stamp.Own => name == m.StampName ? 0 : 1,
                Stamp.None => 2,
                _ => name == m.StampName ? 4 : 3,
            };
            claims.Add((m, name, rank));
        }

        foreach (var group in claims.GroupBy(c => c.name, StringComparer.OrdinalIgnoreCase))
        {
            var ordered = group.OrderBy(c => c.rank).ThenBy(c => c.m.Id).ToList();
            var winner = ordered[0];
            // 복사본은 그 이름을 아무도 쓰지 않을 때만 이어받는다(원래 부재가 대상 밖 · 적을 수 없는 부재여도 쓰는 중이다).
            bool keeps = winner.rank < 4 || !taken.Contains(winner.name);
            foreach (var c in ordered)
            {
                if (keeps && ReferenceEquals(c.m, winner.m))
                {
                    var m = c.m;
                    bool auto = m.Stamp != Stamp.None && m.StampAuto && c.name == m.StampName;
                    bool write = m.Stamp != Stamp.Own || m.StampName != c.name || m.StampAuto != auto;
                    decided[m.Id] = new Decision(m.Id, c.name, auto, write, Why.Kept, m.Origin, m.Name);
                }
                else fresh.Add((c.m, c.rank == 4 ? Why.Copy : Why.Duplicate));
            }
        }

        // 새 번호: 앞 글자마다 위치 차례 → 높이(아래부터) → x → y(cm 로 끊어 — 같은 줄의 부재가 µm 차이로 엇갈리지 않게) → Id.
        static long Cm(double v) => (long)Math.Round(v * 100);
        foreach (var (m, why) in fresh.OrderBy(f => PrefixOfMember(f.m), StringComparer.Ordinal).ThenBy(f => f.m.Zone)
                     .ThenBy(f => Cm(f.m.Z)).ThenBy(f => Cm(f.m.X)).ThenBy(f => Cm(f.m.Y)).ThenBy(f => f.m.Id))
        {
            string prefix = PrefixOfMember(m);
            int next = max.GetValueOrDefault(prefix) + 1;
            max[prefix] = next;
            decided[m.Id] = new Decision(m.Id, Format(prefix, next), true, true, why, m.Origin, m.Name.Trim());
        }
        return members.Select(m => decided[m.Id]).ToList();
    }
}
