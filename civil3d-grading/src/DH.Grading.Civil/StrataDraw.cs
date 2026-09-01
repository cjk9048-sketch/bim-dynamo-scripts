using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using DH.Grading.Core;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;
using CivilApp = Autodesk.Civil.ApplicationServices;
using CivilStyles = Autodesk.Civil.DatabaseServices.Styles;
using CivilDb = Autodesk.Civil.DatabaseServices;

namespace DH.Grading.Civil;

/// <summary>★★★[JACK 0828] 지층 도킹바의 <b>도면 쪽 일</b> — 표식·지반고·지층면.</summary>
public static class StrataDraw
{
    /// <summary>지층 지표면 이름 앞머리 — 지우고 다시 만들 때 이걸로 찾는다.</summary>
    internal const string SurfPrefix = "DH_지층_";

    /// <summary>지하수위 지표면 이름.</summary>
    internal const string WaterSurfName = "DH_지하수위";

    /// <summary>★[JACK 0828 "각 지층과 지하수위의 각층의 좌측 선 위에 해당 층이름을 적어줘"]
    /// 지표면 이름(<c>DH_지층_3_풍화암</c>)에서 <b>화면에 쓸 이름</b>만 뽑는다 → <c>풍화암</c>.
    /// <para>도면에 <c>DH_지층_3_</c> 같은 내부 이름을 그대로 쓰면 읽는 사람에게 군더더기다.</para></summary>
    internal static string ShortName(string surfName)
    {
        if (string.IsNullOrEmpty(surfName)) return "";
        if (surfName == WaterSurfName) return "지하수위";
        if (!surfName.StartsWith(SurfPrefix, System.StringComparison.Ordinal)) return surfName;
        string rest = surfName.Substring(SurfPrefix.Length);
        int us = rest.IndexOf('_');
        return us >= 0 && us + 1 < rest.Length ? rest.Substring(us + 1) : rest;
    }

    /// <summary>보링공 표식의 <b>본디 크기</b>(도면 단위 m) — 동그라미 반지름. 블록이 이 크기로 만들어진다.
    /// <para>★[JACK 0828 "BH점 표시가 너무 커. 지금 크기에서 70% 정도로 해 줘"] 2.0 → <b>1.4m</b>.</para>
    /// <para>여기서 더 키우고 줄이는 것은 <see cref="MarkScale"/>이 한다 — 부지마다 알맞은 크기가 다르다.</para></summary>
    private const double MarkR = 1.4;

    /// <summary>★★[JACK 0831 "BH점 크기를 조절할 수 있는 바 넣어(음량조절처럼)"]
    /// <b>블록은 그대로 두고 참조만 늘리고 줄인다.</b>
    /// <para>블록 자체를 다시 만들면 그 블록을 쓰는 <b>모든 도면 객체</b>가 한꺼번에 바뀌고
    /// 되돌리기도 어렵다. 참조 배율은 표식 하나하나에 걸리므로 안전하고 빠르다.</para></summary>
    internal static double MarkScale = 1.0;

    [CommandMethod("DHSTRATAPICK", CommandFlags.Modal)]
    public static void PickOne()
    {
        var doc = AcadApp.DocumentManager.MdiActiveDocument;
        if (doc == null) return;
        var panel = StrataPanel.Current;
        if (panel == null) { doc.Editor.WriteMessage("\n[지층구성] 창이 안 열려 있습니다 — DHSTRATA를 먼저 치세요."); return; }

        // ★한 번에 하나만 받고 돌아간다 — 여러 번 찍으려면 단추를 다시 누른다.
        //   반복 루프를 여기 두면 도킹바가 그동안 잠긴다.
        var ppo = new PromptPointOptions("\n[지층구성] 시추 위치를 클릭 (Esc=그만): ") { AllowNone = true };
        var pr = doc.Editor.GetPoint(ppo);
        if (pr.Status != PromptStatus.OK) return;
        var p = pr.Value.TransformBy(doc.Editor.CurrentUserCoordinateSystem);
        panel.AddBore(p);
    }

    /// <summary>★★★[JACK 0828] <b>도킹바에서 도면을 건드리려면 문서를 잠가야 한다.</b>
    ///
    /// <para>명령(<c>CommandMethod</c>) 안에서는 AutoCAD가 알아서 잠가 주지만,
    /// <b>도킹바의 단추·표 편집은 명령이 아니다</b> — 화면 스레드에서 바로 들어온다.
    /// 그대로 트랜잭션을 열면 <c>eLockViolation</c>이 난다.</para>
    ///
    /// <para>JACK 요구가 <i>"XY를 고치면 실시간으로 블록이 이동하고 지반고가 갱신된다"</i>이므로
    /// 그 길은 <b>반드시 도킹바에서</b> 들어온다 — 잠그지 않으면 <b>손대는 순간 터진다</b>.</para>
    ///
    /// <para>잠금은 겹쳐도 안전하다(명령 안에서 또 잠가도 된다) —
    /// 그래서 <b>도면을 만지는 자리마다 무조건</b> 두른다. 어디서 불렸는지 따지지 않는다.</para></summary>
    private static IDisposable Lock()
    {
        try { return AcadApp.DocumentManager.MdiActiveDocument?.LockDocument(); }
        catch { return null; }
    }

