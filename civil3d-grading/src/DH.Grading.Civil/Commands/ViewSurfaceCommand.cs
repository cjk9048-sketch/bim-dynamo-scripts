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
    internal static (bool Plan, bool Excav) WhatExists(Database db)
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

    /// <summary>★[JACK 0825] 다른 명령이 끝나고 <b>전부 보기로 돌려놓을</b> 때 쓴다.
    /// <para>JACK: <i>"터파기 기능을 썼을 때 완료가 되면 전부 보기 상태로 복원되게 해줘."</i></para></summary>
    internal static void ShowAll() => Apply("A");

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
            // ★★[JACK 0825] <b>여러 장을 함께 보일 수 있어야 한다.</b>
            //   '전부'가 합성면 한 장이던 것을 세 장(원지반·계획·터파기)으로 바꾸면서 집합이 됐다.
            var keeps = new System.Collections.Generic.HashSet<string>(System.StringComparer.Ordinal);
            string? keep;       // 한 장만 보이는 보기(G/P/E)에서 쓴다
            bool showGround;    // 원지반을 보일까
            bool lines;         // 정지 선 레이어를 보일까
            string msg;

            // ★★[JACK 0825 최종 규칙] 무엇을 보일지는 JACK이 이렇게 정했다:
            //   전부   = 계획+원지반 <b>합성</b>(정지면_DH) + 터파기면 <b>별도 한 장</b>
            //   원지반 = 원지반만
            //   계획   = 계획+원지반 <b>합성</b>(순수면이 아니다 — 부지 밖이 비어 이상해 보인다)
            //   터파기 = 터파기면만
            //
            //   ※ 종전의 <c>전체면_DH</c>(셋을 한 장으로 Paste)는 <b>쓰지 않는다</b>.
            //     TIN은 한 (X,Y)에 점을 하나만 둘 수 있어 수직 옹벽에서 위아래 점이 서로를 밀어내고,
            //     그래서 벽면이 찢어진 채 "최종 형상"처럼 보였다(JACK 3D 스샷).
            string PlanPick() => GradingBuilder.SurfaceExistsByBaseName(tr, Plan) ? Plan : PurePlan;

            switch (kw)
            {
                case "G":
                    // 원지반만 — 우리 산출물은 지표면도 선도 전부 끈다.
                    keep = null; showGround = true; lines = false;
                    msg = "원지반만";
                    break;

                case "P":
                    if (!hasPlan) { ed.WriteMessage("\n[보기] 계획지표면이 아직 없습니다."); tr.Commit(); return; }
                    keeps.Add(PlanPick());          // 합성면 — 원지반을 이미 품고 있다
                    showGround = false; lines = false;
                    keep = null;
                    msg = $"계획지표면({PlanPick()} — 원지반 합성)";
                    break;

                case "E":
                    if (!hasExc) { ed.WriteMessage("\n[보기] 터파기 지표면이 아직 없습니다."); tr.Commit(); return; }
                    keeps.Add(Excav); showGround = false; lines = false;
                    keep = null;
                    msg = "터파기만";
                    break;

                default:
                    if (hasPlan) keeps.Add(PlanPick());
                    if (hasExc) keeps.Add(Excav);
                    // 합성면이 원지반을 품으므로 따로 켜지 않는다. 계획면이 없을 때만 원지반을 켠다.
                    showGround = !hasPlan;
                    lines = true; keep = null;
                    msg = keeps.Count > 0 ? $"전부({string.Join(" + ", keeps)})" : "전부(원지반뿐)";
                    break;
            }

            // 원지반(=우리 것이 아닌 지표면)은 IsolateSurfaces로 한 번에 처리한다.
            //   keepBaseName에 없는 이름을 주면 **전부 숨김**이 된다.
            GradingBuilder.IsolateSurfaces(tr, showGround ? null : " 없는이름 ");
            if (keep != null) keeps.Add(keep);
            foreach (var nm in OurSurfaces())
                GradingBuilder.SetSurfaceVisible(tr, nm, keeps.Contains(nm));

            SetLayers(db, tr, lines);
            tr.Commit();
            ed.WriteMessage($"\n[보기] {msg}");
        }
        catch (System.Exception ex) { ed.WriteMessage("\n[보기 오류] " + ex.Message); }
    }

    /// <summary>우리가 끈 레이어 이름을 적어 두는 자리 — 전부 보기에서 <b>이것만</b> 되켠다.</summary>
    private const string OffDictName = "DH_GRADING";
    private const string OffRecName = "VIEW_OFF";

    /// <summary>★★[JACK 0825] <b>지표면만 남기고 나머지 레이어를 전부 끈다.</b>
    ///
    /// <para>JACK: <i>"전부 보기 외에 나머지 뷰에서는 기존의 계획선이나 터파기 선도 안 보이게
    /// 모든 레이어를 꺼. 순수하게 지표면만 보이게 해줘."</i></para>
    ///
    /// <para>종전엔 <b>우리가 아는 레이어 목록</b>(<see cref="PlanLayers"/>)만 껐다. 그래서 그 목록에
    /// 없는 선 — 남이 그린 것이든 우리가 나중에 늘린 것이든 — 은 그대로 남았다.
    /// 목록을 늘리는 방식은 <b>새 레이어가 생길 때마다 또 새는</b> 구조라 근본이 못 된다.</para>
    ///
    /// <para>→ <b>지표면이 쓰는 레이어만 지키고 나머지는 전부 끈다.</b> 지표면의 표시 여부는
    /// <c>SetSurfaceVisible</c>이 따로 쥐고 있으므로, 레이어를 켜 둬도 숨긴 지표면은 안 보인다.</para>
    ///
    /// <para><b>남의 도면을 망가뜨리지 않는다.</b> ①원래 꺼져 있던 레이어는 손대지 않고
    /// ②우리가 끈 것만 이름으로 적어 두었다가 ③전부 보기에서 <b>그것만</b> 되켠다.
    /// 적어 둔 목록은 <b>누적</b>한다 — 원지반만 → 터파기만 처럼 연달아 눌러도 첫 번째 기록이 안 지워진다.</para></summary>
    private static void SetLayers(Database db, Transaction tr, bool on)
    {
        try
        {
            var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);

            if (on)
            {
                // ── 되돌리기: 우리가 끈 것만 켠다(원래 꺼져 있던 남의 레이어는 그대로 둔다).
                foreach (var nm in LoadOff(db, tr))
                {
                    if (!lt.Has(nm)) continue;
                    try
                    {
                        var lr = (LayerTableRecord)tr.GetObject(lt[nm], OpenMode.ForRead);
                        if (!lr.IsOff) continue;
                        ((LayerTableRecord)tr.GetObject(lt[nm], OpenMode.ForWrite)).IsOff = false;
                    }
                    catch { }
                }
                SaveOff(db, tr, new System.Collections.Generic.List<string>());
                return;
            }

            // ── 끄기: <b>우리가 만든 레이어만</b>.
            //
            //   ★★[JACK 0825] 종전 이 자리는 "지표면 레이어만 빼고 <b>전부</b>"였다 — 그러면
            //   <b>아무것도 안 보인다</b>(JACK 실측). Civil 지표면은 객체의 레이어와 별개로
            //   <b>스타일이 삼각형·등고선을 그리는 레이어</b>를 따로 쓰기 때문이다.
            //   객체 레이어를 지켜도 그 컴포넌트 레이어가 꺼지면 지표면 자체가 사라진다.
            //   도곽·종단뷰·남의 도면 레이어까지 끄는 것도 과했다.
            //
            //   JACK 요구("계획선이나 터파기 선도 안 보이게")의 뜻은 <b>우리가 그린 선</b>을 다 끄라는 것이다.
            //   그건 전부 <c>DH-</c>로 시작한다 — 목록을 손으로 관리하지 않아도 새 레이어가 저절로 들어온다.
            var turned = LoadOff(db, tr);                 // 누적 — 연달아 눌러도 첫 기록을 안 잃는다
            var already = new System.Collections.Generic.HashSet<string>(turned);
            foreach (ObjectId lid in lt)
            {
                try
                {
                    var lr = (LayerTableRecord)tr.GetObject(lid, OpenMode.ForRead);
                    bool ours = lr.Name.StartsWith("DH-", System.StringComparison.Ordinal)
                             || System.Array.IndexOf(PlanLayers, lr.Name) >= 0;
                    // ★[JACK 0826] <b>종단 레이어는 건드리지 않는다</b> — 이 명령은 평면 보기를 다룬다.
                    //   종단도의 옹벽·가시설 막대와 터파기 종단선이 보기 전환 때 사라지면 안 된다.
                    if (lr.Name.StartsWith("DH-종단-", System.StringComparison.Ordinal)) continue;
                    if (!ours) continue;
                    if (lr.IsOff) continue;               // 이미 꺼져 있다 = 우리가 끈 게 아니다
                    if (lid == db.Clayer) continue;       // 현재 레이어는 끄지 않는다
                    ((LayerTableRecord)tr.GetObject(lid, OpenMode.ForWrite)).IsOff = true;
                    if (already.Add(lr.Name)) turned.Add(lr.Name);
                }
                catch { }
            }
            SaveOff(db, tr, turned);
        }
        catch { }
    }

    private static System.Collections.Generic.List<string> LoadOff(Database db, Transaction tr)
    {
        var list = new System.Collections.Generic.List<string>();
        try
        {
            var nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);
            if (!nod.Contains(OffDictName)) return list;
            var d = (DBDictionary)tr.GetObject(nod.GetAt(OffDictName), OpenMode.ForRead);
            if (!d.Contains(OffRecName)) return list;
            var xr = (Xrecord)tr.GetObject(d.GetAt(OffRecName), OpenMode.ForRead);
            if (xr.Data == null) return list;
            foreach (TypedValue tv in xr.Data)
                if (tv.TypeCode == (int)DxfCode.Text && tv.Value is string nm && nm.Length > 0) list.Add(nm);
        }
        catch { }
        return list;
    }

    private static void SaveOff(Database db, Transaction tr,
                                System.Collections.Generic.IReadOnlyList<string> names)
    {
        try
        {
            var nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForWrite);
            DBDictionary d;
            if (nod.Contains(OffDictName)) d = (DBDictionary)tr.GetObject(nod.GetAt(OffDictName), OpenMode.ForWrite);
            else { d = new DBDictionary(); nod.SetAt(OffDictName, d); tr.AddNewlyCreatedDBObject(d, true); }

            var vals = new System.Collections.Generic.List<TypedValue>();
            foreach (var nm in names) vals.Add(new TypedValue((int)DxfCode.Text, nm));
            var xr = new Xrecord { Data = new ResultBuffer(vals.ToArray()) };
            if (d.Contains(OffRecName)) d.Remove(OffRecName);
            d.SetAt(OffRecName, xr);
            tr.AddNewlyCreatedDBObject(xr, true);
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
