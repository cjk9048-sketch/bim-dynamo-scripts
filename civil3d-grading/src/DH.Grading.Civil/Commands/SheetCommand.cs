using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;
using CivilApp = Autodesk.Civil.ApplicationServices;
using CivilDb = Autodesk.Civil.DatabaseServices;

namespace DH.Grading.Civil.Commands;

/// <summary>★[JACK 0810] <b>[도곽] — A1 한 장에 종단도를 앉힌다.</b>
///
/// <para>JACK: "이 작업의 궁극적인 목적은 도면화야. 먼저 도면의 기본 도곽 크기는 A1(594×841)로,
/// 도곽 범위 네모가 그려지고 안쪽으로 위아래 20·좌우 25 오프셋된 네모가 하나 더 그려지고,
/// 그 안에 전체 내부 높이의 <b>2/3</b> 높이로 만들어졌으면 좋겠어
/// (최종적으로 1/3은 종평면도, 1/3은 종단, 1/3은 밴드).
/// 그리고 종단의 폭은 도곽 한 장에 들어오게."</para>
///
/// <para><b>배치(레이아웃)에 1:1로 그린다</b>(JACK 확정). 도곽은 종이 크기 그대로 841×594mm이고,
/// 모형공간의 종단도는 뷰포트를 통해 축척을 걸어 본다 — AutoCAD 정석이고 출력이 깔끔하다.</para>
///
/// <para><b>축척은 노선 길이에 맞춰 자동</b>(JACK 확정). 안쪽 폭 791mm에 노선이 들어가야 하므로
/// 필요한 축척은 <c>노선길이(mm) ÷ 791</c>보다 커야 한다. 표준 축척 중 그 조건을 만족하는
/// <b>가장 작은 값</b>(=가장 크게 보이는 축척)을 고른다. 한 장을 넘기면 경고한다 —
/// 장 넘김은 나중에 관로 기능에서 '정해진 거리마다'로 붙일 자리다(JACK 예고).</para>
///
/// <para><b>수직과장도 자동</b>(JACK 확정). 종단 그래프가 내부 높이의 1/3(≈184.7mm)을 채우도록
/// 표고 범위를 재서 회사 표준 뷰 스타일(수직과장 없음·2.5·5) 중 <b>넘치지 않는 가장 큰 것</b>을 고른다.
/// 넘치는 쪽을 고르면 그래프가 밴드 영역을 침범한다 — 모자란 건 보기 싫을 뿐이지만 넘치면 도면이 깨진다.</para></summary>
public static class SheetCommand
{
    // ── 도곽 규격(mm) — JACK 지정
    private const double SheetW = 841.0, SheetH = 594.0;   // A1 가로
    private const double MarginLR = 25.0, MarginTB = 20.0;
    private const string LayoutBase = "DH-종단도";
    private const string LayFrame = "DH-도곽";
    private const string LayFrameModel = "DH-도곽범위(모형)";

    /// <summary>★[JACK 0810 확정] 기준 축척 — 세로 1:200, 가로 1:1000(수직과장 5배).
    /// "가로 세로 축척에 대해서 정의를 다시 하자. 일단은 V=1:200, H=1:1000을 기준으로 먼저 만들어 보자."
    /// 자동으로 고르면 도면마다 축척이 달라져 비교가 안 되고, 회사 스타일도 특정 축척을 전제한다.</summary>
    private const double VScale = 200.0, HScale = 1000.0;

    /// <summary>흔히 쓰는 표준 축척 — 이 중에서만 고른다(현장에서 읽을 수 있는 값이어야 한다).</summary>
    /// <summary>★[JACK 0810 확정] "축척은 1:50, 1:80, 1:100, 1:120, 1:150, 1:200 이렇게 세분화하고
    /// 1:200 이상은 설계도서에서 주로 쓰는 축척으로 가져가."
    /// <para>200 이하를 촘촘히 둔 이유: 부지정지 종단도는 짧은 구간을 크게 그리는 일이 많아
    /// 100→200으로 건너뛰면 그림이 절반으로 줄어 자리가 크게 남는다(참고 도면도 1:120이었다).
    /// 200 위로는 설계도서 관례값(250·300·500·600·1000·1200·2000·2500·3000·5000)만 쓴다 —
    /// 도면에 적힌 축척은 현장에서 자로 재는 값이라 관례를 벗어나면 안 된다.</para></summary>
    private static readonly double[] Scales =
        { 50, 80, 100, 120, 150, 200, 250, 300, 500, 600, 1000, 1200, 2000, 2500, 3000, 5000 };

    private static double InnerW => SheetW - 2 * MarginLR;   // 791
    private static double InnerH => SheetH - 2 * MarginTB;   // 554
    /// <summary>★[JACK 0810] 회사 참고 도면(C-005)의 실제 구도 — 1/3씩 균등이 아니다.
    /// "제목부 0.5, 종단면도 3, 종단 3.5, 밴드 3 정도 되는 것 같아."
    /// 합 10으로 나눠 내부 높이를 배분한다.</summary>
    private const double UTitle = 0.5, UPlan = 3.0, UGraph = 3.5, UBand = 3.0;
    private static double Unit => InnerH / (UTitle + UPlan + UGraph + UBand);   // 55.4mm
    private static double TitleH => Unit * UTitle;    // 27.7  제목부
    private static double PlanH => Unit * UPlan;      // 166.2 종평면도
    private static double GraphH => Unit * UGraph;    // 193.9 종단 그래프
    private static double BandH => Unit * UBand;      // 166.2 밴드 표

    /// <summary>뷰포트가 실제로 쓰는 높이 — 종단 그래프 + 밴드 표(제목부·종평면도는 그 위).</summary>
    private static double ViewH => GraphH + BandH;    // 360.1

    /// <summary><b>여백 목표</b> — 자리의 92%까지만 차면 보기 좋다는 기준.
    /// JACK 0810: "너무 딱 맞으면 그러니깐 약간의 버퍼는 줘서 도면이 좀 균형감 있게 해야지."
    /// <para>★[v23.5] <b>이 값을 축척 계산에 곱하지 않는다.</b> 곱하면 표준 축척 올림과 겹쳐
    /// 그림이 한 단계(20~60%) 작아진다 — 8% 여백을 사려고 그만큼을 내주는 셈이다.
    /// 여백은 올림이 남기는 몫으로 얻고, 이 값은 <b>못 미쳤을 때 로그로 알리는 임계</b>로만 쓴다.</para></summary>
    private const double Fill = 0.92;

    [CommandMethod("DHSHEET", CommandFlags.Modal)]
    public static void Run()
    {
        var doc = AcadApp.DocumentManager.MdiActiveDocument;
        if (doc == null) return;
        var db = doc.Database;
        Editor ed = doc.Editor;
        var log = new System.Text.StringBuilder();

        // ── ① 대상 종단도 찾기
        ObjectId pvId = PickProfileView(db, ed, out string pvName);
        if (pvId.IsNull) return;
        log.AppendLine($"종단도 '{pvName}'");
        string sum = Build(db, ed, pvId, log);
        try { DiagLog.Append("\n■ DHSHEET(도곽)\n  " + log.ToString().TrimEnd().Replace("\n", "\n  ") + "\n"); } catch { }
        ed.WriteMessage("\n[도곽] " + sum + $"\n  자세한 내용: {DiagLog.FilePath}");
    }

    /// <summary>도곽 만들기 본체 — <b>[종단도]가 끝에서 바로 부른다</b>(JACK 0810:
    /// "도곽 버튼이 왜 필요하지? 그냥 종단도 누르면 모형탭하고 배치까지 자동으로 되야 되").
    /// 버튼을 따로 두지 않는 이유가 그것이다. 명령(DHSHEET)은 이미 만든 종단도에 다시 씌울 때만 쓴다.
    /// <para>나중에 '도곽 버튼'이 생긴다면 그건 <b>불러오기 전용</b>이 될 자리다 —
    /// 회사 도곽 파일을 골라 배치탭에 붙여넣는 기능(JACK 예고, 지금은 구현하지 않는다).</para></summary>
    public static string Build(Database db, Editor ed, ObjectId pvId, System.Text.StringBuilder log)
    {
        // ── ① 밴드를 표로 만든다(칸 균등·간격 0). 크기를 재기 **전에** 해야 뒤 계산이 맞는다.
        string bandNote = NormalizeBands(db, pvId, log);

        // ── ② 축척과 수직과장을 **함께** 푼다 — 따로 정하면 서로를 무너뜨린다.
        string veNote = FitSheet(db, pvId, log, out double scale, out bool overflow);

        // ── ②-a 표 끝 여백은 **종이 기준**이므로 축척을 알아야 모형 거리로 바꿀 수 있다.
        //   ★[JACK 0810] "축척에 따라 모든 기능이 자연스럽게 연동되어야 해."
        //   종전 판은 여기서 기준 축척 200을 <b>박아 놨다</b> — 1:1000 도면이면 여백이 1/5로 쪼그라든다.
        //   그래서 <b>축척을 먼저 풀고 → 꼬리를 붙이고 → 축척을 다시 푼다</b>.
        //   꼬리는 폭의 1~2%라 대개 같은 축척이 다시 나오지만, 경계에 걸리면 2차에서 바로잡힌다.
        if (ExtendTail(db, pvId, scale, log))
            veNote = FitSheet(db, pvId, log, out scale, out overflow);

        // ── ②-b 뷰 스타일이 정해진 **뒤에** 왼쪽 축 눈금을 세운다(JACK: "왼쪽 바를 스케일(체크)로").
        SetAxisTicks(db, pvId, scale, log);
        SetBandWeeding(db, pvId, scale, log);   // 굴곡부 라벨 솎아내기 — 축척을 알아야 정할 수 있다
        PolishView(db, pvId, log);      // V·H 표시 자리 · 종단선 화살표
        DrawScaleBar(db, pvId, scale, log);   // 흑백 교차 표고바 — 직접 그린다(축 스타일엔 그 기능이 없다)

        // ── ③ 정해진 스타일로 실제 크기를 다시 잰다(밴드까지 포함한 전체 상자)
        Extents3d ext;
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            var pv = (Entity)tr.GetObject(pvId, OpenMode.ForRead);
            ext = pv.GeometricExtents;
            tr.Commit();
        }
        catch (System.Exception ex)
        { return "종단도 크기를 재지 못했습니다 — " + ex.Message; }

        // ★[JACK 0810] 종단도 경계상자에는 **밴드가 들어 있지 않다**(실측: 높이 30.0m = 표고범위 그대로).
        //   그래서 도곽이 그래프만 덮고 밴드 6칸이 통째로 밖으로 나갔다. 밴드 높이만큼 아래로 넓힌다.
        if (LastBandModelH > 1e-6)
            ext = new Extents3d(new Point3d(ext.MinPoint.X, ext.MinPoint.Y - LastBandModelH, ext.MinPoint.Z),
                                ext.MaxPoint);

        // ── ④ **모형탭에 도곽 범위를 먼저 놓는다.** 배치는 그걸 가져다 보여줄 뿐이다.
        //   JACK 0810: "배치탭에도 1장씩 자동으로 모형탭의 도각 범위를 가져와야 해.
        //               배치탭에서 사용자가 도곽을 가져오기만 할 수 있게."
        //   이 구조라야 나중에 관로에서 '정해진 거리마다 장이 넘어가게'가 그대로 얹힌다 —
        //   모형에 도곽을 여러 장 늘어놓고 배치를 그 수만큼 만들면 된다.
        var frames = DrawModelFrames(db, ext, scale, log);

        // ── ⑤ 도곽 한 장마다 배치 하나
        string layName;
        try { layName = MakeLayout(db, ed, frames[0], scale, log); }
        catch (System.Exception ex) { return "도곽을 만들지 못했습니다 — " + ex.Message; }

