using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Runtime;
using DH.Grading.Core;
using DH.Grading.Core.Forest;
using System.Windows;
using System.Windows.Controls;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace DH.Grading.Civil.Commands;

/// <summary>★[수량산출서 · JACK 1116] <b>[수량산출서](DHQTYXLSX)</b> — 토공 수량산출서(엑셀) 한 벌: 토공집계표 · 1)시설 토적표 · 수로관산근 · 임목폐기물 둘.
/// 값 = 마지막 [횡단도](부지 · 진입도로) 수량 · U형 측구 목록 · 벌개제근 장부. 엑셀 쓰기는 Core <see cref="QtyReportXlsx"/>(ClosedXML) 한 곳 —
/// 라이브러리를 못 불러오면 짧은 말만 내고 명령은 끝난다(Civil이 죽지 않게 따로 부름 · 실제 Civil에서 확인 필요).</summary>
public sealed class QtyXlsxCommand
{
    /// <summary>도킹창에서 [만들기]를 누르면 값을 여기에 두고 DHQTYXLSXMAKE를 보낸다(문서 안에서 도는 명령).</summary>
    internal static QtyXlsxInput? Pending;
    /// <summary>★[JACK 1124] 지난번 고른 저장 폴더(이 세션) — 다음 저장 창의 처음 폴더.</summary>
    static string _lastDir = "";

    /// <summary>창 상단 상태 글 — scanDwg=false면 도면 검토선은 안 읽음(문서 바뀜 때 · 명령 밖).</summary>
    internal static string StatusText(Document doc, bool scanDwg)
    {
        var db = doc.Database;
        var siteSnap = QtyKeeper.Of(db, false); var roadSnap = QtyKeeper.Of(db, true);
        bool fromDwg = false, roadFromDwg = false;
        if (siteSnap == null && scanDwg) { siteSnap = XsecViewCommand.ReadSiteQtyFromDrawing(doc, out _); fromDwg = siteSnap != null; }   // 이 세션에 안 그렸어도 도면의 검토선에서 읽음
        if (roadSnap == null && scanDwg) { roadSnap = XsecViewCommand.ReadRoadQtyFromDrawing(doc, out _); roadFromDwg = roadSnap != null; }
        return ((siteSnap == null ? (scanDwg ? "부지 횡단 없음 → [횡단도] 먼저\n" : "부지 횡단: 수량산출서 단추로 확인\n") : $"부지 횡단 {siteSnap.Stations.Count}개{(fromDwg ? " (도면에서 읽음)" : "")} · 터파기 구분 H={siteSnap.Limit:0.0#}m\n")
              + (roadSnap == null ? "진입도로 횡단 없음 → 칸 빈칸\n" : $"진입도로 횡단 {roadSnap.Stations.Count}개{(roadFromDwg ? " (도면에서 읽음)" : "")}\n")).TrimEnd();
    }

    [CommandMethod("DHQTYXLSX", CommandFlags.Modal)]
    public void Run()
    {
        var doc = AcadApp.DocumentManager.MdiActiveDocument;
        if (doc == null) return;
        if (SelfCheckLog.Headless) { Make(doc, QtyXlsxInput.Read(doc.Database)); return; }   // 화면 없는 Civil은 창을 못 띄움 — 도면 기록 값으로 바로 만듦
        QtyXlsxPalette.Show();
    }

    /// <summary>도킹창 [만들기] → 여기서 문서 안에서 도면에 값 적고 엑셀 만듦.</summary>
    [CommandMethod("DHQTYXLSXMAKE", CommandFlags.Modal)]
    public void RunMake()
    {
        var doc = AcadApp.DocumentManager.MdiActiveDocument;
        var inp = Pending; Pending = null;
        if (doc == null || inp == null) return;
        Make(doc, inp);
    }

