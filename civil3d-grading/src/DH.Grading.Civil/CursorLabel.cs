using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.GraphicsInterface;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace DH.Grading.Civil;

/// <summary>★★[v111.7.2 · JACK 1102 «마우스에 메모로 못 붙여?»] <b>자리 찍기 + 커서를 따라다니는 이름표(우리가 그린다)</b>.
/// <para>v111.7.1은 AutoCAD 풍선 글(<c>PointMonitor</c> · <c>AppendToolTipText</c>)을 썼는데 JACK 화면에 안 떴다 — 그 풍선은 AutoCAD 설정(롤오버 · 커서가 객체 위에 머묾)에 달렸다.
/// 이제 점 찍기를 <c>DrawJig</c>로 받아 커서 오른쪽 아래(화면 18 px · 18 px)에 이름표를 직접 그린다(화면 px 크기 — 확대 · 축소해도 같은 크기).</para>
/// <para>★★[JACK 1104] 꼴 = <b>흰 카드 + 검은 테 + 검은 글 한 줄</b>(옛 1103 짙은 회색 카드 · 주황 띠 · 둘째 줄 키 안내는 없앰 — <c>hint</c>는 받기만 하고 안 그림).
/// 모양은 모두 지그 그림(일시 그래픽)이라 도면에 아무것도 안 남는다(DBMOD 그대로) — 질문이 끝나면(점 · Enter · Esc · 오류) 지그와 함께 사라진다.
/// 크기는 Windows 배율(DPI)을 곱한다 — 150 %면 1.5배(화면 px이 실제 화소라 그대로 두면 작아 보임).</para>
/// 명령창 글 · Esc · Enter · 키워드 뜻은 <see cref="Editor.GetPoint(PromptPointOptions)"/>와 같고, 돌려주는 점도 GetPoint처럼 <b>UCS</b>다(부르는 쪽 UCS → WCS 변환 그대로).
/// <para>화면 없는 Civil(시험) · 기준점(고무줄) 질문 · 지그가 못 뜨면 이름표 없이 그냥 <c>GetPoint</c>(대본이 주는 자리 그대로).</para></summary>
internal static class CursorLabel
{
    /// <summary><see cref="PromptPointResult"/>와 같은 뜻 — <see cref="Value"/>는 UCS · 키워드면 <see cref="StringResult"/>.</summary>
    internal readonly record struct Result(PromptStatus Status, Point3d Value, string StringResult);

    /// <param name="label">카드 첫 줄(흰 글) — 예: «종단도 작성 위치 클릭».</param>
    /// <param name="hint">둘째 줄(작고 흐린 글) — 그 질문에서 듣는 키만(예: «Enter 지난 자리 · Esc 그만») · 빈 글이면 한 줄 카드.</param>
    /// <param name="extra">★[v112.4 · JACK 1104 «경로 찍다가 휠로 줌하면 전에 찍은 게 없어져»] 이름표 밑에 함께 그릴 것(WCS 커서 자리 · 커서를 받았나) — 지그가 화면마다 다시 그려 줌 · 확대 · 축소에도 안 사라짐(DrawVector는 Regen에서 사라짐).</param>
    internal static Result GetPoint(Editor ed, PromptPointOptions ppo, string label, string hint = "", System.Action<WorldDraw, Point3d, bool>? extra = null)
    {
        if (SelfCheckLog.Headless || ppo.UseBasePoint || string.IsNullOrWhiteSpace(label)) return Plain(ed, ppo);
        LabelJig jig;
        try { jig = new LabelJig(ed, ppo, label, hint ?? "") { Extra = extra }; }
        catch (System.Exception ex) { try { DiagLog.Append("\n[커서 이름표] 못 만듦 — 그냥 찍기: " + ex.Message); } catch { } return Plain(ed, ppo); }
        using (jig)
        {
            while (true)
            {
                PromptResult r;
                jig.LastStatus = PromptStatus.Cancel; jig.LastString = ""; jig.Asked = false;   // ★[검토 낮음] 지난 바퀴(Enter 다시 묻기)의 값이 이번 결과에 섞이지 않게
                try { r = ed.Drag(jig); }
                catch (System.Exception ex)
                {
                    // ★[설치 전 검토 · 낮음] 질문을 띄우기 전에 실패했으면 그냥 찍기로(사람은 한 번만 봄) · 이미 띄웠으면(입력을 받는 중 실패) 다시 묻지 않고 그만(같은 질문 두 번 안 띄움)
                    bool asked = jig.Asked;
                    try { DiagLog.Append("\n[커서 이름표] 지그 실패 — " + (asked ? "그만(이미 물음)" : "그냥 찍기") + ": " + ex.Message); } catch { }
                    return asked ? new Result(PromptStatus.Cancel, Point3d.Origin, "") : Plain(ed, ppo);
                }
                var st = r.Status;
                // 지그의 Sampler는 점이 아닌 입력(Enter · 키워드)에 Cancel을 돌려준다 — 그 입력이 무엇이었는지는 마지막 받은 값이 안다
                if (st != PromptStatus.OK && jig.LastStatus is PromptStatus.None or PromptStatus.Keyword) st = jig.LastStatus;
                if (st == PromptStatus.None && !ppo.AllowNone) continue;   // GetPoint(AllowNone = 거짓)처럼 Enter는 다시 묻기
                if (st == PromptStatus.OK) return new Result(st, jig.Point.TransformBy(ed.CurrentUserCoordinateSystem.Inverse()), "");
                if (st == PromptStatus.Keyword) return new Result(st, Point3d.Origin, jig.LastString.Length > 0 ? jig.LastString : r.StringResult ?? "");
                return new Result(st, Point3d.Origin, "");
            }
        }
    }

