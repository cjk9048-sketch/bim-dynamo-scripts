using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;
using CivilDb = Autodesk.Civil.DatabaseServices;

namespace DH.Grading.Civil;

/// <summary>★★★[JACK 0908] <b>단면검토선 폭을 정지 결과에서 잰다.</b>
///
/// <para>JACK: <i>"종단선형을 그릴 때 단면검토선 횡단폭을 자동으로 계획지표면 폭을 인식해서
/// 지정해 줄 순 없나?"</i> · <i>"한 값이고 5m 단위로 떨어지게, 좌우폭은 무조건 같게,
/// 여유폭은 5m. 자동으로 설정되면 도면설정에도 그 값으로 업데이트."</i></para>
///
/// <para><b>왜 필요한가.</b> JACK 현장 실측(0908): 계획 부지는 폭 55.7m인데
/// <b>정지 결과는 사면까지 83.0m</b>였다. 그런데 절단선은 좌우 30m(합 60m)라
/// <b>사면 끝이 횡단면도에서 잘려 나갔다</b>. 사람이 매번 재서 칠 수는 없다.</para>
///
/// <para><b>무엇을 재나.</b> 정지가 끝나며 남긴 <b>데이라잇 링</b>(사면 바깥 끝)이다.
/// 그 점들을 선형에 대고 직각 거리를 재서 가장 먼 것을 찾는다 —
/// 계획선이 아니라 <b>결과</b>를 재야 사면이 안 잘린다.</para></summary>
internal static class XsecWidth
{
    /// <summary>여유폭(m) — 사면 끝에 딱 맞추면 선이 화면 가장자리에 붙는다(JACK 0908: 5m).</summary>
    internal const double MarginM = 5.0;

    /// <summary>이 단위로 <b>올림</b>한다(m) — 도면에 적히는 숫자가 깔끔해야 한다(JACK 0908: 5m).</summary>
    internal const double StepM = 5.0;

    /// <summary>이보다 넓어지면 <b>말한다</b>. 사면이 아주 긴 현장에서 횡단 축척이 확 작아지는 것을 알린다.
    /// <para>막지는 않는다 — 잘린 도면보다 작은 도면이 낫다. 판단은 사람이 한다.</para></summary>
    internal const double WarnWideM = 80.0;

    /// <summary>★ 이번 도면에 쓸 좌·우 폭을 정한다.
    ///
    /// <para><b>수동</b>이면 도면설정 값을 그대로 쓴다. <b>자동</b>이면 정지 결과에서 재고,
    /// 잰 값을 <b>도면설정에도 되돌려 적는다</b>(JACK 요구) — 그래야 사용자가 무엇이 쓰였는지 본다.</para>
    /// <para>잴 것이 없으면(정지를 아직 안 돌렸다) <b>지금 설정값으로 물러선다</b> — 조용히가 아니라 말하고.</para></summary>
    internal static (double L, double R) Resolve(Database db, ObjectId alignId, System.Text.StringBuilder log)
    {
        double curL = System.Math.Max(0.0, GradingSettings.XsecLeft);
        double curR = System.Math.Max(0.0, GradingSettings.XsecRight);
        if (!GradingSettings.XsecWidthAuto)
        {
            log?.AppendLine($"  절단선 폭 — <b>수동</b> 좌{curL:0.#}/우{curR:0.#}m(도면설정 값)");
            return (curL, curR);
        }

        double far = FarthestOffset(db, alignId, out int nPts, out string why, out string perNote);
        if (double.IsNaN(far) || nPts == 0)
        {
            log?.AppendLine($"  절단선 폭 — 자동이지만 잴 것이 없어 설정값으로 간다 좌{curL:0.#}/우{curR:0.#}m ({why})");
            return (curL, curR);
        }

        // ★<b>좌우 같게</b>(JACK) — 먼 쪽에 맞춘다. 그래야 어느 쪽도 안 잘린다.
        double w = far + MarginM;
        w = System.Math.Ceiling(w / StepM - 1e-9) * StepM;      // 5m 단위 올림
        if (w < StepM) w = StepM;

        GradingSettings.XsecLeft = w;                            // ★도면설정에도 되돌려 적는다(JACK)
        GradingSettings.XsecRight = w;
        // ★★★[검토 0908 · 심각] <b><c>SaveUserPrefs()</c>를 부르지 않는다.</b>
        //   ①<b>얻는 것이 없다</b> — 그 함수는 <c>MiterConvex</c>·<c>XsecWidthAuto</c>·<c>StrataMarkScale</c>
        //     셋만 쓴다. 폭은 <b>하나도 안 남는다</b>(되돌려 적으려고 부른 호출인데 헛돌았다).
        //   ②<b>잃는 것이 크다</b> — 라운드 사면으로 만든 도면을 열면 <c>SyncToDocument</c>가
        //     <c>MiterConvex=false</c>로 맞추는데, 여기서 저장하면 그것이 <b>레지스트리에 박힌다</b>.
        //     그러면 다음에 켠 <b>새 도면</b>이 라운드로 시작한다 —
        //     v17.6의 <i>"같은 부지·같은 설정인데 옹벽 6장↔163장"</i> 결함의 뿌리다.
        //   ★<c>GradingSettings.cs</c>가 그 자리에 <i>"저장은 정지옵션 [저장]에서만"</i>이라 적어 두었다.

        log?.AppendLine($"  절단선 폭 — <b>자동</b> 좌{w:0.#}/우{w:0.#}m"
                      + $" (정지 결과에서 가장 먼 점 {far:F1}m + 여유 {MarginM:0.#}m → {StepM:0.#}m 단위 올림"
                      + $" · 잰 점 {nPts}개) · 도면설정에도 적었다" + perNote);
        // ★★[검토 0908] <b>경고는 사람에게 닿아야 한다.</b>
        //   종전엔 <c>log</c>가 <c>null</c>인 자리(검토선 생성·단면)에서는 <b>아예 안 떴고</b>,
        //   떠도 로그 <b>파일</b>에만 갔다. 폭이 폭주하는 유일한 안전망인데 아무도 못 봤다.
        if (w > WarnWideM)
            try
            {
                Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager
                    .MdiActiveDocument?.Editor?.WriteMessage(
                        $"\n[횡단] ⚠절단선 폭이 좌우 {w:0.#}m로 잡혔습니다({WarnWideM:0.#}m 초과)"
                      + " — 사면이 길면 정상이지만 횡단 축척이 작아집니다."
                      + " 도면설정에서 <수동>으로 바꾸면 원하는 폭을 쓸 수 있습니다.");
            }
            catch { }
        return (w, w);
    }

