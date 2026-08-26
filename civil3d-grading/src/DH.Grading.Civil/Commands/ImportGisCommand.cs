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
/// [가져오기 — JACK 0731] 사내 PostGIS에서 수치지형도 등고선·연속지적을 도면 좌표계로 가져온다.
///  · DHCONTOUR : 범위 두 점 클릭 → 3D 등고선 작도 → **"원지반" TIN 지표면 자동 생성**
///  · DHPARCEL  : 범위 두 점 클릭 → 필지 경계선(사각 범위대로 클립) + 지번 문자(별도 레이어, 글자 1.0)
/// 좌표계는 항상 정지옵션/도면 설정을 따른다(DB가 5186 → 도면 좌표계로 변환해서 보내줌).
/// </summary>
public sealed class ImportGisCommand
{
    internal const string LayerContour = "DH-등고선";
    internal const string LayerContourIndex = "DH-등고선-계곡선";
    internal const string LayerParcel = "DH-지적도";
    internal const string LayerJibun = "DH-지번";
    internal const string GroundSurfaceName = "원지반";

    /// <summary>가져온 데이터가 올라가는 레이어(초기화·보존 판정 공용).</summary>
    internal static readonly string[] ImportLayers =
        { LayerContour, LayerContourIndex, LayerParcel, LayerJibun };

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
            if (!AskBox(ed, "등고선", out double x0, out double y0, out double x1, out double y1)) return;
            int epsg = ResolveEpsg(db, out string csNote);
            ed.WriteMessage($"\n[등고선] 좌표계: {csNote}");
            if (!Reachable(ed)) return;

            ed.WriteMessage("\n[등고선] 사내 DB에서 받는 중…");
            List<GisDb.ContourLine> lines;
            bool cut; string diag;
            try
            {
                lines = GisDb.LoadContours(x0, y0, x1, y1, epsg, SimplifyM, MaxContourRows, out cut, out diag);
            }
            catch (System.Exception dex) { Refuse(ed, "등고선", "등고선을 받지 못했습니다.\n" + dex.Message); return; }

            ed.WriteMessage("\n[등고선] " + diag);
            if (lines.Count == 0)
            {
                Refuse(ed, "등고선", "이 범위에는 등고선 자료가 없습니다.\n" +
                                     "좌표계(원점)가 맞는지, 범위가 국내인지 확인하세요.");
                return;
            }

