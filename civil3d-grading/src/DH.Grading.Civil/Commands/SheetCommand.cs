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

    /// <summary>자리를 얼마나 채울지 — 100%로 채우면 그래프가 테두리에 붙어 답답하다.
    /// JACK 0810: "너무 딱 맞으면 그러니깐 약간의 버퍼는 줘서 도면이 좀 균형감 있게 해야지."</summary>
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
                //   자리의 92%만 쓰기로 하고 나머지는 여백으로 둔다(위아래·좌우 각 4%씩).
                double needW = wM0 * 1000.0 / (InnerW * Fill);
                double needH0 = availMm > 1.0 ? hM0 * 1000.0 / (availMm * Fill) : 1e9;
                double want = System.Math.Max(needW, needH0);
                double s0 = Scales.FirstOrDefault(s => s >= want);
                if (s0 <= 0) { s0 = Scales[Scales.Length - 1]; overflow = true; }
                scale = s0;
                string bind = needW >= needH0 ? "폭" : "높이";
                log.AppendLine($"토공 기준 — 수직과장 없음 · 단일 축척. 그래프 모형 {wM0:F1}m × {hM0:F1}m");
                log.AppendLine($"밴드는 종이 {bandMm:F1}mm 고정 → 그래프 자리 {availMm:F1}mm");
                log.AppendLine($"필요 폭 1:{needW:F0} · 높이 1:{needH0:F0} → **{bind}**이 긴 쪽 → S=1:{s0:F0}");
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
        int n = 0, hOk = 0, gOk = 0, tOk = 0, vOk = 0;
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
                    try { items[i].Gap = 0.0; gOk++; } catch { }
                    vOk += EnableGeometryPoints(items[i]);
                    try
                    {
                        var st = tr.GetObject(items[i].BandStyleId, OpenMode.ForWrite);
                        if (Set(st, "BandHeight", eachM)) hOk++;

                        // ★[JACK 0810] 글씨 크기를 **칸 높이에서 역산**한다 — "밴드 높이에서 위아래
                        //   보조눈금 길이를 제외한 길이를 구하고, 000.00 표현식 기준으로 가장 꽉 찬 크기로."
                        //   제목은 세로로 쓰고 4글자(누가거리·구간거리)가 가장 기니 그걸 기준으로 삼는다.
                        //   ※ JACK 0810: "회사 스타일이란 건 없어. 그냥 네가 만들면 돼" — 값을 직접 정한다.
                        double eachMm = BandH / n;
                        double availMm = eachMm * (1.0 - TickShare);          // 위아래 눈금 자리를 뺀 길이
                        double valMm = availMm / (6.0 * DigitW) * TextFill;   // "000.00" 6자
                        double ttlMm = eachMm / 4.0 * TextFill;               // 한글 4자(폭≈높이)
                        Set(st, "TextHeight", ttlMm / 1000.0);
                        Set(st, "TextBoxWidth", ttlMm * 1.8 / 1000.0);        // 글씨가 상자 밖으로 안 나가게
                        SetLabelHeight(tr, st, valMm / 1000.0, ref tOk);
                    }
                    catch { }
                }
                pv.Bands.SetBottomBandItems(items);
            }
            tr.Commit();
        }
        catch (System.Exception ex) { return "밴드 정리 실패 — " + ex.Message; }
        string s = $"밴드 {n}칸 균등 — 각 {eachM * 1000.0:F1}mm(합 {BandH:F1}mm) · 간격 0 (높이 {hOk} · 간격 {gOk} · 값글씨 {tOk} · 굴곡부 {vOk})";
        log.AppendLine(s);
        return s;
    }

    /// <summary>★[JACK 0810] <b>정지면 굴곡부에 측점이 자동으로 찍히게</b> 한다 —
    /// "처음 종단도 그릴 때 정지 지표면에 한해서 굴곡부는 자동으로 측점이 추가되게 해 줘."
    /// <para>수집기(<see cref="StationMarks"/>)가 굴곡부를 잡고는 있었지만 종단도에 <b>보이지</b> 않았다.
    /// 밴드 항목의 수직 기하점 표시를 켜면 계획 종단이 꺾이는 자리마다 눈금과 측점이 자동으로 찍힌다 —
    /// 단면검토선도, 사람 손도 필요 없다. 옵션 목록의 구조를 이름으로 박지 않고 반사로 훑어 전부 켠다.</para>
    /// 반환=켠 항목 수(0이면 이 방식이 안 통한 것이니 로그에 드러난다).</summary>
    private static int EnableGeometryPoints(object item)
    {
        int on = 0;
        try
        {
            var t = item.GetType();
            foreach (string g in new[] { "GetVerticalGeometryPointsOptions", "GetHorizontalGeometryPointsOptions" })
            {
                var mg = t.GetMethod(g, System.Type.EmptyTypes);
                if (mg == null) continue;
                var ms = t.GetMethod(g.Replace("Get", "Set"));
                object? sel = null;
                try { sel = mg.Invoke(item, null); } catch { }
                if (sel == null) continue;
                if (sel is System.Collections.IEnumerable en)
                    foreach (var o in en)
                    {
                        if (o == null) continue;
                        foreach (string pn in new[] { "Selected", "IsSelected", "Visible" })
                        {
                            try
                            {
                                var pi = o.GetType().GetProperty(pn);
                                if (pi != null && pi.CanWrite && pi.PropertyType == typeof(bool))
                                { pi.SetValue(o, true); on++; break; }
                            }
                            catch { }
                        }
                    }
                try { ms?.Invoke(item, new object[] { sel }); } catch { }
            }
        }
        catch { }
        return on;
    }

    /// <summary>위아래 보조눈금이 칸 높이에서 차지하는 몫 · 숫자 한 글자의 폭(높이 대비) ·
    /// 자리를 얼마나 채울지. JACK 0810 "가장 꽉 찬 크기로" — 다만 테두리에 닿지 않게 조금 남긴다.</summary>
    private const double TickShare = 0.15, DigitW = 0.6, TextFill = 0.9;

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

    /// <summary>밴드 스타일이 물고 있는 <b>라벨 스타일들의 글자 높이</b>를 값 크기에 맞춘다.
    /// 칸 안에 들어가는 숫자는 밴드 자체가 아니라 라벨 스타일이 그리므로 여기까지 손대야 한다.</summary>
    private static void SetLabelHeight(Transaction tr, object bandStyle, double hM, ref int okN)
    {
        foreach (var p in bandStyle.GetType().GetProperties())
        {
            if (!p.Name.EndsWith("LabelStyleId", StringComparison.Ordinal)) continue;
            try
            {
                if (p.GetValue(bandStyle) is not ObjectId id || id.IsNull) continue;
                var ls = tr.GetObject(id, OpenMode.ForWrite);
                // 라벨 스타일의 글자 높이는 구성요소 안에 있어 이름이 여러 가지다 — 있는 것을 쓴다.
                foreach (string nm in new[] { "TextHeight", "Height", "TextSize" })
                    if (Set(ls, nm, hM)) { okN++; break; }
            }
            catch { }
        }
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
