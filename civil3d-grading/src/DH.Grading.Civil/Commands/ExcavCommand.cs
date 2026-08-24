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

    [CommandMethod("DHEXCAV")]
    public void Run()
    {
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
            using (ViewSurfaceCommand.Focus(db, null))   // 만드는 동안엔 전부 보이게(원지반 클릭 필요) — 아래에서 결과만 남긴다
                DoExcav(doc, rPoly.ObjectId, groundId);
            using var trF = db.TransactionManager.StartTransaction();
            GradingBuilder.IsolateSurfaces(trF, SurfName);   // 끝나면 터파기만
            trF.Commit();
            ed.WriteMessage("\n[보기] 터파기만 — 다시 보려면 [보기] 버튼(DHVIEW)");
        }
        catch (System.Exception ex)
        {
            ed.WriteMessage("\n[터파기 오류] " + ex.Message);
            AcadApp.ShowAlertDialog("터파기 지표면 생성 중 오류:\n" + ex.Message);
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
    internal static void DoExcav(Document doc, ObjectId boxPolyId, ObjectId groundId)
    {
        Editor ed = doc.Editor;
        Database db = doc.Database;
        var log = new System.Text.StringBuilder();
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

                    int below = 0, above = 0;
                    foreach (var q in e.Bottom)
                    {
                        if (!target.TryGetElevation(q.X, q.Y, out double tz)) continue;
                        if (q.Z < tz - 0.01) below++; else if (q.Z > tz + 0.01) above++;
                    }
                    log.AppendLine($"■ {tag} 바닥 {bottomZ:F2}m · 구배 1:{e.Slope:0.##} — 목표면보다 낮은 정점 {below}개 · 높은 정점 {above}개");
                    if (below == 0)
                    {
                        log.AppendLine($"■ {tag} — 목표면보다 낮은 데가 없어 건너뜀(팔 것이 없다)");
                        if (k == newIdx)
                            throw new System.Exception(
                                "구조물 바닥이 목표면보다 낮은 데가 없습니다 — 팔 것이 없습니다.\n" +
                                "바닥 표고를 확인하시거나, 정지면을 먼저 만들어 주세요.");
                        continue;
                    }
                    if (above > 0 && k == newIdx)
                        ed.WriteMessage($"\n[터파기] ⚠ 구조물 바닥이 목표면보다 높은 자리가 {above}곳 있습니다 — " +
                                        "그 자리는 터파기가 아니라 성토입니다(굴착만 만듭니다).");

                    var p = MakeParams(e.Bottom, target, e.Slope);
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
                    var loops = RawTriangleIntersectionFinder.GetExactDaylight(vTin, bTin, null);
                    var own = RawTriangleIntersectionFinder.FilterPlanRelated(loops, e.Bottom, 5.0, out string fdiag);
                    log.AppendLine($"■ {tag} 교선 {loops.Count}개 → 루프필터 {fdiag}");

                    System.Collections.Generic.List<Point3>? best = null; double bestA = 0;
                    foreach (var r in own)
                    {
                        double a = 0;
                        for (int i = 0; i < r.Count - 1; i++) a += r[i].X * r[i + 1].Y - r[i + 1].X * r[i].Y;
                        a = System.Math.Abs(a * 0.5);
                        if (a > bestA) { bestA = a; best = r; }
                    }
                    if (best == null)
                    {
                        log.AppendLine($"■ {tag} 굴착 상단선 없음 — 건너뜀");
                        if (k == newIdx) throw new System.Exception(
                            "굴착 상단선(데이라잇)을 찾지 못했습니다 — 굴착이 목표면에 닿지 않았습니다.\n" +
                            "구배를 더 완만하게 해 보세요.");
                        continue;
                    }

                    bool clipped = false;
                    foreach (var (ring, tg) in new[] { (best, "원본"), (RawTriangleIntersectionFinder.CleanRing(best), "정규화") })
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

        try { DiagLog.Append("\n" + diag); } catch { }
        ed.WriteMessage($"\n[터파기 지표면] 완료 — {SurfName} (구조물 {made}개)" +
                        $"\n  굴착 구배 1:{Slope:0.##}{(Slope <= GradingSettings.MinSlope ? " (수직)" : "")}" +
                        $"\n  자세한 내용: {DiagLog.FilePath}");
    }


    /// <summary>터파기 제원으로 <see cref="GradingParams"/>를 만든다 — 절·성토 양쪽에 같은 값을 넣는다.
    /// <para>수직 예산은 <b>실제 표고차</b>에서 온다(정지면과 같은 이유) — 단높이로 곱해 잡으면
    /// 단수 상한에 걸리는 순간 예산이 함께 주저앉아 법면이 목표면에 닿기 전에 끊긴다.</para></summary>
    private static GradingParams MakeParams(System.Collections.Generic.List<Point3> box, IGroundSurface target, double slope)
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
        double sl = System.Math.Max(slope, GradingSettings.MinSlope);   // 0=수직 → 최소구배로
        return new GradingParams
        {
            CutBenchHeight = one, FillBenchHeight = one,
            CutBenchWidth = 0.0, FillBenchWidth = 0.0,
            CutSlope = sl, FillSlope = sl,
            CellSize = GradingSettings.CellSize,
            MaxBenches = 4,
            MaxRise = rise,
            VertexSpacing = GradingSettings.VertexSpacing,
            MinSlope = GradingSettings.MinSlope,
            MinFaceRun = GradingSettings.MinFaceRun,
            MiterConvex = GradingSettings.MiterConvex,
            MiterLimit = GradingSettings.MiterLimit,
        };
    }
}
