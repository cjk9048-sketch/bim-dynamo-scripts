using System.Globalization;
using System.IO;
using System.Text;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.DirectContext3D;
using Autodesk.Revit.DB.ExternalService;
using Autodesk.Revit.UI;
using DH.Takeoff.Core.Formwork;
using Tri = DH.Takeoff.Revit.OverlayMath.Tri;
using Pt = DH.Takeoff.Revit.OverlayMath.P;

namespace DH.Takeoff.Revit;

/// <summary>
/// 거푸집 면 가르기의 결과를 3D 뷰에 색으로 덧그린다(Revit 의 DirectContext3D).
/// 모델은 바꾸지 않는다 — 화면에만 그리는 것이라 저장되지 않고, [색 지우기]나 Revit 을 닫으면 사라진다.
/// 면의 조각마다(안팎·종류가 다른 자리마다) 따로 칠할 수 있어, 한 면이 안쪽과 바깥쪽으로 갈린 자리도 그대로 보인다.
/// 색: 종류마다 한 색, 안쪽을 보는 면은 같은 색의 연한 쪽, 유로폼의 높은 구간은 짙은 쪽. 확인할 자리는 분홍.
///
/// Revit API 문서와 SDK 예제(DuplicateGraphics)를 따른 것:
///  · RenderScene 은 다른 스레드에서 불릴 수 있다 → 그릴 것(Scene)은 만든 뒤 바꾸지 않고 통째로 갈아 끼우며, 버퍼는 RenderScene 안에서만 만진다(잠금).
///  · 버퍼·형식·효과는 그리기 전에 IsValid() 를 보고, 하나라도 못 쓰게 됐으면 다시 만든다.
///  · FlushBuffer 의 인덱스 수는 예제대로 '세모 수 × IndexTriangle.GetSizeInShortInts()'.
///  · 화면 갱신은 UIDocument.UpdateAllOpenViews() (DirectContext3D 로 그린 것은 Revit 이 스스로 다시 그리지 않는다).
/// 그 밖에:
///  · 한 버퍼에 세모를 너무 많이 담으면 그림이 깨진다는 보고가 있어(꼭짓점 번호의 크기) 세모 1만 개씩 나눠 담는다.
///  · 단면 상자로 자른 뷰에서 잘려 나간 자리의 색이 허공에 남지 않게, 뷰의 자르는 평면(DrawContext.GetClipPlanes)으로 세모를 잘라 그린다.
/// </summary>
public sealed class FormworkOverlay : IDirectContext3DServer
{
    private const double MToFt = 1 / 0.3048;
    private const double Lift = 0.004 * MToFt;      // 면에서 바깥으로 4mm 띄워 그린다(모델 면과 겹쳐 깜박이지 않게)
    private const int ChunkTriangles = 10_000;      // 버퍼 하나에 담는 세모 수(꼭짓점 3만 개 — 번호가 16비트여도 넘치지 않는다)

    private static readonly Guid ServerId = new("7a2b6c10-0000-4a00-9b00-0000000000f1");
    private static FormworkOverlay? _instance;

    /// <summary>그릴 것 한 벌. 만든 뒤에는 바꾸지 않는다.</summary>
    private sealed class Scene
    {
        public Document? Doc;
        public Tri[] Triangles = Array.Empty<Tri>();   // 꼭짓점은 Revit 길이 단위(ft)
        public double X0, Y0, Z0, X1, Y1, Z1;          // 세모들을 싸는 상자
        // 무슨 일이 있었는지(요약 창과 기록 파일에 적는다) — 그림마다 따로 둔다.
        public int Renders;                            // 이 그림을 화면에 그린 횟수
        public volatile string Built = "";             // 마지막으로 버퍼를 만든 내용
        public volatile string Error = "";             // 그리다 난 오류
    }

    private sealed class Chunk
    {
        public required VertexBuffer Vertices;
        public required IndexBuffer Indices;
        public int VertexCount, IndexCount, TriangleCount;
    }

    private volatile Scene _scene = new();

    // 아래는 RenderScene 안에서만 만진다(_gate 로 잠근다).
    private readonly object _gate = new();
    private readonly List<Chunk> _chunks = new();
    private VertexFormat? _format;
    private EffectInstance? _effect;
    private Scene? _builtScene, _failedScene;
    private string _builtClip = "", _failedClip = "";
    private int _builtTriangles;