    static Result Plain(Editor ed, PromptPointOptions ppo)
    {
        var pr = ed.GetPoint(ppo);
        return new Result(pr.Status, pr.Value, pr.StringResult ?? "");
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern uint GetDpiForSystem();

    /// <summary>Windows 배율(96 dpi = 1) — 못 읽으면 1.</summary>
    static double DpiScale()
    {
        try { uint d = GetDpiForSystem(); return d >= 96 ? d / 96.0 : 1.0; } catch { return 1.0; }
    }

    // ── 카드 꼴(화면 px · 배율 1 기준 — 미리보기 그림과 같은 수) ──
    // ★[v112.3 · JACK 1104 «아래 Esc 취소 그런 건 다 없애»] 글 한 줄(둘째 줄 없음)
    // ★★[v112.4 · JACK 1104 스샷 «희미하고 멍텅구리 · 도화 느낌 나게(이름 · 로고는 넣지 말고)»] 원인 = 지그의 면 채우기를 AutoCAD가 반투명(50 %)으로 그림
    //   (스샷 화소: 흰 바탕 = 정확히 128 회색 · 글 · 선은 그대로) → 바탕을 면 채우기가 아니라 화면 1 화소 간격 가로선으로 깔아 불투명하게 ·
    //   색 = 도화 청록(DhBrand.BrandHi #0A6269) 바탕 + 흰 굵은 글 + 밝은 청록 테(검은 화면에서도 테가 보이게) + 왼쪽 옅은 청록 띠
    internal const double OffPx = 18, MainPx = 15, HintPx = 11, PadLPx = 11, PadRPx = 12, PadTPx = 7, PadBPx = 7, GapPx = 5, BarPx = 4, RadPx = 4;
    internal static readonly (byte R, byte G, byte B) CardRgb = (0x0A, 0x62, 0x69), EdgeRgb = (0x5F, 0xB3, 0xB8),
        AccentRgb = (0xA8, 0xE0, 0xE3), MainRgb = (0xFF, 0xFF, 0xFF), HintRgb = (0xD6, 0xEE, 0xEF);

    /// <summary>볼록 다각형을 가로선 y로 자른 구간(없으면 null).</summary>
    internal static (double A, double B)? SpanAt(List<(double X, double Y)> poly, double y)
    {
        double a = double.MaxValue, b = double.MinValue;
        for (int i = 0; i < poly.Count; i++)
        {
            var p = poly[i]; var q = poly[(i + 1) % poly.Count];
            if ((p.Y - y) * (q.Y - y) > 0 || System.Math.Abs(q.Y - p.Y) < 1e-15) continue;
            double x = p.X + (q.X - p.X) * (y - p.Y) / (q.Y - p.Y);
            a = System.Math.Min(a, x); b = System.Math.Max(b, x);
        }
        return b > a ? (a, b) : null;
    }
    /// <summary>둘째 줄(키 안내)을 그리나 — JACK 1104 «다 없애».</summary>
    internal const bool ShowHint = false;

    // ★[v112.3 · 1104 튕김(힙 손상 0xc0000374 · 덤프 없음)] 글꼴(TextStyle)은 한 번 만들어 계속 씀 — 지그마다 만들고 Drag 뒤 바로 지우던 것을 없앰
    //   (그린 뒤 AutoCAD 그래픽이 그 글꼴을 다시 볼 때 이미 지운 것이면 메모리가 깨질 수 있다 — 이번 튕김의 원인으로 확인된 것은 아님 · 지울 까닭이 없는 객체라 남겨 둠)
    static TextStyle? _sts;
    static TextStyle SharedStyle()
    {
        if (_sts != null) return _sts;
        var ts = new TextStyle();
        ts.Font = new FontDescriptor("Malgun Gothic", true, false, 129, 34);   // 129 = 한글 charset(ImportGisCommand와 같은 글꼴) · 굵게
        return _sts = ts;
    }

    /// <summary>둥근 사각형 둘레(왼쪽 위에서 시계 방향 · 모서리마다 짧은 호) — x0 &lt; x1 · yB &lt; yT.</summary>
    internal static List<(double X, double Y)> RoundRect(double x0, double yT, double x1, double yB, double r, int seg = 6)
    {
        r = System.Math.Max(0, System.Math.Min(r, System.Math.Min(x1 - x0, yT - yB) / 2));
        var pts = new List<(double, double)>();
        void Arc(double cx, double cy, double a0)
        {
            for (int i = 0; i <= seg; i++)
            {
                double a = (a0 - 90.0 * i / seg) * System.Math.PI / 180.0;
                pts.Add((cx + r * System.Math.Cos(a), cy + r * System.Math.Sin(a)));
            }
        }
        Arc(x0 + r, yT - r, 180); Arc(x1 - r, yT - r, 90); Arc(x1 - r, yB + r, 0); Arc(x0 + r, yB + r, -90);
        return pts;
    }

    /// <summary>볼록 다각형을 세로선 x = <paramref name="xc"/>로 자른 한쪽(<paramref name="left"/> = 왼쪽) — 주황 띠와 카드 몸통이 겹치지 않게(겹친 채우기의 그리는 차례에 기대지 않음).</summary>
    internal static List<(double X, double Y)> ClipX(List<(double X, double Y)> poly, double xc, bool left)
    {
        var o = new List<(double, double)>();
        bool In((double X, double Y) p) => left ? p.X <= xc : p.X >= xc;
        for (int i = 0; i < poly.Count; i++)
        {
            var a = poly[i]; var b = poly[(i + 1) % poly.Count];
            bool ia = In(a), ib = In(b);
            if (ia) o.Add(a);
            if (ia != ib && System.Math.Abs(b.X - a.X) > 1e-12)
            {
                double t = (xc - a.X) / (b.X - a.X);
                o.Add((xc, a.Y + (b.Y - a.Y) * t));
            }
        }
        return o;
    }

    sealed class LabelJig : DrawJig, System.IDisposable
    {
        readonly JigPromptPointOptions _opt;
        readonly string _label, _hint;
        internal System.Action<WorldDraw, Point3d, bool>? Extra;
        readonly Vector3d _right = Vector3d.XAxis, _up = Vector3d.YAxis, _normal = Vector3d.ZAxis;
        readonly TextStyle _ts = SharedStyle();
        readonly double _dpi = DpiScale();
        double _px;               // 화면 1 px = 도면 몇 단위(VIEWSIZE / SCREENSIZE.Y) × 배율
        Extents2d _ext, _extH;    // 글 범위(글 자리 기준 · 도면 단위)
        bool _has;
        internal Point3d Point;   // WCS(지그는 WCS로 받음)
        internal PromptStatus LastStatus = PromptStatus.Cancel;
        internal string LastString = "";
        internal bool Asked;      // 이번 Drag에서 질문을 띄웠나(Sampler가 한 번이라도 불림)

        internal LabelJig(Editor ed, PromptPointOptions ppo, string label, string hint)
        {
            _label = label; _hint = ShowHint ? hint.Trim() : "";
            _opt = new JigPromptPointOptions(ppo.Message)
            {
                UserInputControls = UserInputControls.Accept3dCoordinates | UserInputControls.NullResponseAccepted,
                AppendKeywordsToMessage = ppo.AppendKeywordsToMessage,
            };
            foreach (Keyword k in ppo.Keywords) _opt.Keywords.Add(k.GlobalName, k.LocalName, k.DisplayName, k.Visible, k.Enabled);
            // 화면 오른쪽 · 위 방향(평면이면 WCS X · Y) — 뷰 방향 · 비틀기에서
            try
            {
                using var v = ed.GetCurrentView();
                var n = v.ViewDirection.GetNormal();
                var m = Matrix3d.Rotation(-v.ViewTwist, n, Point3d.Origin) * Matrix3d.PlaneToWorld(n);
                _right = Vector3d.XAxis.TransformBy(m).GetNormal(); _up = Vector3d.YAxis.TransformBy(m).GetNormal(); _normal = n;
            }
            catch { }
            Resize(PixelSize());
        }

        double PixelSize()
        {
            try
            {
                double vs = System.Convert.ToDouble(AcadApp.GetSystemVariable("VIEWSIZE"));
                var sc = (Point2d)AcadApp.GetSystemVariable("SCREENSIZE");
                if (vs > 0 && sc.Y > 1) return vs / sc.Y * _dpi;
            }
            catch { }
            return 1.0;
        }

        static Extents2d Measure(TextStyle ts, string s)
        {
            Extents2d e;
            try { e = ts.ExtentsBox(s, true, true, null); }
            catch { e = new Extents2d(0, -0.2 * ts.TextSize, s.Length * ts.TextSize, ts.TextSize); }
            if (!(e.MaxPoint.X > e.MinPoint.X)) e = new Extents2d(0, -0.2 * ts.TextSize, s.Length * ts.TextSize, ts.TextSize);
            return e;
        }

        void Resize(double px)
        {
            _px = px;
            _ts.TextSize = MainPx * px;
            _ext = Measure(_ts, _label);
            _extH = new Extents2d(0, 0, 0, 0);
        }

        protected override SamplerStatus Sampler(JigPrompts prompts)
        {
            Asked = true;
            var r = prompts.AcquirePoint(_opt);
            LastStatus = r.Status; LastString = r.StringResult ?? "";
            if (r.Status != PromptStatus.OK) return SamplerStatus.Cancel;
            double px = PixelSize();
            bool zoomed = System.Math.Abs(px - _px) > 1e-9 * System.Math.Max(1.0, px);
            if (zoomed) Resize(px);
            if (_has && !zoomed && r.Value.IsEqualTo(Point)) return SamplerStatus.NoChange;
            Point = r.Value; _has = true;
            return SamplerStatus.OK;
        }

        protected override bool WorldDraw(WorldDraw draw)
        {
            if (Extra != null) try { Extra(draw, Point, _has); } catch { }
            if (!_has) return true;
            try
            {
                double u = _px;
                Point3d P(double x, double y) => Point + _right * x + _up * y;
                Point3dCollection Pc(List<(double X, double Y)> q) { var c = new Point3dCollection(); foreach (var p in q) c.Add(P(p.X, p.Y)); return c; }
                bool two = _hint.Length > 0;
                double wM = _ext.MaxPoint.X - _ext.MinPoint.X, wH = two ? _extH.MaxPoint.X - _extH.MinPoint.X : 0;
                // 줄 높이는 글자 크기(글자마다 다른 실제 범위가 아니라) — 두 줄 간격이 이름마다 안 흔들리게
                double hM = MainPx * u, hH = HintPx * u;
                double x0 = OffPx * u, yT = -OffPx * u;
                double xT = x0 + (BarPx + PadLPx) * u;                                   // 글 왼쪽 끝
                double x1 = xT + System.Math.Max(wM, wH) + PadRPx * u;
                double yB = yT - (PadTPx * u + hM + (two ? GapPx * u + hH : 0) + PadBPx * u);
                var card = RoundRect(x0, yT, x1, yB, RadPx * u);
                var tr = draw.SubEntityTraits;
                try { tr.Transparency = new Autodesk.AutoCAD.Colors.Transparency(255); } catch { }   // 불투명(되는 판이면)
                tr.FillType = FillType.FillNever;
                // ★★[v112.4] 바탕 = 화면 1 화소마다 가로선(면 채우기는 지그에서 반투명으로 나옴) · 왼쪽 띠는 다른 색
                double step = System.Math.Max(u / System.Math.Max(_dpi, 1.0), 1e-9) * 0.9, xBar = x0 + BarPx * u;
                var bodyC = new EntityColor(CardRgb.R, CardRgb.G, CardRgb.B); var barC = new EntityColor(AccentRgb.R, AccentRgb.G, AccentRgb.B);
                for (double y = yB; y <= yT + 1e-12; y += step)
                {
                    if (SpanAt(card, y) is not (double a, double b)) continue;
                    if (BarPx > 0 && a < xBar)
                    {
                        tr.TrueColor = barC; draw.Geometry.WorldLine(P(a, y), P(System.Math.Min(xBar, b), y));
                        if (b <= xBar) continue;
                        a = xBar;
                    }
                    tr.TrueColor = bodyC; draw.Geometry.WorldLine(P(a, y), P(b, y));
                }
                tr.TrueColor = new EntityColor(EdgeRgb.R, EdgeRgb.G, EdgeRgb.B);         // 밝은 청록 테
                var edge = Pc(card); edge.Add(edge[0]);
                draw.Geometry.Polyline(edge, _normal, System.IntPtr.Zero);
                // 글 — 줄 칸(높이 = 글자 크기) 가운데에 실제 글자 범위의 가운데를 맞춤(글 자리 = 바닥선 왼쪽)
                tr.TrueColor = new EntityColor(MainRgb.R, MainRgb.G, MainRgb.B);
                double yM = yT - PadTPx * u - hM;
                draw.Geometry.Text(P(xT - _ext.MinPoint.X, yM + 0.5 * (hM - _ext.MaxPoint.Y - _ext.MinPoint.Y)), _normal, _right, _label, true, _ts);
            }
            catch { }
            return true;
        }

        /// <summary>★[v112.3] 글꼴은 함께 쓰는 것이라 안 지움(<see cref="SharedStyle"/>).</summary>
        public void Dispose() { }
    }
}
