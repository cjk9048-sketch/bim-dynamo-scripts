using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;

// DHInfraAuto — InfraWorks UI 자동화 도구 (JACK 0722, 0클릭 목표).
//   dump [maxDepth]  : 실행 중인 InfraWorks 창의 UI 요소 트리를 출력(자동화 대상 파악용). 기본 depth 7.
// InfraWorks를 켜고 (모델 열고 · Data Sources 패널을 보이게 한 뒤) 실행하세요.

string mode = args.Length > 0 ? args[0] : "dump";

// 파일 로그(auto는 CreateNoWindow로 실행돼 콘솔이 안 보임 → %TEMP%\dhinfra_auto.log 로 남긴다).
string logPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dhinfra_auto.log");
void Log(string s) { Console.WriteLine(s); try { System.IO.File.AppendAllText(logPath, s + Environment.NewLine); } catch { } }

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

if (mode == "auto") { try { System.IO.File.WriteAllText(logPath, "=== DHInfraAuto auto 시작 ===" + Environment.NewLine); } catch { } }

// InfraWorks 프로세스 + 메인창 대기. auto는 방금 켜졌을 수 있어 최대 180초 폴링, 그 외엔 즉시.
using var automation = new UIA3Automation();
int waitSec = mode == "auto" ? 180 : 10;
Application? app = null;
Window? main = null;
var swAttach = System.Diagnostics.Stopwatch.StartNew();
while (true)
{
    var procs = System.Diagnostics.Process.GetProcessesByName("InfraWorks");
    if (procs.Length > 0)
    {
        try { app = Application.Attach(procs[0].Id); } catch { app = null; }
        if (app != null)
        {
            try { main = app.GetMainWindow(automation, TimeSpan.FromSeconds(5)); } catch { main = null; }
            if (main != null && main.BoundingRectangle.Width > 200) { Log($"InfraWorks PID={procs[0].Id} 메인창 '{main.Name}'"); break; }
        }
    }
    if (swAttach.Elapsed.TotalSeconds >= waitSec) break;
    System.Threading.Thread.Sleep(2000);
}
if (main == null || app == null) { Log("InfraWorks 메인 창을 찾지 못했습니다. (프로세스/로드 대기 실패)"); return; }

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
else if (mode == "run" || mode == "auto")
{
    // auto(0클릭): 방금 켜진 InfraWorks 홈 화면에서 새 모델을 열고 → 로드 대기 → Data Sources 패널을 연다.
    //   run(수동): 사용자가 이미 모델·패널을 열어둔 상태 — 이 단계는 건너뜀.
    if (mode == "auto")
    {
        string sqlitePath = args.Length > 1 ? args[1] : "";
        string modelName = args.Length > 2 ? args[2]
                          : System.IO.Path.GetFileNameWithoutExtension(sqlitePath);
        if (!OpenModelAndPanel(main, sqlitePath, modelName)) { Log("모델/패널 열기 실패 — 중단"); return; }
        try { main = app.GetMainWindow(automation, TimeSpan.FromSeconds(10)) ?? main; } catch { }
    }

    // Data Sources 트리에서 옹벽=Reimport, 지형=Refresh 를 우클릭 메뉴로 자동 실행.
    var tree = FindByAutomationIdContains(main, "treeDataSources");
    if (tree == null) { Log("데이터소스 트리를 못 찾음 — Data Sources 패널이 열려있는지 확인."); return; }
    Console.WriteLine($"트리 발견: rect=({tree.BoundingRectangle})");

    if (args.Length >= 3)
        DoSource(tree, args[1], new[] { args[2] });   // 단일: run <이름조각> <액션>
    else
    {
        // 터레인·커버리지 = Refresh(Reimport 비활성). 옹벽(3D DWG) = **Reimport 후 Refresh**(둘 다 필요 — JACK 0722 실측).
        string[] refreshOnly = { "지형", "계획면", "소단_절토", "소단_성토", "사면_절토", "사면_성토" };
        foreach (var s in refreshOnly)
        {
            DoSource(tree, s, new[] { "Refresh" });
            System.Threading.Thread.Sleep(2000);   // 소스 간 처리 대기
        }
        DoSource(tree, "옹벽", new[] { "Reimport" });
        // 재임포트는 오래 걸릴 수 있음(대형 DWG) — 고정 대기 대신 상태가 'In Progress'를 벗어날 때까지 폴링(최대 10분).
        WaitImported(tree, "옹벽", maxSec: 600);
        DoSource(tree, "옹벽", new[] { "Refresh" });
    }
    Console.WriteLine("=== run 완료 ===");

    // 소스 행의 상태(같은 Y줄의 형제 TreeItem)가 'In Progress'를 벗어날 때까지 폴링 — 재임포트 완료 대기.
    void WaitImported(AutomationElement treeEl, string itemNameSub, int maxSec)
    {
        Console.WriteLine($"  상태 대기: '{itemNameSub}' 임포트 완료까지(최대 {maxSec}초)…");
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.Elapsed.TotalSeconds < maxSec)
        {
            System.Threading.Thread.Sleep(3000);
            try
            {
                var item = FindTreeItem(treeEl, itemNameSub);
                if (item == null) continue;
                int y = item.BoundingRectangle.Y;
                var rowTexts = treeEl.FindAllDescendants(cf => cf.ByControlType(FlaUI.Core.Definitions.ControlType.TreeItem))
                    .Where(t => Math.Abs(t.BoundingRectangle.Y - y) <= 4)
                    .Select(t => (t.Name ?? "").Trim()).ToList();
                bool busy = rowTexts.Any(t => t.Contains("In Pr") || t.Contains("Progress") || t.Contains("Pending"));
                if (!busy)
                {
                    Console.WriteLine($"  임포트 완료 감지({(int)sw.Elapsed.TotalSeconds}초): {string.Join("|", rowTexts)}");
                    System.Threading.Thread.Sleep(1500);   // 마무리 여유
                    return;
                }
            }
            catch { }
        }
        Console.WriteLine("  상태 대기 시간 초과 — 계속 진행");
    }

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
    Console.WriteLine($"알 수 없는 모드: {mode}  (사용법: DHInfraAuto dump|panel|run|auto|retarget …)");
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

