using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace DH.Grading.Civil;

/// <summary>★★★[계획 4단계] <b>사면 예시 그림 — 팝업과 도킹창이 함께 쓴다.</b>
///
/// <para>JACK 0908: <i>"정지옵션의 기능들을 모두 그 도킹창 안에 이식해 줘. 예시 그림까지도."</i></para>
///
/// <para><b>왜 떼어냈나.</b> 이 그림은 <see cref="GradingDialog"/> 안에 <b>인스턴스 메서드</b>로 있어
/// 입력칸(<c>TextBox</c>)을 직접 읽었다. 도킹창은 그 칸들을 갖고 있지 않으므로
/// 그대로는 못 쓴다 — <b>값만 받는 함수</b>로 바꿔야 둘이 같은 그림을 그린다.
/// 베껴 두면 한쪽만 고쳐진다(§20·§26에서 되풀이해 배운 실패).</para>
///
/// <para>★<b>그리는 계산은 한 줄도 안 고쳤다.</b> 220줄을 <b>그대로</b> 옮겼고,
/// 바뀐 것은 <b>값을 어디서 얻느냐</b>뿐이다(입력칸 → <see cref="Spec"/>).
/// 그림 모양이 달라질 여지를 아예 안 만든 것이다.</para>
///
/// <para>★<b>크기는 <c>Viewbox</c>가 맞춘다</b>(<see cref="Wrap"/>). 계산은 여전히
/// 420×300 자리에서 하고 화면에 맞춰 늘린다 — 좌표를 다시 짜면 그때부터 그림이 달라진다.</para></summary>
internal static class SlopeDiagram
{
    /// <summary>그림이 그려지는 <b>논리 크기</b> — 화면 크기와 별개다(<see cref="Wrap"/>가 늘린다).</summary>
    internal const double W0 = 420, H0 = 300;

    /// <summary>그림 하나를 그리는 데 필요한 값 전부 — <b>컨트롤이 아니라 값</b>이다.</summary>
    internal readonly record struct Spec(
        /// <summary>절토면 참, 성토면 거짓.</summary>
        bool Cut,
        /// <summary>단높이(m).</summary>
        double BenchH,
        /// <summary>소단폭(m). <b>가시설이면 0</b>이다 — 소단 개념이 없다.</summary>
        double BenchW,
        /// <summary>구배 1:n — <b>하한이 적용된</b> 값(그림이 실제와 같아지게).</summary>
        double Slope,
        /// <summary>사용자가 <b>친 그대로</b>의 구배 — 수직(옹벽·가시설)인지 가르는 데 쓴다.
        /// <para>하한을 먹인 값으로 가르면 <b>0을 쳐도 수직이 안 된다</b>.</para></summary>
        double SlopeRaw,
        /// <summary>산지 대소단을 쓰는가. <b>가시설이면 거짓</b>이다.</summary>
        bool Terrace,
        /// <summary>대소단 간격(m).</summary>
        double TerraceInterval,
        /// <summary>대소단 폭(m).</summary>
        double TerraceWidth,
        /// <summary>옹벽 형태 — 수직일 때만 쓴다.</summary>
        WallStyle Style,
        /// <summary>수직으로 볼 구배 문턱(<c>GradingSettings.WallGateSlope</c>).</summary>
        double WallGate);

    /// <summary>★<b>화면 크기에 맞춰 늘어나는 그림틀</b>을 만든다.
    /// <para>안쪽 <see cref="Canvas"/>는 언제나 420×300이다 — 그리는 계산이 그 자리를 쓰기 때문이다.
    /// 좁은 도킹창에서는 <c>Viewbox</c>가 <b>비율을 지키며</b> 줄여 준다.</para></summary>
    internal static Viewbox Wrap(out Canvas canvas)
    {
        canvas = new Canvas { Width = W0, Height = H0 };
        return new Viewbox
        {
            Child = canvas,
            Stretch = Stretch.Uniform,
            StretchDirection = StretchDirection.Both,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
    }

    /// <summary>한쪽 예시(절토/성토) — 계단 단면, 토사 채움, 단높이·소단폭 치수, 구배 1:n,
    /// 원지반 점선, 대소단, 수직이면 형태별 옹벽 단면.</summary>
    internal static void Draw(Canvas c, in Spec spec)
    {
        if (c == null) return;
        c.Children.Clear();
        var profile = new SolidColorBrush(Color.FromRgb(0x33, 0x66, 0x33)); // 정지면(짙은 초록)
        var dim = new SolidColorBrush(Color.FromRgb(0x99, 0x99, 0x99));     // 치수선(회색)
        var txt = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44));     // 글씨

