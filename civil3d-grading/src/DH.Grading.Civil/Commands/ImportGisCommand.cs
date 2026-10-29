using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Civil.DatabaseServices;
using DH.Grading.Core;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;
using AcadEntity = Autodesk.AutoCAD.DatabaseServices.Entity;

namespace DH.Grading.Civil.Commands;

/// <summary>
/// [가져오기 — JACK 0731] 수치지형도 등고선(사내 PostGIS)·연속지적(VWorld — v109.1)을 도면 좌표계로 가져온다.
///  · DHCONTOUR : 범위 두 점 클릭 → 3D 등고선 작도 → **"원지반" TIN 지표면 자동 생성**
///  · DHPARCEL  : 범위 두 점 클릭 → 필지 경계선(사각 범위대로 클립) + 지번 문자(별도 레이어, 글자 1.0) + 국공유지 해치 + 필지 속성
/// 좌표계는 항상 정지옵션/도면 설정을 따른다(DB · VWorld 모두 도면 좌표계로 보내줌).
/// </summary>
public sealed class ImportGisCommand
{
    internal const string LayerContour = "DH-등고선";
    internal const string LayerContourIndex = "DH-등고선-계곡선";
    internal const string LayerParcel = "DH-지적도";
    internal const string LayerJibun = "DH-지번";
    /// <summary>★[v109.1] 국공유지 필지 해치(하늘색 반투명) — 사유지 · 소유 자료 없음은 칠 없음.</summary>
    internal const string LayerParcelPublic = "DH-지적-국공유지";
    /// <summary>★★[v109.1 · 검토 H2] 지적이 올라가는 레이어 <b>한 목록</b> — 가져오기(교체) · 지적도 삭제 · 초기화 보존이 모두 이것을 쓴다(«두 경로가 다른 자» 막기).</summary>
    internal static readonly string[] ParcelLayers = { LayerParcel, LayerJibun, LayerParcelPublic };
    /// <summary>수치지도 DXF의 표고점 — 서버 지표면에는 없고 DXF에만 있다.</summary>
    internal const string LayerSpot = "DH-표고점";

    internal const string GroundSurfaceName = "원지반";

    /// <summary>원지반을 만들 때 <b>갈아 끼우는</b> 레이어 — 서버·수치지도 두 경로가 같이 쓴다.</summary>
    internal static readonly string[] GroundImportLayers =
        { LayerContour, LayerContourIndex, LayerSpot };

    /// <summary>가져온 데이터가 올라가는 레이어(초기화·보존 판정 공용).</summary>
    internal static readonly string[] ImportLayers =
        { LayerContour, LayerContourIndex, LayerParcel, LayerJibun, LayerParcelPublic, LayerSpot };

    private const int MaxContourRows = 60000;   // 안전 상한(실측 5km각 754가닥이라 사실상 여유)
    private const int MaxParcelRows = 20000;    // 실측 5km각 6만 필지 → 상한 걸고 안내
    // [JACK 0731] 지표면이 뭉뚱그려진다는 지적 → 단순화 끔(0). 원본 정점(평균 4.2m 간격)을 그대로 쓴다.
    //   등고선 자체가 가벼워(1km각 40가닥·1,100점) 성능 여유가 충분하다.
    private const double SimplifyM = 0.0;

    // ── 등고선 + 원지반 지표면 ────────────────────────────────────────────────
    [CommandMethod("DHCONTOUR")]
    public void RunContour()
    {
        Document doc = AcadApp.DocumentManager.MdiActiveDocument;
        if (doc == null) return;
        GradingSettings.SyncToDocument(doc);   // [도면 전환 0803] 도면이 바뀌었으면 그 도면 기준으로 설정·기억 재정렬
        Editor ed = doc.Editor;
        Database db = doc.Database;

        try
        {
            // ★★★[JACK 0901 "그냥 정지옵션 좌표계 띄워 주고 도킹바에서 바꾸면 정지옵션도 바뀌게"]
            //   <b>정지옵션 하나를 진짜로 삼는다.</b> 예전에는 도면 좌표계를 먼저 보고 없을 때만
            //   정지옵션을 봤는데(ResolveEpsg), 그러면 둘이 다를 수 있어 <b>"좌표계가 두 개"</b>가 된다.
            //   여기서는 정지옵션을 쓰고, <b>도면 좌표계를 거기에 맞춘다</b> — 그래서 다를 수가 없다.
            //   ★★<b>여기서 도면 좌표계를 건드리지 않는다.</b> 이미 좌표계를 잡아 놓고 쓰던
            //   프로젝트에서 열 때마다 도면을 고치면 <b>남의 설정을 말없이 바꾸는 것</b>이고,
            //   못 바꾸면 쓸데없는 오류 멘트가 뜬다(JACK 0901). 바꾸는 것은 사용자가
            //   도킹바에서 <b>직접 골랐을 때</b>만 한다.
            int epsg = GradingSettings.ExportEpsg;
            string csNote = $"정지옵션 EPSG:{epsg}";
            ed.WriteMessage($"\n[등고선] 좌표계: {csNote}");
            // ★[JACK 0901] 도면에 좌표계가 <b>없을 때만</b> 채운다 — 있으면 안 건드린다.
            //   비워 두면 이 도면이 밖으로 나갔을 때 여기가 어디인지 아무도 모른다.
            try
            {
                var (setIt, csFix) = KoreaCs.AssignIfMissing(db, epsg);
                if (setIt && csFix.Contains("지정")) ed.WriteMessage("\n[등고선] " + csFix);
            }
            catch { }
            // ★★<b>접속부터 본다</b> — 10분 걸려 범위를 고른 뒤에 "VPN을 확인하세요"는
            //   못 할 짓이다. 서버가 없으면 어차피 범위를 골라도 소용이 없다(검토 0901).
            if (!Reachable(ed)) return;

            // ★★★[JACK 0901 "서버 지표면도 지도·두점 명령창 묻지 말고 그냥 바로 도킹바 띄워"]
            //   빈 도면에서 찍을 근거가 없으니 <b>고를 것이 없다</b> — 물어봐야 답이 하나다.
            //   도킹바가 범위를 받으면 스스로 DHCONTOURBOX를 태워 가져오고 <b>닫힌다</b>.
            MapPalette.Show(doc, epsg, csNote);
        }
        catch (System.Exception ex)
        {
            ed.WriteMessage("\n[등고선 오류] " + ex.Message);
            AcadApp.ShowAlertDialog("등고선 가져오기 중 오류:\n" + ex.Message);
        }
    }