    /// <summary>★ 그 자리의 <b>원지반 표고</b>를 읽는다 — 사람이 안 친다(JACK 확정).
    /// <para>원지반 밖이면 <c>NaN</c>이다. <b>0으로 채우지 않는다</b> — 모르는 것과 0은 다르다.</para></summary>
    internal static void ReadGl(BoreRow b)
    {
        b.Gl = double.NaN;
        try
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            using var dl = Lock();
            var db = doc.Database;
            var cdoc = CivilApp.CivilApplication.ActiveDocument;
            var surfs = Commands.SectionCommand.FindSurfaces(db, cdoc);
            ObjectId gid = ObjectId.Null;
            foreach (var s in surfs) if (s.Label == "원지반") { gid = s.SurfId; break; }
            if (gid.IsNull) return;

            using var tr = db.TransactionManager.StartTransaction();
            if (tr.GetObject(gid, OpenMode.ForRead) is CivilDb.TinSurface ts)
            {
                // ★[기억] <c>FindElevationAtXY</c>는 지표면 <b>밖에서 예외</b>를 던진다 — 그것이 곧 "밖"이라는 답이다.
                try { b.Gl = ts.FindElevationAtXY(b.X, b.Y); } catch { b.Gl = double.NaN; }
            }
            tr.Commit();
        }
        catch { }
    }

    /// <summary>보링공 표식을 그린다 — <b>동그라미 안에 이름</b>(JACK 요구).</summary>
    internal static void DrawMark(BoreRow b)
    {
        try
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            using var dl = Lock();
            var db = doc.Database;
            using var tr = db.TransactionManager.StartTransaction();
            var ms = (BlockTableRecord)tr.GetObject(
                SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
            var lay = Commands.SectionCommand.EnsureLayer(db, tr, StrataPalette.MarkLayer, 2);
            var kst = Commands.ImportGisCommand.EnsureKoreanTextStyle(db, tr);
            DrawOne(db, tr, ms, lay, kst, b);
            tr.Commit();
        }
        catch { }
    }

    /// <summary>★★[JACK 0831] <b>크기를 바꾸면 다 다시 그린다 — 한 트랜잭션에서.</b>
    ///
    /// <para><b>왜 배율만 고쳐 두지 않는가.</b> 표식은 동그라미와 이름 글자가 든 블록인데,
    /// 이름 글자(<c>AttributeReference</c>)는 <b>붙일 때의 변환</b>으로 자리와 크기가 정해진다 —
    /// 나중에 참조 배율만 바꾸면 <b>동그라미만 커지고 글자는 그대로</b> 있다.
    /// JACK이 0828에 겪은 "원만 옮겨지고 BH1 글씨는 제자리" 와 <b>같은 종류</b>다.</para>
    ///
    /// <para>→ 지우고 새로 그린다. 글자도 새 변환으로 다시 붙으므로 <b>어긋날 자리가 없다</b>.
    /// 공은 많아야 수십 개고 트랜잭션은 하나라 느리지 않다.</para></summary>
    internal static int Redraw(System.Collections.Generic.IEnumerable<BoreRow> rows)
    {
        if (rows == null) return 0;
        int n = 0;
        try
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return 0;
            using var dl = Lock();
            var db = doc.Database;
            using var tr = db.TransactionManager.StartTransaction();
            var ms = (BlockTableRecord)tr.GetObject(
                SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
            var lay = Commands.SectionCommand.EnsureLayer(db, tr, StrataPalette.MarkLayer, 2);
            var kst = Commands.ImportGisCommand.EnsureKoreanTextStyle(db, tr);
            foreach (var b in rows)
            {
                if (b == null) continue;
                try
                {
                    if (!b.MarkId.IsNull && b.MarkId.Database == db
                        && tr.GetObject(b.MarkId, OpenMode.ForWrite, false, true) is Entity old && !old.IsErased)
                        old.Erase();
                }
                catch { }
                b.MarkId = ObjectId.Null;
                DrawOne(db, tr, ms, lay, kst, b);
                n++;
            }
            tr.Commit();
        }
        catch (System.Exception ex)
        { try { DiagLog.Append("\n  시추 표식 다시 그리기 실패 — " + ex.Message); } catch { } }
        return n;
    }

    /// <summary>표식 하나를 <b>이미 열린 트랜잭션 안에</b> 그린다.
    /// <para>★한 자리에서만 그린다 — 찍기·붙여넣기·크기 바꾸기가 따로 그리면
    /// 한쪽만 고쳐지는 §50 함정에 빠진다.</para></summary>
    private static void DrawOne(Database db, Transaction tr, BlockTableRecord ms,
                                ObjectId lay, ObjectId kst, BoreRow b)
    {
        // 동그라미와 글자를 한 덩이로 묶는다 — 옮길 때 함께 따라오게.
        var grp = new BlockReference(new Point3d(b.X, b.Y, 0), EnsureMarkBlock(db, tr, lay, kst))
        { LayerId = lay };
        // ★크기 조절 — 블록은 그대로, 이 표식만 늘리고 줄인다.
        double k = MarkScale > 1e-6 ? MarkScale : 1.0;
        try { grp.ScaleFactors = new Scale3d(k); } catch { }
        ms.AppendEntity(grp); tr.AddNewlyCreatedDBObject(grp, true);

        // 이름은 속성으로 넣는다 — 블록 하나로 BH1·BH2를 다 쓴다.
        //   ★<c>BlockTransform</c>을 <b>배율을 건 뒤에</b> 읽어야 글자도 같이 커진다.
        foreach (ObjectId aid in ((BlockTableRecord)tr.GetObject(grp.BlockTableRecord, OpenMode.ForRead)))
        {
            if (tr.GetObject(aid, OpenMode.ForRead) is not AttributeDefinition ad || ad.Constant) continue;
            var ar = new AttributeReference();
            ar.SetAttributeFromBlock(ad, grp.BlockTransform);
            ar.TextString = b.Name;
            // ★★★[JACK 0831 "글씨가 원 테두리 밖에 넘어가니깐 글씨 크기도 조정하고.
            //   크기 바로 바꿔도 계속 넘어가"]
            //
            //   <b>원인 둘.</b> ① 글자 높이를 원 반지름의 <b>90%</b>로 박아 뒀다 —
            //   원 지름이 2.8인데 <c>BH1</c> 가로가 2.6이라 이미 꽉 찼고 <c>BH10</c>이면 넘친다.
            //   ② 크기 바는 <b>원과 글자를 같이</b> 키우므로 비율이 안 변한다 — 그래서 아무리 끌어도 그대로였다.
            //
            //   → <b>이름 길이를 보고 그때그때 높이를 낸다.</b> 원 안에 글자 상자가 들어가려면
            //     <c>(W/2)² + (H/2)² ≤ R²</c>이고, 글자 상자 가로는 <c>글자수 × 0.7 × H</c>쯤이다.
            //     여백 15%를 두고 뒤집어 풀면 아래 식이다. <c>BH1</c>이든 <c>BH120</c>이든 안 넘친다.
            try
            {
                double nch = System.Math.Max(1, (b.Name ?? "").Length);
                double wRatio = nch * 0.7;                                   // 글자 높이 대비 가로 배수
                double denom = System.Math.Sqrt(wRatio * wRatio / 4.0 + 0.25);
                double h = MarkR * 0.85 / denom;                             // 0.85 = 테두리 여백
                ar.Height = h * k;                                           // 블록 배율만큼 같이 커진다
                // 가운데 맞춤이라 <c>AlignmentPoint</c>가 자리를 정한다 — 원 한가운데로 다시 잡는다.
                ar.AlignmentPoint = grp.Position;
                ar.AdjustAlignment(db);
            }
            catch { }
            grp.AttributeCollection.AppendAttribute(ar);
            tr.AddNewlyCreatedDBObject(ar, true);
        }
        b.MarkId = grp.ObjectId;
    }

    /// <summary>★ 표에서 좌표를 고치면 <b>표식이 그 자리로 옮겨간다</b>(JACK 요구).</summary>
    /// <summary>★★★[JACK 0828 검토] <b>표식이 이 도면 것인지 먼저 본다.</b>
    /// <para>도킹바는 <c>static</c>이라 도면을 오가도 표가 그대로 남는다.
    /// A에서 찍고 B로 가서 XY를 고치면 <c>MarkId</c>가 <b>남의 Database</b> 것이라
    /// <c>GetObject</c>가 <c>eWrongDatabase</c>로 던지고 <c>catch { }</c>가 삼킨다 —
    /// 표식은 안 움직이는데 <b>아무 말이 없다</b>. 그 상태로 [확인]을 누르면
    /// <b>B 도면에 A의 보링공으로</b> 지층을 만든다. 좌표가 겹치면 그럴듯한 값이 나와 더 못 알아챈다.</para>
    /// <para><c>MarkId.IsNull</c>은 false라 관문이 못 된다 — <b>Database를 견줘야</b> 한다.</para></summary>
    private static bool SameDb(ObjectId id, Database db) =>
        !id.IsNull && db != null && id.Database == db;

    internal static void MoveMark(BoreRow b)
    {
        try
        {
            var db = AcadApp.DocumentManager.MdiActiveDocument?.Database;
            if (db == null) return;
            // 이 도면 것이 아니면 <b>여기에 새로 그린다</b> — 조용히 아무 일도 안 하는 것보다 낫다.
            if (!b.MarkId.IsNull && !SameDb(b.MarkId, db))
            {
                try { DiagLog.Append($"\n  {b.Name} 표식이 <b>다른 도면</b> 것이다 — 이 도면에 새로 그린다"); } catch { }
                b.MarkId = ObjectId.Null;
            }
            if (b.MarkId.IsNull) { DrawMark(b); return; }
            using var dl = Lock();
            using var tr = db.TransactionManager.StartTransaction();
            if (tr.GetObject(b.MarkId, OpenMode.ForWrite, false, true) is BlockReference br && !br.IsErased)
            {
                // ★★★[JACK 0828 "XY로 쳐서 바꾸면 원 객체만 옮겨지고 BH1 글씨는 그 자리 그대로 있어"]
                //   <b>속성 글씨는 블록을 안 따라간다.</b> <c>AttributeReference</c>는 블록 안에 들어 있어도
                //   <b>제 좌표를 따로</b> 가진다 — <c>Position</c>만 바꾸면 동그라미만 옮겨지고 글씨는 남는다.
                //   → <b>움직인 만큼 속성도 같이 민다.</b> 옛 자리와 새 자리의 차이를 그대로 더한다.
                var from = br.Position;
                var to = new Point3d(b.X, b.Y, 0);
                var move = to - from;
                br.Position = to;
                foreach (ObjectId aid in br.AttributeCollection)
                {
                    try
                    {
                        if (tr.GetObject(aid, OpenMode.ForWrite) is not AttributeReference ar || ar.IsErased) continue;
                        ar.Position += move;
                        // 가운데 정렬 글씨는 <c>AlignmentPoint</c>가 자리를 정한다 — 둘 다 밀어야 한다.
                        try { ar.AlignmentPoint += move; } catch { }
                        try { ar.AdjustAlignment(db); } catch { }
                    }
                    catch { }
                }
            }
            tr.Commit();
        }
        catch { }
    }

    /// <summary>표식을 지운다.</summary>
    /// <summary>★[JACK 0828 검토] <b>지웠는지 아닌지를 돌려준다.</b>
    /// <para>종전엔 <c>void</c>에 <c>catch { }</c>라, 못 지워도 부르는 쪽은 성공으로 알고
    /// 표에서 줄을 지웠다 — 평면에 <b>유령 표식</b>이 남고 그것을 가리키던 줄이 사라져
    /// <b>지울 길이 영영 없어진다</b>.</para>
    /// <para>이미 없는 것(<c>MarkId</c>가 비었거나 이미 지워진 것)은 <b>성공</b>이다 —
    /// 부르는 쪽이 바라는 것은 "지워라"가 아니라 "없게 하라"다.</para></summary>
    internal static bool EraseMark(BoreRow b)
    {
        try
        {
            if (b.MarkId.IsNull) return true;
            using var dl = Lock();
            var db = AcadApp.DocumentManager.MdiActiveDocument?.Database;
            if (db == null) return false;
            using var tr = db.TransactionManager.StartTransaction();
            if (tr.GetObject(b.MarkId, OpenMode.ForWrite, false, true) is Entity e && !e.IsErased) e.Erase();
            tr.Commit();
            b.MarkId = ObjectId.Null;
            return true;
        }
        catch (System.Exception ex)
        {
            try { DiagLog.Append("\n  시추 표식을 못 지웠다 — " + ex.Message); } catch { }
            return false;
        }
    }

    /// <summary>표식 블록을 만든다(없으면) — 동그라미 + 가운데 이름 속성.</summary>
    private static ObjectId EnsureMarkBlock(Database db, Transaction tr, ObjectId lay, ObjectId kst)
    {
        var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
        if (bt.Has(StrataPalette.MarkBlock)) return bt[StrataPalette.MarkBlock];

        bt.UpgradeOpen();
        var btr = new BlockTableRecord { Name = StrataPalette.MarkBlock, Origin = Point3d.Origin };
        ObjectId id = bt.Add(btr);
        tr.AddNewlyCreatedDBObject(btr, true);

        var c = new Circle(Point3d.Origin, Vector3d.ZAxis, MarkR) { LayerId = lay };
        btr.AppendEntity(c); tr.AddNewlyCreatedDBObject(c, true);

        var ad = new AttributeDefinition(Point3d.Origin, "BH1", "NAME", "공 이름", ObjectId.Null)
        {
            // ★[JACK 0831] 기본 높이도 낮춘다 — 실제 높이는 <c>DrawOne</c>이 이름 길이를 보고 다시 잡지만,
            //   여기 값이 크면 <b>블록 편집기에서 열어 볼 때</b> 넘쳐 보여 사람이 헷갈린다.
            Height = MarkR * 0.6,
            Justify = AttachmentPoint.MiddleCenter,
            LayerId = lay,
        };
        ad.AlignmentPoint = Point3d.Origin;
        if (!kst.IsNull) ad.TextStyleId = kst;
        btr.AppendEntity(ad); tr.AddNewlyCreatedDBObject(ad, true);
        return id;
    }

    /// <summary>★★ 지층 지표면을 만든다 — <b>평면에서는 숨긴 채로</b>(JACK 확정).
    /// <para>JACK: <i>"지층과 지하수위 등은 평면도에서 보일 필요가 없어. 종단·횡단에서만 표시할 거라
    /// 괜히 평면에서 보이면 더 헷갈리고 무겁기만 해."</i></para>
    /// <para>지층 다섯이면 평면에 지표면이 <b>일곱 겹</b>이라 등고선이 겹쳐 평면도를 못 쓴다 —
    /// <b>숨기는 것이 곁다리가 아니라 요건</b>이다.</para></summary>
    /// <param name="shows">층마다 <b>도면에 그릴까</b>. 비거나 짧으면 그린다.
    /// <para>★[JACK 0831] 지표면은 <b>늘 만든다</b> — 수량은 모든 층이 있어야 갈리기 때문이다.
    /// 이 값은 <b>보일지</b>만 정하고 설명란(<c>DH_SHOW=</c>)에 적혀 도면과 함께 저장된다.</para></param>
    internal static string BuildSurfaces(StrataModel model, out int made, out string note,
                                         System.Collections.Generic.IReadOnlyList<bool> shows = null)
    {
        shows ??= System.Array.Empty<bool>();
        made = 0; note = "";
        var log = new System.Text.StringBuilder();
        log.AppendLine($"\n■ 지층 만들기 {DateTime.Now:yyyy-MM-dd HH:mm:ss}  [DH.Grading {GradingSettings.Version}]");
        try
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) { note = "도면이 없다"; return note; }
            using var dl = Lock();
            var db = doc.Database;
            var cdoc = CivilApp.CivilApplication.ActiveDocument;

            // 원지반을 잡는다 — 격자의 표고를 여기서 읽는다.
            var surfs = Commands.SectionCommand.FindSurfaces(db, cdoc);
            ObjectId gid = ObjectId.Null;
            foreach (var s in surfs) if (s.Label == "원지반") { gid = s.SurfId; break; }
            if (gid.IsNull) { note = "원지반을 못 찾았다 — 등고선을 먼저 불러오세요"; log.AppendLine("  " + note); Flush(log); return note; }

            // ★★★[JACK 0831 · 수량 검토] <b>범위는 원지반이 정한다 — 보링공이 아니다.</b>
            //
            //   종전엔 보링공을 감싼 네모에 여유만 줬다. 보링공 넷이 30m 안에 모여 있고
            //   부지가 400m면 지층면이 <b>130m만 덮는다</b>. 그 밖 절단선에서는 지층 표고가
            //   <c>NaN</c>이 되는데, <c>CrossSectionArea.Above</c>는 NaN 칸을 <b>조용히 건너뛰고</b>
            //   나머지를 더한다 — 즉 <b>지층별 합이 전체 절토보다 적은데 아무 말이 없다</b>.
            //   토적표만 보면 멀쩡해서 <b>수량이 빈 채로 납품될</b> 수 있는 종류다.
            //
            //   → 원지반이 덮는 범위를 받아 <b>합집합</b>으로 잡는다. IDW는 보링공 밖에서도
            //     답을 내므로(가까운 공을 따라간다) 넓혀도 값이 깨지지 않는다.
            double x0 = double.MaxValue, y0 = double.MaxValue, x1 = double.MinValue, y1 = double.MinValue;
            foreach (var b in model.Logs)
            { x0 = Math.Min(x0, b.X); x1 = Math.Max(x1, b.X); y0 = Math.Min(y0, b.Y); y1 = Math.Max(y1, b.Y); }
            string extNote = "보링공 기준";
            try
            {
                using var trG = db.TransactionManager.StartTransaction();
                if (trG.GetObject(gid, OpenMode.ForRead) is CivilDb.TinSurface tsG)
                {
                    var gp = tsG.GetGeneralProperties();
                    x0 = Math.Min(x0, gp.MinimumCoordinateX); x1 = Math.Max(x1, gp.MaximumCoordinateX);
                    y0 = Math.Min(y0, gp.MinimumCoordinateY); y1 = Math.Max(y1, gp.MaximumCoordinateY);
                    extNote = "원지반 ∪ 보링공";
                }
                trG.Commit();
            }
            catch (System.Exception exG)
            { log.AppendLine("  ⚠원지반 범위를 못 읽어 보링공 기준으로만 잡는다 — " + exG.Message
                           + " (부지 밖 측점에서 지층 수량이 빠질 수 있다)"); }
            double padXY = Math.Max(50.0, Math.Max(x1 - x0, y1 - y0) * 0.1);
            x0 -= padXY; x1 += padXY; y0 -= padXY; y1 += padXY;

            // ★격자 수도 범위에 맞춰 늘린다 — 범위가 열 배 넓어졌는데 41×41이면 <b>칸이 성겨진다</b>.
            //   한 칸이 5m를 넘지 않게 하되 201×201에서 멈춘다(그 위는 만드는 데만 한참 걸린다).
            int N = (int)Math.Round(Math.Max(x1 - x0, y1 - y0) / 5.0);
            N = Math.Max(40, Math.Min(200, N));
            double dx = (x1 - x0) / N, dy = (y1 - y0) / N;

            int nFix = 0; double worstDrop = 0;
            // ★★[JACK 0828] <b>어느 층이 뒤집혔는지</b>까지 센다 —
            //   936곳이라는 숫자만으로는 <b>무엇을 고쳐야 하는지</b> 알 수 없다.
            //   한 층에 몰려 있으면 그 층의 <b>적용값(두께/GL)</b>을 바꿔야 한다는 신호다.
            var fixPer = new int[model.Defs.Count];
            var names = new List<string>();

            // ★[검토] 격자는 <b>트랜잭션 밖</b>에 둔다 — 지표면 만들기가 그 뒤에 오기 때문이다.
            //   층마다 지표면 하나. 이름 앞에 차례를 붙여 <b>도구공간에서 순서대로</b> 보이게 한다.
            var zs = new double[model.Defs.Count][];
            for (int i = 0; i < model.Defs.Count; i++) zs[i] = new double[(N + 1) * (N + 1)];
            var zw = new double[(N + 1) * (N + 1)];
            var ok = new bool[(N + 1) * (N + 1)];

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var gts = (CivilDb.TinSurface)tr.GetObject(gid, OpenMode.ForRead);

                for (int iy = 0, k = 0; iy <= N; iy++)
                    for (int ix = 0; ix <= N; ix++, k++)
                    {
                        double x = x0 + dx * ix, y = y0 + dy * iy;
                        double gz;
                        try { gz = gts.FindElevationAtXY(x, y); }
                        catch { ok[k] = false; continue; }      // 원지반 밖 — 그 자리는 안 만든다
                        ok[k] = true;
                        var col = model.At(x, y, gz);
                        // ★★★[JACK 0831 "암선은 해당 층의 상단을 기준으로 작성해.
                        //   우리는 두께로 계산하다 보니 하단이 표시되는 것 같아"]
                        //   <b>맞다.</b> 종전엔 층 i의 면을 <c>Bottom[i]</c>(그 층의 <b>바닥</b>)으로 만들었다 —
                        //   그래서 <c>풍화암</c>이라고 적힌 선이 실제로는 <b>풍화암이 끝나는 자리</b>였다.
                        //   도면 관례는 <b>"그 층이 시작되는 자리"</b>에 층 이름을 적는 것이다.
                        //   → 층 i의 면 = <b>그 층의 상단</b> = 앞 층의 바닥(첫 층은 원지반).
                        //   ※표고 값 자체는 이미 다 갖고 있다 — <b>이름이 한 칸 밀린 것</b>이 전부였다.
                        for (int i = 0; i < model.Defs.Count; i++)
                            zs[i][k] = i == 0 ? col.Ground : col.Bottom[i - 1];
                        zw[k] = col.Water;
                        if (col.Fixed.Count > 0)
                        {
                            nFix++;
                            foreach (var f in col.Fixed)
                            {
                                worstDrop = Math.Max(worstDrop, f.Drop);
                                if (f.Layer >= 0 && f.Layer < fixPer.Length) fixPer[f.Layer]++;
                            }
                        }
                    }

                tr.Commit();
            }

            // ── ★★★[JACK 0828 "지층 0장을 만들었습니다"] <b>만드는 자리를 트랜잭션 밖으로 뺐다.</b>
            //   되는 본보기(<see cref="Commands.ImportGisCommand"/>)는 <c>TinSurface.Create</c>를
            //   <b>트랜잭션을 닫은 뒤</b> 부르고 그다음 새로 연다. 나는 <b>열린 트랜잭션 안에서</b> 불렀고,
            //   실패를 <c>catch { return false; }</c>로 <b>통째로 삼켰다</b> —
            //   그래서 로그에 <c>만든 지표면 0장</c>만 남고 이유가 없었다.
            //   <b>이 저장소가 제일 싫어하는 자리다</b>: 조용히 실패하기.
            // ★★★[JACK 0828 검토] <b>[확인]은 몇 번을 눌러도 같은 결과라야 한다.</b>
            //   종전엔 <c>MakeSurface</c>가 <b>정확히 그 이름</b>만 지웠다. 그래서
            //   · 층 이름을 <c>표토</c>→<c>매립토</c>로 고치면 <c>DH_지층_1_표토</c>가 <b>남고</b>,
            //     <c>AppendStrata</c>가 <b>둘 다 ord=1</b>로 담는다(정렬이 안정적이지도 않아 순서도 안 정해진다).
            //   · 5층→3층으로 줄이면 <c>DH_지층_4·5_*</c>가 <b>영영</b> 남아 종단·횡단에 계속 그려진다.
            //   → 만들기 전에 <b>우리 앞머리를 단 지표면을 싹 지운다</b>. 이번 판에 쓸 것만 새로 선다.
            int nOld = 0;
            try
            {
                using var trW = db.TransactionManager.StartTransaction();
                var cdocW = CivilApp.CivilApplication.ActiveDocument;
                var kill = new List<ObjectId>();
                foreach (ObjectId sid0 in cdocW.GetSurfaceIds())
                    try
                    {
                        if (trW.GetObject(sid0, OpenMode.ForRead) is not CivilDb.Surface s0) continue;
                        string n0 = s0.Name ?? "";
                        if (n0.StartsWith(SurfPrefix, System.StringComparison.Ordinal) || n0 == WaterSurfName)
                            kill.Add(sid0);
                    }
                    catch { }
                foreach (var kid in kill)
                    try { trW.GetObject(kid, OpenMode.ForWrite).Erase(); nOld++; } catch { }
                trW.Commit();
            }
            catch (System.Exception ex)
            { log.AppendLine("  ⚠옛 지층면을 다 못 지웠다 — " + ex.Message + " (겹쳐 남을 수 있다)"); }
            if (nOld > 0) log.AppendLine($"  옛 지층면 {nOld}장을 먼저 지웠다 — 이름을 고치거나 층을 줄여도 남지 않는다");

            var why2 = new System.Text.StringBuilder();
            for (int i = 0; i < model.Defs.Count; i++)
            {
                string nm = $"{SurfPrefix}{i + 1}_{model.Defs[i].Name}";
                if (MakeSurface(db, nm, x0, y0, dx, dy, N, zs[i], ok, out string w))
                {
                    made++; names.Add(nm);
                    // ★★★[JACK 0831] <b>암종을 도면에 남긴다.</b>
                    //   토적표는 층마다 "이것이 토사냐 풍화암이냐"를 알아야 하는데,
                    //   그 값은 지금까지 <b>도킹바 메모리에만</b> 있었다 —
                    //   창을 닫거나 도면을 다시 열면 사라져 <b>수량이 조용히 전부 토사</b>가 된다.
                    //   지표면 설명란에 적어 두면 도면과 함께 저장된다.
                    // ★★[JACK 0901] <b>그릴지는 부르는 쪽이 정한다</b>(<c>Confirm</c>: 암층만 그린다).
                    //   첫 층 잠금 장치는 없앴다 — 첫 층은 어차피 토사라 안 그린다.
                    bool showI = i < shows.Count ? shows[i] : true;
                    WriteRock(db, nm, model.Defs[i].Bucket, showI);
                }
                else why2.Append($"\n      {nm} — {w}");
            }
            if (MakeSurface(db, WaterSurfName, x0, y0, dx, dy, N, zw, ok, out string ww))
            { made++; names.Add(WaterSurfName); }
            else why2.Append($"\n      {WaterSurfName} — {ww}");
            if (why2.Length > 0) log.AppendLine("  ⚠못 만든 것:" + why2);

            // ★ 평면에서 숨긴다 — 만드는 순간부터. 나중에 사람이 끄는 것이 아니다.
            int nHid = HideInPlan(db, names);

            log.AppendLine($"  범위 {x0:F1},{y0:F1} ~ {x1:F1},{y1:F1}({extNote})"
                         + $" · 격자 {N + 1}×{N + 1}(칸 {dx:F1}×{dy:F1}m) · 보링공 {model.Logs.Count}개");
            // ★★★[JACK 0901 "혹시 도면상에 윗선·아랫선 적용이 잘못된 거 아니야?"]
            //   <b>말로 답하지 않고 보링공 자리에서 재서 남긴다.</b>
            //   그 자리는 보간이 <b>친 값을 그대로</b> 돌려주는 자리라(같은 점 규칙),
            //   여기 찍힌 깊이가 시추주상도와 다르면 <b>그때는 정말 배선이 틀린 것</b>이다.
            //   ★<b>선은 그 층의 상단</b>이므로, 지표에서 <c>풍화암</c> 선까지는
            //     그 위 층들(표토·풍화토)의 두께를 <b>다 더한 값</b>이라야 맞다.
            try
            {
                foreach (var bl in model.Logs)
                {
                    var c = model.At(bl.X, bl.Y, bl.Gl);
                    var sb2 = new System.Text.StringBuilder();
                    for (int i = 0; i < model.Defs.Count; i++)
                    {
                        double topZ = i == 0 ? c.Ground : c.Bottom[i - 1];
                        double depth = c.Ground - topZ;                 // 지표에서 그 선까지
                        sb2.Append($"\n        {model.Defs[i].Name} 상단 EL.{topZ:F2}"
                                 + $" (지표에서 {depth:F2}m)");
                    }
                    log.AppendLine($"    ★{bl.Name} 되읽기 — 지반고 EL.{bl.Gl:F2}"
                                 + $" · 친 값 [{string.Join(", ", System.Array.ConvertAll(bl.Thickness, v => double.IsNaN(v) ? "-" : v.ToString("0.##")))}]"
                                 + sb2);
                }
            }
            catch { }
            log.AppendLine($"  만든 지표면 {made}장: {string.Join(" · ", names)}");
            // ★[JACK 0831] 무엇을 도면에 그리고 무엇을 숨겼는지 <b>반드시 남긴다</b> —
            //   "왜 이 선이 안 보이지"를 다음에 다시 헤매지 않으려는 것이다.
            {
                var onN = new System.Text.StringBuilder(); var offN = new System.Text.StringBuilder();
                for (int i = 0; i < model.Defs.Count; i++)
                {
                    bool on = i >= shows.Count || shows[i];
                    (on ? onN : offN).Append(' ').Append(model.Defs[i].Name);
                }
                log.AppendLine($"  도면 표시 — 그림:{(onN.Length > 0 ? onN.ToString() : " (없음)")}"
                             + $" · 숨김:{(offN.Length > 0 ? offN.ToString() : " (없음)")}"
                             + "  ※숨겨도 수량은 그대로 갈린다");
            }
            if (nFix > 0)
            {
                var per = new System.Text.StringBuilder();
                for (int i = 0; i < fixPer.Length; i++)
                    if (fixPer[i] > 0)
                        per.Append($" [{model.Defs[i].Name}({(model.Defs[i].Mode == InterpMode.Thickness ? "두께" : "GL")}) {fixPer[i]}곳]");
                log.AppendLine($"  ⚠역전을 눌러 내린 자리 {nFix}곳 · 가장 크게 누른 폭 {worstDrop:F2}m — <b>그 자리 수량은 덜 믿어야 한다</b>");
                log.AppendLine($"    층별:{per}"
                             + "\n    ※한 층에 몰려 있으면 그 층의 <b>적용값을 GL에서 두께로</b> 바꿔 보세요 —"
                             + " GL은 보링공 표고에 매여 있어 지형이 낮아지면 윗층을 뚫고 올라온다");
            }
            else log.AppendLine("  역전은 한 자리도 없었다");
            log.AppendLine($"  평면에서 숨김 {nHid}장 — 종단·횡단에서만 보인다(JACK 확정)");
            note = nFix > 0 ? $"역전 {nFix}곳(최대 {worstDrop:F2}m)을 눌러 내렸다" : "역전 없음";
            Flush(log);
            return "";
        }
        catch (System.Exception ex)
        {
            note = "실패 — " + ex.Message;
            log.AppendLine("  " + note);
            Flush(log);
            return note;
        }
    }

    /// <summary>지표면 설명란에 적는 암종 표시 — <c>DH_ROCK=Weathered</c>.
    /// <para>사람이 설명란에 딴 글을 써 넣어도 이 조각만 골라 읽는다.</para></summary>
    internal const string RockTag = "DH_ROCK=";

    /// <summary>★[JACK 0831] 도면에 보일까 — 설명란에 <c>DH_SHOW=0/1</c>로 적어 둔다.
    /// <para>암종과 같은 자리에 담아 <b>도면과 함께 저장</b>된다 — 창을 닫아도 안 잃는다.</para></summary>
    internal const string ShowTag = "DH_SHOW=";

    /// <summary>★★★[JACK 0831 검토] 지층면을 <b>어느 방식으로 만들었는가</b>.
    /// <para><c>1</c>=층의 <b>하단</b>(0828~0831 오전) · <c>2</c>=층의 <b>상단</b>(0831 오후~).</para>
    /// <para><b>왜 필요한가.</b> 옛 도면에 남은 지표면은 하단인데 지금 코드는 상단으로 읽는다 —
    /// 그러면 <b>암종이 한 층씩 밀린다</b>(토사가 풍화암 몫을 먹는다).
    /// 합계는 그대로 맞아 <c>Recon</c> 대조로도 안 잡힌다(S83이 그것을 증명한다).
    /// 그래서 <b>만든 방식을 도면에 적어 두고</b>, 옛 것이면 소리 내어 알린다.</para></summary>
    internal const string VerTag = "DH_SVER=";

    /// <summary>지금 만드는 방식 — <b>층의 상단</b>.</summary>
    internal const int SurfVer = 2;

    /// <summary>지표면이 어느 방식으로 만들어졌나. 표시가 없으면 <b>1(옛 방식)</b>로 본다.</summary>
    internal static int VerOf(CivilDb.Surface surf)
    {
        try
        {
            string d = surf?.Description ?? "";
            int at = d.IndexOf(VerTag, System.StringComparison.Ordinal);
            if (at < 0) return 1;
            string rest = d.Substring(at + VerTag.Length);
            int semi = rest.IndexOf(';');
            if (semi >= 0) rest = rest.Substring(0, semi);
            return int.TryParse(rest.Trim(), out int v) ? v : 1;
        }
        catch { return 1; }
    }

    /// <summary>★[JACK 0831] 암종을 지표면 설명란에 적는다 — <b>도면과 함께 저장된다</b>.</summary>
    internal static void WriteRock(Database db, string surfName, RockClass rock, bool show = true)
    {
        try
        {
            var cdoc = CivilApp.CivilApplication.ActiveDocument;
            using var tr = db.TransactionManager.StartTransaction();
            foreach (ObjectId sid in cdoc.GetSurfaceIds())
                try
                {
                    if (tr.GetObject(sid, OpenMode.ForRead) is not CivilDb.Surface s0 || s0.Name != surfName) continue;
                    var sw = (CivilDb.Surface)tr.GetObject(sid, OpenMode.ForWrite);
                    string old = "";
                    try { old = sw.Description ?? ""; } catch { }
                    // 옛 표시는 지우고 새로 적는다 — 두 개가 남으면 어느 것이 참인지 알 수 없다.
                    var keep = new System.Text.StringBuilder();
                    foreach (var part in old.Split(';'))
                        if (part.Trim().Length > 0 && !part.Contains(RockTag)
                            && !part.Contains(ShowTag) && !part.Contains(VerTag))
                            keep.Append(part.Trim()).Append(';');
                    keep.Append(RockTag).Append(rock)
                        .Append(';').Append(ShowTag).Append(show ? 1 : 0)
                        .Append(';').Append(VerTag).Append(SurfVer);
                    sw.Description = keep.ToString();
                    break;
                }
                catch { }
            tr.Commit();
        }
        catch { }
    }

    /// <summary>★★[JACK 0831] 지표면에서 암종을 읽는다 — <b>토적표가 이것으로 층을 가른다</b>.
    ///
    /// <para>세 곳을 차례로 본다. <b>앞의 것이 없을 때만</b> 다음으로 간다:</para>
    /// <list type="number">
    /// <item>지표면 <b>설명란</b>(<c>DH_ROCK=…</c>) — 만들 때 우리가 적어 둔 것. 가장 믿을 만하다.</item>
    /// <item>도킹바가 <b>열려 있으면</b> 그 표의 층 이름과 맞춰 본다.</item>
    /// <item>층 <b>이름 자체</b>가 표준 이름(토사·풍화암·연암·보통암·경암)과 같으면 그것으로 본다.</item>
    /// </list>
    /// <para>셋 다 아니면 <b>토사</b>로 본다 — 그리고 <b>그 사실을 부르는 쪽이 로그에 남긴다</b>.
    /// 조용히 토사로 세면 암 수량이 통째로 사라지는데 도면에는 아무 자국이 없다.</para></summary>
    /// <summary>★[JACK 0831] 이 지표면을 <b>도면에 그릴까</b>. 설명란에 표시가 없으면 <b>그린다</b>(옛 도면 대비).</summary>
    internal static bool ShowOf(ObjectId surfId)
    {
        try
        {
            var db = AcadApp.DocumentManager.MdiActiveDocument?.Database;
            if (db == null || surfId.IsNull) return true;
            using var tr = db.TransactionManager.StartTransaction();
            bool v = true;
            if (tr.GetObject(surfId, OpenMode.ForRead) is CivilDb.Surface su)
            {
                string d = "";
                try { d = su.Description ?? ""; } catch { }
                int at = d.IndexOf(ShowTag, System.StringComparison.Ordinal);
                if (at >= 0)
                {
                    string rest = d.Substring(at + ShowTag.Length);
                    int semi = rest.IndexOf(';');
                    if (semi >= 0) rest = rest.Substring(0, semi);
                    v = rest.Trim() != "0";
                }
            }
            tr.Commit();
            return v;
        }
        catch { return true; }
    }

    internal static RockClass RockOf(CivilDb.Surface surf, out string how)
    {
        how = "";
        string nm = "";
        try { nm = surf?.Name ?? ""; } catch { }
        // ① 설명란
        try
        {
            string d = surf?.Description ?? "";
            int at = d.IndexOf(RockTag, System.StringComparison.Ordinal);
            if (at >= 0)
            {
                string rest = d.Substring(at + RockTag.Length);
                int semi = rest.IndexOf(';');
                if (semi >= 0) rest = rest.Substring(0, semi);
                if (System.Enum.TryParse(rest.Trim(), out RockClass rc))
                { how = "설명란"; return rc; }
            }
        }
        catch { }

        string shortNm = ShortName(nm);
        // ② 열려 있는 도킹바
        try
        {
            var panel = StrataPanel.Current;
            if (panel != null)
                foreach (var lr in panel.Layers)
                    if (lr != null && lr.Name == shortNm) { how = "도킹바"; return lr.Rock; }
        }
        catch { }
        // ③ 이름이 표준 이름 그대로일 때
        try
        {
            foreach (RockClass r in System.Enum.GetValues(typeof(RockClass)))
                if (QtyTableSpec.NameOf(r).Replace(" ", "") == shortNm.Replace(" ", ""))
                { how = "이름"; return r; }
        }
        catch { }
        how = "모름";
        return RockClass.Soil;
    }

    /// <summary>격자 하나로 지표면을 만든다(같은 이름이 있으면 지우고 새로).
    /// <para>★<b>왜 실패했는지를 돌려준다.</b> 종전엔 <c>catch { return false; }</c>였다 —
    /// 로그에 <c>0장</c>만 남고 이유가 없어 아무것도 못 했다.</para>
    /// <para>★<b><c>TinSurface.Create</c>는 트랜잭션 밖에서 부른다.</b>
    /// 되는 본보기가 그렇게 한다 — 열린 트랜잭션 안에서 부르면 조용히 실패한다.</para></summary>
    private static bool MakeSurface(Database db, string name,
                                    double x0, double y0, double dx, double dy, int n,
                                    double[] z, bool[] ok, out string why)
    {
        why = "";
        // ① 쓸 점을 먼저 모은다 — 점이 모자라면 굳이 지표면을 만들 이유가 없다.
        var pts = new Point3dCollection();
        int nNaN = 0, nOut = 0;
        for (int iy = 0, k = 0; iy <= n; iy++)
            for (int ix = 0; ix <= n; ix++, k++)
            {
                if (!ok[k]) { nOut++; continue; }
                if (double.IsNaN(z[k])) { nNaN++; continue; }
                pts.Add(new Point3d(x0 + dx * ix, y0 + dy * iy, z[k]));
            }
        if (pts.Count < 3)
        {
            why = $"쓸 점이 {pts.Count}개뿐이다(표고를 못 구한 자리 {nNaN}개 · 원지반 밖 {nOut}개)"
                + (nNaN > nOut ? " — <b>두께가 안 채워진 층이 아닌지 보세요</b>" : "");
            return false;
        }

        // ② 옛 것을 지운다.
        try
        {
            using var tr0 = db.TransactionManager.StartTransaction();
            GradingBuilder.EraseSurfacesByBaseName(tr0, name);
            tr0.Commit();
        }
        catch (System.Exception ex) { why = "옛 지표면을 못 지웠다 — " + ex.Message; return false; }

        // ③ 만든다 — <b>트랜잭션 밖</b>에서.
        ObjectId sid;
        try { sid = CivilDb.TinSurface.Create(db, name); }
        catch (System.Exception ex) { why = "만들기 실패 — " + ex.Message; return false; }
        if (sid.IsNull) { why = "만들기가 빈 값을 돌려줬다"; return false; }

        // ④ 점을 넣는다.
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            var ts = (CivilDb.TinSurface)tr.GetObject(sid, OpenMode.ForWrite);
            ts.AddVertices(pts);
            tr.Commit();
            return true;
        }
        catch (System.Exception ex) { why = $"점 {pts.Count}개를 못 넣었다 — {ex.Message}"; return false; }
    }

    /// <summary>평면에서 숨긴다 — 이 프로젝트가 목표면·가상면을 숨기는 그 길을 그대로 쓴다.</summary>
    internal const string HideStyleName = "DH_지층(평면숨김)";

    /// <summary>★★★[JACK 0828 검토] <b>남의 스타일을 고르지 않고 우리가 만든다.</b>
    ///
    /// <para><b>원인이 된 함정</b>: <c>PickStyle</c>은 이름을 못 찾으면 <c>ObjectId.Null</c>이 아니라
    /// <b>컬렉션의 첫 스타일</b>을 돌려준다. 그래서 <c>if (!noShow.IsNull)</c>이 관문 노릇을 못 했고,
    /// 도면에 <c>no display</c>·<c>경계만</c>이 없으면 <b>아무 스타일이나 발라 놓고</b>
    /// 로그는 <c>평면에서 숨김 7장</c>이라고 적었다 — JACK 요건이 정확히 뒤집히는데 아무 말이 없다.
    /// (DHT.dwt에 그 이름들이 있다는 보장도 없다.)</para>
    ///
    /// <para>→ 등고선이 쓰는 길(<c>ImportGisCommand.EnsureContourStyle</c>)을 그대로 쓴다:
    /// <b>스타일을 만들고 표시 항목을 하나하나 끈다.</b> 열거값을 통째로 돌므로 버전이 달라도 안 깨진다.
    /// 그리고 <b>되읽어</b> 실제로 꺼졌는지 확인한다 — 안 되면 안 됐다고 적는다.</para></summary>
    private static int HideInPlan(Database db, List<string> names)
    {
        int n = 0;
        string why = null;
        try
        {
            var cdoc = CivilApp.CivilApplication.ActiveDocument;
            using var tr = db.TransactionManager.StartTransaction();

            // ── ① 숨김 스타일을 확보한다(없으면 만든다).
            var styles = cdoc.Styles.SurfaceStyles;
            ObjectId hid = ObjectId.Null;
            foreach (ObjectId sid in styles)
                try
                {
                    if (tr.GetObject(sid, OpenMode.ForRead) is CivilStyles.SurfaceStyle s0
                        && string.Equals(s0.Name, HideStyleName, System.StringComparison.OrdinalIgnoreCase))
                    { hid = sid; break; }
                }
                catch { }
            if (hid.IsNull) { try { hid = styles.Add(HideStyleName); } catch (System.Exception ex) { why = "스타일을 못 만들었다 — " + ex.Message; } }
            if (hid.IsNull)
            {
                tr.Commit();
                try { DiagLog.Append("\n  ⚠평면 숨김 실패 — " + (why ?? "스타일 없음") + " · <b>지층이 평면에 보인다</b>"); } catch { }
                return 0;
            }

            // ── ② 평면·모형 표시 항목을 <b>전부</b> 끈다.
            int offN = 0, failN = 0;
            if (tr.GetObject(hid, OpenMode.ForWrite) is CivilStyles.SurfaceStyle st)
                foreach (CivilStyles.SurfaceDisplayStyleType t in
                         System.Enum.GetValues(typeof(CivilStyles.SurfaceDisplayStyleType)))
                {
                    try { st.GetDisplayStylePlan(t).Visible = false; offN++; } catch { failN++; }
                    try { st.GetDisplayStyleModel(t).Visible = false; } catch { }
                }

            // ── ③ 우리 지표면에만 입힌다.
            foreach (ObjectId sid in cdoc.GetSurfaceIds())
                try
                {
                    if (tr.GetObject(sid, OpenMode.ForWrite) is not CivilDb.TinSurface ts) continue;
                    if (!names.Contains(ts.Name)) continue;
                    ts.StyleId = hid; n++;
                }
                catch { }
            tr.Commit();

            // ── ④ ★<b>되읽는다.</b> 스타일 이름이 맞는지, 정말 다 꺼졌는지.
            int stillOn = 0, wrongSty = 0;
            try
            {
                using var trR = db.TransactionManager.StartTransaction();
                if (trR.GetObject(hid, OpenMode.ForRead) is CivilStyles.SurfaceStyle stR)
                    foreach (CivilStyles.SurfaceDisplayStyleType t in
                             System.Enum.GetValues(typeof(CivilStyles.SurfaceDisplayStyleType)))
                        try { if (stR.GetDisplayStylePlan(t).Visible) stillOn++; } catch { }
                foreach (ObjectId sid in cdoc.GetSurfaceIds())
                    try
                    {
                        if (trR.GetObject(sid, OpenMode.ForRead) is not CivilDb.TinSurface ts) continue;
                        if (!names.Contains(ts.Name)) continue;
                        if (ts.StyleId != hid) wrongSty++;
                    }
                    catch { }
                trR.Commit();
            }
            catch { }
            try
            {
                DiagLog.Append($"\n  평면 숨김 되읽기 — 스타일 '{HideStyleName}' · 끈 항목 {offN}개"
                             + (failN > 0 ? $"(못 끈 것 {failN}개)" : "")
                             + (stillOn > 0 ? $" · ⚠<b>아직 켜져 있는 항목 {stillOn}개</b>" : " · 평면 표시 전부 꺼짐")
                             + (wrongSty > 0 ? $" · ⚠<b>이 스타일이 아닌 지표면 {wrongSty}장</b>" : ""));
            }
            catch { }
        }
        catch (System.Exception ex)
        { try { DiagLog.Append("\n  ⚠평면 숨김 실패 — " + ex.Message); } catch { } }
        return n;
    }

    private static void Flush(System.Text.StringBuilder log)
    {
        try { DiagLog.Append(log.ToString()); } catch { }
    }
}
