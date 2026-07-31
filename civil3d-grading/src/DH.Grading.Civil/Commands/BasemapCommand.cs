using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using DH.Grading.Core;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace DH.Grading.Civil.Commands;

/// <summary>
/// [배경지도 — JACK 0731] "배경지도"(DHMAP) / "지도끄기"(DHMAPOFF).
///  · DHMAP    : 두 점으로 범위를 찍으면 그 범위의 브이월드 위성사진을 받아 **도면 좌표계 그대로** 깔아준다.
///               같은 범위·화질·좌표계를 다시 요청하면 받아둔 파일을 재사용(재다운로드 없음).
///  · DHMAPOFF : 이 기능으로 깐 위성사진을 **한 번에 전부** 제거(레이어 DH-배경지도 기준). 지우는 동안
///               래스터 반응자를 꺼 Raster Design의 "이미지를 분리할까요?" 질문창이 뜨지 않게 한다.
///  · <see cref="RefreshAll"/> : 정지옵션에서 좌표계를 바꿔 저장하면 기존 배경지도를 새 좌표계로 자동 재생성.
///               **선(先)다운로드 후(後)교체** — 새 이미지를 모두 확보한 뒤에만 기존 것을 지운다(리뷰 M-A).
/// 배치 기준은 항상 만들어진 GeoTIFF의 태그(해상도·좌상단 좌표)를 직접 읽어 쓴다(역산 금지 — 오차 누적 방지).
/// </summary>
public sealed class BasemapCommand
{
    private const string Layer = "DH-배경지도";
    private const string DefPrefix = "DH배경지도";

    /// <summary>도면상 배치 범위(축정렬).</summary>
    private readonly record struct Extent(double MinE, double MinN, double MaxE, double MaxN);

    /// <summary>확보된 위성 이미지 1장 — 파일 경로 + 파일에서 읽은 배치 정보.</summary>
    private readonly record struct Img(string Tif, int PxW, int PxH, double Res, double TieX, double TieY);

    [CommandMethod("DHMAP")]
    public void Run()
    {
        Document doc = AcadApp.DocumentManager.MdiActiveDocument;
        if (doc == null) return;
        Editor ed = doc.Editor;
        Database db = doc.Database;

        try
        {
            var (epsg, lon0, fn, csNote, csOk) = ResolveCs(db);
            if (!csOk)
            {
                Refuse(ed, "좌표계를 알 수 없습니다.\n정지 옵션에서 좌표계(원점)를 먼저 지정하세요.");
                return;
            }

            // ── 범위: 두 점 클릭 ──
            var p1 = ed.GetPoint("\n[배경지도] 범위 첫 번째 모서리 클릭 (Esc=취소): ");
            if (p1.Status != PromptStatus.OK) return;
            var pco = new PromptCornerOptions("\n반대쪽 모서리 클릭: ", p1.Value);
            var p2 = ed.GetCorner(pco);
            if (p2.Status != PromptStatus.OK) return;

            // [리뷰 M-2] 클릭 점은 현재 UCS 좌표 — 이미지 배치(Orientation)는 WCS 해석이므로 변환 필수.
            var ucs = ed.CurrentUserCoordinateSystem;
            var w1 = p1.Value.TransformBy(ucs);
            var w2 = p2.Value.TransformBy(ucs);
            var ex = new Extent(System.Math.Min(w1.X, w2.X), System.Math.Min(w1.Y, w2.Y),
                                System.Math.Max(w1.X, w2.X), System.Math.Max(w1.Y, w2.Y));
            if (ex.MaxE - ex.MinE < 1.0 || ex.MaxN - ex.MinN < 1.0)
            {
                Refuse(ed, "지정한 범위가 너무 작습니다(1m 미만).\n범위를 다시 지정하세요.");
                return;
            }

            // [JACK 질문 0731] 어떤 좌표계로 앉히는지 항상 알린다(재사용 때도).
            ed.WriteMessage($"\n[배경지도] 좌표계: {csNote}");

            if (!EnsureFile(ex, epsg, lon0, fn, GradingSettings.BasemapRes, true, out var img, out string note))
            { Refuse(ed, "배경지도를 만들지 못했습니다.\n" + note); return; }
            if (!Attach(db, img, out string aNote))
            { Refuse(ed, "배경지도를 도면에 붙이지 못했습니다.\n" + aNote); return; }

            ed.Regen();
            ed.WriteMessage($"\n[배경지도] {note} · {aNote}\n  · 끄려면 [지도끄기] 버튼 · 여러 번 눌러 여러 곳에 깔 수 있습니다.");
            try { DiagLog.Append($"\n■ DHMAP(배경지도)\n  {note} · {aNote} · {csNote}\n"); } catch { }
        }
        catch (System.Exception exn)
        {
            ed.WriteMessage("\n[배경지도 오류] " + exn.Message);
            AcadApp.ShowAlertDialog("배경지도 처리 중 오류:\n" + exn.Message);
        }
    }

