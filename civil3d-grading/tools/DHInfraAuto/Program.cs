using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;

// DHInfraAuto — InfraWorks UI 자동화 도구 (JACK 0722, 0클릭 목표).
//   dump [maxDepth]  : 실행 중인 InfraWorks 창의 UI 요소 트리를 출력(자동화 대상 파악용). 기본 depth 7.
// InfraWorks를 켜고 (모델 열고 · Data Sources 패널을 보이게 한 뒤) 실행하세요.

string mode = args.Length > 0 ? args[0] : "dump";

// retarget <모델.sqlite> <실행폴더> — InfraWorks 불필요(파일 편집만). 복사한 모델의 소스 경로를 실행 폴더로 재작성.
//   템플릿 소스는 C:/DHInfra/파일 을 가리킴 → C:/DHInfra/yyyyMMdd_HHmmss/파일 로 치환(슬래시 양쪽 형태 처리).
if (mode == "retarget")
{
    if (args.Length < 3) { Console.WriteLine("사용법: DHInfraAuto retarget <모델.sqlite> <실행폴더>"); return; }
    string dbPath = args[1];
    string runFolder = args[2].Replace('\\', '/').TrimEnd('/');
    if (!File.Exists(dbPath)) { Console.WriteLine("모델 파일 없음: " + dbPath); return; }
    using var con = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=" + dbPath);
    con.Open();
    using var cmd = con.CreateCommand();
    cmd.CommandText =
        "UPDATE DATA_SOURCES SET CONNECTION_STRING = replace(replace(CONNECTION_STRING," +
        " 'C:/DHInfra/', @f), 'C:\\DHInfra\\', @b)";
    cmd.Parameters.AddWithValue("@f", runFolder + "/");
    cmd.Parameters.AddWithValue("@b", runFolder.Replace('/', '\\') + "\\");
    int n = cmd.ExecuteNonQuery();
    Console.WriteLine($"retarget 완료: {n}개 소스 경로 → {runFolder}");
    return;
}

var procs = System.Diagnostics.Process.GetProcessesByName("InfraWorks");
if (procs.Length == 0)
{
    Console.WriteLine("InfraWorks 프로세스를 찾지 못했습니다. InfraWorks를 실행하고 모델을 연 뒤 다시 실행하세요.");
    return;
}
Console.WriteLine($"InfraWorks PID={procs[0].Id}");

using var automation = new UIA3Automation();
Application app = Application.Attach(procs[0].Id);
Window? main = null;
try { main = app.GetMainWindow(automation, TimeSpan.FromSeconds(10)); } catch { }
if (main == null) { Console.WriteLine("메인 창을 찾지 못했습니다."); return; }
Console.WriteLine($"메인 창: '{main.Name}'  rect=({main.BoundingRectangle})");

if (mode == "dump")
{
    int max = args.Length > 1 && int.TryParse(args[1], out var m) ? m : 7;
    Console.WriteLine($"=== UI 트리 (depth ≤ {max}) — 이름/ID 있는 요소만 ===");
    Dump(main, 0, max);
    Console.WriteLine("=== 끝 ===");
}
else if (mode == "panel")
{
    // "Data Sources" 툴바 버튼을 찾아 눌러 패널을 연 뒤 다시 덤프(소스 목록 파악용).
    var btn = FindByNameContains(main, "Data Sources", ct: "Button", maxW: 60);
    if (btn == null) { Console.WriteLine("'Data Sources' 버튼을 못 찾음"); return; }
    Console.WriteLine($"Data Sources 버튼 클릭: rect=({btn.BoundingRectangle})");
    try { btn.Click(); } catch (Exception ex) { Console.WriteLine("클릭 실패: " + ex.Message); }
    System.Threading.Thread.Sleep(2000);
    int max = args.Length > 1 && int.TryParse(args[1], out var m) ? m : 9;
    Console.WriteLine($"=== 패널 연 뒤 UI 트리 (depth ≤ {max}) ===");
    Dump(main, 0, max);
    Console.WriteLine("=== 끝 ===");
}
else if (mode == "run")
{
    // Data Sources 트리에서 옹벽=Reimport, 지형=Refresh 를 우클릭 메뉴로 자동 실행.
    var tree = FindByAutomationIdContains(main, "treeDataSources");
    if (tree == null) { Console.WriteLine("데이터소스 트리를 못 찾음 — Data Sources 패널이 열려있는지 확인."); return; }
    Console.WriteLine($"트리 발견: rect=({tree.BoundingRectangle})");

    // 7개 소스 전부: 각 소스마다 Reimport 가능하면 Reimport, 아니면 Refresh(터레인은 Reimport 비활성 → Refresh).
    string[] prefer = { "Reimport", "Refresh" };
    if (args.Length >= 3)
        DoSource(tree, args[1], new[] { args[2] });   // 단일: run <이름조각> <액션>
    else
    {
        string[] sources = { "지형", "계획면", "소단_절토", "소단_성토", "사면_절토", "사면_성토", "옹벽" };
        foreach (var s in sources)
        {
            DoSource(tree, s, prefer);
            System.Threading.Thread.Sleep(2500);   // 소스 간 처리 대기
        }
    }
    Console.WriteLine("=== run 완료 ===");

    bool DoSource(AutomationElement treeEl, string itemNameSub, string[] actionsPreferred)
    {
        Console.WriteLine($"\n--- [{itemNameSub}] → {string.Join("/", actionsPreferred)} ---");
        var desktop = automation.GetDesktop();
        // ★재시도(최대 4회): 연속 실행 시 InfraWorks가 바쁘면 컨텍스트 메뉴가 안 뜰 수 있음.
        for (int attempt = 1; attempt <= 4; attempt++)
        {
            var item = FindTreeItem(treeEl, itemNameSub);
            if (item == null) { Console.WriteLine($"  [{attempt}] TreeItem '{itemNameSub}' 못 찾음"); System.Threading.Thread.Sleep(700); continue; }
            var r = item.BoundingRectangle;
            var center = new System.Drawing.Point(r.X + r.Width / 2, r.Y + r.Height / 2);
            FlaUI.Core.Input.Mouse.MoveTo(center);
            System.Threading.Thread.Sleep(150);
            FlaUI.Core.Input.Mouse.Click(FlaUI.Core.Input.MouseButton.Right);
            System.Threading.Thread.Sleep(500 + attempt * 400);

            var menuItems = desktop.FindAllDescendants(cf => cf.ByControlType(FlaUI.Core.Definitions.ControlType.MenuItem));
            // 선호 순서대로 '활성화된' 첫 액션 선택(터레인은 Reimport 비활성 → Refresh로).
            AutomationElement? target = null; string chosen = "";
            foreach (var act in actionsPreferred)
            {
                var cand = menuItems.FirstOrDefault(mi => (mi.Name ?? "").Contains(act));
                if (cand == null) continue;
                bool en = true; try { en = cand.IsEnabled; } catch { }
                if (en) { target = cand; chosen = act; break; }
            }
            if (target == null)
            {
                Console.WriteLine($"  [{attempt}] 활성 액션 없음/메뉴 안 뜸 — ESC 후 재시도");
                FlaUI.Core.Input.Keyboard.Press(FlaUI.Core.WindowsAPI.VirtualKeyShort.ESCAPE);
                System.Threading.Thread.Sleep(1200);
                continue;
            }
            var mr = target.BoundingRectangle;
            Console.WriteLine($"  [{attempt}] '{chosen}' 클릭");
            var mc = new System.Drawing.Point(mr.X + mr.Width / 2, mr.Y + mr.Height / 2);
            FlaUI.Core.Input.Mouse.MoveTo(mc);
            System.Threading.Thread.Sleep(150);
            FlaUI.Core.Input.Mouse.Click(FlaUI.Core.Input.MouseButton.Left);
            Console.WriteLine("  클릭 완료.");
            return true;
        }
        Console.WriteLine($"  실패: '{itemNameSub}' — 재시도 소진");
        return false;
    }
}
else
{
    Console.WriteLine($"알 수 없는 모드: {mode}  (사용법: DHInfraAuto dump|panel|run [depth])");
}