        void L(double x1, double y1, double x2, double y2, Brush b, double th = 1.0, bool dash = false)
        {
            var ln = new System.Windows.Shapes.Line { X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, Stroke = b, StrokeThickness = th };
            if (dash) ln.StrokeDashArray = new DoubleCollection { 3, 3 };
            c.Children.Add(ln);
        }
        void T(double x, double y, string str, double size = 10)
        {
            var tb = new TextBlock { Text = str, FontSize = size, Foreground = txt };
            Canvas.SetLeft(tb, x); Canvas.SetTop(tb, y);
            c.Children.Add(tb);
        }

        // ★★★[4단계 시험이 잡았다] <b>부품이 스스로 막는다.</b>
        //
        //   <c>NaN</c>이나 무한대가 하나라도 섞이면 WPF가 <i>"NaN은 X2 속성의 유효한 값이 아닙니다"</i>로
        //   <b>그 자리에서 던진다</b>. 팝업은 입력칸을 <c>TryParse</c>+<c>Clamp</c>로 걸러 왔지만,
        //   이제 이것은 <b>둘이 함께 쓰는 부품</b>이다 — 도킹창이 그 검사를 빠뜨리면
        //   <b>그림이 아니라 창이 죽는다</b>. 부르는 쪽을 믿지 않는다.
        //
        //   ★기본값은 <b>팝업이 쓰던 것과 같은 값</b>으로 맞췄다 — 두 화면이 달라질 여지를 안 만든다.
        static double Ok(double v, double dflt, double lo, double hi)
            => double.IsNaN(v) || double.IsInfinity(v) ? dflt : System.Math.Clamp(v, lo, hi);

        // ★값은 <b>받아서</b> 쓴다 — 종전엔 여기서 입력칸을 읽었다(그것이 이 파일이 생긴 이유다).
        bool cut = spec.Cut;
        double H = Ok(spec.BenchH, 5, 0.2, 60);
        double W = Ok(spec.BenchW, 1, 0, 60);
        double n = Ok(spec.Slope, 1.5, 0, 30);
        bool terrace = spec.Terrace;
        double TW = Ok(spec.TerraceWidth, 15, 0, 120);
        double TI = Ok(spec.TerraceInterval, 15, 1, 200);
        var style = spec.Style;
        var wallLine = new SolidColorBrush(Color.FromRgb(0x50, 0x50, 0x50));
        double cw = c.Width, ch = c.Height;

        int nR;                       // 벽면(riser) 수
        double[] flats;               // riser 사이 평탄 폭(m) — 대소단 자리엔 TW
        int terrFlat = -1;            // 대소단인 flat 인덱스
        if (terrace)
        {
            int kTerr = (int)System.Math.Clamp(System.Math.Round(TI / System.Math.Max(H, 0.1)), 1, 6);
            nR = kTerr + 1;
            flats = new double[nR - 1];
            for (int k = 0; k < flats.Length; k++) flats[k] = W;
            terrFlat = kTerr - 1;
            flats[terrFlat] = TW;
        }
        else { nR = 2; flats = new[] { W }; }
        double runG = H * n;
        double flatsSum = 0; foreach (var f in flats) flatsSum += f;
        double geomW = nR * runG + flatsSum;
        double geomH = nR * H;
        double x0 = 88;
        double availW = cw - x0 - 44, availH = ch - 108;
        double s = System.Math.Min(availW / System.Math.Max(geomW, 0.01), availH / System.Math.Max(geomH, 0.01));
        double rp = runG * s, wp = W * s, twp = TW * s, hp = H * s;

        // 레벨 y — 절토=위로 올라가는 계단 / 성토=아래로 내려가는 계단(실단면 방향).
        double yPlan = cut ? ch - 52 : 56;
        double dy = cut ? -1 : 1;
        double Y(int lvl) => yPlan + dy * hp * lvl;

