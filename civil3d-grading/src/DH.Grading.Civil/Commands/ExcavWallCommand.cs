using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using DH.Grading.Core;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;
using AcDoc = Autodesk.AutoCAD.ApplicationServices.Document;

namespace DH.Grading.Civil.Commands;

/// <summary>★★★[계획 7단계 · JACK 0909 확정 <i>"(가) 제대로 한다"</i>]
/// <b>터파기 둘레의 한 구간만 가시설(수직)로 — 그리고 되돌리기.</b>
///
/// <para><b>왜 정지의 것을 그대로 못 쓰나.</b> <see cref="ZoneEditCommon"/>은 671줄이고
/// 정지 번들·단높이·소단폭·옹벽형태에 깊이 묶여 있다. 터파기는 <b>제원이 구배 하나뿐</b>이라
/// (JACK 0824: <i>"단높이 설정은 필요 없어, 어차피 구배로만 치는 거야"</i>)
/// 그 기계를 끌고 오면 <b>쓰지 않는 물음이 대부분</b>이 된다.
/// 대신 <b>사용자가 겪는 흐름</b>은 똑같이 맞춘다 — 자리를 찍고, 길이를 주고, 다시 만든다.</para>
///
/// <para>★<b>옹벽이 아니라 가시설이다.</b> 이 저장소는 둘을 이미 갈라 놓았다 —
/// 종단 막대가 옹벽은 <b>계획면↔원지반</b>, 가시설은 <b>계획면↔터파기 바닥</b>이고
/// 밴드도 가시설은 건너뛴다(JACK 0827). 그래서 여기서 만든 구간은
/// <b>터파기 기록</b>에만 들어가고 정지 번들에는 닿지 않는다 — 태그가 섞이면 종단이 틀리게 그려진다.</para>
///
/// <para>★<b>옹벽 형태(보강토·역T·앵커판넬)는 여기 없다.</b> 그것들은 블록·기초·3m 앵커를 만드는
/// 물건이라 <b>흙막이 부재가 아니다</b>. 가시설 종류(H파일·시트파일 등)가 필요해지면
/// 그때 <b>별도 목록</b>을 만들어야 한다.</para></summary>
public sealed class ExcavWallCommand
{
    /// <summary>한 번에 지정할 수 있는 구간 길이의 하한(m) — 이보다 짧으면 링을 못 자른다.</summary>
    private const double MinRun = 0.5;

    [CommandMethod("DHEXCAVWALL", CommandFlags.Modal)]
    public void ToWall() => Edit(wall: true);

    [CommandMethod("DHEXCAVSLOPE", CommandFlags.Modal)]
    public void ToSlope() => Edit(wall: false);

    /// <summary>이 도면의 <b>모든</b> 가시설 구간을 지우고 순수 사면으로 되돌린다.</summary>
    [CommandMethod("DHEXCAVWALLCLEAR", CommandFlags.Modal)]
    public void ClearAll()
    {
        var doc = AcadApp.DocumentManager.MdiActiveDocument;
        if (doc == null) return;
        var ed = doc.Editor;
        GradingSettings.SyncToDocument(doc);
        try
        {
            var recs = Load(doc, out string why);
            if (recs == null) { ed.WriteMessage("\n[가시설] " + why); return; }
            int n = 0;
            foreach (var r in recs) if (r.WallZones != null && r.WallZones.Count > 0) { r.WallZones = null; n++; }
            if (n == 0) { ed.WriteMessage("\n[가시설] 지정된 구간이 없습니다."); return; }
            Save(doc, recs);
            ed.WriteMessage($"\n[가시설] 구조물 {n}개의 구간을 전부 지웠습니다 — 다시 만듭니다.");
            Rebuild(doc, recs, ed);
        }
        catch (System.Exception ex)
        {
            ed.WriteMessage("\n[가시설] 실패 — " + ex.Message);
            try { DiagLog.Append($"\n■ 가시설 전체해제 예외 — {ex.GetType().Name}: {ex.Message}\n"); } catch { }
        }
    }