    /// <summary>★★★[JACK 0901] <b>범위를 이미 안다</b>는 전제로 등고선을 받아 원지반까지 만든다.
    /// <para>지도 도킹바가 이것을 그대로 부른다 — 범위를 얻는 방법만 다르고
    /// <b>받아서 그리는 일은 한 벌</b>이다(§50).</para></summary>
    internal static bool ImportContourBox(Document doc, int epsg, string csNote,
                                          double x0, double y0, double x1, double y1)
    {
        Editor ed = doc.Editor;
        Database db = doc.Database;
        // ★[검토 0901] <b>도면을 만지는 자리는 스스로 잠근다.</b> 지금 부르는 곳은 둘 다 명령이라
        //   AutoCAD가 알아서 잠가 주지만, 나중에 누가 도킹바 단추에서 바로 부르면 그 순간 터진다.
        //   잠금은 겹쳐도 안전하다 — 어디서 불렸는지 따지지 않는다(StrataDraw와 같은 방식).
        using var dlAll = SafeLock(doc);
        try
        {
            ed.WriteMessage("\n[등고선] 사내 DB에서 받는 중…");
            List<GisDb.ContourLine> lines;
            bool cut; string diag;
            try
            {
                lines = GisDb.LoadContours(x0, y0, x1, y1, epsg, SimplifyM, MaxContourRows, out cut, out diag);
            }
            catch (System.Exception dex) { Refuse(ed, "등고선", "등고선을 받지 못했습니다.\n" + GisDb.Clean(dex.Message)); return false; }

            ed.WriteMessage("\n[등고선] " + diag);
            if (lines.Count == 0)
            {
                Refuse(ed, "등고선", "이 범위에는 등고선 자료가 없습니다.\n" +
                                     "좌표계(원점)가 맞는지, 범위가 국내인지 확인하세요.");
                return false;
            }

            // ── 3D 등고선 작도(주곡선/계곡선 분리) ──
            var ids = new ObjectIdCollection();
            using (var tr = db.TransactionManager.StartTransaction())
            {
                // ★★[검토 0901] <b>표고점도 같이 지운다.</b> 수치지도로 받은 뒤 서버로 다시 받으면
                //   지표면은 새로 만들어지는데 표고점 851개는 <b>아무 지표면도 안 문 채</b> 남는다.
                //   레이어가 꺼져 있어 눈에도 안 띈다. 두 경로가 <b>같은 목록</b>을 지워야 한다(§50).
                // ★[검토 0917] 지적도와 <b>같은 자</b>를 쓴다 — 못 지웠으면 그 자리에서 멈춘다.
                //   (§65 "두 경로가 다른 자를 쓴다"가 바로 이번 중복의 뿌리였다)
                int cFail = EraseOnLayers(db, tr, GroundImportLayers);   // 다시 불러오면 교체
                if (cFail > 0)
                {
                    ed.WriteMessage($"\n[등고선] 이전 등고선·표고점 {cFail}개를 못 지웠습니다 — 새로 그리지 않았습니다."
                        + "\n  해당 레이어의 잠금을 풀고 다시 하세요.");
                    AcadApp.ShowAlertDialog($"등고선을 다시 가져오지 못했습니다.\n\n"
                        + $"이전 자료 {cFail}개를 못 지웠습니다(레이어 잠금 확인).");
                    return false;
                }
                ObjectId layMain = EnsureLayer(db, tr, LayerContour, 8);            // 주곡선 회색
                ObjectId layIdx = EnsureLayer(db, tr, LayerContourIndex, 30);       // 계곡선 주황
                // ★원본 선은 꺼 둔다 — 지표면이 제 등고선을 그리므로 두 벌이 겹친다(JACK 0901).
                HideLayer(db, tr, LayerContour);
                HideLayer(db, tr, LayerContourIndex);
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
                foreach (var ln in lines)
                {
                    try
                    {
                        var pl = new Polyline3d(Poly3dType.SimplePoly, ln.Pts, false);
                        pl.LayerId = ln.IsIndex ? layIdx : layMain;
                        ms.AppendEntity(pl);
                        tr.AddNewlyCreatedDBObject(pl, true);
                        ids.Add(pl.ObjectId);
                    }
                    catch { }
                }
                tr.Commit();
            }
            ed.WriteMessage($"\n[등고선] {ids.Count}가닥 작도 완료");

            // ── "원지반" 지표면 자동 생성 ──
            string surfNote = BuildGroundSurface(db, ed, ids);
            DrawOrderFix.Apply(db);
            ed.Regen();

            string done = $"등고선 {ids.Count}가닥 · {surfNote}" + (cut ? $" · ⚠상한 {MaxContourRows} 도달(범위 축소 권장)" : "");
            ed.WriteMessage($"\n[등고선] {done}");
            AcadApp.ShowAlertDialog("등고선 가져오기 완료\n\n" + diag + "\n" + surfNote +
                (cut ? "\n\n⚠ 자료가 많아 일부만 가져왔습니다 — 범위를 좁혀 다시 받으세요." : ""));
            try { DiagLog.Append($"\n■ DHCONTOUR — {diag} · {surfNote} · {csNote}\n"); } catch { }
            return true;
        }
        catch (System.Exception ex)
        {
            ed.WriteMessage("\n[등고선 오류] " + ex.Message);
            AcadApp.ShowAlertDialog("등고선 가져오기 중 오류:\n" + ex.Message);
            return false;
        }
    }

    // ── 지적도 ────────────────────────────────────────────────────────────────
    [CommandMethod("DHPARCEL")]
    public void RunParcel()
    {
        Document doc = AcadApp.DocumentManager.MdiActiveDocument;
        if (doc == null) return;
        GradingSettings.SyncToDocument(doc);   // [도면 전환 0803] 도면이 바뀌었으면 그 도면 기준으로 설정·기억 재정렬
        Editor ed = doc.Editor;
        Database db = doc.Database;

        try
        {
            // ★좌표계를 <b>먼저</b> 정한다 — 지도에서 고른 박스도 이 원점으로 옮겨야 하기 때문이다.
            int epsg = ResolveEpsg(db, out string csNote);
            ed.WriteMessage($"\n[지적도] 좌표계: {csNote}");
            // ★★[v109.1 · 검토 H1] 지적은 이제 VWorld — <b>키부터 본다</b>(범위를 다 고른 뒤 «키 없음»은 못 할 짓 · 0901 교훈 그대로). 사내 DB(VPN)는 안 본다.
            if (VWorldKey.Current.Length == 0) { Refuse(ed, "지적도", VWorldKey.NoKeyHelp); return; }
            // ★[JACK 0901 "지적도 버튼 눌러서 하는 건 그냥 캐드상 드래그로"]
            //   지적도는 이미 현장이 있는 도면에서 쓰는 것이라 두 점이 더 빠르다.
            if (!TwoPoints(ed, "지적도", out double x0, out double y0, out double x1, out double y1)) return;
            ImportParcelBox(doc, epsg, csNote, x0, y0, x1, y1, alone: true);
        }
        catch (System.Exception ex)
        {
            ed.WriteMessage("\n[지적도 오류] " + ex.Message);
            AcadApp.ShowAlertDialog("지적도 가져오기 중 오류:\n" + ex.Message);
        }
    }

