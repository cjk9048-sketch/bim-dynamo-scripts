using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Civil.DatabaseServices;
using DH.Grading.Core;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace DH.Grading.Civil.Commands;

/// <summary>
/// ★★★[JACK 0824] <b>터파기 지표면 생성(DHEXCAV)</b> — 배수지·정수장 같은 <b>지하구조물</b>의 터파기.
///
/// <para>JACK: <i>"이 애드인은 관로용이 아니라 부지정지용이야. 상하수도는 지하구조물이 많아서
/// 구조물 터파기를 해야 해. 그냥 지금처럼 구조물 바닥계획고가 들어간 폴리선을 사용자가 만들고
/// 똑같이 구배만 줘서 원지반에 정지하는 거야."</i></para>
///
/// <para><b>정지면 생성과 기능이 같다.</b> 다른 것은 두 가지뿐이다:</para>
/// <list type="number">
///   <item><description><b>목표면이 '두 면 중 낮은 쪽'</b> — <see cref="LowerOfSurfaces"/>.
///     절토부는 이미 깎아 놓은 계획면에서 파 내려가고, 성토부는 굳이 다 성토해 놓고 다시 파지 않으므로
///     원지반에서 판다(JACK — 시공 순서). 그 규칙이 곧 '낮은 쪽'이다.</description></item>
///   <item><description><b>결과가 굴착 형상만</b>(바닥 + 법면) — JACK: <i>"터파기는 원지반이나
///     계획지표면까지는 필요 없어. 종단에 투영될 때 순수하게 터파기선만 나오면 돼."</i>
///     그래서 부지 전체를 덮는 합성면을 만들지 않는다. 종단에서도 구조물 위에만 선이 나온다.</description></item>
/// </list>
///
/// <para><b>목표면을 진짜 지표면으로 만드는 방법.</b> 데이라잇은 두 TIN의 교선으로 따므로
/// 목표면도 실물이어야 한다. 그런데 <c>min(계획, 원지반)</c>은 계산식이다 —
/// <b>절토부는 정의상 계획이 원지반보다 낮은 자리</b>이므로,
/// <c>원지반 + 절토부만 붙이기 = min</c>이다. 절토부는 번들에서 되살린다(노리선이 하는 방식).</para>
///
/// <para>제원(구배·단높이·소단)은 <b>정지옵션에 넣지 않는다</b> — JACK: <i>"초반에 세팅하는 게 많아
/// 보여서 너무 복잡해지게 느껴져."</i> 옹벽·사면 변환과 같은 <b>프롬프트 키워드</b>로 그 자리에서 받는다.</para>
/// </summary>
public sealed class ExcavCommand
{
    /// <summary>★[JACK 0824] 터파기 제원은 <b>구배 하나뿐</b>이다 — <i>"단높이 설정은 필요 없어,
    /// 어차피 구배로만 치는 거야."</i> 그래서 단높이·소단을 묻지 않고, 바닥에서 목표면까지
    /// <b>끊김 없는 한 장의 법면</b>으로 올린다(단높이 = 전체 굴착깊이, 소단 0).
    /// <para>세션 동안 기억한다 — 정지옵션과는 섞지 않는다(JACK: "초반에 세팅하는 게 많아 보여서").</para></summary>
    internal static double Slope = 0.5;

    /// <summary>[보기] 상태 — 마지막으로 무엇만 보이게 했는지(null=전부 보임).</summary>
    internal const string SurfName = "터파기면_DH";
    internal const string BaseName = "터파기기준면_DH";
    internal const string VirtName = "가상터파기_DH";

    /// <summary>명령 들어올 때의 메모리 자국 — 끝에서 견준다(JACK 0901).</summary>
    private static (long Alloc, long Live) _mem0;