    private static int _logLines;

    /// <summary>범례: 뜻 → 색 이름. 요약 창에 적는다.</summary>
    public static readonly (string meaning, string colorName)[] Legend =
    {
        (FormworkTable.Euro, "파랑"), (FormworkTable.Ply3, "주황"), (FormworkTable.Ply4, "초록"), (FormworkTable.Ply6, "갈색"),
        ("확인할 자리", "분홍"),
    };

    // 안쪽을 보는 면은 같은 색을 흰색 쪽으로 반쯤 섞어 연하게, 유로폼의 둘째 높이 구간부터는 검은색 쪽으로 섞어 짙게.
    // 색은 수량산출 창의 3D 와 같다(Core 의 FormworkPalette 하나를 같이 쓴다).
    private static int ColorOf(FacePatch patch)
    {
        var (r, g, b) = FormworkPalette.Of(patch);
        return r << 16 | g << 8 | b;
    }

    /// <summary>색을 올린 결과(요약 창에 적을 것).</summary>
    public sealed class Shown
    {
        public int Triangles;           // 올린 세모 수
        public int Elements;            // 칠한 부재 수
        public int Hidden;              // 지금 뷰에 보이지 않아 칠하지 않은 부재 수
        public string View = "";        // 보이는 부재를 가려낸 3D 뷰의 이름("" = 가려내지 않음)
        public string RefreshProblem = "";
    }

    /// <summary>
    /// 결과를 화면에 올린다. 세는 조각과 확인할 조각(아래가 빈 밑면 등)을 그린다.
    /// 지금 뷰가 3D 뷰면 그 뷰에 보이는 부재만 칠한다(숨긴 부재의 색이 허공에 남지 않게) — 숨김을 바꾼 뒤에는 다시 실행해야 맞춰진다.
    /// 실패하면(이 Revit 에서 그리기 서비스를 쓸 수 없으면) 예외를 던진다 — 부르는 쪽이 '색은 못 칠함'으로 알린다.
    /// </summary>
    public static Shown Show(UIDocument uidoc, IReadOnlyList<ElementFormwork> results)
    {
        var doc = uidoc.Document;
        var shown = new Shown();
        HashSet<long>? visible = VisibleIn(uidoc, results, shown);

        var triangles = new List<Tri>();
        double x0 = double.MaxValue, y0 = double.MaxValue, z0 = double.MaxValue, x1 = double.MinValue, y1 = double.MinValue, z1 = double.MinValue;
        foreach (var result in results)
        {
            if (!result.Faces.Any(f => f.Patches.Any(p => p.Counted || p.NeedsCheck))) continue;
            if (visible != null && !visible.Contains(result.Element.Id)) { shown.Hidden++; continue; }
            int before = triangles.Count;
            foreach (var patch in result.Faces.SelectMany(f => f.Patches))
            {
                if (!patch.Counted && !patch.NeedsCheck) continue;
                int color = ColorOf(patch);
                for (int i = 0; i + 2 < patch.Triangles.Count; i += 3)
                {
                    Pt a = Feet(patch.Triangles[i]), b = Feet(patch.Triangles[i + 1]), c = Feet(patch.Triangles[i + 2]);
                    Pt normal = (b - a).Cross(c - a);       // 세모는 면의 바깥을 보게 돌아 있다(Core 의 Poly.Triangles)
                    double length = normal.Length;
                    if (!(length > 1e-12) || !double.IsFinite(length)) continue;   // 넓이 없는 세모 · 숫자가 아닌 좌표
                    Pt lift = normal * (Lift / length);
                    a += lift; b += lift; c += lift;
                    triangles.Add(new Tri(a, b, c, color));
                    foreach (var p in new[] { a, b, c })
                    {
                        x0 = Math.Min(x0, p.X); y0 = Math.Min(y0, p.Y); z0 = Math.Min(z0, p.Z);
                        x1 = Math.Max(x1, p.X); y1 = Math.Max(y1, p.Y); z1 = Math.Max(z1, p.Z);
                    }
                }
            }
            if (triangles.Count > before) shown.Elements++;     // 세모를 얻지 못한 곡면만 가진 부재는 세지 않는다
        }
        shown.Triangles = triangles.Count;

        var server = _instance ??= new FormworkOverlay();
        var scene = new Scene { Doc = doc, Triangles = triangles.ToArray() };
        if (triangles.Count > 0) (scene.X0, scene.Y0, scene.Z0, scene.X1, scene.Y1, scene.Z1) = (x0, y0, z0, x1, y1, z1);
        Interlocked.Exchange(ref _logLines, 0);
        server._scene = scene;      // 통째로 갈아 끼운다 — 그리는 쪽은 옛 것이나 새 것 가운데 하나를 온전히 본다
        Log($"올림: 부재 {shown.Elements}개 · 세모 {shown.Triangles}개 · 숨겨져 뺀 부재 {shown.Hidden}개 · 뷰 '{shown.View}' · 스레드 {Environment.CurrentManagedThreadId}", restart: true);

        if (triangles.Count > 0) Register(server);
        shown.RefreshProblem = Refresh(uidoc);
        return shown;
    }

