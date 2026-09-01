using System;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using DH.Grading.Core;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace DH.Grading.Civil.Commands;

/// <summary>★★★[JACK 0901 "Dmap처럼 civil3d에서 누르면 지도를 뜨게 하고 거기서 박스 치면"
/// "캐드에 박스가 그려지게 할 수 없나?"]
///
/// <para><b>빈 도면에서는 클릭할 근거가 없다.</b> CAD는 검은 화면에서 시작하는데
/// 배경지도·등고선·지적도는 전부 "두 점을 클릭하라"로 시작한다 — 닭이 먼저냐 달걀이 먼저냐다.
/// 지도를 띄워 거기서 고르면 그 고리가 끊긴다.</para>
///
/// <para><b>왜 브라우저인가.</b> AutoCAD 안에 웹뷰를 넣으려면 <c>WebView2</c>를 참조해야 하는데
/// AutoCAD 자신이 쓰는 것과 판이 부딪힐 수 있다. 기본 브라우저를 띄우고
/// <b>localhost로 결과만 받아오면</b> 새 의존성이 없고 어느 PC에서나 돈다.</para>
///
/// <para><b>지도는 VWorld</b>(국토부)다 — 이 애드인이 이미 위성영상을 거기서 받고 있어
/// 키·약관이 정리돼 있고 항공사진·지적도를 같이 켤 수 있다.
/// 받아 오는 것은 <b>박스의 위경도 네 값뿐</b>이다 — 타일 그림은 안 가져온다.</para>
///
/// <para>★★<b>기다리는 동안 CAD가 굳으면 안 된다</b>(검토 0901 HIGH). 브라우저를 띄워 놓고
/// 그냥 기다리면 Windows가 5초 만에 "응답 없음"을 붙인다 — 화면이 하얘지고 Esc도 안 먹는다.
/// 그래서 <b>작은 창</b>을 띄워 그 안에서 기다린다. 창은 메시지를 스스로 돌리므로 CAD가 살아 있고,
/// 지도가 아예 안 떠도 <b>여기서 그만둘 수 있다</b>.</para></summary>
public static class MapPickCommand
{
    internal const string BoxLayer = "DH-범위(지도)";

    /// <summary>중앙자오선에서 이만큼 넘게 떨어지면 <b>원점이 틀렸다</b>고 본다(도).
    /// <para>한국 원점은 1° 간격이고 도엽도 그렇게 나뉜다. 넘어가면 TM 급수가 벌어져
    /// 수십 mm씩 틀어진다 — 하니스 S92가 그 한계를 재 두었다(2°에서 0.02mm, 8°에서 44mm).</para></summary>
    private const double BeltTolDeg = 2.0;

    /// <summary>지도를 기다리는 한도. 너무 길면 잊고 놔둔 창이 포트를 물고 있는다.</summary>
    private static readonly TimeSpan WaitLimit = TimeSpan.FromMinutes(10);

    /// <summary>지도가 어떻게 끝났는가 — <b>넷을 구별한다</b>.
    /// <para>★★검토 0901: 셋을 다 "그만두었다"로 뭉치면
    /// <b>진짜 고장이 사용자의 선택으로 보고된다</b> — 그러면 영영 보고가 안 올라온다.</para></summary>
    internal enum PickEnd
    {
        /// <summary>범위를 받았다.</summary>
        Got,
        /// <summary>사용자가 그만두었다 — 두 점 클릭으로 물러나면 안 된다.</summary>
        Cancelled,
        /// <summary>한도까지 아무것도 안 왔다.</summary>
        TimedOut,
        /// <summary>왔는데 못 읽었다 — 이건 우리 고장이다.</summary>
        Broken,
    }

