using System.IO;
using System.Text.Json;

namespace DH.Grading.Civil;

/// <summary>★[v117.13 · JACK 1112 «형식도 한 번 저장하면 나중에 언제든 불러서 쓸 수 있게»] 측구 <b>저장 형식</b> — 이름 · 형식 · 치수 · 그레이팅.
/// <para>사용자 파일 <c>%APPDATA%\DHGrading\ditch_presets.json</c>(UTF-8) — 어느 도면에서든 쓴다. 없거나 깨진 파일은 빈 목록 + 짧은 알림(<see cref="Note"/>).
/// 기본 제공(<see cref="Builtin"/>)은 파일에 안 적고 지울 수 없다.</para></summary>
internal static class RoadDitchPresets
{
    /// <param name="Kind">1 = U형 · 2 = L형</param>
    /// <param name="Values">칸에 보이는 값 — U: mm 8개 · L: mm · 경사 % … 9개</param>
    internal sealed record Preset(string Name, int Kind, double[] Values, bool Lid, bool Builtin = false);

    /// <summary>파일을 읽다 생긴 알림(없으면 빈 글).</summary>
    internal static string Note = "";

    internal static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DHGrading", "ditch_presets.json");

    /// <summary>기본값(DitchSection 기본 — JACK 도면 대표 치수).</summary>
    internal static readonly Preset[] Builtins =
    {
        new("U형 300×300", 1, new double[] { 300, 300, 150, 150, 100, 100, 50, 50 }, true, true),
        new("L형 기본", 2, new double[] { 500, 6, 200, 250, 200, 140, 50, 50, 220 }, false, true),
    };

    sealed class FileModel { public List<Item> Presets { get; set; } = new(); }
    sealed class Item { public string Name { get; set; } = ""; public int Kind { get; set; } public double[] Values { get; set; } = Array.Empty<double>(); public bool Lid { get; set; } }

    /// <summary>기본 제공 + 저장한 것(파일 차례). 파일이 없으면 기본만.</summary>
    internal static List<Preset> Load()
    {
        Note = "";
        var list = new List<Preset>(Builtins);
        try
        {
            if (!File.Exists(FilePath)) return list;
            var m = JsonSerializer.Deserialize<FileModel>(File.ReadAllText(FilePath, System.Text.Encoding.UTF8));
            if (m?.Presets == null) { Note = "저장 형식 파일이 비었음"; return list; }
            foreach (var it in m.Presets)
            {
                int need = it.Kind == 1 ? 8 : it.Kind == 2 ? 9 : 0;
                if (need == 0 || string.IsNullOrWhiteSpace(it.Name) || it.Values == null || it.Values.Length < need || it.Values.Any(x => !double.IsFinite(x))) continue;
                if (list.Any(p => p.Name == it.Name)) continue;
                list.Add(new Preset(it.Name.Trim(), it.Kind, it.Values.Take(need).ToArray(), it.Kind == 1 && it.Lid));
            }
        }
        catch (Exception ex) { Note = "저장 형식 파일 못 읽음 — " + ex.Message; }
        return list;
    }

    /// <summary>같은 이름이면 덮어쓰고 저장 · 기본 제공 이름은 거절. 실패하면 알림 글(성공 = 빈 글).</summary>
    internal static string Save(Preset p)
    {
        try
        {
            if (Builtins.Any(b => b.Name == p.Name)) return "기본 제공 이름 — 다른 이름";
            var mine = Load().Where(x => !x.Builtin && x.Name != p.Name).ToList();
            mine.Add(p with { Name = p.Name.Trim(), Builtin = false });
            return Write(mine);
        }
        catch (Exception ex) { return "저장 실패 — " + ex.Message; }
    }

    /// <summary>저장한 것 지우기(기본 제공은 못 지움).</summary>
    internal static string Delete(string name)
    {
        try
        {
            if (Builtins.Any(b => b.Name == name)) return "기본 제공은 못 지움";
            return Write(Load().Where(x => !x.Builtin && x.Name != name).ToList());
        }
        catch (Exception ex) { return "지우기 실패 — " + ex.Message; }
    }

    static string Write(List<Preset> mine)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        // ★[v117.13 · 설치 전 검토 MED] 못 읽은 파일(깨짐)을 기본 목록으로 덮어 예전 저장 형식이 다 사라지지 않게 — 덮기 전에 «.bak»으로 남김(날짜 붙임) ·
        //   쓰기는 임시 파일에 쓴 뒤 바꿔 끼움(쓰는 중 끊겨도 원래 파일은 그대로)
        try { if (Note.Length > 0 && File.Exists(FilePath)) File.Copy(FilePath, FilePath + "." + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".bak", true); } catch { }
        var m = new FileModel { Presets = mine.Select(x => new Item { Name = x.Name, Kind = x.Kind, Values = x.Values, Lid = x.Lid }).ToList() };
        string tmp = FilePath + ".tmp";
        WriteText(tmp, JsonSerializer.Serialize(m, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
        if (File.Exists(FilePath)) File.Replace(tmp, FilePath, null); else File.Move(tmp, FilePath);
        return "";
    }

    static void WriteText(string path, string text) => File.WriteAllText(path, text, new System.Text.UTF8Encoding(false));
}
