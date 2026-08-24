using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace DH.Grading.Civil.Commands;

/// <summary>
/// ★[JACK 0824] <b>지표면 보기(DHVIEW)</b> — 화면에 <b>무엇을 보일지</b>만 바꾼다. 형상은 안 건드린다.
///
/// <para>JACK 실측 피드백으로 규칙이 확정됐다 — "보기"는 지표면을 하나씩 켜고 끄는 게 아니라
/// <b>"지금 무엇을 보고 있는가"를 하나로 딱 정하는 것</b>이다:</para>
/// <list type="table">
///   <item><description><b>전부</b> — 원지반·계획·터파기가 <b>합성된 한 장</b>. 여러 장이 겹쳐 보이는 게 아니다.</description></item>
///   <item><description><b>원지반만</b> — 원지반 하나. 계획·터파기는 물론 <b>그려 둔 선까지</b> 안 보인다.</description></item>
///   <item><description><b>계획지표면</b> — <b>순수</b> 정지면(정지순수_DH). 원지반과 합성된 것이 아니다.</description></item>
///   <item><description><b>터파기</b> — 굴착면 하나. 계획면의 데이라잇·사면선도 안 보인다.</description></item>
/// </list>
///
/// <para><b>선(레이어)도 같이 끈다.</b> 지표면만 숨기면 사면선·소단선·데이라잇·옹벽선이 그대로 남아
/// "터파기만 보자"고 했는데 계획면 선들이 겹쳐 보인다(JACK 스샷). 지표면과 선은 한 벌이다.</para>
///
/// <para>이 전환을 <b>생성 명령 안에 숨기지 않고</b> 별도 버튼으로 뺐다 — 이 저장소가 값비싸게 배운
/// 규칙이 있다: <i>"안 건드리는 것은 되돌리는 것이 아니다."</i> 명령이 중간에 실패하면
/// 계획지표면이 꺼진 채 남아 "사라졌다"로 보인다.</para>
/// </summary>
public sealed class ViewSurfaceCommand
{
    private const string Plan = "정지면_DH";                             // 합성면(원지반+계획)
    private const string PurePlan = SectionCommand.PurePadSurfaceBase;   // 순수 계획면
    private const string Excav = ExcavCommand.SurfName;

    /// <summary>★[JACK 0824] '전부' = 원지반·계획·터파기를 합성한 <b>한 장</b>. 터파기를 만들 때 함께 굽는다.</summary>
    internal const string AllName = "전체면_DH";

    /// <summary>정지·터파기가 그려 두는 선 레이어 — 지표면과 <b>한 벌</b>로 켜고 끈다.</summary>
    private static readonly string[] PlanLayers =
    {
        "DH-사면선-절토", "DH-사면선-성토", "DH-사면선-전환",
        "DH-소단선-절토", "DH-소단선-성토", "DH-소단선-전환",
        "DH-노리선", "DH-데이라잇", "DH-옹벽선",
        "DH-옹벽선-윗선(크레스트)", "DH-옹벽선-아랫선(토우)",
        "DH-정지경계", "DH-클립경계", "DH-FGL", "DH-소단",
    };

    /// <summary>우리가 만드는 지표면 전부 — 여기 없는 것(=원지반)은 이름을 모르므로 따로 다룬다.</summary>
    private static System.Collections.Generic.List<string> OurSurfaces()
    {
        var l = new System.Collections.Generic.List<string>
        {
            Plan, Plan + "이전", PurePlan, PurePlan + "이전", AllName,
            Excav, ExcavCommand.BaseName, "가상절토_DH", "가상성토_DH", "_DH토량임시",
        };
        for (int k = 1; k <= 16; k++) l.Add($"{ExcavCommand.VirtName}{k}");
        for (int i = 1; i <= 8; i++)
            for (int r = 1; r <= 8; r++) l.Add($"터파기_절토복원{i}_{r}_DH");
        return l;
    }