AutomationElement? FindByAutomationIdContains(AutomationElement root, string idSub)
{
    AutomationElement? found = null;
    void Rec(AutomationElement el, int d)
    {
        if (found != null || d > 14) return;
        try
        {
            string aid = ""; try { aid = el.AutomationId ?? ""; } catch { }
            if (aid.Contains(idSub)) { found = el; return; }
            foreach (var c in el.FindAllChildren()) { Rec(c, d + 1); if (found != null) return; }
        }
        catch { }
    }
    Rec(root, 0);
    return found;
}

AutomationElement? FindTreeItem(AutomationElement tree, string nameSub)
{
    AutomationElement? found = null;
    void Rec(AutomationElement el, int d)
    {
        if (found != null || d > 6) return;
        try
        {
            var name = (el.Name ?? "");
            var r = el.BoundingRectangle;
            if (el.ControlType == FlaUI.Core.Definitions.ControlType.TreeItem && name.Contains(nameSub) && r.Width > 0)
            { found = el; return; }
            foreach (var c in el.FindAllChildren()) { Rec(c, d + 1); if (found != null) return; }
        }
        catch { }
    }
    Rec(tree, 0);
    return found;
}

// 이름에 substring 포함 + (선택) 컨트롤타입/최대폭 조건으로 첫 요소 찾기.
AutomationElement? FindByNameContains(AutomationElement root, string sub, string? ct = null, int maxW = int.MaxValue)
{
    AutomationElement? found = null;
    void Rec(AutomationElement el, int depth)
    {
        if (found != null || depth > 12) return;
        try
        {
            string name = (el.Name ?? "");
            var r = el.BoundingRectangle;
            if (name.Contains(sub) && (ct == null || el.ControlType.ToString() == ct) && r.Width <= maxW && r.Width > 0)
            { found = el; return; }
            foreach (var c in el.FindAllChildren()) { Rec(c, depth + 1); if (found != null) return; }
        }
        catch { }
    }
    Rec(root, 0);
    return found;
}

void Dump(AutomationElement el, int depth, int max)
{
    if (depth > max) return;
    try
    {
        string ct = el.ControlType.ToString();
        string name = (el.Name ?? "").Trim();
        string aid = "";
        try { aid = el.AutomationId ?? ""; } catch { }
        var r = el.BoundingRectangle;
        bool show = !string.IsNullOrWhiteSpace(name) || !string.IsNullOrWhiteSpace(aid);
        if (show)
        {
            string indent = new string(' ', depth * 2);
            Console.WriteLine($"{indent}[{ct}] name='{Trunc(name)}' id='{aid}' rect=({(int)r.X},{(int)r.Y} {(int)r.Width}x{(int)r.Height})");
        }
        AutomationElement[] kids;
        try { kids = el.FindAllChildren(); } catch { return; }
        foreach (var c in kids) Dump(c, depth + 1, max);
    }
    catch { }
}

static string Trunc(string s) => s.Length > 60 ? s.Substring(0, 60) + "…" : s;
