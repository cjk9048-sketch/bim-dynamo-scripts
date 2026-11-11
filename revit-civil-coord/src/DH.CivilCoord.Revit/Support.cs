using System.IO;
using System.Text.Json;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using Autodesk.Revit.UI;
using DH.CivilCoord.Core;

namespace DH.CivilCoord.Revit;

/// <summary>Revit 알림 창(제목 앞에 애드인 이름이 두 번 붙지 않게).</summary>
internal static class Dialogs
{
    public const string Title = "DH Civil 좌표 맞춤";

    public static void Show(string instruction, string content = "")
    {
        var d = new TaskDialog(Title) { TitleAutoPrefix = false, MainInstruction = instruction, MainContent = content };
        d.Show();
    }

    public static bool YesNo(string instruction, string content)
    {
        var d = new TaskDialog(Title)
        {
            TitleAutoPrefix = false, MainInstruction = instruction, MainContent = content,
            CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No, DefaultButton = TaskDialogResult.No,
        };
        return d.Show() == TaskDialogResult.Yes;
    }
}

/// <summary>PC 기록 — %APPDATA%\DH.CivilCoord\log.txt(단계마다 한 줄) · settings.json(마지막 IFC 폴더). 모델 자료가 아니다.</summary>
internal static class Log
{
    private static string Dir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DH.CivilCoord");

    public static void Line(string text)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            string path = Path.Combine(Dir, "log.txt");
            var fi = new FileInfo(path);
            if (fi.Exists && fi.Length > 2_000_000) File.Move(path, Path.Combine(Dir, "log.old.txt"), true);
            File.AppendAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}\t{text}\r\n");
        }
        catch { }
    }

    private sealed class Settings
    {
        public string LastFolder { get; set; } = "";
        public string LastCivilFolder { get; set; } = "";
        public double WinW { get; set; }
        public double WinH { get; set; }
        public bool WinMax { get; set; }
        public bool InsExact { get; set; } = true;
        public bool InsTopo { get; set; }
        public bool InsLines { get; set; } = true;
    }

    private static Settings LoadSettings()
    {
        try { return JsonSerializer.Deserialize<Settings>(File.ReadAllText(Path.Combine(Dir, "settings.json"))) ?? new Settings(); }
        catch { return new Settings(); }
    }

    private static void SaveSettings(Action<Settings> change)
    {
        try
        {
            var s = LoadSettings();
            change(s);
            Directory.CreateDirectory(Dir);
            File.WriteAllText(Path.Combine(Dir, "settings.json"), JsonSerializer.Serialize(s));
        }
        catch { }
    }

    /// <summary>마지막으로 IFC 를 저장한 폴더.</summary>
    public static string LastFolder { get => LoadSettings().LastFolder; set => SaveSettings(s => s.LastFolder = value); }

    /// <summary>마지막으로 Civil 기준점 파일을 연 폴더.</summary>
    public static string LastCivilFolder { get => LoadSettings().LastCivilFolder; set => SaveSettings(s => s.LastCivilFolder = value); }

    /// <summary>[모델에 넣기]에서 마지막에 고른 것(처음 = 정확한 면 + 3D 선).</summary>
    public static (bool Exact, bool Topo, bool Lines) InsertChoice
    {
        get { var s = LoadSettings(); return (s.InsExact, s.InsTopo, s.InsLines); }
        set => SaveSettings(s => { s.InsExact = value.Exact; s.InsTopo = value.Topo; s.InsLines = value.Lines; });
    }

    /// <summary>맞춤 창 크기(0 = 처음) · 최대화.</summary>
    public static (double W, double H, bool Max) WindowSize
    {
        get { var s = LoadSettings(); return (double.IsFinite(s.WinW) ? s.WinW : 0, double.IsFinite(s.WinH) ? s.WinH : 0, s.WinMax); }
        set => SaveSettings(s => { s.WinW = value.W; s.WinH = value.H; s.WinMax = value.Max; });
    }
}

/// <summary>
/// 맞춘 값을 rvt 안에 둔다(프로젝트 정보의 확장 저장소 · JSON 한 칸). 모델 파일이 기록이다 — 다른 PC 에서 열어도 그 값.
/// </summary>
internal static class FitStore
{
    private static readonly Guid SchemaId = new("6c1f4e2a-93b7-4d58-a1c0-5e7d2b9f8a31");
    private const string Field = "Json";

    private static Schema GetSchema()
    {
        var s = Schema.Lookup(SchemaId);
        if (s != null) return s;
        var b = new SchemaBuilder(SchemaId);
        b.SetSchemaName("DH_CivilCoordFit");
        b.SetReadAccessLevel(AccessLevel.Public);
        b.SetWriteAccessLevel(AccessLevel.Public);
        b.SetVendorId("DHEC");
        b.SetDocumentation("DH Civil coordinate fit (two points, Civil XY, height offset) as JSON");
        b.AddSimpleField(Field, typeof(string));
        return b.Finish();
    }

    public static FitState? Load(Document doc)
    {
        try
        {
            var s = Schema.Lookup(SchemaId);
            if (s == null || doc.ProjectInformation == null) return null;
            var e = doc.ProjectInformation.GetEntity(s);
            if (e == null || !e.IsValid()) return null;
            return FitState.FromJson(e.Get<string>(Field));
        }
        catch (Exception ex) { Log.Line("맞춤 값 읽기 오류\t" + ex.Message); return null; }
    }

    /// <summary>열린 트랜잭션 안에서 쓴다.</summary>
    public static void WriteInside(Document doc, FitState state)
    {
        var e = new Entity(GetSchema());
        e.Set(Field, state.ToJson());
        doc.ProjectInformation.SetEntity(e);
    }

    /// <summary>따로 트랜잭션 하나로 쓴다. 못 쓰면 까닭.</summary>
    public static string? Save(Document doc, FitState state)
    {
        try
        {
            using var t = new Transaction(doc, "DH 좌표 맞춤 값 저장");
            if (t.Start() != TransactionStatus.Started) return "트랜잭션을 못 엶";
            WriteInside(doc, state);
            return t.Commit() == TransactionStatus.Committed ? null : "Revit이 저장을 받지 않음";
        }
        catch (Exception ex) { return ex.Message; }
    }
}
