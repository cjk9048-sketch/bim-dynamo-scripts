namespace DH.Grading.Civil.Tutorial;

/// <summary>★[튜토리얼 · JACK 1117] 단계 정하기 — <b>Autodesk 형을 안 쓴다</b>(화면 없이 시험 가능).
/// <para>지금 도면 · 창의 사실(<see cref="Facts"/>)만 보고 단계를 다시 정한다 — 사용자가 다른 일을 하거나 창을 닫아도
/// 사실이 같으면 같은 단계에 머물고, 사실이 앞서면(예: 원지반이 이미 있음) 그 단계로 건너간다.</para></summary>
internal static class TutorialFlow
{
    /// <summary>★[JACK 1117 «단원으로 — 한 지표면에서 이어서»] 단원 — 1 원지반 가져오기(서버 지표면 맛보기 + 수치지도 DXF) · 2 계획부지 생성 · 3 터파기 생성 · 4 종횡단도 생성.</summary>
    internal enum Chapter { Ground = 1, Site = 2, Excav = 3, Section = 4 }
    internal const int ChapterCount = 4;
    /// <summary>★[JACK 1117 «터파기 가시설까지만»] 지금 여는 단원 — 종횡단도(4)는 틀만 두고 뺌.</summary>
    internal static readonly Chapter[] Active = { Chapter.Ground, Chapter.Site, Chapter.Excav };

    internal static string ChapterName(Chapter c) => c switch
    {
        Chapter.Ground => "원지반 가져오기",
        Chapter.Site => "계획부지 생성",
        Chapter.Excav => "터파기 생성",
        _ => "종횡단도 생성",
    };

    internal static Chapter? Next(Chapter c)
    {
        int i = Array.IndexOf(Active, c);
        return i >= 0 && i + 1 < Active.Length ? Active[i + 1] : null;
    }

    internal enum Step
    {
        Ground, Search, Pick, Send, Importing, Dxf,                         // 1 원지반 가져오기
        Draw, Elev, Panel, PickPlan, PickGround, Slope, Build, Wall,       // 2 계획부지 생성
        ExDraw, ExElev, ExPanel, ExPick, ExShore, ExBuild,                 // 3 터파기 생성
        SecPanel, Prof, Xsec, Qty,                                         // 4 종횡단도 생성(틀만)
        Done,
    }

    /// <summary>단계를 정하는 사실 한 벌(단원마다 쓰는 칸만 본다).</summary>
    /// <param name="Ground">1단원: 지금 부분(서버 · DXF)에서 원지반이 새로 생겼나.</param>
    /// <param name="Dxf">1단원 둘째 부분(수치지도 DXF) — 서버 지표면이 됐거나 건너뜀.</param>
    /// <param name="PageSub">지도 페이지의 작은 단계 — 1 검색 · 2 두 점 · 3 가져오기 단추 · 0 보내는 중.</param>
    /// <param name="Fits">★[JACK 1117 «너무 작게 가져와서 에러»] 경계 + 사면 닿는 거리가 원지반 안에 드나 — 아니면 고도 단계에 머문다.</param>
    /// <param name="SlopeChanged">2단원: 절토 · 성토 구배 칸 하나를 바꿨나.</param>
    /// <param name="Wall">2단원: [옹벽 변환]으로 옹벽 구간이 늘었나.</param>
    /// <param name="Shore">3단원: 터파기 창 [가시설] 체크.</param>
    internal readonly record struct Facts(
        bool Ground, bool Importing, bool MapOpen, int PageSub, bool Dxf,
        bool Boundary, bool Elev, bool PanelOpen, bool PlanPicked, bool GroundPicked, bool Grade, bool Fits = true,
        Chapter Ch = Chapter.Site, bool SlopeChanged = true, bool Wall = false,
        bool ExBoundary = false, bool ExElev = false, bool ExPanelOpen = false, bool ExPicked = false, bool Shore = false, bool ExSurf = false,
        bool SecOpen = false, bool Prof = false, bool Xsec = false, bool QtyFile = false);

    internal static int Total(Chapter c) => c switch { Chapter.Ground => 5, Chapter.Site => 7, Chapter.Excav => 4, _ => 4 };