    /// <summary>정지 결과(데이라잇 링)에서 선형까지 <b>직각으로 가장 먼 거리</b>(m).
    /// <para>못 재면 <c>NaN</c>과 이유를 돌려준다 — <b>0으로 눙치지 않는다</b>.</para></summary>
    private static double FarthestOffset(Database db, ObjectId alignId, out int nPts, out string why, out string perNote)
    {
        nPts = 0; why = ""; perNote = "";
        if (db == null || alignId.IsNull) { why = "선형이 없다"; return double.NaN; }
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            if (tr.GetObject(alignId, OpenMode.ForRead) is not CivilDb.Alignment al)
            { tr.Commit(); why = "선형을 못 열었다"; return double.NaN; }

            var regions = GradingBundleStore.TryLoadAll(db, tr, out string bwhy);
            if (regions == null || regions.Count == 0)
            { tr.Commit(); why = "정지 기록이 없다(" + bwhy + ")"; return double.NaN; }

            // ★★[검토 0908 · 알려진 한계] <b>이어서 하기로 쌓은 구역의 옛 사면도 함께 잰다.</b>
            //   뒤 구역이 앞 구역의 사면을 잘라먹어도 <b>앞 구역 번들에는 그때의 링이 그대로 남는다</b>
            //   (<c>GradingBundleStore.LaterFootprints</c>가 그래서 있다). 여기서는 그것을 안 뺀다.
            //   ★<b>틀리는 방향이 안전한 쪽</b>이다 — 폭이 <b>넓게</b> 나오지 좁게 나오지 않으므로
            //   사면이 잘리는 일은 없다. 대신 필요보다 넓어 <b>횡단 축척이 한 단계 작아질 수</b> 있다.
            //   다중 정지에서 횡단이 유난히 작아 보이면 <b>여기부터 보라</b>.
            double far = 0;
            int n = 0;
            // ★★[검토 0908] <b>어느 구역이 폭을 정했는지 적는다.</b>
            //   "다중 정지에서 횡단이 유난히 작으면 여기부터 보라"고 주석에 써 놓아도
            //   <b>볼 근거가 도면에 없으면</b> 헛돈다. 구역마다 최대치를 남긴다.
            var per = new System.Text.StringBuilder();
            for (int ri = 0; ri < regions.Count; ri++)
            {
                double rFar = 0; int rN = 0;
                foreach (var ring in RingsOf(regions[ri]))
                {
                    if (ring == null) continue;
                    foreach (var p in ring)
                    {
                        double st = 0, off = 0;
                        // ★선형 범위 밖이면 예외가 난다 — 그것이 곧 "그 점은 이 선형과 상관없다"는 답이다.
                        try { al.StationOffset(p.X, p.Y, ref st, ref off); }
                        catch { continue; }
                        double a = System.Math.Abs(off);
                        if (a > rFar) rFar = a;
                        rN++;
                    }
                }
                n += rN;
                bool wins = rN > 0 && rFar > far;
                if (wins) far = rFar;
                if (rN > 0) per.Append($" · 구역{ri + 1} {rFar:F1}m({rN}점){(wins ? " ←정함" : "")}");
            }
            perNote = per.ToString();
            tr.Commit();
            nPts = n;
            if (n == 0) { why = "정지 결과가 선형 범위 밖이다"; return double.NaN; }
            return far;
        }
        catch (System.Exception ex) { why = ex.Message; return double.NaN; }
    }

    /// <summary>한 구역이 들고 있는 <b>바깥 링</b>들 — 절토·성토 양쪽.</summary>
    private static IEnumerable<List<Core.Point3>> RingsOf(GradingBundle b)
    {
        if (b == null) yield break;
        if (b.CutFinalRings != null) foreach (var r in b.CutFinalRings) yield return r;
        else if (b.CutFinalRing != null) yield return b.CutFinalRing;
        if (b.FillFinalRings != null) foreach (var r in b.FillFinalRings) yield return r;
        else if (b.FillFinalRing != null) yield return b.FillFinalRing;
    }
}