    /// <summary>화면에 올린 색을 지운다.</summary>
    public static void Clear(UIDocument? uidoc)
    {
        if (_instance == null) return;
        var old = _instance._scene;
        // 빈 그림으로 갈아 끼운다(상자는 그대로 둔다 — 그리는 도중에 상자를 물어도 엉뚱한 값이 나가지 않게).
        _instance._scene = new Scene { Doc = old.Doc, X0 = old.X0, Y0 = old.Y0, Z0 = old.Z0, X1 = old.X1, Y1 = old.Y1, Z1 = old.Z1 };
        Log("지움");
        if (uidoc != null) Refresh(uidoc);
    }

    /// <summary>지금 색이 올라가 있는가.</summary>
    public static bool IsShowing => _instance != null && _instance._scene.Triangles.Length > 0;

    /// <summary>지금 그림을 화면에 한 번이라도 그렸는가(요약 창의 자가 확인).</summary>
    public static bool Drawn => _instance != null && Volatile.Read(ref _instance._scene.Renders) > 0;

    /// <summary>지금 그림을 그리다 난 오류("" = 없음).</summary>
    public static string Error => _instance?._scene.Error ?? "";

    /// <summary>기록 파일(문제가 있을 때 보내 달라고 할 것).</summary>
    public static string LogPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DH.Takeoff", "log", "formwork_overlay.log");

    private static Pt Feet(double[] p) => new(p[0] * MToFt, p[1] * MToFt, p[2] * MToFt);

    // 지금 뷰가 3D 뷰면 그 뷰에서 칠할 부재의 Id 들. 3D 뷰가 아니거나 알아내지 못하면 null(가려내지 않는다).
    // 빼는 것: 숨긴 부재(뷰에서 숨기기 · 임시 숨기기/분리 · 카테고리 끄기)와 뷰에 그려지지 않는 부재(필터·단계·작업세트 등).
    // 다만 단면 상자가 켜져 있을 때 '상자 밖이라서' 안 그려지는 부재는 빼지 않는다 — 색은 그릴 때 상자에 맞춰 잘리므로,
    // 상자를 움직여도 다시 실행할 필요가 없다. 상자 밖인지는 부재의 경계상자가 단면 상자와 겹치는가로 본다.
    private static HashSet<long>? VisibleIn(UIDocument uidoc, IReadOnlyList<ElementFormwork> results, Shown shown)
    {
        try
        {
            var doc = uidoc.Document;
            if (uidoc.ActiveView is not View3D view || view.IsTemplate) return null;
            if (!FilteredElementCollector.IsViewValidForElementIteration(doc, view.Id)) return null;
            var drawn = new HashSet<long>(new FilteredElementCollector(doc, view.Id).WhereElementIsNotElementType().ToElementIds().Select(id => id.Value));
            bool temporary = view.IsTemporaryHideIsolateActive();
            BoundingBoxXYZ? box = view.IsSectionBoxActive ? view.GetSectionBox() : null;
            var visible = new HashSet<long>();
            foreach (var result in results)
            {
                long id = result.Element.Id;
                try
                {
                    var elementId = new ElementId(id);
                    var element = doc.GetElement(elementId);
                    if (element == null) continue;
                    if (element.IsHidden(view)) continue;
                    if (temporary && !view.IsElementVisibleInTemporaryViewMode(TemporaryViewMode.TemporaryHideIsolate, elementId)) continue;
                    if (element.Category != null && view.GetCategoryHidden(element.Category.Id)) continue;
                    if (drawn.Contains(id) || (box != null && !Touches(element, box))) visible.Add(id);
                }
                catch { visible.Add(id); }     // 알아내지 못한 부재는 칠한다
            }
            shown.View = view.Name;
            return visible;
        }
        catch { return null; }
    }