    internal static Step Derive(Facts f)
    {
        switch (f.Ch)
        {
            case Chapter.Ground:
                if (f.Ground) return Step.Done;
                if (f.Importing) return Step.Importing;
                if (f.Dxf) return Step.Dxf;
                if (!f.MapOpen) return Step.Ground;
                return f.PageSub switch { 0 => Step.Importing, 2 => Step.Pick, 3 => Step.Send, _ => Step.Search };
            case Chapter.Excav:
                if (f.ExSurf) return Step.Done;
                if (!f.ExBoundary) return Step.ExDraw;
                if (!f.ExElev) return Step.ExElev;
                if (!f.ExPanelOpen) return Step.ExPanel;
                if (!f.ExPicked) return Step.ExPick;
                if (!f.Shore) return Step.ExShore;
                return Step.ExBuild;
            case Chapter.Section:
                if (f.QtyFile) return Step.Done;
                if (!f.Prof) return f.SecOpen ? Step.Prof : Step.SecPanel;
                if (!f.Xsec) return f.SecOpen ? Step.Xsec : Step.SecPanel;
                return Step.Qty;
            default:
                if (f.Wall) return Step.Done;
                if (f.Grade) return f.PanelOpen ? Step.Wall : Step.Panel;
                if (!f.Boundary) return Step.Draw;
                if (!f.Elev || !f.Fits) return Step.Elev;
                if (!f.PanelOpen) return Step.Panel;
                if (!f.PlanPicked) return Step.PickPlan;
                if (!f.GroundPicked) return Step.PickGround;
                if (!f.SlopeChanged) return Step.Slope;
                return Step.Build;
        }
    }

    /// <summary>단원 안 번호(1부터). 1단원 «만드는 중»은 DXF 부분이면 5 · 2단원 창 열기는 정지면이 있으면 7.</summary>
    internal static int Num(Step s, bool dxf = false, bool graded = false) => s switch
    {
        Step.Ground => 1, Step.Search => 2, Step.Pick => 3, Step.Send => 4, Step.Importing => dxf ? 5 : 4, Step.Dxf => 5,
        Step.Draw => 1, Step.Elev => 2, Step.Panel => graded ? 7 : 3, Step.PickPlan => 3, Step.PickGround => 4, Step.Slope => 5, Step.Build => 6, Step.Wall => 7,
        Step.ExDraw => 1, Step.ExElev => 1, Step.ExPanel => 2, Step.ExPick => 2, Step.ExShore => 3, Step.ExBuild => 4,
        Step.SecPanel => 1, Step.Prof => 2, Step.Xsec => 3, Step.Qty => 4,
        _ => 0,
    };

    internal static bool IsMap(Step s) => s is Step.Search or Step.Pick or Step.Send;
    internal static bool IsPanel(Step s) => s is Step.PickPlan or Step.PickGround or Step.Slope or Step.Build or Step.Wall;
    internal static bool IsExPanel(Step s) => s is Step.ExPick or Step.ExShore or Step.ExBuild;
    internal static bool IsDraw(Step s) => s is Step.Draw or Step.ExDraw;

    /// <summary>단계 글 — «무엇 → 왜» 한 줄(초보용 단답 · 어요/습니다체 · ※ 없음). <paramref name="guide"/> = 따라 그릴 점선(그려 둔 선)이 있나.</summary>
    internal static string Text(Step s, bool dxf, bool guide = true) => s switch
    {
        Step.Ground => dxf ? "[원지반 가져오기] ▼ → [수치지도 DXF] → DXF 파일로 땅 만들기"
                           : "[원지반 가져오기] ▼ → [서버 지표면] → 지도에서 땅 고르기",
        Step.Search => "검색 칸에 «대모산» → [검색] → 그 자리로 이동",
        Step.Pick => $"지도에서 모서리 두 곳 클릭 → 가로 · 세로 {MinRangeM} m 이상(사면 여유)",
        Step.Send => "[이 범위 가져오기] → 원지반 만들기",
        Step.Importing => "원지반 만드는 중 → 잠시 기다리기",
        Step.Dxf => "[원지반 가져오기] ▼ → [수치지도 DXF] → tut_ch1.dxf [열기] → 원지반 만들기",
        Step.Draw => (guide ? "노란 점선 따라" : "노란 점선 안에") + " 닫힌 폴리선(PLINE → 마지막 C) → 계획 경계",
        Step.Elev => "선 클릭 → Ctrl+1 → «고도» 칸 입력 → 계획 높이",
        Step.Panel => "[계획부지 생성] → 계획부지 창 열기",
        Step.PickPlan => "[계획 경계 선택] → 그린 폴리선 클릭 → 정지할 땅",
        Step.PickGround => "[원지반 선택] → 원지반 클릭 → 기준 땅",
        Step.Slope => "절토 · 성토 «구배 1:» 칸 하나 바꾸기(예: 1.5 → 1.0) → 사면 기울기",
        Step.Build => "[계획부지생성하기] → 정지면 만들기",
        Step.Wall => "[계획부지 수정] 탭 → [옹벽 변환] → 소단선 클릭 → Enter → 옹벽",
        Step.ExDraw => (guide ? "노란 점선 따라" : "노란 점선 안에") + " 닫힌 폴리선(PLINE → 마지막 C) → 구조물 바닥",
        Step.ExElev => "선 클릭 → Ctrl+1 → «고도» 칸 입력 → 구조물 바닥 높이",
        Step.ExPanel => "[계획부지 생성] ▼ → [구조물 터파기] → 터파기 창 열기",
        Step.ExPick => "[터파기선 선택] → 그린 바닥 선 클릭 → 팔 자리",
        Step.ExShore => "[가시설] 체크 → 둘레 수직(흙막이)",
        Step.ExBuild => "[터파기 지표면 생성] → 터파기 면 만들기",
        Step.SecPanel => "[종횡단도] → 종횡단도 창 열기",
        Step.Prof => "[종단도 생성] → 노란 점선 따라 점 클릭 → Enter → 종단도",
        Step.Xsec => "[횡단도 생성] → 놓을 자리 클릭 → 측점마다 횡단도",
        Step.Qty => "[수량 산출서] → 창 [만들기] → 토공 수량 엑셀",
        _ => "완료",
    };