    [CommandMethod("DHEXCAV")]
    public void Run()
    {
        _mem0 = StageTimer.Mem();
        Document doc = AcadApp.DocumentManager.MdiActiveDocument;
        if (doc == null) return;
        GradingSettings.SyncToDocument(doc);
        Editor ed = doc.Editor;
        Database db = doc.Database;

        ed.WriteMessage("\n[터파기 지표면] 구조물 바닥계획고가 들어간 닫힌 폴리선을 고릅니다. " +
                        "굴착 구배는 그 다음에 묻습니다(정지옵션과 별도 · 단높이는 안 씁니다).");

        // 이전 실행이 지표면을 숨겨 놨을 수 있다 — 원지반을 클릭해야 하므로 전부 복원.
        try
        {
            using var trV = db.TransactionManager.StartTransaction();
            GradingBuilder.IsolateSurfaces(trV, null);
            trV.Commit();
        }
        catch { }

        // ── 1) 구조물 바닥 폴리선 ──
        var peo = new PromptEntityOptions("\n구조물 바닥 경계(닫힌 폴리라인/3D폴리라인/피처라인)를 선택: ");
        peo.SetRejectMessage("\n폴리라인 또는 피처라인이어야 합니다.");
        peo.AddAllowedClass(typeof(Polyline), false);
        peo.AddAllowedClass(typeof(Polyline3d), false);
        peo.AddAllowedClass(typeof(FeatureLine), false);
        var rPoly = ed.GetEntity(peo);
        if (rPoly.Status != PromptStatus.OK) return;

        // ── 2) 원지반 ──
        ObjectId groundId = ObjectId.Null;
        try
        {
            using var trG = db.TransactionManager.StartTransaction();
            groundId = NoriCommand.FindByHandle(db, GradingSettings.LastGroundHandle);
            trG.Commit();
        }
        catch { }
        if (groundId.IsNull)
        {
            var peoS = new PromptEntityOptions("\n원지반 표면(TIN Surface)을 선택: ");
            peoS.SetRejectMessage("\nTIN Surface여야 합니다.");
            peoS.AddAllowedClass(typeof(TinSurface), true);
            var rS = ed.GetEntity(peoS);
            if (rS.Status != PromptStatus.OK) return;
            groundId = rS.ObjectId;
        }
        else ed.WriteMessage("\n원지반 = 마지막 정지에 쓴 지반(자동)");

        // ── 3) 제원 — 그 자리에서 키워드로 ──
        if (!AskSpec(ed)) { ed.WriteMessage("\n[터파기] 취소."); return; }

        // ★[JACK 0824] 만드는 동안은 계획지표면을 숨겨 헷갈리지 않게 한다 —
        //   **끝날 때 무조건 복원한다**(예외·Esc 포함). 그게 Focus가 IDisposable인 이유다.
        try
        {
            using (ViewSurfaceCommand.Focus(db, null))   // 만드는 동안엔 전부 보이게(원지반 클릭 필요)
                DoExcav(doc, rPoly.ObjectId, groundId);

            // ★★[JACK 0825] <b>끝나면 전부 보기로 돌아온다.</b>
            //   JACK: <i>"터파기 기능을 썼을 때 완료가 되면 전부 보기 상태로 복원되게 해줘."</i>
            //   종전엔 '터파기만'으로 남겨 뒀다 — 만든 것을 보여주려는 뜻이었지만,
            //   <b>만들자마자 나머지가 사라지면</b> 방금 한 일이 도면 어느 자리인지 알 수 없다.
            ViewSurfaceCommand.ShowAll();
        }
        catch (System.Exception ex)
        {
            ed.WriteMessage("\n[터파기 오류] " + ex.Message);
            AcadApp.ShowAlertDialog("터파기 지표면 생성 중 오류:\n" + ex.Message);
            // ★★★[JACK 0907] <b>터지면 여태 쓴 로그가 통째로 사라지고 있었다.</b>
            //   <c>DoExcav</c>는 로그를 끝에서 한 번에 쓴다(413·428행). 예외가 그 앞을 지나가면
            //   <b>목표면을 어떻게 합성했는지·어느 정점이 어긋났는지</b>가 전부 날아가고
            //   예외 문구 한 줄만 남는다 — 이번에 그것 때문에 <b>어디인지</b>를 못 봤다.
            //   §71·§73에서 배운 그대로: <b>사고 난 판의 로그가 가장 값지다.</b>
            try { DiagLog.Append("\n" + LastLog + "\n"); } catch { }
            try { DiagLog.Append($"\n■ 터파기 예외 — {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}\n"); } catch { }
        }
    }

    /// <summary>제원 프롬프트 — <b>굴착 구배 하나만</b> 묻는다(JACK 0824).
    /// 값을 그대로 받으므로 키워드가 필요 없다 — Enter면 지금 값 그대로.</summary>
    private static bool AskSpec(Editor ed)
    {
        var v = Ask(ed, "굴착 구배 1:n (0=수직)", Slope, 0.0, 30.0);
        if (v == null) return false;
        Slope = v.Value;
        return true;
    }

    private static double? Ask(Editor ed, string label, double def, double lo, double hi)
    {
        var pdo = new PromptDoubleOptions($"\n{label} 〈{def:0.###}〉: ")
        { AllowNegative = false, AllowNone = true, DefaultValue = def, UseDefaultValue = true };
        var r = ed.GetDouble(pdo);
        if (r.Status == PromptStatus.None) return def;
        if (r.Status != PromptStatus.OK) return null;
        if (r.Value < lo || r.Value > hi)
        { ed.WriteMessage($"\n → {lo:0.##}~{hi:0.##} 범위여야 합니다 — 그대로 둡니다."); return def; }
        return r.Value;
    }

    // ────────────────────────────────────────────────────────────────────
    /// <summary>★[JACK 0907] <b>터졌을 때 건져 낼 로그.</b> <see cref="DoExcav"/>는 로그를 끝에서
    /// 한 번에 쓰므로, 중간에 터지면 <c>Run</c>이 이것을 대신 써 준다.</summary>
    private static string LastLog = "";