    /// <summary>★★★[JACK 0901] <b>범위를 이미 안다</b>는 전제로 지적도를 받아 그린다.
    /// <para>지도 도킹바에서 [지적도]를 체크해 두면 지표면과 <b>같은 범위로 같이</b> 들어온다.</para>
    /// <para>★★[v109.1 · 계획_v109.md 3 · JACK 1028 «지적은 VWorld로 · 지번은 속성에 · 국유지/사유지 구분이 중요»]
    /// 사내 DB 대신 <b>VWorld</b>(연속지적 + 토지소유정보) · 필지마다 속성(XData <c>DH_PARCEL</c> + Map 개체 데이터) · <b>국공유지 = 하늘색 반투명 해치</b>
    /// · 지번 글자 = 지번 + 지목(«645-1전» — 지적도 표기). <b>다 받은 뒤에</b> 옛 지적을 지운다 — 하나라도 못 받으면 도면은 그대로(검토 M2).</para>
    /// <param name="alone">혼자 부른 것인가 — 그때만 완료 대화상자를 띄운다.
    ///   지표면과 같이 올 때는 <b>대화상자가 둘</b>이면 성가시다.</param></summary>
    internal static void ImportParcelBox(Document doc, int epsg, string csNote,
                                         double x0, double y0, double x1, double y1, bool alone)
    {
        Editor ed = doc.Editor;
        Database db = doc.Database;
        using var dlAll2 = SafeLock(doc);
        try
        {
            string key = VWorldKey.Current;
            if (key.Length == 0) { Refuse(ed, "지적도", VWorldKey.NoKeyHelp); return; }
            var box = new VWorldParcels.Box(x0, y0, x1, y1);
            ed.WriteMessage($"\n[지적도] VWorld에서 받는 중…({VWorldKey.Source} · {csNote})");
            VWorldCadastral.Got? got = null; string fail = ""; bool keyErr = false;
            using (BusyPopup.Scope("지적 받는 중…"))
            {
                try { got = VWorldCadastral.Fetch(box, epsg, key, MaxParcelRows, BusyPopup.Text); }
                catch (VWorldCadastral.FetchException fx) { fail = fx.Message; keyErr = fx.KeyError; }
                catch (System.Exception ex) { fail = ex.GetType().Name + " — " + ex.Message; }
            }
            if (got == null)
            {
                try { DiagLog.Append($"\n■ DHPARCEL — 못 받음(도면 그대로) · {fail} · {csNote}\n"); } catch { }
                Refuse(ed, "지적도", "지적도를 받지 못했습니다 — 도면의 지적도는 그대로입니다.\n\n" + fail);
                return;
            }
            ed.WriteMessage("\n[지적도] " + got.Diag);
            var res = VWorldParcels.Assemble(got.Raws, got.Own, box);
            if (res.Parcels.Count == 0)
            {
                Refuse(ed, "지적도", "이 범위에는 지적 자료가 없습니다.\n" +
                                     "좌표계(원점)가 맞는지, 범위가 국내인지 확인하세요.");
                return;
            }

            const double txtH = 1.0;   // 지번 글자 크기 — 1.0 고정(JACK 0731)
            string today = System.DateTime.Now.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
            int nLine = 0, nHole = 0, nText = 0, nFill = 0, fillFail = 0, eraseFail = 0, nOd = 0;
            string odNote = "";
            using (var tr = db.TransactionManager.StartTransaction())
            {
                // ★못 지웠으면 <b>새로 그리지 않는다</b> — 그리면 옛것 위에 겹쳐 두 벌이 된다.
                //   (트랜잭션을 커밋 안 하고 나가므로 도면은 <b>건드린 자국조차 안 남는다</b>.)
                eraseFail = EraseOnLayers(db, tr, ParcelLayers);   // 다시 불러오면 교체(선 · 지번 · 국공유지 해치 — 한 목록)
                if (eraseFail > 0) goto erasedFailed;
                ObjectId layP = EnsureLayer(db, tr, LayerParcel, 2);        // 필지 노란색(JACK 0731)
                ObjectId layT = EnsureLayer(db, tr, LayerJibun, 7);         // 지번 흰색(별도 레이어 — JACK)
                bool newFill = !((LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead)).Has(LayerParcelPublic);
                ObjectId layH = EnsureLayer(db, tr, LayerParcelPublic, 4);  // 국공유지 하늘색
                // 투명 60 % — 처음 만들 때만(사람이 바꾼 값은 안 덮음) · 해치는 «도면층별»을 따른다
                if (newFill) try { ((LayerTableRecord)tr.GetObject(layH, OpenMode.ForWrite)).Transparency = new Transparency((byte)102); } catch { }
                GradingBuilder.EnsureRegApp(db, tr, ParcelXData.App);
                // [JACK 0731] 지번이 '?'로 깨지는 문제 — 기본 글꼴(txt.shx)이 한글을 못 그린다('산12-1' 등).
                //   한글 트루타입 글꼴 스타일을 만들어 지번 문자에 지정한다.
                ObjectId styleId = EnsureKoreanTextStyle(db, tr);
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);

                // ★[M9] 특성 창용 개체 데이터 — 안 되는 판이면 XData만(로그) · 한 줄이 느리면(앞 100줄 평균 5 ms 넘게) 그 뒤는 끔
                ParcelObjectData? od = null;
                if (GradingSettings.ParcelObjectData)
                {
                    try { od = OpenParcelOd(db); }
                    catch (System.Exception oex) { odNote = "특성 창 표시(개체 데이터) 못 함 — " + oex.GetType().Name; }
                }
                var odWatch = new System.Diagnostics.Stopwatch();
                void Od(Autodesk.AutoCAD.DatabaseServices.DBObject o, VWorldParcels.Parcel p)
                {
                    if (od == null) return;
                    try
                    {
                        odWatch.Start(); od.Attach(o, p, today); odWatch.Stop(); nOd++;
                        if (nOd == 100 && odWatch.ElapsedMilliseconds > 500) { od = null; odNote = $"개체 데이터가 느려(100줄 {odWatch.ElapsedMilliseconds} ms) 앞 100줄만 넣음"; }
                    }
                    catch (System.Exception oex) { odWatch.Stop(); od = null; odNote = "개체 데이터를 넣다 멈춤 — " + oex.GetType().Name; }
                }

                foreach (var p in res.Parcels)
                {
                    for (int i = 0; i < p.Pieces.Count; i++)
                    {
                        var pc = p.Pieces[i];
                        // 바깥 고리 · 구멍 — 닫힌 폴리선(XData에 역할 · 조각 번호)
                        foreach (var (ring, role) in new[] { (pc.Shell, "outer") }.Concat(pc.Holes.Select(h => (h, "hole"))))
                        {
                            try
                            {
                                var pl = new Polyline(ring.Count) { Closed = true, LayerId = layP };
                                for (int k = 0; k < ring.Count; k++) pl.AddVertexAt(k, new Point2d(ring[k].X, ring[k].Y), 0, 0, 0);
                                ms.AppendEntity(pl); tr.AddNewlyCreatedDBObject(pl, true);
                                pl.XData = ParcelXData.Build(p, role, i, today);
                                if (role == "outer") { nLine++; Od(pl, p); } else nHole++;
                            }
                            catch { }
                        }
                        // 국공유지(일부 국공유는 빗금) — 비연관 해치(바깥 + 구멍 고리)
                        var kind = p.Own.Kind;
                        if (kind != VWorldParcels.OwnKind.Public && kind != VWorldParcels.OwnKind.Mixed) continue;
                        Hatch? h = null;
                        try
                        {
                            h = new Hatch { LayerId = layH, Associative = false };
                            ms.AppendEntity(h); tr.AddNewlyCreatedDBObject(h, true);
                            if (kind == VWorldParcels.OwnKind.Public) h.SetHatchPattern(HatchPatternType.PreDefined, "SOLID");
                            else { h.PatternScale = 1.0; h.SetHatchPattern(HatchPatternType.PreDefined, "ANSI31"); }
                            // ★바깥 고리 = External — Outermost · Default로 주면 해치 넓이를 못 읽는다(eNotApplicable · 특성 창 넓이 빈칸 — 1029 화면 없는 Civil 실험 10×10 네모: External만 100.0)
                            h.AppendLoop(HatchLoopTypes.External | HatchLoopTypes.Polyline, Loop(pc.Shell), Bulges(pc.Shell.Count + 1));
                            foreach (var hr in pc.Holes) h.AppendLoop(HatchLoopTypes.Default | HatchLoopTypes.Polyline, Loop(hr), Bulges(hr.Count + 1));
                            h.EvaluateHatch(true);
                            h.XData = ParcelXData.Build(p, "fill", i, today);
                            Od(h, p);
                            nFill++;
                        }
                        catch { fillFail++; try { h?.Erase(); } catch { } }
                    }
                    // 지번 글자 «645-1전» — 한 필지에 한 번 · 가장 큰 조각의 안쪽 점(오목해도 안쪽)
                    if (p.LabelText.Length == 0) continue;
                    try
                    {
                        var at = new Point3d(p.Label.X, p.Label.Y, 0);
                        var t = new DBText
                        {
                            TextString = p.LabelText,
                            Position = at,
                            Height = txtH,
                            HorizontalMode = TextHorizontalMode.TextCenter,
                            VerticalMode = TextVerticalMode.TextVerticalMid,
                            AlignmentPoint = at,
                            LayerId = layT,
                        };
                        if (!styleId.IsNull) t.TextStyleId = styleId;   // 한글 글꼴
                        ms.AppendEntity(t); tr.AddNewlyCreatedDBObject(t, true);
                        t.XData = ParcelXData.Build(p, "label", -1, today);
                        Od(t, p);
                        nText++;
                    }
                    catch { }
                }
                tr.Commit();
            }
            DrawOrderFix.Apply(db);   // 배경지도 위로 지번이 보이게(JACK 0731) · 국공유지 해치는 이미지 바로 위
            ed.Regen();
            goto drawn;

        erasedFailed:
            ed.WriteMessage($"\n[지적도] 이전 지적도 {eraseFail}개를 못 지웠습니다 — 새로 그리지 않았습니다."
                + $"\n  '{LayerParcel}'·'{LayerJibun}'·'{LayerParcelPublic}' 레이어의 잠금을 풀고 다시 하세요.");
            AcadApp.ShowAlertDialog("지적도를 다시 가져오지 못했습니다.\n\n"
                + $"이전 필지·지번 {eraseFail}개를 못 지웠습니다(레이어 잠금 확인).\n"
                + $"'{LayerParcel}'·'{LayerJibun}'·'{LayerParcelPublic}' 잠금을 풀고 다시 시도하세요.\n\n"
                + "※그냥 그리면 옛것 위에 겹쳐 두 벌이 됩니다.");
            try { DiagLog.Append($"\n■ DHPARCEL — 안 그렸다(이전 것 {eraseFail}개를 못 지움 · 레이어 잠금)\n"); } catch { }
            return;

        drawn:
            // ★[H4] «소유 자료 없음» ≠ 사유지 — 칠하지 않은 까닭을 숨기지 않고 센다
            string own = $"국공유지 {res.Public:N0} · 사유지 {res.Private:N0}" + (res.Mixed > 0 ? $" · 일부 국공유 {res.Mixed:N0}" : "")
                       + (res.NoOwn > 0 ? $" · 소유 자료 없음 {res.NoOwn:N0}" : "");
            var warn = new List<string>();
            if (res.NoOwn > 0) warn.Add($"소유 자료 없음 {res.NoOwn:N0}필지는 칠하지 않았습니다(사유지라는 뜻이 아님)");
            if (got.WfsCut > 0) warn.Add("필지가 아주 많은 곳은 소유 정보가 일부 빠졌을 수 있습니다");
            if (got.LookupFail > 0 || got.LookupSkipped > 0) warn.Add($"공유 필지 소유 조회 {got.LookupFail + got.LookupSkipped:N0}건을 못 했습니다");
            if (fillFail > 0) warn.Add($"국공유지 해치 {fillFail:N0}개를 못 만들었습니다(선은 있음)");
            if (odNote.Length > 0) warn.Add(odNote);
            string done = $"필지 {res.Parcels.Count:N0}개({own}) · 선 {nLine:N0}" + (nHole > 0 ? $"(구멍 {nHole:N0})" : "")
                        + $" · 지번 {nText:N0} · 해치 {nFill:N0}"
                        + (res.DupRows > 0 ? $" · 상자 경계 겹침 {res.DupRows:N0} 걷음" : "") + (res.Fixed > 0 ? $" · 도형 고침 {res.Fixed:N0}" : "")
                        + (res.JibunMismatch > 0 ? $" · 지번 글자 ≠ PNU {res.JibunMismatch:N0}(PNU 씀)" : "");
            ed.WriteMessage($"\n[지적도] {done}");
            foreach (var w in warn) ed.WriteMessage($"\n  ⚠ {w}");
            // ★지표면과 같이 올 때는 대화상자를 안 띄운다 — 둘이 연달아 뜨면 성가시다.
            if (alone)
                AcadApp.ShowAlertDialog($"지적도 가져오기 완료\n\n필지 {res.Parcels.Count:N0}개 — {own}"
                    + (warn.Count > 0 ? "\n\n⚠ " + string.Join("\n⚠ ", warn) : "")
                    + "\n\n※ 출처: VWorld(연속지적 · 토지소유정보)");
            try { DiagLog.Append($"\n■ DHPARCEL — {done} · {got.Diag} · 개체 데이터 {nOd:N0}" + (warn.Count > 0 ? " · ⚠ " + string.Join(" · ", warn) : "") + $" · {csNote}\n"); } catch { }
        }
        catch (System.Exception ex)
        {
            ed.WriteMessage("\n[지적도 오류] " + ex.Message);
            AcadApp.ShowAlertDialog("지적도 가져오기 중 오류:\n" + ex.Message);
        }
    }

    /// <summary>Map 개체 데이터 표 — 따로 부른다(Map 모듈이 없는 판이면 여기서만 터진다 · 부르는 쪽이 잡음).</summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static ParcelObjectData OpenParcelOd(Database db) => ParcelObjectData.Open(db);

    /// <summary>해치 폴리선 고리 — 닫는 점까지.</summary>
    private static Point2dCollection Loop(List<(double X, double Y)> r)
    {
        var c = new Point2dCollection();
        foreach (var (x, y) in r) c.Add(new Point2d(x, y));
        c.Add(new Point2d(r[0].X, r[0].Y));
        return c;
    }

    private static DoubleCollection Bulges(int n)
    {
        var b = new DoubleCollection();
        for (int i = 0; i < n; i++) b.Add(0);
        return b;
    }

    // ── 공통 ──────────────────────────────────────────────────────────────────

    /// <summary>등고선 폴리선들로 "원지반" TIN 지표면 생성(있으면 교체). 반환=안내문.</summary>
    /// <summary>문서 잠금 — 실패해도 넘어간다(명령 안이면 이미 잠겨 있다).</summary>
    private static IDisposable SafeLock(Document doc)
    {
        try { return doc?.LockDocument(); } catch { return null; }
    }

    /// <summary>★<b>원지반을 고르는 규칙은 여기 하나</b>다(§50).
    /// <para>"우리 산출물이 아닌 지표면 중 삼각형이 제일 많은 것" — 이름을 못 박지 않는 이유는
    /// 사용자가 직접 만든 지표면일 수도 있어서다. 이 판정이 <b>세 곳에 따로</b> 있었고
    /// 한 곳만 고쳐져 있었다(검토 0901) — 그래서 인프라웍스가 지층면을 원지반으로 오인했다.</para></summary>
    internal static ObjectId FindGroundSurface(Database db, out string name, out int tris)
    {
        name = ""; tris = 0;
        ObjectId best = ObjectId.Null;
        try
        {
            var civilDoc = Autodesk.Civil.ApplicationServices.CivilApplication.ActiveDocument;
            using var tr = db.TransactionManager.StartTransaction();
            foreach (ObjectId sid in civilDoc.GetSurfaceIds())
            {
                try
                {
                    if (tr.GetObject(sid, OpenMode.ForRead) is not Autodesk.Civil.DatabaseServices.TinSurface ts) continue;
                    string nm = ts.Name ?? "";
                    if (nm.Contains("_DH") || nm.StartsWith("DH_", System.StringComparison.Ordinal)) continue;
                    int n = 0; try { n = ts.Triangles.Count; } catch { }
                    if (n > tris) { tris = n; best = sid; name = nm; }
                }
                catch { }
            }
            tr.Commit();
        }
        catch { }
        return best;
    }

    /// <summary>원지반이 있는가 — 리본 단추를 켜고 끄는 데 쓴다.</summary>
    internal static bool HasGroundSurface(Database db)
        => !FindGroundSurface(db, out _, out int t).IsNull && t > 0;

    /// <summary>수치지도 명령도 이것을 쓴다 — <b>원지반을 만드는 규칙은 한 벌</b>이다(§50).</summary>
    /// <param name="spotIds">표고점(DBPoint). ★★<b>등고선과 같은 자루에 넣으면 안 된다</b>(검토 0901) —
    ///   등고선 정의는 <b>선만</b> 받으므로 점은 조용히 무시된다. 봉우리·안부·계곡 바닥처럼
    ///   등고선 사이에 표고점만 있는 자리가 <b>납작해지는데</b> "표고점 851개"라고 보고되어 맞아 보인다.</param>
    internal static string BuildGroundSurfaceFrom(Database db, Editor ed,
                                                  ObjectIdCollection contourIds,
                                                  ObjectIdCollection spotIds = null)
        => BuildGroundSurface(db, ed, contourIds, spotIds);

    private static string BuildGroundSurface(Database db, Editor ed, ObjectIdCollection contourIds,
                                             ObjectIdCollection spotIds = null)
    {
        if (contourIds.Count == 0) return "지표면 생략(등고선 없음)";
        try
        {
            using (var tr0 = db.TransactionManager.StartTransaction())
            {
                GradingBuilder.EraseSurfacesByBaseName(tr0, GroundSurfaceName);   // 다시 불러오면 교체
                tr0.Commit();
            }
            // ★★★[검토 0901] <b>이름이 정말 비었는지 보고 만든다.</b>
            //   지우기가 실패해도(다른 것이 이 지표면을 물고 있으면 그렇다) 조용히 넘어가는데,
            //   그 상태로 같은 이름을 만들면 터진다 — 그때는 <b>옛 등고선은 이미 지웠고</b>
            //   <b>레이어도 이미 꺼 놓은</b> 뒤라 화면에 낡은 지표면만 남고 사유를 알 수 없다.
            string useName = GroundSurfaceName;
            try
            {
                using var trChk = db.TransactionManager.StartTransaction();
                if (GradingBuilder.SurfaceExistsByBaseName(trChk, GroundSurfaceName))
                {
                    useName = GradingBuilder.UniqueName(db, trChk, GroundSurfaceName);
                    ed.WriteMessage($"\n[지표면] 옛 '{GroundSurfaceName}'을 못 지웠습니다"
                                  + $" — 새 이름 '{useName}'으로 만듭니다(옛것을 지우고 다시 하세요).");
                }
                trChk.Commit();
            }
            catch { }
            ObjectId surfId = TinSurface.Create(db, useName);
            using var tr = db.TransactionManager.StartTransaction();
            var surf = (TinSurface)tr.GetObject(surfId, OpenMode.ForWrite);
            // [JACK 0731 — 지표면이 뭉뚱그려짐] 정밀도 3종 세트:
            //   ① weeding 사실상 끔(0.01m·0.1°) — 원본 등고선 정점을 버리지 않는다.
            //   ② supplementing 5m(중간종거 0.1m) — 등고선을 따라 점을 보충해 삼각형을 잘게.
            //   ③ **평탄부 최소화 전부 켬** — 능선·계곡·정상부에서 생기는 납작한 삼각형을 점 추가·엣지 교환으로 해소.
            //      (등고선만으로 만드는 지표면이 뭉뚱그려 보이는 주된 원인이 이 평탄 삼각형이다.)
            //   ※ 보충점은 **등고선 위**에 찍히므로 표고가 그 등고선 값 그대로다(없는 표고를 지어내지 않음 — JACK 조건).
            //      원본 정점 간격이 평균 4.0m라 3m 기준이면 긴 직선 구간에만 점이 보태진다.
            var flat = new SurfaceMinimizeFlatAreaOptions(true, true, true, true);
            try { surf.ContoursDefinition.AddContours(contourIds, 0.05, 3.0, 0.01, 0.1, flat); }
            catch
            {
                // 옵션 조합을 거부하는 환경 대비 — 평탄부 최소화만 유지하고 보완값을 완화.
                try { surf.ContoursDefinition.AddContours(contourIds, 1.0, 15.0, 0.1, 1.0, flat); }
                catch { surf.ContoursDefinition.AddContours(contourIds, 1.0, 100.0, 0.3, 1.0); }
            }
            // [JACK 0731] 처음 만들어질 때 스타일은 삼각망이 아니라 **등고선**(주 10m·보조 2m — JACK 0901)으로.
            try
            {
                ObjectId stId = EnsureContourStyle(tr);
                if (!stId.IsNull) surf.StyleId = stId;
            }
            catch (System.Exception sex) { ed.WriteMessage("\n[등고선] 지표면 스타일 적용 생략 — " + sex.Message); }
            // ★표고점은 <b>따로</b> 넣는다 — 도면객체(점) 정의가 그 길이다.
            //   실패해도 등고선으로 만든 지표면은 살려야 하므로 여기만 따로 감싼다.
            string spotNote = "";
            if (spotIds != null && spotIds.Count > 0)
            {
                try
                {
                    surf.DrawingObjectsDefinition.AddFromPoints(spotIds, "표고점");
                    spotNote = $" · 표고점 {spotIds.Count}개 반영";
                }
                catch (System.Exception pex)
                {
                    spotNote = " · ⚠표고점 반영 실패";
                    ed.WriteMessage("\n[지표면] 표고점을 못 넣었습니다 — " + pex.Message);
                }
            }
            tr.Commit();

            int pts = 0, tris = 0;
            try
            {
                using var trS = db.TransactionManager.StartTransaction();
                var s = (TinSurface)trS.GetObject(surfId, OpenMode.ForRead);
                try { pts = s.Vertices.Count; } catch { }
                try { tris = s.Triangles.Count; } catch { }
                trS.Commit();
            }
            catch { }
            return $"'{useName}' 지표면 생성(점 {pts}·삼각형 {tris}){spotNote}";
        }
        catch (System.Exception ex)
        {
            ed.WriteMessage("\n[등고선] 지표면 생성 실패 — " + ex.Message);
            return "지표면 생성 실패: " + ex.Message;
        }
    }

    /// <summary>[JACK 0731] '원지반' 전용 지표면 스타일 확보 — 삼각망 대신 등고선 표시.
    ///   · 보조등고선 <see cref="MinorInterval"/>m · 주등고선 <see cref="MajorInterval"/>m
    ///   · 표시 항목은 등고선 + 경계만(경계를 켜둬야 클릭으로 지표면을 집을 수 있다)
    /// 이름이 같은 스타일이 이미 있으면 그것을 갱신해 쓴다(중복 생성 방지). 실패하면 Null → 기본 스타일 유지.</summary>
    /// <summary>★[JACK 0901 "등고선 간격 그냥 DXF나 서버나 <b>10m에 2m</b>로 해 줘"]
    /// <para>처음엔 25·5(지형 훑어보기), 그다음 5·1(너무 촘촘)을 거쳐 여기로 왔다.
    /// <b>DXF든 서버든 같은 값</b>이다 — 어디서 받았느냐로 도면이 달라 보이면 안 된다.</para></summary>
    private const double MinorInterval = 2.0;
    private const double MajorInterval = 10.0;
    /// <summary>지표면 스타일 이름 — <b>간격을 이름에 넣지 않는다</b>(검토 0901).
    /// <para>예전 이름은 "(5·25)"였는데 간격을 5·1로 바꾸자 이름만 거짓말이 됐다.</para></summary>
    internal const string GroundStyleName = "DH-원지반 등고선";

    /// <summary>옛 이름 — 이미 이 이름으로 만들어진 도면이 있어 <b>찾아서 이어 쓴다</b>.
    /// <para>안 그러면 스타일 목록에 둘이 남고, 사람은 어느 것이 사는지 모른다.</para></summary>
    private const string GroundStyleNameOld = "DH-원지반 등고선(5·25)";

    /// <summary>등고선 한 종류의 색을 <b>번호로</b> 못 박는다(평면·모델 둘 다).</summary>
    private static void SetContourColor(Autodesk.Civil.DatabaseServices.Styles.SurfaceStyle st,
                                        Autodesk.Civil.DatabaseServices.Styles.SurfaceDisplayStyleType t,
                                        short aci)
    {
        var c = Autodesk.AutoCAD.Colors.Color.FromColorIndex(
                    Autodesk.AutoCAD.Colors.ColorMethod.ByAci, aci);
        try { st.GetDisplayStylePlan(t).Color = c; } catch { }
        try { st.GetDisplayStyleModel(t).Color = c; } catch { }
    }

    /// <summary>자르기도 이 스타일을 쓴다 — 잘라 만든 지표면은 기본 스타일로 나오기 때문이다(§50).</summary>
    internal static ObjectId EnsureGroundStyle(Transaction tr) => EnsureContourStyle(tr);

    private static ObjectId EnsureContourStyle(Transaction tr)
    {
        var cdoc = Autodesk.Civil.ApplicationServices.CivilApplication.ActiveDocument;
        var styles = cdoc.Styles.SurfaceStyles;

        ObjectId id = ObjectId.Null;
        foreach (ObjectId sid in styles)
        {
            try
            {
                if (tr.GetObject(sid, OpenMode.ForRead) is Autodesk.Civil.DatabaseServices.Styles.SurfaceStyle s0 &&
                    string.Equals(s0.Name, GroundStyleName, System.StringComparison.OrdinalIgnoreCase))
                { id = sid; break; }
            }
            catch { }
        }
        // 옛 이름으로 만들어 둔 것이 있으면 <b>이름만 갈아</b> 이어 쓴다 — 둘로 늘리지 않는다.
        if (id.IsNull)
        {
            foreach (ObjectId sid in styles)
            {
                try
                {
                    if (tr.GetObject(sid, OpenMode.ForRead) is Autodesk.Civil.DatabaseServices.Styles.SurfaceStyle sOld &&
                        string.Equals(sOld.Name, GroundStyleNameOld, System.StringComparison.OrdinalIgnoreCase))
                    {
                        sOld.UpgradeOpen();
                        sOld.Name = GroundStyleName;
                        id = sid;
                        break;
                    }
                }
                catch { }
            }
        }
        if (id.IsNull) id = styles.Add(GroundStyleName);
        if (id.IsNull) return ObjectId.Null;

        var st = (Autodesk.Civil.DatabaseServices.Styles.SurfaceStyle)tr.GetObject(id, OpenMode.ForWrite);
        try { st.ContourStyle.MinorContourInterval = MinorInterval; } catch { }
        try { st.ContourStyle.MajorContourInterval = MajorInterval; } catch { }

        // 표시 항목 정리 — 등고선·경계만 켜고 나머지(삼각망·점·경사 등)는 전부 끈다.
        //   열거값 이름을 일일이 적지 않고 전체를 돌며 처리(버전별 항목 차이에 안전).
        var dt = typeof(Autodesk.Civil.DatabaseServices.Styles.SurfaceDisplayStyleType);
        foreach (Autodesk.Civil.DatabaseServices.Styles.SurfaceDisplayStyleType t in System.Enum.GetValues(dt))
        {
            bool on = t == Autodesk.Civil.DatabaseServices.Styles.SurfaceDisplayStyleType.MajorContour
                   || t == Autodesk.Civil.DatabaseServices.Styles.SurfaceDisplayStyleType.MinorContour
                   || t == Autodesk.Civil.DatabaseServices.Styles.SurfaceDisplayStyleType.Boundary;
            try { st.GetDisplayStylePlan(t).Visible = on; } catch { }
            try { st.GetDisplayStyleModel(t).Visible = on; } catch { }
        }

        // ★★★[JACK 0901 "주등고선은 색상 9, 보조등고선은 색상 8"]
        //   <b>색은 반드시 번호로 못 박는다.</b> 이 저장소가 여러 번 데인 자리인데(§56),
        //   Civil 객체는 <b>스타일 → 표시 레이어 → 뷰별 재정의</b> 세 층을 지나고,
        //   가운데 어디든 <c>ByLayer</c>가 있으면 <b>레이어 색이 이긴다</b> —
        //   그러면 여기서 아무 색을 줘도 화면은 안 바뀐다.
        SetContourColor(st, Autodesk.Civil.DatabaseServices.Styles.SurfaceDisplayStyleType.MajorContour, 9);
        SetContourColor(st, Autodesk.Civil.DatabaseServices.Styles.SurfaceDisplayStyleType.MinorContour, 8);
        return id;
    }

    /// <summary>도면에서 두 모서리를 찍는다(현재 UCS → WCS).</summary>
    private static bool TwoPoints(Editor ed, string label, out double x0, out double y0,
                                  out double x1, out double y1)
    {
        x0 = y0 = x1 = y1 = 0;
        var p1 = ed.GetPoint($"\n[{label}] 가져올 범위 첫 번째 모서리 클릭 (Esc=취소): ");
        if (p1.Status != PromptStatus.OK) return false;
        var p2 = ed.GetCorner(new PromptCornerOptions("\n반대쪽 모서리 클릭: ", p1.Value));
        if (p2.Status != PromptStatus.OK) return false;
        var ucs = ed.CurrentUserCoordinateSystem;
        var w1 = p1.Value.TransformBy(ucs);
        var w2 = p2.Value.TransformBy(ucs);
        x0 = System.Math.Min(w1.X, w2.X); x1 = System.Math.Max(w1.X, w2.X);
        y0 = System.Math.Min(w1.Y, w2.Y); y1 = System.Math.Max(w1.Y, w2.Y);
        return Sane(ed, label, x0, y0, x1, y1);
    }

    /// <summary>★엔진이 없는 PC용 — <b>예전 브라우저 방식</b>으로 지도를 열어 가져온다.</summary>
    [CommandMethod("DHCONTOURWEB")]
    public void RunContourViaBrowser()
    {
        Document doc = AcadApp.DocumentManager.MdiActiveDocument;
        if (doc == null) return;
        GradingSettings.SyncToDocument(doc);
        Editor ed = doc.Editor;
        int epsg = ResolveEpsg(doc.Database, out string csNote);
        ed.WriteMessage($"\n[등고선] 좌표계: {csNote}");
        if (!Reachable(ed)) return;
        var end = MapPickCommand.TryPick(ed, epsg, out double x0, out double y0,
                                         out double x1, out double y1, out string why);
        if (end != MapPickCommand.PickEnd.Got)
        {
            if (end != MapPickCommand.PickEnd.Cancelled)
                ed.WriteMessage($"\n  ⚠지도를 못 썼습니다 — {why}");
            return;
        }
        if (!Sane(ed, "등고선", x0, y0, x1, y1)) return;
        ImportContourBox(doc, epsg, csNote, x0, y0, x1, y1);
    }

    /// <summary>범위가 쓸 만한가 — <b>지도든 두 점이든 여기 하나</b>를 지난다(§50).
    /// <para>★같은 자리를 두 번 클릭하면 0×0이 되는데, 그러면 자료가 0건이라
    /// "좌표계가 맞는지 확인하세요"가 뜬다 — <b>멀쩡한 좌표계를 의심하게 만든다</b>(검토 0901).</para></summary>
    internal static bool Sane(Editor ed, string label, double x0, double y0, double x1, double y1)
    {
        if (x1 - x0 >= 1.0 && y1 - y0 >= 1.0) return true;
        Refuse(ed, label, $"지정한 범위가 너무 작습니다(가로 {x1 - x0:F1}m × 세로 {y1 - y0:F1}m).\n\n" +
                          "모서리 두 곳을 서로 떨어뜨려 다시 골라 주세요.");
        return false;
    }

    /// <summary>도면 좌표계 우선, 없으면 정지옵션 값(배경지도와 동일 규칙).</summary>
    internal static int ResolveEpsg(Database db, out string note)
    {
        int optEpsg = GradingSettings.ExportEpsg;
        string csCode = KoreaCs.Read(db);
        int? det = KoreaCs.ResolveEpsgFromCode(csCode);
        if (KoreaCs.CodeForEpsg(optEpsg) == null)
        { note = $"정지옵션 EPSG:{optEpsg}(도면 좌표계로 표현 불가한 원점이라 옵션 값 사용)"; return optEpsg; }
        if (det.HasValue) { note = $"도면 좌표계 '{csCode}' → EPSG:{det.Value}"; return det.Value; }
        note = $"도면 좌표계 미지정 → 정지옵션 EPSG:{optEpsg}";
        return optEpsg;
    }

    /// <summary>사내망 접속 확인 — 실패 시 VPN 안내.</summary>
    private static bool Reachable(Editor ed)
    {
        if (GisDb.CanConnect(out string why)) return true;
        ed.WriteMessage("\n[가져오기] 사내 DB 접속 실패 — " + why);
        // ★[v109] 접속 정보가 설치 파일에 없으면 VPN 탓이 아니다 — 까닭을 갈라 말한다
        AcadApp.ShowAlertDialog(!DhKeys.Installed.GisDb.Ready
            ? "사내 지형·지적 데이터베이스 접속 정보가 설치돼 있지 않습니다.\n\n"
              + "사내 배포본으로 다시 설치하거나, 수치지형도 DXF를 [수치지도 DXF]로 넣어 주세요."
            : "사내 지형·지적 데이터베이스에 접속할 수 없습니다.\n\n" +
              "사내망(VPN)에 연결되어 있는지 확인해 주세요.\n\n상세: " + why);
        return false;
    }

    /// <summary>주어진 레이어의 객체를 <b>모두</b> 지운다. <b>못 지운 개수</b>를 돌려준다.
    ///
    /// <para>★★★[검토 0917 · JACK <i>"지적도 기능을 쓰면 문자랑 선이 중복돼서 들어와"</i>]
    /// <b>여기서 실패를 조용히 삼키고 있었다.</b> 이 함수를 부르는 쪽은 전부
    /// <i>"다시 불러오면 교체"</i>를 전제로 <b>지운 다음 바로 새로 그린다</b> —
    /// 그런데 레이어가 <b>잠겨 있으면</b> 지우기가 실패하고, 아무 신호 없이 그 위에 또 그린다.
    /// <b>옛것 한 벌 + 새것 한 벌 = 중복</b>이다. 선도 문자도 같이 겹친다(둘 다 이 함수로 지운다).</para>
    ///
    /// <para>★<b>같은 저장소가 이미 아는 실패다.</b> 형제 명령 <see cref="ParcelOffCommand"/>는
    /// 똑같은 두 레이어를 지우면서 <i>"★[검토 0902] 레이어가 잠겼으면 여기로 온다 — <b>숨기지 않는다</b>"</i>라고
    /// 적어 두고 실패를 세어 경고까지 띄운다. <b>가져오기 쪽만 그 교훈이 빠져 있었다</b> —
    /// 이 저장소가 되풀이해 겪은 <b>"두 경로가 다른 자를 쓴다"</b>가 또 난 것이다.</para></summary>
    internal static int EraseOnLayers(Database db, Transaction tr, string[] layers)
    {
        var want = new System.Collections.Generic.HashSet<string>(layers, System.StringComparer.OrdinalIgnoreCase);
        var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
        var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);
        var victims = new System.Collections.Generic.List<ObjectId>();
        foreach (ObjectId id in ms)
        {
            try { if (tr.GetObject(id, OpenMode.ForRead) is AcadEntity e && want.Contains(e.Layer)) victims.Add(id); }
            catch { }
        }
        int fail = 0;
        foreach (var id in victims)
        {
            try { (tr.GetObject(id, OpenMode.ForWrite) as AcadEntity)?.Erase(); }
            catch { fail++; }
        }
        return fail;
    }

    /// <summary>[JACK 0731] 한글 글꼴 텍스트 스타일 확보 — 지번의 '산' 같은 한글이 '?'로 깨지는 것 방지.
    /// AutoCAD 기본 스타일은 txt.shx(한글 미지원)라 트루타입 '맑은 고딕'을 쓰는 스타일을 만든다.
    /// 실패하면 Null 반환(문자는 기본 스타일로 — 기능은 계속).</summary>
    internal static ObjectId EnsureKoreanTextStyle(Database db, Transaction tr)
    {
        const string styleName = "DH-한글";
        try
        {
            var st = (TextStyleTableRecord)null!;
            var tst = (TextStyleTable)tr.GetObject(db.TextStyleTableId, OpenMode.ForRead);
            if (tst.Has(styleName)) return tst[styleName];
            tst.UpgradeOpen();
            st = new TextStyleTableRecord { Name = styleName };
            // 트루타입 지정: FileName(글꼴 파일) + Font(타입페이스·한글 charset 129)
            try { st.FileName = "malgun.ttf"; } catch { }
            try
            {
                st.Font = new Autodesk.AutoCAD.GraphicsInterface.FontDescriptor(
                    "Malgun Gothic", false, false, 129, 34);   // 129=한글 charset
            }
            catch { }
            ObjectId id = tst.Add(st);
            tr.AddNewlyCreatedDBObject(st, true);
            return id;
        }
        catch { return ObjectId.Null; }
    }

    /// <summary>★★★[JACK 0901 "원본 선을 아예 안 그리게 하고"]
    /// <para><b>지우지 않고 <u>끈다</u>.</b> 지표면은 이 선들을 <b>자료로 물고 있어서</b>
    /// 지우면 정의가 끊긴다(다시 만들기를 하면 자료를 못 찾는다).
    /// 레이어를 꺼 두면 화면에서는 사라지고 지표면은 멀쩡하다 —
    /// 보고 싶으면 레이어만 켜면 된다.</para>
    /// <para>왜 겹치는가: 원지반 지표면이 <b>제 등고선을 따로 그린다</b>(주 10m·보조 2m).
    /// 원본까지 보이면 두 벌이 겹쳐 도면이 지저분해진다.</para></summary>
    internal static void HideLayer(Database db, Transaction tr, string name)
    {
        try
        {
            var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            if (!lt.Has(name)) return;
            var ltr = (LayerTableRecord)tr.GetObject(lt[name], OpenMode.ForWrite);
            // ★<b>끄기(Off)</b>지 동결(Freeze)이 아니다 — 동결된 레이어는 지표면 자료로 못 쓰는 판이 있다.
            if (!ltr.IsOff) ltr.IsOff = true;
        }
        catch { }
    }

    internal static ObjectId EnsureLayer(Database db, Transaction tr, string name, short aci)
    {
        var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
        if (lt.Has(name)) return lt[name];
        lt.UpgradeOpen();
        var ltr = new LayerTableRecord { Name = name, Color = Color.FromColorIndex(ColorMethod.ByAci, aci) };
        ObjectId id = lt.Add(ltr);
        tr.AddNewlyCreatedDBObject(ltr, true);
        return id;
    }

    private static void Refuse(Editor ed, string label, string msg)
    {
        ed.WriteMessage($"\n[{label}] " + msg.Replace("\n", " "));
        AcadApp.ShowAlertDialog(msg);
    }
}