        return $"배치 '{layName}' · A1 {SheetW:F0}×{SheetH:F0} · 축척 1:{scale:F0} · {veNote} · {bandNote}"
             + (overflow ? " · ⚠한 장을 넘침(장 넘김은 관로 기능에서)" : "");
    }

    /// <summary>★[JACK 0810] <b>축척과 수직과장을 함께 푼다.</b> 따로 정하면 서로를 무너뜨린다 —
    /// 축척을 먼저 정하면 과장이 갈 곳이 없고, 과장을 먼저 정하면 축척이 그걸 되돌린다.
    ///
    /// <para>JACK 지침 두 개가 답을 정해 준다:
    /// <b>"노선이 짧으면 짧은 대로 높이는 맞게 들어가야지"</b> ·
    /// <b>"폭은 억지로 맞추지 말고 짧은 상태로 들어가야 되는 게 맞다"</b>.
    /// 즉 <b>높이는 꽉 채우고 폭은 남으면 남는 대로</b> 둔다 — 축척을 정하는 것은 폭이 아니라 높이다.</para>
    ///
    /// <para>푸는 법: 밴드 높이는 스타일이 정해 놓아 <b>과장과 무관하게 고정</b>이다. 그래서
    /// 과장 1배로 한 번 재서 밴드 높이를 알아낸 뒤, 표준 축척을 작은 것부터 훑으며
    /// ①폭이 종이에 들어가고 ②밴드가 자리를 남기고 ③남은 자리를 채우는 과장이 있는 첫 축척을 고른다.
    /// 작은 축척부터 보므로 <b>가장 크게 그려지는 조합</b>이 뽑힌다.</para></summary>
    private static string FitSheet(Database db, ObjectId pvId, System.Text.StringBuilder log,
                                   out double scale, out bool overflow)
    {
        // ★★[JACK 0810 확정] 자동으로 고르지 않는다 — **V=1:200, H=1:1000 고정**으로 먼저 간다.
        //   "가로 세로 축척에 대해서 정의를 다시 하자. 일단은 V=1:200, H=1:1000을 기준으로 먼저 만들어 보자."
        //   축척을 노선마다 자동으로 고르면 도면마다 축척이 달라져 현장에서 비교가 안 되고,
        //   회사 스타일(밴드 높이·글자 크기)도 특정 축척을 전제로 만들어져 있다.
        //   기준을 먼저 못 박고, 안 맞는 것은 그 기준 위에서 고친다.
        scale = HScale;
        overflow = false;
        try
        {
            var cdoc = CivilApp.CivilApplication.ActiveDocument;
            var cands = ProfileStyleTemplate.Collect(db, cdoc, x => x.Cls == ProfileStyleTemplate.ClsProfileView)
                        .Select(s => (S: s, V: ParseExaggeration(s.Name)))
                        .Where(x => x.V > 0).OrderBy(x => x.V).ToList();
            // ★★[JACK 0810] **토공과 관로는 축척 규칙이 다르다.** JACK 지적:
            //   "보통 토공은 수직과장 잘 안 하지 않아? V랑 H가 비슷하게 가는 걸로 아는데, 관로는 수직과장이고."
            //   맞다. 토공 종단도는 **사면 구배를 눈으로 판단**해야 해서 과장하면 못 쓴다
            //   (5배 과장하면 1:1.5 사면이 절벽으로 보인다). JACK이 올린 회사 참고 도면도
            //   'S = 1 : 100' 단일 축척 한 줄이다 — V/H를 따로 적지 않는다 = 과장 없음.
            //   반대로 관로는 구배가 0.1~2%라 과장 없이는 일직선으로 보여 아무것도 안 보인다.
            //   → 세트 선택(토공/관로)에 축척 규칙을 묶는다. 사용자가 따로 고를 것이 늘지 않는다.
            bool pipe = GradingSettings.BandSet == "관로";
            if (!pipe)
            {
                // ── 토공: 수직과장 없음 + **단일 축척**, 긴 쪽이 꽉 차게(JACK: "세로든 가로든 긴 쪽이
                //   꽉 차는 스케일로 가는 게 어때"). 축척이 하나뿐이니 긴 쪽을 채우면 나머지는 저절로 정해진다.
                var flat = cands.OrderBy(c => System.Math.Abs(c.V - 1.0)).FirstOrDefault();
                if (!flat.S.Id.IsNull) SetStyle(db, pvId, flat.S.Id);
                var e0 = Measure(db, pvId);
                double wM0 = e0.MaxPoint.X - e0.MinPoint.X, hM0 = e0.MaxPoint.Y - e0.MinPoint.Y;

                // ★★[JACK 0810 계측] 밴드는 **종이 크기로 정의**되어 있다(BandHeight=0.003 = 3mm).
                //   그 값에 도면 축척이 곱해져 모형 크기가 된다. 그래서 밴드가 종이에서 차지하는
                //   높이는 **축척과 무관하게 일정**하다 — 이걸 먼저 빼고 남은 자리를 그래프가 쓴다.
                //   (종전엔 이걸 몰라 도곽이 그래프만 덮고 밴드가 통째로 밖으로 나갔다.)
                double bandPaperM = BandPaperHeight(db, pvId, log);      // 종이 기준 m
                double bandMm = bandPaperM * 1000.0;
                double availMm = ViewH - bandMm;
                // ★[JACK 0810] "1/3이라지만 너무 딱 맞으면 그러니까, 약간의 버퍼는 줘서 도면이 좀
                //   균형감 있게 해야지." — 자리를 100% 채우면 그래프가 테두리에 붙어 답답하다.
                //
                // ★★[v23.5 수정] <b>버퍼는 '곱하는 값'이 아니라 '올림이 남기는 것'이다.</b>
                //   종전엔 필요 축척을 구할 때 자리를 미리 92%로 깎았다. 그런데 그 뒤 표준 축척으로
                //   **올림**이 한 번 더 들어간다 — 실측에서 143 → (버퍼)155 → (올림)200이 됐고,
                //   버퍼가 없었으면 150에 들어갔을 그림이 한 단계 작아져 자리의 72%만 썼다.
                //   버퍼 8%를 얻으려다 28%를 잃은 셈이다.
                //
                //   <b>'축척을 한 단계 더 올려 버퍼를 만든다'도 같은 병이다.</b> 축척 사다리의 간격은
                //   20%(100→120)에서 60%(50→80)까지라, 8% 여백을 사려고 20~60%를 내주게 된다.
                //   → **필요한 만큼만 올린다.** 여백은 올림이 남긴 몫으로 충분하고,
                //     그것이 목표(8%)에 못 미치면 <b>줄이지 말고 로그로 알린다</b> — 그림 크기는 JACK 지침
                //     ("긴 쪽이 꽉 차는 스케일로")이 우선이고, 여백은 그다음이다.
                log.AppendLine($"토공 기준 — 수직과장 없음 · 단일 축척. 그래프 모형 {wM0:F1}m × {hM0:F1}m");
                log.AppendLine($"밴드는 종이 {bandMm:F1}mm 고정 → 그래프 자리 {availMm:F1}mm");

                // ★[v23.5] 밴드가 뷰 자리를 통째로 먹은 경우를 **따로 잡는다.** 종전엔 센티넬 1e9가
                //   그대로 흘러 로그에 `높이 1:1000000000`이 찍히고 정작 진짜 원인이 안 적혔다.
                bool noRoom = availMm <= 1.0;
                double needW = wM0 * 1000.0 / InnerW;
                double needH0 = noRoom ? double.PositiveInfinity : hM0 * 1000.0 / availMm;
                double want = System.Math.Max(needW, needH0);
                double s0 = Scales.FirstOrDefault(s => s >= want);
                if (s0 <= 0) { s0 = Scales[Scales.Length - 1]; overflow = true; }
                scale = s0;
                if (noRoom)
                    log.AppendLine($"⚠밴드가 뷰 자리를 통째로 먹었다 — 밴드 {bandMm:F1}mm ≥ 자리 {ViewH:F1}mm."
                                 + $" 그래프가 들어갈 높이가 없다(밴드 칸수·BandHeight를 먼저 확인할 것). S=1:{s0:F0}로 둔다.");
                else
                {
                    double used = want / s0;                   // 자리를 얼마나 채우는가(1.0 = 꽉 참)
                    string bind = needW >= needH0 ? "폭" : "높이";
                    log.AppendLine($"필요 폭 1:{needW:F0} · 높이 1:{needH0:F0} → **{bind}**이 긴 쪽 → S=1:{s0:F0}"
                                 + $" · {bind} 기준 자리의 {used * 100:F0}% 사용(여백 {(1 - used) * 100:F0}%)"
                                 + (overflow
                                    ? " ⚠가장 작은 축척으로도 안 들어간다"
                                    : used > Fill
                                      ? $" ⚠여백 목표 {(1 - Fill) * 100:F0}%에 못 미친다 — 축척을 한 단계 올리면 그림이 20~60% 작아지므로 그대로 둔다"
                                      : ""));
                }
                log.AppendLine($"종이에서 {wM0 * 1000.0 / s0:F0}mm × (그래프 {hM0 * 1000.0 / s0:F0} + 밴드 {bandMm:F1})mm"
                             + $" = {wM0 * 1000.0 / s0:F0}×{hM0 * 1000.0 / s0 + bandMm:F0}mm (자리 {InnerW:F0}×{ViewH:F0}mm)"
                             + (overflow ? " ⚠넘침" : ""));
                // ★ 도면 축척을 시트 축척에 맞춘다 — 이게 안 맞으면 밴드·글자가 통째로 어긋난다(10배 커 보였다).
                SetDrawingScale(db, s0, log);
                LastBandModelH = bandPaperM * s0;   // 도곽이 밴드까지 덮도록 넘겨준다
                if (flat.S.Id.IsNull) return $"S=1:{s0:F0} (수직과장 스타일 없음 — 도면 기본값)";
                if (System.Math.Abs(flat.V - 1.0) > 0.01)
                    log.AppendLine($"⚠회사 표준에 '수직과장 없음'이 없어 가장 가까운 {flat.V:0.#}배를 썼다.");
                return $"S=1:{s0:F0} · 수직과장 {flat.V:0.#}배 ('{flat.S.Name}')";
            }

            double wantVe = HScale / VScale;                       // 관로: 1000/200 = 5배
            if (cands.Count > 0)
            {
                var pick = cands.OrderBy(c => System.Math.Abs(c.V - wantVe)).First();
                SetStyle(db, pvId, pick.S.Id);
                var e = Measure(db, pvId);
                double w = (e.MaxPoint.X - e.MinPoint.X) * 1000.0 / HScale;
                double h = (e.MaxPoint.Y - e.MinPoint.Y) * 1000.0 / HScale;
                overflow = w > InnerW || h > ViewH;
                log.AppendLine($"기준 축척 V=1:{VScale:F0} H=1:{HScale:F0}(과장 {wantVe:0.#}배) → 스타일 '{pick.S.Name}'(과장 {pick.V:0.#}배)");
                log.AppendLine($"종이에서 {w:F0}mm × {h:F0}mm (자리 {InnerW:F0}×{ViewH:F0}mm)" + (overflow ? " ⚠넘침" : " 들어감"));
                if (System.Math.Abs(pick.V - wantVe) > 0.01)
                    log.AppendLine($"⚠회사 표준에 과장 {wantVe:0.#}배 스타일이 없어 가장 가까운 {pick.V:0.#}배를 썼다 — 실제 V=1:{HScale / pick.V:F0}");
                return $"V=1:{HScale / pick.V:F0} H=1:{HScale:F0} (과장 {pick.V:0.#}배 '{pick.S.Name}')";
            }
            log.AppendLine("⚠회사 표준 종단 뷰 스타일이 없어 과장을 지정하지 못했다 — 축척만 1:1000으로 둔다.");
            return $"H=1:{HScale:F0} (수직과장 스타일 없음)";
        }
        catch (System.Exception ex) { return "축척 지정 실패: " + ex.Message; }
    }

    /// <summary>(구) 축척·과장 자동 맞춤 — JACK이 고정 기준(V=1:200·H=1:1000)으로 정해 지금은 쓰지 않는다.
    /// 노선 길이에 맞춰 자동으로 고르는 길이 필요해지면 여기서 되살린다.</summary>
    private static string FitSheetAuto_Unused(Database db, ObjectId pvId, System.Text.StringBuilder log,
                                              out double scale, out bool overflow)
    {
        scale = Scales[Scales.Length - 1];
        overflow = true;
        try
        {
            var cdoc = CivilApp.CivilApplication.ActiveDocument;
            var cands = ProfileStyleTemplate.Collect(db, cdoc, x => x.Cls == ProfileStyleTemplate.ClsProfileView)
                        .Select(s => (S: s, V: ParseExaggeration(s.Name)))
                        .Where(x => x.V > 0).OrderBy(x => x.V).ToList();

            // 표고 범위 — 종단도에 걸린 종단들의 최고·최저
            double lo = double.MaxValue, hi = double.MinValue;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var pv0 = (CivilDb.ProfileView)tr.GetObject(pvId, OpenMode.ForRead);
                if (tr.GetObject(pv0.AlignmentId, OpenMode.ForRead) is CivilDb.Alignment al)
                    foreach (ObjectId pid in al.GetProfileIds())
                    {
                        if (tr.GetObject(pid, OpenMode.ForRead) is not CivilDb.Profile pr) continue;
                        try { lo = System.Math.Min(lo, pr.ElevationMin); hi = System.Math.Max(hi, pr.ElevationMax); } catch { }
                    }
                tr.Commit();
            }
            double range = (lo <= hi) ? System.Math.Max(0.1, hi - lo) : 0;

            // ── 밴드 높이를 알아내려면 과장을 아는 상태에서 한 번 재야 한다. 가장 작은 과장으로 맞춰 잰다.
            double veBase = cands.Count > 0 ? cands[0].V : 1.0;
            if (cands.Count > 0) SetStyle(db, pvId, cands[0].S.Id);
            var ext0 = Measure(db, pvId);
            double wM = ext0.MaxPoint.X - ext0.MinPoint.X;
            double hM = ext0.MaxPoint.Y - ext0.MinPoint.Y;
            double hBand = System.Math.Max(0.0, hM - range * veBase);
            log.AppendLine($"기준 측정(과장 {veBase:0.#}배): 폭 {wM:F1}m · 전체높이 {hM:F1}m · 표고범위 {range:F2}m · 밴드 {hBand:F2}m");

            // ── 축척을 작은 것부터 — 가장 크게 그려지는 조합이 먼저 걸린다.
            string pickName = cands.Count > 0 ? cands[0].S.Name : "(스타일 없음)";
            double pickVe = veBase;
            foreach (double s in Scales)
            {
                if (wM * 1000.0 / s > InnerW) continue;                 // ① 폭이 종이를 넘음
                double bandPaper = hBand * 1000.0 / s;
                double avail = ViewH - bandPaper;
                if (avail <= 1.0) continue;                             // ② 밴드만으로도 자리가 없음
                if (range <= 0 || cands.Count == 0)                     // 표고를 못 재면 축척만 정한다
                { scale = s; overflow = false; break; }
                var fit = cands.Where(c => range * c.V * 1000.0 / s <= avail)
                               .OrderByDescending(c => c.V).ToList();
                if (fit.Count == 0) continue;                           // ③ 남은 자리를 채울 과장이 없음
                scale = s; pickVe = fit[0].V; pickName = fit[0].S.Name; overflow = false;
                double graphPaper = range * pickVe * 1000.0 / s;
                log.AppendLine($"→ 축척 1:{s:F0} · 과장 {pickVe:0.#}배 · 종이에서 그래프 {graphPaper:F0}mm + 밴드 {bandPaper:F0}mm = {graphPaper + bandPaper:F0}mm (자리 {ViewH:F0}mm)");
                log.AppendLine($"   폭 {wM * 1000.0 / s:F0}mm / {InnerW:F0}mm (남는 폭은 그대로 둔다 — JACK 지침)");
                break;
            }
            if (cands.Count > 0 && pickName != cands[0].S.Name)
                SetStyle(db, pvId, cands.First(c => c.S.Name == pickName).S.Id);
            if (overflow) log.AppendLine("⚠어떤 표준 축척으로도 한 장에 들어가지 않는다 — 가장 작은 축척으로 둔다.");
            return $"수직과장 {pickVe:0.#}배 ('{pickName}')";
        }
        catch (System.Exception ex) { return "축척·과장 자동 실패: " + ex.Message; }
    }

    /// <summary>★[JACK 0810] <b>밴드를 표로 만든다</b> — "밴드 박스가 좀 이상해. 표처럼 딱 맞게 되어야 해."
    /// <para>참고 도면(C-005 배수지 토공 종단면도)의 정보표시 테이블은 칸끼리 <b>간격 없이 붙어</b> 있고
    /// 하나의 사각 테두리로 묶여 있다. 지금 값은 칸마다 제각각이었다 —
    /// 높이 1mm~25mm, 간격은 양수·음수가 섞여 있어(0.0055 / -0.0055) 칸이 떨어지거나 겹쳤다.</para>
    /// <para>규칙: <b>밴드 영역은 언제나 내부 높이의 1/3</b>(JACK: "무조건 1/3을 잡아먹을 테니까")이고
    /// 그 안을 <b>칸 수만큼 균등 분할</b>한다(JACK: "6등분"). 간격은 전부 0.
    /// 크기를 <b>종이 기준</b>으로 넣으므로 축척이 바뀌어도 종이 위 모양은 그대로다 —
    /// Civil 3D가 종이 크기에 도면 축척을 곱해 그리기 때문이다.</para></summary>
    private static string NormalizeBands(Database db, ObjectId pvId, System.Text.StringBuilder log)
    {
        int n = 0, hOk = 0, gOk = 0, tOk = 0, tTry = 0, vOk = 0, vTry = 0, kOk = 0, eOk = 0, dOk = 0;
        double eachM = 0;
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            var pv = (CivilDb.ProfileView)tr.GetObject(pvId, OpenMode.ForWrite);
            using (var probe = pv.Bands.GetBottomBandItems()) n = probe.Count;
            if (n == 0) { tr.Commit(); return "밴드 없음"; }

            eachM = BandH / n / 1000.0;          // 종이 m — 1/3을 칸 수로 균등 분할
            using (var items = pv.Bands.GetBottomBandItems())
            {
                for (int i = 0; i < items.Count; i++)
                {
                    // ★[v23.5] 실패해도 흔적이 남게 한다. 종전엔 맨 `catch { }`라 밴드 스타일을 못 열면
                    //   높이·제목·값글씨 세 작업이 통째로 건너뛰어지고 로그가 한 줄도 안 남았다 —
                    //   요약에는 `높이 0 · 값글씨 0`만 찍혀 **v23.4에서 헤맨 화면과 똑같아진다.**
                    // ★[JACK 0810] <b>"밴드 맨위 성토하고 종단하고 사이에 10의 거리 주고"</b> —
                    //   첫 칸만 그래프에서 띄우고 칸끼리는 붙인다. 표는 붙어야 표로 읽히지만,
                    //   그래프와 표가 맞붙으면 어디까지가 그림이고 어디부터가 표인지 구분이 안 된다.
                    double gap = i == 0 ? TopGapMm / 1000.0 : 0.0;
                    try { items[i].Gap = gap; gOk++; }
                    catch (System.Exception ex) { log.AppendLine($"   [{i}칸] 간격 실패 — {Brief(ex)}"); }

                    // ★[JACK 0810] <b>"여전히 글씨가 이상하게 정렬돼"</b> — 값이 두 단으로 어긋나 있었다.
                    //   범인은 Civil의 <b>엇갈림(Stagger)</b>이다. 라벨이 겹칠 것 같으면 자동으로
                    //   위아래 두 줄로 벌려 놓는다. 그래서 같은 칸 안에서 어떤 값은 위, 어떤 값은 아래에 앉는다.
                    //   참고 도면의 정보표시 테이블은 <b>한 줄로 나란히</b> 선다 — 표는 줄이 맞아야 표다.
                    //   → 엇갈림을 끄고, 겹침은 <b>솎아내기(Weeding)</b>로 푼다. 그쪽이 종이 기준이라
                    //     축척이 바뀌어도 규칙이 유지된다.
                    try
                    {
                        items[i].StaggerLabel = CivilDb.Styles.StaggerLabelType.None;
                        items[i].StaggerLineHeight = 0.0;
                    }
                    catch (System.Exception ex) { log.AppendLine($"   [{i}칸] 엇갈림 끄기 실패 — {Brief(ex)}"); }

                    vTry++;
                    vOk += EnableGeometryPoints(items[i], i, log);
                    try
                    {
                        var st = tr.GetObject(items[i].BandStyleId, OpenMode.ForWrite);
                        if (Set(st, "BandHeight", eachM)) hOk++;
                        else log.AppendLine($"   [{i}칸] 밴드높이 못 씀 — {st.GetType().Name}에 BandHeight가 없거나 읽기전용");

                        // ★[JACK 0810] 글씨 크기를 **칸 높이에서 역산**한다 — "밴드 높이에서 위아래
                        //   보조눈금 길이를 제외한 길이를 구하고, 000.00 표현식 기준으로 가장 꽉 찬 크기로."
                        //   제목은 세로로 쓰고 4글자(누가거리·구간거리)가 가장 기니 그걸 기준으로 삼는다.
                        //   ※ JACK 0810: "회사 스타일이란 건 없어. 그냥 네가 만들면 돼" — 값을 직접 정한다.
                        double eachMm = BandH / n;
                        double availMm = eachMm * (1.0 - TickShare);          // 위아래 눈금 자리를 뺀 길이
                        double valMm = availMm / (6.0 * DigitW) * TextFill;   // "000.00" 6자
                        // ★[JACK 0810] "제목 글씨 너무 큼" — 4글자가 칸의 90%를 먹던 것을 70%로.
                        double ttlMm = eachMm * TitleFill / TitleChars;       // 세로 4글자(폭≈높이)
                        // ★[JACK 0810] "전체적으로 15%만 작게" — 값·제목에 같은 비율로.
                        valMm *= BandTextScale; ttlMm *= BandTextScale;
                        // 제목 글씨는 밴드 스타일 직속(맨 double). 실패하면 그것도 남긴다 —
                        // 밴드 종류가 늘면 조용히 깨질 자리다.
                        if (!Set(st, "TextHeight", ttlMm / 1000.0))
                            log.AppendLine($"   [{i}칸] 제목 글씨높이 못 씀({st.GetType().Name})");
                        if (!Set(st, "TextBoxWidth", ttlMm * 1.8 / 1000.0))   // 글씨가 상자 밖으로 안 나가게
                            log.AppendLine($"   [{i}칸] 제목 상자폭 못 씀({st.GetType().Name})");
                        // 굴곡부 라벨에 **글자를 먼저 만들고** 나서 크기를 맞춘다(순서가 반대면 새 글자가 안 잡힌다).
                        if (EnsureVgpLabel(tr, st, i, log)) eOk++;
                        dOk += EnableVgpDisplay(st, i, log);   // ★ 표시 스위치 — 이것이 마지막 관문이었다
                        tOk += SetLabelHeight(tr, st, valMm / 1000.0, ttlMm / 1000.0, i, log, ref tTry);
                        kOk += SetTicks(st, eachMm, i, log);   // ★[JACK 0810] "보조눈금 좀 키워줘"
                    }
                    catch (System.Exception ex)
                    { log.AppendLine($"   [{i}칸] 밴드 스타일 손보기 실패 — {Brief(ex)}"); }
                }
                pv.Bands.SetBottomBandItems(items);
            }
            tr.Commit();
        }
        catch (System.Exception ex) { return "밴드 정리 실패 — " + Brief(ex); }
        // ★[v23.5] 개수만 세면 원인 자리를 못 좁힌다 — **분모**를 같이 남긴다.
        //   `값글씨 42`가 42/42인지 42/126인지 알 수 없었다.
        string s = $"밴드 {n}칸 균등 — 각 {eachM * 1000.0:F1}mm(합 {BandH:F1}mm) · 간격 0 " +
                   $"(높이 {hOk}/{n} · 간격 {gOk}/{n} · 값글씨 {tOk}/{tTry} · 굴곡부 {vOk}/{vTry * 2} · 눈금 {kOk} · 굴곡부글자 {eOk} · 굴곡부표시 {dOk})";
        log.AppendLine(s);
        return s;
    }

    /// <summary>★[JACK 0810] <b>정지면 굴곡부에 측점이 자동으로 찍히게</b> 한다 —
    /// "처음 종단도 그릴 때 정지 지표면에 한해서 굴곡부는 자동으로 측점이 추가되게 해 줘."
    /// <para>수집기(<see cref="StationMarks"/>)가 굴곡부를 잡고는 있었지만 종단도에 <b>보이지</b> 않았다.
    /// 밴드 항목의 수직 기하점 표시를 켜면 계획 종단이 꺾이는 자리마다 눈금과 측점이 자동으로 찍힌다 —
    /// 단면검토선도, 사람 손도 필요 없다.</para>
    ///
    /// <para>★[v23.5] <b>종전 판은 이 자리를 목록으로 착각했다.</b> 반사로 <c>Selected</c>가 달린 원소를
    /// 훑으려 했는데 <c>GeometryPointSelector&lt;T&gt;</c>는 <b>IEnumerable이 아니다</b> — 그래서
    /// 순회가 한 번도 돌지 않고 <c>굴곡부 0</c>이 찍혔다. 어셈블리 메타데이터를 뜯어 확인한 실제 모양은:</para>
    /// <code>
    /// GeometryPointSelector&lt;T&gt; { GeometryPointLabelOption&lt;T&gt; this[T type]; SelectAll(); UnSelectAll(); }
    /// GeometryPointLabelOption&lt;T&gt; { bool Selected; T PointType; }
    /// </code>
    /// <para><b>인덱서로 종류를 집어</b> 켜고 다시 넣어 주면 된다. <c>SelectAll()</c>은 쓰지 않는다 —
    /// 곡선 시종점·최고최저점까지 전부 찍혀 도면이 지저분해진다. JACK이 요구한 것은 <b>꺾이는 자리</b>다.</para>
    /// 반환=켠 종류 수(0이면 이 방식이 안 통한 것이니 로그에 드러난다).</summary>
    private static int EnableGeometryPoints(CivilDb.Styles.ProfileViewBandSetItem item, int idx,
                                            System.Text.StringBuilder log)
    {
        // 굴곡부(GradeBreak)와 종단곡선 교점(PVI)만 — 지표면에서 딴 종단은 전부 GradeBreak이고,
        // 사람이 설계한 종단은 PVI로 꺾인다. 둘을 다 켜야 두 경우 모두 찍힌다.
        var want = new[] { Autodesk.Civil.ProfilePointType.GradeBreak, Autodesk.Civil.ProfilePointType.PVI };
        int on = 0;
        try
        {
            var sel = item.GetVerticalGeometryPointsOptions();
            if (sel == null) { log.AppendLine($"   [{idx}칸] 굴곡부: 수직 기하점 선택기가 없다"); return 0; }
            foreach (var t in want)
            {
                try { sel[t].Selected = true; }
                catch (System.Exception ex) { log.AppendLine($"   [{idx}칸] 굴곡부 {t} 대입 실패 — {Brief(ex)}"); }
            }
            item.SetVerticalGeometryPointsOptions(sel);

            // ★[v23.5] <b>넣었다고 세지 않는다 — 다시 읽어 확인한다.</b> 값글씨에는 이 원칙을 넣고
            //   여기엔 안 넣으면 `굴곡부 2`가 찍혀도 도면에 안 나올 때 대입이 안 먹은 건지
            //   표시가 꺼진 건지 구분이 안 된다. v23.4의 증상이 정확히 "보이지 않았다"였다.
            //   이 API는 되읽기가 네이티브 집합을 다시 읽어 오므로 **진짜 왕복 확인**이 된다.
            var back = item.GetVerticalGeometryPointsOptions();
            foreach (var t in want)
            {
                bool ok = false;
                try { ok = back[t].Selected; } catch { }
                if (ok) on++;
                else log.AppendLine($"   [{idx}칸] 굴곡부 {t}: 넣었는데 다시 읽으니 꺼져 있다");
            }
        }
        catch (System.Exception ex)
        {
            // 종단 데이터 밴드가 아니면 이 API 자체가 예외를 던진다(진단 로그에서 확인).
            // 실패한 판이야말로 기록이 필요하다 — 이름 없이 개수만 세면 다음 판에서 또 헤맨다.
            // ※ 종류를 읽는 것조차 던질 수 있다(진단 로그에 BandType 계열이 예외로 찍힌 사례가 있다).
            //   기록하려다 터지면 밴드 정리 전체가 중단된다 — 로그 쓰는 자리에서 죽는 건 최악이다.
            string kind; try { kind = item.BandType.ToString(); } catch { kind = "(종류 못 읽음)"; }
            log.AppendLine($"   [{idx}칸] 굴곡부: {kind} 밴드에는 적용 불가 — {Brief(ex)}");
            return 0;
        }
        return on;
    }

    /// <summary>★[JACK 0810] <b>"모든 글씨는 흰색(검정)으로 바꿔"</b> — 색 번호 7.
    /// 7번은 <b>배경 반전색</b>이라 화면(검정 바탕)에선 흰색, 출력(흰 종이)에선 검정으로 나온다.
    /// 도면 표준에서 글씨에 7번을 쓰는 이유가 그것이다 — 한 값으로 화면과 출력이 다 맞는다.</summary>
    private static Autodesk.AutoCAD.Colors.Color White
        => Autodesk.AutoCAD.Colors.Color.FromColorIndex(Autodesk.AutoCAD.Colors.ColorMethod.ByAci, 7);

    /// <summary>★[JACK 0810] <b>굴곡부가 세 판째 안 나온 마지막 관문.</b>
    ///
    /// <para>v23.5는 굴곡부를 <b>켰고</b>, v23.6은 찍을 <b>글자를 만들었고</b>,
    /// v23.9는 <b>체(Weeding 100m)를 걷어냈다</b>. 셋 다 맞았는데도 안 나왔다.
    /// 전수 계측을 넣고서야 드러난 것 — <b>밴드 스타일에 표시 스위치가 따로 있다.</b>
    /// <c>ProfileDataBandStyle.GetDisplayStylePlan(TicksAtVGP / LabelsAtVGP)</c>가 꺼져 있으면
    /// 앞의 준비가 전부 헛것이 된다. 그려질 것이 정해져도 <b>그리지 말라</b>고 되어 있었던 것이다.</para>
    ///
    /// <para><b>교훈은 §22.6과 같다</b> — 이번엔 계측을 먼저 넣어서 한 판에 찾았다.
    /// '무엇이 꺼져 있는지'를 전수로 찍는 한 줄이 세 판을 헤맨 것보다 쌌다.</para>
    ///
    /// <para>겸사겸사 <b>글자류 표시 색을 7번</b>으로 맞춘다(JACK: "모든 글씨는 흰색(검정)으로").</para>
    /// 반환=켠 표시 수.</summary>
    private static int EnableVgpDisplay(object bandStyle, int idx, System.Text.StringBuilder log)
    {
        if (bandStyle is not CivilDb.Styles.ProfileDataBandStyle pdb) return 0;
        int on = 0;
        var wasOff = new System.Text.StringBuilder();
        foreach (var v in System.Enum.GetValues(typeof(CivilDb.Styles.ProfileDataDisplayStyleType)))
        {
            var t = (CivilDb.Styles.ProfileDataDisplayStyleType)v;
            string nm = t.ToString();
            try
            {
                using var ds = pdb.GetDisplayStylePlan(t);
                if (!ds.Visible) wasOff.Append(' ').Append(nm);
                if (t == CivilDb.Styles.ProfileDataDisplayStyleType.TicksAtVGP ||
                    t == CivilDb.Styles.ProfileDataDisplayStyleType.LabelsAtVGP)
                {
                    ds.Visible = true;
                    if (ds.Visible) on++;                       // 넣었다고 세지 않는다 — 되읽어 확인
                    else log.AppendLine($"   [{idx}칸] {nm}: 켰는데 다시 읽으니 꺼져 있다");
                }
                // ★[JACK 0810] "눈금 포함 모든 것" — 글자만 바꿨더니 눈금이 빨간색으로 남았다.
                //   밴드 스타일도 통째로 7번(테두리·눈금·글자 전부).
                ds.Color = White;
            }
            catch (System.Exception ex) { log.AppendLine($"   [{idx}칸] 표시 {nm} 실패 — {Brief(ex)}"); }
        }
        if (wasOff.Length > 0) log.AppendLine($"   [{idx}칸] 밴드 표시 꺼져 있던 것:{wasOff}");
        return on;
    }

    /// <summary>예외 한 줄 요약. <b>종류와 ErrorStatus까지 남긴다</b> —
    /// AutoCAD 예외는 메시지보다 <c>ErrorStatus</c>(eWasErased·eNotOpenForWrite 등)가 진짜 단서다.
    /// 안쪽 예외가 원인인 경우가 많지만 <b>바깥쪽도 버리지 않는다</b>(어느 층에서 났는지가 자리다).</summary>
    private static string Brief(System.Exception ex)
    {
        string s = ex.GetType().Name;
        if (ex is Autodesk.AutoCAD.Runtime.Exception ae) s += $"({ae.ErrorStatus})";
        s += ": " + ex.Message;
        if (ex.InnerException != null) s += " ← " + ex.InnerException.Message;
        return s.Replace("\r", " ").Replace("\n", " ");
    }

    /// <summary>위아래 보조눈금이 칸 높이에서 차지하는 몫(위+아래 합계) · 숫자 한 글자의 폭(높이 대비) ·
    /// 자리를 얼마나 채울지. JACK 0810 "가장 꽉 찬 크기로" — 다만 테두리에 닿지 않게 조금 남긴다.</summary>
    private const double TickShare = 0.15, DigitW = 0.6, TextFill = 0.9;

    /// <summary>보조눈금은 주눈금의 몇 할인지 — 도면 관례상 <b>주가 길고 보조가 짧다</b>.
    /// 같은 길이면 어느 쪽이 주인지 눈으로 구분이 안 된다.</summary>
    private const double MinorTickRatio = 0.6;

    /// <summary>★[JACK 0810] <b>"밴드 제목부분 글씨크기 조정(너무큼)"</b> —
    /// 제목 4글자가 칸 높이에서 차지할 비율. 종전엔 <c>eachMm/4 × 0.9</c>라 4글자가 칸의 <b>90%</b>를
    /// 채워 위아래가 꽉 막혔다. 0.7이면 4글자가 칸의 70%를 쓰고 위아래로 15%씩 숨통이 트인다.</summary>
    private const double TitleFill = 0.7;

    /// <summary>제목에서 기준으로 삼는 글자 수 — 회사 밴드 이름 중 가장 긴 것이 4글자(누가거리·구간거리).</summary>
    private const int TitleChars = 4;

    /// <summary>★[JACK 0810] <b>"밴드 글씨크기를 전체적으로 15%만 작게"</b> —
    /// 값·제목에 <b>함께</b> 곱한다. 한쪽만 줄이면 둘의 비례가 깨져 표가 어색해진다.
    /// 계산식(칸 높이에서 역산)은 그대로 두고 마지막에 한 번만 곱하므로,
    /// 칸 수나 밴드 높이가 바뀌어도 '15% 작게'라는 뜻이 유지된다.</summary>
    private const double BandTextScale = 0.85;

    /// <summary>★[JACK 0810] <b>그래프와 첫 밴드(성토) 사이 틈</b>(종이 mm) — "10의 거리 주고".
    /// 칸끼리는 붙여 표로 읽히게 하되, 그래프와 표 사이만 띄워 경계가 보이게 한다.
    /// 이 틈은 밴드 총높이에 포함되어 축척 계산에 자동으로 반영된다(<see cref="BandPaperHeight"/>가 간격까지 더한다).</summary>
    private const double TopGapMm = 10.0;

    /// <summary>★[JACK 0810] <b>"계획지반고의 변곡점 측점이 누락됨"</b> —
    /// v23.5에서 굴곡부 <b>눈금</b>은 켰는데(굴곡부 12/12) 도면엔 값이 안 나왔다.
    /// 로그가 이유를 그대로 말해 준다: <c>VGPLabelStyleId … 글자 구성요소가 0개</c>.
    /// <b>찍을 눈금은 생겼는데 찍을 글자가 없었다.</b> 게다가 그 라벨 스타일은 이름이 GUID인
    /// <b>빈 껍데기</b>다 — 회사 템플릿이 만들어 두기만 하고 내용을 안 채운 자리.
    ///
    /// <para><b>표현식을 짐작해서 쓰지 않는다.</b> 밴드 라벨의 내용은
    /// <c>&lt;[Station Value(…)]&gt;</c> 같은 필드 문자열인데, 이걸 손으로 지어내는 것이
    /// 정확히 §22.4에서 데인 방식이다. 대신 <b>같은 밴드의 주눈금 라벨에서 읽어다 옮긴다</b> —
    /// 이미 도면에서 제대로 그려지고 있는 문자열이므로 맞는지 아닌지를 따질 필요가 없다.
    /// 각도도 같이 옮긴다(값 글씨는 템플릿에서 이미 세로로 서 있다).</para>
    ///
    /// <para><b>밴드마다 제 것을 옮긴다</b>는 점이 중요하다. 계획고 밴드는 굴곡부에서 계획고를,
    /// 측점 밴드는 측점을 찍는다 — 그래야 변곡점 자리에 <b>세로 한 줄이 통째로</b> 선다.
    /// 실제 종단면도의 정보표시 테이블이 그렇게 읽힌다.</para>
    /// 반환=글자를 새로 만든 경우 true.</summary>
    private static bool EnsureVgpLabel(Transaction tr, object bandStyle, int idx, System.Text.StringBuilder log)
    {
        try
        {
            var pV = bandStyle.GetType().GetProperty("VGPLabelStyleId");
            if (pV?.GetValue(bandStyle) is not ObjectId vid || vid.IsNull) return false;
            if (tr.GetObject(vid, OpenMode.ForWrite) is not CivilDb.Styles.LabelStyle vls) return false;
            using (var have = vls.GetComponents(CivilDb.Styles.LabelStyleComponentType.Text))
                if (have.Count > 0) return false;                  // 이미 글자가 있으면 건드리지 않는다

            // ── 같은 밴드의 주눈금 라벨에서 '무엇을 찍는지'와 '어느 각도로 찍는지'를 읽어 온다.
            string expr = null; double ang = System.Math.PI / 2.0; bool got = false;
            var pM = bandStyle.GetType().GetProperty("MajorIncrementLabelStyleId");
            if (pM?.GetValue(bandStyle) is ObjectId mid && !mid.IsNull &&
                tr.GetObject(mid, OpenMode.ForRead) is CivilDb.Styles.LabelStyle mls)
            {
                using var mc = mls.GetComponents(CivilDb.Styles.LabelStyleComponentType.Text);
                foreach (ObjectId cid in mc)
                {
                    if (tr.GetObject(cid, OpenMode.ForRead) is not CivilDb.Styles.LabelStyleTextComponent mtc) continue;
                    using var mt = mtc.Text;
                    using (var c = mt.Contents) expr = c.Value;
                    using (var a = mt.Angle) ang = a.Value;
                    got = true; break;
                }
            }
            if (!got || string.IsNullOrEmpty(expr))
            { log.AppendLine($"   [{idx}칸] 굴곡부 글자: 주눈금 라벨에서 표현식을 못 읽었다 — 그대로 둔다"); return false; }

            var nid = vls.AddComponent("DH_굴곡부 값", CivilDb.Styles.LabelStyleComponentType.Text);
            if (tr.GetObject(nid, OpenMode.ForWrite) is not CivilDb.Styles.LabelStyleTextComponent ntc)
            { log.AppendLine($"   [{idx}칸] 굴곡부 글자: 구성요소를 만들었으나 열지 못했다"); return false; }
            using (var nt = ntc.Text)
            {
                using (var c = nt.Contents) c.Value = expr;
                using (var a = nt.Angle) a.Value = ang;
            }
            // ★[v23.7] <b>글자는 만들어졌는데 화면엔 안 나왔다</b>(v23.6 실측). 새 구성요소가
            //   안 보이게 태어났을 가능성이 첫 번째다 — 명시적으로 켜고, 켠 뒤의 상태를 남긴다.
            //   그래도 안 나오면 이 로그가 '보임은 켜져 있는데 안 그려진다'로 자리를 좁혀 준다.
            using (var gen = ntc.General)
            {
                try { using (var vis = gen.Visible) { vis.Value = true; } } catch { }
                string an = "?", ap = "?";
                try { using (var x = gen.AnchorComponent) an = x.Value; } catch { }
                try { using (var x = gen.AnchorPoint) ap = x.Value.ToString(); } catch { }
                log.AppendLine($"   [{idx}칸] 굴곡부 글자 상태: 보임 켬 · 앵커부품 '{an}' · 앵커점 {ap}");
            }
            // 무엇을 옮겼는지 남긴다 — 다음 판에서 이 문자열이 맞았는지 따질 근거가 된다.
            log.AppendLine($"   [{idx}칸] 굴곡부 글자 생성: 주눈금에서 복사 · 각도 {ang * 180.0 / System.Math.PI:F0}° · 내용 \"{expr}\"");
            return true;
        }
        catch (System.Exception ex) { log.AppendLine($"   [{idx}칸] 굴곡부 글자 실패 — {Brief(ex)}"); return false; }
    }

    /// <summary>★[JACK 0810] <b>"왼쪽 바를 스케일(체크)로 만들것"</b> —
    /// 스샷의 왼쪽 축은 눈금 없는 <b>맨 직선</b>이라 표고를 눈으로 짚을 수가 없다.
    ///
    /// <para>축 눈금은 밴드가 아니라 <b>종단 뷰 스타일</b>에 있다:
    /// <c>ProfileViewStyle.LeftAxis.MajorTickStyle/MinorTickStyle</c>(<see cref="CivilDb.Styles.AxisTickStyle"/>),
    /// 각각 <c>Size</c>(눈금 길이, 종이 m)·<c>Interval</c>(간격)을 갖는다.
    /// 직선으로 보이는 건 <b>Size가 0에 가깝기 때문</b>이다.</para>
    ///
    /// <para><b>Interval은 건드리지 않는다.</b> 그것을 바꾸면 표고 라벨 밀도가 통째로 달라지는데
    /// JACK이 요구한 것은 '눈금이 보이게'다. 대신 <b>현재 값을 로그에 남겨</b> 다음 판에서
    /// 간격까지 손댈지 판단할 수 있게 한다 — 짐작으로 바꾸고 나중에 되돌리는 것보다 싸다.</para></summary>
    private static void SetAxisTicks(Database db, ObjectId pvId, double scale, System.Text.StringBuilder log)
    {
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            var pv = (CivilDb.ProfileView)tr.GetObject(pvId, OpenMode.ForRead);
            if (tr.GetObject(pv.StyleId, OpenMode.ForWrite) is not CivilDb.Styles.ProfileViewStyle vs)
            { log.AppendLine("   축 눈금: 종단 뷰 스타일을 열지 못했다"); tr.Commit(); return; }

            // 좌우 축 모두 — 오른쪽도 같이 서야 표가 사다리처럼 읽힌다.
            foreach (var (nm, ax) in new[] { ("왼쪽", vs.LeftAxis), ("오른쪽", vs.RightAxis) })
                using (ax)
                {
                    ax.ShowTickAndLabel = true;
                    using (var mj = ax.MajorTickStyle)
                    using (var mn = ax.MinorTickStyle)
                    {
                        log.AppendLine($"   축 눈금({nm}) 전: 주 {mj.Size * 1000:F2}mm/간격 {mj.Interval:0.###}" +
                                       $" · 보조 {mn.Size * 1000:F2}mm/간격 {mn.Interval:0.###} · 표시 {ax.ShowTickAndLabel}");
                        // ★★[v23.7] <b>절대 줄이지 않는다.</b> v23.6이 왼쪽 축의 주눈금을
                        //   <b>14mm → 2.5mm로 깎았다</b> — 키워 달라는 자리를 반대로 줄였다.
                        //   회사 템플릿이 넣어 둔 값(14mm/5m + 1mm/1m)은 이미 사다리 모양을 의도한 것이다.
                        //   내 기준값은 <b>바닥</b>으로만 쓴다.
                        mj.Size = System.Math.Max(mj.Size, AxisMajorTickMm / 1000.0);
                        mn.Size = System.Math.Max(mn.Size, AxisMajorTickMm * MinorTickRatio / 1000.0);

                        // ★[v23.14 · JACK 지적] <b>표고 눈금 간격도 축척을 따라야 한다.</b>
                        //   회사 템플릿은 5m 고정인데, 종이에서 몇 mm가 되는지는 축척이 정한다 —
                        //   1:1000이면 5mm라 숫자가 붙어 못 읽고, 1:50이면 100mm라 눈금이 몇 개 안 남는다.
                        //   <b>범위를 벗어날 때만</b> 관례값으로 바꾼다(멀쩡한 회사 값을 매번 덮지 않는다).
                        double onPaper = mj.Interval / scale * 1000.0;
                        if (mj.Interval > 1e-9 && (onPaper < AxisLabelMinMm || onPaper > AxisLabelMaxMm))
                        {
                            double target = AxisLabelWantMm / 1000.0 * scale;
                            double[] nice = { 0.25, 0.5, 1, 2, 2.5, 5, 10, 20, 25, 50, 100, 200 };
                            double pick = nice.OrderBy(v => System.Math.Abs(v - target)).First();
                            log.AppendLine($"   축 눈금({nm}) 간격: {mj.Interval:0.##}m는 종이 {onPaper:F1}mm라" +
                                           $" 범위({AxisLabelMinMm:F0}~{AxisLabelMaxMm:F0}mm) 밖 → {pick:0.##}m" +
                                           $"(종이 {pick / scale * 1000.0:F1}mm)로 바꿈");
                            mj.Interval = pick;
                            mn.Interval = pick / 5.0;
                        }
                        log.AppendLine($"   축 눈금({nm}) 후: 주 {mj.Size * 1000:F2}mm/간격 {mj.Interval:0.###}" +
                                       $" · 보조 {mn.Size * 1000:F2}mm/간격 {mn.Interval:0.###} (크기는 줄이지 않음)");
                    }
                }

            // ★★[v23.7 계측] <b>14mm였을 때도 안 보였다</b> — 그러니 크기 문제가 아니다.
            //   눈금이 아예 안 그려지는 쪽을 의심해야 한다. 뷰 스타일의 표시 항목을 전수로 찍어
            //   무엇이 꺼져 있는지 다음 판에서 이름으로 짚을 수 있게 한다.
            //   (짐작으로 켜 보는 것보다 한 판 늦지만, v23.6처럼 헛짚어 되돌리는 것보다 싸다.)
            try
            {
                var et = typeof(CivilDb.Styles.ProfileViewDisplayStyleType);
                var sb = new System.Text.StringBuilder();
                foreach (var v in System.Enum.GetValues(et))
                {
                    try
                    {
                        var t = (CivilDb.Styles.ProfileViewDisplayStyleType)v;
                        using var ds = vs.GetDisplayStylePlan(t);
                        if (!ds.Visible) sb.Append($" {v}=꺼짐");
                        // ★[JACK 0810] <b>"모든 선은 흰색(검정)으로. 눈금 포함 모든 것"</b>
                        //   글자만 고쳤더니 축선(파랑)·눈금(빨강)이 남았다. 뷰 스타일은 통째로 7번.
                        //   (종단선 하늘색은 <b>종단 스타일</b>이라 여기서 안 건드린다 — JACK 지침대로 유지.)
                        ds.Color = White;
                        // ★[JACK 0810] "세로줄은 종단에 생겨야 됨" — 그래프 세로 격자를 켠다.
                        //   로그 실측: GridVerticalMajor·GridVerticalMinor가 <b>둘 다 꺼져</b> 있었다.
                        //   밴드엔 세로줄이 넘치고 정작 그래프엔 없었으니 정반대였다.
                        //   ★[JACK 0810 추가] "정체인밖에 없잖아 부체인도 다 들어가야지" —
                        //   주(정체인)만 켰더니 부체인 자리가 비었다. 둘 다 켠다.
                        if (t == CivilDb.Styles.ProfileViewDisplayStyleType.GridVerticalMajor ||
                            t == CivilDb.Styles.ProfileViewDisplayStyleType.GridVerticalMinor)
                            ds.Visible = true;
                    }
                    catch { }
                }
                log.AppendLine(sb.Length > 0 ? "   뷰 표시 꺼진 항목:" + sb : "   뷰 표시: 전부 켜져 있다");
            }
            catch (System.Exception ex) { log.AppendLine("   뷰 표시 훑기 실패 — " + Brief(ex)); }
            tr.Commit();
        }
        catch (System.Exception ex) { log.AppendLine("   축 눈금 실패 — " + Brief(ex)); }
    }

    /// <summary>★[JACK 0810] <b>표고바(체크바)용 눈금 블록을 만든다</b> —
    /// JACK: "눈금블록은 니가 만들어야해."
    ///
    /// <para><b>왜 블록인가.</b> Civil 3D에는 '흑백 교차 표고바'를 켜는 체크박스가 없다.
    /// 실무 처방은 <b>꽉 찬 사각형 블록을 만들어 축의 주눈금에 물리고, 눈금 간격을 블록 높이의 2배</b>로
    /// 두는 것이다 — 그러면 검정·빈칸·검정이 저절로 교차한다(JACK 설명).</para>
    ///
    /// <para><b>여기서 하는 것은 블록 정의까지다.</b> 축 눈금에 <i>물리는</i> 항목
    /// (대화상자의 '눈금 블록 이름')은 <b>.NET에도 COM에도 노출돼 있지 않다</b> —
    /// <c>AxisTickStyle</c>·<c>IAeccTickStyle</c> 어느 쪽에도 블록 속성이 없다(메타데이터 전수 확인).
    /// 그래서 <b>DHT.dwt에서 한 번만 손으로 지정</b>하면, 그 뒤로는 스타일을 통째로 가져오므로
    /// 도면마다 자동이 된다. 블록은 이 코드가 매번 심어 주므로 <b>이름이 항상 존재</b>한다.</para>
    ///
    /// <para>기준점은 <b>오른쪽 아래 모서리</b>(JACK: "Y축 선과 맞닿는 모서리")라 사각형이 축 왼쪽에 걸린다.
    /// 크기는 <b>1×1 단위</b>로 두고 실제 크기는 축 스타일의 눈금 크기가 정한다 —
    /// 여기서 실치수를 박으면 축척이 바뀔 때마다 블록을 다시 만들어야 한다.</para></summary>

    /// <summary>★[JACK 0810] <b>"종단 밴드의 마지막이 값으로 마감되지 않고, 조금 더 연장해서
    /// 값 없이 딱 선으로 마감되게"</b> — 참고 도면(스샷)과 대조한 결과.
    ///
    /// <para>지금은 표의 오른쪽 테두리가 <b>마지막 값에 딱 붙어</b> 있다. 노선이 61.54m에서 끝나고
    /// 종단도의 측점 범위도 거기서 끝나기 때문이다. 참고 도면은 마지막 값 뒤로 <b>빈 여백</b>이
    /// 한 뼘 있고 그 다음에 선으로 닫힌다 — 값이 테두리에 눌리지 않아 읽기 편하다.</para>
    ///
    /// <para><b>측점 범위를 늘려서 푼다.</b> <c>StationRangeMode</c>를 사용자 지정으로 바꾸고
    /// 끝 측점만 여백만큼 민다. 늘어난 구간에는 종단 데이터가 없으므로 <b>값이 저절로 안 찍히고</b>
    /// 격자와 테두리만 그려진다 — JACK이 요구한 '값 없이 선으로 마감'이 그대로 나온다.</para>
    ///
    /// <para>여백은 <b>종이 기준</b>(8mm)이다. 모형 거리로 박으면 축척이 바뀔 때 여백이 커지거나
    /// 사라진다. 그리고 이 함수는 <b>축척을 정하기 전에</b> 불러야 한다 — 폭이 바뀌기 때문이다.</para></summary>
    private const double TailPaperMm = 8.0;

    private static bool ExtendTail(Database db, ObjectId pvId, double scale, System.Text.StringBuilder log)
    {
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            var pv = (CivilDb.ProfileView)tr.GetObject(pvId, OpenMode.ForWrite);

            // ★[v23.14] 여백은 <b>종이 기준</b>이므로 실제 축척을 곱해 모형 거리로 바꾼다.
            //   1:200이면 1.6m, 1:1000이면 8m — 종이 위에서는 언제나 8mm로 보인다.
            double pad = TailPaperMm / 1000.0 * scale;
            double s0 = pv.StationStart, s1 = pv.StationEnd;
            if (pv.StationRangeMode == CivilDb.StationRangeType.UserSpecified)
            { tr.Commit(); return false; }        // 이미 붙여 둔 꼬리에 또 붙이지 않는다
            pv.StationRangeMode = CivilDb.StationRangeType.UserSpecified;
            pv.StationStart = s0;
            pv.StationEnd = s1 + pad;
            double got = pv.StationEnd;
            tr.Commit();

            bool ok = System.Math.Abs(got - (s1 + pad)) <= 1e-6 * System.Math.Max(1.0, pad);
            if (ok)
                log.AppendLine($"표 끝 여백: 측점 {s1:F2}m → {got:F2}m (+{pad:F2}m = 종이 {TailPaperMm:F0}mm × 1:{scale:F0}) — 값 없이 선으로 마감");
            else
                log.AppendLine($"표 끝 여백: 넣은 {s1 + pad:F2}m ≠ 읽은 {got:F2}m — Civil이 노선 끝으로 되돌린 듯하다");
            return ok;
        }
        catch (System.Exception ex) { log.AppendLine("표 끝 여백 실패 — " + Brief(ex)); return false; }
    }

    /// <summary>★[JACK 0810] <b>"계획지표면의 굴곡부 측점이 여전히 누락 — 정 체인말고는 안나와."</b>
    /// JACK: "솔직히 이부분 오류가 제일 심각해."
    ///
    /// <para><b>범인은 Weeding이다.</b> 진단 로그의 밴드 항목에 <c>Weeding = 100</c>이 박혀 있다.
    /// 이건 <b>굴곡부 라벨끼리 최소 몇 m 떨어져야 찍을지</b>를 정하는 값인데,
    /// 이 노선은 <b>56m</b>다. 100m 간격을 요구하니 첫 개를 찍는 순간 나머지가 전부 '너무 가깝다'로
    /// 걸러진다. 정체인(주·보조 눈금)은 이 규칙을 타지 않아 그대로 남는다 —
    /// <b>"정 체인말고는 안나와"라는 증상과 정확히 맞는다.</b></para>
    ///
    /// <para>v23.5는 굴곡부를 <b>켰고</b>(12/12), v23.6은 찍을 <b>글자를 만들었다</b>(6개).
    /// 둘 다 맞았는데 마지막에 이 체가 전부 걸러내고 있었다. 세 판을 끈 이유가 이 한 줄이다.</para>
    ///
    /// <para><b>왜 축척을 알아야 하나.</b> 라벨이 겹치는지는 <b>종이 위 거리</b>로 정해진다 —
    /// 1:200에서 0.4m는 종이 2mm이고, 1:1000이면 같은 0.4m가 0.4mm라 글자가 겹쳐 못 읽는다.
    /// 그래서 종이 기준(2mm)으로 잡고 축척을 곱한다. 이 때문에 축척이 정해진 <b>뒤</b>에 부른다.</para></summary>
    /// <para>★[v23.13] <b>2mm는 너무 좁았다.</b> 굴곡부가 나오기 시작하자 값들이 서로 파고들었다
    /// (실측: "102.70 102.80 102.99 103.10 103.27"이 한 덩어리로 겹침).
    /// 값 글씨는 세로로 쓰이므로 <b>글자 높이가 곧 가로 폭</b>이다 — 지금 5.0mm다.
    /// 이웃 글자와 붙지 않으려면 그보다 넉넉해야 한다.</para>
    private const double WeedPaperMm = 7.0;

    private static void SetBandWeeding(Database db, ObjectId pvId, double scale, System.Text.StringBuilder log)
    {
        double want = WeedPaperMm / 1000.0 * scale;      // 모형 m
        int ok = 0, n = 0, sel = 0;
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            var pv = (CivilDb.ProfileView)tr.GetObject(pvId, OpenMode.ForWrite);
            using (var items = pv.Bands.GetBottomBandItems())
            {
                n = items.Count;
                for (int i = 0; i < items.Count; i++)
                {
                    double before = double.NaN, after = double.NaN;
                    try { before = items[i].Weeding; } catch { }
                    try { items[i].Weeding = want; after = items[i].Weeding; }
                    catch (System.Exception ex) { log.AppendLine($"   [{i}칸] 솎아내기 실패 — {Brief(ex)}"); continue; }
                    if (System.Math.Abs(after - want) <= 1e-6 * System.Math.Max(1.0, want)) ok++;
                    else log.AppendLine($"   [{i}칸] 솎아내기: 넣은 {want:0.###}m ≠ 읽은 {after:0.###}m");

                    // ★ 마지막으로 한 번 더 확인한다 — 굴곡부 선택이 여기까지 살아 있는가.
                    //   앞 단계에서 켠 것이 밴드 항목을 다시 쓰는 사이에 지워졌다면 여기서 드러난다.
                    try
                    {
                        var g = items[i].GetVerticalGeometryPointsOptions();
                        if (g != null && g[Autodesk.Civil.ProfilePointType.GradeBreak].Selected) sel++;
                    }
                    catch { }
                }
                pv.Bands.SetBottomBandItems(items);
            }
            tr.Commit();
        }
        catch (System.Exception ex) { log.AppendLine("   솎아내기 실패 — " + Brief(ex)); return; }
        log.AppendLine($"   굴곡부 솎아내기: {ok}/{n}칸 → {want:0.###}m(종이 {WeedPaperMm:F0}mm) · " +
                       $"굴곡부 선택 살아있음 {sel}/{n}칸 · 종전값 100m는 노선보다 길어 전부 걸러내고 있었다");
    }

    /// <summary>★[JACK 0810] <b>흑백 교차 표고바 — 직접 그린다.</b>
    ///
    /// <para><b>왜 직접 그리나.</b> JACK이 알려준 '눈금 블록' 처방은 종단면도 축에는 쓸 수 없다.
    /// 대화상자 [수직 축] 탭에 <b>블록 항목 자체가 없고</b>(JACK 스샷),
    /// <c>AxisTickStyle</c>·<c>IAeccTickStyle</c> 어느 쪽에도 블록 속성이 없다(메타데이터 전수 확인).
    /// UI와 API가 일치한다 — <b>Civil 3D 2026 종단면도 축에는 그 기능이 없다.</b>
    /// (블록 눈금은 <i>라벨 스타일</i>의 눈금 구성요소에는 있다. 축과는 다른 물건이다.)</para>
    ///
    /// <para>그래서 도곽과 같은 방식으로 <b>모형에 직접 그린다.</b> 오히려 이쪽이 낫다 —
    /// 블록·간격을 손으로 맞출 필요가 없고, 칸 높이를 종이 기준으로 잡으므로 축척이 바뀌어도 모양이 같다.</para>
    ///
    /// <para><b>흑백 교차의 원리는 같다</b>(JACK 설명): 한 칸씩 채우고 한 칸씩 비운다.
    /// 채운 칸은 2D 솔리드, 빈 칸은 테두리만 — 전체를 감싸는 테두리를 함께 그려 빈 칸이 흰 칸으로 읽힌다.</para>
    ///
    /// <para>자리는 <b>왼쪽 축 바깥</b>이고, 표고 숫자가 바에 겹치지 않게 축 라벨의
    /// X 간격띄우기를 바 폭만큼 밀어 준다(이건 API에 있다).</para></summary>
    private const string LayScaleBar = "DH-표고바";
    /// <summary>바 폭 · 축선과 바 사이 틈(종이 mm).</summary>
    private const double BarWidthMm = 3.0, BarGapMm = 1.0;

    /// <summary>★[JACK 0810] <b>주눈금 한 간격을 몇 줄로 나눌지</b> — "한 간격당 5줄".
    /// 축이 5m 간격이면 한 줄이 1m가 되어 표척처럼 읽힌다.</summary>
    private const int RowsPerMajor = 5;

    /// <summary>★[JACK 0810] <b>표고바 한 줄이 종이에서 가져야 할 두께</b>(mm) — 목표·최소·최대.
    /// 축척이 바뀌면 같은 표고 간격도 종이에서 두께가 달라진다. 이 범위를 벗어나면 나눔 수를 바꿔
    /// <b>어떤 축척에서도 표척처럼 읽히게</b> 한다(JACK: "축척에 따라 모든 기능이 자연스럽게 연동").</summary>
    private const double BarRowWantMm = 5.0, BarRowMinMm = 2.5, BarRowMaxMm = 10.0;

    /// <summary>★[JACK 0810] 표고 <b>주눈금 라벨</b>이 종이에서 가져야 할 간격(mm) — 목표·최소·최대.
    /// 회사 템플릿은 5m 간격인데, 1:1000이면 종이 5mm라 숫자가 붙어 못 읽는다.
    /// 반대로 1:50이면 100mm나 벌어져 눈금이 몇 개 안 남는다. 범위를 벗어날 때만 손댄다 —
    /// 멀쩡한 회사 값을 매번 덮어쓰지 않기 위해서다.</summary>
    private const double AxisLabelWantMm = 15.0, AxisLabelMinMm = 8.0, AxisLabelMaxMm = 40.0;

    private static void DrawScaleBar(Database db, ObjectId pvId, double scale, System.Text.StringBuilder log)
    {
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            var pv = (CivilDb.ProfileView)tr.GetObject(pvId, OpenMode.ForRead);
            double lo = pv.ElevationMin, hi = pv.ElevationMax, st = pv.StationStart;
            if (!(hi > lo)) { log.AppendLine("   표고바: 표고 범위를 못 읽었다"); tr.Commit(); return; }

            // ── ★[JACK 0810] <b>칸 높이는 주눈금 간격에서 나온다</b> —
            //   "프로그램이 주눈금 간격을 인식하고 한 간격당 5줄".
            //   축이 5m 간격이면 한 줄이 1m가 된다. 축 눈금과 바가 어긋나지 않는 것이 핵심이다 —
            //   숫자가 붙는 자리와 칸 경계가 다르면 자로 못 읽는다.
            double major = 0;
            try
            {
                if (tr.GetObject(pv.StyleId, OpenMode.ForRead) is CivilDb.Styles.ProfileViewStyle vs0)
                    using (var ax0 = vs0.LeftAxis)
                    using (var mj0 = ax0.MajorTickStyle) major = mj0.Interval;
            }
            catch { }
            if (!(major > 1e-9)) { major = 5.0; log.AppendLine("   표고바: 주눈금 간격을 못 읽어 5m로 가정"); }

            // ★[v23.14 · JACK 지적] <b>"축척에 따라 모든 기능이 자연스럽게 연동되어야 해."</b>
            //   '한 간격당 5줄'을 그대로 박으면 축척이 커질 때 줄이 종이에서 사라진다 —
            //   주눈금 5m를 5줄로 나누면 1:200에선 한 줄 5mm(읽힘)지만 1:1000에선 <b>1mm</b>다.
            //   그래서 <b>5줄을 기본으로 하되, 종이에서 너무 얇거나 두꺼우면 나눔 수를 바꾼다.</b>
            //   기준은 종이 거리이므로 어떤 축척에서도 표척처럼 읽힌다.
            int rowsPer = RowsPerMajor;
            double PaperMm(int k) => major / k / scale * 1000.0;
            if (PaperMm(rowsPer) < BarRowMinMm || PaperMm(rowsPer) > BarRowMaxMm)
            {
                int[] cand = { 1, 2, 4, 5, 8, 10, 20 };
                rowsPer = cand.OrderBy(k => System.Math.Abs(PaperMm(k) - BarRowWantMm)).First();
                log.AppendLine($"   표고바: 한 간격 {RowsPerMajor}줄이면 종이 {PaperMm(RowsPerMajor):F1}mm라" +
                               $" 읽기 어렵다 → {rowsPer}줄로 바꿈(종이 {PaperMm(rowsPer):F1}mm)");
            }
            double step = major / rowsPer;

            // ── ★[v23.10] <b>축선은 데이터 시작점이 아니다.</b> JACK: "파란선기준 왼쪽으로 붙어야지
            //   종단부분을 침범하면 안됨". 실측 원인: 뷰 스타일의 <c>GridStyle.AxisOffsetLeft = 0.005</c>(5mm) —
            //   왼쪽 축선이 데이터 시작점보다 <b>5mm(1:200에서 1.0m) 더 왼쪽</b>에 그려진다.
            //   종전 판은 데이터 시작점 기준으로 0.2~0.8m 왼쪽에 그렸으니 <b>축선과 데이터 사이</b>,
            //   즉 그래프 안쪽에 들어갔다. 그 오프셋만큼 더 밀어야 축선 바깥이 된다.
            double axOffM = 0;
            try
            {
                if (tr.GetObject(pv.StyleId, OpenMode.ForRead) is CivilDb.Styles.ProfileViewStyle vsg)
                    using (var gs = vsg.GridStyle) axOffM = gs.AxisOffsetLeft * scale;
            }
            catch { }

            double wM = BarWidthMm / 1000.0 * scale, gapM = BarGapMm / 1000.0 * scale;
            var layer = SectionCommand.EnsureLayer(db, tr, LayScaleBar, 7);   // 7 = 흰/검(배경 반전)
            var ms = (BlockTableRecord)tr.GetObject(
                SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);

            // ── 다시 돌릴 때 겹치지 않게 먼저 지운다. 우리가 만든 레이어라 남의 것을 건드릴 일이 없다.
            int wiped = 0;
            foreach (ObjectId id in ms)
            {
                try
                {
                    if (tr.GetObject(id, OpenMode.ForRead) is not Entity e || e.LayerId != layer) continue;
                    tr.GetObject(id, OpenMode.ForWrite).Erase(); wiped++;
                }
                catch { }
            }

            // ── 표고 ↔ 모형 Y 대응을 두 점으로 잡는다. 이후는 계산으로 푼다 —
            //   FindXY는 데이터 범위 밖에서 실패할 수 있는데, 우리는 격자 끝까지 그려야 한다.
            double x0 = 0, yLo = 0, x1 = 0, yHi = 0;
            if (!pv.FindXYAtStationAndElevation(st, lo, ref x0, ref yLo) ||
                !pv.FindXYAtStationAndElevation(st, hi, ref x1, ref yHi) ||
                System.Math.Abs(yHi - yLo) < 1e-9)
            { log.AppendLine("   표고바: 축 위치를 못 찾았다"); tr.Commit(); return; }
            double mPerY = (hi - lo) / (yHi - yLo);                 // 모형 Y 1당 표고 몇 m
            double YofE(double e) => yLo + (e - lo) / mPerY;        // 표고 → 모형 Y

            // ── ★[v23.10] <b>길이는 격자 전체다.</b> <c>ElevationMin/Max</c>는 <b>데이터 범위</b>였다
            //   (실측 102.71~112.00m). 격자는 95~125m라 바가 그래프의 1/3만 덮었다.
            //   격자 범위는 종단도 경계상자에서 되읽는다 — 상자에는 밴드가 들어 있지 않다(§도곽 실측).
            var ext = pv.GeometricExtents;
            double gLo = lo + (ext.MinPoint.Y - yLo) * mPerY;
            double gHi = lo + (ext.MaxPoint.Y - yLo) * mPerY;
            if (gHi < gLo) (gLo, gHi) = (gHi, gLo);

            // ── 축선 바깥(왼쪽)에 세운다.
            double xAxis = x0 - axOffM;
            double xR = xAxis - gapM, xL = xR - wM;
            log.AppendLine($"   표고바 자리: 데이터시작 x={x0:F3} · 축오프셋 {axOffM:F3}m → 축선 x={xAxis:F3}" +
                           $" · 바 x={xL:F3}~{xR:F3} (축선 왼쪽) · 격자표고 {gLo:F2}~{gHi:F2}m");

            // ── ★[JACK 0810] <b>한 줄에 검정·흰색 두 칸, 줄마다 뒤집는다</b> — 체스판이다.
            //   "한줄엔 검정, 힌색 두개 이게 반복되게".
            //   가로로 2등분해 한 칸만 채우고, 다음 줄에서 채우는 쪽을 바꾼다.
            //   측량 표척과 같은 무늬라 도면에서 축척이 한눈에 읽힌다.
            double xMid = (xL + xR) / 2.0;
            double e0 = System.Math.Floor(gLo / step) * step;   // 주눈금 격자에 맞춰 시작
            int filled = 0, rows = 0;
            for (double e = e0; e < gHi - 1e-9; e += step)
            {
                double a = System.Math.Max(e, gLo), b = System.Math.Min(e + step, gHi);
                if (b - a < step * 0.05) continue;                     // 반쪽도 안 되는 끄트머리는 버린다
                double ya = YofE(a), yb = YofE(b);
                rows++;
                int r = (int)System.Math.Round((e - e0) / step);
                for (int c = 0; c < 2; c++)
                {
                    if ((r + c) % 2 != 0) continue;                    // 체스판 — 줄마다 채우는 쪽이 바뀐다
                    double cl = c == 0 ? xL : xMid, cr = c == 0 ? xMid : xR;
                    var sol = new Solid(new Point3d(cl, ya, 0), new Point3d(cr, ya, 0),
                                        new Point3d(cl, yb, 0), new Point3d(cr, yb, 0));
                    sol.SetDatabaseDefaults(db); sol.LayerId = layer;
                    ms.AppendEntity(sol); tr.AddNewlyCreatedDBObject(sol, true);
                    filled++;
                }
            }

            // 전체 테두리 — 빈 칸이 '흰 칸'으로 읽히게 묶어 준다. 격자 전체를 감싼다.
            {
                double by0 = YofE(gLo), by1 = YofE(gHi);
                var box = new Polyline(4);
                box.AddVertexAt(0, new Point2d(xL, by0), 0, 0, 0);
                box.AddVertexAt(1, new Point2d(xR, by0), 0, 0, 0);
                box.AddVertexAt(2, new Point2d(xR, by1), 0, 0, 0);
                box.AddVertexAt(3, new Point2d(xL, by1), 0, 0, 0);
                box.Closed = true;
                box.SetDatabaseDefaults(db); box.LayerId = layer;
                ms.AppendEntity(box); tr.AddNewlyCreatedDBObject(box, true);
            }

            // ── 표고 숫자가 바를 밟지 않게 축 라벨을 바 폭만큼 바깥으로 민다(이건 API에 있다).
            try
            {
                if (tr.GetObject(pv.StyleId, OpenMode.ForWrite) is CivilDb.Styles.ProfileViewStyle vs)
                    using (var ax = vs.LeftAxis)
                    {
                        // ★[v23.13] <b>축 오프셋을 빼먹었다.</b> JACK: "체크스케일 때문에 눈금값이 가려짐".
                        //   라벨 오프셋은 데이터 시작점 기준인데 바는 그보다 축오프셋(5mm)만큼 더 왼쪽에 있다.
                        //   종전엔 그 5mm를 안 더해 숫자가 바 위에 얹혔다. 바 왼쪽 끝을 넘도록 민다.
                        double push = (axOffM / scale) * 1000.0 + BarGapMm + BarWidthMm + 2.0;  // 종이 mm
                        foreach (var t in new[] { ax.MajorTickStyle, ax.MinorTickStyle })
                            using (t) t.OffsetX = System.Math.Max(t.OffsetX, push / 1000.0);
                        log.AppendLine($"   표고바: 표고 숫자를 {push:F1}mm 바깥으로 밀었다" +
                                       $" (축오프셋 {axOffM / scale * 1000.0:F1} + 틈 {BarGapMm:F0} + 바 {BarWidthMm:F0} + 여유 2)");
                    }
            }
            catch (System.Exception ex) { log.AppendLine("   표고바 라벨 밀기 실패 — " + Brief(ex)); }

            tr.Commit();
            log.AppendLine($"   표고바: 주눈금 {major:0.##}m ÷ {rowsPer}줄 = 한 줄 {step:0.###}m" +
                           $"(종이 {step / scale * 1000:F1}mm) · 폭 {BarWidthMm:F0}mm · {rows}줄 · 검정 {filled}칸 · " +
                           $"표고 {gLo:F2}~{gHi:F2}m · 레이어 {LayScaleBar}" +
                           (wiped > 0 ? $" (이전 {wiped}개 지움)" : ""));
        }
        catch (System.Exception ex) { log.AppendLine("   표고바 실패 — " + Brief(ex)); }
    }

    /// <summary>★[JACK 0810] 스샷 지적 둘을 한 번에 —
    /// <b>"V H 스케일표시 이상한곳에 찍힘"</b> · <b>"지표면 정지면 선에 이상한 화살표있음"</b>.
    ///
    /// <para><b>V·H 표시</b>는 종단 뷰의 <b>그래프 제목</b>(<c>GraphStyle.TitleStyle</c>)이다.
    /// 지금 왼쪽 아래에 박혀 밴드 표를 파고든다 — 위로 올린다.</para>
    ///
    /// <para><b>화살표</b>는 우리가 그린 게 아니라 <b>회사 템플릿 스타일에 들어 있던 것</b>이다.
    /// 진단 로그: <c>DH_종단 스타일_원지반선 → ArrowHeadOption { Fit=AlwaysDraw · ArrowType=ClosedBlank }</c>.
    /// <c>AlwaysDraw</c>는 '자리가 없어도 무조건 그린다'라 선 위에 삼각형이 줄줄이 찍힌다.
    /// 표시를 끄고(<c>Arrow.Visible=false</c>) 맞춤 규칙도 <c>Omit</c>으로 내린다 — 둘 중 하나만 해도
    /// 될 수 있지만 어느 쪽이 먹었는지 로그로 남기면 다음 판에서 줄일 수 있다.</para></summary>
    private static void PolishView(Database db, ObjectId pvId, System.Text.StringBuilder log)
    {
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            var pv = (CivilDb.ProfileView)tr.GetObject(pvId, OpenMode.ForRead);

            // ── ① V·H 표시(그래프 제목) 자리
            try
            {
                if (tr.GetObject(pv.StyleId, OpenMode.ForWrite) is CivilDb.Styles.ProfileViewStyle vs)
                    using (var gs = vs.GraphStyle)
                    using (var ts = gs.TitleStyle)
                    {
                        log.AppendLine($"   V·H 표시 전: 자리 {ts.Location} · 정렬 {ts.Justification}" +
                                       $" · 오프셋({ts.OffsetX * 1000:F1},{ts.OffsetY * 1000:F1})mm · 글자 {ts.TextHeight * 1000:F1}mm");
                        ts.Location = CivilDb.Styles.GraphTitleLocationType.Top;
                        ts.Justification = CivilDb.Styles.GraphTitleJustificationType.MiddleOrCenter;
                        ts.OffsetX = 0; ts.OffsetY = 0;
                        log.AppendLine($"   V·H 표시 후: 자리 {ts.Location} · 정렬 {ts.Justification} (그래프 위 가운데)");
                    }
            }
            catch (System.Exception ex) { log.AppendLine("   V·H 표시 실패 — " + Brief(ex)); }

            // ── ② 종단선 화살표 끄기 — 이 뷰에 걸린 종단들의 스타일을 따라간다.
            int off = 0;
            try
            {
                if (tr.GetObject(pv.AlignmentId, OpenMode.ForRead) is CivilDb.Alignment al)
                    foreach (ObjectId pid in al.GetProfileIds())
                    {
                        if (tr.GetObject(pid, OpenMode.ForRead) is not CivilDb.Profile pr) continue;
                        if (tr.GetObject(pr.StyleId, OpenMode.ForWrite) is not CivilDb.Styles.ProfileStyle ps) continue;
                        string sn; try { sn = ps.Name; } catch { sn = "(이름 못 읽음)"; }
                        using (var ah = ps.ArrowHeadOption)
                        {
                            log.AppendLine($"   화살표 '{sn}' 전: 맞춤 {ah.Fit} · 종류 {ah.ArrowType} · 크기 {ah.SizeValue}");
                            ah.Fit = Autodesk.Civil.ArrowHeadFitType.Omit;
                        }
                        using (var ds = ps.GetDisplayStyleProfile(CivilDb.Styles.ProfileDisplayStyleProfileType.Arrow))
                        { ds.Visible = false; }
                        off++;
                    }
            }
            catch (System.Exception ex) { log.AppendLine("   화살표 끄기 실패 — " + Brief(ex)); }
            if (off > 0) log.AppendLine($"   화살표 끔: 종단 스타일 {off}개 (표시 off + 맞춤 Omit)");
            tr.Commit();
        }
        catch (System.Exception ex) { log.AppendLine("   뷰 다듬기 실패 — " + Brief(ex)); }
    }

    /// <summary>종단 뷰 좌우 축의 <b>주눈금 길이</b>(종이 mm). 보조는 <see cref="MinorTickRatio"/>배.
    /// 종이 기준이라 축척이 바뀌어도 눈에 보이는 길이는 같다.</summary>
    private const double AxisMajorTickMm = 2.5;

    /// <summary>★[JACK 0810] <b>"보조눈금 좀 키워줘"</b> — 실측 0.2mm였다. 칸이 27.7mm이니 안 보이는 게 당연하다.
    ///
    /// <para><b>예약해 둔 자리를 실제로 채운다.</b> 글씨 크기를 정할 때 이미 칸의 <see cref="TickShare"/>(15%)를
    /// '위아래 눈금 자리'로 빼두고 있었는데, 정작 눈금은 그 자리의 1/10만 쓰고 있었다 — 예약이 허구였다.
    /// 이제 주눈금이 그 몫을 다 쓰고 보조눈금은 그 60%를 쓴다(관례상 주가 길다).
    /// 칸 높이에서 역산하므로 밴드 칸 수가 바뀌어도 비율이 유지된다.</para>
    ///
    /// <para><b>굴곡부·수평기하·측점방정식 눈금은 '가운데 눈금'을 쓴다</b>(칸을 가로지르는 세로선).
    /// 템플릿 값이 40mm로 <b>칸(27.7mm)보다 길어</b> 그대로 두면 표 밖으로 삐져나온다 —
    /// v23.5에서 굴곡부 눈금을 처음 켰으니 이제야 드러날 자리다. 칸 높이로 눌러 준다.</para>
    /// 반환=손본 눈금 종류 수.</summary>
    private static int SetTicks(object bandStyle, double eachMm, int idx, System.Text.StringBuilder log)
    {
        int okN = 0;
        double majorM = eachMm * TickShare / 2.0 / 1000.0;      // 위아래 각각의 몫(m)
        double minorM = majorM * MinorTickRatio;
        double cellM = eachMm / 1000.0;
        foreach (var p in bandStyle.GetType().GetProperties())
        {
            if (!p.Name.EndsWith("TickStyle", StringComparison.Ordinal)) continue;
            try
            {
                if (p.GetValue(bandStyle) is not CivilDb.Styles.BandTickStyle ts) continue;
                // BandTickStyle도 CivilWrapper — 읽을 때마다 새로 생기는 IDisposable이다.
                using (ts)
                {
                    bool isMinor = p.Name.StartsWith("Minor", StringComparison.Ordinal);
                    bool isMajor = p.Name.StartsWith("Major", StringComparison.Ordinal);
                    if (isMinor || isMajor)
                    {
                        double sideM = isMinor ? minorM : majorM;
                        ts.SmallTicksAtTopSize = sideM;
                        ts.SmallTicksAtBottomSize = sideM;
                        double back = ts.SmallTicksAtBottomSize;   // 넣었다고 세지 않는다 — 되읽어 확인
                        if (System.Math.Abs(back - sideM) <= 1e-6 * System.Math.Max(1.0, sideM)) okN++;
                        else log.AppendLine($"   [{idx}칸] {p.Name}: 넣은 {sideM * 1000:F2}mm ≠ 읽은 {back * 1000:F2}mm");
                    }
                    else
                    {
                        // ★[JACK 0810] <b>"밴드에 세로줄이 생김 — 세로줄은 종단에 생겨야 됨"</b>
                        //   굴곡부·수평기하 눈금이 <c>IncrementTicksToFullHeight=True</c>라
                        //   <b>칸을 위아래로 관통</b>해 표가 세로줄투성이가 됐다. 참고 도면의 정보표시
                        //   테이블은 가로 행선만 있고 세로줄이 없다 — 세로 격자는 그래프 쪽 일이다.
                        //   → 관통을 끄고 <b>행 경계의 짧은 눈금</b>만 남긴다.
                        ts.IncrementTicksToFullHeight = false;
                        ts.IncrementSmallTicksAtMiddle = false;
                        ts.SmallTicksAtTopSize = majorM;
                        ts.SmallTicksAtBottomSize = majorM;
                        if (ts.IncrementTicksToFullHeight)
                            log.AppendLine($"   [{idx}칸] {p.Name}: 관통 끄기가 안 먹었다");
                        else okN++;
                    }
                }
            }
            catch (System.Exception ex) { log.AppendLine($"   [{idx}칸] {p.Name} 눈금 실패 — {Brief(ex)}"); }
        }
        return okN;
    }

    /// <summary>반사로 속성 하나 쓰기 — 스타일 종류마다 있는 속성이 달라 이름을 박지 않는다.</summary>
    private static bool Set(object o, string name, double v)
    {
        try
        {
            var p = o.GetType().GetProperty(name);
            if (p == null || !p.CanWrite) return false;
            p.SetValue(o, v); return true;
        }
        catch { return false; }
    }

    /// <summary>밴드 스타일이 물고 있는 <b>라벨 스타일들의 글자 높이</b>를 칸 크기에 맞춘다.
    /// 칸 안에 들어가는 숫자는 밴드 자체가 아니라 라벨 스타일이 그리므로 여기까지 손대야 한다.
    ///
    /// <para>★[v23.5] <b>종전 판이 0을 찍은 자리.</b> 라벨 스타일 객체에 <c>TextHeight</c>·<c>Height</c>·
    /// <c>TextSize</c>를 차례로 넣어 봤는데 <b>셋 다 없다.</b> 진단 로그가 이미 답을 보여주고 있었다 —
    /// <c>GetComponentsDrawOrder() = ['' [AeccDbLabelStyleText]]</c>. 글자는 라벨 스타일이 아니라
    /// 그 안의 <b>구성요소</b>가 그린다. 메타데이터로 확인한 실제 경로는:</para>
    /// <code>
    /// LabelStyle.GetComponents(LabelStyleComponentType.Text)      // → ObjectIdCollection
    ///   → LabelStyleTextComponent.Text.Height                     // → PropertyDouble
    ///     → .Value = 높이(m)                                       // ★ double 대입이 아니라 .Value
    /// </code>
    /// <para><b>Civil의 스타일 값은 맨 double이 아니라 <c>PropertyDouble</c> 래퍼다.</b> 래퍼 자체는
    /// 읽기 전용 속성이라 반사로 '쓸 수 있는 double'을 찾으면 영원히 안 걸린다 — 종전 판이 그랬다.</para>
    ///
    /// <para><b>제목과 값은 다른 자를 쓴다.</b> <c>TitleTextLabelStyleId</c>도 이름이 …LabelStyleId로 끝나서
    /// 종전 방식대로면 제목까지 값 크기로 덮어썼다(제목은 세로 4글자, 값은 가로 6자라 기준이 다르다).</para>
    /// 반환=실제로 <b>읽어서 확인된</b> 구성요소 수 — 넣었다고 세지 않는다(되는 것처럼 보이는 실패 방지).</summary>
    private static int SetLabelHeight(Transaction tr, object bandStyle, double valM, double ttlM,
                                      int idx, System.Text.StringBuilder log, ref int tries)
    {
        int okN = 0;
        foreach (var p in bandStyle.GetType().GetProperties())
        {
            if (!p.Name.EndsWith("LabelStyleId", StringComparison.Ordinal)) continue;
            bool isTitle = p.Name.StartsWith("TitleText", StringComparison.Ordinal);
            double hM = isTitle ? ttlM : valM;
            string tag = $"[{idx}칸] {p.Name}";
            try
            {
                // ★[v23.5] 관문마다 **왜 건너뛰었는지**를 남긴다. 종전엔 네 자리가 조용히 continue라
                //   값글씨가 다시 0으로 나와도 무엇이 먹었는지 알 방법이 없었다 —
                //   특히 '구성요소 0개'는 v23.4 증상과 겉보기가 완전히 같다.
                if (p.GetValue(bandStyle) is not ObjectId id) { log.AppendLine($"   {tag}: ObjectId가 아니다"); continue; }
                if (id.IsNull) { log.AppendLine($"   {tag}: 비어 있다(이 밴드는 이 라벨을 안 쓴다)"); continue; }
                var obj = tr.GetObject(id, OpenMode.ForWrite);
                if (obj is not CivilDb.Styles.LabelStyle ls)
                { log.AppendLine($"   {tag}: 라벨 스타일이 아니다({obj.GetType().Name})"); continue; }

                string lsName; try { lsName = ls.Name; } catch { lsName = "(이름 못 읽음)"; }
                using var comps = ls.GetComponents(CivilDb.Styles.LabelStyleComponentType.Text);
                if (comps.Count == 0)
                { log.AppendLine($"   {tag} '{lsName}': 글자 구성요소가 0개 — 크기를 걸 자리가 없다"); continue; }

                foreach (ObjectId cid in comps)
                {
                    tries++;
                    try
                    {
                        // ★[v23.5] 구성요소 열기를 **안쪽 try 안**에 둔다. 밖에 두면 여기서 던졌을 때
                        //   "라벨 열기 실패"라고 한 칸 어긋난 자리를 적고 남은 구성요소를 통째로 건너뛴다.
                        if (tr.GetObject(cid, OpenMode.ForWrite) is not CivilDb.Styles.LabelStyleTextComponent tc)
                        { log.AppendLine($"   {tag} '{lsName}': 글자 구성요소가 아니다"); continue; }

                        // ★[v23.5] <b>StyleText·PropertyDouble은 둘 다 IDisposable이고 호출마다 새로 생긴다</b>
                        //   (StyleText:CivilWrapper, PropertyDouble:TreeOidWrapper→DisposableWrapper — 메타데이터 확인).
                        //   `tc.Text.Height.Value`를 한 번 타면 버릴 객체가 2개 생긴다. 그 사슬을 세 번 타면
                        //   구성요소당 6개 — 6칸×7라벨이면 한 번 실행에 250개가 넘게 샌다.
                        double back;
                        bool overridden = false, locked = false, ovable = false;
                        using (var txt = tc.Text)
                        using (var hp = txt.Height)
                        {
                            hp.Value = hM;
                            // ★[JACK 0810] <b>"제목 넘어감, 제목글씨를 세로로 할것"</b>
                            //   크기 계산은 처음부터 세로를 전제로 했는데("4글자가 가장 기니 그걸 기준")
                            //   정작 <b>회전을 안 시켰다.</b> 가로로 쓰이니 4글자가 옆 칸을 침범한다.
                            //   세로로 눕히면 4글자 × 6.2mm = 24.9mm로 칸(27.7mm) 안에 들어간다 —
                            //   즉 회전이 빠졌던 것이지 크기 공식이 틀린 게 아니었다.
                            if (isTitle)
                                using (var ang = txt.Angle) { ang.Value = System.Math.PI / 2.0; }
                            // ★[JACK 0810] "모든 글씨는 흰색(검정)으로" — 라벨 스타일이 표시 색을
                            //   덮어쓸 수 있으므로 여기서도 7번을 박는다(밴드 표시 쪽만 고치면 빨간 글씨가 남는다).
                            try { using (var col = txt.Color) col.Value = White; } catch { }
                            // 넣은 값을 **다시 읽어** 확인한다 — 상위 스타일에서 잠겨 있으면 조용히 무시된다.
                            // 그때 '성공'으로 세면 로그가 거짓말을 한다(§22.6 '되는 것처럼 보이는 실패').
                            back = hp.Value;
                            try { ovable = hp.IsOverridable; overridden = hp.Overridden; locked = hp.Locked; } catch { }
                        }
                        if (System.Math.Abs(back - hM) <= 1e-6 * System.Math.Max(1.0, System.Math.Abs(hM))) okN++;
                        else
                            // '잠김?'이라고 추측하지 않는다 — 실제 값을 읽어 적는다.
                            log.AppendLine($"   {tag} '{lsName}': 넣은 {hM * 1000:F2}mm ≠ 읽은 {back * 1000:F2}mm" +
                                           $" (재정의가능 {ovable} · 재정의됨 {overridden} · 잠김 {locked})");
                    }
                    catch (System.Exception ex) { log.AppendLine($"   {tag} '{lsName}' 글씨 실패 — {Brief(ex)}"); }
                }
            }
            catch (System.Exception ex) { log.AppendLine($"   {tag}: 라벨 열기 실패 — {Brief(ex)}"); }
        }
        return okN;
    }

    /// <summary>밴드가 <b>종이에서</b> 차지하는 총높이(m). 축척과 무관하게 일정하다 —
    /// 밴드는 BandHeight(예 0.003=3mm)처럼 종이 크기로 정의되고 거기에 도면 축척이 곱해지기 때문이다.
    /// 속성 이름을 박지 않고 반사로 읽는다(스타일 종류마다 이름이 조금씩 다르다).</summary>
    private static double BandPaperHeight(Database db, ObjectId pvId, System.Text.StringBuilder log)
    {
        double sum = 0; int n = 0;
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            var pv = (CivilDb.ProfileView)tr.GetObject(pvId, OpenMode.ForRead);
            foreach (bool bottom in new[] { true, false })
            {
                using var items = bottom ? pv.Bands.GetBottomBandItems() : pv.Bands.GetTopBandItems();
                for (int i = 0; i < items.Count; i++)
                {
                    double h = 0, gap = 0;
                    try { gap = System.Math.Abs(items[i].Gap); } catch { }
                    try
                    {
                        var st = tr.GetObject(items[i].BandStyleId, OpenMode.ForRead);
                        var p = st.GetType().GetProperty("BandHeight");
                        if (p != null) h = System.Convert.ToDouble(p.GetValue(st));
                    }
                    catch { }
                    sum += h + gap; n++;
                }
            }
            tr.Commit();
        }
        catch (System.Exception ex) { log.AppendLine("밴드 높이 측정 실패 — " + ex.Message); }
        log.AppendLine($"밴드 {n}칸 · 종이 총높이 {sum * 1000.0:F1}mm");
        return sum;
    }

    /// <summary>직전에 계산한 밴드의 <b>모형</b> 높이(m) — 도곽이 밴드까지 덮게 하려고 넘긴다.</summary>
    private static double LastBandModelH;

    /// <summary>★[JACK 0810] 도면 축척을 시트 축척에 맞춘다.
    /// Civil 3D는 밴드 높이·글자 크기를 <b>종이 크기 × 도면 축척</b>으로 그린다. 도면 축척이 1:1000인데
    /// 1:100으로 보면 모든 것이 10배로 보인다 — JACK이 본 '칸 높이가 이상해'가 정확히 이것이었다.</summary>
    private static void SetDrawingScale(Database db, double scale, System.Text.StringBuilder log)
    {
        try
        {
            string nm = $"1:{scale:F0}";
            var occ = db.ObjectContextManager.GetContextCollection("ACDB_ANNOTATIONSCALES");
            if (occ == null) { log.AppendLine("도면 축척 설정 건너뜀(주석 축척 목록 없음)"); return; }
            var ctx = occ.GetContext(nm);
            if (ctx == null)
            {
                var s = new AnnotationScale { Name = nm, PaperUnits = 1.0, DrawingUnits = scale };
                occ.AddContext(s);
                ctx = occ.GetContext(nm);
            }
            if (ctx is AnnotationScale asc) { db.Cannoscale = asc; log.AppendLine($"도면 축척 → {nm}"); }
            else log.AppendLine($"도면 축척 '{nm}'을 만들지 못함 — 밴드 크기가 어긋날 수 있다");
        }
        catch (System.Exception ex) { log.AppendLine("도면 축척 설정 실패 — " + ex.Message); }
    }

    private static void SetStyle(Database db, ObjectId pvId, ObjectId styleId)
    {
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            var pv = (CivilDb.ProfileView)tr.GetObject(pvId, OpenMode.ForWrite);
            pv.StyleId = styleId;
            tr.Commit();
        }
        catch { }
    }

    private static Extents3d Measure(Database db, ObjectId pvId)
    {
        using var tr = db.TransactionManager.StartTransaction();
        var pv = (Entity)tr.GetObject(pvId, OpenMode.ForRead);
        var e = pv.GeometricExtents;
        tr.Commit();
        return e;
    }

    /// <summary>(구) 과장만 고르던 것 — 지금은 <see cref="FitSheet"/>가 축척과 함께 푼다.</summary>
    private static string ApplyExaggeration_Unused(Database db, ObjectId pvId, System.Text.StringBuilder log)
    {
        try
        {
            var cdoc = CivilApp.CivilApplication.ActiveDocument;
            // 회사 표준 종단 뷰 스타일 중 이름에 과장 배율이 적힌 것들을 모은다.
            var cands = ProfileStyleTemplate.Collect(db, cdoc, x => x.Cls == ProfileStyleTemplate.ClsProfileView)
                        .Select(s => (S: s, V: ParseExaggeration(s.Name)))
                        .Where(x => x.V > 0).OrderBy(x => x.V).ToList();
            if (cands.Count == 0) return "수직과장=(회사 표준 뷰 스타일 없음)";

            // 표고 범위 — 종단도에 걸린 종단들의 최고·최저
            double lo = double.MaxValue, hi = double.MinValue;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var pv = (CivilDb.ProfileView)tr.GetObject(pvId, OpenMode.ForRead);
                if (tr.GetObject(pv.AlignmentId, OpenMode.ForRead) is CivilDb.Alignment al)
                {
                    foreach (ObjectId pid in al.GetProfileIds())
                    {
                        if (tr.GetObject(pid, OpenMode.ForRead) is not CivilDb.Profile pr) continue;
                        try { lo = System.Math.Min(lo, pr.ElevationMin); hi = System.Math.Max(hi, pr.ElevationMax); }
                        catch { }
                    }
                }
                tr.Commit();
            }
            if (lo > hi) return "수직과장=(표고 범위를 재지 못함 — 그대로 둠)";
            double range = System.Math.Max(0.1, hi - lo);

            // 축척을 모른 채 과장을 정해야 하므로, '그래프가 밴드 영역을 침범하지 않는' 쪽으로만 고른다.
            //   그래프 높이(모형) = 표고범위 × 과장. 이게 전체 높이의 1/3을 넘지 않게.
            //   축척은 뒤에서 폭·높이를 함께 보고 정하므로, 여기서는 **비율**만 맞춘다.
            double best = cands[0].V; string bestName = cands[0].S.Name;
            foreach (var c in cands)
            {
                // 그래프(표고범위×과장)가 전체(그래프+밴드)의 1/2을 넘지 않는 선 — 밴드가 나머지 절반을 쓴다.
                if (range * c.V <= range * 1.0 * 3.0) { best = c.V; bestName = c.S.Name; }
            }
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var pv = (CivilDb.ProfileView)tr.GetObject(pvId, OpenMode.ForWrite);
                var pick = cands.First(c => c.S.Name == bestName);
                pv.StyleId = pick.S.Id;
                tr.Commit();
            }
            log.AppendLine($"표고 범위 {lo:F2}~{hi:F2}m({range:F2}m) → 뷰 스타일 '{bestName}'");
            return $"수직과장 {best:0.#}배 ('{bestName}')";
        }
        catch (System.Exception ex) { return "수직과장=(자동 선택 실패: " + ex.Message + ")"; }
    }

    /// <summary>'DH_종단 뷰 스타일(수직과장 2.5)' 같은 이름에서 배율을 읽는다. '없음'=1배.</summary>
    private static double ParseExaggeration(string name)
    {
        if (!name.Contains("수직과장")) return 0;
        if (name.Contains("없음")) return 1.0;
        int i = name.IndexOf("수직과장", StringComparison.Ordinal) + 4;
        var digits = new string(name.Skip(i).TakeWhile(ch => char.IsDigit(ch) || ch == '.' || ch == ' ').ToArray()).Trim();
        return double.TryParse(digits, out double v) && v > 0 ? v : 0;
    }

    /// <summary>도곽 한 장이 모형공간에서 차지하는 자리.
    /// <paramref name="View"/>는 배치의 뷰포트가 보여줄 영역(내부 폭 × 내부높이 2/3)이고,
    /// <paramref name="Sheet"/>는 종이 전체가 덮는 영역이다.</summary>
    private readonly record struct Frame(Point2d ViewCenter, double ViewW, double ViewH,
                                         Point2d SheetMin, double SheetW2, double SheetH2);

    /// <summary>모형공간에 <b>도곽 범위</b>를 그린다 — 축척을 곱한 실제 크기.
    /// 바깥 사각형=종이 전체, 안쪽 사각형=배치 뷰포트가 보여줄 자리(아래 2/3).
    /// 눈으로 '종단도가 한 장에 들어오는지'를 바로 대볼 수 있다.</summary>
    private static List<Frame> DrawModelFrames(Database db, Extents3d ext, double scale,
                                               System.Text.StringBuilder log)
    {
        double s = scale / 1000.0;                    // 종이 1mm = 모형 s m
        double vw = InnerW * s, vh = (InnerH * 2.0 / 3.0) * s;
        double cx = (ext.MinPoint.X + ext.MaxPoint.X) / 2.0;
        double cy = (ext.MinPoint.Y + ext.MaxPoint.Y) / 2.0;

        // 지금은 토공 = 한 장(JACK: "토공 종단의 기준이야. 관로 종단은 별도 기준을 만들 거야").
        // 여러 장이 필요해지면 여기서 cx를 폭만큼 밀며 반복하면 된다 — 나머지 구조는 그대로다.
        var list = new List<Frame>();
        double vx0 = cx - vw / 2.0, vy0 = cy - vh / 2.0;
        var sheetMin = new Point2d(vx0 - MarginLR * s, vy0 - MarginTB * s);
        list.Add(new Frame(new Point2d(cx, cy), vw, vh, sheetMin, SheetW * s, SheetH * s));

        using var tr = db.TransactionManager.StartTransaction();
        var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
        var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
        ObjectId layer = SectionCommand.EnsureLayer(db, tr, LayFrameModel, 30);   // 30 = 주황
        foreach (var f in list)
        {
            AddRect(tr, ms, layer, f.SheetMin.X, f.SheetMin.Y,
                    f.SheetMin.X + f.SheetW2, f.SheetMin.Y + f.SheetH2);                 // 종이 전체
            AddRect(tr, ms, layer, f.ViewCenter.X - f.ViewW / 2, f.ViewCenter.Y - f.ViewH / 2,
                    f.ViewCenter.X + f.ViewW / 2, f.ViewCenter.Y + f.ViewH / 2);         // 뷰포트가 볼 자리
        }
        tr.Commit();
        log.AppendLine($"모형 도곽 {list.Count}장 · 종이 {SheetW * s:F1}m × {SheetH * s:F1}m · 뷰 {vw:F1}m × {vh:F1}m (레이어 {LayFrameModel})");
        return list;
    }

    private static void AddRect(Transaction tr, BlockTableRecord owner, ObjectId layer,
                                double x0, double y0, double x1, double y1)
    {
        var pl = new Polyline();
        pl.AddVertexAt(0, new Point2d(x0, y0), 0, 0, 0);
        pl.AddVertexAt(1, new Point2d(x1, y0), 0, 0, 0);
        pl.AddVertexAt(2, new Point2d(x1, y1), 0, 0, 0);
        pl.AddVertexAt(3, new Point2d(x0, y1), 0, 0, 0);
        pl.Closed = true;
        pl.LayerId = layer;
        owner.AppendEntity(pl); tr.AddNewlyCreatedDBObject(pl, true);
    }

    /// <summary>배치를 만들고 도곽·내부선·1/3 구분선을 그린 뒤, 아래 2/3에 뷰포트를 놓는다.
    /// 뷰포트는 <b>모형의 도곽 범위를 그대로</b> 가져온다 — 사용자는 배치에서 가져오기만 하면 된다.</summary>
    private static string MakeLayout(Database db, Editor ed, Frame frame, double scale,
                                     System.Text.StringBuilder log)
    {
        var lm = LayoutManager.Current;
        string name = LayoutBase;
        for (int i = 2; lm.LayoutExists(name); i++) name = $"{LayoutBase}_{i}";
        ObjectId layId = lm.CreateLayout(name);
        lm.CurrentLayout = name;

        using var tr = db.TransactionManager.StartTransaction();
        var lay = (Layout)tr.GetObject(layId, OpenMode.ForWrite);
        var ps = (BlockTableRecord)tr.GetObject(lay.BlockTableRecordId, OpenMode.ForWrite);
        ObjectId layer = SectionCommand.EnsureLayer(db, tr, LayFrame, 7);

        // 도곽은 배치 원점을 좌하단으로 잡는다 — 종이 좌표와 1:1이라 눈으로 검산된다.
        void Rect(double x0, double y0, double x1, double y1)
        {
            var pl = new Polyline();
            pl.AddVertexAt(0, new Point2d(x0, y0), 0, 0, 0);
            pl.AddVertexAt(1, new Point2d(x1, y0), 0, 0, 0);
            pl.AddVertexAt(2, new Point2d(x1, y1), 0, 0, 0);
            pl.AddVertexAt(3, new Point2d(x0, y1), 0, 0, 0);
            pl.Closed = true;
            pl.LayerId = layer;
            ps.AppendEntity(pl); tr.AddNewlyCreatedDBObject(pl, true);
        }
        Rect(0, 0, SheetW, SheetH);                                          // ① 도곽
        Rect(MarginLR, MarginTB, SheetW - MarginLR, SheetH - MarginTB);      // ② 내부 여백선

        // ③ 1/3 구분선 두 개 — 위 1/3=종평면도, 가운데=종단, 아래=밴드
        double y1 = MarginTB + InnerH / 3.0;          // 밴드/종단 경계
        double y2 = MarginTB + InnerH * 2.0 / 3.0;    // 종단/종평면도 경계
        foreach (double y in new[] { y1, y2 })
        {
            var ln = new Line(new Point3d(MarginLR, y, 0), new Point3d(SheetW - MarginLR, y, 0)) { LayerId = layer };
            ps.AppendEntity(ln); tr.AddNewlyCreatedDBObject(ln, true);
        }

        // ④ 아래 2/3에 뷰포트 — **모형의 도곽 범위를 그대로 가져온다**
        double vpH = ViewH;
        var vp = new Viewport();
        ps.AppendEntity(vp); tr.AddNewlyCreatedDBObject(vp, true);
        vp.Width = InnerW;
        vp.Height = vpH;
        vp.CenterPoint = new Point3d(SheetW / 2.0, MarginTB + vpH / 2.0, 0);
        vp.On = true;
        vp.CustomScale = 1000.0 / scale;      // 모형 1m = 종이 1000/축척 mm
        vp.ViewCenter = frame.ViewCenter;     // 모형 도곽의 뷰 영역 한가운데
        vp.Locked = true;                     // 실수로 확대해 축척이 틀어지는 것을 막는다

        // ⑤ 출력 용지를 A1로 — 실패해도 도곽은 그대로 쓸 수 있으므로 조용히 넘어간다.
        try
        {
            var psv = PlotSettingsValidator.Current;
            using var pset = new PlotSettings(lay.ModelType);
            pset.CopyFrom(lay);
            psv.SetPlotConfigurationName(pset, "DWG To PDF.pc3", null);
            psv.RefreshLists(pset);
            string? media = psv.GetCanonicalMediaNameList(pset).Cast<string>()
                               .FirstOrDefault(m => m.Contains("A1", StringComparison.OrdinalIgnoreCase));
            if (media != null) psv.SetCanonicalMediaName(pset, media);
            psv.SetPlotPaperUnits(pset, PlotPaperUnit.Millimeters);
            psv.SetPlotType(pset, Autodesk.AutoCAD.DatabaseServices.PlotType.Layout);
            lay.CopyFrom(pset);
            log.AppendLine("출력 용지: " + (media ?? "(A1 용지를 못 찾음 — 도곽만 적용)"));
        }
        catch (System.Exception ex) { log.AppendLine("출력 용지 설정 건너뜀 — " + ex.Message); }

        tr.Commit();
        log.AppendLine($"배치 '{name}' · 뷰포트 {InnerW:F0}×{vpH:F1}mm · 축척 1:{scale:F0}");
        ed.Regen();
        return name;
    }

    private static ObjectId PickProfileView(Database db, Editor ed, out string name)
    {
        name = "";
        var found = new List<(ObjectId Id, string Name)>();
        using (var tr = db.TransactionManager.StartTransaction())
        {
            try
            {
                var cdoc = CivilApp.CivilApplication.ActiveDocument;
                foreach (ObjectId aid in cdoc.GetAlignmentIds())
                {
                    if (tr.GetObject(aid, OpenMode.ForRead) is not CivilDb.Alignment al) continue;
                    foreach (ObjectId vid in al.GetProfileViewIds())
                        if (tr.GetObject(vid, OpenMode.ForRead) is CivilDb.ProfileView pv) found.Add((vid, pv.Name));
                }
            }
            catch { }
            tr.Commit();
        }
        if (found.Count == 0)
        {
            SectionCommand.Refuse(ed, "도면에 종단도가 없습니다.\n먼저 [종단도]로 만드세요.");
            return ObjectId.Null;
        }
        if (found.Count == 1) { name = found[0].Name; return found[0].Id; }

        ed.WriteMessage($"\n[도곽] 종단도가 {found.Count}개입니다 — 화면에서 고르세요.");
        var peo = new PromptEntityOptions("\n[도곽] 종단도를 클릭 (Esc=취소): ");
        peo.SetRejectMessage("\n종단도가 아닙니다.");
        peo.AddAllowedClass(typeof(CivilDb.ProfileView), true);
        var per = ed.GetEntity(peo);
        if (per.Status != PromptStatus.OK) return ObjectId.Null;
        using (var tr = db.TransactionManager.StartTransaction())
        {
            if (tr.GetObject(per.ObjectId, OpenMode.ForRead) is CivilDb.ProfileView pv) name = pv.Name;
            tr.Commit();
        }
        return per.ObjectId;
    }
}