    // 부재의 경계상자가 단면 상자와 겹치는가. 경계상자의 여덟 모서리를 단면 상자의 좌표로 옮겨 그 범위가 겹치는지 본다
    // (돌려 놓은 상자에서는 조금 넉넉하게 '겹친다'고 본다). 경계상자를 모르면 겹치지 않는 것으로 친다(칠한다).
    internal static bool Touches(Element element, BoundingBoxXYZ box)
    {
        var own = element.get_BoundingBox(null);
        if (own == null) return false;
        var toBox = box.Transform.Inverse;
        double x0 = double.MaxValue, y0 = double.MaxValue, z0 = double.MaxValue, x1 = double.MinValue, y1 = double.MinValue, z1 = double.MinValue;
        for (int k = 0; k < 8; k++)
        {
            var corner = toBox.OfPoint(new XYZ((k & 1) == 0 ? own.Min.X : own.Max.X, (k & 2) == 0 ? own.Min.Y : own.Max.Y, (k & 4) == 0 ? own.Min.Z : own.Max.Z));
            x0 = Math.Min(x0, corner.X); y0 = Math.Min(y0, corner.Y); z0 = Math.Min(z0, corner.Z);
            x1 = Math.Max(x1, corner.X); y1 = Math.Max(y1, corner.Y); z1 = Math.Max(z1, corner.Z);
        }
        return x1 >= box.Min.X && x0 <= box.Max.X && y1 >= box.Min.Y && y0 <= box.Max.Y && z1 >= box.Min.Z && z0 <= box.Max.Z;
    }

    // DirectContext3D 로 그린 것은 Revit 이 스스로 다시 그리지 않는다 — 문서의 안내대로 열린 뷰를 모두 다시 그리게 한다.
    private static string Refresh(UIDocument uidoc)
    {
        try { uidoc.UpdateAllOpenViews(); return ""; }
        catch (Exception first)
        {
            try { uidoc.RefreshActiveView(); return ""; }
            catch { return first.Message; }
        }
    }

    private static void Register(FormworkOverlay server)
    {
        var service = ExternalServiceRegistry.GetService(ExternalServices.BuiltInExternalServices.DirectContext3DService);
        if (service is not MultiServerService multi) throw new InvalidOperationException("3D 그리기 서비스(DirectContext3D)를 찾지 못했습니다.");
        if (!multi.IsRegisteredServerId(ServerId)) multi.AddServer(server);
        var active = multi.GetActiveServerIds();
        if (!active.Contains(ServerId))
        {
            active.Add(ServerId);
            multi.SetActiveServers(active);
        }
    }