    /// <summary>★ 지도를 띄워 범위를 받아 <b>도면 좌표(TM)</b>로 돌려준다 — 그리지도 옮기지도 않는다.
    /// <para>쓸 좌표계는 <b>부르는 쪽이 정한다</b>. ★★박스와 조회가 같은 원점이어야 한다 —
    /// 등고선·지적도는 도면 좌표계를 먼저 보고 없을 때 정지설정을 쓰므로
    /// (<see cref="ImportGisCommand.ResolveEpsg"/>) 여기서 정지설정만 보면 둘이 어긋나
    /// <b>수십 km 옆의 지형</b>을 받아 온다 — 화면만 봐서는 모르는 종류다.</para>
    /// <param name="why">못 얻은 이유. 사용자가 <b>그만둔 것</b>이면 <c>null</c>이다.</param></summary>
    internal static PickEnd TryPick(Editor ed, int epsg, out double x0, out double y0,
                                    out double x1, out double y1, out string why)
    {
        x0 = y0 = x1 = y1 = 0;
        why = null;

        var belt = ShapefileWriter.Belt(epsg);
        if (belt == null)
        {
            why = $"좌표계 EPSG:{epsg}를 아직 못 다룹니다(5180~5188 중에서 고르세요)";
            return PickEnd.Broken;
        }
        double cm = belt.Value.cm, fn = belt.Value.fn;
        ed.WriteMessage($"\n[지도범위] 좌표계 EPSG:{epsg}({belt.Value.name}) · 중앙자오선 {cm}°"
                      + " — 이 도면이 쓰는 원점입니다.");

        Box got;
        try
        {
            using var server = new BoxServer(cm);
            ed.WriteMessage($"\n[지도범위] 브라우저에서 지도를 엽니다 — {server.Url}"
                          + "\n  지도에서 모서리 두 곳을 클릭해 범위를 정하고 [이 범위 보내기]를 누르세요.");
            server.OpenBrowser(ed);
            // ★굳지 않게 <b>창 안에서</b> 기다린다 — 창이 메시지를 돌려 주므로 CAD가 살아 있다.
            got = MapWaitWindow.Run(server, WaitLimit, out PickEnd how);
            if (how != PickEnd.Got)
            {
                if (how == PickEnd.TimedOut) why = $"지도에서 {WaitLimit.TotalMinutes:F0}분 동안 범위가 오지 않았습니다";
                else if (how == PickEnd.Broken) why = "지도가 보낸 값을 읽지 못했습니다";
                return how;
            }
        }
        catch (System.Exception ex) { why = ex.Message; return PickEnd.Broken; }

        return ToTm(ed, epsg, got, out x0, out y0, out x1, out y1) ? PickEnd.Got : PickEnd.Broken;
    }

    /// <summary>★위경도 박스 → <b>도면 좌표(TM)</b>. 원점이 수상하면 그것도 여기서 말한다.
    /// <para>브라우저 경로와 도킹바 경로가 <b>같은 이 함수</b>를 쓴다(§50) —
    /// 옮기는 규칙이 두 벌이 되면 한쪽만 고쳐 놓고 왜 다른지 모르게 된다.</para></summary>
    internal static bool ToTm(Editor ed, int epsg, Box got,
                              out double x0, out double y0, out double x1, out double y1)
    {
        x0 = y0 = x1 = y1 = 0;
        var belt = ShapefileWriter.Belt(epsg);
        if (belt == null || got == null) return false;
        double cm = belt.Value.cm, fn = belt.Value.fn;

        // ── 위경도 → TM. 네 모서리에 <b>위·아래변 한가운데</b>를 더해 경계를 잡는다.
        //   ★위도가 같은 선은 TM에서 <b>휜다</b> — 아래변의 최소 N은 모서리가 아니라
        //     중앙자오선을 지나는 가운데다. 20km 박스에서 5.8m, 50km면 36m를 놓친다(검토 0901).
        double midLon = (got.MinLon + got.MaxLon) / 2.0;
        double off = Math.Abs(midLon - cm);
        var pts = new[]
        {
            KoreaTm.FromLonLat(got.MinLon, got.MinLat, cm, fn),
            KoreaTm.FromLonLat(got.MaxLon, got.MinLat, cm, fn),
            KoreaTm.FromLonLat(got.MinLon, got.MaxLat, cm, fn),
            KoreaTm.FromLonLat(got.MaxLon, got.MaxLat, cm, fn),
            KoreaTm.FromLonLat(midLon,     got.MinLat, cm, fn),   // ★아래변이 휘어 내려간 자리
            KoreaTm.FromLonLat(midLon,     got.MaxLat, cm, fn),
        };
        x0 = double.MaxValue; y0 = double.MaxValue; x1 = double.MinValue; y1 = double.MinValue;
        foreach (var (e, n) in pts)
        {
            x0 = Math.Min(x0, e); x1 = Math.Max(x1, e);
            y0 = Math.Min(y0, n); y1 = Math.Max(y1, n);
        }

        // ★★<b>원점이 맞는지 먼저 말한다.</b> 틀린 원점으로 옮기면 좌표는 그럴듯한데
        //   자리가 수십 km 어긋난다 — 화면만 봐서는 알 수 없는 종류다.
        if (off > BeltTolDeg)
            ed.WriteMessage($"\n  ⚠⚠고른 자리(경도 {midLon:F2}°)가 중앙자오선({cm}°)에서 {off:F1}° 떨어져 있습니다."
                          + "\n     도면·정지설정의 좌표계가 이 지역이 아닐 수 있습니다"
                          + " — 5185(서부125)·5186(중부127)·5187(동부129)·5188(동해131).");

        ed.WriteMessage($"\n[지도범위] {x0:F1},{y0:F1} ~ {x1:F1},{y1:F1}"
                      + $" (가로 {x1 - x0:F0}m × 세로 {y1 - y0:F0}m)");
        try
        {
            DiagLog.Append($"\n[지도범위] EPSG:{epsg}(중앙자오선 {cm}° · 원점가산 {fn}) · 위경도 "
                         + $"{got.MinLon:F6},{got.MinLat:F6} ~ {got.MaxLon:F6},{got.MaxLat:F6}"
                         + $" → TM {x0:F2},{y0:F2} ~ {x1:F2},{y1:F2}"
                         + (off > BeltTolDeg ? $"  ⚠중앙자오선에서 {off:F1}° 떨어짐" : ""));
        }
        catch { }
        return true;
    }

