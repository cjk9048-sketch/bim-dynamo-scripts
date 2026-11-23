// 리본 아이콘 — Civil 3D 2026 리본 느낌(평면 채움 · 밝은 회색 종이 · 파랑 강조 · 모서리 배지). 손으로 고치는 코드(생성 스크립트 없음).
// ★[JACK 1123 «Civil 3D 원본 UI 버튼 이미지 느낌으로 새로»] 그림 규칙:
//   · 32 격자 · 테두리 1px — 테두리 있는 도형은 꼭짓점을 .5 자리에(100 % 화면에서 안 번짐)
//   · 면은 평면 채움(그라데이션 없음) · 입체는 면 두세 단의 밝기 차이로
//   · 종이 = 밝은 회색 + 회색 테두리 + 접힌 귀 · 콘크리트 = 회청색 · 흙 = 갈색 · 계획면 = 초록 · 물 = 하늘색
//   · 강조색(A) = Civil 파랑 — 리본 칸 색(<see cref="RibbonTone.HueOf"/>)이 있으면 그 색상으로 바뀐다(흙 · 풀 · 종이 · 배지는 그대로 — 뜻이 있는 색)
//   · 배지: 오른쪽 위 = 만들기(노랑 반짝) · 보기(눈) · 더하기(초록 +) / 오른쪽 아래 = 지우기(빨강 ×) · 내보내기(→) · 가져오기(↓)
//   · 어두운 리본(#3B4453)과 밝은 리본(#F5F5F5) 둘 다에서 보이게 — 밝은 면엔 어두운 테두리, 선 그림엔 어두운 테(halo)
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace DH.Grading.Civil;

internal static class RibbonIcons
{
    private static readonly object Gate = new();
    private static readonly Dictionary<(string, bool, double), ImageSource?> Cache = new();

    /// <summary>kind 이름의 아이콘(얼림). small = 16×16, 아니면 32×32. hue = 리본 칸 색상(°) — 강조색만 그 색으로. 모르는 이름 · 오류는 null.</summary>
    public static ImageSource? Get(string kind, bool small = false, double? hue = null)
    {
        try
        {
            if (string.IsNullOrEmpty(kind)) return null;
            var key = (kind, small, hue ?? -1);
            lock (Gate)
            {
                if (Cache.TryGetValue(key, out var hit)) return hit;
                var img = Build(kind, small, hue);
                Cache[key] = img;
                return img;
            }
        }
        catch { return null; }
    }