    internal static void DoExcav(Document doc, ObjectId boxPolyId, ObjectId groundId)
    {
        Editor ed = doc.Editor;
        Database db = doc.Database;
        var log = new System.Text.StringBuilder();
        LastLog = "";
        log.AppendLine($"[터파기 지표면] {System.DateTime.Now:yyyy-MM-dd HH:mm:ss}  [DH.Grading {GradingSettings.Version}]");

        string boxHandle = boxPolyId.Handle.ToString();
        string groundHandle = groundId.Handle.ToString();
        ObjectId baseId;
        var recs = new System.Collections.Generic.List<ExcavBundle>();
        int newIdx;

        // ── ① 기록을 읽고 이번 구조물을 더한다(같은 폴리선이면 교체 — 중복 누적 방지) ──
        using (Transaction tr = db.TransactionManager.StartTransaction())
        {
            var box0 = BoundaryReader.Read(tr, boxPolyId);
            if (box0 == null || box0.Count < 3)
                throw new System.Exception("구조물 바닥 경계를 읽지 못했습니다(닫힌 폴리선이어야 합니다).");

            var old = ExcavBundleStore.TryLoadAll(db, tr, out string ewhy);
            if (old != null) recs.AddRange(old);
            else log.AppendLine($"■ 터파기 기록 없음({ewhy}) — 이번이 첫 구조물");

            newIdx = recs.FindIndex(r => r.PolyHandle == boxHandle);
            var cur = new ExcavBundle
            {
                PolyHandle = boxHandle, GroundHandle = groundHandle, Slope = Slope, Bottom = box0,
                // ★[JACK 0825] 지금의 하한을 함께 굳힌다 — 나중에 전역값이 바뀌어도 이 터파기는 안 변한다.
                MinSlope = GradingSettings.MinSlope,
            };
            if (newIdx >= 0) { recs[newIdx] = cur; log.AppendLine($"■ 같은 구조물 다시 — 기록 {newIdx + 1}번을 교체"); }
            else { recs.Add(cur); newIdx = recs.Count - 1; }
            log.AppendLine($"■ 구조물 {recs.Count}개 (이번 것 = {newIdx + 1}번)");
            tr.Commit();
        }

        // ── ② 목표면(=두 면 중 낮은 쪽)을 진짜 지표면으로 만든다 ──
        using (Transaction tr = db.TransactionManager.StartTransaction())
        {
            var groundTin = (TinSurface)tr.GetObject(groundId, OpenMode.ForRead);
            var order = new System.Collections.Generic.List<(ObjectId, string)> { (groundId, "원지반") };

            // 정지 번들이 있으면 **절토부만** 되살려 붙인다 = min(계획, 원지반).
            //   성토부는 붙이지 않는다 — 붙이면 목표면이 계획면(원지반보다 위)이 되어
            //   "굳이 다 성토해 놓고 다시 파진 않는다"는 JACK 규칙을 어긴다.
            int cutParts = 0;
            try
            {
                var regions = GradingBundleStore.TryLoadAll(db, tr, out string why);
                if (regions == null || regions.Count == 0) log.AppendLine($"■ 정지 번들 없음({why}) — 목표면 = 원지반만");
                else
                {
                    var gs = new CachedGroundSurface(groundTin);
                    for (int i = 0; i < regions.Count; i++)
                    {
                        var b = regions[i];
                        if (b == null || !b.CutHasSlope || b.Boundary == null || b.Boundary.Count < 3) continue;
                        // ★[JACK 0824] 절토 영역이 **여러 조각**일 수 있다 — 절성토가 섞인 부지에서
                        //   절토부가 두세 덩어리로 갈리는 건 흔하다. 첫 조각만 쓰면 나머지 조각의
                        //   목표면이 원지반으로 남아 **그 자리 터파기가 너무 깊게 파인다.** 전부 붙인다.
                        var rings = new System.Collections.Generic.List<System.Collections.Generic.List<Point3>>();
                        if (b.CutFinalRings != null) foreach (var r0 in b.CutFinalRings) if (r0 != null && r0.Count >= 3) rings.Add(r0);
                        if (rings.Count == 0 && b.CutFinalRing != null && b.CutFinalRing.Count >= 3) rings.Add(b.CutFinalRing);
                        if (rings.Count == 0) { log.AppendLine($"■ 구역{i + 1} 절토 데이라잇 없음 — 건너뜀"); continue; }
                        var vs = GradingGeometry.Build(b.Boundary, gs, b.Params, up: true, b.CutWallZones);
                        if (!vs.HasSlope) { log.AppendLine($"■ 구역{i + 1} 절토 복원 실패 — 건너뜀"); continue; }
                        for (int rr = 0; rr < rings.Count; rr++)
                        {
                            string nm = $"터파기_절토복원{i + 1}_{rr + 1}_DH";
                            var vid = GradingBuilder.BuildVirtualSlope(db, tr, vs.Rings, nm, vs.CornerLines, groundId);
                            bool ok = false;
                            foreach (var (ring, tag) in new[] { (rings[rr], "원본"),
                                                               (RawTriangleIntersectionFinder.CleanRing(rings[rr]), "정규화") })
                            {
                                if (ring == null) continue;
                                try { GradingBuilder.AddOuterBoundary((TinSurface)tr.GetObject(vid, OpenMode.ForWrite), ring); ok = true; break; }
                                catch (System.Exception bx) { log.AppendLine($"■ 구역{i + 1} 절토조각{rr + 1} 클립[{tag}] 실패 — {bx.Message}"); }
                            }
                            if (!ok)
                            {
                                // 못 자르면 **붙이지 않는다** — 안 자른 절토면을 붙이면 오버사이즈 그대로 퍼져
                                //   성토부까지 계획면으로 덮어 목표면이 통째로 틀어진다.
                                log.AppendLine($"■ 구역{i + 1} 절토조각{rr + 1} — 클립 실패라 목표면에 안 붙임(그 자리는 원지반 기준)");
                                continue;
                            }
                            order.Add((vid, $"절토{i + 1}-{rr + 1}"));
                            cutParts++;
                        }
                    }
                    log.AppendLine($"■ 목표면 = 원지반 + 절토부 {cutParts}개 " +
                                   "(절토부는 계획이 원지반보다 낮은 자리 = 둘 중 낮은 쪽)");
                }
            }
            catch (System.Exception rx) { log.AppendLine($"■ 절토부 복원 예외 — {rx.GetType().Name}: {rx.Message}"); }

            baseId = GradingBuilder.Composite(db, tr, BaseName, order, out string clog, true, groundId);
            log.AppendLine("■ 목표면 합성\n  " + clog.Replace("\n", "\n  "));
            tr.Commit();
        }

        // ── ③ 구조물마다 굴착 형상을 만들고 하나로 합친다(누적) ──
        string diag;
        int made = 0;
        using (Transaction tr = db.TransactionManager.StartTransaction())
        {
            var baseTin = (TinSurface)tr.GetObject(baseId, OpenMode.ForRead);
            var target = new CachedGroundSurface(baseTin);
            var pieces = new System.Collections.Generic.List<(ObjectId, string)>();

            for (int k = 0; k < recs.Count; k++)
            {
                var e = recs[k];
                string tag = $"구조물{k + 1}";
                try
                {
                    double bottomZ = double.MaxValue;
                    foreach (var q in e.Bottom) bottomZ = System.Math.Min(bottomZ, q.Z);

                    // ★★★[JACK 0907 "스샷 같은 오류가 계속 떠"] <b>어디인지 말한다.</b>
                    //   종전 메시지는 <c>1곳 · 최대 6.87m</c>가 전부였다 — 사용자가 도면에서
                    //   <b>어느 모서리를 고쳐야 하는지 알 수 없다</b>. 좌표와 두 표고를 같이 준다.
                    //   ★<b>목표면이 무엇인지도</b> 말한다: 성토부면 원지반, 절토부면 계획면이다.
                    //   그 자리가 성토부라는 것을 알면 "왜 105로 정지했는데 93이 나오지"가 풀린다.
                    int below = 0, above = 0;
                    double worstAbove = 0;
                    var bad = new System.Collections.Generic.List<string>();
                    foreach (var q in e.Bottom)
                    {
                        if (!target.TryGetElevation(q.X, q.Y, out double tz)) continue;
                        if (q.Z < tz - 0.01) below++;
                        else if (q.Z > tz + 0.01)
                        {
                            above++;
                            worstAbove = System.Math.Max(worstAbove, q.Z - tz);
                            if (bad.Count < 5)
                                bad.Add($"({q.X:F1}, {q.Y:F1}) 바닥 {q.Z:F2}m vs 목표면 {tz:F2}m — {q.Z - tz:F2}m 높다");
                        }
                    }
                    log.AppendLine($"■ {tag} 바닥 {bottomZ:F2}m · 구배 1:{e.Slope:0.##} — 목표면보다 낮은 정점 {below}개 · 높은 정점 {above}개");
                    foreach (string b in bad) log.AppendLine("    ⚠" + b);
                    LastLog = log.ToString();   // ★여기서 터져도 여태 쓴 것은 남는다
                    if (below == 0)
                    {
                        log.AppendLine($"■ {tag} — 목표면보다 낮은 데가 없어 건너뜀(팔 것이 없다)");
                        if (k == newIdx)
                            throw new System.Exception(
                                "구조물 바닥이 목표면보다 낮은 데가 없습니다 — 팔 것이 없습니다.\n" +
                                "바닥 표고를 확인하시거나, 정지면을 먼저 만들어 주세요.");
                        continue;
                    }
                    // ★★★[JACK 0907 "터파기 폴리곤 영역에서 조금이라도 높은 부분이 생기면 안 되네.
                    //   무조건 다 원지반보다 낮아야만 인식하는데 이건 좀 아닌데?"]
                    //   <b>일부만 높은 것은 막지 않는다.</b>
                    //
                    //   <b>0901에 넣은 가드가 틀린 자리를 막고 있었다.</b> 그때 주석은
                    //   <i>"팔 것이 없는데 위로 올라가는 법면이라 삼각형이 뒤집히고 스스로 교차한다"</i>
                    //   고 적었는데, <b>그것은 계측하지 않은 짐작이었다</b>.
                    //   0907에 JACK 상황(40×40 바닥 z=100 · 목표면 92→108 기울기)을 그대로
                    //   <see cref="GradingGeometry.Build"/>에 넣어 오프라인으로 돌려 보니
                    //   <b>링은 멀쩡했다</b> — 바닥판 Z=100 균일 · 넓이 1600㎡ 정확 · 부호 정상 ·
                    //   뒤집힘도 자기교차도 없었다. 그럴 수밖에 없다:
                    //   <c>Build</c>는 <b>목표면을 한 번도 안 본다</b>(널 검사뿐).
                    //
                    //   <b>진짜 원인은 데이라잇이다.</b> 성토쪽은 사면이 바깥으로 올라가는데
                    //   목표면은 내려가서 <b>영원히 안 만난다</b>(실측 d=0 +8.00m → d=12 +36.80m).
                    //   그래서 상단선이 <b>절토쪽만 감싸다 끊긴 열린 선</b>이 되고,
                    //   그것을 그대로 넘기면 Civil이 끝점↔첫점을 <b>직선으로 강제 봉합</b>해
                    //   면을 아무렇게나 잘라낸다 — 그것이 "이상한 지표면"이었다.
                    //
                    //   → 막을 자리는 여기가 아니라 <b>상단선을 넣기 전</b>이다(아래 닫힘 검사).
                    //   JACK: <i>"원지반보다 아래인 부분은 터파기로 잡혀야 하는 거 아니냐."</i> — 맞다.
                    //   사면에 정지하면 구조물이 절·성토에 걸치는 것이 <b>정상</b>이다.
                    if (above > 0)
                        log.AppendLine($"■ {tag} — 바닥 일부가 목표면보다 높다(정점 {above}개 · 최대 {worstAbove:F2}m)"
                                     + " → 그 자리는 팔 것이 없다. <b>낮은 쪽만</b> 굴착 형상을 만든다");

                    // ★[JACK 0825] 하한은 <b>그 기록이 들고 있는 값</b>으로 — 세션 전역이 아니다.
                    //   전역을 읽으면 구조물 하나 추가했을 뿐인데 기존 터파기가 통째로 다른 형상이 된다.
                    var p = MakeParams(e.Bottom, target, e.Slope, e.MinSlope);
                    var vs = GradingGeometry.Build(e.Bottom, target, p, up: true, null);
                    if (!vs.HasSlope)
                    {
                        log.AppendLine($"■ {tag} 굴착 법면 없음 — 건너뜀");
                        if (k == newIdx) throw new System.Exception("굴착 법면이 만들어지지 않았습니다(구배·표고를 확인하세요).");
                        continue;
                    }

                    var vid = GradingBuilder.BuildVirtualSlope(db, tr, vs.Rings, $"{VirtName}{k + 1}", vs.CornerLines, groundId);
                    var vTin = (TinSurface)tr.GetObject(vid, OpenMode.ForWrite);
                    var bTin = (TinSurface)tr.GetObject(baseId, OpenMode.ForRead);
                    // ★★★[검토 0903 — 치명] <b>굴착에는 바깥선을 안 넘긴다.</b>
                    //   ① <b>필요가 없다.</b> 파인 자리는 "둘레 일부만 옹벽"일 때 생기는데, 굴착은
                    //      옹벽 구간을 안 쓴다(위 Build 호출의 wallZones = null) — 파일 데가 없다.
                    //   ② <b>넘기면 위험하다.</b> 정지면은 수직 예산에 원지반 <b>전 범위</b>가 들어가
                    //      바깥 링이 원지반을 넉넉히 넘기지만(교선이 한참 안쪽에 생긴다),
                    //      굴착 예산(RiseOf)은 <b>구덩이 바닥 정점에서만</b> 목표면을 표본한다 —
                    //      구덩이가 비탈 발치에 있으면 교선이 바깥 링에 <b>바로 붙는다</b>.
                    //      그 띠를 버리면 "굴착 상단선을 찾지 못했습니다 — 구배를 더 완만하게" 라는
                    //      <b>틀린 원인</b>의 예외가 뜨거나(새 기록), 그 구조물이 <b>조용히 빠진다</b>(옛 기록).
                    var loops = RawTriangleIntersectionFinder.GetExactDaylight(vTin, bTin, null);
                    var own = RawTriangleIntersectionFinder.FilterPlanRelated(loops, e.Bottom, 5.0, out string fdiag);
                    log.AppendLine($"■ {tag} 교선 {loops.Count}개 → 루프필터 {fdiag}");
                    // ★[JACK 0907] <b>고리마다 닫혔는지·얼마나 넓은지</b> 적는다 — 도면을 열기 전에 안다.
                    {
                        int shown = 0;
                        foreach (var r in own)
                        {
                            if (r == null || r.Count < 2 || shown++ >= 6) continue;
                            double a = 0;
                            for (int i = 0; i < r.Count - 1; i++) a += r[i].X * r[i + 1].Y - r[i + 1].X * r[i].Y;
                            double dx = r[0].X - r[r.Count - 1].X, dy = r[0].Y - r[r.Count - 1].Y;
                            double gap = System.Math.Sqrt(dx * dx + dy * dy);
                            log.AppendLine($"    고리 {shown}: {r.Count}점 · {System.Math.Abs(a * 0.5):F0}㎡ · "
                                         + (gap < 0.5 ? "닫힘" : $"⚠열림({gap:F1}m)"));
                        }
                    }
                    LastLog = log.ToString();

                    // ★★★[JACK 0907 · 재현 0907] <b>닫힌 고리만 쓴다.</b>
                    //
                    //   종전엔 <b>넓이가 가장 큰 것</b> 하나만 골랐고 <b>닫혔는지는 안 봤다</b>.
                    //   구조물이 절·성토에 걸치면 상단선이 <b>절토쪽만 감싸다 끊긴 열린 선</b>이 되는데,
                    //   그것을 <see cref="GradingBuilder.AddOuterBoundary"/>에 넘기면
                    //   Civil이 끝점↔첫점을 <b>직선으로 이어</b> 면을 엉뚱하게 잘라낸다.
                    //   그 직선이 성토쪽 빈 구간을 가로지른 것이 0901의 "이상한 지표면"이다.
                    //
                    //   ★열린 선은 <b>버리는 것이 아니라 안 쓰는 것</b>이다 — 몇 개가 어떤 모양이었는지
                    //   로그에 남긴다. 닫힌 것이 하나도 없으면 <b>만들지 않고</b> 왜인지 말한다.
                    //   (종전처럼 조용히 이상한 면을 만드는 것보다 안 만드는 편이 낫다.)
                    const double CloseTol = 0.5;   // 교선 이어닫기가 쓰는 자와 같다
                    System.Collections.Generic.List<Point3>? best = null; double bestA = 0;
                    int nOpen = 0; double openBestA = 0, openGap = 0;
                    foreach (var r in own)
                    {
                        if (r == null || r.Count < 4) continue;
                        double a = 0;
                        for (int i = 0; i < r.Count - 1; i++) a += r[i].X * r[i + 1].Y - r[i + 1].X * r[i].Y;
                        a = System.Math.Abs(a * 0.5);
                        double dx = r[0].X - r[r.Count - 1].X, dy = r[0].Y - r[r.Count - 1].Y;
                        double gap = System.Math.Sqrt(dx * dx + dy * dy);
                        if (gap >= CloseTol)
                        {
                            nOpen++;
                            if (a > openBestA) { openBestA = a; openGap = gap; }
                            continue;                                    // ★열린 선은 안 쓴다
                        }
                        if (a > bestA) { bestA = a; best = r; }
                    }
                    if (nOpen > 0)
                        log.AppendLine($"■ {tag} 열린 상단선 {nOpen}개 안 씀"
                                     + $"(가장 넓은 것 {openBestA:F0}㎡ · 양 끝이 {openGap:F1}m 벌어짐)"
                                     + " — 그대로 쓰면 Civil이 직선으로 봉합해 면이 잘린다");
                    if (best == null)
                    {
                        log.AppendLine($"■ {tag} 닫힌 굴착 상단선 없음 — 건너뜀(교선 {loops.Count}개 · 걸러 남은 {own.Count}개 · 열린 {nOpen}개)");
                        if (k == newIdx) throw new System.Exception(
                            nOpen > 0
                            ? $"굴착 상단선이 닫히지 않았습니다 — 양 끝이 {openGap:F1}m 벌어져 있습니다.\n\n"
                              + "구조물이 성토부에 너무 많이 걸쳐 굴착이 한 바퀴 돌지 못했습니다.\n"
                              + "구조물을 절토부 쪽으로 옮기거나, 바닥 표고를 낮춰 주세요.\n\n"
                              + "(열린 선을 그대로 쓰면 면이 엉뚱하게 잘립니다 — 그래서 만들지 않았습니다.)"
                            : "굴착 상단선(데이라잇)을 찾지 못했습니다 — 굴착이 목표면에 닿지 않았습니다.\n" +
                              "구배를 더 완만하게 해 보세요.");
                        continue;
                    }

                    // ── ★★★[JACK 0907 "살짝 깨짐이 나타났어"] <b>상단선을 재고, 바늘을 뽑는다.</b>
                    //
                    //   구조물이 절·성토에 걸치면 굴착 영역이 <b>한쪽 끝에서 폭 0으로 좁아진다</b>
                    //   (목표면이 바닥과 같아지는 자리). 그 뾰족한 끝에서 교선이 <b>되돌아 꺾이면</b>
                    //   삼각망에 바늘 같은 조각이 남는다 — 이 저장소가 정지 링에서 이미 겪은 그것이다
                    //   (0619 <c>RemoveSpikes</c>: <i>"꺾임 &gt;135°인 점 사후 제거; 진짜 코너는 ≤90°라 안전"</i>).
                    //
                    //   ★<b>재고 나서 뽑는다.</b> 링을 통째로 CSV로 떨궈 두고(오프라인에서 그림을 볼 수 있게),
                    //   되돌아 꺾인 자리를 세어 로그에 적은 뒤, <b>확실한 바늘만</b> 뽑는다 —
                    //   양옆 변이 둘 다 <c>SpikeSegM</c>보다 짧고 <c>SpikeDeg</c>보다 심하게 꺾인 점.
                    //   진짜 뾰족한 끝(폭 0으로 좁아지는 자리)은 변이 길므로 <b>안 건드린다</b>.
                    const double SpikeDeg = 150.0;   // 이보다 심하게 꺾이면 되돌아간 것
                    const double SpikeSegM = 0.5;    // 양옆 변이 둘 다 이보다 짧아야 바늘로 친다
                    try
                    {
                        var csv = new System.Text.StringBuilder("i,x,y,z,seg_m,turn_deg\n");
                        int nRev = 0; double worstDeg = 0; string worstAt = "";
                        double segMin = double.MaxValue, segMax = 0;
                        int m = best.Count;
                        for (int i = 0; i < m; i++)
                        {
                            var a0 = best[(i - 1 + m) % m]; var b0 = best[i]; var c0 = best[(i + 1) % m];
                            double ux = b0.X - a0.X, uy = b0.Y - a0.Y;
                            double vx = c0.X - b0.X, vy = c0.Y - b0.Y;
                            double lu = System.Math.Sqrt(ux * ux + uy * uy), lv = System.Math.Sqrt(vx * vx + vy * vy);
                            double deg = 0;
                            if (lu > 1e-9 && lv > 1e-9)
                            {
                                double cs = (ux * vx + uy * vy) / (lu * lv);
                                deg = System.Math.Acos(System.Math.Clamp(cs, -1, 1)) * 180.0 / System.Math.PI;
                            }
                            if (lv > 1e-9) { segMin = System.Math.Min(segMin, lv); segMax = System.Math.Max(segMax, lv); }
                            if (deg > 135.0) { nRev++; if (deg > worstDeg) { worstDeg = deg; worstAt = $"({b0.X:F1},{b0.Y:F1})"; } }
                            csv.Append($"{i},{b0.X:F3},{b0.Y:F3},{b0.Z:F3},{lv:F3},{deg:F1}\n");
                        }
                        string cp = System.IO.Path.Combine(
                            System.IO.Path.GetDirectoryName(DiagLog.FilePath) ?? ".", $"DHGRADE_터파기상단_{k + 1}.csv");
                        try { System.IO.File.WriteAllText(cp, csv.ToString()); } catch { }
                        log.AppendLine($"    상단선 실측 — {m}점 · 변 {(segMin == double.MaxValue ? 0 : segMin):F3}~{segMax:F1}m"
                                     + $" · 135°넘게 꺾인 점 {nRev}개" + (nRev > 0 ? $"(최대 {worstDeg:F0}° @{worstAt})" : "")
                                     + $" · 링 덤프 {System.IO.Path.GetFileName(cp)}");
                    }
                    catch (System.Exception exM) { log.AppendLine("    ⚠상단선 실측 실패 — " + exM.Message); }

                    // ★[JACK 0907 실측] <b>겹친 점을 먼저 걷어낸다.</b>
                    //   0907 링 덤프에서 <b>길이 0인 변</b>과 1cm 미만 변 9개가 나왔다.
                    //   같은 자리에 점이 둘이면 삼각망이 흔들린다 — 도면에 나오기 전에 없앤다.
                    int nDup = 0;
                    {
                        const double DupM = 0.001;   // 1mm 안이면 같은 점으로 본다
                        var uniq = new System.Collections.Generic.List<Point3>(best.Count);
                        foreach (var q in best)
                        {
                            if (uniq.Count > 0)
                            {
                                var pr = uniq[uniq.Count - 1];
                                if (System.Math.Abs(q.X - pr.X) < DupM && System.Math.Abs(q.Y - pr.Y) < DupM)
                                { nDup++; continue; }
                            }
                            uniq.Add(q);
                        }
                        // 첫점과 끝점이 겹치는 것은 <b>닫힘 표시</b>라 남긴다 — 여기서 지우면 열린 것이 된다.
                        if (nDup > 0 && uniq.Count >= 4) { best = uniq; log.AppendLine($"    겹친 점 {nDup}개 걷어냄 → {best.Count}점"); }
                        else if (nDup > 0) nDup = 0;
                    }

                    // 바늘 뽑기 — 더 뽑을 것이 없을 때까지.
                    int nSpike = 0;
                    for (int pass = 0; pass < 8; pass++)
                    {
                        int m2 = best.Count;
                        if (m2 < 8) break;
                        var keep = new System.Collections.Generic.List<Point3>(m2);
                        bool cut = false;
                        for (int i = 0; i < m2; i++)
                        {
                            var a0 = best[(i - 1 + m2) % m2]; var b0 = best[i]; var c0 = best[(i + 1) % m2];
                            double ux = b0.X - a0.X, uy = b0.Y - a0.Y;
                            double vx = c0.X - b0.X, vy = c0.Y - b0.Y;
                            double lu = System.Math.Sqrt(ux * ux + uy * uy), lv = System.Math.Sqrt(vx * vx + vy * vy);
                            if (lu > 1e-9 && lv > 1e-9 && lu < SpikeSegM && lv < SpikeSegM)
                            {
                                double cs = (ux * vx + uy * vy) / (lu * lv);
                                double deg = System.Math.Acos(System.Math.Clamp(cs, -1, 1)) * 180.0 / System.Math.PI;
                                if (deg > SpikeDeg) { nSpike++; cut = true; continue; }   // 이 점을 버린다
                            }
                            keep.Add(b0);
                        }
                        if (!cut) break;
                        best = keep;
                    }
                    if (nSpike > 0)
                        log.AppendLine($"    바늘 {nSpike}개 뽑음(양옆 변 {SpikeSegM:0.##}m 미만 · {SpikeDeg:F0}° 넘게 꺾인 점) → {best.Count}점");
                    LastLog = log.ToString();

                    // ★★★[JACK 0907 "이 위치에서 쪼개지듯이 깨짐이 발생함" — ID (210289.7, 509800.8, 100.000)]
                    //   <b>링이 스스로 겹쳤는지 먼저 묻는다.</b>
                    //
                    //   JACK이 찍어 준 자리는 <c>Z=100.000</c>, 즉 <b>굴착 바닥면 위</b>이고,
                    //   로그에서 "목표면보다 높다"고 나온 꼭짓점과 <b>Y가 같다</b>(509800.8) —
                    //   굴착 영역이 <b>폭 0으로 좁아지는 바로 그 자리</b>다.
                    //   거기서 상단선이 되돌아 꺾이거나 <b>스스로 살짝 겹치면</b>,
                    //   <see cref="GradingBuilder.AddOuterBoundary"/>가 그 링으로 자를 때 면이 접힌다.
                    //
                    //   ★<b>겹쳤으면 정규화한 것을 먼저 쓴다.</b> 종전엔 <b>원본을 먼저</b> 넣고
                    //   그것이 <b>예외를 던졌을 때만</b> 정규화로 넘어갔다 — 그런데 겹친 링은
                    //   예외를 안 내고 <b>조용히 접힌 면</b>을 만든다. 그래서 정규화가 한 번도 안 돌았다.
                    //   (<c>CleanRing</c>은 NTS <c>Buffer(0)</c>으로 자기교차를 풀어 준다.)
                    bool ringOk = true;
                    try
                    {
                        var gfChk = new NetTopologySuite.Geometries.GeometryFactory();
                        int mm = best.Count;
                        var cc = new NetTopologySuite.Geometries.Coordinate[mm + 1];
                        for (int i = 0; i < mm; i++) cc[i] = new NetTopologySuite.Geometries.Coordinate(best[i].X, best[i].Y);
                        cc[mm] = new NetTopologySuite.Geometries.Coordinate(best[0].X, best[0].Y);
                        var pg = gfChk.CreatePolygon(cc);
                        ringOk = pg.IsValid;
                        log.AppendLine($"    상단선 자기겹침 검사 — {(ringOk ? "겹침 없음" : "⚠스스로 겹친다(정규화한 것을 먼저 쓴다)")}");
                    }
                    catch (System.Exception exV) { ringOk = false; log.AppendLine("    ⚠상단선 겹침 검사 실패 — " + exV.Message + "(정규화 먼저)"); }
                    LastLog = log.ToString();

                    var tryOrder = ringOk
                        ? new[] { (best, "원본"), (RawTriangleIntersectionFinder.CleanRing(best), "정규화") }
                        : new[] { (RawTriangleIntersectionFinder.CleanRing(best), "정규화"), (best, "원본") };

                    bool clipped = false;
                    foreach (var (ring, tg) in tryOrder)
                    {
                        if (ring == null) continue;
                        try
                        {
                            GradingBuilder.AddOuterBoundary(vTin, ring);
                            log.AppendLine($"■ {tag} 클립경계 주입[{tg}] — 굴착 상단 {bestA:F0}㎡");
                            e.FinalRing = ring;
                            clipped = true;
                            break;
                        }
                        catch (System.Exception bx) { log.AppendLine($"■ {tag} 클립경계 주입[{tg}] 실패 — {bx.Message}"); }
                    }
                    if (!clipped)
                    {
                        log.AppendLine($"■ {tag} — 클립 실패라 합치지 않음(오버사이즈 면을 넣으면 지표면이 통째로 틀어진다)");
                        if (k == newIdx) throw new System.Exception("굴착 상단선을 경계로 넣지 못했습니다 — 로그를 확인하세요.");
                        continue;
                    }
                    pieces.Add((vid, tag));
                    made++;
                }
                catch (System.Exception ex) when (k != newIdx)
                {
                    // 옛 기록 하나가 깨져도 이번 작업까지 막지 않는다 — 그 구조물만 빠진다.
                    log.AppendLine($"■ {tag} 예외 — {ex.GetType().Name}: {ex.Message} (그 구조물만 건너뜀)");
                }
            }

            if (pieces.Count == 0) throw new System.Exception("만들어진 굴착이 하나도 없습니다 — 로그를 확인하세요.");
            ObjectId outId = GradingBuilder.Composite(db, tr, SurfName, pieces, out string olog, true, groundId);
            log.AppendLine($"■ 결과 = {SurfName} — 구조물 {made}개 합침(굴착 형상만: 바닥 + 법면)\n  " + olog.Replace("\n", "\n  "));

            // 조각은 숨긴다 — 결과와 겹쳐 보이면 헷갈린다.
            for (int k = 0; k < recs.Count; k++) GradingBuilder.SetSurfaceVisible(tr, $"{VirtName}{k + 1}", false);

            // ★[JACK 0824 "전부 보기는 합성된 하나의 지표면으로 보여야 해"] **전체면을 함께 굽는다.**
            //   보기 명령이 지표면을 새로 만들면 안 된다(보기는 형상을 안 건드려야 한다) —
            //   그래서 여기서 만들어 두고, 보기는 켜고 끄기만 한다.
            try
            {
                var allOrder = new System.Collections.Generic.List<(ObjectId, string)>();
                var planId = GradingBuilder.FindSurfaceByBaseName(tr, "정지면_DH");
                if (!planId.IsNull) allOrder.Add((planId, "정지면"));       // 이미 원지반+계획 합성면이다
                else allOrder.Add((groundId, "원지반"));
                allOrder.Add((outId, "터파기"));                            // 나중에 붙는 것이 이긴다 = 굴착이 파인다
                GradingBuilder.Composite(db, tr, ViewSurfaceCommand.AllName, allOrder, out string alog, true, groundId);
                GradingBuilder.SetSurfaceVisible(tr, ViewSurfaceCommand.AllName, false);
                log.AppendLine($"■ 전체면({ViewSurfaceCommand.AllName}) — 원지반+계획+터파기 합성\n  " + alog.Replace("\n", "\n  "));
            }
            catch (System.Exception ax) { log.AppendLine($"■ 전체면 합성 실패 — {ax.Message}(보기 '전부'는 정지면으로 물러납니다)"); }

            ExcavBundleStore.SaveAll(db, tr, recs);
            log.AppendLine($"■ 터파기 기록 저장 — 구조물 {recs.Count}개(다시 만들 때 폴리선을 안 골라도 된다)");

            diag = log.ToString();
            LastLog = diag;
            tr.Commit();
        }

        // ── ④ 뒷정리: 목표면·복원 절토부는 숨긴다(헷갈리지 않게) ──
        using (Transaction tr = db.TransactionManager.StartTransaction())
        {
            GradingBuilder.SetSurfaceVisible(tr, BaseName, false);
            for (int i = 1; i <= 8; i++)
                for (int r = 1; r <= 8; r++)
                    GradingBuilder.SetSurfaceVisible(tr, $"터파기_절토복원{i}_{r}_DH", false);
            tr.Commit();
        }

        // ★[JACK 0901 "튕긴다"] 이 명령이 메모리를 얼마나 쓰는지 한 줄로 남긴다 — 고치기 전에 <b>재고</b> 본다.
        try { DiagLog.Append("\n" + diag + "\n  ★" + StageTimer.MemSince(_mem0) + "\n"); } catch { }
        ed.WriteMessage($"\n[터파기 지표면] 완료 — {SurfName} (구조물 {made}개)" +
                        $"\n  굴착 구배 1:{Slope:0.##}{(Slope <= GradingSettings.WallGateSlope + 1e-9 ? " (수직)" : "")}" +
                        $"\n  자세한 내용: {DiagLog.FilePath}");
    }


