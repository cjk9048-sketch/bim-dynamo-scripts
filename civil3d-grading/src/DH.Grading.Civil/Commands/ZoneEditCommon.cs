using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using DH.Grading.Core;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace DH.Grading.Civil.Commands;

/// <summary>
/// [변환 공통 0804 — JACK] 옹벽 변환(DHWALL)·사면 변환(DHSLOPE)의 공통 흐름.
/// 규칙(JACK 확정):
///  · **1회 실행 = 1개만 바뀐다.** 선을 연달아 눌러도 마지막에 누른 것만 선택된다.
///  · 제원은 <b>프롬프트 키워드</b>로 바꾸고 Enter를 치면 그대로 적용된다(JACK 0820) —
///      옹벽: 단높이(H) · 소단길이(T)              (구배는 수직 1:0.05 고정)
///      사면: 단높이(H) · 사면구배(R) · 소단길이(T)
///    기본값 = **최초 정지옵션에서 준 값**(클릭한 방향의 값을 따라간다 — JACK 0820)
///    ※ 단높이는 <b>구간이 아니라 방향(절토/성토) 전체</b>에 적용된다 —
///      링은 같은 표고의 등고선이라 둘레의 일부만 단높이를 바꿀 수 없다(v16.9).
///      층 전체를 바꾸는 것은 링마다 표고가 여전히 하나라 안전하다(JACK 0820).
///  · 입력값은 **클릭한 단부터 바깥 끝까지** 적용된다. 여러 번 실행하면 규칙이 쌓여
///    '아래는 급하게 · 위는 완만하게'가 된다.
/// 두 명령은 묻는 항목만 다르고 나머지(선 작도·선택·구간 병합·재생성)는 전부 같다.
/// </summary>
internal static class ZoneEditCommon
{
    private const short SelAci = 2;   // 선택 = 노랑(대상선 시안과 구분)

    /// <summary>★[JACK 0820 '정지옵션과 변환은 연동되긴 해야 해'] 변환 기본값 = <b>지금 정지옵션에 있는 값</b>.
    /// <para>고정 숫자도, 번들 저장값도 아니다 — 사용자가 정지옵션에서 방금 바꾼 값이 그대로 기본값이 된다.
    /// 절토·성토는 단높이·소단폭·구배가 따로이므로(v16.6) <b>클릭한 방향</b>의 값을 따라간다.</para>
    /// 단높이는 <b>그 단에 실제로 적용 중인 값</b>을 준다(규칙이 쌓여 있으면 그 값) —
    /// "지금 몇 m인가"가 기본값이어야 바꿀지 말지를 판단할 수 있다.</summary>
    private static (double H, double N, double W) DefaultsFor(bool up, int bench)
    {
        var p = GradingSettings.ToParams();
        return (p.BenchHeightAt(up, bench), BaseSlopeOf(p, up), p.BenchWidthOf(up));
    }

    /// <summary>취소 시 일반(무태그) 옹벽선 복원용 좌표 — 진입 때 레이어를 비우므로 종료 때 되돌린다.</summary>
    private static System.Collections.Generic.List<System.Collections.Generic.List<Point3>>? _restoreLines;