    private static ImageSource? Build(string kind, bool small, double? hue)
    {
        try
        {
            if (!Icons.TryGetValue(kind, out var draw)) return null;
            var k = new Ink(small, hue);
            draw(k);
            double size = small ? 16 : 32;
            var inner = k.G;
            if (small) inner.Transform = new ScaleTransform(0.5, 0.5);
            var outer = new DrawingGroup { ClipGeometry = new RectangleGeometry(new Rect(0, 0, size, size)) };
            // 투명 바탕 네모 + 자르기 — 모든 아이콘이 같은 크기(32 또는 16)를 갖게(테가 밖으로 나가 그림이 줄어드는 일 없게)
            outer.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, size, size))));
            outer.Children.Add(inner);
            outer.Freeze();
            var img = new DrawingImage(outer);
            img.Freeze();
            return img;
        }
        catch { return null; }
    }

    // ── 색 ────────────────────────────────────────────────────────────────
    private static Color C(string hex) => (Color)ColorConverter.ConvertFromString(hex);
    private static readonly Color White = C("#FFFFFF"), InkC = C("#2B3038");
    private static readonly Color Paper = C("#E8EAED"), PaperE = C("#7C8591"), Fold = C("#B8BFC8"), Grid = C("#C4CAD2");
    private static readonly Color ConcL = C("#E1E5EA"), Conc = C("#BAC3CE"), ConcD = C("#8E98A5"), ConcE = C("#56606C");
    private static readonly Color EarthL = C("#DEB07C"), Earth = C("#C48A50"), EarthD = C("#94612F"), EarthE = C("#66401C"), Rock = C("#8E8378");
    private static readonly Color GrassL = C("#9BDB89"), Grass = C("#62BC57"), GrassD = C("#3E8F38"), GrassE = C("#2A652A");
    private static readonly Color Water = C("#57B4EE"), Asphalt = C("#7D8692");
    private static readonly Color Blue = C("#4BA3E3"), BlueL = C("#A8D6F6"), BlueD = C("#2C7CC1"), BlueE = C("#1D5791");
    private static readonly Color StarC = C("#FFC72C"), StarL = C("#FFF3C4"), StarE = C("#B57B00");
    private static readonly Color Ok = C("#46B94C"), OkE = C("#25702A"), Red = C("#E5483D"), RedE = C("#982016"), Lot = C("#E0A800");

    private static string Inv(FormattableString s) => s.ToString(CultureInfo.InvariantCulture);

    /// <summary>그리는 판 — 한 아이콘의 그림 묶음 + 강조색 + 작은판 여부.</summary>
    private sealed class Ink
    {
        public readonly DrawingGroup G = new();
        public readonly bool Sm;
        public readonly Color A, AL, AD, AE;
        private readonly Stack<DrawingGroup> _st = new();

        public Ink(bool sm, double? hue)
        {
            Sm = sm;
            if (hue is double h) { A = Hsl(h, 0.62, 0.56); AL = Hsl(h, 0.70, 0.80); AD = Hsl(h, 0.58, 0.42); AE = Hsl(h, 0.60, 0.30); }
            else { A = Blue; AL = BlueL; AD = BlueD; AE = BlueE; }
        }

        private DrawingGroup Cur => _st.Count > 0 ? _st.Peek() : G;
        /// <summary>테두리 굵기 — 16판은 2(줄이면 1px).</summary>
        public double E => Sm ? 2.0 : 1.0;
        private double W(double w) => Sm ? Math.Max(w, 2.0) : w;

        public static Geometry P(string d) { var g = Geometry.Parse("F1 " + d); g.Freeze(); return g; }
        private static Brush B(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }
        private static Pen Pn(Color c, double w, double[]? dash = null)
        {
            var p = new Pen(B(c), w) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
            if (dash != null) { p.DashStyle = new DashStyle(dash, 0); p.DashCap = PenLineCap.Flat; p.StartLineCap = p.EndLineCap = PenLineCap.Flat; }
            p.Freeze();
            return p;
        }
        private void Add(Drawing d) => Cur.Children.Add(d);

        public void Fill(string d, Color c) => Fill(P(d), c);
        public void Fill(Geometry g, Color c) => Add(new GeometryDrawing(B(c), null, g));
        public void Shape(string d, Color c, Color e) => Shape(P(d), c, e);
        public void Shape(Geometry g, Color c, Color e, double? w = null) => Add(new GeometryDrawing(B(c), Pn(e, w ?? E), g));
        public void Edge(string d, Color e) => Add(new GeometryDrawing(null, Pn(e, E), P(d)));
        public void Line(string d, Color c, double w) => Line(P(d), c, w);
        public void Line(Geometry g, Color c, double w) => Add(new GeometryDrawing(null, Pn(c, W(w)), g));
        /// <summary>선 + 어두운 테 — 어두운 · 밝은 리본 둘 다에서 보이는 선.</summary>
        public void Halo(string d, Color core, Color halo, double w) => Halo(P(d), core, halo, w);
        public void Halo(Geometry g, Color core, Color halo, double w) { Line(g, halo, W(w) + (Sm ? 2.4 : 1.8)); Line(g, core, w); }
        public void Dash(string d, Color c, double w, double on, double off) => Add(new GeometryDrawing(null, Pn(c, W(w), new[] { on, off }), P(d)));

        public void Push(Geometry? clip = null, double opacity = 1, Transform? tf = null)
        {
            var g = new DrawingGroup { ClipGeometry = clip, Opacity = opacity, Transform = tf };
            Cur.Children.Add(g);
            _st.Push(g);
        }
        public void Pop() => _st.Pop();
    }

    private static Color Hsl(double h, double s, double l)
    {
        double q = l < 0.5 ? l * (1 + s) : l + s - l * s, p = 2 * l - q, hk = ((h % 360) + 360) % 360 / 360.0;
        double F(double t) { if (t < 0) t += 1; if (t > 1) t -= 1; return t < 1.0 / 6 ? p + (q - p) * 6 * t : t < 0.5 ? q : t < 2.0 / 3 ? p + (q - p) * (2.0 / 3 - t) * 6 : p; }
        return Color.FromRgb((byte)Math.Round(F(hk + 1.0 / 3) * 255), (byte)Math.Round(F(hk) * 255), (byte)Math.Round(F(hk - 1.0 / 3) * 255));
    }

    private static Geometry Circle(double cx, double cy, double r) { var g = new EllipseGeometry(new Point(cx, cy), r, r); g.Freeze(); return g; }
    private static Geometry Oval(double cx, double cy, double rx, double ry) { var g = new EllipseGeometry(new Point(cx, cy), rx, ry); g.Freeze(); return g; }
    private static Geometry Box(double x, double y, double w, double h, double r = 0) { var g = new RectangleGeometry(new Rect(x, y, w, h), r, r); g.Freeze(); return g; }
    private static Geometry Poly(IList<Point> pts, bool closed = true)
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(pts[0], closed, closed);
            for (int i = 1; i < pts.Count; i++) c.LineTo(pts[i], true, true);
        }
        g.Freeze();
        return g;
    }

    // ── 배지 ──────────────────────────────────────────────────────────────
    /// <summary>노랑 반짝 — 만들기 · 생성(오른쪽 위).</summary>
    private static void Star(Ink k, double cx = 25.5, double cy = 6.5, double r = 6.2)
    {
        var pts = new List<Point>();
        for (int i = 0; i < 16; i++)
        {
            double a = -Math.PI / 2 + i * Math.PI / 8;
            double rr = i % 4 == 0 ? r : i % 2 == 0 ? r * 0.56 : r * 0.30;
            pts.Add(new Point(cx + rr * Math.Cos(a), cy + rr * Math.Sin(a)));
        }
        k.Shape(Poly(pts), StarC, StarE, k.Sm ? 1.6 : 0.8);
        k.Fill(Circle(cx, cy, r * 0.22), StarL);
    }

    /// <summary>빨간 동그라미 × — 지우기 · 끄기(오른쪽 아래).</summary>
    private static void Erase(Ink k, double cx = 25, double cy = 25)
    {
        k.Shape(Circle(cx, cy, 5.6), Red, RedE);
        k.Line(Inv($"M{cx - 2.4} {cy - 2.4} L{cx + 2.4} {cy + 2.4} M{cx + 2.4} {cy - 2.4} L{cx - 2.4} {cy + 2.4}"), White, 1.8);
    }

    /// <summary>초록 동그라미 + — 더하기(오른쪽 위).</summary>
    private static void Plus(Ink k, double cx = 25, double cy = 7)
    {
        k.Shape(Circle(cx, cy, 5.6), Ok, OkE);
        k.Line(Inv($"M{cx - 3} {cy} H{cx + 3} M{cx} {cy - 3} V{cy + 3}"), White, 2.0);
    }

    /// <summary>초록 동그라미 ▶ — 따라 하기(오른쪽 위).</summary>
    private static void Play(Ink k, double cx = 25, double cy = 7)
    {
        k.Shape(Circle(cx, cy, 5.6), Ok, OkE);
        k.Fill(Inv($"M{cx - 1.8} {cy - 3.2} L{cx + 3.2} {cy} L{cx - 1.8} {cy + 3.2} Z"), White);
    }

    /// <summary>눈 — 보기. hw = 반폭.</summary>
    private static void Eye(Ink k, double cx, double cy, double hw)
    {
        double v = hw * 0.74;
        k.Shape(Inv($"M{cx - hw} {cy} C{cx - hw * 0.55} {cy - v} {cx + hw * 0.55} {cy - v} {cx + hw} {cy} C{cx + hw * 0.55} {cy + v} {cx - hw * 0.55} {cy + v} {cx - hw} {cy} Z"), ConcL, ConcE);
        k.Shape(Circle(cx, cy, hw * 0.45), k.A, k.AE);
        k.Fill(Circle(cx, cy, hw * 0.2), InkC);
        k.Fill(Circle(cx + hw * 0.16, cy - hw * 0.17, hw * 0.1), White);
    }

    /// <summary>오른쪽으로 가는 막대 화살 — 내보내기.</summary>
    private static void ArrowR(Ink k, double x0, double cy, double x1, double hs, double hh, double hl, Color f, Color e)
        => k.Shape(Inv($"M{x0} {cy - hs} H{x1 - hl} V{cy - hh} L{x1} {cy} L{x1 - hl} {cy + hh} V{cy + hs} H{x0} Z"), f, e);

    /// <summary>아래로 가는 막대 화살 — 가져오기 · 내려받기.</summary>
    private static void ArrowD(Ink k, double cx, double y0, double y1, double hs, double hh, double hl, Color f, Color e)
        => k.Shape(Inv($"M{cx - hs} {y0} V{y1 - hl} H{cx - hh} L{cx} {y1} L{cx + hh} {y1 - hl} H{cx + hs} V{y0} Z"), f, e);

    /// <summary>톱니 — 설정.</summary>
    private static void Gear(Ink k, double cx, double cy, double r, Color f, Color e)
    {
        var pts = new List<Point>();
        const int n = 8;
        for (int i = 0; i < n * 4; i++)
        {
            double a = (i - 0.5) * Math.PI * 2 / (n * 4);
            double rr = (i % 4 == 0 || i % 4 == 1) ? r : r * 0.74;
            pts.Add(new Point(cx + rr * Math.Cos(a), cy + rr * Math.Sin(a)));
        }
        k.Shape(Poly(pts), f, e);
        k.Shape(Circle(cx, cy, r * 0.32), Paper, e);
    }

    /// <summary>종이 한 장(접힌 귀) — x0..x1 · y0..y1(.5 자리).</summary>
    private static void Sheet(Ink k, double x0, double y0, double x1, double y1, double fold = 6, Color? fill = null)
    {
        k.Shape(Inv($"M{x0} {y0} H{x1 - fold} L{x1} {y0 + fold} V{y1} H{x0} Z"), fill ?? Paper, PaperE);
        if (fold > 0) k.Shape(Inv($"M{x1 - fold} {y0} V{y0 + fold} H{x1} Z"), Fold, PaperE);
    }

    // ── 공통 모양(같은 뜻은 같은 모양) ─────────────────────────────────────
    private const string HillD = "M1.5 29.5 V24 C5 22 7 13 13 11 C18 9.5 20 15 23 17 C26 19 28.5 18.5 30.5 19.5 V29.5 Z";

    /// <summary>원지반 언덕(흙) + 감긴 등고선.</summary>
    private static void Hill(Ink k)
    {
        var hill = Ink.P(HillD);
        k.Shape(hill, Earth, EarthE);
        if (!k.Sm)
        {
            k.Push(hill);
            k.Line("M1 26 C6 24 9 19.5 13 19 C17 18.5 19.5 22 23.5 23 C26.5 23.8 28.5 23.2 31 23.8", EarthL, 1.4);
            k.Line("M7 18.5 C9 15.8 11 14.6 13.4 14.5 C15.8 14.4 17.4 16 19.2 17.6", EarthL, 1.4);
            k.Pop();
        }
    }

    /// <summary>계획부지 단면 — 흙 몸통 + 초록 계획면(평지 + 양쪽 사면).</summary>
    private static void Pad(Ink k)
    {
        k.Shape("M1.5 29.5 L9.5 16.5 H22.5 L30.5 29.5 Z", Earth, EarthE);
        k.Halo("M2.2 28.4 L10 16.5 H22 L29.8 28.4", Grass, GrassE, 2.4);
    }

    /// <summary>터파기 단면 — 흙에 파인 구덩이 + 강조색 굴착면.</summary>
    private static void Pit(Ink k)
    {
        k.Shape("M1.5 10.5 H7.5 L12.5 22.5 H19.5 L24.5 10.5 H30.5 V29.5 H1.5 Z", Earth, EarthE);
        k.Halo("M7.5 10.5 L12.5 22.5 H19.5 L24.5 10.5", k.A, k.AE, 2.2);
        k.Line("M2 10.5 H7 M25 10.5 H30", Grass, 2.0);
    }

    /// <summary>구조물 단면 — 땅 위아래로 걸친 속 빈 콘크리트 상자.</summary>
    private static void Struct(Ink k)
    {
        k.Shape("M1.5 13.5 H7.5 V27.5 H24.5 V13.5 H30.5 V29.5 H1.5 Z", Earth, EarthE);
        k.Line("M2 13.5 H7 M25 13.5 H30", Grass, 2.0);
        k.Shape("M7.5 4.5 H24.5 V27.5 H7.5 Z M11.5 8.5 V23.5 H20.5 V8.5 Z", Conc, ConcE);
        k.Line("M8.5 5.5 H23.5", ConcL, 1.0);
    }

    /// <summary>지구본 — 파랑 바다(물 = 고정색 · 칸 색 안 따름) + 초록 땅. off = 회색(꺼짐).</summary>
    private static void Globe(Ink k, bool off)
    {
        var disc = Circle(14.5, 17.5, 12);
        k.Shape(disc, off ? ConcD : Blue, off ? ConcE : BlueE);
        k.Push(disc);
        var land = off ? ConcL : Grass;
        k.Fill("M5 9 C8 6.5 13 7.5 14 10.5 C15 13.5 12 15 11 18 C10 21 7 22.5 4.5 20.5 C2.5 17 2.5 12 5 9 Z", land);
        k.Fill("M17 15 C20 12.5 24.5 13.5 26 17 C27.5 21 24 26 20 27.5 C17.5 28.3 16.8 25 18 22 C19 19.5 16 18 17 15 Z", land);
        k.Pop();
        if (!k.Sm) k.Line("M6.5 11 C8.5 8.2 11.5 6.8 14.5 6.6", off ? White : BlueL, 1.2);
    }

    /// <summary>필지 지도 — 연한 땅 칠 + 노랑 경계 + 지번 점.</summary>
    private static void Parcels(Ink k)
    {
        var map = Ink.P("M1.5 3.5 H30.5 V28.5 H1.5 Z");
        k.Shape(map, Paper, PaperE);
        k.Push(map);
        k.Fill("M1.5 3.5 H14.5 L13.4 15.5 H1.5 Z", C("#F2E2B3"));
        k.Fill("M1.5 15.5 H13.4 L12.5 28.5 H1.5 Z", C("#CFE6F6"));
        k.Fill("M14.5 3.5 H30.5 V12.5 L13.6 13.4 Z", C("#E3E6EA"));
        k.Fill("M13.6 13.4 L22.5 12.9 L23.5 28.5 H12.5 Z", C("#F2E2B3"));
        k.Fill("M22.5 12.9 L30.5 12.5 V28.5 H23.5 Z", C("#CFE6F6"));
        k.Pop();
        k.Halo("M14.5 3.5 L12.5 28.5 M1.5 15.5 L13.4 15.5 M13.6 13.4 L30.5 12.5 M22.5 12.9 L23.5 28.5", Lot, C("#8A6400"), 1.4);
        k.Edge("M1.5 3.5 H30.5 V28.5 H1.5 Z", PaperE);
        if (!k.Sm) foreach (var (x, y) in new[] { (7.5, 9.5), (7.0, 22.0), (22.0, 8.0), (18.0, 21.0), (27.0, 21.0) }) k.Fill(Circle(x, y, 1.0), InkC);
    }

    /// <summary>진입도로(원근) — 아스팔트 + 흰 가장자리 + 노랑 중앙선.</summary>
    private static void Road(Ink k)
    {
        k.Shape("M11.5 3.5 H20.5 L30.5 29.5 H1.5 Z", Asphalt, ConcE);
        k.Line("M12.6 4.5 L4 28.5 M19.4 4.5 L28 28.5", White, 1.2);
        k.Fill("M15.6 5 H16.4 L16.6 9 H15.4 Z M15.2 12 H16.8 L17.1 18 H14.9 Z M14.7 21 H17.3 L17.7 28.5 H14.3 Z", StarC);
    }

    // ── 아이콘 표 ─────────────────────────────────────────────────────────
    private static readonly Dictionary<string, Action<Ink>> Icons = new()
    {
        // 설정: 슬라이더 3줄 + 손잡이 — 값을 정하는 곳
        ["설정"] = k =>
        {
            foreach (var (y, kx) in new[] { (8.0, 11.0), (16.0, 22.0), (24.0, 15.0) })
            {
                k.Shape(Box(3.5, y - 1.5, 25, 3, 1.5), ConcD, ConcE);
                k.Shape(Box(3.5, y - 1.5, kx - 3.5, 3, 1.5), k.A, k.AE);
                k.Shape(Box(kx - 2.5, y - 4.5, 5, 9, 1.2), ConcL, ConcE);
            }
        },

        // 수치지도: 접힌 도면 한 장 + 등고선 + 들어오는 화살 — 파일(DXF)에서 읽는다
        ["수치지도"] = k =>
        {
            Sheet(k, 8.5, 2.5, 28.5, 29.5);
            k.Line("M11.5 17 C11.5 11.5 16 9.5 20 10.3 C24.8 11.2 25.8 15 25.4 18.6 C25 23.8 20.5 25.6 16.5 25 C13 24.4 11.5 21.5 11.5 17 Z", Earth, 1.4);
            if (!k.Sm) k.Line("M15 17.3 C15 14.8 17 13.8 19.4 14.2 C21.8 14.6 22.4 16.6 22 18.4 C21.6 20.6 19.6 21.4 17.8 21 C16 20.6 15 19.4 15 17.3 Z", EarthD, 1.3);
            k.Fill(Oval(18.6, 17.6, 1.5, 1.2), EarthD);
            ArrowR(k, 1.5, 23.5, 14.5, 2.5, 5.5, 5.5, Blue, BlueE);
        },

        // 서버지표면: 구름(서버) + 내려받는 화살 + 땅
        ["서버지표면"] = k =>
        {
            k.Shape("M1.5 29.5 V25 C7 23 11 25.5 16 24 C21 22.5 25 24 30.5 23 V29.5 Z", Earth, EarthE);
            Geometry cloud = Circle(10, 12.5, 5.5);
            cloud = Geometry.Combine(cloud, Circle(17, 9.5, 6.5), GeometryCombineMode.Union, null);
            cloud = Geometry.Combine(cloud, Circle(23.5, 13, 5), GeometryCombineMode.Union, null);
            cloud = Geometry.Combine(cloud, Box(7, 12.5, 18, 5.5, 2.5), GeometryCombineMode.Union, null);
            cloud.Freeze();
            k.Shape(cloud, ConcL, ConcE);
            ArrowD(k, 16, 10.5, 27.5, 2.5, 6, 6, Blue, BlueE);
        },

        // 등고선작성: 겹친 등고선 고리 + 고르기 네모 + 반짝 — 도면에 있는 선으로 만든다
        ["등고선작성"] = k =>
        {
            k.Halo("M2.5 19 C2.5 13 7.5 10 13 10.2 C19 10.4 23 14 23 19.2 C23 24.5 18.5 28.5 13 28.3 C7 28 2.5 25 2.5 19 Z", EarthL, EarthE, 1.6);
            k.Halo("M6.8 19 C6.8 15.5 9.5 14 12.8 14.2 C16.5 14.4 18.8 16.5 18.7 19.4 C18.6 22.4 15.8 24.2 12.8 24 C9.5 23.8 6.8 22.3 6.8 19 Z", Earth, EarthE, 1.6);
            k.Shape(Oval(12.8, 19.2, 2.4, 2.0), EarthD, EarthE);
            k.Shape(Box(19.5, 21.5, 7, 7), White, k.AE);
            k.Edge("M21.5 23.5 H24.5 V26.5 H21.5 Z", k.A);
            Star(k);
        },

        // 원지반: 등고선이 감긴 언덕 + 가져오기 화살 — 있는 그대로의 땅을 가져온다
        ["원지반"] = k =>
        {
            Hill(k);
            ArrowD(k, 25, 1.5, 14.5, 2.5, 5.5, 5.5, Blue, BlueE);
        },

        // 원지반삭제: 같은 언덕 + 빨간 × — 지운다(지도끄기와 같은 배지)
        ["원지반삭제"] = k => { Hill(k); Erase(k); },

        // 계획부지: 다듬어진 평지 + 양쪽 사면 + 반짝 — 만들려는 부지
        ["계획부지"] = k => { Pad(k); Star(k); },

        // 정지면: 계단식 정지면(초록 계획면) — 절성토 계단
        ["정지면"] = k =>
        {
            k.Shape("M1.5 29.5 V23.5 H8 L11 17.5 H16 L19 11.5 H24 L27 5.5 H30.5 V29.5 Z", Earth, EarthE);
            k.Halo("M2 23.5 H8 L11 17.5 H16 L19 11.5 H24 L27 5.5 H30", Grass, GrassE, 2.4);
            if (!k.Sm) k.Line("M2.5 27.5 H29.5", EarthD, 1.0);
        },

        // 터파기: 흙에 파인 구덩이 — 바닥 + 양쪽 법면
        ["터파기"] = k => Pit(k),

        // 사면수정: 사면 ↔ 옹벽 — 서로 바꾸는 두 방향 화살
        ["사면수정"] = k =>
        {
            k.Shape("M1.5 29.5 L13.5 15.5 V29.5 Z", Earth, EarthE);
            k.Halo("M2 29 L13.5 15.6", Grass, GrassE, 2.0);
            k.Shape("M26.5 15.5 H30.5 V29.5 H26.5 Z", Earth, EarthE);
            k.Shape("M21.5 15.5 H26.5 V26.5 H29.5 V29.5 H18.5 V26.5 H21.5 Z", Conc, ConcE);
            k.Shape("M1.5 7.5 L7.5 2.5 V5.5 H24.5 V2.5 L30.5 7.5 L24.5 12.5 V9.5 H7.5 V12.5 Z", k.A, k.AE);
        },

        // 옹벽: 쌓은 블록 벽(회청색) + 갓돌 — 수직 벽면
        ["옹벽"] = k =>
        {
            k.Shape("M3.5 8.5 H28.5 V28.5 H3.5 Z", Conc, ConcE);
            if (!k.Sm)
            {
                k.Fill("M3.5 8.5 H11.5 V13.5 H3.5 Z M19.5 13.5 H28.5 V18.5 H19.5 Z M11.5 18.5 H19.5 V23.5 H11.5 Z M3.5 23.5 H7.5 V28.5 H3.5 Z M23.5 23.5 H28.5 V28.5 H23.5 Z", ConcL);
                k.Edge("M3.5 8.5 H28.5 V28.5 H3.5 Z", ConcE);
            }
            k.Line("M3.5 13.5 H28.5 M3.5 18.5 H28.5 M3.5 23.5 H28.5", ConcE, 1.0);
            k.Line("M11.5 8.5 V13.5 M19.5 8.5 V13.5 M7.5 13.5 V18.5 M15.5 13.5 V18.5 M23.5 13.5 V18.5 M11.5 18.5 V23.5 M19.5 18.5 V23.5 M7.5 23.5 V28.5 M15.5 23.5 V28.5 M23.5 23.5 V28.5", ConcE, 1.0);
            k.Shape("M1.5 4.5 H30.5 V8.5 H1.5 Z", ConcL, ConcE);
        },

        // 사면: 비탈(흙) + 초록 사면 — 흙으로 돌아간다
        ["사면"] = k =>
        {
            k.Shape("M1.5 29.5 L28.5 8.5 V29.5 Z", Earth, EarthE);
            k.Halo("M2.2 28.9 L28.5 8.5", Grass, GrassE, 2.4);
            if (!k.Sm) k.Line("M12 25.5 L15 22.5 M18 25.5 L21 22.5 M24 25.5 L27 22.5 M21 19.5 L24 16.5", EarthL, 1.2);
        },

        // ── 보기(눈) ──
        // 보기: 큰 눈 — 무엇을 보일지
        ["보기"] = k => Eye(k, 16, 16, 13.5),

        // 보기전부: 세 겹(흙 · 계획 · 강조) + 눈 — 전부 보기
        ["보기전부"] = k =>
        {
            k.Shape("M13 18.5 L25.5 24 L13 29.5 L0.5 24 Z", Earth, EarthE);
            k.Shape("M13 13.5 L25.5 19 L13 24.5 L0.5 19 Z", Grass, GrassE);
            k.Shape("M13 8.5 L25.5 14 L13 19.5 L0.5 14 Z", k.AL, k.AE);
            Eye(k, 24.5, 6.5, 7);
        },

        // 보기원지반 · 보기계획 · 보기터파기: 그것만 + 눈(오른쪽 위)
        ["보기원지반"] = k => { Hill(k); Eye(k, 24.5, 6.5, 7); },
        ["보기계획"] = k => { Pad(k); Eye(k, 24.5, 6.5, 7); },
        ["보기터파기"] = k => { Pit(k); Eye(k, 24.5, 5.0, 6.5); },

        // ── 도면 ──
        // 도면화: 겹친 배치 도면 두 장(도곽 · 표제란) + 반짝 — 배치탭을 만든다
        ["도면화"] = k =>
        {
            k.Shape("M5.5 4.5 H29.5 V23.5 H5.5 Z", Fold, PaperE);
            k.Shape("M1.5 8.5 H25.5 V28.5 H1.5 Z", Paper, PaperE);
            k.Edge("M3.5 10.5 H23.5 V26.5 H3.5 Z", InkC);
            k.Edge("M3.5 23.5 H23.5 M16.5 23.5 V26.5", InkC);
            k.Halo("M5.5 20 L9 15 L13 17.5 L17.5 12.5 L21.5 15", k.A, k.AE, 1.6);
            Star(k, 26, 6, 5.6);
        },

        // 대안: 겹친 도면 두 장 + 수량 막대 — 대안끼리 견주기
        ["대안"] = k =>
        {
            Sheet(k, 11.5, 2.5, 29.5, 22.5, 5, ConcL);
            k.Shape(Box(22.5, 9.5, 3, 10), Earth, EarthE);
            Sheet(k, 2.5, 9.5, 20.5, 29.5, 5);
            k.Shape(Box(5.5, 20.5, 3, 6), Earth, EarthE);
            k.Shape(Box(9.5, 15.5, 3, 11), k.A, k.AE);
            k.Shape(Box(13.5, 18.5, 3, 8), Grass, GrassE);
            k.Line("M4.5 26.5 H18.5", InkC, 1.0);
        },

        // 도면설정: 설정 종이 + 톱니 — 도면을 어떻게 그릴지(공통설정)
        ["도면설정"] = k =>
        {
            Sheet(k, 2.5, 2.5, 22.5, 29.5);
            foreach (var y in new[] { 12.5, 17.5, 22.5 })
            {
                k.Shape(Box(5.5, y - 1.5, 3, 3), k.A, k.AE);
                k.Line(Inv($"M11 {y} H18"), ConcD, 1.4);
            }
            Gear(k, 23.5, 23.5, 8, Conc, ConcE);
        },

        // 노리선: 평면 사면 표시(비스듬히) — 마루선(강조) + 사면 띠 + 긴/짧은 빗금 + 끝선
        ["노리선"] = k =>
        {
            const double d = 12;                                   // 마루선 → 끝선 거리(x · y 각각)
            Point Cr(double t) => new(2.5 + t * 15, 17.5 - t * 15);  // 마루선 위 점(t = 0..1)
            k.Shape(Poly(new[] { Cr(0), Cr(1), Cr(1) + new Vector(d, d), Cr(0) + new Vector(d, d) }), EarthL, EarthD);
            for (int i = 0; i < 7; i++)
            {
                var p = Cr(0.08 + i * 0.14);
                double len = i % 2 == 0 ? d - 0.8 : d * 0.5;
                k.Line(Inv($"M{p.X} {p.Y} L{p.X + len} {p.Y + len}"), EarthE, 1.3);
            }
            k.Halo("M2.5 17.5 L17.5 2.5", k.A, k.AE, 2.6);
        },

        // 종단: 격자 종이 + 지반선(갈색) + 계획 종단선(강조 · 꺾인 점) — 세로로 자른 그림
        ["종단"] = k =>
        {
            Sheet(k, 1.5, 5.5, 30.5, 27.5);
            if (!k.Sm) k.Line("M7.5 7 V26 M13.5 7 V26 M19.5 7 V26 M2.5 13.5 H24 M2.5 19.5 H29.5", Grid, 1.0);
            k.Line("M3 22 C6 18 9 17 12 19 C15 21 18 24 22 21 C25 18.5 27 18 29.5 19", EarthD, 1.6);
            k.Halo("M3 17 L11 22 L21 15 L29.5 19", k.A, k.AE, 1.8);
            if (!k.Sm) foreach (var (x, y) in new[] { (11.0, 22.0), (21.0, 15.0) }) k.Shape(Box(x - 1.5, y - 1.5, 3, 3), White, InkC);
        },

        // 지층: 세 겹 땅(토사 · 풍화암 · 연암) + 시추공
        ["지층"] = k =>
        {
            k.Fill("M1.5 6.5 H30.5 V12 C25 13.5 20 11 15 12.5 C10 14 6 12 1.5 13 Z", EarthL);
            k.Fill("M1.5 13 C6 12 10 14 15 12.5 C20 11 25 13.5 30.5 12 V20 C25 21.5 19 19 14 20.5 C9 22 5 20.5 1.5 21 Z", Earth);
            k.Fill("M1.5 21 C5 20.5 9 22 14 20.5 C19 19 25 21.5 30.5 20 V29.5 H1.5 Z", Rock);
            k.Line("M1.5 13 C6 12 10 14 15 12.5 C20 11 25 13.5 30.5 12 M1.5 21 C5 20.5 9 22 14 20.5 C19 19 25 21.5 30.5 20", EarthE, 1.0);
            k.Edge("M1.5 6.5 H30.5 V29.5 H1.5 Z", EarthE);
            k.Shape("M20.5 3.5 H24.5 V27.5 H20.5 Z", ConcL, ConcE);
            if (!k.Sm) k.Line("M21.5 12 H23.5 M21.5 20.5 H23.5", ConcE, 1.0);
            k.Shape("M18.5 1.5 H26.5 V4.5 H18.5 Z", k.A, k.AE);
        },

        // 종단생성: 꺾은 노선 + 꼭짓점 그립 — 선을 직접 그린다
        ["종단생성"] = k =>
        {
            k.Halo("M4 26.5 L11.5 12.5 L19.5 20.5 L28 5.5", k.A, k.AE, 2.4);
            foreach (var (x, y) in new[] { (4.0, 26.5), (11.5, 12.5), (19.5, 20.5), (28.0, 5.5) })
                k.Shape(Box(Math.Round(x) - 2.5, Math.Round(y) - 2.5, 5, 5), White, InkC);
        },

        // 측점추가: 노선 위 측점 눈금 + 초록 더하기
        ["측점추가"] = k =>
        {
            k.Halo("M2 21.5 H30", ConcL, ConcE, 2.2);
            foreach (var x in new[] { 6.5, 14.5, 22.5 }) k.Halo(Inv($"M{x} 15.5 V27.5"), k.A, k.AE, 2.0);
            Plus(k, 25, 7);
        },

        // 측점전후: 측점 하나 + 앞뒤로 벌어지는 화살
        ["측점전후"] = k =>
        {
            k.Halo("M2 23.5 H30", ConcL, ConcE, 2.2);
            k.Halo("M16 15.5 V30", k.A, k.AE, 2.2);
            k.Shape("M1.5 8.5 L7.5 3.5 V6.5 H13.5 V10.5 H7.5 V13.5 Z", k.A, k.AE);
            k.Shape("M30.5 8.5 L24.5 3.5 V6.5 H18.5 V10.5 H24.5 V13.5 Z", k.A, k.AE);
        },

        // 횡단: 종이 + 중심선 + 지반선(갈색) + 계획 단면(강조) — 가로로 자른 그림
        ["횡단"] = k =>
        {
            Sheet(k, 1.5, 5.5, 30.5, 27.5);
            // 땅(칠한 단면) — 지반선 아래를 흙으로
            k.Shape("M2.5 21 C7 18 10 16.5 14 18 C18 19.5 21 15.5 25 14.5 C27 14 28.5 14.5 29.5 15 V26.5 H2.5 Z", EarthL, EarthD);
            // 계획 단면(강조 칠) — 평지 + 양쪽 사면
            k.Shape("M4.5 24.5 L10.5 16.5 H21.5 L27.5 24.5 Z", k.AL, k.AE);
            k.Line("M10.5 16.5 H21.5", k.A, 2.0);
            k.Dash("M16 7.5 V26", InkC, 1.0, 2.5, 1.5);
        },

        // ── 기타 ──
        // 위성지도: 위성 + 그 아래 항공사진 한 칸 — 위성사진을 깐다
        ["위성지도"] = k =>
        {
            var tile = Ink.P("M1.5 11.5 H21.5 V29.5 H1.5 Z");
            k.Fill(tile, C("#5E9A4A"));
            k.Push(tile);
            k.Fill("M1.5 11.5 H10.5 L8.5 19 H1.5 Z", C("#A8C46A"));
            k.Fill("M13 21.5 H21.5 V29.5 H15 Z", C("#B99060"));
            k.Line("M1 25.5 C6 23.5 9 27.5 13 25 C16 23 18 19 22 17.5", Water, 2.2);
            k.Line("M11.5 11 L7.5 30", ConcL, 1.3);
            k.Pop();
            k.Edge("M1.5 11.5 H21.5 V29.5 H1.5 Z", ConcE);
            k.Push(tf: new RotateTransform(-30, 22.5, 9));
            k.Line("M13 9 H32", ConcE, 1.4);
            k.Shape(Box(12.5, 5.5, 6, 7), k.A, k.AE);
            k.Shape(Box(26.5, 5.5, 6, 7), k.A, k.AE);
            if (!k.Sm) k.Line("M15.5 5.5 V12.5 M29.5 5.5 V12.5 M12.5 9 H18.5 M26.5 9 H32.5", k.AL, 0.8);
            k.Shape(Box(19.5, 5.5, 6, 7, 1), ConcL, ConcE);
            k.Pop();
        },

        // 자르기: 언덕(흐림) 위 점선 네모 — 남길 자리만 오려낸다(네모 안만 진하게)
        ["자르기"] = k =>
        {
            k.Push(opacity: 0.38);
            Hill(k);
            k.Pop();
            k.Push(Box(7.5, 8.5, 17, 19));
            Hill(k);
            k.Pop();
            k.Line("M7.5 8.5 H24.5 V27.5 H7.5 Z", k.AE, 2.4);   // 고르기 네모 — 어두운 띠 + 흰 점선(«개미 줄»)
            k.Dash("M7.5 8.5 H24.5 V27.5 H7.5 Z", White, 1.2, 2.5, 1.5);
        },

        // U형측구: 지반 아래 U자 콘크리트(회색) + 물(하늘색)
        ["U형측구"] = k =>
        {
            k.Shape("M1.5 9.5 H7.5 V26.5 H24.5 V9.5 H30.5 V29.5 H1.5 Z", Earth, EarthE);
            k.Line("M2 9.5 H7 M25 9.5 H30", Grass, 2.0);
            k.Fill("M11.5 16.5 H20.5 V22.5 H11.5 Z", Water);
            k.Line("M12 16.5 H20", BlueL, 1.0);
            k.Shape("M7.5 9.5 H11.5 V22.5 H20.5 V9.5 H24.5 V26.5 H7.5 Z", Conc, ConcE);
        },

        // 구조물: 땅 위아래로 걸친 속 빈 상자(회색)
        ["구조물"] = k => Struct(k),

        // 지적: 필지 경계(노랑) + 연한 땅 칠 — 나뉜 땅
        ["지적"] = k => Parcels(k),

        // 지도: 지구본 + 반짝 — 위성지도를 더한다
        ["지도"] = k => { Globe(k, false); Star(k, 26, 6, 5.6); },

        // 지도끄기: 회색 지구본 + 빨간 × — 끈다
        ["지도끄기"] = k => { Globe(k, true); Erase(k); },

        // 초기화: 되돌리는 둥근 화살
        ["초기화"] = k =>
        {
            double cx = 16, cy = 16.5, r = 9.6, a0 = 230, a1 = -42;
            var pts = new List<Point>();
            for (int i = 0; i <= 48; i++)
            {
                double a = (a0 + (a1 - a0) * i / 48.0) * Math.PI / 180;
                pts.Add(new Point(cx + r * Math.Cos(a), cy + r * Math.Sin(a)));
            }
            k.Halo(Poly(pts, false), k.A, k.AE, 4.2);
            double t = a1 * Math.PI / 180;
            var b = new Point(cx + r * Math.Cos(t), cy + r * Math.Sin(t));
            var tv = new Vector(Math.Sin(t), -Math.Cos(t));
            var nv = new Vector(Math.Cos(t), Math.Sin(t));
            k.Shape(Poly(new[] { b + tv * 7.0, b + nv * 6.2, b - nv * 6.2 }), k.A, k.AE);
        },

        // 3d: 등각 땅 덩어리 — 초록 윗면(부지) + 흙 옆면 — 3D로 본다
        ["3d"] = k =>
        {
            k.Shape("M2.5 11.5 L16 18.5 V29.5 L2.5 22.5 Z", Earth, EarthE);
            k.Shape("M16 18.5 L29.5 11.5 V22.5 L16 29.5 Z", EarthD, EarthE);
            k.Shape("M16 4.5 L29.5 11.5 L16 18.5 L2.5 11.5 Z", Grass, GrassE);
            k.Shape("M16 8 L22.8 11.5 L16 15 L9.2 11.5 Z", GrassL, GrassD);
            if (!k.Sm) k.Line("M2.5 17 L16 24 L29.5 17", EarthE, 0.9);
        },

        // default: 등각 상자 + 내보내기 화살
        ["default"] = k =>
        {
            k.Shape("M1.5 9.5 L11 14.5 V25.5 L1.5 20.5 Z", Conc, ConcE);
            k.Shape("M11 14.5 L20.5 9.5 V20.5 L11 25.5 Z", ConcD, ConcE);
            k.Shape("M11 4.5 L20.5 9.5 L11 14.5 L1.5 9.5 Z", ConcL, ConcE);
            ArrowR(k, 14.5, 23.5, 30.5, 2.5, 6, 6, Blue, BlueE);
        },

        // 정보: 파란 원 + i(새 판이면 «정보+»에 주황 점) — RibbonApp.InfoGlyph가 부른다
        ["정보"] = k => Info(k, false),
        ["정보+"] = k => Info(k, true),

        // ── 단추 겹침 풀기(JACK 1123 — 같은 그림을 쓰던 단추에 제 그림) ──
        // 수량산출: 표 종이 + Σ — 수량산출서(엑셀)
        ["수량산출"] = k =>
        {
            Sheet(k, 2.5, 2.5, 24.5, 29.5);
            k.Shape("M5.5 10.5 H21.5 V13.5 H5.5 Z", k.A, k.AE);
            k.Edge("M5.5 13.5 H21.5 V26.5 H5.5 Z", ConcD);
            k.Line("M5.5 17.5 H21.5 M5.5 21.5 H21.5 M11.5 13.5 V26.5 M16.5 13.5 V26.5", ConcD, 1.0);
            k.Shape(Box(18.5, 18.5, 12, 12, 2), k.A, k.AE);
            k.Line("M27.5 21.5 H21.5 L25 24.5 L21.5 27.5 H27.5", White, 1.6);
        },

        // 튜토리얼: 펼친 책 + 초록 ▶ — 따라 하기
        ["튜토리얼"] = k =>
        {
            k.Halo("M1.5 11 V28.5 C7 26.5 12 27 16 29.5 C20 27 25 26.5 30.5 28.5 V11", k.A, k.AE, 2.0);
            k.Shape("M2.5 8.5 C7 6.5 11.5 7 15.5 9.5 V27 C11.5 24.8 7 24.5 2.5 26 Z", Paper, PaperE);
            k.Shape("M29.5 8.5 C25 6.5 20.5 7 16.5 9.5 V27 C20.5 24.8 25 24.5 29.5 26 Z", Paper, PaperE);
            if (!k.Sm) k.Line("M5 13 C8 12 10.5 12.2 13 13.5 M5 17 C8 16 10.5 16.2 13 17.5 M5 21 C8 20 10.5 20.2 13 21.5 M19 17.5 C21.5 16.2 24 16 27 17 M19 21.5 C21.5 20.2 24 20 27 21", ConcD, 1.0);
            Play(k, 24.5, 7);
        },

        // 진입도로: 원근 도로 + 반짝 — 진입도로 생성
        ["진입도로"] = k => { Road(k); Star(k, 26, 6, 5.6); },

        // 보기도로 · 보기최종: 도로만 / 부지 + 도로(평면) + 눈
        ["보기도로"] = k => { Road(k); Eye(k, 24.5, 6.5, 7); },
        ["보기최종"] = k =>
        {
            // 평면: 부지(가운데 평지 연초록 + 둘레 사면 초록) + 굽어 들어오는 도로(아스팔트 · 노랑 중앙선) — 하나로 붙은 면
            k.Shape("M2.5 7.5 L17.5 4.5 L22.5 15.5 L15.5 23.5 L3.5 20.5 Z", Grass, GrassE);
            k.Shape("M6.5 10 L15.5 8.5 L18.5 15 L14 19.5 L7 17.5 Z", GrassL, GrassD);
            const string road = "M16 18 C20 22 23 26.5 31 28.5";
            k.Halo(road, Asphalt, ConcE, 5.0);
            k.Line(road, StarC, 0.9);
            Eye(k, 24.5, 6.5, 7);
        },

        // 지적삭제: 필지 + 빨간 ×
        ["지적삭제"] = k => { Parcels(k); Erase(k); },

        // 구조물 넷: 불러오기(↓) · 배치(반짝) · 좌표계 내보내기(→) · 지우기(×) — 리본엔 아직 안 씀([구조물] 창 안 단추용으로 남김)
        ["구조물불러오기"] = k => { Struct(k); ArrowD(k, 26, 16.5, 30.5, 2.3, 5, 5, Blue, BlueE); },
        ["구조물배치"] = k => { Struct(k); Star(k, 26, 6, 5.6); },
        ["구조물내보내기"] = k => { Struct(k); ArrowR(k, 17.5, 25, 31, 2.3, 5, 5, Blue, BlueE); },
        ["구조물지우기"] = k => { Struct(k); Erase(k); },
    };

    private static void Info(Ink k, bool dot)
    {
        k.Shape(Circle(16, 16.5, 12.5), Blue, BlueE);
        k.Fill(Circle(16, 10.5, 2.1), White);
        k.Fill(Box(14.2, 14, 3.6, 10, 1), White);
        if (dot)
        {
            k.Fill(Circle(26, 6, 5.6), White);
            k.Shape(Circle(26, 6, 4.4), C("#F08A24"), C("#A5520A"));
        }
    }

    /// <summary>미리보기 · 검사용 — 그릴 수 있는 이름 전부.</summary>
    internal static IEnumerable<string> Kinds => Icons.Keys;
}