        // 프로파일 정점 + 벽면(riser) 목록 + flat 시작 x 기록
        var pts = new List<Point> { new(20, yPlan), new(x0, yPlan) };
        var risers = new List<(double xa, double ya, double xb, double yb)>();
        var flatX = new double[flats.Length];
        double xcur = x0;
        for (int k = 0; k < nR; k++)
        {
            risers.Add((xcur, Y(k), xcur + rp, Y(k + 1)));
            xcur += rp;
            pts.Add(new Point(xcur, Y(k + 1)));
            if (k < nR - 1)
            {
                flatX[k] = xcur;
                xcur += flats[k] * s;
                pts.Add(new Point(xcur, Y(k + 1)));
            }
        }
        // [JACK 0728] 가로폭 항상 동일 — 기하가 좁으면 상단(초록)을 오른쪽 끝까지 연장하고 토사도 채움.
        double xe = cw - 6;
        pts.Add(new Point(xe, Y(nR)));

        // 토사(흙) 채움 — 프로파일 아래(절토=원지반 흙 / 성토=쌓은 흙+지반).
        var soil = new System.Windows.Shapes.Polygon { Fill = new SolidColorBrush(Color.FromArgb(0x55, 0xC8, 0xA9, 0x6E)) };
        var pc = new PointCollection();
        foreach (var q in pts) pc.Add(q);
        pc.Add(new Point(xe, ch - 16)); pc.Add(new Point(20, ch - 16));
        soil.Points = pc;
        c.Children.Add(soil);

        // 정지면 프로파일(초록)
        for (int i = 0; i + 1 < pts.Count; i++)
            L(pts[i].X, pts[i].Y, pts[i + 1].X, pts[i + 1].Y, profile, 2.6);

        // 원지반(점선)
        if (cut)
        {
            L(x0, yPlan, xe, Y(nR) - 12, dim, 1.2, dash: true);
            T(System.Math.Max(xe - 66, 70), System.Math.Max(Y(nR) - 34, 4), "원지반", 11);
        }
        else
        {
            L(xe, Y(nR), 20, System.Math.Min(Y(nR) + 18, ch - 20), dim, 1.2, dash: true);
            T(System.Math.Max(xe - 66, 70), System.Math.Min(Y(nR) + 6, ch - 22), "원지반", 11);
        }

        // 단높이(세로 치수 + 값) — 첫 단
        double dX = x0 - 16;
        L(dX, Y(0), dX, Y(1), dim, 1.2);
        L(dX - 4, Y(0), dX + 4, Y(0), dim, 1.2); L(dX - 4, Y(1), dX + 4, Y(1), dim, 1.2);
        T(8, (Y(0) + Y(1)) / 2 - 16, "단높이", 12);
        T(8, (Y(0) + Y(1)) / 2 - 1, $"{H:0.##}m", 11);

        // 소단폭(가로 치수 + 값) — 첫 '일반' 소단에(대소단이면 다음 소단, 없으면 텍스트만)
        int wFlat = -1;
        for (int k = 0; k < flats.Length; k++) if (k != terrFlat) { wFlat = k; break; }
        if (wFlat >= 0 && wp >= 12)
        {
            double bx1 = flatX[wFlat], bx2 = bx1 + wp, by = Y(wFlat + 1);
            L(bx1, by - 14, bx2, by - 14, dim, 1.2);
            L(bx1, by - 18, bx1, by - 10, dim, 1.2); L(bx2, by - 18, bx2, by - 10, dim, 1.2);
            T((bx1 + bx2) / 2 - 22, by - 48, "소단폭", 12);
            T((bx1 + bx2) / 2 - 14, by - 33, $"{W:0.##}m", 11);
        }
        else T(System.Math.Min(x0 + rp, cw - 130), Y(1) - 32, $"소단폭 {W:0.##}m", 11);

        // 대소단(계단식 산지) — 간격 도달 단 뒤 넓은 평탄에 표기
        if (terrace && terrFlat >= 0 && twp >= 14)
        {
            double tx1 = flatX[terrFlat], tx2 = tx1 + twp, ty = Y(terrFlat + 1);
            T((tx1 + tx2) / 2 - 36, ty + (cut ? 6 : -20), $"대소단 {TW:0.#}m", 11);
        }

        // 구배 값
        T(System.Math.Min(x0 + rp + wp + rp * 0.3 + 6, cw - 110), (Y(1) + Y(2 > nR ? nR : 2)) / 2 - 8, $"구배 1:{n:0.##}", 11);

