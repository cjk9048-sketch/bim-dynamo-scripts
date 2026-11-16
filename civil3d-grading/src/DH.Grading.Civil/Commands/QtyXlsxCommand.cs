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
        if (!SelfCheckLog.Headless && System.IO.File.Exists(xlsx) && MessageBox.Show("같은 이름 파일이 있음 → 덮어쓰기?\n" + System.IO.Path.GetFileName(xlsx), "수량산출서", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

        QtyReportModel model;
        try { model = BuildModel(doc, inp, siteSnap, roadSnap, ed); }
        catch (System.Exception ex) { ed.WriteMessage("\n[수량산출서] 자료를 못 모음 — " + ex.Message); try { DiagLog.Append("\n[수량산출서] " + ex); } catch { } return; }

        string err = Save(model, xlsx);
        if (err.Length > 0) { ed.WriteMessage("\n[수량산출서] " + err); return; }
        ed.WriteMessage("\n[수량산출서] 저장: " + xlsx);
        if (!SelfCheckLog.Headless) try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(xlsx) { UseShellExecute = true }); } catch { }
    }

    /// <summary>엑셀 쓰기 — ClosedXML 어셈블리는 이 메서드 안에서만 풀린다(못 불러와도 호출한 쪽은 산다).</summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    internal static string Save(QtyReportModel model, string xlsx)
    {
        try { QtyReportXlsx.Write(model, xlsx); return ""; }
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