    static void Make(Document doc, QtyXlsxInput inp)
    {
        var ed = doc.Editor; var db = doc.Database;
        string dwg = ""; try { dwg = db.Filename; } catch { }
        if (!System.IO.Path.IsPathRooted(dwg) || !System.IO.File.Exists(dwg)) { ed.WriteMessage("\n[수량산출서] 도면을 먼저 저장하세요."); return; }

        var siteSnap = QtyKeeper.Of(db, false); var roadSnap = QtyKeeper.Of(db, true);
        if (siteSnap == null) siteSnap = XsecViewCommand.ReadSiteQtyFromDrawing(doc, out _);   // 이 세션에 안 그렸어도 도면의 검토선에서 읽음
        if (roadSnap == null) roadSnap = XsecViewCommand.ReadRoadQtyFromDrawing(doc, out _);   // 진입도로도 같은 길(설계 기록의 횡단 검토선 그룹)
        XsecQuantity.DeepLimit = inp.DeepLimit;
        try { using (doc.LockDocument()) inp.Write(db); } catch (System.Exception ex) { ed.WriteMessage("\n[수량산출서] 값을 도면에 못 적음 — " + ex.Message); }

        string xlsx = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(dwg)!, System.IO.Path.GetFileNameWithoutExtension(dwg) + "_토공수량산출서.xlsx");
        // ★[JACK 1124 «[만들기] 누르면 저장 위치 설정 창이 떠야 함 — 그냥 바로 만들어짐»] 저장 위치 고르기(처음 = 도면 폴더 · 다음부터 지난 폴더) · 덮어쓰기 확인은 창이 함
        if (!SelfCheckLog.Headless)
        {
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Title = "수량산출서 저장 위치",
                Filter = "엑셀 (*.xlsx)|*.xlsx",
                FileName = System.IO.Path.GetFileName(xlsx),
                DefaultExt = ".xlsx",
                AddExtension = true,
                OverwritePrompt = true,
            };
            try { dlg.InitialDirectory = _lastDir.Length > 0 && System.IO.Directory.Exists(_lastDir) ? _lastDir : System.IO.Path.GetDirectoryName(xlsx)!; } catch { }
            bool? okD;
            try { okD = DhBrand.ShowFileDialog(dlg); } catch { okD = false; }
            if (okD != true) { ed.WriteMessage("\n[수량산출서] 취소"); return; }
            xlsx = dlg.FileName;
            try { _lastDir = System.IO.Path.GetDirectoryName(xlsx) ?? ""; } catch { }
        }

        QtyReportModel model;
        try { model = BuildModel(doc, inp, siteSnap, roadSnap, ed); }
        catch (System.Exception ex) { ed.WriteMessage("\n[수량산출서] 자료를 못 모음 — " + ex.Message); try { DiagLog.Append("\n[수량산출서] " + ex); } catch { } return; }

        string err = Save(model, xlsx);
        if (err.Length > 0) { ed.WriteMessage("\n[수량산출서] " + err); return; }
        ed.WriteMessage("\n[수량산출서] 저장: " + xlsx);
        if (!SelfCheckLog.Headless) try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(xlsx) { UseShellExecute = true }); } catch { }
        if (inp.Land)
        {
            try { MakeLand(doc, inp, dwg, System.IO.Path.GetDirectoryName(xlsx) ?? ""); }   // ★[JACK 1124] 산출서와 같은 폴더
            catch (System.Exception ex) { ed.WriteMessage("\n[용지조서] 못 만듦 — " + ex.Message); try { DiagLog.Append("\n[용지조서] " + ex); } catch { } }
        }
    }

    /// <summary>★[용지조서 · JACK 1117] 도면의 [지적도] 필지 ∩ 공사 범위(<see cref="PlanSheet.PlanArea"/> = 정지 경계 ∪ 진입도로 면 자리 — 벌개제근과 같은 범위) → «도면이름_용지조서.xlsx».</summary>
    static void MakeLand(Document doc, QtyXlsxInput inp, string dwg, string dir = "")
    {
        var ed = doc.Editor; var db = doc.Database;
        var gf = NtsSupport.Factory();
        List<LandParcel> parcels; NetTopologySuite.Geometries.Geometry? fp; string fpNote;
        using (var tr = db.TransactionManager.StartTransaction())
        {
            parcels = ReadParcels(db, tr, gf);
            fp = parcels.Count > 0 ? PlanSheet.PlanArea(db, tr, gf, out fpNote) : null;
            tr.Commit();
        }
        if (parcels.Count == 0) { ed.WriteMessage("\n[용지조서] 지적도 없음 → [지적도] 먼저"); return; }
        if (fp == null || fp.IsEmpty) { ed.WriteMessage("\n[용지조서] 공사 범위 없음 → 계획부지 · 진입도로 먼저"); return; }
        var m = LandReport.Build(parcels, fp, inp.Facility.Trim());
        if (m.Rows.Count == 0) { ed.WriteMessage($"\n[용지조서] 공사 범위 안 필지 없음(지적 {m.Parcels}필지) → 지적도 범위 확인"); return; }

        string path = System.IO.Path.Combine(dir.Length > 0 && System.IO.Directory.Exists(dir) ? dir : System.IO.Path.GetDirectoryName(dwg)!, System.IO.Path.GetFileNameWithoutExtension(dwg) + "_용지조서.xlsx");
        if (!SelfCheckLog.Headless && System.IO.File.Exists(path) && MessageBox.Show("같은 이름 파일이 있음 → 덮어쓰기?\n" + System.IO.Path.GetFileName(path), "용지조서", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        string err = Save(() => LandReportXlsx.Write(m, path));
        if (err.Length > 0) { ed.WriteMessage("\n[용지조서] " + err); return; }
        ed.WriteMessage($"\n[용지조서] 저장: {path} · {m.Rows.Count}필지 · 영구점용 {LandReport.R1(m.Rows.Sum(r => r.Occupied)):N1}㎡"
            + (m.NoLedger > 0 ? $" · 대장면적 없음 {m.NoLedger} → 도면 면적" : "")
            + (m.Clamped > 0 ? $" · 편입 > 지적 {m.Clamped} → 지적면적까지" : "")
            + (m.NoShape > 0 ? $" · 면 못 읽음 {m.NoShape}" : ""));
        if (!SelfCheckLog.Headless) try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true }); } catch { }
    }

    /// <summary>[지적도] 필지 선(DH_PARCEL 바깥 고리 · 구멍) → PNU마다 면 하나(조각 여럿이면 다중 면). 속성 = 도면에 적힌 값(지번 · 지목 · 소유 구분 · 대장면적 · 주소).</summary>
    static List<LandParcel> ReadParcels(Database db, Transaction tr, NetTopologySuite.Geometries.GeometryFactory gf)
    {
        var layers = new HashSet<string>(ImportGisCommand.ParcelLayers, System.StringComparer.OrdinalIgnoreCase);
        var by = new Dictionary<string, (ParcelXData.Info Info, SortedDictionary<int, (List<NetTopologySuite.Geometries.LinearRing> Shells, List<NetTopologySuite.Geometries.LinearRing> Holes)> Pieces)>(System.StringComparer.Ordinal);
        var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
        foreach (ObjectId id in ms)
        {
            if (!id.ObjectClass.IsDerivedFrom(Autodesk.AutoCAD.Runtime.RXObject.GetClass(typeof(Polyline)))) continue;
            if (tr.GetObject(id, OpenMode.ForRead) is not Polyline pl || !layers.Contains(pl.Layer)) continue;
            var x = ParcelXData.Read(pl);
            if (x == null || (x.Role != "outer" && x.Role != "hole") || pl.NumberOfVertices < 3) continue;
            var cs = new List<NetTopologySuite.Geometries.Coordinate>();
            for (int i = 0; i < pl.NumberOfVertices; i++) { var q = pl.GetPoint2dAt(i); cs.Add(new NetTopologySuite.Geometries.Coordinate(q.X, q.Y)); }
            if (!cs[0].Equals2D(cs[^1])) cs.Add(cs[0].Copy());
            if (cs.Count < 4) continue;
            NetTopologySuite.Geometries.LinearRing ring;
            try { ring = gf.CreateLinearRing(cs.ToArray()); } catch { continue; }
            string key = x.Pnu.Length > 0 ? x.Pnu : "#" + x.Jibun + "|" + x.Addr;
            if (!by.TryGetValue(key, out var e)) by[key] = e = (x, new());
            if (!e.Pieces.TryGetValue(x.Piece, out var pc)) e.Pieces[x.Piece] = pc = (new(), new());
            (x.Role == "outer" ? pc.Shells : pc.Holes).Add(ring);
        }
        var res = new List<LandParcel>();
        foreach (var (info, pieces) in by.Values)
        {
            var polys = new List<NetTopologySuite.Geometries.Polygon>();
            foreach (var (shells, holes) in pieces.Values)
                foreach (var sh in shells)
                {
                    var shPoly = gf.CreatePolygon(sh);
                    polys.Add(gf.CreatePolygon(sh, holes.Where(h => shells.Count == 1 || shPoly.Contains(h)).ToArray()));
                }
            res.Add(new LandParcel
            {
                Pnu = info.Pnu, Jibun = info.Jibun, Jimok = info.Jimok, Addr = info.Addr, Kind = info.Kind, CodeText = info.CodeText, Ledger = info.Ledger,
                Shape = polys.Count == 0 ? null : polys.Count == 1 ? polys[0] : gf.CreateMultiPolygon(polys.ToArray()),
            });
        }
        return res;
    }

    /// <summary>엑셀 쓰기 — ClosedXML 어셈블리는 이 메서드 안에서만 풀린다(못 불러와도 호출한 쪽은 산다).</summary>
    internal static string Save(QtyReportModel model, string xlsx) => Save(() => QtyReportXlsx.Write(model, xlsx));

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    static string Save(System.Action write)
    {
        try { write(); return ""; }
        catch (System.IO.FileNotFoundException ex) when (ex.FileName != null && !ex.FileName.EndsWith(".xlsx", System.StringComparison.OrdinalIgnoreCase)) { return "엑셀 라이브러리 못 불러옴(" + ex.FileName + ") → 설치 다시"; }
        catch (System.IO.FileLoadException ex) { return "엑셀 라이브러리 못 불러옴(" + ex.FileName + ") → 설치 다시"; }
        catch (System.TypeLoadException ex) { return "엑셀 라이브러리 못 불러옴(" + ex.TypeName + ") → 설치 다시"; }
        catch (System.BadImageFormatException ex) { return "엑셀 라이브러리 못 불러옴(" + ex.FileName + ") → 설치 다시"; }
        catch (System.IO.IOException ex) { return "파일을 못 씀 → 엑셀에서 닫고 다시 — " + ex.Message; }
        catch (System.UnauthorizedAccessException) { return "파일을 못 씀 → 엑셀에서 닫고 다시"; }
        catch (System.Exception ex) { try { DiagLog.Append("\n[수량산출서] " + ex); } catch { } return "만들기 실패(" + ex.GetType().Name + ") — " + ex.Message; }
    }

    internal static QtyReportModel BuildModel(Document doc, QtyXlsxInput inp, QtyKeeper.Snap? site, QtyKeeper.Snap? road, Editor ed)
    {
        var db = doc.Database;
        var m = new QtyReportModel { Facility = inp.Facility.Trim().Length > 0 ? inp.Facility.Trim() : "부지", DeepLimit = site?.Limit ?? inp.DeepLimit };
        if (site != null && System.Math.Abs(site.Limit - inp.DeepLimit) > 1e-9)
            m.Notes.Add($"횡단은 터파기 구분 H={site.Limit:0.0#}m로 만들어짐(설정 H={inp.DeepLimit:0.0#}m) → [횡단도] 다시");
        if (site != null) { m.Site = QtyKeeper.Stations(site, m.Notes); m.SiteKnown = true; }
        else m.Notes.Add("부지: 횡단 없음 → [횡단도] 먼저(토적표 빈 표)");
        if (road != null) { m.Road = QtyKeeper.Stations(road, m.Notes); m.RoadKnown = true; if (System.Math.Abs(road.Limit - m.DeepLimit) > 1e-9) m.Notes.Add($"진입도로 횡단은 H={road.Limit:0.0#}m 기준"); }
        // U형 측구 — 규격별 묶음
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            if (UDitchStore.TryLoad(db, tr, out var rec, out _))
            {
                var s = UDitchStore.LoadSettings(out _, out _);
                m.DitchCover = s.Cover;
                UDitch.Link(rec, s);   // ★[JACK 1123] 합류 · 교차 · 출구 집수정 자리를 뺀 U 길이
                foreach (var d in rec.Ditches)
                {
                    double len;
                    try { len = UDitch.Qty(d, s).Length; } catch { continue; }
                    if (!double.IsFinite(len) || len <= 0) continue;
                    var sz = d.Size;
                    var g = m.Ditches.FirstOrDefault(x => x.Label == sz.Label && System.Math.Abs(x.B - sz.B) < 1e-6 && System.Math.Abs(x.H - sz.H) < 1e-6 && System.Math.Abs(x.Tw - sz.Tw) < 1e-6 && System.Math.Abs(x.Tb - sz.Tb) < 1e-6 && System.Math.Abs(x.Tf - sz.Tf) < 1e-6 && System.Math.Abs(x.Pf - sz.Pf) < 1e-6);
                    if (g == null) m.Ditches.Add(g = new QtyDitch { Label = sz.Label, B = sz.B, H = sz.H, Tw = sz.Tw, Tb = sz.Tb, Tf = sz.Tf, Pf = sz.Pf });
                    g.Length += len; g.Count++;
                }
                // ★[JACK 1123 · 검토 M3 · JACK 1124] 집수정 — 보고서 · 평면 블록과 같은 목록(규격별)
                var bl = UDitch.BasinList(rec, s);
                m.BasinCount = bl.Count; m.BasinVol = bl.Sum(b => b.Volume);
                m.Basins = UDitch.BasinGroups(bl).Select(g => new QtyBasin { Label = g.Label, Kinds = g.Kinds, Count = g.Count, Volume = g.Volume, DepthKnown = g.DepthKnown }).ToList();
                // ★[검토 1124 #1] 평면 블록은 [측구 생성] · 기록 고침마다 맞춤 — 그래도 다르면(손으로 지움 · 맞춤 실패) 알림
                if (rec.Updated.Length > 0 && UDitchStore.CountBasinBlocks(db, tr) is int nb && nb != bl.Count(UDitchStore.Placeable))
                    m.Notes.Add($"집수정 블록 갱신 필요(평면 블록 {nb} · 수량 {bl.Count}개소) → [측구 생성]");
            }
            tr.Commit();
        }
        catch (System.Exception ex) { m.Notes.Add("수로관: 측구 기록 못 읽음 — " + ex.Message); }
        // 벌개제근 · 임상 — 장부(없으면 한 번 잼)
        var f = m.Forest; f.Density = inp.Density; f.Dbh = inp.Dbh; f.Root = inp.Root; f.KRoot = inp.KRoot; f.F = inp.F; f.W0 = inp.W0; f.P = inp.P; f.KForm = inp.KForm; f.Surcharge = inp.Surcharge;
        ClearingResult? cr = ClearingQuantity.LastOf(db);
        if (cr == null)
        {
            ed.WriteMessage("\n[수량산출서] 벌개제근 재는 중...");
            try { cr = ClearingQuantity.Measure(db, new System.Text.StringBuilder(), new List<string>()); } catch (System.Exception ex) { m.Notes.Add("벌개제근: 못 잼 — " + ex.Message); }
        }
        if (cr != null && cr.Known)
        {
            m.Clearing = cr.Total; f.Known = true; f.ByFrtp = (double[])cr.ByFrtp.Clone();
            if (double.IsFinite(inp.Height)) f.Height = inp.Height;
            else if (double.IsFinite(cr.MeanHeight)) { f.Height = cr.MeanHeight; f.HeightAuto = true; }
        }
        else if (cr != null) m.Notes.Add("벌개제근: " + cr.Note);
        if (!f.Known && double.IsFinite(inp.Height)) f.Height = inp.Height;
        return m;
    }
}