            // ── 3D 등고선 작도(주곡선/계곡선 분리) ──
            var ids = new ObjectIdCollection();
            using (var tr = db.TransactionManager.StartTransaction())
            {
                EraseOnLayers(db, tr, new[] { LayerContour, LayerContourIndex });   // 다시 불러오면 교체
                ObjectId layMain = EnsureLayer(db, tr, LayerContour, 8);            // 주곡선 회색
                ObjectId layIdx = EnsureLayer(db, tr, LayerContourIndex, 30);       // 계곡선 주황
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
        }
        catch (System.Exception ex)
        {
            ed.WriteMessage("\n[등고선 오류] " + ex.Message);
            AcadApp.ShowAlertDialog("등고선 가져오기 중 오류:\n" + ex.Message);
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
            if (!AskBox(ed, "지적도", out double x0, out double y0, out double x1, out double y1)) return;
            int epsg = ResolveEpsg(db, out string csNote);
            ed.WriteMessage($"\n[지적도] 좌표계: {csNote}");
            if (!Reachable(ed)) return;

            ed.WriteMessage("\n[지적도] 사내 DB에서 받는 중…");
            List<GisDb.Parcel> parcels;
            bool cut; string diag;
            try
            {
                parcels = GisDb.LoadParcels(x0, y0, x1, y1, epsg, MaxParcelRows, out cut, out diag);
            }
            catch (System.Exception dex) { Refuse(ed, "지적도", "지적도를 받지 못했습니다.\n" + dex.Message); return; }

            ed.WriteMessage("\n[지적도] " + diag);
            if (parcels.Count == 0)
            {
                Refuse(ed, "지적도", "이 범위에는 지적 자료가 없습니다.\n" +
                                     "좌표계(원점)가 맞는지, 범위가 국내인지 확인하세요.");
                return;
            }

            const double txtH = 1.0;   // 지번 글자 크기 — 1.0 고정(JACK 0731)
            int nLine = 0, nText = 0;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                EraseOnLayers(db, tr, new[] { LayerParcel, LayerJibun });   // 다시 불러오면 교체
                ObjectId layP = EnsureLayer(db, tr, LayerParcel, 2);        // 필지 노란색(JACK 0731)
                ObjectId layT = EnsureLayer(db, tr, LayerJibun, 7);         // 지번 흰색(별도 레이어 — JACK)
                // [JACK 0731] 지번이 '?'로 깨지는 문제 — 기본 글꼴(txt.shx)이 한글을 못 그린다('산12-1' 등).
                //   한글 트루타입 글꼴 스타일을 만들어 지번 문자에 지정한다.
                ObjectId styleId = EnsureKoreanTextStyle(db, tr);
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
                foreach (var p in parcels)
                {
                    foreach (var ring in p.Rings)
                    {
                        try
                        {
                            var pl = new Polyline3d(Poly3dType.SimplePoly, ring, true);   // 닫힌 링
                            pl.LayerId = layP;
                            ms.AppendEntity(pl); tr.AddNewlyCreatedDBObject(pl, true);
                            nLine++;
                        }
                        catch { }
                    }
                    // 지번 문자 — 지목 꼬리(한글)를 떼고 지번만(예 '645-1전' → '645-1')
                    string jibun = StripJimok(p.Jibun);
                    if (jibun.Length == 0) continue;
                    try
                    {
                        var t = new DBText
                        {
                            TextString = jibun,
                            Position = p.Label,
                            Height = txtH,
                            HorizontalMode = TextHorizontalMode.TextCenter,
                            VerticalMode = TextVerticalMode.TextVerticalMid,
                            AlignmentPoint = p.Label,
                            LayerId = layT,
                        };
                        if (!styleId.IsNull) t.TextStyleId = styleId;   // 한글 글꼴
                        ms.AppendEntity(t); tr.AddNewlyCreatedDBObject(t, true);
                        nText++;
                    }
                    catch { }
                }
                tr.Commit();
            }
            DrawOrderFix.Apply(db);   // 배경지도 위로 지번이 보이게(JACK 0731)
            ed.Regen();

