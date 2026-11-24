using Autodesk.AutoCAD.Runtime;
using DH.Grading.Core;

namespace DH.Grading.Civil.Commands;

/// <summary>★★[JACK 1124 «측구 설계 시 집수면적 같은 걸 프로그램에서 기본적으로 제시 · 3D 뷰에서 빗물 기능을 이용해서 설계»] [자동 집수] —
/// 빗물 셈으로 측구마다 집수 구역 · 넓이 · C(토지 이용) · 구간 누가 몫을 세어 기록에(사람이 친 값은 그대로) · 평면 구역(레이어 DH-U형측구-집수)을 다시 그림.
/// [측구 생성]도 끝에 같은 셈(<see cref="CatchAfterBuild"/>).</summary>
public sealed partial class UDitchCommand
{
    [CommandMethod("DHUDITCHCATCH")]
    public void PalCatch() => PalRun("자동 집수", (c, m) =>
    {
        if (c.Rec.Ditches.Count == 0) return ("", "측구 없음 → [선 고르기]");
        var r = CatchRun(c);
        return r.Why.Length > 0 ? ("", r.Text) : (r.Text + CatchList(c), "");
    });

    /// <summary>★[JACK 1124 «측구 창에서 집수구역 범위 껐다 켰다 하는 단추»] 평면 집수 구역 레이어(DH-U형측구-집수) 켜기 ↔ 끄기 — 도면은 안 고침(레이어 표시만).</summary>
    [CommandMethod("DHUDITCHCATCHVIS")]
    public void CatchVis()
    {
        var doc = Autodesk.AutoCAD.ApplicationServices.Core.Application.DocumentManager.MdiActiveDocument;
        if (doc == null) return;
        var db = doc.Database;
        try
        {
            using (doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var lt = (Autodesk.AutoCAD.DatabaseServices.LayerTable)tr.GetObject(db.LayerTableId, Autodesk.AutoCAD.DatabaseServices.OpenMode.ForRead);
                if (!lt.Has(UDitchCatchStore.Layer)) { doc.Editor.WriteMessage("\n[U형 측구] 집수 구역이 없습니다 — [자동 집수]부터"); tr.Commit(); return; }
                var ltr = (Autodesk.AutoCAD.DatabaseServices.LayerTableRecord)tr.GetObject(lt[UDitchCatchStore.Layer], Autodesk.AutoCAD.DatabaseServices.OpenMode.ForWrite);
                if (ltr.IsFrozen) ltr.IsFrozen = false;
                ltr.IsOff = !ltr.IsOff;
                doc.Editor.WriteMessage("\n[U형 측구] 집수 구역 " + (ltr.IsOff ? "숨김" : "보임"));
                tr.Commit();
            }
            doc.Editor.Regen();
        }
        catch (System.Exception ex) { doc.Editor.WriteMessage("\n[U형 측구] 집수 구역 표시 바꾸기 실패 — " + ex.Message); }
    }

    /// <summary>[측구 생성] 끝 — 집수 셈(못 세면 알림 없이 로그만). 돌려줌 = 알림 조각(빈 글 = 안 셈).</summary>
    static string CatchAfterBuild(Ctx c)
    {
        try
        {
            var r = CatchRun(c);
            return r.Why.Length > 0 ? "" : string.Format(System.Globalization.CultureInfo.InvariantCulture, "집수 자동 {0}/{1}", r.Zones, r.Ditches);
        }
        catch (System.Exception ex) { c.Log.AppendLine("  ⚠집수 셈 실패 — " + ex.Message); return ""; }
    }

    static UDitchCatchStore.Run CatchRun(Ctx c)
    {
        c.Log.AppendLine("  자동 집수:");
        var r = UDitchCatchStore.Compute(c.Db, c.Rec, c.S, c.Log);
        if (r.Why.Length == 0) SaveRec(c);
        return r;
    }

    /// <summary>알림 꼬리 — 측구마다 «D-001 0.123 ha(자동)» (넷까지).</summary>
    static string CatchList(Ctx c)
    {
        var xs = c.Rec.Ditches.Where(d => d.Auto != null).Take(4)
                   .Select(d => string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0} {1:0.####} ha({2})", d.Id, d.AreaHa, d.AreaAuto ? "자동" : "입력")).ToList();
        return xs.Count == 0 ? "" : " · " + string.Join(" · ", xs) + (c.Rec.Ditches.Count > 4 ? " …" : "");
    }
}