    /// <summary>자리를 찍고 길이를 받아 그 구간을 <b>수직으로</b>(또는 사면으로 되돌린다).</summary>
    private static void Edit(bool wall)
    {
        var doc = AcadApp.DocumentManager.MdiActiveDocument;
        if (doc == null) return;
        var ed = doc.Editor;
        string label = wall ? "가시설 변환" : "사면 변환";
        GradingSettings.SyncToDocument(doc);
        PickSession.Hook();
        if (!PickSession.Begin(ed, label)) return;
        try
        {
            var recs = Load(doc, out string why);
            if (recs == null) { ed.WriteMessage($"\n[{label}] " + why); return; }

            // ① 어느 자리인가 — 터파기 둘레 근처를 찍는다.
            var ppo = new PromptPointOptions($"\n{label}할 자리를 터파기 둘레 근처에서 찍으세요: ")
            { AllowNone = false };
            var pr = ed.GetPoint(ppo);
            if (pr.Status != PromptStatus.OK) { ed.WriteMessage($"\n[{label}] 취소."); return; }

            // ② 어느 구조물의 어느 자리에 가장 가까운가.
            int best = -1; double bestD = double.MaxValue, bestT = 0, bestLen = 0;
            for (int i = 0; i < recs.Count; i++)
            {
                var b = recs[i].Bottom;
                if (b == null || b.Count < 3) continue;
                double t = Project(b, pr.Value.X, pr.Value.Y, out double d, out double total);
                if (d < bestD) { bestD = d; best = i; bestT = t; bestLen = total; }
            }
            if (best < 0) { ed.WriteMessage($"\n[{label}] 터파기 기록에서 둘레를 못 읽었습니다."); return; }
            ed.WriteMessage($"\n  구조물 {best + 1} · 둘레 {bestLen:0.#}m 중 {bestT:0.#}m 자리");

            // ③ 얼마나 — 그 자리를 가운데 두고 이만큼.
            var pdo = new PromptDoubleOptions($"\n{label}할 구간 길이(m) 〈{System.Math.Min(20, bestLen * 0.5):0.#}〉 · 둘레 전체는 {bestLen:0.#}: ")
            { AllowNegative = false, AllowZero = false, AllowNone = true, DefaultValue = System.Math.Min(20, bestLen * 0.5), UseDefaultValue = true };
            var dr = ed.GetDouble(pdo);
            if (dr.Status != PromptStatus.OK) { ed.WriteMessage($"\n[{label}] 취소."); return; }
            double run = dr.Value;
            if (run < MinRun) { ed.WriteMessage($"\n[{label}] 구간이 너무 짧습니다({MinRun:0.#}m 이상)."); return; }
            var rec = recs[best];
            double t0, t1;
            // ★★[검토 0909 · 높음1] <b>한 바퀴는 따로 적는다.</b>
            //   종전엔 <c>bestT ± L/2</c>를 랩 처리해 <b>T0 == T1</b>이 됐고, 안/밖 판정이
            //   <c>T0 &lt;= T1</c> 가지를 타서 <b>그 한 점만</b> 안으로 봤다 — "전체"라고 적어 놓고 <b>0</b>이었다.
            //   둘레 20m 이하 구조물(2×2m 집수정 = 8m)에서는 <b>Enter만 눌러도 반드시</b> 걸렸다.
            if (run >= bestLen - 1e-6) { t0 = 0; t1 = bestLen; }
            else { double half = run * 0.5; t0 = Wrap(bestT - half, bestLen); t1 = Wrap(bestT + half, bestLen); }

            if (wall)
            {
                rec.WallZones ??= new System.Collections.Generic.List<SlopeZone>();
                // ★★[2차 검토 0909 · 보통] <b>하한이 문턱보다 크면 아무 일도 안 일어난다.</b>
                //   구간 규칙은 <c>rec.MinSlope</c>로 적고, "수직인가" 판정은 고정 문턱
                //   <c>WallGateSlope</c>로 한다. 앞이 뒤보다 크면 형상도 안 바뀌고 막대도 안 서는데
                //   <b>성공 메시지만</b> 떴다 — "눌러도 아무 일 없는 단추"가 된다.
                if (rec.MinSlope > GradingSettings.WallGateSlope + 1e-9)
                {
                    ed.WriteMessage($"\n[{label}] 이 터파기의 최소구배가 1:{rec.MinSlope:0.###}로"
                                  + $" 수직 문턱(1:{GradingSettings.WallGateSlope:0.###})보다 완만해"
                                  + " <b>가시설로 세울 수가 없습니다</b>."
                                  // ★[8단계 검토 · 보통4] 최소구배는 <b>어느 화면에도 칸이 없다</b> —
                                  //   없는 곳을 가리키면 사용자는 빠져나갈 길이 없다.
                                  // ★[JACK 0910] [터파기 창] 단추는 리본에서 빠졌다 — 지금 가는 길로 고친다.
                                  + "\n  → 리본 [계획부지 생성] ▸ [구조물 터파기] 창에서"
                                  + " 굴착 구배를 0(수직)으로 놓고 다시 만든 뒤 지정하세요.");
                    return;
                }
                var z = new SlopeZone { T0 = t0, T1 = t1 };
                // ★단높이·소단은 안 쓴다 — 터파기 제원은 구배 하나다(JACK 0824).
                //   소단폭 −1 = "전역값 따름"(이 저장소의 관례).
                // ★★[검토 0909 · 높음3] <b>전역이 아니라 그 기록의 하한</b>을 쓴다.
                //   <c>ExcavBundle.MinSlope</c>가 존재하는 이유가 <i>"전역값이 바뀌는 순간 옛 터파기를
                //   다른 형상으로 되살렸다"</i>인데, 구간 규칙만 다시 전역을 읽으면
                //   <b>막대 두께와 실제 벽면 폭이 달라진다</b>.
                z.Rules.Add((0, rec.MinSlope, -1));
                z.Ref = new System.Collections.Generic.List<Point3>(rec.Bottom);
                // ★[2차 검토 0909 · 보통] <b>겹치는 옛 구간은 걷어내고 넣는다.</b>
                //   안 그러면 같은 자리를 열 번 지정하면 구간이 열 개 쌓이고, 각각
                //   <c>rec.Bottom</c> 복사본을 들고 다녀 기록이 커지고 로그를 못 읽는다.
                //   (판정은 "마지막이 이긴다"라 결과는 같았지만, 자료가 지저분해진다.)
                int dup = rec.WallZones.RemoveAll(q => Overlaps(q.T0, q.T1, t0, t1, bestLen));
                rec.WallZones.Add(z);
                if (dup > 0) ed.WriteMessage($"\n  · 겹치던 옛 구간 {dup}개를 이번 것으로 합쳤습니다.");
                ed.WriteMessage($"\n[{label}] 구조물 {best + 1}의 {run:0.#}m 구간을 <수직>으로 바꿉니다.");
            }
            else
            {
                if (rec.WallZones == null || rec.WallZones.Count == 0)
                { ed.WriteMessage($"\n[{label}] 이 도면에 가시설 구간이 없습니다."); return; }
                double killed = 0;
                foreach (var q in rec.WallZones)
                    if (Overlaps(q.T0, q.T1, t0, t1, bestLen))
                        killed += q.T1 >= q.T0 ? q.T1 - q.T0 : bestLen - q.T0 + q.T1;
                int gone = rec.WallZones.RemoveAll(z => Overlaps(z.T0, z.T1, t0, t1, bestLen));
                if (gone == 0) { ed.WriteMessage($"\n[{label}] 그 자리에 겹치는 가시설 구간이 없습니다."); return; }
                if (rec.WallZones.Count == 0) rec.WallZones = null;
                // ★[2차 검토 0909 · 보통] <b>얼마나 지웠는지 말한다.</b> 이 명령은 <b>겹치면 통째로</b>
                //   지운다 — 둘레 전체(80m) 구간에서 5m만 되돌리려 해도 <b>80m가 다 사라진다</b>.
                //   그것을 "1개를 지웠습니다"로만 알리면 사용자가 눈치챌 길이 없다.
                ed.WriteMessage($"\n[{label}] 구간 {gone}개(합 {killed:0.#}m)를 지웠습니다 — 그 자리는 다시 사면입니다."
                              + (killed > run + 1e-6 ? $"\n  ⚠ 요청한 {run:0.#}m보다 넓습니다 — 겹친 구간은 통째로 지웁니다." : ""));
            }

            // ★[검토 0909 · 보통] 저장을 <b>먼저</b> 해야 <c>DoExcav</c>가 구간을 읽는다.
            //   대신 다시 만들기가 실패하면 <b>그 사실을 반드시 말한다</b> — 기록엔 구간이 있는데
            //   지표면은 옛것인 채로 조용히 남으면, 다음 [종단도]가 없는 벽에 막대를 세운다.
            Save(doc, recs);
            Rebuild(doc, recs, ed, best);
        }
        catch (System.Exception ex)
        {
            ed.WriteMessage($"\n[{label}] 실패 — " + ex.Message);
            try { DiagLog.Append($"\n■ 가시설 편집 예외 — {ex.GetType().Name}: {ex.Message}\n"); } catch { }
        }
        finally { PickSession.End(); }
    }