    /// <summary>이 기능으로 깐 위성사진을 한 번에 전부 제거(레이어 DH-배경지도 기준).</summary>
    [CommandMethod("DHMAPOFF")]
    public void RunOff()
    {
        Document doc = AcadApp.DocumentManager.MdiActiveDocument;
        if (doc == null) return;
        Editor ed = doc.Editor;

        try
        {
            int erased = EraseAll(doc.Database);
            ed.Regen();
            string msg = erased > 0 ? $"배경지도 {erased}개를 모두 제거했습니다." : "제거할 배경지도가 없습니다.";
            ed.WriteMessage("\n[지도끄기] " + msg);
            try { DiagLog.Append($"\n■ DHMAPOFF(지도끄기) — {msg}\n"); } catch { }
        }
        catch (System.Exception ex)
        {
            ed.WriteMessage("\n[지도끄기 오류] " + ex.Message);
            AcadApp.ShowAlertDialog("배경지도 제거 중 오류:\n" + ex.Message);
        }
    }

    /// <summary>[JACK 0731 — 좌표계 연동] 이미 깔린 배경지도를 새 좌표계로 다시 받아 재배치.
    /// [리뷰 M-A] **선다운로드 후교체** — 새 이미지를 전부 확보한 뒤에만 기존 것을 지운다.
    /// 하나라도 실패하면 기존 배경지도를 그대로 두고 중단(팝업 안내). 반환=재배치한 개수.</summary>
    public static int RefreshAll(Document doc)
    {
        if (doc == null) return 0;
        Editor ed = doc.Editor;
        Database db = doc.Database;

        var (epsg, lon0, fn, csNote, csOk) = ResolveCs(db);
        if (!csOk) return 0;

        var targets = CollectTargets(db);
        if (targets.Count == 0) return 0;

        ed.WriteMessage($"\n[배경지도] 좌표계 변경 — 기존 배경지도 {targets.Count}개를 새 좌표계로 다시 받는 중… ({csNote})");

        // 1단계: 새 이미지 확보(도면은 아직 안 건드림).
        //   [JACK 0731] 새 좌표계로 받을 자료가 없으면 그 배경지도는 **그냥 삭제**한다 —
        //   옛 좌표계 기준이라 위치가 틀린 지도를 남겨두면 오히려 오해를 부른다.
        var pairs = new System.Collections.Generic.List<(ObjectId Id, Img Img)>();
        var dead = new System.Collections.Generic.List<ObjectId>();
        string lastErr = "";
        foreach (var (id, ex) in targets)
        {
            if (EnsureFile(ex, epsg, lon0, fn, GradingSettings.BasemapRes, true, out var img, out string n))
            { pairs.Add((id, img)); ed.WriteMessage("\n  · " + n); }
            else { dead.Add(id); lastErr = n; ed.WriteMessage("\n  · 자료 없음 → 이 배경지도는 삭제: " + n); }
        }
        if (dead.Count > 0)
        {
            EraseImages(db, dead);
            AcadApp.ShowAlertDialog(
                $"새 좌표계로 받을 위성자료가 없어 배경지도 {dead.Count}개를 삭제했습니다.\n" + lastErr +
                "\n\n필요하면 [배경지도] 버튼으로 다시 지정하세요.");
        }

        // 2단계: [JACK 0731] **지우지 않고 제자리 교체** — 기존 이미지의 정의가 가리키는 파일과 배치만 바꾼다.
        //   지웠다 다시 붙이면 Raster Design이 "이미지를 분리할까요?" 창을 띄우므로, 삭제 자체를 피한다.
        int ok = 0;
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            foreach (var (id, img) in pairs)
            {
                try
                {
                    if (id.IsErased || tr.GetObject(id, OpenMode.ForWrite) is not RasterImage ri) continue;
                    if (tr.GetObject(ri.ImageDefId, OpenMode.ForWrite) is not RasterImageDef rd) continue;
                    rd.SourceFileName = img.Tif;
                    rd.Load();                                     // 새 파일로 다시 읽기
                    double wM = img.PxW * img.Res, hM = img.PxH * img.Res;
                    ri.Orientation = new CoordinateSystem3d(
                        new Point3d(img.TieX, img.TieY - hM, 0), new Vector3d(wM, 0, 0), new Vector3d(0, hM, 0));
                    ok++;
                    ed.WriteMessage($"\n  · 교체: {wM:F0}m × {hM:F0}m · {img.Res:0.##}m/px");
                }
                catch (System.Exception ex) { ed.WriteMessage("\n  · 교체 실패: " + ex.Message); }
            }
            tr.Commit();
        }
        catch (System.Exception ex) { ed.WriteMessage("\n  · 갱신 오류: " + ex.Message); }