    /// <summary>단원 끝 글.</summary>
    internal static string DoneText(Chapter c) => c switch
    {
        Chapter.Ground => "완료 → 원지반 가져오기 끝",
        Chapter.Site => "완료 → 정지면 · 옹벽 · 계획부지 생성 끝",
        Chapter.Excav => "완료 → 가시설 터파기 · 튜토리얼 끝",
        _ => "완료 → 종단도 · 횡단도 · 수량 산출서 끝",
    };

    internal const string NoteOpenSlope = "[사면 설정] 펼치기 → 구배 칸 보이기";
    internal const string NoteNoZ = "고도값 누락됨 → 선 고도 넣고 다시 선택";
    internal const string NoteWallFail = "옹벽 안 바뀜 → 소단선 클릭 → Enter";
    internal const string NoteNoGuide = "그려 둔 선 없음 → 노란 점선 안에 그리기";

    // ── 알림 한 줄(단계 글 아래 주황) ──
    internal const string NoteNet = "사내망 연결 필요 → VPN 확인";
    internal const string NoteNetChecking = "사내망 확인 중…";
    internal const string NoteImportFail = "원지반 못 만듦 → 사내망 · 범위 확인 → 다시";
    internal const string NoteMapClosed = "지도 창 닫힘 → 다시 [서버 지표면]";
    internal const string NoteMapDead = "지도 못 띄움 → 인터넷 · VWorld 키 확인";
    internal const string NoteOpenLine = "선이 열림 → 마지막에 C(닫기)";
    internal const string NoteOutside = "선이 원지반 밖 → 노란 점선 안에 다시";
    internal const string NoteLineLost = "경계 선이 없어짐 → 다시 그리기";
    internal const string NotePanelClosed = "계획부지 창 닫힘 → 다시 열기";
    internal const string NoteGradeFail = "정지면 못 만듦 → 명령창 글 확인 → 다시";
    internal const string NoteAway = "튜토리얼 도면 밖 → [돌아가기]";
    internal static readonly string NoteSmallRange = $"범위 작음 → 더 넓게({MinRangeM} m 이상)";
    internal const string NoteEdge = "경계가 원지반 끝에 가까움 → 노란 점선 안에 더 작게 · 또는 고도 = 추천 고도";
    internal const string NoteGroundTight = "원지반이 좁음 → [다시 시작] 후 더 넓게 가져오기";
    internal const string NoteNoDxf = "튜토리얼 DXF 없음 → 이 부분 건너뜀";
    internal const string NoteServerOk = "서버 지표면 됨 → 다음: 수치지도 DXF";
    internal const string NoteExOutside = "선이 계획부지 밖 → 노란 점선 안에 다시";
    internal const string NoteExHigh = "바닥이 부지보다 높음 → 추천 고도로";
    internal const string NoteExPanelClosed = "터파기 창 닫힘 → 다시 열기";
    internal const string NoteSecClosed = "종횡단도 창 닫힘 → 다시 [종횡단도]";
    internal const string NoteExFail = "터파기 못 만듦 → 명령창 글 확인 → 다시";
    internal const string NoteSaveFail = "결과 저장 못 함 → 다음 단원은 기본 자료로";