    public static void Run(Document doc, bool wallMode)
    {
        string cmdLabel = wallMode ? "옹벽 변환" : "사면 변환";
        Editor ed = doc.Editor;
        Database db = doc.Database;
        string app = GradingSettings.WallPickAppName;

        GradingBundle? region = null;
        string activePlan = GradingSettings.LastPlanHandle;
        var madeIds = new System.Collections.Generic.List<ObjectId>();
        var info = new System.Collections.Generic.Dictionary<ObjectId, (bool up, int bench, int gid)>();
        var groups = new System.Collections.Generic.Dictionary<(bool up, int gid, int bench),
            System.Collections.Generic.List<ObjectId>>();
        var lineArc = new System.Collections.Generic.Dictionary<(bool up, int gid, int bench), (double T0, double T1)>();
        // ★[JACK 0820] 클릭한 선의 **실제 2D 길이**도 같이 들고 있는다 — 선은 긴데 구간이 0이면
        //   '경계 투영이 무너졌다'는 뜻이라, 이 둘을 나란히 봐야 원인이 갈린다.
        var lineLen = new System.Collections.Generic.Dictionary<(bool up, int gid, int bench), (double Len, int N)>();
        var wholeLoop = new System.Collections.Generic.HashSet<(bool up, int gid, int bench)>();
        // ★★★[JACK 0824 "단마다 해당 단의 가상 계획폴리곤을 기억하고 그걸로 시작한다"]
        //   그 단의 링(닫힌 폴리곤) = 이 단 구간을 재는 **자**. 계획 폴리곤은 너무 작아
        //   바깥 단 조각이 코너 한 점으로 뭉개진다(0820 실측: 선 3m → 구간 0.000000m).
        var benchRing = new System.Collections.Generic.Dictionary<(bool up, int bench),
            System.Collections.Generic.List<Point3>>();
        var lineRef = new System.Collections.Generic.Dictionary<(bool up, int gid, int bench),
            System.Collections.Generic.List<Point3>>();
        // ★[JACK 0824] 클릭한 선의 한가운데 — '이 자리 지금 값이 뭐냐'를 되묻는 데 쓴다.
        var lineMid = new System.Collections.Generic.Dictionary<(bool up, int gid, int bench), Point3>();
        (bool up, int gid, int bench)? pick = null;   // [1회 1개] 선택은 항상 최대 하나
        bool finishedByEnter = false;
        bool clearAll = false;
        int gidSeq = 0;

        try
        {
            System.Collections.Generic.List<Point3>? boundary = null;
            double[]? cumB = null;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var regs = GradingBundleStore.TryLoadAll(db, tr, out string reason);
                if (regs == null || regs.Count == 0)
                {
                    Refuse(ed, cmdLabel, $"{cmdLabel}을(를) 실행할 수 없습니다.\n{reason}\n\n[정지면 생성](DHGRADE)을 먼저 실행하세요.");
                    tr.Commit();
                    return;
                }
                region = regs[^1];
                if (string.IsNullOrEmpty(activePlan)) activePlan = region.PlanHandle;
                if (!region.CutHasSlope && !region.FillHasSlope)
                {
                    Refuse(ed, cmdLabel, "이 구역에 절토·성토 사면이 없습니다.\n[정지면 생성](DHGRADE)을 먼저 실행하세요.");
                    tr.Commit();
                    return;
                }
                boundary = region.Boundary;
                cumB = GradingGeometry.CumLen2D(boundary);

                // 클릭 대상 = 각 단의 '시작선'(절토=소단선·성토=사면선). 옹벽 구간이든 사면 구간이든 전부 대상.
                //   (옹벽도 사면도 같은 규칙 하나로 표현되므로 두 명령이 같은 선을 쓴다.)
                var ng = new NullGround();
                var cutEdges = new System.Collections.Generic.List<(bool, int, int, System.Collections.Generic.List<Point3>)>();
                var fillEdges = new System.Collections.Generic.List<(bool, int, int, System.Collections.Generic.List<Point3>)>();
                static System.Collections.Generic.List<System.Collections.Generic.List<Point3>>? RingsOf(
                    System.Collections.Generic.List<System.Collections.Generic.List<Point3>>? many,
                    System.Collections.Generic.List<Point3>? one)
                    => many ?? (one != null ? new() { one } : null);
                foreach (var (up, hasSlope, ringList, zones, target) in new[]
                {
                    (true, region.CutHasSlope, RingsOf(region.CutFinalRings, region.CutFinalRing),
                     region.CutWallZones, cutEdges),
                    (false, region.FillHasSlope, RingsOf(region.FillFinalRings, region.FillFinalRing),
                     region.FillWallZones, fillEdges),
                })
                {
                    if (!hasSlope || ringList == null) continue;
                    double bs = BaseSlopeOf(region.Params, up), ms = region.Params.MinSlope;
                    var vs = GradingGeometry.Build(region.Boundary, ng, region.Params, up, zones);
                    if (!vs.HasSlope) continue;
                    // ★[JACK 0824] 단마다 **클릭 대상 선이 놓인 링**을 자로 삼는다.
                    //   GenerateEdgeLinesTagged와 같은 짝짓기(2k, 2k+1)·같은 고르기(절토=아랫선/성토=윗선)여야
                    //   클릭한 선과 자가 어긋나지 않는다.
                    static double AvgZOf(System.Collections.Generic.List<Point3> r)
                    {
                        double t = 0; foreach (var q in r) t += q.Z; return r.Count > 0 ? t / r.Count : 0;
                    }
                    for (int k = 0; 2 * k + 1 < vs.Rings.Count; k++)
                    {
                        var rA = vs.Rings[2 * k]; var rB = vs.Rings[2 * k + 1];
                        if (rA.Count < 3 || rB.Count < 3) continue;
                        bool aHigher = AvgZOf(rA) >= AvgZOf(rB);
                        var crest = aHigher ? rA : rB;
                        var toe = aHigher ? rB : rA;
                        benchRing[(up, k)] = up ? toe : crest;
                    }
                    foreach (var fr in ringList)
                    {
                        if (fr == null || fr.Count < 3) continue;
                        var plain = SlopeHatchGenerator.GenerateEdgeLinesTagged(vs.Rings, ng, up, fr,
                            region.Boundary, zones, region.Boundary, null, target, bs, ms);
                        foreach (var e in plain)
                            if (up != e.IsSlope) target.Add((e.IsSlope, e.Bench, e.Seg, e.Pts));
                    }
                }
                GradingBuilder.DrawWallLines(db, tr, System.Array.Empty<System.Collections.Generic.List<Point3>>());
                madeIds = GradingBuilder.DrawWallLinesTagged(db, tr, cutEdges, fillEdges, activePlan);
                _restoreLines = new System.Collections.Generic.List<System.Collections.Generic.List<Point3>>();
                foreach (var (_, _, _, pts) in cutEdges) _restoreLines.Add(pts);
                foreach (var (_, _, _, pts) in fillEdges) _restoreLines.Add(pts);

                double total = cumB[cumB.Length - 1];
                foreach (var id in madeIds)
                {
                    if (!TryReadPick(tr, id, app, out var pki)) continue;
                    var pts = new System.Collections.Generic.List<Point3>();
                    if (tr.GetObject(id, OpenMode.ForRead) is Polyline3d p3)
                        foreach (ObjectId vId in p3)
                            if (tr.GetObject(vId, OpenMode.ForRead) is PolylineVertex3d pv)
                                pts.Add(new Point3(pv.Position.X, pv.Position.Y, pv.Position.Z));
                    int gid = gidSeq++;
                    info[id] = (pki.up, pki.bench, gid);
                    var key = (pki.up, gid, pki.bench);
                    if (!groups.TryGetValue(key, out var g)) groups[key] = g = new();
                    g.Add(id);

                    // [리뷰 0803] 부지를 한 바퀴 도는 '닫힌 고리'는 최대간극 방식이 둘레 전체 비슷한 값을 준다 →
                    //   둘레 전체로 명시하고 안내한다(그 간극이 남아 엉뚱한 조각이 생기는 것도 막는다).
                    double len2d = 0;
                    for (int q = 1; q < pts.Count; q++)
                    {
                        double dx = pts[q].X - pts[q - 1].X, dy = pts[q].Y - pts[q - 1].Y;
                        len2d += System.Math.Sqrt(dx * dx + dy * dy);
                    }
                    lineLen[key] = (len2d, pts.Count);
                    if (pts.Count > 0) lineMid[key] = pts[pts.Count / 2];

                    bool closed = pts.Count >= 3
                        && System.Math.Abs(pts[0].X - pts[pts.Count - 1].X) < 0.05
                        && System.Math.Abs(pts[0].Y - pts[pts.Count - 1].Y) < 0.05;

                    // ★★★[JACK 0824] 이 선이 놓인 **그 단의 링**을 자로 쓴다. 링이 없으면 옛 방식(계획 폴리곤).
                    //   자를 바꾸면 34m 조각은 그 링 위에서 34m다 — 0으로 무너질 수가 없다.
                    var ruler = benchRing.TryGetValue((pki.up, pki.bench), out var br) && br.Count >= 3 ? br : null;
                    var rulerCum = ruler != null ? GradingGeometry.CumLen2D(ruler) : cumB;
                    var rulerPoly = ruler ?? boundary;
                    if (ruler != null) lineRef[key] = ruler;
                    double rulerTot = rulerCum[rulerCum.Length - 1];

                    if (closed) { lineArc[key] = (0.0, rulerTot); wholeLoop.Add(key); }
                    else
                    {
                        var iv = GradingGeometry.PickInterval(pts, rulerPoly, rulerCum);
                        if (iv != null) lineArc[key] = (iv.Value.T0, iv.Value.T1);
                    }
                }

                GradingBuilder.SetLayersColor(db, tr, new[] { "DH-옹벽선" }, GradingBuilder.EdgePickAci); // 시안 강조
                tr.Commit();
            }
            if (madeIds.Count == 0)
            {
                RestoreAndCleanup(db, madeIds);
                Refuse(ed, cmdLabel, "선택할 계단선을 만들지 못했습니다 — [정지면 생성]을 다시 실행한 뒤 시도하세요.");
                return;
            }
            Log($"■ {cmdLabel} 시작 {System.DateTime.Now:HH:mm:ss} — 대상선 {madeIds.Count}개");
            // [JACK 0804] 멘트 간결화 — 안내는 한 줄로.
            ed.WriteMessage($"\n[{cmdLabel}] 계단선을 클릭하고 Enter. 제원은 " +
                            (wallMode ? "단높이(H)·소단길이(T)" : "단높이(H)·사면구배(R)·소단길이(T)") +
                            " 키를 눌러 바꿉니다. 전체해제(C). (Esc=취소)");

            PickGuard.Enter(doc, "DH-옹벽선");

            // ★★[JACK 0820 'fillet처럼 옵션(O)를 하나 만들고'] 제원은 옵션에서 정하고, Enter는 적용만 한다.
            //   종전엔 Enter 뒤에 제원을 <b>순서대로 물었다</b> — 무엇을 고르고 있는지 보이지 않는 상태에서
            //   숫자를 세 번 받아야 했다. 옵션으로 빼면 <b>현재 값이 프롬프트에 늘 보이고</b>
            //   바꿀 것만 바꾸면 된다(AutoCAD 명령들의 방식).
            var d0 = DefaultsFor(true, 0);
            double optH = d0.H, optW = d0.W, optN = d0.N;
            bool setH = false, setN = false, setW = false;   // 사용자가 손댄 항목만 지킨다

            while (true)
            {
                // ★★[JACK 0820 실측 '*유효하지 않은 선택*'] **프롬프트 문구에 대괄호를 쓰면 안 된다.**
                //   AutoCAD는 문구 안의 <c>[...]</c>를 <b>자기 키워드 목록으로 읽는다</b> —
                //   상태 표시에 대괄호를 쓰면 진짜 키워드 목록을 덮어써서 H를 쳐도 안 먹는다
                //   (실측: "점을 예상하거나 또는 최종(L)/선택: 성토 1단]…"로 파싱이 깨졌다).
                //   → 상태는 〈 〉로 감싼다. 대괄호는 AutoCAD가 키워드를 붙일 자리로 비워 둔다.
                string cur = pick == null ? ""
                    : $" 〈선택 {(pick.Value.up ? "절토" : "성토")} {pick.Value.bench + 1}단〉";
                string spec = wallMode
                    ? $"〈단높이 {optH:0.##}m · 소단 {optW:0.##}m · 수직〉"
                    : $"〈단높이 {optH:0.##}m · 구배 1:{optN:0.##} · 소단 {optW:0.##}m〉";
                // ★★[JACK 0820] 제원은 **프롬프트에 바로 걸린 키워드**로 바꾼다(AutoCAD 명령들의 방식) —
                //   옹벽: 단높이(H) · 소단길이(T)   /   사면: 단높이(H) · 사면구배(R) · 소단길이(T)
                //   globalName을 'H'·'R'·'T'로 두어 그 글자만 쳐도 먹는다(StringResult가 globalName을 준다).
                var peo = new PromptEntityOptions($"\n계단선 클릭{cur} {spec} (Enter=적용)");
                peo.AllowNone = true;
                //   ※★[0820 실측] AutoCAD는 입력을 <b>localName의 앞글자</b>와 맞춘다.
                //     "단높이(H)"처럼 H가 <b>맨 뒤</b>면 앞글자가 아니라 'H'를 쳐도 "유효하지 않은 선택"이 된다.
                //     → <b>매칭용 이름(localName)은 글자 하나</b>로 두고, 한글은 <b>표시용(displayName)</b>에만 쓴다.
                //     세 인자가 각각 다른 일을 한다: global=코드가 받는 값 · local=사용자가 치는 값 · display=화면.
                peo.Keywords.Add("H", "H", "단높이(H)");
                if (!wallMode) peo.Keywords.Add("R", "R", "사면구배(R)");
                peo.Keywords.Add("T", "T", "소단길이(T)");
                peo.Keywords.Add("C", "C", "전체해제(C)");
                var per = ed.GetEntity(peo);
                if (per.Status == PromptStatus.None) { finishedByEnter = true; break; }
                if (per.Status == PromptStatus.Cancel) break;
                if (per.Status == PromptStatus.Keyword)
                {
                    // 값 하나만 바꾸고 곧바로 선택 프롬프트로 돌아온다 — 취소해도 현재 값은 그대로 둔다.
                    // ★[JACK 0820 '대문자로 표기되었지만 대문자나 소문자 다 먹어야 돼'] 대소문자를 안 가린다.
                    //   AutoCAD 자체는 원래 안 가리지만, 비교를 대문자로 못 박아 두면 그 보장이 여기서 끊긴다.
                    string kw = (per.StringResult ?? "").Trim().ToUpperInvariant();
                    if (kw == "H")
                    {
                        var h2 = AskPositive(ed, "단높이 (m)", optH, 0.2, 15.0);
                        if (h2 != null) { optH = h2.Value; setH = true; ed.WriteMessage($"\n → 단높이 {optH:0.##}m"); }
                        continue;
                    }
                    if (kw == "R")
                    {
                        var n2 = AskPositive(ed, "사면 구배 1:n", optN, GradingSettings.MinSlope, 30.0);
                        if (n2 != null) { optN = n2.Value; setN = true; ed.WriteMessage($"\n → 사면 구배 1:{optN:0.##}"); }
                        continue;
                    }
                    if (kw == "T")
                    {
                        var w2 = AskPositive(ed, "소단 길이 (m)", optW, 0.0, 60.0);
                        if (w2 != null) { optW = w2.Value; setW = true; ed.WriteMessage($"\n → 소단 길이 {optW:0.##}m"); }
                        continue;
                    }
                    if (kw != "C") continue;                 // 모르는 키워드는 무시(프롬프트 유지)
                    clearAll = true; finishedByEnter = true;
                    ed.WriteMessage("\n → 전체 해제 — 순수 사면으로 재생성합니다.");
                    break;
                }
                if (per.Status != PromptStatus.OK) continue;

                using var tr = db.TransactionManager.StartTransaction();
                if (!info.TryGetValue(per.ObjectId, out var pk))
                {
                    var alt = PickGuard.SnapToLayerLine(ed, tr, per.PickedPoint, "DH-옹벽선");
                    if (alt.IsNull || !info.TryGetValue(alt, out pk))
                    {
                        ed.WriteMessage("\n → 근처에 대상 선이 없습니다 — 시안색 선 근처를 클릭하세요.");
                        tr.Commit();
                        continue;
                    }
                }
                var key = (pk.up, pk.gid, pk.bench);
                if (!lineArc.ContainsKey(key))
                {
                    ed.WriteMessage("\n → 이 선은 선택할 수 없습니다 — 다른 선을 클릭하세요.");
                    tr.Commit();
                    continue;
                }
                void ColorGroup((bool up, int gid, int bench) k2, bool on)
                {
                    if (!groups.TryGetValue(k2, out var g2)) return;
                    foreach (var gid in g2) { if (on) SetColor(tr, gid, SelAci); else SetColorByLayer(tr, gid); }
                }
                // [1회 1개 — JACK] 연달아 누르면 이전 선택은 해제하고 마지막 것만 남긴다.
                if (pick != null && !pick.Value.Equals(key)) ColorGroup(pick.Value, false);
                if (pick != null && pick.Value.Equals(key))
                {
                    pick = null; ColorGroup(key, false);
                    ed.WriteMessage("\n → 선택 해제.");
                }
                else
                {
                    pick = key; ColorGroup(key, true);
                    // ★[JACK 0820] 안 손댄 항목은 **그 방향·그 단의 현재 값**으로 갱신한다 —
                    //   절토·성토는 제원이 따로라(v16.6), 절토 값을 보여 주다 성토 선을 고르면 엉뚱한 값이 기본이 된다.
                    var dk = DefaultsFor(pk.up, pk.bench);
                    if (!setH) optH = dk.H;
                    if (!setN) optN = dk.N;
                    if (!setW) optW = dk.W;
                    ed.WriteMessage($"\n → {(pk.up ? "절토" : "성토")} {pk.bench + 1}단 선택");
                    if (wholeLoop.Contains(key))
                        ed.WriteMessage(" (한 바퀴 고리 — 둘레 전체 적용)");
                }
                tr.Commit();
            }

            // ── 제원 = 옵션(O)에서 정해 둔 값. Enter는 적용만 한다(JACK 0820). ──
            //   옹벽은 구배를 묻지 않는다 — 수직(최소구배) 고정이다.
            double? askW = null, askN = null, askH = null;
            if (finishedByEnter && !clearAll && pick != null)
            {
                askN = wallMode ? GradingSettings.MinSlope : optN;
                askW = optW;
                askH = optH;
            }

            RestoreAndCleanup(db, madeIds);
            Log($"■ {cmdLabel} 종료({(finishedByEnter ? "Enter" : "Esc")}) — 선택 {(pick == null ? "없음" : "1건")}");

            if (!finishedByEnter || (pick == null && !clearAll))
            {
                ed.WriteMessage(finishedByEnter ? $"\n[{cmdLabel}] 선택 없음 — 변경 없이 종료." : $"\n[{cmdLabel}] 취소.");
                return;
            }

            // ★[JACK 0824] 단높이는 아래 루프가 정지옵션을 고치므로 **고치기 전에** 지금 값을 떠 둔다 —
            //   고친 뒤에 읽으면 언제나 '같다'가 나와 '안 바뀐다' 경고가 늘 뜬다.
            double beforeH = pick == null ? 0
                : GradingSettings.ToParams().BenchHeightAt(pick.Value.up, pick.Value.bench);

            // ── 적용: 기존 구간 + 이번 규칙 하나 ──
            var newCut = new System.Collections.Generic.List<SlopeZone>();
            var newFill = new System.Collections.Generic.List<SlopeZone>();
            if (!clearAll)
            {
                foreach (var (up, src, target) in new[]
                {
                    (true, region!.CutWallZones, newCut),
                    (false, region.FillWallZones, newFill),
                })
                {
                    if (src != null)
                        foreach (var z in src)
                            target.Add(new SlopeZone { T0 = z.T0, T1 = z.T1, Rules = new(z.Rules), Ref = z.Ref });
                    if (pick!.Value.up != up) continue;
                    var a = lineArc[pick.Value];
                    // ★[JACK 0824] 이 구간이 어느 자로 잰 값인지 함께 들려 보낸다 — 안 붙이면 재생성이
                    //   계획 폴리곤으로 되읽어 엉뚱한 자리가 된다.
                    var nz = new SlopeZone
                    {
                        T0 = a.T0, T1 = a.T1,
                        Ref = lineRef.TryGetValue(pick.Value, out var pr) ? pr : null,
                    };
                    nz.Rules.Add((pick.Value.bench, askN!.Value, askW!.Value));
                    target.Add(nz);
                    // ★★★[JACK 0820] **단높이는 구간이 아니라 방향 전체에 쌓는다.**
                    //   구배·소단폭은 클릭한 선의 호길이 범위 안에만 적용되지만(위 Flatten),
                    //   단높이는 그러면 안 된다 — 둘레의 일부만 단높이가 다르면 같은 링에 표고가 둘이 되어
                    //   링을 이어 붙일 수 없다(v16.9가 '구간별 불가'라고 한 그 이유).
                    //   층 전체를 바꾸면 링마다 표고는 여전히 하나라 안전하다.
                    // ★★[JACK 0820 '정지옵션과 변환은 연동되긴 해야 해'] 규칙은 <b>정지옵션</b>에 쌓는다 —
                    //   재생성이 정지옵션을 읽으므로 여기 넣어야 먹고, 다음 변환의 기본값도 이 값이 된다.
                    var steps = up ? GradingSettings.CutBenchSteps : GradingSettings.FillBenchSteps;
                    steps.Add((pick.Value.bench, askH!.Value));
                    var norm = GradingSettings.ToParams(); norm.NormalizeBenchSteps();
                    GradingSettings.CutBenchSteps = new System.Collections.Generic.List<(int, double)>(norm.CutBenchSteps);
                    GradingSettings.FillBenchSteps = new System.Collections.Generic.List<(int, double)>(norm.FillBenchSteps);
                    // [스샷 버그 0804] 겹침은 합치지 않고 조각으로 가른다 — 새 규칙은 클릭한 선의 범위 '안'에만 남는다.
                    SlopeZone.Flatten(target, cumB![cumB.Length - 1]);
                    // ★[JACK 0824] 뒤 규칙에 덮여 아무 일도 안 하는 구간은 뺀다 —
                    //   안 빼면 변환할 때마다 쌓여 번들이 커지고 로그를 읽을 수 없다(실측: 4개 중 3개가 죽어 있었다).
                    SlopeZone.Compact(target);
                }
            }

            var planId = NoriCommand.FindByHandle(db, activePlan);
            var groundId = NoriCommand.FindByHandle(db, GradingSettings.LastGroundHandle);
            if (groundId.IsNull) groundId = NoriCommand.FindByHandle(db, region!.GroundHandle);
            if (planId.IsNull || groundId.IsNull)
            {
                AcadApp.ShowAlertDialog("정지면을 재생성하려면 [정지면 생성](DHGRADE)을 먼저 한 번 실행해야 합니다.");
                return;
            }

            // [리뷰 0803 — 치명] 재생성은 세션 설정을 읽는다. 재시작 후엔 기본값이라 '구간만 바꿔 다시 만들기'가
            //   전혀 다른 파라미터로 새로 만들기가 된다 → 이 구역의 저장값을 기준선으로 복원한 뒤 재생성.
            // ★★★[JACK 0820 '정지옵션에서 바꾸고 변환에서 바꿔도 정지옵션이 무조건 처음 설정값 5로 됐다']
            //   **세션 설정을 되돌리지 않는다.** 종전엔 여기서 <c>RestoreFrom(구역.Params)</c>로 덮었다 —
            //   0803이 막으려던 것은 "Civil3D를 껐다 켠 뒤 기본값으로 새로 만들어지는 것"인데,
            //   그건 이미 <c>SyncToDocument</c>가 <b>도면이 바뀔 때</b> 번들 값으로 복원해 막고 있다.
            //   여기서 또 덮으면 <b>사용자가 방금 정지옵션에서 바꾼 값이 매번 지워진다</b>(JACK 실측: 5로 되돌아감).
            //   → 정지옵션과 변환은 <b>연동</b>이다: 변환은 정지옵션 값을 기본값으로 쓰고, 바꾼 값을 거기에 쌓는다.
            GradingSettings.ZoneOverride = (newCut, newFill);
            // ★★[JACK 0824 '마지막 단을 선택하고 사면변환을 했지만 변하지 않았어'] **안 바뀌면 안 바뀐다고 말한다.**
            //   변환 기본값은 '그 단에 지금 적용 중인 값'이라, Enter만 치면 넣은 값이 지금 값과 같아
            //   아무 일도 안 일어난다 — 그런데 화면엔 '적용' 이라고만 떠서 고장으로 보인다(0824 실측:
            //   6단에 1:1.5를 넣었는데 이미 1단부터 1:1.5였다). 셋 다 같으면 그 자리에서 알린다.
            if (!clearAll && pick != null && lineMid.TryGetValue(pick.Value, out var pmid) && boundary != null)
            {
                bool pu = pick.Value.up;
                var oldZones = pu ? region!.CutWallZones : region!.FillWallZones;
                var pOld = region.Params;
                var (curS, curW) = SlopeZone.ResolveAt(oldZones, pmid.X, pmid.Y, pick.Value.bench,
                    BaseSlopeOf(pOld, pu), pOld.BenchWidthOf(pu), boundary, cumB!);
                double curH = beforeH;
                bool sameS = System.Math.Abs(curS - askN!.Value) < 1e-9;
                bool sameW = System.Math.Abs(curW - askW!.Value) < 1e-9;
                bool sameH = System.Math.Abs(curH - askH!.Value) < 1e-9;
                if (sameS && sameW && sameH)
                {
                    string msg = $"이 자리는 이미 {(wallMode ? "수직" : $"1:{curS:0.###}")} · 소단 {curW:0.##}m · 단높이 {curH:0.##}m 입니다 " +
                                 "— 넣은 값이 지금 값과 같아 **모양이 안 바뀝니다.**";
                    ed.WriteMessage($"\n[{cmdLabel}] ⚠ {msg}");
                    ed.WriteMessage($"\n   바꾸려면 {(wallMode ? "단높이(H)·소단길이(T)" : "단높이(H)·사면구배(R)·소단길이(T)")}로 값을 먼저 바꾸세요.");
                    Log($"■ {cmdLabel} ⚠ 값이 지금과 같다 — {(pu ? "절토" : "성토")} {pick.Value.bench + 1}단 " +
                        $"현재 1:{curS:0.###}·소단{curW:0.##}m·단높이{curH:0.##}m / 넣은 값 1:{askN:0.###}·소단{askW:0.##}m·단높이{askH:0.##}m");
                }
            }
            string what = clearAll ? "전체 해제"
                : $"{(pick!.Value.up ? "절토" : "성토")} {pick.Value.bench + 1}단부터 " +
                  (wallMode ? $"수직 옹벽 · 소단 {askW:0.##}m"
                            : $"경사 1:{askN:0.###} · 소단 {askW:0.##}m")
                  // ★[JACK 0820 '단높이가 바꿔도 안 바껴'] **적용한 단높이를 눈에 보이게 적는다.**
                  //   종전 메시지엔 단높이가 없어, 값이 안 들어간 건지 들어갔는데 안 먹는 건지 못 갈랐다.
                  + $" · 단높이 {askH:0.##}m";
            ed.WriteMessage($"\n[{cmdLabel}] {what} 적용 — 정지면 재생성 중…");
            // ★[JACK 0820] 단높이 규칙이 실제로 쌓혔는지 · 재생성이 그 값을 받는지 숫자로 남긴다.
            static string StepsTxt(System.Collections.Generic.IReadOnlyList<(int FromBench, double H)> l)
                => l.Count == 0 ? "없음" : string.Join(" ", l.Select(r => $"{r.FromBench + 1}단~{r.H:0.##}m"));
            Log($"■ {cmdLabel} 적용 — {what} · 절토구간 {newCut.Count} · 성토구간 {newFill.Count}");
                        Log($"   단높이 규칙 — 정지옵션(재생성이 읽는 값): 절토[{StepsTxt(GradingSettings.CutBenchSteps)}] 성토[{StepsTxt(GradingSettings.FillBenchSteps)}]" +
                $" · 전역 단높이 절토 {GradingSettings.CutBenchHeight:0.##}m 성토 {GradingSettings.FillBenchHeight:0.##}m");
            // ★★[JACK 0820 '중간에서 하면 잘 변환되는데 사면 맨 아랫단은 안 바뀌네'] **클릭한 선이 어느 구간으로 잡혔는가.**
            //   기하 엔진은 양방향 맨 아랫단이 모두 정상임을 하니스로 확인했다(S43·S45 — 링도 좁아지고
            //   옹벽선도 그 단에 선다). 그러면 남는 자리는 '클릭한 선 → 호길이 구간' 변환뿐이다:
            //   바깥 단일수록 링이 경계에서 멀어(성토 맨 아랫단 실측 43m) 그 점들을 경계에 투영하면
            //   코너에 뭉쳐 **구간이 실제 선보다 훨씬 좁게 잡힐 수 있다**. 그러면 옹벽은 그 좁은 자리에만
            //   서고 눈에는 '안 바뀐다'로 보인다. 추측 대신 숫자로 가른다.
            if (!clearAll && pick != null && cumB != null)
            {
                // ★[0824] 둘레는 **그 구간의 자** 기준이다 — 계획 폴리곤 둘레로 적으면 %가 엉뚱해진다.
                var pRef = lineRef.TryGetValue(pick.Value, out var pr2) ? pr2 : null;
                double tot = pRef != null
                    ? GradingGeometry.CumLen2D(pRef)[^1]
                    : cumB[cumB.Length - 1];
                var pa = lineArc[pick.Value];
                double segLen = pa.T1 >= pa.T0 ? pa.T1 - pa.T0 : pa.T1 + tot - pa.T0;
                Log($"   클릭한 선 — {(pick.Value.up ? "절토" : "성토")} {pick.Value.bench + 1}단 · " +
                    $"호길이 [{pa.T0:F1}..{pa.T1:F1}] = {segLen:F1}m / 둘레 {tot:F1}m " +
                    $"({segLen / System.Math.Max(tot, 1e-9) * 100:F0}%)" +
                    (pRef != null ? $" · 자=그 단의 링({pRef.Count}점)" : " · 자=계획 폴리곤(옛 방식)") +
                    (wholeLoop.Contains(pick.Value) ? " · 닫힌 고리(둘레 전체)" : "") +
                    (lineLen.TryGetValue(pick.Value, out var ll)
                        ? $" · 선 길이 {ll.Len:F1}m({ll.N}점)" +
                          (segLen < 0.5 && ll.Len > 2.0 ? "  ⚠자가 무너졌다 — 이런 줄이 보이면 알려 주세요" : "")
                        : ""));
                var zs = pick.Value.up ? newCut : newFill;
                for (int zi = 0; zi < zs.Count; zi++)
                {
                    var z = zs[zi];
                    string rt = z.Rules.Count == 0 ? "없음" : string.Join(" ", z.Rules.Select(r =>
                        $"{r.FromBench + 1}단~1:{r.Slope:0.###}" +
                        (r.Slope <= GradingSettings.MinSlope + 1e-9 ? "(수직)" : "")));
                    // ★[0824] 길이는 **그 구간의 자**로 잰다 — 클릭한 선의 자로 재면 자가 다른 구간이 엉뚱하게 찍힌다.
                    double zTot = z.RefCum != null ? z.RefCum[^1] : cumB[cumB.Length - 1];
                    double zl = z.T1 >= z.T0 ? z.T1 - z.T0 : z.T1 + zTot - z.T0;
                    Log($"   구간#{zi + 1} [{z.T0:F1}..{z.T1:F1}] {zl:F1}m/{zTot:F0}m — {rt}" +
                        (z.Ref != null ? $" · 자=링({z.Ref.Count}점)" : " · 자=계획"));
                }
            }
            CreateGradingCommand.DoGrade(doc, planId, groundId, GradeMode.RerunLast);
        }
        catch (System.Exception ex)
        {
            RestoreAndCleanup(db, madeIds);
            ed.WriteMessage($"\n[{cmdLabel} 오류] " + ex.Message);
            Log($"■ {cmdLabel} 예외 — " + ex.GetType().Name + ": " + ex.Message + "\n" + ex.StackTrace);
        }
        finally
        {
            PickGuard.Exit();
        }
    }