    // ★[JACK 0824] 스플릿 버튼용 — 누르면 **묻지 않고 바로** 바뀐다.
    [CommandMethod("DHVIEWALL")] public void ViewAll() => Apply("A");
    [CommandMethod("DHVIEWG")] public void ViewGround() => Apply("G");
    [CommandMethod("DHVIEWP")] public void ViewPlan() => Apply("P");
    [CommandMethod("DHVIEWE")] public void ViewExcav() => Apply("E");

    [CommandMethod("DHVIEW")]
    public void Run()
    {
        Document doc = AcadApp.DocumentManager.MdiActiveDocument;
        if (doc == null) return;
        Editor ed = doc.Editor;

        var (hasPlan, hasExc) = WhatExists(doc.Database);
        var pko = new PromptKeywordOptions(
            $"\n무엇을 볼까요? 〈계획지표면{(hasPlan ? "" : "(없음)")} · 터파기{(hasExc ? "" : "(없음)")}〉");
        pko.Keywords.Add("A", "A", "전부(A)");
        pko.Keywords.Add("G", "G", "원지반만(G)");
        pko.Keywords.Add("P", "P", "계획지표면만(P)");
        pko.Keywords.Add("E", "E", "터파기만(E)");
        pko.AllowNone = true;
        pko.Keywords.Default = "A";
        var r = ed.GetKeywords(pko);
        if (r.Status != PromptStatus.OK && r.Status != PromptStatus.None) return;
        Apply(r.Status == PromptStatus.None ? "A" : (r.StringResult ?? "A").Trim().ToUpperInvariant());
    }