    // ══ ★★[JACK 1117 «너무 작게 가져와서 '사면이 원지반 경계를 벗어남' 에러 → 가져오는 단계에서 넉넉히»] 사면 여유 셈 ══
    //   사면 닿는 거리(수평) ≈ 경계 위 |원지반 − 계획고| 최대 × 실효 구배 + 여유 10 m.
    //   실효 구배 = n + 소단폭 / 단높이(단마다 소단 하나) — 절토 · 성토 중 큰 쪽(1:1.5 · 5 m · 1 m → 1.7).
    //   땅 높이는 부르는 쪽이 준다(gz: 원지반 밖이면 null) — 그래서 여기는 Civil 없이 시험한다.

    /// <summary>지도에서 고를 최소 범위(가로 · 세로 m) — 150 m 부지 + 사면이 들어갈 만큼.</summary>
    internal const int MinRangeM = 600;
    internal const double MarginM = 10;
    /// <summary>점선 네모 반폭 위 · 아래 한도(m) — 150 m 네모까지 · 30 m 아래로는 안 줄인다.</summary>
    internal const double HintHalfMax = 75, HintHalfMin = 15;

    internal static double NEff(double cutN, double cutH, double cutW, double fillN, double fillH, double fillW)
    {
        static double One(double n, double h, double w) => Math.Max(0, n) + (h > 0.01 ? Math.Max(0, w) / h : 0);
        double e = Math.Max(One(cutN, cutH, cutW), One(fillN, fillH, fillW));
        return e > 0.05 ? e : 1.5;
    }

    /// <summary>닫힌 경계를 따라 약 <paramref name="stepM"/> 간격 점(꼭짓점 포함) — 점마다 그 변의 바깥 법선.</summary>
    internal static List<(double X, double Y, double Nx, double Ny)> Ring(IReadOnlyList<(double X, double Y)> poly, double stepM = 5)
    {
        var res = new List<(double, double, double, double)>();
        int n = poly.Count;
        if (n < 3) return res;
        double area2 = 0;
        for (int i = 0; i < n; i++) { var a = poly[i]; var b = poly[(i + 1) % n]; area2 += a.X * b.Y - b.X * a.Y; }
        double sgn = area2 >= 0 ? 1 : -1;   // 반시계(+)면 바깥 = 오른쪽 법선
        for (int i = 0; i < n; i++)
        {
            var a = poly[i]; var b = poly[(i + 1) % n];
            double dx = b.X - a.X, dy = b.Y - a.Y, len = Math.Sqrt(dx * dx + dy * dy);
            if (len < 1e-9) continue;
            double nx = sgn * dy / len, ny = -sgn * dx / len;
            int k = Math.Max(1, (int)Math.Ceiling(len / stepM));
            for (int j = 0; j < k; j++) res.Add((a.X + dx * j / k, a.Y + dy * j / k, nx, ny));
        }
        return res;
    }

    /// <summary>사면 닿는 거리(m) — 경계 위 |땅 − z| 최대 × 실효 구배 + 여유. 땅을 못 읽는 점이 있으면 null(경계가 원지반 밖).</summary>
    internal static double? Reach(IReadOnlyList<(double X, double Y)> poly, double z, Func<double, double, double?> gz, double nEff)
    {
        double dz = 0;
        foreach (var p in Ring(poly))
        {
            var g = gz(p.X, p.Y);
            if (g is not double gv) return null;
            dz = Math.Max(dz, Math.Abs(gv - z));
        }
        return dz * nEff + MarginM;
    }

    /// <summary>경계를 바깥으로 <paramref name="reach"/>만큼 민 자리가 모두 원지반 안인가(변 법선 · 꼭짓점은 두 법선 모두).</summary>
    internal static bool Fits(IReadOnlyList<(double X, double Y)> poly, double reach, Func<double, double, double?> gz)
    {
        var ring = Ring(poly);
        if (ring.Count == 0) return false;
        for (int i = 0; i < ring.Count; i++)
        {
            var p = ring[i];
            if (gz(p.X + p.Nx * reach, p.Y + p.Ny * reach) == null) return false;
            var q = ring[(i + ring.Count - 1) % ring.Count];   // 꼭짓점 — 앞 변 법선으로도
            if (gz(p.X + q.Nx * reach, p.Y + q.Ny * reach) == null) return false;
        }
        return true;
    }