    /// <summary>터파기 제원으로 <see cref="GradingParams"/>를 만든다 — 절·성토 양쪽에 같은 값을 넣는다.
    /// <para>수직 예산은 <b>실제 표고차</b>에서 온다(정지면과 같은 이유) — 단높이로 곱해 잡으면
    /// 단수 상한에 걸리는 순간 예산이 함께 주저앉아 법면이 목표면에 닿기 전에 끊긴다.</para></summary>
    private static GradingParams MakeParams(System.Collections.Generic.List<Point3> box, IGroundSurface target, double slope, double minSlope)
    {
        double bMin = double.MaxValue, bMax = double.MinValue;
        foreach (var q in box) { bMin = System.Math.Min(bMin, q.Z); bMax = System.Math.Max(bMax, q.Z); }
        double tMin = double.MaxValue, tMax = double.MinValue;
        foreach (var q in box)
            if (target.TryGetElevation(q.X, q.Y, out double tz))
            { tMin = System.Math.Min(tMin, tz); tMax = System.Math.Max(tMax, tz); }
        if (tMax < tMin) { tMin = bMin; tMax = bMax; }
        double rise = System.Math.Max(1.0, (tMax - bMin) * 1.5 + 5.0);   // 여유 1.5배 + 5m

        // ★[JACK 0824 "단높이 설정은 필요 없어, 어차피 구배로만"] **단이 하나뿐인 프로파일.**
        //   단높이를 전체 예산으로 두면 첫 단이 끝까지 올라가고, 소단이 0이라 중간에 평탄이 안 낀다
        //   → 바닥에서 목표면까지 **끊김 없는 한 장의 법면**이 된다.
        //   (엔진을 안 고치고 제원만으로 얻는다 — 정지면 쪽에 손대지 않는 게 안전하다.)
        double one = rise + 1.0;
        double sl = System.Math.Max(slope, minSlope);   // 0=수직 → 그 기록의 최소구배로
        return new GradingParams
        {
            CutBenchHeight = one, FillBenchHeight = one,
            CutBenchWidth = 0.0, FillBenchWidth = 0.0,
            CutSlope = sl, FillSlope = sl,
            CellSize = GradingSettings.CellSize,
            MaxBenches = 4,
            MaxRise = rise,
            VertexSpacing = GradingSettings.VertexSpacing,
            MinSlope = minSlope,
            WallGateSlope = GradingSettings.WallGateSlope,
            MinFaceRun = GradingSettings.MinFaceRun,
            MiterConvex = GradingSettings.MiterConvex,
            MiterLimit = GradingSettings.MiterLimit,
        };
    }
}
