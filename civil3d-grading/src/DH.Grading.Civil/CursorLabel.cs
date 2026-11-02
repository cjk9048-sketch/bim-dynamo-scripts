using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.GraphicsInterface;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace DH.Grading.Civil;

/// <summary>★★[v111.7.2 · JACK 1102 «마우스에 메모로 못 붙여?»] <b>자리 찍기 + 커서를 따라다니는 이름표(우리가 그린다)</b>.
/// <para>v111.7.1은 AutoCAD 풍선 글(<c>PointMonitor</c> · <c>AppendToolTipText</c>)을 썼는데 JACK 화면에 안 떴다 — 그 풍선은 AutoCAD 설정(롤오버 · 커서가 객체 위에 머묾)에 달렸다.
/// 이제 점 찍기를 <c>DrawJig</c>로 받아 커서 오른쪽 아래(화면 18 px · 18 px)에 <b>어두운 상자 + 노란 글</b>(화면 14 px 높이 — 확대 · 축소해도 같은 크기)을 직접 그린다.
/// 명령창 글 · Esc · Enter · 키워드 뜻은 <see cref="Editor.GetPoint(PromptPointOptions)"/>와 같고, 돌려주는 점도 GetPoint처럼 <b>UCS</b>다(부르는 쪽 UCS → WCS 변환 그대로).</para>
/// <para>화면 없는 Civil(시험) · 기준점(고무줄) 질문 · 지그가 못 뜨면 이름표 없이 그냥 <c>GetPoint</c>(대본이 주는 자리 그대로).</para></summary>
internal static class CursorLabel
{
    /// <summary><see cref="PromptPointResult"/>와 같은 뜻 — <see cref="Value"/>는 UCS · 키워드면 <see cref="StringResult"/>.</summary>
    internal readonly record struct Result(PromptStatus Status, Point3d Value, string StringResult);

    internal static Result GetPoint(Editor ed, PromptPointOptions ppo, string label)
    {
        if (SelfCheckLog.Headless || ppo.UseBasePoint || string.IsNullOrWhiteSpace(label)) return Plain(ed, ppo);
        LabelJig jig;
        try { jig = new LabelJig(ed, ppo, label); }
        catch (System.Exception ex) { try { DiagLog.Append("\n[커서 이름표] 못 만듦 — 그냥 찍기: " + ex.Message); } catch { } return Plain(ed, ppo); }
        using (jig)
        {
            while (true)
            {
                PromptResult r;
                jig.LastStatus = PromptStatus.Cancel; jig.LastString = "";   // ★[검토 낮음] 지난 바퀴(Enter 다시 묻기)의 값이 이번 결과에 섞이지 않게
                try { r = ed.Drag(jig); }
                catch (System.Exception ex) { try { DiagLog.Append("\n[커서 이름표] 지그 실패 — 그냥 찍기: " + ex.Message); } catch { } return Plain(ed, ppo); }
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

    sealed class LabelJig : DrawJig, System.IDisposable
    {
        const double OffPx = 18, TextPx = 14, PadPx = 4;
        readonly JigPromptPointOptions _opt;
        readonly string _label;
        readonly Vector3d _right = Vector3d.XAxis, _up = Vector3d.YAxis, _normal = Vector3d.ZAxis;
        readonly TextStyle _ts = new();
        double _px;               // 화면 1 px = 도면 몇 단위(VIEWSIZE / SCREENSIZE.Y)
        Extents2d _ext;           // 글 범위(글 자리 기준 · 도면 단위)
        bool _has;
        internal Point3d Point;   // WCS(지그는 WCS로 받음)
        internal PromptStatus LastStatus = PromptStatus.Cancel;
        internal string LastString = "";

        internal LabelJig(Editor ed, PromptPointOptions ppo, string label)
        {
            _label = label;
            _opt = new JigPromptPointOptions(ppo.Message)
            {
                UserInputControls = UserInputControls.Accept3dCoordinates | UserInputControls.NullResponseAccepted,
                AppendKeywordsToMessage = ppo.AppendKeywordsToMessage,
            };
            foreach (Keyword k in ppo.Keywords) _opt.Keywords.Add(k.GlobalName, k.LocalName, k.DisplayName, k.Visible, k.Enabled);
            _ts.Font = new FontDescriptor("Malgun Gothic", false, false, 129, 34);   // 129 = 한글 charset(ImportGisCommand와 같은 글꼴)
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

        static double PixelSize()
        {
            try
            {
                double vs = System.Convert.ToDouble(AcadApp.GetSystemVariable("VIEWSIZE"));
                var sc = (Point2d)AcadApp.GetSystemVariable("SCREENSIZE");
                if (vs > 0 && sc.Y > 1) return vs / sc.Y;
            }
            catch { }
            return 1.0;
        }

        void Resize(double px)
        {
            _px = px;
            _ts.TextSize = TextPx * px;
            try { _ext = _ts.ExtentsBox(_label, true, true, null); }
            catch { _ext = new Extents2d(0, -0.2 * _ts.TextSize, _label.Length * _ts.TextSize, _ts.TextSize); }
            if (!(_ext.MaxPoint.X > _ext.MinPoint.X)) _ext = new Extents2d(0, -0.2 * _ts.TextSize, _label.Length * _ts.TextSize, _ts.TextSize);
        }

        protected override SamplerStatus Sampler(JigPrompts prompts)
        {
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
            if (!_has) return true;
            try
            {
                double pad = PadPx * _px, w = _ext.MaxPoint.X - _ext.MinPoint.X, h = _ext.MaxPoint.Y - _ext.MinPoint.Y;
                Point3d P(double x, double y) => Point + _right * x + _up * y;
                // 상자 왼쪽 위 = 커서 + (18 px, −18 px)
                double x0 = OffPx * _px, y0 = -OffPx * _px, x1 = x0 + w + 2 * pad, y1 = y0 - h - 2 * pad;
                var box = new Point3dCollection { P(x0, y0), P(x1, y0), P(x1, y1), P(x0, y1) };
                var tr = draw.SubEntityTraits;
                tr.FillType = FillType.FillAlways;
                tr.TrueColor = new EntityColor(32, 32, 32);
                draw.Geometry.Polygon(box);
                tr.FillType = FillType.FillNever;
                tr.TrueColor = new EntityColor(255, 255, 0);
                draw.Geometry.Polyline(new Point3dCollection { P(x0, y0), P(x1, y0), P(x1, y1), P(x0, y1), P(x0, y0) }, _normal, System.IntPtr.Zero);
                var at = P(x0 + pad - _ext.MinPoint.X, y0 - pad - _ext.MaxPoint.Y);
                draw.Geometry.Text(at, _normal, _right, _label, true, _ts);
            }
            catch { }
            return true;
        }

        public void Dispose() { try { _ts.Dispose(); } catch { } }
    }
}