        // [JACK 0728] 옹벽 단면(형태별) — 구배≤0.05(수직) + 옹벽 형태 선택 시.
        //   벽체는 면 '앞(공기 쪽)'에 그려 표면이 보이게(절토=면 왼쪽/성토=면 오른쪽), 앵커는 흙 쪽으로.
        double slopeRaw = Ok(spec.SlopeRaw, 1.5, 0, 30);
        bool isWall = slopeRaw <= Ok(spec.WallGate, 0.05, 0, 1) + 1e-9 && style != WallStyle.없음_사면;
        if (isWall)
        {
            double airDir = cut ? -1 : 1;   // 공기(전면) 방향
            foreach (var (xa, ya, xb, yb) in risers)
            {
                double faceX = (xa + xb) / 2, wt = 12;
                double ytop = System.Math.Min(ya, yb), ybot = System.Math.Max(ya, yb);
                if (ybot - ytop < 8) continue;
                double rectX = airDir < 0 ? faceX - wt : faceX;   // 전면이 보이도록 면 앞에 배치
                if (style == WallStyle.역T형)
                {
                    // 역T 단면: 벽체 + 저판(흙쪽으로 넓게) — 1단 전용 개념 표현.
                    double soilD = -airDir;
                    var stem = new System.Windows.Shapes.Rectangle
                    {
                        Width = wt, Height = ybot - ytop,
                        Fill = new SolidColorBrush(Color.FromArgb(0x50, 0xD8, 0xD8, 0xD8)),
                        Stroke = wallLine, StrokeThickness = 1.2,
                    };
                    Canvas.SetLeft(stem, rectX); Canvas.SetTop(stem, ytop); c.Children.Add(stem);
                    double slabW = wt * 3.4, slabH = 6;
                    double slabX = soilD > 0 ? rectX - wt * 0.5 : rectX + wt * 1.5 - slabW;
                    var slab = new System.Windows.Shapes.Rectangle
                    {
                        Width = slabW, Height = slabH,
                        Fill = new SolidColorBrush(Color.FromArgb(0x50, 0xD8, 0xD8, 0xD8)),
                        Stroke = wallLine, StrokeThickness = 1.2,
                    };
                    Canvas.SetLeft(slab, slabX); Canvas.SetTop(slab, ybot); c.Children.Add(slab);
                }
                else
                {
                    var r = new System.Windows.Shapes.Rectangle
                    {
                        Width = wt, Height = ybot - ytop,
                        Fill = new SolidColorBrush(Color.FromArgb(0x50, 0xD8, 0xD8, 0xD8)),
                        Stroke = wallLine, StrokeThickness = 1.2,
                    };
                    Canvas.SetLeft(r, rectX); Canvas.SetTop(r, ytop); c.Children.Add(r);
                    if (style == WallStyle.보강토)
                    {
                        for (double yy = ytop + 6; yy < ybot - 2; yy += 7)
                            L(rectX, yy, rectX + wt, yy, wallLine, 0.9);
                    }
                    else // 앵커판넬 — 앵커는 흙 쪽(전면 반대)으로
                    {
                        double soilX = airDir < 0 ? faceX : faceX;      // 흙쪽 시작 = 면 위치
                        double soilDir = -airDir;
                        for (double yy = ytop + 10; yy < ybot - 4; yy += 18)
                        {
                            L(soilX, yy, soilX + soilDir * 24, yy + 9, wallLine, 1.1);
                            var dot = new System.Windows.Shapes.Ellipse { Width = 5, Height = 5, Fill = wallLine };
                            Canvas.SetLeft(dot, soilX + soilDir * 24 - 2.5); Canvas.SetTop(dot, yy + 7); c.Children.Add(dot);
                        }
                    }
                }
            }
            string wallName = style == WallStyle.보강토 ? "보강토 옹벽"
                : style == WallStyle.역T형 ? "역T형 옹벽(1단)" : "앵커판넬 옹벽";
            T(System.Math.Min(x0 + rp + 18, cw - 150), cut ? Y(1) + 10 : Y(1) - 24, wallName, 11);
        }

        // 계획면(부지) 라벨 — 절토=계획면 아래 / 성토=계획면 위
        T(20, cut ? yPlan + 8 : yPlan - 22, "계획면(부지)", 11);
    }
}