            string done = $"필지 {parcels.Count}개(선 {nLine}·지번 {nText})" +
                          (cut ? $" · ⚠상한 {MaxParcelRows} 도달(범위 축소 권장)" : "");
            ed.WriteMessage($"\n[지적도] {done}");
            AcadApp.ShowAlertDialog("지적도 가져오기 완료\n\n" + done +
                "\n\n※ GIS_Design_Loader server 제공" +
                (cut ? "\n⚠ 자료가 많아 일부만 가져왔습니다 — 범위를 좁혀 다시 받으세요." : ""));
            try { DiagLog.Append($"\n■ DHPARCEL — {done} · {csNote}\n"); } catch { }
        }
        catch (System.Exception ex)
        {
            ed.WriteMessage("\n[지적도 오류] " + ex.Message);
            AcadApp.ShowAlertDialog("지적도 가져오기 중 오류:\n" + ex.Message);
        }
    }

    // ── 공통 ──────────────────────────────────────────────────────────────────

    /// <summary>등고선 폴리선들로 "원지반" TIN 지표면 생성(있으면 교체). 반환=안내문.</summary>
    private static string BuildGroundSurface(Database db, Editor ed, ObjectIdCollection contourIds)
    {
        if (contourIds.Count == 0) return "지표면 생략(등고선 없음)";
        try
        {
            using (var tr0 = db.TransactionManager.StartTransaction())
            {
                GradingBuilder.EraseSurfacesByBaseName(tr0, GroundSurfaceName);   // 다시 불러오면 교체
                tr0.Commit();
            }
            ObjectId surfId = TinSurface.Create(db, GroundSurfaceName);
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
            // [JACK 0731] 처음 만들어질 때 스타일은 삼각망이 아니라 **등고선**(주 25m·보조 5m)으로.
            try
            {
                ObjectId stId = EnsureContourStyle(tr);
                if (!stId.IsNull) surf.StyleId = stId;
            }
            catch (System.Exception sex) { ed.WriteMessage("\n[등고선] 지표면 스타일 적용 생략 — " + sex.Message); }
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
            return $"'{GroundSurfaceName}' 지표면 생성(점 {pts}·삼각형 {tris})";
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
    private const double MinorInterval = 5.0;
    private const double MajorInterval = 25.0;
    internal const string GroundStyleName = "DH-원지반 등고선(5·25)";

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
        if (id.IsNull) id = styles.Add(GroundStyleName);
        if (id.IsNull) return ObjectId.Null;

        var st = (Autodesk.Civil.DatabaseServices.Styles.SurfaceStyle)tr.GetObject(id, OpenMode.ForWrite);
        try { st.ContourStyle.MinorContourInterval = MinorInterval; } catch { }
        try { st.ContourStyle.MajorContourInterval = MajorInterval; } catch { }

        // 표시 항목 정리 — 등고선·경계만 켜고 나머지(삼각망·점·경사 등)는 전부 끈다.
        //   열거값 이름을 일일이 적지 않고 전체를 돌며 처리(버전별 항목 차이에 안전).
        foreach (Autodesk.Civil.DatabaseServices.Styles.SurfaceDisplayStyleType t in
                 System.Enum.GetValues(typeof(Autodesk.Civil.DatabaseServices.Styles.SurfaceDisplayStyleType)))
        {
            bool on = t == Autodesk.Civil.DatabaseServices.Styles.SurfaceDisplayStyleType.MajorContour
                   || t == Autodesk.Civil.DatabaseServices.Styles.SurfaceDisplayStyleType.MinorContour
                   || t == Autodesk.Civil.DatabaseServices.Styles.SurfaceDisplayStyleType.Boundary;
            try { st.GetDisplayStylePlan(t).Visible = on; } catch { }
            try { st.GetDisplayStyleModel(t).Visible = on; } catch { }
        }
        return id;
    }

    /// <summary>범위 두 점 클릭(현재 UCS → WCS 변환). 너무 작으면 거부.</summary>
    private static bool AskBox(Editor ed, string label, out double x0, out double y0, out double x1, out double y1)
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
        if (x1 - x0 < 1.0 || y1 - y0 < 1.0)
        {
            Refuse(ed, label, "지정한 범위가 너무 작습니다(1m 미만).");
            return false;
        }
        return true;
    }

    /// <summary>도면 좌표계 우선, 없으면 정지옵션 값(배경지도와 동일 규칙).</summary>
    private static int ResolveEpsg(Database db, out string note)
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
        AcadApp.ShowAlertDialog(
            "사내 지형·지적 데이터베이스에 접속할 수 없습니다.\n\n" +
            "사내망(VPN)에 연결되어 있는지 확인해 주세요.\n\n상세: " + why);
        return false;
    }

    /// <summary>'645-1전' → '645-1' (지번 뒤 한글 지목 제거 — DB에 지목 컬럼이 따로 없음).</summary>
    internal static string StripJimok(string jibun)
    {
        if (string.IsNullOrWhiteSpace(jibun)) return "";
        string s = jibun.Trim();
        int end = s.Length;
        while (end > 0)
        {
            char c = s[end - 1];
            if (c >= '0' && c <= '9') break;      // 숫자로 끝나면 거기까지가 지번
            end--;
        }
        string cut = end > 0 ? s.Substring(0, end) : s;
        return cut.Length > 0 ? cut : s;
    }

    private static void EraseOnLayers(Database db, Transaction tr, string[] layers)
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
        foreach (var id in victims)
        {
            try { (tr.GetObject(id, OpenMode.ForWrite) as AcadEntity)?.Erase(); } catch { }
        }
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

    private static ObjectId EnsureLayer(Database db, Transaction tr, string name, short aci)
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