    /// <summary>경계를 계획고 <paramref name="z"/>로 정지할 때 사면이 원지반 안에 드나 — (든다, 닿는 거리).</summary>
    internal static (bool Ok, double Reach) Check(IReadOnlyList<(double X, double Y)> poly, double z, Func<double, double, double?> gz, double nEff)
    {
        var r = Reach(poly, z, gz, nEff);
        if (r is not double rv) return (false, double.NaN);
        return (Fits(poly, rv, gz), rv);
    }

    /// <summary>네모 안 땅 평균(격자 N×N · 기본 20) — 못 읽으면 null.</summary>
    internal static double? MeanIn(IReadOnlyList<(double X, double Y)> poly, Func<double, double, double?> gz, int N = 20)
    {
        double x0 = double.MaxValue, y0 = double.MaxValue, x1 = double.MinValue, y1 = double.MinValue;
        foreach (var p in poly) { x0 = Math.Min(x0, p.X); y0 = Math.Min(y0, p.Y); x1 = Math.Max(x1, p.X); y1 = Math.Max(y1, p.Y); }
        double s = 0; int c = 0;
        for (int i = 0; i < N; i++)
            for (int j = 0; j < N; j++)
            {
                double x = x0 + (i + 0.5) / N * (x1 - x0), y = y0 + (j + 0.5) / N * (y1 - y0);
                if (!InPoly(poly, x, y)) continue;
                if (gz(x, y) is double g) { s += g; c++; }
            }
        return c == 0 ? null : s / c;
    }

    internal static bool InPoly(IReadOnlyList<(double X, double Y)> poly, double x, double y)
    {
        bool c = false;
        for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
        {
            var a = poly[i]; var b = poly[j];
            if ((a.Y > y) != (b.Y > y) && x < (b.X - a.X) * (y - a.Y) / (b.Y - a.Y) + a.X) c = !c;
        }
        return c;
    }

    internal static (double X, double Y)[] Square(double cx, double cy, double h) =>
        new[] { (cx - h, cy - h), (cx + h, cy - h), (cx + h, cy + h), (cx - h, cy + h) };

    /// <summary>점선 네모 반폭 — 가운데에서 75 m부터 5 m씩 줄여 «평균 고도로 정지해도 사면이 원지반 안»인 첫 크기.
    /// 15 m까지 안 되면 Fits = 거짓(원지반이 좁음) · 반폭은 15 m.</summary>
    internal static (double Half, bool Fits) HintHalf(double cx, double cy, double maxHalf, Func<double, double, double?> gz, double nEff)
    {
        double h = Math.Min(HintHalfMax, maxHalf);
        for (; h >= HintHalfMin - 1e-9; h -= 5)
        {
            var sq = Square(cx, cy, h);
            if (MeanIn(sq, gz) is not double z) continue;
            if (Check(sq, z, gz, nEff).Ok) return (h, true);
        }
        return (HintHalfMin, false);
    }
    /// <summary>★[1117 단원] 점선 네모 자리 고르기 — 원지반 범위(x0~y1) 안쪽 가운데 둘레 3×3 자리 · 큰 네모부터(75 → 15 m 반폭, 10 m씩) ·
    /// 같은 크기면 가운데에 가까운 자리. 평균 고도로 정지해도 사면이 원지반 안에 드는 첫 자리. 없으면 가운데 · 15 m · Fits 거짓.</summary>
    internal static (double Cx, double Cy, double Half, bool Fits) BestHint(double x0, double y0, double x1, double y1, Func<double, double, double?> gz, double nEff)
    {
        double cx = (x0 + x1) / 2, cy = (y0 + y1) / 2, w = x1 - x0, h = y1 - y0;
        var spots = new List<(double X, double Y)>();
        for (int i = -1; i <= 1; i++) for (int j = -1; j <= 1; j++) spots.Add((cx + i * w * 0.15, cy + j * h * 0.15));
        spots.Sort((a, b) => (Math.Abs(a.X - cx) + Math.Abs(a.Y - cy)).CompareTo(Math.Abs(b.X - cx) + Math.Abs(b.Y - cy)));
        double maxHalf = Math.Min(HintHalfMax, Math.Min(w, h) * 0.3);
        for (double hh = maxHalf; hh >= HintHalfMin - 1e-9; hh -= 10)
            foreach (var p in spots)
            {
                var sq = Square(p.X, p.Y, hh);
                if (MeanIn(sq, gz, 12) is not double z) continue;
                if (Check(sq, z, gz, nEff).Ok) return (p.X, p.Y, hh, true);
            }
        return (cx, cy, HintHalfMin, false);
    }
}