    [CommandMethod("DHMAPPICK", CommandFlags.Modal)]
    public static void Run()
    {
        var doc = AcadApp.DocumentManager.MdiActiveDocument;
        if (doc == null) return;
        GradingSettings.SyncToDocument(doc);
        var ed = doc.Editor;
        var db = doc.Database;

        // ★★<b>등고선이 쓸 원점과 같아야 한다</b>(검토 0901 HIGH). 여기서 정지설정만 보면
        //   이 사각형은 A원점에 그려지는데 [서버 지표면]은 B원점으로 읽어 178km 옆을 받아 온다.
        int epsg = ImportGisCommand.ResolveEpsg(db, out string csNote);
        ed.WriteMessage($"\n[지도범위] 좌표계: {csNote}");

        // ★한 곳에서만 판단한다(§50) — 지도 띄우기·위경도→TM은 전부 TryPick 안에 있다.
        var end = TryPick(ed, epsg, out double x0, out double y0, out double x1, out double y1, out string why);
        if (end != PickEnd.Got)
        {
            ed.WriteMessage(end == PickEnd.Cancelled
                ? "\n[지도범위] 취소되었습니다 — 도면은 그대로 둡니다."
                : "\n[지도범위] 범위를 못 받았습니다 — " + why
                  + "\n  기존처럼 도면에서 두 점을 클릭하는 방법을 쓰세요.");
            return;
        }
        if (x1 - x0 < 1.0 || y1 - y0 < 1.0)
        {
            ed.WriteMessage("\n[지도범위] 고른 범위가 너무 작습니다(1m 미만) — 다시 골라 주세요.");
            return;
        }

        // ── 도면에 사각형을 그리고 그리로 화면을 옮긴다.
        //   ★<b>옮기기만 하면 검은 화면에서 검은 화면으로 갈 뿐</b>이라 볼 것을 같이 그린다.
        int wiped = 0;
        try
        {
            using var dl = doc.LockDocument();
            using var tr = db.TransactionManager.StartTransaction();
            var ms = (BlockTableRecord)tr.GetObject(
                SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
            var lay = SectionCommand.EnsureLayer(db, tr, BoxLayer, 30);

            // 지난번 것을 지운다 — 안 지우면 겹겹이 쌓인다(이 저장소가 여러 번 데인 자리).
            // ★★<b>훑으면서 지우지 않는다</b> — 모아 두고 나서 지운다(EraseOnLayers와 같은 방식).
            //   훑는 중에 지우면 건너뛰는 것이 생기는데 catch{}가 삼켜 조용히 쌓인다(검토 0901).
            var doomed = new System.Collections.Generic.List<ObjectId>();
            foreach (ObjectId id in ms)
            {
                try
                {
                    if (tr.GetObject(id, OpenMode.ForRead) is Entity e0 && e0.LayerId == lay) doomed.Add(id);
                }
                catch { }
            }
            foreach (var id in doomed)
            {
                try { ((Entity)tr.GetObject(id, OpenMode.ForWrite)).Erase(); wiped++; }
                catch { }
            }

            var pl = new Polyline();
            pl.AddVertexAt(0, new Point2d(x0, y0), 0, 0, 0);
            pl.AddVertexAt(1, new Point2d(x1, y0), 0, 0, 0);
            pl.AddVertexAt(2, new Point2d(x1, y1), 0, 0, 0);
            pl.AddVertexAt(3, new Point2d(x0, y1), 0, 0, 0);
            pl.Closed = true;
            if (!lay.IsNull) pl.LayerId = lay;
            ms.AppendEntity(pl);
            tr.AddNewlyCreatedDBObject(pl, true);
            tr.Commit();
        }
        catch (System.Exception ex)
        {
            ed.WriteMessage("\n  ⚠범위 사각형을 못 그렸습니다 — " + ex.Message);
        }

        bool moved = true;
        try
        {
            double w = Math.Max(1.0, x1 - x0), h = Math.Max(1.0, y1 - y0);
            using var vtr = new ViewTableRecord
            {
                CenterPoint = new Point2d((x0 + x1) / 2, (y0 + y1) / 2),
                Width = w * 1.2,
                Height = h * 1.2,
            };
            ed.SetCurrentView(vtr);
        }
        catch { moved = false; }   // ★조용히 넘기면 "가져왔다"는데 화면은 그대로다

        ed.WriteMessage($"\n[지도범위] 가져왔습니다 — {x0:F1},{y0:F1} ~ {x1:F1},{y1:F1}"
                      + $" (가로 {x1 - x0:F0}m × 세로 {y1 - y0:F0}m)"
                      + (wiped > 0 ? $" · 지난 사각형 {wiped}개 지움" : "")
                      + (moved ? "" : "\n  ⚠화면은 못 옮겼습니다 — ZOOM E(범위)로 찾으세요.")
                      + "\n  이제 [배경지도]·[등고선]·[지적도]에서 이 사각형 두 모서리를 스냅으로 찍으시면 됩니다.");
    }

    internal sealed class Box
    {
        public double MinLon, MinLat, MaxLon, MaxLat;
    }

    /// <summary>브라우저에 지도를 내주고 <b>박스 하나</b>를 받아 오는 아주 작은 서버.
    /// <para>★<b>localhost에만</b> 연다 — 바깥에서 들어올 수 없다. 박스를 받거나
    /// 사용자가 그만두면 바로 닫는다.</para>
    /// <para>★★<b>주소에 한 번 쓰는 표를 붙인다</b>(검토 0901). 표가 없으면
    /// 다른 탭에 떠 있는 아무 웹페이지나 포트를 훑어 <c>/box</c>에 좌표를 밀어 넣을 수 있다 —
    /// 그러면 현장이 조용히 바뀐다. 표를 모르면 못 넣는다.</para></summary>
    internal sealed class BoxServer : IDisposable
    {
        /// <summary>보낸 몸통이 이보다 크면 우리 것이 아니다 — 통째로 메모리에 올리지 않는다.</summary>
        private const int MaxBody = 4096;

        private readonly HttpListener _http = new();
        private readonly ManualResetEventSlim _done = new(false);
        private readonly string _html;
        private readonly string _token;
        private readonly Task _loop;
        private Box _box;
        private volatile bool _stop;
        private volatile bool _cancelled;
        private volatile bool _broken;

        public string Url { get; }
        public bool IsDone => _done.IsSet;

        public BoxServer(double cm)
        {
            _token = Guid.NewGuid().ToString("N");
            _html = MapPage.Build(cm, _token);
            int port = FreePort();
            Url = $"http://localhost:{port}/{_token}/";
            _http.Prefixes.Add(Url);
            // 프록시·PAC가 Host를 127.0.0.1로 바꿔 보내는 자리가 있다 — 둘 다 열어 둔다.
            try { _http.Prefixes.Add($"http://127.0.0.1:{port}/{_token}/"); } catch { }
            _http.Start();
            _loop = Task.Run(Loop);
        }

        public void OpenBrowser(Editor ed)
        {
            try
            {
                System.Diagnostics.Process.Start(
                    new System.Diagnostics.ProcessStartInfo(Url) { UseShellExecute = true });
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage("\n  ⚠브라우저를 못 열었습니다 — 위 주소를 직접 붙여 넣으세요. " + ex.Message);
            }
        }

        private void Loop()
        {
            while (!_stop)
            {
                HttpListenerContext ctx;
                try { ctx = _http.GetContext(); }
                catch { break; }

                bool finish = false;
                try
                {
                    string path = ctx.Request.Url?.AbsolutePath ?? "/";
                    string tail = path.StartsWith("/" + _token, StringComparison.Ordinal)
                                ? path.Substring(_token.Length + 1) : null;

                    // 남의 페이지가 부른 것이면 아무것도 안 한다(표를 알아도 Origin이 다르면 거부).
                    string origin = ctx.Request.Headers["Origin"];
                    bool foreign = !string.IsNullOrEmpty(origin)
                                && origin.IndexOf("localhost", StringComparison.OrdinalIgnoreCase) < 0
                                && origin.IndexOf("127.0.0.1", StringComparison.Ordinal) < 0;

                    if (tail == null || foreign)
                    {
                        ctx.Response.StatusCode = 404;
                        Send(ctx, "text/plain; charset=utf-8", "no");
                    }
                    else if (tail == "/box" && ctx.Request.HttpMethod == "POST")
                    {
                        _box = Parse(ReadBody(ctx.Request));
                        if (_box == null) _broken = true;
                        finish = true;
                        Send(ctx, "text/plain; charset=utf-8", "OK");
                    }
                    else if (tail == "/cancel")
                    {
                        _cancelled = true;
                        finish = true;
                        Send(ctx, "text/plain; charset=utf-8", "OK");
                    }
                    else Send(ctx, "text/html; charset=utf-8", _html);
                }
                catch { }
                finally
                {
                    // ★★<b>답장이 실패해도 결과는 살린다</b>(검토 0901 HIGH). 사용자가 탭을 반 초 먼저
                    //   닫으면 Send가 터지는데, 예전에는 그 바람에 _done.Set()을 못 해
                    //   멀쩡한 박스를 손에 쥔 채로 10분을 기다렸다.
                    if (finish) { try { _done.Set(); } catch { } }
                }
            }
        }

        /// <summary>기다린다. 못 받았으면 <c>null</c>이고 <paramref name="how"/>가 이유를 말한다.</summary>
        public Box Wait(TimeSpan limit, out PickEnd how)
        {
            if (!_done.Wait(limit)) { how = PickEnd.TimedOut; return null; }
            if (_cancelled) { how = PickEnd.Cancelled; return null; }
            if (_broken || _box == null) { how = PickEnd.Broken; return null; }
            how = PickEnd.Got;
            return _box;
        }

        /// <summary>사용자가 CAD 쪽 창에서 그만두었다.</summary>
        public void CancelFromCad()
        {
            _cancelled = true;
            try { _done.Set(); } catch { }
        }

        private static string ReadBody(HttpListenerRequest req)
        {
            var buf = new byte[MaxBody];
            int n = 0;
            using var s = req.InputStream;
            while (n < buf.Length)
            {
                int r = s.Read(buf, n, buf.Length - n);
                if (r <= 0) break;
                n += r;
            }
            return Encoding.UTF8.GetString(buf, 0, n);
        }

        private static void Send(HttpListenerContext ctx, string type, string body)
        {
            var bytes = Encoding.UTF8.GetBytes(body);
            ctx.Response.ContentType = type;
            ctx.Response.ContentLength64 = bytes.Length;
            ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
            ctx.Response.OutputStream.Close();
        }

        /// <summary>아주 단순한 JSON 읽기 — <b>우리가 보내는 네 값만</b> 받는다.
        /// <para>이것 하나 때문에 JSON 라이브러리를 늘리지 않는다.</para></summary>
        internal static Box Parse(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            double G(string key)
            {
                int i = s.IndexOf("\"" + key + "\"", StringComparison.Ordinal);
                if (i < 0) return double.NaN;
                i = s.IndexOf(':', i);
                if (i < 0) return double.NaN;
                int j = i + 1;
                while (j < s.Length && char.IsWhiteSpace(s[j])) j++;   // 탭·줄바꿈도 넘긴다
                int k = j;
                while (k < s.Length && (char.IsDigit(s[k]) || s[k] == '.' || s[k] == '-'
                                        || s[k] == '+' || s[k] == 'e' || s[k] == 'E')) k++;
                if (k == j) return double.NaN;
                return double.TryParse(s.Substring(j, k - j),
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out double v) ? v : double.NaN;
            }
            var b = new Box { MinLon = G("minLon"), MinLat = G("minLat"), MaxLon = G("maxLon"), MaxLat = G("maxLat") };
            if (double.IsNaN(b.MinLon) || double.IsNaN(b.MinLat)
                || double.IsNaN(b.MaxLon) || double.IsNaN(b.MaxLat)) return null;
            if (b.MinLon > b.MaxLon || b.MinLat > b.MaxLat) return null;
            // 남한 밖이면 우리 것이 아니다 — 표를 뚫고 들어온 값을 여기서 한 번 더 막는다.
            if (b.MinLon < 122 || b.MaxLon > 134 || b.MinLat < 32 || b.MaxLat > 40) return null;
            return b;
        }

        private static int FreePort()
        {
            var l = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
            l.Start();
            int p = ((System.Net.IPEndPoint)l.LocalEndpoint).Port;
            l.Stop();
            return p;
        }

        public void Dispose()
        {
            _stop = true;
            try { _http.Stop(); } catch { }
            try { _http.Close(); } catch { }
            try { _loop?.Wait(1000); } catch { }   // 돌던 것이 끝난 뒤에 치운다
            try { _done.Dispose(); } catch { }
        }
    }

    /// <summary>지도를 기다리는 <b>작은 창</b>.
    /// <para>★★이 창이 있어야 CAD가 안 굳는다 — 창이 메시지를 돌려 주기 때문이다.
    /// 지도가 아예 안 뜨는 자리(사내망이 CDN을 막는 등)에서도 <b>여기서 그만둘 수 있다</b>.</para></summary>
    private static class MapWaitWindow
    {
        public static Box Run(BoxServer server, TimeSpan limit, out PickEnd how)
        {
            var win = new System.Windows.Window
            {
                Title = "지도에서 범위 고르기",
                Width = 460,
                SizeToContent = System.Windows.SizeToContent.Height,
                WindowStartupLocation = System.Windows.WindowStartupLocation.CenterScreen,
                ResizeMode = System.Windows.ResizeMode.NoResize,
                ShowInTaskbar = false,
            };
            var sp = new System.Windows.Controls.StackPanel { Margin = new System.Windows.Thickness(16) };
            sp.Children.Add(new System.Windows.Controls.TextBlock
            {
                Text = "브라우저에 지도가 열렸습니다."
                     + "\n\n지도에서 모서리 두 곳을 클릭해 범위를 정하고"
                     + "\n[이 범위 보내기]를 누르세요.",
                Margin = new System.Windows.Thickness(0, 0, 0, 12),
            });
            sp.Children.Add(new System.Windows.Controls.TextBlock
            {
                Text = "지도가 안 뜨면 이 주소를 브라우저에 붙여 넣으세요:",
                Margin = new System.Windows.Thickness(0, 0, 0, 4),
            });
            sp.Children.Add(new System.Windows.Controls.TextBox
            {
                Text = server.Url,
                IsReadOnly = true,
                Margin = new System.Windows.Thickness(0, 0, 0, 12),
            });

            var row = new System.Windows.Controls.StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
            };
            var reopen = new System.Windows.Controls.Button
            {
                Content = "브라우저 다시 열기",
                Padding = new System.Windows.Thickness(12, 4, 12, 4),
                Margin = new System.Windows.Thickness(0, 0, 8, 0),
            };
            var quit = new System.Windows.Controls.Button
            {
                Content = "그만두기",
                Padding = new System.Windows.Thickness(12, 4, 12, 4),
                IsCancel = true,
            };
            row.Children.Add(reopen);
            row.Children.Add(quit);
            sp.Children.Add(row);
            win.Content = sp;

            bool quitPressed = false;
            reopen.Click += (_, __) =>
            {
                try
                {
                    System.Diagnostics.Process.Start(
                        new System.Diagnostics.ProcessStartInfo(server.Url) { UseShellExecute = true });
                }
                catch { }
            };
            quit.Click += (_, __) => { quitPressed = true; win.Close(); };

            // 지도가 값을 보내면 창이 스스로 닫힌다.
            var started = System.Diagnostics.Stopwatch.StartNew();
            bool timedOut = false;
            var timer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(200),
            };
            timer.Tick += (_, __) =>
            {
                if (server.IsDone) { win.Close(); return; }
                if (started.Elapsed >= limit) { timedOut = true; win.Close(); }
            };
            win.Closed += (_, __) => timer.Stop();
            timer.Start();

            try { AcadApp.ShowModalWindow(win); }
            catch { try { win.ShowDialog(); } catch { } }
            timer.Stop();

            if (quitPressed) { server.CancelFromCad(); how = PickEnd.Cancelled; return null; }
            if (timedOut) { how = PickEnd.TimedOut; return null; }
            return server.Wait(TimeSpan.Zero, out how);
        }
    }
}