    // 기록 파일에 한 줄 적는다. 그림 하나에 200줄까지만(단면 상자를 끌면 버퍼를 계속 다시 만든다).
    private static void Log(string line, bool restart = false)
    {
        try
        {
            if (Interlocked.Increment(ref _logLines) > 200) return;
            string path = LogPath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            string text = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture) + "  " + line + Environment.NewLine;
            if (restart) File.WriteAllText(path, text, new UTF8Encoding(true));
            else File.AppendAllText(path, text, new UTF8Encoding(false));
        }
        catch { }
    }

    private static void Fail(Scene scene, string where, Exception ex)
    {
        string message = where + ": " + ex.GetType().Name + " — " + ex.Message;
        if (scene.Error == message) return;      // 같은 오류를 그릴 때마다 적지 않는다
        scene.Error = message;
        Log("오류  " + message);
    }

    // ── IDirectContext3DServer ─────────────────────────────────────────────────────

    public Guid GetServerId() => ServerId;
    public ExternalServiceId GetServiceId() => ExternalServices.BuiltInExternalServices.DirectContext3DService;
    public string GetName() => "DH 거푸집 면 색";
    public string GetVendorId() => "DH";
    public string GetDescription() => "거푸집 면 가르기 결과를 3D 뷰에 색으로 그린다(모델은 바꾸지 않는다)";
    public string GetApplicationId() => "";
    public string GetSourceId() => "";
    public bool UsesHandles() => false;
    public bool UseInTransparentPass(View view) => false;

    public Outline GetBoundingBox(View view)
    {
        // 상자는 넉넉해도 된다(문서: 꼭 맞을 필요는 없다). 1ft 여유를 둔다 — 크기 0 인 상자를 주지 않게.
        // 여기서 난 오류도 Revit 으로 내보내지 않는다.
        try
        {
            var scene = _scene;
            return new Outline(new XYZ(scene.X0 - 1, scene.Y0 - 1, scene.Z0 - 1), new XYZ(scene.X1 + 1, scene.Y1 + 1, scene.Z1 + 1));
        }
        catch { return new Outline(new XYZ(-1, -1, -1), new XYZ(1, 1, 1)); }
    }

    public bool CanExecute(View view)
    {
        try
        {
            var scene = _scene;
            return scene.Triangles.Length > 0 && scene.Doc is { IsValidObject: true } doc && view is View3D && view.Document.Equals(doc);
        }
        catch { return false; }
    }

    public void RenderScene(View view, DisplayStyle displayStyle)
    {
        // 그리다 난 오류가 Revit 화면을 멈추게 하면 안 된다 — 전부 삼키고 기록만 남긴다.
        var scene = _scene;
        try
        {
            lock (_gate)
            {
                scene = _scene;     // 잠금을 기다리는 사이에 갈아 끼웠을 수 있다
                if (scene.Triangles.Length == 0) return;
                string clip = ReadClip(out var halves, out string clipNote);
                if (!ReferenceEquals(scene, _builtScene) || clip != _builtClip || !BuffersValid())
                {
                    if (ReferenceEquals(scene, _failedScene) && clip == _failedClip) return;   // 이 그림은 버퍼를 만들다 실패했다 — 그릴 때마다 되풀이하지 않는다
                    try { Build(scene, halves, clip, clipNote); }
                    catch (Exception ex)
                    {
                        Release();
                        _builtScene = null;
                        _failedScene = scene;
                        _failedClip = clip;
                        Fail(scene, "버퍼 만들기", ex);
                        return;
                    }
                }
                if (_format != null && _effect != null)
                    foreach (var chunk in _chunks)
                        DrawContext.FlushBuffer(chunk.Vertices, chunk.VertexCount, chunk.Indices, chunk.IndexCount, _format, _effect,
                            PrimitiveType.TriangleList, 0, chunk.TriangleCount);
                if (Interlocked.Increment(ref scene.Renders) == 1) Log($"처음 그림: 화면 표시 방식 {displayStyle} · {scene.Built}");
            }
        }
        catch (Exception ex) { Fail(scene, "그리기", ex); }
    }

    private bool BuffersValid()
    {
        if (_builtTriangles == 0) return true;      // 그릴 세모가 없다(단면 상자가 다 잘라 냈다)
        if (_format == null || !_format.IsValid() || _effect == null || !_effect.IsValid() || _chunks.Count == 0) return false;
        foreach (var chunk in _chunks)
            if (!chunk.Vertices.IsValid() || !chunk.Indices.IsValid()) return false;
        return true;
    }

    // 뷰의 자르는 평면(단면 상자)을 읽어 '남길 쪽'들로 바꾼다. 돌려주는 글자는 평면들이 바뀌었는지 견주는 데 쓴다.
    private static string ReadClip(out List<OverlayMath.Half> halves, out string note)
    {
        halves = new List<OverlayMath.Half>();
        note = "";
        IList<ClipPlane>? planes;
        try { planes = DrawContext.GetClipPlanes(); }
        catch { return ""; }
        if (planes == null || planes.Count == 0) return "";
        var raw = new List<(Pt origin, Pt normal)>(planes.Count);
        var key = new StringBuilder();
        foreach (var plane in planes)
        {
            try
            {
                XYZ o = plane.Origin, n = plane.Normal;
                raw.Add((new Pt(o.X, o.Y, o.Z), new Pt(n.X, n.Y, n.Z)));
                key.Append(string.Create(CultureInfo.InvariantCulture, $"{o.X:0.#####},{o.Y:0.#####},{o.Z:0.#####};{n.X:0.#####},{n.Y:0.#####},{n.Z:0.#####}|"));
            }
            catch { }
        }
        halves = OverlayMath.KeepSides(raw, out int unresolved);
        note = $"자르는 평면 {raw.Count}개(쓴 것 {halves.Count} · 방향을 몰라 안 쓴 것 {unresolved})";
        return key.ToString();
    }

    // 세모들을(필요하면 자르는 평면으로 잘라) 그리기 버퍼에 담는다 — 꼭짓점마다 위치와 색. RenderScene 안에서만 부른다.
    private void Build(Scene scene, List<OverlayMath.Half> halves, string clip, string clipNote)
    {
        Release();
        var triangles = OverlayMath.Clip(scene.Triangles, halves);
        const VertexFormatBits bits = VertexFormatBits.PositionColored;
        var colors = new Dictionary<int, ColorWithTransparency>();
        for (int start = 0; start < triangles.Count; start += ChunkTriangles)
        {
            int count = Math.Min(ChunkTriangles, triangles.Count - start);
            VertexBuffer? vertices = null;
            IndexBuffer? indices = null;
            try
            {
                int vertexFloats = count * 3 * VertexPositionColored.GetSizeInFloats();
                vertices = new VertexBuffer(vertexFloats);
                vertices.Map(vertexFloats);
                var stream = vertices.GetVertexStreamPositionColored();
                for (int i = start; i < start + count; i++)
                {
                    var t = triangles[i];
                    if (!colors.TryGetValue(t.Color, out var color))
                        colors[t.Color] = color = new ColorWithTransparency((uint)(t.Color >> 16 & 255), (uint)(t.Color >> 8 & 255), (uint)(t.Color & 255), 0);
                    stream.AddVertex(new VertexPositionColored(new XYZ(t.A.X, t.A.Y, t.A.Z), color));
                    stream.AddVertex(new VertexPositionColored(new XYZ(t.B.X, t.B.Y, t.B.Z), color));
                    stream.AddVertex(new VertexPositionColored(new XYZ(t.C.X, t.C.Y, t.C.Z), color));
                }
                vertices.Unmap();

                int indexShorts = count * IndexTriangle.GetSizeInShortInts();
                indices = new IndexBuffer(indexShorts);
                indices.Map(indexShorts);
                var indexStream = indices.GetIndexStreamTriangle();
                for (int i = 0; i < count; i++)
                    indexStream.AddTriangle(new IndexTriangle(3 * i, 3 * i + 1, 3 * i + 2));
                indices.Unmap();

                _chunks.Add(new Chunk { Vertices = vertices, Indices = indices, VertexCount = count * 3, IndexCount = indexShorts, TriangleCount = count });
            }
            catch
            {
                // 만들다 만 버퍼는 버린다(이미 담은 묶음은 부르는 쪽이 Release 로 치운다).
                try { vertices?.Dispose(); } catch { }
                try { indices?.Dispose(); } catch { }
                throw;
            }
        }
        if (triangles.Count > 0)
        {
            _format = new VertexFormat(bits);
            _effect = new EffectInstance(bits);
        }
        _builtScene = scene;
        _builtClip = clip;
        _builtTriangles = triangles.Count;
        scene.Built = $"세모 {scene.Triangles.Length}개 → 자른 뒤 {triangles.Count}개 · 버퍼 {_chunks.Count}개" +
                      $" · 꼭짓점 크기 {VertexPositionColored.GetSizeInFloats()} · 세모 인덱스 크기 {IndexTriangle.GetSizeInShortInts()}" +
                      (clipNote.Length > 0 ? " · " + clipNote : "");
        // 평면의 값(원점;법선)과 그리는 스레드를 적어 둔다 — 평면이 몇 개 오는지, 어느 좌표인지, 법선이 어디를 보는지는 Revit 에서만 알 수 있다.
        Log("버퍼 만듦: " + scene.Built + $" · 스레드 {Environment.CurrentManagedThreadId}" + (clip.Length > 0 ? " · 평면(원점;법선) " + clip : ""));
    }

    private void Release()
    {
        foreach (var chunk in _chunks)
        {
            try { chunk.Vertices.Dispose(); } catch { }
            try { chunk.Indices.Dispose(); } catch { }
        }
        _chunks.Clear();
        try { _format?.Dispose(); } catch { }
        try { _effect?.Dispose(); } catch { }
        _format = null;
        _effect = null;
        _builtTriangles = 0;
    }
}