    /// <summary>그 방향의 전역(원래) 구배 — 최소구배 하한 적용.</summary>
    private static double BaseSlopeOf(GradingParams p, bool up)
        => System.Math.Max(up ? p.CutSlope : p.FillSlope, p.MinSlope);

    /// <summary>숫자 하나 입력 — 기본값은 현재 값(프롬프트의 &lt;&gt;는 AutoCAD가 자동 표시).
    /// 범위 밖이면 다시 묻는다. Esc/취소면 null.</summary>
    private static double? AskPositive(Editor ed, string label, double dflt, double min, double max)
    {
        while (true)
        {
            var pdo = new PromptDoubleOptions($"\n{label}")
            {
                DefaultValue = dflt,
                UseDefaultValue = true,
                AllowNegative = false,
                AllowZero = min <= 0,
                AllowNone = false,
            };
            var r = ed.GetDouble(pdo);
            if (r.Status != PromptStatus.OK) return null;
            if (r.Value < min - 1e-9 || r.Value > max + 1e-9)
            {
                ed.WriteMessage($"\n → {min:0.##}~{max:0.##} 사이 값을 넣어주세요.");
                continue;
            }
            return r.Value;
        }
    }

    private static void RestoreAndCleanup(Database db, System.Collections.Generic.List<ObjectId> madeIds)
    {
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            foreach (var id in madeIds)
            {
                try { if (!id.IsErased && tr.GetObject(id, OpenMode.ForWrite) is Entity e) e.Erase(); } catch { }
            }
            if (_restoreLines != null && _restoreLines.Count > 0)
                GradingBuilder.DrawWallLines(db, tr, _restoreLines);
            GradingBuilder.SetLayersColor(db, tr, new[] { "DH-옹벽선" }, 1);
            tr.Commit();
            _restoreLines = null;
        }
        catch { }
    }

    private static void Refuse(Editor ed, string label, string msg)
    {
        ed.WriteMessage($"\n[{label}] " + msg.Replace("\n", " "));
        AcadApp.ShowAlertDialog(msg);
    }

    private static void Log(string line)
    {
        // ★[JACK 0820] Carry로 남긴다 — 이 줄들을 쓴 직후 DoGrade가 로그를 새로 쓰기 때문에
        //   그냥 Append하면 **재생성 머리말과 함께 지워진다**(0820 실측).
        try { DiagLog.AppendCarry("\n" + line); } catch { }
    }

    private static bool TryReadPick(Transaction tr, ObjectId id, string app,
        out (bool up, bool isSlope, int bench, int seg, string plan) pk)
    {
        pk = default;
        if (id.IsErased) return false;
        if (tr.GetObject(id, OpenMode.ForRead) is not Entity ent) return false;
        var rb = ent.GetXDataForApplication(app);
        if (rb == null) return false;
        var v = rb.AsArray();
        if (v.Length < 5) return false;
        string plan = "";
        if (v.Length >= 6) { try { plan = v[5].Value as string ?? ""; } catch { } }
        pk = (System.Convert.ToInt32(v[1].Value) != 0, System.Convert.ToInt32(v[2].Value) != 0,
              System.Convert.ToInt32(v[3].Value), System.Convert.ToInt32(v[4].Value), plan);
        return true;
    }

    private static void SetColor(Transaction tr, ObjectId id, short aci)
    {
        var ent = (Entity)tr.GetObject(id, OpenMode.ForWrite);
        ent.Color = Color.FromColorIndex(ColorMethod.ByAci, aci);
    }

    private static void SetColorByLayer(Transaction tr, ObjectId id)
    {
        var ent = (Entity)tr.GetObject(id, OpenMode.ForWrite);
        ent.Color = Color.FromColorIndex(ColorMethod.ByLayer, 256);
    }
}