    // ── 자잘한 것 ─────────────────────────────────────────────────────────
    private static System.Collections.Generic.List<ExcavBundle> Load(AcDoc doc, out string why)
    {
        why = "";
        using var tr = doc.Database.TransactionManager.StartTransaction();
        var recs = ExcavBundleStore.TryLoadAll(doc.Database, tr, out string w, out bool tooNew);
        tr.Commit();
        if (tooNew) { why = w + " — 애드인을 최신으로 올린 뒤 다시 하세요."; return null; }
        if (recs == null || recs.Count == 0) { why = "터파기 기록이 없습니다 — [구조물 터파기]를 먼저 하세요."; return null; }
        return recs;
    }

    private static void Save(AcDoc doc, System.Collections.Generic.List<ExcavBundle> recs)
    {
        using var tr = doc.Database.TransactionManager.StartTransaction();
        ExcavBundleStore.SaveAll(doc.Database, tr, recs);
        tr.Commit();
    }

    /// <summary>구간을 바꿨으면 <b>다시 만들어야</b> 보인다 — 기록만 고치면 도면은 그대로다.</summary>
    private static void Rebuild(AcDoc doc, System.Collections.Generic.List<ExcavBundle> recs, Editor ed, int idx = 0)
    {
        ObjectId groundId = ObjectId.Null;
        try
        {
            using var tr = doc.Database.TransactionManager.StartTransaction();
            groundId = NoriCommand.FindByHandle(doc.Database, recs[idx].GroundHandle);
            if (groundId.IsNull) groundId = NoriCommand.FindByHandle(doc.Database, GradingSettings.LastGroundHandle);
            tr.Commit();
        }
        catch { }
        if (groundId.IsNull)
        {
            ed.WriteMessage("\n[가시설] 원지반을 못 찾아 다시 만들지 못했습니다 —"
                          + " [구조물 터파기]를 한 번 돌리면 반영됩니다.");
            return;
        }
        // ★★[2차 검토 0909] <b>고친 그 구조물</b>의 폴리선을 준다.
        //   <c>DoExcav</c>는 기록된 전부를 다시 굽지만, <b>넘긴 폴리선의 기록만</b>
        //   지금 도면 모양으로 새로 읽어 갈아끼운다(<c>cur</c>). 늘 <c>recs[0]</c>을 주면
        //   3번 구조물의 구간을 고쳐도 <b>1번이 다시 읽히고</b>, 제원 물려주기도 1번에만 걸린다.
        if (idx < 0 || idx >= recs.Count) idx = 0;
        ObjectId anyPoly = NoriCommand.FindByHandle(doc.Database, recs[idx].PolyHandle);
        if (anyPoly.IsNull)
        {
            ed.WriteMessage("\n[가시설] 구조물 폴리선을 못 찾아 다시 만들지 못했습니다.");
            return;
        }
        try
        {
            bool ok;
            using (ViewSurfaceCommand.Focus(doc.Database, null))
                // ★★[검토 0909 · 치명3] <c>keepSpec: true</c> — 구배·하한·기준면을 <b>그 기록의 것으로</b>.
                //   구간만 고치러 온 것이지 제원을 바꾸러 온 게 아니다.
                ok = ExcavCommand.DoExcav(doc, anyPoly, groundId, keepSpec: true);
            ViewSurfaceCommand.ShowAll();

            // ★★★[2차 검토 0909 · 높음1] <b>지표면이 "있다"를 성공으로 보면 안 됐다.</b>
            //   <c>DoExcav</c>는 조용히 돌아설 때 <b>아무것도 지우지 않으므로</b>
            //   직전 실행이 만든 <c>터파기면_DH</c>가 그대로 남아 <b>언제나 참</b>이었다 —
            //   막겠다던 바로 그 상태를 못 잡는 처방이었다.
            //   → 이제 <c>DoExcav</c>가 <b>자기가 구웠는지</b>를 직접 답한다.
            if (ok) ExcavPalette.Say("가시설 구간을 바꿔 터파기를 다시 만들었습니다.");
            else
            {
                ed.WriteMessage("\n[가시설] 구간은 저장했지만 지표면을 다시 만들지 못했습니다 —"
                              + " 창의 [터파기 지표면 생성]을 눌러 주세요(누르기 전 종단은 옛 형상입니다).");
                ExcavPalette.Say("구간은 저장했습니다 — [터파기 지표면 생성]을 눌러야 도면에 반영됩니다.");
            }
        }
        catch (System.Exception ex)
        {
            ed.WriteMessage("\n[가시설] 다시 만들기 실패 — " + ex.Message);
            try { ViewSurfaceCommand.ShowAll(); } catch { }
        }
    }