        try { ed.Regen(); } catch { }
        if (ok < pairs.Count)
            AcadApp.ShowAlertDialog($"배경지도 {pairs.Count}개 중 {ok}개만 갱신했습니다.\n" +
                                    "갱신 안 된 것은 [지도끄기] 후 [배경지도]로 다시 지정해 주세요.");
        try { DiagLog.Append($"\n■ 배경지도 좌표계 갱신 — {ok}/{targets.Count}개 제자리 교체 · {csNote}\n"); } catch { }
        return ok;
    }

    // ── 내부 공통 ─────────────────────────────────────────────────────────────

    /// <summary>좌표계 결정 — 기본은 도면 좌표계(MAPCSASSIGN) 우선.
    /// [리뷰 M-B] 단, 정지옵션에서 고른 좌표계가 도면 좌표계 코드로 표현 불가능한 경우(구 좌표계·UTM-K)에는
    /// 사용자의 명시적 선택인 정지옵션 값을 우선한다 — 그러지 않으면 그 사용자는 자기 좌표계를 영원히 못 쓴다.</summary>
    private static (int epsg, double lon0, double fn, string note, bool ok) ResolveCs(Database db)
    {
        int optEpsg = GradingSettings.ExportEpsg;
        int epsg = optEpsg;
        string note;
        string csCode = KoreaCs.Read(db);
        int? det = KoreaCs.ResolveEpsgFromCode(csCode);
        if (KoreaCs.CodeForEpsg(optEpsg) == null)
            note = $"정지옵션 좌표계 EPSG:{epsg}(도면 좌표계로 표현 불가한 원점이라 옵션 값 사용)";
        else if (det.HasValue) { epsg = det.Value; note = $"도면 좌표계 '{csCode}' → EPSG:{epsg}"; }
        else note = $"도면 좌표계 미지정/미인식 → 정지옵션 값 EPSG:{epsg}";
        var belt = ShapefileWriter.Belt(epsg);
        if (belt == null) return (epsg, 127, 600000, note, false);
        return (epsg, belt.Value.cm, belt.Value.fn, note, true);
    }

    /// <summary>위성 이미지 파일 확보(캐시 재사용 또는 다운로드) + 파일에서 배치 정보 읽기. 도면은 안 건드림.</summary>
    private static bool EnsureFile(Extent ex, int epsg, double lon0, double fn, double target,
                                   bool showProgress, out Img img, out string note)
    {
        img = default;
        string dir = System.IO.Path.Combine(
            System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
            "DHGrading", "basemap");
        string name = $"위성_{epsg}_{(long)ex.MinE}_{(long)ex.MinN}_{(long)ex.MaxE}_{(long)ex.MaxN}_{target:0.##}";
        string tif = System.IO.Path.Combine(dir, name + ".tif");

        // [리뷰 C-1] 배치 정보는 **항상 만들어진 TIFF에서 직접 읽는다**(해상도 역산 금지).
        bool have = System.IO.File.Exists(tif) &&
                    TryReadTiffGeo(tif, out int cw, out int ch, out double cr, out double ctx, out double cty);
        if (have)
        {
            TryReadTiffGeo(tif, out cw, out ch, out cr, out ctx, out cty);
            img = new Img(tif, cw, ch, cr, ctx, cty);
            note = "받아둔 위성 이미지 재사용(다운로드 없음)";
            return true;
        }

        ProgressMeter? pm = null;
        (bool ok, string msg, double r, int w, int h) g;
        try
        {
            g = VWorldImagery.ExportBasemap(ex.MinE, ex.MinN, ex.MaxE, ex.MaxN, tif, target, lon0, fn, epsg,
                !showProgress ? null : (done, total) =>
                {
                    if (pm == null)
                    {
                        pm = new ProgressMeter();
                        pm.Start($"위성 타일 {total}장 받는 중");
                        pm.SetLimit(System.Math.Max(1, total));
                    }
                    try { pm.MeterProgress(); } catch { }
                });
        }
        finally { try { pm?.Stop(); pm?.Dispose(); } catch { } }
        if (!g.ok) { note = g.msg; return false; }

        if (!TryReadTiffGeo(tif, out int pxW, out int pxH, out double res, out double tieX, out double tieY))
        { note = "만들어진 위성 이미지를 읽지 못했습니다."; return false; }
        img = new Img(tif, pxW, pxH, res, tieX, tieY);
        note = g.msg;
        return true;
    }

    /// <summary>확보된 이미지를 도면에 부착(좌하단=좌상단−높이, 폭·높이=픽셀수×해상도, 맨 아래 배치).</summary>
    private static bool Attach(Database db, Img img, out string note)
    {
        double wM = img.PxW * img.Res, hM = img.PxH * img.Res;
        var origin = new Point3d(img.TieX, img.TieY - hM, 0);
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            ObjectId dictId = RasterImageDef.GetImageDictionary(db);
            if (dictId.IsNull) dictId = RasterImageDef.CreateImageDictionary(db);
            var dict = (DBDictionary)tr.GetObject(dictId, OpenMode.ForWrite);

            var def = new RasterImageDef { SourceFileName = img.Tif };
            try { def.Load(); }
            catch (System.Exception lex)
            {
                try { def.Dispose(); } catch { }
                note = "위성 이미지를 불러오지 못했습니다: " + lex.Message;
                return false;
            }
            ObjectId defId = dict.SetAt(UniqueDefName(dict, DefPrefix), def);
            tr.AddNewlyCreatedDBObject(def, true);

            ObjectId layId = EnsureLayer(db, tr, Layer, 8);
            var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
            var ri = new RasterImage();
            ri.SetDatabaseDefaults();
            ri.ImageDefId = defId;
            ri.Orientation = new CoordinateSystem3d(origin, new Vector3d(wM, 0, 0), new Vector3d(0, hM, 0));
            ri.ShowImage = true;
            ri.LayerId = layId;
            ms.AppendEntity(ri);
            tr.AddNewlyCreatedDBObject(ri, true);
            // [리뷰 M-3] 표준 순서: Append → AddNewlyCreated → EnableReactors(정적 전역) → Associate.
            RasterImage.EnableReactors(true);
            ri.AssociateRasterDef(def);

            try   // 항상 맨 아래로 — 도면 선/문자를 가리지 않게.
            {
                var dot = (DrawOrderTable)tr.GetObject(ms.DrawOrderTableId, OpenMode.ForWrite);
                dot.MoveToBottom(new ObjectIdCollection { ri.ObjectId });
            }
            catch { }

            try   // 레이어가 꺼져 있었으면 다시 켠다.
            {
                var ltr = (LayerTableRecord)tr.GetObject(layId, OpenMode.ForWrite);
                if (ltr.IsOff) ltr.IsOff = false;
            }
            catch { }

            tr.Commit();
        }
        catch (System.Exception ex) { note = ex.Message; return false; }

        note = $"{wM:F0}m × {hM:F0}m · {img.Res:0.##}m/px";
        return true;
    }

    /// <summary>우리 배경지도 이미지의 (엔티티ID, 도면 범위) 수집 — 제자리 교체(RefreshAll)용.
    /// [리뷰 사소3] 회전·미러된 이미지는 축정렬 전제가 깨지므로 건너뛴다.</summary>
    private static System.Collections.Generic.List<(ObjectId Id, Extent Ex)> CollectTargets(Database db)
    {
        var list = new System.Collections.Generic.List<(ObjectId, Extent)>();
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
            foreach (ObjectId id in ms)
            {
                try
                {
                    if (tr.GetObject(id, OpenMode.ForRead) is not RasterImage ri) continue;
                    if (!string.Equals(ri.Layer, Layer, System.StringComparison.OrdinalIgnoreCase)) continue;
                    var o = ri.Orientation;
                    if (System.Math.Abs(o.Xaxis.Y) > 1e-6 || System.Math.Abs(o.Yaxis.X) > 1e-6) continue; // 회전/미러
                    double w = o.Xaxis.Length, h = o.Yaxis.Length;
                    if (w > 0 && h > 0)
                        list.Add((id, new Extent(o.Origin.X, o.Origin.Y, o.Origin.X + w, o.Origin.Y + h)));
                }
                catch { }
            }
            tr.Commit();
        }
        catch { }
        return list;
    }

    /// <summary>[JACK 0731] 도면 **전체**(모델·배치·블록정의)를 훑어 이미지 정의별 참조 엔티티 목록을 만든다.
    /// GetEntityCount는 반응자 상태에 따라 부정확할 수 있어(exact=false) 판정에 못 쓴다 —
    /// 정의를 안전하게 먼저 지우려면(=분리 질문창 방지) 참조를 직접 세는 편이 확실하다.</summary>
    private static System.Collections.Generic.Dictionary<ObjectId, System.Collections.Generic.List<ObjectId>>
        MapDefRefs(Transaction tr, Database db)
    {
        var map = new System.Collections.Generic.Dictionary<ObjectId, System.Collections.Generic.List<ObjectId>>();
        try
        {
            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            foreach (ObjectId btrId in bt)
            {
                BlockTableRecord btr;
                try { btr = (BlockTableRecord)tr.GetObject(btrId, OpenMode.ForRead); }
                catch { continue; }
                foreach (ObjectId id in btr)
                {
                    try
                    {
                        if (tr.GetObject(id, OpenMode.ForRead) is not RasterImage ri) continue;
                        var d = ri.ImageDefId;
                        if (d.IsNull) continue;
                        if (!map.TryGetValue(d, out var lst)) map[d] = lst = new System.Collections.Generic.List<ObjectId>();
                        lst.Add(id);
                    }
                    catch { }
                }
            }
        }
        catch { }
        return map;
    }

    /// <summary>우리 배경지도 이미지를 전부 지운다(+정의 정리). 반환=지운 개수.</summary>
    private static int EraseAll(Database db)
    {
        var all = new System.Collections.Generic.List<ObjectId>();
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
            foreach (ObjectId id in ms)
            {
                try
                {
                    if (tr.GetObject(id, OpenMode.ForRead) is RasterImage ri &&
                        string.Equals(ri.Layer, Layer, System.StringComparison.OrdinalIgnoreCase))
                        all.Add(id);
                }
                catch { }
            }
            tr.Commit();
        }
        catch { }
        return EraseImages(db, all);
    }

    /// <summary>지정한 배경지도 이미지들을 지운다. 정의(RasterImageDef)를 **먼저** 지워 Raster Design의
    /// "이미지를 분리할까요?" 창이 뜨지 않게 한다. 반환=지운 개수.</summary>
    private static int EraseImages(Database db, System.Collections.Generic.ICollection<ObjectId> targets)
    {
        int erased = 0;
        if (targets == null || targets.Count == 0) return 0;
        // [JACK 0731] 삭제~커밋 전 구간에서 래스터 반응자를 전역으로 꺼둔다 — 켜져 있으면 마지막 참조가
        //   사라질 때 Raster Design이 "이미지를 분리할까요?" 질문창을 띄운다.
        //   [리뷰 M-C] 통지가 커밋 시점에 날 수도 있으므로 tr.Commit()까지 가드 안에 둔다. 끝나면 반드시 원복.
        try { RasterImage.EnableReactors(false); } catch { }
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            var victims = new System.Collections.Generic.HashSet<ObjectId>(targets);

            // [JACK 0731] **정의를 이미지보다 먼저 지운다** — 마지막 이미지가 지워질 때 Raster Design이
            //   "이미지를 분리할까요?" 창을 띄우는데, 그 통지는 정의(RasterImageDef)에 달린 반응자가 낸다.
            //   정의를 먼저 없애면 물어볼 대상 자체가 사라져 창이 뜨지 않는다(EnableReactors만으로는 못 막음).
            //   판정은 GetEntityCount(부정확 가능) 대신 **도면 전체 참조 스캔**으로 확실히 한다:
            //   그 정의를 쓰는 이미지가 전부 이번에 지울 것일 때만 먼저 지운다(다른 곳 복사본은 보호).
            try
            {
                var refs = MapDefRefs(tr, db);
                ObjectId dictId = RasterImageDef.GetImageDictionary(db);
                if (!dictId.IsNull)
                {
                    var dict = (DBDictionary)tr.GetObject(dictId, OpenMode.ForWrite);
                    // [리뷰 사소2] 열거 중 Erase 금지 — 먼저 모아두고 그다음 지운다.
                    var defIds = new System.Collections.Generic.List<ObjectId>();
                    foreach (DBDictionaryEntry e in dict)
                        if (e.Key.StartsWith(DefPrefix, System.StringComparison.Ordinal)) defIds.Add(e.Value);
                    foreach (var did in defIds)
                    {
                        try
                        {
                            bool allMine = true;
                            if (refs.TryGetValue(did, out var users))
                                foreach (var u in users) if (!victims.Contains(u)) { allMine = false; break; }
                            if (!allMine) continue;   // 다른 데서도 쓰는 정의 → 건드리지 않음
                            if (tr.GetObject(did, OpenMode.ForWrite) is RasterImageDef rd) rd.Erase();
                        }
                        catch { }
                    }
                }
            }
            catch { }

            foreach (var id in victims)
            {
                try { (tr.GetObject(id, OpenMode.ForWrite) as Entity)?.Erase(); erased++; } catch { }
            }

            tr.Commit();
        }
        finally { try { RasterImage.EnableReactors(true); } catch { } }
        return erased;
    }

    /// <summary>[리뷰 C-1] GeoTIFF에서 배치에 필요한 값을 직접 읽는다 — 가로·세로 픽셀 + 해상도(ModelPixelScale
    /// 태그 33550) + 좌상단 모델좌표(ModelTiepoint 태그 33922). 픽셀수는 생성 시 올림이라 역산하면 오차가
    /// 누적되므로 반드시 파일 값을 쓴다. 우리가 쓴 무압축 베이스라인 TIFF(리틀엔디안) 대상.</summary>
    private static bool TryReadTiffGeo(string path, out int w, out int h,
                                       out double res, out double tieX, out double tieY)
    {
        w = h = 0; res = 0; tieX = tieY = 0;
        try
        {
            using var fs = System.IO.File.OpenRead(path);
            using var br = new System.IO.BinaryReader(fs);
            if (br.ReadByte() != 'I' || br.ReadByte() != 'I') return false;   // 리틀엔디안만
            if (br.ReadUInt16() != 42) return false;
            uint ifd = br.ReadUInt32();
            fs.Seek(ifd, System.IO.SeekOrigin.Begin);
            int n = br.ReadUInt16();
            uint scaleOff = 0, tieOff = 0;
            for (int i = 0; i < n; i++)
            {
                ushort tag = br.ReadUInt16();
                ushort type = br.ReadUInt16();
                uint count = br.ReadUInt32();
                uint val = br.ReadUInt32();
                // SHORT(3)는 값이 4바이트 필드의 앞 2바이트에, LONG(4)은 4바이트 전체에 들어간다.
                if (tag == 256) w = (int)(type == 3 ? (val & 0xFFFF) : val);
                else if (tag == 257) h = (int)(type == 3 ? (val & 0xFFFF) : val);
                else if (tag == 33550 && type == 12 && count >= 1) scaleOff = val;   // ModelPixelScale(DOUBLE×3)
                else if (tag == 33922 && type == 12 && count >= 6) tieOff = val;     // ModelTiepoint(DOUBLE×6)
            }
            if (w <= 1 || h <= 1 || scaleOff == 0 || tieOff == 0) return false;
            fs.Seek(scaleOff, System.IO.SeekOrigin.Begin);
            res = br.ReadDouble();                                   // X 해상도(m/px)
            fs.Seek(tieOff, System.IO.SeekOrigin.Begin);
            br.ReadDouble(); br.ReadDouble(); br.ReadDouble();       // 래스터 기준점(0,0,0)
            tieX = br.ReadDouble(); tieY = br.ReadDouble();           // 대응 모델좌표(=좌상단)
            return res > 0 && !double.IsNaN(tieX) && !double.IsNaN(tieY);
        }
        catch { return false; }
    }

    private static string UniqueDefName(DBDictionary dict, string prefix)
    {
        for (int i = 1; ; i++)
        {
            string n = $"{prefix}_{i}";
            if (!dict.Contains(n)) return n;
        }
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

    private static void Refuse(Editor ed, string msg)
    {
        ed.WriteMessage("\n[배경지도] " + msg.Replace("\n", " "));
        AcadApp.ShowAlertDialog(msg);
    }
}