    /// <summary>지금 도면에 계획지표면·터파기가 있는가.</summary>
    private static (bool Plan, bool Excav) WhatExists(Database db)
    {
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            bool a = GradingBuilder.SurfaceExistsByBaseName(tr, PurePlan)
                  || GradingBuilder.SurfaceExistsByBaseName(tr, Plan);
            bool b = GradingBuilder.SurfaceExistsByBaseName(tr, Excav);
            tr.Commit();
            return (a, b);
        }
        catch { return (false, false); }
    }

    /// <summary>고른 보기를 적용한다 — 프롬프트 없이. 지표면 <b>하나</b>만 남기고 선 레이어도 맞춘다.</summary>
    private static void Apply(string kw)
    {
        Document doc = AcadApp.DocumentManager.MdiActiveDocument;
        if (doc == null) return;
        Editor ed = doc.Editor;
        Database db = doc.Database;
        var (hasPlan, hasExc) = WhatExists(db);

        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            string? keep;       // 우리 지표면 중 이것 하나만 보인다(null = 우리 것 전부 숨김)
            bool showGround;    // 원지반을 보일까
            bool lines;         // 정지 선 레이어를 보일까
            string msg;

            switch (kw)
            {
                case "G":
                    // 원지반만 — 우리 산출물은 지표면도 선도 전부 끈다.
                    keep = null; showGround = true; lines = false;
                    msg = "원지반만";
                    break;

                case "P":
                    // ★[JACK] **순수** 계획면. 원지반과 합성된 것이 아니다 → 원지반도 끈다.
                    if (!hasPlan) { ed.WriteMessage("\n[보기] 계획지표면이 아직 없습니다."); tr.Commit(); return; }
                    keep = GradingBuilder.SurfaceExistsByBaseName(tr, PurePlan) ? PurePlan : Plan;
                    showGround = false; lines = true;
                    msg = keep == PurePlan ? "계획지표면만(순수)" : "계획지표면만(순수면이 없어 합성면으로 물러남)";
                    break;

                case "E":
                    // ★[JACK] 굴착면 하나 — 계획면 데이라잇·사면선도 안 보인다.
                    if (!hasExc) { ed.WriteMessage("\n[보기] 터파기 지표면이 아직 없습니다."); tr.Commit(); return; }
                    keep = Excav; showGround = false; lines = false;
                    msg = "터파기만";
                    break;

                default:
                    // ★[JACK] 전부 = **합성된 한 장**. 터파기가 있으면 전체면, 없으면 정지면(원지반+계획).
                    //   둘 다 없으면 우리 것을 다 끄고 원지반만 보인다.
                    if (GradingBuilder.SurfaceExistsByBaseName(tr, AllName))
                    { keep = AllName; showGround = false; msg = "전부(원지반+계획+터파기 합성 한 장)"; }
                    else if (GradingBuilder.SurfaceExistsByBaseName(tr, Plan))
                    { keep = Plan; showGround = false; msg = "전부(원지반+계획 합성 한 장 — 터파기 없음)"; }
                    else
                    { keep = null; showGround = true; msg = "전부(원지반뿐)"; }
                    lines = true;
                    break;
            }

            // 원지반(=우리 것이 아닌 지표면)은 IsolateSurfaces로 한 번에 처리한다.
            //   keepBaseName에 없는 이름을 주면 **전부 숨김**이 된다.
            GradingBuilder.IsolateSurfaces(tr, showGround ? null : " 없는이름 ");
            foreach (var nm in OurSurfaces())
                GradingBuilder.SetSurfaceVisible(tr, nm, keep != null && nm == keep);

            SetLayers(db, tr, lines);
            tr.Commit();
            ed.WriteMessage($"\n[보기] {msg}");
        }
        catch (System.Exception ex) { ed.WriteMessage("\n[보기 오류] " + ex.Message); }
    }

    /// <summary>정지·터파기가 그린 선 레이어를 한꺼번에 켜고 끈다.
    /// <para>지표면만 숨기면 사면선·데이라잇이 그대로 남아 "터파기만 보자"가 안 된다(JACK 스샷).</para></summary>
    private static void SetLayers(Database db, Transaction tr, bool on)
    {
        try
        {
            var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            foreach (var nm in PlanLayers)
            {
                if (!lt.Has(nm)) continue;
                try
                {
                    var lr = (LayerTableRecord)tr.GetObject(lt[nm], OpenMode.ForRead);
                    if (lr.IsOff == !on) continue;      // 이미 같으면 쓰지 않는다(쓰는 행위 자체가 '수정'이다)
                    var lw = (LayerTableRecord)tr.GetObject(lt[nm], OpenMode.ForWrite);
                    lw.IsOff = !on;                     // 현재 레이어는 끌 수 없으므로 실패는 조용히 넘긴다
                }
                catch { }
            }
        }
        catch { }
    }

    /// <summary>★[JACK 0824] 생성 명령이 쓰는 <b>되돌릴 수 있는</b> 화면 전환.
    /// <para>명령이 알아서 껐다 켜되 <b>끝날 때 무조건 복원</b>한다(실패·Esc 포함).</para></summary>
    internal static System.IDisposable Focus(Database db, string? keepBaseName) => new FocusScope(db, keepBaseName);

    private sealed class FocusScope : System.IDisposable
    {
        private readonly Database _db;
        private readonly System.Collections.Generic.Dictionary<ObjectId, bool> _was = new();

        public FocusScope(Database db, string? keep)
        {
            _db = db;
            try
            {
                using var tr = db.TransactionManager.StartTransaction();
                var civilDoc = Autodesk.Civil.ApplicationServices.CivilApplication.ActiveDocument;
                foreach (ObjectId sid in civilDoc.GetSurfaceIds())
                {
                    if (tr.GetObject(sid, OpenMode.ForRead) is not Entity e) continue;
                    _was[sid] = e.Visible;   // 지금 상태를 그대로 떠 둔다 — 되돌릴 근거는 이것뿐이다
                }
                GradingBuilder.IsolateSurfaces(tr, keep);
                tr.Commit();
            }
            catch { }
        }

        public void Dispose()
        {
            try
            {
                using var tr = _db.TransactionManager.StartTransaction();
                foreach (var kv in _was)
                {
                    try
                    {
                        if (tr.GetObject(kv.Key, OpenMode.ForRead) is not Entity er) continue;
                        if (er.Visible == kv.Value) continue;   // 같으면 쓰지 않는다
                        var ew = (Entity)tr.GetObject(kv.Key, OpenMode.ForWrite);
                        ew.Visible = kv.Value;
                    }
                    catch { }
                }
                tr.Commit();
            }
            catch { }
        }
    }
}