    /// <summary>점을 닫힌 둘레에 투영해 <b>호길이</b>를 얻는다.</summary>
    private static double Project(System.Collections.Generic.IReadOnlyList<Point3> poly,
                                  double x, double y, out double dist, out double total)
    {
        double best = double.MaxValue, bestT = 0, run = 0;
        for (int i = 0; i < poly.Count; i++)
        {
            var a = poly[i];
            var b = poly[(i + 1) % poly.Count];
            double dx = b.X - a.X, dy = b.Y - a.Y;
            double len2 = dx * dx + dy * dy;
            double segLen = System.Math.Sqrt(len2);
            if (len2 > 1e-12)
            {
                double u = ((x - a.X) * dx + (y - a.Y) * dy) / len2;
                if (u < 0) u = 0; else if (u > 1) u = 1;
                double qx = a.X + u * dx, qy = a.Y + u * dy;
                double d2 = (x - qx) * (x - qx) + (y - qy) * (y - qy);
                if (d2 < best) { best = d2; bestT = run + u * segLen; }
            }
            run += segLen;
        }
        dist = System.Math.Sqrt(best);
        total = run;
        return bestT;
    }

    private static double Wrap(double t, double len) => len <= 0 ? 0 : ((t % len) + len) % len;

    /// <summary>두 구간이 겹치는가 — 랩(0을 지나 이어짐)까지 본다.</summary>
    private static bool Overlaps(double a0, double a1, double b0, double b1, double len)
    {
        bool In(double t, double s, double e) => s <= e ? (t >= s && t <= e) : (t >= s || t <= e);
        return In(a0, b0, b1) || In(a1, b0, b1) || In(b0, a0, a1) || In(b1, a0, a1);
    }
}