// 요소 중심을 마우스로 좌클릭/더블클릭(TreeItem처럼 클릭포인트 예외 나는 요소 대비).
void ClickCenter(AutomationElement el, bool dbl = false)
{
    var r = el.BoundingRectangle;
    var c = new System.Drawing.Point((int)(r.X + r.Width / 2), (int)(r.Y + r.Height / 2));
    FlaUI.Core.Input.Mouse.MoveTo(c);
    System.Threading.Thread.Sleep(150);
    if (dbl) FlaUI.Core.Input.Mouse.DoubleClick(FlaUI.Core.Input.MouseButton.Left);
    else FlaUI.Core.Input.Mouse.Click(FlaUI.Core.Input.MouseButton.Left);
}

// auto: InfraWorks 홈에서 modelName 모델을 열고 → 로드 대기 → Data Sources 패널을 연다. 성공 시 true.
bool OpenModelAndPanel(AutomationElement home, string sqlitePath, string modelName)
{
    Log($"── 모델 열기: '{modelName}' (경로={sqlitePath}) ──");
    System.Threading.Thread.Sleep(3000);           // 홈 화면 안정화 여유

    // 첫 진단: 홈 화면 전체 트리를 로그로 남긴다(요소 구조 확정용 — 이후 정밀 튜닝 근거).
    Log("=== 홈 화면 트리 덤프 (depth ≤ 8) ===");
    Dump(home, 0, 8);
    Log("=== 홈 화면 덤프 끝 ===");

    // 1) 홈의 모델 타일/목록에서 이름이 일치하는 요소를 찾아 더블클릭(가장 흔한 경로).
    var tile = FindByNameContains(home, modelName);
    if (tile != null)
    {
        Log($"모델 타일 발견: '{tile.Name}' ct={tile.ControlType} rect=({tile.BoundingRectangle}) — 더블클릭");
        ClickCenter(tile, dbl: true);
        System.Threading.Thread.Sleep(2000);
        // 타일 클릭 후 'Open/열기' 버튼이 뜨는 UI라면 눌러준다(없으면 더블클릭으로 이미 열림).
        var openBtn = FindByNameContains(home, "Open", ct: "Button", maxW: 200)
                   ?? FindByNameContains(home, "열기", ct: "Button", maxW: 200);
        if (openBtn != null) { Log($"Open 버튼 클릭: rect=({openBtn.BoundingRectangle})"); ClickCenter(openBtn); }
    }
    else
    {
        Log($"모델 타일 '{modelName}' 못 찾음 — 홈 덤프를 확인해 열기 방식을 조정해야 함(1차 폴백 미구현).");
        return false;
    }

    // 2) 모델 로드 대기 — 'Data Sources' 버튼이 나타날 때까지(최대 180초).
    Log("모델 로드 대기(최대 180초)…");
    AutomationElement? dsBtn = null;
    var sw = System.Diagnostics.Stopwatch.StartNew();
    while (sw.Elapsed.TotalSeconds < 180)
    {
        System.Threading.Thread.Sleep(3000);
        try { home = app!.GetMainWindow(automation, TimeSpan.FromSeconds(5)) ?? home; } catch { }
        dsBtn = FindByNameContains(home, "Data Sources", ct: "Button", maxW: 80);
        if (dsBtn != null) break;
    }
    if (dsBtn == null) { Log("Data Sources 버튼이 나타나지 않음 — 모델 로드 실패로 판단."); return false; }

    // 3) Data Sources 패널 열기.
    Log($"Data Sources 패널 열기: rect=({dsBtn.BoundingRectangle})");
    ClickCenter(dsBtn);
    System.Threading.Thread.Sleep(2500);
    return true;
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
            Log($"{indent}[{ct}] name='{Trunc(name)}' id='{aid}' rect=({(int)r.X},{(int)r.Y} {(int)r.Width}x{(int)r.Height})");
        }
        AutomationElement[] kids;
        try { kids = el.FindAllChildren(); } catch { return; }
        foreach (var c in kids) Dump(c, depth + 1, max);
    }
    catch { }
}

static string Trunc(string s) => s.Length > 60 ? s.Substring(0, 60) + "…" : s;
